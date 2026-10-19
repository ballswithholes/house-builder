'use strict';
/* ==========================================================================
   tests/econ.js — 第四版整合核心（W0）：SG.Mods / SG.Econ / SG.AIBudget / battle-math / 月末流程 / 城级 / 结果记录
   INTEGRATION §3–§5、§6.3、§12，COMBAT-V4 §2，DOMESTIC-FX §3.2 / §4.2。
   用法：
     node tests/econ.js             全部本地检查（约 10 秒）
     node tests/econ.js --quick     同上（本套件没有长模式；run-all 的快速档也跑全部）
     node tests/econ.js --refs      另外比对 557 条参考向量：设计原型 v4-all-proto.js 搭出同样的局面（3 种子 × 24 个月，全开），
                                    一次用原型自己的 econ-proto.js、一次用 js/econ.js + js/battle-math.js，逐条相等（浮点 1e-9，
                                    整数相等）；结果存为 tests/refs/econ-refs-{classic,world}.json（Unity 的 Tests~/econ 用它），
                                    以后每次与之比对（--write-refs 重写）
   退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');
const cp = require('child_process');
const { load, ROOT } = require('./v4-load.js');

const ARGS = process.argv.slice(2);
let fails = 0, checks = 0;
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } }
function eq(a, b, msg) { ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function near(a, b, msg, tol) { ok(Math.abs(a - b) <= (tol || 1e-9), msg + ' (got ' + a + ', want ' + b + ')'); }
function section(name, fn) {
  const t0 = Date.now();
  console.log('== ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAIL (threw) ' + (e.stack || e)); }
  const ms = Date.now() - t0;
  if (ms > 500) console.log('  (' + ms + ' ms)');
}

// ----------------------------------------------------------------------------------------- refs child --
if (ARGS[0] === '--refs-child') { refsChild(ARGS[1] || 'classic', ARGS[2]); return; }

const SG = load({ v4: true });
const { GameState, StrategyAI, Mods, Econ, AIBudget, Balance: B, M } = SG;
function newGame(key, seed, scen) {
  if (scen) SG.ScenarioData = SG.Scenarios[scen];
  SG.Random.seed(seed);
  const g = SG.G = GameState.newGame(key, scen);
  return g;
}
// 一个独立的 EconCore 实例，提供者表 P 由测试自己填（不影响全局的 SG.Mods）
function inst(P, opts) {
  const core = SG.EconCore.create(SG, P || {}, opts || {});
  SG.BattleMathCore.extend(core.Mods, SG, P || {});
  return core;
}

// ===================================================================== 1 caps =====
section('1 caps table: SG.Balance.Caps = SG.Mods.CAPS = EconCore.BASE_CAPS = refs file', () => {
  const a = JSON.stringify(SG.Balance.Caps), b = JSON.stringify(Mods.CAPS), c = JSON.stringify(SG.EconCore.BASE_CAPS);
  eq(b, a, 'Mods.CAPS equals Balance.Caps');
  eq(c, a, 'BASE_CAPS equals Balance.Caps');
  for (const scen of ['classic', 'world']) {
    const f = path.join(ROOT, 'design-v4', 'integration', 'results', 'econ-refs-' + scen + '.json');
    if (!fs.existsSync(f)) { ok(false, 'refs file missing: ' + f); continue; }
    const r = JSON.parse(fs.readFileSync(f, 'utf8'));
    eq(JSON.stringify(r.caps), a, 'refs ' + scen + ' caps equal Balance.Caps');
  }
  eq(B.Caps.atk, 1.6, 'atk cap 1.60'); eq(B.Caps.negFloor, 0.5, 'neg floor 0.5'); eq(B.Caps.develop, 0.8, 'develop cap 0.80');
  eq(B.Caps.incomeHi, 0.35, 'income hi 0.35'); eq(B.Caps.incomeLo, -0.1, 'income lo −0.10'); eq(B.Caps.fameBonus, 10, 'fame bonus 10');
  eq(B.AIBudget.Share, 0.22, 'AI purse 22 %'); eq(B.AIBudget.CapMonths, 12, 'purse ≤ 12 months');
  eq(B.Decisions.aiPurseShare, B.AIBudget.Share, 'Decisions.aiPurseShare = AIBudget.Share');
  eq(B.Decisions.duelBudget, 6, 'duel budget 6 (Q3 recommended)');
  eq(B.Decisions.slayRulersOnly, true, 'slaying rulers only (Q8 recommended)');
  eq(B.Decisions.classicForeignChance, 0.15, 'classic foreign treasures 15 % (Q5)');
  eq(B.Decisions.tourneyHostTruce, true, 'tournament host truce on (Q12)');
  eq(B.Decisions.voiceFrequency, 'essential', 'voice frequency essential (Q13)');
  eq(B.Decisions.stackProductCap, null, 'no atk × def product cap (Q1)');
  eq(Object.isFrozen(B.Caps) && Object.isFrozen(B.CityLevel) && Object.isFrozen(B.Decisions), true, 'v4 balance tables frozen');
  eq(SG.Defs.formations.map(f => f.crit).join(), '-0.02,0,0.02,0.01,0.03,0.04,0.01,0', 'FORM_CRIT column (COMBAT-V4 §2.1)');
});

// ===================================================================== 2 identity =====
section('2 SG.Econ without providers == the v3 formulas (model.js V3Econ), every city × general × roll', () => {
  const V3 = SG.Commands.V3Econ;
  let n = 0, bad = 0;
  for (const scen of ['classic', 'world']) {
    const g = newGame(scen === 'world' ? 'roma' : 'liubei', 3, scen);
    for (let m = 0; m < 6; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
    const gens = g.generals.filter(x => !x.dead).slice(0, scen === 'world' ? 160 : 999);
    const cities = g.cities.filter(c => c.owner >= 0);
    for (let i = 0; i < cities.length; i++) {
      const c = cities[i], x = gens[i % gens.length], y = gens[(i * 7 + 3) % gens.length];
      for (let roll = 0; roll <= 4; roll++) for (let k = 0; k < 3; k++) {
        const a = Econ.develop(c, x, k, roll), b = V3.develop(c, x, k, roll);
        n++; if (a.gain !== b.gain || a.g0 !== b.g0 || a.kind !== b.kind) bad++;
        n++; if (Econ.trainGain(x, roll, c) !== V3.trainGain(x, roll, c)) bad++;
      }
      const v3gold = M.roundToInt(c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown);
      n++; if (Econ.cityGold(c).gold !== v3gold) bad++;
      for (const roll of [0.85, 0.93, 1.0, 1.0712, 1.15]) { n++; if (Econ.harvest(c, roll).h !== M.roundToInt(c.land * B.FoodPerLand * roll)) bad++; }
      n++; if (Econ.troopsPerGold(c, x) !== V3.troopsPerGold(c, x)) bad++;
      n++; if (Econ.searchP(c, x) !== V3.searchP(c, x)) bad++;
      if (y.faction >= 0 && x.faction >= 0 && y.faction !== x.faction) {
        n++; if (Econ.hireP(y, x, x.faction, 0) !== V3.hireP(y, x, x.faction, 0)) bad++;
        n++; if (Econ.discordP(x, y) !== V3.discordP(x, y)) bad++;
        n++; if (Econ.persuadeP(x, y, x.faction) !== V3.persuadeP(x, y, x.faction)) bad++;
        for (const gift of [0, 200, 1000]) { n++; if (Econ.allyP(x.faction, y.faction, gift, null) !== V3.allyP(x.faction, y.faction, gift, null)) bad++; }
      }
      n++; if (Econ.rewardLoy(x, 3, null) !== V3.rewardLoy(x, 3, null)) bad++;
      n++; if (Econ.allianceMonths(0, 1, null) !== V3.allianceMonths(0, 1, null)) bad++;
    }
    for (const f of g.factions) { n++; if (Mods.fame(f.id) !== f.fame || Mods.virtue(f.id) !== f.virtue) bad++; }
  }
  console.log(`  ${n} comparisons, ${bad} differences`);
  eq(bad, 0, 'no difference between SG.Econ (no providers) and the v3 formulas');
});

// ===================================================================== 3 formulas & caps =====
section('3 SG.Econ formulas with providers: one clamp at the end, caps of INTEGRATION §3.1', () => {
  const g = newGame('liubei', 5, 'classic');
  const c = g.cities.find(x => x.owner === g.player);
  const gov = g.generals[c.governor];
  // develop: gear + title + project ≤ 0.80
  let I = inst({ gearDev: (x, k) => (k === 'industry' ? 0.5 : 0.1), titleDev: () => 0.25, project: (cc, k) => (k === 'dev:industry' ? 0.10 : 0) });
  let r = I.Econ.develop(c, gov, 'industry', 4);
  const g0 = B.DevelopBase + M.idiv(gov.pol, B.DevelopPolDiv) + 4;
  eq(r.g0, g0, 'develop g0 = base + pol/6 + roll'); near(r.pct, 0.8, 'develop pct capped at 0.80'); eq(r.gain, Math.round(g0 * 1.8), 'develop gain = round(g0 × 1.8)');
  near(r.mods.gear, 0.5, 'develop mods.gear'); near(r.mods.title, 0.25, 'develop mods.title'); near(r.mods.project, 0.1, 'develop mods.project');
  r = I.Econ.develop(c, gov, 'land', 0);
  near(r.pct, 0.35, 'develop land: gear 0.1 + title 0.25 (project only for industry)');
  // city gold: clamp(Σ, −0.10, +0.35), event floor −0.50
  I = inst({ titleCity: (a, k) => (k === 'income' ? 0.1 : 0), titleNat: () => 0.06, itemCity: () => 0.25, project: (cc, k) => (k === 'income' ? 0.11 : 0), pactIncome: () => 0.03, order: () => 100, eventIncome: () => -0.8 });
  r = I.Econ.cityGold(c);
  near(r.pct, 0.35, 'income pct capped at +0.35 (raw +0.60)'); near(r.ev, -0.5, 'event buff floor −0.50');
  eq(r.gold, M.roundToInt(r.b * 1.35 * 0.5), 'gold = roundToInt(b × 1.35 × 0.5)');
  I = inst({ order: () => 0, itemCity: () => -0.3 });
  near(I.Econ.cityGold(c).pct, -0.1, 'income pct floor −0.10');
  near(inst({ order: () => 80 }).Econ.cityGold(c).mods.order, 0.03, 'order term (order − 50) / 1000');
  // harvest
  I = inst({ titleCity: (a, k) => (k === 'harvest' ? 0.2 : 0), itemCity: (cc, k) => (k === 'harvest' ? 0.3 : 0), eventHarvest: () => -0.9 });
  r = I.Econ.harvest(c, 1.12);
  near(r.pct, -0.2, 'harvest pct floor −0.20'); eq(r.rollRaw, 1.12, 'harvest keeps rollRaw');
  near(inst({ itemCity: (cc, k) => (k === 'harvest' ? 0.9 : 0) }).Econ.harvest(c, 1).pct, 0.35, 'harvest pct cap +0.35');
  // recruit: max(recruiter, admin) gear + admin title + project ≤ 0.40
  I = inst({ gearRecruit: x => (x === gov ? 0.15 : 0.3), titleCity: (a, k) => (k === 'recruit' ? 0.1 : 0), project: (cc, k) => (k === 'recruit' ? 0.1 : 0) });
  const other = g.generals.find(x => x.faction === g.player && x !== gov);
  near(I.Econ.troopsPerGold(c, other), B.TroopsPerGold * 1.4, 'troops per gold capped at +40 % (0.3 + 0.1 + 0.1)');
  near(I.Econ.troopsPerGold(c, gov), B.TroopsPerGold * 1.35, 'troops per gold: recruiter gear 0.15 vs admin 0.15 → 0.35');
  // train: flat ≤ 12
  I = inst({ gearTrain: () => 8, titleTrain: () => 6, project: (cc, k) => (k === 'train' ? 3 : 0) });
  eq(I.Econ.trainGain(gov, 2, c), B.TrainBase + M.idiv(gov.war, 10) + 2 + 12, 'train bonus capped at 12');
  // probabilities: one clamp at the end
  I = inst({ gearSearch: () => 0.5, titleSearch: () => 0.2 });
  eq(I.Econ.searchP(c, gov), 0.9, 'search p ≤ 0.90');
  const foe = g.generals.find(x => x.faction >= 0 && x.faction !== g.player && !g.isRuler(x));
  I = inst({ gearHire: () => 0.9 });
  eq(I.Econ.hireP(foe, gov, g.player, 0.2), 0.95, 'hire p ≤ 0.95');
  eq(inst({ gearHire: () => -2 }).Econ.hireP(foe, gov, g.player, 0), 0.05, 'hire p ≥ 0.05');
  eq(inst({ gearDip: () => 0.9 }).Econ.allyP(g.player, foe.faction, 0, gov), 0.9, 'ally p ≤ 0.90');
  near(inst({ rel: () => 40 }).Econ.allyP(g.player, foe.faction, 0, null) - Econ.allyP(g.player, foe.faction, 0, null), 0.1, 'ally relation term rel / 400', 1e-12);
  eq(inst({ gearDiscord: () => 0.9 }).Econ.discordP(gov, foe), 0.9, 'discord p ≤ 0.90');
  near(inst({ gearDiscord: () => 0.9, discordRes: () => 0.2 }).Econ.discordP(gov, foe), 0.72, 'discord × (1 − resistance)');
  eq(inst({ gearPersuade: () => 2 }).Econ.persuadeP(gov, foe, g.player), 0.9, 'persuade p ≤ 0.90');
  eq(inst({ gearPersuade: () => 2 }).Econ.persuadeP(gov, g.ruler(foe.faction), g.player), 0, 'persuade a ruler = 0');
  near(inst({ gearPersuade: () => 0.3, persuadeRes: () => 0.5 }).Econ.persuadeP(gov, foe, g.player),
    M.clamp((Econ.persuadeP(gov, foe, g.player) + 0.3) * 0.5, 0, 0.9), 'persuade resistance before the clamp', 1e-12);
  // fame / virtue / AP / exp / shields / auras
  const f = g.player, F = g.factions[f];
  I = inst({ prestigeFame: () => 13, eventFame: () => 3, gearFame: () => 15 });
  eq(I.Mods.fameBonus(f), 10, 'fame bonus capped at 10 (13 + 3 + 15)');
  eq(I.Mods.fame(f), Math.min(100, F.fame + 10), 'fame = base + capped bonus');
  eq(I.Mods.actionPoints(f), M.clamp(B.ActionPointsBase + M.idiv(Math.min(100, F.fame + 10), B.ActionPointsFameDiv), 2, B.ActionPointsMax), 'AP from Mods.fame');
  eq(inst({ eventVirtue: () => 30 }).Mods.virtue(f), Math.min(100, F.virtue + 15), 'virtue bonus ≤ +15');
  eq(inst({ eventVirtue: () => -30 }).Mods.virtue(f), Math.max(0, F.virtue - 10), 'virtue malus ≥ −10');
  eq(inst({ expBoost: () => 0.9 }).Mods.exp(gov, 100), 140, 'exp multiplier ≤ +40 %');
  eq(inst({ titleShield: () => 12, bondShield: () => 30 }).Mods.effLoy(gov), gov.loyalty + 30, 'effLoy: the larger shield, never the sum');
  eq(inst({ titleLoyAura: () => 2, itemLoyAura: () => 3 }).Mods.loyAura(c), 3, 'loyAura: the larger aura');
  // injury / away / duelWar
  const x = other;
  x.injury = { until: g.monthIndex + 2, war: 5, away: false };
  eq(Mods.eff(x, 'war'), x.war - 5, 'eff(war) subtracts an active injury');
  eq(Mods.eff(x, 'intel'), x.intel, 'injury only on war');
  eq(Mods.isAway(x), false, 'injury.away false → not away');
  eq(Mods.duelWar(x), Math.min(110, x.war) - 5, 'duelWar − injury');
  eq(inst({ gearWar: () => 11 }).Mods.duelWar(x), Math.min(110, x.war + 3) - 5, 'duelWar gear ≤ +3 (⌊11 / 3⌋ = 3)');
  x.injury = { until: g.monthIndex, war: 5, away: true };
  eq(Mods.eff(x, 'war'), x.war, 'expired injury ignored');
  x.injury = { until: g.monthIndex + 3, war: 0, away: true };
  eq(Mods.isAway(x), true, 'injury.away → away');
  const nIn = g.officersIn(g.cities[x.city]).filter(o => o === x).length;
  eq(nIn, 0, 'GameState.generalsIn skips an injured-away general');
  delete x.injury;
  x.away = { kind: 'caravan', until: g.monthIndex + 1 };
  eq(g.officersIn(g.cities[x.city]).indexOf(x), -1, 'generalsIn skips gen.away');
  delete x.away;
  ok(g.officersIn(g.cities[x.city]).indexOf(x) >= 0, 'back in the city after away is removed');
  // trade / reward / alliance months
  near(inst({ tradeK: () => 0.4 }).Econ.tradeK(c), 0.25, 'trade discount ≤ 0.25');
  eq(inst({ gearReward: () => 3 }).Econ.rewardLoy(gov, 2, gov), B.RewardLoyalty + 5, 'reward + ruler gear');
  eq(inst({ allyMonths: () => 12 }).Econ.allianceMonths(0, 1, null), B.AllianceMonths + 12, 'alliance months + ability');
});

// ===================================================================== 4 provider registry =====
section('4 SG.Mods.provide: additive / replace / any-true, when() switch, off()', () => {
  const g = newGame('liubei', 6, 'classic');
  const gen = g.generals.find(x => x.faction === g.player);
  const c = g.cities[gen.city];
  let on = true;
  const off1 = Mods.provide({ gearAtk: () => 0.10, eff: (x, k) => x[k] + 7, cityDef: () => 4 }, { owner: 'testA', when: () => on });
  const off2 = Mods.provide({ gearAtk: () => 0.05, cityDef: () => 6 }, { owner: 'testB' });
  near(Mods.z('gearAtk', gen), 0.15, 'two gearAtk providers add');
  eq(Mods.eff(gen, 'pol'), gen.pol + 7, 'eff replace provider');
  eq(SG.cityDefense(c), 20 + M.idiv(c.town, 12) + 10, 'cityDefense + Σ cityDef');
  on = false;
  near(Mods.z('gearAtk', gen), 0.05, 'when() false removes testA');
  eq(Mods.eff(gen, 'pol'), gen.pol, 'eff falls back to the attribute when the replace provider is off');
  on = true;
  const off3 = Mods.provide({ eff: (x, k) => x[k] + 1 }, { owner: 'testC' });
  eq(Mods.eff(gen, 'pol'), gen.pol + 1, 'the last active replace provider wins');
  off3();
  eq(Mods.eff(gen, 'pol'), gen.pol + 7, 'off() restores the earlier one');
  const off4 = Mods.provide({ noAttack: (a, b) => a === 1 && b === 2, allied: (a, b) => a + b === 5 }, { owner: 'testD' });
  eq(Mods.noAttack(1, 2), true, 'noAttack any-true'); eq(Mods.noAttack(2, 1), false, 'noAttack false otherwise');
  eq(g.allied(2, 3), true, 'GameState.allied reads the allied provider'); eq(g.allied(2, 4), false, 'allied false otherwise');
  off1(); off2(); off4();
  eq(Mods.z('gearAtk', gen), 0, 'all removed → 0');
  eq(SG.cityDefense(c), 20 + M.idiv(c.town, 12), 'cityDefense back to v3');
  eq(JSON.stringify(Mods.providers()), '{}', 'provider table empty again');
  let threw = false; try { Mods.provide({ x: 3 }); } catch (e) { threw = true; } ok(threw, 'non-function provider throws');
});

// ===================================================================== 5 AIBudget =====
section('5 SG.AIBudget: reserve, floor, purse ≤ 12 months, canSpend, fixed order, never below 0', () => {
  const g = newGame('liubei', 8, 'classic'); g.player = -1;
  const A = AIBudget;
  A.reset();
  const c = g.cities.find(x => x.owner >= 0 && g.officersIn(x).length >= 2);
  const offs = g.officersIn(c).length;
  eq(A.reserve(c).gold, 150 + 8 * offs, 'reserve gold 150 + 8 × officers');
  eq(A.reserve(c).food, 2 * M.idiv(g.troopsIn(c), B.TroopsPerFood), 'reserve food = 2 months');
  eq(A.floor(c), 2 * B.SalaryPerGeneral * offs, 'tribute floor = 2 × salary × officers');
  const f = c.owner;
  A.accrue(f, 100);
  near(A.purseOf(f), 22, 'accrue 22 % of gross');
  for (let i = 0; i < 40; i++) A.accrue(f, 100);
  near(A.purseOf(f), 12 * 22, 'purse capped at 12 months');
  ok(g.aiBudget && g.aiBudget.purse && g.aiBudget.purse[f] > 0, 'purse stored in G.aiBudget (saved)');
  c.gold = A.reserve(c).gold + 100;
  eq(A.canSpend(c, 101, 'gear'), false, 'discretionary never below the reserve');
  eq(A.canSpend(c, 100, 'gear'), true, 'discretionary down to the reserve');
  eq(A.canSpend(c, 100, 'recruit'), true, 'non-discretionary ignores the purse');
  c.gold = A.floor(c) + 50;
  eq(A.canSpend(c, 50, 'tribute'), true, 'tribute down to the floor'); eq(A.canSpend(c, 51, 'tribute'), false, 'tribute not below the floor');
  // purse for exactly one item: the title gift (first in the order) gets it, the gear does not
  A.reset();
  c.gold = 5000;
  const bought = [];
  const offT = StrategyAI.onSpend('title', fid => { if (fid === f && A.trySpend(c, 200, 'title')) bought.push('title'); }, { owner: 'test' });
  const offG = StrategyAI.onSpend('gear', fid => { if (fid === f && A.trySpend(c, 200, 'gear')) bought.push('gear'); }, { owner: 'test' });
  // the discretionary phase: accrue (22 % of 1000 = 220) then title → gear → project → festival
  const ctx = { gross: [], news: [] };
  g.factions.forEach(F => { ctx.gross[F.id] = 0; });
  ctx.gross[f] = 1000;
  StrategyAI.phaseDiscretionary(g, ctx);
  near(A.purseOf(f), 20, 'purse 220 − 200 after the title gift');
  eq(bought.join(), 'title', 'order: a purse for exactly one item buys the title gift, not the gear');
  eq(A.stats.refused.gear >= 1, true, 'the refused gear purchase is counted');
  offT(); offG();
  // random spend storm: never below 0
  A.reset();
  SG.Random.seed(99);
  for (let m = 0; m < 300; m++) {
    for (const F of g.factions) if (F.alive) A.accrue(F.id, 300);
    for (const cc of g.cities) if (cc.owner >= 0) {
      cc.gold = Math.round(SG.Random.rangeFloat(0, 900));
      for (const k of ['title', 'gear', 'project', 'festival', 'tribute']) A.trySpend(cc, Math.round(SG.Random.rangeFloat(1, 600)), k);
    }
  }
  eq(A.stats.below0, 0, 'no city below 0 after any spend');
  ok(A.stats.byKind.gear > 0 && A.stats.refused.gear > 0, 'spends and refusals both happen');
  A.reset();
});

// ===================================================================== 6 battle math =====
section('6 battle-math: capped products, army crit (COMBAT-V4 §2), one random draw per roll, unitPreview', () => {
  const I = inst({});
  near(I.Mods.capped([1.3, 1.4], [0.9], 1.6), 1.6 * 0.9, 'positive product capped at 1.6, negatives multiply');
  near(I.Mods.capped([], [0.6, 0.6], 1.6), 0.5, 'negative product floor 0.5');
  near(I.Mods.capped([1.1], [], 1.6), 1.1, 'below the cap unchanged');
  const g = newGame('liubei', 9, 'classic');
  const gy = g.generals.find(x => x.name === '关羽');
  const mk = (war, lvl, form, morale, confused) => ({ gen: { war, level: lvl, exp: SG.Progress.EXP[lvl], name: 'T' + war + '_' + lvl, faction: -1 }, formation: form, morale, confused: confused || 0, side: 0, alive: true });
  const cc = (war, lvl, form, morale, confused) => I.Mods.critChance(mk(war, lvl, form, morale, confused));
  near(cc(30, 1, 1, 70), 0.02, 'war 30 L1 長蛇 → 2 %');
  near(cc(50, 1, 1, 70), 0.02, 'war 50 L1 → 2 %');
  near(cc(70, 1, 1, 70), 0.02 + 0.0015 * 20, 'war 70 L1 → 5 %');
  near(cc(100, 1, 1, 70), 0.02 + 0.0015 * 50, 'war 100 L1 → 9.5 %');
  near(cc(100, 10, 1, 70), 0.02 + 0.0015 * 50 + 0.004 * 9, 'war 100 L10 → 13.1 %');
  near(cc(100, 10, 5, 95), 0.02 + 0.075 + 0.036 + 0.04 + 0.01, 'war 100 L10 锋矢 morale 95 → 18.1 %');
  near(cc(30, 1, 0, 70), 0, '方圆 −2 % floors at 0');
  eq(cc(100, 10, 5, 95, 2), 0, 'a confused unit never crits');
  near(inst({ gearCrit: () => 0.05, titleCrit: () => 0.05 }).Mods.critChance(mk(70, 1, 1, 70)), 0.05 + 0.06, 'gear + title crit ≤ +6 pp');
  near(inst({ gearCrit: () => 0.3 }).Mods.critChance(mk(100, 10, 5, 95)), 0.02 + 0.075 + 0.036 + 0.04 + 0.06 + 0.01, 'best realistic crit 24.1 %');
  near(inst({ gearCrit: () => 0.3 }).Mods.critChance(mk(130, 10, 5, 95)), 0.25, 'crit chance ≤ 25 %');
  near(inst({ gearCritMul: () => 0.5 }).Mods.critK(mk(70, 1, 1, 70)), 1.8, 'crit multiplier ≤ ×1.8');
  near(I.Mods.critK(mk(70, 1, 1, 70)), 1.5, 'crit multiplier ×1.5');
  // vetLevel: domestic exp at half weight
  const d = { war: 80, exp: 800, level: SG.Progress.levelFor(800), dexp: 800, faction: -1, name: 'D' };
  eq(SG.Progress.vetLevel(d), SG.Progress.levelFor(400), 'vetLevel = levelFor(exp − 0.5 × dexp)');
  eq(SG.Progress.vetLevel({ war: 80, exp: 800, level: 6 }), 6, 'vetLevel without dexp = level');
  near(SG.Mods.critChance({ gen: d, formation: 1, morale: 70 }), 0.02 + 0.0015 * 30 + 0.004 * (SG.Progress.levelFor(400) - 1), 'crit level term uses vetLevel');
  // exactly one random draw per rollCrit, crits on or off
  const R = SG.Random; const v0 = R.value; let draws = 0;
  R.value = function () { draws++; return v0.call(R); };
  const u = mk(100, 10, 5, 95);
  for (let i = 0; i < 50; i++) SG.Mods.rollCrit(u);
  eq(draws, 50, 'rollCrit draws one value per call');
  SG.Crit.enabled = false;
  let crits = 0;
  for (let i = 0; i < 50; i++) if (SG.Mods.rollCrit(u).crit) crits++;
  eq(draws, 100, 'one draw per call with crits switched off');
  eq(crits, 0, 'no crit when SG.Crit.enabled = false');
  SG.Crit.enabled = true;
  draws = 0;
  const pv = SG.BattleMath.unitPreview(gy);
  eq(draws, 0, 'unitPreview draws nothing');
  R.value = v0;
  // statistical: p ≈ critChance
  SG.Random.seed(4); let hit = 0; const N = 20000;
  for (let i = 0; i < N; i++) if (SG.Mods.rollCrit(u).crit) hit++;
  near(hit / N, SG.Mods.critChance(u), 'rollCrit rate ≈ critChance', 0.01);
  eq(pv.atkMul, 1, 'unitPreview without providers: atkMul 1'); eq(pv.vetK, SG.Progress.vetK(gy), 'unitPreview vetK = Progress.vetK');
  near(pv.crit, SG.Mods.critChance({ gen: gy, formation: gy.formation | 0, morale: 70 }), 'unitPreview crit = critChance');
  // the hand-built stack of INTEGRATION §8.1 stays below the cap: gear 0.40, title 0.10, bond 0.04, aura 0.06
  const S = inst({ gearAtk: () => 0.40, titleAtk: () => 0.06, cmdAtk: () => 0.04, bondAtk: () => 0.04, auraAtk: () => 0.06 });
  const stk = { gen: gy, side: 0, alive: true, formation: 5, morale: 95 };
  near(S.Mods.atkMul(stk, { commander: () => stk }), Math.min(1.6, 1.4 * 1.1 * 1.04 * 1.06), 'stack product as INTEGRATION §3.2 groups');
  near(S.Mods.atkMul(stk, { commander: () => stk }, [0.2]), 1.6, 'a conditional term can push the product to the cap');
  eq(inst({ gearMorale: () => 20 }).Mods.startMorale(stk, 90), 100, 'start morale ≤ 100');
  eq(inst({ gearMorale: () => 20, titleMorale: () => 20 }).Mods.startMorale(stk, 60), 85, 'start morale bonus ≤ +25');
});

// ===================================================================== 7 commands & lastResult =====
section('7 Commands → SG.Econ, Commands.lastResult records (DOMESTIC-FX §3.2), onCommand', () => {
  const g = newGame('liubei', 10, 'classic');
  const C = SG.Commands;
  const c = g.cities.find(x => x.owner === g.player);
  const gov = g.generals[c.governor];
  const seen = [];
  const off = C.onCommand((cmd, res) => seen.push(cmd + ':' + (res.ok !== false)));
  const evs = [];
  const offE = SG.Events.on('command', (cmd) => evs.push(cmd));
  c.gold = 5000; c.food = 50000;
  C.develop(c, gov, SG.DevKind.Industry);
  let r = C.lastResult;
  eq(r.cmd, 'develop', 'develop record'); eq(r.kind, 'industry', 'develop kind'); ok(r.roll >= 0 && r.roll <= 4, 'develop roll 0–4');
  eq(r.after - r.before, r.gain, 'develop gain = after − before'); eq(r.g0, r.gain, 'no modifiers: gain = g0'); eq(r.city, c.id, 'develop city'); eq(r.gen, gov.id, 'develop gen');
  ok(['normal', 'good', 'great'].indexOf(r.tier) >= 0, 'develop tier set (' + r.tier + ')');
  eq(r.cost, B.DevelopCost, 'develop cost');
  const w = g.generals.find(x => x.faction === g.player && x.city === c.id && x.troops < SG.maxTroops(x));
  C.recruit(c, w, 500); r = C.lastResult;
  eq(r.cmd, 'recruit', 'recruit record'); eq(r.n, 500, 'recruit n'); eq(r.after - r.before, 500, 'recruit troops'); eq(r.cost, 100, 'recruit cost 500 / 5');
  eq(r.cityAfter - r.cityBefore, 500, 'recruit city troops');
  C.train(w); r = C.lastResult; eq(r.cmd, 'train', 'train record'); ok(r.after >= r.before, 'train after ≥ before');
  C.search(c, gov); r = C.lastResult; eq(r.cmd, 'search', 'search record'); ok(['talent', 'gold', 'food', 'none', 'item'].indexOf(r.outcome) >= 0, 'search outcome ' + r.outcome);
  ok(r.p > 0 && r.p <= 0.9, 'search p in record');
  C.reward(c, w); r = C.lastResult; eq(r.cmd, 'reward', 'reward record'); ok(r.after >= r.before, 'reward loyalty up');
  C.buyFood(c, 300); r = C.lastResult; eq(r.cmd, 'trade', 'trade record'); eq(r.side, 'buy', 'trade side'); eq(r.gold, 300, 'trade gold');
  C.sellFood(c, 1000); r = C.lastResult; eq(r.side, 'sell', 'sell side');
  const tgt = g.factions.find(F => F.id !== g.player && F.alive).id;
  C.ally(c, g.player, tgt, 200); r = C.lastResult; eq(r.cmd, 'ally', 'ally record'); eq(r.target, tgt, 'ally target'); ok(r.p >= 0.05 && r.p <= 0.9, 'ally p');
  ok(r.mods && r.mods.gear === 0 && r.mods.title === 0 && r.mods.rel === 0, 'ally record carries its chance mods');
  const foe = g.generals.find(x => x.faction >= 0 && x.faction !== g.player && !g.isRuler(x));
  C.discord(gov, foe); r = C.lastResult; eq(r.cmd, 'discord', 'discord record'); eq(r.target, foe.id, 'discord target');
  ok(r.mods && 'gear' in r.mods && 'res' in r.mods, 'discord record carries its chance mods');
  C.persuade(gov, foe, g.player); r = C.lastResult; eq(r.cmd, 'persuade', 'persuade record');
  ok(r.mods && r.mods.gear === 0 && r.mods.title === 0 && r.mods.res === 0, 'persuade record carries its chance mods (all 0 without providers)');
  const freeG = g.generals.find(x => SG.isFree(x));
  if (freeG) { C.hire(freeG, gov, g.player, c.id, { promise: 0.05 }); r = C.lastResult; eq(r.cmd, 'hire', 'hire record'); eq(r.mods && r.mods.promise, 0.05, 'hire record carries the promise term'); ok(r.p > 0, 'hire p'); }
  C.appoint(c, w); r = C.lastResult; eq(r.cmd, 'appoint', 'appoint record'); eq(c.governor, w.id, 'appoint sets the governor');
  C.develop({ gold: 0 }, gov, 0); r = C.lastResult; eq(r.ok, false, 'failed develop recorded with ok false');
  off(); offE();
  ok(seen.length >= 12 && evs.length === seen.length, 'onCommand and SG.Events command both fire (' + seen.length + ')');
  // searchItem hook (ITEMS §8.5): called after the talent check, before the gold / food roll
  g.generals.forEach(x => { if (x.city === c.id && x.hidden) x.hidden = false; });
  C.searchItem = () => ({ item: 42, id: 'test_item', name: '测试宝物' });
  const msg = C.search(c, gov);
  eq(C.lastResult.outcome, 'item', 'searchItem hook gives an item outcome'); ok(msg.indexOf('测试宝物') >= 0, 'item message');
  C.searchItem = null;
  // attack record from Conquest.prepare
  const src = c, t = g.cities[src.links[0]];
  const s = SG.Conquest.prepare(g.player, src, t, [w], 100, 0);
  eq(C.lastResult.cmd, 'attack', 'attack record'); eq(C.lastResult.to, t.id, 'attack to'); void s;
});

// ===================================================================== 8 month pipeline =====
section('8 StrategyAI.endMonth: phases in INTEGRATION §6.3 order, ctx, discretionary per AI faction, no state when off', () => {
  const g = newGame('liubei', 11, 'classic');
  const order = [];
  const offs = [];
  for (const ph of StrategyAI.PHASES) offs.push(StrategyAI.on(ph, ctx => order.push(ph + '@' + (ph === 'titles' || ph === 'discretionary' || ph === 'tourney' || ph === 'news' ? (ctx.next ? 'next' : 'none') : 'ended:' + (g.month === ctx.ended.month)))));
  const spent = [];
  for (const k of ['festival', 'project', 'gear', 'title']) offs.push(StrategyAI.onSpend(k, f => { if (spent.length < 4 * 40) spent.push(f + ':' + k); }, { owner: 'test' }));
  g.month = 11;
  const news = StrategyAI.endMonth();
  eq(order.join(' '), 'snapshot@ended:true income@ended:true items@ended:true engage@ended:true injuries@ended:true defections@ended:true loyAura@ended:true titles@next discretionary@next tourney@next news@next', 'phase order and month boundary');
  eq(g.month, 12, 'month advanced once');
  ok(Array.isArray(news.harvests) && news.ctx && news.ctx.ended.month === 11 && news.ctx.next.month === 12, 'news carries harvests and ctx');
  const f0 = spent[0].split(':')[0];
  eq(spent.slice(0, 4).join(), [f0 + ':title', f0 + ':gear', f0 + ':project', f0 + ':festival'].join(), 'per faction: title → gear → project → festival');
  ok(!spent.some(x => +x.split(':')[0] === g.player), 'the player faction never spends through AIBudget');
  ok(g.aiBudget && Object.keys(g.aiBudget.purse).length > 0, 'purse accrued for AI factions');
  offs.forEach(o => o());
  // January ordering: the tourney phase sees month 1 of the new year
  g.month = 12;
  let saw = null; const o2 = StrategyAI.on('tourney', ctx => { saw = [ctx.ended.month, g.month, ctx.next.year === ctx.ended.year + 1]; });
  StrategyAI.endMonth(); o2();
  eq(JSON.stringify(saw), '[12,1,true]', 'tourney phase runs in month 1 of the new year');
  // harvest records with rollRaw and tier
  g.month = 7;
  const nh = StrategyAI.endMonth();
  ok(nh.harvests.length === g.citiesOf(g.player).length && nh.harvests.every(h => h.rollRaw >= 0.85 && h.rollRaw <= 1.15 && ['miss', 'normal', 'good', 'great'].indexOf(h.tier) >= 0), 'harvest records: rollRaw and tier');
  // unified injury record (INTEGRATION §6.4): eff war −war while active, away removes him from the city, expiry in phase 4
  {
    const pc = g.citiesOf(g.player)[0], hurt = g.generalsIn(pc.id, g.player).find(x => !g.isRuler(x)) || g.generalsIn(pc.id, g.player)[0];
    const w0 = hurt.war, mi = g.monthIndex;
    hurt.injury = { until: mi + 2, war: 5, away: true };
    eq(SG.Mods.eff(hurt, 'war'), w0 - 5, 'injured: eff war − 5'); eq(SG.Mods.duelWar(hurt), w0 - 5, 'injured: duelWar − 5');
    ok(g.generalsIn(pc.id, g.player).indexOf(hurt) < 0 && SG.Mods.isAway(hurt), 'injury.away: not in the city, isAway');
    let healed1 = null; const oh = StrategyAI.on('injuries', ctx => { healed1 = ctx.healed.slice(); });
    StrategyAI.endMonth();
    ok(hurt.injury && healed1.indexOf(hurt.id) < 0, 'still injured after month 1 of 2');
    StrategyAI.endMonth(); oh();
    ok(!hurt.injury && healed1.indexOf(hurt.id) >= 0, 'injury record removed when it ends; ctx.healed lists him for the hooks');
    eq(SG.Mods.eff(hurt, 'war'), w0, 'healed: eff war back'); ok(g.generalsIn(pc.id, g.player).indexOf(hurt) >= 0, 'healed: back in the city');
  }
  // no hooks → no v4 state
  const g2 = newGame('cao', 12, 'classic');
  for (let m = 0; m < 14; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
  eq(g2.aiBudget, undefined, 'no discretionary hooks → no purse state');
  const keys = Object.keys(JSON.parse(JSON.stringify(g2))).join();
  eq(keys, 'version,scenario,year,month,player,tokens,seed,cities,generals,factions,alliance,log', 'save has only the core keys when every v4 system is off');
  // the day clock
  const g3 = newGame('liubei', 13, 'classic');
  SG.Clock.monthStart(g3);
  near(SG.Clock.hour(), 7, 'clock 07:00 at month start');
  g3.tokens = Math.floor(SG.Clock.total() / 2);
  near(SG.Clock.hour(), 7 + 13 * (SG.Clock.total() - g3.tokens) / SG.Clock.total(), 'clock 7 + 13 × used / total');
  g3.tokens = 0; near(SG.Clock.hour(), 20, 'clock 20:00 when the last token is used');
  StrategyAI.runAI([]); near(SG.Clock.hour(), 20.5, 'clock 20:30 during the AI phase');
  SG.Clock.set(10); near(SG.Clock.hour(), 10, 'clock override'); SG.Clock.restore(); near(SG.Clock.hour(), 20.5, 'clock restore');
  StrategyAI.endMonth(); near(SG.Clock.hour(), 20.5, 'clock 20:30 during the news after endMonth');
  SG.Clock.setPhase('day'); near(SG.Clock.hour(), 7, 'clock 07:00 when the news closes (setPhase day)');
  SG.Clock.setPhase('news'); g3.tokens = SG.Clock.total() - 1;
  near(SG.Clock.hour(), 7 + 13 / SG.Clock.total(), 'news phase ends by itself once a token is spent');
});

// ===================================================================== 9 city level =====
section('9 SG.Balance.CityLevel / SG.Econ.stages vs design-v4/domestic/city-stages.js (both scenarios, every city)', () => {
  const CS = require(path.join(ROOT, 'design-v4', 'domestic', 'city-stages.js'));
  let n = 0, bad = 0;
  eq(JSON.stringify(SG.Balance.CityLevel.HUBS), JSON.stringify(CS.HUBS), 'HUBS equal the reference');
  for (const scen of ['classic', 'world']) {
    const g = newGame(scen === 'world' ? 'roma' : 'liubei', 14, scen);
    for (let m = 0; m < 30; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
    for (const c of g.cities) {
      const ctx = CS.ctxFor(g, c, {});
      const ref = CS.stages(ctx);
      const mine = Econ.stages(c);
      for (const k of SG.Balance.CityLevel.CORE) { n++; if (ref[k] !== mine[k]) bad++; }
      n++; if (CS.cityLevel(ref) !== Econ.cityLevel(c)) bad++;
      // hysteresis: a stat just under a threshold keeps the previous stage while ≥ 0.95 × threshold
      const prev = { farms: 4, workshop: 4, houses: 4, walls: 4, market: 4 };
      const refH = CS.stages(ctx, prev), mineH = Econ.stages(c, prev);
      for (const k of SG.Balance.CityLevel.CORE) { n++; if (refH[k] !== mineH[k]) bad++; }
    }
  }
  console.log(`  ${n} comparisons, ${bad} differences`);
  eq(bad, 0, 'core stages and city level equal the CITY-VIEW reference');
  eq(Econ.levelName(2), '郡城', 'level names');
});

// ===================================================================== 10 tiers =====
section('10 result tiers on pre-multiplier values (DOMESTIC-FX §4.2)', () => {
  eq(Econ.tierDevelop({ before: 280, after: 305, roll: 1, g0: 20 }), 'great', 'develop: visible stage rises (300) → great');
  eq(Econ.tierDevelop({ before: 990, after: 999, roll: 1, g0: 9 }), 'great', 'develop: reaches 999 → great');
  eq(Econ.tierDevelop({ before: 395, after: 420, roll: 1, g0: 10 }), 'good', 'develop: before + g0 crosses 400 → good');
  eq(Econ.tierDevelop({ before: 385, after: 420, roll: 1, g0: 10 }), 'normal', 'develop: only the multiplied gain crosses 400 → normal (modifiers never inflate tiers)');
  eq(Econ.tierDevelop({ before: 310, after: 330, roll: 4, g0: 20 }), 'good', 'develop: roll 4 → good');
  eq(Econ.tierHarvest({ rollRaw: 0.89, land: 700 }), 'miss', 'harvest lean < 0.90');
  eq(Econ.tierHarvest({ rollRaw: 1.04, land: 300 }), 'good', 'harvest good ≥ 1.04');
  eq(Econ.tierHarvest({ rollRaw: 1.12, land: 650 }), 'great', 'harvest great ≥ 1.10 with land ≥ 600');
  eq(Econ.tierHarvest({ rollRaw: 1.12, land: 550 }), 'good', 'harvest ≥ 1.10 but land < 600 → good');
  eq(Econ.tierHarvest({ rollRaw: 1.0, land: 900 }), 'normal', 'harvest normal');
  // distribution of the harvest tiers on uniform rolls: lean ≈ 16.7 %, good+great ≈ 36.7 %
  SG.Random.seed(5); const cnt = { miss: 0, normal: 0, good: 0, great: 0 }; const N = 30000;
  for (let i = 0; i < N; i++) cnt[Econ.tierHarvest({ rollRaw: SG.Random.rangeFloat(0.85, 1.15), land: 650 })]++;
  near(cnt.miss / N, 0.05 / 0.30, 'lean share', 0.01); near((cnt.good + cnt.great) / N, 0.11 / 0.30, 'good + great share', 0.01);
});

// ===================================================================== 11 registries =====
section('11 registries (INTEGRATION §10): Settings, Reg.*, Events', () => {
  const S = SG.Settings;
  const store = {};
  SG.__ctx.localStorage = { getItem: k => (k in store ? store[k] : null), setItem: (k, v) => { store[k] = String(v); }, removeItem: k => { delete store[k]; } };
  let applied = null;
  S.define({ id: 'test.row', tab: 'game', order: 5, label: '测试', type: 'seg', options: [['a', 'A'], ['b', 'B']], default: 'a', store: { key: 'sanguozhi2_test_v4', path: 'row' }, apply: v => { applied = v; } });
  eq(S.get('test.row'), 'a', 'default value'); eq(S.set('test.row', 'b'), true, 'set'); eq(S.get('test.row'), 'b', 'get after set'); eq(applied, 'b', 'apply called');
  eq(JSON.parse(store.sanguozhi2_test_v4).row, 'b', 'stored in the owner key / path');
  eq(S.set('test.row', 'zzz'), false, 'invalid option refused');
  let heard = null; const off = S.on('test.row', v => { heard = v; }); S.set('test.row', 'a'); eq(heard, 'a', 'on() listener'); off();
  S.define({ id: 'test.hidden', tab: 'game', order: 6, label: 'x', type: 'toggle', default: true, when: () => false });
  ok(S.rows('game').some(d => d.id === 'test.row') && !S.rows('game').some(d => d.id === 'test.hidden'), 'rows() hides when() = false');
  S.reset('game'); eq(S.get('test.row'), 'a', 'reset to the default');
  eq(S.export()['test.row'], 'a', 'export');
  let threw = false; try { S.define({ id: 'x', tab: 'nope' }); } catch (e) { threw = true; } ok(threw, 'unknown tab throws');
  delete SG.__ctx.localStorage;
  eq(S.get('test.row'), 'a', 'no storage: falls back to memory / default');
  // generic registries
  SG.Reg.sfx.def('ui-test-click', { recipe: 1 });
  threw = false; try { SG.Reg.sfx.def('ui-test-click', {}); } catch (e) { threw = true; } ok(threw, 'duplicate sfx id throws (INTEGRATION §10.6)');
  threw = false; try { SG.Reg.sfx.def('bad-name', {}); } catch (e) { threw = true; } ok(threw, 'sfx id needs an owner prefix');
  for (const k of ['voiceCues', 'stings', 'icons', 'clips', 'cards']) ok(SG.Reg[k] && typeof SG.Reg[k].def === 'function', 'SG.Reg.' + k + ' exists');
  SG.Reg.stings.def('develop.land', { tiers: 4 }); eq(SG.Reg.stings.get('develop.land').tiers, 4, 'stings registry');
  let ev = 0; const o = SG.Events.on('test.ev', (a, b) => { ev = a + b; }); SG.Events.emit('test.ev', 2, 3); o(); SG.Events.emit('test.ev', 5, 5); eq(ev, 5, 'Events on / emit / off');
});

// ===================================================================== 12 refs =====
// The prototype (v4-all-proto.js refs) builds 3 seeds × 24 months of all-on games and evaluates SG.Econ / SG.Mods on them.
// We run it twice in child processes — once with its own econ-proto.js, once with js/econ.js + js/battle-math.js — on the
// same game files and data, and require all 557 vectors to be equal (floats 1e-9, integers exact). The result is kept as
// tests/refs/econ-refs-<scen>.json for Unity's Tests~/econ (and compared on later runs). The copies in
// design-v4/integration/results/ were generated before the consolidation's final catalogue (items.json, 602 items),
// so they are reported for information only.
if (ARGS.indexOf('--refs') >= 0) {
  section('12 reference vectors (557 per scenario): js/econ.js + js/battle-math.js vs the prototype econ, same games', () => {
    const tmp = fs.mkdtempSync(path.join(require('os').tmpdir(), 'w0-refs-'));
    const refDir = path.join(ROOT, 'tests', 'refs');
    for (const scen of ['classic', 'world']) {
      const t0 = Date.now();
      const out = {};
      for (const which of ['proto', 'js']) {
        const file = path.join(tmp, scen + '-' + which + '.json');
        const r = cp.spawnSync(process.execPath, [__filename, '--refs-child', scen, file], { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 << 20, env: Object.assign({}, process.env, { W0_ECON: which }) });
        if (r.status !== 0 || !fs.existsSync(file)) { ok(false, 'refs child ' + scen + '/' + which + ' failed: ' + (r.stderr || '').slice(-1500)); continue; }
        out[which] = JSON.parse(fs.readFileSync(file, 'utf8'));
      }
      if (!out.proto || !out.js) continue;
      const c1 = compareRefs(out.js, out.proto);
      console.log(`  ${scen}: ${c1.n} vectors (${c1.groups}); js/econ.js vs econ-proto.js: ${c1.bad} mismatches  (${((Date.now() - t0) / 1000).toFixed(1)} s)`);
      for (const m of c1.examples) console.log('    mismatch ' + m);
      eq(c1.bad, 0, 'refs ' + scen + ': js/econ.js equals the prototype on every vector');
      // 557 vectors in the classic game; the world game has 550 with the final catalogue (the count follows the cities
      // with a governor in the generated games; INTEGRATION §4.8 quotes 557 for both from the earlier catalogue)
      ok(c1.n >= (scen === 'classic' ? 557 : 540), 'refs ' + scen + ': enough vectors (' + c1.n + ')');
      const mine = path.join(refDir, 'econ-refs-' + scen + '.json');
      if (ARGS.indexOf('--write-refs') >= 0 || !fs.existsSync(mine)) {
        fs.mkdirSync(refDir, { recursive: true });
        fs.writeFileSync(mine, JSON.stringify(out.js, null, 1));
        console.log('  wrote tests/refs/econ-refs-' + scen + '.json');
      } else {
        const c2 = compareRefs(out.js, JSON.parse(fs.readFileSync(mine, 'utf8')));
        eq(c2.bad, 0, 'refs ' + scen + ' unchanged against tests/refs/econ-refs-' + scen + '.json');
        for (const m of c2.examples) console.log('    regression ' + m);
      }
      const design = path.join(ROOT, 'design-v4', 'integration', 'results', 'econ-refs-' + scen + '.json');
      if (fs.existsSync(design)) {
        const c3 = compareRefs(out.js, JSON.parse(fs.readFileSync(design, 'utf8')));
        console.log(`  (info) vs design-v4/integration/results/econ-refs-${scen}.json: ${c3.bad} differing values — that file predates the final items.json`);
      }
    }
  });
}

function compareRefs(mine, ref) {
  let n = 0, bad = 0; const ex = []; const groups = [];
  const cmp = (a, b, p) => {
    if (typeof b === 'number') { if (typeof a !== 'number' || (Number.isInteger(b) ? a !== b : Math.abs(a - b) > 1e-9)) { bad++; if (ex.length < 8) ex.push(p + ': ' + a + ' vs ' + b); } return; }
    if (b === null || typeof b !== 'object') { if (a !== b) { bad++; if (ex.length < 8) ex.push(p + ': ' + JSON.stringify(a) + ' vs ' + JSON.stringify(b)); } return; }
    for (const k of Object.keys(b)) {
      if (k === 'duelWar') continue;     // ITEMS' duelWar (items-mod), not an Econ / Mods output
      cmp(a ? a[k] : undefined, b[k], p + '.' + k);
    }
  };
  for (const k of ['develop', 'cityGold', 'harvest', 'recruit', 'search', 'ally', 'fame', 'atk']) {
    const A = mine[k] || [], Bv = ref[k] || [];
    if (A.length !== Bv.length) { bad++; ex.push(k + ': ' + A.length + ' vs ' + Bv.length + ' vectors'); }
    for (let i = 0; i < Bv.length; i++) { n++; cmp(A[i], Bv[i], k + '[' + i + ']'); }
    groups.push(k + ' ' + Bv.length);
  }
  cmp(mine.caps, ref.caps, 'caps');
  return { n, bad, examples: ex, groups: groups.join(', ') };
}

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;

// ============================================================================================ refs child --
// Runs the design prototype's `refs` mode in this process — with js/econ.js + js/battle-math.js as its econ module, or with
// its own econ-proto.js when W0_ECON=proto — and writes the vectors to outFile.
function refsChild(scen, outFile) {
  const DV4 = path.join(ROOT, 'design-v4', 'integration');
  // our implementation, from a separate context (it must not see the prototype's global SG)
  const own = load({ files: ['core.js', 'registry.js', 'data.js', 'econ.js', 'battle-math.js'] });
  const shim = {
    CAPS: own.EconCore.BASE_CAPS,
    create(SGp, P, opts) {
      const core = own.EconCore.create(SGp, P, { share: opts && opts.share, capMonths: opts && opts.capMonths, injury: false, purse: 'map' });
      own.BattleMathCore.extend(core.Mods, SGp, P);
      return core;
    },
  };
  let src = fs.readFileSync(path.join(DV4, 'v4-all-proto.js'), 'utf8');
  const swap = (a, b) => { if (src.indexOf(a) < 0) throw new Error('v4-all-proto.js anchor missing: ' + a); src = src.replace(a, b); };
  if (process.env.W0_ECON !== 'proto') swap("const ECON = require('./econ-proto.js');", 'const ECON = __W0_ECON;');   // W0_ECON=proto: the prototype's own econ (diagnostics)
  src = src.replace(/^'use strict';/, '');
  const written = {};
  // W0_ALT=dir: game files under js/ are read from dir when present there (diagnostics: compare with older game code)
  const ALT = process.env.W0_ALT || null, JSDIR = path.join(ROOT, 'js');
  const fsProxy = Object.assign({}, fs, {
    writeFileSync(p, d) { written[p] = String(d); },
    readFileSync(p, o) { if (ALT && typeof p === 'string' && path.dirname(p) === JSDIR && fs.existsSync(path.join(ALT, path.basename(p)))) p = path.join(ALT, path.basename(p)); return fs.readFileSync(p, o); },
  });
  const Mod = require('module');
  const mkReq = file => { const r = Mod.createRequire(file); return m => (m === 'fs' ? fsProxy : m === 'module' ? modProxy : r(m)); };
  const modProxy = Object.assign(Object.create(Mod), { createRequire: f => mkReq(f) });
  const req = mkReq(path.join(DV4, 'v4-all-proto.js'));
  const fakeProcess = { argv: [process.argv[0], path.join(DV4, 'v4-all-proto.js'), 'none', scen], env: Object.assign({}, process.env), stdout: process.stdout, exit: process.exit.bind(process), hrtime: process.hrtime };
  const fn = new Function('require', 'module', '__dirname', '__filename', 'process', '__W0_ECON', '"use strict";' + src + '\nreturn { runRefs };');
  const api = fn(req, { exports: {} }, DV4, path.join(DV4, 'v4-all-proto.js'), fakeProcess, shim);
  const log = console.log; console.log = () => {};
  try { api.runRefs(); } finally { console.log = log; }
  const key = Object.keys(written).find(k => k.indexOf('econ-refs-' + scen + '.json') >= 0);
  if (!key) throw new Error('the prototype wrote no refs file');
  fs.writeFileSync(outFile, written[key]);
}
