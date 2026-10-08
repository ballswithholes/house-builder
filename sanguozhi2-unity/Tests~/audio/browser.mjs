// 网页版离线渲染（tests/music.html 的 window.__renderPcm，OfflineAudioContext）→ WAV，供 compare.mjs 与 C# 渲染比对。
//   node browser.mjs <输出目录> [秒=20] [--demos 秒]   （需要全局安装的 playwright）
// 输出：<目录>/<key>.wav、<目录>/stats.json；--demos 另渲染每种乐器的试听片段到 <目录>/demos/，并写出 demos.json（片段乐谱，C# 用同一份渲染）
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(HERE, '../../../sanguozhi2-web');
const out = process.argv[2] || path.resolve(HERE, '../out/audio/js');
const secs = +(process.argv[3] || 20);
const di = process.argv.indexOf('--demos');
const demoSecs = di > 0 ? +(process.argv[di + 1] || 6) : 0;
fs.mkdirSync(out, { recursive: true });

function wav(file, b64, sr) {
  const pcm = Buffer.from(b64, 'base64');
  const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + pcm.length, 4); h.write('WAVEfmt ', 8);
  h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(2, 22); h.writeUInt32LE(sr, 24); h.writeUInt32LE(sr * 4, 28);
  h.writeUInt16LE(4, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(pcm.length, 40);
  fs.writeFileSync(file, Buffer.concat([h, pcm]));
}
const safe = k => k.replace(/@/g, '_at_').replace(/\|/g, '_');

const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage();
page.on('pageerror', e => console.log('页面错误', e.message));
await page.goto('file://' + path.join(WEB, 'tests/music.html'));
await page.waitForFunction(() => window.__renderPcm && window.SG && window.SG.Music && window.SG.Music.PIECES['map@wa']);
const keys = await page.evaluate(() => window.SG.Music.keys().filter(k => k[0] !== '_'));
const stats = [];
for (const k of keys) {
  const r = await page.evaluate(([k, s]) => window.__renderPcm(k, s), [k, secs]);
  wav(path.join(out, safe(k) + '.wav'), r.b64, r.sr);
  stats.push({ key: k, peak: r.peak, rms: r.rms, nan: r.nan, ms: Math.round(r.ms) });
  console.log(`  ${k.padEnd(24)} 峰值 ${r.peak.toFixed(3)}  RMS ${(20 * Math.log10(r.rms)).toFixed(1)} dB  NaN ${r.nan}  渲染 ${Math.round(r.ms)} ms`);
}
fs.writeFileSync(path.join(out, 'stats.json'), JSON.stringify(stats, null, 1));
if (demoSecs) {
  const dd = path.join(out, 'demos');
  fs.mkdirSync(dd, { recursive: true });
  const names = await page.evaluate(() => Object.keys(window.SG.Music.INST).sort());
  const pieces = [];
  for (const n of names) {
    const info = await page.evaluate(n => {
      const key = window.__demoPiece(n);
      const P = window.SG.Music.get(key);
      const skip = { key_: 1, _keyMidi: 1, _derived: 1, _base: 1, droneMidi: 1 };
      return { key, json: JSON.stringify(P, (k, v) => skip[k] ? undefined : v) };
    }, n);
    pieces.push({ key: info.key, piece: JSON.parse(info.json) });
    const r = await page.evaluate(([k, s]) => window.__renderPcm(k, s), [info.key, demoSecs]);
    wav(path.join(dd, safe(info.key) + '.wav'), r.b64, r.sr);
  }
  fs.writeFileSync(path.join(out, 'demos.json'), JSON.stringify(pieces));
  console.log('乐器试听片段 ' + names.length + ' 个 → ' + dd);
}
await browser.close();
