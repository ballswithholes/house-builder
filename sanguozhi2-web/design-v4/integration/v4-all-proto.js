'use strict';
/* ==========================================================================
   design-v4/integration/v4-all-proto.js — the all-on balance prototype of INTEGRATION.md §9 (one-off design tooling; game files
   are patched in memory only, never on disk, and nothing here is game code).

   It runs every v4 rule system together through ONE pipeline (econ-proto.js = SG.Mods / SG.Econ / SG.AIBudget):
     ENGAGEMENT  design-v4/engage/engage-proto.js, loaded from its own source with its main program cut off and 13 asserted
                 text hooks (income / harvest / festival / project / tribute / reserve / fame / exp / item grants go through the
                 shared pipeline instead of its own formulas);
     TITLES      titles-mod.js (port of design-v4/titles/titles-proto.js; gifts through SG.AIBudget);
     ITEMS       items-mod.js (kits, signature / ruler-unique gear, shops and restock, AI equip and buy through SG.AIBudget,
                 prize grants, gear providers);
     TOURNAMENT  tourney-mod.js (annual AI tournament with prizes, injuries and the 3-title relic);
     COMBAT      army crits (COMBAT-V4 §2, level term on 老练 level) in the tactical test.
   strategy-ai.js gets 5 asserted text hooks (city gold, harvest, population growth, defection with effLoy, auto-resolve power).

   Usage (from sanguozhi2-web/):
     node design-v4/integration/v4-all-proto.js campaign classic [seeds=48] [years=25]     variants off / all  (env VARIANTS, SEED0)
     node design-v4/integration/v4-all-proto.js campaign world   [seeds=24] [years=15]
     node design-v4/integration/v4-all-proto.js player   classic [seeds=6]  [years=20]     scripted peaceful player: v3 / all (env POL, SEED0)
     node design-v4/integration/v4-all-proto.js player   world   [seeds=4]  [years=15]
     node design-v4/integration/v4-all-proto.js tactical classic [seeds=8]                 year-8 all-on armies: v4 battle effects off / on
     node design-v4/integration/v4-all-proto.js stacks   classic                           capped and uncapped stacks (balance table)
     node design-v4/integration/v4-all-proto.js refs     classic                           SG.Econ / SG.Mods test vectors → results/econ-refs.json
   Env: VARIANTS=off,all,nocap; SEED0; SHARE (purse share, default 0.20); CAPM (purse cap in months of accrual, default 12).
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const Module = require('module');

global.window = global;
const MODE = process.argv[2] || 'campaign';
const SCEN = process.argv[3] === 'world' ? 'world' : 'classic';
const HERE = __dirname;
const DV4 = path.join(HERE, '..');

// ------------------------------------------------------------ strategy-ai.js hooks (asserted) --
function patch(src, pairs, file) {
  for (const [a, b] of pairs) { if (src.indexOf(a) < 0) throw new Error(file + ': hook text not found: ' + a.slice(0, 80)); src = src.replace(a, b); }
  return src;
}
global.__V4PATCH = {
  'strategy-ai.js': src => patch(src, [
    ['c.gold += M.roundToInt(c.industry * Balance.GoldPerIndustry + c.town * Balance.GoldPerTown);',
     'c.gold += SG.__v4 ? SG.__v4.cityGold(c) : M.roundToInt(c.industry * Balance.GoldPerIndustry + c.town * Balance.GoldPerTown);'],
    ['const harvest = M.roundToInt(c.land * Balance.FoodPerLand * SG.Random.rangeFloat(0.85, 1.15));',
     'const __roll = SG.Random.rangeFloat(0.85, 1.15); const harvest = SG.__v4 ? SG.__v4.harvest(c, __roll) : M.roundToInt(c.land * Balance.FoodPerLand * __roll);'],
    ['if (c.gold < 0) {\n          c.gold = 0;', 'if (c.gold < 0) {\n          if (SG.__neg) SG.__neg(c);\n          c.gold = 0;'],
    ['c.population = Math.min(2000000, c.population + M.roundToInt(c.population * c.town * Balance.PopGrowthPerTown / 100));',
     'c.population = Math.min(2000000, c.population + M.roundToInt(c.population * c.town * Balance.PopGrowthPerTown / 100 * (SG.__v4 ? SG.__v4.popK(c) : 1)));'],
    ['if (g.isRuler(gen) || gen.loyalty >= 40) continue;\n        const p = (40 - gen.loyalty) / 400',
     'const __L = SG.__v4 ? SG.__v4.effLoy(gen) : gen.loyalty;\n        if (g.isRuler(gen) || __L >= 40) continue;\n        const p = (40 - __L) / 400'],
    ['function power(gen) { return gen.troops * (0.55 + gen.war / 220) * (0.7 + gen.training / 330); }',
     'function power(gen) { if (SG.__v4) return SG.__v4.power(gen); return gen.troops * (0.55 + gen.war / 220) * (0.7 + gen.training / 330); }'],
  ], 'strategy-ai.js'),
};

// ------------------------------------------------------------ ENGAGEMENT from its own source (asserted hooks) --
const ENG = path.join(DV4, 'engage', 'engage-proto.js');
let esrc = fs.readFileSync(ENG, 'utf8');
esrc = esrc.slice(0, esrc.indexOf('// ------------------------------------------------------------------ 主程序 --'));
esrc = patch(esrc, [
  ["const load = (f, patch) => { let src = fs.readFileSync(path.join(root, f), 'utf8'); if (patch) src = patch(src);",
   "const load = (f, patch) => { let src = fs.readFileSync(path.join(root, f), 'utf8'); if (global.__V4PATCH && global.__V4PATCH[f]) src = global.__V4PATCH[f](src); if (patch) src = patch(src);"],
  // prestige / gear fame and event / gear virtue through SG.Mods (one cap each)
  ["  if (!E.on || !E.prestige) return;\n  const bonus = Math.min(10, Math.floor(e.prestige / 150) + e.fameEv);",
   "  if (SG.__v4) return SG.__v4.syncFV(f);\n  if (!E.on || !E.prestige) return;\n  const bonus = Math.min(10, Math.floor(e.prestige / 150) + e.fameEv);"],
  // exp multiplier before the routine cap; story exp multiplied too
  ["  if (!E.on || !E.domexp || !gen || !(amt > 0)) return;\n", "  if (!E.on || !E.domexp || !gen || !(amt > 0)) return;\n  if (SG.__v4) amt = SG.__v4.Mods.exp(gen, amt);\n"],
  ["  const r = Progress.gain(gen, amt, routine ? 'domestic' : 'story');", "  const r = Progress.gain(gen, routine || !SG.__v4 ? amt : SG.__v4.Mods.exp(gen, amt), routine ? 'domestic' : 'story');"],
  // income and harvest adjustments now inside SG.Econ (v3 endMonth hooks)
  ['    const adj = Math.round(base[i] * kI);', '    const adj = SG.__v4 ? 0 : Math.round(base[i] * kI);'],
  ['      const add = Math.round(harv(c) * kH);', '      const add = SG.__v4 ? 0 : Math.round(harv(c) * kH);'],
  // bond shields go into SG.Mods.effLoy (max with the title shield) instead of a temporary loyalty boost
  ["  const boosted = [];\n  if (E.bonds) for (const [key, bo] of g._e.bonds) {", "  const boosted = [];\n  if (E.bonds && !SG.__v4) for (const [key, bo] of g._e.bonds) {"],
  // tribute payments: an obligation, paid only above the obligation floor (SG.AIBudget 'tribute')
  ['      if (!src || !dst || src.gold < p.pay) { p.until = g.monthIndex; addRel(payer, recv, -20); continue; }\n      src.gold -= p.pay;',
   "      if (!src || !dst || src.gold < p.pay || (SG.__v4 && payer !== g.player && !SG.__v4.AIB.canSpend(src, p.pay, 'tribute'))) { p.until = g.monthIndex; addRel(payer, recv, -20); continue; }\n      if (SG.__v4 && payer !== g.player) SG.__v4.AIB.spend(src, p.pay, 'tribute'); else src.gold -= p.pay;"],
  // festivals: deferred to the discretionary phase, AI decisions through SG.AIBudget
  ['  // 节庆（各文化家族一个招牌节庆）\n  for (const F of g.factions) {', '  // 节庆（各文化家族一个招牌节庆）\n  const __fest = () => { for (const F of g.factions) {'],
  ["    const grand = isP ? (E.playerPol === 'v4' && cap.gold >= cost + 250) : cap.gold >= cost * 3;\n    const modest = !grand && cap.gold >= cost / 3 + reserveOf(cap).gold + 50 && (!isP || E.playerPol === 'v4');",
   "    const grand = isP ? (E.playerPol === 'v4' && cap.gold >= cost + 250) : SG.__v4 ? SG.__v4.AIB.canSpend(cap, cost, 'festival') : cap.gold >= cost * 3;\n    const modest = !grand && (isP || !SG.__v4 ? cap.gold >= cost / 3 + reserveOf(cap).gold + 50 : SG.__v4.AIB.canSpend(cap, Math.round(cost / 3), 'festival')) && (!isP || E.playerPol === 'v4');"],
  ['    cap.gold -= Math.round(grand ? cost : cost / 3);', "    if (SG.__v4 && !isP) SG.__v4.AIB.spend(cap, Math.round(grand ? cost : cost / 3), 'festival'); else cap.gold -= Math.round(grand ? cost : cost / 3);"],
  ['    addPrestige(F.id, Math.round((grand ? R.grand.prestige : R.modest.prestige) * temple));\n  }\n',
   '    addPrestige(F.id, Math.round((grand ? R.grand.prestige : R.modest.prestige) * temple));\n  } };\n  if (SG.__v4) SG.__v4.deferred.fest = __fest; else __fest();\n'],
  // the tournament is TOURNAMENT's (tourney-mod), run after the discretionary phase
  ['  if (g.month === 1 && g.monthIndex > g._e.start) tourney();', '  if (g.month === 1 && g.monthIndex > g._e.start) { if (SG.__v4) SG.__v4.deferred.tourney = true; else tourney(); }'],
  ['  if (E.aiProj) aiProjects();', '  if (E.aiProj && !SG.__v4) aiProjects();'],
  ['      if (c.gold < p.cost + 600) break;\n      c.gold -= p.cost;', "      if (SG.__v4 ? !SG.__v4.AIB.canSpend(c, p.cost, 'project') : c.gold < p.cost + 600) break;\n      if (SG.__v4) SG.__v4.AIB.spend(c, p.cost, 'project'); else c.gold -= p.cost;"],
  // one reserve rule (SG.AIBudget.reserve) for event options too
  ['function reserveOf(c) { const g = G(); return {', 'function reserveOf(c) { if (SG.__v4) return SG.__v4.AIB.reserve(c); const g = G(); return {'],
  // items found by events / missions / caravans / expeditions / 文会 become real instances
  ['function gotItem(f, rar, why) {', 'function gotItem(f, rar, why) { if (SG.__v4 && SG.__v4.items) SG.__v4.items.grant(f, rar, why);'],
  // AI–AI 归附 stays off in v4 (ENGAGEMENT §10.8, Q4), even with AI diplomacy on
  ['      if (F !== g.player && !E.aiDip) continue;', '      if (F !== g.player && (!E.aiDip || SG.__v4)) continue;'],
  // the scripted player starts from the prototype's game (items, titles) and keeps AI tribute / projects on
  ["  E.on = pol === 'v4' || pol === 'v4ai'; E.aiDip = false; E.aiProj = false;", "  E.on = pol === 'v4' || pol === 'v4ai'; E.aiDip = !!SG.__v4; E.aiProj = !!SG.__v4;"],
  ['  const g = newGame(key, seed);\n  const f = g.player;', '  const g = SG.__v4start ? SG.__v4start(key, seed) : newGame(key, seed);\n  const f = g.player;'],
]);
esrc += `\nreturn { E, G, rng, inc, harv, clamp, sum, avg, initState, rel, addRel, pactOf, nonAggro, bond, addAff, syncFV, addPrestige, domExp, storyExp,
  buffSum, PROJ, CAT, CS, cityLevel, capitalOf, playerRun, newGame, resetStats, applyOff, isCoast, RIVER, cnt, getCnt, neighbors, power, facInc,
  aiProjects, projReqOK, SG, GameState, C, StrategyAI, Conquest, B, M, Progress, oDev, oTrain, oRec, oSearch, oDiscord };`;
const ereq = Module.createRequire(ENG);
const EN = new Function('require', '__dirname', 'process', esrc)(ereq, path.dirname(ENG), process);
const SG = EN.SG, { E, GameState, C, StrategyAI, Conquest, B, M, Progress } = EN;
const G = () => SG.G;

// ------------------------------------------------------------ the shared pipeline --
const ECON = require('./econ-proto.js');
const ITEMSD = JSON.parse(fs.readFileSync(path.join(DV4, 'items.json'), 'utf8'));
const TITLESD = JSON.parse(fs.readFileSync(path.join(DV4, 'titles.json'), 'utf8'));
const { createItems } = require('./items-mod.js');
const { createTitles } = require('./titles-mod.js');
const { createTourney } = require('./tourney-mod.js');
const V = { on: false, battle: false, crit: true, nocap: false, stats: null };
const R = (a, b) => SG.Random.rangeInt(a, b + 1);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const avg = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);
const median = a => { const b = Float64Array.from(a).sort(); return b.length ? b[b.length >> 1] : 0; };
const maxOf = a => { let m = 0; for (const v of a) if (v > m) m = v; return m; };

let AIB, Mods, Econ, IT, TI, TO;
const P = {};   // providers (filled after the modules exist)
function build() {
  const core = ECON.create(SG, P, { share: process.env.SHARE, capMonths: process.env.CAPM });
  AIB = core.AIBudget; Mods = core.Mods; Econ = core.Econ;
  if (V.nocap) Object.assign(core.CAPS, { atk: 99, def: 99, develop: 9, incomeLo: -9, incomeHi: 9, harvestLo: -9, harvestHi: 9, recruit: 9, train: 99, search: 9, hire: 9, ally: 9, discord: 9, persuade: 9, fameBonus: 99, virtueHi: 99, exp: 9, morale: 99 });
  IT = createItems(SG, ITEMSD, AIB);
  TI = createTitles(SG, TITLESD, AIB);
  TO = createTourney(SG, {
    duelWar: x => IT.duelWar(x),
    drill: f => f >= 0 && G().citiesOf(f).some(c => c._e && c._e.done.has('drill')),
    storyExp: (x, n) => EN.storyExp(x, n),
    prestige: (f, n) => EN.addPrestige(f, n),
    fame: (f, n) => { G()._e.fac[f].fameEv += n; EN.syncFV(f); },
    grant: (f, rar, why, cult, k) => IT.grant(f, rar, why, cult, k),
    relic: (x, id) => { if (x.faction >= 0) { G()._it.arm[x.faction].push(id); G()._it.owned.add(id); IT.aiEquip(x.faction); } },
    host: () => { const g = G(); return g.factions.filter(f => f.alive).sort((a, b) => g._e.fac[b.id].prestige - g._e.fac[a.id].prestige || a.id - b.id)[0].id; },
  });
  Object.assign(P, {
    eff: (x, k) => (V.on ? IT.eff(x, k) : x[k]),
    admin: c => TI.admin(c),
    baseFame: f => G()._e.fac[f].baseFame, baseVirtue: f => G()._e.fac[f].baseVirtue,
    prestigeFame: f => Math.floor(G()._e.fac[f].prestige / 150), eventFame: f => G()._e.fac[f].fameEv,
    eventVirtue: f => G()._e.fac[f].virtueEv,
    gearFame: f => IT.P.gearFame(f), gearVirtue: f => IT.P.gearVirtue(f),
    expBoost: x => IT.P.expBoost(x),
    titleShield: x => TI.shield(x), bondShield: x => (V.shields && V.shields.get(x.id)) || 0,
    titleLoyAura: c => { const a = TI.admin(c); return a ? TI.bonus(a, 'loyAura') : 0; },
    itemLoyAura: c => IT.itemLoyAura(c, TI.admin(c)),
    gearAtk: x => IT.P.gearAtk(x), gearDef: x => IT.P.gearDef(x),
    titleAtk: x => TI.bonus(x, 'atk'), titleDef: x => TI.bonus(x, 'def'),
    cmdAtk: (m, side) => { const cu = m.commander(side); return cu && cu.alive && !(cu.confused > 0) ? TI.bonus(cu.gen, 'cmdAtk') : 0; },
    cmdDef: (m, side) => { const cu = m.commander(side); return cu && cu.alive && !(cu.confused > 0) ? TI.bonus(cu.gen, 'cmdDef') : 0; },
    bondAtk: (u, m) => bondBattle(u, m).atk, bondDef: (u, m) => bondBattle(u, m).def,
    auraAtk: u => IT.P.auraAtk(u),
    gearMorale: x => IT.P.gearMorale(x), titleMorale: x => TI.bonus(x, 'morale'),
    gearCrit: x => IT.P.gearCrit(x), titleCrit: x => TI.bonus(x, 'critArmy'), gearCritMul: x => IT.P.gearCritMul(x),
    vetLevel: x => Progress.levelFor(x.exp - 0.5 * (x._dexp || 0)),
    gearDev: (x, key) => IT.P.gearDev(x, key), titleDev: x => TI.bonus(x, 'dev'),
    project: (c, key) => projectFx(c, key),
    titleCity: (a, key) => TI.bonus(a, key), titleNat: (f, key) => TI.natBonus(f, key),
    itemCity: (c, key) => IT.itemCity(c, TI.admin(c), key),
    order: c => (c._e && E.order ? c._e.order : 50),
    pactIncome: c => pactIncome(c),
    eventIncome: c => (c._e ? EN.buffSum(c, 'income') : 0),
    eventHarvest: c => (c._e ? EN.buffSum(c, 'harvest') : 0),
    gearRecruit: x => IT.P.gearRecruit(x), gearTrain: x => IT.P.gearTrain(x), titleTrain: x => TI.bonus(x, 'train'),
    gearSearch: x => IT.P.gearSearch(x), titleSearch: x => TI.bonus(x, 'search'),
    gearHire: x => IT.P.gearHire(x), titleHire: x => TI.bonus(x, 'hire'),
    gearDip: x => IT.P.gearDip(x), titleDip: x => TI.bonus(x, 'dip'), rel: (f, t) => EN.rel(f, t),
    gearDiscord: x => IT.P.gearDiscord(x), titleDiscord: x => TI.bonus(x, 'discord'),
    gearPersuade: x => IT.P.gearPersuade(x), titlePersuade: x => TI.bonus(x, 'persuade'), persuadeRes: x => TI.persuadeRes(x),
  });
}
// ENGAGEMENT project effects by key (engagement.json projects[].fx): income / harvest / dev:industry (铁官) / recruit (铁官) / train / search
function projectFx(c, key) {
  if (!c._e) return 0;
  let v = 0;
  for (const id of c._e.done) {
    const p = EN.PROJ[id]; if (!p) continue;
    if (key === 'income' && p.fx.income) v += p.fx.income;
    if (key === 'harvest' && p.fx.harvest) v += p.fx.harvest;
    if (key === 'dev:industry' && id === 'forge') v += 0.10;
    if (key === 'recruit' && id === 'forge') v += 0.10;   // INTEGRATION §4.4: 铁官 is +10 % troops per gold inside the cap (was −10 % gold)
    if (key === 'train' && id === 'drill') v += 3;
    if (key === 'search' && id === 'academy') v += 0.06;
  }
  return v;
}
function pactIncome(c) {
  const g = G(); if (c.owner < 0 || !g._e) return 0;
  for (const j of c.links) { const o = g.cities[j].owner; if (o >= 0 && o !== c.owner && EN.pactOf(c.owner, o, 'trade')) return 0.03; }
  return 0;
}
// bonds in tactical battles (ENGAGEMENT §11.3), capped ±8 %
function bondBattle(u, m) {
  if (!V.battle || !G()._e) return { atk: 0, def: 0 };
  let a = 0, d = 0;
  for (const o of m.units) {
    if (o === u || o.side !== u.side || !o.alive) continue;
    const bo = EN.bond(u.gen, o.gen); if (!bo || !bo.k) continue;
    if (bo.k === 'sworn') { a += 0.04; d += 0.04; } else if (bo.k === 'friend' || bo.k === 'spouse') a += 0.02; else if (bo.k === 'rival') a += 0.03; else if (bo.k === 'feud') { a -= 0.04; d -= 0.04; }
  }
  return { atk: clamp(a, -0.08, 0.08), def: clamp(d, -0.08, 0.08) };
}

// ------------------------------------------------------------ the hooks SG.__v4 exposes to the patched sources --
function makeV4() {
  SG.__v4 = {
    Mods, AIB, items: IT, deferred: {},
    cityGold(c) { const r = Econ.cityGold(c); V.stats.incomePct.push(r.pct); if (r.pct >= Mods.CAPS.incomeHi - 1e-9) V.stats.incomeAtCap++; V.stats.incomeN++; return r.gold; },
    harvest(c, roll) { const r = Econ.harvest(c, roll); if (c.owner === G().player) V.stats.harv.push({ rollRaw: roll, land: c.land, h: r.h, pct: r.pct }); return r.h; },
    popK(c) { const a = TI.admin(c); return 1 + (a ? TI.bonus(a, 'pop') : 0); },
    effLoy: x => Mods.effLoy(x),
    power(gen) {
      const atk = IT.raw(gen, 'atk'), def = IT.raw(gen, 'def');
      return gen.troops * (0.55 + IT.eff(gen, 'war') / 220) * (0.7 + gen.training / 330) * (1 + (Math.min(40, atk) + Math.min(45, def)) / 250);
    },
    syncFV(f) { const F = G().factions[f]; F.fame = Mods.fame(f); F.virtue = Mods.virtue(f); },
  };
}
function installWrappers() {
  // garrison (TITLES §7.1) on top of ENGAGEMENT's walls / buffs
  const oCD = SG.cityDefense;
  SG.cityDefense = function (c) { const d = oCD(c); return V.on ? d + TI.garrison(c) : d; };
  // battle exp multiplier
  const oGain = Progress.gain;
  Progress.gain = function (gen, amount, why) { if (V.on && why === 'battle') amount = Mods.exp(gen, amount); return oGain.call(this, gen, amount, why); };
  // vetK on 老练 level (ENGAGEMENT §4.4)
  const oVet = Progress.vetK;
  Progress.vetK = function (gen) { if (!V.on || !Progress.enabled || !gen) return oVet.call(this, gen); const L = P.vetLevel(gen); return L > 1 ? 1 + Progress.VET_PER_LEVEL * (Math.min(L, Progress.MAX_LEVEL) - 1) : 1; };
}

// ------------------------------------------------------------ Commands through SG.Econ --
function installCommands() {
  const v3 = { develop: C.develop, recruitMax: C.recruitMax, recruit: C.recruit, train: C.train, search: C.search, hireChance: C.hireChance, allyChance: C.allyChance, discord: C.discord, persuadeChance: C.persuadeChance, engDevelop: C.develop, engRecruit: C.recruit, engTrain: C.train, engSearch: C.search, engDiscord: C.discord };
  C.develop = function (c, g, kind) {
    if (!V.on) return v3.engDevelop.apply(this, arguments);
    if (c.gold < B.DevelopCost) return '金不足，无法开发。';
    c.gold -= B.DevelopCost;
    const roll = R(0, 4);
    const key = kind === SG.DevKind.Land ? 'land' : kind === SG.DevKind.Industry ? 'industry' : 'town';
    const r = Econ.develop(c, g, key, roll);
    const before = c[key]; c[key] = Math.min(B.StatMax, c[key] + r.gain);
    if (c.owner === G().player) V.stats.dev.push({ key, before, after: c[key], g0: r.g0, gain: c[key] - before, roll, pct: r.pct, city: c });
    V.stats.devPct.push(r.pct);
    EN.domExp(g, 6 + (r.g0 >= 20 ? 2 : 0));
    return 'ok';
  };
  C.recruitMax = function (c, g) {
    if (!V.on) return v3.recruitMax.apply(this, arguments);
    const room = SG.maxTroops(g) - g.troops;
    const byGold = Math.floor(Math.min(c.gold, B.RecruitGoldMax) * Econ.troopsPerGold(c, g));
    return Math.max(0, Math.min(room, Math.min(byGold, M.idiv(c.population, 10))));
  };
  C.recruit = function (c, g, troops) {
    if (!V.on) return v3.engRecruit.apply(this, arguments);
    troops = Math.min(troops, C.recruitMax(c, g));
    if (troops <= 0) return '';
    const cost = Math.ceil(troops / Econ.troopsPerGold(c, g));
    c.gold -= cost; c.population -= troops;
    const total = g.troops + troops;
    g.training = M.idiv(g.training * g.troops + ((c._e && c._e.done.has('drill')) ? 40 : 30) * troops, Math.max(1, total));
    g.troops = total;
    EN.domExp(g, 3);
    if (E.order && c._e) c._e.order = clamp(c._e.order - Math.ceil(troops / 3000), 0, 100);
    return 'ok';
  };
  C.train = function (g) {
    if (!V.on) return v3.engTrain.apply(this, arguments);
    if (g.troops <= 0) return '';
    g.training = Math.min(100, Econ.trainGain(g, R(0, 4), G().cities[g.city]) + g.training);
    EN.domExp(g, 3);
    return 'ok';
  };
  C.search = function (c, g) {
    if (!V.on) return v3.engSearch.apply(this, arguments);
    C.searchFound = null;
    const hidden = G().generals.filter(x => x.city === c.id && x.hidden && SG.isFree(x));
    const p = Econ.searchP(c, g);
    V.stats.searchP.push(p);
    if (hidden.length > 0 && SG.Random.value() < p) {
      const h = hidden[SG.Random.rangeInt(0, hidden.length)];
      h.hidden = false; C.searchFound = h; EN.domExp(g, 12); if (g.faction >= 0) EN.cnt(g.faction, 'hiddenFound', 1); return 'found';
    }
    const r = SG.Random.value();
    if (r < 0.25) c.gold += R(60, 220); else if (r < 0.45) c.food += R(800, 2500);
    EN.domExp(g, 4);
    return '';
  };
  C.hireChance = function (target, recruiter, faction) { return V.on ? Econ.hireP(target, recruiter, faction, 0) : v3.hireChance.apply(this, arguments); };
  C.allyChance = function (f, target, gift) { if (!V.on) return v3.allyChance.apply(this, arguments); const p = Econ.allyP(f, target, gift, null); V.stats.allyP.push(p); return p; };
  C.persuadeChance = function (agent, target, f) { return V.on ? Econ.persuadeP(agent, target, f) : v3.persuadeChance.apply(this, arguments); };
  C.discord = function (agent, target) {
    if (!V.on) return v3.engDiscord.apply(this, arguments);
    if (G().isRuler(target)) return '';
    const before = target.loyalty;
    const p = Econ.discordP(agent, target);
    if (SG.Random.value() > p) { EN.domExp(agent, 2); EN.addRel(agent.faction, target.faction, -5); return ''; }
    let d = M.idiv(R(6, 16) * IT.eff(agent, 'intel'), Math.max(30, IT.eff(target, 'intel')));
    d = M.clamp(d, 3, 25);
    if (target._t) d = Math.max(1, Math.round(d * (1 - 0.08 * target._t.tier)));
    target.loyalty = Math.max(0, target.loyalty - d);
    EN.domExp(agent, target.loyalty < before ? 6 : 2); EN.addRel(agent.faction, target.faction, -5);
    return 'ok';
  };
}

// ------------------------------------------------------------ battle (tactical test) --
function installBattle() {
  const BP = SG.BattleModel.prototype;
  const oAtk = BP.atkPower, oDef = BP.defFactor;
  BP.atkPower = function (u) { const v = oAtk.call(this, u); return V.battle ? v * Mods.atkMul(u, this) : v; };
  BP.defFactor = function (u) { const v = oDef.call(this, u); return V.battle ? v * Mods.defMul(u, this) : v; };
  BP.attack = function (a, t) {
    const r = { dmg: 0, counter: 0, targetRouted: false, selfRouted: false, crit: false };
    const roll = SG.Random.value();                                    // COMBAT-V4 §2.2: one roll first, always
    const crit = V.battle && V.crit && roll < Mods.critChance(a);
    let d = this.atkPower(a) * B.DamageK / this.defFactor(t) * SG.Random.rangeFloat(0.85, 1.15);
    if (crit) d *= Mods.critK(a);
    r.dmg = Math.min(Math.max(10, M.roundToInt(d)), t.troops); t.troops -= r.dmg; r.crit = crit;
    if (V.tstats) { V.tstats.atk++; if (crit) V.tstats.crit++; V.tstats.dmg += r.dmg; }
    t.morale = Math.max(0, t.morale - M.roundToInt(r.dmg * 40 / Math.max(200, t.troops + r.dmg)) - (crit ? 5 : 0));
    r.targetRouted = this.checkRout(t);
    if (!r.targetRouted) {
      r.counter = Math.max(5, M.roundToInt(this.atkPower(t) * B.DamageK * B.CounterRatio * this.modK(t, 'counter') / this.defFactor(a) * SG.Random.rangeFloat(0.85, 1.15)));
      r.counter = Math.min(r.counter, a.troops); a.troops -= r.counter;
      if (V.tstats) V.tstats.dmg += r.counter;
      a.morale = Math.max(0, a.morale - M.roundToInt(r.counter * 30 / Math.max(200, a.troops + r.counter)));
      r.selfRouted = this.checkRout(a);
    }
    a.morale = Math.min(100, a.morale + (r.dmg > r.counter ? 3 : 0));
    this.xpHit(a, t, r.dmg); if (r.counter > 0) this.xpHit(t, a, r.counter);
    this.checkEnd();
    return r;
  };
  const oAP = BP.actionPoints;
  BP.actionPoints = function (s) { if (!V.battle) return oAP.call(this, s); const f = s === 0 ? this.setup.attacker : this.setup.defender; return Mods.actionPoints(f); };
}

// ------------------------------------------------------------ the endMonth pipeline (INTEGRATION §6.3) --
function bondShields() {
  const g = G(), m = new Map();
  if (!E.bonds || !g._e) return m;
  for (const [key, bo] of g._e.bonds) {
    if (!['sworn', 'kin', 'spouse'].includes(bo.k)) continue;
    const [ia, ib] = key.split('-').map(Number), a = g.generals[ia], b = g.generals[ib];
    if (a.dead || b.dead || a.faction < 0 || a.faction !== b.faction) continue;
    const s = bo.k === 'sworn' ? 30 : 15;
    m.set(a.id, Math.max(m.get(a.id) || 0, s)); m.set(b.id, Math.max(m.get(b.id) || 0, s));
  }
  return m;
}
function facGross(f) { let s = 0; for (const c of G().citiesOf(f)) s += c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown; return s; }
function installEndMonth() {
  const eEnd = StrategyAI.endMonth;          // ENGAGEMENT's wrapper around v3 (both patched)
  StrategyAI.endMonth = function () {
    if (!V.on) return eEnd.apply(this, arguments);
    const g = G();
    V.shields = bondShields();
    const gross = g.factions.map(f => (f.alive ? facGross(f.id) : 0));
    const news = eEnd.apply(this, arguments);           // 1 income / harvest (SG.Econ), salary, food, gold < 0 rule, population; 4 defections (effLoy); ENGAGEMENT monthEnd (order, projects, tribute, events, 文会)
    // 2 ITEMS restock and monthly item effects
    IT.monthEnd();
    // 5 loyAura (TITLES / ITEMS, the larger, ≤ 95)
    for (const c of g.cities) { if (c.owner < 0) continue; const la = Mods.loyAura(c); if (la > 0) for (const o of g.officersIn(c)) if (!g.isRuler(o) && o.loyalty < Mods.CAPS.loyAuraCap) o.loyalty = Math.min(Mods.CAPS.loyAuraCap, o.loyalty + la); }
    // 6 Titles.monthStart
    TI.monthStart();
    // 7 discretionary phase, fixed order: title gifts → gear → projects → festivals
    for (const f of g.factions) {
      if (!f.alive) continue;
      AIB.accrue(f.id, gross[f.id]);
      if (f.id === g.player && V.playerPol !== 'all') continue;
      TI.aiManage(f.id);
      IT.aiEquip(f.id);
      if (f.id !== g.player || V.playerShops) IT.aiPhase(f.id);
    }
    if (E.aiProj) EN.aiProjects();
    if (SG.__v4.deferred.fest) { SG.__v4.deferred.fest(); SG.__v4.deferred.fest = null; }
    // 8 TOURNAMENT (Node: autoAll in month 1)
    if (SG.__v4.deferred.tourney) { SG.__v4.deferred.tourney = false; TO.run(); }
    for (const F of g.factions) if (F.alive) EN.syncFV(F.id);
    return news;
  };
  // a destroyed faction's armoury goes to the conqueror (ITEMS §8.8)
  const oApply = Conquest.apply;
  Conquest.apply = function (s) {
    const owner = s.target.owner;
    const r = oApply.apply(this, arguments);
    if (V.on && owner >= 0 && !G().factions[owner].alive && s.attackerWon) IT.transferArmoury(owner, s.attacker);
    return r;
  };
}

// ------------------------------------------------------------ runs --
function newStats() {
  return { incomePct: [], incomeAtCap: 0, incomeN: 0, devPct: [], searchP: [], allyP: [], dev: [], harv: [], neg: 0, negDisc: 0 };
}
function setVariant(v) {
  V.on = v !== 'off'; V.nocap = v === 'nocap';
  build();
  EN.applyOff();
  E.on = V.on; E.aiDip = V.on; E.aiProj = V.on; E.playerPol = null;
  V.stats = newStats();
  EN.resetStats();
  if (V.on) makeV4(); else SG.__v4 = null;
}
let installed = false;
function ensureInstalled() { if (installed) return; installed = true; installWrappers(); installCommands(); installBattle(); installEndMonth(); SG.__v4start = (key, seed) => startGame(key, seed); }
SG.__neg = c => { if (V.stats) V.stats.neg++; };

function startGame(key, seed) {
  const g = EN.newGame(key, seed);     // ENGAGEMENT initState (relations, bonds, order)
  if (V.on) { g._dvar = 1; IT.init(g); TI.monthStart(); for (const f of g.factions) if (f.alive && f.id !== g.player) TI.aiManage(f.id); for (const F of g.factions) if (F.alive) EN.syncFV(F.id); }
  return g;
}
function snapshotStacks(g) {
  // the strongest non-level multipliers present on the map (one unit, as a lone commander)
  let best = { atk: 1, def: 1, who: '' };
  const fake = { commander: () => null, units: [] };
  for (const x of g.generals) {
    if (x.dead || x.faction < 0) continue;
    const u = { gen: x, side: 0, alive: true };
    fake.commander = () => ({ gen: x, alive: true });
    const a = Mods.atkMul(u, fake), d = Mods.defMul(u, fake);
    if (a * d > best.atk * best.def) best = { atk: a, def: d, who: x.name };
  }
  return best;
}
function campaign(seed, years, v) {
  const key = SCEN === 'world' ? 'roma' : 'liubei';
  const g = startGame(key, seed);
  g.player = -1;
  const out = { alive: {}, top: {}, f25: null, lv: {}, vk: {}, gold: {}, stacks: null, fame: {}, ap: {}, dev: 0, defect0: E.stats.defect, gearShare: 0 };
  const total = g.cities.length;
  for (let m = 0; m < years * 12; m++) {
    StrategyAI.runAI([]);
    StrategyAI.endMonth();
    if (out.f25 === null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > total * 0.25) { out.f25 = (m + 1) / 12; break; }
    const y = (m + 1) / 12;
    if ([1, 3, 5, 10, 15, 20, 25].includes(y)) {
      out.alive[y] = g.factions.filter(f => f.alive).length;
      out.top[y] = Math.max(...g.factions.map(f => g.cityCount(f.id)));
      const gs = g.generals.filter(x => x.faction >= 0 && !x.dead);
      out.lv[y] = avg(gs.map(x => x.level));
      out.vk[y] = avg(gs.map(x => Progress.vetK(x)));
      out.gold[y] = avg(g.cities.filter(c => c.owner >= 0).map(c => c.gold));
      const alive = g.factions.filter(f => f.alive);
      out.fame[y] = V.on ? avg(alive.map(f => Mods.fameBonus(f.id))) : 0;
      out.ap[y] = avg(alive.map(f => (V.on ? Mods.actionPoints(f.id) : M.clamp(B.ActionPointsBase + M.idiv(f.fame, B.ActionPointsFameDiv), 2, B.ActionPointsMax))));
      if (y === 10 && V.on) out.stacks = snapshotStacks(g);
    }
  }
  out.defect = E.stats.defect - out.defect0;
  return out;
}
function runCampaigns(seeds, years) {
  ensureInstalled();
  const variants = (process.env.VARIANTS || 'off,all').split(',');
  const s0 = +(process.env.SEED0 || 0);
  const res = {};
  const t0 = Date.now();
  for (const v of variants) {
    setVariant(v);
    const runs = [];
    for (let s = 1; s <= seeds; s++) runs.push(campaign(s0 + s, years, v));
    res[v] = { runs, stats: V.stats, aib: AIB.stats, items: IT.ST, titles: TI.T, tourney: TO.ST, eng: E.stats };
  }
  const ys = [1, 3, 5, 10, 15, 20, 25].filter(y => y <= years);
  const lines = [];
  const log = s => { lines.push(s); console.log(s); };
  log(`== all-on campaign ${SCEN}: ${seeds} seeds (SEED0=${s0}) × ${years} y, all-AI; variants ${variants.join(' / ')}  [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
  for (const v of variants) {
    const { runs, stats, aib, items, titles, tourney } = res[v];
    const f25 = runs.map(r => (r.f25 === null ? 99 : r.f25)).sort((a, b) => a - b);
    const casc = runs.filter(r => r.defect >= 10).length;
    log(`[${v}] alive ${ys.map(y => 'y' + y + ' ' + avg(runs.map(r => r.alive[y])).toFixed(2)).join(', ')}`);
    log(`      largest faction ${ys.map(y => 'y' + y + ' ' + avg(runs.map(r => r.top[y])).toFixed(1)).join(', ')}; first > 25 %: median ${f25[f25.length >> 1].toFixed(2)} y`);
    log(`      defections per seed ${avg(runs.map(r => r.defect)).toFixed(2)} (max ${Math.max(...runs.map(r => r.defect))}); cascades (≥ 10) ${casc}/${runs.length}; v3 "gold < 0" penalties per seed ${(stats.neg / runs.length).toFixed(1)}`);
    log(`      level avg ${ys.map(y => avg(runs.map(r => r.lv[y])).toFixed(2)).join(' / ')}; 老练 avg ${ys.map(y => avg(runs.map(r => r.vk[y])).toFixed(3)).join(' / ')}; city gold avg ${ys.map(y => avg(runs.map(r => r.gold[y])).toFixed(0)).join(' / ')}`);
    log(`      battle AP avg ${ys.map(y => avg(runs.map(r => r.ap[y])).toFixed(2)).join(' / ')}` + (v !== 'off' ? `; fame bonus avg ${ys.map(y => avg(runs.map(r => r.fame[y])).toFixed(2)).join(' / ')}` : ''));
    if (v !== 'off') {
      const g = aib.gross || 1;
      const share = k => (100 * (aib.byKind[k] || 0) / g).toFixed(2) + '%';
      const disc = ['title', 'gear', 'project', 'festival'].reduce((s, k) => s + (aib.byKind[k] || 0), 0);
      log(`      AI spending / gross income: gear ${share('gear')}, title gifts ${share('title')}, projects ${share('project')}, festivals ${share('festival')}, tribute ${share('tribute')}; discretionary total ${(100 * disc / g).toFixed(2)}%; refused ${JSON.stringify(aib.refused)}; cities below 0 after a discretionary spend: ${aib.below0}`);
      log(`      items: bought ${JSON.stringify(items.buys)}, sold ${items.sold}, grants ${JSON.stringify(items.grants)}, hidden found ${items.hiddenFound}; titles granted ${titles.grants}; tournaments ${tourney.held} (entrants ${(tourney.entrants / Math.max(1, tourney.held)).toFixed(1)}, injuries ${tourney.injuries}, relic ${tourney.relic}, ${(tourney.ms / Math.max(1, tourney.held)).toFixed(1)} ms each)`);
      log(`      income pct median ${median(stats.incomePct).toFixed(3)}, at the +35 % cap ${(100 * stats.incomeAtCap / Math.max(1, stats.incomeN)).toFixed(2)}% of city-months; develop pct median ${median(stats.devPct).toFixed(3)} max ${maxOf(stats.devPct).toFixed(2)}; search p max ${maxOf(stats.searchP).toFixed(3)}; ally p max ${maxOf(stats.allyP).toFixed(3)}`);
      const st = runs.filter(r => r.stacks).map(r => r.stacks);
      if (st.length) log(`      strongest non-level stack on the map at y10 (lone commander): atk ×${avg(st.map(s => s.atk)).toFixed(3)} def ×${avg(st.map(s => s.def)).toFixed(3)} (max atk ×${Math.max(...st.map(s => s.atk)).toFixed(3)}, def ×${Math.max(...st.map(s => s.def)).toFixed(3)}; e.g. ${st[0].who})`);
    }
  }
  fs.writeFileSync(path.join(HERE, 'results', `campaign-${SCEN}-${seeds}x${years}-s${s0}${process.env.VARIANTS ? '-' + process.env.VARIANTS.replace(/,/g, '_') : ''}.txt`), lines.join('\n') + '\n');
}

// ---- scripted peaceful player (ENGAGEMENT §19.3 harness): v3 domestic script vs the same script with everything on ----
function runPlayers(seeds, years) {
  ensureInstalled();
  const keys = (process.env.PLAYERS || (SCEN === 'world' ? 'goguryeo,yamatai,khotan,satavahana,caledonia,saba' : 'liubei,cao,kongrong,sunjian,taoqian,menghuo')).split(',');
  const pols = (process.env.POL || 'v3,all').split(',');
  const s0 = +(process.env.SEED0 || 0);
  const ys = [1, 3, 5, 10, 15, 20].filter(y => y <= years);
  const lines = []; const log = s => { lines.push(s); console.log(s); };
  log(`== all-on player ${SCEN}: ${keys.length} rulers × ${seeds} seeds × ${years} y (SEED0=${s0}); policies ${pols.join(' / ')}`);
  for (const pol of pols) {
    setVariant(pol === 'v3' ? 'off' : 'all');
    V.playerPol = pol; V.playerShops = pol === 'all';
    const runs = [];
    for (const key of keys) for (let s = 1; s <= seeds; s++) {
      const seed = s0 + s * 7 + key.length;
      const r = EN.playerRun(key, seed, years, pol === 'v3' ? 'v3' : 'v4');   // ENGAGEMENT's harness; its game comes from startGame (SG.__v4start)
      runs.push(Object.assign(r, { key }));
    }
    const at = y => runs.filter(r => r.months >= y * 12);
    log(`[${pol}] survival ${ys.map(y => 'y' + y + ' ' + (100 * at(y).length / runs.length).toFixed(0) + '%').join(' / ')}`);
    log(`      cities (survivors) ${ys.map(y => avg(at(y).map(r => r.cities[y])).toFixed(1)).join(' / ')}; income ${ys.map(y => avg(at(y).map(r => r.income[y])).toFixed(0)).join(' / ')}; level ${ys.map(y => avg(at(y).map(r => r.lv[y])).toFixed(2)).join(' / ')}`);
    if (pol !== 'v3') {
      log(`      prestige ${ys.map(y => avg(at(y).map(r => r.prestige[y])).toFixed(0)).join(' / ')}; order ${ys.map(y => avg(at(y).map(r => r.order[y])).toFixed(1)).join(' / ')}`);
      const vt = k => { const v = runs.map(r => r.vlog[k]).filter(x => x !== undefined).map(x => x / 12).sort((a, b) => a - b); return v.length ? `${v.length}/${runs.length} (${(100 * v.length / runs.length).toFixed(0)}%) median ${v[v.length >> 1].toFixed(1)} y` : `0/${runs.length}`; };
      log(`      王道 (prestige 1 500, all held 6 months): ${vt('win1500')}`);
      // DOMESTIC-FX §4.2 tiers inside the all-on game (rollRaw / pre-multiplier gain rule)
      const dv = V.stats.dev.filter(d => d.before < 999);   // the real game disables 开发 at 999
      let great = 0, good = 0;
      for (const d of dv) {
        const thr = [300, 450, 600, 800];
        const st = v => thr.filter(t => v >= t).length;
        if (st(d.after) > st(d.before) || (d.before < 999 && d.after >= 999)) great++;
        else if (d.roll === 4 || Math.floor((d.before + d.g0) / 100) > Math.floor(d.before / 100)) good++;
      }
      log(`      开发 tiers (player, n ${dv.length}): great ${(100 * great / Math.max(1, dv.length)).toFixed(1)}%, good ${(100 * good / Math.max(1, dv.length)).toFixed(1)}%; mean pct ${avg(dv.map(d => d.pct)).toFixed(3)}; mean gain ${avg(dv.map(d => d.gain)).toFixed(1)} (g0 ${avg(dv.map(d => d.g0)).toFixed(1)})`);
      const hv = V.stats.harv; const lean = hv.filter(h => h.rollRaw < 0.90).length, gre = hv.filter(h => h.rollRaw >= 1.10 && h.land >= 600).length, goo = hv.filter(h => !(h.rollRaw >= 1.10 && h.land >= 600) && (h.rollRaw >= 1.08 || h.land >= 700)).length;
      const gNew = hv.filter(h => h.rollRaw >= 1.04 && !(h.rollRaw >= 1.10 && h.land >= 600)).length;
      log(`      秋收 tiers, consolidated rule (good = rollRaw ≥ 1.04, no land term): good ${(100 * gNew / Math.max(1, hv.length)).toFixed(1)}%`);
      log(`      秋收 tiers on rollRaw (n ${hv.length}): lean ${(100 * lean / Math.max(1, hv.length)).toFixed(1)}%, good ${(100 * goo / Math.max(1, hv.length)).toFixed(1)}%, great ${(100 * gre / Math.max(1, hv.length)).toFixed(1)}%; same records tiered on harvest / (land × 2.2): ≥ 1.10 ${(100 * hv.filter(h => h.h / (h.land * 2.2) >= 1.10).length / Math.max(1, hv.length)).toFixed(1)}%, < 0.90 ${(100 * hv.filter(h => h.h / (h.land * 2.2) < 0.90).length / Math.max(1, hv.length)).toFixed(1)}%`);
    }
  }
  fs.writeFileSync(path.join(HERE, 'results', `player-${SCEN}-${seeds}x${years}-s${s0}.txt`), lines.join('\n') + '\n');
}

// ---- tactical: year-8 all-on armies, v4 battle effects off / on (sim.js (7g)-style) ----
function fightAI(Mdl) {
  let guard = 0;
  while (Mdl.result === 0 && guard++ < 2000) {
    for (let pass = 0; pass < 4 && Mdl.result === 0; pass++) {
      const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u));
      if (!order.length) break;
      for (const u of order) {
        if (!Mdl.canAct(u) || Mdl.result !== 0) continue;
        const plan = Mdl.planFor(u); if (plan.move) Mdl.move(u, plan.move); if (Mdl.result !== 0) break;
        const a = Mdl.planAction(u);
        if (a.kind === 'attack') Mdl.attack(u, a.target); else if (a.kind === 'tactic') Mdl.useTactic(u, a.tactic, a.target); else if (a.kind === 'duel') Mdl.duel(u, a.target); else if (a.kind === 'special') Mdl.useSpecial(u, a.target);
        Mdl.spend(u);
      }
    }
    if (Mdl.result === 0) Mdl.endSide();
  }
}
function runTactical(seeds) {
  ensureInstalled();
  setVariant('all');
  const snaps = [];
  for (let s = 1; s <= seeds; s++) {
    const g = startGame(SG.ScenarioData.Factions[0].split('|')[0], s);
    g.player = -1;
    for (let m = 0; m < 96; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
    snaps.push({ g: JSON.stringify(g), gens: g.generals.map(x => ({ gear: x._gear, t: x._t || null, dexp: x._dexp || 0, inj: x.injury || null })), e: g._e });
  }
  const modes = ['off', 'on', 'onNoCrit'];
  const out = {};
  for (const mode of modes) {
    let n = 0, w = 0, days = 0, lossA = 0, lossD = 0, dmg = 0;
    V.tstats = { atk: 0, crit: 0, dmg: 0 };
    snaps.forEach((sn, si) => {
      for (let k = 0; k < 60; k++) {
        const g = SG.G = GameState.fromJSON(sn.g);
        g.player = -1; g._e = sn.e;
        g.generals.forEach((x, i) => { x._gear = sn.gens[i].gear; if (sn.gens[i].t) x._t = sn.gens[i].t; x._dexp = sn.gens[i].dexp; if (sn.gens[i].inj) x.injury = sn.gens[i].inj; });
        SG.Random.seed(900 + si * 100 + k);
        const cand = [];
        for (const c of g.cities) { if (c.owner < 0) continue; for (const j of c.links) { const t = g.cities[j]; if (t.owner >= 0 && t.owner !== c.owner && g.officersIn(c).some(x => x.troops > 500) && g.officersIn(t).length) cand.push([c, t]); } }
        if (!cand.length) continue;
        const [c, t] = cand[(k * 5 + si) % cand.length];
        const atk = SG.Seq.orderByDesc(g.officersIn(c).filter(x => x.troops > 0), x => x.troops).slice(0, 5);
        const s = Conquest.prepare(c.owner, c, t, atk, 3000, 0);
        V.battle = mode !== 'off'; V.crit = mode === 'on';
        const Mdl = new SG.BattleModel(s);
        if (V.battle) for (const u of Mdl.units) u.morale = Mods.startMorale(u, u.morale);
        const t0s = Mdl.units.map(u => u.troops);
        fightAI(Mdl);
        V.battle = false;
        n++; days += Mdl.day; if (Mdl.result === 1) w++;
        Mdl.units.forEach((u, i) => { const l = Math.max(0, t0s[i] - Math.max(0, u.troops)); if (u.side === 0) lossA += l; else lossD += l; dmg += l; });
      }
    });
    out[mode] = { n, w: w / Math.max(1, n), days: days / Math.max(1, n), x: lossD / Math.max(1, lossA), dmg: dmg / Math.max(1, n), crit: V.tstats.crit / Math.max(1, V.tstats.atk) };
  }
  const lines = []; const log = s => { lines.push(s); console.log(s); };
  log(`== tactical ${SCEN}: year-8 all-on armies from ${seeds} campaigns, ${out.off.n} battles per mode (gear, titles, bonds, crits, AP; sim.js (7g) style)`);
  for (const m of modes) log(`  v4 battle effects ${m.padEnd(8)}: attacker win ${(100 * out[m].w).toFixed(1)}%, days ${out[m].days.toFixed(1)}, def/atk losses ${out[m].x.toFixed(2)}, troops lost per battle ${out[m].dmg.toFixed(0)}, crit rate ${(100 * out[m].crit).toFixed(1)}%`);
  log(`  shift on − off ${(100 * (out.on.w - out.off.w)).toFixed(1)} pp (target ≤ 4); damage per battle on / off ×${(out.on.dmg / out.off.dmg).toFixed(3)} (target ≤ 1.35)`);
  fs.writeFileSync(path.join(HERE, 'results', `tactical-${SCEN}-${seeds}.txt`), lines.join('\n') + '\n');
}

// ---- stacks: the capped and uncapped extremes of the balance table (INTEGRATION §8) ----
function runStacks() {
  ensureInstalled();
  const lines = []; const log = s => { lines.push(s); console.log(s); };
  for (const nocap of [true, false]) {
    setVariant(nocap ? 'nocap' : 'all');
    const g = startGame('liubei', 1); g.player = -1;
    const I = id => IT.ITEM[id];
    const gy = g.generals.find(x => x.name === '关羽'), xy = g.generals.find(x => x.name === '荀彧') || g.generals.find(x => x.pol >= 95);
    // best-in-slot by atk / def from the whole catalogue (any route), legendary weapon
    const best = (slot, key) => ITEMSD.items.filter(it => it.slot === slot && !(it.req && it.req.weaponStyle)).sort((a, b) => ((b.stats || {})[key] || 0) - ((a.stats || {})[key] || 0))[0];
    gy._gear = { weapon: 'vajra', offhand: best('offhand', 'def') ? best('offhand', 'def').id : null, armour: best('armour', 'def').id, helmet: best('helmet', 'def').id, mount: 'chitu', treasure: best('treasure', 'atk') ? best('treasure', 'atk').id : null };
    gy.exp = 2160; gy.level = 10; gy._dexp = 0;
    gy._t = { post: 'w5', tier: 5, family: 'war', since: 1, faction: gy.faction, suspended: false, month: -1 };
    const fake = { commander: () => ({ gen: gy, alive: true }), units: [] };
    const u = { gen: gy, side: 0, alive: true, formation: 5, morale: 95 };
    V.battle = true;
    // a sworn brother in the same army (+4 % / +4 %) via the bond provider
    P.bondAtk = () => 0.04; P.bondDef = () => 0.04;
    const a = Mods.atkMul(u, fake), d = Mods.defMul(u, fake);
    const vet = 1 + 0.04 * 9;
    log(`[${nocap ? 'uncapped' : 'capped'}] 关羽 L10, 金刚杵 + best def gear + 赤兔, 大将军, sworn brother: atk ×${a.toFixed(3)} def ×${d.toFixed(3)}; exchange vs unequipped L1 ×${(vet * vet * a * d).toFixed(2)} (老练 alone ×${(vet * vet).toFixed(2)}); crit ${(100 * Mods.critChance(u)).toFixed(1)}% ×${Mods.critK(u).toFixed(2)}`);
    V.battle = false;
    P.bondAtk = (uu, m) => bondBattle(uu, m).atk; P.bondDef = (uu, m) => bondBattle(uu, m).def;
    // 开发: 政治 100 with the best domestic kit, 丞相, 铁官
    const pol = g.generals.slice().sort((p, q) => q.pol - p.pol)[0];
    const devBest = ITEMSD.items.filter(it => (it.stats || {}).ind || (it.ability && it.ability.effects.some(e => e.t === 'cmd' && e.cmd === 'develop'))).sort((p, q) => ((q.stats || {}).ind || 0) - ((p.stats || {}).ind || 0));
    pol._gear = { weapon: 'longjin_jian' in IT.ITEM ? 'longjin_jian' : null, offhand: null, armour: null, helmet: null, mount: null, treasure: devBest[0] ? devBest[0].id : null };
    for (const it of devBest.slice(0, 6)) if (!pol._gear[it.slot]) pol._gear[it.slot] = it.id;
    pol._t = { post: 'p5', tier: 5, family: 'pol', since: 1, faction: pol.faction, suspended: false, month: -1 };
    const c = g.cities.find(x => x.owner >= 0);
    c._e.done.add('forge');
    const r = Econ.develop(c, pol, 'industry', 2);
    const v3 = B.DevelopBase + M.idiv(pol.pol, 6) + 2;
    log(`      开发·产业 by ${pol.name} (政治 ${pol.pol}, eff ${IT.eff(pol, 'pol')}): v3 ${v3} → v4 ${r.gain} (g0 ${r.g0}, pct ${r.pct.toFixed(2)}: gear ${r.mods.gear.toFixed(2)} title ${r.mods.title.toFixed(2)} project ${r.mods.project.toFixed(2)}), ×${(r.gain / v3).toFixed(2)}`);
    // income: governor with 交州刺史节-class income gear, 丞相, all income projects, a trade pact, order 100
    c.governor = pol.id; pol.city = c.id; c._e.order = 100; for (const id of ['market', 'roads', 'harbour']) c._e.done.add(id);
    pol._gear.treasure = 'jiaozhou_jie' in IT.ITEM ? pol._gear.treasure : pol._gear.treasure; pol._gear.weapon = 'jiaozhou_jie';
    const ci = Econ.cityGold(c);
    log(`      city income: Σ = ${(ci.mods.title + ci.mods.gear + ci.mods.project + ci.mods.pact + ci.mods.order).toFixed(3)} → pct ${ci.pct.toFixed(3)} (title ${ci.mods.title.toFixed(3)}, gear ${ci.mods.gear.toFixed(3)}, projects ${ci.mods.project.toFixed(3)}, pact ${ci.mods.pact.toFixed(3)}, order ${ci.mods.order.toFixed(3)})`);
    // search: intel 100 + 玉玺-class virtue, 军师将军, 学宫, gear
    const ig = g.generals.slice().sort((p, q) => q.intel - p.intel)[0];
    ig._t = { post: 'i5', tier: 5, family: 'intel', since: 1, faction: ig.faction, suspended: false, month: -1 };
    ig._gear.treasure = ITEMSD.items.filter(it => (it.stats || {}).search).sort((p, q) => q.stats.search - p.stats.search).map(it => it.id)[0] || ig._gear.treasure;
    c._e.done.add('academy');
    const sp = Econ.searchP(c, ig);
    const sRaw = 0.22 + IT.eff(ig, 'intel') / 260 + Mods.virtue(c.owner) / 420 + IT.P.gearSearch(ig) + TI.bonus(ig, 'search') + 0.06;
    log(`      搜索 p: raw sum ${sRaw.toFixed(3)} → ${sp.toFixed(3)}; fame bonus for AP: prestige 2 000 (+13) + 玉玺 (+15) → +${Math.min(Mods.CAPS.fameBonus, 28)} (AP at base fame 80: ${M.clamp(B.ActionPointsBase + M.idiv(Math.min(100, 80 + Math.min(Mods.CAPS.fameBonus, 28)), B.ActionPointsFameDiv), 2, B.ActionPointsMax)} vs v3 ${B.ActionPointsBase + M.idiv(80, B.ActionPointsFameDiv)})`);
    const rp = Econ.troopsPerGold(c, pol);
    log(`      征兵 troops per gold ${rp.toFixed(2)} (v3 5); exp ×${Mods.expK(gy).toFixed(2)}`);
  }
  fs.writeFileSync(path.join(HERE, 'results', 'stacks.txt'), lines.join('\n') + '\n');
}

// ---- refs: SG.Econ / SG.Mods vectors for tests/econ.js --refs and the Unity Econ.cs harness ----
function runRefs() {
  ensureInstalled();
  setVariant('all');
  const out = { version: 1, scenario: SCEN, caps: Mods.CAPS, develop: [], cityGold: [], harvest: [], recruit: [], search: [], ally: [], fame: [], atk: [] };
  for (let s = 1; s <= 3; s++) {
    const g = startGame(SCEN === 'world' ? 'roma' : 'liubei', s); g.player = -1;
    for (let m = 0; m < 24; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
    for (const c of g.cities.filter(x => x.owner >= 0).slice(0, 12)) {
      const gov = c.governor >= 0 ? g.generals[c.governor] : null; if (!gov) continue;
      for (const k of ['land', 'industry', 'town']) out.develop.push({ seed: s, city: c.key, gen: gov.name, kind: k, roll: 3, r: Econ.develop(c, gov, k, 3) });
      out.cityGold.push({ seed: s, city: c.key, r: Econ.cityGold(c) });
      out.harvest.push({ seed: s, city: c.key, roll: 1.07, r: Econ.harvest(c, 1.07) });
      out.recruit.push({ seed: s, city: c.key, gen: gov.name, tpg: Econ.troopsPerGold(c, gov) });
      out.search.push({ seed: s, city: c.key, gen: gov.name, p: Econ.searchP(c, gov) });
    }
    for (const f of g.factions.filter(x => x.alive).slice(0, 6)) out.fame.push({ seed: s, f: f.key, fame: Mods.fame(f.id), virtue: Mods.virtue(f.id), ap: Mods.actionPoints(f.id) });
    // synthetic stacked vectors (they exercise every cap): 8 generals, deterministic best-in-slot kits by rarity, title tiers 1–5
    const RAR = ['common', 'uncommon', 'rare', 'epic', 'legendary'];
    const kitOf = (t, key) => { const k = {}; for (const sl of ['weapon', 'offhand', 'armour', 'helmet', 'mount', 'treasure']) { const c = ITEMSD.items.filter(it => it.slot === sl && RAR.indexOf(it.rarity) <= t && !(it.req && it.req.weaponStyle)).sort((a, b) => ((b.stats || {})[key] || 0) - ((a.stats || {})[key] || 0) || (a.id < b.id ? -1 : 1))[0]; k[sl] = c ? c.id : null; } return k; };
    const gens = g.generals.filter(x => !x.dead && x.faction >= 0 && !g.isRuler(x)).sort((a, b) => b.war + b.intel + b.pol - (a.war + a.intel + a.pol) || a.id - b.id).slice(0, 8);
    gens.forEach((x, i) => {
      const t = i % 5, fam = ['war', 'pol', 'intel'][i % 3];
      x._gear = kitOf(t, fam === 'war' ? 'atk' : fam === 'pol' ? 'ind' : 'search');
      x._t = { post: TITLESD.posts.find(q => q.family === fam && q.tier === t + 1).id, tier: t + 1, family: fam, since: 1, faction: x.faction, suspended: false, month: -1 };
      x.exp = 200 * (i + 1); x._dexp = 40 * i;
      const fake = { commander: () => ({ gen: x, alive: true }), units: [] };
      const u = { gen: x, side: 0, alive: true, formation: i % 8, morale: 70 + 4 * i, confused: 0 };
      V.battle = true;
      out.atk.push({ seed: s, gen: x.name, kit: x._gear, tier: t + 1, formation: u.formation, morale: u.morale, atkMul: Mods.atkMul(u, fake), defMul: Mods.defMul(u, fake), crit: Mods.critChance(u), critK: Mods.critK(u), startMorale: Mods.startMorale(u, 60), duelWar: IT.duelWar(x) });
      V.battle = false;
      const c = g.cities[x.city];
      for (const k of ['land', 'industry', 'town']) for (const roll of [0, 4]) out.develop.push({ seed: s, city: c.key, gen: x.name, kind: k, roll, stacked: true, r: Econ.develop(c, x, k, roll) });
      out.recruit.push({ seed: s, city: c.key, gen: x.name, stacked: true, tpg: Econ.troopsPerGold(c, x), train: Econ.trainGain(x, 2, c) });
      out.search.push({ seed: s, city: c.key, gen: x.name, stacked: true, p: Econ.searchP(c, x) });
      const foe = g.generals.find(y => !y.dead && y.faction >= 0 && y.faction !== x.faction && !g.isRuler(y));
      if (foe) out.search.push({ seed: s, gen: x.name, foe: foe.name, stacked: true, hire: Econ.hireP(foe, x, x.faction, 0), discord: Econ.discordP(x, foe), persuade: Econ.persuadeP(x, foe, x.faction) });
    });
    const al = g.factions.filter(x => x.alive);
    for (let i = 0; i + 1 < al.length && i < 6; i++) for (const gift of [0, 500, 1000]) out.ally.push({ seed: s, f: al[i].key, t: al[i + 1].key, gift, envoy: gens[i] ? gens[i].name : null, p: Econ.allyP(al[i].id, al[i + 1].id, gift, gens[i] || null) });
  }
  fs.writeFileSync(path.join(HERE, 'results', `econ-refs-${SCEN}.json`), JSON.stringify(out, null, 1));
  console.log(`wrote results/econ-refs-${SCEN}.json: ${out.develop.length} develop, ${out.cityGold.length} cityGold, ${out.harvest.length} harvest, ${out.recruit.length} recruit, ${out.search.length} search, ${out.ally.length} ally, ${out.fame.length} fame, ${out.atk.length} battle vectors`);
}

// ------------------------------------------------------------ main --
const t0 = Date.now();
if (MODE === 'campaign') runCampaigns(+process.argv[4] || (SCEN === 'world' ? 24 : 48), +process.argv[5] || (SCEN === 'world' ? 15 : 25));
else if (MODE === 'player') runPlayers(+process.argv[4] || (SCEN === 'world' ? 4 : 6), +process.argv[5] || (SCEN === 'world' ? 15 : 20));
else if (MODE === 'tactical') runTactical(+process.argv[4] || 8);
else if (MODE === 'stacks') runStacks();
else if (MODE === 'refs') runRefs();
else console.log('usage: v4-all-proto.js campaign|player|tactical|stacks|refs classic|world [seeds] [years]');
console.log(`done ${((Date.now() - t0) / 1000).toFixed(1)} s`);
