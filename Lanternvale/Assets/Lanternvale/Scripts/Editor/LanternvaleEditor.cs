// Editor tooling: art import settings, scene creation, data validation and rendering setup.
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
            Configure3D(interactive: false);
            EnsureGameScene();
            Debug.Log($"[Lanternvale] Created {ScenePath} and added it to Build Settings. Press Play!");
        }

        /// <summary>Command-line entry point (-executeMethod): creates the game scene if needed and opens it.</summary>
        public static void OpenGameScene()
        {
            Configure3D(interactive: false);
            var path = File.Exists(ScenePath) ? ScenePath : EnsureGameScene();
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Debug.Log("[Lanternvale] Game scene open. Press Play!");
        }

        /// <summary>Creates the game scene (one GameRoot) and puts it first in Build Settings, without prompts.</summary>
        public static string EnsureGameScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Lanternvale");
            go.AddComponent<GameRoot>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return ScenePath;
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
        /// The 3D presentation renders with its own shaders (Resources/Shaders, own lighting), which are written for the
        /// built-in render pipeline's forward path. Projects created from a URP template (e.g. "Universal 2D") get their
        /// render pipeline asset unassigned — URP stays installed but unused — so every camera renders built-in.
        /// </summary>
        [MenuItem("Lanternvale/Setup/Use the Built-in Render Pipeline (3D)", priority = 40)]
        public static void SetupRendering() => Configure3D(interactive: true);

        /// <summary>Batch-safe rendering setup. Returns true when something changed.</summary>
        public static bool Configure3D(bool interactive)
        {
            bool changed = false;
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                Debug.Log($"[Lanternvale] Unassigning the render pipeline asset '{GraphicsSettings.defaultRenderPipeline.name}' (Lanternvale renders with the built-in pipeline).");
                GraphicsSettings.defaultRenderPipeline = null;
                changed = true;
            }
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline != null) { QualitySettings.renderPipeline = null; changed = true; }
                if (QualitySettings.antiAliasing < 4) { QualitySettings.antiAliasing = 4; changed = true; }
            }
            QualitySettings.SetQualityLevel(current, false);
            if (changed) AssetDatabase.SaveAssets();
            if (interactive)
                EditorUtility.DisplayDialog("Lanternvale", changed
                    ? "Rendering set up: built-in render pipeline, 4x MSAA. Press Play."
                    : "Rendering was already set up (built-in render pipeline).", "OK");
            return changed;
        }

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
