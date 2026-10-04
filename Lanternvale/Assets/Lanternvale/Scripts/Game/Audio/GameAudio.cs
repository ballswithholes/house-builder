// Audio entry point: volume settings, initialisation (SFX synthesis, music source, Ui.Sfx hook)
// and AudioListener safety. Auto-initialises after the first scene loads, so Ui buttons click and
// Sfx.Play works without any setup; call Music.Play(mood) to start the score.
using UnityEngine;

namespace Lanternvale.Game
{
    public static class GameAudio
    {
        /// <summary>Volume settings (0..1). Changes apply immediately (music smoothly).</summary>
        public static float MasterVolume = 1f;
        public static float MusicVolume = 0.6f;
        public static float SfxVolume = 0.85f;
        public static bool Muted;

        public static bool Initialized { get; private set; }

        internal static float SfxGain => Muted ? 0f : Mathf.Clamp01(MasterVolume) * Mathf.Clamp01(SfxVolume);
        internal static float MusicGain => Muted ? 0f : Mathf.Clamp01(MasterVolume) * Mathf.Clamp01(MusicVolume);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInit() => Init();

        /// <summary>Synthesizes the SFX, creates the music source and hooks Ui.Sfx. Idempotent.</summary>
        public static void Init()
        {
            if (Initialized) return;
            Initialized = true;
            LoadPrefs();
            Sfx.Init();
            Music.Ensure();
            Ui.Sfx = id => Sfx.Play(id);
        }

        const string KMaster = "lv_vol_master", KMusic = "lv_vol_music", KSfx = "lv_vol_sfx", KMute = "lv_muted";

        /// <summary>Reads volumes from PlayerPrefs (done by Init).</summary>
        public static void LoadPrefs()
        {
            MasterVolume = PlayerPrefs.GetFloat(KMaster, MasterVolume);
            MusicVolume = PlayerPrefs.GetFloat(KMusic, MusicVolume);
            SfxVolume = PlayerPrefs.GetFloat(KSfx, SfxVolume);
            Muted = PlayerPrefs.GetInt(KMute, Muted ? 1 : 0) != 0;
        }

        /// <summary>Saves volumes to PlayerPrefs (call from the options screen).</summary>
        public static void SavePrefs()
        {
            PlayerPrefs.SetFloat(KMaster, MasterVolume);
            PlayerPrefs.SetFloat(KMusic, MusicVolume);
            PlayerPrefs.SetFloat(KSfx, SfxVolume);
            PlayerPrefs.SetInt(KMute, Muted ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Makes sure exactly one enabled AudioListener exists (CameraRig creates its camera without
        /// one). Adds it to cam when no other active listener is found.
        /// </summary>
        public static void EnsureListener(Camera cam)
        {
            if (cam == null) return;
            if (cam.GetComponent<AudioListener>() != null) return;
            foreach (var l in Resources.FindObjectsOfTypeAll<AudioListener>())
                if (l != null && l.isActiveAndEnabled && l.gameObject.scene.IsValid()) return;
            cam.gameObject.AddComponent<AudioListener>();
        }
    }
}
