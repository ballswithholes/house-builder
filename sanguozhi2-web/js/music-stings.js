'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 指令短乐（DOMESTIC-FX §8；INTEGRATION §10.3）
   34 段短乐（21 段指令与时刻 + ENGAGEMENT 增补的 13 段），简谱记法同 js/music.js。
   数据（STINGS、ROLE、FAMILY_INST、FAMILY_DEG、remapDegrees）逐字照搬 design-v4/domestic/stings.js（规格的参照原型；
   tests/music-stings.js 断言两边一致），build() 另给有音高的打击乐补上 note（kempul、kenong、gongageng、dayan …
   在鼓谱轨上没有音高会算出 NaN，与 music-suites.js 的 PITCHED_KIT 相同）。

   每段短乐分节：A 动作（确定的指令 2 小节，机会类指令 1 小节 = 尝试）· N 普通（机会类 1 小节）· G 好 · X 大成 · M 失手。
   形式：normal = A（或 A N）· good = A G · great = A X · miss = A M。
   登记的曲目键：'sting:<id>'（普通）· 'sting:<id>:good' · 'sting:<id>:great' · 'sting:<id>:miss'，
   各文化家族另有 'sting:<id>[:tier]@<family>'（乐器角色与调式色彩，§8.3）。载入时登记全部 12 个家族
   （34 × 档 × 12 = 1 284 个键；只存对象，编译与采样都在第一次播放时才做）。
   「快速」演出只奏尾段（§3.5）：'sting:<id>~tag[:tier][@family]'（只含 G / X / M / N 一节；普通档为 A 节），
   由 tagKey() 按需派生（_derived，不列入 SG.Music.keys()）。SG.Sfx.cmdSting(id + '~tag', tier) 即可播放。

   SG.MusicStings.ids                                → 34 个编号
   SG.MusicStings.FAMILIES                           → 12 个文化家族
   SG.MusicStings.key(id, tier?, family?, answerFamily?) → 已登记的键（缺的家族 / 应答家族当场生成并登记）
   SG.MusicStings.tagKey(id, tier?, family?)         → 只含尾段的派生键（快速档）
   SG.MusicStings.sections(key)                      → { A: 秒, tag: 秒, total, form }（按曲目的 bpm 与节拍算，不需要编译）
   SG.MusicStings.forms(id) / build(id, family, answerFamily) / registerAll() / STINGS / ROLE / FAMILY_INST / FAMILY_DEG / remapDegrees
   SG.MusicStings.familyOf(culture)                  → 文化代号（18 种）→ 家族（CITY-VIEW §3.1）
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = (W.SG = W.SG || {});

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
    // ================= v4 addendum (DOMESTIC-FX §5.18–§5.28): ENGAGEMENT's commands and moments =================
    // ---- 营建：宫调式，夯土与锤声（阮的「起屋」动机 1-1-5,-1，梆子如锤），落成时唢呐与锣 ---------------
    build: {
      title: '营建', key: 'C', bpm: 120, meter: 2, scale: '1 2 3 5 6', tonic: '1',
      voices: { ru: ['ruan', -1], di: ['dizi', 1, 0.8, 0.2], sh: ['sheng', 0, 0.7], su: ['suona', 0, 0.85], hn: ['horns', -1, 0.85], bs: ['lowstr', -2, 0.8] },
      kit: { K: ['bangzi'], W: ['muyu'], D: ['dagu', 0], G: ['luo'] },
      A: { ru: '!mf 1_ 1_ 5,_ 1_ | 2_ 3_ 5:1 |', bs: '1,:2 | 5,:2 |', k: { K: 'x.x.x... x.x.x...', W: '....x... ....x...' } },
      G: { di: '!f 6_ 5_ 1\':1 |', sh: '1+3+5:2 |', k: { D: 'X.......' } },
      X: { su: '!f 1\'_ 6_ 5_ 6_ | 1\'_ 2\'_ 1\':1 |', hn: '!mf 1+5:2 | !f 1+5+1\':2 |', bs: '1,:2 | 1,:2 |', k: { D: 'X...x.x. X.......', G: '........ X.......', K: 'x.x.x.x. ........' } },
    },
    // ---- 通商：商调式 3 拍子，驼铃与蹄声（琵琶 2-3-5-6 的长短步）------------------------------------
    caravan: {
      title: '通商', key: 'C', bpm: 162, meter: 3, scale: '2 3 5 6 1\'', tonic: '2',
      voices: { pp: ['pipa', 0], ru: ['ruan', -1, 0.85, -0.2], di: ['dizi', 1, 0.8, 0.2], hn: ['horns', -1, 0.85], bs: ['lowstr', -2, 0.8] },
      kit: { H: ['hoof'], W: ['muyu'], C: ['bo'], D: ['dagu', 0], G: ['luo'] },
      A: { pp: '!mf 2:2 3 | 5_ 6_ 5:2 |', ru: '2,:2 5, | 2,:3 |', bs: '2,:3 | 2,:3 |', k: { H: 'x.....x..... x.....x.....', W: '....x.....x. ....x.....x.' } },
      G: { di: '!f 6_ 1\'_ 2\':2 |', ru: '2_ 5_ 6_ 1\'_ 2\':1 |', k: { C: 'X...........' } },
      X: { hn: '!f 2+6:3 | 2+6+2\':3 |', di: '!f 2\'_ 3\'_ 5\':2 | 6\'_ 5\'_ 2\':2 |', pp: '2_ 5_ 6_ 5_ 2\':1 | 2+6+2\':3 |', bs: '2,:3 | 2,:3 |', k: { D: 'X.....x..... X...........', G: '............ X...........', H: 'x...x...x... ............' } },
    },
    // ---- 探索：羽调式的远行（箫 6,-3-2-1 下行如山路），发现古物时号角与锣 ------------------------------
    explore: {
      title: '探索', key: 'C', bpm: 100, meter: 2, scale: '6, 1 2 3 5', tonic: '6,', chance: true,
      voices: { xi: ['xiao', 0], qn: ['qin', -1, 0.9, -0.2], st: ['strings', -1, 0.7], bl: ['bianzhong', 0, 0.85, 0.25], hn: ['horn', 0, 0.9], di: ['dizi', 1, 0.8] },
      kit: { W: ['muyu'], G: ['luo'], D: ['dagu', 0], T: ['tamtam'] },
      A: { xi: '!p 6,_ 3_ 2_ 1_ |', qn: '6,:2 |', st: '!pp 6,+3:2 |', k: { W: 'x...x...' } },
      N: { bl: '3 6 |', qn: '6,_ 1_ 3:1 |' },
      G: { di: '3\'_ 2\'_ 6:1 | 1\'_ 2\'_ 3\':1 |', bl: '6:1 3\' | 6+3\':2 |', st: '6,+3:2 | 1+3+6:2 |' },
      X: { hn: '!f 6,. 3_ | 6:2 |', di: '0:2 | 3\'_ 5\'_ 6\':1 |', bl: '6+3\':2 | 6+3\'+6\':2 |', st: '!mf 6,+3:2 | 6,+3+6:2 |', k: { T: 'X....... ........', D: '....x.x. X.......', G: '........ X.......' } },
      M: { xi: '2_ 1_ 6,:1 |', qn: '6,:2 |' },
    },
    // ---- 外交 · 赠礼：宫调式，扬琴捧出的礼盒 5-6-1'-6 ------------------------------------------------------
    gift: {
      title: '赠礼', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1',
      voices: { yq: ['yangqin', 0], bl: ['bianzhong', 0, 0.8, 0.3], sh: ['sheng', 0, 0.7], eh: ['erhu', 0, 0.85, -0.2], st: ['strings', -1, 0.7] },
      kit: { L: ['xiaoluo'], W: ['muyu'], C: ['bo'] },
      A: { yq: '!mf 5_ 6_ 1\'_ 6_ | 5_ 3_ 5:1 |', bl: '0:2 | 1\':1 0 |', k: { W: 'x....... x.......' } },
      G: { eh: '6_ 5_ 1\':1 |', sh: '1+5:2 |' },
      X: { eh: '!mf 3\'_ 2\'_ 1\'_ 6_ | 5_ 6_ 1\':1 |', bl: '1+5:2 | 1+5+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |', k: { C: '........ X.......', L: 'x.x.x.x. ........' } },
    },
    // ---- 外交 · 联姻：宫调式的婚乐（二胡 3-5-6），应答声部由对方家族的乐器演奏 ----------------------------
    marriage: {
      title: '联姻', key: 'C', bpm: 104, meter: 2, scale: '1 2 3 5 6', tonic: '1', chance: true, answer: ['an'],
      voices: { eh: ['erhu', 0], sh: ['sheng', 0, 0.75], bl: ['bianzhong', 0, 0.8, 0.25], an: ['dizi', 1, 0.85, 0.25], ch: ['choirOo', -1, 0.75], st: ['strings', -1, 0.7] },
      kit: { G: ['luo'], D: ['dagu', 0], C: ['bo'] },
      A: { eh: '!mp 3_ 5_ 6 |', sh: '1+5:2 |' },
      N: { an: '5_ 6_ 1\':1 |', bl: '1+5:2 |' },
      G: { an: '1\'_ 2\'_ 3\'_ 5\'_ | 3\'_ 2\'_ 1\':1 |', bl: '1+5:2 | 3+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |' },
      X: { an: '!f 5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', ch: '!mf 1+5:2 | 1+5+3\':2 |', bl: '1 3 | 1+5+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |', k: { G: '........ X.......', D: 'X....... X.......', C: '........ X.......' } },
      M: { eh: '3_ 2_ 1:1 |', sh: '!pp 6,+3:2 |' },
    },
    // ---- 外交 · 纳贡 / 索贡：商调式，琵琶 2-2-1-6, 的进献与定音鼓（索贡被拒为 miss）---------------------------
    tribute: {
      title: '贡赋', key: 'C', bpm: 108, meter: 2, scale: '2 3 5 6 1\'', tonic: '2', chance: true,
      voices: { pp: ['pipa', 0], hn: ['horns', -1, 0.85], tp: ['timpani', -2], bl: ['bianzhong', 0, 0.8], sh: ['sheng', 0, 0.7] },
      kit: { D: ['dagu', 0], C: ['bo'], G: ['luo'] },
      A: { pp: '!mf 2_ 2_ 1_ 6,_ |', tp: '2,_ 2,_ 2,:1 |', k: { D: 'X...x...' } },
      N: { hn: '2+6:2 |', bl: '0 2\' |' },
      G: { hn: '2+6 5+1\' | 2+6+2\':2 |', pp: '2_ 5_ 6_ 1\'_ | 2\':2 |', k: { D: 'X....... X.......' } },
      X: { hn: '!f 2+6:2 | !ff 2+6+2\':2 |', bl: '2\':1 6 | 2+6+2\':2 |', tp: '2,_ 2,_ 2,_ 2,_ | 2,:2 |', pp: '5_ 6_ 1\'_ 2\'_ | 2+6+2\':2 |', k: { D: 'X.x.X.x. X.......', G: '........ X.......', C: '........ X.......' } },
      M: { pp: '1_ 6,_ 2,:1 |', sh: '!pp 2+6:2 |' },
    },
    // ---- 外交 · 质子：羽调式的离别（箫 6,-1-2-1，低音弦），归来时二胡上行 --------------------------------------
    hostage: {
      title: '质子', key: 'C', bpm: 108, meter: 2, scale: '6, 1 2 3 5', tonic: '6,',
      voices: { xi: ['xiao', 0], ls: ['lowstr', -2, 0.85], qn: ['qin', -1, 0.85, -0.2], eh: ['erhu', 0, 0.85], st: ['strings', -1, 0.7], bl: ['bianzhong', 0, 0.8] },
      kit: { W: ['muyu'], G: ['luo'] },
      A: { xi: '!p 6,:1 1_ 2_ | 1_ 6,_ 6,:1 |', ls: '!pp 6,:2 | 3,:2 |', k: { W: '....x... ....x...' } },
      G: { eh: '3_ 5_ 6:1 |', qn: '6,+3:2 |' },
      X: { eh: '!mf 6_ 1\'_ 2\'_ 3\'_ | 2\'_ 1\'_ 6:1 |', st: '6,+3:2 | 6,+3+6:2 |', bl: '0:2 | 6+3\':2 |', k: { G: '........ x.......' } },
    },
    // ---- 外交 · 停战：宫调式的息兵（琴 5,-1-2-1，箫应答），成议时圆号一声 -----------------------------------------
    truce: {
      title: '停战', key: 'C', bpm: 104, meter: 2, scale: '1 2 3 5 6', tonic: '1', chance: true,
      voices: { qn: ['qin', -1, 0.9], sh: ['sheng', 0, 0.75], xi: ['xiao', 0, 0.8, 0.2], hn: ['horn', 0, 0.85], bl: ['bianzhong', 0, 0.8], st: ['strings', -1, 0.7] },
      kit: { G: ['luo'], W: ['muyu'] },
      A: { qn: '!mp 5,_ 1_ 2_ 1_ |', sh: '!pp 1+5:2 |' },
      N: { xi: '3_ 2_ 1:1 |', sh: '1+5:2 |' },
      G: { xi: '3_ 5_ 6:1 | 5_ 3_ 1:1 |', st: '1,+5,:2 | 1,+5,+3:2 |' },
      X: { hn: '!mf 1 5 | 1\':2 |', bl: '1+5:2 | 1+5+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |', xi: '0:2 | 3\'_ 2\'_ 1\':1 |', k: { G: '........ X.......' } },
      M: { qn: '2_ 1_ 6,:1 |', sh: '!pp 6,+3:2 |' },
    },
    // ---- 外交 · 通商协定：商调式，扬琴 2-5-6-5 与算筹般的梆子、拍板 ---------------------------------------------
    tradepact: {
      title: '互市', key: 'C', bpm: 120, meter: 2, scale: '2 3 5 6 1\'', tonic: '2', chance: true,
      voices: { yq: ['yangqin', 0], pp: ['pipa', 0, 0.9, -0.2], eh: ['erhu', 0, 0.85, 0.2], hn: ['horns', -1, 0.8] },
      kit: { K: ['bangzi'], B: ['ban'], C: ['bo'], D: ['dagu', 0] },
      A: { yq: '!mf 2_ 5_ 6_ 5_ |', k: { K: 'x...x.x.', B: '......x.' } },
      N: { pp: '6_ 5_ 2\':1 |', k: { B: 'x.......' } },
      G: { eh: '2\'_ 1\'_ 6_ 5_ | 6_ 1\'_ 2\':1 |', pp: '2_ 6_ 2\'_ 6_ | 2+6+2\':2 |', k: { B: 'x.x.x.x. X.......' } },
      X: { hn: '!f 2+6:2 | 2+6+2\':2 |', eh: '!f 5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 2\':1 |', yq: '2_ 5_ 6_ 2\'_ | 2+6+2\':2 |', k: { K: 'x.x.x.x. X.......', D: 'X....... X.......', C: '........ X.......' } },
      M: { yq: '5_ 3_ 2:1 |', k: { K: 'x.......' } },
    },
    // ---- 归附：宫调式的受降与迎城（唢呐 1-5-1'-6，号角与合唱；只有大成一档）---------------------------------------
    submit: {
      title: '归附', key: 'C', bpm: 112, meter: 2, scale: '1 2 3 5 6', tonic: '1', only: 'great',
      voices: { su: ['suona', 0], hn: ['horns', -1], ch: ['choir', -1, 0.8], bl: ['bianzhong', 0, 0.8] },
      kit: { D: ['dagu', 0], C: ['bo'], G: ['luo'], T: ['tamtam'] },
      A: { su: '!f 1_ 5_ 1\'_ 6_ | 5_ 6_ 1\':1 |', hn: '1+5:2 | 1+5:2 |', k: { D: 'X...x.x. X...x.x.' } },
      X: { su: '2\'_ 1\'_ 6_ 5_ | 1\':2 |', hn: '!f 2+6:2 | !ff 1+5+1\':2 |', ch: '!f 1+5:2 | 1+5+1\':2 |', bl: '1+5:2 | 1+5+1\':2 |', k: { T: 'X....... ........', G: '........ X.......', C: '........ X.......' } },
    },
    // ---- 国策完成（时刻）：宫调式的编钟 1'-5-3-5，「功成」一锤 -----------------------------------------------------
    mission: {
      title: '功成', key: 'C', bpm: 116, meter: 2, scale: '1 2 3 5 6', tonic: '1',
      voices: { bl: ['bianzhong', 0], di: ['dizi', 1, 0.8, 0.2], sh: ['sheng', 0, 0.7], co: ['choirOo', -1, 0.7], st: ['strings', -1, 0.7] },
      kit: { W: ['muyu'], C: ['bo'], G: ['luo'] },
      A: { bl: '!mf 1\' 5 | 3 5 |', sh: '1+5:2 | 1+3+5:2 |' },
      G: { di: '3_ 5_ 1\':1 |', st: '1,+5,+3:2 |', k: { C: 'X.......' } },
      X: { di: '!f 5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', co: '!mf 1+5:2 | 1+5+3\':2 |', bl: '1\':1 5 | 1+5+1\':2 |', st: '1,+5,:2 | 1,+5,+3:2 |', k: { G: '........ X.......', C: '........ X.......' } },
    },
    // ---- 节庆（时刻）：徵调式 3 拍子的舞曲（笛 3-5-6 起舞）；12 个文化家族由 §8.3 的变换配器（从简 = normal，盛大 = great）------
    festival: {
      title: '节庆', key: 'C', bpm: 162, meter: 3, scale: '5, 6, 1 2 3', tonic: '5,', event: true,
      voices: { di: ['dizi', 1], yq: ['yangqin', 0, 0.9], sh: ['sheng', 0, 0.7], su: ['suona', 0, 0.85], bs: ['lowstr', -2, 0.8] },
      kit: { D: ['dagu', 0], C: ['bo'], L: ['xiaoluo'], W: ['muyu'], G: ['luo'] },
      A: { di: '!mf 3_ 5_ 6:2 | 1\'_ 6_ 5:2 |', yq: '5,_ 1_ 2_ 3_ 5:1 | 6_ 5_ 3_ 2_ 1:1 |', bs: '5,:3 | 1:3 |', k: { D: 'X.....x..... X.....x.....', L: '....x.....x. ....x.....x.' } },
      G: { di: '6_ 1\'_ 2\':2 |', sh: '5,+2+5:3 |', k: { C: 'X...........' } },
      X: { su: '!f 5_ 6_ 1\':2 | 2\'_ 3\'_ 5\':2 |', yq: '5_ 6_ 1\'_ 2\'_ 3\':1 | 5\':3 |', sh: '!mf 5,+2+5:3 | 5,+2+5:3 |', k: { D: 'X.x.X.x.X.x. X...........', G: '............ X...........', C: '............ X...........' } },
    },
    // ---- 文会（时刻）：宫调式的清雅（琴 2-1-6,-1，箫应答），第三 / 第二 / 第一名 = normal / good / great ----------------
    wenhui: {
      title: '文会', key: 'C', bpm: 108, meter: 2, scale: '1 2 3 5 6', tonic: '1',
      voices: { qn: ['qin', -1, 0.9, -0.2], xi: ['xiao', 0, 0.85, 0.2], bl: ['bianzhong', 0, 0.8], sh: ['sheng', 0, 0.7], ch: ['choirOo', -1, 0.75] },
      kit: { W: ['muyu'], G: ['luo'] },
      A: { qn: '!mp 2_ 1_ 6,_ 1_ | 2:2 |', xi: '0:2 | 5_ 3_ 2:1 |', k: { W: 'x....... x.......' } },
      G: { xi: '3_ 5_ 1\':1 |', bl: '1+5:2 |' },
      X: { xi: '!mf 5_ 6_ 1\'_ 2\'_ | 3\'_ 2\'_ 1\':1 |', bl: '1 5 | 1+5+1\':2 |', ch: '!mf 1+5:2 | 1+5+3\':2 |', sh: '1+5:2 | 1+3+5:2 |', k: { G: '........ X.......' } },
    },
  };


  // ------------------------------------------------------------ forms per tier --
  function forms(S) {
    if (typeof S === 'string') S = STINGS[S];
    if (!S) return {};
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

  // 有音高的打击乐在鼓谱轨上需要 note（否则引擎没有音高 → NaN；同 music-suites.js）
  const PITCHED_KIT = { kenong: 1, kempul: 1, gongageng: 1, dayan: 1, saron: 1, gender: 1, bonang: 1, bianzhong: 1, timpani: 1 };
  const FAMILIES = Object.freeze(['hua', 'han3', 'wa', 'steppe', 'oasis', 'indic', 'persia', 'arabia', 'nile', 'roman', 'north', 'nanhai']);
  // 18 个文化代号 → 12 个家族（CITY-VIEW §3.1）
  const CULTURE_FAMILY = Object.freeze({
    han: 'hua', nanman: 'hua', korea: 'han3', wa: 'wa', steppe: 'steppe', sarmatian: 'steppe', tarim: 'oasis',
    kushan: 'indic', india: 'indic', persia: 'persia', arab: 'arabia', moor: 'arabia', africa: 'nile', roman: 'roman',
    celt: 'north', german: 'north', seasia: 'nanhai', yi: 'nanhai',
  });
  const familyOf = c => (FAMILIES.indexOf(c) >= 0 ? c : CULTURE_FAMILY[c] || 'hua');

  // → { key: piece } for one sting, one family（应答声部 answer 用 answerFamily 的乐器）
  function build(id, family, answerFamily) {
    const S = STINGS[id];
    if (!S) return {};
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
      const e = a[1] !== undefined && inst === a[0] ? { inst, art: a[1] } : { inst };
      if (PITCHED_KIT[inst]) e.note = S.tonic || '1';
      kit[k] = e;
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
    const ans = answerFamily && answerFamily !== fam && (S.answer || []).length ? '&' + answerFamily : '';
    for (const [tierName, form] of Object.entries(forms(S))) {
      const key = 'sting:' + id + (tierName === 'normal' ? '' : ':' + tierName) + suffix + ans;
      out[key] = {
        title: S.title + (tierName === 'normal' ? '' : '·' + tierName), key: S.key, bpm: S.bpm, meter: S.meter,
        scale: remapDegrees(S.scale, deg, S.tonic), tonic: S.tonic, loop: false, gain: 0.85, voices, kit, pats, sections, form,
        _sting: { id, tier: tierName, family: fam, answer: answerFamily || null },
      };
      if (ans) out[key]._derived = true;
    }
    return out;
  }

  // ------------------------------------------------------------ registration --
  const IDS = Object.freeze(Object.keys(STINGS));
  const builtFam = new Set();   // 'id@fam' 已登记
  let nKeys = 0;
  function Mu() { return SG.Music && typeof SG.Music.add === 'function' ? SG.Music : null; }
  function ensure(id, family, answerFamily) {
    const M = Mu();
    if (!M || !STINGS[id]) return false;
    const fam = familyOf(family || 'hua');
    const tag = id + '@' + fam + (answerFamily ? '&' + answerFamily : '');
    if (builtFam.has(tag)) return true;
    const pieces = build(id, fam, answerFamily && answerFamily !== fam ? answerFamily : null);
    for (const [k, P] of Object.entries(pieces)) { M.add(k, P); nKeys++; }
    try { if (SG.Reg && SG.Reg.stings && !answerFamily && fam === 'hua' && !SG.Reg.stings.has(id)) SG.Reg.stings.def(id, { id, title: STINGS[id].title, forms: forms(STINGS[id]) }); } catch (e) { /* 忽略 */ }
    builtFam.add(tag);
    return true;
  }
  function registerAll() {
    if (!Mu()) return 0;
    const t0 = Date.now();
    for (const id of IDS) for (const fam of FAMILIES) ensure(id, fam);
    MusicStings.registerMs = Date.now() - t0;
    return nKeys;
  }
  const TIERS = { normal: '', good: ':good', great: ':great', miss: ':miss' };
  function key(id, tier, family, answerFamily) {
    const S = STINGS[id];
    if (!S) return null;
    const fam = familyOf(family || 'hua');
    const f = forms(S);
    let t = tier && f[tier] ? tier : f.normal ? 'normal' : Object.keys(f)[0];
    const ans = answerFamily && answerFamily !== fam && (S.answer || []).length ? answerFamily : null;
    ensure(id, fam, ans);
    return 'sting:' + id + (TIERS[t] !== undefined ? TIERS[t] : ':' + t) + (fam === 'hua' ? '' : '@' + fam) + (ans ? '&' + ans : '');
  }
  // 快速档：只奏尾段（normal / miss 的 A 节换成它的 N / M；没有尾段时奏 A）
  function tagKey(id, tier, family) {
    const M = Mu(), S = STINGS[id];
    if (!M || !S) return null;
    const base = key(id, tier, family);
    const P = M.get(base);
    if (!P) return null;
    const fam = familyOf(family || 'hua');
    const tk = 'sting:' + id + '~tag' + (P._sting ? TIERS[P._sting.tier] || '' : '') + (fam === 'hua' ? '' : '@' + fam);
    if (M.get(tk)) return tk;
    const form = P.form.length > 1 ? [P.form[P.form.length - 1]] : P.form.slice();
    const Q = Object.assign({}, P, { form, title: P.title + '·尾', _derived: true, _base: base });
    M.add(tk, Q);
    return tk;
  }
  // 按 bpm 与节拍从记谱推出各节长度（不编译；与 SG.Music.duration 一致：一小节 = meter 拍）
  function barsOf(sec) {
    let n = 0;
    for (const [v, s] of Object.entries(sec || {})) {
      if (v === 'k') { for (const p of Object.values(s)) n = Math.max(n, String(p).split(/\s+/).filter(Boolean).length); continue; }
      if (typeof s !== 'string') continue;
      n = Math.max(n, s.split('|').filter(x => x.trim()).length);
    }
    return n;
  }
  function sections(k) {
    const M = Mu();
    const P = M ? M.get(k) : null;
    const st = P && P._sting ? P._sting : null;
    const S = st ? STINGS[st.id] : null;
    if (!P || !S) return null;
    const bar = (60 / S.bpm) * S.meter;
    const out = { form: P.form.slice(), A: 0, tag: 0, total: 0 };
    for (const n of P.form) {
      const d = barsOf(S[n]) * bar;
      if (n === 'A') out.A += d; else out.tag += d;
      out.total += d;
    }
    // 编译后的精确长度（有引擎时；与上面的估算一致，tests/music-stings.js 断言）
    try { const d = M.duration && M.duration(k); if (d && d.total > 0) out.total = d.total; } catch (e) { /* 忽略 */ }
    return out;
  }

  const MusicStings = {
    ids: IDS, FAMILIES, CULTURE_FAMILY, familyOf, STINGS, ROLE, FAMILY_INST, FAMILY_DEG, remapDegrees, forms, build,
    ensure, registerAll, key, tagKey, sections,
    get keyCount() { return nKeys; },
    registerMs: 0,
  };
  SG.MusicStings = MusicStings;
  // 汉地（hua）当场全部登记（≈ 3 ms）；其余 11 个家族在浏览器里按空闲分片登记（每片 ≤ 6 ms，不卡开机），
  // 用到时（key() / ensure()）当场补上；Node（测试）一次登记全部 1 284 个键
  if (typeof document === 'undefined' || typeof setTimeout !== 'function') registerAll();
  else {
    for (const id of IDS) ensure(id, 'hua');
    const todo = [];
    for (const fam of FAMILIES) if (fam !== 'hua') for (const id of IDS) todo.push([id, fam]);
    const ric = W.requestIdleCallback ? (fn => W.requestIdleCallback(fn, { timeout: 4000 })) : (fn => setTimeout(fn, 60));
    const slice = () => {
      const t0 = Date.now();
      while (todo.length && Date.now() - t0 < 6) { const [id, fam] = todo.shift(); ensure(id, fam); }
      if (todo.length) ric(slice);
    };
    ric(slice);
  }
})();
