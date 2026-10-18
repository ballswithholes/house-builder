// ==========================================================================
// 三国志-霸王的世界 · 人物模型库 —— 骨架、动作片段、双骨 IK、贴地、弹簧随动、战马步态（网页版 js/figure.js 的移植）
//
// 纯 C#（不依赖 UnityEngine，可无头测试）。全部在 three 坐标里计算（与 JS 逐行一致）：
//   FigClips：片段目录与关键帧（FigureData.g.cs 导出 compiled() 的结果）、samplePose、程序化步态 gaitPose、blendPoses
//   FigRig：一个人物的骨骼局部变换（position / quaternion，= THREE.Bone），setPose → _apply / _ground / _arms / _ik …，
//           grip / ikHand / update(dt) 弹簧；FK 求骨骼在 mesh 空间的位姿
//   FigHorseRig：战马骨骼与 horsePose（idle walk trot gallop rear die）、骑手挂在鞍座骨上
//   FigBake：士兵群的动作烘焙（每帧每骨一个 3×4 蒙皮矩阵，已换成 Unity 坐标）与半精度浮点转换
// Unity 端（View/Figure.cs）把骨骼的局部变换经 FigConv 换手系后写入 Transform.localPosition / localRotation。
// ==========================================================================
using System;
using System.Collections.Generic;
using static Sanguo.FigMath;

namespace Sanguo
{
    // 采样得到的姿势（= JS newPoseObj：17 个三元字段 + 8 个标量字段；N[i] = 该三元字段缺省）
    public sealed class FigPose
    {
        public const int hip = 0, rot = 1, pel = 2, sp = 3, ch = 4, nk = 5, hd = 6, tL = 7, fL = 8, tR = 9, fR = 10, g = 11, w = 12, o = 13, ow = 14, eR = 15, eL = 16;
        public const int kL = 0, kR = 1, twL = 2, cape = 3, sh = 4, gnd = 5, gw = 6, capeF = 7;
        public readonly V3d[] v = new V3d[17];
        public readonly bool[] N = new bool[17];
        public readonly double[] s = new double[8];
        public FigPose CopyTo(FigPose o) { Array.Copy(v, o.v, 17); Array.Copy(N, o.N, 17); Array.Copy(s, o.s, 8); return o; }
    }

    // setPose 的选项（= JS opts）
    public sealed class FigPoseOpts
    {
        public string lower;            // 下半身另用一个片段（如骑乘 'ride'）
        public double lowerT = double.NaN;
        public string blendClip; public double blendT, blendW = 0.5;   // 与另一片段混合，blendW = 本片段的权重
        public bool ik = true;          // false：不做双手 IK
        public bool gallop;             // 骑乘时下半身用疾驰
        public bool clamp = true;       // false：关闭贴地
    }

    public sealed class FigClipInfo { public string name, desc; public double dur; public bool loop, gait; }

    // ------------------------------------------------------------------ 片段
    public static class FigClips
    {
        public sealed class Key { public double t; public int e; public V3d?[] f3 = new V3d?[17]; public double[] f1 = new double[8]; }
        public sealed class Clip { public double dur; public bool loop; public Key[] keys; }
        static Dictionary<string, FigClipInfo> info;
        static readonly Dictionary<string, Clip> compiledCache = new Dictionary<string, Clip>();
        static readonly object lk = new object();
        static FigClips()
        {
            for (int i = 0; i < 17; i++) if (FigureData.FIELDS3[i] != new[] { "hip", "rot", "pel", "sp", "ch", "nk", "hd", "tL", "fL", "tR", "fR", "g", "w", "o", "ow", "eR", "eL" }[i]) throw new Exception("FIELDS3 顺序变了");
            for (int i = 0; i < 8; i++) if (FigureData.FIELDS1[i] != new[] { "kL", "kR", "twL", "cape", "sh", "gnd", "gw", "capeF" }[i]) throw new Exception("FIELDS1 顺序变了");
        }
        public static Dictionary<string, FigClipInfo> Catalog
        {
            get
            {
                lock (lk)
                {
                    if (info == null)
                    {
                        var d = new Dictionary<string, FigClipInfo>();
                        foreach (var kv in FigTables.CLIPS) { var o = (JObj)kv.Value; d[kv.Key] = new FigClipInfo { name = kv.Key, dur = o.D("dur"), loop = o.T("loop"), desc = o.S("desc"), gait = o.T("gait") }; }
                        info = d;
                    }
                    return info;
                }
            }
        }
        public static FigClipInfo Info(string name) { FigClipInfo c; return name != null && Catalog.TryGetValue(name, out c) ? c : null; }
        // 编译后的片段（合并站姿）；步态片段或未知片段为 null
        public static Clip Compiled(string name, string style)
        {
            int[] ids;
            if (name == null || !FigureData.ClipIndex.TryGetValue(name, out ids)) return null;
            int si = Array.IndexOf(FigureData.ClipStyles, style); if (si < 0) si = Array.IndexOf(FigureData.ClipStyles, "one");
            string key = name + "|" + si;
            lock (lk)
            {
                Clip c;
                if (compiledCache.TryGetValue(key, out c)) return c;
                var raw = FigureData.ClipKeys[ids[si]]; int S = FigureData.KeyStride, n = raw.Length / S;
                var ci = Info(name);
                c = new Clip { dur = ci.dur, loop = ci.loop, keys = new Key[n] };
                for (int k = 0; k < n; k++)
                {
                    int o = k * S; var K = new Key { t = raw[o], e = (int)raw[o + 1] };
                    for (int f = 0; f < 17; f++) { double x = raw[o + 2 + f * 3]; if (!double.IsNaN(x)) K.f3[f] = new V3d(x, raw[o + 3 + f * 3], raw[o + 4 + f * 3]); }
                    for (int f = 0; f < 8; f++) K.f1[f] = raw[o + 2 + 51 + f];
                    c.keys[k] = K;
                }
                compiledCache[key] = c;
                return c;
            }
        }
        static double ease(int e, double t) { return e == 3 ? t : e == 1 ? t * t : e == 2 ? 1 - (1 - t) * (1 - t) : t * t * (3 - 2 * t); }
        static double Z(double v) { return double.IsNaN(v) ? 0 : v; }   // JS `x || 0`
        public static FigPose samplePose(Clip c, double t, FigPose o)
        {
            var K = c.keys; int n = K.Length;
            if (c.loop) t = ((t % c.dur) + c.dur) % c.dur; else t = clamp(t, 0, c.dur);
            int i = 0;
            while (i < n - 2 && K[i + 1].t <= t) i++;
            var a = K[i]; var b = K[Math.Min(n - 1, i + 1)];
            double u = b.t > a.t ? ease(b.e, clamp01((t - a.t) / (b.t - a.t))) : 1;
            for (int k = 0; k < 17; k++)
            {
                var A = a.f3[k]; var Bv = b.f3[k];
                if (A == null && Bv == null) { o.N[k] = true; continue; }
                o.N[k] = false;
                var AA = A ?? Bv.Value; var BB = Bv ?? A.Value;
                o.v[k] = new V3d(lerp(AA.x, BB.x, u), lerp(AA.y, BB.y, u), lerp(AA.z, BB.z, u));
            }
            for (int k = 0; k < 8; k++) o.s[k] = lerp(Z(a.f1[k]), Z(b.f1[k]), u);
            return o;
        }
        // 程序化步态（walk run dash ride gallop）
        public static FigPose gaitPose(string name, double t, string style, FigPose o)
        {
            var c = Compiled("idle", style);
            samplePose(c, 0, o);
            double dur = Info(name).dur, ph = ((t % dur) + dur) % dur / dur * TAU;
            double sn = Math.Sin(ph), cs = Math.Cos(ph);
            var v = o.v; var s = o.s;
            if (name == "ride" || name == "gallop")
            {
                bool gl = name == "gallop";
                v[FigPose.hip] = new V3d(0, (gl ? 0.025 : 0.01) * Math.Abs(Math.Sin(ph)) - 0.02, 0);
                v[FigPose.pel] = new V3d(0, 0, 0);
                v[FigPose.sp].x = (gl ? 14 : 4) + sn * (gl ? 4 : 1.5); v[FigPose.ch].x = gl ? 10 : 2; v[FigPose.ch].y = 0; v[FigPose.hd].x = gl ? -12 : -3; v[FigPose.hd].y = 0;
                foreach (var q in new[] { new[] { FigPose.tL, FigPose.kL, FigPose.fL, 1 }, new[] { FigPose.tR, FigPose.kR, FigPose.fR, -1 } })
                {
                    int T = q[0], Kk = q[1], F = q[2], sg = q[3];
                    v[T].x = -62; v[T].y = sg * 4; v[T].z = sg * 24; s[Kk] = 78; v[F].x = -10; v[F].y = 0; v[F].z = -sg * 10;
                }
                v[FigPose.g] = new V3d(-0.24, 1.1 + (gl ? 0.02 * sn : 0), 0.22);
                v[FigPose.w] = new V3d(10, gl ? 72 : 35, -90);
                if (style == "pole") { v[FigPose.w].x = 18; v[FigPose.w].y = gl ? 80 : 42; }
                if (!o.N[FigPose.o] && style != "pole") v[FigPose.o] = new V3d(0.12, 1.1, 0.32);
                s[FigPose.cape] = gl ? 0.9 : 0.25;
                return o;
            }
            bool run = name == "run" || name == "dash", dash = name == "dash";
            double A = dash ? 46 : run ? 38 : 24, kb = dash ? 85 : run ? 70 : 30;
            v[FigPose.hip].y = (run ? -0.06 : -0.03) - (run ? 0.035 : 0.015) * Math.Abs(cs) + (run ? 0.02 : 0);
            v[FigPose.hip].z = 0;
            v[FigPose.pel].y = sn * (run ? 8 : 6); v[FigPose.pel].x = 0;
            v[FigPose.sp].x = run ? 10 : 2; v[FigPose.ch].x = run ? (dash ? 16 : 10) : 3; v[FigPose.ch].y = -sn * (run ? 10 : 6); v[FigPose.hd].x = run ? -10 : -3; v[FigPose.hd].y = sn * 3;
            v[FigPose.tL].x = -sn * A - (run ? 10 : 4); v[FigPose.tL].z = 4; v[FigPose.tR].x = sn * A - (run ? 10 : 4); v[FigPose.tR].z = -4;
            s[FigPose.kL] = 8 + Math.Max(0, cs) * kb + (run ? 12 : 4); s[FigPose.kR] = 8 + Math.Max(0, -cs) * kb + (run ? 12 : 4);
            v[FigPose.fL].x = -v[FigPose.tL].x * 0.5 - s[FigPose.kL] * 0.4 + 10; v[FigPose.fR].x = -v[FigPose.tR].x * 0.5 - s[FigPose.kR] * 0.4 + 10;
            v[FigPose.fL].y = v[FigPose.fR].y = 0; v[FigPose.fL].z = v[FigPose.fR].z = 0;
            v[FigPose.tL].y = v[FigPose.tR].y = 0;
            // 兵器：奔跑时拖在身后（长兵）/ 握在体侧
            if (run)
            {
                if (style == "pole") { v[FigPose.g] = new V3d(-0.3, 1.02 + 0.02 * sn, -0.05); v[FigPose.w] = new V3d(20, 100, -90); }
                else { v[FigPose.g] = new V3d(-0.3, 1.02 + 0.05 * sn, -0.05 + 0.15 * -sn); v[FigPose.w] = new V3d(0, 110, -90); v[FigPose.o] = new V3d(0.28, 1.05 + 0.04 * -sn, 0.15 * sn); }
            }
            else
            {
                v[FigPose.g].z += 0.05 * -sn * (style == "pole" ? 0.3 : 1);
                if (style != "pole") v[FigPose.o].z += 0.08 * sn;
            }
            s[FigPose.cape] = run ? (dash ? 1 : 0.7) : 0.15;
            return o;
        }
        // P = lerp(P, Q, w)
        public static FigPose blendPoses(FigPose P, FigPose Q, double w)
        {
            for (int k = 0; k < 17; k++)
            {
                if (P.N[k] && !Q.N[k]) { P.v[k] = Q.v[k]; P.N[k] = false; continue; }
                if (Q.N[k]) continue;
                var a = P.v[k]; var b = Q.v[k];
                P.v[k] = new V3d(lerp(a.x, b.x, w), lerp(a.y, b.y, w), lerp(a.z, b.z, w));
            }
            for (int k = 0; k < 8; k++) P.s[k] = lerp(P.s[k], Q.s[k], w);
            return P;
        }
    }

    // ------------------------------------------------------------------ 一个人物的骨架（three 坐标；= JS class Figure 的姿势部分）
    public sealed class FigRig
    {
        public readonly FigTemplate tpl;
        public readonly FigProps pr;
        public string style;
        public FigWeaponInfo weaponInfo;
        public readonly int n;
        public readonly V3d[] pos, restPos;
        public readonly Q4d[] quat;
        public readonly V3d[] euler;          // 以 Euler XYZ 设定旋转的骨骼（披风、腰垂、盔缨、发）的当前欧拉角
        public readonly V3d[] Wp; public readonly Q4d[] Wq;   // FK 结果（mesh 空间）
        public bool groundClamp = true;
        public FigHorseRig mounted;
        public bool hasShield;
        public FigBuild.Grip gripR, gripL;
        public FigBuild.Sock shield;
        public V3d backSock, headTopSock;
        public string clipName; public double clipT; public bool hasClip;
        public double lift;
        readonly FigPose P = new FigPose(), P2 = new FigPose(), PL = new FigPose();
        readonly FigRing[] R;
        readonly double skirtFl;
        readonly bool hasCape;
        public bool HasCape { get { return hasCape; } }
        // 弹簧状态（= JS _sec）
        double cape, capeV, plume, plumeV, hair, hairV, tgtCape, tgtPlume, tgtHair, capeF, time; bool live;
        double[] wRange; double armReach = double.NaN;
        static readonly V3d AX = new V3d(1, 0, 0), AY = new V3d(0, 1, 0), AZ = new V3d(0, 0, 1);
        static readonly double[] DEF_O = { 0.27, 1.0, 0.1 }, DEF_OW = { 0, 0, 0 }, DEF_OFF = { 0, 0.5, 0 };
        static readonly int[] FK_LIST = { FigBones.root, FigBones.pelvis, FigBones.spine, FigBones.chest, FigBones.clavL, FigBones.upperArmL, FigBones.foreArmL, FigBones.handL, FigBones.clavR, FigBones.upperArmR, FigBones.foreArmR, FigBones.handR };
        static readonly bool[] EULER_BONES = MkEuler();
        static bool[] MkEuler() { var a = new bool[FigBones.Count]; foreach (var b in new[] { FigBones.cape1, FigBones.cape2, FigBones.cape3, FigBones.skirtF, FigBones.skirtB, FigBones.plume1, FigBones.plume2, FigBones.hair1 }) a[b] = true; return a; }
        // IK 的绑定臂轴（JS SOCK：全局缓存，首个人物的值；臂轴方向与体型无关）
        static V3d? a0L, a0R;

        public FigRig(FigTemplate tpl)
        {
            this.tpl = tpl; pr = tpl.pr; style = tpl.style; weaponInfo = tpl.weapon;
            n = FigBones.Count;
            pos = new V3d[n]; restPos = new V3d[n]; quat = new Q4d[n]; euler = new V3d[n]; Wp = new V3d[n]; Wq = new Q4d[n];
            for (int i = 0; i < n; i++)
            {
                int p = FigBones.PARENT[i];
                var J = pr.J[i]; var PJ = p >= 0 ? pr.J[p] : new V3d(0, 0, 0);
                pos[i] = restPos[i] = new V3d(J.x - PJ.x, J.y - PJ.y, J.z - PJ.z);
                quat[i] = Q4d.Identity;
            }
            gripR = FigBuild.gripLocal(pr, "R"); gripL = FigBuild.gripLocal(pr, "L");
            shield = FigBuild.shieldLocal(pr);
            backSock = new V3d(0, 0.18 * pr.k, -0.17 * pr.k); headTopSock = new V3d(0, 0.26 * pr.k, 0);
            hasShield = tpl.shieldGeo != null || tpl.hasShield;
            R = FigBuild.torsoProfile(pr);
            hasCape = tpl.look.C.cape.HasValue;
            string sk = tpl.look.L.S("skirt") ?? "";
            skirtFl = (sk.Contains("robe") || sk.Contains("coat") || sk.Contains("sarong")) ? 0.12 : (sk != "" && sk != "none") ? 0.07 : 0.02;
            SetPose("idle", 0, null);
        }
        public FigPose LastPose { get { return P; } }
        void SetEuler(int b, double x, double y, double z) { euler[b] = new V3d(x, y, z); quat[b] = Q4d.Euler(x, y, z, "XYZ"); }
        static Q4d qEuler(V3d a, string order = "YXZ") { return Q4d.Euler(Z0(a.x) * D2R, Z0(a.y) * D2R, Z0(a.z) * D2R, order); }
        static double Z0(double v) { return double.IsNaN(v) ? 0 : v; }
        // 兵器朝向：先绕 Y 偏航，再绕 X 俯仰，最后绕自身 Y 横滚
        public static Q4d qWeapon(V3d w) { return Q4d.AxisAngle(AY, Z0(w.x) * D2R) * Q4d.AxisAngle(AX, Z0(w.y) * D2R) * Q4d.AxisAngle(AY, Z0(w.z) * D2R); }

        // ------------------------------------------------------------ 姿势 --
        FigPose Sample(string name, double t, FigPose o)
        {
            var info = FigClips.Info(name) ?? FigClips.Info("idle");
            if (info.gait) return FigClips.gaitPose(FigClips.Info(name) != null ? name : "idle", t, style, o);
            var cc = FigClips.Compiled(name, style) ?? FigClips.Compiled("idle", style);
            return FigClips.samplePose(cc, t, o);
        }
        public void SetPose(string name, double t, FigPoseOpts opts)
        {
            opts = opts ?? DefOpts;
            var Pp = Sample(name, t, P);
            if (opts.blendClip != null)
            {
                var Q = Sample(opts.blendClip, opts.blendT, P2);
                FigClips.blendPoses(Pp, Q, 1 - clamp01(opts.blendW));
            }
            string lowerName = opts.lower ?? (mounted != null ? (opts.gallop || name == "gallop" ? "gallop" : "ride") : null);
            if (lowerName != null && lowerName != name)
            {
                var L = Sample(lowerName, !double.IsNaN(opts.lowerT) ? opts.lowerT : t, PL);
                foreach (int k in new[] { FigPose.hip, FigPose.pel, FigPose.tL, FigPose.tR, FigPose.fL, FigPose.fR }) Pp.v[k] = L.v[k];
                Pp.s[FigPose.kL] = L.s[FigPose.kL]; Pp.s[FigPose.kR] = L.s[FigPose.kR];
                if (mounted != null) { Pp.v[FigPose.rot].x = 0; Pp.v[FigPose.rot].z = 0; }
            }
            clipName = name; clipT = t; hasClip = true;
            Apply(Pp, opts);
        }
        static readonly FigPoseOpts DefOpts = new FigPoseOpts();
        public void BlendPose(string a, double ta, string b, double tb, double w) { SetPose(a, ta, new FigPoseOpts { blendClip = b, blendT = tb, blendW = 1 - w }); }

        void Apply(FigPose Pp, FigPoseOpts opts)
        {
            var v = Pp.v; var J = pr.J;
            // 躯干与腿
            quat[FigBones.root] = qEuler(v[FigPose.rot], "YXZ");
            pos[FigBones.root] = new V3d(0, 0, 0);
            pos[FigBones.pelvis] = restPos[FigBones.pelvis] + v[FigPose.hip];
            quat[FigBones.pelvis] = qEuler(v[FigPose.pel]);
            quat[FigBones.spine] = qEuler(v[FigPose.sp]);
            quat[FigBones.chest] = qEuler(v[FigPose.ch]);
            quat[FigBones.neck] = qEuler(v[FigPose.nk]);
            quat[FigBones.head] = qEuler(v[FigPose.hd]);
            quat[FigBones.thighL] = qEuler(v[FigPose.tL], "XZY"); quat[FigBones.thighR] = qEuler(v[FigPose.tR], "XZY");
            quat[FigBones.shinL] = Q4d.AxisAngle(AX, Pp.s[FigPose.kL] * D2R); quat[FigBones.shinR] = Q4d.AxisAngle(AX, Pp.s[FigPose.kR] * D2R);
            quat[FigBones.footL] = qEuler(v[FigPose.fL], "XZY"); quat[FigBones.footR] = qEuler(v[FigPose.fR], "XZY");
            // 锁骨：手举高时耸肩（举过头顶时最多约 30°）
            double gy = v[FigPose.g].y - J[FigBones.upperArmR].y;
            quat[FigBones.clavR] = Q4d.AxisAngle(AZ, -clamp(gy * 70, 0, 30) * D2R);
            double shk = style == "pole" ? clamp01(Pp.s[FigPose.sh]) : 0;
            double oy = Math.Max(!Pp.N[FigPose.o] ? v[FigPose.o].y : 1.0, shk * (v[FigPose.g].y + 0.25)) - J[FigBones.upperArmL].y;
            quat[FigBones.clavL] = Q4d.AxisAngle(AZ, clamp(oy * 70, 0, 30) * D2R);
            Secondary(Pp);
            // 贴地：身体任何部位不穿入地面（倒地 / 跪地片段同时让最低点落在地面上）
            if (mounted == null && opts.clamp) Ground(Pp);
            if (opts.ik) Arms(Pp);
            // 肩部辅助骨：取上臂局部旋转的一部分
            quat[FigBones.shL] = Q4d.Identity.Slerp(quat[FigBones.upperArmL], FigureData.SH_K);
            quat[FigBones.shR] = Q4d.Identity.Slerp(quat[FigBones.upperArmR], FigureData.SH_K);
            quat[FigBones.dlL] = Q4d.Identity.Slerp(quat[FigBones.upperArmL], FigureData.DL_K);
            quat[FigBones.dlR] = Q4d.Identity.Slerp(quat[FigBones.upperArmR], FigureData.DL_K);
        }
        // 全身正向运动学（mesh 空间）
        public void FkAll()
        {
            for (int i = 0; i < n; i++)
            {
                int j = FigBones.PARENT[i];
                if (j < 0) { Wq[i] = quat[i]; Wp[i] = pos[i]; continue; }
                Wq[i] = Wq[j] * quat[i];
                Wp[i] = pos[i].ApplyQ(Wq[j]) + Wp[j];
            }
        }
        void Fk()
        {
            foreach (int i in FK_LIST)
            {
                int j = FigBones.PARENT[i];
                if (j < 0) { Wq[i] = quat[i]; Wp[i] = pos[i]; continue; }
                Wq[i] = Wq[j] * quat[i];
                Wp[i] = pos[i].ApplyQ(Wq[j]) + Wp[j];
            }
        }
        void Ground(FigPose Pp)
        {
            double k = pr.k;
            FkAll();
            double mn = 1e9;
            Action<int, double, double, double, double> pt = (i, x, y, z, r) => { var vv = new V3d(x * k, y * k, z * k).ApplyQ(Wq[i]) + Wp[i]; double h = vv.y - r * k; if (h < mn) mn = h; };
            pt(FigBones.pelvis, 0, 0, 0, 0.125); pt(FigBones.pelvis, 0.11, -0.03, 0, 0.1); pt(FigBones.pelvis, -0.11, -0.03, 0, 0.1);
            pt(FigBones.spine, 0, 0.06, 0.005, 0.115); pt(FigBones.chest, 0, 0.07, 0, 0.13); pt(FigBones.chest, 0.15, 0.15, -0.01, 0.075); pt(FigBones.chest, -0.15, 0.15, -0.01, 0.075);
            pt(FigBones.head, 0, 0.115, 0.01, 0.112); pt(FigBones.neck, 0, 0.04, 0, 0.06);
            foreach (var S in new[] { "L", "R" })
            {
                int th = FigBones.B("thigh" + S), sh = FigBones.B("shin" + S), ft = FigBones.B("foot" + S);
                var a = Wp[th]; var b = Wp[sh]; var c = Wp[ft];
                double y = (a.y + b.y) / 2 - 0.08 * k; if (y < mn) mn = y;
                y = b.y - 0.062 * k; if (y < mn) mn = y;
                y = (b.y + c.y) / 2 - 0.055 * k; if (y < mn) mn = y;
                pt(ft, 0, 0, 0, 0.045); pt(ft, 0, -0.078, -0.06, 0.008); pt(ft, 0, -0.07, 0.16, 0.012);
            }
            if (hasCape) { pt(FigBones.cape2, 0, 0, -0.02, 0.02); pt(FigBones.cape3, 0, 0, -0.03, 0.02); pt(FigBones.cape3, 0, -0.38, -0.07, 0.015); }
            double g = clamp01(Pp.s[FigPose.gnd]);
            double lf = g * -mn + (1 - g) * Math.Max(0, -mn);
            if (Math.Abs(lf) > 1e-5) pos[FigBones.root] = new V3d(pos[FigBones.root].x, lf, pos[FigBones.root].z);
            lift = lf;
        }
        // 双骨 IK：S 侧手腕到 wrist（root 局部），手朝向 handQ，肘部提示 pole，前臂扭转 tw（度）
        void Ik(string S, V3d wrist, Q4d handQ, V3d pole, double tw)
        {
            int iC = FigBones.B("clav" + S), iU = FigBones.B("upperArm" + S), iF = FigBones.B("foreArm" + S), iH = FigBones.B("hand" + S);
            var Sp = Wp[iU];
            var ru = pos[iF]; var rf = pos[iH];
            double a = ru.Length(), b = rf.Length();
            var d = wrist - Sp;
            double dist = d.Length();
            var dir = dist > 1e-6 ? d * (1 / dist) : new V3d(0, -1, 0);
            dist = clamp(dist, Math.Abs(a - b) + 1e-3, a + b - 1e-4);
            var pl = new V3d(Z0(pole.x), Z0(pole.y), Z0(pole.z));
            pl = pl + dir * -pl.Dot(dir);
            if (pl.LengthSq() < 1e-8) { var r6 = new V3d(S == "L" ? 1 : -1, -0.3, -0.3); pl = r6 + dir * -dir.Dot(r6); }
            pl = pl.Normalized();
            double cosA = clamp((a * a + dist * dist - b * b) / (2 * a * dist), -1, 1), sinA = Math.Sqrt(1 - cosA * cosA);
            var E = Sp + dir * (a * cosA) + pl * (a * sinA);
            var Tc = Sp + dir * dist;
            var a1 = (E - Sp).Normalized();
            var f1 = (Tc - E).Normalized();
            double beta = Math.Acos(clamp(a1.Dot(f1), -1, 1));
            double n1x = a1.y * f1.z - a1.z * f1.y, n1y = a1.z * f1.x - a1.x * f1.z, n1z = a1.x * f1.y - a1.y * f1.x;
            double nl = hypot(n1x, n1y, n1z);
            if (nl < 1e-4) { n1x = -(a1.y * pl.z - a1.z * pl.y); n1y = -(a1.z * pl.x - a1.x * pl.z); n1z = -(a1.x * pl.y - a1.y * pl.x); nl = hypot(n1x, n1y, n1z); if (nl == 0) nl = 1; }
            n1x /= nl; n1y /= nl; n1z /= nl;
            // 绑定姿势的臂轴与屈肘轴
            V3d a0;
            if (S == "L") { if (a0L == null) a0L = ru.Normalized(); a0 = a0L.Value; } else { if (a0R == null) a0R = ru.Normalized(); a0 = a0R.Value; }
            var n0 = v3cross(a0, AZ).Normalized();
            var c0 = v3cross(a0, n0);
            double c1x = a1.y * n1z - a1.z * n1y, c1y = a1.z * n1x - a1.x * n1z, c1z = a1.x * n1y - a1.y * n1x;
            // Qu = Mdes · Mrestᵀ（行主序写法与 JS 的 Matrix4.set 一致）
            var m = new M4d(); var e = m.e;
            double[] A1 = { a1.x, a1.y, a1.z }, N1 = { n1x, n1y, n1z }, C1 = { c1x, c1y, c1z }, A0 = { a0.x, a0.y, a0.z }, N0 = { n0.x, n0.y, n0.z }, C0 = { c0.x, c0.y, c0.z };
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) e[c * 4 + r] = A1[r] * A0[c] + N1[r] * N0[c] + C1[r] * C0[c];
            var Qu = Q4d.FromMatrix(m);
            quat[iU] = Wq[iC].Inverse() * Qu;
            var qf = Q4d.AxisAngle(n0, beta);
            if (tw != 0) qf = qf * Q4d.AxisAngle(a0, tw * D2R);
            quat[iF] = qf;
            var Qf = Qu * qf;
            quat[iH] = Qf.Inverse() * handQ;
        }
        // 双手：右手按兵器朝向 IK；左手按流派（长兵握杆 / 自由位置）
        void Arms(FigPose Pp)
        {
            var v = Pp.v;
            var giR = gripR.q.Inverse(); var giL = gripL.q.Inverse();
            var gpR = gripR.p; var gpL = gripL.p;
            var flip = Q4d.AxisAngle(AY, PI);
            Fk();
            var rootQ = Wq[FigBones.root]; var rootP = Wp[FigBones.root];
            string st = style;
            var Qw = rootQ * qWeapon(v[FigPose.w]);
            var G = v[FigPose.g];
            if (mounted == null) G = AvoidBody(G, Pp);
            G = G.ApplyQ(rootQ) + rootP;
            double gw = mounted != null ? 0 : clamp01(Pp.s[FigPose.gw]);
            if (gw > 0.001) GroundWeapon(ref G, ref Qw, gw);
            if (mounted == null && G.y < 0.04) G.y = 0.04;
            if (mounted == null && gw < 0.999) WeaponAboveGround(G, ref Qw);
            var handR = Qw * giR;
            var wR = G - gpR.ApplyQ(handR);
            Ik("R", wR, handR, v[FigPose.eR], 0);
            // 左手：长兵按 sh（0..1）在「自由位置」与「握杆」之间过渡；其他流派为自由位置（盾 / 副手兵器 / 弓）
            var o = !Pp.N[FigPose.o] ? v[FigPose.o] : new V3d(DEF_O[0], DEF_O[1], DEF_O[2]);
            var handL = rootQ * qWeapon(v[FigPose.ow]) * giL;   // JS：P.ow || DEF_OW（数组恒在，缺省时沿用上次的值）
            var op = o;
            if (!hasShield || st == "bow") op = AvoidBody(op, Pp);
            var wristL = op.ApplyQ(rootQ) + rootP;
            if (mounted == null && wristL.y < 0.04) wristL.y = 0.04;
            wristL = wristL - gpL.ApplyQ(handL);
            double sh = st == "pole" ? clamp01(Pp.s[FigPose.sh]) : 0;
            if (sh > 0.001)
            {
                Fk();
                // 实际握点（IK 可能够不到目标）→ 杆上的左手位置（JS 里 Gp 与 A.G 是同一个向量，此处照样共用 G）
                G = gpR.ApplyQ(Wq[FigBones.handR]) + Wp[FigBones.handR];
                var off = weaponInfo != null && weaponInfo.off.HasValue ? weaponInfo.off.Value : new V3d(DEF_OFF[0], DEF_OFF[1], DEF_OFF[2]);
                var hS = Qw * flip * giL;
                // 腕点 = 杆上点 − 握点偏移（随兵器朝向）：在腕点上求可及区间
                var wo = gpL.ApplyQ(hS);
                G = G - wo;
                var dir = AY.ApplyQ(Qw);
                // 整根杆都在左臂够不到的地方：把兵器（右手握点）朝左肩平移刚好够得着的距离，再解一次右臂
                {
                    var S = Wp[FigBones.upperArmL]; double Rr = ReachL();
                    double b = (G.x - S.x) * dir.x + (G.y - S.y) * dir.y + (G.z - S.z) * dir.z;
                    double tc = clamp(-b, 0.2, 1.2);
                    double cx = G.x + dir.x * tc - S.x, cy = G.y + dir.y * tc - S.y, cz = G.z + dir.z * tc - S.z, dd = hypot(cx, cy, cz);
                    if (dd > Rr)
                    {
                        double k = -(dd - Rr * 0.98) / dd;
                        G.x += cx * k; G.y += cy * k; G.z += cz * k;
                        wR = G - gpR.ApplyQ(handR);
                        Ik("R", wR, handR, v[FigPose.eR], 0);
                        Fk();
                        G = gpR.ApplyQ(Wq[FigBones.handR]) + Wp[FigBones.handR] - wo;
                    }
                }
                double t = ShaftT(G, dir, off.y * 0.8);
                var wS = G + dir * t;
                if (sh >= 0.999) { handL = hS; wristL = wS; }
                else { double kk = sh * sh * (3 - 2 * sh); handL = handL.Slerp(hS, kk); wristL = v3lerp(wristL, wS, kk); }
            }
            Ik("L", wristL, handL, v[FigPose.eL], Pp.s[FigPose.twL]);
        }
        // 左手在杆上的位置（沿杆距右手握点 t 米）：在左臂可及的区间内取最接近 tPref 的值；两手至少相距 0.2 米
        double ShaftT(V3d Gp, V3d dir, double tPref)
        {
            var S = Wp[FigBones.upperArmL];
            double Rr = ReachL();
            double rx = Gp.x - S.x, ry = Gp.y - S.y, rz = Gp.z - S.z;
            double b = rx * dir.x + ry * dir.y + rz * dir.z, c = rx * rx + ry * ry + rz * rz - Rr * Rr;
            var wi = weaponInfo; double hiT = wi != null ? Math.Max(0.3, wi.@base.y - 0.12) : 0.9, loT = -0.75;
            double disc = b * b - c;
            if (disc < 0) return clamp(-b, 0.2, hiT);              // 够不到：取杆上离肩最近的点（IK 伸直）
            double r0 = -b - Math.Sqrt(disc), r1 = -b + Math.Sqrt(disc);
            // 候选区间：[0.2, hiT] 与 [loT, −0.2]（左手在右手之后，如反握）
            double best = double.NaN, bd = 1e9;
            foreach (var iv in new[] { new[] { 0.2, hiT }, new[] { loT, -0.2 } })
            {
                double lo = Math.Max(iv[0], r0), hi = Math.Min(iv[1], r1);
                if (lo > hi) continue;
                double t = clamp(tPref, lo, hi), d = Math.Abs(t - tPref) + (iv[0] < 0 ? 0.15 : 0);
                if (d < bd) { bd = d; best = t; }
            }
            return !double.IsNaN(best) ? best : clamp(-b, 0.2, hiT);
        }
        // 兵器两端不插入地面：低于地面的一端绕握点抬起，刚好贴地
        void WeaponAboveGround(V3d G, ref Q4d Qw)
        {
            if (wRange == null)
            {
                var g = tpl.weaponGeo;
                if (g != null) wRange = new[] { g.bmin.y, g.bmax.y };
                else { double len = weaponInfo != null && weaponInfo.len != 0 ? weaponInfo.len : 1; wRange = new[] { style == "pole" ? -0.85 : -0.12, len }; }
            }
            foreach (double y0 in wRange)
            {
                var e = new V3d(0, y0, 0).ApplyQ(Qw); double L = Math.Abs(y0);
                if (L < 0.05) continue;
                double floor = 0.015 - G.y;
                if (e.y >= floor) continue;
                double cur = Math.Asin(clamp(e.y / L, -1, 1)), tgt = Math.Asin(clamp(floor / L, -1, 1));
                var ax = new V3d(-e.z, 0, e.x);            // e × 上：绕此轴正转使 e 抬向上方
                if (ax.LengthSq() < 1e-8) ax = AX.ApplyQ(Wq[FigBones.root]);
                ax = ax.Normalized();
                Qw = Q4d.AxisAngle(ax, tgt - cur) * Qw;
            }
        }
        double ReachL() { if (double.IsNaN(armReach)) armReach = pos[FigBones.foreArmL].Length() + pos[FigBones.handL].Length(); return armReach * 0.985; }
        // 空手 / 副手目标不进入身体（含腰裙外张）：root 局部坐标
        V3d AvoidBody(V3d v, FigPose Pp)
        {
            double k = pr.k, y = v.y;
            if (y < 0.5 * k || y > 1.47 * k) return v;
            var r = FigBuild.ringAt(R, y);
            double below = clamp01((1.04 * k - y) / (0.5 * k));
            double pad = 0.05 * k + skirtFl * k * below + 0.02;
            // 骨盆偏航：在骨盆坐标中判断
            double a = -Z0(Pp.v[FigPose.pel].y) * D2R, ca = Math.Cos(a), sa = Math.Sin(a);
            double x = v.x * ca + v.z * sa, z = -v.x * sa + v.z * ca - r.zc;
            // 髋以下：两腿（含裤子外张）比躯干剖面宽
            double rx = r.rx, rz = r.rz;
            if (y < 1.0 * k) { double lr = 0.1 * k * pr.limb * 1.22, hk = sstep(1.0 * k, 0.9 * k, y); rx = Math.Max(rx, lerp(rx, Math.Abs(pr.J[FigBones.thighL].x) + lr, hk)); rz = Math.Max(rz, lerp(rz, lr, hk)); }
            double ex = rx + pad, ez = rz + pad, q = (x / ex) * (x / ex) + (z / ez) * (z / ez);
            if (q >= 1) return v;
            double f = 1 / Math.Sqrt(Math.Max(q, 1e-6));
            double nx = x * f, nz = z * f + r.zc;
            return new V3d(nx * ca - nz * sa, v.y, nx * sa + nz * ca);
        }
        // 倒地时兵器平放在身体右侧的地面上：改写右手握点与兵器朝向（按 w 混合）
        void GroundWeapon(ref V3d G, ref Q4d Qw, double w)
        {
            double k = pr.k;
            var side = new V3d(-1, 0, 0).ApplyQ(Wq[FigBones.root]); side.y = 0;
            var along = AY.ApplyQ(Wq[FigBones.root]); along.y = 0;
            if (side.LengthSq() < 1e-4) side = new V3d(-1, 0, 0); side = side.Normalized();
            if (along.LengthSq() < 1e-4) along = new V3d(0, 0, -1); along = along.Normalized();
            var sp = Wp[FigBones.upperArmR];
            var G2 = sp + side * (0.3 * k) + along * (-0.18 * k);
            G2.y = 0.045;
            var Y = (along + side * 0.22).Normalized();
            var Zv = v3cross(AY, Y).Normalized();
            var X = v3cross(Y, Zv);
            var q = Q4d.FromMatrix(M4d.Basis(X, Y, Zv));
            Qw = Qw.Slerp(q, w);
            G = v3lerp(G, G2, w);
        }
        // 披风、前后垂、盔缨、长发：程序化随动（调用 Update(dt) 时改用弹簧）
        void Secondary(FigPose Pp)
        {
            var v = Pp.v; var s = Pp.s;
            double tt = hasClip ? clipT : 0;
            double lean = (v[FigPose.sp].x + v[FigPose.ch].x) * D2R;
            double capeT = clamp(s[FigPose.cape], 0, 1.2) * 0.9 + 0.05 + Math.Max(0, lean) * 0.6;
            tgtCape = capeT;
            tgtPlume = -(v[FigPose.hd].x + v[FigPose.ch].x) * D2R * 0.3 + s[FigPose.cape] * 0.35;
            tgtHair = Math.Max(0, -(v[FigPose.hd].x) * D2R * 0.4) + s[FigPose.cape] * 0.3;
            double fl = Math.Sin(tt * 5.3) * 0.03 + Math.Sin(tt * 8.1 + 1) * 0.02 * s[FigPose.cape];
            double cp = live ? cape : capeT;
            double cf = clamp01(s[FigPose.capeF]);
            capeF = cf;
            SetEuler(FigBones.cape1, lerp(cp * 0.6 + fl, -0.2, cf), 0, 0);
            SetEuler(FigBones.cape2, lerp(cp * 0.35 + fl * 1.5, -0.12, cf), 0, 0);
            SetEuler(FigBones.cape3, lerp(cp * 0.25 + fl * 2, -0.06, cf), Math.Sin(tt * 3.1) * 0.05 * (1 - cf), 0);
            double fwd = Math.Min(0, Math.Min(v[FigPose.tL].x, v[FigPose.tR].x)) * D2R, back = Math.Max(0, Math.Max(v[FigPose.tL].x, v[FigPose.tR].x)) * D2R;
            SetEuler(FigBones.skirtF, fwd * 0.75, 0, 0);
            SetEuler(FigBones.skirtB, lerp(back * 0.6 + s[FigPose.cape] * 0.15, -0.3, cf), 0, 0);
            double pl = live ? plume : tgtPlume;
            SetEuler(FigBones.plume1, pl + Math.Sin(tt * 4.3) * 0.04, 0, Math.Sin(tt * 3.3) * 0.04);
            SetEuler(FigBones.plume2, pl * 0.8 + Math.Sin(tt * 6.1) * 0.06, 0, 0);
            double hr = live ? hair : tgtHair;
            SetEuler(FigBones.hair1, hr, 0, 0);
        }
        // 披风、飘带、盔缨的弹簧随动（每帧调用一次）
        public void Update(double dt)
        {
            if (!(dt > 0)) return;
            dt = Math.Min(dt, 0.05);
            live = true;
            Spring(ref cape, ref capeV, tgtCape, 60, 9, dt);
            Spring(ref plume, ref plumeV, tgtPlume, 90, 8, dt);
            Spring(ref hair, ref hairV, tgtHair, 70, 9, dt);
            cape = clamp(cape, -0.2, 1.4);
            time += dt;
            double fl = Math.Sin(time * 5.3) * 0.03, cf = capeF;
            SetEuler(FigBones.cape1, lerp(cape * 0.6 + fl, -0.2, cf), euler[FigBones.cape1].y, euler[FigBones.cape1].z);
            SetEuler(FigBones.cape2, lerp(cape * 0.35 + fl * 1.5, -0.12, cf), euler[FigBones.cape2].y, euler[FigBones.cape2].z);
            SetEuler(FigBones.cape3, lerp(cape * 0.25 + fl * 2, -0.06, cf), euler[FigBones.cape3].y, euler[FigBones.cape3].z);
            SetEuler(FigBones.plume1, plume + Math.Sin(time * 4.3) * 0.04, euler[FigBones.plume1].y, euler[FigBones.plume1].z);
            SetEuler(FigBones.plume2, plume * 0.8, euler[FigBones.plume2].y, euler[FigBones.plume2].z);
            SetEuler(FigBones.hair1, hair, euler[FigBones.hair1].y, euler[FigBones.hair1].z);
        }
        static void Spring(ref double x, ref double v, double tgt, double k, double d, double dt) { v += ((tgt - x) * k - v * d) * dt; x += v * dt; }

        // 直接设定右手兵器：pos（root 局部）、方向（单位向量）或兵器朝向四元数（root 局部）、横滚（度）
        public void Grip(V3d p, V3d? dir, Q4d? q, double roll)
        {
            P.v[FigPose.g] = p;
            if (q.HasValue) { var e = q.Value.ToEulerYXZ(); P.v[FigPose.w] = new V3d(e.y / D2R, e.x / D2R, e.z / D2R); }
            else if (dir.HasValue) { var d = dir.Value.Normalized(); P.v[FigPose.w] = new V3d(Math.Atan2(d.x, d.z) / D2R, Math.Acos(clamp(d.y, -1, 1)) / D2R, roll); }
            Arms(P);       // 与 JS 相同：不更新肩部辅助骨（下一次 SetPose 时更新）
        }
        // 双骨 IK：把 'R' / 'L' 手腕移到 root 局部坐标 p（q 为手的朝向，缺省保持当前）
        public void IkHand(string side, V3d p, Q4d? q)
        {
            Fk();
            string S = side == "L" ? "L" : "R";
            var hq = q ?? Wq[FigBones.B("hand" + S)];
            Ik(S, p, hq, S == "L" ? new V3d(0.8, -0.5, -0.3) : new V3d(-0.8, -0.5, -0.3), 0);
        }
        void SyncHelpers()
        {
            quat[FigBones.shL] = Q4d.Identity.Slerp(quat[FigBones.upperArmL], FigureData.SH_K);
            quat[FigBones.shR] = Q4d.Identity.Slerp(quat[FigBones.upperArmR], FigureData.SH_K);
            quat[FigBones.dlL] = Q4d.Identity.Slerp(quat[FigBones.upperArmL], FigureData.DL_K);
            quat[FigBones.dlR] = Q4d.Identity.Slerp(quat[FigBones.upperArmR], FigureData.DL_K);
        }
        public void SetWeapon(FigWeaponInfo wi, string st) { weaponInfo = wi; style = st; wRange = null; }
        public void SetWeaponRange(double y0, double y1) { wRange = new[] { y0, y1 }; }

        // 各骨骼在 mesh 空间（= 人物 root 空间）的矩阵（three 坐标；测试与烘焙用）
        public M4d[] BoneMatrices()
        {
            var r = new M4d[n];
            for (int i = 0; i < n; i++)
            {
                var L = M4d.Compose(pos[i], quat[i], new V3d(1, 1, 1));
                int j = FigBones.PARENT[i];
                r[i] = j < 0 ? L : M4d.Mul(r[j], L);
            }
            return r;
        }
    }

    // ------------------------------------------------------------------ 战马骨架（= JS class Horse 的姿势部分）
    public sealed class FigHorseRig
    {
        public readonly FigHorseTemplate tpl;
        public readonly int n;
        public readonly V3d[] pos, restPos, euler;
        public readonly Q4d[] quat;
        public readonly List<FigRig> riders = new List<FigRig>();
        public string clipName = "idle"; public double clipT;
        public static int Saddle { get { return FigBones.HB("saddle"); } }
        public FigHorseRig(FigHorseTemplate t)
        {
            tpl = t; n = FigBones.HBONES.Length;
            pos = new V3d[n]; restPos = new V3d[n]; euler = new V3d[n]; quat = new Q4d[n];
            for (int i = 0; i < n; i++)
            {
                int p = FigBones.HPARENT[i]; var J = t.J[i]; var PJ = p >= 0 ? t.J[p] : new V3d(0, 0, 0);
                pos[i] = restPos[i] = new V3d(J.x - PJ.x, J.y - PJ.y, J.z - PJ.z); quat[i] = Q4d.Identity;
            }
            SetPose("idle", 0);
        }
        public static FigClipInfo ClipInfo(string name)
        {
            var o = FigTables.HCLIPS.O(name ?? "");
            return o == null ? null : new FigClipInfo { name = name, dur = o.D("dur"), loop = o.T("loop"), desc = o.S("desc") };
        }
        // 返回本次实际使用的片段名（未知片段按 idle）；骑手的同步由调用方完成
        public string SetPose(string name, double t)
        {
            var c = ClipInfo(name) ?? ClipInfo("idle");
            double tt = c.loop ? ((t % c.dur) + c.dur) % c.dur : clamp(t, 0, c.dur);
            string nm = ClipInfo(name) != null ? name : "idle";
            HorsePose(nm, tt, c.dur);
            clipName = name; clipT = t;
            return nm;
        }
        void Set(string b, double x, double y = 0, double z = 0) { int i = FigBones.HB(b); euler[i] = new V3d(x * D2R, y * D2R, z * D2R); quat[i] = Q4d.Euler(euler[i].x, euler[i].y, euler[i].z, "XYZ"); }
        void Leg(string L, double ang, double knee, double fet) { Set("up" + L, ang); Set("lo" + L, knee); Set("ft" + L, fet); }
        void HorsePose(string name, double t, double dur)
        {
            double ph = t / dur * TAU;
            for (int i = 0; i < n; i++) { euler[i] = new V3d(0, 0, 0); quat[i] = Q4d.Identity; }
            int body = FigBones.HB("body"), root = FigBones.HB("root");
            pos[body] = restPos[body];
            pos[root] = new V3d(0, 0, 0);
            if (name == "idle")
            {
                double s = Math.Sin(ph);
                Set("neck1", -4 + s * 2); Set("neck2", 6 + s * 2); Set("head", 14 - s * 2); Set("tail1", 20, Math.Sin(ph * 2) * 10); Set("tail2", 15, Math.Sin(ph * 2 + 1) * 12);
                pos[body].y += Math.Sin(ph * 2) * 0.004;
                Leg("FL", 0, 0, 0); Leg("FR", 0, 0, 0); Leg("HL", 0, 0, 0); Leg("HR", 4, -6, 4);
                return;
            }
            if (name == "walk" || name == "trot" || name == "gallop")
            {
                bool g = name == "gallop", tr = name == "trot";
                var off = g ? new Dictionary<string, double> { { "HL", 0 }, { "HR", 0.1 }, { "FR", 0.42 }, { "FL", 0.52 } } : tr ? new Dictionary<string, double> { { "FL", 0 }, { "HR", 0 }, { "FR", 0.5 }, { "HL", 0.5 } } : new Dictionary<string, double> { { "HL", 0 }, { "FL", 0.25 }, { "HR", 0.5 }, { "FR", 0.75 } };
                double A = g ? 42 : tr ? 26 : 18;
                foreach (var L in new[] { "FL", "FR", "HL", "HR" })
                {
                    double p = ph - off[L] * TAU, s = Math.Sin(p), c = Math.Cos(p);
                    double lift = Math.Max(0, c);
                    if (L[0] == 'F') Leg(L, -s * A + (g ? -6 : 0), lift * (g ? 95 : tr ? 70 : 45), -lift * (g ? 30 : 20) + 5);
                    else Leg(L, -s * A * 0.9 + (g ? 6 : 0), -lift * (g ? 60 : tr ? 40 : 25), lift * (g ? 50 : 30));
                }
                double bob = g ? 0.06 : tr ? 0.035 : 0.015;
                pos[body].y += Math.Abs(Math.Sin(ph * (g ? 1 : 2))) * bob - bob * 0.5;
                Set("body", g ? Math.Sin(ph) * 5 : 0);
                Set("neck1", g ? -10 + Math.Sin(ph + 1) * 8 : -6 + Math.Sin(ph * 2) * 3); Set("neck2", g ? 4 : 6); Set("head", g ? 10 : 14 + Math.Sin(ph * 2) * 3);
                Set("tail1", g ? 45 : 22, Math.Sin(ph) * 8); Set("tail2", g ? 25 : 18, Math.Sin(ph + 1) * 10);
                return;
            }
            if (name == "rear")
            {
                double k = sstep(0, 0.45, t) * (1 - sstep(1.15, 1.6, t));
                euler[root] = new V3d(-k * 42 * D2R, 0, 0); quat[root] = Q4d.Euler(euler[root].x, 0, 0, "XYZ");
                pos[root] = new V3d(0, k * 0.25, -k * 0.5);
                Leg("FL", -k * 60, k * 110, -k * 30); Leg("FR", -k * 40, k * 95, -k * 30);
                Leg("HL", k * 38, -k * 20, k * 10); Leg("HR", k * 30, -k * 15, k * 10);
                Set("neck1", -k * 20); Set("head", k * 30); Set("tail1", 10 + k * 30);
                return;
            }
            if (name == "die")
            {
                double k = sstep(0.2, 1.1, t), kk = sstep(0, 0.4, t);
                euler[root] = new V3d(0, 0, k * 82 * D2R); quat[root] = Q4d.Euler(0, 0, euler[root].z, "XYZ");
                pos[root] = new V3d(-k * 0.25, k * 0.05, 0);
                Leg("FL", -kk * 30 + k * 20, kk * 90 - k * 40, 0); Leg("FR", -kk * 20, kk * 60, 0); Leg("HL", kk * 20, -kk * 30, 0); Leg("HR", k * 10, -k * 10, 0);
                Set("neck1", k * 30, 0, -k * 20); Set("head", k * 20); Set("tail1", 10);
            }
        }
        // 骑手的 root 在鞍座骨上的局部位置（= JS mount：fig.root.position = (0, −J.pelvis.y + 0.08, −0.04)，旋转为零）
        public static V3d RiderOffset(FigRig f) { return new V3d(0, -f.pr.J[FigBones.pelvis].y + 0.08, -0.04); }
        public M4d[] BoneMatrices()
        {
            var r = new M4d[n];
            for (int i = 0; i < n; i++)
            {
                var L = M4d.Compose(pos[i], quat[i], new V3d(1, 1, 1));
                int j = FigBones.HPARENT[i];
                r[i] = j < 0 ? L : M4d.Mul(r[j], L);
            }
            return r;
        }
    }

    // ------------------------------------------------------------------ 士兵群的动作烘焙（= JS bakeFramesGen；纯计算，结果为 Unity 坐标的蒙皮矩阵）
    public sealed class FigBakeEntry { public int row, n; public double dur; public bool loop; }
    public sealed class FigBakeData
    {
        public int W, rows, bones;
        public float[] data;                    // rows × W × 4（每骨 3 个 RGBA 像素 = 3×4 矩阵的三行）
        public Dictionary<string, FigBakeEntry> table = new Dictionary<string, FigBakeEntry>();
        public double ms;
        public object unity;                    // Unity 端的烘焙贴图
    }
    public static class FigBake
    {
        public const int FPS = FigureData.BAKE_FPS;
        public sealed class ClipSpec { public string name; public double dur; public bool loop; }
        // poser(clip, t) 摆好姿势后，mats() 给出各骨骼在容器根空间的矩阵（three 坐标），inv 为绑定逆矩阵
        public static IEnumerable<object> Frames(List<ClipSpec> list, int nb, M4d[] inv, Action<string, double> poser, Func<M4d[]> mats, FigBakeData outp)
        {
            int rows = 0;
            foreach (var c in list) { int nn = Math.Max(2, (int)jround(c.dur * FPS) + (c.loop ? 0 : 1)); outp.table[c.name] = new FigBakeEntry { row = rows, n = nn, dur = c.dur, loop = c.loop }; rows += nn + (c.loop ? 1 : 0); }
            int W = nb * 3; var data = new float[W * rows * 4];
            outp.W = W; outp.rows = rows; outp.bones = nb; outp.data = data;
            foreach (var c in list)
            {
                var e = outp.table[c.name];
                for (int f = 0; f < e.n + (c.loop ? 1 : 0); f++)
                {
                    double t = c.loop ? (double)(f % e.n) / e.n * c.dur : Math.Min(c.dur, (double)f / (e.n - 1) * c.dur);
                    poser(c.name, t);
                    var M = mats();
                    for (int i = 0; i < nb; i++)
                    {
                        // Unity 坐标：S·(M·inv)·S（顶点已 x 镜像）
                        var m = FigConv.M(M4d.Mul(M[i], inv[i])).e;
                        int o = ((e.row + f) * W + i * 3) * 4;
                        data[o] = (float)m[0]; data[o + 1] = (float)m[4]; data[o + 2] = (float)m[8]; data[o + 3] = (float)m[12];
                        data[o + 4] = (float)m[1]; data[o + 5] = (float)m[5]; data[o + 6] = (float)m[9]; data[o + 7] = (float)m[13];
                        data[o + 8] = (float)m[2]; data[o + 9] = (float)m[6]; data[o + 10] = (float)m[10]; data[o + 11] = (float)m[14];
                    }
                    if (f % 10 == 9) yield return null;
                }
                yield return null;
            }
        }
        // ---- 士兵群的两种烘焙（= JS bakeSoldierGen / bakeMountedGen 的计算部分）
        public static List<ClipSpec> CrowdClips()
        {
            var l = new List<ClipSpec>();
            foreach (var o in FigTables.CROWD_CLIPS) { var c = FigClips.Info((string)o); l.Add(new ClipSpec { name = c.name, dur = c.dur, loop = c.loop }); }
            return l;
        }
        // 骑兵组合：名称 → [战马片段, 骑手片段, 时长, 循环, (骑手固定时刻)]
        public static List<ClipSpec> MountClips()
        {
            var l = new List<ClipSpec>();
            foreach (var kv in FigTables.MOUNT_CLIPS) { var a = (List<object>)kv.Value; l.Add(new ClipSpec { name = kv.Key, dur = Convert.ToDouble(a[2]), loop = (bool)a[3] }); }
            return l;
        }
        // 步兵：容器根 = 人物 root
        public static IEnumerable<object> Soldier(FigTemplate tpl, FigBakeData outp)
        {
            var f = new FigRig(tpl);
            foreach (var o in Frames(CrowdClips(), f.n, tpl.boneInverses, (c, t) => f.SetPose(c, t, null), f.BoneMatrices, outp)) yield return o;
        }
        // 骑兵：骑手与战马各一份，都在战马 root 坐标中烘焙（骑手随马背起伏）
        public static IEnumerable<object> Mounted(FigTemplate tpl, FigHorseTemplate ht, FigBakeData rider, FigBakeData horse)
        {
            var f = new FigRig(tpl); var h = new FigHorseRig(ht);
            f.mounted = h; h.riders.Add(f);
            f.SetPose(f.hasClip ? f.clipName : "idle", f.hasClip ? f.clipT : 0, null);
            // 循环组合的时长取各循环周期的整数倍：战马片段直接用 t；骑手上半身 / 下半身步态按整周期缩放，首尾无缝
            Func<double, double, double, double> fit = (t, D, d) => t / D * d * Math.Max(1, jround(D / d));
            Action<string, double> pose = (c, t) =>
            {
                var M = (List<object>)FigTables.MOUNT_CLIPS[c];
                string hc = (string)M[0], rcn = (string)M[1]; double D = Convert.ToDouble(M[2]); bool loop = (bool)M[3];
                h.SetPose(hc, loop ? t : Math.Min(t, FigHorseRig.ClipInfo(hc).dur));
                bool gal = hc == "gallop"; double gd = FigClips.Info(gal ? "gallop" : "ride").dur;
                var rc = FigClips.Info(rcn);
                double rt = M.Count > 4 ? Convert.ToDouble(M[4]) : rc.loop ? fit(t, D, rc.dur) : Math.Min(rc.dur, t / D * rc.dur);
                f.SetPose(rcn, rt, new FigPoseOpts { gallop = gal, lowerT = fit(t, D, gd) });
            };
            Func<M4d[]> riderMats = () =>
            {
                var hm = h.BoneMatrices();
                var S = M4d.Mul(hm[FigHorseRig.Saddle], M4d.Compose(FigHorseRig.RiderOffset(f), Q4d.Identity, new V3d(1, 1, 1)));
                var r = f.BoneMatrices();
                for (int i = 0; i < r.Length; i++) r[i] = M4d.Mul(S, r[i]);
                return r;
            };
            var list = MountClips();
            foreach (var o in Frames(list, f.n, tpl.boneInverses, pose, riderMats, rider)) yield return o;
            foreach (var o in Frames(list, h.n, ht.inv, pose, h.BoneMatrices, horse)) yield return o;
        }
        // 半精度数据（贴图上传用；= JS 的 Uint16Array）
        public static ushort[] Halves(FigBakeData b)
        {
            var r = new ushort[b.data.Length];
            for (int i = 0; i < r.Length; i++) r[i] = ToHalf(b.data[i]);
            return r;
        }

        // three DataUtils.toHalfFloat（float32 → half，舍入同 three）
        static readonly uint[] baseTable = new uint[512], shiftTable = new uint[512];
        static FigBake()
        {
            for (int i = 0; i < 256; ++i)
            {
                int e = i - 127;
                if (e < -27) { baseTable[i] = 0x0000; baseTable[i | 0x100] = 0x8000; shiftTable[i] = 24; shiftTable[i | 0x100] = 24; }
                else if (e < -14) { baseTable[i] = (uint)(0x0400 >> (-e - 14)); baseTable[i | 0x100] = (uint)((0x0400 >> (-e - 14)) | 0x8000); shiftTable[i] = (uint)(-e - 1); shiftTable[i | 0x100] = (uint)(-e - 1); }
                else if (e <= 15) { baseTable[i] = (uint)((e + 15) << 10); baseTable[i | 0x100] = (uint)(((e + 15) << 10) | 0x8000); shiftTable[i] = 13; shiftTable[i | 0x100] = 13; }
                else if (e < 128) { baseTable[i] = 0x7c00; baseTable[i | 0x100] = 0xfc00; shiftTable[i] = 24; shiftTable[i | 0x100] = 24; }
                else { baseTable[i] = 0x7c00; baseTable[i | 0x100] = 0xfc00; shiftTable[i] = 13; shiftTable[i | 0x100] = 13; }
            }
        }
        public static ushort ToHalf(float v)
        {
            if (Math.Abs(v) > 65504) v = v > 0 ? 65504 : -65504;
            if (v < -65504) v = -65504; if (v > 65504) v = 65504;
            uint f = BitConverter.ToUInt32(BitConverter.GetBytes(v), 0);
            uint e = (f >> 23) & 0x1ff;
            return (ushort)(baseTable[e] + ((f & 0x007fffff) >> (int)shiftTable[e]));
        }
        public static float FromHalf(ushort h)
        {
            int s = (h >> 15) & 1, e = (h >> 10) & 31, m = h & 1023;
            double v = e == 0 ? m / 1024.0 * Math.Pow(2, -14) : e == 31 ? (m == 0 ? double.PositiveInfinity : double.NaN) : (1 + m / 1024.0) * Math.Pow(2, e - 15);
            return (float)(s == 1 ? -v : v);
        }
    }
}
