'use strict';
/* ==========================================================================
   逻辑层模拟测试（Node，无依赖）
   移植自验证 Unity 版所用的两个 C# 测试程序：
   (1) 全电脑模拟：6 个种子 × 40 年，每月检查一致性；外加 40 场电脑对电脑的战术战斗。
   (2) 随机玩家模拟：6 个势力 × 4 个种子 × 25 年，玩家每月随机下达指令。
   第二版：(1b) 的电脑战斗会施展必杀技；(1c) 必杀技平衡（同一批种子关闭 / 开启必杀技的胜率、天数对比，
   以及实战中必杀技与普通攻击的平均伤害比）；(4) 必杀技数据自检（每位武将都有招式、名称唯一、
   (机制, 参数) 唯一、兜底生成器确定且不撞车）；(5) useSpecial 随机压力测试（不得出现 NaN / 负兵力 /
   重叠 / 越界）与每位武将的参考伤害倍率；(6) duel() 拆分后随机数顺序不变。
   第三版：(7) 成长——等级表、格数与等阶、升级候选与习得、存档 v3 与第二版存档迁移、可习得必杀与计策的数据和规则、
   成长后的压力测试、多年全电脑模拟（成长开 / 关）的等级分布、战术战斗平衡（同一批局面成长开 / 关对比）与成长的分量。
   (1)–(3) 在成长打开时运行：第 1 级的战斗与第二版完全相同，自动结算不看等级，故输出与第二版一致。
   第四版（COMBAT-V4 §2.7 / §15.3）：载入 econ.js 与 battle-math.js；(1d) 军队会心——公式、混乱不会心、每次 attack() 恰好一个
   会心随机数（开 / 关配对）、装备 + 官职封顶、内政经验只算一半的等级项，以及 (1c) 战斗组合 300 场的会心率、强弱比、胜率 / 天数 /
   伤害 / 超时的变化与 battleExp 的会心经验。(1)–(3)、(7e)–(7h) 的数字因随机数流后移而与第三版不同（只更新输出，容差不变）。
   用法：node tests/sim.js        退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
// 第四版：载入 econ.js / battle-math.js（SG.Mods：军队会心 COMBAT-V4 §2、战斗乘数；没有提供者时乘数为 1）——
// attack() 每次先取一个会心随机数，(1)–(3) 的输出因随机数流后移而与第三版不同（COMBAT-V4 §2.2：只更新输出，不放宽容差）
for (const f of ['core.js', 'data.js', 'econ.js', 'model.js', 'strategy-ai.js', 'battle-math.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js']) {
  vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
}
const SG = global.SG;
const { GameState, Commands, Conquest, StrategyAI, BattleModel, DevKind } = SG;

// 一致性检查（两组测试共用）
function checkConsistency(g) {
  for (const c of g.cities) {
    if (c.owner >= 0 && !g.factions[c.owner].alive) throw new Error('dead faction owns city ' + c.name);
    if (c.gold < 0 || c.food < 0) throw new Error('negative res ' + c.name + ' gold=' + c.gold + ' food=' + c.food);
  }
  for (const gen of g.generals) {
    if (gen.troops < 0) throw new Error('neg troops ' + gen.name);
    if (gen.faction >= 0 && !g.factions[gen.faction].alive) throw new Error('gen in dead faction ' + gen.name);
  }
  for (const f of g.factions.filter(f => f.alive)) {
    const r = g.generals[f.ruler];
    if (r.dead || r.faction !== f.id) throw new Error('bad ruler ' + f.name + ' -> ' + r.name + ' dead=' + r.dead + ' fac=' + r.faction);
  }
}

function samePoint(a, b) { return a.x === b.x && a.y === b.y; }

let fails = 0;
const t0 = Date.now();

// =========================================================== (1) 全电脑模拟 --
console.log('== (1) all-AI simulation: 6 seeds x 40 years ==');
const alive10 = [];
for (let seed = 1; seed <= 6; seed++) {
  SG.Random.seed(seed);
  const g = SG.G = GameState.newGame('liubei');
  g.player = -1; // 让所有势力由电脑控制（runAI 跳过 player）
  const start = Date.now();
  let month = 0;
  let line = '';
  try {
    for (; month < 12 * 40; month++) {
      const news = [];
      const pb = StrategyAI.runAI(news);
      if (pb.length > 0) throw new Error('player battles with no player');
      StrategyAI.endMonth();
      checkConsistency(g);
      const alive = g.factions.filter(f => f.alive).length;
      if (month === 119) { line += `[10y alive ${alive}] `; alive10.push(alive); }
      if (alive <= 1) break;
    }
    const top = g.factions.filter(f => f.alive)
      .map(f => ({ f, n: g.cityCount(f.id) }))
      .sort((a, b) => b.n - a.n)
      .slice(0, 5)
      .map(o => o.f.name + ':' + o.n);
    console.log(`${line}seed ${seed}: ${month} months, alive ${g.factions.filter(f => f.alive).length}, neutral ${g.cities.filter(c => c.owner < 0).length}, top [${top.join(', ')}]  ${Date.now() - start}ms`);
  } catch (e) {
    fails++;
    console.log(`seed ${seed} FAILED at month ${month}: ${e.stack || e}`);
  }
}
if (alive10.length) {
  console.log(`factions alive at year 10: [${alive10.join(', ')}]  (min ${Math.min(...alive10)}, max ${Math.max(...alive10)}, avg ${(alive10.reduce((a, b) => a + b, 0) / alive10.length).toFixed(1)}; Unity tuning gave roughly 4-7)`);
}

// 战术战斗：电脑对电脑完整打完（与 battle-controller.js 的电脑回合相同：按 planFor / planAction 行动；
// 疾行令友军再动时，同一回合内再扫一遍可行动的部队）
const M = SG.M;
const median = a => { if (!a.length) return 0; const b = a.slice().sort((x, y) => x - y); return b[b.length >> 1]; };
const pairs = [['chenliu', 'luoyang'], ['pingyuan', 'nanpi'], ['changsha', 'jiangling'], ['beiping', 'nanpi'], ['wan', 'xiangyang']];
// 必杀技结算后的状态检查：无 NaN、无负兵力、士气 0..100、存活部队不越界不重叠、燃烧 / 加成合法
function checkBattleState(Mdl, where) {
  const occ = new Set();
  for (const u of Mdl.units) {
    if (!Number.isFinite(u.troops) || u.troops < 0) throw new Error(where + ': bad troops ' + u.gen.name + ' ' + u.troops);
    if (!Number.isFinite(u.morale) || u.morale < 0 || u.morale > 100) throw new Error(where + ': bad morale ' + u.gen.name + ' ' + u.morale);
    if (!Number.isInteger(u.confused) || u.confused < 0) throw new Error(where + ': bad confused ' + u.gen.name + ' ' + u.confused);
    if (u.alive) {
      if (!Mdl.passable(u.x, u.y)) throw new Error(where + ': unit on impassable tile ' + u.gen.name + ' ' + u.x + ',' + u.y);
      const k = u.x + ',' + u.y;
      if (occ.has(k)) throw new Error(where + ': two units on ' + k);
      occ.add(k);
      // （士气 0 的存活部队在第一版里就可能出现：断粮时士气下降不立即判定溃散）
      if (!(u.troops >= 50)) throw new Error(where + ': alive unit should have routed ' + u.gen.name + ' troops ' + u.troops + ' morale ' + u.morale);
    }
    for (const m of u.mods || []) {
      if (!(m.days >= 1)) throw new Error(where + ': mod with days ' + m.days);
      for (const key of ['atk', 'def', 'counter']) if (m[key] !== undefined && !(Number.isFinite(m[key]) && m[key] > 0)) throw new Error(where + ': bad mod ' + key + '=' + m[key]);
      if (m.dot !== undefined && !(m.dot >= 0 && m.dot < 1)) throw new Error(where + ': bad dot ' + m.dot);
    }
    if (!Number.isFinite(u.mobility) || u.mobility < 1) throw new Error(where + ': bad mobility ' + u.mobility);
  }
  for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) if (!Number.isInteger(Mdl.burning[x][y]) || Mdl.burning[x][y] < 0) throw new Error(where + ': bad burning ' + Mdl.burning[x][y]);
  if (!Number.isInteger(Mdl.ap) || Mdl.ap < 0) throw new Error(where + ': bad ap ' + Mdl.ap);
}
function checkSpecialResult(res, where) {
  // 被必杀技波及的敌军：士气归零者必须已溃散
  const caster = res.sp ? res.sp.gen : '';
  for (const x of res.affected) if (x.alive && x.gen.name !== caster && res.healed.every(h => h.unit !== x) && res.buffed.indexOf(x) < 0 && res.refreshed.indexOf(x) < 0 && !(x.morale > 0)) throw new Error(where + ': affected enemy alive with morale 0: ' + x.gen.name);
  for (const k of ['dmg', 'heal', 'gained']) if (!Number.isFinite(res[k]) || res[k] < 0) throw new Error(where + ': res.' + k + ' = ' + res[k]);
  for (const h of res.hits) if (!Number.isFinite(h.dmg) || h.dmg < 0) throw new Error(where + ': hit dmg ' + h.dmg);
  for (const h of res.healed) if (!Number.isFinite(h.amount) || h.amount < 0) throw new Error(where + ': heal ' + h.amount);
}

// 倍率的分母：武力系招式 = 对同一目标的一次普通攻击（期望）；智力 / 政治系 = 普通攻击与火计期望中较大者
function refAction(Mdl, u, t, sp) {
  let ref = Mdl.atkExpect(u, t);
  if (sp.stat !== 'war' && u.gen.intel >= 50) {
    // 火计的实际期望：与 useTactic 相同的地形修正（林地 ×1.8、本城 / 城门 ×0.6）与防御加成
    const tt = Mdl.map[t.x][t.y];
    const terr = tt === SG.Terrain.Forest ? 1.8 : (tt === SG.Terrain.Castle || tt === SG.Terrain.Gate) ? 0.6 : 1;
    const fire = M.clamp(0.5 + (u.gen.intel - t.gen.intel) / 110, 0.12, 0.92) * (260 + u.gen.intel * 11) * terr * M.clamp(t.troops / 2500, 0.4, 1.4) / Mdl.modK(t, 'def');
    ref = Math.max(ref, fire);
  }
  return Math.min(ref, t.troops);
}
function runBattle(seed, pr, stats) {
  SG.Random.seed(seed * 7);
  const g = SG.G = GameState.newGame('cao');
  const src = g.cityByKey(pr[0]); const tgt = g.cityByKey(pr[1]);
  const atk = g.officersIn(src).filter(x => x.troops > 0).slice(0, 5);
  const s = Conquest.prepare(src.owner, src, tgt, atk, 3000, 0);
  const Mdl = new BattleModel(s);
  let guard = 0;
  while (Mdl.result === 0 && guard++ < 2000) {
    for (let pass = 0; pass < 4 && Mdl.result === 0; pass++) {
      const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u));
      if (order.length === 0) break;
      for (const u of order) {
        if (!Mdl.canAct(u) || Mdl.result !== 0) continue;
        const plan = Mdl.planFor(u);
        if (plan.move) {
          const p = Mdl.pathTo(u, plan.move);
          if (p.length === 0 || !samePoint(p[0], u) || !samePoint(p[p.length - 1], plan.move)) throw new Error('bad path ' + JSON.stringify(p) + ' from ' + u.x + ',' + u.y + ' to ' + JSON.stringify(plan.move));
          for (let i = 1; i < p.length; i++) {
            if (Math.abs(p[i].x - p[i - 1].x) + Math.abs(p[i].y - p[i - 1].y) !== 1) throw new Error('path not contiguous ' + JSON.stringify(p));
            if (!Mdl.passable(p[i].x, p[i].y)) throw new Error('path through impassable tile ' + JSON.stringify(p[i]));
          }
          if (Mdl.unitAt(plan.move.x, plan.move.y) != null) throw new Error('move onto unit');
          Mdl.move(u, plan.move);
        }
        if (Mdl.result !== 0) break;
        const act = Mdl.planAction(u);
        if (act.kind === 'attack') {
          const r = Mdl.attack(u, act.target);
          if (stats) stats.atkDmg.push(r.dmg);
          const cs = stats && stats.crit;   // 第四版 (1d)：会心统计
          if (cs) {
            cs.att++; cs.p += r.critP || 0; cs.dmg += r.dmg;
            if (r.crit) { cs.n++; cs.extra += r.dmg - r.dmg / r.critK; }
            const b = u.gen.war >= 90 ? 'hi' : u.gen.war >= 50 && u.gen.war < 60 ? 'lo' : '';
            if (b) { cs[b + 'A']++; if (r.crit) cs[b + 'C']++; }
          }
        }
        else if (act.kind === 'tactic') Mdl.useTactic(u, act.tactic, act.target);
        else if (act.kind === 'duel') Mdl.duel(u, act.target);
        else if (act.kind === 'special') {
          const sp = Mdl.specialOf(u);
          // 同一时刻该部队“普通一手”的期望伤害（对同一主目标的普通攻击，或智力 ≥ 50 时的火计），作为倍率的分母
          const tg = act.target;
          let ref = 0;
          if (tg && tg.side !== u.side) {
            ref = refAction(Mdl, u, tg, sp);
          }
          const holder = Mdl.unitAt(Mdl.castle.x, Mdl.castle.y);   // 本城上的守军（必杀技不得把他挪走）
          const res = Mdl.useSpecial(u, act.target);
          if (!res.sp || !u.specialUsed) throw new Error('planned special not used: ' + u.gen.name + ' ' + res.why);
          if (holder && holder.side === 1 && holder.alive && (holder.x !== Mdl.castle.x || holder.y !== Mdl.castle.y)) throw new Error(sp.name + ' moved ' + holder.gen.name + ' off the castle');
          checkSpecialResult(res, 'battle ' + seed + ' ' + u.gen.name);
          checkBattleState(Mdl, 'battle ' + seed + ' ' + u.gen.name + ' ' + sp.name);
          if (stats) {
            stats.specials++;
            stats.kinds[sp.kind] = (stats.kinds[sp.kind] || 0) + 1;
            if (res.dmg > 0 && sp.kind !== 'assassinate' && sp.kind !== 'poison' && sp.kind !== 'roar' && sp.kind !== 'scheme') {
              stats.spDmg.push(res.dmg);
              if (ref > 0) {
                const n = new Set(res.hits.map(h => h.unit)).size;
                stats.ratio.push(res.dmg / ref); stats.ratio1.push(res.dmg / n / ref);
                (stats.byKind[sp.kind] = stats.byKind[sp.kind] || []).push(res.dmg / ref);
              }
            }
            if (sp.kind === 'assassinate') stats.assassin.push(res.killed ? 1 : 0);
          }
        }
        Mdl.spend(u);
      }
    }
    if (Mdl.result === 0) Mdl.endSide();
  }
  if (Mdl.result === 0) throw new Error('battle never ended');
  for (const u of Mdl.units) if (u.troops < 0) throw new Error('negative unit troops');
  return { g, s, Mdl };
}

console.log('== (1b) 40 AI-vs-AI tactical battles (with specials) ==');
let bw = 0, bl = 0, bdays = 0, bsp = 0;
const reasons = {};
for (let seed = 1; seed <= 40; seed++) {
  const pr = pairs[seed % pairs.length];
  try {
    const st = { specials: 0, spDmg: [], atkDmg: [], ratio: [], ratio1: [], byKind: {}, kinds: {}, assassin: [] };
    const { g, s, Mdl } = runBattle(seed, pr, st);
    bdays += Mdl.day; if (Mdl.result === 1) bw++; else bl++;
    bsp += st.specials;
    reasons[Mdl.resultReason] = (reasons[Mdl.resultReason] || 0) + 1;
    s.attackerWon = Mdl.result === 1;
    Conquest.apply(s);
    if (s.captives.length > 0) Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
    checkConsistency(g);
    if (seed <= 5) console.log(`battle ${pr[0]}->${pr[1]}: result ${Mdl.result} day ${Mdl.day} reason ${Mdl.resultReason} routed ${s.routed.size} captives ${s.captives.length} specials ${st.specials}`);
  } catch (e) {
    fails++;
    console.log('battle seed ' + seed + ' FAILED: ' + (e.stack || e));
  }
}
console.log(`tactical battles: attacker won ${bw}, defender won ${bl}, avg days ${(bdays / Math.max(1, bw + bl)).toFixed(1)}, specials used ${bsp} (${(bsp / 40).toFixed(1)} per battle)`);
console.log('  end reasons: ' + Object.keys(reasons).map(k => k + ' ×' + reasons[k]).join('  '));

// (1c) 必杀技平衡：同一批种子与城池组合，关闭 / 开启必杀技各打一遍
console.log('== (1c) specials balance: 200 battles with specials off vs on ==');
try {
  const pairs2 = pairs.concat([['xiapi', 'xiaopei'], ['changan', 'tianshui'], ['chengdu', 'jiangzhou'], ['ye', 'jinyang'], ['shouchun', 'lujiang']]);
  const runs = {};
  for (const on of [false, true]) {
    BattleModel.specialsEnabled = on;
    const r = runs[on] = { w: 0, n: 0, days: 0, timeout: 0, st: { specials: 0, spDmg: [], atkDmg: [], ratio: [], ratio1: [], byKind: {}, kinds: {}, assassin: [] }, reasons: {} };
    for (let seed = 1; seed <= 200; seed++) {
      const pr = pairs2[seed % pairs2.length];
      const { Mdl } = runBattle(1000 + seed, pr, r.st);
      r.n++; r.days += Mdl.day; if (Mdl.result === 1) r.w++;
      if (Mdl.resultReason.indexOf('期限') >= 0) r.timeout++;
      r.reasons[Mdl.resultReason] = (r.reasons[Mdl.resultReason] || 0) + 1;
    }
  }
  BattleModel.specialsEnabled = true;
  const avg = a => a.reduce((x, y) => x + y, 0) / Math.max(1, a.length);
  for (const on of [false, true]) {
    const r = runs[on];
    console.log(`  specials ${on ? 'ON ' : 'OFF'}: attacker win ${(100 * r.w / r.n).toFixed(1)}% (${r.w}/${r.n}), avg days ${(r.days / r.n).toFixed(1)}, timeouts ${r.timeout}, normal attack avg dmg ${avg(r.st.atkDmg).toFixed(0)}`);
  }
  const on = runs[true];
  const ratio = median(on.st.ratio), ratio1 = median(on.st.ratio1);
  console.log(`  specials per battle ${(on.st.specials / on.n).toFixed(2)}; damage specials avg ${avg(on.st.spDmg).toFixed(0)} vs normal attack avg ${avg(on.st.atkDmg).toFixed(0)}`);
  console.log(`  same unit, same moment: special damage = ${ratio.toFixed(2)}x its own normal action (median over ${on.st.ratio.length} uses; per unit hit ${ratio1.toFixed(2)}x)`);
  console.log('    (normal action = one normal attack on the same target for war-based specials, best of attack / fire tactic for intel-based; target 1.8-2.5; specials take no counter-attack)');
  console.log('    by kind (median): ' + Object.keys(on.st.byKind).sort().map(k => k + ' ' + median(on.st.byKind[k]).toFixed(2)).join(', '));
  console.log('  kinds used: ' + Object.keys(on.st.kinds).sort((a, b) => on.st.kinds[b] - on.st.kinds[a]).map(k => k + '×' + on.st.kinds[k]).join(' '));
  if (on.st.assassin.length) console.log(`  assassinations: ${on.st.assassin.filter(x => x).length}/${on.st.assassin.length} succeeded`);
  const dw = Math.abs(runs[true].w - runs[false].w) / runs[true].n;
  if (dw > 0.15) throw new Error('attacker win rate shifted by ' + (dw * 100).toFixed(1) + ' points with specials');
  if (on.st.specials < on.n * 0.5) throw new Error('AI almost never uses specials: ' + on.st.specials);
  if (!(ratio >= 1.6 && ratio <= 2.6)) throw new Error('special/attack damage ratio out of range: ' + ratio.toFixed(2));
  if (!(ratio1 >= 1.0 && ratio1 <= 2.4)) throw new Error('special/attack per-unit ratio out of range: ' + ratio1.toFixed(2));
  // 各机制的实战中位数（样本 ≥ 25 才判定；实战里常有补刀、残兵，单项波动大，故区间比设计目标宽）
  for (const k of Object.keys(on.st.byKind)) if (on.st.byKind[k].length >= 25) {
    const m = median(on.st.byKind[k]);
    if (!(m >= 1.3 && m <= 3.0)) throw new Error('in-battle median for ' + k + ' out of range: ' + m.toFixed(2));
  }
  if (runs[true].timeout > runs[false].timeout + 30) throw new Error('specials made many more battles time out');
} catch (e) {
  BattleModel.specialsEnabled = true;
  fails++;
  console.log('specials balance FAILED: ' + (e.stack || e));
}

// (1d) 第四版军队会心（COMBAT-V4 §2.7）：(1c) 的战斗组合 300 场，会心关 / 开（同一批种子；attack() 无论开关都先取一个随机数，一一配对）
console.log('== (1d) army crits: 300 battles with crits off vs on (paired streams) ==');
try {
  const Mo = SG.Mods, B = SG.Balance;
  if (!Mo || !Mo.critChance || !Mo.rollCrit) throw new Error('SG.Mods.critChance / rollCrit missing (js/battle-math.js not loaded)');
  // ---- 规则单元：公式、混乱、反击、每次恰好一个随机数 ----
  const expectP = (war, lvl, form, morale) => M.clamp(B.CritBase + B.CritPerWar * Math.max(0, war - B.CritWarFrom) + B.CritPerLevel * (lvl - 1) + Mo.FORM_CRIT[form] + (morale >= 90 ? 0.01 : 0), 0, B.CritCap);
  let unitBad = 0;
  for (const war of [30, 50, 70, 100]) for (const lvl of [1, 10]) for (const form of [0, 1, 5]) {
    const gen = { name: 't', war, intel: 50, pol: 50, level: lvl, exp: 0, dexp: 0 };
    const p = Mo.critChance({ gen, formation: form, morale: 70, confused: 0 });
    if (Math.abs(p - expectP(war, lvl, form, 70)) > 1e-12) { unitBad++; console.log(`  critChance war ${war} L${lvl} form ${form}: ${p} expected ${expectP(war, lvl, form, 70)}`); }
  }
  const g100 = { name: 't', war: 100, intel: 50, pol: 50, level: 10, exp: 0, dexp: 0 };
  if (Math.abs(Mo.critChance({ gen: g100, formation: 5, morale: 95, confused: 0 }) - expectP(100, 10, 5, 95)) > 1e-12) unitBad++;
  if (Mo.critChance({ gen: g100, formation: 5, morale: 95, confused: 1 }) !== 0) { unitBad++; console.log('  a confused unit must never crit'); }
  // 非战斗经验只算一半（vetLevel = levelFor(exp − 0.5 × dexp)）：同样的经验全是内政经验时，等级项比战斗经验少
  const Lx = SG.Progress.levelFor, need10 = (() => { let e = 0; while (Lx(e) < 10 && e < 1e6) e += 10; return e; })();
  const gBattle = { name: 'b', war: 50, intel: 50, pol: 50, exp: need10, dexp: 0, level: Lx(need10) };
  const gDom = { name: 'd', war: 50, intel: 50, pol: 50, exp: need10, dexp: need10, level: Lx(need10) };
  const pB = Mo.critChance({ gen: gBattle, formation: 1, morale: 70, confused: 0 }), pD = Mo.critChance({ gen: gDom, formation: 1, morale: 70, confused: 0 });
  const vB = Mo.vetLevel(gBattle), vD = Mo.vetLevel(gDom);
  console.log(`  level term: battle exp ${need10} → vetLevel ${vB} crit ${(pB * 100).toFixed(2)}%; the same exp all domestic → vetLevel ${vD} crit ${(pD * 100).toFixed(2)}%`);
  if (!(vD < vB && pD < pB && vD === Lx(need10 * 0.5))) { unitBad++; console.log('  domestic exp should count half for the crit level term'); }
  // 一次 attack() 恰好取一个会心随机数：开 / 关后随机数流位置相同；反击从不会心
  const streamAfter = critOn => {
    SG.Random.seed(5); SG.G = GameState.newGame('cao');
    const g = SG.G, c0 = g.cityByKey('chenliu');
    const s0 = Conquest.prepare(c0.owner, c0, g.cityByKey('luoyang'), g.officersIn(c0).filter(x => x.troops > 0).slice(0, 2), 0, 0);
    const Md = new BattleModel(s0), a = Md.units[0], t = Md.units.find(u => u.side === 1);
    BattleModel.critEnabled = critOn; SG.Random.seed(77);
    const r = Md.attack(a, t);
    const v = SG.Random.value();
    BattleModel.critEnabled = true;
    return { v, r };
  };
  const sOff = streamAfter(false), sOn = streamAfter(true);
  if (sOff.v !== sOn.v) { unitBad++; console.log('  attack() must draw exactly one crit roll whether crits are on or off'); }
  if (sOff.r.crit) { unitBad++; console.log('  crits off must never crit'); }
  // 强制会心（p = 1 的提供者）：伤害 × critK、士气多扣 5、反击照常且不会心（attack 只返回攻方的 crit）
  {
    const off = Mo.provide ? Mo.provide({ gearCrit: () => 1, titleCrit: () => 1 }, { owner: 'sim-1d' }) : null;
    SG.Random.seed(5); SG.G = GameState.newGame('cao');
    const g = SG.G, c0 = g.cityByKey('chenliu');
    const s0 = Conquest.prepare(c0.owner, c0, g.cityByKey('luoyang'), g.officersIn(c0).filter(x => x.troops > 0).slice(0, 2), 0, 0);
    const Md = new BattleModel(s0), a = Md.units[0], t = Md.units.find(u => u.side === 1);
    a.gen.war = 100; a.formation = 5; a.morale = 95;
    const p = Mo.critChance(a);
    if (off) { if (typeof off === 'function') off(); else if (off.off) off.off(); }
    console.log(`  forced: gear + title crit capped at +${(B.CritGearCap * 100).toFixed(0)} pp → p(war 100, 锋矢, morale 95) = ${(p * 100).toFixed(2)}% (cap ${(B.CritCap * 100).toFixed(0)}%)`);
    if (Math.abs(p - Math.min(B.CritCap, expectP(100, Mo.vetLevel(a.gen), 5, 95) + B.CritGearCap)) > 1e-9) { unitBad++; console.log('  gear + title crit should add at most CritGearCap'); }
  }
  if (unitBad) throw new Error(unitBad + ' crit unit checks failed');
  console.log('  unit checks: formula at war 30/50/70/100 × L1/L10 × formations, confused 0, one draw per attack, cap: OK');

  // ---- 300 场配对平衡 ----
  const pairs2 = pairs.concat([['xiapi', 'xiaopei'], ['changan', 'tianshui'], ['chengdu', 'jiangzhou'], ['ye', 'jinyang'], ['shouchun', 'lujiang']]);
  const runs = {};
  for (const on of [false, true]) {
    BattleModel.critEnabled = on;
    const r = runs[on] = { w: 0, n: 0, days: 0, timeout: 0, xpCrits: 0, st: { specials: 0, spDmg: [], atkDmg: [], ratio: [], ratio1: [], byKind: {}, kinds: {}, assassin: [],
      crit: { att: 0, n: 0, p: 0, dmg: 0, extra: 0, hiA: 0, hiC: 0, loA: 0, loC: 0 } } };
    for (let seed = 1; seed <= 300; seed++) {
      const { Mdl } = runBattle(1000 + seed, pairs2[seed % pairs2.length], r.st);
      r.n++; r.days += Mdl.day; if (Mdl.result === 1) r.w++;
      if (Mdl.resultReason.indexOf('期限') >= 0) r.timeout++;
      for (const u of Mdl.units) r.xpCrits += (u.xp && u.xp.crits) || 0;
    }
  }
  BattleModel.critEnabled = true;
  const f1 = x => (100 * x).toFixed(1) + '%';
  for (const on of [false, true]) {
    const r = runs[on], c = r.st.crit;
    console.log(`  crits ${on ? 'ON ' : 'OFF'}: attacker win ${f1(r.w / r.n)}, avg days ${(r.days / r.n).toFixed(2)}, time-outs ${r.timeout}, attacks ${c.att}, crits ${c.n} (${f1(c.n / Math.max(1, c.att))}), avg attack dmg ${(c.dmg / Math.max(1, c.att)).toFixed(0)}`);
  }
  const on = runs[true], off = runs[false], c = on.st.crit;
  const rate = c.n / Math.max(1, c.att), hi = c.hiC / Math.max(1, c.hiA), lo = c.loC / Math.max(1, c.loA);
  const dWin = 100 * (on.w - off.w) / on.n, dDays = (on.days - off.days) / on.n;
  const dmgK = (c.dmg / c.att) / (off.st.crit.dmg / off.st.crit.att), share = c.extra / Math.max(1, c.dmg);
  console.log(`  crit rate ${f1(rate)} (target 4-9%; mean p ${f1(c.p / Math.max(1, c.att))}); war >= 90 ${f1(hi)} (${c.hiA} attacks) vs war 50-59 ${f1(lo)} (${c.loA}) = x${(hi / Math.max(1e-9, lo)).toFixed(1)} (target >= 2)`);
  console.log(`  attacker win shift ${dWin >= 0 ? '+' : ''}${dWin.toFixed(1)} pp (target <= 4), days ${dDays >= 0 ? '+' : ''}${dDays.toFixed(2)} (<= 1.0), attack damage x${dmgK.toFixed(3)} (+2 … +7 %), crit extra damage share ${f1(share)} (<= 6%), time-outs ${on.timeout} vs ${off.timeout} (<= off + 10), xp.crits ${on.xpCrits} (= crits ${c.n})`);
  if (!(rate >= 0.04 && rate <= 0.09)) throw new Error('crit rate ' + f1(rate) + ' outside 4-9%');
  if (!(hi >= 2 * lo)) throw new Error('crit rate of war >= 90 should be at least twice war 50-59');
  if (Math.abs(dWin) > 4) throw new Error('attacker win rate shifted ' + dWin.toFixed(1) + ' pp with crits');
  if (Math.abs(dDays) > 1.0) throw new Error('battle length shifted ' + dDays.toFixed(2) + ' days with crits');
  if (!(dmgK >= 1.02 && dmgK <= 1.07)) throw new Error('average attack damage x' + dmgK.toFixed(3) + ' outside +2 … +7 %');
  if (share > 0.06) throw new Error('crit extra damage share ' + f1(share) + ' > 6%');
  if (on.timeout > off.timeout + 10) throw new Error('crits made more battles time out');
  if (on.xpCrits !== c.n) throw new Error('xp.crits ' + on.xpCrits + ' != crits ' + c.n);
  if (off.st.crit.n !== 0) throw new Error('crits happened while switched off');
  // Progress.battleExp：每次会心 +2（上限之内）
  const x0 = { dmg: 0, heal: 0, routs: 0, cmdRouts: 0, duelWin: 0, duelLoss: 0, specials: 0, tactics: 0, tacFail: 0, crits: 0, won: false, commander: false };
  const e0 = SG.Progress.battleExp(x0), e3 = SG.Progress.battleExp(Object.assign({}, x0, { crits: 3 }));
  console.log(`  Progress.battleExp: 0 crits ${e0}, 3 crits ${e3} (+2 each)`);
  if (e3 - e0 !== 6) throw new Error('battleExp should add 2 per crit');
} catch (e) {
  BattleModel.critEnabled = true;
  fails++;
  console.log('army crits FAILED: ' + (e.stack || e));
}

// ========================================================= (2) 随机玩家模拟 --
console.log('== (2) random-player simulation: 6 factions x 4 seeds x 25 years ==');
for (const key of ['liubei', 'cao', 'dong', 'kongrong', 'menghuo', 'lukang']) {
  for (let seed = 1; seed <= 4; seed++) {
    SG.Random.seed(seed * 13 + key.length);
    const g = SG.G = GameState.newGame(key);
    const rnd = SG.SeededRandom(seed);
    let month = 0, attacks = 0, defends = 0, execs = 0, hires = 0, releases = 0;
    try {
      for (; month < 12 * 25 && g.playerFaction.alive; month++) {
        g.tokens = g.tokensFor(g.player);
        let guard = 0;
        while (g.tokens > 0 && guard++ < 30) {
          const cities = g.citiesOf(g.player); if (cities.length === 0) break;
          const c = cities[rnd.next(cities.length)];
          const offs = g.officersIn(c); if (offs.length === 0) { g.tokens--; continue; }
          const gen = offs[rnd.next(offs.length)];
          const op = rnd.next(9);
          switch (op) {
            case 0: Commands.develop(c, gen, rnd.next(3)); break;
            case 1: Commands.recruit(c, gen, Commands.recruitMax(c, gen)); break;
            case 2: Commands.train(gen); break;
            case 3: Commands.search(c, gen); if (Commands.searchFound != null && Commands.hire(Commands.searchFound, gen, g.player, c.id)) hires++; break;
            case 4: { const d = c.links.map(i => g.cities[i]).find(x => x.owner === g.player); if (d && !gen.moved) Commands.move(gen, d); break; }
            case 5: {
              let t = null;
              for (const x of c.links.map(i => g.cities[i]).filter(x => x.owner >= 0 && x.owner !== g.player)) {
                t = g.officersIn(x).find(y => !g.isRuler(y)) || null;
                if (t) break;
              }
              if (t) { if (rnd.next(2) === 0) Commands.discord(gen, t); else Commands.persuade(gen, t, g.player); }
              break;
            }
            case 6: {
              const cand = g.factions.filter(x => x.alive && x.id !== g.player).map(x => ({ x, k: rnd.next(2147483647) }));
              cand.sort((a, b) => a.k - b.k);
              const f = cand.length ? cand[0].x : null;
              if (f) Commands.ally(c, g.player, f.id, 100);
              break;
            }
            case 7: Commands.buyFood(c, Math.min(c.gold, 50)); break;
            default: {
              const ts = c.links.map(i => g.cities[i]).filter(x => x.owner !== g.player && !g.allied(x.owner, g.player));
              const t = ts.length ? SG.Seq.orderBy(ts, x => g.troopsIn(x))[0] : null;
              const squad = offs.filter(x => !x.moved && x.troops > 0).slice(0, 5);
              if (t && squad.length > 0) {
                attacks++;
                const s = Conquest.prepare(g.player, c, t, squad, Math.min(c.food, 2000), 0);
                Conquest.autoResolve(s); Conquest.apply(s);
                for (const cap of s.captives.slice()) {
                  if (!s.attackerWon) { Conquest.aiDecideCaptives(s, s.defender); break; }
                  const ch = rnd.next(3); const ruler = cap.faction >= 0 && g.factions[cap.faction].ruler === cap.id;
                  if (ch === 0 && !ruler) { if (!Commands.hire(cap, g.ruler(g.player), g.player, s.target.id)) { Conquest.release(cap); releases++; } }
                  else if (ch === 2) { Conquest.execute(cap); execs++; }
                  else { Conquest.release(cap); releases++; }
                }
                for (const f of g.factions) if (f.alive && g.cityCount(f.id) === 0) g.checkFactionDeath(f.id);
              }
              break;
            }
          }
          g.tokens--;
        }
        const news = [];
        const pb = StrategyAI.runAI(news);
        for (const s of pb) {
          defends++;
          Conquest.autoResolve(s); Conquest.apply(s);
          if (s.captives.length > 0) Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
          for (const f of g.factions) if (f.alive && g.cityCount(f.id) === 0) g.checkFactionDeath(f.id);
          if (!g.playerFaction.alive) break;
        }
        if (g.playerFaction.alive) StrategyAI.endMonth();
        checkConsistency(g);
      }
      console.log(`${key} seed ${seed}: ${month} mo, alive=${g.playerFaction.alive} cities=${g.cityCount(g.player)} gens=${g.generalsOf(g.player).length} atk=${attacks} def=${defends} hires=${hires} execs=${execs} releases=${releases}`);
    } catch (e) {
      fails++;
      console.log(`${key} seed ${seed} FAILED month ${month}: ${e.stack || e}`);
    }
  }
}

// ============================================================ 存档往返 --
// Node 下没有 localStorage：save() 应返回 false、load() 返回 null，且不抛出。
console.log('== (3) save/load round trip ==');
try {
  SG.Random.seed(99);
  const g = SG.G = GameState.newGame('cao');
  for (let i = 0; i < 14; i++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
  if (g.save() !== false) throw new Error('save() without localStorage should return false');
  if (GameState.hasSave() !== false) throw new Error('hasSave() without localStorage should be false');
  if (GameState.load() !== null) throw new Error('load() without localStorage should be null');
  const text = JSON.stringify(g);
  if (text.indexOf('"links"') >= 0) throw new Error('save JSON must not contain city.links');
  const back = GameState.fromJSON(text);
  if (JSON.stringify(back) !== text) throw new Error('JSON round trip mismatch');
  if (back.cities.some((c, i) => c.links.join() !== g.cities[i].links.join())) throw new Error('links not rebuilt');
  // 模拟浏览器存储
  const store = {};
  global.localStorage = { getItem: k => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = String(v); }, removeItem: k => { delete store[k]; } };
  if (g.save() !== true || !GameState.hasSave()) throw new Error('save() with storage failed');
  const loaded = GameState.load();
  if (!loaded || JSON.stringify(loaded) !== text || loaded.monthIndex !== g.monthIndex) throw new Error('load() mismatch');
  g.log('测试日志');
  if (g.log.length === 0 || g.log[g.log.length - 1].indexOf('测试日志') < 0) throw new Error('log(text) did not append');
  GameState.deleteSave();
  if (GameState.hasSave()) throw new Error('deleteSave() failed');
  delete global.localStorage;
  console.log(`save/load OK (${text.length} bytes, log ${g.log.length} entries)`);
} catch (e) {
  fails++;
  console.log('save/load FAILED: ' + (e.stack || e));
}

// ======================================================= (4) 必杀技数据 --
// (4)–(6) 按第二版语义测试专属必杀：关闭成长（只有专属必杀与原四计、不获得经验），(7) 再打开
SG.Progress.enabled = false;
console.log('== (4) specials data ==');
try {
  const S = SG.Specials;
  SG.G = null;
  const rep = S.check();
  console.log(`generals ${rep.total}: hand-made ${rep.hand}, generated ${rep.auto}; duplicate names ${rep.dupNames.length}, duplicate (kind, params) ${rep.dupSigs.length}, data problems ${rep.problems.length}`);
  for (const d of rep.dupNames) console.log('  dup name ' + d.name + ': ' + d.gens.join(', '));
  for (const d of rep.dupSigs) console.log('  dup params ' + d.sig + ': ' + d.gens.join(', '));
  for (const p of rep.problems) console.log('  problem: ' + p);
  if (!rep.ok) throw new Error('specials self-check failed');
  // 剧本中每位武将都有招式，且 kind 合法、desc 非空
  const roster = SG.ScenarioData.Generals.map(l => { const p = l.split('|'); return { name: p[0], war: +p[1], intel: +p[2], pol: +p[3] }; });
  for (const g of roster) {
    const sp = S.of(g);
    if (!sp || !S.KINDS[sp.kind] || !sp.name || !sp.desc || !/^#[0-9a-f]{6}$/.test(sp.color) || S.FX.indexOf(sp.fx) < 0) throw new Error('bad special for ' + g.name + ': ' + JSON.stringify(sp));
  }
  // 指定的知名武将使用手写招式
  const want = { '关羽': '青龙偃月斩', '张飞': '长坂怒吼', '赵云': '七进七出', '吕布': '天下无双', '诸葛亮': '借东风', '黄忠': '百步穿杨', '典韦': '双戟护主', '华佗': '麻沸散', '周瑜': '赤壁业火' };
  for (const k of Object.keys(want)) if (S.of({ name: k }).name !== want[k]) throw new Error(k + ' should have ' + want[k]);
  // 兜底生成器：确定性（同名同招）、名称含姓名、与名单内全部招式不撞车
  const synth = [];
  const cultures = ['han', 'wa', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian', 'nanman', 'yi', 'india', 'africa', 'moor'];
  const rnd = SG.SeededRandom(4242);
  const chars = '阿巴卡达伊法加哈米努奥帕萨塔乌瓦克雷图斯马尔罗尼娜莎提亚卑弥呼难升米乙素肖古王区连范师蔓';
  for (let i = 0; i < 160; i++) {
    let nm = '';
    const len = 2 + rnd.next(3);
    for (let j = 0; j < len; j++) nm += chars[rnd.next(chars.length)];
    nm += i;  // 保证测试名单内不重名
    synth.push({ name: nm, war: 20 + rnd.next(80), intel: 20 + rnd.next(80), pol: 20 + rnd.next(80), culture: cultures[i % cultures.length] });
  }
  const a1 = synth.map(g => JSON.stringify(S.fallback(g)));
  const a2 = synth.map(g => JSON.stringify(S.fallback(g)));
  if (a1.join() !== a2.join()) throw new Error('fallback generator is not deterministic');
  for (const g of synth) if (S.of(g).name.indexOf(g.name + '·') !== 0) throw new Error('fallback name should start with the general name: ' + S.of(g).name);
  const rep2 = S.check(roster.concat(synth));
  if (!rep2.ok) throw new Error('roster + 160 synthetic world generals: dup names ' + rep2.dupNames.length + ', dup params ' + rep2.dupSigs.map(d => d.gens.join('/')).join(' ') + ', problems ' + rep2.problems.length);
  const kinds = {};
  for (const sp of S.all(roster.concat(synth))) kinds[sp.kind] = (kinds[sp.kind] || 0) + 1;
  console.log(`+160 synthetic world generals: all unique; kinds ${Object.keys(kinds).sort().map(k => k + ':' + kinds[k]).join(' ')}`);
  for (const k of Object.keys(S.KINDS)) if (!kinds[k]) throw new Error('kind never produced: ' + k);
  // 校验器能发现常见错误
  const bad = S.validate({ name: '错·招', kind: 'smite', power: 9, wat: 1, color: 'red', fx: 'nope' }, '测试');
  if (bad.length < 5) throw new Error('validate() should report 5+ problems, got ' + JSON.stringify(bad));
  console.log('specials data OK');
} catch (e) {
  fails++;
  console.log('specials data FAILED: ' + (e.stack || e));
}

// ================================================ (5) useSpecial 压力测试 --
console.log('== (5) useSpecial fuzz + reference damage ratios ==');
try {
  const S = SG.Specials;
  SG.Random.seed(777);
  const g = SG.G = GameState.newGame('cao');
  const all = g.generals.slice();
  const fakeTarget = g.cities[11];
  let used = 0, checked = 0;
  const seenKinds = {};
  for (let round = 0; round < 6; round++) {
    for (let i = 0; i < all.length; i += 10) {
      const group = all.slice(i, i + 10);
      if (group.length < 2) continue;
      for (const x of group) { x.troops = Math.max(800, SG.maxTroops(x) - SG.Random.rangeInt(0, 2500)); x.training = 50 + SG.Random.rangeInt(0, 40); }
      const half = Math.ceil(group.length / 2);
      const s = { attacker: 0, defender: 1, src: g.cities[0], target: fakeTarget, atk: group.slice(0, half), def: group.slice(half), atkFood: 3000, atkGold: 0, routed: new Set(), captives: [] };
      const Mdl = new BattleModel(s);
      // 随机摆到一起（棋盘中部），随机士气 / 混乱 / 已行动
      const spots = [];
      for (let x = 1; x < 10; x++) for (let y = 1; y < Mdl.H - 1; y++) if (Mdl.passable(x, y)) spots.push({ x, y });
      for (let k = spots.length - 1; k > 0; k--) { const j = SG.Random.rangeInt(0, k + 1); const t = spots[k]; spots[k] = spots[j]; spots[j] = t; }
      Mdl.units.forEach((u, k) => { u.x = spots[k].x; u.y = spots[k].y; u.morale = 20 + SG.Random.rangeInt(0, 80); u.acted = SG.Random.value() < 0.4; });
      for (const u of SG.Seq.orderBy(Mdl.units, () => SG.Random.value())) {
        if (!u.alive || Mdl.result !== 0) continue;
        Mdl.side = u.side;
        const us = Mdl.specialUsable(u);
        const tg = Mdl.specialTargets(u);
        if (u.confused <= 0 && us.ok !== (tg.length > 0)) throw new Error('specialUsable disagrees with specialTargets for ' + u.gen.name + ': ' + us.why);
        if (!us.ok) continue;
        const t = tg[SG.Random.rangeInt(0, tg.length)];
        const plan = Mdl.specialPlan(u, t);
        const v = Mdl.specialValue(u, plan);
        if (!Number.isFinite(v) || v < 0) throw new Error('bad special value ' + v + ' for ' + u.gen.name);
        const res = Mdl.useSpecial(u, t);
        used++;
        seenKinds[res.kind] = (seenKinds[res.kind] || 0) + 1;
        checkSpecialResult(res, 'fuzz ' + u.gen.name);
        checkBattleState(Mdl, 'fuzz ' + u.gen.name + ' ' + res.name);
        if (!u.specialUsed || Mdl.specialUsable(u).ok) throw new Error('special usable twice: ' + u.gen.name);
        const again = Mdl.useSpecial(u, t);
        if (again.success || again.dmg !== 0) throw new Error('second useSpecial should do nothing');
        checked++;
      }
      // 数日推进：中毒、燃烧、加成到期（endSide 两次 = 一日：守方之后 endDay，双方行动开始时加成计时）
      for (let d = 0; d < 8 && Mdl.result === 0; d++) { Mdl.endSide(); checkBattleState(Mdl, 'fuzz endSide'); }
      if (Mdl.result === 0) for (const u of Mdl.units) if (u.alive && u.mods.some(m => !(m.dot > 0) && m.days > 0 && m.days < 4)) throw new Error('fuzz: short buff still active after 4 days on ' + u.gen.name);
    }
  }
  console.log(`fuzz: ${used} specials resolved, state checked after each; kinds ${Object.keys(seenKinds).sort().map(k => k + ':' + seenKinds[k]).join(' ')}`);
  if (Object.keys(seenKinds).length < 10) throw new Error('fuzz covered too few kinds');

  // 参考局面：施展者在平原，敌军在相邻、斜角、隔一格、同列隔一格（3500 兵、武 70、智 60，电脑阵型）；
  // 倍率 = 必杀技期望伤害（范围类最多计 2 支敌军；连斩计全部）÷ 普通一手（见 refAction）
  const dummy = i => ({ id: 9000 + i, name: '靶' + i, war: 70, intel: 60, pol: 50, faction: -1, troops: 3500, training: 60, loyalty: 80 });
  const byKind = {};
  const DAMAGE_KINDS = ['smite', 'cleave', 'charge', 'rampage', 'volley', 'blaze', 'storm', 'flood', 'drain'];
  const roster = SG.ScenarioData.Generals.map(l => l.split('|')[0]);
  for (const name of roster) {
    const gen = g.generals.find(x => x.name === name);
    const sp = S.of(gen);
    gen.troops = SG.maxTroops(gen); gen.training = 70;
    const s = { attacker: 0, defender: 1, src: g.cities[0], target: fakeTarget, atk: [gen], def: [dummy(1), dummy(2), dummy(3), dummy(4)], atkFood: 3000, atkGold: 0, routed: new Set(), captives: [] };
    const Mdl = new BattleModel(s);
    for (let x = 1; x <= 7; x++) for (let y = 1; y <= 7; y++) Mdl.map[x][y] = SG.Terrain.Plain;
    const [u, e1, e2, e3, e4] = Mdl.units;
    u.x = 4; u.y = 4; u.morale = 80;
    [[5, 4], [5, 5], [4, 6], [4, 2]].forEach((p, k) => { const e = [e1, e2, e3, e4][k]; e.x = p[0]; e.y = p[1]; e.morale = 70; });
    Mdl.side = 0;
    const normal = refAction(Mdl, u, e1, sp);
    let best = 0;
    for (const t of Mdl.specialTargets(u)) {
      const pl = Mdl.specialPlan(u, t);
      const per = new Map();
      for (const h of pl.dmg) per.set(h.unit, Math.min(h.unit.troops, (per.get(h.unit) || 0) + h.amt * (h.onFail && pl.kill ? 1 - pl.kill.p : 1)));
      let dmg = [...per.values()].sort((a, b) => b - a).slice(0, sp.kind === 'rampage' ? 99 : 2).reduce((a, b) => a + b, 0);
      if (pl.kill) dmg += pl.kill.p * pl.kill.unit.troops;
      best = Math.max(best, dmg);
    }
    if (best > 0) (byKind[sp.kind] = byKind[sp.kind] || []).push({ name, r: best / Math.max(1, normal) });
  }
  const line = [];
  let worst = null;
  for (const k of Object.keys(byKind).sort()) {
    const rs = byKind[k].map(o => o.r).sort((a, b) => a - b);
    line.push(`${k} ${rs[0].toFixed(1)}-${rs[rs.length - 1].toFixed(1)} (n${rs.length})`);
    if (DAMAGE_KINDS.indexOf(k) >= 0) for (const o of byKind[k]) if (!worst || Math.abs(Math.log(o.r / 2.1)) > Math.abs(Math.log(worst.r / 2.1))) worst = Object.assign({ k }, o);
  }
  console.log('reference damage / normal action, by kind (damage kinds should sit near 1.8-2.5; roar/scheme/poison/assassinate are mostly control): ' + line.join('; '));
  const famous = ['关羽', '吕布', '赵云', '黄忠', '孙策', '周瑜', '诸葛亮', '张飞'].map(n => {
    for (const k of Object.keys(byKind)) { const o = byKind[k].find(q => q.name === n); if (o) return n + ' ' + o.r.toFixed(2); }
    return n + ' -';
  });
  console.log('  hand-made: ' + famous.join(', ') + (worst ? `; furthest from 2.1: ${worst.name} (${worst.k}) ${worst.r.toFixed(2)}` : ''));
  for (const k of DAMAGE_KINDS) for (const o of byKind[k] || []) if (!(o.r > 1.2 && o.r < 3.0)) throw new Error('reference ratio out of range: ' + o.name + ' ' + k + ' ' + o.r.toFixed(2));
} catch (e) {
  fails++;
  console.log('useSpecial fuzz FAILED: ' + (e.stack || e));
}

// ======================================== (5b) 必杀技规则：攻守对称、据守本城、范围封顶 --
console.log('== (5b) specials rules: symmetric durations, castle hold, area cap, per-kind damage ==');
try {
  const S = SG.Specials;
  SG.Random.seed(91);
  const g = SG.G = GameState.newGame('cao');
  const byName = n => g.generals.find(x => x.name === n);
  const dummy = (i, war, intel) => ({ id: 9100 + i, name: '靶' + i, war: war || 70, intel: intel || 60, pol: 50, faction: -1, troops: 3500, training: 60, loyalty: 80 });
  // 平地小战场：caster 一方 + 若干敌我；casterSide = 施展者所在的一方
  function field(casterGen, casterSide, nFoes, nFriends) {
    casterGen.troops = SG.maxTroops(casterGen); casterGen.training = 70;
    const mine = [casterGen].concat(Array.from({ length: nFriends || 0 }, (_, i) => dummy(20 + i)));
    const theirs = Array.from({ length: nFoes }, (_, i) => dummy(i, 40, 30));
    const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: casterSide === 0 ? mine : theirs, def: casterSide === 0 ? theirs : mine, atkFood: 9000, atkGold: 0, routed: new Set(), captives: [] };
    const Mdl = new BattleModel(s);
    for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) Mdl.map[x][y] = SG.Terrain.Plain;
    Mdl.castle = P0(Mdl.W - 1, Mdl.H - 1);
    const u = Mdl.units.find(x => x.gen === casterGen);
    const foes = Mdl.units.filter(x => x.side !== casterSide), friends = Mdl.units.filter(x => x.side === casterSide && x !== u);
    u.x = 5; u.y = 5;
    [[6, 5], [5, 6], [4, 5], [5, 4]].slice(0, foes.length).forEach((p, k) => { foes[k].x = p[0]; foes[k].y = p[1]; });
    friends.forEach((a, k) => { a.x = 6 + k; a.y = 4; });
    for (const x of Mdl.units) { x.morale = 90; x.commander = false; }
    Mdl.startSide(casterSide);
    return { Mdl, u, foes, friends };
  }
  function P0(x, y) { return { x, y }; }
  // 推进到下一次 side 一方行动开始（返回时 Mdl.side === side）
  function nextPhase(Mdl, side) { do { Mdl.endSide(); } while (Mdl.side !== side && Mdl.result === 0); }
  const realValue = SG.Random.value;
  const lines = [];
  // (a) 混乱：无论攻方或守方施展，「混乱 N 日」都让目标恰好失去 N 次本方行动
  const confusers = [S.all().find(sp => sp.kind === 'roar' && sp.turns === 1 && sp.confuse > 0), S.all().find(sp => sp.kind === 'scheme' && sp.turns === 2)];
  for (const sp of confusers) for (const side of [0, 1]) {
    const { Mdl, u, foes } = field(byName(sp.gen), side, 2);
    const t = S.needsTarget(sp) ? foes[0] : u;
    SG.Random.value = () => 0;            // 必定成功
    let res;
    try { res = Mdl.useSpecial(u, t); } finally { SG.Random.value = realValue; }
    if (res.confused.length === 0) throw new Error(sp.name + ' confused nobody');
    const e = res.confused[0];
    let lost = 0;
    for (let k = 0; k < 4; k++) { nextPhase(Mdl, e.side); if (!Mdl.canAct(e)) lost++; }
    lines.push(`${sp.gen}「${sp.name}」混乱 ${sp.turns} 日 by side ${side}: lost ${lost}`);
    if (lost !== sp.turns) throw new Error(`${sp.name} (turns ${sp.turns}) cast by side ${side}: target lost ${lost} phases`);
  }
  // (b) 加成：持续 N 日 = 覆盖敌方 N 次行动（以及本方之后 N−1 次行动），攻守相同
  for (const name of ['法正', '曹洪', '典韦', '曹操']) {
    const sp = S.of(byName(name));
    for (const side of [0, 1]) {
      const { Mdl, u, friends } = field(byName(name), side, 1, 1);
      const res = Mdl.useSpecial(u, u);
      if (res.buffed.length === 0) throw new Error(name + ' buffed nobody');
      const a = res.buffed[0];
      const key = sp.def > 1 ? 'def' : 'atk';
      let enemy = 0, own = 0;
      for (let k = 0; k < 4; k++) {
        nextPhase(Mdl, 1 - side); if (Mdl.modK(a, key) > 1) enemy++;
        nextPhase(Mdl, side); if (Mdl.modK(a, key) > 1) own++;
      }
      lines.push(`${name}「${sp.name}」${key} ${sp.turns} 日 by side ${side}: enemy phases ${enemy}, later own phases ${own}`);
      if (enemy !== sp.turns || own !== sp.turns - 1) throw new Error(`${name} (${key}, turns ${sp.turns}) by side ${side}: covers ${enemy} enemy / ${own} own phases`);
    }
  }
  // (c) 中毒：发作次数 = 持续日数，攻守相同
  {
    const sp = S.of(byName('李儒'));
    for (const side of [0, 1]) {
      const { Mdl, u, foes } = field(byName('李儒'), side, 1);
      foes[0].x = 7; foes[0].y = 5;
      Mdl.useSpecial(u, foes[0]);
      const e = foes[0];
      let ticks = 0;
      for (let k = 0; k < 6 && e.alive; k++) { const before = e.troops; nextPhase(Mdl, side); if (e.troops < before) ticks++; }
      lines.push(`李儒「${sp.name}」中毒 ${sp.turns} 日 by side ${side}: ticks ${ticks}`);
      if (ticks !== sp.turns) throw new Error(`poison by side ${side} ticked ${ticks} times (turns ${sp.turns})`);
    }
  }
  console.log('  ' + lines.join('\n  '));
  // (d) 突击不能把本城 / 城门上的部队撞走（守将被撞出本城后，攻方踏入即破城）
  {
    let n = 0;
    // 只测本局（经典剧本）中的武将；世界剧本新增武将的条目也在 S.all() 里，但不在 g.generals 中
    for (const sp of S.all().filter(q => q.kind === 'charge' && q.push && byName(q.gen))) {
      for (const tile of [SG.Terrain.Castle, SG.Terrain.Gate]) {
        const { Mdl, u, foes } = field(byName(sp.gen), 0, 1);
        const t = foes[0];
        t.x = 9; t.y = 5; u.x = 9 - sp.range; u.y = 5;
        Mdl.map[t.x][t.y] = tile; if (tile === SG.Terrain.Castle) Mdl.castle = P0(t.x, t.y);
        t.commander = tile === SG.Terrain.Castle;
        const pl = Mdl.specialPlan(u, t);
        if (pl.push || pl.blocked !== t) throw new Error(sp.gen + ' charge would push a unit off ' + (tile === SG.Terrain.Castle ? '本城' : '城门'));
        Mdl.useSpecial(u, t);
        if (t.alive && (t.x !== 9 || t.y !== 5)) throw new Error(sp.gen + ' charge moved the defender off its tile');
        n++;
      }
    }
    console.log(`  charge vs 本城 / 城门: ${n} cases, defender never pushed off`);
  }
  // (e) 每位手写武将的伤害倍率：单体（只有一支敌军相邻 / 在射程内）与密集（主目标周围再有 3 支敌军）
  const DAMAGE_KINDS = ['smite', 'cleave', 'charge', 'rampage', 'volley', 'blaze', 'storm', 'flood', 'drain'];
  const roster = SG.ScenarioData.Generals.map(l => l.split('|')[0]);
  const single = {}, cluster = {};
  let worstCluster = null;
  for (const name of roster) {
    const gen = byName(name);
    const sp = S.of(gen);
    if (DAMAGE_KINDS.indexOf(sp.kind) < 0) continue;
    for (const mode of ['single', 'cluster']) {
      const { Mdl, u, foes } = field(gen, 0, mode === 'single' ? 1 : 4);
      const t = foes[0];
      const r = sp.kind === 'cleave' || sp.kind === 'rampage' || sp.kind === 'drain' || sp.kind === 'smite' ? 1 : Math.max(1, Math.min(sp.range, 3));
      t.x = 5 + r; t.y = 5;
      if (mode === 'cluster') {
        const spots = sp.kind === 'cleave' || sp.kind === 'rampage' ? [[5, 6], [4, 5], [5, 4]] : [[t.x, 6], [t.x, 4], [t.x + 1, 5]];
        foes.slice(1).forEach((e, k) => { e.x = spots[k][0]; e.y = spots[k][1]; });
      }
      if (Mdl.specialTargets(u).indexOf(t) < 0) continue;
      const pl = Mdl.specialPlan(u, t);
      const per = new Map();
      for (const h of pl.dmg) per.set(h.unit, Math.min(h.unit.troops, (per.get(h.unit) || 0) + h.amt));
      let tot = [...per.values()].reduce((a, b) => a + b, 0);
      if (pl.drain > 0) for (const h of pl.dmg) if (h.primary) tot += Math.min(h.unit.troops, h.amt) * pl.drain;   // 收编：伤敌 + 己方增兵
      const x = tot / Math.max(1, refAction(Mdl, u, t, sp));
      ((mode === 'single' ? single : cluster)[sp.kind] = (mode === 'single' ? single : cluster)[sp.kind] || []).push({ name, x });
      if (mode === 'cluster' && (!worstCluster || x > worstCluster.x)) worstCluster = { name, kind: sp.kind, x };
    }
  }
  const med = a => { const b = a.slice().sort((p, q) => p - q); return b[b.length >> 1]; };
  const fmtK = o => Object.keys(o).sort().map(k => { const xs = o[k].map(q => q.x); return `${k} ${med(xs).toFixed(2)} [${Math.min(...xs).toFixed(2)}-${Math.max(...xs).toFixed(2)}]`; }).join(', ');
  console.log('  single target, median [min-max] of special / normal action (drain counts troops gained): ' + fmtK(single));
  console.log('  4 clustered foes, total: ' + fmtK(cluster) + `; highest ${worstCluster.name} (${worstCluster.kind}) ${worstCluster.x.toFixed(2)}`);
  // 单体：主打单体的机制中位数落在 1.6–2.6；范围类单体可低些（溅射类 ≥ 0.9，天候 / 水攻这类纯范围招式 ≥ 0.5，靠多目标取胜）
  for (const k of Object.keys(single)) {
    const m = med(single[k].map(q => q.x));
    const lo = ['smite', 'charge', 'rampage', 'drain'].indexOf(k) >= 0 ? 1.6 : k === 'storm' || k === 'flood' ? 0.5 : 0.9;
    if (!(m >= lo && m <= 2.6)) throw new Error(`single-target median for ${k} = ${m.toFixed(2)} (want ${lo}-2.6)`);
    for (const o of single[k]) if (!(o.x <= 3.0)) throw new Error(`single-target ${o.name} (${k}) = ${o.x.toFixed(2)} > 3.0`);
  }
  // 密集：范围封顶后任何人都不超过 3.2 倍（智力系以火计期望为基准，略高于武力系）
  for (const k of Object.keys(cluster)) for (const o of cluster[k]) if (!(o.x <= 3.2)) throw new Error(`clustered ${o.name} (${k}) = ${o.x.toFixed(2)} > 3.2`);
  console.log('specials rules OK');
} catch (e) {
  fails++;
  console.log('specials rules FAILED: ' + (e.stack || e));
}

// ======================================== (5c) 世界剧本武将的必杀技 --
// 载入 world-data.js（只定义 SG.Scenarios / SG.WorldInfo 等，不改 SG.ScenarioData），对经典 + 世界全体名单自检：
// 名称与 (机制, 参数) 唯一、每位新增武将都有手写招式（不走兜底）、名称 2–5 字；
// 再按 (5) 的参考局面计算新增武将伤害类招式的倍率（同样须在 1.2–3.0 之间）。
console.log('== (5c) world scenario generals: hand-made, unique, reference damage ratios ==');
try {
  const S = SG.Specials;
  if (!SG.Scenarios) vm.runInThisContext(fs.readFileSync(path.join(root, 'world-data.js'), 'utf8'), { filename: 'world-data.js' });
  const classicNames = new Set(SG.ScenarioData.Generals.map(l => l.split('|')[0]));
  const wroster = SG.Scenarios.world.Generals.map(l => { const p = l.split('|'); return { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], culture: p[9] || 'han' }; });
  SG.G = null;
  const rep = S.check(wroster);
  console.log(`world roster ${wroster.length}: specials ${rep.total}, hand-made ${rep.hand}, generated ${rep.auto}; duplicate names ${rep.dupNames.length}, duplicate (kind, params) ${rep.dupSigs.length}, data problems ${rep.problems.length}`);
  for (const d of rep.dupNames) console.log('  dup name ' + d.name + ': ' + d.gens.join(', '));
  for (const d of rep.dupSigs) console.log('  dup params ' + d.sig + ': ' + d.gens.join(', '));
  for (const p of rep.problems) console.log('  problem: ' + p);
  if (!rep.ok) throw new Error('world specials self-check failed');
  const fresh = wroster.filter(g => !classicNames.has(g.name));
  const autoW = fresh.filter(g => S.of(g).auto).map(g => g.name);
  if (autoW.length) throw new Error('world generals without a hand-made special: ' + autoW.join(' '));
  for (const g of fresh) { const n = S.of(g).name; if (n.length < 2 || n.length > 5) throw new Error('special name should be 2-5 chars: ' + g.name + ' ' + n); }
  const kinds = {};
  for (const g of fresh) kinds[S.of(g).kind] = (kinds[S.of(g).kind] || 0) + 1;
  console.log(`${fresh.length} new world generals, all hand-made; kinds ${Object.keys(kinds).sort().map(k => k + ':' + kinds[k]).join(' ')}`);
  // 参考伤害倍率（与 (5) 相同的局面；武将以独立对象代入）
  SG.Random.seed(778);
  const g = SG.G = GameState.newGame('cao');
  const dummy = i => ({ id: 9000 + i, name: '靶' + i, war: 70, intel: 60, pol: 50, faction: -1, troops: 3500, training: 60, loyalty: 80 });
  const DAMAGE_KINDS = ['smite', 'cleave', 'charge', 'rampage', 'volley', 'blaze', 'storm', 'flood', 'drain'];
  const byKind = {};
  fresh.forEach((w, i) => {
    const gen = { id: 9600 + i, name: w.name, war: w.war, intel: w.intel, pol: w.pol, faction: -1, troops: 0, training: 70, loyalty: 90, culture: w.culture };
    gen.troops = SG.maxTroops(gen);
    const sp = S.of(gen);
    if (DAMAGE_KINDS.indexOf(sp.kind) < 0) return;
    const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: [gen], def: [dummy(1), dummy(2), dummy(3), dummy(4)], atkFood: 3000, atkGold: 0, routed: new Set(), captives: [] };
    const Mdl = new BattleModel(s);
    for (let x = 1; x <= 7; x++) for (let y = 1; y <= 7; y++) Mdl.map[x][y] = SG.Terrain.Plain;
    const [u, e1, e2, e3, e4] = Mdl.units;
    u.x = 4; u.y = 4; u.morale = 80;
    [[5, 4], [5, 5], [4, 6], [4, 2]].forEach((p, k) => { const e = [e1, e2, e3, e4][k]; e.x = p[0]; e.y = p[1]; e.morale = 70; });
    Mdl.side = 0;
    const normal = refAction(Mdl, u, e1, sp);
    let best = 0;
    for (const t of Mdl.specialTargets(u)) {
      const pl = Mdl.specialPlan(u, t);
      const per = new Map();
      for (const h of pl.dmg) per.set(h.unit, Math.min(h.unit.troops, (per.get(h.unit) || 0) + h.amt * (h.onFail && pl.kill ? 1 - pl.kill.p : 1)));
      best = Math.max(best, [...per.values()].sort((a, b) => b - a).slice(0, sp.kind === 'rampage' ? 99 : 2).reduce((a, b) => a + b, 0));
    }
    (byKind[sp.kind] = byKind[sp.kind] || []).push({ name: w.name, r: best / Math.max(1, normal) });
  });
  console.log('world reference damage / normal action: ' + Object.keys(byKind).sort().map(k => {
    const rs = byKind[k].map(o => o.r).sort((a, b) => a - b);
    return `${k} ${rs[0].toFixed(1)}-${rs[rs.length - 1].toFixed(1)} (n${rs.length})`;
  }).join('; '));
  for (const k of Object.keys(byKind)) for (const o of byKind[k]) if (!(o.r > 1.2 && o.r < 3.0)) throw new Error('world reference ratio out of range: ' + o.name + ' ' + k + ' ' + o.r.toFixed(2));
  console.log('world specials OK');
} catch (e) {
  fails++;
  console.log('world specials FAILED: ' + (e.stack || e));
}

// ============================================== (6) duel() 拆分的随机数顺序 --
console.log('== (6) duel split keeps the random sequence ==');
try {
  // 第一版 duel() 的原样副本：与拆分后的 duel() 在相同随机数下结果必须完全一致
  function duelV1(Mdl, a, b) {
    const M = SG.M;
    const r = { accepted: false, rounds: [], winner: 0 };
    r.accepted = b.gen.war >= a.gen.war - 8 || SG.Random.value() < 0.25 || (b.commander && SG.Random.value() < 0.4);
    if (!r.accepted) { b.morale = Math.max(0, b.morale - 12); a.morale = Math.min(100, a.morale + 8); return r; }
    let hpA = 100, hpB = 100;
    for (let i = 0; i < 30 && hpA > 0 && hpB > 0; i++) {
      const who = SG.Random.value() < a.gen.war / (a.gen.war + b.gen.war) ? 0 : 1;
      const striker = who === 0 ? a : b;
      const dmg = M.roundToInt(SG.Random.rangeInt(6, 16) * (0.6 + striker.gen.war / 120));
      if (who === 0) hpB = Math.max(0, hpB - dmg); else hpA = Math.max(0, hpA - dmg);
      r.rounds.push({ who, dmg, hpA, hpB });
    }
    r.winner = hpA >= hpB ? 0 : 1;
    const loser = r.winner === 0 ? b : a;
    const winner = r.winner === 0 ? a : b;
    loser.troops = 0; loser.alive = false; Mdl.setup.routed.add(loser.gen.id);
    for (const o of Mdl.alive(loser.side)) o.morale = Math.max(0, o.morale - 8);
    winner.morale = Math.min(100, winner.morale + 15);
    Mdl.checkEnd();
    return r;
  }
  let n = 0;
  for (let seed = 1; seed <= 60; seed++) {
    const pr = pairs[seed % pairs.length];
    const snap = [];
    for (const v of [0, 1]) {
      SG.Random.seed(seed * 31);
      const g = SG.G = GameState.newGame('cao');
      const src = g.cityByKey(pr[0]); const tgt = g.cityByKey(pr[1]);
      const s = Conquest.prepare(src.owner, src, tgt, g.officersIn(src).filter(x => x.troops > 0).slice(0, 5), 3000, 0);
      const Mdl = new BattleModel(s);
      const a = Mdl.units[seed % Mdl.alive(0).length], b = Mdl.alive(1)[seed % Mdl.alive(1).length];
      const r = v === 0 ? duelV1(Mdl, a, b) : Mdl.duel(a, b);
      snap.push(JSON.stringify({ r, next: SG.Random.value(), units: Mdl.units.map(u => [u.troops, u.morale, u.alive]), result: Mdl.result, routed: [...s.routed] }));
    }
    if (snap[0] !== snap[1]) throw new Error('duel differs at seed ' + seed + '\n' + snap[0] + '\n' + snap[1]);
    n++;
  }
  // duelAccepts + duelFinish 组合与 duel() 的后果一致
  SG.Random.seed(5);
  const g = SG.G = GameState.newGame('cao');
  const src = g.cityByKey('chenliu'); const tgt = g.cityByKey('luoyang');
  const s = Conquest.prepare(src.owner, src, tgt, g.officersIn(src).filter(x => x.troops > 0).slice(0, 5), 3000, 0);
  const Mdl = new BattleModel(s);
  const a = Mdl.alive(0)[1], b = Mdl.alive(1)[1];
  if (Mdl.duelAccepts(a, b) !== true && b.gen.war >= a.gen.war - 8) throw new Error('duelAccepts should accept equal opponents');
  const fin = Mdl.duelFinish(a, b, true, [{ who: 0, dmg: 100, hpA: 100, hpB: 0 }]);
  if (b.alive || !a.alive || fin.winner !== 0 || !s.routed.has(b.gen.id)) throw new Error('duelFinish did not resolve the duel');
  console.log(`duel split OK (${n} seeds identical to v1)`);
} catch (e) {
  fails++;
  console.log('duel split FAILED: ' + (e.stack || e));
}

// ============================== (7) 第三版成长：经验、等级、可习得必杀与计策、存档 v3、平衡 --
// （第 (4)–(6) 节按第二版语义测试专属必杀，期间 SG.Progress.enabled = false；本节重新打开）
SG.Progress.enabled = true;
BattleModel.specialsEnabled = true;
const Pg = SG.Progress, Tc = SG.Tactics;
const DMG_KINDS = ['smite', 'cleave', 'charge', 'rampage', 'volley', 'drain'];
// 电脑对电脑打完一场（与 runBattle 同样的行动顺序）；st 收集统计，check = true 时每一手后检查状态
function fightAI(Mdl, st, check) {
  let guard = 0;
  while (Mdl.result === 0 && guard++ < 2000) {
    for (let pass = 0; pass < 4 && Mdl.result === 0; pass++) {
      const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u));
      if (order.length === 0) break;
      for (const u of order) {
        if (!Mdl.canAct(u) || Mdl.result !== 0) continue;
        const plan = Mdl.planFor(u);
        if (plan.move) Mdl.move(u, plan.move);
        if (Mdl.result !== 0) break;
        const act = Mdl.planAction(u);
        if (act.kind === 'attack') { const r = Mdl.attack(u, act.target); st.atk.push(r.dmg); st.dmg += r.dmg + r.counter; }
        else if (act.kind === 'tactic') {
          const isNew = Tc.isNew(act.tactic);
          const r = Mdl.useTactic(u, act.tactic, act.target);
          st.dmg += r.dmg || 0;
          if (isNew) { st.newTac++; st.byTac[act.tactic.id] = (st.byTac[act.tactic.id] || 0) + 1; if (check) checkSpecialResult(r, 'tactic ' + act.tactic.name); }
          else st.oldTac++;
        } else if (act.kind === 'duel') Mdl.duel(u, act.target);
        else if (act.kind === 'special') {
          const sp = Mdl.specialOf(u);
          if (act.special && sp !== act.special) throw new Error('planned special ' + act.special.name + ' but default resolves to ' + sp.name);
          const tg = act.target;
          const ref = tg && tg.side !== u.side ? refAction(Mdl, u, tg, sp) : 0;
          const r = Mdl.useSpecial(u, act.target);
          if (!r.sp || u.spUsed.indexOf(sp.id) < 0) throw new Error('planned special not used: ' + u.gen.name + ' ' + r.why);
          st.dmg += r.dmg;
          if (sp.pool) {
            st.pool++;
            st.poolTier[sp.tier] = (st.poolTier[sp.tier] || 0) + 1;
            if (ref > 0 && r.dmg > 0 && DMG_KINDS.indexOf(sp.kind) >= 0) (st.poolRatio[sp.tier] = st.poolRatio[sp.tier] || []).push(r.dmg / ref);
            if (ref > 0 && sp.kind === 'assassinate') { st.assn.push(r.dmg / ref); if (r.killed) st.assnKill++; }
          } else st.sig++;
          if (check) checkSpecialResult(r, 'special ' + sp.name);
        }
        if (check) checkBattleState(Mdl, 'fightAI ' + u.gen.name);
        Mdl.spend(u);
      }
    }
    if (Mdl.result === 0) Mdl.endSide();
  }
  if (Mdl.result === 0) throw new Error('battle never ended');
}
function newStats() { return { n: 0, w: 0, days: 0, dmg: 0, atk: [], oldTac: 0, newTac: 0, byTac: {}, sig: 0, pool: 0, poolTier: {}, poolRatio: {}, assn: [], assnKill: 0, outcomes: [] }; }
function levelHist(gens) { const h = {}; for (const x of gens) h[x.level] = (h[x.level] || 0) + 1; return Object.keys(h).sort((a, b) => a - b).map(k => 'L' + k + ':' + h[k]).join(' '); }
// 习得的招式不超出格数与等阶上限
function checkCaps(x, where) {
  const ss = Pg.specialSlots(x), ts = Pg.tacticSlots(x);
  if (x.specials.length > ss.max) throw new Error(where + ': ' + x.name + ' knows ' + x.specials.length + ' learned specials, slots ' + ss.max);
  x.specials.forEach((id, k) => { const sp = SG.Specials.byId(id); if (!sp || sp.tier > ss.statTier || sp.tier > k + 1) throw new Error(where + ': ' + x.name + ' (war ' + x.war + ') has ' + id + ' beyond tier cap (slot ' + (k + 1) + ')'); });
  if (x.specials.filter(id => SG.Specials.byId(id).kind === 'assassinate').length > 1) throw new Error(where + ': ' + x.name + ' knows more than one pool assassinate');
  const lt = Pg.learnedTactics(x);
  if (lt.length > ts.max) throw new Error(where + ': ' + x.name + ' knows ' + lt.length + ' learned tactics, slots ' + ts.max);
  lt.forEach((id, k) => { const t = Tc.byId(id); if (!t || t.tier > ts.statTier || t.tier > k + 1 || x.intel < t.reqInt) throw new Error(where + ': ' + x.name + ' (intel ' + x.intel + ') has tactic ' + id + ' beyond cap (slot ' + (k + 1) + ')'); });
  if (!(x.level >= 1 && x.level <= Pg.MAX_LEVEL) || x.level !== Pg.levelFor(x.exp)) throw new Error(where + ': bad level ' + x.name + ' ' + x.level + ' exp ' + x.exp);
}

// ---- (7a) 规则单元：等级表、格数与等阶、候选、习得、电脑自动习得 ----
try {
  SG.Random.seed(3);
  const g = SG.G = GameState.newGame('cao');
  for (let L = 2; L <= Pg.MAX_LEVEL; L++) {
    if (!(Pg.EXP[L] > Pg.EXP[L - 1])) throw new Error('EXP table not increasing at ' + L);
    if (Pg.levelFor(Pg.EXP[L]) !== L || Pg.levelFor(Pg.EXP[L] - 1) !== L - 1) throw new Error('levelFor wrong at ' + L);
  }
  let n = 0;
  const mk = (war, intel, lv, faction) => {
    const x = { id: 99000 + n, name: '测试' + (n++), war, intel, pol: 50, faction: faction === undefined ? -1 : faction, city: 0, troops: 3000, training: 60, loyalty: 80 };
    Pg.ensure(x); x.exp = Pg.EXP[lv || 1]; Pg.ensure(x);
    return x;
  };
  const innate = { 39: '', 40: 'inspire', 55: 'fire,inspire', 65: 'fire,rockfall,inspire', 75: 'fire,rockfall,confuse,inspire' };
  for (const k of Object.keys(innate)) if (mk(50, +k).tactics.join() !== innate[k]) throw new Error('innate tactics for intel ' + k + ': ' + mk(50, +k).tactics.join());
  // [能力, 等级, 格数, 能力等阶上限, 等级等阶上限, 已解锁格数]；第 k 格最高 k 阶，故未学时 tier = min(…, 1)
  const slotCase = [[59, 10, 0, 0, 4, 0], [60, 10, 1, 1, 4, 1], [75, 10, 2, 2, 4, 2], [85, 10, 3, 3, 4, 3], [91, 10, 4, 3, 4, 4], [92, 10, 4, 4, 4, 4], [100, 1, 4, 4, 0, 0], [100, 2, 4, 4, 1, 1], [100, 5, 4, 4, 2, 2], [100, 8, 4, 4, 4, 4]];
  for (const [war, lv, max, st, lt, unl] of slotCase) {
    const s = Pg.specialSlots(mk(war, 30, lv));
    if (s.max !== max || s.statTier !== st || s.levelTier !== lt || s.unlocked !== unl || s.tier !== Math.min(st, lt, 1)) throw new Error(`special slots war ${war} L${lv}: ${JSON.stringify(s)}`);
  }
  const tacCase = [[49, 10, 0, 0, 4, 0], [50, 10, 1, 1, 4, 1], [64, 10, 2, 1, 4, 2], [65, 10, 2, 2, 4, 2], [80, 10, 4, 3, 4, 4], [95, 10, 5, 4, 4, 5], [95, 2, 5, 4, 0, 0], [95, 3, 5, 4, 1, 1], [95, 9, 5, 4, 4, 4]];
  for (const [intel, lv, max, st, lt, unl] of tacCase) {
    const s = Pg.tacticSlots(mk(30, intel, lv));
    if (s.max !== max || s.statTier !== st || s.levelTier !== lt || s.unlocked !== unl || s.tier !== Math.min(st, lt, 1)) throw new Error(`tactic slots intel ${intel} L${lv}: ${JSON.stringify(s)}`);
  }
  // 候选：确定、至多 3 个、全部合格；两类都有空格时两类都出现
  const rnd = SG.SeededRandom(77);
  let mixed = 0, total = 0;
  for (let i = 0; i < 300; i++) {
    const x = mk(40 + rnd.next(61), 40 + rnd.next(61), 1 + rnd.next(10));
    for (let step = 0; step < 6; step++) {
      const c1 = Pg.candidates(x), c2 = Pg.candidates(x);
      if (JSON.stringify(c1.map(c => c.id)) !== JSON.stringify(c2.map(c => c.id))) throw new Error('candidates not deterministic');
      const op = Pg.openings(x);
      if (c1.length > 3) throw new Error('more than 3 candidates');
      if (op.total > 0 && c1.length === 0 && (Pg.eligible(x, 'special').length || Pg.eligible(x, 'tactic').length)) throw new Error('openings but no candidates');
      for (const c of c1) {
        const cap = c.type === 'special' ? Pg.specialSlots(x).tier : Pg.tacticSlots(x).tier;
        if (c.tier > cap) throw new Error('candidate above tier cap: ' + c.name);
        if (!Pg.eligible(x, c.type).some(e => e.id === c.id)) throw new Error('ineligible candidate ' + c.id);
        if (!c.name || !c.desc || !c.rules || !/^#[0-9a-f]{6}$/i.test(c.color)) throw new Error('candidate lacks display fields: ' + JSON.stringify(c));
      }
      for (const type of ['special', 'tactic']) {
        const cap = type === 'special' ? Pg.specialSlots(x).tier : Pg.tacticSlots(x).tier;
        if (Pg.eligible(x, type).some(e => e.tier === cap) && !c1.some(c => c.type === type && c.tier === cap)) throw new Error('offer lacks a top-tier ' + type + ' (tier ' + cap + ') for ' + x.name);
      }
      if (op.special > 0 && op.tactic > 0 && Pg.eligible(x, 'special').length && Pg.eligible(x, 'tactic').length) {
        total++;
        if (c1.some(c => c.type === 'special') && c1.some(c => c.type === 'tactic')) mixed++;
      }
      if (!c1.length) break;
      const pick = c1[rnd.next(c1.length)];
      if (!Pg.learn(x, pick)) throw new Error('learn failed for an offered candidate');
      if (Pg.learn(x, pick)) throw new Error('learned the same entry twice');
    }
    checkCaps(x, '7a');
  }
  if (total && mixed !== total) throw new Error(`mixed openings offered both kinds only ${mixed}/${total}`);
  // 不合格的习得一律拒绝
  const low = mk(65, 55, 10);
  const t4 = SG.Specials.pool().find(sp => sp.tier === 4), tt4 = Tc.learnable().find(t => t.tier === 4);
  if (Pg.learn(low, 'special', t4.id) || Pg.learn(low, 'tactic', tt4.id) || Pg.learn(low, 'tactic', 'fire') || Pg.learn(low, 'special', 'lp:nope')) throw new Error('learn() accepted an ineligible entry');
  // 电脑自动习得；玩家武将：playerAutoLearn = false（升级对话框上线后）保留待选，缺省 true 时与电脑一样自动习得
  if (Pg.playerAutoLearn !== true) throw new Error('playerAutoLearn should default to true until the level-up dialog ships');
  const ai = mk(95, 95, 1, 3), me = mk(95, 95, 1, g.player), me2 = mk(95, 95, 1, g.player);
  Pg.playerAutoLearn = false;
  let ra, rm, rm2;
  try { ra = Pg.gain(ai, Pg.EXP[10]); rm = Pg.gain(me, Pg.EXP[10]); } finally { Pg.playerAutoLearn = true; }
  rm2 = Pg.gain(me2, Pg.EXP[10]);
  if (ai.level !== 10 || me.level !== 10 || ai.exp !== Pg.EXP[10]) throw new Error('gain did not reach level 10');
  if (Pg.openings(ai).total !== 0 || ra.learned.length !== 9 || ai.specials.length !== 4 || Pg.learnedTactics(ai).length !== 5) throw new Error('AI auto-learn should fill 4 + 5 slots: ' + JSON.stringify(Pg.openings(ai)) + ' learned ' + ra.learned.length);
  if (rm.learned.length !== 0 || Pg.openings(me).total !== 9) throw new Error('player general should keep openings for the level-up dialog (playerAutoLearn false)');
  if (rm2.learned.length !== 9 || Pg.openings(me2).total !== 0) throw new Error('player general should auto-learn while playerAutoLearn is on: ' + rm2.learned.length);
  g.generals.push(me, me2);
  if (Pg.pending(g.player).indexOf(me) < 0 || Pg.pending(g.player).indexOf(me2) >= 0) throw new Error('pending() wrong for the player generals');
  g.generals.pop(); g.generals.pop();
  // openings 只在有可学计策（载入 tactics-data.js）时计计策格，否则候选为空时 total 不会停在 > 0
  {
    const lone = mk(40, 95, 1, g.player);
    lone.exp = Pg.EXP[10]; Pg.ensure(lone);
    const Tsave = SG.Tactics;
    SG.Tactics = undefined;
    try { if (Pg.openings(lone).total !== 0 || Pg.candidates(lone).length !== 0) throw new Error('openings without tactics-data.js: ' + JSON.stringify(Pg.openings(lone))); }
    finally { SG.Tactics = Tsave; }
    if (Pg.openings(lone).tactic !== 5 || Pg.candidates(lone).length === 0) throw new Error('openings with tactics-data.js: ' + JSON.stringify(Pg.openings(lone)));
  }
  const tiers = ai.specials.map(id => SG.Specials.byId(id).tier).join('');
  if (tiers !== '1234') throw new Error('slot k should take tier k for a war-95 general: ' + tiers);
  // ensure 幂等
  const before = JSON.stringify(ai); Pg.ensure(ai); if (JSON.stringify(ai) !== before) throw new Error('ensure() not idempotent');
  // 自动结算（Conquest.autoResolve 已由 battle-model.js 包装）：双方都得经验，结果在 s.progress，不重复计
  {
    const src = g.cityByKey('chenliu'), tgt = g.cityByKey('luoyang');
    const s = Conquest.prepare(src.owner, src, tgt, g.officersIn(src).filter(x => x.troops > 0).slice(0, 5), 3000, 0);
    const exp0 = s.atk.concat(s.def).map(x => x.exp);
    const rnd0 = SG.Random.value(); SG.Random.seed(3);
    Conquest.autoResolve(s);
    const got = s.atk.concat(s.def).map((x, i) => x.exp - exp0[i]);
    if (!Conquest._progressHook || !s.progressDone || !Array.isArray(s.progress) || s.progress.length !== s.atk.length + s.def.length) throw new Error('autoResolve did not award experience: ' + JSON.stringify(s.progress && s.progress.length));
    if (got.some(v => !(v >= 10))) throw new Error('every participant should gain exp: ' + got.join(','));
    const exp1 = s.atk.concat(s.def).map(x => x.exp);
    Pg.autoBattle(s, Pg.snapshot(s));
    if (s.atk.concat(s.def).some((x, i) => x.exp !== exp1[i])) throw new Error('autoBattle counted the same battle twice');
    void rnd0;
  }
  console.log(`(7a) rules OK: EXP ${Pg.EXP.slice(1).join('/')}; AI L10 (95/95) learned specials tiers ${tiers} + ${Pg.learnedTactics(ai).map(id => Tc.byId(id).name).join('、')}; mixed offers ${mixed}/${total}`);
} catch (e) {
  fails++;
  console.log('(7a) progression rules FAILED: ' + (e.stack || e));
}

// ---- (7b) 存档（第四版：版本 4；v3 / v2 存档只读、读取时迁移）与第二版存档迁移 ----
try {
  SG.Random.seed(17);
  const g = SG.G = GameState.newGame('cao');
  for (let i = 0; i < 30; i++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
  const grown = g.generals.filter(x => x.level > 1);
  if (grown.length < 10) throw new Error('only ' + grown.length + ' generals leveled after 30 AI months');
  const text = JSON.stringify(g);
  const d0 = JSON.parse(text);
  if (d0.version !== GameState.SAVE_VERSION || GameState.SAVE_VERSION !== 4) throw new Error('save version should be 4');
  const back = GameState.fromJSON(text);
  if (JSON.stringify(back) !== text) throw new Error('v4 JSON round trip mismatch');
  if (back.migratedFrom !== 0) throw new Error('v4 save flagged as migrated');
  // 第三版存档（只差版本号）：读取时迁移
  const d3 = JSON.parse(text); d3.version = 3;
  const m3 = GameState.fromJSON(JSON.stringify(d3));
  if (m3.migratedFrom !== 3 || JSON.stringify(m3) !== text) throw new Error('v3 save should migrate to the same v4 game');
  // 第二版存档：去掉成长字段、version 2
  const d = JSON.parse(text);
  d.version = 2;
  for (const x of d.generals) { delete x.exp; delete x.level; delete x.specials; delete x.tactics; delete x.record; }
  const v2text = JSON.stringify(d);
  const m = GameState.fromJSON(v2text);
  if (m.migratedFrom !== 2) throw new Error('v2 save not flagged as migrated');
  for (const x of m.generals) {
    if (x.level !== 1 || x.exp !== 0 || x.specials.length !== 0 || x.tactics.join() !== Pg.innateTactics(x).join() || x.record.battles !== 0) throw new Error('v2 migration wrong for ' + x.name + ': ' + JSON.stringify([x.level, x.exp, x.specials, x.tactics, x.record]));
    if (SG.Specials.known(x).length !== 1) throw new Error('migrated general should only have the signature special');
  }
  if (JSON.stringify(GameState.fromJSON(JSON.stringify(m))) !== JSON.stringify(m)) throw new Error('migrated game round trip mismatch');
  for (const bad of [1, 5, undefined]) {
    const db = JSON.parse(text); db.version = bad;
    let msg = '';
    try { GameState.fromJSON(JSON.stringify(db)); } catch (e) { msg = e.message; }
    if (msg.indexOf('无法读取') < 0) throw new Error('version ' + bad + ' should be refused, got ' + msg);
  }
  // 浏览器存储：只有第二版键时能读（迁移），存档写入第四版键，旧键只读（不写、不删）
  const store = {};
  global.localStorage = { getItem: k => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = String(v); }, removeItem: k => { delete store[k]; } };
  store[GameState.V2_SAVE_KEY] = v2text;
  if (!GameState.hasSave() || GameState.hasLegacySave()) throw new Error('v2 save should count as a save');
  const info = GameState.saveInfo();
  if (!info || info.version !== 2 || info.year !== g.year) throw new Error('saveInfo for v2 save: ' + JSON.stringify(info));
  const L = GameState.load();
  if (!L || L.migratedFrom !== 2 || L.generals.some(x => x.level !== 1)) throw new Error('load() of a v2 save failed: ' + GameState.loadError);
  SG.G = L;
  if (L.save() !== true || !store[GameState.SAVE_KEY] || store[GameState.V2_SAVE_KEY] !== v2text) throw new Error('save() should write the v4 key and leave the v2 key untouched');
  if (GameState.saveInfo().version !== 4) throw new Error('saveInfo after save should be v4');
  store[GameState.LEGACY_SAVE_KEY] = '{}';
  if (GameState.hasLegacySave()) throw new Error('legacy flag with a v3 save present');
  GameState.deleteSave();
  if (GameState.hasSave() || !GameState.hasLegacySave()) throw new Error('deleteSave() should leave only the v1 save');
  delete global.localStorage;
  console.log(`(7b) save v4 OK: ${text.length} bytes, ${grown.length} generals leveled after 30 months (max L${Math.max(...g.generals.map(x => x.level))}); v2 save migrated (level 1, exp 0, signature only, innate tactics)`);
} catch (e) {
  delete global.localStorage;
  fails++;
  console.log('(7b) save v4 FAILED: ' + (e.stack || e));
}

// ---- (7c) 可习得必杀：数据与按等阶的参考伤害倍率 ----
try {
  const S = SG.Specials;
  const pool = S.pool();
  const byTier = {};
  for (const sp of pool) (byTier[sp.tier] = byTier[sp.tier] || []).push(sp);
  for (let t = 1; t <= 4; t++) if (!(byTier[t] && byTier[t].length >= 6)) throw new Error('pool tier ' + t + ' has too few entries');
  for (const sp of pool) {
    if (sp.id.indexOf('lp:') !== 0 || !sp.pool || !sp.name || !sp.desc || S.FX.indexOf(sp.fx) < 0) throw new Error('bad pool entry ' + JSON.stringify(sp));
    if (sp.stat !== 'war') throw new Error('pool special should scale with 武力: ' + sp.name + ' (' + sp.stat + ')');
    if (S.byId(sp.id) !== sp) throw new Error('byId mismatch ' + sp.id);
    if (sp.problems.length) throw new Error('pool problems: ' + sp.problems.join('; '));
  }
  const rep = S.check();
  if (!rep.ok || rep.pool.total !== pool.length) throw new Error('specials check with pool failed: ' + JSON.stringify({ dupNames: rep.dupNames, dupSigs: rep.dupSigs, problems: rep.problems.slice(0, 5) }));
  // 参考局面（同 (5)）：武力 85 / 智力 60 的施展者在平原，四面敌军 3500 兵
  SG.Random.seed(779);
  const g = SG.G = GameState.newGame('cao');
  const dummy = i => ({ id: 9000 + i, name: '靶' + i, war: 70, intel: 60, pol: 50, faction: -1, troops: 3500, training: 60, loyalty: 80 });
  const ratios = {};
  const RANGE = { 1: [1.2, 1.9], 2: [1.45, 2.25], 3: [1.75, 2.6], 4: [2.0, 2.9] };
  const evs = [];     // 暗杀：期望值 = 成功率 × 目标兵力 + (1 − 成功率) × 失手伤害（整支溃散的额外价值不计）
  for (const sp of pool) {
    const assn = sp.kind === 'assassinate';
    if (DMG_KINDS.indexOf(sp.kind) < 0 && !assn) continue;
    const gen = { id: 9700, name: '参考', war: 85, intel: 60, pol: 50, faction: -1, troops: 0, training: 70, loyalty: 90 };
    gen.troops = SG.maxTroops(gen);
    const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: [gen], def: [dummy(1), dummy(2), dummy(3), dummy(4)], atkFood: 3000, atkGold: 0, routed: new Set(), captives: [] };
    const Mdl = new BattleModel(s);
    for (let x = 1; x <= 7; x++) for (let y = 1; y <= 7; y++) Mdl.map[x][y] = SG.Terrain.Plain;
    const [u, e1, e2, e3, e4] = Mdl.units;
    u.x = 4; u.y = 4; u.morale = 80;
    [[5, 4], [5, 5], [4, 6], [4, 2]].forEach((p, k) => { const e = [e1, e2, e3, e4][k]; e.x = p[0]; e.y = p[1]; e.morale = 70; });
    Mdl.side = 0;
    if (assn) for (const x of Mdl.units) x.commander = false;     // 参考目标都不是主将（主将成功率减半）
    const normal = refAction(Mdl, u, e1, sp);
    let best = 0;
    for (const t of Mdl.targetsOf(u, sp)) {
      const pl = Mdl.spPlan(u, sp, t);
      if (assn) {
        const p = pl.kill ? pl.kill.p : 0;
        let miss = 0;
        for (const h of pl.dmg) miss += Math.min(h.unit.troops, h.amt);
        best = Math.max(best, p * (pl.kill ? pl.kill.unit.troops : 0) + (1 - p) * miss);
        continue;
      }
      const per = new Map();
      for (const h of pl.dmg) per.set(h.unit, Math.min(h.unit.troops, (per.get(h.unit) || 0) + h.amt));
      let dmg = [...per.values()].sort((a, b) => b - a).slice(0, sp.kind === 'rampage' ? 99 : 2).reduce((a, b) => a + b, 0);
      if (pl.drain > 0) for (const h of pl.dmg) if (h.primary) dmg += Math.min(h.unit.troops, h.amt) * pl.drain * 0.5;
      best = Math.max(best, dmg);
    }
    if (assn) { evs.push({ name: sp.name, tier: sp.tier, r: best / Math.max(1, normal) }); continue; }
    (ratios[sp.tier] = ratios[sp.tier] || []).push({ name: sp.name, r: best / Math.max(1, normal) });
  }
  // 暗杀的期望值（不计整支溃散 / 主将被刺的额外价值）须落在同阶伤害型的范围内，且逐阶递增
  for (const o of evs) {
    const lo = 0.7, hi = RANGE[o.tier][1];
    if (!(o.r >= lo && o.r <= hi)) throw new Error(`pool assassinate ${o.name} (tier ${o.tier}) expected value / normal attack ${o.r.toFixed(2)} outside ${lo}-${hi}`);
  }
  evs.sort((a, b) => a.tier - b.tier);
  for (let k = 1; k < evs.length; k++) if (evs[k].tier > evs[k - 1].tier && !(evs[k].r > evs[k - 1].r)) throw new Error('pool assassinate expected value should rise with tier');
  // 每位武将至多一招可习得的暗杀
  {
    const killer = { id: 9701, name: '刺客', war: 95, intel: 60, pol: 50, faction: -1, troops: 3000, training: 70, loyalty: 90 };
    Pg.ensure(killer); killer.exp = Pg.EXP[10]; Pg.ensure(killer);
    const assnIds = pool.filter(sp => sp.kind === 'assassinate').map(sp => sp.id);
    killer.specials = [assnIds[0]];
    if (Pg.eligible(killer, 'special').some(sp => sp.kind === 'assassinate')) throw new Error('a general should learn at most one pool assassinate');
    if (Pg.learn(killer, 'special', assnIds[1])) throw new Error('learn() accepted a second pool assassinate');
  }
  const meds = [];
  const line = [];
  for (let t = 1; t <= 4; t++) {
    const rs = ratios[t].map(o => o.r);
    meds.push(median(rs));
    line.push(`T${t} median ${median(rs).toFixed(2)} [${Math.min(...rs).toFixed(2)}-${Math.max(...rs).toFixed(2)}] (n${rs.length})`);
    for (const o of ratios[t]) if (!(o.r >= RANGE[t][0] && o.r <= RANGE[t][1])) throw new Error(`pool ${o.name} (tier ${t}) reference ratio ${o.r.toFixed(2)} outside ${RANGE[t].join('-')}`);
  }
  for (let t = 1; t < 4; t++) if (!(meds[t] > meds[t - 1])) throw new Error('pool tier medians should increase: ' + meds.map(x => x.toFixed(2)).join(' '));
  console.log(`(7c) learnable specials OK: ${pool.length} (${[1, 2, 3, 4].map(t => 'T' + t + ':' + byTier[t].length).join(' ')}), names & (kind, params) unique vs ${rep.total} signature specials; reference damage / normal attack (war 85): ${line.join('; ')}; assassinate expected value ${evs.map(o => o.name + ' ' + o.r.toFixed(2)).join(' ')} (at most one per general)`);
} catch (e) {
  fails++;
  console.log('(7c) learnable specials FAILED: ' + (e.stack || e));
}

// ---- (7d) 计策目录与新计策规则 ----
try {
  const all = Tc.all();
  if (all.length < 16) throw new Error('tactics catalogue too small: ' + all.length);
  if (new Set(all.map(t => t.id)).size !== all.length || new Set(all.map(t => t.name)).size !== all.length) throw new Error('tactic ids / names not unique');
  ['fire', 'rockfall', 'confuse', 'inspire'].forEach((id, i) => {
    const t = Tc.byId(id), d = SG.Defs.tactics[i];
    if (!t || t.kind !== d.kind || t.name !== d.name || t.reqInt !== d.reqInt || t.power !== d.power || Tc.isNew(t)) throw new Error('original tactic changed: ' + id);
  });
  const learn = Tc.learnable();
  for (let t = 1; t <= 4; t++) if (learn.filter(x => x.tier === t).length < 3) throw new Error('too few learnable tactics in tier ' + t);
  for (const t of learn) {
    if (!Tc.isNew(t) || SG.Specials.FX.indexOf(t.fx) < 0 || !/^#[0-9a-f]{6}$/i.test(t.color) || !t.desc || !Tc.rules(t)) throw new Error('bad tactic entry ' + t.id);
    if (t.reqInt !== [0, 50, 65, 80, 92][t.tier]) throw new Error('tactic ' + t.id + ' reqInt should match its tier threshold');
  }
  // 小战场：施计者（智力 96，会全部新计策）对若干敌军
  SG.Random.seed(4321);
  const g = SG.G = GameState.newGame('cao');
  const realValue = SG.Random.value;
  function tfield(nFoes, nFriends) {
    const caster = { id: 9800, name: '施计', war: 60, intel: 96, pol: 60, faction: -1, troops: 4000, training: 70, loyalty: 90 };
    Pg.ensure(caster); caster.tactics = Pg.innateTactics(caster).concat(learn.map(t => t.id));
    const mine = [caster].concat(Array.from({ length: nFriends || 0 }, (_, i) => ({ id: 9810 + i, name: '友' + i, war: 60, intel: 40, pol: 40, faction: -1, troops: 2500, training: 60, loyalty: 80 })));
    const theirs = Array.from({ length: nFoes }, (_, i) => ({ id: 9820 + i, name: '敌' + i, war: 60, intel: 40, pol: 40, faction: -1, troops: 3500, training: 60, loyalty: 80 }));
    const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: mine, def: theirs, atkFood: 9000, atkGold: 0, routed: new Set(), captives: [] };
    const Mdl = new BattleModel(s);
    for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) Mdl.map[x][y] = SG.Terrain.Plain;
    Mdl.castle = { x: Mdl.W - 1, y: Mdl.H - 1 };
    const u = Mdl.units[0], foes = Mdl.units.filter(x => x.side === 1), friends = Mdl.units.filter(x => x.side === 0 && x !== u);
    u.x = 4; u.y = 5;
    foes.forEach((e, k) => { e.x = 6 + (k % 2); e.y = 4 + (k >> 1); e.commander = false; });
    friends.forEach((a, k) => { a.x = 4; a.y = 6 + k; a.commander = false; });
    for (const x of Mdl.units) { x.morale = 80; x.commander = false; }
    Mdl.startSide(0);
    return { Mdl, u, foes, friends };
  }
  const T = id => Tc.byId(id);
  const notes = [];
  // 水计：须临河
  {
    const { Mdl, u, foes } = tfield(2);
    if (Mdl.tacticUsable(u, T('water')).ok || Mdl.tacticUsable(u, T('water')).why.indexOf('临河') < 0) throw new Error('water should need a river');
    Mdl.map[foes[0].x][foes[0].y + 1] = SG.Terrain.River;
    Mdl.burning[foes[0].x][foes[0].y] = 2;
    if (!Mdl.tacticUsable(u, T('water')).ok) throw new Error('water should be usable next to a river');
    SG.Random.value = () => 0;
    let r; try { r = Mdl.useTactic(u, T('water'), foes[0]); } finally { SG.Random.value = realValue; }
    if (!r.success || r.hits.length < 2 || r.doused.length < 1) throw new Error('water: ' + JSON.stringify({ s: r.success, h: r.hits.length, d: r.doused.length }));
    // 冷却：「冷却 N 日」= 用后的 N 个本方回合不能再用（第 d 日用，第 d+N+1 日起可用）
    const cdW = T('water').cd;
    if (!(cdW >= 1)) throw new Error('water should have a cooldown');
    if (Mdl.tacticUsable(u, T('water')).ok) throw new Error('water should be on cooldown the same day');
    for (let k = 1; k <= cdW; k++) {
      Mdl.endSide(); Mdl.endSide();
      if (Mdl.tacticUsable(u, T('water')).ok) throw new Error('water (冷却' + cdW + '日) usable again after only ' + k + ' day(s)');
    }
    Mdl.endSide(); Mdl.endSide();
    if (!Mdl.tacticUsable(u, T('water')).ok) throw new Error('water cooldown (' + cdW + ' day) should be over after ' + (cdW + 1) + ' days: ' + Mdl.tacticUsable(u, T('water')).why);
    notes.push(`水计 hits ${r.hits.length} dmg ${r.dmg}, 冷却${cdW}日 = next use day +${cdW + 1}`);
  }
  // 每种有冷却的计策：每个本方回合都尝试施展（必定成功），可用的日子间隔 = 冷却 + 1
  {
    const pat = [];
    for (const t of learn) {
      if (!(t.cd > 0)) continue;
      const { Mdl, u, foes } = tfield(2, 1);
      foes[0].x = 5; foes[0].y = 5; foes[1].x = 6; foes[1].y = 5;
      if (t.need === 'river') Mdl.map[foes[0].x][foes[0].y + 1] = SG.Terrain.River;
      if (t.need === 'cover') Mdl.map[foes[0].x][foes[0].y + 1] = SG.Terrain.Forest;
      let line = '';
      for (let d = 0; d < 7; d++) {
        if (Mdl.result !== 0) throw new Error('cooldown probe battle ended early (' + t.name + ')');
        for (const x of Mdl.units) { x.troops = x.side === 1 ? 3500 : x === u ? 4000 : 1500; x.morale = 80; x.confused = 0; }
        const ok = Mdl.tacticUsable(u, t).ok;
        line += ok ? 'Y' : '-';
        if (ok) {
          const tg = Mdl.tacticTargets(u, t)[0];
          SG.Random.value = () => 0;
          try { Mdl.useTactic(u, t, tg); } finally { SG.Random.value = realValue; }
        }
        Mdl.endSide(); Mdl.endSide();
      }
      const want = Array.from({ length: 7 }, (_, d) => d % (t.cd + 1) === 0 ? 'Y' : '-').join('');
      if (line !== want) throw new Error(`${t.name} 冷却${t.cd}日: usable pattern ${line}, want ${want}`);
      if (Tc.rules(t).indexOf('冷却' + t.cd + '日') < 0) throw new Error(t.name + ' rules text should state 冷却' + t.cd + '日');
      pat.push(t.name + t.cd);
    }
    notes.push('cooldowns ' + pat.join(' '));
  }
  // 火计击溃敌主将而结束战斗：施计者的计策 / 伤害 / 击溃经验都要计入战后结算（结算在记经验之后）
  {
    const { Mdl, u, foes } = tfield(3);
    const e = foes[0];
    e.troops = 300; e.commander = true;
    foes[2].troops = 0; foes[2].troops0 = 0;     // 无兵上阵的守将（构造时 troops0 = 武将兵力 0）：战后不计经验、不计战绩
    SG.Random.value = () => 0;
    let r; try { r = Mdl.useTactic(u, T('fire'), e); } finally { SG.Random.value = realValue; }
    if (!r.success || e.alive || Mdl.result !== 1) throw new Error('fire should rout the 300-troop commander and end the battle: ' + JSON.stringify({ r, alive: e.alive, result: Mdl.result }));
    const pr = (Mdl.progress || []).find(p => p.gen === u.gen);
    const want = Pg.battleExp({ dmg: 300, routs: 1, cmdRouts: 1, tactics: 1, won: true, commander: false });
    if (!pr || pr.xp.tactics !== 1 || pr.xp.routs !== 1 || pr.xp.cmdRouts !== 1 || pr.xp.dmg !== 300 || pr.exp !== want) throw new Error('battle-ending fire lost the caster exp: ' + JSON.stringify(pr && { exp: pr.exp, want, xp: pr.xp }));
    if (Mdl.progress.some(p => p.gen === foes[2].gen) || !Mdl.progress.some(p => p.gen === foes[1].gen) || (foes[2].gen.record ? foes[2].gen.record.battles : 0) !== 0) throw new Error('a 0-troop unit should get no battle exp / record');
    notes.push(`battle-ending 火计 credits caster ${pr.exp} exp; 0-troop unit skipped`);
  }
  // 伏兵：须靠近林地 / 山丘
  {
    const { Mdl, u, foes } = tfield(1);
    foes[0].x = 5; foes[0].y = 5;
    if (Mdl.tacticUsable(u, T('ambush')).ok) throw new Error('ambush should need cover');
    Mdl.map[5][6] = SG.Terrain.Forest;
    if (!Mdl.tacticUsable(u, T('ambush')).ok || Mdl.tacticTargets(u, T('ambush'))[0] !== foes[0]) throw new Error('ambush should work next to a forest');
  }
  // 补给：耗粮、回复不超过出阵兵力
  {
    const { Mdl, u, friends } = tfield(1, 1);
    const a = friends[0];
    if (Mdl.tacticTargets(u, T('supply')).length) throw new Error('supply targets unhurt units');
    a.troops -= 900;
    const food0 = Mdl.food[0];
    const r = Mdl.useTactic(u, T('supply'), a);
    if (!r.success || r.heal <= 0 || a.troops > a.troops0 || !(Mdl.food[0] < food0)) throw new Error('supply: ' + JSON.stringify({ heal: r.heal, t: a.troops, t0: a.troops0, food: [food0, Mdl.food[0]] }));
    notes.push(`补给 +${r.heal} food −${food0 - Mdl.food[0]}`);
  }
  // 挑拨：敌军之后两次本方行动期间防御下降（我方的攻击更痛）；伪报：敌军下一次本方行动攻击下降
  {
    const { Mdl, u, foes } = tfield(2);
    const e = foes[0], e2 = foes[1];
    SG.Random.value = () => 0;
    try { Mdl.useTactic(u, T('provoke'), e); Mdl.useTactic(u, T('rumor'), e2); } finally { SG.Random.value = realValue; }
    if (!(Mdl.modK(e, 'def') < 1) || !(Mdl.atkExpect(u, e) > Mdl.atkExpect(u, e2))) throw new Error('provoke did not lower defence');
    let phases = 0, atkLow = 0;
    for (let k = 0; k < 4; k++) {
      do { Mdl.endSide(); } while (Mdl.side !== 1);
      if (Mdl.modK(e, 'def') < 1) phases++;
      if (Mdl.modK(e2, 'atk') < 1) atkLow++;
    }
    if (phases !== T('provoke').mod.days || atkLow !== T('rumor').mod.days) throw new Error(`debuff durations: provoke ${phases} (want ${T('provoke').mod.days}), rumor ${atkLow} (want ${T('rumor').mod.days})`);
    notes.push(`挑拨 ${phases} enemy phases, 伪报 ${atkLow}`);
  }
  // 连环：周围一格的敌军一起混乱；奇门遁甲：友军机动 +2；落雷不受城防减免
  {
    const { Mdl, u, foes, friends } = tfield(3, 1);
    SG.Random.value = () => 0;
    let r; try { r = Mdl.useTactic(u, T('chain'), foes[0]); } finally { SG.Random.value = realValue; }
    const near = foes.filter(e => BattleModel.dist(e, foes[0]) <= 1).length;
    if (r.confused.length !== near || near < 2) throw new Error('chain confused ' + r.confused.length + ' of ' + near);
    const m0 = friends[0].mobility;
    Mdl.useTactic(u, T('qimen'), u);
    if (friends[0].mobility !== m0 + 2) throw new Error('qimen should add 2 mobility');
    const { Mdl: M2, u: u2, foes: f2 } = tfield(1);
    const e = f2[0];
    const plain = M2.tacticPlan(u2, T('thunder'), e).dmg[0].amt, firePlain = M2.tacticPlan(u2, T('weather'), e).dmg[0].amt;
    M2.map[e.x][e.y] = SG.Terrain.Gate;
    if (Math.abs(M2.tacticPlan(u2, T('thunder'), e).dmg[0].amt - plain) > 1e-6) throw new Error('thunder should ignore gate defence');
    if (!(M2.tacticPlan(u2, T('weather'), e).dmg[0].amt < firePlain * 0.7)) throw new Error('weather should be reduced on a gate');
    notes.push(`连环 confused ${r.confused.length}`);
  }
  // 原四计不变：与第一版 useTactic 同随机数下结果完全一致
  {
    function useTacticV1(Mdl, u, t, target) {
      const TK = SG.TacticKind, Tr = SG.Terrain;
      if (t.kind === TK.Inspire) { for (const o of Mdl.units) if (o.alive && o.side === u.side && BattleModel.dist(o, u) <= 1) o.morale = Math.min(100, o.morale + 12 + M.idiv(u.gen.intel, 10)); return { success: true, dmg: 0 }; }
      const p = M.clamp(0.5 + (u.gen.intel - target.gen.intel) / 110, 0.12, 0.92);
      if (SG.Random.value() > p) return { success: false, dmg: 0 };
      let dmg = 0;
      if (t.kind === TK.Confuse) { target.confused = 2; target.acted = true; }
      else {
        let baseD = (t.kind === TK.Fire ? 260 : 340) + u.gen.intel * (t.kind === TK.Fire ? 11 : 13);
        const tt = Mdl.map[target.x][target.y];
        if (t.kind === TK.Fire) { if (tt === Tr.Forest) baseD *= 1.8; if (tt === Tr.Castle || tt === Tr.Gate) baseD *= 0.6; Mdl.burning[target.x][target.y] = 2; }
        baseD *= M.clamp(target.troops / 2500, 0.4, 1.4);
        baseD /= Mdl.modK(target, 'def');
        dmg = Math.min(target.troops, M.roundToInt(baseD * SG.Random.rangeFloat(0.85, 1.15)));
        target.troops -= dmg; target.morale = Math.max(0, target.morale - 10); Mdl.checkRout(target);
      }
      Mdl.checkEnd();
      return { success: true, dmg };
    }
    for (let k = 0; k < 80; k++) {
      const snap = [];
      for (const v of [0, 1]) {
        SG.Random.seed(600 + k);
        const { Mdl, u, foes } = tfield(2);
        u.gen.intel = 40 + (k % 60);
        const t = SG.Defs.tactics[k % 4];
        const tg = t.kind === SG.TacticKind.Inspire ? u : foes[k % 2];
        if (k % 3 === 0) Mdl.map[tg.x][tg.y] = SG.Terrain.Forest;
        const r = v === 0 ? useTacticV1(Mdl, u, t, tg) : Mdl.useTactic(u, t, tg);
        snap.push(JSON.stringify({ r, next: SG.Random.value(), units: Mdl.units.map(x => [x.troops, x.morale, x.confused, x.alive]) }));
      }
      if (snap[0] !== snap[1]) throw new Error('original tactic differs from v1 at case ' + k + '\n' + snap[0] + '\n' + snap[1]);
    }
    notes.push('原四计 80 cases identical to v1');
  }
  console.log(`(7d) tactics OK: ${all.length} tactics (${[1, 2, 3, 4].map(t => 'T' + t + ':' + all.filter(x => x.tier === t).length).join(' ')}; learnable ${learn.length}); ${notes.join('; ')}`);
} catch (e) {
  fails++;
  console.log('(7d) tactics FAILED: ' + (e.stack || e));
}

// ---- (7e) 成长后的压力测试：随机等级与习得，电脑完整打完，每一手后检查状态 ----
try {
  let battles = 0, newTac = 0, pool = 0;
  const kinds = {};
  for (let seed = 1; seed <= 24; seed++) {
    SG.Random.seed(seed * 11);
    const g = SG.G = GameState.newGame('cao');
    g.player = -1;
    const r = SG.SeededRandom(seed);
    for (const x of g.generals) { Pg.gain(x, r.next(Pg.EXP[10] + 1)); checkCaps(x, '7e'); }
    const pr = pairs[seed % pairs.length];
    const src = g.cityByKey(pr[0]), tgt = g.cityByKey(pr[1]);
    const s = Conquest.prepare(src.owner, src, tgt, g.officersIn(src).filter(x => x.troops > 0).slice(0, 5), 3000, 0);
    const Mdl = new BattleModel(s);
    const st = newStats();
    fightAI(Mdl, st, true);
    battles++; newTac += st.newTac; pool += st.pool;
    for (const k of Object.keys(st.byTac)) kinds[k] = (kinds[k] || 0) + st.byTac[k];
    if (!Mdl.progress || Mdl.progress.length !== Mdl.units.length || s.progress !== Mdl.progress || !s.progressDone) throw new Error('battle did not settle experience');
    for (const p of Mdl.progress) if (!(p.exp >= 10 && p.exp <= Pg.BATTLE_CAP)) throw new Error('battle exp out of range ' + p.exp);
  }
  console.log(`(7e) leveled AI battles OK: ${battles} battles, learned specials used ${pool}, new tactics used ${newTac} (${Object.keys(kinds).sort().map(k => Tc.byId(k).name + '×' + kinds[k]).join(' ')})`);
} catch (e) {
  fails++;
  console.log('(7e) leveled battles FAILED: ' + (e.stack || e));
}

// ---- (7f) 多年全电脑模拟：成长开 / 关（自动结算不看等级，城池归属应完全相同）与等级分布 ----
const snaps = {};
try {
  const lines = [];
  for (let seed = 1; seed <= 4; seed++) {
    const owners = [];
    for (const on of [true, false]) {
      Pg.enabled = on;
      SG.Random.seed(seed);
      const g = SG.G = GameState.newGame('liubei');
      g.player = -1;
      if (on) (snaps[0] = snaps[0] || []).push(JSON.stringify(g));         // 开局（全员 1 级）
      for (let m = 0; m < 12 * 20; m++) {
        StrategyAI.runAI([]); StrategyAI.endMonth();
        if (on && (m === 5 || m === 11 || m === 35 || m === 95)) (snaps[m] = snaps[m] || []).push(JSON.stringify(g));
        if (on && (m === 59 || m === 119 || m === 239)) {
          const act = g.generals.filter(x => !x.dead && x.record.battles > 0);
          for (const x of g.generals) checkCaps(x, '7f');
          const lv = act.map(x => x.level), bt = act.map(x => x.record.battles);
          if (m === 119) {
            if (!(median(lv) >= 2 && median(lv) <= 6)) throw new Error('median level of active generals at year 10 = ' + median(lv));
            if (!act.some(x => x.level >= 6)) throw new Error('nobody reached level 6 by year 10');
          }
          lines.push(`  seed ${seed} year ${(m + 1) / 12}: active ${act.length}, battles median ${median(bt)} max ${Math.max(...bt)}, ${levelHist(act)}; learned specials ${act.reduce((a, x) => a + x.specials.length, 0)}, tactics ${act.reduce((a, x) => a + Pg.learnedTactics(x).length, 0)}`);
        }
      }
      owners.push(g.cities.map(c => c.owner).join(','));
      if (!on && g.generals.some(x => x.exp !== 0 || x.level !== 1)) throw new Error('progression off but generals gained exp');
    }
    if (owners[0] !== owners[1]) throw new Error('seed ' + seed + ': campaign outcome differs with progression on/off (autoResolve must not use levels or randomness)');
  }
  Pg.enabled = true;
  console.log('(7f) 4 seeds x 20 years, progression ON (city ownership identical with OFF):\n' + lines.join('\n'));
} catch (e) {
  Pg.enabled = true;
  fails++;
  console.log('(7f) campaign FAILED: ' + (e.stack || e));
}

// ---- (7g) 战术战斗平衡：取全电脑模拟开局 / 第 6、12 个月 / 第 3 年 / 第 8 年的局面，成长开 / 关各打一遍 ----
// （第 6、12 个月：玩家最先遇到已升级的电脑武将的时候，早期不能失衡）
try {
  const rows = [];
  const results = {};
  for (const m of [0, 5, 11, 35, 95]) {
    for (const on of [false, true]) {
      const st = newStats();
      (snaps[m] || []).forEach((text, si) => {
        for (let k = 0; k < 40; k++) {
          const g = SG.G = GameState.fromJSON(text);
          g.player = -1;
          SG.Random.seed(500 + si * 100 + k);
          const cand = [];
          for (const c of g.cities) {
            if (c.owner < 0) continue;
            for (const j of c.links) { const t = g.cities[j]; if (t.owner >= 0 && t.owner !== c.owner && g.officersIn(c).some(x => x.troops > 500) && g.officersIn(t).length) cand.push([c, t]); }
          }
          if (!cand.length) continue;
          const [c, t] = cand[(k * 7 + si) % cand.length];
          const atk = SG.Seq.orderByDesc(g.officersIn(c).filter(x => x.troops > 0), x => x.troops).slice(0, 5);
          const s = Conquest.prepare(c.owner, c, t, atk, 3000, 0);
          Pg.enabled = on;
          try {
            const Mdl = new BattleModel(s);
            fightAI(Mdl, st, false);
            st.n++; st.days += Mdl.day; if (Mdl.result === 1) st.w++;
            st.outcomes.push(Mdl.result + ':' + Mdl.day + ':' + Mdl.units.map(u => u.troops).join('/'));
          } finally { Pg.enabled = true; }
        }
      });
      results[m + ':' + on] = st;
    }
    const off = results[m + ':false'], on = results[m + ':true'];
    const pr = o => Object.keys(o.poolRatio).sort().map(t => 'T' + t + ' ' + median(o.poolRatio[t]).toFixed(2)).join(' ');
    // 局面中武力 ≥ 60 的在世武将有几位已学到必杀
    const lrn = (snaps[m] || []).map(text => { const gs = JSON.parse(text).generals.filter(x => !x.dead && x.war >= 60 && x.faction >= 0); return [gs.filter(x => (x.specials || []).length).length, gs.length]; });
    const lrnTxt = lrn.reduce((a, b) => a + b[0], 0) + '/' + lrn.reduce((a, b) => a + b[1], 0);
    rows.push(`  ${m === 0 ? 'start' : m < 12 ? 'month ' + (m + 1) : 'year ' + (m + 1) / 12} (war≥60 with a learned special ${lrnTxt}): OFF atk win ${(100 * off.w / off.n).toFixed(1)}% days ${(off.days / off.n).toFixed(1)} dmg/battle ${(off.dmg / off.n) | 0} | ON atk win ${(100 * on.w / on.n).toFixed(1)}% days ${(on.days / on.n).toFixed(1)} dmg/battle ${(on.dmg / on.n) | 0}; ` +
      `ON per battle: signature ${(on.sig / on.n).toFixed(2)}, learned specials ${(on.pool / on.n).toFixed(2)}, new tactics ${(on.newTac / on.n).toFixed(2)}, old tactics ${(on.oldTac / on.n).toFixed(2)} (OFF ${(off.oldTac / off.n).toFixed(2)})` +
      (on.pool ? `; learned special dmg / normal attack by tier: ${pr(on)}` : ''));
    if (m === 0 && on.outcomes.join() !== off.outcomes.join()) throw new Error('opening battles (all level 1) must be identical with progression on/off');
    const dw = Math.abs(on.w / on.n - off.w / off.n);
    if (dw > 0.12) throw new Error('attacker win rate shifted ' + (dw * 100).toFixed(1) + ' points with progression at month ' + m);
    if (Math.abs(on.days / on.n - off.days / off.n) > 3) throw new Error('battle length changed too much with progression');
    if (on.dmg / on.n > 1.35 * off.dmg / off.n) throw new Error('damage per battle grew too much with progression');
  }
  // 学到的必杀：各等阶的实战伤害倍率（合并第 3 年与第 8 年）
  const all = {};
  for (const key of ['35:true', '95:true']) for (const t of Object.keys(results[key].poolRatio)) all[t] = (all[t] || []).concat(results[key].poolRatio[t]);
  const tierLine = Object.keys(all).sort().map(t => `T${t} ${median(all[t]).toFixed(2)} (n${all[t].length})`).join(', ');
  let assn = [], assnKill = 0;
  for (const key of Object.keys(results)) { assn = assn.concat(results[key].assn); assnKill += results[key].assnKill; }
  const assnLine = assn.length ? `; learned assassinate realised dmg / normal attack mean ${(assn.reduce((a, b) => a + b, 0) / assn.length).toFixed(2)} (n${assn.length}, ${assnKill} kills)` : '';
  if (assn.length >= 10 && !(assn.reduce((a, b) => a + b, 0) / assn.length <= 2.2)) throw new Error('learned assassinate too strong in battle: ' + assnLine);
  for (const t of Object.keys(all)) if (all[t].length >= 10 && !(median(all[t]) >= [0, 1.1, 1.3, 1.6, 1.8][t] && median(all[t]) <= [0, 2.0, 2.3, 2.7, 3.1][t])) throw new Error('in-battle learned special ratio for tier ' + t + ' = ' + median(all[t]).toFixed(2));
  console.log('(7g) tactical balance, 160 battles per row (same seeds and armies, progression OFF vs ON):\n' + rows.join('\n') + '\n  learned special damage / normal attack in battle: ' + tierLine + assnLine);
} catch (e) {
  Pg.enabled = true;
  fails++;
  console.log('(7g) tactical balance FAILED: ' + (e.stack || e));
}

// ---- (7h) 成长的分量：第 8 年局面里，攻方保留成长、守方打回 1 级（及反过来）的胜率与战损比变化 ----
// （第三版修订：老练加成 SG.Progress.vetK 每级 +4% 攻防与计策威力；电脑更常施展习得的计策）
try {
  const strip = x => { x.exp = 0; x.level = 1; x.specials = []; x.tactics = Pg.innateTactics(x); };
  const out = {};
  for (const mode of ['both', 'atkOnly', 'defOnly']) {
    let n = 0, w = 0, lossA = 0, lossD = 0;
    (snaps[95] || []).forEach((text, si) => {
      for (let k = 0; k < 80; k++) {
        const g = SG.G = GameState.fromJSON(text);
        g.player = -1;
        SG.Random.seed(900 + si * 100 + k);
        const cand = [];
        for (const c of g.cities) {
          if (c.owner < 0) continue;
          for (const j of c.links) { const t = g.cities[j]; if (t.owner >= 0 && t.owner !== c.owner && g.officersIn(c).some(x => x.troops > 500) && g.officersIn(t).length) cand.push([c, t]); }
        }
        if (!cand.length) continue;
        const [c, t] = cand[(k * 5 + si) % cand.length];
        const atk = SG.Seq.orderByDesc(g.officersIn(c).filter(x => x.troops > 0), x => x.troops).slice(0, 5);
        const s = Conquest.prepare(c.owner, c, t, atk, 3000, 0);
        if (mode === 'atkOnly') for (const x of s.def) strip(x);
        if (mode === 'defOnly') for (const x of s.atk) strip(x);
        const Mdl = new BattleModel(s);
        const t0s = Mdl.units.map(u => u.troops);
        fightAI(Mdl, newStats(), false);
        n++; if (Mdl.result === 1) w++;
        Mdl.units.forEach((u, i) => { const l = Math.max(0, t0s[i] - Math.max(0, u.troops)); if (u.side === 0) lossA += l; else lossD += l; });
      }
    });
    out[mode] = { n, w: w / Math.max(1, n), x: lossD / Math.max(1, lossA) };
  }
  const shiftA = out.atkOnly.w - out.both.w, shiftD = out.both.w - out.defOnly.w, swing = out.atkOnly.w - out.defOnly.w;
  const pct = v => (100 * v).toFixed(1) + '%';
  console.log(`(7h) year-8 armies (${out.both.n} battles each): attacker win ${pct(out.both.w)} both progressed, ${pct(out.atkOnly.w)} defenders reset to L1, ${pct(out.defOnly.w)} attackers reset to L1 ` +
    `(swing ${(100 * swing).toFixed(1)} pp); defender / attacker losses ${out.both.x.toFixed(2)} / ${out.atkOnly.x.toFixed(2)} / ${out.defOnly.x.toFixed(2)}`);
  if (!(shiftA >= -0.03 && shiftD >= -0.03)) throw new Error('progression should not hurt the progressed side');
  if (shiftA > 0.35 || shiftD > 0.35) throw new Error('progression decides battles too much: shifts ' + shiftA.toFixed(2) + ' / ' + shiftD.toFixed(2));
  // 成长须看得出来：只有攻方成长 vs 只有守方成长，攻方胜率差 ≥ 5 个百分点；成长一方的战损比各好 ≥ 8%
  if (!(swing >= 0.05)) throw new Error('progression barely matters: attacker win swing ' + (100 * swing).toFixed(1) + ' pp');
  if (!(out.atkOnly.x >= 1.08 * out.both.x && out.both.x >= 1.08 * out.defOnly.x)) throw new Error('progressed side should trade troops clearly better');
  if (out.atkOnly.x > 1.8 * out.both.x || out.both.x > 1.8 * out.defOnly.x) throw new Error('progression trades troops too much better');
} catch (e) {
  fails++;
  console.log('(7h) progression weight FAILED: ' + (e.stack || e));
}

console.log(`total ${((Date.now() - t0) / 1000).toFixed(1)}s`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
