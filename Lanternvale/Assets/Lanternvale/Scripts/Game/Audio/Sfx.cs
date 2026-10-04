// Sound effects: Sfx.Play("hit_physical", worldPos). Clips are synthesized once (SfxSynth) and
// played through a small pool of AudioSources; world positions pan gently across the stereo field
// and fade a little when off-screen. Volumes come from GameAudio (master × sfx).
using System.Collections.Generic;
using Lanternvale.Data;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class Sfx
    {
        static Dictionary<string, AudioClip> clips;
        static SfxPlayer player;
        static readonly HashSet<string> warned = new HashSet<string>();
        static volatile Dictionary<string, float[]> readyPcm;
        static volatile bool generating;
        static readonly List<string> genErrors = new List<string>();
        static float genStart;

        /// <summary>True once the clips exist (synthesis runs on a worker thread for a fraction of a second after boot).</summary>
        public static bool Ready => clips != null;

        /// <summary>All synthesized effect ids.</summary>
        public static readonly string[] Ids =
        {
            "ui_click", "ui_open", "ui_close", "hit_physical", "hit_crit", "swing", "bow", "cast_start",
            "impact_fire", "impact_frost", "impact_arcane", "impact_shadow", "impact_holy", "impact_nature",
            "heal", "buff", "debuff", "death", "level_up", "quest", "coin", "footstep_grass", "chest_open", "door",
        };

        /// <summary>Generates the clips and the source pool (idempotent; GameAudio.Init calls it).</summary>
        public static void Init()
        {
            if (clips != null && player != null) return;
            if (clips == null && !generating)
            {
                generating = true;
                genStart = Time.realtimeSinceStartup;
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                    readyPcm = SfxSynth.GeneratePcm(genErrors);
                else
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { readyPcm = SfxSynth.GeneratePcm(genErrors); }
                        catch (System.Exception e) { lock (genErrors) genErrors.Add(e.Message); readyPcm = new Dictionary<string, float[]>(); }
                    });
            }
            if (player == null)
            {
                var go = new GameObject("SFX");
                go.transform.SetParent(PresentationHost.Root.transform, false);
                player = go.AddComponent<SfxPlayer>();
            }
        }

        /// <summary>Main thread: turns finished PCM into AudioClips (called by SfxPlayer.Update).</summary>
        internal static void PollReady()
        {
            if (clips != null) return;
            var pcm = readyPcm;
            if (pcm == null) return;
            var d = new Dictionary<string, AudioClip>(System.StringComparer.Ordinal);
            foreach (var kv in pcm) d[kv.Key] = SynthBuffer.ToClip("sfx_" + kv.Key, kv.Value);
            clips = d;
            readyPcm = null;
            Debug.Log($"[Lanternvale] Synthesized {clips.Count} sound effects ({(Time.realtimeSinceStartup - genStart) * 1000f:0} ms, worker thread).");
            lock (genErrors)
                foreach (var e in genErrors) Debug.LogWarning("[Lanternvale] sfx synthesis failed: " + e);
        }

        /// <summary>Plays an effect. worldPos (optional) pans it and softens it when off-screen.</summary>
        public static void Play(string id, Vector2? worldPos = null) => Play(id, worldPos, 1f, 1f);

        /// <summary>Plays an effect with an extra volume and pitch multiplier.</summary>
        public static void Play(string id, Vector2? worldPos, float volume, float pitch)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (player == null) GameAudio.Init();
            if (clips == null) PollReady();
            if (clips == null || player == null) return; // still synthesizing (first fraction of a second)
            if (!clips.TryGetValue(id, out var clip))
            {
                id = Alias(id);
                if (id == null || !clips.TryGetValue(id, out clip))
                {
                    if (warned.Add(id ?? "null")) Debug.LogWarning("[Lanternvale] Unknown sfx id: " + id);
                    return;
                }
            }
            player.Play(id, clip, worldPos, volume, pitch);
        }

        /// <summary>School impact sound ("impact_fire"…; Physical → "hit_physical").</summary>
        public static void Impact(School school, Vector2? worldPos = null, bool crit = false)
        {
            if (school == School.Physical) Play(crit ? "hit_crit" : "hit_physical", worldPos);
            else
            {
                Play(ImpactId(school), worldPos);
                if (crit) Play("hit_crit", worldPos, 0.5f, 1.2f);
            }
        }

        public static string ImpactId(School s)
        {
            switch (s)
            {
                case School.Fire: return "impact_fire";
                case School.Frost: return "impact_frost";
                case School.Arcane: return "impact_arcane";
                case School.Shadow: return "impact_shadow";
                case School.Holy: return "impact_holy";
                case School.Nature: return "impact_nature";
                default: return "hit_physical";
            }
        }

        static string Alias(string id)
        {
            switch (id)
            {
                case "click": return "ui_click";
                case "open": return "ui_open";
                case "close": return "ui_close";
                case "hit": case "impact_physical": return "hit_physical";
                case "crit": return "hit_crit";
                case "footstep": return "footstep_grass";
                case "gold": case "loot": return "coin";
                case "levelup": return "level_up";
                case "chest": return "chest_open";
                case "cast": return "cast_start";
                default: return null;
            }
        }

        public static bool Has(string id) => clips != null && id != null && clips.ContainsKey(id);
        public static AudioClip Clip(string id) => clips != null && id != null && clips.TryGetValue(id, out var c) ? c : null;
    }

    /// <summary>Pooled AudioSources for Sfx (one GameObject, no per-sound allocations).</summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        const int PoolSize = 16;
        readonly AudioSource[] sources = new AudioSource[PoolSize];
        readonly string[] playingId = new string[PoolSize];
        readonly float[] startTime = new float[PoolSize];
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        Camera listenerCheckedFor;
        int next;

        static readonly HashSet<string> Varied = new HashSet<string> { "hit_physical", "hit_crit", "swing", "bow", "footstep_grass", "coin", "impact_nature", "impact_fire" };

        void Awake()
        {
            for (int i = 0; i < PoolSize; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.loop = false;
                s.dopplerLevel = 0f;
                s.priority = 64;
                sources[i] = s;
            }
        }

        void Update()
        {
            if (!Sfx.Ready) Sfx.PollReady();
            var cam = PresentationHost.Cam;
            if (cam != listenerCheckedFor)
            {
                listenerCheckedFor = cam;
                GameAudio.EnsureListener(cam);
            }
        }

        public void Play(string id, AudioClip clip, Vector2? worldPos, float volume, float pitch)
        {
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(id, out var last) && now - last < 0.03f) return;
            lastPlayed[id] = now;

            // limit simultaneous instances of the same sound
            int same = 0, sameOldest = -1;
            for (int i = 0; i < PoolSize; i++)
                if (sources[i].isPlaying && playingId[i] == id)
                {
                    same++;
                    if (sameOldest < 0 || startTime[i] < startTime[sameOldest]) sameOldest = i;
                }

            int slot = -1;
            if (same >= 4) slot = sameOldest;
            else
            {
                for (int k = 0; k < PoolSize; k++)
                {
                    int i = (next + k) % PoolSize;
                    if (!sources[i].isPlaying) { slot = i; break; }
                }
                if (slot < 0)
                {
                    slot = 0;
                    for (int i = 1; i < PoolSize; i++) if (startTime[i] < startTime[slot]) slot = i;
                }
            }
            next = (slot + 1) % PoolSize;

            float pan = 0f, att = 1f;
            if (worldPos.HasValue)
            {
                var cam = PresentationHost.Cam;
                if (cam != null)
                {
                    float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
                    var cp = cam.transform.position;
                    float dx = worldPos.Value.x - cp.x, dy = worldPos.Value.y - cp.y;
                    pan = Mathf.Clamp(dx / Mathf.Max(1f, halfW), -1f, 1f) * 0.55f;
                    float outX = Mathf.Max(0f, Mathf.Abs(dx) - halfW), outY = Mathf.Max(0f, Mathf.Abs(dy) - halfH);
                    att = Mathf.Clamp01(1f - Mathf.Sqrt(outX * outX + outY * outY) / 14f) * 0.75f + 0.25f;
                    if (outX <= 0f && outY <= 0f) att = 1f;
                }
            }

            var s = sources[slot];
            s.Stop();
            s.clip = clip;
            s.panStereo = pan;
            s.volume = Mathf.Clamp01(GameAudio.SfxGain * volume * att);
            float p = pitch;
            if (Varied.Contains(id)) p *= Random.Range(0.94f, 1.06f);
            s.pitch = p;
            s.Play();
            playingId[slot] = id;
            startTime[slot] = now;
        }
    }
}
