using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Sanguo.Audio;

namespace Sanguo
{
    // 程序合成的音效与背景音乐（无需音频文件）
    // - 音效：按公式算出 PCM（22050 Hz 单声道），PlayOneShot 播放（接口不变）。
    // - 背景音乐（第二版，移植自网页版 js/audio.js + js/music.js）：Core/MusicEngine.cs 在后台线程实时合成
    //   （全新创作的曲目、合成民乐 / 管弦乐音色、前瞻调度、0.8 秒交叉淡入淡出、地域变奏、冲锋乐句），
    //   渲染到环形缓冲，由流式 AudioClip（AudioClip.Create + PCMReaderCallback）经 AudioSource.PlayScheduled 播放。
    //   乐器采样在另一条后台线程预渲染，不阻塞主线程与音频线程。
    // 曲目 kind：title map battle battle-defend duel victory defeat ending council clash
    //   （victory / defeat 为一次性短曲，播放中请求 map / council 会等它奏完；
    //    clash 为叠加在当前曲目上的冲锋乐句，跟随主曲调性与拍子，结束后主曲自动恢复）
    public class Sfx : MonoBehaviour
    {
        static Sfx inst;
        AudioSource sfx, music;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        const int Rate = 22050;
        public static bool MusicOn = true, SoundOn = true;

        // ---- 背景音乐状态（同 audio.js）
        const float MUSIC_VOLUME = 0.42f;              // 背景音乐总音量（相对音效）
        const float STINGER_GAP = 15;                   // 冲锋乐句最短间隔（秒）
        static readonly HashSet<string> ONE_SHOT = new HashSet<string> { "victory", "defeat" };
        static readonly HashSet<string> OVERLAY = new HashSet<string> { "clash" };
        static readonly HashSet<string> HOLD_AFTER_ONE_SHOT = new HashSet<string> { "map", "council" };
        public static string MusicKind;                 // 当前请求的曲目 kind
        public static string Culture = "han";           // 当前地域（map / battle 用其变奏）
        public static bool MusicFinished;               // 一次性短曲已奏完
        static string playingKey;                       // 已交给引擎播放（或准备中）的曲目 key
        static int playReq;
        static float musicVol = 1;
        static string heldKind, heldCulture; static float holdUntil = -1;
        static float lastStinger = -1e9f; static string lastStingerKey;
        static Action oneShotDone; static string oneShotKind; static float oneShotDeadline;

        // ---- 引擎与播放
        MusicEngine engine;
        readonly object engineLock = new object();
        Thread renderThread;
        volatile bool running;
        AudioClip stream;
        int outRate;
        const int RING = 32768;                         // 环形缓冲（帧）
        readonly float[] ring = new float[RING * 2];
        long wpos, rpos;                                // 已写 / 已读帧数（单生产者单消费者）
        int target = 8192;                              // 渲染线程保持的提前量（帧）
        readonly Queue<Action> mainQ = new Queue<Action>();

        public static void Init()
        {
            if (inst != null) return;
            var go = new GameObject("Audio");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Sfx>();
            inst.sfx = go.AddComponent<AudioSource>();
            inst.music = go.AddComponent<AudioSource>();
            inst.music.playOnAwake = false;
            inst.music.loop = true; inst.music.volume = 1;
            inst.music.spatialBlend = 0;
            // 预先在后台渲染标题曲需要的乐器采样
            try { var k = MusicLib.Resolve(MusicKind ?? "title", Culture); if (k != null) MusicLib.PrepareAsync(k, null, null, OutRate()); }
            catch (Exception e) { Debug.LogWarning("音乐预热失败：" + e.Message); }
            if (MusicOn && MusicKind != null) StartMusicIfReady();
        }

        public static void Click() { Play("click", 0.5f); }
        public static void Play(string name, float vol = 0.8f)
        {
            if (inst == null || !SoundOn) return;
            // 胜负乐曲 / 终曲正在播放时，旧的 win / lose 拨弦音效与之调性冲突，略去
            if ((name == "win" || name == "lose") && playingKey != null && MusicOn && MusicKind != null && (ONE_SHOT.Contains(MusicKind) || MusicKind == "ending") && !MusicFinished) return;
            AudioClip c;
            if (!inst.clips.TryGetValue(name, out c)) { c = inst.Make(name); inst.clips[name] = c; }
            if (c != null) inst.sfx.PlayOneShot(c, vol);
        }

        // ================================================================ 音乐接口 ==
        // 切换背景音乐。culture 可选（同 SetCulture）。胜利 / 战败短曲播放中请求 map / council：等短曲奏完再切换（最多短曲长度 + 1 秒）。
        public static void Music(string kind) { Music(kind, null); }
        public static void Music(string kind, string culture)
        {
            try { MusicImpl(kind, culture); } catch (Exception e) { Debug.LogWarning("音乐：" + e.Message); }
        }

        // 设定当前地域：map / battle 换成该地域的变奏；未知文化回退汉地
        public static void SetCulture(string culture)
        {
            Culture = string.IsNullOrEmpty(culture) ? "han" : culture;
            if (MusicOn) StartMusicIfReady();
        }

        // 叠加在当前曲目上的冲锋乐句：主曲压低，乐句结束后恢复。speed ≥ 2 用一小节的短句；
        // force 忽略 15 秒间隔；variant 指定变奏 key（如 "clash-b"）。返回是否真的播放了。
        public static bool Stinger(string kind, float speed = 1f, bool force = false, string variant = null)
        {
            try { return StingerImpl(string.IsNullOrEmpty(kind) ? "clash" : kind, speed, force, variant); }
            catch (Exception e) { Debug.LogWarning("冲锋乐句：" + e.Message); return false; }
        }

        public static void SetMusic(bool on)
        {
            MusicOn = on;
            if (on) StartMusicIfReady(); else StopMusic(0.8);
        }

        // 音乐音量（0..1.5，默认 1；与音效音量无关）
        public static void SetMusicVolume(float v)
        {
            musicVol = Mathf.Clamp(v, 0, 1.5f);
            if (inst != null && inst.engine != null) lock (inst.engineLock) inst.engine.SetVolume(MUSIC_VOLUME * musicVol);
        }

        // 胜利 / 战败短曲：播完（或被别的曲目取代、音乐关闭、超时）后回调 done；之后请求的 map / council 会等它奏完
        public static void PlayOneShotMusic(string kind, Action done = null)
        {
            var prev = oneShotDone;
            oneShotDone = null;
            if (prev != null) prev();
            Music(kind);
            if (done == null) return;
            if (inst == null || !MusicOn || playingKey == null) { done(); return; }
            oneShotDone = done; oneShotKind = kind;
            float sec = 20;
            var d = MusicLib.Duration(playingKey);
            if (d != null) sec = Mathf.Min(24, (float)d[2] + 2);
            oneShotDeadline = Time.unscaledTime + sec;
        }

        public static string PlayingMusic { get { return playingKey; } }
        // 背景音乐是否真的在响（开着且引擎可用）：战斗结束时据此决定放胜负短曲还是旧的 win / lose 音效（同 battle-controller.js 的 musicLive）
        public static bool MusicLive { get { return inst != null && MusicOn && (inst.engine != null || inst.EnsureEngine() != null) && MusicLib.Resolve("victory", Culture) != null; } }

        // ---------------------------------------------------------------- 实现 --
        static void MusicImpl(string kind, string culture)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (OVERLAY.Contains(kind)) { Stinger(kind); return; }
            if (HOLD_AFTER_ONE_SHOT.Contains(kind) && OneShotBusy())
            {
                heldKind = kind; heldCulture = culture;
                if (holdUntil < 0)
                {
                    float sec = 20;
                    var d = MusicLib.Duration(playingKey);
                    if (d != null) sec = Mathf.Min(24, (float)d[2] + 1);
                    holdUntil = Time.unscaledTime + sec;
                }
                return;
            }
            if (heldKind != null) { heldKind = null; holdUntil = -1; }
            if (culture != null) Culture = culture;
            if (MusicKind == kind && !(ONE_SHOT.Contains(kind) && MusicFinished))
            {
                if (culture != null || playingKey == null) StartMusicIfReady();
                return;
            }
            MusicKind = kind;
            MusicFinished = false;
            if (ONE_SHOT.Contains(kind)) playingKey = null;   // 一次性短曲每次都从头播放
            if (MusicOn) StartMusicIfReady();
        }

        static bool OneShotBusy()
        {
            return MusicKind != null && ONE_SHOT.Contains(MusicKind) && !MusicFinished && playingKey != null && MusicOn && inst != null;
        }
        static void ReleaseHold()
        {
            holdUntil = -1;
            string k = heldKind, c = heldCulture;
            heldKind = null;
            if (k != null) MusicImpl(k, c);
        }

        static void StopMusic(double fade)
        {
            playingKey = null;
            if (inst != null && inst.engine != null) lock (inst.engineLock) inst.engine.Stop(fade);
        }

        static void StartMusicIfReady()
        {
            string kind = MusicKind;
            if (kind == null || !MusicOn || inst == null) return;
            if (MusicFinished && ONE_SHOT.Contains(kind)) return;
            string key = MusicLib.Resolve(kind, Culture);
            if (key == null) { StopMusic(0.8); return; }
            if (key == playingKey) return;
            var e = inst.EnsureEngine();
            if (e == null) return;
            playingKey = key;
            MusicFinished = false;
            int req = ++playReq;
            lock (inst.engineLock)
                e.Play(key, new PlayOpts { fade = 0.8 }, ok => inst.ToMain(() =>
                {
                    // 没能开始（被取代等）且之后没有新的请求：清掉记录，再请求同一曲目时能重新开始
                    if (!ok && req == playReq && playingKey == key) playingKey = null;
                    if (ok && (key.StartsWith("battle") || key.StartsWith("duel"))) PrewarmStingers();
                }));
        }

        // 冲锋乐句跟随主曲目：移调到其主音，速度取其拍速的整数比（112–176），复拍子按附点四分对拍
        static MainInfo FollowPlan(string key, out int transpose, out double bpm, out double grid)
        {
            MainInfo info = null;
            if (inst != null && inst.engine != null) lock (inst.engineLock) info = inst.engine.GetMainInfo();
            // 新的主曲目还在准备中（刚切换曲目）：按将要播放的曲目取调性与速度，不对拍
            if (playingKey != null && (info == null || info.key != playingKey))
            {
                var C = MusicLib.Compile(playingKey);
                if (C != null && C.loop) { var sec = C.LoopSection; info = new MainInfo { key = playingKey, bpm = sec != null ? sec.bpm : 0, meter = C.meter, tonicPc = C.tonicPc, pending = true }; }
            }
            transpose = 0; bpm = 0; grid = 1;
            if (info != null)
            {
                int st = MusicLib.Tonic(key);
                if (st >= 0) { transpose = ((info.tonicPc - st) % 12 + 12) % 12; if (transpose > 5) transpose -= 12; }
                grid = info.meter >= 6 && info.meter % 3 == 0 ? 3 : 1;
                double beat = info.bpm / grid;
                var P = MusicLib.Get(key);
                double tgt = P != null ? P.Num("bpm", 0) : 0; if (tgt == 0) tgt = 150;
                foreach (var k in new[] { 1, 1.5, 2, 0.5, 3 })
                {
                    double b = beat * k;
                    if (b >= 112 && b <= 176 && (bpm == 0 || Math.Abs(b - tgt) < Math.Abs(bpm - tgt))) bpm = b;
                }
            }
            return info;
        }

        // 战场 / 单挑曲开始后，空闲时预渲染跟随它的冲锋乐句变奏（第一次攻击时不必等采样）
        static void PrewarmStingers()
        {
            if (inst == null || inst.engine == null) return;
            string b = MusicLib.Resolve("clash", Culture);
            if (b == null) return;
            foreach (var k in new[] { b, b + "-b", b + "-c", b + "-short" })
            {
                if (MusicLib.Get(k) == null) continue;
                int tr; double bpm, grid;
                FollowPlan(k, out tr, out bpm, out grid);
                MusicLib.PrepareAsync(MusicLib.Variant(k, tr, bpm) ?? k, inst.engine.pins, null, inst.outRate);
            }
        }

        static bool StingerImpl(string kind, float speed, bool force, string variant)
        {
            if (!MusicOn || inst == null) return false;
            string b = MusicLib.Resolve(kind, Culture);
            var e = inst.EnsureEngine();
            if (b == null || e == null) return false;
            float now = Time.unscaledTime;
            if (!force && now - lastStinger < STINGER_GAP) return false;
            string key = b;
            if (speed >= 2 && MusicLib.Get(b + "-short") != null) key = b + "-short";
            else
            {
                var vs = new List<string>();
                foreach (var k in new[] { b, b + "-b", b + "-c" }) if (MusicLib.Get(k) != null && k != lastStingerKey) vs.Add(k);
                if (vs.Count > 0) key = vs[UnityEngine.Random.Range(0, vs.Count)];
            }
            if (variant != null && MusicLib.Get(variant) != null) key = variant;
            int tr; double bpm, grid;
            var info = FollowPlan(key, out tr, out bpm, out grid);
            string vk = MusicLib.Variant(key, tr, bpm) ?? key;
            lastStinger = now; lastStingerKey = key;
            lock (inst.engineLock) e.Play(vk, new PlayOpts { overlay = true, fade = 0.2, duck = 0.3, sync = info != null && !info.pending, grid = grid });
            return true;
        }

        // 引擎在渲染线程回调：非循环曲结束
        void OnEnded(string key, bool overlay)
        {
            if (overlay || key != playingKey) return;
            playingKey = null; MusicFinished = true;
            if (oneShotDone != null) FireOneShot();
            if (heldKind != null) ReleaseHold();
        }
        static void FireOneShot()
        {
            var d = oneShotDone;
            oneShotDone = null; oneShotKind = null;
            if (d != null) { try { d(); } catch (Exception ex) { Debug.LogException(ex); } }
        }

        void ToMain(Action a) { lock (mainQ) mainQ.Enqueue(a); }

        void Update()
        {
            for (;;)
            {
                Action a = null;
                lock (mainQ) if (mainQ.Count > 0) a = mainQ.Dequeue();
                if (a == null) break;
                try { a(); } catch (Exception ex) { Debug.LogException(ex); }
            }
            if (heldKind != null && holdUntil >= 0 && Time.unscaledTime >= holdUntil) ReleaseHold();
            if (oneShotDone != null && (Time.unscaledTime >= oneShotDeadline || !MusicOn || MusicKind != oneShotKind)) FireOneShot();
        }

        // ---------------------------------------------------------- 渲染与播放 --
        MusicEngine EnsureEngine()
        {
            if (engine != null) return engine;
            try
            {
                outRate = OutRate();
                var e = new MusicEngine(outRate, false, MUSIC_VOLUME * musicVol);
                e.onEnded = (k, ov) => ToMain(() => OnEnded(k, ov));
                engine = e;
                stream = AudioClip.Create("SanguoMusic", outRate * 8, 2, outRate, true, OnPcmRead, OnPcmSetPosition);
                music.clip = stream;
                music.loop = true;
                running = true;
                renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "SanguoMusicRender" };
                renderThread.Start();
                music.PlayScheduled(AudioSettings.dspTime + 0.05);
            }
            catch (Exception ex) { Debug.LogWarning("音乐引擎创建失败：" + ex.Message); engine = null; }
            return engine;
        }

        // 合成采样率：与输出一致（免去重采样）；异常值用 44100
        static int OutRate() { int r = AudioSettings.outputSampleRate; return r < 22050 || r > 48000 ? 44100 : r; }

        // 渲染线程：保持环形缓冲里有 target 帧的提前量；引擎空闲 4 秒后（混响尾音已衰减）只写静音
        void RenderLoop()
        {
            const int B = MusicEngine.BLOCK;
            var L = new float[B]; var R = new float[B];
            long idleFrames = 0;
            while (running)
            {
                long fill = Interlocked.Read(ref wpos) - Interlocked.Read(ref rpos);
                if (fill >= target || RING - fill < B) { Thread.Sleep(3); continue; }
                bool silent;
                lock (engineLock)
                {
                    bool busy = engine.Busy;
                    idleFrames = busy ? 0 : idleFrames + B;
                    silent = idleFrames > outRate * 4;
                    if (!silent)
                    {
                        try { engine.Render(L, R, 0, B); }
                        catch (Exception) { Array.Clear(L, 0, B); Array.Clear(R, 0, B); }
                    }
                }
                if (silent) { Array.Clear(L, 0, B); Array.Clear(R, 0, B); }
                long w = Interlocked.Read(ref wpos);
                for (int i = 0; i < B; i++)
                {
                    int p = (int)((w + i) % RING) * 2;
                    ring[p] = L[i]; ring[p + 1] = R[i];
                }
                Interlocked.Exchange(ref wpos, w + B);
            }
        }

        // 流式 AudioClip 的读取回调（音频线程）：从环形缓冲取交错立体声；不够时补静音并加大提前量
        void OnPcmRead(float[] data)
        {
            int frames = data.Length / 2;
            long r = Interlocked.Read(ref rpos), avail = Interlocked.Read(ref wpos) - r;
            int n = (int)Math.Min(frames, avail);
            for (int i = 0; i < n; i++)
            {
                int p = (int)((r + i) % RING) * 2;
                data[2 * i] = ring[p]; data[2 * i + 1] = ring[p + 1];
            }
            for (int i = 2 * n; i < data.Length; i++) data[i] = 0;
            Interlocked.Exchange(ref rpos, r + n);
            if (n < frames && running) target = Math.Min(RING - 2 * MusicEngine.BLOCK, Math.Max(target, frames * 2 + MusicEngine.BLOCK));
        }
        void OnPcmSetPosition(int pos) { }

        void OnDestroy()
        {
            running = false;
            if (renderThread != null) { try { renderThread.Join(200); } catch (Exception) { } renderThread = null; }
            if (music != null) music.Stop();
            if (stream != null) { Destroy(stream); stream = null; }
            foreach (var c in clips.Values) if (c != null) Destroy(c);
            clips.Clear();
            if (engine != null) { lock (engineLock) engine.Dispose(); engine = null; }
            if (inst == this) inst = null;
        }
        void OnApplicationQuit() { running = false; }

        // ---------------------------------------------------------- 音效合成 --
        AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }
        static float Env(float t, float a, float d) { return t < a ? t / a : Mathf.Exp(-(t - a) / d); }

        AudioClip Make(string name)
        {
            var rnd = new System.Random(name.GetHashCode());
            float dur = 0.3f;
            switch (name)
            {
                case "click": dur = 0.06f; break;
                case "hit": dur = 0.35f; break;
                case "fire": dur = 1.0f; break;
                case "rock": dur = 0.8f; break;
                case "horn": dur = 1.6f; break;
                case "win": dur = 1.8f; break;
                case "lose": dur = 1.6f; break;
                case "coin": dur = 0.35f; break;
                case "duel": dur = 0.25f; break;
                case "magic": dur = 0.7f; break;
                case "march": dur = 0.9f; break;
            }
            int n = (int)(dur * Rate);
            var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                float s = 0;
                switch (name)
                {
                    case "click": s = Mathf.Sin(t * 2 * Mathf.PI * 1400) * Env(t, 0.002f, 0.015f) * 0.5f; break;
                    case "hit": lp += (noise - lp) * 0.25f; s = (lp * 1.4f + Mathf.Sin(t * 2 * Mathf.PI * (140 - t * 200)) * 0.6f) * Env(t, 0.003f, 0.08f); break;
                    case "duel": s = (Mathf.Sin(t * 2 * Mathf.PI * 2100) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 3170) * 0.3f + noise * 0.3f) * Env(t, 0.001f, 0.05f); break;
                    case "fire": lp += (noise - lp) * 0.08f; s = lp * 1.6f * Env(t, 0.1f, 0.35f) * (0.7f + 0.3f * Mathf.Sin(t * 40)); break;
                    case "rock": lp += (noise - lp) * 0.04f; s = (lp * 2f + Mathf.Sin(t * 2 * Mathf.PI * 60) * 0.5f) * Env(t, 0.01f, 0.22f); break;
                    case "magic": s = Mathf.Sin(t * 2 * Mathf.PI * (600 + Mathf.Sin(t * 30) * 200)) * Env(t, 0.05f, 0.25f) * 0.35f; break;
                    case "coin": s = (Mathf.Sin(t * 2 * Mathf.PI * 1318) * (t < 0.08f ? 1 : 0) + Mathf.Sin(t * 2 * Mathf.PI * 1760) * (t >= 0.08f ? 1 : 0)) * Env(t, 0.002f, 0.12f) * 0.3f; break;
                    case "horn": { float f = 196; s = (Mathf.Sin(t * 2 * Mathf.PI * f) + 0.5f * Mathf.Sin(t * 4 * Mathf.PI * f) + 0.25f * Mathf.Sin(t * 6 * Mathf.PI * f)) * Mathf.Clamp01(t * 6) * Mathf.Clamp01((dur - t) * 3) * 0.25f; break; }
                    case "march": { float beat = Mathf.Repeat(t, 0.3f); lp += (noise - lp) * 0.1f; s = (Mathf.Sin(beat * 2 * Mathf.PI * 80) * 0.8f + lp) * Mathf.Exp(-beat * 18); break; }
                    case "win":
                    case "lose":
                        {
                            float[] notes = name == "win" ? new[] { 523f, 659f, 784f, 1046f } : new[] { 392f, 349f, 311f, 262f };
                            int k = Mathf.Min(3, (int)(t / 0.28f));
                            float lt = t - k * 0.28f;
                            s = Pluck(notes[k], lt) * 0.5f; break;
                        }
                }
                d[i] = Mathf.Clamp(s, -1, 1);
            }
            return Clip(name, d);
        }

        // 拨弦（古筝风格）
        static float Pluck(float f, float t)
        {
            if (t < 0) return 0;
            float e = Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t * 400);
            return (Mathf.Sin(t * 2 * Mathf.PI * f) + 0.45f * Mathf.Sin(t * 4 * Mathf.PI * f) * Mathf.Exp(-t * 6) + 0.2f * Mathf.Sin(t * 6 * Mathf.PI * f) * Mathf.Exp(-t * 9)) * e * 0.4f;
        }
    }
}
