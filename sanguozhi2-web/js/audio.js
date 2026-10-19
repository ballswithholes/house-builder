'use strict';
/* ==========================================================================
   音效与背景音乐（移植自 Core/Sfx.cs；第二版重做音乐；第四版：君主乐章、人声总线、设置、新音效）
   - 音效：按公式算出 PCM（22050 Hz 单声道），装入 AudioBuffer 播放。合成函数是纯计算（不依赖 AudioContext），
     可在 Node 下测试：SG.Sfx.render(name) → Float32Array。第三版的 11 个音效逐位不变；第四版新增：
       COMBAT-V4 §13.3   crit finisher breakParry guardCrush ovhGlint lowHit comboTick
       DOMESTIC-FX §8.6  cmd-press-<id>（17 个指令按下声）cmd-deny fx-tick fx-land fx-seal fx-good fx-great fx-miss fx-build fx-wind
       UI-V4 §14.1       ui-hover … ui-month（22 个界面音效；受 Sfx.uiOn 控制，同名 60 ms 内合并，悬停 80 ms 一次）
     别的模块可 SG.Sfx.defineSfx(name, { dur, level, build(d, R, Rate) }) 登记自己的配方（item- / duel- / crowd- 前缀同时写进 SG.Reg.sfx）。
   - 背景音乐：SG.Music（music.js、music-inst.js、music-han.js、music-world.js）实时合成；第四版的君主乐章
     （music-suites.js、music-suites-gen.js，AUDIO-V4 甲部）与指令短乐（music-stings.js，DOMESTIC-FX §8）也由本文件补载。
     「音乐风格」为 君主乐章 时，music(kind) 先按君主乐章解析（SG.MusicSuites.resolve），否则用第三版的地域变奏。
   - 人声（AUDIO-V4 乙部）：voice-data.js、voice-cry.js、voice.js 同样补载；本文件提供人声总线（高通 90 Hz → 压缩 −20 dB 3:1 →
     0.9 × 语音音量）、1.2 秒大殿混响送出、语音时把音乐压到 0.5（起 60 ms、落 400 ms；号令 / 短乐正在压低时取较低的一个，不叠加）。
   - AudioContext 在第一次用户手势时创建 / 恢复（unlock）；之前调用 music(kind) 只记住曲目，解锁后自动开始。
   - 任何情况下（无 WebAudio、被浏览器禁止、模块缺失等）都不会抛出异常。

   接口：init() unlock() click() play(name, vol, { pitch, pan }) setSound(on) soundOn uiOn
         music(kind[, culture | { suite, culture }]) setMusic(on) musicOn musicKind
         setCulture(culture) culture  setMusicVolume(v)  stinger(kind[, { speed, force, suite, crit }])
         第四版：setSuite(ruler | null) suite fanfare(ruler, { duck }) → 秒 | 0   cmdSting(id, tier, { family, answerFamily, fast, headMotif })
                 previewRuler(ruler)（选君主画面：号令 → 停留 1.2 秒后试听地图曲与名句）  stopPreview()
                 voiceBus() → { ctx, input, hall }  duckForVoice(sec)  setVoiceVolume(v)  setMasterVolume(v)  setSfxVolume(v)
                 settings（AUDIO-V4 §B10.4 各行的当前值）  defineSfx(name, recipe)  sfxNames()
   曲目 kind：title map battle battle-defend duel victory defeat ending council clash fanfare
         （victory / defeat 为一次性短曲，播放中请求 map / council 会等它奏完；clash 为叠加在当前曲目上的冲锋乐句）
   ========================================================================== */
(function () {
  const SG = (typeof window !== 'undefined' ? window : globalThis).SG = (typeof window !== 'undefined' ? window : globalThis).SG || {};
  const M = SG.M || { clamp: (x, a, b) => Math.max(a, Math.min(b, x)), clamp01: x => Math.max(0, Math.min(1, x)), repeat: (t, l) => t - Math.floor(t / l) * l };

  const Rate = 22050;
  const TWO_PI = 2 * Math.PI;
  const MUSIC_VOLUME = 0.42;       // 背景音乐总音量（相对音效）
  const SETTINGS_KEY = 'sanguozhi2_audio_v4';

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

  // ---------------------------------------------------------- 第三版音效（逐位不变） --
  const DUR = { click: 0.06, hit: 0.35, fire: 1.0, rock: 0.8, horn: 1.6, win: 1.8, lose: 1.6, coin: 0.35, duel: 0.25, magic: 0.7, march: 0.9 };
  function renderV3(name) {
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

  // ---------------------------------------------------------- 第四版音效配方 --
  // 小工具：都在 d（Float32Array，Rate）上叠加；R = 装饰性随机（SG.SeededRandom，按名称取种子，不碰 SG.Random）
  function sine(d, t0, dur, f, amp, a, tau, f2) {
    const i0 = Math.round(t0 * Rate), n = Math.min(d.length - i0, Math.round(dur * Rate));
    let ph = 0;
    for (let i = 0; i < n; i++) {
      const t = i / Rate, u = n > 1 ? i / (n - 1) : 0;
      const ff = f2 ? f * Math.pow(f2 / f, u) : f;
      ph += TWO_PI * ff / Rate;
      d[i0 + i] += Math.sin(ph) * amp * env(t, a || 0.001, tau || 0.1);
    }
  }
  // 二阶带通（RBJ），fc 可从 fc 扫到 fc2
  function bpNoise(d, t0, dur, fc, q, amp, a, tau, R, fc2, kind) {
    const i0 = Math.round(t0 * Rate), n = Math.min(d.length - i0, Math.round(dur * Rate));
    let x1 = 0, x2 = 0, y1 = 0, y2 = 0, b0 = 0, b1 = 0, b2 = 0, a1 = 0, a2 = 0;
    const set = f => {
      const w = TWO_PI * Math.min(f, Rate * 0.45) / Rate, al = Math.sin(w) / (2 * q), cs = Math.cos(w), a0 = 1 + al;
      if (kind === 'hp') { b0 = (1 + cs) / 2 / a0; b1 = -(1 + cs) / a0; b2 = b0; }
      else if (kind === 'lp') { b0 = (1 - cs) / 2 / a0; b1 = (1 - cs) / a0; b2 = b0; }
      else { b0 = al / a0; b1 = 0; b2 = -al / a0; }
      a1 = -2 * cs / a0; a2 = (1 - al) / a0;
    };
    set(fc);
    for (let i = 0; i < n; i++) {
      if (fc2 && (i & 15) === 0) set(fc * Math.pow(fc2 / fc, i / Math.max(1, n - 1)));
      const x = R.nextDouble() * 2 - 1;
      const y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
      x2 = x1; x1 = x; y2 = y1; y1 = y;
      const t = i / Rate;
      d[i0 + i] += y * amp * (typeof tau === 'function' ? tau(t, i / n) : env(t, a || 0.001, tau || 0.05));
    }
  }
  function fm(d, t0, dur, fc, ratio, i0x, amp, tau, i1) {
    const i0 = Math.round(t0 * Rate), n = Math.min(d.length - i0, Math.round(dur * Rate));
    for (let i = 0; i < n; i++) {
      const t = i / Rate, k = (i0x + ((i1 || 0) - i0x) * Math.min(1, t / dur));
      d[i0 + i] += Math.sin(TWO_PI * fc * t + k * Math.sin(TWO_PI * fc * ratio * t)) * amp * env(t, 0.001, tau);
    }
  }
  function partials(d, t0, dur, list, amp, swell) {
    for (const p of list) {
      const i0 = Math.round(t0 * Rate), n = Math.min(d.length - i0, Math.round(dur * Rate));
      for (let i = 0; i < n; i++) {
        const t = i / Rate;
        const e = swell ? Math.min(1, t / swell) * Math.exp(-Math.max(0, t - swell) / p[2]) : env(t, 0.001, p[2]);
        d[i0 + i] += Math.sin(TWO_PI * p[0] * t + (p[3] || 0)) * p[1] * amp * e;
      }
    }
  }
  function pluckAt(d, t0, f, amp) { const i0 = Math.round(t0 * Rate); for (let i = 0; i + i0 < d.length; i++) d[i0 + i] += pluck(f, i / Rate) * amp; }
  const GONG = [[180, 1, 1.1], [296, 0.7, 0.9], [412, 0.55, 0.7], [580, 0.4, 0.5], [873, 0.25, 0.35]];
  const RECIPES = {
    // ---- COMBAT-V4 §13.3 ----
    crit: { dur: 0.62, level: -6, build(d, R) { sine(d, 0, 0.3, 72, 1, 0.002, 0.09, 48); bpNoise(d, 0, 0.1, 500, 0.8, 0.6, 0.001, 0.03, R, 0, 'lp'); partials(d, 0.01, 0.6, [[2400, 0.5, 0.4], [3720, 0.25, 0.28], [5310, 0.12, 0.18]], 1); } },
    finisher: { dur: 1.25, level: -5, build(d, R) { sine(d, 0, 0.6, 62, 1, 0.002, 0.18, 42); bpNoise(d, 0, 0.15, 400, 0.7, 0.5, 0.001, 0.05, R, 0, 'lp'); partials(d, 0.02, 1.2, GONG, 0.5, 0.45); } },
    breakParry: { dur: 0.55, level: -6, build(d, R) { bpNoise(d, 0, 0.22, 600, 1.2, 0.8, 0, (t, u) => u * u, R, 3000); partials(d, 0.22, 0.32, [[1820, 0.6, 0.15], [2930, 0.4, 0.12], [4110, 0.25, 0.08]], 1); } },
    guardCrush: { dur: 0.36, level: -6, build(d, R) { bpNoise(d, 0, 0.04, 2500, 0.7, 1, 0.0005, 0.012, R, 0, 'hp'); sine(d, 0.01, 0.3, 92, 0.9, 0.002, 0.1, 58); bpNoise(d, 0.03, 0.06, 1800, 1, 0.4, 0.001, 0.01, R, 0, 'hp'); } },
    ovhGlint: { dur: 0.26, level: -14, build(d) { sine(d, 0, 0.25, 5200, 0.6, 0.004, 0.09, 5600); sine(d, 0, 0.2, 7800, 0.3, 0.004, 0.06); } },
    lowHit: { dur: 0.32, level: -8, build(d, R) { sine(d, 0, 0.25, 82, 1, 0.002, 0.07, 60); bpNoise(d, 0, 0.3, 700, 0.6, 0.45, 0.01, 0.1, R, 0, 'lp'); } },
    comboTick: { dur: 0.08, level: -24, build(d, R) { bpNoise(d, 0, 0.03, 1800, 8, 1, 0.0005, 0.01, R); sine(d, 0, 0.06, 1200, 0.5, 0.0005, 0.015); } },
    // ---- DOMESTIC-FX §8.6 ----
    'cmd-press-develop': { dur: 0.12, level: -14, build(d, R) { bpNoise(d, 0, 0.04, 900, 4, 1, 0.0005, 0.012, R); sine(d, 0, 0.1, 120, 0.7, 0.002, 0.03); } },
    'cmd-press-industry': { dur: 0.12, level: -16, build(d, R) { partials(d, 0, 0.08, [[2100, 0.6, 0.04], [3900, 0.4, 0.03], [5300, 0.3, 0.02]], 1); for (let k = 0; k < 4; k++) bpNoise(d, 0.02 + R.nextDouble() * 0.08, 0.004, 6000, 1, 0.5, 0.0002, 0.002, R, 0, 'hp'); } },
    'cmd-press-town': { dur: 0.16, level: -15, build(d, R) { bpNoise(d, 0, 0.03, 1100, 6, 1, 0.0005, 0.01, R); bpNoise(d, 0.05, 0.03, 1150, 6, 0.9, 0.0005, 0.01, R); bpNoise(d, 0.1, 0.03, 1600, 10, 0.7, 0.0005, 0.012, R); } },
    'cmd-press-recruit': { dur: 0.2, level: -13, build(d, R) { sine(d, 0, 0.18, 78, 1, 0.002, 0.05, 62); bpNoise(d, 0, 0.12, 320, 2, 0.35, 0.002, 0.04, R); } },
    'cmd-press-train': { dur: 0.14, level: -15, build(d, R) { partials(d, 0, 0.08, [[3100, 0.6, 0.03], [4700, 0.4, 0.02]], 1); sine(d, 0.04, 0.09, 62, 0.8, 0.002, 0.03); } },
    'cmd-press-search': { dur: 0.32, level: -18, build(d, R) { bpNoise(d, 0, 0.07, 400, 3, 0.7, 0.01, 0.03, R, 900); partials(d, 0.06, 0.26, [[2637, 0.4, 0.2], [3951, 0.3, 0.16], [5274, 0.2, 0.12]], 1); } },
    'cmd-press-hire': { dur: 0.32, level: -18, build(d) { sine(d, 0, 0.3, 880, 0.7, 0.002, 0.12); sine(d, 0, 0.25, 1760, 0.25, 0.002, 0.07); } },
    'cmd-press-move': { dur: 0.2, level: -15, build(d, R) { for (const t of [0, 0.09]) { bpNoise(d, t, 0.03, 420, 2, 0.8, 0.0005, 0.012, R); sine(d, t, 0.05, 125, 0.6, 0.001, 0.02); } } },
    'cmd-press-transport': { dur: 0.14, level: -18, build(d, R) { bpNoise(d, 0, 0.12, 500, 6, 1, 0.01, (t, u) => (0.6 + 0.4 * Math.sin(TWO_PI * 30 * t)) * Math.sin(Math.PI * u), R, 800); } },
    'cmd-press-diplomacy': { dur: 0.36, level: -18, build(d) { partials(d, 0, 0.36, [[1320, 0.6, 0.3], [2950, 0.35, 0.18], [4100, 0.2, 0.1]], 1); } },
    'cmd-press-attack': { dur: 0.36, level: -14, build(d) { partials(d, 0, 0.06, [[1400, 0.6, 0.03], [2380, 0.4, 0.02], [3360, 0.2, 0.015]], 1); for (const h of [1, 2, 3, 4]) sine(d, 0.04, 0.3, 110 * h, 0.4 / h, 0.2, 0.08); } },
    'cmd-press-reward': { dur: 0.12, level: -16, build(d) { fm(d, 0, 0.05, 3200, 1.41, 2, 0.6, 0.02); fm(d, 0.06, 0.05, 3900, 1.41, 2, 0.5, 0.02); } },
    'cmd-press-trade': { dur: 0.14, level: -16, build(d, R) { bpNoise(d, 0, 0.015, 2200, 12, 1, 0.0003, 0.006, R); sine(d, 0, 0.03, 1900, 0.5, 0.0005, 0.01); fm(d, 0.07, 0.05, 3500, 1.41, 2, 0.4, 0.02); } },
    'cmd-press-appoint': { dur: 0.12, level: -16, build(d, R) { sine(d, 0, 0.09, 60, 1, 0.002, 0.03); bpNoise(d, 0, 0.03, 2000, 0.7, 0.4, 0.001, 0.01, R, 0, 'hp'); } },
    'cmd-press-market': { dur: 0.36, level: -18, build(d) { pluckAt(d, 0, 440, 1.4); partials(d, 0.05, 0.3, [[2800, 0.3, 0.2], [4200, 0.15, 0.12]], 1); } },
    'cmd-press-title': { dur: 0.36, level: -18, build(d, R) { for (const f of [523, 784, 1046]) sine(d, 0, 0.3, f, 0.25, 0.08, 0.1); bpNoise(d, 0, 0.2, 1500, 1, 0.12, 0.05, 0.08, R); partials(d, 0.1, 0.25, [[2349, 0.3, 0.15]], 1); } },
    'cmd-press-end': { dur: 0.14, level: -14, build(d, R) { sine(d, 0, 0.1, 110, 1, 0.001, 0.04, 70); bpNoise(d, 0.015, 0.06, 2000, 0.7, 0.5, 0.001, 0.02, R, 0, 'hp'); } },
    'cmd-deny': { dur: 0.14, level: -18, build(d, R) { sine(d, 0, 0.12, 90, 1, 0.002, 0.05); bpNoise(d, 0.02, 0.04, 3000, 0.8, 0.25, 0.001, 0.01, R, 0, 'lp'); } },
    'fx-tick': { dur: 0.02, level: -20, build(d) { sine(d, 0, 0.018, 1800, 1, 0.0005, 0.006); } },
    'fx-land': { dur: 0.08, level: -16, build(d, R) { bpNoise(d, 0, 0.03, 1000, 5, 1, 0.0005, 0.01, R); sine(d, 0, 0.06, 95, 0.8, 0.001, 0.02); } },
    'fx-seal': { dur: 0.2, level: -12, build(d, R) { sine(d, 0, 0.18, 70, 1, 0.002, 0.06, 52); bpNoise(d, 0.01, 0.05, 1500, 0.6, 0.5, 0.001, 0.015, R, 0, 'hp'); } },
    'fx-good': { dur: 0.45, level: -16, build(d) { partials(d, 0, 0.3, [[880, 0.6, 0.18], [1760, 0.15, 0.1]], 1); partials(d, 0.12, 0.33, [[1318.5, 0.6, 0.2], [2637, 0.15, 0.1]], 1); } },
    'fx-great': { dur: 1.25, level: -12, build(d) { partials(d, 0, 1.2, GONG, 0.6, 0.4); partials(d, 0.05, 0.8, [[1567.98, 0.5, 0.4], [3135.96, 0.15, 0.2]], 1); } },
    'fx-miss': { dur: 0.42, level: -22, build(d, R) { bpNoise(d, 0, 0.4, 1500, 1.2, 1, 0, (t, u) => Math.sin(Math.PI * Math.min(1, u * 1.3)) * (1 - u), R, 500); } },
    'fx-build': { dur: 0.5, level: -16, build(d, R) { for (const t of [0, 0.12, 0.24]) bpNoise(d, t, 0.03, 950, 5, 1, 0.0005, 0.012, R); bpNoise(d, 0.2, 0.3, 4000, 0.7, 0.2, 0.05, 0.12, R, 0, 'hp'); } },
    'fx-wind': { dur: 0.8, level: -22, build(d, R) { bpNoise(d, 0, 0.8, 600, 1.4, 1, 0, (t, u) => Math.sin(Math.PI * u), R, 900); } },
    // ---- UI-V4 §14.1（peak dBFS 即 level）----
    'ui-hover': { dur: 0.025, level: -32, build(d) { sine(d, 0, 0.025, 2200, 1, 0.002, 0.008); sine(d, 0, 0.025, 3300, 0.355, 0.002, 0.008); } },
    'ui-press-wood': { dur: 0.04, level: -20, build(d, R) { bpNoise(d, 0, 0.018, 1100, 6, 1, 0.0005, 0.006, R); sine(d, 0, 0.04, 620, 0.6, 0.001, 0.01); } },
    'ui-press-bronze': { dur: 0.18, level: -24, build(d) { fm(d, 0, 0.18, 1600, 1.41, 2, 1, 0.06, 0); } },
    'ui-press-paper': { dur: 0.06, level: -24, build(d, R) { bpNoise(d, 0, 0.06, 2500, 0.7, 1, 0, (t) => Math.exp(-Math.pow((t - 0.012) / 0.007, 2)) + 0.7 * Math.exp(-Math.pow((t - 0.036) / 0.008, 2)), R, 0, 'hp'); } },
    'ui-stamp': { dur: 0.12, level: -16, build(d, R) { sine(d, 0, 0.09, 110, 1, 0.001, 0.03, 70); bpNoise(d, 0.015, 0.06, 2000, 0.7, 0.6, 0.001, 0.02, R, 0, 'hp'); bpNoise(d, 0, 0.018, 1100, 6, 0.5, 0.0005, 0.006, R); } },
    'ui-deny': { dur: 0.12, level: -20, build(d, R) { sine(d, 0, 0.12, 90, 1, 0.002, 0.05); const t0 = 0; const n = Math.round(0.08 * Rate); let lp = 0; for (let i = 0; i < n; i++) { const t = i / Rate; const sq = Math.sign(Math.sin(TWO_PI * 141 * t)); lp += (sq - lp) * 0.16; d[i] += lp * 0.35 * env(t, 0.002, 0.03); } } },
    'ui-paper-open': { dur: 0.26, level: -26, build(d, R) { bpNoise(d, 0, 0.26, 1500, 1.5, 1, 0, (t, u) => Math.sin(Math.PI * u), R, 5000); } },
    'ui-paper-close': { dur: 0.18, level: -28, build(d, R) { bpNoise(d, 0, 0.18, 5000, 1.5, 1, 0, (t, u) => Math.sin(Math.PI * u), R, 1500); } },
    'ui-scroll': { dur: 0.38, level: -24, build(d, R) { bpNoise(d, 0, 0.38, 400, 0.7, 1, 0, (t, u) => (0.6 + 0.4 * Math.sin(TWO_PI * 12 * t)) * Math.sin(Math.PI * u), R, 0, 'lp'); bpNoise(d, 0.1, 0.26, 1500, 1.5, 0.5, 0, (t, u) => Math.sin(Math.PI * u), R, 5000); } },
    'ui-page': { dur: 0.09, level: -26, build(d, R) { bpNoise(d, 0, 0.09, 3000, 0.7, 1, 0, (t) => Math.exp(-Math.pow((t - 0.015) / 0.008, 2)) + 0.8 * Math.exp(-Math.pow((t - 0.05) / 0.01, 2)), R, 0, 'hp'); } },
    'ui-tab': { dur: 0.07, level: -22, build(d) { sine(d, 0, 0.035, 1250, 1, 0.0005, 0.01); sine(d, 0.03, 0.035, 1900, 0.9, 0.0005, 0.01); } },
    'ui-toggle-on': { dur: 0.25, level: -24, build(d) { fm(d, 0, 0.25, 2400, 2.0, 1.2, 1, 0.08, 0); } },
    'ui-toggle-off': { dur: 0.04, level: -24, build(d, R) { bpNoise(d, 0, 0.018, 880, 6, 1, 0.0005, 0.006, R); sine(d, 0, 0.04, 496, 0.6, 0.001, 0.01); } },
    'ui-bead': { dur: 0.015, level: -30, build(d, R) { sine(d, 0, 0.012, 3100, 1, 0.0005, 0.004); bpNoise(d, 0, 0.004, 5000, 1, 0.4, 0.0002, 0.002, R, 0, 'hp'); } },
    'ui-badge': { dur: 0.3, level: -28, build(d) { sine(d, 0, 0.3, 1760, 1, 0.002, 0.09); sine(d, 0, 0.3, 2640, 0.6, 0.002, 0.08); } },
    'ui-notify': { dur: 0.8, level: -26, build(d) { [1318.5, 1568, 1760].forEach((f, k) => partials(d, k * 0.07, 0.7, [[f, 0.6, 0.2], [f * 2.76, 0.1, 0.08]], 1)); } },
    'ui-coins': { dur: 0.42, level: -22, build(d, R) { for (let k = 0; k < 5; k++) fm(d, k * 0.045, 0.025, 2800 + R.nextDouble() * 1400, 1.41, 2.2, 0.7, 0.008); } },
    'ui-coin-out': { dur: 0.2, level: -24, build(d) { fm(d, 0, 0.025, 3600, 1.41, 2, 0.7, 0.008); fm(d, 0.05, 0.025, 3000, 1.41, 2, 0.6, 0.008); sine(d, 0.11, 0.06, 160, 0.7, 0.002, 0.02); } },
    'ui-grain': { dur: 0.2, level: -28, build(d, R) { bpNoise(d, 0, 0.2, 3000, 1.2, 1, 0, (t, u) => (0.5 + 0.5 * Math.sin(TWO_PI * 25 * t)) * Math.sin(Math.PI * u), R); } },
    'ui-token': { dur: 0.26, level: -24, build(d, R) { bpNoise(d, 0, 0.22, 600, 2, 0.8, 0, (t, u) => u * (1 - u) * 4, R, 3000); bpNoise(d, 0.22, 0.018, 1100, 6, 1, 0.0005, 0.006, R); sine(d, 0.22, 0.04, 620, 0.6, 0.001, 0.01); } },
    'ui-token-refill': { dur: 0.9, level: -24, build(d) { for (let k = 0; k < 8; k++) { const r = Math.pow(2, k / 12); sine(d, k * 0.09, 0.035, 1250 * r, 0.63, 0.0005, 0.01); sine(d, k * 0.09 + 0.03, 0.035, 1900 * r, 0.57, 0.0005, 0.01); } } },
    // ---- ITEMS §7.4: the 12 sfx families of the rare+ abilities (ability.sfx = { id, family, pitch } → SG.Sfx.playItem(sfx)) ----
    'item-ring-blade': { dur: 0.6, level: -14, build(d, R) { bpNoise(d, 0, 0.08, 3500, 0.8, 0.6, 0.002, 0.03, R, 0, 'hp'); partials(d, 0.01, 0.58, [[1910, 0.6, 0.35], [3130, 0.4, 0.25], [4620, 0.22, 0.15]], 1); } },
    'item-whoosh-pole': { dur: 0.38, level: -16, build(d, R) { bpNoise(d, 0, 0.36, 400, 1.6, 1, 0, (t, u) => Math.sin(Math.PI * u) * Math.sin(Math.PI * u), R, 1700); } },
    'item-bell': { dur: 0.8, level: -16, build(d) { partials(d, 0, 0.8, [[880, 0.6, 0.6], [2210, 0.35, 0.35], [3300, 0.18, 0.2]], 1); } },
    'item-twang': { dur: 0.42, level: -14, build(d, R) { bpNoise(d, 0, 0.01, 4000, 0.7, 0.6, 0.0005, 0.004, R, 0, 'hp'); for (const h of [1, 2, 3, 4]) sine(d, 0, 0.4, 196 * h, 0.5 / h, 0.001, 0.25 / h, 180 * h); } },
    'item-hoof-neigh': { dur: 0.75, level: -14, build(d, R) { for (const t of [0, 0.09]) { bpNoise(d, t, 0.03, 420, 2, 0.8, 0.0005, 0.012, R); sine(d, t, 0.05, 125, 0.6, 0.001, 0.02); } const i0 = Math.round(0.2 * Rate), n = Math.round(0.5 * Rate); let ph = 0; for (let i = 0; i < n; i++) { const u = i / n, t = i / Rate; const f = 620 + 280 * Math.sin(Math.PI * u) + 25 * Math.sin(TWO_PI * 9 * t); ph += TWO_PI * f / Rate; d[i0 + i] += (Math.sin(ph) + 0.35 * Math.sin(2 * ph) + 0.2 * Math.sin(3 * ph)) * 0.3 * Math.sin(Math.PI * u); } } },
    'item-clank-plate': { dur: 0.3, level: -14, build(d, R) { partials(d, 0, 0.3, [[700, 0.6, 0.12], [1350, 0.45, 0.1], [2150, 0.3, 0.07]], 1); bpNoise(d, 0, 0.05, 2500, 0.8, 0.5, 0.0005, 0.015, R); } },
    'item-page-flip': { dur: 0.12, level: -20, build(d, R) { bpNoise(d, 0, 0.12, 3000, 0.7, 1, 0, (t) => Math.exp(-Math.pow((t - 0.02) / 0.01, 2)) + 0.8 * Math.exp(-Math.pow((t - 0.07) / 0.012, 2)), R, 0, 'hp'); } },
    'item-stamp': { dur: 0.16, level: -14, build(d, R) { sine(d, 0, 0.12, 100, 1, 0.001, 0.04, 65); bpNoise(d, 0.015, 0.07, 2000, 0.7, 0.6, 0.001, 0.025, R, 0, 'hp'); } },
    'item-clink-helm': { dur: 0.2, level: -18, build(d) { fm(d, 0, 0.08, 2600, 1.41, 2.2, 0.7, 0.03); fm(d, 0.07, 0.1, 2300, 1.41, 2, 0.5, 0.04); } },
    'item-pluck': { dur: 0.5, level: -16, build(d) { pluckAt(d, 0, 330, 1.4); } },
    'item-chime': { dur: 0.7, level: -18, build(d) { [1568, 1976, 2349].forEach((f, k) => partials(d, k * 0.06, 0.6, [[f, 0.5, 0.25], [f * 2.76, 0.08, 0.08]], 1)); } },
    'item-thud-wood': { dur: 0.2, level: -14, build(d, R) { sine(d, 0, 0.15, 300, 0.8, 0.001, 0.04, 200); bpNoise(d, 0, 0.05, 600, 3, 0.7, 0.0005, 0.02, R); } },
    'ui-month': { dur: 2.5, level: -18, build(d) { partials(d, 0, 2.5, [[220, 1, 1.2], [552, 0.6, 0.9], [912, 0.4, 0.6], [1320, 0.25, 0.4]], 1); partials(d, 0, 2.5, GONG.map(p => [p[0] * 0.5, p[1] * 0.4, p[2] * 1.5]), 0.6, 0.8); } },
  };
  function renderRecipe(name) {
    const r = RECIPES[name];
    const d = new Float32Array(Math.ceil(r.dur * Rate));
    const R = SG.SeededRandom ? SG.SeededRandom(hashString(name)) : { nextDouble: (() => { let a = hashString(name); return () => { a = (a * 1103515245 + 12345) & 0x7fffffff; return a / 0x7fffffff; }; })() };
    r.build(d, R, Rate);
    // 10 ms fade-out; normalise the peak to the recipe's level (dBFS before the SFX bus)
    const tail = Math.min(d.length, Math.round(0.006 * Rate));
    for (let i = 0; i < tail; i++) d[d.length - 1 - i] *= i / tail;
    let pk = 0; for (let i = 0; i < d.length; i++) { if (!isFinite(d[i])) d[i] = 0; pk = Math.max(pk, Math.abs(d[i])); }
    const target = Math.pow(10, (r.level === undefined ? -12 : r.level) / 20);
    if (pk > 0) for (let i = 0; i < d.length; i++) d[i] *= target / pk;
    return d;
  }
  function render(name) { return RECIPES[name] ? renderRecipe(name) : renderV3(name); }

  // ---------------------------------------------------------- 播放 --
  const pcm = new Map();          // name → Float32Array（音效）
  const buffers = new Map();      // key → AudioBuffer（需要 AudioContext）
  let ctx = null, master = null, sfxBus = null, musicBus = null, voiceIn = null, voiceOut = null, hallIn = null;
  let inited = false, gestureHooked = false;

  function AC() {
    if (typeof window === 'undefined') return null;
    return window.AudioContext || window.webkitAudioContext || null;
  }
  function setP(p, v) { try { p.value = v; } catch (e) { /* 忽略 */ } }
  function hallImpulse(c, sec) {
    const n = Math.round(sec * c.sampleRate), b = c.createBuffer(2, n, c.sampleRate);
    const R = SG.SeededRandom ? SG.SeededRandom(7331) : { nextDouble: Math.random };
    for (let ch = 0; ch < 2; ch++) { const x = b.getChannelData(ch); for (let i = 0; i < n; i++) x[i] = (R.nextDouble() * 2 - 1) * Math.pow(1 - i / n, 2.4) * Math.exp(-i / (n * 0.35)); }
    return b;
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
      master = ctx.createGain(); master.gain.value = vol('master'); master.connect(ctx.destination);
      sfxBus = ctx.createGain(); sfxBus.gain.value = vol('sfx'); sfxBus.connect(master);
      // 音乐总线：人声闪避（§B10.2）作用在这里，与短乐 / 号令对主曲的压低分开
      musicBus = ctx.createGain(); musicBus.gain.value = 1; musicBus.connect(master);
      // 人声总线：高通 90 Hz → 压缩（−20 dB，3 : 1，knee 6，起 3 ms、落 120 ms）→ 0.9 × 语音音量
      voiceIn = ctx.createGain();
      const hp = ctx.createBiquadFilter(); hp.type = 'highpass'; setP(hp.frequency, 90); setP(hp.Q, 0.7);
      const comp = ctx.createDynamicsCompressor();
      setP(comp.threshold, -20); setP(comp.ratio, 3); setP(comp.knee, 6); setP(comp.attack, 0.003); setP(comp.release, 0.12);
      voiceOut = ctx.createGain(); voiceOut.gain.value = voiceGain();
      voiceIn.connect(hp); hp.connect(comp); comp.connect(voiceOut); voiceOut.connect(master);
      try {
        const conv = ctx.createConvolver(); conv.buffer = hallImpulse(ctx, 1.2);
        hallIn = ctx.createGain(); hallIn.gain.value = 1; hallIn.connect(conv); conv.connect(voiceOut);
      } catch (e) { hallIn = null; }
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

  // ---------------------------------------------------------- 设置（AUDIO-V4 §B10.4、UI-V4 §7 第 1–4、6–9 行） --
  // 默认值来自 SG.VoiceData.CONFIG（玩家决定的默认值只在那一处）；本文件只在它尚未载入时用同样的值兜底
  const FALLBACK = { master: 100, music: 100, sfx: 100, voice: 100, musicStyle: 'suite', language: 'native', frequency: 'essential', subtitles: 'both', readAloud: false };
  function cfg(k) {
    const C = SG.VoiceData && SG.VoiceData.CONFIG;
    if (C) { if (C.volumes && C.volumes[k] !== undefined) return C.volumes[k]; if (C[k] !== undefined) return C[k]; }
    return FALLBACK[k];
  }
  const ROWS = [
    { id: 'audio.master', k: 'master', row: 1, label: '主音量', type: 'slider' },
    { id: 'audio.music', k: 'music', row: 2, label: '音乐', type: 'slider', sub: '试听' },
    { id: 'audio.sfx', k: 'sfx', row: 3, label: '音效', type: 'slider', sub: '试听' },
    { id: 'audio.voice', k: 'voice', row: 4, label: '语音', type: 'slider', sub: '试听' },
    { id: 'audio.musicStyle', k: 'musicStyle', row: 6, label: '音乐风格', type: 'seg', options: [['suite', '君主乐章'], ['region', '地域（第三版）']] },
    { id: 'audio.voiceLang', k: 'language', row: 7, label: '语音语言', type: 'seg', options: [['native', '原声（各说其语）'], ['zh', '一律汉语'], ['off', '关']] },
    { id: 'audio.voiceFreq', k: 'frequency', row: 8, label: '语音频率', type: 'seg', options: [['all', '全部'], ['essential', '精简（仅重要）'], ['critical', '关键（仅关键）']] },
    { id: 'audio.subtitles', k: 'subtitles', row: 9, label: '字幕', type: 'seg', options: [['both', '原文＋中文'], ['zh', '仅中文'], ['off', '关']] },
    { id: 'audio.readAloud', k: 'readAloud', row: 49, label: '朗读', type: 'toggle', tab: 'access', sub: '系统语音，仅本地；月报、报告、装备说明' },
  ];
  const memSet = {};
  function readStore() { try { if (typeof localStorage === 'undefined') return {}; const t = localStorage.getItem(SETTINGS_KEY); const o = t ? JSON.parse(t) : {}; return o && typeof o === 'object' ? o : {}; } catch (e) { return {}; } }
  function writeStore(k, v) { try { if (typeof localStorage === 'undefined') return; const o = readStore(); o[k] = v; localStorage.setItem(SETTINGS_KEY, JSON.stringify(o)); } catch (e) { /* 隐私模式 */ } }
  function getSetting(k) {
    const row = ROWS.find(r => r.k === k);
    try { if (row && SG.Settings && SG.Settings.has && SG.Settings.has(row.id)) { const v = SG.Settings.get(row.id); if (v !== undefined) return v; } } catch (e) { /* 忽略 */ }
    if (memSet[k] !== undefined) return memSet[k];
    const s = readStore()[k];
    return s !== undefined ? s : cfg(k);
  }
  function setSetting(k, v) {
    const row = ROWS.find(r => r.k === k);
    if (row && SG.Settings && SG.Settings.has && SG.Settings.has(row.id)) return SG.Settings.set(row.id, v);
    memSet[k] = v; writeStore(k, v); applySetting(k, v); return true;
  }
  function vol(k) { const v = +getSetting(k); return isFinite(v) ? Math.max(0, Math.min(150, v)) / 100 : 1; }
  function voiceGain() { return 0.9 * vol('voice'); }
  function applySetting(k) {
    try {
      const t = ctx ? ctx.currentTime : 0;
      if (k === 'master' && master) master.gain.setTargetAtTime(vol('master'), t, 0.03);
      if (k === 'sfx' && sfxBus) sfxBus.gain.setTargetAtTime(vol('sfx'), t, 0.03);
      if (k === 'voice' && voiceOut) voiceOut.gain.setTargetAtTime(voiceGain(), t, 0.03);
      if (k === 'music' && engine) engine.setVolume(MUSIC_VOLUME * musicVol * vol('music'));
      if (k === 'musicStyle') { playingKey = null; startMusicIfReady(); }
    } catch (e) { /* 忽略 */ }
  }
  let rowsDefined = false;
  function defineRows() {
    if (rowsDefined || !SG.Settings || !SG.Settings.define) return;
    rowsDefined = true;
    for (const r of ROWS) {
      try {
        if (SG.Settings.has(r.id)) continue;
        SG.Settings.define({ id: r.id, tab: r.tab || 'audio', order: r.row, row: r.row, label: r.label, sub: r.sub, type: r.type, options: r.options,
          min: r.type === 'slider' ? 0 : undefined, max: r.type === 'slider' ? 100 : undefined, step: r.type === 'slider' ? 5 : undefined,
          format: r.type === 'slider' ? (v => Math.round(v) + ' %') : undefined, default: () => cfg(r.k), store: { key: SETTINGS_KEY, path: r.k },
          owner: 'audio', apply: () => applySetting(r.k) });
      } catch (e) { /* 忽略 */ }
    }
  }

  // ---------------------------------------------------------- 背景音乐 --
  // 依次补载：必需的四个音乐文件，第四版的君主乐章与人声（缺了就补），以及可选的模块（文件不存在时跳过）
  const MUSIC_FILES = [
    ['music.js', () => !!SG.Music],
    ['music-inst.js', () => !!(SG.Music && SG.Music.INST && SG.Music.INST.zheng)],
    ['music-han.js', () => !!(SG.Music && SG.Music.PIECES && SG.Music.PIECES.title)],
    ['music-world.js', () => !!(SG.Music && SG.Music.PIECES && SG.Music.PIECES['map@wa'])],
    ['music-suites.js', () => !!(SG.MusicSuites && SG.MusicSuites.buildSuite), true],
    ['music-suites-gen.js', () => !!(SG.MusicSuites && SG.MusicSuites.GEN && Object.keys(SG.MusicSuites.GEN).length), true],
    ['voice-data.js', () => !!SG.VoiceData, true],
    ['voice-cry.js', () => !!SG.VoiceCry, true],
    ['voice.js', () => !!SG.Voice, true],
  ];
  const ONE_SHOT = { victory: 1, defeat: 1 };
  const OVERLAY = { clash: 1 };
  const HOLD_AFTER_ONE_SHOT = { map: 1, council: 1 };
  const STINGER_GAP = 15;          // 冲锋乐句最短间隔（秒）
  const SUITE_KINDS = { map: 1, council: 1, battle: 1, 'battle-defend': 1, victory: 1, defeat: 1, ending: 1 };
  const scriptBase = (function () {
    try {
      const s = typeof document !== 'undefined' && document.currentScript && document.currentScript.src;
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
  let previewSuite = null;        // 选君主画面的试听（覆盖玩家乐章）

  function musicComplete() { return MUSIC_FILES.every(f => f[3] || f[1]()); }
  function loadMusicScripts() {
    if (musicLoading || musicLoadFailed || typeof document === 'undefined' || musicComplete()) return;
    if (!scriptBase) { warnEmpty('找不到 audio.js 的路径，无法补载'); musicLoadFailed = true; return; }
    musicLoading = true;
    let i = 0;
    const next = () => {
      while (i < MUSIC_FILES.length && (MUSIC_FILES[i][1]() || (MUSIC_FILES[i][3] && MUSIC_FILES[i]._tried))) i++;
      if (i >= MUSIC_FILES.length) { musicLoading = false; afterLoad(); return; }
      const f = MUSIC_FILES[i++];
      f._tried = true;
      const el = document.createElement('script');
      el.src = scriptBase + f[0];
      el.async = false;
      el.onload = next;
      el.onerror = () => {
        if (f[2]) { next(); return; }              // 第四版的可选文件：缺了不影响第三版音乐
        musicLoading = false; musicLoadFailed = true; warnEmpty('载入失败：' + el.src);
      };
      (document.head || document.documentElement).appendChild(el);
    };
    next();
  }
  function afterLoad() {
    prewarm(); startMusicIfReady();
    try { if (SG.Voice && SG.Voice.init) SG.Voice.init(); } catch (e) { /* 忽略 */ }
    defineRows();
  }
  function warnEmpty(why) {
    if (warnedEmpty) return;
    warnedEmpty = true;
    console.warn('背景音乐不可用（' + why + '）。index.html 应依次载入 music.js、music-inst.js、music-han.js、music-world.js，或都不载入（由 audio.js 自动补载）。');
  }
  function prewarm() {
    try {
      if (!SG.Music) return;
      const k = resolveKey(Sfx.musicKind || 'title');
      if (k) SG.Music.prepare(k);
    } catch (e) { /* 忽略 */ }
  }
  function ensureEngine() {
    if (engine) return engine;
    if (!ctx || !master || !SG.Music || !SG.Music.Engine) return null;
    try {
      engine = new SG.Music.Engine(ctx, musicBus || master, { volume: MUSIC_VOLUME * musicVol * vol('music') });
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

  // ---------------------------------------------------------- 君主乐章的解析（AUDIO-V4 §A3、§A11） --
  function suitesOn() { return getSetting('musicStyle') !== 'region' && !!(SG.MusicSuites && SG.MusicSuites.resolve); }
  function rulerName(f) { try { const g = SG.G; return g && f >= 0 && g.factions && g.factions[f] ? g.ruler(f).name : null; } catch (e) { return null; } }
  // 玩家的乐章：setSuite 指定的，否则玩家势力当前的君主（继承之后自动跟上）
  function playerSuite() {
    if (Sfx.suite) return Sfx.suite;
    const g = SG.G;
    return g && g.player >= 0 ? rulerName(g.player) : null;
  }
  // 战斗：玩家进攻 / 防守都用玩家的乐章；旁观（玩家不在其中）用攻方君主的乐章
  function battleSuite() {
    try {
      const bc = SG.BattleController && SG.BattleController.current;
      if (bc && bc.playerSide < 0 && bc.M && bc.M.setup) return rulerName(bc.M.setup.attacker);
    } catch (e) { /* 忽略 */ }
    return playerSuite();
  }
  function resolveKey(kind) {
    if (!SG.Music) return null;
    if (kind === 'duel') {
      if (suitesOn() && SG.MusicSuites.duel) {
        try { const D = SG.DuelGame && SG.DuelGame.active; if (D && D.genA && D.genB) { const k = SG.MusicSuites.duel(D.genA, D.genB); if (k) return k; } } catch (e) { /* 回退 */ }
      }
      return SG.Music.resolve('duel', Sfx.culture);
    }
    if (SUITE_KINDS[kind] && suitesOn()) {
      const who = previewSuite || (kind === 'battle' || kind === 'battle-defend' ? battleSuite() : playerSuite());
      if (who) { try { const k = SG.MusicSuites.resolve(kind, { suite: who }); if (k) return k; } catch (e) { /* 回退第三版 */ } }
    }
    return SG.Music.resolve(kind, Sfx.culture);
  }

  function startMusicIfReady() {
    try {
      const kind = Sfx.musicKind;
      if (!kind || !Sfx.musicOn || !ctx || ctx.state !== 'running') return;
      if (!musicComplete()) loadMusicScripts();
      if (!SG.Music || !SG.Music.Engine) return;
      if (Sfx.musicFinished && ONE_SHOT[kind]) return;
      const key = resolveKey(kind);
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
        if (!ok && req === playReq && playingKey === key) playingKey = null;
        if (ok && /^(battle|duel|suite:[^:]+:battle|suite-duel)/.test(key)) prewarmStingers(e);
      }, err => { if (req === playReq && playingKey === key) playingKey = null; console.warn(err); });
    } catch (e) { console.warn(e); }
  }
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
    let opts = null;
    if (culture && typeof culture === 'object') { opts = culture; culture = opts.culture; }
    // 试听（选君主画面）只对带 { suite } 的那一次请求有效；之后不带的请求回到玩家的乐章
    previewSuite = opts && opts.suite !== undefined ? (opts.suite ? (typeof opts.suite === 'string' ? opts.suite : opts.suite.name) : null) : null;
    if (OVERLAY[kind]) { Sfx.stinger(kind); return; }
    if (kind === 'fanfare') { Sfx.fanfare((opts && opts.suite) || playerSuite()); return; }
    if (HOLD_AFTER_ONE_SHOT[kind] && oneShotBusy()) {
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
      startMusicIfReady();           // 同一曲目：地域、君主或试听可能变了（resolveKey 相同则什么也不做）
      return;
    }
    Sfx.musicKind = kind;
    Sfx.musicFinished = false;
    if (ONE_SHOT[kind]) playingKey = null;
    if (Sfx.musicOn) startMusicIfReady();
  }

  // 冲锋乐句跟随主曲目：移调到其主音，速度取其拍速的整数比（第四版 132–168，§A8），复拍子按附点四分对拍
  const FOLLOW_LO = 132, FOLLOW_HI = 168;
  const STINGER_BPM_MAX = 176;      // a stinger may be sped up to this tempo so its accent still lands on the clash beat
  function followPlan(e, key) {
    let info = e.mainInfo();
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
        if (b >= FOLLOW_LO && b <= FOLLOW_HI && (!bpm || Math.abs(b - target) < Math.abs(bpm - target))) bpm = b;
      }
    }
    return { info, transpose, bpm, grid };
  }
  // 攻击方的君主（clash 的 attacker 或 SG.Voice.clashCtx）
  function attackerSuite(opts) {
    if (opts && opts.suite) return typeof opts.suite === 'string' ? opts.suite : opts.suite.name;
    try {
      const c = SG.Voice && SG.Voice.clashCtx;
      const g = SG.G;
      if (c && c.attacker && c.attacker.gen && g && c.attacker.gen.faction >= 0) return rulerName(c.attacker.gen.faction);
    } catch (e) { /* 忽略 */ }
    return null;
  }
  function stingerBase(kind, opts) {
    if (kind === 'clash' && suitesOn()) {
      const who = attackerSuite(opts) || (playingKey && /^suite:/.test(playingKey) ? playingKey.split(':')[1] : null);
      if (who && SG.MusicSuites.ensure(who)) return { base: 'suite:' + who + ':clash', suite: true };
    }
    return { base: SG.Music.resolve(kind, Sfx.culture), suite: false };
  }
  function prewarmStingers(e) {
    try {
      const sb = stingerBase('clash', {});
      if (!sb.base) return;
      const ks = sb.suite ? ['', '-b', '-short', '-crit'] : ['', '-b', '-c', '-short'];
      for (const s of ks) {
        const k = sb.base + s;
        if (!SG.Music.get(k)) continue;
        const p = followPlan(e, k);
        SG.Music.prepare(SG.Music.variant(k, { transpose: p.transpose, bpm: p.bpm }) || k, e.pins);
      }
    } catch (err) { /* 忽略 */ }
  }
  // 攻击画面的时间：接触 → melee1（第四版 1.62 / 速度；第三版时间轴按 TL 算），会心的强奏时刻
  function clashTiming(speed) {
    const C = SG.Clash, TL = C && C.TL;
    // 第四版（有 tlMap）：1.62 s 与会心一拍的正中；没有 tlMap 的 clash.js 仍是第三版时间轴（与 battle-controller 的 v4tl 判断一致）：
    // contact 1.66 → melee1 2.56，慢镜头 0.16 × 1/0.42 → 1.121 s；会心（第三版无慢拍）落在 contact + 0.36 → 0.58 s
    let melee = 1.62, critStab = 0.36 + 0.43;
    const tl = (TL && TL.melee1 && TL.contact) ? TL : { contact: 1.66, melee1: 2.56, slowW: 0.16, slowK: 0.42 };
    if (C && typeof C.tlMap === 'function' && TL) {
      try { melee = C.tlMap(TL.melee1) - C.tlMap(TL.contact); critStab = C.tlMap(TL.contact + 0.36) - C.tlMap(TL.contact); } catch (e) { /* 默认 */ }
    } else if (!(C && typeof C.tlMap === 'function')) {
      const sw = tl.slowW || 0, sk = tl.slowK || 1;
      melee = (tl.melee1 - tl.contact) - sw + sw / sk;
      critStab = 0.36 <= sw ? 0.36 / sk : sw / sk + (0.36 - sw);
    }
    if (!(melee > 0 && melee < 4)) melee = 1.62;
    if (!(critStab > 0 && critStab < 3)) critStab = 0.36 + 0.43;
    speed = speed > 0 ? speed : 1;
    return { melee: melee / speed, critStab: critStab / speed };
  }
  // 冲锋乐句：快速（≥ 1.5）用一小节短句，重音落在 接触 + melee1 / 速度；会心用 clash-crit（不受 15 秒间隔），强奏对准会心一拍
  function stingerImpl(kind, opts) {
    opts = opts || {};
    let speed = opts.speed;
    if (speed === undefined) { try { speed = (SG.Clash && SG.Clash.speed) || 1; } catch (err) { speed = 1; } }
    const cctx = SG.Voice && SG.Voice.clashCtx;
    const crit = opts.crit !== undefined ? !!opts.crit : !!(cctx && cctx.crit);
    // 人声：接触时攻方武将的呐喊与群呼（音乐关着也照常）
    if (kind === 'clash' && !opts.noVoice) { try { if (SG.Voice && SG.Voice.onClashContact && cctx) SG.Voice.onClashContact(Object.assign({}, cctx, { crit })); } catch (e) { /* 忽略 */ } }
    if (!Sfx.musicOn || !ctx || ctx.state !== 'running' || !SG.Music) return false;
    const sb = stingerBase(kind, opts);
    const base = sb.base;
    const e = ensureEngine();
    if (!base || !e) return false;
    const now = ctx.currentTime;
    if (!opts.force && !crit && now - lastStinger < STINGER_GAP) return false;
    let key = base, accentBeats = 4;
    if (crit && sb.suite && SG.Music.get(base + '-crit')) { key = base + '-crit'; accentBeats = 1; }
    // 快速（≥ 1.5），或两小节句即使提到 ♩176 重音也会晚于 melee1 超过 0.2 s（第三版时间轴：接触后 1.12 s）时，用一小节短句
    else if ((speed >= (sb.suite ? 1.5 : 2) || (sb.suite && 240 / STINGER_BPM_MAX - clashTiming(speed).melee > 0.2)) && SG.Music.get(base + '-short')) { key = base + '-short'; accentBeats = 2; }
    else {
      const vs = [base, base + '-b', base + '-c'].filter(k => SG.Music.get(k) && k !== lastStingerKey);
      if (vs.length) key = vs[Math.floor(Math.random() * vs.length)];
    }
    if (opts.variant && SG.Music.get(opts.variant)) key = opts.variant;
    const plan = followPlan(e, key);
    const info = plan.info, grid = plan.grid;
    let bpm = plan.bpm, at;
    if (sb.suite) {
      // 第四版对拍：短句 / 会心按时间轴放置（不再对齐主曲的拍点），常规的两小节句从接触开始
      const T = clashTiming(speed);
      const want = key.endsWith('-crit') ? T.critStab : key.endsWith('-short') ? T.melee : T.melee;
      const b0 = bpm || (SG.Music.get(key) || {}).bpm || 150;
      let lead = accentBeats * 60 / b0;
      if (lead > want + 0.05) { bpm = Math.min(STINGER_BPM_MAX, Math.max(FOLLOW_LO, accentBeats * 60 / Math.max(0.2, want))); lead = accentBeats * 60 / bpm; }
      at = now + Math.max(0.02, Math.min(0.7, want - lead));
    }
    const vk = SG.Music.variant(key, { transpose: plan.transpose, bpm }) || key;
    lastStinger = now; lastStingerKey = key;
    e.play(vk, at !== undefined ? { overlay: true, fade: 0.2, duck: 0.3, at } : { overlay: true, fade: 0.2, duck: 0.3, sync: !!info && !info.pending, grid }).catch(err => console.warn(err));
    Sfx.lastStinger = { key, vk, at: at !== undefined ? at - now : null, speed, crit };
    return true;
  }
  // 号令（§A3）：叠加；从不打断一次性的胜利 / 战败短曲；同一君主 4 秒内不重复
  let lastFanfare = { who: null, t: -1e9 };
  function fanfareImpl(who, opts) {
    opts = opts || {};
    if (!who || !Sfx.musicOn || !ctx || ctx.state !== 'running' || !SG.Music || !SG.MusicSuites) return 0;
    if (oneShotBusy()) return 0;
    const name = typeof who === 'string' ? who : who.name;
    const now = ctx.currentTime;
    if (lastFanfare.who === name && now - lastFanfare.t < 4) return 0;
    const key = SG.MusicSuites.resolve('fanfare', { suite: name });
    const e = ensureEngine();
    if (!key || !e) return 0;
    lastFanfare = { who: name, t: now };
    e.play(key, { overlay: true, fade: 0.15, duck: opts.duck === undefined ? 0.35 : opts.duck }).catch(err => console.warn(err));
    try { const d = SG.Music.duration(key); return d ? d.total : 3.6; } catch (err) { return 3.6; }
  }
  // 指令短乐（DOMESTIC-FX §8.4；AUDIO-V4 §A10）：'sting:<id>[:tier][@family]'，回退汉地；大成里的「霸业」动机换成君主的头三拍
  const BAYE = ['5,. 1_ 2 5', '6,. 2_ 3 6'];
  function cmdStingImpl(id, tier, opts) {
    opts = opts || {};
    if (!Sfx.musicOn || !ctx || ctx.state !== 'running' || !SG.Music) return null;
    let family = opts.family;
    if (!family && SG.MusicSuites) { try { const s = SG.MusicSuites.suiteOf(playerSuite()); family = s && s.family; } catch (e) { family = null; } }
    const t = tier && tier !== 'normal' ? ':' + tier : '';
    const cands = [];
    if (family && family !== 'hua') cands.push('sting:' + id + t + '@' + family);
    cands.push('sting:' + id + t);
    if (t) { if (family && family !== 'hua') cands.push('sting:' + id + '@' + family); cands.push('sting:' + id); }
    let key = cands.find(k => SG.Music.get(k));
    if (!key) return null;
    // 君主的头三拍替换「霸业」动机（只在 great 档、且该短乐里确有这个动机时）
    const head = opts.headMotif !== undefined ? opts.headMotif : (tier === 'great' && SG.MusicSuites ? SG.MusicSuites.headCell(playerSuite()) : null);
    if (head && tier === 'great') key = withHead(key, head) || key;
    const e = ensureEngine();
    if (!e) return null;
    e.play(key, { overlay: true, fade: 0.15, duck: 0.35 }).catch(err => console.warn(err));
    const C = SG.Music.compile(key);
    const dur = SG.Music.duration(key);
    const secs = {};
    try { let acc = 0; for (const n of C.form) { const s = C.sections[n]; if (!(n in secs)) secs[n] = acc; acc += s.dur; } } catch (err) { /* 忽略 */ }
    return { key, duration: dur ? dur.total : 0, sections: secs };
  }
  function withHead(key, head) {
    const MS = SG.MusicSuites, P = SG.Music.get(key);
    if (!P || !MS) return null;
    const hk = key + '|head:' + head;
    if (SG.Music.get(hk)) return hk;
    const sc = MS.scaleInfo(P.scale || '1 2 3 5 6');
    const notes = MS.parseMotif(head + ' |').filter(n => !n.bar).map(n => MS.respellNote(n, sc));
    const repl = notes.map(n => MS.noteHead(n) + ':' + (+n.dur.toFixed(4))).join(' ');
    let hit = false;
    const Q = JSON.parse(JSON.stringify(P));
    delete Q.key_;
    for (const sec of Object.values(Q.sections || {})) for (const [v, str] of Object.entries(sec)) {
      if (typeof str !== 'string') continue;
      for (const b of BAYE) if (str.indexOf(b) >= 0) { sec[v] = str.split(b).join(repl); hit = true; }
    }
    if (!hit) return null;
    Q._derived = true; Q._base = key;
    SG.Music.add(hk, Q);
    if (SG.Music.validate(hk).length) { SG.Music.remove(hk); return null; }
    return hk;
  }
  // 选君主画面（§A3）：悬停 / 点按 → 号令；停留 1.2 秒 → 他的地图曲（0.8 秒交叉）与名句
  let previewTimer = 0;
  function previewRulerImpl(who) {
    const name = typeof who === 'string' ? who : who && who.name;
    if (previewTimer) { clearTimeout(previewTimer); previewTimer = 0; }
    if (!name) return;
    // 停留 0.3 秒才奏号令（列表上快速划过不连响），再过 1.2 秒换成他的地图乐章并说出他的名句
    previewTimer = setTimeout(() => {
      fanfareImpl(name, { duck: 0.35 });
      previewTimer = setTimeout(() => {
        previewTimer = 0;
        musicImpl('map', { suite: name });
        try { if (SG.Voice) SG.Voice.say(name, 'sig', { force: true, scene: 'strategy' }); } catch (e) { /* 忽略 */ }
      }, 1200);
    }, 300);
  }
  // 人声闪避（§B10.2）：音乐总线压到 0.5（起 60 ms），片段结束后 400 ms 恢复；短乐 / 号令正在压低主曲时不再叠加
  let duckUntil = 0, duckTimer = 0, duckLevelNow = 1;
  function overlayActive() { try { return !!(engine && engine.tracks && engine.tracks.some(t => t.overlay && !t.stopping && !t.unducked)); } catch (e) { return false; } }
  function setBus(level, tau) {
    if (Math.abs(level - duckLevelNow) < 1e-3) return;
    duckLevelNow = level;
    const t = ctx.currentTime;
    musicBus.gain.cancelScheduledValues(t);
    musicBus.gain.setValueAtTime(musicBus.gain.value, t);
    musicBus.gain.linearRampToValueAtTime(level, t + tau);
  }
  function duckForVoiceImpl(sec) {
    if (!ctx || !musicBus) return;
    const R = (SG.VoiceData && SG.VoiceData.RULES) || { duckLevel: 0.5, duckAttack: 0.06, duckRelease: 0.4 };
    duckUntil = Math.max(duckUntil, ctx.currentTime + sec);
    // 每 100 ms 重新判断：短乐 / 号令正在压低主曲时人声不再叠加（取较低的一个），短乐结束后人声的 0.5 接上
    const step = () => {
      const t = ctx.currentTime;
      if (t >= duckUntil - 0.01) { duckTimer = 0; setBus(1, R.duckRelease); return; }
      setBus(overlayActive() ? 1 : R.duckLevel, R.duckAttack);
      duckTimer = setTimeout(step, 100);
    };
    if (duckTimer) clearTimeout(duckTimer);
    step();
  }

  function hookGestures() {
    if (gestureHooked || typeof window === 'undefined' || !window.addEventListener) return;
    gestureHooked = true;
    const h = () => Sfx.unlock();
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

  const lastPlay = new Map();
  const Sfx = {
    musicOn: true,
    soundOn: true,
    uiOn: true,                  // 界面音效（UI-V4 第 5 行，界面任务的设置）：只管 ui-* 音效
    musicKind: null,
    musicFinished: false,
    culture: 'han',
    unlocked: false,
    suite: null,                 // 玩家的君主乐章（null = 玩家势力当前的君主）
    lastStinger: null,
    Rate,

    init() {
      if (inited) return;
      inited = true;
      try {
        hookGestures();
        defineRows();
        setTimeout(() => {
          try {
            ['click', 'coin', 'horn', 'hit'].forEach(getPcm);
            if (musicComplete()) afterLoad(); else loadMusicScripts();
          } catch (e) { /* 忽略 */ }
        }, 0);
      } catch (e) { console.warn(e); }
    },

    unlock() {
      try {
        if (!ensureContext()) return;
        if (ctx.state === 'running') { if (!Sfx.unlocked) { Sfx.unlocked = true; startMusicIfReady(); } return; }
        if (ctx.state === 'closed') return;
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

    // play(name, vol, { pitch（播放速率）, pan })
    play(name, vol, opts) {
      if (vol === undefined || vol === null) vol = 0.8;
      try {
        if (!Sfx.soundOn) return;
        if (/^ui-/.test(name) && !Sfx.uiOn) return;
        if ((name === 'win' || name === 'lose') && playingKey && Sfx.musicOn && (ONE_SHOT[Sfx.musicKind] || Sfx.musicKind === 'ending') && !Sfx.musicFinished) return;
        // 同名 60 ms 内合并；悬停 80 ms 一次（UI-V4 §14.1）
        const tNow = (typeof performance !== 'undefined' ? performance.now() : Date.now());
        const gap = name === 'ui-hover' ? 80 : 60;
        if (tNow - (lastPlay.get(name) || -1e9) < gap && (RECIPES[name] || /^ui-/.test(name))) return;
        lastPlay.set(name, tNow);
        const data = getPcm(name);
        if (!ctx || ctx.state !== 'running') return;
        const buf = toBuffer(name, data);
        if (!buf) return;
        const src = ctx.createBufferSource();
        src.buffer = buf;
        if (opts && opts.pitch) { try { src.playbackRate.value = opts.pitch; } catch (e) { /* 忽略 */ } }
        const g = ctx.createGain(); g.gain.value = vol;
        let last = g;
        src.connect(g);
        if (opts && opts.pan && ctx.createStereoPanner) { const p = ctx.createStereoPanner(); p.pan.value = Math.max(-1, Math.min(1, opts.pan)); g.connect(p); last = p; }
        last.connect(sfxBus);
        src.onended = () => { try { src.disconnect(); g.disconnect(); if (last !== g) last.disconnect(); } catch (e) { /* 忽略 */ } };
        src.start();
      } catch (e) { /* 无音频 */ }
    },

    music(kind, culture) {
      try { musicImpl(kind, culture); } catch (e) { /* 无音频 */ }
    },
    setCulture(culture) {
      try {
        Sfx.culture = culture || 'han';
        if (Sfx.musicOn) startMusicIfReady();
      } catch (e) { /* 无音频 */ }
    },
    // opts：{ speed, force, variant, suite（攻方君主）, crit }；返回是否真的播放了
    stinger(kind, opts) {
      try { return stingerImpl(kind || 'clash', opts); } catch (e) { return false; }
    },
    setMusic(on) {
      try {
        Sfx.musicOn = !!on;
        if (Sfx.musicOn) startMusicIfReady(); else stopMusic(0.8);
      } catch (e) { /* 无音频 */ }
    },
    setMusicVolume(v) {
      try {
        musicVol = Math.max(0, Math.min(1.5, +v || 0));
        if (engine) engine.setVolume(MUSIC_VOLUME * musicVol * vol('music'));
      } catch (e) { /* 忽略 */ }
    },
    setSound(on) { Sfx.soundOn = !!on; },

    // ---- 第四版 ----
    // 玩家的君主乐章（null = 跟随玩家势力当前的君主）；登记并钉住它（不被 4 套上限注销）
    setSuite(name) {
      try {
        const n = name && typeof name === 'object' ? name.name : name;
        if (Sfx.suite && SG.MusicSuites) SG.MusicSuites.pin(Sfx.suite, false);
        Sfx.suite = n || null;
        previewSuite = null;
        if (n && SG.MusicSuites) { SG.MusicSuites.ensure(n); SG.MusicSuites.pin(n); }
        if (Sfx.musicOn) startMusicIfReady();
      } catch (e) { /* 忽略 */ }
    },
    fanfare(who, opts) { try { return fanfareImpl(who, opts); } catch (e) { return 0; } },
    cmdSting(id, tier, opts) { try { return cmdStingImpl(id, tier, opts); } catch (e) { return null; } },
    previewRuler(who) { try { previewRulerImpl(who); } catch (e) { /* 忽略 */ } },
    stopPreview() { if (previewTimer) { clearTimeout(previewTimer); previewTimer = 0; } previewSuite = null; },
    voiceBus() { if (!ctx) ensureContext(); return ctx && voiceIn ? { ctx, input: voiceIn, hall: hallIn } : null; },
    duckForVoice(sec) { try { duckForVoiceImpl(+sec || 0.5); } catch (e) { /* 忽略 */ } },
    setVoiceVolume(v) { setSetting('voice', Math.round(Math.max(0, Math.min(1, +v || 0)) * 100)); },
    setMasterVolume(v) { setSetting('master', Math.round(Math.max(0, Math.min(1, +v || 0)) * 100)); },
    setSfxVolume(v) { setSetting('sfx', Math.round(Math.max(0, Math.min(1, +v || 0)) * 100)); },
    setSetting(k, v) { return setSetting(k, v); },
    get settings() { const o = {}; for (const r of ROWS) o[r.k] = getSetting(r.k); return o; },
    settingRows: ROWS,
    defineSfx(name, recipe) {
      if (!name || !recipe || typeof recipe.build !== 'function') throw new Error('SG.Sfx.defineSfx: recipe.build required');
      if (RECIPES[name] && !recipe.replace) throw new Error('SG.Sfx.defineSfx: duplicate ' + name);
      RECIPES[name] = recipe; pcm.delete(name); buffers.delete(name);
      try { if (SG.Reg && SG.Reg.sfx && SG.Reg.SFX_PREFIX.some(p => name.indexOf(p) === 0) && !SG.Reg.sfx.has(name)) SG.Reg.sfx.def(name, recipe); } catch (e) { /* 忽略 */ }
    },
    sfxNames() { return Object.keys(DUR).concat(Object.keys(RECIPES)); },
    // ITEMS §7.4: ability.sfx = { id, family, pitch } → the family's recipe at that pitch
    playItem(sfx, vol) { if (!sfx) return; const n = RECIPES[sfx.id] ? sfx.id : 'item-' + (sfx.family || 'ring-blade'); Sfx.play(n, vol === undefined ? 0.8 : vol, { pitch: sfx.pitch || 1 }); },
    resolveMusic(kind) { try { return resolveKey(kind); } catch (e) { return null; } },
    clashTiming,

    render(name) { return render(name).slice(); },
    renderMusic(kind, seconds) {
      if (!SG.Music) return Promise.resolve(null);
      return SG.Music.renderOffline(resolveKey(kind) || kind, seconds || 20);
    },
    get context() { return ctx; },
    get playingMusic() { return playingKey; },
    get musicEngine() { return engine; },
    get musicBus() { return musicBus; },
  };
  // 第四版音效登记到 SG.Reg.sfx（ui- / fx- 前缀；COMBAT / 指令按下声的名称不带登记前缀，只在本表）
  try { if (SG.Reg && SG.Reg.sfx) for (const n of Object.keys(RECIPES)) if (SG.Reg.SFX_PREFIX.some(p => n.indexOf(p) === 0) && !SG.Reg.sfx.has(n)) SG.Reg.sfx.def(n, RECIPES[n]); } catch (e) { /* 忽略 */ }

  Sfx._pluck = pluck;
  Sfx._recipes = RECIPES;
  SG.Sfx = Sfx;
  if (!SG.Audio) SG.Audio = Sfx;        // INTEGRATION §10.6 calls the SFX table SG.Audio (shop-screen.js uses SG.Audio.play)
})();
