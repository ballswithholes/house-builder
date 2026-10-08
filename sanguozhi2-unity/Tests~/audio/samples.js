#!/usr/bin/env node
'use strict';
// 用网页版的乐器渲染函数（js/music-inst.js，Node 沙箱）渲染一批采样，写成二进制文件供 audio.exe samples 逐点比对。
//   node samples.js [输出文件]     每种采样型乐器取曲库里用到的最多 4 个采样键（最低、最高、两个中间）
const fs = require('fs'), path = require('path'), vm = require('vm');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
const sb = { console, performance: { now: () => Date.now() } };
sb.window = sb; vm.createContext(sb);
for (const f of ['music.js', 'music-inst.js', 'music-han.js', 'music-world.js']) vm.runInContext(fs.readFileSync(path.join(WEB, 'js', f), 'utf8'), sb, { filename: f });
const MU = sb.SG.Music;
const byInst = {};
for (const key of MU.keys()) for (const fk of MU.compile(key).sampleKeys) { const i = fk.indexOf('|'); (byInst[fk.slice(0, i)] = byInst[fk.slice(0, i)] || new Set()).add(fk); }
// 曲库没用到的采样型乐器也测一个音
for (const n of Object.keys(MU.INST)) if (MU.INST[n].type === 'sample' && !byInst[n]) byInst[n] = new Set([n + '|' + (MU.INST[n].pitchless ? 'a0#0' : '62.00')]);
const pick = arr => { arr.sort(); if (arr.length <= 4) return arr; const n = arr.length; return [arr[0], arr[Math.floor(n / 3)], arr[Math.floor(2 * n / 3)], arr[n - 1]]; };
const bufs = [];
let count = 0;
for (const inst of Object.keys(byInst).sort()) {
  for (const fk of pick(Array.from(byInst[inst]))) {
    const i = fk.indexOf('|'), def = MU.INST[inst], sub = fk.slice(i + 1), sr = def.sr || 32000;
    let gen;
    if (sub[0] === 'a') {
      const m = /^a([^@#]+)(?:@([0-9.\-]+))?#(\d+)$/.exec(sub);
      const art = isNaN(+m[1]) ? m[1] : +m[1], pitch = m[2] !== undefined ? parseFloat(m[2]) : null;
      gen = def.render({ sr, art, pitch, freq: pitch !== null ? MU.util.mtof(pitch) : 0, variant: +m[3], seed: MU.util.hash(fk) });
    } else { const p = sub === 'x' ? 60 : parseFloat(sub); gen = def.render({ sr, pitch: p, freq: MU.util.mtof(p), seed: MU.util.hash(fk) }); }
    let r; do { r = gen.next(); } while (!r.done);
    const d = r.value, kb = Buffer.from(fk, 'utf8');
    const h = Buffer.alloc(2 + kb.length + 8);
    h.writeUInt16LE(kb.length, 0); kb.copy(h, 2); h.writeInt32LE(sr, 2 + kb.length); h.writeInt32LE(d.length, 6 + kb.length);
    bufs.push(h, Buffer.from(d.buffer, d.byteOffset, d.length * 4));
    count++;
  }
}
fs.writeFileSync(process.argv[2] || 'js-samples.bin', Buffer.concat(bufs));
console.log('JS 采样 ' + count + ' 个 → ' + (process.argv[2] || 'js-samples.bin'));
