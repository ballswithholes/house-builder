'use strict';
/* ==========================================================================
   design-v4/integration/econ-proto.js — reference implementation of INTEGRATION.md §3 (SG.Mods), §4 (SG.Econ), §5 (SG.AIBudget).
   The browser build ports this file to js/econ.js (SG.Mods, SG.Econ, SG.AIBudget) and js/battle-math.js (the battle part);
   the Unity port mirrors it as Econ.cs / BattleMath.cs with the same operation order (test vectors: `node design-v4/integration/v4-all-proto.js refs`).
   Every v4 spec plugs its numbers in through "providers" (P.*); a missing provider contributes nothing, so any subset of the
   v4 systems can be switched on. No function here draws a random number: the callers pass the v3 rolls (R(0,4), rangeFloat) in.
   ========================================================================== */

// ---- caps (INTEGRATION §3.1) ------------------------------------------------
const BASE_CAPS = {
  atk: 1.60, def: 1.60,          // product of all positive non-level battle multipliers (gear × title/command × bond × aura × conditional)
  negFloor: 0.50,                // product of all negative battle multipliers never below ×0.5
  morale: 25,                    // start morale bonus (gear + title)
  critAdd: 0.06, critMul: 0.30,  // COMBAT-V4 §2 (gear + title crit; crit multiplier)
  develop: 0.80,                 // 开发: gear kind + title dev + project
  incomeLo: -0.10, incomeHi: 0.35, eventIncomeFloor: -0.50,
  harvestLo: -0.20, harvestHi: 0.35,
  recruit: 0.40,                 // 征兵 troops per gold
  train: 12,                     // 训练 flat bonus (gear + title + project)
  search: 0.90, hire: 0.95, ally: 0.90, discord: 0.90, persuade: 0.90,
  fameBonus: 10,                 // prestige + event + gear fame, one cap for every use (action points, 外交 …)
  virtueLo: -10, virtueHi: 15,   // event + gear virtue
  exp: 0.40,                     // exp multiplier (ITEMS exp stat + expBoost abilities), applied before the routine cap
  routineCap: 20,                // ENGAGEMENT §4.1 routine domestic exp per general per month
  loyAuraCap: 95,
};

function create(SG, P, opts) {
  opts = opts || {};
  const CAPS = Object.assign({}, BASE_CAPS);     // a copy per instance (tests switch caps off without touching other instances)
  const B = SG.Balance, M = SG.M;
  const G = () => SG.G;
  const z = (fn, ...a) => (fn ? fn(...a) || 0 : 0);
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const eff = (gen, k) => (P.eff ? P.eff(gen, k) : gen[k]);
  const admin = c => (P.admin ? P.admin(c) : (c.governor >= 0 ? G().generals[c.governor] : null));

  // ============================================================ SG.Mods ==
  const Mods = {
    CAPS,
    // faction-level accessors (every v3 read of faction.fame / virtue goes through these)
    fameBonus(f) { return Math.min(CAPS.fameBonus, z(P.prestigeFame, f) + z(P.eventFame, f) + z(P.gearFame, f)); },
    fame(f) { const F = G().factions[f]; return clamp((P.baseFame ? P.baseFame(f) : F.fame) + Mods.fameBonus(f), 0, 100); },
    virtue(f) { const F = G().factions[f]; return clamp((P.baseVirtue ? P.baseVirtue(f) : F.virtue) + clamp(z(P.eventVirtue, f) + z(P.gearVirtue, f), CAPS.virtueLo, CAPS.virtueHi), 0, 100); },
    actionPoints(f) { const fame = f >= 0 ? Mods.fame(f) : 40; return M.clamp(B.ActionPointsBase + M.idiv(fame, B.ActionPointsFameDiv), 2, B.ActionPointsMax); },
    // exp multiplier, applied BEFORE the routine cap (INTEGRATION §3.4)
    expK(gen) { return 1 + Math.min(CAPS.exp, z(P.expBoost, gen)); },
    exp(gen, amt) { return Math.round(amt * Mods.expK(gen)); },
    // defection check (v3 endMonth): the larger shield counts, never the sum
    effLoy(gen) { return gen.loyalty + Math.max(z(P.titleShield, gen), z(P.bondShield, gen)); },
    loyAura(c) { return Math.max(z(P.titleLoyAura, c), z(P.itemLoyAura, c)); },
    // ---- battle (js/battle-math.js) ----
    // pos: positive multipliers as (1 + x) factors; neg: negative ones. Returns the capped product.
    capped(pos, neg, cap) {
      let p = 1; for (const x of pos) p *= x;
      let n = 1; for (const x of neg) n *= x;
      return Math.min(cap, Math.max(1, p)) * Math.max(CAPS.negFloor, Math.min(1, n));
    },
    atkFactors(u, model) {
      const pos = [], neg = [];
      const push = v => { if (v > 0) pos.push(1 + v); else if (v < 0) neg.push(1 + v); };
      push(z(P.gearAtk, u.gen));                                         // ITEMS atk% (≤ 0.40)
      push(z(P.titleAtk, u.gen) + z(P.cmdAtk, model, u.side));           // TITLES personal + commander aura
      push(z(P.bondAtk, u, model));                                      // ENGAGEMENT bonds (−0.08 … +0.08) + 义愤
      push(z(P.auraAtk, u, model));                                      // ITEMS auras / ability stand-ins
      return { pos, neg };
    },
    atkMul(u, model, cond) {
      const f = Mods.atkFactors(u, model);
      for (const k of cond || []) if (k > 0) f.pos.push(1 + k); else if (k < 0) f.neg.push(1 + k);
      return Mods.capped(f.pos, f.neg, CAPS.atk);
    },
    defMul(u, model) {
      const pos = [], neg = [];
      const push = v => { if (v > 0) pos.push(1 + v); else if (v < 0) neg.push(1 + v); };
      push(z(P.gearDef, u.gen));
      push(z(P.titleDef, u.gen) + z(P.cmdDef, model, u.side));
      push(z(P.bondDef, u, model));
      push(z(P.auraDef, u, model));
      return Mods.capped(pos, neg, CAPS.def);
    },
    startMorale(u, base) { return Math.min(100, base + Math.min(CAPS.morale, z(P.gearMorale, u.gen) + z(P.titleMorale, u.gen))); },
    // COMBAT-V4 §2.1 with the level term on 老练 level (ENGAGEMENT §4.4)
    FORM_CRIT: [-0.02, 0, 0.02, 0.01, 0.03, 0.04, 0.01, 0],
    critChance(u) {
      const g = u.gen;
      const lvl = SG.Progress && SG.Progress.enabled ? (P.vetLevel ? P.vetLevel(g) : (g.level || 1)) : 1;
      return clamp(0.02 + 0.0015 * Math.max(0, g.war - 50) + 0.004 * (lvl - 1) + (Mods.FORM_CRIT[u.formation] || 0)
        + Math.min(CAPS.critAdd, z(P.gearCrit, g) + z(P.titleCrit, g)) + (u.morale >= 90 ? 0.01 : 0) - (u.confused > 0 ? 1 : 0), 0, 0.25);
    },
    critK(u) { return 1.5 + Math.min(CAPS.critMul, z(P.gearCritMul, u.gen)); },
  };

  // ============================================================ SG.Econ ==
  const Econ = {
    lastMods: null,
    // 开发 (INTEGRATION §4.1). roll = the v3 R(0,4) drawn by Commands.develop.
    develop(c, g, kind, roll) {
      const key = kind === 'land' ? 'land' : kind === 'industry' ? 'industry' : 'town';
      const g0 = B.DevelopBase + M.idiv(eff(g, 'pol'), B.DevelopPolDiv) + roll;
      const gear = z(P.gearDev, g, key), title = z(P.titleDev, g), project = z(P.project, c, 'dev:' + key);
      const pct = Math.min(CAPS.develop, gear + title + project);
      const gain = Math.round(g0 * (1 + pct));
      return { g0, pct, gain, roll, mods: { gear, title, project } };
    },
    // city gold income (§4.2): b × (1 + clamp(Σ, −10 %, +35 %)) × (1 + temporary event buffs, floor −50 %)
    cityGold(c) {
      const b = c.industry * B.GoldPerIndustry + c.town * B.GoldPerTown;
      const a = admin(c);
      const titleCity = a ? z(P.titleCity, a, 'income') : 0;
      const nat = c.owner >= 0 ? z(P.titleNat, c.owner, 'natIncome') : 0;
      const item = z(P.itemCity, c, 'income');
      const project = z(P.project, c, 'income');
      const pact = z(P.pactIncome, c);
      const order = P.order ? (P.order(c) - 50) / 1000 : 0;
      const pct = clamp(titleCity + nat + item + project + pact + order, CAPS.incomeLo, CAPS.incomeHi);
      const ev = Math.max(CAPS.eventIncomeFloor, z(P.eventIncome, c));
      const gold = M.roundToInt(b * (1 + pct) * (1 + ev));
      return { b, pct, ev, gold, mods: { title: titleCity + nat, gear: item, project, pact, order } };
    },
    // harvest (§4.3): roll = the v3 rangeFloat(0.85, 1.15), kept as rollRaw for DOMESTIC-FX tiers
    harvest(c, roll) {
      const h0 = c.land * B.FoodPerLand * roll;
      const a = admin(c);
      const title = (a ? z(P.titleCity, a, 'harvest') : 0) + (c.owner >= 0 ? z(P.titleNat, c.owner, 'natHarvest') : 0);
      const item = z(P.itemCity, c, 'harvest'), project = z(P.project, c, 'harvest'), events = z(P.eventHarvest, c);
      const order = P.order ? (P.order(c) - 50) / 1000 : 0;
      const pct = clamp(title + item + project + events + order, CAPS.harvestLo, CAPS.harvestHi);
      return { h0, rollRaw: roll, pct, h: M.roundToInt(h0 * (1 + pct)), mods: { title, gear: item, project, events, order } };
    },
    // 征兵 troops per gold (§4.4): gear of the recruiter or of the administrator (the larger), title of the administrator, projects
    troopsPerGold(c, g) {
      const a = admin(c);
      const gear = Math.max(z(P.gearRecruit, g), a ? z(P.gearRecruit, a) : 0);
      const title = a ? z(P.titleCity, a, 'recruit') : 0, project = z(P.project, c, 'recruit');
      return B.TroopsPerGold * (1 + Math.min(CAPS.recruit, gear + title + project));
    },
    trainGain(g, roll, c) {
      const bonus = Math.min(CAPS.train, z(P.gearTrain, g) + z(P.titleTrain, g) + (c ? z(P.project, c, 'train') : 0));
      return B.TrainBase + M.idiv(eff(g, 'war'), 10) + roll + bonus;
    },
    // probabilities: v3 formula on effective attributes, every bonus added, ONE clamp at the end (§4.5)
    searchP(c, g) {
      const f = c.owner;
      return Math.min(CAPS.search, 0.22 + eff(g, 'intel') / 260 + Mods.virtue(f) / 420 + z(P.gearSearch, g) + z(P.titleSearch, g) + z(P.project, c, 'search'));
    },
    hireP(target, recruiter, f, promise) {
      let p = 0.32 + Mods.virtue(f) / 220 + eff(recruiter, 'intel') / 450 - (target.war + target.intel + target.pol) / 900;
      if (target.faction >= 0) p -= target.loyalty / 160;
      return M.clamp(p + z(P.gearHire, recruiter) + z(P.titleHire, recruiter) + (promise || 0), 0.05, CAPS.hire);
    },
    allyP(f, t, gift, envoy) {
      const g = G();
      let p = 0.25 + Mods.virtue(f) / 300 + gift / 1200 + (Mods.fame(f) - Mods.fame(t)) / 300;
      if (g.cityCount(f) > g.cityCount(t) * 2) p += 0.15;
      const ruler = g.ruler(f);
      p += Math.max(ruler ? z(P.gearDip, ruler) : 0, envoy ? z(P.gearDip, envoy) : 0) + (envoy ? z(P.titleDip, envoy) : 0) + z(P.rel, f, t) / 400;
      return M.clamp(p, 0.05, CAPS.ally);
    },
    discordP(agent, target) {
      return M.clamp(0.4 + (eff(agent, 'intel') - eff(target, 'intel')) / 100 + z(P.gearDiscord, agent) + z(P.titleDiscord, agent), 0.1, CAPS.discord);
    },
    persuadeP(agent, target, f) {
      if (G().isRuler(target)) return 0;
      const p = (100 - target.loyalty) / 110 * (0.45 + eff(agent, 'intel') / 180) * (0.55 + Mods.virtue(f) / 220) + z(P.gearPersuade, agent) + z(P.titlePersuade, agent);
      return M.clamp(p * (1 - z(P.persuadeRes, target)), 0, CAPS.persuade);
    },
  };

  // ============================================================ SG.AIBudget ==
  // One reserve per city, one discretionary purse per faction, one spending order (§5).
  const AIB = {
    share: +(opts.share || 0.22), capMonths: +(opts.capMonths || 12),   // 0.22 adopted in the consolidation (INTEGRATION §5)
    ORDER: ['recruit', 'develop', 'tribute', 'event', 'title', 'gear', 'project', 'festival'],
    DISCRETIONARY: new Set(['title', 'gear', 'project', 'festival']),
    purse: new Map(), income: new Map(), stats: null,
    reset() { AIB.purse.clear(); AIB.income.clear(); AIB.stats = { byKind: {}, refused: {}, below0: 0, gross: 0 }; },
    reserve(c) {
      const g = G(), offs = g.officersIn(c).length;
      return { gold: 150 + 8 * offs, food: 2 * M.idiv(g.troopsIn(c), B.TroopsPerFood) };
    },
    floor(c) { return 2 * B.SalaryPerGeneral * G().officersIn(c).length; },
    // month start of the discretionary phase: purse += share × the faction's gold income this month (capped)
    accrue(f, income) {
      AIB.income.set(f, income);
      AIB.stats.gross += income;
      const p = (AIB.purse.get(f) || 0) + AIB.share * income;
      AIB.purse.set(f, Math.min(p, AIB.capMonths * AIB.share * Math.max(1, income)));
    },
    canSpend(c, amount, kind) {
      if (!(amount > 0)) return true;
      if (kind === 'tribute' || kind === 'pact') return c.gold - amount >= AIB.floor(c);
      if (c.gold - amount < AIB.reserve(c).gold) return false;
      if (AIB.DISCRETIONARY.has(kind) && (AIB.purse.get(c.owner) || 0) < amount) return false;
      return true;
    },
    spend(c, amount, kind) {
      if (!(amount > 0)) return;
      c.gold -= amount;
      if (AIB.DISCRETIONARY.has(kind)) AIB.purse.set(c.owner, (AIB.purse.get(c.owner) || 0) - amount);
      if (c.gold < 0) AIB.stats.below0++;
      AIB.stats.byKind[kind] = (AIB.stats.byKind[kind] || 0) + amount;
    },
    trySpend(c, amount, kind) {
      if (!AIB.canSpend(c, amount, kind)) { AIB.stats.refused[kind] = (AIB.stats.refused[kind] || 0) + 1; return false; }
      AIB.spend(c, amount, kind); return true;
    },
    // the richest own city that can pay
    payer(f, amount, kind) {
      const cs = G().citiesOf(f).slice().sort((a, b) => (b.gold - AIB.reserve(b).gold) - (a.gold - AIB.reserve(a).gold));
      for (const c of cs) if (AIB.canSpend(c, amount, kind)) return c;
      return null;
    },
  };
  AIB.reset();
  return { CAPS, Mods, Econ, AIBudget: AIB };
}

module.exports = { create, CAPS: BASE_CAPS };
