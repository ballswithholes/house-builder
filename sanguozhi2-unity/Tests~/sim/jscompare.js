'use strict';
// JS ↔ C# 输出对照（run.sh 调用）：运行网页版的 tests/sim.js、tests/world.js、tests/move.js，要求 JS 的每一行输出
// （去掉计时之后）都出现在对应 C# 程序（Harness*.cs）的输出里——C# 的统计数字（胜率、伤害比、城数、检查数……）一旦与 JS 不同即失败。
// C# 独有的行（自检、参考文件比对）不算差异。网页版目录取环境变量 SANGUO_WEB，缺省 ../../../sanguozhi2-web。
//   node jscompare.js tests <程序名…>             → 这些 C# 程序对应的 JS 测试（空格分隔；没有则输出空行）
//   node jscompare.js run <输出目录> <JS 测试…>    → 并行运行 JS 测试，输出写到 <输出目录>/js/<测试>.txt（退出码写到 .code）
//   node jscompare.js compare <输出目录> <程序名…> → 逐行对照（C# 输出取 <输出目录>/<程序名>.txt）；退出码 = 不一致的 JS 测试数
// 节与程序的对应：sim.js 按节标题（「== (1) …」「(7a) …」）分到 Harness / Harness2 / HarnessSpecials / HarnessProgress / HarnessTactics；
// 只运行了部分程序时只对照这些程序负责的节。JS 新增了没有对应程序的节也判失败（提醒补移植）。
const path = require('path'), fs = require('fs'), cp = require('child_process');
const WEB = process.env.SANGUO_WEB || path.join(__dirname, '..', '..', '..', 'sanguozhi2-web');

const SIM_SECTIONS = {
  '1': 'Harness', '1b': 'Harness', '1c': 'Harness',
  '2': 'Harness2', '3': 'Harness2',
  '4': 'HarnessSpecials', '5': 'HarnessSpecials', '5b': 'HarnessSpecials', '5c': 'HarnessSpecials', '6': 'HarnessSpecials',
  '7a': 'HarnessProgress', '7b': 'HarnessProgress', '7c': 'HarnessProgress', '7e': 'HarnessProgress', '7f': 'HarnessProgress',
  '7g': 'HarnessProgress', '7h': 'HarnessProgress',
  '7d': 'HarnessTactics',
};
const TESTS = {
  sim: { file: 'tests/sim.js', section: l => { const m = /^== \((\w+)\) /.exec(l) || /^\((7\w)\) /.exec(l); return m ? m[1] : null; }, map: SIM_SECTIONS },
  world: { file: 'tests/world.js', all: 'HarnessWorld' },
  move: { file: 'tests/move.js', all: 'HarnessMove' },
};
// 有意不同的 JS 行：[JS 行的正则, 须在 C# 输出中出现的替代行的正则, 原因]
const EXPECTED_DIFF = {
  world: [[/^== \(3\) classic 5-year all-AI game, seed 7: without world-data\.js /, /^== \(3\) classic 5-year all-AI game, seed 7: /,
    'Node 下的 world.js 不载入 battle-model.js，经典局摘要与 C# 不可比；C# 打印自己的前后摘要，经典局逐月对照由 HarnessTrace 完成']],
};

// 去掉计时（两边同样处理）
function norm(l) {
  l = l.replace(/\s+$/, '');
  if (/^speed: /.test(l)) return 'speed: #';                       // world (2) 的速度行：全是计时
  l = l.replace(/\s+\d+(?:\.\d+)?ms$/, '');                         // 「… top [...]  228ms」
  l = l.replace(/^total \d+(?:\.\d+)?s$/, 'total #s');
  l = l.replace(/\s*\(\d+(?:\.\d+)?s\)$/, '');                      // 「ALL OK  (63.6s)」
  return l;
}
function owners(t) { return t.all ? [t.all] : Array.from(new Set(Object.values(t.map))); }

const [mode, ...rest] = process.argv.slice(2);
if (mode === 'tests') {
  const names = new Set(rest);
  console.log(Object.keys(TESTS).filter(k => owners(TESTS[k]).some(n => names.has(n))).join(' '));
} else if (mode === 'run') {
  const [out, ...tests] = rest;
  const dir = path.join(out, 'js');
  fs.mkdirSync(dir, { recursive: true });
  for (const k of tests) {
    const file = path.join(WEB, TESTS[k].file);
    if (!fs.existsSync(file)) { fs.writeFileSync(path.join(dir, k + '.txt'), '找不到 ' + file + '（设 SANGUO_WEB 指向 sanguozhi2-web）\n'); fs.writeFileSync(path.join(dir, k + '.code'), '127'); continue; }
    const fd = fs.openSync(path.join(dir, k + '.txt'), 'w');
    const p = cp.spawn(process.execPath, [file], { cwd: WEB, stdio: ['ignore', fd, fd] });
    p.on('exit', code => fs.writeFileSync(path.join(dir, k + '.code'), String(code)));
  }
} else if (mode === 'compare') {
  const [out, ...names] = rest;
  const ran = new Set(names);
  let bad = 0;
  for (const k of Object.keys(TESTS)) {
    const t = TESTS[k];
    if (!owners(t).some(n => ran.has(n))) continue;
    const code = (() => { try { return fs.readFileSync(path.join(out, 'js', k + '.code'), 'utf8').trim(); } catch (e) { return '(未运行)'; } })();
    const jsText = (() => { try { return fs.readFileSync(path.join(out, 'js', k + '.txt'), 'utf8'); } catch (e) { return ''; } })();
    if (code !== '0') { bad++; console.log(`  FAIL ${t.file}: JS 测试退出码 ${code}\n` + jsText.split('\n').slice(-6).map(l => '    ' + l).join('\n')); continue; }
    // C# 输出：每个程序一个多重集合
    const pool = {};
    for (const n of owners(t)) {
      if (!ran.has(n)) continue;
      const m = pool[n] = new Map();
      let text = '';
      try { text = fs.readFileSync(path.join(out, n + '.txt'), 'utf8'); } catch (e) { /* 程序编译失败等：下面逐行报缺 */ }
      for (const l of text.split('\n')) { const x = norm(l); if (x) m.set(x, (m.get(x) || 0) + 1); }
    }
    let sec = null, lines = 0, missing = 0, expected = 0, unmapped = new Set();
    const per = {};
    for (const raw of jsText.split('\n')) {
      const l = norm(raw);
      if (!l) continue;
      if (t.section) { const s = t.section(l); if (s) sec = s; }
      const owner = t.all || (sec && t.map[sec]);
      if (!owner) { unmapped.add(sec || '(节标题之前)'); continue; }
      if (!ran.has(owner)) continue;
      lines++;
      per[owner] = (per[owner] || 0) + 1;
      const m = pool[owner];
      const ex = (EXPECTED_DIFF[k] || []).find(e => e[0].test(l));
      if (ex) {
        const alt = Array.from(m.keys()).find(x => ex[1].test(x));
        if (alt) { expected++; continue; }
      }
      const c = m.get(l) || 0;
      if (c > 0) { m.set(l, c - 1); continue; }
      missing++;
      if (missing <= 12) console.log(`  MISSING in ${owner} (${t.file}${sec ? ' 节 (' + sec + ')' : ''}):\n    JS ${raw.replace(/\s+$/, '')}`);
    }
    if (unmapped.size) { bad++; console.log(`  FAIL ${t.file}: 节 ${Array.from(unmapped).join('、')} 没有对应的 C# 程序（JS 新增了测试？请补移植并加到 jscompare.js）`); }
    if (missing) { bad++; console.log(`  FAIL ${t.file}: ${missing}/${lines} 行在 C# 输出中找不到`); }
    else console.log(`  ${t.file}: ${lines} 行全部出现在 C# 输出中（${Object.keys(per).map(n => n + ' ' + per[n]).join('，')}${expected ? `；有意不同 ${expected} 行` : ''}）`);
  }
  if (!bad) console.log('ALL OK');
  process.exit(bad);
} else {
  console.error('用法见文件头'); process.exit(2);
}
