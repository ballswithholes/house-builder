// TOURNAMENT §9.3：在 Chromium 中离线渲染会场音乐（tests/music.html 的 __renderPcm）→ WAV + 响度统计
//   node design-v4/tourney/render-arena.mjs <outdir> [seconds=30]
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(HERE, '../..');
const out = process.argv[2]; const secs = +(process.argv[3] || 30);
fs.mkdirSync(out, { recursive: true });
const src = fs.readFileSync(path.join(HERE, 'arena-music.js'), 'utf8').split('// ------------------------------------------------------------------ 自检')[0];
const browser = await chromium.launch({ args: ['--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage();
page.on('pageerror', e => console.log('pageerror', e.message));
await page.goto('file://' + path.join(WEB, 'tests/music.html'));
await page.waitForFunction(() => window.__renderPcm && window.SG && window.SG.Music && window.SG.Music.PIECES['map@wa']);
await page.addScriptTag({ content: src });
function wav(file, b64, sr) {
  const pcm = Buffer.from(b64, 'base64'); const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + pcm.length, 4); h.write('WAVEfmt ', 8); h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(2, 22);
  h.writeUInt32LE(sr, 24); h.writeUInt32LE(sr * 4, 28); h.writeUInt16LE(4, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(pcm.length, 40);
  fs.writeFileSync(file, Buffer.concat([h, pcm]));
}
const stats = [];
for (const [k, s] of [['tourney', secs * 2], ['tourney-final', secs], ['tourney-ko', 4]]) {
  const r = await page.evaluate(([k, s]) => window.__renderPcm(k, s), [k, s]);
  wav(path.join(out, k + '.wav'), r.b64, r.sr);
  const row = { key: k, sec: s, peak: +r.peak.toFixed(3), rmsDb: +(20 * Math.log10(r.rms)).toFixed(1), nan: r.nan, renderMs: Math.round(r.ms) };
  stats.push(row); console.log(JSON.stringify(row));
}
fs.writeFileSync(path.join(HERE, 'render-stats.json'), JSON.stringify(stats, null, 1));
await browser.close();
