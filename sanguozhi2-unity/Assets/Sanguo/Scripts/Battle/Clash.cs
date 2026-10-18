using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
#if !CLASH_HEADLESS
using UnityEngine.EventSystems;
using UnityEngine.UI;
#endif

namespace Sanguo
{
    /* ==========================================================================
       三国志-霸王的世界 · 攻击画面（网页版 js/clash.js 的移植）
       原作中部队「攻击」时切换到横向交战画面：两军士兵对冲、兵力数字递减。
       这里用独立的三维场景现代化重制：自己的根 GameObject（放在远离战场的 Clash.Origin）
       + 自己的相机（depth 高于其它相机，只拍 Clash.Layer 层）+ 自己的太阳，结束时全部销毁并释放网格 / 材质 / 纹理。
       第三版（DESIGN-V3 §1、§2）：人物改用 View/Figure*.cs 的平滑蒙皮模型——步兵 / 弓手为按兵种 × 文化成组的实例化士兵群
       （FigureCrowd：前排刀盾、其后长枪、弓手，势力色为实例属性），旗手双手竖持旗枪（Figure.Soldier），武将为中精度名将
       （专属兵器、与头像一致的面部）骑中精度战马（马衣 / 具装）并插帅旗；动作片段由时间轴直接求出（行进、开弓放箭、冲锋 / 疾驰、
       混战劈砍、受击、倒地 / 溃逃、收队 / 欢呼）。地形为平滑网格 + 程序化细节着色（Sanguo/ClashGround：草地、踩踏泥土、农田、
       河岸沙、岩石、雪），圆润的树与石块、按防守城池文化的城墙与村舍（中式砖城 / 罗马石城 / 土坯城 / 红砖城 / 木栅寨；
       灰瓦 / 赤陶瓦 / 平顶土坯 / 毡帐 / 长屋 / 圆屋 / 干栏 / 茅屋），树种与地面色按群落，登陆战攻方身后为沙滩、海与搁浅的小船，
       远处为连绵山脊；镜头在接敌到混战时更低、更近，近景散落折枪、圆盾、倒旗与碎石；必杀斩光为正对镜头的月牙弧并沿弧撒火花。

       公开接口（UNITY-V2 §5，参数可增不可改）
         IEnumerator Clash.Play(attacker, defender, terrain, special, playerSide, speed, done, scene = null)
           · 攻方永远在左；兵力数字从 troopsBefore 滚动到 troopsAfter；troopsAfter 为 0 视为溃散。
           · special 不为 null 时冲锋前先播 CutIn 特写，接敌时附带特效与额外伤亡
             （kind：blaze 火海 / storm 雷击 / volley 箭雨，其余一律为斩击弧光 + 冲击波）。
           · playerSide：0 / 1，-1 = 无（电脑对电脑）。speed：本次额外的时间倍率（与 Clash.Speed 相乘）。
           · scene（第三版）：布景——防守城池的文化（城墙、村舍）、群落（temperate / steppe / desert / tropical / savanna / med /
             boreal / alpine：地面色、树种）、coast（登陆：攻方身后为沙滩与海）。为 null 时：两方都由 FromUnit 从同一战场取得则按战场
             （= 网页版 fromBattle 的 scene），否则按防守方文化推测。
           · done(skipped)：画面结束后回调（Mode 为 Off 时立即以 false 回调）。多次调用会排队依次播放。
         IEnumerator Clash.Play(ClashArgs args, float speed, Action<bool> done)   FromBattle 组装的参数
         ClashSide Clash.FromUnit(model, unit, troopsBefore, troopsAfter)       由战场部队组装一方的参数
         ClashArgs Clash.FromBattle(model, a, t, beforeA, beforeT)               = 网页版 SG.Clash.fromBattle（含 scene）
         ClashSceneInfo Clash.SceneOf(model, a, t)                                由战场（BattleTerrainMap）取布景
         ClashPrepJob Clash.Prepare(cultures, budget = 8)                        可选的预热（战斗开始时调用）：后台分片生成这些文化的
                                                                                  士兵群烘焙；多次调用依次排队，Play 播放前先等它做完
         IEnumerator Clash.CutIn(gen, moveName, color, side, cry, speed)         必杀技 / 单挑用的全屏特写
           （1.1 秒 ÷（Clash.Speed × speed），最短 0.7 秒；轻点 / 空格 / 回车 / Esc 可跳过）
         Clash.Mode（On / Fast / Off，读写，存 PlayerPrefs「sanguozhi2_clash_v3」，值同网页版 on / fast / off；缺省 On——第三版换了
           新键，旧键「sanguozhi2_clash」里存下的「关」不再生效，旧的「快」沿用）
         Clash.Enabled / Clash.Speed / Clash.CycleMode() / Clash.ModeLabel()
       坐标：场景逻辑全部沿用网页版的 three 坐标（x, y, z），建网格与摆放时换成 Unity 坐标 (x, y, -z)（P()）；
       人物 / 士兵群用 Figure.PosFromJs / YawFromJs；人物挂件（旗面、帅旗杆）用人物局部的换算（JS 局部 (x, y, z) → Unity (−x, y, z)）；
       逐帧绘制的箭 / 特效 / 粒子的矩阵 M_unity = S · M_three · S（S = diag(1, 1, -1)）。
       光照（Figure.shader 的约定）：Unity 灯光强度 = JS 强度 / π；环境光 Trilight = JS 半球光 × 强度 / π。
       本画面的地面 / 道具 / 草 / 旗 / 水着色器（Sanguo/Clash*）照网页版的 LowPoly 补丁自己加三色环境光（含地平线色）。
       装饰性随机一律使用 SeededRandom（按双方姓名与兵力取种子），不影响规则随机数。
       无头测试（Tests~/clash）以 CLASH_HEADLESS 编译：界面（HUD、特写）换成同名的空实现，其余代码与游戏内相同。
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
        // FromUnit 记下的战场与部队（Play 没给 scene 时由此取布景，= 网页版 fromBattle 的 scene）
        internal BattleModel model;
        internal BUnit unit;
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

    // 布景（= 网页版 play 的 scene：{ culture, biome, coast }）
    public class ClashSceneInfo
    {
        public string culture;                 // 防守城池的文化（城墙、村舍）
        public string biome;                   // 群落名（BattleTerrain.BIOMES 之一）；null = 按文化推测
        public bool coast;                     // 登陆：攻方身后为沙滩与海
    }

    // FromBattle 组装的 Play 参数（= 网页版 fromBattle 的返回值）
    public class ClashArgs
    {
        public ClashSide attacker, defender;
        public Terrain terrain;
        public ClashSpecial special;
        public int playerSide = -1;
        public ClashSceneInfo scene;
    }

    // 预热任务（Clash.Prepare 的返回值）：Done = 做完（含失败）
    public sealed class ClashPrepJob
    {
        public bool Done, Ok;
        internal string[] cultures;
        internal double budget;
    }

    public static class Clash
    {
        public const string ModeKey = "sanguozhi2_clash_v3";                // 第三版的设定键（缺省「开」）
        public const string OldModeKey = "sanguozhi2_clash";                // 第二版的旧键：只沿用「快」
        public const int Layer = 30;                                    // 攻击画面与特写所用的渲染层
        public static readonly Vector3 Origin = new Vector3(0, -2000, 0);   // 场景根的世界位置（远离战场与地图）
        // 调试：FreezeAt ≥ 0 时动画停在该秒；HoldCut 为 true 时特写不自动结束（截图用）
        public static float FreezeAt = -1;
        public static bool HoldCut;

        static bool loaded, enabled = true;
        static float speed = 1f;
        internal static ClashScene active;
        internal static CutInView cut;

        // 第三版：玩家要求「战斗时默认开启动画」——换用新键，旧键里存下的「关」作废一次（DESIGN-V3 §2）
        static string LoadMode()
        {
            try
            {
                string v = PlayerPrefs.GetString(ModeKey, "");
                if (v == "on" || v == "fast" || v == "off") return v;
                // 旧键只沿用「快」（仍是开启动画）；旧的「关」不再生效
                if (PlayerPrefs.GetString(OldModeKey, "") == "fast") return "fast";
            }
            catch (Exception) { /* 忽略 */ }
            return "on";
        }
        static void Load()
        {
            if (loaded) return;
            loaded = true;
            string v = LoadMode();
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
        public static IEnumerator Play(ClashSide attacker, ClashSide defender, Terrain terrain, ClashSpecial special, int playerSide, float speed, Action<bool> done, ClashSceneInfo scene = null)
        {
            if (!Enabled) { if (done != null) done(false); yield break; }
            // 排队：前一场播完再开始
            while (active != null) yield return null;
            if (!Enabled) { if (done != null) done(false); yield break; }
            ClashScene sc = null;
            try
            {
                // 布景：未给出时，两方都从同一战场组装（FromUnit）就按战场取（= 网页版 fromBattle 的 scene）
                if (scene == null && attacker != null && defender != null && attacker.model != null && attacker.model == defender.model && attacker.unit != null && defender.unit != null)
                    scene = SceneOf(attacker.model, attacker.unit, defender.unit);
                sc = new ClashScene(attacker, defender, terrain, special, playerSide, speed, scene);
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
        public static IEnumerator Play(ClashArgs o, float speed, Action<bool> done)
        {
            if (o == null) { if (done != null) done(false); return Empty(); }
            return Play(o.attacker, o.defender, o.terrain, o.special, o.playerSide, speed, done, o.scene);
        }
        static IEnumerator Empty() { yield break; }

        // 由战场部队组装一方的参数（JS fromBattle 的一半）
        public static ClashSide FromUnit(BattleModel m, BUnit u, int troopsBefore, int troopsAfter)
        {
            var o = new ClashSide { troopsBefore = troopsBefore, troopsAfter = troopsAfter, model = m, unit = u };
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
        // 由战场数据组装 Play 参数（= 网页版 SG.Clash.fromBattle：a 攻击 t，before* 为 Attack 之前的兵力）
        public static ClashArgs FromBattle(BattleModel m, BUnit a, BUnit t, int beforeA, int beforeT)
        {
            return new ClashArgs
            {
                attacker = FromUnit(m, a, beforeA, a != null ? a.Troops : 0),
                defender = FromUnit(m, t, beforeT, t != null ? t.Troops : 0),
                terrain = m != null && m.map != null && t != null ? m.map[t.x, t.y] : Terrain.Plain,
                special = null,
                playerSide = -1,
                scene = SceneOf(m, a, t),
            };
        }

        // 由战场（Battle/BattleTerrain.cs 的 BattleTerrainMap，= 网页版 model.terrain）取布景：防守城池的文化、交战处的群落、是否在海岸登陆
        public static ClashSceneInfo SceneOf(BattleModel model, BUnit a, BUnit t)
        {
            var T = model != null ? model.terrain as BattleTerrainMap : null;
            var o = new ClashSceneInfo
            {
                culture = T != null && !string.IsNullOrEmpty(T.culture) ? T.culture : (t != null && t.gen != null && !string.IsNullOrEmpty(t.gen.culture) ? t.gen.culture : "han"),
                biome = T != null ? T.biome : null,
                coast = false,
            };
            try
            {
                var G = T != null ? T.grid : null;
                if (G != null && G.biome != null && G.water != null && a != null && t != null)
                {
                    Func<int, int, int> idx = (x, y) =>
                    {
                        int i = (int)Math.Floor((x - G.u0) / G.step + 0.5), j = (int)Math.Floor((y - G.v0) / G.step + 0.5);
                        return i >= 0 && j >= 0 && i < G.nu && j < G.nv ? j * G.nu + i : -1;
                    };
                    int k0 = idx(t.x, t.y);
                    if (k0 >= 0 && G.biome[k0] < BattleTerrain.BIOMES.Length) o.biome = BattleTerrain.BIOMES[G.biome[k0]];
                    // 攻方身后 3 格内有海（或者是登陆战且攻方站在浅滩 / 水边）→ 海岸布景
                    int n = 0;
                    for (int dx = -3; dx <= 3; dx++)
                        for (int dy = -3; dy <= 3; dy++)
                        {
                            if (dx * dx + dy * dy > 9) continue;
                            int k = idx(a.x + dx, a.y + dy);
                            if (k >= 0 && G.water[k] == 3) n++;
                        }
                    bool wet = model.map != null && model.InBounds(a.x, a.y) && (model.map[a.x, a.y] == Terrain.Water || model.map[a.x, a.y] == Terrain.River);
                    o.coast = n >= 3 || (T.HasNote("landing") && wet);
                }
                // 干旱文化的城池落在世界地图的「温带」（河谷绿洲）时，攻击画面取半干旱的稀树草原（枯草、金合欢、椰枣），不是中原的青草地
                string dflt = ClashLook.CultPick(ClashLook.BIOME_OF, o.culture, "temperate");
                if (o.biome == "temperate" && (dflt == "desert" || dflt == "savanna")) o.biome = "savanna";
            }
            catch (Exception) { /* 旧战场：只按文化 */ }
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

        // ---------------------------------------------------------- 预热 --
        // 兵种：前排刀盾、其后长枪（罗马全为短剑 + 大盾），弓手单列；士兵群烘焙的 seed（= JS TYPE_SEED）
        internal static int TypeSeed(string type) { return type == "sword" ? 1 : 0; }
        internal static FigCrowdSpec[] CrowdSpecs(IEnumerable<string> cultures)
        {
            var o = new List<FigCrowdSpec>();
            foreach (var c in cultures) foreach (var ty in new[] { "spear", "sword", "archer" }) o.Add(new FigCrowdSpec { type = ty, culture = c, seed = TypeSeed(ty) });
            return o.ToArray();
        }
        static readonly List<ClashPrepJob> prepQueue = new List<ClashPrepJob>();
        static bool prepRunning;
        // 有排队中的预热（播放前先等它做完，免得同一模板并行生成两遍；= JS prepChain）
        internal static bool PrepBusy { get { return prepRunning || prepQueue.Count > 0; } }
        // 可选的预热（战斗开始时传入双方文化）：后台分片生成士兵群烘焙与图集；依次排队，多次调用不会并行生成同一模板
        public static ClashPrepJob Prepare(IEnumerable<string> cultures, double budget = 8)
        {
            var cs = new List<string>();
            if (cultures != null) foreach (var c in cultures) if (!string.IsNullOrEmpty(c) && !cs.Contains(c)) cs.Add(c);
            if (cs.Count == 0) cs.Add("han");
            var job = new ClashPrepJob { cultures = cs.ToArray(), budget = budget };
            prepQueue.Add(job);
            var host = ClashHost.Get();
            if (host == null) { prepQueue.Remove(job); job.Done = true; return job; }
            if (!prepRunning) host.StartCoroutine(RunPrep());
            return job;
        }
        static IEnumerator RunPrep()
        {
            if (prepRunning) yield break;
            prepRunning = true;
            while (prepQueue.Count > 0)
            {
                var job = prepQueue[0];
                IEnumerator it = null;
                try { it = Figure.Preload(new FigPreload { cultures = job.cultures, types = new string[0], crowd = CrowdSpecs(job.cultures), budget = job.budget }); }
                catch (Exception e) { Debug.LogException(e); }
                bool ok = it != null;
                while (it != null)
                {
                    bool more;
                    try { more = it.MoveNext(); } catch (Exception e) { Debug.LogException(e); ok = false; break; }
                    if (!more) break;
                    yield return null;
                }
                prepQueue.RemoveAt(0);
                job.Ok = ok; job.Done = true;
            }
            prepRunning = false;
        }
        // 游戏内兜底预热：游戏启动完成（战略画面就绪）后在后台分片生成最常见的汉军士兵群，首次「攻击」不必在黑幕下等待；
        // 动画设为「关」时不做（= 网页版 clash.js 末尾的 warm）
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void WarmUp()
        {
            var host = ClashHost.Get();
            if (host != null) host.StartCoroutine(WarmCo());
        }
        static bool warmed;
        // 游戏启动完成：战略画面与地图就绪、窗口在前台（无头测试没有 Game）
        static bool GameReady()
        {
#if !CLASH_HEADLESS
            return Game.I != null && Game.I.Strategy != null && Game.I.Map != null && Application.isFocused;
#else
            return false;
#endif
        }
        static IEnumerator WarmCo()
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 2.5f) yield return null;
            for (int tries = 0; !warmed && tries < 40; tries++)
            {
                if (GameReady())
                {
                    warmed = true;
                    if (Enabled) Prepare(new[] { "han" }, 6);
                    yield break;
                }
                float t1 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t1 < 1.5f) yield return null;
            }
        }
    }

    // 常驻的协程宿主（预热用；跨场景保留）
    internal class ClashHost : MonoBehaviour
    {
        static ClashHost inst;
        public static ClashHost Get()
        {
            if (inst != null) return inst;
            try
            {
                var go = new GameObject("SG.ClashHost");
                UnityEngine.Object.DontDestroyOnLoad(go);
                inst = go.AddComponent<ClashHost>();
            }
            catch (Exception) { inst = null; }
            return inst;
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

#if !CLASH_HEADLESS
    // 轻点 / 点击：转发给回调（pointerdown 即触发，同网页版）
    internal class ClashTap : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public Action onDown;
        public void OnPointerDown(PointerEventData e) { if (onDown != null) onDown(); }
        public void OnPointerClick(PointerEventData e) { }
    }
#endif

    // ======================================================= 小工具 --
    internal static class ClashUtil
    {
        public static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        public static Vector3 V(double x, double y, double z) { return new Vector3((float)x, (float)y, (float)z); }
        // three 世界坐标 → Unity 坐标（z 取反）
        public static Vector3 P(float x, float y, float z) { return new Vector3(x, y, -z); }
        public static Vector3 P(double x, double y, double z) { return new Vector3((float)x, (float)y, (float)-z); }
        public static Color C(float r, float g, float b) { return new Color(r, g, b); }
        public static Color Shade(Color c, float k) { return Art.Shade(c, k); }
        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        public static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        public static float Seg(float t, float a, float b) { return b <= a ? (t >= a ? 1 : 0) : Clamp01((t - a) / (b - a)); }
        public static float Sm(float t) { return t * t * (3 - 2 * t); }
        public static double Sm(double t) { return t * t * (3 - 2 * t); }
        public static float Eo(float t) { float u = 1 - t; return 1 - u * u * u; }
        public static float Eio(float t) { return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2; }
        public static float Sstep(float e0, float e1, float x) { return Sm(Clamp01((x - e0) / (e1 - e0))); }
        public static double Sstep(double e0, double e1, double x) { return Sm(Clamp01((x - e0) / (e1 - e0))); }
        // [a, b] 内为 1、两端各有 r 秒渐变的梯形窗
        public static float Trap(float t, float a, float b, float r) { return Mathf.Max(0, Mathf.Min(Seg(t, a - r * 0.5f, a + r * 0.5f), 1 - Seg(t, b - r * 0.5f, b + r * 0.5f))); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
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
        // JS Math.abs(hashStr(s)) % n（hashStr 可能是 −2^31，不能用 Math.Abs(int)）
        public static int AbsMod(int h, int n) { return (int)(Math.Abs((long)h) % n); }
        public static float Luma(Color c) { return 0.3f * c.r + 0.59f * c.g + 0.11f * c.b; }
        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant(); }
        // three Color.getHexString（Math.round，夹紧 0–255）
        public static string HexJs(Color c)
        {
            Func<float, int> b = v => Math.Max(0, Math.Min(255, (int)Math.Floor(Math.Max(0, Math.Min(255, v * 255.0)) + 0.5)));
            return "#" + b(c.r).ToString("x2") + b(c.g).ToString("x2") + b(c.b).ToString("x2");
        }
        // JS mixHex(a, b, k)：两色线性混合后的 '#rrggbb'
        // 按 double 计算（同网页版 three Color.lerp + getHexString 的 Math.round）：float 会在 .5 处取错（如 #2f9a55 混白 0.55 的 b = 178.5）
        public static string MixHex(Color a, Color b, double k)
        {
            Color32 x = a, y = b;
            Func<byte, byte, string> ch = (p, q) => { double v = p / 255.0 + (q / 255.0 - p / 255.0) * k; return Math.Max(0, Math.Min(255, (int)Math.Floor(Math.Max(0, Math.Min(255, v * 255)) + 0.5))).ToString("x2"); };
            return "#" + ch(x.r, y.r) + ch(x.g, y.g) + ch(x.b, y.b);
        }
        public static Color Lerp(Color a, Color b, float t) { return Color.Lerp(a, b, t); }
        // three Color.lerp（不夹紧 t）
        public static Color LerpU(Color a, Color b, float t) { return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, 1); }
#if !CLASH_HEADLESS
        public static void Sfx(string name, float vol = 0.8f) { try { Sanguo.Sfx.Play(name, vol); } catch (Exception) { /* 无音频 */ } }
        // 交锋乐句（= clash.js 的 SG.Sfx.stinger('clash', { speed })）。直接调用：签名变了编译就报错，不会静默失效
        public static void Stinger(string kind, float speed) { try { Sanguo.Sfx.Stinger(kind, speed); } catch (Exception) { /* 无音频 */ } }
#else
        public static readonly List<string> SfxLog = new List<string>();
        public static void Sfx(string name, float vol = 0.8f) { SfxLog.Add(name); }
        public static void Stinger(string kind, float speed) { SfxLog.Add("stinger:" + kind); }
#endif
        // three 空间的矩阵 → Unity 空间（S · M · S）
        public static Matrix4x4 ToU(Matrix4x4 m)
        {
            m.m02 = -m.m02; m.m12 = -m.m12; m.m20 = -m.m20; m.m21 = -m.m21; m.m23 = -m.m23; m.m32 = -m.m32;
            return m;
        }
        public static Matrix4x4 TRS(float x, float y, float z, Quaternion q, float s = 1f) { return Matrix4x4.TRS(new Vector3(x, y, z), q, new Vector3(s, s, s)); }
        public static Quaternion AxisX(float rad) { return Quaternion.AngleAxis(rad * Mathf.Rad2Deg, Vector3.right); }
        // three 的 Euler(pitch, yaw, spin, 'YXZ')（矩阵 = Ry · Rx · Rz，数值与 three 相同）
        public static Quaternion YXZ(float pitch, float yaw, float spin)
        {
            return Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(spin * Mathf.Rad2Deg, Vector3.forward);
        }
        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            var t = go.transform;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i).gameObject, layer);
        }
#if !CLASH_HEADLESS
        public static Font Body { get { return UIKit.Body != null ? UIKit.Body : Fallback; } }
        public static Font Kai { get { return UIKit.Title != null ? UIKit.Title : Body; } }
        static Font fallback;
        static Font Fallback { get { if (fallback == null) fallback = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 32); return fallback; } }
#endif
        public static bool IsTouch { get { return Application.isMobilePlatform || (Input.touchSupported && !Input.mousePresent); } }
        // JS 坐标的 three 点 → 本画面根下的 Unity 世界坐标
        public static Vector3 World(Vector3 three) { return Clash.Origin + new Vector3(three.x, three.y, -three.z); }
        // Unity 世界坐标 → 本画面的 three 坐标
        public static Vector3 Three(Vector3 world) { var d = world - Clash.Origin; return new Vector3(d.x, d.y, -d.z); }
    }

    // ======================================================= 网格辅助 --
    internal static class ClashGeo
    {
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Color Shade(Color c, float k) { return Art.Shade(c, k); }
        public static readonly Color WOOD = C(0.43f, 0.3f, 0.17f), GOLD = C(0.88f, 0.7f, 0.28f);

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
        // 任意方向的方柱（Unity 坐标）：a → b，截面宽 w0 → w1（= JS beam）
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

        // 箭（平滑箭杆、箭镞、三片箭羽），沿 three 的 −z 飞行（= JS arrowGeo）。
        // 网页版建模沿 y 轴（MeshBuilder 的 Unity 坐标），three 里 scale(0.8, 0.6, 0.8)、rotateX(−π/2)；
        // 这里输出给 DrawU（M_unity = S·M_three·S）用的 Unity 局部坐标 = 上述 three 坐标的 z 取反：
        //   位置 (x, y, z_u) → (0.8x, −0.8z_u, 0.6y)；法线按逆转置（three 的 normalMatrix）后归一化
        public static Mesh Arrow()
        {
            var mb = new MeshBuilder();
            mb.Lathe(V(0, 0, 0), new[] { new LP(0.011f, -0.3f), new LP(0.011f, 0.28f) }, 5, C(0.52f, 0.4f, 0.24f));
            mb.Lathe(V(0, 0, 0), new[] { new LP(0.024f, 0.27f), new LP(0.004f, 0.42f) }, 5, C(0.78f, 0.79f, 0.83f));
            var fl = C(0.95f, 0.94f, 0.9f);
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                foreach (int sd in new[] { 1, -1 })
                {
                    var nrm = V(-sa * sd, 0, ca * sd);
                    mb.TriN(V(0, -0.3f, 0), V(ca * 0.045f, -0.26f, sa * 0.045f), V(ca * 0.04f, -0.15f, sa * 0.04f), nrm, nrm, nrm, fl);
                    mb.TriN(V(0, -0.3f, 0), V(ca * 0.04f, -0.15f, sa * 0.04f), V(0, -0.17f, 0), nrm, nrm, nrm, fl);
                }
            }
            for (int i = 0; i < mb.v.Count; i++)
            {
                var p = mb.v[i]; mb.v[i] = V(0.8f * p.x, -0.8f * p.z, 0.6f * p.y);
                var n = mb.n[i];
                // three：n3 = normalize(nx / 0.8, ny / 0.6, −nz / 0.8)，旋转后 (n3.x, n3.z, −n3.y)，再 z 取反
                var n3 = Norm(V(n.x / 0.8f, n.y / 0.6f, -n.z / 0.8f));
                mb.n[i] = V(n3.x, n3.z, n3.y);
            }
            var m = mb.ToMesh("clash arrow");
            m.SetUVs(1, new List<Vector2>(new Vector2[mb.v.Count]));      // aKind = 0（原色）
            return m;
        }

        // 斩光贴图：u = 径向（0 内缘 → 1 外缘），v = 沿弧（0 → 1）。外缘内侧一道亮芯，内缘柔和渐隐，两端渐隐（= JS slashTexture）
        static Texture2D slashTex;
        public static Texture2D SlashTexture()
        {
            if (slashTex != null) return slashTex;
            const int W = 64, Hh = 64;
            var px = new Color32[W * Hh];
            Func<double, double, double, double> ss = (a, b, x) => { double t = Math.Max(0, Math.Min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
            for (int y = 0; y < Hh; y++)
                for (int x = 0; x < W; x++)
                {
                    double u = (x + 0.5) / W, v = (y + 0.5) / Hh;
                    double body = ss(0.0, 0.7, u) * (1 - ss(0.9, 1.0, u));
                    double core = Math.Exp(-Math.Pow((u - 0.8) / 0.07, 2));
                    double ends = Math.Pow(Math.Sin(Math.PI * v), 0.6);
                    double al = Math.Max(0, Math.Min(1, (body * 0.55 + core) * ends));
                    double w = Math.Max(0, Math.Min(1, 0.55 + core * 0.6));
                    byte c = (byte)Math.Floor(255 * w * al + 0.5), aa = (byte)Math.Floor(255 * al + 0.5);
                    // canvas 第 y 行在上；three 的 CanvasTexture 上下翻转——图案沿 v 对称，行序无关
                    px[y * W + x] = new Color32(c, c, c, aa);
                }
            slashTex = new Texture2D(W, Hh, TextureFormat.RGBA32, true) { name = "clash slash", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            slashTex.SetPixels32(px);
            slashTex.Apply(true);
            return slashTex;
        }
        // 月牙几何（three 局部 xy 平面；z = 0 的点 z 取反不变）：外缘半径 r1，内缘自中部 r0 向两端收拢到 r1；wid：弧长占比（全长 ±1.05 弧度）
        public static Mesh SlashGeo(float r0, float r1, float wid)
        {
            const int N = 24;
            float A = 1.05f * (wid > 0 ? wid : 1);
            var pos = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>(); var nrm = new List<Vector3>(); var col = new List<Color>();
            for (int i = 0; i <= N; i++)
            {
                float v = (float)i / N, a = -A + 2 * A * v, ri = r1 - (r1 - r0) * Mathf.Pow(Mathf.Sin(Mathf.PI * v), 0.7f);
                pos.Add(V(Mathf.Cos(a) * ri, Mathf.Sin(a) * ri, 0)); pos.Add(V(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, 0));
                uv.Add(new Vector2(0, v)); uv.Add(new Vector2(1, v));
                nrm.Add(V(0, 0, 1)); nrm.Add(V(0, 0, 1)); col.Add(Color.white); col.Add(Color.white);
                if (i < N) { int k = i * 2; idx.Add(k); idx.Add(k + 1); idx.Add(k + 3); idx.Add(k); idx.Add(k + 3); idx.Add(k + 2); }
            }
            var m = new Mesh { name = "clash slash" };
            m.SetVertices(pos); m.SetNormals(nrm); m.SetColors(col); m.SetUVs(0, uv); m.SetTriangles(idx, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    // ======================================================= 布景 --
    // 第三版修订：攻击画面按「防守城池的文化 + 当地群落 + 是否登陆」布景（不再一律是中原的砖城、灰瓦村舍与温带树）
    // 城墙：han 中式砖城门楼 / roman 石城方塔赤陶瓦 / mud 土坯城圆塔阶梯垛（安息、阿拉伯、非洲、毛里、西域）/
    //       brick 红砖城与圆顶亭、窣堵波（天竺、贵霜）/ palisade 土垒木栅与木望楼（日耳曼、凯尔特、草原、南方诸族）
    // 村舍：han 灰瓦 / tile 白墙赤陶瓦 / flat 平顶土坯 / yurt 毡帐 / long 长屋茅顶 / round 圆屋锥顶 / stilt 干栏 / thatch 茅屋
    internal sealed class ClashLook
    {
        public string culture, biome, wall, house;
        public bool coast;
        public Color? grass, tuft;
        public Color leaf, fleaf;
        public string[] treeKinds; public float[] treeW;
        public float treeK, farm, snow, sandy, flowers;

        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        public static readonly Dictionary<string, string> WALL_STYLE = new Dictionary<string, string>
        {
            { "han", "han" }, { "korea", "han" }, { "wa", "han" }, { "roman", "roman" }, { "persia", "mud" }, { "arab", "mud" }, { "africa", "mud" }, { "moor", "mud" }, { "tarim", "mud" },
            { "kushan", "brick" }, { "india", "brick" },
            { "seasia", "palisade" }, { "nanman", "palisade" }, { "yi", "palisade" }, { "celt", "palisade" }, { "german", "palisade" }, { "steppe", "palisade" }, { "sarmatian", "palisade" },
        };
        public static readonly Dictionary<string, string> HOUSE_STYLE = new Dictionary<string, string>
        {
            { "han", "han" }, { "korea", "han" }, { "wa", "han" }, { "roman", "tile" }, { "persia", "flat" }, { "arab", "flat" }, { "africa", "flat" }, { "moor", "flat" }, { "tarim", "flat" },
            { "kushan", "flat" }, { "india", "thatch" },
            { "seasia", "stilt" }, { "nanman", "stilt" }, { "yi", "stilt" }, { "celt", "round" }, { "german", "long" }, { "steppe", "yurt" }, { "sarmatian", "yurt" },
        };
        public static readonly Dictionary<string, string> BIOME_OF = new Dictionary<string, string>
        {
            { "roman", "med" }, { "moor", "med" }, { "persia", "desert" }, { "arab", "desert" }, { "tarim", "desert" }, { "africa", "savanna" }, { "kushan", "steppe" }, { "steppe", "steppe" },
            { "sarmatian", "steppe" }, { "india", "tropical" }, { "seasia", "tropical" }, { "nanman", "tropical" }, { "yi", "tropical" },
        };
        // = SG.BattleTerrain.BIOMES
        public static readonly string[] BIOME_NAMES = { "temperate", "steppe", "desert", "tropical", "savanna", "med", "boreal", "alpine" };
        // grass：地面底色（null = 原温带色）；trees：[树种, 权重]；treeK：树的多寡；farm：农田；snow：雪线系数（0 无雪）；sandy：沙地斑块
        static ClashLook L(Color? grass, Color leaf, Color fleaf, Color? tuft, string[] tk, float[] tw, float treeK, float farm, float snow, float sandy, float flowers)
        {
            return new ClashLook { grass = grass, leaf = leaf, fleaf = fleaf, tuft = tuft, treeKinds = tk, treeW = tw, treeK = treeK, farm = farm, snow = snow, sandy = sandy, flowers = flowers };
        }
        public static readonly Dictionary<string, ClashLook> BIOME_LOOK = new Dictionary<string, ClashLook>
        {
            { "temperate", L(null, C(0.28f, 0.5f, 0.26f), C(0.25f, 0.44f, 0.23f), null, new[] { "pine", "broad", "broad2", "poplar" }, new float[] { 34, 28, 22, 16 }, 1, 1, 1, 0, 1) },
            { "boreal", L(C(0.4f, 0.54f, 0.32f), C(0.22f, 0.42f, 0.24f), C(0.2f, 0.38f, 0.22f), C(0.38f, 0.54f, 0.28f), new[] { "pine", "poplar", "broad" }, new float[] { 70, 15, 15 }, 1.1f, 0.5f, 1, 0, 0.6f) },
            { "alpine", L(C(0.46f, 0.56f, 0.34f), C(0.22f, 0.42f, 0.24f), C(0.2f, 0.38f, 0.22f), C(0.44f, 0.56f, 0.3f), new[] { "pine", "broad" }, new float[] { 80, 20 }, 0.8f, 0.3f, 1.4f, 0, 0.8f) },
            { "steppe", L(C(0.64f, 0.65f, 0.4f), C(0.34f, 0.5f, 0.28f), C(0.3f, 0.46f, 0.26f), C(0.6f, 0.6f, 0.34f), new[] { "poplar", "broad", "scrub" }, new float[] { 50, 35, 15 }, 0.35f, 0.2f, 1, 0.15f, 0.5f) },
            { "desert", L(C(0.78f, 0.68f, 0.48f), C(0.34f, 0.5f, 0.24f), C(0.3f, 0.48f, 0.22f), C(0.66f, 0.6f, 0.36f), new[] { "palm", "olive", "scrub" }, new float[] { 65, 15, 20 }, 0.45f, 0.35f, 0, 0.5f, 0.1f) },
            { "savanna", L(C(0.7f, 0.64f, 0.38f), C(0.36f, 0.5f, 0.24f), C(0.32f, 0.46f, 0.22f), C(0.7f, 0.64f, 0.36f), new[] { "acacia", "palm", "scrub" }, new float[] { 60, 20, 20 }, 0.5f, 0.3f, 0, 0.25f, 0.3f) },
            { "med", L(C(0.56f, 0.6f, 0.34f), C(0.32f, 0.46f, 0.25f), C(0.28f, 0.42f, 0.22f), C(0.56f, 0.58f, 0.32f), new[] { "cypress", "olive", "broad2", "umbrella" }, new float[] { 30, 40, 15, 15 }, 0.8f, 1, 0.6f, 0.08f, 0.8f) },
            { "tropical", L(C(0.34f, 0.58f, 0.26f), C(0.2f, 0.47f, 0.2f), C(0.16f, 0.4f, 0.18f), C(0.3f, 0.56f, 0.22f), new[] { "palm", "broad", "broad2" }, new float[] { 40, 35, 25 }, 1.1f, 0.8f, 0, 0, 1.4f) },
        };
        // 文化查表：本文化 → Defs.CultureBase 的回退文化 → 缺省（= JS cultPick）
        public static string CultPick(Dictionary<string, string> table, string c, string dflt)
        {
            string v;
            if (c != null && table.TryGetValue(c, out v) && !string.IsNullOrEmpty(v)) return v;
            string b = Defs.CultureBase(c);
            if (b != null && table.TryGetValue(b, out v) && !string.IsNullOrEmpty(v)) return v;
            return dflt;
        }
        // scn = { culture, biome, coast }；缺省时取防守方的文化、按文化推测群落（= JS sceneLook）
        public static ClashLook Of(ClashSceneInfo scn, string defCult)
        {
            string culture = scn != null && !string.IsNullOrEmpty(scn.culture) ? scn.culture : !string.IsNullOrEmpty(defCult) ? defCult : "han";
            string biome = scn != null ? scn.biome : null;
            if (biome == null || !BIOME_LOOK.ContainsKey(biome)) biome = CultPick(BIOME_OF, culture, "temperate");
            var b = BIOME_LOOK[biome];
            return new ClashLook
            {
                culture = culture, biome = biome, wall = CultPick(WALL_STYLE, culture, "han"), house = CultPick(HOUSE_STYLE, culture, "han"), coast = scn != null && scn.coast,
                grass = b.grass, leaf = b.leaf, fleaf = b.fleaf, tuft = b.tuft, treeKinds = b.treeKinds, treeW = b.treeW,
                treeK = b.treeK, farm = b.farm, snow = b.snow, sandy = b.sandy, flowers = b.flowers,
            };
        }
        public string FirstTree { get { return treeKinds[0]; } }
        public string PickTree(SeededRandom rnd)
        {
            double tot = 0;
            foreach (var w in treeW) tot += w;
            double u = rnd.NextDouble() * tot;
            for (int i = 0; i < treeKinds.Length; i++) { u -= treeW[i]; if (u <= 0) return treeKinds[i]; }
            return treeKinds[0];
        }
    }

    // ======================================================= 地形 --
    // 第三版：平滑地形（索引网格 + 平滑法线 + Sanguo/ClashGround 的程序化细节），圆润的树、平滑石块，按文化的城墙、拱形城门与曲面屋顶；
    // 远处为连绵的山脊（同一张网格向后延伸）；草丛 / 芦苇 / 野花为实例化网格（随风轻摆）。
    // 坐标：计算全部用 three 坐标（双精度，与网页版逐点相同的判定），建网格时经 P() 换成 Unity 坐标。
    internal sealed class ClashTerrainOut
    {
        public Mesh ground, propsNear, propsFar, river, sea, tuftMesh, flowerMesh;
        public readonly List<Matrix4x4> tufts = new List<Matrix4x4>(), flowers = new List<Matrix4x4>();   // 实例矩阵（本画面根的局部 Unity 坐标）
        public readonly List<Vector4> tuftCols = new List<Vector4>(), flowerCols = new List<Vector4>();
        public int groundTris;
        public float snowH = 99;
    }

    internal static class ClashTerrain
    {
        public const float ZC = -0.55f;                           // 两军纵深中心（three z）
        public const double RIVER_X = 1.05, RIVER_W = 1.7, RIVER_BANK = 1.5, RIVER_BED = -0.42, RIVER_SURF = -0.1;
        public const double WALL_Z = -7.4, GATE_X = 4.6;          // 城墙正面（three z）与城门中心 x
        public const double SEA_Y = -0.14;
        const double ZCd = -0.55;
        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(double x, double y, double z) { return new Vector3((float)x, (float)y, (float)z); }
        static Vector3 P(double x, double y, double z) { return new Vector3((float)x, (float)y, (float)-z); }
        static Color Shade(Color c, double k) { return Art.Shade(c, (float)k); }
        static double Sstep(double a, double b, double x) { return ClashUtil.Sstep(a, b, x); }
        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }
        static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double Perlin(double x, double y) { return WorldGeo.Perlin(x, y); }
        static int JsRound(double v) { return (int)Math.Floor(v + 0.5); }
        public static bool Walled(string kind) { return kind == "gate" || kind == "castle" || kind == "wall"; }
        // 海岸线（登陆战）：攻方（左）身后为沙滩与海，海岸线向远处斜向右后方伸去
        public static double ShoreX(double z) { return -10.6 + Math.Min(60, Math.Max(0, ZCd - z)) * 0.42 - Math.Min(20, Math.Max(0, z - ZCd)) * 0.2 + Math.Sin(z * 0.23) * 0.7; }
        static readonly Color WOOD = ClashGeo.WOOD, GOLD = ClashGeo.GOLD;

        // 高度场（three 坐标 x、z）（= JS makeHeight）
        public sealed class Height
        {
            readonly string kind; readonly double ox, oz; readonly bool hilly, walled, coast;
            public Height(string kind, int seed, ClashLook look)
            {
                this.kind = kind;
                var r = new SeededRandom(seed);
                coast = look != null && look.coast;
                ox = r.NextDouble() * 200; oz = r.NextDouble() * 200;
                hilly = kind == "hill" || kind == "mountain";
                walled = Walled(kind);
            }
            double Pn(double x, double z, double f) { return Perlin(ox + x * f, oz + z * f) - 0.465; }
            public double H(double x, double z)
            {
                double far = Sstep(9, 42, -z);
                double calm = 1 - 0.55 * (1 - Sstep(5, 10, Math.Abs(z - ZCd))) * (1 - Sstep(7, 13, Math.Abs(x)));
                double y;
                if (hilly) y = (Pn(x, z, 0.1) * 1.4 + Pn(x, z, 0.32) * 0.22) * calm + 0.7 * Sstep(-3, 7.5, x) - 0.18;
                else if (walled) y = Pn(x, z, 0.22) * 0.07;
                else y = (Pn(x, z, 0.15) * 0.34 + Pn(x, z, 0.5) * 0.05) * calm;
                y += far * ((Pn(x, z, 0.03) + 0.25) * (hilly ? 14 : 7) + (hilly ? 2.5 : 0.6));
                y += Sstep(16, 34, Math.Abs(x)) * (hilly ? 3 : 1.4) * (0.6 + Pn(x, z, 0.06));
                // 远山：平滑的脊状噪声
                double rk = Sstep(44, 74, -z);
                if (rk > 0)
                {
                    double rn = 1 - Math.Min(1, Math.Abs(Pn(x, z, 0.021) * 2.3));
                    y += rk * (rn * rn * (hilly ? 30 : 15) + (Pn(x, z, 0.07) + 0.3) * (hilly ? 7 : 3.5));
                }
                if (kind == "river")
                {
                    // 近处：河床与缓坡河岸；远处：河谷两侧以有限的坡度升起（不在山脊上切出直壁峡谷）
                    double d = Math.Abs(x - RIVER_X);
                    double bank = Sstep(RIVER_W, RIVER_W + RIVER_BANK, d);
                    double near = Lerp(RIVER_BED + Pn(x, z, 0.8) * 0.04, Math.Max(y * 0.6, -0.02) + 0.06, bank);
                    double vf = Sstep(-6, -26, z);
                    if (vf > 0)
                    {
                        double valley = Lerp(RIVER_BED, 0.06, bank) + Math.Max(0, d - RIVER_W - RIVER_BANK) * (0.18 + 0.22 * Sstep(-20, -70, z)) + Math.Max(0, d - 7) * 0.12;
                        y = Lerp(near, Math.Min(y, valley), vf);
                    }
                    else y = near;
                }
                if (coast)
                {
                    // 登陆：海岸线以外降为缓坡沙滩与海床（远山也不在海上隆起）
                    double d = x - ShoreX(z);
                    if (d < 5)
                    {
                        double beach = Math.Max(-1.5, SEA_Y + 0.04 + d * 0.065);
                        y = Lerp(y, beach, Sstep(5, 0.6, d));
                    }
                }
                return y;
            }
        }

        // 顶点底色与地表权重 aGnd = (泥土 / 踩踏 / 道路, 沙, 岩石倾向, 农田)（= JS groundAttr）
        static Color LerpU(Color a, Color b, double t) { return ClashUtil.LerpU(a, b, (float)t); }
        static void GroundAttr(string kind, double x, double z, double y, double n, double nf, ClashLook look, out Color c, out double g0, out double g1, out double g2, out double g3)
        {
            double dirt = 0, sand = 0, rock = 0, farm = 0;
            bool walled = Walled(kind);
            double trample = Math.Exp(-(x * x) / 12 - ((z - ZCd) * (z - ZCd)) / 8);
            switch (kind)
            {
                case "forest": c = C(0.33f, 0.5f, 0.26f); break;
                case "hill": c = C(0.52f, 0.62f, 0.33f); break;
                case "mountain": c = C(0.5f, 0.56f, 0.36f); break;
                case "gate": case "castle": case "wall": c = C(0.5f, 0.6f, 0.35f); break;
                default: c = C(0.46f, 0.64f, 0.31f); break;
            }
            c = LerpU(c, C(0.58f, 0.68f, 0.33f), Clamp01(n * 1.6) * 0.45);
            // 群落底色（草原枯黄、沙漠沙土、热带深绿、地中海橄榄绿……）
            if (look != null && look.grass.HasValue) c = LerpU(c, Shade(look.grass.Value, (n - 0.1) * 0.25), kind == "forest" ? 0.6 : 0.8);
            if (look != null && look.sandy > 0) sand = Math.Max(sand, look.sandy * Sstep(-0.05, 0.25, nf + n * 0.5));
            if (walled)
            {
                // 墙前夯土与车辙大道、门前石板、墙根阴湿
                double earth = (1 - Sstep(2, 7, z - WALL_Z)) * Sstep(WALL_Z - 2, WALL_Z - 0.5, z);
                dirt = Math.Max(dirt, earth * 0.6 + trample * 0.35);
                if (kind != "wall")
                {
                    double d = Math.Abs(x - GATE_X), w = 1.45 + Math.Max(0, z - WALL_Z) * 0.07;
                    double road = 1 - Sstep(w - 0.35, w + 0.35, d);
                    dirt = Math.Max(dirt, road);
                    if (z < WALL_Z + 2.4 && d < 2.3 && !(look != null && look.wall == "palisade")) { double pv = 1 - Sstep(1.7, 2.3, d); c = LerpU(c, C(0.64f, 0.62f, 0.58f), pv); dirt *= 1 - pv; rock = -pv; }
                }
                c = LerpU(c, C(0.42f, 0.42f, 0.36f), (1 - Sstep(WALL_Z, WALL_Z + 1.2, z)) * 0.35);
            }
            else
            {
                dirt = trample * (kind == "forest" ? 0.55 : 0.7);
                if (kind == "forest") dirt = Math.Max(dirt, Sstep(-4, -9, z) * 0.35 * (0.5 + nf));
                if ((kind == "plain" || kind == "river") && z < -12 && z > -40 && Math.Abs(x) < 46)
                {
                    farm = Sstep(-12, -15, z) * (1 - Sstep(-34, -40, z)) * (1 - Sstep(38, 46, Math.Abs(x))) * Sstep(-0.05, 0.12, nf - (1 - (look != null ? look.farm : 1)) * 0.5);
                    if (kind == "river" && Math.Abs(x - RIVER_X) < RIVER_W + RIVER_BANK + 2.5) farm = 0;
                }
            }
            if (kind == "river")
            {
                double d = Math.Abs(x - RIVER_X);
                sand = 1 - Sstep(RIVER_W + RIVER_BANK - 0.3, RIVER_W + RIVER_BANK + 0.7, d);
                if (d < RIVER_W) c = LerpU(c, C(0.5f, 0.46f, 0.36f), 0.6);
            }
            if (look != null && look.coast)
            {
                // 海滩：海岸线内外约 3 单位为沙，水下也是沙（透出浅海的青绿）
                double d = x - ShoreX(z);
                double b = 1 - Sstep(1.4, 3.6, d + (nf - 0.1) * 1.5);
                sand = Math.Max(sand, b); farm *= Sstep(4, 8, d); dirt *= Sstep(0.5, 2.5, d);
                if (d < 0.2) c = LerpU(c, C(0.62f, 0.58f, 0.46f), 0.6);
            }
            if (kind == "mountain") rock = Math.Max(rock, Sstep(2.5, 7, y) * 0.8);
            if (kind == "hill") rock = Math.Max(rock, Sstep(5, 10, y) * 0.5);
            // 远山：高处草色转黄绿、再到岩石
            double hi = Sstep(6, 22, y) * Sstep(30, 50, -z);
            if (hi > 0) { c = LerpU(c, C(0.55f, 0.58f, 0.4f), hi * 0.7); rock = Math.Max(rock, hi * 0.6); }
            // 远处偏冷（雾会进一步融合）
            c = LerpU(c, C(0.48f, 0.58f, 0.46f), Sstep(16, 60, -z) * 0.35);
            g0 = Clamp01(dirt); g1 = Clamp01(sand); g2 = rock; g3 = Clamp01(farm);
        }
        // 中心 ±fine 内用 step，之外按 grow 递增（不超过 cap）
        static List<double> Axis(double a, double b, double step, double fine, double grow, double cap)
        {
            Func<double, double> st = x => Math.Min(cap, Math.Abs(x) < fine ? step : step + (Math.Abs(x) - fine) * grow);
            var outp = new List<double>();
            for (double x = 0; x <= b;) { outp.Add(x); x += st(x); }
            var neg = new List<double>();
            for (double x = -step; x >= a;) { neg.Add(x); x -= st(x); }
            neg.Reverse();
            neg.AddRange(outp);
            return neg;
        }

        // 道具网格：按 aKind 分成几个 MeshBuilder，最后合并为一个网格（一次绘制 + 一次投影）；uv2.x = aKind
        public const int K_PLAIN = 0, K_LEAF = 1, K_ROCK = 2, K_BRICK = 3, K_ROOF = 4;
        static MeshBuilder[] PropBuilders() { var b = new MeshBuilder[5]; for (int i = 0; i < 5; i++) b[i] = new MeshBuilder(); return b; }
        public static Mesh MergeProps(MeshBuilder[] mbs, string name)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>(); var k2 = new List<Vector2>(); var t = new List<int>();
            for (int k = 0; k < mbs.Length; k++)
            {
                var mb = mbs[k];
                int o = v.Count;
                v.AddRange(mb.v); n.AddRange(mb.n); c.AddRange(mb.c);
                for (int i = 0; i < mb.v.Count; i++) k2.Add(new Vector2(k, 0));
                foreach (var i in mb.t) t.Add(o + i);
            }
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(1, k2); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
        static SphereOpt SO(float seed = 0, float rough = 0, float bottomShade = 0, float top = 0.06f, float smoothN = 0, float flat = -2)
        {
            return new SphereOpt { seed = seed, rough = rough, bottomShade = bottomShade, top = top, smoothN = smoothN, flat = flat };
        }
        static LP L(double r, double y, bool sharp = false) { return new LP((float)r, (float)y, sharp); }
        static LP L(double r, double y, Color c, bool sharp = false) { return new LP((float)r, (float)y, c, sharp); }

        // 树：平滑树干、圆润树冠（近景另加三团叶簇，轮廓起伏）；pine 为层叠的松冠，poplar 为高耸的杨（x, y, z 为 three 坐标，s ≈ 树高）
        static void TreeInto(MeshBuilder[] B, double x, double y, double z, double s, string type, Color leaf, int seed, bool near)
        {
            MeshBuilder tk = B[K_PLAIN], lf = B[K_LEAF];
            var p = P(x, y, z);
            var TR = C(0.38f, 0.27f, 0.16f);
            // 第三版修订：树冠一律 detail 2（中远景也不再露出多边形轮廓），法线向椭球法线混合（明暗圆润）
            int ts = near ? 9 : 6, cs = near ? 16 : 12, d = 2;
            Action<double, double, double, Vector3, Color, int> crown = (cx, cy, cz, r, col, sd) =>
            {
                lf.Sphere(V(p.x + cx, p.y + cy, p.z + cz), r, col, d, SO(seed: sd, rough: 0.09f, bottomShade: 0.32f, top: 0.08f, smoothN: 0.6f));
                if (!near) return;
                for (int k = 0; k < 3; k++)
                {
                    double a = sd * 1.7 + k * 2.09, up = 0.25 + 0.3 * ((k + sd) % 2);
                    lf.Sphere(V(p.x + cx + Math.Cos(a) * r.x * 0.62, p.y + cy + up * r.y, p.z + cz + Math.Sin(a) * r.z * 0.62), V(r.x * 0.5, r.y * 0.52, r.z * 0.5),
                        Shade(col, k == 1 ? -0.06 : 0.04), 1, SO(seed: sd + 7 * k + 1, rough: 0.07f, bottomShade: 0.28f, top: 0.1f, smoothN: 0.6f));
                }
            };
            if (type == "palm")
            {
                // 棕榈 / 椰枣：微弯的环纹树干（石块材质的横纹）+ 9 片下垂的羽状叶
                double lean = (((seed % 7) - 3) * 0.05 + 0.04) * s; const int n = 6;
                var pts = new Vector3[n]; var rr = new float[n];
                for (int i = 0; i < n; i++) { double u = i / (double)(n - 1); pts[i] = V(p.x + lean * u * u, p.y + u * 0.92 * s, p.z + lean * 0.35 * u * u); rr[i] = (float)(Lerp(0.06, 0.034, u) * s); }
                B[K_ROCK].Tube(pts, rr, 7, C(0.5f, 0.42f, 0.31f), false);
                var top = pts[n - 1];
                lf.Sphere(V(top.x, top.y + 0.01 * s, top.z), (float)(0.055 * s), Shade(leaf, -0.22), 1, SO(smoothN: 1));
                for (int k = 0; k < 9; k++)
                {
                    double a = k * 2.399 + seed * 0.37, Ln = (0.42 + 0.08 * ((k * 7 + seed) % 3)) * s;
                    FrondInto(lf, top, Math.Cos(a), Math.Sin(a), Ln, (k % 3 == 0 ? 0.2 : 0.13) * s, 0.075 * s, Shade(leaf, ((k % 3) - 1) * 0.05));
                }
                return;
            }
            if (type == "cypress")
            {
                // 地中海柏树：深绿、细高的尖塔形
                tk.Lathe(p, new[] { L(0.05 * s, 0), L(0.035 * s, 0.16 * s) }, ts, TR);
                lf.Lathe(V(p.x, p.y + 0.1 * s, p.z), new[] { L(0.02 * s, 0), L(0.15 * s, 0.1 * s), L(0.18 * s, 0.32 * s), L(0.14 * s, 0.66 * s), L(0.06 * s, 0.9 * s), L(0, 1.0 * s) }, cs, Shade(leaf, -0.14));
                return;
            }
            if (type == "olive" || type == "acacia" || type == "umbrella" || type == "scrub")
            {
                // 橄榄（扭曲矮干、灰绿宽冠）/ 金合欢（斜干分叉、伞形平顶）/ 石松（高直干、扁平冠）/ 灌丛
                int sd = seed % 13;
                if (type == "scrub") { lf.Sphere(V(p.x, p.y + 0.12 * s, p.z), V(0.3 * s, 0.2 * s, 0.28 * s), Shade(leaf, 0.02), 2, SO(seed: sd, rough: 0.12f, bottomShade: 0.35f, flat: -0.4f, smoothN: 0.5f)); return; }
                double hy = type == "olive" ? 0.36 : type == "acacia" ? 0.62 : 0.66, bend = (sd % 2 == 1 ? 1 : -1) * (type == "olive" ? 0.07 : 0.12) * s;
                tk.Tube(new[] { V(p.x, p.y, p.z), V(p.x + bend * 0.6, p.y + hy * 0.5 * s, p.z), V(p.x + bend, p.y + hy * s, p.z + bend * 0.3) }, new[] { (float)(0.07 * s), (float)(0.05 * s), (float)(0.035 * s) }, ts, type == "olive" ? C(0.42f, 0.36f, 0.28f) : TR, false);
                if (type == "acacia") tk.Tube(new[] { V(p.x + bend * 0.5, p.y + hy * 0.45 * s, p.z), V(p.x - bend * 0.9, p.y + hy * s, p.z - bend * 0.2) }, new[] { (float)(0.035 * s), (float)(0.025 * s) }, 6, TR, false);
                var lc = type == "olive" ? C(0.45f, 0.52f, 0.36f) : leaf;
                var r = type == "olive" ? V(0.42 * s, 0.27 * s, 0.4 * s) : V(0.5 * s, 0.13 * s, 0.46 * s);
                crown(bend, (hy + (type == "olive" ? 0.14 : 0.06)) * s, 0, r, lc, seed);
                if (type != "umbrella") crown(bend - 0.22 * s, (hy + 0.02) * s, 0.1 * s, V(r.x * 0.62, r.y * 0.8, r.z * 0.6), Shade(lc, 0.05), seed + 5);
                return;
            }
            if (type == "pine")
            {
                tk.Lathe(p, new[] { L(0.06 * s, 0), L(0.04 * s, 0.32 * s) }, ts, TR);
                Action<double, double, double, Color> tier = (y0, r, h, col) => lf.Lathe(V(p.x, p.y + y0 * s, p.z), new[] { L(0, h * s * 0.1), L(r * s, 0, true), L(r * s * 0.5, h * s * 0.5), L(0, h * s) }, cs, col);
                tier(0.2, 0.44, 0.62, Shade(leaf, -0.06)); tier(0.52, 0.34, 0.56, leaf); tier(0.8, 0.22, 0.42, Shade(leaf, 0.06));
            }
            else if (type == "poplar")
            {
                tk.Lathe(p, new[] { L(0.05 * s, 0), L(0.035 * s, 0.26 * s) }, ts, TR);
                crown(0, 0.78 * s, 0, V(0.18 * s, 0.56 * s, 0.18 * s), leaf, seed);
            }
            else if (type == "broad2")
            {
                tk.Lathe(p, new[] { L(0.065 * s, 0), L(0.045 * s, 0.4 * s) }, ts, TR);
                crown(0.06 * s, 0.6 * s, 0, V(0.38 * s, 0.34 * s, 0.38 * s), leaf, seed);
                crown(-0.15 * s, 0.82 * s, 0.06 * s, V(0.28 * s, 0.25 * s, 0.28 * s), Shade(leaf, 0.06), seed + 3);
            }
            else
            {
                tk.Lathe(p, new[] { L(0.065 * s, 0), L(0.045 * s, 0.42 * s) }, ts, TR);
                crown(0, 0.66 * s, 0, V(0.44 * s, 0.4 * s, 0.44 * s), leaf, seed);
            }
        }
        static void RockInto(MeshBuilder[] B, double x, double y, double z, double r, Color col, int seed)
        {
            B[K_ROCK].Sphere(P(x, y, z), V(r * 1.25, r * 0.75, r), col, r > 0.3 ? 2 : 1, SO(seed: seed, rough: 0.2f, flat: -0.35f, bottomShade: 0.25f, top: 0.08f, smoothN: 0.35f));
        }
        // 羽状叶（双面，中脉略折）：o 为叶柄起点（Unity），(ca, sa) 为水平方向，L 长、up 起拱、w 半宽
        static void FrondInto(MeshBuilder mb, Vector3 o, double ca, double sa, double Ln, double up, double w, Color col)
        {
            const int N = 5; double sx = -sa, sz = ca;
            var row = new Vector3[N + 1][];
            for (int i = 0; i <= N; i++)
            {
                double u = i / (double)N, cx = o.x + ca * Ln * u, cz = o.z + sa * Ln * u, cy = o.y + up * Math.Sin(Math.PI * u * 0.9) - 0.42 * Ln * u * u;
                double wd = w * Math.Pow(Math.Sin(Math.PI * Math.Min(1, 0.1 + u * 0.9)), 0.75);
                row[i] = new[] { V(cx + sx * wd, cy - wd * 0.35, cz + sz * wd), V(cx, cy, cz), V(cx - sx * wd, cy - wd * 0.35, cz - sz * wd) };
            }
            Vector3 nU = V(ca * 0.25, 1, sa * 0.25), nD = V(-ca * 0.25, -1, -sa * 0.25);
            Color cB = Shade(col, -0.12), cT = Shade(col, 0.08);
            for (int i = 0; i < N; i++)
            {
                var A = row[i]; var Bq = row[i + 1]; Color c0 = i > 0 ? col : cB, c1 = i + 1 == N ? cT : col;
                foreach (var lm in new[] { new[] { 0, 1 }, new[] { 1, 2 } })
                {
                    int l = lm[0], m = lm[1];
                    mb.TriN(A[l], Bq[l], Bq[m], nU, nU, nU, c0, c1, c1); mb.TriN(A[l], Bq[m], A[m], nU, nU, nU, c0, c1, c0);
                    mb.TriN(A[l], Bq[m], Bq[l], nD, nD, nD, c0, c1, c1); mb.TriN(A[l], A[m], Bq[m], nD, nD, nD, c0, c0, c1);
                }
            }
        }

        // ---- 房屋（three 坐标经 P 转换；y 为地面高度）
        static readonly Color WHITE = C(0.88f, 0.84f, 0.76f), MUD = C(0.8f, 0.66f, 0.48f), THATCH = C(0.68f, 0.57f, 0.36f), TERRA = C(0.72f, 0.36f, 0.24f), FELT = C(0.88f, 0.84f, 0.74f), DARKW = C(0.3f, 0.2f, 0.13f);
        // 双坡顶：w 沿屋脊、d 为跨度、h 脊高；rz = true 时屋脊沿 z（山墙朝镜头）；wallCol 给出时补两端山墙三角
        static void GableInto(MeshBuilder R, MeshBuilder Wl, double x, double y0, double z, double w, double d, double h, Color col, Color? wallCol, bool rz)
        {
            Func<double, double, double, Vector3> M3 = (u, yy, v) => rz ? P(x + v, yy, z + u) : P(x + u, yy, z + v);
            Func<double, double, double, Vector3> Nn = (nu, ny, nv) => rz ? V(nv, ny, -nu) : V(nu, ny, -nv);
            double hw = w / 2, hd = d / 2; var dark = Shade(col, -0.16);
            Vector3 r0 = M3(-hw, y0 + h, 0), r1 = M3(hw, y0 + h, 0);
            foreach (int sv in new[] { 1, -1 })
            {
                Vector3 e0 = M3(-hw, y0, sv * hd), e1 = M3(hw, y0, sv * hd), n = Nn(0, hd, sv * h);
                R.TriN(r0, e0, e1, n, n, n, col, dark, dark);
                R.TriN(r0, e1, r1, n, n, n, col, dark, col);
                // 檐下（深色，略厚）
                double t = Math.Min(0.06, h * 0.06); Vector3 u0 = M3(-hw, y0 - t, sv * hd), u1 = M3(hw, y0 - t, sv * hd), no = Nn(0, 0, sv);
                Wl.TriN(e0, u0, u1, no, no, no, Shade(col, -0.45)); Wl.TriN(e0, u1, e1, no, no, no, Shade(col, -0.45));
            }
            if (wallCol.HasValue)
                foreach (int su in new[] { 1, -1 })
                {
                    double g = hw - Math.Min(0.15, w * 0.06); var n = Nn(su, 0, 0);
                    Wl.TriN(M3(su * g, y0, -hd * 0.86), M3(su * g, y0, hd * 0.86), M3(su * g, y0 + h * 0.9, 0), n, n, n, wallCol.Value);
                }
        }
        // 四坡攒尖（金字塔形）
        static void PyramidInto(MeshBuilder R, double x, double y0, double z, double w, double d, double h, Color col)
        {
            var a = P(x, y0 + h, z); double hw = w / 2, hd = d / 2; var dark = Shade(col, -0.14);
            var c = new[] { P(x - hw, y0, z - hd), P(x + hw, y0, z - hd), P(x + hw, y0, z + hd), P(x - hw, y0, z + hd) };
            var n = new[] { V(0, hd, h), V(h, hw, 0), V(0, hd, -h), V(-h, hw, 0) };
            for (int i = 0; i < 4; i++) R.TriN(a, c[i], c[(i + 1) % 4], n[i], n[i], n[i], col, dark, dark);
        }
        static void DomeInto(MeshBuilder Wl, double x, double y, double z, double r, Color col, int seg = 14)
        {
            Wl.Lathe(P(x, y, z), new[] { L(r, 0), L(r * 0.98, r * 0.3), L(r * 0.8, r * 0.7), L(r * 0.45, r * 0.95), L(0.04 * r, r * 1.06), L(0, r * 1.08) }, seg, col);
        }
        static void StupaInto(MeshBuilder[] B, double x, double y, double z, double s)
        {
            var W = B[K_PLAIN]; var c = C(0.9f, 0.87f, 0.8f); var gold = C(0.85f, 0.68f, 0.3f);
            W.Lathe(P(x, y, z), new[] { L(1.5 * s, 0), L(1.5 * s, 0.35 * s, true), L(1.3 * s, 0.4 * s), L(1.3 * s, 0.55 * s) }, 18, Shade(c, -0.08), true);
            W.Lathe(P(x, y + 0.55 * s, z), new[] { L(1.2 * s, 0), L(1.18 * s, 0.35 * s), L(1.0 * s, 0.8 * s), L(0.6 * s, 1.1 * s), L(0, 1.22 * s) }, 18, c);
            W.Box(P(x, y + 1.86 * s, z), V(0.36 * s, 0.24 * s, 0.36 * s), Shade(gold, -0.15));
            W.Tube(new[] { P(x, y + 1.9 * s, z), P(x, y + 2.62 * s, z) }, new[] { (float)(0.03 * s), (float)(0.022 * s) }, 6, gold, false);
            foreach (var hr in new[] { new[] { 2.15, 0.3 }, new[] { 2.33, 0.22 }, new[] { 2.48, 0.14 } })
                W.Lathe(P(x, y + hr[0] * s, z), new[] { L(hr[1] * s, 0), L(hr[1] * s * 0.9, 0.04 * s), L(0, 0.05 * s) }, 12, gold);
        }
        static void HouseInto(MeshBuilder[] B, string style, double x, double y, double z, SeededRandom rnd, double s = 1)
        {
            MeshBuilder Wl = B[K_PLAIN], R = B[K_ROOF], T = B[K_ROCK];
            bool ry = rnd.NextDouble() < 0.5; double w = (1.6 + rnd.NextDouble() * 0.6) * s, dd = 1.1 * s;
            switch (style)
            {
                case "tile":
                    {
                        double hh = 1.05 * s;
                        Wl.Box(P(x, y + hh / 2, z), V(dd + 0.3 * s, hh, w), WHITE);
                        Wl.Box(P(x, y + 0.36 * s, z + w / 2 + 0.01), V(0.32 * s, 0.72 * s, 0.04), DARKW);
                        GableInto(R, Wl, x, y + hh, z, w + 0.3 * s, dd + 0.65 * s, 0.5 * s, TERRA, WHITE, true);
                        break;
                    }
                case "flat":
                    {
                        double hh = (1.1 + rnd.NextDouble() * 0.3) * s; var c = Shade(MUD, (rnd.NextDouble() - 0.5) * 0.1); double ph = 0.16 * s, pt = 0.08 * s;
                        Action<double, double, double, double, double> box = (bx, by, bw, bh, bd) =>
                        {
                            Wl.Box(P(bx, by + bh / 2, z), V(bw, bh, bd), c);
                            Wl.Box(P(bx, by + bh + ph / 2, z + bd / 2 - pt / 2), V(bw, ph, pt), Shade(c, 0.05));
                            Wl.Box(P(bx, by + bh + ph / 2, z - bd / 2 + pt / 2), V(bw, ph, pt), Shade(c, 0.05));
                            Wl.Box(P(bx - bw / 2 + pt / 2, by + bh + ph / 2, z), V(pt, ph, bd), Shade(c, 0.05));
                            Wl.Box(P(bx + bw / 2 - pt / 2, by + bh + ph / 2, z), V(pt, ph, bd), Shade(c, 0.05));
                        };
                        box(x, y, w, hh, dd + 0.2 * s);
                        if (rnd.NextDouble() < 0.5) box(x - w * 0.22, y + hh, w * 0.45, 0.62 * s, dd * 0.7);
                        else if (rnd.NextDouble() < 0.45) DomeInto(Wl, x + w * 0.15, y + hh, z, 0.42 * s, C(0.9f, 0.88f, 0.82f));
                        Wl.Box(P(x + w * 0.2, y + 0.33 * s, z + dd / 2 + 0.11 * s), V(0.3 * s, 0.66 * s, 0.03), DARKW);
                        Wl.Box(P(x - w * 0.22, y + 0.7 * s, z + dd / 2 + 0.11 * s), V(0.18 * s, 0.2 * s, 0.03), C(0.18f, 0.14f, 0.12f));
                        break;
                    }
                case "yurt":
                    {
                        double r = 0.78 * s;
                        Wl.Lathe(P(x, y, z), new[] { L(r, 0), L(r, 0.5 * s), L(r * 0.95, 0.6 * s), L(r * 0.45, 0.95 * s), L(0.1 * s, 1.06 * s), L(0, 1.08 * s) }, 16, FELT);
                        Wl.Lathe(P(x, y + 0.4 * s, z), new[] { L(r + 0.012, 0), L(r + 0.012, 0.07 * s) }, 16, C(0.62f, 0.28f, 0.18f));
                        Wl.Lathe(P(x, y + 0.7 * s, z), new[] { L(r * 0.82, 0), L(r * 0.7, 0.06 * s) }, 16, C(0.62f, 0.28f, 0.18f));
                        Wl.Box(P(x, y + 0.28 * s, z + r - 0.03), V(0.34 * s, 0.56 * s, 0.08), C(0.72f, 0.32f, 0.14f));
                        break;
                    }
                case "long":
                    {
                        double Ln = 3.0 * s, D = 1.4 * s, hw = 0.6 * s;
                        Wl.Box(P(x, y + hw / 2, z), V(Ln, hw, D), C(0.46f, 0.34f, 0.22f));
                        Wl.Box(P(x + Ln * 0.2, y + 0.3 * s, z + D / 2 + 0.01), V(0.34 * s, 0.6 * s, 0.04), DARKW);
                        GableInto(T, Wl, x, y + hw - 0.05, z, Ln + 0.3 * s, D + 0.8 * s, 1.25 * s, THATCH, C(0.5f, 0.38f, 0.25f), false);
                        break;
                    }
                case "round":
                    {
                        double r = 0.85 * s;
                        Wl.Lathe(P(x, y, z), new[] { L(r, 0), L(r, 0.72 * s) }, 16, C(0.64f, 0.54f, 0.4f));
                        Wl.Box(P(x, y + 0.3 * s, z + r), V(0.32 * s, 0.6 * s, 0.06), DARKW);
                        T.Lathe(P(x, y + 0.6 * s, z), new[] { L(r + 0.24 * s, 0), L(r * 0.6, 0.55 * s), L(0.06 * s, 1.18 * s), L(0, 1.22 * s) }, 16, THATCH);
                        break;
                    }
                case "stilt":
                    {
                        double Ln = 2.0 * s, D = 1.3 * s, ph = 0.62 * s; var wc = C(0.66f, 0.56f, 0.38f);
                        foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 })
                                Wl.Tube(new[] { P(x + sx * Ln * 0.42, y, z + sz * D * 0.38), P(x + sx * Ln * 0.42, y + ph, z + sz * D * 0.38) }, new[] { (float)(0.05 * s), (float)(0.045 * s) }, 6, C(0.4f, 0.3f, 0.2f), false);
                        Wl.Box(P(x, y + ph, z), V(Ln + 0.2 * s, 0.1 * s, D + 0.3 * s), C(0.46f, 0.36f, 0.24f));
                        Wl.Box(P(x, y + ph + 0.36 * s, z), V(Ln, 0.7 * s, D), wc);
                        GableInto(T, Wl, x, y + ph + 0.68 * s, z, Ln + 0.5 * s, D + 0.8 * s, 1.0 * s, THATCH, wc, false);
                        break;
                    }
                case "thatch":
                    {
                        double hh = 0.9 * s; var c = C(0.74f, 0.58f, 0.42f);
                        Wl.Box(P(x, y + hh / 2, z), V(w * 0.8, hh, dd), c);
                        Wl.Box(P(x, y + 0.32 * s, z + dd / 2 + 0.01), V(0.3 * s, 0.64 * s, 0.04), DARKW);
                        PyramidInto(T, x, y + hh - 0.04, z, w * 0.8 + 0.5 * s, dd + 0.5 * s, 0.85 * s, THATCH);
                        break;
                    }
                default:
                    {
                        Wl.Box(P(x, y + 0.5 * s, z), ry ? V(dd, 1.1 * s, w) : V(w, 1.1 * s, dd), C(0.86f, 0.8f, 0.68f));
                        R.ChineseRoof(P(x, y + 1.05 * s, z), (float)((ry ? dd : w) + 0.5 * s), (float)((ry ? w : dd) + 0.45 * s), (float)(0.62 * s), C(0.3f, 0.32f, 0.36f));
                        break;
                    }
            }
        }
        // 海滩上的小船（登陆）：U 形船壳 + 桅杆
        static void BoatInto(MeshBuilder[] B, double x, double y, double z, double s, double ang)
        {
            var W = B[K_PLAIN]; var wood = C(0.42f, 0.3f, 0.2f); double ca = Math.Cos(ang), sa = Math.Sin(ang);
            Func<double, double, double, Vector3> at = (u, v, h) => P(x + ca * u - sa * v, y + h, z + sa * u + ca * v);
            const int NU = 8, NV = 5; double Ln = 1.5 * s;
            var rp = new Vector3[NU + 1, NV + 1]; var rn = new Vector3[NU + 1, NV + 1];
            for (int i = 0; i <= NU; i++)
            {
                double u = -1 + 2.0 * i / NU, wd = 0.44 * s * Math.Pow(Math.Max(0, 1 - u * u), 0.55), dp = 0.36 * s * (1 - 0.4 * u * u), sh = 0.1 * s * u * u;
                for (int j = 0; j <= NV; j++)
                {
                    double th = Math.PI * j / NV, cx = Math.Cos(th), cy = -Math.Sin(th);
                    rp[i, j] = at(u * Ln, cx * wd, sh + cy * dp + 0.26 * s); rn[i, j] = V(-sa * cx, cy, -ca * cx);
                }
            }
            var up = V(0, 1, 0); var deck = C(0.5f, 0.38f, 0.26f);
            for (int i = 0; i < NU; i++)
            {
                for (int j = 0; j < NV; j++)
                {
                    var k = Shade(wood, j == 0 || j == NV - 1 ? 0.08 : -0.05);
                    W.TriN(rp[i, j], rp[i + 1, j], rp[i + 1, j + 1], rn[i, j], rn[i + 1, j], rn[i + 1, j + 1], k);
                    W.TriN(rp[i, j], rp[i + 1, j + 1], rp[i, j + 1], rn[i, j], rn[i + 1, j + 1], rn[i, j + 1], k);
                }
                // 甲板（盖住船壳内侧）
                W.TriN(rp[i, 0], rp[i + 1, 0], rp[i + 1, NV], up, up, up, deck); W.TriN(rp[i, 0], rp[i + 1, NV], rp[i, NV], up, up, up, deck);
            }
            W.Tube(new[] { at(0.2 * Ln, 0, 0.1 * s), at(0.2 * Ln, 0, 1.9 * s) }, new[] { (float)(0.035 * s), (float)(0.025 * s) }, 6, wood, false);
            W.Tube(new[] { at(0.2 * Ln - 0.6 * s, 0, 1.25 * s), at(0.2 * Ln + 0.6 * s, 0, 1.25 * s) }, new[] { (float)(0.02 * s), (float)(0.02 * s) }, 5, wood, false);
            W.Tube(new[] { at(0.2 * Ln - 0.5 * s, 0, 1.2 * s), at(0.2 * Ln + 0.5 * s, 0, 1.2 * s) }, new[] { (float)(0.07 * s), (float)(0.06 * s) }, 6, C(0.86f, 0.82f, 0.7f), true);   // 收起的帆
        }
        // 草丛（7 片弯叶，根暗梢亮，法线朝上，与地面同调）/ 野花（茎 + 五瓣花，花瓣乘实例颜色）。顶点色 a = aTint（乘实例颜色的权重）
        public static Mesh TuftMesh()
        {
            var mb = new MeshBuilder();
            var rnd = new SeededRandom(91);
            Color dark = C(0.55f, 0.55f, 0.55f), lite = C(1.05f, 1.05f, 1.0f), mid = C(0.8f, 0.8f, 0.78f);
            for (int k = 0; k < 7; k++)
            {
                double a = k * 0.9 + rnd.NextDouble() * 0.6, ca = Math.Cos(a), sa = Math.Sin(a);
                double r0 = 0.02 + rnd.NextDouble() * 0.05, h = 0.14 + rnd.NextDouble() * 0.12, lean = 0.05 + rnd.NextDouble() * 0.08, w = 0.022;
                double bx = ca * r0, bz = sa * r0;
                Vector3 b0 = V(bx - sa * w, 0, bz + ca * w), b1 = V(bx + sa * w, 0, bz - ca * w);
                Vector3 m0 = V(bx + ca * lean * 0.4 - sa * w * 0.6, h * 0.55, bz + sa * lean * 0.4 + ca * w * 0.6), m1 = V(bx + ca * lean * 0.4 + sa * w * 0.6, h * 0.55, bz + sa * lean * 0.4 - ca * w * 0.6);
                var tip = V(bx + ca * lean, h, bz + sa * lean);
                foreach (int sd in new[] { 1, -1 })
                {
                    var nf = V(ca * 0.35 * sd, 1, sa * 0.35 * sd).normalized;
                    mb.TriN(b0, b1, m1, nf, nf, nf, dark, dark, mid); mb.TriN(b0, m1, m0, nf, nf, nf, dark, mid, mid);
                    mb.TriN(m0, m1, tip, nf, nf, nf, mid, mid, lite);
                }
            }
            for (int i = 0; i < mb.c.Count; i++) { var c = mb.c[i]; c.a = 1; mb.c[i] = c; }
            return mb.ToMesh("clash tuft");
        }
        public static Mesh FlowerMesh()
        {
            var stem = new MeshBuilder(); var pet = new MeshBuilder(); var up = V(0, 1, 0);
            var green = C(0.32f, 0.52f, 0.22f);
            foreach (var p in new[] { new[] { 0, 0, 0.26 }, new[] { 0.1, 0.06, 0.2 }, new[] { -0.07, 0.08, 0.23 } })
            {
                double x = p[0], z = p[1], h = p[2];
                stem.TriN(V(x - 0.012, 0, z), V(x + 0.012, 0, z), V(x, h, z), V(0, 0.3, 1), V(0, 0.3, 1), up, green);
                stem.TriN(V(x + 0.012, 0, z), V(x - 0.012, 0, z), V(x, h, z), V(0, 0.3, -1), V(0, 0.3, -1), up, green);
                const double r = 0.05;
                for (int k = 0; k < 5; k++)
                {
                    double a0 = k * Math.PI * 2 / 5, a1 = a0 + Math.PI * 2 / 5;
                    pet.TriN(V(x, h, z), V(x + Math.Cos(a0) * r, h + 0.01, z + Math.Sin(a0) * r), V(x + Math.Cos(a1) * r, h + 0.01, z + Math.Sin(a1) * r), up, up, up, C(1, 1, 1));
                }
                pet.TriN(V(x, h + 0.016, z), V(x + 0.016, h + 0.013, z), V(x, h + 0.013, z + 0.016), up, up, up, C(1.0f, 0.86f, 0.35f));
            }
            // 茎 aTint = 0，花瓣 = 1
            for (int i = 0; i < stem.c.Count; i++) { var c = stem.c[i]; c.a = 0; stem.c[i] = c; }
            for (int i = 0; i < pet.c.Count; i++) { var c = pet.c[i]; c.a = 1; pet.c[i] = c; }
            int o = stem.v.Count;
            stem.v.AddRange(pet.v); stem.n.AddRange(pet.n); stem.c.AddRange(pet.c); stem.uv.AddRange(pet.uv);
            foreach (var i in pet.t) stem.t.Add(o + i);
            return stem.ToMesh("clash flower");
        }
        // 草丛实例矩阵（three：compose(pos, Euler(0, yaw, 0), (s, s·k, s))）→ 根下 Unity 局部矩阵（three 的绕 Y 转 θ = Unity 绕 Y 转 −θ）
        static Matrix4x4 InstU(double x, double y, double z, double s, double k, double yaw)
        {
            return Matrix4x4.TRS(P(x, y, z), Quaternion.AngleAxis((float)(-yaw * 180 / Math.PI), Vector3.up), V(s, s * k, s));
        }
        static Vector4 Col4(Color c) { return new Vector4(c.r, c.g, c.b, 1); }

        // 三角形索引网格（three 坐标的顶点、按 three computeVertexNormals 求平滑法线）→ Unity 网格（z 取反、绕序反转）
        static Mesh IndexedToUnity(double[] pos, Color[] col, Vector4[] uv2, List<int> idx, string name)
        {
            int nv = pos.Length / 3;
            var nr = new double[nv * 3];
            for (int q = 0; q < idx.Count; q += 3)
            {
                int a = idx[q] * 3, b = idx[q + 1] * 3, c = idx[q + 2] * 3;
                // three：cb = C − B，ab = A − B，n = cb × ab
                double cbx = pos[c] - pos[b], cby = pos[c + 1] - pos[b + 1], cbz = pos[c + 2] - pos[b + 2];
                double abx = pos[a] - pos[b], aby = pos[a + 1] - pos[b + 1], abz = pos[a + 2] - pos[b + 2];
                double nx = cby * abz - cbz * aby, ny = cbz * abx - cbx * abz, nz = cbx * aby - cby * abx;
                nr[a] += nx; nr[a + 1] += ny; nr[a + 2] += nz;
                nr[b] += nx; nr[b + 1] += ny; nr[b + 2] += nz;
                nr[c] += nx; nr[c + 1] += ny; nr[c + 2] += nz;
            }
            var v = new Vector3[nv]; var n = new Vector3[nv];
            for (int i = 0; i < nv; i++)
            {
                v[i] = P(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
                double x = nr[i * 3], y = nr[i * 3 + 1], z = nr[i * 3 + 2], l = Math.Sqrt(x * x + y * y + z * z);
                if (l > 0) { x /= l; y /= l; z /= l; } else y = 1;
                n[i] = V(x, y, -z);
            }
            var t = new int[idx.Count];
            for (int q = 0; q < idx.Count; q += 3) { t[q] = idx[q]; t[q + 1] = idx[q + 2]; t[q + 2] = idx[q + 1]; }
            var m = new Mesh { name = name };
            if (nv > 65535) m.indexFormat = IndexFormat.UInt32;
            m.vertices = v; m.normals = n; m.colors = col;
            if (uv2 != null) m.SetUVs(1, new List<Vector4>(uv2));
            m.triangles = t;
            m.RecalculateBounds();
            return m;
        }

        // 生成器：每做完一段 yield 一次，由场景分帧执行（= JS buildTerrain）。detail：先生成地形细节纹理（Art.TerrainDetailGen，嵌套）
        public static IEnumerator Build(ClashTerrainOut O, string kind, Height H, ClashLook look, SeededRandom rnd, Color defColor, Color colA, Color colD)
        {
            bool coast = look.coast;
            bool walled = Walled(kind), hilly = kind == "hill" || kind == "mountain";
            // 离海岸线的距离（非登陆战为无穷大）：树、石、房屋不放进海里
            Func<double, double, double> dry = (x, z) => coast ? x - ShoreX(z) : 1e9;
            yield return Art.TerrainDetailGen();
            O.snowH = look.snow > 0 ? (float)((kind == "mountain" ? 13 : kind == "hill" ? 17 : 99) / Math.Max(0.5, look.snow)) : 99;
            // ---- 地面：近处 0.5 的平滑网格，向外渐疏（最疏 2.6），向后延伸成远山；看不到的两侧不建三角形
            var xs = Axis(-150, 150, 0.5, 12, 0.16, 2.6);
            var zs = new List<double>();
            for (double z = 64; z > -126;) { zs.Add(z); z -= z > 16 ? 0.5 + (z - 16) * 0.3 : z > -9 ? 0.5 : Math.Min(2.6, 0.5 + (-9 - z) * 0.09); }
            int NX = xs.Count, NZ = zs.Count;
            var pos = new double[NX * NZ * 3]; var colA2 = new Color[NX * NZ]; var gnd = new Vector4[NX * NZ];
            Func<double, double, double> nA = (x, z) => Perlin(x * 0.17 + 11.3, z * 0.17 + 4.7) - 0.465, nF = (x, z) => Perlin(x * 0.09 + 3.1, z * 0.13 + 9.7) - 0.465;
            for (int j = 0; j < NZ; j++)
            {
                if (j % 12 == 11) yield return null;
                double z = zs[j];
                for (int i = 0; i < NX; i++)
                {
                    double x = xs[i], y = H.H(x, z); int k = j * NX + i;
                    pos[k * 3] = x; pos[k * 3 + 1] = y; pos[k * 3 + 2] = z;
                    Color c; double g0, g1, g2, g3;
                    GroundAttr(kind, x, z, y, nA(x, z), nF(x, z), look, out c, out g0, out g1, out g2, out g3);
                    colA2[k] = new Color(c.r, c.g, c.b, 1);
                    gnd[k] = new Vector4((float)g0, (float)g1, (float)g2, (float)g3);
                }
            }
            Func<double, double> lim = z => Math.Max(15, 0.8 * (28 - z) + 6);
            var idx = new List<int>();
            for (int j = 0; j < NZ - 1; j++)
            {
                double Lm = lim(zs[j + 1]);
                for (int i = 0; i < NX - 1; i++)
                {
                    if (Math.Min(Math.Abs(xs[i]), Math.Abs(xs[i + 1])) > Lm) continue;
                    int a = j * NX + i, b = a + 1, c = a + NX, d = c + 1;
                    // zs 由近到远（z 递减）：a(x0,z0) b(x1,z0) c(x0,z1) d(x1,z1)，朝上的绕向（three）
                    if (((i + j) & 1) == 1) { idx.Add(a); idx.Add(b); idx.Add(c); idx.Add(b); idx.Add(d); idx.Add(c); }
                    else { idx.Add(a); idx.Add(b); idx.Add(d); idx.Add(a); idx.Add(d); idx.Add(c); }
                }
            }
            yield return null;
            O.ground = IndexedToUnity(pos, colA2, gnd, idx, "clash ground");
            O.groundTris = idx.Count / 3;
            yield return null;
            // ---- 道具：石块、树、村舍、城墙
            MeshBuilder[] B = PropBuilders(), BF = PropBuilders();     // BF：远处（阴影相机之外）的树与村舍，不投影
            var stone = C(0.6f, 0.59f, 0.56f);
            int rocks = hilly ? 26 : kind == "river" ? 20 : walled ? 6 : 9;
            for (int k = 0; k < rocks; k++)
            {
                double x = (rnd.NextDouble() * 2 - 1) * 16, z = 6 - rnd.NextDouble() * 22;
                if (kind == "river") x = RIVER_X + (rnd.NextDouble() < 0.5 ? -1 : 1) * (RIVER_W + 0.4 + rnd.NextDouble() * 1.6);
                if (Math.Abs(x) < 6 && Math.Abs(z - ZCd) < 3.5) continue;
                if (z > 1.8 && Math.Abs(x) < 9) continue;          // 镜头前不放大石头
                if (walled && z < WALL_Z + 0.9) continue;
                if (dry(x, z) < 1.2) continue;
                double r = 0.16 + rnd.NextDouble() * (kind == "mountain" ? 0.6 : 0.36);
                RockInto(B, x, H.H(x, z) + r * 0.12, z, r, Shade(stone, (rnd.NextDouble() - 0.5) * 0.14), k + 3);
                if (rnd.NextDouble() < 0.5) RockInto(B, x + r * 1.3, H.H(x + r * 1.3, z + r * 0.4), z + r * 0.4, r * 0.45, Shade(stone, -0.04), k + 31);
            }
            // 近景（镜头前 z 3–6.5）的战场遗物：折断的长枪、丢下的圆盾、倒地的军旗、碎石——框住画面下缘，不挡交锋处
            {
                var W = B[K_PLAIN]; var metal = C(0.62f, 0.62f, 0.64f); var cols = new[] { colA, colD };
                Action<double, double, double, double, double, Color, bool> lie = (x, z, len, ang, r, col, tip) =>
                {
                    double ca = Math.Cos(ang) * len / 2, sa = Math.Sin(ang) * len / 2;
                    Vector3 a = P(x - ca, H.H(x - ca, z - sa) + r, z - sa), b = P(x + ca, H.H(x + ca, z + sa) + r + 0.02, z + sa);
                    W.Tube(new[] { a, b }, new[] { (float)r, (float)(r * 0.9) }, 5, col, false);
                    if (tip) W.Tube(new[] { b, P(x + ca * 1.12, H.H(x + ca, z + sa) + r + 0.02, z + sa * 1.12) }, new[] { (float)(r * 1.3), 0.003f }, 5, metal, false);
                };
                for (int k = 0; k < 14; k++)
                {
                    double x = (rnd.NextDouble() * 2 - 1) * 12, z = 3.2 + rnd.NextDouble() * 3.4;
                    if (dry(x, z) < 1) continue;
                    double u = rnd.NextDouble();
                    if (u < 0.28) { bool br = rnd.NextDouble() < 0.5; double len = (br ? 0.45 : 0.9) + rnd.NextDouble() * 0.35, ang = rnd.NextDouble() * 3.14; lie(x, z, len, ang, 0.014, Shade(WOOD, 0.1), !br); }
                    else if (u < 0.5)
                    {
                        double y = H.H(x, z); var c = cols[k % cols.Length];
                        // 斜倚在地上的圆盾（MeshBuilder.M：先绕水平轴倾斜再平移）
                        var pu = P(x, y + 0.08, z);
                        double ex = 0.5 + rnd.NextDouble() * 0.5, ey = rnd.NextDouble() * 6.28;
                        W.M = Matrix4x4.Translate(pu) * Matrix4x4.Rotate(ClashUtil.YXZ((float)ex, (float)ey, 0));
                        W.Lathe(V(0, 0, 0), new[] { L(0.21, -0.012, C(0.3f, 0.24f, 0.18f), true), L(0.2, 0.02, true), L(0.06, 0.04), L(0.035, 0.06, metal), L(0, 0.065, metal) }, 14, Shade(c, -0.15));
                        W.M = Matrix4x4.identity;
                    }
                    else if (u < 0.6)
                    {
                        // 倒地的小旗：旗杆 + 平摊在地上的旗面
                        double ang = rnd.NextDouble() * 3.14; var c = cols[k % cols.Length];
                        lie(x, z, 1.3, ang, 0.016, Shade(WOOD, -0.05), false);
                        double ca = Math.Cos(ang), sa = Math.Sin(ang), y = H.H(x, z) + 0.035, nx = -sa, nz = ca;
                        Func<double, double, Vector3> q = (a, b) => P(x + ca * a + nx * b, y + 0.012 * Math.Sin(a * 5 + b * 3), z + sa * a + nz * b);
                        var up = V(0, 1, 0);
                        for (int i = 0; i < 3; i++)
                        {
                            double a0 = 0.1 + i * 0.16, a1 = a0 + 0.16;
                            var cc = Shade(c, i % 2 == 1 ? -0.06 : 0.02);
                            W.TriN(q(a0, 0), q(a1, 0), q(a1, 0.36), up, up, up, cc); W.TriN(q(a0, 0), q(a1, 0.36), q(a0, 0.36), up, up, up, cc);
                        }
                    }
                    else RockInto(B, x, H.H(x, z) + 0.02, z, 0.1 + rnd.NextDouble() * 0.14, Shade(stone, (rnd.NextDouble() - 0.5) * 0.12), 70 + k);
                }
            }
            yield return null;
            Func<Color> leafC = () => Shade(kind == "forest" ? look.fleaf : look.leaf, (rnd.NextDouble() - 0.5) * 0.16);
            Func<string> pickT = () => look.PickTree(rnd);
            // 近景框边的树：在本群落的树种里挑「大冠」的一种（温带仍是松 / 阔叶）
            Func<string> nearT = () => { var t = pickT(); return t == "scrub" ? "broad" : t == "poplar" ? "broad" : t; };
            Action<double, double, double, string, bool> tree = (x, z, s, type, near) =>
            {
                if (dry(x, z) < 1.5) return;
                // 网页版：缺省树种时按群落的第一种树是否为棕榈放大（不是按随后挑出的树种）
                double sc = s * (type == "palm" || (type == null && look.FirstTree == "palm") ? 1.25 : 1);
                string ty = type ?? pickT();
                var lc = leafC();
                TreeInto(z < -14 ? BF : B, x, H.H(x, z) - 0.05, z, sc, ty, lc, Math.Abs(JsRound(x * 13 + z * 7)) % 97, near);
            };
            if (kind == "forest")
            {
                int nF0 = JsRound(110 * Math.Min(1, 0.55 + look.treeK * 0.45));
                for (int k = 0; k < nF0; k++)
                {
                    if (k % 20 == 19) yield return null;
                    double x = (rnd.NextDouble() * 2 - 1) * 26, z = -4.4 - rnd.NextDouble() * 26;
                    tree(x, z, 2.2 + rnd.NextDouble() * 1.6, null, false);
                }
                // 两侧与近景的树（框住画面）
                foreach (var f in new[] { new[] { -9.5, 3.5, 3.4 }, new[] { 10.2, 2.8, 3.1 }, new[] { -12.5, 0.5, 3.6 }, new[] { 12.8, -1.2, 3.8 }, new[] { -7.6, -3.6, 2.8 }, new[] { 8.4, -4.0, 3.0 }, new[] { -10.6, 6.4, 3.2 }, new[] { 11.4, 6.0, 3.3 } })
                {
                    string ty = look.biome == "temperate" ? (rnd.NextDouble() < 0.4 ? "pine" : "broad") : nearT();
                    tree(f[0], f[1], f[2], ty, true);
                }
                // 林下灌丛
                for (int k = 0; k < 24; k++)
                {
                    double x = (rnd.NextDouble() * 2 - 1) * 14, z = 4 - rnd.NextDouble() * 12;
                    if (Math.Abs(z - ZCd) < 3.4 && Math.Abs(x) < 7.5) continue;
                    if (dry(x, z) < 1.5) continue;
                    B[K_LEAF].Sphere(P(x, H.H(x, z) + 0.05, z), V(0.36, 0.24, 0.32), Shade(look.fleaf, 0.03 + (rnd.NextDouble() - 0.5) * 0.15), 2, SO(seed: k, rough: 0.14f, bottomShade: 0.35f, flat: -0.4f, smoothN: 0.5f));
                }
            }
            else if (!walled)
            {
                int n = JsRound((kind == "river" ? 30 : 24) * look.treeK);
                for (int k = 0; k < n; k++)
                {
                    double x = (rnd.NextDouble() * 2 - 1) * 30, z = -9 - rnd.NextDouble() * 24;
                    if (kind == "river" && Math.Abs(x - RIVER_X) < 4) continue;
                    if (Math.Abs(x) < 9 && z > -16) continue;          // 不在两军交锋处的正后方挡住画面
                    tree(x, z, 2 + rnd.NextDouble() * 1.4, null, false);
                }
                foreach (var f in new[] { new[] { -12.8, -2.5, 2.8 }, new[] { 13.6, -3.4, 3.0 } }) { string ty = look.biome == "temperate" ? "broad" : nearT(); tree(f[0], f[1], f[2], ty, true); }
                // 田间村舍（平原、河川）：按防守城池的文化（灰瓦、白墙赤陶瓦、平顶土坯、毡帐、长屋、圆屋、干栏、茅屋）
                if (kind == "plain" || kind == "river")
                {
                    for (int k = 0; k < 6; k++)
                    {
                        double x = -22 + k * 8.5 + (rnd.NextDouble() - 0.5) * 4, z = -19 - rnd.NextDouble() * 9;
                        if (kind == "river" && Math.Abs(x - RIVER_X) < 6) continue;
                        if (dry(x, z) < 3) continue;
                        HouseInto(BF, look.house, x, H.H(x, z), z, rnd, 1);
                        string ty = look.house == "flat" ? "palm" : look.biome == "temperate" ? "broad" : nearT();
                        tree(x + 1.9, z - 0.8, 1.8, ty, false);
                    }
                }
            }
            else
            {
                // 城外两侧的树
                foreach (var f in new[] { new[] { -12.6, -2.2, 2.9 }, new[] { -10.4, -5.6, 2.5 }, new[] { 14.4, -3.6, 2.7 } }) { string ty = look.biome == "temperate" ? "broad" : nearT(); tree(f[0], f[1], f[2], ty, true); }
            }
            // 登陆：海滩上搁浅的小船（攻方身后）
            if (coast)
            {
                foreach (var b in new[] { new[] { -4.2, 1.0, 0.5 }, new[] { -9.5, 1.15, 0.25 }, new[] { 2.8, 0.9, 0.7 } })
                {
                    double x = ShoreX(b[0]) - 0.2;
                    BoatInto(B, x, Math.Max(SEA_Y - 0.12, H.H(x, b[0])) - 0.06, b[0], b[1], b[2]);
                }
            }
            yield return null;
            // 城墙与城门
            if (walled) { BuildWall(B, kind, rnd, defColor, H, look); yield return null; }
            if (B.Any(mb => mb.v.Count > 0)) O.propsNear = MergeProps(B, "clash props");
            if (BF.Any(mb => mb.v.Count > 0)) O.propsFar = MergeProps(BF, "clash props far");
            yield return null;
            // ---- 草丛 / 芦苇 / 野花（实例化，随风轻摆；不投影）
            int nT = walled ? 150 : kind == "river" ? 380 : kind == "forest" ? 420 : 480;
            var gcol = look.tuft.HasValue ? (kind == "forest" ? Shade(look.tuft.Value, -0.08) : look.tuft.Value) : kind == "forest" ? C(0.3f, 0.48f, 0.24f) : hilly ? C(0.5f, 0.6f, 0.3f) : C(0.4f, 0.6f, 0.28f);
            for (int k = 0; k < nT; k++)
            {
                double x = (rnd.NextDouble() * 2 - 1) * 15, z = 8 - rnd.NextDouble() * (walled ? 14 : 22);
                if (dry(x, z) < 2.4) continue;
                if (walled && z < WALL_Z + 0.8) continue;
                if (kind == "river" && Math.Abs(x - RIVER_X) < RIVER_W + 0.5) continue;
                if (walled && kind != "wall" && Math.Abs(x - GATE_X) < 1.6 + Math.Max(0, z - WALL_Z) * 0.07) continue;
                double tr = Math.Exp(-(x * x) / 12 - ((z - ZCd) * (z - ZCd)) / 8);
                if (rnd.NextDouble() < tr * 0.75) continue;
                if (z > 3.5 && rnd.NextDouble() < 0.45) continue;    // 镜头前稀一些
                double s = (0.75 + rnd.NextDouble() * 0.6) * (z > 3.5 ? 0.8 : 1), yaw = rnd.NextDouble() * 6.28;
                var c = Shade(gcol, (rnd.NextDouble() - 0.5) * 0.24);
                O.tufts.Add(InstU(x, H.H(x, z) - 0.02, z, s, 1, yaw)); O.tuftCols.Add(Col4(c));
            }
            if (kind == "river")
            {
                for (int k = 0; k < 110; k++)
                {
                    int sk = rnd.NextDouble() < 0.5 ? -1 : 1;
                    double x = RIVER_X + sk * (RIVER_W + 0.7 + rnd.NextDouble() * 1.3), z = 6 - rnd.NextDouble() * 22;
                    if (Math.Abs(z - ZCd) < 3.6 && Math.Abs(x) < 5.5) continue;
                    if (z > 2.2) continue;
                    double s = 1.0 + rnd.NextDouble() * 0.5, kk = 2.4 + rnd.NextDouble(), yaw = rnd.NextDouble() * 6.28;
                    var c = Shade(C(0.62f, 0.62f, 0.34f), (rnd.NextDouble() - 0.5) * 0.2);
                    O.tufts.Add(InstU(x, H.H(x, z) - 0.02, z, s, kk, yaw)); O.tuftCols.Add(Col4(c));
                }
            }
            if (O.tufts.Count > 0) O.tuftMesh = TuftMesh();
            if (!walled)
            {
                var fl = new[] { C(0.98f, 0.95f, 0.86f), C(1, 0.82f, 0.3f), C(0.95f, 0.55f, 0.62f), C(0.72f, 0.6f, 0.95f) };
                int nFl = JsRound((kind == "forest" ? 40 : 70) * look.flowers);
                for (int k = 0; k < nFl; k++)
                {
                    double x = (rnd.NextDouble() * 2 - 1) * 14, z = 3 - rnd.NextDouble() * 16;
                    if (dry(x, z) < 3) continue;
                    if (kind == "river" && Math.Abs(x - RIVER_X) < RIVER_W + RIVER_BANK) continue;
                    if (Math.Abs(x) < 6 && Math.Abs(z - ZCd) < 3) continue;
                    double s = 0.8 + rnd.NextDouble() * 0.5, yaw = rnd.NextDouble() * 6.28;
                    var c = fl[rnd.Next(fl.Length)];
                    O.flowers.Add(InstU(x, H.H(x, z) - 0.01, z, s, 1, yaw)); O.flowerCols.Add(Col4(c));
                }
                if (O.flowers.Count > 0) O.flowerMesh = FlowerMesh();
            }
            yield return null;
            // 河水（顶点色 r = 离河心的远近 → 浅色与浪花）
            if (kind == "river")
            {
                var wm = new MeshBuilder();
                double x0 = RIVER_X - RIVER_W - RIVER_BANK - 0.2, x1 = RIVER_X + RIVER_W + RIVER_BANK + 0.2;
                const int nx = 10;
                var zz = new List<double>();
                for (double z = 64; z > -95;) { zz.Add(z); z -= z > 16 ? 1.2 + (z - 16) * 0.3 : z > -10 ? 1.2 : 1.2 + (-10 - z) * 0.25; }
                for (int j = 0; j < zz.Count - 1; j++)
                    for (int i = 0; i < nx; i++)
                    {
                        double xa = Lerp(x0, x1, i / (double)nx), xb = Lerp(x0, x1, (i + 1) / (double)nx);
                        double sa = Clamp01(Math.Abs(xa - RIVER_X) / (RIVER_W + RIVER_BANK));
                        var ka = C((float)(0.25 + sa * 0.6), 0, 0);
                        double y = RIVER_SURF;
                        Vector3 a = P(xa, y, zz[j]), b = P(xa, y, zz[j + 1]), c = P(xb, y, zz[j + 1]), d = P(xb, y, zz[j]);
                        wm.Tri(a, b, c, ka); wm.Tri(a, c, d, ka);
                    }
                O.river = wm.ToMesh("clash river");
            }
            // 海（登陆战）：海岸线以外的水面，岸边浅（顶点色 r → 浪花），远海不做顶点起伏（g = 1）
            if (coast)
            {
                var wm = new MeshBuilder();
                var zz = new List<double>();
                for (double z = 64; z > -126;) { zz.Add(z); z -= z > 16 ? 1.2 + (z - 16) * 0.3 : z > -10 ? 0.9 : 0.9 + (-10 - z) * 0.2; }
                var D = new[] { -160, -70, -34, -18, -10, -6, -3.6, -2.2, -1.2, -0.5, 0 };
                Func<double, double, Vector3> at = (z, d) => P(ShoreX(z) + 0.9 + d, SEA_Y, z);
                Func<double, Color> col = d => C((float)Math.Pow(Clamp01(1 + d / 7), 1.6), d < -30 ? 1 : 0, 0);
                var up = V(0, 1, 0);
                for (int j = 0; j < zz.Count - 1; j++)
                    for (int i = 0; i < D.Length - 1; i++)
                    {
                        Vector3 a = at(zz[j], D[i]), b = at(zz[j + 1], D[i]), c = at(zz[j + 1], D[i + 1]), d = at(zz[j], D[i + 1]);
                        wm.TriN(a, b, c, up, up, up, col(D[i]), col(D[i]), col(D[i + 1]));
                        wm.TriN(a, c, d, up, up, up, col(D[i]), col(D[i + 1]), col(D[i + 1]));
                    }
                O.sea = wm.ToMesh("clash sea");
            }
        }

        // 城墙：按防守城池的文化（look.wall）——
        //   han：砖石墙身（错缝砖纹）、圆润的垛口、角楼与曲面屋顶；城门为半圆拱门（拱券石、包铁门扇与门钉）与城门楼
        //   roman：浅色方石墙、方垛口、方塔赤陶瓦四坡顶，城门两侧方塔（拱窗）；城内白墙赤陶瓦房
        //   mud：土坯墙、阶梯形垛口、圆塔，城门两侧圆塔；城内平顶土坯房与圆顶
        //   brick：红砖墙、圆头垛口、方塔顶上圆顶亭；城内茅屋与窣堵波
        //   palisade：土垒上的削尖木栅、木望楼（茅草攒尖顶）、木门；城内长屋 / 圆屋 / 毡帐 / 干栏
        static readonly Dictionary<string, Color> WALL_STONE = new Dictionary<string, Color> { { "han", C(0.7f, 0.67f, 0.6f) }, { "roman", C(0.8f, 0.76f, 0.67f) }, { "mud", C(0.8f, 0.65f, 0.46f) }, { "brick", C(0.7f, 0.42f, 0.32f) } };
        static void BuildWall(MeshBuilder[] B, string kind, SeededRandom rnd, Color defColor, Height H, ClashLook look)
        {
            string st = look != null && look.wall != null ? look.wall : "han";
            if (st == "palisade") { BuildPalisade(B, kind, rnd, defColor, H, look); return; }
            const double Z = WALL_Z, TH = 1.4, WH = 3.3, X0 = -0.6, X1 = 44;
            Color stone; if (!WALL_STONE.TryGetValue(st, out stone)) stone = WALL_STONE["han"];
            double zc = Z - TH / 2;
            double? gx = kind == "wall" ? (double?)null : GATE_X;
            MeshBuilder S = B[K_BRICK], W = B[K_PLAIN], R = B[K_ROOF];
            bool castle = kind == "castle";
            // 墙基（略外扩）与墙身；城门两侧分段，门洞上方另砌
            Action<double, double> seg = (a, b) =>
            {
                if (b - a < 0.05) return;
                S.Box(P((a + b) / 2, 0.2, zc + 0.06), V(b - a, 0.5, TH + 0.3), Shade(stone, -0.1));
                S.Box(P((a + b) / 2, WH / 2, zc), V(b - a, WH, TH), stone);
            };
            if (gx == null) seg(X0, X1); else { seg(X0, gx.Value - 1.8); seg(gx.Value + 1.8, X1); }
            // 垛口与女墙
            Action<double, double, double, double> merlon = (x, y, z, k) =>
            {
                var mc = Shade(stone, k);
                if (st == "roman") S.Box(P(x, y + 0.26, z), V(0.46, 0.5, 0.3), mc);
                else if (st == "mud") { S.Box(P(x, y + 0.14, z), V(0.5, 0.26, 0.3), mc); S.Box(P(x, y + 0.36, z), V(0.28, 0.2, 0.3), mc); S.Lathe(P(x, y + 0.46, z), new[] { L(0.13, 0), L(0.08, 0.06), L(0, 0.09) }, 8, mc); }
                else if (st == "brick") { S.Box(P(x, y + 0.22, z), V(0.42, 0.4, 0.3), mc); S.Lathe(P(x, y + 0.42, z), new[] { L(0.21, 0), L(0.16, 0.1), L(0, 0.17) }, 8, Shade(mc, 0.03)); }
                else { S.Box(P(x, y + 0.21, z), V(0.46, 0.34, 0.3), mc); S.Lathe(P(x, y + 0.38, z), new[] { L(0.2, 0), L(0.12, 0.08), L(0, 0.11) }, 8, Shade(mc, 0.02)); }
            };
            S.Box(P((X0 + X1) / 2, WH + 0.04, Z - 0.12), V(X1 - X0, 0.08, 0.3), Shade(stone, -0.06));
            for (double x = X0 + 0.3; x < X1; x += st == "roman" ? 0.85 : 0.8)
            {
                if (gx != null && Math.Abs(x - gx.Value) < (st == "han" ? 1.9 : 3.0)) continue;
                merlon(x, WH + 0.04, Z - 0.15, 0.04 + (rnd.NextDouble() - 0.5) * 0.06);
            }
            // 角楼（左端）
            Action<double, double, double, double> towerSq = (x, z, w, h) =>
            {
                S.Box(P(x, h / 2, z), V(w, h, w), Shade(stone, -0.05));
                for (int i = 0; i < 3; i++) foreach (int sz in new[] { -1, 1 }) merlon(x - w * 0.36 + i * w * 0.36, h, z + sz * (w / 2 - 0.15), 0.06);
                foreach (int sx in new[] { -1, 1 }) merlon(x + sx * (w / 2 - 0.15), h, z, 0.06);
            };
            Action<double, double, double, double> towerRound = (x, z, r, h) =>
            {
                S.Lathe(P(x, 0, z), new[] { L(r * 1.1, 0), L(r, h - 0.3, true), L(r * 1.1, h - 0.22, true), L(r * 1.1, h) }, 18, Shade(stone, -0.04), true);
                for (int k = 0; k < 9; k++) { double a = k / 9.0 * Math.PI * 2; merlon(x + Math.Cos(a) * r * 0.9, h, z + Math.Sin(a) * r * 0.9, 0.05); }
            };
            Action<double, double, double, double> chhatri = (x, y, z, r) =>
            {
                var c = C(0.86f, 0.8f, 0.68f);
                W.Box(P(x, y + 0.06, z), V(r * 2.2, 0.12, r * 2.2), Shade(stone, 0.05));
                foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) W.Lathe(P(x + sx * r * 0.8, y + 0.12, z + sz * r * 0.8), new[] { L(0.05, 0), L(0.05, 0.55) }, 6, c);
                W.Box(P(x, y + 0.7, z), V(r * 2.3, 0.1, r * 2.3), Shade(c, -0.05));
                DomeInto(W, x, y + 0.75, z, r * 0.95, c, 12);
                W.Tube(new[] { P(x, y + 0.75 + r * 1.0, z), P(x, y + 0.95 + r * 1.2, z) }, new[] { 0.03f, 0.01f }, 5, GOLD, false);
            };
            if (st == "roman") { towerSq(X0, zc, 2.1, 4.9); PyramidInto(R, X0, 5.3, zc, 2.0, 2.0, 0.85, TERRA); W.Box(P(X0, 3.7, zc + 1.06), V(0.34, 0.6, 0.04), C(0.16f, 0.13f, 0.11f)); }
            else if (st == "mud") towerRound(X0, zc, 1.15, 4.7);
            else if (st == "brick") { towerSq(X0, zc, 2.0, 4.6); chhatri(X0, 4.6, zc, 0.7); }
            else
            {
                S.Box(P(X0, 2.05, zc), V(1.9, 4.1, 1.9), Shade(stone, -0.05));
                W.Box(P(X0, 4.45, zc), V(1.5, 0.7, 1.5), C(0.62f, 0.2f, 0.16f));
                foreach (var dx in new[] { -0.62, 0.62 }) foreach (var dz in new[] { -0.62, 0.62 }) W.Lathe(P(X0 + dx, 4.1, zc + dz), new[] { L(0.07, 0), L(0.07, 0.72) }, 8, C(0.5f, 0.16f, 0.12f));
                R.ChineseRoof(P(X0, 4.8, zc), 2.5f, 2.5f, 0.9f, C(0.24f, 0.28f, 0.36f));
            }
            // 城内屋顶（下半截在墙后看不见，只露出屋顶）
            if (st == "han")
            {
                for (int k = 0; k < 7; k++)
                {
                    double x = 2 + k * 3.4 + rnd.NextDouble() * 1.2, z = Z - 3.5 - rnd.NextDouble() * 5;
                    W.Box(P(x, 1.8, z), V(2.2, 3.6, 1.6), C(0.86f, 0.8f, 0.68f));
                    R.ChineseRoof(P(x, 3.6, z), 3.0f, 2.2f, (float)(1.0 + rnd.NextDouble() * 0.4), C(0.27f, 0.3f, 0.37f));
                }
            }
            else
            {
                string hs = look != null && look.house != null ? look.house : "flat";
                for (int k = 0; k < 7; k++)
                {
                    double x = 2 + k * 3.4 + rnd.NextDouble() * 1.2, z = Z - 3.5 - rnd.NextDouble() * 5;
                    if (k == 3)
                    {
                        // 城中的地标：罗马神庙山花 / 安息与阿拉伯的大圆顶 / 天竺的窣堵波
                        if (st == "roman")
                        {
                            const double y0 = 2.2;
                            W.Box(P(x, y0 + 0.9, z), V(3.4, 1.8, 2.4), WHITE);
                            for (int i = 0; i < 6; i++) W.Lathe(P(x - 1.5 + i * 0.6, y0, z + 1.3), new[] { L(0.12, 0), L(0.1, 1.8) }, 8, Shade(WHITE, 0.04));
                            GableInto(R, W, x, y0 + 1.8, z + 0.2, 3.8, 3.2, 0.7, TERRA, WHITE, true);
                        }
                        else if (st == "mud")
                        {
                            W.Lathe(P(x, 2.4, z), new[] { L(1.2, 0), L(1.2, 1.2) }, 16, Shade(MUD, 0.05));
                            DomeInto(W, x, 3.6, z, 1.2, look != null && look.culture == "persia" ? C(0.36f, 0.62f, 0.66f) : C(0.9f, 0.86f, 0.78f), 18);
                        }
                        else StupaInto(B, x, 1.6, z, 1.2);
                        continue;
                    }
                    HouseInto(B, hs, x, hs == "yurt" || hs == "round" ? 1.9 : 2.1, z, rnd, 1.45);
                }
            }
            // 墙根碎石
            for (int k = 0; k < 9; k++)
            {
                double x = X0 + 1.2 + rnd.NextDouble() * 16;
                if (gx != null && Math.Abs(x - gx.Value) < 2.4) continue;
                double r = 0.12 + rnd.NextDouble() * 0.16;
                double y = H.H(x, Z + 0.35) + r * 0.1, zz = Z + 0.3 + rnd.NextDouble() * 0.4;
                RockInto(B, x, y, zz, r, Shade(stone, -0.12 + (rnd.NextDouble() - 0.5) * 0.1), k + 11);
            }
            WallDressing(W, gx, rnd, defColor, H, WH, st == "han" ? 1.9 : 3.0);
            if (gx == null) return;
            double g = gx.Value;
            // 城门台：门洞两侧的墩台、拱顶以上的墙体
            const double ar = 0.82, ah = 1.75; double fz = Z + 0.38, bz = zc + 0.12, depth = TH + 0.5;
            S.Box(P(g - 1.31, 1.85, bz), V(0.98, 3.7, depth), Shade(stone, -0.04));
            S.Box(P(g + 1.31, 1.85, bz), V(0.98, 3.7, depth), Shade(stone, -0.04));
            S.Box(P(g, (ah + ar + 3.7) / 2, bz), V(1.64, 3.7 - ah - ar, depth), Shade(stone, -0.04));
            // 拱腹（半圆拱，平滑法线朝内，越深越暗）与两侧门洞壁
            const int N = 14; var dark = C(0.16f, 0.13f, 0.11f); var ins = Shade(stone, -0.35); var front = V(0, 0, -1); double ri = ar - 0.01;   // front：Unity −z = three +z（朝镜头）
            for (int i = 0; i < N; i++)
            {
                double a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N;
                Vector3 n0 = V(-Math.Cos(a0), -Math.Sin(a0), 0), n1 = V(-Math.Cos(a1), -Math.Sin(a1), 0);
                Vector3 A0 = P(g + Math.Cos(a0) * ri, ah + Math.Sin(a0) * ri, fz), A1 = P(g + Math.Cos(a1) * ri, ah + Math.Sin(a1) * ri, fz);
                Vector3 B0 = P(g + Math.Cos(a0) * ri, ah + Math.Sin(a0) * ri, fz - depth + 0.2), B1 = P(g + Math.Cos(a1) * ri, ah + Math.Sin(a1) * ri, fz - depth + 0.2);
                W.TriN(A0, B0, A1, n0, n0, n1, ins, dark, ins);
                W.TriN(A1, B0, B1, n1, n0, n1, ins, dark, dark);
            }
            foreach (int sx in new[] { -1, 1 })
            {
                var n = V(-sx, 0, 0);
                W.TriN(P(g + sx * ri, 0, fz), P(g + sx * ri, ah, fz), P(g + sx * ri, ah, fz - depth + 0.2), n, n, n, ins, ins, dark);
                W.TriN(P(g + sx * ri, 0, fz), P(g + sx * ri, ah, fz - depth + 0.2), P(g + sx * ri, 0, fz - depth + 0.2), n, n, n, ins, dark, dark);
            }
            // 拱肩：拱线与门台正面矩形（宽 2·ar、高至 ah + ar）之间的扇形填充
            for (int i = 0; i < N; i++)
            {
                double a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N;
                double cx = i < N / 2.0 ? ar : -ar;
                S.TriN(P(g + Math.Cos(a0) * ar, ah + Math.Sin(a0) * ar, fz), P(g + cx, ah + ar, fz), P(g + Math.Cos(a1) * ar, ah + Math.Sin(a1) * ar, fz), front, front, front, Shade(stone, -0.04));
            }
            // 拱券石（浅色半环，略凸出墙面）
            for (int i = 0; i < N; i++)
            {
                double a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N, r0 = ar, r1 = ar + 0.22;
                Func<double, double, Vector3> q = (a, r) => P(g + Math.Cos(a) * r, ah + Math.Sin(a) * r, fz + 0.04);
                var kc = Shade(stone, 0.1 - (i % 2) * 0.05);
                S.TriN(q(a0, r0), q(a0, r1), q(a1, r1), front, front, front, kc);
                S.TriN(q(a0, r0), q(a1, r1), q(a1, r0), front, front, front, kc);
            }
            // 门扇（深红木门 + 铜门钉；非中式为原木色包铁），退进门洞
            double dzd = fz - 0.35; var doorC = st == "han" ? C(0.42f, 0.2f, 0.11f) : C(0.4f, 0.28f, 0.17f);
            foreach (int sx in new[] { -1, 1 })
            {
                W.Box(P(g + sx * 0.4, ah * 0.5, dzd), V(0.8, ah, 0.08), doorC);
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++)
                        W.Sphere(P(g + sx * (0.16 + c * 0.2), 0.3 + r * 0.38, dzd + 0.05), 0.028f, st == "han" ? GOLD : C(0.36f, 0.34f, 0.32f), 0, SO(top: 0.2f));
            }
            // 拱顶以内的门扇上半（半圆）
            for (int i = 0; i < N; i++)
            {
                double a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N;
                W.TriN(P(g, ah, dzd + 0.04), P(g + Math.Cos(a0) * ar, ah + Math.Sin(a0) * ar, dzd + 0.04), P(g + Math.Cos(a1) * ar, ah + Math.Sin(a1) * ar, dzd + 0.04), front, front, front, Shade(doorC, -0.1));
            }
            if (st != "han")
            {
                // 城门两侧的塔与门上的女墙（罗马方塔拱窗赤陶顶 / 土坯圆塔 / 红砖方塔圆顶亭）
                for (double x = g - 1.6; x <= g + 1.61; x += 0.8) merlon(x, 3.7, Z - 0.05, 0.05);
                foreach (int sx in new[] { -1, 1 })
                {
                    double tx = g + sx * 2.35;
                    if (st == "mud") towerRound(tx, zc + 0.2, 1.0, 5.0);
                    else
                    {
                        towerSq(tx, zc + 0.15, 1.5, 5.2);
                        if (st == "roman")
                        {
                            PyramidInto(R, tx, 5.6, zc + 0.15, 1.45, 1.45, 0.7, TERRA);
                            foreach (var wy in new[] { 3.0, 4.3 }) W.Box(P(tx, wy, zc + 0.92), V(0.3, 0.46, 0.03), C(0.16f, 0.13f, 0.11f));
                        }
                        else chhatri(tx, 5.2, zc + 0.15, 0.55);
                    }
                    if (castle) W.Flag(P(tx + sx * 0.2, st == "mud" ? 5.0 : 5.2, Z - 0.3), 1.55f, 0.8f, 0.52f, defColor);
                }
                return;
            }
            // 城门楼：台基、红柱、檐下额枋与匾额、曲面重檐
            var red = C(0.62f, 0.2f, 0.16f);
            S.Box(P(g, 3.8, zc + 0.12), V(3.3, 0.2, TH + 0.3), Shade(stone, 0.04));
            W.Box(P(g, 4.4, zc), V(2.9, 1.0, 1.3), Shade(red, -0.08));
            for (int i = -2; i <= 2; i++) W.Lathe(P(g + i * 0.68, 3.9, Z + 0.08), new[] { L(0.07, 0), L(0.065, 1.0) }, 8, red);
            W.Box(P(g, 4.82, Z + 0.06), V(3.0, 0.14, 0.1), C(0.2f, 0.36f, 0.4f));
            W.Box(P(g, 4.6, Z + 0.07), V(1.1, 0.34, 0.04), C(0.12f, 0.1f, 0.09f));
            W.Box(P(g, 4.6, Z + 0.09), V(0.9, 0.24, 0.02), GOLD);
            // 本城：金顶城楼 + 守方大旗
            R.ChineseRoof(P(g, 4.9, zc), 4.0f, 2.2f, 1.15f, castle ? C(0.85f, 0.65f, 0.2f) : C(0.22f, 0.26f, 0.34f));
            if (castle)
            {
                W.Flag(P(g + 1.55, 3.9, Z), 1.55f, 0.8f, 0.52f, defColor);
                W.Flag(P(g - 1.55, 3.9, Z), 1.55f, 0.8f, 0.52f, defColor);
            }
        }
        // 拒马、倚墙的云梯、城头旗帜（各式城墙共用）；top：墙头高度，gap：城门两侧不插旗的半宽
        static void WallDressing(MeshBuilder W, double? gx, SeededRandom rnd, Color defColor, Height H, double top, double gap)
        {
            const double Z = WALL_Z;
            var stake = Shade(WOOD, 0.08);
            Action<Vector3, Vector3, double, double, Color> pole = (a, b, r0, r1, col) => W.Tube(new[] { a, b }, new[] { (float)r0, (float)r1 }, 6, col, false);
            Action<double, double, double> juma = (cx, cz, len) =>
            {
                double y0 = H.H(cx, cz);
                pole(P(cx - len / 2, y0 + 0.36, cz), P(cx + len / 2, y0 + 0.36, cz), 0.055, 0.055, Shade(WOOD, -0.08));
                int n = Math.Max(2, JsRound(len / 0.55));
                for (int i = 0; i < n; i++)
                {
                    double x = cx - len / 2 + 0.2 + (len - 0.4) * (i / (double)(n - 1));
                    pole(P(x, y0 + 0.02, cz - 0.42), P(x, y0 + 0.78, cz + 0.4), 0.035, 0.006, stake);
                    pole(P(x + 0.04, y0 + 0.02, cz + 0.42), P(x + 0.04, y0 + 0.78, cz - 0.4), 0.035, 0.006, stake);
                }
            };
            var jx = gx == null ? new[] { 1.4, 6.8 } : new[] { gx.Value - 3.3, gx.Value + 3.3, gx.Value + 8.6 };
            foreach (var x in jx) juma(x, Z + 1.75 + (rnd.NextDouble() - 0.5) * 0.3, 1.9);
            {
                double lx = gx == null ? 9.5 : gx.Value + 6.2, zb = Z + 1.25, zt = Z + 0.12, tp = top - 0.2;
                foreach (var s in new[] { -0.24, 0.24 }) pole(P(lx + s, H.H(lx, zb) + 0.02, zb), P(lx + s, tp, zt), 0.04, 0.035, WOOD);
                for (int i = 1; i < 8; i++)
                {
                    double u = i / 8.0;
                    pole(P(lx - 0.24, Lerp(H.H(lx, zb), tp, u), Lerp(zb, zt, u)), P(lx + 0.24, Lerp(H.H(lx, zb), tp, u), Lerp(zb, zt, u)), 0.025, 0.025, Shade(WOOD, 0.05));
                }
            }
            for (int k = 0; k < 6; k++)
            {
                double x = 1.6 + k * 2.6;
                if (gx != null && Math.Abs(x - gx.Value) < gap) continue;
                W.Flag(P(x, top + 0.1, Z - 0.45), 1.3f, 0.55f, 0.4f, defColor);
            }
        }
        // 木栅寨墙（日耳曼、凯尔特、草原、南方诸族）：土垒、削尖的原木、木望楼与木门
        static void BuildPalisade(MeshBuilder[] B, string kind, SeededRandom rnd, Color defColor, Height H, ClashLook look)
        {
            const double Z = WALL_Z, X0 = -0.6, X1 = 44, zc = Z - 0.45;
            double? gx = kind == "wall" ? (double?)null : GATE_X;
            MeshBuilder W = B[K_PLAIN], T = B[K_ROCK];
            var earth = C(0.5f, 0.42f, 0.3f); bool steppeHide = look != null && (look.culture == "steppe" || look.culture == "sarmatian");
            var roofC = steppeHide ? C(0.62f, 0.52f, 0.4f) : THATCH;
            // 土垒
            Action<double, double> bank = (a, b) => { if (b - a > 0.1) T.Box(P((a + b) / 2, 0.12, zc + 0.15), V(b - a, 0.5, 1.9), earth); };
            if (gx == null) bank(X0, X1); else { bank(X0, gx.Value - 1.25); bank(gx.Value + 1.25, X1); }
            // 原木（顶端削尖），每根高低、粗细、色泽略不同
            double top = 0;
            for (double x = X0 + 0.12; x < X1; x += 0.25)
            {
                if (gx != null && Math.Abs(x - gx.Value) < 1.25) continue;
                double h = 2.45 + rnd.NextDouble() * 0.35, r = 0.11 + rnd.NextDouble() * 0.025; var c = Shade(WOOD, 0.1 + (rnd.NextDouble() - 0.5) * 0.14);
                W.Tube(new[] { P(x, 0.2, zc), P(x, h, zc) }, new[] { (float)r, (float)(r * 0.92) }, 6, c, false);
                W.Lathe(P(x, h, zc), new[] { L(r * 0.92, 0), L(0, 0.28) }, 6, Shade(c, 0.08));
                top = Math.Max(top, h);
            }
            // 横向绑木（正面两道）
            Action<double, double, double> beam = (a, b, y) => { if (b - a > 0.1) W.Tube(new[] { P(a, y, Z + 0.06), P(b, y, Z + 0.06) }, new[] { 0.05f, 0.05f }, 5, Shade(WOOD, -0.12), false); };
            foreach (var y in new[] { 0.9, 1.9 }) { if (gx == null) beam(X0, X1, y); else { beam(X0, gx.Value - 1.25, y); beam(gx.Value + 1.25, X1, y); } }
            // 望楼：四根立柱、平台、栏杆、攒尖顶（草原为毡顶）
            Action<double, double, double> post = (x, z, h) => W.Tube(new[] { P(x, 0, z), P(x, h, z) }, new[] { 0.08f, 0.07f }, 6, Shade(WOOD, -0.05), false);
            Action<double, double, double, double> tower = (x, z, w, h) =>
            {
                foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) post(x + sx * w * 0.42, z + sz * w * 0.42, h + 0.9);
                W.Box(P(x, h, z), V(w + 0.2, 0.14, w + 0.2), Shade(WOOD, -0.1));
                foreach (int sz in new[] { -1, 1 }) W.Box(P(x, h + 0.42, z + sz * (w / 2 + 0.08)), V(w + 0.2, 0.5, 0.06), Shade(WOOD, 0.05));
                foreach (int sx in new[] { -1, 1 }) W.Box(P(x + sx * (w / 2 + 0.08), h + 0.42, z), V(0.06, 0.5, w + 0.2), Shade(WOOD, 0.05));
                PyramidInto(T, x, h + 0.9, z, w + 0.6, w + 0.6, 0.95, roofC);
            };
            tower(X0 + 0.4, zc, 1.5, 3.4);
            // 寨内房屋（只露出屋顶）
            string hs = look != null && look.house != null ? look.house : "long";
            for (int k = 0; k < 7; k++)
            {
                double x = 2 + k * 3.4 + rnd.NextDouble() * 1.2, z = Z - 3.6 - rnd.NextDouble() * 5;
                double s = hs == "long" ? 1.3 : hs == "yurt" ? 1.6 : 1.45;
                HouseInto(B, k == 3 && hs != "yurt" ? (hs == "stilt" ? "stilt" : "long") : hs, x, 1.25, z, rnd, k == 3 ? s * 1.25 : s);
            }
            WallDressing(W, gx, rnd, defColor, H, top, 2.0);
            if (gx == null) return;
            double g = gx.Value;
            // 寨门：两座望楼、门上走道、原木门扇
            foreach (int sx in new[] { -1, 1 }) tower(g + sx * 1.75, zc, 1.3, 3.6);
            W.Box(P(g, 2.95, zc), V(3.6, 0.22, 1.0), Shade(WOOD, -0.12));
            W.Box(P(g, 3.3, Z - 0.02), V(3.6, 0.5, 0.08), Shade(WOOD, 0.04));
            foreach (int sx in new[] { -1, 1 })
            {
                for (int i = 0; i < 4; i++) W.Tube(new[] { P(g + sx * (0.15 + i * 0.26), 0.05, Z - 0.2), P(g + sx * (0.15 + i * 0.26), 2.55, Z - 0.2) }, new[] { 0.12f, 0.12f }, 6, Shade(WOOD, 0.02 + (i % 2) * 0.05), false);
                W.Box(P(g + sx * 0.55, 1.9, Z - 0.05), V(1.05, 0.12, 0.06), Shade(WOOD, -0.15));
                W.Box(P(g + sx * 0.55, 0.7, Z - 0.05), V(1.05, 0.12, 0.06), Shade(WOOD, -0.15));
            }
            if (kind == "castle") foreach (int sx in new[] { -1, 1 }) W.Flag(P(g + sx * 1.9, 4.5, Z - 0.3), 1.55f, 0.8f, 0.52f, defColor);
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
#if CLASH_HEADLESS
            own.Add(baseTex);
            return baseTex;
#else
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
#endif
        }
#if !CLASH_HEADLESS
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
#endif
    }


    // 会飘动的旗面（= JS class Flag）：网页版局部坐标里位于 y-z 平面，旗杆边在 z = 0，向 +z 展开、自 y = 0 向下垂。
    // 第三版挂在人物骨骼 / 帅旗杆上（旗手的右手握点、马鞍后的旗杆），所以用人物局部的换算：JS 局部 (x, y, z) → Unity (−x, y, z)，
    // 三角形绕序随之反转。网页版是双面材质（背面法线取反）：这里正反两面各一份顶点（背面法线取反、绕序相反），着色器只画正面。
    // 法线每帧按 three 的 computeVertexNormals（面积加权）求出后同样换算。
    internal sealed class ClashFlag
    {
        public readonly GameObject go;
        public readonly Mesh mesh;
        public float k = 1;                         // 风力（0 = 下垂，1 = 正常飘扬，>1 疾驰）
        readonly float w, h, seed; readonly bool big;
        readonly int nx, ny, nv;
        readonly Vector3[] pos, nrm;
        readonly double[] p3, n3;
        readonly int[] idx3;
        public ClashFlag(Material mat, float w, float h, bool flipU, float seed, bool big, Transform parent, Vector3 localPos, bool castShadow)
        {
            this.w = w; this.h = h; this.seed = seed; this.big = big;
            nx = 8; ny = big ? 6 : 4;
            nv = (nx + 1) * (ny + 1);
            pos = new Vector3[nv * 2]; nrm = new Vector3[nv * 2];
            p3 = new double[nv * 3]; n3 = new double[nv * 3];
            var uv = new Vector2[nv * 2]; var col = new Color[nv * 2];
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    int q = j * (nx + 1) + i;
                    uv[q] = uv[q + nv] = new Vector2(flipU ? 1 - (float)i / nx : (float)i / nx, 1 - (float)j / ny);
                    col[q] = col[q + nv] = Color.white;
                }
            var front = new List<int>();
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    front.Add(a); front.Add(c); front.Add(b); front.Add(b); front.Add(c); front.Add(d);
                }
            idx3 = front.ToArray();
            // Unity：正面 = 网页版的正面经 x 镜像（绕序反转）；背面 = 另一份顶点、绕序再反（= 网页版原绕序）
            var tris = new List<int>();
            for (int q = 0; q < idx3.Length; q += 3) { tris.Add(idx3[q]); tris.Add(idx3[q + 2]); tris.Add(idx3[q + 1]); }
            for (int q = 0; q < idx3.Length; q += 3) { tris.Add(idx3[q] + nv); tris.Add(idx3[q + 1] + nv); tris.Add(idx3[q + 2] + nv); }
            mesh = new Mesh { name = "clash flag" };
            mesh.MarkDynamic();
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.colors = col;
            mesh.SetTriangles(tris, 0);
            go = new GameObject(big ? "Banner" : "Flag");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.AngleAxis(180, Vector3.up);     // JS makeRotationY(π)（绕 Y 的转动在 x 镜像下不变）
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = false;
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
                    p3[q * 3] = (Mathf.Sin(ph) * 0.085f + Mathf.Sin(ph * 1.7f + 1.3f) * 0.03f) * w * amp * (0.4f + 0.6f * Mathf.Min(1.4f, kk));
                    p3[q * 3 + 1] = -v * h - droop * w * 0.55f - u * w * 0.05f;
                    p3[q * 3 + 2] = u * w * (1 - droop * 0.45f) * (1 - 0.06f * Mathf.Abs(Mathf.Sin(ph)));
                }
            // three computeVertexNormals（索引网格：cb × ab 面积加权累加后归一化）
            Array.Clear(n3, 0, n3.Length);
            for (int q = 0; q < idx3.Length; q += 3)
            {
                int a = idx3[q] * 3, b = idx3[q + 1] * 3, c = idx3[q + 2] * 3;
                double cbx = p3[c] - p3[b], cby = p3[c + 1] - p3[b + 1], cbz = p3[c + 2] - p3[b + 2];
                double abx = p3[a] - p3[b], aby = p3[a + 1] - p3[b + 1], abz = p3[a + 2] - p3[b + 2];
                double nx_ = cby * abz - cbz * aby, ny_ = cbz * abx - cbx * abz, nz_ = cbx * aby - cby * abx;
                for (int s = 0; s < 3; s++) { int o = idx3[q + s] * 3; n3[o] += nx_; n3[o + 1] += ny_; n3[o + 2] += nz_; }
            }
            for (int q = 0; q < nv; q++)
            {
                double x = n3[q * 3], y = n3[q * 3 + 1], z = n3[q * 3 + 2], l = Math.Sqrt(x * x + y * y + z * z);
                if (l > 0) { x /= l; y /= l; z /= l; } else y = 1;
                var pp = new Vector3((float)-p3[q * 3], (float)p3[q * 3 + 1], (float)p3[q * 3 + 2]);
                var nn = new Vector3((float)-x, (float)y, (float)z);
                pos[q] = pp; pos[q + nv] = pp;
                nrm[q] = nn; nrm[q + nv] = -nn;
            }
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.RecalculateBounds();
        }
    }
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
        public string Hex { get { return ClashUtil.HexJs(color); } }
    }
    // 混战动作表的一项：t 起、片段、播放速度、时长（秒）
    internal struct ClashAct { public float t, sp, d; public string clip; }
    internal sealed class ClashSoldier
    {
        public string role;            // inf 步兵 / arch 弓手 / flag 旗手
        public string type = "spear";  // 兵种：spear / sword / archer（士兵群的模板）
        public float f, l; public int file, si;
        public double fd, ld;          // 阵位的 double 原值（选旗手的排序与网页版逐位一致：相差 1e-16 的两排也分得出先后）
        public float x0, z0, ed, cd, rd, push;
        public float w1, p1, w2, p2, w3, p3, w4, p4, wt, pt, p0;
        public float fallT = float.PositiveInfinity; public string fallKind = "melee"; public float fallDir, kb;
        public readonly List<float> hits = new List<float>();
        public float shotDelay;
        public int cid;                // 士兵群里的编号
        public Figure fig;             // 旗手：单独的人物模型
        public ClashFlag flag;
        public List<ClashAct> acts;
        public string colKey = "";
    }
    internal sealed class ClashGeneral
    {
        public float x0, z0, ed, p0;
        public FigureHorse horse;
        public Figure fig;
        public GameObject pole;
        public ClashFlag flag;
        public float x, y, z;
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
        public string hexBase, hexFlash, hexDead;
        public string Hex(string key) { return key == "flash" ? hexFlash : key == "dead" ? hexDead : hexBase; }
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
    internal sealed class ClashFx { public Mesh mesh; public Material mat; public float t0, life; public Action<float> fn; public Matrix4x4 m3 = Matrix4x4.identity; public bool hit; public bool ownMesh = true; }
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
        // 第三版：入场距离缩短（人物按位移换算步频，免得滑步）；人物模型单位为米、面朝 +Z，本场景沿用旧单位（士兵约 0.9 高），统一缩放 FIG_S
        const float ENTER_DX = 1.8f, ZC = ClashTerrain.ZC, FRONT = 2.05f, SP_F = 0.54f, SP_L = 0.62f;
        public const float FIG_S = 0.5f;
        const float BACK = 0.6f, GBACK = 0.45f;          // 收队时步兵 / 武将后撤的距离
        // 旗手持杆（人物局部坐标，米）：网页版 FLAG_GRIP (−0.2, 1.12, 0.24)、FLAG_DIR (0.04, 1, 0.1) → Unity 人物局部 x 取反
        static readonly Vector3 FLAG_GRIP = new Vector3(0.2f, 1.12f, 0.24f), FLAG_DIR = new Vector3(-0.04f, 1, 0.1f);
        public static readonly Color FOG = new Color(0.86f, 0.84f, 0.78f);
        public static readonly string[] FORM_NAMES = { "方圆", "长蛇", "鱼鳞", "鹤翼", "偃月", "锋矢", "雁行", "衡轭" };
        public static readonly Dictionary<string, string> KIND_NAME = new Dictionary<string, string>
        { { "plain", "平原" }, { "forest", "森林" }, { "hill", "山丘" }, { "mountain", "山岳" }, { "river", "河川" }, { "wall", "城墙" }, { "gate", "城门" }, { "castle", "本城" } };
        static readonly string[] NANMAN = { "孟获", "孟优", "祝融", "兀突骨", "木鹿", "朵思", "带来" };
        // 光照（Unity 灯光强度 = JS 强度 / π，见 Figure.shader）：网页版主场景的太阳 1.15π × 0.75、半球光 π × 0.56
        static readonly Color SUN_COLOR = new Color(1, 0.94f, 0.84f);
        const float SUN_I = 1.15f * 0.75f, AMB_K = 0.56f;
        static readonly Color AMB_SKY = new Color(0.62f, 0.68f, 0.8f), AMB_EQ = new Color(0.58f, 0.56f, 0.5f), AMB_GND = new Color(0.32f, 0.3f, 0.26f);

        static Color C(float r, float g, float b) { return new Color(r, g, b); }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }
        static float Seg(float t, float a, float b) { return ClashUtil.Seg(t, a, b); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        static float Clamp(float v, float a, float b) { return ClashUtil.Clamp(v, a, b); }
        static float Sm(float t) { return ClashUtil.Sm(t); }
        static int JsRound(float v) { return (int)Mathf.Floor(v + 0.5f); }
        // Math.round 的 double 版（与网页版同精度：k * 0.45 等在 .5 处不因 float 误差取错）
        static int JsRoundD(double v) { return (int)Math.Floor(v + 0.5); }
        float Rn() { return (float)rnd.NextDouble(); }

        // ------------------------------------------------------------ 状态 --
        public readonly string kind;
        public readonly ClashInfo A, D;
        public readonly ClashLook look;
        public readonly ClashSpecial special;
        public readonly string specialKind;
        public readonly int playerSide;            // -1 = 无
        public readonly float optSpeed;
        public float speed;
        readonly SeededRandom rnd;
        internal readonly int seed;
        public readonly bool recolored;
        public float t, real, tPrev;
        public string state = "prep";              // prep | build | wait | run | hold | out | done
        public bool Skipped, Finished, paused, cutDone, built, shown;
        float shake;
        int evIdx;
        float holdUntil, holdWall, buildStart, showReal;
        bool waitShown;
        internal ClashTerrain.Height h;
        internal ClashTerrainOut terrainOut;
        public ClashArmy[] armies;
        internal float xc, fit = 1;
        internal List<ClashShot> arrows;
        internal List<ClashChunk> chunks;
        internal List<KeyValuePair<float, Action>> events;
        readonly List<ClashFx> temp = new List<ClashFx>();
        ClashPool dust, fx, glow;
        Mesh arrowMesh;
        Color arrowGlow = Color.white;
        internal FigureCrowd crowd;
        internal readonly List<Figure> figs = new List<Figure>();
        internal readonly List<FigureHorse> horses = new List<FigureHorse>();
        readonly List<UnityEngine.Object> own = new List<UnityEngine.Object>();
        readonly List<ClashFlag> flags = new List<ClashFlag>();
        Material propMat, arrowMat, grassMat, flowerMat;
        Matrix4x4[] tuftW, flowerW;
        Vector4[] tuftC, flowerC;
        MaterialPropertyBlock tuftMpb, flowerMpb;
        internal GameObject root;
        public Camera cam;
        ClashRunner runner;
        public MonoBehaviour runnerHost { get { return runner; } }
        public ClashHud hud;
        readonly Stack<IEnumerator> buildStack = new Stack<IEnumerator>();
        IEnumerator prepIt;
        MaterialPropertyBlock mpb;
        bool disposed;
        int lastW, lastH;
        // 播放期间改动、结束时还原的全局状态
        bool fogSaved, fog0; FogMode fogMode0; Color fogColor0; float fogStart0, fogEnd0, shadowDist0;
        bool ambSaved; AmbientMode ambMode0; Color ambSky0, ambEq0, ambGnd0;
        readonly List<KeyValuePair<Light, int>> lightMasks = new List<KeyValuePair<Light, int>>();
        Light sun;
        readonly List<Camera> camsOff = new List<Camera>();
#if !CLASH_HEADLESS
        readonly List<KeyValuePair<CanvasGroup, bool>> groups = new List<KeyValuePair<CanvasGroup, bool>>();
        readonly List<float> groupAlpha = new List<float>();
        readonly List<bool> groupBlocks = new List<bool>();
        readonly List<bool> groupInter = new List<bool>();
#endif
        bool rigSaved, rigInput0;

        public ClashScene(ClashSide a, ClashSide d, Terrain terrain, ClashSpecial sp, int playerSide, float spd, ClashSceneInfo scene = null)
        {
            kind = Clash.TerrainKind(terrain);
            A = Norm(a, 0);
            D = Norm(d, 1);
            // 布景：防守城池的文化与当地群落、是否登陆（缺省按防守方文化）
            look = ClashLook.Of(scene, D.culture);
            if (kind == "river") look.coast = false;
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
        // 识别色：两军势力色相近（原始 RGB 距离 < ACC_DIST）时，各给一种与两军颜色都拉得开、彼此也不同的镶边色（旗帜的犬牙边上）
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
        // 先淡入黑幕（战场照常绘制），黑幕下等预热、分帧搭建场景，再切到自己的相机
        public void Start()
        {
            CreateRoot();
            runner = root.AddComponent<ClashRunner>();
            hud = new ClashHud(this);
            hud.FadeBlack(FADE_IN * 0.75f);
            buildStart = Time.realtimeSinceStartup;
            state = "prep";
            runner.sc = this;
        }
        void CreateRoot()
        {
            mpb = new MaterialPropertyBlock();
            root = new GameObject("Clash");
            root.layer = Clash.Layer;
            root.transform.position = Clash.Origin;
        }
        // 人物模型库的预生成（= JS SG.Figure.preload：图集、环境贴图、两军文化的士兵群烘焙）
        IEnumerator PrepJob()
        {
            var cs = new List<string> { A.culture }; if (!cs.Contains(D.culture)) cs.Add(D.culture);
            return Figure.Preload(new FigPreload { cultures = new[] { A.culture, D.culture }, types = new string[0], crowd = Clash.CrowdSpecs(cs), budget = 24 });
        }
        // 嵌套协程的一步（yield return 子 IEnumerator 为嵌套）；返回 false = 做完
        bool StepBuild()
        {
            while (buildStack.Count > 0)
            {
                var top = buildStack.Peek();
                if (!top.MoveNext()) { buildStack.Pop(); continue; }
                var sub = top.Current as IEnumerator;
                if (sub != null) buildStack.Push(sub);
                return true;
            }
            return false;
        }
        // 测试用：一次做完预热与构建（不切相机、不改全局状态）
        internal void BuildSync()
        {
            if (root == null) CreateRoot();
            var p = PrepJob();
            while (p.MoveNext()) { }
            buildStack.Clear(); buildStack.Push(Build());
            while (StepBuild()) { }
            built = true;
            Resize();
        }

        public void Tick(float dt)
        {
            if (disposed) return;
            try
            {
                if (state == "prep" || state == "build")
                {
                    // 黑幕下准备超过约 0.3 秒（首次遇到的文化、慢设备）时显示「布阵中」，不让玩家对着纯黑画面干等
                    if (!waitShown && Time.realtimeSinceStartup - buildStart > 0.3f) { waitShown = true; if (hud != null) hud.Wait(true); }
                    if (hud != null) hud.Update(dt, 0);       // 黑幕照常淡入
                }
                if (state == "prep")
                {
                    // 正在进行的预热（Clash.Prepare）先做完，免得同一模板并行生成两遍（= JS prepChain）
                    if (Clash.PrepBusy) return;
                    if (prepIt == null) prepIt = PrepJob();
                    if (prepIt.MoveNext()) return;           // 预生成自己按每帧约 24 毫秒分片
                    prepIt = null;
                    buildStack.Clear(); buildStack.Push(Build());
                    state = "build";
                    return;
                }
                if (state == "build")
                {
                    // 黑幕下画面不可见：每片多做一些（约 40 毫秒），少等几帧
                    float f0 = Time.realtimeSinceStartup;
                    bool more = true;
                    while (Time.realtimeSinceStartup - f0 < 0.040f) if (!(more = StepBuild())) break;
                    if (!more)
                    {
                        built = true;
                        Resize();
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

        // 自己的相机（深度高于其它相机，只拍本层）
        internal void SetupCamera()
        {
            var cgo = new GameObject("ClashCamera");
            cgo.transform.SetParent(root.transform, false);
            cgo.layer = Clash.Layer;
            cam = cgo.AddComponent<Camera>();
            float depth = 0;
            foreach (var c in Camera.allCameras) if (c != cam) depth = Mathf.Max(depth, c.depth);
            cam.depth = depth + 10;
            cam.cullingMask = 1 << Clash.Layer;
            // 网页版：scene.background = 雾色，再画主场景的天空穹顶（Gfx.sky）——这里用同一个天空盒材质
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = FOG;
            cgo.AddComponent<Skybox>().material = Art.Sky;
            cam.fieldOfView = 34;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 300;
            cam.allowMSAA = true;
        }
        // 光照与主场景一致（网页版：太阳的颜色 / 强度取主场景，方向取 Gfx.sunDir，阴影只覆盖这条战场；半球光同主场景）
        void SetupLights()
        {
            var sgo = new GameObject("ClashSun");
            sgo.transform.SetParent(root.transform, false);
            sgo.layer = Clash.Layer;
            sgo.transform.rotation = Quaternion.Euler(48, -35, 0);      // = Game.SetupWorld 的太阳（网页版 SUN_DIR）
            sun = sgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SUN_COLOR;
            sun.intensity = SUN_I;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sun.shadowResolution = ClashUtil.IsTouch ? LightShadowResolution.Medium : LightShadowResolution.High;   // 网页版 1024 / 2048
            sun.cullingMask = 1 << Clash.Layer;
            // 其它灯光不照本画面（场景里的太阳等）：去掉本层，结束时还原
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>())
                if (l != sun && (l.cullingMask & (1 << Clash.Layer)) != 0) { lightMasks.Add(new KeyValuePair<Light, int>(l, l.cullingMask)); l.cullingMask &= ~(1 << Clash.Layer); }
            // 环境光：人物用 Unity 的 Trilight（天空 / 地面 = JS 半球光 × 强度 / π，地平线取两者平均，见 Figure.shader）；
            // 本画面的地面 / 道具 / 草 / 旗着色器按网页版 LowPoly 补丁自己算三色环境光（地平线色同主场景）
            ambSaved = true;
            ambMode0 = RenderSettings.ambientMode; ambSky0 = RenderSettings.ambientSkyColor; ambEq0 = RenderSettings.ambientEquatorColor; ambGnd0 = RenderSettings.ambientGroundColor;
            Color sky = AMB_SKY * AMB_K, gnd = AMB_GND * AMB_K;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky; RenderSettings.ambientGroundColor = gnd; RenderSettings.ambientEquatorColor = (sky + gnd) * 0.5f;
            try { DynamicGI.UpdateEnvironment(); } catch (Exception) { /* 忽略 */ }
            SetGlobals();
        }
        internal static void SetGlobals()
        {
            Shader.SetGlobalColor("_SgClashSky", AMB_SKY * AMB_K);
            Shader.SetGlobalColor("_SgClashEq", AMB_EQ * AMB_K);
            Shader.SetGlobalColor("_SgClashGnd", AMB_GND * AMB_K);
            Shader.SetGlobalVector("_SgClashOrigin", Clash.Origin);
        }
        void Show()
        {
            SetupCamera();
            SetupLights();
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
#if !CLASH_HEADLESS
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
#endif
            shown = true;
            showReal = Time.realtimeSinceStartup;
            state = "run";
            if (hud != null) hud.FadeIn(FADE_IN);
        }

        // ---------------------------------------------------------- 构建 --
        IEnumerator Build()
        {
            h = new ClashTerrain.Height(kind, seed, look);
            terrainOut = new ClashTerrainOut();
            yield return ClashTerrain.Build(terrainOut, kind, h, look, new SeededRandom(seed ^ 0x5bd1e995), D.color, A.color, D.color);
            BuildTerrainObjects();
            yield return null;
            // 两军
            armies = new[] { BuildArmy(A, 0), BuildArmy(D, 1) };
            PlanCharge();
            PlanArrows();
            yield return null;
            yield return BuildFigures();
            PlanCasualties();
            PlanActs();
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
        // 地面、道具、草丛、水面的物体与材质
        void BuildTerrainObjects()
        {
            var O = terrainOut;
            var gmat = new Material(Art.LoadShader("ClashGround")) { name = "ClashGround" };
            gmat.SetTexture("_DetA", Art.TerrainDetailA); gmat.SetTexture("_DetB", Art.TerrainDetailB);
            gmat.SetFloat("_SnowH", O.snowH);
            // 触屏设备（手机 GPU、高像素密度）：地面省掉最细一级细节纹理（= JS SG_LITE）
            gmat.SetFloat("_Lite", ClashUtil.IsTouch ? 1 : 0);
            own.Add(gmat); own.Add(O.ground);
            var g = Art.MakeObject("Ground", O.ground, gmat, root.transform, false);
            g.GetComponent<MeshRenderer>().receiveShadows = true;
            propMat = new Material(Art.LoadShader("ClashProp")) { name = "ClashProp" };
            own.Add(propMat);
            if (O.propsNear != null) { own.Add(O.propsNear); Art.MakeObject("Props", O.propsNear, propMat, root.transform, true); }
            if (O.propsFar != null) { own.Add(O.propsFar); Art.MakeObject("PropsFar", O.propsFar, propMat, root.transform, false); }
            // 草丛 / 野花：实例化（Graphics.DrawMeshInstanced，每帧在 Draw 里提交）
            if (O.tuftMesh != null)
            {
                own.Add(O.tuftMesh);
                grassMat = new Material(Art.LoadShader("ClashGrass")) { name = "ClashGrass", enableInstancing = true }; own.Add(grassMat);
                tuftW = O.tufts.Select(m => Matrix4x4.Translate(Clash.Origin) * m).ToArray(); tuftC = O.tuftCols.ToArray();
                tuftMpb = new MaterialPropertyBlock(); tuftMpb.SetVectorArray("_InstColor", tuftC);
            }
            if (O.flowerMesh != null)
            {
                own.Add(O.flowerMesh);
                flowerMat = grassMat ?? new Material(Art.LoadShader("ClashGrass")) { name = "ClashGrass", enableInstancing = true };
                if (grassMat == null) { grassMat = flowerMat; own.Add(flowerMat); }
                flowerW = O.flowers.Select(m => Matrix4x4.Translate(Clash.Origin) * m).ToArray(); flowerC = O.flowerCols.ToArray();
                flowerMpb = new MaterialPropertyBlock(); flowerMpb.SetVectorArray("_InstColor", flowerC);
            }
            // 河水 / 海：网页版的水面着色器（uAlpha 0.66、uAmp 0.06）
            foreach (var wm in new[] { O.river, O.sea })
            {
                if (wm == null) continue;
                var wmat = new Material(Art.LoadShader("ClashWater")) { name = "ClashWater" };
                wmat.SetTexture("_Ripple", Art.Ripple);
                wmat.SetFloat("_Alpha", 0.66f); wmat.SetFloat("_Amp", 0.06f);
                wmat.renderQueue = 3001;
                own.Add(wmat); own.Add(wm);
                var w = Art.MakeObject(wm == O.river ? "River" : "Sea", wm, wmat, root.transform, false);
                w.GetComponent<MeshRenderer>().receiveShadows = false;
            }
            ClashUtil.SetLayer(root, Clash.Layer);
        }

        ClashArmy BuildArmy(ClashInfo info, int si)
        {
            int dir = si == 0 ? 1 : -1;
            // 第三版修订：兵更多（约多两排纵深），混战像人群而不是一条细线
            int n = info.before > 0 ? Math.Max(6, Math.Min(36, JsRoundD(info.before / 240.0) + 3)) : 6;
            int nArch = Math.Max(2, Math.Min(6, JsRoundD(n * 0.2)));
            int nInf = n - nArch;
            var slots = FormationSlots(info.formation, nInf);
            double minF = 0;
            foreach (var sl in slots) minF = Math.Min(minF, sl.f);
            var soldiers = new List<ClashSoldier>();
            float xFront = -dir * FRONT;
            Func<string, double, double, int, ClashSoldier> mk = (role, fD, lD, file) =>
            {
                float f = (float)fD, l = (float)lD;
                var s = new ClashSoldier { role = role, f = f, l = l, fd = fD, ld = lD, file = file, si = si };
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
            // 兵种：前排刀盾、其后长枪（罗马为短剑与长枪交替），弓手单列
            foreach (var sl in slots) { int file = sl.file; mk("inf", sl.f, sl.l, file).type = info.culture == "roman" ? (file % 2 == 1 ? "spear" : "sword") : (file == 0 ? "sword" : "spear"); }
            // 弓手：步兵之后一到两排
            int aRows = nArch > 4 ? 2 : 1, per = Mathf.CeilToInt(nArch / (float)aRows);
            double minAllD = minF;
            for (int k = 0; k < nArch; k++)
            {
                int row = k / per, cnt = Mathf.Min(per, nArch - row * per), i = k - row * per;
                double f = minF - 0.72 - row * 0.5, l = (i - (cnt - 1) / 2.0) * 0.82 + (row % 2) * 0.41;
                mk("arch", f, l, 99).type = "archer";
                minAllD = Math.Min(minAllD, f);
            }
            float minAll = (float)minAllD;
            // 旗手：步兵最后一排中最靠近中线的一到两名
            var rear = soldiers.Where(s => s.role == "inf").OrderBy(s => s.fd).ThenBy(s => Math.Abs(s.ld)).ToList();
            int nFlag = n >= 14 ? 2 : 1;
            var fl = new List<ClashSoldier>();
            foreach (var s in rear)
            {
                if (fl.Count >= nFlag) break;
                if (fl.Any(o => Math.Abs(o.ld - s.ld) < 1.2)) continue;
                s.role = "flag"; s.type = "spear"; fl.Add(s);
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
        internal struct Slot { public double f, l; public int file; }
        internal static List<Slot> FormationSlots(int fi, int K)
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
            // 纵深（列数）有上限，免得后排与主将跑出画面（第三版：最多 10 排）
            R = Math.Max(R, (int)Math.Ceiling(K / (double)(fi == 1 ? 5 : 3)));
            R = Math.Max(1, Math.Min(Math.Min(R, K), 10));
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
            var outp = new List<Slot>();
            for (int i = 0; i < fs.Count; i++)
            {
                double f = fs[i] - mx;
                // 前排序号：按离敌远近重新编号（file 0 = 最前）
                outp.Add(new Slot { f = f, l = ls[i], file = (int)Math.Floor(-f / spF + 0.5) });
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

        // 名驹与毛色（与旧版相同的人物对应；其余按姓名散列）
        static readonly Dictionary<string, KeyValuePair<string, string>> HORSE_OF = new Dictionary<string, KeyValuePair<string, string>>
        {
            { "吕布", new KeyValuePair<string, string>("赤兔", null) }, { "关羽", new KeyValuePair<string, string>("赤兔", null) }, { "刘备", new KeyValuePair<string, string>("的卢", null) },
            { "曹操", new KeyValuePair<string, string>("绝影", null) }, { "赵云", new KeyValuePair<string, string>("白龙驹", null) },
            { "公孙瓒", new KeyValuePair<string, string>(null, "#e8e6e0") }, { "马超", new KeyValuePair<string, string>(null, "#d8d4ca") }, { "张飞", new KeyValuePair<string, string>(null, "#2a2422") },
        };
        static readonly string[] COATS = { "#7a4a2c", "#5e3a22", "#9a7448", "#38302a", "#b8ae9e", "#8a5634" };
        static FigHorseOpts HorseOpts(ClashInfo info)
        {
            string n = info.name;
            var o = new FigHorseOpts { lod = "mid", color = info.Hex, general = true, seed = ClashUtil.AbsMod(ClashUtil.HashStr(n), 5) };
            KeyValuePair<string, string> hv;
            if (HORSE_OF.TryGetValue(n, out hv)) { o.name = hv.Key; o.coat = hv.Value; }
            if (string.IsNullOrEmpty(o.name) && string.IsNullOrEmpty(o.coat)) o.coat = COATS[ClashUtil.AbsMod(ClashUtil.HashStr(n), COATS.Length)];
            // 具装：安息、萨尔马提亚为鳞甲；其余为势力色马衣
            o.armor = info.culture == "persia" || info.culture == "sarmatian" ? "scale" : "cloth";
            return o;
        }
        // 人物挂到本画面根下（Unity 局部坐标；网页版 three 坐标经 PosFromJs / YawFromJs）
        void Place(Transform tr, float x, float y, float z, float yaw)
        {
            tr.localPosition = Figure.PosFromJs(x, y, z);
            tr.localRotation = Quaternion.Euler(0, Figure.YawFromJs(yaw), 0);
        }
        // 士兵群（实例化）、旗手、武将与战马、帅旗、箭
        IEnumerator BuildFigures()
        {
            crowd = new FigureCrowd(new FigCrowdOptions { lod = "low", shadow = true, capacity = 16, parent = root.transform });
            crowd.gameObject.layer = Clash.Layer;
            foreach (var A in armies)
            {
                var info = A.info;
                A.hexBase = info.Hex; A.hexFlash = ClashUtil.MixHex(info.color, Color.white, 0.55); A.hexDead = ClashUtil.MixHex(info.color, Color.black, 0.28);
                foreach (var s in A.soldiers)
                {
                    if (s.role == "flag") continue;
                    s.cid = crowd.Add(new FigCrowdSpec { type = s.type, culture = info.culture, color = info.Hex, seed = Clash.TypeSeed(s.type) });
                    s.colKey = "base";
                    yield return null;
                }
                // 旗手：双手竖持长枪，旗面挂在枪头下
                string glyph = Surname(info);
                var bigTex = ClashBanner.Make(glyph, info.color, true, info.accent, own);
                var smallTex = ClashBanner.Make(glyph, info.color, false, info.accent, own);
                var bigMat = FlagMaterial(bigTex); var smallMat = FlagMaterial(smallTex);
                foreach (var s in A.soldiers) if (s.role == "flag")
                    {
                        var f = s.fig = Figure.Soldier(new FigOpts { type = "spear", culture = info.culture, color = info.Hex, seed = 2, lod = "low" });
                        f.Root.SetParent(root.transform, false);
                        f.Root.localScale = Vector3.one * FIG_S;
                        ClashUtil.SetLayer(f.gameObject, Clash.Layer);
                        figs.Add(f);
                        var wi = f.WeaponInfo;
                        float top = (float)((wi != null ? wi.tip.y : 2.3 - 0.3) - 0.16);
                        s.flag = new ClashFlag(smallMat, 0.56f / FIG_S, 0.4f / FIG_S, A.dir > 0, Rn() * 6, false, f.Sockets["gripR"], new Vector3(0, top, 0), true);
                        s.flag.go.layer = Clash.Layer;
                        flags.Add(s.flag); own.Add(s.flag.mesh);
                        yield return null;
                    }
                // 武将：中精度名将（专属兵器、与头像一致的面部）骑中精度战马；鞍后左侧插帅旗
                var G = A.general;
                G.horse = Figure.Horse(HorseOpts(info));
                G.fig = Figure.General(info.gen, new FigOpts { lod = "mid", culture = info.culture, color = info.Hex, mounted = true });
                G.horse.Mount(G.fig);
                G.horse.Root.SetParent(root.transform, false);
                G.horse.Root.localScale = Vector3.one * FIG_S;
                figs.Add(G.fig); horses.Add(G.horse);
                var pm = new MeshBuilder();
                pm.Lathe(V(0, 0, 0), new[] { new LP(0.035f, 0), new LP(0.028f, 4.1f) }, 8, C(0.3f, 0.2f, 0.12f));
                pm.Lathe(V(0, 4.1f, 0), new[] { new LP(0.05f, 0), new LP(0.06f, 0.05f), new LP(0.0f, 0.3f) }, 8, ClashGeo.GOLD);
                pm.Sphere(V(0, 4.08f, 0), 0.06f, ClashGeo.GOLD, 1);
                var pmesh = ClashTerrain.MergeProps(new[] { pm }, "clash pole");
                own.Add(pmesh);
                G.pole = Art.MakeObject("Pole", pmesh, propMat, null, true);
                G.pole.GetComponent<MeshRenderer>().receiveShadows = true;
                G.horse.Attach(G.pole.transform, "saddle");
                G.pole.transform.localPosition = new Vector3(-0.3f, -0.35f, -0.52f);     // 网页版 (0.3, −0.35, −0.52)（人物局部 x 取反）
                G.flag = new ClashFlag(bigMat, 0.74f / FIG_S, 0.98f / FIG_S, A.dir > 0, A.si * 3.1f, true, G.pole.transform, new Vector3(0, 3.96f, 0), true);
                flags.Add(G.flag); own.Add(G.flag.mesh);
                ClashUtil.SetLayer(G.horse.gameObject, Clash.Layer);
                yield return null;
            }
            // 箭（特殊箭雨染成必杀技颜色并发光）
            arrowMesh = ClashGeo.Arrow(); own.Add(arrowMesh);
            arrowMat = propMat;
            if (special != null)
            {
                var sc = special.color;
                arrowGlow = new Color(0.6f + sc.r * 1.6f, 0.6f + sc.g * 1.6f, 0.6f + sc.b * 1.6f);
            }
        }
        // 旗面材质：Sanguo/ClashFlag（贴图 + alphaTest 0.5，LowPoly 光照）
        Material FlagMaterial(Texture tex)
        {
            var m = new Material(Art.LoadShader("ClashFlag")) { name = "ClashFlag" };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_Cutoff", 0.5f);
            own.Add(m);
            return m;
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
                if (info.loss > 0 && k == 0 && frac >= 0.035) k = 1;
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

        // 混战动作表（前两排步兵）：横斩、反撩、重劈、格挡交替，受击时插入「受击」
        void PlanActs()
        {
            foreach (var A in armies)
                foreach (var s in A.soldiers)
                {
                    if (s.role != "inf" || s.file > 1) continue;
                    var acts = new List<ClashAct>();
                    float tt = Contact - 0.08f + Rn() * 0.1f + s.file * 0.14f;
                    float end = Mathf.Min(s.fallT, Melee1 + s.rd);
                    while (tt < end)
                    {
                        float u = Rn();
                        string clip = u < 0.36f ? "light1" : u < 0.7f ? "light2" : u < 0.86f ? "heavy" : "block";
                        float sp = clip == "heavy" ? 1.55f : clip == "block" ? 1 : 1.12f + Rn() * 0.3f;
                        float d = clip == "block" ? 0.22f + Rn() * 0.16f : (clip == "heavy" ? 0.9f : 0.42f) / sp;
                        acts.Add(new ClashAct { t = tt, clip = clip, sp = sp, d = d });
                        tt += d + Rn() * 0.07f;
                    }
                    foreach (var hh in s.hits) acts.Add(new ClashAct { t = hh, clip = "hit", sp = 1.4f, d = 0.42f / 1.4f });
                    // 稳定排序（同 JS Array.prototype.sort）
                    s.acts = acts.Select((a, i) => new KeyValuePair<int, ClashAct>(i, a)).OrderBy(p => p.Value.t).ThenBy(p => p.Key).Select(p => p.Value).ToList();
                }
        }

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
        internal void SoldierXZ(ClashArmy A, ClashSoldier s, float tt, out float ox, out float oz, out float jit)
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
            x += dir * jit * engage * (Mathf.Sin(tt * s.w1 + s.p1) * 0.09f + Mathf.Sin(tt * s.w2 + s.p2) * 0.04f);
            z += jit * Mathf.Sin(tt * s.w3 + s.p3) * 0.06f;
            if (A.routed)
            {
                float uf = Seg(tt, Melee1 + s.rd, Regroup1 + 0.25f);
                x -= dir * 8 * Mathf.Pow(uf, 1.4f);
            }
            else if (s.role != "arch" && !A.cheer)
            {
                x -= dir * BACK * ClashUtil.Eio(Seg(tt, Melee1 + s.rd, Regroup1 - 0.04f));
            }
            ox = x; oz = z;
        }
        internal void SoldierXZ(ClashArmy A, ClashSoldier s, float tt, out float ox, out float oz) { float j; SoldierXZ(A, s, tt, out ox, out oz, out j); }

        // 步幅（米 / 周期）与步频上限（周期 / 秒）：位移过快时把步幅放大（略有滑步），步频不超过自然值的 1.3 倍
        static float GaitStride(string g) { return g == "walk" ? 1.45f : g == "run" ? 3.0f : g == "hwalk" ? 1.9f : g == "trot" ? 2.6f : 5.0f; }
        static float GaitDur(string g) { return g == "walk" ? 1.0f : g == "run" ? 0.66f : g == "hwalk" ? 1.1f : g == "trot" ? 0.7f : 0.5f; }
        // 一段匀加速 / 缓动位移的步态时刻：dist = 已走距离（场景单位），D / T = 全程距离与时长
        static float GaitT(string g, float dist, float Dd, float T)
        {
            float stride = Mathf.Max(GaitStride(g) * FIG_S, (Dd / Mathf.Max(0.05f, T)) * GaitDur(g) / 1.3f);
            return (dist / stride) * GaitDur(g);
        }
        // 动作：时刻 t 该士兵播放的片段与片段内时刻（纯函数；步频由位移换算，倒放即为后退）
        internal void AnimOf(ClashArmy A, ClashSoldier s, float tt, out string clip, out float ct)
        {
            clip = "idle"; ct = tt + s.p0;
            if (tt >= s.fallT)
            {
                float u = tt - s.fallT;
                if (s.fallKind == "blast") { clip = "knockdown"; ct = u * 1.4f; }
                else { clip = "die"; ct = Mathf.Min(1.4f, 0.12f + u * 1.75f); }
                return;
            }
            float m0 = s.ed, m1 = March1 + s.ed * 0.5f;
            if (tt < m0) return;
            if (tt < m1) { clip = "run"; ct = GaitT("run", ENTER_DX * ClashUtil.Eo(Seg(tt, m0, m1)), ENTER_DX, m1 - m0) + s.p0 * 0.1f; return; }
            if (A.routed && tt >= Melee1 + s.rd)
            {
                float r0 = Melee1 + s.rd, r1 = Regroup1 + 0.25f;
                clip = "run"; ct = GaitT("run", 8 * Mathf.Pow(Seg(tt, r0, r1), 1.4f), 8, r1 - r0) + s.p0 * 0.1f;
                return;
            }
            if (s.role == "arch")
            {
                float aim = Aim + s.ed * 0.2f, rel = A.volleyT + s.shotDelay;
                if (tt >= aim && tt < rel) { clip = "bowDraw"; ct = 0.8f * Seg(tt, aim, rel); }
                else if (tt >= rel && tt < rel + 0.5f) { clip = "bowShoot"; ct = tt - rel; }
                else if (A.cheer && tt > Melee1 + 0.15f) { clip = "cheer"; ct = tt - Melee1 - 0.15f + s.p0 * 0.2f; }
                return;
            }
            float c0 = Charge0 + s.cd, c1 = Contact + s.cd * 0.35f;
            if (tt < c0)
            {
                // 敌方箭雨落下前后：前两排举盾（举兵器）护身
                var E = armies[1 - A.si];
                if (s.role == "inf" && s.file <= 1 && tt > E.volleyT + 0.12f && tt < E.volleyT + Flight + 0.3f) { clip = "block"; ct = tt - E.volleyT; }
                return;
            }
            if (tt < c1)
            {
                float Dd = A.chargeDX + s.push, u = Seg(tt, c0, c1);
                clip = "run"; ct = GaitT("run", Dd * u * u, Dd, c1 - c0) + s.p0 * 0.1f;
                return;
            }
            if (tt < Melee1 + s.rd)
            {
                if (s.acts != null)
                    for (int i = s.acts.Count - 1; i >= 0; i--)
                    {
                        var a = s.acts[i];
                        if (tt >= a.t) { if (tt < a.t + a.d) { clip = a.clip; ct = (tt - a.t) * a.sp; } break; }
                    }
                return;
            }
            if (A.cheer) { clip = "cheer"; ct = tt - Melee1 - s.rd + s.p0 * 0.2f; return; }
            float b0 = Melee1 + s.rd, b1 = Regroup1 - 0.04f;
            if (s.role != "arch" && tt < b1) { clip = "walk"; ct = 10 - GaitT("walk", BACK * ClashUtil.Eio(Seg(tt, b0, b1)), BACK, b1 - b0); }
        }
        // 武将与战马的动作；骑手动作之间用 bclip / bt / bw（bw = 前一动作的残留权重）交叉淡化，不在切换处跳帧
        internal struct GenPose { public string hclip, rclip, bclip; public float ht, rt, bt, bw, move; }
        internal GenPose GenAnim(ClashArmy A, float tt)
        {
            var G = A.general;
            var o = new GenPose { hclip = "idle", ht = tt + G.p0, rclip = "idle", rt = tt + G.p0, move = 0, bclip = null, bt = 0, bw = 0 };
            Action<string, float, float, float> fadeFrom = (clip, ct, t0, dur) => { float w = 1 - Sm(Seg(tt, t0, t0 + dur)); if (w > 0.001f) { o.bclip = clip; o.bt = ct; o.bw = w; } };
            float m0 = G.ed, m1 = March1 + 0.05f;
            if (tt >= m0 && tt < m1) { o.hclip = "trot"; o.ht = GaitT("trot", ENTER_DX * ClashUtil.Eo(Seg(tt, m0, m1)), ENTER_DX, m1 - m0) + G.p0; o.move = 0.8f; }
            // 号令放箭：高举兵器（举起、放下都淡入淡出）
            float a0 = Aim - 0.06f, a1 = A.volleyT + 0.3f;
            if (tt > a0 && tt < a1) { o.rclip = "charge"; o.rt = tt - Aim; fadeFrom("idle", tt + G.p0, a0, 0.2f); }
            else if (tt >= a1) fadeFrom("charge", a1 - Aim, a1, 0.22f);
            float c0 = Charge0 + 0.1f, c1 = Contact + 0.1f;
            if (!A.routed)
            {
                if (tt >= c0 && tt < c1) { o.hclip = "gallop"; o.ht = (tt - c0) * 1.15f; o.rclip = "charge"; o.rt = tt - c0; o.move = 1; o.bclip = null; o.bw = 0; fadeFrom("idle", tt + G.p0, c0, 0.2f); }
                // 接敌：武将在本阵后方督战，不对空挥砍——勒马人立，兵器自高举处向前一指号令全军，随后高举兵器激励士卒。
                // heavy 片段 0.42 秒处正是 charge 的举起姿势，从那里接着播到前指（约 0.58 秒）定住，无跳帧
                float p0 = Contact - 0.12f, q0 = Contact + 0.5f, e0 = Melee1 + 0.1f;
                float ptT = Mathf.Min(0.58f, 0.42f + (tt - p0) * 0.9f);
                if (tt >= p0 && tt < e0)
                {
                    o.rclip = "heavy"; o.rt = ptT;
                    if (tt >= q0) { o.rclip = "cheer"; o.rt = tt - q0; fadeFrom("heavy", 0.58f, q0, 0.35f); }
                }
                if (tt >= Contact && tt < Contact + 1.0f) { o.hclip = "rear"; o.ht = (tt - Contact) * 1.6f; }
                if (tt >= e0)
                {
                    if (A.cheer) { o.rclip = "victory"; o.rt = tt - e0; fadeFrom("cheer", e0 - q0, e0, 0.3f); }
                    else
                    {
                        fadeFrom("cheer", e0 - q0, e0, 0.3f);
                        float b1 = Regroup1;
                        if (tt < b1) { o.hclip = "walk"; o.ht = 10 - GaitT("hwalk", GBACK * ClashUtil.Eio(Seg(tt, e0, b1)), GBACK, b1 - e0); }
                    }
                }
            }
            else
            {
                if (tt >= c0 && tt < c1) { o.hclip = "trot"; o.ht = (tt - c0) * 1.1f; o.move = 0.6f; }
                if (tt >= Melee1 + 0.12f)
                {
                    float r0 = Melee1 + 0.12f, r1 = Regroup1 + 0.3f;
                    o.hclip = "gallop"; o.ht = GaitT("gallop", 9 * Mathf.Pow(Seg(tt, r0, r1), 1.4f), 9, r1 - r0); o.move = 1;
                }
            }
            return o;
        }

        internal float GroundY(float x, float z) { return (float)h.H(x, z); }

        // 按时刻 t 摆出全部士兵、旗手、武将、箭（纯函数式：跳过时直接摆出结束姿态）
        internal void Pose(float tt0)
        {
            crowd.SetTime(tt0);
            foreach (var A in armies)
            {
                float yaw0 = A.dir > 0 ? Mathf.PI / 2 : -Mathf.PI / 2;
                foreach (var s in A.soldiers)
                {
                    bool dead = tt0 >= s.fallT;
                    float tt = dead ? s.fallT : tt0;
                    float ox, oz, jit;
                    SoldierXZ(A, s, tt, out ox, out oz, out jit);
                    float x = ox, z = oz, lift = 0;
                    if (dead && s.fallKind == "blast")
                    {
                        float u = Seg(tt0, s.fallT, s.fallT + 0.5f);
                        x -= A.dir * s.kb * ClashUtil.Eo(u);       // 被震飞：朝本方后方
                        lift = 4 * 1.1f * u * (1 - u);
                    }
                    float y = GroundY(x, z) + lift;
                    float yaw = yaw0;
                    if (A.routed) yaw += Mathf.PI * Sm(Seg(tt, Melee1 + s.rd, Melee1 + s.rd + 0.2f)) * (s.p0 > 3.1f ? 1 : -1);
                    yaw += jit * Mathf.Sin(tt * s.w4 + s.p4) * 0.22f;
                    string clip; float ct;
                    AnimOf(A, s, tt0, out clip, out ct);
                    // 颜色：受击闪亮、阵亡变暗
                    string key = "base";
                    if (dead) key = tt0 - s.fallT < 0.12f ? "flash" : tt0 - s.fallT > 0.5f ? "dead" : "base";
                    else foreach (var ht in s.hits) { float d = tt0 - ht; if (d >= 0 && d < 0.14f) { key = "flash"; break; } }
                    if (s.fig != null)
                    {
                        var f = s.fig;
                        Place(f.Root, x, y, z, yaw);
                        f.SetPose(clip, ct, new FigPoseOpts { ik = false });
                        if (clip != "die" && clip != "knockdown") f.Grip(FLAG_GRIP, FLAG_DIR, null, 0);
                        if (key != s.colKey) { s.colKey = key; f.SetTint("#ffffff", key == "flash" ? 0.5f : 0); }
                        s.flag.k = dead ? 0.15f : clip == "run" ? 1.5f : 0.85f;
                    }
                    else
                    {
                        crowd.SetTransform(s.cid, Figure.PosFromJs(x, y, z), Figure.YawFromJs(yaw), FIG_S);
                        crowd.Play(s.cid, clip, tt0 - ct);
                        if (key != s.colKey) { s.colKey = key; crowd.SetColor(s.cid, A.Hex(key)); }
                    }
                }
                PoseGeneral(A, tt0);
            }
            PoseArrows(tt0);
        }

        void PoseGeneral(ClashArmy A, float tt)
        {
            var G = A.general;
            float x = GeneralX(A, tt), z = G.z0, y = GroundY(x, z);
            float yaw = A.dir > 0 ? Mathf.PI / 2 : -Mathf.PI / 2;
            if (A.routed) yaw += Mathf.PI * Sm(Seg(tt, Melee1 + 0.12f, Melee1 + 0.35f));
            var ga = GenAnim(A, tt);
            Place(G.horse.Root, x, y, z, yaw);
            G.horse.SetPose(ga.hclip, ga.ht);
            var po = new FigPoseOpts { gallop = ga.hclip == "gallop" };
            if (ga.bw > 0) { po.blendClip = ga.bclip; po.blendT = ga.bt; po.blendW = 1 - ga.bw; }
            G.fig.SetPose(ga.rclip, ga.rt, po);
            // 帅旗竖插在鞍后：战马人立（整匹马抬头约 42°）时旗杆只随之倾斜约四分之一，不像硬牌子一样向后倒、滑出画面
            // （网页版 pole.rotation.x = −(root.rotation.x + body.rotation.x) × 0.75；绕 x 的转动在人物局部的 x 镜像下不变）
            var hr = G.horse.Rig;
            double ex = hr.euler[FigBones.HB("root")].x + hr.euler[FigBones.HB("body")].x;
            G.pole.transform.localRotation = Quaternion.AngleAxis((float)(-ex * 0.75 * 180 / Math.PI), Vector3.right);
            G.x = x; G.y = y; G.z = z;
            G.flag.k = 0.85f + ga.move * 0.7f;
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
                if (u >= 1) p += d * (r.water ? 0.4f : -0.08f);   // 插入地面约 0.17（露出约六成箭杆与箭羽）
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
            r.p0 = V(ox + r.S.dir * 0.3f, GroundY(ox, oz) + 0.74f, oz);
            if (r.target != null)
            {
                SoldierXZ(r.E, r.target, r.land, out ox, out oz);
                r.p1 = V(ox, GroundY(ox, oz) + 0.6f, oz);
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
                r.water = kind == "river" && Mathf.Abs(x - (float)ClashTerrain.RIVER_X) < (float)ClashTerrain.RIVER_W + 0.6f;
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
        const float RIVER_SURF = (float)ClashTerrain.RIVER_SURF;
        void DustAt(float x, float z, float k, bool big)
        {
            float y = GroundY(x, z);
            bool wet = kind == "river" && y < RIVER_SURF - 0.05f;
            if (wet)
            {
                EmitP(fx, V(x, RIVER_SURF + 0.03f, z), big ? 10 : 4, new ClashEmit
                {
                    color = C(0.97f, 0.99f, 1), color2 = C(0.78f, 0.9f, 0.96f), alpha = 0.95f * k, life = 0.6f, speed = big ? 2.6f : 1.9f, size = 0.2f, sizeEnd = 0.55f,
                    gravity = 0.85f, radius = 0.15f, up = true, lifeVar = 0.3f, speedVar = 0.45f,
                });
                EmitP(fx, V(x, RIVER_SURF + 0.04f, z), 1, new ClashEmit { color = C(0.9f, 0.96f, 1), alpha = 0.55f * k, life = 0.5f, speed = 0, size = 0.6f, sizeEnd = 2.2f, radius = 0.05f });
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
                EmitP(fx, V(r.p1.x, RIVER_SURF + 0.02f, r.p1.z), 5, new ClashEmit
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
            if (hud != null) hud.Flash(new Color(1, 244 / 255f, 214 / 255f, 0.32f), 0.35f);
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
            if (hud != null) hud.Loss(ch.si, ch.amount);
            // 世界坐标飘字：在该部队中心上方
            float x = 0; int n = 0;
            foreach (var s in A.soldiers) if (!(t >= s.fallT + 0.6f)) { float ox, oz; SoldierXZ(A, s, t, out ox, out oz); x += ox; n++; }
            x = n > 0 ? x / n : A.xFront;
            int k = chunks.Count(c => c.si == ch.si && c.t < ch.t);
            x += (k % 2 == 1 ? 0.6f : -0.4f) * A.dir;
            // 两军接战后中心会挤在一起：各自的飘字保持在接敌线本方一侧，免得数字叠在一起
            float line = ContactLine(), gap = 1.35f;
            x = A.dir > 0 ? Mathf.Min(x, line - gap) : Mathf.Max(x, line + gap);
            if (hud != null) hud.Pop(V(x, 2.0f + (k % 3) * 0.32f + ch.si * 0.18f, ZC), "−" + ch.amount, ch.kind == "special");
        }
        void OnRout(ClashArmy A)
        {
            ClashUtil.Sfx("lose", 0.3f);
            if (hud != null) hud.Stamp(A.info.name + "部", "溃 散", false);
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
            if (hud != null) hud.Flash(new Color(col.r, col.g, col.b, 1), 0.45f);
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
        // 斩光（第三版修订）：月牙形弧（外缘圆、内缘向两端收拢），径向 / 沿弧两端渐隐的柔边贴图，加色混合；
        // 弧面正对镜头（不再侧对镜头成一堵半透明的墙），沿弧撒火花
        void Slash(Vector3 pos, Color col)
        {
            // three Object3D.lookAt：局部 +z 指向镜头（相机位置换回 three 坐标）
            var camP = cam != null ? ClashUtil.Three(cam.transform.position) : pos + V(0, 0, 1);
            var fwd = camP - pos;
            Quaternion bas = fwd.sqrMagnitude > 1e-12f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : Quaternion.identity;
            var specs = new[] { new Vector4(1.5f, 3.3f, 0.4f, 0.98f), new Vector4(2.1f, 3.0f, 0.9f, 1f), new Vector4(2.6f, 2.9f, 1.0f, 1.05f) };
            var wids = new[] { 1.0f, 0.85f, 0.7f };
            for (int i = 0; i < 3; i++)
            {
                var sp = specs[i];
                var c = i == 2 ? C(1, 1, 0.95f) : col;
                var mesh = ClashGeo.SlashGeo(sp.x, sp.y, wids[i]);
                var mat = FxMat(new Color(c.r, c.g, c.b, sp.z), ClashGeo.SlashTexture(), 3015);
                var f = new ClashFx { mesh = mesh, mat = mat, t0 = t, life = 0.46f };
                float kk = sp.w;
                f.fn = u =>
                {
                    float s = 0.55f + ClashUtil.Eo(u) * 0.75f * kk;
                    var q = bas * Quaternion.AngleAxis((-0.35f - u * 0.9f) * Mathf.Rad2Deg, Vector3.forward);
                    f.m3 = Matrix4x4.TRS(pos, q, new Vector3(s, s, 1));
                    var cc = f.mat.color; cc.a = (1 - u * u) * (kk > 1 ? 1.0f : kk < 1 ? 0.4f : 0.9f); f.mat.color = cc;
                };
                f.m3 = Matrix4x4.TRS(pos, bas * Quaternion.AngleAxis(-0.35f * Mathf.Rad2Deg, Vector3.forward), Vector3.one);
                temp.Add(f);
            }
            // 沿弧的火花
            Vector3 right = bas * Vector3.right, up = bas * Vector3.up;
            for (int i = 0; i < 5; i++)
            {
                float a = -0.8f + i * 0.4f - 0.35f, r = 2.4f;
                Sparks(pos + right * (Mathf.Cos(a) * r * 0.9f) + up * (Mathf.Sin(a) * r * 0.9f), 4, Art.Shade(col, 0.5f));
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
                // 折线按 three 坐标求出，建网格时换成 Unity 坐标（= 网页版第三版 beam(V(x, y, z), …)，雷落在落点上）
                ClashGeo.Beam(mb, ClashUtil.P(x, y, z), ClashUtil.P(nx, ny, nz), 0.12f, 0.1f, Color.white);
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
        internal void CameraPose(float tt)
        {
            if (cam == null) return;
            // 注视点比两军略高一点：画面上方约 1/6 被 HUD 占去，让军阵落在 HUD 之下的画面中部
            var K = new[]
            {
                new[] { 0f, -1.1f, 4.0f, 18.4f, 0.2f, 2.05f, -0.8f },
                new[] { 0.7f, -0.4f, 2.55f, 15.0f, 0, 2.1f, -0.8f },
                new[] { Charge0, -0.3f, 2.3f, 14.0f, 0, 1.88f, -0.8f },
                // 第三版修订：接敌到混战时机位更低、更近（士兵占满画面中下部，不再是远处的一条细带）
                new[] { Contact, xc * 0.45f + 0.2f, 1.5f, 9.8f, xc * 0.5f, 1.22f, -0.8f },
                new[] { Melee1, xc * 0.45f + 0.5f, 1.42f, 9.4f, xc * 0.5f + 0.2f, 1.16f, -0.8f },
                new[] { Regroup1, xc * 0.3f + 0.2f, 2.5f, 13.8f, xc * 0.3f, 2.02f, -0.8f },
            };
            int i = 0;
            while (i < K.Length - 2 && tt > K[i + 1][0]) i++;
            var a = K[i]; var b = K[i + 1];
            float u = Sm(Seg(tt, a[0], b[0]));
            float lx0 = Lerp(a[4], b[4], u), ly = Lerp(a[5], b[5], u), lz = Lerp(a[6], b[6], u);
            float dx = Lerp(a[1], b[1], u) - lx0, dy = Lerp(a[2], b[2], u) - ly, dz = Lerp(a[3], b[3], u) - lz;
            float aspect = cam.aspect > 0 ? cam.aspect : 1.78f;
            // 横向取景：注视点向两员武将（含帅旗）的中点靠一半；施展必杀时再偏向攻方武将
            float el, er, egl, egr;
            ArmyExtent(tt, out el, out er, out egl, out egr);
            float lx = Lerp(lx0, (el + er) / 2, 0.5f);
            if (special != null) lx = Lerp(lx, egl, 0.22f * ClashUtil.Trap(tt, Charge0 - 0.3f, Melee1, 0.4f));
            // 竖屏：拉远之后再抬高机位俯视，压低天空、让纵深铺开
            float kP = Clamp((1.15f - aspect) / 0.6f, 0, 1);
            // 竖屏（画面很窄）：冲锋到混战期间镜头推向接敌线，只保证前两排在画面内（两端的弓手、武将与帅旗可出画，头像与姓名在上方 HUD 里）
            float wP0 = kP * Sm(ClashUtil.Trap(tt, Charge0 + 0.35f, Regroup1 + 0.35f, 0.7f));
            // 横屏同样在混战时推近：只保证前几排在画面内（两端的武将与帅旗可出画，头像在 HUD 里）
            float wP = Mathf.Max(wP0, (1 - kP) * 0.62f * Sm(ClashUtil.Trap(tt, Charge0 + 0.3f, Regroup1 + 0.1f, 0.75f)));
            if (wP > 0) lx = Lerp(lx, xc, wP * 0.8f);
            // 推近不得把两军（尤其两端的武将与帅旗）推出画面：按实际军阵外缘求最小拉远倍数（左右各留约 4%）
            float tanH = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2) * aspect * 0.92f;
            float need = ViewNeed(tt, lx, lz, dz, tanH, wP);
            float ft = Mathf.Max(Lerp(fit, 1, wP), need);
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * ft;
            float px = lx + dx * ft, py = ly + dy * ft + kP * dist * 0.27f * (1 - 0.5f * wP0), pz = lz + dz * ft;
            if (shake > 0.001f)
            {
                float k = shake * 0.11f;
                px += Mathf.Sin(real * 71) * k;
                py += Mathf.Sin(real * 57 + 1.3f) * k * 0.7f;
            }
            // 竖屏推近时注视点降到士兵胸口附近（远景时高处的注视点到了近处会把军阵压到画面底部）
            float lookY = ly - kP * 0.35f - wP0 * 0.95f;
            var tr = cam.transform;
            tr.position = Clash.Origin + ClashUtil.P(px, py, pz);
            tr.LookAt(Clash.Origin + ClashUtil.P(lx, lookY, lz), Vector3.up);
            // 城门 / 本城：城门楼屋脊不得顶进上方 HUD——投影屋脊，若高于 HUD 下沿就抬高注视点，最多抬 1.6
            // 竖屏推近混战时不做（近景里城楼本就出画，不再是「无顶红楼」）
            if ((kind == "gate" || kind == "castle") && wP < 0.999f)
            {
                float hb = hud != null ? hud.BarBottomNdc() : 1;
                float upMax = 1.6f * (1 - wP);
                if (hb < 0.98f)
                {
                    float tanV = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
                    float dl = Mathf.Sqrt((px - lx) * (px - lx) + (py - lookY) * (py - lookY) + (pz - lz) * (pz - lz));
                    float up = 0;
                    var ridge = Clash.Origin + ClashUtil.P((float)ClashTerrain.GATE_X, 6.2f, (float)ClashTerrain.WALL_Z - 0.7f);
                    for (int it = 0; it < 4; it++)
                    {
                        float y = cam.WorldToViewportPoint(ridge).y * 2 - 1;
                        float over = y - (hb - 0.03f);
                        if (over <= 0.004f || up >= upMax) break;
                        up = Mathf.Min(upMax, up + over * tanV * dl * 0.9f);
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
            lastDist = dist;
        }
        internal float lastDist;
        // 武将 x（与 PoseGeneral 同一公式；镜头取景用）
        float GeneralX(ClashArmy A, float tt)
        {
            var G = A.general; int dir = A.dir;
            float x = G.x0 - dir * ENTER_DX * (1 - ClashUtil.Eo(Seg(tt, G.ed, March1 + 0.05f)));
            float uc = Seg(tt, Charge0 + 0.1f, Contact + 0.1f);
            x += dir * A.genDX * uc * uc;
            if (A.routed) x -= dir * 9 * Mathf.Pow(Seg(tt, Melee1 + 0.12f, Regroup1 + 0.3f), 1.4f);
            else if (!A.cheer) x -= dir * GBACK * ClashUtil.Eio(Seg(tt, Melee1 + 0.1f, Regroup1));
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
        // wCore > 0（混战推近）：在「全军」与「前两排」两种取景需求之间插值
        float ViewNeed(float tt, float cx, float lz, float dz, float tanH, float wCore)
        {
            if (!(dz > 0.1f) || !(tanH > 0.01f)) return 1;
            float need = 0, core = 0;
            Func<float, float, float> nOf = (x, z) => (Mathf.Abs(x - cx) / tanH + (z - lz)) / dz;
            foreach (var A in armies)
            {
                float ft = FrameT(A, tt), gx = GeneralX(A, ft), gz = A.general.z0;
                need = Mathf.Max(need, nOf(gx - A.dir * 1.75f, gz - 0.25f));   // 帅旗（含旗面飘动）在武将身后
                need = Mathf.Max(need, nOf(gx + A.dir * 1.0f, gz));             // 马头
                foreach (var s in A.soldiers)
                {
                    float ox, oz; SoldierXZ(A, s, ft, out ox, out oz);
                    float n = nOf(ox - A.dir * 0.3f, oz + 0.25f);
                    if (n > need) need = n;
                    if (s.file <= 1 && s.role != "arch" && n > core) core = n;
                }
            }
            return wCore > 0 ? Lerp(need, core * 1.05f, wCore) : need;
        }
        internal void Resize()
        {
            lastW = Screen.width; lastH = Screen.height;
            float aspect = cam != null && cam.aspect > 0 ? cam.aspect : lastW / (float)Mathf.Max(1, lastH);
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
            Shader.SetGlobalFloat("_SgClashTime", real);
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
            if (f.mesh != null && f.ownMesh) UnityEngine.Object.Destroy(f.mesh);
            if (f.mat != null) UnityEngine.Object.Destroy(f.mat);
            f.mesh = null; f.mat = null;
        }
        // 推进时间轴并触发途经的事件（fx = false 时只更新状态，不放特效）
        internal void SetTime(float nt, bool withFx)
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
        void DrawU(Mesh mesh, Matrix4x4 m3, Material mat, Color tint, bool shadows = true)
        {
            var m = Matrix4x4.Translate(Clash.Origin) * ClashUtil.ToU(m3);
            mpb.SetColor(ColorId, tint);
            Graphics.DrawMesh(mesh, m, mat, Clash.Layer, cam, 0, mpb, shadows, true, false);
        }
        // 每帧提交的绘制：箭、草丛 / 野花（实例化）、临时特效、粒子（人物、士兵群、地形、旗面是物体，自己绘制）
        public void Draw()
        {
            if (disposed || !shown || cam == null || armies == null) return;
            foreach (var r in arrows) if (r.visible) DrawU(arrowMesh, r.m3, arrowMat, r.spec && special != null ? arrowGlow : Color.white, true);
            DrawInstanced(terrainOut.tuftMesh, tuftW, tuftC, tuftMpb);
            DrawInstanced(terrainOut.flowerMesh, flowerW, flowerC, flowerMpb);
            foreach (var f in temp)
                if (f.mesh != null && f.mat != null)
                    Graphics.DrawMesh(f.mesh, Matrix4x4.Translate(Clash.Origin) * ClashUtil.ToU(f.m3), f.mat, Clash.Layer, cam, 0, null, false, false, false);
            var tr = cam.transform;
            // 面向镜头的方片：ClashPool.Build 把粒子的 three 坐标换成 Unity 坐标后，沿镜头的 right / up（Unity 世界方向）展开
            foreach (var p in new[] { dust, fx, glow })
            {
                if (p.Count == 0) continue;
                p.Build(tr.right, tr.up);
                Graphics.DrawMesh(p.mesh, Matrix4x4.Translate(Clash.Origin), p.mat, Clash.Layer, cam, 0, null, false, false, false);
            }
        }
        // 草丛 / 野花：一组一次实例化绘制（每批 ≤ 1023；不投影、接受阴影）；不支持实例化时逐个绘制
        void DrawInstanced(Mesh mesh, Matrix4x4[] w, Vector4[] cols, MaterialPropertyBlock block)
        {
            if (mesh == null || w == null || w.Length == 0 || grassMat == null) return;
            if (SystemInfo.supportsInstancing)
            {
                if (w.Length <= 1023) { Graphics.DrawMeshInstanced(mesh, 0, grassMat, w, w.Length, block, ShadowCastingMode.Off, true, Clash.Layer, cam); return; }
                for (int s = 0; s < w.Length; s += 1023)
                {
                    int n = Math.Min(1023, w.Length - s);
                    var mb = new Matrix4x4[n]; var cb = new Vector4[n];
                    Array.Copy(w, s, mb, 0, n); Array.Copy(cols, s, cb, 0, n);
                    var pb = new MaterialPropertyBlock(); pb.SetVectorArray("_InstColor", cb);
                    Graphics.DrawMeshInstanced(mesh, 0, grassMat, mb, n, pb, ShadowCastingMode.Off, true, Clash.Layer, cam);
                }
                return;
            }
            for (int i = 0; i < w.Length; i++)
            {
                mpb.SetVector("_InstColor", cols[i]);
                Graphics.DrawMesh(mesh, w[i], grassMat, Clash.Layer, cam, 0, mpb, false, true, false);
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
            if (hud != null)
            {
                hud.Final();
                foreach (var a in armies) if (a.routed) hud.Stamp(a.info.name + "部", "溃 散", true);
            }
            state = "hold";
            holdUntil = real + SKIP_HOLD;
            holdWall = Time.realtimeSinceStartup + SKIP_HOLD;
        }
        void Finish()
        {
            if (state == "out" || state == "done") return;
            state = "out";
            if (hud == null) { state = "done"; Dispose(); return; }
            hud.Final();
            hud.FadeOut(FADE_OUT, () => { state = "done"; Dispose(); });
        }

        // 测试用：模型与预算统计（= JS stats()）
        internal string Stats()
        {
            int figTris = 0;
            foreach (var f in figs) figTris += f.Triangles;
            foreach (var h0 in horses) figTris += h0.Triangles;
            var c = crowd != null ? crowd.Stats() : null;
            return "crowd " + (c != null ? c.soldiers + "/" + c.groups + "/" + c.drawCalls + "/" + c.triangles : "-") + " figs " + (figs.Count + horses.Count) + " figTris " + figTris + " groundTris " + (terrainOut != null ? terrainOut.groundTris : 0);
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
            foreach (var kv in lightMasks) if (kv.Key != null) kv.Key.cullingMask = kv.Value;
            lightMasks.Clear();
            if (ambSaved)
            {
                RenderSettings.ambientMode = ambMode0; RenderSettings.ambientSkyColor = ambSky0; RenderSettings.ambientEquatorColor = ambEq0; RenderSettings.ambientGroundColor = ambGnd0;
                try { DynamicGI.UpdateEnvironment(); } catch (Exception) { /* 忽略 */ }
                ambSaved = false;
            }
            if (fogSaved)
            {
                RenderSettings.fog = fog0; RenderSettings.fogMode = fogMode0; RenderSettings.fogColor = fogColor0;
                RenderSettings.fogStartDistance = fogStart0; RenderSettings.fogEndDistance = fogEnd0;
                QualitySettings.shadowDistance = shadowDist0;
                fogSaved = false;
            }
#if !CLASH_HEADLESS
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i].Key;
                if (g == null) continue;
                if (groups[i].Value) UnityEngine.Object.Destroy(g);
                else { g.alpha = groupAlpha[i]; g.blocksRaycasts = groupBlocks[i]; g.interactable = groupInter[i]; }
            }
            groups.Clear();
            if (rigSaved && Game.I != null && Game.I.Rig != null) Game.I.Rig.InputEnabled = rigInput0;
#endif
            rigSaved = false;
            // 释放：人物与战马（模板与面部贴图的引用）、士兵群、网格 / 材质 / 纹理
            if (hud != null) { hud.Dispose(); hud = null; }
            foreach (var f in temp) DestroyFx(f);
            temp.Clear();
            if (crowd != null) { try { crowd.Dispose(); } catch (Exception e) { Debug.LogException(e); } crowd = null; }
            foreach (var hz in horses) { try { hz.Dispose(); } catch (Exception e) { Debug.LogException(e); } }
            foreach (var f in figs) { try { f.Dispose(); } catch (Exception e) { Debug.LogException(e); } }
            horses.Clear(); figs.Clear();
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

#if !CLASH_HEADLESS
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
        // 「布阵中」的转圈：2px 圆环 rgba(243,201,105,.25)，上边（±45°）为 #f3c969（= CSS border-top-color）；每次新建，由 HUD 释放
        public static Texture2D Spinner()
        {
            const int n = 64; var t = Tex(n, n);
            float r1 = n / 2f - 0.5f, r0 = r1 * (1 - 0.24f);       // 1.1em 直径里 2px 边（15px 字号时约占半径的 24%）
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n / 2f, dy = y + 0.5f - n / 2f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float cov = Mathf.Clamp01(r1 + 0.5f - d) * Mathf.Clamp01(d - (r0 - 0.5f));
                    float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;          // 0 = 正上（纹理 y 向上）
                    float top = Mathf.Clamp01((45 - Mathf.Abs(ang)) / 3f + 0.5f);
                    t.SetPixel(x, y, new Color(243 / 255f, 201 / 255f, 105 / 255f, cov * Mathf.Lerp(0.25f, 1, top)));
                }
            t.Apply();
            return t;
        }
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
        // 黑幕上的「布阵中…」提示（第三版：准备超过约 0.3 秒时淡入）
        RectTransform waitRt, spinRt; Text waitText; ClashSpacing waitSp; Texture2D spinTex;
        bool waitOn; float waitA;

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
            waitRt = ClashUI.Rect("Wait", fadeImg.transform);
            spinTex = ClashSprites.Spinner(); own.Add(spinTex);
            spinRt = ClashUI.Raw(waitRt, spinTex, Color.white, "Spin").rectTransform;
            waitText = ClashUI.Label(waitRt, "布阵中…", kai, 15, GOLDC, TextAnchor.MiddleLeft);
            waitSp = waitText.gameObject.AddComponent<ClashSpacing>();
            SetGroupAlpha(waitRt, 0);
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
            // 「布阵中…」：屏幕正中，转圈 1.1em + 间隔 .7em + 字（字距 .3em）
            float wfs = Mathf.Max(15, u * 1.5f);
            waitText.fontSize = Mathf.RoundToInt(wfs); waitSp.Set(wfs * 0.3f);
            float tw = ClashUI.Width(waitText, wfs * 0.3f) + wfs * 0.3f, sp = wfs * 1.1f, ww = sp + wfs * 0.7f + tw, wh = wfs * 1.3f;
            ClashUI.PlaceC(waitRt, vw / 2, vh / 2, ww, wh);
            ClashUI.PlaceC(spinRt, sp / 2, wh / 2, sp, sp);
            ClashUI.Place(waitText.rectTransform, sp + wfs * 0.7f, 0, tw + 4, wh);
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
        // 黑幕上的「布阵中」提示
        public void Wait(bool on) { waitOn = on; }
        public void FadeIn(float sec)
        {
            Wait(false);
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
            // 「布阵中」：opacity .25 秒过渡到 .92，随黑幕一起显隐；转圈 .9 秒一周（顺时针）
            waitA = Mathf.MoveTowards(waitA, waitOn ? 0.92f : 0, dt / 0.25f * 0.92f);
            SetGroupAlpha(waitRt, waitA * fadeImg.color.a);
            if (waitA > 0) spinRt.localEulerAngles = new Vector3(0, 0, spinRt.localEulerAngles.z - 360 * dt / 0.9f);
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
#else
    // 无头测试（Tests~/clash，CLASH_HEADLESS）：界面（HUD、特写）换成同名的空实现，场景代码不变
    internal sealed class ClashHud
    {
        public static float BarNdc = 1;        // 测试设定：HUD 顶栏下沿的 NDC（网页版 hudLineNdc 的值）
        public ClashHud(ClashScene sc) { }
        public void FadeBlack(float sec) { }
        public void Wait(bool on) { }
        public void FadeIn(float sec) { }
        public void FadeOut(float sec, Action done) { if (done != null) done(); }
        public void Flash(Color c, float a) { }
        public void Loss(int si, int amount) { }
        public void Pop(Vector3 world, string text, bool big) { }
        public void Stamp(string sub, string text, bool instant) { }
        public void Final() { }
        public float BarBottomNdc() { return BarNdc; }
        public void Update(float dt, float popDt) { }
        public void Layout() { }
        public void Dispose() { }
    }
    internal sealed class CutInView
    {
        public bool Ended = true;
        public Action onEnd;
        public static int PortraitPx() { return 64; }
        public static CutInView Open(General g, string moveName, Color color, int side, string cry, float speed) { return null; }
        public void Skip() { }
        public void End() { }
    }
#endif
}
