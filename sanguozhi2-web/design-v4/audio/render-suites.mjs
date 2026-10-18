// AUDIO-V4 §A9: render suite pieces in Chromium (tests/music.html, OfflineAudioContext) → WAV + stats.
//   node design-v4/audio/render-suites.mjs <outdir> [seconds=20] [ruler,ruler,...]
// The pieces are built in Node by suites.js (the same builder the implementation copies) and injected with SG.Music.add.
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { createRequire } from 'module';
import { fileURLToPath } from 'url';
const require = createRequire(import.meta.url);
const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(HERE, '../..');
const out = process.argv[2]; const secs = +(process.argv[3] || 20);
const who = (process.argv[4] || '曹操,刘备,董卓,卑弥呼,康茂德,沃洛吉斯,魁头,胡维色迦,区连,阿根托科克斯').split(',');
fs.mkdirSync(out, { recursive: true });
// build in Node
const S = require('./suites.js');
const vm = require('vm');
global.window = global;
for (const f of ['core.js', 'music.js', 'music-inst.js', 'music-han.js', 'music-world.js', 'data.js', 'world-data.js']) vm.runInThisContext(fs.readFileSync(path.join(WEB, 'js', f), 'utf8'), { filename: f });
const MU = global.SG.Music;
const votes = {};
for (const k of MU.keys()) for (const v of Object.values(MU.get(k).voices || {})) { (votes[v.inst] = votes[v.inst] || {}); votes[v.inst][v.oct || 0] = (votes[v.inst][v.oct || 0] || 0) + 1; }
const OCT = {}; for (const [i, o] of Object.entries(votes)) OCT[i] = +Object.entries(o).sort((a, b) => b[1] - a[1])[0][0];
const culture = n => { try { return global.SG.WorldData.cultureOfGeneral(n); } catch (e) { return 'han'; } };
const jobs = [];
for (const name of who) {
  const H = S.HAND[name]; if (!H) { console.log('no hand suite', name); continue; }
  const suite = Object.assign({ family: S.FAMILY_OF[culture(name)] || 'hua' }, H);
  const pieces = S.buildSuite(MU, name, suite, OCT);
  for (const kind of (process.env.KINDS ? process.env.KINDS.split(',') : ['map', 'battle', 'battle-defend', 'council', 'victory', 'defeat', 'fanfare', 'clash', 'clash-short', 'clash-crit', 'ending'])) jobs.push({ key: `suite:${name}:${kind}`, piece: pieces[kind], kind, name, family: suite.family });
}
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required'] });
const page = await browser.newPage();
page.on('pageerror', e => console.log('pageerror', e.message));
await page.goto('file://' + path.join(WEB, 'tests/music.html'));
await page.waitForFunction(() => window.__renderPcm && window.SG && window.SG.Music && window.SG.Music.PIECES['map@wa']);
const stats = [];
function wav(file, b64, sr) {
  const pcm = Buffer.from(b64, 'base64'); const h = Buffer.alloc(44);
  h.write('RIFF', 0); h.writeUInt32LE(36 + pcm.length, 4); h.write('WAVEfmt ', 8); h.writeUInt32LE(16, 16); h.writeUInt16LE(1, 20); h.writeUInt16LE(2, 22);
  h.writeUInt32LE(sr, 24); h.writeUInt32LE(sr * 4, 28); h.writeUInt16LE(4, 32); h.writeUInt16LE(16, 34); h.write('data', 36); h.writeUInt32LE(pcm.length, 40);
  fs.writeFileSync(file, Buffer.concat([h, pcm]));
}
for (const j of jobs) {
  const sec = /clash|fanfare/.test(j.kind) ? 5 : /victory|defeat/.test(j.kind) ? 22 : secs;
  const r = await page.evaluate(([k, P, s]) => { window.SG.Music.add(k, P); return window.__renderPcm(k, s); }, [j.key, j.piece, sec]);
  const fn = path.join(out, `${j.name}_${j.kind}.wav`); wav(fn, r.b64, r.sr);
  const row = { key: j.key, family: j.family, sec, peak: +r.peak.toFixed(3), rmsDb: +(20 * Math.log10(r.rms)).toFixed(1), nan: r.nan, renderMs: Math.round(r.ms) };
  stats.push(row); console.log(JSON.stringify(row));
}
fs.writeFileSync(path.join(out, 'stats.json'), JSON.stringify(stats, null, 1));
await browser.close();
