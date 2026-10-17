'use strict';
/* ==========================================================================
   单挑格斗（第二版 §4E，第三版格开 §3）人机强度与格开规则回归（Node，无依赖）
   脚本玩家（SG.DuelGame.scriptBot：固定套路，规则随机数走 SG.Random，可复现）对战真实电脑（SG.DuelGame._AI），检查：
   (1) 原有回归（与 tests/duel.html 的 botTally 相同）：跳入空中斩 / 连按 不能以弱胜强；
   (2) 连按（masher）：同武力 72–76 的电脑平均胜率 ≤ 45%，武力 ≤ 40 仍好欺负（≥ 80%），武力 90 以上更难（≤ 30%，但并非无法取胜）；
   (3) 电脑对电脑：武力决定胜负、同将对打五五开；电脑会用格开（武力高者），武力 35 以下不会；
   (4) 格开规则（脚本化的确定性场景）：窗口随武力 0.15–0.22 秒；成功 → 对手硬直约 0.6 秒、己方 0.7 秒反击窗口，
       反击第一击 ×1.5 且不可格挡；落空 → 0.4 秒破绽；冷却 2.5 秒（明显长于攻击与格挡）；受击硬直中按下只格开第三段，
       且须踩准节奏（第三段打到前 0.6 × 窗口内），按早了白费（提示「过早」），没等到第三段则收势破绽；绝技不可格开；
       电脑抓格开落空的破绽（武力越高越准）；
   (5) 格开机器人（parry：熟练的防守反击 + 人的反应 0.22 秒与节奏误差 σ 0.045 秒的格开）与连按格开机器人（spam）
       在不同武力差下的胜率表：强将难、弱将易；连按格开不比连按更占便宜；格开对熟练玩家确有帮助（不低于同一打法不用格开
       的 guard）；按途径 / 招式分类格开次数（确有按节奏格开第三段、看准格开满蓄重击），电脑格开机器人重击的次数有上限；
   (6) 重击与反应：轻点重击（约 0.15 秒）谁都来不及看准格开，满蓄重击（约 0.27 秒）人的反应来得及；电脑看重击的反应
       时间不快于看其他来招，格开满蓄重击至多约一半。
   第三版改动说明：绝技起手（定格与突进）改为无敌（原先对手被定格前已出的招一解冻就能打断绝技）、电脑会格开连按者的连段，
   因此连按武力 98 的同将更难了（约 5–10%），(1) 中「连按 关羽 对 关羽」的下限由 5% 调为 2%（仍须能赢）；
   (2) 中「连按武力 90 以上」的平均下限同理由 3% 调为 2%（N = 60、种子 7/3/11 实测 4.5–7%，修订前后相同，40 场时偶有 2.5%）。
   修订（审查意见）：受击硬直中的格开须踩准第三段的节奏（原先挨了第二段随时按都行）、满蓄重击松手后举刀一顿（起手随蓄力
   0.13 → 0.27 秒）、电脑看重击不再比看其他来招快（原为 0.6 × 反应时间）；(4) C 改为「踩准节奏 → 格开 / 按早 → 白费」，新增 (6)。
   用法：node tests/duel-bots.js [每组场数，默认 40] [种子，默认 7]      退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
for (const f of ['core.js', 'data.js', 'duel-game.js']) vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
const SG = global.SG, D = SG.DuelGame, MV = D.MOVES, PR = D.PARRY;
const N = +process.argv[2] || 40;
SG.Random.seed(+process.argv[3] || 7);

const gens = {};
for (const s of SG.ScenarioData.Generals) { const p = s.split('|'); gens[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3] }; }

let fails = 0, checks = 0;
function ok(cond, msg) { checks++; if (!cond) { fails++; console.log('  FAIL: ' + msg); } }
function section(name, fn) {
  console.log('== ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAILED: ' + (e.stack || e)); }
}
const near = (a, b, eps) => Math.abs(a - b) <= eps;

// 脚本玩家（左，f[0]）对 真实电脑（右）；返回玩家胜率 % 与格开统计
// by：格开按「谁 途径/招式」分类计数（如 'P hitstun/light3'、'AI stance/heavy'）；heavies：双方松手的重击数（满蓄 / 全部）
function play(kind, p, a, n) {
  const P = gens[p], A = gens[a];
  let w = 0, parries = 0, tries = 0, aiParries = 0;
  const by = {}, heavies = { P: 0, PFull: 0, AI: 0, AIFull: 0 };
  for (let i = 0; i < n; i++) {
    const sim = new D._Sim(P, A, D.lookOf(P), D.lookOf(A));
    sim.f[0].ctrl = kind === 'ai' ? new D._AI(sim.f[0]) : D.scriptBot(kind, 0); sim.f[1].ctrl = new D._AI(sim.f[1]);
    sim.running = true;
    for (let k = 0; k < 60 * 75 && !sim.result; k++) {
      sim.events.length = 0; sim.step();
      for (const e of sim.events) {
        if (e.type === 'parry') { const key = (e.def === 0 ? 'P ' : 'AI ') + e.route + '/' + e.move; by[key] = (by[key] || 0) + 1; }
        else if (e.type === 'heavyGo') { const k2 = e.who === 0 ? 'P' : 'AI'; heavies[k2]++; if (e.charge >= 0.85) heavies[k2 + 'Full']++; }
      }
    }
    if (!sim.result) sim.timeUp();
    const o = sim.outcome();
    if (o.winner === 0) w++;
    parries += o.stats[0].parries; tries += o.stats[0].parryTries; aiParries += o.stats[1].parries;
  }
  return { pct: 100 * w / n, parries: parries / n, tries: tries / n, aiParries: aiParries / n, by, heavies };
}
const rate = (kind, p, a, n) => play(kind, p, a, n).pct;

section('(1) botTally regression (same cases as tests/duel.html)', () => {
  const cases = [
    ['jumper', '孙乾', '吕布', 0, 10], ['jumper', '刘备', '吕布', 0, 40], ['jumper', '关羽', '关羽', 0, 75],
    ['masher', '孙乾', '吕布', 0, 10], ['masher', '刘备', '吕布', 0, 40], ['masher', '关羽', '关羽', 2, 75], ['masher', '张飞', '刘备', 60, 100],
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
let masherMidAvg = 0;
section('(2) masher vs equal-war AI', () => {
  const rows = MID.map(([p, a]) => [p, a, rate('masher', p, a, N)]);
  const avg = rows.reduce((s, r) => s + r[2], 0) / rows.length, max = Math.max(...rows.map(r => r[2]));
  masherMidAvg = avg;
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
  ok(sAvg <= 30 && sAvg >= 2, `strong generals (war >= 90) should be hard but not impossible to mash (got ${sAvg.toFixed(0)}%)`);
  ok(sAvg < avg + 5, `strong generals should not be easier to mash than average ones (${sAvg.toFixed(0)}% vs ${avg.toFixed(0)}%)`);
});

section('(3) AI vs AI: war decides, mirror is even; strong AI parries, weak AI does not', () => {
  const n = N;
  const sim = (a, b) => { let w = 0; for (let i = 0; i < n; i++) if (D.simulate(gens[a], gens[b]).winner === 0) w++; return 100 * w / n; };
  const big = sim('吕布', '孙乾'), gap = sim('典韦', '李典'), mirror = sim('关羽', '关羽');
  console.log(`  吕布 vs 孙乾 ${big.toFixed(0)}%, 典韦 vs 李典 ${gap.toFixed(0)}%, 关羽 mirror ${mirror.toFixed(0)}%`);
  ok(big >= 95, '吕布 should crush 孙乾');
  ok(gap >= 75, '典韦(96) should usually beat 李典(78)');
  ok(mirror >= 25 && mirror <= 75, 'mirror match should be roughly even');
  const strongAI = play('ai', '吕布', '吕布', Math.max(10, n >> 1)), weakAI = play('ai', '孙乾', '孙乾', Math.max(10, n >> 1));
  console.log(`  parries per match: 吕布 mirror ${strongAI.parries.toFixed(2)} + ${strongAI.aiParries.toFixed(2)}, 孙乾 mirror ${weakAI.parries.toFixed(2)} (tries ${weakAI.tries.toFixed(2)})`);
  ok(strongAI.parries + strongAI.aiParries >= 0.4, `strong AIs should parry each other sometimes (got ${(strongAI.parries + strongAI.aiParries).toFixed(2)} per match)`);
  ok(weakAI.tries === 0, `war 30 AI should not parry (tried ${weakAI.tries.toFixed(2)} per match)`);
});

// ---------------------------------------------------------------- (4) 格开规则 --
// 手动操作者：taps / held 由测试直接写
function manual() { return { held: { x: 0, up: false, light: false, heavy: false, guard: false, special: false, parry: false, dash: 0 }, taps: { light: 0, up: 0, special: 0, heavy: 0, dir: 0, parry: 0 }, update() {} }; }
// 场景：左 f[0]、右 f[1] 相距 dist，双方手动操作
function stage(a, b, dist) {
  const A = gens[a], B = gens[b];
  const sim = new D._Sim(A, B, D.lookOf(A), D.lookOf(B));
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

section('(4) parry rules (scripted)', () => {
  // 窗口随武力
  console.log(`  window: war 30 ${D.parryWindow(30).toFixed(3)}s, war 65 ${D.parryWindow(65).toFixed(3)}s, war 100 ${D.parryWindow(100).toFixed(3)}s; cooldown ${PR.CD}s`);
  ok(near(D.parryWindow(30), 0.15, 1e-6) && near(D.parryWindow(100), 0.22, 1e-6) && D.parryWindow(65) > 0.15 && D.parryWindow(65) < 0.22, 'parry window should scale 0.15 → 0.22 s with war');
  // 冷却明显长于普通攻击（轻击一个循环）与格挡（随按随放）
  const l1 = MV.light1, cyc = (l1.startup + l1.active + l1.recovery) / 0.9;
  ok(PR.CD >= 2 && PR.CD > cyc * 4, `parry cooldown ${PR.CD}s should be clearly longer than a light attack cycle (${cyc.toFixed(2)}s)`);

  // A. 成功格开：右方轻击，左方在起手中按 U → 对手 parried ≈0.6 秒，己方 parryok + 0.7 秒反击窗口
  {
    const sim = stage('关羽', '关羽', 1.6), L = sim.f[0], R = sim.f[1];
    R.ctrl.taps.light++;
    run(sim, 0.03);
    L.ctrl.taps.parry++;
    const e = until(sim, (s, ev) => ev && ev.type === 'parry', 0.5);
    ok(!!e && e.def === 0 && e.move === 'light1', 'a light attack landing inside the window should be parried');
    ok(R.state === 'parried' && near(R.stun, PR.STAGGER, 1e-6), `attacker should be staggered ${PR.STAGGER}s (state ${R.state}, stun ${R.stun})`);
    ok(L.state === 'parryok' && near(L.counterUntil - sim.clock, PR.COUNTER_WIN, 0.02), `parrier should get a ${PR.COUNTER_WIN}s counter window (state ${L.state})`);
    ok(R.hp === 100 && L.hp === 100, 'a parry deals no damage');
    // 对手硬直结束后按住格挡，反击仍在窗口内 → 不可格挡、×1.5
    R.ctrl.held.guard = true;
    const tParry = sim.clock;
    until(sim, s => s.clock >= tParry + 0.52, 1);
    ok(R.state === 'parried', 'attacker is still staggered 0.52 s after the parry');
    L.ctrl.taps.light++;
    const h = until(sim, (s, ev) => ev && ev.type === 'hit' && ev.att === 0, 0.4);
    ok(!!h && h.riposte && !h.guarded, `the first hit inside the counter window should be an unblockable riposte (riposte ${h && h.riposte}, guarded ${h && h.guarded}, defender ${R.state})`);
    const base = MV.light1.dmg * (D.WEAPONS.guandao.pow || 1) * PR.COUNTER_K, exp = D.damage(base, 98, 98);
    ok(!!h && h.dmg >= Math.floor(exp * 0.92) && h.dmg <= Math.ceil(exp * 1.08), `riposte damage should be x${PR.COUNTER_K} (got ${h && h.dmg}, expected about ${exp.toFixed(1)})`);
    ok(!sim.countering(L), 'the counter window is used up by the first hit');
    // 冷却中再按 U：不进入格开
    const cdLeft = sim.parryCd(L);
    ok(cdLeft > 1.5 && cdLeft < PR.CD, `parry should be on cooldown right after use (${cdLeft.toFixed(2)}s left)`);
  }
  // B. 落空：没有来招 → 窗口过后 0.4 秒破绽；冷却 2.5 秒（期间再按无效），冷却好了才能再格开；挨打算「破绽」
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
    // 落空破绽中挨打 = 破绽 ×1.2
    const sim2 = stage('刘备', '刘备', 1.6), L2 = sim2.f[0], R2 = sim2.f[1];
    L2.ctrl.taps.parry++;
    until(sim2, s => L2.state === 'parrywhiff', 0.5);
    R2.ctrl.taps.light++;
    const h = until(sim2, (s, ev) => ev && ev.type === 'hit' && ev.att === 1, 0.4);
    ok(!!h && h.counter && !h.guarded, 'a hit during the whiff recovery should be a counter hit (破绽)');
  }
  // C. 看破连段：受击硬直中按 U 只格开第三段；只打两段则硬直结束时收势破绽
  {
    const combo = (sim, R, n) => {
      R.ctrl.taps.light++;
      let pressed = 1;
      return () => { if (pressed < n && MV[R.state] && R.state === 'light' + pressed && R.hitIdx > 0) { R.ctrl.taps.light++; pressed++; } };
    };
    // 踩准节奏：在第三段打到前 0.3 × 窗口按下 → 格开；挨了第二段立刻按（早于 0.6 × 窗口）→ 白费，第三段照样打中、提示「过早」
    const third = (early) => {
      const sim = stage('刘备', '关羽', 1.5), L = sim.f[0], R = sim.f[1];
      const tick = combo(sim, R, 3);
      let pressedU = false, ev = null, earlyEv = null;
      for (let i = 0; i < 120 && !ev; i++) {
        tick();
        const now2 = early ? R.state === 'light2' && R.hitIdx > 0 : R.state === 'light3' && (MV.light3.startup - R.t) / R.spd <= L.parryW * PR.HS_K * 0.5;
        if (!pressedU && now2 && L.state === 'hitstun') { L.ctrl.taps.parry++; pressedU = true; }
        sim.events.length = 0; sim.step();
        ev = sim.events.find(e => e.type === 'parry' || (e.type === 'hit' && e.move === 'light3')) || null;
        earlyEv = earlyEv || sim.events.find(e => e.type === 'parryEarly') || null;
      }
      return { pressedU, ev, earlyEv };
    };
    const good = third(false), bad = third(true);
    ok(good.pressedU && !!good.ev && good.ev.type === 'parry' && good.ev.move === 'light3' && good.ev.route === 'hitstun', `U pressed on the 3rd hit's rhythm should parry it (got ${good.ev ? good.ev.type + ' ' + good.ev.move : 'none'})`);
    ok(bad.pressedU && !!bad.ev && bad.ev.type === 'hit' && !!bad.earlyEv, `U pressed right as the 2nd hit lands is too early: the 3rd hit lands, flagged 过早 (got ${bad.ev ? bad.ev.type : 'none'}, early ${!!bad.earlyEv})`);
    const gap23 = (MV.light2.active * 0.6 + MV.light3.startup) / 1.12;   // 第二段打中 → 第三段打中（武力 100 出招最快）
    ok(PR.HS_K * D.parryWindow(100) < gap23 - 0.03, `the hitstun parry must be timed: its window (${(PR.HS_K * D.parryWindow(100)).toFixed(3)}s) should be clearly shorter than the 2nd→3rd hit gap (${gap23.toFixed(3)}s)`);
    // 只打两段
    const sim2 = stage('刘备', '关羽', 1.5), L2 = sim2.f[0], R2 = sim2.f[1];
    const tick2 = combo(sim2, R2, 2);
    let pressed2 = false, whiff = false;
    for (let i = 0; i < 90; i++) {
      tick2();
      if (!pressed2 && R2.state === 'light2' && R2.hitIdx > 0 && L2.state === 'hitstun') { L2.ctrl.taps.parry++; pressed2 = true; }
      sim2.step();
      if (L2.state === 'parrywhiff') { whiff = true; break; }
    }
    ok(pressed2 && whiff && L2.whiffT < PR.WHIFF, 'a hitstun parry that never meets a 3rd hit should leave a short whiff when the hitstun ends');
    // 第一段挨打后按 U 不能格开第二段（连段前两段只能硬吃或格挡）
    const sim3 = stage('刘备', '关羽', 1.5), L3 = sim3.f[0], R3 = sim3.f[1];
    const tick3 = combo(sim3, R3, 2);
    let p3 = false, parried = false;
    for (let i = 0; i < 60; i++) {
      tick3();
      if (!p3 && R3.state === 'light1' && R3.hitIdx > 0 && L3.state === 'hitstun') { L3.ctrl.taps.parry++; p3 = true; }
      sim3.events.length = 0; sim3.step();
      if (sim3.events.some(e => e.type === 'parry')) parried = true;
    }
    ok(p3 && !parried, 'a hitstun parry should not parry the 2nd hit of a combo');
  }
  // D. 挡下一击后格开连段的下一击（举防 → 格开 → 反击）
  {
    const sim = stage('关羽', '刘备', 1.5), L = sim.f[0], R = sim.f[1];
    L.ctrl.held.guard = true;
    run(sim, 0.05);
    R.ctrl.taps.light++;
    let chained = false, pressed = false, ev = null;
    for (let i = 0; i < 60 && !ev; i++) {
      if (!chained && R.state === 'light1' && R.hitIdx > 0) { R.ctrl.taps.light++; chained = true; }
      if (!pressed && L.state === 'guardstun') { L.ctrl.taps.parry++; pressed = true; }
      sim.events.length = 0; sim.step();
      ev = sim.events.find(e => e.type === 'parry') || null;
    }
    ok(!!ev && ev.move === 'light2', `blocking the 1st hit then pressing U should parry the 2nd (got ${ev ? ev.move : 'none'})`);
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
    // 只统计真正落空的格开（电脑恰好打进窗口的算格开成功，不计）：落空破绽中（或刚恢复的一瞬）挨打 = 抓到
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
    const strong = punish('吕布', '刘备', 30), weak = punish('孙乾', '刘备', 30);
    console.log(`  AI punishes a whiffed parry: 吕布 ${strong.toFixed(0)}%, 孙乾 ${weak.toFixed(0)}%`);
    ok(strong >= 50, `strong AI should usually punish a whiffed parry (got ${strong.toFixed(0)}%)`);
    ok(strong > weak + 20, `strong AI should punish whiffed parries much more often than weak AI (${strong.toFixed(0)}% vs ${weak.toFixed(0)}%)`);
  }
});

// ---------------------------------------------------------------- (5) 胜率表 --
section('(5) parry bot / parry-spam bot vs AI across war gaps', () => {
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
  const avg = (k, keys) => keys.reduce((s, x) => s + T[x][k].pct, 0) / keys.length;
  const mid = ['刘备>刘备', '曹操>曹操', '郭汜>潘凤'], strong = ['关羽>关羽', '吕布>吕布'];
  const spamMid = avg('spam', mid), mashMid = avg('masher', mid), spamStrong = avg('spam', strong);
  const parryMid = avg('parry', mid), guardMid = avg('guard', mid), parryStrong = avg('parry', strong), guardStrong = avg('guard', strong);
  console.log(`  mid (72-76) avg: masher ${mashMid.toFixed(0)}%, spam ${spamMid.toFixed(0)}%, guard ${guardMid.toFixed(0)}%, parry ${parryMid.toFixed(0)}%;` +
    ` strong (98-100) avg: spam ${spamStrong.toFixed(0)}%, guard ${guardStrong.toFixed(0)}%, parry ${parryStrong.toFixed(0)}%`);
  // 连按格开不占便宜
  ok(spamMid <= 45, `parry-spam should not beat equal mid AIs (got ${spamMid.toFixed(0)}%, want <= 45%)`);
  ok(spamMid <= mashMid + 15, `parry-spam should not be much better than plain mashing (${spamMid.toFixed(0)}% vs ${mashMid.toFixed(0)}%)`);
  ok(spamStrong <= 30, `parry-spam vs war 98-100 should be hard (got ${spamStrong.toFixed(0)}%)`);
  ok(T['孙乾>孙乾'].spam.pct >= 70, 'weak generals stay easy even for the parry-spammer');
  // 格开对熟练玩家有用，但强将仍难、弱将仍易
  const parryUse = mid.concat(strong).reduce((s, x) => s + T[x].parry.parries, 0) / 5;
  ok(parryUse >= 0.5, `the parry bot should actually land parries (got ${parryUse.toFixed(2)} per match)`);
  ok(parryMid >= guardMid - 5 && parryStrong >= guardStrong - 5, `parrying should help a skilled player (mid ${parryMid.toFixed(0)}% vs ${guardMid.toFixed(0)}%, strong ${parryStrong.toFixed(0)}% vs ${guardStrong.toFixed(0)}%)`);
  ok(parryStrong <= 90, `even a skilled parry player should not always beat war 98-100 mirrors (got ${parryStrong.toFixed(0)}%)`);
  ok(T['刘备>吕布'].parry.pct <= 40, `刘备(76) should rarely beat 吕布(100) even with parries (got ${T['刘备>吕布'].parry.pct.toFixed(0)}%)`);
  ok(T['孙乾>孙乾'].parry.pct >= 80 && T['张飞>刘备'].parry.pct >= 80, 'a skilled player should beat weak / much weaker AIs');
  // 格开途径分类（parry 机器人 = 人的反应与节奏误差）：确实是按节奏 / 看准才格开的，而电脑看准格开重击有上限
  const by = {}, hv = { P: 0, PFull: 0, AI: 0, AIFull: 0 };
  for (const x of mid.concat(strong)) { const r = T[x].parry; for (const k in r.by) by[k] = (by[k] || 0) + r.by[k]; for (const k in hv) hv[k] += r.heavies[k]; }
  console.log('  parry bot, mid + strong mirrors, parries by route/move: ' + Object.keys(by).sort().map(k => `${k} ${by[k]}`).join(', '));
  console.log(`  heavies released: bot ${hv.P} (full ${hv.PFull}), AI ${hv.AI} (full ${hv.AIFull})`);
  const sum = re => Object.keys(by).filter(k => re.test(k)).reduce((t, k) => t + by[k], 0);
  ok(sum(/^P hitstun\/light3$/) > 0, 'the human-latency bot should land some timed 3rd-hit parries');
  // heavy 机器人（同 parry，试探多用重击，一半满蓄）对强将：电脑格开重击的次数有上限（满蓄至多约一半，轻点几乎没有）
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
// 玩家（左，手动）从重击距离松开重击，右方为真实电脑：电脑看重击与看其他来招同样慢（this.react），轻点重击来不及格开，
// 满蓄重击来得及但至多一半；人的反应（0.22 ± 0.04 秒）也能格开满蓄重击
section('(6) heavy release: reaction parries are fair for both sides', () => {
  // 全体武将中出招最快 / 最慢者：轻点重击（蓄力 0.15）松手到打中、满蓄重击松手到打中（实际秒数）
  let sMin = 9, sMax = 0;
  for (const g of Object.values(gens)) { const sp = new D._Sim(g, g, D.lookOf(g), D.lookOf(g)).f[0].spd; sMin = Math.min(sMin, sp); sMax = Math.max(sMax, sp); }
  const rt = (c, sp) => D.heavySU(c, sp) / sp;
  console.log(`  heavy release → impact: tap ${rt(0.15, sMax).toFixed(3)}–${rt(0.15, sMin).toFixed(3)}s, full charge ${rt(1, sMax).toFixed(3)}–${rt(1, sMin).toFixed(3)}s`);
  ok(rt(0.15, sMin) < 0.17, `a tap heavy should be too fast to react to (${rt(0.15, sMin).toFixed(3)}s)`);
  ok(rt(1, sMax) >= 0.24, `a full-charge heavy should be reactable by a human (${rt(1, sMax).toFixed(3)}s >= 0.24s)`);
  // 电脑（右）对玩家松开的重击
  const vsAI = (p, ai, chg, n) => {
    const out = { parried: 0, hit: 0, guarded: 0, gbreak: 0 }, lat = [];
    const react = new D._AI(new D._Sim(gens[ai], gens[ai], D.lookOf(gens[ai]), D.lookOf(gens[ai])).f[0]).react;
    for (let i = 0; i < n * 3 && out.parried + out.hit + out.guarded + out.gbreak < n; i++) {
      const sim = stage(p, ai, 2.05 + (i % 4) * 0.05), L = sim.f[0], R = sim.f[1];
      R.ctrl = new D._AI(R);
      run(sim, 0.3 + (i % 9) * 0.04);
      if (!sim.actionable(L)) continue;
      L.ctrl.taps.heavy++; L.ctrl.held.heavy = true;
      let rel = -1, res = null, press = -1;
      for (let k = 0; k < 150 && !res; k++) {
        if (L.state === 'charge' && L.charge >= chg) L.ctrl.held.heavy = false;
        sim.events.length = 0; sim.step();
        for (const e of sim.events) {
          if (e.type === 'heavyGo' && e.who === 0) rel = sim.clock;
          if (e.type === 'parryStart' && e.who === 1 && rel >= 0 && press < 0) press = sim.clock;
          if (e.type === 'parry' && e.def === 1) res = 'parried';
          if (e.type === 'hit' && e.att === 0) res = e.breaks ? 'gbreak' : e.guarded ? 'guarded' : 'hit';
          if (e.type === 'hit' && e.att === 1) res = 'skip';
        }
        if (rel >= 0 && !res && L.state !== 'heavy' && L.state !== 'charge') res = 'skip';
      }
      if (!res || res === 'skip') continue;
      out[res]++;
      if (res === 'parried') lat.push(press - rel);
    }
    const n2 = out.parried + out.hit + out.guarded + out.gbreak;
    return { out, n: n2, pct: n2 ? 100 * out.parried / n2 : 0, minLat: lat.length ? Math.min(...lat) : null, react };
  };
  for (const [p, ai] of [['关羽', '吕布'], ['关羽', '关羽'], ['张飞', '张辽'], ['刘备', '曹操']]) {
    const t = vsAI(p, ai, 0.15, 40), f = vsAI(p, ai, 0.95, 60);
    console.log(`  ${p} heavy vs AI ${ai}(${gens[ai].war}): tap parried ${t.pct.toFixed(0)}% of ${t.n} ${JSON.stringify(t.out)}; full parried ${f.pct.toFixed(0)}% of ${f.n} ${JSON.stringify(f.out)}` +
      (f.minLat !== null ? `; AI press ≥ ${(f.minLat * 1000).toFixed(0)}ms after release (react ${(f.react * 1000).toFixed(0)}ms)` : ''));
    ok(t.pct <= 5, `AI ${ai} should not parry tap heavies on reaction (${t.pct.toFixed(0)}%)`);
    ok(f.pct <= 100 * PR.HEAVY_CAP + 15, `AI ${ai} should parry at most about half of full-charge heavies (${f.pct.toFixed(0)}%)`);
    ok(f.minLat === null || f.minLat >= f.react - 1e-6, `AI ${ai} must not react to a heavy faster than to other attacks (${f.minLat !== null ? (f.minLat * 1000).toFixed(0) : '-'}ms < ${(f.react * 1000).toFixed(0)}ms)`);
  }
  // 人的反应：看到松手 0.22 ± 0.04 秒后按 U
  const human = (def, att, chg, n) => {
    let par = 0, tot = 0;
    for (let i = 0; i < n; i++) {
      const sim = stage(def, att, 2.1), L = sim.f[0], R = sim.f[1];
      R.ctrl.taps.heavy++; R.ctrl.held.heavy = true;
      const rt = 0.22 + 0.04 * ((SG.Random.value() + SG.Random.value() + SG.Random.value() + SG.Random.value() - 2) * 1.732);
      let rel = -1, res = null, pressed = false;
      for (let k = 0; k < 150 && !res; k++) {
        if (R.state === 'charge' && R.charge >= chg) R.ctrl.held.heavy = false;
        if (rel >= 0 && sim.clock >= rel + rt && !pressed) { L.ctrl.taps.parry++; pressed = true; }
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

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
