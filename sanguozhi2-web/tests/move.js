'use strict';
/* ==========================================================================
   调动武将（第二版 §4F）逻辑测试（Node，无依赖）
   在构造的局面上检查：经由己方城池链的可达性（BFS）、路程、移动 / 输送规则与不可用原因。
   用法：node tests/move.js        退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
for (const f of ['core.js', 'data.js', 'model.js', 'strategy-ai.js']) {
  vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
}
const SG = global.SG;
const { GameState, Commands, Conquest } = SG;

let fails = 0, checks = 0;
function ok(cond, msg) {
  checks++;
  if (!cond) { fails++; console.log('  FAIL: ' + msg); }
}
function eq(a, b, msg) { ok(JSON.stringify(a) === JSON.stringify(b), msg + '  got ' + JSON.stringify(a) + ' want ' + JSON.stringify(b)); }
function section(name, fn) {
  console.log('== ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAILED: ' + (e.stack || e)); }
}
function fresh(key, seed) {
  SG.Random.seed(seed || 7);
  return (SG.G = GameState.newGame(key || 'liubei'));
}
function give(g, keys, f) { for (const k of keys) { const c = g.cityByKey(k); c.owner = f; g.autoGovernor(c); } }
function names(g, routes) { return routes.map(r => r.city.name + r.hops); }

// 参照实现：在 f 方子图上做 Floyd–Warshall，求所有点对最短路程
function refHops(g, f) {
  const n = g.cities.length, INF = 1e9;
  const d = [];
  for (let i = 0; i < n; i++) { d.push(new Array(n).fill(INF)); d[i][i] = 0; }
  for (const c of g.cities) {
    if (c.owner !== f) continue;
    for (const j of c.links) if (g.cities[j].owner === f) d[c.id][j] = 1;
  }
  for (let k = 0; k < n; k++) for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    if (d[i][k] + d[k][j] < d[i][j]) d[i][j] = d[i][k] + d[k][j];
  }
  return d;
}

// ------------------------------------------------------------------ 开局 --
section('start: one city', () => {
  const g = fresh('liubei');
  const c = g.cityByKey('pingyuan');
  ok(c.owner === g.player, 'player owns 平原');
  eq(g.routesFrom(c).length, 0, 'no routes from the only city');
  eq(Commands.moveBlocked(c), '只有一座城池——取得第二座城（出征攻取或「拉拢」敌将献城）后即可调动武将。', 'move blocked reason (one city)');
  eq(Commands.transportBlocked(c), '只有一座城池——取得第二座城（出征攻取或「拉拢」敌将献城）后即可输送金粮。', 'transport blocked reason (one city)');
  // 董卓开局两城相邻：可以调动
  const g2 = fresh('dong');
  const ly = g2.cityByKey('luoyang');
  eq(names(g2, g2.routesFrom(ly)), ['长安1'], 'dong: 洛阳 → 长安 adjacent');
  eq(Commands.moveBlocked(ly), null, 'dong: move allowed');
  eq(Commands.transportBlocked(ly), null, 'dong: transport allowed');
});

// ------------------------------------------------------------ 构造局面 --
// 平原(我) — 北海(我) — 下邳(我) — 小沛(我)；濮阳为空城（平原、小沛都与之相邻，但不可穿行）；
// 长沙(我) 与上述城池不相连。
section('constructed chain: reachability and distance', () => {
  const g = fresh('liubei');
  const P = g.player;
  give(g, ['beihai', 'xiapi', 'xiaopei', 'changsha'], P);
  const py = g.cityByKey('pingyuan');
  const routes = g.routesFrom(py);
  eq(names(g, routes), ['北海1', '下邳2', '小沛3'], 'routes from 平原 (BFS order, hops)');
  eq(routes.map(r => r.path.map(i => g.cities[i].key)),
    [['pingyuan', 'beihai'], ['pingyuan', 'beihai', 'xiapi'], ['pingyuan', 'beihai', 'xiapi', 'xiaopei']], 'paths go through own cities only');
  ok(!routes.some(r => r.city.key === 'changsha'), 'disconnected own city 长沙 is not reachable');
  ok(!routes.some(r => r.city.key === 'puyang'), 'neutral 濮阳 is never a destination');
  eq(Commands.moveTargets(py).map(r => r.city.key), ['beihai', 'xiapi', 'xiaopei'], 'Commands.moveTargets');
  eq(Commands.transportTargets(py).map(r => r.city.key), ['beihai', 'xiapi', 'xiaopei'], 'Commands.transportTargets');
  eq(g.routeBetween(py, g.cityByKey('xiaopei')).length, 4, 'routeBetween length');
  eq(g.routeBetween(py, g.cityByKey('changsha')), null, 'routeBetween unreachable');
  eq(g.routeBetween(py, py), null, 'routeBetween same city');
  // 限制路程（maxHops）：只取不超过它的城，顺序不变
  eq(names(g, g.routesFrom(py, P, 1)), ['北海1'], 'maxHops 1');
  eq(names(g, g.routesFrom(py, P, 2)), ['北海1', '下邳2'], 'maxHops 2');
  eq(names(g, g.routesFrom(py, P, 0)), ['北海1', '下邳2', '小沛3'], 'maxHops 0 = unlimited');
  eq(names(g, g.routesFrom(py, undefined, 2)), ['北海1', '下邳2'], 'maxHops with default faction');
  eq(Commands.aiMoveTargets(py, P).map(x => x.key), ['beihai', 'xiapi'], 'Commands.aiMoveTargets: within 2 hops');
  eq(Commands.aiMoveTargets(g.cityByKey('xiaopei'), P).map(x => x.key), ['xiapi', 'beihai'], 'aiMoveTargets from 小沛');
  // 从一座敌城 / 空城出发
  eq(g.routesFrom(g.cityByKey('puyang'), P).length, 0, 'start city not owned → no routes');
  eq(g.routesFrom(g.cityByKey('puyang')).length, 0, 'neutral start → no routes');
  // 长沙孤立
  const cs = g.cityByKey('changsha');
  eq(g.routesFrom(cs).length, 0, '长沙 isolated');
  ok(/隔着/.test(Commands.moveBlocked(cs)), 'isolated move reason: ' + Commands.moveBlocked(cs));
  ok(/隔着/.test(Commands.transportBlocked(cs)), 'isolated transport reason');
  // 若濮阳归我方，小沛的路程缩短为 2（经濮阳）
  give(g, ['puyang'], P);
  const r2 = g.routesFrom(py);
  eq(r2.find(r => r.city.key === 'xiaopei').hops, 2, '小沛 via own 濮阳 is 2 hops');
  // 同盟城不可穿行
  give(g, ['puyang'], g.factionByKey('cao').id);
  g.setAlliance(P, g.factionByKey('cao').id, g.monthIndex + 12);
  eq(g.routesFrom(py).find(r => r.city.key === 'xiaopei').hops, 3, 'allied city is not traversable');
});

// ---------------------------------------------------------------- 移动 --
section('move command', () => {
  const g = fresh('liubei');
  const P = g.player;
  give(g, ['beihai', 'xiapi', 'changsha'], P);
  const py = g.cityByKey('pingyuan'), xp = g.cityByKey('xiapi'), cs = g.cityByKey('changsha');
  eq(Commands.moveBlocked(py), null, 'move allowed from 平原');
  const gy = g.generals.find(x => x.name === '关羽');
  const zf = g.generals.find(x => x.name === '张飞');
  // 不相邻但相连：可以
  const msg = Commands.move(gy, xp);
  ok(gy.city === xp.id && gy.moved, 'moved 关羽 to non-adjacent 下邳: ' + msg);
  eq(xp.governor, gy.id, '关羽 became governor of empty 下邳');
  // 不相连：拒绝，状态不变
  const before = JSON.stringify(g);
  const msg2 = Commands.move(zf, cs);
  eq(JSON.stringify(g), before, 'unreachable move leaves state unchanged');
  ok(/无法移驻/.test(msg2), 'unreachable move message: ' + msg2);
  ok(zf.city === py.id && !zf.moved, '张飞 stays');
  // 原地 → 拒绝
  ok(/无法移驻/.test(Commands.move(zf, py)), 'move to own city rejected');
  // 全员已行动
  for (const x of g.officersIn(py)) x.moved = true;
  eq(Commands.moveBlocked(py), '本城武将本月都已行动，没有可调动的武将。', 'all moved reason');
  // 空城（无武将）
  const bh = g.cityByKey('beihai');
  eq(g.officersIn(bh).length, 0, '北海 has no officers (given in setup)');
  eq(Commands.moveBlocked(bh), '北海没有武将。', 'no officers reason');
});

// ---------------------------------------------------------------- 输送 --
section('transport command', () => {
  const g = fresh('liubei');
  const P = g.player;
  give(g, ['beihai', 'xiapi', 'changsha'], P);
  const py = g.cityByKey('pingyuan'), xp = g.cityByKey('xiapi'), cs = g.cityByKey('changsha');
  const g0 = py.gold, f0 = py.food, xg = xp.gold, xf = xp.food;
  const msg = Commands.transport(py, xp, 100, 1000);
  ok(py.gold === g0 - 100 && py.food === f0 - 1000 && xp.gold === xg + 100 && xp.food === xf + 1000, 'transport to non-adjacent 下邳: ' + msg);
  const before = JSON.stringify(g);
  const msg2 = Commands.transport(py, cs, 100, 1000);
  eq(JSON.stringify(g), before, 'unreachable transport leaves state unchanged');
  ok(/无法输送/.test(msg2), 'unreachable transport message: ' + msg2);
  py.gold = 0; py.food = 0;
  eq(Commands.transportBlocked(py), '平原没有可输送的金粮。', 'nothing to send reason');
});

// ------------------------------------------------- 与参照实现比对（随机局面）--
section('random ownership vs Floyd–Warshall', () => {
  const rnd = SG.SeededRandom(2024);
  let compared = 0;
  for (let t = 0; t < 60; t++) {
    const g = fresh('liubei', t + 1);
    const nf = g.factions.length;
    for (const c of g.cities) c.owner = rnd.next(-1, Math.min(nf, 4));   // 少数势力，形成较长的城池链
    for (let f = 0; f < Math.min(nf, 4); f++) {
      const d = refHops(g, f);
      for (const c of g.cities) {
        if (c.owner !== f) { ok(g.routesFrom(c, f).length === 0, 'non-own start has no routes'); continue; }
        const routes = g.routesFrom(c, f);
        const want = g.cities.filter(x => x.id !== c.id && x.owner === f && d[c.id][x.id] < 1e9).map(x => x.id).sort((a, b) => a - b);
        eq(routes.map(r => r.city.id).sort((a, b) => a - b), want, `trial ${t} faction ${f} from ${c.name}: reachable set`);
        let lastHops = 0;
        for (const r of routes) {
          compared++;
          if (r.hops !== d[c.id][r.city.id]) ok(false, `hops ${c.name}→${r.city.name} ${r.hops} vs ${d[c.id][r.city.id]}`);
          if (r.hops < lastHops) ok(false, 'routes not sorted by hops');
          lastHops = r.hops;
          if (r.path.length !== r.hops + 1 || r.path[0] !== c.id || r.path[r.path.length - 1] !== r.city.id) ok(false, 'bad path ends ' + r.path);
          for (let i = 1; i < r.path.length; i++) {
            const a = g.cities[r.path[i - 1]], b = g.cities[r.path[i]];
            if (a.links.indexOf(b.id) < 0 || b.owner !== f) { ok(false, 'path step not a link through own city'); break; }
          }
        }
      }
    }
  }
  console.log(`  compared ${compared} routes`);
  ok(compared > 500, 'enough routes compared');
});

// ------------------------------------------------- 占领第二座城后即可调动 --
section('after first conquest the move command unlocks', () => {
  const g = fresh('liubei');
  const py = g.cityByKey('pingyuan');
  eq(Commands.moveBlocked(py) !== null, true, 'blocked before conquest');
  const target = g.cityByKey('puyang');           // 空城，与平原相邻
  const zf = g.generals.find(x => x.name === '张飞');
  const s = Conquest.prepare(g.player, py, target, [zf], 500, 0);
  Conquest.autoResolve(s);
  Conquest.apply(s);
  ok(s.attackerWon && target.owner === g.player, 'took 濮阳');
  eq(g.cityCount(g.player), 2, 'two cities now');
  eq(Commands.moveBlocked(py), null, 'move unlocked from 平原 (unmoved generals remain)');
  eq(Commands.moveBlocked(target), '本城武将本月都已行动，没有可调动的武将。', '濮阳: the attacker has already acted');
  eq(names(g, g.routesFrom(target)), ['平原1'], 'route back from 濮阳');
});

// ------------------------------------------- 「拉拢」敌将献城也能解锁 --
section('a defecting city also unlocks the move command', () => {
  const g = fresh('liubei');
  const py = g.cityByKey('pingyuan');
  // 构造：与平原相邻的濮阳归曹操，只驻一名非君主武将；劝诱必然成功时他献城归降
  const t = g.cityByKey('puyang');
  const cao = g.factionByKey('cao').id;
  ok(py.links.indexOf(t.id) >= 0, '濮阳 is adjacent to 平原');
  t.owner = cao;
  const target = g.generalsOf(cao).find(x => !g.isRuler(x));
  target.city = t.id;
  g.autoGovernor(t);
  eq(g.officersIn(t).length, 1, '濮阳 has a single officer');
  const agent = g.generals.find(x => x.name === '刘备');
  const rv = SG.Random.value;
  SG.Random.value = () => 0;
  let msg;
  try { msg = Commands.persuade(agent, target, g.player); } finally { SG.Random.value = rv; }
  ok(/献城归降/.test(msg), 'persuade: ' + msg);
  eq(t.owner, g.player, t.name + ' now ours');
  eq(Commands.moveBlocked(py), null, 'move unlocked from 平原 after the defection');
});

// --------------------------------------------- 电脑：现有调动规则依旧成立 --
// 电脑只把武将调往相邻的己方城；新规则下这些移动必然可达（相邻即一步），Commands.move 不会拒绝。
section('AI moves stay valid under the new rule', () => {
  let moves = 0, rejected = 0;
  const orig = Commands.move;
  Commands.move = function (gen, to) {
    moves++;
    const from = SG.G.cities[gen.city];
    const r = orig.call(Commands, gen, to);
    if (gen.city !== to.id) { rejected++; console.log('  rejected: ' + r + ' from ' + from.name); }
    return r;
  };
  try {
    for (let seed = 1; seed <= 3; seed++) {
      SG.Random.seed(seed);
      const g = SG.G = GameState.newGame('liubei');
      g.player = -1;
      for (let m = 0; m < 12 * 15; m++) { SG.StrategyAI.runAI([]); SG.StrategyAI.endMonth(); }
    }
  } finally { Commands.move = orig; }
  console.log(`  AI moves ${moves}, rejected ${rejected}`);
  ok(moves > 0, 'AI moved generals');
  eq(rejected, 0, 'no AI move rejected');
});

// ------------------------- 可选接线（INTEGRATION）：电脑调往两步之内的前线 --
// 在独立的 vm 上下文里把 strategy-ai.js 第 6 步换成 Commands.aiMoveTargets(c, f)（若尚未接线），
// 跑 15 年全电脑对局：电脑确实会调往不相邻的城，且没有任何移动被拒绝。
section('optional AI wiring: 2-hop fronts', () => {
  const OLD = 'const fronts = c.links.map(i => g.cities[i]).filter(n => n.owner === f);';
  const NEW = 'const fronts = Commands.aiMoveTargets(c, f);';
  let ai = fs.readFileSync(path.join(root, 'strategy-ai.js'), 'utf8');
  if (ai.indexOf(NEW) < 0) {
    if (ai.indexOf(OLD) < 0) { ok(false, 'strategy-ai.js step 6 not found (neither old nor wired line)'); return; }
    ai = ai.replace(OLD, NEW);
  }
  const ctx = { console, Math, JSON };
  ctx.window = ctx;
  vm.createContext(ctx);
  for (const f of ['core.js', 'data.js', 'model.js']) vm.runInContext(fs.readFileSync(path.join(root, f), 'utf8'), ctx, { filename: f });
  vm.runInContext(ai, ctx, { filename: 'strategy-ai.js (wired)' });
  const X = ctx.SG;
  let moves = 0, far = 0, rejected = 0;
  const orig = X.Commands.move;
  X.Commands.move = function (gen, to) {
    moves++;
    const from = X.G.cities[gen.city];
    if (from.links.indexOf(to.id) < 0) far++;
    const r = orig.call(X.Commands, gen, to);
    if (gen.city !== to.id) rejected++;
    return r;
  };
  for (let seed = 1; seed <= 3; seed++) {
    X.Random.seed(seed);
    const g = X.G = X.GameState.newGame('liubei');
    g.player = -1;
    for (let m = 0; m < 12 * 15; m++) { X.StrategyAI.runAI([]); X.StrategyAI.endMonth(); }
  }
  console.log(`  AI moves ${moves} (non-adjacent ${far}), rejected ${rejected}`);
  ok(moves > 0 && far > 0, 'wired AI moves generals to non-adjacent cities');
  eq(rejected, 0, 'no wired AI move rejected');
});

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
