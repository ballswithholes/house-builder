// ==========================================================================
// 三国志II 霸王的大陆 · 单挑格斗 · 程序化关键帧动画与武将（网页版 js/duel-game.js「视图：姿势 / 武将（动画）」的移植）
//
// 姿势向量（身体局部坐标，单位米 / 弧度 / 兵器角为度）：
//   hx hy 骨盆位置 · lean 前倾 · twist 扭身 · head 低头 · gx gy gz 右手握点 · wa 兵器角（0 = 向前水平，90 = 竖直向上）· wy 兵器偏航
//   ox oy oz 左手（双持时为第二把兵器的握点）· wa2 第二把兵器角 · fLx fLy 前脚（左）· fRx fRy 后脚（右）· fall 倒地（0..1）· cape 披风扬起
// 全部计算在 three 坐标（右手系）里进行，写入 Transform 时经 DuelXf 换算成 Unity 坐标。
// ==========================================================================
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sanguo
{
    public sealed class DuelKey { public float t; public float[] p; public DuelKey(float t, float[] p) { this.t = t; this.p = p; } }

    public sealed class DuelClips
    {
        public float[] idle, charge, guard, jump, power, dash, land, guardstun, hitA, hitB, guardbreak, knock, down;
        public DuelKey[] light1, light2, light3, heavy, air, victory, defeat, getup, special, intro, bowSpecial;
        public DuelKey[] Of(string state)
        {
            switch (state) { case "light1": return light1; case "light2": return light2; case "light3": return light3; case "heavy": return heavy; case "air": return air; }
            return null;
        }
    }

    public static class DuelPose
    {
        public const int hx = 0, hy = 1, lean = 2, twist = 3, head = 4, gx = 5, gy = 6, gz = 7, wa = 8, wy = 9, ox = 10, oy = 11, oz = 12, wa2 = 13, fLx = 14, fLy = 15, fRx = 16, fRy = 17, fall = 18, cape = 19, N = 20;
        static readonly string[] Names = { "hx", "hy", "lean", "twist", "head", "gx", "gy", "gz", "wa", "wy", "ox", "oy", "oz", "wa2", "fLx", "fLy", "fRx", "fRy", "fall", "cape" };
        static int Idx(string k) { for (int i = 0; i < Names.Length; i++) if (Names[i] == k) return i; throw new System.ArgumentException("pose key " + k); }

        // pose(base, "k:v k:v …")：复制 base 后按次序覆盖（后写的键优先，同 Object.assign）
        public static float[] P(float[] bs, string spec)
        {
            var a = new float[N];
            if (bs != null) System.Array.Copy(bs, a, N);
            if (!string.IsNullOrEmpty(spec))
                foreach (var kv in spec.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
                {
                    int c = kv.IndexOf(':');
                    a[Idx(kv.Substring(0, c))] = float.Parse(kv.Substring(c + 1), CultureInfo.InvariantCulture);
                }
            return a;
        }

        public static readonly float[] IdlePole = P(null, "hx:0 hy:0.9 lean:0.1 twist:-0.5 head:-0.06 gx:0.02 gy:1.0 gz:0.08 wa:30 ox:0.4 oy:1.15 oz:0 wa2:0 fLx:0.3 fLy:0.08 fRx:-0.3 fRy:0.08");
        public static readonly float[] IdleOne = P(null, "hx:0 hy:0.9 lean:0.08 twist:-0.22 head:-0.05 gx:0.3 gy:1.12 gz:0.2 wa:55 ox:0.16 oy:1.04 oz:-0.16 wa2:30 fLx:0.28 fLy:0.08 fRx:-0.3 fRy:0.08");

        static DuelKey K(float t, float[] p) { return new DuelKey(t, p); }

        // 招式片段：键为 [相位, 姿势]；相位 0..1 = 起手，1..2 = 判定，2..3 = 收招（与 DuelMove 的时序对齐）
        static DuelClips Make(string style)
        {
            var I = style == "pole" ? IdlePole : IdleOne;
            bool dual = style == "dual";
            var c = new DuelClips();
            if (style == "pole")
            {
                System.Func<string, float[]> Pp = o => P(I, o);
                c.light1 = new[] { K(0, I), K(0.7f, Pp("gx:-0.14 gy:1.06 wa:22 twist:-0.78 lean:0.02 hx:-0.04")), K(1, Pp("gx:0.5 gy:1.16 wa:6 twist:-0.22 lean:0.3 hx:0.1 fLx:0.46")), K(2, Pp("gx:0.55 gy:1.14 wa:4 twist:-0.18 lean:0.32 hx:0.12 fLx:0.47")), K(3, I) };
                c.light2 = new[] { K(0, Pp("gx:0.4 gy:1.14 wa:6 twist:-0.25 lean:0.28 hx:0.08")), K(0.7f, Pp("gx:0.12 gy:0.82 wa:-28 twist:-0.62 lean:0.25 hy:0.84")), K(1, Pp("gx:0.36 gy:1.3 wa:62 twist:-0.15 lean:0.06")), K(2, Pp("gx:0.26 gy:1.42 wa:92 twist:0 lean:-0.02")), K(3, I) };
                c.light3 = new[] { K(0, Pp("gx:0.25 gy:1.4 wa:90 twist:0 lean:0")), K(0.75f, Pp("gx:-0.05 gy:1.62 wa:128 lean:-0.16 twist:-0.36 hy:0.95 fLx:0.32 head:-0.16")), K(1, Pp("gx:0.48 gy:0.98 wa:-12 lean:0.4 hy:0.78 fLx:0.58 fRx:-0.42 twist:-0.3 head:0.1 hx:0.08")), K(2, Pp("gx:0.5 gy:0.86 wa:-24 lean:0.42 hy:0.75 fLx:0.6 fRx:-0.42 hx:0.1")), K(3, I) };
                c.charge = Pp("gx:-0.32 gy:1.2 wa:168 twist:-1.05 lean:0.06 hy:0.82 fLx:0.44 fRx:-0.42 head:0.06");
                c.heavy = new[] { K(0, c.charge), K(1, Pp("gx:0.56 gy:1.18 wa:-2 twist:0.45 lean:0.36 hy:0.82 fLx:0.56 fRx:-0.45 hx:0.08")), K(2, Pp("gx:0.42 gy:1.08 wa:-26 twist:0.65 lean:0.38 hy:0.8 fLx:0.56 fRx:-0.45 hx:0.1")), K(3, I) };
                c.guard = Pp("gx:0.22 gy:1.16 gz:0.08 wa:84 twist:-0.25 lean:-0.02 hy:0.86 head:0.06 fLx:0.32 fRx:-0.36");
                c.jump = Pp("hy:0.96 lean:0.05 fLx:0.22 fLy:0.42 fRx:-0.12 fRy:0.3 gx:0.05 gy:1.22 wa:60");
                c.air = new[] { K(0, c.jump), K(0.6f, P(c.jump, "gx:-0.02 gy:1.48 wa:115 lean:-0.1")), K(1, P(c.jump, "gx:0.45 gy:0.98 wa:-35 lean:0.35")), K(2, P(c.jump, "gx:0.42 gy:0.9 wa:-42 lean:0.32")), K(3, c.jump) };
                c.power = Pp("gx:-0.18 gy:1.62 wa:150 twist:-0.9 lean:-0.12 hy:0.86 head:-0.18 fLx:0.42 fRx:-0.42 cape:0.6");
                c.victory = new[] { K(0, I), K(0.35f, Pp("gx:0.2 gy:1.42 wa:200 twist:-0.25 lean:-0.02")), K(0.7f, Pp("gx:0.44 gy:1.36 wa:93 lean:-0.08 head:-0.12 twist:0.04 hy:0.93 cape:0.2")), K(1.6f, Pp("gx:0.46 gy:1.38 wa:94 lean:-0.1 head:-0.1 twist:0.06 hy:0.93 cape:0.2")) };
                c.defeat = new[] { K(0, I), K(0.5f, Pp("hy:0.55 lean:0.4 head:0.36 fLx:0.36 fLy:0.08 fRx:-0.34 fRy:0.05 gx:0.32 gy:0.72 wa:-58 twist:-0.2")) };
                c.dash = Pp("lean:0.42 hy:0.84 fLx:0.5 fLy:0.12 fRx:-0.52 fRy:0.1 gx:-0.06 gy:0.98 wa:12 twist:-0.62 cape:0.8");
            }
            else
            {
                // 单手 / 双持
                string dx = dual ? "ox:0.28 oy:0.98 wa2:22 " : "";
                System.Func<string, float[]> D = o => P(I, dx + o);
                c.light1 = new[] { K(0, I), K(0.7f, D("gx:-0.02 gy:1.42 wa:150 twist:-0.55 lean:0.0 hx:-0.04")), K(1, D("gx:0.46 gy:1.08 wa:2 twist:0.25 lean:0.25 hx:0.08 fLx:0.42")), K(2, D("gx:0.36 gy:0.94 wa:-45 twist:0.42 lean:0.28 hx:0.08 fLx:0.42")), K(3, I) };
                if (dual)
                {
                    // 双持：第二击由左手出
                    c.light2 = new[] { K(0, D("gx:0.36 gy:0.94 wa:-45 twist:0.42 lean:0.28")), K(0.7f, D("gx:0.25 gy:0.95 wa:-30 ox:-0.06 oy:1.4 wa2:150 twist:0.45 lean:0.05")), K(1, D("gx:0.2 gy:1.0 wa:-10 ox:0.5 oy:1.08 wa2:0 twist:-0.45 lean:0.27 fLx:0.44")), K(2, D("gx:0.2 gy:1.0 wa:10 ox:0.4 oy:0.95 wa2:-42 twist:-0.55 lean:0.28 fLx:0.44")), K(3, I) };
                    c.light3 = new[] { K(0, I), K(0.7f, D("gx:0.0 gy:1.2 wa:8 ox:-0.02 oy:1.1 wa2:6 hx:-0.1 lean:-0.02 twist:-0.3")), K(1, D("gx:0.7 gy:1.24 wa:4 ox:0.66 oy:1.1 wa2:0 hx:0.16 lean:0.38 twist:0.0 fLx:0.62 fRx:-0.4 hy:0.84")), K(2, D("gx:0.74 gy:1.22 wa:2 ox:0.68 oy:1.08 wa2:-2 hx:0.18 lean:0.4 fLx:0.62 fRx:-0.4 hy:0.84")), K(3, I) };
                }
                else
                {
                    c.light2 = new[] { K(0, D("gx:0.36 gy:0.94 wa:-45 twist:0.42 lean:0.28")), K(0.7f, D("gx:0.32 gy:0.84 wa:-55 twist:0.42 lean:0.22")), K(1, D("gx:0.42 gy:1.42 wa:78 twist:-0.3 lean:0.02")), K(2, D("gx:0.22 gy:1.54 wa:118 twist:-0.42")), K(3, I) };
                    c.light3 = new[] { K(0, D("gx:0.22 gy:1.5 wa:110 twist:-0.4")), K(0.7f, D("gx:0.0 gy:1.2 wa:8 hx:-0.1 lean:-0.02 twist:-0.6")), K(1, D("gx:0.72 gy:1.22 wa:3 hx:0.16 lean:0.38 twist:0.1 fLx:0.62 fRx:-0.4 hy:0.84")), K(2, D("gx:0.75 gy:1.2 wa:1 hx:0.18 lean:0.4 twist:0.12 fLx:0.62 fRx:-0.4 hy:0.84")), K(3, I) };
                }
                c.charge = D("gx:-0.2 gy:1.68 wa:135 ox:" + (dual ? "-0.25" : "-0.12") + " oy:" + (dual ? "1.5" : "1.62") + " oz:-0.1 wa2:150 lean:-0.1 hy:0.85 twist:-0.42 fLx:0.4 fRx:-0.4");
                c.heavy = new[] { K(0, c.charge), K(1, D("gx:0.48 gy:0.98 wa:-32 ox:" + (dual ? "0.46" : "0.42") + " oy:" + (dual ? "0.9" : "1.0") + " oz:-0.06 wa2:-40 lean:0.42 hy:0.78 fLx:0.56 fRx:-0.42 twist:0 hx:0.08")), K(2, D("gx:0.44 gy:0.86 wa:-52 ox:0.4 oy:0.88 oz:-0.06 wa2:-55 lean:0.44 hy:0.76 fLx:0.56 fRx:-0.42 hx:0.1")), K(3, I) };
                c.guard = D(dual ? "gx:0.3 gy:1.26 wa:116 ox:0.3 oy:1.26 wa2:64 twist:-0.1 hy:0.86 lean:-0.02" : "gx:0.3 gy:1.3 wa:102 ox:0.3 oy:1.42 oz:-0.05 twist:-0.12 hy:0.86 lean:-0.02");
                c.jump = D("hy:0.96 lean:0.05 fLx:0.22 fLy:0.42 fRx:-0.12 fRy:0.3 gx:0.15 gy:1.4 wa:90");
                c.air = new[] { K(0, c.jump), K(0.6f, P(c.jump, "gx:-0.06 gy:1.52 wa:145 lean:-0.1")), K(1, P(c.jump, "gx:0.42 gy:0.95 wa:-40 lean:0.35")), K(2, P(c.jump, "gx:0.38 gy:0.88 wa:-50 lean:0.32")), K(3, c.jump) };
                c.power = D("gx:-0.1 gy:1.7 wa:120 ox:0.25 oy:1.25 wa2:40 twist:-0.6 lean:-0.12 hy:0.86 head:-0.18 fLx:0.42 fRx:-0.42 cape:0.6");
                c.victory = new[] { K(0, I), K(0.35f, D("gx:0.25 gy:1.4 wa:160 twist:-0.25")), K(0.7f, D("gx:0.25 gy:1.86 wa:86 ox:0.06 oy:1.0 lean:-0.12 head:-0.2 hy:0.93")), K(1.6f, D("gx:0.26 gy:1.88 wa:88 ox:0.06 oy:1.0 lean:-0.13 head:-0.18 hy:0.93")) };
                c.defeat = new[] { K(0, I), K(0.5f, D("hy:0.55 lean:0.4 head:0.36 fLx:0.36 fLy:0.08 fRx:-0.34 fRy:0.05 gx:0.36 gy:0.62 wa:-80 ox:0.2 oy:0.8 twist:-0.1")) };
                c.dash = D("lean:0.42 hy:0.84 fLx:0.5 fLy:0.12 fRx:-0.52 fRy:0.1 gx:0.1 gy:1.0 wa:170 twist:-0.4 cape:0.8");
            }
            c.idle = I;
            c.land = P(I, "hy:0.76 lean:0.25");
            c.guardstun = P(c.guard, "hx:-0.07 lean:-0.12 head:-0.1");
            c.hitA = P(I, "hx:-0.08 lean:-0.32 head:-0.32 twist:-0.25 hy:0.88 cape:0.3");
            c.hitA[gx] = I[gx] - 0.1f; c.hitA[gy] = I[gy] + 0.05f; c.hitA[wa] = I[wa] + 20;
            c.hitB = P(I, "lean:0.38 head:0.32 hy:0.84");
            c.hitB[gx] = I[gx] + 0.05f; c.hitB[gy] = I[gy] - 0.12f; c.hitB[wa] = I[wa] - 20;
            c.guardbreak = P(I, "lean:-0.38 head:-0.36 gx:-0.1 gy:1.38 wa:120 ox:0.0 oy:1.3 hx:-0.12 fLx:0.14 hy:0.88");
            c.knock = P(I, "lean:-0.45 head:-0.4 gx:-0.25 gy:1.42 wa:150 ox:0.1 oy:1.45 fLx:0.38 fLy:0.4 fRx:0.05 fRy:0.3 cape:0.8");
            c.down = P(I, "fall:1 lean:-0.1 head:-0.15 gx:0.05 gy:1.42 wa:165 ox:0.25 oy:1.4 fLx:0.32 fLy:0.12 fRx:0.08 fRy:0.16 hy:0.86 twist:-0.3");
            c.getup = new[] { K(0, c.down), K(0.18f, P(I, "fall:0.45 hy:0.62 lean:0.55 head:0.2 fLx:0.35 fRx:-0.25")), K(0.42f, I) };
            // 绝技（秒）：蓄势定格 → 突进 → 四连斩
            float[] L1 = c.light1[2].p, L2 = c.light2[2].p, L3 = c.light3[2].p, H = c.heavy[1].p;
            float SF = (float)DuelSim.SPECIAL_FREEZE;
            float t0 = SF + 0.1f; var h = DuelSim.Special.hits;
            float h0 = (float)h[0], h1 = (float)h[1], h2 = (float)h[2], h3 = (float)h[3];
            float spEnd = t0 + (float)DuelSim.Special.active + (float)DuelSim.Special.recovery;
            c.special = new[] { K(0, I), K(0.25f, c.power), K(SF, c.power), K(SF + 0.06f, c.dash), K(t0 + h0 - 0.03f, c.light1[1].p), K(t0 + h0, L1),
                K(t0 + h1 - 0.06f, c.light2[1].p), K(t0 + h1, L2), K(t0 + h2 - 0.06f, c.light3[1].p), K(t0 + h2, L3), K(t0 + h3 - 0.1f, c.charge), K(t0 + h3, H),
                K(t0 + h3 + 0.2f, c.heavy[2].p), K(spEnd, I) };
            var in1 = P(I, null); in1[wa] = I[wa] + 180; in1[gy] = I[gy] + 0.3f; in1[twist] = I[twist] + 0.3f;
            var in2 = P(I, null); in2[wa] = I[wa] + 360; in2[gy] = I[gy] + 0.1f;
            c.intro = new[] { K(0, I), K(0.25f, in1), K(0.5f, in2), K(0.85f, c.guard), K(1.3f, I) };
            // 弓将绝技「连珠箭」：插刀于地 → 侧身开弓 → 三连射 → 满弓重箭 → 收弓拔刀（g* = 拉弦的右手，o* = 持弓的左手）
            const string B0 = "twist:-1.05 lean:0.03 hy:0.86 head:0.02 fLx:0.44 fLy:0.08 fRx:-0.42 fRy:0.08 oz:-0.1 gz:0.12 cape:0.3 ";
            var DRAW = P(I, B0 + "gx:-0.02 gy:1.47 ox:0.6 oy:1.46");
            var FULL = P(I, B0 + "gx:-0.1 gy:1.5 ox:0.62 oy:1.5 lean:-0.06 hy:0.84 head:-0.06 cape:0.6");
            var REL = P(I, B0 + "gx:-0.16 gy:1.52 gz:0.2 ox:0.62 oy:1.47");
            var NOCK = P(I, B0 + "gx:0.28 gy:1.36 ox:0.58 oy:1.44");
            var RAISE = P(I, B0 + "gx:0.2 gy:1.62 ox:0.44 oy:1.72 twist:-0.8 lean:-0.08 head:-0.14 cape:0.6");
            var bk = new List<DuelKey> { K(0, I), K(0.22f, RAISE), K(0.45f, NOCK), K(SF, DRAW) };
            for (int i = 0; i < h.Length; i++)
            {
                float th = t0 + (float)h[i]; bool last = i == h.Length - 1;
                if (i > 0) { bk.Add(K(t0 + (float)h[i - 1] + 0.05f, NOCK)); bk.Add(K(th - (last ? 0.04f : 0.02f), last ? FULL : DRAW)); }
                else bk.Add(K(th - 0.02f, DRAW));
                bk.Add(K(th + 0.02f, REL));
            }
            bk.Add(K(t0 + (float)h[h.Length - 1] + 0.3f, REL)); bk.Add(K(spEnd, I));
            c.bowSpecial = bk.ToArray();
            return c;
        }
        static readonly Dictionary<string, DuelClips> cache = new Dictionary<string, DuelClips>();
        public static DuelClips For(string style) { DuelClips c; if (!cache.TryGetValue(style, out c)) cache[style] = c = Make(style); return c; }

        // 片段采样：相邻关键帧间用平滑插值
        public static void Sample(DuelKey[] keys, float t, float[] o)
        {
            if (t <= keys[0].t) { System.Array.Copy(keys[0].p, o, N); return; }
            for (int i = 1; i < keys.Length; i++)
            {
                if (t <= keys[i].t)
                {
                    var a = keys[i - 1]; var b = keys[i];
                    float u = (t - a.t) / Mathf.Max(1e-6f, b.t - a.t);
                    u = u * u * (3 - 2 * u);
                    for (int j = 0; j < N; j++) o[j] = a.p[j] + (b.p[j] - a.p[j]) * u;
                    return;
                }
            }
            System.Array.Copy(keys[keys.Length - 1].p, o, N);
        }
        // 出招时间 → 相位（0..3）
        public static float PhaseOf(DuelMove mv, double t)
        {
            if (t < mv.startup) return (float)(t / mv.startup);
            if (t < mv.startup + mv.active) return (float)(1 + (t - mv.startup) / mv.active);
            return (float)System.Math.Min(3, 2 + (t - mv.startup - mv.active) / mv.recovery);
        }
    }

    // 一名武将的模型、姿势混合与 IK
    public sealed class DuelWarrior
    {
        public const float TURN = 0.42f;   // 3/4 侧身角度（向镜头转）
        static readonly Dictionary<string, float> BLEND = new Dictionary<string, float>
        {
            { "idle", 0.14f }, { "walk", 0.12f }, { "guard", 0.06f }, { "guardstun", 0.03f }, { "hitstun", 0.035f }, { "knockdown", 0.07f }, { "down", 0.1f }, { "getup", 0.08f },
            { "victory", 0.2f }, { "defeat", 0.2f }, { "charge", 0.1f }, { "dash", 0.06f }, { "jump", 0.08f }, { "land", 0.04f },
        };

        public readonly DuelFighter f;
        public readonly DuelLook look;
        public readonly string style;
        readonly DuelClips clips;
        readonly float[] idlePose;
        public readonly Material mat;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Object> owned = new List<Object>();
        readonly float bk;
        public readonly GameObject root;
        readonly Transform body, torso, head, cape1, cape2;
        public readonly Transform wpn, wpn2;
        readonly Transform shield, handBow, nocked;
        readonly LineRenderer bowString;
        readonly Dictionary<string, Transform> m = new Dictionary<string, Transform>();
        public readonly float tipLen, bladeLen;
        float bowK;
        Vector3 handL, handR;
        readonly float[] cur, from, tgt;
        string key = ""; float blend = 1, blendDur = 0.1f;
        float walkPhase, time, capeA = -0.1f, capeV;
        public float flash; Color flashColor = Color.white;
        public float intro = -1;      // ≥0：开场亮相动画计时
        public readonly Vector3[] tipW = new Vector3[2], baseW = new Vector3[2];
        public Vector3 chestW;
        Matrix4x4 torsoM;

        static Color C(string h) { return DuelXf.C(h); }

        public DuelWarrior(DuelFighter f, Color team, Transform parent, int layer)
        {
            this.f = f; look = f.look; style = f.wpn.style;
            clips = DuelPose.For(style);
            idlePose = style == "pole" ? DuelPose.IdlePole : DuelPose.IdleOne;
            mat = Art.NewLowPoly(); owned.Add(mat);
            bk = (float)look.bulk;
            root = new GameObject("Duelist_" + (f.gen != null ? f.gen.name : "?"));
            root.layer = layer;
            root.transform.SetParent(parent, false);
            body = NewGroup("Body", root.transform, layer);
            body.localScale = Vector3.one * (float)look.height;
            var parts = DuelModels.BuildParts(look, team, f.war);
            foreach (var n in new[] { "pelvis", "upperR", "foreR", "upperL", "foreL", "thighR", "shinR", "thighL", "shinL", "footR", "footL" }) Mk(parts, n, body, layer);
            torso = NewGroup("Torso", body, layer); Mk(parts, "torso", torso, layer);
            head = NewGroup("Head", torso, layer); DuelXf.Pos(head, 0.01f, 0.53f, 0); Mk(parts, "head", head, layer);
            if (parts.ContainsKey("cape1"))
            {
                cape1 = NewGroup("Cape1", torso, layer); DuelXf.Pos(cape1, -0.14f * bk, 0.47f, 0); Mk(parts, "cape1", cape1, layer);
                cape2 = NewGroup("Cape2", cape1, layer); DuelXf.Pos(cape2, -0.015f, -0.5f, 0); Mk(parts, "cape2", cape2, layer);
            }
            var w = DuelModels.BuildWeapon(look.weapon, look);
            var wm = w.pb.ToMesh("DuelWeapon"); meshes.Add(wm);
            wpn = NewGroup("Weapon", body, layer); MeshObj("WeaponMesh", wm, wpn, layer, true);
            if (style == "dual") { wpn2 = NewGroup("Weapon2", body, layer); MeshObj("WeaponMesh", wm, wpn2, layer, true); }
            if (look.shield != null && style == "one")
            {
                var sm = DuelModels.BuildShield(look.shield, team).ToMesh("DuelShield"); meshes.Add(sm);
                shield = MeshObj("Shield", sm, body, layer, true).transform;
            }
            tipLen = w.tip; bladeLen = w.len;
            // 弓将：背弓 + 手持弓（弦为折线，满弓时随右手拉开）+ 搭在弦上的箭
            if (parts.ContainsKey("backbow")) Mk(parts, "backbow", torso, layer);
            if (look.bow)
            {
                var bm = DuelModels.BuildBow().ToMesh("DuelBow"); meshes.Add(bm);
                handBow = MeshObj("HandBow", bm, body, layer, true).transform; handBow.gameObject.SetActive(false);
                var sgo = new GameObject("BowString"); sgo.layer = layer; sgo.transform.SetParent(body, false);
                bowString = sgo.AddComponent<LineRenderer>();
                bowString.useWorldSpace = false; bowString.positionCount = 3; bowString.widthMultiplier = 0.012f;
                bowString.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; bowString.receiveShadows = false;
                var lm = Art.NewUnlit(C("#f0e8d0")); owned.Add(lm);
                bowString.sharedMaterial = lm;
                sgo.SetActive(false);
                var am = DuelModels.BuildArrow(false).ToMesh("DuelArrow"); meshes.Add(am);
                nocked = MeshObj("Nocked", am, body, layer, false).transform; nocked.gameObject.SetActive(false);
            }
            cur = DuelPose.P(idlePose, null); from = DuelPose.P(idlePose, null); tgt = DuelPose.P(idlePose, null);
            time = f.idx * 1.7f;
            Apply(cur);
        }

        static Transform NewGroup(string name, Transform parent, int layer)
        {
            var go = new GameObject(name); go.layer = layer; go.transform.SetParent(parent, false); return go.transform;
        }
        GameObject MeshObj(string name, Mesh mesh, Transform parent, int layer, bool cast)
        {
            var go = Art.MakeObject(name, mesh, mat, parent, cast);
            go.layer = layer;
            go.GetComponent<MeshRenderer>().receiveShadows = false;
            return go;
        }
        void Mk(Dictionary<string, DuelPB> parts, string name, Transform parent, int layer)
        {
            DuelPB pb;
            if (!parts.TryGetValue(name, out pb) || pb.Empty) return;
            var mesh = pb.ToMesh("Duel_" + name); meshes.Add(mesh);
            m[name] = MeshObj(name, mesh, parent, layer, true).transform;
        }
        Transform Part(string n) { Transform t; return m.TryGetValue(n, out t) ? t : null; }

        // 根据逻辑状态选片段并采样（目标姿势写入 tgt，并在片段切换时开始混合）
        void SampleState()
        {
            var o = tgt; var c = clips;
            string st = f.state;
            string k = st + ":" + f.seq;
            float dur; if (!BLEND.TryGetValue(st, out dur)) dur = 0.05f;
            var mv = DuelSim.Mv(st);
            if (intro >= 0) { k = "intro"; DuelPose.Sample(c.intro, intro, o); dur = 0.1f; }
            else if (mv != null && st != "special") DuelPose.Sample(c.Of(st), DuelPose.PhaseOf(mv, f.t), o);
            else switch (st)
                {
                    case "special": DuelPose.Sample(f.bow ? c.bowSpecial : c.special, (float)f.t, o); break;
                    case "charge": Copy(c.charge, o); break;
                    case "guard": Copy(c.guard, o); break;
                    case "guardstun": Copy(c.guardstun, o); break;
                    case "guardbreak": Copy(c.guardbreak, o); break;
                    case "hitstun": Copy(f.seq % 2 != 0 ? c.hitA : c.hitB, o); break;
                    case "knockdown": Copy(c.knock, o); o[DuelPose.fall] = Mathf.Min(0.9f, (float)f.t * 2.4f); break;
                    case "down": Copy(c.down, o); o[DuelPose.fall] = 1; break;
                    case "getup": DuelPose.Sample(c.getup, (float)f.t, o); break;
                    case "dash": Copy(c.dash, o); break;
                    case "jump":
                        {
                            Copy(c.jump, o);
                            float kk = Mathf.Clamp01((2 - (float)f.vy) / 8);            // 下落时腿伸直准备落地
                            o[DuelPose.fLy] -= kk * 0.26f; o[DuelPose.fRy] -= kk * 0.14f; o[DuelPose.fLx] += kk * 0.05f;
                            break;
                        }
                    case "land": Copy(c.land, o); break;
                    case "victory": DuelPose.Sample(c.victory, (float)f.t, o); break;
                    case "defeat": if (f.ko) { Copy(c.down, o); o[DuelPose.fall] = 1; } else DuelPose.Sample(c.defeat, (float)f.t, o); break;
                    case "walk": Copy(idlePose, o); k = "walk"; break;
                    default: Copy(idlePose, o); k = "idle"; break;
                }
            if (k != key)
            {
                Copy(cur, from);
                blend = 0;
                blendDur = dur;
                key = k;
            }
        }
        static void Copy(float[] a, float[] b) { System.Array.Copy(a, b, DuelPose.N); }

        public void Update(float dt, bool frozen)
        {
            var P = cur;
            time += dt;
            if (intro >= 0) intro += dt;
            SampleState();
            if (!frozen) blend += dt;
            float w = blendDur > 0 ? Mathf.Clamp01(blend / blendDur) : 1;
            float k = w * w * (3 - 2 * w);
            for (int i = 0; i < DuelPose.N; i++) P[i] = from[i] + (tgt[i] - from[i]) * k;
            string st = f.state;
            // 程序化叠加：呼吸、步伐、蓄力颤动、受击抖动
            if (st == "idle" || st == "guard" || st == "walk" || st == "charge")
            {
                float b = Mathf.Sin(time * 2.3f);
                P[DuelPose.hy] += b * 0.012f; P[DuelPose.lean] += Mathf.Sin(time * 2.3f + 0.6f) * 0.015f;
                P[DuelPose.gy] += b * 0.01f; P[DuelPose.wa] += Mathf.Sin(time * 1.25f) * 2.2f;
            }
            if (st == "walk")
            {
                float v = (float)f.vx * f.face;
                walkPhase += v * dt * (Mathf.PI * 2 / 1.0f);
                float s = Mathf.Sin(walkPhase), c = Mathf.Cos(walkPhase);
                P[DuelPose.fLx] += s * 0.2f; P[DuelPose.fLy] += Mathf.Max(0, c) * 0.11f;
                P[DuelPose.fRx] -= s * 0.2f; P[DuelPose.fRy] += Mathf.Max(0, -c) * 0.11f;
                P[DuelPose.hy] -= Mathf.Abs(s) * 0.025f; P[DuelPose.lean] += v * 0.03f; P[DuelPose.cape] += Mathf.Abs(v) * 0.12f;
            }
            if (st == "charge")
            {
                float q = (float)f.charge, n = time * 61;
                P[DuelPose.gx] += Mathf.Sin(n) * 0.012f * q; P[DuelPose.gy] += Mathf.Cos(n * 1.3f) * 0.012f * q; P[DuelPose.hy] -= q * 0.04f;
            }
            if (st == "hitstun" && f.t < 0.12) P[DuelPose.hx] += Mathf.Sin((float)f.t * 120) * 0.025f * (1 - (float)f.t / 0.12f);
            if (st == "dash") P[DuelPose.cape] += 0.4f;
            // 弓将绝技期间：刀插在地上，弓在手中
            var sp = DuelSim.Special;
            bowK = f.bow && st == "special" && f.t > 0.14 && f.t < sp.startup + sp.active + sp.recovery - 0.22 ? 1 : 0;
            Apply(P);
            if (handBow != null) UpdateBow();
            // 位置与朝向
            float fall = P[DuelPose.fall];
            DuelXf.Pos(root.transform, (float)f.x, (float)f.y + fall * 0.12f, 0);
            root.transform.localScale = new Vector3(f.face, 1, 1);
            DuelXf.RotYZX(body, 0, -TURN * (1 - fall * 0.7f), fall * 1.5f);
            // 披风：弹簧追随
            float tgtA = -0.1f - Mathf.Abs((float)f.vx) * 0.06f - P[DuelPose.cape] * 0.55f - (f.y > 0 ? Mathf.Min(0.5f, Mathf.Abs((float)f.vy) * 0.05f) : 0);
            capeV += ((tgtA - capeA) * 60 - capeV * 9) * dt;
            capeA += capeV * dt;
            if (capeA < -1.15f) { capeA = -1.15f; if (capeV < 0) capeV = 0; }   // 披风最多扬到身后近水平
            else if (capeA > 0.35f) { capeA = 0.35f; if (capeV > 0) capeV = 0; }
            if (cape1 != null)
            {
                DuelXf.RotXYZ(cape1, 0, 0, P[DuelPose.lean] + capeA + Mathf.Sin(time * 5.3f) * 0.03f);
                DuelXf.RotXYZ(cape2, 0, 0, capeA * 0.6f + Mathf.Sin(time * 6.1f + 1) * 0.06f * (1 + Mathf.Abs((float)f.vx) * 0.3f));
            }
            // 受击闪光 / 蓄力发光
            if (flash > 0) flash = Mathf.Max(0, flash - dt * 7);
            mat.SetFloat("_Flash", flash);
            mat.SetColor("_FlashColor", flashColor);
            float em = 0;
            if (st == "charge") em = (float)f.charge * 0.25f + (f.charge >= 1 ? 0.12f * (0.5f + 0.5f * Mathf.Sin(time * 30)) : 0);
            else if (st == "special" && f.t < DuelSim.SPECIAL_FREEZE + 0.1) em = 0.3f + 0.1f * Mathf.Sin(time * 25);
            else if (f.rage >= 100) em = 0.06f + 0.05f * Mathf.Sin(time * 8);
            mat.SetFloat("_Emission", em);
            // 世界坐标（刀光拖尾与特效用）
            tipW[0] = wpn.TransformPoint(new Vector3(0, tipLen, 0));
            baseW[0] = wpn.TransformPoint(new Vector3(0, tipLen - bladeLen, 0));
            if (wpn2 != null)
            {
                tipW[1] = wpn2.TransformPoint(new Vector3(0, tipLen, 0));
                baseW[1] = wpn2.TransformPoint(new Vector3(0, tipLen - bladeLen, 0));
            }
            chestW = torso.TransformPoint(new Vector3(0.05f, 0.3f, 0));
        }

        public void FlashHit(Color col, float k) { flash = k > 0 ? k : 0.85f; flashColor = col; }

        // 手持弓、弓弦与搭箭（身体局部坐标）
        void UpdateBow()
        {
            bool on = bowK > 0;
            handBow.gameObject.SetActive(on); bowString.gameObject.SetActive(on);
            var back = Part("backbow"); if (back != null) back.gameObject.SetActive(!on);
            if (!on) { nocked.gameObject.SetActive(false); return; }
            Vector3 L = handL, R = handR;
            DuelXf.Pos(handBow, L);
            handBow.localRotation = Quaternion.identity;
            float tx = L.x + DuelModels.BowTipX, ty0 = L.y + DuelModels.BowTipY, ty1 = L.y - DuelModels.BowTipY;
            // 右手在弓后方、与握把同高附近时视为拉弦
            bool drawn = R.x < L.x - 0.12f && Mathf.Abs(R.y - L.y) < 0.28f;
            float nx = drawn ? R.x : tx, ny = drawn ? R.y : L.y, nz = drawn ? R.z : L.z;
            bowString.SetPosition(0, DuelXf.P(tx, ty0, L.z)); bowString.SetPosition(1, DuelXf.P(nx, ny, nz)); bowString.SetPosition(2, DuelXf.P(tx, ty1, L.z));
            nocked.gameObject.SetActive(drawn);
            if (drawn)
            {
                DuelXf.Pos(nocked, nx, ny, nz);
                float dx = L.x + 0.1f - nx, dy = L.y - ny, dz = L.z - nz;
                DuelXf.RotYZX(nocked, 0, Mathf.Atan2(-dz, dx), Mathf.Atan2(dy, Mathf.Sqrt(dx * dx + dz * dz)));
            }
        }

        // 两段 IK：S（根）→ T（末端），返回中间关节 E（膝 / 肘）与可达的末端 outT；hint 为弯曲方向
        static void SolveIK(Vector3 S, Vector3 T, float l1, float l2, Vector3 hint, out Vector3 outE, out Vector3 outT)
        {
            var d = T - S;
            float len = d.magnitude;
            float maxL = (l1 + l2) * 0.999f, minL = Mathf.Abs(l1 - l2) + 0.02f;
            if (len < 1e-5f) { d = new Vector3(0, -1, 0); len = 1e-5f; }
            float L = Mathf.Clamp(len, minL, maxL);
            d *= 1 / len;
            outT = S + d * L;
            float x = (l1 * l1 - l2 * l2 + L * L) / (2 * L);
            float h = Mathf.Sqrt(Mathf.Max(0, l1 * l1 - x * x));
            var p = hint - d * Vector3.Dot(hint, d);
            if (p.sqrMagnitude < 1e-8f) p = new Vector3(0, 0, 1);
            p.Normalize();
            outE = S + d * x + p * h;
        }
        // 让骨骼的局部 −y 指向 from → to，局部 +x 尽量朝前
        static void Orient(Transform obj, Vector3 from, Vector3 to)
        {
            var y = from - to;
            if (y.sqrMagnitude < 1e-10f) y = new Vector3(0, 1, 0);
            y.Normalize();
            var x = new Vector3(y.y, -y.x, 0);
            if (x.sqrMagnitude < 1e-6f) x = new Vector3(1, 0, 0);
            x = (x - y * Vector3.Dot(x, y)).normalized;
            var z = Vector3.Cross(x, y);
            DuelXf.Rot(obj, Quaternion.LookRotation(z, y));   // 列向量 (x, y, z) 的旋转（three 坐标）
            DuelXf.Pos(obj, from);
        }

        // 把姿势向量应用到各部件（身体局部坐标 + IK）
        void Apply(float[] P)
        {
            float hx = P[DuelPose.hx], hy = P[DuelPose.hy], tw = P[DuelPose.twist];
            var pel = Part("pelvis");
            if (pel != null) { DuelXf.Pos(pel, hx, hy, 0); DuelXf.RotXYZ(pel, 0, tw * 0.35f, 0); }
            var torsoPos = new Vector3(hx, hy + 0.04f, 0);
            var torsoRot = DuelXf.EulerYZX(0, tw, -P[DuelPose.lean]);
            DuelXf.Pos(torso, torsoPos); DuelXf.Rot(torso, torsoRot);
            DuelXf.RotXYZ(head, 0, -tw * 0.35f, -P[DuelPose.head] + P[DuelPose.lean] * 0.35f);
            torsoM = Matrix4x4.TRS(torsoPos, torsoRot, Vector3.one);
            // 兵器
            float wa = P[DuelPose.wa] * Mathf.Deg2Rad, wy = P[DuelPose.wy] * Mathf.Deg2Rad;
            var G = new Vector3(P[DuelPose.gx], P[DuelPose.gy], P[DuelPose.gz]);
            if (bowK > 0)
            {
                // 插刀于地：长兵器刃朝上、短兵器刃朝下
                bool pole = style == "pole";
                DuelXf.Pos(wpn, 0.34f, pole ? 0.63f : 0.9f, 0.36f);
                DuelXf.RotYZX(wpn, 0, 0.2f, (pole ? 94 : -86) * Mathf.Deg2Rad - Mathf.PI / 2);
            }
            else
            {
                DuelXf.Pos(wpn, G);
                DuelXf.RotYZX(wpn, 0, wy, wa - Mathf.PI / 2);
            }
            // 左手目标
            Vector3 O;
            if (style == "pole" && bowK <= 0)
            {
                var dir = new Vector3(Mathf.Cos(wa) * Mathf.Cos(wy), Mathf.Sin(wa), -Mathf.Cos(wa) * Mathf.Sin(wy));
                var SL = torsoM.MultiplyPoint3x4(new Vector3(0, 0.47f, -0.22f * bk));
                float reach = (DuelModels.LenUpper + DuelModels.LenFore) * 0.97f;
                float best = 0.12f;
                for (float s = 0.46f; s >= 0.12f; s -= 0.04f)
                {
                    var t = G + dir * s;
                    if (Vector3.Distance(t, SL) <= reach) { best = s; break; }
                }
                O = G + dir * best;
            }
            else O = new Vector3(P[DuelPose.ox], P[DuelPose.oy], P[DuelPose.oz]);
            if (wpn2 != null)
            {
                DuelXf.Pos(wpn2, O);
                DuelXf.RotYZX(wpn2, 0, 0, P[DuelPose.wa2] * Mathf.Deg2Rad - Mathf.PI / 2);
            }
            if (shield != null)
            {   // 盾挂在左手前方，盾面朝前并略转向镜头
                DuelXf.Pos(shield, O.x + 0.07f, O.y - 0.02f, O.z + 0.02f);
                DuelXf.RotYZX(shield, 0, -0.5f + tw * 0.4f, -P[DuelPose.lean] * 0.4f);
            }
            // 手臂 IK
            foreach (var sd in new[] { 1, -1 })
            {
                var S = torsoM.MultiplyPoint3x4(new Vector3(0, 0.47f, sd * 0.22f * bk));
                var tg = sd > 0 ? G : O;
                Vector3 E, H;
                SolveIK(S, tg, DuelModels.LenUpper, DuelModels.LenFore, new Vector3(-0.35f, -1, sd * 0.55f), out E, out H);
                var up = Part(sd > 0 ? "upperR" : "upperL"); var fo = Part(sd > 0 ? "foreR" : "foreL");
                if (up != null) Orient(up, S, E);
                if (fo != null) Orient(fo, E, H);
                if (sd > 0) handR = H; else handL = H;
            }
            // 腿 IK
            foreach (var sd in new[] { 1, -1 })
            {
                var Hp = new Vector3(hx, hy - 0.06f, sd * 0.1f * bk);
                var F = new Vector3(sd > 0 ? P[DuelPose.fRx] : P[DuelPose.fLx], sd > 0 ? P[DuelPose.fRy] : P[DuelPose.fLy], sd * 0.13f * bk);
                Vector3 E, H;
                SolveIK(Hp, F, DuelModels.LenThigh, DuelModels.LenShin, new Vector3(1, 0, sd * 0.15f), out E, out H);
                var th = Part(sd > 0 ? "thighR" : "thighL"); var sn = Part(sd > 0 ? "shinR" : "shinL"); var ft = Part(sd > 0 ? "footR" : "footL");
                if (th != null) Orient(th, Hp, E);
                if (sn != null) Orient(sn, E, H);
                if (ft != null) { DuelXf.Pos(ft, H); DuelXf.RotXYZ(ft, 0, 0, -Mathf.Max(0, (H.y - 0.1f) * 0.8f)); }
            }
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            foreach (var x in meshes) if (x != null) Object.Destroy(x);
            foreach (var x in owned) if (x != null) Object.Destroy(x);
            meshes.Clear(); owned.Clear();
        }
    }
}
