// Sound effects: Sfx.Play("hit_blade", worldPos). Clips are synthesized once (SfxSynth, on a worker thread) and played
// through a pool of AudioSources; world positions pan gently across the stereo field and fade a little when off-screen.
// Volumes come from GameAudio (master × sfx).
//
// Variants: the synthesizer publishes "<id>#<n>" (n = 1…K). They are grouped by base id and played round-robin in a
// fresh random order every cycle, never the same variant twice in a row, with ±3 % pitch and ±1.5 dB of jitter (UI
// chimes and stingers play as authored). Recorded overrides: any .wav/.ogg under Resources/Audio/Sfx/<id>/ replaces the
// synthesized variants of <id> (absent folder = no change; Resources/Audio/Sfx/CREDITS.md says what may go there).
//
// Voice rules (keyed on the base id): a 30 ms rate limit, at most 4 overlapping voices of one id, at most 6 new voices
// per frame (UI ids exempt), 24 pooled sources. Sfx.PlayAfter queues a sound on scaled time, so a body fall or an
// arrow thunk keeps in step with the presentation speed and waits while the game is paused.
// The pure bookkeeping lives in Core/Util/SfxBank.cs (tested by TestsSfxPlayback); combat semantics in CombatSfx.
using System.Collections.Generic;
using Lanternvale.Data;
using Lanternvale.Rules;
using Lanternvale.Util;
using UnityEngine;

namespace Lanternvale.Game
{
    public static class Sfx
    {
        /// <summary>Base id → its variants (synthesized, or the recorded overrides).</summary>
        static Dictionary<string, AudioClip[]> bank;
        static readonly Dictionary<string, SfxVariantPicker> pickers = new Dictionary<string, SfxVariantPicker>(System.StringComparer.Ordinal);
        static SfxPlayer player;
        static readonly HashSet<string> warned = new HashSet<string>(System.StringComparer.Ordinal);
        static volatile Dictionary<string, float[]> readyPcm;
        static volatile bool generating;
        static readonly List<string> genErrors = new List<string>();
        static float genStart;
        static readonly System.Random rng = new System.Random(0x5fc);

        /// <summary>Resources folder of the recorded overrides: Resources/Audio/Sfx/&lt;id&gt;/*.wav.</summary>
        public const string OverrideFolder = "Audio/Sfx/";

        /// <summary>True once the clips exist (synthesis runs on a worker thread for a fraction of a second after boot).</summary>
        public static bool Ready => bank != null;

        /// <summary>Every effect id the game knows: the original 24 and the expansion's (Docs/Expansion.md §4).</summary>
        public static readonly string[] Ids = (string[])CombatSounds.AllIds.Clone();

        /// <summary>Base ids that have clips right now (empty until Ready).</summary>
        public static IEnumerable<string> LoadedIds => bank != null ? bank.Keys : (IEnumerable<string>)System.Array.Empty<string>();

        /// <summary>Number of variants of an id (0 when unknown or not ready yet).</summary>
        public static int VariantCount(string id)
        {
            if (bank == null || string.IsNullOrEmpty(id)) return 0;
            return bank.TryGetValue(SfxIds.BaseOf(id), out var v) ? v.Length : 0;
        }

        /// <summary>
        /// Play-mode entry (GameAudio.ResetStatics): forgets the destroyed player and, if Unity unloaded any
        /// synthesized clip with the previous session, lets Init synthesize them again.
        /// </summary>
        internal static void ResetStatics()
        {
            player = null;
            var b = bank;
            if (b == null) return;
            foreach (var set in b.Values)
            {
                bool lost = set == null;
                if (!lost) foreach (var clip in set) if (clip == null) { lost = true; break; }
                if (lost) { bank = null; pickers.Clear(); generating = false; break; }
            }
        }

        /// <summary>Generates the clips and the source pool (idempotent; GameAudio.Init calls it).</summary>
        public static void Init()
        {
            if (bank != null && player != null) return;
            if (bank == null && !generating)
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

        /// <summary>
        /// Main thread: turns finished PCM into AudioClips grouped by base id, then lets recorded files under
        /// Resources/Audio/Sfx/&lt;id&gt;/ replace them (called by SfxPlayer.Update and the first Play).
        /// </summary>
        internal static void PollReady()
        {
            if (bank != null) return;
            var pcm = readyPcm;
            if (pcm == null) return;
            var d = new Dictionary<string, AudioClip[]>(System.StringComparer.Ordinal);
            int variants = 0;
            foreach (var kv in SfxIds.Group(pcm.Keys))
            {
                var list = new List<AudioClip>(kv.Value.Count);
                foreach (var key in kv.Value)
                    if (pcm.TryGetValue(key, out var data) && data != null && data.Length > 0)
                        list.Add(SynthBuffer.ToClip("sfx_" + key, data));
                if (list.Count == 0) continue;
                d[kv.Key] = list.ToArray();
                variants += list.Count;
            }
            int synthesized = d.Count;

            // recorded overrides: every id the game knows plus anything the synthesizer published
            var probe = new List<string>(Ids);
            probe.AddRange(d.Keys);
            List<string> overridden;
            try { overridden = SfxOverrides.Apply(d, probe, LoadOverride); }
            catch (System.Exception e) { overridden = new List<string>(); Debug.LogWarning("[Lanternvale] sfx overrides failed: " + e.Message); }

            bank = d;
            pickers.Clear();
            readyPcm = null;
            Debug.Log($"[Lanternvale] Synthesized {synthesized} sound effects ({variants} variants, " +
                      $"{(Time.realtimeSinceStartup - genStart) * 1000f:0} ms, worker thread)" +
                      (overridden.Count > 0 ? $"; recorded overrides: {string.Join(", ", overridden)}." : "."));
            lock (genErrors)
                foreach (var e in genErrors) Debug.LogWarning("[Lanternvale] sfx synthesis failed: " + e);
        }

        /// <summary>Recorded variants of a base id: every clip in Resources/Audio/Sfx/&lt;id&gt;/ (name order), or a single file Audio/Sfx/&lt;id&gt;.</summary>
        static AudioClip[] LoadOverride(string id)
        {
            var set = Resources.LoadAll<AudioClip>(OverrideFolder + id);
            if (set != null && set.Length > 0)
            {
                System.Array.Sort(set, (a, b) => string.CompareOrdinal(a != null ? a.name : "", b != null ? b.name : ""));
                return set;
            }
            var one = Resources.Load<AudioClip>(OverrideFolder + id);
            return one != null ? new[] { one } : null;
        }

        /// <summary>Plays an effect. worldPos (optional) pans it and softens it when off-screen.</summary>
        public static void Play(string id, Vector2? worldPos = null) => Play(id, worldPos, 1f, 1f);

        /// <summary>Plays an effect with an extra volume and pitch multiplier ("id#n" plays that exact variant).</summary>
        public static void Play(string id, Vector2? worldPos, float volume, float pitch)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (player == null) GameAudio.Init(); // re-entrant: recreates a destroyed player
            if (bank == null) PollReady();
            if (bank == null || player == null) return; // still synthesizing (first fraction of a second)
            var baseId = Resolve(id);
            if (baseId == null) return;
            var set = bank[baseId];
            AudioClip clip;
            int exact = SfxIds.VariantOf(id);
            if (exact > 0 && SfxIds.BaseOf(id) == baseId && exact <= set.Length) clip = set[exact - 1];
            else
            {
                if (!pickers.TryGetValue(baseId, out var pick) || pick.Count != set.Length)
                    pickers[baseId] = pick = new SfxVariantPicker(set.Length);
                clip = set[pick.Next(rng)];
            }
            if (clip == null) return;
            if (SfxJitter.IsJittered(baseId))
            {
                volume *= SfxJitter.Gain(rng);
                pitch *= SfxJitter.Pitch(rng);
            }
            player.Play(baseId, clip, worldPos, volume, pitch);
        }

        /// <summary>
        /// Plays an effect after a delay in scaled (presentation) seconds: follows Time.timeScale (combat speed, fast
        /// forward) and waits while the game is paused. A delay ≤ 0 plays now.
        /// </summary>
        public static void PlayAfter(string id, float scaledDelay, Vector2? worldPos = null, float volume = 1f, float pitch = 1f)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!(scaledDelay > 0f)) { Play(id, worldPos, volume, pitch); return; }
            if (player == null) GameAudio.Init();
            if (player == null) return;
            player.Enqueue(id, scaledDelay, worldPos, volume, pitch);
        }

        /// <summary>Drops every queued PlayAfter sound (e.g. when the world is torn down).</summary>
        public static void CancelDelayed()
        {
            if (player != null) player.ClearQueue();
        }

        /// <summary>
        /// The base id that plays for id: itself, a legacy alias ("hit" → "hit_physical"), or — for an expansion id that is
        /// not synthesized (yet) — its nearest original sound. Unknown ids warn once, by their own name. Null = silence.
        /// </summary>
        static string Resolve(string id)
        {
            var baseId = SfxIds.BaseOf(id);
            if (bank.ContainsKey(baseId)) return baseId;
            var alias = Alias(baseId);
            if (alias != null && bank.ContainsKey(alias)) return alias;
            var fallback = Fallback(baseId);
            if (warned.Add(baseId))
                Debug.LogWarning("[Lanternvale] Unknown sfx id: " + baseId +
                                 (fallback != null && bank.ContainsKey(fallback) ? " (playing " + fallback + " instead)" : ""));
            return fallback != null && bank.ContainsKey(fallback) ? fallback : null;
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

        /// <summary>The original sound closest to an expansion id (used only while that id has no clip). Null = stay silent.</summary>
        internal static string Fallback(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("hit_")) return "hit_physical";
            if (id.StartsWith("cast_")) return "cast_start";
            if (id.StartsWith("footstep_")) return "footstep_grass";
            if (id.StartsWith("vo_")) return "death";
            if (id.StartsWith("loot_") || id.StartsWith("quest_") || id == "set_complete" || id == "secret_found") return "quest";
            switch (id)
            {
                case "swing_light": case "swing_heavy": case "miss": case "dodge": return "swing";
                case "xbow_release": case "throw_release": case "gun_fire": return "bow";
                case "wand_zap": case "portal_whoosh": return "cast_start";
                case "impact_lightning": return "impact_nature";
                case "parry": case "block_wood": case "block_metal": case "stomp": return "hit_physical";
                case "resist": case "immune": case "boss_pull": case "raid_warning": return "debuff";
                case "absorb": case "shout_horn": return "buff";
                case "door_stone": return "door";
                default: return null;   // mat_*, body falls, armour clatter/jingle, arrow flight: silence beats a wrong sound
            }
        }

        /// <summary>True when id (or its alias) has clips.</summary>
        public static bool Has(string id)
        {
            if (bank == null || string.IsNullOrEmpty(id)) return false;
            var b = SfxIds.BaseOf(id);
            if (bank.ContainsKey(b)) return true;
            var a = Alias(b);
            return a != null && bank.ContainsKey(a);
        }

        /// <summary>The first variant of id ("id#n": that variant), or null.</summary>
        public static AudioClip Clip(string id)
        {
            if (bank == null || string.IsNullOrEmpty(id)) return null;
            var b = SfxIds.BaseOf(id);
            if (!bank.TryGetValue(b, out var set))
            {
                var a = Alias(b);
                if (a == null || !bank.TryGetValue(a, out set)) return null;
            }
            int n = SfxIds.VariantOf(id);
            return n > 0 && n <= set.Length ? set[n - 1] : set[0];
        }
    }

    /// <summary>Pooled AudioSources for Sfx (one GameObject, no per-sound allocations) and the delayed-play queue.</summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        const int PoolSize = SfxVoiceRules.PoolSize;
        readonly AudioSource[] sources = new AudioSource[PoolSize];
        readonly string[] playingId = new string[PoolSize];
        readonly float[] startTime = new float[PoolSize];
        readonly bool[] playing = new bool[PoolSize];
        readonly SfxVoiceRules rules = new SfxVoiceRules();
        Camera listenerCheckedFor;
        int next;

        struct Pending
        {
            public string Id;
            public Vector2? Pos;
            public float Volume, Pitch;
        }

        readonly SfxDelayQueue<Pending> queue = new SfxDelayQueue<Pending>();
        readonly List<Pending> due = new List<Pending>();
        float queueClock;

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
            queueClock = Time.time;
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

            // delayed sounds run on scaled time: they follow the presentation speed and wait while paused
            float now = Time.time;
            if (now < queueClock) queue.Rebase(queueClock, now);
            queueClock = now;
            if (queue.Count > 0)
            {
                due.Clear();
                queue.PopDue(now, due);
                for (int i = 0; i < due.Count; i++)
                {
                    var p = due[i];
                    Sfx.Play(p.Id, p.Pos, p.Volume, p.Pitch);
                }
                due.Clear();
            }
        }

        public void Enqueue(string id, float scaledDelay, Vector2? worldPos, float volume, float pitch)
        {
            queue.Add(Time.time, scaledDelay, new Pending { Id = id, Pos = worldPos, Volume = volume, Pitch = pitch });
        }

        public void ClearQueue() => queue.Clear();

        public void Play(string baseId, AudioClip clip, Vector2? worldPos, float volume, float pitch)
        {
            float now = Time.unscaledTime;
            if (!rules.TryStart(baseId, now, Time.frameCount)) return;

            for (int i = 0; i < PoolSize; i++) playing[i] = sources[i] != null && sources[i].isPlaying;
            int slot = SfxVoiceRules.ChooseSlot(baseId, playing, playingId, startTime, ref next);
            if (slot < 0 || sources[slot] == null) return;

            float pan = 0f, att = 1f;
            if (worldPos.HasValue)
            {
                var cam = PresentationHost.Cam;
                if (cam != null)
                {
                    // pan from where the sound sits on screen; softer the further it is outside the view (works for any
                    // camera: the ground point is projected, so perspective and yaw are accounted for)
                    var vp = cam.WorldToViewportPoint(new Vector3(worldPos.Value.x, worldPos.Value.y, 0f));
                    if (vp.z <= 0f) { pan = 0f; att = 0.25f; }
                    else
                    {
                        float vx = vp.x * 2f - 1f, vy = vp.y * 2f - 1f;
                        pan = Mathf.Clamp(vx, -1f, 1f) * 0.55f;
                        float outX = Mathf.Max(0f, Mathf.Abs(vx) - 1f), outY = Mathf.Max(0f, Mathf.Abs(vy) - 1f);
                        att = (outX <= 0f && outY <= 0f) ? 1f : Mathf.Clamp01(1f - Mathf.Sqrt(outX * outX + outY * outY) * 0.9f) * 0.75f + 0.25f;
                    }
                }
            }

            var s = sources[slot];
            s.Stop();
            s.clip = clip;
            s.panStereo = pan;
            s.volume = Mathf.Clamp01(GameAudio.SfxGain * volume * att);
            s.pitch = Mathf.Clamp(pitch, 0.25f, 3f);
            s.Play();
            playingId[slot] = baseId;
            startTime[slot] = now;
        }
    }
}
