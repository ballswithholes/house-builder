'use strict';
/* ==========================================================================
   tests/run-all.js — 测试矩阵（INTEGRATION §13，DESIGN-V4 §19）
     node tests/run-all.js            快速档（每次推送，预算 ≤ 6 分钟）：每个套件的快速模式
     node tests/run-all.js --nightly  夜间档（预算 ≤ 45 分钟）：长模式（经典 2 × 48 × 25 年、世界 24 × 15 年、和平玩家、战术 …）
     node tests/run-all.js --list     只列出套件与命令
     选项：-j N 并行数（缺省 2；与其他进程共用 CPU 时别开太大）、--only a,b（按名字过滤）、--skip a,b
   规则：
   - 尚未落地的套件（文件不存在）记为 skip，不算失败；各工作流的新套件在 SUITES 里已经登记好了命令。
   - 每个套件以退出码判定（0 = 通过）；输出存到 $RUN_ALL_OUT（缺省 系统临时目录/sanguozhi2-run-all/<档>/<名>.log），失败时打印末尾 40 行。
   - 超出预算只警告（CI 机器快慢不一）；总退出码 = 失败的套件数。
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const cp = require('child_process');

const ROOT = path.join(__dirname, '..');
const ARGS = process.argv.slice(2);
const NIGHTLY = ARGS.indexOf('--nightly') >= 0;
const LIST = ARGS.indexOf('--list') >= 0;
const J = Math.max(1, parseInt((ARGS[ARGS.indexOf('-j') + 1] || ''), 10) || 2);
const pick = flag => { const i = ARGS.indexOf(flag); return i >= 0 && ARGS[i + 1] ? ARGS[i + 1].split(',') : null; };
const ONLY = pick('--only'), SKIP = pick('--skip') || [];
const BUDGET_S = NIGHTLY ? 45 * 60 : 6 * 60;

// name, file (existence decides skip), quick args, nightly args, owner
const SUITES = [
  // ---- integration (W0)
  ['econ', 'tests/econ.js', ['--refs'], ['--refs'], 'integration'],
  ['save-v4', 'tests/save-v4.js', [], [], 'integration'],
  ['load-order', 'tests/load-order.js', [], [], 'integration'],
  ['v4-all', 'tests/v4-all.js', [], ['--nightly'], 'integration'],
  // ---- v3 suites (unchanged behaviour with every v4 switch off)
  ['sim', 'tests/sim.js', [], [], 'v3 + COMBAT'],
  ['move', 'tests/move.js', [], [], 'v3'],
  ['world', 'tests/world.js', [], [], 'v3'],
  ['duel-bots', 'tests/duel-bots.js', ['--quick'], [], 'COMBAT'],
  // ---- v4 workstreams (skip until they land)
  ['items', 'tests/items.js', ['--quick'], ['--full'], 'ITEMS'],          // --full: classic 40 × 20 y, world 10 × 20 y (ITEMS §17.4)
  ['titles', 'tests/titles.js', ['--quick'], ['--nightly'], 'TITLES'],   // --nightly: the second 48-seed set and the world years (TITLES §12.1)
  ['engage', 'tests/engage.js', ['--quick'], [], 'ENGAGEMENT'],
  ['tourney', 'tests/tourney.js', ['--quick'], [], 'TOURNAMENT'],
  ['combos', 'tests/combos.js', [], [], 'COMBAT'],
  ['domestic-tiers', 'tests/domestic-tiers.js', [], [], 'DOMESTIC-FX'],
  ['music-stings', 'tests/music-stings.js', [], [], 'DOMESTIC-FX'],
  ['suites', 'tests/suites.js', [], [], 'AUDIO'],
  ['voice-lines', 'tests/voice-lines.js', [], [], 'AUDIO'],
  ['voice-pick', 'tests/voice-pick.js', [], [], 'AUDIO'],
  ['city-layout', 'tests/city-layout.js', [], [], 'CITY-VIEW'],
  ['settings', 'tests/settings.js', [], [], 'UI'],
  ['saves', 'tests/saves.js', [], [], 'UI'],
  // ---- design-data checkers (specs' own validators; cheap)
  ['spec-stings', 'design-v4/domestic/stings.js', [], [], 'DOMESTIC-FX (spec)'],
  ['spec-city-stages', 'design-v4/domestic/city-stages.js', [], [], 'CITY-VIEW (spec)'],
  ['spec-voice-lines', 'design-v4/audio/voice-lines.js', [], [], 'AUDIO (spec)'],
];


const tier = NIGHTLY ? 'nightly' : 'quick';
const outDir = process.env.RUN_ALL_OUT || path.join(require('os').tmpdir(), 'sanguozhi2-run-all', tier);
const jobs = SUITES.filter(s => (!ONLY || ONLY.indexOf(s[0]) >= 0) && SKIP.indexOf(s[0]) < 0);

if (LIST) {
  for (const [name, file, q, n, owner] of jobs) console.log(`${name.padEnd(18)} ${owner.padEnd(20)} node ${file} ${(NIGHTLY ? n : q).join(' ')}${fs.existsSync(path.join(ROOT, file)) ? '' : '   (not yet present)'}`);
  return;
}

fs.mkdirSync(outDir, { recursive: true });
const T0 = Date.now();
const results = [];
let next = 0, running = 0;
console.log(`run-all ${tier}: ${jobs.length} suites, ${J} in parallel, budget ${BUDGET_S / 60} min`);

function start() {
  while (running < J && next < jobs.length) {
    const [name, file, q, n, owner] = jobs[next++];
    const abs = path.join(ROOT, file);
    if (!fs.existsSync(abs)) { results.push({ name, owner, status: 'skip', s: 0, why: 'not yet present' }); continue; }
    const args = NIGHTLY ? n : q;
    const t0 = Date.now();
    running++;
    const log = path.join(outDir, name + '.log');
    const child = cp.spawn(process.execPath, [abs].concat(args), { cwd: ROOT, env: process.env });
    const chunks = [];
    child.stdout.on('data', d => chunks.push(d));
    child.stderr.on('data', d => chunks.push(d));
    child.on('close', code => {
      running--;
      const text = Buffer.concat(chunks).toString('utf8');
      fs.writeFileSync(log, text);
      const s = (Date.now() - t0) / 1000;
      const r = { name, owner, status: code === 0 ? 'ok' : 'FAIL', code, s, log };
      results.push(r);
      console.log(`${r.status === 'ok' ? '  ok  ' : '  FAIL'} ${name.padEnd(18)} ${s.toFixed(1).padStart(7)} s   ${owner}`);
      if (code !== 0) console.log(text.split('\n').slice(-40).map(l => '      | ' + l).join('\n'));
      if (next < jobs.length) start(); else if (running === 0) finish();
    });
  }
  if (running === 0 && next >= jobs.length) finish();
}
let finished = false;
function finish() {
  if (finished) return; finished = true;
  const total = (Date.now() - T0) / 1000;
  const failed = results.filter(r => r.status === 'FAIL');
  const skipped = results.filter(r => r.status === 'skip');
  console.log('');
  console.log(`== ${tier}: ${results.length - skipped.length} run, ${failed.length} failed, ${skipped.length} skipped (${skipped.map(r => r.name).join(', ') || '-'}), ${total.toFixed(0)} s wall`);
  if (total > BUDGET_S) console.log(`  WARNING: over the ${tier} budget (${BUDGET_S} s)`);
  fs.writeFileSync(path.join(outDir, 'summary.json'), JSON.stringify({ tier, total, results }, null, 1));
  console.log(failed.length === 0 ? 'ALL OK' : 'FAILURES: ' + failed.map(r => r.name).join(', '));
  process.exitCode = failed.length;
}
start();
