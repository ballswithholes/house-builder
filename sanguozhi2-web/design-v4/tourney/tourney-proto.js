'use strict';
/* ==========================================================================
   武林大会 · 设计原型（TOURNAMENT.md 的数字来源；只读游戏数据，不改任何游戏文件）

   用法（在仓库根目录 sanguozhi2-web/ 下）：
     node design-v4/tourney/tourney-proto.js entrants            开局名单：各剧本的参赛人数、签表大小、种子
     node design-v4/tourney/tourney-proto.js single [N] [scen]  从开局阵容连办 N 届（默认 200），冠军分布、用时
     node design-v4/tourney/tourney-proto.js campaign [seeds] [years] [scen]
                                                                全电脑对局：每年正月办大会（奖金、出赛武将本月不得行动），
                                                                与「不办」配对比较；三冠（金刚杵）出现的年份与得主
     node design-v4/tourney/tourney-proto.js bracket [N]        签表算法自检（种子位置、轮空、同势力回避、确定性）

   说明
   · 单挑用第三版的格斗模拟（SG.DuelGame.simulate，电脑对电脑）；第四版的连段 / 会心按 COMBAT-V4 保持胜率曲线「同样的精神」，
     所以这里量到的「谁更常夺冠」可作第四版的估计（COMBAT-V4 §15.2 的胜率目标与第三版相同）。
   · 装备：只计开局的名将专属与君主专属装备的 武力（items.json），单挑时按 ITEMS §2.4 的 duelWar = 武力 + min(6, ⌊装备武力/2⌋)。
     冠军奖品若是兵器 / 防具，按其 武力 计入（同一规则）。商店购买不模拟。
   · 每场比赛单独播种（hash(局种子, 年, 场次)），比赛结束后主随机流按 hash(局种子, 年, 'post') 重新播种：
     实际实现用 SG.Random.swap（TOURNAMENT §10.2），主随机流完全不受观战 / 亲自出战与否的影响。
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

global.window = global;
const ROOT = path.join(__dirname, '..', '..');
const JS = path.join(ROOT, 'js');
const load = f => vm.runInThisContext(fs.readFileSync(path.join(JS, f), 'utf8'), { filename: f });
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js', 'model.js', 'strategy-ai.js', 'duel-game.js']) load(f);
const SG = global.SG;
const D = SG.DuelGame;
const ITEMS = JSON.parse(fs.readFileSync(path.join(ROOT, 'design-v4', 'items.json'), 'utf8'));
const ITEM = new Map(ITEMS.items.map(i => [i.id, i]));

// ------------------------------------------------------------------ 工具 --
function hash(...parts) {
  let h = 0x811c9dc5;
  const s = parts.join('|');
  for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}
const pct = (a, b) => (b ? (100 * a / b).toFixed(1) + '%' : '-');
const avg = a => (a.length ? a.reduce((s, x) => s + x, 0) / a.length : 0);
const median = a => { const b = a.slice().sort((x, y) => x - y); return b.length ? b[b.length >> 1] : 0; };

// ------------------------------------------------------------------ 规则（TOURNAMENT §3–§5 的参考实现） --
const R = {
  T_ENTRY: { classic: 72, world: 75 },   // 电脑推荐的武力门槛
  T_COURTESY: 65,                         // 无人达标时，最强者武力 ≥ 65 也派 1 人（以示礼数）
  T_MIN: 60,                              // 报名下限（玩家同样）
  MAX_PER_FACTION: 5,
  MAX_DRAW: 128,
  RULER_WAR: 85,                          // 君主亲自下场：武力 ≥ 85，或史载好斗者
  RULER_FIGHTERS: new Set(['康茂德']),    // Cassius Dio 73.17–20：康茂德亲自以角斗士身份下场
  GOLD: [400, 200, 100],
};
function scenarioOf(g) { return g.scenario || 'classic'; }

// 装备 武力（开局专属 / 君主专属 + 奖品），按 ITEMS §2.4 折半计入单挑
const gearWar = new Map();   // name → 装备 武力 合计
function setupGear(g) {
  gearWar.clear();
  const sc = scenarioOf(g);
  const T = ITEMS[sc];
  const add = (name, id) => { const it = ITEM.get(id); if (!it || !it.stats) return; gearWar.set(name, (gearWar.get(name) || 0) + (it.stats.war || 0)); };
  for (const s of T.signature) add(s.holder, s.item);
  for (const k of Object.keys(T.rulers)) add(T.rulers[k].ruler, T.rulers[k].item);
}
function duelWar(gen) { return Math.min(110, gen.war + Math.min(6, Math.floor((gearWar.get(gen.name) || 0) / 2))); }

function seedScore(gen, st) {
  const titles = Math.min(3, st.wins[gen.name] || 0);
  return duelWar(gen) + 0.6 * (gen.level || 1) + 1.5 * titles + (st.defending === gen.name ? 10 : 0);
}

// 电脑的出赛名单
function aiPick(g, f, st) {
  const sc = scenarioOf(g);
  const isRuler = x => g.factions[f].ruler === x.id;
  const cands = g.generalsOf(f).filter(x => !x.dead && x.war >= R.T_MIN && !(st.injured.get(x.name) > g.monthIndex) &&
    (!isRuler(x) || x.war >= R.RULER_WAR || R.RULER_FIGHTERS.has(x.name)));
  cands.sort((a, b) => seedScore(b, st) - seedScore(a, st) || a.id - b.id);
  let pick = cands.filter(x => x.war >= R.T_ENTRY[sc]).slice(0, R.MAX_PER_FACTION);
  if (!pick.length && cands.length && cands[0].war >= R.T_COURTESY) pick = [cands[0]];
  return pick;
}

// ------------------------------------------------------------------ 签表 --
// 标准种子顺序：order(2) = [1,2]；order(2n) = 对 order(n) 的每个 x 依次放 x、2n+1−x。相邻两位为第一轮对阵
function seedOrder(S) {
  let o = [1, 2];
  while (o.length < S) { const n2 = o.length * 2; const p = []; for (const x of o) p.push(x, n2 + 1 - x); o = p; }
  return o;
}
// 两个签位（0 起）最早相遇的轮次：1 + ⌊log2(i xor j)⌋
function meetRound(i, j) { let x = i ^ j, r = 0; while (x) { r++; x >>= 1; } return r; }
const MEET_W = [0, 16, 8, 4, 2, 0, 0, 0, 0];
function collisionCost(slots) {
  let c = 0;
  for (let i = 0; i < slots.length; i++) {
    const a = slots[i]; if (!a) continue;
    for (let j = i + 1; j < slots.length; j++) { const b = slots[j]; if (b && a.f === b.f) c += MEET_W[meetRound(i, j)] || 0; }
  }
  return c;
}
// entrants: [{ gen, f, score }]（任意顺序）→ { S, K, slots: [entry|null]（null = 轮空）, ranks }
function makeDraw(entrants, rng) {
  const N = entrants.length;
  let S = 8; while (S < N) S *= 2;
  S = Math.min(S, R.MAX_DRAW);
  const ranked = entrants.slice().sort((a, b) => b.score - a.score || a.gen.id - b.gen.id).slice(0, S);
  const n = ranked.length;
  const K = Math.max(2, S / 4);
  const order = seedOrder(S);                       // order[slot] = 该签位在完美种子表中的名次
  const posOf = new Array(S + 1);
  order.forEach((r, slot) => { posOf[r] = slot; });
  const slots = new Array(S).fill(null);
  // 种子 1、2 固定；3–4、5–8、9–16、17–32 各成一档，档内随机分到本档的签位（取同势力相遇代价最小者，至多 24 次尝试）
  const place = (r, e) => { slots[posOf[r]] = e; };
  place(1, ranked[0]); if (n > 1) place(2, ranked[1]);
  for (let lo = 3; lo <= Math.min(K, n); lo = lo * 2 - 1) {
    const hi = Math.min(lo * 2 - 2, K, n);
    const band = []; for (let r = lo; r <= hi; r++) band.push(r);
    const members = band.map(r => ranked[r - 1]);
    let best = null, bestC = Infinity;
    for (let t = 0; t < 24; t++) {
      const perm = members.slice();
      for (let i = perm.length - 1; i > 0; i--) { const j = Math.floor(rng.nextDouble() * (i + 1)); [perm[i], perm[j]] = [perm[j], perm[i]]; }
      band.forEach((r, i) => place(r, perm[i]));
      const c = collisionCost(slots);
      if (c < bestC) { bestC = c; best = perm; }
      if (c === 0) break;
    }
    band.forEach((r, i) => place(r, best[i]));
  }
  // 轮空：B = S − n 个，给名次 1..B。种子的轮空在其实际签位的对面；B > K 时，名次 K+1..B 的非种子随机占一个空的对阵格，对面轮空
  const B = S - n;
  const bye = new Array(S).fill(false);
  for (let r = 1; r <= Math.min(B, K, n); r++) bye[slots.findIndex(e => e === ranked[r - 1]) ^ 1] = true;
  const rest = ranked.slice(K);
  const shuffle = a => { for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rng.nextDouble() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };
  const emptyPairs = []; for (let i = 0; i < S; i += 2) if (!slots[i] && !slots[i + 1] && !bye[i] && !bye[i + 1]) emptyPairs.push(i);
  shuffle(emptyPairs);
  let ri = 0;
  for (let r = K + 1; r <= B && ri < rest.length; r++, ri++) { const i = emptyPairs.pop(); const k = rng.nextDouble() < 0.5 ? 0 : 1; slots[i + k] = rest[ri]; bye[i + 1 - k] = true; }
  // 其余非种子随机填入剩下的空签位
  const open = []; for (let i = 0; i < S; i++) if (!slots[i] && !bye[i]) open.push(i);
  const others = shuffle(rest.slice(ri));
  open.forEach((slot, i) => { slots[slot] = others[i]; });
  // 可交换的签位：非种子且没有轮空的对阵（交换不改变轮空的归属）
  const swappable = open.filter(i => !bye[i ^ 1]);
  // 同势力回避：非种子之间两两交换（爬山 400 步），只接受代价下降的交换
  let cost = collisionCost(slots);
  for (let it = 0; it < 400 && cost > 0 && swappable.length > 1; it++) {
    const a = swappable[Math.floor(rng.nextDouble() * swappable.length)], b = swappable[Math.floor(rng.nextDouble() * swappable.length)];
    if (a === b) continue;
    [slots[a], slots[b]] = [slots[b], slots[a]];
    const c = collisionCost(slots);
    if (c < cost) cost = c; else [slots[a], slots[b]] = [slots[b], slots[a]];
  }
  ranked.forEach((e, i) => { e.rank = i + 1; });
  return { S, K, slots, n, cost };
}

// ------------------------------------------------------------------ 比赛 --
const stats = { duels: 0, ms: 0, time: [], timeouts: 0, ko: 0, upsets: 0, gapWins: {} };
function playMatch(g, a, b, mid) {
  // a = 名次较高者（平局判 a 胜，与 BattleModel.duel / DuelGame 的「挑战者胜」一致）
  if (a.rank > b.rank) [a, b] = [b, a];
  SG.Random.seed(hash(g.seed, g.year, 'match', mid));
  const ga = { name: a.gen.name, war: duelWar(a.gen), intel: a.gen.intel, pol: a.gen.pol };
  const gb = { name: b.gen.name, war: duelWar(b.gen), intel: b.gen.intel, pol: b.gen.pol };
  const t0 = process.hrtime.bigint();
  const r = D.simulate(ga, gb);
  stats.ms += Number(process.hrtime.bigint() - t0) / 1e6;
  stats.duels++;
  stats.time.push(r.time);
  if (r.kind === 'time') stats.timeouts++; else stats.ko++;
  const w = r.winner === 0 ? a : b, l = r.winner === 0 ? b : a;
  const gap = Math.round((duelWar(w.gen) - duelWar(l.gen)) / 5) * 5;
  stats.gapWins[gap] = (stats.gapWins[gap] || 0) + 1;
  if (duelWar(w.gen) < duelWar(l.gen)) stats.upsets++;
  return { w, l, kind: r.kind };
}
function bo3(g, a, b, mid) {
  let wa = 0, wb = 0, k = 0;
  while (wa < 2 && wb < 2) { const r = playMatch(g, a, b, mid * 10 + (k++)); if (r.w === a) wa++; else wb++; }
  return wa === 2 ? { w: a, l: b } : { w: b, l: a };
}

// 一届大会（电脑对电脑）。返回 { podium:[1st,2nd,3rd], N, S, rounds, entrants, byFaction }
function runTournament(g, st, opts) {
  opts = opts || {};
  const entrants = [];
  for (const f of g.factions) {
    if (!f.alive) continue;
    for (const x of aiPick(g, f.id, st)) entrants.push({ gen: x, f: f.id, score: seedScore(x, st) });
  }
  if (entrants.length < 4) return null;
  const rng = SG.SeededRandom(hash(g.seed, g.year, 'draw'));
  const dr = makeDraw(entrants, rng);
  let line = dr.slots.slice();
  let round = 0, sfLosers = [];
  let mid = 0;
  while (line.length > 2) {
    round++;
    const next = [];
    for (let i = 0; i < line.length; i += 2) {
      const a = line[i], b = line[i + 1];
      if (!a || !b) { next.push(a || b); continue; }
      const r = playMatch(g, a, b, round * 1000 + (mid++));
      next.push(r.w);
      if (line.length === 4) sfLosers.push(r.l);
    }
    line = next;
  }
  let fin = null;
  if (line[0] && line[1]) fin = bo3(g, line[0], line[1], 9000); else fin = { w: line[0] || line[1], l: null };
  let third = null;
  if (sfLosers.length === 2) third = playMatch(g, sfLosers[0], sfLosers[1], 9500).w; else third = sfLosers[0] || null;
  const podium = [fin.w, fin.l, third];
  // 奖励（金；冠军奖品的 武力 计入装备）
  if (opts.prizes !== false) {
    podium.forEach((e, i) => { if (!e) return; const c = g.cities[e.gen.city]; if (c && c.owner === e.gen.faction) c.gold += R.GOLD[i]; st.goldTo[e.f] = (st.goldTo[e.f] || 0) + R.GOLD[i]; });
    if (opts.prizeWar && fin.w) gearWar.set(fin.w.gen.name, (gearWar.get(fin.w.gen.name) || 0) + opts.prizeWar);
  }
  // 出赛武将本月不得行动（仍可守城）
  if (opts.away !== false) for (const e of entrants) e.gen.moved = true;
  const champ = fin.w.gen.name;
  st.wins[champ] = (st.wins[champ] || 0) + 1;
  st.defending = champ;
  return { podium, N: entrants.length, S: dr.S, rounds: round + 1, entrants, cost: dr.cost };
}
function newState() { return { wins: {}, defending: null, injured: new Map(), goldTo: {} }; }

// ------------------------------------------------------------------ 模式 --
function newGame(sc, seed) {
  SG.Random.seed(seed);
  const g = SG.G = SG.GameState.newGame('cao', sc);
  g.player = -1;
  setupGear(g);
  return g;
}

function modeEntrants() {
  for (const sc of ['classic', 'world']) {
    const g = newGame(sc, 1);
    const st = newState();
    const all = [];
    let facs = 0;
    for (const f of g.factions) { const p = aiPick(g, f.id, st); if (p.length) facs++; for (const x of p) all.push({ gen: x, f: f.id, score: seedScore(x, st) }); }
    const dr = makeDraw(all, SG.SeededRandom(7));
    console.log(`${sc}: entrants ${all.length} from ${facs}/${g.factions.length} factions; draw ${dr.S}, seeds ${dr.K}, byes ${dr.S - dr.n}, rounds ${Math.log2(dr.S)} + final BO3 + 3rd; same-faction cost ${dr.cost}`);
    const top = all.slice().sort((a, b) => b.score - a.score).slice(0, 16);
    console.log('  top seeds: ' + top.map((e, i) => `${i + 1}.${e.gen.name}${duelWar(e.gen)}`).join(' '));
    const byF = {};
    for (const e of all) byF[e.f] = (byF[e.f] || 0) + 1;
    const hist = [0, 0, 0, 0, 0, 0];
    for (const k of Object.keys(byF)) hist[byF[k]]++;
    console.log('  factions sending 1..5: ' + hist.slice(1).join(' / '));
  }
}

function modeSingle(N, sc) {
  N = N || 200;
  for (const s of sc ? [sc] : ['classic', 'world']) {
    const champs = {}, finals = [], warW = [], ns = [];
    stats.duels = 0; stats.ms = 0; stats.time = []; stats.timeouts = 0; stats.ko = 0; stats.upsets = 0; stats.gapWins = {};
    let seed1 = 0, top4 = 0;
    for (let i = 0; i < N; i++) {
      const g = newGame(s, 1000 + i);
      g.year = 191; g.month = 1;
      const st = newState();
      const r = runTournament(g, st, { prizes: false, away: false });
      const c = r.podium[0];
      champs[c.gen.name] = (champs[c.gen.name] || 0) + 1;
      warW.push(duelWar(c.gen)); ns.push(r.N);
      if (c.rank === 1) seed1++;
      if (c.rank <= 4) top4++;
    }
    const list = Object.entries(champs).sort((a, b) => b[1] - a[1]).slice(0, 12).map(([k, v]) => `${k} ${pct(v, N)}`).join(', ');
    console.log(`== ${s}: ${N} tournaments from the opening rosters (191-01), entrants median ${median(ns)}`);
    console.log(`  champions: ${list}; distinct ${Object.keys(champs).length}`);
    console.log(`  champion duelWar median ${median(warW)}, min ${Math.min(...warW)}; top seed wins ${pct(seed1, N)}, a top-4 seed wins ${pct(top4, N)}`);
    console.log(`  duels ${stats.duels}: ${(stats.ms / stats.duels).toFixed(2)} ms each (v3 sim, Node), per tournament ${(stats.ms / N).toFixed(0)} ms; avg duel ${avg(stats.time).toFixed(1)} s, time-outs ${pct(stats.timeouts, stats.duels)}; upsets (lower duelWar wins) ${pct(stats.upsets, stats.duels)}`);
  }
}

function snapshot(g) {
  const alive = g.factions.filter(f => f.alive).length;
  const maxShare = Math.max(...g.factions.map(f => g.cityCount(f.id))) / g.cities.length;
  return { alive, maxShare };
}
function modeCampaign(seeds, years, sc) {
  seeds = seeds || 24; years = years || 15; sc = sc || 'classic';
  const out = { on: [], off: [] };
  const firstTriple = [], distinct = [], repeat = [], goldShare = [], strongestWins = [];
  for (const mode of ['off', 'on']) {
    for (let s = 0; s < seeds; s++) {
      const g = newGame(sc, (+process.env.SEED0 || 500) + s);
      const st = newState();
      let triple = null;
      let income = {};
      const champsSeen = new Set();
      let strong = 0, held = 0;
      for (let m = 0; m < years * 12; m++) {
        if (g.month === 1 && g.year >= 191 && mode === 'on') {
          const r = runTournament(g, st, { prizeWar: 0, away: process.env.AWAY !== '0', prizes: process.env.PRIZES !== '0' });
          if (r) {
            held++;
            const c = r.podium[0];
            champsSeen.add(c.gen.name);
            if (!triple && st.wins[c.gen.name] >= 3) triple = { year: g.year, name: c.gen.name, n: held };
            const big = g.factions.slice().sort((a, b) => g.cityCount(b.id) - g.cityCount(a.id))[0];
            if (c.f === big.id) strong++;
            SG.Random.seed(hash(g.seed, g.year, 'post'));
          }
        } else if (g.month === 1 && g.year >= 191) {
          SG.Random.seed(hash(g.seed, g.year, 'post'));   // 同样的重新播种（配对比较只差「办不办」）
        }
        // 收入统计（月）
        for (const c of g.cities) if (c.owner >= 0) income[c.owner] = (income[c.owner] || 0) + c.industry * 0.12 + c.town * 0.05;
        SG.StrategyAI.runAI([]);
        SG.StrategyAI.endMonth();
      }
      const snap = snapshot(g);
      out[mode].push(snap);
      if (mode === 'on') {
        firstTriple.push(triple);
        distinct.push(champsSeen.size);
        repeat.push(held ? Math.max(...Object.values(st.wins)) : 0);
        strongestWins.push(held ? strong / held : 0);
        let tg = 0, ti = 0;
        for (const k of Object.keys(st.goldTo)) { tg += st.goldTo[k]; }
        for (const k of Object.keys(income)) ti += income[k];
        goldShare.push(tg / Math.max(1, ti));
      }
    }
  }
  const A = k => avg(out.on.map(x => x[k])), B = k => avg(out.off.map(x => x[k]));
  console.log(`== campaign ${sc}: ${seeds} seeds × ${years} years, all AI`);
  console.log(`  factions alive at the end: off ${B('alive').toFixed(2)} / on ${A('alive').toFixed(2)}; largest faction's city share: off ${(100 * B('maxShare')).toFixed(1)}% / on ${(100 * A('maxShare')).toFixed(1)}%`);
  const tr = firstTriple.filter(Boolean);
  console.log(`  first three-time champion (金刚杵): in ${tr.length}/${seeds} runs; median after ${median(tr.map(t => t.n))} tournaments; names ${Object.entries(tr.reduce((m, t) => (m[t.name] = (m[t.name] || 0) + 1, m), {})).sort((a, b) => b[1] - a[1]).map(([k, v]) => k + '×' + v).join(' ')}`);
  console.log(`  distinct champions per run: median ${median(distinct)}, min ${Math.min(...distinct)}; most titles by one general: median ${median(repeat)}, max ${Math.max(...repeat)}`);
  console.log(`  champion from the largest faction: ${(100 * avg(strongestWins)).toFixed(1)}% of tournaments`);
  console.log(`  prize gold as a share of all factions' gross income: ${(100 * avg(goldShare)).toFixed(2)}%`);
}

function modeBracket(N) {
  N = N || 300;
  let fails = 0;
  const ok = (c, m) => { if (!c) { fails++; console.log('  FAIL ' + m); } };
  // 种子顺序
  ok(seedOrder(8).join() === '1,8,4,5,2,7,3,6', 'seedOrder(8)');
  ok(seedOrder(16).join() === '1,16,8,9,4,13,5,12,2,15,7,10,3,14,6,11', 'seedOrder(16)');
  for (let t = 0; t < N; t++) {
    const rng = SG.SeededRandom(t);
    const n = 5 + Math.floor(rng.nextDouble() * 124);
    const es = [];
    for (let i = 0; i < n; i++) es.push({ gen: { id: i, name: 'g' + i }, f: Math.floor(rng.nextDouble() * Math.max(2, n / 4)), score: Math.floor(rng.nextDouble() * 60) + 40 });
    const d1 = makeDraw(es, SG.SeededRandom(99 + t));
    const d2 = makeDraw(es.map(e => Object.assign({}, e)), SG.SeededRandom(99 + t));
    ok(d1.slots.map(e => e ? e.gen.id : -1).join() === d2.slots.map(e => e ? e.gen.id : -1).join(), 'deterministic n=' + n);
    ok(d1.S >= n && d1.S / 2 < Math.max(n, 8) + 0.1, 'size n=' + n);
    // 轮空只在前几号种子对面；不会两个轮空相遇
    for (let i = 0; i < d1.S; i += 2) {
      const a = d1.slots[i], b = d1.slots[i + 1];
      ok(a || b, 'bye vs bye n=' + n);
      if (!a || !b) ok((a || b).rank <= d1.S - d1.n, 'bye to a top seed n=' + n);
    }
    // 种子 1 与 2 只能在决赛相遇
    const p1 = d1.slots.findIndex(e => e && e.rank === 1), p2 = d1.slots.findIndex(e => e && e.rank === 2);
    if (p2 >= 0) ok(meetRound(p1, p2) === Math.log2(d1.S), 'seed 1/2 meet only in the final n=' + n);
    // 前 4 号种子在不同的四分之一区
    const nq = Math.min(4, d1.K, d1.n);
    const q = new Set(); for (let r = 1; r <= nq; r++) q.add(Math.floor(d1.slots.findIndex(e => e && e.rank === r) / (d1.S / 4)));
    ok(q.size === nq, 'top-4 seeds in distinct quarters n=' + n);
    ok(d1.slots.filter(Boolean).length === d1.n, 'everyone placed n=' + n);
  }
  console.log(`bracket self-check: ${N} random fields, ${fails ? fails + ' FAIL' : 'ALL OK'}`);
  return fails;
}

const mode = process.argv[2] || 'entrants';
if (mode === 'entrants') modeEntrants();
else if (mode === 'single') modeSingle(+process.argv[3] || 200, process.argv[4]);
else if (mode === 'campaign') modeCampaign(+process.argv[3] || 24, +process.argv[4] || 15, process.argv[5] || 'classic');
else if (mode === 'bracket') process.exitCode = modeBracket(+process.argv[3] || 300);
else console.log('modes: entrants | single [N] [scen] | campaign [seeds] [years] [scen] | bracket [N]');
