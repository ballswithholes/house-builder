using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sanguo
{
    // 启动：任何场景中都会自动创建游戏（也可放在空场景里）
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => Ensure();
            Ensure();
        }
        static void Ensure()
        {
            if (Object.FindObjectOfType<Game>() != null) return;
            new GameObject("Game").AddComponent<Game>();
        }
    }

    // 创建世界、标题画面（地图缓慢环绕）、操作说明、选择剧本与君主、战略循环（← 网页版 js/game.js）
    // 第二版 §4G：
    //   新的征程 → 选择剧本（「董卓专横 · 中原」经典 38 城 / 「天下大势 · 世界」）→ 开场白 → 选择君主。
    //     经典：单一列表（与旧版相同，另加君主头像）；世界：先选地域（势力数与几位君主的头像），再选该地域的君主，
    //     悬停（鼠标）时镜头飞往其都城，确认卡片显示势力介绍与「游戏取舍」。
    //   地图按剧本分片构建（MapView.BuildAsync），载入画面显示进度；镜头范围按剧本（CameraRig.UseMap）。
    //   读档失败（Load() 返回 null）时显示 GameState.LoadError；只有旧版存档时「继续征程」可按，按下说明无法读取。
    public class Game : MonoBehaviour
    {
        public static Game I;
        public CameraRig Rig;
        public MapView Map;
        public StrategyScreen Strategy;
        public float LastBuildMs;          // 最近一次地图构建耗时（毫秒）
        Light sun;

        public const string HelpText = "【操作】拖动平移地图，滚轮或双指缩放，点击城池查看情报。\n【令牌】每月可下达的指令数量取决于所领城池数。灰色指令点按可查看原因。\n【出征】选择相邻的敌城与至多五名武将；可亲自指挥战斗或委任电脑。\n【移动 / 输送】调动武将、调拨金粮：经由己方城池相连的城都可前往，不必相邻（列表注明经过几城），每次 1 枚令牌。己方两城相连即可使用。\n【战斗】点选部队移动，相邻时可攻击、施展策略或单挑。击败敌军主将或攻入本城即可获胜，三十日内未能攻下则撤退。";

        // 剧本（Scenarios 的键）
        public static readonly string[][] ScenarioList =
        {
            new[] { "classic", "董卓专横 · 中原", "原作剧本：董卓专权，关东诸侯并起，逐鹿中原。规则与原作相同。" },
            new[] { "world", "天下大势 · 世界", "东起倭国、西至罗马：欧亚大陆各国群雄并起，先选地域再选君主。" },
        };
        public static string ScenarioName(string id)
        {
            foreach (var s in ScenarioList) if (s[0] == id) return s[1];
            return id;
        }

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            QualitySettings.shadowDistance = 170;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.antiAliasing = 4;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (Application.isMobilePlatform)
            {
                Screen.autorotateToPortrait = false; Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true; Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
            // 清理场景中默认的相机与灯光，统一由代码创建
            foreach (var c in FindObjectsOfType<Camera>()) Destroy(c.gameObject);
            foreach (var l in FindObjectsOfType<Light>()) Destroy(l.gameObject);
            SetupWorld();
            Sfx.Init();
            UIKit.Init();
        }

        void SetupWorld()
        {
            Rig = Game.CreateRig();
            sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sun.shadowBias = 0.04f; sun.shadowNormalBias = 0.3f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.56f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.3f, 0.26f);
            RenderSettings.skybox = Art.Sky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.86f, 0.84f, 0.78f);
            RenderSettings.fogStartDistance = 90;
            RenderSettings.fogEndDistance = 320;
            Map = new GameObject("Map").AddComponent<MapView>();
        }

        static CameraRig CreateRig()
        {
            var rig = CameraRig.Create();
            rig.Focus(new Vector3(60, 0, 50), 95, true);
            return rig;
        }

        IEnumerator Start()
        {
            // 载入画面：先用一份临时的经典剧本分片生成地图，作为标题背景
            var loading = UIKit.ShowLoading("霸王的大陆", false);
            Scenarios.Current = Scenarios.Classic;
            GameState.Current = GameState.NewGame("liubei", "classic");
            yield return null; yield return null;
            float t0 = Time.realtimeSinceStartup;
            bool failed = false;
            yield return UIKit.Guard(Map.BuildAsync(GameState.Current, "auto", (p, label) => loading.Set(p, label)), e =>
            {
                Debug.LogException(e);
                failed = true;
                loading.Error("无法启动：" + e.Message);
            });
            if (failed) yield break;
            LastBuildMs = (Time.realtimeSinceStartup - t0) * 1000f;
            Rig.UseMap(Map);
            Strategy = gameObject.AddComponent<StrategyScreen>();
            yield return null;
            loading.Hide();
            yield return TitleFlow();
        }

        // ---------------------------------------------------------- 地图 --
        // 按 GameState.Current 重建地图（region "china" | "world"），载入画面显示进度；完成后镜头范围随地图
        public IEnumerator BuildMap(string region, string title)
        {
            var el = UIKit.ShowLoading(string.IsNullOrEmpty(title) ? "霸王的大陆" : title);
            el.Set(0, "准备");
            Map.Select(-1);
            yield return null; yield return null;
            float t0 = Time.realtimeSinceStartup;
            yield return UIKit.Guard(Map.BuildAsync(GameState.Current, region, (p, label) => el.Set(p, label)), e => Debug.LogException(e));
            LastBuildMs = (Time.realtimeSinceStartup - t0) * 1000f;
            Rig.UseMap(Map);
            yield return null;
            el.Hide();
        }
        // 地图与剧本一致（经典 → 中国地图；世界 → 欧亚大陆），不一致时重建
        public IEnumerator EnsureMap(string sid)
        {
            string want = sid == "world" ? "world" : "china";
            if (Map.Root != null && Map.Region == want) { Rig.UseMap(Map); yield break; }
            yield return BuildMap(want, sid == "world" ? "天下大势" : "霸王的大陆");
        }

        // 空闲时（后台线程）预生成全部在世武将的头像：城池面板的武将行与对话的说话人
        void PreloadPortraits()
        {
            try { StartCoroutine(Portrait.PreloadState(GameState.Current, new[] { UIKit.PortraitPx(StrategyScreen.RowPortrait), UIKit.PortraitPx(96) })); }
            catch (System.Exception e) { Debug.LogWarning(e); }
        }

        // 选择君主列表要用的君主头像（经典 32；世界：地域行 30、君主行 40、确认卡片 140），在开场白期间后台生成
        void PreloadRulers(string sid)
        {
            try
            {
                var g = GameState.Current;
                var rulers = g.factions.Where(f => f.alive).Select(f => g.Ruler(f.id)).Where(x => x != null).ToList();
                if (sid == "world")
                {
                    StartCoroutine(Portrait.PreloadAsync(rulers, UIKit.PortraitPx(30)));
                    StartCoroutine(Portrait.PreloadAsync(rulers, UIKit.PortraitPx(40)));
                }
                else StartCoroutine(Portrait.PreloadAsync(rulers, UIKit.PortraitPx(32)));
            }
            catch (System.Exception e) { Debug.LogWarning(e); }
        }

        // ---------------------------------------------------------- 标题 --
        IEnumerator TitleFlow()
        {
            Sfx.Music("title");
            Rig.InputEnabled = false;
            var title = UIKit.NewRect("Title", UIKit.Screens); UIKit.Stretch(title);
            var vig = UIKit.Img(title, UIKit.Vignette, new Color(1, 1, 1, 0.95f), "Vignette"); UIKit.Stretch(vig.rectTransform); vig.raycastTarget = false;
            var seal = UIKit.Img(title, UIKit.RR, UIKit.Cinnabar, "Seal");
            UIKit.Place(seal.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(96, 96));
            var sealT = UIKit.Label(seal.transform, "霸", 64, new Color(1, 0.95f, 0.88f), TextAnchor.MiddleCenter, true); UIKit.Stretch(sealT.rectTransform);
            var sub0 = UIKit.Label(title, "三国志 II", 40, UIKit.Text, TextAnchor.MiddleCenter, true);
            UIKit.Place(sub0.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -210), new Vector2(800, 60));
            var t = UIKit.Label(title, "霸王的大陆", 140, UIKit.Gold, TextAnchor.MiddleCenter, true);
            UIKit.Place(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -320), new Vector2(1200, 170));
            var o = t.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0.3f, 0.12f, 0.02f, 1); o.effectDistance = new Vector2(3, -3);
            t.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -8);
            var sub = UIKit.Label(title, "群雄逐鹿 · 一统天下", 30, new Color(1, 0.94f, 0.8f, 0.8f), TextAnchor.MiddleCenter, true);
            UIKit.Place(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -430), new Vector2(800, 50));

            int choice = -1;
            var menu = UIKit.VList(title, 14);
            UIKit.Place(menu, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(380, 230));
            UIKit.Btn cont = null;
            UIKit.Size(UIKit.Button(menu, "新的征程", () => choice = 0, 32, true).bg, -1, 66);
            cont = UIKit.Button(menu, "继续征程", () => choice = 1, 32);
            UIKit.Size(cont.bg, -1, 66);
            bool legacy = GameState.HasLegacySave;
            cont.button.interactable = GameState.HasSave || legacy;
            UIKit.Size(UIKit.Button(menu, "操作说明", () => choice = 2, 28).bg, -1, 66);
            // 存档摘要（剧本 · 势力 · 年月）；只有旧版存档时直接说明读不了
            var info = GameState.HasSave ? GameState.GetSaveInfo() : null;
            string infoText = info != null ? ScenarioName(info.scenario).Split(new[] { " · " }, System.StringSplitOptions.None)[0] + " · " + info.faction + " · " + info.year + "年" + info.month + "月"
                : legacy ? "旧版存档（新版本无法读取）" : null;
            if (infoText != null)
            {
                var si = UIKit.Label(title, infoText, 17, new Color(0.96f, 0.93f, 0.86f, 0.72f), TextAnchor.MiddleCenter);
                UIKit.Place(si.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, 86), new Vector2(600, 30));
                var ss = si.gameObject.AddComponent<Shadow>(); ss.effectColor = new Color(0, 0, 0, 0.85f); ss.effectDistance = new Vector2(0, -1);
            }

            float ang = 0;
            while (true)
            {
                while (choice < 0)
                {
                    ang += Time.deltaTime * 2.5f;
                    Rig.DesiredYaw = Mathf.Sin(ang * 0.05f) * 25;
                    Rig.Desired = new Vector3(58 + Mathf.Sin(ang * 0.03f) * 18, 0, 48 + Mathf.Cos(ang * 0.04f) * 12);
                    Rig.DesiredDistance = 85;
                    yield return null;
                }
                if (choice == 2) { choice = -1; yield return UIKit.Say(HelpText); continue; }
                if (choice == 1)
                {
                    GameState loaded = null;
                    try { loaded = GameState.Load(); } catch (System.Exception e) { Debug.LogWarning(e); loaded = null; }
                    if (loaded == null)
                    {
                        choice = -1;
                        yield return UIKit.Say(string.IsNullOrEmpty(GameState.LoadError) ? "存档无法读取。" : GameState.LoadError);
                        continue;
                    }
                    title.gameObject.SetActive(false);
                    GameState.Current = loaded;
                    yield return EnsureMap(loaded.scenario);
                    PreloadPortraits();
                    Destroy(title.gameObject);
                    UIKit.Toast("读取进度：" + ScenarioName(loaded.scenario) + "　" + loaded.year + "年" + loaded.month + "月", 2.4f);
                    break;
                }
                // 新的征程：选择剧本 → 选择君主；中途返回则回到标题
                var sid = new Ref<string>();
                yield return ChooseScenario(sid);
                if (sid.v != null)
                {
                    title.gameObject.SetActive(false);
                    Rig.DesiredYaw = 0;
                    var ok = new Ref<bool>();
                    yield return SelectRuler(sid.v, ok);
                    if (ok.v) { Destroy(title.gameObject); break; }
                    title.gameObject.SetActive(true);
                    Rig.DesiredYaw = 0;
                    Rig.FocusMap(58, 48, 85, true);
                }
                choice = -1;
            }
            Rig.DesiredYaw = 0;
            Rig.InputEnabled = true;
            Map.Refresh(GameState.Current);
            yield return Strategy.Run();
        }

        // ---------------------------------------------------------- 选择剧本 --
        // → "classic" | "world" | null（取消）
        IEnumerator ChooseScenario(Ref<string> result)
        {
            result.v = null;
            var list = ScenarioList.Where(s => Scenarios.Get(s[0]) != null).ToList();
            if (list.Count == 1) { result.v = list[0][0]; yield break; }
            var items = new List<UIKit.Item>();
            foreach (var s in list)
            {
                var d = Scenarios.Get(s[0]);
                var alive = new HashSet<string>(d.Generals.Select(l => l.Split('|')[4]).Where(k => k != "-"));
                int nf = d.Factions.Count(l => alive.Contains(l.Split('|')[0]));
                items.Add(new UIKit.Item(s[1], d.StartYear + "年　城 " + d.Cities.Length + "　势力 " + nf, true, s[2]) { big = true });
            }
            var r = new Ref<int>();
            yield return UIKit.Choose("选择剧本", items, r, null, 760);
            if (r.v >= 0) result.v = list[r.v][0];
        }

        // ---------------------------------------------------------- 选择君主 --
        // 经典：单一列表；世界：先选地域再选君主。ok.v = true（已开局）/ false（返回标题）
        IEnumerator SelectRuler(string sid, Ref<bool> ok)
        {
            ok.v = false;
            Scenarios.Current = Scenarios.Get(sid) ?? Scenarios.Classic;
            // 临时局面：供地图与列表显示（经典剧本沿用标题背景的那一份，随机数序列与旧版相同）
            if (GameState.Current == null || GameState.Current.scenario != sid)
                GameState.Current = GameState.NewGame(Scenarios.Current.Factions[0].Split('|')[0], sid);
            yield return EnsureMap(sid);
            PreloadRulers(sid);
            yield return UIKit.Say(Scenarios.Current.Intro);
            var f0 = new Ref<Faction>();
            if (sid == "world" && Scenarios.Current.Regions != null && Scenarios.Current.Regions.Length > 0) yield return PickRulerByRegion(f0);
            else yield return PickRulerList(f0);
            Map.Select(-1);
            if (f0.v == null) yield break;
            GameState.Current = GameState.NewGame(f0.v.key, sid);
            PreloadPortraits();
            ok.v = true;
        }

        static string Stats(GameState g, Faction f)
        {
            return string.Format("城 {0}　将 {1}　德 {2}　人望 {3}", g.CityCount(f.id), g.GeneralsOf(f.id).Count(), f.virtue, f.fame);
        }
        static string Swatch(Faction f) { return "<color=#" + ColorUtility.ToHtmlStringRGB(f.Col) + ">■</color>"; }
        static Faction FactionByKey(GameState g, string key)
        {
            foreach (var f in g.factions) if (f.key == key) return f;
            return null;
        }

        City FocusCapital(GameState g, Faction f, float dist)
        {
            City cap = null;
            var ru = g.Ruler(f.id);
            if (ru != null && ru.city >= 0 && ru.city < g.cities.Count) cap = g.cities[ru.city];
            if (cap == null) cap = g.CitiesOf(f.id).FirstOrDefault();
            if (cap == null) return null;
            var p = cap.MapPos;
            FocusFree(p.x, p.y, dist);
            Map.Select(cap.id);
            return cap;
        }
        // 对准地图点，使其落在靠左对话框右侧空白区的中央（镜头偏航为 0 时屏幕 x 即地图 x）
        void FocusFree(float x, float y, float dist)
        {
            float frac, dx;
            if (UIKit.SideFree(out frac, out dx) && dx > 0 && Rig.Cam != null)
            {
                float visW = 2 * dist * Mathf.Tan(Rig.Cam.fieldOfView * Mathf.Deg2Rad / 2) * Rig.Cam.aspect;
                x -= dx / Mathf.Max(1, Screen.width) * visW;
            }
            Rig.FocusMap(x, y, dist);
        }

        // 经典剧本：单一列表（与旧版相同，另加君主头像）
        IEnumerator PickRulerList(Ref<Faction> result)
        {
            result.v = null;
            var g = GameState.Current;
            var factions = g.factions.Where(f => f.alive).OrderByDescending(f => g.CityCount(f.id)).ToList();   // 稳定排序：并列时按编号
            while (true)
            {
                var items = factions.Select(f => new UIKit.Item(Swatch(f) + " " + g.Ruler(f.id).name, Stats(g, f)) { portrait = g.Ruler(f.id), portraitSize = 32 }).ToList();
                var r = new Ref<int>();
                yield return UIKit.Choose("选择君主　" + Scenarios.Current.StartYear + "年 · " + Scenarios.Current.Title, items, r, "德越高越易登用人才；人望决定战场上的行动力。", 820);
                if (r.v < 0) yield break;
                var f0 = factions[r.v];
                FocusCapital(g, f0, 45);
                var ok = new Ref<bool>();
                yield return UIKit.Confirm("以" + g.Ruler(f0.id).name + "开始？", ok, "出阵", "再想想");
                if (!ok.v) { Map.Select(-1); continue; }
                result.v = f0;
                yield break;
            }
        }

        // 世界剧本第一步：选择地域（每行：地域名、几位君主的头像、势力数与城数）
        IEnumerator PickRulerByRegion(Ref<Faction> result)
        {
            result.v = null;
            var g = GameState.Current;
            var rows = new List<KeyValuePair<ScenarioRegion, List<Faction>>>();
            foreach (var reg in Scenarios.Current.Regions)
            {
                var fs = reg.Factions.Select(k => FactionByKey(g, k)).Where(f => f != null && f.alive)
                    .OrderByDescending(f => g.CityCount(f.id)).ThenBy(f => f.id).ToList();
                if (fs.Count > 0) rows.Add(new KeyValuePair<ScenarioRegion, List<Faction>>(reg, fs));
            }
            int sel = 0;
            while (true)
            {
                var items = new List<UIKit.Item>();
                foreach (var o in rows)
                {
                    var fs = o.Value;
                    items.Add(new UIKit.Item(o.Key.Name, "势力 " + fs.Count + "　城 " + fs.Sum(f => g.CityCount(f.id)), true,
                        string.Join("、", fs.Take(6).Select(f => f.name).ToArray()) + (fs.Count > 6 ? " 等" : ""))
                    {
                        faces = fs.Take(5).Select(f => g.Ruler(f.id)).ToList(), moreFaces = Mathf.Max(0, fs.Count - 5),
                    });
                }
                items[sel].selected = true;
                var r = new Ref<int>();
                var opts = new UIKit.ChooseOpts { side = true, initial = sel, nameMin = 5.6f, hover = i => FlyToFactions(g, rows[i].Value) };
                yield return UIKit.Choose("选择地域　" + Scenarios.Current.StartYear + "年 · " + Scenarios.Current.Title, items, r, "先选地域，再选该地域的君主。", 760, opts);
                if (r.v < 0) yield break;
                sel = r.v;
                FlyToFactions(g, rows[r.v].Value);
                var pick = new Ref<Faction>();
                yield return PickRulerIn(rows[r.v], pick);
                if (pick.v != null) { result.v = pick.v; yield break; }
            }
        }

        // 镜头飞往一组势力的都城（看到全部）
        void FlyToFactions(GameState g, List<Faction> fs)
        {
            float xMin = float.PositiveInfinity, yMin = float.PositiveInfinity, xMax = float.NegativeInfinity, yMax = float.NegativeInfinity;
            foreach (var f in fs)
            {
                var ru = g.Ruler(f.id);
                if (ru == null || ru.city < 0 || ru.city >= g.cities.Count) continue;
                var p = g.cities[ru.city].MapPos;
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x); yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
            }
            if (!(xMax >= xMin)) return;
            const float pad = 14;
            xMin -= pad; yMin -= pad; xMax += pad; yMax += pad;
            Map.Select(-1);
            // 能在对话框右侧空白区看到整个范围的距离（按视口宽高比与俯角估算，同 CameraRig.FitDistance）
            float frac, dx;
            if (!UIKit.SideFree(out frac, out dx)) frac = 1;
            var cam = Rig.Cam;
            float tn = Mathf.Tan((cam != null && cam.fieldOfView > 0 ? cam.fieldOfView : 34) * Mathf.Deg2Rad / 2);
            float aspect = (cam != null && cam.aspect > 0 ? cam.aspect : 16f / 9f) * frac;
            float pitch = (Rig.Pitch + 4) * Mathf.Deg2Rad;
            float d = Mathf.Max((yMax - yMin) * Mathf.Sin(pitch) / (2 * tn), (xMax - xMin) / (2 * tn * aspect)) * 1.05f;
            FocusFree((xMin + xMax) / 2, (yMin + yMax) / 2, Mathf.Clamp(d, Rig.MinDist, Rig.MaxDist));
        }

        // 世界剧本第二步：该地域的君主（头像、势力名、城 / 将 / 德 / 人望）；悬停时镜头飞往都城
        IEnumerator PickRulerIn(KeyValuePair<ScenarioRegion, List<Faction>> row, Ref<Faction> result)
        {
            result.v = null;
            var g = GameState.Current;
            var fs = row.Value;
            while (true)
            {
                var items = fs.Select(f =>
                {
                    var ru = g.Ruler(f.id);
                    var fi = WorldScenarioData.InfoOfFaction(f.key);
                    string full = fi != null && !string.IsNullOrEmpty(fi.Full) ? fi.Full : null;
                    string fname = f.name != ru.name && full == null ? "  " + UIKit.Sz(19) + "<color=#a8a194>" + f.name + "</color></size>" : "";
                    return new UIKit.Item(Swatch(f) + " <b>" + ru.name + "</b>" + fname, Stats(g, f), true, full) { portrait = ru, portraitSize = 40 };
                }).ToList();
                var r = new Ref<int>();
                var opts = new UIKit.ChooseOpts { side = true, initial = 0, hover = i => FocusCapital(g, fs[i], 58) };
                yield return UIKit.Choose("选择君主　" + row.Key.Name, items, r, "德越高越易登用人才；人望决定战场上的行动力。", 760, opts);
                if (r.v < 0) { Map.Select(-1); yield break; }
                var f0 = fs[r.v];
                var go = new Ref<bool>();
                yield return FactionCard(f0, go, () => FocusCapital(g, f0, 45));
                if (go.v) { result.v = f0; yield break; }
                Map.Select(-1);
            }
        }

        // 开局确认卡片：君主头像、势力全名、史实介绍、游戏取舍；「出阵」/「再想想」。opened：卡片建好后回调（镜头据此对准）
        IEnumerator FactionCard(Faction f, Ref<bool> result, System.Action opened)
        {
            result.v = false;
            var g = GameState.Current;
            var fi = WorldScenarioData.InfoOfFaction(f.key);
            var ru = g.Ruler(f.id);
            const float W = 760, pad = 30, ps = 140;
            // 先按文字排版算出高度
            var m = UIKit.OpenModal(W, 400, true, true);
            float w = m.panel.sizeDelta.x;
            int done = 0;   // 1 出阵，-1 再想想
            var head = UIKit.Label(m.panel, "以" + ru.name + (f.name != ru.name ? "（" + f.name + "）" : "") + "开始？", 32, UIKit.Gold, TextAnchor.MiddleLeft, true);
            UIKit.Place(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(pad, -18), new Vector2(w - 120, 48));
            var close = UIKit.Button(m.panel, "×", () => done = -1, 24);
            UIKit.Place(close.rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -18), new Vector2(52, 48));
            ScrollRect sr;
            var body = UIKit.Scroll(m.panel, out sr);
            var srt = (RectTransform)sr.transform;
            body.GetComponent<VerticalLayoutGroup>().enabled = false;
            body.GetComponent<ContentSizeFitter>().enabled = false;
            var pim = UIKit.PortraitImg(body, ru, ps);
            UIKit.Place(pim.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -6), new Vector2(ps, ps));
            float tx = ps + 22, tw = w - 2 * pad - tx, y = 4;
            y += UIKit.FlowText(body, Swatch(f) + " <b>" + (fi != null && !string.IsNullOrEmpty(fi.Full) ? fi.Full : f.name) + "</b>", 23, UIKit.Text, tx, y, tw) + 9;
            y += UIKit.FlowText(body, Stats(g, f), 21, UIKit.Muted, tx, y, tw) + 9;
            if (fi != null && !string.IsNullOrEmpty(fi.Note)) y += UIKit.FlowText(body, fi.Note, 21, new Color(0.96f, 0.93f, 0.86f, 0.92f), tx, y, tw) + 9;
            if (fi != null && !string.IsNullOrEmpty(fi.Liberty)) y += LibertyBox(body, fi.Liberty, tx, y, tw) + 9;
            float bodyH = Mathf.Max(y, ps + 12);
            body.anchorMin = new Vector2(0, 1); body.anchorMax = new Vector2(1, 1); body.pivot = new Vector2(0.5f, 1);
            body.sizeDelta = new Vector2(0, bodyH);
            float headH = 80, footH = 100;
            float h = Mathf.Min(UIKit.MaxModalHeight, headH + bodyH + footH);
            m.panel.sizeDelta = new Vector2(w, h);
            UIKit.Stretch(srt, pad, footH, pad, headH);
            var no = UIKit.Button(m.panel, "再想想", () => done = -1, 28);
            UIKit.Place(no.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24 - 200 - 16, 22), new Vector2(170, 62));
            var yes = UIKit.Button(m.panel, "出阵", () => done = 1, 28, true);
            UIKit.Place(yes.rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 22), new Vector2(200, 62));
            if (opened != null) opened();
            while (done == 0)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) done = -1;
                else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) done = 1;
                yield return null;
            }
            m.Close();
            result.v = done > 0;
        }

        // 「游戏取舍」说明框：左侧金线、淡金底
        public static float LibertyBox(RectTransform parent, string text, float x, float y, float w)
        {
            var box = UIKit.Img(parent, null, new Color(0.95f, 0.79f, 0.42f, 0.07f), "Liberty");
            box.raycastTarget = false;
            var t = UIKit.Label(box.transform, "<color=#f3c969><b>游戏取舍</b></color>　" + text, 19, UIKit.Muted, TextAnchor.UpperLeft);
            UIKit.Place(box.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, 10));
            UIKit.Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(15, -7), new Vector2(w - 27, 10));
            float th = Mathf.Ceil(t.preferredHeight) + 2;
            t.rectTransform.sizeDelta = new Vector2(w - 27, th);
            float h = th + 14;
            box.rectTransform.sizeDelta = new Vector2(w, h);
            var line = UIKit.Img(box.transform, null, new Color(0.95f, 0.79f, 0.42f, 0.6f), "Line");
            line.rectTransform.anchorMin = new Vector2(0, 0); line.rectTransform.anchorMax = new Vector2(0, 1);
            line.rectTransform.offsetMin = Vector2.zero; line.rectTransform.offsetMax = new Vector2(3, 0);
            return h;
        }
    }
}
