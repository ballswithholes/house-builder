'use strict';
/* ==========================================================================
   单挑格斗（第二版 §4E）人机强度回归（Node，无依赖）
   用脚本玩家（固定节奏、无反应延迟）对战真实电脑（SG.DuelGame._AI），检查：
   (1) 原有回归（与 tests/duel.html 的 botTally 相同）：跳入空中斩 / 连按 不能以弱胜强；
   (2) 连按（masher：走近、够得着就每 0.12 秒按一次轻击，怒气满放绝技）：
       同武力 72–76 的电脑平均胜率 ≤ 45%（各组合含兵器长短不同），武力 ≤ 40 仍好欺负（≥ 80%），
       武力 90 以上更难（≤ 30%，但并非无法取胜）。
   用法：node tests/duel-bots.js [每组场数，默认 40] [种子，默认 7]      退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
for (const f of ['core.js', 'data.js', 'duel-game.js']) vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
const SG = global.SG, D = SG.DuelGame;
const N = +process.argv[2] || 40;
SG.Random.seed(+process.argv[3] || 7);

const gens = {};
for (const s of SG.ScenarioData.Generals) { const p = s.split('|'); gens[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3] }; }

let fails = 0, checks = 0;
function ok(cond, msg) { checks++; if (!cond) { fails++; console.log('  FAIL: ' + msg); } }

// 与 tests/duel.html 的 botCtl 相同的两个脚本玩家
function bot(kind) {
  const c = { held: { x: 0, up: false, light: false, heavy: false, guard: false, special: false, dash: 0 }, taps: { light: 0, up: 0, special: 0, heavy: 0, dir: 0 } };
  let nt = 0;
  c.update = (dt, sim) => {
    const me = sim.f[0], o = sim.f[1], d = Math.abs(o.x - me.x);
    if (kind === 'jumper') {
      c.held.x = me.face;
      if (sim.clock < nt) return;
      if (sim.actionable(me) && d < 3.2 && d > 1.2) { c.taps.up++; nt = sim.clock + 0.1; }
      else if (me.state === 'jump' && d < me.reach + 0.3) { c.taps.light++; nt = sim.clock + 0.1; }
      else if (me.state !== 'jump' && d < me.reach) { c.taps.light++; nt = sim.clock + 0.1; }
    } else {
      c.held.x = d > me.reach * 0.9 ? me.face : 0;
      if (sim.clock > nt && d < me.reach * 1.1) { c.taps.light++; nt = sim.clock + 0.12; }
      if (me.rage >= 100) c.taps.special++;
    }
  };
  return c;
}
function rate(kind, p, a, n) {
  const P = gens[p], A = gens[a];
  let w = 0;
  for (let i = 0; i < n; i++) {
    const sim = new D._Sim(P, A, D.lookOf(P), D.lookOf(A));
    sim.f[0].ctrl = bot(kind); sim.f[1].ctrl = new D._AI(sim.f[1]);
    sim.runToEnd();
    if (sim.outcome().winner === 0) w++;
  }
  return 100 * w / n;
}
function section(name, fn) {
  console.log('== ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAILED: ' + (e.stack || e)); }
}

section('(1) botTally regression (same cases as tests/duel.html)', () => {
  const cases = [
    ['jumper', '孙乾', '吕布', 0, 10], ['jumper', '刘备', '吕布', 0, 40], ['jumper', '关羽', '关羽', 0, 75],
    ['masher', '孙乾', '吕布', 0, 10], ['masher', '刘备', '吕布', 0, 40], ['masher', '关羽', '关羽', 5, 75], ['masher', '张飞', '刘备', 60, 100],
  ];
  for (const [kind, p, a, lo, hi] of cases) {
    const pct = rate(kind, p, a, N);
    console.log(`  ${kind} ${p}(${gens[p].war}) vs AI ${a}(${gens[a].war}): ${pct.toFixed(0)}% (want ${lo}-${hi})`);
    ok(pct >= lo && pct <= hi, `${kind} ${p} vs ${a}: ${pct.toFixed(0)}% outside ${lo}-${hi}`);
  }
});

// 同武力的若干组合（含长枪对短兵、双剑、大斧、骨朵等兵器差异）
const MID = [['刘备', '刘备'], ['曹操', '曹操'], ['王平', '王平'], ['郭汜', '郭汜'], ['李傕', '刘备'], ['刘备', '王平'], ['霍峻', '刘备'],
  ['张济', '曹操'], ['祖茂', '黄祖'], ['郭汜', '潘凤'], ['潘凤', '孟优'], ['吴懿', '孟优'], ['孟达', '潘凤'], ['孟优', '吴懿']];
section('(2) masher vs equal-war AI', () => {
  const rows = MID.map(([p, a]) => [p, a, rate('masher', p, a, N)]);
  const avg = rows.reduce((s, r) => s + r[2], 0) / rows.length, max = Math.max(...rows.map(r => r[2]));
  console.log('  war 72-76: ' + rows.map(r => `${r[0]}>${r[1]} ${r[2].toFixed(0)}`).join('  '));
  console.log(`  war 72-76 average ${avg.toFixed(0)}%, worst pair ${max.toFixed(0)}%`);
  ok(avg <= 45, `masher average vs war 72-76 AI is ${avg.toFixed(0)}% (want <= 45%)`);
  ok(max <= 65, `masher worst pair vs war 72-76 AI is ${max.toFixed(0)}% (want <= 65%)`);
  ok(avg >= 5, `war 72-76 AI should still be beatable by mashing sometimes (got ${avg.toFixed(0)}%)`);

  const weak = [['孙乾', '孙乾'], ['简雍', '孙乾'], ['沮授', '陶谦'], ['陶谦', '沮授']].map(([p, a]) => rate('masher', p, a, N));
  const wAvg = weak.reduce((s, x) => s + x, 0) / weak.length;
  console.log(`  war <= 40: ${weak.map(x => x.toFixed(0)).join(' ')}  average ${wAvg.toFixed(0)}%`);
  ok(wAvg >= 80, `weak generals (war <= 40) should stay easy to mash (got ${wAvg.toFixed(0)}%)`);

  const strong = [['关羽', '关羽'], ['张飞', '赵云'], ['华雄', '张郃'], ['吕布', '吕布']].map(([p, a]) => rate('masher', p, a, N));
  const sAvg = strong.reduce((s, x) => s + x, 0) / strong.length;
  console.log(`  war >= 90: ${strong.map(x => x.toFixed(0)).join(' ')}  average ${sAvg.toFixed(0)}%`);
  ok(sAvg <= 30 && sAvg >= 3, `strong generals (war >= 90) should be hard but not impossible to mash (got ${sAvg.toFixed(0)}%)`);
  ok(sAvg < avg + 5, `strong generals should not be easier to mash than average ones (${sAvg.toFixed(0)}% vs ${avg.toFixed(0)}%)`);
});

section('(3) AI vs AI: war decides, mirror is even', () => {
  const n = N;
  const sim = (a, b) => { let w = 0; for (let i = 0; i < n; i++) if (D.simulate(gens[a], gens[b]).winner === 0) w++; return 100 * w / n; };
  const big = sim('吕布', '孙乾'), gap = sim('典韦', '李典'), mirror = sim('关羽', '关羽');
  console.log(`  吕布 vs 孙乾 ${big.toFixed(0)}%, 典韦 vs 李典 ${gap.toFixed(0)}%, 关羽 mirror ${mirror.toFixed(0)}%`);
  ok(big >= 95, '吕布 should crush 孙乾');
  ok(gap >= 75, '典韦(96) should usually beat 李典(78)');
  ok(mirror >= 25 && mirror <= 75, 'mirror match should be roughly even');
});

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
