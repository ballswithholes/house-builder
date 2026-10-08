// 背景音乐 · 乐器（全部程序合成）· 移植自网页版 js/music-inst.js
// 三类乐器：
// 1) 采样型（Sample）：按音高预渲染一次，缓存（MusicLib 采样缓存）。
//    - 拨弦：扩展 Karplus-Strong（三角 + 滤波噪声激励、拨弦位置梳状、一阶全通微调音高、环路低通、多弦组微失谐、琴体共鸣峰、贾瓦里蜂鸣）
//    - 打击：模态合成（非谐分音 + 击打噪声 + 音高滑移）/ 膜鸣鼓
// 2) 连奏型（Legato）：单音乐句实时合成——PeriodicWave 振荡器 + 颤音 LFO（音分）+ 滑音 / 倚音 / 颤音的频率自动化
//    + 弓噪 / 气声（带通噪声，可随音高）+ 共鸣峰滤波链
// 3) 复音型（Poly）：弦乐垫音、低音弦乐、笙、合唱、铜管组、持续低音、喉音低吟、合成低音
// 每种乐器的 gain / pan / rev 是缺省混音参数，可被乐曲的声部设置覆盖；gain 最后由混音校准表 CAL 覆盖（同 JS）。
// 不依赖 UnityEngine（Tests~/audio 无头编译）。
using System;
using System.Collections.Generic;

namespace Sanguo.Audio
{
    public enum InstType { Sample, Legato, Poly }

    public sealed class RenderArgs
    {
        public int sr, art, variant, seed;
        public double pitch = double.NaN, freq;
    }

    // 乐器定义。数值字段 NaN 表示 JS 里的 undefined。
    public sealed class InstDef
    {
        public string name, label;
        public InstType type;
        public int sr = 32000;
        public double gain = double.NaN, rev = double.NaN, pan = double.NaN, ring = double.NaN, attackBend = double.NaN, vibCents = double.NaN,
            vibRate = double.NaN, tremRate = double.NaN, slideSemi = double.NaN, strum = double.NaN, release = double.NaN, humanT = double.NaN, velCurve = double.NaN;
        public double[] velLp;
        public bool ringAll, pitchless, trill;
        public int variants = 1;
        public Func<RenderArgs, float[]> render;
        public LegatoSpec legato;
        public PolySpec poly;
        public bool overtone;
    }

    // 连奏乐句参数（legato(P)）
    public sealed class LegatoSpec
    {
        public string wave; public Func<int, double> harm; public int nh;
        public double level, attack = double.NaN, release = double.NaN, dip = double.NaN, port = double.NaN, bigSlide = double.NaN,
            vibRate = double.NaN, vib = double.NaN, vibDeep = double.NaN, vibDelay = double.NaN, trem = double.NaN, drift = double.NaN,
            trillRate = double.NaN, scoop = double.NaN, autoGrace = double.NaN, detune2 = double.NaN, mix2 = double.NaN, velCurve = double.NaN;
        public bool shape = true;
        public NoiseSpec noise;
        public double[][] filters;   // [type, f, Q, gain]：type 0 lowpass 1 highpass 2 bandpass 3 peaking
        public BrightSpec bright;
    }
    public sealed class NoiseSpec { public double amp, f = double.NaN, q = double.NaN, track = double.NaN, chiff = double.NaN; public bool post; }
    public sealed class BrightSpec { public double bas, vel, q = double.NaN, add = double.NaN; }

    // 复音参数（poly(P)）：oscs [类型或波形, 音分, 频率倍数?, 增益?]
    public sealed class PolyOsc { public string type; public string wave; public Func<int, double> harm; public double cents, mult = double.NaN, gain = double.NaN; }
    public sealed class PolySpec
    {
        public PolyOsc[] oscs; public Func<double, double, double> lp; public double q = double.NaN, attack, release, level, vib = double.NaN, vibRate = double.NaN,
            sustain = double.NaN, velCurve = double.NaN;
        public double[][] formants;   // [中心, 增益, 带宽]
        public bool lpEnv;
    }

    public static partial class Instruments
    {
        const double TAU = Math.PI * 2;
        static readonly Dictionary<string, InstDef> INST = new Dictionary<string, InstDef>();
        static readonly List<string> names = new List<string>();
        public static InstDef Get(string name) { InstDef d; return name != null && INST.TryGetValue(name, out d) ? d : null; }
        public static List<string> Names { get { return names; } }
        static InstDef Def(string name, InstDef d) { d.name = name; if (!INST.ContainsKey(name)) names.Add(name); INST[name] = d; return d; }

        // ===================================================== 采样渲染工具 ==
        const int SIN_N = 8192;
        static readonly float[] SIN = MakeSin();
        static float[] MakeSin() { var s = new float[SIN_N + 1]; for (int i = 0; i <= SIN_N; i++) s[i] = (float)Math.Sin(TAU * i / SIN_N); return s; }

        // RBJ 双二阶滤波系数（Q 线性；与 WebAudio 节点不同，这是 JS 端采样渲染用的）
        static double[] Biq(string type, double f, double sr, double q, double gainDb)
        {
            double w = TAU * Math.Min(f, sr * 0.45) / sr, cw = Math.Cos(w), sw = Math.Sin(w);
            double al = sw / (2 * MU.Or(q, 0.707));
            double A = Math.Pow(10, MU.Or(gainDb, 0) / 40);
            double b0, b1, b2, a0, a1, a2;
            switch (type)
            {
                case "lp": b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
                case "hp": b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
                case "bp": b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
                default: b0 = 1 + al * A; b1 = -2 * cw; b2 = 1 - al * A; a0 = 1 + al / A; a1 = -2 * cw; a2 = 1 - al / A; break;
            }
            return new[] { b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0 };
        }
        static float[] Filt(float[] d, double[] c)
        {
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = c[0], b1 = c[1], b2 = c[2], a1 = c[3], a2 = c[4];
            for (int i = 0; i < d.Length; i++)
            {
                double x = d[i];
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                d[i] = (float)y;
            }
            return d;
        }
        static float[] DcBlock(float[] d, double sr, double fc)
        {
            double R = Math.Exp(-TAU * MU.Or(fc, 25) / sr), x1 = 0, y1 = 0;
            for (int i = 0; i < d.Length; i++) { double y = d[i] - x1 + R * y1; x1 = d[i]; y1 = y; d[i] = (float)y; }
            return d;
        }
        static float[] Normalize(float[] d, double peak)
        {
            double m = 0;
            for (int i = 0; i < d.Length; i++) { double a = Math.Abs(d[i]); if (a > m) m = a; }
            if (m > 1e-9) { double g = peak / m; for (int i = 0; i < d.Length; i++) d[i] = (float)(d[i] * g); }
            return d;
        }
        static float[] FadeOut(float[] d, double sr, double sec)
        {
            int n = (int)Math.Min(d.Length, MU.JsRound(sr * sec));
            for (int i = 0; i < n; i++) d[d.Length - 1 - i] = (float)(d[d.Length - 1 - i] * ((double)i / n));
            return d;
        }
        static float[] TrimSilence(float[] d, double sr)
        {
            int last = d.Length - 1;
            const double th = 2e-4;
            while (last > sr * 0.05 && Math.Abs(d[last]) < th) last--;
            int n = (int)Math.Min(d.Length, last + MU.JsRound(sr * 0.02));
            if (n < d.Length) { var c = new float[n]; Array.Copy(d, c, n); return FadeOut(c, sr, 0.02); }
            return d;
        }
        static double R(Rng r) { return r.Next(); }

        // ===================================================== Karplus-Strong 拨弦 ==
        public sealed class KS
        {
            public double emph, t60, t60k = double.NaN, damp, pick = double.NaN, bright = double.NaN, tri = double.NaN, noise = double.NaN, click, buzz, dur = double.NaN, durK, lpf, hpf;
            public double[][] body, courses;
            public int sr;
        }
        static float[] KsRender(RenderArgs a, KS o)
        {
            double sr = a.sr, f = a.freq;
            double dur = Math.Min(MU.Or(o.dur, 2), o.durK != 0 ? o.durK * Math.Pow(220 / f, 0.5) : 99);
            int N = (int)MU.JsRound(sr * Math.Max(0.2, dur));
            var outp = new float[N];
            var r = new Rng(a.seed);
            var courses = o.courses ?? new[] { new double[] { 0, 1 } };
            double t60 = Math.Max(0.15, Math.Min(12, o.t60 * Math.Pow(220 / f, MU.Def(o.t60k, 0.5))));
            for (int cI = 0; cI < courses.Length; cI++)
            {
                double cf = f * Math.Pow(2, courses[cI][0] / 1200);
                double P = sr / cf, da = o.damp, w = TAU * cf / sr;
                double pd = Math.Atan2(da * Math.Sin(w), 1 - da * Math.Cos(w)) / w;
                double mag = (1 - da) / Math.Sqrt(1 - 2 * da * Math.Cos(w) + da * da);
                double total = P - pd;
                int L = Math.Max(2, (int)Math.Floor(total - 0.5));
                double dl = total - L, C = (1 - dl) / (1 + dl);
                double g = Math.Min(0.99998, Math.Pow(10, -3 / (t60 * cf)) / mag);
                var buf = new float[L];
                int pk = Math.Max(1, (int)MU.JsRound(MU.Or(o.pick, 0.15) * L));
                var nz = new float[L];
                double lp = 0, kb = MU.Def(o.bright, 0.6);
                for (int i = 0; i < L; i++) { lp += (R(r) * 2 - 1 - lp) * kb; nz[i] = (float)lp; }
                double mean = 0, triA = MU.Def(o.tri, 0.6), noiseA = MU.Def(o.noise, 0.4);
                for (int i = 0; i < L; i++)
                {
                    double tri = i < pk ? (double)i / pk : (double)(L - i) / (L - pk);
                    double nn = nz[i] - (i >= pk ? nz[i - pk] : 0);
                    buf[i] = (float)(triA * tri + noiseA * nn);
                    mean += buf[i];
                }
                mean /= L;
                double peak0 = 0;
                for (int i = 0; i < L; i++) { buf[i] = (float)(buf[i] - mean); peak0 = Math.Max(peak0, Math.Abs(buf[i])); }
                double buzzTh = o.buzz != 0 ? o.buzz * peak0 : 0;
                double cg = courses[cI][1];
                int idx = 0; double apx = 0, apy = 0, lpy = 0;
                for (int n = 0; n < N; n++)
                {
                    double x = buf[idx];
                    double ap = C * x + apx - C * apy; apx = x; apy = ap;
                    lpy = (1 - da) * ap + da * lpy;
                    double y = g * lpy;
                    if (buzzTh != 0 && y > buzzTh) y = buzzTh + (y - buzzTh) * 0.12;
                    buf[idx] = (float)y;
                    outp[n] = (float)(outp[n] + x * cg);
                    if (++idx == L) idx = 0;
                }
            }
            if (o.click != 0)
            {
                int cn = (int)MU.JsRound(sr * 0.006);
                var c = new float[cn];
                for (int i = 0; i < cn; i++) c[i] = (float)((R(r) * 2 - 1) * Math.Exp(-i / (cn * 0.25)));
                Filt(c, Biq("hp", 2500, sr, 0.7, 0));
                double pk = 0; for (int i = 0; i < 256 && i < N; i++) pk = Math.Max(pk, Math.Abs(outp[i]));
                for (int i = 0; i < cn && i < N; i++) outp[i] = (float)(outp[i] + c[i] * o.click * (pk != 0 ? pk : 1));
            }
            if (o.emph != 0) { double x1 = 0; for (int i = 0; i < N; i++) { double x = outp[i]; outp[i] = (float)(x - o.emph * x1); x1 = x; } }
            if (o.body != null) foreach (var b in o.body) Filt(outp, Biq("pk", b[0], sr, b[2], b[1]));
            if (o.lpf != 0) Filt(outp, Biq("lp", o.lpf, sr, 0.6, 0));
            DcBlock(outp, sr, MU.Or(o.hpf, 30));
            Normalize(outp, 0.9);
            return TrimSilence(FadeOut(outp, sr, 0.05), sr);
        }

        // =========================================================== 模态合成 ==
        // partials：[频率比, 振幅, t60, 起音秒?]；glide = [比例, 时间常数]
        public sealed class Modal
        {
            public double dur, attack, jitter, fvar, ampVar, f0, hpf, lpf, norm;
            public double[][] partials; public double[] glide, strike;
            public int sr;
        }
        static float[] ModalRender(RenderArgs a, Modal o)
        {
            double sr = a.sr;
            double f0 = (a.freq != 0 ? a.freq : o.f0) * (o.fvar != 0 ? 1 + (new Rng(unchecked(a.seed + 7)).Next() - 0.5) * o.fvar : 1);
            int N = (int)MU.JsRound(sr * o.dur);
            var outp = new float[N];
            var r = new Rng(a.seed);
            var gl = o.glide;
            double kg = gl != null ? Math.Exp(-1 / (gl[1] * sr)) : 1;
            foreach (var p in o.partials)
            {
                double fr = f0 * p[0] * (o.jitter != 0 ? 1 + (R(r) - 0.5) * o.jitter : 1);
                if (fr >= sr * 0.48) continue;
                double amp = p[1] * (o.ampVar != 0 ? 1 + (R(r) - 0.5) * o.ampVar : 1);
                double k = Math.Exp(-6.9 / (p[2] * sr));
                double attS = (p.Length > 3 && p[3] != 0) ? p[3] : MU.Or(o.attack, 0.0005);
                int att = Math.Max(1, (int)MU.JsRound(attS * sr));
                double ph = R(r), e = amp, gd = gl != null ? gl[0] : 0;
                double inc0 = fr / sr;
                for (int n = 0; n < N; n++)
                {
                    double inc = gl != null ? inc0 * (1 + gd) : inc0;
                    if (gl != null) gd *= kg;
                    ph += inc; if (ph >= 1) ph -= 1;
                    double x = ph * SIN_N; int xi = (int)x;
                    double s = SIN[xi] + (SIN[xi + 1] - SIN[xi]) * (x - xi);
                    outp[n] = (float)(outp[n] + s * e * (n < att ? (double)n / att : 1));
                    e *= k;
                    if (e < 1e-5 && n > att) break;
                }
            }
            if (o.strike != null)
            {
                var s = o.strike;
                int n = (int)Math.Min(N, MU.JsRound(sr * s[3] * 6));
                var c = new float[n];
                for (int i = 0; i < n; i++) c[i] = (float)((R(r) * 2 - 1) * Math.Exp(-i / (sr * s[3])));
                Filt(c, Biq("bp", s[1], sr, s[2], 0));
                for (int i = 0; i < n; i++) outp[i] = (float)(outp[i] + c[i] * s[0] * 4);
            }
            if (o.hpf != 0) Filt(outp, Biq("hp", o.hpf, sr, 0.7, 0));
            if (o.lpf != 0) Filt(outp, Biq("lp", o.lpf, sr, 0.7, 0));
            DcBlock(outp, sr, 20);
            Normalize(outp, MU.Or(o.norm, 0.9));
            return TrimSilence(FadeOut(outp, sr, 0.04), sr);
        }

        // =========================================================== 膜鸣鼓 ==
        public sealed class Drum
        {
            public double f0, bend, tau, t60, dur, thump, fvar = double.NaN, lpf;
            public double[][] modes, peaks; public double[] skin, click, jingle, rise;
        }
        static float[] DrumRender(RenderArgs a, Drum o)
        {
            double sr = a.sr;
            var r = new Rng(a.seed);
            int vv = a.variant;
            double f0 = (a.freq != 0 ? a.freq : o.f0) * (1 + (R(r) - 0.5) * MU.Def(o.fvar, 0.04));
            int N = (int)MU.JsRound(sr * o.dur);
            var outp = new float[N];
            var parts = new List<double[]> { new[] { 1, 1, o.t60 } };
            if (o.modes != null) parts.AddRange(o.modes);
            double kTau = Math.Exp(-1 / (MU.Or(o.tau, 0.03) * sr));
            double kRise = o.rise != null ? Math.Exp(-1 / (o.rise[1] * sr)) : 1;
            foreach (var p in parts)
            {
                double k = Math.Exp(-6.9 / (p[2] * sr));
                double e = p[1] * (1 + (R(r) - 0.5) * 0.12), ph = R(r) * 0.25, b = MU.Or(o.bend, 1) - 1, rs = o.rise != null ? o.rise[0] : 0;
                int att = (int)MU.JsRound(sr * 0.0008);
                for (int n = 0; n < N; n++)
                {
                    double fr = f0 * p[0] * (1 + b) * (1 + (o.rise != null ? o.rise[0] - rs : 0));
                    b *= kTau; if (o.rise != null) rs *= kRise;
                    ph += fr / sr; if (ph >= 1) ph -= 1;
                    double x = ph * SIN_N; int xi = (int)x;
                    outp[n] = (float)(outp[n] + (SIN[xi] + (SIN[xi + 1] - SIN[xi]) * (x - xi)) * e * (n < att ? (double)n / att : 1));
                    e *= k;
                    if (e < 1e-5) break;
                }
            }
            if (o.skin != null)
            {
                var s = o.skin;
                int n = (int)Math.Min(N, MU.JsRound(sr * s[3] * 7));
                var c = new float[n];
                for (int i = 0; i < n; i++) c[i] = (float)((R(r) * 2 - 1) * Math.Exp(-i / (sr * s[3])));
                Filt(c, Biq("bp", s[1] * (1 + (R(r) - 0.5) * 0.1), sr, s[2], 0));
                for (int i = 0; i < n; i++) outp[i] = (float)(outp[i] + c[i] * s[0] * 3);
            }
            if (o.thump != 0)
            {
                int n = (int)Math.Min(N, MU.JsRound(sr * 0.12));
                var c = new float[n];
                for (int i = 0; i < n; i++) c[i] = (float)((R(r) * 2 - 1) * Math.Exp(-i / (sr * 0.02)));
                Filt(c, Biq("lp", 180, sr, 0.7, 0)); Filt(c, Biq("lp", 180, sr, 0.7, 0));
                for (int i = 0; i < n; i++) outp[i] = (float)(outp[i] + c[i] * o.thump * 6);
            }
            if (o.click != null)
            {
                int n = (int)Math.Min(N, MU.JsRound(sr * o.click[1] * 6));
                var c = new float[n];
                for (int i = 0; i < n; i++) c[i] = (float)((R(r) * 2 - 1) * Math.Exp(-i / (sr * o.click[1])));
                Filt(c, Biq("hp", o.click.Length > 2 && o.click[2] != 0 ? o.click[2] : 2000, sr, 0.7, 0));
                for (int i = 0; i < n; i++) outp[i] = (float)(outp[i] + c[i] * o.click[0]);
            }
            if (o.jingle != null)
            {
                var j = o.jingle;
                int n = (int)Math.Min(N, MU.JsRound(sr * j[1] * 6));
                var c = new float[n];
                for (int i = 0; i < n; i++) { double t = i / sr; c[i] = (float)((R(r) * 2 - 1) * Math.Exp(-t / j[1]) * (0.6 + 0.4 * Math.Sin(TAU * 31 * t + vv))); }
                Filt(c, Biq("bp", 7200, sr, 1.4, 0)); Filt(c, Biq("hp", 3500, sr, 0.7, 0));
                for (int i = 0; i < n; i++) outp[i] = (float)(outp[i] + c[i] * j[0] * 5);
            }
            if (o.lpf != 0) Filt(outp, Biq("lp", o.lpf, sr, 0.7, 0));
            if (o.peaks != null) foreach (var b in o.peaks) Filt(outp, Biq("pk", b[0], sr, b[2], b[1]));
            DcBlock(outp, sr, 20);
            Normalize(outp, 0.9);
            return TrimSilence(FadeOut(outp, sr, 0.02), sr);
        }

        // ---- 便捷定义
        static InstDef Ks(string name, KS o, InstDef extra)
        {
            extra.type = InstType.Sample;
            if (extra.sr == 32000 && o.sr != 0) extra.sr = o.sr;
            o.sr = extra.sr;
            extra.render = a => KsRender(a, o);
            return Def(name, extra);
        }
        static InstDef ModalI(string name, Modal o, InstDef extra)
        {
            extra.type = InstType.Sample;
            if (o.sr != 0) extra.sr = o.sr;
            extra.render = a => ModalRender(a, o);
            return Def(name, extra);
        }
        // 多种击法（art）的打击乐器：arts[art] = Modal 或 Drum
        static InstDef Kit(string name, object[] arts, InstDef extra)
        {
            extra.type = InstType.Sample;
            if (extra.variants == 1 && !kitVariantsSet) extra.variants = 3;
            kitVariantsSet = false;
            extra.pitchless = true;
            extra.render = a =>
            {
                object o = a.art >= 0 && a.art < arts.Length && arts[a.art] != null ? arts[a.art] : arts[0];
                return o is Modal ? ModalRender(a, (Modal)o) : DrumRender(a, (Drum)o);
            };
            return Def(name, extra);
        }
        static bool kitVariantsSet;
        static InstDef V(int variants, InstDef d) { d.variants = variants; kitVariantsSet = true; return d; }
        static Modal Wood(double f1, double f2, double d1)
        {
            double dd = MU.Or(d1, 0.06);
            return new Modal { dur = 0.22, attack = 0.0003, partials = new[] { P(1, 1, dd), P(f2 / f1, 0.5, dd * 0.6), P(3.9, 0.15, 0.02) }, f0 = f1, strike = new double[] { 0.25, 4000, 1, 0.003 }, hpf = 300 };
        }
        static double[] P(params double[] v) { return v; }
        static double[][] PP(params double[][] v) { return v; }

        // ================================================================ 定义 ==
        static Instruments()
        {
            DefineSamples();
            DefineRealtime();
            // ------------------------------------------------------------ 混音校准 --
            // 由 tests/music.html 的试听片段测得（未经压缩的原始电平）：旋律目标 RMS −23 dBFS，垫音 −26，拨弦峰值 −9，鼓峰值 −6，锣 −9。
            var CAL = new Dictionary<string, double>
            {
                { "aulos", 3.3 }, { "ban", 0.44 }, { "bangzi", 0.41 }, { "bansuri", 0.67 }, { "bayan", 0.36 }, { "bianzhong", 0.38 }, { "bo", 0.41 }, { "bodhran", 0.26 },
                { "bonang", 0.4 }, { "buk", 0.45 }, { "cello", 0.67 }, { "chanter", 3.7 }, { "choir", 0.56 }, { "choirOo", 0.3 }, { "cornu", 0.87 }, { "crotala", 0.35 },
                { "daegeum", 0.84 }, { "daf", 0.43 }, { "dagu", 0.5 }, { "darbuka", 0.39 }, { "dayan", 0.5 }, { "dizi", 1.16 }, { "dombra", 0.44 }, { "drones", 3.05 },
                { "erhu", 0.76 }, { "frame", 0.38 }, { "gayageum", 0.38 }, { "gender", 0.37 }, { "gongageng", 0.34 }, { "haegeum", 1.02 }, { "harp", 0.34 }, { "hoof", 0.44 },
                { "horn", 0.7 }, { "horns", 0.84 }, { "janggu", 0.39 }, { "jing", 0.21 }, { "kamancheh", 0.71 }, { "kargyraa", 0.73 }, { "kempul", 0.39 }, { "kendang", 0.39 },
                { "kenong", 0.38 }, { "kkwaeng", 0.43 }, { "koto", 0.43 }, { "lowstr", 0.61 }, { "luo", 0.33 }, { "morin", 0.79 }, { "muyu", 0.36 }, { "ney", 0.67 },
                { "oud", 0.47 }, { "overtone", 0.77 }, { "pipa", 0.44 }, { "piri", 3.39 }, { "pizz", 0.41 }, { "qanun", 0.52 }, { "qin", 0.38 }, { "ruan", 0.43 },
                { "santur", 0.51 }, { "saron", 0.35 }, { "shakuhachi", 0.58 }, { "shamandrum", 0.37 }, { "sheng", 2.01 }, { "shinobue", 1.97 }, { "sitar", 0.5 }, { "strings", 0.85 },
                { "sub", 0.21 }, { "suling", 1.0 }, { "suona", 2.88 }, { "taiko", 0.41 }, { "tamtam", 0.23 }, { "tanggu", 0.4 }, { "tanpura", 0.48 }, { "timpani", 0.62 },
                { "wardrum", 0.39 }, { "whistle", 0.77 }, { "xiao", 0.58 }, { "xiaoluo", 0.35 }, { "yangqin", 0.48 }, { "zheng", 0.46 }, { "zurna", 3.25 },
            };
            foreach (var kv in CAL) { var d = Get(kv.Key); if (d != null) d.gain = kv.Value; }
        }

        static void DefineSamples()
        {
            // ================================================================ 拨弦 ==
            Ks("zheng", new KS { emph = 0.6, t60 = 3.4, t60k = 0.45, damp = 0.1, pick = 0.12, bright = 0.72, tri = 0.55, noise = 0.5, click = 0.18,
                body = PP(P(180, 4, 1.1), P(420, 2.5, 1.4), P(1250, 2, 1.2), P(3300, 3, 1.4)), dur = 3.4 },
                new InstDef { gain = 0.5, rev = 0.32, ring = 1.0, attackBend = 7, vibCents = 24, vibRate = 5.6, tremRate = 13, slideSemi = 2, velLp = P(3000, 9000), label = "古筝" });
            Ks("pipa", new KS { emph = 0.7, t60 = 1.25, t60k = 0.4, damp = 0.06, pick = 0.085, bright = 0.9, tri = 0.35, noise = 0.7, click = 0.3,
                body = PP(P(270, 4, 1.4), P(900, 2, 1.2), P(2500, 4, 1.8)), dur = 2.0 },
                new InstDef { gain = 0.46, rev = 0.24, ring = 0.3, attackBend = 6, tremRate = 17, strum = 0.012, vibCents = 18, slideSemi = 1.5, velLp = P(3400, 9000), label = "琵琶" });
            Ks("qin", new KS { emph = 0.2, t60 = 6, t60k = 0.35, damp = 0.36, pick = 0.17, bright = 0.35, tri = 0.85, noise = 0.2, click = 0.04,
                body = PP(P(110, 4, 0.9), P(320, 3, 1.1), P(900, -2, 1)), dur = 4.5 },
                new InstDef { gain = 0.62, rev = 0.4, ring = 2.0, vibCents = 18, vibRate = 4.2, slideSemi = 2, label = "古琴" });
            Ks("ruan", new KS { emph = 0.35, t60 = 2.0, t60k = 0.4, damp = 0.22, pick = 0.2, bright = 0.5, tri = 0.7, noise = 0.35, click = 0.1,
                body = PP(P(140, 4, 1), P(600, 2, 1.1), P(2000, -2, 1)), dur = 2.6 },
                new InstDef { gain = 0.5, rev = 0.22, ring = 0.4, strum = 0.014, tremRate = 12, label = "中阮" });
            Ks("yangqin", new KS { emph = 0.5, t60 = 2.8, t60k = 0.4, damp = 0.05, pick = 0.11, bright = 0.95, tri = 0.25, noise = 0.45, click = 0.35,
                courses = PP(P(-2, 1), P(0, 1), P(2.5, 0.9)), body = PP(P(320, 2, 1), P(2100, 3, 1.4)), dur = 3 },
                new InstDef { gain = 0.4, rev = 0.3, ring = 1.0, tremRate = 12, label = "扬琴" });
            Ks("koto", new KS { emph = 0.7, t60 = 1.9, t60k = 0.45, damp = 0.09, pick = 0.075, bright = 0.85, tri = 0.45, noise = 0.6, click = 0.28,
                body = PP(P(250, 3, 1.2), P(1500, 4, 1.4), P(3500, 2, 2)), dur = 2.6 },
                new InstDef { gain = 0.5, rev = 0.32, ring = 0.6, attackBend = 10, vibCents = 26, vibRate = 5, slideSemi = 1, tremRate = 12, velLp = P(3200, 9000), label = "筝（日本）" });
            Ks("gayageum", new KS { emph = 0.35, t60 = 2.5, t60k = 0.5, damp = 0.3, pick = 0.2, bright = 0.48, tri = 0.8, noise = 0.3, click = 0.06,
                body = PP(P(200, 4, 1), P(700, 3, 1.2), P(1800, 1, 1)), dur = 3.2 },
                new InstDef { gain = 0.6, rev = 0.32, ring = 0.8, attackBend = 5, vibCents = 42, vibRate = 4.4, slideSemi = 1.5, label = "伽倻琴" });
            Ks("harp", new KS { emph = 0.3, t60 = 2.3, t60k = 0.5, damp = 0.28, pick = 0.25, bright = 0.45, tri = 0.85, noise = 0.2, click = 0.05,
                body = PP(P(220, 3, 1), P(900, 2, 1)), dur = 3.2 },
                new InstDef { gain = 0.55, rev = 0.36, ring = 1.2, strum = 0.03, label = "竖琴 / 里拉琴" });
            Ks("oud", new KS { emph = 0.45, t60 = 0.95, t60k = 0.3, damp = 0.17, pick = 0.12, bright = 0.7, tri = 0.5, noise = 0.6, click = 0.3,
                courses = PP(P(0, 1), P(2.5, 0.8)), body = PP(P(110, 5, 1), P(240, 4, 1.2), P(1100, 2, 1), P(3000, -3, 1)), dur = 1.8 },
                new InstDef { gain = 0.55, rev = 0.25, ring = 0.2, tremRate = 15, strum = 0.012, slideSemi = 1, vibCents = 16, label = "乌德琴" });
            Ks("santur", new KS { emph = 0.5, t60 = 2.6, t60k = 0.4, damp = 0.05, pick = 0.1, bright = 0.95, tri = 0.2, noise = 0.45, click = 0.4,
                courses = PP(P(-2, 1), P(0, 1), P(1.5, 1), P(3, 0.9)), body = PP(P(300, 2, 1), P(2000, 3, 1.5)), dur = 3 },
                new InstDef { gain = 0.38, rev = 0.32, ring = 1.0, tremRate = 12, label = "桑图尔" });
            Ks("qanun", new KS { emph = 0.55, t60 = 1.7, t60k = 0.45, damp = 0.08, pick = 0.1, bright = 0.85, tri = 0.4, noise = 0.5, click = 0.3,
                courses = PP(P(-1.5, 1), P(0, 1), P(1.5, 1)), body = PP(P(250, 3, 1), P(1200, 2, 1)), dur = 2.4 },
                new InstDef { gain = 0.42, rev = 0.3, ring = 0.6, tremRate = 14, label = "卡龙琴" });
            Ks("sitar", new KS { emph = 0.55, t60 = 3.2, t60k = 0.4, damp = 0.09, pick = 0.06, bright = 0.9, tri = 0.4, noise = 0.5, click = 0.25, buzz = 0.22,
                body = PP(P(350, 3, 1), P(1800, 4, 1.5), P(4200, 2, 2)), dur = 3.2 },
                new InstDef { gain = 0.42, rev = 0.32, ring = 1.0, slideSemi = 2, vibCents = 30, vibRate = 4.8, label = "西塔尔" });
            Ks("tanpura", new KS { emph = 0.4, t60 = 6, t60k = 0.2, damp = 0.12, pick = 0.15, bright = 0.7, tri = 0.6, noise = 0.4, click = 0.05, buzz = 0.16,
                body = PP(P(180, 3, 1), P(1500, 3, 1.2)), dur = 5 },
                new InstDef { gain = 0.42, rev = 0.38, ringAll = true, label = "坦布拉（持续音）" });
            Ks("dombra", new KS { emph = 0.6, t60 = 0.75, t60k = 0.3, damp = 0.1, pick = 0.1, bright = 0.85, tri = 0.4, noise = 0.6, click = 0.3,
                body = PP(P(180, 4, 1), P(800, 3, 1.2), P(2600, 2, 1.5)), dur = 1.3 },
                new InstDef { gain = 0.48, rev = 0.22, ring = 0.15, strum = 0.01, tremRate = 14, label = "冬不拉 / 托布秀尔" });
            Ks("pizz", new KS { emph = 0.2, t60 = 0.6, t60k = 0.25, damp = 0.3, pick = 0.2, bright = 0.45, tri = 0.8, noise = 0.25, click = 0.05,
                body = PP(P(100, 4, 1), P(400, 2, 1)), dur = 1.3, sr = 24000 },
                new InstDef { gain = 0.62, rev = 0.25, ring = 0.12, sr = 24000, label = "弦乐拨奏 / 低音" });

            // ============================================================ 定音打击 ==
            ModalI("bianzhong", new Modal { dur = 3.6, attack = 0.001, jitter = 0.004, partials = PP(
                P(0.9985, 1, 2.8), P(1.0015, 0.35, 2.4), P(1.19, 0.16, 1.9), P(2.42, 0.5, 1.3), P(2.93, 0.24, 1.0), P(4.16, 0.22, 0.6), P(5.43, 0.12, 0.42), P(6.8, 0.08, 0.3)),
                strike = P(0.12, 2800, 1.2, 0.008) }, new InstDef { gain = 0.42, rev = 0.45, ring = 2.5, label = "编钟" });
            ModalI("timpani", new Modal { sr = 22050, dur = 2.6, attack = 0.002, glide = P(0.03, 0.06), partials = PP(
                P(1, 1, 2.2), P(1.5, 0.45, 1.4), P(1.99, 0.3, 1.0), P(2.44, 0.15, 0.7), P(2.9, 0.1, 0.5), P(0.52, 0.18, 0.4)),
                strike = P(0.35, 400, 0.8, 0.012) }, new InstDef { gain = 0.6, rev = 0.3, ring = 2, label = "定音鼓" });
            ModalI("saron", new Modal { dur = 2.4, attack = 0.0008, partials = PP(
                P(0.994, 1, 2.2), P(1.006, 0.8, 2.2), P(2.76, 0.28, 0.8), P(2.79, 0.2, 0.8), P(5.4, 0.1, 0.3)), strike = P(0.25, 3500, 1, 0.006) },
                new InstDef { gain = 0.42, rev = 0.36, ring = 1.6, label = "萨隆" });
            ModalI("gender", new Modal { dur = 3.6, attack = 0.003, partials = PP(
                P(0.996, 1, 3.4), P(1.004, 0.7, 3.4), P(2.83, 0.12, 1.1), P(4.1, 0.05, 0.5)), strike = P(0.05, 1200, 1, 0.01) },
                new InstDef { gain = 0.48, rev = 0.4, ring = 2.2, label = "金德尔" });
            ModalI("bonang", new Modal { dur = 1.9, attack = 0.001, partials = PP(
                P(0.997, 1, 1.5), P(1.003, 0.6, 1.5), P(1.51, 0.25, 0.8), P(2.02, 0.35, 0.9), P(2.9, 0.2, 0.5), P(3.95, 0.1, 0.3)), strike = P(0.18, 2600, 1, 0.006) },
                new InstDef { gain = 0.4, rev = 0.34, ring = 1.0, label = "博南" });
            ModalI("kenong", new Modal { dur = 3.2, attack = 0.002, partials = PP(
                P(0.9975, 1, 2.6), P(1.0025, 0.7, 2.6), P(1.52, 0.2, 1.4), P(2.03, 0.3, 1.2), P(3.0, 0.12, 0.6)), strike = P(0.1, 1800, 1, 0.008) },
                new InstDef { gain = 0.42, rev = 0.4, ring = 2.0, label = "克农" });
            ModalI("kempul", new Modal { sr = 24000, dur = 4.5, attack = 0.004, partials = PP(
                P(0.994, 1, 4.0), P(1.006, 0.8, 4.0), P(1.47, 0.35, 2.2), P(2.08, 0.3, 1.8), P(2.74, 0.2, 1.2), P(3.6, 0.1, 0.8)), strike = P(0.06, 900, 1, 0.01) },
                new InstDef { gain = 0.5, rev = 0.4, ring = 3, label = "肯普尔" });
            ModalI("gongageng", new Modal { sr = 22050, dur = 7, attack = 0.02, partials = PP(
                P(0.9925, 1, 7.5), P(1.0075, 0.85, 7.5), P(1.46, 0.3, 4.5), P(2.02, 0.35, 4), P(2.48, 0.18, 3), P(2.95, 0.15, 2.4), P(4.1, 0.08, 1.6)), strike = P(0.03, 300, 1, 0.02) },
                new InstDef { gain = 0.7, rev = 0.45, ring = 5, label = "甘美兰大锣" });
            ModalI("dayan", new Modal { dur = 1.1, attack = 0.0006, partials = PP(
                P(1, 1, 0.9), P(2, 0.6, 0.7), P(3, 0.45, 0.55), P(4, 0.28, 0.4), P(5, 0.18, 0.3)), strike = P(0.25, 3200, 1, 0.004) },
                new InstDef { gain = 0.42, rev = 0.22, ring = 0.6, label = "塔布拉（右鼓）" });

            // ================================================================ 鼓 ==
            Kit("dagu", new object[] {
                new Drum { f0 = 60, bend = 1.7, tau = 0.035, t60 = 1.1, modes = PP(P(1.59, 0.3, 0.35), P(2.14, 0.16, 0.25), P(2.3, 0.12, 0.2)), skin = P(0.3, 900, 0.8, 0.05), thump = 0.5, click = P(0.1, 0.004, 2500), dur = 1.6 },
                Wood(950, 1550, 0.05) }, new InstDef { gain = 0.8, rev = 0.28, label = "大鼓" });
            Kit("tanggu", new object[] {
                new Drum { f0 = 150, bend = 1.4, tau = 0.03, t60 = 0.5, modes = PP(P(1.59, 0.35, 0.2), P(2.14, 0.2, 0.15)), skin = P(0.35, 1500, 0.9, 0.035), click = P(0.15, 0.003, 3000), dur = 0.9 },
                Wood(1300, 2300, 0.04) }, new InstDef { gain = 0.6, rev = 0.25, label = "堂鼓" });
            Kit("taiko", new object[] {
                new Drum { f0 = 70, bend = 1.55, tau = 0.04, t60 = 1.3, modes = PP(P(1.59, 0.3, 0.4), P(2.14, 0.18, 0.3), P(2.65, 0.08, 0.2)), skin = P(0.35, 700, 0.8, 0.06), thump = 0.6, click = P(0.12, 0.004, 2000), dur = 1.8 },
                Wood(1700, 2900, 0.04),
                new Drum { f0 = 340, bend = 1.15, tau = 0.02, t60 = 0.2, modes = PP(P(1.59, 0.3, 0.1)), skin = P(0.6, 2200, 1, 0.025), click = P(0.2, 0.002, 3500), dur = 0.4 } },
                new InstDef { gain = 0.8, rev = 0.3, label = "太鼓" });
            Kit("wardrum", new object[] {
                new Drum { f0 = 52, bend = 1.8, tau = 0.045, t60 = 1.2, modes = PP(P(1.59, 0.25, 0.4), P(2.14, 0.14, 0.3)), skin = P(0.25, 600, 0.8, 0.07), thump = 0.7, dur = 1.8 },
                Wood(800, 1350, 0.05) }, new InstDef { gain = 0.8, rev = 0.3, label = "战鼓" });
            // 锣钹：京锣（音高下滑）、小锣（音高上扬）、抄锣（长鸣）、钹
            Kit("luo", new object[] {
                new Modal { dur = 3.6, attack = 0.001, glide = P(0.09, 0.35), f0 = 215, jitter = 0.01, partials = PP(
                    P(1, 1, 3.2), P(1.42, 0.6, 2.6), P(2.04, 0.4, 2.2), P(2.6, 0.35, 1.6), P(3.3, 0.25, 1.2), P(4.1, 0.2, 0.9), P(5.2, 0.15, 0.6), P(6.5, 0.1, 0.5), P(7.9, 0.07, 0.35)),
                    strike = P(0.5, 3000, 0.7, 0.03) } }, V(2, new InstDef { gain = 0.5, rev = 0.36, label = "京锣" }));
            Kit("xiaoluo", new object[] {
                new Modal { dur = 1.5, attack = 0.0008, glide = P(-0.07, 0.12), f0 = 760, jitter = 0.01, partials = PP(
                    P(1, 1, 1.1), P(1.5, 0.3, 0.6), P(2.3, 0.25, 0.4), P(3.4, 0.12, 0.25)), strike = P(0.25, 4000, 1, 0.01) } },
                V(2, new InstDef { gain = 0.36, rev = 0.3, pan = 0.3, label = "小锣" }));
            {
                // 抄锣：大量随机非谐分音，高频分音起音更慢（“涌起”），长鸣
                var pr = new Rng(4242);
                var ps = new List<double[]> { P(0.5, 0.5, 6, 0.01) };
                for (int i = 0; i < 46; i++) { double fr = 1 + Math.Pow(pr.Next(), 1.6) * 34; ps.Add(P(fr, 0.9 / Math.Sqrt(fr), 6.5 - fr * 0.13, 0.02 + fr * 0.012)); }
                Kit("tamtam", new object[] { new Modal { sr = 24000, dur = 6.5, f0 = 110, partials = ps.ToArray(), jitter = 0.004, strike = P(0.25, 600, 0.6, 0.03), hpf = 60 } },
                    V(1, new InstDef { gain = 0.55, rev = 0.5, sr = 24000, label = "抄锣" }));
                var cp = new List<double[]>();
                for (int i = 0; i < 40; i++) { double fr = 1 + pr.Next() * 2.6; double am = 0.6 + pr.Next() * 0.4; double t6 = 0.7 + pr.Next() * 1.0; cp.Add(P(fr, am, t6)); }
                var cp1 = cp.ConvertAll(p => P(p[0], p[1], 0.12));
                Kit("bo", new object[] {
                    new Modal { dur = 2.2, f0 = 3100, partials = cp.ToArray(), jitter = 0.01, strike = P(1.2, 5500, 0.5, 0.06), hpf = 900 },
                    new Modal { dur = 0.3, f0 = 3100, partials = cp1.ToArray(), jitter = 0.01, strike = P(1.2, 5000, 0.6, 0.03), hpf = 900 } },
                    V(2, new InstDef { gain = 0.36, rev = 0.32, pan = -0.15, label = "钹" }));
            }
            Kit("ban", new object[] { Wood(1250, 2900, 0.05), Wood(1050, 2500, 0.04) }, new InstDef { gain = 0.36, rev = 0.22, pan = -0.25, label = "拍板" });
            Kit("muyu", new object[] { Wood(700, 1650, 0.12), Wood(540, 1300, 0.12) }, new InstDef { gain = 0.4, rev = 0.25, pan = 0.2, label = "木鱼" });
            Kit("bangzi", new object[] { Wood(1850, 3600, 0.09) }, new InstDef { gain = 0.32, rev = 0.22, pan = 0.3, label = "梆子" });
            // 达夫鼓（铁环铃片）：0 中心低音 dum，1 边缘 tak，2 无铃片
            Kit("daf", new object[] {
                new Drum { f0 = 72, bend = 1.3, tau = 0.03, t60 = 0.65, modes = PP(P(1.59, 0.25, 0.25)), skin = P(0.3, 500, 0.8, 0.05), jingle = P(0.12, 0.12), dur = 0.9 },
                new Drum { f0 = 290, bend = 1.1, tau = 0.015, t60 = 0.15, modes = PP(P(1.59, 0.3, 0.08)), skin = P(0.5, 1800, 1, 0.025), jingle = P(0.3, 0.14), dur = 0.6 },
                new Drum { f0 = 85, bend = 1.3, tau = 0.03, t60 = 0.5, modes = PP(P(1.59, 0.25, 0.2)), skin = P(0.3, 700, 0.8, 0.04), dur = 0.7 } },
                new InstDef { gain = 0.6, rev = 0.25, label = "达夫鼓" });
            Kit("darbuka", new object[] {
                new Drum { f0 = 95, bend = 1.25, tau = 0.025, t60 = 0.55, modes = PP(P(1.59, 0.25, 0.2), P(2.14, 0.12, 0.12)), skin = P(0.2, 800, 0.8, 0.03), dur = 0.8 },
                new Drum { f0 = 520, bend = 1.05, tau = 0.01, t60 = 0.2, modes = PP(P(1.59, 0.4, 0.12), P(2.14, 0.2, 0.08)), skin = P(0.45, 3000, 1, 0.012), click = P(0.3, 0.002, 4000), dur = 0.4 },
                new Drum { f0 = 470, bend = 1.05, tau = 0.01, t60 = 0.12, modes = PP(P(1.59, 0.3, 0.08)), skin = P(0.35, 2600, 1, 0.01), dur = 0.3 } },
                new InstDef { gain = 0.55, rev = 0.22, label = "达布卡鼓" });
            // 塔布拉左鼓（巴扬）：0 ge（掌根推压，音高上扬）1 ke（闷击）
            Kit("bayan", new object[] {
                new Drum { f0 = 88, bend = 1.0, rise = P(0.28, 0.12), t60 = 0.75, modes = PP(P(2.0, 0.12, 0.3)), skin = P(0.15, 500, 0.8, 0.03), dur = 1.0 },
                new Drum { f0 = 120, bend = 1.2, tau = 0.01, t60 = 0.08, skin = P(0.6, 900, 0.8, 0.02), dur = 0.25 } },
                new InstDef { gain = 0.6, rev = 0.2, label = "塔布拉（左鼓）" });
            // 朝鲜杖鼓：0 宫（鼓槌，低圆）1 德（细鞭，清脆）2 双面齐击
            Kit("janggu", new object[] {
                new Drum { f0 = 105, bend = 1.25, tau = 0.03, t60 = 0.5, modes = PP(P(1.59, 0.3, 0.2), P(2.14, 0.15, 0.12)), skin = P(0.25, 700, 0.8, 0.04), thump = 0.2, dur = 0.8 },
                new Drum { f0 = 320, bend = 1.08, tau = 0.01, t60 = 0.13, modes = PP(P(1.59, 0.3, 0.08)), skin = P(0.7, 2600, 1, 0.012), click = P(0.35, 0.0015, 4000), dur = 0.35 },
                new Drum { f0 = 110, bend = 1.25, tau = 0.03, t60 = 0.45, modes = PP(P(2.9, 0.3, 0.12)), skin = P(0.6, 2200, 0.9, 0.02), click = P(0.3, 0.002, 4000), thump = 0.2, dur = 0.8 } },
                new InstDef { gain = 0.55, rev = 0.25, label = "杖鼓" });
            Kit("buk", new object[] {
                new Drum { f0 = 84, bend = 1.45, tau = 0.035, t60 = 0.65, modes = PP(P(1.59, 0.3, 0.25), P(2.14, 0.15, 0.2)), skin = P(0.3, 800, 0.8, 0.05), thump = 0.45, dur = 1.0 },
                Wood(1100, 1900, 0.04) }, new InstDef { gain = 0.7, rev = 0.28, label = "北（朝鲜鼓）" });
            Kit("jing", new object[] {
                new Modal { sr = 24000, dur = 5.5, attack = 0.012, glide = P(-0.012, 0.6), f0 = 142, partials = PP(
                    P(0.994, 1, 5.2), P(1.006, 0.8, 5.2), P(1.5, 0.3, 3.4), P(2.01, 0.35, 3), P(2.47, 0.15, 2.2), P(3.1, 0.1, 1.4), P(4.2, 0.05, 0.9)), strike = P(0.03, 400, 1, 0.02) } },
                V(1, new InstDef { gain = 0.6, rev = 0.45, sr = 24000, label = "钲（朝鲜大锣）" }));
            Kit("kkwaeng", new object[] {
                new Modal { dur = 1.0, f0 = 1180, partials = PP(P(1, 1, 0.7), P(1.44, 0.6, 0.5), P(2.1, 0.5, 0.4), P(2.9, 0.3, 0.3), P(3.7, 0.2, 0.2)), strike = P(0.4, 5000, 1, 0.004) },
                new Modal { dur = 0.2, f0 = 1180, partials = PP(P(1, 1, 0.08), P(1.44, 0.6, 0.06), P(2.1, 0.5, 0.05), P(2.9, 0.3, 0.04)), strike = P(0.4, 5000, 1, 0.003) } },
                new InstDef { gain = 0.26, rev = 0.25, pan = 0.35, label = "小金" });
            // 肯当（甘美兰手鼓）：0 低音 dhung 1 拍击 tak 2 中音 thung
            Kit("kendang", new object[] {
                new Drum { f0 = 82, bend = 1.15, tau = 0.03, t60 = 0.45, modes = PP(P(1.59, 0.25, 0.2)), skin = P(0.2, 600, 0.8, 0.03), dur = 0.7 },
                new Drum { f0 = 460, bend = 1.05, tau = 0.01, t60 = 0.08, skin = P(0.7, 2400, 1, 0.012), click = P(0.25, 0.002, 4000), dur = 0.25 },
                new Drum { f0 = 210, bend = 1.08, tau = 0.02, t60 = 0.3, modes = PP(P(1.59, 0.3, 0.15)), skin = P(0.3, 1200, 0.8, 0.02), dur = 0.5 } },
                new InstDef { gain = 0.55, rev = 0.22, label = "肯当鼓" });
            Kit("frame", new object[] {
                new Drum { f0 = 82, bend = 1.3, tau = 0.03, t60 = 0.6, modes = PP(P(1.59, 0.25, 0.22)), skin = P(0.35, 650, 0.8, 0.045), dur = 0.9 },
                new Drum { f0 = 260, bend = 1.08, tau = 0.012, t60 = 0.14, skin = P(0.55, 1700, 1, 0.02), dur = 0.4 } },
                new InstDef { gain = 0.6, rev = 0.26, label = "框鼓" });
            Kit("bodhran", new object[] {
                new Drum { f0 = 68, bend = 1.3, tau = 0.03, t60 = 0.38, modes = PP(P(1.59, 0.25, 0.16)), skin = P(0.5, 600, 0.7, 0.04), thump = 0.25, dur = 0.6 },
                new Drum { f0 = 92, bend = 1.2, tau = 0.02, t60 = 0.25, modes = PP(P(1.59, 0.25, 0.12)), skin = P(0.5, 900, 0.7, 0.03), dur = 0.45 },
                Wood(1200, 2100, 0.03) }, new InstDef { gain = 0.62, rev = 0.24, label = "宝思兰鼓" });
            // 小军鼓（凯尔特风笛鼓队）：短促鼓皮 + 宽带响弦噪声；0 正击 1 轻击 / 装饰
            Kit("snare", new object[] {
                new Drum { f0 = 190, bend = 1.15, tau = 0.01, t60 = 0.16, modes = PP(P(1.59, 0.4, 0.09), P(2.14, 0.25, 0.07)), skin = P(0.9, 3600, 0.55, 0.055), click = P(0.35, 0.002, 5000), dur = 0.45 },
                new Drum { f0 = 190, bend = 1.1, tau = 0.008, t60 = 0.07, skin = P(0.65, 4200, 0.6, 0.03), click = P(0.2, 0.0015, 5000), dur = 0.28 } },
                new InstDef { gain = 0.3, rev = 0.2, pan = 0.12, label = "小军鼓" });
            Kit("shamandrum", new object[] {
                new Drum { f0 = 58, bend = 1.25, tau = 0.04, t60 = 0.95, modes = PP(P(1.59, 0.25, 0.3), P(2.14, 0.12, 0.2)), skin = P(0.3, 500, 0.7, 0.05), jingle = P(0.05, 0.22), thump = 0.35, dur = 1.3 },
                new Drum { f0 = 160, bend = 1.1, tau = 0.02, t60 = 0.2, skin = P(0.4, 1200, 0.8, 0.03), jingle = P(0.1, 0.18), dur = 0.6 } },
                new InstDef { gain = 0.66, rev = 0.32, label = "萨满鼓" });
            Kit("hoof", new object[] {
                new Modal { dur = 0.16, f0 = 560, partials = PP(P(1, 1, 0.05), P(2.2, 0.4, 0.03), P(0.22, 0.6, 0.05)), strike = P(0.25, 1500, 0.8, 0.004), hpf = 80 } },
                V(4, new InstDef { gain = 0.36, rev = 0.18, pan = 0.15, label = "马蹄" }));
            Kit("crotala", new object[] {
                new Modal { dur = 1.2, f0 = 2300, partials = PP(P(1, 1, 0.9), P(2.4, 0.5, 0.6), P(3.9, 0.3, 0.4), P(5.2, 0.2, 0.3)), strike = P(0.2, 6000, 1, 0.003) } },
                V(2, new InstDef { gain = 0.22, rev = 0.3, pan = 0.35, label = "指钹" }));
        }

        // ===================================================== 实时合成乐器 ==
        static double Ripple(int k, double a, double s) { return 1 + a * Math.Sin(k * s + 0.7); }
        static double Arr(double[] a, int k, double dflt) { return k < a.Length && a[k] != 0 ? a[k] : dflt; }
        static readonly double[] FLUTE = { 0, 1, 0.48, 0.3, 0.13, 0.09, 0.055, 0.04, 0.025, 0.018 };
        static readonly double[] FBUZZ = { 0, 1, 0.5, 0.32, 0.14, 0.1, 0.06, 0.045, 0.03, 0.022 };
        static readonly double[] PURE = { 0, 1, 0.16, 0.1, 0.04, 0.02 };
        static readonly Func<int, double>
            H_bowed = k => Math.Pow(k, -1.05) * Ripple(k, 0.35, 1.7),
            H_bowedSoft = k => Math.Pow(k, -1.35) * Ripple(k, 0.3, 2.3),
            H_flute = k => Arr(FLUTE, k, 0.01 / k),
            H_fluteBuzz = k => Arr(FBUZZ, k, 0) + (k >= 6 && k <= 22 ? 0.028 / (1 + (k - 11) * (k - 11) / 30.0) : 0),
            H_pure = k => Arr(PURE, k, 0),
            H_reed = k => Math.Pow(k, -0.62) * Ripple(k, 0.25, 2.9),
            H_reedNasal = k => Math.Pow(k, -0.5) * (k % 2 == 1 ? 1 : 0.55),
            H_brass = k => Math.Pow(k, -1.0),
            H_sheng = k => Math.Pow(k, -0.78) * (k % 2 == 1 ? 1 : 1.12);

        const double LO = 0, HI = 1, BA = 2, PE = 3;
        static double[] F(double type, double f, double q, double g = 0) { return new[] { type, f, q, g }; }
        static InstDef Leg(string name, double rev, string label, LegatoSpec P) { return Def(name, new InstDef { type = InstType.Legato, gain = 1, rev = rev, label = label, legato = P }); }
        static InstDef Pol(string name, double rev, string label, PolySpec P) { return Def(name, new InstDef { type = InstType.Poly, gain = 1, rev = rev, label = label, poly = P }); }
        static PolyOsc O(string type, double cents, double mult = double.NaN, double gain = double.NaN) { return new PolyOsc { type = type, cents = cents, mult = mult, gain = gain }; }
        static PolyOsc OW(string wave, Func<int, double> harm, double cents) { return new PolyOsc { wave = wave, harm = harm, cents = cents }; }

        static void DefineRealtime()
        {
            // ---------------------------------------------------------- 弓弦乐器 --
            Leg("erhu", 0.32, "二胡", new LegatoSpec { wave = "erhu", harm = H_bowed, level = 0.2, attack = 0.09, release = 0.16, dip = 0.28, port = 0.05, bigSlide = 0.16,
                vibRate = 6.1, vib = 16, vibDeep = 36, vibDelay = 0.16, drift = 4,
                noise = new NoiseSpec { amp = 0.09, f = 2600, q = 0.8, chiff = 0.25 },
                filters = new[] { F(HI, 280, 0.7), F(PE, 850, 1.3, 6), F(PE, 2700, 2, 5), F(PE, 1600, 3, -3), F(LO, 6500, 0.6) } });
            Leg("haegeum", 0.3, "奚琴", new LegatoSpec { wave = "haegeum", harm = k => Math.Pow(k, -0.95) * Ripple(k, 0.45, 2.1), level = 0.19, attack = 0.1, release = 0.18, dip = 0.3, port = 0.06, bigSlide = 0.2,
                vibRate = 4.6, vib = 22, vibDeep = 55, vibDelay = 0.25, drift = 6,
                noise = new NoiseSpec { amp = 0.14, f = 2200, q = 0.7, chiff = 0.3 },
                filters = new[] { F(HI, 320, 0.7), F(PE, 1100, 1.6, 7), F(PE, 3000, 2, 4), F(LO, 6000, 0.6) } });
            Leg("kamancheh", 0.32, "卡曼恰", new LegatoSpec { wave = "kamancheh", harm = H_bowed, level = 0.2, attack = 0.1, release = 0.18, dip = 0.25, port = 0.05, bigSlide = 0.15,
                vibRate = 5.6, vib = 18, vibDeep = 34, vibDelay = 0.2,
                noise = new NoiseSpec { amp = 0.08, f = 2400, q = 0.8, chiff = 0.2 },
                filters = new[] { F(HI, 220, 0.7), F(PE, 700, 1.2, 5), F(PE, 2200, 1.8, 4), F(LO, 5500, 0.6) } });
            Leg("morin", 0.36, "马头琴", new LegatoSpec { wave = "morin", harm = H_bowedSoft, level = 0.24, attack = 0.14, release = 0.25, dip = 0.22, port = 0.07, bigSlide = 0.2,
                vibRate = 5.0, vib = 18, vibDeep = 40, vibDelay = 0.25, drift = 5,
                noise = new NoiseSpec { amp = 0.12, f = 1800, q = 0.6, chiff = 0.3 },
                filters = new[] { F(HI, 120, 0.7), F(PE, 480, 1.2, 5), F(PE, 1500, 1.5, 3), F(LO, 4200, 0.6) } });
            Leg("cello", 0.36, "大提琴", new LegatoSpec { wave = "cello", harm = k => Math.Pow(k, -1.1) * Ripple(k, 0.3, 1.3), level = 0.22, attack = 0.12, release = 0.22, dip = 0.2, port = 0.05, bigSlide = 0.12,
                vibRate = 5.4, vib = 14, vibDeep = 26, vibDelay = 0.2,
                noise = new NoiseSpec { amp = 0.05, f = 2000, q = 0.7, chiff = 0.15 },
                filters = new[] { F(HI, 60, 0.7), F(PE, 250, 1, 4), F(PE, 1200, 1.4, 3), F(LO, 4000, 0.6) } });

            // ---------------------------------------------------------- 吹管乐器 --
            Leg("dizi", 0.34, "笛子", new LegatoSpec { wave = "dizi", harm = H_fluteBuzz, level = 0.17, attack = 0.04, release = 0.1, dip = 0.55, port = 0.03, bigSlide = 0.1,
                vibRate = 5.2, vib = 10, vibDeep = 24, vibDelay = 0.22, trem = 0.1, drift = 3, trillRate = 11,
                noise = new NoiseSpec { amp = 0.16, f = 1, q = 5, track = 2.0, chiff = 0.6 },
                filters = new[] { F(HI, 400, 0.7), F(PE, 3800, 1.5, 4), F(LO, 9000, 0.6) } });
            Leg("xiao", 0.38, "箫", new LegatoSpec { wave = "xiao", harm = H_pure, level = 0.24, attack = 0.09, release = 0.18, dip = 0.4, port = 0.05, bigSlide = 0.14,
                vibRate = 4.6, vib = 9, vibDeep = 22, vibDelay = 0.3, trem = 0.12, drift = 3,
                noise = new NoiseSpec { amp = 0.3, f = 1, q = 3.5, track = 1.0, chiff = 0.5 },
                filters = new[] { F(HI, 200, 0.7), F(LO, 5000, 0.6) } });
            Leg("shakuhachi", 0.4, "尺八", new LegatoSpec { wave = "shaku", harm = k => Arr(new double[] { 0, 1, 0.22, 0.12, 0.05, 0.025 }, k, 0), level = 0.24, attack = 0.1, release = 0.2, dip = 0.5, port = 0.06, bigSlide = 0.22,
                vibRate = 4.2, vib = 6, vibDeep = 30, vibDelay = 0.45, trem = 0.16, drift = 6, scoop = 40,
                noise = new NoiseSpec { amp = 0.42, f = 1, q = 2.6, track = 1.0, chiff = 1.3 },
                filters = new[] { F(HI, 220, 0.7), F(PE, 1800, 1, 3), F(LO, 6000, 0.6) } });
            Leg("shinobue", 0.34, "筱笛", new LegatoSpec { wave = "shino", harm = H_flute, level = 0.15, attack = 0.03, release = 0.1, dip = 0.6, port = 0.03, bigSlide = 0.12,
                vibRate = 5.6, vib = 8, vibDeep = 26, vibDelay = 0.25, trem = 0.1, trillRate = 12,
                noise = new NoiseSpec { amp = 0.22, f = 1, q = 4, track = 2.0, chiff = 0.7 },
                filters = new[] { F(HI, 600, 0.7), F(PE, 4500, 1.2, 3), F(LO, 10000, 0.6) } });
            Leg("daegeum", 0.4, "大笒", new LegatoSpec { wave = "daegeum", harm = H_fluteBuzz, level = 0.2, attack = 0.1, release = 0.2, dip = 0.45, port = 0.06, bigSlide = 0.24,
                vibRate = 4.2, vib = 14, vibDeep = 48, vibDelay = 0.3, trem = 0.14, drift = 5, scoop = 30,
                noise = new NoiseSpec { amp = 0.28, f = 1, q = 3.5, track = 1.5, chiff = 0.8 },
                filters = new[] { F(HI, 260, 0.7), F(PE, 3000, 1.4, 4), F(LO, 7000, 0.6) } });
            Leg("ney", 0.4, "奈伊笛", new LegatoSpec { wave = "ney", harm = k => Arr(new double[] { 0, 1, 0.3, 0.18, 0.08, 0.05, 0.03 }, k, 0), level = 0.22, attack = 0.12, release = 0.2, dip = 0.45, port = 0.06, bigSlide = 0.2,
                vibRate = 5.0, vib = 10, vibDeep = 30, vibDelay = 0.3, trem = 0.14, drift = 4, scoop = 25,
                noise = new NoiseSpec { amp = 0.5, f = 1, q = 2.4, track = 1.0, chiff = 0.9 },
                filters = new[] { F(HI, 240, 0.7), F(PE, 1400, 1, 3), F(LO, 6000, 0.6) } });
            Leg("bansuri", 0.38, "班苏里笛", new LegatoSpec { wave = "bansuri", harm = k => Arr(new double[] { 0, 1, 0.3, 0.16, 0.07, 0.04 }, k, 0), level = 0.22, attack = 0.08, release = 0.18, dip = 0.45, port = 0.07, bigSlide = 0.22,
                vibRate = 5.2, vib = 8, vibDeep = 28, vibDelay = 0.3, trem = 0.12, drift = 3,
                noise = new NoiseSpec { amp = 0.32, f = 1, q = 3, track = 1.0, chiff = 0.6 },
                filters = new[] { F(HI, 220, 0.7), F(LO, 6500, 0.6) } });
            Leg("suling", 0.36, "苏林笛", new LegatoSpec { wave = "suling", harm = H_flute, level = 0.18, attack = 0.05, release = 0.14, dip = 0.5, port = 0.04, bigSlide = 0.14,
                vibRate = 5.8, vib = 10, vibDeep = 26, vibDelay = 0.25, trem = 0.12,
                noise = new NoiseSpec { amp = 0.28, f = 1, q = 3.5, track = 1.0, chiff = 0.5 },
                filters = new[] { F(HI, 300, 0.7), F(LO, 8000, 0.6) } });
            Leg("whistle", 0.36, "哨笛", new LegatoSpec { wave = "whistle", harm = k => Arr(new double[] { 0, 1, 0.12, 0.06, 0.03 }, k, 0), level = 0.2, attack = 0.04, release = 0.12, dip = 0.6, port = 0.025, bigSlide = 0.08,
                vibRate = 5.4, vib = 6, vibDeep = 18, vibDelay = 0.35, trem = 0.08, trillRate = 13,
                noise = new NoiseSpec { amp = 0.2, f = 1, q = 4, track = 1.0, chiff = 0.5 },
                filters = new[] { F(HI, 300, 0.7), F(LO, 7000, 0.6) } });

            // 双簧 / 风笛
            Leg("suona", 0.3, "唢呐", new LegatoSpec { wave = "suona", harm = H_reed, level = 0.12, attack = 0.03, release = 0.09, dip = 0.45, port = 0.04, bigSlide = 0.14,
                vibRate = 6.4, vib = 14, vibDeep = 34, vibDelay = 0.15, drift = 4, trillRate = 10,
                noise = new NoiseSpec { amp = 0.05, f = 3000, q = 0.8, chiff = 0.2 },
                filters = new[] { F(HI, 420, 0.7), F(PE, 1300, 1.4, 7), F(PE, 3100, 2, 5), F(LO, 7500, 0.6) },
                bright = new BrightSpec { bas = 3, vel = 6, q = 0.6, add = 600 } });
            Leg("piri", 0.32, "觱篥", new LegatoSpec { wave = "piri", harm = H_reedNasal, level = 0.13, attack = 0.05, release = 0.14, dip = 0.35, port = 0.06, bigSlide = 0.24,
                vibRate = 4.6, vib = 18, vibDeep = 55, vibDelay = 0.22, drift = 6, scoop = 50,
                noise = new NoiseSpec { amp = 0.06, f = 2500, q = 0.8, chiff = 0.2 },
                filters = new[] { F(HI, 350, 0.7), F(PE, 1000, 1.6, 6), F(PE, 2500, 2, 4), F(LO, 6000, 0.6) },
                bright = new BrightSpec { bas = 2.5, vel = 4.5, q = 0.6, add = 500 } });
            Leg("aulos", 0.34, "阿夫洛斯管", new LegatoSpec { wave = "aulos", harm = H_reedNasal, level = 0.13, attack = 0.04, release = 0.12, dip = 0.35, port = 0.04, bigSlide = 0.12,
                vibRate = 5.4, vib = 8, vibDeep = 22, vibDelay = 0.3, detune2 = 7, mix2 = 0.5,
                noise = new NoiseSpec { amp = 0.06, f = 2600, q = 0.8, chiff = 0.25 },
                filters = new[] { F(HI, 300, 0.7), F(PE, 1200, 1.4, 5), F(LO, 5500, 0.6) },
                bright = new BrightSpec { bas = 2.5, vel = 4, q = 0.6, add = 400 } });
            Leg("zurna", 0.3, "祖尔纳", new LegatoSpec { wave = "zurna", harm = H_reed, level = 0.11, attack = 0.03, release = 0.09, dip = 0.4, port = 0.04, bigSlide = 0.14,
                vibRate = 6.6, vib = 12, vibDeep = 30, vibDelay = 0.15, trillRate = 11,
                noise = new NoiseSpec { amp = 0.05, f = 3200, q = 0.8, chiff = 0.2 },
                filters = new[] { F(HI, 450, 0.7), F(PE, 1500, 1.4, 7), F(PE, 3400, 2, 5), F(LO, 8000, 0.6) },
                bright = new BrightSpec { bas = 3, vel = 6, q = 0.6, add = 700 } });
            Leg("chanter", 0.3, "风笛（主管）", new LegatoSpec { wave = "chanter", harm = k => Math.Pow(k, -0.55) * Ripple(k, 0.3, 2.2), level = 0.11, attack = 0.03, release = 0.06, dip = 0, port = 0.012,
                vibRate = 5, vib = 0, vibDeep = 0, drift = 1.5, autoGrace = 22, shape = false,
                noise = new NoiseSpec { amp = 0.03, f = 3000, q = 0.8 },
                filters = new[] { F(HI, 500, 0.7), F(PE, 1400, 1.2, 6), F(PE, 3300, 2, 5), F(LO, 8500, 0.6) } });

            // 铜管
            Leg("cornu", 0.4, "罗马号角", new LegatoSpec { wave = "cornu", harm = H_brass, level = 0.2, attack = 0.06, release = 0.2, dip = 0.4, port = 0.05, bigSlide = 0.1,
                vibRate = 5, vib = 4, vibDeep = 14, vibDelay = 0.4, scoop = 45, detune2 = 9, mix2 = 0.6,
                filters = new[] { F(HI, 90, 0.7), F(PE, 900, 1, 3) },
                bright = new BrightSpec { bas = 2.2, vel = 5.5, q = 1.1, add = 200 } });
            Leg("horn", 0.45, "圆号", new LegatoSpec { wave = "horn", harm = H_brass, level = 0.22, attack = 0.1, release = 0.25, dip = 0.3, port = 0.06, bigSlide = 0.1,
                vibRate = 5, vib = 3, vibDeep = 10, vibDelay = 0.5, scoop = 20, detune2 = 6, mix2 = 0.8,
                filters = new[] { F(HI, 70, 0.7), F(PE, 500, 1, 3), F(LO, 3500, 0.5) },
                bright = new BrightSpec { bas = 1.6, vel = 3.6, q = 0.7, add = 150 } });

            // 呼麦泛音
            Def("overtone", new InstDef { type = InstType.Legato, gain = 1, rev = 0.4, label = "呼麦（泛音）", overtone = true });

            // ------------------------------------------------------- 复音（垫音） --
            const string SAW = "sawtooth";
            Pol("strings", 0.42, "弦乐组", new PolySpec { oscs = new[] { O(SAW, -9), O(SAW, 0), O(SAW, 8) }, lp = (f, v) => Math.Min(6000, 900 + f * 2.2 + v * 1600), q = 0.5,
                attack = 0.45, release = 0.7, level = 0.075, vib = 7, vibRate = 5.2, lpEnv = true });
            Pol("stab", 0.36, "弦乐顿弓", new PolySpec { oscs = new[] { O(SAW, -8), O(SAW, 0), O(SAW, 7) }, lp = (f, v) => Math.Min(7000, 1200 + f * 3 + v * 2500), q = 0.6,
                attack = 0.012, release = 0.16, level = 0.085, vib = 4, vibRate = 5.5, sustain = 0.55, lpEnv = true });
            Pol("lowstr", 0.32, "低音弦乐", new PolySpec { oscs = new[] { O(SAW, -6), O(SAW, 5), O("sine", 0, 1, 0.8) }, lp = (f, v) => 380 + f * 2.5 + v * 500, q = 0.6,
                attack = 0.12, release = 0.35, level = 0.11, vib = 4, vibRate = 5 });
            Pol("sheng", 0.36, "笙", new PolySpec { oscs = new[] { OW("sheng", H_sheng, -4), OW("sheng", H_sheng, 4) }, lp = (f, v) => Math.Min(7000, f * 5 + 1200 + v * 1500), q = 0.7,
                attack = 0.07, release = 0.3, level = 0.06, vib = 3, vibRate = 5.6, sustain = 0.95 });
            Pol("choir", 0.5, "合唱", new PolySpec { oscs = new[] { O(SAW, -11), O(SAW, 0), O(SAW, 10) }, lp = (f, v) => 3600, q = 0.5,
                formants = PP(P(650, 1.0, 80), P(1080, 0.55, 90), P(2650, 0.28, 120), P(2900, 0.2, 130)),
                attack = 0.55, release = 0.9, level = 0.5, vib = 9, vibRate = 4.8 });
            Pol("choirOo", 0.5, "合唱（u）", new PolySpec { oscs = new[] { O(SAW, -11), O(SAW, 0), O(SAW, 10) }, lp = (f, v) => 2400, q = 0.5,
                formants = PP(P(380, 1.0, 60), P(820, 0.35, 80), P(2500, 0.1, 120)),
                attack = 0.6, release = 1.0, level = 0.55, vib = 8, vibRate = 4.6 });
            Pol("horns", 0.45, "圆号组", new PolySpec { oscs = new[] { O(SAW, -7), O(SAW, 6) }, lp = (f, v) => Math.Min(4500, f * (1.6 + 3.4 * v) + 150), q = 0.7,
                attack = 0.14, release = 0.35, level = 0.1, vib = 3, lpEnv = true });
            Func<int, double> droneH = k => Math.Pow(k, -0.7) * (k % 2 == 1 ? 1 : 0.8);
            Pol("drones", 0.3, "持续低音（风笛 / 管）", new PolySpec { oscs = new[] { OW("drone", droneH, -2), OW("drone", droneH, 3) },
                lp = (f, v) => Math.Min(5000, f * 9), q = 0.8, attack = 0.4, release = 0.5, level = 0.045, vib = 0, sustain = 1 });
            Pol("kargyraa", 0.42, "喉音低吟", new PolySpec { oscs = new[] { O(SAW, -4), O(SAW, 4), O(SAW, 0, 0.5, 0.6) }, lp = (f, v) => 1400, q = 0.5,
                formants = PP(P(420, 1.0, 60), P(760, 0.6, 70), P(2400, 0.15, 100)),
                attack = 0.7, release = 1.0, level = 0.5, vib = 4, vibRate = 3.2, sustain = 1 });
            Pol("sub", 0.08, "低频", new PolySpec { oscs = new[] { O("sine", 0), O("triangle", 0, 1, 0.3) }, lp = (f, v) => 400, q = 0.5,
                attack = 0.03, release = 0.15, level = 0.32, vib = 0 });
        }

        // 预生成某乐器在 [p0, p1]（MIDI，±3 半音余量供滑音 / 颤音）用到的振荡器波表
        public static void WarmWaves(string name, double p0, double p1, double sr)
        {
            var d = Get(name);
            if (d == null || p1 < p0) return;
            double f0 = MU.Mtof(p0 - 3), f1 = MU.Mtof(p1 + 3);
            if (d.legato != null) PWave.Custom(d.legato.wave, d.legato.harm, d.legato.nh, sr).Warm(f0, f1);
            if (d.poly != null)
                foreach (var oc in d.poly.oscs)
                {
                    var w = oc.type != null ? PWave.Basic(oc.type, sr) : PWave.Custom(oc.wave, oc.harm, 48, sr);
                    double m = MU.Or(oc.mult, 1);
                    w.Warm(f0 * m, f1 * m);
                }
            if (d.overtone) PWave.Basic("sawtooth", sr).Warm(MU.Mtof(p0 - 40), MU.Mtof(p0 - 10));
        }

        // ============================================================ 演奏 ==
        static Biquad BQ(double[] F, double sr)
        {
            int t = F[0] == LO ? Biquad.LP : F[0] == HI ? Biquad.HP : F[0] == BA ? Biquad.BP : Biquad.PK;
            return new Biquad(t, F[1], F[2], F.Length > 3 ? F[3] : 0, sr);
        }

        // 连奏乐句（单音乐器）：所有自动化在开始时一次排好（同 JS legato(P)）
        internal static void PlayLegato(LegatoSpec P, Track tr, Channel ch, List<PhNote> notes, Rng r)
        {
            var e = tr.eng; double sr = e.sr;
            var n0 = notes[0]; var nl = notes[notes.Count - 1];
            double t0 = n0.t, tEnd = nl.t + nl.d;
            double rel = MU.Or(P.release, 0.15);
            double stopT = tEnd + rel * 3 + 0.08;
            double velCurve = MU.Or(P.velCurve, 1.25);
            Func<double, double> lvl = v => P.level * Math.Pow(Math.Min(1.2, v), velCurve);
            var wv = PWave.Custom(P.wave, P.harm, P.nh, sr);
            var V = new LegatoVoice();
            V.o1 = new Osc(wv, sr);
            if (MU.Or(P.detune2, 0) != 0) { V.o2 = new Osc(wv, sr); V.det2 = P.detune2; V.mix2 = MU.Def(P.mix2, 0.7); V.f2 = new Param(440); }
            var chain = new List<Biquad>();
            if (P.filters != null) foreach (var Fd in P.filters) chain.Add(BQ(Fd, sr));
            V.chain = chain.ToArray();
            if (P.bright != null) { V.bright = new Biquad(Biquad.LP, 2000, MU.Or(P.bright.q, 0.8), 0, sr); V.br = new Param(2000); }
            V.lfoF = MU.Or(P.vibRate, 5.5) * (0.93 + r.Next() * 0.14);
            V.driftF = 0.25 + r.Next() * 0.35;
            V.dg = MU.Def(P.drift, 3);
            if (MU.Or(P.trem, 0) != 0) V.trem = new Param(0);
            if (P.noise != null)
            {
                double nf0 = MU.Or(P.noise.f, 2500);
                V.nbp = new Biquad(Biquad.BP, nf0, MU.Or(P.noise.q, 1), 0, sr);
                V.ng = new Param(0); V.noisePost = P.noise.post;
                if (MU.Or(P.noise.track, 0) != 0) V.nf = new Param(nf0);
                V.noise = e.noise;
            }
            var fA = new Auto(V.f1); var aA = new Auto(V.amp); var vA = new Auto(V.vib);
            var f2A = V.f2 != null ? new Auto(V.f2) : null;
            var nA = V.ng != null ? new Auto(V.ng) : null;
            var nfA = V.nf != null ? new Auto(V.nf) : null;
            var bA = V.br != null ? new Auto(V.br) : null;
            var tA = V.trem != null ? new Auto(V.trem) : null;
            double track = P.noise != null ? MU.Or(P.noise.track, 0) : 0;
            Action<double, double, bool> Fq = (v, t, ex) =>
            {
                if (ex) { fA.Exp(v, t); if (f2A != null) f2A.Exp(v, t); if (nfA != null) nfA.Exp(v * track, t); }
                else { fA.Set(v, t); if (f2A != null) f2A.Set(v, t); if (nfA != null) nfA.Set(v * track, t); }
            };
            double att = MU.Or(P.attack, 0.08);
            double dip = MU.Def(P.dip, 0.3);
            double nAmt = P.noise != null ? P.noise.amp : 0;
            double chiff = P.noise != null ? MU.Or(P.noise.chiff, 0) : 0;
            Func<double, double, double, double> brightAt = (f, v, k) => Math.Min(16000, f * (P.bright.bas + P.bright.vel * v) * k + MU.Or(P.bright.add, 0));
            double prevF = n0.f, prevL = 0;
            aA.Set(0, t0 - 0.002); vA.Set(0, t0 - 0.002);
            if (nA != null) nA.Set(0, t0 - 0.002);
            if (tA != null) tA.Set(0, t0 - 0.002);
            double autoGrace = MU.Or(P.autoGrace, 0);
            double gSemi = MU.Has(ch.V.grace) ? ch.V.grace : autoGrace;
            double graceF = autoGrace != 0 ? MU.Mtof(tr.C.keyMidi + gSemi) : 0;
            double SEMI = Math.Pow(2, 1.0 / 12);
            double vibP = MU.Or(P.vib, 0), vibDeep = MU.Or(P.vibDeep, vibP * 2);
            double vd = MU.Def(P.vibDelay, 0.18);
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i]; var o = n.orn ?? new Orn();
                double ti = n.t, d = Math.Max(0.03, n.d);
                double L = lvl(n.v);
                var prev = i > 0 ? notes[i - 1] : null;
                bool tied = prev != null && prev.orn != null && prev.orn.tie;
                // ---- 音高
                if (i == 0)
                {
                    if (o.slide || o.gliss != 0)
                    {
                        double fs = n.f / Math.Pow(SEMI, o.gliss != 0 ? 5 : 2);
                        Fq(fs, ti, false); Fq(fs, ti + 0.02, false); Fq(n.f, ti + (o.gliss != 0 ? 0.32 : 0.16), true);
                    }
                    else if (MU.Has(n.gp)) { double fg = MU.Mtof(n.gp); Fq(fg, ti, false); Fq(fg, ti + 0.055, false); Fq(n.f, ti + 0.08, true); }
                    else if (MU.Or(P.scoop, 0) != 0) { Fq(n.f * Math.Pow(2, -P.scoop / 1200), ti, false); Fq(n.f, ti + 0.07, true); }
                    else Fq(n.f, ti, false);
                }
                else
                {
                    double iv = Math.Abs(Math.Log(n.f / prevF, 2) * 12);
                    if (autoGrace != 0 && !tied && graceF != 0)
                    {
                        double gF = graceF; while (gF < n.f * 1.05) gF *= 2;
                        Fq(prevF, ti - 0.04, false); Fq(gF, ti - 0.035, false); Fq(gF, ti - 0.004, false); Fq(n.f, ti, false);
                    }
                    else if (MU.Has(n.gp))
                    {
                        double fg = MU.Mtof(n.gp);
                        Fq(prevF, ti - 0.012, false); Fq(fg, ti, false); Fq(fg, ti + 0.05, false); Fq(n.f, ti + 0.075, true);
                    }
                    else if (o.slide || o.gliss != 0 || iv >= 4.5)
                    {
                        double bs = MU.Or(P.bigSlide, 0.14);
                        double gt = o.gliss != 0 ? 0.32 : (o.slide ? bs : bs * 0.6);
                        Fq(prevF, ti - gt * 0.35, false); Fq(n.f, ti + gt * 0.65, true);
                    }
                    else if (iv > 0.05)
                    {
                        double gt = MU.Or(P.port, 0.045);
                        Fq(prevF, ti - gt * 0.5, false); Fq(n.f, ti + gt * 0.5, true);
                    }
                }
                // 颤音 / 打音（trill）
                if (o.trem && MU.Has(n.tp))
                {
                    double ft = MU.Mtof(n.tp);
                    double rate = MU.Or(P.trillRate, 9);
                    int k = 0;
                    for (double tt = ti + 0.06; tt < ti + d - 0.08; tt += 0.5 / rate, k++)
                    {
                        Fq(k % 2 == 1 ? n.f : ft, tt, false); Fq(k % 2 == 1 ? n.f : ft, tt + 0.5 / rate - 0.012, false);
                    }
                    Fq(n.f, ti + d - 0.06, false);
                }
                if (o.fall) { Fq(n.f, ti + d * 0.6, false); Fq(n.f / Math.Pow(SEMI, 3), ti + d, true); }
                // ---- 音量
                if (i == 0)
                {
                    double pk = o.acc ? L * 1.35 : L * 1.06;
                    aA.Lin(pk, ti + att);
                    aA.Lin(L, ti + att + 0.12);
                }
                else if (tied || autoGrace != 0)
                {
                    aA.Lin(L, ti + 0.06);
                }
                else
                {
                    aA.Lin(prevL, ti - 0.025);
                    aA.Lin(prevL * (1 - dip), ti + 0.012);
                    aA.Lin(o.acc ? L * 1.3 : L, ti + Math.Min(att, 0.07));
                    if (o.acc) aA.Lin(L, ti + 0.2);
                }
                double endL = L;
                if (o.swell) { aA.Lin(L * 1.5, ti + d * 0.92); endL = L * 1.5; }
                else if (d > 1.0 && P.shape) { aA.Lin(L * 1.1, ti + d * 0.55); aA.Lin(L * 0.94, ti + d - 0.03); endL = L * 0.94; }
                if (o.fall) { aA.Lin(endL, ti + d * 0.6); aA.Lin(endL * 0.25, ti + d); endL *= 0.25; }
                prevL = endL; prevF = o.fall ? n.f / Math.Pow(SEMI, 3) : n.f;
                // ---- 颤音深度：长音才展开
                double depth = o.vib != 0 ? vibDeep : vibP;
                vA.Lin(depth * 0.15, ti + 0.02);
                if (d > vd + 0.12) { vA.Lin(depth * 0.3, ti + vd); vA.Lin(depth, ti + Math.Min(d - 0.02, vd + 0.35)); }
                if (tA != null) { tA.Lin(0, ti + 0.02); if (d > vd + 0.12) tA.Lin(P.trem * L, ti + Math.Min(d - 0.02, vd + 0.35)); }
                // ---- 气声：起音时更多（chiff）
                if (nA != null)
                {
                    if (i == 0 || (!tied && autoGrace == 0)) { nA.Lin(nAmt * L + chiff * L, ti + 0.012); nA.Lin(nAmt * L, ti + 0.07); }
                    else nA.Lin(nAmt * L, ti + 0.05);
                    if (d > 0.4) nA.Lin(nAmt * L * (o.swell ? 1.3 : 0.9), ti + d - 0.02);
                }
                // ---- 动态亮度
                if (bA != null)
                {
                    double fb = n.f;
                    if (i == 0 || (!tied && autoGrace == 0))
                    {
                        bA.Set(Math.Max(80, brightAt(fb, n.v, 0.45)), ti);
                        bA.Exp(brightAt(fb, n.v, o.acc ? 1.8 : 1.35), ti + Math.Min(0.06, d * 0.5));
                        bA.Exp(brightAt(fb, n.v, 1.0), ti + Math.Min(0.32, d * 0.9));
                    }
                    else bA.Exp(brightAt(fb, n.v, 1.0), ti + 0.05);
                    if (o.swell) bA.Exp(brightAt(fb, n.v * 1.4, 1.2), ti + d * 0.9);
                }
            }
            aA.Lin(prevL, tEnd);
            aA.Lin(0, tEnd + rel);
            if (nA != null) nA.Lin(0, tEnd + rel * 0.8);
            vA.Lin(0, tEnd + rel);
            double tS = Math.Max(0, t0 - 0.002);
            if (V.noise != null) V.nPos = (int)(r.Next() * 1.5 * sr);
            V.startT = tS; V.Stop(stopT);
            tr.AddVoice(ch, V);
        }

        internal sealed class LegatoVoice : Voice
        {
            public Osc o1, o2;
            public readonly Param f1 = new Param(440), amp = new Param(0), vib = new Param(0);
            public Param f2, ng, nf, br, trem;
            public double det2, mix2, lfoF, driftF, dg;
            public Biquad[] chain;
            public Biquad bright, nbp;
            public bool noisePost;
            public float[] noise;
            public int nPos;
            double lfoPh, drPh;
            bool started;
            public override bool Render(MusicEngine e, float[] o, int n, double t0)
            {
                double dt = e.dt;
                if (t0 >= stopT) return false;
                if (t0 + n * dt <= startT) return true;
                var s = e.s;
                f1.Fill(s[1], n, t0, dt); amp.Fill(s[2], n, t0, dt); vib.Fill(s[3], n, t0, dt);
                if (f2 != null) f2.Fill(s[4], n, t0, dt);
                if (ng != null) ng.Fill(s[5], n, t0, dt);
                if (nf != null) nf.Fill(s[6], n, t0, dt);
                if (br != null) br.Fill(s[7], n, t0, dt);
                if (trem != null) trem.Fill(s[8], n, t0, dt);
                float[] F1 = s[1], A = s[2], VB = s[3], F2 = s[4], NG = s[5], NF = s[6], BR = s[7], TR = s[8];
                double li = MU.TAU * lfoF * dt, di = MU.TAU * driftF * dt;
                int nl = noise != null ? noise.Length : 0;
                for (int i = 0; i < n; i++)
                {
                    double t = t0 + i * dt;
                    if (t < startT) continue;
                    if (t >= stopT) return false;
                    if (!started) { started = true; double tau = t - startT; lfoPh = MU.TAU * lfoF * tau; drPh = MU.TAU * driftF * tau; }
                    double lfo = Math.Sin(lfoPh), det = VB[i] * lfo + dg * Math.Sin(drPh);
                    lfoPh += li; drPh += di;
                    double x = o1.Next(F1[i] * MU.Cents(det));
                    if (o2 != null) x += mix2 * o2.Next(F2[i] * MU.Cents(det2 + det));
                    double g = A[i]; if (trem != null) g += TR[i] * lfo;
                    x *= g;
                    double nz = 0;
                    if (noise != null)
                    {
                        if (nf != null && (i & 7) == 0 && NF[i] != nbp.freq) nbp.Set(NF[i]);
                        nz = nbp.Tick(noise[nPos]) * NG[i];
                        if (++nPos >= nl) nPos = 0;
                        if (!noisePost) x += nz;
                    }
                    for (int k = 0; k < chain.Length; k++) x = chain[k].Tick(x);
                    if (bright != null) { if ((i & 7) == 0 && BR[i] != bright.freq) bright.Set(BR[i]); x = bright.Tick(x); }
                    if (noisePost) x += nz;
                    o[i] += (float)x;
                }
                if (lfoPh > 1e4) lfoPh -= MU.TAU * Math.Floor(lfoPh / MU.TAU);
                if (drPh > 1e4) drPh -= MU.TAU * Math.Floor(drPh / MU.TAU);
                return true;
            }
        }

        // 呼麦泛音：固定的低音 + 极窄带通选出泛音，随旋律滑动
        internal static void PlayOvertone(Track tr, Channel ch, List<PhNote> notes, Rng r)
        {
            var e = tr.eng; double sr = e.sr;
            var n0 = notes[0]; var nl = notes[notes.Count - 1];
            double t0 = n0.t, tEnd = nl.t + nl.d;
            double dp; if (!tr.C.droneMidi.TryGetValue(ch.name, out dp)) dp = tr.C.keyMidi - 24;
            double f0 = MU.Mtof(dp);
            Func<double, double> harm = f => Math.Max(2, MU.JsRound(f / f0)) * f0;
            double h0 = harm(n0.f);
            var V = new OvertoneVoice { osc = new Osc(PWave.Basic("sawtooth", sr), sr), f0 = f0, bp = new Biquad(Biquad.BP, h0, 28, 0, sr), bp2 = new Biquad(Biquad.BP, h0, 28, 0, sr) };
            V.fp = new Param(h0); V.fp2 = new Param(h0);
            var fA = new Auto(V.fp); var f2A = new Auto(V.fp2); var gA = new Auto(V.g);
            gA.Set(0, t0); fA.Set(h0, t0); f2A.Set(h0, t0);
            double pf = h0;
            foreach (var n in notes)
            {
                double L = 2.4 * Math.Pow(n.v, 1.2);
                double hf = harm(n.f);
                if (n != n0) { fA.Set(pf, n.t - 0.05); f2A.Set(pf, n.t - 0.05); fA.Exp(hf, n.t + 0.07); f2A.Exp(hf, n.t + 0.07); }
                gA.Lin(L, n.t + (n == n0 ? 0.25 : 0.08));
                gA.Lin(L * 0.85, n.t + n.d - 0.02);
                pf = hf;
            }
            gA.Lin(0, tEnd + 0.4);
            V.startT = t0; V.Stop(tEnd + 0.6);
            tr.AddVoice(ch, V);
        }
        internal sealed class OvertoneVoice : Voice
        {
            public Osc osc; public double f0; public Biquad bp, bp2;
            public Param fp, fp2; public readonly Param g = new Param(0);
            public override bool Render(MusicEngine e, float[] o, int n, double t0)
            {
                double dt = e.dt;
                if (t0 >= stopT) return false;
                if (t0 + n * dt <= startT) return true;
                fp.Fill(e.s[1], n, t0, dt); fp2.Fill(e.s[2], n, t0, dt); g.Fill(e.s[3], n, t0, dt);
                float[] F = e.s[1], F2 = e.s[2], G = e.s[3];
                for (int i = 0; i < n; i++)
                {
                    double t = t0 + i * dt;
                    if (t < startT) continue;
                    if (t >= stopT) return false;
                    if ((i & 7) == 0) { if (F[i] != bp.freq) bp.Set(F[i]); if (F2[i] != bp2.freq) bp2.Set(F2[i]); }
                    double x = bp2.Tick(bp.Tick(osc.Next(f0)));
                    o[i] += (float)(x * G[i]);
                }
                return true;
            }
        }

        // 复音（每个音高一组振荡器 → 低通 → 共振峰 → 增益）
        internal static void PlayPoly(PolySpec P, Track tr, Channel ch, Item it, double t, double durS, double vel, Rng r)
        {
            var e = tr.eng; double sr = e.sr;
            var o = it.orn ?? new Orn();
            double L = P.level * Math.Pow(Math.Min(1.2, vel), MU.Or(P.velCurve, 1.2)) / Math.Sqrt(Math.Max(1, it.ps.Length) * 0.7);
            double att = Math.Min(P.attack, durS * 0.6);
            double rel = P.release;
            double end = t + durS;
            foreach (var p in it.ps)
            {
                double f = MU.Mtof(p);
                var V = new PolyVoice();
                double lp0 = P.lp(f, vel);
                V.lp = new Biquad(Biquad.LP, lp0, MU.Or(P.q, 0.5), 0, sr);
                if (P.formants != null)
                {
                    V.fm = new Biquad[P.formants.Length]; V.fmG = new double[P.formants.Length];
                    for (int k = 0; k < P.formants.Length; k++) { var fm = P.formants[k]; V.fm[k] = new Biquad(Biquad.BP, fm[0], fm[0] / fm[2], 0, sr); V.fmG[k] = fm[1]; }
                }
                V.lfoF = MU.Or(P.vibRate, 5) * (0.9 + r.Next() * 0.2);
                V.vg = MU.Or(P.vib, 0);
                V.osc = new Osc[P.oscs.Length]; V.det = new double[P.oscs.Length]; V.og = new double[P.oscs.Length]; V.freq = new double[P.oscs.Length];
                for (int k = 0; k < P.oscs.Length; k++)
                {
                    var oc = P.oscs[k];
                    var w = oc.type != null ? PWave.Basic(oc.type, sr) : PWave.Custom(oc.wave, oc.harm, 48, sr);
                    V.osc[k] = new Osc(w, sr);
                    V.freq[k] = f * MU.Or(oc.mult, 1);
                    V.det[k] = oc.cents + (r.Next() - 0.5) * 4;
                    V.og[k] = MU.Def(oc.gain, 1);
                }
                if (o.trem) { V.tremF = 12.5 + r.Next() * 1.5; }
                var A = new Auto(V.g);
                A.Set(0, t);
                if (o.swell) { A.Lin(L * 0.35, t + att); A.Lin(L * 1.25, end - 0.05); }
                else { A.Lin(L * (o.acc ? 1.3 : 1), t + att); if (o.acc) A.Lin(L, t + att + 0.25); A.Lin(L * MU.Or(P.sustain, 0.92), end); }
                A.Lin(0, end + rel);
                if (P.lpEnv)
                {
                    V.lpP = new Param(lp0);
                    var LA = new Auto(V.lpP);
                    LA.Set(lp0 * 0.5, t); LA.Exp(lp0 * (o.swell ? 1.4 : 1), t + att * 1.5 + 0.05);
                }
                V.startT = t; V.Stop(end + rel + 0.1);
                tr.AddVoice(ch, V);
            }
        }
        internal sealed class PolyVoice : Voice
        {
            public Osc[] osc; public double[] det, og, freq; public double lfoF, vg, tremF;
            public Biquad lp; public Biquad[] fm; public double[] fmG;
            public readonly Param g = new Param(0);
            public Param lpP;
            double lfoPh, trPh; bool started;
            public override bool Render(MusicEngine e, float[] o, int n, double t0)
            {
                double dt = e.dt;
                if (t0 >= stopT) return false;
                if (t0 + n * dt <= startT) return true;
                g.Fill(e.s[1], n, t0, dt);
                if (lpP != null) lpP.Fill(e.s[2], n, t0, dt);
                float[] G = e.s[1], LP = e.s[2];
                double li = MU.TAU * lfoF * dt, ti = tremF * dt;
                int no = osc.Length;
                for (int i = 0; i < n; i++)
                {
                    double t = t0 + i * dt;
                    if (t < startT) continue;
                    if (t >= stopT) return false;
                    if (!started) { started = true; double tau = t - startT; lfoPh = MU.TAU * lfoF * tau; trPh = tremF * tau; }
                    double vib = vg != 0 ? vg * Math.Sin(lfoPh) : 0;
                    lfoPh += li;
                    double x = 0;
                    for (int k = 0; k < no; k++) x += osc[k].Next(freq[k] * MU.Cents(det[k] + vib)) * og[k];
                    if (lpP != null && (i & 7) == 0 && LP[i] != lp.freq) lp.Set(LP[i]);
                    x = lp.Tick(x);
                    if (fm != null) { double sum = 0; for (int k = 0; k < fm.Length; k++) sum += fm[k].Tick(x) * fmG[k]; x = sum; }
                    if (tremF > 0)
                    {
                        // 弦乐震音：0.55 + 0.45 × 三角波（约 13 Hz）
                        double ph = trPh - Math.Floor(trPh);
                        double tri = ph < 0.25 ? 4 * ph : ph < 0.75 ? 2 - 4 * ph : 4 * ph - 4;
                        x *= 0.55 + 0.45 * tri;
                        trPh += ti;
                    }
                    o[i] += (float)(x * G[i]);
                }
                if (lfoPh > 1e4) lfoPh -= MU.TAU * Math.Floor(lfoPh / MU.TAU);
                return true;
            }
        }
    }
}
