#!/usr/bin/env node
'use strict';
// 网页版编译结果逐条导出（与 audio.exe dump 的格式一致），用于核对 C# 的记谱解析 / 编译
//   node compile-dump.js [输出文件]
const fs = require('fs'), path = require('path'), vm = require('vm');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
const sb = { console, performance: { now: () => Date.now() } };
sb.window = sb; vm.createContext(sb);
for (const f of ['music.js', 'music-inst.js', 'music-han.js', 'music-world.js']) vm.runInContext(fs.readFileSync(path.join(WEB, 'js', f), 'utf8'), sb, { filename: f });
const MU = sb.SG.Music;
const N = v => (v === undefined || v === null) ? '-' : (Math.round(v * 1e6) / 1e6).toFixed(6);
const O = o => o ? ['vib', 'trem', 'grace', 'slide', 'fall', 'acc', 'swell', 'tie', 'gliss', 'glissDown'].map(k => o[k] ? k[0] + k.slice(-1) + (typeof o[k] === 'number' ? o[k] : '') : '').join('') : '';
const out = [];
for (const key of MU.keys()) {
  const C = MU.compile(key);
  out.push(`P ${key} tonic=${C.tonicPc} intro=${N(C.introDur)} loop=${N(C.loopDur)} loopFrom=${C.loopFrom} form=${C.form.join(',')} keys=${C.sampleKeys.join(',')}`);
  for (const sn in C.sections) {
    const S = C.sections[sn];
    out.push(`S ${sn} beats=${N(S.beats)} bpm=${N(S.bpm)} bpmTo=${N(S.bpmTo)} dur=${N(S.dur)} n=${S.items.length}`);
    for (const it of S.items) {
      out.push(`I ${it.type} ${it.voice || it.lane} ${it.inst} ${N(it.beat)} ${N(it.dur)} ${N(it.vel)} ${(it.ps || []).map(N).join(',')} ${N(it.gp)} ${N(it.tp)} ${(it.run || []).map(N).join(',')} ${it.art === undefined ? '-' : it.art} ${N(it.p)} ${O(it.orn)}`);
      if (it.notes) for (const n of it.notes) out.push(`  n ${N(n.beat)} ${N(n.dur)} ${N(n.p)} ${N(n.vel)} ${N(n.gp)} ${N(n.tp)} ${O(n.orn)}`);
    }
  }
}
fs.writeFileSync(process.argv[2] || 'js-compile.txt', out.join('\n') + '\n');
