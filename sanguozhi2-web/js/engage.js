'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 非战斗玩法（ENGAGEMENT.md；INTEGRATION §3–§6）
   民心 · 声望 · 邦交（好感与盟约）· 羁绊 · 事件（33 种，带选项的卡）· 国策 · 营建 · 商队 · 探索 · 文会 · 节庆 · 功业 · 王道 / 天命 /
   万国来朝与天下榜 · 内政经验（每人每月常规上限 20）· 电脑的同一套规则 · 存档 v4 的迁移。
   纯逻辑：无 THREE / DOM，Node 可测（tests/engage.js）。数据：js/engage-data.js（SG.EngageData，tools/gen-engage.js 生成）。
   移植自 design-v4/engage/engage-proto.js（同样的规则与数字），接在第四版的整合核心上：
     数字一律以「提供者」交给 SG.Mods / SG.Econ（民心项、营建、事件增益、通商协定、声望人望、事件德、羁绊防护与战斗、
     不战、攻城所需兵力比、城防、人口增长、新兵训练、藏宝图、交易）；电脑花钱一律经 SG.AIBudget（event · tribute · project · festival）；
     月末各相经 StrategyAI.on / onSpend（INTEGRATION §6.3）；指令的副作用经 SG.Events 'command'；攻城经 'conquest.prepare / apply'。
   随机数：本模块从不取 SG.Random；每条规则用自己的种子流 rng(月序, 标签…) = SG.SeededRandom(FNV(G.seed | 月序 | 标签))。
   关掉（SG.Engage.enabled = false）时不建任何状态、提供者全为 0 / 缺省、钩子不运行：第四版其余部分与没有本模块时逐位相同。
   子开关（INTEGRATION §1.2）：events order prestige projects bonds diplomacy trade exped festivals wenhui；另 aiTribute aiProjects（缺省开，
   ENGAGEMENT Q3）、aiSubmit（电脑之间的归附，缺省关，Q4）。可调的数一律在 SG.Engage.CFG（一处）；DESIGN-V4 §20 的玩家决定读
   SG.Balance.Decisions（wangdaoPrestige、wangdaoOrder、orderIncomeWeight 由 SG.Econ 读）。

   ---- 存档（ENGAGEMENT §20，INTEGRATION §6.1；全部是普通 JSON，读档后逐字节相同）----
   city.order = 0…100     city.eng = { buffs: [{ mod, v, until }], flags: { plague: until … }, cd: { eventId: 月序 }, lastDis, proj: { id, work, sup } | null,
                                       done: [projectId], maxLv, trouble }
   faction.prestige = 整数   faction.eng = { fameEv, virtueEv, cd, recent, refused: { 势力: until }, lastDemand, festivalAuto, lastSubmit, lastGift, marriages, lastAttack }
   gen.away = { kind: 'caravan'|'exped'|'hostage'|'leave', until, home, ref } | 无     gen.injury（共用，INTEGRATION §6.4）    gen.dexp（model.js）
   G.engage = { v, start, rel: { 'a-b': 好感 }, war: { 'a-b': 月序 }, pacts: [{ a, b, kind, since, until, payer, pay, hostage }], bonds: { 'a-b': { k, a, m, s } },
                cards: [待答的卡], missions, mOffers, caravans, exped, counters: { 'f:键': n }, ach: [id], achAt: { id: 月序 }, visited, visitedAt, clues: [{ item, city }],
                vlog, log: [{ m, kind, ref, v }]（≤ 200）, seq, xp: { m, u }, gflags, disc, hist, ys, wenhui, victory, scoreGame }

   ---- SG.Engage（ENGAGEMENT §2.1）----
   enabled version CFG stats · init(G) migrate(G, from) load(G, data) saveKeys ['engage']（SG.MigrateV4 自动发现：第三个迁移）
   monthEnd(ctx)（第 3 相；StrategyAI 的钩子调用）· monthStartCards(f) → [card]（≤ 1 张随机 + ≤ 1 张定期，R1）· resolve(card, optionId, opts?) → { ok, effects, roll, tier, text }
   dismiss(card)（× 关闭：安全的免费项，R4）· pendingCards(f) · onCommand(cmd, res)
   order(c) orderTarget(c) prestige(f) prestigeName(f) fameBonus(f) virtueBonus(f) addPrestige(f, v, why)
   rel(a, b) addRel(a, b, d) atWar(a, b) pacts(f) pactOf(a, b, kind?) noAttack(a, b) needK(f, t) betrayalCost(f, t)
   chance(kind, f, t, opts) propose(kind, f, t, opts) → { ok, text, pact, p }    kind ∈ gift ally marriage tribute demand truce trade
   bond(a, b) bondsOf(gen) addAff(a, b, d) battleBonus(unit, model) → { atk, def } isRival(a, b) rivalDmg(a, b)
   buildable(c) → [{ id, name, cost, work, ok, why, months }] startProject(c, id, supId) project(c) projects(c) hasProject(c, id) projectName(id, fam)
   caravanTargets(c, leader) → [{ city, hops, margin, risk, months, expected, foreign }] sendCaravan(c, leader, cityId, invest) caravans(f)
   sites(f, c) → [{ site, hops, months, cost, ok, why }] sendExpedition(c, leader, siteId) expeditions(f)
   missionOffers(f) acceptMission(f, offerId) cancelMission(f, id) missions(f) missionProgress(m)
   wenhuiEntrants(f) setWenhuiEntrants(f, ids) runWenhui(opts?) debate(state, stance) → state   touhuScore(throws)
   festivalOf(f) festivalCost(f) holdFestival(f, choice, opts)
   achievements(f) → [{ id, done, month }] victory(f) → { wangdao, tianming, wanguo } score(f) leaderboard() yearReview(f)
   vetLevel(gen) tourneyResult(res) tourneyPicks(f) shopTierBonus(c) shopUncommonSlots(c) defense(c)
   提供者（owner 'engage'，when = 开着且本局有状态）：order project eventIncome eventHarvest pactIncome prestigeFame eventFame eventVirtue rel
     bondShield bondAtk bondDef noAttack needK cityDef popGrowth recruitDrill treasureMapK tradeK persuadeRes
   月末（StrategyAI）：'income'（屯田、秋收后清掉收成增益）· 'engage'（月末全部规则）· onSpend('project')（电脑营建）· onSpend('festival')（电脑节庆）·
     'news'（外出到期、城级新闻、年度快照、师徒传艺）
   事件（SG.Events）：command · conquest.prepare · conquest.apply · faction.death · title.grant · tourney.result；发出 'engage.card' (card)、
     'engage.achievement' (id)、'engage.project' (c, id)、'engage.victory' (kind)
   ========================================================================== */
(function () {
  const SG = window.SG;
  const G = () => SG.G;
  const D = () => SG.EngageData || null;
  const B = () => SG.Balance;
  const DEC = () => (SG.Balance && SG.Balance.Decisions) || {};

  // ================================================================ 配置（一处）==
  const CFG = {
    // §3.1 民心
    order: { base: 45, virtueDiv: 5, townDiv: 150, startLo: 30, startHi: 75, empty: 45, targetEmpty: 40, targetHi: 95, up: 2, down: 1,
      govPol: 80, govAdd: 3, recruitPer: 3000, conquestDrop: 15, conquestFloor: 15, repelled: 3, absorbAdd: 5 },
    // §3.2 声望
    prestige: { conquest: 5, destroy: 20, defend: 2, project: 10, wonder: 50, wonderMonthly: 5, cityLevel: 10, alliance: 5, marriage: 20, truce: 5,
      tributeIn: 1, submit: 30, betray: 20, betrayMarriage: 50, wenhuiHost: 20, tourney: [50, 25, 12], tourneyHost: 20 },
    famePer: 150, giftPrestigeDiv: 2000, giftPrestigeMax: 1.5,
    prestigeNames: [[3000, '万世景仰'], [1500, '四海归心'], [1000, '天下闻名'], [600, '名动一方'], [300, '闻名州郡'], [100, '小有名气'], [0, '无闻']],
    // §4 内政经验（常规；上限 SG.Mods.CAPS.routineCap = 20 / 人 / 月）
    exp: { develop: 6, developBig: 2, developBigGain: 20, recruit: 3, train: 3, search: 4, searchFound: 12, hireOk: 10, hireFail: 2, envoyOk: 12, envoyFail: 3,
      discordOk: 6, persuadeOk: 15, plotFail: 2, governor: 3, governorHi: 1, governorOrder: 50, governorOrderHi: 75, supervisor: 3, academy: 2, academyIntel: 60, mentor: 4,
      routineCap: 20 },
    // §6 事件
    events: { pBase: 0.28, pPerCity: 0.025, pMax: 0.55, recentMonths: 6, disasterGap: 12, globalP: 1 / 40, pairK: 0.5, spreadP: 0.25, spreadMonths: 3, spreadPop: 0.02, spreadOrder: 4 },
    // R2「小事委任」：这些事件的卡在设置为「小事委任」时按 ★ 自动回答并列入新闻（其余一律询问；降将、结义、归附、联姻、索贡永远询问）
    minorEvents: ['bumper', 'volunteers', 'banquet', 'homesick', 'restless', 'quake', 'fire', 'nile'],
    delegate: 'minor',            // Node / 无设置时的缺省（设置第 51 行 engage.delegate：'minor' 小事委任 · 'ask' 全部询问）
    // §3.3 / §10 邦交
    rel: { attack: -25, conquer: -15, discord: -5, persuade: -8, ally: 10, truce: 15, marriage: 30, tribute: 10, trade: 5, caravan: 3, warMonths: 12, friendly: 60,
      floors: { marriage: 50, ally: 30, tribute: 20, truce: 20, trade: 20 }, inner: 30, innerEvery: 2, outerEvery: 3, allyMin: -20 },
    gift: { min: 50, cap: 25, base: 8, perCity: 2, halfMonths: 6 },
    marriage: { months: 48, cost: 300, costPerCity: 50, max: 3, base: 0.20, relDiv: 250, virtueDiv: 400, smaller: 0.10, prestigeDiv: 5000, prestigeMax: 0.10, lo: 0.05, hi: 0.85, minRel: 30, virtueEv: 2 },
    tribute: { months: 12, share: 0.08, shareLo: 0.05, shareHi: 0.20, base: 0.40, relDiv: 300, lo: 0.05, hi: 0.95, breakRel: -20 },
    demand: { ratio: 2, base: 0.15, perRatio: 0.15, fameDiv: 400, relDiv: 400, lo: 0.05, hi: 0.75, refuseRel: -20 },
    truce: { months: 12, base: 0.20, incomeK: 6, relDiv: 300, troopK: 0.2, lo: 0.05, hi: 0.80 },
    trade: { months: 24, minRel: 10, base: 0.35, relDiv: 200, polDiv: 400, lo: 0.10, hi: 0.90, margin: 0.05, income: 0.03 },
    hostage: { base: 0.10, statDiv: 600, exp: 20, loyalty: 5, partnerBreakVirtue: -10 },
    betray: { marriageVirtue: -15, otherVirtue: -8, marriageRel: -80, alliesRel: -20 },
    aiDemand: { ratio: 2.5, maxRel: 20, p: 1 / 12, everyMonths: 12, share: 0.08, refuseRel: -15, refuseMonths: 12, needK: -0.15 },
    aiOffers: { ally: { rel: 40, p: 0.08 }, marriage: { rel: 60, p: 0.02 }, trade: { rel: 30, p: 0.04 }, waitMonths: 1 },
    aiTribute: { ratio: 2.5, minRel: -30, p: 0.12, acceptBase: 0.5, acceptRelDiv: 200, acceptLo: 0.1, acceptHi: 0.9 },
    aiMarriage: { rel: 50, p: 0.05 },
    submit: { maxCities: 3, ratio: 2, virtue: 50, rel: 50, base: 0.01, relDiv: 1000, ratioK: 0.005, ratioMax: 10, virtueDiv: 2000, prestigeDiv: 40000, pMax: 0.06, digest: 36,
      loyalty: 60, loyaltyVirtueDiv: 5, rulerAdd: 10, loLo: 50, loHi: 95, tributeMonths: 12, declineRel: 5 },
    // §11 羁绊
    bonds: { cityAff: 1, cityAffMax: 60, apartDecay: 0.5, winAff: 4, friendAt: 50, feudAt: -50, friendDrop: 30, feudDrop: -30, oathAt: 70, maxSworn: 3,
      mentorStat: 80, mentorLevel: 5, mentorGap: 3, mentorMax: 2, perCity: 6, friendLoyMax: 90, feudLoyFloor: 60,
      atk: { sworn: 0.04, friend: 0.02, spouse: 0.03, rival: 0.03, feud: -0.04 }, def: { sworn: 0.04, feud: -0.04 }, yifen: 0.06, cap: 0.08,
      shield: { sworn: 30, kin: 15, spouse: 15 }, persuadeRes: { sworn: 0.5, kin: 0.3 }, swornFollowP: 0.25, swornLoss: 15, rivalDmg: 0.04, rivalTitleLoss: 5,
      learnP: 0.25, passedOverAff: -10 },
    // §8 营建
    projects: { base: 10, polDiv: 8, absent: 5, newMonths: 3, aiP: 1 / 6,
      aiBorder: ['walls', 'beacons', 'drill', 'market', 'irrigation'], aiInner: ['market', 'irrigation', 'temple', 'granary', 'academy'],
      wonderLevel: 3, beaconsDef: 5, tuntianFood: 0.25 },
    // §9 商队
    caravan: { maxBase: 1, perCities: 4, max: 4, investMin: 100, investBase: 200, investPerTier: 150, investMax: 800, maxHops: 6,
      base: 0.04, hop: 0.02, foreign: 0.05, tier: 0.02, polDiv: 800, market: 0.04, pact: 0.05, sea: 0.06, cap: 0.30, ownHalf: 0.5,
      riskHop: 0.02, riskWarDiv: 200, riskOrder: 1.3, riskMax: 0.30, roadsRisk: 0.5, lostK: 0.5, lostExp: 5, exp: 10, expHop: 4, foreignPrestige: 2,
      itemP: 0.35, itemHop: 0.05, itemW: [0.80, 0.19, 0.01] },
    // §12 探索
    exped: { cost: 100, costHop: 25, maxHops: 8, revisit: 24, months: 2, maxMonths: 6, rareMax: 0.06, rareHop: 0.005, exp: 30, expHop: 10, myth: 10, talent: 0.06,
      mishapMonths: 2, mishapExp: 15, two: 8, rareFallbackGold: 150, clueK: 1 },
    // §7 国策
    missions: { slots: 3, offers: 2, every: 3, offerMonths: 3, militaryAfter: 24, weakK: 2.5 },
    // §13 文会
    wenhui: { month: 9, n: 3, min: 70, academy: 5, prizes: [{ gold: 300, prestige: 50, exp: 80, item: 'rare', fame: 1 }, { gold: 150, prestige: 25, exp: 50, item: 'uncommon' }, { gold: 80, prestige: 12, exp: 35, item: 'common' }],
      entrantExp: 20, hostOrder: 5, types: ['civbook', 'milbook', 'music'], rareFallbackGold: 150, debateMax: 12, autoDebate: 0 },
    // §5.3 节庆
    festival: { templeK: 1.5, aiGrandK: 3, touhuMax: 2 },
    // §15 胜利
    victory: { hold: 6, prosperBig: 2, peaceMonths: 24, friendRel: 30, friendsMin: 4, tianmingPrestige: 2000, tianmingFame: 90, wanguoPacts: 12, wanguoRegions: 5, wanguoProfit: 8000, wanguoPrestige: 2000 },
    // §15.5 天下榜
    score: { city: 20, devDiv: 100, prestigeDiv: 10, pact: 10, histYears: 10 },
    logMax: 200,
    profile: false,              // 测试：月末各段的耗时记入 stats.parts
    clock: null,                 // 计时用的时钟（null = performance.now）
    // 文案
    text: {
      place: { hua: ['洛阳太学', '襄阳学舍', '北海郑玄门下', '颍川书院', '鸿都门学'], roman: ['雅典学园', '亚历山大图书馆'], persia: ['泰西封的祭司学堂'], indic: ['那烂陀精舍', '塔克西拉'], any: ['名师门下', '四方名都'] },
      home: { hua: '中原', han3: '三韩', wa: '倭国', steppe: '草原', oasis: '西域', indic: '天竺', persia: '波斯', arabia: '阿拉伯', nile: '尼罗河畔', roman: '罗马', north: '北方林地', nanhai: '南海' },
    },
  };
  const FAMILY_OF = c => { const d = D(); return (d && d.family[c]) || 'hua'; };

  // ================================================================ 开关与工具 ==
  const Engage = {
    enabled: true, version: 1,
    events: true, order: true, prestige: true, projects: true, bonds: true, diplomacy: true, trade: true, exped: true, festivals: true, wenhui: true,
    aiTribute: true, aiProjects: true, aiSubmit: false,
  };
  const on = () => Engage.enabled !== false && !!D();
  const live = () => { if (!on()) return false; const g = G(); return !!(g && g.engage); };
  const sub = k => live() && Engage[k] !== false;
  const st = () => G().engage;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const sum = (a, fn) => { let s = 0; for (const x of a) s += fn ? fn(x) : x; return s; };
  const avg = a => (a.length ? sum(a) / a.length : 0);
  function hash32(str) { let h = 0x811c9dc5; for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; }
  const rngAt = (mi, ...k) => SG.SeededRandom(hash32(G().seed + '|' + mi + '|' + k.join('|')));
  const rng = (...k) => rngAt(G().monthIndex, ...k);
  const pick = (k, arr) => arr[Math.floor(k.nextDouble() * arr.length)];
  const inc = c => c.industry * B().GoldPerIndustry + c.town * B().GoldPerTown;
  const harv = c => c.land * B().FoodPerLand;
  const pkey = (a, b) => (a < b ? a + '-' + b : b + '-' + a);
  const famOfCity = c => FAMILY_OF(c.culture || 'han');
  const famOfFac = f => FAMILY_OF(G().factions[f].culture || 'han');
  const famOfGen = x => FAMILY_OF(x.culture || 'han');
  const Mods = () => SG.Mods;
  const virtueOf = f => (MEMO ? memoF(f, 'virtue', () => virtue0(f)) : virtue0(f));
  const fameOf = f => (MEMO ? memoF(f, 'fame', () => fame0(f)) : fame0(f));
  const virtue0 = f => (SG.Mods ? SG.Mods.virtue(f) : G().factions[f].virtue);
  const fame0 = f => (SG.Mods ? SG.Mods.fame(f) : G().factions[f].fame);
  const adminOf = c => (SG.Mods ? SG.Mods.admin(c) : c.governor >= 0 ? G().generals[c.governor] : null);
  const isRuler = x => { const g = G(); return x && x.faction >= 0 && g.factions[x.faction] && g.factions[x.faction].ruler === x.id; };
  const absent = x => { const g = G(); return !!(x && ((x.away && x.away.until > g.monthIndex) || (x.injury && x.injury.away && x.injury.until > g.monthIndex))); };
  const present = (x, f) => x && !x.dead && x.faction === f && !absent(x);
  function emit(name, a, b, c) { const E = SG.Events; if (E) E.emit(name, a, b, c); }
  const now = () => (CFG.clock ? CFG.clock() : typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());
  const STATS0 = () => ({ events: {}, eventsByFac: 0, facMonths: 0, evValue: 0, gross: 0, pacts: {}, aiTribute: 0, aiMarriage: 0, aiProj: 0, demand: 0, submit: 0, offers: 0,
    cards: 0, delegated: 0, overflow: 0, ms: 0, msMax: 0, months: 0, festivals: { grand: 0, modest: 0, skip: 0 }, projectsDone: 0, defectFollow: 0, wenhui: 0 });
  Engage.stats = STATS0();

  // 每月的武将索引（城 → 在城中的本势力武将），月末各相开始时重建（规则只读，不进存档）
  let IDX = null, IDXm = -1, IDXg = null;
  function gensIn(c) {
    const g = G();
    if (IDXg !== g || IDXm !== g.monthIndex || !IDX) reindex();
    return (IDX.get(c.id) || []).filter(x => x.faction === c.owner && !x.dead && !absent(x));
  }
  function reindex() {
    const g = G();
    IDX = new Map(); IDXg = g; IDXm = g.monthIndex;
    for (const x of g.generals) { if (x.dead || x.faction < 0) continue; let a = IDX.get(x.city); if (!a) IDX.set(x.city, a = []); a.push(x); }
  }
  const troopsAt = c => { let t = 0; for (const x of gensIn(c)) t += x.troops; return t; };
  // R(c) = 150 + 8 × 武将数 金、两个月军粮（SG.AIBudget.reserve 的同一公式，用本模块的城索引计数：不扫全体武将）
  function reserveOf(c) {
    const A = SG.Balance && SG.Balance.AIBudget;
    if (!A) return SG.AIBudget ? SG.AIBudget.reserve(c) : { gold: 150, food: 0 };
    return { gold: A.ReserveBase + A.ReservePerOfficer * gensIn(c).length, food: A.ReserveFoodMonths * Math.floor(troopsAt(c) / B().TroopsPerFood) };
  }
  const dirtyIdx = () => { IDX = null; if (MEMO) { MEMO.nb = new Map(); MEMO.nbAll = null; MEMO.co = null; MEMO.pw = null; MEMO.f = new Map(); MEMO.hid = null; } };

  // ================================================================ 状态 ==
  function newCityEng() { return { buffs: [], flags: {}, cd: {}, lastDis: -999, proj: null, done: [] }; }
  function newFacEng() { return { fameEv: 0, virtueEv: 0, cd: {}, recent: {}, refused: {}, lastDemand: -999, festivalAuto: null, lastSubmit: -999, lastGift: {}, marriages: 0, lastAttack: -999 }; }
  function orderStart(c, g) {
    const O = CFG.order;
    if (c.owner < 0) return O.empty;
    return clamp(Math.round(O.base + g.factions[c.owner].virtue / O.virtueDiv + c.town / O.townDiv), O.startLo, O.startHi);
  }
  function buildState(g) {
    const d = D();
    g.engage = { v: 1, start: g.monthIndex, rel: {}, war: {}, pacts: [], bonds: {}, cards: [], missions: [], mOffers: [], caravans: [], exped: [], counters: {},
      ach: [], achAt: {}, visited: [], visitedAt: {}, clues: [], vlog: {}, log: [], seq: 1, xp: { m: g.monthIndex, u: {} }, gflags: {}, disc: {}, hist: {}, ys: null,
      wenhui: null, victory: {}, scoreGame: null };
    for (const c of g.cities) { c.order = orderStart(c, g); c.eng = newCityEng(); }
    for (const f of g.factions) { f.prestige = f.prestige | 0; f.eng = newFacEng(); }
    for (const x of g.generals) if (!(x.dexp >= 0)) x.dexp = 0;
    // 初始邦交（engagement.json relations；'*地域' = 该地域的每个势力）
    const fk = new Map(g.factions.map(f => [f.key, f.id]));
    for (const [a, b, v] of d.relations) {
      const ia = fk.get(a); if (ia === undefined) continue;
      if (b[0] === '*') { const reg = b.slice(1); for (const f of g.factions) if (f.id !== ia && f.region === reg) setRel(ia, f.id, v); }
      else { const ib = fk.get(b); if (ib !== undefined) setRel(ia, ib, v); }
    }
    // 初始羁绊（engagement.json bonds；师徒 a 为师）
    const byName = new Map();
    for (const x of g.generals) if (!x.dead && !byName.has(x.name)) byName.set(x.name, x);
    for (const [a, b, kind, aff] of d.bonds) {
      const x = byName.get(a), y = byName.get(b);
      if (!x || !y || x === y) continue;
      const o = { k: kind, a: aff, s: g.monthIndex };
      if (kind === 'mentor') o.m = x.id;
      st2(g).bonds[pkey(x.id, y.id)] = o;
    }
    // 城级的起点（首次达到的声望只奖励以后的升级）
    for (const c of g.cities) c.eng.maxLv = cityLevel(c);
    bondVer++;
  }
  const st2 = g => g.engage;
  function ensure(g) {
    // 读档 / 旧存档补齐字段（向前兼容；不改变已有的值）
    const s = g.engage;
    const keys = { rel: {}, war: {}, pacts: [], bonds: {}, cards: [], missions: [], mOffers: [], caravans: [], exped: [], counters: {}, ach: [], achAt: {}, visited: [], visitedAt: {},
      clues: [], vlog: {}, log: [], gflags: {}, disc: {}, hist: {}, victory: {} };
    for (const k of Object.keys(keys)) if (s[k] === undefined || s[k] === null) s[k] = keys[k];
    if (!(s.seq > 0)) s.seq = 1;
    if (!s.xp) s.xp = { m: g.monthIndex, u: {} };
    for (const c of g.cities) { if (typeof c.order !== 'number') c.order = orderStart(c, g); if (!c.eng) c.eng = newCityEng(); if (c.eng.maxLv === undefined) c.eng.maxLv = cityLevel(c); }
    for (const f of g.factions) { if (typeof f.prestige !== 'number') f.prestige = 0; if (!f.eng) f.eng = newFacEng(); }
  }

  // ================================================================ 邦交（§3.3）==
  function rel(a, b) { if (a < 0 || b < 0 || a === b) return 0; const g = G(); const v = g.engage && g.engage.rel[pkey(a, b)]; return v | 0; }
  function setRel(a, b, v) {
    if (a < 0 || b < 0 || a === b) return;
    v = Math.round(clamp(v, -100, 100));
    const R = G().engage.rel, k = pkey(a, b);
    if (v === 0) delete R[k]; else R[k] = v;
  }
  function addRel(a, b, d) { if (!sub('diplomacy') || !d) return; setRel(a, b, rel(a, b) + d); }
  function atWar(a, b) { if (a < 0 || b < 0) return false; const w = st().war[pkey(a, b)]; return w !== undefined && G().monthIndex - w < CFG.rel.warMonths; }
  // 盟约的索引（'a-b' → [约]）：盟约表变化时重建（pacts 数组长度 / 版本号）
  let pactIdx = null, pactIdxKey = '';
  function pactIndex() {
    const s = st(), mi = G().monthIndex;
    const key = s.pacts.length + '|' + mi + '|' + pactVer;
    if (pactIdx && pactIdxKey === key) return pactIdx;
    pactIdx = new Map(); pactIdxKey = key;
    for (const p of s.pacts) { if (p.until <= mi) continue; const k = pkey(p.a, p.b); let l = pactIdx.get(k); if (!l) pactIdx.set(k, l = []); l.push(p); }
    return pactIdx;
  }
  let pactVer = 0;
  function activePacts() { const mi = G().monthIndex; return st().pacts.filter(p => p.until > mi); }
  function pactOf(a, b, kind) { const l = pactIndex().get(pkey(a, b)); if (!l) return null; const mi = G().monthIndex; for (const p of l) if (p.until > mi && (!kind || p.kind === kind)) return p; return null; }
  const NONAGGRO = new Set(['marriage', 'tribute', 'truce']);
  function nonAggro(a, b) { const l = pactIndex().get(pkey(a, b)); if (!l) return false; const mi = G().monthIndex; for (const p of l) if (p.until > mi && NONAGGRO.has(p.kind)) return true; return false; }
  function pactsOf(f) { return activePacts().filter(p => p.a === f || p.b === f); }
  function addPact(a, b, kind, months, extra) {
    const g = G();
    const p = Object.assign({ a, b, kind, since: g.monthIndex, until: g.monthIndex + months }, extra || {});
    st().pacts.push(p); pactVer++;
    Engage.stats.pacts[kind] = (Engage.stats.pacts[kind] || 0) + 1;
    return p;
  }
  function vassalOk(a, b) { return G().allied(a, b) || nonAggro(a, b); }
  function noAttack(a, b) { if (!sub('diplomacy') || a < 0 || b < 0 || a === b) return false; return nonAggro(a, b) || rel(a, b) >= CFG.rel.friendly; }
  let MEMO = null;   // monthEnd 期间的缓存（势力兵力、邻国、在野隐士的城 …）；易主 / 归附 / 投奔时清空
  function memoF(f, k, fn) { if (!MEMO) return fn(); const key = f + ':' + k; if (MEMO.f.has(key)) return MEMO.f.get(key); const v = fn(); MEMO.f.set(key, v); return v; }
  function memoHidden() {
    if (MEMO && MEMO.hid) return MEMO.hid;
    const set = new Set(); for (const x of G().generals) if (x.hidden && SG.isFree(x)) set.add(x.city);
    if (MEMO) MEMO.hid = set;
    return set;
  }
  // 月末之内：一次扫描得到所有势力的城与邻国（与 g.citiesOf / neighbors0 同序）
  const EMPTY = Object.freeze([]);
  function memoCities() {
    if (MEMO.co) return MEMO.co;
    const g = G(), co = new Map(), nb = new Map();
    for (const c of g.cities) {
      const o = c.owner; if (o < 0) continue;
      let a = co.get(o); if (!a) { co.set(o, a = []); nb.set(o, new Set()); }
      a.push(c);
      const s = nb.get(o);
      for (const j of c.links) { const o2 = g.cities[j].owner; if (o2 >= 0 && o2 !== o) s.add(o2); }
    }
    MEMO.co = co; MEMO.nbAll = new Map();
    for (const [o, set] of nb) MEMO.nbAll.set(o, Object.freeze(Array.from(set).sort((x, y) => x - y)));
    return co;
  }
  const citiesM = f => (MEMO ? memoCities().get(f) || EMPTY : G().citiesOf(f));
  const countM = f => (MEMO ? (memoCities().get(f) || EMPTY).length : G().cityCount(f));
  function neighbors(f) {
    if (MEMO) { if (!MEMO.nbAll) memoCities(); return MEMO.nbAll.get(f) || EMPTY; }
    return neighbors0(f);
  }
  function neighbors0(f) {
    const g = G(), s = new Set();
    for (const c of g.cities) { if (c.owner !== f) continue; for (const j of c.links) { const o = g.cities[j].owner; if (o >= 0 && o !== f) s.add(o); } }
    return Array.from(s).sort((a, b) => a - b);
  }
  function power(f) {
    if (MEMO) { if (!MEMO.pw) { MEMO.pw = new Map(); for (const x of G().generals) if (x.faction >= 0 && !x.dead) MEMO.pw.set(x.faction, (MEMO.pw.get(x.faction) || 0) + x.troops); } return MEMO.pw.get(f) || 0; }
    let s = 0; for (const x of G().generals) if (x.faction === f && !x.dead) s += x.troops; return s;
  }
  function facInc(f) { let s = 0; for (const c of G().cities) if (c.owner === f) s += inc(c); return s; }
  function capitalOf(f) { const g = G(); if (f < 0 || !g.factions[f] || !g.factions[f].alive) return null; const r = g.ruler(f); if (r && !r.dead && g.cities[r.city] && g.cities[r.city].owner === f) return g.cities[r.city]; return g.citiesOf(f)[0] || null; }
  function hopsBetween(fromId, toId, max, pass) {
    if (fromId === toId) return 0;
    const g = G(), seen = new Set([fromId]); let q = [fromId];
    for (let h = 1; h <= max && q.length; h++) {
      const nx = [];
      for (const id of q) for (const j of g.cities[id].links) { if (seen.has(j)) continue; seen.add(j); if (j === toId) return h; if (!pass || pass(g.cities[j])) nx.push(j); }
      q = nx;
    }
    return -1;
  }
  function facDistance(a, b, max) {
    const g = G();
    const ca = capitalOf(a); if (!ca) return 99;
    const seen = new Set([ca.id]); let q = [ca.id];
    for (let h = 1; h <= max && q.length; h++) {
      const nx = [];
      for (const id of q) for (const j of g.cities[id].links) { if (seen.has(j)) continue; seen.add(j); if (g.cities[j].owner === b) return h; nx.push(j); }
      q = nx;
    }
    return 99;
  }
  function threatened(f, c) {
    const g = G(); let t = 0;
    for (const j of c.links) { const n = g.cities[j]; if (n.owner >= 0 && n.owner !== f && !g.allied(n.owner, f) && !noAttack(n.owner, f)) t += troopsAt(n); }
    return t > troopsAt(c) * 1.1;
  }

  // ================================================================ 声望与人望（§3.2）==
  function addPrestige(f, v, why) {
    if (!sub('prestige') || f < 0 || !v) return;
    const F = G().factions[f]; if (!F) return;
    F.prestige = Math.max(0, Math.round((F.prestige | 0) + v));
  }
  const prestigeOf = f => (f >= 0 && G().factions[f] ? G().factions[f].prestige | 0 : 0);
  function prestigeName(f) { const p = prestigeOf(f); for (const [t, n] of CFG.prestigeNames) if (p >= t) return n; return '无闻'; }
  const prestigeK = f => Math.min(CFG.giftPrestigeMax, 1 + prestigeOf(f) / CFG.giftPrestigeDiv);
  function addFameEv(f, v) { if (f < 0 || !v) return; const e = G().factions[f].eng; if (e) e.fameEv = (e.fameEv || 0) + v; }
  function addVirtueEv(f, v) { if (f < 0 || !v) return; const e = G().factions[f].eng; if (e) e.virtueEv = (e.virtueEv || 0) + v; }

  // ================================================================ 计数与记录 ==
  function cnt(f, k, v) { if (f < 0) return; const c = st().counters; const key = f + ':' + k; c[key] = (c[key] || 0) + (v === undefined ? 1 : v); }
  const getCnt = (f, k) => (live() ? st().counters[f + ':' + k] || 0 : 0);
  function logEvt(kind, ref, v, f) {
    const g = G(), s = st();
    s.log.push({ m: g.monthIndex, kind, ref: ref === undefined ? null : ref, v: v === undefined ? null : v, f: f === undefined ? g.player : f });
    if (s.log.length > CFG.logMax) s.log.splice(0, s.log.length - CFG.logMax);
  }
  const isPlayer = f => f >= 0 && f === G().player;
  let NEWS = [];   // 本月给玩家的新闻（monthEnd 期间收集，写入 ctx.news）
  const news = s => { NEWS.push(s); };

  // ================================================================ 内政经验（§4）==
  function routineExp(gen, amt) {
    if (!live() || !gen || gen.dead || !(amt > 0) || !SG.Progress) return 0;
    const s = st(), g = G();
    if (!s.xp || s.xp.m === undefined) s.xp = { m: g.monthIndex, u: {} };
    const cap = (SG.Mods && SG.Mods.CAPS && SG.Mods.CAPS.routineCap) || CFG.exp.routineCap;
    const add0 = SG.Mods ? SG.Mods.exp(gen, amt) : amt;          // 装备经验加成在上限之前乘（INTEGRATION §3.4）
    const used = s.xp.u[gen.id] || 0;
    const add = Math.min(add0, cap - used);
    if (add <= 0) return 0;
    s.xp.u[gen.id] = used + add;
    gain(gen, add, 'domestic');
    return add;
  }
  function storyExp(gen, amt) { if (!live() || !gen || gen.dead || !(amt > 0) || !SG.Progress) return 0; return gain(gen, Math.round(amt), 'story'); }
  // Progress.gain 的快路径：不升级的那些经验只加数（与 gain 的效果相同：exp、dexp；升级时才走 gain 的习得与记录）。
  // 世界剧本每月几百次太守政绩，全走 gain（每次 ensure + 空格的候选）要 2–3 ms
  function gain(gen, amt, why) {
    const P = SG.Progress;
    if (!P.enabled) return 0;
    let add = amt;
    if (SG.Mods && P.EXP_MUL_WHY && P.EXP_MUL_WHY[why]) add = SG.Mods.exp(gen, add);
    add = Math.round(add);
    if (!(add > 0)) return 0;
    if (!(gen.exp >= 0) || !(gen.level >= 1) || !Array.isArray(gen.specials) || !Array.isArray(gen.tactics)) { const r = P.gain(gen, amt, why); return r ? r.exp : 0; }
    const max = P.EXP[P.MAX_LEVEL], e0 = gen.exp, ne = Math.min(e0 + add, max);
    if (P.levelFor(ne) !== gen.level) { const r = P.gain(gen, amt, why); return r ? r.exp : 0; }
    gen.exp = ne;
    if (ne > e0) gen.dexp = (gen.dexp || 0) + (ne - e0);
    return add;
  }
  function vetLevel(gen) { return SG.Progress ? SG.Progress.vetLevel(gen) : (gen.level | 0) || 1; }

  // ================================================================ 城：增益、民心、营建效果 ==
  function buffSum(c, mod) { const mi = G().monthIndex; let v = 0; if (c.eng) for (const b of c.eng.buffs) if (b.mod === mod && b.until > mi) v += b.v; return v; }
  function buffMul(c, mod) { const mi = G().monthIndex; let v = 1; if (c.eng) for (const b of c.eng.buffs) if (b.mod === mod && b.until > mi) v *= b.v; return v; }
  const PROJ = () => { const d = D(); if (!d) return {}; if (!d._proj) Object.defineProperty(d, '_proj', { value: Object.fromEntries(d.projects.map(p => [p.id, p])), enumerable: false }); return d._proj; };
  const EV = () => { const d = D(); if (!d) return {}; if (!d._ev) Object.defineProperty(d, '_ev', { value: Object.fromEntries(d.events.map(e => [e.id, e])), enumerable: false }); return d._ev; };
  const hasProject = (c, id) => !!(c && c.eng && c.eng.done.indexOf(id) >= 0);
  const facHas = (f, id) => G().cities.some(c => c.owner === f && hasProject(c, id));
  function projFx(c, key) { if (!c || !c.eng || !sub('projects')) return 0; let v = 0; const P = PROJ(); for (const id of c.eng.done) { const p = P[id]; if (p && typeof p.fx[key] === 'number') v += p.fx[key]; } return v; }
  function nextHarvestIdx() { const g = G(), H = B().HarvestMonth; return (g.month <= H ? g.year : g.year + 1) * 12 + H + 1; }
  function orderTarget(c) {
    if (c.owner < 0) return CFG.order.targetEmpty;
    const O = CFG.order;
    let t = O.base + virtueOf(c.owner) / O.virtueDiv + buffSum(c, 'orderTarget') + projFx(c, 'orderTarget');
    const a = adminOf(c);
    if (a && a.pol >= O.govPol) t += O.govAdd;
    return clamp(t, 0, O.targetHi);
  }
  function cityLevel(c) { const E = SG.Econ; return E && E.cityLevel && SG.Balance.CityLevel ? E.cityLevel(c) : 0; }
  const RIVER = () => { const d = D(); if (!d._river) Object.defineProperty(d, '_river', { value: new Set(d.river), enumerable: false }); return d._river; };
  let COAST = null, COASTg = null;
  function isCoast(c) {
    const g = G();
    if (COASTg !== g || !COAST) {
      COASTg = g; COAST = new Set();
      for (const l of (SG.ScenarioData && SG.ScenarioData.SeaLinks) || []) for (const k of l.split('-')) COAST.add(k);
      for (const k of ['beihai', 'guangling', 'wu', 'kuaiji', 'nanpi', 'beiping']) COAST.add(k);
    }
    return COAST.has(c.key);
  }
  const isRiver = c => RIVER().has(c.key);
  function isFrontier(c) { const g = G(); return c.links.some(j => { const o = g.cities[j].owner; return o >= 0 && o !== c.owner; }); }
  function shopTier(c) { const E = SG.Econ; const t = E && E.shopTier && SG.Balance.CityLevel ? E.shopTier(c) : 1; return Math.min(4, t + (hasProject(c, 'market') && sub('projects') ? 1 : 0)); }

  // ================================================================ 羁绊（§3.4、§11）==
  let bondVer = 0, bondIdx = null, bondIdxVer = -1, bondIdxG = null;
  function bondRec(a, b) { if (!a || !b || a.id === b.id) return null; return st().bonds[pkey(a.id, b.id)] || null; }
  function bondKind(a, b) { const o = bondRec(a, b); return o ? o.k || null : null; }
  function bondsIndex() {
    const g = G();
    if (bondIdx && bondIdxVer === bondVer && bondIdxG === g) return bondIdx;
    bondIdx = new Map(); bondIdxVer = bondVer; bondIdxG = g;
    const B2 = st().bonds;
    for (const k in B2) {
      const o = B2[k]; if (!o.k) continue;
      const i = k.indexOf('-'), a = +k.slice(0, i), b = +k.slice(i + 1);
      for (const [x, y] of [[a, b], [b, a]]) { let l = bondIdx.get(x); if (!l) bondIdx.set(x, l = []); l.push({ other: y, k: o.k, a: o.a, m: o.m }); }
    }
    return bondIdx;
  }
  function bondsOf(gen) {
    if (!live() || !gen) return [];
    const S = st().bonds;
    return (bondsIndex().get(gen.id) || []).map(r => { const o = S[pkey(gen.id, r.other)] || {}; return { gen: G().generals[r.other], kind: r.k, aff: o.a === undefined ? r.a : o.a, mentor: o.m === undefined ? r.m : o.m, since: o.s }; });
  }
  // 全部记录（含尚未成形的亲密度；人物关系图用）
  function affinitiesOf(gen) {
    if (!live() || !gen) return [];
    const g = G(), S = st().bonds, out = [];
    for (const k in S) { const i = k.indexOf('-'), a = +k.slice(0, i), b = +k.slice(i + 1); if (a !== gen.id && b !== gen.id) continue; const y = g.generals[a === gen.id ? b : a]; if (y && !y.dead) out.push({ gen: y, kind: S[k].k || null, aff: S[k].a, mentor: S[k].m, since: S[k].s }); }
    return out.sort((p, q) => (q.kind ? 1 : 0) - (p.kind ? 1 : 0) || Math.abs(q.aff) - Math.abs(p.aff) || p.gen.id - q.gen.id);
  }
  function setKind(a, b, kind) {
    const S = st().bonds, k = pkey(a.id, b.id);
    let o = S[k];
    if (!o) o = S[k] = { a: 0 };
    o.k = kind; o.s = G().monthIndex;
    if (kind === 'mentor') o.m = a.id; else delete o.m;
    bondVer++;
    return o;
  }
  function addAff(a, b, d) {
    if (!sub('bonds') || !a || !b || a.id === b.id || !d) return;
    const S = st().bonds, k = pkey(a.id, b.id);
    let o = S[k];
    if (!o) { if (Math.abs(d) < 1) return; o = S[k] = { a: 0 }; }
    o.a = clamp(Math.round((o.a + d) * 2) / 2, -100, 100);
    const Bc = CFG.bonds, k0 = o.k;
    if (!o.k && o.a >= Bc.friendAt) { o.k = 'friend'; o.s = G().monthIndex; if (isPlayer(a.faction) && a.faction === b.faction) { logEvt('friend', [a.id, b.id]); news(`${a.name}与${b.name}结为知己。`); } }
    if (!o.k && o.a <= Bc.feudAt) { o.k = 'feud'; o.s = G().monthIndex; }
    if (o.k === 'friend' && o.a < Bc.friendDrop) o.k = null;
    if (o.k === 'feud' && o.a > Bc.feudDrop) o.k = null;
    if (!o.k) { delete o.k; delete o.s; }
    if ((o.k || null) !== (k0 || null)) bondVer++;
  }
  function swornGroup(x) { return bondsOf(x).filter(r => r.kind === 'sworn' && r.gen && !r.gen.dead).map(r => r.gen); }
  // 战术战斗（battle-math.js 的第 3 组）：同一方的羁绊（结义 +4 / +4、知己 +2 攻、夫妻 +3 攻、宿敌同军 +3 攻、不和 −4 / −4）+ 义愤（+6 攻），合计 ±8 %
  function battleBonus(u, model) {
    const out = { atk: 0, def: 0 };
    if (!sub('bonds') || !u || !u.gen || !model || !model.units) return out;
    const idx = bondsIndex().get(u.gen.id);
    if (!idx || !idx.length) return out;
    const A = CFG.bonds.atk, Df = CFG.bonds.def;
    let yifen = false;
    for (const o of model.units) {
      if (o === u || o.side !== u.side || !o.gen) continue;
      const r = idx.find(q => q.other === o.gen.id);
      if (!r) continue;
      if (!o.alive) { if (r.k === 'sworn') yifen = true; continue; }
      if (A[r.k]) out.atk += A[r.k];
      if (Df[r.k]) out.def += Df[r.k];
    }
    if (yifen) out.atk += CFG.bonds.yifen;
    const cap = CFG.bonds.cap;
    out.atk = clamp(out.atk, -cap, cap); out.def = clamp(out.def, -cap, cap);
    return out;
  }
  function bondShield(x) {
    if (!sub('bonds') || !x || x.faction < 0) return 0;
    const idx = bondsIndex().get(x.id); if (!idx) return 0;
    let s = 0; const S = CFG.bonds.shield, g = G();
    for (const r of idx) { const y = g.generals[r.other]; if (S[r.k] && y && !y.dead && y.faction === x.faction) s = Math.max(s, S[r.k]); }
    return s;
  }
  function bondPersuadeRes(x) {
    if (!sub('bonds') || !x || x.faction < 0) return 0;
    const idx = bondsIndex().get(x.id); if (!idx) return 0;
    let r0 = 0; const R = CFG.bonds.persuadeRes, g = G();
    for (const r of idx) { const y = g.generals[r.other]; if (R[r.k] && y && !y.dead && y.faction === x.faction) r0 = Math.max(r0, R[r.k]); }
    if (!r0) return 0;
    // 与官爵的拉拢抗性合成乘积：(1 − t)(1 − b) = 1 − (t + b(1 − t))（提供者相加，这里只交出 b(1 − t)）
    const T = SG.Titles, t = T && T.enabled !== false && typeof T.persuadeRes === 'function' ? T.persuadeRes(x) || 0 : 0;
    return r0 * (1 - Math.min(1, t));
  }
  const isRival = (a, b) => live() && bondKind(a, b) === 'rival';
  const rivalDmg = (a, b) => (isRival(a, b) && a.faction !== b.faction ? CFG.bonds.rivalDmg : 0);

  // ================================================================ 事件引擎（§6）==
  // 效果的数量
  function amount(o, c, k, cur) {
    if (o.add !== undefined) return o.add;
    if (o.pct !== undefined) return cur * o.pct;
    if (o.inc !== undefined) return inc(c) * o.inc;
    if (o.harv !== undefined) return harv(c) * o.harv;
    if (o.range) return o.range[0] + Math.floor(k.nextDouble() * (o.range[1] - o.range[0] + 1));
    return 0;
  }
  // 金的等价（统计用，ENGAGEMENT §6.7）：粮 0.12、开发点 2.6、兵 0.2、民心 1 点 = 本城收入 × 0.3
  function valueOf(key, d, c) {
    switch (key) {
      case 'gold': return d; case 'food': return d * 0.12;
      case 'land': case 'industry': case 'town': return d * 2.6;
      case 'troops': case 'gtroops': return d * 0.2;
      case 'population': return d / 10 * 0.04;
      case 'order': return d * inc(c) * 0.3;
      default: return 0;
    }
  }
  function spendGold(ctx, amt) {
    // 选项的花费：电脑经 SG.AIBudget（kind 'event'，记账；canSpend 已在选择时检查），玩家直接扣
    const c = ctx.c; amt = Math.round(amt);
    if (amt <= 0) return;
    if (!isPlayer(ctx.f) && SG.AIBudget && ctx.costing) SG.AIBudget.spend(c, Math.min(amt, c.gold), 'event');
    else c.gold = Math.max(0, c.gold - amt);
  }
  function applyEffects(list, ctx) {
    const g = G(), c = ctx.c;
    let val = 0;
    for (const o of list || []) {
      const k = o.k;
      const who = o.who === 'a' ? ctx.a : o.who === 'b' ? ctx.b : o.who === 'gov' ? ctx.gov : o.who === 'subject' ? ctx.subject : o.who === 'winner' ? ctx.winner || ctx.a : o.who === 'loser' ? ctx.loser || ctx.b : ctx.actor;
      if (k === 'gold' || k === 'food' || k === 'land' || k === 'industry' || k === 'town' || k === 'population') {
        let d = amount(o, c, ctx.k, c[k]);
        if (d < 0 && k === 'food' && ctx.disaster && hasProject(c, 'granary')) d *= 0.5;
        if (d < 0 && ctx.costing && ctx.costK) d *= ctx.costK;
        const before = c[k];
        if (k === 'gold' && d < 0 && ctx.costing) { spendGold(ctx, -d); val += valueOf('gold', c.gold - before, c); continue; }
        const cap = k === 'land' || k === 'industry' || k === 'town' ? B().StatMax : k === 'population' ? 2000000 : Infinity;
        c[k] = Math.round(clamp(c[k] + d, 0, cap));
        val += valueOf(k, c[k] - before, c);
      } else if (k === 'order') {
        if (!sub('order')) continue;
        const d = amount(o, c, ctx.k, 0);
        const cs = o.all ? g.citiesOf(ctx.f) : [c];
        for (const cc of cs) { cc.order = clamp(Math.round(cc.order + d), 0, 100); val += valueOf('order', d, cc); }
      } else if (k === 'troops') {
        for (const x of gensIn(c)) { const d = Math.round(x.troops * (o.pct || 0)); x.troops = Math.max(0, x.troops + d); val += valueOf('troops', d, c); }
      } else if (k === 'training') {
        const cs = o.where === 'capital' ? [capitalOf(ctx.f)].filter(Boolean) : [c];
        for (const cc of cs) for (const x of gensIn(cc)) if (x.troops > 0) x.training = clamp(x.training + (o.add || 0), 0, 100);
      } else if (k === 'gtroops') {
        if (!who) continue;
        if (o.pct !== undefined) { const d = Math.round(who.troops * o.pct); who.troops = Math.max(0, who.troops + d); val += valueOf('troops', d, c); continue; }
        const room = Math.max(0, SG.maxTroops(who) - who.troops);
        const n = Math.min(room, Math.round(amount(o, c, ctx.k, 0)));
        if (n > 0) { const tr = o.training || 30; who.training = Math.round((who.training * who.troops + tr * n) / (who.troops + n)); who.troops += n; val += valueOf('troops', n, c); }
      } else if (k === 'prestige') { addPrestige(ctx.f, amount(o, c, ctx.k, 0)); }
      else if (k === 'fame') { addFameEv(ctx.f, o.add || 0); }
      else if (k === 'virtue') { addVirtueEv(ctx.f, o.add || 0); }
      else if (k === 'exp') { if (who) storyExp(who, (o.add || 0) + (o.intelBonus && who.intel >= 70 ? o.intelBonus : 0)); }
      else if (k === 'expLater') { if (who && who.away) who.away.exp = (who.away.exp || 0) + (o.add || 0); else if (who) storyExp(who, o.add || 0); }
      else if (k === 'loyalty') { if (who && !isRuler(who) && !who.dead) who.loyalty = clamp(who.loyalty + (o.add || 0), 0, 100); }
      else if (k === 'officers.loyalty') { for (const x of gensIn(c)) if (!isRuler(x)) x.loyalty = clamp(x.loyalty + (o.add || 0), 0, 100); }
      else if (k === 'loyalty3') {
        const xs = gensIn(c).filter(x => !isRuler(x)).sort((p, q) => p.loyalty - q.loyalty || p.id - q.id).slice(0, 3);
        const v = ctx.touhu !== undefined ? ctx.touhu : Math.round(amount(o, c, ctx.k, 0));
        for (const x of xs) x.loyalty = clamp(x.loyalty + v, 0, 100);
      } else if (k === 'bond3') { const xs = gensIn(c).slice(0, 4); for (let i = 0; i < xs.length; i++) for (let j = i + 1; j < xs.length; j++) addAff(xs[i], xs[j], o.add); }
      else if (k === 'buff') {
        const until = o.untilHarvest ? nextHarvestIdx() : g.monthIndex + (o.months || 1);
        c.eng.buffs.push({ mod: o.mod, v: o.v, until });
      } else if (k === 'flag') {
        if (o.id === 'vanguard' || o.id === 'tourneyPick') { if (who) { const gf = st().gflags; (gf[who.id] = gf[who.id] || {})[o.id] = g.monthIndex + (o.months || 12); } }
        else if (o.months > 0) c.eng.flags[o.id] = g.monthIndex + o.months; else delete c.eng.flags[o.id];
      }
      else if (k === 'rel') {
        const targets = o.who === 'neighbors' ? neighbors(ctx.f) : o.who === 'region' ? g.factions.filter(x => x.alive && x.region === g.factions[ctx.f].region && x.id !== ctx.f).map(x => x.id)
          : o.who === 'far' ? (ctx.far >= 0 ? [ctx.far] : []) : o.who === 'subject' ? (ctx.subjectFac >= 0 ? [ctx.subjectFac] : []) : (ctx.origin >= 0 ? [ctx.origin] : []);
        for (const t of targets) addRel(ctx.f, t, o.add);
      } else if (k === 'bond') {
        if (ctx.a && ctx.b) {
          addAff(ctx.a, ctx.b, o.add);
          if (o.rivalIfClose && Math.abs(ctx.a.war - ctx.b.war) <= 6 && ctx.a.war >= 85 && ctx.b.war >= 85 && ctx.k.nextDouble() < o.rivalIfClose) { const bo = bondRec(ctx.a, ctx.b); if (bo && !bo.k) { setKind(ctx.a, ctx.b, 'rival'); if (isPlayer(ctx.f)) news(`${ctx.a.name}与${ctx.b.name}自此互为宿敌。`); } }
        }
      } else if (k === 'bondKind') {
        if (ctx.a && ctx.b) {
          setKind(ctx.a, ctx.b, o.kind);
          if (o.kind === 'sworn') { cnt(ctx.f, 'sworn'); logEvt('sworn', [ctx.a.id, ctx.b.id], null, ctx.f); }
          if (o.kind === 'mentor') { cnt(ctx.f, 'mentors'); logEvt('mentor', [ctx.a.id, ctx.b.id], null, ctx.f); }
        }
      } else if (k === 'reveal') {
        const near = o.who === 'near2' ? new Set([c.id, ...c.links, ...c.links.flatMap(i => g.cities[i].links)]) : new Set([c.id]);
        const pool = g.generals.filter(x => x.hidden && SG.isFree(x) && near.has(x.city) && (o.who !== 'bandit' || (x.war >= 60 && x.intel < 60)));
        if (pool.length) { const h = pick(ctx.k, pool); h.hidden = false; if (MEMO) MEMO.hid = null; cnt(ctx.f, 'revealed'); if (isPlayer(ctx.f)) news(`在野的${h.name}之名传入${g.cities[h.city].name}。`); }
        else if (o.orMission && isPlayer(ctx.f)) offerMissions(ctx.f, 1, true);
      } else if (k === 'item') {
        const w = o.w || { common: 70, uncommon: 30 };
        const rar = rollRarity(w, ctx.k);
        const cult = ctx.far >= 0 ? g.factions[ctx.far].culture : null;
        grantItem(ctx.f, rar, 'event', { culture: cult, k: ctx.k });
      } else if (k === 'away') {
        if (who && !isRuler(who)) { who.away = { kind: 'leave', until: g.monthIndex + 1 + (o.add || 0), home: who.city }; dirtyIdx(); }
      } else if (k === 'defect') {
        const s = ctx.subject; if (!s || s.faction < 0 || s.dead) continue;
        const of = s.faction, oc = g.cities[s.city];
        s.faction = ctx.f; s.city = c.id; s.troops = Math.round(s.troops * (o.troopsK || 0.5)); s.loyalty = clamp(55 + Math.round(virtueOf(ctx.f) / 5), 40, 95); s.moved = true;
        dirtyIdx();
        g.autoGovernor(oc); g.autoGovernor(c);
        if (g.officersIn(oc).length === 0) { oc.owner = -1; dirtyIdx(); if (g.rehomeStrays) g.rehomeStrays(); g.checkFactionDeath(of); }
        cnt(ctx.f, 'hires');
      } else if (k === 'separate') {
        const x = ctx.b || ctx.a; if (!x) continue;
        const dest = g.citiesOf(ctx.f).filter(cc => cc.id !== x.city).sort((p, q) => hopsBetween(x.city, p.id, 9) - hopsBetween(x.city, q.id, 9) || p.id - q.id)[0];
        if (dest && !isRuler(x)) { const from = g.cities[x.city]; x.city = dest.id; dirtyIdx(); g.autoGovernor(from); g.autoGovernor(dest); }
      } else if (k === 'plan') { applyPlan(ctx); cnt(ctx.f, 'plans'); }
      else if (k === 'projectDiscount') { const dd = st().disc; (dd[ctx.f] = dd[ctx.f] || {})[o.project] = o.v; }
      else if (k === 'roll3') { const r = ctx.k.nextDouble(); let i = 0, acc = 0; for (; i < o.p.length; i++) { acc += o.p[i]; if (r < acc) break; } val += applyEffects(o.outcomes[Math.min(i, o.outcomes.length - 1)], ctx); }
    }
    return val;
  }
  function applyPlan(ctx) {
    const d = D(), plan = d.proposals.find(p => p.id === ctx.plan) || d.proposals[0];
    applyEffects(plan.effect.map(e => (e.k === 'training' ? Object.assign({}, e, { where: 'capital' }) : e)), Object.assign({}, ctx, { c: capitalOf(ctx.f) || ctx.c }));
  }
  function rollRarity(w, k) { const r = k.nextDouble() * 100; const c = w.common || 0, u = w.uncommon || 0; return r < c ? 'common' : r < c + u ? 'uncommon' : 'rare'; }
  // ITEMS 的物品池（§12.3；INTEGRATION §7.2：珍品 = 没有任何势力拥有的商店珍品；凡品 / 良品 = 本文化的商店货）
  function grantItem(f, rarity, why, o) {
    o = o || {};
    cnt(f, 'items'); cnt(f, 'item:' + rarity); cnt(f, 'itemFrom:' + why);
    const I = SG.Items;
    if (!I || I.enabled === false || !G().items || typeof I.pool !== 'function') return null;
    const g = G();
    const cult = o.culture || (g.factions[f] && g.factions[f].culture) || 'han';
    let list = I.pool({ culture: rarity === 'rare' ? (o.culture || null) : cult, types: o.types || null, rarity });
    if (rarity === 'rare' && !list.length && o.culture) list = I.pool({ culture: null, types: o.types || null, rarity: 'rare' });
    let gold = 0;
    if (!list.length && rarity === 'rare') { list = I.pool({ culture: cult, types: o.types || null, rarity: 'uncommon' }); gold = o.fallbackGold || 0; }
    if (!list.length && o.types) list = I.pool({ culture: cult, rarity: rarity === 'rare' ? 'uncommon' : rarity });
    if (!list.length) return null;
    const k = o.k || rng('grant', f, why, getCnt(f, 'items'));
    const id = list[Math.floor(k.nextDouble() * list.length)];
    const uid = I.award(o.gen || f, id, { why: 'engage-' + why });
    if (gold > 0) { const cap = capitalOf(f); if (cap) cap.gold += gold; }
    if (uid != null && isPlayer(f)) { let nm = id; try { nm = I.get(id).name; } catch (e) { /* 原编号 */ } news(`得到${rarity === 'rare' ? '珍品' : rarity === 'uncommon' ? '良品' : ''}「${nm}」。`); }
    return uid != null ? id : null;
  }

  // 条件（mo = 卡出现的那个月，即下一个月；事件在月末抽、下个月初呈现）
  // 条件（mo = 卡出现的那个月，即下一个月；事件在月末抽、下个月初呈现）：势力的条件（每次抽签每个事件一次）+ 城的条件
  // 每个事件的静态信息（有无势力条件 / 城条件、权重 buff 的键），按事件对象缓存
  const EVX = new Map();
  const FAC_KEYS = ['scenario', 'minVirtue', 'minFame', 'minCities', 'minPrestige', 'date', 'minAvgOrder', 'farFaction', 'deserterNear'];
  const CITY_KEYS = ['families', 'notFamilies', 'minLat', 'river', 'riverOrCoast', 'minLand', 'minTown', 'minPop', 'minOrder', 'maxOrder', 'minTier', 'hiddenHere', 'govPol', 'neighborTrouble'];
  function evx(ev) {
    let x = EVX.get(ev);
    if (!x) { const w = ev.when || {}; x = { fac: FAC_KEYS.some(k => w[k] !== undefined), city: CITY_KEYS.some(k => w[k] !== undefined), wk: 'eventW:' + ev.id }; EVX.set(ev, x); }
    return x;
  }
  // 抽签时一个势力的事实（一次抽签之内不变）
  function facFacts(f, cities) { return { virtue: virtueOf(f), fame: fameOf(f), n: cities ? cities.length : countM(f), prestige: prestigeOf(f) }; }
  function factionOK(ev, f, ym, ff) {
    const w = ev.when; if (!w) return true;
    const g = G();
    if (w.scenario && w.scenario !== g.scenario) return false;
    if (w.minVirtue && (ff ? ff.virtue : virtueOf(f)) < w.minVirtue) return false;
    if (w.minFame && (ff ? ff.fame : fameOf(f)) < w.minFame) return false;
    if (w.minCities && (ff ? ff.n : countM(f)) < w.minCities) return false;
    if (w.minPrestige && (ff ? ff.prestige : prestigeOf(f)) < w.minPrestige) return false;
    if (w.date && w.date.to) { const p = w.date.to.split('-'); if (ym > (+p[0]) * 12 + (+p[1])) return false; }
    if (w.minAvgOrder && memoF(f, 'avgOrder', () => avg(citiesM(f).map(x => x.order))) < w.minAvgOrder) return false;
    if (w.farFaction && memoF(f, 'far' + w.farFaction, () => farFaction(f, w.farFaction)) < 0) return false;
    if (w.deserterNear && !memoF(f, 'deserter', () => deserterNear(f, null))) return false;
    return true;
  }
  function cityOK(ev, c, mi) {
    const w = ev.when; if (!w) return true;
    const cf = cityFacts(c), fam = cf.fam;
    if (w.families && w.families.indexOf(fam) < 0) return false;
    if (w.notFamilies && w.notFamilies.indexOf(fam) >= 0) return false;
    if (w.minLat && c.lat < w.minLat && fam === 'hua') return false;
    if (w.river && !cf.river) return false;
    if (w.riverOrCoast && !cf.river && !cf.coast) return false;
    if (w.minLand && c.land < w.minLand) return false;
    if (w.minTown && c.town < w.minTown) return false;
    if (w.minPop && c.population < w.minPop) return false;
    if (w.minOrder && c.order < w.minOrder) return false;
    if (w.maxOrder && c.order > w.maxOrder) return false;
    if (w.minTier && cf.tier < w.minTier) return false;
    if (w.hiddenHere && !memoHidden().has(c.id)) return false;
    if (w.govPol) { const a = adminOf(c); if (!a || a.pol < w.govPol) return false; }
    if (w.neighborTrouble) {
      const g = G(); let t = false;
      for (const j of c.links) { const n = g.cities[j].eng; if (n && ((n.trouble !== undefined && mi - n.trouble <= 2) || mi - n.lastDis <= 2)) { t = true; break; } }
      if (!t) return false;
    }
    return true;
  }
  function condOK(ev, c, f, mo, ym) {
    const w = ev.when || {};
    if (w.months && w.months.indexOf(mo) < 0) return false;
    return factionOK(ev, f, ym) && cityOK(ev, c, G().monthIndex);
  }
  // 城的固定事实（一次月末之内不变）：家族、临河、临海、商店等级
  function cityFacts(c) {
    if (MEMO && MEMO.cf.has(c.id)) return MEMO.cf.get(c.id);
    const o = { fam: famOfCity(c), river: isRiver(c), coast: isCoast(c), tier: shopTier(c) };
    if (MEMO) MEMO.cf.set(c.id, o);
    return o;
  }
  // 本月可能的事件（月份、剧本、非天下事件）
  function eventsFor(mo) {
    if (MEMO && MEMO.ev && MEMO.evMo === mo) return MEMO.ev;
    const g = G();
    const list = D().events.filter(ev => !ev.globalP && !(ev.when && ((ev.when.months && ev.when.months.indexOf(mo) < 0) || (ev.when.scenario && ev.when.scenario !== g.scenario))));
    if (MEMO) { MEMO.ev = list; MEMO.evMo = mo; }
    return list;
  }
  // 一次广度搜索得到各势力离本势力都城的跳数（与 facDistance 相同：都城本身不算，max 跳之外 = 99）
  function facDistances(a, max) {
    const g = G(), out = new Map(), ca = capitalOf(a); if (!ca) return out;
    const seen = new Set([ca.id]); let q = [ca.id];
    for (let h = 1; h <= max && q.length; h++) {
      const nx = [];
      for (const id of q) for (const j of g.cities[id].links) { if (seen.has(j)) continue; seen.add(j); const o = g.cities[j].owner; if (!out.has(o)) out.set(o, h); nx.push(j); }
      q = nx;
    }
    return out;
  }
  function farFaction(f, minHops) {
    const g = G(), me = g.factions[f], ns = new Set(neighbors(f));
    const dist = minHops ? (capitalOf(f) ? facDistances(f, minHops) : null) : null;
    const cands = g.factions.filter(x => x.alive && x.id !== f && x.culture !== me.culture && !ns.has(x.id) && (!minHops || (dist ? (dist.has(x.id) ? dist.get(x.id) : 99) : 99) >= minHops));
    return cands.length ? cands[hash32(g.monthIndex + '|' + f) % cands.length].id : -1;
  }
  function weightOf(ev, c, f, ff, projOn) {
    let w = ev.w;
    if (ev.wOrder === 'low') w *= 1.6 - c.order / 100;
    if (ev.wOrder === 'high') w *= c.order / 65;
    if (ev.wPop) w *= clamp(c.population / 400000, 0.3, 3);
    if (ev.wTown) w *= c.town / 600;
    if (ev.wVirtue) w *= 1 + (ff ? ff.virtue : virtueOf(f)) / 100;
    if (c.eng && c.eng.buffs.length) w *= buffMul(c, evx(ev).wk);
    if (ev.disaster && c.eng && c.eng.done.length && (projOn === undefined ? sub('projects') : projOn)) { const P = PROJ(); for (const id of c.eng.done) { const p = P[id]; if (p && p.fx.disasterK && p.fx.disasterK[ev.id]) w *= p.fx.disasterK[ev.id]; } }
    return Math.max(0, w);
  }
  function pickPair(kind, f, k) {
    const g = G(), gens = g.generalsOf(f).filter(x => !absent(x));
    const out = [];
    const Bc = CFG.bonds;
    const swornN = x => swornGroup(x).length + 1;
    const students = x => bondsOf(x).filter(r => r.kind === 'mentor' && r.mentor === x.id).length;
    const hasMentor = x => bondsOf(x).some(r => r.kind === 'mentor' && r.mentor !== x.id);
    for (let i = 0; i < gens.length && out.length < 40; i++) for (let j = i + 1; j < gens.length; j++) {
      let a = gens[i], b = gens[j];
      if (a.city !== b.city && kind !== 'enemies') continue;
      const bo = bondRec(a, b);
      if (kind === 'spar') { if (a.war >= 70 && b.war >= 70 && !isRuler(a) && !isRuler(b) && !((g.factions[f].eng.cd['spar:' + pkey(a.id, b.id)] || -999) + 24 > g.monthIndex)) out.push([a, b]); }
      else if (kind === 'oath') { if (bo && bo.a >= Bc.oathAt && ['sworn', 'kin', 'spouse'].indexOf(bo.k) < 0 && swornN(a) < Bc.maxSworn && swornN(b) < Bc.maxSworn) out.push([a, b]); }
      else if (kind === 'mentor') {
        if (Math.max(b.war, b.intel, b.pol) > Math.max(a.war, a.intel, a.pol)) { const t = a; a = b; b = t; }
        if ((!bo || !bo.k) && Math.max(a.war, a.intel, a.pol) >= Bc.mentorStat && a.level >= Bc.mentorLevel && b.level <= a.level - Bc.mentorGap && students(a) < Bc.mentorMax && !hasMentor(b)) out.push([a, b]);
      } else if (kind === 'enemies') { if (bo && bo.a <= -30) out.push([a, b]); }
      else if (kind === 'quarrel') { if (bo && bo.k === 'rival') out.push([a, b]); }
    }
    return out.length ? out[Math.floor(k.nextDouble() * out.length)] : null;
  }
  function pickGen(need, f, k) {
    const g = G(), me = g.factions[f];
    const gens = g.generalsOf(f).filter(x => !isRuler(x) && !absent(x)).filter(x =>
      (!need.minIntel || x.intel >= need.minIntel) && (!need.minWar || x.war >= need.minWar) && (!need.maxLevel || (x.level | 0) <= need.maxLevel) &&
      (!need.foreign || famOfGen(x) !== FAMILY_OF(me.culture)) && (!need.idleMonths || g.monthIndex - (x.lastBattle === undefined ? st().start : x.lastBattle) >= need.idleMonths));
    return gens.length ? gens[Math.floor(k.nextDouble() * gens.length)] : null;
  }
  function deserterNear(f, k) {
    const g = G(), out = [];
    for (const c of citiesM(f)) for (const j of c.links) {
      const n = g.cities[j];
      if (n.owner < 0 || n.owner === f || rel(f, n.owner) > 0) continue;
      const offs = gensIn(n);
      if (offs.length < 2) continue;
      for (const x of offs) if (!isRuler(x) && x.loyalty <= 45 && Math.max(x.war, x.intel) >= 60) out.push([x, c]);
    }
    if (!k) return out.length > 0;
    return out.length ? out[Math.floor(k.nextDouble() * out.length)] : null;
  }
  function personaFor(f, c, ym, k) {
    const d = D(), fam = famOfCity(c), cult = G().factions[f].culture;
    const famKey = { hua: 'han', roman: 'roman', persia: 'persia', indic: 'indic', steppe: 'steppe', north: 'north', wa: 'wa' }[fam];
    const g = G(), near = new Set();
    for (const cc of g.citiesOf(f)) { near.add(cc.key); for (const j of cc.links) near.add(g.cities[j].key); }
    const list = (d.sages[famKey] || []).filter(p => (!p.where || !p.where.length || p.where.some(k2 => near.has(k2))) && (!p.date || !p.date.to || ym <= (+p.date.to.split('-')[0]) * 12 + (+p.date.to.split('-')[1])));
    const named = list.filter(p => p.where && p.where.length);
    if (named.length && k.nextDouble() < 0.7) return pick(k, named);
    const gen = (d.sages[famKey] || []).filter(p => !p.where || !p.where.length);
    return gen.length ? pick(k, gen) : pick(k, d.sages.any);
  }

  // 抽一个事件（城 + 主角）：→ { ev, c, w } | null
  function drawEvent(f, k, mo, ym) {
    const g = G(), cands = [];
    const cities = citiesM(f);
    if (!cities.length) return null;
    const cap = capitalOf(f);
    const fe = g.factions[f].eng;
    const E = CFG.events;
    const mi = g.monthIndex;
    const ff = facFacts(f, cities), projOn = sub('projects');
    for (const ev of eventsFor(mo)) {
      if (fe.recent[ev.id] !== undefined && mi - fe.recent[ev.id] < E.recentMonths) continue;   // R6
      const w0 = ev.when, x = evx(ev);
      if (ev.scope === 'city') {
        if (w0 && w0.hiddenHere && !memoHidden().size) continue;
        if (x.fac && !factionOK(ev, f, ym, ff)) continue;
        const id = ev.id, cd = ev.cd, dis = ev.disaster;
        for (const c of cities) {
          const last = c.eng.cd[id];
          if (last !== undefined && last + cd > mi) continue;
          if (dis && mi - c.eng.lastDis < E.disasterGap) continue;
          if (x.city && !cityOK(ev, c, mi)) continue;
          const w = weightOf(ev, c, f, ff, projOn); if (w > 0) cands.push({ ev, c, w });
        }
      } else {
        if ((fe.cd[ev.id] === undefined ? -999 : fe.cd[ev.id]) + ev.cd > mi) continue;
        if (!cap || (x.fac && !factionOK(ev, f, ym, ff)) || (x.city && !cityOK(ev, cap, mi))) continue;
        const w = ev.when && ev.when.needPair ? ev.w * E.pairK : ev.w;
        cands.push({ ev, c: cap, w: w * Math.max(1, Math.sqrt(cities.length)) });
      }
    }
    if (!cands.length) return null;
    let tot = 0; for (const x of cands) tot += x.w;
    let r = k.nextDouble() * tot;
    for (const x of cands) { r -= x.w; if (r < 0) return x; }
    return cands[cands.length - 1];
  }
  // 事件的上下文（选择执行者、主角、对象）；不能成立（缺一对武将等）时 null
  function makeCtx(f, pick0, k, mo, ym) {
    const g = G(), ev = pick0.ev, c = pick0.c;
    const ctx = { f, c, k, ev, gov: adminOf(c) || g.ruler(f), ruler: g.ruler(f), disaster: !!ev.disaster, origin: -1, far: -1, subjectFac: -1, mo, ym };
    if (ev.scope === 'pair') { const p = pickPair(ev.when.needPair, f, k); if (!p) return null; ctx.a = p[0]; ctx.b = p[1]; if (ev.when.needPair === 'spar') g.factions[f].eng.cd['spar:' + pkey(p[0].id, p[1].id)] = g.monthIndex; }
    if (ev.scope === 'general') {
      if (ev.id === 'deserter') { const d = deserterNear(f, k); if (!d) return null; ctx.subject = d[0]; ctx.subjectFac = d[0].faction; ctx.c = d[1]; ctx.gov = adminOf(d[1]) || ctx.ruler; }
      else { const s = pickGen(ev.when.needGen || {}, f, k); if (!s) return null; ctx.subject = s; }
    }
    if (ev.id === 'merchant') { const ns = neighbors(f); ctx.origin = ns.length ? ns[Math.floor(k.nextDouble() * ns.length)] : -1; }
    if (ev.id === 'gift') { ctx.far = farFaction(f, (ev.when && ev.when.farFaction) || 0); if (ctx.far < 0) return null; }
    if (ev.id === 'sage') ctx.persona = personaFor(f, c, ym, k);
    if (ev.id === 'omen') { const om = D().omens[famOfCity(c)] || D().omens.any; ctx.omen = pick(k, om); }
    if (ev.id === 'proposal') ctx.plan = pick(k, D().proposals).id;
    if (ev.id === 'study') { const T = CFG.text.place; ctx.place = pick(k, T[famOfFac(f)] || T.any); }
    if (ev.id === 'homesick' && ctx.subject) ctx.home = CFG.text.home[famOfGen(ctx.subject)] || '故乡';
    if (ev.id === 'comet' && ev.variants && ev.variants.any && k.nextDouble() < 0.4) ctx.variant = 'any';
    return ctx;
  }
  function stamp(ctx) {
    const g = G(), ev = ctx.ev, c = ctx.c;
    if (ev.scope === 'city') { c.eng.cd[ev.id] = g.monthIndex; if (ev.disaster) c.eng.lastDis = g.monthIndex; }
    else g.factions[ctx.f].eng.cd[ev.id] = g.monthIndex;
    g.factions[ctx.f].eng.recent[ev.id] = g.monthIndex;
  }
  function agentFor(o, ctx) {
    if (!o.agent) return ctx.ev && ctx.ev.scope === 'general' && ctx.subject ? ctx.subject : ctx.gov;
    if (o.agent === 'gov') return ctx.gov;
    if (o.agent === 'ruler') return ctx.ruler;
    if (o.agent === 'subject') return ctx.subject;
    const gens = gensIn(ctx.c);
    const key = o.agent.split(':')[1];
    if (!gens.length) return ctx.gov;
    const sc = x => (key === 'war' ? x.war * 2 + x.troops / 200 : key === 'intel' ? x.intel : key === 'pol' ? x.pol : Math.max(x.intel, x.pol));
    return gens.reduce((a, b) => (sc(b) > sc(a) ? b : a));
  }
  function rollP(p, ctx, actor) {
    const a = actor || ctx.actor || ctx.gov || ctx.ruler;
    let v = p.base || 0;
    if (a) v += (p.war || 0) * a.war + (p.intel || 0) * a.intel + (p.pol || 0) * a.pol + (p.best || 0) * Math.max(a.intel, a.pol) + (p.troops || 0) * a.troops;
    v += (p.virtue || 0) * virtueOf(ctx.f);
    return clamp(v, 0.05, p.cap || 0.9);
  }
  function healerHere(ctx) { const g = G(); return gensIn(ctx.c).some(x => x.name === '华佗' || x.name === '张机' || x.name === '盖伦') || (ctx.persona && ctx.persona.special === 'doctor'); }
  function optCost(o, ctx) {
    let gold = 0, food = 0;
    const k0 = SG.SeededRandom(1);
    let k = 1;
    if (o.healer && healerHere(ctx)) k *= o.healer.costK || 1;
    if (ctx.ev && ctx.ev.id === 'plague' && hasProject(ctx.c, 'temple')) k *= PROJ().temple.fx.plagueK || 1;
    for (const e of o.cost || []) { if (e.k === 'gold') gold -= amount(e, ctx.c, k0, ctx.c.gold); if (e.k === 'food') food -= amount(e, ctx.c, k0, ctx.c.food); }
    return { gold: Math.round(gold * k), food: Math.round(food), k };
  }
  function tourneyWithin(months) {
    if (!SG.Tourney || SG.Tourney.enabled === false) return false;
    const m = G().month; const until = (13 - m) % 12;
    return until <= months;
  }
  // 能否选这一项：花费付得起（电脑与 ★ 另要保留 R(c)，INTEGRATION §5；kind 'event'）、兵力、离城的规则
  function affordable(o, ctx, reserve) {
    const { gold, food } = optCost(o, ctx);
    if (gold > 0 && gold > ctx.c.gold) return false;
    if (food > 0 && food > ctx.c.food) return false;
    if (reserve) {
      const r = reserveOf(ctx.c);
      if (gold > 0 && ctx.c.gold - gold < r.gold) return false;
      if (food > 0 && ctx.c.food - food < r.food) return false;
    }
    if (o.need && o.need.troops && sum(gensIn(ctx.c), x => x.troops) < o.need.troops) return false;
    if (o.needTourneyWithin && !tourneyWithin(o.needTourneyWithin)) return false;
    if ((o.effects || []).some(e => e.k === 'away') && ctx.subject) {
      const home = G().cities[ctx.subject.city];
      if (gensIn(home).length < 2 || threatened(ctx.f, home)) return false;
    }
    return true;
  }
  function aiScore(o, ctx) {
    let s = o.ai || 0;
    const q = o.aiIf || {}, c = ctx.c, g = G();
    let okIf = Object.keys(q).length > 0;
    if (q.foodOverHarv !== undefined && !(c.food > harv(c) * q.foodOverHarv)) okIf = false;
    if (q.goldOverInc !== undefined && !(c.gold > inc(c) * q.goldOverInc)) okIf = false;
    if (q.goldOver !== undefined && !(c.gold > q.goldOver)) okIf = false;
    if (q.troopsOver !== undefined && !(troopsAt(c) > q.troopsOver)) okIf = false;
    if (q.orderBelow !== undefined && !(c.order < q.orderBelow)) okIf = false;
    if (q.minVirtue !== undefined && !(virtueOf(ctx.f) >= q.minVirtue)) okIf = false;
    if (q.govPol !== undefined && !(ctx.gov && ctx.gov.pol >= q.govPol)) okIf = false;
    if (q.genWar !== undefined && !gensIn(c).some(x => x.war >= q.genWar)) okIf = false;
    if (q.threatened !== undefined && !threatened(ctx.f, c)) okIf = false;
    if (q.notThreatened !== undefined && threatened(ctx.f, c)) okIf = false;
    return okIf ? s * 2 : s;
  }
  // 电脑（与玩家卡上的 ★）：可负担（含保留）的选项中 ai 分最高者；aiIf 满足时 ×2（ENGAGEMENT §6.5）
  function aiChoose(opts, ctx) {
    let best = null, bs = -1;
    for (const o of opts) {
      if (o.playerOnly) continue;
      if (!affordable(o, ctx, true)) continue;
      const s = aiScore(o, ctx);
      if (s > bs) { bs = s; best = o; }
    }
    return best || safeOption(opts, ctx);
  }
  // R4：安全的免费项（不花钱、电脑可选的项里 ai 分最高者）
  function safeOption(opts, ctx) {
    const free = opts.filter(o => !o.playerOnly && !(o.cost || []).length && affordable(o, ctx, false));
    if (!free.length) { const np = opts.filter(o => !o.playerOnly); return np[np.length - 1] || opts[opts.length - 1]; }
    return free.reduce((a, b) => ((b.ai || 0) > (a.ai || 0) ? b : a));
  }
  function applyOption(o, ctx) {
    let val = 0;
    ctx.actor = agentFor(o, ctx);
    const oc = optCost(o, ctx);
    ctx.costing = true; ctx.costK = oc.k;
    val += applyEffects(o.cost, ctx);
    ctx.costing = false; ctx.costK = 1;
    let rolled = null;
    if (o.roll) {
      const p = rollP(o.roll.p, ctx);
      rolled = { p, ok: ctx.k.nextDouble() < p };
      val += applyEffects(rolled.ok ? o.roll.ok : o.roll.fail, ctx);
    }
    val += applyEffects(o.effects, ctx);
    if (o.healer && healerHere(ctx)) val += applyEffects(o.healer.effects, ctx);
    if (o.opensShop && SG.Items && SG.Items.enabled !== false && G().items && typeof SG.Items.openMerchant === 'function') { try { SG.Items.openMerchant(ctx.c, ctx.k); } catch (e) { console.error(e); } }
    const f = ctx.f, ev = ctx.ev;
    if (o.id === 'relief' || o.id === 'doctor' || o.id === 'settle') cnt(f, 'reliefs');
    if (ev.id === 'sage' && o.id === 'host') { cnt(f, 'sages'); sageSpecial(ctx); }
    if (ev.id === 'merchant' && o.id === 'host') cnt(f, 'merchantHosted');
    return { val, rolled };
  }
  // §6.4 高士的特别结果（「延为上宾」时）
  function sageSpecial(ctx) {
    const p = ctx.persona; if (!p || !p.special) return;
    const g = G(), f = ctx.f;
    if (p.special === 'yuedan') {
      const ru = g.ruler(f);
      addFameEv(f, ru && ru.name === '曹操' ? 2 : 1); addPrestige(f, 10);
      const gs = g.generalsOf(f).filter(x => !isRuler(x) && !absent(x)).sort((a, b) => (b.war + b.intel + b.pol) - (a.war + a.intel + a.pol) || a.id - b.id).slice(0, 2);
      for (const x of gs) { storyExp(x, 20); x.loyalty = Math.min(100, x.loyalty + 3); }
      cnt(f, 'event:yuedan');
      if (isPlayer(f)) news(ru && ru.name === '曹操' ? '许劭品题：「清平之奸贼，乱世之英雄。」' : `许劭月旦品题${ru ? ru.name : ''}，名动州郡。`);
    } else if (p.special === 'shuijing') {
      const near = new Set(); for (const c of g.citiesOf(f)) { near.add(c.id); for (const j of c.links) near.add(j); }
      let any = false;
      for (const x of g.generals) if ((x.name === '诸葛亮' || x.name === '庞统') && x.hidden && SG.isFree(x) && near.has(x.city)) { x.hidden = false; any = true; cnt(f, 'revealed'); }
      if (any) { cnt(f, 'event:shuijing'); if (isPlayer(f)) news('司马徽：「此间自有伏龙、凤雏。」'); }
    } else if (p.special === 'zhengxuan') {
      for (const x of gensIn(ctx.c)) if (x.intel >= 60) storyExp(x, 30);
      const dd = st().disc; (dd[f] = dd[f] || {}).academy = 0.3;
    } else if (p.special === 'yuji') {
      ctx.c.eng.flags.plagueImmune = g.monthIndex + 12; ctx.c.order = Math.min(100, ctx.c.order + 10);
    } else if (p.special === 'doctor') {
      if (!(ctx.c.eng.flags.plague > g.monthIndex)) { const x = ctx.actor || ctx.gov; if (x) storyExp(x, 40); addVirtueEv(f, 1); }
    }
    if (p.name === '管宁') addVirtueEv(f, 1);
  }

  // 文案（§6.1 的占位符；engage-screen.js 用同一个函数）
  function fillText(s, ctx) {
    const g = G();
    const nm = x => (x ? x.name : '');
    const cap = capitalOf(ctx.f);
    const map = {
      city: ctx.c ? ctx.c.name : '', gov: nm(ctx.gov), ruler: nm(ctx.ruler), persona: ctx.persona ? ctx.persona.title + ctx.persona.name : '高士',
      capital: cap ? cap.name : '', omen: ctx.omen || '祥瑞', faction: ctx.subjectFac >= 0 ? g.factions[ctx.subjectFac].name : '', subject: nm(ctx.subject),
      far: ctx.far >= 0 ? g.factions[ctx.far].name : '远方', farRuler: ctx.far >= 0 ? nm(g.ruler(ctx.far)) : '其君', item: ctx.itemName || '方物', a: nm(ctx.a), b: nm(ctx.b),
      place: ctx.place || '名师门下', home: ctx.home || '故乡', plan: ctx.plan ? (D().proposals.find(p => p.id === ctx.plan) || {}).name || '' : '', hidden: ctx.hiddenName || '隐士',
    };
    return String(s || '').replace(/\{([a-zA-Z]+)\}/g, (m, k) => (map[k] !== undefined ? map[k] : m));
  }
  function evText(ev, ctx) {
    const fam = ctx.c ? famOfCity(ctx.c) : 'hua';
    const v = ctx.variant ? ev.variants && ev.variants[ctx.variant] : ev.variants && ev.variants[fam];
    return { name: (v && v.name) || ev.name, text: fillText((v && v.text) || ev.text, ctx) };
  }

  // 预报（卡上每个选项的一行 + 结构化的期望值 fx：[{ k, v, p }]，脚本玩家按它挑选）
  const FX_ZH = { gold: '金', food: '粮', land: '土地', industry: '产业', town: '町', population: '人口', order: '民心', troops: '兵', gtroops: '兵', prestige: '声望', fame: '人望', virtue: '德',
    exp: '经验', expLater: '经验', loyalty: '忠诚', 'officers.loyalty': '诸将忠诚', loyalty3: '诸将忠诚', training: '训练', rel: '好感', bond: '亲密' };
  function forecast(o, ctx) {
    const k0 = SG.SeededRandom(7);
    const fx = [];
    const add = (list, p, costK) => {
      for (const e of list || []) {
        if (e.k === 'buff') { fx.push({ k: 'buff:' + e.mod, v: e.v, p, months: e.untilHarvest ? 'harvest' : e.months }); continue; }
        if (e.k === 'item') { fx.push({ k: 'item', v: 1, p }); continue; }
        if (e.k === 'reveal' || e.k === 'defect' || e.k === 'bondKind' || e.k === 'plan' || e.k === 'away' || e.k === 'flag' || e.k === 'separate' || e.k === 'roll3') { fx.push({ k: e.k, v: e.kind || e.id || e.add || 1, p }); continue; }
        let v;
        if (e.range) v = (e.range[0] + e.range[1]) / 2;
        else if (['gold', 'food', 'land', 'industry', 'town', 'population'].indexOf(e.k) >= 0) v = amount(e, ctx.c, k0, ctx.c[e.k]);
        else if (e.k === 'gtroops' && e.pct !== undefined) v = (ctx.actor || ctx.gov || { troops: 0 }).troops * e.pct;
        else v = e.add || 0;
        if (costK && v < 0) v *= costK;
        if (e.k === 'exp' && e.intelBonus) v += e.intelBonus / 2;
        fx.push({ k: e.k, v: Math.round(v * 100) / 100, p, all: !!e.all, who: e.who || null });
      }
    };
    const oc = optCost(o, ctx);
    add(o.cost, 1, oc.k);
    let chance = null;
    if (o.roll) { chance = rollP(o.roll.p, ctx, agentFor(o, ctx)); add(o.roll.ok, chance); add(o.roll.fail, 1 - chance); }
    add(o.effects, 1);
    // 一行文字：只列确定的项（机会项在 chance 环里）
    const parts = [];
    const sumK = {};
    for (const e of fx) if (e.p === 1 && FX_ZH[e.k]) sumK[e.k] = (sumK[e.k] || 0) + e.v;
    for (const kk of Object.keys(sumK)) { const v = Math.round(sumK[kk]); if (v) parts.push(FX_ZH[kk] + ' ' + (v > 0 ? '+' : '−') + Math.abs(v)); }
    for (const e of fx) if (e.k === 'buff:harvest' && e.p === 1) parts.push('收成 ' + (e.v > 0 ? '+' : '−') + Math.round(Math.abs(e.v) * 100) + '%');
    for (const e of fx) if (e.k === 'buff:income' && e.p === 1) parts.push('收入 ' + (e.v > 0 ? '+' : '−') + Math.round(Math.abs(e.v) * 100) + '% ' + e.months + '月');
    return { fx, text: parts.join('、'), chance, cost: { gold: oc.gold, food: oc.food } };
  }

  // 一个事件：抽中之后（电脑当场结算；玩家生成卡，minor 时按设置委任）
  function runEvent(f, pick0, k, mo, ym, tag) {
    const g = G();
    const ctx = makeCtx(f, pick0, k, mo, ym);
    if (!ctx) return null;
    stamp(ctx);
    const ev = ctx.ev;
    let val = applyEffects(ev.pre, ctx);
    Engage.stats.events[ev.id] = (Engage.stats.events[ev.id] || 0) + 1;
    const opts = (ev.options || []).filter(o => !o.playerOnly || isPlayer(f));
    if (isPlayer(f) && ev.modal !== false && opts.length) {
      const card = makeEventCard(ctx, tag);
      Engage.stats.evValue += val;
      return { card, val };
    }
    if (isPlayer(f) && ev.modal === false) { const t = evText(ev, ctx); news(t.text); logEvt('toast', ev.id, null, f); }
    if (opts.length) {
      const o = aiChoose(opts, ctx);
      val += applyOption(o, ctx).val;
    }
    Engage.stats.evValue += val;
    return { val };
  }
  function makeEventCard(ctx, tag) {
    const g = G(), ev = ctx.ev, s = st();
    const t = evText(ev, ctx);
    const opts = (ev.options || []).filter(o => !o.playerOnly || isPlayer(ctx.f));
    const rec = aiChoose(opts, ctx);
    const safe = safeOption(opts, ctx);
    const card = {
      uid: s.seq++, kind: 'event', slot: 'random', ev: ev.id, cat: ev.cat, icon: ev.icon, f: ctx.f, city: ctx.c.id, due: g.monthIndex + (tag === 'now' ? 0 : 1), made: g.monthIndex,
      name: t.name, text: t.text, hist: tag && tag.hist ? tag.hist : null, foot: ev.note || null,
      who: { gov: ctx.gov ? ctx.gov.id : -1, ruler: ctx.ruler ? ctx.ruler.id : -1, a: ctx.a ? ctx.a.id : -1, b: ctx.b ? ctx.b.id : -1, subject: ctx.subject ? ctx.subject.id : -1 },
      ctx: { origin: ctx.origin, far: ctx.far, subjectFac: ctx.subjectFac, persona: ctx.persona ? ctx.persona.name : null, omen: ctx.omen || null, plan: ctx.plan || null, place: ctx.place || null, home: ctx.home || null, variant: ctx.variant || null, disaster: !!ctx.disaster },
      options: opts.map(o => {
        const fc = forecast(o, ctx);
        return { id: o.id, label: o.label, cost: fc.cost, forecast: fc.text, fx: fc.fx, chance: fc.chance, rec: o === rec, safe: o === safe, ok: affordable(o, ctx, false), playerOnly: !!o.playerOnly, duel: o.duel || null, miniGame: o.miniGame || null };
      }),
      minor: CFG.minorEvents.indexOf(ev.id) >= 0,
    };
    return card;
  }
  // 卡 → 结算用的上下文（读档后武将可能已不在：没了的人跳过）
  function ctxOfCard(card) {
    const g = G(), d = D();
    const gen = id => (id >= 0 && g.generals[id] && !g.generals[id].dead ? g.generals[id] : null);
    const c = g.cities[card.city];
    const ev = EV()[card.ev];
    const persona = card.ctx.persona ? Object.values(d.sages).flat().find(p => p.name === card.ctx.persona) || null : null;
    const ctx = { f: card.f, c, ev, k: rngAt(card.made, 'card', card.uid), gov: gen(card.who.gov) || adminOf(c) || g.ruler(card.f), ruler: g.ruler(card.f), a: gen(card.who.a), b: gen(card.who.b),
      subject: gen(card.who.subject), disaster: card.ctx.disaster, origin: card.ctx.origin, far: card.ctx.far, subjectFac: card.ctx.subjectFac, persona, omen: card.ctx.omen, plan: card.ctx.plan,
      place: card.ctx.place, home: card.ctx.home, variant: card.ctx.variant };
    if (ctx.subject && ev.id === 'deserter') ctx.subjectFac = ctx.subject.faction;
    return ctx;
  }

  // ================================================================ 卡的排期（R1，INTEGRATION §6.5）==
  // 定期卡的优先级（数字小者先；SG.Reg.cards 可覆盖）：归附 > 索贡 > 武林大会（不占名额）> 文会 > 节庆 > 电脑的提议
  const CARD_PRI = { submit: 1, demand: 2, tourney: 3, wenhui: 4, festival: 5, offer: 6 };
  function cardPri(kind) { const R = SG.Reg && SG.Reg.cards; const v = R && R.has && R.has(kind) ? R.get(kind) : null; return v && typeof v.pri === 'number' ? v.pri : CARD_PRI[kind] || 9; }
  function delegateMode() {
    try { if (SG.Settings && typeof SG.Settings.get === 'function') { const v = SG.Settings.get('engage.delegate'); if (v === 'ask' || v === 'minor') return v; } } catch (e) { /* 无设置 */ }
    return CFG.delegate;
  }
  // 新卡入队：玩家的自动作答（脚本 / 委任）在此处理
  function queueCard(card) {
    const s = st();
    Engage.stats.cards++;
    // 测试与脚本玩家：Engage.autoAnswer(card) → optionId（或 null 交给玩家）
    if (typeof Engage.autoAnswer === 'function') {
      let id = null;
      try { id = Engage.autoAnswer(card); } catch (e) { console.error(e); }
      if (id) { s.cards.push(card); resolve(card, id, { auto: 'script' }); return card; }
    }
    if (card.kind === 'event' && card.minor && delegateMode() === 'minor') {
      s.cards.push(card);
      const r = resolve(card, recOf(card), { auto: 'delegate' });
      Engage.stats.delegated++;
      news(`委任：${card.name}——${optLabel(card, r.option)}。`);
      return card;
    }
    s.cards.push(card);
    emit('engage.card', card);
    return card;
  }
  const recOf = card => { const o = card.options.find(x => x.rec) || card.options.find(x => x.ok) || card.options[0]; return o ? o.id : null; };
  const optLabel = (card, id) => { const o = card.options.find(x => x.id === id); return o ? o.label : id; };
  // 每月一次（第 3 相末尾）：本月要呈现的卡 ≤ 1 随机 + ≤ 1 定期；其余：时间性的（节庆、文会）按 ★ 自动、其余延到下月
  function scheduleCards() {
    const g = G(), s = st();
    const due = g.monthIndex + 1;
    const mine = s.cards.filter(c => !c.done && c.due === due && c.f === g.player);
    const rnd = mine.filter(c => c.slot === 'random');
    rnd.sort((a, b) => (b.hist ? 1 : 0) - (a.hist ? 1 : 0) || a.uid - b.uid);
    for (const c of rnd.slice(1)) { const r = resolve(c, recOf(c), { auto: 'overflow' }); Engage.stats.overflow++; news(`${c.name}：${optLabel(c, r.option)}（按推荐处理）。`); }
    const sch = mine.filter(c => c.slot === 'scheduled' && c.kind !== 'tourney');
    sch.sort((a, b) => cardPri(a.kind) - cardPri(b.kind) || a.uid - b.uid);
    for (const c of sch.slice(1)) {
      if (c.kind === 'festival' || c.kind === 'wenhui') { const r = resolve(c, recOf(c), { auto: 'overflow' }); Engage.stats.overflow++; news(`${c.name}：${optLabel(c, r.option)}（按推荐处理）。`); }
      else if ((c.waited | 0) < CFG.aiOffers.waitMonths + (c.kind === 'submit' || c.kind === 'demand' ? 1 : 0)) { c.waited = (c.waited | 0) + 1; c.due = due + 1; if ((c.due - 1) % 12 + 1 === 1 && (c.kind === 'submit' || c.kind === 'demand')) c.due++; }
      else { resolve(c, safeOf(c), { auto: 'expired' }); }
    }
  }
  const safeOf = card => { const o = card.options.find(x => x.safe) || card.options[card.options.length - 1]; return o.id; };
  // 上个月没答的卡（界面没有呈现、Node、读档）：按 ★ 结算（R1 溢出的同一规则）
  function expireCards() {
    const g = G(), s = st();
    for (const c of s.cards) {
      if (c.done || c.due > g.monthIndex) continue;
      if (c.kind === 'tourney') continue;
      resolve(c, recOf(c), { auto: 'expired' });
    }
    s.cards = s.cards.filter(c => !c.done && g.monthIndex - c.made < 24);
  }
  function monthStartCards(f) {
    if (!live()) return [];
    const g = G();
    if (f === undefined) f = g.player;
    const list = st().cards.filter(c => !c.done && c.f === f && c.due <= g.monthIndex);
    const rnd = list.filter(c => c.slot === 'random').sort((a, b) => a.uid - b.uid).slice(0, 1);
    const sch = list.filter(c => c.slot === 'scheduled' && c.kind !== 'tourney').sort((a, b) => cardPri(a.kind) - cardPri(b.kind) || a.uid - b.uid).slice(0, 1);
    return rnd.concat(sch);
  }
  const pendingCards = f => (live() ? st().cards.filter(c => !c.done && c.f === (f === undefined ? G().player : f)) : []);

  // 结算一张卡：→ { ok, option, roll, tier, text, effects }
  function resolve(card, optionId, opts) {
    opts = opts || {};
    if (!live() || !card || card.done) return { ok: false, why: '此事已了结。' };
    const g = G();
    const o = card.options.find(x => x.id === optionId) || card.options.find(x => x.rec) || card.options[0];
    card.done = true; card.answer = o.id; card.answeredAt = g.monthIndex; card.auto = opts.auto || null;
    let res;
    try {
      if (card.kind === 'event') res = resolveEvent(card, o.id, opts);
      else res = resolveScheduled(card, o.id, opts);
    } catch (e) { console.error('SG.Engage.resolve', e); res = { ok: false, why: String(e && e.message) }; }
    res = res || { ok: true };
    res.option = o.id;
    if (isPlayer(card.f)) logEvt('card', card.kind === 'event' ? card.ev : card.kind, o.id, card.f);
    return res;
  }
  function resolveEvent(card, optionId, opts) {
    const ev = EV()[card.ev];
    const ctx = ctxOfCard(card);
    const o = ev.options.find(x => x.id === optionId);
    if (!o) return { ok: false };
    const before = { gold: ctx.c.gold, food: ctx.c.food, order: ctx.c.order, prestige: prestigeOf(ctx.f) };
    // 付不起（期间钱粮变了）：退回安全项
    let oo = o;
    if (!affordable(o, ctx, false)) oo = safeOption(ev.options.filter(x => !x.playerOnly || isPlayer(ctx.f)), ctx);
    if (opts.touhu !== undefined) ctx.touhu = opts.touhu;
    if (opts.duel && o.duel) {
      // 亲临观战：胜者 / 败者由单挑结果给出（engage-screen.js）
      ctx.winner = opts.duel.winner >= 0 ? G().generals[opts.duel.winner] : ctx.a;
      ctx.loser = opts.duel.loser >= 0 ? G().generals[opts.duel.loser] : ctx.b;
      applyEffects(o.effects, ctx); applyEffects(o.win, ctx);
      return { ok: true, tier: 'good', effects: diff(before, ctx) };
    }
    if (o.duel) oo = ev.options.find(x => !x.playerOnly) || oo;   // 没有单挑画面：按「击鼓助兴」
    const r = applyOption(oo, ctx);
    const tier = r.rolled ? (r.rolled.ok ? 'great' : 'miss') : ev.disaster && (oo.cost || []).length ? 'good' : 'normal';
    return { ok: true, tier, roll: r.rolled, effects: diff(before, ctx), option: oo.id };
  }
  function diff(before, ctx) {
    const out = [];
    const c = ctx.c;
    for (const k of ['gold', 'food', 'order']) if (c[k] !== before[k]) out.push({ k, from: before[k], to: c[k] });
    const p = prestigeOf(ctx.f); if (p !== before.prestige) out.push({ k: 'prestige', from: before.prestige, to: p });
    return out;
  }

  // ================================================================ 国策（§7）==
  const MIS = () => { const d = D(); if (!d._mis) Object.defineProperty(d, '_mis', { value: Object.fromEntries(d.missions.map(m => [m.id, m])), enumerable: false }); return d._mis; };
  function snapshot(f, c) {
    return { stat: Math.min(c.land, c.industry, c.town), statKey: ['land', 'industry', 'town'].reduce((a, b) => (c[b] < c[a] ? b : a)), hires: getCnt(f, 'hires'), caravans: getCnt(f, 'caravans'),
      caravansFar: getCnt(f, 'caravansForeign'), exped: getCnt(f, 'expeditions'), expedFar: getCnt(f, 'expedFar'), projects: getCnt(f, 'projects'), items: getCnt(f, 'items'),
      common: getCnt(f, 'item:common'), uncommon: getCnt(f, 'item:uncommon'), rare: getCnt(f, 'item:rare'), w1: getCnt(f, 'wenhuiPlace1'), w2: getCnt(f, 'wenhuiPlace2'), w3: getCnt(f, 'wenhuiPlace3'),
      t1: getCnt(f, 'tourneyPlace1'), t2: getCnt(f, 'tourneyPlace2'), t3: getCnt(f, 'tourneyPlace3'), conq: getCnt(f, 'conquests') };
  }
  function missionWeight(m, f) {
    const g = G(), cs = g.citiesOf(f), K = CFG.missions.weakK;
    switch (m.weight) {
      case 'devRoom': return cs.some(c => Math.min(c.land, c.industry, c.town) < 600) ? K : 1;
      case 'lowFood': { const cons = Math.max(1, Math.floor(sum(cs, c => g.troopsIn(c)) / B().TroopsPerFood)); return sum(cs, c => c.food) < cons * 6 ? K : 1; }
      case 'fewGens': return g.generalsOf(f).length < cs.length * 3 ? K : 1;
      case 'lowOrder': return avg(cs.map(c => c.order)) < 60 ? K : 1;
      case 'threat': return cs.some(c => threatened(f, c)) ? K : 1;
      default: return 1;
    }
  }
  function offerMissions(f, n, bonus) {
    const g = G(), s = st();
    const active = s.missions.filter(m => !m.done && !m.expired && m.until > g.monthIndex).length;
    if (!bonus && active >= CFG.missions.slots) return [];
    const k = rng('mission', s.seq++);
    const recentAttack = g.monthIndex - (g.factions[f].eng.lastAttack === undefined ? -999 : g.factions[f].eng.lastAttack) < CFG.missions.militaryAfter;
    const list = D().missions.filter(m => (!m.military || recentAttack) && (m.needs !== 'titles' || (SG.Titles && SG.Titles.enabled !== false))
      && (m.goal !== 'tourneyPlace' || (SG.Tourney && SG.Tourney.enabled !== false)) && (m.goal !== 'wenhuiPlace' || sub('wenhui'))
      && !s.missions.some(x => x.id === m.id && !x.done && x.until > g.monthIndex) && !s.mOffers.some(x => x.id === m.id));
    const out = [];
    const cities = g.citiesOf(f);
    if (!cities.length) return out;
    let military = 0;
    for (let i = 0; i < n && list.length; i++) {
      const w = list.map(m => (m.military && military ? 0 : missionWeight(m, f)));
      let tot = sum(w), r = k.nextDouble() * tot, j = 0;
      for (; j < list.length; j++) { r -= w[j]; if (r < 0) break; }
      const t = list.splice(Math.min(j, list.length - 1), 1)[0];
      if (t.military) military++;
      const tier = 1 + Math.floor(k.nextDouble() * 3);
      const c = t.id === 'm_dev' ? cities.slice().sort((a, b) => Math.min(a.land, a.industry, a.town) - Math.min(b.land, b.industry, b.town) || a.id - b.id)[0] : cities[Math.floor(k.nextDouble() * cities.length)];
      const offer = { oid: s.seq++, id: t.id, tier, city: c.id, since: g.monthIndex, until: g.monthIndex + 1 + CFG.missions.offerMonths, by: advisorOf(f), extra: missionExtra(t, f, c, tier, k), bonus: !!bonus };
      s.mOffers.push(offer);
      out.push(offer);
      cnt(f, 'missionsOffered');
    }
    return out;
  }
  function missionExtra(t, f, c, tier, k) {
    const g = G();
    if (t.goal === 'relation') { const ns = neighbors(f).filter(h => !vassalOk(f, h)); const cand = ns.length ? ns : g.factions.filter(x => x.alive && x.id !== f).map(x => x.id); return { target: cand.length ? cand[Math.floor(k.nextDouble() * cand.length)] : -1 }; }
    if (t.goal === 'project') { const b = buildable(c).filter(x => x.ok); return { project: b.length ? b[Math.floor(k.nextDouble() * b.length)].id : null }; }
    return {};
  }
  function advisorOf(f) { const g = G(); const gs = g.generalsOf(f).filter(x => !isRuler(x)); return gs.length ? gs.reduce((a, b) => (b.intel > a.intel ? b : a)).id : (g.ruler(f) || {}).id; }
  function acceptMission(f, oid) {
    if (!live()) return { ok: false, why: '系统未开启。' };
    const g = G(), s = st();
    const i = s.mOffers.findIndex(x => x.oid === oid);
    if (i < 0) return { ok: false, why: '此策已过期。' };
    if (s.missions.filter(m => !m.done && !m.expired && m.until > g.monthIndex).length >= CFG.missions.slots) return { ok: false, why: '国策已满三项。' };
    const o = s.mOffers.splice(i, 1)[0];
    // §7.1：朝廷一次提两项，玩家取其一（或不取）——取了一项，同批的另一项撤回；高士加的那一项（bonus）不算在内
    if (!o.bonus) s.mOffers = s.mOffers.filter(x => x.bonus);
    const t = MIS()[o.id], c = g.cities[o.city];
    const m = { id: o.id, mid: o.oid, tier: o.tier, city: o.city, since: g.monthIndex, until: g.monthIndex + t.dl[o.tier - 1], done: false, start: snapshot(f, c), rewardBase: facInc(f), extra: o.extra || {} };
    if (t.goal === 'wenhuiPlace') m.until = Math.max(m.until, nextMonthIdx(CFG.wenhui.month) + 1);
    if (t.goal === 'tourneyPlace') m.until = Math.max(m.until, nextMonthIdx(1) + 1);
    s.missions.push(m);
    return { ok: true, mission: m };
  }
  function nextMonthIdx(month) { const g = G(); const y = g.month <= month ? g.year : g.year + 1; return y * 12 + month; }
  function cancelMission(f, mid) { if (!live()) return false; const s = st(); const m = s.missions.find(x => x.mid === mid && !x.done); if (!m) return false; m.until = G().monthIndex; m.cancelled = true; return true; }
  function missionGoal(m, f) {
    const g = G(), c = g.cities[m.city], t = MIS()[m.id], P = t.params, i = m.tier - 1, s = m.start;
    switch (t.goal) {
      case 'cityStatGain': return { cur: c.owner === f ? c[s.statKey] - s.stat : 0, need: P.delta[i] };
      case 'foodMonths': { const cons = Math.max(1, Math.floor(sum(g.citiesOf(f), x => g.troopsIn(x)) / B().TroopsPerFood)); return { cur: sum(g.citiesOf(f), x => x.food), need: cons * P.months[i] + 2000 }; }
      case 'hires': return { cur: getCnt(f, 'hires') - s.hires, need: P.n[i] };
      case 'drilled': return { cur: g.generalsOf(f).filter(x => x.troops >= P.troops && x.training >= P.training).length, need: P.n[i] };
      case 'minOrder': { const cs = g.citiesOf(f); return { cur: cs.filter(x => x.order >= P.order[i]).length, need: cs.length }; }
      case 'caravans': return m.tier === 3 && P.foreignHard ? { cur: Math.min(getCnt(f, 'caravans') - s.caravans, (getCnt(f, 'caravansForeign') - s.caravansFar) > 0 ? 99 : P.n[i] - 1), need: P.n[i] } : { cur: getCnt(f, 'caravans') - s.caravans, need: P.n[i] };
      case 'relation': { const t2 = m.extra && m.extra.target >= 0 ? m.extra.target : -1; if (t2 < 0 || !g.factions[t2].alive) return { cur: 0, need: 1, fail: true }; return { cur: vassalOk(f, t2) ? P.rel[i] : Math.max(0, rel(f, t2)), need: P.rel[i] }; }
      case 'project': { const pid = m.extra && m.extra.project; return { cur: pid ? (hasProject(c, pid) && c.owner === f ? 1 : 0) : getCnt(f, 'projects') - s.projects, need: 1 }; }
      case 'expeditions': return m.tier === 3 && P.farHard ? { cur: Math.min(getCnt(f, 'expeditions') - s.exped, (getCnt(f, 'expedFar') - s.expedFar) > 0 ? 99 : P.n[i] - 1), need: P.n[i] } : { cur: getCnt(f, 'expeditions') - s.exped, need: P.n[i] };
      case 'wenhuiPlace': { const need = P.place[i]; let d = getCnt(f, 'wenhuiPlace1') - s.w1; if (need >= 2) d += getCnt(f, 'wenhuiPlace2') - s.w2; if (need >= 3) d += getCnt(f, 'wenhuiPlace3') - s.w3; return { cur: d > 0 ? 1 : 0, need: 1 }; }
      case 'tourneyPlace': { const need = P.place[i]; let d = getCnt(f, 'tourneyPlace1') - s.t1; if (need >= 2) d += getCnt(f, 'tourneyPlace2') - s.t2; if (need >= 3) d += getCnt(f, 'tourneyPlace3') - s.t3; return { cur: d > 0 ? 1 : 0, need: 1 }; }
      case 'items': { const r = P.rarity[i]; return { cur: getCnt(f, 'item:' + r) - s[r], need: P.n[i] }; }
      case 'titled': { const T = SG.Titles; if (!T || T.enabled === false) return { cur: 0, need: P.n[i], fail: true }; return { cur: g.generalsOf(f).filter(x => (T.curTier ? T.curTier(x) : 0) >= P.tier[i]).length, need: P.n[i] }; }
      case 'levels': return { cur: g.generalsOf(f).filter(x => (x.level | 0) >= P.level[i]).length, need: P.n[i] };
      case 'cityLevel': return { cur: Math.max(0, ...g.citiesOf(f).map(cityLevel)), need: P.level[i] };
      case 'defense': { const cs = g.citiesOf(f).filter(isFrontier); return { cur: cs.filter(x => SG.cityDefense(x) >= P.def[i]).length, need: cs.length }; }
      case 'conquer': return { cur: getCnt(f, 'conquests') - s.conq, need: P.n[i] };
      default: return { cur: 0, need: 1 };
    }
  }
  function missionText(m) {
    const g = G(), t = MIS()[m.id], P = t.params, i = m.tier - 1, c = g.cities[m.city];
    const pn = id => projectName(id, famOfCity(c));
    switch (t.goal) {
      case 'cityStatGain': return `${c.name}的${{ land: '土地', industry: '产业', town: '町' }[m.start.statKey]}提高 ${P.delta[i]}`;
      case 'foodMonths': return `存粮可供全军 ${P.months[i]} 个月另加 2000`;
      case 'hires': return `登用 ${P.n[i]} 名人才`;
      case 'drilled': return `${P.n[i]} 名武将统兵 ${P.troops} 以上、训练 ${P.training}`;
      case 'minOrder': return `每座城民心 ≥ ${P.order[i]}`;
      case 'caravans': return `${P.n[i]} 支商队平安归来` + (m.tier === 3 ? '（其中一支往异域）' : '');
      case 'relation': return `与${m.extra && m.extra.target >= 0 ? g.factions[m.extra.target].name : '邻国'}好感 ≥ ${P.rel[i]} 或订约`;
      case 'project': return m.extra && m.extra.project ? `在${c.name}建成「${pn(m.extra.project)}」` : '完成一项营建';
      case 'expeditions': return `完成 ${P.n[i]} 次探索` + (m.tier === 3 ? '（其中一次往他方）' : '');
      case 'wenhuiPlace': return `文会${P.place[i] === 1 ? '夺魁' : '名列前 ' + P.place[i]}`;
      case 'tourneyPlace': return `武林大会${P.place[i] === 1 ? '夺冠' : '名列前 ' + P.place[i]}`;
      case 'items': return `得到 ${P.n[i]} 件${{ common: '凡品', uncommon: '良品', rare: '珍品' }[P.rarity[i]]}`;
      case 'titled': return `${P.n[i]} 名武将任 ${['', '九品', '七品', '五品', '三品', '一品'][P.tier[i]]}以上官职`;
      case 'levels': return `${P.n[i]} 名武将达到 ${P.level[i]} 级`;
      case 'cityLevel': return `一座城成为${SG.Econ ? SG.Econ.levelName(P.level[i]) : '名城'}`;
      case 'defense': return `每座边城城防 ≥ ${P.def[i]}`;
      case 'conquer': return `攻取 ${P.n[i]} 座城`;
      default: return t.name;
    }
  }
  function missionReward(m, f) {
    const R = D().missionReward, t = m.tier - 1;
    return { gold: Math.round(R.goldK[t] * m.rewardBase), prestige: R.prestige[t], exp: R.exp[t], item: R.item[t], fame: R.fame[t] };
  }
  function missionsMonth(f) {
    const g = G(), s = st(), mi1 = g.monthIndex + 1, Mc = CFG.missions;
    let freed = false;
    for (const m of s.missions) {
      if (m.done || m.expired || m.until <= g.monthIndex) continue;
      const q = missionGoal(m, f);
      if (q.fail || q.cur < q.need) continue;
      m.done = true; m.doneAt = g.monthIndex; freed = true;
      cnt(f, 'missionsDone');
      const R = missionReward(m, f), k = rng('mreward', m.since, m.id);
      const cap = capitalOf(f);
      if (cap) cap.gold += R.gold;
      cnt(f, 'missionGold', R.gold);
      addPrestige(f, R.prestige);
      const c = g.cities[m.city];
      const who = c.owner === f ? adminOf(c) || g.ruler(f) : g.ruler(f);
      storyExp(who, R.exp);
      if (R.fame) addFameEv(f, R.fame);
      const it = R.item;
      if (it) { const r = k.nextDouble(); if (it.rare && r < it.rare) grantItem(f, 'rare', 'mission', { k }); else if (r < (it.rare || 0) + it.uncommon) grantItem(f, 'uncommon', 'mission', { k }); }
      logEvt('mission', m.id, m.tier, f);
      news(`国策「${MIS()[m.id].name}」功成：金 +${R.gold}、声望 +${R.prestige}。`);
    }
    // 期满未成：悄然作罢（R4）
    for (const m of s.missions) if (!m.done && !m.expired && m.until <= mi1) { m.expired = true; freed = true; }
    s.mOffers = s.mOffers.filter(o => o.until > mi1);
    const active = s.missions.filter(m => !m.done && !m.expired && m.until > mi1).length;
    // 每三个月、或有一席空出时，朝廷提两项（旧的提议换掉）；玩家选一项或不选（不弹卡，国策签上亮角标，R8）
    if ((mi1 % Mc.every === 0 || freed || (!s.mOffers.length && !s.missions.length)) && active < Mc.slots) { s.mOffers = s.mOffers.filter(o => o.bonus); offerMissions(f, Mc.offers); }
    s.missions = s.missions.filter(m => (m.done ? g.monthIndex - m.doneAt < 12 : !m.expired || g.monthIndex - m.until < 3));
  }
  function missions(f) {
    if (!live()) return [];
    const g = G();
    return st().missions.filter(m => !m.done && !m.expired && m.until > g.monthIndex).map(m => Object.assign({ name: MIS()[m.id].name, text: missionText(m), progress: missionGoal(m, f === undefined ? g.player : f), left: m.until - g.monthIndex, reward: missionReward(m, f) }, m));
  }
  function missionOffers(f) {
    if (!live()) return [];
    const g = G();
    return st().mOffers.map(o => { const m = Object.assign({ start: snapshot(f === undefined ? g.player : f, g.cities[o.city]), rewardBase: facInc(f === undefined ? g.player : f) }, o); return Object.assign({ name: MIS()[o.id].name, text: missionText(m), months: MIS()[o.id].dl[o.tier - 1], reward: missionReward(m, f) }, o); });
  }

  // ================================================================ 营建（§8）==
  function projectName(id, fam) { const p = PROJ()[id]; if (!p) return id; return p.names[fam] || p.names.any || p.names.hua || p.name; }
  function projReq(p, c) {
    const g = G(), r = p.req || {};
    if (r.minLand && c.land < r.minLand) return `需土地 ${r.minLand}`;
    if (r.minTown && c.town < r.minTown) return `需町 ${r.minTown}`;
    if (r.minIndustry && c.industry < r.minIndustry) return `需产业 ${r.minIndustry}`;
    if (r.minIndTown && c.industry + c.town < r.minIndTown) return `需产业 + 町 ${r.minIndTown}`;
    if (r.minTroopsHere && g.troopsIn(c) < r.minTroopsHere) return `需城中兵 ${r.minTroopsHere}`;
    if (r.coastalOrRiver && !isCoast(c) && !isRiver(c)) return '需临河或临海';
    if (r.frontier && !isFrontier(c)) return '只建于边城';
    if (r.capital && capitalOf(c.owner) !== c) return '只建于都城';
    if (r.oncePerFaction && g.citiesOf(c.owner).some(x => hasProject(x, p.id) || (x.eng.proj && x.eng.proj.id === p.id))) return '每个势力只能建一座';
    // 奇观：城级 ≥ 郡城（ENGAGEMENT §8.1；数据 minCityLevel 为 1–5 级，城级下标 = 级 − 1）
    if (r.minCityLevel !== undefined && cityLevel(c) + 1 < r.minCityLevel) return `需城级 ≥ ${SG.Econ ? SG.Econ.levelName(r.minCityLevel - 1) : r.minCityLevel}`;
    return null;
  }
  function projCost(p, c) { const dd = st().disc[c.owner] || {}; return Math.round(p.cost * (1 - (dd[p.id] || 0))); }
  function projRate(sup, c) { return sup && present(sup, c.owner) && sup.city === c.id ? CFG.projects.base + sup.pol / CFG.projects.polDiv : CFG.projects.absent; }
  function buildable(c) {
    if (!live() || !c || c.owner < 0) return [];
    const g = G(), fam = famOfCity(c);
    const sup = gensIn(c).slice().sort((a, b) => b.pol - a.pol || a.id - b.id)[0] || null;
    return D().projects.map(p => {
      let why = c.eng.done.indexOf(p.id) >= 0 ? '已建成' : c.eng.proj ? '本城正在营建' : projReq(p, c);
      const cost = projCost(p, c);
      if (!why && c.gold < cost) why = `金不足（需 ${cost}）`;
      const rate = projRate(sup, c);
      return { id: p.id, name: projectName(p.id, fam), cost, work: p.work, ok: !why, why, months: Math.ceil(p.work / rate), sup: sup ? sup.id : -1, fx: p.fx, anchor: p.anchor };
    });
  }
  function startProject(c, id, supId, opts) {
    opts = opts || {};
    if (!sub('projects')) return { ok: false, why: '营建未开启。' };
    const g = G(), p = PROJ()[id];
    if (!p || !c || c.owner < 0) return { ok: false, why: '无此工程。' };
    if (c.eng.proj) return { ok: false, why: '本城正在营建。' };
    if (c.eng.done.indexOf(id) >= 0) return { ok: false, why: '已建成。' };
    const why = projReq(p, c); if (why) return { ok: false, why };
    const cost = projCost(p, c);
    if (opts.ai) { if (!SG.AIBudget || !SG.AIBudget.canSpend(c, cost, 'project')) return { ok: false, why: '钱不够' }; SG.AIBudget.spend(c, cost, 'project'); }
    else { if (c.gold < cost) return { ok: false, why: `金不足（需 ${cost}）` }; c.gold -= cost; }
    const dd = st().disc[c.owner]; if (dd && dd[id]) delete dd[id];
    let sup = supId >= 0 ? g.generals[supId] : null;
    if (!sup || sup.faction !== c.owner) sup = gensIn(c).slice().sort((a, b) => b.pol - a.pol || a.id - b.id)[0] || g.ruler(c.owner);
    c.eng.proj = { id, work: 0, sup: sup ? sup.id : -1, since: g.monthIndex };
    if (isPlayer(c.owner)) { cnt(c.owner, 'projStarted'); cnt(c.owner, 'spend:project', cost); logEvt('build', [c.id, id], cost, c.owner); }
    if (!opts.ai && SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('build', c, sup, { ok: true, project: id, cost, work: p.work, months: Math.ceil(p.work / projRate(sup, c)), sub: 'start' });
    return { ok: true, cost, project: c.eng.proj };
  }
  function projectTick(c, ctx) {
    const g = G(), pr = c.eng.proj; if (!pr) return;
    const p = PROJ()[pr.id], sup = pr.sup >= 0 ? g.generals[pr.sup] : null;
    const here = sup && present(sup, c.owner) && sup.city === c.id;
    pr.work += here ? CFG.projects.base + sup.pol / CFG.projects.polDiv : CFG.projects.absent;
    if (here) routineExp(sup, CFG.exp.supervisor);
    if (pr.work < p.work) return;
    c.eng.done.push(pr.id); c.eng.proj = null; c.eng.newAt = g.monthIndex;
    Engage.stats.projectsDone++;
    cnt(c.owner, 'projects');
    addPrestige(c.owner, pr.id === 'wonder' ? CFG.prestige.wonder : CFG.prestige.project);
    if (pr.id === 'wonder') cnt(c.owner, 'wonder');
    if (isPlayer(c.owner)) {
      news(`${c.name}「${projectName(pr.id, famOfCity(c))}」落成。`);
      logEvt('built', [c.id, pr.id], null, c.owner);
      if (ctx && ctx.stageUps) ctx.stageUps.push({ city: c.id, kind: 'project', id: pr.id });
      if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('build', c, sup && !sup.dead ? sup : null, { ok: true, project: pr.id, sub: 'done', tier: 'great' });
    }
    emit('engage.project', c, pr.id);
  }
  // §8.6 电脑营建（自由支出第 7 相 onSpend('project')）
  function aiProjects(f) {
    if (!sub('projects') || !Engage.aiProjects) return;
    const g = G();
    for (const c of g.cities) {
      if (c.owner !== f || c.eng.proj) continue;
      if (rngAt(g.monthIndex, 'aiproj', c.id).nextDouble() > CFG.projects.aiP) continue;
      if (!gensIn(c).length) continue;   // 城中无人监工：不开工
      const order = isFrontier(c) ? CFG.projects.aiBorder : CFG.projects.aiInner;
      for (const id of order) {
        const p = PROJ()[id];
        if (c.eng.done.indexOf(id) >= 0 || projReq(p, c)) continue;
        const r = startProject(c, id, -1, { ai: true });
        if (r.ok) Engage.stats.aiProj++;
        break;
      }
    }
  }
  const shopTierBonus = c => (sub('projects') && hasProject(c, 'market') ? 1 : 0);
  const shopUncommonSlots = c => (sub('projects') && hasProject(c, 'forge') ? 1 : 0);
  function defense(c) { if (!live() || !c || !c.eng) return 0; let d = buffSum(c, 'defense'); if (sub('projects')) { if (hasProject(c, 'walls')) d += PROJ().walls.fx.defense; if (hasProject(c, 'beacons')) d += CFG.projects.beaconsDef; } return d; }

  // ================================================================ 商队（§9.1）==
  function caravanLimit(f) { const n = G().cityCount(f); return Math.min(CFG.caravan.max, CFG.caravan.maxBase + Math.floor(n / CFG.caravan.perCities)); }
  function investMax(c) { const C = CFG.caravan; return Math.min(C.investMax, C.investBase + C.investPerTier * shopTier(c)); }
  function caravanPass(f, o) { const g = G(); return o < 0 || o === f || g.allied(o, f) || nonAggro(o, f) || pactOf(o, f) || (rel(f, o) >= 0 && !atWar(f, o)); }
  function caravanTargets(c, leader) {
    if (!sub('trade') || !c || c.owner < 0) return [];
    const g = G(), f = c.owner, C = CFG.caravan;
    leader = leader || gensIn(c).filter(x => !isRuler(x)).sort((a, b) => b.pol - a.pol || a.id - b.id)[0] || null;
    const lp = leader ? leader.pol : 50, lw = leader ? leader.war : 50;
    // 0-1-2 加权的最短路（海路：两端都没有港口时 +1 跳）；≤ 6 跳；只经过可通行的城
    const harbour = id => hasProject(g.cities[id], 'harbour');
    const dist = new Map([[c.id, { h: 0, sea: 0 }]]);
    let frontier = [c.id];
    const out = [];
    while (frontier.length) {
      frontier.sort((a, b) => dist.get(a).h - dist.get(b).h || a - b);
      const id = frontier.shift(); const d0 = dist.get(id);
      if (d0.done) continue; d0.done = true;
      for (const j of g.cities[id].links) {
        const sea = g.seaLinks && g.seaLinks.has(Math.min(id, j) + '-' + Math.max(id, j));
        const w = 1 + (sea && !harbour(id) && !harbour(j) ? 1 : 0);
        const h = d0.h + w;
        if (h > C.maxHops) continue;
        const prev = dist.get(j);
        if (prev && prev.h <= h) continue;
        const o = g.cities[j].owner;
        if (!caravanPass(f, o)) continue;
        dist.set(j, { h, sea: d0.sea + (sea ? 1 : 0), seaH: (d0.seaH || 0) + (sea && (harbour(id) || harbour(j)) ? 1 : 0) });
        frontier.push(j);
      }
    }
    for (const [id, d] of dist) {
      if (id === c.id) continue;
      const t = g.cities[id], o = t.owner;
      const own = o === f;
      if (o < 0) continue;
      const foreign = famOfCity(t) !== famOfCity(c);
      if (own && !foreign) continue;
      let m = C.base + C.hop * d.h + (foreign ? C.foreign : 0) + C.tier * (shopTier(t) - 1) + (lp - 50) / C.polDiv + (hasProject(c, 'market') ? C.market : 0)
        + (!own && pactOf(f, o, 'trade') ? C.pact : 0) + buffSum(c, 'caravan') + C.sea * (d.seaH || 0);
      m = Math.min(C.cap, m);
      if (own) m *= C.ownHalf;
      let risk = clamp(C.riskHop * d.h * (1 - lw / C.riskWarDiv) * (C.riskOrder - c.order / 100), 0, C.riskMax);
      if (hasProject(c, 'roads')) risk *= C.roadsRisk;
      const months = Math.max(1, 1 + Math.ceil(d.h / 2) - (hasProject(c, 'roads') ? 1 : 0));
      const inv = investMax(c);
      const expected = Math.round(inv * ((1 - risk) * m - risk * C.lostK));
      out.push({ city: id, owner: o, hops: d.h, sea: d.sea, margin: Math.round(m * 1000) / 1000, risk: Math.round(risk * 1000) / 1000, months, foreign, own, expected, perMonth: expected / months, invest: inv });
    }
    out.sort((a, b) => b.perMonth - a.perMonth || a.city - b.city);
    return out;
  }
  function sendCaravan(c, leader, cityId, invest) {
    if (!sub('trade')) return { ok: false, why: '商队未开启。' };
    const g = G(), f = c.owner, s = st();
    if (!leader || leader.faction !== f || isRuler(leader) || absent(leader) || leader.city !== c.id) return { ok: false, why: '须派城中的部将领队（君主不行）。' };
    if (gensIn(c).length < 2) return { ok: false, why: '不能派走城中最后一员武将。' };
    if (s.caravans.filter(x => !x.back && x.f === f).length >= caravanLimit(f)) return { ok: false, why: `商队已达上限 ${caravanLimit(f)} 支。` };
    const t = caravanTargets(c, leader).find(x => x.city === cityId);
    if (!t) return { ok: false, why: '路途不通。' };
    invest = Math.round(clamp(invest === undefined ? t.invest : invest, CFG.caravan.investMin, investMax(c)));
    if (c.gold < invest) return { ok: false, why: `金不足（需 ${invest}）` };
    c.gold -= invest;
    const id = s.seq++;
    leader.away = { kind: 'caravan', until: g.monthIndex + t.months, home: c.id, ref: id };
    leader.moved = true;
    dirtyIdx();
    g.autoGovernor(c);
    const cv = { id, f, from: c.id, to: cityId, owner: t.owner, hops: t.hops, inv: invest, m: t.margin, risk: t.risk, since: g.monthIndex, until: g.monthIndex + t.months, leader: leader.id, foreign: t.foreign, fam: famOfCity(g.cities[cityId]), back: false };
    s.caravans.push(cv);
    if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('caravan', c, leader, { ok: true, sub: 'depart', to: cityId, invest, margin: t.margin, risk: t.risk, months: t.months });
    return { ok: true, caravan: cv };
  }
  function caravanReturn(cv) {
    const g = G(), f = cv.f, C = CFG.caravan;
    cv.back = true;
    const k = rngAt(cv.since, 'caravan', cv.id);
    const lost = k.nextDouble() < cv.risk;
    const ret = lost ? Math.round(cv.inv * C.lostK) : Math.round(cv.inv * (1 + cv.m));
    const home = g.cities[cv.from];
    const dest = home.owner === f ? home : capitalOf(f);
    if (dest) dest.gold += ret;
    cv.ret = ret; cv.lost = lost;
    cnt(f, 'tradeProfit', ret - cv.inv); cnt(f, 'caravans');
    const L = g.generals[cv.leader];
    if (L && !L.dead) storyExp(L, lost ? C.lostExp : C.exp + C.expHop * cv.hops);
    let item = null;
    if (!lost) {
      if (cv.owner >= 0 && cv.owner !== f) addRel(f, cv.owner, CFG.rel.caravan);
      if (cv.foreign) { addPrestige(f, C.foreignPrestige); cnt(f, 'caravansForeign'); const fams = st().counters[f + ':caravanFams'] || ''; if (fams.split(',').indexOf(cv.fam) < 0) st().counters[f + ':caravanFams'] = fams ? fams + ',' + cv.fam : cv.fam; }
      if (k.nextDouble() < C.itemP + C.itemHop * cv.hops) { const r = k.nextDouble(); const rar = r < C.itemW[0] ? 'common' : r < C.itemW[0] + C.itemW[1] ? 'uncommon' : 'rare'; item = grantItem(f, rar, 'caravan', { culture: g.cities[cv.to].culture, k }); }
    }
    cv.item = item;
    if (isPlayer(f)) { news(lost ? `往${g.cities[cv.to].name}的商队途中遇险，只收回 ${ret} 金。` : `往${g.cities[cv.to].name}的商队归来，得金 ${ret}（利 ${ret - cv.inv}）。`); logEvt('caravan', cv.to, ret - cv.inv, f); }
    if (isPlayer(f) && SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('caravan', dest || home, L && !L.dead ? L : null, { ok: !lost, sub: 'return', profit: ret - cv.inv, item, lost, tier: lost ? 'miss' : (ret - cv.inv >= 300 || (item && SG.Items && SG.Items.tier && SG.Items.tier(item) >= 2)) ? 'great' : 'good' });
  }
  const caravans = f => (live() ? st().caravans.filter(x => x.f === (f === undefined ? G().player : f) && !x.back) : []);

  // ================================================================ 探索（§12）==
  const SITE = () => { const d = D(); if (!d._site) Object.defineProperty(d, '_site', { value: Object.fromEntries(d.sites.map(s => [s.id, s])), enumerable: false }); return d._site; };
  function expedLimit(f) { return G().cityCount(f) >= CFG.exped.two ? 2 : 1; }
  function sites(f, c) {
    if (!sub('exped')) return [];
    const g = G(); f = f === undefined ? g.player : f;
    c = c || capitalOf(f); if (!c) return [];
    const X = CFG.exped, s = st();
    const dist = new Map([[c.id, 0]]); let q = [c.id];
    for (let h = 1; h <= X.maxHops && q.length; h++) {
      const nx = [];
      for (const id of q) for (const j of g.cities[id].links) { if (dist.has(j)) continue; const o = g.cities[j].owner; if (o >= 0 && o !== f && atWar(f, o)) continue; dist.set(j, h); nx.push(j); }
      q = nx;
    }
    const out = [];
    for (const site of D().sites) {
      const nc = g.cityByKey(site.near);
      if (!nc) continue;
      const hops = dist.has(nc.id) ? dist.get(nc.id) : -1;
      let why = hops < 0 ? '路途不通' : null;
      const last = s.visitedAt[site.id];
      if (!why && last !== undefined && g.monthIndex - last < X.revisit) why = `${X.revisit - (g.monthIndex - last)} 个月后可再访`;
      if (!why && s.exped.some(e => !e.back && e.site === site.id)) why = '已有人前往';
      const cost = X.cost + X.costHop * Math.max(0, hops);
      if (!why && c.gold < cost) why = `金不足（需 ${cost}）`;
      out.push({ site, hops, months: Math.min(X.maxMonths, X.months + Math.ceil(Math.max(0, hops) / 2)), cost, ok: !why, why, visited: s.visited.indexOf(site.id) >= 0, type: D().siteTypes[site.type] });
    }
    return out;
  }
  function sendExpedition(c, leader, siteId) {
    if (!sub('exped')) return { ok: false, why: '探索未开启。' };
    const g = G(), f = c.owner, s = st();
    if (!leader || leader.faction !== f || isRuler(leader) || absent(leader) || leader.city !== c.id) return { ok: false, why: '须派城中的部将领队（君主不行）。' };
    if (gensIn(c).length < 2) return { ok: false, why: '不能派走城中最后一员武将。' };
    if (s.exped.filter(x => !x.back && x.f === f).length >= expedLimit(f)) return { ok: false, why: '已有探索在途。' };
    const it = sites(f, c).find(x => x.site.id === siteId);
    if (!it) return { ok: false, why: '无此去处。' };
    if (!it.ok) return { ok: false, why: it.why };
    c.gold -= it.cost;
    const id = s.seq++;
    leader.away = { kind: 'exped', until: g.monthIndex + it.months, home: c.id, ref: id };
    leader.moved = true;
    dirtyIdx();
    g.autoGovernor(c);
    s.visitedAt[siteId] = g.monthIndex;
    const ex = { id, f, from: c.id, site: siteId, hops: it.hops, since: g.monthIndex, until: g.monthIndex + it.months, leader: leader.id, cost: it.cost, back: false };
    s.exped.push(ex);
    if (isPlayer(f)) cnt(f, 'spend:exped', it.cost);
    if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('explore', c, leader, { ok: true, sub: 'depart', site: siteId, cost: it.cost, months: it.months });
    return { ok: true, expedition: ex };
  }
  function expedReturn(ex) {
    const g = G(), f = ex.f, X = CFG.exped, s = st();
    ex.back = true;
    const S = SITE()[ex.site], T = D().siteTypes[S.type], k = rngAt(ex.since, 'exped', ex.id);
    const L = g.generals[ex.leader];
    cnt(f, 'expeditions');
    if (s.visited.indexOf(S.id) < 0) s.visited.push(S.id);
    if (S.myth) cnt(f, 'mythSites');
    const fromC = g.cities[ex.from], siteC = g.cityByKey(S.near);
    if (siteC && fromC && famOfCity(siteC) !== famOfCity(fromC)) cnt(f, 'expedFar');
    const res = { site: S.id, mishap: false, gold: 0, item: null, exp: 0, prestige: 0, clue: null, talent: null };
    if (k.nextDouble() < T.mishap) {
      res.mishap = true;
      if (L && !L.dead) { L.injury = { until: g.monthIndex + 1 + X.mishapMonths, war: 0, away: true }; storyExp(L, X.mishapExp); }
      cnt(f, 'mishaps');
    } else {
      const gold = Math.round((T.gold[0] + k.nextDouble() * (T.gold[1] - T.gold[0])) * (1 + ex.hops / 4));
      const cap = capitalOf(f); if (cap) cap.gold += gold;
      res.gold = gold; cnt(f, 'expGold', gold);
      const rr = k.nextDouble(), rare = Math.min(X.rareMax, T.item.rare + X.rareHop * ex.hops);
      const opt = { culture: S.culture, types: T.pool, k, fallbackGold: X.rareFallbackGold };
      if (rr < rare) res.item = grantItem(f, 'rare', 'exped', opt);
      else if (rr < rare + T.item.uncommon) res.item = grantItem(f, 'uncommon', 'exped', opt);
      else if (rr < rare + T.item.uncommon + T.item.common) res.item = grantItem(f, 'common', 'exped', opt);
      if (L && !L.dead) res.exp = storyExp(L, X.exp + X.expHop * ex.hops);
      res.prestige = T.prestige + (S.myth ? X.myth : 0);
      addPrestige(f, res.prestige);
      if (T.virtue) addVirtueEv(f, T.virtue);
      if (k.nextDouble() < X.talent) {
        const near = siteC ? new Set([siteC.id, ...siteC.links]) : null;
        const h = g.generals.find(x => x.hidden && SG.isFree(x) && (!near || near.has(x.city))) || g.generals.find(x => x.hidden && SG.isFree(x));
        if (h) { h.hidden = false; cnt(f, 'revealed'); res.talent = h.id; }
      }
      if (T.clue && k.nextDouble() < T.clue * X.clueK && siteC && SG.Items && SG.Items.enabled !== false && g.items && typeof SG.Items.hiddenIn === 'function') {
        const region = [siteC].concat(siteC.links.map(j => g.cities[j]));
        for (const cc of region) { const hs = SG.Items.hiddenIn(cc); if (hs.length && !s.clues.some(q => q.item === hs[0].item)) { s.clues.push({ item: hs[0].item, city: cc.id, f }); res.clue = { item: hs[0].item, city: cc.id }; cnt(f, 'clueFound'); break; } }
      }
    }
    ex.res = res;
    if (isPlayer(f)) {
      news(res.mishap ? `${L ? L.name : '探索队'}探访${S.name}途中受伤，无功而返。` : `${L ? L.name : '探索队'}自${S.name}归来：金 +${res.gold}${res.item ? '，得宝物' : ''}${res.clue ? '，得藏宝图一张' : ''}。`);
      logEvt('exped', S.id, res.gold, f);
      if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('explore', g.cities[ex.from], L && !L.dead ? L : null, { ok: !res.mishap, sub: 'return', site: S.id, gold: res.gold, item: res.item, clue: !!res.clue, tier: res.mishap ? 'miss' : (res.clue || (res.item && SG.Items && SG.Items.tier && SG.Items.tier(res.item) >= 2)) ? 'great' : 'good' });
    }
  }
  const expeditions = f => (live() ? st().exped.filter(x => x.f === (f === undefined ? G().player : f) && !x.back) : []);

  // ================================================================ 外交动作（§10）==
  const envoyK = envoy => 0.8 + (envoy ? Math.max(envoy.intel, envoy.pol) : 50) / 250;
  const hostageBonus = h => (h ? CFG.hostage.base + Math.max(h.war, h.intel, h.pol) / CFG.hostage.statDiv : 0);
  function giftDelta(f, t, gold, envoy) {
    const g = G(), Gc = CFG.gift;
    let d = Math.min(Gc.cap, Math.round(gold / (Gc.base + Gc.perCity * g.cityCount(t)) * envoyK(envoy) * prestigeK(f)));
    const lg = g.factions[f].eng.lastGift[t];
    if (lg !== undefined && g.monthIndex - lg < Gc.halfMonths) d = Math.round(d / 2);
    return d;
  }
  function chance(kind, f, t, o) {
    o = o || {};
    const g = G();
    if (!live() || f < 0 || t < 0 || !g.factions[t] || !g.factions[t].alive) return 0;
    const r = rel(f, t), env = o.envoy || null, hb = hostageBonus(o.hostage || null);
    switch (kind) {
      case 'gift': return 1;
      case 'ally': { if (r < CFG.rel.allyMin) return 0; const p = SG.Commands.allyChance(f, t, o.gold || 0, env); return Math.min(SG.Mods ? SG.Mods.CAPS.ally : 0.9, p + hb); }
      case 'marriage': { const M2 = CFG.marriage; return clamp(M2.base + r / M2.relDiv + virtueOf(f) / M2.virtueDiv + (g.cityCount(t) <= g.cityCount(f) ? M2.smaller : 0) + Math.min(M2.prestigeMax, prestigeOf(f) / M2.prestigeDiv) + hb, M2.lo, M2.hi); }
      case 'tribute': { const T = CFG.tribute; const X = o.pay || Math.round(facInc(f) * T.share); return clamp(T.base + 2 * X / Math.max(1, facInc(t)) + r / T.relDiv + hb, T.lo, T.hi); }
      case 'demand': { const Dm = CFG.demand; const ratio = power(f) / Math.max(1, power(t)); return clamp(Dm.base + Dm.perRatio * (ratio - 2) + fameOf(f) / Dm.fameDiv + r / Dm.relDiv, Dm.lo, Dm.hi); }
      case 'truce': { const T = CFG.truce; return clamp(T.base + (o.gold || 0) / (T.incomeK * Math.max(1, facInc(t))) + r / T.relDiv + T.troopK * clamp(power(f) / Math.max(1, power(t)) - 1, -0.5, 0.5) + hb, T.lo, T.hi); }
      case 'trade': { const T = CFG.trade; return clamp(T.base + r / T.relDiv + (env ? env.pol : 50) / T.polDiv, T.lo, T.hi); }
      default: return 0;
    }
  }
  // 能否提出（原因 | null）
  function canPropose(kind, f, t, o) {
    o = o || {};
    const g = G();
    if (!sub('diplomacy')) return '外交新约未开启。';
    if (t < 0 || !g.factions[t] || !g.factions[t].alive || t === f) return '无此势力。';
    const r = rel(f, t);
    switch (kind) {
      case 'gift': return (o.gold || 0) < CFG.gift.min && o.item == null ? `礼金至少 ${CFG.gift.min}` : null;
      case 'ally': return g.allied(f, t) ? '已有同盟' : r < CFG.rel.allyMin ? `好感须 ≥ ${CFG.rel.allyMin}` : null;
      case 'marriage': {
        const M2 = CFG.marriage;
        if (pactOf(f, t, 'marriage')) return '已有婚盟';
        if (st().pacts.some(p => p.kind === 'marriage' && ((p.a === f && p.b === t) || (p.a === t && p.b === f)))) return '两家已联过姻';
        if ((g.factions[f].eng.marriages | 0) >= M2.max) return `一局至多联姻 ${M2.max} 次`;
        if (r < M2.minRel && !g.allied(f, t)) return `好感须 ≥ ${M2.minRel} 或已同盟`;
        if (!g.ruler(f) || !g.ruler(t)) return '无君主';
        return null;
      }
      case 'tribute': return nonAggro(f, t) ? '已有互不侵犯之约' : null;
      case 'demand': {
        if (power(f) < CFG.demand.ratio * Math.max(1, power(t))) return `兵力须为对方 ${CFG.demand.ratio} 倍以上`;
        if (neighbors(f).indexOf(t) < 0) return '须与对方接壤';
        if (st().pacts.some(p => p.kind === 'tribute' && p.until > g.monthIndex && p.payer === t && (p.a === f || p.b === f))) return '对方已在纳贡';
        return null;
      }
      case 'truce': return !atWar(f, t) ? '双方并未交战' : nonAggro(f, t) ? '已有停战之约' : null;
      case 'trade': return pactOf(f, t, 'trade') ? '已有通商协定' : r < CFG.trade.minRel ? `好感须 ≥ ${CFG.trade.minRel}` : atWar(f, t) ? '交战中不能通商' : null;
      default: return '无此动作。';
    }
  }
  function payFrom(f, amount) { const cs = G().citiesOf(f).slice().sort((a, b) => b.gold - a.gold || a.id - b.id); return cs[0] && cs[0].gold >= amount ? cs[0] : null; }
  function sendHostage(f, t, h, months) {
    const g = G(), cap = capitalOf(t);
    if (!h || isRuler(h) || h.faction !== f) return false;
    if (gensIn(g.cities[h.city]).length < 2) return false;
    h.away = { kind: 'hostage', until: g.monthIndex + months, home: h.city, ref: t };
    dirtyIdx(); g.autoGovernor(g.cities[h.city]);
    return true;
  }
  function propose(kind, f, t, o) {
    o = o || {};
    if (!live()) return { ok: false, text: '系统未开启。' };
    const g = G();
    const why = canPropose(kind, f, t, o);
    if (why) return { ok: false, text: why };
    const env = o.envoy || null, from = o.city || capitalOf(f);
    const k = rng('propose', kind, f, t, st().seq++);
    const T = g.factions[t];
    let res;
    const envExp = ok => { if (env) routineExp(env, ok ? CFG.exp.envoyOk : CFG.exp.envoyFail); };
    switch (kind) {
      case 'gift': {
        let gold = Math.round(o.gold || 0), text;
        if (o.item != null && SG.Items && typeof SG.Items.giftValue === 'function') {
          const v = Math.round(SG.Items.giftValue(o.item, [g.ruler(f), env].filter(Boolean)) || 0);
          const tr = SG.Items.transfer ? SG.Items.transfer(o.item, t, { from: f }) : { ok: false };
          if (!tr || !tr.ok) return { ok: false, text: (tr && tr.why) || '礼物送不出去。' };
          gold = Math.max(gold, v);
        } else {
          const src = from && from.gold >= gold ? from : payFrom(f, gold);
          if (!src) return { ok: false, text: '金不足。' };
          src.gold -= gold;
          if (capitalOf(t)) capitalOf(t).gold += gold;
        }
        const d = giftDelta(f, t, gold, env);
        addRel(f, t, d);
        g.factions[f].eng.lastGift[t] = g.monthIndex;
        envExp(true);
        if (isPlayer(f)) { cnt(f, 'gifts'); cnt(f, 'spend:gift', gold); }
        text = `${T.name}收下了礼物（好感 +${d}）。`;
        res = { ok: true, text, d };
        break;
      }
      case 'ally': {
        const p = chance('ally', f, t, o);
        const hb = o.hostage ? hostageBonus(o.hostage) : 0;
        const gift = Math.min(o.gold || 0, from ? from.gold : 0);
        if (!hb) {
          // 原样走 v3 的同盟指令（SG.Random，玩家的指令）；结果记录与成功后的好感 / 声望由 onCommand 处理
          const msg = SG.Commands.ally(from, f, t, gift, env);
          res = { ok: g.allied(f, t), text: msg, p };
        } else {
          if (from) from.gold -= gift;
          const ok = k.nextDouble() < p;
          const months = SG.Econ ? SG.Econ.allianceMonths(f, t, env) : B().AllianceMonths;
          if (ok) { g.setAlliance(f, t, g.monthIndex + months); sendHostage(f, t, o.hostage, months); }
          // 结果记录走 'ally'（onCommand 记好感 +10、声望 +5、使者经验，与 v3 的同盟指令同一处）
          if (SG.Commands && typeof SG.Commands.record === 'function' && from) SG.Commands.record('ally', from, env, { ok, target: t, p, gift, months: ok ? months : 0, hostage: o.hostage.id });
          else { if (ok) onAlly(f, t); envExp(ok); }
          res = { ok, text: ok ? `${T.name}同意结盟。` : `${T.name}拒绝了结盟。`, p };
        }
        break;
      }
      case 'marriage': {
        const M2 = CFG.marriage, cost = M2.cost + M2.costPerCity * g.cityCount(f);
        const src = from && from.gold >= cost ? from : payFrom(f, cost);
        if (!src) return { ok: false, text: `聘礼不足（需 ${cost} 金）。` };
        const p = chance('marriage', f, t, o);
        src.gold -= cost;
        if (isPlayer(f)) cnt(f, 'spend:marriage', cost);
        const ok = k.nextDouble() < p;
        envExp(ok);
        if (ok) {
          const pact = addPact(f, t, 'marriage', M2.months, o.hostage && sendHostage(f, t, o.hostage, M2.months) ? { hostage: o.hostage.id } : null);
          g.setAlliance(f, t, Math.max(g.alliance[f * g.factions.length + t] || 0, g.monthIndex + M2.months));
          addRel(f, t, CFG.rel.marriage);
          addPrestige(f, CFG.prestige.marriage); addPrestige(t, CFG.prestige.marriage);
          addVirtueEv(f, M2.virtueEv);
          g.factions[f].eng.marriages = (g.factions[f].eng.marriages | 0) + 1; g.factions[t].eng.marriages = (g.factions[t].eng.marriages | 0) + 1;
          cnt(f, 'marriages');
          logEvt('marriage', t, null, f);
          res = { ok: true, text: `${T.name}应允婚事，两家结为秦晋之好。`, pact, p };
        } else { addRel(f, t, Math.round(giftDelta(f, t, cost, env) / 2)); res = { ok: false, text: `${T.name}婉拒了婚事。`, p }; }
        break;
      }
      case 'tribute': {
        const Tc = CFG.tribute;
        const pay = Math.max(5, Math.round(o.pay || facInc(f) * Tc.share));
        const p = chance('tribute', f, t, Object.assign({}, o, { pay }));
        const ok = k.nextDouble() < p;
        envExp(ok);
        if (ok) { const pact = addPact(f, t, 'tribute', Tc.months, Object.assign({ payer: f, pay }, o.hostage && sendHostage(f, t, o.hostage, Tc.months) ? { hostage: o.hostage.id } : {})); addRel(f, t, CFG.rel.tribute); res = { ok: true, text: `${T.name}接受了岁贡，答应不犯我境。`, pact, p }; }
        else res = { ok: false, text: `${T.name}不屑于此。`, p };
        break;
      }
      case 'demand': {
        const p = chance('demand', f, t, o);
        const ok = k.nextDouble() < p;
        envExp(ok);
        if (ok) { const pact = addPact(f, t, 'tribute', CFG.tribute.months, { payer: t, pay: Math.max(5, Math.round(facInc(t) * CFG.tribute.share)) }); res = { ok: true, text: `${T.name}慑于兵威，答应岁岁纳贡。`, pact, p }; }
        else { addRel(f, t, CFG.demand.refuseRel); res = { ok: false, text: `${T.name}断然拒绝。`, p }; }
        break;
      }
      case 'truce': {
        const gold = Math.round(o.gold || 0);
        const src = gold > 0 ? (from && from.gold >= gold ? from : payFrom(f, gold)) : from;
        if (gold > 0 && !src) return { ok: false, text: '金不足。' };
        const p = chance('truce', f, t, o);
        if (gold > 0) { src.gold -= gold; const cap = capitalOf(t); if (cap) cap.gold += gold; if (isPlayer(f)) cnt(f, 'spend:truce', gold); }
        const ok = k.nextDouble() < p;
        envExp(ok);
        if (ok) {
          const pact = addPact(f, t, 'truce', CFG.truce.months, o.hostage && sendHostage(f, t, o.hostage, CFG.truce.months) ? { hostage: o.hostage.id } : null);
          addRel(f, t, CFG.rel.truce); addPrestige(f, CFG.prestige.truce); cnt(f, 'truces');
          res = { ok: true, text: `${T.name}同意罢兵休战。`, pact, p };
        } else { if (gold > 0) addRel(f, t, Math.round(giftDelta(f, t, gold, env) / 2)); res = { ok: false, text: `${T.name}不肯罢兵。`, p }; }
        break;
      }
      case 'trade': {
        const p = chance('trade', f, t, o);
        const ok = k.nextDouble() < p;
        envExp(ok);
        if (ok) { const pact = addPact(f, t, 'trade', CFG.trade.months); addRel(f, t, CFG.rel.trade); cnt(f, 'tradePacts'); res = { ok: true, text: `与${T.name}订立通商协定。`, pact, p }; }
        else res = { ok: false, text: `${T.name}无意通商。`, p };
        break;
      }
    }
    if (kind !== 'ally') {
      const stingKind = { gift: 'gift', marriage: 'marriage', tribute: 'tribute', demand: 'tribute', truce: 'truce', trade: 'tradepact', ally: 'diplomacy' }[kind];
      if (SG.Commands && typeof SG.Commands.record === 'function' && from) SG.Commands.record(stingKind, from, env, { ok: !!res.ok, sub: kind, target: t, p: res.p === undefined ? null : res.p, tier: res.ok ? (kind === 'marriage' ? 'great' : 'good') : 'miss' });
    }
    return res;
  }
  function onAlly(f, t) { addRel(f, t, CFG.rel.ally); addPrestige(f, CFG.prestige.alliance); }
  // 背约（§10.10）：玩家出征盟约伙伴的代价（出征确认卡用）
  function betrayalCost(f, t) {
    if (!live() || t < 0) return null;
    const p = pactOf(f, t);
    if (!p || !NONAGGRO.has(p.kind)) return null;
    const m = p.kind === 'marriage';
    return { kind: p.kind, prestige: m ? CFG.prestige.betrayMarriage : CFG.prestige.betray, virtue: -(m ? CFG.betray.marriageVirtue : CFG.betray.otherVirtue), rel: m ? -CFG.betray.marriageRel : 0,
      text: `背约：${{ marriage: '婚盟', tribute: '纳贡之约', truce: '停战之约' }[p.kind]}作废，德 ${m ? CFG.betray.marriageVirtue : CFG.betray.otherVirtue}、声望 −${m ? CFG.prestige.betrayMarriage : CFG.prestige.betray}` + (m ? `、好感 ${CFG.betray.marriageRel}` : '') + (p.hostage !== undefined && p.payer !== t ? '，质子将为对方所拘' : '') };
  }
  function betray(f, t) {
    const g = G();
    for (const p of st().pacts) {
      if (p.until <= g.monthIndex || !NONAGGRO.has(p.kind) || !((p.a === f && p.b === t) || (p.a === t && p.b === f))) continue;
      p.until = g.monthIndex; p.broken = f;
      const m = p.kind === 'marriage';
      addVirtueEv(f, m ? CFG.betray.marriageVirtue : CFG.betray.otherVirtue);
      addPrestige(f, -(m ? CFG.prestige.betrayMarriage : CFG.prestige.betray));
      if (m) { addRel(f, t, CFG.betray.marriageRel); for (let h = 0; h < g.factions.length; h++) if (h !== f && h !== t && g.factions[h].alive && g.allied(h, t)) addRel(f, h, CFG.betray.alliesRel); }
      // 背约者的质子落入对方之手
      if (p.hostage !== undefined) { const x = g.generals[p.hostage]; if (x && x.away && x.away.kind === 'hostage' && x.faction === f) hostageCaptured(x, t); }
      if (isPlayer(f)) { news(`背弃与${g.factions[t].name}的盟约，天下侧目。`); logEvt('betray', t, null, f); }
    }
  }
  function hostageCaptured(x, t) {
    const g = G();
    delete x.away; dirtyIdx();
    const k = rng('hostage', x.id);
    if (k.nextDouble() < 0.5) { const cap = capitalOf(x.faction); if (cap) x.city = cap.id; return; }   // 放还
    if (k.nextDouble() < 0.5) { x.dead = true; x.troops = 0; const of = x.faction; x.faction = -1; dirtyIdx(); g.addLog(x.name + '作为质子被处斩。'); for (const c of g.cities) if (c.governor === x.id) g.autoGovernor(c); emit('captive.executed', x, { faction: of, wasRuler: false }); }
    else { x.faction = t; const cap = capitalOf(t); if (cap) x.city = cap.id; x.loyalty = 50; x.troops = 0; dirtyIdx(); }
  }

  // ================================================================ 节庆（§5.3）==
  function festivalOf(f) {
    const d = D(), F = G().factions[f];
    const fam = FAMILY_OF(F.culture || 'han');
    const base = d.festivals[fam]; if (!base) return null;
    const o = base.overrides && base.overrides[F.key];
    return o ? Object.assign({ fam }, base, o) : Object.assign({ fam }, base);
  }
  function festivalCost(f) { const R = D().festivalRule, n = G().cityCount(f); return Math.min(R.grand.cost.cap, R.grand.cost.base + R.grand.cost.perCity * n); }
  function festivalRec(f) {
    const cap = capitalOf(f); if (!cap) return 'skip';
    const cost = festivalCost(f), A = SG.AIBudget;
    const resv = A ? A.reserve(cap).gold : 150;
    if (cap.gold >= cost * CFG.festival.aiGrandK) return 'grand';
    if (cap.gold - Math.round(cost / 3) >= resv) return 'modest';
    return 'skip';
  }
  function holdFestival(f, choice, o) {
    o = o || {};
    const g = G(), R = D().festivalRule, cap = capitalOf(f);
    if (!cap || choice === 'skip') { Engage.stats.festivals.skip++; return { ok: true, choice: 'skip' }; }
    const cost = choice === 'grand' ? festivalCost(f) : Math.round(festivalCost(f) * R.modest.costK);
    if (o.ai) { if (!SG.AIBudget || !SG.AIBudget.canSpend(cap, cost, 'festival')) { Engage.stats.festivals.skip++; return { ok: false, choice: 'skip' }; } SG.AIBudget.spend(cap, cost, 'festival'); }
    else { if (cap.gold < cost) return { ok: false, why: `金不足（需 ${cost}）` }; cap.gold -= cost; if (isPlayer(f)) cnt(f, 'spend:festival', cost); }
    const grand = choice === 'grand';
    const tK = (facHas(f, 'temple') || facHas(f, 'wonder')) && sub('projects') ? CFG.festival.templeK : 1;
    for (const c of g.citiesOf(f)) c.order = clamp(Math.round(c.order + (grand ? R.grand.order : R.modest.order) * (hasProject(c, 'temple') ? CFG.festival.templeK : 1)), 0, 100);
    if (grand) {
      for (const x of gensIn(cap)) if (!isRuler(x)) x.loyalty = Math.min(100, x.loyalty + R.grand.capitalLoyalty);
      if (o.touhu !== undefined) { const add = Math.min(CFG.festival.touhuMax, Math.max(0, Math.round(o.touhu / 4))); const xs = gensIn(cap).filter(x => !isRuler(x)).sort((a, b) => a.loyalty - b.loyalty || a.id - b.id).slice(0, 3); for (const x of xs) x.loyalty = Math.min(100, x.loyalty + add); }
      cnt(f, 'grandFestivals');
    }
    addPrestige(f, Math.round((grand ? R.grand.prestige : R.modest.prestige) * tK));
    Engage.stats.festivals[grand ? 'grand' : 'modest']++;
    if (isPlayer(f)) {
      const fe = festivalOf(f);
      news(`${fe ? fe.name : '节庆'}${grand ? '盛大举行' : '从简举行'}，民心${grand ? '大' : ''}悦。`);
      logEvt('festival', choice, cost, f);
      if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('festival', cap, g.ruler(f), { ok: true, sub: choice, cost, fam: fe ? fe.fam : 'hua', tier: grand ? 'great' : 'good' });
    }
    return { ok: true, choice, cost };
  }
  function aiFestival(f) {
    if (!sub('festivals')) return;
    const g = G(), fe = festivalOf(f);
    if (!fe || fe.month !== g.month) return;
    const fx = g.factions[f].eng;
    if (fx.festYear === g.year) return;
    fx.festYear = g.year;
    holdFestival(f, festivalRec(f), { ai: true });
  }
  function festivalCard(f) {
    const g = G(), fe = festivalOf(f); if (!fe) return;
    const fx = g.factions[f].eng;
    const cost = festivalCost(f), R = D().festivalRule;
    const rec = festivalRec(f);
    const fyear = Math.floor(g.monthIndex / 12);   // 下个月（节庆月）的年份：月序 = 年 × 12 + 月，下个月 = 月序 + 1
    if (fx.festivalAuto) { fx.festYear = fyear; holdFestival(f, fx.festivalAuto); news(`${fe.name}照此办理（${{ grand: '盛大', modest: '从简', skip: '免' }[fx.festivalAuto]}）。`); return; }
    const capG = (capitalOf(f) || { gold: 0 }).gold;
    const card = { uid: st().seq++, kind: 'festival', slot: 'scheduled', f, city: (capitalOf(f) || {}).id, due: g.monthIndex + 1, made: g.monthIndex, fyear, name: fe.name, icon: 'lantern', text: fe.text, grade: fe.grade, fam: fe.fam,
      foot: fe.grade === 'C' ? '设计补足' : fe.grade === 'B' ? '同一文化、他时他地之俗' : null,
      options: [
        { id: 'grand', label: '盛大', cost: { gold: cost, food: 0 }, forecast: `各城民心 +${R.grand.order}、都城诸将忠诚 +${R.grand.capitalLoyalty}、声望 +${R.grand.prestige}`, rec: rec === 'grand', ok: capG >= cost, miniGame: 'touhu' },
        { id: 'modest', label: '从简', cost: { gold: Math.round(cost * R.modest.costK), food: 0 }, forecast: `民心 +${R.modest.order}、声望 +${R.modest.prestige}`, rec: rec === 'modest', ok: capG >= Math.round(cost * R.modest.costK) },
        { id: 'skip', label: '免', cost: { gold: 0, food: 0 }, forecast: '无', rec: rec === 'skip', ok: true, safe: true },
      ] };
    queueCard(card);
  }

  // ================================================================ 文会（§13）==
  function wenhuiEligible(f) { const g = G(), W = CFG.wenhui; return g.generalsOf(f).filter(x => (x.intel >= W.min || x.pol >= W.min) && !absent(x)).sort((a, b) => (b.intel + b.pol) - (a.intel + a.pol) || a.id - b.id); }
  function wenhuiEntrants(f) { const g = G(); f = f === undefined ? g.player : f; const s = st(); const pick0 = s.wenhuiPick && s.wenhuiPick.year === wenhuiYear() && s.wenhuiPick.f === f ? s.wenhuiPick.ids.map(i => g.generals[i]).filter(x => x && present(x, f)) : null; return pick0 || wenhuiEligible(f).slice(0, CFG.wenhui.n); }
  function setWenhuiEntrants(f, ids) { const s = st(); const ok = new Set(wenhuiEligible(f).map(x => x.id)); s.wenhuiPick = { year: wenhuiYear(), f, ids: (ids || []).filter(i => ok.has(i)).slice(0, CFG.wenhui.n) }; return s.wenhuiPick.ids; }
  const wenhuiYear = () => { const g = G(); return g.month <= CFG.wenhui.month ? g.year : g.year + 1; };
  function wenhuiHost() {
    const g = G();
    const alive = g.factions.filter(F => F.alive);
    const withAcad = alive.filter(F => facHas(F.id, 'academy') || facHas(F.id, 'wonder'));
    const pool = withAcad.length ? withAcad : alive;
    return pool.slice().sort((a, b) => prestigeOf(b.id) - prestigeOf(a.id) || a.id - b.id)[0] || null;
  }
  function wenhuiName(host) {
    if (!host) return '文会';
    const g = G(), fam = famOfFac(host.id);
    if (fam === 'hua') { const ly = g.cityByKey('luoyang'); return ly && ly.owner === host.id ? '白虎观论经' : '鸿都文会'; }
    return { roman: '卡皮托利努斯竞赛', indic: '论议大会', persia: '麻葛论道' }[fam] || '文会';
  }
  function runWenhui(o) {
    o = o || {};
    if (!sub('wenhui')) return null;
    const g = G(), s = st(), W = CFG.wenhui;
    const year = o.year || g.year;
    if (s.wenhui && s.wenhui.year === year) return s.wenhui;
    const k = rngAt(year * 12 + W.month, 'wenhui');
    const host = wenhuiHost();
    const ents = [];
    for (const F of g.factions) {
      if (!F.alive) continue;
      const gens = F.id === g.player && o.playerIds ? o.playerIds.map(i => g.generals[i]).filter(x => x && present(x, F.id)) : F.id === g.player ? wenhuiEntrants(F.id) : wenhuiEligible(F.id).slice(0, W.n);
      if (F.id === g.player && o.skipPlayer) continue;
      const acad = facHas(F.id, 'academy') && sub('projects') ? W.academy : 0;
      for (const x of gens) {
        const lv = x.level | 0 || 1;
        const s1 = 0.5 * x.pol + 0.5 * x.intel + 2 * lv + acad + k.nextDouble() * 20;
        const s2 = 0.7 * x.intel + 0.3 * x.pol + 2 * lv + acad + k.nextDouble() * 30;
        const deb = F.id === g.player ? clamp(o.debate !== undefined ? o.debate : W.autoDebate, -W.debateMax, W.debateMax) : 0;
        const s3 = 0.6 * x.intel + 0.4 * x.pol + 2 * lv + acad + k.nextDouble() * 25 + deb;
        ents.push({ gen: x.id, f: F.id, s: Math.round((s1 + s2 + s3) * 10) / 10, r: [Math.round(s1), Math.round(s2), Math.round(s3)], lv });
      }
    }
    ents.sort((a, b) => b.s - a.s || b.lv - a.lv || a.gen - b.gen);
    for (const e of ents) storyExp(g.generals[e.gen], W.entrantExp);
    const top = ents.slice(0, 3);
    top.forEach((e, i) => {
      const P = W.prizes[i], cap = capitalOf(e.f), x = g.generals[e.gen];
      if (cap) cap.gold += P.gold;
      if (isPlayer(e.f)) cnt(e.f, 'prizeGold', P.gold);
      addPrestige(e.f, P.prestige); storyExp(x, P.exp);
      if (P.fame) addFameEv(e.f, P.fame);
      const cult = P.item === 'rare' ? x.culture || g.factions[e.f].culture : g.factions[e.f].culture;
      e.item = grantItem(e.f, P.item, 'wenhui', { culture: cult, types: W.types, k, gen: x, fallbackGold: W.rareFallbackGold });
      cnt(e.f, 'wenhuiPlace' + (i + 1)); if (i === 0) cnt(e.f, 'wenhuiWin');
    });
    if (host) { addPrestige(host.id, CFG.prestige.wenhuiHost); const cap = capitalOf(host.id); if (cap) cap.order = Math.min(100, cap.order + W.hostOrder); }
    s.wenhui = { year, host: host ? host.id : -1, name: wenhuiName(host), city: host && capitalOf(host.id) ? capitalOf(host.id).id : -1, top: top.map(e => ({ gen: e.gen, f: e.f, s: e.s, r: e.r, item: e.item || null })), n: ents.length,
      mine: ents.filter(e => isPlayer(e.f)).map(e => ({ gen: e.gen, s: e.s, r: e.r, place: ents.indexOf(e) + 1 })) };
    Engage.stats.wenhui++;
    const mine = top.findIndex(e => isPlayer(e.f));
    if (g.player >= 0) news(`${s.wenhui.name}落幕：魁首${g.generals[top[0] ? top[0].gen : -1] ? g.generals[top[0].gen].name : '—'}` + (mine >= 0 ? `，我方名列第 ${mine + 1}。` : '。'));
    if (mine >= 0) { logEvt('wenhui', mine + 1, null, g.player); if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('wenhui', capitalOf(g.player), g.generals[top[mine].gen], { ok: true, place: mine + 1, tier: mine === 0 ? 'great' : 'good' }); }
    return s.wenhui;
  }
  function wenhuiCard(f) {
    const g = G();
    const ents = wenhuiEntrants(f);
    const host = wenhuiHost();
    const card = { uid: st().seq++, kind: 'wenhui', slot: 'scheduled', f, city: host && capitalOf(host.id) ? capitalOf(host.id).id : -1, due: g.monthIndex + 1, made: g.monthIndex, name: wenhuiName(host), icon: 'book',
      text: `${host ? host.name : '天下'}于${host && capitalOf(host.id) ? capitalOf(host.id).name : '都城'}举办${wenhuiName(host)}，诸国各遣文士三人，比试经义、辞赋与策论。`, entrants: ents.map(x => x.id),
      options: [
        { id: 'send', label: ents.length ? `遣${ents.map(x => x.name).join('、')}赴会` : '无人可遣', cost: { gold: 0, food: 0 }, forecast: `魁首：珍品典籍 + 300 金 + 声望 50`, rec: ents.length > 0, ok: ents.length > 0 },
        { id: 'skip', label: '不赴会', cost: { gold: 0, food: 0 }, forecast: '无', rec: !ents.length, ok: true, safe: true },
      ] };
    queueCard(card);
  }
  // §13.2 论辩：确定性的内核（界面每次交给它一个立场）
  const STANCES = { yin: { zh: '引经', stat: x => x.pol, beats: 'yu' }, yu: { zh: '设喻', stat: x => (x.intel + x.pol) / 2, beats: 'bo' }, bo: { zh: '驳难', stat: x => x.intel, beats: 'yin' } };
  function debate(state, stance) {
    const g = G();
    if (!state.started) {
      const me = g.generals[state.me], rv = g.generals[state.rival];
      return Object.assign({}, state, { started: true, round: 0, pts: [0, 0], meter: 0, boost: false, used: false, log: [], seed: state.seed || hash32(g.seed + '|debate|' + state.me + '|' + state.rival + '|' + g.year), tell: tellFor(0, me, rv, state.seed || hash32(g.seed + '|debate|' + state.me + '|' + state.rival + '|' + g.year)) });
    }
    if (state.round >= 5 || !STANCES[stance]) return state;
    const me = g.generals[state.me], rv = g.generals[state.rival];
    const k = SG.SeededRandom(state.seed + state.round * 7919);
    const rs = rivalStance(rv, k);
    const pm = STANCES[stance].stat(me), pr = STANCES[rs].stat(rv);
    let a = 0, b = 0, won = 0;
    if (stance === rs) { if (pm >= pr) { a = 6; b = 3; } else { a = 3; b = 6; } }
    else if (STANCES[stance].beats === rs) { a = Math.max(4, Math.round(10 + (pm - pr) / 10)); won = 1; }
    else { b = Math.max(4, Math.round(10 + (pr - pm) / 10)); won = -1; }
    if (state.boost && won === 1) a *= 2;
    const used = state.used || (state.boost && won === 1);
    const meter = won === 1 ? state.meter + 1 : state.meter;
    const boost = !used && (state.boost ? won !== 1 && state.boost : meter >= 2);
    const pts = [state.pts[0] + a, state.pts[1] + b];
    const round = state.round + 1;
    const log = state.log.concat([{ round, mine: stance, rival: rs, a, b, won }]);
    const next = Object.assign({}, state, { round, pts, meter, boost, used, log, tell: round < 5 ? tellFor(round, me, rv, state.seed) : null });
    if (round >= 5) next.bonus = clamp(Math.round((pts[0] - pts[1]) / 3), -CFG.wenhui.debateMax, CFG.wenhui.debateMax);
    return next;
  }
  function rivalStance(rv, k) {
    const w = Object.keys(STANCES).map(s => Math.pow(STANCES[s].stat(rv), 2) + 400);
    let r = k.nextDouble() * sum(w);
    const keys = Object.keys(STANCES);
    for (let i = 0; i < keys.length; i++) { r -= w[i]; if (r < 0) return keys[i]; }
    return keys[keys.length - 1];
  }
  const TELLS = { yin: '对方抚须，似欲引《春秋》', yu: '对方举目四顾，似要设喻', bo: '对方冷笑，似欲驳难' };
  function tellFor(round, me, rv, seed) {
    const k = SG.SeededRandom(seed + round * 7919);
    const rs = rivalStance(rv, k);
    const t = SG.SeededRandom(seed ^ (round * 104729 + 17));
    const truthful = t.nextDouble() < 0.6;
    const keys = Object.keys(STANCES);
    const shown = truthful ? rs : keys[(keys.indexOf(rs) + 1 + Math.floor(t.nextDouble() * 2)) % 3];
    return { text: TELLS[shown], stance: shown };
  }
  // §13.3 投壶：每支箭 → 0 / 1 依竿 / 2 中壶 / 3 贯耳（angle、power 为 0…1 的误差）
  function touhuScore(throws) {
    let s = 0;
    for (const t of throws || []) { const e = Math.hypot(t.angle || 0, t.power || 0); s += e < 0.06 ? 3 : e < 0.14 ? 2 : e < 0.22 ? 1 : 0; }
    return Math.min(9, s);
  }
  const touhuLoyalty = score => (score >= 7 ? 5 : score >= 5 ? 4 : score >= 3 ? 3 : 2);

  // ================================================================ 武林大会的钩子（§13.5；规则在 TOURNAMENT）==
  function tourneyResult(r) {
    if (!live() || !r || !r.placings) return;
    const g = G();
    const P = CFG.prestige.tourney;
    for (const p of r.placings) {
      if (p.f === undefined || p.f < 0 || !(p.place >= 1 && p.place <= 3)) continue;
      addPrestige(p.f, P[p.place - 1]);
      cnt(p.f, 'tourneyPlace' + p.place);
      if (p.place === 1) cnt(p.f, 'tourneyWin');
      if (isPlayer(p.f)) logEvt('tourney', p.place, p.gen, p.f);
    }
    if (r.host >= 0 && g.factions[r.host]) { addPrestige(r.host, CFG.prestige.tourneyHost); const cap = capitalOf(r.host); if (cap) cap.order = Math.min(100, cap.order + CFG.wenhui.hostOrder); }
    const fin = r.placings.filter(p => p.place <= 2).map(p => g.generals[p.gen]).filter(Boolean);
    if (fin.length === 2) {
      const [a, b] = fin;
      if (a.faction !== b.faction && a.war >= 85 && b.war >= 85 && rngAt(g.monthIndex, 'bond', 'tourney', a.id, b.id).nextDouble() < 0.3) { if (!bondKind(a, b)) setKind(a, b, 'rival'); }
      else if (a.faction === b.faction) addAff(a, b, 10);
    }
  }
  function tourneyPicks(f) { if (!live()) return []; const g = G(), gf = st().gflags; return Object.keys(gf).map(Number).filter(id => gf[id].tourneyPick > g.monthIndex && g.generals[id] && g.generals[id].faction === f); }

  // ================================================================ 归附与电脑的外交（§10.5、§10.8、§10.9）==
  function submissions(mo) {
    const g = G(), S = CFG.submit;
    for (const H of g.factions) {
      if (!H.alive || H.id === g.player || countM(H.id) > S.maxCities) continue;
      const hp = power(H.id) + 1;
      let best = null, bp = 0;
      for (const F of neighbors(H.id)) {
        if (F !== g.player && !Engage.aiSubmit) continue;
        const fe = g.factions[F].eng;
        if (g.monthIndex - (fe.lastSubmit === undefined ? -999 : fe.lastSubmit) < S.digest) continue;
        if (st().cards.some(c => !c.done && c.kind === 'submit' && c.from === H.id)) continue;
        const ratio = (power(F) + 1) / hp;
        const pact = pactOf(F, H.id, 'marriage') || st().pacts.find(q => q.kind === 'tribute' && q.payer === H.id && (q.a === F || q.b === F) && q.until > g.monthIndex && g.monthIndex - q.since >= S.tributeMonths);
        const r = rel(F, H.id);
        if (ratio < S.ratio || virtueOf(F) < S.virtue || (r < S.rel && !pact)) continue;
        const p = clamp(S.base + Math.max(0, r - S.rel) / S.relDiv + S.ratioK * Math.min(S.ratioMax, ratio - S.ratio) + virtueOf(F) / S.virtueDiv + prestigeOf(F) / S.prestigeDiv, 0, S.pMax);
        if (p > bp) { bp = p; best = F; }
      }
      if (best === null || rngAt(g.monthIndex + 1, 'submit', H.id).nextDouble() >= bp) continue;
      if (best === g.player) {
        let due = g.monthIndex + 1;
        if ((due - 1) % 12 + 1 === 1) due++;   // INTEGRATION §6.5：12 月生成的归附排到 2 月
        const ru = g.ruler(H.id);
        const card = { uid: st().seq++, kind: 'submit', slot: 'scheduled', f: g.player, from: H.id, city: (capitalOf(H.id) || {}).id, due, made: g.monthIndex, name: '归附', icon: 'submit',
          text: `${ru ? ru.name : H.name}愿以${g.citiesOf(H.id).map(c => c.name).join('、')}相让，举州归附。`,
          options: [{ id: 'accept', label: '纳其土地', cost: { gold: 0, food: 0 }, forecast: `得城 ${g.cityCount(H.id)} 座、武将 ${g.generalsOf(H.id).length} 人、声望 +${CFG.prestige.submit}`, rec: true, ok: true },
            { id: 'decline', label: '婉言谢绝', cost: { gold: 0, food: 0 }, forecast: `好感 +${S.declineRel}`, rec: false, ok: true, safe: true }] };
        Engage.stats.submit++;
        queueCard(card);
      } else absorb(best, H.id);
    }
  }
  function absorb(F, H) {
    const g = G(), S = CFG.submit;
    if (!g.factions[H].alive || !g.factions[F].alive) return false;
    const rulerH = g.ruler(H);
    const HN = g.factions[H].name;
    for (const c of g.citiesOf(H)) { c.owner = F; c.order = Math.min(100, c.order + CFG.order.absorbAdd); }
    for (const x of g.generalsOf(H)) { x.faction = F; x.loyalty = clamp(S.loyalty + Math.round(virtueOf(F) / S.loyaltyVirtueDiv) + (x === rulerH ? S.rulerAdd : 0), S.loLo, S.loHi); }
    dirtyIdx();
    for (const p of st().pacts) if (p.until > g.monthIndex && (p.a === H || p.b === H)) p.until = g.monthIndex;
    g.checkFactionDeath(H);
    for (const c of g.citiesOf(F)) g.autoGovernor(c);
    addPrestige(F, CFG.prestige.submit); cnt(F, 'submissions'); g.factions[F].eng.lastSubmit = g.monthIndex;
    g.addLog(HN + '举州归附' + g.factions[F].name + '。');
    if (isPlayer(F)) { news(`${HN}举州归附。`); logEvt('submit', H, null, F); if (SG.Commands && typeof SG.Commands.record === 'function') SG.Commands.record('submit', capitalOf(F), rulerH, { ok: true, from: H, tier: 'great' }); }
    return true;
  }
  function aiDiplomacy() {
    const g = G(), A = CFG.aiTribute;
    for (const F of g.factions) {
      if (!F.alive || F.id === g.player) continue;
      const k = rngAt(g.monthIndex + 1, 'aidip', F.id);
      const myP = power(F.id) + 1;
      for (const h of neighbors(F.id)) {
        if (h === g.player) continue;
        if (nonAggro(F.id, h) || g.allied(F.id, h)) continue;
        const hp = power(h);
        if (Engage.aiTribute && hp >= A.ratio * myP && rel(F.id, h) >= A.minRel && !atWar(F.id, h) && k.nextDouble() < A.p) {
          const pAcc = clamp(A.acceptBase + rel(F.id, h) / A.acceptRelDiv, A.acceptLo, A.acceptHi);
          if (k.nextDouble() < pAcc) { addPact(F.id, h, 'tribute', CFG.tribute.months, { payer: F.id, pay: Math.max(5, Math.round(facInc(F.id) * CFG.tribute.share)) }); addRel(F.id, h, CFG.rel.tribute); Engage.stats.aiTribute++; if (g.player >= 0 && (neighbors(g.player).indexOf(h) >= 0 || neighbors(g.player).indexOf(F.id) >= 0)) news(`${F.name}向${g.factions[h].name}纳贡。`); }
          break;
        }
        if (Engage.aiTribute && rel(F.id, h) >= CFG.aiMarriage.rel && !pactOf(F.id, h, 'marriage') && (F.eng.marriages | 0) < CFG.marriage.max && k.nextDouble() < CFG.aiMarriage.p) {
          addPact(F.id, h, 'marriage', CFG.marriage.months); addRel(F.id, h, CFG.rel.marriage); F.eng.marriages = (F.eng.marriages | 0) + 1; g.factions[h].eng.marriages = (g.factions[h].eng.marriages | 0) + 1; Engage.stats.aiMarriage++;
          break;
        }
      }
    }
  }
  function aiTowardPlayer() {
    const g = G(), f = g.player; if (f < 0 || !g.factions[f].alive) return;
    const Dm = CFG.aiDemand, O = CFG.aiOffers;
    let due = g.monthIndex + 1;
    const janNext = (due - 1) % 12 + 1 === 1;
    for (const h of neighbors(f)) {
      const k = rngAt(g.monthIndex + 1, 'aitp', h);
      const e = g.factions[h].eng;
      if (st().cards.some(c => !c.done && c.from === h)) continue;
      if (power(h) >= Dm.ratio * (power(f) + 1) && rel(h, f) <= Dm.maxRel && !nonAggro(f, h) && !g.allied(f, h) && (e.lastDemand === undefined ? -99 : e.lastDemand) + Dm.everyMonths <= g.monthIndex && k.nextDouble() < Dm.p) {
        e.lastDemand = g.monthIndex; Engage.stats.demand++;
        const pay = Math.max(5, Math.round(facInc(f) * Dm.share));
        const thr = threatFrom(f, h) > g.troopsIn(capitalOf(f) || g.citiesOf(f)[0]) * 0.8;
        const card = { uid: st().seq++, kind: 'demand', slot: 'scheduled', f, from: h, city: (capitalOf(f) || {}).id, due: janNext ? due + 1 : due, made: g.monthIndex, name: '索贡', icon: 'tribute', pay,
          text: `${g.ruler(h).name}遣使来书：「岁贡金 ${pay}，否则兵戎相见。」`,
          options: [{ id: 'accept', label: '忍辱纳贡', cost: { gold: pay, food: 0 }, forecast: `每月金 −${pay}，12 个月互不侵犯`, rec: thr, ok: true },
            { id: 'refuse', label: '严词拒绝', cost: { gold: 0, food: 0 }, forecast: `好感 ${Dm.refuseRel}，对方一年内更易来犯`, rec: !thr, ok: true, safe: true }] };
        queueCard(card);
        continue;
      }
      if (!sub('diplomacy')) continue;
      for (const kind of ['ally', 'marriage', 'trade']) {
        const q = O[kind];
        if (rel(h, f) < q.rel) continue;
        if (kind === 'ally' && g.allied(f, h)) continue;
        if (kind === 'marriage' && (pactOf(f, h, 'marriage') || canPropose('marriage', h, f))) continue;
        if (kind === 'trade' && (pactOf(f, h, 'trade') || atWar(f, h))) continue;
        if (k.nextDouble() >= q.p) continue;
        Engage.stats.offers++;
        const T = g.factions[h];
        const card = { uid: st().seq++, kind: 'offer', sub: kind, slot: 'scheduled', f, from: h, city: (capitalOf(f) || {}).id, due, made: g.monthIndex, name: { ally: '结盟之请', marriage: '联姻之请', trade: '通商之请' }[kind], icon: { ally: 'diplomacy', marriage: 'marriage', trade: 'tradepact' }[kind],
          text: `${g.ruler(h).name}遣使至${(capitalOf(f) || {}).name || ''}，愿${{ ally: '与我结为同盟', marriage: '与我家联姻', trade: '与我订立通商之约' }[kind]}。`,
          options: [{ id: 'accept', label: '欣然应允', cost: { gold: 0, food: 0 }, forecast: { ally: `同盟 ${B().AllianceMonths} 个月`, marriage: `婚盟 ${CFG.marriage.months} 个月、声望 +${CFG.prestige.marriage}`, trade: `通商 ${CFG.trade.months} 个月` }[kind], rec: true, ok: true },
            { id: 'decline', label: '婉言谢绝', cost: { gold: 0, food: 0 }, forecast: '无', rec: false, ok: true, safe: true }] };
        queueCard(card);
        break;
      }
    }
  }
  function threatFrom(f, h) { const g = G(); let t = 0; for (const c of g.citiesOf(f)) for (const j of c.links) if (g.cities[j].owner === h) t += g.troopsIn(g.cities[j]); return t; }
  function resolveScheduled(card, id, opts) {
    const g = G(), f = card.f;
    switch (card.kind) {
      case 'festival': {
        if (opts.remember && id) g.factions[f].eng.festivalAuto = id;
        g.factions[f].eng.festYear = card.fyear;
        const r = holdFestival(f, id, { touhu: opts.touhu });
        if (!r.ok && id !== 'skip') return holdFestival(f, 'skip');
        return r;
      }
      case 'wenhui': {
        if (id === 'skip') { st().wenhuiSkip = wenhuiYear(); return { ok: true }; }
        if (opts.ids) setWenhuiEntrants(f, opts.ids);
        if (opts.run) return { ok: true, wenhui: runWenhui({ debate: opts.debate }) };
        return { ok: true };
      }
      case 'demand': {
        const h = card.from;
        if (!g.factions[h] || !g.factions[h].alive) return { ok: false };
        if (id === 'accept') { addPact(f, h, 'tribute', CFG.tribute.months, { payer: f, pay: card.pay }); addRel(f, h, CFG.rel.tribute); cnt(f, 'tributeOut'); return { ok: true }; }
        addRel(f, h, CFG.aiDemand.refuseRel); g.factions[f].eng.refused[h] = g.monthIndex + CFG.aiDemand.refuseMonths;
        return { ok: true };
      }
      case 'submit': {
        const H = card.from;
        if (!g.factions[H] || !g.factions[H].alive) return { ok: false };
        if (id === 'accept') return { ok: absorb(f, H) };
        addRel(f, H, CFG.submit.declineRel);
        return { ok: true };
      }
      case 'offer': {
        const h = card.from;
        if (!g.factions[h] || !g.factions[h].alive || id !== 'accept') return { ok: true };
        if (card.sub === 'ally') { g.setAlliance(f, h, g.monthIndex + B().AllianceMonths); onAlly(f, h); cnt(f, 'aiOffers'); }
        else if (card.sub === 'marriage') {
          addPact(f, h, 'marriage', CFG.marriage.months); g.setAlliance(f, h, g.monthIndex + CFG.marriage.months); addRel(f, h, CFG.rel.marriage);
          addPrestige(f, CFG.prestige.marriage); addPrestige(h, CFG.prestige.marriage); addVirtueEv(f, CFG.marriage.virtueEv); cnt(f, 'marriages');
          g.factions[f].eng.marriages = (g.factions[f].eng.marriages | 0) + 1; g.factions[h].eng.marriages = (g.factions[h].eng.marriages | 0) + 1;
        } else if (card.sub === 'trade') { addPact(f, h, 'trade', CFG.trade.months); addRel(f, h, CFG.rel.trade); }
        return { ok: true };
      }
      default: return { ok: true };
    }
  }

  // ================================================================ 功业（§14）==
  const ACH = () => { const d = D(); if (!d._ach) Object.defineProperty(d, '_ach', { value: Object.fromEntries(d.achievements.map(a => [a.id, a])), enumerable: false }); return d._ach; };
  function itemsOwned(f) { const I = SG.Items; if (!I || I.enabled === false || !G().items || !I.instancesOf) return []; try { return I.instancesOf(f).map(u => { const r = I.inst ? I.inst(u) : null; return r ? r.id : null; }).filter(Boolean); } catch (e) { return []; } }
  function achTest(a, f) {
    const g = G(), cs = g.citiesOf(f);
    const [ck, arg0] = String(a.cond).split(':');
    const n = arg0 === undefined ? 1 : +arg0;
    switch (ck) {
      case 'cityStat999': return cs.some(c => c.land >= 999 || c.industry >= 999 || c.town >= 999);
      case 'cityLevel': return cs.some(c => cityLevel(c) >= n);
      case 'cityPop': return cs.some(c => c.population >= n);
      case 'allOrder': return cs.length > 0 && cs.every(c => c.order >= n);
      case 'cityOrder': return cs.some(c => c.order >= n);
      case 'harvestTotal': return getCnt(f, 'harvestTotal') >= n;
      case 'goldTotal': return getCnt(f, 'goldTotal') >= n;
      case 'pacts': return g.factions.filter(h => h.alive && h.id !== f && (g.allied(f, h.id) || nonAggro(f, h.id) || pactOf(f, h.id, 'trade'))).length >= n;
      case 'tributaries': return st().pacts.filter(p => p.kind === 'tribute' && p.until > g.monthIndex && p.payer !== f && (p.a === f || p.b === f)).length >= n;
      case 'relAll': { const al = g.factions.filter(h => h.alive && h.id !== f); return al.length > 0 && al.every(h => rel(f, h.id) >= n || g.allied(f, h.id) || nonAggro(f, h.id)); }
      case 'farRel': return g.factions.some(h => h.alive && h.id !== f && rel(f, h.id) >= n && facDistance(f, h.id, 12) >= 8);
      case 'caravanFamilies': return (st().counters[f + ':caravanFams'] || '').split(',').filter(Boolean).length >= n;
      case 'event': return getCnt(f, 'event:' + arg0) >= 1;
      case 'itemsOwned': return itemsOwned(f).length >= n;
      case 'rareOwned': case 'epicOwned': { const I = SG.Items; if (!I || !I.tier) return false; const t = ck === 'rareOwned' ? 2 : 3; return itemsOwned(f).filter(id => I.tier(id) >= t).length >= n; }
      case 'codexPct': { const I = SG.Items; if (!I || I.enabled === false || !g.items || !I.collection) return false; try { const c = I.collection(f); return c.total > 0 && c.seen / c.total >= +arg0; } catch (e) { return false; } }
      case 'item': return itemsOwned(f).indexOf(arg0) >= 0;
      case 'gens': return g.generalsOf(f).length >= n;
      case 'level': return g.generalsOf(f).some(x => (x.level | 0) >= n);
      case 'titleTier': { const T = SG.Titles; if (!T || T.enabled === false || !T.curTier) return false; return g.generalsOf(f).some(x => T.curTier(x) >= n); }
      case 'hiddenFound': return getCnt(f, 'hiddenFound') + getCnt(f, 'revealed') >= n;
      case 'cities': return cs.length >= n;
      case 'sitesVisited': return st().visited.length >= n;
      case 'wonder': return getCnt(f, 'wonder') >= 1;
      default: return getCnt(f, ck) >= n;
    }
  }
  function achievementsMonth(f) {
    const s = st(), g = G();
    const got = [];
    for (const a of D().achievements) {
      if (s.ach.indexOf(a.id) >= 0) continue;
      if (a.needs === 'titles' && !(SG.Titles && SG.Titles.enabled !== false)) continue;
      if (!achTest(a, f)) continue;
      s.ach.push(a.id); s.achAt[a.id] = g.monthIndex;
      addPrestige(f, a.prestige); if (a.fame) addFameEv(f, a.fame); if (a.virtue) addVirtueEv(f, a.virtue);
      logEvt('ach', a.id, a.prestige, f);
      got.push(a);
      emit('engage.achievement', a.id);
    }
    if (got.length) news(`功业：${got.map(a => '「' + a.name + '」').join('')}（声望 +${sum(got, a => a.prestige)}）。`);
    return got;
  }
  function achievements(f) { if (!live()) return []; const s = st(); return D().achievements.map(a => ({ id: a.id, name: a.name, cat: a.cat, prestige: a.prestige, fame: a.fame || 0, virtue: a.virtue || 0, done: s.ach.indexOf(a.id) >= 0, month: s.achAt[a.id] === undefined ? null : s.achAt[a.id], needs: a.needs || null })); }

  // ================================================================ 胜利与天下榜（§15）==
  function wangdaoConds(f) {
    const g = G(), cs = g.citiesOf(f), V = CFG.victory, Dd = DEC();
    const lv = cs.map(cityLevel);
    const ok = h => rel(f, h) >= V.friendRel || g.allied(f, h) || pactOf(f, h);
    const ns = neighbors(f), al = g.factions.filter(h => h.alive && h.id !== f);
    return {
      prestige: prestigeOf(f) >= (Dd.wangdaoPrestige || 1500),
      order: cs.length > 0 && avg(cs.map(c => c.order)) >= (Dd.wangdaoOrder || 75),
      prosper: lv.filter(v => v >= 3).length >= V.prosperBig || lv.some(v => v >= 4),
      friends: ns.every(ok) && al.filter(h => ok(h.id)).length >= Math.min(V.friendsMin, al.length) && al.length >= 1,
      peace: g.monthIndex - (g.factions[f].eng.lastAttack === undefined ? -999 : g.factions[f].eng.lastAttack) >= V.peaceMonths,
    };
  }
  function tianmingConds(f) {
    const g = G(), V = CFG.victory;
    const ru = g.ruler(f);
    const seal = itemsOwned(f).indexOf('chuanguoxi') >= 0 && !!(ru && ru.gear && SG.Items && Object.values(ru.gear).some(u => u != null && SG.Items.inst && (SG.Items.inst(u) || {}).id === 'chuanguoxi'));
    const capOk = ['luoyang', 'changan'].some(k => { const c = g.cityByKey(k); return c && c.owner === f; });
    return { seal, capital: capOk, prestige: prestigeOf(f) >= V.tianmingPrestige, fame: fameOf(f) >= V.tianmingFame, scenario: g.scenario === 'classic' };
  }
  function wanguoConds(f) {
    const g = G(), V = CFG.victory;
    const partners = g.factions.filter(h => h.alive && h.id !== f && (g.allied(f, h.id) || pactOf(f, h.id, 'marriage') || pactOf(f, h.id, 'trade') || st().pacts.some(p => p.kind === 'tribute' && p.until > g.monthIndex && p.payer === h.id && (p.a === f || p.b === f))));
    const regions = new Set(partners.map(h => h.region));
    return { pacts: partners.length >= V.wanguoPacts, regions: regions.size >= V.wanguoRegions, profit: getCnt(f, 'tradeProfit') >= V.wanguoProfit, prestige: prestigeOf(f) >= V.wanguoPrestige, scenario: g.scenario === 'world' };
  }
  function victoryMonth(f) {
    const s = st(), g = G();
    const v = s.vlog;
    const run = (key, conds) => {
      const all = Object.values(conds).every(Boolean);
      v[key] = all ? (v[key] || 0) + 1 : 0;
      if (v[key] >= CFG.victory.hold && s.victory[key] === undefined) {
        s.victory[key] = g.monthIndex;
        logEvt('victory', key, null, f);
        news({ wangdao: '王道既成：天下归心，诸侯遣使来朝。', tianming: '天命所归：汉帝禅位。', wanguo: '万国来朝。' }[key]);
        emit('engage.victory', key);
      }
    };
    const wc = wangdaoConds(f);
    for (const k of Object.keys(wc)) if (wc[k] && v['first:' + k] === undefined) v['first:' + k] = g.monthIndex - s.start;
    run('wangdao', wc);
    if (g.scenario === 'classic') run('tianming', tianmingConds(f));
    if (g.scenario === 'world') run('wanguo', wanguoConds(f));
  }
  function victory(f) {
    if (!live()) return null;
    const g = G(); f = f === undefined ? g.player : f;
    const s = st();
    return { wangdao: { conds: wangdaoConds(f), run: s.vlog.wangdao || 0, done: s.victory.wangdao !== undefined ? s.victory.wangdao : null },
      tianming: g.scenario === 'classic' ? { conds: tianmingConds(f), run: s.vlog.tianming || 0, done: s.victory.tianming !== undefined ? s.victory.tianming : null } : null,
      wanguo: g.scenario === 'world' ? { conds: wanguoConds(f), run: s.vlog.wanguo || 0, done: s.victory.wanguo !== undefined ? s.victory.wanguo : null } : null, hold: CFG.victory.hold };
  }
  function score(f) {
    const g = G(), S = CFG.score;
    if (!g.factions[f] || !g.factions[f].alive) return 0;
    const cs = g.citiesOf(f);
    const dev = sum(cs, c => c.land + c.industry + c.town) / S.devDiv;
    const lv = sum(g.generalsOf(f), x => x.level | 0 || 1);
    const pacts = live() ? pactsOf(f).length + g.factions.filter(h => h.alive && h.id !== f && g.alliance[f * g.factions.length + h.id] > g.monthIndex).length : 0;
    return Math.round(S.city * cs.length + dev + prestigeOf(f) / S.prestigeDiv + lv + S.pact * pacts);
  }
  function leaderboard() {
    const g = G();
    const rows = g.factions.filter(f => f.alive).map(f => ({ f: f.id, name: f.name, score: score(f.id), prestige: prestigeOf(f.id), cities: g.cityCount(f.id), title: prestigeName(f.id) }));
    rows.sort((a, b) => b.score - a.score || a.f - b.f);
    rows.forEach((r, i) => { r.rank = i + 1; r.hist = live() ? Object.keys(st().hist).sort().map(y => st().hist[y][r.f] || 0) : []; });
    return rows;
  }
  // 三十年之约（§15.5）：等第
  function scoreGameResult(f) {
    const rows = leaderboard();
    const me = rows.find(r => r.f === f);
    if (!me) return { rank: rows.length + 1, grade: '丙下' };
    const n = rows.length;
    const grade = me.rank === 1 ? '甲上' : me.rank <= 3 ? '甲' : me.rank <= Math.ceil(n / 4) ? '乙' : '丙';
    return { rank: me.rank, n, score: me.score, grade };
  }

  // ================================================================ 年鉴（§16.4）==
  function yearReview(f) {
    if (!live()) return null;
    const g = G(), s = st(); f = f === undefined ? g.player : f;
    const y0 = (g.year - 1) * 12 + 1, y1 = g.year * 12 + 1;
    const logs = s.log.filter(e => e.m >= y0 && e.m < y1 && e.f === f);
    const rankK = { victory: 9, ach: 6, built: 5, marriage: 5, submit: 6, wenhui: 5, tourney: 6, sworn: 4, mission: 3, exped: 2, caravan: 1 };
    const moments = logs.filter(e => rankK[e.kind]).sort((a, b) => rankK[b.kind] - rankK[a.kind] || a.m - b.m).slice(0, 3);
    const ys = s.ys && s.ys.f === f ? s.ys : null;
    let genOfYear = null;
    if (ys && ys.exp) { let best = -1; for (const x of g.generalsOf(f)) { const d = x.exp - (ys.exp[x.id] || 0); if (d > best) { best = d; genOfYear = { gen: x.id, exp: d }; } } }
    const fe = festivalOf(f);
    return { year: g.year - 1, cities: g.cityCount(f), income: facInc(f), food: sum(g.citiesOf(f), c => c.food), prestige: prestigeOf(f), prestigeGained: ys ? prestigeOf(f) - ys.prestige : null,
      order: Math.round(avg(g.citiesOf(f).map(c => c.order))), moments, genOfYear, missionsDone: logs.filter(e => e.kind === 'mission').length,
      calendar: [{ month: 1, what: '武林大会', on: !!(SG.Tourney && SG.Tourney.enabled !== false) }, { month: 3, what: '庙会', on: !!(SG.Items && SG.Items.enabled !== false) }, { month: 7, what: '秋收', on: true }, { month: 9, what: '文会', on: sub('wenhui') }, fe ? { month: fe.month, what: fe.name, on: sub('festivals') } : null].filter(Boolean).sort((a, b) => a.month - b.month),
      offers: s.mOffers.length };
  }

  // ================================================================ 月末（第 3 相）==
  function monthEnd(ctx) {
    if (!live()) return null;
    const t0 = now();
    const g = G(), s = st(), d = D();
    NEWS = [];
    reindex();
    MEMO = { nb: new Map(), pw: null, f: new Map(), hid: null, cf: new Map(), ev: null, evMo: 0 };
    const ended = ctx && ctx.ended ? ctx.ended : { year: g.year, month: g.month, monthIndex: g.monthIndex };
    const mo = ended.month % 12 + 1, ym = (mo === 1 ? ended.year + 1 : ended.year) * 12 + mo;
    // 常规经验的计数窗口：上月末的政绩 + 本月的指令（与原型相同）
    s.xp = { m: g.monthIndex, u: {} };
    const P = PROJ();
    const prof = CFG.profile ? (Engage.stats.parts = Engage.stats.parts || {}) : null;
    let tp = prof ? now() : 0;
    const lap = k => { if (!prof) return; const t = now(); prof[k] = (prof[k] || 0) + t - tp; tp = t; };
    const onOrder = sub('order'), onProj = sub('projects'), onPres = sub('prestige');
    let hidFree = null;
    for (const c of g.cities) {
      if (c.owner < 0) continue;
      const E = c.eng;
      // 民心向目标移动（+2 / −1）
      if (onOrder) { const t = orderTarget(c); c.order = c.order < t ? Math.min(Math.round(t), c.order + CFG.order.up) : c.order > t ? c.order - CFG.order.down : c.order; }
      if (E.buffs.length) E.buffs = E.buffs.filter(b => b.until > g.monthIndex + 1);
      // 疫病蔓延（祠庙之城不受）
      if (E.flags.plague > g.monthIndex) for (const j of c.links) {
        const n = g.cities[j];
        if (n.owner < 0 || n.eng.flags.plague > g.monthIndex || hasProject(n, 'temple') || n.eng.flags.plagueImmune > g.monthIndex) continue;
        if (rngAt(g.monthIndex, 'spread', c.id, j).nextDouble() < CFG.events.spreadP) { n.eng.flags.plague = g.monthIndex + CFG.events.spreadMonths; n.population = Math.round(n.population * (1 - CFG.events.spreadPop)); n.order = Math.max(0, n.order - CFG.events.spreadOrder); }
      }
      for (const k in E.flags) if (E.flags[k] <= g.monthIndex) delete E.flags[k];
      // 政绩：太守（主政者）每月 +3（民心 ≥ 50），≥ 75 再 +1
      const gov = c.governor >= 0 ? g.generals[c.governor] : null;
      if (gov && present(gov, c.owner) && c.order >= CFG.exp.governorOrder) routineExp(gov, CFG.exp.governor + (c.order >= CFG.exp.governorOrderHi ? CFG.exp.governorHi : 0));
      // 书院：智力 ≥ 60 的武将 +2；在野隐士 3 % / 月被发现
      if (onProj && E.done.length && hasProject(c, 'academy')) {
        for (const x of gensIn(c)) if (x.intel >= CFG.exp.academyIntel) routineExp(x, CFG.exp.academy);
        if (!hidFree) hidFree = g.generals.filter(x => x.hidden && SG.isFree(x));
        const hid = hidFree.filter(x => x.hidden && (x.city === c.id || c.links.indexOf(x.city) >= 0));
        if (hid.length && rngAt(g.monthIndex, 'acad', c.id).nextDouble() < P.academy.fx.revealP) { hid[0].hidden = false; if (MEMO) MEMO.hid = null; cnt(c.owner, 'revealed'); if (isPlayer(c.owner)) news(`${c.name}学宫的士子荐举了隐士${hid[0].name}。`); }
      }
      if (onProj) projectTick(c, ctx);
      // 城级：首次达到新的一级 +10 × 级（玩家每月；电脑每年正月，省时）
      if (onPres && (isPlayer(c.owner) || mo === 1)) {
        const lv = cityLevel(c);
        if (lv > (E.maxLv | 0)) {
          addPrestige(c.owner, CFG.prestige.cityLevel * lv);
          E.maxLv = lv;
          if (isPlayer(c.owner)) { news(`${c.name}升为${SG.Econ.levelName(lv)}。`); logEvt('cityLv', c.id, lv, c.owner); if (ctx && ctx.stageUps) ctx.stageUps.push({ city: c.id, kind: 'level', level: lv }); }
        }
      }
      if (onProj && E.done.length && hasProject(c, 'wonder')) addPrestige(c.owner, CFG.prestige.wonderMonthly);
    }
    lap('cities');
    bondsMonth(); lap('bonds');
    relationsMonth(); lap('relations');
    // 事件
    if (sub('events')) eventsMonth(mo, ym);
    lap('events');
    // 定期：玩家的节庆卡（下个月是节庆月）、文会（九月末结算；八月末给玩家发卡）
    if (g.player >= 0 && g.factions[g.player].alive) {
      const fe = festivalOf(g.player);
      if (sub('festivals') && fe && fe.month === mo && g.factions[g.player].eng.festYear !== Math.floor(g.monthIndex / 12)) festivalCard(g.player);
      if (sub('wenhui') && mo === CFG.wenhui.month) wenhuiCard(g.player);
    }
    if (sub('wenhui') && ended.month === CFG.wenhui.month) runWenhui({ year: ended.year, skipPlayer: st().wenhuiSkip === ended.year });
    lap('festivals');
    // 归附、电脑外交、电脑对玩家
    if (sub('diplomacy')) { submissions(mo); aiDiplomacy(); aiTowardPlayer(); }
    lap('diplomacy');
    // 玩家：国策、商队与探索归来、功业、胜利
    const pf = g.player;
    for (const cv of s.caravans) if (!cv.back && cv.until <= g.monthIndex + 1) caravanReturn(cv);
    for (const ex of s.exped) if (!ex.back && ex.until <= g.monthIndex + 1) expedReturn(ex);
    s.caravans = s.caravans.filter(x => !x.back || g.monthIndex - x.until < 24);
    s.exped = s.exped.filter(x => !x.back || g.monthIndex - x.until < 24);
    if (pf >= 0 && g.factions[pf].alive) {
      missionsMonth(pf);
      if (ctx && ctx.cityGold) cnt(pf, 'goldTotal', sum(ctx.cityGold, x => Math.max(0, x.gold)));
      achievementsMonth(pf);
      victoryMonth(pf);
    }
    // 卡：上月没答的结算，本月新卡排期（R1）
    if (pf >= 0) { expireCards(); scheduleCards(); }
    lap('player');
    Engage.stats.facMonths += g.factions.filter(f => f.alive).length;
    Engage.stats.months++;
    const ms = now() - t0;
    Engage.stats.ms += ms; Engage.stats.msMax = Math.max(Engage.stats.msMax, ms);
    if (ctx && ctx.news) for (const l of NEWS) ctx.news.push(l);
    const out = { news: NEWS.slice(), cards: pf >= 0 ? s.cards.filter(c => !c.done && c.due === g.monthIndex + 1).length : 0 };
    NEWS = []; MEMO = null;
    return out;
  }
  function eventsMonth(mo, ym) {
    const g = G(), E = CFG.events;
    for (const F of g.factions) {
      if (!F.alive) continue;
      const k = rngAt(g.monthIndex + 1, 'evt', F.id);
      const p = Math.min(E.pMax, E.pBase + E.pPerCity * countM(F.id));
      if (k.nextDouble() >= p) continue;
      const tq = CFG.profile ? now() : 0;
      const pk = drawEvent(F.id, k, mo, ym);
      if (CFG.profile) { const t1 = now(); Engage.stats.parts.draw = (Engage.stats.parts.draw || 0) + t1 - tq; }
      if (!pk) continue;
      const tr = CFG.profile ? now() : 0;
      const r = runEvent(F.id, pk, k, mo, ym);
      if (CFG.profile) { const kk = 'ev:' + pk.ev.id; Engage.stats.parts[kk] = (Engage.stats.parts[kk] || 0) + now() - tr; }
      if (r) { Engage.stats.eventsByFac++; if (r.card) queueCard(r.card); }
    }
    // 天下事件（彗星 / 日食）
    const kg = rngAt(g.monthIndex + 1, 'global');
    if (kg.nextDouble() < E.globalP) {
      const comet = EV().comet;
      for (const F of g.factions) {
        if (!F.alive) continue;
        const cap = capitalOf(F.id); if (!cap) continue;
        const r = runEvent(F.id, { ev: comet, c: cap }, rngAt(g.monthIndex + 1, 'comet', F.id), mo, ym);
        if (r && r.card) queueCard(r.card);
      }
    }
    // 剧本史事（日期 = 下个月；冷却与灾害间隔照常）
    for (const h of D().historical) {
      if (h.scenario !== g.scenario) continue;
      const [y, m] = h.date.split('-').map(Number);
      if (y * 12 + m !== ym) continue;
      const ev = EV()[h.event];
      for (const key of h.cityKeys) {
        const c = g.cityByKey(key); if (!c || c.owner < 0) continue;
        if ((c.eng.cd[ev.id] === undefined ? -999 : c.eng.cd[ev.id]) + ev.cd > g.monthIndex) continue;
        if (ev.disaster && g.monthIndex - c.eng.lastDis < E.disasterGap) continue;
        if (rngAt(g.monthIndex + 1, 'hist', h.id, key).nextDouble() >= h.p) continue;
        const r = runEvent(c.owner, { ev, c }, rngAt(g.monthIndex + 1, 'histev', h.id, key), mo, ym, { hist: h.text });
        if (r && r.card) { r.card.hist = h.text; queueCard(r.card); }
      }
    }
  }
  function bondsMonth() {
    if (!sub('bonds')) return;
    const g = G(), S = st().bonds, Bc = CFG.bonds;
    // 同城的武将（每城前 6 名）两两建立记录
    for (const c of g.cities) {
      if (c.owner < 0) continue;
      const os = gensIn(c).slice(0, Bc.perCity);
      for (let i = 0; i < os.length; i++) for (let j = i + 1; j < os.length; j++) { const k = pkey(os[i].id, os[j].id); if (!S[k]) S[k] = { a: 0 }; }
    }
    let changed = false;
    for (const key of Object.keys(S)) {
      const bo = S[key];
      const i = key.indexOf('-'), a = g.generals[+key.slice(0, i)], b = g.generals[+key.slice(i + 1)];
      if (!a || !b) { delete S[key]; changed = true; continue; }
      if (a.dead || b.dead) { if (!bo.k) { delete S[key]; changed = true; } continue; }
      const together = a.faction >= 0 && a.faction === b.faction && a.city === b.city && !absent(a) && !absent(b);
      if (!together) { if (!bo.k && bo.a > 0) { bo.a = Math.max(0, bo.a - Bc.apartDecay); if (bo.a === 0) { delete S[key]; changed = true; } } continue; }
      if (bo.a < Bc.cityAffMax && bo.a >= 0 && bo.k !== 'feud') { bo.a += Bc.cityAff; if (!bo.k && bo.a >= Bc.friendAt) { bo.k = 'friend'; bo.s = g.monthIndex; changed = true; if (isPlayer(a.faction)) news(`${a.name}与${b.name}朝夕共事，结为知己。`); } }
      if (bo.k === 'friend' || bo.k === 'spouse') for (const x of [a, b]) if (!isRuler(x) && x.loyalty < Bc.friendLoyMax) x.loyalty++;
      if (bo.k === 'feud') for (const x of [a, b]) if (!isRuler(x) && x.loyalty > Bc.feudLoyFloor) x.loyalty--;
      if (bo.k === 'mentor') { const stu = bo.m === a.id ? b : a; storyExp(stu, CFG.exp.mentor); }
    }
    if (changed) bondVer++;
  }
  function relationsMonth() {
    const g = G(), s = st(), R = s.rel, C = CFG.rel;
    if (sub('diplomacy')) {
      const even = g.monthIndex % C.innerEvery === 0, third = g.monthIndex % C.outerEvery === 0;
      for (const key of Object.keys(R)) {
        let v = R[key];
        if (v > C.inner) { if (third) v -= 1; } else if (v < -C.inner) { if (third) v += 1; } else if (even) v += v > 0 ? -1 : 1;
        if (v === 0) delete R[key]; else R[key] = v;
      }
      // 盟约的好感下限
      for (const p of s.pacts) {
        if (p.until <= g.monthIndex) continue;
        const fl = C.floors[p.kind]; if (fl && rel(p.a, p.b) < fl) setRel(p.a, p.b, fl);
      }
      for (let a = 0; a < g.factions.length; a++) for (let b = a + 1; b < g.factions.length; b++) if (g.alliance[a * g.factions.length + b] > g.monthIndex && rel(a, b) < C.floors.ally && g.factions[a].alive && g.factions[b].alive) setRel(a, b, C.floors.ally);
      for (const k of Object.keys(s.war)) if (g.monthIndex - s.war[k] >= C.warMonths) delete s.war[k];
    }
    // 贡金（AIBudget 的底线规则：电脑付不起当月跳过；玩家付不起 → 约止，好感 −20）
    for (const p of s.pacts) {
      if (p.kind !== 'tribute' || p.until <= g.monthIndex || !p.pay) continue;
      const payer = p.payer, recv = p.payer === p.a ? p.b : p.a;
      if (!g.factions[payer].alive || !g.factions[recv].alive) { p.until = g.monthIndex; continue; }
      const dst = capitalOf(recv);
      if (isPlayer(payer)) {
        const src = payFrom(payer, p.pay);
        if (!src || !dst) { p.until = g.monthIndex; addRel(payer, recv, CFG.tribute.breakRel); news(`无力缴纳岁贡，与${g.factions[recv].name}之约作废。`); continue; }
        src.gold -= p.pay; dst.gold += p.pay; cnt(payer, 'spend:tribute', p.pay);
      } else {
        const A = SG.AIBudget;
        const src = A ? A.payer(payer, p.pay, 'tribute') : payFrom(payer, p.pay);
        if (!src || !dst) continue;
        if (A) A.spend(src, p.pay, 'tribute'); else src.gold -= p.pay;
        dst.gold += p.pay;
      }
      cnt(recv, 'tributeIn', p.pay); addPrestige(recv, CFG.prestige.tributeIn);
    }
    // 到期的约：质子平安归来
    for (const p of s.pacts) {
      if (p.until > g.monthIndex + 1 || p.closed) continue;
      p.closed = true;
      if (p.hostage !== undefined && !p.broken) { const x = g.generals[p.hostage]; if (x && !x.dead && x.away && x.away.kind === 'hostage') { storyExp(x, CFG.hostage.exp); x.loyalty = Math.min(100, x.loyalty + CFG.hostage.loyalty); } }
    }
    s.pacts = s.pacts.filter(p => p.until > g.monthIndex - 24 || p.kind === 'marriage'); pactVer++;
  }

  // ================================================================ 指令（INTEGRATION §6.3；Commands.lastResult 是输入）==
  function onCommand(cmd, res) {
    if (!live() || !res) return;
    const g = G();
    const gen = res.gen != null && res.gen >= 0 ? g.generals[res.gen] : null;
    const f = res.f;
    const X = CFG.exp;
    switch (cmd) {
      case 'develop': if (res.ok && gen) routineExp(gen, X.develop + ((res.gain | 0) >= X.developBigGain ? X.developBig : 0)); break;
      case 'recruit': if (res.ok && gen) { routineExp(gen, X.recruit); const c = g.cities[res.city]; if (c && sub('order')) c.order = clamp(c.order - Math.ceil((res.n | 0) / CFG.order.recruitPer), 0, 100); } break;
      case 'train': if (res.ok && gen) routineExp(gen, X.train); break;
      case 'search': if (gen) { const found = res.outcome === 'talent' || res.outcome === 'item'; routineExp(gen, found ? X.searchFound : X.search); if (res.outcome === 'talent') cnt(f, 'hiddenFound'); } break;
      case 'hire': if (gen) routineExp(gen, res.ok ? X.hireOk : X.hireFail); if (res.ok) cnt(f, 'hires'); break;
      case 'ally': if (gen) routineExp(gen, res.ok ? X.envoyOk : X.envoyFail); if (res.ok && res.target != null) onAlly(f, res.target); break;
      case 'discord': {
        if (gen) routineExp(gen, res.ok ? X.discordOk : X.plotFail);
        const t = res.target != null ? g.generals[res.target] : null;
        if (t && t.faction >= 0) addRel(f, t.faction, CFG.rel.discord);
        break;
      }
      case 'persuade': {
        if (gen) routineExp(gen, res.ok ? X.persuadeOk : X.plotFail);
        const of = res.from !== undefined ? res.from : null;
        if (of !== null && of >= 0) addRel(f, of, CFG.rel.persuade);
        if (res.ok && res.surrendered) cnt(f, 'persuadeCity');
        // 结义兄弟被劝走：其余兄弟忠诚 −15，25 % 随之而去（§3.4）
        const t = res.target != null ? g.generals[res.target] : null;
        if (res.ok && t && of !== null && of >= 0 && sub('bonds')) {
          const k = rng('follow', t.id);
          for (const y of swornGroup(t)) {
            if (y.faction !== of || isRuler(y) || y.dead) continue;
            y.loyalty = Math.max(0, y.loyalty - CFG.bonds.swornLoss);
            if (k.nextDouble() < CFG.bonds.swornFollowP && g.officersIn(g.cities[y.city]).length > 1) { const oc = g.cities[y.city]; y.faction = f; y.city = t.city; y.loyalty = 65; y.troops = Math.round(y.troops / 2); y.moved = true; dirtyIdx(); g.autoGovernor(oc); Engage.stats.defectFollow++; }
          }
        }
        break;
      }
      default: break;
    }
  }

  // ================================================================ 攻城（SG.Events）==
  function onPrepare(s) {
    if (!live() || !s || !s.target) return;
    const g = G();
    s._engT = { atk: sum(s.atk, x => x.troops), def: sum(s.def, x => x.troops) };
    const A = s.attacker, D2 = s.target.owner;
    if (D2 < 0 || A < 0) return;
    st().war[pkey(A, D2)] = g.monthIndex;
    addRel(A, D2, CFG.rel.attack);
    g.factions[A].eng.lastAttack = g.monthIndex;
    if (sub('diplomacy') && nonAggro(A, D2)) betray(A, D2);
    for (const x of s.atk) x.lastBattle = g.monthIndex;
  }
  function onApply(s, info) {
    if (!live() || !s || !s.target || !s.target.eng) return;
    const g = G(), t = s.target;
    const A = info ? info.attacker : s.attacker, D2 = info ? info.defender : s.defender;
    const T0 = s._engT || { atk: 0, def: 0 };
    if (s.attackerWon) {
      if (sub('order')) t.order = Math.max(CFG.order.conquestFloor, t.order - CFG.order.conquestDrop);
      t.eng.trouble = g.monthIndex;
      if (D2 >= 0) {
        addRel(A, D2, CFG.rel.conquer); addPrestige(A, CFG.prestige.conquest); cnt(A, 'conquests');
        if (!g.factions[D2].alive) addPrestige(A, CFG.prestige.destroy);
      }
      // 城中的营建：攻下的城保留它的建筑（新主人接手）；未竣的工程中断
      if (t.eng.proj) t.eng.proj = null;
    } else {
      if (sub('order')) t.order = Math.max(0, t.order - CFG.order.repelled);
      if (D2 >= 0) { if (T0.atk >= T0.def) addPrestige(D2, CFG.prestige.defend); cnt(D2, 'defenses'); }
    }
    for (const x of (s.atk || []).concat(s.def || [])) x.lastBattle = g.monthIndex;
    const winners = s.attackerWon ? s.atk : s.def;
    if (winners) for (let i = 0; i < winners.length; i++) for (let j = i + 1; j < winners.length; j++) if (winners[i].faction === winners[j].faction && winners[i].faction >= 0) addAff(winners[i], winners[j], CFG.bonds.winAff);
    dirtyIdx();
  }
  function onFactionDeath(f) {
    if (!live()) return;
    const g = G(), s = st();
    for (const p of s.pacts) if (p.until > g.monthIndex && (p.a === f || p.b === f)) p.until = g.monthIndex;
    for (const c of s.cards) if (!c.done && c.from === f) { c.done = true; c.answer = null; }
  }
  function onTitleGrant(x, res) {
    if (!live() || !x || !res || !res.ok) return;
    for (const q of res.passedOver || []) if (q.gen) addAff(q.gen, x, CFG.bonds.passedOverAff);
    for (const r of bondsOf(x)) if (r.kind === 'rival' && r.gen && r.gen.faction === x.faction && !isRuler(r.gen)) r.gen.loyalty = Math.max(0, r.gen.loyalty - CFG.bonds.rivalTitleLoss);
  }

  // ================================================================ 其他相 ==
  function onIncome(ctx) {
    if (!live()) return;
    const g = G();
    const m = ctx && ctx.ended ? ctx.ended.month : g.month;
    for (const c of g.cities) {
      if (c.owner < 0 || !c.eng) continue;
      // 屯田：驻军耗粮 −25 %（phaseIncome 已扣全额，这里退回）
      if (sub('projects') && hasProject(c, 'tuntian')) c.food += Math.round(Math.floor(g.troopsIn(c) / B().TroopsPerFood) * CFG.projects.tuntianFood);
      if (m === B().HarvestMonth) {
        c.eng.buffs = c.eng.buffs.filter(b => b.mod !== 'harvest');
      }
    }
    if (m === B().HarvestMonth && g.player >= 0 && ctx && ctx.harvests) cnt(g.player, 'harvestTotal', sum(ctx.harvests, h => Math.max(0, h.harvest)));
  }
  function onNews(ctx) {
    if (!live()) return;
    const g = G(), s = st();
    // 外出到期：留下的记录删掉（游学的经验此时入账）
    for (const x of g.generals) {
      if (!x.away || x.away.until > g.monthIndex) continue;
      if (x.away.exp && !x.dead) storyExp(x, x.away.exp);
      delete x.away;
    }
    for (const id of Object.keys(s.gflags)) { const o = s.gflags[id]; for (const k of Object.keys(o)) if (o[k] <= g.monthIndex) delete o[k]; if (!Object.keys(o).length) delete s.gflags[id]; }
    dirtyIdx();
    // 正月：天下榜的年度快照、年鉴的起点、师徒传艺
    if (g.month === 1) {
      const row = {};
      for (const F of g.factions) if (F.alive) row[F.id] = score(F.id);
      s.hist[g.year] = row;
      const ys = Object.keys(s.hist).map(Number).sort((a, b) => a - b);
      while (ys.length > CFG.score.histYears) delete s.hist[ys.shift()];
      if (g.player >= 0) { const ex = {}; for (const x of g.generalsOf(g.player)) ex[x.id] = x.exp | 0; s.ys = { f: g.player, year: g.year, prestige: prestigeOf(g.player), exp: ex }; }
      mentorLearn();
    }
    if (s.scoreGame && g.year * 12 + g.month >= s.scoreGame.end && !s.scoreGame.done) { s.scoreGame.done = scoreGameResult(g.player); emit('engage.victory', 'scoreGame'); }
  }
  function mentorLearn() {
    if (!sub('bonds') || !SG.Progress || typeof SG.Progress.learn !== 'function') return;
    const g = G(), S = st().bonds;
    for (const key of Object.keys(S)) {
      const bo = S[key]; if (bo.k !== 'mentor') continue;
      const i = key.indexOf('-'), a = g.generals[+key.slice(0, i)], b = g.generals[+key.slice(i + 1)];
      if (!a || !b || a.dead || b.dead || a.faction !== b.faction || a.faction < 0) continue;
      const mentor = bo.m === a.id ? a : b, stu = mentor === a ? b : a;
      if (rngAt(g.monthIndex, 'mlearn', stu.id).nextDouble() >= CFG.bonds.learnP) continue;
      const tryList = [].concat((mentor.specials || []).slice().reverse().map(id => ['special', id]), (mentor.tactics || []).slice().reverse().map(id => ['tactic', id]));
      for (const [type, id] of tryList) if (SG.Progress.learn(stu, type, id)) { if (isPlayer(stu.faction)) news(`${stu.name}得${mentor.name}传授，习得新招。`); break; }
    }
  }

  // ================================================================ 新局 / 迁移 / 读档 ==
  function init(g) {
    if (!on()) return;
    const prev = SG.G; SG.G = g;
    try { buildState(g); } finally { SG.G = prev; }
    installHooks();
  }
  function migrate(g, from) {
    if (!on()) return;
    const prev = SG.G; SG.G = g;
    try { if (!g.engage) buildState(g); else ensure(g); } finally { SG.G = prev; }
    installHooks();
  }
  function load(g) {
    if (!on() || !g.engage) { installHooks(); return; }
    const prev = SG.G; SG.G = g;
    try { ensure(g); } finally { SG.G = prev; }
    bondVer++; IDX = null; COAST = null;
    installHooks();
  }

  // ================================================================ 提供者（INTEGRATION §3.3）==
  const PROVIDERS = {
    order: c => (sub('order') && typeof c.order === 'number' && c.owner >= 0 ? c.order : undefined),
    project: (c, key) => {
      if (!sub('projects') || !c || !c.eng || !c.eng.done.length) return 0;
      switch (key) {
        case 'dev:industry': return projFx(c, 'devIndustry');
        case 'income': return projFx(c, 'income');
        case 'harvest': return projFx(c, 'harvest');
        case 'recruit': return hasProject(c, 'forge') ? -PROJ().forge.fx.recruitCost : 0;   // 铁官：每金得兵 +10 %（INTEGRATION §4.4）
        case 'train': return projFx(c, 'train');
        case 'search': return projFx(c, 'search');
        default: return 0;
      }
    },
    eventIncome: c => (live() && c.eng ? buffSum(c, 'income') : 0),
    eventHarvest: c => (live() && c.eng ? buffSum(c, 'harvest') : 0),
    pactIncome: c => {
      if (!sub('trade') || c.owner < 0) return 0;
      const g = G();
      for (const p of st().pacts) {
        if (p.kind !== 'trade' || p.until <= g.monthIndex || (p.a !== c.owner && p.b !== c.owner)) continue;
        const o = p.a === c.owner ? p.b : p.a;
        if (c.links.some(j => g.cities[j].owner === o)) return CFG.trade.income;
      }
      return 0;
    },
    prestigeFame: f => (sub('prestige') && f >= 0 ? Math.floor(prestigeOf(f) / CFG.famePer) : 0),
    eventFame: f => (live() && f >= 0 && G().factions[f] && G().factions[f].eng ? G().factions[f].eng.fameEv || 0 : 0),
    eventVirtue: f => (live() && f >= 0 && G().factions[f] && G().factions[f].eng ? G().factions[f].eng.virtueEv || 0 : 0),
    rel: (f, t) => (sub('diplomacy') ? rel(f, t) : 0),
    bondShield: x => bondShield(x),
    bondAtk: (u, model) => battleBonus(u, model).atk,
    bondDef: (u, model) => battleBonus(u, model).def,
    noAttack: (a, b) => (live() ? noAttack(a, b) : false),
    needK: (f, t) => {
      if (!sub('diplomacy') || !t || t.owner < 0) return 0;
      const e = G().factions[t.owner].eng;
      return e && e.refused && e.refused[f] > G().monthIndex ? CFG.aiDemand.needK : 0;
    },
    cityDef: c => defense(c),
    popGrowth: c => (sub('order') && typeof c.order === 'number' && c.owner >= 0 ? c.order / 100 - 0.5 : 0),
    recruitDrill: c => (sub('projects') && hasProject(c, 'drill') ? PROJ().drill.fx.recruitTraining : 0),
    treasureMapK: (c, itemId) => (live() && c && st().clues.some(q => q.item === itemId && q.city === c.id && q.f === c.owner) ? 1 : 0),
    tradeK: c => (live() && c && c.eng ? (sub('projects') && hasProject(c, 'granary') ? PROJ().granary.fx.sellK : 0) + (buffSum(c, 'foodPrice') < 0 ? -buffSum(c, 'foodPrice') : 0) : 0),
    persuadeRes: x => bondPersuadeRes(x),
  };
  let providersOff = null;
  function installProviders() {
    if (providersOff || !SG.Mods || typeof SG.Mods.provide !== 'function') return;
    providersOff = SG.Mods.provide(PROVIDERS, { owner: 'engage', when: live });
  }
  installProviders();

  let hooksInstalled = false;
  function installHooks() {
    if (hooksInstalled) return true;
    const A = SG.StrategyAI;
    if (!A || typeof A.on !== 'function') return false;
    hooksInstalled = true;
    A.on('income', ctx => onIncome(ctx), { owner: 'engage', when: live, order: 50 });
    A.on('engage', ctx => monthEnd(ctx), { owner: 'engage', when: live, order: 50 });
    A.on('news', ctx => onNews(ctx), { owner: 'engage', when: live, order: 50 });
    A.onSpend('project', f => aiProjects(f), { owner: 'engage', when: live });
    A.onSpend('festival', f => aiFestival(f), { owner: 'engage', when: live });
    return true;
  }
  let eventsInstalled = false;
  function installEvents() {
    const E = SG.Events;
    if (eventsInstalled || !E) return;
    eventsInstalled = true;
    E.on('command', (cmd, res) => { try { onCommand(cmd, res); } catch (e) { console.error(e); } });
    E.on('conquest.prepare', s => { try { onPrepare(s); } catch (e) { console.error(e); } });
    E.on('conquest.apply', (s, info) => { try { onApply(s, info); } catch (e) { console.error(e); } });
    E.on('faction.death', f => { try { onFactionDeath(f); } catch (e) { console.error(e); } });
    E.on('title.grant', (x, res) => { try { onTitleGrant(x, res); } catch (e) { console.error(e); } });
    E.on('tourney.result', r => { try { tourneyResult(r); } catch (e) { console.error(e); } });
  }
  installEvents();

  Object.assign(Engage, {
    CFG, saveKeys: ['engage'],
    get data() { return D(); },
    init, migrate, load, installHooks, installProviders, providers: PROVIDERS,
    resetStats() { Engage.stats = STATS0(); },
    reset() { IDX = null; COAST = null; bondIdx = null; NEWS = []; },
    // 月末与卡
    monthEnd, monthStartCards, pendingCards, resolve, dismiss: card => resolve(card, safeOf(card), { auto: 'dismiss' }), onCommand, scheduleCards, queueCard,
    // 民心、声望
    order: c => (c && typeof c.order === 'number' ? c.order : null), orderTarget, prestige: prestigeOf, prestigeName, addPrestige,
    fameBonus: f => (SG.Mods ? SG.Mods.fameBonus(f) : 0), virtueBonus: f => (SG.Mods ? SG.Mods.virtue(f) - SG.Mods.baseVirtue(f) : 0),
    // 邦交
    rel, addRel, setRel, atWar, pacts: f => (live() ? pactsOf(f) : []), pactOf, noAttack, needK: (f, t) => PROVIDERS.needK(f, t), betrayalCost,
    chance, canPropose, propose, giftDelta, envoyK, neighbors, power, facInc, capitalOf,
    // 羁绊
    bond: (a, b) => (live() ? bondRec(a, b) : null), bondKind, bondsOf, affinitiesOf, addAff, setKind, battleBonus, isRival, rivalDmg, bondShield,
    // 营建
    buildable, startProject, project: c => (c && c.eng ? c.eng.proj : null), projects: c => (c && c.eng ? c.eng.done.slice() : []), hasProject, projectName, projectFx: projFx,
    shopTierBonus, shopUncommonSlots, defense, cityLevel, shopTier,
    // 商队、探索
    caravanTargets, sendCaravan, caravans, caravanLimit, investMax, sites, sendExpedition, expeditions, expedLimit,
    // 国策
    missionOffers, acceptMission, cancelMission, missions, missionGoal, missionText, offerMissions,
    // 文会、节庆、小游戏
    wenhuiEntrants, setWenhuiEntrants, wenhuiEligible, wenhuiHost, wenhuiName, runWenhui, debate, touhuScore, touhuLoyalty, STANCES,
    festivalOf, festivalCost, festivalRec, holdFestival,
    // 功业、胜利、天下榜
    achievements, victory, wangdaoConds, score, leaderboard, yearReview, scoreGameResult,
    startScoreGame(years) { if (!live()) return null; const g = G(); st().scoreGame = { years: years || 30, start: g.monthIndex, end: g.year * 12 + g.month + (years || 30) * 12 }; return st().scoreGame; },
    // 其他
    vetLevel, tourneyResult, tourneyPicks, fillText, evText, cnt, getCnt, logEvt, routineExp, storyExp, grantItem, absorb, threatened,
    isOn: live,
    // 测试用的内部函数（不是公共接口）
    _internals: { drawEvent, factionOK, cityOK, weightOf, cityFacts, makeCtx, aiChoose, affordable, eventsFor, beginMemo: () => { MEMO = { nb: new Map(), pw: null, f: new Map(), hid: null, cf: new Map(), ev: null, evMo: 0 }; reindex(); }, endMemo: () => { MEMO = null; } },
  });
  SG.Engage = Engage;
  installHooks();   // strategy-ai.js 在本文件之后载入：这里通常装不上，init / migrate / load 时再装
})();
