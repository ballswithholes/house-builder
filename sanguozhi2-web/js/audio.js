'use strict';
/* ==========================================================================
   音效与背景音乐（移植自 Core/Sfx.cs；第二版重做音乐）
   - 音效：按 C# 的公式算出 PCM（22050 Hz 单声道），装入 AudioBuffer 播放。
     合成函数是纯计算（不依赖 AudioContext），可在 Node 下测试：SG.Sfx.render(name) → Float32Array
   - 背景音乐：由 SG.Music（js/music.js、music-inst.js、music-han.js、music-world.js）
     实时合成——全新创作的曲目、合成民乐 / 管弦乐音色、前瞻调度、交叉淡入淡出、地域变奏。
     index.html 应在 audio.js 之后依次用 <script> 载入这四个文件；缺少的文件（一个或全部）
     会按 audio.js 自身的路径自动补载，曲库仍为空时 console.warn 一次。
   - AudioContext 在第一次用户手势时创建 / 恢复（unlock）；之前调用 music(kind) 只记住曲目，解锁后自动开始。
   - 任何情况下（无 WebAudio、被浏览器禁止、音乐模块缺失等）都不会抛出异常。

   接口：init() unlock() click() play(name, vol) setSound(on) soundOn
         music(kind[, culture]) setMusic(on) musicOn musicKind
         setCulture(culture) culture  setMusicVolume(v)  stinger(kind[, { speed, force }])
   曲目 kind：title map battle battle-defend duel victory defeat ending council clash
         （victory / defeat 为一次性短曲，播放中请求 map / council 会等它奏完；
          clash 为叠加在当前曲目上的冲锋乐句，跟随主曲调性与拍子，结束后主曲自动恢复）
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
  // 四个音乐文件，各带一个“已载入”标记。index.html 用 <script> 全部载入时无需补载；
  // 缺哪个（例如只加了 music.js 的标签）就按 audio.js 所在目录依次补载哪个。
  const MUSIC_FILES = [
    ['music.js', () => !!SG.Music],
    ['music-inst.js', () => !!(SG.Music && SG.Music.INST && SG.Music.INST.zheng)],
    ['music-han.js', () => !!(SG.Music && SG.Music.PIECES && SG.Music.PIECES.title)],
    ['music-world.js', () => !!(SG.Music && SG.Music.PIECES && SG.Music.PIECES['map@wa'])],
  ];
  const ONE_SHOT = { victory: 1, defeat: 1 };
  const OVERLAY = { clash: 1 };
  // 一次性短曲（胜利 / 战败）播放中再请求这些“平静”曲目时，等短曲奏完再切换（其余曲目立即切换）
  const HOLD_AFTER_ONE_SHOT = { map: 1, council: 1 };
  const STINGER_GAP = 15;          // 冲锋乐句最短间隔（秒）：长战斗里不会每次攻击都压低主曲
  const scriptBase = (function () {
    try {
      const s = document.currentScript && document.currentScript.src;
      if (s) return s.replace(/audio\.js(\?.*)?$/, '');
    } catch (e) { /* 忽略 */ }
    return null;
  })();
  let musicLoading = false, musicLoadFailed = false, warnedEmpty = false;
  let engine = null;
  let playingKey = null;          // 已交给引擎播放（或准备中）的曲目 key
  let musicVol = 1;
  let held = null;                // 等一次性短曲奏完再切换的请求 { kind, culture }
  let holdTimer = 0;
  let lastStinger = -1e9, lastStingerKey = null;
  let playReq = 0;

  function musicComplete() { return MUSIC_FILES.every(f => f[1]()); }

  // 补载缺少的音乐文件（按顺序、逐个；已由 <script> 载入的跳过）
  function loadMusicScripts() {
    if (musicLoading || musicLoadFailed || typeof document === 'undefined' || musicComplete()) return;
    if (!scriptBase) { warnEmpty('找不到 audio.js 的路径，无法补载'); musicLoadFailed = true; return; }
    musicLoading = true;
    let i = 0;
    const next = () => {
      while (i < MUSIC_FILES.length && MUSIC_FILES[i][1]()) i++;
      if (i >= MUSIC_FILES.length) { musicLoading = false; prewarm(); startMusicIfReady(); return; }
      const el = document.createElement('script');
      el.src = scriptBase + MUSIC_FILES[i++][0];
      el.async = false;
      el.onload = next;
      el.onerror = () => { musicLoading = false; musicLoadFailed = true; warnEmpty('载入失败：' + el.src); };
      (document.head || document.documentElement).appendChild(el);
    };
    next();
  }

  // 没有可播放的曲目时提示一次（而不是悄无声息）
  function warnEmpty(why) {
    if (warnedEmpty) return;
    warnedEmpty = true;
    console.warn('背景音乐不可用（' + why + '）。index.html 应依次载入 music.js、music-inst.js、music-han.js、music-world.js，或都不载入（由 audio.js 自动补载）。');
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
        if (!overlay && key === playingKey) {
          playingKey = null; Sfx.musicFinished = true;
          if (held) releaseHold();
        }
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
      if (!musicComplete()) loadMusicScripts();
      if (!SG.Music || !SG.Music.Engine) return;      // 补载完成后会再调用本函数
      if (Sfx.musicFinished && ONE_SHOT[kind]) return;
      const key = SG.Music.resolve(kind, Sfx.culture);
      if (!key) {
        if (musicLoading) return;
        if (!SG.Music.keys().length) warnEmpty('曲库为空');
        stopMusic(); return;
      }
      if (key === playingKey) return;
      const e = ensureEngine();
      if (!e) return;
      playingKey = key;
      Sfx.musicFinished = false;
      const req = ++playReq;
      e.play(key, { fade: 0.8 }).then(ok => {
        // 没能开始（引擎已停止等）且之后没有新的请求：清掉记录，再请求同一曲目时能重新开始
        if (!ok && req === playReq && playingKey === key) playingKey = null;
        if (ok && /^(battle|duel)/.test(key)) prewarmStingers(e);
      }, err => { if (req === playReq && playingKey === key) playingKey = null; console.warn(err); });
    } catch (e) { console.warn(e); }
  }

  // 一次性短曲是否正在播放（或准备中）
  function oneShotBusy() {
    return !!(ONE_SHOT[Sfx.musicKind] && !Sfx.musicFinished && playingKey && Sfx.musicOn && ctx && ctx.state === 'running');
  }
  function releaseHold() {
    if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; }
    const h = held;
    held = null;
    if (h) musicImpl(h.kind, h.culture);
  }

  function musicImpl(kind, culture) {
    if (OVERLAY[kind]) { Sfx.stinger(kind); return; }
    if (HOLD_AFTER_ONE_SHOT[kind] && oneShotBusy()) {
      // 胜利 / 战败短曲奏完再回到地图曲（上限：短曲长度 + 1 秒）
      held = { kind, culture };
      if (!holdTimer) {
        let sec = 20;
        try { const d = SG.Music.duration(playingKey); if (d) sec = Math.min(24, d.total + 1); } catch (e) { /* 默认 */ }
        holdTimer = setTimeout(() => { holdTimer = 0; releaseHold(); }, sec * 1000);
      }
      return;
    }
    if (held) { held = null; if (holdTimer) { clearTimeout(holdTimer); holdTimer = 0; } }
    if (culture) Sfx.culture = culture;
    if (Sfx.musicKind === kind && !(ONE_SHOT[kind] && Sfx.musicFinished)) {
      if (culture || !playingKey) startMusicIfReady();    // 同一曲目但地域可能变了 / 上次请求被取代
      return;
    }
    Sfx.musicKind = kind;
    Sfx.musicFinished = false;
    if (ONE_SHOT[kind]) playingKey = null; // 一次性短曲每次都从头播放
    if (Sfx.musicOn) startMusicIfReady();
  }

  // 冲锋乐句跟随主曲目：移调到其主音，速度取其拍速的整数比（112–176），复拍子按附点四分对拍
  function followPlan(e, key) {
    let info = e.mainInfo();
    // 新的主曲目还在准备中（刚切换曲目）：按将要播放的曲目取调性与速度，不对拍
    if (playingKey && (!info || info.key !== playingKey)) {
      const C = SG.Music.compile(playingKey);
      if (C && C.loop) { const sec = C.sections[C.form[C.loopFrom]]; info = { key: playingKey, bpm: sec.bpm, meter: C.meter, tonicPc: C.tonicPc, pending: true }; }
    }
    let transpose = 0, bpm = 0, grid = 1;
    if (info) {
      const st = SG.Music.tonic(key);
      if (st !== null && info.tonicPc !== null && info.tonicPc !== undefined) {
        transpose = (((info.tonicPc - st) % 12) + 12) % 12;
        if (transpose > 5) transpose -= 12;
      }
      grid = info.meter >= 6 && info.meter % 3 === 0 ? 3 : 1;
      const beat = info.bpm / grid;
      const target = (SG.Music.get(key) || {}).bpm || 150;
      for (const k of [1, 1.5, 2, 0.5, 3]) {
        const b = beat * k;
        if (b >= 112 && b <= 176 && (!bpm || Math.abs(b - target) < Math.abs(bpm - target))) bpm = b;
      }
    }
    return { info, transpose, bpm, grid };
  }

  // 战场 / 单挑曲开始后，空闲时预渲染跟随它的冲锋乐句变奏（第一次攻击时不必等采样）
  function prewarmStingers(e) {
    try {
      const base = SG.Music.resolve('clash', Sfx.culture);
      if (!base) return;
      for (const k of [base, base + '-b', base + '-c', base + '-short']) {
        if (!SG.Music.get(k)) continue;
        const p = followPlan(e, k);
        SG.Music.prepare(SG.Music.variant(k, { transpose: p.transpose, bpm: p.bpm }) || k, e.pins);
      }
    } catch (err) { /* 忽略 */ }
  }

  // 冲锋乐句：选变奏（快速模式用一小节的短句；否则在 2–3 个变奏里轮换），
  // 移调到当前主曲目的主音、速度取主曲目拍速的整数比，并从主曲目的下一拍进入
  function stingerImpl(kind, opts) {
    opts = opts || {};
    if (!Sfx.musicOn || !ctx || ctx.state !== 'running' || !SG.Music) return false;
    const base = SG.Music.resolve(kind, Sfx.culture);
    const e = ensureEngine();
    if (!base || !e) return false;
    const now = ctx.currentTime;
    if (!opts.force && now - lastStinger < STINGER_GAP) return false;
    let speed = opts.speed;
    if (speed === undefined) { try { speed = (SG.Clash && SG.Clash.speed) || 1; } catch (err) { speed = 1; } }
    let key = base;
    if (speed >= 2 && SG.Music.get(base + '-short')) key = base + '-short';
    else {
      const vs = [base, base + '-b', base + '-c'].filter(k => SG.Music.get(k) && k !== lastStingerKey);
      if (vs.length) key = vs[Math.floor(Math.random() * vs.length)];
    }
    if (opts.variant && SG.Music.get(opts.variant)) key = opts.variant;
    const plan = followPlan(e, key);
    const info = plan.info, grid = plan.grid;
    const vk = SG.Music.variant(key, { transpose: plan.transpose, bpm: plan.bpm }) || key;
    lastStinger = now; lastStingerKey = key;
    e.play(vk, { overlay: true, fade: 0.2, duck: 0.3, sync: !!info && !info.pending, grid }).catch(err => console.warn(err));
    return true;
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
            if (musicComplete()) prewarm(); else loadMusicScripts();
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
        // 胜负乐曲 / 终曲正在播放时，旧的 win / lose 拨弦音效（C 大调 / C 小调）与之调性冲突，略去
        if ((name === 'win' || name === 'lose') && playingKey && Sfx.musicOn && (ONE_SHOT[Sfx.musicKind] || Sfx.musicKind === 'ending') && !Sfx.musicFinished) return;
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
    // 胜利 / 战败短曲播放中请求 map / council：等短曲奏完再切换（最多等短曲长度 + 1 秒）。
    music(kind, culture) {
      try { musicImpl(kind, culture); } catch (e) { /* 无音频 */ }
    },

    // 设定当前地域（见 DESIGN-V2 §6 文化代号）：map / battle 随之换成该地域的变奏
    setCulture(culture) {
      try {
        Sfx.culture = culture || 'han';
        if (Sfx.musicOn) startMusicIfReady();
      } catch (e) { /* 无音频 */ }
    },

    // 叠加在当前曲目上的短乐句（如攻击画面的冲锋号）：主曲压低，乐句结束后恢复。
    // opts：{ speed }（默认取 SG.Clash.speed；≥2 用一小节的短句）、{ force }（忽略 15 秒间隔）、{ variant }（指定变奏 key）。
    // 返回是否真的播放了。
    stinger(kind, opts) {
      try { return stingerImpl(kind || 'clash', opts); } catch (e) { return false; }
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
