'use strict';
/* ==========================================================================
   AUDIO-V4.md Part A reference prototype: 君主乐章 (per-ruler music suites).
   The implementation copies HAND / generate() / buildSuite() into js/music-suites.js
   (loaded after music-world.js and music-stings.js); audio.js resolves kinds through SG.MusicSuites.

   A suite = the ruler's leitmotif (2 bars of 4/4 in 简谱 degrees) + a palette
   (family, lead / answer roles, tempo factor, transposition). buildSuite() turns it into
   real SG.Music pieces by
     · wrapping the family's existing map / battle / council / defend / duel pieces
       (music-han.js, music-world.js) with motif sections: Mi (4-bar intro statement),
       Mt (2-bar tag at the end of each loop), Mc (2-bar battle call at the top of each loop),
       Md (defend: motif in augmentation), Ma / Mb (duel: challenger's and defender's calls);
     · generating short pieces from the motif alone: victory (8 bars), defeat (6 bars),
       fanfare (2 bars), clash / clash-b / clash-short / clash-crit (v4 clash timing).
   Motif degrees are re-spelled into each template's mode (respell()), so one motif keeps its
   contour and rhythm in a pentatonic map piece, a Bhairav battle piece and a 12/8 steppe gallop.

   node design-v4/audio/suites.js          → builds the 42 hand suites + generated suites for every
                                             general of both scenarios, validates every piece with the
                                             real SG.Music.validate, checks durations, distinctness
                                             and prints a summary (ALL OK / FAIL)
   node design-v4/audio/suites.js --list   → also prints every ruler's motif
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');
const { FAMILY_INST } = require('../domestic/stings.js');

// ------------------------------------------------------------ culture → family / templates --
const FAMILY_OF = {
  han: 'hua', nanman: 'hua', korea: 'han3', wa: 'wa', steppe: 'steppe', sarmatian: 'steppe', tarim: 'oasis',
  kushan: 'indic', india: 'indic', persia: 'persia', arab: 'arabia', moor: 'arabia', africa: 'nile',
  roman: 'roman', celt: 'north', german: 'north', seasia: 'nanhai', yi: 'nanhai',
};
// family → existing culture variant used as the arrangement template (music.js resolve keys)
const TEMPLATE_CULTURE = {
  hua: 'han', han3: 'korea', wa: 'wa', steppe: 'steppe', oasis: 'kushan', indic: 'kushan', persia: 'persia',
  arabia: 'arab', nile: 'arab', roman: 'roman', north: 'celt', nanhai: 'seasia',
};
// base (hua) instrument of every role; FAMILY_INST (domestic/stings.js) substitutes per family
const HUA = {
  flute: 'dizi', flute2: 'xiao', bow: 'erhu', pluck: 'zheng', pluck2: 'yangqin', pluck3: 'ruan', pluck4: 'qin',
  reed: 'suona', brass: 'horns', brass1: 'horn', bell: 'bianzhong', pad: 'sheng', strings: 'strings', choir: 'choir',
  bass: 'lowstr', timp: 'timpani', drum: 'dagu', drum2: 'tanggu', gong: 'luo', tamtam: 'tamtam', cymbal: 'bo', wood: 'muyu',
};
// legato / poly instruments only may carry a motif line (sample instruments are fine for pluck roles)
function inst(fam, role) { return (FAMILY_INST[fam] || {})[role] || HUA[role]; }
// pitched percussion needs a kit `note` (else the engine has no pitch → NaN); found by the Chromium render (§A9)
const PITCHED_KIT = { kenong: 1, kempul: 1, gongageng: 1, dayan: 1, saron: 1, gender: 1, bonang: 1, bianzhong: 1, timpani: 1 };
function kitEntry(i, tonic) { return PITCHED_KIT[i] ? { inst: i, note: tonic } : { inst: i }; }

// ------------------------------------------------------------ the 42 hand-composed suites --
// motif: 2 bars of 4/4 in 简谱 degrees (1 = the template's "1"); the cadence note is the suite's final.
// lead / answer: instrument roles; tempo: factor on the template's bpm; tr: semitones; temper: see §A3
const HAND = {
  // ---- 中原（经典剧本 18 君主）----
  '董卓':   { motif: '6,:2 3> #4_ 3_ | 1 7,_ 6,_ 6,:2 |', lead: 'brass', answer: 'bow', tempo: 0.94, tr: -2, temper: 'tyrant', note: '低音铜管拖着增四度，暴君的阴影' },
  '袁绍':   { motif: '5,. 1_ 3 5 | 6. 5_ 3 1 |', lead: 'brass1', answer: 'bell', tempo: 0.98, tr: 1, temper: 'noble', note: '四世三公：附点的仪仗，钟声应答' },
  '曹操':   { motif: '6,_ 3_ 6 5. 3_ | 2 3 6,:2 |', lead: 'reed', answer: 'bow', tempo: 1.04, tr: 0, temper: 'hero', note: '羽调式，上冲八度后回落，雄心与诡谲' },
  '刘备':   { motif: '3 5 6. 1\'_ | 6 5 3:2 |', lead: 'bow', answer: 'flute', tempo: 0.97, tr: 0, temper: 'benevolent', note: '宫调式，二胡的温厚上行，落在角音' },
  '孙坚':   { motif: '5,_ 5,_ 1 5. 6_ | 1\' 6 5:2 |', lead: 'brass1', answer: 'reed', tempo: 1.05, tr: -1, temper: 'tiger', note: '孙氏家传动机 5, 1 5：四度 + 五度的猛跃' },
  '袁术':   { motif: '1\'. 6_ 5 3^ | 2 1 2:2 |', lead: 'reed', answer: 'pluck2', tempo: 1.0, tr: 2, temper: 'vain', note: '华丽下行，倚音虚饰，停在商音不解决' },
  '公孙瓒': { motif: '5_ 5__ 5__ 6_ 5__ 5__ 1\' 6 | 5. 3_ 5:2 |', lead: 'brass1', answer: 'flute', tempo: 1.06, tr: 0, temper: 'cavalry', note: '白马义从：长短短的马蹄节奏' },
  '刘表':   { motif: '2. 3_ 5 6 | 5 3 2:2 |', lead: 'pluck4', answer: 'flute2', tempo: 0.95, tr: 0, temper: 'scholar', note: '商调式，古琴与箫的平稳级进' },
  '刘焉':   { motif: '3 5_ 6_ 3\' 2\'_ 1\'_ | 6:2 3:2 |', lead: 'flute2', answer: 'bell', tempo: 0.96, tr: 1, temper: 'mystic', note: '角调式，蜀中的神秘上扬' },
  '马腾':   { motif: '6, 3_ 3_ 6 3\'_ 2\'_ | 1\' 6 6:2 |', lead: 'bow', answer: 'reed', tempo: 1.04, tr: -2, temper: 'frontier', note: '西凉：羽调式的马步与大跳' },
  '陶谦':   { motif: '5 3 2. 1_ | 2 3 1:2 |', lead: 'pad', answer: 'flute', tempo: 0.93, tr: 0, temper: 'elder', note: '老成持重，笙的下行' },
  '孔融':   { motif: '5 6^ 1\' 6_ 5_ | 3 5 5:2 |', lead: 'pluck4', answer: 'flute2', tempo: 0.96, tr: 2, temper: 'scholar', note: '建安文士：琴的倚音与回环' },
  '韩馥':   { motif: '2 0_ 3_ 2 1 | 6, 1 2:2 |', lead: 'flute2', answer: 'pluck', tempo: 0.97, tr: -1, temper: 'timid', note: '迟疑的休止，商音悬而未决' },
  '张鲁':   { motif: '3:2 6 5 | 3 2 3:2 |', lead: 'bell', answer: 'flute2', tempo: 0.94, tr: 0, temper: 'priest', note: '五斗米道：编钟的仪式音型' },
  '张杨':   { motif: '5 5_ 6_ 5 1\' | 6 5 5:2 |', lead: 'reed', answer: 'pluck3', tempo: 1.0, tr: 1, temper: 'plain', note: '朴实的徵调号子' },
  '刘繇':   { motif: '1 3 2 1_ 6,_ | 5, 6, 1:2 |', lead: 'flute', answer: 'pluck', tempo: 0.98, tr: -1, temper: 'official', note: '扬州：宫调式的低回' },
  '陆康':   { motif: '6, 1 3. 2_ | 1 6, 6,:2 |', lead: 'bow', answer: 'pluck4', tempo: 0.95, tr: 2, temper: 'loyal', note: '庐江死守：庄重的羽调' },
  '孟获':   { motif: '6,_ 6,_ 6,_ 1_ 3 3 | 2_ 1_ 6,_ 5,_ 6,:2 |', lead: 'reed', answer: 'bow', tempo: 1.06, tr: -2, temper: 'wild', note: '南中：铜鼓般的重复音' },
  // ---- 可能的继承者（继承家传动机的头三个音）----
  '孙策':   { motif: '5,_ 5,_ 1 5 1\' | 2\'. 1\'_ 6 5 |', lead: 'reed', answer: 'brass1', tempo: 1.08, tr: -1, temper: 'tiger', house: '孙坚', note: '小霸王：家传头三音，更高更急' },
  '刘璋':   { motif: '3 5_ 6_ 5 3 | 2 1 6,:2 |', lead: 'flute2', answer: 'pluck', tempo: 0.93, tr: 1, temper: 'timid', house: '刘焉', note: '刘焉的动机向下塌落' },
  '马超':   { motif: '6, 3_ 3_ 6 6\' | 5\'. 3\'_ 6:2 |', lead: 'reed', answer: 'bow', tempo: 1.08, tr: -2, temper: 'cavalry', house: '马腾', note: '锦马超：家传马步冲上高八度' },
  '公孙康': { motif: '2 5 2\' 6 | 5 3 2:2 |', lead: 'brass1', answer: 'flute', tempo: 1.0, tr: 0, temper: 'plain', house: '公孙度', note: '承袭辽东的商调' },
  // ---- 世界剧本 ----
  '公孙度': { motif: '2 5 2\'. 1\'_ | 6 5 2:2 |', lead: 'brass1', answer: 'bow', tempo: 1.0, tr: 0, temper: 'harsh', note: '辽东：北地的商调，四度上冲' },
  '士燮':   { motif: '3_ 5_ 6 1\' 2\'_ 1\'_ | 6 5:3 |', lead: 'flute', answer: 'pluck', tempo: 0.95, tr: 2, temper: 'scholar', note: '交趾四十年太平：笛子的长句' },
  '卑弥呼': { motif: '3:2 4 6 | 7^ 6 4 3 |', lead: 'flute', answer: 'pluck', tempo: 0.92, tr: 0, temper: 'shaman', note: '都节音阶，篠笛的鬼道咒语' },
  '卑弥弓呼': { motif: '6, 7, 3. 4_ | 3 7, 6,:2 |', lead: 'flute2', answer: 'pluck', tempo: 1.03, tr: -1, temper: 'rival', note: '狗奴：低沉的尺八与太鼓' },
  '故国川王': { motif: '6,:2 2 1 | 3. 2_ 1 6, |', lead: 'flute', answer: 'bow', tempo: 0.98, tr: 0, temper: 'wise', note: '大笒长音，界面调' },
  '首露王': { motif: '5 3 5 6_ 5_ | 3 2 1:2 |', lead: 'bow', answer: 'pluck', tempo: 0.97, tr: 1, temper: 'founder', note: '卵生的始祖：奚琴与伽倻琴' },
  '魁头':   { motif: '1 5 1\':2 | 6 5 3 5 |', lead: 'bow', answer: 'flute2', tempo: 1.02, tr: 0, temper: 'khan', note: '长调的五度、八度大跳' },
  '於夫罗': { motif: '6, 6,_ 3_ 6 5 | 3 2_ 1_ 6,:2 |', lead: 'bow', answer: 'pluck', tempo: 1.04, tr: -2, temper: 'exile', note: '南匈奴单于：马头琴的流亡之歌' },
  '胡维色迦': { motif: '1 5 4 3 | 2 3 1:2 |', lead: 'pluck', answer: 'flute', tempo: 0.98, tr: 0, temper: 'emperor', note: '贵霜：西塔尔的庄严回旋' },
  '耶若室利': { motif: '1 3 5. 6_ | 5 4 3:2 |', lead: 'flute', answer: 'pluck', tempo: 1.0, tr: 2, temper: 'king', note: '百乘：班苏里的上行' },
  '迦梨迦罗': { motif: '1\' 7 6 5_ 6_ | 5 3 5:2 |', lead: 'reed', answer: 'pluck', tempo: 1.04, tr: -1, temper: 'hero', note: '朱罗：筑大堤的英雄' },
  '沃洛吉斯': { motif: '1 3 2. 1_ | 7, 2 1:2 |', lead: 'flute', answer: 'bow', tempo: 0.98, tr: 0, temper: 'kingofkings', note: '安息：奈伊的 Shur 色彩' },
  '戈契赫尔': { motif: '1 5 6. 5_ | 4 3 2 1 |', lead: 'bow', answer: 'pluck', tempo: 1.0, tr: 1, temper: 'harsh', note: '波西斯：萨珊前夜' },
  '阿布加尔': { motif: '1 4. 3_ 2_ 3_ | 2 1 7,:2 |', lead: 'pluck', answer: 'flute', tempo: 0.97, tr: 0, temper: 'pious', note: '埃德萨：乌德的木卡姆' },
  '加达拉特': { motif: '1 5 4 5 | 3:2 1:2 |', lead: 'reed', answer: 'pluck', tempo: 1.0, tr: 0, temper: 'king', note: '阿克苏姆：竖琴与阿夫洛斯管' },
  '康茂德': { motif: '1 1_ 5,_ 1 3 | 5. 4_ 3 1 |', lead: 'brass1', answer: 'flute', tempo: 1.02, tr: 0, temper: 'gladiator', note: '罗马的赫拉克勒斯：号角的竞技场' },
  '阿根托科克斯': { motif: '5, 1 1_ 2_ 1 | 5, 6,_ 7,_ 1:2 |', lead: 'reed', answer: 'pluck', tempo: 1.0, tr: 0, temper: 'chieftain', note: '喀里多尼亚：风笛' },
  '巴洛马尔': { motif: '1 1 5. 4_ | 3 2 1:2 |', lead: 'brass1', answer: 'flute', tempo: 1.02, tr: -1, temper: 'warlord', note: '马科曼尼：卢尔号' },
  '菲利默': { motif: '1 3 5 5 | 6 5 3:2 |', lead: 'reed', answer: 'pluck', tempo: 1.0, tr: 1, temper: 'migrant', note: '哥特：迁徙之歌' },
  '区连':   { motif: '3 5 1\' 5 | 3 2 3:2 |', lead: 'flute', answer: 'pluck', tempo: 1.02, tr: 0, temper: 'rebel', note: '林邑：192 年起兵的苏林笛' },
};

// ------------------------------------------------------------ motif parsing / respelling --
const DEG_SEMI = [0, 0, 2, 4, 5, 7, 9, 11];
const SPELL = ['1', 'b2', '2', 'b3', '3', '4', '#4', '5', 'b6', '6', 'b7', '7'];
// '5,.' → { rest, acc, deg, oct, dur, orn }
function parseMotif(s) {
  const out = [];
  for (const tk of s.trim().split(/\s+/)) {
    if (tk === '|') { out.push({ bar: true }); continue; }
    const m = /^([#b]?)([0-7])([',]*)([_.t]*)(?::([0-9.]+))?([~*^v\/\\><)@&]*)$/.exec(tk);
    if (!m) throw new Error('bad motif token ' + tk);
    let dur = m[5] ? parseFloat(m[5]) : 1;
    for (const c of m[4]) dur *= c === '_' ? 0.5 : c === '.' ? 1.5 : 2 / 3;
    let oct = 0; for (const c of m[3]) oct += c === "'" ? 1 : -1;
    out.push({ rest: m[2] === '0', acc: m[1], deg: +m[2], oct, dur, orn: m[6] || '' });
  }
  return out;
}
// template scale → { byDeg: {deg: semitone}, pcs: [semitones] }
function scaleInfo(scale) {
  const byDeg = {}, pcs = [];
  for (const t of String(scale).trim().split(/\s+/)) {
    const m = /^([#b]?)([1-7])/.exec(t); if (!m) continue;
    const st = DEG_SEMI[+m[2]] + (m[1] === '#' ? 1 : m[1] === 'b' ? -1 : 0);
    const pc = ((st % 12) + 12) % 12;
    if (byDeg[+m[2]] === undefined) byDeg[+m[2]] = pc;
    if (!pcs.includes(pc)) pcs.push(pc);
  }
  return { byDeg, pcs: pcs.sort((a, b) => a - b) };
}
// keep the degree number, take its accidental from the template's mode; degrees the mode lacks
// move to the nearest scale tone (explicit accidentals in the motif — e.g. 董卓's #4 — are kept)
function respellNote(n, sc) {
  if (n.rest || n.bar) return n;
  if (n.acc) return n;
  let pc = sc.byDeg[n.deg];
  if (pc === undefined) {
    const base = DEG_SEMI[n.deg];
    let best = null;
    for (const p of sc.pcs) for (const q of [p - 12, p, p + 12]) if (best === null || Math.abs(q - base) < Math.abs(best - base) || (Math.abs(q - base) === Math.abs(best - base) && q < best)) best = q;
    const abs = 12 * n.oct + best;
    const o = Math.floor(abs / 12), s = SPELL[((abs % 12) + 12) % 12];
    const mm = /^([#b]?)([1-7])$/.exec(s);
    return Object.assign({}, n, { acc: mm[1], deg: +mm[2], oct: o });
  }
  const want = pc, natural = DEG_SEMI[n.deg];
  const d = ((want - natural + 18) % 12) - 6;
  return Object.assign({}, n, { acc: d === 1 ? '#' : d === -1 ? 'b' : '' });
}
const noteHead = n => n.rest ? '0' : n.acc + n.deg + (n.oct > 0 ? "'".repeat(n.oct) : ','.repeat(-n.oct));
// D section (consolidation, AUDIO-V4 §A7): move a respelled note k steps along the template's scale (a melodic sequence)
function seqDeg(n, k, sc) {
  if (n.rest || n.bar) return n;
  const abs = 12 * n.oct + DEG_SEMI[n.deg] + (n.acc === '#' ? 1 : n.acc === 'b' ? -1 : 0);
  const steps = [];
  for (let o = -3; o <= 3; o++) for (const pc of sc.pcs) steps.push(12 * o + pc);
  let i = 0, best = Infinity;
  steps.forEach((v, j) => { if (Math.abs(v - abs) < best) { best = Math.abs(v - abs); i = j; } });
  const t = steps[Math.max(0, Math.min(steps.length - 1, i + k))];
  const o = Math.floor(t / 12), sp = SPELL[((t % 12) + 12) % 12];
  const mm = /^([#b]?)([1-7])$/.exec(sp);
  return Object.assign({}, n, { acc: mm[1], deg: +mm[2], oct: o });
}
// inversion around the first pitched note, in scale steps (an ascending step becomes a descending one)
function invDeg(ns, sc) {
  const steps = [];
  for (let o = -3; o <= 3; o++) for (const pc of sc.pcs) steps.push(12 * o + pc);
  const absOf = n => 12 * n.oct + DEG_SEMI[n.deg] + (n.acc === '#' ? 1 : n.acc === 'b' ? -1 : 0);
  const idxOf = a => { let i = 0, best = Infinity; steps.forEach((v, j) => { if (Math.abs(v - a) < best) { best = Math.abs(v - a); i = j; } }); return i; };
  const first = ns.find(n => !n.rest && !n.bar);
  if (!first) return ns;
  const i0 = idxOf(absOf(first));
  return ns.map(n => {
    if (n.rest || n.bar) return n;
    const t = steps[Math.max(0, Math.min(steps.length - 1, 2 * i0 - idxOf(absOf(n))))];
    const o = Math.floor(t / 12), sp = SPELL[((t % 12) + 12) % 12];
    const mm = /^([#b]?)([1-7])$/.exec(sp);
    return Object.assign({}, n, { acc: mm[1], deg: +mm[2], oct: o });
  });
}
// bars of a section = the longest voice line's barlines
function sectionBars(sec) { let m = 0; for (const v of Object.values(sec)) if (typeof v === 'string') m = Math.max(m, (v.match(/\|/g) || []).length); return m; }
// share of the loop's bars that are motif-derived (sections Mi, Mr, Mt, Mv, Mc, MB, Md); AUDIO-V4 §A12 target ≥ 0.25
function motifShare(P) {
  const i0 = Math.max(0, P.form.indexOf(P.loopFrom));
  let all = 0, mot = 0;
  for (const name of P.form.slice(i0)) { const b = sectionBars(P.sections[name] || {}); all += b; if (/^M[itrvcBd]$/.test(name)) mot += b; }
  return all ? mot / all : 0;
}
// motif (4/4, 8 beats) → notation for a template meter; f = template beats per 4/4 beat
function render(notes, meter, opts) {
  opts = opts || {};
  const scale = opts.aug || 1;                 // augmentation (defend: ×2)
  let f = meter % 3 === 0 && meter >= 6 ? meter / 4 : 1;   // 12 → 3, 6 → 1.5
  const ev = notes.filter(n => !n.bar).map(n => Object.assign({}, n, { dur: n.dur * scale }));
  // compound metres: dotted pairs and even eighths become long–short (2:1) so the motif swings
  let d = ev.map(n => n.dur * f);
  if (f !== 1) {
    for (let i = 0; i + 1 < ev.length; i++) {
      const a = ev[i].dur, b = ev[i + 1].dur;
      if ((a === 1.5 * scale && b === 0.5 * scale) || (a === 0.5 * scale && b === 0.5 * scale)) {
        const tot = (a + b) * f; d[i] = tot * 2 / 3; d[i + 1] = tot / 3; i++;
      } else if (a === 0.75 * scale && b === 0.25 * scale) { const tot = (a + b) * f; d[i] = tot * 2 / 3; d[i + 1] = tot / 3; i++; }
    }
  }
  if (meter === 3) {                         // 3/4 (celt map): 8 beats → 3 bars of 3 = 9 beats, last note +1
    f = 1; d = ev.map(n => n.dur); { const sum = d.reduce((x, y) => x + y, 0); d[d.length - 1] += (3 - (Math.round(sum) % 3)) % 3; }   // pad to whole bars (8 beats → +1, augmented 16 → +2)
  }
  const bar = meter === 3 ? 3 : (f !== 1 ? meter : 4);
  // emit with barlines; split notes that cross a barline with a tie ')'
  const toks = [];
  let pos = 0;
  ev.forEach((n, i) => {
    let left = d[i];
    let first = true;
    while (left > 1e-9) {
      const room = bar - (pos % bar);
      const take = Math.min(left, room);
      const tail = left - take > 1e-9;
      let t = noteHead(n) + ':' + (+take.toFixed(4));
      if (first && n.orn && !n.rest) t += n.orn.replace(/\)/g, '');
      if (tail && !n.rest) t += ')';
      toks.push(t);
      pos += take; left -= take; first = false;
      if (Math.abs(pos % bar) < 1e-9) toks.push('|');
    }
  });
  if (toks[toks.length - 1] !== '|') toks.push('|');
  return { str: toks.join(' '), beats: pos, bars: Math.round(pos / bar) };
}
function rests(bars, meter) { return Array(bars).fill('0:' + meter).join(' | ') + ' |'; }
function droneBars(bars, meter, tonicAbs, fifth) {
  const t = spellAbs(tonicAbs), v = spellAbs(tonicAbs + (fifth === false ? 12 : 7));
  return Array(bars).fill(`${t}+${v}:${meter}`).join(' | ') + ' |';
}
function spellAbs(abs) { const o = Math.floor(abs / 12); return SPELL[((abs % 12) + 12) % 12] + (o > 0 ? "'".repeat(o) : ','.repeat(-o)); }
function tonicAbsOf(P) {
  const t = P.tonic || String(P.scale || '1').trim().split(/\s+/)[0];
  const m = /^([#b]?)([1-7])([',]*)/.exec(t);
  let o = 0; for (const c of m[3]) o += c === "'" ? 1 : -1;
  return 12 * o + DEG_SEMI[+m[2]] + (m[1] === '#' ? 1 : m[1] === 'b' ? -1 : 0);
}
// kit pattern string for one bar of `meter` beats at step 0.25 (or 0.5 for compound metres)
function drumBar(meter, kind) {
  const step = meter >= 6 ? 0.5 : 0.25, n = Math.round(meter / step);
  const a = Array(n).fill('.');
  const beat = meter >= 6 ? 3 : 1;           // pulse in template beats (dotted quarter in 12/8)
  for (let b = 0; b < meter; b += beat) {
    const i = Math.round(b / step);
    a[i] = b === 0 ? 'X' : (kind === 'martial' ? 'x' : 'o');
    if (kind === 'martial' && i + Math.round(beat / step / 2) < n) a[i + Math.round(beat / step / 2)] = 'o';
  }
  return { s: a.join(''), step };
}

// ------------------------------------------------------------ the generator (§A4) --
function fnv(s) { let h = 2166136261 >>> 0; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }
function rng(seed) { let a = seed | 0; return () => { a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
function temperOf(g) {
  const m = Math.max(g.war, g.intel, g.pol);
  if (g.war === m && g.war >= g.intel + 6) return 'martial';
  if (g.intel === m && g.intel >= g.pol) return 'scholar';
  if (g.pol === m) return 'statesman';
  return 'martial';
}
// rhythm banks: each entry is one bar (4 beats) as durations
const RHY = {
  martial: { a: [[1.5, 0.5, 1, 1], [0.5, 0.5, 1, 1.5, 0.5], [1, 0.5, 0.5, 1, 1], [0.75, 0.25, 1, 2], [1, 1, 0.5, 0.5, 1]],
             b: [[1, 1, 2], [1.5, 0.5, 2], [0.5, 0.5, 1, 2], [1, 3]] },
  scholar: { a: [[2, 1, 1], [1, 1, 1, 0.5, 0.5], [1.5, 0.5, 1, 1], [1, 0.5, 0.5, 2]],
             b: [[1, 1, 2], [2, 2], [1, 3], [0.5, 0.5, 1, 2]] },
  statesman: { a: [[1, 1, 1, 1], [2, 1, 1], [1, 1, 2], [1.5, 0.5, 1, 1]],
               b: [[1, 1, 2], [2, 2], [1, 1, 1, 1], [1, 3]] },
};
// scale degree list (ascending, one octave) of a template scale → [{acc,deg}]
function degList(scale) {
  return String(scale).trim().split(/\s+/).map(t => /^([#b]?)([1-7])([',]*)$/.exec(t)).filter(Boolean)
    .map(m => { let o = 0; for (const c of m[3]) o += c === "'" ? 1 : -1; return { acc: m[1], deg: +m[2], oct: o, st: 12 * o + DEG_SEMI[+m[2]] + (m[1] === '#' ? 1 : m[1] === 'b' ? -1 : 0) }; })
    .sort((a, b) => a.st - b.st);
}
function generate(g, ctx, attempt) {
  // g: { name, war, intel, pol, sex, born }, ctx: { family, virtue, mapScale }
  const seed = fnv(g.name + '|' + (attempt || 0));
  const R = rng(seed);
  const T = temperOf(g);
  const deg = degList(ctx.mapScale);
  const n = deg.length;
  const at = i => { const o = Math.floor(i / n), d = deg[((i % n) + n) % n]; return { acc: d.acc, deg: d.deg, oct: d.oct + o }; };
  const pick = a => a[Math.floor(R() * a.length)];
  const bar1 = pick(RHY[T].a), bar2 = pick(RHY[T].b);
  const durs = bar1.concat(bar2);
  // final: bright modes for virtuous rulers (index 0 = scale's first degree), darker for harsh ones
  // finals by pitch class: 1 (宫 / tonic) and 5 (徵) for virtuous rulers, 6 (羽, minor) and 2 (商) for harsh ones
  const near = pc => { let b = 0; deg.forEach((d, i) => { if (Math.abs(((d.st % 12) + 12) % 12 - pc) < Math.abs(((deg[b].st % 12) + 12) % 12 - pc)) b = i; }); return b; };
  const finals = ctx.virtue >= 60 ? [near(0), near(0), near(7)] : ctx.virtue <= 30 ? [near(9), near(2)] : [near(0), near(7), near(9)];
  const fin = pick(finals);
  const leapP = T === 'martial' ? 0.45 : T === 'scholar' ? 0.15 : 0.28;
  let idx = pick(T === 'martial' ? [fin - n, fin, fin + 2] : [fin, fin + 1, fin + 2]);
  const notes = [];
  for (let i = 0; i < durs.length; i++) {
    if (i === durs.length - 1) idx = fin + (idx > fin + 3 ? n : 0);   // cadence on the final (upper octave if far above)
    // register: keep every note between 5, and 3'' (−7 … +16 semitones from "1") so it sits in every lead's range
    const stOf = j => { const o = Math.floor(j / n); return deg[((j % n) + n) % n].st + 12 * o; };
    while (stOf(idx) < -7) idx += n;
    while (stOf(idx) > 16) idx -= n;
    notes.push(Object.assign(at(idx), { dur: durs[i], orn: '', rest: false }));
    const leap = R() < leapP;
    const dir = R() < (i < durs.length / 2 ? 0.62 : 0.4) ? 1 : -1;
    idx += dir * (leap ? 2 + Math.floor(R() * 2) : 1);
    idx = Math.max(fin - n, Math.min(fin + n + 2, idx));
  }
  // ornaments: scholar → vibrato on the longest note; martial → accent on the first
  if (T === 'scholar') { let L = 0; notes.forEach((x, i) => { if (x.dur > notes[L].dur) L = i; }); notes[L].orn = '~'; }
  if (T === 'martial') notes[0].orn = '>';
  if (g.sex === 'f' && T !== 'martial') notes[1].orn = notes[1].orn || '^';
  const motif = notes.slice(0, bar1.length).map(x => noteHead(x) + ':' + x.dur + x.orn).join(' ') + ' | ' +
    notes.slice(bar1.length).map(x => noteHead(x) + ':' + x.dur + x.orn).join(' ') + ' |';
  const age = g.born ? 190 - g.born : 35;
  const lead = T === 'martial' ? pick(['reed', 'brass1', 'bow']) : T === 'scholar' ? pick(['flute', 'flute2', 'pluck4']) : pick(['bow', 'pluck', 'flute']);
  const answer = lead === 'pluck' || lead === 'pluck4' ? 'flute' : 'pluck';
  const tempo = +((T === 'martial' ? 1.04 : T === 'scholar' ? 0.96 : 1.0) - (age >= 55 ? 0.03 : 0) + (R() - 0.5) * 0.04).toFixed(3);
  const tr = Math.floor(R() * 5) - 2;
  return { motif, lead, answer, tempo, tr, temper: T, generated: true, attempt: attempt || 0 };
}

// ------------------------------------------------------------ distinctness (§A4.4) --
function signature(motifStr) {
  const ns = parseMotif(motifStr).filter(n => !n.bar && !n.rest);
  const st = ns.map(n => 12 * n.oct + DEG_SEMI[n.deg] + (n.acc === '#' ? 1 : n.acc === 'b' ? -1 : 0));
  const iv = []; for (let i = 1; i < Math.min(st.length, 7); i++) iv.push(Math.sign(st[i] - st[i - 1]) * Math.min(7, Math.abs(st[i] - st[i - 1])));
  const rh = ns.slice(0, 7).map(n => n.dur);
  return { iv, rh };
}
function lev(a, b) {
  const d = Array.from({ length: b.length + 1 }, (_, j) => j);
  for (let i = 1; i <= a.length; i++) { let p = d[0]; d[0] = i; for (let j = 1; j <= b.length; j++) { const t = d[j]; d[j] = Math.min(d[j] + 1, d[j - 1] + 1, p + (a[i - 1] === b[j - 1] ? 0 : 1)); p = t; } }
  return d[b.length];
}
function distance(A, B) { return lev(A.iv, B.iv) + 0.5 * lev(A.rh, B.rh); }
const D_MIN = +(process.env.D_MIN || 3);
const D_MIN_HOUSE = 2;            // a successor's motif shares the house head on purpose

// ------------------------------------------------------------ piece builder (§A5) --
function clone(o) { return JSON.parse(JSON.stringify(o)); }
function octOfInst(MU) {
  const votes = {};
  for (const k of MU.keys()) { const P = MU.get(k); for (const v of Object.values(P.voices || {})) { (votes[v.inst] = votes[v.inst] || {}); votes[v.inst][v.oct || 0] = (votes[v.inst][v.oct || 0] || 0) + 1; } }
  const out = {};
  for (const [i, o] of Object.entries(votes)) out[i] = +Object.entries(o).sort((a, b) => b[1] - a[1])[0][0];
  return out;
}
function buildSuite(MU, id, S, OCT) {
  const fam = S.family, cul = TEMPLATE_CULTURE[fam];
  const T = k => MU.get(MU.resolve(k, cul)) ? MU.resolve(k, cul) : null;
  const leadI = inst(fam, S.lead), ansI = inst(fam, S.answer);
  const pieces = {};
  const motif = parseMotif(S.motif);
  const voiceOf = (i, extra) => Object.assign({ inst: i, oct: OCT[i] !== undefined ? OCT[i] : 0 }, extra || {});
  const padI = fam === 'hua' || fam === 'han3' || fam === 'wa' || fam === 'nanhai' ? 'strings' : (fam === 'steppe' ? 'kargyraa' : 'drones');
  function wrap(kind, tplKey, how) {
    const P = clone(MU.get(tplKey));
    const meter = P.meter || 4;
    const sc = scaleInfo(P.scale || '1 2 3 5 6');
    const ns = motif.map(n => respellNote(n, sc));
    const tAbs = tonicAbsOf(P);
    P.title = id + '·' + P.title; P.transpose = (P.transpose || 0) + (S.tr || 0);
    P.bpm = Math.round(P.bpm * (S.tempo || 1) * (how.bpmK || 1));
    for (const sec of Object.values(P.sections)) {
      if (sec.bpm) sec.bpm = Math.round(sec.bpm * (S.tempo || 1) * (how.bpmK || 1));
      if (sec.bpmTo) sec.bpmTo = Math.round(sec.bpmTo * (S.tempo || 1) * (how.bpmK || 1));
      if (how.dyn) sec.dyn = +((sec.dyn || 1) * how.dyn).toFixed(3);
      if (how.noDrums) delete sec.dr;
    }
    if (how.noDrums) P.gain = +((P.gain || 1) * 1.26).toFixed(3);   // +2 dB: the drumless council is quieter (§A12)
    // lead swap: the template's busiest legato melody voice takes the suite's lead instrument, so the whole
    // loop (not only the motif sections) carries the ruler's timbre (§A5.2). Sample / poly leads are not swapped.
    const LEGATO = MU.INST[how.leadInst || leadI] && MU.INST[how.leadInst || leadI].type === 'legato';
    if (LEGATO && how.swap !== false) {
      let best = null, bestN = 0;
      for (const [vn, v] of Object.entries(P.voices)) {
        if (!MU.INST[v.inst] || MU.INST[v.inst].type !== 'legato') continue;
        let n = 0; for (const sec of Object.values(P.sections)) if (typeof sec[vn] === 'string') n += (sec[vn].match(/(^|\s)[#b]?[1-7]/g) || []).length;
        if (n > bestN) { bestN = n; best = vn; }
      }
      if (best) { P.voices[best] = Object.assign({}, P.voices[best], { inst: how.leadInst || leadI }); P._leadSwap = best + ':' + (how.leadInst || leadI); }
    }
    P.voices.mL = voiceOf(how.leadInst || leadI, { pan: 0.08, gain: 0.95 });
    P.voices.mA = voiceOf(how.ansInst || ansI, { pan: -0.18, gain: 0.8 });
    P.voices.mP = voiceOf(padI, { oct: -1, gain: 0.55 });
    P.voices.mB = voiceOf('lowstr', { oct: -2, gain: 0.6 });
    P.kit = P.kit || {}; P.pats = P.pats || {};
    P.kit.mD = kitEntry(inst(fam, 'drum'), spellAbs(tAbs)); P.kit.mG = kitEntry(inst(fam, 'gong'), spellAbs(tAbs - 12));
    const m1 = render(ns, meter, { aug: how.aug || 1 });
    const bars = m1.bars, mbar = meter === 3 ? 3 : meter;
    const add = [];
    for (const [sec, spec] of Object.entries(how.sections)) {
      const o = {};
      if (spec === 'intro') {             // Mi: motif on lead (2 bars), answer on pluck (2 bars), drone
        const ans = render(ns, meter);
        o.mL = m1.str + ' ' + rests(ans.bars, mbar); o.mA = rests(bars, mbar) + ' ' + ans.str;
        o.mP = '!p ' + droneBars(bars + ans.bars, mbar, tAbs); o.mB = '!p ' + droneBars(bars + ans.bars, mbar, tAbs - 12, false);
      } else if (spec === 'develop') {    // Mv (the D section): sequence a 2nd up on the answer → motif on the lead → inversion on the answer → motif in augmentation on the lead
        const up = render(ns.map(n => seqDeg(n, 1, sc)), meter), dn = render(invDeg(ns, sc), meter);
        const aug = render(ns, meter, { aug: 2 });
        const total = up.bars + bars + dn.bars + aug.bars;
        o.mA = '!mp ' + up.str + ' ' + rests(bars, mbar) + ' ' + dn.str + ' ' + rests(aug.bars, mbar);
        o.mL = rests(up.bars, mbar) + ' !mf ' + m1.str + ' ' + rests(dn.bars, mbar) + ' !f ' + aug.str;
        o.mP = '!pp ' + droneBars(total, mbar, tAbs); o.mB = '!p ' + droneBars(total, mbar, tAbs - 12, false);
      } else if (spec === 'tag') {        // Mt: motif quietly on the answer instrument
        o.mA = '!mp ' + m1.str; o.mP = '!pp ' + droneBars(bars, mbar, tAbs);
      } else if (spec === 'call') {       // Mc / Md: motif on lead over drums, bass pulse
        o.mL = '!f ' + m1.str; o.mB = '!mf ' + droneBars(bars, mbar, tAbs - 12, false);
        const db = drumBar(mbar, how.martial ? 'martial' : 'plain');
        P.pats['p' + sec] = { _step: db.step, mD: Array(bars).fill(db.s).join(' '), mG: Array(bars).fill(0).map((_, i) => i === 0 ? 'x' + '.'.repeat(db.s.length - 1) : '.'.repeat(db.s.length)).join(' ') };
        o.dr = 'p' + sec;
      } else if (spec.duel) {             // Ma / Mb: another ruler's motif, respelled into this mode
        const other = parseMotif(spec.duel).map(n => respellNote(n, sc));
        const r = render(other, meter);
        P.voices[sec + 'L'] = voiceOf(spec.inst, { pan: spec.pan, gain: 0.95 });
        o[sec + 'L'] = '!f ' + r.str; o.mB = '!mf ' + droneBars(r.bars, mbar, tAbs - 12, false);
      }
      P.sections[sec] = o; add.push(sec);
    }
    how.place(P, add);
    pieces[kind] = P;
  }
  // map: Mi before everything, Mt at the end of every loop
  const mapK = T('map');
  // consolidation (AUDIO-V4 §A7): a D section Mv in the middle of the loop, so ≥ 25 % of the loop's bars come from the motif
  // the D section goes right after the template's B section (the reviewer's placement); a template without B in its loop gets it mid-loop
  const midLoop = (P, sec) => { const i0 = P.form.indexOf(P.loopFrom); const iB = P.form.indexOf('B', i0); if (iB >= i0) { P.form.splice(iB + 1, 0, sec); return; } const n = P.form.length - i0; P.form.splice(i0 + Math.max(1, Math.floor(n / 2)), 0, sec); };
  wrap('map', mapK, { sections: { Mi: 'intro', Mr: 'intro', Mt: 'tag', Mv: 'develop' }, place: (P) => {
    midLoop(P, 'Mv'); const i = P.form.indexOf(P.loopFrom); P.form.splice(i, 0, 'Mr'); P.loopFrom = 'Mr';   // Mr: the statement again at the top of every loop
    P.form = ['Mi'].concat(P.form, ['Mt']); } });
  // council: map (or 庙堂 for hua) slower, drums out, motif tag
  const councilK = fam === 'hua' ? 'council' : mapK;
  wrap('council', councilK, { bpmK: fam === 'hua' ? 1 : 0.88, noDrums: fam !== 'hua', dyn: 0.9, leadInst: inst(fam, 'pluck'), sections: { Mi: 'intro', Mt: 'tag' }, place: (P) => { P.form = ['Mi'].concat(P.form, ['Mt']); } });
  // ending: the unifier's motif opens 「一统天下」 on the choir (hua template for everyone: there is one ending)
  wrap('ending', 'ending', { leadInst: 'choir', ansInst: inst(fam, 'brass1') === 'horn' ? 'horn' : inst(fam, 'brass1'), swap: false, sections: { Mi: 'intro' }, place: (P) => { P.form = ['Mi'].concat(P.form); } });
  // battle: Mc call at the top of every loop
  const batK = T('battle');
  // battle: Mc call at the top of every loop, the D section Mv in the middle; the classic (hua) battle also states the
  // ruler's call MB before its B section, so the 18 classic rulers' battles are told apart there too (consolidation)
  wrap('battle', batK, { martial: true, leadInst: inst(fam, S.lead === 'flute' || S.lead === 'flute2' || S.lead === 'pluck' || S.lead === 'pluck4' ? 'reed' : S.lead), sections: Object.assign({ Mc: 'call', Mv: 'develop' }, fam === 'hua' ? { MB: 'call' } : {}), place: (P) => {
    const i = P.form.indexOf(P.loopFrom); P.form.splice(i, 0, 'Mc'); P.loopFrom = 'Mc';
    if (fam === 'hua' && P.form.indexOf('B') > 0) P.form.splice(P.form.indexOf('B'), 0, 'MB'); else delete P.sections.MB;
    midLoop(P, 'Mv');
  } });
  // battle-defend: hua uses 孤城; others the battle template slowed, motif in augmentation on low brass / bow
  const defK = fam === 'hua' ? 'battle-defend' : batK;
  wrap('battle-defend', defK, { bpmK: fam === 'hua' ? 1 : 0.85, dyn: 0.88, aug: 2, leadInst: inst(fam, 'bow'), sections: { Md: 'call' }, place: (P) => { const i = P.form.indexOf(P.loopFrom); P.form.splice(i, 0, 'Md'); P.loopFrom = 'Md'; } });
  // victory / defeat / fanfare / clash: short pieces generated from the motif in the battle template's mode
  const B = MU.get(batK), M = MU.get(mapK);
  const scB = scaleInfo(B.scale), scM = scaleInfo(M.scale);
  const nsB = motif.map(n => respellNote(n, scB)), nsM = motif.map(n => respellNote(n, scM));
  const tB = tonicAbsOf(B), tM = tonicAbsOf(M);
  const kitF = { D: kitEntry(inst(fam, 'drum'), spellAbs(tB)), t: kitEntry(inst(fam, 'drum2'), spellAbs(tB)), G: kitEntry(inst(fam, 'gong'), spellAbs(tB - 12)), C: kitEntry(inst(fam, 'cymbal'), spellAbs(tB)) };
  const brass = inst(fam, 'brass1') === 'horn' && fam !== 'hua' ? inst(fam, 'reed') : inst(fam, 'brass1');
  const head = ns => { const h = []; let t = 0; for (const n of ns) { if (n.bar) continue; if (t >= 3) break; h.push(Object.assign({}, n, { dur: Math.min(n.dur, 3 - t) })); t += Math.min(n.dur, 3 - t); } return h; };
  // victory (6 bars ≈ 13.8 s at ♩104): motif on brass over a drum roll, the motif a 4th higher on the lead, then the held final
  {
    const r = render(nsM, 4), up = render(nsM.map(n => n.bar || n.rest ? n : respellNote(Object.assign({}, n, { deg: ((n.deg + 2) % 7) + 1, oct: n.oct + (n.deg + 3 > 7 ? 1 : 0), acc: '' }), scM)), 4);
    pieces.victory = { title: id + '·凯旋', key: M.key, bpm: 104, meter: 4, scale: M.scale, tonic: M.tonic, loop: false, gain: 1.0, transpose: S.tr || 0,
      voices: { L: voiceOf(brass, { pan: 0.1 }), A: voiceOf(leadI, { pan: -0.15 }), H: voiceOf(inst(fam, 'brass'), { oct: -1, gain: 0.8 }), P: voiceOf('strings', { oct: -1, gain: 0.7 }), Bs: voiceOf('lowstr', { oct: -2 }), Tp: voiceOf('timpani', { oct: -2 }) },
      kit: kitF, pats: { roll: { D: 'o.o.o.oooxxxxXXX', G: '................' }, hit: { D: 'X.......x...x...', G: 'X...............' }, end: { D: 'X...............', G: 'X...............', C: 'X...............' } },
      sections: {
        A: { L: '!f ' + r.str, P: '!mf ' + droneBars(2, 4, tM), Bs: '!mf ' + droneBars(2, 4, tM - 12, false), Tp: `!mf ${spellAbs(tM - 12)}:4* | ${spellAbs(tM - 12)}:4 |`, dr: 'roll hit' },
        B: { A: '!f ' + up.str, H: '!f ' + droneBars(2, 4, tM + 5), P: '!f ' + droneBars(2, 4, tM + 5), Bs: '!f ' + droneBars(2, 4, tM - 7, false), dr: 'hit*2' },
        C: { L: '!ff ' + r.str, A: '!f ' + r.str, H: '!f ' + droneBars(2, 4, tM), P: '!f ' + droneBars(2, 4, tM), Bs: '!f ' + droneBars(2, 4, tM - 12, false), dr: 'hit*2' },
        E: { L: `!ff ${spellAbs(tM)}:4 | ${spellAbs(tM)}:4 |`, H: '!ff ' + droneBars(2, 4, tM), P: '!f !> ' + droneBars(2, 4, tM) + ' !p', Bs: '!f ' + droneBars(2, 4, tM - 12, false), Tp: `!f ${spellAbs(tM - 12)}:4* | ${spellAbs(tM - 12)}:4 |`, dr: 'end _' },
      }, form: ['A', 'B', 'E'] };
  }
  // defeat: motif in augmentation on the bow, falling to the battle final, choir pad
  {
    const r = render(nsB.filter(n => !n.bar).slice(0, 4), 4, { aug: 1.5 });
    const fill = 4 - (r.beats % 4 || 4);
    pieces.defeat = { title: id + '·残阳', key: B.key, bpm: 60, meter: 4, scale: B.scale, tonic: B.tonic, loop: false, gain: 1.05, transpose: S.tr || 0,
      voices: { L: voiceOf(inst(fam, 'bow'), { pan: 0.05 }), P: voiceOf('choirOo', { oct: -1, gain: 0.6 }), Bs: voiceOf('lowstr', { oct: -2, gain: 0.7 }) },
      sections: { A: { L: '!mp ' + r.str.replace(/\|$/, '') + (fill ? ` 0:${fill} |` : '') + ` !p ${spellAbs(tB)}:4~ | 0:4 |`, P: '!pp ' + droneBars(r.bars + (fill ? 1 : 0) + 2, 4, tB), Bs: '!pp ' + droneBars(r.bars + (fill ? 1 : 0) + 2, 4, tB - 12, false) } }, form: ['A'] };
    // render() pads only full bars; recompute the line to fit exactly
    const evs = render(nsB.filter(n => !n.bar).slice(0, 4), 4, { aug: 1.5 });
    const totalBars = Math.ceil(evs.beats / 4) + 2;
    const pad = Math.ceil(evs.beats / 4) * 4 - evs.beats;
    pieces.defeat.sections.A.L = '!mp ' + evs.str.replace(/\s*\|\s*$/, '') + (pad > 1e-9 ? ` 0:${+pad.toFixed(4)} |` : ' |') + ` !p ${spellAbs(tB)}:4~ | 0:4 |`;
    pieces.defeat.sections.A.P = '!pp ' + droneBars(totalBars, 4, tB); pieces.defeat.sections.A.Bs = '!pp ' + droneBars(totalBars, 4, tB - 12, false);
  }
  // fanfare (2 bars ≈ 3.6 s at ♩132): the head on brass, gong on the final (diplomacy answers, 授官, 武林大会 entrances)
  {
    const h = head(nsM);
    const r = render(h.concat([Object.assign({}, nsM.filter(n => !n.bar && !n.rest).slice(-1)[0], { dur: 5, orn: '' })]), 4);
    pieces.fanfare = { title: id + '·号令', key: M.key, bpm: 132, meter: 4, scale: M.scale, tonic: M.tonic, loop: false, gain: 0.85, transpose: S.tr || 0,
      voices: { L: voiceOf(brass, { pan: 0.05 }), H: voiceOf(inst(fam, 'brass'), { oct: -1, gain: 0.7 }), Tp: voiceOf('timpani', { oct: -2 }) },
      kit: kitF, pats: { f: { D: 'X.......o.o.xxXX X...............', G: '................ X...............' } },
      sections: { A: { L: '!f ' + r.str, H: '!mf 0:3 ' + `${spellAbs(tM)}+${spellAbs(tM + 7)}:1 | ${spellAbs(tM)}+${spellAbs(tM + 7)}:4 |`, Tp: `!mf 0:2 ${spellAbs(tM - 12)}* ${spellAbs(tM - 12)}* | ${spellAbs(tM - 12)}:4 |`, dr: 'f' } }, form: ['A'] };
  }
  // clash stingers (v4 timing, §A6): start on contact; bar 2 downbeat (1.6 s at ♩150) = melee1
  {
    const h = head(nsB);
    const cl = (vk, pat, L2) => ({ title: id + '·冲锋', key: B.key, bpm: 150, meter: 4, scale: B.scale, tonic: B.tonic || spellAbs(tB), loop: false, gain: 0.9,
      voices: { L: voiceOf(brass, { pan: 0.1 }), H: voiceOf(inst(fam, 'brass'), { oct: -1 }), Tp: voiceOf('timpani', { oct: -2 }) }, kit: kitF, pats: { c: pat },
      sections: { A: Object.assign({ dr: 'c' }, L2) }, form: ['A'] });
    const hr = render(h.concat([{ rest: true, dur: 1 }]), 4);
    const fifth = `${spellAbs(tB)}+${spellAbs(tB + 7)}`;
    pieces.clash = cl('clash', { D: 'X...o.x.x.xxXXXX X.....x.X.......', t: '....o.o.xxxxrrrr ..x...x.........', C: '................ X...............', G: '................ X...............' },
      { L: '!f ' + hr.str + ` !ff ${spellAbs(tB + 12)}:2 ${spellAbs(tB)}:2 |`, H: `0:4 | !f ${fifth}:4 |`, Tp: `!mf !< ${spellAbs(tB - 12)}* ${spellAbs(tB - 12)}* ${spellAbs(tB - 12)}* ${spellAbs(tB - 12)}* !f | ${spellAbs(tB - 12)}:2 ${spellAbs(tB - 12)}:2 |` });
    pieces['clash-b'] = cl('clash-b', { D: 'X.x.X.x.X.x.xxXX X.....x.X.......', t: 'x.x.x.x.xxxxrrrr ..x...x.........', C: '................ X...............', G: '................ X...............' },
      { H: `!mf !< ${fifth}:2 ${spellAbs(tB + 2)}+${spellAbs(tB + 7)}:2 | !ff ${fifth}:4 |`, L: `0:4 | !f ${spellAbs(tB + 12)}:4 |`, Tp: `!mf ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)} ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)} | !f ${spellAbs(tB - 12)}:4 |` });
    const hs = render(h.map(n => Object.assign({}, n, { dur: n.dur / 2 })).concat([{ rest: true, dur: 2.5 }]), 4);
    pieces['clash-short'] = Object.assign(cl('clash-short', { D: 'x.x.xxxxX.......', t: 'xxxxrrrr........', C: '........X.......', G: '........X.......' },
      { L: '!f ' + hs.str, H: `0:2 !f ${fifth}:2 |`, Tp: `!f ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)}_ ${spellAbs(tB - 12)} ${spellAbs(tB - 12)}:2 |` }), { title: id + '·冲锋·短' });
    // crit (+0.43 s slow beat in the clash): a beat of silence then a tamtam + brass stab
    pieces['clash-crit'] = Object.assign(cl('clash-crit', { D: 'X...........X... X.....x.X.......', G: '............X... X...............', C: '............X... ................' },
      { L: `!f ${noteHead(h[0])}:1 0:2 !ff ${spellAbs(tB + 12)}:1 | ${spellAbs(tB + 12)}:2 ${spellAbs(tB)}:2 |`, H: `0:3 !ff ${fifth}:1 | ${fifth}:4 |`, Tp: `!f ${spellAbs(tB - 12)} 0:2 ${spellAbs(tB - 12)}* | ${spellAbs(tB - 12)}:4 |` }), { title: id + '·会心' });
  }
  return pieces;
}

// ------------------------------------------------------------ Node check --
function load() {
  global.window = global;
  const root = path.join(__dirname, '..', '..', 'js');
  const ld = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
  for (const f of ['core.js', 'music.js', 'music-inst.js', 'music-han.js', 'music-world.js', 'data.js', 'world-data.js']) ld(f);
  return global.SG;
}
// every general of both scenarios with what the generator needs
function roster(SG) {
  const out = [], seen = new Set();
  const fac = {};
  for (const sc of [SG.Scenarios.classic, SG.Scenarios.world]) {
    for (const l of sc.Factions) { const p = l.split('|'); fac[p[0]] = { key: p[0], name: p[1], ruler: p[2], virtue: +p[4], culture: p[6] || (p[0] === 'menghuo' ? 'nanman' : 'han') }; }
  }
  for (const [si, sc] of [[0, SG.Scenarios.classic], [1, SG.Scenarios.world]]) {
    for (const l of sc.Generals) {
      const p = l.split('|');
      if (seen.has(p[0])) continue; seen.add(p[0]);
      const culture = p[9] || (SG.WorldData ? SG.WorldData.cultureOfGeneral(p[0]) : 'han') || 'han';
      out.push({ name: p[0], war: +p[1], intel: +p[2], pol: +p[3], faction: p[4], culture, sex: SG.WorldData.sexOf(p[0]), born: SG.WorldData.bornOf(p[0]), scen: si });
    }
  }
  return { gens: out, fac };
}
function check() {
  const SG = load();
  const MU = SG.Music;
  const OCT = octOfInst(MU);
  const list = process.argv.includes('--list');
  const { gens, fac } = roster(SG);
  const rulers = new Set(Object.values(fac).map(f => f.ruler));
  const fails = [];
  // 1. hand suites: attach family from the general's culture
  const suites = {};
  const byName = Object.fromEntries(gens.map(g => [g.name, g]));
  for (const [name, H] of Object.entries(HAND)) {
    const g = byName[name];
    if (!g) { fails.push('hand suite for unknown general ' + name); continue; }
    suites[name] = Object.assign({ family: FAMILY_OF[g.culture] || 'hua' }, H);
  }
  // 2. generated suites for everyone else, in roster order, with re-rolls for distinctness
  const sigs = Object.entries(suites).map(([n, s]) => [n, signature(s.motif), s.family]);
  for (let i = 0; i < sigs.length; i++) for (let j = i + 1; j < sigs.length; j++) {
    const d = distance(sigs[i][1], sigs[j][1]);
    const house = HAND[sigs[i][0]].house === sigs[j][0] || HAND[sigs[j][0]].house === sigs[i][0];
    if (d < (house ? D_MIN_HOUSE : D_MIN)) fails.push(`hand motifs too close: ${sigs[i][0]} ~ ${sigs[j][0]} (d=${d})`);
  }
  let rerolls = 0, maxAttempt = 0;
  const genStats = { martial: 0, scholar: 0, statesman: 0 };
  for (const g of gens) {
    if (suites[g.name]) continue;
    const family = FAMILY_OF[g.culture] || 'hua';
    const f = fac[g.faction];
    const ctx = { family, virtue: f ? f.virtue : 50, mapScale: MU.get(MU.resolve('map', TEMPLATE_CULTURE[family])).scale };
    let s = null;
    for (let a = 0; a < 64; a++) {
      const c = generate(g, ctx, a);
      const sg = signature(c.motif);
      // distinct from every motif; within the same family also the exact rhythm+contour must differ
      const paletteOk = !rulers.has(g.name) || [...rulers].every(r => !suites[r] || suites[r].family !== family ||
        inst(family, suites[r].lead) !== inst(family, c.lead) || Math.abs(suites[r].tempo - c.tempo) >= 0.03 || (suites[r].tr || 0) !== (c.tr || 0));
      if (paletteOk && sigs.every(x => distance(sg, x[1]) >= D_MIN)) { s = c; rerolls += a; maxAttempt = Math.max(maxAttempt, a); sigs.push([g.name, sg, family]); break; }
    }
    if (!s) { fails.push('generator could not find a distinct motif for ' + g.name); continue; }
    genStats[s.temper]++;
    suites[g.name] = Object.assign({ family }, s);
  }
  // 3. build + validate: every hand suite, every starting ruler, and a sample of 60 other generals
  const toBuild = Object.keys(suites).filter(n => HAND[n] || rulers.has(n));
  const others = Object.keys(suites).filter(n => !toBuild.includes(n));
  for (let i = 0; i < 60 && others.length; i++) toBuild.push(others[(i * 7919) % others.length]);
  const durs = {}, shares = {};
  let nKeys = 0;
  for (const name of toBuild) {
    const S = suites[name];
    let pieces;
    try { pieces = buildSuite(MU, name, S, OCT); } catch (e) { fails.push(name + ': build threw ' + e.message); continue; }
    for (const [kind, P] of Object.entries(pieces)) {
      const key = 'suite:' + name + ':' + kind;
      for (const v of Object.values(P.voices)) if (!MU.INST[v.inst]) fails.push(key + ': unknown inst ' + v.inst);
      for (const v of Object.values(P.kit || {})) if (!MU.INST[v.inst]) fails.push(key + ': unknown kit inst ' + v.inst);
      MU.add(key, P); nKeys++;
      const err = MU.validate(key);
      if (err && err.length) fails.push(key + ': ' + err.slice(0, 2).join(' / '));
      const d = MU.duration(key);
      (durs[kind] = durs[kind] || []).push(d ? d.total : NaN);
      if (kind === 'map' || kind === 'battle') { const sh = motifShare(P); (shares[kind] = shares[kind] || []).push(sh); if (sh < 0.25) fails.push(key + ` motif-derived bars ${(100 * sh).toFixed(0)} % < 25 %`); }
      if (kind === 'clash' || kind === 'clash-b') { if (!(d.total > 3.0 && d.total < 3.4)) fails.push(key + ' clash length ' + d.total.toFixed(2)); }
      if (kind === 'clash-short' && !(d.total > 1.5 && d.total < 1.7)) fails.push(key + ' short length ' + d.total.toFixed(2));
      if (kind === 'victory' && !(d.total > 12 && d.total < 16)) fails.push(key + ' victory length ' + d.total.toFixed(2));
      if (kind === 'fanfare' && !(d.total > 3.0 && d.total < 3.9)) fails.push(key + ' fanfare length ' + d.total.toFixed(2));
    }
    if (list) console.log((HAND[name] ? 'H ' : 'G ') + name.padEnd(8, '　') + ' ' + S.family.padEnd(7) + ' ' + (S.temper || '').padEnd(11) + ' ' + S.lead.padEnd(7) + ' t' + S.tempo + ' tr' + S.tr + '  ' + S.motif);
  }
  // 4. duel calls: every hand pair of the classic scenario in the han duel template
  {
    const D = 'duel';
    const cls = Object.keys(HAND).slice(0, 6);
    for (let i = 0; i + 1 < cls.length; i += 2) {
      const a = suites[cls[i]], b = suites[cls[i + 1]];
      const P = clone(MU.get(D));
      const sc = scaleInfo(P.scale); const tAbs = tonicAbsOf(P);
      const ra = render(parseMotif(a.motif).map(n => respellNote(n, sc)), 4), rb = render(parseMotif(b.motif).map(n => respellNote(n, sc)), 4);
      P.voices.MaL = { inst: inst(a.family, a.lead === 'pluck4' ? 'reed' : a.lead), oct: OCT[inst(a.family, a.lead)] || 0, pan: -0.3 };
      P.voices.MbL = { inst: inst(b.family, b.lead === 'pluck4' ? 'reed' : b.lead), oct: OCT[inst(b.family, b.lead)] || 0, pan: 0.3 };
      P.voices.mB = { inst: 'lowstr', oct: -2 };
      P.sections.Ma = { MaL: '!f ' + ra.str, mB: droneBars(ra.bars, 4, tAbs - 12, false) };
      P.sections.Mb = { MbL: '!f ' + rb.str, mB: droneBars(rb.bars, 4, tAbs - 12, false) };
      const ia = P.form.indexOf('A'); P.form.splice(ia, 0, 'Ma'); P.form.splice(P.form.indexOf('B'), 0, 'Mb'); P.loopFrom = 'Ma';
      const key = 'suite-duel:' + cls[i] + '|' + cls[i + 1];
      MU.add(key, P); nKeys++;
      const err = MU.validate(key); if (err && err.length) fails.push(key + ': ' + err.slice(0, 2).join(' / '));
    }
  }
  // 4b. palette rule for the 70 starting rulers (§A4.5): besides the motif, every pair of the same family differs in
  //     at least one of lead instrument, tempo (≥ 3 %) or key
  {
    const rs = [...rulers].filter(n => suites[n]);
    let pairs = 0, same = 0;
    for (let i = 0; i < rs.length; i++) for (let j = i + 1; j < rs.length; j++) {
      const a = suites[rs[i]], b = suites[rs[j]];
      if (a.family !== b.family) continue;
      pairs++;
      const differs = inst(a.family, a.lead) !== inst(b.family, b.lead) || Math.abs(a.tempo - b.tempo) >= 0.03 || (a.tr || 0) !== (b.tr || 0);
      if (!differs) { same++; fails.push(`palette identical: ${rs[i]} ~ ${rs[j]}`); }
    }
    console.log(`starting rulers: ${rs.length}; same-family pairs ${pairs}, palette-identical ${same}`);
  }
  // 5. report
  const all = Object.keys(suites).length;
  let minD = 99, minPair = null;
  for (let i = 0; i < sigs.length; i++) for (let j = i + 1; j < sigs.length; j++) {
    const a = sigs[i][0], b = sigs[j][0];
    if ((HAND[a] && HAND[a].house === b) || (HAND[b] && HAND[b].house === a)) continue;
    const d = distance(sigs[i][1], sigs[j][1]); if (d < minD) { minD = d; minPair = a + ' ~ ' + b; }
  }
  console.log(`suites: ${all} (${Object.keys(HAND).length} hand-composed, ${all - Object.keys(HAND).length} generated: martial ${genStats.martial} / scholar ${genStats.scholar} / statesman ${genStats.statesman})`);
  console.log(`distinctness: min distance ${minD} (${minPair}); threshold ${D_MIN}; generator re-rolls total ${rerolls}, worst ${maxAttempt}`);
  console.log(`built ${toBuild.length} suites → ${nKeys} pieces validated`);
  for (const [k, a] of Object.entries(durs)) { const v = a.filter(x => !isNaN(x)); console.log(`  ${k.padEnd(14)} n=${String(v.length).padStart(3)}  ${Math.min(...v).toFixed(1)}–${Math.max(...v).toFixed(1)} s`); }
  for (const [k, a] of Object.entries(shares)) console.log(`  motif-derived share of the ${k} loop: min ${(100 * Math.min(...a)).toFixed(0)} %, median ${(100 * a.slice().sort((x, y) => x - y)[a.length >> 1]).toFixed(0)} % (target ≥ 25 %)`);
  console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 40).join('\n  ') + (fails.length > 40 ? `\n  … ${fails.length - 40} more` : '') : 'ALL OK');
  process.exitCode = fails.length ? 1 : 0;
  return { suites, fails };
}

module.exports = { HAND, FAMILY_OF, TEMPLATE_CULTURE, HUA, parseMotif, respellNote, seqDeg, invDeg, scaleInfo, render, generate, signature, distance, buildSuite, temperOf, motifShare, check };
if (require.main === module) check();
