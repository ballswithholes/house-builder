'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版整合核心：SG.Mods / SG.Econ / SG.AIBudget（INTEGRATION §3–§5）
   移植自 design-v4/integration/econ-proto.js（同样的运算顺序；Unity 的 Econ.cs 照此逐位移植，
   测试向量见 design-v4/integration/results/econ-refs-*.json，tests/econ.js --refs 回放）。
   纯逻辑：无 THREE / DOM，不取随机数（调用者把 v3 的随机数 R(0,4)、rangeFloat 传进来）。

   载入顺序（INTEGRATION §11）：core.js、registry.js、data.js、world-data.js、各 *-data.js 之后，model.js 之前；
   各规则模块（items.js、titles.js、engage.js、tourney.js、combos.js）在其后登记自己的「提供者」。
   战斗部分（atkMul / defMul / critChance / critK / startMorale / rollCrit / unitPreview）在 js/battle-math.js，
   挂到同一个 SG.Mods 上。

   ---- 提供者（INTEGRATION §3.3）----
   SG.Mods.provide({ gearAtk: gen => 0.12, … }, { owner: 'items', when: () => SG.Items.enabled }) → off()
     缺少的提供者贡献 0，所以第四版的任意子集都能打开；when() 为假时该模块的提供者整体不计（返回 0 / 缺省）。
     同名的多个提供者：数值相加（如 cityDef：官爵的驻军 + 营建的城墙）；下列「替换型」取最后一个打开的：
       eff(gen, k)        属性 + 装备（不减伤病：SG.Mods.eff 统一减 gen.injury.war，INTEGRATION §6.4）
       admin(c)           城的主政者（INTEGRATION §4.6；缺省 = 太守）
       baseFame(f) baseVirtue(f)   势力的基础人望 / 德（缺省 = faction.fame / virtue）
       vetLevel(gen)      老练等级（缺省 SG.Progress.vetLevel）       order(c)  民心 0–100（缺省：无民心项）
       autoPower(gen)     自动结算的战力（缺省 v3 公式）               aiSearchP(c, f)  电脑搜索的概率（缺省 0.15）
     「任一为真」型：noAttack(a, b)（盟约伙伴既非目标也非威胁）、allied(a, b)（GameState.allied 的附加条件）、
       cityProtected(c, f)（这座城本月不受 f 攻击：武林大会东道主停战，TOURNAMENT Q12）。
     名称表：gearAtk gearDef titleAtk titleDef cmdAtk cmdDef bondAtk bondDef auraAtk auraDef gearMorale titleMorale
       gearCrit titleCrit gearCritMul vetLevel gearDev titleDev project titleCity titleNat itemCity order pactIncome
       eventIncome eventHarvest gearRecruit gearTrain titleTrain gearSearch titleSearch gearHire titleHire gearDip
       titleDip rel gearDiscord titleDiscord discordRes gearPersuade titlePersuade persuadeRes prestigeFame eventFame
       gearFame eventVirtue gearVirtue expBoost titleShield bondShield titleLoyAura itemLoyAura eff admin baseFame
       baseVirtue gearWar cityDef popGrowth needK aiTargetK gearReward tradeK allyMonths autoPower aiSearchP noAttack allied
       cityProtected recruitDrill

   ---- SG.Mods（§3）----
   CAPS（= SG.Balance.Caps 的副本，测试可按实例改）· fameBonus(f) fame(f) virtue(f) actionPoints(f) · expK(gen) exp(gen, n)
   effLoy(gen) loyAura(c) · eff(gen, k) injuryWar(gen) isAway(gen) duelWar(gen) · cityDefBonus(c) popK(c)
   needK(f, t) noAttack(a, b) cityProtected(c, f) aiTargetK(f, t) autoPower(gen) aiSearchP(c, f) · z(name, …) has(name) provide(table, opts)

   ---- SG.Econ（§4）：玩家与电脑共用的一条内政公式 ----
   develop(c, g, kind, roll) → { kind, g0, pct, gain, roll, mods }          kind: 'land'|'industry'|'town' 或 DevKind
   cityGold(c) → { b, pct, ev, gold, mods }      harvest(c, roll) → { h0, rollRaw, pct, h, mods }
   troopsPerGold(c, g)   trainGain(g, roll, c)   searchP(c, g)   hireP(t, r, f, promise)   allyP(f, t, gift, envoy)
   discordP(a, t)   persuadeP(a, t, f)   rewardLoy(g, roll, ruler)   tradeK(c)   allianceMonths(f, t, envoy)
   hireMods(t, r, promise) / allyMods(f, t, envoy) / discordMods(a, t) / persuadeMods(a, t) → 各概率的加成项（结果记录的 mods）
   hubTier(c)   shopTier(c)   stageOf(id, c, prev)   stages(c, prev) → { farms, workshop, houses, walls, market }
   levelOfStages(st) / cityLevel(c, prev) → 0–4   levelName(lv)
   tierDevelop(rec) / tierHarvest(rec) → 'miss'|'normal'|'good'|'great'（DOMESTIC-FX §4.2，只看乘成之前的 g0 / rollRaw）

   ---- SG.AIBudget（§5）：一份保留、一个钱袋、一个次序 ----
   reserve(c) → { gold, food }   floor(c)   accrue(f, income)   purse(f)   canSpend(c, amount, kind)   spend(c, amount, kind)
   trySpend(c, amount, kind)   payer(f, amount, kind)   reset()   stats { byKind, refused, below0, gross }   ORDER DISCRETIONARY
   钱袋存在 G.aiBudget.purse[势力编号]（随存档）；玩家从不受 AIBudget 限制。

   SG.EconCore.create(SG, P, opts) → { CAPS, Mods, Econ, AIBudget }   另起一个实例（测试、设计原型回放用；opts.share、
     opts.capMonths、opts.injury = false（eff 不减伤病，交给 P.eff）、opts.purse = 'map'（钱袋放在实例自己的 Map 里））
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});

  // INTEGRATION §3.1（js/data.js 的 SG.Balance.Caps 缺失时的后备；两者由 tests/econ.js 断言一致）
  const BASE_CAPS = {
    atk: 1.60, def: 1.60, negFloor: 0.50, morale: 25, critAdd: 0.06, critMul: 0.30,
    develop: 0.80, incomeLo: -0.10, incomeHi: 0.35, eventIncomeFloor: -0.50, harvestLo: -0.20, harvestHi: 0.35,
    recruit: 0.40, train: 12, search: 0.90, hire: 0.95, ally: 0.90, discord: 0.90, persuade: 0.90,
    fameBonus: 10, virtueLo: -10, virtueHi: 15, exp: 0.40, routineCap: 20, loyAuraCap: 95,
  };
  const REPLACE = new Set(['eff', 'admin', 'baseFame', 'baseVirtue', 'vetLevel', 'order', 'autoPower', 'aiSearchP']);
  const ANY = new Set(['noAttack', 'allied', 'cityProtected']);
  const KIND = ['land', 'industry', 'town'];

  function create(SGx, P, opts) {
    opts = opts || {};
    const B = () => SGx.Balance;
    const M = SGx.M;
    const G = () => SGx.G;
    const CAPS = Object.assign({}, BASE_CAPS, (SGx.Balance && SGx.Balance.Caps) || {});
    const z = (fn, a, b, c) => (fn ? fn(a, b, c) || 0 : 0);
    const rep = (fn, a, b) => (fn ? fn(a, b) : undefined);
    const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
    const injuryOn = opts.injury !== false;
    const injuryWar = gen => {
      const j = gen && gen.injury, g = G();
      return j && g && j.until > g.monthIndex ? (j.war | 0) : 0;
    };
    const eff = (gen, k) => {
      const v = rep(P.eff, gen, k);
      const base = v === undefined ? gen[k] : v;
      return injuryOn && k === 'war' ? base - injuryWar(gen) : base;
    };
    const admin = c => {
      const v = rep(P.admin, c);
      if (v !== undefined) return v;
      return c.governor >= 0 ? G().generals[c.governor] : null;
    };

    // ============================================================ SG.Mods ==
    const Mods = {
      CAPS,
      P,
      z(name, a, b, c) { return z(P[name], a, b, c); },
      has(name) { return typeof P[name] === 'function'; },
      // faction-level accessors (every v3 read of faction.fame / virtue goes through these, INTEGRATION §3.4)
      baseFame(f) { const v = rep(P.baseFame, f); return v === undefined ? G().factions[f].fame : v; },
      baseVirtue(f) { const v = rep(P.baseVirtue, f); return v === undefined ? G().factions[f].virtue : v; },
      fameBonus(f) { return Math.min(CAPS.fameBonus, z(P.prestigeFame, f) + z(P.eventFame, f) + z(P.gearFame, f)); },
      fame(f) { return clamp(Mods.baseFame(f) + Mods.fameBonus(f), 0, 100); },
      virtue(f) { return clamp(Mods.baseVirtue(f) + clamp(z(P.eventVirtue, f) + z(P.gearVirtue, f), CAPS.virtueLo, CAPS.virtueHi), 0, 100); },
      actionPoints(f) { const Bb = B(); const fame = f >= 0 ? Mods.fame(f) : 40; return M.clamp(Bb.ActionPointsBase + M.idiv(fame, Bb.ActionPointsFameDiv), 2, Bb.ActionPointsMax); },
      // exp multiplier, applied BEFORE the routine cap (INTEGRATION §3.4)
      expK(gen) { return 1 + Math.min(CAPS.exp, z(P.expBoost, gen)); },
      exp(gen, amt) { return Math.round(amt * Mods.expK(gen)); },
      // defection check: the larger shield counts, never the sum
      effLoy(gen) { return gen.loyalty + Math.max(z(P.titleShield, gen), z(P.bondShield, gen)); },
      loyAura(c) { return Math.max(z(P.titleLoyAura, c), z(P.itemLoyAura, c)); },
      // unified general state (INTEGRATION §6.4)
      eff,
      injuryWar,
      isAway(gen) {
        const g = G(); if (!gen || !g) return false;
        const mi = g.monthIndex;
        return !!((gen.away && gen.away.until > mi) || (gen.injury && gen.injury.away && gen.injury.until > mi));
      },
      // INTEGRATION §8.3：duelWar = min(110, war + min(3, ⌊gear 武力 / 3⌋)) − injury
      duelWar(gen) { return Math.min(110, gen.war + Math.min(3, Math.floor(z(P.gearWar, gen) / 3))) - injuryWar(gen); },
      // strategy-level hooks (all 0 / false without providers: v3 unchanged)
      cityDefBonus(c) { return z(P.cityDef, c); },
      popK(c) { return 1 + z(P.popGrowth, c); },
      needK(f, t) { return z(P.needK, f, t); },
      aiTargetK(f, t) { return z(P.aiTargetK, f, t); },
      noAttack(a, b) { return !!(P.noAttack && P.noAttack(a, b)); },
      cityProtected(c, f) { return !!(P.cityProtected && P.cityProtected(c, f)); },
      alliedExtra(a, b) { return !!(P.allied && P.allied(a, b)); },
      autoPower(gen) { return rep(P.autoPower, gen); },
      aiSearchP(c, f) { const v = rep(P.aiSearchP, c, f); return v === undefined ? 0.15 : v; },
      admin,
    };

    // ============================================================ SG.Econ ==
    // 民心项（ENGAGEMENT §3.1，DESIGN-V4 §20 问题 9）：(民心 − 50) / 50 × Decisions.orderIncomeWeight（推荐 0.05 = ±5 %）。
    // 写成 (ov − 50) / (50 / w)：50 / 0.05 恰为 1000，缺省值与 (ov − 50) / 1000 逐位相同（参考向量、Unity）
    const orderTerm = ov => {
      const D = B() && B().Decisions, w = D && typeof D.orderIncomeWeight === 'number' ? D.orderIncomeWeight : 0.05;
      return w === 0 ? 0 : (ov - 50) / (50 / w);
    };
    const kindKey = kind => (typeof kind === 'number' ? KIND[kind] || 'town' : kind === 'land' ? 'land' : kind === 'industry' ? 'industry' : 'town');
    const Econ = {
      // 开发 (INTEGRATION §4.1). roll = the v3 R(0,4) drawn by Commands.develop.
      develop(c, g, kind, roll) {
        const Bb = B();
        const key = kindKey(kind);
        const g0 = Bb.DevelopBase + M.idiv(eff(g, 'pol'), Bb.DevelopPolDiv) + roll;
        const gear = z(P.gearDev, g, key), title = z(P.titleDev, g), project = z(P.project, c, 'dev:' + key);
        const pct = Math.min(CAPS.develop, gear + title + project);
        const gain = Math.round(g0 * (1 + pct));
        return { kind: key, g0, pct, gain, roll, mods: { gear, title, project } };
      },
      // city gold income (§4.2): b × (1 + clamp(Σ, −10 %, +35 %)) × (1 + temporary event buffs, floor −50 %)
      cityGold(c) {
        const Bb = B();
        const b = c.industry * Bb.GoldPerIndustry + c.town * Bb.GoldPerTown;
        const a = admin(c);
        const titleCity = a ? z(P.titleCity, a, 'income') : 0;
        const nat = c.owner >= 0 ? z(P.titleNat, c.owner, 'natIncome') : 0;
        const item = z(P.itemCity, c, 'income');
        const project = z(P.project, c, 'income');
        const pact = z(P.pactIncome, c);
        const ov = rep(P.order, c);
        const order = ov !== undefined ? orderTerm(ov) : 0;
        const pct = clamp(titleCity + nat + item + project + pact + order, CAPS.incomeLo, CAPS.incomeHi);
        const ev = Math.max(CAPS.eventIncomeFloor, z(P.eventIncome, c));
        const gold = M.roundToInt(b * (1 + pct) * (1 + ev));
        return { b, pct, ev, gold, mods: { title: titleCity + nat, gear: item, project, pact, order, events: ev } };
      },
      // harvest (§4.3): roll = the v3 rangeFloat(0.85, 1.15), kept as rollRaw for DOMESTIC-FX tiers
      harvest(c, roll) {
        const Bb = B();
        const h0 = c.land * Bb.FoodPerLand * roll;
        const a = admin(c);
        const title = (a ? z(P.titleCity, a, 'harvest') : 0) + (c.owner >= 0 ? z(P.titleNat, c.owner, 'natHarvest') : 0);
        const item = z(P.itemCity, c, 'harvest'), project = z(P.project, c, 'harvest'), events = z(P.eventHarvest, c);
        const ov = rep(P.order, c);
        const order = ov !== undefined ? orderTerm(ov) : 0;
        const pct = clamp(title + item + project + events + order, CAPS.harvestLo, CAPS.harvestHi);
        return { h0, rollRaw: roll, pct, h: M.roundToInt(h0 * (1 + pct)), mods: { title, gear: item, project, events, order } };
      },
      // 征兵 troops per gold (§4.4): gear of the recruiter or of the administrator (the larger), title of the administrator, projects
      troopsPerGold(c, g) {
        const a = admin(c);
        const gear = Math.max(z(P.gearRecruit, g), a ? z(P.gearRecruit, a) : 0);
        const title = a ? z(P.titleCity, a, 'recruit') : 0, project = z(P.project, c, 'recruit');
        return B().TroopsPerGold * (1 + Math.min(CAPS.recruit, gear + title + project));
      },
      recruitMods(c, g) {
        const a = admin(c);
        const gear = Math.max(z(P.gearRecruit, g), a ? z(P.gearRecruit, a) : 0);
        const title = a ? z(P.titleCity, a, 'recruit') : 0, project = z(P.project, c, 'recruit');
        return { gear, title, project, pct: Math.min(CAPS.recruit, gear + title + project) };
      },
      trainGain(g, roll, c) {
        const bonus = Math.min(CAPS.train, z(P.gearTrain, g) + z(P.titleTrain, g) + (c ? z(P.project, c, 'train') : 0));
        return B().TrainBase + M.idiv(eff(g, 'war'), 10) + roll + bonus;
      },
      trainMods(g, c) {
        const gear = z(P.gearTrain, g), title = z(P.titleTrain, g), project = c ? z(P.project, c, 'train') : 0;
        return { gear, title, project, bonus: Math.min(CAPS.train, gear + title + project) };
      },
      // probabilities: v3 formula on effective attributes, every bonus added, ONE clamp at the end (§4.5)
      searchP(c, g) {
        const f = c.owner;
        return Math.min(CAPS.search, 0.22 + eff(g, 'intel') / 260 + Mods.virtue(f) / 420 + z(P.gearSearch, g) + z(P.titleSearch, g) + z(P.project, c, 'search'));
      },
      searchMods(c, g) { return { gear: z(P.gearSearch, g), title: z(P.titleSearch, g), project: z(P.project, c, 'search') }; },
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
        return M.clamp(0.4 + (eff(agent, 'intel') - eff(target, 'intel')) / 100 + z(P.gearDiscord, agent) + z(P.titleDiscord, agent), 0.1, CAPS.discord) * (1 - z(P.discordRes, target));
      },
      persuadeP(agent, target, f) {
        if (G().isRuler(target)) return 0;
        const p = (100 - target.loyalty) / 110 * (0.45 + eff(agent, 'intel') / 180) * (0.55 + Mods.virtue(f) / 220) + z(P.gearPersuade, agent) + z(P.titlePersuade, agent);
        // INTEGRATION §4.5 / TITLES §7.1：先一次截断（≤ 0.90），再乘 (1 − 对方官职的拉拢抗性)；与 discordP 同一次序
        return M.clamp(p, 0, CAPS.persuade) * (1 - z(P.persuadeRes, target));
      },
      // the bonus terms of each chance (probability points; for the result record's chips, DOMESTIC-FX §3.2)
      hireMods(target, recruiter, promise) { return { gear: z(P.gearHire, recruiter), title: z(P.titleHire, recruiter), promise: promise || 0 }; },
      allyMods(f, t, envoy) {
        const ruler = G().ruler(f);
        return { gear: Math.max(ruler ? z(P.gearDip, ruler) : 0, envoy ? z(P.gearDip, envoy) : 0), title: envoy ? z(P.titleDip, envoy) : 0, rel: z(P.rel, f, t) / 400 };
      },
      discordMods(agent, target) { return { gear: z(P.gearDiscord, agent), title: z(P.titleDiscord, agent), res: z(P.discordRes, target) }; },
      persuadeMods(agent, target) { return { gear: z(P.gearPersuade, agent), title: z(P.titlePersuade, agent), res: z(P.persuadeRes, target) }; },
      // 赏赐：v3 RewardLoyalty + R(0,4)，加上君主装备的 reward（ITEMS §3.4）
      rewardLoy(g, roll, ruler) { return B().RewardLoyalty + roll + (ruler ? z(P.gearReward, ruler) : 0); },
      // 交易：买价 × (1 − t)，卖价 × (1 + t)，t = min(0.25, 城中最好的商才 / 100)（ITEMS §3.4）
      tradeK(c) { return Math.min(0.25, Math.max(0, z(P.tradeK, c))); },
      // 同盟期限（ITEMS：dip 能力 + months）
      allianceMonths(f, t, envoy) { return B().AllianceMonths + Math.round(z(P.allyMonths, f, t, envoy)); },

      // ---- 城级（INTEGRATION §12；SG.Balance.CityLevel）----
      hubTier(c) {
        const H = B().CityLevel.HUBS;
        for (const t of [4, 3, 2]) if (H[t] && H[t].indexOf(c.name) >= 0) return t;
        return 0;
      },
      shopTier(c) {
        const S = B().CityLevel.SHOP.sum, v = c.industry + c.town;
        let n = 0; for (const x of S) if (v >= x) n++;
        return Math.max(1 + n, Econ.hubTier(c));
      },
      stageValue(id, c) {
        if (id === 'farms') return c.land;
        if (id === 'workshop') return c.industry;
        if (id === 'houses') return c.town;
        if (id === 'walls') return SGx.cityDefense ? SGx.cityDefense(c) : 20 + M.idiv(c.town, 12);
        if (id === 'market') return Econ.shopTier(c) * 1000 + c.industry + c.town;
        throw new Error('SG.Econ.stageValue: unknown core building ' + id);
      },
      stageOf(id, c, prev) {
        const CL = B().CityLevel, t = CL.T[id], v = Econ.stageValue(id, c);
        let s = 0; for (const x of t) if (v >= x) s++;
        if (prev !== undefined && prev > s && prev > 0 && v >= CL.DOWN * t[prev - 1]) s = prev;
        return Math.min(4, s);
      },
      stages(c, prev) {
        const o = {};
        for (const id of B().CityLevel.CORE) o[id] = Econ.stageOf(id, c, prev ? prev[id] : undefined);
        return o;
      },
      // 城级 0–4（= NAMES 的下标；ENGAGEMENT 的 1–5 级 = 下标 + 1）：五座核心建筑阶数的平均，四舍五入
      levelOfStages(st) {
        const CORE = B().CityLevel.CORE;
        let s = 0; for (const k of CORE) s += st[k];
        return Math.min(4, Math.floor(s / CORE.length + 0.5));
      },
      cityLevel(c, prev) { return Econ.levelOfStages(Econ.stages(c, prev)); },
      levelName(lv) { return B().CityLevel.NAMES[M.clamp(lv | 0, 0, 4)]; },

      // ---- 结果分级（DOMESTIC-FX §4.2；纯函数，只看乘成之前的数值）----
      tierDevelop(r) {
        const T = B().Tiers.develop;
        const st = v => { let n = 0; for (const x of T.stages) if (v >= x) n++; return n; };
        if (r.before < B().StatMax && r.after >= B().StatMax) return 'great';
        if (st(r.after) > st(r.before)) return 'great';
        if (r.roll === T.goodRoll || Math.floor((r.before + r.g0) / T.hundred) > Math.floor(r.before / T.hundred)) return 'good';
        return 'normal';
      },
      tierHarvest(r) {
        const T = B().Tiers.harvest;
        if (r.rollRaw < T.lean) return 'miss';
        if (r.rollRaw >= T.great && r.land >= T.greatLand) return 'great';
        if (r.rollRaw >= T.good) return 'good';
        return 'normal';
      },
    };

    // ============================================================ SG.AIBudget ==
    const AB = () => (SGx.Balance && SGx.Balance.AIBudget) || { Share: 0.22, CapMonths: 12, ReserveBase: 150, ReservePerOfficer: 8, ReserveFoodMonths: 2, FloorSalaryMonths: 2 };
    const mapPurse = new Map();
    const purseStore = opts.purse === 'map'
      ? { get: f => mapPurse.get(f) || 0, set: (f, v) => mapPurse.set(f, v), clear: () => mapPurse.clear() }
      : {
        get(f) { const g = G(); return g && g.aiBudget && g.aiBudget.purse ? g.aiBudget.purse[f] || 0 : 0; },
        set(f, v) { const g = G(); if (!g) return; if (!g.aiBudget) g.aiBudget = { purse: {} }; if (!g.aiBudget.purse) g.aiBudget.purse = {}; g.aiBudget.purse[f] = v; },
        clear() { const g = G(); if (g && g.aiBudget) g.aiBudget.purse = {}; },
      };
    // 钱袋份额：DESIGN-V4 §20 问题 2 的 Decisions.aiPurseShare（唯一的地方；Balance.AIBudget.Share 只是它的引用）；
    // 设了 AIBudget.ShareByDifficulty 时按难度
    const shareOf = () => {
      if (opts.share) return +opts.share;
      const A = AB(), g = G(), D = SGx.Balance && SGx.Balance.Decisions;
      if (A.ShareByDifficulty && g && g.difficulty && A.ShareByDifficulty[g.difficulty] > 0) return A.ShareByDifficulty[g.difficulty];
      if (D && typeof D.aiPurseShare === 'number') return D.aiPurseShare;
      return A.Share;
    };
    const AIB = {
      get share() { return shareOf(); },
      get capMonths() { return +(opts.capMonths || AB().CapMonths); },
      ORDER: ['recruit', 'develop', 'tribute', 'event', 'title', 'gear', 'project', 'festival'],
      DISCRETIONARY: new Set(['title', 'gear', 'project', 'festival']),
      purse: opts.purse === 'map' ? mapPurse : undefined,
      income: new Map(), stats: null,
      reset() { purseStore.clear(); AIB.income.clear(); AIB.stats = { byKind: {}, refused: {}, below0: 0, gross: 0 }; },
      resetStats() { AIB.stats = { byKind: {}, refused: {}, below0: 0, gross: 0 }; },
      purseOf(f) { return purseStore.get(f); },
      reserve(c) {
        const g = G(), A = AB(), offs = g.officersIn(c).length;
        return { gold: A.ReserveBase + A.ReservePerOfficer * offs, food: A.ReserveFoodMonths * M.idiv(g.troopsIn(c), B().TroopsPerFood) };
      },
      floor(c) { return AB().FloorSalaryMonths * B().SalaryPerGeneral * G().officersIn(c).length; },
      // month start of the discretionary phase: purse += share × the faction's gold income this month (capped)
      accrue(f, income) {
        const share = AIB.share, capM = AIB.capMonths;
        AIB.income.set(f, income);
        AIB.stats.gross += income;
        const p = purseStore.get(f) + share * income;
        purseStore.set(f, Math.min(p, capM * share * Math.max(1, income)));
      },
      canSpend(c, amount, kind) {
        if (!(amount > 0)) return true;
        if (kind === 'tribute' || kind === 'pact') return c.gold - amount >= AIB.floor(c);
        if (c.gold - amount < AIB.reserve(c).gold) return false;
        if (AIB.DISCRETIONARY.has(kind) && purseStore.get(c.owner) < amount) return false;
        return true;
      },
      spend(c, amount, kind) {
        if (!(amount > 0)) return;
        c.gold -= amount;
        if (AIB.DISCRETIONARY.has(kind)) purseStore.set(c.owner, purseStore.get(c.owner) - amount);
        if (c.gold < 0) AIB.stats.below0++;
        AIB.stats.byKind[kind] = (AIB.stats.byKind[kind] || 0) + amount;
      },
      trySpend(c, amount, kind) {
        if (!AIB.canSpend(c, amount, kind)) { AIB.stats.refused[kind] = (AIB.stats.refused[kind] || 0) + 1; return false; }
        AIB.spend(c, amount, kind); return true;
      },
      // the richest own city (gold above its reserve) that can pay. The spare gold is computed once per city (W2: the
      // comparator used to rebuild reserve(c) — an officersIn scan — twice per comparison); same order as before
      // (Array.prototype.sort is stable, ties keep the citiesOf order).
      payer(f, amount, kind) {
        const cs = G().citiesOf(f).map(c => ({ c, spare: c.gold - AIB.reserve(c).gold }));
        cs.sort((a, b) => b.spare - a.spare);
        for (const x of cs) if (AIB.canSpend(x.c, amount, kind)) return x.c;
        return null;
      },
    };
    AIB.reset();
    return { CAPS, Mods, Econ, AIBudget: AIB };
  }

  // ---------------------------------------------------------------- the global instance --
  // P：全局的提供者表；每个名字是一个合成函数（同名的多个提供者相加，替换型取最后一个打开的，见文件头）
  const entries = new Map();   // name → [{ fn, when, owner }]
  const P = {};
  function compose(name) {
    const list = entries.get(name) || [];
    if (!list.length) { delete P[name]; return; }
    const on = e => !e.when || e.when();
    if (REPLACE.has(name)) {
      P[name] = function (a, b, c) { for (let i = list.length - 1; i >= 0; i--) if (on(list[i])) return list[i].fn(a, b, c); return undefined; };
    } else if (ANY.has(name)) {
      P[name] = function (a, b, c) { for (const e of list) if (on(e) && e.fn(a, b, c)) return true; return false; };
    } else if (list.length === 1) {
      const e = list[0];
      P[name] = function (a, b, c) { return on(e) ? e.fn(a, b, c) || 0 : 0; };
    } else {
      P[name] = function (a, b, c) { let s = 0; for (const e of list) if (on(e)) s += e.fn(a, b, c) || 0; return s; };
    }
  }
  const core = create(SG, P, {});
  core.Mods.provide = function (table, o) {
    o = o || {};
    const added = [];
    for (const name of Object.keys(table || {})) {
      const fn = table[name];
      if (typeof fn !== 'function') throw new Error('SG.Mods.provide: ' + name + ' is not a function');
      if (!entries.has(name)) entries.set(name, []);
      const e = { fn, when: typeof o.when === 'function' ? o.when : null, owner: o.owner || '' };
      entries.get(name).push(e);
      added.push([name, e]);
      compose(name);
    }
    return function off() {
      for (const [name, e] of added) { const l = entries.get(name); const i = l ? l.indexOf(e) : -1; if (i >= 0) l.splice(i, 1); compose(name); }
    };
  };
  core.Mods.providers = function () { const o = {}; for (const [k, l] of entries) if (l.length) o[k] = l.map(e => e.owner || '?'); return o; };
  core.Mods.REPLACE = REPLACE;
  core.Mods.ANY = ANY;

  SG.EconCore = { create, BASE_CAPS, REPLACE, ANY };
  SG.Mods = core.Mods;
  SG.Econ = core.Econ;
  SG.AIBudget = core.AIBudget;
})();
