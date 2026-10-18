'use strict';
// 程序化“人声式”短喝（无 TTS 时的兜底）：声门脉冲 + 共振峰滤波（Klatt 式级联 / 并联简化版）
// 输入：音段数组 [{ p:'a', d:0.12 }, ...] 与音高轮廓；输出 Float32Array（22050 Hz）
const SR = 22050, TAU = 2 * Math.PI;
// 元音共振峰 F1..F4（Hz，成年男性）与带宽
const V = {
  a: [730, 1090, 2440, 3400], e: [530, 1840, 2480, 3500], i: [270, 2290, 3010, 3700],
  o: [570, 840, 2410, 3400], u: [300, 870, 2240, 3400], 'ə': [500, 1500, 2500, 3500], 'ɯ': [340, 1250, 2400, 3400],
  // 浊辅音的近似（鼻音 / 边音 / 近音）
  m: [250, 1100, 2200, 3300], n: [250, 1700, 2600, 3500], 'ŋ': [250, 2000, 2700, 3500], l: [360, 1300, 2700, 3500], r: [420, 1300, 1600, 3300], w: [300, 610, 2200, 3300], j: [260, 2200, 3000, 3700],
};
const BW = [80, 90, 120, 180];
// 清辅音：噪声带通中心与带宽、是否带爆破
const C = { s: [6000, 3000, 0], 'ʃ': [3000, 2000, 0], 'ʂ': [2600, 1800, 0], h: [1500, 3000, 0], f: [7000, 4000, 0], x: [1800, 1500, 0],
  p: [900, 1200, 1], t: [3500, 2500, 1], k: [1800, 1500, 1], 'ʈʂ': [2600, 1800, 1], 'ts': [5500, 3000, 1], b: [700, 1000, 1], d: [3000, 2500, 1], g: [1500, 1500, 1] };
function rng(seed) { let a = seed | 0; return () => { a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
// 二阶谐振器（Klatt）
function reso() { return { a: 0, b: 0, c: 0, y1: 0, y2: 0 }; }
function setReso(r, f, bw) { const C = -Math.exp(-TAU * bw / SR), B = 2 * Math.exp(-Math.PI * bw / SR) * Math.cos(TAU * f / SR); r.c = C; r.b = B; r.a = 1 - B - C; }
function runReso(r, x) { const y = r.a * x + r.b * r.y1 + r.c * r.y2; r.y2 = r.y1; r.y1 = y; return y; }
// voice = { f0: 基频, sex: 'm'|'f', age: 年龄, rough: 0..1 粗糙度, shout: 0..1 }
function bark(segs, voice, seed) {
  voice = voice || {};
  const fs = voice.sex === 'f' ? 1.17 : 1, age = voice.age || 35;
  const f0 = voice.f0 || (voice.sex === 'f' ? 210 : 120) * (age > 55 ? 0.9 : 1);
  const shout = voice.shout || 0, rough = voice.rough || (age > 55 ? 0.35 : 0.1);
  const R = rng(seed || 1);
  const total = segs.reduce((s, x) => s + x.d, 0), n = Math.ceil((total + 0.12) * SR);
  const out = new Float32Array(n);
  const rs = [reso(), reso(), reso(), reso()], nz = reso();
  let phase = 0, t0 = 0, i = 0, cur = V['ə'].slice();
  for (let si = 0; si < segs.length; si++) {
    const sg = segs[si], len = Math.round(sg.d * SR);
    const tgt = (V[sg.p] || cur).map((f, k) => f * fs * (k < 2 ? 1 + shout * 0.12 : 1));
    const cons = C[sg.p];
    for (let j = 0; j < len && i < n; j++, i++) {
      const u = (i / SR) / total;
      // 音高：喊叫型轮廓（起 → 峰 → 落），加抖动与颤动
      const contour = 1 + shout * 0.35 * Math.sin(Math.PI * Math.min(1, u * 1.4)) - 0.18 * Math.max(0, u - 0.7);
      const jit = 1 + (R() - 0.5) * 0.02 * (1 + rough * 3) + 0.012 * Math.sin(TAU * 5.5 * i / SR) * (age > 55 ? 2 : 0.5);
      const f = f0 * (1 + shout * 0.5) * contour * jit;
      // 共振峰平滑过渡（约 25 ms）
      for (let k = 0; k < 4; k++) cur[k] += (tgt[k] - cur[k]) * 0.004;
      if ((i & 31) === 0) for (let k = 0; k < 4; k++) setReso(rs[k], cur[k], BW[k] * (1 + shout * 0.5));
      phase += f / SR; if (phase >= 1) phase -= 1;
      // Rosenberg 声门脉冲（开相 60%）+ 送气噪声
      const op = 0.6 - shout * 0.15;
      let g = phase < op ? 0.5 * (1 - Math.cos(Math.PI * phase / op)) : Math.max(0, Math.cos(Math.PI * (phase - op) / (2 * (1 - op))));
      g = g - 0.5;
      let voiced = cons ? 0 : 1;
      if (cons && cons[2] && j < len * 0.35) voiced = 0;
      const asp = (R() * 2 - 1) * (0.06 + rough * 0.2 + shout * 0.1);
      let x = (g * voiced + asp * (voiced ? 0.5 : 0));
      let y = 0;
      for (let k = 0; k < 4; k++) { x = runReso(rs[k], x); }  // 级联
      y = x * 0.06;
      if (cons) {   // 清辅音：带通噪声；爆破：开头 8 ms 的强脉冲
        setReso(nz, cons[0], cons[1]);
        const burst = cons[2] && j < SR * 0.01 ? 3 : 1;
        y += runReso(nz, (R() * 2 - 1)) * 0.25 * burst * Math.min(1, j / (SR * 0.004));
      }
      // 段内包络：起止 10 ms 斜坡
      const e = Math.min(1, j / (SR * 0.01), (len - j) / (SR * 0.012) + 0.3);
      out[i] = y * e;
    }
  }
  let pk = 0; for (const v of out) pk = Math.max(pk, Math.abs(v));
  if (pk > 0) for (let k = 0; k < n; k++) out[k] = Math.tanh(out[k] / pk * (1.2 + shout)) * 0.85;
  return out;
}
module.exports = { bark, SR };
if (require.main === module) {
  const fs = require('fs');
  const wav = (fn, d) => { const b = Buffer.alloc(44 + d.length * 2); b.write('RIFF', 0); b.writeUInt32LE(36 + d.length * 2, 4); b.write('WAVEfmt ', 8); b.writeUInt32LE(16, 16); b.writeUInt16LE(1, 20); b.writeUInt16LE(1, 22); b.writeUInt32LE(SR, 24); b.writeUInt32LE(SR * 2, 28); b.writeUInt16LE(2, 32); b.writeUInt16LE(16, 34); b.write('data', 36); b.writeUInt32LE(d.length * 2, 40); for (let i = 0; i < d.length; i++) b.writeInt16LE(Math.round(d[i] * 32767), 44 + i * 2); fs.writeFileSync(fn, b); };
  const out = process.argv[2];
  const tests = {
    chong_a: [[{ p: 'ʈʂ', d: 0.09 }, { p: 'u', d: 0.12 }, { p: 'ŋ', d: 0.1 }, { p: 'a', d: 0.38 }], { shout: 1 }],
    zun_ming: [[{ p: 'ts', d: 0.08 }, { p: 'u', d: 0.1 }, { p: 'ə', d: 0.08 }, { p: 'n', d: 0.07 }, { p: 'm', d: 0.06 }, { p: 'i', d: 0.16 }, { p: 'ŋ', d: 0.14 }], { shout: 0.2 }],
    ad_arma: [[{ p: 'a', d: 0.12 }, { p: 'd', d: 0.06 }, { p: 'a', d: 0.14 }, { p: 'r', d: 0.07 }, { p: 'm', d: 0.08 }, { p: 'a', d: 0.3 }], { shout: 0.8 }],
    old_hm: [[{ p: 'h', d: 0.08 }, { p: 'ə', d: 0.12 }, { p: 'm', d: 0.3 }], { age: 70 }],
    fem_ha: [[{ p: 'h', d: 0.07 }, { p: 'a', d: 0.35 }], { sex: 'f', shout: 0.6 }],
  };
  for (const k in tests) {
    const t0 = process.hrtime.bigint();
    let d; for (let r = 0; r < 20; r++) d = bark(tests[k][0], tests[k][1], 7);
    const ms = Number(process.hrtime.bigint() - t0) / 1e6 / 20;
    wav(out + '/' + k + '.wav', d);
    console.log(JSON.stringify({ id: k, dur: +(d.length / SR).toFixed(3), render_ms: +ms.toFixed(2), rt_factor: +(ms / 1000 / (d.length / SR)).toFixed(4) }));
  }
}
