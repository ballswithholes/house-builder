// 比对网页版（browser.mjs）与 C#（audio.exe render / pieces）渲染的 WAV：
//   峰值、RMS、倍频程频带能量（63 Hz – 16 kHz）、波形相关系数（允许 ±3 ms 平移），并输出上下并排的声谱图 PNG。
//   node compare.mjs <js 目录> <cs 目录> [png 目录] [--tol dB]
// 退出码：频带最大差超过容差（缺省 3 dB）或 RMS 差超过 1.5 dB 的曲目数。
import fs from 'fs';
import path from 'path';
import zlib from 'zlib';

const [jsDir, csDir, pngDir0] = process.argv.slice(2);
const ti = process.argv.indexOf('--tol');
const TOL = ti > 0 ? +process.argv[ti + 1] : 3;
const pngDir = pngDir0 && !pngDir0.startsWith('--') ? pngDir0 : null;
if (pngDir) fs.mkdirSync(pngDir, { recursive: true });

function readWav(f) {
  const b = fs.readFileSync(f);
  let o = 12, sr = 44100, ch = 2, data = null;
  while (o < b.length) {
    const id = b.toString('ascii', o, o + 4), len = b.readUInt32LE(o + 4);
    if (id === 'fmt ') { ch = b.readUInt16LE(o + 10); sr = b.readUInt32LE(o + 12); }
    if (id === 'data') { data = b.subarray(o + 8, o + 8 + len); break; }
    o += 8 + len;
  }
  const n = data.length / (2 * ch), L = new Float32Array(n), R = new Float32Array(n);
  for (let i = 0; i < n; i++) { L[i] = data.readInt16LE(i * 2 * ch) / 32767; R[i] = data.readInt16LE(i * 2 * ch + (ch > 1 ? 2 : 0)) / 32767; }
  return { sr, L, R, n };
}
function fft(re, im) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) { let bit = n >> 1; for (; j & bit; bit >>= 1) j ^= bit; j ^= bit; if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; } }
  for (let len = 2; len <= n; len <<= 1) {
    const ang = -2 * Math.PI / len, wr = Math.cos(ang), wi = Math.sin(ang);
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < len / 2; k++) {
        const a = i + k, b = a + len / 2, xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
        const nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
      }
    }
  }
}
const BANDS = [63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
function analyze(w) {
  const { L, R, n, sr } = w;
  let peak = 0, s2 = 0;
  for (let i = 0; i < n; i++) { const a = Math.max(Math.abs(L[i]), Math.abs(R[i])); if (a > peak) peak = a; s2 += L[i] * L[i] + R[i] * R[i]; }
  const rms = Math.sqrt(s2 / (2 * n));
  // 倍频程频带能量（Hann 4096，hop 2048，单声道）
  const N = 4096, re = new Float64Array(N), im = new Float64Array(N), band = new Float64Array(BANDS.length);
  for (let s = 0; s + N <= n; s += N / 2) {
    for (let i = 0; i < N; i++) { const h = 0.5 - 0.5 * Math.cos(2 * Math.PI * i / N); re[i] = (L[s + i] + R[s + i]) * 0.5 * h; im[i] = 0; }
    fft(re, im);
    for (let k = 1; k < N / 2; k++) {
      const f = k * sr / N, p = re[k] * re[k] + im[k] * im[k];
      for (let b = 0; b < BANDS.length; b++) if (f >= BANDS[b] / Math.SQRT2 && f < BANDS[b] * Math.SQRT2) band[b] += p;
    }
  }
  return { peak, rms, band: Array.from(band, p => 10 * Math.log10(p + 1e-12)) };
}
function corr(a, b, maxLag) {
  const n = Math.min(a.L.length, b.L.length);
  const x = new Float32Array(n), y = new Float32Array(n);
  for (let i = 0; i < n; i++) { x[i] = a.L[i] + a.R[i]; y[i] = b.L[i] + b.R[i]; }
  let best = -2, lag = 0;
  for (let d = -maxLag; d <= maxLag; d += 1) {
    let sxy = 0, sxx = 0, syy = 0;
    for (let i = Math.max(0, -d); i < n && i + d < n; i += 4) { const u = x[i], v = y[i + d]; sxy += u * v; sxx += u * u; syy += v * v; }
    const c = sxy / Math.sqrt(sxx * syy + 1e-20);
    if (c > best) { best = c; lag = d; }
  }
  return { c: best, lag };
}
// 声谱图（对数频率 35 Hz – 16 kHz，+4 dB/倍频程倾斜，色阶按两者合并的 99.5 百分位）
function spectro(w, W, H) {
  const { L, R, n, sr } = w, N = 2048, re = new Float64Array(N), im = new Float64Array(N);
  const fLo = 35, fHi = Math.min(16000, sr / 2), db = new Float32Array(W * H);
  for (let x = 0; x < W; x++) {
    const c = Math.floor((x + 0.5) * n / W) - N / 2;
    for (let i = 0; i < N; i++) { const k = c + i; re[i] = k >= 0 && k < n ? (L[k] + R[k]) * 0.5 * (0.5 - 0.5 * Math.cos(2 * Math.PI * i / N)) : 0; im[i] = 0; }
    fft(re, im);
    for (let y = 0; y < H; y++) {
      const f = fLo * Math.pow(fHi / fLo, 1 - y / (H - 1)), bin = f * N / sr;
      const b0 = Math.floor(bin), b1 = Math.min(N / 2 - 1, Math.ceil(bin * Math.pow(fHi / fLo, 1 / H)));
      let m = 0;
      for (let b = b0; b <= Math.max(b0, b1); b++) { const p = re[b] * re[b] + im[b] * im[b]; if (p > m) m = p; }
      db[y * W + x] = 10 * Math.log10(m + 1e-12) + 4 * Math.log2(Math.max(20, f) / 1000);
    }
  }
  return db;
}
const RAMP = [[0, 0, 4], [40, 11, 84], [101, 21, 110], [159, 42, 99], [212, 72, 66], [245, 125, 21], [250, 193, 39], [252, 255, 164]];
function color(v) { const x = Math.max(0, Math.min(1, v)) * (RAMP.length - 1), i = Math.min(RAMP.length - 2, Math.floor(x)), f = x - i; return [0, 1, 2].map(k => Math.round(RAMP[i][k] + (RAMP[i + 1][k] - RAMP[i][k]) * f)); }
function png(file, W, H, rgb) {
  const raw = Buffer.alloc((W * 3 + 1) * H);
  for (let y = 0; y < H; y++) { raw[y * (W * 3 + 1)] = 0; rgb.copy(raw, y * (W * 3 + 1) + 1, y * W * 3, (y + 1) * W * 3); }
  const crcT = new Int32Array(256).map((_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c; });
  const crc = b => { let c = -1; for (const x of b) c = crcT[(c ^ x) & 255] ^ (c >>> 8); return (c ^ -1) >>> 0; };
  const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32LE(0); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(W, 0); ihdr.writeUInt32BE(H, 4); ihdr[8] = 8; ihdr[9] = 2;
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', zlib.deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]));
}
function pair(file, a, b) {
  const W = 640, H = 150, GAP = 4;
  const sa = spectro(a, W, H), sb = spectro(b, W, H);
  const all = Float32Array.from([...sa, ...sb]).sort();
  const top = all[Math.floor(all.length * 0.995)];
  const rgb = Buffer.alloc(W * (2 * H + GAP) * 3, 40);
  [sa, sb].forEach((s, j) => { for (let i = 0; i < W * H; i++) { const c = color((s[i] - top + 60) / 60), o = (j * (H + GAP) * W + i) * 3; rgb[o] = c[0]; rgb[o + 1] = c[1]; rgb[o + 2] = c[2]; } });
  png(file, W, 2 * H + GAP, rgb);
}

const files = fs.readdirSync(jsDir).filter(f => f.endsWith('.wav') && fs.existsSync(path.join(csDir, f))).sort();
let bad = 0;
const rows = [];
console.log('曲目                       峰值 JS/C#      RMS dB JS/C#   Δ频带最大(dB)  相关(平移)   各频带 Δ dB（63…16k）');
for (const f of files) {
  const a = readWav(path.join(jsDir, f)), b = readWav(path.join(csDir, f));
  const A = analyze(a), B = analyze(b);
  const dB = A.band.map((v, i) => B.band[i] - v);
  // 频带能量很低（比最强频带低 45 dB 以上）的频带不计入最大差
  const ref = Math.max(...A.band);
  const dmax = Math.max(...dB.map((d, i) => A.band[i] > ref - 45 ? Math.abs(d) : 0));
  const dr = 20 * Math.log10(B.rms / A.rms);
  const cr = corr(a, b, 400);
  const ok = dmax <= TOL && Math.abs(dr) <= 1.5;
  if (!ok) bad++;
  rows.push({ key: f.replace(/\.wav$/, ''), jsPeak: A.peak, csPeak: B.peak, jsRms: A.rms, csRms: B.rms, dRmsDb: dr, dBand: dB, corr: cr.c, lag: cr.lag, ok });
  console.log(`${(ok ? '  ' : '✗ ') + f.replace(/\.wav$/, '').padEnd(25)} ${A.peak.toFixed(3)}/${B.peak.toFixed(3)}   ${(20 * Math.log10(A.rms)).toFixed(1).padStart(6)}/${(20 * Math.log10(B.rms)).toFixed(1).padStart(6)}   ${dmax.toFixed(2).padStart(6)}       ${cr.c.toFixed(3)} (${cr.lag})   ${dB.map(d => (d >= 0 ? '+' : '') + d.toFixed(1)).join(' ')}`);
  if (pngDir) pair(path.join(pngDir, f.replace(/\.wav$/, '.png')), a, b);
}
fs.writeFileSync(path.join(csDir, 'compare.json'), JSON.stringify(rows, null, 1));
console.log(`${bad ? '失败' : '通过'}：${files.length} 首比对，${bad} 首超出容差（频带 ±${TOL} dB、RMS ±1.5 dB）${pngDir ? '；声谱图（上 JS / 下 C#）→ ' + pngDir : ''}`);
process.exit(bad);
