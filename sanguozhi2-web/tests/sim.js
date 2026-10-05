'use strict';
/* ==========================================================================
   逻辑层模拟测试（Node，无依赖）
   移植自验证 Unity 版所用的两个 C# 测试程序：
   (1) 全电脑模拟：6 个种子 × 40 年，每月检查一致性；外加 40 场电脑对电脑的战术战斗。
   (2) 随机玩家模拟：6 个势力 × 4 个种子 × 25 年，玩家每月随机下达指令。
   用法：node tests/sim.js        退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
for (const f of ['core.js', 'data.js', 'model.js', 'strategy-ai.js', 'battle-model.js']) {
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

// 战术战斗：电脑对电脑完整打完
console.log('== (1b) 40 AI-vs-AI tactical battles ==');
let bw = 0, bl = 0, bdays = 0;
const pairs = [['chenliu', 'luoyang'], ['pingyuan', 'nanpi'], ['changsha', 'jiangling'], ['beiping', 'nanpi'], ['wan', 'xiangyang']];
const reasons = {};
for (let seed = 1; seed <= 40; seed++) {
  SG.Random.seed(seed * 7);
  const g = SG.G = GameState.newGame('cao');
  const pr = pairs[seed % pairs.length];
  const src = g.cityByKey(pr[0]); const tgt = g.cityByKey(pr[1]);
  const atk = g.officersIn(src).filter(x => x.troops > 0).slice(0, 5);
  try {
    const s = Conquest.prepare(src.owner, src, tgt, atk, 3000, 0);
    const Mdl = new BattleModel(s);
    let guard = 0;
    while (Mdl.result === 0 && guard++ < 2000) {
      const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u));
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
        if (act.kind === 'attack') Mdl.attack(u, act.target);
        else if (act.kind === 'tactic') Mdl.useTactic(u, act.tactic, act.target);
        else if (act.kind === 'duel') Mdl.duel(u, act.target);
        Mdl.spend(u);
      }
      if (Mdl.result === 0) Mdl.endSide();
    }
    if (Mdl.result === 0) throw new Error('battle never ended');
    for (const u of Mdl.units) if (u.troops < 0) throw new Error('negative unit troops');
    bdays += Mdl.day; if (Mdl.result === 1) bw++; else bl++;
    reasons[Mdl.resultReason] = (reasons[Mdl.resultReason] || 0) + 1;
    s.attackerWon = Mdl.result === 1;
    Conquest.apply(s);
    if (s.captives.length > 0) Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender);
    checkConsistency(g);
    if (seed <= 5) console.log(`battle ${pr[0]}->${pr[1]}: result ${Mdl.result} day ${Mdl.day} reason ${Mdl.resultReason} routed ${s.routed.size} captives ${s.captives.length}`);
  } catch (e) {
    fails++;
    console.log('battle seed ' + seed + ' FAILED: ' + (e.stack || e));
  }
}
console.log(`tactical battles: attacker won ${bw}, defender won ${bl}, avg days ${(bdays / Math.max(1, bw + bl)).toFixed(1)}`);
console.log('  end reasons: ' + Object.keys(reasons).map(k => k + ' ×' + reasons[k]).join('  '));

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

console.log(`total ${((Date.now() - t0) / 1000).toFixed(1)}s`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
