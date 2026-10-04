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

    public class Game : MonoBehaviour
    {
        public static Game I;
        public CameraRig Rig;
        public MapView Map;
        public StrategyScreen Strategy;
        Light sun;

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
            // 先用一份临时剧本生成地图，作为标题背景
            GameState.Current = GameState.NewGame("liubei");
            Map.Build(GameState.Current);
            Strategy = gameObject.AddComponent<StrategyScreen>();
            yield return null;
            yield return TitleFlow();
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
            cont.button.interactable = GameState.HasSave;
            UIKit.Size(UIKit.Button(menu, "操作说明", () => choice = 2, 28).bg, -1, 66);

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
                    try { GameState.Current = GameState.Load(); }
                    catch (System.Exception e) { Debug.LogWarning(e); choice = -1; UIKit.Toast("存档无法读取"); continue; }
                    Destroy(title.gameObject);
                    break;
                }
                Destroy(title.gameObject);
                yield return SelectRuler();
                break;
            }
            Rig.DesiredYaw = 0;
            Rig.InputEnabled = true;
            Map.Refresh(GameState.Current);
            yield return Strategy.Run();
        }

        const string HelpText = "【操作】拖动平移地图，滚轮或双指缩放，点击城池查看情报。\n【令牌】每月可下达的指令数量取决于所领城池数。\n【出征】选择相邻的敌城与至多五名武将；可亲自指挥战斗或委任电脑。\n【战斗】点选部队移动，相邻时可攻击、施展策略或单挑。击败敌军主将或攻入本城即可获胜，三十日内未能攻下则撤退。";

        // ---------------------------------------------------------- 选择君主 --
        IEnumerator SelectRuler()
        {
            yield return UIKit.Say(ScenarioData.Intro);
            var g = GameState.Current;
            var factions = g.factions.Where(f => f.alive).OrderByDescending(f => g.CityCount(f.id)).ToList();
            while (true)
            {
                var items = factions.Select(f => new UIKit.Item(
                    "<color=#" + ColorUtility.ToHtmlStringRGB(f.Col) + ">■</color> " + g.Ruler(f.id).name,
                    string.Format("城 {0}　将 {1}　德 {2}　人望 {3}", g.CityCount(f.id), g.GeneralsOf(f.id).Count(), f.virtue, f.fame))).ToList();
                var r = new Ref<int>();
                yield return UIKit.Choose("选择君主　" + ScenarioData.StartYear + "年 · " + ScenarioData.Title, items, r, "德越高越易登用人才；人望决定战场上的行动力。", 820);
                if (r.v < 0) continue;
                var f0 = factions[r.v];
                var capital = g.CitiesOf(f0.id).First();
                Rig.Focus(new Vector3(capital.MapPos.x, 0, capital.MapPos.y), 45);
                Map.Select(capital.id);
                var ok = new Ref<bool>();
                yield return UIKit.Confirm("以" + g.Ruler(f0.id).name + "开始？", ok, "出阵", "再想想");
                if (!ok.v) { Map.Select(-1); continue; }
                GameState.Current = GameState.NewGame(f0.key);
                Map.Select(-1);
                break;
            }
        }
    }
}
