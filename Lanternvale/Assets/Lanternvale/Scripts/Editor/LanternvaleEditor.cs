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
        // Bump whenever the settings below change: the Asset Database then reimports the textures this
        // postprocessor handles, instead of keeping Library artifacts built with the old settings.
        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Lanternvale/Resources/Art/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            // World art is authored at 170-600 px/m but the default camera shows ~87 px/m at 1080p, and icons and
            // portraits are drawn well below their size. Without a mip chain, bilinear sampling skips most texels, so
            // ink lines and hair shimmer as units breathe/bob and the camera moves sub-pixel. Mips + trilinear keep
            // them stable (at magnification this samples mip 0, so it looks exactly like bilinear). UI art (vignette,
            // logo, parchment) is drawn at or above its native size, so it keeps a single level.
            bool mips = !path.Contains("/UI/");
            ti.mipmapEnabled = mips;
            ti.alphaIsTransparency = true; // dilates colour under transparent texels so lower mips get no dark fringes
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear;
            ti.maxTextureSize = 4096;
            bool tile = path.Contains("/Backgrounds/") || path.Contains("/Ground/");
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
            // UniversalRenderPipelineAsset lives in the main URP runtime assembly; Renderer2DData moved to
            // Unity.RenderPipelines.Universal.2D.Runtime in URP 17 / Unity 6 (2023.2+), so look in both.
            var pipelineType = FindType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset",
                UrpRuntimeAssembly);
            if (pipelineType == null)
            {
                if (EditorUtility.DisplayDialog("Lanternvale", "The Universal Render Pipeline package is not installed. Install it now? Run this menu again after Unity finishes importing.", "Install URP", "Cancel"))
                    UnityEditor.PackageManager.Client.Add("com.unity.render-pipelines.universal");
                return;
            }
            var rendererDataType = FindType("UnityEngine.Rendering.Universal.Renderer2DData",
                Urp2DRuntimeAssembly, UrpRuntimeAssembly);
            if (rendererDataType == null)
            {
                EditorUtility.DisplayDialog("Lanternvale", "URP is installed, but its 2D Renderer (Renderer2DData) could not be found in this URP version. Create a URP asset with a 2D Renderer manually (Assets > Create > Rendering > URP Asset (with 2D Renderer)) and assign it in Project Settings > Graphics.", "OK");
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
            LanternvaleShaderIncludes.Ensure();
            Debug.Log("[Lanternvale] URP with the 2D Renderer is configured. Sprites now react to 2D lights.");
        }

        const string UrpRuntimeAssembly = "Unity.RenderPipelines.Universal.Runtime";
        const string Urp2DRuntimeAssembly = "Unity.RenderPipelines.Universal.2D.Runtime";

        /// <summary>
        /// Resolves <paramref name="fullName"/> from the named assemblies (in order), then from any loaded
        /// assembly, so a type that moved between URP assemblies across Unity versions is still found.
        /// </summary>
        static Type FindType(string fullName, params string[] assemblies)
        {
            foreach (var asm in assemblies)
            {
                var t = Type.GetType($"{fullName}, {asm}");
                if (t != null) return t;
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName, false); }
                catch (Exception) { continue; }
                if (t != null) return t;
            }
            return null;
        }

        [MenuItem("Lanternvale/Open Save Folder", priority = 60)]
        public static void OpenSaveFolder() => EditorUtility.RevealInFinder(Application.persistentDataPath);
    }
}

namespace Lanternvale.EditorTools
{
    /// <summary>
    /// Sprites are created at runtime, so nothing in a scene references URP's 2D sprite shaders and a player
    /// build could strip them (sprites would then ignore Light2D). Before every build, and from the URP setup
    /// menu, both shaders are added to Project Settings > Graphics > Always Included Shaders when URP is present.
    /// </summary>
    public sealed class LanternvaleShaderIncludes : UnityEditor.Build.IPreprocessBuildWithReport
    {
        static readonly string[] Shaders =
        {
            "Universal Render Pipeline/2D/Sprite-Lit-Default",
            "Universal Render Pipeline/2D/Sprite-Unlit-Default",
        };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => Ensure();

        [MenuItem("Lanternvale/Setup/Include 2D Sprite Shaders in Builds", priority = 41)]
        public static void Ensure()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0 || settings[0] == null) return;
            var so = new SerializedObject(settings[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray) return;
            bool changed = false;
            foreach (var name in Shaders)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue; // URP not installed: nothing to include
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                if (present) continue;
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                list.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
                changed = true;
            }
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[Lanternvale] Added URP 2D sprite shaders to Always Included Shaders so runtime sprites stay lit in builds.");
        }
    }
}
