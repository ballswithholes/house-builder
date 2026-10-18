// ==========================================================================
// 三国志-霸王的世界 · 单挑格斗 · 武将（网页版 js/duel-game.js「视图：武将（第三版 · js/figure.js 高精度模型）」FigWarrior 的移植）
//
// 每名武将 = Figure.General(…, lod "high")（View/Figure.cs：平滑蒙皮网格、按头像外貌生成的面部、层叠衣甲、披风、专属兵器）。
// 动作取人物模型库的程序化片段（idle / walk / light1–3 / heavy / block / parry / hit / hitB / knockdown / down / getup /
// guardbreak / victory / defeat / jump / dash / bowDraw / bowShoot …），由单挑自己的时钟与状态机驱动：
//   出招按 DuelSim 招式的 起手 / 判定 / 收招 三段映射到片段时间，判定开始的一刻 = 片段里兵器尖端伸得最远（「打到」）的一刻；
//   状态切换时按骨骼四元数交叉淡化（BLEND 表的时长），任何姿势之间都不跳变。
// 兵器长度对齐规则判定距离：构建时逐片段测量尖端在单挑画面里的前伸距离，按 武将.reach × reachK 缩放兵器
// （长兵 0.9–1.15 倍，短兵 1–1.5 倍，真三国无双式的大兵器），仍够不到的部分由出招时的前冲步补足（按与对手的实际距离，
// 只冲到刃口刚好碰到对手为止；起手期间按包络确定性地迈步，判定开始的一帧必定到位）。对手比兵器全伸更近时，
// 「打到」的一刻改取挥击段里尖端第一次越过对手中线的时刻（ContactT），定格时长兵不会整片穿出对手背后。
// 受击闪光、格开架势的冷光、反击窗口的金光、蓄力 / 绝技的发光都叠加为人物的辉光（Figure.SetTint：以轮廓为主）。
//
// 每帧顺序（= 网页版 FigWarrior.update；见 Figure.cs）：SetPose → 改骨骼 Transform（交叉淡化、脊柱后仰、前臂抖动）
//   → Figure.Update(定格 ? 0 : dt)（只写 6 根弹簧骨）→ 披风下垂（HangCape，有披风时）。
// 坐标：单挑世界 = 网页版 three 坐标 z 取反（Unity (x, y, −z)）；人物模型库的局部坐标是另一种换算（FigUnity.V：(−x, y, z)），
//   所以挂在单挑世界里的模型库物体（人物根、插地的兵器）的旋转 = DuelXf.Q(three 旋转) × 绕 Y 180°（人物根即 Figure.YawFromJs）。
//   tipW / baseW / chestW 为 Unity 世界坐标（x、y 与网页版相同）。
// 另含 DuelXf：three 坐标 → Unity 的换算与颜色工具（原 DuelModels.cs；低多边形的旧模型与旧动画由本文件取代后删除）。
// ==========================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    // three 坐标 → Unity 的换算与欧拉角（three 的弧度、指定顺序），颜色工具
    public static class DuelXf
    {
        public const string STEEL = "#cdd3da", EDGE = "#eef2f6", GOLD = "#c9a24e", RED = "#c8382c";
        public static Vector3 P(Vector3 t) { return new Vector3(t.x, t.y, -t.z); }
        public static Vector3 P(float x, float y, float z) { return new Vector3(x, y, -z); }
        public static Quaternion Q(Quaternion t) { return new Quaternion(-t.x, -t.y, t.z, t.w); }
        const float R2D = Mathf.Rad2Deg;
        static Quaternion AX(float r) { return Quaternion.AngleAxis(r * R2D, Vector3.right); }
        static Quaternion AY(float r) { return Quaternion.AngleAxis(r * R2D, Vector3.up); }
        static Quaternion AZ(float r) { return Quaternion.AngleAxis(r * R2D, Vector3.forward); }
        // three 的 Euler（XYZ 顺序 = Rx·Ry·Rz）在 three 坐标下的四元数
        public static Quaternion EulerXYZ(float x, float y, float z) { return AX(x) * AY(y) * AZ(z); }
        // three 的 Euler（YZX 顺序 = Ry·Rz·Rx）
        public static Quaternion EulerYZX(float x, float y, float z) { return AY(y) * AZ(z) * AX(x); }
        public static void Pos(Transform tr, Vector3 three) { tr.localPosition = P(three); }
        public static void Pos(Transform tr, float x, float y, float z) { tr.localPosition = new Vector3(x, y, -z); }
        public static void Rot(Transform tr, Quaternion three) { tr.localRotation = Q(three); }
        public static void RotXYZ(Transform tr, float x, float y, float z) { tr.localRotation = Q(EulerXYZ(x, y, z)); }
        public static void RotYZX(Transform tr, float x, float y, float z) { tr.localRotation = Q(EulerYZX(x, y, z)); }
        // 模型库物体（FigUnity 坐标）放进单挑世界时的旋转：three 旋转换手系后再绕 Y 转 180°
        public static Quaternion FigQ(Quaternion three) { return Q(three) * Quaternion.AngleAxis(180, Vector3.up); }

        static readonly Dictionary<string, Color> cache = new Dictionary<string, Color>();
        public static Color C(string hex)
        {
            if (hex == null) return Color.white;
            Color c;
            if (cache.TryGetValue(hex, out c)) return c;
            if (!ColorUtility.TryParseHtmlString(hex, out c)) c = Color.white;
            c.a = 1;
            cache[hex] = c;
            return c;
        }
        // = SG.Gfx.shade / lerpColor
        public static Color Sh(Color c, float k) { var s = Art.Shade(c, k); s.a = 1; return s; }
        public static Color Sh(string c, float k) { return Sh(C(c), k); }
        public static Color Mix(Color a, Color b, float t) { var m = Color.Lerp(a, b, t); m.a = 1; return m; }
        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant(); }
    }

    // 单挑的一名武将（= 网页版 FigWarrior）
    public sealed class DuelFigure
    {
        public const float TURN = 0.42f;          // 3/4 侧身角度（向镜头转）
        // 规则兵器 → 模型库兵器
        static readonly Dictionary<string, string> FIG_WEAPON = new Dictionary<string, string>
        {
            { "guandao", "guandao" }, { "snake", "snake" }, { "ji", "halberd" }, { "spear", "spear" }, { "poleblade", "poleblade" }, { "axe", "axe" }, { "bigblade", "bigblade" },
            { "sword", "jian" }, { "dao", "dao" }, { "shortji", "dao" }, { "mace", "mace" }, { "twinsword", "swords" }, { "twinji", "twinji" }, { "twindao", "twindao" },
            { "gladius", "gladius" }, { "lance", "lance" }, { "handaxe", "handaxe" }, { "tachi", "tachi" },
        };
        // 西方 / 草原 / 南亚文化的「长剑」用 spatha 造型
        static readonly HashSet<string> FIG_SPATHA = new HashSet<string> { "roman", "celt", "german", "persia", "arab", "kushan", "sarmatian", "africa", "moor" };
        static readonly Dictionary<string, string> FIG_SHIELD = new Dictionary<string, string>
        {
            { "wood", "wa" }, { "round", "round" }, { "scutum", "scutum" }, { "oval", "oval" }, { "pai", "pai" }, { "rattan", "rattan" }, { "long", "long" }, { "hide", "hide" },
        };
        // 出招片段的挥击段 [挥起, 劈到]（模型库关键帧时刻）；判定开始的一刻在这一段里取尖端前伸最远处
        static readonly string[] SWING_CLIPS = { "light1", "light2", "light3", "heavy" };
        static readonly Dictionary<string, double[]> FIG_SWING = new Dictionary<string, double[]>
        {
            { "light1", new[] { 0.08, 0.2 } }, { "light2", new[] { 0.08, 0.21 } }, { "light3", new[] { 0.12, 0.26 } }, { "heavy", new[] { 0.42, 0.56 } },
        };
        static readonly HashSet<string> FIG_SEC = new HashSet<string> { "cape1", "cape2", "cape3", "plume1", "plume2", "hair1" };   // 弹簧随动的骨（不参与交叉淡化）
        // 前冲步的上限：一般兵器 ≤ 0.55 米；短兵（日耳曼战斧等）按需放宽到 0.9 米，保证判定距离处刃口真的够到
        public const double FIG_LUNGE_MAX = 0.55, FIG_LUNGE_CAP = 0.9;
        public const double FIG_BITE = 0.04;      // 近身时「打到」的姿势：尖端刚越过对手身体中线这么多（米）
        // 状态切换的交叉淡化时长（= 网页版 BLEND）
        static readonly Dictionary<string, double> BLEND = new Dictionary<string, double>
        {
            { "idle", 0.14 }, { "walk", 0.12 }, { "guard", 0.06 }, { "guardstun", 0.03 }, { "hitstun", 0.035 }, { "knockdown", 0.07 }, { "down", 0.1 }, { "getup", 0.08 },
            { "victory", 0.2 }, { "defeat", 0.2 }, { "charge", 0.1 }, { "dash", 0.06 }, { "jump", 0.08 }, { "land", 0.04 },
            { "parry", 0.025 }, { "parryok", 0.02 }, { "parried", 0.02 }, { "parrywhiff", 0.05 }, { "backstep", 0.05 },
        };
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Ease2(double t) { return t * t * (3 - 2 * t); }
        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double Clamp01(double v) { return Clamp(v, 0, 1); }

        // 无头测试替换（头像模块的 Unity 部分不参与无头编译）
#if SANGUO_HEADLESS
        public static Func<General, PortraitGen> Describe = g =>
        {
            var pg = new PortraitGen { name = g.name, war = g.war, intel = g.intel, pol = g.pol, culture = g.culture };
            if (g.born.HasValue && g.born.Value > 0) pg.born = g.born.Value;
            pg.sex = g.sex ?? (g.female ? "f" : null);
            return pg;
        };
#else
        public static Func<General, PortraitGen> Describe = g => Portrait.Describe(g);
#endif

        // 兵器：规则的兵器种类决定流派与判定距离；名将若有同流派的专属兵器（青龙偃月刀、方天画戟、涯角枪……）则用专属造型。
        // 规则的「短戟」是单手兵器：模型库没有单支短戟，一般武将用单手环首刀造型（流派一致）；
        // 唯一的例外是 太史慈 的专属「双短戟」（模型库的 sig 兵器，双持造型，规则仍按单手计时与判定）
        public static string WeaponOf(DuelLook look, General gen)
        {
            DuelWeapon wd;
            if (look.weapon == null || !DuelLooks.Weapons.TryGetValue(look.weapon, out wd)) DuelLooks.Weapons.TryGetValue("sword", out wd);
            string want = wd != null ? wd.style : "one";
            string w;
            if (look.weapon == null || !FIG_WEAPON.TryGetValue(look.weapon, out w)) w = "jian";
            if (w == "jian" && look.culture != null && FIG_SPATHA.Contains(look.culture)) w = "spatha";
            if (w == "jian" && look.culture == "india") w = "khanda";   // 天竺的长剑是坎达剑
            try
            {
                var fl = gen != null ? Figure.LookOf(Describe(gen), new FigOpts { culture = look.culture }) : null;
                string sig = fl != null && fl.sig != null ? fl.sig.S("weapon") : null;
                if (sig != null && FigBuild.HasWeapon(sig))
                {
                    string st = FigBuild.WeaponStyle(sig);
                    if (st == want || (want == "one" && st == "dual" && look.weapon == "shortji")) w = sig;
                }
            }
            catch (Exception) { /* 用缺省 */ }
            return w;
        }

        // 出招的测量值（= 网页版 cal[clip]）
        public sealed class Cal
        {
            public double tc, te, dur, tip, grip, target, lunge;
            public int n; public float[] T, X, Y;       // 起手到「打到」逐帧的尖端前伸（缩放后），供近身时找刚好打到的一刻
            public List<double[]> raw;
            public Cal Copy() { return (Cal)MemberwiseClone(); }
        }
        struct Pick
        {
            public string clip, key; public double t, env; public FigPoseOpts opts; public Cal cal;
            public Pick(string clip, double t, string key, FigPoseOpts opts = null, double env = 0, Cal cal = null) { this.clip = clip; this.t = t; this.key = key; this.opts = opts; this.env = env; this.cal = cal; }
        }

        public readonly DuelFighter f;
        public readonly DuelLook look;
        public readonly string style;             // 规则的流派（one / dual / pole）
        public readonly string figWeapon;          // 模型库兵器
        public readonly string fstyle;             // 模型库兵器的流派
        public readonly Figure fig;
        public readonly GameObject root;
        public readonly bool wpn2;                 // 双持（两道刀光）
        readonly int layer;
        readonly string[] boneNames;
        readonly Transform[] bones;
        readonly Quaternion[] snapQ;
        readonly Vector3[] snapP;
        readonly bool[] blendMask;
        string key = ""; double blend = 1, blendDur = 0.1;
        double walkPhase, time, lungeX, turn = TURN;
        string tcKey, lungeKey; double tcHit; bool lungeLock;
        double capeMax = double.NaN;
        public float flash; Color flashColor = Color.white;
        static readonly Color parryCol = DuelXf.C("#bfe6ff"), ctrCol = DuelXf.C("#ffc850"), emCol = DuelXf.C("#ffcf8a");
        public double ctr;                         // 反击窗口余下的秒数（DuelScreen 每帧设）
        public double intro = -1;                  // ≥0：开场亮相动画计时
        public readonly Vector3[] tipW = new Vector3[2], baseW = new Vector3[2];
        public Vector3 chestW;
        public DuelFighter opp;
        public double wScale = 1, lungeMax = FIG_LUNGE_MAX;
        public Dictionary<string, Cal> cal;
        public Color tint;                         // 本帧的辉光（测试用）
        // 弓将（黄忠、夏侯渊）：绝技时换弓连射，主兵器插在身旁地上
        public bool bowOn;
        string bowName;
        GameObject planted, nocked;
        LineRenderer bowString;
        Material stringMat;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        // 只读状态（测试与调试用）
        public double Clock { get { return time; } set { time = value; } }   // 视图时钟（待机呼吸、辉光脉动）
        public string Key { get { return key; } }
        public double LungeX { get { return lungeX; } }
        public double Turn { get { return turn; } }
        public bool HasBow { get { return planted != null; } }
        public string BowName { get { return bowName; } }

        public DuelFigure(DuelFighter f, Color team, Transform parent, int layer, Material env)
        {
            this.f = f; look = f.look; style = f.wpn.style; this.layer = layer;
            figWeapon = WeaponOf(f.look, f.gen);
            string wst = FigBuild.HasWeapon(figWeapon) ? FigBuild.WeaponStyle(figWeapon) : null;
            string shield = null;
            if (style == "one" && !string.IsNullOrEmpty(f.look.shield) && (wst == null || wst == "one"))
                shield = FIG_SHIELD.TryGetValue(f.look.shield, out shield) ? shield : "round";
            var gen = f.gen ?? new General { name = "?" };
            fig = Figure.General(Describe(gen), new FigOpts { culture = f.look.culture, color = DuelXf.Hex(team), lod = "high", weapon = figWeapon, shield = shield });
            fstyle = fig.Style;
            fig.SetTint("#000000", 0);
            // （网页版此处给头部换一个不带贴图的深度材质，防止 three.js 的共用阴影材质记住已释放的面部贴图；Unity 没有这个问题）
            root = new GameObject("Duelist_" + (f.gen != null ? f.gen.name : "?"));
            root.layer = layer;
            root.transform.SetParent(parent, false);
            fig.Root.SetParent(root.transform, false);
            fig.Root.localRotation = Quaternion.Euler(0, Figure.YawFromJs(Math.PI / 2 - TURN), 0);   // 面朝 +Z 的模型 → 面向对手并向镜头侧转 3/4
            SetLayer(fig.gameObject);
            boneNames = Figure.BoneNames;
            bones = new Transform[boneNames.Length];
            snapQ = new Quaternion[boneNames.Length]; snapP = new Vector3[boneNames.Length]; blendMask = new bool[boneNames.Length];
            for (int i = 0; i < boneNames.Length; i++)
            {
                bones[i] = fig.Bones[boneNames[i]];
                snapQ[i] = bones[i].localRotation; snapP[i] = bones[i].localPosition;
                blendMask[i] = !FIG_SEC.Contains(boneNames[i]);
            }
            time = f.idx * 1.7;
            wpn2 = fig.WeaponMesh2 != null;
            Calibrate();
            if (f.bow) BuildBowKit(team, parent, env);
            Update(0, false);
        }

        void SetLayer(GameObject go)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject);
        }

        // ---------------------------------------------------------- 兵器长度与判定距离对齐 --
        void Calibrate()
        {
            var rt = root.transform;
            var wi = fig.WeaponInfo;
            V3d tipL = wi != null ? wi.tip : new V3d(0, 1, 0);
            Vector3 tipU = FigUnity.V(tipL);
            var c0 = new Dictionary<string, Cal>();
            foreach (var clip in SWING_CLIPS)
            {
                var sw = FIG_SWING[clip]; var info = fig.ClipInfo(clip);
                double dur = info != null ? info.dur : 0.6;
                double best = -9, bt = sw[1], gx = 0;
                var samp = new List<double[]>();     // 起手到挥击段逐帧：[t, 尖端 x, y, 握点 x, y]×两件兵器（未缩放）
                for (double t = clip == "heavy" ? sw[0] : 0; t <= sw[1] + 1e-6; t += 0.005)
                {
                    fig.SetPose(clip, t);
                    var ms = new[] { fig.WeaponMesh, fig.WeaponMesh2 };
                    double a = -9, b = -9;
                    var row = new double[9]; row[0] = t;
                    for (int k = 0; k < 2; k++)
                    {
                        var mm = ms[k];
                        if (mm == null) { row[1 + k * 4] = -9; row[2 + k * 4] = 9; row[3 + k * 4] = 0; row[4 + k * 4] = 0; continue; }
                        var tp = rt.InverseTransformPoint(mm.transform.TransformPoint(tipU));
                        var gp = rt.InverseTransformPoint(mm.transform.position);
                        row[1 + k * 4] = tp.x; row[2 + k * 4] = tp.y; row[3 + k * 4] = gp.x; row[4 + k * 4] = gp.y;
                        if (k == 0) a = tp.x; else b = tp.x;
                    }
                    int mi = a >= b ? 0 : 1; double x = Math.Max(a, b);
                    if (t >= sw[0] - 1e-6 && x > best + 1e-4) { best = x; bt = t; gx = ms[mi] != null ? row[3 + mi * 4] : 0; }
                    samp.Add(row);
                }
                c0[clip] = new Cal { tc = bt, te = Math.Max(sw[1], bt + 0.02), dur = dur, tip = best, grip = gx, target = f.reach * DuelSim.Mv(clip).reachK, raw = samp };
            }
            // 兵器缩放：使各招的尖端前伸接近判定距离（取平均，限制在合理范围）
            double sum = 0; int n = 0;
            foreach (var clip in SWING_CLIPS) { var c = c0[clip]; double arm = c.tip - c.grip; if (arm > 0.15) { sum += (c.target - c.grip) / arm; n++; } }
            // 长兵 0.9–1.15 倍；短兵 1 倍起，上限按兵器本身长短（短剑可放大到 1.5 倍，长剑 1.3 倍：真三国无双式的大兵器）
            bool pole = fstyle == "pole";
            double len = wi != null && wi.len > 0 ? wi.len : 0.9;
            double s = Clamp(n > 0 ? sum / n : 1, pole ? 0.9 : 1, pole ? 1.15 : Clamp(1.12 / len, 1.3, 1.5));
            wScale = s;
            ScaleWeapons();
            // 前冲上限：短兵放大到上限仍不够长时，按需放宽（最多 FIG_LUNGE_CAP）
            double need = 0;
            foreach (var clip in SWING_CLIPS) { var c = c0[clip]; need = Math.Max(need, c.target - (c.grip + (c.tip - c.grip) * s)); }
            double lmax = lungeMax = Clamp(need, FIG_LUNGE_MAX, FIG_LUNGE_CAP);
            foreach (var clip in SWING_CLIPS)
            {
                var c = c0[clip];
                c.tip = c.grip + (c.tip - c.grip) * s;
                c.lunge = Clamp(c.target - c.tip, 0, lmax);   // 判定距离处仍够不到的部分：出招时前冲补足
                // 逐帧尖端前伸（缩放后，只留到最远处为止），供近身时找「刚好打到」的一刻（网页版 Float32Array：单精度）
                int cnt = c.raw.FindIndex(r => r[0] >= c.tc - 1e-6) + 1;
                if (cnt == 0) cnt = c.raw.Count;
                c.n = cnt; c.T = new float[cnt]; c.X = new float[cnt]; c.Y = new float[cnt];
                for (int i = 0; i < cnt; i++)
                {
                    var r = c.raw[i];
                    c.T[i] = (float)r[0];
                    double bx = -9, by = 9;
                    for (int k2 = 0; k2 < 2; k2++)
                    {
                        int o = 1 + k2 * 4;
                        double x = r[o + 2] + (r[o] - r[o + 2]) * s, y = r[o + 3] + (r[o + 1] - r[o + 3]) * s;
                        if (r[o] > -8 && x > bx) { bx = x; by = y; }
                    }
                    c.X[i] = (float)bx; c.Y[i] = (float)by;
                }
            }
            var air = c0["heavy"].Copy();
            air.target = f.reach * DuelSim.Air.reachK;
            air.lunge = Clamp(air.target - c0["heavy"].tip, 0, lmax);
            c0["air"] = air;
            cal = c0;
            fig.SetPose("idle", 0);
        }
        void ScaleWeapons()
        {
            foreach (var m in new[] { fig.WeaponMesh, fig.WeaponMesh2 }) if (m != null) m.transform.localScale = Vector3.one * (float)wScale;
        }

        // ---------------------------------------------------------- 弓与弦（绝技「连珠箭」） --
        void BuildBowKit(Color team, Transform world, Material env)
        {
            bowName = f.gen != null && f.gen.name == "黄忠" ? "huangbow" : "bow";
            // 插在地上的主兵器（独立网格）
            planted = Figure.Weapon(figWeapon, new FigOpts { color = DuelXf.Hex(team) });
            planted.SetActive(false);
            planted.transform.SetParent(root.transform, false);
            planted.transform.localScale = Vector3.one * (float)wScale;
            SetLayer(planted);
            // 拉开的弓弦（折线，世界坐标随弓与右手）与搭在弦上的箭：放在场景根下
            var sgo = new GameObject("BowString"); sgo.layer = layer; sgo.transform.SetParent(world, false);
            bowString = sgo.AddComponent<LineRenderer>();
            bowString.useWorldSpace = true; bowString.positionCount = 3;
            bowString.startWidth = bowString.endWidth = 0.006f;     // three 的 Line 恒为 1 像素：单挑镜头距离下约 6 毫米
            stringMat = Art.NewUnlit(DuelXf.C("#f0e8d0")); owned.Add(stringMat);
            bowString.sharedMaterial = stringMat;
            bowString.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; bowString.receiveShadows = false;
            bowString.enabled = false;
            var am = DuelTextures.Arrow(false); owned.Add(am);
            nocked = Art.MakeObject("Nocked", am, env, world, true);
            nocked.layer = layer;
            nocked.SetActive(false);
        }
        void SetBow(bool on)
        {
            if (on == bowOn || planted == null) return;
            bowOn = on;
            fig.SetWeapon(on ? bowName : figWeapon);
            SetLayer(fig.gameObject);
            if (!on) ScaleWeapons();
            planted.SetActive(on);
            if (on)
            {
                // 长兵刃朝上斜插、短兵插地（在身后偏镜头一侧，不挡住开弓）
                bool pole = fstyle == "pole";
                DuelXf.Pos(planted.transform, -0.42f, pole ? -0.25f : -0.18f, 0.22f);
                planted.transform.localRotation = DuelXf.FigQ(DuelXf.EulerXYZ(pole ? 0.12f : Mathf.PI - 0.12f, 0, pole ? -0.16f : 0.16f));
            }
            bowString.enabled = false; nocked.SetActive(false);
        }
        void UpdateBowString()
        {
            if (!bowOn) return;
            var wi = fig.WeaponInfo; var m = fig.WeaponMesh;
            if (m == null || wi == null || !wi.nockTop.HasValue || !wi.nockBot.HasValue) return;
            Vector3 top = m.transform.TransformPoint(FigUnity.V(wi.nockTop.Value)), bot = m.transform.TransformPoint(FigUnity.V(wi.nockBot.Value));
            Vector3 H = fig.Sockets["gripR"].position;
            Vector3 mid = (top + bot) * 0.5f;
            // 右手离弓把足够远且与弓同高附近 → 视为拉弦
            float d = Vector3.Distance(H, mid);
            bool drawn = d > 0.2f && d < 0.95f;
            Vector3 N = drawn ? H : mid;
            bowString.SetPosition(0, top); bowString.SetPosition(1, N); bowString.SetPosition(2, bot);
            bowString.enabled = true;
            nocked.SetActive(drawn);
            if (drawn)
            {
                nocked.transform.position = H;
                nocked.transform.rotation = Quaternion.FromToRotation(Vector3.right, (mid - H).normalized);
            }
        }

        // 弓将绝技：插刀 → 开弓 → 四连射 → 收弓
        Pick BowSpecialPose(double t)
        {
            var sp = DuelSim.Special; double t0 = sp.startup; var h = sp.hits; double end = sp.startup + sp.active + sp.recovery;
            SetBow(t > 0.14 && t < end - 0.22);
            if (t <= 0.14) return new Pick("idle", time, "bs0");
            if (!bowOn) return new Pick("idle", time, "bs9");
            if (t < t0 + h[0]) return new Pick("bowDraw", Math.Min(0.8, (t - 0.14) / (t0 + h[0] - 0.2) * 0.8), "bs1");
            for (int i = 0; i < h.Length; i++)
            {
                double T = t0 + h[i], next = i + 1 < h.Length ? t0 + h[i + 1] : end;
                if (t < T + 0.06) return new Pick("bowShoot", (t - T) / 0.06 * 0.08, "bsS" + i);
                if (t < next && i + 1 < h.Length) return new Pick("bowDraw", 0.35 + 0.45 * Clamp01((t - T - 0.06) / Math.Max(0.04, next - T - 0.08)), "bsD" + i);
            }
            return new Pick("bowShoot", 0.08 + Math.Min(0.42, (t - t0 - h[h.Length - 1] - 0.06) * 1.2), "bsE");
        }

        // 近身时的「打到」时刻：对手比兵器全伸的距离近时，取起手 / 挥击段里尖端（在头顶以下）向前越过对手中线的一刻，
        // 定格（打击停顿）时刃口停在对手身上，而不是整片穿出对手背后；够不到中线时仍取全伸的一刻（远处由前冲补足）
        public double ContactT(Cal c)
        {
            var o = opp;
            if (o == null || c.T == null || c.n < 2) return c.tc;
            double want = (o.x - f.x) * f.face + FIG_BITE;
            if (want >= c.tip - 0.06) return c.tc;
            // 向前挥出时尖端由近及远越过 want 的一刻（长兵架势里尖端本就在前方，先收后刺，取刺出时的那次越过）
            for (int i = 1; i < c.n; i++)
                if (c.X[i] >= want && c.X[i - 1] < want && c.Y[i] <= 2.0) return Lerp(c.T[i - 1], c.T[i], (want - c.X[i - 1]) / ((double)c.X[i] - c.X[i - 1]));
            return c.tc;
        }

        // 近战绝技：蓄势亮相（高举兵器）→ 突进 → 轻一 / 轻二 / 轻三 / 重击 四连斩（每段的「打到」与规则的命中时刻对齐）
        static readonly string[] SP_CLIP = { "light1", "light2", "light3", "heavy" };
        static readonly double[] SP_S0 = { 0, 0, 0, 0.2 };
        Pick SpecialPose(double t)
        {
            var sp = DuelSim.Special; double t0 = sp.startup; var h = sp.hits; var C = cal;
            if (t < DuelSim.SPECIAL_FREEZE) return new Pick("heavy", Math.Min(0.42, t / 0.3 * 0.42), "sp0");
            double prev = DuelSim.SPECIAL_FREEZE;
            for (int i = 0; i < h.Length; i++)
            {
                double T = t0 + h[i]; string clip = SP_CLIP[i]; double s0 = SP_S0[i]; var c = C[clip];
                if (t < T)
                {
                    // 提前一帧到达「打到」的姿势：命中时进入下一段（交叉淡化在打击停顿中不推进），定格的正是这一帧
                    double u = Clamp01((t - prev) / Math.Max(0.03, T - prev - 1.0 / 60));
                    return new Pick(clip, Lerp(s0, ContactT(c), u), "sp" + (i + 1), i == 0 ? new FigPoseOpts { lower = "dash", lowerT = t } : null, i == 0 ? Ease2(Clamp01((u - 0.2) / 0.8)) : 1, c);
                }
                prev = T;
            }
            var ch = C["heavy"]; double end = t0 + sp.active + sp.recovery;
            double uu = Clamp01((t - prev) / Math.Max(0.05, end - prev));
            return new Pick("heavy", Lerp(ch.tc, ch.dur, uu), "sp5", null, 1 - uu, ch);
        }

        // 出招时间 → 相位（0..3）。hold：起手前先定住的时长（满蓄重击举刀亮刃的一顿），之后按原节奏劈下（= 网页版 phaseOf）
        public static double PhaseOf(DuelMove mv, double t, double hold)
        {
            if (hold > 0) { if (t < hold) return 0; t -= hold; }
            if (t < mv.startup) return t / mv.startup;
            if (t < mv.startup + mv.active) return 1 + (t - mv.startup) / mv.active;
            return Math.Min(3, 2 + (t - mv.startup - mv.active) / mv.recovery);
        }
        static FigPoseOpts Lower(string clip, double t) { return new FigPoseOpts { lower = clip, lowerT = t }; }
        static FigPoseOpts BlendWith(string clip, double t, double w) { return new FigPoseOpts { blendClip = clip, blendT = t, blendW = w }; }

        // 状态 → (片段, 片段时间, 键, SetPose 选项, 前冲包络 0..1, 出招的测量值)
        Pick PickPose()
        {
            var C = cal; var mv = DuelSim.Mv(f.state);
            string st = f.state;
            string k = st + ":" + f.seq;
            if (intro >= 0)
            {
                double it = intro;
                if (it < 0.55) return new Pick("victory", it / 0.55 * 0.7, "in0");
                if (it < 0.95) return new Pick("block", it, "in1");
                return new Pick("idle", time, "idle");
            }
            if (st == "special") return f.bow ? BowSpecialPose(f.t) : SpecialPose(f.t);
            if (mv != null && st != "air")
            {
                var c = C[st];
                double hold = st == "heavy" ? DuelSim.HeavySU(f.charge, f.spd) - mv.startup : 0;
                double ph = PhaseOf(mv, f.t, hold);
                double s0 = st == "heavy" ? FIG_SWING["heavy"][0] : 0;
                // 「打到」的片段时刻：起手期间按与对手的距离逐帧取（连续变化），进入判定后锁定（打击停顿时姿势不跳）
                if (ph < 1 || tcKey != k) { tcKey = k; tcHit = ContactT(c); }
                double tc = tcHit;
                // 判定段先贴住「打到」的姿势再加速挥过（命中多在判定开始的一两帧，挥击末段极快，线性推进会在定格前就挥过头）
                double t = ph < 1 ? Lerp(s0, tc, st == "heavy" ? ph : Ease2(ph) * 0.35 + ph * 0.65) : ph < 2 ? Lerp(tc, c.te, (ph - 1) * (ph - 1)) : Lerp(c.te, c.dur, Ease2(ph - 2));
                // 前冲包络：整个起手里迈步（重击在举刀蓄势的停顿之后才迈），判定开始时正好到位（命中定格的一帧刃口贴住对手），收招时退回
                double env = ph < 1 ? Ease2(ph) : ph < 2 ? 1 : 1 - Ease2(ph - 2);
                return new Pick(st, t, k, null, env, c);
            }
            double jumpT = 0.12 + Clamp01((DuelSim.JUMP_V - f.vy) / (2 * DuelSim.JUMP_V)) * 0.5;
            switch (st)
            {
                case "air":
                    {
                        var c = C["air"]; double ph = PhaseOf(DuelSim.Air, f.t, 0);
                        double t = ph < 1 ? Lerp(0.36, c.tc, ph) : ph < 2 ? Lerp(c.tc, c.te, (ph - 1) * (ph - 1)) : Lerp(c.te, 0.74, ph - 2);
                        return new Pick("heavy", t, k, Lower("jump", jumpT), ph < 1 ? ph : ph < 2 ? 1 : 3 - ph, c);
                    }
                case "walk": return new Pick("idle", time, "walk", Lower("walk", walkPhase / (Math.PI * 2) * 1.0));
                case "guard": return new Pick("block", time, "guard");
                case "guardstun": return new Pick("block", time, k, BlendWith("hit", Math.Min(0.07, f.t * 1.2), 0.72));
                case "guardbreak": return new Pick("guardbreak", f.t < 0.08 ? f.t / 0.08 * 0.1 : 0.1 + (f.t - 0.08) / Math.Max(0.1, f.stun - 0.08) * 0.5, k);
                case "hitstun":
                    {
                        string clip = f.hitBack || f.hitH != "high" ? "hitB" : "hit";
                        double t = f.t < 0.07 ? f.t : 0.07 + (f.t - 0.07) / Math.Max(0.08, f.stun - 0.07) * 0.35;
                        return new Pick(clip, t, k + (f.hitH ?? ""));
                    }
                case "parry": return new Pick("parry", f.t < 0.04 ? f.t / 0.04 * 0.06 : 0.06 + Math.Min(0.02, (f.t - 0.04) * 0.12), k);
                case "parryok": return new Pick("parry", 0.08 + Math.Min(1, f.t / 0.06) * 0.06 + Math.Max(0, f.t - 0.06) * 0.3, k);
                case "parrywhiff":
                    {
                        double T = f.whiffT > 0 ? f.whiffT : DuelSim.PARRY_WHIFF, u = f.t / T;
                        double t = u < 0.25 ? 0.08 + u / 0.25 * 0.06 : 0.14 + (u - 0.25) / 0.75 * 0.22;
                        double w = u < 0.25 ? 1 - 0.55 * (u / 0.25) : 0.45 + 0.55 * Ease2((u - 0.25) / 0.75);
                        return new Pick("parry", t, k, BlendWith("hitB", 0.07, w));
                    }
                case "parried": return new Pick("guardbreak", f.t < 0.08 ? f.t / 0.08 * 0.1 : 0.1 + (f.t - 0.08) / Math.Max(0.1, f.stun - 0.08) * 0.5, k);
                case "backstep": return new Pick("block", time, k, Lower("jump", 0.1 + f.t / DuelSim.BACKSTEP_T * 0.62));
                case "knockdown":
                    {
                        // 估计落地时间：腾空段播完「仰面倒下」（片段 0 → 0.55），落地时正好躺平
                        double g = DuelSim.GRAV, rem = f.y > 0 || f.vy > 0 ? (f.vy + Math.Sqrt(Math.Max(0, f.vy * f.vy + 2 * g * Math.Max(0, f.y)))) / g : 0;
                        double tot = Math.Max(0.2, f.t + rem);
                        return new Pick("knockdown", Math.Min(0.55, f.t / tot * 0.55), k);
                    }
                case "down": return f.ko ? new Pick("down", 0.9 + f.t, k) : new Pick("knockdown", 0.55 + Math.Min(0.45, f.t), k);
                case "getup": return new Pick("getup", Math.Min(0.8, f.t / 0.42 * 0.8), k);
                case "dash": return new Pick("dash", f.t, k);
                case "jump": return new Pick("jump", jumpT, "jump");
                case "land": return new Pick("jump", Math.Min(0.8, 0.66 + f.t * 1.2), k);
                case "charge": return new Pick("heavy", Math.Min(0.42, f.t / 0.16 * 0.42), "charge");
                case "victory": return new Pick("victory", f.t, k);
                case "defeat": return f.ko ? new Pick("down", 0.9 + f.t, k) : new Pick("defeat", f.t, k);
                default: return new Pick("idle", time, "idle");
            }
        }

        // three 的 slerpQuaternions（最短弧；与 Quaternion.Slerp 等价，写出来以便无头测试逐值一致）
        static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            if (t <= 0) return a;
            if (t >= 1) return b;
            double x = a.x, y = a.y, z = a.z, w = a.w, bx = b.x, by = b.y, bz = b.z, bw = b.w;
            double cos = w * bw + x * bx + y * by + z * bz;
            if (cos < 0) { bx = -bx; by = -by; bz = -bz; bw = -bw; cos = -cos; }
            if (cos >= 1.0) return a;
            double sqrSin = 1.0 - cos * cos;
            if (sqrSin <= double.Epsilon)
            {
                double s0 = 1 - t;
                double rx = s0 * x + t * bx, ry = s0 * y + t * by, rz = s0 * z + t * bz, rw = s0 * w + t * bw;
                double l = Math.Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
                return new Quaternion((float)(rx / l), (float)(ry / l), (float)(rz / l), (float)(rw / l));
            }
            double sin = Math.Sqrt(sqrSin), half = Math.Atan2(sin, cos);
            double ra = Math.Sin((1 - t) * half) / sin, rb = Math.Sin(t * half) / sin;
            return new Quaternion((float)(x * ra + bx * rb), (float)(y * ra + by * rb), (float)(z * ra + bz * rb), (float)(w * ra + bw * rb));
        }

        // ---------------------------------------------------------- 每帧 --
        public void Update(float dt, bool frozen)
        {
            string st = f.state;
            time += dt;
            if (intro >= 0) intro += dt;
            if (st == "walk") walkPhase += f.vx * f.face * dt * (Math.PI * 2 / 1.25);
            if (bowOn && st != "special") SetBow(false);   // 绝技被打断 / 结束：收弓、拔回主兵器
            var p = PickPose();
            double dur; if (!BLEND.TryGetValue(st, out dur)) dur = 0.05;
            if (p.key != key)
            {
                for (int i = 0; i < bones.Length; i++) { snapQ[i] = bones[i].localRotation; snapP[i] = bones[i].localPosition; }
                blend = key.Length > 0 ? 0 : 1;
                blendDur = p.key.StartsWith("sp", StringComparison.Ordinal) || p.key.StartsWith("bs", StringComparison.Ordinal) ? 0.04 : dur;
                key = p.key;
            }
            fig.SetPose(p.clip, p.t, p.opts);
            if (!frozen) blend += dt;
            if (blend < blendDur)
            {
                float w = (float)Ease2(Clamp01(blend / blendDur));
                for (int i = 0; i < bones.Length; i++)
                {
                    if (!blendMask[i]) continue;
                    var b = bones[i];
                    b.localRotation = Slerp(snapQ[i], b.localRotation, w);
                    b.localPosition = Vector3.LerpUnclamped(snapP[i], b.localPosition, w);
                }
            }
            // 程序化叠加：重击举刀的一顿（后仰蓄势）、蓄力颤动、受击 / 被格开的抖动
            // （three 的 rotation.x −= a = 在父空间左乘 Rx(−a)；rotation.z += a = 在本地右乘 Rz(a)，换到模型库坐标为 Rz(−a)）
            double hold = st == "heavy" ? DuelSim.HeavySU(f.charge, f.spd) - DuelSim.Heavy.startup : 0;
            if (hold > 0 && f.t < hold) { double u = f.t / hold; var sp = fig.Bones["spine"]; sp.localRotation = Quaternion.AngleAxis((float)(-0.1 * u) * Mathf.Rad2Deg, Vector3.right) * sp.localRotation; }
            double jx = 0, jy = 0;
            if (st == "charge") { double q = f.charge, nn = time * 61; jx += Math.Sin(nn) * 0.008 * q; jy += Math.Cos(nn * 1.3) * 0.006 * q; }
            if (st == "hitstun" && f.t < 0.12) jx += Math.Sin(f.t * 120) * 0.03 * (1 - f.t / 0.12);
            if (st == "guardstun" && f.t < 0.1) jx += Math.Sin(f.t * 110) * 0.02 * (1 - f.t / 0.1);
            if (st == "parried")
            {
                double kk = Math.Max(0, 1 - f.t / Math.Max(0.1, f.stun));
                jx += Math.Sin(f.t * 9) * 0.025 * kk;
                var fa = fig.Bones["foreArmR"];
                fa.localRotation = fa.localRotation * Quaternion.AngleAxis((float)(-Math.Sin(f.t * 70) * 0.05 * kk) * Mathf.Rad2Deg, Vector3.forward);
            }
            fig.Update(frozen ? 0 : dt);
            HangCape(st, dt);
            // 前冲：只冲到刃口刚好碰到对手的身体（对手更近时少冲或不冲；够不着时最多冲到判定距离处）
            var o = opp; double env = p.env; var c = p.cal;
            double want = 0;
            if (env > 0 && c != null && c.lunge > 0)
            {
                double dx = o != null ? (o.x - f.x) * f.face : c.target + DuelSim.BODY * 0.5;
                want = env * Clamp(dx - DuelSim.BODY * 0.5 - c.tip, 0, c.lunge);
            }
            // 出招中跟住包络（确定性：判定开始的一帧必定到位，打击停顿时也不落后；限速 14 米/秒防止换招时跳变）；
            // 招式被打断 / 换招时先平滑退回，等包络追上当前位置再跟住
            if (p.key != lungeKey) { lungeKey = p.key; lungeLock = false; }
            if (env > 0 && want >= lungeX - 0.02) lungeLock = true;
            if (lungeLock && env > 0) { double mx = 14 * Math.Max(dt, 1.0 / 60); lungeX += Clamp(want - lungeX, -mx, mx); }
            else if (!frozen) lungeX += (want - lungeX) * (1 - Math.Exp(-9 * dt));
            // 位置与朝向（root.scale.x = 朝向：两人都以握兵器的右手一侧朝向镜头）；胜利姿势时转身面向镜头亮相
            double turnTo = st == "victory" ? 1.15 : intro >= 0 && intro < 0.6 ? 0.75 : TURN;
            turn += (turnTo - turn) * (1 - Math.Exp(-(st == "victory" ? 3 : 8) * dt));
            fig.Root.localRotation = Quaternion.Euler(0, Figure.YawFromJs(Math.PI / 2 - turn), 0);
            root.transform.localPosition = new Vector3((float)(f.x + f.face * lungeX + jx * f.face), (float)(f.y + jy), 0);
            root.transform.localScale = new Vector3(f.face, 1, 1);
            // 受击闪光 / 格开架势的冷光 / 反击窗口的金光 / 蓄力与绝技的发光
            // 第三版修订：着色改为以轮廓为主的辉光（Figure.SetTint），受击闪光封顶 0.6、衰减更快；反击窗口为脉动的金色轮廓
            if (flash > 0) flash = Mathf.Max(0, flash - dt * 10);
            Color T = Color.black;
            if (flash > 0) T = flashColor * Mathf.Min(0.6f, flash * 0.75f);
            if (st == "parry" && f.t <= f.parryW) T += parryCol * (float)(0.34 * (1 - f.t / f.parryW) + 0.1);
            else if (ctr > 0) T += ctrCol * (float)(0.32 + 0.22 * Math.Sin(time * 26));
            double em = 0;
            if (st == "charge") em = f.charge * 0.22 + (f.charge >= 1 ? 0.1 * (0.5 + 0.5 * Math.Sin(time * 30)) : 0);
            else if (hold > 0 && f.t < hold + DuelSim.Heavy.startup) em = 0.16 + 0.2 * f.charge;
            else if (st == "special" && f.t < DuelSim.SPECIAL_FREEZE + 0.1) em = 0.24 + 0.08 * Math.Sin(time * 25);
            else if (f.rage >= 100) em = 0.04 + 0.04 * Math.Sin(time * 8);
            if (em > 0) T += emCol * (float)em;
            T.a = 0;
            SetTint(T);
            // 世界坐标（刀光拖尾与特效用）
            var wi = fig.WeaponInfo;
            Vector3 tipU = FigUnity.V(wi != null ? wi.tip : new V3d(0, 1, 0)), baseU = FigUnity.V(wi != null ? wi.@base : new V3d(0, 0.2, 0));
            var ms = new[] { fig.WeaponMesh, fig.WeaponMesh2 };
            for (int k2 = 0; k2 < 2; k2++)
            {
                var m = ms[k2];
                if (m == null) { if (k2 == 0) { tipW[0] = root.transform.parent.TransformPoint(new Vector3((float)f.x, 1.2f, 0)); baseW[0] = tipW[0]; } continue; }
                tipW[k2] = m.transform.TransformPoint(tipU);
                baseW[k2] = m.transform.TransformPoint(baseU);
            }
            chestW = fig.Bones["chest"].position; chestW.y += 0.12f;
            if (bowOn) UpdateBowString();
        }

        // 辉光：Figure.SetTint(颜色, k) 写 _Tint = 颜色 × k；把可能超过 1 的颜色拆成「归一化颜色 × 最大分量」
        Color lastTint = new Color(-1, -1, -1, 0);
        void SetTint(Color c)
        {
            tint = c;
            if (c.r == lastTint.r && c.g == lastTint.g && c.b == lastTint.b) return;
            lastTint = c;
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (mx <= 0) { fig.SetTint("#000000", 0); return; }
            fig.SetTint(DuelXf.Hex(new Color(c.r / mx, c.g / mx, c.b / mx)), mx);
        }

        public void FlashHit(Color col, float k = 0.85f) { flash = k > 0 ? k : 0.85f; flashColor = col; }

        // 披风下垂：模型库的披风随躯干前倾一起扬起（弓身、跪地时会向后平伸成一片）。静止类姿势里限制披风偏离铅垂的角度，
        // 冲刺、突进、劈砍、腾空时放宽（披风可以飘起）
        void HangCape(string st, float dt)
        {
            Transform c1, c3;
            if (!fig.HasCape || !fig.Bones.TryGetValue("cape1", out c1) || !fig.Bones.TryGetValue("cape3", out c3)) return;
            var mv = DuelSim.Mv(st);
            bool dyn = st == "dash" || st == "backstep" || st == "jump" || st == "air" || st == "knockdown" || st == "special" || (mv != null && f.Phase != "startup") || (st == "walk" && Math.Abs(f.vx) > 1.5);
            double want = dyn ? 0.95 : st == "down" || st == "getup" ? 3 : 0.42;
            capeMax = double.IsNaN(capeMax) ? want : capeMax + (want - capeMax) * (1 - Math.Exp(-6 * Math.Max(dt, 0.016)));
            var R = fig.Root;
            // 模型库局部坐标的 y、z 与网页版相同（只有 x 取反）
            Vector3 a = R.InverseTransformPoint(c1.position), b = R.InverseTransformPoint(c3.position);
            double ang = Math.Atan2(a.z - b.z, a.y - b.y);   // 0 = 铅垂下垂，π/2 = 向后水平
            if (ang > capeMax) c1.localRotation = Quaternion.AngleAxis((float)(-(ang - capeMax) * 0.9) * Mathf.Rad2Deg, Vector3.right) * c1.localRotation;
        }

        public void Dispose()
        {
            if (bowString != null) UnityEngine.Object.Destroy(bowString.gameObject);
            if (nocked != null) UnityEngine.Object.Destroy(nocked);
            foreach (var o in owned) if (o != null) UnityEngine.Object.Destroy(o);
            owned.Clear();
            fig.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root);
        }
    }
}
