// 单挑画面（第三版）的无头测试：DuelGame.cs / DuelFigure.cs / DuelArena.cs 原样编译（界面换成 DuelStub.cs 的记录替身）。
//   流程（flow）：真实的 DuelScreen —— Setup → 协程（开场、模型库预生成、建场景、亮相、操作说明、开战）→ 每帧 Update（逻辑步进、
//     事件表现、武将视图、场地、镜头）→ 结束演出与清理。两场：关羽（脚本玩家 parry：格开 → 反击）对 吕布（电脑）；
//     黄忠 对 刘备（电脑对电脑，开战时双方怒气满：弓将连珠箭与近战绝技）。检查：无异常、走完全程、结果与统计、
//     出现过的状态（待机 / 连段 / 格开 / 被格开 / 反击 / 重击 / 击倒 / 倒地 / 绝技 / 胜负）与每个状态用到的动作片段、
//     界面调用（「格开！」飘字、四周金光、反击小圆、开战公告、胜负面板）、音效（格开、铮、挥击）、胜方欢呼、弓弦与插地兵器。
//   对照（compare）：与网页版 web-<名>.json 逐项比对 —— 兵器与盾的选择、兵器长度校准（缩放、前冲上限、各招 tc / te / dur / tip /
//     grip / target / lunge、逐帧尖端采样）、场地（实例化景物逐类统计、各网格的顶点数与坐标和、火把、两军人数、场地随机数的下一个值）、各镜头的片段、
//     片段时刻、键、根节点、转身角、前冲、辉光、刃尖 / 刃根与骨骼世界坐标（three 坐标）。
//   渲染：各镜头由 DuelRender.cs 画成 unity-<名>-<镜头>.png。
// 用法：mono duelview.exe <输出目录> [名…]（run.sh 调用）；环境变量 NO_RENDER=1 不画图，FLOW_ONLY=1 只跑流程。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sanguo;
using UnityEngine;

static class DuelViewHarness
{
    static int fails, checks;
    static string cur = "";
    static void Fail(string what) { fails++; Console.WriteLine("  ✗ " + cur + " " + what); }
    static void Ok(bool ok, string what) { checks++; if (!ok) Fail(what); }
    static void Eq(string what, object cs, object web)
    {
        checks++;
        string a = cs == null ? "null" : Convert.ToString(cs, System.Globalization.CultureInfo.InvariantCulture), b = web == null ? "null" : Convert.ToString(web, System.Globalization.CultureInfo.InvariantCulture);
        if (a != b) Fail(what + "：C# " + a + " ≠ 网页 " + b);
    }
    static readonly Dictionary<string, double> maxDiff = new Dictionary<string, double>();
    static readonly Dictionary<string, HashSet<string>> allStates = new Dictionary<string, HashSet<string>>();
    static void Near(string what, double cs, double web, double tol, string cat = null)
    {
        checks++;
        double d = Math.Abs(cs - web);
        if (cat != null) { double m; maxDiff.TryGetValue(cat, out m); if (d > m || double.IsNaN(d)) maxDiff[cat] = double.IsNaN(d) ? 99 : d; }
        if (double.IsNaN(cs) || double.IsNaN(web) || d > tol) Fail(what + string.Format("：C# {0:G7} ≠ 网页 {1:G7}（差 {2:G3}，容差 {3:G3}）", cs, web, cs - web, tol));
    }
    static double Num(object o) { return o is double ? (double)o : o is int ? (int)o : o is long ? (long)o : o is bool ? ((bool)o ? 1 : 0) : double.NaN; }
    static List<object> L(object o) { return o as List<object>; }
    static T F<T>(object o, string name)
    {
        var fi = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (fi == null) throw new Exception("没有字段 " + o.GetType().Name + "." + name);
        return (T)fi.GetValue(o);
    }
    static void Call(object o, string name) { o.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(o, null); }
    static Vector3 Three(Vector3 u) { return new Vector3(u.x, u.y, -u.z); }

    // ------------------------------------------------------------ 协程（替身登记的协程逐帧推进；嵌套的 IEnumerator 一并推进）
    static readonly List<Stack<IEnumerator>> co = new List<Stack<IEnumerator>>();
    static void PumpCoroutines()
    {
        foreach (var e in DuelStubHooks.Coroutines) { var st = new Stack<IEnumerator>(); st.Push(e); co.Add(st); }
        DuelStubHooks.Coroutines.Clear();
        for (int i = 0; i < co.Count; i++)
        {
            var st = co[i];
            while (st.Count > 0)
            {
                var top = st.Peek();
                if (!top.MoveNext()) { st.Pop(); continue; }
                var c = top.Current as IEnumerator;
                if (c != null) { st.Push(c); continue; }
                break;
            }
            if (st.Count == 0) co.RemoveAt(i--);
        }
    }

    static General Gen(JObj g)
    {
        return new General { name = g.S("name"), war = (int)Num(g.G("war")), intel = (int)Num(g.G("intel")), pol = (int)Num(g.G("pol")), culture = g.S("culture") };
    }
    static Color Col(string hex) { Color c; ColorUtility.TryParseHtmlString(hex, out c); c.a = 1; return c; }

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string dir = args[0];
        var only = args.Skip(1).ToList();
        bool noRender = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_RENDER"));
        bool flowOnly = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLOW_ONLY"));
        UnityEngine.Random.InitState(7);
        DuelSim.Rnd = () => UnityEngine.Random.value;      // = DuelGame 的 InitRandom（无头时不跑 RuntimeInitializeOnLoadMethod）：流程测试可重复
        DuelLooks.CultureLookup = null;      // 文化取自武将（world-data 的 culture 列），与网页版测试页相同
        var gensFile = Path.Combine(dir, "web-gens.json");
        var gens = new Dictionary<string, JObj>();
        if (File.Exists(gensFile)) foreach (JObj g in (List<object>)JObj.ParseJson(File.ReadAllText(gensFile))) gens[g.S("name")] = g;
        var cfg = (JObj)JObj.ParseJson(File.ReadAllText(Path.Combine(dir, "cases.json")));
        var t0 = DateTime.Now;
        if (only.Count == 0 || only.Contains("flow"))
        {
            Flow("flow-parry", gens, "关羽", "吕布", Terrain.Plain, 0, "parry", false);
            Flow("flow-bow", gens, "黄忠", "刘备", Terrain.Castle, -1, null, true);
            cur = "flow";
            // 两场合计须走过：待机、连段三击、重击、受击、格开 / 被格开 / 格开得手、击倒、倒地、绝技、胜利
            foreach (var st in new[] { "idle", "walk", "light1", "light2", "light3", "heavy", "hitstun", "parried", "parryok", "knockdown", "down", "special", "victory" })
                Ok(allStates.ContainsKey(st), "两场合计出现过状态 " + st);
        }
        if (!flowOnly)
            foreach (JObj c in L(cfg.G("cases")))
            {
                string name = c.S("name");
                if (only.Count > 0 && !only.Contains(name)) continue;
                var wf = Path.Combine(dir, "web-" + name + ".json");
                if (!File.Exists(wf)) { Console.WriteLine("== " + name + "：没有网页版参考 " + wf + "，跳过"); continue; }
                Compare(c, (JObj)JObj.ParseJson(File.ReadAllText(wf)), cfg, gens, dir, noRender);
            }
        Console.WriteLine("== 最大差：" + string.Join("，", maxDiff.Select(kv => kv.Key + " " + kv.Value.ToString("G3"))));
        Console.WriteLine(fails == 0 ? "== 全部通过（" + checks + " 项，" + (DateTime.Now - t0).TotalSeconds.ToString("0") + " 秒）" : "== 不一致 " + fails + " / " + checks + " 项");
        return Math.Min(fails, 98);
    }

    // ------------------------------------------------------------ 流程
    static DuelScreen NewScreen(General a, Color ca, General b, Color cb, int player, Terrain t)
    {
        co.Clear(); DuelStubHooks.Coroutines.Clear();
        var go = new GameObject("DuelScreen");
        var s = go.AddComponent<DuelScreen>();
        s.Setup(a, ca, b, cb, player, t);
        return s;
    }
    static void Frame(DuelScreen s, float dt)
    {
        Time.unscaledDeltaTime = dt; Time.deltaTime = dt; Time.time += dt;
        Call(s, "Update");
        PumpCoroutines();
    }

    static void Flow(string name, Dictionary<string, JObj> gens, string an, string bn, Terrain terr, int player, string bot, bool fullRage)
    {
        cur = name;
        Console.WriteLine("== 流程 " + name + "：" + an + " 对 " + bn + (bot != null ? "（玩家一方 = 脚本 " + bot + "）" : "（电脑对电脑）"));
        var tt = DateTime.Now;
        DuelHud.Log.Clear(); AudioSource.Played.Clear(); Sfx.Log.Clear();
        General a = gens.ContainsKey(an) ? Gen(gens[an]) : new General { name = an, war = 90, intel = 60, pol = 50, culture = "han" };
        General b = gens.ContainsKey(bn) ? Gen(gens[bn]) : new General { name = bn, war = 90, intel = 60, pol = 50, culture = "han" };
        Color ca = Col(gens.ContainsKey(an) ? gens[an].S("color") : "#2f9a55"), cb = Col(gens.ContainsKey(bn) ? gens[bn].S("color") : "#7a2a3a");
        DuelGame.Ctrl = bot != null ? new Func<DuelFighter, DuelSim, IDuelCtrl>[] { (f, sm) => new DuelScriptBot(bot, f.idx), null } : null;
        DuelGame.Speed = 1;
        var s = NewScreen(a, ca, b, cb, player, terr);
        Ok(s.scripted == (bot != null), "scripted 标志");
        var sim = s.sim;
        var statesSeen = new Dictionary<string, HashSet<string>>();   // 状态 → 用到的片段
        var events = new HashSet<string>();
        bool raged = false, cheered = false, bowSeen = false, plantedSeen = false, stringSeen = false;
        int frames = 0; double maxTint = 0;
        string lastErr = null;
        while (!s.finished && frames < 60 * 150)
        {
            // 事件在 Events() 里清空：逐帧在 Update 之前读不到，改在 sim.Step 之后的 Update 里——这里用 sim.events 的快照（Step 由 Update 驱动）
            Frame(s, 1f / 60);
            frames++;
            if (!F<bool>(s, "built")) continue;
            var war = F<DuelFigure[]>(s, "war");
            if (war == null) continue;
            if (fullRage && sim.running && !raged) { sim.f[0].rage = 100; sim.f[1].rage = 100; raged = true; }
            for (int i = 0; i < 2; i++)
            {
                var f = sim.f[i]; var w = war[i];
                HashSet<string> hs; if (!statesSeen.TryGetValue(f.state, out hs)) statesSeen[f.state] = hs = new HashSet<string>();
                hs.Add(w.fig.ClipName);
                if (w.bowOn) { bowSeen = true; var pl = F<GameObject>(w, "planted"); if (pl != null && pl.activeSelf) plantedSeen = true; var ls = F<LineRenderer>(w, "bowString"); if (ls != null && ls.enabled) stringSeen = true; }
                maxTint = Math.Max(maxTint, Math.Max(w.tint.r, Math.Max(w.tint.g, w.tint.b)));
            }
            var arena = F<DuelArena>(s, "arena");
            if (arena != null && arena.cheered) cheered = true;
        }
        Ok(s.finished, "走完全程（" + frames + " 帧）");
        Ok(!F<bool>(s, "failed") && F<int>(s, "errors") == 0, "无异常（errors " + F<int>(s, "errors") + "，" + (lastErr ?? "") + "）");
        var r = s.result;
        Ok(r != null && (r.winner == 0 || r.winner == 1), "结果");
        if (r != null)
        {
            Ok(r.stats != null && r.stats.Length == 2, "结果带格斗统计（stats）");
            Console.WriteLine(string.Format("   结果：{0} 胜 [{1}] 体力 {2}:{3}，命中 {4} 次，{5:0.0} 秒；格开 {6}/{7} : {8}/{9}，反击 {10}:{11}",
                r.winner == 0 ? an : bn, r.kind, r.hpA, r.hpB, r.log.Count, r.time,
                r.stats[0].parries, r.stats[0].parryTries, r.stats[1].parries, r.stats[1].parryTries, r.stats[0].counters, r.stats[1].counters));
        }
        Console.WriteLine("   状态 → 片段：" + string.Join("；", statesSeen.OrderBy(kv => kv.Key).Select(kv => kv.Key + " " + string.Join("/", kv.Value))));
        // 每个状态对应的片段（= 网页版 FigWarrior.pick）
        var expect = new Dictionary<string, string[]>
        {
            { "idle", new[] { "idle", "victory", "block" } }, { "walk", new[] { "idle" } }, { "guard", new[] { "block" } }, { "guardstun", new[] { "block" } },
            { "light1", new[] { "light1" } }, { "light2", new[] { "light2" } }, { "light3", new[] { "light3" } }, { "heavy", new[] { "heavy" } }, { "air", new[] { "heavy" } },
            { "charge", new[] { "heavy" } }, { "hitstun", new[] { "hit", "hitB" } }, { "guardbreak", new[] { "guardbreak" } }, { "parried", new[] { "guardbreak" } },
            { "parry", new[] { "parry" } }, { "parryok", new[] { "parry" } }, { "parrywhiff", new[] { "parry" } }, { "backstep", new[] { "block" } },
            { "knockdown", new[] { "knockdown" } }, { "down", new[] { "knockdown", "down" } }, { "getup", new[] { "getup" } }, { "dash", new[] { "dash" } },
            { "jump", new[] { "jump" } }, { "land", new[] { "jump" } }, { "victory", new[] { "victory" } }, { "defeat", new[] { "defeat", "down" } },
            { "special", new[] { "heavy", "light1", "light2", "light3", "idle", "bowDraw", "bowShoot" } },
        };
        foreach (var kv in statesSeen)
        {
            string[] ok; if (!expect.TryGetValue(kv.Key, out ok)) { Fail("未预期的状态 " + kv.Key); continue; }
            foreach (var c in kv.Value) Ok(ok.Contains(c), "状态 " + kv.Key + " 用了片段 " + c);
        }
        foreach (var kv in statesSeen) { HashSet<string> u; if (!allStates.TryGetValue(kv.Key, out u)) allStates[kv.Key] = u = new HashSet<string>(); u.UnionWith(kv.Value); }
        var log = DuelHud.Log;
        Func<string, bool> has = p => log.Any(x => x.StartsWith(p, StringComparison.Ordinal));
        Ok(has("Announce 开　战"), "开战公告");
        Ok(has("WinPanel "), "胜负面板");
        Ok(has("IntroVS"), "开场 VS");
        if (player >= 0) Ok(has("ControlsCard"), "操作说明卡");
        if (bot == "parry")
        {
            // 格开架势（parry）可能不足一帧就转入得手（parryok：脚本玩家按得很准），逐帧取样时看不到，所以只要求得手 / 被格开
            foreach (var st in new[] { "parryok", "parried" }) Ok(statesSeen.ContainsKey(st), "出现过状态 " + st);
            Ok(has("FloatDmg 格开！ [lbl parry]"), "「格开！」飘字");
            Ok(has("EdgeFlash"), "四周金光");
            Ok(has("CounterChip"), "反击小圆");
            Ok(has("ParryButtonFlash"), "触摸格开按钮闪光（玩家格开）");
            Ok(AudioSource.Played.Any(x => x.StartsWith("Duel_parry")), "格开的金铁交鸣");
            Ok(r != null && r.stats[0].parries > 0, "玩家一方格开成功");
            Ok(has("FloatDmg 反击 [lbl ctr]") || (r != null && r.stats[0].counters == 0), "反击飘字（有反击时）");
        }
        if (fullRage)
        {
            Ok(has("CutIn "), "绝技特写");
            Ok(bowSeen && plantedSeen && stringSeen, "弓将连珠箭：换弓、插地兵器、弓弦（" + bowSeen + "/" + plantedSeen + "/" + stringSeen + "）");
            Ok(AudioSource.Played.Any(x => x.StartsWith("Duel_twang")), "弓弦声");
        }
        Ok(AudioSource.Played.Any(x => x.StartsWith("Duel_whoosh")), "挥击风声");
        Ok(cheered, "胜方欢呼");
        Ok(maxTint > 0.2, "辉光（受击 / 格开 / 反击 / 蓄力）出现过，最大 " + maxTint.ToString("0.00"));
        Ok(F<GameObject>(s, "world") == null, "结束后场景已拆除");
        var hits = log.Count(x => x.StartsWith("FloatDmg"));
        Console.WriteLine("   界面调用 " + log.Count + " 次（飘字 " + hits + "），音效 " + AudioSource.Played.Count + " 次；" + (DateTime.Now - tt).TotalSeconds.ToString("0") + " 秒");
        DuelGame.Ctrl = null;
    }

    // ------------------------------------------------------------ 对照与渲染
    static void Compare(JObj c, JObj web, JObj cfg, Dictionary<string, JObj> gens, string dir, bool noRender)
    {
        string name = c.S("name");
        cur = name;
        var tt = DateTime.Now;
        var wg = L(web.G("gens"));
        var wc = L(web.G("cols"));
        General a = Gen((JObj)wg[0]), b = Gen((JObj)wg[1]);
        Color ca = Col(wc != null ? (string)wc[0] : ((JObj)wg[0]).S("color")), cb = Col(wc != null ? (string)wc[1] : ((JObj)wg[1]).S("color"));
        if (wc == null && gens.ContainsKey(a.name)) { ca = Col(gens[a.name].S("color")); cb = Col(gens[b.name].S("color")); }
        DuelGame.Ctrl = null;
        SystemInfo.supportsInstancing = noRender;    // 画图时士兵群走回退路径（每名士兵一个 Figure），植被逐株 DrawMesh
        var s = NewScreen(a, ca, b, cb, -1, (Terrain)(int)Num(c.G("terrain")));
        int guard = 0;
        while (!F<bool>(s, "built") && guard++ < 2000) Frame(s, 1f / 60);
        co.Clear();      // 场景已建好：不再推进流程（网页版同样接管了 DuelScreen.update）
        var sim = s.sim; var war = F<DuelFigure[]>(s, "war"); var arena = F<DuelArena>(s, "arena"); var cam = F<Camera>(s, "cam");
        Console.WriteLine("== 对照 " + name + "：" + a.name + " 对 " + b.name + "（" + arena.kind + "）");
        // ---- 武将与兵器校准
        var ww = L(web.G("war"));
        for (int i = 0; i < 2; i++)
        {
            var w = war[i]; var o = (JObj)ww[i]; string p = "武将" + i + "（" + o.S("name") + "）";
            Eq(p + " 文化", w.look.culture, o.S("culture"));
            Eq(p + " 规则兵器", w.look.weapon, o.S("weapon"));
            Eq(p + " 模型兵器", w.figWeapon, o.S("figWeapon"));
            Eq(p + " 兵器流派", w.fstyle, o.S("fstyle"));
            Eq(p + " 盾", w.fig.ShieldMesh != null, (bool)o.G("hasShield"));
            Eq(p + " 披风", w.fig.HasCape, (bool)o.G("hasCape"));
            Eq(p + " 弓将", w.HasBow, (bool)o.G("bow"));
            if (w.HasBow) Eq(p + " 弓", w.BowName, o.S("bowName"));
            Eq(p + " 双持", w.wpn2, (bool)o.G("wpn2"));
            Near(p + " reach", w.f.reach, Num(o.G("reach")), 1e-12);
            Near(p + " 兵器缩放", w.wScale, Num(o.G("wScale")), 2e-4, "缩放");
            Near(p + " 前冲上限", w.lungeMax, Num(o.G("lungeMax")), 2e-4, "前冲上限");
            var oc = (JObj)o.G("cal");
            foreach (var k in new[] { "light1", "light2", "light3", "heavy", "air" })
            {
                var q = w.cal[k]; var r = (JObj)oc.G(k); string pk = p + " " + k;
                Near(pk + ".tc", q.tc, Num(r.G("tc")), 1e-9, "tc/te");
                Near(pk + ".te", q.te, Num(r.G("te")), 1e-9, "tc/te");
                Near(pk + ".dur", q.dur, Num(r.G("dur")), 1e-9);
                Near(pk + ".tip", q.tip, Num(r.G("tip")), 2e-4, "tip/grip");
                Near(pk + ".grip", q.grip, Num(r.G("grip")), 2e-4, "tip/grip");
                Near(pk + ".target", q.target, Num(r.G("target")), 1e-9);
                Near(pk + ".lunge", q.lunge, Num(r.G("lunge")), 2e-4, "lunge");
                Eq(pk + ".n", q.n, (int)Num(r.G("n")));
                var T = L(r.G("T")); var X = L(r.G("X")); var Y = L(r.G("Y"));
                for (int j = 0; j < Math.Min(q.n, T.Count); j++)
                {
                    Near(pk + ".T[" + j + "]", q.T[j], Num(T[j]), 1e-6);
                    Near(pk + ".X[" + j + "]", q.X[j], Num(X[j]), 3e-4, "采样 X/Y");
                    Near(pk + ".Y[" + j + "]", q.Y[j], Num(Y[j]), 3e-4, "采样 X/Y");
                }
            }
            Console.WriteLine(string.Format("   {0}：{1}（{2}）×{3:0.000}，前冲上限 {4:0.00}；light1 tip {5:0.000} / target {6:0.000}，heavy lunge {7:0.000}{8}",
                o.S("name"), w.figWeapon, w.fstyle, w.wScale, w.lungeMax, w.cal["light1"].tip, w.cal["light1"].target, w.cal["heavy"].lunge, w.fig.ShieldMesh != null ? "，盾" : ""));
        }
        // ---- 场地
        var wa = (JObj)web.G("arena");
        Eq("场地种类", arena.kind, wa.S("kind"));
        var inst = InstSums(arena);
        var wi = (JObj)wa.G("inst");
        foreach (var kv in wi)
        {
            var r = (JObj)kv.Value; object ci; inst.TryGetValue(kv.Key, out ci);
            var cs = ci as double[];
            if (cs == null) { Fail("景物 " + kv.Key + "：C# 没有"); continue; }
            Eq("景物 " + kv.Key + " 数量", (int)cs[0], (int)Num(r.G("n")));
            Near("景物 " + kv.Key + " Σx", cs[1], Num(r.G("x")), 1e-3, "景物和");
            Near("景物 " + kv.Key + " Σy", cs[2], Num(r.G("y")), 1e-3, "景物和");
            Near("景物 " + kv.Key + " Σz", cs[3], Num(r.G("z")), 1e-3, "景物和");
            Near("景物 " + kv.Key + " Σs", cs[4], Num(r.G("s")), 1e-3, "景物和");
            Near("景物 " + kv.Key + " Σyaw", cs[5], Num(r.G("yaw")), 1e-3, "景物和");
        }
        foreach (var k in inst.Keys) if (!wi.ContainsKey(k)) Fail("景物 " + k + "：网页版没有");
        var wt = L(wa.G("torches"));
        Eq("火把数", arena.torches.Count, wt.Count);
        for (int j = 0; j < Math.Min(arena.torches.Count, wt.Count); j++)
        {
            var p = L(wt[j]);
            Near("火把" + j + ".x", arena.torches[j].x, Num(p[0]), 1e-4); Near("火把" + j + ".y", arena.torches[j].y, Num(p[1]), 1e-4); Near("火把" + j + ".z", arena.torches[j].z, Num(p[2]), 1e-4);
        }
        var tr = L(wa.G("troops"));
        if (tr != null) { Eq("甲军人数", arena.troops[0].Count, (int)Num(tr[0])); Eq("乙军人数", arena.troops[1].Count, (int)Num(tr[1])); }
        // 网页版的网格（顶点数与世界坐标和，three 坐标）：两边都先 arena.update(arenaT)（旗面按同一时刻摆好，= web.mjs），
        //   按顶点数分组、组内按坐标和就近配对，逐个比对（容差：每顶点平均 3e-6 米，至少 0.003；实测地面 7665 个顶点差 0.0094，其余 ≤ 0.0015）
        arena.Update((float)Num(cfg.G("arenaT")));
        Graphics.Meshes.Clear();
        var cmesh = AllMeshes(arena.group.transform).ToList();
        var wm = L(wa.G("meshes")).Cast<JObj>().Select(m => new[] { Num(m.G("n")), Num(m.G("sx")), Num(m.G("sy")), Num(m.G("sz")) }).ToList();
        Console.WriteLine("   网格（顶点数 Σx Σy Σz，three 坐标）网页 " + wm.Count + " 个 / C# " + cmesh.Count + " 个");
        Eq("场地网格数", cmesh.Count, wm.Count);
        var used = new bool[cmesh.Count];
        double worst = 0; string worstAt = "—";
        foreach (var m in wm.OrderBy(m => m[0]).ThenBy(m => m[1]))
        {
            int bi = -1; double bd = double.MaxValue;
            for (int j = 0; j < cmesh.Count; j++)
            {
                if (used[j] || cmesh[j][0] != m[0]) continue;
                double d = Math.Abs(cmesh[j][1] - m[1]) + Math.Abs(cmesh[j][2] - m[2]) + Math.Abs(cmesh[j][3] - m[3]);
                if (d < bd) { bd = d; bi = j; }
            }
            string pm = string.Format("网格（{0} 个顶点，网页 Σ {1:0.0} {2:0.0} {3:0.0}）", m[0], m[1], m[2], m[3]);
            if (bi < 0) { Fail(pm + "：C# 没有同顶点数的网格"); continue; }
            used[bi] = true; var q = cmesh[bi];
            double tol = Math.Max(0.003, m[0] * 3e-6);
            Near(pm + " Σx", q[1], m[1], tol, "网格和"); Near(pm + " Σy", q[2], m[2], tol, "网格和"); Near(pm + " Σz", q[3], m[3], tol, "网格和");
            for (int k = 1; k <= 3; k++) if (Math.Abs(q[k] - m[k]) > worst) { worst = Math.Abs(q[k] - m[k]); worstAt = m[0] + " 个顶点的 Σ" + "xyz"[k - 1]; }
        }
        Console.WriteLine("   网格和最大差 " + worst.ToString("G3") + "（" + worstAt + "）");
        for (int j = 0; j < cmesh.Count; j++)
            if (!used[j]) Fail(string.Format("网格（{0} 个顶点，C# Σ {1:0.0} {2:0.0} {3:0.0}）：网页版没有", cmesh[j][0], cmesh[j][1], cmesh[j][2], cmesh[j][3]));
        // ---- 镜头
        var shots = (JObj)cfg.G("shots");
        int steps = (int)Num(cfg.G("steps")); float dt = (float)Num(cfg.G("dt"));
        var times = L(cfg.G("time")); var C = (JObj)cfg.G("cam");
        var wshots = L(web.G("shots"));
        foreach (JObj ws in wshots)
        {
            string sn = ws.S("name"); var spec = (JObj)shots.G(sn);
            sim.clock = Num(cfg.G("clock"));
            for (int i = 0; i < 2; i++)
            {
                var f = sim.f[i]; var o = (JObj)L(spec.G("f"))[i];
                f.state = "idle"; f.t = 0; f.seq = 0; f.y = 0; f.vx = 0; f.vy = 0; f.charge = 0; f.stun = 0.3; f.hitH = "mid"; f.hitBack = false; f.rage = 0; f.ko = false; f.counterUntil = 0; f.parryReadyAt = 0;
                f.face = i == 0 ? 1 : -1;
                foreach (var kv in o)
                {
                    switch (kv.Key)
                    {
                        case "state": f.state = (string)kv.Value; break;
                        case "t": f.t = Num(kv.Value); break;
                        case "seq": f.seq = (int)Num(kv.Value); break;
                        case "x": f.x = Num(kv.Value); break;
                        case "y": f.y = Num(kv.Value); break;
                        case "vx": f.vx = Num(kv.Value); break;
                        case "vy": f.vy = Num(kv.Value); break;
                        case "charge": f.charge = Num(kv.Value); break;
                        case "stun": f.stun = Num(kv.Value); break;
                        case "hitH": f.hitH = (string)kv.Value; break;
                        case "rage": f.rage = Num(kv.Value); break;
                        case "ko": f.ko = (bool)kv.Value; break;
                        case "counterUntil": f.counterUntil = Num(kv.Value); break;
                        default: throw new Exception("镜头字段 " + kv.Key);
                    }
                }
                war[i].intro = -1;
                // 弹簧（披风 / 盔缨 / 发）与人物时钟归零（= web.mjs）
                var rig = war[i].fig.Rig;
                foreach (var fn in new[] { "cape", "capeV", "plume", "plumeV", "hair", "hairV", "time" }) rig.GetType().GetField(fn, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(rig, 0.0);
                rig.GetType().GetField("live", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(rig, false);
            }
            for (int i = 0; i < 2; i++) war[i].Clock = Num(times[i]) - steps * dt;
            for (int k = 0; k < steps; k++)
                for (int i = 0; i < 2; i++)
                {
                    var w = war[i];
                    w.ctr = sim.Countering(sim.f[i]) ? sim.f[i].counterUntil - sim.clock : 0;
                    w.Update(dt, false);
                }
            if (!noRender) Graphics.Recorded = new List<Graphics.InstancedDraw>();
            Graphics.Meshes.Clear();
            arena.Update((float)Num(cfg.G("arenaT")));
            float mid = (float)(sim.f[0].x + sim.f[1].x) / 2;
            cam.transform.position = DuelXf.P(mid, (float)Num(C.G("h")), (float)Num(C.G("dist")));
            cam.transform.LookAt(DuelXf.P(mid, (float)Num(C.G("look")), 0), Vector3.up);
            // 对照
            var wf = L(ws.G("f"));
            for (int i = 0; i < 2; i++)
            {
                var w = war[i]; var o = (JObj)wf[i]; string p = sn + " 武将" + i;
                Eq(p + " 片段", w.fig.ClipName, o.S("clip"));
                Near(p + " 片段时刻", w.fig.ClipT, Num(o.G("t")), 1e-6, "片段时刻");
                Eq(p + " 键", w.Key, o.S("key"));
                Near(p + " 转身角", w.Turn, Num(o.G("turn")), 1e-6, "转身角");
                Near(p + " 前冲", w.LungeX, Num(o.G("lungeX")), 1e-4, "前冲");
                Eq(p + " 弓", w.bowOn, (bool)o.G("bowOn"));
                var rt = Three(w.root.transform.position); var orr = L(o.G("root"));
                for (int k = 0; k < 3; k++) Near(p + " 根[" + k + "]", rt[k], Num(orr[k]), 2e-4, "根节点");
                var tint = L(o.G("tint"));
                Near(p + " 辉光 r", w.tint.r, Num(tint[0]), 2e-4, "辉光"); Near(p + " 辉光 g", w.tint.g, Num(tint[1]), 2e-4, "辉光"); Near(p + " 辉光 b", w.tint.b, Num(tint[2]), 2e-4, "辉光");
                var tip = Three(w.tipW[0]); var otip = L(o.G("tip")); var bas = Three(w.baseW[0]); var obas = L(o.G("base"));
                for (int k = 0; k < 3; k++) { Near(p + " 刃尖[" + k + "]", tip[k], Num(otip[k]), 3e-3, "刃尖 / 刃根"); Near(p + " 刃根[" + k + "]", bas[k], Num(obas[k]), 3e-3, "刃尖 / 刃根"); }
                foreach (var kv in (JObj)o.G("bones"))
                {
                    Transform bt; if (!w.fig.Bones.TryGetValue(kv.Key, out bt)) { Fail(p + " 骨骼 " + kv.Key + "：C# 没有"); continue; }
                    var bp = Three(bt.position); var ob = L(kv.Value);
                    for (int k = 0; k < 3; k++) Near(p + " 骨骼 " + kv.Key + "[" + k + "]", bp[k], Num(ob[k]), 3e-3, "骨骼");
                }
            }
            if (!noRender)
            {
                var keyL = F<Light>(s, "keyL"); var rimL = F<Light>(s, "rimL");
                DuelRender.Frame(new DuelRender.Scene { world = F<GameObject>(s, "world").transform, cam = cam, pal = arena.pal, key = keyL, rim = rimL, inst = Graphics.Recorded, meshes = Graphics.Meshes.ToList() },
                    Path.Combine(dir, "unity-" + name + "-" + sn + ".png"), 960, 540);
                Graphics.Recorded = null;
            }
        }
        // 场地随机数的下一个值（取用次数与顺序一致时相同）
        var rnd = F<SeededRandom>(arena, "rnd");
        Near("场地随机数的下一个值", rnd.NextDouble(), Num(wa.G("rndNext")), 1e-12);
        Console.WriteLine("   " + wshots.Count + " 个镜头；" + (DateTime.Now - tt).TotalSeconds.ToString("0") + " 秒");
        Call(s, "Cleanup");
        SystemInfo.supportsInstancing = true;
    }

    // 实例化景物逐类统计：[数量, Σx, Σ(y − sink·s), Σz, Σs, Σyaw]（three 坐标，= 网页版 arena.inst）
    static Dictionary<string, object> InstSums(DuelArena arena)
    {
        var res = new Dictionary<string, object>();
        var dict = (IDictionary)arena.GetType().GetField("inst", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(arena);
        foreach (DictionaryEntry kv in dict)
        {
            var s = new double[6];
            foreach (var o in (IList)kv.Value)
            {
                Func<string, double> g = n => Convert.ToDouble(o.GetType().GetField(n).GetValue(o));
                s[0]++; s[1] += g("x"); s[2] += g("y") - g("sink") * g("s"); s[3] += g("z"); s[4] += g("s"); s[5] += g("yaw");
            }
            res[(string)kv.Key] = s;
        }
        return res;
    }
    // 场地根下全部网格：顶点数与世界坐标和（three 坐标）
    static IEnumerable<double[]> AllMeshes(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null && t.gameObject.activeInHierarchy)
        {
            var m = t.localToWorldMatrix; var v = mf.sharedMesh.vertices; double sx = 0, sy = 0, sz = 0;
            foreach (var p in v) { var q = m.MultiplyPoint3x4(p); sx += q.x; sy += q.y; sz -= q.z; }
            yield return new double[] { v.Length, sx, sy, sz };
        }
        foreach (Transform ch in t) foreach (var x in AllMeshes(ch)) yield return x;
    }
}
