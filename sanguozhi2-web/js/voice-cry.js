'use strict';
/* ==========================================================================
   程序化的喊叫 · SG.VoiceCry（AUDIO-V4 §B5；移植自 design-v4/audio/tts-lab/formant-bark.js）
   声门脉冲（Rosenberg）+ 送气噪声 → 4 个级联 Klatt 共振器在元音目标之间滑动（约 25 ms 平滑），辅音用带通噪声与爆破，
   喊叫型音高轮廓（起 → 峰 → 落）、抖动与老年颤音、tanh 饱和。纯计算、确定（装饰性种子，不取 SG.Random），Node 可测。
   像人声而不是词：用力声（单挑的轻 / 重击、蓄力、受击、倒地）、军队的群呼、C 级语言里没有可考的词的地方、语音包尚未载入时的即时替代。

   SG.VoiceCry.render(kind, voice, seed) → Float32Array（SR = 22050 Hz，单声道）
       kind：light heavy charge hurt knockout shout roar cheer hm sigh no grumble laugh breath
       voice：{ sex: 'm'|'f', pitchK: 0.86–1.12, age, rough: 0–1, shout: 0–1 覆盖, martial: bool }
   SG.VoiceCry.crowd(kind, n, seed) → { L, R }（Float32Array 立体声；kind：charge cheer dismay；6–10 声，失谐 ±8 %，错开 0–120 ms）
   SG.VoiceCry.KINDS, SG.VoiceCry.SR, SG.VoiceCry.voiceOf(profile)（SG.Voice.profile 的记录 → voice 参数）
   ========================================================================== */
(function () {
  const SG = (typeof window !== 'undefined' ? window : globalThis).SG = (typeof window !== 'undefined' ? window : globalThis).SG || {};
  const SR = 22050, TAU = 2 * Math.PI;
  // 元音共振峰 F1..F4（Hz，成年男性）；浊辅音（鼻音 / 边音 / 近音）的近似
  const V = {
    a: [730, 1090, 2440, 3400], e: [530, 1840, 2480, 3500], i: [270, 2290, 3010, 3700],
    o: [570, 840, 2410, 3400], u: [300, 870, 2240, 3400], 'ə': [500, 1500, 2500, 3500], 'ɯ': [340, 1250, 2400, 3400],
    m: [250, 1100, 2200, 3300], n: [250, 1700, 2600, 3500], 'ŋ': [250, 2000, 2700, 3500], l: [360, 1300, 2700, 3500], r: [420, 1300, 1600, 3300], w: [300, 610, 2200, 3300], j: [260, 2200, 3000, 3700],
  };
  const BW = [80, 90, 120, 180];
  // 清辅音：噪声带通中心、带宽、是否爆破
  const C = { s: [6000, 3000, 0], 'ʃ': [3000, 2000, 0], h: [1500, 3000, 0], f: [7000, 4000, 0], x: [1800, 1500, 0],
    p: [900, 1200, 1], t: [3500, 2500, 1], k: [1800, 1500, 1], 'ʈʂ': [2600, 1800, 1], ts: [5500, 3000, 1], b: [700, 1000, 1], d: [3000, 2500, 1], g: [1500, 1500, 1], 'ʔ': [800, 600, 1] };
  // §B5 表：音段（元音 / 辅音，秒）与喊叫量；rise / fall = 音高整体上扬 / 下落
  const KINDS = {
    light:    { segs: [['h', 0.04], ['a', 0.14]], shout: 0.45 },
    heavy:    { segs: [['h', 0.05], ['ə', 0.08], ['a', 0.30]], shout: 0.8 },
    charge:   { segs: [['h', 0.30], ['a', 0.20]], shout: 0.55, rise: true },
    hurt:     { segs: [['ʔ', 0.03], ['ə', 0.12]], shout: 0.35, fall: true },
    knockout: { segs: [['a', 0.6], ['ə', 0.4]], shout: 0.6, fall: true, fade: true },
    shout:    { segs: [['h', 0.03], ['a', 0.45]], shout: 1 },
    roar:     { segs: [['o', 0.15], ['a', 0.4]], shout: 1, rough: 0.55 },
    cheer:    { segs: [['h', 0.04], ['e', 0.1], ['i', 0.25]], shout: 0.7 },
    hm:       { segs: [['h', 0.06], ['ə', 0.08], ['m', 0.25]], shout: 0 },
    sigh:     { segs: [['h', 0.22], ['a', 0.16]], shout: 0, fall: true, breathy: 0.6 },
    no:       { segs: [['n', 0.06], ['o', 0.25]], shout: 0.3, fall: true },
    grumble:  { segs: [['h', 0.05], ['ə', 0.1], ['m', 0.3]], shout: 0.05, rough: 0.45, low: 0.85 },
    laugh:    { segs: [['h', 0.05], ['a', 0.1], ['h', 0.05], ['a', 0.1], ['h', 0.05], ['a', 0.14]], shout: 0.3, fall: true },
    breath:   { segs: [['h', 0.3]], shout: 0, breathy: 1 },
  };
  function rng(seed) { let a = seed | 0; return () => { a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
  function reso() { return { a: 0, b: 0, c: 0, y1: 0, y2: 0 }; }
  function setReso(r, f, bw) { const c = -Math.exp(-TAU * bw / SR), b = 2 * Math.exp(-Math.PI * bw / SR) * Math.cos(TAU * f / SR); r.c = c; r.b = b; r.a = 1 - b - c; }
  function runReso(r, x) { const y = r.a * x + r.b * r.y1 + r.c * r.y2; r.y2 = r.y1; r.y1 = y; return y; }

  // segs：[[音, 秒], …]；voice：见文件头
  function bark(segs, voice, seed, opt) {
    voice = voice || {}; opt = opt || {};
    const female = voice.sex === 'f';
    const fs = female ? 1.17 : 1, age = voice.age || 35;
    const pk = voice.pitchK || 1;
    const f0 = (female ? 210 : 120) * pk * (age > 55 ? 0.92 : 1) * (opt.low || 1);
    const shout = voice.shout !== undefined ? voice.shout : (opt.shout || 0);
    const rough = Math.min(1, (voice.rough !== undefined ? voice.rough : (age > 55 ? 0.35 : 0.1)) + (opt.rough || 0) + (voice.martial ? 0.1 : 0));
    const breathy = opt.breathy || 0;
    const R = rng(seed || 1);
    const total = segs.reduce((s, x) => s + x[1], 0), n = Math.ceil((total + 0.12) * SR);
    const out = new Float32Array(n);
    const rs = [reso(), reso(), reso(), reso()], nz = reso();
    let phase = 0, i = 0;
    const cur = V['ə'].map(f => f * fs);
    for (let si = 0; si < segs.length; si++) {
      const p = segs[si][0], len = Math.round(segs[si][1] * SR);
      const tgt = (V[p] || cur).map((f, k) => (V[p] ? f * fs : f) * (k < 2 ? 1 + shout * 0.12 : 1));
      const cons = C[p];
      if (cons) setReso(nz, cons[0], cons[1]);          // once per segment (exp / cos per sample was the hot spot)
      for (let j = 0; j < len && i < n; j++, i++) {
        const u = (i / SR) / total;
        let contour = 1 + shout * 0.35 * Math.sin(Math.PI * Math.min(1, u * 1.4)) - 0.18 * Math.max(0, u - 0.7);
        if (opt.rise) contour *= 0.85 + 0.35 * u;
        if (opt.fall) contour *= 1.08 - 0.3 * u;
        const jit = 1 + (R() - 0.5) * 0.02 * (1 + rough * 3) + 0.012 * Math.sin(TAU * 5.5 * i / SR) * (age > 55 ? 2 : 0.5);
        const f = f0 * (1 + shout * 0.5) * contour * jit;
        for (let k = 0; k < 4; k++) cur[k] += (tgt[k] - cur[k]) * 0.004;
        if ((i & 31) === 0) for (let k = 0; k < 4; k++) setReso(rs[k], cur[k], BW[k] * (1 + shout * 0.5));
        phase += f / SR; if (phase >= 1) phase -= 1;
        const op = 0.6 - shout * 0.15;
        let g = phase < op ? 0.5 * (1 - Math.cos(Math.PI * phase / op)) : Math.max(0, Math.cos(Math.PI * (phase - op) / (2 * (1 - op))));
        g -= 0.5;
        let voiced = cons ? 0 : 1 - breathy * 0.85;
        if (cons && cons[2] && j < len * 0.35) voiced = 0;
        const asp = (R() * 2 - 1) * (0.06 + rough * 0.2 + shout * 0.1 + breathy * 0.5);
        let x = g * voiced + asp * (voiced ? 0.5 : (p === 'h' ? 0.9 : 0));
        for (let k = 0; k < 4; k++) x = runReso(rs[k], x);
        let y = x * 0.06;
        if (cons) {
          const burst = cons[2] && j < SR * 0.01 ? 3 : 1;
          y += runReso(nz, (R() * 2 - 1)) * (p === 'h' ? 0.12 : 0.25) * burst * Math.min(1, j / (SR * 0.004));
        }
        let e = Math.min(1, j / (SR * 0.01), (len - j) / (SR * 0.012) + 0.3);
        if (opt.fade && si === segs.length - 1) e *= 1 - j / len;
        out[i] = y * e;
      }
    }
    let peak = 0; for (let k = 0; k < n; k++) peak = Math.max(peak, Math.abs(out[k]));
    if (peak > 0) for (let k = 0; k < n; k++) out[k] = Math.tanh(out[k] / peak * (1.2 + shout)) * 0.85;
    // 末尾 10 ms 淡出，避免喀哒声
    const tail = Math.round(0.01 * SR);
    for (let k = 0; k < tail && k < n; k++) out[n - 1 - k] *= k / tail;
    return out;
  }
  function render(kind, voice, seed) {
    const K = KINDS[kind] || KINDS.shout;
    return bark(K.segs, voice, seed, K);
  }
  // 群呼：n 个声音（6–10），失谐 ±8 %，错开 0–120 ms，左右分开
  const CROWD = { charge: [['u', 0.15], ['o', 0.5]], cheer: [['h', 0.03], ['e', 0.12], ['i', 0.35]], dismay: [['o', 0.2], ['ə', 0.35]] };
  function crowd(kind, n, seed) {
    n = Math.max(6, Math.min(10, n | 0 || 8));
    const segs = CROWD[kind] || CROWD.charge;
    const R = rng((seed | 0) ^ 0x51ed);
    const parts = [];
    let len = 0;
    for (let k = 0; k < n; k++) {
      const voice = { sex: R() < 0.12 ? 'f' : 'm', pitchK: 0.92 + R() * 0.16, age: 20 + R() * 40, shout: kind === 'dismay' ? 0.3 : 0.85 + R() * 0.15 };
      const d = bark(segs.map(s => [s[0], s[1] * (0.85 + R() * 0.3)]), voice, (seed | 0) + k * 7919, { fall: kind === 'dismay' });
      const off = Math.round(R() * 0.12 * SR), pan = (k / Math.max(1, n - 1)) * 1.2 - 0.6;
      parts.push({ d, off, pan });
      len = Math.max(len, off + d.length);
    }
    const L = new Float32Array(len), Rr = new Float32Array(len);
    const g = 1 / Math.sqrt(n);
    for (const p of parts) {
      const gl = Math.cos((p.pan + 1) * Math.PI / 4) * g, gr = Math.sin((p.pan + 1) * Math.PI / 4) * g;
      for (let i = 0; i < p.d.length; i++) { L[p.off + i] += p.d[i] * gl; Rr[p.off + i] += p.d[i] * gr; }
    }
    return { L, R: Rr };
  }
  // SG.Voice.profile 的记录 → bark 的 voice 参数
  function voiceOf(p) {
    p = p || {};
    return { sex: p.sex || 'm', pitchK: p.pitchK || 1, age: p.age || 35, martial: p.temper === 'martial', rough: p.band === 'old' || p.band === 'elder' ? 0.35 : undefined };
  }
  SG.VoiceCry = { SR, KINDS, render, crowd, bark, voiceOf };
})();
