'use strict';
/* ==========================================================================
   DOMESTIC-FX.md §8 reference prototype: the command stings (指令短乐), in the js/music.js notation.
   The implementation copies STINGS / build() into js/music-stings.js (loaded after music-han.js);
   audio.js plays them as overlays (SG.Sfx.cmdSting, DOMESTIC-FX.md §8.4).

   Every sting is one small piece with sections:
     A  the action (2 bars for deterministic commands, 1 bar for chance commands: the attempt)
     N  normal outcome tag (chance commands only, 1 bar)
     G  good tag          (deterministic: 1 bar after A; chance: 2 bars)
     X  great tag         (2 bars; talent / legendary moments 3 bars) — fuller orchestra, gong / bells ring out
     M  miss              (1 bar, gentle, never punishing)
   Registered keys: 'sting:<id>' (normal) · 'sting:<id>:good' · 'sting:<id>:great' · 'sting:<id>:miss'
   and per culture family 'sting:<id>[:tier]@<family>' (instrument roles and scale colour, §8.3).

   node design-v4/domestic/stings.js   → validates every key (all 12 families), prints durations, checks the §8 targets
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

// ------------------------------------------------------------ instrument roles --
// role of each instrument used below → per family substitute (all names exist in js/music-inst.js)
const ROLE = {
  dizi: 'flute', xiao: 'flute2', erhu: 'bow', zheng: 'pluck', pipa: 'pluck', yangqin: 'pluck2', ruan: 'pluck3', qin: 'pluck4',
  bianzhong: 'bell', suona: 'reed', horns: 'brass', horn: 'brass1', sheng: 'pad', choir: 'choir', choirOo: 'choir2', strings: 'strings',
  lowstr: 'bass', pizz: 'pizz', timpani: 'timp', stab: 'stab',
  dagu: 'drum', tanggu: 'drum2', muyu: 'wood', bangzi: 'wood2', ban: 'wood3', luo: 'gong', xiaoluo: 'gong2', bo: 'cymbal', tamtam: 'tamtam',
};
const FAMILY_INST = {
  hua: {},
  han3: { flute: 'daegeum', flute2: 'daegeum', bow: 'haegeum', pluck: 'gayageum', pluck2: 'gayageum', pluck4: 'gayageum', reed: 'piri', drum: 'buk', drum2: 'janggu', gong: 'jing', gong2: 'kkwaeng', cymbal: 'kkwaeng' },
  wa: { flute: 'shinobue', flute2: 'shakuhachi', bow: 'shakuhachi', pluck: 'koto', pluck2: 'koto', pluck3: 'koto', pluck4: 'koto', reed: 'shinobue', drum: 'taiko', drum2: 'taiko' },
  steppe: { flute: 'whistle', flute2: 'overtone', bow: 'morin', pluck: 'dombra', pluck2: 'dombra', pluck3: 'dombra', pluck4: 'dombra', pad: 'kargyraa', drum: 'shamandrum', drum2: 'shamandrum', wood: 'hoof', wood2: 'hoof' },
  oasis: { flute: 'ney', flute2: 'ney', bow: 'kamancheh', pluck2: 'santur', pluck4: 'oud', pad: 'drones', drum: 'daf', drum2: 'darbuka', wood: 'darbuka' },
  indic: { flute: 'bansuri', flute2: 'bansuri', bow: 'bansuri', pluck: 'sitar', pluck2: 'sitar', pluck3: 'sitar', pluck4: 'sitar', pad: 'drones', drum: 'bayan', drum2: 'dayan', wood: 'dayan', bell: 'gender' },
  persia: { flute: 'ney', flute2: 'ney', bow: 'kamancheh', pluck: 'santur', pluck2: 'santur', pluck3: 'oud', pluck4: 'oud', reed: 'zurna', pad: 'drones', drum: 'daf', drum2: 'daf', wood: 'darbuka' },
  arabia: { flute: 'ney', flute2: 'ney', bow: 'kamancheh', pluck: 'oud', pluck2: 'qanun', pluck3: 'oud', pluck4: 'oud', reed: 'zurna', pad: 'drones', drum: 'darbuka', drum2: 'daf', wood: 'darbuka', cymbal: 'crotala' },
  nile: { flute: 'ney', flute2: 'ney', bow: 'ney', pluck: 'harp', pluck2: 'harp', pluck3: 'harp', pluck4: 'harp', reed: 'aulos', pad: 'drones', drum: 'frame', drum2: 'frame', cymbal: 'crotala', gong2: 'crotala' },
  roman: { flute: 'aulos', flute2: 'aulos', bow: 'aulos', pluck: 'harp', pluck2: 'harp', pluck3: 'harp', pluck4: 'harp', reed: 'aulos', brass: 'cornu', brass1: 'cornu', drum: 'wardrum', drum2: 'frame', cymbal: 'crotala', gong2: 'crotala' },
  north: { flute: 'whistle', flute2: 'whistle', bow: 'whistle', pluck: 'harp', pluck2: 'harp', pluck3: 'harp', pluck4: 'harp', reed: 'chanter', brass1: 'cornu', pad: 'drones', drum: 'bodhran', drum2: 'bodhran', wood: 'frame' },
  nanhai: { flute: 'suling', flute2: 'suling', bow: 'suling', pluck: 'saron', pluck2: 'gender', pluck3: 'saron', pluck4: 'gender', bell: 'bonang', drum: 'kendang', drum2: 'kendang', gong: 'kempul', gong2: 'kenong', cymbal: 'kenong', tamtam: 'gongageng' },
};
// scale colour: intervals ABOVE THE STING'S TONIC → regional mode (semitones; only major intervals are bent, so the
// minor (羽) stings keep their shape and every sting keeps its tonic). Applied to note tokens only.
const FAMILY_DEG = {
  hua: null, han3: null, steppe: null, oasis: null, nanhai: null, north: null, nile: null,
  wa: { 2: 1, 4: 5, 9: 8 },        // 都节（In / Miyako-bushi: 1 b2 4 5 b6）
  indic: { 2: 1, 9: 8 },           // Bhairav colour (b2, b6)
  persia: { 2: 1, 4: 3, 9: 8 },    // Shur colour (b2, b3, b6)
  arabia: { 2: 1, 9: 8 },          // Hijaz colour (b2, 3, b6)
  roman: { 4: 3 },                 // Dorian colour (b3)
};
const DEG_SEMI = [0, 0, 2, 4, 5, 7, 9, 11];
const SPELL = ['1', 'b2', '2', 'b3', '3', '4', '#4', '5', 'b6', '6', 'b7', '7'];
function absOf(acc, d, marks) {
  let o = 0; for (const ch of marks) o += ch === "'" ? 1 : -1;
  return 12 * o + DEG_SEMI[+d] + (acc === '#' ? 1 : acc === 'b' ? -1 : 0);
}
function spell(abs) {
  const o = Math.floor(abs / 12), pc = ((abs % 12) + 12) % 12;
  return SPELL[pc] + (o > 0 ? "'".repeat(o) : ','.repeat(-o));
}
// rewrite each unaccidented note token d[',]* whose interval above `tonic` (e.g. '6,') is in map
function remapDegrees(s, map, tonic) {
  if (!map || typeof s !== 'string') return s;
  const tm = /^([#b]?)([1-7])([',]*)$/.exec(tonic || '1');
  const t = tm ? absOf(tm[1], tm[2], tm[3]) : 0;
  return s.replace(/(^|[\s|+])([#b]?)([1-7])([',]*)(?=[_.:~*^v\/\\><)@&t\-+\s|]|$)/g, (m, pre, acc, d, marks) => {
    if (acc) return m;                         // keep explicit accidentals (清角、变宫 in 离间)
    const a = absOf('', d, marks);
    const rel = ((a - t) % 12 + 12) % 12;
    if (map[rel] === undefined) return m;
    return pre + spell(a + (map[rel] - rel));
  });
}

// ------------------------------------------------------------ the stings --
// voices: name → [inst, oct, gain?, pan?]; kit: letter → [inst, art?]; sections: { A, G, X, N, M } with voice strings and k (drum tracks)
const STINGS = {
  // ---- 开发 · 土地：徵调式，田园，上行的「生长」动机 5,-6,-1-2-3 ------------------------------
  'develop.land': {
    title: '开垦', key: 'C', bpm: 116, meter: 2, scale: '5, 6, 1 2 3', tonic: '5,',
    voices: { di: ['dizi', 1], zh: ['zheng', -1, 0.9, -0.3], sh: ['sheng', 0, 0.7], bl: ['bianzhong', 0, 0.8, 0.3], co: ['choirOo', -1, 0.7], bs: ['lowstr', -2, 0.8] },
    kit: { W: ['muyu'], D: ['dagu', 0], C: ['bo'], G: ['luo'] },
    A: { di: '!mp 5,_ 6,_ 1_ 2_ | 3_ 2_ 5~ |', zh: '5,_ 2_ 5_ 2_ | 1_ 5_ 1\' |', sh: '0:2 | !p 5,+2+5:2 |', bs: '5,:2 | 5,:2 |', k: { W: 'x...x.x. x...x...', D: '........ o.......' } },
    G: { di: '!mf 6_ 1\'_ 5:1~ |', zh: '5_ 6_ 1\'_ 2\'_ |', bl: '5:2 |', sh: '5,+2+5:2 |', bs: '5,:2 |', k: { C: 'X.......', D: 'X.......', W: '....x.x.' } },
    X: { di: '!f 1\'_ 2\'_ 3\'_ 5\'_ | 6\'_ 5\'_ 5\':1* |', zh: '5_ 1\'_ 2\'_ 3\'_ | 5\'@:2 |', bl: '5\':1 3\' | 5+2\':2 |', sh: '!mf 1+3+5:2 | 5,+2+5:2 |', co: '0:2 | !mf 5+2\':2 |', bs: '1,:2 | 5,:2 |', k: { D: 'X...x.x. X.......', C: '........ X.......', G: '........ x.......' } },
  },
  // ---- 开发 · 产业：商调式，铁砧般的切分（梆子打在拍上）----------------------------------------
  'develop.industry': {
    title: '冶铸', key: 'C', bpm: 126, meter: 2, scale: '2 3 5 6 1\'', tonic: '2',
    voices: { pp: ['pipa', 0], su: ['suona', 0, 0.85, 0.1], lo: ['lowstr', -2, 0.9], hn: ['horns', -1, 0.8, -0.2] },
    kit: { A: ['bangzi'], L: ['xiaoluo'], D: ['dagu', 0], T: ['tamtam'] },
    A: { pp: '!mf 2_ 2_ 5_ 2\'_ | 1\'_ 6_ 2:1 |', lo: '2,_ 0_ 2,_ 0_ | 2,_ 0_ 2,:1 |', k: { A: 'X...x.X. x.x.X...', L: '........ ......x.' } },
    G: { pp: '!f 5_ 1\'_ 2\':1* |', hn: '2+6:2 |', lo: '2,:2 |', k: { A: 'X.x.X.x.', D: 'X.......', L: '....x...' } },
    X: { su: '!f 2\'_ 1\'_ 6_ 5_ | 6_ 1\'_ 2\':1 |', pp: '2_ 5_ 6_ 1\'_ | 2+6+2\':2 |', hn: '!mf 2+6:2 | !f 2+6+2\':2 |', lo: '2,:2 | 2,:2 |', k: { D: 'X...X.x. X.......', A: 'x.x.x.x. X.......', T: '........ X.......' } },
  },
  // ---- 开发 · 町：宫调式，市声喧闹（扬琴碎音 + 木鱼）------------------------------------------
  'develop.town': {
    title: '兴市', key: 'C', bpm: 120, meter: 2, scale: '1 2 3 5 6', tonic: '1',
    voices: { yq: ['yangqin', 0], di: ['dizi', 1, 0.8, 0.2], ru: ['ruan', -1, 0.8, -0.25], sh: ['sheng', 0, 0.7], su: ['suona', 0, 0.8] },
    kit: { W: ['muyu'], L: ['xiaoluo'], C: ['bo'], D: ['tanggu', 0] },
    A: { yq: '!mf 1_ 3_ 5_ 3_ | 6_ 5_ 3_ 1_ |', di: '0:2 | 6_ 5_ 1\':1 |', ru: '1,_ 5,_ 1_ 5,_ | 1,_ 5,_ 1:1 |', k: { W: 'x.x.x.x. x.x.x...', D: 'x...x... x...x...' } },
    G: { di: '!f 2\'_ 3\'_ 1\':1 |', yq: '5_ 6_ 1\'_ 1\'_ |', sh: '1+3+5:2 |', k: { L: 'x.x.X...', C: '....X...' } },
    X: { su: '!f 5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', yq: '1_ 3_ 5_ 1\'_ | 1+3+5+1\':2 |', di: '!f 1\'_ 3\'_ 5\'_ 3\'_ | 1\':2 |', sh: '1+3+5:2 | 1+3+5:2 |', k: { L: 'x.x.x.x. X.......', C: 'X....... X.......', D: 'X.x.X.x. X.......', W: 'x.x.x.x. ........' } },
  },
  // ---- 征兵：羽调式，募兵的鼓与号（号角先呼，唢呐 6,-3-6 应答）--------------------------------
  recruit: {
    title: '募兵', key: 'C', bpm: 132, meter: 2, scale: '6, 1 2 3 5', tonic: '6,',
    voices: { hn: ['horns', -1], su: ['suona', 0, 0.9, 0.1], tp: ['timpani', -2], ch: ['choir', -1, 0.8] },
    kit: { D: ['dagu', 0], t: ['tanggu', 0], C: ['bo'], G: ['luo'] },
    A: { hn: '!mf 6,+3:1 6,+3_ 6,+3_ | !f 2+6 3+6 |', su: '0:2 | 6,_ 3_ 6:1 |', tp: '6,_ 6,_ 6,_ 6,_ | 6,:1 6, |', k: { D: 'X...x.x. X.x.X...', t: '....o.o. ....rrrr' } },
    G: { su: '!f 3_ 6_ 6\':1~ |', hn: '6,+3+6:2 |', tp: '6,:2 |', k: { D: 'X.......', C: 'X.......', G: '....x...' } },
    X: { su: '!f 1\'_ 6_ 3_ 6_ | 1\'_ 2\'_ 3\':1 |', hn: '!f 3+6:1 5+1\' | !ff 6,+3+6:2 |', ch: '0:2 | !f 6,+3+6:2 |', tp: '6,_ 6,_ 6,_ 6,_ | 6,:2 |', k: { D: 'X.x.X.x. X.......', t: 'xxxxrrrr ........', C: '........ X.......', G: '........ X.......' } },
  },
  // ---- 训练：角调式，操练口令（二胡跳弓 3-3-5 / 3-6-5，钹打后半拍）-----------------------------
  train: {
    title: '操练', key: 'C', bpm: 132, meter: 2, scale: '3 5 6 1\' 2\'', tonic: '3',
    voices: { eh: ['erhu', 0], di: ['dizi', 1, 0.8, 0.2], hn: ['horns', -1, 0.8], bs: ['lowstr', -2, 0.9], ch: ['choir', -1, 0.75] },
    kit: { t: ['tanggu', 0], C: ['bo'], B: ['ban'], D: ['dagu', 0] },
    A: { eh: '!f 3_ 3_ 5 | 3_ 6_ 5 |', bs: '3,_ 0_ 3,_ 0_ | 3,_ 0_ 6,_ 0_ |', k: { t: 'x.x.x.x. x.x.x.x.', C: '..x...x. ..x...x.', B: 'x....... x.......' } },
    G: { eh: '1\'_ 6_ 3:1 |', di: '3\'_ 5\'_ 3\':1 |', hn: '3+6:2 |', k: { D: 'X...x...', C: 'X.......' } },
    X: { eh: '!ff 3_ 5_ 6_ 1\'_ | 2\'_ 1\'_ 3\':1 |', di: '!f 3\'_ 5\'_ 6\'_ 5\'_ | 3\':2 |', hn: '!f 3+6:2 | 3+6+3\':2 |', ch: '0:2 | !f 3+6:1 0 |', k: { D: 'X.x.X.x. X.......', t: 'rrrrrrrr X.......', C: 'X...X... X.......' } },
  },
  // ---- 搜索：羽调式的「寻访」（箫 + 琴），结果段各异；发现人才时转入宫调奏出「霸业」动机 -----------
  search: {
    title: '寻访', key: 'C', bpm: 108, meter: 2, scale: '6, 1 2 3 5', tonic: '6,', chance: true,
    voices: { xi: ['xiao', 0], qn: ['qin', -1, 0.9, -0.2], st: ['strings', -1, 0.7], bl: ['bianzhong', 0, 0.85, 0.25], yq: ['yangqin', 0, 0.85], sh: ['sheng', 0, 0.7], hn: ['horn', 0, 0.9], di: ['dizi', 1, 0.8] },
    kit: { W: ['muyu'], G: ['luo'], D: ['dagu', 0] },
    A: { xi: '!p 6,_ 1_ 3~ |', qn: '6,:1 3 |', st: '!pp 6,+3:2 |', k: { W: 'x.......' } },
    N: { yq: '3_ 5_ 6:1 |', bl: '0 6 |', k: { W: 'x.x.....' } },
    G: { yq: '3_ 5_ 6_ 1\'_ | 2\'_ 3\'_ 6\':1 |', bl: '6:1 3\' | 6+3\':2 |', sh: '0:2 | 6,+3+6:2 |' },
    X: { hn: '!f 5,. 1_ | 2 5 | 5:2 |', di: '0:2 | 0 3\'_ 5\'_ | 1\':2 |', bl: '1+5:2 | 2+5:2 | 1+5+1\':2 |', st: '!mf 1,+5,:2 | 2,+6,:2 | 1,+5,+3:2 |', k: { G: 'X....... ........ X.......', D: '........ ....o.x. X.......' } },
    M: { xi: '2_ 1_ 6,:1\\ |', qn: '6,:2 |', st: '!pp 6,+3:2 |' },
  },
  // ---- 登用：宫调式的迎宾（琴与二胡对答）；大成时号角与合唱奏「霸业」动机 -----------------------
  hire: {
    title: '求贤', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1', chance: true,
    voices: { qn: ['qin', -1, 0.9, -0.2], eh: ['erhu', 0], sh: ['sheng', 0, 0.7], hn: ['horns', -1, 0.85], ch: ['choir', -1, 0.8], bl: ['bianzhong', 0, 0.8, 0.25] },
    kit: { W: ['muyu'], G: ['luo'], D: ['dagu', 0] },
    A: { eh: '!mp 3 5 |', qn: '1_ 5_ 1\'_ 5_ |', k: { W: 'x.......' } },
    N: { eh: '6_ 5_ 1\':1 |', sh: '1+5:2 |' },
    G: { eh: '6_ 1\'_ 2\'_ 3\'_ | 2\'_ 1\'_ 1\':1 |', sh: '1+3+5:2 | 1+3+5:2 |', bl: '0:2 | 1\':2 |', qn: '1_ 5_ 1\'_ 2\'_ | 1\':2 |' },
    X: { hn: '!f 5,. 1_ | 2 5 | 1+5+1\':2 |', ch: '!mf 1+5:2 | 2+5:2 | !f 1+5+1\':2 |', bl: '1+5:2 | 5+2\':2 | 1+5+1\':2 |', eh: '0:2 | 0 3\'_ 5\'_ | 1\':2 |', k: { G: 'X....... ........ X.......', D: 'X....... ....x.x. X.......' } },
    M: { eh: '!mp 3_ 2_ 6,:1\\ |', qn: '6,:2 |' },
  },
  // ---- 移动：商调式，行军步伐（阮的走句 + 木鱼脚步）--------------------------------------------
  move: {
    title: '移驻', key: 'C', bpm: 116, meter: 2, scale: '2 3 5 6 1\'', tonic: '2',
    voices: { ru: ['ruan', -1], pz: ['pizz', -1, 0.9], di: ['dizi', 1, 0.8, 0.2], hn: ['horns', -1, 0.85] },
    kit: { W: ['muyu'], t: ['tanggu', 0], C: ['bo'], D: ['dagu', 0] },
    A: { ru: '2_ 3_ 5_ 6_ | 5_ 3_ 2:1 |', pz: '2,_ 0_ 5,_ 0_ | 2,_ 0_ 2,:1 |', di: '0:2 | 6_ 5_ 2\':1 |', k: { W: 'x.x.x.x. x.x.x.x.', t: 'x...x... x...x...' } },
    G: { di: '3\'_ 5\'_ 2\':1 |', ru: '5_ 6_ 1\'_ 2\'_ |', k: { t: 'x.x.x.x.', C: '....X...' } },
    X: { hn: '!f 2+6 3+6 | 5+1\' 2+6+2\' |', di: '!f 2\'_ 3\'_ 5\'_ 6\'_ | 5\'_ 3\'_ 2\':1 |', ru: '2_ 5_ 2\'_ 5_ | 2\':2 |', k: { t: 'X.x.X.x. X.x.X...', C: '........ X.......', D: 'X....... X.......' } },
  },
  // ---- 输送：徵调式 3 拍子，车轮与牛蹄（木鱼 + 马蹄）---------------------------------------------
  transport: {
    title: '转输', key: 'C', bpm: 168, meter: 3, scale: '5, 6, 1 2 3', tonic: '5,',
    voices: { ru: ['ruan', -1], sh: ['sheng', 0, 0.7], di: ['dizi', 1, 0.8, 0.2], hn: ['horns', -1, 0.85], bs: ['lowstr', -2, 0.8] },
    kit: { W: ['muyu'], H: ['hoof'], C: ['bo'], D: ['dagu', 0], G: ['luo'] },
    A: { ru: '5,:2 6, | 1:2 5, |', sh: '!p 5,+2:3 | 5,+2:3 |', bs: '5,:3 | 5,:3 |', k: { W: 'x...x.x...x. x...x.x...x.', H: 'x.....x..... x.....x.....' } },
    G: { di: '3_ 2_ 1_ 6,_ 5,:1 |', sh: '5,+2+5:3 |', k: { W: 'x...x.x...x.', C: 'X...........' } },
    X: { di: '!f 1_ 2_ 3:2 | 5_ 3_ 5:2 |', hn: '!f 5,+2:3 | 5,+2+5:3 |', ru: '5,_ 1_ 2_ 3_ 5:1 | 5:3 |', k: { D: 'X.....x..... X...........', W: 'x...x.x...x. x...........', G: '............ X...........' } },
  },
  // ---- 外交 · 同盟：宫调式的礼乐（编钟 + 琴）；应答声部由「对方」的地域乐器演奏 ------------------
  ally: {
    title: '会盟', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1', chance: true, answer: ['an'],
    voices: { bl: ['bianzhong', 0], qn: ['qin', -1, 0.9, -0.2], sh: ['sheng', 0, 0.7], an: ['dizi', 1, 0.85, 0.25], st: ['strings', -1, 0.7], ch: ['choirOo', -1, 0.75] },
    kit: { G: ['luo'], D: ['dagu', 0] },
    A: { bl: '!mf 1 5 |', qn: '1,_ 5,_ 1_ 5,_ |' },
    N: { an: '3_ 5_ 1\':1 |', sh: '1+5:2 |' },
    G: { an: '3_ 5_ 6_ 5_ | 6_ 5_ 1\':1 |', bl: '0:2 | 5+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |' },
    X: { bl: '!f 1 3 | 5 6 | 5+1\':2 |', an: '5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\'_ 2\'_ | 1\':2 |', ch: '!mf 1+5:2 | 2+6:2 | 1+5+3\':2 |', st: '1,+5,:2 | 2,+6,:2 | 1,+5,+3:2 |', k: { G: '........ ........ X.......', D: 'X....... ........ X.......' } },
    M: { qn: '3,_ 2,_ 1,:1 |', sh: '!pp 6,+3:2 |' },
  },
  // ---- 外交 · 离间：清角 4 与变宫 7 的阴影，拨弦与木鱼窃窃私语 ------------------------------------
  discord: {
    title: '反间', key: 'C', bpm: 104, meter: 2, scale: '6, 7, 1 2 3 4 5', tonic: '6,', chance: true,
    voices: { pz: ['pizz', 0], xi: ['xiao', 0, 0.8], ls: ['lowstr', -2, 0.9], sb: ['stab', -1, 0.85] },
    kit: { W: ['muyu'], K: ['bangzi'], C: ['bo'], T: ['tamtam'], D: ['dagu', 0] },
    A: { pz: '6,_ 0_ 7,_ 0_ |', xi: '!pp 3_ 4_ 3:1~ |', ls: '!pp 6,:2* |', k: { W: 'x...x...' } },
    N: { pz: '1_ 0_ #4_ 0_ |', ls: '6,:2 |', k: { K: 'X.......' } },
    G: { sb: '!f 6,+#4+3\':1 0 | 0:2 |', ls: '6,:2* | 6,:2 |', xi: '0:2 | 3_ 4_ 3:1 |', k: { K: 'X....... ........', C: 'X....... ........' } },
    X: { sb: '!f 6,+#4+3\':1 0 | !ff 6,+7,+#4+3\':2 |', ls: '!< 6,:2* | !ff 6,:2 |', xi: '3\'_ 4\'_ 3\'_ 7_ | 6:2\\ |', k: { T: '........ X.......', D: 'X.....x. X.......' } },
    M: { pz: '6,_ 0_ 3,:1 |', xi: '7_ 6_ 4_ 3_ |', k: { W: 'x.x.....' } },
  },
  // ---- 外交 · 拉拢：羽调式的劝说转入宫调（二胡 + 琵琶）；献城时号角与锣 ---------------------------
  persuade: {
    title: '说降', key: 'C', bpm: 104, meter: 2, scale: '6, 1 2 3 5', tonic: '6,', chance: true,
    voices: { eh: ['erhu', 0], pp: ['pipa', 0, 0.85, -0.2], hn: ['horns', -1, 0.85], sh: ['sheng', 0, 0.7] },
    kit: { G: ['luo'], D: ['dagu', 0] },
    A: { eh: '!mp 6,_ 1_ 2_ 3_ |', pp: '6,:2 |' },
    N: { eh: '5_ 6_ 1\':1 |', sh: '1+5:2 |' },
    G: { eh: '5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', pp: '1_ 5_ 1\'_ 5_ | 1+5+1\':2 |', sh: '0:2 | 1+5:2 |' },
    X: { hn: '!f 1+5:2 | 1+5+1\':2 |', eh: '5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', pp: '1_ 5_ 1\'_ 5_ | 1+5+1\':2 |', k: { G: '........ X.......', D: 'X....... X.......' } },
    M: { eh: '3_ 2_ 6,:1\\ |', pp: '6,,:2 |' },
  },
  // ---- 出征：羽调式的「霸业」动机（6,. 2_ 3 6 = 霸业 5,. 1_ 2 5 的小调形态），唢呐与号角 ------------
  attack: {
    title: '出师', key: 'C', bpm: 126, meter: 2, scale: '6, 1 2 3 5', tonic: '6,',
    voices: { su: ['suona', 0], hn: ['horns', -1], tp: ['timpani', -2], ch: ['choir', -1, 0.8] },
    kit: { D: ['dagu', 0], T: ['tamtam'], C: ['bo'], t: ['tanggu', 0], G: ['luo'] },
    A: { su: '!f 6,. 2_ | 3 6 |', hn: '6,+3:2 | 2+6 3+6 |', tp: '6,_ 6,_ 6,_ 6,_ | 6,:1 6, |', k: { D: 'X.....x. X...X...', t: '....o.o. ....xxxx' } },
    G: { su: '6_ 1\'_ 6:1~ |', hn: '6,+3+6:2 |', k: { D: 'X.......', C: 'X.......', G: '....x...' } },
    X: { su: '!ff 6. 1\'_ | 2\' 3\'~ |', hn: '!f 3+6:2 | !ff 6,+3+6:2 |', ch: '!f 6,+3+6:2 | 6,+3+6:2 |', tp: '6,_ 6,_ 6,_ 6,_ | 6,:2 |', k: { T: 'X....... ........', D: 'X.x.X.x. X.......', C: '........ X.......' } },
  },
  // ---- 赏赐：宫调式下行的「赐予」（扬琴 1'-6-5-3），编钟点缀 ----------------------------------------
  reward: {
    title: '恩赏', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1',
    voices: { yq: ['yangqin', 0], bl: ['bianzhong', 1, 0.75, 0.3], sh: ['sheng', 0, 0.7], st: ['strings', -1, 0.7], co: ['choirOo', -1, 0.7], di: ['dizi', 1, 0.8] },
    kit: { L: ['xiaoluo'], C: ['bo'], W: ['muyu'] },
    A: { yq: '1\'_ 6_ 5_ 3_ | 2_ 3_ 1:1 |', bl: '1\':1 0 | 5:1 0 |', sh: '0:2 | 1+5:2 |', k: { W: 'x....... x.......' } },
    G: { di: '3_ 5_ 1\':1 |', bl: '1+5:2 |', st: '1,+5,+3:2 |' },
    X: { co: '!mf 1+5:2 | 1+5+3\':2 |', st: '!mf 6,+3:2 | 1,+5,+3:2 |', di: '5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', yq: '1_ 3_ 5_ 1\'_ | 1+3+5+1\':2 |', k: { C: '........ X.......', L: 'x.x.x.x. ........' } },
  },
  // ---- 交易：商调式的讨价还价（琵琶问、二胡答，梆子与拍板如算筹）-------------------------------------
  trade: {
    title: '市易', key: 'C', bpm: 124, meter: 2, scale: '2 3 5 6 1\'', tonic: '2',
    voices: { pp: ['pipa', 0, 1, -0.2], eh: ['erhu', 0, 0.9, 0.2], ru: ['ruan', -1, 0.8], hn: ['horns', -1, 0.8] },
    kit: { K: ['bangzi'], B: ['ban'], D: ['dagu', 0] },
    A: { pp: '2_ 5_ 2\'_ 0_ | 0:2 |', eh: '0:2 | 1\'_ 6_ 2:1 |', ru: '2,_ 0_ 2,_ 0_ | 5,_ 0_ 2,:1 |', k: { K: 'x...x... x.x.x...', B: '....x... ......x.' } },
    G: { pp: '5_ 6_ 2\':1 |', k: { B: 'x.x.x.x.' } },
    X: { hn: '!f 2+6 5+1\' | 2+6+2\':2 |', pp: '2_ 3_ 5_ 6_ | 2+6+2\':2 |', eh: '!f 6_ 1\'_ 2\'_ 3\'_ | 2\':2 |', k: { K: 'x.x.x.x. X.......', D: 'X....... X.......' } },
  },
  // ---- 任命：宫调式的庄重（圆号独奏 + 编钟），第二小节末一记「盖印」的大鼓 --------------------------
  appoint: {
    title: '授印', key: 'C', bpm: 108, meter: 2, scale: '1 2 3 5 6', tonic: '1',
    voices: { hn: ['horn', 0], bl: ['bianzhong', 0, 0.85, 0.25], tp: ['timpani', -2], sh: ['sheng', 0, 0.7], ch: ['choir', -1, 0.8] },
    kit: { D: ['dagu', 1], T: ['tamtam'] },
    A: { hn: '!mf 1 3 | 2_ 6,_ 1 |', bl: '1+5:2 | 0 1\' |', tp: '!pp 1,_ 1,_ 1,_ 1,_ | 1,:1 0 |', k: { D: '........ ....X...' } },
    G: { bl: '5+1\':2 |', sh: '1+3+5:2 |', hn: '3_ 2_ 1:1 |' },
    X: { bl: '5+1\':2 |', sh: '1+3+5:2 |', hn: '3_ 2_ 1:1 |', ch: '!f 1+5+1\':2 |', k: { T: 'X.......' } },
  },
  // ---- 市场购入（ITEMS.md §5.5 的揭示画面用；按稀有度分档）---------------------------------------
  market: {
    title: '购宝', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1', chance: true,
    voices: { pp: ['pipa', 0], yq: ['yangqin', 0], bl: ['bianzhong', 0, 0.85], st: ['strings', -1, 0.7], ch: ['choir', -1, 0.8], hn: ['horns', -1, 0.85] },
    kit: { T: ['tamtam'], G: ['luo'], D: ['dagu', 0] },
    A: { pp: '1_ 3_ 5_ 1\'_ |' },
    N: { yq: '5_ 3_ 1:1 |' },
    G: { bl: '1\' 5 | 1+5+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |' },
    X: { ch: '!mf 1+5:2 | 2+6:2 | 1+5+1\':2 |', hn: '!f 5,. 1_ | 2 5 | 1+5+1\':2 |', st: '1,+5,:2 | 2,+6,:2 | 1,+5,+3:2 |', k: { T: 'X....... ........ ........', D: '........ ....x.x. X.......', G: '........ ........ X.......' } },
  },
  // ---- 授官（TITLES.md）：宫调式上行 1-3-5-6（「晋升」），笙与编钟 --------------------------------
  title: {
    title: '册封', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1',
    voices: { sh: ['sheng', 0, 0.8], bl: ['bianzhong', 0], hn: ['horns', -1, 0.85], ch: ['choir', -1, 0.8], qn: ['qin', -1, 0.85] },
    kit: { T: ['tamtam'], D: ['dagu', 0] },
    A: { sh: '!mp 1+5:2 | 2+6:2 |', bl: '1 3 | 5 6 |', qn: '1,_ 5,_ 1_ 3_ | 2_ 6,_ 2:1 |' },
    G: { bl: '1\':2 |', sh: '1+3+5:2 |', hn: '1+5:2 |' },
    X: { bl: '1\':2 |', sh: '1+3+5:2 |', hn: '!f 1+5:2 |', ch: '!f 1+5+1\':2 |', k: { T: 'X.......', D: 'X.......' } },
  },
  // ---- 秋收（七月月末，DOMESTIC-FX §9.1）：徵调式 3 拍子的丰收舞 -----------------------------------
  harvest: {
    title: '秋成', key: 'C', bpm: 168, meter: 3, scale: '5, 6, 1 2 3', tonic: '5,', chance: true, event: true,
    voices: { di: ['dizi', 1], sh: ['sheng', 0, 0.7], yq: ['yangqin', 0, 0.9], su: ['suona', 0, 0.85], bs: ['lowstr', -2, 0.8] },
    kit: { D: ['dagu', 0], C: ['bo'], L: ['xiaoluo'], W: ['muyu'], G: ['luo'] },
    A: { di: '5,_ 6,_ 1:2 | 2_ 3_ 5:2 |', yq: '5,_ 1_ 2_ 3_ 5:1 | 3_ 2_ 1_ 6,_ 5,:1 |', sh: '!p 5,+2:3 | 1+5:3 |', bs: '5,:3 | 1:3 |', k: { D: 'X.....x..... X.....x.....', W: 'x.x.x.x.x.x. x.x.x.x.x.x.' } },
    N: { di: '3_ 2_ 5,:2 |', sh: '5,+2+5:3 |', k: { C: 'X...........' } },
    G: { di: '6_ 5_ 3:2 | 2_ 3_ 5:2 |', yq: '5_ 6_ 1\'_ 2\'_ 3\':1 | 5\':3 |', sh: '!mf 5,+2+5:3 | 5,+2+5:3 |', k: { L: 'x...x...x... X...........', D: 'X.....X..... X...........' } },
    X: { su: '!f 1\'_ 2\'_ 3\':2 | 5\'_ 3\'_ 5\':2 |', di: '6_ 5_ 3:2 | 2_ 3_ 5:2 |', yq: '5_ 6_ 1\'_ 2\'_ 3\':1 | 5\':3 |', sh: '!mf 5,+2+5:3 | 5,+2+5:3 |', k: { L: 'x.x.x.x.x.x. X...........', D: 'X.x.X.x.X.x. X...........', C: 'X........... X...........', G: '............ X...........' } },
    M: { di: '3_ 2_ 6,:2\\ |', sh: '!pp 6,+3:3 |' },
  },
  // ---- 城池升阶（CITY-VIEW §4.4 的庆典；只有大成一档）---------------------------------------------
  stageUp: {
    title: '升格', key: 'C', bpm: 132, meter: 2, scale: '1 2 3 5 6', tonic: '1', only: 'great',
    voices: { su: ['suona', 0], hn: ['horns', -1], bl: ['bianzhong', 0, 0.8] },
    kit: { D: ['dagu', 0], C: ['bo'], G: ['luo'] },
    A: { su: '!f 1_ 2_ 3_ 5_ | 6_ 5_ 3_ 5_ |', hn: '1+5:2 | 2+6:2 |', k: { D: 'X.x.X.x. X.x.X.x.', C: 'X....... ........' } },
    X: { su: '1\':2 |', hn: '1+5+1\':2 |', bl: '1+5+1\':2 |', k: { D: 'X.......', C: 'X.......', G: 'X.......' } },
  },
  // ---- 月初（新的一月）：一声编钟，可选 ---------------------------------------------------------------
  month: {
    title: '朔日', key: 'C', bpm: 96, meter: 2, scale: '1 2 3 5 6', tonic: '1', only: 'normal',
    voices: { bl: ['bianzhong', 0, 0.7], sh: ['sheng', 0, 0.5] },
    kit: {},
    A: { bl: '5 1\' |', sh: '!pp 1+5:2 |' },
  },
};

// forms per tier
function forms(S) {
  if (S.only === 'great') return { great: ['A', 'X'] };
  if (S.only === 'normal') return { normal: ['A'] };
  if (S.chance) {
    const f = { normal: ['A', 'N'], good: ['A', 'G'], great: ['A', 'X'] };
    if (S.M) f.miss = ['A', 'M'];
    if (!S.N) f.normal = ['A', 'G'];          // e.g. persuade has no plain success
    return f;
  }
  const f = { normal: ['A'], good: ['A', 'G'], great: ['A', 'X'] };
  if (S.M) f.miss = ['A', 'M'];
  return f;
}

// → { key: piece } for one sting, one family
function build(id, family, answerFamily) {
  const S = STINGS[id];
  const fam = family || 'hua';
  const instMap = FAMILY_INST[fam] || {};
  const ansMap = FAMILY_INST[answerFamily || fam] || {};
  const deg = FAMILY_DEG[fam];
  const voices = {};
  for (const [v, a] of Object.entries(S.voices)) {
    const role = ROLE[a[0]];
    const isAns = (S.answer || []).includes(v);
    const inst = (isAns ? ansMap[role] : instMap[role]) || a[0];
    voices[v] = { inst, oct: a[1], gain: a[2] === undefined ? 1 : a[2], pan: a[3] || 0 };
  }
  const kit = {};
  for (const [k, a] of Object.entries(S.kit)) {
    const inst = instMap[ROLE[a[0]]] || a[0];
    kit[k] = a[1] !== undefined && inst === a[0] ? { inst, art: a[1] } : { inst };
  }
  const pats = {}, sections = {};
  for (const sec of ['A', 'N', 'G', 'X', 'M']) {
    if (!S[sec]) continue;
    const o = {};
    for (const [v, s] of Object.entries(S[sec])) if (v !== 'k') o[v] = remapDegrees(s, deg, S.tonic);
    if (S[sec].k && Object.keys(S[sec].k).length) { pats['p' + sec] = S[sec].k; o.dr = 'p' + sec; }
    sections[sec] = o;
  }
  const out = {};
  const suffix = fam === 'hua' ? '' : '@' + fam;
  for (const [tierName, form] of Object.entries(forms(S))) {
    const key = 'sting:' + id + (tierName === 'normal' ? '' : ':' + tierName) + suffix;
    out[key] = {
      title: S.title + (tierName === 'normal' ? '' : '·' + tierName), key: S.key, bpm: S.bpm, meter: S.meter,
      scale: remapDegrees(S.scale, deg, S.tonic), tonic: S.tonic, loop: false, gain: 0.85, voices, kit, pats, sections, form,
    };
  }
  return out;
}

// ------------------------------------------------------------ Node check --
function check() {
  global.window = global;
  const root = path.join(__dirname, '..', '..', 'js');
  const load = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
  for (const f of ['core.js', 'music.js', 'music-inst.js', 'music-han.js']) load(f);
  const MU = global.SG.Music;
  const fails = [];
  const rows = [];
  const RANGE = { normal: [1.7, 2.7], good: [2.6, 3.6], great: [3.0, 4.5], miss: [1.6, 2.6] };
  let nKeys = 0;
  for (const id of Object.keys(STINGS)) {
    for (const fam of ['hua', 'han3', 'wa', 'steppe', 'oasis', 'indic', 'persia', 'arabia', 'nile', 'roman', 'north', 'nanhai']) {
      const pieces = build(id, fam, fam === 'hua' ? 'roman' : 'hua');
      for (const [key, P] of Object.entries(pieces)) {
        for (const v of Object.values(P.voices)) if (!MU.INST[v.inst]) fails.push(key + ': unknown inst ' + v.inst);
        for (const v of Object.values(P.kit)) if (!MU.INST[v.inst]) fails.push(key + ': unknown kit inst ' + v.inst);
        MU.add(key, P);
        nKeys++;
        const err = MU.validate(key);
        if (err && err.length) fails.push(key + ': ' + err.slice(0, 3).join(' / '));
        if (fam === 'hua') {
          const d = MU.duration(key);
          const tierName = key.split(':')[2] || 'normal';
          const sec = d ? d.total : NaN;
          rows.push([key, sec]);
          const [lo, hi] = RANGE[tierName];
          if (STINGS[id].event) { if (!(sec <= 4.5)) fails.push(key + ' event sting over 4.5 s'); }
          else if (!(sec >= lo && sec <= hi) && !STINGS[id].only) fails.push(key + ` duration ${sec.toFixed(2)} s outside ${lo}–${hi}`);
        }
      }
    }
  }
  // monotone: normal ≤ good ≤ great (notated length) for every sting
  const dur = Object.fromEntries(rows);
  for (const id of Object.keys(STINGS)) {
    const n = dur['sting:' + id], g = dur['sting:' + id + ':good'], x = dur['sting:' + id + ':great'];
    if (n && g && g < n) fails.push(id + ' good shorter than normal');
    if (g && x && x < g - 1e-6) fails.push(id + ' great shorter than good');
  }
  // the A-section motifs must be distinct (first 4 pitch tokens of the lead voice)
  const seen = new Map();
  for (const [id, S] of Object.entries(STINGS)) {
    const lead = Object.values(S.A).find(s => typeof s === 'string' && /[1-7]/.test(s.replace(/![a-z<>]+/g, '').replace(/0:\d/g, '')));
    const sig = (lead || '').replace(/![a-z<>]+/g, '').split(/[\s|]+/).filter(t => /^[#b]?[1-7]/.test(t)).slice(0, 4).join(' ');
    if (seen.has(sig) && sig) fails.push(`motif of ${id} equals ${seen.get(sig)}`);
    seen.set(sig, id);
  }
  for (const [k, s] of rows) console.log(k.padEnd(34) + s.toFixed(2) + ' s');
  console.log(`${nKeys} keys (${Object.keys(STINGS).length} stings × tiers × 12 families) validated`);
  console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 40).join('\n  ') : 'ALL OK');
  process.exitCode = fails.length ? 1 : 0;
}

module.exports = { STINGS, ROLE, FAMILY_INST, FAMILY_DEG, remapDegrees, build, forms };
if (require.main === module) check();
