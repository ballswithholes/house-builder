using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sanguo
{
    /* ==========================================================================
       攻击画面（网页版 js/clash.js 的移植）
       原作中部队「攻击」时切换到横向交战画面：两军士兵对冲、兵力数字递减。
       这里用独立的低多边形三维场景现代化重制：自己的根 GameObject（放在远离战场的 Clash.Origin）
       + 自己的相机（depth 高于其它相机，只拍 Clash.Layer 层），结束时全部销毁并释放网格 / 材质 / 纹理。

       公开接口（UNITY-V2 §5）
         IEnumerator Clash.Play(attacker, defender, terrain, special, playerSide, speed, done)
           · 攻方永远在左；兵力数字从 troopsBefore 滚动到 troopsAfter；troopsAfter 为 0 视为溃散。
           · special 不为 null 时冲锋前先播 CutIn 特写，接敌时附带特效与额外伤亡
             （kind：blaze 火海 / storm 雷击 / volley 箭雨，其余一律为斩击弧光 + 冲击波）。
           · playerSide：0 / 1，-1 = 无（电脑对电脑）。speed：本次额外的时间倍率（与 Clash.Speed 相乘）。
           · done(skipped)：画面结束后回调（Mode 为 Off 时立即以 false 回调）。多次调用会排队依次播放。
         ClashSide Clash.FromUnit(model, unit, troopsBefore, troopsAfter)    由战场部队组装一方的参数
         IEnumerator Clash.CutIn(gen, moveName, color, side, cry, speed)     必杀技 / 单挑用的全屏特写
           （1.1 秒 ÷（Clash.Speed × speed），最短 0.7 秒；轻点 / 空格 / 回车 / Esc 可跳过）
         Clash.Mode（On / Fast / Off，读写，存 PlayerPrefs「sanguozhi2_clash」，值同网页版 on / fast / off）
         Clash.Enabled / Clash.Speed / Clash.CycleMode() / Clash.ModeLabel()
       坐标：场景逻辑全部沿用网页版的 three 坐标（x, y, z），绘制时换成 Unity 坐标 (x, y, -z)；
       矩阵 M_unity = S · M_three · S（S = diag(1, 1, -1)）。模型网格本来就按 Unity 坐标建（同 JS 的 V()）。
       装饰性随机一律使用 SeededRandom（按双方姓名与兵力取种子），不影响规则随机数。
       ========================================================================== */

    public enum ClashMode { On, Fast, Off }

    // 交战一方
    public class ClashSide
    {
        public General gen;
        public int side;                       // 0 攻方 1 守方（决定 HUD 的「攻方 / 守方」与识别色）
        public Color color = new Color(0.5f, 0.5f, 0.5f);   // 势力色
        public int troopsBefore;
        public int troopsAfter = -1;           // < 0 视为与 troopsBefore 相同（无伤亡）
        public int formation = -1;             // Defs.Formations 下标；< 0 取 gen.formation
        public string culture;                 // 缺省取 gen.culture，再缺省按南蛮名单 / han
    }

    // 必杀技（画面用）
    public class ClashSpecial
    {
        public string name;
        public Color color = new Color32(0xff, 0xd2, 0x4d, 255);
        public string kind, fx, cry;
        public static ClashSpecial From(Special sp)
        {
            if (sp == null) return null;
            return new ClashSpecial { name = sp.name, color = Art.Hex(Specials.UiColor(sp)), kind = sp.kind, fx = sp.fx, cry = sp.cry };
        }
    }

    public static class Clash
    {
        public const string ModeKey = "sanguozhi2_clash";
        public const int Layer = 30;                                    // 攻击画面与特写所用的渲染层
        public static readonly Vector3 Origin = new Vector3(0, -2000, 0);   // 场景根的世界位置（远离战场与地图）
        // 调试：FreezeAt ≥ 0 时动画停在该秒；HoldCut 为 true 时特写不自动结束（截图用）
        public static float FreezeAt = -1;
        public static bool HoldCut;

        static bool loaded, enabled = true;
        static float speed = 1f;
        internal static ClashScene active;
        internal static CutInView cut;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            string v = "on";
            try { v = PlayerPrefs.GetString(ModeKey, "on"); } catch (Exception) { /* 忽略 */ }
            enabled = v != "off";
            speed = v == "fast" ? 2f : 1f;
        }
        public static bool Enabled { get { Load(); return enabled; } set { Load(); enabled = value; } }
        public static float Speed { get { Load(); return speed; } set { Load(); speed = value; } }
        public static ClashMode Mode
        {
            get { Load(); return !enabled ? ClashMode.Off : (speed > 1.01f ? ClashMode.Fast : ClashMode.On); }
            set
            {
                Load();
                if (value == ClashMode.Off) { enabled = false; speed = 1; }
                else if (value == ClashMode.Fast) { enabled = true; speed = 2; }
                else { enabled = true; speed = 1; }
                try { PlayerPrefs.SetString(ModeKey, ModeName(Mode)); PlayerPrefs.Save(); } catch (Exception) { /* 忽略 */ }
            }
        }
        public static string ModeName(ClashMode m) { return m == ClashMode.Off ? "off" : m == ClashMode.Fast ? "fast" : "on"; }
        public static ClashMode CycleMode()
        {
            var m = Mode;
            Mode = m == ClashMode.On ? ClashMode.Fast : m == ClashMode.Fast ? ClashMode.Off : ClashMode.On;
            return Mode;
        }
        public static string ModeLabel() { var m = Mode; return m == ClashMode.On ? "开" : m == ClashMode.Fast ? "快" : "关"; }
        public static bool Active { get { return active != null; } }        // 攻击画面正在播放
        public static bool CutActive { get { return cut != null; } }        // 特写正在播放

        // ---------------------------------------------------------- 播放 --
        public static IEnumerator Play(ClashSide attacker, ClashSide defender, Terrain terrain, ClashSpecial special, int playerSide, float speed, Action<bool> done)
        {
            if (!Enabled) { if (done != null) done(false); yield break; }
            // 排队：前一场播完再开始
            while (active != null) yield return null;
            if (!Enabled) { if (done != null) done(false); yield break; }
            ClashScene sc = null;
            try
            {
                sc = new ClashScene(attacker, defender, terrain, special, playerSide, speed);
                active = sc;
                sc.Start();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (sc != null) sc.Dispose();
                active = null;
                if (done != null) done(false);
                yield break;
            }
            // 场景由根物体上的 ClashRunner 逐帧推进（调用方的协程中途停掉也会按时结束并释放）
            while (!sc.Finished) yield return null;
            if (done != null) done(sc.Skipped);
        }

        // 由战场部队组装一方的参数（JS fromBattle 的一半）
        public static ClashSide FromUnit(BattleModel m, BUnit u, int troopsBefore, int troopsAfter)
        {
            var o = new ClashSide { troopsBefore = troopsBefore, troopsAfter = troopsAfter };
            if (u == null) return o;
            o.gen = u.gen;
            o.side = u.side;
            o.formation = u.formation;
            o.culture = u.gen != null ? u.gen.culture : null;
            var g = GameState.Current;
            int idx = m != null && m.setup != null ? (u.side == 0 ? m.setup.attacker : m.setup.defender) : -1;
            if (g != null && idx >= 0 && idx < g.factions.Count) o.color = g.factions[idx].Col;
            else o.color = new Color(0.5f, 0.5f, 0.5f);
            return o;
        }

        // 必杀技 / 单挑的全屏特写
        public static IEnumerator CutIn(General g, string moveName, Color color, int side, string cry, float speed)
        {
            if (!Enabled) yield break;
            var v = CutInView.Open(g, moveName, color, side, cry, speed);
            if (v == null) yield break;
            while (!v.Ended) yield return null;
        }

        public static string TerrainKind(Terrain t)
        {
            switch (t)
            {
                case Terrain.Forest: return "forest";
                case Terrain.Hill: return "hill";
                case Terrain.Mountain: return "mountain";
                case Terrain.River: return "river";
                case Terrain.Wall: return "wall";
                case Terrain.Gate: return "gate";
                case Terrain.Castle: return "castle";
                default: return "plain";
            }
        }
    }

    // 场景根物体上的驱动器：每帧推进场景；物体被意外销毁时也释放资源
    internal class ClashRunner : MonoBehaviour
    {
        public ClashScene sc;
        void Update() { if (sc != null) sc.Tick(Time.unscaledDeltaTime); }
        void LateUpdate() { if (sc != null) sc.Draw(); }
        void OnDestroy() { if (sc != null) { var s = sc; sc = null; s.Dispose(); } }
    }

    // 轻点 / 点击：转发给回调（pointerdown 即触发，同网页版）
    internal class ClashTap : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public Action onDown;
        public void OnPointerDown(PointerEventData e) { if (onDown != null) onDown(); }
        public void OnPointerClick(PointerEventData e) { }
    }

    // ======================================================= 小工具 --
    internal static class ClashUtil
    {
        public static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        // three 世界坐标 → Unity 坐标（z 取反）
        public static Vector3 P(float x, float y, float z) { return new Vector3(x, y, -z); }
        public static Color C(float r, float g, float b) { return new Color(r, g, b); }
        public static Color Shade(Color c, float k) { return Art.Shade(c, k); }
        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static float Seg(float t, float a, float b) { return b <= a ? (t >= a ? 1 : 0) : Clamp01((t - a) / (b - a)); }
        public static float Sm(float t) { return t * t * (3 - 2 * t); }
        public static float Eo(float t) { float u = 1 - t; return 1 - u * u * u; }
        public static float Eio(float t) { return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; }
        public static float Sstep(float e0, float e1, float x) { return Sm(Clamp01((x - e0) / (e1 - e0))); }
        // [a, b] 内为 1、两端各有 r 秒渐变的梯形窗
        public static float Trap(float t, float a, float b, float r) { return Mathf.Max(0, Mathf.Min(Seg(t, a - r * 0.5f, a + r * 0.5f), 1 - Seg(t, b - r * 0.5f, b + r * 0.5f))); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float R(SeededRandom r) { return (float)r.NextDouble(); }
        public static int HashStr(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                if (s == null) s = "";
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
                return (int)h;
            }
        }
        public static float Luma(Color c) { return 0.3f * c.r + 0.59f * c.g + 0.11f * c.b; }
        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant(); }
        public static Color Lerp(Color a, Color b, float t) { return Color.Lerp(a, b, t); }
        public static void Sfx(string name, float vol = 0.8f) { try { Sanguo.Sfx.Play(name, vol); } catch (Exception) { /* 无音频 */ } }
        // 交锋乐句（Sfx.Stinger 由音乐模块提供；尚未提供时静默）
        static System.Reflection.MethodInfo stinger;
        static bool stingerLooked;
        public static void Stinger(string kind, float speed)
        {
            try
            {
                if (!stingerLooked) { stingerLooked = true; stinger = typeof(Sanguo.Sfx).GetMethod("Stinger", new[] { typeof(string), typeof(float) }); }
                if (stinger != null) stinger.Invoke(null, new object[] { kind, speed });
            }
            catch (Exception) { /* 无音频 */ }
        }
        // three 空间的矩阵 → Unity 空间（S · M · S）
        public static Matrix4x4 ToU(Matrix4x4 m)
        {
            m.m02 = -m.m02; m.m12 = -m.m12; m.m20 = -m.m20; m.m21 = -m.m21; m.m23 = -m.m23; m.m32 = -m.m32;
            return m;
        }
        public static Matrix4x4 TRS(float x, float y, float z, Quaternion q, float s = 1f) { return Matrix4x4.TRS(new Vector3(x, y, z), q, new Vector3(s, s, s)); }
        public static Quaternion AxisX(float rad) { return Quaternion.AngleAxis(rad * Mathf.Rad2Deg, Vector3.right); }
        // three 的 Euler(pitch, yaw, spin, 'YXZ')
        public static Quaternion YXZ(float pitch, float yaw, float spin)
        {
            return Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(spin * Mathf.Rad2Deg, Vector3.forward);
        }
        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }
        public static Font Body { get { return UIKit.Body != null ? UIKit.Body : Fallback; } }
        public static Font Kai { get { return UIKit.Title != null ? UIKit.Title : Body; } }
        static Font fallback;
        static Font Fallback { get { if (fallback == null) fallback = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 32); return fallback; } }
        public static bool IsTouch { get { return Application.isMobilePlatform || (Input.touchSupported && !Input.mousePresent); } }
    }

    // ======================================================= 士兵与武将模型 --
    // 全部以 Unity 坐标建模、面朝 +z。部件分开以便逐个摆动：
    // 腿（髋部为原点）、身体（含头盔、盾）、兵器（右手握点为原点）。
    // 文化装束（DESIGN-V2 §6）：全部是参数化的方块 / 圆柱拼件，每方只建一次网格。
    //   hat 士兵头饰 / ghat 武将头盔 / shield 盾形 / armor 甲衣样式 / legs 腿（裤、裸腿、长靴、绑腿）
    //   pants 裤色、hair 发色、beard 胡须、trim 默认镶边色、cloak 武将披风色（缺省为势力色暗部）
    internal sealed class ClashCult
    {
        public Color skin;
        public string hat, ghat, shield, armor, legs;
        public Color? trim, pants, hair, cloak;
        public bool beard, tattoo;
    }

    internal static class ClashArt
    {
        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Color Shade(Color c, float k) { return Art.Shade(c, k); }

        static readonly Color HAIR_D = C(0.12f, 0.1f, 0.09f);
        public static readonly Dictionary<string, ClashCult> CULT = new Dictionary<string, ClashCult>
        {
            { "han", new ClashCult { skin = C(0.95f, 0.79f, 0.62f), hat = "cone", ghat = "g_han", shield = "rect", armor = "lamellar", legs = "trousers" } },
            { "nanman", new ClashCult { skin = C(0.7f, 0.5f, 0.34f), hat = "bun", ghat = "g_feather", shield = "rattan", armor = "rattan", legs = "bare", trim = C(0.92f, 0.88f, 0.76f) } },
            { "wa", new ClashCult { skin = C(0.93f, 0.77f, 0.6f), hat = "mizura", ghat = "g_wa", shield = "wood", armor = "tanko", legs = "wraps", pants = C(0.86f, 0.82f, 0.72f) } },
            { "yi", new ClashCult { skin = C(0.68f, 0.48f, 0.32f), hat = "feather", ghat = "g_feather", shield = "round", armor = "bare", legs = "bare", trim = C(0.95f, 0.93f, 0.86f), tattoo = true } },
            { "korea", new ClashCult { skin = C(0.95f, 0.8f, 0.64f), hat = "plume", ghat = "g_plume", shield = "rect", armor = "lamellar", legs = "trousers", pants = C(0.84f, 0.8f, 0.7f) } },
            { "steppe", new ClashCult { skin = C(0.86f, 0.68f, 0.5f), hat = "fur", ghat = "g_fur", shield = "small", armor = "furcoat", legs = "boots", pants = C(0.42f, 0.32f, 0.22f) } },
            { "seasia", new ClashCult { skin = C(0.62f, 0.43f, 0.28f), hat = "seband", ghat = "g_crown", shield = "oval", armor = "bare", legs = "bare" } },
            { "tarim", new ClashCult { skin = C(0.92f, 0.76f, 0.6f), hat = "tall", ghat = "tall", shield = "round", armor = "robe", legs = "boots", pants = C(0.5f, 0.36f, 0.26f), trim = C(0.85f, 0.78f, 0.6f) } },
            { "kushan", new ClashCult { skin = C(0.84f, 0.64f, 0.48f), hat = "kushan", ghat = "g_kushan", shield = "round", armor = "robe", legs = "boots", pants = C(0.3f, 0.24f, 0.2f) } },
            { "persia", new ClashCult { skin = C(0.84f, 0.64f, 0.48f), hat = "phrygian", ghat = "g_tiara", shield = "spara", armor = "scale", legs = "boots", pants = C(0.46f, 0.2f, 0.18f), beard = true } },
            { "arab", new ClashCult { skin = C(0.78f, 0.58f, 0.42f), hat = "kufiya", ghat = "kufiya", shield = "round", armor = "robe", legs = "bare", beard = true, trim = C(0.93f, 0.9f, 0.82f) } },
            { "roman", new ClashCult { skin = C(0.95f, 0.8f, 0.66f), hat = "crest", ghat = "g_roman", shield = "scutum", armor = "segmentata", legs = "bare", cloak = C(0.72f, 0.12f, 0.1f) } },
            { "celt", new ClashCult { skin = C(0.97f, 0.84f, 0.72f), hat = "limed", ghat = "limed", shield = "oval", armor = "plaid", legs = "trousers", pants = C(0.4f, 0.42f, 0.3f), hair = C(0.62f, 0.3f, 0.12f), beard = true } },
            { "german", new ClashCult { skin = C(0.97f, 0.84f, 0.72f), hat = "knot", ghat = "knot", shield = "hex", armor = "furcloak", legs = "wraps", pants = C(0.36f, 0.3f, 0.22f), hair = C(0.8f, 0.64f, 0.34f), beard = true } },
            { "sarmatian", new ClashCult { skin = C(0.9f, 0.74f, 0.58f), hat = "spike", ghat = "g_spike", shield = "small", armor = "scale", legs = "boots", pants = C(0.3f, 0.26f, 0.22f) } },
        };
        public static ClashCult CultOf(string c) { ClashCult r; return c != null && CULT.TryGetValue(c, out r) ? r : CULT["han"]; }
        static readonly Color IRON = C(0.42f, 0.43f, 0.47f), IRON_D = C(0.3f, 0.31f, 0.35f), WOOD = C(0.43f, 0.3f, 0.17f), RED = C(0.8f, 0.16f, 0.12f), GOLD = C(0.88f, 0.7f, 0.28f);
        static readonly Color BRONZE = C(0.7f, 0.5f, 0.26f), FUR = C(0.46f, 0.33f, 0.2f), TAN = C(0.74f, 0.62f, 0.4f), WHITE = C(0.95f, 0.94f, 0.89f);
        public static Color Wood { get { return WOOD; } }
        public static Color Gold { get { return GOLD; } }

        // ---------------------------------------------------------- 网格辅助 --
        static void QuadOut(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 rf)
        {
            var u = b - a; var v = c - a;
            var n = new Vector3(u.y * v.z - u.z * v.y, u.z * v.x - u.x * v.z, u.x * v.y - u.y * v.x);
            var m = (a + c) / 2 - rf;
            if (n.x * m.x + n.y * m.y + n.z * m.z >= 0) mb.Quad(a, b, c, d, col); else mb.Quad(a, d, c, b, col);
        }
        static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        static Vector3 Norm(Vector3 a) { float l = Mathf.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z); if (l == 0) l = 1; return a / l; }
        static readonly int[,] CS = { { -1, -1 }, { 1, -1 }, { 1, 1 }, { -1, 1 } };
        // 任意方向的方柱（Unity 坐标）：a → b，截面宽 w0 → w1
        public static void Beam(MeshBuilder mb, Vector3 a, Vector3 b, float w0, float w1, Color col)
        {
            var d = Norm(b - a);
            var rf = Mathf.Abs(d.y) < 0.9f ? V(0, 1, 0) : V(1, 0, 0);
            var u = Norm(Cross(d, rf)); var v = Cross(d, u);
            var A = new Vector3[4]; var B = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float p = CS[i, 0], q = CS[i, 1];
                A[i] = a + (u * p + v * q) * w0 / 2;
                B[i] = b + (u * p + v * q) * w1 / 2;
            }
            var mid = (a + b) / 2;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var k = Shade(col, i % 2 == 1 ? -0.07f : 0.03f);
                if (w1 > 1e-5f) QuadOut(mb, A[i], A[j], B[j], B[i], k, mid);
                else
                {
                    var n = Cross(A[j] - A[i], b - A[i]);
                    var m = (A[i] + A[j] + b) / 3 - mid;
                    if (n.x * m.x + n.y * m.y + n.z * m.z >= 0) mb.Tri(A[i], A[j], b, k);
                    else mb.Tri(A[i], b, A[j], k);
                }
            }
            QuadOut(mb, A[0], A[1], A[2], A[3], Shade(col, -0.1f), mid);
            if (w1 > 1e-5f) QuadOut(mb, B[0], B[1], B[2], B[3], Shade(col, 0.06f), mid);
        }
        // 沿 x 轴的扁圆盘（圆盾）：中心 c，半径 r（ry 为竖向半径，缺省同 r），厚 t
        static void DiscX(MeshBuilder mb, Vector3 c, float r, float t, Color col, int seg = 8, float ry = 0)
        {
            if (ry <= 0) ry = r;
            var rf = c;
            for (int i = 0; i < seg; i++)
            {
                float a0 = (float)i / seg * Mathf.PI * 2, a1 = (float)(i + 1) / seg * Mathf.PI * 2;
                float p0x = Mathf.Cos(a0) * r, p0y = Mathf.Sin(a0) * ry, p1x = Mathf.Cos(a1) * r, p1y = Mathf.Sin(a1) * ry;
                Vector3 o0 = V(c.x - t / 2, c.y + p0y, c.z + p0x), o1 = V(c.x - t / 2, c.y + p1y, c.z + p1x);
                Vector3 i0 = V(c.x + t / 2, c.y + p0y, c.z + p0x), i1 = V(c.x + t / 2, c.y + p1y, c.z + p1x);
                QuadOut(mb, o0, o1, i1, i0, Shade(col, -0.2f), rf);
                Vector3 ca = V(c.x - t / 2, c.y, c.z), cb = V(c.x + t / 2, c.y, c.z);
                DiscTri(mb, ca, o0, o1, Shade(col, (i % 2) * 0.05f), rf);
                DiscTri(mb, cb, i0, i1, Shade(col, -0.1f), rf);
            }
        }
        static void DiscTri(MeshBuilder mb, Vector3 p, Vector3 q, Vector3 s, Color k, Vector3 rf)
        {
            var n = Cross(q - p, s - p);
            float mx = p.x - rf.x;
            if (n.x * mx >= 0) mb.Tri(p, q, s, k); else mb.Tri(p, s, q, k);
        }

        // ---------------------------------------------------------- 头饰 --
        // y0 = 头顶，z0 = 头部中心 z，s = 尺寸倍数；acc = 识别色（近色对阵时的缨 / 羽 / 带，可为 null）
        static void Headgear(MeshBuilder mb, string hat, Color team, float y0, float s, float z0, Color? acc, ClashCult cu)
        {
            Func<float, float, float, Vector3> at = (x, y, z) => V(x * s, y0 + y * s, z0 + z * s);
            Func<float, float, float, Vector3> sz = (x, y, z) => V(x * s, y * s, z * s);
            Color hair = cu != null && cu.hair.HasValue ? cu.hair.Value : HAIR_D;
            Color tassel = acc ?? RED;
            Action cap = () => mb.Box(at(0, 0.0f, -0.005f), sz(0.17f, 0.035f, 0.165f), hair);
            switch (hat)
            {
                case "bun":      // 南中：椎髻 + 骨簪 + 红头带
                    mb.Box(at(0, -0.03f, 0), sz(0.17f, 0.03f, 0.16f), acc ?? RED);
                    mb.Box(at(0, 0.03f, -0.02f), sz(0.08f, 0.07f, 0.08f), hair);
                    mb.Box(at(0, 0.05f, -0.02f), sz(0.17f, 0.018f, 0.018f), C(0.94f, 0.9f, 0.78f));
                    break;
                case "mizura":   // 倭：美豆良（左右耳侧的发环）
                    mb.Box(at(0, 0.005f, 0), sz(0.17f, 0.035f, 0.16f), hair);
                    mb.Box(at(-0.1f, -0.08f, 0), sz(0.04f, 0.09f, 0.05f), hair);
                    mb.Box(at(0.1f, -0.08f, 0), sz(0.04f, 0.09f, 0.05f), hair);
                    mb.Box(at(0, -0.025f, 0), sz(0.175f, 0.02f, 0.165f), acc ?? WHITE);
                    break;
                case "band":
                    mb.Box(at(0, -0.03f, 0), sz(0.17f, 0.035f, 0.16f), acc ?? Shade(team, 0.2f));
                    mb.Box(at(0, 0.005f, 0), sz(0.155f, 0.03f, 0.15f), hair);
                    break;
                case "feather":  // 夷洲：头带 + 竖羽
                    cap();
                    mb.Box(at(0, -0.03f, 0), sz(0.175f, 0.035f, 0.165f), acc ?? Shade(team, 0.15f));
                    Beam(mb, at(-0.04f, -0.01f, -0.06f), at(-0.07f, 0.17f, -0.09f), 0.035f * s, 0.012f * s, WHITE);
                    Beam(mb, at(0.0f, -0.01f, -0.07f), at(0.0f, 0.2f, -0.1f), 0.035f * s, 0.012f * s, tassel);
                    Beam(mb, at(0.04f, -0.01f, -0.06f), at(0.07f, 0.17f, -0.09f), 0.035f * s, 0.012f * s, WHITE);
                    break;
                case "seband":   // 林邑 / 扶南：金箍 + 顶髻
                    cap();
                    mb.Box(at(0, -0.03f, 0), sz(0.178f, 0.03f, 0.168f), GOLD);
                    mb.Cone(at(0, 0.0f, -0.01f), 0.05f * s, 0.09f * s, 5, hair);
                    mb.Box(at(0, 0.05f, -0.01f), sz(0.03f, 0.03f, 0.03f), GOLD);
                    break;
                case "plume":    // 高句丽 / 三韩：铁盔 + 白羽
                    mb.Cone(at(0, -0.035f, 0), 0.115f * s, 0.12f * s, 6, IRON);
                    mb.Box(at(0, 0.13f, -0.01f), sz(0.025f, 0.14f, 0.05f), acc ?? C(0.95f, 0.95f, 0.9f));
                    mb.Box(at(-0.09f, -0.1f, 0.02f), sz(0.02f, 0.08f, 0.06f), IRON_D);
                    mb.Box(at(0.09f, -0.1f, 0.02f), sz(0.02f, 0.08f, 0.06f), IRON_D);
                    break;
                case "fur":      // 草原：毡帽 + 毛皮檐
                    mb.Cylinder(at(0, -0.05f, 0), 0.115f * s, 0.1f * s, 0.09f * s, 6, FUR);
                    mb.Cone(at(0, 0.035f, 0), 0.08f * s, 0.07f * s, 6, Shade(team, -0.1f));
                    if (acc.HasValue) mb.Box(at(0, 0.1f, 0), sz(0.03f, 0.04f, 0.03f), acc.Value);
                    mb.Box(at(0, -0.12f, -0.08f), sz(0.06f, 0.12f, 0.03f), hair);    // 辫
                    break;
                case "tall":     // 西域：白毡尖帽 + 帽檐
                    mb.Cylinder(at(0, -0.04f, 0), 0.105f * s, 0.1f * s, 0.03f * s, 6, C(0.82f, 0.76f, 0.62f));
                    mb.Cone(at(0, -0.01f, 0), 0.095f * s, 0.24f * s, 6, C(0.86f, 0.8f, 0.66f));
                    mb.Box(at(0, -0.035f, 0), sz(0.2f, 0.012f, 0.19f), acc ?? Shade(team, 0.1f));
                    break;
                case "kushan":   // 贵霜：深色高尖帽 + 金箍
                    mb.Cylinder(at(0, -0.045f, 0), 0.11f * s, 0.11f * s, 0.035f * s, 6, acc ?? GOLD);
                    mb.Cone(at(0, -0.01f, -0.01f), 0.1f * s, 0.27f * s, 6, Shade(team, -0.42f));
                    break;
                case "phrygian": // 安息：弗里吉亚软帽（帽尖前倾）+ 护耳垂片
                    mb.Cylinder(at(0, -0.05f, 0), 0.112f * s, 0.105f * s, 0.07f * s, 6, Shade(team, -0.3f));
                    Beam(mb, at(0, 0.015f, -0.01f), at(0, 0.12f, 0.07f), 0.16f * s, 0.03f * s, Shade(team, -0.22f));
                    mb.Box(at(-0.088f, -0.12f, 0.01f), sz(0.02f, 0.1f, 0.07f), Shade(team, -0.3f));
                    mb.Box(at(0.088f, -0.12f, 0.01f), sz(0.02f, 0.1f, 0.07f), Shade(team, -0.3f));
                    if (acc.HasValue) mb.Box(at(0, -0.03f, 0.002f), sz(0.228f, 0.02f, 0.215f), acc.Value);
                    break;
                case "kufiya":   // 阿拉伯：头巾垂肩 + 深色头箍
                    mb.Box(at(0, -0.01f, 0), sz(0.185f, 0.05f, 0.175f), WHITE);
                    mb.Box(at(0, -0.12f, -0.085f), sz(0.19f, 0.2f, 0.025f), C(0.9f, 0.88f, 0.82f));
                    mb.Box(at(-0.092f, -0.1f, -0.02f), sz(0.02f, 0.16f, 0.12f), C(0.9f, 0.88f, 0.82f));
                    mb.Box(at(0.092f, -0.1f, -0.02f), sz(0.02f, 0.16f, 0.12f), C(0.9f, 0.88f, 0.82f));
                    mb.Cylinder(at(0, 0.01f, 0), 0.1f * s, 0.1f * s, 0.022f * s, 6, acc ?? C(0.15f, 0.12f, 0.1f));
                    break;
                case "spike":    // 萨尔马提亚：尖顶铁盔 + 锁子护颈
                    mb.Cone(at(0, -0.04f, 0), 0.112f * s, 0.2f * s, 6, IRON);
                    mb.Box(at(0, -0.1f, -0.075f), sz(0.16f, 0.09f, 0.025f), IRON_D);
                    if (acc.HasValue) mb.Box(at(0, 0.165f, 0), sz(0.03f, 0.05f, 0.03f), acc.Value);
                    break;
                case "crest":    // 罗马：高卢式铁盔 + 纵冠 + 护颊 + 宽护颈
                    mb.Cone(at(0, -0.035f, 0), 0.115f * s, 0.11f * s, 6, IRON);
                    mb.Box(at(0, 0.09f, 0), sz(0.03f, 0.06f, 0.2f), tassel);
                    mb.Box(at(-0.085f, -0.09f, 0.03f), sz(0.02f, 0.08f, 0.06f), IRON_D);
                    mb.Box(at(0.085f, -0.09f, 0.03f), sz(0.02f, 0.08f, 0.06f), IRON_D);
                    mb.Box(at(0, -0.075f, -0.085f), sz(0.2f, 0.025f, 0.06f), IRON_D);
                    break;
                case "limed":    // 喀里多尼亚：石灰竖发 + 长发
                    cap();
                    for (int i = -1; i <= 1; i++) mb.Cone(at(i * 0.05f, 0.0f, -0.01f - Mathf.Abs(i) * 0.02f), 0.035f * s, 0.09f * s, 4, Shade(hair, 0.25f));
                    mb.Box(at(0, -0.1f, -0.075f), sz(0.17f, 0.17f, 0.035f), hair);
                    break;
                case "knot":     // 日耳曼：苏维汇发髻（右侧）+ 长发
                    cap();
                    mb.Box(at(0, -0.1f, -0.075f), sz(0.17f, 0.18f, 0.04f), hair);
                    mb.Box(at(0.075f, 0.0f, -0.01f), sz(0.06f, 0.06f, 0.07f), Shade(hair, -0.1f));
                    if (acc.HasValue) mb.Box(at(0, -0.035f, 0), sz(0.178f, 0.018f, 0.168f), acc.Value);
                    break;
                case "hair":
                    mb.Box(at(0, 0.0f, -0.01f), sz(0.17f, 0.04f, 0.17f), hair);
                    mb.Box(at(0, -0.1f, -0.075f), sz(0.17f, 0.18f, 0.04f), hair);
                    break;
                case "turban":
                    mb.Cylinder(at(0, -0.05f, 0), 0.105f * s, 0.11f * s, 0.08f * s, 6, C(0.92f, 0.9f, 0.84f));
                    mb.Cone(at(0, 0.03f, 0), 0.07f * s, 0.05f * s, 6, C(0.9f, 0.88f, 0.8f));
                    break;
                // ---- 武将专用（尺寸不乘 s）----
                case "g_han":    // 汉：金盔 + 雉尾
                    mb.Cylinder(at(0, -0.04f, 0), 0.125f, 0.125f, 0.035f, 6, Shade(GOLD, -0.2f));
                    mb.Cone(at(0, -0.01f, 0), 0.12f, 0.14f, 6, GOLD);
                    Beam(mb, at(0, 0.12f, -0.015f), at(0, 0.36f, -0.215f), 0.035f, 0.012f, tassel);
                    Beam(mb, at(0.03f, 0.12f, -0.015f), at(0.06f, 0.32f, -0.235f), 0.03f, 0.01f, Shade(tassel, 0.2f));
                    break;
                case "g_roman":  // 罗马：铁盔 + 横向红冠（百夫长式）+ 护颊
                    mb.Cone(at(0, -0.035f, 0), 0.122f, 0.12f, 6, IRON);
                    mb.Box(at(0, 0.095f, 0), V(0.3f, 0.075f, 0.035f), tassel);
                    mb.Box(at(0, 0.055f, 0), V(0.04f, 0.03f, 0.04f), GOLD);
                    mb.Box(at(-0.09f, -0.11f, 0.045f), V(0.02f, 0.09f, 0.07f), IRON_D);
                    mb.Box(at(0.09f, -0.11f, 0.045f), V(0.02f, 0.09f, 0.07f), IRON_D);
                    mb.Box(at(0, -0.1f, -0.075f), V(0.17f, 0.06f, 0.03f), IRON_D);
                    break;
                case "g_spike":  // 萨尔马提亚：鎏金尖顶分片盔 + 锁子护颈 + 两条飘带
                    mb.Cylinder(at(0, -0.04f, 0), 0.125f, 0.125f, 0.03f, 6, Shade(GOLD, -0.25f));
                    mb.Cone(at(0, -0.02f, 0), 0.118f, 0.24f, 6, GOLD);
                    for (int i = -1; i <= 1; i += 2) mb.Box(at(i * 0.055f, 0.04f, 0), V(0.012f, 0.11f, 0.012f), Shade(GOLD, -0.35f));
                    mb.Box(at(0, -0.13f, -0.085f), V(0.19f, 0.12f, 0.03f), IRON_D);
                    Beam(mb, at(-0.04f, -0.04f, -0.105f), at(-0.07f, -0.24f, -0.315f), 0.03f, 0.015f, WHITE);
                    Beam(mb, at(0.04f, -0.04f, -0.105f), at(0.07f, -0.21f, -0.295f), 0.03f, 0.015f, tassel);
                    break;
                case "g_plume":  // 高句丽 / 三韩：鎏金盔 + 高耸白羽
                    mb.Cylinder(at(0, -0.04f, 0), 0.125f, 0.125f, 0.035f, 6, Shade(GOLD, -0.2f));
                    mb.Cone(at(0, -0.01f, 0), 0.115f, 0.13f, 6, GOLD);
                    Beam(mb, at(0, 0.1f, 0), at(0, 0.4f, -0.03f), 0.05f, 0.02f, acc ?? C(0.96f, 0.95f, 0.9f));
                    Beam(mb, at(0.035f, 0.1f, 0), at(0.07f, 0.34f, -0.05f), 0.035f, 0.012f, C(0.92f, 0.9f, 0.84f));
                    Beam(mb, at(-0.035f, 0.1f, 0), at(-0.07f, 0.34f, -0.05f), 0.035f, 0.012f, C(0.92f, 0.9f, 0.84f));
                    break;
                case "g_tiara":  // 安息：高圆提亚拉冠（势力色暗部）+ 珠串金箍 + 背后飘带 + 披发
                    mb.Cylinder(at(0, -0.045f, 0), 0.118f, 0.112f, 0.17f, 6, Shade(team, -0.38f));
                    mb.Cylinder(at(0, 0.125f, 0), 0.112f, 0.06f, 0.06f, 6, Shade(team, -0.3f));
                    mb.Cylinder(at(0, -0.045f, 0), 0.124f, 0.124f, 0.035f, 6, GOLD);
                    mb.Cylinder(at(0, 0.09f, 0), 0.116f, 0.116f, 0.022f, 6, GOLD);
                    for (int i = -2; i <= 2; i++) mb.Box(at(i * 0.035f, 0.035f, 0.11f), V(0.018f, 0.018f, 0.014f), WHITE);
                    mb.Box(at(0, -0.14f, -0.08f), V(0.18f, 0.2f, 0.05f), hair);
                    Beam(mb, at(-0.04f, -0.03f, -0.11f), at(-0.08f, -0.25f, -0.3f), 0.03f, 0.015f, tassel);
                    Beam(mb, at(0.04f, -0.03f, -0.11f), at(0.08f, -0.22f, -0.3f), 0.03f, 0.015f, GOLD);
                    break;
                case "g_wa":     // 倭：冲角付胄（前凸的铁盔）+ 宽护颈 + 立饰
                    mb.Cylinder(at(0, -0.045f, 0), 0.128f, 0.122f, 0.05f, 6, IRON_D);
                    mb.Cone(at(0, 0.0f, 0), 0.12f, 0.12f, 6, IRON);
                    Beam(mb, at(0, 0.02f, 0.02f), at(0, -0.02f, 0.15f), 0.08f, 0.02f, IRON);
                    mb.Box(at(0, -0.1f, -0.1f), V(0.27f, 0.035f, 0.11f), IRON_D);
                    mb.Box(at(0, -0.14f, -0.12f), V(0.3f, 0.035f, 0.11f), Shade(IRON_D, -0.1f));
                    mb.Box(at(0, 0.13f, 0), V(0.02f, 0.07f, 0.06f), acc ?? GOLD);
                    break;
                case "g_fur":    // 草原：狐皮帽 + 护耳 + 势力色毡顶 + 双辫
                    mb.Cylinder(at(0, -0.06f, 0), 0.13f, 0.12f, 0.1f, 6, FUR);
                    mb.Cone(at(0, 0.04f, 0), 0.1f, 0.15f, 6, Shade(team, -0.05f));
                    mb.Box(at(-0.105f, -0.13f, 0.0f), V(0.03f, 0.12f, 0.08f), Shade(FUR, 0.1f));
                    mb.Box(at(0.105f, -0.13f, 0.0f), V(0.03f, 0.12f, 0.08f), Shade(FUR, 0.1f));
                    mb.Box(at(0, 0.2f, 0), V(0.03f, 0.05f, 0.03f), acc ?? GOLD);
                    mb.Box(at(-0.05f, -0.18f, -0.09f), V(0.04f, 0.16f, 0.03f), hair);
                    mb.Box(at(0.05f, -0.18f, -0.09f), V(0.04f, 0.16f, 0.03f), hair);
                    break;
                case "g_feather":// 南中 / 夷洲首领：金箍 + 扇形羽冠
                    mb.Box(at(0, -0.03f, 0), V(0.18f, 0.04f, 0.17f), GOLD);
                    mb.Box(at(0, 0.0f, -0.005f), V(0.17f, 0.035f, 0.165f), hair);
                    for (int i = -2; i <= 2; i++) Beam(mb, at(i * 0.03f, 0.0f, -0.06f), at(i * 0.09f, 0.26f - Mathf.Abs(i) * 0.04f, -0.1f), 0.045f, 0.015f, i % 2 != 0 ? WHITE : (i != 0 ? tassel : Shade(team, 0.25f)));
                    break;
                case "g_crown":  // 林邑 / 扶南：多层金尖冠
                    mb.Cylinder(at(0, -0.04f, 0), 0.115f, 0.11f, 0.06f, 6, GOLD);
                    mb.Cylinder(at(0, 0.02f, 0), 0.095f, 0.075f, 0.08f, 6, Shade(GOLD, 0.08f));
                    mb.Cylinder(at(0, 0.1f, 0), 0.065f, 0.045f, 0.07f, 6, GOLD);
                    mb.Cone(at(0, 0.17f, 0), 0.04f, 0.14f, 5, Shade(GOLD, 0.12f));
                    if (acc.HasValue) mb.Box(at(0, -0.01f, 0.105f), V(0.04f, 0.04f, 0.02f), acc.Value);
                    break;
                case "g_kushan": // 贵霜王冠：高尖帽 + 金冠带 + 飘带
                    mb.Cylinder(at(0, -0.045f, 0), 0.12f, 0.12f, 0.045f, 6, GOLD);
                    mb.Cone(at(0, 0.0f, -0.01f), 0.108f, 0.3f, 6, Shade(team, -0.42f));
                    Beam(mb, at(-0.04f, -0.03f, -0.11f), at(-0.08f, -0.23f, -0.3f), 0.03f, 0.015f, acc ?? GOLD);
                    Beam(mb, at(0.04f, -0.03f, -0.11f), at(0.08f, -0.2f, -0.3f), 0.03f, 0.015f, acc ?? GOLD);
                    break;
                default:         // 'cone' 汉军铁胄：盔体、帽檐、红缨、顿项
                    mb.Cylinder(at(0, -0.045f, 0), 0.118f * s, 0.118f * s, 0.03f * s, 6, IRON_D);
                    mb.Cone(at(0, -0.02f, 0), 0.112f * s, 0.13f * s, 6, IRON);
                    mb.Box(at(0, 0.12f, 0), sz(0.035f, 0.05f, 0.035f), tassel);
                    mb.Box(at(0, -0.1f, -0.07f), sz(0.17f, 0.1f, 0.03f), IRON_D);
                    break;
            }
        }

        // 胡须（面朝 +z）：hc = 头部中心，hs = 头部尺寸
        static void BeardOf(MeshBuilder mb, ClashCult cu, Vector3 hc, float hs)
        {
            if (!cu.beard) return;
            Color hair = cu.hair ?? HAIR_D;
            mb.Box(V(hc.x, hc.y - hs * 0.36f, hc.z + hs * 0.46f), V(hs * 0.82f, hs * 0.36f, hs * 0.16f), hair);
            mb.Box(V(hc.x, hc.y - hs * 0.16f, hc.z + hs * 0.52f), V(hs * 0.5f, hs * 0.08f, hs * 0.06f), hair);
        }

        // 甲衣样式：躯干 (y, z, w, h, d)（面朝 +z）；trim = 镶边 / 识别色
        static void TorsoDeco(MeshBuilder mb, ClashCult cu, float y, float z, float w, float h, float d, Color t, Color trim)
        {
            float fz = z + d / 2 + 0.006f, bz = z - d / 2 - 0.006f;
            var light = Shade(t, 0.16f);
            Action<float, float, float, float, Color, float> both = (cx, cy, sw, sh, col, dz) =>
            {
                mb.Box(V(cx, cy, fz + dz), V(sw, sh, 0.02f), col);
                mb.Box(V(cx, cy, bz - dz), V(sw, sh, 0.02f), Shade(col, -0.08f));
            };
            float OV = fz + 0.014f;     // 贴在胸甲外的细节
            switch (cu.armor)
            {
                case "scale":        // 鱼鳞甲：前后三排错色甲片
                    for (int r = 0; r < 3; r++)
                    {
                        float yy = y + h * (0.26f - r * 0.21f), ww = w * (0.8f - r * 0.04f);
                        both(0, yy, ww, h * 0.2f, r % 2 == 1 ? IRON : BRONZE, 0);
                        for (int i = -1; i <= 1; i += 2) mb.Box(V(i * ww * 0.25f, yy - h * 0.09f, OV), V(0.02f, 0.02f, 0.012f), Shade(r % 2 == 1 ? IRON : BRONZE, -0.3f));
                    }
                    break;
                case "segmentata":   // 环片甲：铁条绕身（缝隙露出势力色）
                    for (int r = 0; r < 3; r++) mb.Box(V(0, y + h * (0.32f - r * 0.25f), z), V(w + 0.016f, h * 0.15f, d + 0.016f), r % 2 == 1 ? IRON_D : IRON);
                    mb.Box(V(0, y + h * 0.45f, z), V(w * 0.55f, h * 0.1f, d + 0.02f), IRON_D);
                    break;
                case "tanko":        // 短甲：铁胸板 + 势力色横带 + 勾玉项链
                    both(0, y + h * 0.08f, w * 0.8f, h * 0.62f, IRON, 0);
                    both(0, y + h * 0.08f, w * 0.82f, h * 0.1f, light, 0.008f);
                    for (int i = -2; i <= 2; i++) mb.Box(V(i * 0.026f, y + h * 0.4f - Mathf.Abs(i) * 0.01f, OV), V(0.018f, 0.026f, 0.014f), C(0.2f, 0.62f, 0.45f));
                    break;
                case "robe":         // 长袍：前襟镶边
                    both(0, y, w * 0.12f, h * 0.95f, trim, 0);
                    break;
                case "furcoat":      // 皮袍：毛皮领与前襟
                    mb.Box(V(0, y + h * 0.43f, z), V(w * 0.78f, h * 0.16f, d + 0.02f), FUR);
                    both(0, y - h * 0.05f, w * 0.14f, h * 0.8f, FUR, 0);
                    break;
                case "rattan":       // 藤甲：棕黄编织胸甲（纵横两色）
                    both(0, y + h * 0.05f, w * 0.82f, h * 0.7f, TAN, 0);
                    for (int i = -1; i <= 1; i++) mb.Box(V(i * w * 0.24f, y + h * 0.05f, OV), V(0.014f, h * 0.66f, 0.012f), Shade(TAN, -0.25f));
                    mb.Box(V(0, y + h * 0.05f, OV), V(w * 0.8f, 0.014f, 0.012f), Shade(TAN, -0.25f));
                    break;
                case "bare":         // 短衣：金饰 / 贝珠项链
                    mb.Box(V(0, y + h * 0.42f, z + d * 0.2f), V(w * 0.6f, 0.03f, d * 0.75f), cu.tattoo ? WHITE : GOLD);
                    if (!cu.tattoo) mb.Box(V(0, y + h * 0.3f, fz), V(0.05f, 0.05f, 0.015f), GOLD);
                    break;
                case "plaid":        // 方格衣 + 金项圈（torc）
                    for (int r = 0; r < 2; r++) for (int i = -1; i <= 1; i++) if ((r + i) % 2 == 0) both(i * w * 0.28f, y + h * (0.2f - r * 0.36f), w * 0.24f, h * 0.3f, Shade(t, -0.32f), 0);
                    mb.Box(V(0, y + h * 0.52f, z + d * 0.15f), V(w * 0.5f, 0.03f, d * 0.7f), GOLD);
                    break;
                case "furcloak":     // 毛皮披肩（肩与背）
                    mb.Box(V(0, y + h * 0.4f, z - d * 0.1f), V(w + 0.05f, h * 0.22f, d + 0.02f), FUR);
                    mb.Box(V(0, y + h * 0.05f, bz - 0.01f), V(w * 0.9f, h * 0.7f, 0.03f), Shade(FUR, -0.08f));
                    break;
                default:             // 'lamellar' 札甲：胸甲 + 两道甲片横缝
                    both(0, y + h * 0.08f, w * 0.76f, h * 0.62f, light, 0);
                    for (int r = 0; r < 2; r++) mb.Box(V(0, y + h * (0.16f - r * 0.2f), OV), V(w * 0.74f, 0.012f, 0.012f), Shade(t, -0.12f));
                    break;
            }
        }

        // 盾（左臂外侧，盾面法线沿 x）：c = 盾心，trim = 镶边 / 识别色（null 时用文化默认）
        static void ShieldOf(MeshBuilder mb, string kind, Vector3 c, Color t, Color? trim)
        {
            Color face = Shade(t, 0.06f), dark = Shade(t, -0.4f);
            Color rim = trim ?? dark, boss = trim ?? GOLD;
            Func<float, Vector3> o = dx => V(c.x - dx, c.y, c.z);       // 向外（-x）偏移
            switch (kind)
            {
                case "round":
                    DiscX(mb, V(c.x + 0.006f, c.y, c.z), 0.185f, 0.03f, rim);
                    DiscX(mb, c, 0.17f, 0.035f, face);
                    mb.Box(o(0.025f), V(0.02f, 0.06f, 0.06f), boss);
                    break;
                case "small":        // 小圆皮盾：皮色盾面 + 势力色盾心
                    DiscX(mb, c, 0.135f, 0.03f, C(0.4f, 0.28f, 0.16f));
                    DiscX(mb, o(0.012f), 0.09f, 0.012f, face);
                    mb.Box(o(0.026f), V(0.02f, 0.045f, 0.045f), boss);
                    break;
                case "rattan":       // 藤牌：棕黄编织 + 势力色内圈
                    DiscX(mb, c, 0.18f, 0.035f, TAN);
                    DiscX(mb, o(0.012f), 0.1f, 0.012f, face);
                    mb.Box(o(0.028f), V(0.02f, 0.05f, 0.05f), trim ?? C(0.92f, 0.88f, 0.76f));
                    break;
                case "oval":         // 长椭圆盾（凯尔特 / 南海）：竖脊 + 盾心
                    DiscX(mb, V(c.x + 0.006f, c.y, c.z), 0.14f, 0.03f, rim, 10, 0.25f);
                    DiscX(mb, c, 0.125f, 0.035f, face, 10, 0.235f);
                    mb.Box(o(0.022f), V(0.015f, 0.4f, 0.03f), boss);
                    mb.Box(o(0.028f), V(0.02f, 0.07f, 0.07f), boss);
                    break;
                case "hex":          // 日耳曼六角盾
                    DiscX(mb, V(c.x + 0.006f, c.y, c.z), 0.2f, 0.03f, rim, 6);
                    DiscX(mb, c, 0.18f, 0.035f, face, 6);
                    mb.Box(o(0.026f), V(0.02f, 0.065f, 0.065f), boss);
                    break;
                case "scutum":       // 罗马长方大盾：势力色盾面 + 金色边框、竖脊、盾心
                    mb.Box(c, V(0.04f, 0.46f, 0.3f), face);
                    mb.Box(o(0.022f), V(0.008f, 0.46f, 0.022f), boss);
                    mb.Box(V(c.x - 0.022f, c.y + 0.22f, c.z), V(0.008f, 0.022f, 0.3f), trim ?? GOLD);
                    mb.Box(V(c.x - 0.022f, c.y - 0.22f, c.z), V(0.008f, 0.022f, 0.3f), trim ?? GOLD);
                    mb.Box(V(c.x - 0.022f, c.y, c.z + 0.14f), V(0.008f, 0.46f, 0.02f), trim ?? GOLD);
                    mb.Box(V(c.x - 0.022f, c.y, c.z - 0.14f), V(0.008f, 0.46f, 0.02f), trim ?? GOLD);
                    mb.Box(o(0.03f), V(0.02f, 0.08f, 0.08f), boss);
                    break;
                case "spara":        // 安息藤编立盾：浅黄编条 + 势力色横纹
                    mb.Box(c, V(0.03f, 0.46f, 0.24f), C(0.78f, 0.68f, 0.46f));
                    for (int i = -1; i <= 1; i++) mb.Box(V(c.x - 0.018f, c.y + i * 0.13f, c.z), V(0.008f, 0.06f, 0.24f), face);
                    mb.Box(V(c.x - 0.018f, c.y, c.z + 0.115f), V(0.01f, 0.46f, 0.02f), rim);
                    mb.Box(V(c.x - 0.018f, c.y, c.z - 0.115f), V(0.01f, 0.46f, 0.02f), rim);
                    break;
                case "wood":         // 倭：木质立盾 + 势力色锯齿纹
                    mb.Box(c, V(0.035f, 0.44f, 0.2f), Shade(WOOD, 0.12f));
                    for (int i = -1; i <= 1; i++) mb.Box(V(c.x - 0.02f, c.y + i * 0.13f, c.z + (i % 2 != 0 ? 0.04f : -0.04f)), V(0.008f, 0.07f, 0.11f), face);
                    mb.Box(V(c.x - 0.02f, c.y + 0.205f, c.z), V(0.01f, 0.03f, 0.2f), rim);
                    break;
                default:             // 'rect' 汉：长方盾
                    mb.Box(c, V(0.04f, 0.4f, 0.25f), face);
                    mb.Box(o(0.025f), V(0.02f, 0.3f, 0.08f), dark);
                    mb.Box(o(0.03f), V(0.02f, 0.07f, 0.07f), boss);
                    mb.Box(V(c.x, c.y + 0.205f, c.z), V(0.045f, 0.02f, 0.25f), rim);
                    mb.Box(V(c.x, c.y - 0.205f, c.z), V(0.045f, 0.02f, 0.25f), rim);
                    break;
            }
        }

        // 士兵身体：acc = 识别色（两军势力色相近时给出，用在肩、腰带、盾缘、盔缨上；否则 null）
        public static Mesh Body(Color team, string culture, bool archer, Color? acc)
        {
            var mb = new MeshBuilder();
            var cu = CultOf(culture);
            Color t = team, mid = Shade(t, -0.18f), dark = Shade(t, -0.4f);
            Color skin = cu.skin;
            var leather = C(0.32f, 0.22f, 0.13f);
            Color? trim = acc ?? cu.trim;
            bool armBare = cu.armor == "bare" || cu.armor == "plaid" || cu.armor == "rattan";
            Color arm = armBare ? skin : mid;
            bool robe = cu.armor == "robe" || cu.armor == "furcoat";
            if (robe)
            {
                float hem = cu.armor == "robe" ? 0.2f : 0.27f;
                mb.Box(V(0, (hem + 0.45f) / 2, 0), V(0.31f, 0.45f - hem, 0.205f), mid);                         // 袍摆
                mb.Box(V(0, hem + 0.015f, 0), V(0.33f, 0.03f, 0.215f), cu.armor == "furcoat" ? FUR : (trim ?? dark));
            }
            else
            {
                mb.Box(V(0, 0.385f, 0), V(0.31f, 0.13f, 0.2f), mid);               // 甲裙
                mb.Box(V(0, 0.335f, 0.0f), V(0.33f, 0.04f, 0.215f), trim ?? dark); // 甲裙下缘
            }
            mb.Box(V(0, 0.565f, 0), V(0.29f, 0.26f, 0.18f), t);                     // 躯干
            TorsoDeco(mb, cu, 0.565f, 0, 0.29f, 0.26f, 0.18f, t, trim ?? GOLD);
            mb.Box(V(0, 0.455f, 0), V(0.3f, 0.045f, 0.19f), acc ?? leather);        // 腰带
            mb.Box(V(0, 0.455f, 0.1f), V(0.06f, 0.05f, 0.02f), GOLD);               // 带扣
            if (!armBare && cu.armor != "furcloak")
            {
                Color sh = acc ?? (cu.armor == "segmentata" ? IRON : dark);
                mb.Box(V(-0.185f, 0.655f, 0), V(0.1f, 0.075f, 0.18f), sh);            // 肩甲
                mb.Box(V(0.185f, 0.655f, 0), V(0.1f, 0.075f, 0.18f), sh);
            }
            else if (acc.HasValue)
            {
                mb.Box(V(-0.185f, 0.665f, 0), V(0.09f, 0.04f, 0.17f), acc.Value);
                mb.Box(V(0.185f, 0.665f, 0), V(0.09f, 0.04f, 0.17f), acc.Value);
            }
            mb.Box(V(-0.2f, 0.53f, 0.03f), V(0.07f, 0.19f, 0.08f), arm);            // 左臂
            mb.Box(V(0.2f, 0.575f, 0.04f), V(0.07f, 0.13f, 0.08f), arm);            // 右上臂
            mb.Box(V(0.2f, 0.505f, 0.12f), V(0.065f, 0.065f, 0.16f), arm);          // 右前臂（前伸到握点）
            if (cu.tattoo) foreach (var sx in new[] { -0.2f, 0.2f }) mb.Box(V(sx, 0.56f, 0.072f), V(0.074f, 0.018f, 0.012f), C(0.18f, 0.22f, 0.3f));   // 文身
            mb.Box(V(0, 0.71f, 0), V(0.08f, 0.04f, 0.08f), skin);                   // 颈
            mb.Box(V(0, 0.795f, 0.005f), V(0.155f, 0.145f, 0.15f), skin);           // 头
            mb.Box(V(0, 0.79f, 0.081f), V(0.11f, 0.025f, 0.01f), Shade(skin, -0.45f));   // 眉眼的阴影
            BeardOf(mb, cu, V(0, 0.795f, 0.005f), 0.15f);
            Headgear(mb, cu.hat, t, 0.87f, 1, 0, acc, cu);
            if (archer)
            {
                // 箭囊（背后）与箭羽
                mb.Box(V(0.06f, 0.6f, -0.13f), V(0.09f, 0.3f, 0.08f), leather);
                for (int i = 0; i < 3; i++) mb.Box(V(0.04f + i * 0.022f, 0.78f, -0.13f), V(0.016f, 0.07f, 0.016f), C(0.92f, 0.9f, 0.85f));
            }
            else ShieldOf(mb, cu.shield, V(-0.265f, 0.51f, 0.07f), t, acc);
            return mb.ToMesh("clash body");
        }
        // 腿（髋部为原点）：trousers 裤 + 绑腿 + 靴 / bare 裸腿 + 凉鞋 / boots 裤 + 高靴 / wraps 浅色裤 + 交叉绑带
        public static Mesh Leg(string culture)
        {
            var mb = new MeshBuilder();
            var cu = CultOf(culture);
            Color skin = cu.skin;
            Color pants = cu.pants ?? C(0.27f, 0.22f, 0.19f);
            switch (cu.legs)
            {
                case "bare":
                    mb.Box(V(0, -0.12f, 0), V(0.095f, 0.24f, 0.1f), skin);
                    mb.Box(V(0, -0.25f, 0), V(0.1f, 0.03f, 0.105f), C(0.4f, 0.27f, 0.15f));
                    mb.Box(V(0, -0.33f, 0.02f), V(0.11f, 0.06f, 0.15f), C(0.36f, 0.24f, 0.13f));
                    break;
                case "boots":
                    mb.Box(V(0, -0.1f, 0), V(0.11f, 0.2f, 0.12f), pants);
                    mb.Box(V(0, -0.27f, 0.01f), V(0.12f, 0.18f, 0.135f), C(0.2f, 0.14f, 0.1f));
                    mb.Box(V(0, -0.345f, 0.035f), V(0.12f, 0.04f, 0.17f), C(0.15f, 0.11f, 0.09f));
                    break;
                case "wraps":
                    mb.Box(V(0, -0.12f, 0), V(0.11f, 0.24f, 0.12f), pants);
                    mb.Box(V(0, -0.17f, 0.0f), V(0.115f, 0.022f, 0.125f), C(0.3f, 0.22f, 0.14f));
                    mb.Box(V(0, -0.24f, 0.0f), V(0.115f, 0.022f, 0.125f), C(0.3f, 0.22f, 0.14f));
                    mb.Box(V(0, -0.31f, 0.02f), V(0.115f, 0.1f, 0.16f), C(0.24f, 0.17f, 0.11f));
                    break;
                default:
                    mb.Box(V(0, -0.12f, 0), V(0.105f, 0.24f, 0.115f), pants);
                    mb.Box(V(0, -0.235f, 0), V(0.11f, 0.05f, 0.12f), C(0.72f, 0.66f, 0.55f));   // 绑腿
                    mb.Box(V(0, -0.31f, 0.02f), V(0.115f, 0.1f, 0.16f), C(0.15f, 0.11f, 0.09f));  // 靴
                    break;
            }
            return mb.ToMesh("clash leg");
        }
        public static Mesh Spear()
        {
            var mb = new MeshBuilder();
            mb.Box(V(0, 0.27f, 0), V(0.024f, 1.38f, 0.024f), WOOD);                  // 杆：y −0.42 → 0.96
            mb.Cone(V(0, 0.95f, 0), 0.034f, 0.15f, 4, C(0.88f, 0.9f, 0.94f));          // 矛头
            mb.Box(V(0, 0.935f, 0), V(0.05f, 0.035f, 0.05f), RED);                    // 红缨
            mb.Box(V(0, 0, 0), V(0.055f, 0.065f, 0.06f), C(0.92f, 0.76f, 0.6f));      // 拳
            return mb.ToMesh("clash spear");
        }
        public static Mesh Bow()
        {
            var mb = new MeshBuilder();
            const int N = 6; const float R = 0.4f;
            for (int i = 0; i < N; i++)
            {
                float y0 = -R + ((float)i / N) * 2 * R, y1 = -R + ((float)(i + 1) / N) * 2 * R;
                float z0 = 0.14f * (1 - (y0 / R) * (y0 / R)), z1 = 0.14f * (1 - (y1 / R) * (y1 / R));
                Beam(mb, V(0, y0, z0), V(0, y1, z1), 0.028f, 0.028f, C(0.36f, 0.22f, 0.12f));
            }
            Beam(mb, V(0, -R, 0), V(0, R, 0), 0.007f, 0.007f, C(0.9f, 0.88f, 0.8f));   // 弦
            mb.Box(V(0, 0, 0.03f), V(0.055f, 0.065f, 0.06f), C(0.92f, 0.76f, 0.6f));
            return mb.ToMesh("clash bow");
        }
        public static Mesh Arrow()
        {
            var mb = new MeshBuilder();
            Beam(mb, V(0, 0, -0.3f), V(0, 0, 0.28f), 0.018f, 0.018f, C(0.5f, 0.38f, 0.22f));
            Beam(mb, V(0, 0, 0.28f), V(0, 0, 0.4f), 0.05f, 0, C(0.75f, 0.76f, 0.8f));
            var fl = C(0.94f, 0.93f, 0.88f);
            mb.Quad(V(0, 0, -0.3f), V(0, 0.045f, -0.26f), V(0, 0.045f, -0.16f), V(0, 0, -0.18f), fl);
            mb.Quad(V(0, 0, -0.3f), V(0, 0, -0.18f), V(0, 0.045f, -0.16f), V(0, 0.045f, -0.26f), fl);
            mb.Quad(V(0, 0, -0.3f), V(0.045f, 0, -0.26f), V(0.045f, 0, -0.16f), V(0, 0, -0.18f), fl);
            mb.Quad(V(0, 0, -0.3f), V(0, 0, -0.18f), V(0.045f, 0, -0.16f), V(0.045f, 0, -0.26f), fl);
            return mb.ToMesh("clash arrow");
        }
        public static Mesh HorseLeg()
        {
            var mb = new MeshBuilder();
            Beam(mb, V(0, 0.02f, 0), V(0, -0.3f, 0.02f), 0.1f, 0.07f, C(1, 1, 1));
            Beam(mb, V(0, -0.3f, 0.02f), V(0, -0.5f, 0.0f), 0.065f, 0.055f, C(0.92f, 0.92f, 0.92f));
            mb.Box(V(0, -0.525f, 0.015f), V(0.075f, 0.05f, 0.09f), C(0.28f, 0.28f, 0.28f));
            return mb.ToMesh("clash horse leg");
        }
        // 武将骑马像：马身、马鞍披挂（势力色）、骑者（按文化的头盔 / 甲衣 / 披风）、长柄刀、帅旗杆
        public static Mesh General(Color team, string culture, Color horse, string weapon, Color? acc)
        {
            var mb = new MeshBuilder();
            var cu = CultOf(culture);
            Color t = team, dark = Shade(t, -0.4f), mid = Shade(t, -0.18f);
            Color skin = cu.skin;
            Color hd = Shade(horse, -0.25f), mane = ClashUtil.Luma(horse) > 0.6f ? C(0.85f, 0.83f, 0.8f) : C(0.12f, 0.1f, 0.09f);
            Color trim = acc ?? GOLD;
            // 马
            mb.Box(V(0, 0.69f, -0.02f), V(0.3f, 0.28f, 0.86f), horse);
            mb.Box(V(0, 0.71f, 0.36f), V(0.32f, 0.3f, 0.2f), Shade(horse, 0.04f));      // 胸
            mb.Box(V(0, 0.7f, -0.38f), V(0.31f, 0.27f, 0.18f), Shade(horse, -0.05f));   // 臀
            Beam(mb, V(0, 0.76f, 0.42f), V(0, 1.06f, 0.62f), 0.2f, 0.15f, horse);          // 颈
            Beam(mb, V(0, 1.1f, 0.6f), V(0, 0.97f, 0.88f), 0.15f, 0.1f, Shade(horse, 0.04f));   // 头
            mb.Box(V(0, 0.96f, 0.88f), V(0.09f, 0.07f, 0.06f), hd);                       // 口鼻
            mb.Cone(V(-0.045f, 1.14f, 0.6f), 0.025f, 0.07f, 4, hd);
            mb.Cone(V(0.045f, 1.14f, 0.6f), 0.025f, 0.07f, 4, hd);
            Beam(mb, V(0, 0.87f, 0.4f), V(0, 1.15f, 0.58f), 0.06f, 0.05f, mane);           // 鬃
            Beam(mb, V(0, 0.76f, -0.47f), V(0, 0.4f, -0.66f), 0.09f, 0.04f, mane);         // 尾
            // 鞍与披挂（草原 / 萨尔马提亚为毛皮鞍褥）
            mb.Box(V(0, 0.85f, -0.04f), V(0.34f, 0.06f, 0.4f), dark);
            mb.Box(V(0, 0.7f, -0.04f), V(0.335f, 0.26f, 0.44f), t);
            mb.Box(V(0, 0.575f, -0.04f), V(0.34f, 0.03f, 0.45f), trim);
            mb.Box(V(0, 0.9f, -0.12f), V(0.2f, 0.06f, 0.16f), cu.armor == "furcoat" ? FUR : C(0.36f, 0.22f, 0.12f));
            // 骑者
            bool robe = cu.armor == "robe" || cu.armor == "furcoat";
            Color legC = cu.legs == "bare" ? skin : (robe ? mid : dark);
            mb.Box(V(-0.16f, 0.8f, 0.06f), V(0.09f, 0.22f, 0.12f), legC);                  // 腿
            mb.Box(V(0.16f, 0.8f, 0.06f), V(0.09f, 0.22f, 0.12f), legC);
            Color boot = cu.legs == "boots" ? C(0.2f, 0.14f, 0.1f) : C(0.15f, 0.11f, 0.09f);
            float bootH = 0.07f + (cu.legs == "boots" ? 0.05f : 0);
            mb.Box(V(-0.16f, 0.68f, 0.1f), V(0.1f, bootH, 0.15f), boot);
            mb.Box(V(0.16f, 0.68f, 0.1f), V(0.1f, bootH, 0.15f), boot);
            mb.Box(V(0, 1.1f, -0.06f), V(0.3f, 0.32f, 0.2f), t);                          // 躯干
            TorsoDeco(mb, cu, 1.1f, -0.06f, 0.3f, 0.32f, 0.2f, t, trim);
            mb.Box(V(0, 0.95f, -0.06f), V(0.32f, 0.06f, 0.21f), trim);                    // 腰带
            if (cu.armor != "furcloak" && cu.armor != "bare")
            {
                mb.Box(V(-0.19f, 1.22f, -0.06f), V(0.11f, 0.08f, 0.2f), cu.armor == "segmentata" ? IRON : trim);   // 肩甲
                mb.Box(V(0.19f, 1.22f, -0.06f), V(0.11f, 0.08f, 0.2f), cu.armor == "segmentata" ? IRON : trim);
            }
            Color cloak = cu.cloak ?? (cu.armor == "furcloak" ? FUR : (cu.armor == "plaid" ? Shade(t, -0.3f) : mid));
            Beam(mb, V(0, 1.24f, -0.17f), V(0, 0.84f, -0.38f), 0.3f, 0.38f, cloak);         // 披风（罗马将领为红色 paludamentum）
            Color arm = (cu.armor == "bare" || cu.armor == "plaid") ? skin : mid;
            mb.Box(V(-0.2f, 1.08f, 0.0f), V(0.075f, 0.2f, 0.08f), arm);                    // 左臂（持缰）
            mb.Box(V(0.21f, 1.1f, 0.02f), V(0.075f, 0.18f, 0.08f), arm);                   // 右臂
            mb.Box(V(0.22f, 1.0f, 0.12f), V(0.07f, 0.07f, 0.14f), arm);
            mb.Box(V(0, 1.29f, -0.05f), V(0.09f, 0.04f, 0.09f), skin);
            mb.Box(V(0, 1.38f, -0.045f), V(0.165f, 0.155f, 0.16f), skin);                 // 头
            mb.Box(V(0, 1.375f, 0.04f), V(0.11f, 0.025f, 0.01f), Shade(skin, -0.45f));
            BeardOf(mb, cu, V(0, 1.38f, -0.045f), 0.16f);
            // 将盔：按文化区分（DESIGN-V2 §6）；ghat 以 g_ 开头者为武将专用盔
            string gh = cu.ghat ?? cu.hat;
            Headgear(mb, gh, t, 1.46f, gh.StartsWith("g_", StringComparison.Ordinal) ? 1 : 1.06f, -0.045f, acc, cu);
            // 兵器：长柄大刀（右手）
            const float wx = 0.24f, wz = 0.14f;
            Beam(mb, V(wx, 0.5f, wz), V(wx, 1.95f, wz), 0.035f, 0.03f, C(0.35f, 0.22f, 0.13f));
            if (weapon == "halberd")
            {
                Beam(mb, V(wx, 1.95f, wz), V(wx, 2.2f, wz), 0.05f, 0, C(0.88f, 0.9f, 0.94f));
                Beam(mb, V(wx, 1.86f, wz), V(wx, 1.86f, wz + 0.2f), 0.03f, 0.05f, C(0.85f, 0.87f, 0.9f));
                Beam(mb, V(wx, 1.86f, wz), V(wx, 1.86f, wz - 0.2f), 0.03f, 0.05f, C(0.85f, 0.87f, 0.9f));
            }
            else if (weapon == "spear")
            {
                Beam(mb, V(wx, 1.95f, wz), V(wx, 2.25f, wz + 0.03f), 0.045f, 0, C(0.88f, 0.9f, 0.94f));
                Beam(mb, V(wx, 2.0f, wz), V(wx + 0.02f, 2.25f, wz + 0.16f), 0.03f, 0, C(0.85f, 0.87f, 0.9f));
            }
            else
            {
                // 偃月刀：弯刃（几段方柱拼成）
                var blade = C(0.86f, 0.88f, 0.92f);
                Beam(mb, V(wx, 1.9f, wz + 0.02f), V(wx, 2.12f, wz + 0.1f), 0.03f, 0.06f, blade);
                Beam(mb, V(wx, 2.12f, wz + 0.1f), V(wx, 2.3f, wz + 0.06f), 0.06f, 0.04f, blade);
                Beam(mb, V(wx, 2.3f, wz + 0.06f), V(wx, 2.38f, wz - 0.04f), 0.04f, 0.0f, blade);
            }
            mb.Box(V(wx, 1.9f, wz), V(0.06f, 0.05f, 0.06f), acc ?? RED);
            mb.Box(V(wx, 1.0f, wz + 0.02f), V(0.06f, 0.07f, 0.07f), skin);
            // 帅旗杆（背后左侧）
            Beam(mb, V(-0.18f, 0.78f, -0.3f), V(-0.18f, 3.0f, -0.3f), 0.035f, 0.03f, C(0.3f, 0.2f, 0.12f));
            mb.Cone(V(-0.18f, 3.0f, -0.3f), 0.04f, 0.14f, 4, GOLD);
            Beam(mb, V(-0.18f, 2.86f, -0.3f), V(-0.18f, 2.86f, -1.06f), 0.025f, 0.025f, C(0.3f, 0.2f, 0.12f));
            return mb.ToMesh("clash general");
        }
        static readonly Dictionary<string, Color> HORSES = new Dictionary<string, Color>
        {
            { "吕布", C(0.66f, 0.2f, 0.1f) }, { "关羽", C(0.6f, 0.22f, 0.12f) }, { "赵云", C(0.88f, 0.87f, 0.84f) }, { "公孙瓒", C(0.86f, 0.85f, 0.82f) },
            { "曹操", C(0.17f, 0.15f, 0.14f) }, { "张飞", C(0.16f, 0.14f, 0.13f) }, { "马超", C(0.85f, 0.84f, 0.8f) }, { "刘备", C(0.78f, 0.74f, 0.68f) },
        };
        static readonly Color[] HORSE_PAL = { C(0.5f, 0.3f, 0.18f), C(0.38f, 0.23f, 0.14f), C(0.62f, 0.48f, 0.3f), C(0.22f, 0.17f, 0.14f), C(0.72f, 0.68f, 0.62f), C(0.55f, 0.36f, 0.22f) };
        public static Color HorseColor(string name)
        {
            Color c;
            if (name != null && HORSES.TryGetValue(name, out c)) return c;
            return HORSE_PAL[(int)(Math.Abs((long)ClashUtil.HashStr(name)) % HORSE_PAL.Length)];
        }
        public static string WeaponOf(General gen)
        {
            string n = gen != null ? gen.name : null;
            if (n == "吕布") return "halberd";
            if (n == "张飞" || n == "赵云" || n == "马超" || n == "公孙瓒") return "spear";
            return (gen != null && gen.war >= 85 && Math.Abs((long)ClashUtil.HashStr(n)) % 3 == 0) ? "spear" : "glaive";
        }
    }

    // ======================================================= 地形 --
    internal static class ClashTerrain
    {
        public const float ZC = -0.55f;                           // 两军纵深中心（three z）
        public const float RIVER_X = 1.05f, RIVER_W = 1.7f, RIVER_BANK = 1.5f, RIVER_BED = -0.42f, RIVER_SURF = -0.1f;
        public const float WALL_Z = -7.4f, GATE_X = 4.6f;        // 城墙正面（three z）与城门中心 x
        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Vector3 P(float x, float y, float z) { return new Vector3(x, y, -z); }
        static Color Shade(Color c, float k) { return Art.Shade(c, k); }
        static float Sstep(float a, float b, float x) { return ClashUtil.Sstep(a, b, x); }
        static float R(SeededRandom r) { return (float)r.NextDouble(); }
        public static bool Walled(string kind) { return kind == "gate" || kind == "castle" || kind == "wall"; }

        // 高度场（three 坐标 x、z）
        public sealed class Height
        {
            readonly string kind; readonly float ox, oz; readonly bool hilly, walled;
            public Height(string kind, int seed)
            {
                this.kind = kind;
                var r = new SeededRandom(seed);
                ox = (float)(r.NextDouble() * 200); oz = (float)(r.NextDouble() * 200);
                hilly = kind == "hill" || kind == "mountain";
                walled = Walled(kind);
            }
            float Pn(float x, float z, float f) { return Mathf.PerlinNoise(ox + x * f, oz + z * f) - 0.465f; }
            public float H(float x, float z)
            {
                float far = Sstep(9, 42, -z);
                float calm = 1 - 0.55f * (1 - Sstep(5, 10, Mathf.Abs(z - ZC))) * (1 - Sstep(7, 13, Mathf.Abs(x)));
                float y;
                if (hilly) y = (Pn(x, z, 0.1f) * 1.4f + Pn(x, z, 0.32f) * 0.22f) * calm + 0.7f * Sstep(-3, 7.5f, x) - 0.18f;
                else if (walled) y = Pn(x, z, 0.22f) * 0.07f;
                else y = (Pn(x, z, 0.15f) * 0.34f + Pn(x, z, 0.5f) * 0.05f) * calm;
                y += far * ((Pn(x, z, 0.03f) + 0.25f) * (hilly ? 14 : 7) + (hilly ? 2.5f : 0.6f));
                y += Sstep(16, 34, Mathf.Abs(x)) * (hilly ? 3 : 1.4f) * (0.6f + Pn(x, z, 0.06f));
                if (kind == "river")
                {
                    float d = Mathf.Abs(x - RIVER_X);
                    float bank = Sstep(RIVER_W, RIVER_W + RIVER_BANK, d);
                    y = ClashUtil.Lerp(RIVER_BED + Pn(x, z, 0.8f) * 0.04f, Mathf.Max(y * 0.6f, -0.02f) + 0.06f, bank);
                }
                return y;
            }
        }

        static Color GroundColor(string kind, float x, float z, float y, float n)
        {
            Color c;
            float trample = Mathf.Exp(-(x * x) / 10 - ((z - ZC) * (z - ZC)) / 7);
            switch (kind)
            {
                case "forest": c = C(0.3f, 0.47f, 0.25f); break;
                case "hill": c = C(0.53f, 0.6f, 0.34f); break;
                case "mountain": c = C(0.5f, 0.54f, 0.38f); break;
                case "gate": case "castle": case "wall": c = C(0.6f, 0.55f, 0.43f); break;
                default: c = C(0.44f, 0.64f, 0.32f); break;
            }
            if (Walled(kind))
            {
                var grass = C(0.47f, 0.6f, 0.35f);
                c = Color.Lerp(c, grass, Sstep(3, 12, Mathf.Abs(z - 2.5f)) * 0.6f + n * 0.1f);
                if (kind != "wall")
                {
                    // 通往城门的夯土大道（两道车辙）与门前石板
                    float d = Mathf.Abs(x - GATE_X), w = 1.45f + Mathf.Max(0, z - WALL_Z) * 0.07f;
                    float road = 1 - Sstep(w - 0.35f, w + 0.25f, d);
                    if (road > 0)
                    {
                        c = Color.Lerp(c, C(0.55f, 0.47f, 0.35f), road * 0.85f);
                        float rut = Mathf.Abs(d - w * 0.42f);
                        if (rut < 0.22f) c = Color.Lerp(c, C(0.45f, 0.38f, 0.28f), road * 0.55f);
                    }
                    if (z < WALL_Z + 2.4f && d < 2.3f) c = Color.Lerp(c, C(0.66f, 0.64f, 0.6f), 0.75f * (1 - Sstep(1.7f, 2.3f, d)));
                }
                // 墙根阴湿
                c = Color.Lerp(c, C(0.42f, 0.42f, 0.36f), (1 - Sstep(WALL_Z, WALL_Z + 1.2f, z)) * 0.35f);
            }
            else
            {
                c = Color.Lerp(c, C(0.58f, 0.52f, 0.37f), trample * 0.42f);
                if (n > 0.12f) c = Color.Lerp(c, Shade(c, kind == "forest" ? -0.12f : 0.08f), 0.6f);
                if (n < -0.14f) c = Color.Lerp(c, C(0.62f, 0.62f, 0.36f), 0.35f);
            }
            if (kind == "river")
            {
                float d = Mathf.Abs(x - RIVER_X);
                if (d < RIVER_W + RIVER_BANK + 0.5f) c = Color.Lerp(C(0.62f, 0.57f, 0.42f), C(0.5f, 0.46f, 0.34f), Sstep(RIVER_W + RIVER_BANK, RIVER_W * 0.4f, d));
            }
            if (kind == "mountain" && y > 3) c = Color.Lerp(c, C(0.58f, 0.57f, 0.54f), Sstep(3, 8, y));
            // 远处偏冷偏灰（雾会进一步融合）
            c = Color.Lerp(c, C(0.5f, 0.58f, 0.46f), Sstep(14, 45, -z) * 0.5f);
            return c;
        }
        // 中心 ±fine 内用 step，之外按 grow 递增
        static List<float> Axis(float a, float b, float step, float fine, float grow)
        {
            var outp = new List<float>();
            for (float x = 0; x <= b;) { outp.Add(x); x += Mathf.Abs(x) < fine ? step : step + (Mathf.Abs(x) - fine) * grow; }
            var neg = new List<float>();
            for (float x = -step; x >= a;) { neg.Add(x); x -= Mathf.Abs(x) < fine ? step : step + (Mathf.Abs(x) - fine) * grow; }
            neg.Reverse();
            neg.AddRange(outp);
            return neg;
        }

        static void Blade(MeshBuilder deco, float bx, float by, float bz, float h, float lean, float ang, Color col)
        {
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang), w = 0.045f;
            Vector3 a = P(bx - ca * w, by, bz - sa * w), b = P(bx + ca * w, by, bz + sa * w);
            Vector3 tip = P(bx + lean * -sa, by + h, bz + lean * ca);
            deco.Tri(a, tip, b, col); deco.Tri(a, b, tip, col);
        }
        static void Tree(MeshBuilder deco, Height H, SeededRandom rnd, float x, float z, float s, bool pine)
        {
            var col = Shade(C(0.24f, 0.46f, 0.25f), (R(rnd) - 0.5f) * 0.18f);
            Models.Tree(deco, P(x, H.H(x, z) - 0.05f, z), s, col, pine, (int)Mathf.Abs(Mathf.Floor(x * 13 + z * 7 + 0.5f)));
        }

        // 分段构建（每段 yield 一次，由场景按帧时间预算推进）；生成的物体挂在 root 下
        public static IEnumerator Build(Transform root, List<UnityEngine.Object> own, string kind, Height H, SeededRandom rnd, Color defColor)
        {
            var mb = new MeshBuilder();
            // 地面网格（three 坐标 x、z）
            var xs = Axis(-48, 48, 0.8f, 13, 0.25f);
            var zs = new List<float>();
            // 近处（z > 16）用越来越疏的行一直铺到镜头身后：竖屏 / 宽阵拉远机位时画面下缘不露底
            for (float z = 64; z > -95;) { zs.Add(z); z -= z > 16 ? 0.75f + (z - 16) * 0.3f : z > -9 ? 0.75f : 0.75f + (-9 - z) * 0.16f; }
            Func<float, float, float> pn = (x, z) => Mathf.PerlinNoise(x * 0.21f + 11.3f, z * 0.21f + 4.7f) - 0.465f;
            for (int j = 0; j < zs.Count - 1; j++)
            {
                if (j % 6 == 5) yield return null;
                for (int i = 0; i < xs.Count - 1; i++)
                {
                    float x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];   // z0 > z1（近 → 远）
                    Vector3 a = P(x0, H.H(x0, z0), z0), b = P(x0, H.H(x0, z1), z1), c = P(x1, H.H(x1, z1), z1), d = P(x1, H.H(x1, z0), z0);
                    float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, cy = (a.y + c.y) / 2;
                    var col = GroundColor(kind, cx, cz, cy, pn(cx, cz));
                    bool flip = ((i + j) & 1) == 0;
                    if (flip)
                    {
                        mb.Tri(a, b, c, Shade(col, (R(rnd) - 0.5f) * 0.07f));
                        mb.Tri(a, c, d, Shade(col, (R(rnd) - 0.5f) * 0.07f));
                    }
                    else
                    {
                        mb.Tri(a, b, d, Shade(col, (R(rnd) - 0.5f) * 0.07f));
                        mb.Tri(b, c, d, Shade(col, (R(rnd) - 0.5f) * 0.07f));
                    }
                }
            }
            yield return null;
            var deco = new MeshBuilder();
            bool walled = Walled(kind);
            // 草丛与小花
            var flowers = new[] { C(0.98f, 0.95f, 0.85f), C(1, 0.82f, 0.3f), C(0.95f, 0.55f, 0.6f), C(0.75f, 0.62f, 0.95f) };
            int tufts = walled ? 140 : kind == "river" ? 360 : 520;
            for (int k = 0; k < tufts; k++)
            {
                if (k % 120 == 119) yield return null;
                float x = (R(rnd) * 2 - 1) * 14, z = 7 - R(rnd) * (walled ? 9 : 20);
                if (walled && z < -6.4f) continue;
                if (kind == "river" && Mathf.Abs(x - RIVER_X) < RIVER_W + 0.6f) continue;
                float y = H.H(x, z);
                var gcol = kind == "forest" ? C(0.26f, 0.45f, 0.22f) : kind == "hill" ? C(0.5f, 0.58f, 0.3f) : C(0.36f, 0.58f, 0.26f);
                var col = Shade(gcol, (R(rnd) - 0.5f) * 0.25f);
                float s = 0.8f + R(rnd) * 0.7f;
                for (int j = 0; j < 3; j++)
                {
                    float bx = x + (R(rnd) - 0.5f) * 0.12f, by = y - 0.02f, bz = z + (R(rnd) - 0.5f) * 0.12f;
                    float bh = (0.16f + R(rnd) * 0.1f) * s, lean = (R(rnd) - 0.5f) * 0.14f, ang = R(rnd) * Mathf.PI;
                    Blade(deco, bx, by, bz, bh, lean, ang, Shade(col, j * 0.06f));
                }
                if (!walled && z < 2.5f && R(rnd) < 0.12f)
                {
                    var fc = flowers[rnd.Next(flowers.Length)];
                    deco.Box(P(x + 0.1f, y + 0.06f, z + 0.05f), V(0.05f, 0.035f, 0.05f), fc, 0.1f);
                }
            }
            // 石块
            int rocks = kind == "hill" || kind == "mountain" ? 26 : kind == "river" ? 22 : 8;
            for (int k = 0; k < rocks; k++)
            {
                float x = (R(rnd) * 2 - 1) * 16, z = 6 - R(rnd) * 22;
                if (kind == "river") { x = RIVER_X + (R(rnd) < 0.5f ? -1 : 1) * (RIVER_W + 0.4f + R(rnd) * 1.6f); }
                if (Mathf.Abs(x) < 6 && Mathf.Abs(z - ZC) < 3.5f) continue;
                if (z > 1.8f && Mathf.Abs(x) < 9) continue;          // 镜头前不放大石头
                if (walled && z < -6.5f) continue;
                float r = 0.18f + R(rnd) * (kind == "mountain" ? 0.7f : 0.4f);
                deco.Blob(P(x, H.H(x, z) + r * 0.25f, z), V(r * 1.2f, r * 0.75f, r), Shade(C(0.56f, 0.55f, 0.52f), (R(rnd) - 0.5f) * 0.15f), k + 3);
            }
            // 树
            if (kind == "forest")
            {
                for (int k = 0; k < 120; k++)
                {
                    if (k % 30 == 29) yield return null;
                    float x = (R(rnd) * 2 - 1) * 26, z = -4.4f - R(rnd) * 26;
                    float s = 2.2f + R(rnd) * 1.6f; bool pine = R(rnd) < 0.45f;
                    Tree(deco, H, rnd, x, z, s, pine);
                }
                // 两侧与近景的树（框住画面）
                var frame = new[,] { { -9.5f, 3.5f, 3.4f }, { 10.2f, 2.8f, 3.1f }, { -12.5f, 0.5f, 3.6f }, { 12.8f, -1.2f, 3.8f }, { -7.6f, -3.6f, 2.8f }, { 8.4f, -4.0f, 3.0f }, { -10.6f, 6.4f, 3.2f }, { 11.4f, 6.0f, 3.3f } };
                for (int k = 0; k < frame.GetLength(0); k++) { bool pine = R(rnd) < 0.4f; Tree(deco, H, rnd, frame[k, 0], frame[k, 1], frame[k, 2], pine); }
                // 落叶
                var leaves = new[] { C(0.62f, 0.42f, 0.18f), C(0.72f, 0.56f, 0.22f), C(0.5f, 0.36f, 0.16f) };
                for (int k = 0; k < 160; k++)
                {
                    float x = (R(rnd) * 2 - 1) * 12, z = 6 - R(rnd) * 14, y = H.H(x, z) + 0.012f;
                    var lc = leaves[rnd.Next(3)];
                    float s = 0.06f + R(rnd) * 0.05f, a = R(rnd) * 3;
                    deco.Tri(P(x + Mathf.Cos(a) * s, y, z + Mathf.Sin(a) * s), P(x - Mathf.Sin(a) * s, y, z + Mathf.Cos(a) * s), P(x - Mathf.Cos(a) * s, y, z - Mathf.Sin(a) * s), lc);
                }
            }
            else if (!walled)
            {
                int n = kind == "river" ? 28 : 22;
                for (int k = 0; k < n; k++)
                {
                    float x = (R(rnd) * 2 - 1) * 30, z = -9 - R(rnd) * 24;
                    if (kind == "river" && Mathf.Abs(x - RIVER_X) < 4) continue;
                    float s = 2 + R(rnd) * 1.4f; bool pine = R(rnd) < 0.4f;
                    Tree(deco, H, rnd, x, z, s, pine);
                }
                Tree(deco, H, rnd, -12.8f, -2.5f, 2.8f, false);
                Tree(deco, H, rnd, 13.6f, -3.4f, 3.0f, false);
            }
            // 河岸芦苇
            if (kind == "river")
            {
                for (int k = 0; k < 90; k++)
                {
                    float sideK = R(rnd) < 0.5f ? -1 : 1;
                    float x = RIVER_X + sideK * (RIVER_W + 0.9f + R(rnd) * 1.1f), z = 6 - R(rnd) * 22;
                    if (Mathf.Abs(z - ZC) < 3.6f && Mathf.Abs(x) < 5.5f) continue;
                    if (z > 2.2f) continue;
                    float y = H.H(x, z);
                    for (int j = 0; j < 4; j++)
                    {
                        float bx = x + (R(rnd) - 0.5f) * 0.2f, bz = z + (R(rnd) - 0.5f) * 0.2f, bh = 0.45f + R(rnd) * 0.35f;
                        float lean = (R(rnd) - 0.5f) * 0.2f, ang = R(rnd) * Mathf.PI;
                        Blade(deco, bx, y, bz, bh, lean, ang, Shade(C(0.58f, 0.6f, 0.32f), (R(rnd) - 0.5f) * 0.2f));
                    }
                }
            }
            yield return null;
            // 远山
            var far = new MeshBuilder();
            bool hilly = kind == "hill" || kind == "mountain";
            for (int k = 0; k < 9; k++)
            {
                float x = -70 + k * 17 + (R(rnd) - 0.5f) * 8, z = -62 - R(rnd) * 22;
                float h = (hilly ? 14 : 8) + R(rnd) * (hilly ? 14 : 7);
                float rad = 14 + R(rnd) * 10;
                far.Cone(P(x, -2, z), rad, h, 7, Shade(C(0.5f, 0.56f, 0.52f), (R(rnd) - 0.5f) * 0.1f));
                if (hilly && h > 18) far.Cone(P(x, -2 + h * 0.72f, z), (14 + 10 * 0.5f) * 0.28f, h * 0.28f, 7, C(0.93f, 0.94f, 0.96f));
            }
            // 城墙与城门
            if (walled) { yield return null; BuildWall(deco, kind, rnd, defColor, H); }
            yield return null;
            var lp = Art.LowPoly;
            var ground = mb.ToMesh("clash ground"); var decoM = deco.ToMesh("clash deco"); var farM = far.ToMesh("clash far");
            own.Add(ground); own.Add(decoM); own.Add(farM);
            var g0 = Art.MakeObject("Ground", ground, lp, root, false);
            Art.MakeObject("Deco", decoM, lp, root, true);
            var g2 = Art.MakeObject("Far", farM, lp, root, false);
            g2.GetComponent<MeshRenderer>().receiveShadows = false;
            g0.GetComponent<MeshRenderer>().receiveShadows = true;
            // 河水
            if (kind == "river")
            {
                var wm = new MeshBuilder();
                float x0 = RIVER_X - RIVER_W - RIVER_BANK - 0.2f, x1 = RIVER_X + RIVER_W + RIVER_BANK + 0.2f;
                const int nx = 10;
                var zz = new List<float>();
                for (float z = 64; z > -95;) { zz.Add(z); z -= z > 16 ? 1.2f + (z - 16) * 0.3f : z > -10 ? 1.2f : 1.2f + (-10 - z) * 0.25f; }
                for (int j = 0; j < zz.Count - 1; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        float xa = ClashUtil.Lerp(x0, x1, (float)i / nx), xb = ClashUtil.Lerp(x0, x1, (float)(i + 1) / nx);
                        float sa = ClashUtil.Clamp01(Mathf.Abs(xa - RIVER_X) / (RIVER_W + RIVER_BANK));
                        var ka = C(0.25f + sa * 0.6f, 0, 0);
                        float y = RIVER_SURF;
                        Vector3 a = P(xa, y, zz[j]), b = P(xa, y, zz[j + 1]), c = P(xb, y, zz[j + 1]), d = P(xb, y, zz[j]);
                        wm.Tri(a, b, c, ka); wm.Tri(a, c, d, ka);
                    }
                var wmesh = wm.ToMesh("clash water");
                own.Add(wmesh);
                var wmat = new Material(Art.Water);
                wmat.SetFloat("_Alpha", 0.66f);
                wmat.SetFloat("_Amp", 0.06f);
                own.Add(wmat);
                var w = Art.MakeObject("Water", wmesh, wmat, root, false);
                w.GetComponent<MeshRenderer>().receiveShadows = false;
            }
        }

        static void BuildWall(MeshBuilder mb, string kind, SeededRandom rnd, Color defColor, Height H)
        {
            const float Z = WALL_Z, TH = 1.4f, WH = 3.3f, X0 = -0.6f, X1 = 44;
            var stone = C(0.7f, 0.67f, 0.6f);
            float zc = Z - TH / 2;
            bool hasGate = kind != "wall";
            float gx = GATE_X;
            // 墙基与墙身
            mb.Box(P((X0 + X1) / 2, 0.15f, zc), V(X1 - X0, 0.5f, TH + 0.25f), Shade(stone, -0.12f));
            mb.Box(P((X0 + X1) / 2, WH / 2, zc), V(X1 - X0, WH, TH), stone);
            // 墙砖（正面上不同明暗的块）
            float fz = Z + 0.012f;
            for (float x = X0; x < X1 - 0.01f; x += 1.25f)
                for (int row = 0; row < 4; row++)
                {
                    float y0 = 0.42f + row * 0.7f, x0 = x + (row % 2) * 0.6f, x1 = Mathf.Min(X1, x0 + 1.2f);
                    if (x1 <= x0) continue;
                    if (hasGate && Mathf.Abs((x0 + x1) / 2 - gx) < 2.1f) continue;
                    var col = Shade(stone, (R(rnd) - 0.5f) * 0.12f - 0.02f);
                    mb.Quad(P(x0 + 0.04f, y0 + 0.04f, fz), P(x0 + 0.04f, y0 + 0.66f, fz), P(x1 - 0.04f, y0 + 0.66f, fz), P(x1 - 0.04f, y0 + 0.04f, fz), col);
                }
            // 垛口
            for (float x = X0 + 0.3f; x < X1; x += 0.8f)
            {
                if (hasGate && Mathf.Abs(x - gx) < 1.9f) continue;
                mb.Box(P(x, WH + 0.2f, Z - 0.15f), V(0.45f, 0.4f, 0.3f), Shade(stone, 0.05f));
            }
            mb.Box(P((X0 + X1) / 2, WH + 0.04f, Z - 0.12f), V(X1 - X0, 0.08f, 0.3f), Shade(stone, -0.08f));
            // 角楼（左端）
            mb.Box(P(X0, 2.05f, zc), V(1.9f, 4.1f, 1.9f), Shade(stone, -0.05f));
            mb.Box(P(X0, 4.45f, zc), V(1.5f, 0.7f, 1.5f), C(0.62f, 0.2f, 0.16f));
            mb.ChineseRoof(P(X0, 4.8f, zc), 2.5f, 2.5f, 0.9f, C(0.24f, 0.28f, 0.36f));
            // 城内屋顶
            for (int k = 0; k < 7; k++)
            {
                float x = 2 + k * 3.4f + R(rnd) * 1.2f, z = Z - 3.5f - R(rnd) * 5;
                mb.Box(P(x, 1.8f, z), V(2.2f, 3.6f, 1.6f), C(0.86f, 0.8f, 0.68f));
                mb.ChineseRoof(P(x, 3.6f, z), 3.0f, 2.2f, 1.0f + R(rnd) * 0.4f, C(0.27f, 0.3f, 0.37f));
            }
            // 墙根碎石
            for (int k = 0; k < 9; k++)
            {
                float x = X0 + 1.2f + R(rnd) * 16;
                if (hasGate && Mathf.Abs(x - gx) < 2.4f) continue;
                float r = 0.12f + R(rnd) * 0.16f;
                float zz = Z + 0.25f + R(rnd) * 0.4f;
                mb.Blob(P(x, H.H(x, Z + 0.35f) + r * 0.3f, zz), V(r * 1.3f, r * 0.8f, r), Shade(stone, -0.12f + (R(rnd) - 0.5f) * 0.1f), k + 11);
            }
            // 拒马：横木上交叉的削尖木桩（城门两侧、墙前）
            var stake = Shade(ClashArt.Wood, 0.08f);
            Action<float, float, float> juma = (cx, cz, len) =>
            {
                float y0 = H.H(cx, cz);
                ClashArt.Beam(mb, P(cx - len / 2, y0 + 0.36f, cz), P(cx + len / 2, y0 + 0.36f, cz), 0.1f, 0.1f, Shade(ClashArt.Wood, -0.08f));
                int n = Mathf.Max(2, Mathf.FloorToInt(len / 0.55f + 0.5f));
                for (int i = 0; i < n; i++)
                {
                    float x = cx - len / 2 + 0.2f + (len - 0.4f) * ((float)i / (n - 1));
                    ClashArt.Beam(mb, P(x, y0 + 0.02f, cz - 0.42f), P(x, y0 + 0.78f, cz + 0.4f), 0.06f, 0.012f, stake);
                    ClashArt.Beam(mb, P(x + 0.04f, y0 + 0.02f, cz + 0.42f), P(x + 0.04f, y0 + 0.78f, cz - 0.4f), 0.06f, 0.012f, stake);
                }
            };
            var jx = hasGate ? new[] { gx - 3.3f, gx + 3.3f, gx + 8.6f } : new[] { 1.4f, 6.8f };
            foreach (var x in jx) juma(x, Z + 1.75f + (R(rnd) - 0.5f) * 0.3f, 1.9f);
            // 倚墙的云梯（攻城痕迹）
            {
                float lx = hasGate ? gx + 6.2f : 9.5f, zb = Z + 1.25f, zt = Z + 0.12f, top = WH - 0.2f;
                foreach (var s in new[] { -0.24f, 0.24f }) ClashArt.Beam(mb, P(lx + s, H.H(lx, zb) + 0.02f, zb), P(lx + s, top, zt), 0.07f, 0.06f, ClashArt.Wood);
                for (int i = 1; i < 8; i++)
                {
                    float u = i / 8f;
                    float yy = ClashUtil.Lerp(H.H(lx, zb), top, u), zz = ClashUtil.Lerp(zb, zt, u);
                    ClashArt.Beam(mb, P(lx - 0.24f, yy, zz), P(lx + 0.24f, yy, zz), 0.045f, 0.045f, Shade(ClashArt.Wood, 0.05f));
                }
            }
            // 城头旗帜
            for (int k = 0; k < 6; k++)
            {
                float x = 1.6f + k * 2.6f;
                if (hasGate && Mathf.Abs(x - gx) < 1.9f) continue;
                mb.Flag(P(x, WH + 0.1f, Z - 0.45f), 1.3f, 0.55f, 0.4f, defColor);
            }
            if (!hasGate) return;
            // 城门：门洞、门扇与门钉、城门楼
            var red = C(0.62f, 0.2f, 0.16f);
            mb.Box(P(gx, 1.85f, zc + 0.12f), V(3.6f, 3.7f, TH + 0.5f), Shade(stone, -0.04f));
            float fz2 = Z + 0.38f;
            mb.Quad(P(gx - 0.75f, 0.05f, fz2), P(gx - 0.75f, 1.75f, fz2), P(gx + 0.75f, 1.75f, fz2), P(gx + 0.75f, 0.05f, fz2), C(0.12f, 0.1f, 0.09f));
            // 拱顶（三角形近似）
            for (int i = 0; i < 6; i++)
            {
                float a0 = Mathf.PI * i / 6, a1 = Mathf.PI * (i + 1) / 6;
                mb.Tri(P(gx, 1.75f, fz2), P(gx + Mathf.Cos(a1) * 0.75f, 1.75f + Mathf.Sin(a1) * 0.45f, fz2), P(gx + Mathf.Cos(a0) * 0.75f, 1.75f + Mathf.Sin(a0) * 0.45f, fz2), C(0.12f, 0.1f, 0.09f));
            }
            foreach (var sx in new[] { -1, 1 })
            {
                mb.Box(P(gx + sx * 0.37f, 0.95f, fz2 + 0.03f), V(0.68f, 1.8f, 0.06f), C(0.4f, 0.23f, 0.12f));
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++)
                        mb.Box(P(gx + sx * (0.15f + c * 0.19f), 0.3f + r * 0.42f, fz2 + 0.07f), V(0.05f, 0.05f, 0.03f), ClashArt.Gold);
            }
            mb.Box(P(gx, 3.8f, zc + 0.12f), V(3.3f, 0.2f, TH + 0.3f), Shade(stone, 0.04f));
            mb.Box(P(gx, 4.4f, zc), V(2.9f, 1.0f, 1.3f), red);
            for (int i = -2; i <= 2; i++) mb.Box(P(gx + i * 0.68f, 4.4f, Z + 0.06f), V(0.12f, 1.0f, 0.1f), Shade(red, -0.25f));
            mb.Box(P(gx, 4.75f, Z + 0.07f), V(1.1f, 0.34f, 0.04f), C(0.12f, 0.1f, 0.09f));
            mb.Box(P(gx, 4.75f, Z + 0.09f), V(0.9f, 0.24f, 0.02f), ClashArt.Gold);
            // 本城：金顶城楼 + 守方大旗（不再叠第二层楼——宽屏 / 手机上第二层会顶进上方 HUD；轮廓与城门同高）
            bool castle = kind == "castle";
            mb.ChineseRoof(P(gx, 4.9f, zc), 4.0f, 2.2f, 1.15f, castle ? C(0.85f, 0.65f, 0.2f) : C(0.22f, 0.26f, 0.34f));
            if (castle)
            {
                mb.Flag(P(gx + 1.55f, 3.9f, Z), 1.55f, 0.8f, 0.52f, defColor);
                mb.Flag(P(gx - 1.55f, 3.9f, Z), 1.55f, 0.8f, 0.52f, defColor);
            }
        }
    }

    // ======================================================= 旗帜 --
    internal static class ClashBanner
    {
        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static RGBA Rgba(Color c, float a = 1) { return new RGBA(c.r, c.g, c.b, a); }
        // 旗面贴图：势力色底、犬牙边、中央圆徽与姓氏（accent：识别色，近色对阵时给出，用作犬牙边与内框）
        public static Texture Make(string glyph, Color baseC, bool big, Color? accent, List<UnityEngine.Object> own)
        {
            int W = 128, H = big ? 168 : 104;
            float lum = ClashUtil.Luma(baseC);
            float tooth = big ? 12 : 10;
            Color edge = accent ?? (lum > 0.62f ? Art.Shade(baseC, -0.55f) : (lum < 0.25f ? C(0.88f, 0.72f, 0.32f) : Art.Shade(baseC, -0.45f)));
            var r = new Raster(W, H);
            // 犬牙边（左、右、下三边），颜色与旗面成对比
            r.FillStyle = Paint.Solid(Rgba(edge));
            r.BeginPath();
            float tw = tooth * 1.4f;
            for (float x = 0; x < W; x += tw) { r.MoveTo(x, H - tooth); r.LineTo(x + tw / 2, H); r.LineTo(x + tw, H - tooth); }
            for (float y = 0; y < H - tooth; y += tw)
            {
                r.MoveTo(tooth, y); r.LineTo(0, y + tw / 2); r.LineTo(tooth, y + tw);
                r.MoveTo(W - tooth, y); r.LineTo(W, y + tw / 2); r.LineTo(W - tooth, y + tw);
            }
            r.Fill();
            // 旗面
            var grd = Paint.Linear(0, 0, W, H);
            grd.AddStop(0, Rgba(Art.Shade(baseC, 0.18f)));
            grd.AddStop(0.55, Rgba(baseC));
            grd.AddStop(1, Rgba(Art.Shade(baseC, -0.22f)));
            r.FillStyle = grd;
            r.FillRect(tooth - 1, 0, W - 2 * tooth + 2, H - tooth + 1);
            r.StrokeStyle = Paint.Solid(Rgba(edge));
            r.LineWidth = 3;
            r.BeginPath(); r.Rect(tooth + 4, 5, W - 2 * tooth - 8, H - tooth - 10); r.Stroke();
            // 圆徽
            float cx = W / 2f, cy = (H - tooth) / 2, rad = Mathf.Min(W - 2 * tooth, H - tooth) * (big ? 0.36f : 0.38f);
            if (big)
            {
                r.FillColor = "#f4ecd6";
                r.BeginPath(); r.Arc(cx, cy, rad, 0, Math.PI * 2); r.Fill();
                r.StrokeStyle = Paint.Solid(Rgba(edge)); r.LineWidth = 3; r.Stroke();
            }
            var baseTex = r.ToTexture(false);
            // 姓氏：用楷体字形画进 RenderTexture（Raster 不含文字）
            Color ink = big ? Art.Hex("#17130f") : (lum > 0.62f ? Art.Hex("#1d1a17") : Art.Hex("#f7efdc"));
            int fs = Mathf.Max(8, Mathf.RoundToInt(rad * (big ? 1.45f : 1.55f)));
            RenderTexture rt = null;
            try
            {
                rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = false, name = "clash banner", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                rt.Create();
                var prev = RenderTexture.active;
                Graphics.Blit(baseTex, rt);
                DrawGlyph(rt, glyph, fs, ink, cx, cy + rad * 0.06f, W, H);
                RenderTexture.active = prev;
                rt.GenerateMips();
                UnityEngine.Object.Destroy(baseTex);
                own.Add(rt);
                return rt;
            }
            catch (Exception e)
            {
                Debug.LogWarning("clash banner glyph: " + e.Message);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                own.Add(baseTex);
                return baseTex;
            }
        }
        // 在 RenderTexture 上居中画一个字（canvas 坐标：左上为原点；cx、cy 为字心）
        static void DrawGlyph(RenderTexture rt, string glyph, int fs, Color ink, float cx, float cy, int W, int H)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            var font = ClashUtil.Kai;
            if (font == null || font.material == null) return;
            font.RequestCharactersInTexture(glyph, fs, FontStyle.Bold);
            CharacterInfo ci;
            if (!font.GetCharacterInfo(glyph[0], out ci, fs, FontStyle.Bold)) return;
            float gw = ci.maxX - ci.minX, gh = ci.maxY - ci.minY;
            float x0 = cx - ci.advance / 2f + ci.minX, x1 = x0 + gw;
            float yc = H - cy;                                       // GL 像素坐标：左下为原点
            float y0 = yc - gh / 2, y1 = yc + gh / 2;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, W, 0, H);
            font.material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(ink);
            GL.TexCoord(ci.uvBottomLeft); GL.Vertex3(x0, y0, 0);
            GL.TexCoord(ci.uvTopLeft); GL.Vertex3(x0, y1, 0);
            GL.TexCoord(ci.uvTopRight); GL.Vertex3(x1, y1, 0);
            GL.TexCoord(ci.uvBottomRight); GL.Vertex3(x1, y0, 0);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }
    }

    // 会飘动的旗面：局部（three）坐标里位于 y-z 平面，旗杆边在 z = 0，向 +z（身后）展开、自 y = 0 向下垂
    internal sealed class ClashFlag
    {
        public readonly Mesh mesh;
        public readonly Material mat;
        public Matrix4x4 m3 = Matrix4x4.identity;   // three 空间的矩阵（每帧由姿态写入）
        public float k = 1;                         // 风力（0 = 下垂，1 = 正常飘扬，>1 疾驰）
        readonly float w, h, seed; readonly bool big;
        readonly int nx, ny;
        readonly Vector3[] pos; readonly Color[] col;
        Vector3[] nrm;
        public ClashFlag(Material mat, float w, float h, bool flipU, float seed, bool big)
        {
            this.w = w; this.h = h; this.seed = seed; this.big = big; this.mat = mat;
            nx = 8; ny = big ? 6 : 4;
            int nv = (nx + 1) * (ny + 1);
            pos = new Vector3[nv]; col = new Color[nv];
            var uv = new Vector2[nv];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    int q = j * (nx + 1) + i;
                    uv[q] = new Vector2(flipU ? 1 - (float)i / nx : (float)i / nx, 1 - (float)j / ny);
                    col[q] = Color.white;
                }
            var idx = new List<int>();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
                }
            mesh = new Mesh { name = "clash flag" };
            mesh.MarkDynamic();
            mesh.vertices = pos;
            mesh.uv = uv;
            mesh.colors = col;
            mesh.SetTriangles(idx, 0);
            Wave(0, 1);
        }
        public void Wave(float t, float kk)
        {
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    int q = j * (nx + 1) + i;
                    float u = (float)i / nx, v = (float)j / ny;
                    float amp = u * (big ? 0.3f + 0.7f * v : 1);
                    float ph = t * (6.5f + kk * 3) - u * 5.2f - v * 0.8f + seed;
                    float droop = (1 - Mathf.Min(1, kk)) * u;
                    float x = (Mathf.Sin(ph) * 0.085f + Mathf.Sin(ph * 1.7f + 1.3f) * 0.03f) * w * amp * (0.4f + 0.6f * Mathf.Min(1.4f, kk));
                    float y = -v * h - droop * w * 0.55f - u * w * 0.05f;
                    float z = u * w * (1 - droop * 0.45f) * (1 - 0.06f * Mathf.Abs(Mathf.Sin(ph)));
                    pos[q] = new Vector3(x, y, -z);          // Unity 局部坐标
                }
            mesh.vertices = pos;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            nrm = mesh.normals;
        }
        // 旗面用无光照材质：按太阳方向把明暗写进顶点色（近似主场景的半兰伯特）
        public void Light(Matrix4x4 worldU, Vector3 toLight)
        {
            if (nrm == null) return;
            for (int i = 0; i < nrm.Length; i++)
            {
                var n = worldU.MultiplyVector(nrm[i]).normalized;
                float ndl = Mathf.Abs(Vector3.Dot(n, toLight)) * 0.5f + 0.5f;
                float c = Mathf.Min(1f, 0.5f + 0.62f * ndl * ndl);
                col[i] = new Color(c, c, c, 1);
            }
            mesh.colors = col;
        }
    }

    // ======================================================= 粒子 --
    internal sealed class ClashParticle
    {
        public float x, y, z, vx, vy, vz, age, life, size, sizeEnd, r, g, b, r1, g1, b1, a, grav, drag, fadeIn, ac;
        public float floor = float.NaN;
    }
    internal sealed class ClashPool
    {
        readonly int max;
        public readonly List<ClashParticle> parts = new List<ClashParticle>();
        public readonly Mesh mesh;
        public readonly Material mat;
        readonly Vector3[] v; readonly Color[] c; readonly Vector2[] uv; readonly int[] tri;
        int drawn = -1;
        public ClashPool(int max, Material mat)
        {
            this.max = max; this.mat = mat;
            v = new Vector3[max * 4]; c = new Color[max * 4]; uv = new Vector2[max * 4]; tri = new int[max * 6];
            for (int i = 0; i < max; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(0, 1); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(1, 0);
            }
            mesh = new Mesh { name = "clash particles" };
            mesh.MarkDynamic();
            mesh.vertices = v; mesh.uv = uv; mesh.colors = c;
            mesh.triangles = tri;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
        }
        public void Add(ClashParticle p) { if (parts.Count >= max) parts.RemoveAt(0); parts.Add(p); }
        public void Step(float dt)
        {
            int n = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                p.age += dt;
                if (p.age >= p.life) continue;
                p.vy -= p.grav * 9.81f * dt;
                if (p.drag != 0) { float k = Mathf.Max(0, 1 - p.drag * dt); p.vx *= k; p.vy *= k; p.vz *= k; }
                p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
                if (!float.IsNaN(p.floor) && p.y < p.floor) { p.y = p.floor; p.vy *= -0.2f; p.vx *= 0.5f; p.vz *= 0.5f; }
                parts[n++] = p;
            }
            if (n < parts.Count) parts.RemoveRange(n, parts.Count - n);
        }
        // 写网格：面向相机的方片（right / up 为相机的世界方向；粒子位置为 three 坐标）
        public void Build(Vector3 right, Vector3 up)
        {
            int n = parts.Count;
            for (int i = 0; i < n; i++)
            {
                var p = parts[i];
                float t = p.age / p.life;
                float fade = p.fadeIn > 0 ? Mathf.Min(1, t / p.fadeIn) : 1;
                var col = new Color(p.r + (p.r1 - p.r) * t, p.g + (p.g1 - p.g) * t, p.b + (p.b1 - p.b) * t,
                    p.a * (p.ac == 1 ? 1 - t : 1 - Mathf.Pow(t, p.ac)) * fade);
                float s = p.size * (1 + (p.sizeEnd - 1) * t) * 0.5f;
                var ctr = new Vector3(p.x, p.y, -p.z);
                Vector3 rx = right * s, uy = up * s;
                v[i * 4] = ctr - rx - uy; v[i * 4 + 1] = ctr - rx + uy; v[i * 4 + 2] = ctr + rx + uy; v[i * 4 + 3] = ctr + rx - uy;
                c[i * 4] = c[i * 4 + 1] = c[i * 4 + 2] = c[i * 4 + 3] = col;
            }
            if (n != drawn)
            {
                for (int i = 0; i < max; i++)
                {
                    int b = i * 4;
                    if (i < n) { tri[i * 6] = b; tri[i * 6 + 1] = b + 1; tri[i * 6 + 2] = b + 2; tri[i * 6 + 3] = b; tri[i * 6 + 4] = b + 2; tri[i * 6 + 5] = b + 3; }
                    else { tri[i * 6] = tri[i * 6 + 1] = tri[i * 6 + 2] = tri[i * 6 + 3] = tri[i * 6 + 4] = tri[i * 6 + 5] = 0; }
                }
            }
            mesh.vertices = v; mesh.colors = c;
            if (n != drawn) { mesh.triangles = tri; drawn = n; }
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000);
        }
        public int Count { get { return parts.Count; } }
        public void Clear() { parts.Clear(); }
    }

    // ======================================================= 场景 --
    internal sealed class ClashInfo
    {
        public General gen;
        public string name;
        public int side;
        public Color color;
        public int before, after, loss;
        public int formation;
        public Color? accent;          // 识别色，见 PickAccents
        public string culture;
    }
    internal sealed class ClashSoldier
    {
        public string role;            // inf 步兵 / arch 弓手 / flag 旗手
        public float f, l; public int file, si;
        public float x0, z0, ed, cd, rd, push;
        public float w1, p1, w2, p2, w3, p3, w4, p4, wt, pt, p0;
        public float fallT = float.PositiveInfinity; public string fallKind = "melee"; public float fallDir, kb;
        public readonly List<float> hits = new List<float>();
        public float shotDelay;
        public ClashFlag flag;
        // 每帧的姿态（three 空间矩阵）与颜色
        public Matrix4x4 mBody, mLeg0, mLeg1, mWeapon; public float tint = 1;
    }
    internal sealed class ClashGeneral
    {
        public float x0, z0, ed, p0;
        public Color horse;
        public Mesh mesh;
        public ClashFlag flag;
        public Matrix4x4 m3;
        public readonly Matrix4x4[] legs = new Matrix4x4[4];
    }
    internal sealed class ClashArmy
    {
        public int si, dir, n;
        public ClashInfo info;
        public List<ClashSoldier> soldiers;
        public ClashGeneral general;
        public float xFront, minAll;
        public bool routed, brace, cheer;
        public float chargeDX, genDX, volleyT;
        public int fallen;
        public Mesh infMesh, archMesh, legMesh;
    }
    internal sealed class ClashShot
    {
        public ClashArmy S, E; public ClashSoldier archer, target;
        public float t0, T, land, apex;
        public Vector3 p0, p1;
        public bool hit, spec, ready, water, visible;
        public Matrix4x4 m3;
    }
    internal sealed class ClashChunk { public int si; public float t; public int amount; public string kind; }
    internal sealed class ClashFx { public Mesh mesh; public Material mat; public float t0, life; public Action<float> fn; public Matrix4x4 m3 = Matrix4x4.identity; public bool hit; }
    // 粒子发射参数（JS emit 的 o）
    internal sealed class ClashEmit
    {
        public Color color; public Color? color2, colorEnd;
        public float mix = 0.35f, alpha = 1, life, speed, size, sizeEnd = 0.3f, gravity, radius, lifeVar, speedVar, sizeVar, drag, fadeIn, alphaCurve = 1, vx, vy;
        public float jitter = 0.25f;
        public bool up;
        public Vector3? box;
        public float floor = float.NaN;
    }

    internal sealed class ClashScene
    {
        // ------------------------------------------------------------ 时间轴 --
        // 单位：秒（speed = 1）。接敌后有一小段慢镜头（SlowW 秒内以 SlowK 倍速推进）。
        public const float March1 = 0.62f, Aim = 0.34f, Flight = 0.6f, Charge0 = 1.06f, Contact = 1.66f, SlowW = 0.16f, SlowK = 0.42f,
            Melee1 = 2.56f, Regroup1 = 3.08f, End = 3.34f;
        static readonly float[] Volley = { 0.46f, 0.6f };
        public const float FADE_IN = 0.2f, FADE_OUT = 0.2f, SKIP_HOLD = 0.4f;
        const float ENTER_DX = 2.8f, ZC = ClashTerrain.ZC, FRONT = 2.05f, SP_F = 0.54f, SP_L = 0.62f, HIP = 0.36f;
        static readonly Vector3 GRIP = new Vector3(0.2f, 0.5f, -0.2f);   // three 局部坐标：右手握点
        public static readonly Color FOG = new Color(0.86f, 0.84f, 0.78f);
        public static readonly string[] FORM_NAMES = { "方圆", "长蛇", "鱼鳞", "鹤翼", "偃月", "锋矢", "雁行", "衡轭" };
        public static readonly Dictionary<string, string> KIND_NAME = new Dictionary<string, string>
        { { "plain", "平原" }, { "forest", "森林" }, { "hill", "山丘" }, { "mountain", "山岳" }, { "river", "河川" }, { "wall", "城墙" }, { "gate", "城门" }, { "castle", "本城" } };
        static readonly string[] NANMAN = { "孟获", "孟优", "祝融", "兀突骨", "木鹿", "朵思", "带来" };

        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static float Seg(float t, float a, float b) { return ClashUtil.Seg(t, a, b); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        static float Clamp(float v, float a, float b) { return ClashUtil.Clamp(v, a, b); }
        static int JsRound(float v) { return (int)Mathf.Floor(v + 0.5f); }
        // Math.round 的 double 版（与网页版同精度：k * 0.45 等在 .5 处不因 float 误差取错）
        static int JsRoundD(double v) { return (int)Math.Floor(v + 0.5); }
        float Rn() { return (float)rnd.NextDouble(); }

        // ------------------------------------------------------------ 状态 --
        public readonly string kind;
        public readonly ClashInfo A, D;
        public readonly ClashSpecial special;
        public readonly string specialKind;
        public readonly int playerSide;            // -1 = 无
        public readonly float optSpeed;
        public float speed;
        readonly SeededRandom rnd;
        readonly int seed;
        public readonly bool recolored;
        public float t, real, tPrev;
        public string state = "build";             // build | run | hold | out | done
        public bool Skipped, Finished, paused, cutDone, built, shown;
        float shake;
        int evIdx;
        float holdUntil, holdWall, buildStart, showReal;
        ClashTerrain.Height h;
        public ClashArmy[] armies;
        float xc, fit = 1;
        List<ClashShot> arrows;
        List<ClashChunk> chunks;
        List<KeyValuePair<float, Action>> events;
        readonly List<ClashFx> temp = new List<ClashFx>();
        ClashPool dust, fx, glow;
        Mesh spearMesh, bowMesh, horseLegMesh, arrowMesh;
        Color arrowGlow = Color.white;
        readonly List<UnityEngine.Object> own = new List<UnityEngine.Object>();
        readonly List<ClashFlag> flags = new List<ClashFlag>();
        GameObject root;
        public Camera cam;
        ClashRunner runner;
        public MonoBehaviour runnerHost { get { return runner; } }
        public ClashHud hud;
        IEnumerator buildIt;
        MaterialPropertyBlock mpb;
        bool disposed;
        int lastW, lastH;
        // 播放期间改动、结束时还原的全局状态
        bool fogSaved, fog0; FogMode fogMode0; Color fogColor0; float fogStart0, fogEnd0, shadowDist0;
        readonly List<Camera> camsOff = new List<Camera>();
        readonly List<KeyValuePair<CanvasGroup, bool>> groups = new List<KeyValuePair<CanvasGroup, bool>>();
        readonly List<float> groupAlpha = new List<float>();
        readonly List<bool> groupBlocks = new List<bool>();
        readonly List<bool> groupInter = new List<bool>();
        bool rigSaved, rigInput0;

        public ClashScene(ClashSide a, ClashSide d, Terrain terrain, ClashSpecial sp, int playerSide, float spd)
        {
            kind = Clash.TerrainKind(terrain);
            A = Norm(a, 0);
            D = Norm(d, 1);
            float d0 = ColorDist(A.color, D.color);
            recolored = SeparateColors(A, D);
            if (d0 < ACC_DIST) PickAccents(A, D);
            if (sp != null && !string.IsNullOrEmpty(sp.name))
            {
                special = sp;
                specialKind = SpecKind(sp);
            }
            this.playerSide = playerSide == 0 || playerSide == 1 ? playerSide : -1;
            optSpeed = spd > 0 ? spd : 1;
            speed = Mathf.Max(0.1f, Clash.Speed * optSpeed);
            seed = ClashUtil.HashStr(A.name + "|" + D.name + "|" + A.before + "|" + D.before + "|" + kind);
            rnd = new SeededRandom(seed);
        }

        static ClashInfo Norm(ClashSide s, int dfltSide)
        {
            if (s == null) s = new ClashSide { side = dfltSide };
            var gen = s.gen ?? new General { name = "无名", war = 50 };
            int before = Mathf.Max(0, s.troopsBefore);
            int after = s.troopsAfter < 0 ? before : Mathf.Clamp(s.troopsAfter, 0, before);
            string nm = string.IsNullOrEmpty(gen.name) ? "无名" : gen.name;
            string cult = !string.IsNullOrEmpty(s.culture) ? s.culture : !string.IsNullOrEmpty(gen.culture) ? gen.culture
                : (NANMAN.Any(p => nm.StartsWith(p, StringComparison.Ordinal)) ? "nanman" : "han");
            return new ClashInfo
            {
                gen = gen, name = nm, side = s.side == 1 ? 1 : 0, color = new Color(s.color.r, s.color.g, s.color.b),
                before = before, after = after, loss = before - after,
                formation = Mathf.Clamp(s.formation >= 0 ? s.formation : gen.formation, 0, 7),
                culture = cult,
            };
        }
        public static string FormName(int i)
        {
            i = Mathf.Clamp(i, 0, 7);
            return i < Defs.Formations.Length && !string.IsNullOrEmpty(Defs.Formations[i].Name) ? Defs.Formations[i].Name : FORM_NAMES[i];
        }
        public static string Surname(ClashInfo info) { return string.IsNullOrEmpty(info.name) ? "兵" : info.name.Substring(0, 1); }

        // 必杀种类 → 画面特效：火（blaze / fx fire）、雷（storm / fx lightning）、箭雨（volley / fx arrow(s)），其余为斩光
        static string SpecKind(ClashSpecial sp)
        {
            string k = sp.kind ?? "", f = sp.fx ?? "";
            if (k == "blaze" || f == "fire") return "blaze";
            if (k == "storm" || f == "lightning" || f == "shock") return "storm";
            if (k == "volley" || f == "arrows" || f == "arrow") return "volley";
            return k.Length > 0 ? k : "smite";
        }

        // ---------------------------------------------------------- 配色 --
        // 两军势力色过于接近时（袁绍 #2f6db5 对曹操 #2c3d8f 等）拉开：较亮的一方再提亮，另一方压暗并转开色相
        static int[] Rgb255(Color c) { Color32 k = c; return new[] { (int)k.r, k.g, k.b }; }
        static float[] RgbHsl(int ri, int gi, int bi)
        {
            float r = ri / 255f, g = gi / 255f, b = bi / 255f;
            float mx = Mathf.Max(r, g, b), mn = Mathf.Min(r, g, b), l = (mx + mn) / 2;
            if (mx == mn) return new[] { 0, 0, l };
            float d = mx - mn, s = l > 0.5f ? d / (2 - mx - mn) : d / (mx + mn);
            float hh = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return new[] { hh / 6, s, l };
        }
        static Color HslColor(float hh, float s, float l)
        {
            hh = ((hh % 1) + 1) % 1;
            Func<float, int> f = n =>
            {
                float k = (n + hh * 12) % 12, a = s * Mathf.Min(l, 1 - l);
                return JsRound(255 * (l - a * Mathf.Max(-1, Mathf.Min(k - 3, Mathf.Min(9 - k, 1)))));
            };
            return new Color32((byte)Mathf.Clamp(f(0), 0, 255), (byte)Mathf.Clamp(f(8), 0, 255), (byte)Mathf.Clamp(f(4), 0, 255), 255);
        }
        static float ColorDist(Color x, Color y)
        {
            var a = Rgb255(x); var b = Rgb255(y);
            return Mathf.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        }
        const float SEP_DIST = 95, ACC_DIST = 130;
        static bool SeparateColors(ClashInfo A, ClashInfo D)
        {
            var a = Rgb255(A.color); var d = Rgb255(D.color);
            float dist = ColorDist(A.color, D.color);
            if (dist >= SEP_DIST) return false;
            float k = 0.6f + 0.4f * (1 - dist / SEP_DIST);
            float[] ha = RgbHsl(a[0], a[1], a[2]), hd = RgbHsl(d[0], d[1], d[2]);
            float[] up = ha[2] >= hd[2] ? ha : hd, dn = up == ha ? hd : ha;
            float dh = dn[0] - up[0];
            if (dh > 0.5f) dh -= 1; if (dh < -0.5f) dh += 1;
            up[2] = Mathf.Clamp(up[2] + 0.12f * k, 0, 0.72f);
            dn[2] = Mathf.Clamp(dn[2] - 0.1f * k, 0.14f, 1);
            dn[0] += (dh >= 0 ? 1 : -1) * 0.09f * k;
            dn[1] = Mathf.Max(dn[1], 0.35f * k);
            A.color = HslColor(ha[0], ha[1], ha[2]);
            D.color = HslColor(hd[0], hd[1], hd[2]);
            return true;
        }
        // 识别色：两军势力色相近（原始 RGB 距离 < ACC_DIST）时，各给一种与两军颜色都拉得开、彼此也不同的镶边色
        static readonly string[] ACCENTS = { "#f4ecd8", "#f0b42c", "#1b1815", "#d8342c", "#3cc0c8", "#9be05a" };
        static Color BestAccent(params Color[] avoid)
        {
            Color pick = Art.Hex(ACCENTS[0]); float sc = -1;
            foreach (var h in ACCENTS)
            {
                var c = Art.Hex(h);
                float m = float.MaxValue;
                foreach (var a in avoid) m = Mathf.Min(m, ColorDist(c, a));
                if (m > sc) { sc = m; pick = c; }
            }
            return pick;
        }
        static void PickAccents(ClashInfo A, ClashInfo D)
        {
            A.accent = BestAccent(A.color, D.color);
            D.accent = BestAccent(A.color, D.color, A.accent.Value);
        }

        // ---------------------------------------------------------- 流程 --
        public void Start()
        {
            mpb = new MaterialPropertyBlock();
            root = new GameObject("Clash");
            root.transform.position = Clash.Origin;
            runner = root.AddComponent<ClashRunner>();
            // 先淡入黑幕（战场照常绘制），黑幕下分帧搭建场景，再切到自己的相机
            hud = new ClashHud(this);
            hud.FadeBlack(FADE_IN * 0.75f);
            buildStart = Time.realtimeSinceStartup;
            buildIt = Build();
            runner.sc = this;
        }

        public void Tick(float dt)
        {
            if (disposed) return;
            try
            {
                if (state == "build")
                {
                    float f0 = Time.realtimeSinceStartup;
                    // 每帧最多约 10 毫秒
                    while (Time.realtimeSinceStartup - f0 < 0.010f)
                        if (!buildIt.MoveNext()) { buildIt = null; break; }
                    if (hud != null) hud.Update(dt, 0);       // 黑幕照常淡入
                    if (buildIt == null)
                    {
                        built = true;
                        Resize();
                        CameraPose(0);
                        state = "wait";
                    }
                    return;
                }
                if (state == "wait")
                {
                    if (hud != null) hud.Update(dt, 0);
                    if (Time.realtimeSinceStartup - buildStart >= FADE_IN * 0.75f) Show();
                    return;
                }
                if (Screen.width != lastW || Screen.height != lastH) Resize();
                // 跳过：空格 / 回车 / Esc（特写画面优先）
                if (Clash.cut == null && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape)))
                    Skip();
                Advance(dt);
                // 保险：画面长时间没有推进也会结束，免得战斗流程卡住
                if (state != "done" && Clash.FreezeAt < 0 && Time.realtimeSinceStartup - showReal > 60f) { state = "done"; Dispose(); }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Dispose();
            }
        }

        void Show()
        {
            // 自己的相机（深度高于其它相机，只拍本层）
            var cgo = new GameObject("ClashCamera");
            cgo.transform.SetParent(root.transform, false);
            cam = cgo.AddComponent<Camera>();
            float depth = 0;
            foreach (var c in Camera.allCameras) if (c != cam) depth = Mathf.Max(depth, c.depth);
            cam.depth = depth + 10;
            cam.cullingMask = 1 << Clash.Layer;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = FOG;
            cam.fieldOfView = 34;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 300;
            cam.allowMSAA = true;
            ClashUtil.SetLayer(root, Clash.Layer);
            CameraPose(t);
            // 其余相机暂停（全屏场景栈：只画最上层）
            foreach (var c in Camera.allCameras) if (c != cam && c.enabled) { c.enabled = false; camsOff.Add(c); }
            // 雾：本画面自己的远近
            fogSaved = true;
            fog0 = RenderSettings.fog; fogMode0 = RenderSettings.fogMode; fogColor0 = RenderSettings.fogColor;
            fogStart0 = RenderSettings.fogStartDistance; fogEnd0 = RenderSettings.fogEndDistance;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = FOG;
            RenderSettings.fogStartDistance = 34; RenderSettings.fogEndDistance = 135;
            // 阴影只需覆盖这条战场（网页版太阳阴影相机 ±15）：缩短阴影距离让士兵影子清晰
            shadowDist0 = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = 45;
            // 隐藏战场 HUD、世界标签与提示层；相机不再接收按键
            var layers = UIKit.Root != null ? new[] { UIKit.Screens, UIKit.LabelLayer, UIKit.ToastLayer } : new RectTransform[0];
            foreach (var layer in layers)
            {
                if (layer == null) continue;
                var g = layer.GetComponent<CanvasGroup>();
                bool added = g == null;
                if (added) g = layer.gameObject.AddComponent<CanvasGroup>();
                groups.Add(new KeyValuePair<CanvasGroup, bool>(g, added));
                groupAlpha.Add(g.alpha); groupBlocks.Add(g.blocksRaycasts); groupInter.Add(g.interactable);
                g.alpha = 0; g.blocksRaycasts = false; g.interactable = false;
            }
            // 空格 / 回车用于跳过：不让它们同时「提交」战场菜单里选中的按钮
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (Game.I != null && Game.I.Rig != null) { rigSaved = true; rigInput0 = Game.I.Rig.InputEnabled; Game.I.Rig.InputEnabled = false; }
            shown = true;
            showReal = Time.realtimeSinceStartup;
            state = "run";
            hud.FadeIn(FADE_IN);
        }

        // ---------------------------------------------------------- 构建 --
        IEnumerator Build()
        {
            h = new ClashTerrain.Height(kind, seed);
            var it = ClashTerrain.Build(root.transform, own, kind, h, new SeededRandom(seed ^ 0x5bd1e995), D.color);
            while (it.MoveNext()) yield return null;
            // 两军
            armies = new[] { BuildArmy(A, 0), BuildArmy(D, 1) };
            PlanCharge();
            PlanArrows();
            yield return null;
            var m = BuildMeshes();
            while (m.MoveNext()) yield return null;
            PlanCasualties();
            PlanEvents();
            // 粒子：扬尘（柔边）、溅水 / 火焰、发光
            var dustMat = Art.NewUnlit(Color.white, ClashTex.Dust); dustMat.renderQueue = 3011;
            var fxMat = Art.NewUnlit(Color.white, Art.SoftDot); fxMat.renderQueue = 3012;
            var glowMat = Art.NewAdditive(Color.white); glowMat.renderQueue = 3014;
            own.Add(dustMat); own.Add(fxMat); own.Add(glowMat);
            dust = new ClashPool(520, dustMat); fx = new ClashPool(420, fxMat); glow = new ClashPool(520, glowMat);
            own.Add(dust.mesh); own.Add(fx.mesh); own.Add(glow.mesh);
            Pose(0);
        }

        ClashArmy BuildArmy(ClashInfo info, int si)
        {
            int dir = si == 0 ? 1 : -1;
            int n = info.before > 0 ? Math.Max(6, Math.Min(28, JsRoundD(info.before / 300.0))) : 6;
            int nArch = Math.Max(2, Math.Min(6, JsRoundD(n * 0.22)));
            int nInf = n - nArch;
            var slots = FormationSlots(info.formation, nInf);
            float minF = 0;
            foreach (var sl in slots) minF = Mathf.Min(minF, sl.x);
            var soldiers = new List<ClashSoldier>();
            float xFront = -dir * FRONT;
            Func<string, float, float, int, ClashSoldier> mk = (role, f, l, file) =>
            {
                var s = new ClashSoldier { role = role, f = f, l = l, file = file, si = si };
                s.x0 = xFront + dir * f; s.z0 = ZC + l;
                s.ed = Rn() * 0.08f + (-f) * 0.012f;
                s.cd = Rn() * 0.07f + (-f) * 0.03f;
                s.rd = Rn() * 0.12f;
                s.w1 = 7 + Rn() * 5; s.p1 = Rn() * 6.3f; s.w2 = 13 + Rn() * 6; s.p2 = Rn() * 6.3f;
                s.w3 = 5 + Rn() * 4; s.p3 = Rn() * 6.3f; s.w4 = 6 + Rn() * 5; s.p4 = Rn() * 6.3f;
                s.wt = 9 + Rn() * 6; s.pt = Rn() * 6.3f; s.p0 = Rn() * 6.3f;
                s.fallDir = Rn() < 0.72f ? 1 : -1;
                s.shotDelay = Rn() * 0.1f;
                soldiers.Add(s);
                return s;
            };
            foreach (var sl in slots) mk("inf", sl.x, sl.y, (int)sl.z);
            // 弓手：步兵之后一到两排
            int aRows = nArch > 4 ? 2 : 1, per = Mathf.CeilToInt(nArch / (float)aRows);
            float minAll = minF;
            for (int k = 0; k < nArch; k++)
            {
                int row = k / per, cnt = Mathf.Min(per, nArch - row * per), i = k - row * per;
                float f = minF - 0.72f - row * 0.5f, l = (i - (cnt - 1) / 2f) * 0.82f + (row % 2) * 0.41f;
                mk("arch", f, l, 99);
                minAll = Mathf.Min(minAll, f);
            }
            // 旗手：步兵最后一排中最靠近中线的一到两名
            var rear = soldiers.Where(s => s.role == "inf").OrderBy(s => s.f).ThenBy(s => Mathf.Abs(s.l)).ToList();
            int nFlag = n >= 14 ? 2 : 1;
            var fl = new List<ClashSoldier>();
            foreach (var s in rear)
            {
                if (fl.Count >= nFlag) break;
                if (fl.Any(o => Mathf.Abs(o.l - s.l) < 1.2f)) continue;
                s.role = "flag"; fl.Add(s);
            }
            // 前两排冲进敌阵的深度（交错进入）
            foreach (var s in soldiers) if (s.role == "inf" && s.file <= 1) s.push = (s.file == 0 ? 0.18f : 0.06f) + Rn() * 0.32f;
            var general = new ClashGeneral { x0 = xFront + dir * (minAll - 1.2f), z0 = ZC - 0.3f, ed = 0.1f, p0 = Rn() * 6 };
            return new ClashArmy
            {
                si = si, dir = dir, info = info, n = n, soldiers = soldiers, general = general, xFront = xFront, minAll = minAll,
                routed = info.before > 0 && info.after <= 0,
                brace = info.formation == 0 || info.formation == 7,
            };
        }

        // 返回 K 个步兵的队形位置（x = f 向敌为正、前排为 0；y = l 纵深方向；z = file）
        static List<Vector3> FormationSlots(int fi, int K)
        {
            // 用 double 计算（同网页版）：前排序号按 Math.round 取整，float 误差会在 .5 处取错排
            const double spF = 0.54, spL = 0.62;     // 同 SP_F、SP_L（取 double 字面值）
            int R;
            switch (fi)
            {
                case 0: R = Math.Max(3, Math.Min(6, (int)Math.Floor(Math.Sqrt(K * 1.2) + 0.5))); break;   // 方圆
                case 1: R = K > 14 ? 3 : 2; break;                                       // 长蛇
                case 2: R = 5; break;                                                    // 鱼鳞
                case 3: R = 7; break;                                                    // 鹤翼
                case 4: R = 6; break;                                                    // 偃月
                case 5: R = 5; break;                                                    // 锋矢
                case 6: R = 6; break;                                                    // 雁行
                default: R = 5; break;                                                   // 衡轭
            }
            // 纵深（列数）有上限，免得后排与主将跑出画面
            R = Math.Max(R, (int)Math.Ceiling(K / (double)(fi == 1 ? 6 : 4)));
            R = Math.Max(1, Math.Min(Math.Min(R, K), 8));
            int F = (int)Math.Ceiling(K / (double)R);
            var fs = new List<double>(); var ls = new List<double>();
            for (int c = 0; c < F; c++)
            {
                int inFile = Math.Min(R, K - c * R);
                for (int r = 0; r < inFile; r++)
                {
                    double rr = r + (R - inFile) / 2.0;
                    double ln = R > 1 ? (rr / (R - 1)) * 2 - 1 : 0;
                    double l = (rr - (R - 1) / 2.0) * spL;
                    double f = -c * spF;
                    switch (fi)
                    {
                        case 0: f += (Math.Abs(ln) > 0.9 && (c == 0 || c == F - 1)) ? -0.18 : 0; break;
                        case 1: l += Math.Sin(c * 1.3) * 0.3; f *= 1.05; break;
                        case 2: f -= Math.Abs(ln) * 0.95; if (r % 2 == 1) f -= spF * 0.5; break;
                        case 3: f += (Math.Abs(ln) - 1) * 1.25; l *= 1.06; break;
                        case 4: f -= ln * ln * 1.15; break;
                        case 5: f -= Math.Abs(ln) * 1.8; l *= 0.85; break;
                        case 6: f += ln * 1.35; break;
                        default: if (c >= F / 2.0) { f -= 0.6; l += 0.31; } break;
                    }
                    fs.Add(f); ls.Add(l);
                }
            }
            double mx = double.NegativeInfinity;
            foreach (var f in fs) mx = Math.Max(mx, f);
            var outp = new List<Vector3>();
            for (int i = 0; i < fs.Count; i++)
            {
                double f = fs[i] - mx;
                // 前排序号：按离敌远近重新编号（file 0 = 最前）
                outp.Add(new Vector3((float)f, (float)ls[i], (float)Math.Floor(-f / spF + 0.5)));
            }
            return outp;
        }

        void PlanCharge()
        {
            ClashArmy L = armies[0], R = armies[1];
            // 守方结方圆 / 衡轭时原地坚守，攻方冲过整段距离
            float x = 0;
            if (R.brace && !L.brace) x = FRONT - 0.3f;
            else if (L.brace && !R.brace) x = -(FRONT - 0.3f);
            L.chargeDX = (x - 0.27f) - L.xFront;
            R.chargeDX = R.xFront - (x + 0.27f);
            foreach (var a in armies) a.genDX = Mathf.Min(0.9f, a.chargeDX * 0.4f);
            xc = x;
            L.cheer = R.routed && !L.routed; R.cheer = L.routed && !R.routed;
        }

        IEnumerator BuildMeshes()
        {
            foreach (var a in armies)
            {
                var team = a.info.color;
                a.infMesh = ClashArt.Body(team, a.info.culture, false, a.info.accent);
                a.archMesh = ClashArt.Body(team, a.info.culture, true, a.info.accent);
                a.legMesh = ClashArt.Leg(a.info.culture);     // 腿按文化（裤 / 裸腿 / 长靴 / 绑腿），每方一组
                own.Add(a.infMesh); own.Add(a.archMesh); own.Add(a.legMesh);
                yield return null;
            }
            spearMesh = ClashArt.Spear(); bowMesh = ClashArt.Bow(); horseLegMesh = ClashArt.HorseLeg(); arrowMesh = ClashArt.Arrow();
            own.Add(spearMesh); own.Add(bowMesh); own.Add(horseLegMesh); own.Add(arrowMesh);
            // 武将、帅旗与士兵旗
            foreach (var a in armies)
            {
                var g = a.general;
                g.horse = ClashArt.HorseColor(a.info.name);
                g.mesh = ClashArt.General(a.info.color, a.info.culture, g.horse, ClashArt.WeaponOf(a.info.gen), a.info.accent);
                own.Add(g.mesh);
                string glyph = Surname(a.info);
                var bigTex = ClashBanner.Make(glyph, a.info.color, true, a.info.accent, own);
                var smallTex = ClashBanner.Make(glyph, a.info.color, false, a.info.accent, own);
                var bigMat = Art.NewUnlit(Color.white, bigTex); bigMat.renderQueue = 3001;
                var smallMat = Art.NewUnlit(Color.white, smallTex); smallMat.renderQueue = 3001;
                own.Add(bigMat); own.Add(smallMat);
                g.flag = new ClashFlag(bigMat, 0.74f, 0.98f, a.dir > 0, a.si * 3.1f, true);
                flags.Add(g.flag); own.Add(g.flag.mesh);
                foreach (var s in a.soldiers) if (s.role == "flag")
                    {
                        s.flag = new ClashFlag(smallMat, 0.56f, 0.4f, a.dir > 0, Rn() * 6, false);
                        flags.Add(s.flag); own.Add(s.flag.mesh);
                    }
                yield return null;
            }
            // 箭（特殊箭雨染成必杀技颜色并发光）
            if (special != null)
            {
                var sc = special.color;
                arrowGlow = new Color(0.6f + sc.r * 1.6f, 0.6f + sc.g * 1.6f, 0.6f + sc.b * 1.6f);
            }
        }

        // 箭：每方一轮齐射
        void PlanArrows()
        {
            arrows = new List<ClashShot>();
            foreach (var S in armies)
            {
                var E = armies[1 - S.si];
                var archers = S.soldiers.Where(s => s.role == "arch").ToList();
                int baseN = (int)Clamp(archers.Count * 3, 8, 16);
                int n = baseN + (special != null && specialKind == "volley" && S.si == 0 ? 18 : 0);
                S.volleyT = Volley[S.si];
                for (int i = 0; i < n; i++)
                {
                    var a = archers[i % archers.Count];
                    bool spec = i >= baseN;
                    float t0 = S.volleyT + a.shotDelay + (spec ? 0.04f + Rn() * 0.16f : Rn() * 0.05f);
                    float T = Flight * (0.92f + Rn() * 0.16f);
                    arrows.Add(new ClashShot { S = S, E = E, archer = a, t0 = t0, T = T, land = t0 + T, spec = spec });
                }
            }
        }

        float SpecialT() { return Contact + 0.02f; }

        // 伤亡：按兵力损失比例决定倒下人数，分配给箭雨 / 必杀 / 混战
        void PlanCasualties()
        {
            chunks = new List<ClashChunk>();       // 兵力数字的扣减时刻
            foreach (var A in armies)
            {
                var info = A.info;
                double frac = info.before > 0 ? info.loss / (double)info.before : 0;
                int k = JsRoundD(A.n * Math.Min(frac, A.routed ? 0.6 : 0.9));
                if (A.routed) k = Math.Max(k, (int)Math.Ceiling(A.n * 0.45));
                if (info.loss > 0 && k == 0 && frac >= 0.035f) k = 1;
                if (!A.routed) k = Mathf.Min(k, A.n - 2);
                k = Mathf.Max(0, k);
                bool sp = special != null && A.si == 1;
                double shareSpec = sp ? 0.45 : 0, shareArrow = sp && specialKind == "volley" ? 0 : (sp ? 0.12 : 0.2);
                int kSpec = sp ? JsRoundD(k * 0.45) : 0;
                var incoming = arrows.Where(r => r.E == A && !r.spec).ToList();
                int kArrow = Math.Min(Math.Min(JsRoundD(k * shareArrow + 0.25), incoming.Count / 2), 3);
                if (k - kSpec - kArrow < 0) kArrow = Mathf.Max(0, k - kSpec);
                int kMelee = k - kSpec - kArrow;
                // 选人：箭伤落在中后排步兵 / 弓手，混战伤亡集中在前排
                var pool = A.soldiers.ToList();
                Func<Func<ClashSoldier, float>, ClashSoldier> pick = score =>
                {
                    int best = -1; float bs = float.NegativeInfinity;
                    for (int i = 0; i < pool.Count; i++) { float v = score(pool[i]) + Rn() * 0.9f; if (v > bs) { bs = v; best = i; } }
                    if (best < 0) return null;
                    var s = pool[best]; pool.RemoveAt(best); return s;
                };
                var melee = new List<ClashSoldier>(); var arrowC = new List<ClashSoldier>(); var specC = new List<ClashSoldier>();
                for (int i = 0; i < kSpec; i++) { var s = pick(x => (x.role == "inf" ? 2 : 0) - x.file * 0.5f); if (s != null) specC.Add(s); }
                for (int i = 0; i < kMelee; i++) { var s = pick(x => (x.role == "inf" ? 2.5f : x.role == "flag" ? -1 : 0) - x.file * 0.9f); if (s != null) melee.Add(s); }
                for (int i = 0; i < kArrow; i++) { var s = pick(x => (x.role == "flag" ? -3 : 0) + (x.file > 0 ? 1 : 0)); if (s != null) arrowC.Add(s); }
                // 箭伤：让对应的箭射中此人
                var spare = incoming.ToList();
                foreach (var s in arrowC)
                {
                    int ix = rnd.Next(Mathf.Max(1, spare.Count));
                    if (ix >= spare.Count) continue;
                    var r = spare[ix]; spare.RemoveAt(ix);
                    r.target = s; s.fallT = r.land; s.fallKind = "arrow";
                }
                // 必杀：volley 型由特殊箭射中，其余在接敌瞬间被震飞
                if (sp)
                {
                    var specArrows = arrows.Where(r => r.E == A && r.spec).ToList();
                    for (int i = 0; i < specC.Count; i++)
                    {
                        var s = specC[i];
                        if (specialKind == "volley" && specArrows.Count > 0)
                        {
                            var r = specArrows[i % specArrows.Count];
                            if (r.target == null) { r.target = s; s.fallT = r.land; } else s.fallT = r.land + 0.05f;
                            s.fallKind = "arrow";
                        }
                        else
                        {
                            s.fallT = SpecialT() + 0.02f + Rn() * 0.12f;
                            s.fallKind = "blast"; s.kb = 1.1f + Rn() * 1.1f; s.fallDir = 1;
                        }
                    }
                }
                float m0 = Contact + 0.08f, m1 = Melee1 - 0.2f;
                melee = melee.OrderBy(s => s.file).ToList();
                for (int i = 0; i < melee.Count; i++)
                {
                    var s = melee[i];
                    float u = (i + 0.3f + Rn() * 0.5f) / Mathf.Max(1, melee.Count);
                    s.fallT = Lerp(m0, m1, Mathf.Pow(u, 1.15f));
                    s.fallKind = "melee";
                }
                // 兵力扣减分段（总和 = loss）
                int L = info.loss;
                if (L > 0)
                {
                    var parts = new List<KeyValuePair<float, KeyValuePair<double, string>>>();   // t → (w, kind)
                    float arrowLand = incoming.Count > 0 ? incoming.Min(r => r.land) + 0.12f : Contact;
                    if (shareArrow > 0) parts.Add(Part(arrowLand, shareArrow, "arrow"));
                    if (sp)
                    {
                        float st;
                        if (specialKind == "volley")
                        {
                            var sa = arrows.Where(r => r.E == A && r.spec).ToList();
                            st = sa.Count > 0 ? sa.Min(r => r.land) + 0.1f : float.PositiveInfinity;
                        }
                        else st = SpecialT() + 0.08f;
                        parts.Add(Part(!float.IsInfinity(st) ? st : Contact, shareSpec + (specialKind == "volley" ? 0.12 : 0), "special"));
                    }
                    double rest = 1 - parts.Sum(p => p.Value.Key);
                    float lag = A.si * 0.09f;
                    parts.Add(Part(Contact + 0.3f + lag, rest * 0.55, "melee"));
                    parts.Add(Part(Contact + 0.68f + lag, rest * 0.45, "melee"));
                    parts = parts.OrderBy(p => p.Key).ToList();
                    int left = L;
                    for (int i = 0; i < parts.Count; i++)
                    {
                        int amt = i == parts.Count - 1 ? left : Math.Min(left, JsRoundD(L * parts[i].Value.Key));
                        left -= amt;
                        if (amt > 0) chunks.Add(new ClashChunk { si = A.si, t = parts[i].Key, amount = amt, kind = parts[i].Value.Value });
                    }
                }
                A.fallen = A.soldiers.Count(s => !float.IsInfinity(s.fallT));
            }
            chunks = chunks.OrderBy(c => c.t).ToList();
            // 非致命的受击闪光（前排混战）
            foreach (var A in armies)
                foreach (var s in A.soldiers)
                    if (s.role == "inf" && s.file <= 1)
                    {
                        int n = 1 + rnd.Next(3);
                        for (int i = 0; i < n; i++) s.hits.Add(Lerp(Contact + 0.05f, Melee1 - 0.1f, Rn()));
                    }
        }
        static KeyValuePair<float, KeyValuePair<double, string>> Part(float t, double w, string kind) { return new KeyValuePair<float, KeyValuePair<double, string>>(t, new KeyValuePair<double, string>(w, kind)); }

        void PlanEvents()
        {
            var ev = new List<KeyValuePair<float, Action>>();
            Action<float, Action> add = (tt, fn) => ev.Add(new KeyValuePair<float, Action>(tt, fn));
            add(0.02f, () => ClashUtil.Sfx("horn", 0.35f));
            add(0.12f, () => ClashUtil.Sfx("march", 0.35f));
            foreach (var r in arrows) { var rr = r; add(r.land, () => ArrowImpact(rr)); }
            add(Contact, OnContact);
            if (special != null) add(SpecialT(), SpecialImpact);
            foreach (var A in armies)
                foreach (var s in A.soldiers)
                    if (!float.IsInfinity(s.fallT)) { var aa = A; var ss = s; add(s.fallT, () => OnFall(aa, ss)); }
            foreach (var ch in chunks) { var cc = ch; add(ch.t, () => OnChunk(cc)); }
            for (float tt = Contact + 0.1f; tt < Melee1 - 0.05f; tt += 0.13f + Rn() * 0.08f)
                add(tt, () => ClashUtil.Sfx(Rn() < 0.6f ? "duel" : "hit", 0.22f + Rn() * 0.15f));
            foreach (var A in armies) if (A.routed) { var aa = A; add(Melee1 + 0.05f, () => OnRout(aa)); }
            events = ev.OrderBy(e => e.Key).ToList();
        }

        // ---------------------------------------------------------- 动作函数 --
        // 士兵在时刻 t 的位置（不含倒地后的位移）；jit = 混战抖动权重
        void SoldierXZ(ClashArmy A, ClashSoldier s, float tt, out float ox, out float oz, out float jit)
        {
            int dir = A.dir;
            float x = s.x0, z = s.z0;
            x -= dir * ENTER_DX * (1 - ClashUtil.Eo(Seg(tt, s.ed, March1 + s.ed * 0.5f)));
            if (s.role != "arch")
            {
                float uc = Seg(tt, Charge0 + s.cd, Contact + s.cd * 0.35f);
                x += dir * (A.chargeDX + s.push) * uc * uc;
            }
            jit = s.role == "arch" ? 0 : Seg(tt, Contact, Contact + 0.18f) * (1 - Seg(tt, Melee1, Melee1 + 0.25f));
            float engage = s.file <= 1 ? 1 : 0.4f;
            x += dir * jit * engage * (Mathf.Sin(tt * s.w1 + s.p1) * 0.13f + Mathf.Sin(tt * s.w2 + s.p2) * 0.06f);
            z += jit * Mathf.Sin(tt * s.w3 + s.p3) * 0.09f;
            if (A.routed)
            {
                float uf = Seg(tt, Melee1 + s.rd, Regroup1 + 0.25f);
                x -= dir * 8 * Mathf.Pow(uf, 1.4f);
            }
            else if (s.role != "arch")
            {
                x -= dir * 0.85f * ClashUtil.Eio(Seg(tt, Melee1 + s.rd, Regroup1 - 0.04f));
            }
            ox = x; oz = z;
        }
        void SoldierXZ(ClashArmy A, ClashSoldier s, float tt, out float ox, out float oz) { float j; SoldierXZ(A, s, tt, out ox, out oz, out j); }

        // 步态：相位（腿摆动）、幅度、身体前倾
        void Gait(ClashArmy A, ClashSoldier s, float tt, out float oph, out float oamp, out float olean)
        {
            var segs = new List<float[]>();
            segs.Add(new[] { s.ed, March1 + s.ed * 0.5f, 11, 0.5f, -0.05f });
            if (s.role != "arch")
            {
                segs.Add(new[] { Charge0 + s.cd, Contact + s.cd * 0.35f, 18, 0.75f, -0.3f });
                segs.Add(new[] { Contact, Melee1, 6, 0.2f, -0.1f });
                segs.Add(A.routed ? new[] { Melee1 + s.rd, Regroup1 + 0.3f, 18, 0.75f, -0.3f } : new[] { Melee1 + s.rd, Regroup1 - 0.04f, 10, 0.36f, 0.07f });
            }
            if (s.role == "arch" && A.routed) segs.Add(new[] { Melee1 + s.rd, Regroup1 + 0.3f, 18, 0.75f, -0.3f });
            float ph = 0, amp = 0, lean = 0, wsum = 0;
            foreach (var g in segs)
            {
                ph += g[2] * Clamp(tt - g[0], 0, g[1] - g[0]);
                float w = ClashUtil.Trap(tt, g[0], g[1], 0.1f);
                amp = Mathf.Max(amp, w * g[3]);
                lean += w * g[4]; wsum += w;
            }
            oph = ph + s.p0; oamp = amp; olean = wsum > 0 ? lean / Mathf.Max(1, wsum) : 0;
        }

        float GroundY(float x, float z) { return h.H(x, z); }

        // 按时刻 t 摆出全部士兵、武将、箭（纯函数式：跳过时直接摆出结束姿态）
        void Pose(float tt0)
        {
            foreach (var A in armies)
            {
                float yaw0 = A.dir > 0 ? -Mathf.PI / 2 : Mathf.PI / 2;
                foreach (var s in A.soldiers)
                {
                    bool dead = tt0 >= s.fallT;
                    float tt = dead ? s.fallT : tt0;
                    float ox, oz, jit, gph, gamp, glean;
                    SoldierXZ(A, s, tt, out ox, out oz, out jit);
                    Gait(A, s, tt, out gph, out gamp, out glean);
                    float x = ox, z = oz, lift = 0;
                    float fk = dead ? Mathf.Pow(Seg(tt0, s.fallT, s.fallT + (s.fallKind == "blast" ? 0.5f : 0.34f)), 2) : 0;
                    float spin = 0;
                    if (dead && s.fallKind == "blast")
                    {
                        float u = Seg(tt0, s.fallT, s.fallT + 0.5f);
                        x -= A.dir * s.kb * ClashUtil.Eo(u);       // 被震飞：朝本方后方
                        lift += 4 * 1.1f * u * (1 - u);
                        spin = (1 - u) * u * 4 * 1.6f * (s.p0 > 3.1f ? 1 : -1);
                    }
                    float y = GroundY(x, z);
                    float bob = Mathf.Abs(Mathf.Sin(gph)) * 0.035f * (gamp / 0.75f);
                    // 朝向：溃逃时转身
                    float yaw = yaw0;
                    if (A.routed) yaw += Mathf.PI * ClashUtil.Sm(Seg(tt, Melee1 + s.rd, Melee1 + s.rd + 0.2f)) * (s.p0 > 3.1f ? 1 : -1);
                    yaw += jit * Mathf.Sin(tt * s.w4 + s.p4) * 0.28f;
                    float pitch = glean + (jit != 0 ? Mathf.Sin(tt * s.w2 + s.p1) * 0.08f * jit : 0);
                    // 胜方欢呼：收队时轻跳
                    float cheer = 0;
                    if (A.cheer && !dead) cheer = Seg(tt0, Melee1 + 0.15f, Melee1 + 0.3f);
                    float jump = cheer != 0 ? Mathf.Max(0, Mathf.Sin((tt0 - Melee1) * 13 + s.p0)) * 0.12f * cheer : 0;
                    if (dead)
                    {
                        pitch = Lerp(pitch, s.fallDir * Mathf.PI / 2 * 0.96f, fk);
                        lift += 0.1f * fk;
                        if (fk >= 1) pitch += Mathf.Sin(Mathf.Min(1, (tt0 - s.fallT - 0.34f) * 6) * Mathf.PI) * 0.05f * s.fallDir;
                    }
                    // 身体矩阵 B = T · Ry(yaw) · Rx(pitch) · Rz(spin)
                    var m = ClashUtil.TRS(x, y + lift + (dead ? 0 : bob + jump), z, ClashUtil.YXZ(pitch, yaw, spin));
                    s.mBody = m;
                    // 腿
                    float swing = dead ? 0.15f * s.fallDir : Mathf.Sin(gph) * gamp;
                    s.mLeg0 = m * ClashUtil.TRS(-0.065f, HIP, 0, ClashUtil.AxisX(swing));
                    s.mLeg1 = m * ClashUtil.TRS(0.065f, HIP, 0, ClashUtil.AxisX(-swing));
                    // 兵器
                    float wp, thrust = 0;
                    if (s.role == "arch")
                    {
                        float rel = A.volleyT + s.shotDelay;
                        float upk = ClashUtil.Sm(Seg(tt, Aim + s.ed * 0.2f, Aim + 0.16f));
                        float down = ClashUtil.Sm(Seg(tt, rel + 0.12f, rel + 0.5f));
                        wp = 0.95f * upk * (1 - down) + 0.12f * down - 0.1f * (1 - upk) - Mathf.Max(0, 1 - Mathf.Abs(tt - rel) * 12) * 0.12f;
                        if (A.routed) wp = Lerp(wp, 0.5f, Seg(tt, Melee1, Melee1 + 0.3f));
                    }
                    else if (s.role == "flag")
                    {
                        wp = Mathf.Sin(tt * 2.3f + s.p0) * 0.05f - 0.08f * Seg(tt, Charge0, Contact);
                    }
                    else
                    {
                        wp = Mathf.Sin(tt * 2.1f + s.p0) * 0.04f - 0.06f;
                        float lower = ClashUtil.Sm(Seg(tt, Charge0 - 0.12f + s.cd * 0.4f, Charge0 + 0.14f + s.cd * 0.4f));
                        wp = Lerp(wp, -1.42f, lower);
                        if (jit > 0)
                        {
                            float eng = s.file <= 1 ? 1 : 0.35f;
                            wp += jit * Mathf.Sin(tt * s.w4 + s.pt) * 0.16f;
                            thrust = jit * eng * Mathf.Pow(Mathf.Max(0, Mathf.Sin(tt * s.wt + s.pt)), 3) * 0.34f;
                        }
                        float back = Seg(tt, Melee1 + s.rd, Regroup1);
                        if (A.routed) wp = Lerp(wp, 0.55f, ClashUtil.Sm(back));
                        else if (A.cheer) wp = Lerp(wp, -0.12f + Mathf.Sin(tt0 * 9 + s.p0) * 0.12f, ClashUtil.Sm(back));
                        else wp = Lerp(wp, -0.95f, ClashUtil.Sm(back));
                    }
                    // 倒地后兵器顺着身体躺平
                    if (dead) { wp = Lerp(wp, s.role == "arch" ? 0.3f : 0.12f * s.fallDir, fk); thrust = 0; }
                    var mw = m * ClashUtil.TRS(GRIP.x, GRIP.y, GRIP.z, ClashUtil.AxisX(wp));
                    if (thrust != 0) mw = mw * Matrix4x4.Translate(new Vector3(0, thrust, 0));
                    s.mWeapon = mw;
                    if (s.flag != null)
                    {
                        s.flag.m3 = mw * Matrix4x4.Translate(new Vector3(0, 0.82f, 0));
                        s.flag.k = dead ? 0.15f : 0.75f + gamp * 0.8f;
                    }
                    // 颜色：受击闪白、阵亡变暗
                    float fl = 0;
                    if (!dead) foreach (var ht in s.hits) { float d = tt0 - ht; if (d >= 0 && d < 0.16f) fl = Mathf.Max(fl, 1 - d / 0.16f); }
                    if (dead) fl = Mathf.Max(fl, 1 - (tt0 - s.fallT) / 0.18f);
                    float darkK = dead ? Lerp(1, 0.52f, Seg(tt0, s.fallT + 0.1f, s.fallT + 0.7f)) : 1;
                    s.tint = darkK * (1 + Mathf.Max(0, fl) * 0.75f);
                }
                PoseGeneral(A, tt0);
            }
            PoseArrows(tt0);
        }

        void PoseGeneral(ClashArmy A, float tt)
        {
            var G = A.general; int dir = A.dir;
            float x = GeneralX(A, tt);
            float z = G.z0;
            float y = GroundY(x, z);
            // 步态：入场与冲锋时小跑，溃逃时疾驰
            float moving = ClashUtil.Trap(tt, 0.08f, March1 + 0.05f, 0.12f) * 0.8f + ClashUtil.Trap(tt, Charge0 + 0.1f, Contact + 0.1f, 0.12f) * 0.6f
                + (A.routed ? ClashUtil.Trap(tt, Melee1 + 0.12f, Regroup1 + 0.5f, 0.1f) : ClashUtil.Trap(tt, Melee1 + 0.1f, Regroup1, 0.1f) * 0.4f);
            float ph = tt * 13 + G.p0;
            float rear = (A.routed ? 0 : 1) * Mathf.Sin(Mathf.PI * Seg(tt, Contact + 0.02f, Contact + 0.6f)) * 0.36f;
            float yaw = dir > 0 ? -Mathf.PI / 2 : Mathf.PI / 2;
            if (A.routed) yaw += Mathf.PI * ClashUtil.Sm(Seg(tt, Melee1 + 0.12f, Melee1 + 0.35f));
            float pitch = rear + Mathf.Sin(ph * 2) * 0.03f * moving;
            // 扬蹄时绕后蹄抬起
            float lift = Mathf.Abs(Mathf.Sin(ph)) * 0.05f * moving + Mathf.Sin(rear) * 0.4f;
            var m = ClashUtil.TRS(x, y + lift, z, ClashUtil.YXZ(pitch, yaw, 0), 1.12f);
            G.m3 = m;
            // 马腿（前后腿交替）
            float[,] legs = { { -0.1f, 0.56f, -0.36f, 0 }, { 0.1f, 0.56f, -0.36f, 0.6f }, { -0.1f, 0.56f, 0.38f, 2.6f }, { 0.1f, 0.56f, 0.38f, 3.3f } };
            for (int i = 0; i < 4; i++)
            {
                float sw = Mathf.Sin(ph + legs[i, 3]) * 0.55f * moving;
                if (rear > 0.05f && i < 2) sw = -0.9f * rear / 0.36f + Mathf.Sin(tt * 20 + i) * 0.15f;
                G.legs[i] = m * ClashUtil.TRS(legs[i, 0], legs[i, 1], legs[i, 2], ClashUtil.AxisX(sw));
            }
            // 帅旗：旗杆顶端横杆下（局部 Unity (-0.18, 2.86, -0.3) → three (-0.18, 2.86, 0.3)）
            G.flag.m3 = m * Matrix4x4.Translate(new Vector3(-0.18f, 2.84f, 0.32f));
            G.flag.k = 0.85f + moving * 0.7f;
        }

        void PoseArrows(float tt)
        {
            foreach (var r in arrows)
            {
                if (tt < r.t0 || (r.hit && tt >= r.land)) { r.visible = false; continue; }
                if (!r.ready) AimArrow(r);
                float u = Mathf.Min(1, (tt - r.t0) / r.T);
                var p = Vector3.Lerp(r.p0, r.p1, u);
                p.y += r.apex * 4 * u * (1 - u);
                var d = r.p1 - r.p0;
                d.y += r.apex * 4 * (1 - 2 * u);
                d.Normalize();
                var q = Quaternion.FromToRotation(new Vector3(0, 0, -1), d);
                if (u >= 1) p += d * (r.water ? 0.4f : 0.14f);   // 插入地面
                if (u >= 1 && r.water) { r.visible = false; continue; }
                r.m3 = Matrix4x4.TRS(p, q, Vector3.one);
                r.visible = true;
            }
        }
        void AimArrow(ClashShot r)
        {
            r.ready = true;
            float ox, oz;
            SoldierXZ(r.S, r.archer, r.t0, out ox, out oz);
            r.p0 = V(ox + r.S.dir * 0.25f, GroundY(ox, oz) + 0.95f, oz);
            if (r.target != null)
            {
                SoldierXZ(r.E, r.target, r.land, out ox, out oz);
                r.p1 = V(ox, GroundY(ox, oz) + 0.55f, oz);
                r.hit = true;
            }
            else
            {
                // 落在敌阵附近
                var E = r.E;
                float cx = 0; int n = 0;
                foreach (var s in E.soldiers) { SoldierXZ(E, s, r.land, out ox, out oz); cx += ox; n++; }
                cx /= Mathf.Max(1, n);
                float x = cx + (Rn() - 0.5f) * 3.2f, z = ZC + (Rn() - 0.5f) * 4.4f;
                r.p1 = V(x, GroundY(x, z), z);
                r.water = kind == "river" && Mathf.Abs(x - ClashTerrain.RIVER_X) < ClashTerrain.RIVER_W + 0.6f;
            }
            float dd = Mathf.Abs(r.p1.x - r.p0.x);
            r.apex = Mathf.Min(2.5f, 1.6f + dd * 0.22f);     // 弧顶不进入画面上方的 HUD 带
        }

        // ---------------------------------------------------------- 特效 --
        void EmitP(ClashPool pool, Vector3 pos, int n, ClashEmit o)
        {
            for (int i = 0; i < n; i++)
            {
                float px, py, pz, vx, vy, vz;
                if (o.box.HasValue)
                {
                    var b = o.box.Value;
                    px = pos.x + (Rn() - 0.5f) * b.x; py = pos.y + (Rn() - 0.5f) * b.y; pz = pos.z + (Rn() - 0.5f) * b.z;
                    float sp = o.speed * (0.55f + Rn() * 0.6f), j = o.jitter;
                    vx = (Rn() - 0.5f) * 2 * j + o.vx; vz = (Rn() - 0.5f) * 2 * j; vy = sp;
                }
                else
                {
                    float x, y, z, d;
                    do { x = Rn() * 2 - 1; y = Rn() * 2 - 1; z = Rn() * 2 - 1; d = x * x + y * y + z * z; } while (d > 1 || d < 1e-6f);
                    d = Mathf.Sqrt(d);
                    if (o.up) y = Mathf.Abs(y);
                    px = pos.x + x * o.radius; py = pos.y + y * o.radius; pz = pos.z + z * o.radius;
                    float sp = o.speed * (o.speedVar != 0 ? 1 - o.speedVar * Rn() : 1);
                    vx = x / d * sp + o.vx; vy = y / d * sp + o.vy; vz = z / d * sp;
                }
                Color c = o.color;
                if (o.color2.HasValue && Rn() < o.mix) c = o.color2.Value;
                Color ce = o.colorEnd ?? c;
                pool.Add(new ClashParticle
                {
                    x = px, y = py, z = pz, vx = vx, vy = vy, vz = vz, age = 0,
                    r1 = ce.r, g1 = ce.g, b1 = ce.b,
                    life = o.life * (o.lifeVar != 0 ? 1 - o.lifeVar * Rn() : 1),
                    size = o.size * (o.sizeVar != 0 ? 1 - o.sizeVar * Rn() : 1), sizeEnd = o.sizeEnd,
                    r = c.r, g = c.g, b = c.b, a = o.alpha, grav = o.gravity, drag = o.drag, fadeIn = o.fadeIn,
                    ac = o.alphaCurve, floor = o.floor,
                });
            }
        }
        void DustAt(float x, float z, float k, bool big)
        {
            float y = GroundY(x, z);
            bool wet = kind == "river" && y < ClashTerrain.RIVER_SURF - 0.05f;
            if (wet)
            {
                EmitP(fx, V(x, ClashTerrain.RIVER_SURF + 0.03f, z), big ? 10 : 4, new ClashEmit
                {
                    color = C(0.97f, 0.99f, 1), color2 = C(0.78f, 0.9f, 0.96f), alpha = 0.95f * k, life = 0.6f, speed = big ? 2.6f : 1.9f, size = 0.2f, sizeEnd = 0.55f,
                    gravity = 0.85f, radius = 0.15f, up = true, lifeVar = 0.3f, speedVar = 0.45f,
                });
                EmitP(fx, V(x, ClashTerrain.RIVER_SURF + 0.04f, z), 1, new ClashEmit { color = C(0.9f, 0.96f, 1), alpha = 0.55f * k, life = 0.5f, speed = 0, size = 0.6f, sizeEnd = 2.2f, radius = 0.05f });
                return;
            }
            var col = kind == "forest" ? C(0.62f, 0.57f, 0.47f) : ClashTerrain.Walled(kind) ? C(0.8f, 0.74f, 0.62f) : C(0.8f, 0.74f, 0.6f);
            EmitP(dust, V(x, y + 0.12f, z), big ? 3 : 1, new ClashEmit
            {
                color = col, colorEnd = Art.Shade(col, 0.12f), alpha = 0.5f * k, life = big ? 1.2f : 0.8f, speed = big ? 0.9f : 0.45f, size = big ? 1.1f : 0.62f, sizeEnd = big ? 2.6f : 2.0f,
                gravity = -0.02f, radius = big ? 0.4f : 0.2f, up = true, lifeVar = 0.3f, drag = 1.4f, fadeIn = 0.08f,
            });
        }
        void Sparks(Vector3 pos, int n, Color? col = null)
        {
            EmitP(glow, pos, n, new ClashEmit
            {
                color = col ?? C(1, 0.88f, 0.5f), color2 = C(1, 1, 0.9f), colorEnd = C(1, 0.4f, 0.1f), alpha = 1, life = 0.4f, speed = 3.4f, size = 0.15f, sizeEnd = 0.35f,
                gravity = 0.9f, radius = 0.08f, lifeVar = 0.4f, speedVar = 0.5f,
            });
            EmitP(glow, pos, 2, new ClashEmit { color = col ?? C(1, 0.9f, 0.62f), alpha = 0.9f, life = 0.16f, speed = 0, size = 0.8f, sizeEnd = 1.8f, radius = 0.01f });
        }
        void ArrowImpact(ClashShot r)
        {
            if (!r.ready) AimArrow(r);
            if (r.hit)
            {
                Sparks(r.p1, 5, C(1, 0.85f, 0.6f));
                ClashUtil.Sfx("hit", 0.18f);
            }
            else if (r.water)
            {
                EmitP(fx, V(r.p1.x, ClashTerrain.RIVER_SURF + 0.02f, r.p1.z), 5, new ClashEmit
                {
                    color = C(0.92f, 0.96f, 1), alpha = 0.85f, life = 0.45f, speed = 1.6f, size = 0.1f, sizeEnd = 0.4f, gravity = 1, radius = 0.05f, up = true, lifeVar = 0.3f,
                });
            }
            else
            {
                EmitP(dust, V(r.p1.x, r.p1.y + 0.05f, r.p1.z), 1, new ClashEmit
                {
                    color = C(0.62f, 0.56f, 0.44f), alpha = 0.35f, life = 0.6f, speed = 0.4f, size = 0.35f, sizeEnd = 1.6f, radius = 0.05f, up = true, drag = 1.5f,
                });
            }
        }
        float ContactLine()
        {
            var L = armies[0];
            float x = 0; int n = 0;
            foreach (var s in L.soldiers) if (s.role == "inf" && s.file == 0) { float ox, oz; SoldierXZ(L, s, t, out ox, out oz); x += ox; n++; }
            return n > 0 ? x / n + 0.3f : xc;
        }
        void OnContact()
        {
            shake = 1;
            ClashUtil.Sfx("hit", 0.9f); ClashUtil.Sfx("duel", 0.5f);
            // 交锋乐句：跟随当前战斗曲的调与速度
            if (!Skipped) ClashUtil.Stinger("clash", speed);
            float x = ContactLine();
            for (int i = 0; i < 9; i++)
            {
                float z = ZC + (Rn() - 0.5f) * 4;
                DustAt(x + (Rn() - 0.5f) * 0.8f, z, 1, true);
                if (i % 2 == 0) Sparks(V(x, GroundY(x, z) + 0.6f + Rn() * 0.4f, z), 6);
            }
            hud.Flash(new Color(1, 244 / 255f, 214 / 255f, 0.32f), 0.35f);
        }
        void OnFall(ClashArmy A, ClashSoldier s)
        {
            float ox, oz;
            SoldierXZ(A, s, s.fallT, out ox, out oz);
            DustAt(ox - A.dir * 0.3f, oz, 0.9f, false);
            if (s.fallKind == "melee") Sparks(V(ox + A.dir * 0.25f, GroundY(ox, oz) + 0.6f, oz), 4);
        }
        void OnChunk(ClashChunk ch)
        {
            var A = armies[ch.si];
            hud.Loss(ch.si, ch.amount);
            // 世界坐标飘字：在该部队中心上方
            float x = 0; int n = 0;
            foreach (var s in A.soldiers) if (!(t >= s.fallT + 0.6f)) { float ox, oz; SoldierXZ(A, s, t, out ox, out oz); x += ox; n++; }
            x = n > 0 ? x / n : A.xFront;
            int k = chunks.Count(c => c.si == ch.si && c.t < ch.t);
            x += (k % 2 == 1 ? 0.6f : -0.4f) * A.dir;
            // 两军接战后中心会挤在一起：各自的飘字保持在接敌线本方一侧，免得数字叠在一起
            float line = ContactLine(), gap = 1.35f;
            x = A.dir > 0 ? Mathf.Min(x, line - gap) : Mathf.Max(x, line + gap);
            hud.Pop(V(x, 2.0f + (k % 3) * 0.32f + ch.si * 0.18f, ZC), "−" + ch.amount, ch.kind == "special");
        }
        void OnRout(ClashArmy A)
        {
            ClashUtil.Sfx("lose", 0.3f);
            hud.Stamp(A.info.name + "部", "溃 散", false);
        }
        void SpecialImpact()
        {
            if (special == null) return;
            var col = special.color;
            var Dd = armies[1];
            float cx = 0; int n = 0;
            foreach (var s in Dd.soldiers) { float ox, oz; SoldierXZ(Dd, s, t, out ox, out oz); cx += ox; n++; }
            cx = n > 0 ? cx / n : Dd.xFront;
            shake = 1.4f;
            hud.Flash(new Color(col.r, col.g, col.b, 1), 0.45f);
            ClashUtil.Sfx("magic", 0.7f); ClashUtil.Sfx("hit", 0.9f);
            string k = specialKind;
            if (k == "blaze")
            {
                for (int i = 0; i < 16; i++)
                {
                    float x = cx + (Rn() - 0.5f) * 3.6f, z = ZC + (Rn() - 0.5f) * 3.8f, y = GroundY(x, z);
                    EmitP(fx, V(x, y + 0.25f, z), 10, new ClashEmit
                    {
                        color = C(1, 0.66f, 0.16f), color2 = C(1, 0.86f, 0.4f), colorEnd = C(0.7f, 0.12f, 0.03f), alpha = 1, life = 1.1f, speed = 1.8f, size = 0.95f, sizeEnd = 0.3f,
                        gravity = -0.25f, box = V(0.8f, 0.2f, 0.8f), jitter = 0.3f, lifeVar = 0.35f, alphaCurve = 2,
                    });
                    EmitP(glow, V(x, y + 0.4f, z), 3, new ClashEmit { color = C(1, 0.6f, 0.2f), alpha = 0.7f, life = 0.7f, speed = 0.6f, size = 1.2f, sizeEnd = 0.4f, radius = 0.3f, gravity = -0.2f });
                }
                EmitP(dust, V(cx, 1.6f, ZC), 14, new ClashEmit { color = C(0.2f, 0.18f, 0.16f), alpha = 0.45f, life = 1.8f, speed = 0.9f, size = 1.2f, sizeEnd = 2.6f, radius = 1.4f, up = true, gravity = -0.04f, drag = 0.4f, fadeIn = 0.15f });
                return;
            }
            if (k == "storm")
            {
                for (int b = 0; b < 3; b++)
                {
                    float bx = cx + (b - 1) * 1.1f + (Rn() - 0.5f) * 0.6f, bz = ZC + (Rn() - 0.5f) * 2.4f;
                    Bolt(V(bx, 0, bz), col, b * 0.07f);
                }
                return;
            }
            if (k == "volley")
            {
                for (int i = 0; i < 6; i++) { float sx = cx + (Rn() - 0.5f) * 3, sz = ZC + (Rn() - 0.5f) * 3; Sparks(V(sx, 0.6f, sz), 5, Art.Shade(col, 0.4f)); }
                return;
            }
            // 默认：巨大的弧形斩光 + 地面冲击波
            float line = ContactLine();
            Slash(V(line - 0.4f, 1.1f, ZC + 0.9f), col);
            Shockwave(V(line + 0.9f, GroundY(line + 0.9f, ZC) + 0.06f, ZC), col);
            for (int i = 0; i < 6; i++)
            {
                float sx = line + 0.4f + Rn() * 1.6f, sy = 0.5f + Rn() * 0.8f, sz = ZC + (Rn() - 0.5f) * 3.4f;
                Sparks(V(sx, sy, sz), 7, Art.Shade(col, 0.5f));
            }
        }
        Material FxMat(Color c, Texture tex, int queue)
        {
            var m = Art.NewAdditive(c);
            m.mainTexture = tex != null ? tex : Texture2D.whiteTexture;
            m.renderQueue = queue;
            return m;
        }
        // 圆环弧（three 的 RingGeometry(r0, r1, 28, 1, -0.95, 1.9)，在 xy 平面）
        static Mesh RingArc(float r0, float r1)
        {
            var mb = new MeshBuilder();
            const int N = 28; const float a0 = -0.95f, len = 1.9f;
            for (int i = 0; i < N; i++)
            {
                float u0 = a0 + len * i / N, u1 = a0 + len * (i + 1) / N;
                Vector3 i0 = V(Mathf.Cos(u0) * r0, Mathf.Sin(u0) * r0, 0), i1 = V(Mathf.Cos(u1) * r0, Mathf.Sin(u1) * r0, 0);
                Vector3 o0 = V(Mathf.Cos(u0) * r1, Mathf.Sin(u0) * r1, 0), o1 = V(Mathf.Cos(u1) * r1, Mathf.Sin(u1) * r1, 0);
                mb.Quad(i0, o0, o1, i1, Color.white);
            }
            return mb.ToMesh("clash slash");
        }
        void Slash(Vector3 pos, Color col)
        {
            var specs = new[] { new Vector4(1.6f, 3.3f, 0.35f, 0.98f), new Vector4(2.15f, 3.0f, 0.85f, 1f), new Vector4(2.55f, 2.82f, 0.95f, 1.05f) };
            for (int i = 0; i < 3; i++)
            {
                var sp = specs[i];
                var c = i == 2 ? C(1, 1, 0.95f) : col;
                var mesh = RingArc(sp.x, sp.y);
                var mat = FxMat(new Color(c.r, c.g, c.b, sp.z), null, 3015);
                var f = new ClashFx { mesh = mesh, mat = mat, t0 = t, life = 0.42f };
                float kk = sp.w;
                f.fn = u =>
                {
                    float s = 0.55f + ClashUtil.Eo(u) * 0.75f * kk;
                    float rot = -0.35f - u * 0.9f;
                    f.m3 = Matrix4x4.TRS(pos, Quaternion.AngleAxis(rot * Mathf.Rad2Deg, Vector3.forward), new Vector3(s, s, 1));
                    var cc = f.mat.color; cc.a = (1 - u * u) * (kk > 1 ? 0.95f : kk < 1 ? 0.35f : 0.85f); f.mat.color = cc;
                };
                f.fn(0);
                temp.Add(f);
            }
        }
        void Shockwave(Vector3 pos, Color col)
        {
            var mb = new MeshBuilder();
            mb.FlatQuad(V(-0.5f, 0, -0.5f), V(-0.5f, 0, 0.5f), V(0.5f, 0, 0.5f), V(0.5f, 0, -0.5f), Color.white);
            var mesh = mb.ToMesh("clash shock");
            var mat = FxMat(new Color(col.r, col.g, col.b, 0.9f), Art.Ring, 3013);
            var f = new ClashFx { mesh = mesh, mat = mat, t0 = t, life = 0.55f };
            f.fn = u =>
            {
                float s = 1 + ClashUtil.Eo(u) * 7;
                f.m3 = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1, s));
                var cc = f.mat.color; cc.a = 0.9f * (1 - u); f.mat.color = cc;
            };
            f.fn(0);
            temp.Add(f);
        }
        void Bolt(Vector3 bas, Color col, float delay)
        {
            var mb = new MeshBuilder();
            // 雷从 HUD 带之下的一团云光中劈下（起点不高于约 5.4）
            const float top = 5.4f;
            float x = bas.x, z = bas.z, y = top;
            float gy = GroundY(bas.x, bas.z);
            while (y > gy)
            {
                float nx = bas.x + (Rn() - 0.5f) * 0.9f, ny = Mathf.Max(gy, y - 0.6f - Rn() * 0.5f), nz = z + (Rn() - 0.5f) * 0.3f;
                // 网页版这里误用了 Unity 坐标（雷落在镜像的 z 上，偏近镜头约 1.1）；此处按 three 坐标建在落点上
                ClashArt.Beam(mb, ClashUtil.P(x, y, z), ClashUtil.P(nx, ny, nz), 0.12f, 0.1f, Color.white);
                x = nx; y = ny; z = nz;
                if (ny <= gy + 0.01f) break;
            }
            var mesh = mb.ToMesh("clash bolt");
            var bc = Art.Shade(col, 0.55f);
            var mat = FxMat(new Color(bc.r, bc.g, bc.b, 0), null, 3015);
            var f = new ClashFx { mesh = mesh, mat = mat, t0 = t + delay, life = 0.38f, m3 = Matrix4x4.identity };
            f.fn = u =>
            {
                var cc = f.mat.color; cc.a = u < 0 ? 0 : (1 - u) * (0.75f + 0.25f * Mathf.Sin(u * 60)); f.mat.color = cc;
                if (u > 0 && !f.hit)
                {
                    f.hit = true;
                    EmitP(glow, V(bas.x, top + 0.2f, bas.z), 5, new ClashEmit { color = Art.Shade(col, 0.6f), alpha = 0.85f, life = 0.4f, speed = 0.5f, size = 2.6f, sizeEnd = 4.2f, radius = 0.7f, gravity = 0 });
                    Sparks(V(bas.x, GroundY(bas.x, bas.z) + 0.2f, bas.z), 12, Art.Shade(col, 0.4f)); DustAt(bas.x, bas.z, 1, true);
                }
            };
            temp.Add(f);
        }

        // ---------------------------------------------------------- 镜头 --
        void CameraPose(float tt)
        {
            if (cam == null) return;
            // 注视点比两军略高一点：画面上方约 1/6 被 HUD 占去，让军阵落在 HUD 之下的画面中部
            var K = new[]
            {
                new[] { 0f, -1.1f, 4.0f, 18.4f, 0.2f, 2.05f, -0.8f },
                new[] { 0.7f, -0.4f, 2.55f, 15.0f, 0, 2.1f, -0.8f },
                new[] { Charge0, -0.3f, 2.5f, 14.6f, 0, 2.05f, -0.8f },
                new[] { Contact, xc * 0.45f + 0.2f, 2.18f, 11.9f, xc * 0.5f, 1.78f, -0.8f },
                new[] { Melee1, xc * 0.45f + 0.5f, 2.1f, 11.4f, xc * 0.5f + 0.2f, 1.72f, -0.8f },
                new[] { Regroup1, xc * 0.3f + 0.2f, 2.5f, 13.8f, xc * 0.3f, 2.02f, -0.8f },
            };
            int i = 0;
            while (i < K.Length - 2 && tt > K[i + 1][0]) i++;
            var a = K[i]; var b = K[i + 1];
            float u = ClashUtil.Sm(Seg(tt, a[0], b[0]));
            float lx0 = Lerp(a[4], b[4], u), ly = Lerp(a[5], b[5], u), lz = Lerp(a[6], b[6], u);
            float dx = Lerp(a[1], b[1], u) - lx0, dy = Lerp(a[2], b[2], u) - ly, dz = Lerp(a[3], b[3], u) - lz;
            float aspect = cam.aspect > 0 ? cam.aspect : 1.78f;
            // 横向取景：注视点向两员武将（含帅旗）的中点靠一半；施展必杀时再偏向攻方武将
            float el, er, egl, egr;
            ArmyExtent(tt, out el, out er, out egl, out egr);
            float lx = Lerp(lx0, (el + er) / 2, 0.5f);
            if (special != null) lx = Lerp(lx, egl, 0.22f * ClashUtil.Trap(tt, Charge0 - 0.3f, Melee1, 0.4f));
            // 推近不得把两军（尤其两端的武将与帅旗）推出画面：按实际军阵外缘求最小拉远倍数（左右各留约 4%）
            float tanH = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2) * aspect * 0.92f;
            float need = ViewNeed(tt, lx, lz, dz, tanH);
            float ft = Mathf.Max(fit, need);
            // 竖屏：拉远之后再抬高机位俯视，压低天空、让纵深铺开
            float kP = Clamp((1.15f - aspect) / 0.6f, 0, 1);
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * ft;
            float px = lx + dx * ft, py = ly + dy * ft + kP * dist * 0.27f, pz = lz + dz * ft;
            if (shake > 0.001f)
            {
                float k = shake * 0.11f;
                px += Mathf.Sin(real * 71) * k;
                py += Mathf.Sin(real * 57 + 1.3f) * k * 0.7f;
            }
            float lookY = ly - kP * 0.35f;
            var tr = cam.transform;
            tr.position = Clash.Origin + ClashUtil.P(px, py, pz);
            tr.LookAt(Clash.Origin + ClashUtil.P(lx, lookY, lz), Vector3.up);
            // 城门 / 本城：城门楼屋脊不得顶进上方 HUD——投影屋脊，若高于 HUD 下沿就抬高注视点，最多抬 1.6
            if (kind == "gate" || kind == "castle")
            {
                float hb = hud != null ? hud.BarBottomNdc() : 1;
                if (hb < 0.98f)
                {
                    float tanV = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
                    float dl = Mathf.Sqrt((px - lx) * (px - lx) + (py - lookY) * (py - lookY) + (pz - lz) * (pz - lz));
                    float up = 0;
                    var ridge = Clash.Origin + ClashUtil.P(ClashTerrain.GATE_X, 6.2f, ClashTerrain.WALL_Z - 0.7f);
                    for (int it = 0; it < 4; it++)
                    {
                        float y = cam.WorldToViewportPoint(ridge).y * 2 - 1;
                        float over = y - (hb - 0.03f);
                        if (over <= 0.004f || up >= 1.6f) break;
                        up = Mathf.Min(1.6f, up + over * tanV * dl * 0.9f);
                        tr.LookAt(Clash.Origin + ClashUtil.P(lx, lookY + up, lz), Vector3.up);
                    }
                }
            }
            // 雾随机位远近平移，拉远时军阵不被雾吞掉
            if (shown)
            {
                float extra = Mathf.Max(0, dist - 19);
                RenderSettings.fogStartDistance = 34 + extra; RenderSettings.fogEndDistance = 135 + extra;
            }
        }
        // 武将 x（与 PoseGeneral 同一公式；镜头取景用）
        float GeneralX(ClashArmy A, float tt)
        {
            var G = A.general; int dir = A.dir;
            float x = G.x0 - dir * ENTER_DX * (1 - ClashUtil.Eo(Seg(tt, G.ed, March1 + 0.05f)));
            float uc = Seg(tt, Charge0 + 0.1f, Contact + 0.1f);
            x += dir * A.genDX * uc * uc;
            if (A.routed) x -= dir * 9 * Mathf.Pow(Seg(tt, Melee1 + 0.12f, Regroup1 + 0.3f), 1.4f);
            else x -= dir * 0.45f * ClashUtil.Eio(Seg(tt, Melee1 + 0.1f, Regroup1));
            return x;
        }
        // 取景用的时刻：入场时按列阵位置（让队伍走进画面），溃逃时停在溃散前（不追着逃兵拉远）
        float FrameT(ClashArmy A, float tt) { return Clamp(tt, March1, A.routed ? Melee1 + 0.12f : End); }
        void ArmyExtent(float tt, out float l, out float r, out float gl, out float gr)
        {
            ClashArmy L = armies[0], R = armies[1];
            gl = GeneralX(L, FrameT(L, tt)); gr = GeneralX(R, FrameT(R, tt));
            l = gl - 1.75f; r = gr + 1.75f;
        }
        float ViewNeed(float tt, float cx, float lz, float dz, float tanH)
        {
            if (!(dz > 0.1f) || !(tanH > 0.01f)) return 1;
            float need = 0;
            foreach (var A in armies)
            {
                float ft = FrameT(A, tt), gx = GeneralX(A, ft), gz = A.general.z0;
                need = Mathf.Max(need, (Mathf.Abs(gx - A.dir * 1.75f - cx) / tanH + (gz - 0.25f - lz)) / dz);   // 帅旗（含旗面飘动）在武将身后
                need = Mathf.Max(need, (Mathf.Abs(gx + A.dir * 1.0f - cx) / tanH + (gz - lz)) / dz);           // 马头
                foreach (var s in A.soldiers)
                {
                    float ox, oz; SoldierXZ(A, s, ft, out ox, out oz);
                    need = Mathf.Max(need, (Mathf.Abs(ox - A.dir * 0.3f - cx) / tanH + (oz + 0.25f - lz)) / dz);
                }
            }
            return need;
        }
        void Resize()
        {
            lastW = Screen.width; lastH = Screen.height;
            float aspect = lastW / (float)Mathf.Max(1, lastH);
            // 以 16:9 为基准：更窄的屏幕拉远，更宽的屏幕（手机横屏）适当推近；两军外缘的约束见 ViewNeed
            fit = Clamp(1.78f / aspect, 0.86f, 1.75f);
            if (hud != null) hud.Layout();
        }

        // ---------------------------------------------------------- 每帧 --
        void Advance(float dt)
        {
            if (state == "done") return;
            if (!(dt >= 0)) dt = 0;
            dt = Mathf.Min(dt, 0.1f);
            real += dt;
            float tBefore = t; bool wasRun = state == "run";
            if (state == "run" && !paused)
            {
                float nt = t;
                float k = (nt >= Contact && nt < Contact + SlowW) ? SlowK : 1;
                nt += dt * speed * k;
                if (Clash.FreezeAt >= 0) nt = Mathf.Min(nt, Clash.FreezeAt);
                if (special != null && !cutDone && nt >= Charge0 - 0.1f)
                {
                    nt = Charge0 - 0.1f;
                    cutDone = true;
                    paused = true;
                    var sp = special;
                    var v = CutInView.Open(A.gen, sp.name, sp.color, A.side, sp.cry, Mathf.Max(0.1f, optSpeed));
                    if (v != null) v.onEnd = () => { paused = false; };
                    else paused = false;
                }
                SetTime(nt, true);
                if (t >= End) Finish();
            }
            else if (state == "hold")
            {
                // 帧率很低时（dt 被截断）也不让跳过后的停留超过 SKIP_HOLD 实际秒数
                if (real >= holdUntil || Time.realtimeSinceStartup >= holdWall) Finish();
            }
            shake = Mathf.Max(0, shake - dt * 3.2f);
            CameraPose(t);
            // 粒子、旗帜、临时特效：播放中随时间轴推进（慢镜头、特写定格、调试冻结时一并停住）
            float step = wasRun ? Mathf.Max(0, t - tBefore) : dt * speed;
            dust.Step(step); fx.Step(step); glow.Step(step);
            float ws = real * Mathf.Min(1.6f, speed);
            foreach (var a in armies)
            {
                a.general.flag.Wave(ws, a.general.flag.k);
                foreach (var s in a.soldiers) if (s.flag != null) s.flag.Wave(ws + s.p0, s.flag.k);
            }
            for (int i = temp.Count - 1; i >= 0; i--)
            {
                var f = temp[i];
                float u = (t - f.t0) / f.life;
                if (u >= 1 || state != "run") { DestroyFx(f); temp.RemoveAt(i); continue; }
                f.fn(u);
            }
            if (hud != null) hud.Update(dt, state == "run" ? (t - tPrev) / speed : dt);
            tPrev = t;
        }
        void DestroyFx(ClashFx f)
        {
            if (f.mesh != null) UnityEngine.Object.Destroy(f.mesh);
            if (f.mat != null) UnityEngine.Object.Destroy(f.mat);
            f.mesh = null; f.mat = null;
        }
        // 推进时间轴并触发途经的事件（fx = false 时只更新状态，不放特效）
        void SetTime(float nt, bool withFx)
        {
            float prev = t;
            t = nt;
            while (evIdx < events.Count && events[evIdx].Key <= nt)
            {
                var e = events[evIdx++];
                if (withFx) { try { e.Value(); } catch (Exception err) { Debug.LogException(err); } }
            }
            if (withFx && !paused) Ambient(prev, nt);
            Pose(nt);
        }
        // 持续的扬尘、溅水与火花
        void Ambient(float t0, float t1)
        {
            float dt = t1 - t0;
            if (dt <= 0) return;
            foreach (var A in armies)
                foreach (var s in A.soldiers)
                {
                    if (t1 >= s.fallT) continue;
                    bool charging = s.role != "arch" && t1 > Charge0 + s.cd && t1 < Contact + 0.05f;
                    bool marching = t1 < March1;
                    bool fleeing = A.routed && t1 > Melee1;
                    float rate = charging ? 5 : fleeing ? 4 : marching ? 0.9f : 0;
                    if (rate > 0 && Rn() < rate * dt) { float ox, oz; SoldierXZ(A, s, t1, out ox, out oz); DustAt(ox - A.dir * 0.2f, oz, charging ? 0.8f : 0.5f, false); }
                }
            if (t1 > Contact && t1 < Melee1)
            {
                float x = ContactLine();
                Color? sp = special != null ? Art.Shade(special.color, 0.3f) : (Color?)null;
                if (Rn() < 16 * dt)
                {
                    float z = ZC + (Rn() - 0.5f) * 4;
                    float sx = x + (Rn() - 0.5f) * 0.8f, sy = GroundY(x, z) + 0.45f + Rn() * 0.45f;
                    Sparks(V(sx, sy, z), 4, sp.HasValue && Rn() < 0.4f ? sp : null);
                }
                if (Rn() < 12 * dt) { float dx = x + (Rn() - 0.5f) * 1.8f, dz = ZC + (Rn() - 0.5f) * 4.2f; DustAt(dx, dz, 0.8f, true); }
            }
        }

        // ---------------------------------------------------------- 绘制 --
        static readonly int ColorId = Shader.PropertyToID("_Color");
        void DrawU(Mesh mesh, Matrix4x4 m3, Material mat, float tint, bool shadows = true)
        {
            var m = Matrix4x4.Translate(Clash.Origin) * ClashUtil.ToU(m3);
            mpb.SetColor(ColorId, new Color(tint, tint, tint, 1));
            Graphics.DrawMesh(mesh, m, mat, Clash.Layer, cam, 0, mpb, shadows, true, false);
        }
        void DrawU(Mesh mesh, Matrix4x4 m3, Material mat, Color tint, bool shadows = true)
        {
            var m = Matrix4x4.Translate(Clash.Origin) * ClashUtil.ToU(m3);
            mpb.SetColor(ColorId, tint);
            Graphics.DrawMesh(mesh, m, mat, Clash.Layer, cam, 0, mpb, shadows, true, false);
        }
        public void Draw()
        {
            if (disposed || !shown || cam == null || armies == null) return;
            var lp = Art.LowPoly;
            Vector3 toLight = RenderSettings.sun != null ? -RenderSettings.sun.transform.forward : new Vector3(0.38f, 0.74f, -0.55f).normalized;
            foreach (var A in armies)
            {
                foreach (var s in A.soldiers)
                {
                    DrawU(s.role == "arch" ? A.archMesh : A.infMesh, s.mBody, lp, s.tint);
                    DrawU(A.legMesh, s.mLeg0, lp, s.tint);
                    DrawU(A.legMesh, s.mLeg1, lp, s.tint);
                    DrawU(s.role == "arch" ? bowMesh : spearMesh, s.mWeapon, lp, s.tint);
                }
                var G = A.general;
                DrawU(G.mesh, G.m3, lp, 1f);
                for (int i = 0; i < 4; i++) DrawU(horseLegMesh, G.legs[i], lp, G.horse);
            }
            foreach (var r in arrows) if (r.visible) DrawU(arrowMesh, r.m3, lp, r.spec && special != null ? arrowGlow : Color.white);
            foreach (var f in flags)
            {
                var mu = Matrix4x4.Translate(Clash.Origin) * ClashUtil.ToU(f.m3);
                f.Light(mu, toLight);
                Graphics.DrawMesh(f.mesh, mu, f.mat, Clash.Layer, cam, 0, null, false, false, false);
            }
            foreach (var f in temp)
                if (f.mesh != null && f.mat != null)
                    Graphics.DrawMesh(f.mesh, Matrix4x4.Translate(Clash.Origin) * ClashUtil.ToU(f.m3), f.mat, Clash.Layer, cam, 0, null, false, false, false);
            var tr = cam.transform;
            foreach (var p in new[] { dust, fx, glow })
            {
                if (p.Count == 0) continue;
                p.Build(tr.right, tr.up);
                Graphics.DrawMesh(p.mesh, Matrix4x4.Translate(Clash.Origin), p.mat, Clash.Layer, cam, 0, null, false, false, false);
            }
        }

        // ---------------------------------------------------------- 流程 --
        public void Skip()
        {
            if (state != "run" || !built || real < 0.25f) return;
            Skipped = true;
            paused = false;
            cutDone = true;
            if (Clash.cut != null) Clash.cut.Skip();
            SetTime(Regroup1, false);
            dust.Clear(); fx.Clear(); glow.Clear();
            hud.Final();
            foreach (var a in armies) if (a.routed) hud.Stamp(a.info.name + "部", "溃 散", true);
            state = "hold";
            holdUntil = real + SKIP_HOLD;
            holdWall = Time.realtimeSinceStartup + SKIP_HOLD;
        }
        void Finish()
        {
            if (state == "out" || state == "done") return;
            state = "out";
            hud.Final();
            hud.FadeOut(FADE_OUT, () => { state = "done"; Dispose(); });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            state = "done";
            if (Clash.cut != null) Clash.cut.End();
            // 还原全局状态
            foreach (var c in camsOff) if (c != null) c.enabled = true;
            camsOff.Clear();
            if (fogSaved)
            {
                RenderSettings.fog = fog0; RenderSettings.fogMode = fogMode0; RenderSettings.fogColor = fogColor0;
                RenderSettings.fogStartDistance = fogStart0; RenderSettings.fogEndDistance = fogEnd0;
                QualitySettings.shadowDistance = shadowDist0;
                fogSaved = false;
            }
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i].Key;
                if (g == null) continue;
                if (groups[i].Value) UnityEngine.Object.Destroy(g);
                else { g.alpha = groupAlpha[i]; g.blocksRaycasts = groupBlocks[i]; g.interactable = groupInter[i]; }
            }
            groups.Clear();
            if (rigSaved && Game.I != null && Game.I.Rig != null) Game.I.Rig.InputEnabled = rigInput0;
            rigSaved = false;
            // 释放
            if (hud != null) { hud.Dispose(); hud = null; }
            foreach (var f in temp) DestroyFx(f);
            temp.Clear();
            foreach (var o in own)
            {
                if (o == null) continue;
                var rt = o as RenderTexture;
                if (rt != null) rt.Release();
                UnityEngine.Object.Destroy(o);
            }
            own.Clear();
            flags.Clear();
            if (runner != null) runner.sc = null;
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null; cam = null;
            if (Clash.active == this) Clash.active = null;
            Finished = true;
        }
    }

    // 粒子贴图
    internal static class ClashTex
    {
        static Texture2D dust;
        // 扬尘：柔边圆点（alpha^0.6，同网页版 uSoft = 0.6）
        public static Texture2D Dust
        {
            get
            {
                if (dust != null) return dust;
                const int n = 64;
                dust = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "clash dust" };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1 - d); a = a * a;
                        dust.SetPixel(x, y, new Color(1, 1, 1, Mathf.Pow(a, 0.6f)));
                    }
                dust.Apply();
                return dust;
            }
        }
    }

    // ======================================================= 界面小工具 --
    // 本画面的界面按网页版 CSS 的像素排版：画布用固定像素缩放（scaleFactor = 设备像素比），
    // 单位 u = clamp(min(1.3vw, 2.45vh), 8.5px, 26px)（竖屏另算），全部位置由 Layout() 计算。
    internal static class ClashUI
    {
        public static float Dpr()
        {
            float dpi = Screen.dpi;
            if (dpi <= 0) return 1;
            return Mathf.Clamp(dpi / (Application.isMobilePlatform ? 160f : 96f), 1f, 4f);
        }
        public static Canvas NewCanvas(string name, int order, out float dpr)
        {
            var go = new GameObject(name);
            var cv = go.AddComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = order;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            dpr = Dpr();
            sc.scaleFactor = dpr;
            go.AddComponent<GraphicRaycaster>();
            return cv;
        }
        // 左上角锚定的矩形（y 向下为正）
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            return rt;
        }
        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
        // 以中心定位（便于缩放 / 旋转动画）
        public static void PlaceC(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, -cy);
            rt.sizeDelta = new Vector2(w, h);
        }
        public static void Full(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f); rt.offsetMin = rt.offsetMax = Vector2.zero; }
        public static Image Img(Transform parent, Sprite s, Color c, string name = "Img", bool ray = false)
        {
            var rt = Rect(name, parent);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s; im.color = c; im.raycastTarget = ray;
            if (ray) im.canvasRenderer.cullTransparentMesh = false;     // 透明的点击区也要接收射线
            if (s != null && s.border != Vector4.zero) im.type = Image.Type.Sliced;
            return im;
        }
        public static RawImage Raw(Transform parent, Texture t, Color c, string name = "Raw")
        {
            var rt = Rect(name, parent);
            var im = rt.gameObject.AddComponent<RawImage>();
            im.texture = t; im.color = c; im.raycastTarget = false;
            return im;
        }
        // 圆角：UIKit.RR（64 像素、圆角 18）按半径缩放切片
        public static void Round(Image im, float radius)
        {
            im.sprite = UIKit.RR;
            im.type = Image.Type.Sliced;
            im.pixelsPerUnitMultiplier = Mathf.Max(0.05f, 18f / Mathf.Max(0.5f, radius));
        }
        public static Text Label(Transform parent, string text, Font font, float size, Color col, TextAnchor align, FontStyle style = FontStyle.Normal, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = Mathf.Max(1, Mathf.RoundToInt(size)); t.color = col; t.alignment = align; t.text = text; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            return t;
        }
        public static Shadow AddShadow(Graphic g, Color c, float dx, float dy)
        {
            var s = g.gameObject.AddComponent<Shadow>();
            s.effectColor = c; s.effectDistance = new Vector2(dx, -dy);
            return s;
        }
        public static Outline AddOutline(Graphic g, Color c, float d)
        {
            var s = g.gameObject.AddComponent<Outline>();
            s.effectColor = c; s.effectDistance = new Vector2(d, -d);
            return s;
        }
        // 文字宽度（含字距）
        public static float Width(Text t, float spacing = 0)
        {
            int n = t.text != null ? t.text.Length : 0;
            return t.preferredWidth + Mathf.Max(0, n - 1) * spacing;
        }
        public static Color Css(string hex, float a = 1) { var c = Art.Hex(hex); c.a = a; return c; }
        public static Color Rgba(int r, int g, int b, float a) { return new Color(r / 255f, g / 255f, b / 255f, a); }
        public static Color Mix(Color a, Color b, float ka) { return new Color(a.r * ka + b.r * (1 - ka), a.g * ka + b.g * (1 - ka), a.b * ka + b.b * (1 - ka), 1); }

        // CSS cubic-bezier 缓动
        public static float Bezier(float x1, float y1, float x2, float y2, float x)
        {
            if (x <= 0) return 0; if (x >= 1) return 1;
            float t = x;
            for (int i = 0; i < 8; i++)
            {
                float cx = 3 * x1 * t * (1 - t) * (1 - t) + 3 * x2 * t * t * (1 - t) + t * t * t - x;
                float dx = 3 * x1 * (1 - t) * (1 - t) + 6 * (x2 - x1) * t * (1 - t) + 3 * (1 - x2) * t * t;
                if (Mathf.Abs(cx) < 1e-4f) break;
                if (Mathf.Abs(dx) < 1e-5f) break;
                t = Mathf.Clamp01(t - cx / dx);
            }
            return 3 * y1 * t * (1 - t) * (1 - t) + 3 * y2 * t * t * (1 - t) + t * t * t;
        }
        public static float EaseIn(float x) { return Bezier(0.42f, 0, 1, 1, x); }
        public static float EaseOut(float x) { return Bezier(0, 0, 0.58f, 1, x); }
        public static float Ease(float x) { return Bezier(0.25f, 0.1f, 0.25f, 1, x); }
        // 关键帧插值：keys = { 位置, 值, 位置, 值, … }（位置 0..1），ease 作用于每段
        public static float Keys(float p, float[] keys, Func<float, float> ease = null)
        {
            if (p <= keys[0]) return keys[1];
            for (int i = 0; i + 3 < keys.Length; i += 2)
            {
                float a = keys[i], b = keys[i + 2];
                if (p <= b)
                {
                    float u = b > a ? (p - a) / (b - a) : 1;
                    if (ease != null) u = ease(u);
                    return keys[i + 1] + (keys[i + 3] - keys[i + 1]) * u;
                }
            }
            return keys[keys.Length - 1];
        }
    }

    // 平行四边形 / 渐变条：沿 axis（0 = 横向，1 = 纵向）的多段渐变；skew = 顶边相对底边的横移（像素）
    internal class ClashPoly : MaskableGraphic
    {
        public float[] stops = { 0, 1 };
        public Color[] colors = { Color.white, Color.white };
        public int axis = 1;
        public float skew, grow;
        public Vector2 shift;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r0 = rectTransform.rect;
            var r = new Rect(r0.xMin - grow + shift.x, r0.yMin - grow + shift.y, r0.width + 2 * grow, r0.height + 2 * grow);
            int n = Mathf.Min(stops.Length, colors.Length);
            if (n < 2) return;
            Func<float, float, Vector3> pt = (u, v) =>
            {
                // u：横向 0..1，v：纵向 0（底）..1（顶）
                return new Vector3(r.xMin + u * r.width + skew * v, r.yMin + v * r.height);
            };
            for (int i = 0; i < n - 1; i++)
            {
                float s0 = stops[i], s1 = stops[i + 1];
                Color c0 = colors[i] * color, c1 = colors[i + 1] * color;
                int b = vh.currentVertCount;
                if (axis == 0)
                {
                    vh.AddVert(pt(s0, 0), c0, Vector2.zero); vh.AddVert(pt(s0, 1), c0, Vector2.zero);
                    vh.AddVert(pt(s1, 1), c1, Vector2.zero); vh.AddVert(pt(s1, 0), c1, Vector2.zero);
                }
                else
                {
                    // 纵向：stops 从顶（0）到底（1）
                    vh.AddVert(pt(0, 1 - s1), c1, Vector2.zero); vh.AddVert(pt(0, 1 - s0), c0, Vector2.zero);
                    vh.AddVert(pt(1, 1 - s0), c0, Vector2.zero); vh.AddVert(pt(1, 1 - s1), c1, Vector2.zero);
                }
                vh.AddTriangle(b, b + 1, b + 2); vh.AddTriangle(b, b + 2, b + 3);
            }
        }
        public void Set(float[] st, Color[] cs) { stops = st; colors = cs; SetVerticesDirty(); }
    }

    // 字距（CSS letter-spacing）：逐字形四边形横移；alignment 决定整体回中
    internal class ClashSpacing : BaseMeshEffect
    {
        public float spacing;
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Abs(spacing) < 0.01f) return;
            int n = vh.currentVertCount / 4;
            if (n <= 1) return;
            var t = GetComponent<Text>();
            float k = 0;
            if (t != null)
            {
                var a = t.alignment;
                if (a == TextAnchor.UpperCenter || a == TextAnchor.MiddleCenter || a == TextAnchor.LowerCenter) k = 0.5f;
                else if (a == TextAnchor.UpperRight || a == TextAnchor.MiddleRight || a == TextAnchor.LowerRight) k = 1;
            }
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                int q = i / 4;
                vh.PopulateUIVertex(ref v, i);
                v.position.x += q * spacing - (n - 1) * spacing * k;
                vh.SetUIVertex(v, i);
            }
        }
        public void Set(float s) { if (Mathf.Abs(s - spacing) > 0.01f) { spacing = s; if (graphic != null) graphic.SetVerticesDirty(); } }
    }

    // 纵向渐变填色（金色招式名）：按顶点高度在 stops 间插值
    internal class ClashVGrad : BaseMeshEffect
    {
        public float[] stops = { 0, 1 };
        public Color[] colors = { Color.white, Color.white };
        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float top = float.MinValue, bot = float.MaxValue;
            for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); top = Mathf.Max(top, v.position.y); bot = Mathf.Min(bot, v.position.y); }
            float hh = Mathf.Max(1, top - bot);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float p = (top - v.position.y) / hh;
                Color c = colors[colors.Length - 1];
                for (int s = 0; s + 1 < stops.Length; s++)
                    if (p <= stops[s + 1]) { c = Color.Lerp(colors[s], colors[s + 1], (p - stops[s]) / Mathf.Max(1e-4f, stops[s + 1] - stops[s])); break; }
                Color32 o = v.color;
                v.color = new Color(c.r, c.g, c.b, o.a / 255f);
                vh.SetUIVertex(v, i);
            }
        }
    }

    // 生成的界面贴图（常驻，跨场次复用）
    internal static class ClashSprites
    {
        static Sprite vig, shade, hgrad, seal, stampBg;
        static Sprite Make(Texture2D t, Vector4 border) { return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border); }
        static Texture2D Tex(int w, int h) { return new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave }; }
        // 暗角：ellipse 75% 70% at 50% 58%，55% 内透明 → 边缘 rgba(10,8,6,.42)
        public static Sprite Vignette
        {
            get
            {
                if (vig != null) return vig;
                const int n = 128; var t = Tex(n, n);
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = 1 - (y + 0.5f) / n;
                        float d = Mathf.Sqrt(Mathf.Pow((u - 0.5f) / 0.75f, 2) + Mathf.Pow((v - 0.58f) / 0.7f, 2));
                        t.SetPixel(x, y, new Color(10 / 255f, 8 / 255f, 6 / 255f, 0.42f * Mathf.Clamp01((d - 0.55f) / 0.45f)));
                    }
                t.Apply(); vig = Make(t, Vector4.zero); return vig;
            }
        }
        // 顶部压暗带：rgba(8,8,12,.62) → .28（55%）→ 0
        public static Sprite Shade
        {
            get
            {
                if (shade != null) return shade;
                var t = Tex(1, 64);
                for (int y = 0; y < 64; y++)
                {
                    float p = 1 - (y + 0.5f) / 64f;
                    float a = p < 0.55f ? Mathf.Lerp(0.62f, 0.28f, p / 0.55f) : Mathf.Lerp(0.28f, 0, (p - 0.55f) / 0.45f);
                    t.SetPixel(0, y, new Color(8 / 255f, 8 / 255f, 12 / 255f, a));
                }
                t.Apply(); shade = Make(t, Vector4.zero); return shade;
            }
        }
        // 横向 透明 → 白 → 透明（发光线）
        public static Sprite HGlow
        {
            get
            {
                if (hgrad != null) return hgrad;
                var t = Tex(64, 4);
                for (int y = 0; y < 4; y++) for (int x = 0; x < 64; x++) { float p = (x + 0.5f) / 64f; t.SetPixel(x, y, new Color(1, 1, 1, 1 - Mathf.Abs(p - 0.5f) * 2)); }
                t.Apply(); hgrad = Make(t, Vector4.zero); return hgrad;
            }
        }
        // 「战」印：radial-gradient(circle at 35% 30%, #e0523f, #b52a20 60%, #7d1610) + 圆角 + 内描边
        public static Sprite Seal
        {
            get
            {
                if (seal != null) return seal;
                const int n = 64; var t = Tex(n, n);
                Color c0 = Art.Hex("#e0523f"), c1 = Art.Hex("#b52a20"), c2 = Art.Hex("#7d1610");
                float rad = n * 0.153f;
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float u = (x + 0.5f) / n, v = 1 - (y + 0.5f) / n;
                        float d = Mathf.Sqrt((u - 0.35f) * (u - 0.35f) + (v - 0.3f) * (v - 0.3f)) / 0.955f;
                        Color c = d < 0.6f ? Color.Lerp(c0, c1, d / 0.6f) : Color.Lerp(c1, c2, (d - 0.6f) / 0.4f);
                        float dx = Mathf.Max(rad - x - 0.5f, x + 0.5f - (n - rad), 0), dy = Mathf.Max(rad - y - 0.5f, y + 0.5f - (n - rad), 0);
                        float e = Mathf.Sqrt(dx * dx + dy * dy) - rad;          // < 0 内部
                        float edge = Mathf.Min(Mathf.Min(x + 0.5f, n - x - 0.5f), Mathf.Min(y + 0.5f, n - y - 0.5f));
                        if (e > -2.2f || edge < 2.2f) c = Color.Lerp(c, new Color(1, 220 / 255f, 170 / 255f), 0.35f);
                        c.a = Mathf.Clamp01(0.5f - e);
                        t.SetPixel(x, y, c);
                    }
                t.Apply(); seal = Make(t, Vector4.zero); return seal;
            }
        }
        // 「溃散」印底：linear-gradient(180deg, #d24434, #9c1e15) + 圆角 + 内描边（九宫切片）
        public static Sprite StampBg
        {
            get
            {
                if (stampBg != null) return stampBg;
                const int n = 64; var t = Tex(n, n);
                Color c0 = Art.Hex("#d24434"), c1 = Art.Hex("#9c1e15");
                const float rad = 8;
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                    {
                        float v = 1 - (y + 0.5f) / n;
                        Color c = Color.Lerp(c0, c1, v);
                        float dx = Mathf.Max(rad - x - 0.5f, x + 0.5f - (n - rad), 0), dy = Mathf.Max(rad - y - 0.5f, y + 0.5f - (n - rad), 0);
                        float e = Mathf.Sqrt(dx * dx + dy * dy) - rad;
                        float edge = Mathf.Min(Mathf.Min(x + 0.5f, n - x - 0.5f), Mathf.Min(y + 0.5f, n - y - 0.5f));
                        if (e > -3f || edge < 3f) c = Color.Lerp(c, new Color(1, 226 / 255f, 180 / 255f), 0.5f);
                        c.a = Mathf.Clamp01(0.5f - e);
                        t.SetPixel(x, y, c);
                    }
                t.Apply(); stampBg = Make(t, new Vector4(12, 12, 12, 12)); return stampBg;
            }
        }
        // 姓氏徽章：radial-gradient(circle at 34% 28%, 势力色 + 55% 白, 势力色 50%, 势力色 + 55% 黑)（每次新建，用完销毁）
        public static Texture2D Medal(Color fc)
        {
            const int n = 64; var t = Tex(n, n);
            Color c0 = ClashUI.Mix(fc, Color.white, 0.55f), c2 = ClashUI.Mix(fc, Color.black, 0.55f);
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = 1 - (y + 0.5f) / n;
                    float d = Mathf.Sqrt((u - 0.34f) * (u - 0.34f) + (v - 0.28f) * (v - 0.28f)) / 0.97f;
                    Color c = d < 0.5f ? Color.Lerp(c0, fc, d / 0.5f) : Color.Lerp(fc, c2, (d - 0.5f) / 0.5f);
                    c.a = 1;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }
    }

    // 退出后黑幕再淡去（此时已回到战场）
    internal class ClashFadeAway : MonoBehaviour
    {
        public Image fade; public float sec = 0.2f; float t;
        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (fade != null) { var c = fade.color; c.a = 1 - ClashUI.EaseOut(Mathf.Clamp01(t / Mathf.Max(0.01f, sec))); fade.color = c; }
            if (t >= sec + 0.12f) Destroy(gameObject);
        }
    }

    // ======================================================= HUD --
    internal sealed class ClashHud
    {
        static readonly Color[] SIDE_COLOR = { ClashUI.Css("#73bfff"), ClashUI.Css("#ff806b") };
        static readonly Color GOLDC = ClashUI.Css("#f3c969");
        readonly ClashScene sc;
        readonly Canvas canvas;
        readonly RectTransform root;
        float dpr, u, vw, vh;
        bool portrait;
        float safeL, safeR, safeT, safeB;
        Image vig, shadeImg, flashImg, fadeImg;
        RectTransform pops, stampRt, skipRt, modeRt, midRt;
        Text skipText, modeText, terrText, sealText, stampSmall, stampBig;
        Image sealImg, stampBgImg, skipBg, modeBg;
        ClashSpacing terrSp, stampSmallSp, skipSp, modeSp;
        float barBottom = -1;
        readonly List<UnityEngine.Object> own = new List<UnityEngine.Object>();
        // 状态：pre（进场前）→ live（播放中）→ out（淡出）
        bool live, fading;
        float liveT = -1, outT = -1;
        float fadeFrom, fadeTo, fadeDur, fadeT = -1; Func<float, float> fadeEase;
        Action fadeDone; bool fadeDoneCalled;
        float flashA; Color flashC;
        float stampT = -1; bool stampNow;
        bool handed;

        sealed class Side
        {
            public ClashInfo info; public int k;
            public RectTransform rt, picRt, metaRt, trRt, nameRt, formRt, numRt, barRt, lossRt, chipRt;
            public ClashPoly bg, border, shadow, fill;
            public Image goldRing, fcRing, picBg, portraitImg, medalImg, trail, barBg, chipBg;
            public Text name, chip, form, numSmall, num, loss, medalText;
            public ClashSpacing nameSp;
            public float max, shown, target, trailV, trailHold, hitT; public int lost;
            public bool me;
            public float w, h, px;  // 面板尺寸、头像像素
            public float baseX, barW; public bool barRight, lossOn;
            public Texture2D medalTex;
        }
        readonly Side[] sides = new Side[2];
        sealed class PopItem { public RectTransform rt; public Text text; public Vector3 world; public float age; public bool big; }
        readonly List<PopItem> popList = new List<PopItem>();

        public ClashHud(ClashScene sc)
        {
            this.sc = sc;
            canvas = ClashUI.NewCanvas("ClashHUD", 30, out dpr);
            root = (RectTransform)canvas.transform;
            ComputeUnits();
            Font body = ClashUtil.Body, kai = ClashUtil.Kai;
            // 整屏点击 = 跳过
            var hit = ClashUI.Img(root, null, new Color(0, 0, 0, 0), "Hit", true);
            ClashUI.Full(hit.rectTransform);
            hit.gameObject.AddComponent<ClashTap>().onDown = () => sc.Skip();
            vig = ClashUI.Img(root, ClashSprites.Vignette, Color.white, "Vignette"); ClashUI.Full(vig.rectTransform);
            shadeImg = ClashUI.Img(root, ClashSprites.Shade, Color.white, "Shade");
            // 两侧信息栏 + 中间「战」印
            for (int k = 0; k < 2; k++) sides[k] = MakeSide(k == 0 ? sc.A : sc.D, k, body, kai);
            midRt = ClashUI.Rect("Mid", root);
            sealImg = ClashUI.Img(midRt, ClashSprites.Seal, Color.white, "Seal");
            sealText = ClashUI.Label(sealImg.transform, "战", kai, 20, ClashUI.Css("#fff2e0"), TextAnchor.MiddleCenter, FontStyle.Bold);
            ClashUI.Full(sealText.rectTransform);
            string tn; if (!ClashScene.KIND_NAME.TryGetValue(sc.kind, out tn)) tn = "平原";
            terrText = ClashUI.Label(midRt, tn, body, 12, GOLDC, TextAnchor.UpperCenter);
            terrSp = terrText.gameObject.AddComponent<ClashSpacing>();
            ClashUI.AddShadow(terrText, new Color(0, 0, 0, 0.8f), 0, 1);
            pops = ClashUI.Rect("Pops", root); ClashUI.Full(pops);
            // 「溃散」印
            stampRt = ClashUI.Rect("Stamp", root);
            stampSmall = ClashUI.Label(stampRt, "", body, 13, ClashUI.Css("#f5eddb"), TextAnchor.MiddleCenter);
            stampSmallSp = stampSmall.gameObject.AddComponent<ClashSpacing>();
            ClashUI.AddShadow(stampSmall, new Color(0, 0, 0, 0.8f), 0, 2);
            stampBgImg = ClashUI.Img(stampRt, ClashSprites.StampBg, Color.white, "StampBg");
            stampBig = ClashUI.Label(stampBgImg.transform, "", kai, 34, ClashUI.Css("#fff3e4"), TextAnchor.MiddleCenter, FontStyle.Bold);
            stampBig.gameObject.AddComponent<ClashSpacing>();
            ClashUI.AddShadow(stampBig, new Color(0, 0, 0, 0.4f), 0, 2);
            stampRt.gameObject.SetActive(false);
            // 跳过提示与「动画 开 / 快 / 关」
            skipBg = ClashUI.Img(root, UIKit.RR, ClashUI.Rgba(10, 10, 16, 0.45f), "Skip");
            skipRt = skipBg.rectTransform;
            var skipBorder = ClashUI.Img(skipRt, UIKit.RROutline, ClashUI.Rgba(243, 201, 105, 0.3f), "Border"); ClashUI.Full(skipBorder.rectTransform);
            skipText = ClashUI.Label(skipRt, (ClashUtil.IsTouch ? "轻触跳过" : "点击 / 空格 跳过") + "<color=#f3c969>▶▶</color>", body, 12, ClashUI.Rgba(245, 237, 219, 0.82f), TextAnchor.MiddleCenter);
            ClashUI.Full(skipText.rectTransform);
            skipSp = skipText.gameObject.AddComponent<ClashSpacing>();
            modeBg = ClashUI.Img(root, UIKit.RR, ClashUI.Rgba(10, 10, 16, 0.45f), "Mode");
            modeRt = modeBg.rectTransform;
            var modeBorder = ClashUI.Img(modeRt, UIKit.RROutline, ClashUI.Rgba(243, 201, 105, 0.3f), "Border"); ClashUI.Full(modeBorder.rectTransform);
            modeText = ClashUI.Label(modeRt, "", body, 12, ClashUI.Rgba(245, 237, 219, 0.82f), TextAnchor.MiddleCenter);
            ClashUI.Full(modeText.rectTransform);
            modeSp = modeText.gameObject.AddComponent<ClashSpacing>();
            // 触摸命中区 ≥ 44px：外观不变，四周透明扩展
            var modeHit = ClashUI.Img(modeRt, null, new Color(0, 0, 0, 0), "Hit", true);
            modeHit.rectTransform.anchorMin = new Vector2(0, 0.5f); modeHit.rectTransform.anchorMax = new Vector2(1, 0.5f);
            modeHit.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            modeHit.gameObject.AddComponent<ClashTap>().onDown = OnMode;
            ModeLabel();
            flashImg = ClashUI.Img(root, null, new Color(1, 1, 1, 0), "Flash"); ClashUI.Full(flashImg.rectTransform);
            fadeImg = ClashUI.Img(root, null, ClashUI.Css("#0b0a0f", 0), "Fade"); ClashUI.Full(fadeImg.rectTransform);
            Layout();
            SetPre(true);
            if (sc.runnerHost != null) sc.runnerHost.StartCoroutine(LoadPortraits());
        }

        void ComputeUnits()
        {
            dpr = ClashUI.Dpr();
            var cs = canvas.GetComponent<CanvasScaler>(); if (cs != null) cs.scaleFactor = dpr;
            vw = Screen.width / dpr; vh = Screen.height / dpr;
            portrait = vw <= vh;
            if (portrait) u = Mathf.Clamp(Mathf.Min(vw * 0.023f, vh * 0.022f), 8, 18);
            else if (vh <= 540) u = Mathf.Clamp(Mathf.Min(vw * 0.013f, vh * 0.025f), 8, 26);
            else u = Mathf.Clamp(Mathf.Min(vw * 0.013f, vh * 0.0245f), 8.5f, 26);
            var sa = Screen.safeArea;
            safeL = sa.xMin / dpr; safeR = (Screen.width - sa.xMax) / dpr; safeB = sa.yMin / dpr; safeT = (Screen.height - sa.yMax) / dpr;
        }

        Side MakeSide(ClashInfo info, int k, Font body, Font kai)
        {
            var S = new Side { info = info, k = k };
            S.rt = ClashUI.Rect("Side" + k, root);
            S.shadow = S.rt.gameObject.AddComponent<ClashPoly>();       // 根物体本身画投影
            S.shadow.raycastTarget = false;
            S.shadow.Set(new[] { 0f, 1f }, new[] { new Color(0, 0, 0, 0.28f), new Color(0, 0, 0, 0.28f) });
            var brt = ClashUI.Rect("Border", S.rt); S.border = brt.gameObject.AddComponent<ClashPoly>(); S.border.raycastTarget = false;
            S.border.Set(new[] { 0f, 1f }, new[] { ClashUI.Rgba(243, 201, 105, 0.55f), ClashUI.Rgba(243, 201, 105, 0.55f) });
            var bgrt = ClashUI.Rect("Bg", S.rt); S.bg = bgrt.gameObject.AddComponent<ClashPoly>(); S.bg.raycastTarget = false;
            S.bg.Set(new[] { 0f, 1f }, new[] { ClashUI.Rgba(38, 34, 44, 0.93f), ClashUI.Rgba(14, 13, 20, 0.9f) });
            var c = SIDE_COLOR[info.side];
            var gs = ClashUI.Img(S.rt, ClashSprites.HGlow, new Color(c.r, c.g, c.b, 0.35f), "GlowSoft");
            var gl = ClashUI.Img(S.rt, ClashSprites.HGlow, new Color(c.r, c.g, c.b, 0.9f), "GlowLine");
            gs.name = "GlowSoft"; gl.name = "GlowLine";
            // 头像框：金边 + 势力色边 + 底色（圆角遮罩）
            S.goldRing = ClashUI.Img(S.rt, UIKit.RR, ClashUI.Rgba(243, 201, 105, 0.85f), "Gold");
            S.fcRing = ClashUI.Img(S.rt, UIKit.RR, info.color, "Fc");
            S.picBg = ClashUI.Img(S.rt, UIKit.RR, ClashUI.Css("#14121a"), "Pic");
            S.picRt = S.picBg.rectTransform;
            S.picBg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            S.medalTex = ClashSprites.Medal(info.color);
            own.Add(S.medalTex);
            var mraw = ClashUI.Raw(S.picRt, S.medalTex, Color.white, "Medal"); ClashUI.Full(mraw.rectTransform);
            S.medalText = ClashUI.Label(mraw.transform, ClashScene.Surname(info), kai, 20, ClashUI.Css("#fff6e2"), TextAnchor.MiddleCenter, FontStyle.Bold);
            ClashUI.Full(S.medalText.rectTransform);
            ClashUI.AddShadow(S.medalText, new Color(0, 0, 0, 0.45f), 0, 2);
            S.portraitImg = ClashUI.Img(S.picRt, null, Color.white, "Portrait"); ClashUI.Full(S.portraitImg.rectTransform);
            S.portraitImg.enabled = false;
            // 姓名、标签（攻方 / 守方 / 我军）、阵型
            S.me = sc.playerSide >= 0 && sc.playerSide == info.side;
            S.name = ClashUI.Label(S.rt, info.name, kai, 16, ClashUI.Css("#fff4dc"), k == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, FontStyle.Bold, "Name");
            S.nameSp = S.name.gameObject.AddComponent<ClashSpacing>();
            ClashUI.AddShadow(S.name, new Color(0, 0, 0, 0.6f), 0, 2);
            S.chipBg = ClashUI.Img(S.rt, UIKit.RR, S.me ? GOLDC : c, "Chip");
            S.chip = ClashUI.Label(S.chipBg.transform, S.me ? "我军" : (info.side == 0 ? "攻方" : "守方"), body, 11, ClashUI.Css("#111111"), TextAnchor.MiddleCenter, FontStyle.Bold);
            ClashUI.Full(S.chip.rectTransform);
            S.form = ClashUI.Label(S.rt, "<color=#f3c969>◆</color> " + ClashScene.FormName(info.formation) + "之阵", body, 12, ClashUI.Css("#d8cfbb"), k == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, FontStyle.Normal, "Form");
            // 兵力
            S.numSmall = ClashUI.Label(S.rt, "兵力", body, 11, ClashUI.Css("#ada392"), TextAnchor.LowerRight, FontStyle.Normal, "NumLabel");
            S.num = ClashUI.Label(S.rt, info.before.ToString(), body, 20, ClashUI.Css("#fff8e6"), TextAnchor.LowerLeft, FontStyle.Bold, "Num");
            ClashUI.AddShadow(S.num, new Color(0, 0, 0, 0.6f), 0, 2);
            S.barBg = ClashUI.Img(S.rt, UIKit.RR, new Color(0, 0, 0, 0.55f), "Bar");
            S.barRt = S.barBg.rectTransform;
            S.trail = ClashUI.Img(S.barRt, UIKit.RR, ClashUI.Css("#ff5a45"), "Trail");
            var frt = ClashUI.Rect("Fill", S.barRt); S.fill = frt.gameObject.AddComponent<ClashPoly>(); S.fill.raycastTarget = false; S.fill.axis = 0;
            S.fill.Set(new[] { 0f, 1f }, new[] { c, ClashUI.Mix(c, Color.white, 0.6f) });
            S.loss = ClashUI.Label(S.rt, "", kai, 13, ClashUI.Css("#ff5b4d"), k == 0 ? TextAnchor.UpperRight : TextAnchor.UpperLeft, FontStyle.Bold, "Loss");
            ClashUI.AddShadow(S.loss, ClashUI.Css("#2a0703"), 0, 1);
            S.loss.color = new Color(S.loss.color.r, S.loss.color.g, S.loss.color.b, 0);
            int max = info.before;
            if (info.gen != null && info.gen.war > 0) max = Mathf.Max(max, info.gen.MaxTroops);
            S.max = Mathf.Max(1, max);
            S.shown = S.target = S.trailV = info.before;
            return S;
        }

        // ---------------------------------------------------------- 排版 --
        public void Layout()
        {
            ComputeUnits();
            float gap = portrait ? u * 0.45f : u * 1.3f;
            float padT = u * 0.7f + safeT;
            float padL = (portrait ? u * 0.5f : u * 1.1f) + safeL, padR = (portrait ? u * 0.5f : u * 1.1f) + safeR;
            // 中间印章与地形
            float sealW = portrait ? u * 3 : u * 3.6f;
            float terrFs = Mathf.Max(11, u * 0.92f);
            terrText.fontSize = Mathf.RoundToInt(terrFs); terrSp.Set(terrFs * 0.3f);
            float terrW = ClashUI.Width(terrText, terrFs * 0.3f) + terrFs * 0.3f;
            float midW = Mathf.Max(sealW, terrW);
            float midH = u * 0.35f + sealW + u * 0.3f + terrFs * 1.25f;
            float avail = vw - padL - padR;
            float sideW = Mathf.Max(40, Mathf.Min(u * 36, (avail - midW - 2 * gap) / 2));
            float total = 2 * sideW + midW + 2 * gap;
            float x0 = padL + Mathf.Max(0, (avail - total) / 2);
            float sideH = 0;
            for (int k = 0; k < 2; k++) sideH = Mathf.Max(sideH, LayoutSide(sides[k], sideW));
            float sx0 = x0, sx1 = x0 + sideW + gap + midW + gap;
            PlaceSide(sides[0], sx0, padT);
            PlaceSide(sides[1], sx1, padT);
            ClashUI.PlaceC(midRt, x0 + sideW + gap + midW / 2, padT + midH / 2, midW, midH);
            ClashUI.PlaceC(sealImg.rectTransform, midW / 2, u * 0.35f + sealW / 2, sealW, sealW);
            sealImg.rectTransform.localEulerAngles = new Vector3(0, 0, 6);
            sealText.fontSize = Mathf.RoundToInt(portrait ? u * 2 : u * 2.4f);
            ClashUI.Place(terrText.rectTransform, 0, u * 0.35f + sealW + u * 0.3f, midW + terrFs * 0.3f, terrFs * 1.3f);
            barBottom = padT + Mathf.Max(sideH, midH);
            // 顶部压暗带
            ClashUI.Place(shadeImg.rectTransform, 0, 0, vw, (portrait ? u * 13 : u * 11) + safeT);
            // 跳过与动画按钮
            float chipFs = Mathf.Max(11, u * 0.95f);
            skipText.fontSize = modeText.fontSize = Mathf.RoundToInt(chipFs);
            skipSp.Set(chipFs * 0.12f); modeSp.Set(chipFs * 0.12f);
            float skipW = ClashUI.Width(skipText, chipFs * 0.12f) + chipFs * 1.6f, skipH = chipFs * 1.2f + chipFs * 0.7f;
            ClashUI.Place(skipRt, vw - (u * 1.2f + safeR) - skipW, vh - (u * 1.0f + safeB) - skipH, skipW, skipH);
            ClashUI.Round(skipBg, skipH / 2); ClashUI.Round(skipRt.Find("Border").GetComponent<Image>(), skipH / 2);
            ModeLabel();
            float modeH = Mathf.Max(30, u * 2.4f), modeW = ClashUI.Width(modeText, chipFs * 0.12f) + chipFs * 1.8f;
            ClashUI.Place(modeRt, u * 1.2f + safeL, vh - (u * 1.0f + safeB) - modeH, modeW, modeH);
            ClashUI.Round(modeBg, modeH / 2); ClashUI.Round(modeRt.Find("Border").GetComponent<Image>(), modeH / 2);
            var mh = (RectTransform)modeRt.Find("Hit");
            mh.sizeDelta = new Vector2(20, Mathf.Max(48, modeH + 16) - modeH);
            mh.anchoredPosition = Vector2.zero;
            // 「溃散」印
            float sfs = Mathf.Max(13, u * 1.3f), bfs = Mathf.Max(34, u * 5.2f);
            stampSmall.fontSize = Mathf.RoundToInt(sfs); stampSmallSp.Set(sfs * 0.2f);
            stampBig.fontSize = Mathf.RoundToInt(bfs); stampBig.GetComponent<ClashSpacing>().Set(bfs * 0.06f);
            float bw = ClashUI.Width(stampBig, bfs * 0.06f) + bfs * 0.6f, bh = bfs * 1.28f;
            float sh = sfs * 1.3f + u * 0.4f + bh, sw = Mathf.Max(bw, ClashUI.Width(stampSmall, sfs * 0.2f)) + 40;
            ClashUI.PlaceC(stampRt, vw / 2, vh * 0.44f, sw, sh);
            stampSmall.rectTransform.anchorMin = stampSmall.rectTransform.anchorMax = new Vector2(0.5f, 1);
            stampSmall.rectTransform.pivot = new Vector2(0.5f, 1);
            stampSmall.rectTransform.anchoredPosition = Vector2.zero; stampSmall.rectTransform.sizeDelta = new Vector2(sw, sfs * 1.3f);
            var bgr = stampBgImg.rectTransform;
            bgr.anchorMin = bgr.anchorMax = new Vector2(0.5f, 0); bgr.pivot = new Vector2(0.5f, 0.5f);
            bgr.anchoredPosition = new Vector2(0, bh / 2); bgr.sizeDelta = new Vector2(bw, bh);
            bgr.localEulerAngles = new Vector3(0, 0, 5);
            ClashUI.Full(stampBig.rectTransform);
            stampBgImg.pixelsPerUnitMultiplier = 12f / Mathf.Max(1, bfs * 0.12f);
        }

        // 计算一侧面板的内部排版，返回面板高度
        float LayoutSide(Side S, float w)
        {
            bool s1 = S.k == 1;
            float nameFs = portrait ? Mathf.Max(14, u * 1.6f) : Mathf.Max(15, u * 1.75f);
            float chipFs = Mathf.Max(11, u * 0.85f), formFs = Mathf.Max(11, u * 1.0f), smallFs = Mathf.Max(11, u * 0.85f);
            float numFs = portrait ? Mathf.Max(17, u * 2.1f) : Mathf.Max(18, u * 2.35f);
            float lossFs = Mathf.Max(13, u * 1.25f), lossH = Mathf.Max(14, u * 1.3f), barH = Mathf.Max(5, u * 0.48f);
            S.name.fontSize = Mathf.RoundToInt(nameFs); S.nameSp.Set(nameFs * 0.04f);
            S.chip.fontSize = Mathf.RoundToInt(chipFs);
            S.form.fontSize = Mathf.RoundToInt(formFs);
            S.numSmall.fontSize = Mathf.RoundToInt(smallFs);
            S.num.fontSize = Mathf.RoundToInt(numFs);
            S.loss.fontSize = Mathf.RoundToInt(lossFs);
            S.form.gameObject.SetActive(!portrait);
            S.numSmall.gameObject.SetActive(!portrait);
            float picS = portrait ? u * 4.4f : u * 5.4f;
            float h, padL, padR, padTop = 0;
            float nameH = nameFs * 1.2f, formH = formFs * 1.2f, numH = numFs * 1.2f;
            float metaH = portrait ? nameH : nameH + u * 0.25f + formH;
            float trH = numH + u * 0.2f + barH + u * 0.2f + lossH;
            float numW = ClashUI.Width(S.num) + (portrait ? 0 : ClashUI.Width(S.numSmall) + smallFs * 0.25f);
            float trW = portrait ? 0 : Mathf.Max(u * 7.4f, numW);
            if (portrait)
            {
                padL = s1 ? u * 1.1f : u * 0.6f; padR = s1 ? u * 0.6f : u * 1.1f;
                padTop = u * 0.6f;
                h = padTop + Mathf.Max(picS, metaH + u * 0.2f + trH) + u * 0.5f;
            }
            else
            {
                padL = s1 ? u * 1.4f : u * 0.6f; padR = s1 ? u * 0.6f : u * 1.4f;
                h = u * 6.4f;
            }
            S.w = w; S.h = h;
            // 背景（平行四边形；skewX(∓14deg)，竖屏 ∓7deg，底边不动）
            float skew = h * Mathf.Tan((portrait ? 7 : 14) * Mathf.Deg2Rad) * (s1 ? -1 : 1);
            ClashUI.Place(S.border.rectTransform, 0, 0, w, h); S.border.skew = skew; S.border.SetVerticesDirty();
            ClashUI.Place(S.bg.rectTransform, 1, 1, w - 2, h - 2); S.bg.skew = skew * (h - 2) / h; S.bg.SetVerticesDirty();
            S.shadow.skew = skew; S.shadow.SetVerticesDirty();
            var gs = (RectTransform)S.rt.Find("GlowSoft"); var gl = (RectTransform)S.rt.Find("GlowLine");
            float gh = u * 0.22f;
            ClashUI.Place(gl, w * 0.04f, h + 1 - gh, w * 0.92f, gh);
            ClashUI.Place(gs, w * 0.02f, h + 1 - gh - u * 0.3f, w * 0.96f, gh + u * 0.6f);
            // 头像
            float px, py;
            if (portrait) { px = s1 ? w - padR - picS : padL; py = padTop + (Mathf.Max(picS, metaH + u * 0.2f + trH) - picS) / 2; }
            else { px = s1 ? w - padR - picS : padL; py = (h - picS) / 2 + u * 0.45f; }
            ClashUI.Place(S.goldRing.rectTransform, px - 3.5f, py - 3.5f, picS + 7, picS + 7); ClashUI.Round(S.goldRing, u * 0.5f + 3.5f);
            ClashUI.Place(S.fcRing.rectTransform, px - 2, py - 2, picS + 4, picS + 4); ClashUI.Round(S.fcRing, u * 0.5f + 2);
            ClashUI.Place(S.picRt, px, py, picS, picS); ClashUI.Round(S.picBg, u * 0.5f);
            S.medalText.fontSize = Mathf.RoundToInt(u * 3.2f * picS / (u * 5.4f));
            S.px = picS;
            // 文字区
            float gapI = portrait ? u * 0.6f : u * 0.85f;
            float metaX0 = s1 ? padL + (portrait ? 0 : trW + gapI) : px + picS + gapI;
            float metaX1 = s1 ? px - gapI : w - padR - (portrait ? 0 : trW + gapI);
            float metaW = Mathf.Max(10, metaX1 - metaX0);
            float metaY = portrait ? padTop : (h - metaH) / 2;
            // 姓名 + 标签
            float chipW = ClashUI.Width(S.chip) + chipFs * 0.9f, chipH = chipFs * 1.44f;
            float nameW = Mathf.Min(ClashUI.Width(S.name, nameFs * 0.04f) + 2, Mathf.Max(10, metaW - chipW - u * 0.45f));
            S.name.horizontalOverflow = HorizontalWrapMode.Wrap;
            S.name.verticalOverflow = VerticalWrapMode.Truncate;
            if (!s1)
            {
                ClashUI.Place(S.name.rectTransform, metaX0, metaY, nameW, nameH);
                ClashUI.Place(S.chipBg.rectTransform, metaX0 + nameW + u * 0.45f, metaY + (nameH - chipH) / 2, chipW, chipH);
            }
            else
            {
                ClashUI.Place(S.name.rectTransform, metaX1 - nameW, metaY, nameW, nameH);
                ClashUI.Place(S.chipBg.rectTransform, metaX1 - nameW - u * 0.45f - chipW, metaY + (nameH - chipH) / 2, chipW, chipH);
            }
            ClashUI.Round(S.chipBg, chipFs * 0.35f);
            ClashUI.Place(S.form.rectTransform, metaX0, metaY + nameH + u * 0.25f, metaW, formH);
            // 兵力、兵力条、伤亡
            float trX, trY, trWW;
            if (portrait) { trX = metaX0; trWW = metaW; trY = metaY + metaH + u * 0.2f; }
            else { trWW = trW; trX = s1 ? padL : w - padR - trW; trY = (h - trH) / 2; }
            float numTextW = ClashUI.Width(S.num);
            float smallW = portrait ? 0 : ClashUI.Width(S.numSmall);
            // 网页版：横屏时兵力块右对齐（守方左对齐）；竖屏时攻方左对齐、守方右对齐
            bool alignRight = portrait ? s1 : !s1;
            float rowW = numTextW + (portrait ? 0 : smallW + smallFs * 0.25f);
            float rx = alignRight ? trX + trWW - rowW : trX;
            S.num.alignment = alignRight ? TextAnchor.LowerRight : TextAnchor.LowerLeft; S.numSmall.alignment = TextAnchor.LowerLeft;
            if (!portrait) ClashUI.Place(S.numSmall.rectTransform, rx, trY, smallW + 2, numH - numFs * 0.12f);
            float numX = rx + (portrait ? 0 : smallW + smallFs * 0.25f);
            ClashUI.Place(S.num.rectTransform, alignRight ? numX - 4 : numX, trY, numTextW + 4, numH);
            float barW = portrait ? trWW : u * 7.4f;
            float bx = alignRight ? trX + trWW - barW : trX;
            ClashUI.Place(S.barRt, bx, trY + numH + u * 0.2f, barW, barH);
            ClashUI.Round(S.barBg, Mathf.Min(3, barH / 2));
            S.barW = barW; S.barRight = s1;
            ClashUI.Place(S.loss.rectTransform, trX, trY + numH + u * 0.2f + barH + u * 0.2f, trWW, lossH);
            S.loss.alignment = alignRight ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            SetBar(S);
            return h;
        }
        void PlaceSide(Side S, float x, float y)
        {
            ClashUI.Place(S.rt, x, y, S.w, S.h);
            S.baseX = x;
            // 投影：box-shadow 0 u*.3 u*1.2 rgba(0,0,0,.45) 的近似（下移、略放大）
            S.shadow.shift = new Vector2(0, -u * 0.3f); S.shadow.grow = u * 0.3f; S.shadow.SetVerticesDirty();
        }

        void SetBar(Side S)
        {
            float f = Mathf.Clamp01(S.shown / S.max), tr = Mathf.Clamp01(S.trailV / S.max);
            var fr = S.fill.rectTransform; var trt = S.trail.rectTransform;
            if (S.barRight)
            {
                fr.anchorMin = new Vector2(1 - f, 0); fr.anchorMax = Vector2.one;
                trt.anchorMin = new Vector2(1 - tr, 0); trt.anchorMax = Vector2.one;
            }
            else
            {
                fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(f, 1);
                trt.anchorMin = Vector2.zero; trt.anchorMax = new Vector2(tr, 1);
            }
            fr.pivot = trt.pivot = new Vector2(0.5f, 0.5f);
            fr.offsetMin = fr.offsetMax = trt.offsetMin = trt.offsetMax = Vector2.zero;
            ClashUI.Round(S.trail, Mathf.Min(3, Mathf.Max(1, S.barRt.rect.height / 2)));
        }

        void ModeLabel()
        {
            if (modeText != null) modeText.text = "动画<color=#f3c969><b> " + Clash.ModeLabel() + "</b></color>";
        }
        void OnMode()
        {
            var m = Clash.CycleMode();
            ModeLabel();
            Layout();
            if (m == ClashMode.Off) sc.Skip();
            else sc.speed = Mathf.Max(0.1f, Clash.Speed * sc.optSpeed);
        }

        // ---------------------------------------------------------- 头像 --
        IEnumerator LoadPortraits()
        {
            var jobs = new List<KeyValuePair<PortraitGen, PortraitOpts>>();
            var want = new List<KeyValuePair<Side, int>>();
            foreach (var S in sides)
            {
                if (S.info.gen == null || S.info.name == "无名") continue;
                int px = Mathf.Max(32, Mathf.RoundToInt(u * 5.6f * dpr));
                jobs.Add(new KeyValuePair<PortraitGen, PortraitOpts>(Portrait.Describe(S.info.gen), PortraitOpts.Make(px, ClashUtil.Hex(S.info.color), "angry", false, S.k == 0)));
                want.Add(new KeyValuePair<Side, int>(S, px));
            }
            // 必杀特写的头像也先备好
            if (sc.special != null && sc.A.gen != null && sc.A.name != "无名")
                jobs.Add(new KeyValuePair<PortraitGen, PortraitOpts>(Portrait.Describe(sc.A.gen), PortraitOpts.Make(CutInView.PortraitPx(), ClashUtil.Hex(sc.special.color), "angry", false, sc.A.side == 0)));
            if (jobs.Count > 0)
            {
                IEnumerator it = null;
                try { it = Portrait.PreloadAsync(jobs, 6f); } catch (Exception e) { Debug.LogWarning("clash portrait: " + e.Message); }
                while (it != null)
                {
                    bool more;
                    try { more = it.MoveNext(); } catch (Exception e) { Debug.LogWarning("clash portrait: " + e.Message); break; }
                    if (!more) break;
                    yield return it.Current;
                }
            }
            foreach (var kv in want)
            {
                var S = kv.Key;
                if (S.portraitImg == null) continue;
                Texture2D tex = null;
                try { tex = Portrait.Cached(S.info.gen, kv.Value, S.info.color, PortraitMood.Angry, false, S.k == 0); } catch (Exception) { /* 徽章 */ }
                if (tex == null) continue;
                S.portraitImg.sprite = Portrait.SpriteOf(tex);
                S.portraitImg.enabled = true;
                S.portraitImg.transform.parent.Find("Medal").gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------- 状态 --
        void SetPre(bool pre)
        {
            vig.enabled = !pre; shadeImg.enabled = !pre;
            skipRt.gameObject.SetActive(!pre); modeRt.gameObject.SetActive(!pre);
            if (pre)
            {
                // 进场前两侧栏与印章不可见（首帧前就设好，免得闪一下）
                foreach (var S in sides) SetGroupAlpha(S.rt, 0);
                SetGroupAlpha(midRt, 0);
                SetGroupAlpha(skipRt, 0); SetGroupAlpha(modeRt, 0);
            }
        }
        // 进场前：战场上淡入黑幕
        public void FadeBlack(float sec) { StartFade(0, 1, sec, ClashUI.EaseIn, null); }
        public void FadeIn(float sec)
        {
            StartFade(1, 0, sec, ClashUI.EaseOut, null);
            live = true; liveT = 0;
            SetPre(false);
        }
        public void FadeOut(float sec, Action done)
        {
            fading = true; live = false; outT = 0;
            StartFade(fadeImg.color.a, 1, sec, ClashUI.EaseIn, done);
            fadeOutSec = sec;
        }
        float fadeOutSec = 0.2f;
        void StartFade(float a, float b, float sec, Func<float, float> ease, Action done)
        {
            fadeFrom = a; fadeTo = b; fadeDur = Mathf.Max(0.01f, sec); fadeT = 0; fadeEase = ease; fadeDone = done; fadeDoneCalled = false;
            var c = fadeImg.color; c.a = a; fadeImg.color = c;
        }
        public void Flash(Color c, float a)
        {
            flashC = c; flashA = a;
        }
        public void Loss(int si, int amount)
        {
            var S = sides[si];
            S.target = Mathf.Max(S.info.after, S.target - amount);
            S.lost += amount;
            S.loss.text = "−" + S.lost;
            S.lossOn = true;
            S.hitT = 0.3f;
            S.trailHold = 0.35f;
        }
        public void Pop(Vector3 world, string text, bool big)
        {
            var rt = ClashUI.Rect("Pop", pops);
            float fs = big ? Mathf.Max(28, u * 4.2f) : Mathf.Max(22, u * 3.1f);
            var t = ClashUI.Label(rt, text, ClashUtil.Kai, fs, big ? ClashUI.Css("#ffd24d") : ClashUI.Css("#ff4a3a"), TextAnchor.MiddleCenter, FontStyle.Bold);
            ClashUI.Full(t.rectTransform);
            ClashUI.AddOutline(t, ClashUI.Css("#2b0602"), Mathf.Max(1, u * 0.09f));
            ClashUI.AddShadow(t, ClashUI.Css("#2b0602"), 0, u * 0.18f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(fs * 6, fs * 1.4f);
            var p = new PopItem { rt = rt, text = t, world = world, age = 0, big = big };
            popList.Add(p);
            PlacePop(p);
        }
        public void Stamp(string sub, string text, bool instant)
        {
            stampSmall.text = sub; stampBig.text = text;
            stampRt.gameObject.SetActive(true);
            stampT = 0; stampNow = instant;
            Layout();
        }
        // 直接显示最终结果
        public void Final()
        {
            foreach (var S in sides)
            {
                S.target = S.info.after; S.shown = S.info.after; S.trailV = S.info.after;
                S.lost = S.info.loss;
                if (S.lost > 0) { S.loss.text = "−" + S.lost; S.lossOn = true; }
                S.num.text = S.info.after.ToString();
                SetBar(S);
            }
            if (sc.Skipped) { foreach (var p in popList) if (p.rt != null) UnityEngine.Object.Destroy(p.rt.gameObject); popList.Clear(); }
        }
        // HUD 顶栏下沿在 NDC 中的 y（1 = 画面顶）
        public float BarBottomNdc() { return barBottom < 0 || vh <= 0 ? 1 : 1 - 2 * barBottom / vh; }

        // ---------------------------------------------------------- 每帧 --
        public void Update(float dt, float popDt)
        {
            if (canvas == null) return;
            if (Screen.width / dpr != vw || Screen.height / dpr != vh) Layout();
            // 黑幕
            if (fadeT >= 0)
            {
                fadeT += dt;
                float p = Mathf.Clamp01(fadeT / fadeDur);
                var c = fadeImg.color; c.a = Mathf.Lerp(fadeFrom, fadeTo, fadeEase != null ? fadeEase(p) : p); fadeImg.color = c;
                if (p >= 1 && fadeDone != null && !fadeDoneCalled) { fadeDoneCalled = true; var d = fadeDone; fadeDone = null; d(); return; }
            }
            // 进场：两侧滑入、印章弹出；跳过提示与动画按钮延迟 .5 秒淡入
            if (liveT >= 0) liveT += dt;
            float lt = Mathf.Max(0, liveT);
            for (int k = 0; k < 2; k++)
            {
                var S = sides[k];
                float pIn = live || fading ? ClashUI.Bezier(0.2f, 0.8f, 0.2f, 1, Mathf.Clamp01(lt / 0.45f)) : 0;
                float off = (1 - pIn) * 1.15f * S.w * (k == 0 ? -1 : 1);
                S.rt.anchoredPosition = new Vector2(S.baseX + off, S.rt.anchoredPosition.y);
                float op = live || fading ? ClashUI.Ease(Mathf.Clamp01(lt / 0.3f)) : 0;
                SetGroupAlpha(S.rt, op);
            }
            {
                float pm = live || fading ? ClashUI.Bezier(0.17f, 0.89f, 0.32f, 1.3f, Mathf.Clamp01(lt / 0.4f)) : 0;
                float s = Mathf.LerpUnclamped(0.3f, 1, pm);
                midRt.localScale = new Vector3(s, s, 1);
                SetGroupAlpha(midRt, live || fading ? Mathf.Clamp01(lt / 0.3f) : 0);
            }
            // 淡出时保持原样（黑幕会盖住；网页版的 .5 秒延迟使其在黑幕落下前不变）
            if (live) chipFade = ClashUI.Ease(Mathf.Clamp01((lt - 0.5f) / 0.4f));
            SetGroupAlpha(skipRt, chipFade); SetGroupAlpha(modeRt, chipFade);
            // 兵力数字滚动、受击变色、扣减拖尾
            foreach (var S in sides)
            {
                if (S.shown > S.target)
                {
                    float d = S.shown - S.target;
                    S.shown = Mathf.Max(S.target, S.shown - Mathf.Max(d * dt * 7, dt * 300));
                    S.num.text = Mathf.RoundToInt(S.shown).ToString();
                }
                if (S.trailHold > 0) S.trailHold -= dt;
                else if (S.trailV > S.shown) S.trailV = Mathf.Max(S.shown, S.trailV - Mathf.Max((S.trailV - S.shown) * dt * 4, dt * 120));
                SetBar(S);
                if (S.hitT > 0) S.hitT -= dt;
                // color 过渡 .2s
                var target = S.hitT > 0 ? ClashUI.Css("#ff7a66") : ClashUI.Css("#fff8e6");
                S.num.color = Color.Lerp(S.num.color, target, Mathf.Clamp01(dt / 0.2f * 2.5f));
                var lc = S.loss.color; lc.a = Mathf.MoveTowards(lc.a, S.lossOn ? 1 : 0, dt / 0.2f); S.loss.color = lc;
            }
            // 闪屏
            if (flashA > 0)
            {
                flashImg.color = new Color(flashC.r, flashC.g, flashC.b, flashC.a * flashA);
                flashA = Mathf.Max(0, flashA - dt * 2.6f);
                if (flashA == 0) flashImg.color = new Color(1, 1, 1, 0);
            }
            // 「溃散」印：.42 秒 cubic-bezier(.2,.9,.25,1.2)：scale 2.4 → .94（60%）→ 1
            if (stampT >= 0)
            {
                stampT += dt;
                float p = stampNow ? 1 : Mathf.Clamp01(stampT / 0.42f);
                Func<float, float> e = x => ClashUI.Bezier(0.2f, 0.9f, 0.25f, 1.2f, x);
                float s = ClashUI.Keys(p, new[] { 0f, 2.4f, 0.6f, 0.94f, 1f, 1f }, e);
                float a = ClashUI.Keys(p, new[] { 0f, 0f, 0.6f, 1f, 1f, 1f }, e);
                stampRt.localScale = new Vector3(s, s, 1);
                SetGroupAlpha(stampRt, a);
            }
            // 飘字跟随三维位置：1.15 秒关键帧（cubic-bezier(.2,.8,.2,1)）
            for (int i = popList.Count - 1; i >= 0; i--)
            {
                var p = popList[i];
                p.age += popDt;
                if (p.age > 1.2f || p.rt == null) { if (p.rt != null) UnityEngine.Object.Destroy(p.rt.gameObject); popList.RemoveAt(i); continue; }
                PlacePop(p);
            }
        }
        float chipFade;
        void PlacePop(PopItem p)
        {
            Func<float, float> e = x => ClashUI.Bezier(0.2f, 0.8f, 0.2f, 1, x);
            float q = Mathf.Clamp01(p.age / 1.15f);
            float s = ClashUI.Keys(q, new[] { 0f, 2.1f, 0.12f, 0.92f, 0.22f, 1.05f, 0.32f, 1f, 1f, 0.96f }, e);
            float a = ClashUI.Keys(q, new[] { 0f, 0f, 0.12f, 1f, 0.75f, 1f, 1f, 0f }, e);
            float rise = ClashUI.Keys(q, new[] { 0f, 0f, 0.32f, 0f, 1f, -u * 3.2f }, e);
            var c = p.text.color; c.a = a; p.text.color = c;
            p.rt.localScale = new Vector3(s, s, 1);
            Vector2 pos = new Vector2(-9999, -9999);
            if (sc.cam != null)
            {
                var sp = sc.cam.WorldToScreenPoint(Clash.Origin + ClashUtil.P(p.world.x, p.world.y, p.world.z));
                if (sp.z > 0) pos = new Vector2(sp.x / dpr, -(Screen.height - sp.y) / dpr);
            }
            p.rt.anchorMin = p.rt.anchorMax = new Vector2(0, 1);
            p.rt.anchoredPosition = new Vector2(pos.x, pos.y - rise);
        }
        static void SetGroupAlpha(RectTransform rt, float a)
        {
            var g = rt.GetComponent<CanvasGroup>();
            if (g == null) { g = rt.gameObject.AddComponent<CanvasGroup>(); g.interactable = false; }
            g.alpha = a;
            g.blocksRaycasts = a > 0.01f;
        }

        public void Dispose()
        {
            foreach (var o in own) if (o != null) UnityEngine.Object.Destroy(o);
            own.Clear();
            if (canvas == null) return;
            if (fading && fadeDoneCalled && !handed)
            {
                // 退出后黑幕再淡去：只留黑幕
                handed = true;
                foreach (Transform c in root) if (c != fadeImg.transform) c.gameObject.SetActive(false);
                canvas.GetComponent<GraphicRaycaster>().enabled = false;
                var f = canvas.gameObject.AddComponent<ClashFadeAway>();
                f.fade = fadeImg; f.sec = fadeOutSec;
                return;
            }
            UnityEngine.Object.Destroy(canvas.gameObject);
        }
    }

    // ======================================================= 特写 CutIn --
    // 斜向色带（速度线）+ 头像滑入 + 毛笔飞白上的招式名大字 + 台词。
    // 时长 = 1.1 秒 ÷（Clash.Speed × speed），最短 0.7 秒；轻点 / 空格 / 回车 / Esc 跳过（开始 0.12 秒后才接受）。
    internal sealed class CutInView : MonoBehaviour
    {
        public bool Ended;
        public Action onEnd;
        float dur, t, skipAt = -1;
        int side;
        Color col;
        float dpr, u, vw, vh;
        bool portrait, light;
        Canvas canvas;
        RectTransform rootRt, picRt, textRt, nameRt;
        Image dim, flash;
        CutBand band;
        Image goldRing, colRing, picBg, portraitImg;
        RawImage medal, swash;
        Text who, nameText, cry, medalText;
        ClashSpacing nameSp, whoSp;
        CanvasGroup picG, textG, cryG, bandG;
        General gen;
        int px;
        Texture2D medalTex, swashTex;
        float nameW, nameH, swashW;
        float nk = 1;

        public static int PortraitPx() { return Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.58f), 64, 512); }

        public static CutInView Open(General g, string moveName, Color color, int side, string cry, float speed)
        {
            if (!Clash.Enabled) return null;
            if (Clash.cut != null) Clash.cut.End();
            try
            {
                var go = new GameObject("ClashCutIn");
                var v = go.AddComponent<CutInView>();
                Clash.cut = v;
                v.Init(g, moveName, color, side, cry, speed);
                return v;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Clash.cut != null) Clash.cut.End();
                return null;
            }
        }

        void Init(General g, string moveName, Color color, int sd, string cryText, float speed)
        {
            gen = g;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);   // 空格 / 回车只用于跳过
            float spd = Mathf.Max(0.1f, Clash.Speed * (speed > 0 ? speed : 1));
            dur = Mathf.Max(0.7f, 1.1f / spd);
            side = sd == 1 ? 1 : 0;
            col = new Color(color.r, color.g, color.b, 1);
            light = ClashUtil.Luma(col) > 0.62f;
            canvas = ClashUI.NewCanvas("Canvas", 40, out dpr);
            canvas.transform.SetParent(transform, false);
            rootRt = (RectTransform)canvas.transform;
            vw = Screen.width / dpr; vh = Screen.height / dpr;
            portrait = vw <= vh;
            u = portrait ? Mathf.Clamp(Mathf.Min(vw * 0.023f, vh * 0.022f), 8, 18) : Mathf.Clamp(Mathf.Min(vw * 0.013f, vh * 0.0245f), 8, 26);
            Font kai = ClashUtil.Kai;
            // 整屏点击 = 跳过
            var hit = ClashUI.Img(rootRt, null, new Color(0, 0, 0, 0), "Hit", true);
            ClashUI.Full(hit.rectTransform);
            hit.gameObject.AddComponent<ClashTap>().onDown = Skip;
            dim = ClashUI.Img(rootRt, null, ClashUI.Rgba(6, 5, 10, 0.62f), "Dim"); ClashUI.Full(dim.rectTransform);
            // 色带
            var brt = ClashUI.Rect("Band", rootRt);
            band = brt.gameObject.AddComponent<CutBand>();
            band.raycastTarget = false;
            band.Setup(col, light, side, ClashUtil.HashStr((moveName ?? "") + side), u);
            bandG = brt.gameObject.AddComponent<CanvasGroup>();
            // 头像
            goldRing = ClashUI.Img(rootRt, UIKit.RR, ClashUI.Rgba(243, 201, 105, 0.9f), "Gold");
            picRt = goldRing.rectTransform;
            picG = goldRing.gameObject.AddComponent<CanvasGroup>();
            colRing = ClashUI.Img(picRt, UIKit.RR, col, "Color");
            picBg = ClashUI.Img(picRt, UIKit.RR, ClashUI.Css("#14121a"), "Pic");
            picBg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            medalTex = ClashSprites.Medal(col);
            medal = ClashUI.Raw(picBg.rectTransform, medalTex, Color.white, "Medal"); ClashUI.Full(medal.rectTransform);
            string glyph = g != null && !string.IsNullOrEmpty(g.name) ? g.name.Substring(0, 1) : "将";
            medalText = ClashUI.Label(medal.transform, glyph, kai, 40, ClashUI.Css("#fff6e2"), TextAnchor.MiddleCenter, FontStyle.Bold);
            ClashUI.Full(medalText.rectTransform);
            ClashUI.AddShadow(medalText, new Color(0, 0, 0, 0.45f), 0, 2);
            portraitImg = ClashUI.Img(picBg.rectTransform, null, Color.white, "Portrait"); ClashUI.Full(portraitImg.rectTransform);
            portraitImg.enabled = false;
            px = PortraitPx();
            if (g != null && !string.IsNullOrEmpty(g.name))
            {
                Texture2D tex = null;
                try { tex = Portrait.Cached(g, px, col, PortraitMood.Angry, false, side == 0); } catch (Exception) { /* 徽章 */ }
                if (tex != null) ShowPortrait(tex); else StartCoroutine(LoadPortrait());
            }
            // 文字：姓名、招式名（毛笔飞白底）、台词
            textRt = ClashUI.Rect("Text", rootRt);
            textG = textRt.gameObject.AddComponent<CanvasGroup>();
            who = ClashUI.Label(textRt, g != null ? g.name : "", kai, 16, ClashUI.Css("#fff3dc"), side == 0 ? TextAnchor.LowerLeft : TextAnchor.LowerRight, FontStyle.Bold, "Who");
            whoSp = who.gameObject.AddComponent<ClashSpacing>();
            ClashUI.AddOutline(who, ClashUI.Rgba(10, 6, 4, 0.95f), 1);
            ClashUI.AddShadow(who, new Color(0, 0, 0, 0.8f), 0, 2);
            nameRt = ClashUI.Rect("Name", textRt);
            string nm = string.IsNullOrEmpty(moveName) ? "必杀" : moveName;
            swashTex = SwashTexture(ClashUtil.HashStr(moveName ?? ""), col);
            swash = ClashUI.Raw(nameRt, swashTex, Color.white, "Swash");
            nameText = ClashUI.Label(nameRt, nm, kai, 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Move");
            var vg = nameText.gameObject.AddComponent<ClashVGrad>();
            vg.stops = new[] { 0f, 0.42f, 0.62f, 1f };
            vg.colors = new[] { ClashUI.Css("#ffffff"), ClashUI.Css("#fff4cf"), ClashUI.Css("#ffd76a"), ClashUI.Css("#f3a93b") };
            nameSp = nameText.gameObject.AddComponent<ClashSpacing>();
            ClashUI.AddOutline(nameText, ClashUI.Rgba(30, 12, 4, 0.95f), 1);
            ClashUI.AddShadow(nameText, ClashUI.Rgba(20, 8, 2, 0.85f), 0, u * 0.25f);
            ClashUI.AddOutline(nameText, new Color(col.r, col.g, col.b, 0.45f), u * 0.4f);
            // 招式名过长时按字数缩小（以 6 字为满宽）
            int len = nm.Length;
            if (len > 6) nk = 6f / len;
            if (!string.IsNullOrEmpty(cryText))
            {
                cry = ClashUI.Label(textRt, "「" + cryText + "」", kai, 14, ClashUI.Css("#fff6e0"), side == 0 ? TextAnchor.UpperLeft : TextAnchor.UpperRight, FontStyle.Bold, "Cry");
                cry.horizontalOverflow = HorizontalWrapMode.Wrap;
                cry.lineSpacing = 1.08f;
                cry.gameObject.AddComponent<ClashSpacing>();
                ClashUI.AddOutline(cry, ClashUI.Rgba(10, 6, 4, 0.95f), 1);
                ClashUI.AddShadow(cry, new Color(0, 0, 0, 0.8f), 0, 2);
                cryG = cry.gameObject.AddComponent<CanvasGroup>();
            }
            flash = ClashUI.Img(rootRt, null, new Color(1, 1, 1, 0), "Flash"); ClashUI.Full(flash.rectTransform);
            Layout();
            ClashUtil.Sfx("magic", 0.6f); ClashUtil.Sfx("horn", 0.25f);
            Animate(0);
        }

        IEnumerator LoadPortrait()
        {
            var jobs = new List<KeyValuePair<PortraitGen, PortraitOpts>>
            { new KeyValuePair<PortraitGen, PortraitOpts>(Portrait.Describe(gen), PortraitOpts.Make(px, ClashUtil.Hex(col), "angry", false, side == 0)) };
            IEnumerator it = null;
            try { it = Portrait.PreloadAsync(jobs, 4f); } catch (Exception) { yield break; }
            while (true)
            {
                bool more;
                try { more = it.MoveNext(); } catch (Exception) { yield break; }
                if (!more) break;
                yield return it.Current;
            }
            Texture2D tex = null;
            try { tex = Portrait.Cached(gen, px, col, PortraitMood.Angry, false, side == 0); } catch (Exception) { /* 徽章 */ }
            if (tex != null && !Ended) ShowPortrait(tex);
        }
        void ShowPortrait(Texture2D tex)
        {
            portraitImg.sprite = Portrait.SpriteOf(tex);
            portraitImg.enabled = true;
            medal.gameObject.SetActive(false);
        }

        // 毛笔飞白（viewBox 1000×100，preserveAspectRatio none）
        static Texture2D SwashTexture(int seed, Color color)
        {
            var r = new SeededRandom(seed);
            var top = new List<Vector2>(); var bot = new List<Vector2>();
            const int N = 28;
            for (int i = 0; i <= N; i++)
            {
                float uu = (float)i / N, x = uu * 1000;
                // 收笔：最后 ~12% 平滑收细到笔尖
                float te = Mathf.Min(1, (1 - uu) / 0.12f), tap = te * te * (3 - 2 * te);
                float w = 40 * (0.55f + 0.45f * Mathf.Sin(Mathf.Min(1, uu * 1.08f) * Mathf.PI)) * (uu < 0.05f ? 0.5f + uu * 10 : 1) * tap;
                float jit = 7 * Mathf.Max(0.25f, tap);
                // 网页版输出路径时把坐标四舍五入（x 取整、y 保留 1 位）
                top.Add(new Vector2(Mathf.Round(x), Mathf.Round((50 - w + ((float)r.NextDouble() - 0.5f) * jit) * 10) / 10));
                float b = 50 + w * (0.9f + (float)r.NextDouble() * 0.2f) + ((float)r.NextDouble() - 0.5f) * jit;
                bot.Add(new Vector2(Mathf.Round(x), Mathf.Round(b * 10) / 10));
            }
            var ras = new Raster(1000, 100);
            var path = new Path2D();
            path.MoveTo(top[0].x, top[0].y);
            for (int i = 1; i < top.Count; i++) path.LineTo(top[i].x, top[i].y);
            for (int i = bot.Count - 1; i >= 0; i--) path.LineTo(bot[i].x, bot[i].y);
            path.ClosePath();
            ras.GlobalAlpha = 0.88f;
            ras.FillColor = "#0c0806";
            ras.Fill(path);
            ras.Save();
            ras.GlobalAlpha = 0.55f;
            ras.Translate(0, 4);
            ras.StrokeStyle = Paint.Solid(new RGBA(color.r, color.g, color.b, 1));
            ras.LineWidth = 3;
            ras.Stroke(path);
            ras.Restore();
            // 飞白：笔毛散出，末端各自长短不一、向笔尖略收拢
            ras.GlobalAlpha = 0.8f;
            ras.StrokeColor = "#0c0806";
            ras.LineCap = LineCap.Round;
            for (int i = 0; i < 7; i++)
            {
                float y = 16 + i * 11 + ((float)r.NextDouble() - 0.5f) * 6, x0 = 640 + (float)r.NextDouble() * 200, x1 = 960 + (float)r.NextDouble() * 130;
                float y1 = y + (50 - y) * 0.4f + ((float)r.NextDouble() - 0.5f) * 4;
                ras.LineWidth = Mathf.Round((2 + (float)r.NextDouble() * 4) * 10) / 10;
                var p = new Path2D();
                p.MoveTo(Mathf.Round(x0), Mathf.Round(y * 10) / 10); p.LineTo(Mathf.Round(x1), Mathf.Round(y1 * 10) / 10);
                ras.Stroke(p);
            }
            var tex = ras.ToTexture(false);
            tex.name = "clash swash";
            return tex;
        }

        void Layout()
        {
            vw = Screen.width / dpr; vh = Screen.height / dpr;
            // 色带：left/right −10%，top 30%，高 40%（竖屏 34% / 34%）
            float bTop = portrait ? 0.34f : 0.3f, bH = portrait ? 0.34f : 0.4f;
            ClashUI.Place(band.rectTransform, -0.1f * vw, bTop * vh, 1.2f * vw, bH * vh);
            // 头像
            float ph, pxl, pyl;
            if (portrait) { ph = Mathf.Min(0.6f * vw, 0.36f * vh); pyl = 0.1f * vh; pxl = side == 0 ? 0.06f * vw : vw - 0.06f * vw - ph; }
            else { ph = 0.58f * vh; pyl = 0.21f * vh; pxl = side == 0 ? 0.05f * vw : vw - 0.05f * vw - ph; }
            ClashUI.PlaceC(picRt, pxl + ph / 2, pyl + ph / 2, ph + 10, ph + 10);
            ClashUI.Round(goldRing, u * 0.9f + 5);
            var cr = colRing.rectTransform; ClashUI.Full(cr); cr.offsetMin = new Vector2(2, 2); cr.offsetMax = new Vector2(-2, -2); ClashUI.Round(colRing, u * 0.9f + 3);
            var pr = picBg.rectTransform; ClashUI.Full(pr); pr.offsetMin = new Vector2(5, 5); pr.offsetMax = new Vector2(-5, -5); ClashUI.Round(picBg, u * 0.9f);
            medalText.fontSize = Mathf.RoundToInt(portrait ? Mathf.Min(0.3f * vw, 0.18f * vh) : u * 11);
            // 文字块
            float whoFs = Mathf.Max(16, u * 2.0f);
            float nameFs = portrait ? Mathf.Min(u * 6.4f, 0.12f * vw) * nk : Mathf.Max(34 * nk, u * 6.4f * nk);
            float cryFs = Mathf.Max(13, u * 1.55f);
            float tl, tr;
            if (portrait) { tl = 0.05f * vw; tr = 0.05f * vw; }
            else if (side == 0) { tl = 0.05f * vw + 0.58f * vh + 0.03f * vw; tr = 0.03f * vw; }
            else { tl = 0.03f * vw; tr = 0.05f * vw + 0.58f * vh + 0.03f * vw; }
            float tw = Mathf.Max(40, vw - tl - tr);
            who.fontSize = Mathf.RoundToInt(whoFs); whoSp.Set(whoFs * 0.3f);
            float whoH = whoFs * 1.25f;
            nameText.fontSize = Mathf.RoundToInt(nameFs);
            nameSp.Set(nameFs * 0.08f);
            nameW = ClashUI.Width(nameText, nameFs * 0.08f);
            nameH = nameFs * 1.08f;
            float boxW = nameW + nameFs * 0.8f, boxH = nameH + nameFs * 0.24f;     // padding .08em .5em .16em .3em
            float cryH = 0;
            if (cry != null)
            {
                cry.fontSize = Mathf.RoundToInt(cryFs);
                cry.GetComponent<ClashSpacing>().Set(cryFs * 0.06f);
                var settings = cry.GetGenerationSettings(new Vector2(tw - cryFs * 0.4f, 0));
                cryH = cry.cachedTextGeneratorForLayout.GetPreferredHeight(cry.text, settings) / cry.pixelsPerUnit + cryFs * 0.35f;
            }
            float blockH = whoH + whoFs * 0.1f + boxH + cryH;
            float top = portrait ? 0.41f * vh : 0.5f * vh - blockH / 2;
            ClashUI.Place(textRt, tl, top, tw, blockH);
            ClashUI.Place(who.rectTransform, whoFs * 0.2f, 0, tw - whoFs * 0.2f, whoH);     // margin-left .2em
            // 招式名框
            float bx = side == 0 ? 0 : tw - boxW;
            ClashUI.Place(nameRt, bx, whoH + whoFs * 0.1f, boxW, boxH);
            nameRt.pivot = new Vector2(0.5f, 0.5f);
            nameRt.anchoredPosition = new Vector2(bx + boxW / 2, -(whoH + whoFs * 0.1f + boxH / 2));
            ClashUI.Place(nameText.rectTransform, nameFs * 0.3f, nameFs * 0.08f, nameW + 4, nameH);
            // 飞白：left −6%、top −14%、宽 112%、高 128%
            swashW = boxW * 1.12f;
            ClashUI.Place(swash.rectTransform, -0.06f * boxW, -0.14f * boxH, swashW, boxH * 1.28f);
            if (cry != null)
            {
                ClashUI.Place(cry.rectTransform, side == 0 ? cryFs * 0.4f : 0, whoH + whoFs * 0.1f + boxH + cryFs * 0.35f, tw - cryFs * 0.4f, cryH);
                cryBaseY = cry.rectTransform.anchoredPosition.y;
            }
            band.Relayout();
        }

        void Update()
        {
            if (Ended) return;
            float dt = Time.unscaledDeltaTime;
            t += dt;
            if (Screen.width / dpr != vw || Screen.height / dpr != vh) Layout();
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Escape)) Skip();
            if (skipAt >= 0)
            {
                // 跳过：全部 .12 秒淡出后结束
                float a = 1 - Mathf.Clamp01((t - skipAt) / 0.12f);
                var g = rootRt.GetComponent<CanvasGroup>();
                if (g == null) g = rootRt.gameObject.AddComponent<CanvasGroup>();
                g.alpha = Mathf.Min(g.alpha, a);
                if (t - skipAt >= 0.12f) End();
                return;
            }
            float tt = Clash.HoldCut ? Mathf.Min(t, dur * 0.5f) : t;
            Animate(Mathf.Clamp01(tt / dur));
            band.Tick(t);
            if (!Clash.HoldCut && t >= dur + 0.03f) End();
        }

        // 关键帧动画（网页版 CSS @keyframes，线性）
        void Animate(float p)
        {
            float d = ClashUI.Keys(p, new[] { 0f, 0f, 0.09f, 1f, 0.84f, 1f, 1f, 0f });
            dim.color = ClashUI.Rgba(6, 5, 10, 0.62f * d);
            // 色带
            float s0 = side == 0 ? -1 : 1;
            band.tx = ClashUI.Keys(p, new[] { 0f, 0.6f * s0, 0.1f, 0f, 0.84f, -0.02f * s0, 1f, -0.04f * s0 });
            band.sy = ClashUI.Keys(p, new[] { 0f, 0.05f, 0.1f, 1f, 0.84f, 1f, 1f, 0.02f });
            bandG.alpha = ClashUI.Keys(p, new[] { 0f, 0f, 0.1f, 1f, 0.84f, 1f, 1f, 0f });
            band.SetVerticesDirty();
            // 头像：translateX(∓140%) scale(1.15) → 0 → 5% → 9%
            float ptx = ClashUI.Keys(p, new[] { 0f, -1.4f, 0.26f, 0f, 0.84f, 0.05f, 1f, 0.09f }) * (side == 0 ? 1 : -1);
            float ps = ClashUI.Keys(p, new[] { 0f, 1.15f, 0.26f, 1f, 0.84f, 1.03f, 1f, 1.04f });
            picG.alpha = ClashUI.Keys(p, new[] { 0f, 0f, 0.06f, 1f, 0.84f, 1f, 1f, 0f });
            picRt.localScale = new Vector3(ps, ps, 1);
            var pw = picRt.sizeDelta.x;
            picRt.anchoredPosition = new Vector2(PicCx() + ptx * pw, picRt.anchoredPosition.y);
            // 文字块
            textG.alpha = ClashUI.Keys(p, new[] { 0f, 0f, 0.14f, 0f, 0.18f, 1f, 0.84f, 1f, 1f, 0f });
            // 招式名：scale 1.9 → .95（30%）→ 1.04（34%）→ 1（37%）→ 1（40%）→ 1.03（84%）→ 1.06；字距 .5em → .08em
            float ns = ClashUI.Keys(p, new[] { 0f, 1.9f, 0.15f, 1.9f, 0.3f, 0.95f, 0.34f, 1.04f, 0.37f, 1f, 0.4f, 1f, 0.84f, 1.03f, 1f, 1.06f });
            float ntx = ClashUI.Keys(p, new[] { 0f, 0f, 0.3f, 0f, 0.34f, -0.01f, 0.37f, 0.01f, 0.4f, 0f, 1f, 0f });
            float na = ClashUI.Keys(p, new[] { 0f, 0f, 0.15f, 0f, 0.3f, 1f, 1f, 1f });
            float nls = ClashUI.Keys(p, new[] { 0f, 0.5f, 0.15f, 0.5f, 0.3f, 0.08f, 1f, 0.08f });
            nameText.rectTransform.localScale = new Vector3(ns, ns, 1);
            nameText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var nr = nameText.rectTransform;
            nr.anchoredPosition = new Vector2(nameText.fontSize * 0.3f + (nameW + 4) / 2 + ntx * nameRt.sizeDelta.x, -(nameText.fontSize * 0.08f + nameH / 2));
            var nc = nameText.color; nc.a = na; nameText.color = nc;
            nameSp.Set(nls * nameText.fontSize);
            // 飞白自左向右展开（clip-path inset 右缘 110% → −15%，13% → 30%）
            float rv = Mathf.Lerp(-0.1f, 1.15f, Mathf.Clamp01((p - 0.13f) / 0.17f));
            float f = Mathf.Clamp01(rv);
            swash.uvRect = new Rect(0, 0, f, 1);
            var sr = swash.rectTransform; sr.sizeDelta = new Vector2(swashW * f, sr.sizeDelta.y);
            // 台词
            if (cry != null)
            {
                cryG.alpha = ClashUI.Keys(p, new[] { 0f, 0f, 0.26f, 0f, 0.36f, 1f, 1f, 1f });
                float ty = ClashUI.Keys(p, new[] { 0f, 0.6f, 0.26f, 0.6f, 0.36f, 0f, 1f, 0f }) * cry.fontSize;
                var crt = cry.rectTransform;
                crt.anchoredPosition = new Vector2(crt.anchoredPosition.x, cryBaseY - ty);
            }
            // 结尾闪白
            flash.color = new Color(1, 1, 1, ClashUI.Keys(p, new[] { 0f, 0f, 0.8f, 0f, 0.84f, 0.55f, 1f, 0f }));
        }
        float cryBaseY;
        float PicCx()
        {
            float ph = portrait ? Mathf.Min(0.6f * vw, 0.36f * vh) : 0.58f * vh;
            float pxl = portrait ? (side == 0 ? 0.06f * vw : vw - 0.06f * vw - ph) : (side == 0 ? 0.05f * vw : vw - 0.05f * vw - ph);
            return pxl + ph / 2;
        }

        public void Skip()
        {
            if (Ended || skipAt >= 0 || t < 0.12f) return;
            skipAt = t;
        }
        public void End()
        {
            if (Ended) return;
            Ended = true;
            if (Clash.cut == this) Clash.cut = null;
            if (medalTex != null) Destroy(medalTex);
            if (swashTex != null) Destroy(swashTex);
            medalTex = null; swashTex = null;
            if (band != null) band.Release();
            var cb = onEnd; onEnd = null;
            Destroy(gameObject);
            if (cb != null) { try { cb(); } catch (Exception e) { Debug.LogException(e); } }
        }
        void OnDestroy()
        {
            if (!Ended) End();
        }
    }

    // 特写色带：竖向五段渐变 + 外框 + 光晕 + 速度线；变换（平移、skewY ∓7°、scaleY）直接作用在顶点上
    internal class CutBand : MaskableGraphic
    {
        public float tx, sy = 1;            // translateX（色带宽度的比例）、scaleY
        Color c; bool light; int side; float u;
        float now;
        struct Line { public float y, x, len, sp, w, a; public bool white; }
        Line[] lines;
        public void Setup(Color col, bool isLight, int sd, int seed, float unit)
        {
            c = col; light = isLight; side = sd; u = unit;
            var rnd = new SeededRandom(seed);
            lines = new Line[70];
            for (int i = 0; i < 70; i++)
                lines[i] = new Line
                {
                    y = (float)rnd.NextDouble(), x = (float)rnd.NextDouble(), len = 0.08f + (float)rnd.NextDouble() * 0.3f, sp = 1.6f + (float)rnd.NextDouble() * 2.8f,
                    w = 0.6f + (float)rnd.NextDouble() * 2.6f, a = 0.25f + (float)rnd.NextDouble() * 0.6f, white = (float)rnd.NextDouble() < 0.55f
                };
        }
        public void Relayout() { SetVerticesDirty(); }
        public void Tick(float t) { now = t; SetVerticesDirty(); }
        public void Release() { lines = null; }

        // 色带局部坐标（x：0..W 自左向右，y：0..H 自上向下）→ 画布坐标（变换后）
        Vector3 Map(float x, float y, Rect r)
        {
            float W = r.width, H = r.height;
            float cx = x - W / 2, cy = y - H / 2;          // 以中心为原点（y 向下）
            cy *= sy;                                      // scaleY
            float k = Mathf.Tan((side == 0 ? -7 : 7) * Mathf.Deg2Rad);
            cy += k * cx;                                  // skewY
            cx += tx * W;                                  // translateX
            return new Vector3(r.center.x + cx, r.center.y - cy);
        }
        void Quad(VertexHelper vh, Rect r, float x0, float y0, float x1, float y1, Color c00, Color c10, Color c11, Color c01)
        {
            int b = vh.currentVertCount;
            vh.AddVert(Map(x0, y0, r), c00, Vector2.zero);
            vh.AddVert(Map(x1, y0, r), c10, Vector2.zero);
            vh.AddVert(Map(x1, y1, r), c11, Vector2.zero);
            vh.AddVert(Map(x0, y1, r), c01, Vector2.zero);
            vh.AddTriangle(b, b + 1, b + 2); vh.AddTriangle(b, b + 2, b + 3);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (lines == null) return;
            var r = rectTransform.rect;
            float W = r.width, H = r.height;
            // 光晕（box-shadow 0 0 u*4 色 70%）与外框（2px rgba(255,236,190,.55)）
            var g1 = new Color(c.r, c.g, c.b, 0.12f); var g2 = new Color(c.r, c.g, c.b, 0.22f);
            float e1 = u * 2.4f, e2 = u * 1.1f;
            Quad(vh, r, -e1, -e1, W + e1, H + e1, g1, g1, g1, g1);
            Quad(vh, r, -e2, -e2, W + e2, H + e2, g2, g2, g2, g2);
            var bc = new Color(1, 236 / 255f, 190 / 255f, 0.55f);
            Quad(vh, r, -2, -2, W + 2, H + 2, bc, bc, bc, bc);
            // 五段竖向渐变
            float[] st = { 0, 0.3f, 0.5f, 0.7f, 1 };
            Color[] cs = light
                ? new[] { ClashUI.Mix(c, Color.black, 0.55f), ClashUI.Mix(c, Color.black, 0.82f), ClashUI.Mix(c, Color.black, 0.92f), ClashUI.Mix(c, Color.black, 0.82f), ClashUI.Mix(c, Color.black, 0.55f) }
                : new[] { ClashUI.Mix(c, Color.black, 0.7f), c, ClashUI.Mix(c, Color.white, 0.75f), c, ClashUI.Mix(c, Color.black, 0.7f) };
            for (int i = 0; i < 4; i++) Quad(vh, r, 0, st[i] * H, W, st[i + 1] * H, cs[i], cs[i], cs[i + 1], cs[i + 1]);
            // 速度线（裁到色带内）
            float dir = side == 0 ? -1 : 1;
            var tint = new Color(Mathf.Min(1, c.r + 110 / 255f), Mathf.Min(1, c.g + 110 / 255f), Mathf.Min(1, c.b + 110 / 255f), 1);
            foreach (var L in lines)
            {
                float x = (((L.x + dir * now * L.sp) % 1.4f) + 1.4f) % 1.4f - 0.2f;
                float xa = x * W, xb = (x + L.len * -dir) * W, y = L.y * H;
                var lc = L.white ? Color.white : tint;
                float aa = L.a, ab = 0;
                // 裁剪
                float lo = Mathf.Min(xa, xb), hi = Mathf.Max(xa, xb);
                if (hi <= 0 || lo >= W) continue;
                float ca = Mathf.Clamp(xa, 0, W), cb = Mathf.Clamp(xb, 0, W);
                float span = xb - xa;
                float ta = Mathf.Abs(span) > 1e-3f ? (ca - xa) / span : 0, tb = Mathf.Abs(span) > 1e-3f ? (cb - xa) / span : 1;
                float alA = Mathf.Lerp(aa, ab, ta), alB = Mathf.Lerp(aa, ab, tb);
                float hw = L.w / 2;
                Quad(vh, r, ca, y - hw, cb, y + hw, new Color(lc.r, lc.g, lc.b, alA), new Color(lc.r, lc.g, lc.b, alB), new Color(lc.r, lc.g, lc.b, alB), new Color(lc.r, lc.g, lc.b, alA));
            }
        }
    }
}
