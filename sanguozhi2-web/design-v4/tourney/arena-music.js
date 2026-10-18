'use strict';
/* ==========================================================================
   武林大会 · 会场音乐（TOURNAMENT.md §9.3 的谱面；实现时并入 js/music-tourney.js）
   与 js/music-han.js 同一记谱法（见 js/music.js 文件头），同一套乐器（js/music-inst.js）。

   tourney        「群英会」 1=D 宫调式 ♩=112 —— 开幕三通鼓 → 主题（唢呐 + 铜管）→ 万国轮奏（十二个文化家族的领奏乐器
                  按擂台一圈的顺序各奏两小节，东 → 北 → 西 → 南 → 东）→ 全奏 → 循环。大会进行中（签表、出场、颁奖之间）播放
   tourney-final  「月下决」 决赛之夜：编钟与人声垫音的慢引子，主题在大提琴与二胡上（♩=84），再入全奏（♩=112）
   tourney-ko     KO 的一记：锣 + 大鼓 + 全奏和弦（一次性，≈ 2.6 秒，overlay）

   自检：node design-v4/tourney/arena-music.js   → 载入音乐引擎（Node 中只做解析与校验，不发声），打印校验错误与时长
   ========================================================================== */
(function () {
  const SG = (typeof window !== 'undefined' ? window : global).SG;
  if (!SG || !SG.Music) return;   // Node 自检时由下方载入引擎后再执行
  const MU = SG.Music;

  // 主题（8 小节）：前两小节是「头」，万国轮奏只用头与答句
  const HEAD = `1 1_ 2_ 3 5 | 6 5_ 3_ 5 - |`;
  const ANS = `6 6_ 1'_ 2' 1'_ 6_ | 5 - - 0 |`;
  const THEME = `${HEAD} ${ANS} 3 3_ 5_ 6 1' | 2' 1'_ 6_ 5 3_ 2_ | 1 2_ 3_ 5 3_ 2_ | 1 - - 0 |`;
  // 低音与和声（每小节一个和弦，宫 – 羽 – 徵 – 宫 的五声进行）
  const BASS = `1,:4 | 6,,:4 | 6,,:2 5,,:2 | 5,,:4 | 3,:2 1,:2 | 2,:2 5,,:2 | 1,:2 5,,:2 | 1,:4 |`;
  const PAD = `1+5+1':4 | 6,+3+6:4 | 6,+3+1':2 5,+2+5:2 | 5,+2+5:4 | 3+6+1':2 1+5+1':2 | 2+6+2':2 5,+2+5:2 | 1+5+1':2 5,+2+5:2 | 1+5+1':4 |`;
  // 万国轮奏：两小节一位，头 / 答句交替；和声固定为 宫 – 徵
  const R_PAD = `1+5+1':4 | 5,+2+5:4 |`;
  const R_BASS = `1,:4 | 5,,:4 |`;
  const rest2 = `0:4 | 0:4 |`;
  // 十二个家族（CITY-VIEW §3.1）按擂台一圈的顺序（方位角递减）：华夏 草原 绿洲 安息 北方 罗马 尼罗 阿拉伯 天竺 南海 倭 三韩
  const ROUND = ['hua', 'steppe', 'oasis', 'persia', 'north', 'roman', 'nile', 'arabia', 'indic', 'nanhai', 'wa', 'han3'];
  function roundLine(fam) {
    let s = '';
    ROUND.forEach((f, i) => { s += (f === fam ? (i % 2 === 0 ? HEAD : ANS) : rest2) + ' '; });
    return s;
  }
  const roundPad = ROUND.map(() => R_PAD).join(' ');
  const roundBass = ROUND.map(() => R_BASS).join(' ');

  MU.add('tourney', {
    title: '群英会', key: 'D', bpm: 112, meter: 4, scale: '1 2 3 5 6', gain: 0.92, lowShelf: -3,
    voices: {
      su: { inst: 'suona', oct: 0, pan: 0.1 },
      hns: { inst: 'horns', oct: -1, pan: -0.2, gain: 0.85 },
      pad: { inst: 'strings', oct: -1, gain: 0.62 },
      ls: { inst: 'lowstr', oct: -1, gain: 0.85 },
      tp: { inst: 'timpani', oct: -2, gain: 0.9 },
      // 万国轮奏的领奏（家族 → 乐器，与 DOMESTIC-FX FAMILY_INST 的 lead 角色一致或相近）
      hua: { inst: 'dizi', oct: 1, pan: 0.15 },
      steppe: { inst: 'morin', oct: 0, pan: -0.2 },
      oasis: { inst: 'dombra', oct: 0, pan: 0.25 },
      persia: { inst: 'ney', oct: 0, pan: -0.1 },
      roman: { inst: 'cornu', oct: 0, pan: 0.2 },
      north: { inst: 'chanter', oct: 0, pan: -0.25 },
      nile: { inst: 'harp', oct: 0, pan: 0.2 },
      arabia: { inst: 'zurna', oct: 0, pan: -0.15 },
      indic: { inst: 'bansuri', oct: 0, pan: 0.15 },
      nanhai: { inst: 'saron', oct: 0, pan: -0.2 },
      wa: { inst: 'shinobue', oct: 1, pan: 0.2 },
      han3: { inst: 'piri', oct: 0, pan: -0.15 },
    },
    kit: {
      D: { inst: 'dagu', art: 0 },
      t: { inst: 'tanggu', art: 0, pan: 0.15 },
      k: { inst: 'tanggu', art: 1, pan: -0.15 },
      C: { inst: 'bo', pan: -0.3 },
      x: { inst: 'xiaoluo', pan: 0.35 },
      G: { inst: 'luo', pan: 0.2 },
      z: { inst: 'bangzi', pan: 0.3 },
      f: { inst: 'frame', pan: -0.35 },
    },
    pats: {
      // 三通鼓：大鼓由疏而密滚奏，第三通末一记锣
      tong1: { D: 'X.......X.......' },
      tong2: { D: 'X...X...X...X...' },
      tong3: { D: 'X.X.X.X.XxXxrrrr', G: '...............X' },
      march: { D: 'X.......X.......', t: '....x.......x.x.', z: 'x...x...x...x...', C: 'X...............' },
      march2: { D: 'X.......X...X...', t: '..x.x...x.x.x.xx', z: 'x...x...x...x...' },
      fill: { D: 'X...X...X.x.x.x.', t: 'x.x.x.x.xxxxrrrr', C: '...............X' },
      round: { D: 'X.......X.......', f: '..x...x...x...x.', z: 'x...x...x...x...' },
      roundC: { D: 'X.......X.......', f: '..x...x...x...x.', C: 'X...............', x: '........x.......' },
    },
    sections: {
      intro: {
        tp: `!mp 1,:4 | !mf 1,:4 | !< 1,* 1,* 1,* 1,* | !f 1,:4 |`,
        dr: 'tong1 tong2 tong3 _',
        hns: `0:4 | 0:4 | 0:4 | !f 1+5:4 |`,
      },
      A: {
        su: `!f ${THEME}`,
        hns: `!mf ${PAD}`,
        pad: `!mp ${PAD}`,
        ls: `!mf ${BASS}`,
        tp: `!f 1,:4 | 6,,:4 | 6,,:2 5,,:2 | 5,,:4 | 3,:2 1,:2 | 2,:2 5,,:2 | 1,:2 5,,:2 | 1,_ 1,_ 1, 1, 0 |`,
        dr: 'march march2 march march2 march march2 march fill',
      },
      B: Object.assign({
        pad: `!mp ${roundPad}`,
        ls: `!mp ${roundBass}`,
        dr: ROUND.map((f, i) => (i % 2 === 0 ? 'roundC' : 'round') + ' round').join(' '),
      }, Object.fromEntries(ROUND.map(f => [f, '!mf ' + roundLine(f)]))),
      A2: {
        su: `!ff ${THEME}`,
        hua: `!f ${THEME}`,
        hns: `!f ${PAD}`,
        pad: `!f ${PAD}`,
        ls: `!f ${BASS}`,
        tp: `!f 1,:4 | 6,,:4 | 6,,:2 5,,:2 | 5,,:4 | 3,:2 1,:2 | 2,:2 5,,:2 | 1,:2 5,,:2 | !< 1,* 1,* 1,* 1,* !ff |`,
        dr: 'march march2 march march2 march march2 march fill',
      },
    },
    form: ['intro', 'A', 'B', 'A2'],
    loopFrom: 'A',
  });

  MU.add('tourney-final', {
    title: '月下决', key: 'D', bpm: 84, meter: 4, scale: '1 2 3 5 6', gain: 0.9, lowShelf: -3,
    voices: {
      bz: { inst: 'bianzhong', oct: 0, gain: 0.8 },
      ch: { inst: 'choirOo', oct: -1, gain: 0.6 },
      vc: { inst: 'cello', oct: -1, pan: -0.1 },
      eh: { inst: 'erhu', oct: 0, pan: 0.1 },
      su: { inst: 'suona', oct: 0, pan: 0.1 },
      hns: { inst: 'horns', oct: -1, pan: -0.2, gain: 0.85 },
      pad: { inst: 'strings', oct: -1, gain: 0.62 },
      ls: { inst: 'lowstr', oct: -1, gain: 0.85 },
      tp: { inst: 'timpani', oct: -2, gain: 0.9 },
    },
    kit: {
      D: { inst: 'dagu', art: 0 },
      t: { inst: 'tanggu', art: 0, pan: 0.15 },
      z: { inst: 'bangzi', pan: 0.3 },
      C: { inst: 'bo', pan: -0.3 },
      G: { inst: 'luo', pan: 0.2 },
    },
    pats: {
      heart: { D: 'X..o............' },
      heart2: { D: 'X..o....X..o....' },
      march: { D: 'X.......X.......', t: '....x.......x.x.', z: 'x...x...x...x...', C: 'X...............' },
      march2: { D: 'X.......X...X...', t: '..x.x...x.x.x.xx', z: 'x...x...x...x...' },
      fill: { D: 'X...X...X.x.x.x.', t: 'x.x.x.x.xxxxrrrr', C: '...............X', G: 'X...............' },
    },
    sections: {
      intro: {
        bz: `!mp 1':2 5:2 | 6:2 3:2 | 5:2 2:2 | 1:4 |`,
        ch: `!p 1+5:4 | 6,+3:4 | 5,+2:4 | 1+5:4 |`,
        dr: 'heart heart heart2 heart2',
      },
      S: {
        vc: `!mp ${THEME}`,
        eh: `!p 0:4 | 0:4 | 0:4 | 0:4 | ${'3 3_ 5_ 6 1\' | 2\' 1\'_ 6_ 5 3_ 2_ | 1 2_ 3_ 5 3_ 2_ | 1 - - 0 |'}`,
        ch: `!p ${PAD}`,
        tp: `!p 1,:4 | 0:4 | 6,,:4 | 0:4 | 3,:4 | 0:4 | 1,:4 | !< 1,* 1,* 1,* 1,* !f |`,
        dr: 'heart heart heart2 heart heart2 heart heart2 heart2',
      },
      A: {
        bpm: 112,
        su: `!ff ${THEME}`,
        eh: `!f ${THEME}`,
        hns: `!f ${PAD}`,
        pad: `!f ${PAD}`,
        ls: `!f ${BASS}`,
        tp: `!f 1,:4 | 6,,:4 | 6,,:2 5,,:2 | 5,,:4 | 3,:2 1,:2 | 2,:2 5,,:2 | 1,:2 5,,:2 | 1,_ 1,_ 1, 1, 0 |`,
        dr: 'march march2 march march2 march march2 march fill',
      },
    },
    form: ['intro', 'S', 'A'],
    loopFrom: 'S',
  });

  MU.add('tourney-ko', {
    title: 'KO', key: 'D', bpm: 112, meter: 4, scale: '1 2 3 5 6', gain: 0.95, loop: false,
    voices: {
      hns: { inst: 'horns', oct: -1, gain: 0.9 },
      pad: { inst: 'strings', oct: -1, gain: 0.7 },
      tp: { inst: 'timpani', oct: -2 },
    },
    kit: { D: { inst: 'dagu', art: 0 }, G: { inst: 'luo' }, C: { inst: 'bo' } },
    pats: { hit: { D: 'X.....xxX.......', G: 'X...............', C: 'X...............' } },
    sections: {
      A: {
        hns: `!ff 1+5+1':4 |`,
        pad: `!f 1+5+1':4 |`,
        tp: `!ff 1,* 1,* 1,:2 |`,
        dr: 'hit',
      },
    },
    form: ['A'],
  });
})();

// ------------------------------------------------------------------ 自检（Node） --
if (typeof module !== 'undefined' && require.main === module) {
  const path = require('path'), fs = require('fs'), vm = require('vm');
  global.window = global;
  const JS = path.join(__dirname, '..', '..', 'js');
  for (const f of ['core.js', 'music.js', 'music-inst.js', 'music-han.js']) vm.runInThisContext(fs.readFileSync(path.join(JS, f), 'utf8'), { filename: f });
  vm.runInThisContext(fs.readFileSync(__filename, 'utf8').replace(/^'use strict';/, '').split('// ------------------------------------------------------------------ 自检')[0], { filename: 'arena-music.js' });
  let bad = 0;
  for (const k of ['tourney', 'tourney-final', 'tourney-ko']) {
    const e = global.SG.Music.validate(k);
    const c = global.SG.Music.compile(k);
    console.log(k, e.length ? 'ERRORS ' + JSON.stringify(e) : 'ok', 'intro', (c.introDur || 0).toFixed(1) + 's', 'loop', (c.loopDur || 0).toFixed(1) + 's');
    bad += e.length;
  }
  process.exitCode = bad ? 1 : 0;
}
