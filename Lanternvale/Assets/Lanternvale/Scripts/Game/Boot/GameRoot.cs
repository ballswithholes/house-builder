// Entry point. Put a GameRoot in a scene (menu: Lanternvale > Create Game Scene) — or just press
// Play in any scene: AutoBoot creates one. Owns the database, global services and the game mode.
using System;
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public enum GameMode { Boot, MainMenu, CharacterCreation, Exploration, Dialogue, Combat, GameOver }

    [DefaultExecutionOrder(-500)]
    public sealed partial class GameRoot : MonoBehaviour
    {
        public static GameRoot Instance { get; private set; }

        public GameDatabase Db { get; private set; }
        public GameMode Mode { get; private set; } = GameMode.Boot;
        public event Action<GameMode, GameMode> ModeChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (Instance != null) return;
            new GameObject("Lanternvale").AddComponent<GameRoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;

            Lanternvale.Util.Log.InfoHandler = s => Debug.Log("[Lanternvale] " + s);
            Lanternvale.Util.Log.WarnHandler = s => Debug.LogWarning("[Lanternvale] " + s);
            Lanternvale.Util.Log.ErrorHandler = s => Debug.LogError("[Lanternvale] " + s);

            ArtLibrary.Init();
            GameInput.EnsureDriver();
            var rig = CameraRig.Create();
            DontDestroyOnLoad(rig.gameObject);
            Db = LoadDatabase();
            OnBoot();
        }

        /// <summary>Loads every JSON TextAsset under Resources/Data into a validated database.</summary>
        public static GameDatabase LoadDatabase()
        {
            var files = new List<KeyValuePair<string, string>>();
            foreach (var ta in Resources.LoadAll<TextAsset>("Data"))
                files.Add(new KeyValuePair<string, string>(ta.name, ta.text));
            var db = GameDatabase.Load(files);
            if (db.Problems.Count > 0)
                Debug.LogWarning($"[Lanternvale] {db.Problems.Count} data problems (run Lanternvale > Validate Data):\n" +
                                 string.Join("\n", db.Problems.GetRange(0, Math.Min(40, db.Problems.Count))));
            Debug.Log($"[Lanternvale] Loaded {files.Count} data files: {db.Classes.Count} classes, {db.Abilities.Count} abilities, {db.Talents.Count} talents, {db.Items.Count} items, {db.Creatures.Count} creatures, {db.Maps.Count} maps.");
            return db;
        }

        public void SetMode(GameMode mode)
        {
            if (mode == Mode) return;
            var old = Mode;
            Mode = mode;
            ModeChanged?.Invoke(old, mode);
        }

        /// <summary>Extension point implemented by the game-flow partial (creates controllers and UI).</summary>
        partial void OnBoot();
    }
}
