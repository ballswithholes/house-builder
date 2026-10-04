using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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
            var active = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty) EditorSceneManager.OpenScene(ScenePath);
        }

        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("三国志II：已创建主场景 " + ScenePath + "（游戏由代码自动生成，场景保持为空即可）");
        }

        [MenuItem("三国志II/打开主场景")]
        static void OpenMain() { if (!File.Exists(ScenePath)) CreateScene(); EditorSceneManager.OpenScene(ScenePath); }

        [MenuItem("三国志II/删除存档")]
        static void DeleteSave()
        {
            var p = Path.Combine(Application.persistentDataPath, "sanguozhi2_save.json");
            if (File.Exists(p)) { File.Delete(p); Debug.Log("已删除存档：" + p); } else Debug.Log("没有存档。");
        }
    }
}
