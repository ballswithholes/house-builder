// Generative music front-end: Music.Play("village") crossfades to a mood. The score is synthesized
// continuously by MusicEngine inside OnAudioFilterRead on a dedicated AudioSource (playing a silent
// carrier clip so the filter always runs). Moods: village, forest, shrine, combat, menu.
// Note: OnAudioFilterRead is not supported on WebGL (music is silent there; SFX still work).
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class Music : MonoBehaviour
    {
        public static Music Instance { get; private set; }

        /// <summary>Known moods.</summary>
        public static readonly string[] Moods = { "village", "forest", "shrine", "combat", "menu" };

        /// <summary>Currently requested mood ("" when stopped).</summary>
        public static string Mood { get; private set; } = "";

        MusicEngine engine;
        AudioSource source;
        uint seed = 12345u;

        public static Music Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Music");
            go.transform.SetParent(PresentationHost.Root.transform, false);
            go.AddComponent<AudioSource>();
            Instance = go.AddComponent<Music>();
            return Instance;
        }

        /// <summary>
        /// Crossfades to a mood (no-op if it's already playing). Accepts mood names or map music ids
        /// such as "music_whisperwood" (matched by keyword; unknown names play "village").
        /// </summary>
        public static void Play(string mood, float fadeSeconds = 3f)
        {
            if (string.IsNullOrEmpty(mood)) { Stop(fadeSeconds); return; }
            mood = mood.ToLowerInvariant();
            var m = Ensure();
            if (m.engine == null) return;
            var spec = MoodLibrary.Get(mood);
            if (spec.name == Mood) return;
            Mood = spec.name;
            m.seed = m.seed * 1664525u + 1013904223u;
            m.engine.Submit(new Composer(spec, m.engine.SampleRate, Mathf.Max(0.05f, fadeSeconds), m.seed));
        }

        /// <summary>Fades the music out.</summary>
        public static void Stop(float fadeSeconds = 2f)
        {
            Mood = "";
            if (Instance != null && Instance.engine != null) Instance.engine.Stop(fadeSeconds);
        }

        /// <summary>Mood for a map from MapDef.music (e.g. "music_whisperwood" → forest), else from the map id.</summary>
        public static string MoodForMap(MapDef map)
        {
            if (map == null) return "village";
            return MoodLibrary.Normalize(!string.IsNullOrEmpty(map.music) ? map.music + " " + map.id : map.id);
        }

        /// <summary>Normalised mood name for any mood/music id.</summary>
        public static string Normalize(string moodOrMusicId) => MoodLibrary.Normalize(moodOrMusicId);

        void Awake()
        {
            Instance = this;
            int sr = AudioSettings.outputSampleRate;
            if (sr <= 0) sr = 44100;
            engine = new MusicEngine(sr);
            engine.TargetGain = GameAudio.MusicGain;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 1f;
            source.priority = 0;
            source.bypassReverbZones = true;
            var silent = AudioClip.Create("lv_music_carrier", sr, 1, sr, false);
            silent.SetData(new float[sr], 0);
            source.clip = silent;
            source.Play();
        }

        void Update()
        {
            if (engine == null) return;
            engine.TargetGain = GameAudio.MusicGain;
            if (source != null && !source.isPlaying && source.isActiveAndEnabled) source.Play();
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var e = engine;
            if (e != null) e.Render(data, channels);
        }
    }
}
