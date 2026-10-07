'use strict';
/* ==========================================================================
   背景音乐 · 乐器（全部程序合成）
   三类乐器：
   1) 采样型（type 'sample'）：在 JS 里按音高预渲染一次，缓存为 AudioBuffer。
      - 拨弦：扩展 Karplus-Strong（三角 + 滤波噪声激励、拨弦位置梳状、一阶全通微调音高、
        环路低通控制高频衰减、多弦组微失谐、琴体共鸣峰、西塔尔式“贾瓦里”蜂鸣）
        古筝 琵琶 古琴 中阮 扬琴 · 日本筝 · 伽倻琴 · 竖琴 / 里拉琴 · 乌德 桑图尔 卡龙 · 西塔尔 坦布拉 · 冬不拉 · 拨奏低音
      - 打击：模态合成（非谐分音 + 击打噪声 + 音高滑移）
        编钟 定音鼓 · 大鼓 堂鼓 太鼓 · 京锣 小锣 抄锣 钹 · 拍板 木鱼 梆子 ·
        达夫鼓 达布卡 塔布拉 · 杖鼓 北 钲 小金 · 甘美兰（萨隆 金德尔 博南 克农 肯普尔 大锣）肯当 ·
        框鼓 战鼓 指钹 宝思兰 萨满鼓 马蹄
   2) 连奏型（type 'legato'）：单音乐句实时合成——PeriodicWave 振荡器 + 揉弦 LFO（接 detune，单位音分）
      + 滑音 / 倚音 / 颤音的频率自动化 + 弓噪 / 气声（带通噪声，可随音高）+ 共鸣峰滤波链
        二胡 奚琴 卡曼恰 马头琴 大提琴 · 笛子 箫 尺八 筱笛 大笒 奈伊 班苏里 苏林 哨笛 ·
        唢呐 觱篥 阿夫洛斯 风笛 祖尔纳 · 罗马号角 圆号 · 呼麦泛音
   3) 复音型（type 'poly'）：弦乐垫音、低音弦乐、笙、合唱、铜管组、持续低音、喉音低吟、合成低音
   每种乐器的 gain / pan / rev 是缺省混音参数，可被乐曲的声部设置覆盖。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const MU = SG.Music;
  if (!MU) return;
  const { rng, mtof, ctxRes } = MU.util;
  const def = MU.defineInst;
  const TAU = Math.PI * 2;

  // ========================================================= JS 端 DSP 工具 ==
  const SIN_N = 8192;
  const SIN = new Float32Array(SIN_N + 1);
  for (let i = 0; i <= SIN_N; i++) SIN[i] = Math.sin(TAU * i / SIN_N);

  // RBJ 双二阶滤波系数
  function biq(type, f, sr, q, gainDb) {
    const w = TAU * Math.min(f, sr * 0.45) / sr, cw = Math.cos(w), sw = Math.sin(w);
    const al = sw / (2 * (q || 0.707));
    const A = Math.pow(10, (gainDb || 0) / 40);
    let b0, b1, b2, a0, a1, a2;
    switch (type) {
      case 'lp': b0 = (1 - cw) / 2; b1 = 1 - cw; b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
      case 'hp': b0 = (1 + cw) / 2; b1 = -(1 + cw); b2 = b0; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
      case 'bp': b0 = al; b1 = 0; b2 = -al; a0 = 1 + al; a1 = -2 * cw; a2 = 1 - al; break;
      default:   // peaking
        b0 = 1 + al * A; b1 = -2 * cw; b2 = 1 - al * A; a0 = 1 + al / A; a1 = -2 * cw; a2 = 1 - al / A;
    }
    return [b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0];
  }
  function filt(d, c) {
    let x1 = 0, x2 = 0, y1 = 0, y2 = 0;
    const [b0, b1, b2, a1, a2] = c;
    for (let i = 0; i < d.length; i++) {
      const x = d[i];
      const y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
      x2 = x1; x1 = x; y2 = y1; y1 = y;
      d[i] = y;
    }
    return d;
  }
  function dcBlock(d, sr, fc) {
    const R = Math.exp(-TAU * (fc || 25) / sr);
    let x1 = 0, y1 = 0;
    for (let i = 0; i < d.length; i++) { const y = d[i] - x1 + R * y1; x1 = d[i]; y1 = y; d[i] = y; }
    return d;
  }
  function normalize(d, peak) {
    let m = 0;
    for (let i = 0; i < d.length; i++) { const a = Math.abs(d[i]); if (a > m) m = a; }
    if (m > 1e-9) { const g = peak / m; for (let i = 0; i < d.length; i++) d[i] *= g; }
    return d;
  }
  function fadeOut(d, sr, sec) {
    const n = Math.min(d.length, Math.round(sr * sec));
    for (let i = 0; i < n; i++) d[d.length - 1 - i] *= i / n;
    return d;
  }
  function trimSilence(d, sr, thresh) {
    // 去掉尾部低于阈值的部分（节省内存）
    let last = d.length - 1;
    const th = thresh || 2e-4;
    while (last > sr * 0.05 && Math.abs(d[last]) < th) last--;
    const n = Math.min(d.length, last + Math.round(sr * 0.02));
    return n < d.length ? fadeOut(d.slice(0, n), sr, 0.02) : d;
  }

  // ===================================================== Karplus-Strong 拨弦 ==
  // o：t60（220Hz 时的衰减秒数）t60k（随音高缩放指数）damp（环路一阶低通系数，越大越暗）
  //    pick（拨弦位置 0..0.5）bright（激励噪声亮度 0..1）tri / noise（激励成分）click（指甲 / 拨片声）
  //    body（[[频率, dB, Q], ...] 琴体共鸣）courses（[[音分, 增益], ...] 多弦组）buzz（贾瓦里蜂鸣）dur
  function* ksRender(a, o) {
    const sr = a.sr, f = a.freq;
    const dur = Math.min(o.dur || 2, (o.durK ? o.durK * Math.pow(220 / f, 0.5) : 99));
    const N = Math.round(sr * Math.max(0.2, dur));
    const out = new Float32Array(N);
    const r = rng(a.seed);
    const courses = o.courses || [[0, 1]];
    const t60 = Math.max(0.15, Math.min(12, o.t60 * Math.pow(220 / f, o.t60k === undefined ? 0.5 : o.t60k)));
    for (let cI = 0; cI < courses.length; cI++) {
      const cf = f * Math.pow(2, courses[cI][0] / 1200);
      const P = sr / cf;
      const da = o.damp;
      const w = TAU * cf / sr;
      const pd = Math.atan2(da * Math.sin(w), 1 - da * Math.cos(w)) / w;
      const mag = (1 - da) / Math.sqrt(1 - 2 * da * Math.cos(w) + da * da);
      const total = P - pd;
      const L = Math.max(2, Math.floor(total - 0.5));
      const dl = total - L;
      const C = (1 - dl) / (1 + dl);
      const g = Math.min(0.99998, Math.pow(10, -3 / (t60 * cf)) / mag);
      const buf = new Float32Array(L);
      // 激励：拨弦位置的三角形 + 低通噪声（梳状滤波模拟拨弦位置）
      const pk = Math.max(1, Math.round((o.pick || 0.15) * L));
      const nz = new Float32Array(L);
      let lp = 0;
      const kb = o.bright === undefined ? 0.6 : o.bright;
      for (let i = 0; i < L; i++) { lp += (r() * 2 - 1 - lp) * kb; nz[i] = lp; }
      let mean = 0;
      for (let i = 0; i < L; i++) {
        const tri = i < pk ? i / pk : (L - i) / (L - pk);
        const nn = nz[i] - (i >= pk ? nz[i - pk] : 0);
        buf[i] = (o.tri === undefined ? 0.6 : o.tri) * tri + (o.noise === undefined ? 0.4 : o.noise) * nn;
        mean += buf[i];
      }
      mean /= L;
      let peak0 = 0;
      for (let i = 0; i < L; i++) { buf[i] -= mean; peak0 = Math.max(peak0, Math.abs(buf[i])); }
      const buzzTh = o.buzz ? o.buzz * peak0 : 0;
      const cg = courses[cI][1];
      let idx = 0, apx = 0, apy = 0, lpy = 0;
      for (let n = 0; n < N; n++) {
        const x = buf[idx];
        const ap = C * x + apx - C * apy; apx = x; apy = ap;
        lpy = (1 - da) * ap + da * lpy;
        let y = g * lpy;
        if (buzzTh && y > buzzTh) y = buzzTh + (y - buzzTh) * 0.12;
        buf[idx] = y;
        out[n] += x * cg;
        if (++idx === L) idx = 0;
        if ((n & 16383) === 16383) yield;
      }
    }
    // 拨片 / 指甲的瞬态
    if (o.click) {
      const cn = Math.round(sr * 0.006);
      const c = new Float32Array(cn);
      for (let i = 0; i < cn; i++) c[i] = (r() * 2 - 1) * Math.exp(-i / (cn * 0.25));
      filt(c, biq('hp', 2500, sr, 0.7));
      let pk = 0; for (let i = 0; i < 256 && i < N; i++) pk = Math.max(pk, Math.abs(out[i]));
      for (let i = 0; i < cn && i < N; i++) out[i] += c[i] * o.click * (pk || 1);
    }
    // 预加重：琴码处的受力 ≈ 弦位移的导数，高次谐波更明亮
    if (o.emph) { let x1 = 0; for (let i = 0; i < N; i++) { const x = out[i]; out[i] = x - o.emph * x1; x1 = x; } }
    if (o.body) for (const b of o.body) filt(out, biq('pk', b[0], sr, b[2], b[1]));
    if (o.lpf) filt(out, biq('lp', o.lpf, sr, 0.6));
    dcBlock(out, sr, o.hpf || 30);
    yield;
    normalize(out, 0.9);
    return trimSilence(fadeOut(out, sr, 0.05), sr);
  }

  // =========================================================== 模态合成 ==
  // partials：[[频率比, 振幅, t60, 起音秒?], ...]；o.glide = [比例, 时间常数]（音高滑移：正 = 由高落下）
  function* modalRender(a, o) {
    const sr = a.sr;
    const f0 = (a.freq || o.f0) * (o.fvar ? 1 + (rng(a.seed + 7)() - 0.5) * o.fvar : 1);
    const N = Math.round(sr * o.dur);
    const out = new Float32Array(N);
    const r = rng(a.seed);
    const gl = o.glide || null;
    const kg = gl ? Math.exp(-1 / (gl[1] * sr)) : 1;
    for (const p of o.partials) {
      const fr = f0 * p[0] * (o.jitter ? 1 + (r() - 0.5) * o.jitter : 1);
      if (fr >= sr * 0.48) continue;
      const amp = p[1] * (o.ampVar ? 1 + (r() - 0.5) * o.ampVar : 1);
      const k = Math.exp(-6.9 / (p[2] * sr));
      const att = Math.max(1, Math.round((p[3] || o.attack || 0.0005) * sr));
      let ph = r(), e = amp, gd = gl ? gl[0] : 0;
      const inc0 = fr / sr;
      for (let n = 0; n < N; n++) {
        const inc = gl ? inc0 * (1 + gd) : inc0;
        if (gl) gd *= kg;
        ph += inc; if (ph >= 1) ph -= 1;
        const x = ph * SIN_N, xi = x | 0;
        const s = SIN[xi] + (SIN[xi + 1] - SIN[xi]) * (x - xi);
        out[n] += s * e * (n < att ? n / att : 1);
        e *= k;
        if (e < 1e-5 && n > att) break;
      }
      yield;
    }
    // 击打噪声
    if (o.strike) {
      const s = o.strike;   // [振幅, 带通中心, Q, 衰减秒]
      const n = Math.min(N, Math.round(sr * s[3] * 6));
      const c = new Float32Array(n);
      for (let i = 0; i < n; i++) c[i] = (r() * 2 - 1) * Math.exp(-i / (sr * s[3]));
      filt(c, biq('bp', s[1], sr, s[2]));
      for (let i = 0; i < n; i++) out[i] += c[i] * s[0] * 4;
    }
    if (o.hpf) filt(out, biq('hp', o.hpf, sr, 0.7));
    if (o.lpf) filt(out, biq('lp', o.lpf, sr, 0.7));
    dcBlock(out, sr, 20);
    yield;
    normalize(out, o.norm || 0.9);
    return trimSilence(fadeOut(out, sr, 0.04), sr);
  }

  // =========================================================== 膜鸣鼓 ==
  // o：f0, bend（起始频率倍数）, tau（滑落时间常数）, t60, modes [[比, 振幅, t60]],
  //    skin [振幅, 中心, Q, 衰减]（鼓皮噪声）, click [振幅, 衰减], thump（低频噪声）, rise（音高上滑，塔布拉 ge）
  function* drumRender(a, o) {
    const sr = a.sr;
    const r = rng(a.seed);
    const vv = a.variant || 0;
    const f0 = (a.freq || o.f0) * (1 + (r() - 0.5) * (o.fvar === undefined ? 0.04 : o.fvar));
    const N = Math.round(sr * o.dur);
    const out = new Float32Array(N);
    const parts = [[1, 1, o.t60]].concat(o.modes || []);
    const kTau = Math.exp(-1 / ((o.tau || 0.03) * sr));
    const kRise = o.rise ? Math.exp(-1 / (o.rise[1] * sr)) : 1;
    for (const p of parts) {
      const k = Math.exp(-6.9 / (p[2] * sr));
      let e = p[1] * (1 + (r() - 0.5) * 0.12), ph = r() * 0.25, b = (o.bend || 1) - 1, rs = o.rise ? o.rise[0] : 0;
      const att = Math.round(sr * 0.0008);
      for (let n = 0; n < N; n++) {
        const fr = f0 * p[0] * (1 + b) * (1 + (o.rise ? o.rise[0] - rs : 0));
        b *= kTau; if (o.rise) rs *= kRise;
        ph += fr / sr; if (ph >= 1) ph -= 1;
        const x = ph * SIN_N, xi = x | 0;
        out[n] += (SIN[xi] + (SIN[xi + 1] - SIN[xi]) * (x - xi)) * e * (n < att ? n / att : 1);
        e *= k;
        if (e < 1e-5) break;
      }
      yield;
    }
    if (o.skin) {
      const s = o.skin;
      const n = Math.min(N, Math.round(sr * s[3] * 7));
      const c = new Float32Array(n);
      for (let i = 0; i < n; i++) c[i] = (r() * 2 - 1) * Math.exp(-i / (sr * s[3]));
      filt(c, biq('bp', s[1] * (1 + (r() - 0.5) * 0.1), sr, s[2]));
      for (let i = 0; i < n; i++) out[i] += c[i] * s[0] * 3;
    }
    if (o.thump) {
      const n = Math.min(N, Math.round(sr * 0.12));
      const c = new Float32Array(n);
      for (let i = 0; i < n; i++) c[i] = (r() * 2 - 1) * Math.exp(-i / (sr * 0.02));
      filt(c, biq('lp', 180, sr, 0.7)); filt(c, biq('lp', 180, sr, 0.7));
      for (let i = 0; i < n; i++) out[i] += c[i] * o.thump * 6;
    }
    if (o.click) {
      const n = Math.min(N, Math.round(sr * o.click[1] * 6));
      const c = new Float32Array(n);
      for (let i = 0; i < n; i++) c[i] = (r() * 2 - 1) * Math.exp(-i / (sr * o.click[1]));
      filt(c, biq('hp', o.click[2] || 2000, sr, 0.7));
      for (let i = 0; i < n; i++) out[i] += c[i] * o.click[0];
    }
    if (o.jingle) {
      // 铃片 / 铁片：高频金属噪声 + 少量随机分音
      const j = o.jingle;   // [振幅, 衰减秒]
      const n = Math.min(N, Math.round(sr * j[1] * 6));
      const c = new Float32Array(n);
      for (let i = 0; i < n; i++) {
        const t = i / sr;
        c[i] = (r() * 2 - 1) * Math.exp(-t / j[1]) * (0.6 + 0.4 * Math.sin(TAU * 31 * t + vv));
      }
      filt(c, biq('bp', 7200, sr, 1.4)); filt(c, biq('hp', 3500, sr, 0.7));
      for (let i = 0; i < n; i++) out[i] += c[i] * j[0] * 5;
    }
    if (o.lpf) filt(out, biq('lp', o.lpf, sr, 0.7));
    if (o.peaks) for (const b of o.peaks) filt(out, biq('pk', b[0], sr, b[2], b[1]));
    dcBlock(out, sr, 20);
    yield;
    normalize(out, 0.9);
    return trimSilence(fadeOut(out, sr, 0.02), sr);
  }

  // 定义采样型乐器的便捷函数
  function ks(name, o, extra) {
    return def(name, Object.assign({ type: 'sample', sr: o.sr || 32000, render: a => ksRender(a, o) }, extra));
  }
  function modal(name, o, extra) {
    return def(name, Object.assign({ type: 'sample', sr: o.sr || 32000, render: a => modalRender(a, o) }, extra));
  }
  // 多种击法（art）的打击乐器：arts = { art: 参数 }
  function kit(name, arts, extra) {
    return def(name, Object.assign({
      type: 'sample', sr: extra && extra.sr || 32000, variants: 3, pitchless: !(extra && extra.pitched),
      render: a => {
        const o = arts[a.art] || arts[0] || arts[Object.keys(arts)[0]];
        return o.modal ? modalRender(a, o) : drumRender(a, o);
      },
    }, extra));
  }

  // ================================================================ 拨弦 ==
  ks('zheng', { emph: 0.6, t60: 3.4, t60k: 0.45, damp: 0.1, pick: 0.12, bright: 0.72, tri: 0.55, noise: 0.5, click: 0.18,
    body: [[180, 4, 1.1], [420, 2.5, 1.4], [1250, 2, 1.2], [3300, 3, 1.4]], dur: 3.4 },
  { gain: 0.5, rev: 0.32, ring: 1.0, attackBend: 7, vibCents: 24, vibRate: 5.6, tremRate: 13, slideSemi: 2, velLp: [1800, 9000], label: '古筝' });

  ks('pipa', { emph: 0.7, t60: 1.25, t60k: 0.4, damp: 0.06, pick: 0.085, bright: 0.9, tri: 0.35, noise: 0.7, click: 0.3,
    body: [[270, 4, 1.4], [900, 2, 1.2], [2500, 4, 1.8]], dur: 2.0 },
  { gain: 0.46, rev: 0.24, ring: 0.3, attackBend: 6, tremRate: 17, strum: 0.012, vibCents: 18, slideSemi: 1.5, velLp: [2200, 9000], label: '琵琶' });

  ks('qin', { emph: 0.2, t60: 6, t60k: 0.35, damp: 0.36, pick: 0.17, bright: 0.35, tri: 0.85, noise: 0.2, click: 0.04,
    body: [[110, 4, 0.9], [320, 3, 1.1], [900, -2, 1]], dur: 4.5 },
  { gain: 0.62, rev: 0.4, ring: 2.0, vibCents: 18, vibRate: 4.2, slideSemi: 2, label: '古琴' });

  ks('ruan', { emph: 0.35, t60: 2.0, t60k: 0.4, damp: 0.22, pick: 0.2, bright: 0.5, tri: 0.7, noise: 0.35, click: 0.1,
    body: [[140, 4, 1], [600, 2, 1.1], [2000, -2, 1]], dur: 2.6 },
  { gain: 0.5, rev: 0.22, ring: 0.4, strum: 0.014, tremRate: 12, label: '中阮' });

  ks('yangqin', { emph: 0.5, t60: 2.8, t60k: 0.4, damp: 0.05, pick: 0.11, bright: 0.95, tri: 0.25, noise: 0.45, click: 0.35,
    courses: [[-2, 1], [0, 1], [2.5, 0.9]], body: [[320, 2, 1], [2100, 3, 1.4]], dur: 3 },
  { gain: 0.4, rev: 0.3, ring: 1.0, tremRate: 12, label: '扬琴' });

  ks('koto', { emph: 0.7, t60: 1.9, t60k: 0.45, damp: 0.09, pick: 0.075, bright: 0.85, tri: 0.45, noise: 0.6, click: 0.28,
    body: [[250, 3, 1.2], [1500, 4, 1.4], [3500, 2, 2]], dur: 2.6 },
  { gain: 0.5, rev: 0.32, ring: 0.6, attackBend: 10, vibCents: 26, vibRate: 5, slideSemi: 1, tremRate: 12, velLp: [2000, 9000], label: '筝（日本）' });

  ks('gayageum', { emph: 0.35, t60: 2.5, t60k: 0.5, damp: 0.3, pick: 0.2, bright: 0.48, tri: 0.8, noise: 0.3, click: 0.06,
    body: [[200, 4, 1], [700, 3, 1.2], [1800, 1, 1]], dur: 3.2 },
  { gain: 0.6, rev: 0.32, ring: 0.8, attackBend: 5, vibCents: 42, vibRate: 4.4, slideSemi: 1.5, label: '伽倻琴' });

  ks('harp', { emph: 0.3, t60: 2.3, t60k: 0.5, damp: 0.28, pick: 0.25, bright: 0.45, tri: 0.85, noise: 0.2, click: 0.05,
    body: [[220, 3, 1], [900, 2, 1]], dur: 3.2 },
  { gain: 0.55, rev: 0.36, ring: 1.2, strum: 0.03, label: '竖琴 / 里拉琴' });

  ks('oud', { emph: 0.45, t60: 0.95, t60k: 0.3, damp: 0.17, pick: 0.12, bright: 0.7, tri: 0.5, noise: 0.6, click: 0.3,
    courses: [[0, 1], [2.5, 0.8]], body: [[110, 5, 1], [240, 4, 1.2], [1100, 2, 1], [3000, -3, 1]], dur: 1.8 },
  { gain: 0.55, rev: 0.25, ring: 0.2, tremRate: 15, strum: 0.012, slideSemi: 1, vibCents: 16, label: '乌德琴' });

  ks('santur', { emph: 0.5, t60: 2.6, t60k: 0.4, damp: 0.05, pick: 0.1, bright: 0.95, tri: 0.2, noise: 0.45, click: 0.4,
    courses: [[-2, 1], [0, 1], [1.5, 1], [3, 0.9]], body: [[300, 2, 1], [2000, 3, 1.5]], dur: 3 },
  { gain: 0.38, rev: 0.32, ring: 1.0, tremRate: 12, label: '桑图尔' });

  ks('qanun', { emph: 0.55, t60: 1.7, t60k: 0.45, damp: 0.08, pick: 0.1, bright: 0.85, tri: 0.4, noise: 0.5, click: 0.3,
    courses: [[-1.5, 1], [0, 1], [1.5, 1]], body: [[250, 3, 1], [1200, 2, 1]], dur: 2.4 },
  { gain: 0.42, rev: 0.3, ring: 0.6, tremRate: 14, label: '卡龙琴' });

  ks('sitar', { emph: 0.55, t60: 3.2, t60k: 0.4, damp: 0.09, pick: 0.06, bright: 0.9, tri: 0.4, noise: 0.5, click: 0.25, buzz: 0.22,
    body: [[350, 3, 1], [1800, 4, 1.5], [4200, 2, 2]], dur: 3.2 },
  { gain: 0.42, rev: 0.32, ring: 1.0, slideSemi: 2, vibCents: 30, vibRate: 4.8, label: '西塔尔' });

  ks('tanpura', { emph: 0.4, t60: 6, t60k: 0.2, damp: 0.12, pick: 0.15, bright: 0.7, tri: 0.6, noise: 0.4, click: 0.05, buzz: 0.16,
    body: [[180, 3, 1], [1500, 3, 1.2]], dur: 5 },
  { gain: 0.42, rev: 0.38, ringAll: true, label: '坦布拉（持续音）' });

  ks('dombra', { emph: 0.6, t60: 0.75, t60k: 0.3, damp: 0.1, pick: 0.1, bright: 0.85, tri: 0.4, noise: 0.6, click: 0.3,
    body: [[180, 4, 1], [800, 3, 1.2], [2600, 2, 1.5]], dur: 1.3 },
  { gain: 0.48, rev: 0.22, ring: 0.15, strum: 0.01, tremRate: 14, label: '冬不拉 / 托布秀尔' });

  ks('pizz', { emph: 0.2, t60: 0.6, t60k: 0.25, damp: 0.3, pick: 0.2, bright: 0.45, tri: 0.8, noise: 0.25, click: 0.05,
    body: [[100, 4, 1], [400, 2, 1]], dur: 1.3, sr: 24000 },
  { gain: 0.62, rev: 0.25, ring: 0.12, sr: 24000, label: '弦乐拨奏 / 低音' });

  // ============================================================ 定音打击 ==
  // 编钟：椭圆截面、一钟双音（第二基音小三度上方）、非谐分音
  modal('bianzhong', { dur: 3.6, attack: 0.001, jitter: 0.004, partials: [
    [0.9985, 1, 2.8], [1.0015, 0.35, 2.4], [1.19, 0.16, 1.9], [2.42, 0.5, 1.3], [2.93, 0.24, 1.0], [4.16, 0.22, 0.6], [5.43, 0.12, 0.42], [6.8, 0.08, 0.3]],
    strike: [0.12, 2800, 1.2, 0.008] }, { gain: 0.42, rev: 0.45, ring: 2.5, label: '编钟' });
  // 定音鼓（现代管弦乐低音支撑）
  modal('timpani', { sr: 22050, dur: 2.6, attack: 0.002, glide: [0.03, 0.06], partials: [
    [1, 1, 2.2], [1.5, 0.45, 1.4], [1.99, 0.3, 1.0], [2.44, 0.15, 0.7], [2.9, 0.1, 0.5], [0.52, 0.18, 0.4]],
    strike: [0.35, 400, 0.8, 0.012] }, { gain: 0.6, rev: 0.3, ring: 2, label: '定音鼓' });
  // 甘美兰：萨隆（硬槌、成对微失谐产生 ombak 波动）、金德尔（软槌、共鸣管）、博南 / 克农（壶锣）
  modal('saron', { dur: 2.4, attack: 0.0008, partials: [
    [0.994, 1, 2.2], [1.006, 0.8, 2.2], [2.76, 0.28, 0.8], [2.79, 0.2, 0.8], [5.4, 0.1, 0.3]], strike: [0.25, 3500, 1, 0.006] },
  { gain: 0.42, rev: 0.36, ring: 1.6, label: '萨隆' });
  modal('gender', { dur: 3.6, attack: 0.003, partials: [
    [0.996, 1, 3.4], [1.004, 0.7, 3.4], [2.83, 0.12, 1.1], [4.1, 0.05, 0.5]], strike: [0.05, 1200, 1, 0.01] },
  { gain: 0.48, rev: 0.4, ring: 2.2, label: '金德尔' });
  modal('bonang', { dur: 1.9, attack: 0.001, partials: [
    [0.997, 1, 1.5], [1.003, 0.6, 1.5], [1.51, 0.25, 0.8], [2.02, 0.35, 0.9], [2.9, 0.2, 0.5], [3.95, 0.1, 0.3]], strike: [0.18, 2600, 1, 0.006] },
  { gain: 0.4, rev: 0.34, ring: 1.0, label: '博南' });
  modal('kenong', { dur: 3.2, attack: 0.002, partials: [
    [0.9975, 1, 2.6], [1.0025, 0.7, 2.6], [1.52, 0.2, 1.4], [2.03, 0.3, 1.2], [3.0, 0.12, 0.6]], strike: [0.1, 1800, 1, 0.008] },
  { gain: 0.42, rev: 0.4, ring: 2.0, label: '克农' });
  modal('kempul', { sr: 24000, dur: 4.5, attack: 0.004, partials: [
    [0.994, 1, 4.0], [1.006, 0.8, 4.0], [1.47, 0.35, 2.2], [2.08, 0.3, 1.8], [2.74, 0.2, 1.2], [3.6, 0.1, 0.8]], strike: [0.06, 900, 1, 0.01] },
  { gain: 0.5, rev: 0.4, ring: 3, label: '肯普尔' });
  modal('gongageng', { sr: 22050, dur: 7, attack: 0.02, partials: [
    [0.9925, 1, 7.5], [1.0075, 0.85, 7.5], [1.46, 0.3, 4.5], [2.02, 0.35, 4], [2.48, 0.18, 3], [2.95, 0.15, 2.4], [4.1, 0.08, 1.6]], strike: [0.03, 300, 1, 0.02] },
  { gain: 0.7, rev: 0.45, ring: 5, label: '甘美兰大锣' });
  // 塔布拉右鼓（达扬）定音的“纳”：近谐波分音
  modal('dayan', { dur: 1.1, attack: 0.0006, partials: [
    [1, 1, 0.9], [2, 0.6, 0.7], [3, 0.45, 0.55], [4, 0.28, 0.4], [5, 0.18, 0.3]], strike: [0.25, 3200, 1, 0.004] },
  { gain: 0.42, rev: 0.22, ring: 0.6, label: '塔布拉（右鼓）' });

  // ================================================================ 鼓 ==
  const WOOD = (f1, f2, d1) => ({ modal: true, dur: 0.22, attack: 0.0003, partials: [[1, 1, d1 || 0.06], [f2 / f1, 0.5, (d1 || 0.06) * 0.6], [3.9, 0.15, 0.02]], f0: f1, strike: [0.25, 4000, 1, 0.003], hpf: 300 });
  kit('dagu', {
    0: { f0: 60, bend: 1.7, tau: 0.035, t60: 1.1, modes: [[1.59, 0.3, 0.35], [2.14, 0.16, 0.25], [2.3, 0.12, 0.2]], skin: [0.3, 900, 0.8, 0.05], thump: 0.5, click: [0.1, 0.004, 2500], dur: 1.6 },
    1: WOOD(950, 1550, 0.05),
  }, { gain: 0.8, rev: 0.28, label: '大鼓' });
  kit('tanggu', {
    0: { f0: 150, bend: 1.4, tau: 0.03, t60: 0.5, modes: [[1.59, 0.35, 0.2], [2.14, 0.2, 0.15]], skin: [0.35, 1500, 0.9, 0.035], click: [0.15, 0.003, 3000], dur: 0.9 },
    1: WOOD(1300, 2300, 0.04),
  }, { gain: 0.6, rev: 0.25, label: '堂鼓' });
  kit('taiko', {
    0: { f0: 70, bend: 1.55, tau: 0.04, t60: 1.3, modes: [[1.59, 0.3, 0.4], [2.14, 0.18, 0.3], [2.65, 0.08, 0.2]], skin: [0.35, 700, 0.8, 0.06], thump: 0.6, click: [0.12, 0.004, 2000], dur: 1.8 },
    1: WOOD(1700, 2900, 0.04),
    2: { f0: 340, bend: 1.15, tau: 0.02, t60: 0.2, modes: [[1.59, 0.3, 0.1]], skin: [0.6, 2200, 1, 0.025], click: [0.2, 0.002, 3500], dur: 0.4 },
  }, { gain: 0.8, rev: 0.3, label: '太鼓' });
  kit('wardrum', {
    0: { f0: 52, bend: 1.8, tau: 0.045, t60: 1.2, modes: [[1.59, 0.25, 0.4], [2.14, 0.14, 0.3]], skin: [0.25, 600, 0.8, 0.07], thump: 0.7, dur: 1.8 },
    1: WOOD(800, 1350, 0.05),
  }, { gain: 0.8, rev: 0.3, label: '战鼓' });
  // 锣钹：京锣（音高下滑）、小锣（音高上扬）、抄锣（长鸣）、钹
  kit('luo', {
    0: { modal: true, dur: 3.6, attack: 0.001, glide: [0.09, 0.35], f0: 215, jitter: 0.01, partials: [
      [1, 1, 3.2], [1.42, 0.6, 2.6], [2.04, 0.4, 2.2], [2.6, 0.35, 1.6], [3.3, 0.25, 1.2], [4.1, 0.2, 0.9], [5.2, 0.15, 0.6], [6.5, 0.1, 0.5], [7.9, 0.07, 0.35]],
      strike: [0.5, 3000, 0.7, 0.03] },
  }, { gain: 0.5, rev: 0.36, variants: 2, label: '京锣' });
  kit('xiaoluo', {
    0: { modal: true, dur: 1.5, attack: 0.0008, glide: [-0.07, 0.12], f0: 760, jitter: 0.01, partials: [
      [1, 1, 1.1], [1.5, 0.3, 0.6], [2.3, 0.25, 0.4], [3.4, 0.12, 0.25]], strike: [0.25, 4000, 1, 0.01] },
  }, { gain: 0.36, rev: 0.3, pan: 0.3, variants: 2, label: '小锣' });
  (function () {
    // 抄锣：大量随机非谐分音，高频分音起音更慢（“涌起”），长鸣
    const pr = rng(4242), ps = [[0.5, 0.5, 6, 0.01]];
    for (let i = 0; i < 46; i++) { const fr = 1 + Math.pow(pr(), 1.6) * 34; ps.push([fr, 0.9 / Math.sqrt(fr), 6.5 - fr * 0.13, 0.02 + fr * 0.012]); }
    kit('tamtam', { 0: { modal: true, sr: 24000, dur: 6.5, f0: 110, partials: ps, jitter: 0.004, strike: [0.25, 600, 0.6, 0.03], hpf: 60 } },
      { gain: 0.55, rev: 0.5, sr: 24000, variants: 1, label: '抄锣' });
    const cp = [];
    for (let i = 0; i < 40; i++) { const fr = 1 + pr() * 2.6; cp.push([fr, 0.6 + pr() * 0.4, 0.7 + pr() * 1.0]); }
    kit('bo', {
      0: { modal: true, dur: 2.2, f0: 3100, partials: cp, jitter: 0.01, strike: [1.2, 5500, 0.5, 0.06], hpf: 900 },
      1: { modal: true, dur: 0.3, f0: 3100, partials: cp.map(p => [p[0], p[1], 0.12]), jitter: 0.01, strike: [1.2, 5000, 0.6, 0.03], hpf: 900 },
    }, { gain: 0.36, rev: 0.32, pan: -0.15, variants: 2, label: '钹' });
  })();
  kit('ban', { 0: WOOD(1250, 2900, 0.05), 1: WOOD(1050, 2500, 0.04) }, { gain: 0.36, rev: 0.22, pan: -0.25, label: '拍板' });
  kit('muyu', { 0: WOOD(700, 1650, 0.12), 1: WOOD(540, 1300, 0.12) }, { gain: 0.4, rev: 0.25, pan: 0.2, label: '木鱼' });
  kit('bangzi', { 0: WOOD(1850, 3600, 0.09) }, { gain: 0.32, rev: 0.22, pan: 0.3, label: '梆子' });
  // 达夫鼓（铁环铃片）：0 中心低音 dum，1 边缘 tak，2 无铃片
  kit('daf', {
    0: { f0: 72, bend: 1.3, tau: 0.03, t60: 0.65, modes: [[1.59, 0.25, 0.25]], skin: [0.3, 500, 0.8, 0.05], jingle: [0.12, 0.12], dur: 0.9 },
    1: { f0: 290, bend: 1.1, tau: 0.015, t60: 0.15, modes: [[1.59, 0.3, 0.08]], skin: [0.5, 1800, 1, 0.025], jingle: [0.3, 0.14], dur: 0.6 },
    2: { f0: 85, bend: 1.3, tau: 0.03, t60: 0.5, modes: [[1.59, 0.25, 0.2]], skin: [0.3, 700, 0.8, 0.04], dur: 0.7 },
  }, { gain: 0.6, rev: 0.25, label: '达夫鼓' });
  kit('darbuka', {
    0: { f0: 95, bend: 1.25, tau: 0.025, t60: 0.55, modes: [[1.59, 0.25, 0.2], [2.14, 0.12, 0.12]], skin: [0.2, 800, 0.8, 0.03], dur: 0.8 },
    1: { f0: 520, bend: 1.05, tau: 0.01, t60: 0.2, modes: [[1.59, 0.4, 0.12], [2.14, 0.2, 0.08]], skin: [0.45, 3000, 1, 0.012], click: [0.3, 0.002, 4000], dur: 0.4 },
    2: { f0: 470, bend: 1.05, tau: 0.01, t60: 0.12, modes: [[1.59, 0.3, 0.08]], skin: [0.35, 2600, 1, 0.01], dur: 0.3 },
  }, { gain: 0.55, rev: 0.22, label: '达布卡鼓' });
  // 塔布拉左鼓（巴扬）：0 ge（掌根推压，音高上扬）1 ke（闷击）
  kit('bayan', {
    0: { f0: 88, bend: 1.0, rise: [0.28, 0.12], t60: 0.75, modes: [[2.0, 0.12, 0.3]], skin: [0.15, 500, 0.8, 0.03], dur: 1.0 },
    1: { f0: 120, bend: 1.2, tau: 0.01, t60: 0.08, skin: [0.6, 900, 0.8, 0.02], dur: 0.25 },
  }, { gain: 0.6, rev: 0.2, label: '塔布拉（左鼓）' });
  // 朝鲜杖鼓：0 宫（鼓槌，低圆）1 德（细鞭，清脆）2 双面齐击
  kit('janggu', {
    0: { f0: 105, bend: 1.25, tau: 0.03, t60: 0.5, modes: [[1.59, 0.3, 0.2], [2.14, 0.15, 0.12]], skin: [0.25, 700, 0.8, 0.04], thump: 0.2, dur: 0.8 },
    1: { f0: 320, bend: 1.08, tau: 0.01, t60: 0.13, modes: [[1.59, 0.3, 0.08]], skin: [0.7, 2600, 1, 0.012], click: [0.35, 0.0015, 4000], dur: 0.35 },
    2: { f0: 110, bend: 1.25, tau: 0.03, t60: 0.45, modes: [[2.9, 0.3, 0.12]], skin: [0.6, 2200, 0.9, 0.02], click: [0.3, 0.002, 4000], thump: 0.2, dur: 0.8 },
  }, { gain: 0.55, rev: 0.25, label: '杖鼓' });
  kit('buk', {
    0: { f0: 84, bend: 1.45, tau: 0.035, t60: 0.65, modes: [[1.59, 0.3, 0.25], [2.14, 0.15, 0.2]], skin: [0.3, 800, 0.8, 0.05], thump: 0.45, dur: 1.0 },
    1: WOOD(1100, 1900, 0.04),
  }, { gain: 0.7, rev: 0.28, label: '北（朝鲜鼓）' });
  kit('jing', {
    0: { modal: true, sr: 24000, dur: 5.5, attack: 0.012, glide: [-0.012, 0.6], f0: 142, partials: [
      [0.994, 1, 5.2], [1.006, 0.8, 5.2], [1.5, 0.3, 3.4], [2.01, 0.35, 3], [2.47, 0.15, 2.2], [3.1, 0.1, 1.4], [4.2, 0.05, 0.9]], strike: [0.03, 400, 1, 0.02] },
  }, { gain: 0.6, rev: 0.45, sr: 24000, variants: 1, label: '钲（朝鲜大锣）' });
  kit('kkwaeng', {
    0: { modal: true, dur: 1.0, f0: 1180, partials: [[1, 1, 0.7], [1.44, 0.6, 0.5], [2.1, 0.5, 0.4], [2.9, 0.3, 0.3], [3.7, 0.2, 0.2]], strike: [0.4, 5000, 1, 0.004] },
    1: { modal: true, dur: 0.2, f0: 1180, partials: [[1, 1, 0.08], [1.44, 0.6, 0.06], [2.1, 0.5, 0.05], [2.9, 0.3, 0.04]], strike: [0.4, 5000, 1, 0.003] },
  }, { gain: 0.26, rev: 0.25, pan: 0.35, label: '小金' });
  // 肯当（甘美兰手鼓）：0 低音 dhung 1 拍击 tak 2 中音 thung
  kit('kendang', {
    0: { f0: 82, bend: 1.15, tau: 0.03, t60: 0.45, modes: [[1.59, 0.25, 0.2]], skin: [0.2, 600, 0.8, 0.03], dur: 0.7 },
    1: { f0: 460, bend: 1.05, tau: 0.01, t60: 0.08, skin: [0.7, 2400, 1, 0.012], click: [0.25, 0.002, 4000], dur: 0.25 },
    2: { f0: 210, bend: 1.08, tau: 0.02, t60: 0.3, modes: [[1.59, 0.3, 0.15]], skin: [0.3, 1200, 0.8, 0.02], dur: 0.5 },
  }, { gain: 0.55, rev: 0.22, label: '肯当鼓' });
  kit('frame', {
    0: { f0: 82, bend: 1.3, tau: 0.03, t60: 0.6, modes: [[1.59, 0.25, 0.22]], skin: [0.35, 650, 0.8, 0.045], dur: 0.9 },
    1: { f0: 260, bend: 1.08, tau: 0.012, t60: 0.14, skin: [0.55, 1700, 1, 0.02], dur: 0.4 },
  }, { gain: 0.6, rev: 0.26, label: '框鼓' });
  kit('bodhran', {
    0: { f0: 68, bend: 1.3, tau: 0.03, t60: 0.38, modes: [[1.59, 0.25, 0.16]], skin: [0.5, 600, 0.7, 0.04], thump: 0.25, dur: 0.6 },
    1: { f0: 92, bend: 1.2, tau: 0.02, t60: 0.25, modes: [[1.59, 0.25, 0.12]], skin: [0.5, 900, 0.7, 0.03], dur: 0.45 },
    2: WOOD(1200, 2100, 0.03),
  }, { gain: 0.62, rev: 0.24, label: '宝思兰鼓' });
  // 小军鼓（凯尔特风笛鼓队）：短促鼓皮 + 宽带响弦噪声；0 正击 1 轻击 / 装饰
  kit('snare', {
    0: { f0: 190, bend: 1.15, tau: 0.01, t60: 0.16, modes: [[1.59, 0.4, 0.09], [2.14, 0.25, 0.07]], skin: [0.9, 3600, 0.55, 0.055], click: [0.35, 0.002, 5000], dur: 0.45 },
    1: { f0: 190, bend: 1.1, tau: 0.008, t60: 0.07, skin: [0.65, 4200, 0.6, 0.03], click: [0.2, 0.0015, 5000], dur: 0.28 },
  }, { gain: 0.3, rev: 0.2, pan: 0.12, label: '小军鼓' });
  kit('shamandrum', {
    0: { f0: 58, bend: 1.25, tau: 0.04, t60: 0.95, modes: [[1.59, 0.25, 0.3], [2.14, 0.12, 0.2]], skin: [0.3, 500, 0.7, 0.05], jingle: [0.05, 0.22], thump: 0.35, dur: 1.3 },
    1: { f0: 160, bend: 1.1, tau: 0.02, t60: 0.2, skin: [0.4, 1200, 0.8, 0.03], jingle: [0.1, 0.18], dur: 0.6 },
  }, { gain: 0.66, rev: 0.32, label: '萨满鼓' });
  kit('hoof', {
    0: { modal: true, dur: 0.16, f0: 560, partials: [[1, 1, 0.05], [2.2, 0.4, 0.03], [0.22, 0.6, 0.05]], strike: [0.25, 1500, 0.8, 0.004], hpf: 80 },
  }, { gain: 0.36, rev: 0.18, pan: 0.15, variants: 4, label: '马蹄' });
  kit('crotala', {
    0: { modal: true, dur: 1.2, f0: 2300, partials: [[1, 1, 0.9], [2.4, 0.5, 0.6], [3.9, 0.3, 0.4], [5.2, 0.2, 0.3]], strike: [0.2, 6000, 1, 0.003] },
  }, { gain: 0.22, rev: 0.3, pan: 0.35, variants: 2, label: '指钹' });

  // ===================================================== 实时合成：通用件 ==
  // PeriodicWave（每个 AudioContext 缓存）
  function wave(ctx, name, harm, n) {
    const R = ctxRes(ctx);
    if (R.waves[name]) return R.waves[name];
    const N = n || 48;
    const re = new Float32Array(N + 1), im = new Float32Array(N + 1);
    for (let k = 1; k <= N; k++) im[k] = harm(k);
    const w = ctx.createPeriodicWave(re, im);
    R.waves[name] = w;
    return w;
  }

  function biquadNode(ctx, type, f, q, g) {
    const b = ctx.createBiquadFilter();
    b.type = type; b.frequency.value = f; b.Q.value = q === undefined ? 0.707 : q;
    if (g !== undefined) b.gain.value = g;
    return b;
  }

  // 自动化：按时间顺序追加，保证时间严格递增
  function Auto(param) { this.p = param; this.t = -1; }
  Auto.prototype.set = function (v, t) { t = Math.max(t, this.t + 1e-4, 0); this.p.setValueAtTime(v, t); this.t = t; };
  Auto.prototype.lin = function (v, t) { t = Math.max(t, this.t + 1e-4, 0); this.p.linearRampToValueAtTime(v, t); this.t = t; };
  Auto.prototype.exp = function (v, t) { t = Math.max(t, this.t + 1e-4, 0); this.p.exponentialRampToValueAtTime(Math.max(1e-4, v), t); this.t = t; };

  const SEMI = Math.pow(2, 1 / 12);

  // ------------------------------------------------- 连奏乐句（单音乐器） --
  // P：wave 名 + harm(k) 谐波振幅；level；attack；release；dip（换弓 / 吐音时的音量下陷）；
  //    port（普通连音滑动时长）；bigSlide（大跳 / 标 / 的滑音时长）；vibRate；vib（音分）；vibDeep；vibDelay；
  //    trem（与颤音同步的音量起伏）；noise {amp, f, q, track, chiff}；filters [[type, f, Q, gain]]；
  //    bright {base, vel, q}（随力度与音高的动态低通，铜管 / 双簧）；scoop（起音由低滑入的音分）；
  //    autoGrace（风笛：每次重新起音前自动加高音装饰）；detune2（第二振荡器，合奏 / 双管）
  function legato(P) {
    return function (tr, ch, notes, r) {
      const ctx = tr.ctx;
      const n0 = notes[0], nl = notes[notes.length - 1];
      const t0 = n0.t, tEnd = nl.t + nl.d;
      const rel = P.release || 0.15;
      const stopT = tEnd + rel * 3 + 0.08;
      const lvl = v => P.level * Math.pow(Math.min(1.2, v), P.velCurve || 1.25);
      const nodes = [];
      const osc = ctx.createOscillator();
      osc.setPeriodicWave(wave(ctx, P.wave, P.harm, P.nh));
      const amp = ctx.createGain(); amp.gain.value = 0;
      osc.connect(amp);
      let osc2 = null;
      if (P.detune2) {
        osc2 = ctx.createOscillator(); osc2.setPeriodicWave(wave(ctx, P.wave, P.harm, P.nh));
        osc2.detune.value = P.detune2;
        const g2 = ctx.createGain(); g2.gain.value = P.mix2 === undefined ? 0.7 : P.mix2;
        osc2.connect(g2); g2.connect(amp); nodes.push(g2);
      }
      const head = ctx.createGain();
      let tail = head;
      for (const F of (P.filters || [])) { const b = biquadNode(ctx, F[0], F[1], F[2], F[3]); tail.connect(b); tail = b; nodes.push(b); }
      let bright = null;
      if (P.bright) { bright = biquadNode(ctx, 'lowpass', 2000, P.bright.q || 0.8); tail.connect(bright); tail = bright; nodes.push(bright); }
      tail.connect(ch.input);
      amp.connect(head);
      nodes.push(amp, head);
      // 颤音 LFO（音分）
      const lfo = ctx.createOscillator();
      lfo.frequency.value = (P.vibRate || 5.5) * (0.93 + r() * 0.14);
      const vib = ctx.createGain(); vib.gain.value = 0;
      lfo.connect(vib); vib.connect(osc.detune); if (osc2) vib.connect(osc2.detune);
      nodes.push(vib);
      // 缓慢的音高漂移（人味）
      const drift = ctx.createOscillator(); drift.frequency.value = 0.25 + r() * 0.35;
      const dg = ctx.createGain(); dg.gain.value = P.drift === undefined ? 3 : P.drift;
      drift.connect(dg); dg.connect(osc.detune); if (osc2) dg.connect(osc2.detune);
      nodes.push(dg);
      let tremG = null;
      if (P.trem) { tremG = ctx.createGain(); tremG.gain.value = 0; lfo.connect(tremG); tremG.connect(amp.gain); nodes.push(tremG); }
      // 气声 / 弓噪
      let ns = null, nf = null, ng = null;
      if (P.noise) {
        const R = ctxRes(ctx);
        ns = ctx.createBufferSource(); ns.buffer = R.noise; ns.loop = true;
        nf = biquadNode(ctx, 'bandpass', P.noise.f || 2500, P.noise.q || 1);
        ng = ctx.createGain(); ng.gain.value = 0;
        ns.connect(nf); nf.connect(ng); ng.connect(P.noise.post ? ch.input : head);
        nodes.push(nf, ng);
      }
      const fA = new Auto(osc.frequency), aA = new Auto(amp.gain), vA = new Auto(vib.gain);
      const f2A = osc2 ? new Auto(osc2.frequency) : null;
      const nA = ng ? new Auto(ng.gain) : null;
      const nfA = (nf && P.noise.track) ? new Auto(nf.frequency) : null;
      const bA = bright ? new Auto(bright.frequency) : null;
      const tA = tremG ? new Auto(tremG.gain) : null;
      const F = (v, t, kind) => {
        if (kind === 'exp') { fA.exp(v, t); if (f2A) f2A.exp(v, t); if (nfA) nfA.exp(v * P.noise.track, t); }
        else { fA.set(v, t); if (f2A) f2A.set(v, t); if (nfA) nfA.set(v * P.noise.track, t); }
      };
      const att = P.attack || 0.08;
      const dip = P.dip === undefined ? 0.3 : P.dip;
      const nAmt = P.noise ? P.noise.amp : 0;
      const chiff = P.noise && P.noise.chiff ? P.noise.chiff : 0;
      const brightAt = (f, v, k) => Math.min(16000, f * (P.bright.base + P.bright.vel * v) * k + (P.bright.add || 0));
      let prevF = n0.f, prevL = 0;
      aA.set(0, t0 - 0.002); vA.set(0, t0 - 0.002);
      if (nA) nA.set(0, t0 - 0.002);
      if (tA) tA.set(0, t0 - 0.002);
      // 风笛装饰音：缺省为“1”上方 autoGrace 个半音；声部可用 grace（半音数）按本曲调高另设（如 1=D 时高音 G = 17）
      const gSemi = ch.V && ch.V.grace !== undefined ? ch.V.grace : P.autoGrace;
      const graceF = P.autoGrace ? (tr.C.piece._keyMidi !== undefined ? mtof(tr.C.piece._keyMidi + gSemi) : 0) : 0;
      for (let i = 0; i < notes.length; i++) {
        const n = notes[i], o = n.orn || {};
        const ti = n.t, d = Math.max(0.03, n.d);
        const L = lvl(n.v);
        const prev = i > 0 ? notes[i - 1] : null;
        const tied = prev && prev.orn && prev.orn.tie;
        // ---- 音高
        if (i === 0) {
          if (o.slide || o.gliss) { F(n.f / Math.pow(SEMI, o.gliss ? 5 : 2), ti); F(n.f / Math.pow(SEMI, o.gliss ? 5 : 2), ti + 0.02); F(n.f, ti + (o.gliss ? 0.32 : 0.16), 'exp'); }
          else if (n.gp !== undefined) { const fg = mtof(n.gp); F(fg, ti); F(fg, ti + 0.055); F(n.f, ti + 0.08, 'exp'); }
          else if (P.scoop) { F(n.f * Math.pow(2, -P.scoop / 1200), ti); F(n.f, ti + 0.07, 'exp'); }
          else F(n.f, ti);
        } else {
          const iv = Math.abs(Math.log2(n.f / prevF) * 12);
          if (P.autoGrace && !tied && graceF) {
            let gF = graceF; while (gF < n.f * 1.05) gF *= 2;
            F(prevF, ti - 0.04); F(gF, ti - 0.035); F(gF, ti - 0.004); F(n.f, ti);
          } else if (n.gp !== undefined) {
            const fg = mtof(n.gp);
            F(prevF, ti - 0.012); F(fg, ti); F(fg, ti + 0.05); F(n.f, ti + 0.075, 'exp');
          } else if (o.slide || o.gliss || iv >= 4.5) {
            const gt = o.gliss ? 0.32 : (o.slide ? (P.bigSlide || 0.14) : (P.bigSlide || 0.14) * 0.6);
            F(prevF, ti - gt * 0.35); F(n.f, ti + gt * 0.65, 'exp');
          } else if (iv > 0.05) {
            const gt = P.port || 0.045;
            F(prevF, ti - gt * 0.5); F(n.f, ti + gt * 0.5, 'exp');
          }
        }
        // 颤音 / 打音（trill）：在本音与上方邻音之间快速交替
        if (o.trem && n.tp !== undefined) {
          const ft = mtof(n.tp);
          const rate = P.trillRate || 9;
          let k = 0;
          for (let tt = ti + 0.06; tt < ti + d - 0.08; tt += 0.5 / rate, k++) {
            F(k % 2 ? n.f : ft, tt); F(k % 2 ? n.f : ft, tt + 0.5 / rate - 0.012);
          }
          F(n.f, ti + d - 0.06);
        }
        if (o.fall) { F(n.f, ti + d * 0.6); F(n.f / Math.pow(SEMI, 3), ti + d, 'exp'); }
        // ---- 音量
        if (i === 0) {
          const pk = o.acc ? L * 1.35 : L * 1.06;
          aA.lin(pk, ti + att);
          aA.lin(L, ti + att + 0.12);
        } else if (tied || P.autoGrace) {
          aA.lin(L, ti + 0.06);
        } else {
          aA.lin(prevL, ti - 0.025);
          aA.lin(prevL * (1 - dip), ti + 0.012);
          aA.lin(o.acc ? L * 1.3 : L, ti + Math.min(att, 0.07));
          if (o.acc) aA.lin(L, ti + 0.2);
        }
        let endL = L;
        if (o.swell) { aA.lin(L * 1.5, ti + d * 0.92); endL = L * 1.5; }
        else if (d > 1.0 && P.shape !== false) { aA.lin(L * 1.1, ti + d * 0.55); aA.lin(L * 0.94, ti + d - 0.03); endL = L * 0.94; }
        if (o.fall) { aA.lin(endL, ti + d * 0.6); aA.lin(endL * 0.25, ti + d); endL *= 0.25; }
        prevL = endL; prevF = o.fall ? n.f / Math.pow(SEMI, 3) : n.f;
        // ---- 颤音深度：长音才展开
        const depth = (o.vib ? (P.vibDeep || P.vib * 2) : P.vib) || 0;
        const vd = P.vibDelay === undefined ? 0.18 : P.vibDelay;
        vA.lin(depth * 0.15, ti + 0.02);
        if (d > vd + 0.12) { vA.lin(depth * 0.3, ti + vd); vA.lin(depth, ti + Math.min(d - 0.02, vd + 0.35)); }
        if (tA) { tA.lin(0, ti + 0.02); if (d > vd + 0.12) tA.lin(P.trem * L, ti + Math.min(d - 0.02, vd + 0.35)); }
        // ---- 气声：起音时更多（chiff）
        if (nA) {
          if (i === 0 || (!tied && !P.autoGrace)) { nA.lin(nAmt * L + chiff * L, ti + 0.012); nA.lin(nAmt * L, ti + 0.07); }
          else nA.lin(nAmt * L, ti + 0.05);
          if (d > 0.4) nA.lin(nAmt * L * (o.swell ? 1.3 : 0.9), ti + d - 0.02);
        }
        // ---- 动态亮度
        if (bA) {
          const fb = n.f;
          if (i === 0 || (!tied && !P.autoGrace)) {
            bA.set(Math.max(80, brightAt(fb, n.v, 0.45)), ti);
            bA.exp(brightAt(fb, n.v, o.acc ? 1.8 : 1.35), ti + Math.min(0.06, d * 0.5));
            bA.exp(brightAt(fb, n.v, 1.0), ti + Math.min(0.32, d * 0.9));
          } else bA.exp(brightAt(fb, n.v, 1.0), ti + 0.05);
          if (o.swell) bA.exp(brightAt(fb, n.v * 1.4, 1.2), ti + d * 0.9);
        }
      }
      // 收尾
      aA.lin(prevL, tEnd);
      aA.lin(0, tEnd + rel);
      if (nA) { nA.lin(0, tEnd + rel * 0.8); }
      vA.lin(0, tEnd + rel);
      const srcs = [osc, lfo, drift];
      if (osc2) srcs.push(osc2);
      const tS = Math.max(0, t0 - 0.002);
      if (ns) { srcs.push(ns); try { ns.start(tS, r() * 1.5); } catch (e) { ns.start(tS); } }
      for (const s of srcs) { if (s !== ns) s.start(tS); s.stop(stopT); }
      tr.track(osc, nodes);
      for (const s of srcs) if (s !== osc) tr.track(s, null);
    };
  }

  // 谐波形状
  const ripple = (k, a, s) => 1 + a * Math.sin(k * s + 0.7);
  const H = {
    bowed: k => Math.pow(k, -1.05) * ripple(k, 0.35, 1.7),
    bowedSoft: k => Math.pow(k, -1.35) * ripple(k, 0.3, 2.3),
    flute: k => [0, 1, 0.48, 0.3, 0.13, 0.09, 0.055, 0.04, 0.025, 0.018][k] || 0.01 / k,
    fluteBuzz: k => ([0, 1, 0.5, 0.32, 0.14, 0.1, 0.06, 0.045, 0.03, 0.022][k] || 0) + (k >= 6 && k <= 22 ? 0.028 / (1 + (k - 11) * (k - 11) / 30) : 0),
    pure: k => [0, 1, 0.16, 0.1, 0.04, 0.02][k] || 0,
    reed: k => Math.pow(k, -0.62) * ripple(k, 0.25, 2.9),
    reedNasal: k => Math.pow(k, -0.5) * (k % 2 ? 1 : 0.55),
    brass: k => Math.pow(k, -1.0),
    sheng: k => Math.pow(k, -0.78) * (k % 2 ? 1 : 1.12),
  };

  // ---------------------------------------------------------- 弓弦乐器 --
  def('erhu', { type: 'legato', gain: 1, rev: 0.32, label: '二胡', phrase: legato({
    wave: 'erhu', harm: H.bowed, level: 0.2, attack: 0.09, release: 0.16, dip: 0.28, port: 0.05, bigSlide: 0.16,
    vibRate: 6.1, vib: 16, vibDeep: 36, vibDelay: 0.16, drift: 4,
    noise: { amp: 0.09, f: 2600, q: 0.8, chiff: 0.25 },
    filters: [['highpass', 280, 0.7], ['peaking', 850, 1.3, 6], ['peaking', 2700, 2, 5], ['peaking', 1600, 3, -3], ['lowpass', 6500, 0.6]] }) });
  def('haegeum', { type: 'legato', gain: 1, rev: 0.3, label: '奚琴', phrase: legato({
    wave: 'haegeum', harm: k => Math.pow(k, -0.95) * ripple(k, 0.45, 2.1), level: 0.19, attack: 0.1, release: 0.18, dip: 0.3, port: 0.06, bigSlide: 0.2,
    vibRate: 4.6, vib: 22, vibDeep: 55, vibDelay: 0.25, drift: 6,
    noise: { amp: 0.14, f: 2200, q: 0.7, chiff: 0.3 },
    filters: [['highpass', 320, 0.7], ['peaking', 1100, 1.6, 7], ['peaking', 3000, 2, 4], ['lowpass', 6000, 0.6]] }) });
  def('kamancheh', { type: 'legato', gain: 1, rev: 0.32, label: '卡曼恰', phrase: legato({
    wave: 'kamancheh', harm: H.bowed, level: 0.2, attack: 0.1, release: 0.18, dip: 0.25, port: 0.05, bigSlide: 0.15,
    vibRate: 5.6, vib: 18, vibDeep: 34, vibDelay: 0.2,
    noise: { amp: 0.08, f: 2400, q: 0.8, chiff: 0.2 },
    filters: [['highpass', 220, 0.7], ['peaking', 700, 1.2, 5], ['peaking', 2200, 1.8, 4], ['lowpass', 5500, 0.6]] }) });
  def('morin', { type: 'legato', gain: 1, rev: 0.36, label: '马头琴', phrase: legato({
    wave: 'morin', harm: H.bowedSoft, level: 0.24, attack: 0.14, release: 0.25, dip: 0.22, port: 0.07, bigSlide: 0.2,
    vibRate: 5.0, vib: 18, vibDeep: 40, vibDelay: 0.25, drift: 5,
    noise: { amp: 0.12, f: 1800, q: 0.6, chiff: 0.3 },
    filters: [['highpass', 120, 0.7], ['peaking', 480, 1.2, 5], ['peaking', 1500, 1.5, 3], ['lowpass', 4200, 0.6]] }) });
  def('cello', { type: 'legato', gain: 1, rev: 0.36, label: '大提琴', phrase: legato({
    wave: 'cello', harm: k => Math.pow(k, -1.1) * ripple(k, 0.3, 1.3), level: 0.22, attack: 0.12, release: 0.22, dip: 0.2, port: 0.05, bigSlide: 0.12,
    vibRate: 5.4, vib: 14, vibDeep: 26, vibDelay: 0.2,
    noise: { amp: 0.05, f: 2000, q: 0.7, chiff: 0.15 },
    filters: [['highpass', 60, 0.7], ['peaking', 250, 1, 4], ['peaking', 1200, 1.4, 3], ['lowpass', 4000, 0.6]] }) });

  // ---------------------------------------------------------- 吹管乐器 --
  def('dizi', { type: 'legato', gain: 1, rev: 0.34, label: '笛子', phrase: legato({
    wave: 'dizi', harm: H.fluteBuzz, level: 0.17, attack: 0.04, release: 0.1, dip: 0.55, port: 0.03, bigSlide: 0.1,
    vibRate: 5.2, vib: 10, vibDeep: 24, vibDelay: 0.22, trem: 0.1, drift: 3, trillRate: 11,
    noise: { amp: 0.16, f: 1, q: 5, track: 2.0, chiff: 0.6 },
    filters: [['highpass', 400, 0.7], ['peaking', 3800, 1.5, 4], ['lowpass', 9000, 0.6]] }) });
  def('xiao', { type: 'legato', gain: 1, rev: 0.38, label: '箫', phrase: legato({
    wave: 'xiao', harm: H.pure, level: 0.24, attack: 0.09, release: 0.18, dip: 0.4, port: 0.05, bigSlide: 0.14,
    vibRate: 4.6, vib: 9, vibDeep: 22, vibDelay: 0.3, trem: 0.12, drift: 3,
    noise: { amp: 0.3, f: 1, q: 3.5, track: 1.0, chiff: 0.5 },
    filters: [['highpass', 200, 0.7], ['lowpass', 5000, 0.6]] }) });
  def('shakuhachi', { type: 'legato', gain: 1, rev: 0.4, label: '尺八', phrase: legato({
    wave: 'shaku', harm: k => [0, 1, 0.22, 0.12, 0.05, 0.025][k] || 0, level: 0.24, attack: 0.1, release: 0.2, dip: 0.5, port: 0.06, bigSlide: 0.22,
    vibRate: 4.2, vib: 6, vibDeep: 30, vibDelay: 0.45, trem: 0.16, drift: 6, scoop: 40,
    noise: { amp: 0.42, f: 1, q: 2.6, track: 1.0, chiff: 1.3 },
    filters: [['highpass', 220, 0.7], ['peaking', 1800, 1, 3], ['lowpass', 6000, 0.6]] }) });
  def('shinobue', { type: 'legato', gain: 1, rev: 0.34, label: '筱笛', phrase: legato({
    wave: 'shino', harm: H.flute, level: 0.15, attack: 0.03, release: 0.1, dip: 0.6, port: 0.03, bigSlide: 0.12,
    vibRate: 5.6, vib: 8, vibDeep: 26, vibDelay: 0.25, trem: 0.1, trillRate: 12,
    noise: { amp: 0.22, f: 1, q: 4, track: 2.0, chiff: 0.7 },
    filters: [['highpass', 600, 0.7], ['peaking', 4500, 1.2, 3], ['lowpass', 10000, 0.6]] }) });
  def('daegeum', { type: 'legato', gain: 1, rev: 0.4, label: '大笒', phrase: legato({
    wave: 'daegeum', harm: H.fluteBuzz, level: 0.2, attack: 0.1, release: 0.2, dip: 0.45, port: 0.06, bigSlide: 0.24,
    vibRate: 4.2, vib: 14, vibDeep: 48, vibDelay: 0.3, trem: 0.14, drift: 5, scoop: 30,
    noise: { amp: 0.28, f: 1, q: 3.5, track: 1.5, chiff: 0.8 },
    filters: [['highpass', 260, 0.7], ['peaking', 3000, 1.4, 4], ['lowpass', 7000, 0.6]] }) });
  def('ney', { type: 'legato', gain: 1, rev: 0.4, label: '奈伊笛', phrase: legato({
    wave: 'ney', harm: k => [0, 1, 0.3, 0.18, 0.08, 0.05, 0.03][k] || 0, level: 0.22, attack: 0.12, release: 0.2, dip: 0.45, port: 0.06, bigSlide: 0.2,
    vibRate: 5.0, vib: 10, vibDeep: 30, vibDelay: 0.3, trem: 0.14, drift: 4, scoop: 25,
    noise: { amp: 0.5, f: 1, q: 2.4, track: 1.0, chiff: 0.9 },
    filters: [['highpass', 240, 0.7], ['peaking', 1400, 1, 3], ['lowpass', 6000, 0.6]] }) });
  def('bansuri', { type: 'legato', gain: 1, rev: 0.38, label: '班苏里笛', phrase: legato({
    wave: 'bansuri', harm: k => [0, 1, 0.3, 0.16, 0.07, 0.04][k] || 0, level: 0.22, attack: 0.08, release: 0.18, dip: 0.45, port: 0.07, bigSlide: 0.22,
    vibRate: 5.2, vib: 8, vibDeep: 28, vibDelay: 0.3, trem: 0.12, drift: 3,
    noise: { amp: 0.32, f: 1, q: 3, track: 1.0, chiff: 0.6 },
    filters: [['highpass', 220, 0.7], ['lowpass', 6500, 0.6]] }) });
  def('suling', { type: 'legato', gain: 1, rev: 0.36, label: '苏林笛', phrase: legato({
    wave: 'suling', harm: H.flute, level: 0.18, attack: 0.05, release: 0.14, dip: 0.5, port: 0.04, bigSlide: 0.14,
    vibRate: 5.8, vib: 10, vibDeep: 26, vibDelay: 0.25, trem: 0.12,
    noise: { amp: 0.28, f: 1, q: 3.5, track: 1.0, chiff: 0.5 },
    filters: [['highpass', 300, 0.7], ['lowpass', 8000, 0.6]] }) });
  def('whistle', { type: 'legato', gain: 1, rev: 0.36, label: '哨笛', phrase: legato({
    wave: 'whistle', harm: k => [0, 1, 0.12, 0.06, 0.03][k] || 0, level: 0.2, attack: 0.04, release: 0.12, dip: 0.6, port: 0.025, bigSlide: 0.08,
    vibRate: 5.4, vib: 6, vibDeep: 18, vibDelay: 0.35, trem: 0.08, trillRate: 13,
    noise: { amp: 0.2, f: 1, q: 4, track: 1.0, chiff: 0.5 },
    filters: [['highpass', 300, 0.7], ['lowpass', 7000, 0.6]] }) });

  // 双簧 / 风笛
  def('suona', { type: 'legato', gain: 1, rev: 0.3, label: '唢呐', phrase: legato({
    wave: 'suona', harm: H.reed, level: 0.12, attack: 0.03, release: 0.09, dip: 0.45, port: 0.04, bigSlide: 0.14,
    vibRate: 6.4, vib: 14, vibDeep: 34, vibDelay: 0.15, drift: 4, trillRate: 10,
    noise: { amp: 0.05, f: 3000, q: 0.8, chiff: 0.2 },
    filters: [['highpass', 420, 0.7], ['peaking', 1300, 1.4, 7], ['peaking', 3100, 2, 5], ['lowpass', 7500, 0.6]],
    bright: { base: 3, vel: 6, q: 0.6, add: 600 } }) });
  def('piri', { type: 'legato', gain: 1, rev: 0.32, label: '觱篥', phrase: legato({
    wave: 'piri', harm: H.reedNasal, level: 0.13, attack: 0.05, release: 0.14, dip: 0.35, port: 0.06, bigSlide: 0.24,
    vibRate: 4.6, vib: 18, vibDeep: 55, vibDelay: 0.22, drift: 6, scoop: 50,
    noise: { amp: 0.06, f: 2500, q: 0.8, chiff: 0.2 },
    filters: [['highpass', 350, 0.7], ['peaking', 1000, 1.6, 6], ['peaking', 2500, 2, 4], ['lowpass', 6000, 0.6]],
    bright: { base: 2.5, vel: 4.5, q: 0.6, add: 500 } }) });
  def('aulos', { type: 'legato', gain: 1, rev: 0.34, label: '阿夫洛斯管', phrase: legato({
    wave: 'aulos', harm: H.reedNasal, level: 0.13, attack: 0.04, release: 0.12, dip: 0.35, port: 0.04, bigSlide: 0.12,
    vibRate: 5.4, vib: 8, vibDeep: 22, vibDelay: 0.3, detune2: 7, mix2: 0.5,
    noise: { amp: 0.06, f: 2600, q: 0.8, chiff: 0.25 },
    filters: [['highpass', 300, 0.7], ['peaking', 1200, 1.4, 5], ['lowpass', 5500, 0.6]],
    bright: { base: 2.5, vel: 4, q: 0.6, add: 400 } }) });
  def('zurna', { type: 'legato', gain: 1, rev: 0.3, label: '祖尔纳', phrase: legato({
    wave: 'zurna', harm: H.reed, level: 0.11, attack: 0.03, release: 0.09, dip: 0.4, port: 0.04, bigSlide: 0.14,
    vibRate: 6.6, vib: 12, vibDeep: 30, vibDelay: 0.15, trillRate: 11,
    noise: { amp: 0.05, f: 3200, q: 0.8, chiff: 0.2 },
    filters: [['highpass', 450, 0.7], ['peaking', 1500, 1.4, 7], ['peaking', 3400, 2, 5], ['lowpass', 8000, 0.6]],
    bright: { base: 3, vel: 6, q: 0.6, add: 700 } }) });
  def('chanter', { type: 'legato', gain: 1, rev: 0.3, label: '风笛（主管）', phrase: legato({
    wave: 'chanter', harm: k => Math.pow(k, -0.55) * ripple(k, 0.3, 2.2), level: 0.11, attack: 0.03, release: 0.06, dip: 0, port: 0.012,
    vibRate: 5, vib: 0, vibDeep: 0, drift: 1.5, autoGrace: 22, shape: false,
    noise: { amp: 0.03, f: 3000, q: 0.8 },
    filters: [['highpass', 500, 0.7], ['peaking', 1400, 1.2, 6], ['peaking', 3300, 2, 5], ['lowpass', 8500, 0.6]] }) });

  // 铜管
  def('cornu', { type: 'legato', gain: 1, rev: 0.4, label: '罗马号角', phrase: legato({
    wave: 'cornu', harm: H.brass, level: 0.2, attack: 0.06, release: 0.2, dip: 0.4, port: 0.05, bigSlide: 0.1,
    vibRate: 5, vib: 4, vibDeep: 14, vibDelay: 0.4, scoop: 45, detune2: 9, mix2: 0.6,
    filters: [['highpass', 90, 0.7], ['peaking', 900, 1, 3]],
    bright: { base: 2.2, vel: 5.5, q: 1.1, add: 200 } }) });
  def('horn', { type: 'legato', gain: 1, rev: 0.45, label: '圆号', phrase: legato({
    wave: 'horn', harm: H.brass, level: 0.22, attack: 0.1, release: 0.25, dip: 0.3, port: 0.06, bigSlide: 0.1,
    vibRate: 5, vib: 3, vibDeep: 10, vibDelay: 0.5, scoop: 20, detune2: 6, mix2: 0.8,
    filters: [['highpass', 70, 0.7], ['peaking', 500, 1, 3], ['lowpass', 3500, 0.5]],
    bright: { base: 1.6, vel: 3.6, q: 0.7, add: 150 } }) });

  // 呼麦泛音：固定的低音（声部 drone 参数）+ 极窄带通选出泛音，随旋律滑动
  def('overtone', { type: 'legato', gain: 1, rev: 0.4, label: '呼麦（泛音）', phrase: function (tr, ch, notes, r) {
    const ctx = tr.ctx;
    const n0 = notes[0], nl = notes[notes.length - 1];
    const t0 = n0.t, tEnd = nl.t + nl.d;
    const dp = ch.V.droneMidi !== undefined ? ch.V.droneMidi : (tr.C.piece._keyMidi - 24);
    const f0 = mtof(dp);
    // 带通中心对准最接近的泛音（真正的呼麦只能唱出持续音的泛音列）
    const harm = f => Math.max(2, Math.round(f / f0)) * f0;
    const h0 = harm(n0.f);
    const osc = ctx.createOscillator(); osc.type = 'sawtooth'; osc.frequency.value = f0;
    const bp = biquadNode(ctx, 'bandpass', h0, 28);
    const bp2 = biquadNode(ctx, 'bandpass', h0, 28);
    const g = ctx.createGain(); g.gain.value = 0;
    osc.connect(bp); bp.connect(bp2); bp2.connect(g); g.connect(ch.input);
    const fA = new Auto(bp.frequency), f2A = new Auto(bp2.frequency), gA = new Auto(g.gain);
    gA.set(0, t0); fA.set(h0, t0); f2A.set(h0, t0);
    let pf = h0;
    for (const n of notes) {
      const L = 2.4 * Math.pow(n.v, 1.2);
      const hf = harm(n.f);
      if (n !== n0) { fA.set(pf, n.t - 0.05); f2A.set(pf, n.t - 0.05); fA.exp(hf, n.t + 0.07); f2A.exp(hf, n.t + 0.07); }
      gA.lin(L, n.t + (n === n0 ? 0.25 : 0.08));
      gA.lin(L * 0.85, n.t + n.d - 0.02);
      pf = hf;
    }
    gA.lin(0, tEnd + 0.4);
    osc.start(t0); osc.stop(tEnd + 0.6);
    tr.track(osc, [bp, bp2, g]);
  } });

  // ------------------------------------------------------- 复音（垫音） --
  // P：oscs [[type|wave, 音分]], lp(f, v) 截止频率, q, attack, release, level, vib, formants, sub
  function poly(P) {
    return function (tr, ch, it, t, durS, vel, r) {
      const ctx = tr.ctx;
      const o = it.orn || {};
      const L = P.level * Math.pow(Math.min(1.2, vel), P.velCurve || 1.2) / Math.sqrt(Math.max(1, it.ps.length) * 0.7);
      const att = Math.min(P.attack, durS * 0.6);
      const rel = P.release;
      const end = t + durS;
      for (const p of it.ps) {
        const f = mtof(p);
        const g = ctx.createGain(); g.gain.value = 0;
        const lp = biquadNode(ctx, 'lowpass', P.lp(f, vel), P.q || 0.5);
        const nodes = [g, lp];
        let into = lp;
        if (P.formants) {
          const sum = ctx.createGain(); sum.gain.value = 1;
          for (const fm of P.formants) {
            const b = biquadNode(ctx, 'bandpass', fm[0], fm[0] / fm[2]);
            const bg = ctx.createGain(); bg.gain.value = fm[1];
            lp.connect(b); b.connect(bg); bg.connect(sum); nodes.push(b, bg);
          }
          into = sum; nodes.push(sum);
        }
        into.connect(g); g.connect(ch.input);
        const lfo = ctx.createOscillator(); lfo.frequency.value = (P.vibRate || 5) * (0.9 + r() * 0.2);
        const vg = ctx.createGain(); vg.gain.value = P.vib || 0;
        lfo.connect(vg); nodes.push(vg);
        const srcs = [lfo];
        for (const oc of P.oscs) {
          const os = ctx.createOscillator();
          if (typeof oc[0] === 'string') os.type = oc[0];
          else os.setPeriodicWave(wave(ctx, oc[0].name, oc[0].harm));
          os.frequency.value = f * (oc[2] || 1);
          os.detune.value = oc[1] + (r() - 0.5) * 4;
          vg.connect(os.detune);
          if (oc[3] !== undefined) { const og = ctx.createGain(); og.gain.value = oc[3]; os.connect(og); og.connect(lp); nodes.push(og); }
          else os.connect(lp);
          srcs.push(os);
        }
        // 弦乐震音（*）：约 13 Hz 的音量调制
        if (o.trem) {
          const tl = ctx.createOscillator(); tl.type = 'triangle'; tl.frequency.value = 12.5 + r() * 1.5;
          const tg = ctx.createGain(); tg.gain.value = 0.45;
          const am = ctx.createGain(); am.gain.value = 0.55;
          into.disconnect(); into.connect(am); am.connect(g);
          tl.connect(tg); tg.connect(am.gain);
          nodes.push(tg, am); srcs.push(tl);
        }
        const A = new Auto(g.gain);
        A.set(0, t);
        if (o.swell) { A.lin(L * 0.35, t + att); A.lin(L * 1.25, end - 0.05); }
        else { A.lin(L * (o.acc ? 1.3 : 1), t + att); if (o.acc) A.lin(L, t + att + 0.25); A.lin(L * (P.sustain || 0.92), end); }
        A.lin(0, end + rel);
        if (P.lpEnv) {
          const LA = new Auto(lp.frequency);
          LA.set(P.lp(f, vel) * 0.5, t); LA.exp(P.lp(f, vel) * (o.swell ? 1.4 : 1), t + att * 1.5 + 0.05);
        }
        const stopT = end + rel + 0.1;
        for (const s of srcs) { s.start(t); s.stop(stopT); }
        tr.track(srcs[1] || srcs[0], nodes);
        for (const s of srcs) if (s !== (srcs[1] || srcs[0])) tr.track(s, null);
      }
    };
  }

  const SAW = 'sawtooth';
  def('strings', { type: 'poly', gain: 1, rev: 0.42, label: '弦乐组', play: poly({
    oscs: [[SAW, -9], [SAW, 0], [SAW, 8]], lp: (f, v) => Math.min(6000, 900 + f * 2.2 + v * 1600), q: 0.5,
    attack: 0.45, release: 0.7, level: 0.075, vib: 7, vibRate: 5.2, lpEnv: true }) });
  def('stab', { type: 'poly', gain: 1, rev: 0.36, label: '弦乐顿弓', play: poly({
    oscs: [[SAW, -8], [SAW, 0], [SAW, 7]], lp: (f, v) => Math.min(7000, 1200 + f * 3 + v * 2500), q: 0.6,
    attack: 0.012, release: 0.16, level: 0.085, vib: 4, vibRate: 5.5, sustain: 0.55, lpEnv: true }) });
  def('lowstr', { type: 'poly', gain: 1, rev: 0.32, label: '低音弦乐', play: poly({
    oscs: [[SAW, -6], [SAW, 5], ['sine', 0, 1, 0.8]], lp: (f, v) => 380 + f * 2.5 + v * 500, q: 0.6,
    attack: 0.12, release: 0.35, level: 0.11, vib: 4, vibRate: 5 }) });
  def('sheng', { type: 'poly', gain: 1, rev: 0.36, label: '笙', play: poly({
    oscs: [[{ name: 'sheng', harm: H.sheng }, -4], [{ name: 'sheng', harm: H.sheng }, 4]], lp: (f, v) => Math.min(7000, f * 5 + 1200 + v * 1500), q: 0.7,
    attack: 0.07, release: 0.3, level: 0.06, vib: 3, vibRate: 5.6, sustain: 0.95 }) });
  def('choir', { type: 'poly', gain: 1, rev: 0.5, label: '合唱', play: poly({
    oscs: [[SAW, -11], [SAW, 0], [SAW, 10]], lp: () => 3600, q: 0.5,
    formants: [[650, 1.0, 80], [1080, 0.55, 90], [2650, 0.28, 120], [2900, 0.2, 130]],
    attack: 0.55, release: 0.9, level: 0.5, vib: 9, vibRate: 4.8 }) });
  def('choirOo', { type: 'poly', gain: 1, rev: 0.5, label: '合唱（u）', play: poly({
    oscs: [[SAW, -11], [SAW, 0], [SAW, 10]], lp: () => 2400, q: 0.5,
    formants: [[380, 1.0, 60], [820, 0.35, 80], [2500, 0.1, 120]],
    attack: 0.6, release: 1.0, level: 0.55, vib: 8, vibRate: 4.6 }) });
  def('horns', { type: 'poly', gain: 1, rev: 0.45, label: '圆号组', play: poly({
    oscs: [[SAW, -7], [SAW, 6]], lp: (f, v) => Math.min(4500, f * (1.6 + 3.4 * v) + 150), q: 0.7,
    attack: 0.14, release: 0.35, level: 0.1, vib: 3, lpEnv: true }) });
  def('drones', { type: 'poly', gain: 1, rev: 0.3, label: '持续低音（风笛 / 管）', play: poly({
    oscs: [[{ name: 'drone', harm: k => Math.pow(k, -0.7) * (k % 2 ? 1 : 0.8) }, -2], [{ name: 'drone', harm: k => Math.pow(k, -0.7) * (k % 2 ? 1 : 0.8) }, 3]],
    lp: (f) => Math.min(5000, f * 9), q: 0.8, attack: 0.4, release: 0.5, level: 0.045, vib: 0, sustain: 1 }) });
  def('kargyraa', { type: 'poly', gain: 1, rev: 0.42, label: '喉音低吟', play: poly({
    oscs: [[SAW, -4], [SAW, 4], [SAW, 0, 0.5, 0.6]], lp: () => 1400, q: 0.5,
    formants: [[420, 1.0, 60], [760, 0.6, 70], [2400, 0.15, 100]],
    attack: 0.7, release: 1.0, level: 0.5, vib: 4, vibRate: 3.2, sustain: 1 }) });
  def('sub', { type: 'poly', gain: 1, rev: 0.08, label: '低频', play: poly({
    oscs: [['sine', 0], ['triangle', 0, 1, 0.3]], lp: () => 400, q: 0.5,
    attack: 0.03, release: 0.15, level: 0.32, vib: 0 }) });

  // ------------------------------------------------------------ 混音校准 --
  // 由 tests/music.html 的试听片段测得（未经压缩的原始电平）：
  // 旋律（连奏）目标 RMS −23 dBFS，垫音 −26，拨弦峰值 −9，鼓峰值 −6，锣 −9。
  const CAL = { aulos: 3.3, ban: 0.44, bangzi: 0.41, bansuri: 0.67, bayan: 0.36, bianzhong: 0.38, bo: 0.41, bodhran: 0.26, bonang: 0.4, buk: 0.45, cello: 0.67, chanter: 3.7, choir: 0.56, choirOo: 0.3, cornu: 0.87, crotala: 0.35, daegeum: 0.84, daf: 0.43, dagu: 0.5, darbuka: 0.39, dayan: 0.5, dizi: 1.16, dombra: 0.44, drones: 3.05, erhu: 0.76, frame: 0.38, gayageum: 0.38, gender: 0.37, gongageng: 0.34, haegeum: 1.02, harp: 0.34, hoof: 0.44, horn: 0.7, horns: 0.84, janggu: 0.39, jing: 0.21, kamancheh: 0.71, kargyraa: 0.73, kempul: 0.39, kendang: 0.39, kenong: 0.38, kkwaeng: 0.43, koto: 0.43, lowstr: 0.61, luo: 0.33, morin: 0.79, muyu: 0.36, ney: 0.67, oud: 0.47, overtone: 0.77, pipa: 0.44, piri: 3.39, pizz: 0.41, qanun: 0.52, qin: 0.38, ruan: 0.43, santur: 0.51, saron: 0.35, shakuhachi: 0.58, shamandrum: 0.37, sheng: 2.01, shinobue: 1.97, sitar: 0.5, strings: 0.85, sub: 0.21, suling: 1.0, suona: 2.88, taiko: 0.41, tamtam: 0.23, tanggu: 0.4, tanpura: 0.48, timpani: 0.62, wardrum: 0.39, whistle: 0.77, xiao: 0.58, xiaoluo: 0.35, yangqin: 0.48, zheng: 0.46, zurna: 3.25 };
  for (const k in CAL) if (MU.INST[k]) MU.INST[k].gain = CAL[k];
})();
