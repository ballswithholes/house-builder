'use strict';
/* ==========================================================================
   音效与背景音乐（移植自 Core/Sfx.cs）
   全部为程序合成：先按 C# 的公式算出 PCM（22050 Hz 单声道），
   再装入 WebAudio 的 AudioBuffer 播放。无需任何音频文件。
   - 合成函数是纯计算（不依赖 AudioContext），可在 Node 下测试：
     SG.Sfx.render(name) → Float32Array，SG.Sfx.renderMusic(kind) → Float32Array
   - AudioContext 在第一次用户手势时创建 / 恢复（unlock）。
   - 任何情况下（无 WebAudio、被浏览器禁止等）都不会抛出异常。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const Rate = 22050;
  const TWO_PI = 2 * Math.PI;
  const MUSIC_VOLUME = 0.32;

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

  // 与 pluck 数学上完全相同，但用递推代替逐点的 sin / exp，用于长曲目的快速合成。
  // 把 len 个采样累加进 d[start..]
  function addPluck(d, start, len, f) {
    const w1 = TWO_PI * f / Rate, w2 = 2 * w1, w3 = 3 * w1;
    const c1 = 2 * Math.cos(w1), c2 = 2 * Math.cos(w2), c3 = 2 * Math.cos(w3);
    // sin(n·w) 的二阶递推：s[n+1] = 2cos(w)·s[n] − s[n−1]
    let a0 = 0, a1 = Math.sin(w1), b0 = 0, b1 = Math.sin(w2), g0 = 0, g1 = Math.sin(w3);
    const k0 = Math.exp(-3.2 / Rate), k2 = Math.exp(-6 / Rate), k3 = Math.exp(-9 / Rate);
    let e0 = 1, e2 = 1, e3 = 1;
    for (let i = 0; i < len; i++) {
      const ramp = i * 400 / Rate;
      const e = e0 * (ramp < 1 ? ramp : 1);
      d[start + i] += (a0 + 0.45 * b0 * e2 + 0.2 * g0 * e3) * e * 0.4;
      let n = c1 * a1 - a0; a0 = a1; a1 = n;
      n = c2 * b1 - b0; b0 = b1; b1 = n;
      n = c3 * g1 - g0; g0 = g1; g1 = n;
      e0 *= k0; e2 *= k2; e3 *= k3;
    }
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

  // ---------------------------------------------------------- 音乐合成 --
  // 五声音阶的古筝旋律 + 低音（战斗曲另加战鼓）。返回 Promise<Float32Array>；
  // 分片计算，避免长时间阻塞主线程。sync = true 时同步计算（测试用）。
  function musicPlan(kind) {
    const battle = kind === 'battle';
    const beat = battle ? 0.2 : kind === 'title' ? 0.42 : 0.32;
    const bars = 16, stepsPerBar = 8;
    const dur = bars * stepsPerBar * beat;
    const n = Math.trunc(dur * Rate);
    // 五声音阶：宫商角徵羽
    const root = battle ? 220 : kind === 'title' ? 196 : 261.6;
    const scale = battle ? [0, 3, 5, 7, 10, 12, 15] : [0, 2, 4, 7, 9, 12, 14, 16];
    const rnd = SG.SeededRandom(hashString(kind));
    let idx = 3;
    const notes = [];
    for (let s = 0; s < bars * stepsPerBar; s++) {
      if (rnd.nextDouble() < (battle ? 0.22 : 0.38)) continue;
      idx = M.clamp(idx + rnd.next(-2, 3), 0, scale.length - 1);
      notes.push([s * beat, root * 2 * Math.pow(2, scale[idx] / 12)]);
    }
    const bass = battle ? [0, 0, 3, 5] : [0, 5, 7, 4];
    for (let b = 0; b < bars * 2; b++) notes.push([M.idiv(b * stepsPerBar, 2) * beat, root * 0.5 * Math.pow(2, bass[M.idiv(b, 2) % 4] / 12)]);
    return { battle, beat, bars, stepsPerBar, n, notes, rnd };
  }

  function musicNotes(p, d, from, to) {
    for (let k = from; k < to; k++) {
      const nt = p.notes[k];
      const start = Math.trunc(nt[0] * Rate);
      const len = Math.min(p.n - start, Math.trunc(Rate * 1.6));
      if (len > 0) addPluck(d, start, len, nt[1]);
    }
  }

  function musicDrums(p, d) {
    if (!p.battle) return;
    // 战鼓
    const rnd = p.rnd, n = p.n;
    for (let b = 0; b < p.bars * p.stepsPerBar; b += 2) {
      const start = Math.trunc(b * p.beat * Rate); let lp = 0;
      for (let i = 0; i < Rate * 0.3 && start + i < n; i++) {
        const t = i / Rate; lp += ((rnd.nextDouble() * 2 - 1) - lp) * 0.06;
        d[start + i] += (Math.sin(t * TWO_PI * (90 - t * 120)) * 0.7 + lp) * Math.exp(-t * 14) * (b % 8 === 0 ? 0.9 : 0.45);
      }
    }
  }

  function musicFinish(d) {
    for (let i = 0; i < d.length; i++) d[i] = M.clamp(d[i] * 0.6, -1, 1);
    return d;
  }

  function renderMusicSync(kind) {
    const p = musicPlan(kind);
    const d = new Float32Array(p.n);
    musicNotes(p, d, 0, p.notes.length);
    musicDrums(p, d);
    return musicFinish(d);
  }

  function yieldTask() { return new Promise(r => setTimeout(r, 0)); }

  async function renderMusic(kind) {
    const p = musicPlan(kind);
    const d = new Float32Array(p.n);
    const CHUNK = 24;
    for (let k = 0; k < p.notes.length; k += CHUNK) {
      musicNotes(p, d, k, Math.min(p.notes.length, k + CHUNK));
      await yieldTask();
    }
    musicDrums(p, d);
    return musicFinish(d);
  }

  // ---------------------------------------------------------- 播放 --
  const pcm = new Map();          // name → Float32Array（音效）
  const musicPcm = new Map();     // kind → Promise<Float32Array>
  const buffers = new Map();      // key → AudioBuffer（需要 AudioContext）
  let ctx = null, master = null, sfxBus = null, musicBus = null;
  let musicSrc = null, musicSrcKind = null;
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
      musicBus = ctx.createGain(); musicBus.gain.value = MUSIC_VOLUME; musicBus.connect(master);
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

  function getMusicPcm(kind) {
    let p = musicPcm.get(kind);
    if (!p) {
      p = renderMusic(kind).catch(e => { console.warn('音乐合成失败', e); return null; });
      musicPcm.set(kind, p);
    }
    return p;
  }

  function stopMusicSource() {
    if (!musicSrc) return;
    const src = musicSrc;
    musicSrc = null; musicSrcKind = null;
    try {
      // 短暂淡出，避免爆音
      const g = src._gain;
      if (g && ctx) {
        const now = ctx.currentTime;
        g.gain.cancelScheduledValues(now);
        g.gain.setValueAtTime(g.gain.value, now);
        g.gain.linearRampToValueAtTime(0, now + 0.25);
        src.stop(now + 0.3);
      } else src.stop();
    } catch (e) { /* 已停止 */ }
  }

  function startMusicIfReady() {
    try {
      const kind = Sfx.musicKind;
      if (!kind || !Sfx.musicOn || !ctx || ctx.state !== 'running') return;
      if (musicSrc && musicSrcKind === kind) return;
      getMusicPcm(kind).then(data => {
        if (!data || !ctx || ctx.state !== 'running') return;
        if (Sfx.musicKind !== kind || !Sfx.musicOn) return;
        if (musicSrc && musicSrcKind === kind) return;
        const buf = toBuffer('music_' + kind, data);
        if (!buf) return;
        stopMusicSource();
        const src = ctx.createBufferSource();
        src.buffer = buf; src.loop = true;
        const g = ctx.createGain();
        const now = ctx.currentTime;
        g.gain.setValueAtTime(0, now);
        g.gain.linearRampToValueAtTime(1, now + 0.4);
        src.connect(g); g.connect(musicBus);
        src._gain = g;
        src.start(now);
        musicSrc = src; musicSrcKind = kind;
      }).catch(e => console.warn(e));
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
    unlocked: false,
    Rate,

    init() {
      if (inited) return;
      inited = true;
      try {
        hookGestures();
        // 预先在后台合成标题音乐与常用音效，第一次播放时无需等待
        setTimeout(() => {
          try { getMusicPcm('title'); ['click', 'coin', 'horn', 'hit'].forEach(getPcm); } catch (e) { /* 忽略 */ }
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

    // kinds: 'title' | 'map' | 'battle'
    music(kind) {
      try {
        if (Sfx.musicKind === kind) return;
        Sfx.musicKind = kind;
        getMusicPcm(kind);
        stopMusicSource();
        if (Sfx.musicOn) startMusicIfReady();
      } catch (e) { /* 无音频 */ }
    },

    setMusic(on) {
      try {
        Sfx.musicOn = !!on;
        if (Sfx.musicOn) startMusicIfReady(); else stopMusicSource();
      } catch (e) { /* 无音频 */ }
    },

    setSound(on) { Sfx.soundOn = !!on; },

    // 纯计算接口（测试 / 调试用）
    render(name) { return render(name).slice(); },
    renderMusic(kind) { return renderMusicSync(kind); },
    get context() { return ctx; },
    get playingMusic() { return musicSrcKind; },
  };

  // 供测试比较：逐点版本的拨弦
  Sfx._pluck = pluck;

  SG.Sfx = Sfx;
})();
