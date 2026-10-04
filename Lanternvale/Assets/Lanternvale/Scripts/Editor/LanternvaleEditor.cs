// Editor tooling: art import settings, scene creation, data validation and URP 2D setup.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Lanternvale.Data;
using Lanternvale.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lanternvale.EditorTools
{
    /// <summary>Applies sprite-friendly import settings to everything under Resources/Art.</summary>
    public sealed class LanternvaleArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Lanternvale/Resources/Art/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.filterMode = FilterMode.Bilinear;
            ti.maxTextureSize = 4096;
            bool tile = path.Contains("/Background/") || path.Contains("/Ground/");
            ti.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            bool crisp = path.Contains("/Icon") || path.Contains("/UI/") || path.Contains("/Portrait");
            ti.textureCompression = crisp ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            ti.SetTextureSettings(settings);
        }
    }

    public static class LanternvaleMenu
    {
        const string ScenePath = "Assets/Lanternvale/Scenes/Lanternvale.unity";
        const string SettingsDir = "Assets/Lanternvale/Settings";

        [MenuItem("Lanternvale/Create Game Scene", priority = 0)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Lanternvale");
            go.AddComponent<GameRoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Lanternvale] Created {ScenePath} and added it to Build Settings. Press Play!");
        }

        [MenuItem("Lanternvale/Validate Data", priority = 20)]
        public static void ValidateData()
        {
            var files = new List<KeyValuePair<string, string>>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/Lanternvale/Resources/Data" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".json")) continue;
                files.Add(new KeyValuePair<string, string>(p, File.ReadAllText(p)));
            }
            var db = GameDatabase.Load(files);
            var t = Type.GetType("Lanternvale.Rules.Specials, Lanternvale.Core");
            var m = t?.GetMethod("IsImplemented", BindingFlags.Public | BindingFlags.Static);
            if (m != null) DataValidator.IsSpecialImplemented = s => (bool)m.Invoke(null, new object[] { s });
            var problems = DataValidator.Validate(db);
            if (problems.Count == 0)
                Debug.Log($"[Lanternvale] Data OK: {db.Classes.Count} classes, {db.Abilities.Count} abilities, {db.Talents.Count} talents, {db.Items.Count} items, {db.Creatures.Count} creatures, {db.Maps.Count} maps.");
            else
            {
                Debug.LogWarning($"[Lanternvale] {problems.Count} data problems:\n" + string.Join("\n", problems.Take(200)));
            }
        }

        [MenuItem("Lanternvale/Reimport Art", priority = 21)]
        public static void ReimportArt()
        {
            AssetDatabase.ImportAsset("Assets/Lanternvale/Resources/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }

        /// <summary>
        /// Installs URP if needed and creates/assigns a URP asset that uses the 2D Renderer, so sprites
        /// react to Light2D. (Projects created from the "Universal 2D" template already have this.)
        /// </summary>
        [MenuItem("Lanternvale/Setup/Configure URP 2D Renderer", priority = 40)]
        public static void SetupUrp2D()
        {
            const string asm = "Unity.RenderPipelines.Universal.Runtime";
            var rendererDataType = Type.GetType($"UnityEngine.Rendering.Universal.Renderer2DData, {asm}");
            var pipelineType = Type.GetType($"UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, {asm}");
            if (rendererDataType == null || pipelineType == null)
            {
                if (EditorUtility.DisplayDialog("Lanternvale", "The Universal Render Pipeline package is not installed. Install it now? Run this menu again after Unity finishes importing.", "Install URP", "Cancel"))
                    UnityEditor.PackageManager.Client.Add("com.unity.render-pipelines.universal");
                return;
            }
            Directory.CreateDirectory(SettingsDir);
            var dataPath = SettingsDir + "/Lanternvale_2DRenderer.asset";
            var pipePath = SettingsDir + "/Lanternvale_URP2D.asset";
            var data = AssetDatabase.LoadAssetAtPath(dataPath, rendererDataType);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance(rendererDataType);
                AssetDatabase.CreateAsset(data, dataPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath(pipePath, pipelineType) as RenderPipelineAsset;
            if (pipeline == null)
            {
                var create = pipelineType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(mi => mi.Name == "Create" && mi.GetParameters().Length == 1);
                if (create == null) { Debug.LogError("[Lanternvale] UniversalRenderPipelineAsset.Create not found in this URP version. Create a URP asset with a 2D Renderer manually."); return; }
                pipeline = (RenderPipelineAsset)create.Invoke(null, new object[] { data });
                AssetDatabase.CreateAsset(pipeline, pipePath);
            }
            // GraphicsSettings.defaultRenderPipeline (2021.2+) or renderPipelineAsset (older/obsolete in Unity 6)
            var gp = typeof(GraphicsSettings).GetProperty("defaultRenderPipeline", BindingFlags.Public | BindingFlags.Static)
                     ?? typeof(GraphicsSettings).GetProperty("renderPipelineAsset", BindingFlags.Public | BindingFlags.Static);
            gp?.SetValue(null, pipeline, null);
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
            Lighting2D.Reset();
            Debug.Log("[Lanternvale] URP with the 2D Renderer is configured. Sprites now react to 2D lights.");
        }

        [MenuItem("Lanternvale/Open Save Folder", priority = 60)]
        public static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}
