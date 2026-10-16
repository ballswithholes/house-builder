using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sanguo.EditorTools
{
    // 打开工程时自动完成设置：创建主场景、加入构建列表、横屏、应用名称
    [InitializeOnLoad]
    public static class SanguoSetup
    {
        public const string ScenePath = "Assets/Sanguo/Scenes/Main.unity";

        static SanguoSetup() { EditorApplication.delayCall += Setup; }

        static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ScenePath)) CreateScene();
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes[0].path != ScenePath)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            if (PlayerSettings.companyName == "DefaultCompany") PlayerSettings.companyName = "Sanguo";
            if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName == "sanguozhi2-unity" || PlayerSettings.productName.StartsWith("New Unity"))
                PlayerSettings.productName = "三国志II 霸王的大陆";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            if (PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS).StartsWith("com.DefaultCompany") || PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.iOS) == "")
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, "com.sanguo.bawang");
            KeepRuntimeVariants();
            var active = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty) EditorSceneManager.OpenScene(ScenePath);
        }

        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // 雾由代码在运行时开启（地图 / 交锋 / 单挑都是线性雾）；场景里也开着，免得着色器剥离把雾变体去掉
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("三国志II：已创建主场景 " + ScenePath + "（游戏由代码自动生成，场景保持为空即可）");
        }

        // 着色器变体剥离：雾只在运行时开启（Game / Clash / DuelGame 都用 FogMode.Linear），缺省的“自动”按构建场景
        // 判断用到的雾模式，空场景没有雾 → 真机构建里 LowPoly / LowPolyFade / Water 的 FOG_LINEAR 被剥掉。
        // 改为“自定义”并保留线性雾；Art.LowPoly 在运行时创建的材质开了 GPU Instancing，也保留全部 instancing 变体。
        // ProjectSettings 不在 git 里，每次克隆都会重建，所以每次打开工程都检查一遍。
        static void KeepRuntimeVariants()
        {
            try
            {
                var gs = GraphicsSettings.GetGraphicsSettings();
                if (gs == null) return;
                var so = new SerializedObject(gs);
                bool changed = false;
                var fog = so.FindProperty("m_FogStripping");
                if (fog != null && fog.intValue != 1) { fog.intValue = 1; changed = true; }
                var lin = so.FindProperty("m_FogKeepLinear");
                if (lin != null && !lin.boolValue) { lin.boolValue = true; changed = true; }
                var inst = so.FindProperty("m_InstancingStripping");
                if (inst != null && inst.intValue != 2) { inst.intValue = 2; changed = true; }
                if (!changed) return;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log("三国志II：图形设置改为保留线性雾与 instancing 着色器变体");
            }
            catch (System.Exception e) { Debug.LogWarning("三国志II：无法修改图形设置的变体剥离 " + e.Message); }
        }

        [MenuItem("三国志II/打开主场景")]
        static void OpenMain() { if (!File.Exists(ScenePath)) CreateScene(); EditorSceneManager.OpenScene(ScenePath); }

        [MenuItem("三国志II/删除存档")]
        static void DeleteSave()
        {
            // 第二版存档在 PlayerPrefs（GameState.SaveKey）；同时清掉第一版的 PlayerPrefs 键与存档文件、移动提示标记
            bool any = false;
            foreach (var k in new[] { Sanguo.GameState.SaveKey, Sanguo.GameState.LegacySaveKey })
                if (PlayerPrefs.HasKey(k)) { PlayerPrefs.DeleteKey(k); any = true; Debug.Log("已删除存档：PlayerPrefs " + k); }
            PlayerPrefs.DeleteKey("sanguozhi2_tip_move");
            PlayerPrefs.Save();
            var p = Path.Combine(Application.persistentDataPath, "sanguozhi2_save.json");
            if (File.Exists(p)) { File.Delete(p); any = true; Debug.Log("已删除旧版存档文件：" + p); }
            if (!any) Debug.Log("没有存档。");
        }
    }
}
