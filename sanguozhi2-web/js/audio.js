'use strict';
/* ==========================================================================
   音效与背景音乐（移植自 Core/Sfx.cs；第二版重做音乐）
   - 音效：按 C# 的公式算出 PCM（22050 Hz 单声道），装入 AudioBuffer 播放。
     合成函数是纯计算（不依赖 AudioContext），可在 Node 下测试：SG.Sfx.render(name) → Float32Array
   - 背景音乐：由 SG.Music（js/music.js、music-inst.js、music-han.js、music-world.js）
     实时合成——全新创作的曲目、合成民乐 / 管弦乐音色、前瞻调度、交叉淡入淡出、地域变奏。
     若页面没有用 <script> 载入这些文件，init() 会按 audio.js 自身的路径自动补载。
   - AudioContext 在第一次用户手势时创建 / 恢复（unlock）；之前调用 music(kind) 只记住曲目，解锁后自动开始。
   - 任何情况下（无 WebAudio、被浏览器禁止、音乐模块缺失等）都不会抛出异常。

   接口：init() unlock() click() play(name, vol) setSound(on) soundOn
         music(kind[, culture]) setMusic(on) musicOn musicKind
         setCulture(culture) culture  setMusicVolume(v)  stinger(kind)
   曲目 kind：title map battle battle-defend duel victory defeat ending council clash
         （victory / defeat 为一次性短曲；clash 为叠加在当前曲目上的冲锋乐句，结束后主曲自动恢复）
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;


  const Rate = 22050;
  const TWO_PI = 2 * Math.PI;
  const MUSIC_VOLUME = 0.42;       // 背景音乐总音量（相对音效）

  // C# string.GetHashCode() 的替代：稳定的字符串哈希（用于 System.Random 种子）
  function hashString(s) {
    let h = 5381;
    for (let i = 0; i < s.length; i++) h = (Math.imul(h, 33) + s.charCodeAt(i)) | 0;
    return h;
  }

  // Env(t, a, d)：线性起音 + 指数衰减
  function env(t, a, d) { return t < a ? t / a : Math.exp(-(t - a) / d); }

  // 拨弦（古筝风格）
  function pluck(f, t) {
    if (t < 0) return 0;
    const e = Math.exp(-t * 3.2) * M.clamp01(t * 400);
    return (Math.sin(t * TWO_PI * f)
      + 0.45 * Math.sin(t * 2 * TWO_PI * f) * Math.exp(-t * 6)
      + 0.2 * Math.sin(t * 3 * TWO_PI * f) * Math.exp(-t * 9)) * e * 0.4;
  }

  // ---------------------------------------------------------- 音效合成 --
  const DUR = { click: 0.06, hit: 0.35, fire: 1.0, rock: 0.8, horn: 1.6, win: 1.8, lose: 1.6, coin: 0.35, duel: 0.25, magic: 0.7, march: 0.9 };

  function render(name) {
    const rnd = SG.SeededRandom(hashString(name));
    const dur = DUR[name] !== undefined ? DUR[name] : 0.3;
    const n = Math.trunc(dur * Rate);
    const d = new Float32Array(n);
    let lp = 0;
    const notes = name === 'win' ? [523, 659, 784, 1046] : [392, 349, 311, 262];
    for (let i = 0; i < n; i++) {
      const t = i / Rate;
      const noise = rnd.nextDouble() * 2 - 1;
      let s = 0;
      switch (name) {
        case 'click': s = Math.sin(t * TWO_PI * 1400) * env(t, 0.002, 0.015) * 0.5; break;
        case 'hit': lp += (noise - lp) * 0.25; s = (lp * 1.4 + Math.sin(t * TWO_PI * (140 - t * 200)) * 0.6) * env(t, 0.003, 0.08); break;
        case 'duel': s = (Math.sin(t * TWO_PI * 2100) * 0.5 + Math.sin(t * TWO_PI * 3170) * 0.3 + noise * 0.3) * env(t, 0.001, 0.05); break;
        case 'fire': lp += (noise - lp) * 0.08; s = lp * 1.6 * env(t, 0.1, 0.35) * (0.7 + 0.3 * Math.sin(t * 40)); break;
        case 'rock': lp += (noise - lp) * 0.04; s = (lp * 2 + Math.sin(t * TWO_PI * 60) * 0.5) * env(t, 0.01, 0.22); break;
        case 'magic': s = Math.sin(t * TWO_PI * (600 + Math.sin(t * 30) * 200)) * env(t, 0.05, 0.25) * 0.35; break;
        case 'coin': s = (Math.sin(t * TWO_PI * 1318) * (t < 0.08 ? 1 : 0) + Math.sin(t * TWO_PI * 1760) * (t >= 0.08 ? 1 : 0)) * env(t, 0.002, 0.12) * 0.3; break;
        case 'horn': { const f = 196; s = (Math.sin(t * TWO_PI * f) + 0.5 * Math.sin(t * 2 * TWO_PI * f) + 0.25 * Math.sin(t * 3 * TWO_PI * f)) * M.clamp01(t * 6) * M.clamp01((dur - t) * 3) * 0.25; break; }
        case 'march': { const beat = M.repeat(t, 0.3); lp += (noise - lp) * 0.1; s = (Math.sin(beat * TWO_PI * 80) * 0.8 + lp) * Math.exp(-beat * 18); break; }
        case 'win':
        case 'lose': {
          const k = Math.min(3, Math.trunc(t / 0.28));
          const lt = t - k * 0.28;
          s = pluck(notes[k], lt) * 0.5; break;
        }
      }
      d[i] = M.clamp(s, -1, 1);
    }
    return d;
  }

  // ---------------------------------------------------------- 播放 --
  const pcm = new Map();          // name → Float32Array（音效）
  const buffers = new Map();      // key → AudioBuffer（需要 AudioContext）
  let ctx = null, master = null, sfxBus = null;
  let inited = false, gestureHooked = false;

  function AC() {
    if (typeof window === 'undefined') return null;
    return window.AudioContext || window.webkitAudioContext || null;
  }

  function ensureContext() {
    if (ctx) return ctx;
    const Ctor = AC();
    if (!Ctor) return null;
    try {
      ctx = new Ctor({ latencyHint: 'interactive' });
    } catch (e) {
      try { ctx = new Ctor(); } catch (e2) { ctx = null; return null; }
    }
    try {
      master = ctx.createGain(); master.gain.value = 1; master.connect(ctx.destination);
      sfxBus = ctx.createGain(); sfxBus.gain.value = 1; sfxBus.connect(master);
      ctx.onstatechange = () => { if (ctx && ctx.state === 'running') startMusicIfReady(); };
    } catch (e) { ctx = null; return null; }
    return ctx;
  }

  function toBuffer(key, data) {
    let b = buffers.get(key);
    if (b) return b;
    if (!ctx) return null;
    try {
      b = ctx.createBuffer(1, data.length, Rate);
      if (b.copyToChannel) b.copyToChannel(data, 0); else b.getChannelData(0).set(data);
      buffers.set(key, b);
      return b;
    } catch (e) { return null; }
  }

  function getPcm(name) {
    let d = pcm.get(name);
    if (!d) { d = render(name); pcm.set(name, d); }
    return d;
  }

  // ---------------------------------------------------------- 背景音乐 --
  const MUSIC_FILES = ['music.js', 'music-inst.js', 'music-han.js', 'music-world.js'];
  const ONE_SHOT = { victory: 1, defeat: 1 };
  const OVERLAY = { clash: 1 };
  const scriptBase = (function () {
    try {
      const s = document.currentScript && document.currentScript.src;
      if (s) return s.replace(/audio\.js(\?.*)?$/, '');
    } catch (e) { /* 忽略 */ }
    return null;
  })();
  let musicLoading = false;
  let engine = null;
  let playingKey = null;          // 已交给引擎播放（或准备中）的曲目 key
  let musicVol = 1;

  // 页面未用 <script> 载入音乐模块时，按 audio.js 的位置依次补载
  function loadMusicScripts() {
    if (SG.Music || musicLoading || !scriptBase || typeof document === 'undefined') return;
    musicLoading = true;
    let i = 0;
    const next = () => {
      if (i >= MUSIC_FILES.length) { musicLoading = false; prewarm(); startMusicIfReady(); return; }
      const el = document.createElement('script');
      el.src = scriptBase + MUSIC_FILES[i++];
      el.async = false;
      el.onload = next;
      el.onerror = () => { console.warn('音乐模块载入失败：' + el.src); musicLoading = false; };
      (document.head || document.documentElement).appendChild(el);
    };
    next();
  }

  // 空闲时预渲染标题曲与地图曲需要的采样（分片，不卡顿）
  function prewarm() {
    try {
      if (!SG.Music) return;
      const k = SG.Music.resolve(Sfx.musicKind || 'title', Sfx.culture);
      if (k) SG.Music.prepare(k);
    } catch (e) { /* 忽略 */ }
  }

  function ensureEngine() {
    if (engine) return engine;
    if (!ctx || !master || !SG.Music || !SG.Music.Engine) return null;
    try {
      engine = new SG.Music.Engine(ctx, master, { volume: MUSIC_VOLUME * musicVol });
      engine.onEnded = (key, overlay) => {
        if (!overlay && key === playingKey) { playingKey = null; Sfx.musicFinished = true; }
      };
    } catch (e) { console.warn('音乐引擎创建失败', e); engine = null; }
    return engine;
  }

  function stopMusic(fade) {
    playingKey = null;
    try { if (engine) engine.stop(fade === undefined ? 0.8 : fade); } catch (e) { /* 忽略 */ }
  }

  function startMusicIfReady() {
    try {
      const kind = Sfx.musicKind;
      if (!kind || !Sfx.musicOn || !ctx || ctx.state !== 'running') return;
      if (!SG.Music) { loadMusicScripts(); return; }
      if (Sfx.musicFinished && ONE_SHOT[kind]) return;
      const key = SG.Music.resolve(kind, Sfx.culture);
      if (!key) { stopMusic(); return; }
      if (key === playingKey) return;
      const e = ensureEngine();
      if (!e) return;
      playingKey = key;
      Sfx.musicFinished = false;
      e.play(key, { fade: 0.8 }).catch(err => console.warn(err));
    } catch (e) { console.warn(e); }
  }

  function hookGestures() {
    if (gestureHooked || typeof window === 'undefined' || !window.addEventListener) return;
    gestureHooked = true;
    const h = () => Sfx.unlock();
    // 保持监听：iOS 从后台返回后可能处于 interrupted 状态，需要下一次手势再恢复
    ['pointerdown', 'touchend', 'mousedown', 'keydown', 'click'].forEach(ev => window.addEventListener(ev, h, { capture: true, passive: true }));
    if (typeof document !== 'undefined' && document.addEventListener) {
      document.addEventListener('visibilitychange', () => {
        try {
          if (!ctx) return;
          if (document.hidden) { if (ctx.state === 'running') ctx.suspend(); }
          else if (Sfx.unlocked && ctx.state !== 'running' && ctx.state !== 'closed') ctx.resume().catch(() => {});
        } catch (e) { /* 忽略 */ }
      });
    }
  }

  const Sfx = {
    musicOn: true,
    soundOn: true,
    musicKind: null,
    musicFinished: false,
    culture: 'han',
    unlocked: false,
    Rate,

    init() {
      if (inited) return;
      inited = true;
      try {
        hookGestures();
        // 预先在后台合成常用音效与标题曲采样，第一次播放时无需等待
        setTimeout(() => {
          try {
            ['click', 'coin', 'horn', 'hit'].forEach(getPcm);
            if (SG.Music) prewarm(); else loadMusicScripts();
          } catch (e) { /* 忽略 */ }
        }, 0);
      } catch (e) { console.warn(e); }
    },

    // 第一次用户手势时调用：创建 / 恢复 AudioContext
    unlock() {
      try {
        if (!ensureContext()) return;
        if (ctx.state === 'running') { if (!Sfx.unlocked) { Sfx.unlocked = true; startMusicIfReady(); } return; }
        if (ctx.state === 'closed') return;
        // iOS：在手势内播放一段无声缓冲以解锁输出
        try {
          const b = ctx.createBuffer(1, 1, Rate);
          const s = ctx.createBufferSource(); s.buffer = b; s.connect(ctx.destination); s.start(0);
        } catch (e) { /* 忽略 */ }
        const p = ctx.resume();
        Sfx.unlocked = true;
        if (p && p.then) p.then(() => startMusicIfReady()).catch(() => {});
        else startMusicIfReady();
      } catch (e) { /* 无音频 */ }
    },

    click() { Sfx.play('click', 0.5); },

    play(name, vol) {
      if (vol === undefined || vol === null) vol = 0.8;
      try {
        if (!Sfx.soundOn) return;
        const data = getPcm(name);
        if (!ctx || ctx.state !== 'running') return;
        const buf = toBuffer(name, data);
        if (!buf) return;
        const src = ctx.createBufferSource();
        src.buffer = buf;
        const g = ctx.createGain(); g.gain.value = vol;
        src.connect(g); g.connect(sfxBus);
        src.onended = () => { try { src.disconnect(); g.disconnect(); } catch (e) { /* 忽略 */ } };
        src.start();
      } catch (e) { /* 无音频 */ }
    },

    // 切换背景音乐。kind 见文件头；culture 可选（同 setCulture）。
    // 音频未解锁时只记住 kind，解锁后自动开始。
    music(kind, culture) {
      try {
        if (OVERLAY[kind]) { Sfx.stinger(kind); return; }
        if (culture) Sfx.culture = culture;
        if (Sfx.musicKind === kind && !(ONE_SHOT[kind] && Sfx.musicFinished)) {
          if (culture) startMusicIfReady();    // 同一曲目但地域可能变了
          return;
        }
        Sfx.musicKind = kind;
        Sfx.musicFinished = false;
        if (ONE_SHOT[kind]) playingKey = null; // 一次性短曲每次都从头播放
        if (Sfx.musicOn) startMusicIfReady();
      } catch (e) { /* 无音频 */ }
    },

    // 设定当前地域（见 DESIGN-V2 §6 文化代号）：map / battle 随之换成该地域的变奏
    setCulture(culture) {
      try {
        Sfx.culture = culture || 'han';
        if (Sfx.musicOn) startMusicIfReady();
      } catch (e) { /* 无音频 */ }
    },

    // 叠加在当前曲目上的短乐句（如攻击画面的冲锋号）：主曲压低，乐句结束后恢复
    stinger(kind) {
      try {
        if (!Sfx.musicOn || !ctx || ctx.state !== 'running' || !SG.Music) return;
        const key = SG.Music.resolve(kind, Sfx.culture);
        const e = ensureEngine();
        if (!key || !e) return;
        e.play(key, { overlay: true, fade: 0.2 }).catch(err => console.warn(err));
      } catch (e) { /* 无音频 */ }
    },

    setMusic(on) {
      try {
        Sfx.musicOn = !!on;
        if (Sfx.musicOn) startMusicIfReady(); else stopMusic(0.8);
      } catch (e) { /* 无音频 */ }
    },

    // 音乐音量（0..1，默认 1；与音效音量无关）
    setMusicVolume(v) {
      try {
        musicVol = Math.max(0, Math.min(1.5, +v || 0));
        if (engine) engine.setVolume(MUSIC_VOLUME * musicVol);
      } catch (e) { /* 忽略 */ }
    },

    setSound(on) { Sfx.soundOn = !!on; },

    // 纯计算接口（测试 / 调试用）
    render(name) { return render(name).slice(); },
    // 离线渲染一段音乐（测试用）→ Promise<{ buffer, peak, rms, ... }>
    renderMusic(kind, seconds) {
      if (!SG.Music) return Promise.resolve(null);
      return SG.Music.renderOffline(SG.Music.resolve(kind, Sfx.culture) || kind, seconds || 20);
    },
    get context() { return ctx; },
    get playingMusic() { return playingKey; },
    get musicEngine() { return engine; },
  };

  // 供测试比较：逐点版本的拨弦
  Sfx._pluck = pluck;

  SG.Sfx = Sfx;
})();
