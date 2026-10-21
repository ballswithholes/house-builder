'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 内政指令的结果分级（DOMESTIC-FX §4；INTEGRATION §4.7）
   移植自 design-v4/domestic/tiers.js（同样的门槛），字段按 js/model.js 的 Commands.lastResult 记录（DOMESTIC-FX §3.2）
   与 ENGAGEMENT 的新指令 / 时刻（§5.18–§5.28）。

   纯逻辑：无 THREE / DOM，**不取任何随机数**（tests/sim.js 的状态哈希不变，tests/domestic-tiers.js 断言）。
   只读 SG.Balance、SG.G（按编号查武将 / 势力的城数），从不写游戏状态。

   SG.DomesticTiers.tier(res) → { tier: 'miss'|'normal'|'good'|'great'|null, why: [代码], stamp: '印文'|null }
     tier = null：不是一次真正的尝试（金不足、无兵、路不通、离间君主……：res.reason 有值，或交易 0 金），不演出。
     规则只看「乘成之前」的数值：开发用 g0（不看乘过装备 / 官职 / 营建的 gain），秋收用 rollRaw（INTEGRATION §4.7）。
     开发的「大成」= 对应建筑的可见阶段升高：res.stBefore / stAfter（domestic-fx.js 由城池快照填入，含回落滞后）
     优先；没有时按 SG.Balance.CityLevel.T 的门槛由 before / after 推出（= SG.Econ.tierDevelop）。
   SG.DomesticTiers.TH            全部门槛（一处定义；开发 / 秋收的常数引用 SG.Balance.Tiers）
   SG.DomesticTiers.STAMPS        大成朱印的两个字（每条指令自己的）
   SG.DomesticTiers.TRAIN_RANKS / trainRank(v) / nextRank(v) → { name, at, need } | null
   SG.DomesticTiers.oddsTier(p)   以小博大：p < 0.25 大成、< 0.45 好
   SG.DomesticTiers.RANK          { miss: -1, normal: 0, good: 1, great: 2 }   up(a, b) 取较高的一档
   SG.DomesticTiers.IDS           能分级的指令 / 时刻编号（34 段短乐的编号 + develop 的三个子项）
   SG.DomesticTiers.subId(res)    记录 → 演出 / 短乐的编号（develop → 'develop.land' …，trade → 'trade'）
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = (W.SG = W.SG || {});

  const T = Object.freeze({ MISS: 'miss', N: 'normal', G: 'good', X: 'great' });
  const RANK = Object.freeze({ miss: -1, normal: 0, good: 1, great: 2 });
  const up = (a, b) => (RANK[b] > RANK[a] ? b : a);
  const crossed = (before, after, marks) => marks.some(m => before < m && after >= m);
  const maxStat = g => (g ? Math.max(g.war | 0, g.intel | 0, g.pol | 0) : 0);

  // ---------------------------------------------------------------- 门槛（一处定义）--
  const B = () => SG.Balance || {};
  const devT = () => (B().Tiers && B().Tiers.develop) || { goodRoll: 4, hundred: 100, stages: [300, 450, 600, 800] };
  const harvT = () => (B().Tiers && B().Tiers.harvest) || { lean: 0.90, good: 1.04, great: 1.10, greatLand: 600 };
  const TH = Object.freeze({
    recruit: Object.freeze({ wan: Object.freeze([10000, 20000, 30000, 40000, 50000, 60000, 80000, 100000]), greatFullN: 3000, goodN: 2000 }),
    train: Object.freeze({ fullK: 0.9, rank80: 80, rank80Roll: 3, top: 100, goodRoll: 4 }),
    search: Object.freeze({ goodGold: 160, goodFood: 1900, star: 90 }),
    hire: Object.freeze({ great: 90, good: 75, goodP: 0.45, greatP: 0.20 }),
    move: Object.freeze({ greatGens: 5, greatTroops: 20000, goodGens: 3, goodTroops: 10000, goodHops: 3 }),
    transport: Object.freeze({ greatGold: 1500, greatFood: 15000, goodGold: 500, goodFood: 5000, rescueBefore: 2, rescueAfter: 6 }),
    odds: Object.freeze({ great: 0.25, good: 0.45 }),
    ally: Object.freeze({ bigK: 2, bigMin: 4 }),
    discord: Object.freeze({ great: 18, good: 10, defect: 40 }),
    persuade: Object.freeze({ star: 85 }),
    attack: Object.freeze({ greatTroops: 40000, rulerGens: 5, rulerTroops: 20000, goodGens: 3, goodGensTroops: 12000, goodTroops: 20000 }),
    reward: Object.freeze({ top: 100, from: 90, goodRoll: 4 }),
    trade: Object.freeze({ buyFav: 8, sellFav: 16, great: 1000, good: 600 }),
    appoint: Object.freeze({ great: 90, good: 75, gain: 15 }),
    title: Object.freeze({ great: 4, good: 3 }),
    // ENGAGEMENT（§5.18–§5.28）
    build: Object.freeze({ goodPol: 80, goodCost: 500 }),
    caravan: Object.freeze({ good: 150, great: 300 }),
    explore: Object.freeze({ goodPrestige: 10 }),
    gift: Object.freeze({ good: 10, great: 20 }),
    tribute: Object.freeze({ offerK: 2, demandCities: 3 }),
    truce: Object.freeze({ goodP: 0.45 }),
    tradepact: Object.freeze({ goodRel: 40, greatK: 2 }),
    get develop() { return devT(); },
    get harvest() { return harvT(); },
  });
  // 一般的稀有度次序（ITEMS）：rare 以上算「好」，epic / legendary 算「大成」
  const RARE_UP = { rare: 1, epic: 2, legendary: 2 };
  // 物品 → 0 / 1（稀有）/ 2（史诗以上）：{ rarity } 对象、稀有度名，或物品编号（SG.Items.tier：0 普通 … 4 传说）
  function rareLv(x) {
    if (x === null || x === undefined || x === false) return 0;
    if (typeof x === 'string') return RARE_UP[x] || 0;
    if (typeof x === 'object' && x.rarity) return RARE_UP[x.rarity] || 0;
    try { const t = SG.Items && SG.Items.tier ? SG.Items.tier(typeof x === 'object' ? x.id : x) : 0; return t >= 3 ? 2 : t === 2 ? 1 : 0; } catch (e) { return 0; }
  }

  // 大成朱印（白文两字，DOMESTIC-FX §3.7；ENGAGEMENT 的新指令 §5.18–§5.28 补齐）
  const STAMPS = Object.freeze({
    'develop.land': '丰穰', 'develop.industry': '百炼', 'develop.town': '殷阜', recruit: '云集', train: '精锐', search: '得贤',
    'search.item': '宝现', hire: '归心', move: '星驰', transport: '辇输', ally: '盟成', discord: '离心', persuade: '倒戈',
    attack: '出师', reward: '恩泽', trade: '居奇', appoint: '得人', harvest: '大有', market: '神兵', title: '册封', stageUp: '升格',
    build: '落成', caravan: '满载', explore: '探幽', gift: '交欢', marriage: '秦晋', tribute: '来朝', hostage: '归国',
    truce: '息兵', tradepact: '互市', submit: '归附', mission: '功成', festival: '同乐', wenhui: '魁首', month: '朔日',
  });

  // 训练的阶名（校场与演出里显示）
  const TRAIN_RANKS = Object.freeze([[0, '新募'], [40, '操练'], [60, '精练'], [80, '精锐'], [100, '百战']]);
  function trainRank(v) { let r = TRAIN_RANKS[0][1]; for (const [t, n] of TRAIN_RANKS) if (v >= t) r = n; return r; }
  function nextRank(v) { for (const [t, n] of TRAIN_RANKS) if (v < t) return { name: n, at: t, need: t - v }; return null; }
  const oddsTier = p => (p < TH.odds.great ? T.X : p < TH.odds.good ? T.G : T.N);

  // 按编号或对象取武将 / 势力（只读）
  const G = () => SG.G || null;
  function gen(x) {
    if (x && typeof x === 'object') return x;
    const g = G();
    return g && g.generals && x !== null && x !== undefined ? g.generals[x] || null : null;
  }
  function cityCount(f) { const g = G(); try { return g && f !== undefined && f !== null && f >= 0 ? g.cityCount(f) : 0; } catch (e) { return 0; } }
  function troopsOf(f) { const g = G(); try { return g && f >= 0 ? g.generalsOf(f).reduce((a, x) => a + (x.troops || 0), 0) : 0; } catch (e) { return 0; } }
  const num = v => (typeof v === 'number' && isFinite(v) ? v : 0);
  const stageCount = (v, t) => { let n = 0; for (const x of t) if (v >= x) n++; return n; };
  const DEV_BLDG = { land: 'farms', industry: 'workshop', town: 'houses' };

  const out = (t, why, stampId) => ({ tier: t, why, stamp: t === T.X ? STAMPS[stampId] || null : null });
  const none = why => ({ tier: null, why: [why], stamp: null });

  function tier(r) {
    if (!r || typeof r !== 'object' || !r.cmd) return none('no record');
    // 不是真正的尝试（规则拒绝：金不足、无兵、路不通、离间君主……）→ 不演出
    if (r.reason) return none(String(r.reason));
    const why = [];
    let t = T.N;
    switch (r.cmd) {
      case 'develop': {
        const D = devT();
        const kind = r.kind || 'land';
        const bldg = DEV_BLDG[kind] || 'farms';
        const thr = (B().CityLevel && B().CityLevel.T && B().CityLevel.T[bldg]) || D.stages;
        const before = num(r.before), after = num(r.after);
        const stB = typeof r.stBefore === 'number' ? r.stBefore : stageCount(before, thr);
        const stA = typeof r.stAfter === 'number' ? r.stAfter : stageCount(after, thr);
        const cap = B().StatMax || 999;
        const g0 = typeof r.g0 === 'number' ? r.g0 : after - before;   // 乘成之前的增量（INTEGRATION §4.7）
        if (stA > stB) { t = T.X; why.push('stageUp'); }
        else if (r.roll === D.goodRoll || Math.floor((before + g0) / D.hundred) > Math.floor(before / D.hundred)) { t = T.G; why.push(r.roll === D.goodRoll ? 'roll4' : 'hundred'); }
        if (after >= cap && before < cap) { t = T.X; why.push('max'); }
        return out(t, why, 'develop.' + kind);
      }
      case 'recruit': {
        const R = TH.recruit, n = num(r.n), full = num(r.after) >= num(r.max) && num(r.max) > 0;
        if (crossed(num(r.cityBefore), num(r.cityAfter), R.wan)) { t = T.X; why.push('wan'); }
        else if (full && n >= R.greatFullN) { t = T.X; why.push('full3000'); }
        else if (n >= R.goodN || full) { t = T.G; why.push(full ? 'full' : 'n2000'); }
        return out(t, why, 'recruit');
      }
      case 'train': {
        const R = TH.train, before = num(r.before), after = num(r.after);
        const top = after >= R.top && before < R.top;
        if (top && num(r.troops) >= R.fullK * num(r.max)) { t = T.X; why.push('hundred+full'); }
        else if (r.roll === R.goodRoll || top || (crossed(before, after, [R.rank80]) && r.roll >= R.rank80Roll)) { t = T.G; why.push('rank'); }
        return out(t, why, 'train');
      }
      case 'search': {
        const R = TH.search;
        if (r.outcome === 'talent') return { tier: T.X, why: [maxStat(gen(r.found)) >= R.star ? 'star' : 'talent'], stamp: STAMPS.search };
        if (r.outcome === 'item') return { tier: T.X, why: ['item'], stamp: STAMPS['search.item'] };
        if (r.outcome === 'gold') return out(num(r.value) >= R.goodGold ? T.G : T.N, ['gold'], 'search');
        if (r.outcome === 'food') return out(num(r.value) >= R.goodFood ? T.G : T.N, ['food'], 'search');
        return { tier: T.MISS, why: ['none'], stamp: null };
      }
      case 'hire': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        const R = TH.hire, m = maxStat(gen(r.target)), p = typeof r.p === 'number' ? r.p : 1;
        t = m >= R.great ? T.X : m >= R.good ? T.G : T.N;
        if (p < R.goodP) t = up(t, T.G);          // 以小博大，至少「好」
        if (p < R.greatP) t = T.X;
        why.push('max' + m, 'p' + Math.round(p * 100));
        return out(t, why, 'hire');
      }
      case 'move': {
        if (r.ok === false) return none('failed');
        const R = TH.move, gens = num(r.gens) || 1, troops = num(r.troops);
        if ((r.frontline && troops >= R.greatTroops) || gens >= R.greatGens) { t = T.X; why.push('massing'); }
        else if (gens >= R.goodGens || troops >= R.goodTroops || num(r.hops) >= R.goodHops) { t = T.G; why.push('big'); }
        return out(t, why, 'move');
      }
      case 'transport': {
        if (r.ok === false) return none('failed');
        const R = TH.transport, need = num(r.destNeed), before = num(r.destFoodBefore), gold = num(r.gold), food = num(r.food);
        if (gold <= 0 && food <= 0) return none('empty');
        const rescue = need > 0 && before < R.rescueBefore * need && before + food >= R.rescueAfter * need;
        if (rescue || gold >= R.greatGold || food >= R.greatFood) { t = T.X; why.push(rescue ? 'rescue' : 'huge'); }
        else if (gold >= R.goodGold || food >= R.goodFood) { t = T.G; why.push('big'); }
        return out(t, why, 'transport');
      }
      case 'ally': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        const R = TH.ally, p = typeof r.p === 'number' ? r.p : 1;
        const tc = typeof r.targetCities === 'number' ? r.targetCities : cityCount(r.target);
        const oc = typeof r.ourCities === 'number' ? r.ourCities : cityCount(r.f);
        t = oddsTier(p);
        if (tc >= oc) { t = up(t, T.G); why.push('peer'); }
        if (tc >= R.bigK * oc && tc >= R.bigMin) { t = T.X; why.push('great power'); }
        why.push('p' + Math.round(p * 100));
        return out(t, why, 'ally');
      }
      case 'discord': {
        if (!r.ok) return { tier: T.MISS, why: ['seen through'], stamp: null };
        const R = TH.discord, d = num(r.d), la = num(r.loyaltyAfter);
        if (d >= R.great || (la < R.defect && la + d >= R.defect)) { t = T.X; why.push(d >= R.great ? 'deep' : 'wavering'); }
        else if (d >= R.good) { t = T.G; why.push('d10'); }
        return out(t, why, 'discord');
      }
      case 'persuade': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        // model.js：献城 surrendered: true（res.city 是施计者所在城的编号，不是布尔值）；原型用 city: true
        const city = r.surrendered === true || r.city === true;
        t = (city || maxStat(gen(r.target)) >= TH.persuade.star) ? T.X : T.G;
        why.push(city ? 'city' : 'defector');
        return out(t, why, 'persuade');
      }
      case 'attack': {
        const R = TH.attack, troops = num(r.troops), gens = num(r.gens);
        if (troops >= R.greatTroops || (r.ruler && gens >= R.rulerGens && troops >= R.rulerTroops)) { t = T.X; why.push('grand army'); }
        else if ((gens >= R.goodGens && troops >= R.goodGensTroops) || troops >= R.goodTroops) { t = T.G; why.push('big'); }
        return out(t, why, 'attack');
      }
      case 'reward': {
        const R = TH.reward, before = num(r.before), after = num(r.after);
        const gift = r.gift !== null && r.gift !== undefined && r.gift !== false;
        if (r.giftFits || (after >= R.top && before < R.top && r.roll === R.goodRoll)) { t = T.X; why.push(r.giftFits ? 'fit gift' : 'loyal100+roll4'); }
        else if (r.roll === R.goodRoll || (after >= R.top && before <= R.from) || gift) { t = T.G; why.push(gift ? 'gift' : 'roll4/100'); }
        return out(t, why, 'reward');
      }
      case 'trade': {
        if (r.ok === false) return none('empty');
        const R = TH.trade, price = num(r.price);
        const fav = r.side === 'buy' ? price <= R.buyFav : price >= R.sellFav;
        const volume = r.side === 'buy' ? num(r.gold) : num(r.goldOut);
        if (fav && volume >= R.great) { t = T.X; why.push('corner'); }
        else if (fav || volume >= R.good) { t = T.G; why.push(fav ? 'price' : 'volume'); }
        return out(t, why, 'trade');
      }
      case 'appoint': {
        const R = TH.appoint, pol = num(r.pol), prev = num(r.prevPol);
        if (pol >= R.great) { t = T.X; why.push('pol90'); }
        else if (pol >= R.good || (prev > 0 && pol - prev >= R.gain)) { t = T.G; why.push('fit'); }
        return out(t, why, 'appoint');
      }
      case 'harvest': {
        const H = harvT(), roll = typeof r.rollRaw === 'number' ? r.rollRaw : num(r.roll);   // rollRaw = v3 的 rangeFloat(0.85, 1.15)
        if (roll < H.lean) return { tier: T.MISS, why: ['lean year'], stamp: null };
        if (roll >= H.great && num(r.land) >= H.greatLand) return { tier: T.X, why: ['bumper'], stamp: STAMPS.harvest };
        if (roll >= H.good) return { tier: T.G, why: ['good year'], stamp: null };
        return { tier: T.N, why: ['year'], stamp: null };
      }
      case 'market': {   // ITEMS：买入按稀有度；卖出一律普通
        if (r.side === 'sell') return { tier: T.N, why: ['sell'], stamp: null };
        const k = RARE_UP[r.rarity] || 0;
        t = k === 2 ? T.X : k === 1 ? T.G : T.N;
        return out(t, [r.rarity || 'common'], 'market');
      }
      case 'title': {    // TITLES：所授官位的品阶 1–5
        const R = TH.title, k = num(r.titleTier);
        t = k >= R.great ? T.X : k >= R.good ? T.G : T.N;
        return out(t, ['t' + k], 'title');
      }
      // ---------------------------------------------------- ENGAGEMENT（§5.18–§5.28）--
      case 'build': {
        const R = TH.build, sup = gen(r.supervisor !== undefined ? r.supervisor : r.gen), done = !!(r.done || r.sub === 'done');
        if (done || r.wonder) { t = T.X; why.push(done ? 'done' : 'wonder'); }
        else if ((sup && sup.pol >= R.goodPol) || num(r.cost) >= R.goodCost) { t = T.G; why.push('big'); }
        return out(t, why, 'build');
      }
      case 'caravan': {
        if (r.lost) return { tier: T.MISS, why: ['lost'], stamp: null };
        if (r.sub === 'depart' || r.profit === undefined || r.profit === null) return { tier: T.N, why: ['depart'], stamp: null };
        const R = TH.caravan, items = Array.isArray(r.items) ? r.items : r.item !== undefined && r.item !== null ? [r.item] : [];
        const rare = items.some(x => rareLv(x) >= 1);
        if (num(r.profit) >= R.great || rare) { t = T.X; why.push(rare ? 'rare' : 'profit300'); }
        else if (num(r.profit) >= R.good || items.length) { t = T.G; why.push(items.length ? 'item' : 'profit150'); }
        return out(t, why, 'caravan');
      }
      case 'explore': {
        // ENGAGEMENT 的记录：sub 'depart' / 'return'，归来时 clue、item、ok = false（失手）；也认 outcome 字段
        const oc = r.outcome || (r.sub === 'return' ? (r.ok === false ? 'mishap' : r.clue ? 'clue' : r.item !== undefined && r.item !== null ? 'item' : 'exp') : null);
        if (!oc) return { tier: T.N, why: ['depart'], stamp: null };
        if (oc === 'mishap') return { tier: T.MISS, why: ['mishap'], stamp: null };
        const rare = (r.rarity ? RARE_UP[r.rarity] || 0 : rareLv(r.item)) >= 1;
        if ((oc === 'item' && rare) || oc === 'clue') { t = T.X; why.push(oc === 'clue' ? 'clue' : 'rare'); }
        else if (oc === 'item' || num(r.prestige) >= TH.explore.goodPrestige) { t = T.G; why.push('find'); }
        return out(t, why, 'explore');
      }
      case 'gift': {
        const R = TH.gift, d = num(r.dRel), item = r.item !== null && r.item !== undefined && r.item !== false;
        if (d >= R.great || item) { t = T.X; why.push(item ? 'item' : 'dRel20'); }
        else if (d >= R.good) { t = T.G; why.push('dRel10'); }
        return out(t, why, 'gift');
      }
      case 'marriage': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        return { tier: T.X, why: ['wed'], stamp: STAMPS.marriage };
      }
      case 'tribute': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        const R = TH.tribute;
        const tc = typeof r.targetCities === 'number' ? r.targetCities : cityCount(r.target);
        const oc = typeof r.ourCities === 'number' ? r.ourCities : cityCount(r.f);
        if (r.mode === 'demand') { t = tc >= R.demandCities ? T.X : T.G; why.push('demand'); }
        else if (tc >= R.offerK * oc) { t = T.G; why.push('offer big'); }
        return out(t, why, 'tribute');
      }
      case 'hostage': {
        if (r.back) return { tier: T.X, why: ['home'], stamp: STAMPS.hostage };
        return { tier: T.N, why: ['depart'], stamp: null };
      }
      case 'truce': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        const p = typeof r.p === 'number' ? r.p : 1;
        const theirs = typeof r.targetTroops === 'number' ? r.targetTroops : troopsOf(r.target);
        const ours = typeof r.ourTroops === 'number' ? r.ourTroops : troopsOf(r.f);
        if (theirs >= ours && theirs > 0) { t = T.X; why.push('peace with a peer'); }
        else if (p < TH.truce.goodP) { t = T.G; why.push('p' + Math.round(p * 100)); }
        return out(t, why, 'truce');
      }
      case 'tradepact': {
        if (!r.ok) return { tier: T.MISS, why: ['refused'], stamp: null };
        const R = TH.tradepact;
        const tc = typeof r.targetCities === 'number' ? r.targetCities : cityCount(r.target);
        const oc = typeof r.ourCities === 'number' ? r.ourCities : cityCount(r.f);
        if (tc >= R.greatK * oc) { t = T.X; why.push('big partner'); }
        else if (num(r.rel) >= R.goodRel) { t = T.G; why.push('friend'); }
        return out(t, why, 'tradepact');
      }
      case 'submit': return { tier: T.X, why: ['submit'], stamp: STAMPS.submit };
      case 'mission': {
        // 国策的品阶：missionTier（推荐字段名：res.tier 是 model.js 写结果分级的字段）；旧写法 tier: 1–3 也认
        const k = typeof r.missionTier === 'number' ? r.missionTier : typeof r.tier === 'number' ? r.tier : 1;
        t = k >= 3 ? T.X : k >= 2 ? T.G : T.N;
        return out(t, ['t' + k], 'mission');
      }
      case 'festival': { const gr = r.grade || r.sub || 'modest'; t = gr === 'grand' ? T.X : T.N; return out(t, [gr], 'festival'); }
      case 'wenhui': { const pl = num(r.place); t = pl === 1 ? T.X : pl === 2 ? T.G : T.N; return out(t, ['place' + pl], 'wenhui'); }
      case 'stageUp': return { tier: T.X, why: ['stage'], stamp: STAMPS.stageUp };
      case 'month': return { tier: T.N, why: ['month'], stamp: null };
      default: return { tier: T.N, why: ['unknown'], stamp: null };
    }
  }

  // 记录 → 演出 / 短乐 / 图标的编号
  function subId(r) {
    if (!r) return null;
    if (r.cmd === 'develop') return 'develop.' + (r.kind || 'land');
    return r.cmd;
  }

  const IDS = Object.freeze(['develop.land', 'develop.industry', 'develop.town', 'recruit', 'train', 'search', 'hire', 'move', 'transport',
    'ally', 'discord', 'persuade', 'attack', 'reward', 'trade', 'appoint', 'market', 'title', 'harvest', 'stageUp', 'month',
    'build', 'caravan', 'explore', 'gift', 'marriage', 'tribute', 'hostage', 'truce', 'tradepact', 'submit', 'mission', 'festival', 'wenhui']);

  SG.DomesticTiers = { tier, subId, STAMPS, TH, TRAIN_RANKS, trainRank, nextRank, oddsTier, RANK, up, IDS, maxStat };
})();
