// ==========================================================================
// 三国志II 霸王的大陆 · 单挑格斗（第二版 DESIGN-V2 §4E；网页版 js/duel-game.js 的移植）
//
// 单挑 = 一场横版 2.5D 格斗：独立的三维场景（自己的相机，层 30），两名程序化建模的低多边形武将，
// 程序化关键帧动画，键盘 / 多点触控操作，攻击力与伤害取决于 武力。
//
// 公开接口（UNITY-V2 §5）
//   IEnumerator DuelGame.Play(General a, int sideA, Color colA, General b, int sideB, Color colB, int playerSide, Terrain terrain, Action<DuelGame.DuelResult> done)
//     a = 挑战者（画面左侧），b = 应战者（画面右侧）；sideA / sideB 为两人在战场上的阵营（仅记录）。
//     playerSide：玩家操作哪一方 —— 0 = a，1 = b（同网页版：a.side == 玩家阵营 ? 0 : 1）；−1 = 电脑对电脑（观战，可跳过）。
//     done(DuelResult)：winner 0 = a 胜 / 1 = b 胜；kind "ko" | "time" | "skip"；hpA、hpB；
//       log 为每次命中的 "who|dmg|hpA|hpB|move|guarded|t"；rounds 为同样内容的 DuelRound（可直接交给 BattleModel.DuelFinish）。
//   DuelGame.Auto     测试用：true 时玩家一方也由电脑操作（仍显示操作界面）
//   DuelGame.Speed    测试用：时间倍率（缺省 1）
//   DuelGame.Simulate(a, b) → DuelResult   不渲染、电脑对电脑快速模拟一整场（同一套规则；委任 / 出错时的兜底）
//   DuelGame.MusicAfter  结束后切回的乐曲（null = 不切；由调用方设定，如 "battle" / "battle-defend"）
//
// 结构：DuelSim.cs（纯逻辑，固定 60Hz 步长）+ DuelAI + DuelModels.cs / DuelAnim.cs（武将模型与动画）+
//       DuelArena.cs（场地与特效）+ DuelHud.cs（界面与输入）+ 本文件（流程、镜头、事件表现、音效）。
// 规则相关的随机数走 UnityEngine.Random（DuelSim.Rnd）；纯装饰用 SeededRandom。
// ==========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    public static class DuelGame
    {
        public class DuelResult
        {
            public int winner;
            public string kind;
            public int hpA, hpB;
            public List<string> log;
            public List<DuelRound> rounds;    // 同 log（BattleModel.DuelFinish 的 rounds）
            public double time;
        }
        public static bool Auto;
        public static float Speed = 1;
        public static bool Enabled = true;
        public static string MusicAfter;
        public static bool Active { get { return DuelScreen.current != null; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitRandom() { DuelSim.Rnd = () => UnityEngine.Random.value; }

        public static DuelResult ToResult(DuelOutcome o)
        {
            var r = new DuelResult { winner = o.winner, kind = o.kind, hpA = o.hpA, hpB = o.hpB, time = o.time, log = new List<string>(), rounds = new List<DuelRound>() };
            foreach (var e in o.log) { r.log.Add(e.ToString()); r.rounds.Add(new DuelRound { who = e.who, dmg = e.dmg, hpA = e.hpA, hpB = e.hpB }); }
            return r;
        }
        public static DuelResult Simulate(General a, General b)
        {
            DuelSim.Rnd = () => UnityEngine.Random.value;
            return ToResult(DuelSim.Simulate(a, b));
        }

        public static IEnumerator Play(General a, int sideA, Color colA, General b, int sideB, Color colB, int playerSide, Terrain terrain, Action<DuelResult> done)
        {
            while (DuelScreen.current != null) yield return null;
            DuelSim.Rnd = () => UnityEngine.Random.value;
            if (UIKit.Root == null)
            {   // 界面尚未初始化（不应发生）：直接用无画面模拟分出胜负
                var r0 = Simulate(a, b);
                if (done != null) done(r0);
                yield break;
            }
            var go = new GameObject("DuelScreen");
            var s = go.AddComponent<DuelScreen>();
            s.Setup(a, colA, b, colB, playerSide == 0 || playerSide == 1 ? playerSide : -1, terrain);
            while (!s.finished) yield return null;
            var res = s.result ?? Simulate(a, b);
            UnityEngine.Object.Destroy(go);
            if (done != null) done(res);
        }
    }

    // 一场单挑的画面与流程
    public sealed class DuelScreen : MonoBehaviour
    {
        public const int Layer = 30;
        internal static DuelScreen current;
        const float DT = (float)DuelSim.DT;

        // ---- 设定
        public General genA, genB;
        public Color colA, colB;
        public DuelLook lookA, lookB;
        public DuelSpecialInfo spA, spB;
        public int player = -1;
        public bool isTouch, hybrid;
        public DuelInput input;
        public DuelSim sim;
        string kind;
        // ---- 画面
        DuelHud hud;
        GameObject world;
        Camera cam; Light keyL, rimL; Material skyMat, envMat;
        DuelArena arena;
        DuelWarrior[] war;
        DuelParticles sparks, dust, motes;
        readonly List<DuelTrail> trails = new List<DuelTrail>();
        sealed class Fx { public GameObject go; public Material mat; public float t = 1, life, size, rot; }
        readonly List<Fx> stars = new List<Fx>(), rings = new List<Fx>();
        Mesh quadMesh; Texture2D starTex;
        sealed class ArrowObj { public GameObject go; public MeshFilter mf; public int id; }
        readonly List<ArrowObj> arrows = new List<ArrowObj>();
        Mesh[] arrowMesh;
        DuelAudio audio;
        SeededRandom fxR, moteR;
        // ---- 状态
        float slow = 1, slowT, acc, realT, shake, introCam = 1;
        sealed class Zoom { public int who; public float until; public bool ko; }
        Zoom zoom;
        bool snapCam, skipped, built;
        readonly float[] trailOn = new float[2];
        Vector3 camPos = new Vector3(0, 1.8f, 10), camLook = new Vector3(0, 1, 0);   // three 坐标
        bool pauseFlag, appPaused, inWas; float resumeAt;
        public bool finished;
        public DuelGame.DuelResult result;
        bool failed; int errors;
        // ---- 复原
        readonly List<Camera> camsOff = new List<Camera>();
        readonly List<KeyValuePair<Light, int>> lightMasks = new List<KeyValuePair<Light, int>>();
        readonly List<KeyValuePair<CanvasGroup, float>> uiHidden = new List<KeyValuePair<CanvasGroup, float>>();
        readonly List<CanvasGroup> uiAdded = new List<CanvasGroup>();
        bool rsSaved, rsFog, rigWas; float rsShadowDist; FogMode rsFogMode; Color rsFogColor, rsSky, rsEq, rsGround; float rsFogStart, rsFogEnd; UnityEngine.Rendering.AmbientMode rsAmbMode;

        static Color C(string h) { return DuelXf.C(h); }

        public void Setup(General a, Color ca, General b, Color cb, int playerSide, Terrain terrain)
        {
            current = this;
            genA = a ?? new General { name = "甲", war = 70 };
            genB = b ?? new General { name = "乙", war = 70 };
            colA = ca; colB = cb; colA.a = 1; colB.a = 1;
            // 双方势力色过于接近时，应战者改用对比色，免得分不清敌我
            Func<Color, Color, float> dist = (x, y) => Mathf.Sqrt((x.r - y.r) * (x.r - y.r) + (x.g - y.g) * (x.g - y.g) + (x.b - y.b) * (x.b - y.b));
            if (dist(colA, colB) < 0.22f)
            {
                Color red = C("#c8382c"), blue = C("#3a78c8");
                colB = dist(colA, red) > dist(colA, blue) ? red : blue;
            }
            lookA = DuelLooks.LookOf(genA, null);
            lookB = DuelLooks.LookOf(genB, null);
            spA = DuelLooks.SpecialOf(genA, lookA);
            spB = DuelLooks.SpecialOf(genB, lookB);
            player = playerSide;
            // 触摸屏笔记本：既有触摸也有精确指针（鼠标 / 触控板）→ 同时给键盘说明与触摸按键
            isTouch = Input.touchSupported || Application.isMobilePlatform;
            hybrid = isTouch && !Application.isMobilePlatform && Input.mousePresent;
            kind = DuelArena.TerrainKind(terrain);
            sim = new DuelSim(genA, genB, lookA, lookB);
            // 操作者：玩家一方为键盘 / 触摸；自动测试（DuelGame.Auto）时仍显示操作界面，但交给电脑
            input = player >= 0 ? new DuelInput() : null;
            if (input != null) input.SetEnabled(false);      // 开场 / 操作说明期间不接受出招（开战时才启用）
            foreach (var f in sim.f) f.ctrl = player == f.idx && !DuelGame.Auto ? (IDuelCtrl)input : new DuelAI(f);
            StartCoroutine(Guard(Flow()));
        }

        void OnApplicationPause(bool p) { appPaused = p; }
        void OnDestroy() { if (current == this) current = null; Cleanup(); if (hud != null) { hud.Dispose(); hud = null; } }

        // 流程协程的保护层：出错时记录并改用无画面结算，保证 done 一定被调用
        IEnumerator Guard(IEnumerator inner)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (failed || !inner.MoveNext()) break;
                    cur = inner.Current;
                }
                catch (Exception e) { Debug.LogError("单挑画面出错，改为直接结算：" + e); failed = true; break; }
                yield return cur;
            }
            if (failed)
            {
                try { if (!sim.hasResult) { foreach (var f in sim.f) f.ctrl = new DuelAI(f); sim.RunToEnd(); } result = DuelGame.ToResult(sim.Outcome()); }
                catch (Exception e) { Debug.LogError(e); result = null; }
                Cleanup();
                if (hud != null) { hud.Dispose(); hud = null; }
            }
            if (current == this) current = null;
            finished = true;
        }

        float Wait(float sec) { return realT + sec / Mathf.Max(0.05f, DuelGame.Speed); }
        static bool AnyPress()
        {
            if (Input.anyKeyDown) return true;
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
        }
        static bool AnyKeyNoMouse()
        {
            if (!Input.anyKeyDown) return false;
            return !(Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2));
        }
        static bool AnyTap()
        {
            if (Input.GetMouseButtonDown(0)) return true;
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return true;
            return false;
        }
        static Vector2 TapPos()
        {
            for (int i = 0; i < Input.touchCount; i++) if (Input.GetTouch(i).phase == TouchPhase.Began) return Input.GetTouch(i).position;
            return Input.mousePosition;
        }

        IEnumerator Flow()
        {
            hud = new DuelHud(this);
            hud.Build();
            hud.SetFade(1);
            float until = realT + 0.26f;
            while (realT < until) yield return null;
            hud.Preload();
            hud.BuildHud();
            yield return null;
            BuildScene();
            yield return null;
            EnterScreen();
            try { Sfx.Music("duel"); } catch (Exception) { /* 无音频 */ }
            yield return null;
            hud.SetFade(0);
            // ---- 开场亮相：镜头推近、两将挥舞兵器、VS 头像
            introCam = 0; snapCam = true;
            foreach (var w in war) w.intro = 0;
            var closeVS = hud.IntroVS();
            Sfx.Play("horn", 0.55f);
            float t0 = realT, dur = 1.5f / Mathf.Max(0.05f, DuelGame.Speed);
            while (true)
            {
                introCam = Mathf.Clamp01((realT - t0) / dur);
                if (introCam >= 1 || skipped) break;
                yield return null;
            }
            introCam = 1;
            closeVS();
            foreach (var w in war) w.intro = -1;
            hud.ShowHud(true);
            // ---- 操作说明卡（玩家参与时）
            if (player >= 0 && !skipped)
            {
                var me = player == 0 ? genA : genB; var foe = player == 0 ? genB : genA;
                var closeCard = hud.ControlsCard(player == 0 ? spA : spB, me, foe);
                float autoAt = DuelGame.Auto ? realT + 0.7f / Mathf.Max(0.1f, DuelGame.Speed) : float.MaxValue;
                yield return null;
                while (!AnyPress() && realT < autoAt) yield return null;
                closeCard();
            }
            if (!skipped)
            {
                hud.Announce("开　战", null, null, 0.9f);
                Sfx.Play("duel", 0.9f);
                until = Wait(0.45f);
                while (realT < until && !skipped) yield return null;
                hud.ShowControls(true);
                if (input != null) input.SetEnabled(true);
                sim.running = true;
            }
            while (!skipped && !sim.hasResult) yield return null;
            hud.ShowControls(false);
            // ---- 结束演出
            if (!skipped) while (!skipped && !(sim.endT > (sim.resultKind == "ko" ? 1.6 : 1.1))) yield return null;
            int win = sim.resultWinner;
            var gen = win == 0 ? genA : genB; var loser = win == 0 ? genB : genA;
            bool lose = player >= 0 && !DuelGame.Auto && player != win;
            bool perfect = sim.resultKind == "ko" && sim.f[win].hp >= 100;
            string sub = skipped ? "（观战跳过）" : sim.resultKind == "ko" ? (perfect ? "完胜 · 毫发无伤击破" : "击破") + loser.name : "时间到 · 体力占优";
            hud.WinPanel(lose, gen, sub, DuelLooks.HashStr(gen.name));
            Sfx.Play(lose ? "lose" : "win", 0.6f);
            until = Wait(0.8f);
            while (realT < until && !skipped) yield return null;
            float timeout = realT + (player < 0 || DuelGame.Auto ? 1.6f : 3.2f) / Mathf.Max(0.1f, DuelGame.Speed);
            yield return null;
            while (!AnyPress() && realT < timeout) yield return null;
            hud.SetFade(1);
            until = realT + 0.32f;
            while (realT < until) yield return null;
            result = DuelGame.ToResult(sim.Outcome(skipped ? "skip" : null));
            // 拆除画面，再淡出遮罩
            Cleanup();
            hud.DisposeKeepFade();
            try { if (!string.IsNullOrEmpty(DuelGame.MusicAfter)) Sfx.Music(DuelGame.MusicAfter); } catch (Exception) { /* 无音频 */ }
            hud.SetFade(0);
            until = realT + 0.35f;
            while (realT < until) yield return null;
            hud.DisposeFade();
            hud = null;
        }

        // ------------------------------------------------------------ 场景 --
        void BuildScene()
        {
            world = new GameObject("DuelWorld"); world.layer = Layer;
            world.transform.SetParent(transform, false);
            var pal = DuelArena.Palettes[kind];
            envMat = Art.NewLowPoly();
            arena = new DuelArena(world.transform, Layer, kind, colA, colB, string.IsNullOrEmpty(genA.name) ? "甲" : genA.name.Substring(0, 1),
                string.IsNullOrEmpty(genB.name) ? "乙" : genB.name.Substring(0, 1), DuelLooks.HashStr(genA.name + genB.name), envMat);
            // 相机（层 30），天空盒用 Sky 着色器的副本
            var cgo = new GameObject("DuelCamera"); cgo.layer = Layer; cgo.transform.SetParent(transform, false);
            cam = cgo.AddComponent<Camera>();
            cam.fieldOfView = 30; cam.nearClipPlane = 0.3f; cam.farClipPlane = 400;
            cam.cullingMask = 1 << Layer; cam.depth = 50;
            cam.clearFlags = CameraClearFlags.Skybox; cam.backgroundColor = C(pal.fog);
            skyMat = new Material(Art.Sky);
            skyMat.SetColor("_Top", C(pal.top)); skyMat.SetColor("_Horizon", C(pal.hor)); skyMat.SetColor("_Bottom", C(pal.low));
            skyMat.SetColor("_SunColor", C(pal.sun));
            var sd = DuelXf.P(pal.sunDir).normalized; skyMat.SetVector("_SunDir", new Vector4(sd.x, sd.y, sd.z, 0));
            cgo.AddComponent<Skybox>().material = skyMat;
            // 灯光：主光（投影）+ 轮廓光；强度按网页版的曝光换算（三维 π·I ↔ Unity I / 0.75）
            keyL = NewLight("DuelKey", C(pal.key), pal.keyI / 0.75f, new Vector3(6, -11, -8), true);
            rimL = NewLight("DuelRim", C(pal.rim), pal.rimI / 0.75f, new Vector3(-5, -4, 9), false);
            // 武将
            war = new[] { new DuelWarrior(sim.f[0], colA, world.transform, Layer), new DuelWarrior(sim.f[1], colB, world.transform, Layer) };
            // 特效
            sparks = new DuelParticles(420, true, 1.0f, 12, world.transform, Layer);
            dust = new DuelParticles(240, false, 1.6f, 11, world.transform, Layer);
            motes = new DuelParticles(110, true, 1.4f, 10, world.transform, Layer);
            for (int i = 0; i < 4; i++) trails.Add(new DuelTrail(12, world.transform, Layer));
            quadMesh = DuelTextures.Quad("DuelQuad");
            starTex = DuelTextures.Star();
            for (int i = 0; i < 6; i++)
            {
                var m = new Material(Art.LoadShader("Additive")) { mainTexture = starTex, renderQueue = 3030 };
                var g = Art.MakeObject("Star", quadMesh, m, world.transform, false); g.layer = Layer; g.SetActive(false);
                stars.Add(new Fx { go = g, mat = m, life = 0.15f, size = 1 });
            }
            for (int i = 0; i < 3; i++)
            {
                var m = new Material(Art.LoadShader("Additive")) { mainTexture = Art.Ring, renderQueue = 3009 };
                var g = Art.MakeObject("Ring", quadMesh, m, world.transform, false); g.layer = Layer; g.SetActive(false);
                g.transform.localRotation = Quaternion.Euler(90, 0, 0);
                rings.Add(new Fx { go = g, mat = m, life = 0.5f, size = 4 });
            }
            // 飞箭（弓将绝技）：小对象池，位置每帧取自 sim.shots
            arrowMesh = new[] { DuelModels.BuildArrow(false).ToMesh("DuelArrow"), DuelModels.BuildArrow(true).ToMesh("DuelArrowFin") };
            for (int i = 0; i < 6; i++)
            {
                var g = Art.MakeObject("Arrow", arrowMesh[0], envMat, world.transform, true); g.layer = Layer; g.SetActive(false);
                arrows.Add(new ArrowObj { go = g, mf = g.GetComponent<MeshFilter>() });
            }
            moteR = new SeededRandom(5);
            for (int i = 0; i < 70; i++) SpawnMote(true);
            fxR = new SeededRandom(17);
            audio = new DuelAudio(gameObject);
            built = true;
        }
        Light NewLight(string name, Color col, float intensity, Vector3 dirThree, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional; l.color = col; l.intensity = intensity;
            l.cullingMask = 1 << Layer;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.shadowStrength = 0.55f; l.shadowBias = 0.04f; l.shadowNormalBias = 0.3f;
            go.transform.rotation = Quaternion.LookRotation(DuelXf.P(dirThree).normalized);
            return l;
        }

        // 进入单挑画面：关掉其他相机、其他灯光不照本层、换上本场的雾与环境光、隐藏下层界面与地图操作
        void EnterScreen()
        {
            foreach (var c in Camera.allCameras) if (c != cam && c.enabled) { c.enabled = false; camsOff.Add(c); }
            foreach (var l in FindObjectsOfType<Light>())
                if (l != keyL && l != rimL) { lightMasks.Add(new KeyValuePair<Light, int>(l, l.cullingMask)); l.cullingMask &= ~(1 << Layer); }
            var pal = arena.pal;
            rsSaved = true;
            rsFog = RenderSettings.fog; rsFogMode = RenderSettings.fogMode; rsFogColor = RenderSettings.fogColor; rsFogStart = RenderSettings.fogStartDistance; rsFogEnd = RenderSettings.fogEndDistance;
            rsAmbMode = RenderSettings.ambientMode; rsSky = RenderSettings.ambientSkyColor; rsEq = RenderSettings.ambientEquatorColor; rsGround = RenderSettings.ambientGroundColor;
            rsShadowDist = QualitySettings.shadowDistance; QualitySettings.shadowDistance = 30;   // 网页版的投影范围只覆盖场地（±7 米）
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogColor = C(pal.fog);
            RenderSettings.fogStartDistance = pal.fogN; RenderSettings.fogEndDistance = pal.fogF;
            float amb = pal.hemiI / 0.56f;
            Color hs = C(pal.hemiS), hg = C(pal.hemiG);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = hs * amb; RenderSettings.ambientEquatorColor = Color.Lerp(hs, hg, 0.5f) * amb; RenderSettings.ambientGroundColor = hg * amb;
            // 单挑期间隐藏同层的其他画面界面（战场顶栏、部队卡、按钮、名牌等）
            foreach (var layer in new[] { UIKit.Screens, UIKit.LabelLayer })
            {
                if (layer == null) continue;
                foreach (Transform ch in layer)
                {
                    var g = ch.GetComponent<CanvasGroup>();
                    if (g == null) { g = ch.gameObject.AddComponent<CanvasGroup>(); uiAdded.Add(g); }
                    uiHidden.Add(new KeyValuePair<CanvasGroup, float>(g, g.alpha));
                    g.alpha = 0;
                }
            }
            var rig = Game.I != null ? Game.I.Rig : null;
            if (rig != null) { rigWas = rig.InputEnabled; rig.InputEnabled = false; }
        }

        void Cleanup()
        {
            built = false;
            foreach (var c in camsOff) if (c != null) c.enabled = true;
            camsOff.Clear();
            foreach (var kv in lightMasks) if (kv.Key != null) kv.Key.cullingMask = kv.Value;
            lightMasks.Clear();
            if (rsSaved)
            {
                rsSaved = false;
                RenderSettings.fog = rsFog; RenderSettings.fogMode = rsFogMode; RenderSettings.fogColor = rsFogColor; RenderSettings.fogStartDistance = rsFogStart; RenderSettings.fogEndDistance = rsFogEnd;
                RenderSettings.ambientMode = rsAmbMode; RenderSettings.ambientSkyColor = rsSky; RenderSettings.ambientEquatorColor = rsEq; RenderSettings.ambientGroundColor = rsGround;
                QualitySettings.shadowDistance = rsShadowDist;
                var rig = Game.I != null ? Game.I.Rig : null;
                if (rig != null) rig.InputEnabled = rigWas;
            }
            foreach (var kv in uiHidden) if (kv.Key != null) kv.Key.alpha = kv.Value;
            uiHidden.Clear();
            foreach (var g in uiAdded) if (g != null) Destroy(g);
            uiAdded.Clear();
            if (war != null) foreach (var w in war) w.Dispose();
            war = null;
            if (arena != null) arena.Dispose(); arena = null;
            foreach (var p in new[] { sparks, dust, motes }) if (p != null) p.Dispose();
            sparks = dust = motes = null;
            foreach (var t in trails) t.Dispose(); trails.Clear();
            foreach (var s in stars) Destroy(s.mat); stars.Clear();
            foreach (var r in rings) Destroy(r.mat); rings.Clear();
            arrows.Clear();
            if (arrowMesh != null) foreach (var m in arrowMesh) Destroy(m);
            arrowMesh = null;
            if (quadMesh != null) Destroy(quadMesh); quadMesh = null;
            if (starTex != null) Destroy(starTex); starTex = null;
            if (skyMat != null) Destroy(skyMat); skyMat = null;
            if (envMat != null) Destroy(envMat); envMat = null;
            if (audio != null) audio.Dispose(); audio = null;
            if (world != null) Destroy(world); world = null;
            if (cam != null) Destroy(cam.gameObject); cam = null;
            if (keyL != null) Destroy(keyL.gameObject); keyL = null;
            if (rimL != null) Destroy(rimL.gameObject); rimL = null;
        }

        // ------------------------------------------------------------ 特效 --
        static DuelParticles.Part Pt(float x, float y, float z, float vx, float vy, float vz, Color c, float a, float size, float life)
        {
            return new DuelParticles.Part { x = x, y = y, z = z, vx = vx, vy = vy, vz = vz, r = c.r, g = c.g, b = c.b, a = a, size = size, life = life };
        }
        float R() { return (float)fxR.NextDouble(); }
        void SpawnMote(bool init)
        {
            var r = moteR;
            Color c = C(kind == "forest" ? "#e8ffb0" : kind == "castle" ? "#ffb070" : "#fff0c8");
            float life = 6 + (float)r.NextDouble() * 6;
            float x = ((float)r.NextDouble() * 2 - 1) * 12, y = 0.2f + (float)r.NextDouble() * 4, z = -3 + (float)r.NextDouble() * 5, vx = 0.15f + (float)r.NextDouble() * 0.2f, vy = ((float)r.NextDouble() - 0.3f) * 0.12f;
            float a = 0.35f + (float)r.NextDouble() * 0.35f, size = 0.035f + (float)r.NextDouble() * 0.04f;
            var p = Pt(x, y, z, vx, vy, 0, c, a, size, life); p.ac = 2; p.fadeIn = 0.2f;
            motes.Add(p);
            if (init) p.age = (float)r.NextDouble() * life;
        }
        void Burst(float x, float y, float z, Color c, int n, float speed, float dir = 0, float size = 0.09f, float life = 0.35f, float grav = 9)
        {
            for (int i = 0; i < n; i++)
            {
                float a = R() * Mathf.PI * 2, e = (R() - 0.35f) * Mathf.PI * 0.8f;
                float v = speed * (0.4f + R() * 0.8f);
                float vx = Mathf.Cos(a) * Mathf.Cos(e) * v + dir * speed * 0.6f, vy = Mathf.Sin(e) * v + speed * 0.25f, vz = Mathf.Sin(a) * Mathf.Cos(e) * v * 0.5f;
                float sz = size * (0.6f + R() * 0.8f), lf = life * (0.6f + R() * 0.7f);
                var p = Pt(x, y, z, vx, vy, vz, c, 1, sz, lf); p.grav = grav; p.drag = 2.2f; p.ac = 1.5f; p.floor = true;
                sparks.Add(p);
            }
        }
        void Puff(float x, int n, float scale, float dir = 0)
        {
            var c = DuelXf.Mix(C(arena.pal.lane), C("#e8dcc8"), 0.35f);
            for (int i = 0; i < n; i++)
            {
                float v = (0.6f + R() * 1.4f) * scale;
                float px = x + (R() - 0.5f) * 0.4f, py = 0.08f + R() * 0.15f, pz = (R() - 0.5f) * 0.6f;
                float d = dir != 0 ? dir : (R() < 0.5f ? -1 : 1);
                float vx = d * v * (0.4f + R()), vy = 0.3f + R() * 0.7f * scale, vz = (R() - 0.5f) * v;
                float size = (0.35f + R() * 0.4f) * scale, life = 0.7f + R() * 0.6f;
                var p = Pt(px, py, pz, vx, vy, vz, c, 0.45f, size, life); p.grow = 2.2f; p.drag = 2.5f; p.grav = -0.2f; p.ac = 1.2f;
                dust.Add(p);
            }
        }
        void Star(float x, float y, Color col, float size)
        {
            Fx s = null; foreach (var o in stars) if (o.t >= 1) { s = o; break; }
            if (s == null) s = stars[0];
            DuelXf.Pos(s.go.transform, x, y, 0.35f); s.mat.color = col; s.t = 0; s.size = size; s.go.SetActive(true);
            s.rot = R() * 0.8f - 0.4f;
        }
        void Ring(float x, Color col, float size, float life = 0.5f)
        {
            Fx g = null; foreach (var o in rings) if (o.t >= 1) { g = o; break; }
            if (g == null) g = rings[0];
            DuelXf.Pos(g.go.transform, x, 0.06f, 0); g.mat.color = col; g.t = 0; g.size = size; g.life = life; g.go.SetActive(true);
        }
        Color SpColor(int idx) { var s = idx == 0 ? spA : spB; return s.color != null ? C(s.color) : (idx == 0 ? colA : colB); }
        Color SpColorOr(int idx, string fallback) { var s = idx == 0 ? spA : spB; return C(s.color ?? fallback); }

        // 处理逻辑事件 → 画面、声音、HUD
        void Events()
        {
            var E = sim.events;
            if (E.Count == 0) return;
            foreach (var e in E)
            {
                switch (e.type)
                {
                    case "hit":
                        {
                            var att = sim.f[e.att]; var def = sim.f[e.def];
                            bool sp = e.move == "special";
                            Color spc = SpColor(att.idx);
                            Color col = e.guarded ? C("#bfe0ff") : sp ? spc : e.big ? C("#ffc040") : C("#fff0b0");
                            int n = e.guarded ? 10 : Mathf.RoundToInt(10 + e.dmg * 2.2f);
                            float x = (float)e.x, y = (float)e.y;
                            Burst(x, y, 0.2f, col, n, e.guarded ? 3.5f : 5 + e.dmg * 0.25f, att.face, e.big ? 0.12f : 0.09f);
                            if (!e.guarded) Burst(x, y, 0.2f, Color.white, 6, 2.5f, att.face, 0.14f, 0.18f, 0);
                            Star(x, y, e.guarded ? C("#a8d0ff") : sp ? spc : e.big ? C("#ffd27a") : Color.white, (e.guarded ? 0.9f : 1.1f) + Mathf.Min(1.6f, e.dmg * 0.09f));
                            shake = Mathf.Max(shake, e.guarded ? 0.04f : 0.05f + e.dmg * 0.012f + (e.big ? 0.06f : 0));
                            if (!e.guarded) war[def.idx].FlashHit(e.big ? C("#ffd0a0") : Color.white, e.big ? 0.95f : 0.8f);
                            float fv = (float)(fxR.NextDouble() - 0.5) * 0.6f;
                            if (e.guarded)
                            {
                                Sfx.Play("duel", 0.8f);
                                hud.FloatDmg(new Vector3(x, y + 0.3f, 0), e.dmg > 0 ? e.dmg.ToString() : "格", "g", fv);
                                if (e.parry) hud.FloatDmg(new Vector3((float)def.x, 2.4f, 0), "格开", "lbl", (float)(fxR.NextDouble() - 0.5) * 0.6f);
                            }
                            else
                            {
                                Sfx.Play("hit", Mathf.Min(1, 0.55f + e.dmg * 0.03f));
                                if (e.big) audio.Play("thud", 0.5f);
                                hud.FloatDmg(new Vector3(x, y + 0.3f, 0), e.dmg.ToString(), e.big ? "big" : "", fv);
                                if (e.counter) hud.FloatDmg(new Vector3(x, y + 0.75f, 0), "破绽", "lbl", (float)(fxR.NextDouble() - 0.5) * 0.6f);
                            }
                            if (e.breaks) { hud.FloatDmg(new Vector3((float)def.x, 2.4f, 0), "破防", "lbl", (float)(fxR.NextDouble() - 0.5) * 0.6f); Sfx.Play("rock", 0.6f); }
                            if (e.big && !e.guarded) { Ring((float)def.x, col, 3.2f, 0.45f); Puff((float)def.x, 6, 0.8f, att.face); }
                            if (sp && e.finisher) { hud.ScreenFlash(0.5f); Ring((float)def.x, spc, 6, 0.7f); }
                            hud.HitShake(def.idx);
                            if (e.combo >= 2) hud.ShowCombo(att.idx, e.combo, realT);
                            break;
                        }
                    case "swing":
                        {
                            var f = sim.f[e.who];
                            trailOn[e.who] = realT + (e.move == "heavy" || e.move == "special" ? 0.2f : 0.14f);
                            audio.Play("whoosh", e.move == "heavy" ? 1 : 0.65f);
                            if (e.move == "heavy" && e.charge > 0.6) Puff((float)f.x, 4, 0.6f, f.face);
                            break;
                        }
                    case "arrow":
                        {
                            var f = sim.f[e.who];
                            audio.Play("twang", e.fin ? 1 : 0.7f);
                            audio.Play("whoosh", e.fin ? 0.9f : 0.5f);
                            if (e.fin) { shake = Mathf.Max(shake, 0.06f); Burst((float)f.x + f.face * 0.7f, 1.4f, 0.1f, C("#fff0c0"), 12, 3, f.face, 0.09f, 0.25f, 0); }
                            break;
                        }
                    case "jump": Puff((float)sim.f[e.who].x, 4, 0.5f); break;
                    case "land": Puff((float)e.x, 5, 0.6f); break;
                    case "dash": Puff((float)sim.f[e.who].x, 5, 0.6f, -sim.f[e.who].face); audio.Play("whoosh", 0.4f); break;
                    case "down": Puff((float)e.x, 14, 1.2f); shake = Mathf.Max(shake, 0.12f); audio.Play("thud", 0.8f); Ring((float)e.x, C("#e8d8b8"), 3.4f, 0.5f); break;
                    case "chargeFull": { var f = sim.f[e.who]; Burst((float)f.x + f.face * 0.3f, 1.4f, 0.2f, C("#ffe08a"), 16, 2.5f, 0, 0.09f, 0.5f, -2); Sfx.Play("coin", 0.5f); break; }
                    case "guardbreak": shake = Mathf.Max(shake, 0.12f); break;
                    case "special":
                        {
                            var f = sim.f[e.who]; var spx = f.idx == 0 ? spA : spB;
                            hud.CutIn(f.idx, spx, DuelGame.Speed);
                            zoom = new Zoom { who = f.idx, until = realT + (float)DuelSim.SPECIAL_FREEZE / Mathf.Max(0.1f, DuelGame.Speed) + 0.15f };
                            Color sc = SpColorOr(f.idx, "#ffd040");
                            Ring((float)f.x, sc, 5, 0.8f);
                            Burst((float)f.x, 1.2f, 0, sc, 40, 4, 0, 0.1f, 0.7f, -3);
                            Sfx.Play("horn", 0.6f);
                            break;
                        }
                    case "ko":
                        slow = 0.22f; slowT = 1.25f;
                        hud.ScreenFlash(0.75f);
                        shake = 0.25f;
                        zoom = new Zoom { who = e.who, until = realT + 1.6f, ko = true };
                        hud.Announce("击　破", "red", null, 1.6f);
                        audio.Play("thud", 1);
                        Sfx.Play("rock", 0.8f);
                        break;
                    case "timeup": hud.Announce("时间到", "small", null, 1.4f); Sfx.Play("horn", 0.5f); break;
                }
            }
            E.Clear();
        }

        void DoSkip()
        {
            if (skipped || player >= 0) return;
            skipped = true;
            sim.events.Clear();
            if (!sim.hasResult) sim.RunToEnd();
            sim.events.Clear();   // 网页版此处留下了快进时的全部事件（下一帧一齐放出火花与音效），这里一并清掉
        }

        // ------------------------------------------------------------ 每帧 --
        void Update()
        {
            float dt = Mathf.Min(0.1f, Mathf.Max(0, Time.unscaledDeltaTime));
            if (finished) return;
            if (!built) { realT += dt; if (hud != null) SafeHud(dt); return; }
            try { Tick(dt); }
            catch (Exception e) { errors++; Debug.LogException(e); if (errors > 3) failed = true; }
            SafeHud(dt);
        }
        void SafeHud(float dt)
        {
            if (hud == null) return;
            try { hud.Update(dt, realT, built ? sim : null, cam); }
            catch (Exception e) { errors++; Debug.LogException(e); if (errors > 3) failed = true; }
        }

        void Tick(float dt)
        {
            realT += dt;
            // 输入（逻辑步长之前）
            if (input != null) { input.Poll(); hud.PollTouch(input, realT, player >= 0 && !DuelGame.Auto && sim.f[player].rage >= 100); }
            // 观战：跳过按钮随时可按；开打后按任意键 / 轻触画面也可跳过
            if (player < 0 && !skipped)
            {
                if (AnyTap() && hud.PointInSkip(TapPos())) DoSkip();
                else if (sim.running && !sim.hasResult && (AnyKeyNoMouse() || AnyTap())) DoSkip();
            }
            // 暂停（竖屏 / 切到后台）：模拟与计时停住，清掉按住的输入；恢复后「继续」倒数片刻再开打
            bool hold = false;
            if (sim.running && !sim.hasResult)
            {
                bool portrait = Application.isMobilePlatform && Screen.height > Screen.width;
                if (appPaused || portrait)
                {
                    if (!pauseFlag) { pauseFlag = true; inWas = input != null && input.enabled; if (input != null) input.SetEnabled(false); }
                    hud.ShowPauseHint(portrait);
                    resumeAt = 0;
                    hold = true;
                }
                else if (pauseFlag)
                {
                    pauseFlag = false;
                    hud.ShowPauseHint(false);
                    resumeAt = realT + 1.0f;
                    hud.Announce("继　续", "small", null, 1.0f);
                }
                if (resumeAt > 0)
                {
                    if (realT < resumeAt) hold = true;
                    else { resumeAt = 0; if (input != null && inWas) input.SetEnabled(true); }
                }
            }
            if (hold)
            {
                acc = 0;
                if (zoom != null) zoom.until += dt;
                for (int i = 0; i < 2; i++) if (trailOn[i] > realT - dt) trailOn[i] += dt;
            }
            else if (slowT > 0) { slowT -= dt; if (slowT <= 0) slow = 1; }
            float ts = hold ? 0 : DuelGame.Speed * slow;
            acc += dt * ts;
            int n = 0;
            while (acc >= DT && n < 24) { sim.Step(DuelSim.DT); acc -= DT; n++; }
            if (n >= 24) acc = 0;
            Events();
            float adt = dt * ts;
            bool frozen = sim.hitstop > 0;
            for (int i = 0; i < 2; i++)
            {
                var w = war[i];
                w.Update(frozen || sim.freeze[i] > 0 ? 0 : adt, frozen);
                // 刀光
                var f = sim.f[i];
                bool on = realT < trailOn[i] || (f.state == "special" && f.t > DuelSim.SPECIAL_FREEZE && !f.bow);
                var spInfo = i == 0 ? spA : spB;
                Color tc = f.state == "special" ? C(spInfo.color ?? "#ffd060") : f.state == "heavy" ? (f.charge > 0.8 ? C("#ffb040") : C("#ffe0a0")) : C("#dfe8ff");
                for (int k2 = 0; k2 < (w.wpn2 != null ? 2 : 1); k2++)
                {
                    var tr = trails[i * 2 + k2];
                    tr.on = on; tr.color = tc;
                    if (!frozen) tr.Push(w.baseW[k2], w.tipW[k2]);
                    tr.Update(adt);
                }
                // 蓄力 / 怒气满：上升的光点
                if (!frozen && (f.state == "charge" || f.rage >= 100 || (f.state == "special" && f.t < DuelSim.SPECIAL_FREEZE)))
                {
                    float rate = f.state == "charge" ? 40 * (0.3f + (float)f.charge) : f.state == "special" ? 90 : 10;
                    float ra = rate * adt;
                    int cnt = R() < ra % 1 ? Mathf.CeilToInt(ra) : Mathf.FloorToInt(ra);
                    Color c = f.state == "charge" ? C("#ffd27a") : C(spInfo.color ?? "#ffb040");
                    for (int j = 0; j < cnt; j++)
                    {
                        float px = (float)f.x + (R() - 0.5f) * 0.7f, py = 0.1f + R() * 1.4f, pz = (R() - 0.5f) * 0.5f, vy = 1.2f + R() * 1.6f, size = 0.06f + R() * 0.05f, life = 0.5f + R() * 0.4f;
                        var p = Pt(px, py, pz, 0, vy, 0, c, 0.9f, size, life); p.ac = 1.5f;
                        sparks.Add(p);
                    }
                }
            }
            UpdateArrows(adt);
            // 环境（城头火把）
            arena.Update(realT);
            foreach (var p0 in arena.torches)
            {
                int kk = Mathf.FloorToInt(dt * 34 + R());
                for (int j = 0; j < kk; j++)
                {
                    float px = p0.x + (R() - 0.5f) * 0.15f, vx = (R() - 0.5f) * 0.3f, vy = 0.8f + R() * 0.8f, g = 0.55f + R() * 0.25f, size = 0.22f + R() * 0.12f, life = 0.45f + R() * 0.3f;
                    var p = Pt(px, p0.y, p0.z, vx, vy, 0, new Color(1, g, 0.2f), 0.85f, size, life); p.grow = -0.6f; p.ac = 1.2f;
                    sparks.Add(p);
                }
            }
            if (motes.Count < 70) SpawnMote(false);
            UpdateCamera(dt);
            Vector3 right = cam.transform.right, up = cam.transform.up;
            sparks.Update(adt, right, up); dust.Update(adt, right, up); motes.Update(dt, right, up);
            foreach (var s in stars)
            {
                if (s.t >= 1) { s.go.SetActive(false); continue; }
                s.t = Mathf.Min(1, s.t + adt / s.life);
                float k = Mathf.Sin(s.t * Mathf.PI);
                s.go.transform.localScale = Vector3.one * (s.size * (0.4f + s.t * 0.9f));
                s.go.transform.rotation = cam.transform.rotation * Quaternion.Euler(0, 0, s.rot * Mathf.Rad2Deg);
                var c = s.mat.color; c.a = k; s.mat.color = c;
            }
            foreach (var g in rings)
            {
                if (g.t >= 1) { g.go.SetActive(false); continue; }
                g.t = Mathf.Min(1, g.t + adt / g.life);
                g.go.transform.localScale = Vector3.one * (g.size * (0.2f + g.t));
                var c = g.mat.color; c.a = (1 - g.t) * 0.9f; g.mat.color = c;
            }
        }

        void UpdateArrows(float dt)
        {
            var S = sim.shots;
            foreach (var a in arrows)
            {
                bool alive = false;
                if (a.id != 0) foreach (var sh in S) if (sh.id == a.id) { alive = true; break; }
                if (!alive) { a.id = 0; a.go.SetActive(false); }
            }
            foreach (var sh in S)
            {
                ArrowObj a = null;
                foreach (var x in arrows) if (x.id == sh.id) { a = x; break; }
                if (a == null)
                {
                    foreach (var x in arrows) if (x.id == 0) { a = x; break; }
                    if (a == null) continue;
                    a.id = sh.id; a.mf.sharedMesh = arrowMesh[sh.fin ? 1 : 0];
                    a.go.transform.localScale = Vector3.one * (sh.fin ? 1.9f : 1.45f);
                }
                a.go.SetActive(true);
                float d = Math.Sign(sh.vx); if (d == 0) d = 1;
                float sc = a.go.transform.localScale.x;
                DuelXf.Pos(a.go.transform, (float)sh.x - d * 0.95f * sc, (float)sh.y, 0.08f);
                DuelXf.RotXYZ(a.go.transform, 0, d > 0 ? 0 : Mathf.PI, 0);
                // 箭尾流光
                if (dt > 0)
                {
                    Color c = sh.fin ? SpColorOr(sh.owner, "#ffd040") : C("#fff4d8");
                    for (int j = 0; j < (sh.fin ? 3 : 2); j++)
                    {
                        float px = (float)sh.x - d * (0.2f + R() * 0.6f), py = (float)sh.y + (R() - 0.5f) * 0.05f, size = (sh.fin ? 0.13f : 0.07f) * (0.6f + R() * 0.6f);
                        var p = Pt(px, py, 0.08f, -d * 0.6f, 0, 0, c, 0.8f, size, 0.22f); p.ac = 1.2f;
                        sparks.Add(p);
                    }
                }
            }
        }

        // 镜头：两人都留在画面里，地平线放在画面下部；特写（绝技 / KO）、开场推近、震屏
        void UpdateCamera(float dt)
        {
            DuelFighter a = sim.f[0], b = sim.f[1];
            float aspect = cam.aspect, tanV = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2), tanH = tanV * aspect;
            float mid = (float)(a.x + b.x) / 2;
            float sep = Mathf.Abs((float)(a.x - b.x));
            bool touch = hud.hasTouch;
            float frac = touch ? 0.54f : hud.compact ? 0.62f : 0.7f;
            float dist = Mathf.Clamp((sep + 2.6f) / frac / (2 * tanH), 6.2f, 15);
            dist = Mathf.Max(dist, (touch ? 3.0f : 2.9f) / (2 * tanV * (touch ? 0.64f : 0.72f)));
            float hc = 1.55f + dist * 0.045f;
            float ground = touch ? 0.42f : 0.46f;     // 地平线（脚下）在屏幕中的位置：NDC −ground
            var z = zoom;
            if (z != null && realT < z.until)
            {
                var f = sim.f[z.who];
                mid = Mathf.Lerp(mid, (float)f.x, z.ko ? 0.55f : 0.75f);
                if (z.ko)
                {
                    // KO 慢镜：轻推近，镜头不压低；被击飞者腾空时地平线下移，把他留在画面里
                    dist *= 0.84f; hc += 0.1f;
                    ground = (touch ? 0.48f : 0.5f) + Mathf.Min(0.28f, 0.14f * Mathf.Max(0, (float)f.y));
                }
                else { dist *= 0.62f; hc -= 0.1f; ground = 0.38f; }
            }
            else zoom = null;
            // 开场：从侧后方推近
            if (introCam < 1)
            {
                float k0 = introCam, e = 1 - Mathf.Pow(1 - k0, 3);
                mid = Mathf.Lerp(mid - 4.5f, mid, e); dist = Mathf.Lerp(dist * 0.55f, dist, e); hc = Mathf.Lerp(0.9f, hc, e);
            }
            float halfW = dist * tanH;
            float lim = Mathf.Max(0, (float)DuelSim.ARENA + 1.6f - halfW);
            mid = Mathf.Clamp(mid, -lim, lim);
            float alpha = Mathf.Atan2(hc, dist), theta = alpha - Mathf.Atan(ground * tanV);
            float kk = 1 - Mathf.Exp(-dt * (zoom != null ? 10 : 5));
            var want = new Vector3(mid, hc, dist);
            if (snapCam) { camPos = want; snapCam = false; } else camPos = Vector3.Lerp(camPos, want, kk);
            float lookY = camPos.y - camPos.z * Mathf.Tan(theta);
            camLook = new Vector3(camPos.x, lookY, 0);
            // 震屏
            shake = Mathf.Max(0, shake - dt * 0.6f) * Mathf.Exp(-dt * 6);
            float s = shake;
            var pos = new Vector3(camPos.x + (R() - 0.5f) * s * 2, camPos.y + (R() - 0.5f) * s * 2, camPos.z);
            var look = new Vector3(camLook.x + (R() - 0.5f) * s, camLook.y + (R() - 0.5f) * s, 0);
            cam.transform.position = DuelXf.P(pos);
            cam.transform.LookAt(DuelXf.P(look), Vector3.up);
        }
    }

    // 挥击风声、重击闷响、弓弦（网页版用 WebAudio 即时合成；这里离线合成成 AudioClip）
    public sealed class DuelAudio
    {
        const int SR = 44100;
        readonly AudioSource src;
        readonly AudioClip[] whoosh = new AudioClip[3];
        readonly AudioClip thud, twang;
        int wi;

        public DuelAudio(GameObject host)
        {
            src = host.AddComponent<AudioSource>();
            src.playOnAwake = false; src.spatialBlend = 0;
            var noise = new float[SR];
            var r = new SeededRandom(99);
            for (int i = 0; i < noise.Length; i++) noise[i] = (float)(r.NextDouble() * 2 - 1);
            for (int k = 0; k < 3; k++) whoosh[k] = Make("whoosh" + k, Whoosh(noise, 0.07f + k * 0.15f));
            thud = Make("thud", Thud(noise));
            twang = Make("twang", Twang(noise));
        }
        static AudioClip Make(string name, float[] d) { var c = AudioClip.Create("Duel_" + name, d.Length, 1, SR, false); c.SetData(d, 0); return c; }
        public void Play(string kind, float vol)
        {
            if (!Sfx.SoundOn || src == null) return;
            AudioClip c = kind == "thud" ? thud : kind == "twang" ? twang : whoosh[wi++ % 3];
            src.PlayOneShot(c, Mathf.Clamp01(vol));
        }
        public void Dispose()
        {
            foreach (var c in whoosh) if (c != null) UnityEngine.Object.Destroy(c);
            if (thud != null) UnityEngine.Object.Destroy(thud);
            if (twang != null) UnityEngine.Object.Destroy(twang);
        }

        // WebAudio 的 exponentialRampToValueAtTime：分段指数插值
        static float Ramp(float t, float[] ts, float[] vs)
        {
            if (t <= ts[0]) return vs[0];
            for (int i = 1; i < ts.Length; i++)
                if (t <= ts[i]) return vs[i - 1] * Mathf.Pow(vs[i] / vs[i - 1], (t - ts[i - 1]) / (ts[i] - ts[i - 1]));
            return vs[vs.Length - 1];
        }
        // RBJ 双二阶滤波器（kind：0 低通 / 1 高通 / 2 带通）
        sealed class Biquad
        {
            float b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public void Set(int kind, float f, float q)
            {
                float w = 2 * Mathf.PI * Mathf.Clamp(f, 10, SR * 0.45f) / SR, cs = Mathf.Cos(w), al = Mathf.Sin(w) / (2 * q), a0 = 1 + al;
                if (kind == 0) { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = (1 - cs) / 2; }
                else if (kind == 1) { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2; }
                else { b0 = al; b1 = 0; b2 = -al; }
                a1 = -2 * cs; a2 = 1 - al;
                b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
            }
            public float Run(float x) { float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return y; }
        }
        static float[] Whoosh(float[] noise, float offset)
        {
            int n = (int)(0.26f * SR), o = (int)(offset * SR); var d = new float[n]; var bq = new Biquad();
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                if (i % 32 == 0) bq.Set(2, Ramp(t, new[] { 0, 0.12f, 0.22f }, new[] { 500f, 2600, 700 }), 1.4f);
                d[i] = bq.Run(noise[(o + i) % noise.Length]) * Ramp(t, new[] { 0, 0.05f, 0.24f }, new[] { 0.0001f, 0.22f, 0.0001f });
            }
            return d;
        }
        static float[] Thud(float[] noise)
        {
            int n = (int)(0.5f * SR); var d = new float[n]; var bq = new Biquad(); float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                if (i % 32 == 0) bq.Set(0, Ramp(t, new[] { 0, 0.35f }, new[] { 900f, 90 }), 0.707f);
                float s = bq.Run(noise[i % noise.Length]) * Ramp(t, new[] { 0, 0.01f, 0.45f }, new[] { 0.0001f, 0.7f, 0.0001f });
                ph += 2 * Mathf.PI * Ramp(t, new[] { 0, 0.35f }, new[] { 110f, 38 }) / SR;
                if (t < 0.42f) s += Mathf.Sin(ph) * Ramp(t, new[] { 0, 0.4f }, new[] { 0.5f, 0.0001f });
                d[i] = Mathf.Clamp(s, -1, 1);
            }
            return d;
        }
        static float[] Twang(float[] noise)
        {
            int n = (int)(0.32f * SR), o = (int)(0.1f * SR); var d = new float[n]; var bq = new Biquad(); bq.Set(1, 2500, 0.707f); float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                ph += Ramp(t, new[] { 0, 0.25f }, new[] { 240f, 150 }) / SR;
                float tri = 1 - 4 * Mathf.Abs((ph % 1f) - 0.5f);
                float s = tri * Ramp(t, new[] { 0, 0.005f, 0.3f }, new[] { 0.0001f, 0.35f, 0.0001f });
                if (t < 0.08f) s += bq.Run(noise[(o + i) % noise.Length]) * Ramp(t, new[] { 0, 0.004f, 0.06f }, new[] { 0.0001f, 0.15f, 0.0001f });
                d[i] = Mathf.Clamp(s, -1, 1);
            }
            return d;
        }
    }
}
