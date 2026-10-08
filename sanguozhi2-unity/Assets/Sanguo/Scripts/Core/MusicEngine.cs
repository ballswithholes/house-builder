// 背景音乐引擎（第二版）· 移植自网页版 js/music.js（记谱解析、编曲时间线、前瞻调度、采样缓存、混音总线）
// 网页版用 WebAudio 节点图实时合成；这里用一个小型的 C# DSP 库按 WebAudio（Chromium 实现）的语义逐块渲染：
//   AudioParam 时间线（set / 线性 / 指数 / setTarget / cancel）、BiquadFilter（LP/HP 的 Q 按 dB）、
//   PeriodicWave 振荡器（归一化、按音高裁掉超过奈奎斯特的分音）、StereoPanner（等功率）、
//   ConvolverNode（同一个程序生成的脉冲响应，两级分区 FFT 卷积）、DynamicsCompressor（Chromium 内核逐行移植）。
// 本文件与 MusicInstruments.cs、Data/Generated/MusicScores.g.cs 不依赖 UnityEngine，可在 Tests~/audio 下无头编译测试；
// 播放（后台线程渲染 → 流式 AudioClip）在 Core/Sfx.cs。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;

namespace Sanguo.Audio
{
    // ================================================================ 工具 ==
    // 有序对象（乐谱 JSON 的对象；键保持 JS 的属性顺序，与 for-in 一致）
    public sealed class MObj
    {
        public readonly List<string> Keys = new List<string>();
        readonly Dictionary<string, object> map = new Dictionary<string, object>();
        public object this[string k]
        {
            get { object v; return k != null && map.TryGetValue(k, out v) ? v : null; }
            set { if (!map.ContainsKey(k)) Keys.Add(k); map[k] = value; }
        }
        public bool Has(string k) { return map.ContainsKey(k); }
        public void Remove(string k) { if (map.Remove(k)) Keys.Remove(k); }
        public MObj Clone() { var o = new MObj(); foreach (var k in Keys) o[k] = map[k]; return o; }
        public int Count { get { return Keys.Count; } }
        public double Num(string k, double def) { object v; return map.TryGetValue(k, out v) && v is double ? (double)v : def; }
        public string Str(string k) { return this[k] as string; }
        public MObj Obj(string k) { return this[k] as MObj; }
        public List<object> Arr(string k) { return this[k] as List<object>; }
    }

    // 极简 JSON 解析（只用于导出工具生成的规范 JSON）
    public static class MJson
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++; }
        static object Val(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                i++; var o = new MObj(); Ws(s, ref i);
                if (s[i] == '}') { i++; return o; }
                for (;;)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++;   // ':'
                    o[k] = Val(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return o;
                }
            }
            if (c == '[')
            {
                i++; var a = new List<object>(); Ws(s, ref i);
                if (s[i] == ']') { i++; return a; }
                for (;;)
                {
                    a.Add(Val(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return a;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }

    public static class MU
    {
        public const double TAU = Math.PI * 2;
        public static readonly double NaN = double.NaN;
        // FNV-1a 32（UTF-16 码元）→ 有符号 32 位（同 JS hash）
        public static int Hash(string s)
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h = unchecked(h * 16777619); }
            return unchecked((int)h);
        }
        public static double Mtof(double m) { return 440 * Math.Pow(2, (m - 69) / 12); }
        public static double JsRound(double x) { return Math.Floor(x + 0.5); }
        // JS：x === undefined ? d : x（NaN 表示 undefined）
        public static double Def(double x, double d) { return double.IsNaN(x) ? d : x; }
        // JS：x || d
        public static double Or(double x, double d) { return double.IsNaN(x) || x == 0 ? d : x; }
        public static bool Has(double x) { return !double.IsNaN(x); }
        // (Math.round(p * 100) / 100).toFixed(2)
        public static string Fixed2(double p) { return (Math.Floor(p * 100 + 0.5) / 100).ToString("F2", CultureInfo.InvariantCulture); }
        // String(number)（整数不带小数点）
        public static string JsNum(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
        static readonly int[] NOTE_SEMI = { 9, 11, 0, 2, 4, 5, 7 };   // A B C D E F G
        public static double KeyToMidi(string key, double oct)
        {
            if (string.IsNullOrEmpty(key)) key = "C";
            if (key.Length < 1 || key.Length > 2 || key[0] < 'A' || key[0] > 'G' || (key.Length == 2 && key[1] != '#' && key[1] != 'b')) return 60;
            int s = NOTE_SEMI[key[0] - 'A'] + (key.Length == 2 ? (key[1] == '#' ? 1 : -1) : 0);
            return 12 * (oct + 1) + s;
        }
        // 2^(cents/1200)：小音分用多项式（颤音 / 漂移每个采样都要算）
        public static double Cents(double c)
        {
            if (c > -240 && c < 240)
            {
                double y = c * 0.00057762265046662105;   // ln2 / 1200
                return 1 + y * (1 + y * (0.5 + y * (1.0 / 6 + y * (1.0 / 24))));
            }
            return Math.Exp(c * 0.00057762265046662105);
        }
    }

    // 装饰性随机（与 JS rng 逐位一致）
    public sealed class Rng
    {
        int a;
        public Rng(int seed) { a = seed ^ 0x5bd1e995; }
        public double Next()
        {
            unchecked
            {
                a = a + 0x6D2B79F5;
                int t = (a ^ (int)((uint)a >> 15)) * (1 | a);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }
    }

    // ===================================================== AudioParam 时间线 ==
    // 语义同 WebAudio：事件按时间排序；线性 / 指数斜坡从前一事件的值开始；setTarget 从当时的值指数逼近；
    // 同类型同时间的事件替换（Chromium）；早于已渲染时间插入的事件按“现在”处理。
    public sealed class Param
    {
        const byte SET = 0, LIN = 1, EXP = 2, TGT = 3;
        struct Ev { public byte type; public double t, v, tc, bas, ins; }
        Ev[] ev = new Ev[4];
        int n, cur = -1;
        public double def;
        double done;
        public Param(double v) { def = v; }
        public bool Constant { get { return n == 0 || (cur == n - 1 && ev[cur].type != TGT); } }

        void Insert(byte type, double v, double t, double tc)
        {
            if (t < done) t = done;
            int i = n;
            while (i > 0 && ev[i - 1].t > t) i--;
            for (int j = i - 1; j >= 0 && ev[j].t == t; j--)
                if (ev[j].type == type) { ev[j].v = v; ev[j].tc = tc; if (j <= cur) cur = j - 1; return; }
            if (n == ev.Length) Array.Resize(ref ev, n * 2);
            if (i < n) Array.Copy(ev, i, ev, i + 1, n - i);
            ev[i] = new Ev { type = type, v = v, t = t, tc = tc, ins = done };
            n++;
            if (i <= cur) cur = i - 1;
        }
        public void SetValueAtTime(double v, double t) { Insert(SET, v, t, 0); }
        public void LinearRampToValueAtTime(double v, double t) { Insert(LIN, v, t, 0); }
        public void ExponentialRampToValueAtTime(double v, double t) { Insert(EXP, v, t, 0); }
        public void SetTargetAtTime(double v, double t, double tc) { if (tc <= 0) Insert(SET, v, t, 0); else Insert(TGT, v, t, tc); }
        public void CancelScheduledValues(double t)
        {
            int k = n;
            while (k > 0 && ev[k - 1].t >= t) k--;
            n = k;
            if (cur >= n) cur = n - 1;
        }

        void Enter(int k)
        {
            if (ev[k].type != TGT) { ev[k].bas = ev[k].v; return; }
            if (k == 0) { ev[k].bas = def; return; }
            var p = ev[k - 1];
            ev[k].bas = p.type == TGT ? p.v + (p.bas - p.v) * Math.Exp(-(ev[k].t - p.t) / p.tc) : p.bas;
        }
        void Advance(double t) { while (cur + 1 < n && ev[cur + 1].t <= t) { cur++; Enter(cur); } }

        // 当前段在 t 时的值（调用前已 Advance）
        double At(double t)
        {
            double a, ta;
            if (cur < 0)
            {
                if (n == 0 || (ev[0].type != LIN && ev[0].type != EXP)) return def;
                a = def; ta = ev[0].ins;
            }
            else
            {
                var e = ev[cur];
                a = e.bas; ta = e.t;
                if (cur + 1 >= n || (ev[cur + 1].type != LIN && ev[cur + 1].type != EXP))
                    return e.type == TGT ? e.v + (a - e.v) * Math.Exp(-(t - e.t) / e.tc) : a;
            }
            var nx = ev[cur + 1];
            double span = nx.t - ta;
            if (span <= 0) return nx.v;
            double f = (t - ta) / span;
            if (nx.type == LIN) return a + (nx.v - a) * f;
            if (a == 0 || a * nx.v < 0) return a;
            return a * Math.Pow(nx.v / a, f);
        }

        public double ValueAt(double t) { Advance(t); return At(t); }

        // 按帧填入 o[0..count)，第 i 帧的时间 = t0 + i·dt
        public void Fill(float[] o, int count, double t0, double dt)
        {
            int i = 0;
            while (i < count)
            {
                double t = t0 + i * dt;
                Advance(t);
                int end = count;
                if (cur + 1 < n)
                {
                    int e2 = (int)Math.Ceiling((ev[cur + 1].t - t0) / dt);
                    if (e2 < end) end = e2;
                    if (end <= i) end = i + 1;
                }
                Seg(o, i, end, t0, dt);
                i = end;
            }
            done = t0 + count * dt;
            if (cur > 16) { Array.Copy(ev, cur, ev, 0, n - cur); n -= cur; cur = 0; }
        }

        void Seg(float[] o, int i, int end, double t0, double dt)
        {
            bool ramp = cur + 1 < n && (ev[cur + 1].type == LIN || ev[cur + 1].type == EXP);
            if (!ramp)
            {
                if (cur >= 0 && ev[cur].type == TGT)
                {
                    var e = ev[cur];
                    double d = (e.bas - e.v) * Math.Exp(-(t0 + i * dt - e.t) / e.tc), k = Math.Exp(-dt / e.tc);
                    for (int j = i; j < end; j++) { o[j] = (float)(e.v + d); d *= k; }
                    return;
                }
                float c = (float)(cur >= 0 ? ev[cur].bas : def);
                for (int j = i; j < end; j++) o[j] = c;
                return;
            }
            double a, ta;
            if (cur < 0) { a = def; ta = ev[0].ins; } else { a = ev[cur].bas; ta = ev[cur].t; }
            var nx = ev[cur + 1];
            double span = nx.t - ta;
            if (span <= 0) { for (int j = i; j < end; j++) o[j] = (float)nx.v; return; }
            if (nx.type == LIN)
            {
                double k = (nx.v - a) / span;
                for (int j = i; j < end; j++) o[j] = (float)(a + k * (t0 + j * dt - ta));
                return;
            }
            if (a == 0 || a * nx.v < 0) { for (int j = i; j < end; j++) o[j] = (float)a; return; }
            double ratio = nx.v / a;
            double v = a * Math.Pow(ratio, (t0 + i * dt - ta) / span), step = Math.Pow(ratio, dt / span);
            for (int j = i; j < end; j++) { o[j] = (float)v; v *= step; }
        }
    }

    // 按时间顺序追加自动化，保证时间严格递增（同 music-inst.js 的 Auto）
    public sealed class Auto
    {
        readonly Param p; double t = -1;
        public Auto(Param p) { this.p = p; }
        double T(double tt) { return Math.Max(Math.Max(tt, t + 1e-4), 0); }
        public void Set(double v, double tt) { tt = T(tt); p.SetValueAtTime(v, tt); t = tt; }
        public void Lin(double v, double tt) { tt = T(tt); p.LinearRampToValueAtTime(v, tt); t = tt; }
        public void Exp(double v, double tt) { tt = T(tt); p.ExponentialRampToValueAtTime(Math.Max(1e-4, v), tt); t = tt; }
    }

    // ========================================================= 双二阶滤波 ==
    // WebAudio BiquadFilterNode（Chromium biquad.cc）：lowpass / highpass 的 Q 以 dB 计；bandpass / peaking 的 Q 为线性；
    // 搁架滤波 S = 1。状态与系数用双精度。
    public sealed class Biquad
    {
        public const int LP = 0, HP = 1, BP = 2, PK = 3, LS = 4, HS = 5;
        readonly int type; readonly double q, gainDb, nyq;
        double b0 = 1, b1, b2, a1, a2, x1, x2, y1, y2;
        public double freq = -1;
        public Biquad(int type, double f, double q, double gainDb, double sr)
        {
            this.type = type; this.q = q; this.gainDb = gainDb; nyq = sr / 2;
            Set(f);
        }
        public static int Type(string s)
        {
            switch (s)
            {
                case "lowpass": return LP; case "highpass": return HP; case "bandpass": return BP;
                case "peaking": return PK; case "lowshelf": return LS; case "highshelf": return HS;
            }
            return PK;
        }
        void N(double nb0, double nb1, double nb2, double na0, double na1, double na2)
        {
            double k = 1.0 / na0;
            b0 = nb0 * k; b1 = nb1 * k; b2 = nb2 * k; a1 = na1 * k; a2 = na2 * k;
        }
        public void Set(double f)
        {
            freq = f;
            double fr = f / nyq;
            switch (type)
            {
                case LP:
                case HP:
                    {
                        fr = Math.Min(1, Math.Max(0, fr));
                        if (fr == 1) { if (type == LP) N(1, 0, 0, 1, 0, 0); else N(0, 0, 0, 1, 0, 0); return; }
                        if (fr <= 0) { if (type == LP) N(0, 0, 0, 1, 0, 0); else N(1, 0, 0, 1, 0, 0); return; }
                        double res = Math.Pow(10, q / 20), th = Math.PI * fr, al = Math.Sin(th) / (2 * res), cw = Math.Cos(th);
                        if (type == LP) { double be = (1 - cw) / 2; N(be, 2 * be, be, 1 + al, -2 * cw, 1 - al); }
                        else { double be = (1 + cw) / 2; N(be, -2 * be, be, 1 + al, -2 * cw, 1 - al); }
                        return;
                    }
                case BP:
                    {
                        fr = Math.Max(0, fr); double Q = Math.Max(0, q);
                        if (fr > 0 && fr < 1)
                        {
                            if (Q > 0) { double w = Math.PI * fr, al = Math.Sin(w) / (2 * Q), k = Math.Cos(w); N(al, 0, -al, 1 + al, -2 * k, 1 - al); }
                            else N(1, 0, 0, 1, 0, 0);
                        }
                        else N(0, 0, 0, 1, 0, 0);
                        return;
                    }
                case PK:
                    {
                        fr = Math.Min(1, Math.Max(0, fr)); double Q = Math.Max(0, q), A = Math.Pow(10, gainDb / 40);
                        if (fr > 0 && fr < 1)
                        {
                            if (Q > 0) { double w = Math.PI * fr, al = Math.Sin(w) / (2 * Q), k = Math.Cos(w); N(1 + al * A, -2 * k, 1 - al * A, 1 + al / A, -2 * k, 1 - al / A); }
                            else N(A * A, 0, 0, 1, 0, 0);
                        }
                        else N(1, 0, 0, 1, 0, 0);
                        return;
                    }
                default:
                    {
                        fr = Math.Min(1, Math.Max(0, fr)); double A = Math.Pow(10, gainDb / 40);
                        bool low = type == LS;
                        if (fr == 1) { if (low) N(A * A, 0, 0, 1, 0, 0); else N(1, 0, 0, 1, 0, 0); return; }
                        if (fr <= 0) { if (low) N(1, 0, 0, 1, 0, 0); else N(A * A, 0, 0, 1, 0, 0); return; }
                        double w = Math.PI * fr, al = 0.5 * Math.Sin(w) * Math.Sqrt(2), k = Math.Cos(w), k2 = 2 * Math.Sqrt(A) * al, ap = A + 1, am = A - 1;
                        if (low) N(A * (ap - am * k + k2), 2 * A * (am - ap * k), A * (ap - am * k - k2), ap + am * k + k2, -2 * (am + ap * k), ap + am * k - k2);
                        else N(A * (ap + am * k + k2), -2 * A * (am + ap * k), A * (ap + am * k - k2), ap - am * k + k2, 2 * (am - ap * k), ap - am * k - k2);
                        return;
                    }
            }
        }
        public double Tick(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
        public void Run(float[] d, int count)
        {
            double _x1 = x1, _x2 = x2, _y1 = y1, _y2 = y2;
            for (int i = 0; i < count; i++)
            {
                double x = d[i];
                double y = b0 * x + b1 * _x1 + b2 * _x2 - a1 * _y1 - a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                d[i] = (float)y;
            }
            if (Math.Abs(_y1) < 1e-25) _y1 = 0;
            if (Math.Abs(_y2) < 1e-25) _y2 = 0;
            x1 = _x1; x2 = _x2; y1 = _y1; y2 = _y2;
        }
    }

    // ===================================================== PeriodicWave 振荡器 ==
    // Chromium periodic_wave.cc：表长 4096（采样率 ≤ 88.2k）、36 个音域表（每表少 1/3 个八度的分音），
    // 以分音最全的表的峰值归一化；振荡器在相邻两表之间插值，表内线性插值。
    public sealed class PWave
    {
        public const int Ranges = 36;
        public readonly int size;
        readonly double[] re, im;
        readonly int ncomp;
        readonly float[][] tables = new float[Ranges][];
        double norm = -1;
        public readonly double lowestF;
        PWave(double[] re, double[] im, int ncomp, double sr)
        {
            size = sr <= 24000 ? 2048 : sr <= 88200 ? 4096 : 16384;
            this.re = re; this.im = im;
            this.ncomp = Math.Min(ncomp, size / 2);
            lowestF = (sr / 2) / (size / 2);
        }
        int Partials(int r)
        {
            float cents = r * (1200f / 3);
            float scale = (float)Math.Pow(2, -cents / 1200);
            return (int)(scale * (size / 2));
        }
        public float[] Table(int r)
        {
            var t = tables[r];
            if (t != null) return t;
            lock (this) { if (tables[r] == null) Build(r); return tables[r]; }
        }
        void Build(int r)
        {
            if (norm < 0 && r != 0) Build(0);
            int kmax = Math.Min(ncomp, Partials(r) + 1) - 1;
            int N = size;
            var d = new double[N];
            {
                var zr = new double[N]; var zi = new double[N];
                for (int k = 1; k <= kmax; k++)
                {
                    double a = k < re.Length ? re[k] : 0, b = k < im.Length ? im[k] : 0;
                    zr[k] = a; zi[k] = -b; zr[N - k] = a; zi[N - k] = b;
                }
                FFT.Get(N).Run(zr, zi, true);
                for (int i = 0; i < N; i++) d[i] = zr[i] * 0.5;
            }
            if (norm < 0)
            {
                double m = 0;
                for (int i = 0; i < N; i++) m = Math.Max(m, Math.Abs(d[i]));
                norm = m > 0 ? 1 / m : 0.5;
            }
            var t = new float[N + 1];
            for (int i = 0; i < N; i++) t[i] = (float)(d[i] * norm);
            t[N] = t[0];
            tables[r] = t;
        }
        // 预先生成 f0..f1 Hz 需要的音域表（后台准备线程调用，避免渲染线程第一次用到时现算）
        public void Warm(double f0, double f1)
        {
            float[] a, b; double x;
            for (double f = Math.Max(1, f0); ; f *= 1.18) { Select(Math.Min(f, f1), out a, out b, out x); if (f >= f1) break; }
        }
        public void Select(double f, out float[] lower, out float[] higher, out double interp)
        {
            f = Math.Abs(f);
            double ratio = f > 0 ? f / lowestF : 0.5;
            double cents = Math.Log(ratio, 2) * 1200;
            double pr = 1 + cents / 400;
            if (pr < 0) pr = 0;
            if (pr > Ranges - 1) pr = Ranges - 1;
            int r1 = (int)pr, r2 = r1 < Ranges - 1 ? r1 + 1 : r1;
            lower = Table(r2); higher = Table(r1); interp = pr - r1;
        }

        static readonly Dictionary<string, PWave> cache = new Dictionary<string, PWave>();
        // 自定义波形：harm(k) 为第 k 次谐波（正弦项）振幅，k = 1..nh
        public static PWave Custom(string name, Func<int, double> harm, int nh, double sr)
        {
            string key = name + "@" + sr;
            lock (cache)
            {
                PWave w;
                if (cache.TryGetValue(key, out w)) return w;
                int N = nh > 0 ? nh : 48;
                var re = new double[N + 1]; var im = new double[N + 1];
                for (int k = 1; k <= N; k++) im[k] = harm(k);
                w = new PWave(re, im, N + 1, sr);
                cache[key] = w;
                return w;
            }
        }
        // 内置波形 sine / sawtooth / triangle / square（Chromium GenerateBasicWaveform）
        public static PWave Basic(string type, double sr)
        {
            string key = "#" + type + "@" + sr;
            lock (cache)
            {
                PWave w;
                if (cache.TryGetValue(key, out w)) return w;
                int size = sr <= 24000 ? 2048 : sr <= 88200 ? 4096 : 16384, half = size / 2;
                var re = new double[half]; var im = new double[half];
                for (int n = 1; n < half; n++)
                {
                    double pf = 2.0 / (n * Math.PI), b;
                    switch (type)
                    {
                        case "sine": b = n == 1 ? 1 : 0; break;
                        case "square": b = (n & 1) == 1 ? 2 * pf : 0; break;
                        case "sawtooth": b = pf * ((n & 1) == 1 ? 1 : -1); break;
                        default: b = (n & 1) == 1 ? 8 / (Math.PI * Math.PI * n * n) * ((n & 3) == 1 ? 1 : -1) : 0; break;   // triangle
                    }
                    im[n] = b;
                }
                w = new PWave(re, im, half, sr);
                cache[key] = w;
                return w;
            }
        }
    }

    public sealed class Osc
    {
        readonly PWave w; readonly double scale; readonly int size;
        double phase, selF = -1, tf;
        float[] lo, hi;
        public Osc(PWave w, double sr) { this.w = w; size = w.size; scale = w.size / sr; }
        public double Next(double f)
        {
            if (f != selF && (selF <= 0 || Math.Abs(f - selF) > selF * 0.002)) { selF = f; w.Select(f, out lo, out hi, out tf); }
            int i0 = (int)phase; double fr = phase - i0;
            double sh = hi[i0] + (hi[i0 + 1] - hi[i0]) * fr;
            double s = tf == 0 ? sh : sh + ((lo[i0] + (lo[i0 + 1] - lo[i0]) * fr) - sh) * tf;
            phase += f * scale;
            if (phase >= size) phase -= size * Math.Floor(phase / size);
            else if (phase < 0) phase -= size * Math.Floor(phase / size);
            return s;
        }
    }

    // ================================================================ FFT ==
    public sealed class FFT
    {
        public readonly int n;
        readonly int[] rev; readonly double[] cs, sn;
        FFT(int n)
        {
            this.n = n;
            rev = new int[n];
            int bits = 0; while ((1 << bits) < n) bits++;
            for (int i = 0; i < n; i++) { int r = 0; for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b); rev[i] = r; }
            cs = new double[n / 2]; sn = new double[n / 2];
            for (int k = 0; k < n / 2; k++) { cs[k] = Math.Cos(MU.TAU * k / n); sn[k] = Math.Sin(MU.TAU * k / n); }
        }
        static readonly Dictionary<int, FFT> cache = new Dictionary<int, FFT>();
        public static FFT Get(int n) { lock (cache) { FFT f; if (!cache.TryGetValue(n, out f)) cache[n] = f = new FFT(n); return f; } }
        // 原地复数 FFT；inverse 为 e^{+i}，不做 1/n 缩放
        public void Run(double[] re, double[] im, bool inverse)
        {
            for (int i = 0; i < n; i++) { int j = rev[i]; if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; } }
            double sg = inverse ? 1 : -1;
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1, step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0; k < half; k++)
                    {
                        double wr = cs[k * step], wi = sg * sn[k * step];
                        int a = i + k, b = a + half;
                        double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    }
                }
            }
        }
    }

    // 均匀分区重叠保留卷积（立体声、左右各一条脉冲响应；L + iR 打包成一次复数 FFT）
    sealed class PartConv
    {
        readonly int B, N, P, bins;
        readonly FFT fft;
        readonly double[][] hLr, hLi, hRr, hRi, xLr, xLi, xRr, xRi;
        readonly double[] prevL, prevR, zr, zi, aLr, aLi, aRr, aRi;
        int head;
        public PartConv(float[] irL, float[] irR, int off, int len, int B)
        {
            this.B = B; N = 2 * B; bins = B + 1;
            P = Math.Max(1, (len + B - 1) / B);
            fft = FFT.Get(N);
            hLr = new double[P][]; hLi = new double[P][]; hRr = new double[P][]; hRi = new double[P][];
            xLr = new double[P][]; xLi = new double[P][]; xRr = new double[P][]; xRi = new double[P][];
            zr = new double[N]; zi = new double[N];
            for (int p = 0; p < P; p++)
            {
                Array.Clear(zr, 0, N); Array.Clear(zi, 0, N);
                for (int i = 0; i < B; i++)
                {
                    int k = off + p * B + i;
                    if (k < off + len && k < irL.Length) { zr[i] = irL[k]; zi[i] = irR[k]; }
                }
                fft.Run(zr, zi, false);
                hLr[p] = new double[bins]; hLi[p] = new double[bins]; hRr[p] = new double[bins]; hRi[p] = new double[bins];
                Split(hLr[p], hLi[p], hRr[p], hRi[p]);
                xLr[p] = new double[bins]; xLi[p] = new double[bins]; xRr[p] = new double[bins]; xRi[p] = new double[bins];
            }
            prevL = new double[B]; prevR = new double[B];
            aLr = new double[bins]; aLi = new double[bins]; aRr = new double[bins]; aRi = new double[bins];
        }
        void Split(double[] lr, double[] li, double[] rr, double[] ri)
        {
            for (int k = 0; k < bins; k++)
            {
                int m = (N - k) & (N - 1);
                lr[k] = (zr[k] + zr[m]) * 0.5; li[k] = (zi[k] - zi[m]) * 0.5;
                rr[k] = (zi[k] + zi[m]) * 0.5; ri[k] = -(zr[k] - zr[m]) * 0.5;
            }
        }
        // 输入 B 帧，输出 B 帧（写入 outL / outR）
        public void Process(float[] inL, float[] inR, int inOff, double[] outL, double[] outR)
        {
            for (int i = 0; i < B; i++)
            {
                zr[i] = prevL[i]; zi[i] = prevR[i];
                double l = inL[inOff + i], r = inR[inOff + i];
                zr[B + i] = l; zi[B + i] = r; prevL[i] = l; prevR[i] = r;
            }
            fft.Run(zr, zi, false);
            head = (head + 1) % P;
            Split(xLr[head], xLi[head], xRr[head], xRi[head]);
            Array.Clear(aLr, 0, bins); Array.Clear(aLi, 0, bins); Array.Clear(aRr, 0, bins); Array.Clear(aRi, 0, bins);
            for (int p = 0; p < P; p++)
            {
                int s = head - p; if (s < 0) s += P;
                double[] xr = xLr[s], xi = xLi[s], hr = hLr[p], hi = hLi[p];
                double[] yr = xRr[s], yi = xRi[s], gr = hRr[p], gi = hRi[p];
                for (int k = 0; k < bins; k++)
                {
                    aLr[k] += xr[k] * hr[k] - xi[k] * hi[k]; aLi[k] += xr[k] * hi[k] + xi[k] * hr[k];
                    aRr[k] += yr[k] * gr[k] - yi[k] * gi[k]; aRi[k] += yr[k] * gi[k] + yi[k] * gr[k];
                }
            }
            for (int k = 0; k < bins; k++) { zr[k] = aLr[k] - aRi[k]; zi[k] = aLi[k] + aRr[k]; }
            for (int k = bins; k < N; k++) { int m = N - k; zr[k] = aLr[m] + aRi[m]; zi[k] = -aLi[m] + aRr[m]; }
            fft.Run(zr, zi, true);
            double sc = 1.0 / N;
            for (int i = 0; i < B; i++) { outL[i] = zr[B + i] * sc; outR[i] = zi[B + i] * sc; }
        }
    }

    // ConvolverNode（normalize = false）：头部 8192 个采样按 1024 分区，其余按 8192 分区（零延迟）
    sealed class Reverb
    {
        public const int B = 1024, H = 8192;
        readonly PartConv headC, tailC;
        readonly float[] bigL = new float[H], bigR = new float[H];
        readonly double[] hl = new double[B], hr = new double[B], tailL = new double[H], tailR = new double[H];
        int pos;
        public Reverb(float[] irL, float[] irR)
        {
            int len = irL.Length;
            headC = new PartConv(irL, irR, 0, Math.Min(H, len), B);
            if (len > H) tailC = new PartConv(irL, irR, H, len - H, H);
        }
        public void Process(float[] inL, float[] inR, float[] outL, float[] outR)
        {
            headC.Process(inL, inR, 0, hl, hr);
            for (int i = 0; i < B; i++)
            {
                outL[i] = (float)(hl[i] + tailL[pos + i]); outR[i] = (float)(hr[i] + tailR[pos + i]);
                bigL[pos + i] = inL[i]; bigR[pos + i] = inR[i];
            }
            pos += B;
            if (pos == H)
            {
                pos = 0;
                if (tailC != null) tailC.Process(bigL, bigR, 0, tailL, tailR);
            }
        }
    }

    // ===================================================== 动态压缩（Chromium） ==
    // dynamics_compressor_kernel.cc 逐行移植：6 ms 前视延迟、软膝曲线、自适应释放、sin 弯曲、自动补偿增益（^0.6）
    sealed class Compressor
    {
        const float PiOverTwo = (float)(Math.PI / 2);
        const int MaxPre = 1024, Mask = 1023;
        readonly float linearThreshold, kneeThreshold, kneeThresholdDb, ykneeThresholdDb, k, slope, masterGain, attackFrames, satReleaseFrames;
        readonly float kA, kB, kC, kD, kE;
        float detectorAverage = 0, compressorGain = 1, maxAttackDiffDb = -1;
        readonly float[] preL = new float[MaxPre], preR = new float[MaxPre];
        int rd, wr;
        static float Db2Lin(float db) { return (float)Math.Pow(10, 0.05f * db); }
        static float Lin2Db(float x) { return (float)(20 * Math.Log10(x)); }
        float KneeCurve(float x, float kk)
        {
            if (x < linearThreshold) return x;
            return linearThreshold + (1 - (float)Math.Exp(-kk * (x - linearThreshold))) / kk;
        }
        float Saturate(float x, float kk)
        {
            if (x < kneeThreshold) return KneeCurve(x, kk);
            float xDb = Lin2Db(x);
            float yDb = ykneeThresholdDb + slope * (xDb - kneeThresholdDb);
            return Db2Lin(yDb);
        }
        float SlopeAt(float x, float kk)
        {
            if (x < linearThreshold) return 1;
            float x2 = x * 1.001f;
            float xDb = Lin2Db(x), x2Db = Lin2Db(x2), yDb = Lin2Db(KneeCurve(x, kk)), y2Db = Lin2Db(KneeCurve(x2, kk));
            return (y2Db - yDb) / (x2Db - xDb);
        }
        float KAtSlope(float desired, float dbThreshold, float dbKnee)
        {
            float xDb = dbThreshold + dbKnee, x = Db2Lin(xDb);
            float minK = 0.1f, maxK = 10000, kk = 5;
            for (int i = 0; i < 15; i++)
            {
                float s = SlopeAt(x, kk);
                if (s < desired) maxK = kk; else minK = kk;
                kk = (float)Math.Sqrt(minK * maxK);
            }
            return kk;
        }
        public Compressor(double sampleRate, float threshold, float knee, float ratio, float attack, float release)
        {
            float sr = (float)sampleRate;
            linearThreshold = Db2Lin(threshold);
            slope = 1 / ratio;
            kneeThreshold = 0; kneeThresholdDb = 0; ykneeThresholdDb = 0;
            k = KAtSlope(1 / ratio, threshold, knee);
            kneeThresholdDb = threshold + knee;
            kneeThreshold = Db2Lin(kneeThresholdDb);
            ykneeThresholdDb = Lin2Db(KneeCurve(kneeThreshold, k));
            float fullRangeGain = Saturate(1, k);
            float makeup = (float)Math.Pow(1 / fullRangeGain, 0.6f);
            masterGain = Db2Lin(0) * makeup;
            attackFrames = Math.Max(0.001f, attack) * sr;
            float releaseFrames = sr * release;
            satReleaseFrames = 0.0025f * sr;
            float y1 = releaseFrames * 0.09f, y2 = releaseFrames * 0.16f, y3 = releaseFrames * 0.42f, y4 = releaseFrames * 0.98f;
            kA = 0.9999999999999998f * y1 + 1.8432219684323923e-16f * y2 - 1.9373394351676423e-16f * y3 + 8.824516011816245e-18f * y4;
            kB = -1.5788320352845888f * y1 + 2.3305837032074286f * y2 - 0.9141194204840429f * y3 + 0.1623677525612032f * y4;
            kC = 0.5334142869106424f * y1 - 1.272736789213631f * y2 + 0.9258856042207512f * y3 - 0.18656310191776226f * y4;
            kD = 0.08783463138207234f * y1 - 0.1694162967925622f * y2 + 0.08588057951595272f * y3 - 0.00429891410546283f * y4;
            kE = -0.042416883008123074f * y1 + 0.1115693827987602f * y2 - 0.09764676325265872f * y3 + 0.028494263462021576f * y4;
            int pre = (int)(0.006f * sr);
            if (pre > MaxPre - 1) pre = MaxPre - 1;
            rd = 0; wr = pre;
        }
        // 原地处理；count 为 32 的倍数
        public void Process(float[] L, float[] R, int count)
        {
            int idx = 0;
            for (int div = 0; div < count / 32; div++)
            {
                if (float.IsNaN(detectorAverage) || float.IsInfinity(detectorAverage)) detectorAverage = 1;
                float desired = detectorAverage;
                float scaledDesired = (float)Math.Asin(desired) / PiOverTwo;
                float envelopeRate;
                bool releasing = scaledDesired > compressorGain;
                float diffDb = Lin2Db(compressorGain / scaledDesired);
                if (releasing)
                {
                    maxAttackDiffDb = -1;
                    if (float.IsNaN(diffDb) || float.IsInfinity(diffDb)) diffDb = -1;
                    float x = Math.Min(0, Math.Max(-12, diffDb));
                    x = 0.25f * (x + 12);
                    float x2 = x * x, x3 = x2 * x, x4 = x2 * x2;
                    float relFrames = kA + kB * x + kC * x2 + kD * x3 + kE * x4;
                    envelopeRate = Db2Lin(5 / relFrames);
                }
                else
                {
                    if (float.IsNaN(diffDb) || float.IsInfinity(diffDb)) diffDb = 1;
                    if (maxAttackDiffDb == -1 || maxAttackDiffDb < diffDb) maxAttackDiffDb = diffDb;
                    float eff = Math.Max(0.5f, maxAttackDiffDb);
                    float x = 0.25f / eff;
                    envelopeRate = 1 - (float)Math.Pow(x, 1 / attackFrames);
                }
                float det = detectorAverage, cg = compressorGain;
                for (int f = 0; f < 32; f++, idx++)
                {
                    float l = L[idx], r = R[idx];
                    preL[wr] = l; preR[wr] = r;
                    float al = l > 0 ? l : -l, ar = r > 0 ? r : -r;
                    float absIn = al > ar ? al : ar;
                    float shaped = Saturate(absIn, k);
                    float att = absIn <= 0.0001f ? 1 : shaped / absIn;
                    float attDb = Math.Max(2f, -Lin2Db(att));
                    float satRate = Db2Lin(attDb / satReleaseFrames) - 1;
                    float rate = att > det ? satRate : 1;
                    det += (att - det) * rate;
                    det = Math.Min(1f, det);
                    if (float.IsNaN(det) || float.IsInfinity(det)) det = 1;
                    if (envelopeRate < 1) cg += (scaledDesired - cg) * envelopeRate;
                    else { cg *= envelopeRate; cg = Math.Min(1f, cg); }
                    float total = masterGain * (float)Math.Sin(PiOverTwo * cg);
                    L[idx] = preL[rd] * total; R[idx] = preR[rd] * total;
                    rd = (rd + 1) & Mask; wr = (wr + 1) & Mask;
                }
                if (Math.Abs(det) < 1e-30f) det = 0;
                if (Math.Abs(cg) < 1e-30f) cg = 0;
                detectorAverage = det; compressorGain = cg;
            }
        }
    }

    // ================================================================ 乐谱 ==
    public sealed class Orn
    {
        public int vib, gliss, grace;
        public bool trem, slide, fall, acc, swell, tie, glissDown;
    }
    // 声部 / 鼓轨设定
    public sealed class Spec
    {
        public string inst, drone, note;
        public double oct, gain = double.NaN, pan = double.NaN, rev = double.NaN, grace = double.NaN, choke = double.NaN;
        public int art;
        public static Spec From(MObj o)
        {
            var s = new Spec();
            if (o == null) return s;
            s.inst = o.Str("inst"); s.drone = o.Str("drone"); s.note = o.Str("note");
            s.oct = o.Num("oct", 0);
            s.gain = o.Num("gain", double.NaN); s.pan = o.Num("pan", double.NaN); s.rev = o.Num("rev", double.NaN);
            s.grace = o.Num("grace", double.NaN); s.choke = o.Num("choke", double.NaN);
            s.art = (int)o.Num("art", 0);
            return s;
        }
    }
    public sealed class PNote { public double beat, dur, p, vel, gp = double.NaN, tp = double.NaN; public Orn orn; }
    public sealed class Item
    {
        public char type;   // 's' 采样 'y' 复音 'p' 连奏乐句 'd' 鼓
        public string voice, lane, inst;
        public double beat, dur, vel, gp = double.NaN, tp = double.NaN, p = double.NaN;
        public double[] ps, run;
        public Orn orn;
        public int art;
        public List<PNote> notes;
        public Item Clone() { return (Item)MemberwiseClone(); }
    }
    public sealed class Section
    {
        public string name;
        public double beats, bars, bpm, bpmTo, dur;
        public List<Item> items;
        bool ramp; double b0, r, k;
        public void MakeTime(double beats, double bpm0, double bpm1)
        {
            if (bpm1 == 0 || Math.Abs(bpm1 - bpm0) < 1e-6 || beats <= 0) { ramp = false; k = 60 / bpm0; dur = beats * k; return; }
            ramp = true; b0 = bpm0; r = (bpm1 - bpm0) / beats; dur = Time(beats);
        }
        public double Time(double b) { return ramp ? 60 / r * Math.Log((b0 + r * b) / b0) : b * k; }
    }
    public sealed class Compiled
    {
        public string key;
        public MObj piece;
        public Dictionary<string, Section> sections = new Dictionary<string, Section>();
        public List<string> form = new List<string>();
        public int loopFrom;
        public bool loop;
        public List<string> errors = new List<string>();
        public List<string> sampleKeys = new List<string>();
        public double introDur, loopDur, meter, keyMidi;
        public int tonicPc;
        public Dictionary<string, double> droneMidi = new Dictionary<string, double>();
        public Dictionary<string, Spec> voices = new Dictionary<string, Spec>(), kit = new Dictionary<string, Spec>();
        public double[] accents;
        public Dictionary<string, double[]> pitchSpan = new Dictionary<string, double[]>();   // 实时合成乐器 → [最低, 最高] MIDI 音高（预生成振荡器波表）
        public Section LoopSection { get { return form.Count > 0 ? sections[form[Math.Min(loopFrom, form.Count - 1)]] : null; } }
    }

    // 乐曲注册、解析、编译、采样缓存（全部线程安全）
    public static class MusicLib
    {
        static readonly int[] DEG_SEMI = { 0, 0, 2, 4, 5, 7, 9, 11 };
        static readonly Dictionary<string, double> DYN = new Dictionary<string, double>
        { { "ppp", 0.25 }, { "pp", 0.34 }, { "p", 0.46 }, { "mp", 0.6 }, { "mf", 0.74 }, { "f", 0.88 }, { "ff", 1.0 }, { "fff", 1.08 } };
        const string ORN_CHARS = "~*^v/\\><)@&";
        public static readonly Dictionary<string, string> CultureFallback = new Dictionary<string, string>
        {
            { "han", "han" }, { "nanman", "han" }, { "yi", "han" }, { "wa", "wa" }, { "korea", "korea" }, { "steppe", "steppe" }, { "tarim", "kushan" },
            { "seasia", "seasia" }, { "kushan", "kushan" }, { "persia", "persia" }, { "arab", "arab" }, { "roman", "roman" }, { "celt", "celt" },
            { "german", "celt" }, { "sarmatian", "steppe" },
        };

        static readonly object L = new object();
        static readonly Dictionary<string, MObj> PIECES = new Dictionary<string, MObj>();
        static readonly List<string> order = new List<string>();
        static readonly HashSet<string> derived = new HashSet<string>();
        static readonly Dictionary<string, Compiled> COMPILED = new Dictionary<string, Compiled>();
        static bool loaded;
        public static int LoadedHash;
        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var P = MusicScores.Pieces;
            for (int i = 0; i + 1 < P.Length; i += 2) AddLocked(P[i], (MObj)MJson.Parse(P[i + 1]));
        }
        static void AddLocked(string key, MObj piece)
        {
            if (!PIECES.ContainsKey(key)) order.Add(key);
            PIECES[key] = piece; COMPILED.Remove(key);
        }
        public static void Add(string key, MObj piece) { lock (L) { Load(); AddLocked(key, piece); derived.Remove(key); } }
        public static MObj Get(string key) { if (key == null) return null; lock (L) { Load(); MObj p; return PIECES.TryGetValue(key, out p) ? p : null; } }
        public static List<string> Keys() { lock (L) { Load(); var r = new List<string>(); foreach (var k in order) if (!derived.Contains(k)) r.Add(k); return r; } }

        public static string Resolve(string kind, string culture)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            lock (L)
            {
                Load();
                if (kind.IndexOf('@') >= 0) return PIECES.ContainsKey(kind) ? kind : kind.Split('@')[0];
                string c;
                if (culture == null || !CultureFallback.TryGetValue(culture, out c)) c = culture;
                if (!string.IsNullOrEmpty(c) && c != "han" && PIECES.ContainsKey(kind + "@" + c)) return kind + "@" + c;
                return PIECES.ContainsKey(kind) ? kind : null;
            }
        }

        // 派生版本：移调 / 改速（冲锋乐句跟随当前战场曲）；key 形如 'clash|t3|b138'，不列入 Keys()
        public static string Variant(string key, double transpose, double bpm)
        {
            lock (L)
            {
                Load();
                MObj P;
                if (!PIECES.TryGetValue(key, out P)) return null;
                int tr = (int)MU.JsRound(transpose);
                double b = bpm != 0 && !double.IsNaN(bpm) ? MU.JsRound(bpm) : 0;
                double pb = P.Num("bpm", 0);
                if (tr == 0 && (b == 0 || b == pb)) return key;
                string vk = key + "|t" + tr + "|b" + MU.JsNum(b != 0 ? b : pb);
                if (!PIECES.ContainsKey(vk))
                {
                    var V = P.Clone();
                    V["transpose"] = P.Num("transpose", 0) + tr;
                    if (b != 0) V["bpm"] = b;
                    AddLocked(vk, V);
                    derived.Add(vk);
                }
                return vk;
            }
        }

        public static List<string> Validate(string key) { var C = Compile(key); return C != null ? new List<string>(C.errors) : new List<string> { "未知曲目 " + key }; }
        public static int Tonic(string key) { var C = Compile(key); return C != null ? C.tonicPc : -1; }
        // { intro, loop, total }
        public static double[] Duration(string key) { var C = Compile(key); return C != null ? new[] { C.introDur, C.loopDur, C.introDur + C.loopDur } : null; }

        // ------------------------------------------------------------ 解析 --
        sealed class Head { public string acc; public int deg, oct; public string rest; }
        static Head ParseHead(string s)
        {
            int i = 0;
            string acc = "";
            if (i < s.Length && (s[i] == '#' || s[i] == 'b')) { acc = s[i].ToString(); i++; }
            if (i >= s.Length || s[i] < '0' || s[i] > '7') return null;
            int deg = s[i] - '0'; i++;
            int oct = 0;
            while (i < s.Length && (s[i] == '\'' || s[i] == ',')) { oct += s[i] == '\'' ? 1 : -1; i++; }
            return new Head { acc = acc, deg = deg, oct = oct, rest = s.Substring(i) };
        }
        static int AccS(string acc) { return acc == "#" ? 1 : acc == "b" ? -1 : 0; }

        static void ParseSuffix(string s, Action<string> err, out double mul, out double explicitDur, out Orn orn)
        {
            mul = 1; explicitDur = double.NaN; orn = new Orn();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '_') { mul *= 0.5; i++; }
                else if (c == '.') { mul *= 1.5; i++; }
                else if (c == 't') { mul *= 2.0 / 3; i++; }
                else if (c == ':')
                {
                    int j = i + 1, st = j;
                    while (j < s.Length && s[j] >= '0' && s[j] <= '9') j++;
                    if (j < s.Length && s[j] == '.') { j++; while (j < s.Length && s[j] >= '0' && s[j] <= '9') j++; }
                    // 正则 :([0-9]*\.?[0-9]+)：末尾必须是数字
                    while (j > st && s[j - 1] == '.') j--;
                    if (j == st) { err("时值格式错误 " + s); i++; continue; }
                    explicitDur = double.Parse(s.Substring(st, j - st), CultureInfo.InvariantCulture); i = j;
                }
                else if (ORN_CHARS.IndexOf(c) >= 0)
                {
                    switch (c)
                    {
                        case '~': orn.vib++; break;
                        case '*': orn.trem = true; break;
                        case '^': orn.grace = 1; break;
                        case 'v': orn.grace = -1; break;
                        case '/': orn.slide = true; break;
                        case '\\': orn.fall = true; break;
                        case '>': orn.acc = true; break;
                        case '<': orn.swell = true; break;
                        case ')': orn.tie = true; break;
                        case '@': orn.gliss++; break;
                        case '&': orn.glissDown = true; break;
                    }
                    i++;
                }
                else { err("未知记号 \"" + c + "\" 于 " + s); i++; }
            }
        }

        sealed class VEv { public bool rest, breath; public double beat, dur, vel; public List<Head> notes; public Orn orn; }
        static string F3(double x) { return MU.JsNum(Math.Round(x, 3)); }

        static List<VEv> ParseVoice(string str, double meter, Action<string> err, out double beatsOut)
        {
            var toks = (str ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var evs = new List<VEv>();
            double beat = 0, barStart = 0, vel = DYN["mf"];
            int bar = 1;
            VEv last = null;
            int hpFrom = -1; double hpV0 = 0;
            foreach (var tk in toks)
            {
                if (tk == "|" || tk == "||" || tk == "|:" || tk == ":|")
                {
                    double len = beat - barStart;
                    if (Math.Abs(len - meter) > 1e-6) err("第 " + bar + " 小节 " + F3(len) + " 拍（应为 " + MU.JsNum(meter) + "）");
                    barStart = beat; bar++;
                    continue;
                }
                if (tk == "-") { if (last != null) last.dur += 1; beat += 1; continue; }
                if (tk == ";") { if (last != null) last.breath = true; continue; }
                if (tk[0] == '!')
                {
                    string d = tk.Substring(1);
                    if (d == "<" || d == ">") { hpFrom = evs.Count; hpV0 = vel; continue; }
                    double dv;
                    if (!DYN.TryGetValue(d, out dv)) { err("未知力度 " + tk); continue; }
                    if (hpFrom >= 0)
                    {
                        int nn = evs.Count - hpFrom;
                        for (int k = 0; k < nn; k++) evs[hpFrom + k].vel = hpV0 + (dv - hpV0) * ((k + 0.5) / nn);
                        hpFrom = -1;
                    }
                    vel = dv;
                    continue;
                }
                var parts = tk.Split('+');
                var heads = new List<Head>();
                bool ok = true;
                for (int p = 0; p < parts.Length; p++)
                {
                    var h = ParseHead(parts[p]);
                    if (h == null) { err("无法解析 \"" + tk + "\""); ok = false; break; }
                    if (p < parts.Length - 1 && h.rest.Length > 0) err("和弦内只有最后一个音可带后缀：" + tk);
                    heads.Add(h);
                }
                if (!ok) continue;
                double mul, ex; Orn orn;
                ParseSuffix(heads[heads.Count - 1].rest, err, out mul, out ex, out orn);
                double dur = !double.IsNaN(ex) ? ex * mul : mul;
                if (heads.Count == 1 && heads[0].deg == 0)
                {
                    last = new VEv { rest = true, beat = beat, dur = dur };
                    evs.Add(last);
                    beat += dur;
                    continue;
                }
                last = new VEv { beat = beat, dur = dur, notes = heads, vel = vel, orn = orn };
                if (orn.acc) last.vel = Math.Min(1.1, vel * 1.18);
                evs.Add(last);
                beat += dur;
            }
            if (beat - barStart > 1e-6)
            {
                double len = beat - barStart;
                if (Math.Abs(len - meter) > 1e-6) err("第 " + bar + " 小节 " + F3(len) + " 拍（应为 " + MU.JsNum(meter) + "）");
            }
            beatsOut = beat;
            return evs;
        }

        sealed class Hit { public string lane; public double beat, vel; }
        sealed class Pat { public double beats, step; public List<Hit> hits = new List<Hit>(); }
        static Pat ParsePattern(MObj pat, Action<string> err, string name)
        {
            double step = pat.Num("_step", 0); if (step == 0) step = 0.25;
            var R = new Pat { step = step };
            double beats = -1;
            foreach (var lane in pat.Keys)
            {
                if (lane.Length > 0 && lane[0] == '_') continue;
                var raw = pat[lane]; string sv = raw is string ? (string)raw : Convert.ToString(raw, CultureInfo.InvariantCulture);
                var sb = new StringBuilder();
                foreach (char ch in sv) if (!char.IsWhiteSpace(ch) && ch != '|') sb.Append(ch);
                string s = sb.ToString();
                double len = s.Length * step;
                if (beats >= 0 && Math.Abs(len - beats) > 1e-6) err("节奏型 " + name + " 的轨 " + lane + " 长 " + MU.JsNum(len) + " 拍，与其他轨不符");
                beats = Math.Max(beats, len);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '.' || c == '-') continue;
                    double b = i * step;
                    if (c == 'X') R.hits.Add(new Hit { lane = lane, beat = b, vel = 1.0 });
                    else if (c == 'x') R.hits.Add(new Hit { lane = lane, beat = b, vel = 0.74 });
                    else if (c == 'o') R.hits.Add(new Hit { lane = lane, beat = b, vel = 0.44 });
                    else if (c == 'f') { R.hits.Add(new Hit { lane = lane, beat = b - 0.06, vel = 0.42 }); R.hits.Add(new Hit { lane = lane, beat = b, vel = 0.84 }); }
                    else if (c == 'r') { for (int k = 0; k < 4; k++) R.hits.Add(new Hit { lane = lane, beat = b + k * step / 4, vel = 0.42 + 0.14 * k }); }
                    else if (c >= '1' && c <= '9') R.hits.Add(new Hit { lane = lane, beat = b, vel = (c - '0') / 9.0 });
                    else err("节奏型 " + name + " 未知字符 " + c);
                }
            }
            R.beats = Math.Max(0, beats);
            return R;
        }

        public static string SampleKeyPitch(InstDef def, double p) { return def.pitchless ? "x" : MU.Fixed2(p); }
        public static string DrumKey(InstDef def, int art, double pitch, int variant)
        {
            return "a" + art + (!double.IsNaN(pitch) && !def.pitchless ? "@" + MU.Fixed2(pitch) : "") + "#" + variant;
        }

        sealed class RSec { public MObj voices = new MObj(), inst = new MObj(), oct = new MObj(); public string dr; public double bpm = double.NaN, bpmTo = double.NaN, dyn = 1, tr = 0; }
        static readonly HashSet<string> RESERVED = new HashSet<string> { "from", "mute", "inst", "oct", "bpm", "bpmTo", "dyn", "dr", "bars", "label", "tr" };

        // ------------------------------------------------------------ 编译 --
        public static Compiled Compile(string key)
        {
            if (key == null) return null;
            lock (L)
            {
                Load();
                Compiled C;
                if (COMPILED.TryGetValue(key, out C)) return C;
                MObj P;
                if (!PIECES.TryGetValue(key, out P)) return null;
                C = CompileLocked(key, P);
                COMPILED[key] = C;
                return C;
            }
        }

        static Compiled CompileLocked(string key, MObj P)
        {
            var C = new Compiled { key = key, piece = P };
            var errors = C.errors;
            double meter = P.Num("meter", 0); if (meter == 0) meter = 4;
            C.meter = meter;
            double keyMidi = MU.KeyToMidi(P.Str("key") ?? "C", P.Has("oct") ? P.Num("oct", 4) : 4) + P.Num("transpose", 0);
            C.keyMidi = keyMidi;
            var cents = P.Obj("cents") ?? new MObj();
            Func<string, double> centOf = s => { double v = cents.Num(s, 0); return v; };
            // 调式音阶
            string scaleStr = P.Str("scale"); if (string.IsNullOrEmpty(scaleStr)) scaleStr = "1 2 3 5 6";
            var scale = new List<double[]>();
            foreach (var s in scaleStr.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                var h = ParseHead(s);
                if (h != null) scale.Add(new[] { DEG_SEMI[h.deg] + AccS(h.acc), centOf(s) });
            }
            StableSort(scale, (a, b) => a[0].CompareTo(b[0]));
            var scalePitches = new List<double>();
            for (int o = -6; o <= 6; o++) foreach (var s in scale) scalePitches.Add(keyMidi + o * 12 + s[0] + s[1] / 100);
            scalePitches.Sort();
            Func<double, int, double> neighbor = (p, dir) =>
            {
                if (dir > 0) { foreach (var q in scalePitches) if (q > p + 0.3) return q; return p + 2; }
                for (int i = scalePitches.Count - 1; i >= 0; i--) if (scalePitches[i] < p - 0.3) return scalePitches[i];
                return p - 2;
            };
            Func<double, double, List<double>> runBelow = (p, span) => { var r = new List<double>(); foreach (var q in scalePitches) if (q < p - 0.3 && q >= p - span - 0.01) r.Add(q); return r; };
            Func<double, double, List<double>> runAbove = (p, span) => { var r = new List<double>(); foreach (var q in scalePitches) if (q > p + 0.3 && q <= p + span + 0.01) r.Add(q); r.Reverse(); return r; };
            Func<Head, double, double> pitchOf = (n, voiceOct) =>
            {
                double c = centOf((n.acc ?? "") + n.deg);
                return keyMidi + DEG_SEMI[n.deg] + AccS(n.acc) + 12 * (n.oct + voiceOct) + c / 100;
            };

            var voicesO = P.Obj("voices") ?? new MObj();
            var kitO = P.Obj("kit") ?? new MObj();
            foreach (var v in voicesO.Keys)
            {
                var V = Spec.From(voicesO.Obj(v));
                C.voices[v] = V;
                if (V.drone != null) { var h = ParseHead(V.drone); if (h != null) C.droneMidi[v] = pitchOf(h, 0); else errors.Add("声部 " + v + " 的 drone 无法解析"); }
                if (Instruments.Get(V.inst) == null) errors.Add("声部 " + v + " 未知乐器 " + V.inst);
            }
            foreach (var l in kitO.Keys)
            {
                var K = Spec.From(kitO.Obj(l));
                C.kit[l] = K;
                if (Instruments.Get(K.inst) == null) errors.Add("鼓轨 " + l + " 未知乐器 " + K.inst);
            }
            var acc = P.Arr("accents");
            if (acc != null) { C.accents = new double[acc.Count]; for (int i = 0; i < acc.Count; i++) C.accents[i] = acc[i] is double ? (double)acc[i] : 0; }

            var pats = new Dictionary<string, Pat>();
            var patsO = P.Obj("pats");
            if (patsO != null) foreach (var name in patsO.Keys) { string nm = name; pats[name] = ParsePattern(patsO.Obj(name) ?? new MObj(), m => errors.Add("[节奏型 " + nm + "] " + m), name); }

            var sectionsO = P.Obj("sections") ?? new MObj();
            Func<string, int, RSec> resolveSection = null;
            resolveSection = (name, depth) =>
            {
                var S = sectionsO.Obj(name);
                if (S == null) { errors.Add("未定义的段 " + name); return null; }
                if (depth > 8) { errors.Add("段继承过深 " + name); return null; }
                var bs = new RSec();
                if (S.Str("from") != null)
                {
                    var b = resolveSection(S.Str("from"), depth + 1);
                    if (b != null) bs = new RSec { voices = b.voices.Clone(), inst = b.inst.Clone(), oct = b.oct.Clone(), dr = b.dr, bpm = b.bpm, bpmTo = double.NaN, dyn = b.dyn, tr = b.tr };
                }
                foreach (var k in S.Keys)
                {
                    if (RESERVED.Contains(k)) continue;
                    if (!voicesO.Has(k)) { errors.Add("[段 " + name + "] 未知声部 " + k); continue; }
                    bs.voices[k] = S[k];
                }
                var mute = S.Arr("mute");
                if (mute != null) foreach (var v in mute) if (v is string) bs.voices.Remove((string)v);
                var si = S.Obj("inst"); if (si != null) foreach (var k in si.Keys) bs.inst[k] = si[k];
                var so = S.Obj("oct"); if (so != null) foreach (var k in so.Keys) bs.oct[k] = so[k];
                if (S.Has("dr")) bs.dr = S.Str("dr");
                if (S.Has("bpm")) bs.bpm = S.Num("bpm", double.NaN);
                if (S.Has("bpmTo")) bs.bpmTo = S.Num("bpmTo", double.NaN);
                if (S.Has("dyn")) bs.dyn = S.Num("dyn", 1);
                if (S.Has("tr")) bs.tr = S.Num("tr", 0);
                return bs;
            };

            var seenKeys = new HashSet<string>();
            Action<string> addKey = k => { if (seenKeys.Add(k)) C.sampleKeys.Add(k); };
            foreach (var name in sectionsO.Keys)
            {
                var R = resolveSection(name, 0);
                if (R == null) continue;
                double TR = R.tr;
                Func<double, int, double> nb = (p, d) => neighbor(p - TR, d) + TR;
                Func<double, double, double[]> rb = (p, s) => { var l = runBelow(p - TR, s); var a = new double[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = l[i] + TR; return a; };
                Func<double, double, double[]> ra = (p, s) => { var l = runAbove(p - TR, s); var a = new double[l.Count]; for (int i = 0; i < a.Length; i++) a[i] = l[i] + TR; return a; };
                var items = new List<Item>();
                double beats = 0;
                var lens = new List<KeyValuePair<string, double>>();
                foreach (var v in R.voices.Keys)
                {
                    var V = C.voices[v];
                    string instName = R.inst.Str(v); if (string.IsNullOrEmpty(instName)) instName = V.inst;
                    var def = Instruments.Get(instName);
                    if (def == null) { errors.Add("[段 " + name + "] 声部 " + v + " 未知乐器 " + instName); continue; }
                    double voiceOct = V.oct + R.oct.Num(v, 0);
                    string vv = v, nm = name;
                    double vb;
                    var rawV = R.voices[v];
                    var evs = ParseVoice(rawV as string ?? Convert.ToString(rawV, CultureInfo.InvariantCulture), meter, m => errors.Add("[段 " + nm + " · " + vv + "] " + m), out vb);
                    lens.Add(new KeyValuePair<string, double>(v, vb));
                    beats = Math.Max(beats, vb);
                    double dyn = R.dyn;
                    if (def.type != InstType.Sample)
                    {
                        double[] span;
                        if (!C.pitchSpan.TryGetValue(instName, out span)) C.pitchSpan[instName] = span = new[] { 999.0, -999.0 };
                        foreach (var e in evs) if (!e.rest) foreach (var h in e.notes) { double q = pitchOf(h, voiceOct) + TR; span[0] = Math.Min(span[0], q); span[1] = Math.Max(span[1], q); }
                    }
                    if (def.type == InstType.Legato)
                    {
                        Item ph = null;
                        foreach (var e in evs)
                        {
                            if (e.rest) { ph = null; continue; }
                            var n = e.notes[e.notes.Count - 1];
                            double p = pitchOf(n, voiceOct) + TR;
                            var note = new PNote { beat = e.beat, dur = e.dur, p = p, vel = e.vel * dyn, orn = e.orn };
                            if (e.orn.grace != 0) note.gp = nb(p, e.orn.grace);
                            if (e.orn.trem) note.tp = nb(p, 1);
                            if (ph == null) { ph = new Item { type = 'p', voice = v, inst = instName, beat = e.beat, notes = new List<PNote>() }; items.Add(ph); }
                            ph.notes.Add(note);
                            if (e.breath) ph = null;
                        }
                    }
                    else
                    {
                        foreach (var e in evs)
                        {
                            if (e.rest) continue;
                            var ps = new double[e.notes.Count];
                            for (int i = 0; i < ps.Length; i++) ps[i] = pitchOf(e.notes[i], voiceOct) + TR;
                            var it = new Item { type = def.type == InstType.Sample ? 's' : 'y', voice = v, inst = instName, beat = e.beat, dur = e.dur, ps = ps, vel = e.vel * dyn, orn = e.orn };
                            if (def.type == InstType.Sample)
                            {
                                double top = ps[ps.Length - 1];
                                if (e.orn.grace != 0) it.gp = nb(top, e.orn.grace);
                                if (e.orn.gliss != 0) it.run = rb(top, 12 * e.orn.gliss);
                                if (e.orn.glissDown) it.run = ra(top, 12);
                                if (e.orn.trem && def.trill) it.tp = nb(top, 1);
                                foreach (var q in ps) addKey(def.name + "|" + SampleKeyPitch(def, q));
                                if (!double.IsNaN(it.gp)) addKey(def.name + "|" + SampleKeyPitch(def, it.gp));
                                if (!double.IsNaN(it.tp)) addKey(def.name + "|" + SampleKeyPitch(def, it.tp));
                                if (it.run != null) foreach (var q in it.run) addKey(def.name + "|" + SampleKeyPitch(def, q));
                            }
                            items.Add(it);
                        }
                    }
                }
                foreach (var kv in lens) if (Math.Abs(kv.Value - beats) > 1e-6) errors.Add("[段 " + name + "] 声部 " + kv.Key + " 长 " + MU.JsNum(kv.Value) + " 拍，段长 " + MU.JsNum(beats) + " 拍");
                // 鼓谱
                if (!string.IsNullOrEmpty(R.dr))
                {
                    var seq = new List<string>();
                    foreach (var tk in R.dr.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int star = tk.IndexOf('*');
                        string pn = star >= 0 ? tk.Substring(0, star) : tk;
                        int cnt = 1;
                        if (star >= 0) { string ns = tk.Substring(star + 1); bool digits = ns.Length > 0; foreach (char ch in ns) if (ch < '0' || ch > '9') digits = false; if (!digits || pn.Length == 0) { errors.Add("[段 " + name + "] 鼓谱序列错误 " + tk); continue; } cnt = int.Parse(ns, CultureInfo.InvariantCulture); }
                        if (pn.Length == 0) { errors.Add("[段 " + name + "] 鼓谱序列错误 " + tk); continue; }
                        for (int k = 0; k < cnt; k++) seq.Add(pn);
                    }
                    double b = 0;
                    foreach (var pn in seq)
                    {
                        if (pn == "_") { b += meter; continue; }
                        Pat pt;
                        if (!pats.TryGetValue(pn, out pt)) { errors.Add("[段 " + name + "] 未知节奏型 " + pn); continue; }
                        foreach (var h in pt.hits)
                        {
                            Spec K;
                            if (!C.kit.TryGetValue(h.lane, out K)) { errors.Add("[节奏型 " + pn + "] 未知鼓轨 " + h.lane); continue; }
                            var def = Instruments.Get(K.inst);
                            if (def == null) { errors.Add("鼓轨 " + h.lane + " 未知乐器 " + K.inst); continue; }
                            double pitch = double.NaN;
                            if (!string.IsNullOrEmpty(K.note)) { var nh = ParseHead(K.note); pitch = nh != null ? pitchOf(nh, 0) + TR : double.NaN; }
                            items.Add(new Item { type = 'd', lane = h.lane, inst = K.inst, beat = b + h.beat, vel = h.vel * R.dyn, art = K.art, p = pitch });
                            int nv = Math.Max(1, def.variants);
                            for (int k = 0; k < nv; k++) addKey(def.name + "|" + DrumKey(def, K.art, pitch, k));
                        }
                        b += pt.beats;
                    }
                    if (beats == 0) beats = b;
                    else if (b > beats + 1e-6) errors.Add("[段 " + name + "] 鼓谱 " + MU.JsNum(b) + " 拍，超过段长 " + MU.JsNum(beats) + " 拍");
                    else if (b < beats - 1e-6 && seq.Count > 0)
                    {
                        double loopLen = b;
                        if (loopLen > 0)
                        {
                            var bas = items.FindAll(x => x.type == 'd');
                            for (double off = loopLen; off < beats - 1e-6; off += loopLen)
                                foreach (var h in bas) if (h.beat + off < beats - 1e-6) { var c = h.Clone(); c.beat = h.beat + off; items.Add(c); }
                        }
                    }
                }
                if (Math.Abs(beats / meter - Math.Round(beats / meter)) > 1e-6) errors.Add("[段 " + name + "] 段长 " + MU.JsNum(beats) + " 拍，不是整小节");
                StableSort(items, (a, c) => a.beat.CompareTo(c.beat));
                double bpm0 = MU.Or(R.bpm, MU.Or(P.Num("bpm", 0), 90));
                double bpm1 = MU.Or(R.bpmTo, bpm0);
                var sec = new Section { name = name, beats = beats, items = items, bars = beats / meter, bpm = bpm0, bpmTo = bpm1 };
                sec.MakeTime(beats, bpm0, bpm1);
                C.sections[name] = sec;
            }
            var formA = P.Arr("form");
            var form0 = new List<string>();
            if (formA != null) { foreach (var f in formA) form0.Add(f as string); } else form0.AddRange(sectionsO.Keys);
            foreach (var n in form0) { if (n == null || !C.sections.ContainsKey(n)) { errors.Add("曲式中未知的段 " + n); continue; } C.form.Add(n); }
            C.loopFrom = 0;
            if (P.Has("loopFrom"))
            {
                var lf = P["loopFrom"];
                C.loopFrom = lf is double ? (int)(double)lf : C.form.IndexOf(lf as string);
                if (C.loopFrom < 0) { errors.Add("loopFrom 不在曲式中"); C.loopFrom = 0; }
            }
            string tonicS = P.Str("tonic");
            if (string.IsNullOrEmpty(tonicS)) { var sc = (P.Str("scale") ?? "1").Split((char[])null, StringSplitOptions.RemoveEmptyEntries); tonicS = sc.Length > 0 ? sc[0] : ""; if (string.IsNullOrEmpty(P.Str("scale"))) tonicS = "1"; }
            var th = ParseHead(tonicS);
            C.tonicPc = th != null ? (int)((((long)MU.JsRound(keyMidi + DEG_SEMI[th.deg] + AccS(th.acc)) % 12) + 12) % 12) : (int)((((long)MU.JsRound(keyMidi) % 12) + 12) % 12);
            for (int i = 0; i < C.form.Count; i++) { if (i < C.loopFrom) C.introDur += C.sections[C.form[i]].dur; else C.loopDur += C.sections[C.form[i]].dur; }
            object lo = P["loop"];
            C.loop = !(lo is bool) || (bool)lo;
            return C;
        }

        // 稳定排序（与 V8 的 Array.prototype.sort 一致）
        public static void StableSort<T>(List<T> list, Comparison<T> cmp)
        {
            int n = list.Count;
            if (n < 2) return;
            var a = list.ToArray(); var tmp = new T[n];
            for (int w = 1; w < n; w *= 2)
            {
                for (int lo = 0; lo < n; lo += 2 * w)
                {
                    int mid = Math.Min(lo + w, n), hi = Math.Min(lo + 2 * w, n), i = lo, j = mid, k = lo;
                    while (i < mid && j < hi) tmp[k++] = cmp(a[j], a[i]) < 0 ? a[j++] : a[i++];
                    while (i < mid) tmp[k++] = a[i++];
                    while (j < hi) tmp[k++] = a[j++];
                }
                var t = a; a = tmp; tmp = t;
            }
            for (int i = 0; i < n; i++) list[i] = a[i];
        }

        // ======================================================== 采样缓存 ==
        public sealed class Sample { public float[] data; public int sr; public long used; }
        static readonly object SL = new object();
        static readonly Dictionary<string, Sample> SAMPLES = new Dictionary<string, Sample>();
        static long sampleTotal, clock;
        const long SAMPLE_BUDGET = 7000000;   // 采样缓存上限（浮点数个数，约 28MB）
        static readonly List<HashSet<string>> pinSets = new List<HashSet<string>>();
        static readonly HashSet<string> warmPins = new HashSet<string>();
        public static int SampleCount { get { lock (SL) return SAMPLES.Count; } }
        public static long SampleFloats { get { lock (SL) return sampleTotal; } }

        public static void RegisterPins(HashSet<string> pins) { lock (SL) if (!pinSets.Contains(pins)) pinSets.Add(pins); }
        public static void UnregisterPins(HashSet<string> pins) { lock (SL) pinSets.Remove(pins); }
        public static object PinLock { get { return SL; } }

        public static Sample GetSample(string fk)
        {
            lock (SL) { Sample s; if (SAMPLES.TryGetValue(fk, out s)) { s.used = ++clock; return s; } return null; }
        }
        public static bool HasSample(string fk) { lock (SL) return SAMPLES.ContainsKey(fk); }

        // 'inst|子键' → 渲染一个采样
        public static float[] RenderSample(string fk, out int sr)
        {
            sr = 0;
            int bar = fk.IndexOf('|');
            if (bar < 0) return null;
            var def = Instruments.Get(fk.Substring(0, bar));
            if (def == null || def.render == null) return null;
            string sub = fk.Substring(bar + 1);
            sr = def.sr;
            var a = new RenderArgs { sr = def.sr, seed = MU.Hash(fk) };
            if (sub.Length > 0 && sub[0] == 'a')
            {
                int at = sub.IndexOf('@'), hs = sub.LastIndexOf('#');
                string artS = sub.Substring(1, (at >= 0 ? at : hs) - 1);
                int art; a.art = int.TryParse(artS, NumberStyles.Integer, CultureInfo.InvariantCulture, out art) ? art : 0;
                a.pitch = at >= 0 ? double.Parse(sub.Substring(at + 1, hs - at - 1), CultureInfo.InvariantCulture) : double.NaN;
                a.freq = !double.IsNaN(a.pitch) ? MU.Mtof(a.pitch) : 0;
                a.variant = int.Parse(sub.Substring(hs + 1), CultureInfo.InvariantCulture);
            }
            else
            {
                double p = sub == "x" ? 60 : double.Parse(sub, CultureInfo.InvariantCulture);
                a.pitch = p; a.freq = MU.Mtof(p);
            }
            try { return def.render(a); }
            catch (Exception) { return null; }
        }
        static void Store(string fk, float[] data, int sr)
        {
            lock (SL)
            {
                if (SAMPLES.ContainsKey(fk)) return;
                SAMPLES[fk] = new Sample { data = data, sr = sr, used = ++clock };
                sampleTotal += data.Length;
            }
        }
        public static void RenderSampleSync(string fk)
        {
            if (HasSample(fk)) return;
            int sr;
            var d = RenderSample(fk, out sr);
            if (d != null) Store(fk, d, sr);
        }
        static bool IsPinned(string k)
        {
            if (warmPins.Contains(k)) return true;
            foreach (var s in pinSets) if (s.Contains(k)) return true;
            return false;
        }
        static void Evict()
        {
            lock (SL)
            {
                if (sampleTotal <= SAMPLE_BUDGET) return;
                var arr = new List<KeyValuePair<string, Sample>>();
                foreach (var kv in SAMPLES) if (!IsPinned(kv.Key)) arr.Add(kv);
                arr.Sort((x, y) => x.Value.used.CompareTo(y.Value.used));
                foreach (var kv in arr)
                {
                    if (sampleTotal <= SAMPLE_BUDGET * 0.8) break;
                    SAMPLES.Remove(kv.Key); sampleTotal -= kv.Value.data.Length;
                }
            }
        }
        public static void PrepareSync(string key)
        {
            var C = Compile(key);
            if (C == null) return;
            foreach (var k in C.sampleKeys) RenderSampleSync(k);
        }

        // ---- 后台线程：预渲染采样（不占用音频渲染线程）
        static readonly Queue<Action> jobs = new Queue<Action>();
        static Thread worker;
        static int queued;
        public static int PendingJobs { get { lock (jobs) return queued; } }
        static void Enqueue(Action a)
        {
            lock (jobs)
            {
                jobs.Enqueue(a); queued++;
                if (worker == null)
                {
                    worker = new Thread(WorkerLoop) { IsBackground = true, Name = "SanguoMusicPrep", Priority = ThreadPriority.BelowNormal };
                    worker.Start();
                }
                Monitor.Pulse(jobs);
            }
        }
        static void WorkerLoop()
        {
            for (;;)
            {
                Action a;
                lock (jobs)
                {
                    while (jobs.Count == 0) Monitor.Wait(jobs);
                    a = jobs.Dequeue();
                }
                try { a(); } catch (Exception) { /* 忽略：渲染失败的采样不播放 */ }
                lock (jobs) queued--;
            }
        }
        // pins：保护这些采样的集合（引擎传自己的；null = 预热，只保护最近一次预热的曲目）；done 在后台线程回调
        public static void PrepareAsync(string key, HashSet<string> pins, Action done, double waveSr = 0)
        {
            Enqueue(() =>
            {
                var C = Compile(key);
                if (C != null && waveSr > 0) foreach (var kv in C.pitchSpan) Instruments.WarmWaves(kv.Key, kv.Value[0], kv.Value[1], waveSr);
                if (C != null)
                {
                    lock (SL)
                    {
                        var ps = pins;
                        if (ps == null) { warmPins.Clear(); ps = warmPins; }
                        foreach (var k in C.sampleKeys) ps.Add(k);
                    }
                    foreach (var k in C.sampleKeys) RenderSampleSync(k);
                    Evict();
                }
                if (done != null) done();
            });
        }
        public static void RequestSample(string fk) { if (!HasSample(fk)) Enqueue(() => RenderSampleSync(fk)); }
    }

    // ================================================================ 引擎 ==
    public sealed class PlayOpts
    {
        public double fade = 0.8, at = double.NaN, fadeIn = double.NaN, grid = 1, duck = double.NaN;
        public bool overlay, sync;
    }
    public sealed class MainInfo { public string key; public double bpm, meter; public int tonicPc; public bool pending; }

    // 一个播放实例（多个声部 → 曲目总线 → 引擎混音）。线程：除 Post 外的方法都应在持有引擎锁时调用（Unity 端由 Sfx 保证）。
    public sealed class MusicEngine
    {
        public const double LOOKAHEAD = 0.4;      // 前瞻排程时长（秒）
        public const int BLOCK = Reverb.B;        // 渲染块（帧）
        internal const double PAN_WIDTH = 2.0;    // 谱面声像 × 此系数（上限 ±0.85）
        public readonly double sr, dt;
        public readonly bool offline;
        public double volume;
        public string[] stems;                    // 测试：只保留这些声部
        public Action<string, bool> onEnded;      // 非循环曲结束（在渲染线程回调）
        long frames;
        public double time { get { return frames / sr; } }
        internal readonly List<Track> tracks = new List<Track>();
        readonly float[] mixL, mixR, revL, revR, wetL, wetR;
        readonly Biquad rhL, rhR, rlL, rlR, hpL, hpR;
        readonly Reverb conv;
        readonly Compressor glue, lim;
        readonly Param outGain;
        internal readonly float[][] s = new float[10][];   // 声部渲染用的暂存（单线程渲染）
        internal readonly float[] noise;
        double duckLevel = double.NaN;
        int token, ovToken;
        readonly Dictionary<string, int> pending = new Dictionary<string, int>();
        public readonly HashSet<string> pins = new HashSet<string>();
        bool dirtyPins, disposed;
        public bool raw;                          // 测试：绕过压缩 / 限幅
        readonly Queue<Action> posted = new Queue<Action>();

        public MusicEngine(double sampleRate, bool offline, double volume = 1)
        {
            sr = sampleRate; dt = 1 / sr; this.offline = offline; this.volume = volume;
            int N = BLOCK;
            mixL = new float[N]; mixR = new float[N]; revL = new float[N]; revR = new float[N]; wetL = new float[N]; wetR = new float[N];
            for (int i = 0; i < s.Length; i++) s[i] = new float[N];
            rhL = new Biquad(Biquad.HP, 180, 0.6, 0, sr); rhR = new Biquad(Biquad.HP, 180, 0.6, 0, sr);
            rlL = new Biquad(Biquad.LP, 10000, 0.5, 0, sr); rlR = new Biquad(Biquad.LP, 10000, 0.5, 0, sr);
            hpL = new Biquad(Biquad.HP, 35, 0.7, 0, sr); hpR = new Biquad(Biquad.HP, 35, 0.7, 0, sr);
            var ir = Impulse(sr);
            conv = new Reverb(ir[0], ir[1]);
            glue = new Compressor(sr, -20, 10, 2.2f, 0.025f, 0.3f);
            lim = new Compressor(sr, -4, 0, 20, 0.002f, 0.12f);
            outGain = new Param(volume);
            noise = Noise(sr);
            if (!offline) MusicLib.RegisterPins(pins);
        }

        // ---- 共享资源（每个采样率一份）
        static readonly Dictionary<double, float[][]> irCache = new Dictionary<double, float[][]>();
        static readonly Dictionary<double, float[]> noiseCache = new Dictionary<double, float[]>();
        const double IR_SEC = 3.2, IR_DECAY = 2.5;
        // 程序生成的混响脉冲：早期反射 + 指数衰减的去相关噪声，高频衰减更快（同 JS impulseGen）
        public static float[][] Impulse(double sr)
        {
            lock (irCache)
            {
                float[][] b;
                if (irCache.TryGetValue(sr, out b)) return b;
                int n = (int)MU.JsRound(sr * IR_SEC);
                b = new[] { new float[n], new float[n] };
                for (int c = 0; c < 2; c++)
                {
                    var d = b[c];
                    var r = new Rng(777 + c * 31);
                    int pre = (int)MU.JsRound(sr * 0.012);
                    double lp = 0;
                    for (int i = pre; i < n; i++)
                    {
                        double t = (i - pre) / sr;
                        double e = Math.Exp(-t * 6.9 / IR_DECAY);
                        double k = 0.75 - 0.6 * Math.Min(1, t / IR_DECAY);
                        lp += (r.Next() * 2 - 1 - lp) * k;
                        d[i] = (float)(lp * e * (t < 0.06 ? t / 0.06 * 0.6 + 0.4 : 1));
                    }
                    double[] taps = { 0.017, 0.023, 0.031, 0.041, 0.053, 0.067, 0.079 };
                    for (int k = 0; k < taps.Length; k++)
                    {
                        int i = (int)MU.JsRound(sr * (taps[k] + c * 0.0031 * (k % 3)));
                        if (i < n) d[i] = (float)(d[i] + (k % 2 == 1 ? -1 : 1) * 0.5 * Math.Exp(-k * 0.35));
                    }
                    double e2 = 0;
                    for (int i = 0; i < n; i++) e2 += (double)d[i] * d[i];
                    double g = 1 / Math.Sqrt(e2);
                    for (int i = 0; i < n; i++) d[i] = (float)(d[i] * g);
                }
                irCache[sr] = b;
                return b;
            }
        }
        static float[] Noise(double sr)
        {
            lock (noiseCache)
            {
                float[] d;
                if (noiseCache.TryGetValue(sr, out d)) return d;
                int n = (int)MU.JsRound(sr * 2);
                d = new float[n];
                var r = new Rng(12345);
                for (int i = 0; i < n; i++) d[i] = (float)(r.Next() * 2 - 1);
                noiseCache[sr] = d;
                return d;
            }
        }

        // 从其他线程投递到渲染线程（下一块开始时执行）
        public void Post(Action a) { lock (posted) posted.Enqueue(a); }

        internal void Repin()
        {
            var keys = new List<string>();
            foreach (var t in tracks) if (!t.stopping) keys.Add(t.key);
            keys.AddRange(pending.Keys);
            var cs = new List<Compiled>();
            foreach (var key in keys) { var C = MusicLib.Compile(key); if (C != null) cs.Add(C); }   // 编译在锁外（锁顺序：引擎 → 采样，不嵌套乐曲锁）
            lock (MusicLib.PinLock)
            {
                pins.Clear();
                foreach (var C in cs) foreach (var k in C.sampleKeys) pins.Add(k);
            }
        }

        // 当前主曲目（非叠加）的信息
        public MainInfo GetMainInfo()
        {
            foreach (var tr in tracks)
            {
                if (tr.stopping || tr.overlay) continue;
                var sec = tr.seg != null ? tr.seg.sec : tr.C.LoopSection;
                return new MainInfo { key = tr.key, bpm = sec != null ? sec.bpm : 0, meter = tr.C.meter, tonicPc = tr.C.tonicPc };
            }
            return null;
        }

        // 主曲目在 after 秒之后的下一个拍点
        public double NextBeat(double after, double grid)
        {
            Track tr = null;
            foreach (var t in tracks) if (!t.stopping && !t.overlay && t.seg != null) { tr = t; break; }
            if (tr == null) return after;
            if (!(grid > 0)) grid = 1;
            var seg = tr.seg; var sec = seg.sec;
            if (seg.t0 <= after)
            {
                for (double b = 0; b <= sec.beats + 1e-6; b += grid) { double t = seg.t0 + sec.Time(b); if (t >= after - 1e-4) return t; }
                return seg.t0 + sec.dur;
            }
            double bd = sec.Time(grid);
            if (!(bd > 0)) return after;
            return seg.t0 - Math.Floor((seg.t0 - after) / bd) * bd;
        }

        public void SetVolume(double v)
        {
            volume = v;
            double t = time;
            outGain.CancelScheduledValues(t); outGain.SetTargetAtTime(v, t, 0.05);
        }

        public string Current { get { foreach (var t in tracks) if (!t.stopping) return t.key; return null; } }
        public bool Busy { get { return tracks.Count > 0 || pending.Count > 0; } }

        // 播放曲目（交叉淡入淡出）。done(true) = 开始播放；false = 被后来的请求取代 / 未知曲目。
        // 实时模式下 done 在渲染线程回调。
        public void Play(string key, PlayOpts opts = null, Action<bool> done = null)
        {
            if (MusicLib.Get(key) == null) { if (done != null) done(false); return; }
            opts = opts ?? new PlayOpts();
            bool ov = opts.overlay;
            int tok = ov ? ++ovToken : ++token;
            int c; pending.TryGetValue(key, out c); pending[key] = c + 1;
            Action go = () => { bool ok = Go(key, opts, ov, tok); if (done != null) done(ok); };
            if (offline) { MusicLib.PrepareSync(key); go(); return; }
            MusicLib.PrepareAsync(key, pins, () => Post(go), sr);
        }

        bool Go(string key, PlayOpts opts, bool ov, int tok)
        {
            int c; pending.TryGetValue(key, out c);
            if (c > 1) pending[key] = c - 1; else pending.Remove(key);
            if (tok != (ov ? ovToken : token) || disposed) { Repin(); return false; }
            var C = MusicLib.Compile(key);
            if (C == null) return false;
            double now = time;
            if (ov)
            {
                foreach (var t in tracks) if (!t.stopping && t.overlay) t.Stop(now, 0.2);
                duckLevel = double.IsNaN(opts.duck) ? 0.22 : opts.duck;
                Duck(duckLevel, 0.25);
            }
            else
            {
                foreach (var t in tracks) if (!t.stopping && !t.overlay) t.Stop(now, opts.fade);
            }
            double t0 = !double.IsNaN(opts.at) ? opts.at : now + 0.06;
            if (opts.sync && double.IsNaN(opts.at))
            {
                double tb = NextBeat(now + 0.05, opts.grid > 0 ? opts.grid : 1);
                if (tb >= now + 0.03 && tb <= now + 0.75) t0 = tb;
            }
            double fadeIn = !double.IsNaN(opts.fadeIn) ? opts.fadeIn : C.piece.Num("fadeIn", 0.02);
            var tr = new Track(this, C, t0, fadeIn, ov);
            if (!ov)
            {
                bool ducked = false;
                foreach (var t in tracks) if (t.overlay && !t.stopping && !t.unducked) ducked = true;
                if (ducked) tr.SetDuck(MU.Or(duckLevel, 0.3), now, 0.05);
            }
            tracks.Add(tr);
            Repin();
            Tick();
            return true;
        }

        public void Stop(double fade = 0.8)
        {
            token++; ovToken++;
            double now = time;
            foreach (var t in tracks) if (!t.stopping) t.Stop(now, fade);
        }

        // 压低（或恢复）当前非叠加曲目的音量：冲锋乐句叠加时使用
        public void Duck(double level, double sec = 0.3)
        {
            double now = time;
            foreach (var t in tracks) if (!t.stopping && !t.overlay) t.SetDuck(level, now, sec);
        }

        public void Tick()
        {
            double now = time, horizon = now + LOOKAHEAD;
            for (int i = 0; i < tracks.Count; i++) tracks[i].ScheduleUntil(horizon, now);
            foreach (var t in tracks)
                if (t.overlay && !t.stopping && !t.unducked && t.seg == null && now >= t.endTime - 0.3) { t.unducked = true; Duck(1, 1.0); }
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                var t = tracks[i];
                if (!t.Dead(now)) continue;
                tracks.RemoveAt(i);
                dirtyPins = true;
                if (t.finished && !t.stopping)
                {
                    if (t.overlay && !t.unducked) Duck(1, 0.6);
                    if (onEnded != null) { try { onEnded(t.key, t.overlay); } catch (Exception) { } }
                }
            }
            if (dirtyPins) { dirtyPins = false; Repin(); }
        }

        public void ScheduleUntil(double t) { foreach (var tr in tracks) tr.ScheduleUntil(t, 0); }

        public void Dispose()
        {
            disposed = true;
            MusicLib.UnregisterPins(pins);
            tracks.Clear();
        }

        // 渲染 count 帧（BLOCK 的整数倍）立体声到 L / R（从 off 开始）
        public void Render(float[] L, float[] R, int off, int count)
        {
            for (int b = 0; b + BLOCK <= count; b += BLOCK) RenderBlock(L, R, off + b);
        }

        void RenderBlock(float[] L, float[] R, int off)
        {
            int N = BLOCK;
            for (;;)
            {
                Action a = null;
                lock (posted) if (posted.Count > 0) a = posted.Dequeue();
                if (a == null) break;
                try { a(); } catch (Exception) { }
            }
            double t0 = time;
            if (offline) ScheduleUntil(t0 + LOOKAHEAD); else Tick();
            Array.Clear(mixL, 0, N); Array.Clear(mixR, 0, N); Array.Clear(revL, 0, N); Array.Clear(revR, 0, N);
            for (int i = 0; i < tracks.Count; i++) tracks[i].Render(mixL, mixR, revL, revR, N, t0);
            // 混响：高通 180 → 低通 10k → 卷积 → ×0.8
            rhL.Run(revL, N); rhR.Run(revR, N); rlL.Run(revL, N); rlR.Run(revR, N);
            conv.Process(revL, revR, wetL, wetR);
            for (int i = 0; i < N; i++) { mixL[i] += 0.8f * wetL[i]; mixR[i] += 0.8f * wetR[i]; }
            var og = s[0];
            outGain.Fill(og, N, t0, dt);
            if (raw)
            {
                for (int i = 0; i < N; i++) { L[off + i] = mixL[i] * og[i]; R[off + i] = mixR[i] * og[i]; }
            }
            else
            {
                // 35 Hz 高通 → 黏合压缩 → 限幅 → 0.62 → 软削波
                hpL.Run(mixL, N); hpR.Run(mixR, N);
                glue.Process(mixL, mixR, N);
                lim.Process(mixL, mixR, N);
                for (int i = 0; i < N; i++) { L[off + i] = (float)(SoftClip(mixL[i] * 0.62) * og[i]); R[off + i] = (float)(SoftClip(mixR[i] * 0.62) * og[i]); }
            }
            frames += N;
        }

        // 软限幅：|x| ≤ 0.75 线性，其上平滑逼近 0.98（输入超过 ±2 时钳位）——等价于 JS 的 WaveShaper 曲线
        static double SoftClip(double x)
        {
            double a = x < 0 ? -x : x;
            if (a <= 0.75) return x;
            if (a > 2) a = 2;
            double y = 0.75 + 0.23 * Math.Tanh((a - 0.75) / 0.23);
            return x < 0 ? -y : y;
        }

        // ======================================================== 离线渲染（测试） ==
        public sealed class OfflineResult { public float[] L, R; public double ms, prepMs, peak, rms; public int nan; public double sr; }
        public static OfflineResult RenderOffline(string key, double seconds, double sampleRate = 44100, string[] stems = null, bool raw = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            MusicLib.PrepareSync(key);
            double prepMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var e = new MusicEngine(sampleRate, true, 1) { stems = stems, raw = raw };
            e.Play(key, new PlayOpts { fade = 0, fadeIn = 0, at = 0 });
            int len = (int)MU.JsRound(sampleRate * seconds);
            int padded = (len + BLOCK - 1) / BLOCK * BLOCK;
            var L = new float[padded]; var R = new float[padded];
            e.Render(L, R, 0, padded);
            double ms = sw.Elapsed.TotalMilliseconds;
            e.Dispose();
            Array.Resize(ref L, len); Array.Resize(ref R, len);
            double peak = 0, sum = 0; int nan = 0;
            foreach (var d in new[] { L, R })
                for (int i = 0; i < len; i++)
                {
                    float v = d[i];
                    if (float.IsNaN(v)) { nan++; continue; }
                    double a = v < 0 ? -v : v;
                    if (a > peak) peak = a;
                    sum += (double)v * v;
                }
            return new OfflineResult { L = L, R = R, ms = ms, prepMs = prepMs, peak = peak, rms = Math.Sqrt(sum / (len * 2.0)), nan = nan, sr = sampleRate };
        }
    }

    // ----------------------------------------------------------- 曲目实例 --
    internal sealed class Seg { public Section sec; public double t0; public int i; }
    internal sealed class Channel
    {
        public string name;
        public InstDef def;
        public Spec V;
        public double gain, gl = 1, gr = 1, sg;
        public float[] buf;
        public readonly List<Voice> voices = new List<Voice>();
    }

    // 一个发声单元（采样、乐句、复音音符）：渲染时把单声道输出加到所属声部的缓冲
    internal abstract class Voice
    {
        public double startT, stopT = double.PositiveInfinity;
        // 渲染 n 帧（第 i 帧时间 t0 + i·dt），返回 false 表示已结束
        public abstract bool Render(MusicEngine e, float[] o, int n, double t0);
        public void Stop(double t) { stopT = t; }   // 同 WebAudio：后一次 stop() 取代前一次
    }

    // 预渲染采样的播放（AudioBufferSourceNode → GainNode → 可选低通）
    internal sealed class BufVoice : Voice
    {
        readonly float[] d; readonly double srcRate;
        public readonly Param rate = new Param(1), gain = new Param(1);
        Biquad lp;
        double pos; bool started;
        public double amp;
        public BufVoice(float[] data, double srcRate) { d = data; this.srcRate = srcRate; }
        public void SetLowpass(double f, double sr) { lp = new Biquad(Biquad.LP, f, 0.4, 0, sr); }
        public override bool Render(MusicEngine e, float[] o, int n, double t0)
        {
            double dt = e.dt;
            if (t0 >= stopT) return false;
            if (t0 + n * dt <= startT) return true;
            float[] g = e.s[1], rt = e.s[2];
            gain.Fill(g, n, t0, dt); rate.Fill(rt, n, t0, dt);
            int len = d.Length;
            for (int i = 0; i < n; i++)
            {
                double t = t0 + i * dt;
                if (t < startT) continue;
                if (t >= stopT) return false;
                double r = rt[i] * srcRate;
                if (!started) { started = true; pos = (t - startT) * r; }
                int ip = (int)pos;
                if (ip >= len) return false;
                double fr = pos - ip;
                double x = d[ip] + ((ip + 1 < len ? d[ip + 1] : 0) - d[ip]) * fr;
                double y = x * g[i];
                if (lp != null) y = lp.Tick(y);
                o[i] += (float)y;
                pos += r;
            }
            return true;
        }
    }

    internal sealed class Track
    {
        public readonly MusicEngine eng;
        public readonly Compiled C;
        public readonly string key;
        public readonly bool overlay;
        public readonly Rng rnd;
        readonly Param bus = new Param(1), send = new Param(1), duck = new Param(1);
        readonly List<Biquad> eq = new List<Biquad>();
        readonly Dictionary<string, Channel> channels = new Dictionary<string, Channel>();
        readonly List<Channel> chList = new List<Channel>();
        readonly float[] bL, bR, sL, sR;
        public int segIndex, loopCount;
        public Seg seg;
        public double t0, endTime = double.PositiveInfinity, stopAt = double.PositiveInfinity;
        public bool stopping, finished, unducked;
        readonly double sr;

        public Track(MusicEngine eng, Compiled C, double t0, double fadeIn, bool overlay)
        {
            this.eng = eng; this.C = C; key = C.key; this.overlay = overlay; sr = eng.sr;
            var P = C.piece;
            rnd = new Rng(unchecked(MU.Hash(C.key) + 99));
            double g0 = P.Num("gain", 1);
            int N = MusicEngine.BLOCK;
            bL = new float[N]; bR = new float[N]; sL = new float[N]; sR = new float[N];
            double ls = P.Num("lowShelf", 0), air = P.Num("air", 0);
            if (ls != 0) { eq.Add(new Biquad(Biquad.LS, 160, 1, ls, sr)); eq.Add(new Biquad(Biquad.LS, 160, 1, ls, sr)); }
            if (air != 0) { eq.Add(new Biquad(Biquad.HS, 6000, 1, air, sr)); eq.Add(new Biquad(Biquad.HS, 6000, 1, air, sr)); }
            double s0 = g0 * P.Num("reverb", 1);
            if (fadeIn > 0.01)
            {
                bus.SetValueAtTime(0, t0); bus.LinearRampToValueAtTime(g0, t0 + fadeIn);
                send.SetValueAtTime(0, t0); send.LinearRampToValueAtTime(s0, t0 + fadeIn);
            }
            else { bus.SetValueAtTime(g0, t0); send.SetValueAtTime(s0, t0); }
            this.t0 = t0;
            StartSeg(0, t0);
        }

        public int VoiceCount { get { int n = 0; foreach (var c in chList) n += c.voices.Count; return n; } }

        public Channel GetChannel(string name, bool isDrum)
        {
            Channel ch;
            if (channels.TryGetValue(name, out ch)) return ch;
            Spec V;
            if (!(isDrum ? C.kit : C.voices).TryGetValue(name, out V)) V = new Spec();
            var def = Instruments.Get(V.inst);
            double gain = MU.Def(V.gain, 1) * (def != null ? MU.Def(def.gain, 1) : 1);
            if (eng.stems != null && Array.IndexOf(eng.stems, name) < 0) gain = 0;
            ch = new Channel { name = name, def = def, V = V, gain = gain, buf = new float[MusicEngine.BLOCK] };
            double pv = (MU.Has(V.pan) ? V.pan : (def != null ? MU.Or(def.pan, 0) : 0)) * C.piece.Num("width", MusicEngine.PAN_WIDTH);
            if (pv != 0)
            {
                double p = Math.Max(-0.85, Math.Min(0.85, pv));
                double x = (p + 1) / 2;
                ch.gl = Math.Cos(x * Math.PI / 2); ch.gr = Math.Sin(x * Math.PI / 2);
            }
            ch.sg = MU.Has(V.rev) ? V.rev : (def != null ? MU.Def(def.rev, 0.25) : 0.25);
            channels[name] = ch; chList.Add(ch);
            return ch;
        }

        void StartSeg(int i, double t)
        {
            if (i >= C.form.Count)
            {
                if (!C.loop) { seg = null; endTime = t; return; }
                i = C.loopFrom; loopCount++;
            }
            segIndex = i;
            seg = new Seg { sec = C.sections[C.form[i]], t0 = t, i = 0 };
        }

        public void ScheduleUntil(double horizon, double now)
        {
            if (stopping) return;
            int guard = 0;
            while (seg != null && guard++ < 64)
            {
                var sg = seg; var sec = sg.sec; var items = sec.items;
                while (sg.i < items.Count)
                {
                    var it = items[sg.i];
                    double t = sg.t0 + sec.Time(it.beat);
                    if (t > horizon) return;
                    sg.i++;
                    if (!eng.offline && t < now - 0.08 && it.type != 'p' && it.type != 'y') continue;
                    try { Fire(it, sg, t); } catch (Exception) { }
                }
                double tEnd = sg.t0 + sec.dur;
                if (tEnd > horizon) return;
                StartSeg(segIndex + 1, tEnd);
            }
        }

        public double TAt(Seg sg, double beat) { return sg.t0 + sg.sec.Time(beat); }

        double Human(double beat)
        {
            double meter = C.meter;
            double inBar = beat - Math.Floor(beat / meter + 1e-9) * meter;
            double ib = MU.JsRound(inBar);
            double m;
            if (Math.Abs(inBar - ib) > 1e-6) m = 0.95;
            else if (C.accents != null) { double a = C.accents[(int)ib % C.accents.Length]; m = a != 0 ? a : 1; }
            else m = ib == 0 ? 1.06 : 1.0;
            return m * (1 + (rnd.Next() + rnd.Next() - 1) * 0.06);
        }

        void Fire(Item it, Seg sg, double t)
        {
            var def = Instruments.Get(it.inst);
            if (def == null) return;
            var ch = GetChannel(it.type == 'd' ? it.lane : it.voice, it.type == 'd');
            if (ch.gain == 0) return;
            var r = rnd;
            double jit = (r.Next() + r.Next() - 1) * MU.Def(def.humanT, 0.007);
            double floorT = eng.offline ? 0 : eng.time;
            double tt = Math.Max(floorT, t + jit);
            if (it.type == 'd')
            {
                int nv = Math.Max(1, def.variants);
                int variant = (int)Math.Floor(r.Next() * nv);
                string fk = def.name + "|" + MusicLib.DrumKey(def, it.art, it.p, variant);
                Spec K; C.kit.TryGetValue(it.lane, out K);
                double choke = K != null ? MU.Or(K.choke, double.NaN) : double.NaN;
                PlayBuffer(ch, fk, tt, Math.Max(0.05, it.vel * Human(it.beat)), def, 1, choke, 0.08, double.NaN, null);
                return;
            }
            if (it.type == 's') { PlayPluck(ch, def, it, sg, tt, Math.Max(0.05, it.vel * Human(it.beat))); return; }
            if (it.type == 'y')
            {
                double t1 = TAt(sg, it.beat + it.dur);
                double v = Math.Max(0.05, it.vel * Human(it.beat));
                if (def.poly != null) Instruments.PlayPoly(def.poly, this, ch, it, tt, t1 - t, v, r);
                return;
            }
            if (it.type == 'p')
            {
                var notes = new List<PhNote>(it.notes.Count);
                for (int k = 0; k < it.notes.Count; k++)
                {
                    var n = it.notes[k];
                    double nt = Math.Max(floorT, TAt(sg, n.beat) + (k == 0 ? jit : (r.Next() + r.Next() - 1) * 0.004));
                    var pn = new PhNote { t = nt, d = TAt(sg, n.beat + n.dur) - TAt(sg, n.beat), p = n.p, f = MU.Mtof(n.p), orn = n.orn, gp = n.gp, tp = n.tp };
                    pn.v = Math.Max(0.05, n.vel * Human(n.beat));
                    notes.Add(pn);
                }
                for (int k = 1; k < notes.Count; k++) if (notes[k].t < notes[k - 1].t + 0.01) notes[k].t = notes[k - 1].t + 0.01;
                if (def.legato != null) Instruments.PlayLegato(def.legato, this, ch, notes, r);
                else if (def.overtone) Instruments.PlayOvertone(this, ch, notes, r);
            }
        }

        public void AddVoice(Channel ch, Voice v)
        {
            if (stopping) v.Stop(Math.Min(v.stopT, stopAt));
            ch.voices.Add(v);
        }

        // 播放一个预渲染采样；返回 null = 采样未就绪
        internal BufVoice PlayBuffer(Channel ch, string fk, double t, double vel, InstDef def, double rate, double dur, double release, double lpF, Orn bendOrn, double bendDur = 0, double ring = 0)
        {
            t = Math.Max(t, eng.offline ? 0 : eng.time);
            var smp = MusicLib.GetSample(fk);
            if (smp == null)
            {
                if (eng.offline) { MusicLib.RenderSampleSync(fk); smp = MusicLib.GetSample(fk); }
                else { MusicLib.RequestSample(fk); return null; }
                if (smp == null) return null;
            }
            if (!(rate != 0)) rate = 1;
            var v = new BufVoice(smp.data, smp.sr / eng.sr);
            v.rate.SetValueAtTime(rate, t);
            double curve = MU.Def(def != null ? def.velCurve : double.NaN, 1.5);
            double amp = Math.Pow(Math.Min(1.2, vel), curve);
            v.gain.SetValueAtTime(amp, t);
            if (MU.Has(lpF)) v.SetLowpass(lpF, eng.sr);
            double natural = smp.data.Length / (double)smp.sr / rate;
            double end = t + natural;
            if (MU.Has(dur) && dur != 0 && dur < natural)
            {
                double rel = MU.Or(release, 0.1);
                v.gain.SetValueAtTime(amp, t + dur);
                v.gain.SetTargetAtTime(0, t + dur, rel / 3);
                end = t + dur + rel + 0.02;
            }
            if (bendOrn != null) Bend(v.rate, t, rate, bendOrn, def, bendDur, ring);
            v.startT = t; v.Stop(end);
            v.amp = amp;
            AddVoice(ch, v);
            return v;
        }

        // 弯音（按滑、揉弦、下滑）
        static void Bend(Param param, double t0, double rate, Orn o, InstDef def, double durS, double ring)
        {
            if (o.slide)
            {
                double s = MU.Or(def.slideSemi, 1.6);
                param.SetValueAtTime(rate * Math.Pow(2, -s / 12), t0);
                param.SetValueAtTime(rate * Math.Pow(2, -s / 12), t0 + 0.05);
                param.ExponentialRampToValueAtTime(rate, t0 + 0.2);
            }
            else if (MU.Or(def.attackBend, 0) != 0)
            {
                param.SetValueAtTime(rate * Math.Pow(2, def.attackBend / 1200), t0);
                param.ExponentialRampToValueAtTime(rate, t0 + 0.09);
            }
            if (o.vib != 0)
            {
                double depth = MU.Or(def.vibCents, 22) * o.vib / 1200;
                double rateHz = MU.Or(def.vibRate, 5.5);
                double start = t0 + Math.Min(0.25, durS * 0.3);
                double end = t0 + Math.Min(durS + ring, 4);
                int k = 0;
                for (double tt = start; tt < end; tt += 0.5 / rateHz, k++)
                {
                    double sgn = k % 2 == 1 ? -1 : 1;
                    double amt = Math.Min(1, (tt - start) / 0.3);
                    param.LinearRampToValueAtTime(rate * (1 + sgn * depth * amt * 0.69), tt + 0.25 / rateHz);
                }
                param.LinearRampToValueAtTime(rate, end + 0.05);
            }
            if (o.fall)
            {
                double fs = t0 + durS * 0.55;
                param.SetValueAtTime(rate, fs);
                param.ExponentialRampToValueAtTime(rate * Math.Pow(2, -1.5 / 12), t0 + durS);
            }
        }

        // 拨弦 / 打击类演奏法：扫弦、轮指、倚音、刮奏、按滑 / 揉弦 / 下滑
        void PlayPluck(Channel ch, InstDef def, Item it, Seg sg, double t, double vel)
        {
            var ps = it.ps; var o = it.orn;
            double t1 = TAt(sg, it.beat + it.dur);
            double durS = Math.Max(0.05, t1 - TAt(sg, it.beat));
            double ring = MU.Def(def.ring, 0.5);
            double brightLp = def.velLp != null ? def.velLp[0] + def.velLp[1] * vel : double.NaN;
            var r = rnd;
            Func<double, double> holdFor = d => def.ringAll ? double.NaN : d + ring;
            Func<double, string> fkOf = p => def.name + "|" + MusicLib.SampleKeyPitch(def, p);
            if (it.run != null && it.run.Length > 0)
            {
                int n = it.run.Length;
                double span = Math.Min(0.42, 0.045 * n);
                for (int k = 0; k < n; k++)
                {
                    double tk = t - span + (k * span) / n;
                    PlayBuffer(ch, fkOf(it.run[k]), Math.Max(eng.offline ? 0 : eng.time, tk), vel * (0.35 + 0.45 * k / n), def, 1, holdFor(0.25), 0.4, brightLp, null);
                }
            }
            if (MU.Has(it.gp)) PlayBuffer(ch, fkOf(it.gp), Math.Max(0, t - 0.075), vel * 0.7, def, 1, 0.09, 0.05, brightLp, null);
            bool bend = o.slide || o.vib != 0 || o.fall || MU.Or(def.attackBend, 0) != 0;
            double strum = MU.Def(def.strum, 0.016);
            if (o.trem)
            {
                double rateHz = MU.Or(def.tremRate, 14);
                int n = Math.Max(2, (int)MU.JsRound(durS * rateHz));
                double step = durS / n;
                var prev = new List<BufVoice>();
                for (int k = 0; k < n; k++)
                {
                    double tk = t + k * step + (r.Next() - 0.5) * 0.006;
                    double[] pk = MU.Has(it.tp) && def.trill && k % 2 == 1 ? new[] { it.tp } : ps;
                    double vk = vel * (k == 0 ? 1 : (0.62 + 0.18 * ((k % 4) == 0 ? 1 : (k % 2 == 1 ? 0.2 : 0.6)) + 0.1 * r.Next()));
                    foreach (var pv in prev) { pv.gain.SetTargetAtTime(0, tk, 0.006); pv.Stop(tk + 0.06); }
                    prev.Clear();
                    for (int j = 0; j < pk.Length; j++)
                    {
                        var h = PlayBuffer(ch, fkOf(pk[j]), tk + j * strum * 0.5, vk, def, 1, k == n - 1 ? holdFor(step) : step + 0.08, k == n - 1 ? 0.5 : 0.05, brightLp, null);
                        if (h != null) prev.Add(h);
                    }
                }
                return;
            }
            for (int j = 0; j < ps.Length; j++)
            {
                bool lastNote = j == ps.Length - 1;
                PlayBuffer(ch, fkOf(ps[j]), t + j * strum, vel * (lastNote ? 1 : 0.85), def, 1, holdFor(durS), MU.Or(def.release, 0.35), brightLp,
                    lastNote && bend ? o : null, durS, ring);
            }
        }

        public void SetDuck(double level, double now, double sec)
        {
            double v = duck.ValueAt(now);
            duck.CancelScheduledValues(now);
            duck.SetValueAtTime(v, now);
            duck.LinearRampToValueAtTime(level, now + sec);
        }

        public void Stop(double now, double fade)
        {
            if (stopping) return;
            stopping = true;
            fade = Math.Max(0.02, fade);
            foreach (var g in new[] { bus, send })
            {
                double v = g.ValueAt(now);
                g.CancelScheduledValues(now);
                g.SetValueAtTime(v, now);
                g.LinearRampToValueAtTime(0, now + fade);
            }
            stopAt = now + fade + 0.05;
            foreach (var c in chList) foreach (var v in c.voices) v.Stop(stopAt);
        }

        public bool Dead(double now)
        {
            if (stopping) return now > stopAt + 0.1;
            if (seg == null && endTime < double.PositiveInfinity)
            {
                if (VoiceCount == 0 || now > endTime + 6) { finished = true; return now > endTime; }
            }
            return false;
        }

        public void Render(float[] mixL, float[] mixR, float[] revL, float[] revR, int N, double t0)
        {
            double dt = eng.dt;
            bool any = false;
            foreach (var ch in chList)
            {
                if (ch.voices.Count == 0) continue;
                if (!any) { any = true; Array.Clear(bL, 0, N); Array.Clear(bR, 0, N); Array.Clear(sL, 0, N); Array.Clear(sR, 0, N); }
                var buf = ch.buf;
                Array.Clear(buf, 0, N);
                var vs = ch.voices;
                for (int i = 0; i < vs.Count; i++)
                {
                    if (!vs[i].Render(eng, buf, N, t0)) { vs[i] = vs[vs.Count - 1]; vs.RemoveAt(vs.Count - 1); i--; }
                }
                float gl = (float)(ch.gain * ch.gl), gr = (float)(ch.gain * ch.gr), sgl = (float)(ch.gain * ch.gl * ch.sg), sgr = (float)(ch.gain * ch.gr * ch.sg);
                for (int i = 0; i < N; i++) { float x = buf[i]; bL[i] += x * gl; bR[i] += x * gr; sL[i] += x * sgl; sR[i] += x * sgr; }
            }
            if (!any) { Array.Clear(bL, 0, N); Array.Clear(bR, 0, N); Array.Clear(sL, 0, N); Array.Clear(sR, 0, N); }
            float[] gB = eng.s[7], gS = eng.s[8], gD = eng.s[9];
            bus.Fill(gB, N, t0, dt); send.Fill(gS, N, t0, dt); duck.Fill(gD, N, t0, dt);
            for (int i = 0; i < N; i++) { bL[i] *= gB[i]; bR[i] *= gB[i]; }
            for (int k = 0; k + 1 < eq.Count; k += 2) { eq[k].Run(bL, N); eq[k + 1].Run(bR, N); }
            for (int i = 0; i < N; i++)
            {
                mixL[i] += bL[i] * gD[i]; mixR[i] += bR[i] * gD[i];
                revL[i] += sL[i] * gS[i]; revR[i] += sR[i] * gS[i];
            }
        }
    }

    // 连奏乐句里的一个音（时间已换算成秒）
    internal sealed class PhNote { public double t, d, p, f, v, gp = double.NaN, tp = double.NaN; public Orn orn; }
}
