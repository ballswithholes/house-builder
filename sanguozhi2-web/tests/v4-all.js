'use strict';
/* ==========================================================================
   tests/v4-all.js — 全部开关打开的跨系统平衡门槛（DESIGN-V4 §16，INTEGRATION §9、§13）
   两部分：
   A 本地（native）：按 index.html 载入全部现有的第四版规则模块（items.js、titles.js、engage.js、tourney.js、combos.js
     …… 有的都载入），在第四版月末流程上跑全电脑战役，「全关」对「全开」（各模块的 enabled 开关），量 §16 的门槛。
     另有一致性检查：全关时第四版流程（endMonthV4）与第三版（无 js/econ.js 的 endMonthV3）的整局 JSON 逐位相同。
     还没有落地的模块不在场时，「全开」= 「全关」（门槛自然通过，报告里会写明哪些模块在场）。
   B 原型（--proto）：设计原型 design-v4/integration/v4-all-proto.js（ITEMS / TITLES / ENGAGEMENT / TOURNAMENT 的规则原型）
     在子进程里运行，但 SG.Mods / SG.Econ / SG.AIBudget 换成 js/econ.js + js/battle-math.js —— 即用真实的整合核心量
     §16 的门槛（战役存活、首个大势力、叛逃、世界存活、电脑支出份额、跌破 0 的城、人望上限、战术、和平玩家、王道、指令分级）。
     这是在各工作流的真模块落地之前，整合核心对平衡门槛的验证；模块落地后 A 部分接手。
   用法：
     node tests/v4-all.js                 快速档（≤ 2 分钟）：A 经典 12 种子 × 6 年、世界 4 × 6 年 + B 经典 12 × 6、世界 4 × 6、
                                          战术 2、和平玩家 1 种子 × 6 年、极限叠加（门槛按样本大小放宽）
     node tests/v4-all.js --nightly       夜间档（≈ 25–35 分钟）：经典 2 × 48 × 25 年、世界 24 × 15 年、和平玩家 6 × 20 / 4 × 15、战术 8
     node tests/v4-all.js --native | --proto      只跑一部分
   退出码 = 未通过的门槛数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const os = require('os');
const cp = require('child_process');
const crypto = require('crypto');
const { load, ROOT } = require('./v4-load.js');

const ARGS = process.argv.slice(2);
const NIGHTLY = ARGS.indexOf('--nightly') >= 0;
const ONLY = ARGS.indexOf('--native') >= 0 ? 'native' : ARGS.indexOf('--proto') >= 0 ? 'proto' : 'both';
const T0 = Date.now();
let fails = 0, checks = 0;
const gates = [];
function gate(name, pass, detail) { checks++; gates.push({ name, pass, detail }); if (!pass) fails++; console.log(`  ${pass ? 'PASS' : 'FAIL'} ${name}: ${detail}`); }
function info(s) { console.log('  ' + s); }
const avg = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);
const median = a => { const b = a.slice().sort((x, y) => x - y); return b.length ? b[b.length >> 1] : 0; };

// =========================================================================================== A native =====
const MODULES = [   // switch objects of the v4 systems (INTEGRATION §1.2)
  ['Items', 'items.js'], ['Titles', 'titles.js'], ['Engage', 'engage.js'], ['Tourney', 'tourney.js'], ['Combos', 'combos.js'], ['Crit', 'battle-math.js'],
];
function setSwitches(SG, on) {
  const set = [];
  for (const [name] of MODULES) {
    const o = SG[name];
    if (!o || typeof o !== 'object') continue;
    if ('enabled' in o && typeof o.enabled !== 'function') { o.enabled = on; set.push(name); }
  }
  return set;
}
function nativeIdentity() {
  console.log('== A1 identity: all v4 switches off, endMonthV4 == the v3 month (endMonthV3) — whole-game JSON');
  const v4 = load({ v4: true });
  const v3 = load({ v4: false });
  setSwitches(v4, false);
  const run = (SG, scen, key, seed, months) => {
    SG.ScenarioData = SG.Scenarios[scen];
    SG.Random.seed(seed);
    const g = SG.G = SG.GameState.newGame(key, scen);
    g.player = -1;
    for (let m = 0; m < months; m++) { SG.StrategyAI.runAI([]); SG.StrategyAI.endMonth(); }
    return crypto.createHash('md5').update(JSON.stringify(g).replace(/"version":\d/, '"version":X')).digest('hex');
  };
  const cases = NIGHTLY ? [['classic', 'liubei', 1, 120], ['classic', 'cao', 2, 120], ['world', 'roma', 1, 36]] : [['classic', 'liubei', 1, 60], ['world', 'roma', 1, 12]];
  for (const [scen, key, seed, months] of cases) {
    const a = run(v3, scen, key, seed, months), b = run(v4, scen, key, seed, months);
    gate(`identity ${scen} seed ${seed} × ${months} months`, a === b, a + (a === b ? ' = ' : ' ≠ ') + b);
  }
}
function nativeCampaign(scen, seeds, years) {
  const SG = load({ v4: true });
  const present = MODULES.filter(([n]) => SG[n] && typeof SG[n] === 'object' && 'enabled' in SG[n]).map(([n]) => n);
  const out = {};
  for (const variant of ['off', 'all']) {
    const switched = setSwitches(SG, variant === 'all');
    SG.AIBudget.reset();
    const st = { incomePct: [], atCap: 0, n: 0 };
    const oGold = SG.Econ.cityGold;
    SG.Econ.cityGold = function (c) { const r = oGold.call(this, c); st.n++; st.incomePct.push(r.pct); if (r.pct >= SG.Mods.CAPS.incomeHi - 1e-9) st.atCap++; return r; };
    const runs = [];
    for (let s = 1; s <= seeds; s++) {
      SG.ScenarioData = SG.Scenarios[scen];
      SG.Random.seed(s);
      const g = SG.G = SG.GameState.newGame(scen === 'world' ? 'roma' : 'liubei', scen);
      g.player = -1;
      const d0 = SG.StrategyAI.stats.defections;
      const r = { alive: {}, f25: null, defections: 0, fame: 0, ap: 0, ms: 0, fm: 0 };
      const total = g.cities.length;
      for (let m = 0; m < years * 12; m++) {
        const t0 = process.hrtime.bigint();
        SG.StrategyAI.runAI([]); SG.StrategyAI.endMonth();
        r.ms += Number(process.hrtime.bigint() - t0) / 1e6;
        r.fm += g.factions.filter(f => f.alive && f.id !== g.player).length;
        if (r.f25 === null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > total * 0.25) { r.f25 = (m + 1) / 12; break; }
        if ((m + 1) % 12 === 0) r.alive[(m + 1) / 12] = g.factions.filter(f => f.alive).length;
      }
      r.defections = SG.StrategyAI.stats.defections - d0;
      const alive = g.factions.filter(f => f.alive);
      r.fame = avg(alive.map(f => SG.Mods.fameBonus(f.id)));
      r.ap = avg(alive.map(f => SG.Mods.actionPoints(f.id)));
      runs.push(r);
    }
    SG.Econ.cityGold = oGold;
    out[variant] = { runs, st, aib: JSON.parse(JSON.stringify(SG.AIBudget.stats)), switched };
  }
  out.present = present;
  return out;
}
function nativeGates() {
  const C = NIGHTLY ? [48, 25] : [12, 6], Wd = NIGHTLY ? [24, 15] : [4, 6];
  nativeIdentity();
  for (const [scen, [seeds, years]] of [['classic', C], ['world', Wd]]) {
    const t0 = Date.now();
    console.log(`== A2 native all-AI campaign ${scen}: ${seeds} seeds × ${years} y, variants off / all`);
    const r = nativeCampaign(scen, seeds, years);
    info(`v4 modules present: ${r.present.length ? r.present.join(', ') : '(none yet — all = off)'}  [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
    const yk = scen === 'classic' ? Math.min(10, years) : Math.min(15, years);
    const aliveOff = avg(r.off.runs.map(x => x.alive[yk])), aliveAll = avg(r.all.runs.map(x => x.alive[yk]));
    const tolA = scen === 'classic' ? (NIGHTLY ? 0.8 : 1.5) : (NIGHTLY ? 2 : 4);
    gate(`${scen} factions alive y${yk} (all ≥ off − ${tolA})`, aliveAll >= aliveOff - tolA, `${aliveOff.toFixed(2)} → ${aliveAll.toFixed(2)}`);
    if (scen === 'classic') {
      const f = v => median(v.runs.map(x => (x.f25 === null ? 99 : x.f25)));
      const tolF = NIGHTLY ? 0.5 : 1.0;
      gate(`classic first faction > 25 % median (all ≥ off − ${tolF} y)`, f(r.all) >= f(r.off) - tolF, `${f(r.off).toFixed(2)} → ${f(r.all).toFixed(2)} y`);
    }
    const dOff = avg(r.off.runs.map(x => x.defections)), dAll = avg(r.all.runs.map(x => x.defections));
    gate(`${scen} defections per seed (all ≤ off + 2)`, dAll <= dOff + 2, `${dOff.toFixed(2)} → ${dAll.toFixed(2)}`);
    const a = r.all.aib, gross = a.gross || 0;
    const disc = ['title', 'gear', 'project', 'festival'].reduce((s, k) => s + (a.byKind[k] || 0), 0);
    gate(`${scen} no city below 0 after a discretionary spend`, a.below0 === 0, `below0 ${a.below0}`);
    gate(`${scen} discretionary spending ≤ 22 % of gross (+ starting purse)`, gross === 0 || disc / gross <= 0.22 + 0.01, gross ? (100 * disc / gross).toFixed(2) + ' %' : 'no discretionary spending (no module spends yet)');
    if (r.present.indexOf('Items') >= 0 && gross > 0 && NIGHTLY) {
      const gs = (a.byKind.gear || 0) / gross;
      gate(`${scen} AI gear spending in ITEMS' 8–15 % band`, scen === 'world' ? gs >= 0.07 && gs <= 0.15 : gs >= 0.08 && gs <= 0.15, (100 * gs).toFixed(2) + ' %');
    }
    const fameAll = avg(r.all.runs.map(x => x.fame));
    gate(`${scen} fame bonus ≤ +10`, fameAll <= 10 + 1e-9, fameAll.toFixed(2));
    // INTEGRATION §6.6: ≤ 60 ms CPU per AI faction-month (v3 AI turn + every v4 phase; the January tournament is exempt and
    // measured by tests/tourney.js)
    const msPer = v => v.runs.reduce((s, x) => s + x.ms, 0) / Math.max(1, v.runs.reduce((s, x) => s + x.fm, 0));
    gate(`${scen} CPU per AI faction-month ≤ 60 ms (runAI + endMonth, all on)`, msPer(r.all) <= 60, `${msPer(r.off).toFixed(3)} → ${msPer(r.all).toFixed(3)} ms`);
    info(`income pct median ${median(r.all.st.incomePct).toFixed(3)}, at the +35 % cap ${(100 * r.all.st.atCap / Math.max(1, r.all.st.n)).toFixed(2)} % of city-months; AP avg ${avg(r.all.runs.map(x => x.ap)).toFixed(2)}`);
  }
}

// =========================================================================================== B prototype =====
// Runs v4-all-proto.js in a child with js/econ.js + js/battle-math.js as its econ module (W0_ECON=js, default) or with its
// own econ-proto.js (W0_ECON=proto). Prints the prototype's lines; returns them.
// W0_PIPE (default 1): the prototype's modules run on the shipped pipeline (endMonthV4, model.js Commands, the real
// SG.AIBudget; see "B pipeline mode" below); W0_PIPE=0 keeps the prototype's own month order and command wrappers.
const PIPE_DEFAULT = process.env.W0_PIPE === '0' ? '0' : '1';
function protoRun(mode, scen, a, b, env) {
  const r = cp.spawnSync(process.execPath, [__filename, '--proto-child', mode, scen, String(a || ''), String(b || '')], {
    cwd: ROOT, encoding: 'utf8', maxBuffer: 256 << 20, env: Object.assign({}, process.env, { W0_PIPE: PIPE_DEFAULT }, env || {}),
  });
  const lines = (r.stdout || '').split('\n').filter(l => l.trim() && !/^done /.test(l));
  if (r.status !== 0) throw new Error(`proto ${mode} ${scen} failed (${r.status}): ${(r.stderr || '').slice(-2000)}`);
  return lines;
}
function num(re, lines, i) { for (const l of lines) { const m = l.match(re); if (m) return +m[i || 1]; } return NaN; }
function blockOf(lines, tag) {
  const i = lines.findIndex(l => l.indexOf('[' + tag + ']') === 0);
  if (i < 0) return [];
  let j = i + 1; while (j < lines.length && lines[j].indexOf('[') !== 0 && lines[j].indexOf('==') !== 0) j++;
  return lines.slice(i, j);
}
function protoGates() {
  const Q = !NIGHTLY;
  const runs = [];
  // ---- equivalence: the same prototype campaign with its own econ-proto.js and with js/econ.js prints the same numbers
  {
    const [seeds, years] = Q ? [4, 4] : [12, 10];
    console.log(`== B0 equivalence: prototype campaign classic ${seeds} × ${years} y, econ-proto.js vs js/econ.js (every printed number)`);
    // wall-clock numbers ("[9 s]", "3.4 ms each") are not results
    const strip = L => L.map(l => l.replace(/\[\d+ s\]/g, '').replace(/[0-9.]+ ms each/g, '_ ms each'));
    const a = strip(protoRun('campaign', 'classic', seeds, years, { VARIANTS: 'off,all', SEED0: '0', W0_ECON: 'proto', W0_PIPE: '0' }));
    const b = strip(protoRun('campaign', 'classic', seeds, years, { VARIANTS: 'off,all', SEED0: '0', W0_ECON: 'js', W0_PIPE: '0' }));
    const diff = a.filter((l, i) => l !== b[i]);
    gate('prototype campaign identical with js/econ.js', diff.length === 0 && a.length === b.length && a.length > 5, diff.length ? diff.slice(0, 3).join(' | ') : a.length + ' lines identical');
    // the pipeline mode with every switch off is v3 again: its [off] block equals the prototype wiring's [off] block
    if (PIPE_DEFAULT === '1') {
      const c = strip(protoRun('campaign', 'classic', seeds, years, { VARIANTS: 'off', SEED0: '0', W0_PIPE: '1' }));
      const offB = blockOf(b, 'off'), offC = blockOf(c, 'off');
      const d2 = offB.filter((l, i) => l !== offC[i]);
      gate('pipeline mode, all off == the prototype\'s off (v3: endMonthV4 + model.js Commands + js/econ.js with no provider active)',
        d2.length === 0 && offB.length === offC.length && offB.length >= 4, d2.length ? d2.slice(0, 2).join(' | ') + ' ≠ ' + offC.filter((l, i) => l !== offB[i]).slice(0, 2).join(' | ') : offB.length + ' lines identical');
      info('gates B1–B5 below run the prototype modules on the SHIPPED pipeline (INTEGRATION §6.3 month order: endMonthV4 phases 0–9, model.js Commands + onCommand, the real SG.AIBudget); W0_PIPE=0 measures the prototype\'s own wiring');
    } else info('W0_PIPE=0: gates B1–B5 below use the prototype\'s own month order and command wrappers (not the shipped pipeline)');
  }
  // ---- classic campaign(s)
  const classic = Q ? [[12, 6, 0]] : [[48, 25, 0], [48, 25, 1000]];
  for (const [seeds, years, s0] of classic) {
    const t0 = Date.now();
    console.log(`== B1 prototype campaign classic ${seeds} × ${years} y (SEED0 ${s0}) through js/econ.js`);
    const L = protoRun('campaign', 'classic', seeds, years, { VARIANTS: 'off,all', SEED0: String(s0) });
    L.forEach(l => info(l));
    const off = blockOf(L, 'off'), all = blockOf(L, 'all');
    const yk = years >= 10 ? 10 : years >= 5 ? 5 : 3;
    const al = b => num(new RegExp('y' + yk + ' ([0-9.]+)'), b.slice(0, 1));
    const tolA = Q ? 1.5 : 0.8, tolF = Q ? 1.0 : 0.5, tolD = 2;
    gate(`classic s${s0}: factions alive y${yk} (all ≥ off − ${tolA})`, al(all) >= al(off) - tolA, `${al(off)} → ${al(all)}`);
    const f25 = b => num(/first > 25 %: median ([0-9.]+) y/, b);
    gate(`classic s${s0}: first faction > 25 % (all ≥ off − ${tolF} y)`, f25(all) >= f25(off) - tolF, `${f25(off)} → ${f25(all)} y`);
    const de = b => num(/defections per seed ([0-9.]+)/, b);
    gate(`classic s${s0}: defections per seed (all ≤ off + ${tolD})`, de(all) <= de(off) + tolD, `${de(off)} → ${de(all)}`);
    const below = num(/below 0 after a discretionary spend: (\d+)/, all);
    gate(`classic s${s0}: cities below 0 after a discretionary spend = 0`, below === 0, String(below));
    const disc = num(/discretionary total ([0-9.]+)%/, all), gear = num(/gear ([0-9.]+)%/, all);
    gate(`classic s${s0}: discretionary ≤ 22 % of gross`, disc <= 22.5, disc + ' %');
    if (!Q) gate(`classic s${s0}: AI gear spending 8–15 % of gross`, gear >= 8 && gear <= 15, gear + ' %');
    else info(`(quick) AI gear spending ${gear} % of gross (band 8–15 % checked nightly)`);
    const fame = num(/fame bonus avg (?:[0-9.]+ \/ )*([0-9.]+)\s*$/, all.filter(l => /fame bonus avg/.test(l)));
    gate(`classic s${s0}: fame bonus (last year) ≤ +10`, fame <= 10, String(fame));
    const cap = num(/at the \+35 % cap ([0-9.]+)% of city-months/, all);
    info(`income at the +35 % cap: ${cap} % of city-months  [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
    runs.push({ kind: 'campaign classic', seeds, years, s0, lines: L });
  }
  // ---- world campaign
  {
    const [seeds, years] = Q ? [4, 6] : [24, 15];
    const t0 = Date.now();
    console.log(`== B2 prototype campaign world ${seeds} × ${years} y through js/econ.js`);
    const L = protoRun('campaign', 'world', seeds, years, { VARIANTS: 'off,all', SEED0: '0' });
    L.forEach(l => info(l));
    const off = blockOf(L, 'off'), all = blockOf(L, 'all');
    const yk = years >= 15 ? 15 : years >= 5 ? 5 : 3;
    const al = b => num(new RegExp('y' + yk + ' ([0-9.]+)'), b.slice(0, 1));
    const tol = Q ? 4 : 2;
    gate(`world: factions alive y${yk} (all ≥ off − ${tol})`, al(all) >= al(off) - tol, `${al(off)} → ${al(all)}`);
    const de = b => num(/defections per seed ([0-9.]+)/, b);
    gate('world: defections per seed (all ≤ off + 2)', de(all) <= de(off) + 2, `${de(off)} → ${de(all)}`);
    const below = num(/below 0 after a discretionary spend: (\d+)/, all);
    gate('world: cities below 0 after a discretionary spend = 0', below === 0, String(below));
    const disc = num(/discretionary total ([0-9.]+)%/, all);
    gate('world: discretionary ≤ 22 % of gross', disc <= 22.5, disc + ' %');
    info(`[${((Date.now() - t0) / 1000).toFixed(0)} s]`);
    runs.push({ kind: 'campaign world', seeds, years, lines: L });
  }
  // ---- tactical
  {
    const seeds = Q ? 2 : 8;
    const t0 = Date.now();
    console.log(`== B3 prototype tactical classic ${seeds} (year-8 all-on armies, v4 battle effects off / on, crits) through js/battle-math.js`);
    const L = protoRun('tactical', 'classic', seeds, '');
    L.forEach(l => info(l));
    const shift = num(/shift on − off (-?[0-9.]+) pp/, L), dmg = num(/damage per battle on \/ off ×([0-9.]+)/, L);
    gate('tactical: attacker win shift ≤ 4 pp', Math.abs(shift) <= 4, shift + ' pp');
    gate('tactical: troops lost per battle on / off ≤ 1.35', dmg <= 1.35, '×' + dmg);
    const crit = num(/on\s+: attacker win [0-9.]+%.*crit rate ([0-9.]+)%/, L);
    gate('tactical: army crit rate 4–9 % of attacks (COMBAT-V4 §2.7)', crit >= 4 && crit <= 9.5, crit + ' %');
    info(`[${((Date.now() - t0) / 1000).toFixed(0)} s]`);
    runs.push({ kind: 'tactical', seeds, lines: L });
  }
  // ---- stacks
  {
    console.log('== B4 prototype stacks (INTEGRATION §8.1) through js/econ.js');
    const L = protoRun('stacks', 'classic', '', '');
    L.forEach(l => info(l));
    const capped = L.slice(L.findIndex(l => l.indexOf('[capped]') === 0));
    const atk = num(/atk ×([0-9.]+)/, capped), def = num(/def ×([0-9.]+)/, capped);
    gate('stacks: hand-built perfect general stays ≤ ×1.60 (atk, def)', atk <= 1.6 + 1e-9 && def <= 1.6 + 1e-9, `atk ×${atk} def ×${def}`);
    const inc = capped.find(l => /city income/.test(l)) || '';
    const pct = num(/→ pct ([0-9.]+)/, [inc]);
    gate('stacks: city income clamps at +35 %', pct <= 0.35 + 1e-9, String(pct));
    const sp = num(/搜索 p: raw sum [0-9.]+ → ([0-9.]+)/, capped);
    gate('stacks: search p clamps at 0.90', sp <= 0.9 + 1e-9, String(sp));
    runs.push({ kind: 'stacks', lines: L });
  }
  // ---- scripted peaceful player
  {
    const sets = Q ? [['classic', 1, 6, 0]] : [['classic', 6, 20, 0], ['classic', 6, 20, 500], ['world', 4, 15, 0]];
    for (const [scen, seeds, years, s0] of sets) {
      const t0 = Date.now();
      console.log(`== B5 prototype scripted peaceful player ${scen} ${seeds} × ${years} y (SEED0 ${s0}) through js/econ.js`);
      const L = protoRun('player', scen, seeds, years, { POL: 'v3,all', SEED0: String(s0) });
      L.forEach(l => info(l));
      const v3 = blockOf(L, 'v3'), all = blockOf(L, 'all');
      const yk = [1, 3, 5, 10, 15, 20].filter(y => y <= years).pop();   // the prototype reports these years
      const sv = b => num(new RegExp('y' + yk + ' ([0-9.]+)%'), b.slice(0, 1));
      gate(`player ${scen} s${s0}: survival y${yk} (all ≥ v3 − 8 pp)`, sv(all) >= sv(v3) - 8, `${sv(v3)} % → ${sv(all)} %`);
      const great = num(/开发 tiers .*great ([0-9.]+)%/, all), good = num(/开发 tiers .*good ([0-9.]+)%/, all);
      gate(`player ${scen} s${s0}: 开发 tiers great 5–16 %, good 18–40 %`, great >= 5 && great <= 16 && good >= 18 && good <= 40, `great ${great} %, good ${good} %`);
      const hg = num(/秋收 tiers, consolidated rule .*good ([0-9.]+)%/, all);
      if (!Q) gate(`player ${scen} s${s0}: 秋收 good 18–40 % (rollRaw ≥ 1.04)`, hg >= 18 && hg <= 40, hg + ' %');
      else info(`(quick) 秋收 good ${hg} % (checked nightly; one seed has too few harvests)`);
      if (!Q && scen === 'classic') {
        const wd = num(/王道 .*?: \d+\/\d+ \(([0-9.]+)%\)/, all);
        gate(`player classic s${s0}: 王道 reached 10–40 %`, wd >= 10 && wd <= 40, wd + ' %');
      }
      info(`[${((Date.now() - t0) / 1000).toFixed(0)} s]`);
      runs.push({ kind: 'player ' + scen, seeds, years, s0, lines: L });
    }
  }
  return runs;
}

// ------------------------------------------------------------------------------------- B pipeline mode --
// W0_PIPE=1 (the default for the B gates): the prototype's rule modules drive the SHIPPED integration core instead of their own
// wiring — INTEGRATION §6.3 says tests/v4-all.js re-measures the gates on the real month order:
//   · js/registry.js + js/econ.js + js/battle-math.js are loaded into the prototype's game (ENGAGEMENT's loader), so
//     StrategyAI.endMonth is the real endMonthV4 (phases 0–9), model.js Commands go through the real SG.Econ, and
//     SG.AIBudget is the real singleton (purse in G.aiBudget, i.e. per game);
//   · the prototype's provider table P is forwarded into SG.Mods.provide (when the variant is on);
//   · its month work is registered as phase hooks: 'snapshot' (bond shields), 'items' (ITEMS restock, hidden items),
//     'engage' (ENGAGEMENT's month end: order, projects, caravans, events, tribute, 文会 …), 'defections' (count),
//     'titles' (Titles.monthStart), onSpend title → gear → project → festival per AI faction (phase 7), 'tourney', 'news'
//     (syncFV) — defections now run AFTER ITEMS and ENGAGEMENT (the prototype ran them inside v3's month, before);
//   · the command side effects of ENGAGEMENT and the prototype (domestic exp, order cost of recruits, relations, TITLES'
//     离间 loss reduction, stats) come back through Commands.onCommand.
//   ITEMS' and ENGAGEMENT's own month-end code keeps the calendar view it was written for (the month being entered), so their
//   seeded streams and calendar rules (文会 in month 9, the tournament flag in month 1, festivals) are unchanged; only their
//   position in the month moved. The tactical gate keeps the prototype's battle wiring (battle-model.js gets SG.Mods with
//   W5's patch, INTEGRATION request 1) but reads the real singleton.
const PIPE_ENGAGE = [   // design-v4/engage/engage-proto.js (read through the child's fs proxy)
  ["for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);",
   "for (const f of ['core.js', 'registry.js', 'world-geo.js', 'data.js', 'world-data.js', 'econ.js']) load(f);"],
  ["for (const f of ['battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js']) load(f);",
   "for (const f of ['battle-math.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js']) load(f);"],
  ['  const month = g.month;\n  const base = g.cities.map(c => (c.owner >= 0 ? inc(c) : 0));\n  const popB = g.cities.map(c => c.population);\n  const foodB = g.cities.map(c => c.food);',
   '  if (SG.__pipe && !SG.__engMid) return oEnd.apply(this, arguments);   // W0 pipeline: the month is endMonthV4; this body is its phase 3\n' +
   '  const month = g.month;\n  const base = g.cities.map(c => (c.owner >= 0 ? inc(c) : 0));\n' +
   '  const popB = SG.__engSnap ? SG.__engSnap.popB : g.cities.map(c => c.population);\n  const foodB = SG.__engSnap ? SG.__engSnap.foodB : g.cities.map(c => c.food);'],
  ['  const news = oEnd.apply(this, arguments);\n  for (const [x, add] of boosted)', '  const news = SG.__engMid ? SG.__engMid() : oEnd.apply(this, arguments);\n  for (const [x, add] of boosted)'],
  ['function aiProjects() {\n  const g = G();\n  for (const c of g.cities) {\n    if (c.owner < 0 || c.owner === g.player || c._e.proj) continue;',
   'function aiProjects(onlyF) {\n  const g = G();\n  for (const c of g.cities) {\n    if (c.owner < 0 || c.owner === g.player || c._e.proj || (onlyF !== undefined && c.owner !== onlyF)) continue;'],
];
const PIPE_PROTO = [    // design-v4/integration/v4-all-proto.js (its own source text; \n inside its string literals stays escaped)
  // the v3 "gold < 0" counter also on phaseIncome's copy of the rule (second occurrence in strategy-ai.js)
  [String.raw`    ['if (c.gold < 0) {\n          c.gold = 0;', 'if (c.gold < 0) {\n          if (SG.__neg) SG.__neg(c);\n          c.gold = 0;'],`,
   String.raw`    ['if (c.gold < 0) {\n          c.gold = 0;', 'if (c.gold < 0) {\n          if (SG.__neg) SG.__neg(c);\n          c.gold = 0;'],
    ['if (c.gold < 0) {\n          c.gold = 0;', 'if (c.gold < 0) {\n          if (SG.__neg) SG.__neg(c);\n          c.gold = 0;'],`],
  // story exp: model.js Progress.gain multiplies 'story' / 'battle' itself once SG.Mods exists (no double multiplier)
  ['routine || !SG.__v4 ? amt : SG.__v4.Mods.exp(gen, amt)', 'amt'],
  ["Progress.gain = function (gen, amount, why) { if (V.on && why === 'battle') amount", "Progress.gain = function (gen, amount, why) { if (V.on && why === 'battle' && !SG.__pipe) amount"],
  // festivals per faction (phase 7 runs per AI faction)
  ["const __fest = () => { for (const F of g.factions) {'", "const __fest = (onlyF) => { for (const F of g.factions) { if (onlyF !== undefined && F.id !== onlyF) continue;'"],
  // the raw model.js commands (before ENGAGEMENT's wrappers)
  ['oDev, oTrain, oRec, oSearch, oDiscord };', 'oDev, oTrain, oRec, oSearch, oDiscord, oHire, oAlly, oPersuade, oAllyCh };'],
  ['const SG = EN.SG, { E, GameState, C, StrategyAI, Conquest, B, M, Progress } = EN;', 'const SG = EN.SG, { E, GameState, C, StrategyAI, Conquest, B, M, Progress } = EN;\nSG.__pipe = true;'],
  ['function installCommands() {', 'function installCommands() { if (SG.__pipe) return __pipeCommands();'],
  ['function installEndMonth() {', 'function installEndMonth() { if (SG.__pipe) return __pipeEndMonth();'],
  ['// ------------------------------------------------------------ runs --', String.raw`// ------------------------------------------------------------ W0 pipeline (tests/v4-all.js) --
function __pipeCommands() {
  // model.js Commands (through SG.Econ + the forwarded providers); the side effects return through Commands.onCommand
  C.develop = EN.oDev; C.train = EN.oTrain; C.recruit = EN.oRec; C.search = EN.oSearch; C.hire = EN.oHire; C.ally = EN.oAlly;
  C.persuade = EN.oPersuade; C.allyChance = EN.oAllyCh;
  let discBefore = 0;
  C.discord = function (agent, target) { discBefore = target.loyalty; return EN.oDiscord.apply(this, arguments); };
  C.onCommand((cmd, r) => {
    if (!V.on) return;
    const g = G(), gen = r.gen != null ? g.generals[r.gen] : null, c = r.city != null ? g.cities[r.city] : null;
    if (cmd === 'develop') {
      if (!r.ok) return;
      if (c.owner === g.player) V.stats.dev.push({ key: r.kind, before: r.before, after: r.after, g0: r.g0, gain: r.gain, roll: r.roll, pct: r.pct, city: c });
      V.stats.devPct.push(r.pct);
      EN.domExp(gen, 6 + (r.g0 >= 20 ? 2 : 0));
    } else if (cmd === 'recruit') {
      if (!r.ok) return;
      EN.domExp(gen, 3);
      if (E.order && c._e) c._e.order = clamp(c._e.order - Math.ceil(r.n / 3000), 0, 100);
    } else if (cmd === 'train') { if (r.ok) EN.domExp(gen, 3); }
    else if (cmd === 'search') {
      V.stats.searchP.push(r.p);
      if (r.outcome === 'talent') { EN.domExp(gen, 12); if (gen && gen.faction >= 0) EN.cnt(gen.faction, 'hiddenFound', 1); } else EN.domExp(gen, 4);
    } else if (cmd === 'hire') { EN.domExp(gen, r.ok ? 10 : 2); if (r.ok) EN.cnt(g.generals[r.target].faction, 'hires', 1); }
    else if (cmd === 'ally') { V.stats.allyP.push(r.p); if (g.allied(r.f, r.target)) EN.addRel(r.f, r.target, 10); }
    else if (cmd === 'discord') {
      if (r.reason === 'ruler') return;
      const t = g.generals[r.target];
      if (r.ok) {   // TITLES' prototype rule: an office-holder loses 8 % less loyalty per tier (INTEGRATION §4.5 puts it on the chance)
        const d2 = t._t ? Math.max(1, Math.round(r.d * (1 - 0.08 * t._t.tier))) : r.d;
        t.loyalty = Math.max(0, discBefore - d2);
        EN.domExp(gen, t.loyalty < discBefore ? 6 : 2);
      } else EN.domExp(gen, 2);
      EN.addRel(gen.faction, t.faction, -5);
    } else if (cmd === 'persuade') { const t = g.generals[r.target]; EN.domExp(gen, r.ok ? 15 : 2); EN.addRel(r.f, r.ok ? r.from : t.faction, -8); }
  });
}
function __pipeEndMonth() {
  const eng = StrategyAI.endMonth;           // ENGAGEMENT's wrapper; with SG.__pipe it passes straight to endMonthV4
  const on = { owner: 'proto', when: () => V.on };
  const asNext = (g, fn) => { const y = g.year, m = g.month; g.month++; if (g.month > 12) { g.month = 1; g.year++; } try { return fn(); } finally { g.year = y; g.month = m; } };
  StrategyAI.on('snapshot', ctx => { const g = ctx.g; V.shields = bondShields(); SG.__engSnap = { popB: g.cities.map(c => c.population), foodB: g.cities.map(c => c.food) }; }, on);
  StrategyAI.on('items', ctx => asNext(ctx.g, () => IT.monthEnd()), on);
  StrategyAI.on('engage', ctx => {
    const g = ctx.g, y = g.year, m = g.month;
    SG.__engMid = () => { g.month++; if (g.month > 12) { g.month = 1; g.year++; } return ctx.news; };
    try { eng.call(StrategyAI); } finally { g.year = y; g.month = m; SG.__engMid = null; SG.__engSnap = null; }
  }, on);
  StrategyAI.on('defections', ctx => { for (const d of ctx.defections) if (ctx.g.factions[d.from].alive) E.stats.defect++; }, on);
  StrategyAI.on('titles', () => TI.monthStart(), on);
  StrategyAI.onSpend('title', f => TI.aiManage(f), on);
  StrategyAI.onSpend('gear', f => { IT.aiEquip(f); IT.aiPhase(f); }, on);
  StrategyAI.onSpend('project', f => { if (E.aiProj) EN.aiProjects(f); }, on);
  StrategyAI.onSpend('festival', f => { const fe = SG.__v4 && SG.__v4.deferred.fest; if (fe) fe(f); }, on);
  StrategyAI.on('discretionary', ctx => {   // the scripted player's faction (the real phase 7 skips the player)
    const g = ctx.g, p = g.player;
    if (p >= 0 && g.factions[p].alive) {
      AIB.accrue(p, ctx.gross[p] || 0);
      if (V.playerPol === 'all') { TI.aiManage(p); IT.aiEquip(p); if (V.playerShops) IT.aiPhase(p); }
      const fe = SG.__v4.deferred.fest; if (fe) fe(p);
    }
    SG.__v4.deferred.fest = null;
  }, on);
  StrategyAI.on('tourney', () => { if (SG.__v4.deferred.tourney) { SG.__v4.deferred.tourney = false; TO.run(); } }, on);
  StrategyAI.on('news', ctx => { for (const F of ctx.g.factions) if (F.alive) EN.syncFV(F.id); }, on);
  // the campaign statistics the prototype took inside its own income hooks
  const E4 = SG.Econ, oGold = E4.cityGold, oHarv = E4.harvest;
  E4.cityGold = function (c) { const r = oGold.call(this, c); if (V.on && V.stats) { V.stats.incomePct.push(r.pct); if (r.pct >= Mods.CAPS.incomeHi - 1e-9) V.stats.incomeAtCap++; V.stats.incomeN++; } return r; };
  E4.harvest = function (c, roll) { const r = oHarv.call(this, c, roll); if (V.on && V.stats && c.owner === G().player) V.stats.harv.push({ rollRaw: roll, land: c.land, h: r.h, pct: r.pct }); return r; };
  // a destroyed faction's armoury goes to the conqueror (ITEMS §8.8) — as in installEndMonth
  const oApply = Conquest.apply;
  Conquest.apply = function (s) {
    const owner = s.target.owner;
    const r = oApply.apply(this, arguments);
    if (V.on && owner >= 0 && !G().factions[owner].alive && s.attackerWon) IT.transferArmoury(owner, s.attacker);
    return r;
  };
}

// ------------------------------------------------------------ runs --`],
];
function patchText(src, pairs, what) {
  for (const [a, b] of pairs) {
    const i = src.indexOf(a);
    if (i < 0) throw new Error(what + ': pipeline anchor missing: ' + a.slice(0, 90));
    if (src.indexOf(a, i + a.length) >= 0) throw new Error(what + ': pipeline anchor not unique: ' + a.slice(0, 90));
    src = src.slice(0, i) + b + src.slice(i + a.length);
  }
  return src;
}
// the real SG.Mods / SG.Econ / SG.AIBudget singleton handed to the prototype's build(); its provider table P is forwarded
function pipeShim() {
  let Pcur = null, installed = false;
  const NAMES = ['admin', 'aiSearchP', 'aiTargetK', 'allied', 'allyMonths', 'auraAtk', 'auraDef', 'baseFame', 'baseVirtue', 'bondAtk', 'bondDef', 'bondShield', 'cityDef',
    'cityProtected', 'cmdAtk', 'cmdDef', 'discordRes', 'eventFame', 'eventHarvest', 'eventIncome', 'eventVirtue', 'expBoost', 'gearAtk', 'gearCrit', 'gearCritMul',
    'gearDef', 'gearDev', 'gearDip', 'gearDiscord', 'gearFame', 'gearHire', 'gearMorale', 'gearPersuade', 'gearRecruit', 'gearReward', 'gearSearch', 'gearTrain',
    'gearVirtue', 'gearWar', 'itemCity', 'itemLoyAura', 'needK', 'noAttack', 'order', 'pactIncome', 'persuadeRes', 'prestigeFame', 'project', 'rel', 'titleAtk',
    'titleCity', 'titleCrit', 'titleDef', 'titleDev', 'titleDip', 'titleDiscord', 'titleHire', 'titleLoyAura', 'titleMorale', 'titleNat', 'titlePersuade',
    'titleSearch', 'titleShield', 'titleTrain', 'tradeK', 'vetLevel'];
  return {
    CAPS: null,
    create(SGp, P, opts) {
      void opts;   // SHARE / CAPM: the real purse reads SG.Balance.Decisions.aiPurseShare (0.22) and AIBudget.CapMonths (12)
      Pcur = P;
      const M4 = SGp.Mods;
      Object.assign(M4.CAPS, SGp.EconCore.BASE_CAPS, SGp.Balance.Caps);   // the nocap variant mutates CAPS: restore every build
      if (!installed) {
        installed = true;
        const t = {};
        for (const n of NAMES) {
          const rep = M4.REPLACE.has(n), any = M4.ANY.has(n);
          t[n] = (a, b, c) => { const f = Pcur[n]; return f ? f(a, b, c) : rep ? undefined : any ? false : 0; };
        }
        // items-mod's eff already subtracts gen.injury.war; the real SG.Mods.eff subtracts it once more → add it back
        t.eff = (x, k) => { const f = Pcur.eff; if (!f) return undefined; const v = f(x, k); return k === 'war' ? v + M4.injuryWar(x) : v; };
        // the prototype's text hooks on v3's endMonth / power() / recruit become providers of the real pipeline
        t.popGrowth = c => (SGp.__v4 ? SGp.__v4.popK(c) - 1 : 0);
        t.autoPower = x => (SGp.__v4 ? SGp.__v4.power(x) : undefined);
        t.recruitDrill = c => (c._e && c._e.done && c._e.done.has('drill') ? 10 : 0);
        M4.provide(t, { owner: 'v4-all-proto', when: () => !!SGp.__v4 });
      }
      SGp.AIBudget.reset();
      return { CAPS: M4.CAPS, Mods: M4, Econ: SGp.Econ, AIBudget: SGp.AIBudget };
    },
  };
}

function protoChild(mode, scen, a, b) {
  const DV4 = path.join(ROOT, 'design-v4', 'integration');
  const PIPE = process.env.W0_PIPE === '1' && process.env.W0_ECON !== 'proto';
  const own = load({ files: ['core.js', 'registry.js', 'data.js', 'econ.js', 'battle-math.js'] });
  const shim = PIPE ? pipeShim() : {
    CAPS: own.EconCore.BASE_CAPS,
    create(SGp, P, opts) {
      const core = own.EconCore.create(SGp, P, { share: opts && opts.share, capMonths: opts && opts.capMonths, injury: false, purse: 'map' });
      own.BattleMathCore.extend(core.Mods, SGp, P);
      return core;
    },
  };
  let src = fs.readFileSync(path.join(DV4, 'v4-all-proto.js'), 'utf8').replace(/^'use strict';/, '');
  if (process.env.W0_ECON !== 'proto') {
    const anchor = "const ECON = require('./econ-proto.js');";
    if (src.indexOf(anchor) < 0) throw new Error('v4-all-proto.js anchor missing: ' + anchor);
    src = src.replace(anchor, 'const ECON = __W0_ECON;');
  }
  if (PIPE) src = patchText(src, PIPE_PROTO, 'v4-all-proto.js');
  const ENGF = path.join(ROOT, 'design-v4', 'engage', 'engage-proto.js');
  const outDir = fs.mkdtempSync(path.join(os.tmpdir(), 'w0-proto-'));
  const fsProxy = Object.assign({}, fs, {
    readFileSync(p, o) {
      const d = fs.readFileSync(p, o);
      return PIPE && typeof p === 'string' && path.resolve(p) === ENGF ? patchText(String(d), PIPE_ENGAGE, 'engage-proto.js') : d;
    },
    writeFileSync(p, d) { if (typeof p === 'string' && p.indexOf(path.join(DV4, 'results')) === 0) p = path.join(outDir, path.basename(p)); return fs.writeFileSync(p, d); },
  });
  const Mod = require('module');
  const mkReq = file => { const r = Mod.createRequire(file); return m => (m === 'fs' ? fsProxy : m === 'module' ? modProxy : r(m)); };
  const modProxy = Object.assign(Object.create(Mod), { createRequire: f => mkReq(f) });
  const fakeProcess = { argv: [process.argv[0], path.join(DV4, 'v4-all-proto.js'), mode, scen].concat(a ? [a] : []).concat(b ? [b] : []), env: process.env, stdout: process.stdout, exit: process.exit.bind(process), hrtime: process.hrtime };
  const fn = new Function('require', 'module', '__dirname', '__filename', 'process', '__W0_ECON', '"use strict";' + src);
  fn(mkReq(path.join(DV4, 'v4-all-proto.js')), { exports: {} }, DV4, path.join(DV4, 'v4-all-proto.js'), fakeProcess, shim);
}

// =========================================================================================== main =====
if (ARGS[0] === '--proto-child') { protoChild(ARGS[1], ARGS[2], ARGS[3], ARGS[4]); return; }
console.log(`v4-all ${NIGHTLY ? 'nightly' : 'quick'} (${ONLY})`);
try { if (ONLY !== 'proto') nativeGates(); } catch (e) { fails++; console.log('  FAIL native harness threw: ' + (e.stack || e)); }
try { if (ONLY !== 'native') protoGates(); } catch (e) { fails++; console.log('  FAIL prototype harness threw: ' + (e.stack || e)); }
console.log(`${checks} gates, ${fails} failed  (${((Date.now() - T0) / 1000).toFixed(0)} s)`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
