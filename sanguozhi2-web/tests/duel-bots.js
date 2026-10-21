'use strict';
/* ==========================================================================
   单挑格斗 人机强度与规则回归（Node，无依赖）——第二版 §4E、第三版格开 §3、第四版 COMBAT-V4 §15
   脚本玩家（SG.DuelGame.scriptBot：固定套路，规则随机数走 SG.Random，可复现）对战真实电脑（SG.DuelGame._AI）。
   (1)–(7) 第三版的目标「精神不变」（同样的对局、同样的场数；第四版的按键与招式谱下）：
     (1) 跳入 / 只按 J 连按不能以弱胜强 · (2) 连按对同武力 72–76 的电脑平均 ≤ 30%，武力 ≤ 40 仍好欺负，90 以上难但能赢 ·
     (3) 电脑对电脑：武力决定胜负、同将五五开、刘备同将 20–40 秒且超时 ≤ 10%、强将会格开、弱将不会 ·
     (4) 格开规则（脚本化）：窗口 0.15–0.22 秒、成功 / 落空 / 冷却 / 反击；受击硬直中的格开改为「连段第 3 步起、按来招类型的窗口」·
     (5) 格开 / 连按格开机器人的胜率表 · (6) 重击与跃劈的反应（跃劈起手 ≥ 0.30 秒）· (7) 防守型打法与武力阶梯
   (8) 后退格挡：与防键格挡的胜率差 ≤ 6 个百分点；近身判定（有威胁时后退不动）；命中那一步才按后退也挡得住；越身 0.10 秒宽限；空中不能挡
   (9) 下段 / 上段：只能蹲挡 / 站挡；削血 ×0.2；人的反应能站挡跃劈；电脑看跃劈（强 ≥ 70%、弱 ≤ 40%）；电脑挡二择的下段 35–75%（≤ 80%）；
       换挡不早于反应时间；mixup 对同武力电脑 ≤ 60%
   (10) 连段规则 a–k（脚本化、确定性）· (11) 看破连段（脚本化）· (12) 连段对电脑（平衡）· (13) 单挑会心 · (14) 性能 ·
   (15) 装备在单挑里（预算、各稀有度最佳一套、15 武力差、白名单）与兵器类型平衡（各兵器对长剑 45–55%）
   用法：node tests/duel-bots.js [每组场数，默认 40] [种子，默认 7] [--quick]（(10) 每人 4 次而非 20 次） [--nightly]（(15) 每格 300 场，缺省 60 场）
        [--only 1,4,10]（只跑这几节）      退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
// DUEL_GAME_JS=路径：用另一份 duel-game.js 跑同一套测试（试改电脑 AI 时对比用）
for (const f of ['core.js', 'data.js', 'world-data.js', 'combo-data.js', 'combos.js', 'duel-game.js']) {
  const file = f === 'duel-game.js' && process.env.DUEL_GAME_JS ? process.env.DUEL_GAME_JS : path.join(root, f);
  vm.runInThisContext(fs.readFileSync(file, 'utf8'), { filename: f });
}
const SG = global.SG, D = SG.DuelGame, MV = D.MOVES, ST = D.STEP, PR = D.PARRY, CO = D.COMBO, CR = D.CRIT;
const ARGS = process.argv.slice(2), POS = ARGS.filter(a => !a.startsWith('--'));
const QUICK = ARGS.includes('--quick');
const NIGHTLY = ARGS.includes('--nightly');      // (15) 每格 300 场、兵器平衡按 45–55%
const N = +POS[0] || 40;
const SEED = +POS[1] || 7;
const onlyI = ARGS.indexOf('--only'), ONLY = onlyI >= 0 ? (ARGS[onlyI + 1] || '').split(',') : null;
SG.Random.seed(SEED);
const T0 = Date.now();

const gens = {};
for (const s of SG.ScenarioData.Generals) { const p = s.split('|'); gens[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3] }; }
const worldGens = {};
for (const s of SG.Scenarios.world.Generals) { const p = s.split('|'); worldGens[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], culture: p[9] || 'han' }; }

let fails = 0, checks = 0;
function ok(cond, msg) { checks++; if (!cond) { fails++; console.log('  FAIL: ' + msg); } }
function section(id, name, fn) {
  if (ONLY && ONLY.indexOf(String(id)) < 0) return;
  const t = Date.now();
  console.log('== (' + id + ') ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAILED: ' + (e.stack || e)); }
  console.log(`  [${((Date.now() - t) / 1000).toFixed(1)} s]`);
}
const near = (a, b, eps) => Math.abs(a - b) <= eps;
const avg = a => a.reduce((s, x) => s + x, 0) / Math.max(1, a.length);
const TK = { L: 'light', M: 'medium', H: 'heavy' };

// BOTS-BEGIN（本文件另加的脚本玩家，规则随机数同样走 SG.Random）
//   turtle 龟缩：电脑在兵器距离 +1.2 米内就一直按住格挡，只抓看得见的破绽（收招 / 落空 / 被格开；蓄力够得着也打），
//          在跟前蓄力的一半会被他上前打断；隔 1.5–2.5 秒才试探一下；从不格开——量电脑会不会用满蓄重击破防（在他够不着处蓄满）
//   cautious 谨慎：同 guard，但知道「被挡之后轮到对方」：自己的试探被挡就继续举防约 0.3–0.5 秒，一半时候按人的节奏格开电脑抢回合的那一刀
//   turtleP 龟缩 + 格开：同 turtle，另看见电脑松开蓄到六成以上的重击就按人的反应（0.22 ± 0.04 秒）格开
//   backstepper 后撤：同 guard，自己的试探被挡就立刻后撤步，再抓电脑挥空的收招（看见 0.2 秒后冲刺斩）
const gz = () => (SG.Random.value() + SG.Random.value() + SG.Random.value() + SG.Random.value() - 2) * 1.732;
const HELD = () => ({ x: 0, up: false, down: false, light: false, medium: false, heavy: false, guard: false, special: false, parry: false, dash: 0 });
const TAPS = () => ({ light: 0, medium: 0, up: 0, special: 0, heavy: 0, dir: 0, parry: 0, ovh: 0 });
function turtleBot(idx) {
  const c = { held: HELD(), taps: TAPS() };
  let nt = -1, cSeq = -1, cHit = false, chSeq = -1;
  c.update = (dt, sim) => {
    const me = sim.f[idx], o = sim.f[1 - idx], d = Math.abs(o.x - me.x), now = sim.clock, H = c.held, T = c.taps;
    const reach = me.reach * MV.L.reachK + 0.2 - 0.03, free = sim.actionable(me) || me.state === 'guard';
    H.x = 0; H.heavy = false;
    if (nt < 0) nt = now + 1 + SG.Random.value();
    if (me.state === 'parryok' || sim.countering(me)) { H.guard = false; if (d <= reach + 0.05) T.light++; else H.x = me.face; return; }
    if (me.state === 'str' && me.strOn && me.strIdx < 2 && me.hitIdx > 0 && o.state === 'hitstun' && chSeq !== me.seq) { chSeq = me.seq; T[TK[me.seqStr[me.strIdx + 1]]]++; return; }
    const seen = sim.seen(o.idx, 0.2);
    if (seen && seen.state === 'charge' && seen.seq !== cSeq) { cSeq = seen.seq; cHit = SG.Random.value() < 0.5; }   // 看见对手在跟前蓄力：一半时候上前打断
    const open = seen && o.seq === seen.seq && (seen.phase === 'recovery' || seen.state === 'parrywhiff' || seen.state === 'parried' || (seen.state === 'charge' && cHit));
    if (open && d <= reach + 0.1 && free) { H.guard = false; T.light++; return; }
    if (me.rage >= 100 && d < reach + 1 && sim.actionable(me)) { H.guard = false; T.special++; return; }
    if (d > o.reach + 1.2) { H.guard = false; H.x = me.face; return; }
    if (now > nt && d <= reach * 0.95 && free && !(seen && seen.atk)) { nt = now + 1.5 + SG.Random.value(); H.guard = false; T.light++; return; }
    H.guard = true;
  };
  return c;
}
// 第三版原样：cautious 一半时候赌格开（复核用第三版 duel-game.js 实测：MID8 × 20 场格开 700 次、成功 76 次、胜 37.5%，与第三版的 36% 相符）。
// 不赌格开的版本另列为 cautiousN（只打印，不设目标）
function cautiousBot(idx, gamble) {
  const base = D.scriptBot('guard', idx), c = { held: base.held, taps: base.taps };
  let holdUntil = 0, pAt = -1, gSeq = -1;
  c.update = (dt, sim) => {
    const me = sim.f[idx], o = sim.f[1 - idx], now = sim.clock, H = c.held, T = c.taps;
    // 自己的出手被挡（对手进入防御硬直）：料定对手硬直一结束就还手
    if (o.state === 'guardstun' && o.seq !== gSeq && me.mv && me.hitIdx > 0) {
      gSeq = o.seq;
      const freeAt = now + Math.max(0, o.stun - o.t);
      holdUntil = freeAt + 0.3 + 0.2 * SG.Random.value();
      if (gamble && sim.parryReady(me) && SG.Random.value() < 0.5) pAt = freeAt + MV.L.startup / o.spd - me.parryW * 0.5 + 0.045 * gz();
    }
    base.update(dt, sim);
    if (me.state === 'parryok' || sim.countering(me)) { pAt = -1; holdUntil = 0; return; }
    if (pAt >= 0 && now >= pAt && (sim.actionable(me) || me.state === 'guard' || me.state === 'guardstun')) { T.parry++; pAt = -1; }
    if (now < holdUntil && (sim.actionable(me) || me.state === 'guard')) { H.guard = true; H.x = 0; T.light = 0; T.medium = 0; T.heavy = 0; T.special = 0; }
  };
  return c;
}
function turtleParryBot(idx) {
  const base = turtleBot(idx), c = { held: base.held, taps: base.taps };
  let hvSeq = -1, pAt = -1;
  c.update = (dt, sim) => {
    const me = sim.f[idx], o = sim.f[1 - idx], now = sim.clock;
    base.update(dt, sim);
    if (o.state === 'heavy' && o.seq !== hvSeq) {
      hvSeq = o.seq;
      if (o.charge >= 0.6 && sim.parryReady(me)) pAt = now - o.t / o.spd + Math.max(0.12, 0.22 + 0.04 * gz());
    }
    if (pAt >= 0 && now >= pAt) { c.taps.parry++; pAt = -1; }
    if (me.state === 'parryok' || sim.countering(me)) pAt = -1;
  };
  return c;
}
function backstepBot(idx) {
  const base = D.scriptBot('guard', idx), c = { held: base.held, taps: base.taps };
  let gSeq = -1, bsPending = false, pun = -1;
  c.update = (dt, sim) => {
    const me = sim.f[idx], o = sim.f[1 - idx], H = c.held, T = c.taps, d = Math.abs(o.x - me.x);
    if (o.state === 'guardstun' && o.seq !== gSeq && me.mv && me.hitIdx > 0) { gSeq = o.seq; bsPending = true; }
    base.update(dt, sim);
    H.dash = 0;
    if (bsPending) {
      if (sim.actionable(me) || me.state === 'guard') { H.guard = false; H.dash = -me.face; T.light = 0; bsPending = false; }
      else T.light = 0;
      return;
    }
    // 电脑挥空的收招（看见 0.2 秒后）：近则出手，稍远冲刺斩
    const seen = sim.seen(o.idx, 0.2), reach = me.reach * MV.L.reachK + 0.2 - 0.03;
    if (seen && o.seq === seen.seq && o.mv && seen.phase === 'recovery' && o.hitIdx === 0 && o.seq !== pun && (sim.actionable(me) || me.state === 'guard' || me.state === 'backstep') && d < reach + 1.2) {
      pun = o.seq; H.guard = false; if (d > reach) H.dash = me.face; T.light++;
    }
  };
  return c;
}
const BOTS = { turtle: turtleBot, cautious: i => cautiousBot(i, true), cautiousN: i => cautiousBot(i, false), turtleP: turtleParryBot, backstepper: backstepBot };
// BOTS-END

// 脚本玩家（左，f[0]）对 真实电脑（右）；返回玩家胜率 % 与统计
// by：格开按「谁 途径/招式」分类计数；heavies：双方松手的重击数（满蓄 / 全部）；aiStats：电脑一方的 outcome 统计累计
function play(kind, p, a, n, opts) {
  opts = opts || {};
  const P = opts.genP || gens[p], A = opts.genA || gens[a];
  let w = 0, parries = 0, tries = 0, aiParries = 0, time = 0, tos = 0;
  const by = {}, heavies = { P: 0, PFull: 0, AI: 0, AIFull: 0 }, agg = { P: {}, AI: {} }, ev = {};
  for (let i = 0; i < n; i++) {
    const sim = new D._Sim(P, A, D.lookOf(P), D.lookOf(A), opts.sim);
    sim.f[0].ctrl = kind === 'ai' ? new D._AI(sim.f[0]) : BOTS[kind] ? BOTS[kind](0) : D.scriptBot(kind, 0);
    sim.f[1].ctrl = new D._AI(sim.f[1]);
    if (opts.setup) opts.setup(sim);
    sim.running = true;
    for (let k = 0; k < 60 * 75 && !sim.result; k++) {
      sim.events.length = 0; sim.step();
      for (const e of sim.events) {
        if (e.type === 'parry') { const key = (e.def === 0 ? 'P ' : 'AI ') + e.route + '/' + e.move; by[key] = (by[key] || 0) + 1; }
        else if (e.type === 'heavyGo') { const k2 = e.who === 0 ? 'P' : 'AI'; heavies[k2]++; if (e.charge >= 0.85) heavies[k2 + 'Full']++; }
        if (opts.onEvent) opts.onEvent(sim, e);
      }
      if (opts.onStep) opts.onStep(sim);
    }
    if (!sim.result) sim.timeUp();
    const o = sim.outcome();
    if (o.winner === 0) w++;
    if (o.kind === 'time') tos++;
    time += sim.time;
    parries += o.stats[0].parries; tries += o.stats[0].parryTries; aiParries += o.stats[1].parries;
    for (const [side, st] of [['P', o.stats[0]], ['AI', o.stats[1]]]) for (const k in st) if (typeof st[k] === 'number') agg[side][k] = (agg[side][k] || 0) + st[k];
  }
  return { pct: 100 * w / n, parries: parries / n, tries: tries / n, aiParries: aiParries / n, by, heavies, agg, n, time: time / n, tos: 100 * tos / n };
}
const rate = (kind, p, a, n) => play(kind, p, a, n).pct;

// ---------------------------------------------------------------- 手动场景 --
// 手动操作者：taps / held 由测试直接写
function manual() { return { held: HELD(), taps: TAPS(), update() {} }; }
// 场景：左 f[0]、右 f[1] 相距 dist，双方手动操作；ga / gb 可为姓名或武将对象
function stage(a, b, dist, opts) {
  const A = typeof a === 'string' ? gens[a] || worldGens[a] : a, B = typeof b === 'string' ? gens[b] || worldGens[b] : b;
  const sim = new D._Sim(A, B, D.lookOf(A), D.lookOf(B), opts);
  sim.f[0].x = -dist / 2; sim.f[1].x = dist / 2;
  sim.f[0].ctrl = manual(); sim.f[1].ctrl = manual();
  sim.running = true;
  sim.step(); sim.events.length = 0;
  return sim;
}
// 推进到 pred(sim, ev) 为真（ev 为本步的事件），最多 sec 秒；返回触发的事件或 true / null
function until(sim, pred, sec) {
  for (let i = 0; i < Math.round(sec * 60); i++) {
    sim.events.length = 0;
    sim.step();
    for (const e of sim.events) if (pred(sim, e)) return e;
    if (pred(sim, null)) return true;
  }
  return null;
}
const run = (sim, sec) => until(sim, () => false, sec);
// 连段驱动（脚本）：f 开打本人的招式谱，每一步在窗口一开就按下一步（= 踩准节拍：在接续点开始下一步）。
//   upto：打到第几步为止（1–8）；late：第 lateAt 步（2–8）晚按 late 秒（相对接续点）；hold：第 holdAt 步是重步时按住 L 迟滞 hold 秒；
//   onStep(sim, f, i)：每步开始时回调。返回 tick()，每步调用一次（在 sim.step 之前）
function driver(sim, f, opts) {
  opts = opts || {};
  const upto = opts.upto || 8;
  let started = false, pressedFor = -1, pressAt = -1, holdTill = -1, lateSeq = -1;
  return () => {
    const c = f.ctrl, now = sim.clock;
    if (holdTill >= 0) { c.held.heavy = now < holdTill; if (now >= holdTill) holdTill = -1; }
    if (!started) { started = true; c.taps[TK[f.seqStr[0]]]++; return; }
    if (f.state !== 'str' || !f.strOn) return;
    const i = f.strIdx, ni = i + 1;
    if (ni >= upto || ni > 7 || pressedFor === ni) return;
    // 本步的时钟（下一逻辑步推进后）
    const tNext = f.t + (1 / 60) * f.spd;
    if (pressAt < 0 || lateSeq !== f.seq) {
      lateSeq = f.seq;
      const late = opts.lateAt === ni + 1 ? opts.late || 0 : 0;
      pressAt = late > 0 ? f.cp + late * f.spd : f.winOpen + 1e-6;
    }
    if (tNext >= pressAt) {
      c.taps[TK[f.seqStr[ni]]]++; pressedFor = ni; pressAt = -1;
      // 迟滞：按住 L 直到「下一步开始（本步接续点）+ 起手到停住处 + hold」
      if (opts.hold && opts.holdAt === ni + 1 && f.seqStr[ni] === 'H') { c.held.heavy = true; holdTill = now + 1 / 60 + Math.max(0, f.cp - tNext) / f.spd + (ST[ni === 7 ? 'FH' : 'H'].startup - CO.WIN_EARLY) / f.spd + opts.hold; }
    }
  };
}
// 用给定的招式谱造一名武将（兜底不进表：直接改 seqStr / kStr）
function withSeq(sim, idx, seq) { const f = sim.f[idx]; f.seqStr = seq; f.kStr = SG.Combos.kStr(seq); return f; }
// 打一串，返回事件列表与 { hits, guarded, dmg }；dummy：对手的 held（每步写入）
function playString(sim, f, o, opts) {
  opts = opts || {};
  const tick = driver(sim, f, opts), out = { hits: 0, guarded: 0, dmg: 0, events: [], steps: [] };
  for (let k = 0; k < 60 * (opts.sec || 3); k++) {
    tick();
    if (opts.dummy) opts.dummy(sim, o, f);
    sim.events.length = 0; sim.step();
    for (const e of sim.events) {
      out.events.push(e);
      if (e.type === 'hit' && e.att === f.idx) { if (e.guarded) out.guarded++; else out.hits++; out.dmg += e.dmg; out.steps.push(e.step); }
    }
    if (k > 10 && f.state !== 'str' && f.state !== 'special' && !(o.state === 'hitstun' || o.state === 'guardstun')) break;
    if (opts.stop && opts.stop(sim, out)) break;
  }
  return out;
}
const war76 = g => Object.assign({}, g, { war: 76 });

section(1, 'botTally regression (same cases as tests/duel.html)', () => {
  const cases = [
    ['jumper', '孙乾', '吕布', 0, 10], ['jumper', '刘备', '吕布', 0, 40], ['jumper', '关羽', '关羽', 0, 75],
    ['masher', '孙乾', '吕布', 0, 10], ['masher', '刘备', '吕布', 0, 40], ['masher', '关羽', '关羽', 0, 75], ['masher', '张飞', '刘备', 40, 100],
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
section(2, 'masher vs equal-war AI', () => {
  const rows = MID.map(([p, a]) => [p, a, rate('masher', p, a, N)]);
  const av = rows.reduce((s, r) => s + r[2], 0) / rows.length, max = Math.max(...rows.map(r => r[2]));
  console.log('  war 72-76: ' + rows.map(r => `${r[0]}>${r[1]} ${r[2].toFixed(0)}`).join('  '));
  console.log(`  war 72-76 average ${av.toFixed(0)}%, worst pair ${max.toFixed(0)}%`);
  ok(av <= 30, `masher average vs war 72-76 AI is ${av.toFixed(0)}% (want <= 30%)`);
  ok(max <= 65, `masher worst pair vs war 72-76 AI is ${max.toFixed(0)}% (want <= 65%)`);
  ok(av >= 5, `war 72-76 AI should still be beatable by mashing sometimes (got ${av.toFixed(0)}%)`);
  const weak = [['孙乾', '孙乾'], ['简雍', '孙乾'], ['沮授', '陶谦'], ['陶谦', '沮授']].map(([p, a]) => rate('masher', p, a, N));
  const wAvg = avg(weak);
  console.log(`  war <= 40: ${weak.map(x => x.toFixed(0)).join(' ')}  average ${wAvg.toFixed(0)}%`);
  ok(wAvg >= 80, `weak generals (war <= 40) should stay easy to mash (got ${wAvg.toFixed(0)}%)`);
  // 强将：每组至少 75 场（合计 ≥ 300 场），「并非无法取胜」才不至于碰运气
  const nS = Math.max(N, 75);
  const strong = [['关羽', '关羽'], ['张飞', '赵云'], ['华雄', '张郃'], ['吕布', '吕布']].map(([p, a]) => rate('masher', p, a, nS));
  const sAvg = avg(strong);
  console.log(`  war >= 90 (${nS} each): ${strong.map(x => x.toFixed(1)).join(' ')}  average ${sAvg.toFixed(1)}%`);
  ok(sAvg <= 30 && sAvg >= 1, `strong generals (war >= 90) should be hard but not impossible to mash (got ${sAvg.toFixed(1)}%)`);
  ok(sAvg < av + 5, `strong generals should not be easier to mash than average ones (${sAvg.toFixed(0)}% vs ${av.toFixed(0)}%)`);
});

section(3, 'AI vs AI: war decides, mirror is even; strong AI parries, weak AI does not', () => {
  const n = N;
  const simR = (a, b) => { let w = 0; for (let i = 0; i < n; i++) if (D.simulate(gens[a], gens[b]).winner === 0) w++; return 100 * w / n; };
  const big = simR('吕布', '孙乾'), gap = simR('典韦', '李典'), mirror = simR('关羽', '关羽');
  console.log(`  吕布 vs 孙乾 ${big.toFixed(0)}%, 典韦 vs 李典 ${gap.toFixed(0)}%, 关羽 mirror ${mirror.toFixed(0)}%`);
  ok(big >= 95, '吕布 should crush 孙乾');
  ok(gap >= 75, '典韦(96) should usually beat 李典(78)');
  ok(mirror >= 25 && mirror <= 75, 'mirror match should be roughly even');
  const dur = (a, b, m) => { let t = 0, to = 0; for (let i = 0; i < m; i++) { const r = D.simulate(gens[a], gens[b]); t += r.time; if (r.kind === 'time') to++; } return { t: t / m, to: 100 * to / m }; };
  const dMid = dur('刘备', '刘备', Math.max(40, n)), dWeak = dur('孙乾', '孙乾', Math.max(80, n));
  console.log(`  AI mirror duration: 刘备 ${dMid.t.toFixed(1)}s (time-outs ${dMid.to.toFixed(0)}%), 孙乾 ${dWeak.t.toFixed(1)}s (time-outs ${dWeak.to.toFixed(0)}%)`);
  ok(dMid.t >= 20 && dMid.t <= 40 && dMid.to <= 10, `AI mid mirrors: 刘备 mirror ${dMid.t.toFixed(1)}s, ${dMid.to.toFixed(0)}% time-outs (want 20-40 s, <= 10%)`);
  ok(dWeak.to <= 45, `weak AI mirrors should mostly end before the clock (孙乾 mirror ${dWeak.to.toFixed(0)}% time-outs; want <= 45%)`);
  const strongAI = play('ai', '吕布', '吕布', Math.max(10, n >> 1)), weakAI = play('ai', '孙乾', '孙乾', Math.max(10, n >> 1));
  console.log(`  parries per match: 吕布 mirror ${strongAI.parries.toFixed(2)} + ${strongAI.aiParries.toFixed(2)}, 孙乾 mirror ${weakAI.parries.toFixed(2)} (tries ${weakAI.tries.toFixed(2)})`);
  ok(strongAI.parries + strongAI.aiParries >= 0.4, `strong AIs should parry each other sometimes (got ${(strongAI.parries + strongAI.aiParries).toFixed(2)} per match)`);
  ok(weakAI.tries === 0, `war 30 AI should not parry (tried ${weakAI.tries.toFixed(2)} per match)`);
});

// ---------------------------------------------------------------- (4) 格开规则 --
section(4, 'parry rules (scripted)', () => {
  console.log(`  window: war 30 ${D.parryWindow(30).toFixed(3)}s, war 65 ${D.parryWindow(65).toFixed(3)}s, war 100 ${D.parryWindow(100).toFixed(3)}s; cooldown ${PR.CD}s`);
  ok(near(D.parryWindow(30), 0.15, 1e-6) && near(D.parryWindow(100), 0.22, 1e-6) && D.parryWindow(65) > 0.15 && D.parryWindow(65) < 0.22, 'parry window should scale 0.15 → 0.22 s with war');
  const l1 = MV.L, cyc = (l1.startup + l1.active + l1.recovery) / 0.9;
  ok(PR.CD >= 2 && PR.CD > cyc * 4, `parry cooldown ${PR.CD}s should be clearly longer than a light attack cycle (${cyc.toFixed(2)}s)`);
  // A. 成功格开：右方轻击，左方在起手中按 U → 对手 parried ≈0.6 秒，己方 parryok + 0.7 秒反击窗口
  {
    const sim = stage('关羽', '关羽', 1.6), L = sim.f[0], R = sim.f[1];
    R.ctrl.taps.light++;
    run(sim, 0.03);
    L.ctrl.taps.parry++;
    const e = until(sim, (s, ev) => ev && ev.type === 'parry', 0.5);
    ok(!!e && e.def === 0 && e.kind === 'L', `a light attack landing inside the window should be parried (got ${e && e.move}/${e && e.kind})`);
    ok(R.state === 'parried' && near(R.stun, PR.STAGGER, 1e-6), `attacker should be staggered ${PR.STAGGER}s (state ${R.state}, stun ${R.stun})`);
    ok(L.state === 'parryok' && near(L.counterUntil - sim.clock, PR.COUNTER_WIN, 0.02), `parrier should get a ${PR.COUNTER_WIN}s counter window (state ${L.state})`);
    ok(R.hp === R.hpMax && L.hp === L.hpMax, 'a parry deals no damage');
    R.ctrl.held.guard = true;
    const tParry = sim.clock;
    until(sim, s => s.clock >= tParry + 0.52, 1);
    ok(R.state === 'parried', 'attacker is still staggered 0.52 s after the parry');
    L.ctrl.taps.light++;   // 关羽的招式谱以 M 起手：J 起手（代替第 1 步，伤害 × kStr）
    const h = until(sim, (s, ev) => ev && ev.type === 'hit' && ev.att === 0, 0.4);
    ok(!!h && h.riposte && !h.guarded, `the first hit inside the counter window should be an unblockable riposte (riposte ${h && h.riposte}, guarded ${h && h.guarded}, defender ${R.state})`);
    const base = MV.L.dmg * (D.WEAPONS.guandao.pow || 1) * PR.COUNTER_K * (D.jEntry(L.seqStr) >= 0 ? L.kStr : 1), exp = D.damage(base, 98, 98);
    ok(!!h && h.dmg >= Math.floor(exp * 0.92) && h.dmg <= Math.ceil(exp * 1.08), `riposte damage should be x${PR.COUNTER_K} (got ${h && h.dmg}, expected about ${exp.toFixed(1)})`);
    ok(!sim.countering(L), 'the counter window is used up by the first hit');
    const cdLeft = sim.parryCd(L);
    ok(cdLeft > 1.5 && cdLeft < PR.CD, `parry should be on cooldown right after use (${cdLeft.toFixed(2)}s left)`);
  }
  // B. 落空：没有来招 → 窗口过后 0.4 秒破绽；冷却 2.5 秒；挨打算「破绽」
  {
    const sim = stage('刘备', '刘备', 3.5), L = sim.f[0];
    L.ctrl.taps.parry++;
    run(sim, 0.02);
    ok(L.state === 'parry', `U should enter the parry stance (state ${L.state})`);
    until(sim, s => L.state !== 'parry', 0.5);
    ok(L.state === 'parrywhiff', `an empty parry should whiff (state ${L.state})`);
    const t0 = sim.clock;
    until(sim, s => L.state !== 'parrywhiff', 1);
    ok(near(sim.clock - t0, PR.WHIFF, 0.03), `whiff recovery should last ${PR.WHIFF}s (got ${(sim.clock - t0).toFixed(2)})`);
    run(sim, 0.4);
    L.ctrl.taps.parry++;
    run(sim, 0.05);
    ok(L.state !== 'parry', 'pressing U during the cooldown should do nothing');
    until(sim, s => s.parryReady(L), 3);
    L.ctrl.taps.parry++;
    run(sim, 0.03);
    ok(L.state === 'parry', `after the cooldown U should parry again (state ${L.state})`);
    const sim2 = stage('刘备', '刘备', 1.6), L2 = sim2.f[0], R2 = sim2.f[1];
    L2.ctrl.taps.parry++;
    until(sim2, s => L2.state === 'parrywhiff', 0.5);
    R2.ctrl.taps.light++;
    const h = until(sim2, (s, ev) => ev && ev.type === 'hit' && ev.att === 1, 0.4);
    ok(!!h && h.counter && !h.guarded, 'a hit during the whiff recovery should be a counter hit (破绽)');
  }
  // C. 受击硬直中的格开（第四版）：连段第 1–2 步不可看破；第 3 步起按来招类型的窗口（见 (11) 的完整矩阵）；只打两步则收势破绽 0.24 秒
  {
    const hs = (pressFor, upto) => {
      const sim = stage('刘备', '关羽', 1.5), L = sim.f[0], R = sim.f[1];
      const tick = driver(sim, R, { upto });
      let pressed = false, out = { parry: null, early: false, whiffT: 0 };
      for (let i = 0; i < 150; i++) {
        tick();
        if (!pressed && L.state === 'hitstun' && R.state === 'str' && R.strIdx === pressFor - 2 && R.hitIdx > 0) { L.ctrl.taps.parry++; pressed = true; }
        sim.events.length = 0; sim.step();
        for (const e of sim.events) { if (e.type === 'parry') out.parry = e; if (e.type === 'parryEarly') out.early = true; }
        if (L.state === 'parrywhiff') out.whiffT = L.whiffT;
        if (out.parry) break;
      }
      return out;
    };
    const s2 = hs(2, 8);
    ok(!s2.parry && s2.early, `a hitstun parry right after step 1 can never parry step 2 (parry ${!!s2.parry}, 过早 ${s2.early})`);
    const stop = hs(3, 2);
    ok(!stop.parry && near(stop.whiffT, CO.HS_WHIFF, 1e-6), `a hitstun parry that meets no further step leaves a ${CO.HS_WHIFF}s whiff (got ${stop.whiffT})`);
  }
  // D. 挡下一步后格开连段的下一步（举防 → 格开 → 反击）：全窗口
  {
    const sim = stage('关羽', '刘备', 1.5), L = sim.f[0], R = sim.f[1];
    L.ctrl.held.guard = true;
    run(sim, 0.05);
    const tick = driver(sim, R, { upto: 3 });
    let pressed = false, ev = null;
    for (let i = 0; i < 90 && !ev; i++) {
      tick();
      if (!pressed && L.state === 'guardstun') { L.ctrl.taps.parry++; pressed = true; }
      sim.events.length = 0; sim.step();
      ev = sim.events.find(e => e.type === 'parry') || null;
    }
    ok(!!ev && ev.move === 'str' && ev.step === 1 && ev.route === 'guard', `blocking string step 1 then pressing U should parry step 2 (got ${ev ? ev.move + ' step ' + ev.step + ' ' + ev.route : 'none'})`);
  }
  // E. 绝技不可格开；重击可格开
  {
    const sim = stage('关羽', '吕布', 1.6), L = sim.f[0], R = sim.f[1];
    R.rage = 100; R.ctrl.taps.special++;
    until(sim, () => sim.freeze[0] <= 0 && R.state === 'special', 1.5);
    L.ctrl.taps.parry++;
    const e = until(sim, (s, ev) => ev && (ev.type === 'hit' || ev.type === 'parry') && ev.att === 1, 1.2);
    ok(!!e && e.type === 'hit' && e.move === 'special', `specials cannot be parried (got ${e && e.type})`);
    const sim2 = stage('关羽', '吕布', 1.6), L2 = sim2.f[0], R2 = sim2.f[1];
    R2.ctrl.taps.heavy++;
    const go = until(sim2, (s, ev) => ev && ev.type === 'heavyGo', 0.6);
    ok(!!go, 'releasing a heavy emits the telegraph event');
    L2.ctrl.taps.parry++;
    const e2 = until(sim2, (s, ev) => ev && (ev.type === 'hit' || ev.type === 'parry'), 0.5);
    ok(!!e2 && e2.type === 'parry' && e2.move === 'heavy', `a heavy seen on its telegraph can be parried (got ${e2 && e2.type})`);
    ok(R2.state === 'parried' && R2.stun > PR.STAGGER, 'a parried heavy staggers a little longer');
  }
  // F. 电脑抓格开落空的破绽：武力高者多半抓到，武力低者少
  {
    const punish = (ai, me, n) => {
      let got = 0, whiffs = 0;
      for (let i = 0; i < n * 3 && whiffs < n; i++) {
        const sim = stage(me, ai, 1.75), L = sim.f[0], R = sim.f[1];
        R.ctrl = new D._AI(R);
        run(sim, 0.25 + 0.01 * (i % 7));
        if (!sim.actionable(L)) continue;
        L.ctrl.taps.parry++;
        if (!until(sim, () => L.state === 'parry', 0.1)) continue;
        if (until(sim, (s, ev) => ev ? ev.type === 'parry' || ev.type === 'hit' : L.state === 'parrywhiff', 0.5) !== true) continue;
        whiffs++;
        const h = until(sim, (s, ev) => ev ? ev.type === 'hit' && ev.att === 1 : L.state !== 'parrywhiff', 1);
        if (h && h.type === 'hit') got++;
      }
      return whiffs ? 100 * got / whiffs : 0;
    };
    const strong = punish('吕布', '刘备', 200), weak = punish('孙乾', '刘备', 200);
    console.log(`  AI punishes a whiffed parry: 吕布 ${strong.toFixed(0)}%, 孙乾 ${weak.toFixed(0)}%`);
    ok(strong >= 50, `strong AI should usually punish a whiffed parry (got ${strong.toFixed(0)}%)`);
    ok(strong > weak + 20, `strong AI should punish whiffed parries much more often than weak AI (${strong.toFixed(0)}% vs ${weak.toFixed(0)}%)`);
  }
});

// ---------------------------------------------------------------- (5) 胜率表 --
section(5, 'parry bot / parry-spam bot vs AI across war gaps', () => {
  const n = Math.max(10, N >> 1);
  const PAIRS = [['孙乾', '孙乾'], ['刘备', '刘备'], ['曹操', '曹操'], ['郭汜', '潘凤'], ['关羽', '关羽'], ['吕布', '吕布'], ['刘备', '吕布'], ['张飞', '刘备']];
  const KINDS = ['masher', 'spam', 'guard', 'parry'];
  const T = {};
  console.log('  player vs AI            ' + KINDS.map(k => k.padStart(7)).join('') + '   parry bot parries/tries, AI parries');
  for (const [p, a] of PAIRS) {
    const row = {};
    for (const k of KINDS) row[k] = play(k, p, a, n);
    T[p + '>' + a] = row;
    const pr = row.parry;
    console.log(`  ${(p + '(' + gens[p].war + ') vs ' + a + '(' + gens[a].war + ')').padEnd(22, '　')}` + KINDS.map(k => (row[k].pct.toFixed(0) + '%').padStart(7)).join('') +
      `   ${pr.parries.toFixed(1)}/${pr.tries.toFixed(1)}, ${pr.aiParries.toFixed(1)}`);
  }
  const av = (k, keys) => keys.reduce((s, x) => s + T[x][k].pct, 0) / keys.length;
  const mid = ['刘备>刘备', '曹操>曹操', '郭汜>潘凤'], strong = ['关羽>关羽', '吕布>吕布'];
  const spamMid = av('spam', mid), mashMid = av('masher', mid), spamStrong = av('spam', strong);
  const parryMid = av('parry', mid), guardMid = av('guard', mid), parryStrong = av('parry', strong), guardStrong = av('guard', strong);
  console.log(`  mid (72-76) avg: masher ${mashMid.toFixed(0)}%, spam ${spamMid.toFixed(0)}%, guard ${guardMid.toFixed(0)}%, parry ${parryMid.toFixed(0)}%;` +
    ` strong (98-100) avg: spam ${spamStrong.toFixed(0)}%, guard ${guardStrong.toFixed(0)}%, parry ${parryStrong.toFixed(0)}%`);
  ok(spamMid <= 45, `parry-spam should not beat equal mid AIs (got ${spamMid.toFixed(0)}%, want <= 45%)`);
  ok(spamMid <= mashMid + 15, `parry-spam should not be much better than plain mashing (${spamMid.toFixed(0)}% vs ${mashMid.toFixed(0)}%)`);
  ok(spamStrong <= 30, `parry-spam vs war 98-100 should be hard (got ${spamStrong.toFixed(0)}%)`);
  ok(T['孙乾>孙乾'].spam.pct >= 70, 'weak generals stay easy even for the parry-spammer');
  const parryUse = mid.concat(strong).reduce((s, x) => s + T[x].parry.parries, 0) / 5;
  ok(parryUse >= 0.5, `the parry bot should actually land parries (got ${parryUse.toFixed(2)} per match)`);
  ok(parryMid >= guardMid - 5 && parryStrong >= guardStrong - 5, `parrying should help a skilled player (mid ${parryMid.toFixed(0)}% vs ${guardMid.toFixed(0)}%, strong ${parryStrong.toFixed(0)}% vs ${guardStrong.toFixed(0)}%)`);
  ok(parryStrong <= 90, `even a skilled parry player should not always beat war 98-100 mirrors (got ${parryStrong.toFixed(0)}%)`);
  ok(T['刘备>吕布'].parry.pct <= 40, `刘备(76) should rarely beat 吕布(100) even with parries (got ${T['刘备>吕布'].parry.pct.toFixed(0)}%)`);
  ok(T['孙乾>孙乾'].parry.pct >= 80 && T['张飞>刘备'].parry.pct >= 80, 'a skilled player should beat weak / much weaker AIs');
  const by = {}, hv = { P: 0, PFull: 0, AI: 0, AIFull: 0 };
  for (const x of mid.concat(strong)) { const r = T[x].parry; for (const k in r.by) by[k] = (by[k] || 0) + r.by[k]; for (const k in hv) hv[k] += r.heavies[k]; }
  console.log('  parry bot, mid + strong mirrors, parries by route/move: ' + Object.keys(by).sort().map(k => `${k} ${by[k]}`).join(', '));
  console.log(`  heavies released: bot ${hv.P} (full ${hv.PFull}), AI ${hv.AI} (full ${hv.AIFull})`);
  const sum = re => Object.keys(by).filter(k => re.test(k)).reduce((t, k) => t + by[k], 0);
  ok(sum(/^P hitstun\/str$/) > 0, 'the human-latency bot should land some timed string-step parries from hitstun');
  const hb = {}, hh = { P: 0, PFull: 0, AI: 0, AIFull: 0 }, hw = [];
  for (const [p, a] of [['关羽', '关羽'], ['吕布', '吕布'], ['张飞', '张辽'], ['刘备', '刘备']]) {
    const r = play('heavy', p, a, n); hw.push(`${p}>${a} ${r.pct.toFixed(0)}%`);
    for (const k in r.by) hb[k] = (hb[k] || 0) + r.by[k]; for (const k in hh) hh[k] += r.heavies[k];
  }
  const aiHv = Object.keys(hb).filter(k => /^AI .*\/heavy$/.test(k)).reduce((t, k) => t + hb[k], 0);
  console.log(`  heavy bot: ${hw.join(', ')}; bot heavies ${hh.P} (full ${hh.PFull}), AI parried ${aiHv}; parries ${Object.keys(hb).sort().map(k => `${k} ${hb[k]}`).join(', ')}`);
  ok(hh.PFull > 0 && aiHv <= PR.HEAVY_CAP * hh.PFull + 0.05 * (hh.P - hh.PFull) + 2, `AI heavy parries vs a human-latency player should stay bounded (${aiHv} of ${hh.P} heavies, ${hh.PFull} full)`);
});

// ---------------------------------------------------------------- (6) 重击与反应 --
section(6, 'heavy / overhead release: reaction parries and guards are fair for both sides', () => {
  let sMin = 9, sMax = 0;
  for (const g of Object.values(gens)) { const sp = new D._Sim(g, g, D.lookOf(g), D.lookOf(g)).f[0].spd; sMin = Math.min(sMin, sp); sMax = Math.max(sMax, sp); }
  const rt = (c, sp) => D.heavySU(c, sp) / sp;
  console.log(`  heavy release → impact: tap ${rt(0.15, sMax).toFixed(3)}–${rt(0.15, sMin).toFixed(3)}s, full charge ${rt(1, sMax).toFixed(3)}–${rt(1, sMin).toFixed(3)}s; K+L 跃劈 startup ${D.OVH_SU.toFixed(2)}s real at any speed`);
  ok(rt(0.15, sMin) < 0.17, `a tap heavy should be too fast to react to (${rt(0.15, sMin).toFixed(3)}s)`);
  ok(rt(1, sMax) >= 0.24, `a full-charge heavy should be reactable by a human (${rt(1, sMax).toFixed(3)}s >= 0.24s)`);
  // 跃劈：武力 100 也 ≥ 0.30 秒（实测：K+L 到打中）
  {
    const sim = stage('吕布', '吕布', 1.7), L = sim.f[0], R = sim.f[1];
    R.ctrl.held.guard = true;
    L.ctrl.taps.ovh++;
    const t0 = sim.clock;
    const h = until(sim, (s, ev) => ev && ev.type === 'hit' && ev.att === 0, 1);
    const dt = sim.clock - t0;
    ok(!!h && dt >= 0.30, `K+L overhead startup at war 100 should be >= 0.30 s real (measured ${dt.toFixed(3)}s)`);
  }
  // 人与电脑同一按键同一时刻起招（复核：K / L 同按的 0.05 秒等待曾让人的中 / 重击晚 3 步）：玩家输入（klHold）按下 K / L 当步即起招；
  // 0.05 秒内另一键也到 → 跃劈，招式时钟接着第一招走；超过 0.05 秒 → 各算各的
  {
    const startSteps = (kl, keys, gap) => {
      const sim = stage('关羽', '刘备', 2), f = sim.f[0];
      f.ctrl.klHold = kl;
      f.ctrl.taps[keys[0]]++; f.ctrl.held[keys[0]] = true;
      const states = [];
      for (let i = 0; i < 8; i++) { if (keys[1] && i === gap) { f.ctrl.taps[keys[1]]++; f.ctrl.held[keys[1]] = true; } sim.step(); states.push(f.state); }
      return { states, ovhT: f.state === 'ovh' ? f.t : -1 };
    };
    const lat = ['medium', 'heavy'].map(k => [startSteps(false, [k]).states.join(), startSteps(true, [k]).states.join()]);
    const both = startSteps(true, ['medium', 'heavy'], 1), late = startSteps(true, ['heavy', 'medium'], 4);
    ok(lat.every(([a, b]) => a === b), `a human K / L press (klHold) starts on the same step as the same press without the K+L wait (${lat.map(x => x[1].split(',')[0]).join(', ')})`);
    ok(both.states[1] === 'ovh' && near(both.ovhT, 7 / 60 * stage('关羽', '刘备', 2).f[0].spd, 0.002), `K then L one step later turns into the overhead, timed from the K press (${both.states.slice(0, 3).join()}, t ${both.ovhT.toFixed(3)})`);
    ok(late.states.indexOf('ovh') < 0, `K pressed 0.067 s after L stays a separate press (${late.states.slice(0, 6).join()})`);
  }
  const vsAI = (p, ai, chg, n) => {
    const out = { parried: 0, hit: 0, guarded: 0, gbreak: 0, avoid: 0 }, lat = [];
    let looked = 0, lookedParried = 0, unseenOk = 0;
    const react = new D._AI(new D._Sim(gens[ai], gens[ai], D.lookOf(gens[ai]), D.lookOf(gens[ai])).f[0]).react;
    for (let i = 0; i < n * 12 && looked < n; i++) {
      const sim = stage(p, ai, 2.05 + (i % 4) * 0.05), L = sim.f[0], R = sim.f[1];
      const A = new D._AI(R); R.ctrl = A;
      run(sim, 0.3 + (i % 9) * 0.04);
      if (!sim.actionable(L)) continue;
      L.ctrl.taps.heavy++; L.ctrl.held.heavy = true;
      let rel = -1, res = null, press = -1, hSeq = -1, saw = false, viaSeen = false;
      for (let k = 0; k < 150 && !res; k++) {
        if (L.state === 'charge' && L.charge >= chg) L.ctrl.held.heavy = false;
        const hv0 = A.hvSeq, d0 = Math.abs(L.x - R.x);
        sim.events.length = 0; sim.step();
        if (L.state === 'heavy' && hSeq < 0) hSeq = L.seq;
        if (hSeq >= 0 && hv0 !== hSeq && A.hvSeq === hSeq && L.state === 'heavy' && L.phase === 'startup')
          saw = d0 <= L.reach * MV.heavy.reachK + 0.2 + 0.6;
        for (const e of sim.events) {
          if (e.type === 'heavyGo' && e.who === 0) rel = sim.clock;
          if (e.type === 'parryStart' && e.who === 1 && rel >= 0 && press < 0) press = sim.clock;
          if (e.type === 'parry' && e.def === 1) { res = 'parried'; viaSeen = hSeq >= 0 && A.hvSeq === hSeq; }   // 经「看清这一记重击」那条路按下的
          if (e.type === 'hit' && e.att === 0) res = e.breaks ? 'gbreak' : e.guarded ? 'guarded' : 'hit';
          if (e.type === 'hit' && e.att === 1) res = 'skip';
        }
        if (rel >= 0 && !res && L.state !== 'heavy' && L.state !== 'charge') res = 'avoid';
      }
      if (!res || res === 'skip') continue;
      out[res]++;
      if (res === 'parried') lat.push(press - rel);
      if (saw) { looked++; if (res === 'parried') lookedParried++; }
      else if (res === 'parried' && viaSeen) unseenOk++;     // 看清了（反应时间之后）但起手时稍远于统计口径的距离：不算「没看见就格开」
    }
    const reached = out.parried + out.hit + out.guarded + out.gbreak;
    return { out, n: looked, pct: looked ? 100 * lookedParried / looked : 0, reachedPct: reached ? 100 * out.parried / reached : 0,
      other: out.parried - lookedParried - unseenOk, minLat: lat.length ? Math.min(...lat) : null, react };
  };
  for (const [p, ai] of [['关羽', '吕布'], ['关羽', '关羽'], ['张飞', '张辽'], ['刘备', '曹操']]) {
    const t = vsAI(p, ai, 0.15, 40), f = vsAI(p, ai, 0.95, QUICK ? 120 : 200);
    console.log(`  ${p} heavy vs AI ${ai}(${gens[ai].war}): tap parried ${t.pct.toFixed(0)}% of ${t.n} seen; full parried ${f.pct.toFixed(0)}% of ${f.n} seen` +
      ` (${f.reachedPct.toFixed(0)}% of those that reached it; ${f.other} parried unseen) ${JSON.stringify(f.out)}` +
      (f.minLat !== null ? `; AI press ≥ ${(f.minLat * 1000).toFixed(0)}ms after release (react ${(f.react * 1000).toFixed(0)}ms)` : ''));
    ok(t.pct <= 5 && t.out.parried <= 2, `AI ${ai} should not parry tap heavies on reaction (${t.pct.toFixed(0)}%, ${t.out.parried} parried)`);
    ok(f.pct <= 100 * PR.HEAVY_CAP + 12, `AI ${ai} should parry at most about half of the full-charge heavies it sees (${f.pct.toFixed(0)}%)`);
    ok(f.other === 0, `AI ${ai} should parry full heavies only on reaction (${f.other} parried without seeing the release)`);
    ok(f.minLat === null || f.minLat >= f.react - 1e-6, `AI ${ai} must not react to a heavy faster than to other attacks (${f.minLat !== null ? (f.minLat * 1000).toFixed(0) : '-'}ms < ${(f.react * 1000).toFixed(0)}ms)`);
  }
  const human = (def, att, chg, n) => {
    let par = 0, tot = 0;
    for (let i = 0; i < n; i++) {
      const sim = stage(def, att, 2.1), L = sim.f[0], R = sim.f[1];
      R.ctrl.taps.heavy++; R.ctrl.held.heavy = true;
      const rt2 = 0.22 + 0.04 * ((SG.Random.value() + SG.Random.value() + SG.Random.value() + SG.Random.value() - 2) * 1.732);
      let rel = -1, res = null, pressed = false;
      for (let k = 0; k < 150 && !res; k++) {
        if (R.state === 'charge' && R.charge >= chg) R.ctrl.held.heavy = false;
        if (rel >= 0 && sim.clock >= rel + rt2 && !pressed) { L.ctrl.taps.parry++; pressed = true; }
        sim.events.length = 0; sim.step();
        for (const e of sim.events) {
          if (e.type === 'heavyGo' && e.who === 1) rel = sim.clock;
          if (e.type === 'parry' && e.def === 0) res = 'parried';
          if (e.type === 'hit' && e.att === 1) res = 'hit';
        }
      }
      if (res) { tot++; if (res === 'parried') par++; }
    }
    return tot ? 100 * par / tot : 0;
  };
  const hFull = human('刘备', '关羽', 0.95, 60), hTap = human('刘备', '关羽', 0.15, 60);
  console.log(`  human reaction (0.22 ± 0.04 s) parrying 关羽's heavies as 刘备: full charge ${hFull.toFixed(0)}%, tap ${hTap.toFixed(0)}%`);
  ok(hFull >= 50, `a human-latency player should parry most full-charge heavies on reaction (${hFull.toFixed(0)}%)`);
  ok(hTap <= 10, `nobody should parry tap heavies on reaction (${hTap.toFixed(0)}%)`);
});

// ---------------------------------------------------------------- (7) 防守试探型打法 --
section(7, 'guard-and-poke / cautious / turtle bots vs AI; war-gap ladder', () => {
  const n = Math.max(20, N >> 1);
  const MID8 = [['刘备', '刘备'], ['曹操', '曹操'], ['郭汜', '潘凤'], ['王平', '王平'], ['李傕', '刘备'], ['张济', '曹操'], ['祖茂', '黄祖'], ['孟达', '潘凤']];
  const res = {};
  for (const k of ['guard', 'parry', 'cautious', 'cautiousN', 'turtle', 'turtleP', 'backstepper']) {
    const rs = MID8.map(([p, a]) => play(k, p, a, n));
    const a = avg(rs.map(r => r.pct));
    const full = rs.reduce((t, r) => t + r.heavies.AIFull, 0) / (rs.length * n), aiP = rs.reduce((t, r) => t + r.aiParries, 0) / rs.length;
    res[k] = { avg: a, full, aiP };
    console.log(`  ${k.padEnd(8)} vs equal mid AI: ${a.toFixed(0)}%  [${rs.map(r => r.pct.toFixed(0)).join(' ')}]  AI full-charge heavies ${full.toFixed(2)}/match, AI parries ${aiP.toFixed(2)}/match`);
  }
  ok(res.guard.avg <= 50, `guard-and-poke bot should not beat equal mid AIs (got ${res.guard.avg.toFixed(0)}%, want <= 50%)`);
  ok(res.parry.avg >= 25 && res.parry.avg <= 70, `a skilled parry player should keep a fair chance against equal mid AIs (got ${res.parry.avg.toFixed(0)}%, want 25-70%)`);
  ok(res.cautious.avg >= 25 && res.cautious.avg <= 65, `a cautious player should keep a fair chance (got ${res.cautious.avg.toFixed(0)}%)`);
  ok(res.turtle.avg <= 70, `pure turtling should not beat equal mid AIs (got ${res.turtle.avg.toFixed(0)}%, want <= 70%)`);
  ok(res.turtle.full >= 1, `the AI should break a turtle with full-charge heavies from out of his reach (got ${res.turtle.full.toFixed(2)} per match)`);
  ok(res.turtleP.avg <= 70, `turtling and parrying every charged heavy should not beat equal mid AIs (got ${res.turtleP.avg.toFixed(0)}%, want <= 70%)`);
  ok(res.backstepper.avg <= 55, `poke-and-backstep should not beat equal mid AIs (got ${res.backstepper.avg.toFixed(0)}%, want <= 55%)`);
  const L = ['简雍', '刘表', '徐庶', '樊稠', '董卓', '吕布'];
  for (const k of ['guard', 'parry']) {
    const row = L.map(a => (play(k, '刘备', a, n).pct + play(k, '曹操', a, n).pct) / 2);
    console.log(`  ladder ${k.padEnd(6)} vs AI war ${L.map(a => gens[a].war).join('/')}: ${row.map(x => x.toFixed(0)).join(' ')}`);
    ok(row.every((x, i) => i === 0 || x <= row[i - 1] + 12), `${k} win rate should fall as the AI's war rises (${row.map(x => x.toFixed(0)).join(' ')})`);
    ok(row[0] >= 85 && row[L.length - 1] <= 15, `${k}: war 30 AI should be easy and war 100 AI hard for a war 72-76 player (${row[0].toFixed(0)}% / ${row[L.length - 1].toFixed(0)}%)`);
  }
  for (const [ps, below, near6] of [[['刘表', '张鲁'], ['陆康'], ['糜芳']], [['关羽', '马超'], ['董卓', '华雄'], ['张辽', '关羽']]]) {
    const a2 = (as, nn) => as.reduce((t, a) => t + ps.reduce((u, p) => u + play('parry', p, a, nn || n).pct, 0) / ps.length, 0) / as.length;
    // 关羽 / 马超 对低 6–12 武力的真值约 68–72%（n 800），n 20 × 4 时 σ ≈ 5，离门槛 65% 太近：「低于」这一项取 n ≥ 60
    const lo = a2(below, Math.max(60, n)), hi = a2(near6);
    console.log(`  parry bot as ${ps.join('/')}(${gens[ps[0]].war}): vs AI ${below.map(a => a + gens[a].war).join('/')} ${lo.toFixed(0)}%, vs AI ${near6.map(a => a + gens[a].war).join('/')} ${hi.toFixed(0)}%`);
    ok(lo >= 65, `a skilled player should usually beat an AI 6-12 war below (${ps[0]}: ${lo.toFixed(0)}%)`);
    ok(hi >= 25, `a skilled player should keep a fair chance within +6 war (${ps[0]}: ${hi.toFixed(0)}%)`);
  }
});

// ---------------------------------------------------------------- (8) 后退格挡 --
section(8, 'hold-back guard (proximity guard, hit-time blocking, cross-up, air)', () => {
  const n = Math.max(60, Math.round(N * 1.5));      // 两个机器人胜率之差 ≤ 6 个百分点：每组 20 场时差值的噪声就有 ±5 个百分点，故加大场数
  const MID8 = [['刘备', '刘备'], ['曹操', '曹操'], ['郭汜', '潘凤'], ['王平', '王平'], ['李傕', '刘备'], ['张济', '曹操'], ['祖茂', '黄祖'], ['孟达', '潘凤']];
  const STR = [['关羽', '关羽'], ['吕布', '吕布']];
  const g = MID8.map(([p, a]) => play('guard', p, a, n).pct), hb = MID8.map(([p, a]) => play('hbguard', p, a, n).pct);
  const nS = n * 3;     // 强将同将只有两组：场数再 ×3，差值的噪声才在 ±3 个百分点以内
  const gS = STR.map(([p, a]) => play('guard', p, a, nS).pct), hbS = STR.map(([p, a]) => play('hbguard', p, a, nS).pct);
  console.log(`  equal mid AI: guard (防键) ${avg(g).toFixed(0)}% vs hbguard (后退) ${avg(hb).toFixed(0)}%; strong mirrors ${avg(gS).toFixed(0)}% vs ${avg(hbS).toFixed(0)}%`);
  ok(Math.abs(avg(g) - avg(hb)) <= 6, `hold-back guard should play like stationary guard on the MID set (|Δ| ${Math.abs(avg(g) - avg(hb)).toFixed(1)} pp)`);
  ok(Math.abs(avg(gS) - avg(hbS)) <= 6, `... and against strong mirrors (|Δ| ${Math.abs(avg(gS) - avg(hbS)).toFixed(1)} pp)`);
  // 近身判定：有威胁时后退不动（Δx = 0）；没有威胁时照常后退
  {
    const sim = stage('刘备', '关羽', 3.0), L = sim.f[0], R = sim.f[1];
    L.ctrl.held.x = -1; run(sim, 0.3);
    const walked = L.x < -1.5 - 0.3;
    ok(walked && L.state === 'walk', `holding back with no threat walks backward (x ${L.x.toFixed(2)}, state ${L.state})`);
    const sim2 = stage('刘备', '关羽', 2.0), L2 = sim2.f[0], R2 = sim2.f[1];
    L2.ctrl.held.x = -1; R2.ctrl.taps.heavy++; R2.ctrl.held.heavy = true;
    let x0 = null, moved = 0, guardSeen = false;
    for (let i = 0; i < 60; i++) {
      sim2.events.length = 0; sim2.step();
      if (sim2.events.some(e => e.type === 'hit')) break;
      if (sim2.threat(L2, R2)) { if (x0 === null) x0 = L2.x; moved = Math.max(moved, Math.abs(L2.x - x0)); guardSeen = guardSeen || L2.state === 'guard'; }
    }
    ok(guardSeen && moved < 1e-9, `while threatened (foe charging in range), holding back raises the guard and never moves (Δx ${moved.toFixed(4)}, guard ${guardSeen})`);
  }
  // 命中那一步才按后退：照样挡住
  {
    const findStep = () => { const sim = stage('刘备', '关羽', 1.6); sim.f[1].ctrl.taps.light++; for (let i = 0; i < 60; i++) { sim.events.length = 0; sim.step(); if (sim.events.some(e => e.type === 'hit')) return i; } return -1; };
    const K = findStep();
    const sim = stage('刘备', '关羽', 1.6), L = sim.f[0], R = sim.f[1];
    R.ctrl.taps.light++;
    let hit = null;
    for (let i = 0; i <= K; i++) { if (i === K) L.ctrl.held.x = -1; sim.events.length = 0; sim.step(); hit = sim.events.find(e => e.type === 'hit') || hit; }
    ok(K > 0 && !!hit && hit.guarded, `a back press on the very step the hit lands blocks it (step ${K}, guarded ${hit && hit.guarded})`);
  }
  // 越身：0.10 秒内两个方向都算后退；之后只认正确方向（直接查 guardOf，命中判定用的同一函数）
  {
    const sim = stage('刘备', '关羽', 1.4), L = sim.f[0], R = sim.f[1];
    const h = { mv: MV.L, name: 'str' };
    const test = (since, x) => { L.xAt = sim.clock - since; L.ctrl.held.x = x; L.h = L.ctrl.held; return sim.guardOf(L, R, h); };
    // R 在 L 右侧：背向 = −1。越身 0.05 秒前：+1 / −1 都挡；0.15 秒前：只有 −1
    ok(test(0.05, 1) && test(0.05, -1), 'within 0.10 s after a cross-up, either direction blocks');
    ok(!test(0.15, 1) && test(0.15, -1), 'after 0.10 s only the direction away from the attacker blocks');
  }
  // 空中不能挡
  {
    const sim = stage('刘备', '关羽', 1.4), L = sim.f[0], R = sim.f[1];
    L.ctrl.taps.up++; run(sim, 0.12);
    L.ctrl.held.guard = true; L.ctrl.held.x = -1;
    ok(!L.grounded && !sim.guardOf(L, R, { mv: MV.cH, name: 'cH' }), 'an airborne fighter can never guard');
  }
});

// ---------------------------------------------------------------- (9) 下段与上段 --
section(9, 'lows and overheads', () => {
  // 规则矩阵：下段只能蹲挡、上段只能站挡、中段都能挡
  {
    const sim = stage('刘备', '关羽', 1.4), L = sim.f[0], R = sim.f[1];
    L.ctrl.held.guard = true; run(sim, 0.03);
    const res = {};
    for (const low of [false, true]) {
      L.ctrl.held.down = low; L.h = L.ctrl.held;
      for (const mv of ['L', 'cL', 'cM', 'ovh', 'air']) res[mv + (low ? '/c' : '/s')] = sim.guardOf(L, R, { mv: MV[mv], name: mv });
    }
    ok(res['L/s'] && res['L/c'], 'mids are blocked standing and crouching');
    ok(!res['cL/s'] && res['cL/c'] && !res['cM/s'] && res['cM/c'], 'lows are blocked only crouching');
    ok(res['ovh/s'] && !res['ovh/c'] && res['air/s'] && !res['air/c'], 'overheads (K+L, air attacks) are blocked only standing');
    // 削血 ×0.2（不按连击递减）
    const s2 = stage('刘备', '刘备', 1.5), A = s2.f[0], B = s2.f[1];
    B.ctrl.held.guard = true; run(s2, 0.05);
    A.ctrl.taps.medium++;
    const h = until(s2, (s, ev) => ev && ev.type === 'hit', 0.5);
    const raw = D.damage(MV.M.dmg * A.pow * (A.seqStr[0] === 'M' ? A.kStr : 1), A.war, B.war);
    ok(!!h && h.guarded && h.dmg >= Math.floor(raw * 0.2 * 0.92) && h.dmg <= Math.ceil(raw * 0.2 * 1.08), `chip is ×0.2 of the unscaled damage (got ${h && h.dmg}, raw ${raw.toFixed(1)})`);
  }
  // 人的反应：蹲挡中看见跃劈起手（0.22 ± 0.04 秒后）站起 → 至少 80% 挡住
  {
    let blocked = 0, total = 0;
    for (let i = 0; i < 100; i++) {
      const sim = stage('刘备', '关羽', 1.7), L = sim.f[0], R = sim.f[1];
      L.ctrl.held.x = -1; L.ctrl.held.down = true;
      run(sim, 0.05 + 0.01 * (i % 5));
      R.ctrl.taps.ovh++;
      const rt = 0.22 + 0.04 * gz();
      let t0 = -1, res = null;
      for (let k = 0; k < 60 && !res; k++) {
        if (t0 >= 0 && sim.clock >= t0 + rt) L.ctrl.held.down = false;
        sim.events.length = 0; sim.step();
        for (const e of sim.events) { if (e.type === 'ovhGo' && e.who === 1) t0 = sim.clock; if (e.type === 'hit' && e.att === 1) res = e; }
      }
      if (res) { total++; if (res.guarded) blocked++; }
    }
    console.log(`  human reaction (0.22 ± 0.04 s) stand-blocks ${blocked}/${total} K+L overheads seen from crouch`);
    ok(total >= 80 && blocked >= 0.8 * total, `a human-latency player should stand-block >= 80% of overheads seen from crouch (${blocked}/${total})`);
  }
  // 电脑看跃劈：蹲挡（惯于防下段）中看见跃劈 → 站挡；武力 ≥ 70 ≥ 70%，≤ 35 ≤ 40%
  const aiOvh = (ai, n) => {
    let blocked = 0, total = 0;
    for (let i = 0; i < n * 3 && total < n; i++) {
      const sim = stage('刘备', ai, 1.75), L = sim.f[0], R = sim.f[1];
      const A = new D._AI(R); R.ctrl = A; A.lowP = 1; A.gLow = true; A.gHabitAt = 1e9;   // 布置：认定对手惯出下段、一直蹲挡（不再按习惯重掷）
      run(sim, 0.4 + 0.02 * (i % 9));
      if (!sim.actionable(L) || !A.gLow) continue;      // 只量「跃劈起手时正蹲着挡」的局面
      L.ctrl.taps.ovh++;
      let res = null;
      for (let k = 0; k < 60 && !res; k++) { sim.events.length = 0; sim.step(); for (const e of sim.events) if (e.type === 'hit' && e.att === 0) res = e; else if (e.type === 'hit' || e.type === 'parry') res = 'skip'; }
      if (!res || res === 'skip') continue;
      total++; if (res.guarded) blocked++;
    }
    return total ? 100 * blocked / total : 0;
  };
  const sOvh = ['关羽', '张辽', '夏侯惇'].map(a => aiOvh(a, 80)), wOvh = ['孙乾', '简雍'].map(a => aiOvh(a, 80));
  console.log(`  AI stand-blocks K+L overheads (crouch-guarding beforehand): war >= 70 ${sOvh.map(x => x.toFixed(0)).join('/')}%, war <= 35 ${wOvh.map(x => x.toFixed(0)).join('/')}%`);
  ok(avg(sOvh) >= 70, `AI war >= 70 should stand-block >= 70% of overheads (got ${avg(sOvh).toFixed(0)}%)`);
  ok(avg(wOvh) <= 40, `AI war <= 35 should stand-block <= 40% of overheads (got ${avg(wOvh).toFixed(0)}%)`);
  // mixup 机器人 vs 电脑：电脑挡下段 35–75%（≤ 80%：0.10 秒的下段看不及）；换挡不早于反应时间（诚实）
  {
    const MIDm = [['刘备', '刘备'], ['曹操', '曹操'], ['郭汜', '潘凤'], ['王平', '王平']];
    let lows = 0, lowsBlocked = 0, early = 0, wins = [];
    for (const [p, a] of MIDm) {
      const r = play('mixup', p, a, Math.max(40, N), {      // 下段次数不多（每场约 1–2 次）：场数加倍
        onEvent(sim, e) {
          if (e.type === 'hit' && e.att === 0 && e.line === 'low') { lows++; if (e.guarded) lowsBlocked++; }
        },
        onStep(sim) {
          const A = sim.f[1].ctrl;
          // 诚实：电脑因看见来招的高度而换挡，所据的快照至少是 react 秒以前的，且换挡再晚 hand 秒才生效
          if (A.gLowTo !== null && A._lowChk !== A.gLowAt) {
            A._lowChk = A.gLowAt;
            if (A.gLowAt - A.hand - A.gLowSrc < A.react - 1e-6) early++;
          }
        },
      });
      wins.push(r.pct);
    }
    const pctLow = lows ? 100 * lowsBlocked / lows : 0;
    console.log(`  mixup bot vs equal mid AI: ${avg(wins).toFixed(0)}% [${wins.map(x => x.toFixed(0)).join(' ')}]; AI blocked ${lowsBlocked}/${lows} lows (${pctLow.toFixed(0)}%); height switches before react: ${early}`);
    ok(pctLow >= 35 && pctLow <= 75, `AI should block 35-75% of the mixup bot's lows (got ${pctLow.toFixed(0)}%)`);
    ok(early === 0, `the AI must never switch guard height sooner than its reaction time (${early} early)`);
    ok(avg(wins) <= 60, `mixup bot should not beat equal mid AIs (got ${avg(wins).toFixed(0)}%, want <= 60%)`);
  }
});

// ---------------------------------------------------------------- (10) 连段规则 --
section(10, 'strings: rules (scripted, deterministic)', () => {
  const C = SG.Combos;
  // a. 全体 499 人：同武力 76 对站着不动的木人打满一整串（踩准节拍），20 次平均 → 1 / 2 / 3 个重步 28.0 / 29.1 / 30.2 ± 1.5（按兵器威力折算）
  {
    const bad = [], byH = { 1: [], 2: [], 3: [] };
    let incomplete = 0;
    const names = Object.keys(worldGens);
    D.critEnabled = false;      // 归一化的是基础伤害（会心另算，见 (13)）
    for (const name of names) {
      const g = war76(worldGens[name]);
      let tot = 0, runs = QUICK ? 4 : 20;
      for (let r = 0; r < runs; r++) {
        const sim = stage(g, war76(gens['刘备']), 1.5);
        const f = sim.f[0];
        const out = playString(sim, f, sim.f[1]);
        if (out.hits !== 8) { incomplete++; break; }
        tot += out.dmg;
      }
      const e = C.of(name), nH = e.nH;
      const per = tot / runs / (sim0Pow(g) || 1);
      byH[nH].push(per);
      const want = 28 * (1 + 0.04 * (nH - 1));
      if (Math.abs(per - want) > 1.5) bad.push(`${name} ${e.seq} ${per.toFixed(1)}/${want.toFixed(1)}`);
    }
    D.critEnabled = true;
    console.log(`  full on-beat strings at equal war 76 (÷ weapon pow): 1 H ${avg(byH[1]).toFixed(1)} (n${byH[1].length}), 2 H ${avg(byH[2]).toFixed(1)} (n${byH[2].length}), 3 H ${avg(byH[3]).toFixed(1)} (n${byH[3].length}); incomplete ${incomplete}`);
    ok(incomplete === 0, `every general's on-beat string lands 8/8 on a standing dummy (${incomplete} did not)`);
    ok(bad.length === 0, `every string deals 28.0 / 29.1 / 30.2 ± 1.5 HP (${bad.length} outside: ${bad.slice(0, 4).join(', ')})`);
  }
  // b. 真连段：30 串 × 攻方武力 {30, 100} × 守方武力 {30, 100}：第一击打中后 8/8 全中，即使守方按住后退并狂按 J
  {
    const all = C.all(), pick = [];
    for (let i = 0; i < 30; i++) pick.push(all[(i * 97) % all.length]);
    let failed = [];
    for (const e of pick) for (const wa of [30, 100]) for (const wd of [30, 100]) {
      const sim = stage(Object.assign({}, worldGens[e.name], { war: wa }), Object.assign({}, gens['刘备'], { war: wd }), 1.4);
      const o = sim.f[1], bj = D.scriptBot('dummyBackJ', 1);
      // 守方一直按住后退；第一击打中后才开始狂按 J（第一击之前按 J 会抢先打断起手，那不是本项要量的）
      o.ctrl = { held: bj.held, taps: bj.taps, update: (dt, s) => { if (s.f[0].stats.unguarded > 0) bj.update(dt, s); else { bj.held.x = -o.face; } } };
      const out = playString(sim, sim.f[0], o);
      const first = out.events.find(x => x.type === 'hit');
      if (first && first.att === 0 && !first.guarded && out.hits !== 8) failed.push(`${e.name} ${e.seq} ${wa}v${wd}: ${out.hits}`);
    }
    ok(failed.length === 0, `on-beat strings are true combos against back + J mashing (${failed.length} broke: ${failed.slice(0, 4).join(', ')})`);
  }
  // c. 迟按 +0.06 秒仍连上；+0.12 秒放人（按住后退的木人挡下下一步）；重步迟滞 0.10 秒被按住后退的木人挡下
  {
    const seq = 'LMLHMLMH';
    const lateRun = (late, lateAt, hold) => {
      const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: gens['刘备'].war }), 1.4), f = withSeq(sim, 0, seq), o = sim.f[1];   // 同武力：不会硬挡（硬挡是另一条随机规则）
      o.ctrl.update = (dt, s) => { o.ctrl.held.x = f.stats.unguarded > 0 ? -o.face : 0; };     // 挨了第一击后一直按住后退
      const out = playString(sim, f, o, { late, lateAt, hold, holdAt: lateAt });
      const ev = out.events.filter(x => x.type === 'hit' && x.att === 0);
      return ev.find(x => x.step === lateAt) || null;
    };
    const ok06 = lateRun(0.06, 3), ok12 = lateRun(0.12, 3), hold10 = lateRun(0, 4, 0.10), hold05 = lateRun(0, 4, 0.05);
    ok(!!ok06 && !ok06.guarded, `a step pressed 0.06 s late still combos (step 3 ${ok06 ? (ok06.guarded ? 'blocked' : 'hit') : 'none'})`);
    ok(!!ok12 && ok12.guarded, `a step pressed 0.12 s late releases the defender, who blocks it by holding back (step 3 ${ok12 ? (ok12.guarded ? 'blocked' : 'hit') : 'none'})`);
    ok(!!hold10 && hold10.guarded, `an H step delayed 0.10 s is blocked by a defender holding back (step 4 ${hold10 ? (hold10.guarded ? 'blocked' : 'hit') : 'none'})`);
    ok(!!hold05 && !hold05.guarded, `an H step delayed 0.05 s (≤ 0.08) still combos (step 4 ${hold05 ? (hold05.guarded ? 'blocked' : 'hit') : 'none'})`);
  }
  // d. 防连按：randmash 起的连段 ≤ 1% 打完；只按 J 的连按至多打到招式谱开头那几个 L（≤ 2）。
  //    J 起手（jEntry）：第 1 步是中击的，J 代替第 1 步或从第 2 步起——算从起手那一步起连上的步数（起手那一步当作 L）
  {
    let started = 0, completed = 0;
    for (let i = 0; i < 80 && started < 400; i++) {
      const r = play('randmash', '刘备', '刘备', 1, { setup: sim => { sim.f[1].ctrl = D.scriptBot('dummy', 1); } });
      started += r.agg.P.strings || 0; completed += r.agg.P.fullStrings || 0;
    }
    console.log(`  randmash: ${completed}/${started} strings completed`);
    ok(started >= 400 && completed <= 0.01 * started, `button mashing completes <= 1% of the strings it starts (${completed}/${started})`);
    let maxRun = 0, badJ = [];
    for (const name of ['张飞', '赵云', '甘宁', '刘备', '曹操', '夏侯渊']) {
      const sim = stage(name, '刘备', 1.4), f = sim.f[0], o = sim.f[1];
      o.ctrl = D.scriptBot('dummy', 1);
      const ent = D.jEntry(f.seqStr), lead = ent < 0 ? 0 : 1 + (f.seqStr.slice(ent + 1).match(/^L*/) || [''])[0].length;
      let maxStep = 0, from = 0;
      for (let k = 0; k < 60 * 4; k++) { if (k % 6 === 0) f.ctrl.taps.light++; sim.events.length = 0; sim.step(); for (const e of sim.events) if (e.type === 'strStep' && e.who === 0) { if (e.entry) from = e.step; maxStep = Math.max(maxStep, e.step + 1 - from); } }
      maxRun = Math.max(maxRun, maxStep);
      if (maxStep > Math.max(lead, 0) || maxStep > 2) badJ.push(`${name} ${f.seqStr} ${maxStep}`);
    }
    ok(badJ.length === 0 && maxRun <= 2, `J-only mashing never exceeds the string's leading run of L, at most 2 (${badJ.join(', ')})`);
  }
  // e. 按错键：本串到此为止；那一键只在收招结束前 BUFFER（0.16 秒）以内按下时才变成单出招（按得早就作废）
  {
    const wrong = atFrac => {
      const sim = stage('刘备', '关羽', 1.4), f = withSeq(sim, 0, 'LMLHMLMH'); sim.f[1].ctrl = D.scriptBot('dummy', 1);
      f.ctrl.taps.light++;
      until(sim, () => f.state === 'str' && f.strIdx === 0 && f.t >= f.winOpen + (f.winClose - f.winOpen) * atFrac, 0.5);
      const left = (f.mv.startup + f.mv.active + f.mv.recovery - f.t) / f.spd;
      f.ctrl.taps.heavy++;     // 应为 M
      let miss = false, next = false, fired = false;
      for (let k = 0; k < 50; k++) { sim.events.length = 0; sim.step(); for (const e of sim.events) { if (e.type === 'strMiss') miss = true; if (e.type === 'strStep' && e.who === 0) next = true; } if (f.state === 'charge' || f.state === 'heavy') { fired = true; break; } }
      return { miss, next, fired, left };
    };
    const late = wrong(0.8), early = wrong(0.0);
    console.log(`  wrong press ${late.left.toFixed(3)}s before recovery end → standalone ${late.fired}; ${early.left.toFixed(3)}s before → ${early.fired}`);
    ok(late.miss && !late.next && early.miss && !early.next, 'a wrong button ends the string');
    ok(late.left <= 0.16 && late.fired, 'a wrong press within BUFFER of the recovery end fires as the standalone move');
    ok(early.left > 0.16 && !early.fired, 'a wrong press older than BUFFER at the recovery end is dropped');
  }
  // f. 挡串：L / M → L / M 无空隙（防御硬直一结束按 J 的木人被下一步打中）；重步前留 0.10 ± 0.02 秒（那一刀破绽击中重步）
  {
    const gapRun = seq => {
      const sim = stage('刘备', '刘备', 1.4), f = withSeq(sim, 0, seq), o = sim.f[1];
      let jab = false, gsEnd = -1, res = null;
      o.ctrl.update = (dt, s) => {
        const H = o.ctrl.held; H.guard = true;
        // 防御硬直在这一步结束（o.t 推进一步后 ≥ stun）就按 J（同一步即出招）
        if (o.state === 'guardstun' && f.stats.guarded === 2 && !jab && o.t + dt >= o.stun - 1e-9) { jab = true; H.guard = false; o.ctrl.taps.light++; }   // 第 2 步的防御硬直（攻方此时多半已进入第 3 步）
        if (jab) H.guard = false;
      };
      const out = playString(sim, f, o, { stop: (s) => !!res });
      for (const e of out.events) if (e.type === 'hit' && e.step === 3 && e.att === 0) res = { by: 'next', guarded: e.guarded }; else if (e.type === 'hit' && e.att === 1 && !res) res = { by: 'jab', counter: e.counter };
      const bs = D.stepGap(seq, 1) / f.spd + (seq[2] === 'H' ? -CO.TRAP : CO.BLOCK_PAD);
      return { res, bs, gap: D.stepGap(seq, 1) / f.spd };
    };
    const lm = gapRun('LMLLMHMH'), mh = gapRun('LMHLMLMH');
    ok(!!lm.res && lm.res.by === 'next', `L/M → L/M blockstrings leave no gap: a jab at blockstun end is hit by the next step (${JSON.stringify(lm.res)})`);
    ok(!!mh.res && mh.res.by === 'jab' && mh.res.counter, `the gap before an H step lets a jab at blockstun end counter-hit it (${JSON.stringify(mh.res)})`);
    ok(near(mh.gap - mh.bs, CO.TRAP, 0.02), `the gap before H is ${CO.TRAP} ± 0.02 s (${(mh.gap - mh.bs).toFixed(3)})`);
  }
  // g. 被挡后的优劣（攻方停手）：单出招与 §7.1 一致（±0.02 秒，武力 76）；连段步与 §8.4 的公式一致；武力 30 与 100 同号
  {
    const advOf = (war, setup, mvName) => {
      const g = Object.assign({}, gens['王平'], { war }), sim = stage(g, Object.assign({}, gens['刘备'], { war }), 1.3), f = sim.f[0], o = sim.f[1];
      o.ctrl.held.guard = true; o.ctrl.held.down = mvName === 'cL' || mvName === 'cM'; run(sim, 0.03);
      setup(sim, f);
      let tHit = -1, fFree = -1, oFree = -1;
      for (let k = 0; k < 120; k++) {
        sim.events.length = 0; sim.step();
        if (tHit < 0 && sim.events.some(e => e.type === 'hit' && e.att === 0)) { tHit = sim.clock; o.ctrl.held.guard = false; o.ctrl.held.down = false; f.ctrl.held.down = false; }
        if (tHit >= 0) {
          if (fFree < 0 && (sim.actionable(f) || f.state === 'crouch')) fFree = sim.clock;
          if (oFree < 0 && (sim.actionable(o) || o.state === 'crouch')) oFree = sim.clock;
          if (fFree >= 0 && oFree >= 0) break;
        }
      }
      return fFree - oFree;   // > 0 → 守方先能动 → 攻方负
    };
    const rows = [], badG = [];
    const single = { L: k => { k.ctrl.taps.light++; }, M: k => { k.ctrl.taps.medium++; }, cL: k => { k.ctrl.held.down = true; k.ctrl.taps.light++; }, cM: k => { k.ctrl.held.down = true; k.ctrl.taps.medium++; }, ovh: k => { k.ctrl.taps.ovh++; } };
    for (const name in single) {
      // 单出的 L 只在 J 起手关掉时才有（COMBO.J_OPEN；开着时 J 总是起连段，被挡后的优劣按下面「第 1 步后停手」的连段公式）
      if (name === 'L') CO.J_OPEN = false;
      const vals = [30, 76, 100].map(w => -advOf(w, (s, f) => { withSeq(s, 0, name === 'L' ? 'MLMLHLML' : name === 'M' ? 'LMLMHLMH' : 'LMLMHLMH'); single[name](f); }, name));
      CO.J_OPEN = true;
      const want = MV[name].adv;
      rows.push(`${name} ${vals.map(v => v.toFixed(3)).join('/')} (want ${want})`);
      if (!near(vals[1], want, 0.02 + 1 / 60) || Math.sign(vals[0]) !== Math.sign(want) || Math.sign(vals[2]) !== Math.sign(want)) badG.push(name);
    }
    // 'J:'：第 1 步是中击的串以 J 起手（J 代替第 1 步，用单出轻击的数据）
    for (const key of ['LLMHMLMH', 'MLMHLMLM', 'LHMLMLMH', 'J:MLMHLMLM', 'J:MMLHMLHH']) {
      const jo = key.startsWith('J:'), seq = jo ? key.slice(2) : key, s1 = jo ? 'L' : seq[0];
      const vals = [30, 76, 100].map(w => -advOf(w, (s, f) => { withSeq(s, 0, seq); s.f[0].ctrl.taps[TK[s1]]++; }, seq));
      const f76 = new D._Sim(Object.assign({}, gens['王平'], { war: 76 }), gens['刘备'], D.lookOf(gens['王平']), D.lookOf(gens['刘备'])).f[0];
      const mv = D.stepMove(seq, 0, s1), bs = D.stepGap(seq, 0, s1) / f76.spd + (seq[1] === 'H' ? -CO.TRAP : CO.BLOCK_PAD);
      const want = bs - (mv.active + mv.recovery) / f76.spd;
      rows.push(`stop after step 1 of ${key} ${vals.map(v => v.toFixed(3)).join('/')} (want ${want.toFixed(3)})`);
      if (!near(vals[1], want, 0.02 + 1 / 60) || Math.sign(vals[0]) !== Math.sign(want) || Math.sign(vals[2]) !== Math.sign(want)) badG.push(key);
    }
    console.log('  on-block advantage (war 30/76/100): ' + rows.join('; '));
    ok(badG.length === 0, `on-block advantages match the frame data at war 76 (±0.02 s + 1 frame) with the same sign at 30 and 100 (${badG.join(', ')})`);
  }
  // h. 压制破防：恰好连挡 5 步后终结技破防；4 步不破（守方一直按防键；第 lateAt 步迟按 0.12 秒放人，此后每步都被挡）
  {
    const crush = lateAt => {
      const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: gens['刘备'].war }), 1.4), f = withSeq(sim, 0, 'LMLMLHMH'), o = sim.f[1];   // 同武力：不会硬挡
      o.ctrl.update = () => { o.ctrl.held.guard = f.stats.unguarded > 0; };      // 挨了第一击就一直按防键（真连段里按也没用）
      const out = playString(sim, f, o, { late: 0.10, lateAt });
      return { crush: out.events.some(e => e.type === 'guardcrush'), blocked: out.guarded };
    };
    const c5 = crush(3), c4 = crush(4);
    ok(c5.crush && c5.blocked === 6 && !c4.crush && c4.blocked === 5, `a blocked finisher crushes the guard after exactly 5 blocked steps, not after 4 (${JSON.stringify(c5)} ${JSON.stringify(c4)})`);
    // 中间挥空一步就重新数（复核）：已连挡 4 步时，这一步打空（对手在够不着处）→ 计数归零；同一步被挡 → 5
    const runAfter = far => {
      const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: gens['刘备'].war }), far ? 6 : 1.4), f = withSeq(sim, 0, 'LMLMLHMH'), o = sim.f[1];
      o.ctrl.update = () => { o.ctrl.held.guard = true; };
      f.ctrl.taps[TK['L']]++;
      until(sim, () => f.state === 'str', 0.2);
      f.blockedRun = 4;
      until(sim, () => f.state !== 'str' || f.t >= D.stepMove(f.seqStr, 0, 'L').startup + D.stepMove(f.seqStr, 0, 'L').active + 0.02, 1);
      return f.blockedRun;
    };
    const bw = runAfter(true), bb = runAfter(false);
    ok(bw === 0 && bb === 5, `a whiffed string step resets the guard-crush count (whiff → ${bw}, blocked → ${bb})`);
  }
  // i. 绝技取消：打中 / 被挡时可取消，挥空不行；绝技的伤害按连击递减（下限 0.6）；任何一串 + 取消 ≤ 50 HP（武力同为 100）
  {
    const cancel = (guard, whiff) => {
      const sim = stage(Object.assign({}, gens['吕布'], { war: 100 }), Object.assign({}, gens['吕布'], { war: 100 }), whiff ? 9 : 1.4), f = sim.f[0], o = sim.f[1];
      f.rage = 100; o.ctrl.held.guard = guard;
      let cancelled = false, tot = 0;
      const tick = driver(sim, f, { upto: 7 });
      for (let k = 0; k < 60 * 4; k++) {
        tick();
        if (f.state === 'str' && f.strIdx === 6 && f.t >= f.cp) f.ctrl.taps.special++;
        sim.events.length = 0; sim.step();
        for (const e of sim.events) { if (e.type === 'strCancel') cancelled = true; if (e.type === 'hit' && e.att === 0) tot += e.dmg; }
        if (cancelled && f.state !== 'special' && k > 30) break;
      }
      return { cancelled, tot, hp: o.hp };
    };
    const ch = cancel(false, false), cb = cancel(true, false), cw = cancel(false, true);
    console.log(`  special cancel: on hit ${ch.cancelled} (${ch.tot} HP total), on block ${cb.cancelled}, on whiff ${cw.cancelled}`);
    ok(ch.cancelled && cb.cancelled && !cw.cancelled, 'a string can be cancelled into the special on hit and on block, not on whiff');
    let worst = 0, worstName = '', worstRaw = 0;
    D.critEnabled = false;
    for (const e of SG.Combos.all().filter((x, i) => i % 7 === 0)) {
      const sim = stage(Object.assign({}, worldGens[e.name], { war: 100 }), Object.assign({}, gens['吕布'], { war: 100 }), 1.4), f = sim.f[0];
      f.rage = 100;
      let sum = 0;
      for (let r = 0; r < 4; r++) {
        const sim2 = r ? stage(Object.assign({}, worldGens[e.name], { war: 100 }), Object.assign({}, gens['吕布'], { war: 100 }), 1.4) : sim, f2 = sim2.f[0];
        f2.rage = 100;
        const tick = driver(sim2, f2, { upto: 7 });
        let tot = 0, done = false;
        for (let k = 0; k < 60 * 4 && !done; k++) {
          tick();
          if (f2.state === 'str' && f2.strIdx === 6 && f2.t >= f2.cp) f2.ctrl.taps.special++;
          sim2.events.length = 0; sim2.step();
          for (const ev of sim2.events) { if (ev.type === 'hit' && ev.att === 0) tot += ev.dmg; }
          if (f2.state === 'idle' && k > 60) done = true;
        }
        sum += tot;
      }
      const per = sum / 4;     // 每串 4 次的平均（伤害浮动 ±8 %），含兵器威力（实战的数；§8.5「一次破绽不超过半条体力」，复核：不得按威力折算）
      worstRaw = Math.max(worstRaw, sum / 4 / f.pow);
      if (per > worst) { worst = per; worstName = e.name + ' ' + e.seq; }
    }
    D.critEnabled = true;
    console.log(`  worst 7 steps + special cancel at equal war 100 (mean of 4, incl. weapon pow): ${worst.toFixed(1)} HP (${worstName}); ÷ pow ${worstRaw.toFixed(1)}`);
    ok(worst <= 50, `any string + special cancel deals <= 50 HP at equal war 100, weapon pow included (worst ${worst.toFixed(1)})`);
  }
  // j. 格开后以连段起手反击：第一击 ×1.5、不可挡，之后正常递减
  {
    const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: gens['刘备'].war }), 1.6), L = withSeq(sim, 0, 'LMLMHLMH'), R = sim.f[1];   // 同武力：不会硬挡
    R.ctrl.taps.light++; run(sim, 0.03);
    L.ctrl.taps.parry++;
    until(sim, (s, ev) => ev && ev.type === 'parry', 0.5);
    R.ctrl.held.guard = true;
    const out = playString(sim, L, R, { upto: 3 });
    const hits = out.events.filter(e => e.type === 'hit' && e.att === 0);
    ok(hits.length >= 2 && hits[0].riposte && !hits[0].guarded && hits[0].step === 1 && hits[0].scale === 1, `the riposte may start the string: step 1 is ×1.5 and unblockable (${hits.map(h => h.step + (h.riposte ? 'R' : '') + (h.guarded ? 'g' : '')).join(' ')})`);
    ok(hits.length >= 3 && !hits[1].riposte && hits[2].scale === CO.SCALE[2], `later steps scale normally (${hits.map(h => h.step + ':' + h.scale + (h.guarded ? 'g' : '')).join(' ')})`);
  }
  // k. 击倒（空中被打）结束连段；攻方被打（相打）结束连段
  {
    const sim = stage('刘备', '关羽', 1.4), f = withSeq(sim, 0, 'LMLMHLMH'), o = sim.f[1];
    const tick = driver(sim, f);
    let k3 = false, after = 0;
    for (let k = 0; k < 200; k++) {
      tick();
      if (f.strIdx === 2 && f.state === 'str' && !k3 && f.t < 0.02) { o.y = 0.6; o.vy = 2; k3 = true; }   // 第 3 步打中前把守方抛到空中
      sim.events.length = 0; sim.step();
      for (const e of sim.events) if (e.type === 'strStep' && e.who === 0 && k3 && e.step >= 3) after++;
    }
    ok(k3 && after === 0, `knocking the defender down mid-string ends the string (${after} steps after the knockdown)`);
    const sim2 = stage('刘备', '关羽', 1.4), f2 = withSeq(sim2, 0, 'LMLMHLMH'), o2 = sim2.f[1];
    const tick2 = driver(sim2, f2);
    let hitBack = false, after2 = 0;
    for (let k = 0; k < 200; k++) {
      tick2();
      if (f2.strIdx === 1 && !hitBack) { sim2.setState(f2, 'hitstun'); f2.stun = 0.3; hitBack = true; }
      sim2.events.length = 0; sim2.step();
      for (const e of sim2.events) if (e.type === 'strStep' && e.who === 0 && hitBack) after2++;
    }
    ok(hitBack && after2 === 0, `getting hit mid-string ends the attacker's string (${after2} steps after)`);
  }
});
function sim0Pow(g) { const f = new D._Sim(g, g, D.lookOf(g), D.lookOf(g)).f[0]; return f.pow; }   // 兵器随武力而定（≥ 85 换长兵）：用同一份武将数据

// ---------------------------------------------------------------- (11) 看破连段 --
section(11, 'strings: parry breaks (scripted)', () => {
  // 受击硬直中按 U：第 1–2 步永远不成功；第 3 步起，当且仅当按在 parryW × 类型系数（并不超过上一间隔 − 0.03）之内
  const trial = (seq, target, before, defWar) => {
    const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: defWar || 76 }), 1.4), f = withSeq(sim, 0, seq), o = sim.f[1];
    const tick = driver(sim, f);
    let res = null, early = false, armed = false, hitAt = -1;
    // 先跑一遍找到第 target 步打中的时刻
    return { sim, f, o, tick };
  };
  const hitTime = (seq, target, defWar) => {
    const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: defWar || 76 }), 1.4), f = withSeq(sim, 0, seq);
    const tick = driver(sim, f);
    for (let k = 0; k < 200; k++) { tick(); sim.events.length = 0; sim.step(); for (const e of sim.events) if (e.type === 'hit' && e.att === 0 && e.step === target) return sim.clock; }
    return -1;
  };
  const press = (seq, target, before, defWar) => {
    const T = hitTime(seq, target, defWar);
    const sim = stage('刘备', Object.assign({}, gens['关羽'], { war: defWar || 76 }), 1.4), f = withSeq(sim, 0, seq), o = sim.f[1];
    const tick = driver(sim, f);
    let res = null, early = false, pressed = false, prevHit = target <= 1;
    for (let k = 0; k < 200 && !res; k++) {
      tick();
      if (!pressed && prevHit && sim.clock + 1 / 60 >= T - before - 1e-9) { o.ctrl.taps.parry++; pressed = true; }
      sim.events.length = 0; sim.step();
      for (const e of sim.events) { if (e.type === 'parry') res = e; if (e.type === 'parryEarly') early = true; if (e.type === 'hit' && e.att === 0 && e.step === target) res = res || e; if (e.type === 'hit' && e.att === 0 && e.step === target - 1) prevHit = true; }
    }
    return { res, early, cd: sim.parryCd(o), o, f, sim, T };
  };
  const seq = 'LMLMHLMH';
  // （第 1 步打到前守方不在硬直中：那是站立格开，不在此列）
  for (const t of [2]) {
    const r = press(seq, t, 0.03);
    ok(!!r.res && r.res.type === 'hit' && r.early, `a hitstun parry can never break step ${t} (got ${r.res && r.res.type}, 过早 ${r.early})`);
  }
  const rows = [], bad = [];
  for (const [target, type] of [[3, 'L'], [4, 'M'], [5, 'H'], [8, 'F']]) {
    const sim0 = stage('刘备', '关羽', 1.4), f0 = withSeq(sim0, 0, seq), o0 = sim0.f[1];
    const win = Math.min(o0.parryW * CO.HS_K_TYPE[type], D.stepGap(seq, target - 2) / f0.spd - CO.HS_CAP_PAD);
    // 按键时刻按逻辑步取整（晚至多 1/60 秒）：内侧取 win − 0.02，外侧取 win + 1/60 + 0.004（取整之后仍在窗口外；都在上一击之后）
    const inside = press(seq, target, win - 0.02), outside = press(seq, target, win + 1 / 60 + 0.004);
    rows.push(`step ${target} (${type}) window ${win.toFixed(3)}s: inside ${inside.res && inside.res.type}, outside ${outside.res && outside.res.type}${outside.early ? ' 过早' : ''}`);
    if (!(inside.res && inside.res.type === 'parry' && inside.res.route === 'hitstun')) bad.push('inside ' + target);
    if (!(outside.res && outside.res.type === 'hit' && outside.early && outside.cd > 1.5)) bad.push('outside ' + target);
  }
  console.log('  ' + rows.join('; '));
  ok(bad.length === 0, `hitstun parries from step 3 succeed iff pressed within parryW × K_type before the hit; early → 过早 with the cooldown spent (${bad.join(', ')})`);
  // 举防中格开下一步：全窗口
  {
    const sim0 = stage('刘备', '关羽', 1.4), o0 = sim0.f[1];
    const sim = stage('刘备', '关羽', 1.4), f = withSeq(sim, 0, seq), o = sim.f[1];
    o.ctrl.held.guard = true;
    const tick = driver(sim, f);
    let res = null, pressed = false;
    for (let k = 0; k < 200 && !res; k++) {
      tick();
      // 挡下第 2 步后，在第 3 步打到前约 0.9 × 全窗口按下（超出受击硬直窗口 0.55 × parryW）
      if (!pressed && o.state === 'guardstun' && f.strIdx === 2 && f.state === 'str') {
        const T = sim.clock + Math.max(0, (D.stepMove(seq, 2).startup - f.t) / f.spd);
        if (sim.clock + 1 / 60 >= T - o0.parryW * 0.9) { o.ctrl.taps.parry++; pressed = true; }
      }
      sim.events.length = 0; sim.step();
      for (const e of sim.events) if (e.type === 'parry' || (e.type === 'hit' && e.att === 0 && e.step === 3)) res = e;
    }
    ok(!!res && res.type === 'parry' && res.route === 'guard', `a guardstun parry of the next step uses the full window (got ${res && res.type + '/' + res.route})`);
  }
  // 看破之后：攻方硬直 0.6 秒（重步 0.7）、守方 0.7 秒反击窗口，并可以本人连段起手反击
  {
    const r = press(seq, 5, 0.05);
    ok(!!r.res && r.res.type === 'parry' && r.f.state === 'parried' && near(r.f.stun, PR.STAGGER + 0.1, 1e-6), `breaking an H step staggers the attacker ${PR.STAGGER + 0.1}s (state ${r.f.state}, ${r.f.stun})`);
    ok(r.o.state === 'parryok' && near(r.o.counterUntil - r.sim.clock, PR.COUNTER_WIN, 0.03), 'the defender gets the 0.7 s counter window');
    const r2 = press(seq, 3, 0.05);
    ok(!!r2.res && r2.res.type === 'parry' && near(r2.f.stun, PR.STAGGER, 1e-6), `breaking an L step staggers ${PR.STAGGER}s`);
    r2.o.ctrl.taps[TK[r2.o.seqStr[0]]]++;
    const h = until(r2.sim, (s, ev) => ev && ev.type === 'hit' && ev.att === 1, 0.6);
    ok(!!h && h.riposte && (r2.o.seqStr[0] === 'L' || r2.o.seqStr[0] === 'M') && (h.step === 1 || h.step === 0), `the riposte starts with the defender's own string step 1 (${h && h.step})`);
  }
});

// ---------------------------------------------------------------- (12) 连段对电脑（平衡） --
section(12, 'strings vs AI (balance)', () => {
  const n = Math.max(20, N >> 1);
  const MID8 = [['刘备', '刘备'], ['曹操', '曹操'], ['郭汜', '潘凤'], ['王平', '王平'], ['李傕', '刘备'], ['张济', '曹操'], ['祖茂', '黄祖'], ['孟达', '潘凤']];
  const STRONG = [['关羽', '关羽'], ['吕布', '吕布']], WEAK = [['孙乾', '孙乾']];
  const set = (kind, pairs, opts) => avg(pairs.map(([p, a]) => play(kind, p, a, n, opts).pct));
  // 胜率：连段机器人（命中确认、踩准节拍）不该打垮同武力电脑，但比只会挡的强；会看破的更强
  // 看破机器人的真值约 64–66%（n 960–1600），离上限 70% 只有约 1.5σ（n 20 × 8 对时 σ ≈ 3.6）：这一项取 n ≥ 80（8 对共 640 场，σ ≈ 1.9；
  // n 40 时 σ ≈ 2.6，种子 7 换了随机数流后实测 72%，约 5% 的运行会误报）
  const combo = set('combo', MID8), hb = set('hbguard', MID8), breaker = avg(MID8.map(([p, a]) => play('breaker', p, a, Math.max(80, n)).pct)), comboS = set('combo', STRONG), comboW = set('combo', WEAK);
  console.log(`  vs equal mid AI: combo ${combo.toFixed(0)}%, hbguard ${hb.toFixed(0)}%, breaker ${breaker.toFixed(0)}%; combo vs STRONG ${comboS.toFixed(0)}%, vs WEAK ${comboW.toFixed(0)}%`);
  ok(combo >= hb - 5 && combo >= 30 && combo <= 65, `combo vs equal mid AI should be 30-65% and >= hbguard - 5 (${combo.toFixed(0)}% vs ${hb.toFixed(0)}%)`);
  ok(comboS >= 5 && comboS <= 40, `combo vs strong mirrors should be 5-40% (${comboS.toFixed(0)}%)`);
  ok(breaker >= 35 && breaker <= 70 && breaker >= combo, `breaker vs equal mid AI should be 35-70% and >= combo (${breaker.toFixed(0)}% vs ${combo.toFixed(0)}%)`);
  ok(comboW >= 85, `combo vs weak mirrors should be >= 85% (${comboW.toFixed(0)}%)`);
  // 电脑看破机器人的连段（电脑已见过每一步：战斗记忆 aiKnow 全知）：第一击打中的整串尝试里被看破的比例
  const breakRate = (kind, pairs, nn) => {
    let att = 0, brk = 0, hs = 0;
    for (const [p, a] of pairs) {
      const know = new Map([[p, 255]]);
      let cur = false;     // 当前这一串的第一击打中了（整串尝试）
      play(kind, p, a, nn || nb, { sim: { aiKnow: know }, onEvent(sim, e) {
        if (e.type === 'strStep' && e.who === 0 && e.step === 0) cur = false;
        if (e.type === 'hit' && e.att === 0 && e.step === 1 && !e.guarded) { att++; cur = true; }
        if (e.type === 'parry' && e.att === 0 && cur && e.step >= 0) { brk++; if (e.route === 'hitstun') hs++; cur = false; }
      } });
    }
    return { att, brk, hs, pct: att ? 100 * brk / att : 0, hsPct: att ? 100 * hs / att : 0 };
  };
  const HI = [['关羽', '关羽'], ['吕布', '吕布'], ['张飞', '张飞'], ['赵云', '赵云']], MIDb = [['刘备', '刘备'], ['曹操', '曹操'], ['王平', '王平'], ['李傕', '李傕']], LO = [['孙乾', '孙乾'], ['简雍', '简雍']];
  const nb = Math.max(30, N);
  // 迟滞对比（dH / bH 的硬直看破率之比，历次约 0.44–0.66，门槛 0.7）：两组各取 n ≥ 60，免得 n 40 时偶尔越过门槛
  const nH = Math.max(60, N), bH = breakRate('combo', HI, nH), bM = breakRate('combo', MIDb), bL = breakRate('combo', LO), dH = breakRate('delay', HI, nH);
  console.log(`  AI breaks of combo strings (steps known): war 90-100 ${bH.pct.toFixed(0)}% (${bH.brk}/${bH.att}), war 60-76 ${bM.pct.toFixed(0)}% (${bM.brk}/${bM.att}), war <= 35 ${bL.pct.toFixed(0)}% (${bL.brk}/${bL.att}); vs delay bot ${dH.pct.toFixed(0)}% (${dH.brk}/${dH.att}); hitstun breaks combo ${bH.hsPct.toFixed(0)}% vs delay ${dH.hsPct.toFixed(0)}%`);
  ok(bH.pct >= 25 && bH.pct <= 55, `AI war 90-100 should break 25-55% of known full-string attempts (${bH.pct.toFixed(0)}%)`);
  ok(bM.pct >= 10 && bM.pct <= 35, `AI war 60-76 should break 10-35% (${bM.pct.toFixed(0)}%)`);
  ok(bL.brk === 0, `AI war <= 35 should never break strings (${bL.brk})`);
  // 迟滞对付的是受击硬直中按节拍的看破（§9.3）；迟滞超过 0.08 秒放人后被挡、再在举防中格开下一步，是设计里的「反制之反制」，不算在内
  ok(dH.hsPct <= 0.7 * bH.hsPct, `delaying H steps should cut the AI's hitstun break rate by >= 30% (${dH.hsPct.toFixed(0)}% vs ${bH.hsPct.toFixed(0)}%)`);
  // 电脑对电脑：整串打满（8/8 命中）的次数（每名电脑每场）
  // 每名电脑每场整串数约 0.6（n 600：关羽 / 吕布镜像 0.61–0.63），n 20 时 σ ≈ 0.09，离门槛 0.5 只有约 1.3σ：取 n ≥ 60（σ ≈ 0.05）
  const nf = Math.max(60, n);
  const full = pairs => { let s = 0, m = 0; for (const [a, b] of pairs) for (let i = 0; i < nf; i++) { const r = D.simulate(gens[a], gens[b]); s += r.stats[0].fullStrings + r.stats[1].fullStrings; m += 2; } return s / m; };
  const fT = full([['关羽', '关羽'], ['吕布', '吕布']]), fM = full([['刘备', '刘备'], ['曹操', '曹操'], ['王平', '王平']]), fW = full([['孙乾', '孙乾'], ['简雍', '简雍']]);
  console.log(`  AI full strings per AI per match: war 98-100 ${fT.toFixed(2)}, mid ${fM.toFixed(2)}, weak ${fW.toFixed(2)}`);
  ok(fT >= 0.5, `top AIs should complete >= 0.5 full strings per match (${fT.toFixed(2)})`);
  ok(fM >= 0.25, `mid AIs should complete >= 0.25 full strings per match (${fM.toFixed(2)})`);
  ok(fW <= 0.15, `weak AIs should complete <= 0.15 full strings per match (${fW.toFixed(2)})`);
  // 电脑被挡时仍接重步（对手格开就绪时）≤ 25%；诚实：没见过的一步按中击节奏估（时刻误差 ≥ 0.03 秒）、见过的按真实节奏；
  // 看破只在挨了上一步之后才计划；电脑的见识永远是对手本场出过的步的子集
  let opp = 0, went = 0, pend = null, unk = [], kn = [], before = 0, plans = 0, overKnow = 0;
  for (const [kind, p, a] of MID8.concat(STRONG).map(x => ['combo'].concat(x)).concat(MID8.slice(0, 4).map(x => ['ai'].concat(x)))) {
    play(kind, p, a, n, {
      setup(sim) {
        const A = sim.f[1].ctrl;
        A.onPlan = r => { plans++; const err = Math.abs(r.T - r.trueT); if (r.known) kn.push(err); else if (r.type !== 'M' && r.step !== 7) unk.push(err); if (sim.f[0].state !== 'hitstun' && sim.f[1].state !== 'hitstun') before++; };
      },
      onEvent(sim, e) {
        const ai = sim.f[1];
        if (e.type === 'hit' && e.att === 1 && e.guarded && e.step >= 1 && e.step <= 7 && ai.seqStr[e.step] === 'H' && sim.parryReady(sim.f[0])) { opp++; pend = { idx: e.step, t: sim.clock }; }
        else if (pend && e.type === 'strStep' && e.who === 1) { if (e.step === pend.idx) went++; pend = null; }
      },
      onStep(sim) {
        if (pend && sim.clock - pend.t > 0.8) pend = null;
        const A = sim.f[1].ctrl, foe = sim.f[0];
        if (A.known & ~foe.seenMask & 255) overKnow++;
      },
    });
  }
  const hp = opp ? 100 * went / opp : 0;
  console.log(`  AI continues into an H step on block with the foe's parry ready: ${went}/${opp} (${hp.toFixed(0)}%); break plans ${plans}: known-step error ${(1000 * avg(kn)).toFixed(1)} ms, unknown L/H steps ${(1000 * avg(unk)).toFixed(1)} ms (n${unk.length}); planned outside hitstun ${before}; knowledge beyond seen steps ${overKnow}`);
  ok(hp <= 25, `the AI should go into an H step on block <= 25% of the time when the foe's parry is ready (${hp.toFixed(0)}%)`);
  ok(kn.length === 0 || avg(kn) <= 0.005, `known steps are planned on the true beat (${(1000 * avg(kn)).toFixed(1)} ms)`);
  ok(unk.length === 0 || Math.min(...unk) >= 0.03 - 1e-9, `unknown L/H steps are planned at M timing, >= 0.03 s off (min ${unk.length ? (1000 * Math.min(...unk)).toFixed(1) : '-'} ms)`);
  ok(before === 0 && overKnow === 0, `AI honesty: breaks planned only from hitstun (${before}), knowledge only of steps it has seen (${overKnow})`);
  // 起手键平衡（复核要求）：招式谱第 1 步是中击 vs 是轻击的电脑，同武力、同兵器（长剑）、各取不同的招式谱，左右轮换。
  // 第 1 步是中击的占 256 / 499：没有 J 起手时只胜 36–43%（J 是最快的一键，只有轻击起手的人抓破绽能接整串）。
  // 关口：三格合计 45–55%，每格 40–60%（每格 200 场的噪声约 ±3.5 个百分点；--nightly 每格 300 场）
  {
    const allC = SG.Combos.all(), Ls = allC.filter(e => e.seq[0] === 'L').map(e => e.seq), Ms = allC.filter(e => e.seq[0] === 'M').map(e => e.seq);
    const nP = NIGHTLY ? 300 : 200, cells = [];
    let tot = 0;
    for (const war of [50, 70, 90]) {
      let w = 0;
      for (let i = 0; i < nP; i++) {
        const ga = { name: '甲', war, intel: 50, pol: 50 }, gb = { name: '乙', war, intel: 50, pol: 50 };
        const la = D.lookOf(ga), lb = D.lookOf(gb); la.weapon = lb.weapon = 'sword'; la.bow = lb.bow = false;
        const swap = i % 2 === 1, sim = swap ? new D._Sim(gb, ga, lb, la) : new D._Sim(ga, gb, la, lb), iM = swap ? 1 : 0;
        withSeq(sim, iM, Ms[(i * 5) % Ms.length]); withSeq(sim, 1 - iM, Ls[(i * 3) % Ls.length]);
        sim.f[0].ctrl = new D._AI(sim.f[0]); sim.f[1].ctrl = new D._AI(sim.f[1]);
        sim.runToEnd();
        if (sim.outcome().winner === iM) w++;
      }
      cells.push(100 * w / nP); tot += w;
    }
    const pool = 100 * tot / (3 * nP);
    console.log(`  string-starter parity: M-start vs L-start AI at equal war 50/70/90 (${nP} each): ${cells.map(v => v.toFixed(1)).join(' / ')}%, pooled ${pool.toFixed(1)}%`);
    ok(pool >= 45 && pool <= 55 && cells.every(v => v >= 40 && v <= 60), `M-start strings win 45-55% (pooled) against L-start strings at equal war, each cell 40-60% (${cells.map(v => v.toFixed(1)).join(' / ')}; pooled ${pool.toFixed(1)})`);
  }
});

// ---------------------------------------------------------------- (13) 单挑会心 --
section(13, 'duel crits', () => {
  // 电脑同将（中等武力）：会心占未被挡命中的 4–12%，会心伤害占 ≤ 15%
  const MIDc = [['刘备', '刘备'], ['曹操', '曹操'], ['郭汜', '潘凤'], ['王平', '王平']];
  let crits = 0, ung = 0, cdmg = 0, dmg = 0;
  for (const [a, b] of MIDc) for (let i = 0; i < Math.max(20, N >> 1); i++) { const r = D.simulate(gens[a], gens[b]); for (const s of r.stats) { crits += s.crits; ung += s.unguarded; cdmg += s.critDmg; dmg += s.dmg; } }
  console.log(`  AI mid mirrors: crits ${crits}/${ung} unguarded hits (${(100 * crits / ung).toFixed(1)}%), crit damage share ${(100 * cdmg / dmg).toFixed(1)}%`);
  ok(crits / ung >= 0.04 && crits / ung <= 0.12, `crit share of unguarded hits should be 4-12% (${(100 * crits / ung).toFixed(1)}%)`);
  ok(cdmg / dmg <= 0.15, `crit damage share should be <= 15% (${(100 * cdmg / dmg).toFixed(1)}%)`);
  ok(D.critBase(100) / D.critBase(50) >= 1.6, `base crit ratio war 100 / war 50 >= 1.6 (${(D.critBase(100) / D.critBase(50)).toFixed(2)})`);
  // 脚本：被挡、反击、绝技、箭不会心；破绽 +15%、终结 +10% 生效（固定种子 2000 次强制判定，±1.5 pp）
  {
    const forced = (setup, n) => {
      let c = 0, k = 0;
      for (let i = 0; i < n; i++) {
        const sim = stage('刘备', '刘备', 1.4), f = sim.f[0], o = sim.f[1];
        const r = setup(sim, f, o);
        if (r === null) continue;
        k++; if (r) c++;
      }
      return k ? c / k : 0;
    };
    const hitOnce = (sim, f, o, mvName, prep) => {
      if (prep) prep(sim, f, o);
      if (mvName === 'L') f.ctrl.taps.light++;
      const e = until(sim, (s, ev) => ev && ev.type === 'hit' && ev.att === 0, 0.6);
      return e && e.type === 'hit' ? e : null;
    };
    // 中立轻击（刘备 武力 76 → 3 + 4 × 0.52 = 5.08%）
    const base = D.critBase(76);
    SG.Random.seed(2026);      // 固定种子（§15.2 (13)）
    const neutral = forced((sim, f, o) => { withSeq(sim, 0, 'MLMLHLML'); const e = hitOnce(sim, f, o, 'L'); return e && !e.guarded ? e.crit : null; }, 2000);
    const counter = forced((sim, f, o) => { withSeq(sim, 0, 'MLMLHLML'); o.ctrl.taps.heavy++; o.ctrl.held.heavy = true; run(sim, 0.03); const e = hitOnce(sim, f, o, 'L'); return e && !e.guarded && e.counter ? e.crit : null; }, 2000);
    console.log(`  forced rolls: neutral ${(100 * neutral).toFixed(1)}% (want ${(100 * base).toFixed(1)}), counter-hit ${(100 * counter).toFixed(1)}% (want ${(100 * (base + CR.COUNTER)).toFixed(1)})`);
    ok(Math.abs(neutral - base) <= 0.015, 'neutral crit rate matches the formula within ±1.5 pp');
    ok(Math.abs(counter - (base + CR.COUNTER)) <= 0.03, 'counter-hits add +15% (within ±3 pp at n = 2000)');
    // 终结技
    let fc = 0, fn = 0;
    SG.Random.seed(2027);      // §15.2 (13)：固定种子、2000 次（复核：600 次时下限只差 2σ，偶尔误报）
    for (let i = 0; i < 2000; i++) {
      const sim = stage('刘备', '刘备', 1.4), f = withSeq(sim, 0, 'LMLMHLMM');
      const out = playString(sim, f, sim.f[1]);
      const fin = out.events.find(e => e.type === 'hit' && e.step === 8 && !e.guarded);
      if (fin) { fn++; if (fin.crit) fc++; }
    }
    console.log(`  finisher crit ${(100 * fc / fn).toFixed(1)}% of ${fn} (want ${(100 * (base + CR.FINISHER)).toFixed(1)} … +8 if a tip hit)`);
    ok(fn >= 1600 && fc / fn >= base + CR.FINISHER - 0.015, `finishers add +10% crit chance (${(100 * fc / fn).toFixed(1)}%, at least ${(100 * (base + CR.FINISHER - 0.015)).toFixed(1)})`);
    // 被挡 / 反击 / 绝技 / 箭 永不会心
    let bad = 0;
    for (let i = 0; i < 300; i++) {
      const sim = stage('刘备', '刘备', 1.4), f = sim.f[0], o = sim.f[1];
      o.ctrl.held.guard = true; run(sim, 0.03); f.ctrl.taps.medium++;
      const e = until(sim, (s, ev) => ev && ev.type === 'hit', 0.5); if (e && e.crit) bad++;
    }
    for (let i = 0; i < 200; i++) {
      const sim = stage('关羽', '关羽', 1.6), L = sim.f[0], R = sim.f[1];
      R.ctrl.taps.light++; run(sim, 0.03); L.ctrl.taps.parry++;
      until(sim, (s, ev) => ev && ev.type === 'parry', 0.5);
      L.ctrl.taps.light++;
      const e = until(sim, (s, ev) => ev && ev.type === 'hit' && ev.att === 0, 0.5); if (e && e.crit) bad++;
    }
    for (const who of ['关羽', '黄忠']) for (let i = 0; i < 40; i++) {
      const sim = stage(who, '刘备', 1.6), f = sim.f[0];
      f.rage = 100; f.ctrl.taps.special++;
      for (let k = 0; k < 150; k++) { sim.events.length = 0; sim.step(); for (const e of sim.events) if (e.type === 'hit' && e.att === 0 && e.crit) bad++; }
    }
    ok(bad === 0, `no crits on guarded hits, ripostes, specials or arrows (${bad})`);
    // 关掉会心：照样取随机数（随机数流与开着时相同）
    const streamAt = on => { D.critEnabled = on; SG.Random.seed(99); const sim = stage('刘备', '刘备', 1.4); sim.f[0].ctrl.taps.light++; until(sim, (s, ev) => ev && ev.type === 'hit', 0.5); D.critEnabled = true; return SG.Random.value(); };
    ok(streamAt(false) === streamAt(true), 'switching duel crits off keeps the random stream (one draw per unguarded hit)');
  }
});

// ---------------------------------------------------------------- (14) 性能 --
section(14, 'performance', () => {
  for (let i = 0; i < 3; i++) D.simulate(gens['吕布'], gens['吕布']);
  // 各 20 场一批，取最快的一批。量的是本进程的 CPU 时间（process.cpuUsage），不是墙钟：机器上同时有别的进程时（负载 20–40），
  // 墙钟连最快的一批也会被拖到 15 ms 以上（复核：负载约 30 时墙钟 15.3 ms，同一份代码空闲时 6.7–8.0 ms）；CPU 时间受排队影响小得多，
  // 但虚拟机里超售时仍会偏高（负载 23 时 14 ms），所以再加下面的「多量几批」
  let ms = Infinity, wall = Infinity, steps = 0;
  const M2 = 20;
  // 至少五批；超过 15 ms 时再多量几批（至多 15 批），等一个机器不那么忙的时刻——取的是最小值，多量只会更接近真值
  for (let b = 0; b < 15 && (b < 5 || ms > 15); b++) {
    const t0 = process.hrtime.bigint(), c0 = process.cpuUsage(); let st = 0;
    for (let i = 0; i < M2; i++) { const r = D.simulate(gens['吕布'], gens['吕布']); st += r.time * 60; }
    const c = process.cpuUsage(c0), v = (c.user + c.system) / 1e3 / M2, w = Number(process.hrtime.bigint() - t0) / 1e6 / M2;
    if (v < ms) { ms = v; steps = st; }
    wall = Math.min(wall, w);
  }
  console.log(`  D.simulate 吕布 mirror: ${ms.toFixed(1)} ms CPU per duel (${(steps / M2).toFixed(0)} steps; fastest wall-clock batch ${wall.toFixed(1)} ms)`);
  ok(ms <= 15, `an AI 吕布 mirror simulates in <= 15 ms of CPU time (${ms.toFixed(1)} ms)`);
});

// ---------------------------------------------------------------- (15) 装备在单挑里与兵器平衡 --
section(15, 'items in the duel (budget, best-in-slot sets, 15-war gap, abilities, whitelist) and weapon parity', () => {
  const cell = NIGHTLY ? 300 : 60;          // 每格场数：缺省 / --quick 60，--nightly 300（COMBAT-V4 §15.2 (15)）
  const names = Object.keys(gens).filter(k => gens[k].war >= 60).slice(0, 40);   // 名字只决定招式谱：取剧本里的武将，招式谱各不相同
  // 一场电脑对电脑：A 方（gA，可带 modsA）对 B 方；奇数场左右互换。返回 A 方胜（平局 0.5）
  const duelAB = (i, gA, gB, modsA, lookA, lookB, out) => {
    const la = lookA || D.lookOf(gA), lb = lookB || D.lookOf(gB);
    const ma = modsA ? D.duelModsOf(gA, gB, modsA) : D.duelModsOf(gA, gB, {}), mb = D.duelModsOf(gB, gA, {});
    const swap = i % 2 === 1;
    const sim = swap ? new D._Sim(gB, gA, lb, la, { modsA: mb, modsB: ma }) : new D._Sim(gA, gB, la, lb, { modsA: ma, modsB: mb });
    sim.f[0].ctrl = new D._AI(sim.f[0]); sim.f[1].ctrl = new D._AI(sim.f[1]);
    sim.runToEnd();
    const r = sim.outcome(), ia = swap ? 1 : 0;
    if (out) { out.gain += r.stats[ia].abilGain; out.n++; }
    return r.winner === ia ? 1 : r.winner === 1 - ia ? 0 : 0.5;
  };
  const gOf = (i, war, salt) => ({ name: names[(i * 7 + salt) % names.length], war, intel: 50, pol: 50 });
  // ---- 装备：经 SG.Gear.duelMods（js/items.js）取各稀有度「每部位单挑数值最好的一件」的整套
  let IS = null;
  try { IS = require('./v4-load.js').load({ v4: true, extra: ['items-data.js', 'items.js'] }); } catch (e) { IS = null; }
  if (!IS || !IS.Gear || typeof IS.Gear.duelMods !== 'function') console.log('  (js/items.js not loaded: item cells skipped)');
  else {
    for (const n2 of ['Titles', 'Engage', 'Tourney']) if (IS[n2] && typeof IS[n2] === 'object' && 'enabled' in IS[n2]) IS[n2].enabled = false;
    IS.ScenarioData = IS.Scenarios.classic;   // 经典剧本：独一无二的装备尚无人持有（世界剧本里多在各国君主手上）
    const DI = IS.ItemsData, TIERS = IS.Items.TIER || { common: 0, uncommon: 1, rare: 2, epic: 3, legendary: 4 };
    const score = it => (it.stats.dHp || 0) * 0.4 + (it.stats.dDmg || 0) + (it.stats.dGuard || 0) * 0.2 + (it.stats.war || 0) * 0.05 + (it.stats.crit || 0) * 0.1 + (it.ability ? 0.5 : 0);
    const kitOf = (rar, war) => {
      // 每套一局新游戏（独一无二的装备只能发一次）
      IS.Random.seed(31);
      const g = (IS.G = IS.GameState.newGame('liubei', 'classic')), fac = g.player, city = g.cities.find(c => c.owner === fac).id;
      const t = { id: g.generals.length, name: '试' + rar + war, war, intel: 40, pol: 40, faction: fac, city, loyalty: 90, troops: 2000, training: 50, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
      g.generals.push(t);
      const ids = [];
      for (const sl of ['weapon', 'armour', 'helmet', 'mount', 'treasure', 'offhand']) {
        // 该部位按单挑数值排序，拿得到（独一无二者可能已有人持有）且装得上（盾须配单手兵器）的第一件
        // 「该稀有度或更低」中每部位最好的一件（INTEGRATION §8.3 / integration/duel-gear-estimate.js 的定义；复核：不能只取恰好该稀有度的）
        for (const it of DI.items.filter(x => TIERS[x.rarity] <= TIERS[rar] && x.slot === sl && !(x.req && (x.req.war > war || x.req.intel > 40 || x.req.pol > 40 || x.req.ruler))).sort((a, b) => score(b) - score(a))) {
          const u = IS.Items.award(fac, it.id, { why: 'test' });
          if (u && IS.Gear.equip(t, u, { force: true, free: true }).ok) { ids.push(it.id); break; }
        }
      }
      return { t, ids, mods: IS.Gear.duelMods(t) };
    };
    const RAR = ['common', 'uncommon', 'rare', 'epic', 'legendary'], WARS = [60, 80, 95];
    const rows = [], over = [], kitsSeen = [];
    let legMods = null;
    for (const rar of RAR) {
      const vals = WARS.map(w => {
        const k = kitOf(rar, w), mods = JSON.parse(JSON.stringify(k.mods));
        if (w === 80) kitsSeen.push(`${rar}: ${k.ids.join(',')} (war +${k.mods.war - w}, hp +${k.mods.hp - 100}, dmg ${(100 * k.mods.dmg).toFixed(0)}%, ${k.mods.weapon ? k.mods.weapon.key : '-'}, ${k.mods.abilities.map(a => a.t).join('+') || 'no abilities'})`);
        let s = 0;
        for (let i = 0; i < cell; i++) s += duelAB(i, gOf(i, w, 1), gOf(i, w, 5), mods);
        if (rar === 'legendary' && w === 80) legMods = JSON.parse(JSON.stringify(k.mods));
        if (k.mods.budget && k.mods.budget.used > 6 + 1e-9) over.push(rar + w);
        return 100 * s / cell;
      });
      rows.push(`${rar} ${vals.map(v => v.toFixed(0)).join('/')}`);
      // 夜间档（每格 300 场）每格 ≤ 75%（正式的关口）。缺省 60 场一格的标准误约 5.6 个百分点、三格平均约 3.2：
      // 快速档只抓明显的失衡——三格平均 ≤ 81（75 + 2σ）、每格 ≤ 88
      if (NIGHTLY ? vals.some(v => v > 75) : avg(vals) > 81 || vals.some(v => v > 88)) over.push(rar);
    }
    console.log(`  best-in-slot set vs ungeared at equal war 60/80/95 (${cell} per cell): ${rows.join(' · ')}`);
    console.log(`  sets: ${kitsSeen.join(' · ')}`);
    ok(over.length === 0, `every rarity's best-in-slot set wins <= 75% against an ungeared equal${NIGHTLY ? '' : ' (quick: mean of 3 cells <= 81, each <= 88; the 75% gate is --nightly)'} and stays inside the budget (${over.join(', ')})`);
    // 15 武力差：65 + 传说一整套 vs 80 不带装备
    {
      const k = kitOf('legendary', 65), mods = JSON.parse(JSON.stringify(k.mods));
      let s = 0;
      for (let i = 0; i < cell; i++) s += duelAB(i, gOf(i, 65, 2), gOf(i, 80, 6), mods);
      const pct = 100 * s / cell;
      console.log(`  war 65 + legendary set vs war 80 ungeared: ${pct.toFixed(0)}% (duelWar ${k.mods.war}, hp ${k.mods.hp}, dmg ${k.mods.dmg})`);
      ok(pct <= 30, `a 15-war gap is not bought back by a legendary set (${pct.toFixed(0)}% <= 30%)`);
    }
    // 神兵之能（复核：只数内部计数 abilGain 时多数能力恒为 0）：折合体力 = 同一种子下「带 / 不带」该能力的终局体力差之差（配对），
    // 即规格的「造成的伤害 − 受到的伤害」之差（§15.2 (15)）；另量「+4 体力」作参照（被击倒的一方体力恒为 0，终局差只认得约四分之三）。
    // dHpAdd 折进装备体力（预算里按体力算，不归「能力体力 ≤ 4」），只打印。
    // 目录里每一种单挑参数化都量（按 t / on / eff / cond / 字段分组，数值取各组最大的一份——上限关口，最强者过即全过）；
    // 装备模块会折进数值的（dHpAdd、dDmgK、dGuard、dReach、dParry.window、dGuardBreak、dSpd、dCrit）按 SG.Gear.duelMods 的同样方式折进 mods。
    // 每格：缺省 40 对，夜间 300 对（方差大的——改动出招节奏的那几种——加到 1200 对）。关口：夜间每种 ≤ 4；缺省只抓明显超出（均值 − 2 标准误 > 4）
    {
      const foldOf = e => {
        const m = { abilities: [], abilityHp: 0 };   // 预算按玩家决定（缺省 6：回复至多 4 点，§3）
        switch (e.t) {
          case 'dHpAdd': m.hp = 100 + (e.n || 0); break;
          case 'dDmgK': m.dmg = e.k || 0; break;
          case 'dGuard': m.guardPP = e.k || 0; break;
          case 'dReach': m.reachK = (e.m || 0) / D.WEAPONS.sword.reach; break;
          case 'dGuardBreak': m.guardBreak = (e.k || 0) / 100; break;
          case 'dSpd': m.spd = (e.k || 0) / 100; break;
          case 'dCrit': m.critDuel = (e.crit || 0) / 100; m.critMulDuel = (e.critDmg || 0) / 100; m.abilities.push(e); break;
          case 'dParry': m.parry = e.window || 0; m.abilities.push(e); break;
          default: m.abilities.push(e);
        }
        return m;
      };
      const groups = new Map();
      for (const it of DI.items) for (const e of (it.ability && it.ability.effects) || []) {
        if (!e || D.DUEL_ITEM_EFFECTS.indexOf(e.t) < 0) continue;
        const key = [e.t, e.on || '', e.eff || '', e.cond || '', Object.keys(e).sort().join(',')].join('|');
        const num = Object.values(e).reduce((t, v) => t + (typeof v === 'number' ? Math.abs(v) : 0), 0);
        const g = groups.get(key);
        if (!g || num > g.num) groups.set(key, { e, num });
      }
      const pairN = NIGHTLY ? 300 : 40;
      const pairRun = (mods, n0, gA, gB) => {
        const one = (i, md) => {
          SG.Random.seed(9000 + i);
          const a = gA(i), b = gB(i), ma = D.duelModsOf(a, b, md || {}), mb = D.duelModsOf(b, a, {});
          const swap = i % 2 === 1;
          const sim = swap ? new D._Sim(b, a, D.lookOf(b), D.lookOf(a), { modsA: mb, modsB: ma }) : new D._Sim(a, b, D.lookOf(a), D.lookOf(b), { modsA: ma, modsB: mb });
          sim.f[0].ctrl = new D._AI(sim.f[0]); sim.f[1].ctrl = new D._AI(sim.f[1]);
          sim.runToEnd();
          const A = sim.f[swap ? 1 : 0], B = sim.f[swap ? 0 : 1];
          return A.hp - B.hp;
        };
        let n = 0, s1 = 0, s2 = 0;
        const more = k => { for (let j = 0; j < k; j++, n++) { const d = one(n, mods.with) - one(n, mods.without); s1 += d; s2 += d * d; } };
        more(n0);
        const stat = () => { const m = s1 / n; return { m, se: Math.sqrt(Math.max(0, s2 / n - m * m) / n), n }; };
        let st = stat();
        if (NIGHTLY && st.se > 0.6) { more(1200 - n0); st = stat(); }
        return st;
      };
      const g80 = salt => i => ({ name: names[(i * 7 + salt) % names.length], war: 80, intel: 50, pol: 50 });
      const cal = pairRun({ with: { hp: 104 }, without: null }, pairN, g80(3), g80(4));
      const perHp = 1;
      const res = [], bad = [], zero = [];
      for (const [key, { e }] of groups) {
        const st = pairRun({ with: foldOf(e), without: null }, pairN, g80(3), g80(4));
        const hpEq = st.m / perHp, se = st.se / perHp;
        const label = e.t + (e.on ? '/' + e.on : '') + (e.eff ? '/' + e.eff : '') + (e.cond ? '/' + e.cond : '');
        if (Math.abs(hpEq) < 0.05 && se < 0.05) zero.push(label); else res.push(`${label} ${hpEq.toFixed(1)}±${se.toFixed(1)}`);
        if (e.t !== 'dHpAdd' && (NIGHTLY ? hpEq > 4 : hpEq - 2 * se > 4)) bad.push(`${label} ${hpEq.toFixed(1)}`);
      }
      // 一名武将全部的能力合计：传说一整套（武力 80）带能力 vs 同一套去掉能力
      let leg = null;
      if (legMods) {
        const noAb = JSON.parse(JSON.stringify(legMods)); noAb.abilities = []; noAb.abilityHp = 0;
        const st = pairRun({ with: legMods, without: noAb }, pairN, g80(3), g80(4));
        leg = { hpEq: st.m / perHp, se: st.se / perHp };
      }
      console.log(`  ability HP-equivalent per duel (paired with / without, AI mirror war 80, ${pairN}+ pairs; +4 HP measures ${cal.m.toFixed(2)}): ${res.join(' · ')}`);
      console.log(`  no duel effect: ${zero.join(', ') || '-'}; legendary set's abilities together ${leg ? leg.hpEq.toFixed(1) + '±' + leg.se.toFixed(1) : '-'}`);
      ok(bad.length === 0, `each duel ability alone is worth <= 4 HP per duel${NIGHTLY ? '' : ' (quick: mean - 2 SE)'} (${bad.join(', ')})`);
      ok(!leg || (NIGHTLY ? leg.hpEq <= 4 : leg.hpEq - 2 * leg.se <= 4), `all of one fighter's abilities together <= 4 HP per duel (${leg ? leg.hpEq.toFixed(1) : '-'})`);
    }
    // 白名单：白名单之外的单挑字段在目录检查里报错（载入即报错见 tests/items.js §1）；单挑本身也不读它
    {
      const probs = IS.Items.validateDuel ? IS.Items.validateDuel({ id: 'x_test', ability: { effects: [{ t: 'unblockable' }, { t: 'dArmor', on: 'heavyStart', dmgK: 0.6 }] } }) : ['(no validateDuel)'];
      const dm = D.duelModsOf({ name: '试', war: 80 }, null, { abilities: [{ t: 'unblockable' }, { t: 'dashUnblockable' }, { t: 'dArmor', on: 'heavyStart', dmgK: 0.6 }] });
      ok(probs.length >= 1 && probs.some(p => /unblockable/.test(p)), `SG.Items.validateDuel rejects a non-whitelisted duel field (${probs.join('; ')})`);
      ok(dm.abil.length === 1 && dm.abil[0].t === 'dArmor', `the duel reads only DUEL_ITEM_EFFECTS abilities (${dm.abil.map(a => a.t).join(',')})`);
      // 预算读玩家决定（DESIGN-V4 §20 问题 3；复核：单挑曾把宽松预算 8 悄悄裁回 6）：装备模块给的 budget.limit 优先，否则 Balance.Decisions.duelBudget
      const kit = { hp: 110, dmg: 0.03, guardPP: 4, abilityHp: 0, abilities: [] };
      const b6 = D.duelModsOf({ name: '试', war: 80 }, null, Object.assign({}, kit, { budget: { limit: 6, gearHp: 10, dmg: 3 } }));
      const b8 = D.duelModsOf({ name: '试', war: 80 }, null, Object.assign({}, kit, { budget: { limit: 8, gearHp: 10, dmg: 3 } }));
      const bd = D.duelModsOf({ name: '试', war: 80 }, null, Object.assign({}, kit));
      const dec = SG.Balance.Decisions;
      ok(b6.budget === 6 && b8.budget === 8 && bd.budget === dec.duelBudget && b8.hp > b6.hp && b8.abilCap === dec.duelBudgetLoose.abilityHp && b6.abilCap === 4,
        `the duel applies the same budget as the items module (limit 6 → hp ${b6.hp}, 8 → hp ${b8.hp}, ability cap ${b6.abilCap}/${b8.abilCap}; no limit → Decisions.duelBudget ${bd.budget})`);
    }
  }
  // ---- 兵器平衡：各兵器对长剑，无装备，同武力 80（双方都是近战外观；弓将的绝技不计）
  {
    const keys = Object.keys(D.WEAPONS).filter(k => k !== 'sword'), rows = [], bad = [];
    // 夜间档按 45–55%（正式的关口）——每种 1000 场：300 场的噪声 ±2.9 个百分点，21 种兵器同时判，真在 50% 的兵器也有八成机会至少一种出界；
    // 1000 场 ±1.6，误报约 4%。缺省每种 120 场（复核：60 场 30–70% 太松，62–69% 的兵器也能过）：噪声约 ±4.6、21 种同时判——
    // 每种 35–65%、平均偏差 ≤ 6.5（全都真在 45–55% 时误报约 1%）
    const pcell = NIGHTLY ? 1000 : 120, lo = NIGHTLY ? 45 : 35, hi = NIGHTLY ? 55 : 65;
    const devs = [];
    for (const key of keys) {
      let s = 0;
      for (let i = 0; i < pcell; i++) {
        const a = gOf(i, 80, 0), b = gOf(i, 80, 3);
        const la = D.lookOf(a), lb = D.lookOf(b); la.weapon = key; lb.weapon = 'sword'; la.bow = false; lb.bow = false;
        s += duelAB(i, a, b, null, la, lb);
      }
      const pct = 100 * s / pcell;
      rows.push(`${key} ${pct.toFixed(0)}`);
      devs.push(Math.abs(pct - 50));
      if (pct < lo || pct > hi) bad.push(key + ' ' + pct.toFixed(0));
    }
    console.log(`  weapon vs 长剑 at war 80 (${pcell} per key): ${rows.join(' · ')}; mean |win - 50| ${avg(devs).toFixed(1)}`);
    ok(bad.length === 0, `every duel weapon wins ${lo}-${hi}% against the sword at equal war (${bad.join(', ')})`);
    if (!NIGHTLY) ok(avg(devs) <= 6.5, `weapon parity: mean |win - 50| <= 6.5 pp at ${pcell} duels per key (${avg(devs).toFixed(1)})`);
  }
});

console.log(`${checks} checks, ${((Date.now() - T0) / 1000).toFixed(0)} s`);
console.log(fails === 0 ? "ALL OK" : "FAILURES: " + fails);
process.exitCode = fails;
