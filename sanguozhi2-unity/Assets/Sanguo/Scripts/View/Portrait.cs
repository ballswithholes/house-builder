// ==========================================================================
// 三国志II 霸王的大陆 · 武将头像（第二版 DESIGN-V2 §4B；网页版 js/portrait.js 的移植）
//
// 原作（FC《霸王的大陆》）头像的现代化：深色背景、四分之三侧面胸像、粗轮廓、有限色板 →
// 代码合成的矢量图（赛璐璐分层阴影、轮廓光、发须与甲胄纹样），统一的势力色边框。
// 全部在代码里生成，不载入任何外部资源。
//
// 渲染管线与网页版相同：外貌参数 → SVG 标记字符串（与 JS 逐字相同，测试里逐条比对）→
// 自带的 SVG 子集解释器 → View/Raster.cs 的矢量栅格器 → Texture2D。
//
// 公开接口（Unity）
//   Portrait.Texture(General g, int size, Color factionColor, string mood = "neutral", bool frame = true, bool flip = false) → Texture2D（缓存）
//   Portrait.Sprite(...)                     → UGUI Image 用的 Sprite（同一缓存）
//   Portrait.PreloadAsync(gens, size, colorOf) → 协程：后台线程栅格化、主线程按时间片上传纹理
//   Portrait.Describe                        → General → PortraitGen（文化 / 生年 / 性别的接入点，见 INTEGRATION）
//   Portrait.Year                            → 当前年份（画像年龄 = 当前年份 − 生年）
// 公开接口（纯 C#，后台线程可用）
//   Portrait.Spec(gen) / Svg(gen, opts) / Render(gen, opts) → 外貌参数 / SVG 源码 / Raster
//   Portrait.Cultures                        → 全部 15 种文化代号
//   PortraitDataStore.Add(name, JObj)        → 追加 / 覆盖外貌设定（对应 SG.PortraitData.add）
//
// 画面约定：视窗 256×256 单位；脸朝画面左侧（四分之三侧面），主光来自左上前方，
// 画面右侧为背光面，右缘有轮廓光。flip = true 时人物水平镜像（边框不变）。
// ==========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
#if !SANGUO_HEADLESS
using UnityEngine;
#endif

namespace Sanguo
{
    // JS 对象的替身：保持插入顺序（只增改、不删除）的字典。
    // 值：string / double / bool / null / JObj / List<object>
    public sealed class JObj : Dictionary<string, object>
    {
        public JObj() { }
        public JObj(JObj src) { if (src != null) foreach (var kv in src) this[kv.Key] = kv.Value; }
        public object G(string k) { object v; return TryGetValue(k, out v) ? v : null; }
        public string S(string k) { return G(k) as string; }
        public JObj O(string k) { return G(k) as JObj; }
        public List<object> L(string k) { return G(k) as List<object>; }
        // 数值（缺失或非数值为 NaN）
        public double D(string k) { object v = G(k); return v is double ? (double)v : v is int ? (int)v : double.NaN; }
        // JS 的 `o.k || def`（0 / NaN / 缺失 → def）
        public double Or(string k, double def) { double v = D(k); return double.IsNaN(v) || v == 0 ? def : v; }
        // JS 的 `o.k || def`（字符串）；值为非空字符串时返回它，否则 def
        public string Or(string k, string def) { var s = S(k); return string.IsNullOrEmpty(s) ? def : s; }
        public bool Has(string k) { return G(k) != null; }          // JS `o.k != null`
        public bool T(string k) { return Js.Truthy(G(k)); }         // JS 真值
        public bool IsFalse(string k) { object v = G(k); return v is bool && !(bool)v; }   // JS `o.k === false`
        public JObj Clone() { return new JObj(this); }
        public JObj Set(string k, object v) { this[k] = v; return this; }
        // Object.assign({}, a, b, …)
        public static JObj Assign(params JObj[] parts)
        {
            var o = new JObj();
            foreach (var p in parts) if (p != null) foreach (var kv in p) o[kv.Key] = kv.Value;
            return o;
        }
        public JObj Hat { get { return O("hat"); } }
        public JObj Outfit { get { return O("outfit"); } }
        public JObj Hair { get { return O("hair"); } }
        public JObj Beard { get { return O("beard"); } }
        public JObj Eyes { get { return O("eyes"); } }
        public JObj Brows { get { return O("brows"); } }
        public JObj Nose { get { return O("nose"); } }
        public JObj Mouth { get { return O("mouth"); } }
        public JObj Face { get { return O("face"); } }
        public bool Includes(string listKey, string item)
        {
            var l = L(listKey);
            if (l == null) return false;
            foreach (var x in l) if (x as string == item) return true;
            return false;
        }

        // 极简 JSON 解析（导出工具生成的数据用）
        public static object ParseJson(string s) { int i = 0; return JVal(s, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object JVal(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var o = new JObj(); i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = (string)JVal(s, ref i);
                    Ws(s, ref i); i++; // ':'
                    o[k] = JVal(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return o;     // '}'
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(JVal(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;     // ']'
                }
            }
            if (c == '"')
            {
                var sb = new StringBuilder(); i++;
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        if (e == 'n') sb.Append('\n'); else if (e == 't') sb.Append('\t'); else if (e == 'r') sb.Append('\r');
                        else if (e == 'b') sb.Append('\b'); else if (e == 'f') sb.Append('\f');
                        else if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e);
                        i++;
                    }
                    else sb.Append(s[i++]);
                }
                i++;
                return sb.ToString();
            }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        // 规范 JSON（键按插入顺序，数值按 JS 格式）：数据校验哈希用
        public static string ToJson(object v)
        {
            var sb = new StringBuilder(); WriteJson(sb, v); return sb.ToString();
        }
        static void WriteJson(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double) { sb.Append(Js.Num((double)v)); return; }
            if (v is string)
            {
                sb.Append('"');
                foreach (char c in (string)v)
                {
                    if (c == '"') sb.Append("\\\""); else if (c == '\\') sb.Append("\\\\");
                    else if (c == '\n') sb.Append("\\n"); else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                }
                sb.Append('"'); return;
            }
            var o = v as JObj;
            if (o != null)
            {
                sb.Append('{'); bool first = true;
                foreach (var kv in o) { if (!first) sb.Append(','); first = false; WriteJson(sb, kv.Key); sb.Append(':'); WriteJson(sb, kv.Value); }
                sb.Append('}'); return;
            }
            var l = v as List<object>;
            if (l != null)
            {
                sb.Append('['); for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); WriteJson(sb, l[i]); }
                sb.Append(']'); return;
            }
            sb.Append("null");
        }
    }

    // JS 语义的小工具
    public static class Js
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        public static bool Truthy(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            if (v is double) { double d = (double)v; return d != 0 && !double.IsNaN(d); }
            if (v is int) return (int)v != 0;
            if (v is string) return ((string)v).Length > 0;
            return true;
        }
        // JS 的 Number → 字符串（模板字符串里直接插入数值时）
        public static string Num(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CI);
            return v.ToString("R", CI);
        }
        public static double Round(double v) { return Math.Floor(v + 0.5); }   // Math.round
        public static string Or(string a, string b) { return string.IsNullOrEmpty(a) ? b : a; }
        // String(v)（模板字符串里插入任意值）；null → null
        public static string Str(object v)
        {
            if (v == null) return null;
            if (v is string) return (string)v;
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double) return Num((double)v);
            return v.ToString();
        }
    }

    // 武将的头像输入（对应 JS 的 gen：至少 name / war / intel / pol，可选 culture / born / sex / portrait）
    public sealed class PortraitGen
    {
        public string name;
        public double? war, intel, pol, born;
        public string culture, sex;
        public JObj portrait;           // 无 PortraitData 条目时使用的外貌参数
        public string color;            // 势力色（'#rrggbb'），opts.color 缺省时使用
    }

    // 绘制选项（对应 JS 的 normOpts 结果）
    public sealed class PortraitOpts
    {
        public int size = 96, px = 96;
        public string color = "#8a8a92";
        public int lod = 1;
        public bool frame = true, flip;
        public string mood = "neutral";
        public double lw = 2.9;
        // size：边长（像素）；scale：像素倍率（JS 的 devicePixelRatio，缺省 1）
        public static PortraitOpts Make(int size = 96, string color = null, string mood = "neutral", bool frame = true, bool flip = false, double scale = 1, PortraitGen gen = null)
        {
            var o = new PortraitOpts();
            o.size = Math.Max(16, (int)Js.Round(size <= 0 ? 96 : size));
            o.px = Math.Max(16, (int)Js.Round(o.size * (scale > 0 ? scale : 1)));
            o.color = Portrait.NormColor(color ?? (gen != null && !string.IsNullOrEmpty(gen.color) ? gen.color : "#8a8a92"), "#8a8a92");
            o.lod = o.px < 90 ? 0 : o.px < 170 ? 1 : 2;
            o.frame = frame; o.flip = flip; o.mood = string.IsNullOrEmpty(mood) ? "neutral" : mood;
            o.lw = o.lod == 0 ? 3.4 : o.lod == 1 ? 2.9 : 2.6;
            return o;
        }
    }

    // 外貌设定数据（对应 SG.PortraitData）：生成数据 PortraitData.g.cs + 运行时追加
    public static class PortraitDataStore
    {
        static Dictionary<string, JObj> entries;
        static readonly object lk = new object();
        static void Load()
        {
            if (entries != null) return;
            var d = new Dictionary<string, JObj>();
            var src = PortraitData.Entries;
            for (int i = 0; i + 1 < src.Length; i += 2) d[src[i]] = (JObj)JObj.ParseJson(src[i + 1]);
            entries = d;
        }
        public static JObj Get(string name)
        {
            lock (lk) { Load(); JObj e; return name != null && entries.TryGetValue(name, out e) ? e : null; }
        }
        // 合并同名旧条目并清空头像缓存（同 JS 的 add）
        public static void Add(string name, JObj entry)
        {
            lock (lk)
            {
                Load();
                JObj old;
                entries[name] = entries.TryGetValue(name, out old) ? JObj.Assign(old, entry) : new JObj(entry);
            }
            Portrait.ClearCache();
        }
        public static ICollection<string> Names() { lock (lk) { Load(); return new List<string>(entries.Keys); } }
        public static string[] WorldHand { get { return PortraitData.WorldHand; } }
    }

    public static partial class Portrait
    {
        // =============================================================== 工具 ==
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        internal static uint HashStr(string s)
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            h ^= h >> 16; h *= 0x85ebca6b; h ^= h >> 13; h *= 0xc2b2ae35; h ^= h >> 16;
            return h;
        }
        // 确定性随机数（mulberry32）。每个“方面”用独立子流，互不干扰
        internal sealed class Rng
        {
            uint a;
            public Rng(uint seed) { a = seed; }
            public double f()
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1 | a);
                t = (t + ((t ^ (t >> 7)) * (61 | t))) ^ t;
                return (double)(t ^ (t >> 14)) / 4294967296.0;
            }
            public double range(double a0, double b0) { return a0 + (b0 - a0) * f(); }
            public int @int(int a0, int b0) { return a0 + (int)Math.Floor(f() * (b0 - a0 + 1)); }
            public bool chance(double p) { return f() < p; }
            public string pick(string[] arr) { return arr[(int)Math.Floor(f() * arr.Length)]; }
            public string wpick(WL list)
            {
                double tot = 0;
                foreach (var w in list.w) tot += w;
                double x = f() * tot;
                for (int i = 0; i < list.k.Length; i++) { x -= list.w[i]; if (x < 0) return list.k[i]; }
                return list.k[list.k.Length - 1];
            }
        }
        // 加权候选表
        internal sealed class WL
        {
            public string[] k; public double[] w;
            public WL(params object[] a)
            {
                k = new string[a.Length / 2]; w = new double[a.Length / 2];
                for (int i = 0; i < k.Length; i++) { k[i] = (string)a[i * 2]; w[i] = Convert.ToDouble(a[i * 2 + 1], CI); }
            }
        }
        static Rng Sub(string name, string tag) { return new Rng(HashStr(name + "\u0001" + tag)); }
        static double clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double hyp(double x, double y) { return Math.Sqrt(x * x + y * y); }
        static double smooth01(double t) { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); }
        static readonly double PI = Math.PI;

        // ----------------------------------------------------------- 颜色 --
        static readonly Dictionary<string, int[]> rgbCache = new Dictionary<string, int[]>();
        static int[] rgb(string c)
        {
            int[] v;
            lock (rgbCache) { if (rgbCache.TryGetValue(c, out v)) return v; }
            string h = c.Trim();
            if (h.Length > 0 && h[0] == '#') h = h.Substring(1);
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            int n = ParseHexPrefix(h.Length > 6 ? h.Substring(0, 6) : h);
            v = new[] { (n >> 16) & 255, (n >> 8) & 255, n & 255 };
            lock (rgbCache) rgbCache[c] = v;
            return v;
        }
        // parseInt(h, 16) || 0：读到第一个非十六进制字符为止
        static int ParseHexPrefix(string h)
        {
            int n = 0, i = 0;
            for (; i < h.Length; i++)
            {
                char ch = h[i]; int d;
                if (ch >= '0' && ch <= '9') d = ch - '0'; else if (ch >= 'a' && ch <= 'f') d = ch - 'a' + 10; else if (ch >= 'A' && ch <= 'F') d = ch - 'A' + 10; else break;
                n = n * 16 + d;
            }
            return n;
        }
        static int c8(double v) { return v < 0 ? 0 : v > 255 ? 255 : (int)Js.Round(v); }
        static string hex(double r, double g, double b) { return "#" + ((1 << 24) | (c8(r) << 16) | (c8(g) << 8) | c8(b)).ToString("x", CI).Substring(1); }
        static string mix(string a, string b, double t)
        {
            var x = rgb(a); var y = rgb(b);
            return hex(x[0] + (y[0] - x[0]) * t, x[1] + (y[1] - x[1]) * t, x[2] + (y[2] - x[2]) * t);
        }
        static string mul(string a, double k) { var x = rgb(a); return hex(x[0] * k, x[1] * k, x[2] * k); }
        static double lum(string c) { var x = rgb(c); return (0.299 * x[0] + 0.587 * x[1] + 0.114 * x[2]) / 255; }
        // '#hex' / 'rgb()' → '#rrggbb'
        public static string NormColor(string c, string fallback)
        {
            if (string.IsNullOrEmpty(c)) return fallback;
            if (c[0] == '#') { var x = rgb(c); return hex(x[0], x[1], x[2]); }
            int i0 = c.IndexOf("rgb", StringComparison.Ordinal);
            if (i0 >= 0)
            {
                int a = c.IndexOf('(', i0), b = c.IndexOf(')', i0);
                if (a > 0 && b > a)
                {
                    var p = c.Substring(a + 1, b - a - 1).Split(',');
                    double[] v = new double[3];
                    for (int i = 0; i < 3 && i < p.Length; i++) double.TryParse(p[i].Trim(), NumberStyles.Float, CI, out v[i]);
                    return hex(v[0], v[1], v[2]);
                }
            }
            return fallback;
        }
        // 材质色阶：b 基色 s 阴影 d 暗部 h 高光（肤色另有 blush / line / lip）
        internal sealed class Tone { public string b, s, d, h, blush, line, lip; }
        static Tone skinT(string b)
        {
            return new Tone
            {
                b = b, s = mix(mul(b, 0.83), "#9a3a5a", 0.16), d = mix(mul(b, 0.62), "#4a1830", 0.24),
                h = mix(b, "#fff4e6", 0.45), blush = mix(b, "#e2525e", 0.42), line = mix(mul(b, 0.42), "#2a0c18", 0.45),
                lip = mix(mul(b, 0.86), "#b04454", 0.22),
            };
        }
        static Tone clothT(string b) { return new Tone { b = b, s = mix(mul(b, 0.68), "#221a40", 0.2), d = mix(mul(b, 0.44), "#0c0818", 0.25), h = mix(b, "#fffaf0", 0.3) }; }
        static Tone metalT(string b) { return new Tone { b = b, s = mix(mul(b, 0.6), "#1c2030", 0.15), d = mul(b, 0.36), h = mix(b, "#ffffff", 0.62) }; }
        static Tone hairT(string b)
        {
            double l = lum(b);
            return new Tone { b = b, s = mul(b, 0.6), d = mul(mix(b, "#000", 0.25), 0.5), h = l < 0.18 ? mix(b, "#8696b8", 0.34) : mix(b, "#fff6e0", 0.38) };
        }
        const string INK = "#150e12";
        const string GOLD = "#e2b45a";

        // ----------------------------------------------------------- 路径 --
        // 点：double[3]，第三分量为样条张力 k（NaN = 未给出，按 1）
        static readonly double U = double.NaN;
        static double[] V(double x, double y) { return new[] { x, y, U }; }
        static double[] V(double x, double y, double k) { return new[] { x, y, k }; }
        static List<double[]> Pts(params double[][] a) { return new List<double[]>(a); }
        static List<double[]> Cat(params List<double[]>[] a)
        {
            var o = new List<double[]>();
            foreach (var l in a) o.AddRange(l);
            return o;
        }
        static List<double[]> Cat(List<double[]> a, params double[][] b) { var o = new List<double[]>(a); o.AddRange(b); return o; }
        // Array.prototype.slice（支持负下标）
        static List<double[]> Slice(List<double[]> a, int s, int? e = null)
        {
            int n = a.Count;
            int st = s < 0 ? Math.Max(0, n + s) : Math.Min(s, n);
            int en = e == null ? n : e.Value < 0 ? Math.Max(0, n + e.Value) : Math.Min(e.Value, n);
            var o = new List<double[]>();
            for (int i = st; i < en; i++) o.Add(a[i]);
            return o;
        }
        static List<double[]> Rev(List<double[]> a) { var o = new List<double[]>(a); o.Reverse(); return o; }

        // 数字 → 一位小数的字符串（同 JS 的 N）
        static string Nn(long n) { long i = n / 10, f = n - i * 10; return f != 0 ? i.ToString(CI) + "." + f.ToString(CI) : i.ToString(CI); }
        static string N(double v)
        {
            if (double.IsNaN(v)) return "0";
            long n = (long)Math.Floor(v * 10 + 0.5);
            return n < 0 ? "-" + Nn(-n) : Nn(n);
        }
        static string ps(double[] p) { return N(p[0]) + "," + N(p[1]); }
        static string ps(double x, double y) { return N(x) + "," + N(y); }
        // Catmull-Rom → 三次贝塞尔。点的第三分量 k（0 = 尖角，1 = 圆滑）
        static string spline(List<double[]> pts, bool closed = false)
        {
            int n = pts.Count;
            if (n < 2) return "";
            var d = new StringBuilder("M").Append(ps(pts[0]));
            if (n == 2) return d.Append('L').Append(ps(pts[1])).Append(closed ? "Z" : "").ToString();
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                double[] p1 = pts[i], p2 = pts[(i + 1) % n];
                double[] p0 = closed ? pts[(i - 1 + n) % n] : pts[Math.Max(0, i - 1)];
                double[] p3 = closed ? pts[(i + 2) % n] : pts[Math.Min(n - 1, i + 2)];
                double k1 = (double.IsNaN(p1[2]) ? 1 : p1[2]) / 6, k2 = (double.IsNaN(p2[2]) ? 1 : p2[2]) / 6;
                // 控制柄长度不超过本段长度的一半，避免短段旁的长切线造成打圈
                double seg = hyp(p2[0] - p1[0], p2[1] - p1[1]) * 0.5;
                double ax = (p2[0] - p0[0]) * k1, ay = (p2[1] - p0[1]) * k1, bx = (p3[0] - p1[0]) * k2, by = (p3[1] - p1[1]) * k2;
                double la = hyp(ax, ay), lb = hyp(bx, by);
                if (la > seg) { ax *= seg / la; ay *= seg / la; }
                if (lb > seg) { bx *= seg / lb; by *= seg / lb; }
                d.Append('C').Append(ps(p1[0] + ax, p1[1] + ay)).Append(' ').Append(ps(p2[0] - bx, p2[1] - by)).Append(' ').Append(ps(p2));
            }
            return closed ? d.Append('Z').ToString() : d.ToString();
        }
        static string poly(List<double[]> pts, bool closed = true)
        {
            var d = new StringBuilder("M");
            for (int i = 0; i < pts.Count; i++) { if (i > 0) d.Append('L'); d.Append(ps(pts[i])); }
            if (closed) d.Append('Z');
            return d.ToString();
        }
        static List<double[]> shift(List<double[]> pts, double dx, double dy)
        {
            var o = new List<double[]>(pts.Count);
            foreach (var p in pts) o.Add(new[] { p[0] + dx, p[1] + dy, p[2] });
            return o;
        }
        static double[] lp(double[] a, double[] b, double t) { return V(a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t); }
        static double[] add(double[] a, double dx, double dy) { return V(a[0] + dx, a[1] + dy); }
        // 变宽笔触：中心线 pts，宽度函数 w(t)。返回闭合路径
        static string taper(List<double[]> pts, Func<double, double> wf) { return taperImpl(pts, wf, null, double.NaN); }
        static string taper(List<double[]> pts, double w) { return taperImpl(pts, null, null, w); }
        static string taper(List<double[]> pts, double[] ws) { return taperImpl(pts, null, ws, double.NaN); }
        static string taperImpl(List<double[]> pts, Func<double, double> wf, double[] wa, double wn)
        {
            int n = pts.Count;
            var Lp = new List<double[]>(); var Rr = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double[] a = pts[Math.Max(0, i - 1)], b = pts[Math.Min(n - 1, i + 1)];
                double dx = b[0] - a[0], dy = b[1] - a[1];
                double l = hyp(dx, dy); if (l == 0 || double.IsNaN(l)) l = 1; dx /= l; dy /= l;
                double w = (wf != null ? wf(n == 1 ? 0 : (double)i / (n - 1)) : wa == null ? wn : wa[i]) / 2;
                Lp.Add(V(pts[i][0] - dy * w, pts[i][1] + dx * w));
                Rr.Add(V(pts[i][0] + dy * w, pts[i][1] - dx * w));
            }
            double w0 = wf != null ? wf(0) : wa == null ? wn : wa[0];
            double w1 = wf != null ? wf(1) : wa == null ? wn : wa[n - 1];
            if (w1 < 0.08) { Rr.RemoveAt(Rr.Count - 1); Lp[n - 1][2] = 0.4; }
            Rr.Reverse();
            if (w0 < 0.08) { Rr.RemoveAt(Rr.Count - 1); Lp[0][2] = 0.4; }
            return spline(Cat(Lp, Rr), true);
        }
        // 叶形发绺 / 须绺：根 a、尖 b、根宽 w、弯曲 bend（相对长度的侧偏）
        static string leaf(double[] a, double[] b, double w, double bend)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy); if (l == 0 || double.IsNaN(l)) l = 1;
            double nx = -dy / l, ny = dx / l;
            double cx = (a[0] + b[0]) / 2 + nx * bend * l, cy = (a[1] + b[1]) / 2 + ny * bend * l;
            double h = w / 2;
            return "M" + ps(a[0] + nx * h, a[1] + ny * h) + "Q" + ps(cx + nx * h * 0.7, cy + ny * h * 0.7) + " " + ps(b) +
                "Q" + ps(cx - nx * h * 0.7, cy - ny * h * 0.7) + " " + ps(a[0] - nx * h, a[1] - ny * h) + "Z";
        }
        // 弯曲的发绺 / 须绺（毛笔笔触）：根宽 w，尖端收细，bend 为侧弯
        static string @lock(double[] a, double[] b, double w, double bend)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy); if (l == 0 || double.IsNaN(l)) l = 1;
            double nx = -dy / l, ny = dx / l;
            var m1 = V(a[0] + dx * 0.4 + nx * bend * l, a[1] + dy * 0.4 + ny * bend * l);
            var m2 = V(a[0] + dx * 0.75 + nx * bend * l * 0.6, a[1] + dy * 0.75 + ny * bend * l * 0.6);
            return taper(Pts(a, m1, m2, b), t => w * Math.Pow(1 - t, 0.8));
        }
        // 椭圆弧上的点（数学角度，y 向上为正）
        static List<double[]> arcPts(double cx, double cy, double rx, double ry, double a0, double a1, int n, double k = double.NaN)
        {
            var o = new List<double[]>();
            for (int i = 0; i <= n; i++)
            {
                double a = a0 + (a1 - a0) * i / n;
                o.Add(V(cx + rx * Math.Cos(a), cy - ry * Math.Sin(a), k));
            }
            return o;
        }
        static string ellipse(double cx, double cy, double rx, double ry, double rot = 0)
        {
            return "<ellipse cx=\"" + N(cx) + "\" cy=\"" + N(cy) + "\" rx=\"" + N(rx) + "\" ry=\"" + N(ry) + "\"" +
                (rot != 0 && !double.IsNaN(rot) ? " transform=\"rotate(" + N(rot) + " " + N(cx) + " " + N(cy) + ")\"" : "");
        }
        static string circle(double cx, double cy, double r) { return "<circle cx=\"" + N(cx) + "\" cy=\"" + N(cy) + "\" r=\"" + N(r) + "\""; }

        // ======================================================== SVG 拼装器 ==
        static int uidSeq;
        static readonly string[] LAYERS = { "back", "collarBack", "neck", "body", "body2", "head", "ear", "face", "beard", "hair", "hat", "front", "top" };
        internal sealed class Svg
        {
            public PortraitOpts o;
            public string uid;
            public List<string> defs = new List<string>();
            public Dictionary<string, List<string>> L = new Dictionary<string, List<string>>();
            public List<string> headShade = new List<string>();
            public double lw;
            public string bg, frame;
            public int win;
            public Svg(PortraitOpts o)
            {
                this.o = o;
                int seq = Interlocked.Increment(ref uidSeq) - 1;
                uid = "q" + Base36(seq);
                foreach (var k in LAYERS) L[k] = new List<string>();
                lw = o.lw;
            }
            static string Base36(int v)
            {
                if (v == 0) return "0";
                var sb = new StringBuilder();
                while (v > 0) { int d = v % 36; sb.Insert(0, (char)(d < 10 ? '0' + d : 'a' + d - 10)); v /= 36; }
                return sb.ToString();
            }
            public string id(string n) { return uid + n; }
            public string clip(string n, string d) { defs.Add("<clipPath id=\"" + id(n) + "\"><path d=\"" + d + "\"/></clipPath>"); return "url(#" + id(n) + ")"; }
            public void add(string layer, string s) { L[layer].Add(s); }
            // 填充 + 可选描边
            public void path(string layer, string d, string fill, double sw = 0, string extra = null)
            {
                L[layer].Add("<path d=\"" + d + "\" fill=\"" + fill + "\"" + (sw != 0 && !double.IsNaN(sw) ? " stroke=\"" + INK + "\" stroke-width=\"" + N(sw) + "\"" : "") + (extra ?? "") + "/>");
            }
            public void line(string layer, string d, string color, double sw, string extra = null)
            {
                L[layer].Add("<path d=\"" + d + "\" fill=\"none\" stroke=\"" + color + "\" stroke-width=\"" + N(sw) + "\"" + (extra ?? "") + "/>");
            }
            // 带阴影的形状：填充 → 裁剪内阴影 → 轮廓（sw：NaN = 缺省线宽，0 = 不描边）
            public void shape(string layer, string d, string fill, Action shadeFn, double sw = double.NaN, string clipName = null, double fillOp = 0)
            {
                L[layer].Add("<path d=\"" + d + "\" fill=\"" + fill + "\"" + (fillOp != 0 ? " opacity=\"" + Js.Num(fillOp) + "\"" : "") + "/>");
                if (shadeFn != null)
                {
                    string cid = !string.IsNullOrEmpty(clipName) ? clipName : "c" + defs.Count;
                    L[layer].Add("<g clip-path=\"" + clip(cid, d) + "\">");
                    shadeFn();
                    L[layer].Add("</g>");
                }
                if (sw != 0) L[layer].Add("<path d=\"" + d + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(double.IsNaN(sw) ? lw : sw) + "\"/>");
            }
        }
        // 发绺项
        internal struct Item { public string d, fill; public string op; public Item(string d, string fill, string op = null) { this.d = d; this.fill = fill; this.op = op; } }
        static Item It(string d, string fill) { return new Item(d, fill); }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;

    public static partial class Portrait
    {
        // ======================================================== 文化外观 ==
        // 每种文化：肤色范围、发色、眼色、眼睑（lid：0 深双眼皮 … 1 单眼皮）、鼻梁高度、
        // 发型 / 冠帽 / 衣甲 的加权候选（按角色）、胡须候选、配色、标记与配饰的概率。
        // 角色（role）：warrior 武将 · general 智勇兼备 · strategist 谋士 · official 文官 ·
        //               ruler 君主 · commoner 小吏 / 草莽 · female 女性
        internal sealed class Cult
        {
            public string name; public string[] skins; public double warSkin, ruddy, freckle;
            public WL hairC; public string[] eyeC; public double lid, noseBridge, noseW; public WL noses;
            public Dictionary<string, WL> hair, hat, outfit;
            public string[] cloth, metal; public double cross;
            public string beard;            // 'han' = 汉式胡须规则
            public WL beardsYoung, beardsAdult, musts, marks, acc;
            public bool mustWithBeard, mustAlways;
        }
        static Dictionary<string, WL> RM(params object[] a)
        {
            var d = new Dictionary<string, WL>();
            for (int i = 0; i + 1 < a.Length; i += 2) d[(string)a[i]] = (WL)a[i + 1];
            return d;
        }
        static WL W(params object[] a) { return new WL(a); }
        static readonly WL DARK = W("#16131a", 6, "#221a18", 3, "#2c2019", 1);
        static readonly string[] BROWNEYE = { "#4a2c1a", "#56351e", "#3c2414", "#4e3420" };
        static readonly string[] LIGHTEYE = { "#3a5a7a", "#4a6a8a", "#5a7a5a", "#6a6a5a", "#4a3a2a", "#3a2a1a" };
        // 插入顺序即 Portrait.Cultures 的顺序（同 JS 的 Object.keys(CULT)）
        static readonly string[] CULT_ORDER = { "han", "nanman", "wa", "yi", "korea", "steppe", "seasia", "tarim", "kushan", "persia", "arab", "roman", "celt", "german", "sarmatian" };
        static readonly Dictionary<string, Cult> CULT = new Dictionary<string, Cult>
        {
            { "han", new Cult {
                name = "汉", skins = new[] { "#f0caa6", "#eac09a", "#e2b48c", "#d8a77f", "#cb9a72" }, warSkin = 0.25,
                hairC = DARK, eyeC = BROWNEYE, lid = 0.55, noseBridge = 0.45, noseW = 1.0,
                hair = RM("m", W("topknot", 1), "f", W("bun", 3, "twinbun", 1)),
                hat = RM(
                    "warrior", W("helmet", 5, "ze", 3, "headband", 1, "turban", 1, "wuguan", 0.6),
                    "general", W("helmet", 4, "ze", 2, "wuguan", 1, "crown", 1),
                    "strategist", W("jinxian", 3, "lunjin", 1, "ze", 2, "futou", 2),
                    "official", W("jinxian", 6, "ze", 2, "futou", 1),
                    "ruler", W("crown", 4, "jinxian", 2, "helmet", 2),
                    "commoner", W("turban", 3, "ze", 3, "headband", 2, "futou", 1, "none", 1),
                    "female", W("none", 1)),
                outfit = RM(
                    "warrior", W("lamellar", 5, "plate", 2, "robearmor", 1), "general", W("plate", 3, "robearmor", 2, "lamellar", 2),
                    "strategist", W("robe", 1), "official", W("robe", 1), "ruler", W("robe", 2, "plate", 1),
                    "commoner", W("robe", 2, "lamellar", 2), "female", W("robe", 1)),
                cloth = new[] { "#2c4a7a", "#7a2a2a", "#3a5a3a", "#5a3a6a", "#7a5a2a", "#2a3a4a", "#6a2a3a", "#3a3a3a", "#8a6a3a", "#2a5a5a", "#4a3a2a", "#1e3a5a" },
                metal = new[] { "#7a808c", "#8a7048", "#5a5e68", "#a08048", "#3c3c44", "#7a3028", "#9aa0aa" },
                beard = "han" } },
            { "nanman", new Cult {
                name = "南蛮", skins = new[] { "#c48c62", "#b98058", "#a8714c", "#986444", "#8a5a3c" }, warSkin = 0.2,
                hairC = W("#141012", 1), eyeC = BROWNEYE, lid = 0.45, noseBridge = 0.25, noseW = 1.2,
                noses = W("broad", 3, "flat", 2, "straight", 1),
                hair = RM("m", W("mallet", 3, "wild", 2, "long", 1), "f", W("mallet", 1, "braids", 1)),
                hat = RM("warrior", W("feathers", 3, "none", 2, "headband", 2), "ruler", W("chief", 1), "female", W("feathers", 2, "none", 1), "_", W("feathers", 2, "none", 2, "headband", 1)),
                outfit = RM("warrior", W("pelt", 3, "rattan", 2, "bare", 1), "ruler", W("chiefrobe", 1), "female", W("pelt", 1), "_", W("pelt", 2, "rattan", 1, "bare", 1)),
                cloth = new[] { "#8a3a24", "#b8862a", "#4a6a2a", "#7a2a3a", "#2a5a6a", "#a04a2a" },
                metal = new[] { "#8a7048", "#6a5a3a" },
                beardsYoung = W("none", 3, "stubble", 2), beardsAdult = W("short", 3, "full", 2, "none", 2, "stubble", 2, "bristle", 1),
                musts = W("thick", 2, "droop", 2, "none", 1),
                marks = W("warpaint", 0.5), acc = W("hoops", 0.8, "bones", 0.6) } },
            { "wa", new Cult {
                name = "倭", skins = new[] { "#efcaa4", "#e6bd96", "#dcb088", "#d0a27a" }, warSkin = 0.2,
                hairC = W("#16131a", 1), eyeC = BROWNEYE, lid = 0.7, noseBridge = 0.35, noseW = 1.05,
                hair = RM("m", W("mizura", 4, "topknot", 1), "f", W("wahair", 1)),
                hat = RM("warrior", W("none", 3, "headband", 2, "wahelm", 2), "ruler", W("headband", 1, "wahelm", 1), "female", W("shaman", 1, "none", 1), "_", W("none", 3, "headband", 1)),
                outfit = RM("warrior", W("tanko", 3, "kantoui", 2), "female", W("kantoui", 1), "_", W("kantoui", 3, "tanko", 1)),
                cloth = new[] { "#e6dcc4", "#c8b890", "#9a3a2a", "#3a3a5a", "#d8ccb0", "#7a5a3a" },
                metal = new[] { "#5a5e68", "#4a4a50", "#7a6a48" },
                beardsYoung = W("none", 3, "stubble", 1), beardsAdult = W("short", 3, "full", 2, "goatee", 1, "stubble", 2, "none", 1),
                musts = W("thick", 2, "thin", 2, "droop", 1),
                marks = W("tattoo-wa", 0.75), acc = W("magatama", 0.6) } },
            { "yi", new Cult {
                name = "夷洲", skins = new[] { "#d8a676", "#c99666", "#b98656", "#a87a4e" }, warSkin = 0.2,
                hairC = W("#141012", 1), eyeC = BROWNEYE, lid = 0.55, noseBridge = 0.3, noseW = 1.15,
                noses = W("broad", 3, "straight", 2, "flat", 1),
                hair = RM("m", W("long", 3, "mallet", 1), "f", W("long", 1)),
                hat = RM("_", W("yiband", 4, "none", 1)),
                outfit = RM("_", W("yivest", 3, "bare", 1)),
                cloth = new[] { "#b02a24", "#1a1a1a", "#e8dcc0", "#2a4a6a" },
                metal = new[] { "#8a7048" },
                beardsYoung = W("none", 1), beardsAdult = W("none", 4, "stubble", 2, "goatee", 1),
                musts = W("none", 3, "thin", 1),
                marks = W("tattoo-yi", 0.75), acc = W("shells", 0.7, "hoops", 0.4) } },
            { "korea", new Cult {
                name = "朝鲜", skins = new[] { "#f0cba8", "#e8c09c", "#dfb490", "#d6a884" }, warSkin = 0.2,
                hairC = W("#16131a", 1), eyeC = BROWNEYE, lid = 0.75, noseBridge = 0.4, noseW = 1.0,
                hair = RM("m", W("topknot", 1), "f", W("bun", 2, "koreanbraid", 1)),
                hat = RM("warrior", W("helmet", 3, "jeolpung", 2), "general", W("helmet", 2, "birdfeather", 2), "ruler", W("goldcrown", 2, "birdfeather", 2), "female", W("none", 1), "_", W("jeolpung", 3, "birdfeather", 2)),
                outfit = RM("warrior", W("lamellar", 3, "scale", 1), "general", W("lamellar", 2, "jacket", 1), "female", W("jacket", 1), "_", W("jacket", 1)),
                cloth = new[] { "#c84a2a", "#e8c84a", "#3a5a8a", "#6a3a6a", "#e8e0d0", "#2a6a4a", "#8a2a2a" },
                metal = new[] { "#5a5e68", "#7a808c", "#8a7048" },
                beardsYoung = W("none", 3, "goatee", 1), beardsAdult = W("goatee", 3, "short", 2, "long", 1, "none", 1),
                musts = W("thin", 3, "thick", 1) } },
            { "steppe", new Cult {
                name = "草原", skins = new[] { "#e8bc92", "#dcae84", "#d0a078", "#c49470" }, warSkin = 0.25, ruddy = 0.6,
                hairC = W("#1a1614", 4, "#2a1e16", 2, "#3a2a1c", 1), eyeC = BROWNEYE, lid = 0.75, noseBridge = 0.42, noseW = 1.05,
                hair = RM("m", W("kunfa", 4, "braids", 3), "f", W("braids", 1)),
                hat = RM("warrior", W("felt", 3, "fur", 2, "none", 2, "helmet", 1), "ruler", W("fur", 2, "felt", 2), "female", W("felt", 1, "none", 1), "_", W("felt", 2, "none", 2, "fur", 1)),
                outfit = RM("warrior", W("kaftan", 3, "fur", 2, "lamellar", 1), "_", W("kaftan", 3, "fur", 2)),
                cloth = new[] { "#8a5a3a", "#5a3a2a", "#3a4a5a", "#7a2a24", "#2a4a3a", "#a8743a", "#4a3a5a" },
                metal = new[] { "#5a5e68", "#7a6a48" }, cross = -1,
                beardsYoung = W("none", 3, "stubble", 1), beardsAdult = W("goatee", 2, "short", 2, "none", 2, "full", 1),
                musts = W("droop", 4, "thin", 2, "thick", 1),
                acc = W("earring", 0.5) } },
            { "seasia", new Cult {
                name = "南海", skins = new[] { "#bc8656", "#b07a4a", "#a06c40", "#946238", "#87582f" }, warSkin = 0.15,
                hairC = W("#141012", 1), eyeC = BROWNEYE, lid = 0.4, noseBridge = 0.28, noseW = 1.25,
                noses = W("flat", 2, "broad", 3, "straight", 1),
                hair = RM("m", W("highbun", 3, "long", 1), "f", W("highbun", 1)),
                hat = RM("ruler", W("tallcrown", 1), "warrior", W("none", 2, "flowerwrap", 1, "headband", 1), "female", W("none", 1), "_", W("none", 2, "flowerwrap", 2)),
                outfit = RM("_", W("bare", 3, "sash", 2), "female", W("sash", 1)),
                cloth = new[] { "#c8a030", "#a02a3a", "#e8dcc0", "#2a6a5a", "#7a3a8a", "#c85a2a" },
                metal = new[] { "#c8a040" },
                beardsYoung = W("none", 1), beardsAdult = W("none", 4, "goatee", 1, "stubble", 1),
                musts = W("none", 3, "thin", 2),
                acc = W("bigears", 0.8, "goldcollar", 0.5) } },
            { "tarim", new Cult {
                name = "西域", skins = new[] { "#f2d0b0", "#ebc6a2", "#e2ba96", "#d8ae8a" }, warSkin = 0.2,
                hairC = W("#3a2618", 3, "#5a3a20", 2, "#7a4a28", 1, "#1a1614", 2, "#8a5a30", 1), eyeC = new[] { "#3a2a1a", "#4a6a5a", "#6a6a5a", "#5a4a2a", "#4a5a6a" },
                lid = 0.15, noseBridge = 0.75, noseW = 0.95, noses = W("straight", 3, "aquiline", 2),
                hair = RM("m", W("bob", 3, "long", 1, "short", 1), "f", W("long", 1, "braids", 1)),
                hat = RM("ruler", W("diadem", 2, "pointcap", 1), "_", W("pointcap", 3, "none", 2)),
                outfit = RM("warrior", W("kaftan", 2, "scale", 2), "_", W("kaftan", 3)),
                cloth = new[] { "#7a3a2a", "#2a5a6a", "#c8a04a", "#5a2a4a", "#e0d0b0", "#3a5a3a" },
                metal = new[] { "#7a808c", "#8a7048" },
                beardsYoung = W("none", 3, "stubble", 1), beardsAdult = W("short", 3, "goatee", 2, "full", 1, "none", 1),
                musts = W("curl", 3, "thin", 2, "thick", 1),
                acc = W("earring", 0.3) } },
            { "kushan", new Cult {
                name = "贵霜", skins = new[] { "#e6bd96", "#dcb08a", "#cfa27c", "#c49470" }, warSkin = 0.2,
                hairC = W("#1a1614", 3, "#2a1e16", 3, "#3a2a1c", 1), eyeC = BROWNEYE, lid = 0.15, noseBridge = 0.9, noseW = 1.0,
                noses = W("aquiline", 3, "straight", 2, "bulb", 1),
                hair = RM("m", W("curly", 2, "long", 2), "f", W("long", 1)),
                hat = RM("ruler", W("tallhat", 2, "diadem", 1), "warrior", W("helmet", 2, "tallhat", 1, "diadem", 1), "female", W("none", 1), "_", W("tallhat", 2, "diadem", 1, "none", 1)),
                outfit = RM("warrior", W("kaftan", 2, "scale", 2), "_", W("kaftan", 3)),
                cloth = new[] { "#8a2a2a", "#c8903a", "#3a5a3a", "#2a3a6a", "#e0c8a0", "#6a2a5a" },
                metal = new[] { "#8a7048", "#7a808c", "#a08048" },
                beardsYoung = W("short", 1, "none", 1), beardsAdult = W("full", 3, "curled", 1, "short", 2, "long", 1),
                musts = W("thick", 3, "curl", 1),
                acc = W("earring", 0.5) } },
            { "persia", new Cult {
                name = "安息", skins = new[] { "#e2b48c", "#d6a880", "#ca9a72", "#bd8c66" }, warSkin = 0.2,
                hairC = W("#16131a", 3, "#221a18", 3), eyeC = BROWNEYE, lid = 0.1, noseBridge = 0.95, noseW = 1.0,
                noses = W("aquiline", 3, "straight", 2),
                hair = RM("m", W("bushy", 3, "curly", 1), "f", W("long", 1)),
                hat = RM("ruler", W("tiara", 3, "diadem", 1), "warrior", W("helmet", 2, "diadem", 1, "phrygian", 1), "female", W("diadem", 1), "_", W("diadem", 2, "phrygian", 2, "none", 1)),
                outfit = RM("warrior", W("scale", 3, "kaftan", 1), "ruler", W("tunic", 2, "scale", 1), "female", W("tunic", 1), "_", W("tunic", 2, "kaftan", 1)),
                cloth = new[] { "#7a2a5a", "#2a4a7a", "#c8a040", "#8a2a24", "#3a6a5a", "#5a2a6a" },
                metal = new[] { "#7a808c", "#8a7048", "#5a5e68" },
                beardsYoung = W("short", 2, "none", 1), beardsAdult = W("curled", 2, "long", 2, "forked", 1, "full", 2),
                musts = W("thick", 2, "curl", 2),
                acc = W("earring", 0.6, "torc", 0.3) } },
            { "arab", new Cult {
                name = "阿拉伯", skins = new[] { "#deaa7a", "#d29c6c", "#c48e5e", "#b88254", "#a8744a" }, warSkin = 0.2,
                hairC = W("#141012", 1), eyeC = BROWNEYE, lid = 0.1, noseBridge = 0.9, noseW = 1.0,
                noses = W("aquiline", 3, "straight", 2),
                hair = RM("m", W("curly", 3, "long", 1), "f", W("long", 1)),
                hat = RM("ruler", W("hatra", 1, "keffiyeh", 1), "female", W("keffiyeh", 1), "_", W("keffiyeh", 3, "arabturban", 2)),
                outfit = RM("warrior", W("robea", 2, "scale", 2), "_", W("robea", 3)),
                cloth = new[] { "#e8e0d0", "#8a2a24", "#2a3a5a", "#c8a050", "#3a5a3a", "#5a3a2a" },
                metal = new[] { "#7a808c", "#8a7048" },
                beardsYoung = W("short", 2, "none", 1), beardsAdult = W("full", 3, "curled", 1, "short", 2, "goatee", 1),
                musts = W("thick", 3, "thin", 1) } },
            { "roman", new Cult {
                name = "罗马", skins = new[] { "#efcfb0", "#e6c09e", "#dbb28e", "#cfa480" }, warSkin = 0.25,
                hairC = W("#2a1e16", 3, "#3a2a1c", 2, "#1a1614", 2, "#5a3a22", 1, "#8a6a40", 0.5), eyeC = new[] { "#3a2a1a", "#4a5a6a", "#5a6a4a", "#2a1a10" },
                lid = 0.1, noseBridge = 0.9, noseW = 0.95, noses = W("straight", 3, "aquiline", 2),
                hair = RM("m", W("short", 3, "curly", 2), "f", W("romanf", 1)),
                hat = RM("ruler", W("laurel", 3), "warrior", W("galea", 3, "none", 1), "general", W("galea", 2, "none", 1), "female", W("none", 1), "_", W("none", 3, "laurel", 0.4)),
                outfit = RM("warrior", W("segmentata", 2, "musculata", 1), "general", W("musculata", 2), "ruler", W("musculata", 2, "toga", 1), "official", W("toga", 2), "strategist", W("toga", 1, "tunic", 1), "female", W("tunic", 1), "_", W("tunic", 1, "segmentata", 1)),
                cloth = new[] { "#a02a24", "#7a2a5a", "#e8e0d0", "#2a3a5a", "#c8a050", "#8a3a2a" },
                metal = new[] { "#b08a50", "#7a808c", "#9aa0aa" },
                beardsYoung = W("none", 3, "stubble", 1), beardsAdult = W("none", 2, "short", 3, "full", 1, "stubble", 1),
                musts = W("none", 1, "thick", 1), mustWithBeard = true } },
            { "celt", new Cult {
                name = "凯尔特", skins = new[] { "#f6dcc6", "#f0d0b8", "#e8c4aa", "#ddb89c" }, warSkin = 0.15, freckle = 0.4,
                hairC = W("#a8481e", 3, "#c86a2a", 2, "#d8b070", 2, "#6a4a2a", 2, "#3a2a1c", 1), eyeC = LIGHTEYE,
                lid = 0.0, noseBridge = 0.8, noseW = 1.0,
                hair = RM("m", W("wild", 3, "spiky", 2, "braids", 2), "f", W("braids", 2, "long", 1)),
                hat = RM("_", W("none", 5, "celthelm", 1), "female", W("none", 1)),
                outfit = RM("_", W("plaid", 3, "fur", 1, "bare", 1), "female", W("plaid", 1)),
                cloth = new[] { "#3a6a3a", "#7a2a24", "#2a4a7a", "#8a6a2a", "#5a3a5a" },
                metal = new[] { "#b08a50", "#7a808c" },
                beardsYoung = W("none", 2, "stubble", 1), beardsAdult = W("none", 3, "braided", 1, "full", 2, "short", 1),
                musts = W("droop", 5, "thick", 1), mustAlways = true,
                marks = W("woad", 0.55), acc = W("torc", 0.7) } },
            { "german", new Cult {
                name = "日耳曼", skins = new[] { "#f4d8c0", "#ecccb2", "#e2c0a4", "#d8b498" }, warSkin = 0.15, freckle = 0.2,
                hairC = W("#d8b878", 3, "#c8a060", 2, "#a87a40", 2, "#8a5a2a", 2, "#c06030", 1), eyeC = LIGHTEYE,
                lid = 0.0, noseBridge = 0.85, noseW = 1.0,
                hair = RM("m", W("suebian", 4, "long", 2, "wild", 1), "f", W("braids", 2, "long", 1)),
                hat = RM("warrior", W("none", 4, "spangen", 1, "furcap", 1), "female", W("none", 1), "_", W("none", 4, "furcap", 1)),
                outfit = RM("_", W("fur", 3, "tunic", 1)),
                cloth = new[] { "#5a4a3a", "#3a4a3a", "#7a3a2a", "#4a4a5a", "#8a7a5a" },
                metal = new[] { "#7a808c", "#5a5e68" },
                beardsYoung = W("short", 1, "none", 1), beardsAdult = W("full", 3, "long", 2, "braided", 1, "short", 1),
                musts = W("thick", 2, "droop", 2),
                acc = W("armring", 0.3) } },
            { "sarmatian", new Cult {
                name = "萨尔马提亚", skins = new[] { "#f0d0b0", "#e6c4a2", "#dcb894", "#d2ac88" }, warSkin = 0.2,
                hairC = W("#5a3a22", 3, "#3a2a1c", 2, "#8a6a40", 2, "#c8a060", 1, "#1a1614", 1), eyeC = LIGHTEYE,
                lid = 0.2, noseBridge = 0.85, noseW = 1.0,
                hair = RM("m", W("long", 3, "braids", 1), "f", W("braids", 1, "long", 1)),
                hat = RM("warrior", W("conical", 4, "none", 1), "ruler", W("conical", 2, "diadem", 1), "female", W("pointcap", 1), "_", W("pointcap", 2, "conical", 1, "none", 1)),
                outfit = RM("_", W("scale", 4, "kaftan", 1)),
                cloth = new[] { "#7a2a24", "#2a4a5a", "#c8a050", "#3a3a3a", "#6a3a5a" },
                metal = new[] { "#7a808c", "#8a7048", "#5a5e68" }, cross = -1,
                beardsYoung = W("none", 2, "short", 1), beardsAdult = W("long", 2, "full", 2, "short", 1),
                musts = W("droop", 4, "thick", 1),
                acc = W("earring", 0.5, "torc", 0.3) } },
        };
        // 冠帽默认配色（按类型）
        static readonly Dictionary<string, string[]> HAT_COL = new Dictionary<string, string[]>
        {
            { "ze", new[] { "#2a2226", "#2a2226", "#7a2424", "#3a2a20", "#2a3040" } },
            { "headband", new[] { "#a02a24", "#2a2a2a", "#c8a030", "#2a4a7a", "#7a2a5a" } },
            { "turban", new[] { "#3a6a3a", "#5a4a3a", "#2a3a5a", "#7a3a2a", "#4a4a4a", "#6a5a2a" } },
            { "futou", new[] { "#5a4a3a", "#3a3a3a", "#6a6050", "#2a3a4a" } },
            { "felt", new[] { "#e0d4b8", "#8a3a2a", "#5a4a3a", "#3a4a5a" } },
            { "pointcap", new[] { "#e8dcc0", "#9a2a24", "#5a4a3a", "#c8a050" } },
            { "tallhat", new[] { "#e8dcc0", "#8a2a2a", "#c8a050" } },
            { "phrygian", new[] { "#9a2a24", "#2a4a7a", "#c8a050", "#5a2a5a" } },
            { "keffiyeh", new[] { "#ece6da", "#e8dcc8", "#d8c8a8" } },
            { "arabturban", new[] { "#ece6da", "#2a3a5a", "#8a2a24", "#c8a050" } },
            { "flowerwrap", new[] { "#c83a4a", "#e8c040", "#2a8a7a", "#e0d8c0" } },
        };
        static readonly WL PLUMES = W("#c0302a", 5, "#1a1a1a", 1, "#e8e8e8", 1, "#2a4aa0", 1, "#d8a020", 1);
        static readonly string[] SKIN_TINT = { "#d0806a", "#c8b07a", "#a87050", "#e0a090" };

        // ======================================================== 外貌参数 ==
        static readonly Dictionary<string, double[]> ROLE_AGE = new Dictionary<string, double[]>
        {
            { "warrior", new double[] { 24, 46 } }, { "general", new double[] { 28, 50 } }, { "strategist", new double[] { 26, 56 } }, { "official", new double[] { 30, 62 } },
            { "ruler", new double[] { 34, 58 } }, { "commoner", new double[] { 22, 50 } }, { "female", new double[] { 18, 34 } },
        };
        static HashSet<string> rulerNames;
        static bool IsRuler(string name)
        {
            if (rulerNames == null)
            {
                var s = new HashSet<string>();
                try { foreach (var line in ScenarioData.Factions) s.Add(line.Split('|')[2]); } catch (Exception) { /* 无剧本数据 */ }
                rulerNames = s;
            }
            return rulerNames.Contains(name);
        }
        static string RoleOf(double war, double intel, double pol, bool ruler, string sex)
        {
            if (sex == "f") return "female";
            if (ruler) return "ruler";
            if (war >= 80 && war >= intel + 8) return "warrior";
            if (war >= 70 && intel >= 70) return "general";
            if (intel >= 76 && intel >= war + 12) return pol > intel + 4 ? "official" : "strategist";
            if (pol >= 72 && pol >= war + 12) return "official";
            if (war >= 66) return "warrior";
            return "commoner";
        }
        // 简写 → 对象：'turban' → { type: 'turban' }
        static readonly Dictionary<string, string> OBJ_KEYS = new Dictionary<string, string>
        {
            { "hat", "type" }, { "outfit", "type" }, { "hair", "style" }, { "beard", "style" }, { "mustache", "style" },
            { "eyes", "shape" }, { "brows", "shape" }, { "nose", "shape" }, { "mouth", "shape" },
        };
        static JObj NormEntry(JObj e)
        {
            var o = new JObj();
            foreach (var kv in e)
            {
                string ok;
                if (OBJ_KEYS.TryGetValue(kv.Key, out ok) && kv.Value is string) o[kv.Key] = new JObj { { ok, kv.Value } };
                else o[kv.Key] = kv.Value;
            }
            return o;
        }
        // 当前年份（画像年龄 = 当前年份 − 生年）。Unity 端由游戏状态提供
        public static Func<int> Year = () => ScenarioData.StartYear;
        static int CurYear() { try { int y = Year != null ? Year() : 0; return y > 0 ? y : ScenarioData.StartYear; } catch (Exception) { return ScenarioData.StartYear; } }

        static WL PickTable(Dictionary<string, WL> table, string role)
        {
            WL l;
            if (table != null && (table.TryGetValue(role, out l) || table.TryGetValue("_", out l) || table.TryGetValue("commoner", out l))) return l;
            return W("none", 1);
        }
        static List<object> SL(params string[] a) { return new List<object>(a); }

        static JObj DefaultSpec(PortraitGen gen, JObj data)
        {
            string name = string.IsNullOrEmpty(gen.name) ? "无名" : gen.name;
            string dc = data.S("culture");
            string culture = dc != null && CULT.ContainsKey(dc) ? dc : gen.culture != null && CULT.ContainsKey(gen.culture) ? gen.culture : (NANMAN.Contains(name) ? "nanman" : "han");
            var C = CULT[culture];
            double war = gen.war ?? 50, intel = gen.intel ?? 50, pol = gen.pol ?? 50;
            string sex = Js.Or(data.S("sex"), Js.Or(gen.sex, "m"));
            bool fem = sex == "f";
            string role = Js.Or(data.S("role"), RoleOf(war, intel, pol, IsRuler(name), sex));
            Rng rb = Sub(name, "base"), rf = Sub(name, "face"), rh = Sub(name, "hair"), rc = Sub(name, "cloth"), rx = Sub(name, "extra");
            double[] ar; if (!ROLE_AGE.TryGetValue(role, out ar)) ar = new double[] { 24, 50 };
            double age = data.D("age");
            if (double.IsNaN(age) && gen.born.HasValue && gen.born.Value != 0) age = CurYear() - gen.born.Value;
            if (double.IsNaN(age)) age = Js.Round(lerp(ar[0], ar[1], Math.Pow(rb.f(), 1.3)));
            age = clamp(age, 14, 92);
            double tough = clamp((war - 50) / 50, -1, 1);           // 武勇
            double wise = clamp((intel - 50) / 50, -1, 1);          // 智略
            double old = clamp((age - 40) / 30, 0, 1);
            bool martial = role == "warrior" || role == "general";
            // 肤色：武将偏深（风吹日晒），并加一点个人色调
            double skinI = clamp(rb.f() * 0.85 + (martial ? C.warSkin : 0) + (fem ? -0.2 : 0), 0, 0.999);
            string skin = C.skins[(int)Math.Floor(skinI * C.skins.Length)];
            if (rb.chance(0.65)) { string tint = rb.pick(SKIN_TINT); skin = mix(skin, tint, rb.range(0.04, 0.14)); }
            double fat;
            if (data.Has("fat")) fat = data.D("fat");
            else fat = (rb.chance(0.14) ? rb.range(0.25, 0.6) : 0) * (role == "warrior" ? 0.7 : 1) * (fem ? 0.3 : 1);
            var face = new JObj();
            face["w"] = (fem ? 0.95 : 1) * rf.range(0.91, 1.08) + tough * 0.03;
            face["jaw"] = fem ? rf.range(0.76, 0.9) : rf.range(0.84, 1.18) + tough * 0.08;
            face["chin"] = fem ? rf.range(0.84, 0.95) : rf.range(0.84, 1.16);
            face["len"] = fem ? rf.range(0.92, 1.0) : rf.range(0.92, 1.1) + wise * 0.03;
            face["cheek"] = rf.range(0, 1) * (fem ? 0.4 : 1);
            face["hollow"] = clamp(rf.range(0, 0.5) + old * 0.6 - fat, 0, 1);
            face["brow"] = clamp(rf.range(0, 1) * (fem ? 0.2 : 1) + tough * 0.3, 0, 1.4);
            string hairColor;
            {
                string bs = rh.wpick(C.hairC);
                if (age >= 66) hairColor = rh.pick(new[] { "#e8e4dc", "#d8d4cc", "#f0ece4" });
                else if (age >= 52) hairColor = mix(bs, "#c8c4bc", clamp((age - 50) / 18, 0.3, 0.8));
                else if (age >= 44 && rh.chance(0.5)) hairColor = mix(bs, "#a8a49c", 0.25);
                else hairColor = bs;
            }
            WL eyeShapes = fem ? (war >= 70 ? W("phoenix", 3, "sharp", 2, "gentle", 1) : W("gentle", 3, "phoenix", 3, "big", 1.5))
                : role == "warrior" ? W("sharp", 4, "normal", 3, "round", 2, "phoenix", 1, "narrow", 1, "sleepy", 0.5)
                : role == "strategist" || role == "official" ? W("narrow", 3, "normal", 3, "gentle", 2, "phoenix", 2, "sharp", 1, "sleepy", 1)
                : W("normal", 4, "sharp", 2, "gentle", 2, "narrow", 1, "round", 1, "sleepy", 1);
            WL browShapes = fem ? W("thin", 1)
                : role == "warrior" ? W("angled", 4, "bushy", 3, "knit", 2, "straight", 1, "sword", 1)
                : role == "strategist" || role == "official" ? W("thin", 3, "arched", 3, "straight", 2, "angled", 1)
                : W("straight", 3, "arched", 2, "angled", 2, "bushy", 1);
            WL noseShapes = fem ? W("small", 1) : (C.noses ?? W("straight", 5, "aquiline", 1, "broad", 2, "button", 1, "bulb", 0.4));
            // 胡须
            string beard = "none", must = "none"; double beardLen = 1;
            var rB = Sub(name, "beard");
            if (!fem && age >= 18)
            {
                if (C.beard == "han")
                {
                    if (role == "warrior")
                    {
                        beard = rB.wpick(age < 26 ? W("none", 3, "stubble", 3, "short", 2, "goatee", 1) : W("full", 2, "short", 3, "bristle", 1, "long", 1, "goatee", 2, "stubble", 1.5, "none", 1, "forked", 0.5));
                        must = beard == "none" ? rB.wpick(W("none", 2, "thick", 1)) : rB.wpick(W("thick", 3, "droop", 2, "thin", 1, "curl", 0.5));
                    }
                    else if (role == "strategist" || role == "official")
                    {
                        beard = rB.wpick(age < 28 ? W("none", 3, "goatee", 2) : W("long", 3, "goatee", 4, "short", 1, "forked", 0.5));
                        must = beard == "none" ? (rB.chance(0.3) ? "thin" : "none") : rB.wpick(W("thin", 4, "droop", 1));
                    }
                    else if (role == "ruler" || role == "general")
                    {
                        beard = rB.wpick(W("long", 3, "short", 3, "goatee", 2, "full", 1, "forked", 0.5));
                        must = rB.wpick(W("thin", 3, "thick", 2, "droop", 1));
                    }
                    else
                    {
                        beard = rB.wpick(age < 26 ? W("none", 3, "stubble", 2, "goatee", 1) : W("short", 3, "goatee", 2, "full", 2, "stubble", 2, "none", 1));
                        must = beard == "none" ? "none" : rB.wpick(W("thin", 2, "thick", 2, "droop", 1));
                    }
                }
                else
                {
                    bool hasTb = C.beardsYoung != null;
                    WL young = hasTb ? C.beardsYoung : W("none", 1), adult = hasTb ? C.beardsAdult : W("short", 1);
                    beard = rB.wpick(age < 25 ? young : adult);
                    must = rB.wpick(C.musts ?? W("thick", 1));
                    if (C.mustWithBeard && (beard == "none" || beard == "stubble")) must = "none";
                    if (!C.mustAlways && beard == "none" && rB.chance(0.5)) must = "none";
                }
                beardLen = rB.range(0.8, 1.3) + old * 0.3;
            }
            // 冠帽
            string hatType = rc.wpick(PickTable(C.hat, role));
            string[] hc;
            var hat = new JObj { { "type", hatType }, { "color", HAT_COL.TryGetValue(hatType, out hc) ? rc.pick(hc) : null }, { "color2", null } };
            if (hatType == "helmet")
            {
                WL vv;
                if (culture == "korea") vv = W("plume", 3, "round", 1);
                else if (culture == "kushan" || culture == "persia") vv = W("spike", 3, "round", 1);
                else if (culture == "steppe") vv = W("round", 2, "spike", 1);
                else vv = war >= 88 ? W("plain", 4, "round", 3, "spike", 2, "wing", 1.5, "horn", 0.6) : W("plain", 4, "round", 3, "spike", 2);
                hat["variant"] = rc.wpick(vv);
                hat["plume"] = rc.wpick(PLUMES);
                hat["neck"] = rc.chance(0.5) ? null : rc.pick(new[] { "#5a3a24", "#7a2020", "#2a2a3a", "#4a4030" });
            }
            // 衣甲
            // 女将（武力高的女性）穿本文化的武将衣甲；女性不赤膊
            string outfitType = rc.wpick(PickTable(C.outfit, fem && war >= 70 ? "warrior" : role));
            if (fem && outfitType == "bare")
            {
                WL fl; string found = null;
                if (C.outfit.TryGetValue("female", out fl) || C.outfit.TryGetValue("_", out fl)) foreach (var t in fl.k) if (t != "bare") { found = t; break; }
                outfitType = found ?? "robe";
            }
            string clothC = rc.pick(C.cloth);
            string cloth2 = rc.pick(C.cloth);
            if (cloth2 == clothC) cloth2 = mix(clothC, "#000", 0.4);
            var outfit = new JObj { { "type", outfitType }, { "color", clothC }, { "color2", cloth2 } };
            outfit["metal"] = rc.pick(C.metal);
            outfit["cape"] = role == "ruler" ? "faction" : null;
            outfit["cross"] = C.cross != 0 ? C.cross : 1.0;
            // 标记与配饰
            var marks = new List<object>(); var acc = new List<object>();
            if (C.marks != null) for (int i = 0; i < C.marks.k.Length; i++) if (!fem && rx.chance(C.marks.w[i])) marks.Add(C.marks.k[i]);
            if (C.freckle != 0 && rx.chance(C.freckle)) marks.Add("freckles");
            if (C.acc != null) for (int i = 0; i < C.acc.k.Length; i++) if (rx.chance(C.acc.w[i])) acc.Add(C.acc.k[i]);
            if (martial && rx.chance(0.06)) marks.Add("scar");
            if (rx.chance(0.05)) marks.Add("mole");
            // 散发几缕（武人）
            double locks = martial && rh.chance(0.3) ? rh.@int(1, 2) : 0;
            var sp = new JObj();
            sp["name"] = name; sp["culture"] = culture; sp["sex"] = sex; sp["role"] = role; sp["age"] = age;
            sp["war"] = war; sp["intel"] = intel; sp["pol"] = pol; sp["fat"] = fat; sp["face"] = face; sp["skin"] = skin;
            sp["blush"] = C.ruddy != 0 ? 1.8 : 1.0;
            sp["build"] = fem ? 0.84 : clamp(0.94 + tough * 0.1 + fat * 0.15 + rb.range(-0.04, 0.05), 0.86, 1.2);
            sp["neck"] = fem ? 0.74 : clamp(0.95 + tough * 0.12 + fat * 0.4, 0.85, 1.45);
            var eyes = new JObj();
            eyes["shape"] = rf.wpick(eyeShapes); eyes["color"] = rf.pick(C.eyeC); eyes["size"] = rf.range(0.9, 1.08) * (fem ? 1.08 : 1);
            eyes["lid"] = C.lid; eyes["gap"] = rf.range(-0.035, 0.035); eyes["y"] = rf.range(-0.035, 0.035);
            sp["eyes"] = eyes;
            var brows = new JObj();
            brows["shape"] = rf.wpick(browShapes); brows["thick"] = clamp(rf.range(0.8, 1.15) + tough * 0.15, 0.6, 1.45) * (fem ? 0.7 : 1); brows["color"] = null; brows["y"] = rf.range(-0.05, 0.04);
            sp["brows"] = brows;
            var nose = new JObj();
            nose["shape"] = rf.wpick(noseShapes); nose["size"] = rf.range(0.9, 1.1); nose["len"] = rf.range(0.92, 1.1); nose["bridge"] = C.noseBridge; nose["w"] = C.noseW * rf.range(0.9, 1.12);
            sp["nose"] = nose;
            var mouth = new JObj();
            mouth["w"] = rf.range(0.88, 1.1) * (fem ? 0.9 : 1); mouth["lips"] = fem ? 1.2 : rf.range(0.75, 1.15); mouth["shape"] = "neutral"; mouth["y"] = rf.range(-0.03, 0.04);
            sp["mouth"] = mouth;
            sp["ears"] = new JObj { { "size", rf.range(0.9, 1.12) } };
            WL hs; if (!C.hair.TryGetValue(sex, out hs)) hs = C.hair["m"];
            var hair = new JObj();
            hair["style"] = rh.wpick(hs); hair["color"] = hairColor; hair["vol"] = rh.range(0.85, 1.15); hair["locks"] = locks;
            sp["hair"] = hair;
            sp["beard"] = new JObj { { "style", beard }, { "len", beardLen }, { "color", rB.chance(0.15) ? mix(hairColor, "#5a3a20", 0.3) : null } };
            sp["mustache"] = new JObj { { "style", must } };
            sp["hat"] = hat; sp["outfit"] = outfit; sp["acc"] = acc; sp["marks"] = marks; sp["weapon"] = null;
            sp["expr"] = martial ? rb.wpick(W("stern", 3, "fierce", 2, "neutral", 2, "smile", 0.7, "sly", 0.4)) : rb.wpick(W("neutral", 3, "calm", 2, "stern", 1, "smile", 1, "sly", 0.6));
            return sp;
        }
        static readonly HashSet<string> NANMAN = new HashSet<string> { "孟获", "祝融", "孟优", "兀突骨", "带来洞主", "沙摩柯", "孟节", "朵思大王", "木鹿大王", "金环三结", "董荼那", "阿会喃" };

        // 深合并一层（对象字段逐项覆盖）
        static JObj MergeSpec(JObj bs, JObj data)
        {
            var o = bs.Clone();
            foreach (var kv in data)
            {
                string k = kv.Key; object v = kv.Value;
                var vo = v as JObj;
                // 换了冠帽类型时，不沿用默认冠帽的配色（否则紫金冠会染上帻的黑色）
                if (k == "hat" && vo != null && vo.T("type") && bs.O("hat") != null && vo.S("type") != bs.O("hat").S("type"))
                {
                    string t = vo.S("type"); string[] hc;
                    var first = new JObj { { "type", t }, { "color", null }, { "color2", null } };
                    JObj mid = HAT_COL.TryGetValue(t, out hc) ? new JObj { { "color", hc[HashStr(bs.S("name") + "hc") % (uint)hc.Length] } } : null;
                    o["hat"] = JObj.Assign(first, mid, vo);
                    continue;
                }
                if (vo != null && bs.O(k) != null) o[k] = JObj.Assign(bs.O(k), vo);
                else o[k] = v;
            }
            return o;
        }
        static PortraitGen FindGen(string name)
        {
            var g = FindGenHook != null ? FindGenHook(name) : null;
            if (g != null) return g;
            try
            {
                foreach (var line in ScenarioData.Generals)
                {
                    var p = line.Split('|');
                    if (p[0] == name) return new PortraitGen { name = name, war = double.Parse(p[1], CI), intel = double.Parse(p[2], CI), pol = double.Parse(p[3], CI) };
                }
            }
            catch (Exception) { /* 忽略 */ }
            return new PortraitGen { name = name };
        }
        // 按姓名找当前游戏中的武将（Unity 端接入；缺省只查经典剧本）
        public static Func<string, PortraitGen> FindGenHook;

        static readonly Dictionary<string, JObj> specCache = new Dictionary<string, JObj>();
        static int dataVersion;
        static string SpecKey(PortraitGen gen)
        {
            string name = string.IsNullOrEmpty(gen.name) ? "无名" : gen.name;
            // 只有带出生年的人才随年份变老（否则不必每年重画）
            return name + "|" + (gen.culture ?? "") + "|" + I32(gen.war) + "|" + I32(gen.intel) + "|" + I32(gen.pol) + "|" +
                (gen.born.HasValue && gen.born.Value != 0 ? Js.Num(gen.born.Value) + "@" + CurYear() : "") + "|" + (gen.sex ?? "");
        }
        static string I32(double? v) { return v.HasValue && !double.IsNaN(v.Value) ? ((int)v.Value).ToString(CI) : "0"; }
        // 解析后的外貌参数（调试用；格式见 js/portrait-data.js 文件头）
        public static JObj Spec(PortraitGen gen)
        {
            gen = gen ?? new PortraitGen { name = "无名" };
            string key = SpecKey(gen);
            lock (specCache) { JObj c; if (specCache.TryGetValue(key, out c)) return c; }
            string name = string.IsNullOrEmpty(gen.name) ? "无名" : gen.name;
            JObj raw = PortraitDataStore.Get(name) ?? gen.portrait ?? new JObj();
            var data = NormEntry(raw);
            var sp = MergeSpec(DefaultSpec(gen, data), data);
            if (sp.S("sex") == "f") { sp["beard"] = new JObj { { "style", "none" } }; sp["mustache"] = new JObj { { "style", "none" } }; }
            if (!sp.Brows.T("color")) sp.Brows["color"] = sp.Hair.G("color");
            if (!sp.Beard.T("color")) sp.Beard["color"] = sp.Hair.G("color");
            sp["key"] = key;
            lock (specCache) specCache[key] = sp;
            return sp;
        }
        public static JObj Spec(string name) { return Spec(FindGen(name)); }
        public static string[] Cultures { get { return (string[])CULT_ORDER.Clone(); } }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static partial class Portrait
    {
        // ======================================================== 几何骨架 ==
        // 所有面部位置以 R（半个头宽）为单位，相对 (hx, ey)（头轴、眼线）。
        internal sealed class EyeG { public double u, y, cx, w, dir; }
        internal sealed class EarG { public double u0, u1, v0, v1, x0, x1, y0, y1; }
        internal sealed class Ell { public double a, b, c, Cx, Cy; }
        internal sealed class NoseG { public double[] top, tip, bridgeMid, @base, alaTop, alaBack, alaBot, nostril; public double s, ny, dz, nw; public NoseShape n; }
        internal sealed class Geo
        {
            public double R, L, hx, ey, t, fat, jw, cw, st, ct, spe, cpe;
            public bool fem, hideEar;
            public Ell ell;
            public double lamFar, lamNear;
            public List<double[]> farC, jawC, headPts, neckPts, torso;
            public string headD, neckD;
            public double[] chinPt, jawAngle;
            public double eyeY, eyeDy, noseY, mouthY, chinY, bw, nw, chestX;
            public EyeG eyeN, eyeF;
            public EarG ear;
            public NoseG noseG;
            public double[] P(double x, double y) { return new[] { hx + x * R, ey + y * R, double.NaN }; }
            public double[] P(double x, double y, double k) { return new[] { hx + x * R, ey + y * R, k }; }
            // 面部宽度系数（下巴处收窄）
            public double wf(double y) { return y < 0.42 ? 1 : 1 - (1 - 0.58 * cw) * smooth01((y - 0.42) / (1.38 * L - 0.42)); }
            // 正面横向偏移 u（-1..1，单位半脸宽）在第 y 行的画面 x
            public double FX(double u, double y, double dz = 0) { return hx + R * wf(y) * Math.Sin(Math.Asin(clamp(u, -1, 1)) - t) - dz * R * st; }
            public double FS(double u, double y) { return wf(y) * Math.Cos(Math.Asin(clamp(u, -1, 1)) - t); }
            public double[] F(double u, double y, double dz = 0) { return new[] { FX(u, y, dz), ey + y * R, double.NaN }; }
            // 颅骨椭球上的点：经度 lam、纬度 bet、放大 s（第三分量为深度 z，同 JS）
            public double[] h3(double lam, double bet, double s = 1)
            {
                if (s == 0 || double.IsNaN(s)) s = 1;
                double X = ell.a * s * Math.Cos(bet) * Math.Sin(lam), Y = ell.b * s * Math.Sin(bet), Z = ell.c * s * Math.Cos(bet) * Math.Cos(lam);
                double x = X * ct - Z * st, z = X * st + Z * ct;
                return new[] { ell.Cx + x, ell.Cy - Y * cpe + z * spe, z };
            }
            public double silRx(double s = 1) { if (s == 0 || double.IsNaN(s)) s = 1; return s * Math.Sqrt(ell.a * ell.a * ct * ct + ell.c * ell.c * st * st); }
            public double silRy(double s = 1) { if (s == 0 || double.IsNaN(s)) s = 1; return s * ell.b * cpe; }
            public double midX(double y) { return FX(0, y); }
        }
        static Geo Geom(JObj sp)
        {
            var f = sp.Face; bool fem = sp.S("sex") == "f";
            double fat = sp.Or("fat", 0);
            var G = new Geo();
            double R = 38.5 * f.D("w") * (fem ? 0.95 : 1) * (1 + fat * 0.07);
            double L = f.D("len"), jw = f.D("jaw"), cw = f.D("chin");
            double hx = 136, ey = 121 - (L - 1) * 12;
            double t = 0.40, st = Math.Sin(t), ct = Math.Cos(t);
            double pe = 0.18, spe = Math.Sin(pe), cpe = Math.Cos(pe);
            G.R = R; G.L = L; G.hx = hx; G.ey = ey; G.t = t; G.fem = fem; G.fat = fat; G.jw = jw; G.cw = cw;
            G.st = st; G.ct = ct; G.spe = spe; G.cpe = cpe;
            // 颅骨椭球：中心 (hx+0.08R, ey-0.5R)，半轴 a 横 / b 竖 / c 前后
            double a = 1.0 * R, b = 1.02 * R, c = 1.22 * R, Cx = hx + 0.08 * R, Cy = ey - 0.5 * R;
            G.ell = new Ell { a = a, b = b, c = c, Cx = Cx, Cy = Cy };
            G.lamFar = Math.Atan2(-c * ct, a * st) + Math.PI; // 远侧轮廓经度（负值）
            if (G.lamFar > Math.PI) G.lamFar -= 2 * Math.PI;
            G.lamFar = -Math.Abs(Math.Atan(c * ct / (a * st)));
            G.lamNear = G.lamFar + Math.PI;
            // 头部轮廓（含颅骨）
            double rx1 = G.silRx(1), ry1 = G.silRy(1);
            Func<double, double, double[]> cr = (deg, k) => { double q = deg * Math.PI / 180; return V(Cx + rx1 * Math.Cos(q), Cy - ry1 * Math.Sin(q), k); };
            double bb = f.Or("brow", 0), ck = f.Or("cheek", 0), hol = f.Or("hollow", 0);
            double fk = fem ? 0.03 : 0;
            G.farC = Pts(
                G.P(-0.955, -0.58),
                G.P(-1.0 - bb * 0.035 + fk, -0.30),
                G.P(-0.955 + fk * 0.3, -0.06, fem ? 1 : 0.7),
                G.P(-0.99 - ck * 0.05 - fat * 0.03 + fk, 0.24),
                G.P(-0.93 + hol * 0.07 - fat * 0.12 + fk, 0.55),
                G.P(-0.82 - (jw - 1) * 0.25 - fat * 0.2 + fk, 0.86 * L),
                G.P(-0.66 * cw - (jw - 1) * 0.15 - fat * 0.2, 1.12 * L + fat * 0.06),
                G.P(-0.52 * cw - fat * 0.14, 1.31 * L + fat * 0.1, 0.7),
                G.P(-0.34 * cw - fat * 0.05, 1.42 * L + fat * 0.13));
            G.jawC = Pts(
                G.P(-0.06, 1.43 * L + fat * 0.22),
                G.P(0.30 * jw + fat * 0.15, 1.23 * L + fat * 0.16),
                G.P(0.66 * jw + fat * 0.25, 0.86 * L + fat * 0.1, 0.55),
                G.P(0.90 + fat * 0.1, 0.50),
                cr(-25, U));
            G.headPts = Cat(Pts(cr(160, U)), G.farC, G.jawC, Pts(cr(10, U), cr(45, U), cr(90, U), cr(130, U)));
            G.headD = spline(G.headPts, true);
            G.chinPt = G.P(-0.30 * cw, 1.43 * L);
            G.jawAngle = G.jawC[2];
            // 五官位置
            var E = sp.Eyes;
            double es = E.Or("size", 1), gap = E.Or("gap", 0), edy = E.Or("y", 0);
            G.eyeY = ey; G.eyeDy = edy * R;
            G.eyeN = new EyeG { u = 0.42 + gap, y = edy, cx = G.FX(0.42 + gap, edy), w = 0.42 * R * G.FS(0.42 + gap, edy) * es, dir = 1 };
            G.eyeF = new EyeG { u = -0.42 - gap, y = edy, cx = G.FX(-0.42 - gap, edy) + 0.02 * R, w = 0.42 * R * G.FS(-0.42 - gap, edy) * es, dir = -1 };
            var ears = sp.O("ears") ?? new JObj();
            double es2 = ears.Or("size", 1), el2 = ears.Or("lobe", 1);
            double eu0 = 0.52, eu1 = 0.52 + 0.32 * es2, ev0 = -0.22 - (es2 - 1) * 0.1, ev1 = 0.62 * Math.Sqrt(L) + (es2 - 1) * 0.35 + (el2 - 1) * 0.3;
            G.ear = new EarG { u0 = eu0, u1 = eu1, v0 = ev0, v1 = ev1, x0 = hx + eu0 * R, x1 = hx + eu1 * R, y0 = ey + ev0 * R, y1 = ey + ev1 * R };
            G.noseY = 0.6 * L * sp.Nose.Or("len", 1); G.mouthY = 0.93 * L + sp.Mouth.Or("y", 0) + fat * 0.04; G.chinY = 1.38 * L + fat * 0.06;
            // 颈
            double nw = clamp(sp.Or("neck", 1), 0.7, 1.22); // 再粗就比头还宽了
            G.neckPts = Pts(G.P(-0.36 * nw, 1.22 * L, 1), G.P(-0.46 * nw, 1.9), G.P(-0.5 * nw, 2.7, 0), G.P(1.0 * nw, 2.6, 0), G.P(0.96 * nw, 1.7), G.P(0.84 * nw, 0.45));
            G.neckD = spline(G.neckPts, true);
            // 躯干（衣甲共用轮廓）
            double bw = sp.Or("build", 1);
            G.bw = bw; G.nw = nw;
            G.torso = Pts(
                G.P(-0.48 * nw, 1.78), G.P(-1.45 * bw, 2.16), G.P(-2.42 * bw, 2.6, 0.9), G.P(-2.86 * bw, 3.08), G.P(-3.1 * bw, 4.4, 0),
                G.P(3.4 * bw, 4.4, 0), G.P(3.1 * bw, 2.9), G.P(2.62 * bw, 2.3, 0.9), G.P(1.55 * bw, 1.82), G.P(0.92 * nw, 1.5));
            G.chestX = hx - 0.55 * R; // 胸口中线
            return G;
        }
        // 头带 / 冠沿：经度从远侧轮廓到近侧轮廓的可见前半圈，纬度 β(λ) = bm + ba·cosλ
        static List<double[]> band(Geo G, double bm, double ba, double s, int n = 16)
        {
            if (n == 0) n = 16;
            var o = new List<double[]>();
            double l0 = G.lamFar + 0.02, l1 = G.lamNear - 0.02;
            for (int i = 0; i <= n; i++)
            {
                double lam = l0 + (l1 - l0) * i / n;
                o.Add(G.h3(lam, bm + ba * Math.Cos(lam), s));
            }
            return o;
        }
        // 冠顶：band 以上的部分（band 点 + 剪影弧，经由顶部）
        static List<double[]> capOutline(Geo G, List<double[]> bandPts, double s, double topLift = 1)
        {
            var e = G.ell; double rx = G.silRx(s), ry = G.silRy(s) * (topLift == 0 ? 1 : topLift);
            double[] a0 = bandPts[bandPts.Count - 1], a1 = bandPts[0];
            Func<double[], double> ang = p => Math.Atan2(-(p[1] - e.Cy) / ry, (p[0] - e.Cx) / rx);
            double t0 = ang(a0), t1 = ang(a1);
            if (t1 < t0) t1 += Math.PI * 2;
            var arc = Slice(arcPts(e.Cx, e.Cy, rx, ry, t0, t1, 10), 1, -1);
            return Cat(bandPts, arc);
        }

        // ======================================================== 绘制：背景 ==
        static void DrawBackground(Svg S, PortraitOpts o)
        {
            string fc = o.color;
            string c0 = mix(fc, "#3a3446", 0.6), c1 = mix(fc, "#15111c", 0.82), c2 = "#07060a";
            S.defs.Add("<radialGradient id=\"" + S.id("bg") + "\" cx=\"0.4\" cy=\"0.34\" r=\"0.8\"><stop offset=\"0\" stop-color=\"" + c0 + "\"/><stop offset=\"0.55\" stop-color=\"" + c1 + "\"/><stop offset=\"1\" stop-color=\"" + c2 + "\"/></radialGradient>");
            S.bg = "<rect x=\"0\" y=\"0\" width=\"256\" height=\"256\" fill=\"url(#" + S.id("bg") + ")\"/>";
            // 头后光晕（让暗色头发与背景分开）
            S.bg += circle(138, 104, 92) + " fill=\"" + mix(fc, "#ffffff", 0.35) + "\" opacity=\"0.07\"/>";
            S.bg += circle(132, 100, 62) + " fill=\"" + mix(fc, "#ffffff", 0.5) + "\" opacity=\"0.05\"/>";
            if (o.lod >= 1)
            {
                // 淡淡的祥云纹
                string cl = mix(fc, "#ffffff", 0.45);
                var d = new StringBuilder();
                Action<double, double, double> cloud = (x, y, s) =>
                {
                    d.Append("M" + N(x) + "," + N(y) + "c" + N(-8 * s) + ",0 " + N(-12 * s) + "," + N(-10 * s) + " " + N(-4 * s) + "," + N(-14 * s) + "c" + N(6 * s) + "," + N(-3 * s) + " " + N(12 * s) + "," + N(2 * s) + " " + N(9 * s) + "," + N(7 * s) +
                        "M" + N(x) + "," + N(y) + "c" + N(10 * s) + ",0 " + N(16 * s) + "," + N(-12 * s) + " " + N(26 * s) + "," + N(-8 * s) + "c" + N(8 * s) + "," + N(3 * s) + " " + N(6 * s) + "," + N(14 * s) + " " + N(-2 * s) + "," + N(12 * s) + "c" + N(-5 * s) + "," + N(-1 * s) + " " + N(-5 * s) + "," + N(-7 * s) + " 0," + N(-7 * s));
                };
                cloud(36, 70, 1.2); cloud(206, 46, 0.9); cloud(30, 150, 0.8); cloud(214, 132, 1.0);
                S.bg += "<path d=\"" + d + "\" fill=\"none\" stroke=\"" + cl + "\" stroke-width=\"2.2\" opacity=\"0.1\"/>";
            }
        }
        // ======================================================== 绘制：边框 ==
        static void DrawFrame(Svg S, PortraitOpts o)
        {
            string fc = o.color;
            int lo = o.lod;
            S.defs.Add("<linearGradient id=\"" + S.id("fg") + "\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"" + mix(fc, "#ffffff", 0.3) + "\"/><stop offset=\"0.45\" stop-color=\"" + fc + "\"/><stop offset=\"1\" stop-color=\"" + mix(fc, "#000000", 0.5) + "\"/></linearGradient>");
            S.defs.Add("<linearGradient id=\"" + S.id("gg") + "\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#fff1c0\"/><stop offset=\"0.5\" stop-color=\"" + GOLD + "\"/><stop offset=\"1\" stop-color=\"#8a5a1e\"/></linearGradient>");
            var s = new StringBuilder();
            int bw = lo == 0 ? 13 : 11;
            // 外框：势力色带（挖空中间）
            s.Append("<path d=\"M0,0H256V256H0Z M" + bw + "," + bw + "V" + (256 - bw) + "H" + (256 - bw) + "V" + bw + "Z\" fill=\"url(#" + S.id("fg") + ")\" fill-rule=\"evenodd\"/>");
            // 内阴影
            s.Append("<rect x=\"" + (bw + 1) + "\" y=\"" + (bw + 1) + "\" width=\"" + (254 - 2 * bw) + "\" height=\"" + (254 - 2 * bw) + "\" fill=\"none\" stroke=\"#000\" stroke-opacity=\"0.5\" stroke-width=\"4\"/>");
            // 色带上的暗纹（回纹意象）
            if (lo >= 2)
            {
                string dk = mix(fc, "#000", 0.35);
                var d = new StringBuilder();
                double m = bw / 2.0;
                for (int i = 30; i < 230; i += 14)
                {
                    d.Append("M" + i + "," + Js.Num(m - 2) + "h6v4h-3"); d.Append("M" + i + "," + Js.Num(256 - m + 2) + "h6v-4h-3");
                    d.Append("M" + Js.Num(m - 2) + "," + i + "v6h4v-3"); d.Append("M" + Js.Num(256 - m + 2) + "," + i + "v6h-4v-3");
                }
                s.Append("<path d=\"" + d + "\" fill=\"none\" stroke=\"" + dk + "\" stroke-width=\"1.1\" opacity=\"0.55\"/>");
            }
            // 金线
            s.Append("<rect x=\"1.2\" y=\"1.2\" width=\"253.6\" height=\"253.6\" rx=\"3\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"2.4\"/>");
            s.Append("<rect x=\"" + Js.Num(bw - 1.2) + "\" y=\"" + Js.Num(bw - 1.2) + "\" width=\"" + Js.Num(258.4 - 2 * bw) + "\" height=\"" + Js.Num(258.4 - 2 * bw) + "\" fill=\"none\" stroke=\"url(#" + S.id("gg") + ")\" stroke-width=\"" + (lo == 0 ? "3" : "2.4") + "\"/>");
            s.Append("<rect x=\"" + (bw - 3) + "\" y=\"" + (bw - 3) + "\" width=\"" + (262 - 2 * bw) + "\" height=\"" + (262 - 2 * bw) + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"1\" opacity=\"0.7\"/>");
            s.Append("<rect x=\"3.2\" y=\"3.2\" width=\"249.6\" height=\"249.6\" rx=\"2\" fill=\"none\" stroke=\"#fff\" stroke-opacity=\"0.22\" stroke-width=\"1\"/>");
            if (lo >= 1)
            {
                // 四角如意云头
                Action<double, double, double, double> corner = (x, y, sx, sy) =>
                {
                    string t = "translate(" + Js.Num(x) + "," + Js.Num(y) + ") scale(" + Js.Num(sx) + "," + Js.Num(sy) + ")";
                    s.Append("<path transform=\"" + t + "\" d=\"M0,0h22c0,0 -2,6 -8,6c3,3 1,9 -4,9c0,5 -6,7 -9,4c-2,4 -1,3 -1,3z\" fill=\"url(#" + S.id("gg") + ")\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
                    s.Append("<path transform=\"" + t + "\" d=\"M5,5c3,-1 6,1 5,4c-1,2 -4,2 -4,0\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"1.1\" opacity=\"0.8\"/>");
                };
                corner(1.5, 1.5, 1, 1); corner(254.5, 1.5, -1, 1); corner(1.5, 254.5, 1, -1); corner(254.5, 254.5, -1, -1);
                // 上下中央宝珠
                Action<double, double> gem = (x, y) => { s.Append("<path d=\"M" + Js.Num(x) + "," + Js.Num(y - 5) + "l5,5l-5,5l-5,-5z\" fill=\"url(#" + S.id("gg") + ")\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>"); };
                gem(128, bw / 2.0 + 0.5); gem(128, 256 - bw / 2.0 - 0.5);
            }
            S.frame = s.ToString();
            S.win = bw;
        }

        // ======================================================== 绘制：颈 / 头 ==
        internal sealed class Theme { public Tone skin; public string rim; }
        static void DrawNeck(Svg S, Geo G, JObj sp, Theme T)
        {
            var K = T.skin;
            S.shape("neck", G.neckD, K.s, () =>
            {
                // 迎光的颈前侧
                S.path("neck", spline(Pts(G.P(-0.6, 1.1), G.P(-0.44 * G.nw, 1.5), G.P(-0.4 * G.nw, 2.0), G.P(-0.48, 2.8), G.P(-0.05, 2.8, 0), G.P(0.02, 2.0), G.P(-0.05, 1.5)), true), K.b);
                // 下颌投影
                S.path("neck", spline(Pts(G.P(-0.6, 1.25), G.P(-0.1, 1.62), G.P(0.5, 1.6), G.P(1.1, 1.25), G.P(1.1, 0.4, 0), G.P(-0.6, 0.4, 0)), true), K.d, 0, " opacity=\"0.75\"");
                if (!G.fem && S.o.lod >= 1)
                {
                    // 喉结与胸锁乳突肌
                    S.line("neck", spline(Pts(G.P(-0.4 * G.nw, 1.62), G.P(-0.33 * G.nw, 1.7), G.P(-0.38 * G.nw, 1.8))), K.line, 1.2, " opacity=\"0.6\"");
                    S.line("neck", spline(Pts(G.P(0.62, 0.9), G.P(0.4, 1.6), G.P(0.05, 2.1))), K.d, 1.6, " opacity=\"0.5\"");
                }
            }, S.lw, "neck");
            // 轮廓光
            rimOn(S, "neck", G.neckPts, T.rim, 3.2);
        }
        // 轮廓光：形状与其左移副本的偶奇差 → 右缘细月牙
        static void rimOn(Svg S, string layer, List<double[]> pts, string color, double w, double op = 0)
        {
            string d = spline(pts, true), d2 = spline(shift(pts, -w, w * 0.25), true);
            string cu = S.clip("rim" + S.defs.Count, d);
            S.add(layer, "<g clip-path=\"" + cu + "\"><path d=\"" + d + " " + d2 + "\" fill=\"" + color + "\" fill-rule=\"evenodd\" opacity=\"" + Js.Num(op != 0 ? op : 0.85) + "\"/></g>");
        }

        static void DrawHead(Svg S, Geo G, JObj sp, Theme T)
        {
            var K = T.skin; double R = G.R, L = G.L;
            int lo = S.o.lod;
            S.add("head", "<path d=\"" + G.headD + "\" fill=\"" + K.b + "\"/>");
            S.add("head", "<g clip-path=\"" + S.clip("hd", G.headD) + "\">");
            // 1) 主阴影（背光侧脸颊，明暗交界线绕过颧骨下方）
            double fatK = G.fat;
            double hol = sp.Face.Or("hollow", 0);
            double age = sp.D("age");
            // 明暗交界线：年轻、丰润的脸平滑；年长、清瘦的脸在颧骨下内收（骨相）
            double bony = clamp(hol * 0.8 + (age - 28) / 36, 0, 1) * (1 - fatK);
            var term = Pts(
                G.P(0.46, -1.7, 0), G.P(0.42, -0.95), G.P(0.48, -0.55), G.P(0.53, -0.2), G.P(0.56, 0.06),
                G.P(lerp(0.55, 0.5, bony) + fatK * 0.12, 0.3), G.P(lerp(0.5, 0.34 - hol * 0.1, bony) + fatK * 0.12, 0.5), G.P(lerp(0.44, 0.24, bony) + fatK * 0.16, 0.72 * L),
                G.P(lerp(0.36, 0.27, bony) + fatK * 0.12, 0.98 * L), G.P(lerp(0.22, 0.16, bony), 1.2 * L), G.P(-0.04, 1.5 * L, 0), G.P(2, 1.6, 0), G.P(2, -1.7, 0));
            S.add("head", "<path d=\"" + spline(term, true) + "\" fill=\"" + K.s + "\"/>");
            // 远侧颊下凹陷（瘦者、老者）
            if (hol > 0.25) S.add("head", "<path d=\"" + spline(Pts(G.P(-0.98, 0.38), G.P(-0.8, 0.5), G.P(-0.72, 0.8 * L), G.P(-0.86, 0.95 * L), G.P(-1.1, 0.7, 0)), true) + "\" fill=\"" + K.s + "\" opacity=\"" + N(clamp(hol, 0, 0.8)) + "\"/>");
            // 下颌底面
            S.add("head", "<path d=\"" + spline(Pts(G.P(-0.75, 1.28 * L), G.P(-0.3, 1.36 * L), G.P(0.3, 1.16 * L), G.P(0.7, 0.86 * L), G.P(1.2, 1.0, 0), G.P(1.0, 1.8, 0), G.P(-0.8, 1.8, 0)), true) + "\" fill=\"" + K.d + "\" opacity=\"0.55\"/>");
            // 2) 眼窝
            S.add("head", "<path d=\"" + spline(Pts(G.F(0.06, -0.24), G.F(0.3, -0.2), G.F(0.62, -0.18), G.F(0.7, -0.05), G.F(0.5, -0.09), G.F(0.24, -0.04), G.F(0.1, 0.12), G.F(0.02, 0.0)), true) + "\" fill=\"" + K.s + "\" opacity=\"0.9\"/>");
            S.add("head", "<path d=\"" + spline(Pts(G.F(-0.14, -0.2), G.F(-0.42, -0.2), G.F(-0.7, -0.16), G.F(-0.6, -0.06), G.F(-0.36, -0.08), G.F(-0.16, -0.02)), true) + "\" fill=\"" + K.s + "\" opacity=\"0.55\"/>");
            // 3) 鼻侧阴影
            var nt = NoseGeom(G, sp);
            S.add("head", "<path d=\"" + spline(Pts(G.F(0.04, -0.08), G.F(0.12, 0.1), G.F(0.16, 0.36), nt.alaTop, nt.alaBack, G.F(0.06, 0.4 * L), nt.bridgeMid), true) + "\" fill=\"" + K.s + "\"/>");
            S.add("head", "<path d=\"" + spline(Pts(nt.@base, add(nt.@base, 0.14 * R, 0.02 * R), add(nt.alaBot, 0.08 * R, 0.08 * R), add(nt.@base, 0.02 * R, 0.1 * R)), true) + "\" fill=\"" + K.d + "\" opacity=\"0.6\"/>");
            // 4) 高光：额、鼻梁、颧、下巴
            if (lo >= 1)
            {
                S.add("head", ellipse(G.FX(-0.3, -0.62), G.ey - 0.62 * R, 0.3 * R, 0.13 * R, -8) + " fill=\"" + K.h + "\" opacity=\"0.55\"/>");
                S.add("head", "<path d=\"" + taper(Pts(G.F(-0.06, -0.05), G.F(-0.04, 0.2, 0.1), G.F(-0.05, 0.42, 0.22)), t => 0.06 * R * Math.Sin(Math.PI * t) + 0.3) + "\" fill=\"" + K.h + "\" opacity=\"0.8\"/>");
                S.add("head", ellipse(G.FX(-0.72, 0.2), G.ey + 0.22 * R, 0.13 * R, 0.07 * R, -30) + " fill=\"" + K.h + "\" opacity=\"0.55\"/>");
                S.add("head", ellipse(G.FX(-0.12, 1.22 * L, 0.08), G.ey + 1.24 * L * R, 0.12 * R, 0.06 * R, -10) + " fill=\"" + K.h + "\" opacity=\"0.5\"/>");
            }
            // 5) 腮红
            string bl = N(clamp((G.fem ? 0.32 : age > 55 ? 0.12 : 0.2) * sp.Or("blush", 1) * (S.o.mood == "angry" ? 2.2 : 1), 0, 0.7));
            S.add("head", ellipse(G.FX(0.3, 0.42), G.ey + 0.42 * R, 0.26 * R, 0.13 * R, -10) + " fill=\"" + K.blush + "\" opacity=\"" + bl + "\"/>");
            // N(bl * 0.8)：bl 为字符串，JS 里乘法先转回数值
            S.add("head", ellipse(G.FX(-0.7, 0.42), G.ey + 0.42 * R, 0.12 * R, 0.1 * R) + " fill=\"" + K.blush + "\" opacity=\"" + N(double.Parse(bl, CI) * 0.8) + "\"/>");
            if (fatK > 0.45)
            {
                // 双下巴：下颌之下再垂一层肉
                var dc = Pts(G.P(-0.66 - fatK * 0.1, 1.05 * L), G.P(-0.52, 1.5 * L + fatK * 0.12), G.P(-0.12, 1.68 * L + fatK * 0.14), G.P(0.4, 1.52 * L + fatK * 0.1), G.P(0.72, 1.05 * L, 0));
                string dcD = spline(dc, true);
                S.add("body2", "<path d=\"" + dcD + "\" fill=\"" + K.s + "\"/><g clip-path=\"" + S.clip("dchin", dcD) + "\"><path d=\"" + spline(Pts(G.P(-0.9, 1.0), G.P(-0.5, 1.45 * L), G.P(-0.15, 1.58 * L), G.P(-0.1, 1.2 * L)), true) + "\" fill=\"" + K.b + "\" opacity=\"0.8\"/></g>" +
                    "<path d=\"" + spline(Slice(dc, 0, 4)) + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.85) + "\"/>");
            }
            if (fatK > 0.35 && lo >= 1)
            {
                S.add("head", "<path d=\"" + spline(Pts(G.P(-0.62 * G.cw, 1.3 * L + fatK * 0.05), G.P(-0.3, 1.42 * L + fatK * 0.04), G.P(0.2, 1.3 * L + fatK * 0.06))) + "\" fill=\"none\" stroke=\"" + K.line + "\" stroke-width=\"1.3\" opacity=\"" + N(clamp(fatK, 0, 0.8)) + "\"/>");
                S.add("head", "<path d=\"" + spline(Pts(G.P(0.42, 0.62), G.P(0.5, 0.95 * L), G.P(0.36, 1.18 * L))) + "\" fill=\"none\" stroke=\"" + K.line + "\" stroke-width=\"1.1\" opacity=\"" + N(clamp(fatK * 0.7, 0, 0.6)) + "\"/>");
            }
            S.add("head", "%HEADSHADE%");
            S.add("head", "</g>");
            S.add("head", "<path d=\"" + G.headD + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw) + "\"/>");
            rimOn(S, "head", G.headPts, T.rim, 2.6, 0.8);
        }

        // ---------------------------------------------------------------- 耳 --
        static void DrawEar(Svg S, Geo G, JObj sp, Theme T)
        {
            if (G.hideEar) return;
            var K = T.skin;
            double x0 = G.ear.u0, x1 = G.ear.u1, y0 = G.ear.v0, y1 = G.ear.v1;
            double w = x1 - x0, h = y1 - y0;
            Func<double, double, double[]> E = (u, v) => G.P(x0 + u * w, y0 + v * h);
            var outer = Pts(E(0.08, 0.12), E(0.45, -0.02), E(0.92, 0.12), E(1.0, 0.42), E(0.85, 0.72), E(0.62, 0.92), E(0.38, 1.0), E(0.16, 0.9), E(0.08, 0.7), E(-0.05, 0.4));
            string d = spline(outer, true);
            S.shape("ear", d, K.s, () =>
            {
                S.path("ear", spline(Pts(E(0.25, 0.22), E(0.6, 0.18), E(0.78, 0.4), E(0.62, 0.62), E(0.4, 0.68), E(0.3, 0.5)), true), K.d);
                S.path("ear", spline(Pts(E(0.18, 0.12), E(0.5, 0.03), E(0.86, 0.16), E(0.94, 0.42), E(0.8, 0.7), E(0.88, 0.42), E(0.78, 0.2), E(0.5, 0.12)), true), K.b, 0, " opacity=\"0.9\"");
                S.path("ear", spline(Pts(E(0.35, 0.78), E(0.6, 0.86), E(0.45, 0.96), E(0.25, 0.9)), true), K.b, 0, " opacity=\"0.6\"");
            }, S.lw * 0.8);
            S.line("ear", spline(Pts(E(0.36, 0.3), E(0.62, 0.32), E(0.62, 0.55), E(0.45, 0.7))), K.line, 1.1, " opacity=\"0.8\"");
        }

        // ---------------------------------------------------------------- 眼 --
        internal sealed class EyeShape
        {
            public double w, h, tilt, low, iris, crease, angular, round, heavy, smile, lash, innerDrop, squint;
            public EyeShape Clone() { return (EyeShape)MemberwiseClone(); }
        }
        static readonly Dictionary<string, EyeShape> EYE = new Dictionary<string, EyeShape>
        {
            { "normal", new EyeShape { w = 1, h = 0.17, tilt = 0.06, low = 0.42, iris = 0.82, crease = 1 } },
            { "sharp", new EyeShape { w = 1.02, h = 0.14, tilt = 0.22, low = 0.32, iris = 0.86, crease = 0.8, angular = 1 } },
            { "phoenix", new EyeShape { w = 1.14, h = 0.12, tilt = 0.42, low = 0.28, iris = 0.92, crease = 0.6, angular = 1 } },
            { "round", new EyeShape { w = 1.0, h = 0.24, tilt = 0.04, low = 0.6, iris = 0.5, crease = 1.2, round = 1 } },
            { "narrow", new EyeShape { w = 1.04, h = 0.1, tilt = 0.12, low = 0.25, iris = 1.0, crease = 0.4, heavy = 1 } },
            { "gentle", new EyeShape { w = 1.0, h = 0.15, tilt = -0.12, low = 0.18, iris = 0.92, crease = 1, smile = 1 } },
            { "big", new EyeShape { w = 1.1, h = 0.2, tilt = 0.14, low = 0.5, iris = 0.78, crease = 1.15, lash = 1 } },
            { "sleepy", new EyeShape { w = 1.0, h = 0.12, tilt = -0.06, low = 0.35, iris = 0.95, crease = 0.9, heavy = 1 } },
        };
        static EyeShape EyeMood(EyeShape bs, string mood, bool isNear, double fat)
        {
            var e = bs.Clone();
            e.innerDrop = 0; e.squint = 1;
            if (fat > 0.4) { e.h *= 1 - (fat - 0.4) * 0.45; e.low *= 0.6; }
            if (mood == "angry") { e.innerDrop = 0.62; e.h *= 0.95; e.iris = Math.Min(e.iris, 0.68); }
            else if (mood == "hurt") { e.h *= isNear ? 0.22 : 0.7; e.innerDrop = -0.3; }
            else if (mood == "win") { e.smile = 1; e.low = Math.Min(e.low, 0.15); e.h *= 0.85; }
            return e;
        }
        static void DrawEyes(Svg S, Geo G, JObj sp, Theme T, string mood)
        {
            EyeShape bs; if (!EYE.TryGetValue(sp.Eyes.S("shape") ?? "", out bs)) bs = EYE["normal"];
            bool patch = sp.Includes("acc", "eyepatch");
            foreach (var which in new[] { "F", "N" })
            {
                var g = which == "N" ? G.eyeN : G.eyeF;
                var ex = EyeMood(bs, mood, which == "N", sp.Or("fat", 0));
                if (which == "N" && patch) { DrawEyePatch(S, G, sp, T, g); continue; }
                DrawEye(S, G, sp, T, g, ex, which == "F");
            }
        }
        static void DrawEye(Svg S, Geo G, JObj sp, Theme T, EyeG g, EyeShape ex, bool far)
        {
            var K = T.skin; double R = G.R; int lo = S.o.lod;
            double dir = g.dir, w = g.w * ex.w, h = ex.h * R * sp.Eyes.Or("size", 1);
            double cy = G.ey + G.eyeDy + (far ? 0.01 * R : 0);
            double cx = g.cx;
            var inner = V(cx - dir * w / 2, cy + h * 0.12);
            var outer = V(cx + dir * w / 2, cy - h * ex.tilt * 1.6);
            Func<double, double, double[]> at = (t, dy) => { var p = lp(inner, outer, t); return V(p[0], p[1] + dy); };
            double up = ex.round != 0 ? 1.05 : 0.95;
            double drop = ex.innerDrop * h;
            var Up = Pts(inner, at(0.18, -h * up * 0.72 + drop * 0.8), at(0.45, -h * up + drop * 0.35), at(0.78, -h * up * (ex.angular != 0 ? 0.95 : 0.8)), outer);
            var D = Pts(outer, at(0.72, h * ex.low * 1.1), at(0.4, h * ex.low * (ex.smile != 0 ? 0.4 : 1.0)), at(0.12, h * ex.low * 0.5), inner);
            if (ex.smile != 0) { D[1] = at(0.7, h * ex.low * 0.2 - h * 0.12); D[2] = at(0.4, -h * 0.05); }
            string eyeD = spline(Cat(Up, Slice(D, 1, -1)), true);
            string cid = "eye" + (far ? "f" : "n");
            S.add("face", "<path d=\"" + eyeD + "\" fill=\"#f1eadf\"/>");
            S.add("face", "<g clip-path=\"" + S.clip(cid, eyeD) + "\">");
            // 上睑投影
            S.add("face", "<path d=\"" + spline(shift(Up, 0, h * 0.4), false) + "L" + ps(add(outer, 0, -h * 2)) + "L" + ps(add(inner, 0, -h * 2)) + "Z\" fill=\"#b8a496\"/>");
            // 虹膜
            double ri = h * ex.iris * (ex.round != 0 ? 1.42 : 0.9) * (far ? 0.92 : 1);
            double ix = cx + dir * w * 0.02 + (far ? w * 0.06 : w * 0.04), iy = cy - h * (ex.round != 0 ? 0.06 : 0.02);
            string ic = sp.Eyes.Or("color", "#2a1810");
            double iw = far ? 0.78 : 0.94;
            S.add("face", ellipse(ix, iy, ri * iw, ri) + " fill=\"" + mix(ic, "#7a5a40", 0.25) + "\"/>");
            S.add("face", ellipse(ix, iy - ri * 0.25, ri * iw * 0.95, ri * 0.75) + " fill=\"" + mul(ic, 0.6) + "\"/>");
            S.add("face", ellipse(ix, iy, ri * iw * 0.5, ri * 0.52) + " fill=\"#0a0608\"/>");
            S.add("face", ellipse(ix, iy, ri * iw, ri) + " fill=\"none\" stroke=\"" + mul(ic, 0.4) + "\" stroke-width=\"0.9\"/>");
            S.add("face", ellipse(ix - ri * 0.38, iy - ri * 0.4, ri * 0.28, ri * 0.24) + " fill=\"#fff\" opacity=\"0.92\"/>");
            if (lo >= 1) S.add("face", circle(ix + ri * 0.35, iy + ri * 0.35, ri * 0.12) + " fill=\"#fff\" opacity=\"0.7\"/>");
            S.add("face", "</g>");
            // 双眼皮褶 / 上睑
            double lwk = S.lw / 2.6;
            if (ex.crease > 0.3 && !(ex.heavy != 0 && sp.Eyes.D("lid") > 0.8))
            {
                var crs = shift(Slice(Up, 1, 4), dir * w * 0.02, -h * (0.42 + ex.crease * 0.18));
                S.line("face", spline(crs), K.line, 1.0 * lwk, " opacity=\"0.65\"");
            }
            if (ex.heavy != 0)
            {
                // 厚重眼睑：上睑压低
                S.add("face", "<path d=\"" + spline(Up, false) + "L" + ps(add(outer, 0, -h * 0.7)) + "L" + ps(at(0.45, -h * 1.5)) + "L" + ps(add(inner, 0, -h * 0.6)) + "Z\" fill=\"" + K.s + "\"/>");
            }
            // 上睑线（粗）
            bool fem = sp.S("sex") == "f";
            double lidW = (fem ? 3.3 : 2.3) * lwk;
            var ext = add(outer, dir * w * (fem ? 0.22 : 0.08), fem ? -h * 0.55 : h * 0.12);
            var lid = Cat(Up, ext);
            S.add("face", "<path d=\"" + taper(lid, t => (t < 0.15 ? lerp(0.5, 1.4, t / 0.15) : t > 0.85 ? lerp(lidW, 0.2, (t - 0.85) / 0.15) : lerp(1.4, lidW, (t - 0.15) / 0.7))) + "\" fill=\"" + INK + "\"/>");
            if (fem && lo >= 1)
            {
                // 睫毛
                var ld = new StringBuilder();
                for (int i = 0; i < 3; i++) { var p = lp(Up[2], outer, 0.35 + i * 0.3); ld.Append("M" + ps(p) + "l" + N(dir * w * (0.06 + i * 0.03)) + "," + N(-h * (0.45 + i * 0.12))); }
                S.line("face", ld.ToString(), INK, 1.1 * lwk);
            }
            // 下睑
            S.line("face", spline(Slice(D, 0, 3)), fem ? INK : K.line, (fem ? 1.4 : 1.0) * lwk, fem ? " opacity=\"0.55\"" : " opacity=\"0.75\"");
            // 眼袋 / 鱼尾纹（年长）
            double age = sp.D("age");
            if (age >= 44 && lo >= 1)
            {
                double a = clamp((age - 40) / 30, 0.3, 0.9);
                S.line("face", spline(Pts(at(0.15, h * 0.9), at(0.5, h * 1.5), at(0.85, h * 1.0))), K.line, 0.9 * lwk, " opacity=\"" + N(a) + "\"");
                if (age >= 52)
                {
                    for (int i = 0; i < 2; i++) S.line("face", spline(Pts(add(outer, dir * w * 0.12, -h * 0.2 + i * h * 0.6), add(outer, dir * w * 0.3, -h * 0.4 + i * h * 0.9))), K.line, 0.8 * lwk, " opacity=\"" + N(a * 0.8) + "\"");
                }
            }
        }
        static void DrawEyePatch(Svg S, Geo G, JObj sp, Theme T, EyeG g)
        {
            double R = G.R, cx = g.cx, cy = G.ey;
            // 系带
            S.line("face", spline(Pts(V(G.FX(-0.9, -0.62), G.ey - 0.62 * R), V(cx, cy - 0.3 * R), V(G.hx + 0.95 * R, G.ey - 0.08 * R))), INK, 2.6);
            S.line("face", spline(Pts(V(G.FX(-0.9, -0.62), G.ey - 0.62 * R), V(cx, cy - 0.3 * R), V(G.hx + 0.95 * R, G.ey - 0.08 * R))), "#3a2a22", 1.4);
            string d = spline(Pts(V(cx - 0.28 * R, cy - 0.14 * R), V(cx + 0.02 * R, cy - 0.24 * R), V(cx + 0.3 * R, cy - 0.12 * R), V(cx + 0.26 * R, cy + 0.16 * R), V(cx - 0.02 * R, cy + 0.24 * R), V(cx - 0.24 * R, cy + 0.12 * R)), true);
            S.shape("face", d, "#2a201c", () =>
            {
                S.add("face", ellipse(cx - 0.06 * R, cy - 0.06 * R, 0.16 * R, 0.09 * R, -12) + " fill=\"#5a4a40\" opacity=\"0.8\"/>");
            }, S.lw * 0.85);
        }

        // ---------------------------------------------------------------- 眉 --
        internal sealed class BrowShape { public double y0, ym, y1; public double[] w; public bool sharp, bushy, knit; }
        static readonly Dictionary<string, BrowShape> BROW = new Dictionary<string, BrowShape>
        {
            { "straight", new BrowShape { y0 = -0.27, ym = -0.33, y1 = -0.3, w = new[] { 0.09, 0.08, 0.04 } } },
            { "arched", new BrowShape { y0 = -0.26, ym = -0.38, y1 = -0.28, w = new[] { 0.07, 0.07, 0.03 } } },
            { "angled", new BrowShape { y0 = -0.24, ym = -0.36, y1 = -0.42, w = new[] { 0.11, 0.09, 0.03 }, sharp = true } },
            { "bushy", new BrowShape { y0 = -0.26, ym = -0.35, y1 = -0.3, w = new[] { 0.14, 0.13, 0.07 }, bushy = true } },
            { "thin", new BrowShape { y0 = -0.28, ym = -0.37, y1 = -0.3, w = new[] { 0.05, 0.045, 0.015 } } },
            { "knit", new BrowShape { y0 = -0.2, ym = -0.33, y1 = -0.36, w = new[] { 0.13, 0.1, 0.04 }, knit = true } },
            { "silkworm", new BrowShape { y0 = -0.27, ym = -0.42, y1 = -0.36, w = new[] { 0.08, 0.15, 0.05 }, sharp = true } },
            { "sword", new BrowShape { y0 = -0.25, ym = -0.36, y1 = -0.46, w = new[] { 0.1, 0.1, 0.02 }, sharp = true } },
            { "worried", new BrowShape { y0 = -0.36, ym = -0.36, y1 = -0.26, w = new[] { 0.08, 0.07, 0.03 } } },
        };
        static void DrawBrows(Svg S, Geo G, JObj sp, Theme T, string mood)
        {
            BrowShape b; if (!BROW.TryGetValue(sp.Brows.S("shape") ?? "", out b)) b = BROW["straight"];
            double th = sp.Brows.Or("thick", 1);
            double inD = 0;
            string expr = sp.S("expr");
            if (mood == "angry") inD = 0.17;
            else if (mood == "hurt") inD = -0.15;
            else if (expr == "fierce") inD = 0.05;
            else if (mood == "win") inD = -0.07;
            string col = mix(sp.Brows.Or("color", "#1a1414"), INK, 0.15);
            bool old = sp.D("age") >= 58;
            foreach (int side in new[] { -1, 1 })
            {
                double u0 = side * 0.08, u1 = side * 0.82, um = side * 0.5;
                double by = sp.Brows.Or("y", 0) + sp.Eyes.Or("y", 0) * 0.8;
                Func<double, double, double[]> yy = (y, u) => V(G.FX(u, y), G.ey + (y + by + (Math.Abs(u) < 0.2 ? inD : 0)) * G.R);
                var pts = Pts(yy(b.y0, u0), yy(lerp(b.y0, b.ym, 0.7), side * 0.28), yy(b.ym, um), yy(b.y1, u1));
                double sc = side > 0 ? 1 : 0.85;
                var ws = new double[3]; for (int i = 0; i < 3; i++) ws[i] = b.w[i] * G.R * th * sc;
                Func<double, double> wf = t => (t < 0.5 ? lerp(ws[0], ws[1], t * 2) : lerp(ws[1], ws[2], (t - 0.5) * 2) * (b.sharp && t > 0.9 ? (1 - t) * 10 : 1));
                string d = taper(pts, wf);
                S.add("face", "<path d=\"" + d + "\" fill=\"" + col + "\"/>");
                if ((b.bushy || old) && S.o.lod >= 1)
                {
                    // 眉毛的毛流
                    var hd = new StringBuilder();
                    for (int i = 0; i < 7; i++)
                    {
                        double t = (i + 0.5) / 7; int fi = (int)Math.Floor(t * 3);
                        var p = lp(pts[fi], pts[Math.Min(3, fi + 1)], (t * 3) % 1);
                        double wv = wf(t);
                        hd.Append("M" + ps(p[0] - side * 1, p[1] + wv * 0.5) + "L" + ps(p[0] + side * 3.2, p[1] - wv * 0.9));
                    }
                    S.line("face", hd.ToString(), old ? mix(col, "#fff", 0.4) : col, 1.2);
                }
            }
            if ((b.knit || mood == "angry" || expr == "fierce") && S.o.lod >= 1)
            {
                // 眉间竖纹
                double x = G.FX(0, -0.2), y = G.ey - 0.22 * G.R;
                S.line("face", "M" + N(x - 1) + "," + N(y - 4) + "q1.5,4 0,8M" + N(x + 3) + "," + N(y - 3) + "q1,3 0,6", T.skin.line, 1.1, " opacity=\"0.7\"");
            }
        }

        // ---------------------------------------------------------------- 鼻 --
        internal sealed class NoseShape { public double len, dz, hook, tip, w; }
        static readonly Dictionary<string, NoseShape> NOSE = new Dictionary<string, NoseShape>
        {
            { "straight", new NoseShape { len = 1, dz = 0.32, hook = 0, tip = 1, w = 1 } },
            { "aquiline", new NoseShape { len = 1.06, dz = 0.4, hook = 1, tip = 0.95, w = 1 } },
            { "broad", new NoseShape { len = 0.96, dz = 0.28, hook = 0, tip = 1.25, w = 1.25 } },
            { "button", new NoseShape { len = 0.9, dz = 0.24, hook = -0.3, tip = 1.1, w = 0.95 } },
            { "small", new NoseShape { len = 0.86, dz = 0.24, hook = -0.2, tip = 0.85, w = 0.85 } },
            { "bulb", new NoseShape { len = 1.0, dz = 0.34, hook = 0.2, tip = 1.5, w = 1.3 } },
            { "flat", new NoseShape { len = 0.9, dz = 0.2, hook = -0.2, tip = 1.3, w = 1.4 } },
        };
        static NoseG NoseGeom(Geo G, JObj sp)
        {
            NoseShape n; if (!NOSE.TryGetValue(sp.Nose.S("shape") ?? "", out n)) n = NOSE["straight"];
            double s = sp.Nose.Or("size", 1);
            double ny = G.noseY * n.len;
            double dz = n.dz * s * (sp.Nose.Has("bridge") ? 0.75 + sp.Nose.D("bridge") * 0.5 : 1);
            double nw = sp.Nose.Or("w", 1) * n.w;
            return new NoseG
            {
                n = n, s = s, ny = ny, dz = dz, nw = nw,
                top = G.F(0.0, -0.12, 0.05),
                tip = G.F(-0.02, ny - 0.04, dz),
                bridgeMid = G.F(0.0, ny * 0.45, dz * 0.55 + n.hook * 0.07),
                @base = G.F(0.02, ny + 0.06, dz * 0.3),
                alaTop = G.F(0.14 * nw, ny - 0.1, 0.06),
                alaBack = G.F(0.21 * nw, ny + 0.0, 0.0),
                alaBot = G.F(0.15 * nw, ny + 0.07, 0.04),
                nostril = G.F(0.06 * nw, ny + 0.05, dz * 0.22),
            };
        }
        static void DrawNose(Svg S, Geo G, JObj sp, Theme T)
        {
            var K = T.skin; double R = G.R; int lo = S.o.lod;
            var g = NoseGeom(G, sp);
            double lwk = S.lw / 2.6;
            // 鼻梁远侧轮廓线（下半段为主）
            var br = G.fem ? Pts(lp(g.bridgeMid, g.tip, 0.35), lp(g.bridgeMid, g.tip, 0.75), add(g.tip, -0.01 * R, 0.0)) : Pts(lp(g.top, g.bridgeMid, 0.45), g.bridgeMid, lp(g.bridgeMid, g.tip, 0.6), add(g.tip, -0.01 * R, 0.0));
            S.add("face", "<path d=\"" + taper(br, t => lerp(0.2, (G.fem ? 1.4 : 1.9) * lwk, t * t)) + "\" fill=\"" + K.line + "\" opacity=\"" + (G.fem ? "0.6" : "0.85") + "\"/>");
            // 鼻尖与鼻底
            double tipR = 0.09 * R * g.n.tip;
            var tipCurve = Pts(add(g.tip, -0.005 * R, -tipR * 0.6), add(g.tip, -tipR * 0.35, tipR * 0.25), add(g.tip, tipR * 0.3, tipR * 0.75), lp(g.tip, g.@base, 0.7), g.@base);
            S.add("face", "<path d=\"" + taper(tipCurve, t => 1.9 * lwk * (1 - t * 0.55)) + "\" fill=\"" + INK + "\" opacity=\"0.9\"/>");
            // 鼻翼
            S.line("face", spline(Pts(g.alaTop, add(g.alaBack, 0.02 * R, -0.02 * R), g.alaBot, add(g.nostril, 0.04 * R, 0.03 * R))), K.line, 1.5 * lwk);
            // 鼻孔
            S.add("face", ellipse(g.nostril[0], g.nostril[1], 0.055 * R * g.nw, 0.03 * R, 18) + " fill=\"" + K.d + "\"/>");
            S.add("face", ellipse(g.nostril[0] + 0.01 * R, g.nostril[1] + 0.005 * R, 0.035 * R * g.nw, 0.018 * R, 18) + " fill=\"" + INK + "\" opacity=\"0.85\"/>");
            // 鼻尖高光
            if (lo >= 1) S.add("face", ellipse(g.tip[0] - 0.02 * R, g.tip[1] - 0.04 * R, 0.035 * R * g.n.tip, 0.025 * R, -20) + " fill=\"" + K.h + "\" opacity=\"0.9\"/>");
            G.noseG = g;
        }

        // ---------------------------------------------------------------- 口 --
        static readonly Dictionary<string, double> MOUTH_CDY = new Dictionary<string, double> { { "neutral", 0.0 }, { "smile", -0.06 }, { "grim", 0.05 }, { "shout", 0.06 }, { "clench", 0.03 }, { "grin", -0.08 }, { "smirk", 0 } };
        static void DrawMouth(Svg S, Geo G, JObj sp, Theme T, string mood)
        {
            var K = T.skin; double R = G.R; int lo = S.o.lod;
            double lwk = S.lw / 2.6;
            double my = G.mouthY, mw = sp.Mouth.Or("w", 1);
            string shape = sp.Mouth.Or("shape", "neutral");
            string expr = sp.S("expr");
            if (expr == "smile") shape = "smile";
            if (expr == "sly") shape = "smirk";
            if (expr == "stern" || expr == "fierce") shape = "grim";
            if (mood == "angry") shape = "shout";
            else if (mood == "hurt") shape = "clench";
            else if (mood == "win") shape = "grin";
            double cdy; if (!MOUTH_CDY.TryGetValue(shape, out cdy)) cdy = 0;
            double[] cf = G.F(-0.34 * mw, my + cdy, 0.05), cn = G.F(0.34 * mw, my + cdy, 0.0);
            double[] cc = G.F(-0.02, my - 0.005, 0.12);
            bool fem = G.fem;
            double lipT = sp.Mouth.Or("lips", 1) * 0.1 * R;
            string lipCol = fem ? mix(K.b, "#c23848", 0.55) : K.lip;
            double age = sp.D("age");
            if (shape == "shout" || shape == "grin" || shape == "clench")
            {
                double open = shape == "shout" ? 0.4 : shape == "grin" ? 0.19 : 0.11;
                var top = Pts(cf, G.F(-0.18 * mw, my - 0.05, 0.1), G.F(0, my - 0.05, 0.12), G.F(0.2 * mw, my - 0.04, 0.06), cn);
                var bot = Pts(cn, G.F(0.18 * mw, my + open * 0.8, 0.06), G.F(0, my + open, 0.12), G.F(-0.18 * mw, my + open * 0.85, 0.1));
                string md = spline(Cat(top, bot), true);
                S.add("face", "<path d=\"" + md + "\" fill=\"#3a1016\"/>");
                S.add("face", "<g clip-path=\"" + S.clip("mo", md) + "\">");
                S.add("face", "<path d=\"" + spline(shift(top, 0, 0.07 * R), false) + "L" + ps(add(cn, 0, -0.1 * R)) + "L" + ps(add(cf, 0, -0.1 * R)) + "Z\" fill=\"#efe6d8\"/>");
                if (shape != "shout") S.add("face", "<path d=\"" + spline(shift(bot, 0, -0.06 * R), false) + "L" + ps(add(cf, 0, 0.2 * R)) + "L" + ps(add(cn, 0, 0.2 * R)) + "Z\" fill=\"#e2d8c8\"/>");
                else S.add("face", ellipse(G.FX(0, my + open * 0.75, 0.1), G.ey + (my + open * 0.75) * R, 0.16 * R, 0.07 * R) + " fill=\"#a8404a\"/>");
                if (lo >= 1)
                {
                    var td = new StringBuilder();
                    foreach (var u in new[] { -0.12, 0.04, 0.18 }) td.Append("M" + ps(G.F(u * mw, my - 0.05, 0.1)) + "L" + ps(G.F(u * mw, my + 0.03, 0.1)));
                    S.line("face", td.ToString(), "#b0a090", 0.8);
                }
                S.add("face", "</g>");
                S.add("face", "<path d=\"" + md + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(1.6 * lwk) + "\"/>");
                // 下唇
                S.line("face", spline(Pts(G.F(-0.18 * mw, my + open + 0.09, 0.08), G.F(0.0, my + open + 0.11, 0.1), G.F(0.16 * mw, my + open + 0.08, 0.04))), K.line, 1.1 * lwk, " opacity=\"0.7\"");
            }
            else
            {
                var mid = shape == "smirk" ? G.F(0.16 * mw, my - 0.03, 0.06) : G.F(0.17 * mw, my + cdy * 0.3, 0.06);
                var line = Pts(cf, G.F(-0.17 * mw, my + cdy * 0.3 + 0.01, 0.1), cc, mid, cn);
                // 上唇（阴影面）
                var ul = Pts(cf, G.F(-0.16 * mw, my - lipT / R * 0.9, 0.1), G.F(-0.03, my - lipT / R * 0.7, 0.13), G.F(0.02, my - lipT / R * 0.95, 0.12), G.F(0.2 * mw, my - lipT / R * 0.8, 0.05), cn);
                S.add("face", "<path d=\"" + spline(Cat(ul, Rev(Slice(line, 1, -1))), true) + "\" fill=\"" + (fem ? mul(lipCol, 0.8) : K.lip) + "\" opacity=\"" + (fem ? "1" : "0.75") + "\"/>");
                // 下唇
                var ll = Pts(cn, G.F(0.16 * mw, my + lipT / R * 1.3, 0.05), G.F(-0.04, my + lipT / R * 1.5, 0.12), G.F(-0.2 * mw, my + lipT / R * 1.1, 0.09), cf);
                S.add("face", "<path d=\"" + spline(Cat(Slice(line, 1, -1), ll), true) + "\" fill=\"" + (fem ? lipCol : mix(K.b, K.lip, 0.45)) + "\"/>");
                if (lo >= 1) S.add("face", ellipse(G.FX(-0.06, my + 0.09, 0.1), G.ey + (my + lipT / R * 0.8) * R, 0.09 * R, 0.025 * R, -6) + " fill=\"" + K.h + "\" opacity=\"" + (fem ? "0.75" : "0.5") + "\"/>");
                // 唇下阴影
                S.add("face", "<path d=\"" + spline(Pts(G.F(-0.16 * mw, my + 0.19, 0.08), G.F(0, my + 0.22, 0.1), G.F(0.14 * mw, my + 0.19, 0.04), G.F(0.02, my + 0.27, 0.1)), true) + "\" fill=\"" + K.s + "\" opacity=\"0.6\"/>");
                // 口缝
                S.add("face", "<path d=\"" + taper(line, t => (0.5 + 1.5 * Math.Sin(Math.PI * clamp(t * 1.1, 0, 1))) * lwk) + "\" fill=\"" + INK + "\"/>");
                // 口角
                S.line("face", spline(Pts(add(cn, -0.02 * R, -0.01 * R), add(cn, 0.04 * R, (cdy > 0 ? 0.05 : -0.03) * R))), K.line, 1.1 * lwk);
            }
            // 人中
            if (lo >= 1) S.line("face", spline(Pts(G.F(0.04, G.noseY + 0.12, 0.2), G.F(0.05, my - 0.12, 0.13))), K.line, 0.9 * lwk, " opacity=\"0.45\"");
            // 法令纹
            double nl = age >= 30 ? clamp((age - 26) / 30, 0.15, 0.8) : (shape == "grin" || shape == "shout" ? 0.3 : shape == "smile" && age >= 24 ? 0.2 : 0);
            if (fem) nl = age >= 45 ? nl * 0.5 : 0;
            if (nl > 0 && lo >= 1)
            {
                var ng = G.noseG;
                S.line("face", spline(Pts(add(ng.alaBack, 0.04 * R, -0.04 * R), G.F(0.42, G.noseY + 0.2, 0), G.F(0.46 * mw, my + 0.08, 0), G.F(0.4 * mw, my + 0.3, 0))), K.line, 1.2 * lwk, " opacity=\"" + N(nl) + "\"");
                if (age >= 45) S.line("face", spline(Pts(G.F(-0.36, G.noseY + 0.1, 0.05), G.F(-0.52, my, 0.03), G.F(-0.48, my + 0.22, 0.02))), K.line, 1.0 * lwk, " opacity=\"" + N(nl * 0.7) + "\"");
            }
            // 下巴沟
            if (lo >= 1 && !fem && age >= 40) S.line("face", spline(Pts(G.F(-0.16, G.chinY - 0.2, 0.08), G.F(-0.06, G.chinY - 0.17, 0.1), G.F(0.04, G.chinY - 0.19, 0.08))), K.line, 0.8 * lwk, " opacity=\"0.3\"");
        }
        // 皱纹（额头等）
        static void DrawWrinkles(Svg S, Geo G, JObj sp, Theme T)
        {
            double age = sp.D("age");
            if (age < 46 || S.o.lod < 1) return;
            double a = clamp((age - 42) / 28, 0.25, 0.8); var K = T.skin;
            var d = new StringBuilder();
            for (int i = 0; i < (age > 60 ? 3 : 2); i++)
            {
                double y = -0.56 - i * 0.12;
                d.Append(spline(Pts(G.F(-0.62, y + 0.03), G.F(-0.3, y - 0.02), G.F(0.0, y + 0.01), G.F(0.28, y - 0.01))));
            }
            S.headShade.Add("<path d=\"" + d + "\" fill=\"none\" stroke=\"" + K.line + "\" stroke-width=\"1\" opacity=\"" + N(a) + "\"/>");
        }
        // 疤痕与刺青等
        static void DrawMarks(Svg S, Geo G, JObj sp, Theme T, string mood)
        {
            double R = G.R;
            foreach (var mo in sp.L("marks") ?? new List<object>())
            {
                string m = mo as string;
                if (m == "scar") S.line("face", spline(Pts(G.F(-0.7, 0.1), G.F(-0.6, 0.35), G.F(-0.5, 0.6))), "#a04848", 1.6, " opacity=\"0.8\"");
                if (m == "mole") S.add("face", circle(G.FX(-0.3, 0.75), G.ey + 0.72 * R, 1.3) + " fill=\"" + T.skin.line + "\"/>");
                if (m == "scars")
                {
                    S.line("face", spline(Pts(G.F(0.1, -0.7), G.F(0.3, -0.45), G.F(0.42, -0.2))) + spline(Pts(G.F(-0.62, 0.45), G.F(-0.42, 0.62))) + spline(Pts(G.F(0.3, 0.6), G.F(0.45, 0.95))), "#a04848", 1.5, " opacity=\"0.8\"");
                    S.line("body2", spline(Pts(G.P(0.6, 2.4), G.P(1.4, 2.9))) + spline(Pts(G.P(-1.6, 2.6), G.P(-1.0, 3.2))), "#a04848", 1.6, " opacity=\"0.7\"");
                }
                if (m == "scales" && S.o.lod >= 1)
                {
                    var d = new StringBuilder();
                    for (int r = 0; r < 3; r++) for (int i = 0; i < 4; i++) { var p = G.F(-0.75 + i * 0.12 + (r % 2) * 0.06, 0.15 + r * 0.14); d.Append("M" + N(p[0] - 2.5) + "," + N(p[1]) + "q2.5,3 5,0"); }
                    for (int r = 0; r < 3; r++) for (int i = 0; i < 4; i++) { var p = G.P(0.2 + i * 0.2 + (r % 2) * 0.1, 1.3 + r * 0.18); d.Append("M" + N(p[0] - 3) + "," + N(p[1]) + "q3,3.5 6,0"); }
                    S.line("face", d.ToString(), "#3a4a2a", 1.1, " opacity=\"0.7\"");
                }
            }
            if (mood == "hurt")
            {
                // 颊上刀痕、额角流血、冷汗
                S.line("face", spline(Pts(G.F(-0.8, 0.12), G.F(-0.6, 0.36))), "#9a1a1a", 2.6);
                S.line("face", spline(Pts(G.F(-0.79, 0.13), G.F(-0.61, 0.35))), "#ff8070", 0.9);
                var b0 = G.F(-0.62, -0.62);
                S.add("face", "<path d=\"" + taper(Pts(b0, add(b0, -0.04 * R, 0.25 * R), add(b0, 0.0, 0.5 * R), add(b0, -0.05 * R, 0.78 * R)), t => (0.09 - 0.05 * t) * R) + "\" fill=\"#a81c1c\" opacity=\"0.9\"/>");
                double x = G.FX(0.7, -0.55), y = G.ey - 0.6 * R;
                S.add("top", "<path d=\"M" + N(x) + "," + N(y) + "q5,8 0,11q-5,-3 0,-11z\" fill=\"#cfeaff\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
            }
        }

        // ======================================================== 发绺 / 须绺 ==
        // “描边垫底”画法：所有发绺先用粗墨线描一遍，再逐个填色 → 并集只留外轮廓。
        static void clumps(Svg S, string layer, List<Item> items, double lw)
        {
            if (items.Count == 0) return;
            var all = new StringBuilder();
            foreach (var i in items) all.Append(i.d);
            S.add(layer, "<path d=\"" + all + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(lw * 2) + "\"/>");
            foreach (var it in items) S.add(layer, "<path d=\"" + it.d + "\" fill=\"" + it.fill + "\"" + (!string.IsNullOrEmpty(it.op) ? " opacity=\"" + it.op + "\"" : "") + "/>");
        }
        // 在折线上按弧长均匀取 n 个点
        static List<double[]> resample(List<double[]> pts, int n)
        {
            var segs = new List<double>();
            double tot = 0;
            for (int i = 0; i < pts.Count - 1; i++) { double l = hyp(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]); segs.Add(l); tot += l; }
            var o = new List<double[]>();
            for (int k = 0; k < n; k++)
            {
                double d = tot * k / (n - 1); int i = 0;
                while (i < segs.Count - 1 && d > segs[i]) { d -= segs[i]; i++; }
                o.Add(lp(pts[i], pts[i + 1], segs[i] != 0 ? clamp(d / segs[i], 0, 1) : 0));
            }
            return o;
        }
        // 外法线（顺时针绕行：右侧向下 → 底部向左 → 左侧向上）
        static List<double[]> normals(List<double[]> pts)
        {
            var o = new List<double[]>();
            for (int i = 0; i < pts.Count; i++)
            {
                double[] a = pts[Math.Max(0, i - 1)], b = pts[Math.Min(pts.Count - 1, i + 1)];
                double dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy); if (l == 0 || double.IsNaN(l)) l = 1;
                o.Add(V(dy / l, -dx / l));
            }
            return o;
        }

        // ======================================================== 胡须 ==
        // 下颌一圈：近侧鬓角 → 下颌角 → 下巴 → 远侧颊
        static List<double[]> jawRing(Geo G)
        {
            double L = G.L, jw = G.jw, cw = G.cw, fat = G.fat;
            return Pts(
                G.P(0.5, -0.06), G.P(0.6, 0.36), G.P(0.66 * jw + fat * 0.12, 0.86 * L), G.P(0.3 * jw, 1.23 * L + fat * 0.05),
                G.P(-0.06, 1.43 * L + fat * 0.1), G.P(-0.34 * cw, 1.42 * L), G.P(-0.54 * cw, 1.31 * L), G.P(-0.68 * cw - (jw - 1) * 0.15 - fat * 0.12, 1.12 * L),
                G.P(-0.84 - (jw - 1) * 0.25 - fat * 0.14, 0.86 * L), G.P(-0.92, 0.6));
        }
        // 颊上的须线（从远侧到近侧，绕开嘴唇）
        static List<double[]> beardInner(Geo G, JObj sp, bool hi = false)
        {
            double my = G.mouthY, mw = sp.Mouth.Or("w", 1);
            double up = hi ? -0.12 : 0;
            return Pts(G.P(-0.92, 0.56 + up), G.F(-0.52 * mw, my + 0.02, 0.02), G.F(-0.16, my + 0.22, 0.08), G.F(0.14, my + 0.2, 0.04), G.F(0.48 * mw, my - 0.02, 0), G.P(0.34, 0.42 + up), G.P(0.44, -0.02));
        }
        static void DrawMustache(Svg S, Geo G, JObj sp, Theme T)
        {
            string st = sp.O("mustache").S("style");
            if (string.IsNullOrEmpty(st) || st == "none") return;
            var H = hairT(Js.Or(sp.Beard.S("color"), sp.Hair.S("color")));
            double R = G.R, my = G.mouthY, ny = G.noseY, mw = sp.Mouth.Or("w", 1);
            double lwk = S.lw / 2.6;
            var items = new List<Item>();
            foreach (int side in new[] { -1, 1 })
            {
                double sc = side > 0 ? 1 : 0.64;
                var r0 = G.F(side * 0.04, ny + 0.13, 0.2);
                string fill = side < 0 ? H.b : H.s;
                if (st == "thin")
                    items.Add(It(taper(Pts(G.F(side * 0.07, ny + 0.17, 0.18), G.F(side * 0.26, my - 0.1, 0.1), G.F(side * 0.44 * mw, my + 0.03, 0.03), G.F(side * 0.52 * mw, my + 0.24, 0)), t => (1 - t) * 0.075 * R * sc + 0.25), fill));
                else if (st == "droop")
                    items.Add(It(taper(Pts(r0, G.F(side * 0.24, my - 0.1, 0.12), G.F(side * 0.42 * mw, my + 0.02, 0.04), G.F(side * 0.48 * mw, my + 0.32, 0), G.F(side * 0.46 * mw, my + 0.6, 0)), t => (t < 0.3 ? lerp(0.08, 0.14, t / 0.3) : lerp(0.14, 0.01, (t - 0.3) / 0.7)) * R * sc), fill));
                else if (st == "curl")
                    items.Add(It(taper(Pts(r0, G.F(side * 0.24, my - 0.12, 0.12), G.F(side * 0.46 * mw, my - 0.05, 0.03), G.F(side * 0.58 * mw, my - 0.2, 0), G.F(side * 0.52 * mw, my - 0.3, 0)), t => (t < 0.4 ? lerp(0.1, 0.13, t / 0.4) : lerp(0.13, 0.02, (t - 0.4) / 0.6)) * R * sc), fill));
                else
                { // thick / bristle
                    int n = st == "bristle" ? 4 : 3;
                    for (int i = 0; i < n; i++)
                    {
                        double k = (double)i / (n - 1);
                        var a = G.F(side * (0.04 + k * 0.2), ny + 0.12 + k * 0.08, 0.18 - k * 0.08);
                        var b = G.F(side * (0.3 + k * 0.28) * mw, my + 0.02 + k * 0.12 + (st == "bristle" ? -0.04 * i : 0), 0.04);
                        items.Add(It(leaf(a, b, (0.17 - k * 0.04) * R * sc, side * -0.08), fill));
                    }
                }
            }
            clumps(S, "beard", items, 0.75 * lwk);
            if (S.o.lod >= 1 && st != "thin") S.line("beard", spline(Pts(G.F(-0.05, ny + 0.17, 0.18), G.F(-0.2, my - 0.08, 0.1), G.F(-0.34 * mw, my, 0.04))), H.h, 1.0, " opacity=\"0.7\"");
        }
        static readonly Dictionary<string, double[]> JAW_LEN = new Dictionary<string, double[]>
        {
            { "short", new[] { 0.06, 0.16 } }, { "full", new[] { 0.14, 0.42 } }, { "bristle", new[] { 0.2, 0.5 } }, { "flowing", new[] { 0.12, 0.3 } },
            { "long", new[] { 0.08, 0.2 } }, { "forked", new[] { 0.1, 0.24 } }, { "curled", new[] { 0.12, 0.3 } }, { "braided", new[] { 0.12, 0.3 } },
        };
        sealed class Lk { public double[] root, tip; public double w, bend; }
        static void DrawBeard(Svg S, Geo G, JObj sp, Theme T)
        {
            string st = sp.Beard.S("style");
            if (string.IsNullOrEmpty(st) || st == "none") return;
            var H = hairT(Js.Or(sp.Beard.S("color"), sp.Hair.S("color")));
            double R = G.R, L = G.L, my = G.mouthY; int lo = S.o.lod;
            double len = sp.Beard.Or("len", 1);
            double lwk = S.lw / 2.6;
            double chinX = G.FX(-0.12, G.chinY, 0.12);
            if (st == "stubble")
            {
                var ring0 = jawRing(G); var inner0 = beardInner(G, sp);
                S.headShade.Add("<path d=\"" + spline(Cat(ring0, inner0), true) + "\" fill=\"" + H.b + "\" opacity=\"0.2\"/>");
                S.headShade.Add("<path d=\"" + spline(Pts(G.F(-0.36, G.noseY + 0.14, 0.1), G.F(0, G.noseY + 0.12, 0.2), G.F(0.4, G.noseY + 0.18, 0), G.F(0.38, my - 0.06, 0), G.F(0, my - 0.08, 0.12), G.F(-0.36, my - 0.04, 0.05)), true) + "\" fill=\"" + H.b + "\" opacity=\"0.18\"/>");
                return;
            }
            var items = new List<Item>();
            var lines = new List<string>(); var hls = new List<string>();
            string jawSil = null;
            // ---- 颊须（沿下颌一圈的短绺）
            double[] jawLen; JAW_LEN.TryGetValue(st, out jawLen);
            object bj = sp.Beard.G("jaw");
            bool jawOn = jawLen != null && (st == "short" || st == "full" || st == "bristle" || !(bj is bool && !(bool)bj) && (st == "flowing" || st == "curled" || st == "braided" || Js.Truthy(bj)));
            if (jawOn)
            {
                var ring = resample(jawRing(G), st == "bristle" ? 15 : 13);
                var nm = normals(ring);
                var inner = resample(Rev(beardInner(G, sp, st == "bristle")), ring.Count);
                int n = ring.Count;
                var tips = new List<double[]>();
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / (n - 1);
                    double bell = Math.Exp(-Math.Pow((t - 0.42) / 0.22, 2));
                    double l = lerp(jawLen[0], jawLen[1] * len, bell) * R;
                    if (st == "bristle") l *= (i % 2 != 0 ? 0.7 : 1.15);
                    double down = st == "bristle" ? 0.3 : 1.7;
                    double dx = nm[i][0], dy = nm[i][1] + down;
                    double dl = hyp(dx, dy); if (dl == 0 || double.IsNaN(dl)) dl = 1; dx /= dl; dy /= dl;
                    tips.Add(V(ring[i][0] + dx * l, ring[i][1] + dy * l));
                }
                // 整体外形（尖端成锯齿状的须簇），底色为迎光色
                var outer = new List<double[]>();
                for (int i = 0; i < n; i++)
                {
                    outer.Add(V(tips[i][0], tips[i][1], 0.2));
                    if (i < n - 1) { var m = lp(ring[i], ring[i + 1], 0.5); var mt = lp(tips[i], tips[i + 1], 0.5); outer.Add(lp(m, mt, st == "bristle" ? 0.42 : 0.6)); }
                }
                string silD = spline(Cat(outer, Rev(inner)), true);
                jawSil = silD;
                items.Add(It(silD, H.b));
                // 须线：沿每簇方向的细线（只画一部分，避免碎）
                for (int i = 0; i < n; i++)
                {
                    var root = lp(ring[i], inner[i], 0.55);
                    bool lit = ring[i][0] < G.hx - 0.05 * R;
                    if (lo >= 1 && (lit || i % 2 == 0))
                    {
                        lines.Add(spline(Pts(lp(root, tips[i], 0.12), add(lp(root, tips[i], 0.55), i % 2 != 0 ? 1.0 : -1.0, 0), lp(root, tips[i], 0.9))));
                        if (lit && i % 2 == 0) hls.Add(spline(Pts(lp(root, tips[i], 0.22), add(lp(root, tips[i], 0.5), -1.2, 0), lp(root, tips[i], 0.72))));
                    }
                }
            }
            // ---- 下巴的长须 / 山羊须
            if (st == "goatee" || st == "long" || st == "flowing" || st == "forked" || st == "curled" || st == "braided")
            {
                double blen = (st == "goatee" ? 0.5 : st == "flowing" ? 2.55 : st == "curled" ? 1.3 : st == "braided" ? 1.0 : 1.35) * len;
                double sway = st == "flowing" ? -0.22 : st == "goatee" ? -0.04 : -0.1;
                double wide = st == "curled" ? 1.25 : st == "goatee" ? 0.55 : st == "flowing" ? 1.0 : 0.85;
                int nL = st == "goatee" ? 3 : st == "flowing" ? 8 : 6;
                double baseY = G.chinY - (st == "goatee" ? 0.2 : 0.05);
                var locks = new List<Lk>();
                for (int i = 0; i < nL; i++)
                {
                    double k = nL == 1 ? 0.5 : (double)i / (nL - 1);
                    double u = lerp(-0.48, 0.34, k) * wide;
                    var root = st == "goatee" && i == 1 ? G.F(-0.06, my + 0.2, 0.1) : G.F(u, baseY - Math.Abs(k - 0.5) * 0.3, 0.1);
                    double ly = blen * (0.72 + 0.28 * Math.Sin(Math.PI * (0.25 + k * 0.6)));
                    if (st == "forked") ly *= (k < 0.5 ? 1.0 : 0.95);
                    double tx = chinX + (sway + (k - 0.5) * (st == "curled" ? 0.9 : 0.35) * wide) * R;
                    if (st == "forked") tx = chinX + (sway + (k < 0.5 ? -0.28 : 0.22)) * R;
                    var tip = V(tx, G.ey + (G.chinY + ly) * R);
                    locks.Add(new Lk { root = root, tip = tip, w = (st == "goatee" ? 0.2 : 0.34 * wide) * R, bend = (k - 0.5) * -0.12 + (st == "flowing" ? 0.05 * Math.Sin(i * 2.1) : 0) });
                }
                // 外形底层：从各绺两侧取点
                foreach (var lk in locks) items.Add(It(leaf(lk.root, lk.tip, lk.w * 1.25, lk.bend), H.s));
                for (int i = 0; i < locks.Count; i++)
                {
                    var lk = locks[i];
                    bool lit = i < locks.Count * 0.6;
                    items.Add(It(@lock(lp(lk.root, lk.tip, 0.02), lp(lk.root, lk.tip, lit ? 0.98 : 0.92), lk.w * (lit ? 0.85 : 0.7), lk.bend * 1.5 + (i % 2 != 0 ? 0.04 : -0.04)), lit ? H.b : mix(H.b, H.s, 0.45)));
                    if (lo >= 1)
                    {
                        var m = lp(lk.root, lk.tip, 0.5);
                        lines.Add(spline(Pts(lp(lk.root, lk.tip, 0.1), add(m, (i % 2 != 0 ? 1.5 : -1.5), 0), lp(lk.root, lk.tip, 0.93))));
                        if (lit) hls.Add(spline(Pts(add(lp(lk.root, lk.tip, 0.12), -lk.w * 0.18, 0), add(m, -lk.w * 0.22 - 1, 0), add(lp(lk.root, lk.tip, 0.7), -lk.w * 0.1, 0))));
                    }
                }
                if (st == "braided")
                {
                    // 末端编成辫子
                    var top = V(chinX - 0.08 * R, G.ey + (G.chinY + blen * 0.8) * R);
                    for (int j = 0; j < 4; j++)
                    {
                        var c = add(top, -0.02 * R * j, j * 0.16 * R);
                        items.Add(It(spline(Pts(add(c, -0.1 * R, 0), add(c, 0, -0.05 * R), add(c, 0.1 * R, 0.02 * R), add(c, 0, 0.14 * R)), true), j % 2 != 0 ? H.b : H.s));
                    }
                }
            }
            clumps(S, "beard", items, 0.8 * lwk);
            if (jawSil != null)
            {
                // 颊须的阴影面：背光的近侧与唇下（赛璐璐两阶）
                string sh = spline(Pts(G.P(0.16, -0.3), G.P(0.12, 0.5), G.P(0.02, 1.0 * L), G.P(-0.2, 1.55 * L), G.P(-0.3, 2.6, 0), G.P(2.2, 2.6, 0), G.P(2.2, -0.3, 0)), true);
                string lip = spline(Pts(G.F(-0.42, my + 0.1, 0.06), G.F(-0.1, my + 0.3, 0.1), G.F(0.3, my + 0.22, 0.04), G.F(0.1, my + 0.5, 0.08), G.F(-0.3, my + 0.42, 0.06)), true);
                S.add("beard", "<g clip-path=\"" + S.clip("jb", jawSil) + "\"><path d=\"" + sh + "\" fill=\"" + H.s + "\"/><path d=\"" + lip + "\" fill=\"" + H.s + "\" opacity=\"0.7\"/></g>");
            }
            if (st == "curled" && lo >= 1)
            {
                // 波斯式卷须：成排的小卷
                var cd = new StringBuilder();
                for (int row = 0; row < 4; row++) for (int j = 0; j < 5; j++)
                    {
                        double x = chinX + (-0.55 + j * 0.25 + (row % 2) * 0.12) * R * 1.1, y = G.ey + (G.chinY + 0.15 + row * 0.28) * R;
                        cd.Append("M" + N(x + 3.2) + "," + N(y) + "a3.2,3.2 0 1,1 -3.2,-3.2");
                    }
                S.line("beard", cd.ToString(), H.d, 1.1, " opacity=\"0.8\"");
            }
            if (lines.Count > 0) S.line("beard", string.Join("", lines.ToArray()), H.d, 1.0, " opacity=\"0.6\"");
            if (hls.Count > 0) S.line("beard", string.Join("", hls.ToArray()), H.h, 1.2, " opacity=\"0.55\"");
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static partial class Portrait
    {
        // ======================================================== 头发 ==
        internal sealed class HatInfo
        {
            public bool coversTop, ownsBun, handled;
            public HatInfo(bool coversTop, bool ownsBun = false, bool handled = false) { this.coversTop = coversTop; this.ownsBun = ownsBun; this.handled = handled; }
        }
        internal delegate void HairFn(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H);
        internal delegate HatInfo HatFn(Svg S, Geo G, JObj sp, Theme T);

        static List<double[]> hairline(Geo G, JObj sp)
        {
            double rec = sp.Hair.Has("recede") ? sp.Hair.D("recede") : (sp.D("age") > 55 ? 0.12 : 0);
            double sb = sp.Hair.Or("sideburn", 1);
            return Pts(
                G.P(-0.95, -0.54), G.P(-0.8, -0.86 - rec), G.P(-0.44, -0.98 - rec, 1), G.P(-0.08, -0.97 - rec), G.P(0.3, -0.86 - rec * 0.8),
                G.P(0.44, -0.6, 0.6), G.P(0.47, -0.36), G.P(0.49, 0.04 * sb, 0.3), G.P(0.6, 0.06 * sb, 0.3), G.P(0.64, 0.45), G.P(0.94, 0.42));
        }
        // 头发外缘：颅骨椭圆放大
        static List<double[]> hairBack(Geo G, double vol, double nape = 0, int bumps = 0)
        {
            var e = G.ell; double s = 1 + vol;
            double rx = G.silRx(s), ry = G.silRy(s);
            if (bumps == 0) return Cat(Pts(V(e.Cx + rx * 0.99, e.Cy + ry * (nape != 0 ? nape : 0.5))), arcPts(e.Cx, e.Cy, rx, ry, -0.2, Math.PI * 0.96, 9));
            // 卷发：外缘起伏
            var o = Pts(V(e.Cx + rx * 0.99, e.Cy + ry * (nape != 0 ? nape : 0.5)));
            int n = bumps * 2;
            for (int i = 0; i <= n; i++)
            {
                double a = -0.2 + (Math.PI * 0.96 + 0.2) * i / n, k = i % 2 != 0 ? 1.06 : 0.98;
                o.Add(V(e.Cx + rx * k * Math.Cos(a), e.Cy - ry * k * Math.Sin(a), i % 2 != 0 ? 1 : 0.35));
            }
            return o;
        }
        static void DrawHair(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo)
        {
            string style = sp.Hair.Or("style", "topknot");
            HairFn fn; if (!HAIR.TryGetValue(style, out fn)) fn = HAIR["topknot"];
            if (hatInfo.handled) return;
            fn(S, G, sp, T, hatInfo, hairT(sp.Hair.S("color")));
        }
        internal sealed class HairOpt
        {
            public List<double[]> hairline, back;
            public double vol, nape; public int bumps;
            public double[] target;
            public bool shine = true, strands = true;
            public Action extra;
        }
        // 头发主体（发际线以上）
        static List<double[]> hairCap(Svg S, Geo G, JObj sp, Theme T, Tone H, HairOpt opt = null)
        {
            opt = opt ?? new HairOpt();
            double R = G.R; int lo = S.o.lod;
            double vol = sp.Hair.Or("vol", 1) * (opt.vol != 0 ? opt.vol : 0.06);
            var hl = opt.hairline ?? hairline(G, sp);
            var hp = Cat(hl, opt.back ?? hairBack(G, vol, opt.nape, opt.bumps));
            string d = spline(hp, true);
            S.shape("hair", d, H.b, () =>
            {
                S.path("hair", spline(Pts(G.P(0.28, -2.0, 0), G.P(0.3, -1.1), G.P(0.55, -0.6), G.P(0.62, 0.9, 0), G.P(2, 0.9, 0), G.P(2, -2, 0)), true), H.s);
                if (opt.shine)
                {
                    // 发丝高光带：上缘顺着头形，下缘锯齿
                    var top = Pts(G.P(-0.88, -0.98), G.P(-0.5, -1.27), G.P(0.0, -1.38), G.P(0.48, -1.3));
                    var lo2 = new List<double[]>();
                    for (int i = 0; i <= 8; i++)
                    {
                        double t = i / 8.0, u = lerp(0.42, -0.84, t);
                        double yb = -1.24 + Math.Pow(Math.Abs(t - 0.45) * 1.6, 2) * 0.22;
                        lo2.Add(G.P(u, yb + (i % 2 != 0 ? 0.12 : 0), i % 2 != 0 ? 0 : 0.6));
                    }
                    S.path("hair", spline(Cat(top, lo2), true), H.h, 0, " opacity=\"0.8\"");
                }
                if (lo >= 1 && opt.strands)
                {
                    var sd = new StringBuilder();
                    var tgt = opt.target ?? G.P(0.25, -1.55);
                    for (int i = 0; i < 10; i++)
                    {
                        int fi = (int)Math.Floor(i * 0.5);
                        var a = lp(hl[Math.Min(hl.Count - 1, 1 + fi)], hl[Math.Min(hl.Count - 1, 2 + fi)], (i * 0.5) % 1);
                        var q = lp(a, tgt, 0.55);
                        sd.Append(spline(Pts(add(a, 0, -1), V(q[0] + (i - 4) * 1.6, q[1] - 2), lp(a, tgt, 0.9))));
                    }
                    S.line("hair", sd.ToString(), H.d, 1.0, " opacity=\"0.6\"");
                }
                if (opt.extra != null) opt.extra();
            }, S.lw);
            rimOn(S, "hair", hp, T.rim, 2.4, 0.55);
            S.headShade.Add("<path d=\"" + spline(Cat(Slice(hl, 0, 7), Rev(shift(Slice(hl, 0, 7), 1.5, 0.09 * R))), true) + "\" fill=\"" + T.skin.s + "\" opacity=\"0.9\"/>");
            return hl;
        }
        // 鬓边垂下的几缕散发
        static void templeLocks(Svg S, Geo G, Tone H, double n, double len)
        {
            double R = G.R; var items = new List<Item>();
            for (int i = 0; i < n; i++)
            {
                double[] a = G.P(0.42 + i * 0.05, -0.62 + i * 0.05), b = G.P(0.36 + i * 0.1, -0.1 + len * (0.5 + i * 0.25));
                items.Add(It(leaf(a, b, 0.1 * R, 0.12), i != 0 ? H.s : H.b));
            }
            clumps(S, "hair", items, S.lw * 0.5);
        }
        static string bun(Svg S, Geo G, Tone H, double x, double y, double r)
        {
            string bd = spline(Pts(V(x - r, y + 0.55 * r), V(x - 0.85 * r, y - 0.55 * r), V(x + 0.15 * r, y - r), V(x + r, y - 0.45 * r), V(x + 0.95 * r, y + 0.6 * r)), true);
            S.shape("hair", bd, H.b, () =>
            {
                S.path("hair", spline(Pts(V(x + 0.15 * r, y - 1.1 * r), V(x + 1.1 * r, y - 0.3 * r), V(x + r, y + 0.8 * r), V(x + 0.3 * r, y + 0.8 * r)), true), H.s);
                S.path("hair", spline(Pts(V(x - 0.72 * r, y - 0.3 * r), V(x - 0.15 * r, y - 0.72 * r), V(x + 0.3 * r, y - 0.68 * r), V(x - 0.3 * r, y - 0.36 * r)), true), H.h);
                if (S.o.lod >= 1) S.line("hair", spline(Pts(V(x - 0.6 * r, y + 0.3 * r), V(x - 0.2 * r, y - 0.5 * r), V(x + 0.5 * r, y - 0.6 * r))) + spline(Pts(V(x - 0.3 * r, y + 0.5 * r), V(x + 0.2 * r, y - 0.2 * r), V(x + 0.8 * r, y - 0.2 * r))), H.d, 1, " opacity=\"0.6\"");
            }, S.lw);
            return bd;
        }
        static readonly Dictionary<string, HairFn> HAIR = new Dictionary<string, HairFn>
        {
            { "topknot", HairTopknot }, { "bun", HairBun }, { "twinbun", HairTwinbun }, { "long", HairLong },
            { "mallet", HairMallet }, { "wild", HairWild }, { "mizura", HairMizura }, { "wahair", HairWahair }, { "koreanbraid", HairKoreanbraid },
            { "kunfa", HairKunfa }, { "braids", HairBraids }, { "highbun", HairHighbun }, { "bob", HairBob }, { "short", HairShort },
            { "curly", HairCurly }, { "bushy", HairBushy }, { "spiky", HairSpiky }, { "suebian", HairSuebian }, { "romanf", HairRomanf },
        };
        // 汉式束发：梳拢成髻
        static void HairTopknot(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            hairCap(S, G, sp, T, H);
            if (!hatInfo.coversTop)
            {
                double R = G.R, bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
                bun(S, G, H, bx, by, 0.36 * R);
                if (!hatInfo.ownsBun)
                {
                    S.path("hair", spline(Pts(V(bx - 0.36 * R, by + 0.12 * R), V(bx, by + 0.2 * R), V(bx + 0.35 * R, by + 0.1 * R), V(bx + 0.36 * R, by + 0.24 * R), V(bx, by + 0.34 * R), V(bx - 0.36 * R, by + 0.26 * R)), true), sp.Hair.Or("tie", "#7a2a24"), 1.2);
                    S.line("hair", "M" + N(bx - 0.62 * R) + "," + N(by + 0.02 * R) + "L" + N(bx + 0.62 * R) + "," + N(by - 0.12 * R), INK, 3.4);
                    S.line("hair", "M" + N(bx - 0.6 * R) + "," + N(by + 0.02 * R) + "L" + N(bx + 0.6 * R) + "," + N(by - 0.12 * R), sp.Hair.Or("pin", GOLD), 1.8);
                }
            }
            if (sp.Hair.T("locks")) templeLocks(S, G, H, sp.Hair.D("locks"), 1);
        }
        // 女子高髻：中分，鬓发垂于耳前，簪钗步摇
        static void HairBun(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            var hl = Pts(G.P(-0.95, -0.5), G.P(-0.78, -0.84), G.P(-0.42, -0.98, 0.3), G.P(-0.2, -0.9), G.P(0.22, -0.84), G.P(0.44, -0.58), G.P(0.46, -0.3), G.P(0.42, 0.3, 0.4), G.P(0.62, 0.4, 0.4), G.P(0.66, 0.5), G.P(1.0, 0.6));
            hairCap(S, G, sp, T, H, new HairOpt { hairline = hl, vol = 0.12, target = G.P(0.1, -1.7), shine = true });
            if (!hatInfo.coversTop)
            {
                double bx = G.hx + 0.05 * R, by = G.ey - 1.72 * R;
                bun(S, G, H, bx + 0.42 * R, by + 0.12 * R, 0.42 * R);
                bun(S, G, H, bx - 0.12 * R, by - 0.05 * R, 0.4 * R);
                // 发钗与步摇
                string pin = sp.Hair.Or("pin", GOLD);
                S.line("hair", "M" + N(bx - 0.75 * R) + "," + N(by + 0.25 * R) + "L" + N(bx + 0.4 * R) + "," + N(by - 0.35 * R), INK, 3.2);
                S.line("hair", "M" + N(bx - 0.73 * R) + "," + N(by + 0.24 * R) + "L" + N(bx + 0.4 * R) + "," + N(by - 0.35 * R), pin, 1.7);
                double fx = bx - 0.72 * R, fy = by + 0.25 * R;
                S.add("hair", circle(fx, fy, 0.11 * R) + " fill=\"" + sp.Hair.Or("flower", "#e0607a") + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
                S.add("hair", circle(fx, fy, 0.04 * R) + " fill=\"#ffe08a\"/>");
                if (S.o.lod >= 1)
                {
                    S.line("hair", "M" + N(fx) + "," + N(fy) + "v" + N(0.45 * R) + "M" + N(fx - 3) + "," + N(fy) + "v" + N(0.32 * R), pin, 1.0);
                    S.add("hair", circle(fx, fy + 0.48 * R, 1.8) + " fill=\"" + pin + "\"/>" + circle(fx - 3, fy + 0.35 * R, 1.5) + " fill=\"#c8e0ff\"/>");
                }
            }
            // 耳前鬓发
            clumps(S, "hair", new List<Item> {
                It(leaf(G.P(0.44, -0.6), G.P(0.4, 0.55), 0.16 * R, 0.08), H.s),
                It(leaf(G.P(-0.9, -0.5), G.P(-0.98, 0.3), 0.14 * R, -0.08), H.b),
            }, S.lw * 0.55);
        }
        static void HairTwinbun(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            HairBun(S, G, sp.Clone(), T, new HatInfo(true), H);
            double R = G.R;
            bun(S, G, H, G.hx - 0.42 * R, G.ey - 1.65 * R, 0.32 * R);
            bun(S, G, H, G.hx + 0.62 * R, G.ey - 1.5 * R, 0.34 * R);
        }
        // 披发 / 长发（散在肩后）
        static void HairLong(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            var items = new List<Item>();
            for (int i = 0; i < 6; i++)
            {
                double[] a = G.P(0.3 + i * 0.16, -0.9 + i * 0.12), b = G.P(0.8 + i * 0.2, 1.8 + (i % 2) * 0.4);
                items.Add(It(leaf(a, b, 0.5 * R, 0.06), i % 2 != 0 ? H.s : H.d));
            }
            items.Add(It(leaf(G.P(-0.9, -0.7), G.P(-1.05, 1.0), 0.3 * R, -0.06), H.s));
            clumps(S, "back", items, S.lw * 0.6);
            hairCap(S, G, sp, T, H, new HairOpt { vol = 0.1, target = G.P(0.6, -0.5) });
            if (!hatInfo.coversTop && sp.Hair.T("topknot")) HairTopknot(S, G, sp, T, new HatInfo(false, false), H);
        }

        // 发辫：沿折线的一串交错发节
        static void braid(Svg S, string layer, Geo G, List<double[]> pts, double w, Tone H, string tie)
        {
            var items = new List<Item>();
            var seg = resample(pts, Math.Max(4, (int)Js.Round(pathLen(pts) / (w * 0.85))));
            for (int i = 0; i < seg.Count - 1; i++)
            {
                double[] a = seg[i], b = seg[i + 1];
                items.Add(It(leaf(lp(a, b, -0.15), lp(a, b, 1.25), w * (1 - (double)i / seg.Count * 0.35), i % 2 != 0 ? 0.18 : -0.18), i % 2 != 0 ? H.b : H.s));
            }
            clumps(S, layer, items, S.lw * 0.55);
            var e = seg[seg.Count - 1];
            if (!string.IsNullOrEmpty(tie)) S.add(layer, ellipse(e[0], e[1], w * 0.45, w * 0.3) + " fill=\"" + tie + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
        }
        static double pathLen(List<double[]> pts) { double l = 0; for (int i = 0; i < pts.Count - 1; i++) l += hyp(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]); return l; }
        // 环形发髻（角髪）
        static void hairLoop(Svg S, string layer, Geo G, double[] c, double rx, double ry, Tone H)
        {
            string d = ellipsePath(c[0], c[1], rx, ry) + ellipsePath(c[0] + rx * 0.08, c[1], rx * 0.45, ry * 0.5);
            S.add(layer, "<path d=\"" + d + "\" fill=\"" + H.b + "\" fill-rule=\"evenodd\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            S.add(layer, "<path d=\"" + spline(Pts(V(c[0] - rx * 0.8, c[1] - ry * 0.3), V(c[0] - rx * 0.3, c[1] - ry * 0.85), V(c[0] + rx * 0.2, c[1] - ry * 0.8))) + "\" fill=\"none\" stroke=\"" + H.h + "\" stroke-width=\"1.4\" opacity=\"0.8\"/>");
        }
        static string ellipsePath(double cx, double cy, double rx, double ry)
        {
            return "M" + N(cx - rx) + "," + N(cy) + "a" + N(rx) + "," + N(ry) + " 0 1,0 " + N(2 * rx) + ",0a" + N(rx) + "," + N(ry) + " 0 1,0 " + N(-2 * rx) + ",0Z";
        }
        // 中分发际线
        static List<double[]> partLine(Geo G)
        {
            return Pts(G.P(-0.95, -0.5), G.P(-0.8, -0.86), G.P(-0.46, -0.99, 0.2), G.P(-0.36, -0.88), G.P(0.15, -0.86), G.P(0.42, -0.62), G.P(0.46, -0.3), G.P(0.48, -0.06, 0.3), G.P(0.62, -0.06, 0.3), G.P(0.66, 0.35), G.P(0.96, 0.4));
        }
        // 椎髻（南中）：脑后上方一个大髻，骨簪横插
        static void HairMallet(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            hairCap(S, G, sp, T, H, new HairOpt { target = G.P(0.6, -1.4) });
            if (hatInfo.coversTop) return;
            double bx = G.hx + 0.62 * R, by = G.ey - 1.42 * R;
            bun(S, G, H, bx, by, 0.5 * R);
            S.add("hair", "<path d=\"" + taper(Pts(V(bx - 0.48 * R, by + 0.26 * R), V(bx, by + 0.42 * R), V(bx + 0.46 * R, by + 0.28 * R)), 0.16 * R) + "\" fill=\"" + sp.Hair.Or("tie", "#a83220") + "\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>");
            foreach (var dd in new[] { new[] { 0.0, 0.0 }, new[] { 0.12, 0.28 } })
            {
                double dx = dd[0], dy = dd[1];
                double[] a = V(bx - 0.85 * R + dx * R, by - 0.05 * R + dy * R), b = V(bx + 0.75 * R + dx * R, by - 0.5 * R + dy * R);
                S.add("hair", "<path d=\"" + taper(Pts(a, lp(a, b, 0.5), b), t => 0.12 * R * (1 - Math.Abs(t - 0.5)) + 0.6) + "\" fill=\"#efe4c8\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>");
            }
        }
        // 蓬乱长发（蛮族、草莽）
        static void HairWild(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R; var e = G.ell;
            var back = new List<Item>();
            for (int i = 0; i < 7; i++) back.Add(It(leaf(G.P(0.2 + i * 0.16, -0.9 + i * 0.12), G.P(0.7 + i * 0.22 + (i % 2) * 0.1, 1.9 + (i % 3) * 0.35), 0.5 * R, (i % 2 != 0 ? 0.08 : -0.04)), i % 2 != 0 ? H.s : H.d));
            back.Add(It(leaf(G.P(-0.85, -0.8), G.P(-1.15, 1.2), 0.38 * R, -0.08), H.s));
            clumps(S, "back", back, S.lw * 0.6);
            hairCap(S, G, sp, T, H, new HairOpt { vol = 0.1, target = G.P(0.4, -0.4) });
            if (!hatInfo.coversTop)
            {
                double rx = G.silRx(1.1), ry = G.silRy(1.1); var items = new List<Item>();
                for (int i = 0; i < 9; i++)
                {
                    double a = -0.25 + i * 0.4;
                    var root = V(e.Cx + rx * 0.85 * Math.Cos(a), e.Cy - ry * 0.85 * Math.Sin(a));
                    var tip = V(e.Cx + rx * 1.28 * Math.Cos(a + 0.12), e.Cy - ry * 1.22 * Math.Sin(a + 0.12));
                    items.Add(It(leaf(root, tip, 0.42 * R, 0.12), i % 2 != 0 ? H.b : H.s));
                }
                clumps(S, "hair", items, S.lw * 0.6);
            }
            // 额前乱发
            var bangs = new List<Item>();
            for (int i = 0; i < 4; i++) bangs.Add(It(leaf(G.P(-0.8 + i * 0.3, -1.08), G.P(-0.75 + i * 0.32 + (i % 2) * 0.08, -0.62 - (i % 2) * 0.08), 0.24 * R, i % 2 != 0 ? 0.12 : -0.1), i % 2 != 0 ? H.s : H.b));
            clumps(S, "hair", bangs, S.lw * 0.5);
        }
        // 美豆良（角髪）：中分，两耳旁结成发环
        static void HairMizura(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            hairCap(S, G, sp, T, H, new HairOpt { hairline = partLine(G), target = G.P(0.6, -0.2), vol = 0.05 });
            hairLoop(S, "hat", G, G.P(0.66, 0.08), 0.2 * R, 0.26 * R, H);
            hairLoop(S, "hat", G, G.P(0.7, 0.55), 0.22 * R, 0.28 * R, H);
            S.add("hat", "<path d=\"" + taper(Pts(G.P(0.5, 0.3), G.P(0.68, 0.32), G.P(0.88, 0.3)), 0.1 * R) + "\" fill=\"" + sp.Hair.Or("tie", "#e8e0d0") + "\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>");
            hairLoop(S, "back", G, G.P(-1.04, 0.1), 0.12 * R, 0.24 * R, H);
            hairLoop(S, "back", G, G.P(-1.06, 0.55), 0.13 * R, 0.26 * R, H);
            G.hideEar = true;
        }
        // 倭女：中分长直发，耳前垂发
        static void HairWahair(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            S.shape("back", spline(Pts(G.P(0.2, -1.2), G.P(1.2, -0.6), G.P(1.45, 0.8), G.P(1.6, 2.6, 0), G.P(0.4, 2.7, 0), G.P(0.6, 0.6)), true), H.s, null, S.lw);
            hairCap(S, G, sp, T, H, new HairOpt { hairline = partLine(G), target = G.P(0.8, 0.2), vol = 0.08 });
            clumps(S, "front", new List<Item> {
                It(taper(Pts(G.P(0.46, -0.5), G.P(0.56, 0.6), G.P(0.62, 1.6), G.P(0.5, 2.6)), t => 0.26 * R * (1 - t * 0.5)), H.b),
                It(taper(Pts(G.P(-0.92, -0.4), G.P(-1.0, 0.8), G.P(-1.05, 2.2)), t => 0.2 * R * (1 - t * 0.5)), H.b),
            }, S.lw * 0.55);
            G.hideEar = true;
        }
        // 朝鲜女子：中分，脑后一条长辫系红绳
        static void HairKoreanbraid(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            hairCap(S, G, sp, T, H, new HairOpt { hairline = partLine(G), target = G.P(1.0, 0.0), vol = 0.06 });
            braid(S, "back", G, Pts(G.P(1.05, 0.3), G.P(1.25, 1.2), G.P(1.3, 2.4), G.P(1.2, 3.3)), 0.32 * R, H, "#c02a30");
        }
        // 髡发：头顶剃净（青色头皮），额前留一小撮，两鬓垂辫
        static void HairKunfa(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            if (!hatInfo.coversTop)
            {
                var hl = hairline(G, sp);
                S.headShade.Add("<path d=\"" + spline(Cat(hl, hairBack(G, 0, 0.5)), true) + "\" fill=\"#3a4a5a\" opacity=\"0.28\"/>");
                var tuft = new List<Item>();
                for (int i = 0; i < 3; i++) tuft.Add(It(leaf(G.P(-0.62 + i * 0.12, -1.05), G.P(-0.6 + i * 0.13, -0.78), 0.16 * R, 0.1), i % 2 != 0 ? H.s : H.b));
                clumps(S, "hair", tuft, S.lw * 0.5);
            }
            braid(S, "hat", G, Pts(G.P(0.5, -0.4), G.P(0.52, 0.4), G.P(0.58, 1.2), G.P(0.62, 1.7)), 0.22 * R, H, sp.Hair.Or("tie", "#c8a030"));
            braid(S, "back", G, Pts(G.P(-0.95, -0.35), G.P(-1.02, 0.5), G.P(-1.08, 1.4)), 0.18 * R, H, sp.Hair.Or("tie", "#c8a030"));
        }
        // 双辫垂肩
        static void HairBraids(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            hairCap(S, G, sp, T, H, new HairOpt { hairline = sp.S("sex") == "f" ? partLine(G) : null, target = G.P(0.6, -0.6) });
            braid(S, "front", G, Pts(G.P(0.82, 0.4), G.P(1.0, 1.3), G.P(0.95, 2.3), G.P(0.85, 3.0)), 0.3 * R, H, sp.Hair.Or("tie", "#b03028"));
            braid(S, "back", G, Pts(G.P(-0.9, 0.0), G.P(-1.15, 1.0), G.P(-1.22, 2.2)), 0.24 * R, H, sp.Hair.Or("tie", "#b03028"));
        }
        // 高髻（南海）：头顶一个高髻，金箍
        static void HairHighbun(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            hairCap(S, G, sp, T, H, new HairOpt { target = G.P(0.0, -1.7) });
            if (hatInfo.coversTop) return;
            double bx = G.hx - 0.05 * R, by = G.ey - 1.85 * R;
            bun(S, G, H, bx, by, 0.4 * R);
            S.add("hair", "<path d=\"" + taper(Pts(V(bx - 0.4 * R, by + 0.3 * R), V(bx, by + 0.42 * R), V(bx + 0.4 * R, by + 0.3 * R)), 0.16 * R) + "\" fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>");
            if (!sp.Hair.IsFalse("flower")) S.add("hair", flower(bx + 0.42 * R, by + 0.25 * R, 0.16 * R, sp.Hair.Or("flower", "#f0e0a0")));
        }
        // 齐耳短发（龟兹）：齐眉刘海，两侧垂到下颌
        static void HairBob(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            S.shape("back", spline(Pts(G.P(-0.9, -0.6), G.P(-1.12, 0.2), G.P(-1.1, 1.0, 0), G.P(-0.82, 1.0, 0), G.P(-0.9, 0.2)), true), H.s, null, S.lw);
            var hl = Pts(G.P(-0.96, -0.55), G.P(-0.86, -0.64, 0), G.P(-0.6, -0.62), G.P(-0.3, -0.64), G.P(0.0, -0.62), G.P(0.3, -0.64, 0), G.P(0.42, -0.55), G.P(0.42, 0.95, 0), G.P(0.7, 1.08), G.P(1.12, 0.95, 0));
            var back = Cat(Pts(V(G.ell.Cx + G.silRx(1.08) * 1.0, G.ell.Cy + G.silRy(1.08) * 0.5)), arcPts(G.ell.Cx, G.ell.Cy, G.silRx(1.08), G.silRy(1.08), -0.2, Math.PI * 0.97, 9));
            hairCap(S, G, sp, T, H, new HairOpt
            {
                hairline = hl, back = back, strands = false, extra = () =>
                {
                    var sd = new StringBuilder();
                    for (int i = 0; i < 7; i++) sd.Append(spline(Pts(G.P(-0.8 + i * 0.22, -1.2), G.P(-0.78 + i * 0.22, -0.9), G.P(-0.76 + i * 0.22, -0.66))));
                    for (int i = 0; i < 3; i++) sd.Append(spline(Pts(G.P(0.5 + i * 0.18, -0.6), G.P(0.55 + i * 0.2, 0.2), G.P(0.55 + i * 0.2, 0.95))));
                    S.line("hair", sd.ToString(), H.d, 1.0, " opacity=\"0.6\"");
                }
            });
            G.hideEar = true;
        }
        // 罗马式短发：贴头，额前短刘海
        static void HairShort(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            var hl = Pts(G.P(-0.95, -0.52), G.P(-0.86, -0.76), G.P(-0.66, -0.8, 0.3), G.P(-0.56, -0.74, 0.3), G.P(-0.42, -0.82, 0.3), G.P(-0.28, -0.76, 0.3), G.P(-0.12, -0.84, 0.3), G.P(0.1, -0.8, 0.3), G.P(0.32, -0.8), G.P(0.44, -0.58), G.P(0.47, -0.36), G.P(0.49, -0.08, 0.3), G.P(0.6, -0.08, 0.3), G.P(0.64, 0.3), G.P(0.94, 0.32));
            hairCap(S, G, sp, T, H, new HairOpt
            {
                hairline = hl, vol = 0.04, strands = false, shine = false, bumps = hatInfo.coversTop ? 0 : 10, extra = () =>
                {
                    if (S.o.lod < 1) return;
                    var cd = new StringBuilder(); var hd = new StringBuilder();
                    var rr = Sub(sp.S("name"), "curl");
                    for (int i = 0; i < 30; i++)
                    {
                        double lam = rr.range(-1.0, 1.7), bet = rr.range(0.3, 1.35);
                        var p = G.h3(lam, bet, 1.03);
                        string d = "M" + N(p[0] + 1.5) + "," + N(p[1] - 1.5) + "q" + N(-3) + "," + N(0.5) + " " + N(-3.2) + "," + N(3.8);
                        if (lam < 0.3 && bet > 0.6 && i % 2 != 0) hd.Append(d); else cd.Append(d);
                    }
                    S.line("hair", cd.ToString(), H.d, 1.2, " opacity=\"0.75\"");
                    S.line("hair", hd.ToString(), H.h, 1.4, " opacity=\"0.85\"");
                }
            });
        }
        // 短卷发：外缘起伏，内有小卷
        static void HairCurly(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            var hl = Pts(G.P(-0.95, -0.52), G.P(-0.86, -0.8), G.P(-0.6, -0.86, 0.4), G.P(-0.4, -0.8, 0.4), G.P(-0.2, -0.88, 0.4), G.P(0.1, -0.84, 0.4), G.P(0.32, -0.8), G.P(0.44, -0.58), G.P(0.47, -0.36), G.P(0.49, 0.0, 0.3), G.P(0.6, 0.0, 0.3), G.P(0.64, 0.4), G.P(0.98, 0.4));
            hairCap(S, G, sp, T, H, new HairOpt
            {
                hairline = hl, vol = 0.08, bumps = hatInfo.coversTop ? 0 : 8, strands = false, shine = false, extra = () =>
                {
                    if (S.o.lod < 1) return;
                    var cd = new StringBuilder(); var hd = new StringBuilder();
                    var rr = Sub(sp.S("name"), "curl");
                    for (int i = 0; i < 26; i++)
                    {
                        double lam = rr.range(-1.0, 1.9), bet = rr.range(0.1, 1.35);
                        var p = G.h3(lam, bet, 1.06);
                        double r0 = rr.range(2.2, 3.4);
                        cd.Append("M" + N(p[0] + r0) + "," + N(p[1]) + "a" + N(r0) + "," + N(r0) + " 0 1,0 " + N(-r0) + "," + N(r0));
                        if (lam < 0.4 && bet > 0.5) hd.Append("M" + N(p[0] - r0 * 0.6) + "," + N(p[1] - r0 * 0.4) + "a" + N(r0) + "," + N(r0) + " 0 0,1 " + N(r0) + "," + N(-r0 * 0.5));
                    }
                    S.line("hair", cd.ToString(), H.d, 1.2, " opacity=\"0.75\"");
                    S.line("hair", hd.ToString(), H.h, 1.3, " opacity=\"0.8\"");
                }
            });
        }
        // 安息式浓密卷发：脑后与两鬓蓬起
        static void HairBushy(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            var puffs = new[] { new[] { 0.95, -0.55, 0.42 }, new[] { 1.25, -0.05, 0.46 }, new[] { 1.25, 0.5, 0.44 }, new[] { 0.95, 0.85, 0.4 }, new[] { 1.5, 0.3, 0.32 }, new[] { -1.02, 0.15, 0.26 }, new[] { -1.0, 0.5, 0.22 } };
            var pd = new StringBuilder();
            foreach (var pf in puffs) { var c = G.P(pf[0], pf[1]); pd.Append(circle(c[0], c[1], pf[2] * R) + "/>"); }
            S.add("back", "<g fill=\"" + H.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 2) + "\">" + pd + "</g><g fill=\"" + H.b + "\">" + pd + "</g>");
            if (S.o.lod >= 1)
            {
                var cd = new StringBuilder();
                foreach (var pf in puffs) for (int k = 0; k < 3; k++) { var c = G.P(pf[0] + Math.Cos(k * 2.1) * pf[2] * 0.45, pf[1] + Math.Sin(k * 2.1) * pf[2] * 0.45); cd.Append("M" + N(c[0] + 3) + "," + N(c[1]) + "a3,3 0 1,0 -3,3"); }
                S.line("back", cd.ToString(), H.d, 1.2, " opacity=\"0.8\"");
            }
            HairCurly(S, G, sp, T, hatInfo, H);
        }
        // 石灰竖起的尖发（凯尔特）
        static void HairSpiky(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R; var e = G.ell;
            hairCap(S, G, sp, T, H, new HairOpt { vol = 0.06, target = G.P(0.4, -1.8) });
            if (hatInfo.coversTop) return;
            double rx = G.silRx(1.05), ry = G.silRy(1.05); var items = new List<Item>();
            string tipC = mix(H.b, "#f0e8d0", 0.55);
            for (int i = 0; i < 10; i++)
            {
                double a = 0.0 + i * 0.32;
                var root = V(e.Cx + rx * 0.8 * Math.Cos(a), e.Cy - ry * 0.8 * Math.Sin(a));
                var tip = V(e.Cx + rx * 1.42 * Math.Cos(a - 0.15), e.Cy - ry * 1.45 * Math.Sin(a - 0.15));
                items.Add(It(leaf(root, tip, 0.34 * R, 0.06), i % 2 != 0 ? H.b : H.s));
                items.Add(It(leaf(lp(root, tip, 0.6), tip, 0.16 * R, 0.04), tipC));
            }
            clumps(S, "hair", items, S.lw * 0.55);
        }
        // 苏维汇发髻：头发梳向近侧太阳穴上方打结
        static void HairSuebian(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            var k = G.h3(1.3, 0.62, 1.08);
            hairCap(S, G, sp, T, H, new HairOpt { target = k, vol = 0.07 });
            bun(S, G, H, k[0], k[1] - 0.05 * R, 0.3 * R);
            clumps(S, "hair", new List<Item> { It(leaf(add(k, 0.1 * R, 0.1 * R), add(k, 0.5 * R, 0.95 * R), 0.36 * R, 0.1), H.s), It(leaf(add(k, 0, 0.1 * R), add(k, 0.25 * R, 1.1 * R), 0.3 * R, -0.08), H.b) }, S.lw * 0.55);
            S.add("hair", "<path d=\"" + taper(Pts(add(k, -0.28 * R, 0.12 * R), add(k, 0, 0.22 * R), add(k, 0.28 * R, 0.1 * R)), 0.12 * R) + "\" fill=\"" + sp.Hair.Or("tie", "#7a5a3a") + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
        }
        // 罗马女子：中分波浪，脑后挽髻
        static void HairRomanf(Svg S, Geo G, JObj sp, Theme T, HatInfo hatInfo, Tone H)
        {
            double R = G.R;
            hairCap(S, G, sp, T, H, new HairOpt
            {
                hairline = partLine(G), target = G.P(1.0, -0.2), vol = 0.08, extra = () =>
                {
                    var wd = new StringBuilder();
                    for (int i = 0; i < 4; i++) wd.Append(spline(Pts(G.P(-0.4, -0.95 + i * 0.12), G.P(0.0, -1.0 + i * 0.16), G.P(0.4, -0.85 + i * 0.16), G.P(0.85, -0.6 + i * 0.16))));
                    S.line("hair", wd.ToString(), H.d, 1.1, " opacity=\"0.6\"");
                }
            });
            bun(S, G, H, G.hx + 1.12 * R, G.ey - 0.25 * R, 0.36 * R);
        }
        static string flower(double x, double y, double r, string col)
        {
            var s = new StringBuilder();
            for (int i = 0; i < 5; i++) { double a = i * 1.2566; s.Append(circle(x + Math.Cos(a) * r * 0.62, y + Math.Sin(a) * r * 0.62, r * 0.48) + "/>"); }
            return "<g fill=\"" + col + "\" stroke=\"" + INK + "\" stroke-width=\"0.9\">" + s + "</g>" + circle(x, y, r * 0.34) + " fill=\"#e8b030\" stroke=\"#7a4a10\" stroke-width=\"0.6\"/>";
        }

        // ======================================================== 冠帽 ==
        static HatInfo DrawHat(Svg S, Geo G, JObj sp, Theme T)
        {
            string type = sp.Hat.Or("type", "none");
            HatFn fn;
            if (!HATS.TryGetValue(type, out fn)) return new HatInfo(false);
            return fn(S, G, sp, T) ?? new HatInfo(true);
        }
        static void bandShadow(Svg S, Geo G, List<double[]> pts, Theme T, double k = 0)
        {
            S.headShade.Add("<path d=\"" + spline(Cat(pts, Rev(shift(pts, 1, (k != 0 ? k : 0.14) * G.R))), true) + "\" fill=\"" + T.skin.d + "\" opacity=\"0.55\"/>");
        }
        // 条带（两条 band 之间）
        sealed class Strip { public List<double[]> lo, hi, pts; }
        static Strip bandStrip(Geo G, double bm0, double bm1, double ba, double s, int n = 0)
        {
            var lo = band(G, bm0, ba, s, n); var hi = band(G, bm1, ba, s, n);
            return new Strip { lo = lo, hi = hi, pts = Cat(lo, Rev(hi)) };
        }
        // 雉尾 / 鹖尾：中线 + 斑纹（stroke-dasharray）
        static void feather(Svg S, string layer, List<double[]> pts, double w, string col, string bar = null)
        {
            string d = taper(pts, t => w * (1 - t * 0.75) + 0.4);
            S.add(layer, "<path d=\"" + d + "\" fill=\"" + col + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.75) + "\"/>");
            if (S.o.lod >= 1) S.add(layer, "<path d=\"" + spline(pts) + "\" fill=\"none\" stroke=\"" + Js.Or(bar, mul(col, 0.45)) + "\" stroke-width=\"" + N(w * 0.8) + "\" stroke-dasharray=\"" + N(w * 0.55) + " " + N(w * 0.75) + "\" stroke-linecap=\"butt\" opacity=\"0.85\"/>");
            S.add(layer, "<path d=\"" + spline(pts) + "\" fill=\"none\" stroke=\"" + mix(col, "#fff", 0.5) + "\" stroke-width=\"0.8\" opacity=\"0.7\"/>");
        }
        // 红缨
        static void tassel(Svg S, string layer, Geo G, double[] top, string col, int n, double len, double dir = 1)
        {
            double R = G.R; var items = new List<Item>();
            if (dir == 0) dir = 1;
            var C = clothT(col);
            for (int i = 0; i < n; i++)
            {
                double a = -0.5 + (double)i / (n - 1) * 1.6;
                var tip = add(top, dir * Math.Cos(a) * len * R * (0.7 + 0.3 * ((i * 7) % 3) / 2.0), (Math.Sin(a) * 0.8 + 0.2) * len * R);
                items.Add(It(leaf(top, tip, 0.22 * R, dir * 0.12), i % 2 != 0 ? C.s : C.b));
            }
            clumps(S, layer, items, S.lw * 0.6);
        }
        static JObj HatOnly(JObj sp, JObj hat) { var o = sp.Clone(); o["hat"] = hat; return o; }

        static readonly Dictionary<string, HatFn> HATS = new Dictionary<string, HatFn>
        {
            { "none", (S, G, sp, T) => new HatInfo(false) },
            { "ze", (S, G, sp, T) => HatZe(S, G, sp, T, false) },
            { "jinxian", HatJinxian }, { "futou", HatFutou }, { "turban", HatTurban }, { "headband", HatHeadband }, { "lunjin", HatLunjin },
            { "wuguan", HatWuguan }, { "helmet", HatHelmet }, { "crown", HatCrown }, { "pheasant", HatPheasant }, { "daoist", HatDaoist },
            { "feathers", HatFeathers }, { "chief", HatChief }, { "wahelm", HatWahelm }, { "shaman", HatShaman }, { "yiband", HatYiband },
            { "jeolpung", (S, G, sp, T) => HatJeolpung(S, G, sp, T, false) }, { "birdfeather", (S, G, sp, T) => HatJeolpung(S, G, sp, T, true) },
            { "goldcrown", HatGoldcrown }, { "felt", HatFelt }, { "fur", (S, G, sp, T) => HatFur(S, G, sp, T, false) }, { "furcap", (S, G, sp, T) => HatFur(S, G, sp, T, true) },
            { "tallcrown", HatTallcrown }, { "flowerwrap", HatFlowerwrap }, { "pointcap", (S, G, sp, T) => HatPointcap(S, G, sp, T, false) }, { "tallhat", HatTallhat },
            { "diadem", HatDiadem }, { "tiara", HatTiara }, { "phrygian", HatPhrygian }, { "keffiyeh", HatKeffiyeh }, { "arabturban", HatArabturban },
            { "hatra", HatHatra }, { "laurel", HatLaurel }, { "galea", HatGalea }, { "celthelm", HatCelthelm }, { "spangen", HatSpangen }, { "conical", HatConical },
        };
        // 帻：包住发髻的头巾帽（介帻，后部隆起）
        static HatInfo HatZe(Svg S, Geo G, JObj sp, Theme T, bool flat)
        {
            string col = sp.Hat.Or("color", "#2a2226");
            var C = clothT(col); double R = G.R;
            double s = 1.08;
            var bnd = band(G, 0.16, 0.36, s);
            var cap = capOutline(G, bnd, s, 1.03);
            if (!flat)
            {
                // 介帻的“屋”：顶后部两片隆起
                var top = G.h3(1.05, 1.2, s);
                double[] a0 = G.h3(0.2, 1.0, s), a1 = G.h3(1.85, 0.62, s);
                string roof = spline(Pts(V(a0[0], a0[1], 0), V(top[0] - 0.45 * R, top[1] - 0.32 * R), V(top[0] - 0.05 * R, top[1] - 0.44 * R, 0.4), V(top[0] + 0.3 * R, top[1] - 0.3 * R), V(top[0] + 0.55 * R, top[1] - 0.05 * R), V(a1[0], a1[1], 0)), true);
                S.shape("hat", roof, C.b, () =>
                {
                    S.path("hat", spline(Pts(V(top[0] - 0.05 * R, top[1] - 0.6 * R), V(top[0] + 0.8 * R, top[1] - 0.2 * R), V(top[0] + 0.5 * R, top[1] + 0.6 * R), V(top[0] - 0.05 * R, top[1] + 0.2 * R)), true), C.s);
                    S.line("hat", spline(Pts(V(top[0] - 0.05 * R, top[1] - 0.44 * R), V(top[0] + 0.02 * R, top[1] - 0.05 * R))), C.d, 1.3);
                }, S.lw);
            }
            S.shape("hat", spline(cap, true), C.b, () =>
            {
                S.path("hat", spline(Pts(G.h3(0.95, 0.2, 1.3), G.h3(0.65, 1.2, 1.3), G.h3(2.2, 1.0, 1.3), G.h3(2.2, -0.2, 1.3)), true), C.s);
                S.path("hat", spline(Cat(bnd, Rev(shift(bnd, 0, -0.22 * R))), true), C.d, 0, " opacity=\"0.65\"");
                if (S.o.lod >= 1) S.path("hat", spline(Pts(G.h3(-0.6, 0.72, s), G.h3(-0.15, 0.95, s), G.h3(0.3, 1.04, s), G.h3(0.0, 0.88, s)), true), C.h, 0, " opacity=\"0.55\"");
            }, S.lw);
            bandShadow(S, G, bnd, T);
            return new HatInfo(true);
        }
        // 进贤冠：黑漆纱冠 + 前高后低的“展筩”，冠梁数表示品级
        static HatInfo HatJinxian(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#1e1a1e");
            HatZe(S, G, HatOnly(sp, new JObj { { "color", col } }), T, true);
            double R = G.R;
            double[] b0 = G.h3(-0.45, 0.78, 1.1), b1 = G.h3(0.95, 0.92, 1.1);
            double[] t0 = V(b0[0] + 0.02 * R, b0[1] - 1.0 * R), t1 = V(b1[0] + 0.42 * R, b1[1] - 0.62 * R);
            string plate = spline(Pts(V(b0[0], b0[1], 0), V(t0[0], t0[1], 0.2), V(t0[0] + 0.3 * R, t0[1] - 0.08 * R, 0.3), V(t1[0], t1[1], 0.2), V(b1[0], b1[1], 0)), true);
            S.shape("hat", plate, mix(col, "#3a3038", 0.6), () =>
            {
                S.path("hat", poly(Pts(b1, t1, V(t1[0] + 0.3 * R, t1[1] + 0.3 * R), V(b1[0] + 0.3 * R, b1[1]))), mul(col, 0.7));
                double liang = sp.Hat.Or("liang", 2);
                var ld = new StringBuilder();
                for (int i = 1; i <= liang; i++) { double k = i / (liang + 1); ld.Append("M" + ps(lp(b0, b1, k)) + "L" + ps(lp(V(t0[0] + 0.15 * R, t0[1]), t1, k))); }
                S.line("hat", ld.ToString(), "#6a6070", 1.6);
            }, S.lw);
            // 冠缨（系于颔下）
            var st = G.h3(1.15, 0.05, 1.06);
            S.line("hat", spline(Pts(st, G.P(0.86, 0.5), G.P(0.72, 1.15))), INK, 2.4);
            S.line("hat", spline(Pts(st, G.P(0.86, 0.5), G.P(0.72, 1.15))), sp.Hat.Or("color2", "#7a3a30"), 1.1);
            return new HatInfo(true);
        }
        // 幅巾：软巾裹头，顶上打结，巾尾垂于脑后
        static HatInfo HatFutou(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#5a4a3a");
            var C = clothT(col); double R = G.R;
            double s = 1.1;
            var bnd = band(G, 0.14, 0.36, s);
            var cap = capOutline(G, bnd, s, 1.1);
            // 巾尾
            var k0 = G.h3(1.9, 0.5, s);
            clumps(S, "back", new List<Item> { It(leaf(k0, G.P(1.55, 1.25), 0.32 * R, 0.1), C.s), It(leaf(k0, G.P(1.3, 1.5), 0.3 * R, -0.05), C.b) }, S.lw * 0.6);
            S.shape("hat", spline(cap, true), C.b, () =>
            {
                S.path("hat", spline(Pts(G.h3(0.9, 0.2, 1.3), G.h3(0.6, 1.3, 1.3), G.h3(2.2, 1.0, 1.3), G.h3(2.2, -0.2, 1.3)), true), C.s);
                var fd = new StringBuilder();
                for (int i = 0; i < 3; i++) fd.Append(spline(Pts(G.h3(-0.9 + i * 0.4, 0.55 + i * 0.15, s), G.h3(-0.1 + i * 0.4, 1.0 + i * 0.12, s), G.h3(0.8 + i * 0.3, 1.25, s))));
                S.line("hat", fd.ToString(), C.d, 1.3, " opacity=\"0.7\"");
                if (S.o.lod >= 1) S.path("hat", spline(Pts(G.h3(-0.6, 0.7, s), G.h3(-0.2, 0.95, s), G.h3(0.2, 1.05, s), G.h3(-0.05, 0.85, s)), true), C.h, 0, " opacity=\"0.5\"");
            }, S.lw);
            // 顶结
            var kt = G.h3(0.35, 1.25, s * 1.02);
            clumps(S, "hat", new List<Item> { It(leaf(kt, add(kt, -0.42 * R, -0.3 * R), 0.3 * R, 0.2), C.b), It(leaf(kt, add(kt, 0.38 * R, -0.36 * R), 0.3 * R, -0.2), C.s) }, S.lw * 0.6);
            S.add("hat", circle(kt[0], kt[1], 0.12 * R) + " fill=\"" + C.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            bandShadow(S, G, bnd, T);
            return new HatInfo(true);
        }
        // 头巾：裹头的布巾，斜向缠绕，脑后打结垂尾
        static HatInfo HatTurbanBase(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#3a6a3a");
            var C = clothT(col); double R = G.R;
            double s = 1.13;
            var bnd = band(G, 0.12, 0.38, s);
            var cap = capOutline(G, bnd, s, 1.1);
            var k0 = G.h3(2.05, 0.75, s);
            clumps(S, "back", new List<Item> { It(leaf(k0, G.P(1.7, 1.45), 0.4 * R, 0.12), C.s), It(leaf(k0, G.P(1.35, 1.75), 0.36 * R, -0.08), C.b) }, S.lw * 0.6);
            S.shape("hat", spline(cap, true), C.b, () =>
            {
                S.path("hat", spline(Pts(G.h3(0.95, 0.1, 1.4), G.h3(0.6, 1.3, 1.4), G.h3(2.2, 1.0, 1.4), G.h3(2.2, -0.2, 1.4)), true), C.s);
                // 缠绕的布褶
                for (int i = 0; i < 4; i++)
                {
                    var pts = new List<double[]>();
                    for (int j = 0; j <= 10; j++) { double lam = G.lamFar + (G.lamNear - G.lamFar) * j / 10; pts.Add(G.h3(lam, 0.46 + i * 0.24 - 0.22 * Math.Sin(lam - 0.4) + 0.1 * Math.Cos(lam), s * 1.01)); }
                    S.path("hat", spline(Cat(pts, Rev(shift(pts, 0, 0.09 * R))), true), C.d, 0, " opacity=\"0.45\"");
                    if (S.o.lod >= 1) S.line("hat", spline(shift(pts, 0, -0.05 * R)), C.h, 1.2, " opacity=\"0.45\"");
                }
            }, S.lw);
            // 额前的布沿
            var fr = bandStrip(G, 0.12, 0.3, 0.38, s * 1.01);
            S.shape("hat", spline(fr.pts, true), C.b, () =>
            {
                S.path("hat", spline(Cat(fr.hi, Rev(shift(fr.hi, 0, 0.06 * R))), true), C.h, 0, " opacity=\"0.5\"");
            }, S.lw * 0.9);
            if (sp.Hat.T("gem")) S.add("hat", circle(G.h3(0, 0.62, s * 1.02)[0], G.h3(0, 0.62, s * 1.02)[1], 0.09 * R) + " fill=\"" + Js.Str(sp.Hat.G("gem")) + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
            var kn = G.h3(1.85, 0.85, s * 1.02);
            S.add("hat", ellipse(kn[0], kn[1], 0.2 * R, 0.16 * R, 20) + " fill=\"" + C.s + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            bandShadow(S, G, bnd, T, 0.18);
            return new HatInfo(true);
        }
        // 头巾补充：noTails（不垂巾尾）、big（更大的缠头）
        static HatInfo HatTurban(Svg S, Geo G, JObj sp, Theme T)
        {
            if (!sp.Hat.T("noTails") && !sp.Hat.T("big")) return HatTurbanBase(S, G, sp, T);
            string col = sp.Hat.Or("color", "#ece6da");
            var C = clothT(col); double R = G.R;
            double s = sp.Hat.T("big") ? 1.2 : 1.13;
            var sh = capShape(G, 0.12, 0.38, s, sp.Hat.T("big") ? 1.2 : 1.1);
            S.shape("hat", spline(sh.cap, true), C.b, () =>
            {
                capShade(S, G, C);
                for (int i = 0; i < 5; i++)
                {
                    var pts = new List<double[]>();
                    for (int j = 0; j <= 10; j++) { double lam = G.lamFar + (G.lamNear - G.lamFar) * j / 10; pts.Add(G.h3(lam, 0.4 + i * 0.22 - 0.25 * Math.Sin(lam - 0.4) + 0.1 * Math.Cos(lam), s * 1.01)); }
                    S.path("hat", spline(Cat(pts, Rev(shift(pts, 0, 0.08 * R))), true), C.d, 0, " opacity=\"0.4\"");
                }
            }, S.lw);
            bandShadow(S, G, sh.bnd, T, 0.18);
            return new HatInfo(true);
        }
        // 抹额：额上一条带子，脑后系结
        static HatInfo HatHeadband(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#a02a24");
            var C = clothT(col); double R = G.R;
            var st = bandStrip(G, 0.08, 0.25, 0.36, 1.05);
            var k0 = G.h3(G.lamNear - 0.15, 0.2, 1.05);
            clumps(S, "hat", new List<Item> { It(leaf(k0, add(k0, 0.65 * R, 0.85 * R), 0.22 * R, 0.15), C.b), It(leaf(k0, add(k0, 0.35 * R, 1.1 * R), 0.2 * R, -0.1), C.s) }, S.lw * 0.55);
            S.shape("hat", spline(st.pts, true), C.b, () =>
            {
                S.path("hat", spline(Cat(Slice(st.lo, 9), Rev(Slice(st.hi, 9))), true), C.s);
                S.path("hat", spline(Cat(Slice(st.hi, 0, 10), Rev(shift(Slice(st.hi, 0, 10), 0, 0.05 * R))), true), C.h, 0, " opacity=\"0.55\"");
            }, S.lw * 0.9);
            if (sp.Hat.T("gem")) { var g = G.h3(0, 0.5, 1.07); S.add("hat", circle(g[0], g[1], 0.1 * R) + " fill=\"" + Js.Str(sp.Hat.G("gem")) + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>"); }
            bandShadow(S, G, st.lo, T, 0.1);
            return new HatInfo(false);
        }
        // 纶巾：青丝软巾，方顶，两条飘带
        static HatInfo HatLunjin(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#2c3a58");
            var C = clothT(col); double R = G.R;
            double s = 1.1;
            var bnd = band(G, 0.16, 0.36, s);
            var cap = capOutline(G, bnd, s, 1.42);
            double yTop = double.MaxValue; foreach (var p in cap) yTop = Math.Min(yTop, p[1]);
            var cap2 = new List<double[]>();
            for (int i = 0; i < cap.Count; i++) { var p = cap[i]; cap2.Add(i >= bnd.Count ? new[] { p[0], lerp(p[1], yTop, 0.45), p[2] } : p); }
            cap = cap2;
            // 飘带
            var k0 = G.h3(1.95, 0.55, s);
            feather(S, "back", Pts(k0, G.P(1.45, 0.4), G.P(1.75, 1.2), G.P(1.55, 2.1)), 0.2 * R, sp.Hat.Or("color2", "#3a4a6a"), "none");
            feather(S, "back", Pts(k0, G.P(1.25, 0.6), G.P(1.35, 1.5), G.P(1.1, 2.3)), 0.18 * R, sp.Hat.Or("color2", "#3a4a6a"), "none");
            S.shape("hat", spline(cap, true), C.b, () =>
            {
                S.path("hat", spline(Pts(G.h3(0.9, 0.1, 1.5), G.h3(0.7, 1.4, 1.6), V(G.hx + 2 * R, yTop - R), G.h3(2.2, -0.2, 1.5)), true), C.s);
                var pd = new StringBuilder();
                for (int i = 0; i < 6; i++) { double lam = -0.8 + i * 0.42; var b0 = G.h3(lam, 0.28 + 0.36 * Math.Cos(lam) + 0.04, s); pd.Append("M" + ps(b0) + "L" + ps(V(b0[0] + (lam - 0.6) * 0.06 * R, yTop + 0.12 * R))); }
                S.line("hat", pd.ToString(), C.d, 1.3, " opacity=\"0.7\"");
                S.path("hat", spline(Cat(bnd, Rev(shift(bnd, 0, -0.2 * R))), true), C.h, 0, " opacity=\"0.35\"");
            }, S.lw);
            bandShadow(S, G, bnd, T);
            return new HatInfo(true);
        }
        // 武冠（鹖冠）：黑纱笼冠，两侧插鹖尾
        static HatInfo HatWuguan(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#1e1a1e");
            double R = G.R;
            HatZe(S, G, HatOnly(sp, new JObj { { "color", col } }), T, true);
            double[] p0 = G.h3(-0.95, 0.45, 1.14), p1 = G.h3(1.75, 0.2, 1.14);
            double top = G.ey - 2.05 * R;
            string box = spline(Pts(p0, V(p0[0] - 0.12 * R, top + 0.12 * R, 0.3), V(lerp(p0[0], p1[0], 0.5), top - 0.08 * R, 1), V(p1[0] + 0.2 * R, top + 0.05 * R, 0.3), p1, V(lerp(p0[0], p1[0], 0.55), G.ey - 0.82 * R)), true);
            S.shape("hat", box, "#16121a", () =>
            {
                if (S.o.lod >= 2)
                {
                    var md = new StringBuilder();
                    for (double x = p0[0] - 0.2 * R; x < p1[0] + 0.3 * R; x += 0.12 * R) md.Append("M" + N(x) + "," + N(top - 0.2 * R) + "l" + N(0.2 * R) + "," + N(2 * R));
                    S.line("hat", md.ToString(), "#4a4450", 0.6, " opacity=\"0.6\"");
                }
                S.path("hat", spline(Pts(V(p1[0] - 0.3 * R, top), V(p1[0] + 0.4 * R, top), V(p1[0] + 0.4 * R, p1[1]), V(p1[0] - 0.1 * R, p1[1])), true), "#08060a", 0, " opacity=\"0.6\"");
            }, S.lw, null, 0.82);
            string fc = sp.Hat.Or("color2", "#7a5a3a");
            feather(S, "hat", Pts(G.h3(1.5, 0.55, 1.15), V(p1[0] + 0.05 * R, top - 0.3 * R), V(p1[0] + 0.3 * R, top - 1.1 * R)), 0.16 * R, fc);
            feather(S, "back", Pts(G.h3(-0.9, 0.6, 1.15), V(p0[0] - 0.15 * R, top - 0.2 * R), V(p0[0] - 0.05 * R, top - 0.9 * R)), 0.12 * R, fc);
            return new HatInfo(true);
        }
        // 兜鍪（头盔）。variant：plain 普通 / wing 凤翅 / lion 狮首 / horn 角 / spike 尖顶
        static HatInfo HatHelmetBase(Svg S, Geo G, JObj sp, Theme T)
        {
            string v = sp.Hat.Or("variant", "plain");
            string metal = Js.Or(sp.Hat.S("color"), sp.Outfit.Or("metal", "#8a8f9a"));
            var Mt = metalT(metal); double R = G.R; int lo = S.o.lod;
            string trim = sp.Hat.Or("trim", GOLD);
            double s = 1.14;
            var bnd = band(G, 0.08, 0.34, s);
            var cap = capOutline(G, bnd, s, v == "spike" ? 1.25 : 1.1);
            // 顿项（护颈）：近侧脑后垂到肩，远侧露出一条
            var ng = Pts(G.h3(1.3, 0.05, s), G.h3(1.75, -0.02, s), G.h3(G.lamNear - 0.02, -0.06, s), G.P(1.48, 0.95), G.P(1.6, 1.62, 0.4), G.P(0.98, 1.7, 0.4), G.P(0.78, 0.8));
            string ngC = sp.Hat.Or("neck", mix(metal, "#3a2a22", 0.4));
            S.shape("back", spline(ng, true), ngC, () =>
            {
                var rd = new StringBuilder();
                for (int i = 1; i < 5; i++) rd.Append(spline(Pts(G.P(0.7, 0.05 + i * 0.36), G.P(1.15, 0.12 + i * 0.36), G.P(1.7, 0.1 + i * 0.36))));
                S.line("back", rd.ToString(), mul(ngC, 0.55), 1.4);
                S.path("back", spline(Pts(G.P(1.15, -0.2), G.P(1.7, 0), G.P(1.7, 1.8), G.P(1.3, 1.8)), true), mul(ngC, 0.7), 0, " opacity=\"0.6\"");
            }, S.lw);
            var nf = Pts(G.h3(G.lamFar + 0.05, 0.1, s), G.P(-1.2, 0.3), G.P(-1.2, 1.0, 0.4), G.P(-0.9, 1.05));
            S.shape("back", spline(nf, true), mul(ngC, 0.8), null, S.lw);
            // 盔钵
            S.shape("hat", spline(cap, true), Mt.b, () =>
            {
                S.path("hat", spline(Pts(G.h3(0.85, 0.0, 1.5), G.h3(0.55, 1.3, 1.5), V(G.hx + 2.2 * R, G.ey - 2.4 * R), G.h3(2.2, -0.2, 1.5)), true), Mt.s);
                S.path("hat", spline(Pts(G.h3(-0.75, 0.5, s), G.h3(-0.45, 1.0, s), G.h3(-0.05, 1.22, s), G.h3(-0.2, 0.95, s), G.h3(-0.55, 0.62, s)), true), Mt.h, 0, " opacity=\"0.75\"");
                // 盔梁（铆钉竖条）
                var md = new StringBuilder(); var rv = new StringBuilder();
                foreach (var lam in new[] { -0.55, -0.05, 0.45, 0.95, 1.45 })
                {
                    var pts = new List<double[]>();
                    for (double b = 0.3 + 0.36 * Math.Cos(lam) + 0.04; b < 1.45; b += 0.22) pts.Add(G.h3(lam, b, s * 1.005));
                    md.Append(spline(pts));
                    if (lo >= 2) foreach (var p in pts) rv.Append(circle(p[0], p[1], 0.9) + " fill=\"" + Mt.h + "\"/>");
                }
                S.line("hat", md.ToString(), Mt.d, 1.6);
                if (rv.Length > 0) S.add("hat", rv.ToString());
            }, S.lw);
            // 眉庇与金边
            var vis = Slice(band(G, 0.04, 0.34, s * 1.03, 16), 2, 13);
            var visB = Rev(shift(vis, 0, 0.11 * R));
            var visB2 = new List<double[]>();
            for (int i = 0; i < visB.Count; i++) { var p = visB[i]; visB2.Add(V(p[0] - ((double)i / (visB.Count - 1) - 0.5) * 0.06 * R, p[1])); }
            string visD = spline(Cat(vis, visB2), true);
            S.shape("hat", visD, Mt.s, () => { S.path("hat", spline(Cat(shift(vis, 0, -0.01 * R), Rev(shift(vis, 0, 0.04 * R))), true), Mt.h, 0, " opacity=\"0.7\""); }, S.lw * 0.9);
            S.line("hat", spline(Slice(bnd, 1, -1)), INK, 3.6);
            S.line("hat", spline(Slice(bnd, 1, -1)), trim, 1.8);
            bandShadow(S, G, vis, T, 0.2);
            var apex = G.h3(0.6, 1.5, s * (v == "spike" ? 1.2 : 1.08));
            if (v == "wing")
            {
                // 凤翅：两侧上扬的金翅
                var w0 = G.h3(1.3, 0.55, s);
                var wing = Pts(w0, add(w0, 0.3 * R, -0.5 * R), add(w0, 0.75 * R, -1.25 * R), add(w0, 0.95 * R, -1.15 * R), add(w0, 0.6 * R, -0.35 * R), add(w0, 0.3 * R, 0.15 * R));
                S.shape("hat", spline(wing, true), trim, () => { S.line("hat", spline(Pts(add(w0, 0.25 * R, -0.1 * R), add(w0, 0.55 * R, -0.6 * R), add(w0, 0.82 * R, -1.1 * R))), mul(trim, 0.6), 1.4); }, S.lw * 0.9);
                var w1 = G.h3(-0.85, 0.55, s);
                S.shape("hat", spline(Pts(w1, add(w1, -0.2 * R, -0.5 * R), add(w1, -0.4 * R, -1.0 * R), add(w1, -0.25 * R, -0.95 * R), add(w1, 0.05 * R, -0.3 * R)), true), mul(trim, 0.85), null, S.lw * 0.9);
            }
            if (v == "horn")
            {
                var h0 = G.h3(0.0, 0.95, s);
                feather(S, "hat", Pts(h0, add(h0, -0.3 * R, -0.6 * R), add(h0, -0.15 * R, -1.1 * R)), 0.22 * R, trim, "none");
            }
            if (v == "lion") lionMask(S, G, sp, s, trim);
            // 盔顶：金顶与红缨
            tassel(S, "hat", G, apex, sp.Hat.Or("plume", "#c0302a"), lo == 0 ? 4 : 7, v == "lion" ? 1.4 : 1.0, 1);
            S.add("hat", circle(apex[0], apex[1], 0.13 * R) + " fill=\"" + trim + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            S.add("hat", "<path d=\"M" + N(apex[0]) + "," + N(apex[1] - 0.12 * R) + "l" + N(-0.07 * R) + "," + N(-0.35 * R) + "l" + N(0.14 * R) + ",0z\" fill=\"" + trim + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.6) + "\"/>");
            return new HatInfo(true);
        }
        // 头盔补充变体：round 圆钵宽檐 / spike 尖顶 / plume 高羽（高句丽）
        static HatInfo HatHelmet(Svg S, Geo G, JObj sp, Theme T)
        {
            string v = sp.Hat.Or("variant", "plain");
            if (v == "plain" || v == "wing" || v == "lion" || v == "horn") return HatHelmetBase(S, G, sp, T);
            double R = G.R;
            string metal = Js.Or(sp.Hat.S("color"), sp.Outfit.Or("metal", "#8a8f9a"));
            string ngC = sp.Hat.Or("neck", mix(metal, "#3a2a22", 0.4));
            var ng = Pts(G.h3(1.3, 0.05, 1.14), G.h3(G.lamNear - 0.02, -0.06, 1.14), G.P(1.48, 0.95), G.P(1.6, 1.62, 0.4), G.P(0.98, 1.7, 0.4), G.P(0.78, 0.8));
            S.shape("back", spline(ng, true), ngC, () =>
            {
                var rd = new StringBuilder();
                for (int i = 1; i < 5; i++) rd.Append(spline(Pts(G.P(0.7, 0.05 + i * 0.36), G.P(1.15, 0.12 + i * 0.36), G.P(1.7, 0.1 + i * 0.36))));
                S.line("back", rd.ToString(), mul(ngC, 0.55), 1.4);
            }, S.lw);
            S.shape("back", spline(Pts(G.h3(G.lamFar + 0.05, 0.1, 1.14), G.P(-1.2, 0.3), G.P(-1.2, 1.0, 0.4), G.P(-0.9, 1.05)), true), mul(ngC, 0.8), null, S.lw);
            double[] apex = v == "spike" ? V(G.hx + 0.05 * R, G.ey - 2.45 * R) : null;
            bowl(S, G, sp, T, new BowlOpt { metal = metal, cone = v == "spike", apex = apex, lift = 1.12, ribs = new[] { -0.55, 0.2, 0.95 }, trim = sp.Hat.G("trim") });
            if (v == "round")
            {
                // 宽檐
                var brim = Slice(band(G, 0.02, 0.34, 1.2, 16), 1, -1);
                S.add("hat", "<path d=\"" + taper(brim, t => 0.12 * R * Math.Sin(Math.PI * clamp(t * 1.05, 0, 1)) + 0.5) + "\" fill=\"" + mix(metal, "#fff", 0.1) + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
                var top = G.h3(0.6, 1.48, 1.18);
                tassel(S, "hat", G, top, sp.Hat.Or("plume", "#c0302a"), S.o.lod == 0 ? 4 : 6, 0.9, 1);
                S.add("hat", circle(top[0], top[1], 0.12 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.3\"/>");
            }
            else if (v == "spike")
            {
                S.add("hat", "<path d=\"M" + N(apex[0] - 0.08 * R) + "," + N(apex[1] + 0.1 * R) + "L" + N(apex[0]) + "," + N(apex[1] - 0.6 * R) + "L" + N(apex[0] + 0.08 * R) + "," + N(apex[1] + 0.1 * R) + "Z\" fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
                tassel(S, "hat", G, add(apex, 0, -0.1 * R), sp.Hat.Or("plume", "#c0302a"), 4, 0.6, 1);
            }
            else if (v == "plume")
            {
                var top = G.h3(0.5, 1.45, 1.16);
                string pl = sp.Hat.S("plume");
                feather(S, "hat", Pts(top, add(top, -0.1 * R, -0.7 * R), add(top, 0.25 * R, -1.4 * R)), 0.2 * R, !string.IsNullOrEmpty(pl) && pl != "#c0302a" ? pl : "#ece6d8");
                feather(S, "hat", Pts(top, add(top, 0.3 * R, -0.6 * R), add(top, 0.75 * R, -1.15 * R)), 0.18 * R, "#c8a050");
            }
            return new HatInfo(true);
        }
        // 束发金冠：小冠罩在发髻上（前高后低的弧形冠），横插玉簪
        static HatInfo HatCrown(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            var H = hairT(sp.Hair.S("color"));
            string hs = sp.Hair.S("style");
            HAIR[hs != null && HAIR.ContainsKey(hs) ? hs : "topknot"](S, G, sp, T, new HatInfo(false, true), H);
            double bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R, r = 0.44 * R;
            string gc = sp.Hat.Or("color", GOLD);
            var Mt = metalT(gc);
            string cr = spline(Pts(V(bx - r * 1.05, by + r * 0.55, 0.3), V(bx - r * 1.08, by - r * 0.2), V(bx - r * 0.55, by - r * 1.05), V(bx + r * 0.25, by - r * 1.12), V(bx + r * 0.9, by - r * 0.5), V(bx + r * 1.02, by + r * 0.45, 0.3), V(bx, by + r * 0.72)), true);
            S.shape("hat", cr, Mt.b, () =>
            {
                S.path("hat", spline(Pts(V(bx + r * 0.15, by - r * 1.3), V(bx + r * 1.3, by - r * 0.6), V(bx + r * 1.2, by + r), V(bx + r * 0.3, by + r)), true), Mt.s);
                S.path("hat", spline(Pts(V(bx - r * 0.85, by - r * 0.1), V(bx - r * 0.5, by - r * 0.8), V(bx - r * 0.1, by - r * 0.9), V(bx - r * 0.55, by - r * 0.45)), true), Mt.h, 0, " opacity=\"0.85\"");
                if (S.o.lod >= 1)
                {
                    var ld = new StringBuilder();
                    for (int i = 1; i <= 3; i++) { double k = i * 0.22; ld.Append(spline(Pts(V(bx - r * (1.05 - k), by + r * 0.5), V(bx - r * (0.7 - k * 0.5), by - r * (0.9 - k)), V(bx + r * (0.3 - k * 0.2), by - r * (1.05 - k)), V(bx + r * (0.95 - k * 0.6), by + r * 0.4)))); }
                    S.line("hat", ld.ToString(), Mt.d, 1.1, " opacity=\"0.8\"");
                }
                S.path("hat", spline(Pts(V(bx - r * 1.1, by + r * 0.35), V(bx, by + r * 0.5), V(bx + r * 1.1, by + r * 0.3), V(bx + r * 1.1, by + r * 0.75), V(bx, by + r * 0.9), V(bx - r * 1.1, by + r * 0.75)), true), Mt.d, 0, " opacity=\"0.55\"");
            }, S.lw);
            if (!sp.Hat.IsFalse("gem"))
            {
                string gem = Js.Or(Js.Str(sp.Hat.G("gem")), "#c83040");
                S.add("hat", circle(bx - r * 0.3, by - r * 0.15, r * 0.2) + " fill=\"" + gem + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>" + circle(bx - r * 0.36, by - r * 0.22, r * 0.07) + " fill=\"#fff\" opacity=\"0.85\"/>");
            }
            if (sp.Hat.T("pearls")) for (int i = 0; i < 3; i++) S.add("hat", circle(bx - r * (0.55 - i * 0.5), by - r * (1.0 + (i == 1 ? 0.18 : 0)), r * 0.14) + " fill=\"#f4f0e8\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
            string pin = sp.Hat.Or("pin", "#e8f0e0");
            S.line("hat", "M" + N(bx - 2.0 * r) + "," + N(by + 0.42 * r) + "L" + N(bx + 2.0 * r) + "," + N(by - 0.05 * r), INK, 3.6);
            S.line("hat", "M" + N(bx - 1.96 * r) + "," + N(by + 0.42 * r) + "L" + N(bx + 1.96 * r) + "," + N(by - 0.05 * r), pin, 2.0);
            return new HatInfo(true, false, true);
        }
        // 紫金冠 + 雉翎（吕布）
        static HatInfo HatPheasant(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            double bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
            string fc = sp.Hat.Or("color2", "#c8903a");
            // 两根长雉翎：从冠后升起，向两侧弯成长弧（经典的吕布形象），翎尾垂向两肩之外
            feather(S, "back", Pts(V(bx - 0.2 * R, by - 0.05 * R), V(bx - 0.55 * R, by - 0.62 * R), V(bx - 1.3 * R, by - 0.98 * R), V(bx - 2.2 * R, by - 0.88 * R), V(bx - 2.95 * R, by - 0.3 * R), V(bx - 3.3 * R, by + 0.5 * R)), 0.26 * R, fc, "#4a2a14");
            feather(S, "back", Pts(V(bx + 0.15 * R, by - 0.05 * R), V(bx + 0.5 * R, by - 0.68 * R), V(bx + 1.25 * R, by - 1.02 * R), V(bx + 2.1 * R, by - 0.92 * R), V(bx + 2.75 * R, by - 0.35 * R), V(bx + 3.0 * R, by + 0.45 * R)), 0.26 * R, mix(fc, "#fff", 0.1), "#4a2a14");
            return HatCrown(S, G, HatOnly(sp, JObj.Assign(new JObj { { "gem", "#7a3aa8" }, { "pearls", true } }, sp.Hat, new JObj { { "color", sp.Hat.Or("color", "#d8a83a") } })), T);
        }
        // 道冠：莲花冠（张鲁等）
        static HatInfo HatDaoist(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            var H = hairT(sp.Hair.S("color"));
            HairTopknot(S, G, sp, T, new HatInfo(false, true), H);
            double bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
            string gc = sp.Hat.Or("color", "#d8b04a");
            var items = new List<Item>();
            for (int i = 0; i < 5; i++)
            {
                double a = Math.PI * (0.15 + i * 0.175);
                items.Add(It(leaf(V(bx, by + 0.3 * R), V(bx + Math.Cos(a) * 0.62 * R, by + 0.25 * R - Math.Sin(a) * 0.75 * R), 0.36 * R, 0), i % 2 != 0 ? mul(gc, 0.8) : gc));
            }
            clumps(S, "hat", items, S.lw * 0.6);
            S.line("hat", "M" + N(bx - 0.8 * R) + "," + N(by + 0.25 * R) + "L" + N(bx + 0.8 * R) + "," + N(by + 0.05 * R), INK, 3.4);
            S.line("hat", "M" + N(bx - 0.78 * R) + "," + N(by + 0.25 * R) + "L" + N(bx + 0.78 * R) + "," + N(by + 0.05 * R), "#e8e0c8", 1.8);
            return new HatInfo(true, false, true);
        }
        // 狮首盔的狮面（额前）
        static void lionMask(Svg S, Geo G, JObj sp, double s, string trim)
        {
            double R = G.R;
            var c = G.h3(-0.05, 0.6, s * 1.05);
            var Mt = metalT(trim);
            R *= 1.3;
            var mane = new List<Item>();
            for (int i = 0; i < 9; i++)
            {
                double a = Math.PI * (0.05 + i * 0.11);
                mane.Add(It(leaf(c, add(c, Math.Cos(a) * 0.55 * R, -Math.Sin(a) * 0.5 * R - 0.05 * R), 0.22 * R, 0.1), i % 2 != 0 ? Mt.s : Mt.b));
            }
            clumps(S, "hat", mane, S.lw * 0.5);
            string face = spline(Pts(add(c, -0.26 * R, -0.12 * R), add(c, 0, -0.26 * R), add(c, 0.26 * R, -0.12 * R), add(c, 0.2 * R, 0.16 * R), add(c, 0, 0.26 * R), add(c, -0.2 * R, 0.16 * R)), true);
            S.shape("hat", face, Mt.b, () => { S.path("hat", spline(Pts(add(c, 0.05 * R, -0.3 * R), add(c, 0.3 * R, -0.1 * R), add(c, 0.2 * R, 0.3 * R), add(c, 0.05 * R, 0.3 * R)), true), Mt.s); }, S.lw * 0.7);
            S.line("hat", "M" + ps(add(c, -0.16 * R, -0.06 * R)) + "q" + N(0.06 * R) + "," + N(-0.05 * R) + " " + N(0.11 * R) + ",0M" + ps(add(c, 0.05 * R, -0.06 * R)) + "q" + N(0.06 * R) + "," + N(-0.05 * R) + " " + N(0.11 * R) + ",0", INK, 1.4);
            S.add("hat", "<path d=\"M" + ps(add(c, -0.12 * R, 0.08 * R)) + "q" + N(0.12 * R) + "," + N(0.12 * R) + " " + N(0.24 * R) + ",0\" fill=\"" + INK + "\" opacity=\"0.8\"/>");
            S.add("hat", ellipse(c[0] - 0.02 * R, c[1] + 0.02 * R, 0.05 * R, 0.035 * R) + " fill=\"" + INK + "\"/>");
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static partial class Portrait
    {
        // ======================================================== 各文化冠帽 ==
        sealed class Shp { public List<double[]> bnd, cap; }
        // 一般的“冠顶”形状：band 以上，可调高度与平顶
        static Shp capShape(Geo G, double bm, double ba, double s, double lift = 0, double flat = 0)
        {
            var bnd = band(G, bm, ba, s);
            var cap = capOutline(G, bnd, s, lift != 0 ? lift : 1.05);
            if (flat != 0)
            {
                double yTop = double.MaxValue; foreach (var p in cap) yTop = Math.Min(yTop, p[1]);
                var c2 = new List<double[]>();
                for (int i = 0; i < cap.Count; i++) { var p = cap[i]; c2.Add(i >= bnd.Count ? new[] { p[0], lerp(p[1], yTop, flat), p[2] } : p); }
                cap = c2;
            }
            return new Shp { bnd = bnd, cap = cap };
        }
        // 尖顶冠（圆锥）：底边 band，顶点 apex
        static Shp coneShape(Geo G, double bm, double ba, double s, double[] apex, double bulge = 0)
        {
            var bnd = band(G, bm, ba, s);
            double[] a = bnd[0], b = bnd[bnd.Count - 1];
            double[] mA = lp(a, apex, 0.5), mB = lp(b, apex, 0.5);
            double bl = bulge != 0 ? bulge : 0.12;
            return new Shp { bnd = bnd, cap = Cat(bnd, V(mB[0] + bl * G.R, mB[1] - bl * 0.3 * G.R), V(apex[0], apex[1], 0.2), V(mA[0] - bl * G.R, mA[1] - bl * 0.3 * G.R)) };
        }
        // 冠顶的通用着色：背光侧 + 迎光高光
        static void capShade(Svg S, Geo G, Tone C, string lay = "hat")
        {
            S.path(lay, spline(Pts(G.h3(0.9, -0.1, 1.6), G.h3(0.55, 1.4, 1.7), V(G.hx + 2.5 * G.R, G.ey - 3 * G.R), G.h3(2.3, -0.3, 1.6)), true), C.s);
            if (S.o.lod >= 1) S.path(lay, spline(Pts(G.h3(-0.7, 0.6, 1.12), G.h3(-0.3, 0.95, 1.12), G.h3(0.1, 1.15, 1.12), G.h3(-0.15, 0.85, 1.12)), true), C.h, 0, " opacity=\"0.5\"");
        }
        // 飘带（两条）
        static void ribbons(Svg S, Geo G, double[] from, string col, double len = 1)
        {
            double R = G.R;
            if (len == 0) len = 1;
            feather(S, "back", Pts(from, add(from, 0.35 * R, 0.5 * R * len), add(from, 0.2 * R, 1.2 * R * len), add(from, 0.45 * R, 1.9 * R * len)), 0.16 * R, col, "none");
            feather(S, "back", Pts(from, add(from, 0.6 * R, 0.35 * R * len), add(from, 0.75 * R, 1.0 * R * len), add(from, 1.05 * R, 1.6 * R * len)), 0.14 * R, col, "none");
        }
        // 毛边：沿折线排一圈短毛绺
        static void furEdge(Svg S, string layer, Geo G, List<double[]> pts, string col, double len, int n = 0)
        {
            var C = clothT(col); double R = G.R; var items = new List<Item>();
            var sm = resample(pts, n != 0 ? n : 14); var nm = normals(sm);
            for (int i = 0; i < sm.Count; i++)
            {
                double k = i % 2 != 0 ? 0.7 : 1;
                var tip = add(sm[i], nm[i][0] * len * R * k, nm[i][1] * len * R * k);
                items.Add(It(leaf(add(sm[i], -nm[i][0] * len * R * 0.6, -nm[i][1] * len * R * 0.6), tip, 0.26 * R, i % 2 != 0 ? 0.15 : -0.15), i % 2 != 0 ? C.s : C.b));
            }
            clumps(S, layer, items, S.lw * 0.5);
        }
        sealed class BowlOpt
        {
            public string metal; public bool cone; public double[] apex; public double s, bm, lift, ribW; public double[] ribs; public string ribC;
            public object trim; public Action<Tone> extra;
        }
        // 头盔通用：盔钵 + 金边
        static Shp bowl(Svg S, Geo G, JObj sp, Theme T, BowlOpt opt)
        {
            double R = G.R;
            var Mt = metalT(opt.metal);
            double s = opt.s != 0 ? opt.s : 1.14;
            double bm = opt.bm != 0 ? opt.bm : 0.08;
            var sh = opt.cone ? coneShape(G, bm, 0.34, s, opt.apex, 0.25) : capShape(G, bm, 0.34, s, opt.lift != 0 ? opt.lift : 1.1);
            S.shape("hat", spline(sh.cap, true), Mt.b, () =>
            {
                S.path("hat", spline(Pts(G.h3(0.85, 0.0, 1.5), G.h3(0.55, 1.3, 1.5), V(G.hx + 2.2 * R, G.ey - 2.8 * R), G.h3(2.2, -0.2, 1.5)), true), Mt.s);
                S.path("hat", spline(Pts(G.h3(-0.75, 0.5, s), G.h3(-0.45, 1.0, s), G.h3(-0.05, 1.22, s), G.h3(-0.2, 0.95, s), G.h3(-0.55, 0.62, s)), true), Mt.h, 0, " opacity=\"0.75\"");
                if (opt.ribs != null)
                {
                    var md = new StringBuilder();
                    foreach (var lam in opt.ribs)
                    {
                        var pts = new List<double[]>();
                        for (double b = bm + 0.34 * Math.Cos(lam) + 0.04; b < 1.5; b += 0.2) pts.Add(G.h3(lam, b, s * 1.005));
                        if (opt.apex != null) pts.Add(opt.apex);
                        md.Append(spline(pts));
                    }
                    double rw = opt.ribW != 0 ? opt.ribW : 3.2;
                    S.add("hat", "<path d=\"" + md + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(rw) + "\"/><path d=\"" + md + "\" fill=\"none\" stroke=\"" + Js.Or(opt.ribC, Mt.h) + "\" stroke-width=\"" + N(rw * 0.55) + "\"/>");
                }
                if (opt.extra != null) opt.extra(Mt);
            }, S.lw);
            if (!(opt.trim is bool && !(bool)opt.trim))
            {
                S.line("hat", spline(Slice(sh.bnd, 1, -1)), INK, 4.2);
                S.line("hat", spline(Slice(sh.bnd, 1, -1)), Js.Or(Js.Str(opt.trim), GOLD), 2.4);
            }
            bandShadow(S, G, sh.bnd, T, 0.16);
            return sh;
        }
        // 南蛮羽冠：编带 + 一排彩羽
        static HatInfo HatFeathers(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            var st = bandStrip(G, 0.1, 0.28, 0.36, 1.06);
            var colsL = sp.Hat.L("colors");
            var cols = new List<string>();
            if (colsL != null) foreach (var c in colsL) cols.Add(Js.Str(c)); else cols.AddRange(new[] { "#c03028", "#2a6aa0", "#ece4d4", "#d8a020", "#2a8a4a" });
            var roots = Slice(st.hi, 3, 12);
            for (int i = 0; i < roots.Count; i++)
            {
                var r = roots[i];
                double k = (double)i / (roots.Count - 1);
                var tip = add(r, (k - 0.55) * 0.9 * R, -(1.1 + Math.Sin(k * Math.PI) * 0.5) * R);
                feather(S, i % 2 != 0 ? "back" : "hat", Pts(r, lp(r, tip, 0.5), tip), 0.17 * R, cols[i % cols.Count]);
            }
            S.shape("hat", spline(st.pts, true), sp.Hat.Or("color", "#a03020"), () =>
            {
                if (S.o.lod >= 1)
                {
                    var zd = new StringBuilder();
                    var lo2 = Slice(st.lo, 1, -1); var hi2 = Slice(st.hi, 1, -1);
                    for (int i = 0; i < lo2.Count - 1; i++) zd.Append("M" + ps(lp(lo2[i], hi2[i], 0.2)) + "L" + ps(lp(lo2[i + 1], hi2[i + 1], 0.8)));
                    S.line("hat", zd.ToString(), GOLD, 1.4);
                }
            }, S.lw * 0.8);
            bandShadow(S, G, st.lo, T, 0.1);
            return new HatInfo(false);
        }
        // 孟获：嵌宝紫金冠 + 背后羽扇
        static HatInfo HatChief(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R, bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
            var cols = new[] { "#c03028", "#e8e0d0", "#2a6aa0", "#d8a020", "#2a8a4a" };
            for (int i = 0; i < 7; i++)
            {
                double a = Math.PI * (0.15 + i * 0.12);
                feather(S, "back", Pts(V(bx, by), V(bx + Math.Cos(a) * 0.8 * R, by - Math.Sin(a) * 0.9 * R), V(bx + Math.Cos(a) * 1.7 * R, by - Math.Sin(a) * 1.8 * R)), 0.2 * R, cols[i % cols.Length]);
            }
            return HatCrown(S, G, HatOnly(sp, JObj.Assign(new JObj { { "color", "#d0a040" }, { "gem", "#2a9aa0" }, { "pearls", true } }, sp.Hat)), T);
        }
        // 衝角付冑（倭）：盔前船首形突脊，后垂錣
        static HatInfo HatWahelm(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string metal = sp.Hat.Or("color", "#4a4c54");
            var Mt = metalT(metal);
            var ng = Pts(G.h3(1.2, 0.05, 1.14), G.h3(G.lamNear - 0.02, -0.06, 1.14), G.P(1.55, 0.7), G.P(1.45, 1.4, 0.4), G.P(0.8, 1.35, 0.4), G.P(0.7, 0.4));
            S.shape("back", spline(ng, true), Mt.s, () =>
            {
                var rd = new StringBuilder();
                for (int i = 1; i < 4; i++) rd.Append(spline(Pts(G.P(0.7, 0.1 + i * 0.36), G.P(1.15, 0.2 + i * 0.36), G.P(1.6, 0.18 + i * 0.36))));
                S.line("back", rd.ToString(), INK, 1.6);
            }, S.lw);
            bowl(S, G, sp, T, new BowlOpt
            {
                metal = metal, lift = 1.06, trim = "#c8a050", extra = M2 =>
                {
                    var bd = new StringBuilder();
                    foreach (var b in new[] { 0.75, 1.1 }) { var pts = band(G, b - 0.34 * 0.5, 0.2, 1.145); bd.Append(spline(pts)); }
                    S.line("hat", bd.ToString(), INK, 1.6);
                    if (S.o.lod >= 2) foreach (var b in new[] { 0.75, 1.1 }) foreach (var p in band(G, b - 0.17, 0.2, 1.15, 10)) S.add("hat", circle(p[0], p[1], 0.9) + " fill=\"" + M2.h + "\"/>");
                }
            });
            double[] b0 = G.h3(-0.2, 0.52, 1.16), up = G.h3(0.5, 1.45, 1.16);
            string keel = poly(Pts(b0, add(b0, -0.5 * R, -0.2 * R), add(lp(b0, up, 0.5), -0.35 * R, -0.35 * R), up));
            S.add("hat", "<path d=\"" + keel + "\" fill=\"" + Mt.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.85) + "\"/>");
            S.add("hat", "<path d=\"" + poly(Pts(b0, add(b0, -0.5 * R, -0.2 * R), add(lp(b0, up, 0.5), -0.35 * R, -0.35 * R)), false) + "\" fill=\"none\" stroke=\"" + Mt.h + "\" stroke-width=\"1.2\" opacity=\"0.8\"/>");
            return new HatInfo(true);
        }
        // 巫女头饰（卑弥呼）：白布带 + 额前铜镜 + 两侧勾玉垂饰
        static HatInfo HatShaman(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            var st = bandStrip(G, 0.12, 0.28, 0.36, 1.06);
            S.shape("hat", spline(st.pts, true), "#ece6d8", () =>
            {
                var td = new StringBuilder();
                var lo2 = Slice(st.lo, 1, -1); var hi2 = Slice(st.hi, 1, -1);
                for (int i = 0; i < lo2.Count - 1; i += 2) td.Append(poly(Pts(lo2[i], lp(hi2[i], hi2[i + 1], 0.5), lo2[i + 1])));
                S.path("hat", td.ToString(), "#b0302a");
            }, S.lw * 0.8);
            var c = G.h3(0, 0.62, 1.08);
            S.add("hat", circle(c[0], c[1], 0.3 * R) + " fill=\"#b08a3a\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>" + circle(c[0], c[1], 0.22 * R) + " fill=\"none\" stroke=\"#7a5a20\" stroke-width=\"1.4\"/>" + circle(c[0] - 0.08 * R, c[1] - 0.08 * R, 0.08 * R) + " fill=\"#f0e0a0\" opacity=\"0.8\"/>");
            foreach (var lam in new[] { -0.95, 1.05 })
            {
                var a = G.h3(lam, 0.32, 1.07);
                S.line("hat", "M" + ps(a) + "l0," + N(0.7 * R), "#e8e0c8", 1.2);
                for (int i = 0; i < 3; i++) S.add("hat", magatama(a[0], a[1] + (0.2 + i * 0.22) * R, 0.09 * R, i % 2 != 0 ? "#3a9a6a" : "#e8e0c8"));
            }
            bandShadow(S, G, st.lo, T, 0.1);
            return new HatInfo(false);
        }
        // 夷洲：贝珠编带 + 羽毛
        static HatInfo HatYiband(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            var st = bandStrip(G, 0.08, 0.26, 0.36, 1.06);
            var k = G.h3(1.4, 0.45, 1.08);
            feather(S, "back", Pts(k, add(k, 0.25 * R, -0.8 * R), add(k, 0.7 * R, -1.6 * R)), 0.2 * R, "#ece8e0", "#1a1a1a");
            feather(S, "back", Pts(k, add(k, 0.5 * R, -0.6 * R), add(k, 1.2 * R, -1.1 * R)), 0.18 * R, "#2a2a2a", "#ece8e0");
            S.shape("hat", spline(st.pts, true), sp.Hat.Or("color", "#b02a24"), () =>
            {
                var bd = new StringBuilder();
                var mid = new List<double[]>();
                for (int i = 0; i < st.lo.Count; i++) mid.Add(lp(st.lo[i], st.hi[i], 0.5));
                foreach (var p in Slice(mid, 1, -1)) bd.Append(circle(p[0], p[1], 0.06 * R) + "/>");
                S.add("hat", "<g fill=\"#f4f0e4\">" + bd + "</g>");
            }, S.lw * 0.8);
            bandShadow(S, G, st.lo, T, 0.1);
            return new HatInfo(false);
        }
        // 折风（弁）：小尖帽，颔下系带；birds = 鸟羽冠（两侧插羽）
        static HatInfo HatJeolpung(Svg S, Geo G, JObj sp, Theme T, bool birds)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", birds ? "#3a2a4a" : "#c8b07a");
            var C = clothT(col);
            var apex = V(G.hx + 0.05 * R, G.ey - 2.4 * R);
            var sh = coneShape(G, 0.42, 0.3, 1.06, apex, 0.18);
            if (birds)
            {
                string fc = sp.Hat.Or("color2", "#ece6d8");
                feather(S, "hat", Pts(G.h3(1.0, 0.85, 1.08), V(apex[0] + 0.5 * R, apex[1] + 0.2 * R), V(apex[0] + 0.8 * R, apex[1] - 0.7 * R)), 0.2 * R, fc);
                feather(S, "back", Pts(G.h3(-0.7, 0.85, 1.08), V(apex[0] - 0.55 * R, apex[1] + 0.2 * R), V(apex[0] - 0.7 * R, apex[1] - 0.6 * R)), 0.16 * R, fc);
            }
            S.shape("hat", spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
            S.line("hat", spline(Slice(sh.bnd, 1, -1)), INK, 3.6);
            S.line("hat", spline(Slice(sh.bnd, 1, -1)), C.d, 1.8);
            // 颔下系带
            var b = sh.bnd[sh.bnd.Count - 3];
            S.line("hat", spline(Pts(b, G.P(0.62, 0.3), G.P(0.3, 1.3), G.P(-0.1, 1.48))), INK, 2.2);
            S.line("hat", spline(Pts(b, G.P(0.62, 0.3), G.P(0.3, 1.3), G.P(-0.1, 1.48))), mix(col, "#000", 0.3), 1.0);
            bandShadow(S, G, sh.bnd, T, 0.08);
            return new HatInfo(false);
        }
        // 新罗金冠：金带 + 三根“出”字形立饰 + 勾玉与金片
        static HatInfo HatGoldcrown(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string gc = sp.Hat.Or("color", "#e0b440");
            var Mt = metalT(gc);
            var st = bandStrip(G, 0.14, 0.3, 0.36, 1.06);
            foreach (int pi in new[] { 3, 8, 13 })
            {
                var p = st.hi[pi];
                double h = 1.15 * R;
                var d = new StringBuilder("M" + ps(p) + "l0," + N(-h));
                for (int k = 1; k <= 3; k++) { double y = p[1] - h * k / 3.4; d.Append("M" + N(p[0]) + "," + N(y) + "l" + N(-0.22 * R) + ",0l0," + N(-0.18 * R) + "M" + N(p[0]) + "," + N(y) + "l" + N(0.22 * R) + ",0l0," + N(-0.18 * R)); }
                S.add("hat", "<path d=\"" + d + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.11 * R + 2.4) + "\" stroke-linecap=\"square\"/><path d=\"" + d + "\" fill=\"none\" stroke=\"" + Mt.b + "\" stroke-width=\"" + N(0.11 * R) + "\" stroke-linecap=\"square\"/>");
                if (S.o.lod >= 1) for (int k = 0; k < 3; k++) S.add("hat", magatama(p[0] + (k - 1) * 0.22 * R, p[1] - h * (0.25 + k * 0.25), 0.07 * R, "#3aa06a"));
            }
            S.shape("hat", spline(st.pts, true), Mt.b, () =>
            {
                S.path("hat", spline(Cat(Slice(st.lo, 8), Rev(Slice(st.hi, 8))), true), Mt.s);
                if (S.o.lod >= 1) { var dd = new StringBuilder(); foreach (var p in Slice(st.lo, 1, -1)) dd.Append(circle(p[0], p[1] - 0.06 * R, 0.035 * R) + "/>"); S.add("hat", "<g fill=\"" + Mt.h + "\">" + dd + "</g>"); }
            }, S.lw * 0.8);
            // 垂饰
            foreach (var lam in new[] { -1.0, 1.3 })
            {
                var a = G.h3(lam, 0.25, 1.06);
                S.line("hat", "M" + ps(a) + "l" + N(lam > 0 ? -0.05 * R : 0.02 * R) + "," + N(0.9 * R), Mt.b, 1.6);
                S.add("hat", "<path d=\"" + leaf(add(a, 0, 0.85 * R), add(a, 0, 1.2 * R), 0.16 * R, 0) + "\" fill=\"" + Mt.b + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
            }
            bandShadow(S, G, st.lo, T, 0.1);
            return new HatInfo(false);
        }
        // 毡帽：圆顶，翻起的毛皮帽檐，护耳盖住耳朵
        static HatInfo HatFelt(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#e0d4b8");
            var C = clothT(col);
            string furC = sp.Hat.Or("color2", "#6a4a2a");
            G.hideEar = true;
            var sh = capShape(G, 0.18, 0.32, 1.12, 1.16);
            // 护耳
            var flapN = Pts(G.h3(0.95, 0.15, 1.12), G.h3(1.75, 0.05, 1.12), G.P(1.05, 0.75), G.P(0.72, 1.0, 0.5), G.P(0.46, 0.75), G.P(0.44, 0.1));
            S.shape("hat", spline(flapN, true), clothT(furC).b, () => { S.path("hat", spline(Pts(G.P(0.9, -0.3), G.P(1.4, 0), G.P(1.2, 1.2), G.P(0.8, 1.2)), true), clothT(furC).s); }, S.lw);
            S.shape("back", spline(Pts(G.h3(G.lamFar + 0.05, 0.2, 1.12), G.P(-1.18, 0.3), G.P(-1.1, 0.95), G.P(-0.92, 0.9)), true), clothT(furC).s, null, S.lw);
            S.shape("hat", spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
            furEdge(S, "hat", G, Slice(band(G, 0.1, 0.32, 1.16, 12), 1, -1), furC, 0.14, 15);
            bandShadow(S, G, sh.bnd, T, 0.16);
            return new HatInfo(true);
        }
        // 高毛皮帽（草原 / 日耳曼毛帽 lowCap）
        static HatInfo HatFur(Svg S, Geo G, JObj sp, Theme T, bool lowCap)
        {
            double R = G.R;
            string furC = sp.Hat.Or("color", lowCap ? "#6a4a30" : "#4a3424");
            var C = clothT(furC);
            var sh = capShape(G, 0.08, 0.34, 1.16, lowCap ? 1.12 : 1.45, lowCap ? 0 : 0.45);
            if (!lowCap)
            {
                var t = G.h3(0.6, 1.4, 1.3);
                S.add("hat", ellipse(t[0], t[1] - 0.1 * R, 0.5 * R, 0.3 * R) + " fill=\"" + sp.Hat.Or("color2", "#a02a24") + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw) + "\"/>");
            }
            S.shape("hat", spline(sh.cap, true), C.b, () =>
            {
                capShade(S, G, C);
                if (S.o.lod >= 1)
                {
                    var fd = new StringBuilder();
                    var rr = Sub(sp.S("name"), "fur");
                    for (int i = 0; i < 26; i++)
                    {
                        double la = rr.range(-1.1, 1.9), be = rr.range(0.3, 1.4);
                        var p = G.h3(la, be, 1.18);
                        double dx = rr.range(-2, 2), dy = rr.range(2.5, 4.5);
                        fd.Append("M" + N(p[0]) + "," + N(p[1]) + "l" + N(dx) + "," + N(dy));
                    }
                    S.line("hat", fd.ToString(), C.d, 1.2, " opacity=\"0.7\"");
                }
            }, S.lw);
            furEdge(S, "hat", G, Cat(Slice(sh.cap, sh.bnd.Count - 1), sh.cap[0]), furC, 0.08, 16);
            furEdge(S, "hat", G, Slice(sh.bnd, 1, -1), furC, 0.1, 13);
            bandShadow(S, G, sh.bnd, T, 0.16);
            return new HatInfo(true);
        }
        // 南海诸国的多层尖顶金冠
        static HatInfo HatTallcrown(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            var Mt = metalT(sp.Hat.Or("color", "#e0b040"));
            var st = bandStrip(G, 0.14, 0.3, 0.36, 1.08);
            var top = G.h3(0.3, 1.2, 1.08);
            // 层层收小的金冠
            var tiers = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                double w = (0.95 - i * 0.2) * R, h = 0.36 * R, y = top[1] + 0.05 * R - i * h * 0.85;
                tiers.Add(spline(Pts(V(top[0] - w, y + h * 0.2, 0.2), V(top[0] - w * 0.82, y - h * 0.7), V(top[0], y - h * 0.9), V(top[0] + w * 0.82, y - h * 0.7), V(top[0] + w, y + h * 0.2, 0.2), V(top[0], y + h * 0.45)), true));
            }
            var spire = V(top[0], top[1] - 1.9 * R);
            S.add("hat", "<path d=\"" + poly(Pts(V(top[0] - 0.14 * R, top[1] - 1.1 * R), spire, V(top[0] + 0.14 * R, top[1] - 1.1 * R))) + "\" fill=\"" + Mt.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            for (int i = tiers.Count - 1; i >= 0; i--)
            {
                S.shape("hat", tiers[i], i % 2 != 0 ? Mt.s : Mt.b, () => { S.path("hat", spline(Pts(V(top[0] + 0.1 * R, top[1] - 3 * R), V(top[0] + 1.5 * R, top[1] - 2 * R), V(top[0] + 1.2 * R, top[1] + R), V(top[0] + 0.25 * R, top[1] + R)), true), Mt.s, 0, " opacity=\"0.6\""); }, S.lw * 0.8);
                if (S.o.lod >= 1) S.add("hat", circle(top[0] - 0.1 * R, top[1] - i * 0.3 * R - 0.05 * R, 0.07 * R) + " fill=\"" + (i % 2 != 0 ? "#c02a3a" : "#2a9a6a") + "\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>");
            }
            S.shape("hat", spline(st.pts, true), Mt.b, () => S.path("hat", spline(Cat(Slice(st.lo, 9), Rev(Slice(st.hi, 9))), true), Mt.s), S.lw * 0.8);
            // 耳侧火焰纹饰片
            var e = G.h3(1.45, 0.3, 1.08);
            S.add("hat", "<path d=\"" + spline(Pts(e, add(e, 0.35 * R, -0.25 * R), add(e, 0.25 * R, -0.75 * R), add(e, 0.5 * R, -0.45 * R), add(e, 0.45 * R, 0.25 * R), add(e, 0.1 * R, 0.35 * R)), true) + "\" fill=\"" + Mt.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            bandShadow(S, G, st.lo, T, 0.1);
            return new HatInfo(true);
        }
        // 花布裹头
        static HatInfo HatFlowerwrap(Svg S, Geo G, JObj sp, Theme T)
        {
            HatTurban(S, G, HatOnly(sp, new JObj { { "color", sp.Hat.Or("color", "#c83a4a") }, { "noTails", true } }), T);
            double R = G.R;
            var f = G.h3(1.1, 0.55, 1.16);
            S.add("hat", flower(f[0], f[1], 0.2 * R, "#f4ecd8") + flower(f[0] + 0.26 * R, f[1] + 0.22 * R, 0.16 * R, "#f0c040"));
            return new HatInfo(true);
        }
        // 尖顶毡帽（塞种 / 西域），帽檐上翻
        static HatInfo HatPointcap(Svg S, Geo G, JObj sp, Theme T, bool tall)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", "#e8dcc0");
            var C = clothT(col);
            var apex = tall ? V(G.hx + 0.15 * R, G.ey - 3.0 * R) : V(G.hx - 0.1 * R, G.ey - 2.55 * R);
            var sh = coneShape(G, 0.14, 0.34, 1.12, apex, 0.3);
            S.shape("hat", spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
            var st = bandStrip(G, 0.08, 0.24, 0.34, 1.15);
            S.shape("hat", spline(st.pts, true), sp.Hat.Or("color2", mix(col, "#7a2a20", 0.6)), () =>
            {
                if (S.o.lod >= 1) { var dd = new StringBuilder(); foreach (var p in Slice(st.lo, 1, -1)) dd.Append(circle(p[0], p[1] - 0.07 * G.R, 0.04 * G.R) + "/>"); S.add("hat", "<g fill=\"" + GOLD + "\">" + dd + "</g>"); }
            }, S.lw * 0.85);
            bandShadow(S, G, st.lo, T, 0.12);
            return new HatInfo(true);
        }
        // 贵霜高帽：高圆锥帽 + 王带飘带
        static HatInfo HatTallhat(Svg S, Geo G, JObj sp, Theme T)
        {
            HatPointcap(S, G, HatOnly(sp, JObj.Assign(new JObj { { "color2", GOLD } }, sp.Hat)), T, true);
            ribbons(S, G, G.h3(1.85, 0.25, 1.14), sp.Hat.Or("ribbon", "#e8e0d0"), 1.1);
            return new HatInfo(true);
        }
        // 王带（希腊化 / 安息）：束在发上的带子，脑后两条飘带
        static HatInfo HatDiadem(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", "#ece6d8");
            var st = bandStrip(G, 0.18, 0.3, 0.32, 1.1);
            ribbons(S, G, G.h3(1.8, 0.35, 1.1), col, 1.0);
            S.shape("hat", spline(st.pts, true), col, () => S.path("hat", spline(Cat(Slice(st.lo, 9), Rev(Slice(st.hi, 9))), true), mul(col, 0.75)), S.lw * 0.75);
            if (sp.Hat.T("gem")) { var g = G.h3(0, 0.65, 1.12); S.add("hat", circle(g[0], g[1], 0.09 * R) + " fill=\"" + Js.Str(sp.Hat.G("gem")) + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>"); }
            return new HatInfo(false);
        }
        // 安息提亚拉：高圆冠，护耳护颈，缀珠与星月
        static HatInfo HatTiara(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", "#3a2a4a");
            var C = clothT(col);
            G.hideEar = true;
            var flap = Pts(G.h3(0.95, 0.1, 1.14), G.h3(G.lamNear - 0.02, 0.0, 1.14), G.P(1.35, 1.0), G.P(1.2, 1.55, 0.4), G.P(0.62, 1.2, 0.4), G.P(0.44, 0.2));
            S.shape("hat", spline(flap, true), C.b, () => { S.path("hat", spline(Pts(G.P(0.9, -0.3), G.P(1.6, 0), G.P(1.5, 1.6), G.P(0.85, 1.6)), true), C.s); }, S.lw);
            S.shape("back", spline(Pts(G.h3(G.lamFar + 0.05, 0.15, 1.14), G.P(-1.22, 0.4), G.P(-1.18, 1.2), G.P(-0.95, 1.1)), true), C.s, null, S.lw);
            var sh = capShape(G, 0.08, 0.34, 1.15, 1.5, 0.35);
            S.shape("hat", spline(sh.cap, true), C.b, () =>
            {
                capShade(S, G, C);
                if (S.o.lod >= 1)
                {
                    var pd = new StringBuilder();
                    for (int i = 0; i < 7; i++) { var p = G.h3(0.05, 0.6 + i * 0.13, 1.17); pd.Append(circle(p[0], p[1], 0.05 * R) + "/>"); }
                    S.add("hat", "<g fill=\"#f4f0e4\" stroke=\"" + INK + "\" stroke-width=\"0.6\">" + pd + "</g>");
                    var s0 = G.h3(0.85, 0.9, 1.17);
                    S.add("hat", "<path d=\"M" + N(s0[0]) + "," + N(s0[1] - 0.18 * R) + "l" + N(0.05 * R) + "," + N(0.13 * R) + "l" + N(0.13 * R) + "," + N(0.05 * R) + "l" + N(-0.13 * R) + "," + N(0.05 * R) + "l" + N(-0.05 * R) + "," + N(0.13 * R) + "l" + N(-0.05 * R) + "," + N(-0.13 * R) + "l" + N(-0.13 * R) + "," + N(-0.05 * R) + "l" + N(0.13 * R) + "," + N(-0.05 * R) + "z\" fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>");
                }
            }, S.lw);
            var st = bandStrip(G, 0.06, 0.2, 0.34, 1.17);
            S.shape("hat", spline(st.pts, true), GOLD, () =>
            {
                var pd = new StringBuilder();
                var mid = new List<double[]>();
                for (int i = 0; i < st.lo.Count; i++) mid.Add(lp(st.lo[i], st.hi[i], 0.5));
                foreach (var p in Slice(mid, 1, -1)) pd.Append(circle(p[0], p[1], 0.05 * R) + "/>");
                if (S.o.lod >= 1) S.add("hat", "<g fill=\"#f4f0e4\">" + pd + "</g>");
            }, S.lw * 0.8);
            ribbons(S, G, G.h3(1.85, 0.2, 1.15), sp.Hat.Or("ribbon", "#e8e0d0"), 1.2);
            bandShadow(S, G, st.lo, T, 0.12);
            return new HatInfo(true);
        }
        // 弗里吉亚软帽：帽尖前倾
        static HatInfo HatPhrygian(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", "#9a2a24");
            var C = clothT(col);
            var sh = capShape(G, 0.1, 0.34, 1.13, 1.2);
            var top = G.h3(0.2, 1.4, 1.25);
            var tipPts = Pts(G.h3(-0.6, 1.0, 1.13), V(top[0] - 0.85 * R, top[1] - 0.35 * R), V(top[0] - 1.05 * R, top[1] + 0.05 * R, 0.3), V(top[0] - 0.55 * R, top[1] + 0.05 * R), G.h3(0.6, 1.25, 1.13));
            S.shape("hat", spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
            S.shape("hat", spline(tipPts, true), C.b, () => S.path("hat", spline(Pts(V(top[0] - 0.6 * R, top[1] - 0.6 * R), V(top[0] + 0.4 * R, top[1]), V(top[0] - 0.4 * R, top[1] + 0.3 * R)), true), C.s), S.lw);
            bandShadow(S, G, sh.bnd, T, 0.14);
            return new HatInfo(true);
        }
        // 阿拉伯头巾 + 头箍：两侧垂到肩，遮住耳朵
        static HatInfo HatKeffiyeh(Svg S, Geo G, JObj sp, Theme T)
        {
            string col = sp.Hat.Or("color", "#ece6da");
            var C = clothT(col);
            G.hideEar = true;
            var sh = capShape(G, 0.1, 0.34, 1.14, 1.08);
            var drapeN = Pts(sh.bnd[sh.bnd.Count - 7], G.h3(G.lamNear - 0.02, 0.0, 1.14), G.P(1.4, 0.8), G.P(1.75, 2.1), G.P(1.2, 2.45, 0.5), G.P(0.7, 1.7), G.P(0.52, 0.8), G.P(0.46, 0.0));
            S.shape("back", spline(Pts(G.h3(G.lamFar + 0.05, 0.3, 1.14), G.P(-1.25, 0.6), G.P(-1.6, 2.2), G.P(-1.0, 2.4), G.P(-0.95, 1.0)), true), C.s, null, S.lw);
            S.shape("hat", spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
            S.shape("hat", spline(drapeN, true), C.b, () =>
            {
                S.path("hat", spline(Pts(G.P(1.0, -0.5), G.P(1.9, 0.5), G.P(1.9, 2.6), G.P(1.1, 2.6), G.P(1.0, 1.0)), true), C.s);
                var fd = new StringBuilder();
                for (int i = 0; i < 3; i++) fd.Append(spline(Pts(G.P(0.62 + i * 0.22, 0.3 + i * 0.1), G.P(0.75 + i * 0.25, 1.2), G.P(0.95 + i * 0.25, 2.1))));
                S.line("hat", fd.ToString(), C.d, 1.3, " opacity=\"0.6\"");
                if (sp.Hat.T("check")) { var cd = new StringBuilder(); for (int i = 0; i < 8; i++) cd.Append(spline(Pts(G.P(0.4 + i * 0.16, 0), G.P(0.6 + i * 0.16, 2.4)))); S.line("hat", cd.ToString(), Js.Str(sp.Hat.G("check")), 1.3, " opacity=\"0.5\""); }
            }, S.lw);
            // 头箍（双圈黑绳）
            foreach (var off in new[] { 0.0, 0.14 })
            {
                string bd = spline(Slice(band(G, 0.32 + off, 0.3, 1.16, 14), 1, -1));
                S.line("hat", bd, INK, 4.6);
                S.line("hat", bd, sp.Hat.Or("color2", "#2a2226"), 2.8);
            }
            bandShadow(S, G, sh.bnd, T, 0.16);
            return new HatInfo(true);
        }
        // 缠头巾（阿拉伯 / 南海）
        static HatInfo HatArabturban(Svg S, Geo G, JObj sp, Theme T)
        {
            return HatTurban(S, G, HatOnly(sp, new JObj { { "color", sp.Hat.Or("color", "#ece6da") }, { "noTails", true }, { "big", true } }), T);
        }
        // 哈特拉王的高冠（鹰徽）
        static HatInfo HatHatra(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", "#c8a050");
            var Mt = metalT(col);
            var sh = capShape(G, 0.08, 0.34, 1.14, 1.7, 0.55);
            S.shape("hat", spline(sh.cap, true), Mt.b, () =>
            {
                capShade(S, G, Mt);
                if (S.o.lod >= 1) { var pd = new StringBuilder(); for (int r = 0; r < 3; r++) foreach (var p in Slice(band(G, 0.4 + r * 0.32, 0.2, 1.16, 10), 1, -1)) pd.Append(circle(p[0], p[1], 0.04 * R) + "/>"); S.add("hat", "<g fill=\"#f4f0e4\">" + pd + "</g>"); }
            }, S.lw);
            var c = G.h3(0.05, 0.95, 1.18);
            Func<double, string> wing = s2 => spline(Pts(c, add(c, s2 * 0.35 * R, -0.3 * R), add(c, s2 * 0.55 * R, -0.05 * R), add(c, s2 * 0.3 * R, 0.1 * R)), true);
            S.add("hat", "<path d=\"" + wing(-1) + wing(1) + "\" fill=\"" + mul(col, 0.6) + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>" + circle(c[0], c[1] - 0.1 * R, 0.08 * R) + " fill=\"" + mul(col, 0.6) + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
            ribbons(S, G, G.h3(1.85, 0.2, 1.14), "#e8e0d0", 1.0);
            bandShadow(S, G, sh.bnd, T, 0.14);
            return new HatInfo(true);
        }
        // 桂冠：两枝月桂叶环头，脑后系带
        static HatInfo HatLaurel(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string col = sp.Hat.Or("color", "#5a8a3a");
            var items = new List<Item>();
            var pts = Slice(band(G, 0.26, 0.3, 1.1, 14), 1, -1);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                var nx = pts[Math.Min(pts.Count - 1, i + 1)]; double dx = nx[0] - p[0], dy = nx[1] - p[1];
                double l = hyp(dx, dy); if (l == 0 || double.IsNaN(l)) l = 1;
                items.Add(It(leaf(p, add(p, dx / l * 0.3 * R - dy / l * 0.18 * R, dy / l * 0.3 * R - 0.2 * R), 0.13 * R, 0.1), col));
                items.Add(It(leaf(p, add(p, dx / l * 0.3 * R + 0.02 * R, dy / l * 0.3 * R + 0.14 * R), 0.13 * R, -0.1), mul(col, 0.75)));
            }
            clumps(S, "hat", items, S.lw * 0.45);
            ribbons(S, G, G.h3(1.8, 0.2, 1.1), sp.Hat.Or("ribbon", "#c02a2a"), 0.9);
            return new HatInfo(false);
        }
        // 罗马盔：护颊、护颈、马鬃羽冠
        static HatInfo HatGalea(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string metal = sp.Hat.Or("color", "#b08a50");
            var Mt = metalT(metal);
            G.hideEar = true;
            S.shape("back", spline(Pts(G.h3(1.3, 0.05, 1.14), G.h3(G.lamNear, -0.05, 1.14), G.P(1.75, 0.35), G.P(1.62, 0.55, 0.4), G.P(1.1, 0.5)), true), Mt.s, null, S.lw);
            bowl(S, G, sp, T, new BowlOpt
            {
                metal = metal, lift = 1.08, trim = mix(metal, "#fff", 0.2), extra = M2 =>
                {
                    S.line("hat", spline(Slice(band(G, 0.3, 0.3, 1.15, 12), 1, -1)), INK, 3);
                    S.line("hat", spline(Slice(band(G, 0.3, 0.3, 1.15, 12), 1, -1)), mix(metal, "#fff", 0.3), 1.4);
                }
            });
            // 护颊
            var ch = Pts(G.h3(0.9, 0.18, 1.12), G.h3(1.45, 0.1, 1.12), G.P(0.92, 0.5), G.P(0.74, 1.0, 0.5), G.P(0.36, 0.9), G.P(0.36, 0.25));
            S.shape("hat", spline(ch, true), Mt.b, () => { S.path("hat", spline(Pts(G.P(0.7, 0), G.P(1.2, 0), G.P(1.0, 1.2), G.P(0.6, 1.2)), true), Mt.s); }, S.lw * 0.9);
            if (S.o.lod >= 1) S.add("hat", circle(G.P(0.6, 0.5)[0], G.P(0.6, 0.5)[1], 0.05 * R) + " fill=\"" + Mt.h + "\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>");
            // 羽冠
            var cr = new List<Item>();
            string crest = sp.Hat.Or("plume", "#b02020");
            for (int i = 0; i < 9; i++)
            {
                double lam = -0.5 + i * 0.27;
                var b = G.h3(lam, 1.25, 1.16);
                cr.Add(It(leaf(b, add(b, 0.12 * R, -0.75 * R + Math.Abs(i - 4) * 0.06 * R), 0.32 * R, 0.1), i % 2 != 0 ? mul(crest, 0.75) : crest));
            }
            clumps(S, "hat", cr, S.lw * 0.55);
            return new HatInfo(true);
        }
        // 凯尔特铜盔：圆顶 + 顶钮 + 一对小角
        static HatInfo HatCelthelm(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string metal = sp.Hat.Or("color", "#b08a50");
            double[] h0 = G.h3(-0.6, 1.0, 1.15), h1 = G.h3(1.2, 1.0, 1.15);
            feather(S, "back", Pts(h1, add(h1, 0.5 * R, -0.3 * R), add(h1, 0.65 * R, -0.85 * R)), 0.2 * R, "#e8e0c8", "none");
            bowl(S, G, sp, T, new BowlOpt { metal = metal, lift = 1.12, trim = mix(metal, "#fff", 0.2) });
            feather(S, "hat", Pts(h0, add(h0, -0.4 * R, -0.35 * R), add(h0, -0.5 * R, -0.85 * R)), 0.2 * R, "#e8e0c8", "none");
            var t = G.h3(0.6, 1.5, 1.15);
            S.add("hat", circle(t[0], t[1], 0.12 * R) + " fill=\"" + metal + "\" stroke=\"" + INK + "\" stroke-width=\"1.4\"/>");
            return new HatInfo(true);
        }
        // 分片尖盔 + 护鼻
        static HatInfo HatSpangen(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string metal = sp.Hat.Or("color", "#7a808c");
            var apex = V(G.hx - 0.05 * R, G.ey - 2.25 * R);
            bowl(S, G, sp, T, new BowlOpt { metal = metal, cone = true, apex = apex, ribs = new[] { -0.6, 0.2, 1.0 }, ribC = "#c8a050", trim = "#c8a050" });
            // 护鼻
            double[] n0 = G.h3(0.0, 0.55, 1.16), n1 = G.F(0.0, 0.45, 0.25);
            S.add("hat", "<path d=\"" + taper(Pts(n0, n1), t => 0.14 * R * (1 - t * 0.3)) + "\" fill=\"" + metal + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            return new HatInfo(true);
        }
        // 萨尔马提亚尖盔 + 锁子护颈
        static HatInfo HatConical(Svg S, Geo G, JObj sp, Theme T)
        {
            double R = G.R;
            string metal = sp.Hat.Or("color", "#8a8f9a");
            var Mt = metalT(metal);
            G.hideEar = true;
            var mail = Pts(G.h3(0.9, 0.08, 1.14), G.h3(G.lamNear - 0.02, -0.05, 1.14), G.P(1.4, 0.9), G.P(1.3, 1.65, 0.4), G.P(0.62, 1.55, 0.4), G.P(0.44, 0.9), G.P(0.44, 0.1));
            S.shape("hat", spline(mail, true), Mt.s, () =>
            {
                if (S.o.lod >= 1)
                {
                    var md = new StringBuilder();
                    for (int i = 0; i < 9; i++) md.Append("M" + ps(G.P(0.4, -0.1 + i * 0.2)) + "L" + ps(G.P(1.6, 0.1 + i * 0.2)));
                    S.add("hat", "<path d=\"" + md + "\" fill=\"none\" stroke=\"" + Mt.d + "\" stroke-width=\"2.2\" stroke-dasharray=\"2 1.4\" opacity=\"0.8\"/>");
                }
                S.path("hat", spline(Pts(G.P(0.95, -0.3), G.P(1.6, 0.0), G.P(1.5, 1.8), G.P(1.0, 1.8)), true), Mt.d, 0, " opacity=\"0.45\"");
            }, S.lw);
            S.shape("back", spline(Pts(G.h3(G.lamFar + 0.05, 0.15, 1.14), G.P(-1.2, 0.4), G.P(-1.15, 1.1), G.P(-0.95, 1.05)), true), Mt.d, null, S.lw);
            var apex = V(G.hx + 0.1 * R, G.ey - 2.5 * R);
            bowl(S, G, sp, T, new BowlOpt { metal = metal, cone = true, apex = apex, ribs = new[] { -0.5, 0.35, 1.2 }, ribC = GOLD });
            tassel(S, "hat", G, apex, sp.Hat.Or("plume", "#c0302a"), 4, 0.6, 1);
            return new HatInfo(true);
        }
        // 勾玉（逗号形）
        static string magatama(double x, double y, double r, string col)
        {
            return "<path d=\"M" + N(x) + "," + N(y - r) + "a" + N(r) + "," + N(r) + " 0 1,1 0," + N(2 * r) + "q" + N(-r * 1.2) + "," + N(r * 0.2) + " " + N(-r * 1.4) + "," + N(r * 1.4) + "q" + N(-r * 0.6) + "," + N(-r * 1.6) + " " + N(r * 1.4) + "," + N(-r * 3.4) + "z\" fill=\"" + col + "\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>";
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    public static partial class Portrait
    {
        // ======================================================== 衣甲 ==
        internal delegate void OutfitFn(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o);
        static string torsoD(Geo G) { return spline(G.torso, true); }
        static void DrawBody(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            string type = sp.Outfit.Or("type", "robe");
            OutfitFn fn; if (!OUTFITS.TryGetValue(type, out fn)) fn = OUTFITS["robe"];
            fn(S, G, sp, T, o);
            if (sp.Outfit.T("cape")) mantle(S, G, sp, T, sp.Outfit.S("cape") == "faction" ? o.color : Js.Str(sp.Outfit.G("cape")));
        }
        static JObj OutfitWith(JObj sp, JObj outfit) { var o = sp.Clone(); o["outfit"] = outfit; return o; }
        // 交领（右衽）：外襟从近侧颈部斜向远侧下方
        static void crossCollar(Svg S, Geo G, JObj sp, Theme T, string col, string innerCol, double wide = 0)
        {
            double R = G.R;
            var C = clothT(col);
            double cw = 0.3 * (wide != 0 ? wide : 1);
            var outerL = Pts(G.P(0.92 * G.nw, 1.4), G.P(0.62, 1.95), G.P(-0.2, 2.75), G.P(-1.25, 4.4));
            var innerL = Pts(G.P(-0.5 * G.nw, 1.68), G.P(-0.3, 2.2), G.P(0.1, 2.62));
            S.add("body2", "<path d=\"" + taper(innerL, cw * R * 0.9) + "\" fill=\"" + C.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            S.add("body2", "<path d=\"" + taper(Slice(shift(outerL, -0.2 * R, -0.05 * R), 0, 3), t => cw * R * 0.55) + "\" fill=\"" + Js.Or(innerCol, "#ece4d4") + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            S.add("body2", "<path d=\"" + taper(outerL, t => cw * R * (1 + t * 0.4)) + "\" fill=\"" + C.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.9) + "\"/>");
            S.add("body2", "<path d=\"" + taper(shift(outerL, 0.1 * R, 0.02 * R), t => cw * R * 0.35) + "\" fill=\"" + C.s + "\" opacity=\"0.8\"/>");
            if (S.o.lod >= 2 && !sp.Outfit.IsFalse("trimDots"))
            {
                var dd = new StringBuilder();
                foreach (var p in Slice(resample(shift(outerL, -0.02 * R, 0), 9), 1)) dd.Append(circle(p[0], p[1], 1.1) + " fill=\"" + mix(C.b, "#fff", 0.45) + "\" opacity=\"0.7\"/>");
                S.add("body2", dd.ToString());
            }
        }
        static void torsoBase(Svg S, Geo G, JObj sp, Theme T, string fill, string shade, Action extra = null)
        {
            string d = torsoD(G);
            S.shape("body", d, fill, () =>
            {
                S.path("body", spline(Pts(G.P(0.75, 1.4), G.P(1.15, 2.4), G.P(1.25, 3.2), G.P(1.1, 4.6, 0), G.P(4, 4.6, 0), G.P(4, 1.4, 0)), true), shade);
                S.path("body", spline(Pts(G.P(-0.9, 1.6), G.P(-0.3, 2.35), G.P(0.6, 2.3), G.P(1.2, 1.8), G.P(1.2, 1.2, 0), G.P(-0.9, 1.2, 0)), true), shade, 0, " opacity=\"0.8\"");
                if (extra != null) extra();
            }, S.lw, "torso");
            rimOn(S, "body", G.torso, T.rim, 3.4, 0.9);
        }
        sealed class LamOpt { public double rowH, pw; public bool scale; public string alt; }
        // 甲片排（自下而上，上排压住下排），裁剪到躯干
        static void lamellae(Svg S, Geo G, Tone Mt, double y0, double y1, LamOpt opt = null)
        {
            opt = opt ?? new LamOpt();
            double R = G.R; int lo = S.o.lod;
            double rowH = (opt.rowH != 0 ? opt.rowH : 0.2) * R;
            double xl = G.hx - 2.6 * R, xr = G.hx + 3.0 * R;
            var o = new StringBuilder();
            int row = 0;
            for (double y = y1; y >= y0; y -= rowH, row++)
            {
                var d = new StringBuilder();
                double pw = (opt.pw != 0 ? opt.pw : 0.21) * R;
                double off = row % 2 != 0 ? pw / 2 : 0;
                for (double x = xl - off; x < xr; x += pw)
                {
                    double k = clamp(0.62 + 0.38 * (x - xl) / (xr - xl), 0.6, 1.1);
                    double w = pw * k * 0.96, h = rowH * 1.35;
                    if (opt.scale) d.Append("M" + N(x) + "," + N(y) + "h" + N(w) + "v" + N(h * 0.4) + "q0," + N(h * 0.6) + " " + N(-w / 2) + "," + N(h * 0.6) + "q" + N(-w / 2) + ",0 " + N(-w / 2) + "," + N(-h * 0.6) + "z");
                    else d.Append("M" + N(x) + "," + N(y) + "h" + N(w) + "v" + N(h - w * 0.4) + "q0," + N(w * 0.4) + " " + N(-w / 2) + "," + N(w * 0.4) + "q" + N(-w / 2) + ",0 " + N(-w / 2) + "," + N(-w * 0.4) + "z");
                }
                o.Append("<path d=\"" + d + "\" fill=\"" + (row % 2 != 0 && !string.IsNullOrEmpty(opt.alt) ? opt.alt : Mt.b) + "\" stroke=\"" + INK + "\" stroke-width=\"" + (lo == 0 ? "1.2" : "0.9") + "\"/>");
            }
            S.add("body", "<g clip-path=\"url(#" + S.id("torso") + ")\">" + o +
                "<path d=\"" + spline(Pts(G.P(0.75, 1.4), G.P(1.15, 2.4), G.P(1.25, 3.2), G.P(1.1, 4.6, 0), G.P(4, 4.6, 0), G.P(4, 1.4, 0)), true) + "\" fill=\"" + Mt.d + "\" opacity=\"0.5\"/>" +
                "<path d=\"" + spline(Pts(G.P(-2.6, 2.4), G.P(-1.8, 2.2), G.P(-1.0, 2.5), G.P(-1.6, 3.3), G.P(-2.4, 3.4)), true) + "\" fill=\"" + Mt.h + "\" opacity=\"0.2\"/>" + "</g>");
        }
        // 披风（盖住两肩，胸前打开）
        static void mantle(Svg S, Geo G, JObj sp, Theme T, string col)
        {
            double R = G.R, bw = G.bw;
            var C = clothT(col);
            var near = Pts(G.P(0.95 * G.nw, 1.42), G.P(1.7 * bw, 1.78), G.P(2.75 * bw, 2.3, 0.8), G.P(3.25 * bw, 3.0), G.P(3.5 * bw, 4.4, 0), G.P(1.5, 4.4, 0), G.P(1.25, 3.3), G.P(1.0, 2.4));
            var far = Pts(G.P(-0.52 * G.nw, 1.72), G.P(-0.9, 2.3), G.P(-1.35, 3.3), G.P(-1.55, 4.4, 0), G.P(-3.2 * bw, 4.4, 0), G.P(-3.0 * bw, 3.1), G.P(-2.55 * bw, 2.55, 0.8), G.P(-1.5 * bw, 2.08));
            foreach (var pr in new[] { new KeyValuePair<List<double[]>, int>(near, 1), new KeyValuePair<List<double[]>, int>(far, -1) })
            {
                var pts = pr.Key; int sd = pr.Value;
                S.shape("body2", spline(pts, true), sd > 0 ? C.s : C.b, () =>
                {
                    if (sd < 0) S.path("body2", spline(Pts(G.P(-1.4, 2.6), G.P(-1.2, 3.3), G.P(-1.45, 4.5), G.P(-0.6, 4.5, 0), G.P(-0.6, 2.3, 0)), true), C.s);
                    else S.path("body2", spline(Pts(G.P(1.6, 1.8), G.P(2.6, 2.3), G.P(2.2, 2.6), G.P(1.4, 2.3)), true), C.b, 0, " opacity=\"0.7\"");
                    var fd = new StringBuilder();
                    for (int i = 0; i < 3; i++) fd.Append(spline(Pts(G.P(sd * (1.6 + i * 0.5), 2.6 + i * 0.1), G.P(sd * (1.7 + i * 0.5), 3.4), G.P(sd * (1.65 + i * 0.55), 4.4))));
                    S.line("body2", fd.ToString(), C.d, 1.4, " opacity=\"0.6\"");
                }, S.lw);
            }
            rimOn(S, "body2", near, T.rim, 3.4, 0.9);
            // 领口扣结
            var k = G.P(0.15, 2.05);
            S.add("body2", circle(k[0], k[1], 0.16 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>" + circle(k[0] - 1, k[1] - 1, 0.06 * R) + " fill=\"#fff6d0\"/>");
        }
        static readonly Dictionary<string, OutfitFn> OUTFITS = new Dictionary<string, OutfitFn>
        {
            { "robe", OutRobe }, { "lamellar", OutLamellar }, { "plate", OutPlate }, { "robearmor", OutRobearmor },
            { "bare", (S, G, sp, T, o) => bareTorso(S, G, sp, T) }, { "pelt", OutPelt }, { "rattan", OutRattan }, { "chiefrobe", OutChiefrobe },
            { "kantoui", OutKantoui }, { "tanko", OutTanko }, { "yivest", OutYivest }, { "jacket", OutJacket }, { "kaftan", OutKaftan },
            { "fur", OutFur }, { "sash", OutSash }, { "scale", OutScale }, { "tunic", OutTunic }, { "robea", OutRobea },
            { "segmentata", OutSegmentata }, { "musculata", OutMusculata }, { "toga", OutToga }, { "plaid", OutPlaid },
        };
        // 袍服
        static void OutRobe(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var C = clothT(sp.Outfit.Or("color", "#3a4a6a"));
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                if (S.o.lod >= 1)
                {
                    S.line("body", spline(Pts(G.P(-1.9, 2.9), G.P(-1.7, 3.5), G.P(-1.75, 4.3))) + spline(Pts(G.P(1.9, 2.7), G.P(2.05, 3.4), G.P(2.0, 4.3))), C.d, 1.5, " opacity=\"0.7\"");
                    S.line("body", spline(Pts(G.P(-2.3, 2.75), G.P(-2.2, 3.1))), C.h, 1.4, " opacity=\"0.6\"");
                }
                if (S.o.lod >= 2 && !sp.Outfit.IsFalse("pattern"))
                {
                    // 暗纹团花
                    var pd = new StringBuilder();
                    foreach (var xy in new[] { new[] { -2.0, 3.6 }, new[] { 1.9, 3.3 }, new[] { -0.8, 3.9 }, new[] { 2.6, 4.1 } }) { var c = G.P(xy[0], xy[1]); pd.Append(circle(c[0], c[1], 0.32 * G.R) + "/>" + circle(c[0], c[1], 0.16 * G.R) + "/>"); }
                    S.add("body", "<g fill=\"none\" stroke=\"" + C.h + "\" stroke-width=\"1.2\" opacity=\"0.22\">" + pd + "</g>");
                }
            });
            crossCollar(S, G, sp, T, sp.Outfit.Or("color2", mix(C.b, "#000", 0.35)), sp.Outfit.S("inner"));
        }
        // 札甲
        static void OutLamellar(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var Mt = metalT(sp.Outfit.Or("metal", "#7a808c"));
            var C = clothT(sp.Outfit.Or("color", "#7a2a2a"));
            torsoBase(S, G, sp, T, Mt.s, Mt.d);
            lamellae(S, G, Mt, G.ey + 2.1 * G.R, 262);
            scarf(S, G, sp, T, C);
            pauldron(S, G, sp, T, Mt, -1);
            pauldron(S, G, sp, T, Mt, 1);
        }
        // 明光铠：胸前两面护心镜
        static void OutPlate(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var Mt = metalT(sp.Outfit.Or("metal", "#8a8f9a"));
            var C = clothT(sp.Outfit.Or("color", "#8a2a24"));
            double R = G.R; int lo = S.o.lod;
            string trim = sp.Outfit.Or("trim", GOLD);
            torsoBase(S, G, sp, T, Mt.s, Mt.d);
            lamellae(S, G, Mt, G.ey + 3.45 * R, 262, new LamOpt { rowH = 0.17, pw = 0.18 });
            // 胸甲上缘
            var top = Pts(G.P(-2.0, 2.45), G.P(-0.8, 2.3), G.P(0.5, 2.25), G.P(1.9, 2.35), G.P(2.1, 3.5, 0), G.P(-2.1, 3.65, 0));
            S.shape("body", spline(top, true), Mt.b, () =>
            {
                S.path("body", spline(Pts(G.P(0.9, 2.0), G.P(2.4, 2.2), G.P(2.4, 3.8), G.P(0.9, 3.8)), true), Mt.s);
                S.line("body", spline(Pts(G.P(-0.35, 2.3), G.P(-0.38, 3.0), G.P(-0.4, 3.6))), Mt.d, 1.6);
            }, S.lw * 0.9);
            // 护心镜
            foreach (var m in new[] { new[] { -1.35, 3.0, 0.42, 0.5 }, new[] { 0.55, 2.95, 0.62, 0.58 } })
            {
                double x = m[0], y = m[1], rx = m[2], ry = m[3];
                var c = G.P(x, y);
                S.add("body", ellipse(c[0], c[1], rx * R + 2.4, ry * R + 2.4) + " fill=\"" + trim + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.9) + "\"/>");
                S.add("body", ellipse(c[0], c[1], rx * R, ry * R) + " fill=\"" + Mt.b + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
                S.add("body", ellipse(c[0] + rx * R * 0.25, c[1] + ry * R * 0.2, rx * R * 0.7, ry * R * 0.7) + " fill=\"" + Mt.s + "\" opacity=\"0.6\"/>");
                S.add("body", ellipse(c[0] - rx * R * 0.35, c[1] - ry * R * 0.35, rx * R * 0.3, ry * R * 0.18, -30) + " fill=\"" + Mt.h + "\" opacity=\"0.9\"/>");
            }
            // 束甲绊（金带）
            S.add("body", "<path d=\"" + taper(Pts(G.P(-2.2, 3.65), G.P(0, 3.55), G.P(2.2, 3.5)), 0.14 * R) + "\" fill=\"" + trim + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            scarf(S, G, sp, T, C);
            pauldron(S, G, sp, T, Mt, -1, trim);
            pauldron(S, G, sp, T, Mt, 1, trim, lo >= 1);
        }
        // 袍内衬甲：外罩战袍，肩披甲
        static void OutRobearmor(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var C = clothT(sp.Outfit.Or("color", "#3a6a3a"));
            var Mt = metalT(sp.Outfit.Or("metal", "#7a808c"));
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                if (S.o.lod >= 1) S.line("body", spline(Pts(G.P(-1.9, 2.9), G.P(-1.7, 3.5), G.P(-1.75, 4.3))) + spline(Pts(G.P(1.9, 2.7), G.P(2.05, 3.4), G.P(2.0, 4.3))), C.d, 1.5, " opacity=\"0.7\"");
            });
            // 胸前露出甲片
            S.add("body", "<g clip-path=\"url(#" + S.id("torso") + ")\"><path d=\"" + spline(Pts(G.P(-0.5, 1.7), G.P(0.65, 1.9), G.P(-0.3, 2.9), G.P(-1.2, 3.8), G.P(-1.3, 2.4)), true) + "\" fill=\"" + Mt.b + "\"/></g>");
            crossCollar(S, G, sp, T, sp.Outfit.Or("color2", mix(C.b, "#000", 0.35)), Mt.h);
            pauldron(S, G, sp, T, Mt, -1, sp.Outfit.S("trim"));
            pauldron(S, G, sp, T, Mt, 1, sp.Outfit.S("trim"), S.o.lod >= 1);
        }
        // 领巾：绕颈一圈的卷边布巾，近侧打结垂两短尾
        static void scarf(Svg S, Geo G, JObj sp, Theme T, Tone C)
        {
            double R = G.R, nw = G.nw;
            var ring = Pts(G.P(-0.6 * nw, 1.6), G.P(-0.25, 1.92), G.P(0.3, 1.96), G.P(0.82, 1.72), G.P(1.02 * nw, 1.42));
            string d = taper(ring, t => (0.34 - 0.08 * Math.Abs(t - 0.4)) * R);
            S.shape("body2", d, C.b, () =>
            {
                S.path("body2", spline(Pts(G.P(0.45, 1.5), G.P(1.3, 1.2), G.P(1.3, 2.4), G.P(0.4, 2.3)), true), C.s);
                S.path("body2", taper(shift(Slice(ring, 0, 3), 0, -0.08 * R), t => 0.08 * R * Math.Sin(Math.PI * t) + 0.3), C.h, 0, " opacity=\"0.7\"");
                if (S.o.lod >= 1) S.line("body2", spline(Slice(shift(ring, 0, 0.06 * R), 0, 4)), C.d, 1.1, " opacity=\"0.6\"");
            }, S.lw * 0.9);
            var k = G.P(0.42, 2.02);
            clumps(S, "body2", new List<Item> {
                It(leaf(add(k, 0.02 * R, 0.08 * R), add(k, 0.3 * R, 0.72 * R), 0.26 * R, -0.12), C.s),
                It(leaf(add(k, -0.04 * R, 0.08 * R), add(k, -0.12 * R, 0.62 * R), 0.24 * R, 0.1), C.b),
                It(spline(Pts(add(k, -0.18 * R, -0.12 * R), add(k, 0.14 * R, -0.16 * R), add(k, 0.2 * R, 0.1 * R), add(k, -0.14 * R, 0.14 * R)), true), C.b),
            }, S.lw * 0.45);
        }
        static void pauldron(Svg S, Geo G, JObj sp, Theme T, Tone Mt, int side, string trim = null, bool beast = false)
        {
            double R = G.R; int lo = S.o.lod;
            double bw = G.bw;
            double sx = side > 0 ? 2.45 * bw : -2.3 * bw, sy = side > 0 ? 2.45 : 2.7;
            double w = side > 0 ? 1.15 : 0.85;
            for (int i = 2; i >= 0; i--)
            {
                double yy = sy + i * 0.32;
                var pts = Pts(G.P(sx - side * w * 0.95, yy - 0.18, 0.5), G.P(sx, yy - 0.55), G.P(sx + side * w * 0.95, yy - 0.05), G.P(sx + side * w * 0.9, yy + 0.32, 0.5), G.P(sx, yy + 0.12), G.P(sx - side * w * 0.9, yy + 0.18, 0.5));
                string d = spline(pts, true);
                S.shape("body2", d, i == 0 ? Mt.b : mix(Mt.b, Mt.s, i * 0.3), () =>
                {
                    S.path("body2", spline(shift(pts, side * -0.25 * R, -0.12 * R), true), Mt.h, 0, " opacity=\"0.35\"");
                    S.path("body2", spline(shift(pts, side * 0.45 * R, 0.1 * R), true), Mt.d, 0, " opacity=\"0.5\"");
                }, S.lw * 0.9);
                if (lo >= 1) S.line("body2", spline(Pts(G.P(sx - side * w * 0.8, yy + 0.08), G.P(sx, yy - 0.05), G.P(sx + side * w * 0.8, yy + 0.22))), Js.Or(trim, GOLD), 1.3, " opacity=\"0.9\"");
            }
            if (beast) beastBoss(S, G, G.P(sx - side * 0.1, sy - 0.14), 0.36 * R, Js.Or(trim, GOLD));
        }
        // 兽吞（肩甲上的兽面）：小尺寸时只画金色圆钉
        static void beastBoss(Svg S, Geo G, double[] c, double r, string col)
        {
            var Mt = metalT(col); int lo = S.o.lod;
            if (lo < 2)
            {
                S.add("body2", circle(c[0], c[1], r * 0.62) + " fill=\"" + Mt.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>" + circle(c[0] - r * 0.2, c[1] - r * 0.2, r * 0.2) + " fill=\"" + Mt.h + "\"/>");
                return;
            }
            // 鬃毛火焰纹
            var mane = new List<Item>();
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI * 2 * i / 10 + 0.3;
                mane.Add(It(leaf(c, V(c[0] + Math.Cos(a) * r * 1.25, c[1] + Math.Sin(a) * r * 1.15), r * 0.7, 0.18), i % 2 != 0 ? Mt.s : Mt.b));
            }
            clumps(S, "body2", mane, S.lw * 0.45);
            string face = spline(Pts(V(c[0] - r * 0.78, c[1] - r * 0.45), V(c[0], c[1] - r * 0.72), V(c[0] + r * 0.78, c[1] - r * 0.45), V(c[0] + r * 0.62, c[1] + r * 0.35), V(c[0], c[1] + r * 0.8), V(c[0] - r * 0.62, c[1] + r * 0.35)), true);
            S.shape("body2", face, Mt.b, () =>
            {
                S.path("body2", spline(Pts(V(c[0] + r * 0.1, c[1] - r), V(c[0] + r, c[1] - r * 0.4), V(c[0] + r * 0.6, c[1] + r), V(c[0] + r * 0.1, c[1] + r)), true), Mt.s);
            }, S.lw * 0.6);
            // 怒眉、眼、鼻、獠牙
            S.add("body2", "<path d=\"M" + N(c[0] - r * 0.62) + "," + N(c[1] - r * 0.38) + "L" + N(c[0] - r * 0.08) + "," + N(c[1] - r * 0.12) + "L" + N(c[0] - r * 0.1) + "," + N(c[1] - r * 0.28) + "ZM" + N(c[0] + r * 0.62) + "," + N(c[1] - r * 0.38) + "L" + N(c[0] + r * 0.08) + "," + N(c[1] - r * 0.12) + "L" + N(c[0] + r * 0.1) + "," + N(c[1] - r * 0.28) + "Z\" fill=\"" + INK + "\"/>");
            S.add("body2", ellipse(c[0] - r * 0.3, c[1] - r * 0.12, r * 0.13, r * 0.08) + " fill=\"#c02a20\"/>" + ellipse(c[0] + r * 0.3, c[1] - r * 0.12, r * 0.13, r * 0.08) + " fill=\"#c02a20\"/>");
            S.add("body2", "<path d=\"M" + N(c[0] - r * 0.16) + "," + N(c[1] + r * 0.02) + "h" + N(r * 0.32) + "l" + N(-r * 0.06) + "," + N(r * 0.2) + "h" + N(-r * 0.2) + "z\" fill=\"" + Mt.d + "\"/>");
            S.add("body2", "<path d=\"M" + N(c[0] - r * 0.42) + "," + N(c[1] + r * 0.32) + "Q" + N(c[0]) + "," + N(c[1] + r * 0.62) + " " + N(c[0] + r * 0.42) + "," + N(c[1] + r * 0.32) + "Z\" fill=\"#3a1010\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>");
            S.add("body2", "<path d=\"M" + N(c[0] - r * 0.3) + "," + N(c[1] + r * 0.36) + "l" + N(r * 0.08) + "," + N(r * 0.22) + "l" + N(r * 0.06) + "," + N(-r * 0.18) + "ZM" + N(c[0] + r * 0.3) + "," + N(c[1] + r * 0.36) + "l" + N(-r * 0.08) + "," + N(r * 0.22) + "l" + N(-r * 0.06) + "," + N(-r * 0.18) + "Z\" fill=\"#f4eee0\"/>");
        }

        // ======================================================== 兵器（肩后） ==
        static void DrawWeapon(Svg S, Geo G, JObj sp, Theme T)
        {
            string w = Js.Str(sp.G("weapon"));
            if (string.IsNullOrEmpty(w) || S.o.lod == 0) return;
            double R = G.R;
            Action<double, double, double, double, string> pole = (x0, y0, x1, y1, col) =>
            {
                S.add("back", "<path d=\"M" + N(x0) + "," + N(y0) + "L" + N(x1) + "," + N(y1) + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.2 * R + 2) + "\" fill=\"none\"/>");
                S.add("back", "<path d=\"M" + N(x0) + "," + N(y0) + "L" + N(x1) + "," + N(y1) + "\" stroke=\"" + Js.Or(col, "#5a3a24") + "\" stroke-width=\"" + N(0.2 * R) + "\" fill=\"none\"/>");
            };
            Action<string> blade = d => S.add("back", "<path d=\"" + d + "\" fill=\"#c8d0d8\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.9) + "\"/>");
            double[] top = G.P(-2.25, -2.3), bot = G.P(-1.7, 4.4);
            if (w == "guandao")
            {
                pole(bot[0], bot[1], top[0] + 2, top[1] + 0.4 * R, "#3a5a3a");
                var b = top;
                blade(spline(Pts(add(b, 2, 0.6 * R), add(b, -0.5 * R, 0.3 * R), add(b, -0.7 * R, -0.5 * R), add(b, -0.2 * R, -1.6 * R), add(b, 0.25 * R, -0.6 * R), add(b, 0.3 * R, 0.4 * R)), true));
                S.line("back", spline(Pts(add(b, 0, 0.4 * R), add(b, -0.35 * R, -0.3 * R), add(b, -0.15 * R, -1.2 * R))), "#fff", 1.4, " opacity=\"0.7\"");
                S.add("back", ellipse(b[0] + 0.05 * R, b[1] + 0.55 * R, 0.2 * R, 0.16 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.4\"/>");
                tassel(S, "back", G, add(b, 0.1 * R, 0.7 * R), "#c0302a", 4, 0.6, 1);
            }
            else if (w == "snake")
            {
                pole(bot[0], bot[1], top[0], top[1], "#2a2a2a");
                var b = top;
                blade(spline(Pts(add(b, -0.12 * R, 0.1 * R), add(b, -0.22 * R, -0.3 * R), add(b, 0.05 * R, -0.6 * R), add(b, -0.18 * R, -0.95 * R), add(b, 0.0 * R, -1.4 * R), add(b, 0.1 * R, -0.95 * R), add(b, 0.22 * R, -0.6 * R), add(b, 0.05 * R, -0.3 * R), add(b, 0.12 * R, 0.1 * R)), true));
                tassel(S, "back", G, add(b, 0, 0.2 * R), "#c0302a", 4, 0.5, 1);
            }
            else if (w == "halberd")
            {
                pole(bot[0], bot[1], top[0], top[1], "#6a2a20");
                var b = top;
                blade(spline(Pts(add(b, 0, -1.4 * R), add(b, 0.12 * R, -0.6 * R), add(b, 0.1 * R, 0.1 * R), add(b, -0.1 * R, 0.1 * R), add(b, -0.12 * R, -0.6 * R)), true));
                blade(spline(Pts(add(b, -0.08 * R, -0.35 * R), add(b, -0.65 * R, -0.75 * R), add(b, -0.85 * R, -0.25 * R), add(b, -0.55 * R, 0.15 * R), add(b, -0.6 * R, -0.25 * R), add(b, -0.08 * R, 0.0)), true));
                blade(spline(Pts(add(b, 0.08 * R, -0.35 * R), add(b, 0.55 * R, -0.75 * R), add(b, 0.75 * R, -0.25 * R), add(b, 0.45 * R, 0.15 * R), add(b, 0.5 * R, -0.25 * R), add(b, 0.08 * R, 0.0)), true));
                tassel(S, "back", G, add(b, 0, 0.25 * R), "#c0302a", 4, 0.55, 1);
            }
            else if (w == "spear")
            {
                pole(bot[0], bot[1], top[0], top[1], "#5a3a24");
                blade(spline(Pts(add(top, 0, -1.2 * R), add(top, 0.16 * R, -0.4 * R), add(top, 0.06 * R, 0.1 * R), add(top, -0.06 * R, 0.1 * R), add(top, -0.16 * R, -0.4 * R)), true));
                tassel(S, "back", G, add(top, 0, 0.18 * R), sp.Or("weaponColor", "#c0302a"), 4, 0.55, 1);
            }
            else if (w == "ji")
            {
                foreach (var dx in new[] { 0.0, 0.6 })
                {
                    double[] t2 = add(top, dx * R, 0.3 * R), b2 = add(bot, dx * R, 0);
                    pole(b2[0], b2[1], t2[0], t2[1], "#3a2a20");
                    blade(spline(Pts(add(t2, 0, -0.9 * R), add(t2, 0.12 * R, -0.3 * R), add(t2, -0.12 * R, -0.3 * R)), true));
                    blade(spline(Pts(add(t2, -0.06 * R, -0.25 * R), add(t2, -0.55 * R, -0.5 * R), add(t2, -0.6 * R, 0.05 * R), add(t2, -0.06 * R, 0.05 * R)), true));
                }
            }
            else if (w == "bow")
            {
                var c = G.P(-2.4, 0.6);
                S.add("back", "<path d=\"M" + N(c[0] + 0.6 * R) + "," + N(c[1] - 2.6 * R) + "q" + N(-1.6 * R) + "," + N(2.6 * R) + " 0," + N(5.2 * R) + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.22 * R + 2) + "\"/>");
                S.add("back", "<path d=\"M" + N(c[0] + 0.6 * R) + "," + N(c[1] - 2.6 * R) + "q" + N(-1.6 * R) + "," + N(2.6 * R) + " 0," + N(5.2 * R) + "\" fill=\"none\" stroke=\"#7a4a24\" stroke-width=\"" + N(0.22 * R) + "\"/>");
                S.add("back", "<path d=\"M" + N(c[0] + 0.6 * R) + "," + N(c[1] - 2.6 * R) + "v" + N(5.2 * R) + "\" stroke=\"#e8e0d0\" stroke-width=\"1\"/>");
            }
            else if (w == "sword" || w == "swords")
            {
                // 背后剑柄从远侧肩头探出
                var hilts = w == "swords" ? new[] { new[] { -2.05, 2.8, -0.4 }, new[] { -1.7, 2.8, -0.2 } } : new[] { new[] { -1.9, 2.8, -0.32 } };
                foreach (var hl in hilts)
                {
                    double[] h0 = G.P(hl[0], hl[1]), h1 = add(h0, hl[2] * R, -1.5 * R);
                    S.add("back", "<path d=\"M" + ps(h0) + "L" + ps(h1) + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.2 * R + 2.4) + "\"/><path d=\"M" + ps(h0) + "L" + ps(h1) + "\" stroke=\"#3a2418\" stroke-width=\"" + N(0.2 * R) + "\"/>");
                    if (S.o.lod >= 2) S.add("back", "<path d=\"M" + ps(h0) + "L" + ps(h1) + "\" stroke=\"#7a5a3a\" stroke-width=\"" + N(0.2 * R) + "\" stroke-dasharray=\"2 3\" stroke-linecap=\"butt\"/>");
                    var g = lp(h0, h1, 0.3);
                    S.add("back", "<path d=\"M" + ps(add(g, -0.32 * R, -0.05 * R)) + "L" + ps(add(g, 0.32 * R, 0.05 * R)) + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.14 * R + 2.4) + "\"/><path d=\"M" + ps(add(g, -0.3 * R, -0.05 * R)) + "L" + ps(add(g, 0.3 * R, 0.05 * R)) + "\" stroke=\"" + GOLD + "\" stroke-width=\"" + N(0.14 * R) + "\"/>");
                    S.add("back", circle(h1[0], h1[1], 0.12 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.3\"/>");
                    tassel(S, "back", G, h1, "#c0302a", 3, 0.45, -1);
                }
            }
            else if (w == "axe")
            {
                pole(bot[0], bot[1], top[0], top[1] + 0.2 * R, "#5a3a24");
                blade(spline(Pts(add(top, 0, 0.0), add(top, -0.8 * R, -0.5 * R), add(top, -0.95 * R, 0.4 * R), add(top, -0.8 * R, 1.0 * R), add(top, 0, 0.6 * R)), true));
            }
            // fan：羽扇放在胸前，见 DrawFan
        }
        // 羽扇（诸葛亮）：胸前斜持的白鹤羽扇——宽羽层叠成圆润扇面，羽轴细线，扇柄金箍
        static void DrawFan(Svg S, Geo G, JObj sp, Theme T)
        {
            if (!sp.Includes("acc", "fan")) return;
            double R = G.R; int lo = S.o.lod;
            var bs = G.P(-1.35, 3.55);
            Func<double[], double[], double, string> fth = (a, b, w) =>
            {
                double dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy), nx = -dy / l, ny = dx / l;
                Func<double, double, double[]> at = (t, k) => V(a[0] + dx * t + nx * w * k, a[1] + dy * t + ny * w * k);
                return spline(Pts(a, at(0.25, 0.22), at(0.6, 0.48), at(0.86, 0.36), V(b[0], b[1], 0), at(0.86, -0.3), at(0.6, -0.42), at(0.25, -0.2)), true);
            };
            var items = new List<Item>(); var spines = new StringBuilder(); var tipsD = new StringBuilder();
            int n = 9;
            for (int i = 0; i < n; i++)
            {
                double u = (double)i / (n - 1);
                double ang = -0.78 + u * 1.2;                       // 扇面略向左倾
                double len = 2.05 * R * (0.84 + 0.16 * Math.Sin(Math.PI * u));
                var tip = add(bs, Math.Sin(ang) * len, -Math.Cos(ang) * len);
                string fd = fth(bs, tip, 0.62 * R);
                items.Add(It(fd, i % 2 != 0 ? "#e8e5dc" : "#f8f6f0"));
                tipsD.Append(fd);
                spines.Append(spline(Pts(add(bs, Math.Sin(ang) * 0.3 * R, -Math.Cos(ang) * 0.3 * R), add(bs, Math.Sin(ang) * len * 0.9, -Math.Cos(ang) * len * 0.9))));
            }
            clumps(S, "front", items, S.lw * 0.7);
            // 背光一侧的阴影、灰色羽尖（鹤羽）
            string fanD = tipsD.ToString();
            S.add("front", "<g clip-path=\"" + S.clip("fan", fanD) + "\">" +
                "<path d=\"" + spline(Pts(add(bs, 0.15 * R, 0), add(bs, 0.75 * R, -1.3 * R), add(bs, 1.1 * R, -2.6 * R), add(bs, 2.4 * R, -1.0 * R)), true) + "\" fill=\"#c9c4b8\" opacity=\"0.9\"/>" +
                "<path d=\"" + spline(Cat(arcPts(bs[0], bs[1], 2.4 * R, 2.4 * R, 2.5, 0.9, 10), arcPts(bs[0], bs[1], 1.66 * R, 1.66 * R, 0.9, 2.5, 10)), true) + "\" fill=\"#6a6660\" opacity=\"0.55\"/>" +
                "</g>");
            if (lo >= 1) S.line("front", spines.ToString(), "#9a948a", 0.9, " opacity=\"0.8\"");
            // 扇柄与金箍
            double[] h0 = add(bs, -0.02 * R, -0.1 * R), h1 = add(bs, 0.18 * R, 1.0 * R);
            S.add("front", "<path d=\"M" + ps(h0) + "L" + ps(h1) + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.2 * R + 2.2) + "\"/><path d=\"M" + ps(h0) + "L" + ps(h1) + "\" stroke=\"#5a3a22\" stroke-width=\"" + N(0.2 * R) + "\"/>");
            S.add("front", ellipse(bs[0], bs[1] - 0.05 * R, 0.2 * R, 0.13 * R, -10) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
            // 握扇的手（简化：袖口 + 手指）
            var hand = add(bs, 0.12 * R, 0.55 * R);
            var K = T.skin;
            S.add("front", "<path d=\"" + spline(Pts(add(hand, -0.28 * R, -0.12 * R), add(hand, 0.05 * R, -0.24 * R), add(hand, 0.32 * R, -0.08 * R), add(hand, 0.3 * R, 0.2 * R), add(hand, -0.05 * R, 0.3 * R), add(hand, -0.3 * R, 0.14 * R)), true) + "\" fill=\"" + K.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            if (lo >= 1) S.line("front", spline(Pts(add(hand, -0.12 * R, -0.05 * R), add(hand, 0.18 * R, 0.02 * R))) + spline(Pts(add(hand, -0.14 * R, 0.08 * R), add(hand, 0.16 * R, 0.14 * R))), K.line, 1.0, " opacity=\"0.7\"");
        }

        // ======================================================== 各文化衣甲 ==
        // 裸露的躯干（肌肉线条）
        static void bareTorso(Svg S, Geo G, JObj sp, Theme T)
        {
            var K = T.skin;
            // 女性不赤膊：内穿短衣
            if (sp.S("sex") == "f") { OutTunic(S, G, OutfitWith(sp, JObj.Assign(sp.Outfit, new JObj { { "color", sp.Outfit.Or("color2", "#7a3a24") } })), T, null); return; }
            torsoBase(S, G, sp, T, K.b, K.s, () =>
            {
                // 斜方肌的背光面、锁骨下与三角肌的阴影（赛璐璐两阶）
                S.path("body", spline(Pts(G.P(0.55, 1.5), G.P(1.3, 1.85), G.P(2.3, 2.15), G.P(2.9, 2.7), G.P(3.4, 2.4, 0), G.P(3.4, 1.4, 0)), true), K.s);
                S.path("body", spline(Pts(G.P(-0.35, 2.2), G.P(-1.0, 2.42), G.P(-1.7, 2.5), G.P(-1.1, 2.75), G.P(-0.4, 2.6)), true), K.s, 0, " opacity=\"0.7\"");
                S.path("body", spline(Pts(G.P(0.6, 2.12), G.P(1.4, 2.25), G.P(2.0, 2.35), G.P(1.6, 2.65), G.P(0.7, 2.5)), true), K.s, 0, " opacity=\"0.8\"");
                S.path("body", spline(Pts(G.P(-2.95, 2.75), G.P(-2.4, 2.5), G.P(-2.1, 2.9), G.P(-2.5, 3.6), G.P(-3.1, 3.6, 0)), true), K.s, 0, " opacity=\"0.55\"");
                if (S.o.lod >= 1)
                {
                    // 锁骨、胸肌、三角肌
                    S.line("body", spline(Pts(G.P(-0.4, 2.05), G.P(-1.0, 2.25), G.P(-1.6, 2.35))) + spline(Pts(G.P(0.7, 1.95), G.P(1.4, 2.05), G.P(2.1, 2.15))), K.line, 1.6, " opacity=\"0.8\"");
                    S.line("body", spline(Pts(G.P(-2.1, 3.0), G.P(-1.5, 3.4), G.P(-0.7, 3.3), G.P(-0.45, 3.0))) + spline(Pts(G.P(-0.3, 2.95), G.P(0.4, 3.3), G.P(1.4, 3.2), G.P(1.9, 2.75))), K.line, 1.5, " opacity=\"0.7\"");
                    S.line("body", spline(Pts(G.P(-0.4, 2.3), G.P(-0.38, 3.0), G.P(-0.4, 4.2))), K.line, 1.1, " opacity=\"0.45\"");
                    S.line("body", spline(Pts(G.P(-2.2, 2.6), G.P(-2.45, 3.3))) + spline(Pts(G.P(2.2, 2.4), G.P(2.5, 3.1))), K.line, 1.2, " opacity=\"0.5\"");
                }
                S.path("body", spline(Pts(G.P(-2.6, 2.55), G.P(-2.0, 2.4), G.P(-1.6, 2.7), G.P(-2.2, 3.0)), true), K.h, 0, " opacity=\"0.6\"");
            });
        }
        static string patternSpots(Svg S, Geo G, List<double[]> pts, int n, uint seed, bool stripes)
        {
            double R = G.R; var rr = new Rng(seed);
            var d = new StringBuilder();
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (var p in pts) { x0 = Math.Min(x0, p[0]); x1 = Math.Max(x1, p[0]); y0 = Math.Min(y0, p[1]); y1 = Math.Max(y1, p[1]); }
            for (int i = 0; i < n; i++)
            {
                double x = rr.range(x0, x1), y = rr.range(y0, y1);
                if (stripes) d.Append("M" + N(x) + "," + N(y) + "q" + N(0.15 * R) + "," + N(0.25 * R) + " " + N(0.05 * R) + "," + N(0.55 * R));
                else d.Append("M" + N(x) + "," + N(y) + "m-3,0a3,2.5 0 1,0 6,0a3,2.5 0 1,0 -6,0");
            }
            return d.ToString();
        }
        // 兽皮（豹 / 虎）斜披远侧肩
        static void OutPelt(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            if (sp.S("sex") == "f") OutTunic(S, G, OutfitWith(sp, JObj.Assign(sp.Outfit, new JObj { { "color", sp.Outfit.Or("color2", "#7a3a24") } })), T, o);
            else bareTorso(S, G, sp, T);
            bool tiger = sp.Outfit.S("pelt") == "tiger" || (!sp.Outfit.T("pelt") && HashStr(sp.S("name")) % 2 != 0);
            string pc = tiger ? "#d08a30" : "#d8a848";
            var C = clothT(pc);
            var pts = Pts(G.P(-0.55 * G.nw, 1.72), G.P(-1.5 * G.bw, 2.12), G.P(-2.5 * G.bw, 2.55), G.P(-3.0 * G.bw, 3.2), G.P(-3.2 * G.bw, 4.5, 0), G.P(1.8, 4.5, 0), G.P(0.6, 3.4), G.P(-0.1, 2.4));
            string d = spline(pts, true);
            S.shape("body2", d, C.b, () =>
            {
                S.path("body2", spline(Pts(G.P(-0.4, 2.5), G.P(0.9, 3.6), G.P(2.0, 4.6), G.P(-0.5, 4.6)), true), C.s);
                S.add("body2", "<path d=\"" + patternSpots(S, G, pts, tiger ? 14 : 26, HashStr(sp.S("name") + "pelt"), tiger) + "\" fill=\"" + (tiger ? "none" : mul(pc, 0.35)) + "\" stroke=\"" + INK + "\" stroke-width=\"" + (tiger ? "2.6" : "1.1") + "\" opacity=\"0.85\"/>");
            }, S.lw);
            furEdge(S, "body2", G, Pts(G.P(-0.1, 2.4), G.P(0.6, 3.4), G.P(1.8, 4.5)), pc, 0.1, 9);
            // 兽爪垂在胸前
            var k = G.P(-0.2, 2.5);
            S.add("body2", "<path d=\"" + leaf(k, add(k, 0.4 * R, 0.9 * R), 0.4 * R, 0.1) + "\" fill=\"" + C.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            var cl = new StringBuilder();
            for (int i = 0; i < 3; i++) cl.Append("M" + ps(add(k, (0.3 + i * 0.08) * R, (0.85 - i * 0.05) * R)) + "l" + N(0.04 * R) + "," + N(0.16 * R));
            S.line("body2", cl.ToString(), "#f4ecd8", 2);
        }
        // 藤甲：油浸藤条编成，深褐带光泽
        static void OutRattan(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var C = clothT(sp.Outfit.Or("rattan", "#9a7a3a"));
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                var wd = new StringBuilder();
                for (int i = -10; i < 16; i++) { wd.Append("M" + ps(G.P(i * 0.34 - 2, 1.6)) + "l" + N(2.2 * R) + "," + N(2.8 * R)); wd.Append("M" + ps(G.P(i * 0.34 + 2, 1.6)) + "l" + N(-2.2 * R) + "," + N(2.8 * R)); }
                S.line("body", wd.ToString(), C.d, 1.4, " opacity=\"0.55\"");
                var hb = new StringBuilder();
                foreach (var y in new[] { 2.4, 3.05, 3.7 }) hb.Append(spline(Pts(G.P(-3.2, y + 0.1), G.P(-0.5, y - 0.05), G.P(3.4, y + 0.05))));
                S.add("body", "<path d=\"" + hb + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.16 * R + 2) + "\"/><path d=\"" + hb + "\" fill=\"none\" stroke=\"" + C.s + "\" stroke-width=\"" + N(0.16 * R) + "\"/>");
                S.path("body", spline(Pts(G.P(-2.5, 2.5), G.P(-1.6, 2.3), G.P(-1.2, 2.8), G.P(-2.2, 3.2)), true), C.h, 0, " opacity=\"0.45\"");
            });
            pauldron(S, G, sp, T, metalT(C.b), -1, mul(C.b, 0.6));
            pauldron(S, G, sp, T, metalT(C.b), 1, mul(C.b, 0.6));
        }
        // 孟获：缨络红锦袍
        static void OutChiefrobe(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            OutRobe(S, G, OutfitWith(sp, JObj.Assign(new JObj { { "color", "#a8282a" }, { "color2", "#d8a040" } }, sp.Outfit)), T, o);
            // 缨络（成串珠饰）
            for (int k = 0; k < 2; k++)
            {
                var sw = resample(Pts(G.P(-1.5 + k * 0.2, 2.25), G.P(-0.4, 2.9 + k * 0.45), G.P(0.8, 2.75 + k * 0.4), G.P(1.7 - k * 0.1, 2.1)), 13);
                var bd = new StringBuilder();
                for (int i = 0; i < sw.Count; i++) { var p = sw[i]; bd.Append(circle(p[0], p[1], (i % 3 == 1 ? 0.09 : 0.06) * R) + " fill=\"" + (i % 3 == 1 ? "#2aa0a0" : i % 3 == 2 ? "#e8c050" : "#c83040") + "\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>"); }
                S.add("body2", bd.ToString());
            }
        }
        // 贯头衣（倭）：麻布，中间开领口
        static void OutKantoui(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var C = clothT(sp.Outfit.Or("color", "#e6dcc4"));
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                if (S.o.lod >= 1)
                {
                    S.line("body", spline(Pts(G.P(-1.9, 2.9), G.P(-1.7, 3.5), G.P(-1.75, 4.3))) + spline(Pts(G.P(1.9, 2.7), G.P(2.05, 3.4), G.P(2.0, 4.3))), C.d, 1.4, " opacity=\"0.6\"");
                    // 染色纹样带
                    string st = sp.Outfit.Or("color2", "#9a3a2a");
                    S.add("body", "<path d=\"" + spline(Pts(G.P(-3, 3.5), G.P(0, 3.35), G.P(3.4, 3.45))) + "\" fill=\"none\" stroke=\"" + st + "\" stroke-width=\"" + N(0.18 * R) + "\" opacity=\"0.85\"/>");
                    var td = new StringBuilder();
                    for (int i = 0; i < 12; i++) td.Append("M" + ps(G.P(-2.6 + i * 0.5, 3.48)) + "l" + N(0.12 * R) + "," + N(-0.1 * R) + "l" + N(0.12 * R) + "," + N(0.1 * R));
                    S.line("body", td.ToString(), "#f4ecd8", 1.1, " opacity=\"0.8\"");
                }
            });
            // 领口
            S.add("body2", "<path d=\"" + spline(Pts(G.P(-0.5 * G.nw, 1.75), G.P(0.1, 2.15), G.P(0.95 * G.nw, 1.5))) + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw) + "\"/>");
        }
        // 短甲（倭，横矧板铆留）
        static void OutTanko(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var Mt = metalT(sp.Outfit.Or("metal", "#4a4c54"));
            torsoBase(S, G, sp, T, Mt.s, Mt.d);
            var bd = new StringBuilder(); var rv = new StringBuilder();
            for (int i = 0; i < 5; i++)
            {
                double y = 2.3 + i * 0.42;
                bd.Append(spline(Pts(G.P(-3.2, y + 0.1), G.P(-0.5, y - 0.06), G.P(3.4, y + 0.04))));
                if (S.o.lod >= 1) for (int j = 0; j < 14; j++) { var p = G.P(-2.8 + j * 0.45, y + 0.1 - (j > 5 ? 0.02 : 0)); rv.Append(circle(p[0], p[1], 0.9) + "/>"); }
            }
            S.add("body", "<g clip-path=\"url(#" + S.id("torso") + ")\"><path d=\"" + bd + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"2.2\"/><path d=\"" + bd + "\" fill=\"none\" stroke=\"" + Mt.h + "\" stroke-width=\"0.8\" transform=\"translate(0,1.5)\" opacity=\"0.6\"/><g fill=\"" + Mt.h + "\">" + rv + "</g></g>");
            // 颈甲
            var C = clothT(sp.Outfit.Or("color", "#8a2a24"));
            scarf(S, G, sp, T, C);
            pauldron(S, G, sp, T, Mt, -1, "#c8a050");
            pauldron(S, G, sp, T, Mt, 1, "#c8a050");
        }
        // 夷洲：敞开的织纹背心
        static void OutYivest(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            bareTorso(S, G, sp, T);
            var C = clothT(sp.Outfit.S("color") == "#1a1a1a" ? "#e8dcc0" : sp.Outfit.Or("color", "#e8dcc0"));
            string pat = sp.Outfit.Or("color2", "#b02a24");
            var sides = new[]
            {
                new KeyValuePair<List<double[]>, int>(Pts(G.P(0.95 * G.nw, 1.42), G.P(1.6, 1.8), G.P(2.65, 2.3), G.P(3.1, 2.9), G.P(3.4, 4.5, 0), G.P(0.9, 4.5, 0), G.P(0.6, 2.8)), 1),
                new KeyValuePair<List<double[]>, int>(Pts(G.P(-0.52 * G.nw, 1.72), G.P(-1.5, 2.1), G.P(-2.45, 2.6), G.P(-2.9, 3.1), G.P(-3.1, 4.5, 0), G.P(-1.1, 4.5, 0), G.P(-0.8, 2.6)), -1),
            };
            foreach (var pr in sides)
            {
                var pts = pr.Key; int side = pr.Value;
                S.shape("body2", spline(pts, true), side > 0 ? C.s : C.b, () =>
                {
                    var zd = new StringBuilder();
                    for (int r = 0; r < 4; r++) { double x = side > 0 ? 0.6 : -3.1; zd.Append("M" + ps(G.P(x, 2.9 + r * 0.38))); for (int i = 0; i < 9; i++) { x += 0.3; zd.Append("L" + ps(G.P(x, 2.9 + r * 0.38 + (i % 2 != 0 ? -0.14 : 0.14)))); } }
                    S.line("body2", zd.ToString(), pat, 2.2);
                }, S.lw);
            }
        }
        // 朝鲜：交领短袄（高句丽壁画的圆点纹）
        static void OutJacket(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var C = clothT(sp.Outfit.Or("color", "#c84a2a"));
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                var rr = new Rng(HashStr(sp.S("name") + "dots"));
                var dd = new StringBuilder();
                for (int i = 0; i < 36; i++) { double px = rr.range(-3, 3.2), py = rr.range(2.0, 4.3); var p = G.P(px, py); dd.Append(circle(p[0], p[1], 0.07 * R) + "/>"); }
                S.add("body", "<g fill=\"" + sp.Outfit.Or("dots", mix(C.b, "#1a1010", 0.55)) + "\" opacity=\"0.75\">" + dd + "</g>");
            });
            crossCollar(S, G, sp, T, sp.Outfit.Or("color2", "#2a2a3a"), "#ece4d4", 1.1);
        }
        // 翻领长袍（草原 / 西域 / 贵霜）：两片三角翻领
        static void OutKaftan(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var C = clothT(sp.Outfit.Or("color", "#8a5a3a"));
            var tr = clothT(sp.Outfit.Or("color2", "#c8a050"));
            double cross = sp.Outfit.Or("cross", 1);
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                if (S.o.lod >= 1) S.line("body", spline(Pts(G.P(-1.9, 2.9), G.P(-1.7, 3.5), G.P(-1.75, 4.3))) + spline(Pts(G.P(1.9, 2.7), G.P(2.05, 3.4), G.P(2.0, 4.3))), C.d, 1.4, " opacity=\"0.6\"");
            });
            // 内衬
            S.add("body2", "<path d=\"" + spline(Pts(G.P(-0.5 * G.nw, 1.72), G.P(0.92 * G.nw, 1.45), G.P(0.35, 2.7, 0)), true) + "\" fill=\"" + sp.Outfit.Or("inner", "#e8dcc0") + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            // 前襟镶边（直通下摆）
            var edge = cross > 0 ? Pts(G.P(0.35, 2.6), G.P(0.1, 3.4), G.P(-0.2, 4.5)) : Pts(G.P(0.35, 2.6), G.P(0.6, 3.4), G.P(0.85, 4.5));
            S.add("body2", "<path d=\"" + taper(edge, 0.2 * R) + "\" fill=\"" + tr.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            var flaps = new[]
            {
                new KeyValuePair<List<double[]>, string>(Pts(G.P(0.92 * G.nw, 1.42), G.P(0.38, 2.62, 0), G.P(1.25, 2.55, 0.3), G.P(1.2, 1.75)), tr.s),
                new KeyValuePair<List<double[]>, string>(Pts(G.P(-0.5 * G.nw, 1.72), G.P(0.34, 2.62, 0), G.P(-0.75, 2.75, 0.3), G.P(-0.9, 2.0)), tr.b),
            };
            foreach (var pr in flaps)
            {
                var pts = pr.Key;
                S.shape("body2", spline(pts, true), pr.Value, () =>
                {
                    if (S.o.lod >= 2) S.line("body2", spline(shift(Slice(pts, 0, 3), 0, 0.06 * R)), tr.h, 1.2, " opacity=\"0.8\" stroke-dasharray=\"2 2\"");
                }, S.lw * 0.85);
            }
        }
        // 毛皮大衣：厚毛领
        static void OutFur(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var C = clothT(sp.Outfit.Or("color", "#5a4a3a"));
            string furC = sp.Outfit.Or("fur", mix(sp.Outfit.Or("color2", "#8a6a4a"), "#7a5a3a", 0.5));
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                if (S.o.lod >= 1) S.line("body", spline(Pts(G.P(-1.9, 2.9), G.P(-1.7, 3.5), G.P(-1.75, 4.3))) + spline(Pts(G.P(1.9, 2.7), G.P(2.05, 3.4), G.P(2.0, 4.3))), C.d, 1.4, " opacity=\"0.6\"");
            });
            S.add("body2", "<path d=\"" + spline(Pts(G.P(-0.5 * G.nw, 1.72), G.P(0.92 * G.nw, 1.45), G.P(0.3, 3.2, 0)), true) + "\" fill=\"" + sp.Outfit.Or("inner", "#d8ccb0") + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
            var F = clothT(furC);
            var collar = Pts(G.P(-0.75 * G.nw, 1.6), G.P(-1.7, 2.0), G.P(-2.6, 2.55), G.P(-1.6, 2.9), G.P(-0.4, 3.3), G.P(0.3, 3.3, 0.3), G.P(1.6, 2.7), G.P(2.7, 2.3), G.P(1.6, 1.6), G.P(1.0 * G.nw, 1.3), G.P(0.5, 2.6, 0.3), G.P(-0.3, 2.6, 0.3));
            S.shape("body2", spline(collar, true), F.b, () => S.path("body2", spline(Pts(G.P(0.6, 1.4), G.P(2.8, 2.0), G.P(2.8, 3.0), G.P(0.6, 3.0)), true), F.s), S.lw);
            furEdge(S, "body2", G, Pts(G.P(-2.6, 2.55), G.P(-1.6, 2.95), G.P(-0.4, 3.35), G.P(0.3, 3.35), G.P(1.6, 2.75), G.P(2.75, 2.35)), furC, 0.12, 16);
        }
        // 南海：裸身斜披布带，金饰
        static void OutSash(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            if (sp.S("sex") == "f") OutTunic(S, G, OutfitWith(sp, JObj.Assign(sp.Outfit, new JObj { { "color", sp.Outfit.Or("color2", "#c8a030") } })), T, o);
            else bareTorso(S, G, sp, T);
            var C = clothT(sp.Outfit.Or("color", "#c8a030"));
            var sh = Pts(G.P(-1.6, 2.15), G.P(-0.8, 2.0), G.P(1.6, 4.5, 0), G.P(0.5, 4.5, 0), G.P(-1.9, 2.55));
            S.shape("body2", spline(sh, true), C.b, () =>
            {
                S.path("body2", spline(Pts(G.P(0.2, 3.3), G.P(1.8, 4.6), G.P(0.4, 4.6)), true), C.s);
                if (S.o.lod >= 1) S.line("body2", spline(Pts(G.P(-1.5, 2.2), G.P(0.9, 4.5))), GOLD, 1.6, " opacity=\"0.9\"");
            }, S.lw);
        }
        // 鱼鳞甲（安息、萨尔马提亚、西域）
        static void OutScale(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var Mt = metalT(sp.Outfit.Or("metal", "#7a808c"));
            torsoBase(S, G, sp, T, Mt.s, Mt.d);
            lamellae(S, G, Mt, G.ey + 1.85 * G.R, 262, new LamOpt { scale = true, rowH = 0.17, pw = 0.2, alt = mix(Mt.b, Mt.s, 0.35) });
            var C = clothT(sp.Outfit.Or("color", "#7a2a24"));
            string cu = sp.S("culture");
            if (cu == "persia" || cu == "sarmatian")
                S.add("body2", "<path d=\"" + taper(Pts(G.P(-0.6 * G.nw, 1.62), G.P(-0.2, 1.95), G.P(0.35, 1.98), G.P(0.85, 1.72), G.P(1.0 * G.nw, 1.42)), 0.22 * G.R) + "\" fill=\"" + C.b + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>");
            else scarf(S, G, sp, T, C);
            pauldron(S, G, sp, T, Mt, -1, sp.Outfit.S("trim"));
            pauldron(S, G, sp, T, Mt, 1, sp.Outfit.S("trim"));
        }
        // 长衫（圆领 / V 领 + 镶边，罗马与波斯的两道竖纹）
        static void OutTunic(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var C = clothT(sp.Outfit.Or("color", "#8a2a24"));
            string tr = sp.Outfit.Or("color2", "#c8a050");
            string cu = sp.S("culture");
            torsoBase(S, G, sp, T, C.b, C.s, () =>
            {
                if (S.o.lod >= 1) S.line("body", spline(Pts(G.P(-1.9, 2.9), G.P(-1.7, 3.5), G.P(-1.75, 4.3))) + spline(Pts(G.P(1.9, 2.7), G.P(2.05, 3.4), G.P(2.0, 4.3))), C.d, 1.4, " opacity=\"0.6\"");
                if (cu == "roman" || cu == "persia" || cu == "arab") S.add("body", "<path d=\"" + spline(Pts(G.P(-1.2, 2.15), G.P(-1.25, 4.5))) + spline(Pts(G.P(0.5, 2.05), G.P(0.55, 4.5))) + "\" fill=\"none\" stroke=\"" + tr + "\" stroke-width=\"" + N(0.16 * R) + "\" opacity=\"0.9\"/>");
            });
            var nl = Pts(G.P(-0.5 * G.nw, 1.72), G.P(-0.1, 2.3), G.P(0.35, 2.32), G.P(0.92 * G.nw, 1.45));
            S.add("body2", "<path d=\"" + taper(nl, 0.16 * R) + "\" fill=\"" + tr + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.75) + "\"/>");
            if (S.o.lod >= 2) S.add("body2", "<path d=\"" + spline(nl) + "\" fill=\"none\" stroke=\"" + mul(tr, 0.6) + "\" stroke-width=\"1\" stroke-dasharray=\"1.5 2\"/>");
        }
        // 阿拉伯长袍 + 斗篷
        static void OutRobea(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            bool white = sp.Outfit.S("color") == "#e8e0d0";
            OutTunic(S, G, OutfitWith(sp, JObj.Assign(sp.Outfit, new JObj { { "color", "#e8e0d0" }, { "color2", white ? sp.Outfit.Or("color2", "#5a3a2a") : sp.Outfit.G("color") } })), T, o);
            if (!sp.Outfit.T("cape")) mantle(S, G, sp, T, white ? sp.Outfit.Or("color2", "#5a3a2a") : sp.Outfit.S("color"));
        }
        // 罗马环片甲
        static void OutSegmentata(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            var Mt = metalT(sp.Outfit.Or("metal", "#8a8f9a"));
            torsoBase(S, G, sp, T, Mt.b, Mt.s);
            var bd = new StringBuilder();
            for (int i = 0; i < 6; i++) { double y = 2.5 + i * 0.36; bd.Append(spline(Pts(G.P(-3.2, y + 0.12), G.P(-0.5, y - 0.06), G.P(3.4, y + 0.06)))); }
            S.add("body", "<g clip-path=\"url(#" + S.id("torso") + ")\"><path d=\"" + bd + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"2.2\"/><path d=\"" + bd + "\" fill=\"none\" stroke=\"" + Mt.h + "\" stroke-width=\"1\" transform=\"translate(0,1.6)\" opacity=\"0.7\"/></g>");
            // 胸前铜扣
            if (S.o.lod >= 1) foreach (var y in new[] { 2.62, 2.98 }) { var c = G.P(-0.45, y); S.add("body", "<rect x=\"" + N(c[0] - 3) + "\" y=\"" + N(c[1] - 2) + "\" width=\"6\" height=\"4\" fill=\"#c8a050\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>"); }
            // 肩部环片
            foreach (int side in new[] { -1, 1 })
            {
                double sx = side > 0 ? 2.3 : -2.15, sy = side > 0 ? 2.4 : 2.65;
                for (int i = 3; i >= 0; i--)
                {
                    double w = side > 0 ? 1.05 : 0.8;
                    var pts = Pts(G.P(sx - side * w, sy - 0.3 + i * 0.22, 0.4), G.P(sx, sy - 0.62 + i * 0.22), G.P(sx + side * w, sy - 0.18 + i * 0.22, 0.4), G.P(sx + side * w, sy + 0.04 + i * 0.22), G.P(sx, sy - 0.4 + i * 0.22), G.P(sx - side * w, sy - 0.08 + i * 0.22));
                    S.shape("body2", spline(pts, true), i % 2 != 0 ? Mt.s : Mt.b, null, S.lw * 0.8);
                }
            }
            scarf(S, G, sp, T, clothT(sp.Outfit.Or("color", "#a02a24")));
        }
        // 罗马胸甲（肌肉甲）+ 肩部皮条
        static void OutMusculata(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var Mt = metalT(sp.Outfit.Or("metal", "#b08a50"));
            // 肩部皮条（pteruges）
            string pc = sp.Outfit.Or("color2", "#e8dcc0");
            foreach (int side in new[] { -1, 1 })
            {
                var d = new StringBuilder();
                for (int i = 0; i < 5; i++) { var p = G.P(side * (side > 0 ? 2.3 : 2.05) + side * (i - 2) * 0.22, side > 0 ? 2.6 : 2.8); d.Append("M" + N(p[0] - 0.09 * R) + "," + N(p[1]) + "h" + N(0.18 * R) + "v" + N(0.8 * R) + "h" + N(-0.18 * R) + "z"); }
                S.add("body", "<path d=\"" + d + "\" fill=\"" + pc + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
            }
            var cuir = Pts(G.P(-0.5 * G.nw, 1.75), G.P(-1.4 * G.bw, 2.15), G.P(-2.0 * G.bw, 2.6), G.P(-2.1, 4.5, 0), G.P(2.4, 4.5, 0), G.P(2.25 * G.bw, 2.45), G.P(1.5 * G.bw, 1.85), G.P(0.92 * G.nw, 1.5));
            S.shape("body", spline(cuir, true), Mt.b, () =>
            {
                S.path("body", spline(Pts(G.P(0.75, 1.4), G.P(1.15, 2.4), G.P(1.25, 3.2), G.P(1.1, 4.6, 0), G.P(4, 4.6, 0), G.P(4, 1.4, 0)), true), Mt.s);
                S.line("body", spline(Pts(G.P(-2.0, 3.0), G.P(-1.4, 3.45), G.P(-0.6, 3.3), G.P(-0.42, 3.05))) + spline(Pts(G.P(-0.3, 3.0), G.P(0.4, 3.35), G.P(1.4, 3.25), G.P(2.0, 2.8))) + spline(Pts(G.P(-0.4, 3.4), G.P(-0.4, 4.4))), Mt.d, 2.0);
                S.path("body", spline(Pts(G.P(-1.9, 2.6), G.P(-1.2, 2.5), G.P(-0.8, 3.0), G.P(-1.6, 3.2)), true), Mt.h, 0, " opacity=\"0.7\"");
            }, S.lw, "torso");
            rimOn(S, "body", cuir, T.rim, 3.2, 0.85);
            if (!sp.Outfit.T("cape")) mantle(S, G, sp, T, sp.Outfit.Or("color", "#a02a24"));
        }
        // 托加袍：白袍斜搭，紫边
        static void OutToga(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            var C = clothT(sp.Outfit.Or("toga", "#ece6da"));
            torsoBase(S, G, sp, T, C.b, C.s);
            var drape = Pts(G.P(-0.6 * G.nw, 1.7), G.P(-1.5, 2.1), G.P(-2.5, 2.6), G.P(-3.0, 3.2), G.P(-3.2, 4.5, 0), G.P(-0.2, 4.5, 0), G.P(0.6, 3.3), G.P(1.1, 2.4), G.P(0.2, 2.6));
            S.shape("body2", spline(drape, true), C.b, () =>
            {
                var fd = new StringBuilder();
                for (int i = 0; i < 5; i++) fd.Append(spline(Pts(G.P(-1.6 + i * 0.4, 2.3 + i * 0.05), G.P(-0.9 + i * 0.4, 3.2), G.P(-0.6 + i * 0.35, 4.4))));
                S.line("body2", fd.ToString(), C.d, 1.5, " opacity=\"0.6\"");
                S.path("body2", spline(Pts(G.P(-0.2, 2.8), G.P(1.2, 2.3), G.P(0.8, 4.6), G.P(-0.4, 4.6)), true), C.s, 0, " opacity=\"0.7\"");
            }, S.lw);
            S.add("body2", "<path d=\"" + taper(Pts(G.P(1.1, 2.4), G.P(0.6, 3.3), G.P(-0.2, 4.5)), 0.14 * R) + "\" fill=\"" + sp.Outfit.Or("color2", "#6a2a6a") + "\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.7) + "\"/>");
        }
        // 凯尔特：方格斗篷 + 圆形别针
        static void OutPlaid(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            double R = G.R;
            OutTunic(S, G, OutfitWith(sp, JObj.Assign(sp.Outfit, new JObj { { "color", sp.Outfit.Or("color2", "#6a5a3a") }, { "color2", "#8a6a3a" } })), T, o);
            var C = clothT(sp.Outfit.Or("color", "#3a6a3a"));
            var cloak = Pts(G.P(0.95 * G.nw, 1.42), G.P(1.7 * G.bw, 1.78), G.P(2.75 * G.bw, 2.3, 0.8), G.P(3.25 * G.bw, 3.0), G.P(3.5 * G.bw, 4.5, 0), G.P(-0.5, 4.5, 0), G.P(-0.3, 3.0), G.P(-0.6 * G.nw, 1.75), G.P(-1.5, 2.1), G.P(-2.5, 2.6), G.P(-2.9, 3.0), G.P(-2.4, 2.4));
            S.shape("body2", spline(cloak, true), C.b, () =>
            {
                string ck = sp.Outfit.Or("check", "#c8a040");
                var hd = new StringBuilder(); var vd = new StringBuilder();
                for (int i = 0; i < 10; i++) { hd.Append("M" + ps(G.P(-3.4, 1.6 + i * 0.32)) + "l" + N(7.2 * R) + "," + N(0.3 * R)); vd.Append("M" + ps(G.P(-3.2 + i * 0.75, 1.4)) + "l" + N(0.2 * R) + "," + N(3.4 * R)); }
                S.add("body2", "<path d=\"" + hd + vd + "\" fill=\"none\" stroke=\"" + ck + "\" stroke-width=\"" + N(0.1 * R) + "\" opacity=\"0.5\"/>");
                S.add("body2", "<path d=\"" + hd + vd + "\" fill=\"none\" stroke=\"" + C.d + "\" stroke-width=\"" + N(0.05 * R) + "\" opacity=\"0.6\" transform=\"translate(" + N(0.12 * R) + "," + N(0.12 * R) + ")\"/>");
                S.path("body2", spline(Pts(G.P(0.75, 1.4), G.P(1.15, 2.4), G.P(1.25, 3.2), G.P(1.1, 4.6, 0), G.P(4, 4.6, 0), G.P(4, 1.4, 0)), true), C.s, 0, " opacity=\"0.6\"");
            }, S.lw);
            var b = G.P(1.2, 2.2);
            S.add("body2", circle(b[0], b[1], 0.24 * R) + " fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.1 * R + 2) + "\"/>" + circle(b[0], b[1], 0.24 * R) + " fill=\"none\" stroke=\"" + GOLD + "\" stroke-width=\"" + N(0.1 * R) + "\"/>");
            S.line("body2", "M" + ps(add(b, -0.35 * R, -0.25 * R)) + "L" + ps(add(b, 0.35 * R, 0.25 * R)), GOLD, 1.6);
        }

        // ======================================================== 配饰 ==
        static List<double[]> catenary(double[] a, double[] b, double sag, int n)
        {
            var o = new List<double[]>();
            for (int i = 0; i <= n; i++) { double t = (double)i / n; o.Add(V(lerp(a[0], b[0], t), lerp(a[1], b[1], t) + Math.Sin(Math.PI * t) * sag)); }
            return o;
        }
        static void DrawAcc(Svg S, Geo G, JObj sp, Theme T)
        {
            var acc = sp.L("acc");
            if (acc == null || acc.Count == 0) return;
            double R = G.R; var E = G.ear;
            var lobe = V(lerp(E.x0, E.x1, 0.42), E.y1 - 0.02 * R);
            bool earVisible = !G.hideEar;
            foreach (var ao in acc)
            {
                string a = ao as string;
                if ((a == "earring" || a == "hoops" || a == "bigears") && earVisible)
                {
                    if (a == "earring") S.add("ear", "<path d=\"M" + ps(lobe) + "l0," + N(0.12 * R) + "\" stroke=\"" + GOLD + "\" stroke-width=\"1.4\"/>" + circle(lobe[0], lobe[1] + 0.2 * R, 0.08 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
                    else if (a == "hoops") S.add("ear", circle(lobe[0], lobe[1] + 0.18 * R, 0.2 * R) + " fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"" + N(0.07 * R + 2) + "\"/>" + circle(lobe[0], lobe[1] + 0.18 * R, 0.2 * R) + " fill=\"none\" stroke=\"" + GOLD + "\" stroke-width=\"" + N(0.07 * R) + "\"/>");
                    else S.add("ear", ellipse(lobe[0], lobe[1] + 0.12 * R, 0.13 * R, 0.17 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>" + ellipse(lobe[0], lobe[1] + 0.12 * R, 0.06 * R, 0.08 * R) + " fill=\"#8a5a20\"/>");
                }
                if (a == "bones" || a == "magatama" || a == "shells")
                {
                    var pts = catenary(G.P(-0.95, 2.0), G.P(1.05, 1.7), (a == "bones" ? 0.75 : 0.6) * R, 11);
                    S.add("body2", "<path d=\"" + spline(pts) + "\" fill=\"none\" stroke=\"" + INK + "\" stroke-width=\"2\"/>");
                    var inner = Slice(pts, 1, -1);
                    for (int i = 0; i < inner.Count; i++)
                    {
                        var p = inner[i];
                        if (a == "bones") S.add("body2", "<path d=\"" + leaf(p, add(p, 0.02 * R, 0.3 * R), 0.12 * R, 0.1) + "\" fill=\"#efe6d0\" stroke=\"" + INK + "\" stroke-width=\"1\"/>");
                        else if (a == "magatama") S.add("body2", i % 2 != 0 ? magatama(p[0], p[1] + 0.06 * R, 0.08 * R, "#3a9a6a") : circle(p[0], p[1], 0.05 * R) + " fill=\"#d8d0b8\" stroke=\"#1a1012\" stroke-width=\"0.7\"/>");
                        else S.add("body2", ellipse(p[0], p[1] + 0.04 * R, 0.07 * R, 0.05 * R) + " fill=\"#f4f0e4\" stroke=\"" + INK + "\" stroke-width=\"0.8\"/>");
                    }
                }
                if (a == "torc")
                {
                    var pts = Pts(G.P(-0.62 * G.nw, 1.62), G.P(-0.2, 1.95), G.P(0.35, 1.98), G.P(0.85, 1.74), G.P(1.02 * G.nw, 1.45));
                    S.add("body2", "<path d=\"" + taper(pts, 0.11 * R) + "\" fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.2\"/>");
                    if (S.o.lod >= 1) S.add("body2", "<path d=\"" + spline(pts) + "\" fill=\"none\" stroke=\"#8a5a1e\" stroke-width=\"1\" stroke-dasharray=\"2 2\"/>");
                    foreach (var p in new[] { G.P(-0.12, 2.05), G.P(0.22, 2.08) }) S.add("body2", circle(p[0], p[1], 0.1 * R) + " fill=\"" + GOLD + "\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>");
                }
                if (a == "goldcollar")
                {
                    var outer = catenary(G.P(-1.8, 2.3), G.P(2.0, 1.9), 1.15 * R, 12); var inner = catenary(G.P(-0.6, 1.85), G.P(0.95, 1.55), 0.55 * R, 8);
                    S.shape("body2", spline(Cat(outer, Rev(inner)), true), GOLD, () =>
                    {
                        var jd = new StringBuilder();
                        foreach (var p in Slice(catenary(G.P(-1.2, 2.1), G.P(1.5, 1.75), 0.85 * R, 9), 1, -1)) jd.Append(circle(p[0], p[1], 0.07 * R) + "/>");
                        S.add("body2", "<g fill=\"#c02a3a\" stroke=\"" + INK + "\" stroke-width=\"0.7\">" + jd + "</g>");
                    }, S.lw * 0.8);
                }
                if (a == "mirror")
                {
                    var c = G.P(-0.5, 3.0);
                    S.add("body2", circle(c[0], c[1], 0.36 * R) + " fill=\"#b08a3a\" stroke=\"" + INK + "\" stroke-width=\"" + N(S.lw * 0.8) + "\"/>" + circle(c[0], c[1], 0.26 * R) + " fill=\"none\" stroke=\"#7a5a20\" stroke-width=\"1.3\"/>" + circle(c[0] - 0.1 * R, c[1] - 0.1 * R, 0.1 * R) + " fill=\"#f4e0a0\" opacity=\"0.7\"/>");
                }
                if (a == "knives")
                {
                    // 祝融：鬓插飞刀
                    for (int i = 0; i < 3; i++)
                    {
                        double[] b = G.h3(0.9 + i * 0.22, 0.75 + i * 0.05, 1.08), t = add(b, (0.35 + i * 0.05) * R, -0.7 * R);
                        S.add("hat", "<path d=\"" + poly(Pts(add(b, -0.04 * R, 0), add(lp(b, t, 0.7), -0.07 * R, 0), t, add(lp(b, t, 0.7), 0.07 * R, 0), add(b, 0.04 * R, 0))) + "\" fill=\"#d8dee8\" stroke=\"" + INK + "\" stroke-width=\"1.1\"/>");
                        S.add("hat", "<path d=\"M" + ps(b) + "l" + N(-0.05 * R) + "," + N(0.18 * R) + "\" stroke=\"#8a2a20\" stroke-width=\"2.4\"/>");
                    }
                }
                if (a == "flower") S.add("hat", flower(G.h3(1.05, 0.55, 1.1)[0], G.h3(1.05, 0.55, 1.1)[1], 0.18 * R, "#f06080"));
            }
        }
        // 面部纹样
        static void DrawFaceMarks(Svg S, Geo G, JObj sp, Theme T)
        {
            foreach (var mo in sp.L("marks") ?? new List<object>())
            {
                string m = mo as string;
                if (m == "tattoo-wa")
                {
                    var d = new StringBuilder();
                    for (int i = 0; i < 3; i++) d.Append(spline(Pts(G.F(0.28 + i * 0.08, 0.2), G.F(0.24 + i * 0.1, 0.45), G.F(0.3 + i * 0.1, 0.7))));
                    for (int i = 0; i < 2; i++) d.Append(spline(Pts(G.F(-0.62 - i * 0.08, 0.22), G.F(-0.6 - i * 0.08, 0.45), G.F(-0.66 - i * 0.08, 0.66))));
                    d.Append(spline(Pts(G.F(-0.3, -0.6), G.F(-0.1, -0.68), G.F(0.1, -0.6))));
                    S.line("face", d.ToString(), "#2a3a52", 1.6, " opacity=\"0.75\"");
                }
                else if (m == "tattoo-yi")
                {
                    var d = new StringBuilder();
                    for (int i = -1; i <= 1; i++) d.Append("M" + ps(G.F(i * 0.08 - 0.02, -0.75)) + "L" + ps(G.F(i * 0.08 - 0.02, -0.45)));
                    d.Append(spline(Pts(G.F(-0.4, G.chinY - 0.1, 0.05), G.F(-0.1, G.chinY - 0.04, 0.1), G.F(0.2, G.chinY - 0.1, 0.05))));
                    d.Append(spline(Pts(G.F(-0.36, G.chinY - 0.2, 0.05), G.F(-0.1, G.chinY - 0.14, 0.1), G.F(0.18, G.chinY - 0.2, 0.05))));
                    S.line("face", d.ToString(), "#22303e", 2.0, " opacity=\"0.8\"");
                }
                else if (m == "woad")
                {
                    var c = G.F(0.3, 0.42);
                    var d = new StringBuilder("M" + N(c[0]) + "," + N(c[1]) + "m-1,0a2,2 0 1,1 2,2a4,4 0 1,1 -4,-4a6,6 0 1,1 6,6");
                    var c2 = G.F(-0.15, -0.62);
                    d.Append("M" + N(c2[0]) + "," + N(c2[1]) + "m-1,0a2,2 0 1,1 2,2a4,4 0 1,1 -4,-4");
                    d.Append(spline(Pts(G.F(-0.75, 0.25), G.F(-0.62, 0.42), G.F(-0.72, 0.6))));
                    S.line("face", d.ToString(), "#2a5aa8", 1.8, " opacity=\"0.75\"");
                }
                else if (m == "warpaint")
                {
                    S.line("face", spline(Pts(G.F(0.12, 0.3), G.F(0.5, 0.25))) + spline(Pts(G.F(0.14, 0.42), G.F(0.52, 0.38))), "#c8302a", 2.4, " opacity=\"0.85\"");
                    S.line("face", spline(Pts(G.F(-0.5, 0.3), G.F(-0.75, 0.28))), "#c8302a", 2.2, " opacity=\"0.85\"");
                    S.line("face", spline(Pts(G.F(-0.2, -0.58), G.F(0.05, -0.6))), "#f0ece0", 2.2, " opacity=\"0.9\"");
                }
                else if (m == "freckles" && S.o.lod >= 1)
                {
                    var rr = new Rng(HashStr(sp.S("name") + "fr"));
                    var d = new StringBuilder();
                    for (int i = 0; i < 18; i++) { double u = rr.range(-0.65, 0.4), y = rr.range(0.15, 0.55); var p = G.F(u, y, u > -0.2 && u < 0.1 ? 0.15 : 0); d.Append(circle(p[0], p[1], 0.7) + "/>"); }
                    S.add("face", "<g fill=\"" + mix(T.skin.b, "#7a3a1a", 0.45) + "\" opacity=\"0.7\">" + d + "</g>");
                }
            }
        }

        // ======================================================== 主装配 ==
        static void Figure(Svg S, Geo G, JObj sp, Theme T, PortraitOpts o)
        {
            DrawBody(S, G, sp, T, o);
            DrawNeck(S, G, sp, T);
            DrawHead(S, G, sp, T);
            var hatInfo = DrawHat(S, G, sp, T);
            DrawHair(S, G, sp, T, hatInfo);
            DrawEar(S, G, sp, T);
            DrawWrinkles(S, G, sp, T);
            DrawEyes(S, G, sp, T, o.mood);
            DrawBrows(S, G, sp, T, o.mood);
            DrawNose(S, G, sp, T);
            DrawMouth(S, G, sp, T, o.mood);
            DrawMarks(S, G, sp, T, o.mood);
            DrawBeard(S, G, sp, T);
            DrawMustache(S, G, sp, T);
            DrawWeapon(S, G, sp, T);
            DrawFan(S, G, sp, T);
            DrawAcc(S, G, sp, T);
            DrawFaceMarks(S, G, sp, T);
        }
        const double ZOOM = 1.1;
        internal sealed class Parts { public Svg S; public string fig; public int w; }
        // 生成各部分：fig（人物，含镜像 g）、defs（裁剪路径等）、bg / frame（背景与边框，只与颜色和细节档有关）
        static Parts BuildParts(JObj sp, PortraitOpts o)
        {
            var S = new Svg(o);
            var G = Geom(sp);
            string rimC = mix("#d6ecff", o.color, 0.3);
            var T = new Theme { skin = skinT(sp.S("skin")), rim = rimC };
            DrawBackground(S, o);
            DrawFrame(S, o);
            Figure(S, G, sp, T, o);
            var L = S.L;
            string head = string.Join("", L["head"].ToArray()).Replace("%HEADSHADE%", string.Join("", S.headShade.ToArray()));
            var inner = new StringBuilder();
            foreach (var k in new[] { "back", "collarBack", "neck", "body", "body2" }) foreach (var x in L[k]) inner.Append(x);
            inner.Append(head);
            foreach (var k in new[] { "hair", "ear", "face", "beard", "hat", "front", "top" }) foreach (var x in L[k]) inner.Append(x);
            string flip = o.flip ? " transform=\"matrix(-1 0 0 1 256 0)\"" : "";
            // 构图：人物整体以 (132, 150) 为中心放大 ZOOM 倍（原作头像的脸占画面更大）
            string zoom = "translate(" + N(132 * (1 - ZOOM)) + "," + N(150 * (1 - ZOOM)) + ") scale(" + Js.Num(ZOOM) + ")";
            return new Parts { S = S, fig = "<g" + flip + " stroke-linejoin=\"round\" stroke-linecap=\"round\"><g transform=\"" + zoom + "\">" + inner + "</g></g>", w = o.frame ? S.win : 0 };
        }
        static string WinClipDef(Svg S, int w)
        {
            return "<clipPath id=\"" + S.id("win") + "\"><rect x=\"" + w + "\" y=\"" + w + "\" width=\"" + (256 - 2 * w) + "\" height=\"" + (256 - 2 * w) + "\"/></clipPath>";
        }
        static string BuildSvg(JObj sp, PortraitOpts o)
        {
            var p = BuildParts(sp, o);
            var S = p.S;
            return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + o.px + "\" height=\"" + o.px + "\" viewBox=\"0 0 256 256\"><defs>" + WinClipDef(S, p.w) + string.Join("", S.defs.ToArray()) + "</defs>" +
                "<g clip-path=\"url(#" + S.id("win") + ")\">" + S.bg + p.fig + "</g>" + (o.frame ? S.frame : "") + "</svg>";
        }
        // 只含背景或只含边框的 SVG（按颜色、像素、细节档缓存成栅格）
        sealed class ChromeSvg { public string bg, frame; public int w; }
        static ChromeSvg ChromeSvgs(PortraitOpts o)
        {
            var S = new Svg(o);
            DrawBackground(S, o);
            DrawFrame(S, o);
            int w = o.frame ? S.win : 0;
            string head = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + o.px + "\" height=\"" + o.px + "\" viewBox=\"0 0 256 256\"><defs>" + WinClipDef(S, w) + string.Join("", S.defs.ToArray()) + "</defs>";
            return new ChromeSvg { bg = head + "<g clip-path=\"url(#" + S.id("win") + ")\">" + S.bg + "</g></svg>", frame = o.frame ? head + S.frame + "</svg>" : null, w = w };
        }
    }
}
namespace Sanguo
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
#if !SANGUO_HEADLESS
    using UnityEngine;
#endif

    public enum PortraitMood { Neutral, Angry, Hurt, Win }

    public static partial class Portrait
    {
        // ======================================================== SVG 子集 → Raster ==
        // 头像只用到 SVG 的一个小子集（path / ellipse / circle / rect / g，填充、描边、不透明度、
        // 裁剪、简单变换、背景与边框的线性 / 径向渐变）。直接解释成 Raster 的绘制调用（同 JS 的 paintMarkup）。
        sealed class Attrs
        {
            public readonly Dictionary<string, string> a = new Dictionary<string, string>();
            public string this[string k] { get { string v; return a.TryGetValue(k, out v) ? v : null; } }
            public void Parse(string s, int i0, int i1)
            {
                a.Clear();
                int i = i0;
                while (i < i1)
                {
                    while (i < i1 && (s[i] == ' ' || s[i] == '\n' || s[i] == '\t')) i++;
                    int ks = i;
                    while (i < i1 && s[i] != '=' && s[i] != ' ' && s[i] != '/') i++;
                    if (i >= i1 || s[i] != '=') { i++; continue; }
                    string key = s.Substring(ks, i - ks);
                    i++;
                    if (i >= i1 || s[i] != '"') continue;
                    int vs = ++i;
                    while (i < i1 && s[i] != '"') i++;
                    a[key] = s.Substring(vs, i - vs);
                    i++;
                }
            }
        }
        static double Num(string s, double def = 0)
        {
            double v;
            return s != null && double.TryParse(s, NumberStyles.Float, CI, out v) ? v : def;
        }
        static double[] Nums(string s)
        {
            var p = s.Split(new[] { ' ', ',', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var o = new double[p.Length];
            for (int i = 0; i < p.Length; i++) o[i] = Num(p[i]);
            return o;
        }
        static void ApplyTransform(Raster ctx, string tr)
        {
            int i = 0;
            while (i < tr.Length)
            {
                int p = tr.IndexOf('(', i);
                if (p < 0) break;
                int q = tr.IndexOf(')', p);
                if (q < 0) break;
                string name = tr.Substring(i, p - i).Trim(' ', ',');
                var v = Nums(tr.Substring(p + 1, q - p - 1));
                if (name == "matrix" && v.Length >= 6) ctx.ApplyTransform(v[0], v[1], v[2], v[3], v[4], v[5]);
                else if (name == "translate" && v.Length >= 1) ctx.Translate(v[0], v.Length > 1 ? v[1] : 0);
                else if (name == "scale" && v.Length >= 1) ctx.Scale(v[0], v.Length > 1 ? v[1] : v[0]);
                else if (name == "rotate" && v.Length >= 1)
                {
                    double r = v[0] * Math.PI / 180;
                    if (v.Length >= 3) { ctx.Translate(v[1], v[2]); ctx.Rotate(r); ctx.Translate(-v[1], -v[2]); } else ctx.Rotate(r);
                }
                i = q + 1;
            }
        }
        static Path2D ShapePath(string tag, Attrs a)
        {
            Path2D p = null;
            if (tag == "path") p = new Path2D(a["d"]);
            else if (tag == "ellipse") { p = new Path2D(); p.Ellipse(Num(a["cx"]), Num(a["cy"]), Math.Max(0, Num(a["rx"])), Math.Max(0, Num(a["ry"])), 0, 0, Math.PI * 2); }
            else if (tag == "circle") { p = new Path2D(); p.Arc(Num(a["cx"]), Num(a["cy"]), Math.Max(0, Num(a["r"])), 0, Math.PI * 2); }
            else if (tag == "rect")
            {
                p = new Path2D();
                double x = Num(a["x"]), y = Num(a["y"]), w = Num(a["width"]), h = Num(a["height"]), r = Num(a["rx"]);
                if (r > 0) p.RoundRect(x, y, w, h, r); else p.Rect(x, y, w, h);
            }
            return p;
        }
        // defs 里的 <clipPath id><path|rect/></clipPath> 与渐变
        sealed class Defs
        {
            readonly Dictionary<string, string> clipSrc = new Dictionary<string, string>();
            readonly Dictionary<string, Path2D> clipPath = new Dictionary<string, Path2D>();
            public readonly Dictionary<string, Paint> grads = new Dictionary<string, Paint>();
            public Defs(string defs)
            {
                int i = 0;
                while ((i = defs.IndexOf("<clipPath id=\"", i, StringComparison.Ordinal)) >= 0)
                {
                    int s = i + 14, e = defs.IndexOf('"', s);
                    int b = defs.IndexOf('>', e) + 1, c = defs.IndexOf("</clipPath>", b, StringComparison.Ordinal);
                    clipSrc[defs.Substring(s, e - s)] = defs.Substring(b, c - b);
                    i = c;
                }
                ParseGrads(defs);
            }
            public Path2D Clip(string id)
            {
                Path2D p;
                if (clipPath.TryGetValue(id, out p)) return p;
                string src;
                if (clipSrc.TryGetValue(id, out src))
                {
                    int t0 = src.IndexOf('<'); int t1 = t0 + 1;
                    while (t1 < src.Length && char.IsLetter(src[t1])) t1++;
                    int end = src.IndexOf('>', t1);
                    var at = new Attrs(); at.Parse(src, t1, end);
                    p = ShapePath(src.Substring(t0 + 1, t1 - t0 - 1), at);
                }
                clipPath[id] = p;
                return p;
            }
            void ParseGrads(string defs)
            {
                foreach (var kind in new[] { "linearGradient", "radialGradient" })
                {
                    int i = 0;
                    while ((i = defs.IndexOf("<" + kind, i, StringComparison.Ordinal)) >= 0)
                    {
                        int h = defs.IndexOf('>', i);
                        var at = new Attrs(); at.Parse(defs, i + kind.Length + 1, h);
                        int end = defs.IndexOf("</" + kind + ">", h, StringComparison.Ordinal);
                        Paint g = kind[0] == 'l'
                            ? Paint.Linear(Num(at["x1"], 0), Num(at["y1"], 0), Num(at["x2"], 1), Num(at["y2"], 0))
                            : Paint.Radial(Num(at["cx"], 0.5), Num(at["cy"], 0.5), Num(at["r"], 0.5));
                        int j = h;
                        while ((j = defs.IndexOf("<stop", j, StringComparison.Ordinal)) >= 0 && j < end)
                        {
                            int k = defs.IndexOf('>', j);
                            var sa = new Attrs(); sa.Parse(defs, j + 5, k);
                            var c = RGBA.Parse(sa["stop-color"] ?? "#000");
                            c.a *= (float)Num(sa["stop-opacity"], 1);
                            g.AddStop(Num(sa["offset"]), c);
                            j = k;
                        }
                        grads[at["id"] ?? ""] = g;
                        i = end;
                    }
                }
            }
        }
        sealed class PSt { public string fill = "#000", stroke = "none", lj = "miter", lc = "butt"; public double sw = 1, alpha = 1; public PSt Clone() { return (PSt)MemberwiseClone(); } }
        static LineJoin Join(string s) { return s == "round" ? LineJoin.Round : s == "bevel" ? LineJoin.Bevel : LineJoin.Miter; }
        static LineCap Cap(string s) { return s == "round" ? LineCap.Round : s == "square" ? LineCap.Square : LineCap.Butt; }
        static readonly Dictionary<string, Paint> solidCache = new Dictionary<string, Paint>();
        static Paint Solid(string c)
        {
            lock (solidCache)
            {
                Paint p;
                if (!solidCache.TryGetValue(c, out p)) { p = Paint.Solid(c); solidCache[c] = p; }
                return p;
            }
        }
        // url(#id) → 渐变（objectBoundingBox 单位）；找不到时为 null（不画）
        static Paint PaintOf(Raster ctx, string v, Defs defs)
        {
            if (v[0] != 'u') return Solid(v);
            if (defs == null) return null;
            Paint g;
            int a = v.IndexOf('#'), b = v.IndexOf(')');
            if (a < 0 || b < a || !defs.grads.TryGetValue(v.Substring(a + 1, b - a - 1), out g)) return null;
            ctx.UseBoundingBoxUnits(g);
            return g;
        }
        static void PaintMarkup(Raster ctx, string m, Defs defs)
        {
            var stack = new Stack<PSt>();
            var st = new PSt();
            var a = new Attrs();
            int depth = 0;
            int i = 0, n = m.Length;
            while ((i = m.IndexOf('<', i)) >= 0)
            {
                int e = m.IndexOf('>', i);
                if (e < 0) break;
                bool close = m[i + 1] == '/';
                int t0 = close ? i + 2 : i + 1, t1 = t0;
                while (t1 < e && char.IsLetter(m[t1])) t1++;
                string tag = m.Substring(t0, t1 - t0);
                bool self = m[e - 1] == '/';
                int ae = self ? e - 1 : e;
                i = e + 1;
                if (tag == "g")
                {
                    if (close) { if (depth > 0) { ctx.Restore(); depth--; st = stack.Count > 0 ? stack.Pop() : st; } continue; }
                    a.Parse(m, t1, ae);
                    stack.Push(st);
                    st = st.Clone();
                    ctx.Save(); depth++;
                    string tr = a["transform"];
                    if (tr != null) ApplyTransform(ctx, tr);
                    string cp = a["clip-path"];
                    if (cp != null && defs != null)
                    {
                        var p = defs.Clip(cp.Substring(5, cp.Length - 6));
                        if (p != null) ctx.Clip(p);
                    }
                    if (a["fill"] != null) st.fill = a["fill"];
                    if (a["stroke"] != null) st.stroke = a["stroke"];
                    if (a["stroke-width"] != null) st.sw = Num(a["stroke-width"]);
                    if (a["stroke-linejoin"] != null) st.lj = a["stroke-linejoin"];
                    if (a["stroke-linecap"] != null) st.lc = a["stroke-linecap"];
                    if (a["opacity"] != null) st.alpha *= Num(a["opacity"]);
                    if (self) { ctx.Restore(); depth--; st = stack.Pop(); } // <g/>
                    continue;
                }
                if (close || (tag != "path" && tag != "ellipse" && tag != "circle" && tag != "rect")) continue;
                a.Parse(m, t1, ae);
                var path = ShapePath(tag, a);
                if (path == null) continue;
                string trs = a["transform"];
                if (trs != null) { ctx.Save(); ApplyTransform(ctx, trs); }
                double alpha = st.alpha * (a["opacity"] != null ? Num(a["opacity"]) : 1);
                string fill = a["fill"] ?? st.fill;
                if (fill != "none")
                {
                    var pf = PaintOf(ctx, fill, defs);
                    if (pf != null)
                    {
                        ctx.GlobalAlpha = (float)(alpha * (a["fill-opacity"] != null ? Num(a["fill-opacity"]) : 1));
                        ctx.FillStyle = pf;
                        ctx.Fill(path, a["fill-rule"] == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero);
                    }
                }
                string stroke = a["stroke"] ?? st.stroke;
                if (stroke != "none")
                {
                    var ps0 = PaintOf(ctx, stroke, defs);
                    if (ps0 != null)
                    {
                        ctx.GlobalAlpha = (float)(alpha * (a["stroke-opacity"] != null ? Num(a["stroke-opacity"]) : 1));
                        ctx.StrokeStyle = ps0;
                        ctx.LineWidth = a["stroke-width"] != null ? Num(a["stroke-width"]) : st.sw;
                        ctx.LineJoin = Join(a["stroke-linejoin"] ?? st.lj);
                        ctx.LineCap = Cap(a["stroke-linecap"] ?? st.lc);
                        string da = a["stroke-dasharray"];
                        if (da != null) ctx.SetLineDash(Nums(da));
                        ctx.Stroke(path);
                        if (da != null) ctx.SetLineDash(null);
                    }
                }
                if (trs != null) ctx.Restore();
            }
            while (depth > 0) { ctx.Restore(); depth--; }
            ctx.GlobalAlpha = 1;
        }
        // 整份 SVG（<svg …><defs>…</defs>…</svg>）画到 px×px 的栅格上（背景 / 边框缓存用）
        static void PaintSvgDoc(Raster ctx, string svg, int px)
        {
            int d0 = svg.IndexOf("<defs>", StringComparison.Ordinal), d1 = svg.IndexOf("</defs>", StringComparison.Ordinal);
            var defs = new Defs(d0 >= 0 && d1 > d0 ? svg.Substring(d0 + 6, d1 - d0 - 6) : "");
            string body = d1 >= 0 ? svg.Substring(d1 + 7) : svg;
            ctx.Save();
            ctx.Scale(px / 256.0, px / 256.0);
            PaintMarkup(ctx, body, defs);
            ctx.Restore();
        }

        // 任意 SVG 文档（同一子集）栅格化为 px×px（测试用）
        public static Raster RenderSvgDocument(string svg, int px)
        {
            var r = new Raster(px, px);
            PaintSvgDoc(r, svg, px);
            return r;
        }

        // ======================================================== 缓存与栅格化 ==
        public static string KeyOf(JObj sp, PortraitOpts o) { return sp.S("key") + "|" + o.px + "|" + o.color + "|" + (o.frame ? 1 : 0) + (o.flip ? 1 : 0) + "|" + o.mood; }
        sealed class Chrome { public Raster bg, frame; public int w; }
        static readonly Dictionary<string, Chrome> chromeCache = new Dictionary<string, Chrome>();
        static readonly List<string> chromeOrder = new List<string>();
        static Chrome GetChrome(PortraitOpts o)
        {
            string k = o.color + "|" + o.px + "|" + o.lod + "|" + (o.frame ? 1 : 0);
            lock (chromeCache) { Chrome c; if (chromeCache.TryGetValue(k, out c)) return c; }
            var sv = ChromeSvgs(o);
            var ch = new Chrome { w = sv.w, bg = new Raster(o.px, o.px) };
            PaintSvgDoc(ch.bg, sv.bg, o.px);
            if (sv.frame != null) { ch.frame = new Raster(o.px, o.px); PaintSvgDoc(ch.frame, sv.frame, o.px); }
            lock (chromeCache)
            {
                if (!chromeCache.ContainsKey(k)) { chromeCache[k] = ch; chromeOrder.Add(k); }
                if (chromeOrder.Count > 64) { chromeCache.Remove(chromeOrder[0]); chromeOrder.RemoveAt(0); }
            }
            return ch;
        }
        // 把一张头像画到 ctx（同 JS 的 paintPortrait）
        static void PaintPortrait(Raster ctx, JObj sp, PortraitOpts o, Chrome ch)
        {
            var p = BuildParts(sp, o);
            double k = o.px / 256.0;
            if (ch.bg != null) ctx.DrawImage(ch.bg);
            ctx.Save();
            ctx.Scale(k, k);
            ctx.BeginPath();
            ctx.Rect(p.w, p.w, 256 - 2 * p.w, 256 - 2 * p.w);
            ctx.Clip();
            PaintMarkup(ctx, p.fig, new Defs(string.Join("", p.S.defs.ToArray())));
            ctx.Restore();
            if (ch.frame != null) ctx.DrawImage(ch.frame);
        }

        // ---------------------------------------------------------------- 纯 C# 接口（后台线程可用）
        public static readonly Stats stats = new Stats();
        public sealed class Stats { public int built, rastered; public double totalMs, maxMs; }
        public static PortraitOpts Opts(int size = 96, string color = null, string mood = "neutral", bool frame = true, bool flip = false, double scale = 1)
        {
            return PortraitOpts.Make(size, color, mood, frame, flip, scale);
        }
        // SVG 源码（与网页版 SG.Portrait.svg 逐字相同，除裁剪 / 渐变 id 的流水号外）
        public static string SvgSource(PortraitGen gen, PortraitOpts o)
        {
            var sp = Spec(gen);
            return BuildSvg(sp, o);
        }
        // 栅格化一张头像（px×px，预乘 RGBA）
        public static Raster Render(PortraitGen gen, PortraitOpts o)
        {
            var t0 = DateTime.UtcNow;
            var sp = Spec(gen);
            var ch = GetChrome(o);
            var r = new Raster(o.px, o.px);
            PaintPortrait(r, sp, o, ch);
            double ms = (DateTime.UtcNow - t0).TotalMilliseconds;
            lock (stats) { stats.built++; stats.rastered++; stats.totalMs += ms; if (ms > stats.maxMs) stats.maxMs = ms; }
            return r;
        }
        // 清除缓存（PortraitDataStore.Add 时自动调用）；name 给出时只清该人
        public static void ClearCache(string name = null)
        {
            lock (specCache) specCache.Clear();
            Interlocked.Increment(ref dataVersion);
#if !SANGUO_HEADLESS
            ClearTextures(name);
#endif
        }
        // 按姓名找武将（当前游戏优先，其次剧本）；找不到为 null
        public static PortraitGen Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var g = FindGen(name);
            return g.war != null ? g : (PortraitDataStore.Get(name) != null ? g : null);
        }

#if !SANGUO_HEADLESS
        // ======================================================== Unity 接口 ==
        // General → PortraitGen（同 JS 的 gen：name / war / intel / pol，及 culture / born / sex）。
        // General 上有 culture（string）、born（int / int? / float）、sex（"m" / "f"）或 female（bool）字段 / 属性时
        // 自动读取（反射，只查一次）；也可整体替换此委托。
        public static Func<General, PortraitGen> Describe = DescribeDefault;
        static System.Reflection.MemberInfo mCulture, mBorn, mSex, mFemale;
        static bool reflected;
        static object Member(object o, System.Reflection.MemberInfo m)
        {
            var f = m as System.Reflection.FieldInfo;
            if (f != null) return f.GetValue(o);
            var p = m as System.Reflection.PropertyInfo;
            return p != null ? p.GetValue(o, null) : null;
        }
        static System.Reflection.MemberInfo Find(Type t, string name)
        {
            const System.Reflection.BindingFlags BF = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            return (System.Reflection.MemberInfo)t.GetField(name, BF) ?? t.GetProperty(name, BF);
        }
        public static PortraitGen DescribeDefault(General g)
        {
            if (!reflected)
            {
                var t = typeof(General);
                mCulture = Find(t, "culture"); mBorn = Find(t, "born"); mSex = Find(t, "sex"); mFemale = Find(t, "female");
                reflected = true;
            }
            var pg = new PortraitGen { name = g.name, war = g.war, intel = g.intel, pol = g.pol };
            if (mCulture != null) pg.culture = Member(g, mCulture) as string;
            if (mBorn != null)
            {
                object v = Member(g, mBorn);
                double b = v is int ? (int)v : v is float ? (float)v : v is double ? (double)v : v is long ? (long)v : 0;
                if (b > 0) pg.born = b;
            }
            if (mSex != null) pg.sex = Member(g, mSex) as string;
            if (pg.sex == null && mFemale != null && Member(g, mFemale) is bool && (bool)Member(g, mFemale)) pg.sex = "f";
            return pg;
        }
        // 无 Unity 线程（WebGL）时在主线程按时间片栅格化
        public static bool? UseThreadsOverride;
        static bool UseThreads { get { return UseThreadsOverride ?? Application.platform != RuntimePlatform.WebGLPlayer; } }
        public const int TextureCacheMax = 600;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitHooks()
        {
            Year = () => GameState.Current != null && GameState.Current.year > 0 ? GameState.Current.year : ScenarioData.StartYear;
            FindGenHook = name =>
            {
                var gs = GameState.Current;
                if (gs == null) return null;
                foreach (var g in gs.generals) if (g.name == name) return Describe(g);
                return null;
            };
        }
        static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant(); }
        public static string MoodName(PortraitMood m) { return m == PortraitMood.Angry ? "angry" : m == PortraitMood.Hurt ? "hurt" : m == PortraitMood.Win ? "win" : "neutral"; }
        public static Color FactionColorOf(General g)
        {
            var gs = GameState.Current;
            if (g != null && gs != null && g.faction >= 0 && g.faction < gs.factions.Count) return gs.factions[g.faction].Col;
            return new Color32(0x8a, 0x8a, 0x92, 255);
        }

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly LinkedList<string> texOrder = new LinkedList<string>();
        static readonly Dictionary<Texture2D, UnityEngine.Sprite> spriteCache = new Dictionary<Texture2D, UnityEngine.Sprite>();
        static void Remember(string key, Texture2D t)
        {
            Texture2D old;
            if (texCache.TryGetValue(key, out old) && old != t) { texOrder.Remove(key); DestroyTex(old); }
            texCache[key] = t; texOrder.AddLast(key);
            while (texCache.Count > TextureCacheMax)
            {
                string k = texOrder.First.Value; texOrder.RemoveFirst();
                Texture2D d; if (texCache.TryGetValue(k, out d)) { texCache.Remove(k); DestroyTex(d); }
            }
        }
        static void DestroyTex(Texture2D t)
        {
            UnityEngine.Sprite s;
            if (spriteCache.TryGetValue(t, out s)) { spriteCache.Remove(t); if (s != null) UnityEngine.Object.Destroy(s); }
            if (t != null) UnityEngine.Object.Destroy(t);
        }
        static void ClearTextures(string name)
        {
            var keys = new List<string>(texCache.Keys);
            foreach (var k in keys)
            {
                if (name != null && !k.StartsWith(name + "|", StringComparison.Ordinal)) continue;
                DestroyTex(texCache[k]); texCache.Remove(k); texOrder.Remove(k);
            }
        }
        static Texture2D Upload(Raster r, string key)
        {
            var t = r.ToTexture();
            t.name = "Portrait " + key;
            t.hideFlags = HideFlags.DontSave;
            return t;
        }
        // 头像纹理（缓存）。size 为像素边长；factionColor 为边框与背景的势力色
        public static Texture2D Texture(General g, int size, Color factionColor, PortraitMood mood = PortraitMood.Neutral, bool frame = true, bool flip = false)
        {
            return Texture(Describe(g), PortraitOpts.Make(size, Hex(factionColor), MoodName(mood), frame, flip));
        }
        public static Texture2D Texture(General g, int size, PortraitMood mood = PortraitMood.Neutral) { return Texture(g, size, FactionColorOf(g), mood); }
        public static Texture2D Texture(PortraitGen gen, PortraitOpts o)
        {
            var sp = Spec(gen);
            string key = KeyOf(sp, o);
            Texture2D t;
            if (texCache.TryGetValue(key, out t) && t != null) return t;
            t = Upload(Render(gen, o), key);
            Remember(key, t);
            return t;
        }
        // 已有纹理时返回，否则 null（不触发生成）
        public static Texture2D Cached(General g, int size, Color factionColor, PortraitMood mood = PortraitMood.Neutral, bool frame = true, bool flip = false)
        {
            var o = PortraitOpts.Make(size, Hex(factionColor), MoodName(mood), frame, flip);
            Texture2D t;
            return texCache.TryGetValue(KeyOf(Spec(Describe(g)), o), out t) ? t : null;
        }
        // UGUI Image 用的 Sprite（与纹理同一缓存）
        public static UnityEngine.Sprite Sprite(General g, int size, Color factionColor, PortraitMood mood = PortraitMood.Neutral, bool frame = true, bool flip = false)
        {
            return SpriteOf(Texture(g, size, factionColor, mood, frame, flip));
        }
        public static UnityEngine.Sprite Sprite(PortraitGen gen, PortraitOpts o) { return SpriteOf(Texture(gen, o)); }
        public static UnityEngine.Sprite SpriteOf(Texture2D t)
        {
            UnityEngine.Sprite s;
            if (spriteCache.TryGetValue(t, out s) && s != null) return s;
            s = UnityEngine.Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            s.name = t.name;
            spriteCache[t] = s;
            return s;
        }
        // 预生成（协程）：后台线程栅格化，主线程每帧按 budgetMs 上传纹理；无线程平台在主线程按时间片栅格化
        public static IEnumerator PreloadAsync(IList<General> gens, int size, Func<General, Color> colorOf = null, PortraitMood mood = PortraitMood.Neutral, float budgetMs = 6f)
        {
            var jobs = new List<KeyValuePair<PortraitGen, PortraitOpts>>();
            foreach (var g in gens)
            {
                if (g == null) continue;
                jobs.Add(new KeyValuePair<PortraitGen, PortraitOpts>(Describe(g), PortraitOpts.Make(size, Hex(colorOf != null ? colorOf(g) : FactionColorOf(g)), MoodName(mood))));
            }
            return PreloadAsync(jobs, budgetMs);
        }
        // 预生成当前游戏全部在世武将（势力色边框），sizes 为像素边长列表（同 JS 的 preloadState）
        public static IEnumerator PreloadState(GameState state = null, int[] sizes = null, float budgetMs = 6f)
        {
            state = state ?? GameState.Current;
            if (state == null) yield break;
            var jobs = new List<KeyValuePair<PortraitGen, PortraitOpts>>();
            foreach (int size in sizes ?? new[] { 44, 96 })
                foreach (var g in state.generals)
                    if (!g.dead) jobs.Add(new KeyValuePair<PortraitGen, PortraitOpts>(Describe(g), PortraitOpts.Make(size, Hex(FactionColorOf(g)))));
            yield return PreloadAsync(jobs, budgetMs);
        }
        public static IEnumerator PreloadAsync(IList<KeyValuePair<PortraitGen, PortraitOpts>> jobs, float budgetMs = 6f)
        {
            int ver = dataVersion;
            var todo = new List<KeyValuePair<string, KeyValuePair<PortraitGen, PortraitOpts>>>();
            var seen = new HashSet<string>();
            foreach (var j in jobs)
            {
                string key = KeyOf(Spec(j.Key), j.Value);
                if (texCache.ContainsKey(key) || !seen.Add(key)) continue;
                todo.Add(new KeyValuePair<string, KeyValuePair<PortraitGen, PortraitOpts>>(key, j));
            }
            if (todo.Count == 0) yield break;
            if (!UseThreads)
            {
                foreach (var t in todo)
                {
                    float t0 = Time.realtimeSinceStartup;
                    if (!texCache.ContainsKey(t.Key)) Remember(t.Key, Upload(Render(t.Value.Key, t.Value.Value), t.Key));
                    if ((Time.realtimeSinceStartup - t0) * 1000f > budgetMs * 0.5f) yield return null;
                }
                yield break;
            }
            // 后台线程：固定数量的工作线程依次取任务
            var done = new Queue<KeyValuePair<string, Raster>>();
            int next = -1, finished = 0;
            int workers = Math.Max(1, Math.Min(4, SystemInfo.processorCount - 1));
            for (int w = 0; w < workers; w++)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= todo.Count) break;
                        Raster r = null;
                        try { r = Render(todo[i].Value.Key, todo[i].Value.Value); } catch (Exception e) { Debug.LogWarning("portrait " + e); }
                        lock (done) done.Enqueue(new KeyValuePair<string, Raster>(todo[i].Key, r));
                    }
                });
            }
            while (finished < todo.Count)
            {
                float t0 = Time.realtimeSinceStartup;
                while (true)
                {
                    KeyValuePair<string, Raster> it;
                    lock (done) { if (done.Count == 0) break; it = done.Dequeue(); }
                    finished++;
                    if (it.Value != null && ver == dataVersion && !texCache.ContainsKey(it.Key)) Remember(it.Key, Upload(it.Value, it.Key));
                    if ((Time.realtimeSinceStartup - t0) * 1000f > budgetMs) break;
                }
                if (finished < todo.Count) yield return null;
            }
        }
#endif
    }
}
