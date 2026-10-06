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
   用法：node tests/sim.js        退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
for (const f of ['core.js', 'data.js', 'model.js', 'strategy-ai.js', 'battle-model.js', 'specials-data.js', 'specials.js']) {
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
  if (sp.stat !== 'war' && u.gen.intel >= 50) ref = Math.max(ref, M.clamp(0.5 + (u.gen.intel - t.gen.intel) / 110, 0.12, 0.92) * (260 + u.gen.intel * 11) * M.clamp(t.troops / 2500, 0.4, 1.4));
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
        if (act.kind === 'attack') { const r = Mdl.attack(u, act.target); if (stats) stats.atkDmg.push(r.dmg); }
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
          const res = Mdl.useSpecial(u, act.target);
          if (!res.sp || !u.specialUsed) throw new Error('planned special not used: ' + u.gen.name + ' ' + res.why);
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
  if (!(ratio >= 1.5 && ratio <= 3.2)) throw new Error('special/attack damage ratio out of range: ' + ratio.toFixed(2));
  if (!(ratio1 >= 1.0 && ratio1 <= 2.8)) throw new Error('special/attack per-unit ratio out of range: ' + ratio1.toFixed(2));
  if (runs[true].timeout > runs[false].timeout + 30) throw new Error('specials made many more battles time out');
} catch (e) {
  BattleModel.specialsEnabled = true;
  fails++;
  console.log('specials balance FAILED: ' + (e.stack || e));
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
  const cultures = ['han', 'wa', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian', 'nanman', 'yi'];
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
      // 数日推进：中毒、燃烧、加成到期
      for (let d = 0; d < 4 && Mdl.result === 0; d++) { Mdl.endDay(); checkBattleState(Mdl, 'fuzz endDay'); }
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
  for (const k of DAMAGE_KINDS) for (const o of byKind[k] || []) if (!(o.r > 1.2 && o.r < 3.6)) throw new Error('reference ratio out of range: ' + o.name + ' ' + k + ' ' + o.r.toFixed(2));
} catch (e) {
  fails++;
  console.log('useSpecial fuzz FAILED: ' + (e.stack || e));
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

console.log(`total ${((Date.now() - t0) / 1000).toFixed(1)}s`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
