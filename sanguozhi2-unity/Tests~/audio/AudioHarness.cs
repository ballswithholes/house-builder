// 无头音乐测试（不依赖 UnityEngine；与 Core/MusicEngine.cs、Core/MusicInstruments.cs、Data/Generated/MusicScores.g.cs 一起编译，见 run.sh）
//   mono audio.exe data                              乐谱数据完整性（曲数 + 哈希与导出工具一致）+ 乐器表与 JS 缺省参数核对
//   mono audio.exe validate                          编译全部乐曲，报告记谱错误
//   mono audio.exe render <秒> <输出目录> [key…]      离线渲染每首（缺省全部）为 WAV（16 位立体声 44.1k），检查峰值 / RMS / NaN / 耗时
//   mono audio.exe samples <js 采样文件>             与 Node 渲染的采样逐点比对（Karplus-Strong / 模态 / 膜鸣鼓）
//   mono audio.exe live [wav]                         实时模式（Sfx 的用法）：后台线程准备采样、交叉淡入淡出、冲锋乐句叠加、一次性短曲结束回调
//   mono audio.exe dump <输出文件>                    编译结果逐条导出（与 compile-dump.js 的 JS 结果比对）
//   mono audio.exe pieces <乐曲 JSON> <秒> <输出目录>  渲染额外的乐曲（乐器试听片段，JSON 由 browser.mjs 导出）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Sanguo.Audio;

static class AudioHarness
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string mode = args.Length > 0 ? args[0] : "data";
        try
        {
            switch (mode)
            {
                case "data": return Data();
                case "validate": return ValidateAll();
                case "render": return Render(double.Parse(args[1]), args[2], Rest(args, 3));
                case "samples": return Samples(args[1]);
                case "pieces": return Pieces(args[1], double.Parse(args[2]), args[3]);
                case "dump": return Dump(args[1]);
                case "live": return Live(args.Length > 1 ? args[1] : "live.wav");
            }
        }
        catch (Exception e) { Console.WriteLine("异常：" + e); return 1; }
        Console.WriteLine("未知模式 " + mode);
        return 1;
    }
    static List<string> Rest(string[] a, int from) { var l = new List<string>(); for (int i = from; i < a.Length; i++) l.Add(a[i]); return l; }

    // ------------------------------------------------------------ 规范 JSON
    static string Canon(object v)
    {
        if (v == null) return "null";
        if (v is double) return MU.JsNum((double)v);
        if (v is bool) return (bool)v ? "true" : "false";
        if (v is string) return Q((string)v);
        if (v is List<object>) { var sb = new StringBuilder("["); var l = (List<object>)v; for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Canon(l[i])); } return sb.Append(']').ToString(); }
        var o = (MObj)v; var s = new StringBuilder("{"); bool first = true;
        foreach (var k in o.Keys) { if (!first) s.Append(','); first = false; s.Append(Q(k)).Append(':').Append(Canon(o[k])); }
        return s.Append('}').ToString();
    }
    static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    static int Data()
    {
        int fails = 0;
        uint h = 2166136261;
        var keys = MusicLib.Keys();
        foreach (var k in keys)
        {
            string s = k + "\u0001" + Canon(MusicLib.Get(k)) + "\u0002";
            foreach (char c in s) { h ^= c; h *= 16777619; }
        }
        bool ok = keys.Count == MusicScores.Count && h == MusicScores.Hash;
        if (!ok) fails++;
        Console.WriteLine((ok ? "通过" : "失败") + $"：乐谱 {keys.Count}/{MusicScores.Count} 首，hash 0x{h:x8} / 0x{MusicScores.Hash:x8}");
        // 乐器表：名称、类型与缺省参数（JS 的 INST 标量字段）
        var I = MusicScores.Instruments;
        int bad = 0, n = 0;
        var seen = new HashSet<string>();
        for (int i = 0; i + 1 < I.Length; i += 2)
        {
            n++;
            string name = I[i]; seen.Add(name);
            var js = (MObj)MJson.Parse(I[i + 1]);
            var d = Instruments.Get(name);
            if (d == null) { Console.WriteLine("  缺少乐器 " + name); bad++; continue; }
            var diffs = new List<string>();
            string ty = js.Str("type");
            string cty = d.type == InstType.Sample ? "sample" : d.type == InstType.Legato ? "legato" : "poly";
            if (ty != cty) diffs.Add("type " + ty + "/" + cty);
            Action<string, double, double> num = (f, cs, dflt) => { double jv = js.Num(f, double.NaN); double cv = cs; if (double.IsNaN(jv) && double.IsNaN(cv)) return; if (double.IsNaN(jv) || double.IsNaN(cv) || Math.Abs(jv - cv) > 1e-12) diffs.Add(f + " " + jv + "/" + cv); };
            num("gain", d.gain, 1); num("rev", d.rev, 0); num("pan", d.pan, 0); num("ring", d.ring, 0); num("attackBend", d.attackBend, 0);
            num("vibCents", d.vibCents, 0); num("vibRate", d.vibRate, 0); num("tremRate", d.tremRate, 0); num("slideSemi", d.slideSemi, 0); num("strum", d.strum, 0);
            if (ty == "sample")
            {
                if ((int)js.Num("sr", 0) != d.sr) diffs.Add("sr " + js.Num("sr", 0) + "/" + d.sr);
                if ((int)js.Num("variants", 1) != d.variants) diffs.Add("variants " + js.Num("variants", 1) + "/" + d.variants);
                bool pl = js["pitchless"] is bool && (bool)js["pitchless"];
                if (pl != d.pitchless) diffs.Add("pitchless");
            }
            bool ra = js["ringAll"] is bool && (bool)js["ringAll"];
            if (ra != d.ringAll) diffs.Add("ringAll");
            var vl = js.Arr("velLp");
            if ((vl == null) != (d.velLp == null) || (vl != null && ((double)vl[0] != d.velLp[0] || (double)vl[1] != d.velLp[1]))) diffs.Add("velLp");
            if (js.Str("label") != d.label) diffs.Add("label " + js.Str("label") + "/" + d.label);
            if (diffs.Count > 0) { bad++; Console.WriteLine("  乐器 " + name + "：" + string.Join("，", diffs)); }
        }
        foreach (var nm in Instruments.Names) if (!seen.Contains(nm)) { bad++; Console.WriteLine("  多出的乐器 " + nm); }
        if (bad > 0) fails++;
        Console.WriteLine((bad == 0 ? "通过" : "失败") + $"：乐器 {Instruments.Names.Count}/{n} 种，缺省参数不一致 {bad} 种");
        return fails;
    }

    static int ValidateAll()
    {
        int bad = 0;
        var sw = Stopwatch.StartNew();
        foreach (var k in MusicLib.Keys())
        {
            var e = MusicLib.Validate(k);
            var C = MusicLib.Compile(k);
            if (e.Count > 0) { bad++; Console.WriteLine("  " + k + "：" + e.Count + " 处 " + string.Join(" / ", e.GetRange(0, Math.Min(3, e.Count)))); }
            else Console.WriteLine($"  {k,-16} 段 {C.sections.Count,2} 曲式 {C.form.Count,2} 采样 {C.sampleKeys.Count,3} 主音 {C.tonicPc,2} 长 {C.introDur:0.0}+{C.loopDur:0.0}s{(C.loop ? "" : "（一次）")}");
        }
        Console.WriteLine((bad == 0 ? "通过" : "失败") + $"：{MusicLib.Keys().Count} 首记谱，{bad} 首有错误（编译 {sw.ElapsedMilliseconds} ms）");
        return bad == 0 ? 0 : 1;
    }

    public static void WriteWav(string path, float[] L, float[] R, int sr)
    {
        int n = L.Length;
        using (var f = new BinaryWriter(File.Create(path)))
        {
            f.Write(Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + n * 4); f.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            f.Write(16); f.Write((short)1); f.Write((short)2); f.Write(sr); f.Write(sr * 4); f.Write((short)4); f.Write((short)16);
            f.Write(Encoding.ASCII.GetBytes("data")); f.Write(n * 4);
            for (int i = 0; i < n; i++)
            {
                f.Write((short)Math.Max(-32767, Math.Min(32767, Math.Round(L[i] * 32767.0, MidpointRounding.AwayFromZero))));
                f.Write((short)Math.Max(-32767, Math.Min(32767, Math.Round(R[i] * 32767.0, MidpointRounding.AwayFromZero))));
            }
        }
    }

    static int RenderKeys(IEnumerable<string> keys, double sec, string outDir, bool strict)
    {
        Directory.CreateDirectory(outDir);
        int bad = 0;
        double total = 0, audio = 0;
        var json = new StringBuilder("[\n");
        bool first = true;
        foreach (var k in keys)
        {
            var errs = MusicLib.Validate(k);
            var r = MusicEngine.RenderOffline(k, sec);
            WriteWav(Path.Combine(outDir, Safe(k) + ".wav"), r.L, r.R, 44100);
            bool pass = r.nan == 0 && r.peak <= 0.98 && r.rms > 0.01 && r.rms < 0.35 && errs.Count == 0;
            if (!pass && strict) bad++;
            total += r.ms; audio += sec;
            Console.WriteLine($"  {(pass ? "通过" : "失败")} {k,-24} 峰值 {r.peak:0.000}  RMS {20 * Math.Log10(Math.Max(1e-9, r.rms)),6:0.0} dB  NaN {r.nan}  渲染 {r.ms,6:0} ms（{sec * 1000 / r.ms:0.0}× 实时）  采样 {r.prepMs,5:0} ms");
            if (!first) json.Append(",\n");
            first = false;
            json.Append($"  {{\"key\":{Q(k)},\"peak\":{r.peak.ToString("R")},\"rms\":{r.rms.ToString("R")},\"nan\":{r.nan},\"ms\":{r.ms:0},\"prepMs\":{r.prepMs:0},\"pass\":{(pass ? "true" : "false")}}}");
        }
        json.Append("\n]\n");
        File.WriteAllText(Path.Combine(outDir, "stats.json"), json.ToString());
        Console.WriteLine($"渲染合计 {total / 1000:0.0} s / 音频 {audio:0} s（{audio * 1000 / Math.Max(1, total):0.0}× 实时，单线程 Mono）；采样缓存 {MusicLib.SampleCount} 个 {MusicLib.SampleFloats * 4 / 1048576.0:0.0} MB");
        return bad;
    }
    static string Safe(string k) { return k.Replace("@", "_at_").Replace("|", "_"); }

    static int Render(double sec, string outDir, List<string> keys)
    {
        if (keys.Count == 0) keys = MusicLib.Keys();
        int bad = RenderKeys(keys, sec, outDir, true);
        Console.WriteLine((bad == 0 ? "通过" : "失败") + $"：{keys.Count} 首，{bad} 首未通过（峰值 ≤ 0.98、−40 dB < RMS < −9 dB、无 NaN、记谱无误）");
        return bad == 0 ? 0 : 1;
    }

    static int Pieces(string file, double sec, string outDir)
    {
        var arr = (List<object>)MJson.Parse(File.ReadAllText(file, Encoding.UTF8));
        var keys = new List<string>();
        foreach (MObj o in arr) { string k = o.Str("key"); MusicLib.Add(k, o.Obj("piece")); keys.Add(k); }
        RenderKeys(keys, sec, outDir, false);
        return 0;
    }

    // 实时模式脚本：0 s 地图 → 6 s 战场（0.8 s 交叉淡入淡出）→ 10 s 冲锋乐句（叠加、对拍、压低主曲）→ 16 s 胜利短曲（一次性）→ 结束回调
    static int Live(string wav)
    {
        const int sr = 44100, B = MusicEngine.BLOCK;
        var e = new MusicEngine(sr, false, 0.42);
        var ended = new List<string>();
        var log = new List<string>();
        e.onEnded = (k, ov) => { lock (ended) ended.Add(k + (ov ? "(叠加)" : "")); };
        int seconds = 40, nb = seconds * sr / B;
        var L = new float[nb * B]; var R = new float[nb * B];
        double maxMs = 0, warmMs = 0, sumMs = 0, tailMs = 0, headMs = 0; var sw = new Stopwatch();
        bool okMap = false, okBattle = false, okClash = false, okVic = false;
        int fails = 0;
        Action<string, PlayOpts, Action<bool>> play = (k, o, cb) => e.Play(k, o, cb);
        for (int b = 0; b < nb; b++)
        {
            double t = b * (double)B / sr;
            if (b == 0) play("map", new PlayOpts { fade = 0.8 }, ok => { okMap = ok; log.Add($"{e.time:0.00}s 地图开始 {ok}"); });
            if (Math.Abs(t - 6) < 0.012) play("battle", new PlayOpts { fade = 0.8 }, ok => { okBattle = ok; log.Add($"{e.time:0.00}s 战场开始 {ok}"); });
            if (Math.Abs(t - 10) < 0.012)
            {
                var info = e.GetMainInfo();
                int st = MusicLib.Tonic("clash");
                int tr = ((info.tonicPc - st) % 12 + 12) % 12; if (tr > 5) tr -= 12;
                string vk = MusicLib.Variant("clash", tr, 0) ?? "clash";
                double nbt = e.NextBeat(e.time + 0.05, 1);
                log.Add($"{t:0.00}s 冲锋乐句 {vk}（主曲 {info.key} {info.bpm} bpm，下一拍 {nbt:0.000}s）");
                play(vk, new PlayOpts { overlay = true, fade = 0.2, duck = 0.3, sync = true }, ok => { okClash = ok; log.Add($"{e.time:0.00}s 冲锋乐句开始 {ok}"); });
            }
            if (Math.Abs(t - 16) < 0.012) play("victory", new PlayOpts { fade = 0.8 }, ok => { okVic = ok; log.Add($"{e.time:0.00}s 胜利短曲开始 {ok}"); });
            sw.Restart();
            e.Render(L, R, b * B, B);
            double bms = sw.Elapsed.TotalMilliseconds;
            if (b >= 40) maxMs = Math.Max(maxMs, bms); else warmMs = Math.Max(warmMs, bms);
            sumMs += bms; if (b % 8 == 7) tailMs = Math.Max(tailMs, bms); else if (b >= 40) headMs = Math.Max(headMs, bms);
            if (b < 200) System.Threading.Thread.Sleep(1);   // 给后台准备线程时间（实时播放时渲染线程本来就在等音频线程）
        }
        int nan = 0; double peak = 0;
        for (int i = 0; i < L.Length; i++) { if (float.IsNaN(L[i]) || float.IsNaN(R[i])) nan++; peak = Math.Max(peak, Math.Max(Math.Abs(L[i]), Math.Abs(R[i]))); }
        WriteWav(wav, L, R, sr);
        foreach (var l in log) Console.WriteLine("  " + l);
        var sbr = new StringBuilder("  每秒 RMS(dB)：");
        for (int s = 0; s < seconds; s++) { double q = 0; for (int i = s * sr; i < Math.Min(L.Length, (s + 1) * sr); i++) q += L[i] * L[i] + R[i] * R[i]; sbr.Append($"{20 * Math.Log10(Math.Sqrt(q / (2.0 * sr)) + 1e-9):0} "); }
        Console.WriteLine(sbr);
        Console.WriteLine("  结束回调：" + string.Join("、", ended) + $"；剩余曲目 {e.Current ?? "无"}；渲染耗时：平均 {sumMs / nb:0.00} ms / 块（块长 {1000.0 * B / sr:0.0} ms），最长 {maxMs:0.0} ms（含尾部卷积的块 {tailMs:0.0} ms，其余 {headMs:0.0} ms；预热期 {warmMs:0.0} ms）；采样 {MusicLib.SampleCount} 个，待渲染 {MusicLib.PendingJobs}");
        if (!okMap || !okBattle || !okClash || !okVic) { fails++; Console.WriteLine("  失败：有曲目没有开始"); }
        if (!ended.Contains("victory")) { fails++; Console.WriteLine("  失败：胜利短曲没有结束回调"); }
        if (!ended.Exists(x => x.StartsWith("clash"))) { fails++; Console.WriteLine("  失败：冲锋乐句没有结束回调"); }
        if (nan > 0 || peak > 0.98 * 0.42 + 1e-6) { fails++; Console.WriteLine($"  失败：NaN {nan}，峰值 {peak:0.000}"); }
        Console.WriteLine((fails == 0 ? "通过" : "失败") + $"：实时模式脚本（{seconds} 秒，峰值 {peak:0.000}，NaN {nan}）→ {wav}");
        e.Dispose();
        return fails;
    }

    static string N(double v) { return double.IsNaN(v) ? "-" : (Math.Round(v * 1e6, MidpointRounding.AwayFromZero) / 1e6).ToString("F6", CultureInfo.InvariantCulture); }
    static string O(Orn o)
    {
        if (o == null) return "";
        return (o.vib != 0 ? "vb" + o.vib : "") + (o.trem ? "tm" : "") + (o.grace != 0 ? "ge" + o.grace : "") + (o.slide ? "se" : "") + (o.fall ? "fl" : "") + (o.acc ? "ac" : "")
            + (o.swell ? "sl" : "") + (o.tie ? "te" : "") + (o.gliss != 0 ? "gs" + o.gliss : "") + (o.glissDown ? "gn" : "");
    }
    static string J(double[] a) { if (a == null) return ""; var l = new List<string>(); foreach (var x in a) l.Add(N(x)); return string.Join(",", l); }
    static int Dump(string file)
    {
        var sb = new StringBuilder();
        foreach (var key in MusicLib.Keys())
        {
            var C = MusicLib.Compile(key);
            sb.Append($"P {key} tonic={C.tonicPc} intro={N(C.introDur)} loop={N(C.loopDur)} loopFrom={C.loopFrom} form={string.Join(",", C.form)} keys={string.Join(",", C.sampleKeys)}\n");
            foreach (var sn in C.piece.Obj("sections").Keys)
            {
                Section S; if (!C.sections.TryGetValue(sn, out S)) continue;
                sb.Append($"S {sn} beats={N(S.beats)} bpm={N(S.bpm)} bpmTo={N(S.bpmTo)} dur={N(S.dur)} n={S.items.Count}\n");
                foreach (var it in S.items)
                {
                    sb.Append($"I {it.type} {it.voice ?? it.lane} {it.inst} {N(it.beat)} {(it.type == 'd' || it.type == 'p' ? "-" : N(it.dur))} {(it.type == 'p' ? "-" : N(it.vel))} {J(it.ps)} {N(it.gp)} {N(it.tp)} {J(it.run)} {(it.type == 'd' ? it.art.ToString() : "-")} {N(it.p)} {O(it.orn)}\n");
                    if (it.notes != null) foreach (var n in it.notes) sb.Append($"  n {N(n.beat)} {N(n.dur)} {N(n.p)} {N(n.vel)} {N(n.gp)} {N(n.tp)} {O(n.orn)}\n");
                }
            }
        }
        File.WriteAllText(file, sb.ToString());
        return 0;
    }

    // 采样文件格式（Node 写出）：重复 [u16 键长][键 UTF-8][i32 采样率][i32 长度][f32 × 长度]
    static int Samples(string file)
    {
        int bad = 0, n = 0;
        double worst = 0; string worstK = "";
        using (var f = new BinaryReader(File.OpenRead(file)))
        {
            while (f.BaseStream.Position < f.BaseStream.Length)
            {
                int kl = f.ReadUInt16();
                string fk = Encoding.UTF8.GetString(f.ReadBytes(kl));
                int sr = f.ReadInt32(), len = f.ReadInt32();
                var js = new float[len];
                for (int i = 0; i < len; i++) js[i] = f.ReadSingle();
                int csr;
                var cs = MusicLib.RenderSample(fk, out csr);
                n++;
                if (cs == null) { bad++; Console.WriteLine("  " + fk + "：C# 无法渲染"); continue; }
                double md = 0, e = 0;
                int m = Math.Min(len, cs.Length);
                for (int i = 0; i < m; i++) { md = Math.Max(md, Math.Abs(js[i] - cs[i])); e += (double)js[i] * js[i]; }
                bool ok = csr == sr && Math.Abs(cs.Length - len) <= 1 && md < 1e-3;
                if (md > worst) { worst = md; worstK = fk; }
                if (!ok) { bad++; Console.WriteLine($"  {fk}：长度 {cs.Length}/{len}，采样率 {csr}/{sr}，最大差 {md:0.000000}"); }
            }
        }
        Console.WriteLine((bad == 0 ? "通过" : "失败") + $"：采样 {n} 个与 JS 逐点比对，{bad} 个不一致；最大差 {worst:0.0e0}（{worstK}）");
        return bad == 0 ? 0 : 1;
    }
}
