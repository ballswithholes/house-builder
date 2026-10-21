'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 装备与商店（ITEMS.md；INTEGRATION §3–§8）
   纯逻辑：无 THREE / DOM，Node 可测（tests/items.js）。数据：js/items-data.js（SG.ItemsData，由 tools/gen-items.js 生成）。
   载入：model.js 之后、migrate-v4.js / strategy-ai.js 之前（INTEGRATION §11）。所有战斗 / 内政数字都以「提供者」交给
   SG.Mods / SG.Econ（INTEGRATION §3.3），本模块自己从不乘战斗或内政的数。

   开关：SG.Items.enabled（缺省 true；关掉时不建任何状态、不取随机数、提供者返回 0，INTEGRATION §1.2）
   可调的数一律在 SG.Items.CFG（一处）；DESIGN-V4 §20 的玩家决定读 SG.Balance.Decisions（duelBudget、slayRulersOnly、
   slayOthersK、safeSwastikaName、classicForeignChance），设置行「阵斩」（items.slay）可改阵斩范围。

   ---- 存档（ITEMS §15）----
   G.items = { next, inst: { uid: { id, loc: 'gen'|'arm'|'city', owner, gifted, giftLoy, bound, since, kind? } },
               hiddenFound: [id], singletonsGiven: [id], tourneyAwarded: [id], cooldown: { cityId: { id: monthIndex } },
               seen: { id: 1 }, owned: { id: 1 }, clues: { cityKey: 1 }, aggro: { 'f>t': untilMonth },
               aiGear: { factionId: { c: 电脑买装额度, mi: 记账月 } } }
   gen.gear = { weapon, offhand, armour, helmet, mount, treasure }（实例编号或 null）    faction.armoury = [uid]
   city.shop = { stock: [{ id, qty, price, age, foreign? }], merchant: { until, stock } | null }    city.vault = [uid]（镇城之宝等）

   ---- SG.Items ----
   enabled  CFG  SLOTS  RARITIES  TIER  DUEL_ITEM_EFFECTS  EFFECTS（§7.2 能力词汇）
   get(id) all() tier(id|item) isShield(it) styleOf(it) rarityInfo(r) price(id) valueOf(id)
   inst(uid) instOf(uid) → { uid, id, item, loc, owner, … }   where(uid)   count(id)（世上实例数）   instancesOf(f)
   setup(G) / init(G) / migrate(G, from) / load(G, data)        新局 / 迁移 / 读档（SG.MigrateV4 自动发现，saveKeys ['items']）
   ---- 商店（§6）----
   shopTier(c) slots(c) cityK(c) restock(c, first?) restockAll() entryPrice(c, entry, f) sellPrice(uid|id, c) canTrade(f, c) → 原因 | null
   buy(f, c, entryIndex, { gen, payCity, merchant }) → { ok, why, uid, price }      sell(f, uid, c) → { ok, why, gold }
   openMerchant(c, rng?) → stock（ENGAGEMENT「行商过境」卡调用；c.shop.merchant）
   ---- 获取（§8）----
   searchHidden(c, g) → { item: uid, id, name } | null（Commands.searchItem）   hiddenIn(c)   clueFor(c)
   spoils(victim, f, how) → [uid]   slay(victim, byFaction, how) → [uid]   slayChance(victim, how, ctx) rollSlay(victim, how, byFaction, ctx) → bool
   disarm(gen, f) → [uid]（缴械释放）   award(target, id, opts) → uid   grant(f, rarity|id, why, culture?, rng?) → id
   pool({ culture, types, rarity, slots }) → [id]   offers(gen, rarity, n, rng) → [id]   tourneyPrize(gen, place, rng, opts) → { id, gold, foreign }
   titleRelic(gen) → uid | null（武林大会三冠：金刚杵）   takeReveals() → [{ id, uid, to, from, how }]（界面的神品揭示）
   ---- 赠送、府库 ----
   gift(gen, uid, opts) → { ok, why, loy, before, afterEquip, after, eqDelta, displaced, fits, line } | { ok: false, needConfirm, displaced }
   giftDisplace(gen, uid) → { uid, id, cost, sig, worse } | null   giftK(id, givers)   giftValue(uid | id, givers)   armoury(f, { vault }) → [uid]
   transfer(uid, t, { from }) → { ok, why }（外交礼物：送进 t 的府库）
   takeCost(gen, uid, replacement?) → { cost, refuse?, gift? }（从 gen 身上拿走 uid 的忠诚代价；界面预告用）
   ---- 电脑（§13）----
   score(gen, id) role(gen) aiEquip(f) aiShop(f) aiSell(f)   stats（本局统计）
   ---- 图鉴（§14.2）----
   markSeen(id) collection(f?) → { pages, fame, seen, owned, total, epicSeen }
   ---- 校验（§17.1）----
   checkCatalogue(data?) → { problems: [], stats }   validateDuel(item) → [问题]（白名单之外的单挑字段，载入时即报错）

   ---- SG.Gear（每名武将的装备数值）----
   slots(gen) → { weapon: inst|null, … }   item(gen, slot) → 装备条目 | null   equipped(gen) → [{ slot, uid, id, item, req }]
   raw(gen, key) stat(gen, key)（COMBAT 键 critArmy / critDuel / critMulArmy / critMulDuel 返回小数）
   eff(gen, k) duelWar(gen) abilities(gen, t?) has(gen, t) duelMods(gen, foe?) summary(gen)
   candidates(gen, slot) → [{ uid, id, item, delta, ok, why, from }]   equip(gen, uid, opts) → { ok, why, loyaltyDelta, displaced }
   unequip(gen, slot, opts) → { ok, why, loyaltyDelta }   canEquip(gen, uid) → { ok, why }   invalidate()
   battle.*：战斗规则的纯函数（ITEMS §3.2 / §7.2；battle-model.js 接线，见 INTEGRATION 请求）：mobility ease hard awe openingAwe
     escape tac tacHit fireWeak antiHorse antiHorseK heal counter counterGuard armorBreak mounted chargeK attackK(ctx) condAtk condDef
     noCounter(gen, { moved }) critAt critAdd critMulAdd critRes critWard critTaken dmgTaken ward splash trample procs capture rescue
     decoy guard opening defenderOpening oath onRout rally regen throwAttack hitRun zoc defendCityK expBoost medic
   ---- 世界 / 内政能力的接线点（调用处见 INTEGRATION 请求）----
   duelLure(gen) freeUses(f, cmd) useFree(f, cmd) relFloor(f, t, envoy?) scout(gen) scouted(f) → Set(城) defendCity(c)
   captureP(gen, p, setup) rescueAt(model, unit) critTaken(p, targetGen)

   提供者（SG.Mods.provide，owner 'items'，when = SG.Items.enabled）：eff gearWar gearAtk gearDef gearCrit gearCritMul gearMorale
   auraAtk auraDef gearDev gearRecruit gearTrain gearSearch gearHire gearDip gearDiscord gearPersuade expBoost gearFame gearVirtue
   gearReward tradeK allyMonths itemCity itemLoyAura autoPower aiTargetK；另有新名（battle-math 接线前不起作用）：
   gearCritCond(gen, ctx) gearCritMulCond(gen, ctx) gearCritRes(gen) gearCritWard(gen)
   月末（StrategyAI，第 2 相 'items'：补货（为下个月备货）、每月装备效果、电脑寻宝；第 5 相 'loyAura'：负的 / 有上限的 / 限文化的忠诚光环；
   第 7 相 onSpend('gear')：电脑换装与买装，经 SG.AIBudget）
   事件（SG.Events）：captive.taken / captive.released / captive.executed（战利品、缴械）、faction.death（府库转交）、
   conquest.prepare / resolved / apply（自动结算的守方加成；线索）、command（搜索失败时的线索）
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const G = () => SG.G;

  const SLOTS = Object.freeze(['weapon', 'offhand', 'armour', 'helmet', 'mount', 'treasure']);
  const RARITIES = Object.freeze(['common', 'uncommon', 'rare', 'epic', 'legendary']);
  const TIER = Object.freeze({ common: 0, uncommon: 1, rare: 2, epic: 3, legendary: 4 });
  const SLOT_NAME = Object.freeze({ weapon: '兵器', offhand: '副手', armour: '铠甲', helmet: '头盔', mount: '坐骑', treasure: '宝物' });

  // ================================================================ 配置（一处）==
  // DESIGN-V4 §20 的玩家决定：数据在 js/data.js 的 Balance.Decisions（冻结，唯一的一处）；Items.decide(k, v) 在运行时覆盖（测试、设置用，不存档）
  const DEC = () => (SG.Balance && SG.Balance.Decisions) || {};
  const OVR = {};
  const dec = (k, d) => { if (OVR[k] !== undefined) return OVR[k]; const v = DEC()[k]; return v === undefined || v === null ? d : v; };
  const CFG = {
    // §2.4 有效属性
    attrCap: 110, gearAttrCap: 15, duelWarCap: 3, duelWarDiv: 3,
    // §2.6 收回装备的忠诚代价（按品级）；招牌装备离开原主 −15，忠诚会跌破 50 时拒绝
    takeLoy: [0, 1, 3, 6, 10], sigTakeLoy: 15, sigRefuseBelow: 50,
    armouryCap: 40,
    kitBow: true,      // 起始装备：不持盾的武将配弓（与 design-v4/integration/items-mod.js 相同；ITEMS §8.10 的表没写，见报告）
    // §3.3 / INTEGRATION §8.3 单挑：全合一预算（预算值本身读 Decisions.duelBudget，缺省 6）
    duel: { gearHp: 10, dmg: 3, abilityHp: 4, guardPP: 6, parry: 0.01, critPP: 3, critMul: 0.30, reachK: 0.05, spdLo: -6, spdHi: 4, move: 10,
      guardBase: 0.80, guardMax: 0.86, guardAbsMax: 0.88, guardBreak: 12, guardFloor: 0.5, dmgAll: 6, hpPerPP: 0.4, guardHpK: 0.5 },
    // §4.3 价格
    originK: { native: 1.0, foreign: 1.2, merchant: 1.5 }, alliedK: 1.15, discCap: 0.125, discDiv: 200,
    sellTourneyK: 0.5,
    // §6.2 / §6.4 商店与补货
    slotsByTier: [0, 4, 6, 8, 10],
    restock: { ageOld: 4, pDropOld: 0.35, rareMaxAge: 12, pRare: 0.12, pUncommon: 0.37, pForeign: 0.25, commonQtyMax: 3, cooldown: 24, epicCooldown: 60 },
    // §6.5 胡商 / 海商（ENGAGEMENT 的「行商过境」卡触发）
    merchant: { n: 3, pRare: 0.20, months: 2, radiusDeg: 25, maxCultures: 3 },
    // §6.6 赐宝物
    gift: { base: 8, rollMax: 4, valueDiv: 60, max: 30, fitK: 1.5, chituGuanyu: 30 },
    // §8.2 阵斩（君主；Decisions.slayRulersOnly / 设置 items.slay 打开「所有武将」时其他武将 × Decisions.slayOthersK）
    slay: { critRout: 0.25, duelKOBig: 0.35, duelKO: 0.15, timeout: 0, assassinate: 0.50 },
    // §8.5 隐藏宝物
    hidden: { pMax: 0.40, searchDiv: 200, sunJianK: 2, cluePFail: 0.30, aiEvery: 3 },
    // §8.8 俘虏
    confiscate: { aggroK: 1.1, months: 12, loyalty: 5, aiP: 0.5 },
    // §13 电脑
    ai: { buysPerMonth: 2, rareCost: 2, minDelta: 2, rareCities: 3, rareGold: 1500, epicCities: 8, equipGain: 1.05, armouryMax: 30, topGens: 12,
      treasureK: 1.10,
      // §13.3 / §17.4 买装额度（ITEMS 自己的「买什么」规则，在 SG.AIBudget 的钱袋之内，INTEGRATION §5.1）：钱袋每月记
      // share × 总收入（share = Decisions.aiPurseShare 或 AIBudget.ShareByDifficulty，DESIGN-V4 §20 问题 2），其中 gearOfPurse
      // 这一份同时记作装备的额度（至多 gearCapMonths 个月）；买装同时要钱袋与这份额度。钱袋其余的留给官爵礼金（≈ 2 %）、
      // 营建（≈ 5.8 %）与节庆（≈ 0.6 %）：设计原型里营建在买装之前花钱袋，量得买装 11.2–11.4 %（世界 8.5 %）；
      // 实际的次序是 title → gear → project → festival（INTEGRATION §5.1），不设额度时买装吃掉整个钱袋（经典 48 × 25 年 15.0 %）。
      // 0.59 × 0.22 = 0.13：经典 12 × 25 年 11.8 %、世界 6 × 15 年 9.3 %；钱袋份额改了（难度、玩家决定）额度按比例跟着变。
      // gearShare：数字时直接当作「总收入的份额」（测试与对照用；null = gearOfPurse × 钱袋份额）
      gearOfPurse: 0.59, gearShare: null, gearCapMonths: 24 },
    // §14.2 图鉴与庙会
    collection: { famePerPage: 1, fameMax: 5 },
    festival: { month: 3, slots: 2, priceK: 0.9 },
    // 战斗中的光环合计上限（INTEGRATION §3.2 第 4 组）
    auraCap: 0.12,
    // 城的装备收入 / 收成项上限（与官职、营建合计后由 SG.Econ 再截断）
    itemCityCap: 0.25, itemLoyCap: 3, loyAuraTop: 95,
    get duelBudget() { const v = dec('duelBudget', 6); return typeof v === 'number' && v > 0 ? v : 6; },
    get slayRulersOnly() { return dec('slayRulersOnly', true) !== false; },
    get slayOthersK() { const v = dec('slayOthersK', 1 / 3); return typeof v === 'number' ? v : 1 / 3; },
    get classicForeignChance() { const v = dec('classicForeignChance', 0.15); return typeof v === 'number' ? v : 0.15; },
    get safeSwastikaName() { return dec('safeSwastikaName', true) !== false; },
    // DESIGN-V4 §20 问题 4 / ITEMS Q7：数据里用安全名；Decisions.safeSwastikaName = false 时显示这里的原名（只此一件）
    altNames: { paratarajas_jian: '卍纹铜剑' },
  };

  // ================================================================ 单挑白名单（COMBAT-V4 §3，INTEGRATION §8.2）==
  // 单挑只读这些装备字段。COMBAT §3 表格的行（duelWar、dHp / dDmg / dGuard、crit / critDmg、dParry、dArmor、dProc、dCombo、
  // dProjectile、dRegen、dLifesteal、dCounter、dRage、dLowHp、dDmgK、dSpd、dReach）加上 ITEMS §7.2 单挑词汇中映射到这些行的
  // 能力（dCrit → crit / critDmg 行，dGuard / dGuardBreak / dGuardBreakFull → 格挡数，dHpAdd → dHp，dEndure → 能力体力，
  // dFear → 对手的 dRage / dSpd，dSpecial → 冲刺，dSuperKnock → dProc 击退，dWeak → 弱点）。白名单之外的单挑字段载入即报错。
  const DUEL_ITEM_EFFECTS = Object.freeze({
    stats: Object.freeze(['war', 'dHp', 'dDmg', 'dGuard', 'dParry', 'crit', 'critDmg', 'dSpd', 'dReach', 'dMove', 'dGuardBreak']),
    effects: Object.freeze({
      dArmor: { on: ['heavyStart', 'lightTaken'], dmgK: [0.5, 0.7] },
      dProc: { on: ['first', 'dash', 'thrust', 'heavy', 'heavyFull', 'counter', 'comboEnd'], eff: ['knockdown', 'knockback', 'stun', 'burn'], p: [0, 1], dmgK: [0, 1.6] },
      dCombo: { bonusDmg: [0, 3], lightBonus: [0, 0.04] },
      dProjectile: { hp: [0, 4] },
      dRegen: { hpPerSec: [0, 0.8] },
      dLifesteal: { k: [0, 0.10] },
      dCounter: { k: [1, 1.7] },
      dRage: { start: [0, 0.3], gainK: [1, 1.3] },
      dLowHp: { dmgK: [1, 1.15] },
      dDmgK: { k: [0, 0.03] },
      dSpd: { k: [-6, 4] },
      dReach: { m: [0, 0.10] },
      dParry: { window: [0, 0.01], guardWindow: [0, 0.02], counterK: [1, 1.7], cdMinus: [0, 0.5] },
      dCrit: { crit: [0, 3], critDmg: [0, 30] },
      dGuard: { k: [0, 4] },
      dGuardBreak: { k: [0, 12] },
      dGuardBreakFull: { minCharge: [0.75, 1] },
      dHpAdd: { n: [0, 10] },
      dEndure: { hp: [0, 10] },
      dFear: { foeRageK: [0.7, 1], foeSpdK: [0.94, 1] },
      dSpecial: { dashDist: [1, 1.5] },
      dSuperKnock: { k: [1, 1.5] },
      dWeak: {},
    }),
    forbidden: Object.freeze(['unblockable', 'unparryable', 'dashUnblockable', 'dPerfectGuard', 'noBreak']),
  });

  // ITEMS §7.2 的能力词汇：效果编号 → 允许的参数（tests/items.js 与 tools/gen-items.js check 用）
  const EFFECTS = Object.freeze({
    active: 'kind name range power dash push morale fx radius atk def turns confuse douse pierce strikes burn splash douseSelf heal captureOnRout counter bind returns selfDef rootDays chance dot cond',
    activeChoice: 'name options', aura: 'radius morale side def atk heal bonusFor bonusAtk cond ease days move', opening: 'morale moveAll awe',
    defenderOpening: 'disableFoeRanged foeAtk', oath: 'names atk morale', charge: 'k noCounter cond allMounted', first: 'k', vsCommander: 'k', siege: 'k',
    selfAtk: 'k', outnumbered: 'atk critDmg', crit: 'crit critDmg cond', splash: 'k', trample: 'k', sunder: 'p k days', sunderAll: 'k', noCounter: '',
    counterUp: 'k', counterGuard: 'k', dmgTaken: 'attack counter fire tactic', ward: 'fire confuse tactic weather crit moraleLoss pierce',
    proc: 'on p then', focus: 'k', throwAttack: 'range perBattle k noCounter returns', hitRun: 'move', mobility: 'move ease cond', zoc: 'radius cond',
    terrain: 'terrains atk def', castle: 'def atk counterUp', sea: 'atk def', mount: 'elephant camel charge atk escape aura', antiMounted: 'k',
    regen: 'k cond', lastStand: 'below atk def', rally: 'below morale allAllies', onRout: 'atkStack cap morale side cmdMorale', allyRout: 'morale',
    guard: 'protect radius k', tacAmp: 'power hit cd fire', tacReflect: 'p', capture: 'k', escapeK: 'k', decoy: 'p partner partnerP', rescue: 'radius p',
    medic: 'k', expBoost: 'k',
    dProc: 'on p eff dmgK dps dur k', dProjectile: 'on dmg reach count guardable shape', dCombo: 'hit bonusDmg eff pairFinisher lightBonus', dCrit: 'crit critDmg',
    dGuard: 'k', dGuardBreak: 'k', dGuardBreakFull: 'minCharge', dParry: 'window guardWindow counterK cdMinus', dRegen: 'hpPerSec cond', dRage: 'start gainK',
    dEndure: 'hp rage', dLowHp: 'below dmgK', dArmor: 'on dmgK', dWeak: 'fire onAirborne', dSpecial: 'dashDist', dReach: 'm on', dLifesteal: 'k',
    dFear: 'foeRageK foeSpdK', dSpd: 'k', dDmgK: 'k', dHpAdd: 'n', dSuperKnock: 'on k',
    cmd: 'cmd kind k', free: 'cmd n', income: 'gold food', harvest: 'k', pop: 'k', loyalAura: 'n factionWide cap culture', expMonthly: 'n sameCity',
    talent: 'p', fame: 'n', virtue: 'n', dipPlus: 'k months relFloor with persuade', giftK: 'k holder', treasureHunt: 'k', scout: 'k', shopDiscount: 'cat k',
    defendCity: 'k', aggro: 'k', deterrence: 'minWar aggroK', omen: 'p loyalty', duelLure: 'accept',
  });
  const WORLD_EFFECTS = new Set(['cmd', 'free', 'income', 'harvest', 'pop', 'loyalAura', 'expMonthly', 'talent', 'fame', 'virtue', 'dipPlus', 'giftK',
    'treasureHunt', 'scout', 'shopDiscount', 'defendCity', 'aggro', 'deterrence', 'omen', 'duelLure', 'expBoost']);
  const isDuelEffect = t => /^d[A-Z]/.test(t);

  // ================================================================ 目录 ==
  let D = null;
  const ITEM = new Map();
  const SHOP_CITIES = { classic: new Map(), world: new Map() };   // 物品 → [城键]（运行时反查，商店表即其来源）
  let CHAMPION = { classic: new Set(), world: new Set() };
  function setData(data) {
    D = data || null;
    ITEM.clear();
    SHOP_CITIES.classic.clear(); SHOP_CITIES.world.clear();
    if (!D) return;
    for (const it of D.items) ITEM.set(it.id, it);
    // giftK 的两种作用范围（§7.2）：「以之赏赐」= 这件东西被赐出时价值 ×k；「以宝物赏赐」= 持有者（君主 / 使者）赐出的任何宝物 ×k（holder）。
    // tools/gen-items.js 把 holder 写进数据；直接读 items.json（check 模式）时按能力说明补上
    for (const it of D.items) if (it.ability) for (const e of it.ability.effects) if (e.t === 'giftK' && e.holder === undefined) e.holder = /以宝物/.test(it.ability.desc || '');
    for (const sid of ['classic', 'world']) {
      const shops = D.shops && D.shops[sid];
      if (!shops) continue;
      for (const key of Object.keys(shops)) {
        const s = shops[key];
        for (const k of ['common', 'commonForeign', 'uncommon', 'rare', 'epic']) for (const id of s[k] || []) {
          if (!SHOP_CITIES[sid].has(id)) SHOP_CITIES[sid].set(id, []);
          const a = SHOP_CITIES[sid].get(id); if (a.indexOf(key) < 0) a.push(key);
        }
      }
      const T = D[sid] && D[sid].tournament;
      CHAMPION[sid] = new Set(T ? T.champion || [] : []);
    }
  }
  setData(SG.ItemsData);

  const get = id => ITEM.get(id) || null;
  const itemOf = x => (typeof x === 'string' ? ITEM.get(x) : x) || null;
  const tierOf = x => { const it = itemOf(x); return it ? TIER[it.rarity] : -1; };
  const typeOf = it => (D && it && D.types[it.type]) || {};
  const isShield = it => !!it && it.slot === 'offhand' && typeOf(it).style === 'shield';
  const styleOf = it => (it && it.duel ? it.duel.style : null);
  const sidOf = g => (g && g.scenario === 'world' ? 'world' : 'classic');
  const avail = (it, sid) => (it && it.availability ? it.availability[sid || sidOf(G())] : null) || null;
  // 显示名：吉祥纹铜剑在 Decisions.safeSwastikaName = false 时显示原名「卍纹铜剑」（CFG.altNames；其余物品一律 it.name）
  const displayName = x => { const it = itemOf(x); return it ? (!CFG.safeSwastikaName && CFG.altNames[it.id] ? CFG.altNames[it.id] : it.name) : ''; };
  const valueOf = x => { const it = itemOf(x); return it ? it.value || it.price || 0 : 0; };

  // ---------------------------------------------------------------- 杂项工具 --
  function hash32(s) { s = String(s); let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; }
  // 种子随机数（不取 SG.Random，INTEGRATION §1.2；v3 的随机数流不受影响）
  function rng() { const g = G(); return SG.SeededRandom(hash32((g ? g.seed : 0) + '|' + (g ? g.monthIndex : 0) + '|items|' + Array.prototype.join.call(arguments, '|'))); }
  const round10 = v => Math.round(v / 10) * 10;
  const now = () => (typeof performance !== 'undefined' && performance && performance.now ? performance.now() : Date.now());
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const isRuler = x => { const g = G(); return !!(g && x && x.faction >= 0 && g.factions[x.faction] && g.factions[x.faction].ruler === x.id); };

  // ================================================================ 状态与实例 ==
  const S = () => { const g = G(); return g && g.items; };
  const emptyGear = () => ({ weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null });
  function ensure(g) {
    if (!g.items) g.items = { next: 1, inst: {}, hiddenFound: [], singletonsGiven: [], tourneyAwarded: [], cooldown: {}, seen: {}, owned: {}, clues: {}, aggro: {}, aiGear: {} };
    const s = g.items;
    for (const k of ['hiddenFound', 'singletonsGiven', 'tourneyAwarded']) if (!Array.isArray(s[k])) s[k] = [];
    for (const k of ['inst', 'cooldown', 'seen', 'owned', 'clues', 'aggro', 'aiGear']) if (!s[k] || typeof s[k] !== 'object') s[k] = {};
    if (!(s.next > 0)) s.next = 1;
    for (const x of g.generals) if (!x.gear || typeof x.gear !== 'object') x.gear = emptyGear();
    for (const f of g.factions) if (!Array.isArray(f.armoury)) f.armoury = [];
    for (const c of g.cities) if (!c.shop || typeof c.shop !== 'object') c.shop = { stock: [], merchant: null };
  }
  // 世上每件装备的实例数（不含商店货架）；读档、换局时重建
  let IDX = { g: null, count: new Map() };
  function idx() {
    const g = G();
    if (IDX.g !== g || !IDX.count) {
      IDX = { g, count: new Map() };
      const s = g && g.items;
      if (s) for (const u of Object.keys(s.inst)) { const id = s.inst[u].id; IDX.count.set(id, (IDX.count.get(id) || 0) + 1); }
    }
    return IDX;
  }
  const count = id => idx().count.get(id) || 0;
  // 缓存失效：每名武将自己的版本号（换装时 +1）与全局纪元（读档、测试改装备时 +1）
  let EPOCH = 1;
  const GV = new WeakMap();
  const touch = x => { if (x) GV.set(x, (GV.get(x) || 0) + 1); };
  const bump = () => { EPOCH++; };
  const instRec = uid => { const s = S(); return s && uid != null ? s.inst[uid] || null : null; };

  function detach(uid) {
    const g = G(), r = instRec(uid);
    if (!r) return;
    if (r.loc === 'gen') { const x = g.generals[r.owner]; if (x && x.gear) for (const sl of SLOTS) if (x.gear[sl] === uid) x.gear[sl] = null; touch(x); }
    else if (r.loc === 'arm') { const f = g.factions[r.owner]; if (f && f.armoury) { const i = f.armoury.indexOf(uid); if (i >= 0) f.armoury.splice(i, 1); } }
    else if (r.loc === 'city') { const c = g.cities[r.owner]; if (c && c.vault) { const i = c.vault.indexOf(uid); if (i >= 0) c.vault.splice(i, 1); if (!c.vault.length) delete c.vault; } }
    r.loc = null; r.owner = -1;
  }
  function place(uid, loc, owner) {
    const g = G(), r = instRec(uid);
    if (!r) return false;
    if (r.loc) detach(uid);
    r.loc = loc; r.owner = owner;
    if (loc === 'gen') {
      const x = g.generals[owner], it = ITEM.get(r.id);
      if (!x.gear) x.gear = emptyGear();
      x.gear[it.slot] = uid; touch(x);
      if (x.faction === g.player) markOwned(r.id);
    } else if (loc === 'arm') {
      const f = g.factions[owner]; if (!f.armoury) f.armoury = []; f.armoury.push(uid);
      if (owner === g.player) markOwned(r.id);
    } else if (loc === 'city') {
      const c = g.cities[owner]; if (!c.vault) c.vault = []; c.vault.push(uid);
    }
    return true;
  }
  function mkInst(id, loc, owner, extra) {
    const s = S(), it = ITEM.get(id);
    if (!s || !it) return null;
    const uid = s.next++;
    s.inst[uid] = Object.assign({ id, loc: null, owner: -1, gifted: null, giftLoy: 0, bound: false, since: G().monthIndex }, extra || {});
    const X = idx(); X.count.set(id, (X.count.get(id) || 0) + 1);
    if (TIER[it.rarity] >= 3 && s.singletonsGiven.indexOf(id) < 0) s.singletonsGiven.push(id);
    place(uid, loc, owner);
    return uid;
  }
  function destroy(uid) {
    const s = S(), r = instRec(uid);
    if (!r) return;
    detach(uid);
    delete s.inst[uid];
    const X = idx(); X.count.set(r.id, Math.max(0, (X.count.get(r.id) || 0) - 1));
  }
  function markSeen(id) { const s = S(); if (s && ITEM.has(id)) s.seen[id] = 1; }
  function markOwned(id) { const s = S(); if (s && ITEM.has(id)) { s.owned[id] = 1; s.seen[id] = 1; } }
  function instView(uid) {
    const r = instRec(uid);
    if (!r) return null;
    return Object.assign({ uid: +uid, item: ITEM.get(r.id) || null }, r);
  }
  function genOf(uid) { const r = instRec(uid); return r && r.loc === 'gen' ? G().generals[r.owner] : null; }
  // 势力 f 能调配的实例：府库 + 己方城的镇城之宝 / 遗物（vault）
  function armouryOf(f, opts) {
    const g = G(); if (!g || f < 0 || !g.factions[f]) return [];
    const out = (g.factions[f].armoury || []).slice();
    if (!opts || opts.vault !== false) for (const c of g.cities) if (c.owner === f && c.vault) for (const u of c.vault) out.push(u);
    return out;
  }
  // 实例归属的势力（−1：在野武将身上或无主城的库）
  function factionOfInst(uid) {
    const g = G(), r = instRec(uid);
    if (!r) return -1;
    if (r.loc === 'gen') return g.generals[r.owner].faction;
    if (r.loc === 'arm') return r.owner;
    if (r.loc === 'city') return g.cities[r.owner].owner;
    return -1;
  }
  function stash(f, uid) {   // 交给势力 f 的府库；f < 0 时留在原处
    if (f >= 0 && G().factions[f] && G().factions[f].alive !== false) place(uid, 'arm', f);
  }

  // ================================================================ 每名武将的装备数值 ==
  // 要求（§2.2，按基础属性）：'ok' | 'half'（不足，数值减半）| 'none'（不起作用）| 'polOnly'（传国玉玺在非君主手上：只给政治 +10）
  function reqState(x, it, weaponStyle) {
    const r = it.req;
    if (!r) return 'ok';
    if (r.weaponStyle === 'one' && weaponStyle !== 'one') return 'none';
    if (r.ruler && !isRuler(x)) return r.notRuler === 'polOnly' ? 'polOnly' : 'none';
    for (const k of ['war', 'intel', 'pol']) if (r[k] && (x[k] | 0) < r[k]) return r.below === 'half' ? 'half' : 'none';
    return 'ok';
  }
  function reqWhy(x, it, weaponStyle) {
    const r = it.req || {};
    if (r.weaponStyle === 'one' && weaponStyle !== 'one') return '盾须配单手兵器';
    const nm = { war: '武力', intel: '智力', pol: '政治' };
    for (const k of ['war', 'intel', 'pol']) if (r[k] && (x[k] | 0) < r[k]) return '需' + nm[k] + ' ' + r[k] + (r.below === 'half' ? '（不足时数值减半）' : '');
    if (r.ruler && !isRuler(x)) return '须由君主佩持' + (r.notRuler === 'polOnly' ? '（否则只有政治 +10）' : '');
    return '';
  }
  const HALF_KEYS = ['k', 'atk', 'def', 'crit', 'critDmg', 'morale', 'power', 'gold', 'food', 'n'];
  function halfEffect(e) {
    const o = Object.assign({}, e, { half: true });
    if (e.t === 'escapeK') { o.k = 1 - 0.5 * (1 - (e.k || 0)); return o; }
    for (const k of HALF_KEYS) if (typeof o[k] === 'number' && !(e.t === 'active' && k === 'power')) o[k] = k === 'n' ? Math.round(o[k] / 2) : o[k] / 2;
    return o;
  }
  const cache = new WeakMap();
  const EMPTY_CALC = Object.freeze({ key: '', sums: {}, ease: [], hard: [], escape: 0, abil: [], byT: {}, list: [], style: null });
  function calc(x) {
    if (!x || !x.gear) return EMPTY_CALC;
    const ru = isRuler(x);
    let c = cache.get(x);
    const ver = GV.get(x) | 0;
    if (c && c.ver === ver && c.ep === EPOCH && c.war === x.war && c.intel === x.intel && c.pol === x.pol && c.ruler === ru && c.g === SG.G) return c;
    if (!S()) return EMPTY_CALC;
    c = { ver, ep: EPOCH, war: x.war, intel: x.intel, pol: x.pol, ruler: ru, g: SG.G, sums: {}, ease: [], hard: [], escape: 0, abil: [], byT: {}, list: [], style: null };
    const wr = instRec(x.gear.weapon), w = wr && ITEM.get(wr.id);
    c.style = w && w.duel ? w.duel.style : null;
    for (const sl of SLOTS) {
      const uid = x.gear[sl]; if (uid == null) continue;
      const r = instRec(uid); const it = r && ITEM.get(r.id); if (!it) continue;
      const rs = reqState(x, it, c.style);
      c.list.push({ slot: sl, uid, id: it.id, item: it, req: rs });
      if (rs === 'none') continue;
      const k = rs === 'half' ? 0.5 : 1;
      for (const [s, v] of Object.entries(it.stats || {})) {
        if (rs === 'polOnly' && s !== 'pol') continue;
        if (Array.isArray(v)) { const arr = s === 'hard' ? c.hard : c.ease; for (const t of v) if (arr.indexOf(t) < 0) arr.push(t); continue; }
        if (typeof v !== 'number') continue;
        if (s === 'escape') { c.escape = Math.max(c.escape, v * k); continue; }
        c.sums[s] = (c.sums[s] || 0) + v * k;
      }
      if (rs === 'polOnly' || !it.ability) continue;
      for (const e0 of it.ability.effects) {
        const e = Object.assign(rs === 'half' ? halfEffect(e0) : Object.assign({}, e0), { item: it.id, slot: sl });
        c.abil.push(e);
        (c.byT[e.t] = c.byT[e.t] || []).push(e);
      }
    }
    cache.set(x, c);
    return c;
  }
  const capOf = k => { const sk = D && D.statKeys[k]; return sk && typeof sk.cap === 'number' ? sk.cap : Infinity; };
  // 某属性在全部装备上的合计（物品单位：% / pp / + / s / m），按 statKeys 的上限截断
  function raw(x, k) {
    const c = calc(x);
    if (k === 'escape') return Math.min(100, c.escape);
    const v = c.sums[k] || 0;
    if (k === 'mob') return clamp(v, -1, 2);
    if (k === 'dSpd') return clamp(v, CFG.duel.spdLo, CFG.duel.spdHi);
    const cap = capOf(k);
    return v > cap ? cap : v;
  }
  const abil = (x, t) => calc(x).byT[t] || [];
  const maxOf = (list, f, d) => { let m = d === undefined ? 0 : d; for (const e of list) { const v = typeof f === 'function' ? f(e) : e[f]; if (typeof v === 'number' && v > m) m = v; } return m; };
  const cmdK = (x, cmd, kind) => { let v = 0; for (const e of abil(x, 'cmd')) if (e.cmd === cmd && (!kind || !e.kind || e.kind === kind)) v = Math.max(v, e.k || 0); return v; };
  function eff(x, k) {
    if (!x) return 0;
    const base = x[k] | 0;
    if (k !== 'war' && k !== 'intel' && k !== 'pol') return base;
    return Math.min(CFG.attrCap, base + Math.min(CFG.gearAttrCap, raw(x, k)));
  }
  const injuryWar = x => { const g = G(), j = x && x.injury; return j && g && j.until > g.monthIndex ? (j.war | 0) : 0; };
  function duelWar(x) { return Math.min(CFG.attrCap, (x.war | 0) + Math.min(CFG.duelWarCap, Math.floor(raw(x, 'war') / CFG.duelWarDiv))) - injuryWar(x); }
  // 无条件的会心能力（cond 缺省；cond: charge / vsCommander / … 由战斗按时机加）
  function critAbil(x, key, conds) { let v = 0; for (const e of abil(x, 'crit')) if (!e.cond || (conds && conds[e.cond])) v = Math.max(v, e[key] || 0); return v; }
  function stat(x, key) {
    switch (key) {
      case 'critArmy': return Math.min(0.04, (raw(x, 'crit') + critAbil(x, 'crit')) / 100);
      case 'critDuel': return Math.min(CFG.duel.critPP / 100, (raw(x, 'crit') + maxOf(abil(x, 'dCrit'), 'crit')) / 100);
      case 'critMulArmy': return Math.min(CFG.duel.critMul, (raw(x, 'critDmg') + critAbil(x, 'critDmg')) / 100);
      case 'critMulDuel': return Math.min(CFG.duel.critMul, (raw(x, 'critDmg') + maxOf(abil(x, 'dCrit'), 'critDmg')) / 100);
      default: return raw(x, key);
    }
  }
  // 单挑能力折算的体力（INTEGRATION §8.3：一名武将全部单挑能力 ≤ 4 点；估算表，section (15) 实测）
  function abilityHp(x) {
    let hp = 0;
    for (const e of calc(x).abil) {
      if (!isDuelEffect(e.t)) continue;
      switch (e.t) {
        case 'dEndure': hp += Math.min(4, (e.hp || 0) * 0.5 + 1); break;
        case 'dRegen': hp += Math.min(3, (e.hpPerSec || 0) * (e.cond === 'always' ? 8 : 4)); break;
        case 'dLifesteal': hp += (e.k || 0) * 25; break;
        case 'dProjectile': hp += Math.min(4, (e.dmg || 0) * (e.count || 1) * 0.5); break;
        case 'dCombo': hp += (e.bonusDmg || 0) * 0.5 + (e.lightBonus || 0) * 20; break;
        case 'dProc': hp += (e.dmgK && e.dmgK > 1 ? (e.dmgK - 1) * 3 : 0.5) * (e.p === undefined ? 1 : Math.min(1, e.p * 2)); break;
        case 'dLowHp': hp += ((e.dmgK || 1) - 1) * 10; break;
        case 'dArmor': hp += (1 - (e.dmgK || 1)) * 4; break;
        case 'dRage': hp += 0.5; break;
        case 'dFear': hp += 0.5; break;
        case 'dWeak': hp -= 0.5; break;
        default: hp += 0.25;
      }
    }
    return clamp(hp, 0, CFG.duel.abilityHp);
  }
  // SG.Gear.duelMods（ITEMS §3.3，INTEGRATION §8.3）：单挑只经此读装备（白名单字段），并受全合一预算约束
  function duelMods(x, foe) {
    const d = CFG.duel, c = calc(x), Mo = SG.Mods;
    const wr = x && x.gear ? instRec(x.gear.weapon) : null, w = wr && ITEM.get(wr.id);
    const weapon = w && w.duel ? { key: w.duel.key, style: w.duel.style, reach: w.duel.reach, pow: w.duel.pow, item: w.id, newKey: !!w.duel.newKey } : null;
    const baseReach = weapon && D.duelWeapons[weapon.key] ? D.duelWeapons[weapon.key].reach : (weapon ? weapon.reach : 1.8);
    const dReach = Math.min(capOf('dReach'), raw(x, 'dReach')) + maxOf(abil(x, 'dReach'), 'm');
    const reachK = Math.min(d.reachK, dReach / Math.max(0.1, baseReach));
    // 伤害项：装备 + 官职 dmgDuel + 宿敌（ENGAGEMENT），合计 ≤ 6 %，装备 ≤ 3 %
    const gearDmg = Math.min(d.dmg, raw(x, 'dDmg') + maxOf(abil(x, 'dDmgK'), 'k') * 100);
    const td = Mo ? Mo.z('titleDmgDuel', x) : 0, titleDmg = td > 1 ? td : td * 100;
    const rivalDmg = Mo && foe ? Mo.z('rivalDmgDuel', x, foe) * 100 : 0;
    let dmg = Math.min(d.dmgAll, gearDmg + titleDmg + rivalDmg);
    let gearHp = Math.min(d.gearHp, raw(x, 'dHp') + maxOf(abil(x, 'dHpAdd'), 'n'));
    const aHp = abilityHp(x);
    let guardPP = Math.min(d.guardPP, raw(x, 'dGuard'));
    const guardAb = maxOf(abil(x, 'dGuard'), 'k');
    // 全合一预算：dmg + 0.4 × (HP + 能力体力 + 0.5 × 格挡) ≤ B；超出时先削体力、再削伤害（INTEGRATION §8.3 的次序），
    // 两样削完仍超出（只剩格挡）才削格挡——确定的次序，不取随机数
    const B = CFG.duelBudget;
    const total = () => dmg + d.hpPerPP * (gearHp + aHp + d.guardHpK * (guardPP + guardAb));
    let over = total() - B;
    if (over > 1e-9) { const cut = Math.min(gearHp, over / d.hpPerPP); gearHp -= cut; over = total() - B; }
    if (over > 1e-9) { dmg = Math.max(0, dmg - over); over = total() - B; }
    if (over > 1e-9) { const cut = Math.min(guardPP, over / (d.hpPerPP * d.guardHpK)); guardPP -= cut; over = total() - B; }
    const spd = (raw(x, 'dSpd') + maxOf(abil(x, 'dSpd'), 'k')) / 100;
    const move = Math.min(d.move, raw(x, 'dMove')) / 100;
    const guardMax = guardAb > 0 ? d.guardAbsMax : d.guardMax;
    const guard = Math.min(guardMax, d.guardBase + (guardPP + guardAb) / 100);
    const parry = Math.min(d.parry, raw(x, 'dParry') + maxOf(abil(x, 'dParry'), 'window'));
    const guardBreak = Math.min(d.guardBreak, raw(x, 'dGuardBreak') + maxOf(abil(x, 'dGuardBreak'), 'k')) / 100;
    const titleCrit = Mo ? Mo.z('titleCritDuel', x) : 0;
    const critDuel = Math.min(d.critPP / 100, stat(x, 'critDuel') + titleCrit);
    const abilities = c.abil.filter(e => isDuelEffect(e.t) && DUEL_ITEM_EFFECTS.effects[e.t]);
    return {
      war: duelWar(x), weapon, reachK, dmg: Math.max(0, dmg) / 100, hp: 100 + Math.max(0, gearHp), abilityHp: aHp,
      guard, guardPP: guardPP + guardAb, guardBreak, parry, spd: clamp(spd, d.spdLo / 100, d.spdHi / 100), move,
      critDuel, critMulDuel: stat(x, 'critMulDuel'), abilities,
      budget: { limit: B, used: Math.round(total() * 1000) / 1000, gearHp, abilityHp: aHp, guardPP: guardPP + guardAb, dmg: Math.max(0, dmg) },
      lure: abil(x, 'duelLure').length > 0,
    };
  }

  // ================================================================ 提供者（INTEGRATION §3.3）==
  const Items = { enabled: true };
  const on = () => Items.enabled !== false && !!S();
  const BATTLE_ONLY = new Set(['aura', 'opening', 'defenderOpening', 'oath']);
  function auraSum(u, model, key) {
    if (!u || !model || !Array.isArray(model.units)) return 0;
    let pos = 0, neg = 0;
    const dist = (a, b) => Math.abs((a.x | 0) - (b.x | 0)) + Math.abs((a.y | 0) - (b.y | 0));
    for (const v of model.units) {
      if (!v || v.alive === false || !v.gen) continue;
      const list = abil(v.gen, 'aura');
      if (!list.length) continue;
      for (const e of list) {
        const val = e[key]; if (!(typeof val === 'number' && val !== 0)) continue;
        const foeAura = e.side === 'foe';
        if (foeAura ? v.side === u.side : v.side !== u.side) continue;
        if (dist(u, v) > (e.radius || 0)) continue;
        if (e.cond === 'mounted' && !(u.gen && u.gen.gear && u.gen.gear.mount != null)) continue;
        if (e.cond === 'cultureHan' && !(u.gen && (u.gen.culture || 'han') === 'han')) continue;
        let k = val;
        if (Array.isArray(e.bonusFor) && u.gen && e.bonusFor.indexOf(u.gen.name) >= 0 && key === 'atk') k += e.bonusAtk || 0;
        if (k > 0) pos = Math.max(pos, k); else neg = Math.min(neg, k);
      }
    }
    return Math.min(CFG.auraCap, pos) + neg;
  }
  // 自动结算（Conquest.autoResolve）的战力：v3 的 power()，武力取有效值，× (1 + (攻 + 防) / 250)（ITEMS §3.2）
  //   守方（正在自动结算的这一仗的守军）另 × (1 + 坐镇守城 defendCity) × (1 + 守城 castle.def)：自动结算里只有这两种能力算（§3.2、§7.1）
  let sortie = null;   // Conquest.prepare 发出的最近一仗（conquest.prepare 事件记下，结算完清掉）
  function autoPower(x) {
    const Mo = SG.Mods;
    const war = Mo ? Mo.eff(x, 'war') : eff(x, 'war');
    let k = 1 + (Math.min(40, raw(x, 'atk')) + Math.min(45, raw(x, 'def'))) / 250;
    if (sortie && sortie.def && sortie.def.indexOf(x) >= 0) k *= (1 + defendCityK(sortie.target)) * (1 + maxOf(abil(x, 'castle'), 'def'));
    return x.troops * (0.55 + war / 220) * (0.7 + x.training / 330) * k;
  }
  // 电脑的目标排序（StrategyAI：守军 × (1 + k)，k 越小越想打）：怀璧（aggro）× k、威慑 × 0.85、他国君主持神品 / 天宝 × 1.10（§3.5、§13.6）
  let aggroCache = { mi: -1, g: null, holder: new Map(), war90: new Map(), map: new Map() };
  function holderInfo(holder) {
    const g = G();
    let h = aggroCache.holder.get(holder);
    if (h) return h;
    h = { a: 1, det: null };
    const fa = g.factions[holder];
    const r = fa && fa.ruler >= 0 ? g.generals[fa.ruler] : null;
    if (r) { h.a *= Math.max(1, maxOf(abil(r, 'aggro'), 'k', 1)); if (calc(r).list.some(e => TIER[e.item.rarity] >= 3)) h.a *= CFG.ai.treasureK; }
    for (const x of g.generals) if (x.faction === holder && !x.dead && x.gear) for (const e of abil(x, 'deterrence')) if (!h.det || e.aggroK < h.det.aggroK) h.det = e;
    aggroCache.holder.set(holder, h);
    return h;
  }
  function hasWar(f, minWar) {
    const k = f + '|' + minWar;
    if (aggroCache.war90.has(k)) return aggroCache.war90.get(k);
    const v = G().generals.some(x => x.faction === f && !x.dead && x.war >= minWar);
    aggroCache.war90.set(k, v);
    return v;
  }
  function attraction(f, holder) {
    const g = G();
    if (aggroCache.g !== g || aggroCache.mi !== g.monthIndex) aggroCache = { mi: g.monthIndex, g, holder: new Map(), war90: new Map(), map: new Map() };
    const key = f * 4096 + holder;
    const hit = aggroCache.map.get(key);
    if (hit !== undefined) return hit;
    const h = holderInfo(holder);
    let a = h.a;
    if (h.det && !hasWar(f, h.det.minWar || 90)) a *= h.det.aggroK || 0.85;
    const s = S(); const until = s && s.aggro[f + '>' + holder];
    if (until && until > g.monthIndex) a *= CFG.confiscate.aggroK;   // 缴械释放：被羞辱者的势力更想打你
    aggroCache.map.set(key, a);
    return a;
  }
  function itemCity(c, key) {
    const Mo = SG.Mods, a = Mo ? Mo.admin(c) : (c.governor >= 0 ? G().generals[c.governor] : null);
    if (!a) return 0;
    let v = 0;
    if (key === 'income') v = maxOf(abil(a, 'income'), 'gold');
    else if (key === 'harvest') v = Math.max(maxOf(abil(a, 'harvest'), 'k'), maxOf(abil(a, 'income'), 'food'));
    return Math.min(CFG.itemCityCap, v);
  }
  // 忠诚光环（§3.5，INTEGRATION §3.4）：提供者 itemLoyAura 只报「普通」的正光环（主政者的同城光环、君主的全势力光环、loyal 属性），
  // 由 SG.Mods.loyAura 与官职光环取较大者（≤ 95）。三种特别的在本模块的 loyAura 相钩子里另算（loyAuraSpecial）：
  //   负光环（交州刺史节 −1：同城武将每月 −1，不被正光环抵消）、带上限的（军人公民权铜板：至 90 为止）、限文化的全势力光环（滇王之印：南中武将）
  const plainAura = e => (e.n || 0) > 0 && !e.culture && !(typeof e.cap === 'number' && e.cap < CFG.loyAuraTop);
  function itemLoyAura(c) {
    const g = G(), Mo = SG.Mods, a = Mo ? Mo.admin(c) : null;
    let v = 0;
    if (a) { for (const e of abil(a, 'loyalAura')) if (!e.factionWide && plainAura(e)) v = Math.max(v, e.n); v = Math.max(v, raw(a, 'loyal')); }
    if (c.owner >= 0) { const r = g.ruler(c.owner); if (r) for (const e of abil(r, 'loyalAura')) if (e.factionWide && plainAura(e)) v = Math.max(v, e.n); }
    return Math.min(CFG.itemLoyCap, v);
  }
  function loyAuraSpecial(ctx) {
    const g = G(), Mo = SG.Mods;
    if (!on() || !Mo) return;
    const top = (Mo.CAPS && Mo.CAPS.loyAuraCap) || CFG.loyAuraTop;
    for (const c of g.cities) {
      if (c.owner < 0) continue;
      const a = Mo.admin(c), r = g.ruler(c.owner);
      const local = a ? abil(a, 'loyalAura').filter(e => !e.factionWide && !plainAura(e)) : [];
      const wide = r ? abil(r, 'loyalAura').filter(e => e.factionWide && !plainAura(e)) : [];
      if (!local.length && !wide.length) continue;
      const la = Math.max(0, Mo.loyAura(c));   // 第 5 相已经加过的
      for (const o of g.officersIn(c)) {
        if (g.isRuler(o)) continue;
        let pos = 0, cap = top, neg = 0;
        for (const e of local.concat(wide)) {
          if (e.culture && (o.culture || 'han') !== e.culture) continue;
          const n = e.n || 0;
          if (n < 0) { neg = Math.min(neg, n); continue; }
          if (n > pos) { pos = n; cap = Math.min(top, typeof e.cap === 'number' ? e.cap : top); }
        }
        if (pos > la && o.loyalty < cap) o.loyalty = Math.min(cap, o.loyalty + Math.min(CFG.itemLoyCap, pos) - la);
        if (neg < 0) o.loyalty = Math.max(0, o.loyalty + neg);
      }
    }
  }
  function bestInCity(c, fn) { let b = 0; for (const x of G().officersIn(c)) b = Math.max(b, fn(x)); return b; }
  const PROVIDERS = {
    eff: (x, k) => eff(x, k),
    gearWar: x => raw(x, 'war'),
    gearAtk: x => raw(x, 'atk') / 100,
    gearDef: x => raw(x, 'def') / 100,
    gearCrit: x => stat(x, 'critArmy'),
    gearCritMul: x => stat(x, 'critMulArmy'),
    // 会心的条件项与抗会心（battle-math 的 rollCrit / critChance 接线前为 0 影响；INTEGRATION 请求）
    gearCritCond: (x, ctx) => (ctx ? Gear.battle.critAdd(x, ctx) : 0),
    gearCritMulCond: (x, ctx) => (ctx ? Gear.battle.critMulAdd(x, ctx) : 0),
    gearCritRes: x => Gear.battle.critRes(x),
    gearCritWard: x => Gear.battle.critWard(x),
    gearMorale: x => raw(x, 'morale'),
    auraAtk: (u, model) => auraSum(u, model, 'atk'),
    auraDef: (u, model) => auraSum(u, model, 'def'),
    gearDev: (x, key) => { const st = key === 'land' ? 'land' : key === 'industry' ? 'ind' : 'town'; return Math.min(0.60, raw(x, st) / 100 + cmdK(x, 'develop', st)); },
    gearRecruit: x => Math.min(0.40, (raw(x, 'recruit') + cmdK(x, 'recruit')) / 100),
    gearTrain: x => Math.min(10, raw(x, 'train') + cmdK(x, 'train')),
    gearSearch: x => raw(x, 'search') / 100,
    gearHire: x => Math.min(0.20, (raw(x, 'hire') + cmdK(x, 'hire')) / 100),
    gearDip: x => Math.min(0.25, (raw(x, 'dip') + maxOf(abil(x, 'dipPlus'), 'k')) / 100),
    gearDiscord: x => Math.min(0.20, (raw(x, 'discord') + cmdK(x, 'discord')) / 100),
    gearPersuade: x => Math.min(0.20, (raw(x, 'persuade') + cmdK(x, 'persuade') + maxOf(abil(x, 'dipPlus'), 'persuade')) / 100),
    expBoost: x => Math.min(0.40, raw(x, 'exp') / 100 + maxOf(abil(x, 'expBoost'), 'k')),
    gearFame: f => { const g = G(), r = f >= 0 ? g.ruler(f) : null; let v = r ? Math.min(20, raw(r, 'fame') + maxOf(abil(r, 'fame'), 'n')) : 0; if (f === g.player) v += collectionFame(); return v; },
    gearVirtue: f => { const r = f >= 0 ? G().ruler(f) : null; return r ? Math.min(12, raw(r, 'virtue') + maxOf(abil(r, 'virtue'), 'n')) : 0; },
    gearReward: x => Math.min(10, raw(x, 'reward') + cmdK(x, 'reward')),
    tradeK: c => bestInCity(c, x => (raw(x, 'trade') + cmdK(x, 'trade')) / 100),
    allyMonths: (f, t, envoy) => { const r = f >= 0 ? G().ruler(f) : null; return Math.max(r ? maxOf(abil(r, 'dipPlus'), 'months') : 0, envoy ? maxOf(abil(envoy, 'dipPlus'), 'months') : 0); },
    itemCity: (c, key) => itemCity(c, key),
    itemLoyAura: c => itemLoyAura(c),
    autoPower: x => autoPower(x),
    aiTargetK: (f, t) => (t && t.owner >= 0 && t.owner !== f ? 1 / attraction(f, t.owner) - 1 : 0),
  };
  let providersOff = null;
  function installProviders() {
    if (providersOff || !SG.Mods || typeof SG.Mods.provide !== 'function') return;
    providersOff = SG.Mods.provide(PROVIDERS, { owner: 'items', when: on });
  }
  installProviders();

  // ================================================================ 商店（§6）==
  const shopRow = (c, sid) => { const T = D && D.shops[sid || sidOf(G())]; return T ? T[c.key] || null : null; };
  function cityK(c) { return Math.round(clamp(1.2 - ((c.industry | 0) + (c.town | 0)) / 2500, 0.85, 1.15) * 100) / 100; }
  // Econ.shopTier 只看 产业 + 城镇 与城名（INTEGRATION §12 的 SHOP.sum 门槛、HUBS 名单）：按 (城, 产业 + 城镇) 记住，每月补货不必重查名单
  const tierMemo = new WeakMap();
  function shopTier(c) {
    const E = SG.Econ;
    if (E && typeof E.shopTier === 'function' && SG.Balance && SG.Balance.CityLevel) {
      const v = c.industry + c.town, CL = SG.Balance.CityLevel;
      const m = tierMemo.get(c);
      if (m && m.v === v && m.cl === CL && m.name === c.name) return m.t;
      const t = E.shopTier(c);
      tierMemo.set(c, { v, cl: CL, name: c.name, t });
      return t;
    }
    const row = shopRow(c); return row ? row.tier : 1;
  }
  // 庙会（§14.2）：每年 3 月，有主之城的市场 +2 个货位、本势力买价 −10 %。month 缺省 = 当月（买价、货位显示）；
  // 补货在月末（第 2 相，g.month 还是刚结束的那个月）为「下个月」备货，所以 restock 传入下个月（2 月末为 3 月备 +2，3 月末收摊）
  const festivalOn = (c, month) => { const g = G(); return !!(g && (month === undefined ? g.month : month) === CFG.festival.month && c.owner >= 0); };
  const nextMonth = g => (g.month % 12) + 1;
  function slotsOf(c) { return CFG.slotsByTier[clamp(shopTier(c), 1, 4)] + (festivalOn(c) ? CFG.festival.slots : 0); }
  function basePrice(it, sid) { const a = avail(it, sid); return (a && a.price) || it.price || it.value || 100; }
  function originOf(it, c, sid) {
    const a = avail(it, sid);
    if (a && a.foreign) return 'foreign';
    const row = shopRow(c, sid);
    if (row && it.rarity === 'common' && (row.commonForeign || []).indexOf(it.id) >= 0) return 'foreign';
    if (row && it.culture !== row.culture && it.rarity === 'uncommon') return 'foreign';
    return 'native';
  }
  function listPrice(it, c, origin) { return round10(basePrice(it) * cityK(c) * (CFG.originK[origin] || 1)); }
  // 在 c 的市场里，势力 f 买 entry 的价格（§4.3：城价 × 来源 × 同盟 × (1 − 商才折扣) × 庙会）
  // 本城的商才折扣与持有者商店折扣（entryPrice 用；dc 可由调用者缓存）
  function cityDiscount(c, offs) {
    const out = { disc: 0, shop: [] };
    for (const x of offs || G().officersIn(c)) {
      out.disc = Math.max(out.disc, (raw(x, 'trade') + cmdK(x, 'trade')) / CFG.discDiv);
      for (const ab of abil(x, 'shopDiscount')) out.shop.push(ab);
    }
    out.disc = Math.min(CFG.discCap, out.disc);
    return out;
  }
  function entryPrice(c, e, f, dc) {
    const it = ITEM.get(e.id); if (!it) return Infinity;
    const origin = e.merchant ? 'merchant' : e.foreign ? 'foreign' : 'native';
    let p = basePrice(it) * cityK(c) * (CFG.originK[origin] || 1);
    if (f >= 0 && c.owner >= 0 && c.owner !== f) p *= CFG.alliedK;
    // 商才：本城（买方的城）最好的武将的交易 / 200，≤ 12.5 %；持有者所在城的商店折扣（乌兹钢锭：兵器 −10 %）
    if (f >= 0 && c.owner === f) {
      const d = dc || cityDiscount(c);
      let shopD = 0;
      for (const ab of d.shop) if (!ab.cat || ab.cat === it.cat || ab.cat === it.slot) shopD = Math.max(shopD, ab.k || 0);
      p *= (1 - d.disc) * (1 - shopD);
    }
    if (festivalOn(c) && f === c.owner) p *= CFG.festival.priceK;
    return Math.max(10, round10(p));
  }
  function sellPrice(x, c) {
    const id = typeof x === 'number' || (typeof x === 'string' && /^\d+$/.test(x)) ? (instRec(x) || {}).id : x;
    const it = ITEM.get(id); if (!it) return 0;
    const g = G(), sid = sidOf(g);
    if (!sellable(it, typeof x === 'number' ? instRec(x) : null).ok) return 0;
    const a = avail(it, sid);
    const k = a && a.kind === 'tournament' ? CFG.sellTourneyK : (D.rarities[it.rarity].sellK || 0.4);
    return round10((it.price || it.value || 0) * k * (c ? cityK(c) : 1));
  }
  function sellable(it, r) {
    const g = G(), sid = sidOf(g), a = avail(it, sid);
    if (!it) return { ok: false, why: '无此物' };
    if (TIER[it.rarity] >= 3) return { ok: false, why: '此物不可变卖（神品 / 天宝）' };
    if (r && r.bound) return { ok: false, why: '此物不可变卖（专属兵器）' };
    if (a && a.kind === 'signature') return { ok: false, why: '此物不可变卖（名将旧物）' };
    if (a && a.kind === 'ruler') return { ok: false, why: '此物不可变卖（君主专属）' };
    if (a && a.kind === 'cityTreasure') return { ok: false, why: '此物不可变卖（镇城之宝）' };
    return { ok: true };
  }
  // 补货的种子：每月一个基数（G.seed、月序），每城以整数混合（不必每城再算一次字符串散列）
  let rsBase = { g: null, mi: -1, h: 0 };
  function restockSeed(g, cid) {
    if (rsBase.g !== g || rsBase.mi !== g.monthIndex) rsBase = { g, mi: g.monthIndex, h: hash32((g.seed | 0) + '|' + g.monthIndex + '|items|restock') };
    let h = (rsBase.h ^ Math.imul(cid + 1, 0x9E3779B1)) >>> 0;
    h = Math.imul(h ^ (h >>> 16), 0x85EBCA6B) >>> 0;
    return (h ^ (h >>> 13)) | 0;
  }
  // 补货（§6.4）：种子随机数（G.seed、城、月），不取 SG.Random
  function restock(c, first) {
    const g = G(), sid = sidOf(g), row = shopRow(c, sid);
    if (!row) return [];
    if (!c.shop) c.shop = { stock: [], merchant: null };
    const st = c.shop.stock, R = CFG.restock, k = SG.SeededRandom(restockSeed(g, c.id));
    const added = [];
    const stockMonth = first ? g.month : nextMonth(g), fest = festivalOn(c, stockMonth);
    if (!first) for (const e of st) e.age = (e.age | 0) + 1;
    for (let i = st.length - 1; i >= 0; i--) {
      const e = st[i], it = ITEM.get(e.id);
      if (!it) { st.splice(i, 1); continue; }
      const t = TIER[it.rarity];
      if (t <= 1 && e.age >= R.ageOld && k.nextDouble() < R.pDropOld) st.splice(i, 1);
      else if (t === 2 && e.age >= R.rareMaxAge) st.splice(i, 1);
    }
    const s = S(), cd = s.cooldown[c.id] || {};
    const have = [];   // 一城至多十几件：数组比 Set 便宜
    for (const e of st) have.push(e.id);
    for (const id of row.epic || []) {
      const it = ITEM.get(id);
      if (it && count(id) === 0 && !have.includes(id) && !(cd[id] > g.monthIndex)) { const e = { id, qty: 1, price: listPrice(it, c, originOf(it, c, sid)), age: 0 }; st.push(e); added.push(e); have.push(id); }
    }
    const tier = shopTier(c), slots = CFG.slotsByTier[clamp(tier, 1, 4)] + (fest ? CFG.festival.slots : 0);
    // 庙会收摊：多出来的货位上最新的凡品 / 良品撤下（珍品、神品留着；不取随机数）
    for (let i = st.length - 1; i >= 0 && st.length > slots; i--) if (TIER[(ITEM.get(st[i].id) || {}).rarity] <= 1) st.splice(i, 1);
    let guard = 0;
    let rares = null;
    // 抽中已有的货就再抽（至多 40 次）。哪一池都没有新货时再抽也加不了什么：提前停（这些抽签只用本城本月的种子，
    // 抽完即弃，提前停与抽满 40 次结果相同；§17.4 补货 ≤ 1 ms）
    let misses = 0, canAdd = null;
    const fresh = list => { if (list) for (const x of list) if (!have.includes(x)) return true; return false; };
    while (st.length < slots && guard++ < 40) {
      if (misses >= 2) {
        if (canAdd === null) {
          if (!rares) rares = (row.rare || []).filter(x => !have.includes(x) && !(cd[x] > g.monthIndex));
          canAdd = (tier >= 2 && (rares.length > 0 || fresh(row.uncommon))) || fresh(row.common) || fresh(row.commonForeign);
        }
        if (!canAdd) break;
      }
      const r = k.nextDouble();
      let id = null, foreign = false;
      if (!rares) rares = (row.rare || []).filter(x => !have.includes(x) && !(cd[x] > g.monthIndex));
      if (tier >= 2 && rares.length && r < R.pRare) id = rares[Math.floor(k.nextDouble() * rares.length)];
      else if (tier >= 2 && (row.uncommon || []).length && r < R.pUncommon) id = row.uncommon[Math.floor(k.nextDouble() * row.uncommon.length)];
      else {
        const useF = (row.commonForeign || []).length > 0 && (k.nextDouble() < R.pForeign || !(row.common || []).length);
        const pool = useF ? row.commonForeign : row.common;
        if (pool && pool.length) { id = pool[Math.floor(k.nextDouble() * pool.length)]; foreign = useF; }
      }
      if (!id || have.includes(id)) { misses++; continue; }
      const it = ITEM.get(id);
      have.push(id); misses = 0; canAdd = null;
      if (it.rarity === 'rare') rares = null;
      const origin = foreign ? 'foreign' : originOf(it, c, sid);
      const e = { id, qty: TIER[it.rarity] === 0 ? 1 + Math.floor(k.nextDouble() * R.commonQtyMax) : 1, price: listPrice(it, c, origin), age: 0 };
      if (origin === 'foreign') e.foreign = true;
      st.push(e); added.push(e);
    }
    if (c.shop.merchant && c.shop.merchant.until <= g.monthIndex) c.shop.merchant = null;
    if (c.owner >= 0 && c.owner === g.player) for (const e of st) markSeen(e.id);
    return added;
  }
  function restockAll(news) {
    const g = G();
    for (const c of g.cities) {
      const added = restock(c);
      if (news && c.owner === g.player) for (const e of added) {
        const it = ITEM.get(e.id);
        if (it && TIER[it.rarity] >= 2) news.push(rareNews(c, it));
      }
    }
  }
  const RARE_NEWS = { dilu: '{c}市中有人牵来一匹额有白点的骏马……（的卢）', tianma: '{c}的胡商牵来一匹汗血宝马，索价惊人！' };
  function rareNews(c, it) { const t = RARE_NEWS[it.id]; return t ? t.replace('{c}', c.name) : `${c.name}的市上新到一件${D.rarities[it.rarity].alias}：「${displayName(it)}」。`; }
  // 能否在 c 的市场里交易：己方城；与己方城相邻的同盟城（×1.15，§6.1）；他国 / 空城关闭
  function canTrade(f, c) {
    const g = G();
    if (!c || f < 0) return '没有势力。';
    if (c.owner === f) return null;
    if (c.owner >= 0 && g.allied(c.owner, f) && c.links.some(j => g.cities[j].owner === f)) return null;
    return c.owner >= 0 && g.allied(c.owner, f) ? '只有与己方城池相邻的同盟城可以通商。' : '他国与空城的市场不对你开放。';
  }
  function payCityFor(f, c) {
    const g = G();
    if (c.owner === f) return c;
    let best = null;
    for (const j of c.links) { const n = g.cities[j]; if (n.owner === f && (!best || n.gold > best.gold)) best = n; }
    return best;
  }
  // 玩家 / 脚本的购买（§6.1、§6.7）：金从市场所在城（同盟城：相邻己方城中金最多的一座）出；货进府库或直接装备给 opts.gen
  function buy(f, c, idx, opts) {
    opts = opts || {};
    const g = G();
    if (!on()) return { ok: false, why: '装备系统未开启。' };
    const why = canTrade(f, c); if (why) return { ok: false, why };
    const list = opts.merchant ? (c.shop.merchant && c.shop.merchant.until > g.monthIndex ? c.shop.merchant.stock : []) : c.shop.stock;
    const e = list[idx];
    if (!e || !(e.qty > 0)) return { ok: false, why: '货已售罄。' };
    const it = ITEM.get(e.id);
    const price = entryPrice(c, opts.merchant ? Object.assign({ merchant: true }, e) : e, f);
    const pay = opts.payCity || payCityFor(f, c);
    if (!pay || pay.gold < price) return { ok: false, why: `金不足（需 ${price} 金）。`, price };
    if (!opts.gen && armouryOf(f, { vault: false }).length >= CFG.armouryCap) return { ok: false, why: `府库已满（${CFG.armouryCap} 件），请先变卖或赏赐。` };
    if (TIER[it.rarity] >= 3 && count(it.id) > 0) return { ok: false, why: '此物世间只有一件，已有主人。' };
    pay.gold -= price;
    const uid = mkInst(it.id, 'arm', f);
    if (--e.qty <= 0) list.splice(idx, 1);
    if (TIER[it.rarity] >= 2) { const s = S(); (s.cooldown[c.id] = s.cooldown[c.id] || {})[it.id] = g.monthIndex + (TIER[it.rarity] >= 3 ? CFG.restock.epicCooldown : CFG.restock.cooldown); }
    let eq = null;
    if (opts.gen) { eq = Gear.equip(opts.gen, uid, { force: !!opts.force }); }
    if (f === g.player) markOwned(it.id);
    ST.buys[it.rarity] = (ST.buys[it.rarity] || 0) + 1;
    if (f === g.player) ST.playerSpend += price;
    const C = SG.Commands;
    if (C && typeof C.record === 'function' && opts.record !== false) C.record('market', pay, opts.gen || null, { ok: true, side: 'buy', item: it.id, rarity: it.rarity, price, uid, marketCity: c.id });
    return { ok: true, uid, price, equip: eq, item: it };
  }
  function sell(f, uid, c) {
    const g = G(), r = instRec(uid);
    if (!r) return { ok: false, why: '无此物。' };
    const it = ITEM.get(r.id);
    const s = sellable(it, r); if (!s.ok) return s;
    const of = factionOfInst(uid);
    if (of !== f) return { ok: false, why: '不是本势力之物。' };
    if (r.loc === 'gen') { const x = g.generals[r.owner]; const u = Gear.unequip(x, it.slot, { toArmoury: true }); if (!u.ok) return u; }
    const city = c && c.owner === f ? c : (g.citiesOf(f)[0] || null);
    if (!city) return { ok: false, why: '没有可以变卖的城。' };
    const gold = sellPrice(+uid, city);
    city.gold += gold;
    destroy(uid);
    ST.sold++;
    const C = SG.Commands;
    if (C && typeof C.record === 'function' && f === g.player) C.record('market', city, null, { ok: true, side: 'sell', item: it.id, rarity: it.rarity, gold });
    return { ok: true, gold, item: it };
  }
  // 胡商 / 海商的货（§6.5；ENGAGEMENT「行商过境」卡的「看看货物」调用）：25° 内至多 3 种文化的货 3 件（×1.5，2 个月），20 % 其中一件为珍品
  function openMerchant(c, k) {
    const g = G(), sid = sidOf(g), MC = CFG.merchant;
    if (!on() || !c) return null;
    k = k || rng('merchant', c.id);
    const near = new Map();
    for (const o of g.cities) {
      const d = Math.hypot((o.lon || 0) - (c.lon || 0), (o.lat || 0) - (c.lat || 0));
      if (d <= MC.radiusDeg && o.culture) near.set(o.culture, Math.min(near.get(o.culture) || 1e9, d));
    }
    const cults = Array.from(near.keys()).filter(x => x !== c.culture).sort((a, b) => near.get(a) - near.get(b)).slice(0, MC.maxCultures);
    if (!cults.length) cults.push(c.culture || 'han');
    const cand = D.items.filter(it => TIER[it.rarity] <= 1 && cults.indexOf(it.culture) >= 0 && avail(it, 'world'));
    const stock = [];
    const used = new Set();
    let guard = 0;
    while (stock.length < MC.n && cand.length && guard++ < 30) {
      const it = cand[Math.floor(k.nextDouble() * cand.length)];
      if (used.has(it.id)) continue;
      used.add(it.id);
      stock.push({ id: it.id, qty: 1, price: round10(basePrice(it) * cityK(c) * CFG.originK.merchant), age: 0, merchant: true });
    }
    if (k.nextDouble() < MC.pRare && stock.length) {
      let pool;
      if (sid === 'classic') pool = D.items.filter(it => TIER[it.rarity] <= 1 && !avail(it, 'classic') && avail(it, 'world'));
      else pool = D.items.filter(it => it.rarity === 'rare' && (avail(it, sid) || {}).kind === 'shop' && count(it.id) === 0);
      if (pool.length) { const it = pool[Math.floor(k.nextDouble() * pool.length)]; stock[stock.length - 1] = { id: it.id, qty: 1, price: round10(basePrice(it) * cityK(c) * CFG.originK.merchant), age: 0, merchant: true }; }
    }
    if (!c.shop) c.shop = { stock: [], merchant: null };
    c.shop.merchant = { until: g.monthIndex + MC.months, stock };
    if (c.owner === g.player) for (const e of stock) markSeen(e.id);
    return c.shop.merchant;
  }

  // ================================================================ 装备的换与收（§2）==
  function canEquip(x, uid, opts) {
    const g = G(), r = instRec(uid);
    if (!r) return { ok: false, why: '无此物。' };
    const it = ITEM.get(r.id);
    if (!x || x.dead) return { ok: false, why: '此人已不在。' };
    const fx = factionOfInst(uid);
    if (r.loc === 'gen' && r.owner === x.id) return { ok: true, already: true };
    if (r.bound) return { ok: false, why: '专属兵器不能转交。' };
    if (!(opts && opts.force) && fx !== x.faction) return { ok: false, why: '不是本势力之物。' };
    const st = x.gear && x.gear[it.slot] != null ? instRec(x.gear[it.slot]) : null;
    if (st && st.bound) return { ok: false, why: '此位已有专属兵器，不能替换。' };
    // 要求（基础属性）：不足而不能装备的（没有 below: 'half'）拒绝；盾要单手兵器
    const style = it.slot === 'weapon' ? styleOf(it) : calc(x).style;
    const req = it.req || {};
    for (const k of ['war', 'intel', 'pol']) if (req[k] && (x[k] | 0) < req[k] && req.below !== 'half') return { ok: false, why: reqWhy(x, it, style) };
    if (isShield(it) && style !== 'one') return { ok: false, why: '盾须配单手兵器。' };
    return { ok: true, why: reqWhy(x, it, style) };
  }
  // 从武将身上拿走一件装备的忠诚代价（§2.6）：君主无；赏赐过的那件 = giftLoy；招牌装备离开原主 −15（跌破 50 则拒绝）；
  // 其余按品级（换上同级或更好的不算拿走）
  function takeCost(x, uid, replacement) {
    const g = G(), r = instRec(uid);
    if (!r || !x || isRuler(x)) return { cost: 0 };
    const it = ITEM.get(r.id), a = avail(it, sidOf(g));
    if (a && a.kind === 'signature' && a.holder === x.name) {
      if (x.loyalty - CFG.sigTakeLoy < CFG.sigRefuseBelow) return { cost: CFG.sigTakeLoy, refuse: '「此物随我征战多年，恕难从命。」' };
      return { cost: CFG.sigTakeLoy };
    }
    if (r.gifted === x.id) return { cost: r.giftLoy | 0, gift: true };   // 赐过的那件：扣回 giftLoy（INTEGRATION §7.1），代替品级表
    const nt = replacement ? TIER[ITEM.get(instRec(replacement).id).rarity] : -1;
    const ot = TIER[it.rarity];
    return { cost: nt >= ot ? 0 : CFG.takeLoy[ot] || 0 };
  }
  function homeOf(uid, f) {   // 卸下的东西放回哪里：镇城之宝回本城库（城仍归本势力），其余进府库
    const g = G(), r = instRec(uid), it = ITEM.get(r.id), a = avail(it, sidOf(g));
    if (a && a.kind === 'cityTreasure') { const c = g.cityByKey(a.city); if (c && c.owner === f) return ['city', c.id]; }
    return f >= 0 ? ['arm', f] : null;
  }
  const Gear = {
    invalidate: bump,
    slots(x) { const o = {}; for (const sl of SLOTS) o[sl] = x && x.gear && x.gear[sl] != null ? instView(x.gear[sl]) : null; return o; },
    item(x, sl) { const u = x && x.gear ? x.gear[sl] : null; const r = u != null ? instRec(u) : null; return r ? ITEM.get(r.id) : null; },
    equipped(x) { return calc(x).list.slice(); },
    raw, stat, eff, duelWar, duelMods, abilityHp,
    abilities(x, t) { return t ? abil(x, t).slice() : calc(x).abil.slice(); },
    has(x, t) { return abil(x, t).length > 0; },
    canEquip,
    // opts: { force（脚本 / 奖品：不查势力）, dropShield: true（换双手兵器时把盾放回府库）, free: false（不计忠诚代价）}
    equip(x, uid, opts) {
      opts = opts || {};
      const g = G(), r = instRec(uid);
      const chk = canEquip(x, uid, opts);
      if (!chk.ok || chk.already) return { ok: !!chk.ok, why: chk.why || '', loyaltyDelta: 0, displaced: [] };
      const it = ITEM.get(r.id);
      let loy = 0;
      const displaced = [];
      // 原主（同势力的另一位武将）失去此物的代价
      if (r.loc === 'gen' && !opts.free) {
        const prev = g.generals[r.owner];
        const tc = takeCost(prev, uid, null);
        if (tc.refuse && !opts.force) return { ok: false, why: prev.name + tc.refuse, loyaltyDelta: 0, displaced };
        if (tc.cost) { prev.loyalty = Math.max(0, prev.loyalty - tc.cost); }
        if (tc.gift) r.giftLoy = 0;
      }
      const old = x.gear[it.slot];
      if (old != null) {
        const tc = opts.free ? { cost: 0 } : takeCost(x, old, uid);
        if (tc.refuse && !opts.force) return { ok: false, why: x.name + tc.refuse, loyaltyDelta: 0, displaced };
        loy -= tc.cost || 0;
        if (tc.gift) instRec(old).giftLoy = 0;
        const h = homeOf(old, x.faction);
        if (h) place(old, h[0], h[1]); else detach(old);
        displaced.push(old);
      }
      place(uid, 'gen', x.id);
      // 双手 / 双持 / 扇：盾卸回府库（§2.2）
      if (it.slot === 'weapon' && styleOf(it) !== 'one' && x.gear.offhand != null && opts.dropShield !== false) {
        const sr = instRec(x.gear.offhand);
        if (sr && isShield(ITEM.get(sr.id)) && !sr.bound) { const h = homeOf(x.gear.offhand, x.faction); const u2 = x.gear.offhand; if (h) place(u2, h[0], h[1]); else detach(u2); displaced.push(u2); }
      }
      if (loy) x.loyalty = clamp(x.loyalty + loy, 0, 100);
      return { ok: true, why: '', loyaltyDelta: loy, displaced };
    },
    unequip(x, sl, opts) {
      opts = opts || {};
      const u = x && x.gear ? x.gear[sl] : null;
      if (u == null) return { ok: false, why: '此位没有装备。', loyaltyDelta: 0 };
      const r = instRec(u);
      if (r.bound) return { ok: false, why: '专属兵器不能卸下。', loyaltyDelta: 0 };
      const tc = opts.free ? { cost: 0 } : takeCost(x, u, null);
      if (tc.refuse && !opts.force) return { ok: false, why: x.name + tc.refuse, loyaltyDelta: 0 };
      const h = homeOf(u, x.faction);
      if (h) place(u, h[0], h[1]); else detach(u);
      if (tc.cost) x.loyalty = Math.max(0, x.loyalty - tc.cost);
      if (tc.gift) r.giftLoy = 0;
      return { ok: true, why: '', loyaltyDelta: -(tc.cost || 0), uid: u };
    },
    // 人物菜单的候选（§14.1）：府库在前，再是当前城市场（带价格）；delta = 电脑估值差，ok / why = 能否装备
    candidates(x, sl, opts) {
      const g = G(); opts = opts || {};
      const out = [];
      const cur = x.gear ? x.gear[sl] : null;
      const curScore = cur != null ? score(x, instRec(cur).id) : 0;
      for (const u of armouryOf(x.faction)) {
        const r = instRec(u), it = r && ITEM.get(r.id);
        if (!it || it.slot !== sl) continue;
        const ce = canEquip(x, u);
        out.push({ uid: u, id: it.id, item: it, from: r.loc === 'city' ? 'vault' : 'armoury', delta: score(x, it.id) - curScore, ok: ce.ok, why: ce.why || '' });
      }
      for (const y of g.generalsOf(x.faction)) {
        if (y === x || !y.gear || y.gear[sl] == null) continue;
        const u = y.gear[sl], r = instRec(u), it = ITEM.get(r.id);
        if (r.bound) continue;
        const ce = canEquip(x, u);
        out.push({ uid: u, id: it.id, item: it, from: 'general', holder: y.id, delta: score(x, it.id) - curScore, ok: ce.ok, why: ce.why || '' });
      }
      const c = g.cities[x.city];
      if (c && opts.shop !== false && !canTrade(x.faction, c)) {
        c.shop.stock.forEach((e, i) => {
          const it = ITEM.get(e.id); if (!it || it.slot !== sl) return;
          out.push({ uid: null, id: it.id, item: it, from: 'shop', city: c.id, entry: i, price: entryPrice(c, e, x.faction), delta: score(x, it.id) - curScore, ok: true, why: '' });
        });
      }
      out.sort((a, b) => (b.ok - a.ok) || (b.delta - a.delta));
      return out;
    },
    // 人物菜单「总览」与商店比较列的数字（同一套 SG.Mods，与战斗相同）
    summary(x) {
      const Mo = SG.Mods;
      const pv = Mo && Mo.unitPreview ? Mo.unitPreview(x) : null;
      const dm = duelMods(x);
      const base = { war: x.war | 0, intel: x.intel | 0, pol: x.pol | 0 };
      return {
        base, eff: { war: eff(x, 'war'), intel: eff(x, 'intel'), pol: eff(x, 'pol') },
        army: { atk: pv ? pv.atk : 1 + raw(x, 'atk') / 100, def: pv ? pv.def : 1 + raw(x, 'def') / 100, crit: pv ? pv.crit : stat(x, 'critArmy'), critK: pv ? pv.critK : 1.5 + stat(x, 'critMulArmy'),
          mob: raw(x, 'mob') + maxOf(abil(x, 'mobility'), 'move'), morale: raw(x, 'morale'), tac: Gear.battle.tac(x), escape: raw(x, 'escape') },
        duel: { war: dm.war, hp: dm.hp, dmg: dm.dmg, reach: dm.weapon ? dm.weapon.reach * (1 + dm.reachK) : null, guard: dm.guard, parry: dm.parry, crit: dm.critDuel, budget: dm.budget },
        abilities: calc(x).list.filter(e => e.item.ability).map(e => ({ slot: e.slot, id: e.id, name: e.item.ability.name, desc: e.item.ability.desc, active: e.item.ability.effects.some(q => q.t === 'active' || q.t === 'activeChoice'), req: e.req })),
        locks: calc(x).list.filter(e => e.req === 'none' || e.req === 'half' || e.req === 'polOnly').map(e => ({ slot: e.slot, id: e.id, req: e.req, why: reqWhy(x, e.item, calc(x).style) })),
      };
    },
  };

  // ================================================================ 战斗规则的纯函数（ITEMS §3.2、§7.2；battle-model.js 接线）==
  const MOUNTED = new Set(['horse', 'warhorse', 'pony', 'camel', 'elephant', 'chariot', 'cataphract']);
  const mountType = x => { const it = Gear.item(x, 'mount'); return it ? it.type : null; };
  // mount 能力（§7.2）：只在骑着对应的坐骑时（elephant / camel）
  const mountMatch = (x, e) => (e.elephant && mountType(x) === 'elephant') || (e.camel && mountType(x) === 'camel');
  Gear.battle = {
    mobility(x) { return clamp(raw(x, 'mob') + maxOf(abil(x, 'mobility').filter(e => !e.cond), 'move'), -1, 2); },
    ease(x) { const s = new Set(calc(x).ease); for (const e of abil(x, 'mobility')) for (const t of e.ease || []) s.add(t); for (const e of abil(x, 'aura')) for (const t of e.ease || []) s.add(t); return s; },
    hard(x) { return new Set(calc(x).hard); },
    // 威（§3.2）：开战时敌方每支部队士气 − 我方的最大值（取一方最大，不相加）；开场 opening.awe 是另加的（§7.2「awe here is an extra」）
    awe(x) { return raw(x, 'awe'); },
    openingAwe(x) { return maxOf(abil(x, 'opening'), 'awe'); },
    // 脱身（§3.2 capture rolls）：被俘几率 × (1 − escape)；escape = 1 − (1 − 坐骑脱身 %) × min(escapeK) × (1 − 骑驼 / 骑象时的 mount.escape %)
    escape(x) {
      const k = abil(x, 'escapeK').reduce((m, e) => Math.min(m, typeof e.k === 'number' ? e.k : 1), 1);
      let me = 0; for (const e of abil(x, 'mount')) if (e.escape && mountMatch(x, e)) me = Math.max(me, e.escape);
      return clamp(1 - (1 - raw(x, 'escape') / 100) * k * (1 - me / 100), 0, 1);
    },
    tac(x) { return Math.min(40, raw(x, 'tac') + maxOf(abil(x, 'tacAmp'), 'power') * 100) / 100; },
    tacHit(x) { return Math.min(12, raw(x, 'tacHit') + maxOf(abil(x, 'tacAmp'), 'hit') * (maxOf(abil(x, 'tacAmp'), 'hit') < 1 ? 100 : 1)) / 100; },
    fireWeak(x) { return raw(x, 'fireWeak') / 100 + maxOf(abil(x, 'dmgTaken'), 'fire'); },
    antiHorse(x) { return raw(x, 'antiHorse') / 100; },
    heal(x) { return Math.min(30, raw(x, 'heal') + maxOf(abil(x, 'medic'), 'k') * 100) / 100; },
    counter(x) { return raw(x, 'counter') / 100 + maxOf(abil(x, 'counterUp'), 'k'); },
    counterGuard(x) { return Math.min(35, raw(x, 'counterGuard') + maxOf(abil(x, 'counterGuard'), 'k') * 100) / 100; },
    armorBreak(x) { return Math.min(25, raw(x, 'armorBreak') + maxOf(abil(x, 'sunderAll'), 'k') * 100) / 100; },
    mounted(x) { return MOUNTED.has(mountType(x)); },
    // 攻击的条件乘数（§3.2）：ctx = { moved: 本次行动移动的格数, firstAttack, targetCommander, targetOnFort, outnumbered, focused, targetMounted,
    //   allies: 同一方仍在场的武将（萨尔马提亚龙旗 allMounted：己方骑乘部队冲锋 +k，取一方最大）}
    attackK(x, ctx) {
      ctx = ctx || {};
      let k = 1;
      if ((ctx.moved | 0) >= 2) k *= 1 + Gear.battle.chargeK(x, ctx);
      if (ctx.firstAttack) k *= 1 + raw(x, 'first') / 100 + maxOf(abil(x, 'first'), 'k');
      if (ctx.targetCommander) k *= 1 + maxOf(abil(x, 'vsCommander'), 'k');
      if (ctx.targetOnFort) k *= 1 + maxOf(abil(x, 'siege'), 'k');
      if (ctx.outnumbered) k *= 1 + maxOf(abil(x, 'outnumbered'), 'atk');
      if (ctx.targetMounted) k *= 1 + maxOf(abil(x, 'antiMounted'), 'k');
      k *= 1 + maxOf(abil(x, 'selfAtk'), 'k');
      if (ctx.focused) k *= 1 + (ctx.focusK || 0);
      return k;
    },
    // 条件攻防（作为 SG.Mods.atkMul / defMul 的 cond 传入）：地形、城门城楼、海战、背水
    condAtk(x, ctx) { ctx = ctx || {}; const out = []; for (const e of abil(x, 'terrain')) if (ctx.terrain && (e.terrains || []).indexOf(ctx.terrain) >= 0 && e.atk) out.push(e.atk); for (const e of abil(x, 'castle')) if (ctx.onFort && e.atk) out.push(e.atk); for (const e of abil(x, 'sea')) if (ctx.sea && e.atk) out.push(e.atk); for (const e of abil(x, 'lastStand')) if (ctx.troopsK !== undefined && ctx.troopsK < e.below && e.atk) out.push(e.atk); for (const e of abil(x, 'mount')) if (e.atk && mountMatch(x, e)) out.push(e.atk); return out; },
    condDef(x, ctx) { ctx = ctx || {}; const out = []; for (const e of abil(x, 'terrain')) if (ctx.terrain && (e.terrains || []).indexOf(ctx.terrain) >= 0 && e.def) out.push(e.def); for (const e of abil(x, 'castle')) if (ctx.onFort && e.def) out.push(e.def); for (const e of abil(x, 'sea')) if (ctx.sea && e.def) out.push(e.def); for (const e of abil(x, 'lastStand')) if (ctx.troopsK !== undefined && ctx.troopsK < e.below && e.def) out.push(e.def); return out; },
    // 会心（§3.1、§7.2）：条件会心能力（cond charge / vsCommander / moreTroops / plain）与以寡击众的会心伤害，
    //   ctx = { charge: 移动 ≥ 2 格, vsCommander, moreTroops: 我方兵多于目标, plain: 攻方站在平原, outnumbered }。
    //   critAt = 此刻的装备会心（仍 ≤ 0.04 / ≤ 0.30）；critAdd / critMulAdd = 比无条件的 stat('critArmy' / 'critMulArmy') 多出的部分
    critAt(x, ctx) {
      const c = ctx || {};
      const crit = Math.min(0.04, (raw(x, 'crit') + critAbil(x, 'crit', c)) / 100);
      const critMul = Math.min(CFG.duel.critMul, (raw(x, 'critDmg') + critAbil(x, 'critDmg', c) + (c.outnumbered ? maxOf(abil(x, 'outnumbered'), 'critDmg') : 0)) / 100);
      return { crit, critMul };
    },
    critAdd(x, ctx) { return Math.max(0, Gear.battle.critAt(x, ctx).crit - stat(x, 'critArmy')); },
    critMulAdd(x, ctx) { return Math.max(0, Gear.battle.critAt(x, ctx).critMul - stat(x, 'critMulArmy')); },
    // 抗会心（§3.2 新项 p(a) −= critRes(t)/100，≤ 5 pp）与护身（ward.crit：敌军对其的会心率 × (1 − k)，吉祥纹铜剑 0.4、天降神盾 0.5）
    critRes(t) { return raw(t, 'critRes') / 100; },
    critWard(t) { return Math.min(1, maxOf(abil(t, 'ward'), 'crit')); },
    critTaken(p, t) { return t ? Math.max(0, p - Gear.battle.critRes(t)) * (1 - Gear.battle.critWard(t)) : p; },
    // 拒马（骆驼 antiHorse，§3.2）：攻方骑乘（马 / 战马 / 矮马 / 战车）且与持有者相邻时，攻方攻击 × (1 − max antiHorse)
    antiHorseK(x, adjacentFoes) {
      const mt = mountType(x);
      if (!mt || mt === 'camel' || mt === 'elephant' || !Array.isArray(adjacentFoes)) return 1;
      let m = 0; for (const y of adjacentFoes) if (y && y.gear) m = Math.max(m, raw(y, 'antiHorse'));
      return 1 - m / 100;
    },
    // 坐镇守城（defendCity，§7.2）：城中本势力武将的最大值；守方部队防御 × (1 + k)，自动结算守方战力 × (1 + k)
    defendCityK(c) { return defendCityK(c); },
    // ---- battle-model 的一站式上下文（纯计算，不取随机数；battle-model.patch 调用）----
    // 战场格 → 能力数据里的地形名（plain forest hill mountain river wall gate castle）
    terrainKey(tt) { const T = SG.Terrain || {}; for (const k of Object.keys(T)) if (T[k] === tt) return k.toLowerCase(); return null; },
    // attack(a, t) 的条件：{ moved, firstAttack, targetCommander, targetOnFort, outnumbered, targetMounted, allies, crit: { charge, vsCommander, moreTroops, plain, outnumbered } }
    attackCtx(model, a, t) {
      const T = SG.Terrain || {}, tt = model.map[t.x][t.y], at = model.map[a.x][a.y];
      let mine = 0, foes = 0; const allies = [];
      for (const o of model.units) if (o.alive) { if (o.side === a.side) { mine++; allies.push(o.gen); } else foes++; }
      const moved = a.movedThisAct | 0, outnumbered = foes > mine;
      return { moved, firstAttack: !(a.attacks > 0), targetCommander: !!t.commander, targetOnFort: tt === T.Gate || tt === T.Castle, outnumbered,
        targetMounted: Gear.battle.mounted(t.gen), allies,
        crit: { charge: moved >= 2, vsCommander: !!t.commander, moreTroops: a.troops > t.troops, plain: at === T.Plain, outnumbered } };
    },
    // atkMul / defMul 的条件项（SG.Mods.atkMul(u, model, cond)）：地形、城门城楼（castle）、海战（setup.sea）、背水（lastStand）、
    // 骑驼 / 骑象（mount.atk）；守方另加坐镇守城（defendCity）
    condTerms(model, u, kind) {
      if (!u || !u.gen || !model || !model.map) return [];
      const T = SG.Terrain || {}, tt = model.map[u.x] ? model.map[u.x][u.y] : null;
      const ctx = { terrain: Gear.battle.terrainKey(tt), onFort: tt === T.Gate || tt === T.Castle, sea: !!(model.setup && model.setup.sea), troopsK: u.troops0 > 0 ? u.troops / u.troops0 : undefined };
      const out = kind === 'def' ? Gear.battle.condDef(u.gen, ctx) : Gear.battle.condAtk(u.gen, ctx);
      if (kind === 'def' && u.side === 1 && model.setup && model.setup.target) { const k = defendCityK(model.setup.target); if (k) out.push(k); }
      return out;
    },
    // 拒马：a 的攻击 × (1 − 相邻敌军的 antiHorse)，a 骑马时
    antiHorseAt(model, a) {
      const adj = []; for (const o of model.units) if (o.alive && o.side !== a.side && Math.abs(o.x - a.x) + Math.abs(o.y - a.y) === 1) adj.push(o.gen);
      return Gear.battle.antiHorseK(a.gen, adj);
    },
    // 开战（BattleModel 构造之末）：威（awe，一方最大）与开场（opening：士气 +、威 另加）；不取随机数。返回 [{ side, awe, morale }]
    applyOpening(model) {
      const out = [];
      for (let sd = 0; sd < 2; sd++) {
        let awe = 0, extra = 0, morale = 0;
        for (const u of model.units) if (u.side === sd && u.gen) { awe = Math.max(awe, Gear.battle.awe(u.gen)); extra = Math.max(extra, Gear.battle.openingAwe(u.gen)); morale = Math.max(morale, maxOf(abil(u.gen, 'opening'), 'morale')); }
        out.push({ side: sd, awe: Math.min(capOf('awe'), awe) + extra, morale });
      }
      for (const o of out) for (const u of model.units) {
        if (u.side === o.side && o.morale > 0) u.morale = Math.min(100, u.morale + o.morale);
        if (u.side !== o.side && o.awe > 0) u.morale = Math.max(1, u.morale - o.awe);
      }
      return out;
    },
    dmgTaken(x, kind) { let v = 0; for (const e of abil(x, 'dmgTaken')) if (typeof e[kind] === 'number' && Math.abs(e[kind]) > Math.abs(v)) v = e[kind]; return v; },
    ward(x, kind) { return maxOf(abil(x, 'ward'), kind); },
    splash(x) { return maxOf(abil(x, 'splash'), 'k'); },
    trample(x) { return maxOf(abil(x, 'trample'), 'k'); },
    // 冲锋（移动 ≥ 2 格后）的加成：装备 charge % + 自身的 charge 能力（cond mounted 只在骑乘时；allMounted 只给骑乘者）
    //   + 同方 allMounted 的最大值（骑乘者，不与自身的那件重复计）
    chargeK(x, ctx) {
      ctx = ctx || {};
      const mt = Gear.battle.mounted(x);
      let ch = raw(x, 'charge') / 100, side = 0;
      for (const e of abil(x, 'charge')) {
        if (e.allMounted) { if (mt) side = Math.max(side, e.k || 0); continue; }
        if (!e.cond || (e.cond === 'mounted' && mt)) ch += e.k || 0;
      }
      if (mt && Array.isArray(ctx.allies)) for (const y of ctx.allies) if (y && y !== x && y.gear) for (const e of abil(y, 'charge')) if (e.allMounted) side = Math.max(side, e.k || 0);
      for (const e of abil(x, 'mount')) if (e.charge && mountMatch(x, e)) ch += e.charge;
      return ch + side;
    },
    // 敌军不能反击（§7.2）：noCounter 能力（卢赫之矛：所有普通攻击）；charge.noCounter（赤兔、两刃矛）只在移动 ≥ 2 格后的攻击，
    // cond mounted（两刃矛）还要骑乘。ctx = { moved }（battle-model 传 a.movedThisAct）
    noCounter(x, ctx) {
      if (abil(x, 'noCounter').length > 0) return true;
      const moved = ctx && typeof ctx === 'object' ? ctx.moved | 0 : 0;
      if (moved < 2) return false;
      return abil(x, 'charge').some(e => e.noCounter && (e.cond !== 'mounted' || Gear.battle.mounted(x)));
    },
    procs(x, on) { return abil(x, 'proc').filter(e => !on || e.on === on); },
    capture(x) { return maxOf(abil(x, 'capture'), 'k'); },
    rescue(x) { return abil(x, 'rescue')[0] || null; },
    decoy(x) { return abil(x, 'decoy')[0] || null; },
    guard(x) { return abil(x, 'guard'); },
    opening(x) { return abil(x, 'opening'); },
    defenderOpening(x) { return abil(x, 'defenderOpening'); },
    oath(x) { return abil(x, 'oath'); },
    onRout(x) { return abil(x, 'onRout'); },
    rally(x) { return abil(x, 'rally')[0] || null; },
    regen(x) { return abil(x, 'regen')[0] || null; },
    throwAttack(x) { return abil(x, 'throwAttack')[0] || null; },
    hitRun(x) { return maxOf(abil(x, 'hitRun'), 'move'); },
    zoc(x) { return abil(x, 'zoc')[0] || null; },
    expBoost(x) { return PROVIDERS.expBoost(x); },
    medic(x) { return Gear.battle.heal(x); },
  };

  function defendCityK(c) {
    const g = G();
    if (!g || !c || c.owner < 0) return 0;
    let k = 0;
    for (const x of g.officersIn(c)) if (x.faction === c.owner) k = Math.max(k, maxOf(abil(x, 'defendCity'), 'k'));
    return k;
  }
  // 被俘几率（§3.2，Conquest.apply 的两处俘虏判定：v3 的 SG.Random.value() < p 照取一次，只改 p）：
  //   p' = (p + 击溃者的 capture) × (1 − escape(x)) × (1 − 替身 decoy) × (1 − 救主 rescue)；s.routedBy / s.rescueP 由 battle-model 记下
  function captureP(x, p, s) {
    if (!on() || !x) return p;
    const g = G();
    let q = p;
    const by = s && s.routedBy ? s.routedBy[x.id] : undefined;
    if (by != null && g.generals[by]) q += Gear.battle.capture(g.generals[by]);
    q *= 1 - Gear.battle.escape(x);
    const dc = Gear.battle.decoy(x);
    if (dc) {
      const partner = dc.partner && s && (s.atk || []).concat(s.def || []).some(y => y && y.name === dc.partner && y.faction === x.faction);
      q *= 1 - (partner ? (dc.partnerP !== undefined ? dc.partnerP : 1) : (dc.p || 0));
    }
    const rp = s && s.rescueP ? s.rescueP[x.id] || 0 : 0;
    if (rp > 0) q *= 1 - rp;
    return clamp(q, 0, 1);
  }
  // 救主（照夜玉狮子 rescue：周围 radius 格内的友军主将溃散时 p 几率被救出）：battle-model 在主将溃散时调用，记进 s.rescueP
  function rescueAt(model, u) {
    if (!on() || !model || !u || !u.commander) return 0;
    let p = 0;
    for (const v of model.units || []) {
      if (!v || v === u || v.alive === false || v.side !== u.side || !v.gen) continue;
      const e = Gear.battle.rescue(v.gen); if (!e) continue;
      if (Math.abs(v.x - u.x) + Math.abs(v.y - u.y) <= (e.radius || 0)) p = Math.max(p, e.p || 0);
    }
    return p;
  }

  // 不耗令牌的指令（free：{ cmd, n }，君主佩持）：本月还剩几次；useFree 用掉一次（记在 G.items.free，随存档）
  function freeUses(f, cmd) {
    const g = G(), st = S();
    if (!on() || !st || f < 0) return 0;
    const r = g.ruler(f);
    const n = r ? abil(r, 'free').filter(e => e.cmd === cmd).reduce((m, e) => m + (e.n || 0), 0) : 0;
    if (!n) return 0;
    const rec = st.free && st.free[f];
    const used = rec && rec.mi === g.monthIndex ? rec.used[cmd] | 0 : 0;
    return Math.max(0, n - used);
  }
  function useFree(f, cmd) {
    const g = G(), st = S();
    if (freeUses(f, cmd) <= 0) return false;
    if (!st.free) st.free = {};
    let rec = st.free[f];
    if (!rec || rec.mi !== g.monthIndex) rec = st.free[f] = { mi: g.monthIndex, used: {} };
    rec.used[cmd] = (rec.used[cmd] | 0) + 1;
    return true;
  }
  // 好感下限（dipPlus.relFloor）：f 与 t 结盟期间，f 的君主（或 envoy）佩持时；with 限对方文化
  function relFloor(f, t, envoy) {
    const g = G();
    if (!on() || f < 0 || t < 0 || f === t) return 0;
    if (typeof g.allied === 'function' && !g.allied(f, t)) return 0;
    const tc = g.factions[t] ? g.factions[t].culture : null;
    let v = 0;
    for (const x of [g.ruler(f), envoy || null]) if (x) for (const e of abil(x, 'dipPlus')) if (e.relFloor && (!e.with || e.with === tc)) v = Math.max(v, e.relFloor);
    return v;
  }
  // 斥候（scout k）：本势力持有者所在城 k 条路以内的他国城
  function scouted(f) {
    const g = G(), out = new Set();
    if (!on() || f < 0) return out;
    for (const x of g.generalsOf(f)) {
      const k = maxOf(abil(x, 'scout'), 'k'); if (!(k > 0)) continue;
      let front = [x.city]; const seen = new Set(front);
      for (let d = 0; d < k; d++) { const nx = []; for (const ci of front) for (const j of g.cities[ci].links) if (!seen.has(j)) { seen.add(j); nx.push(j); } front = nx; }
      for (const ci of seen) if (g.cities[ci].owner !== f) out.add(ci);
    }
    return out;
  }

  // ================================================================ 武将估值与电脑（§13）==
  const W = {
    war: { war: 3, atk: 2.0, def: 1.8, crit: 2.5, critDmg: 0.5, charge: 0.6, first: 0.5, counter: 0.5, counterGuard: 0.8, armorBreak: 1.0, morale: 0.5, awe: 0.6, mob: 12, escape: 0.15, dHp: 0.6, dDmg: 1.5, dGuard: 0.8, critRes: 1.5 },
    intel: { intel: 3, tac: 1.5, tacHit: 2.5, def: 1.0, atk: 0.6, search: 1.0, discord: 1.0, persuade: 1.0 },
    pol: { pol: 3, land: 1.2, ind: 1.2, town: 1.2, recruit: 0.8, train: 1.5, hire: 1.5, trade: 1.0, dip: 1.5, loyal: 10, reward: 2, heal: 0.5 },
    ruler: { fame: 4, virtue: 3, dip: 2, hire: 1 },
  };
  const SCOPE_BY_ROLE = { war: ['battle', 'duel'], intel: ['tactic'], pol: ['domestic'], ruler: ['world'] };
  // 太守（政治 ≥ 70）算 'pol'。只有政治 ≥ 70 而不高于智力的人才要查是不是太守（其余的角色不受太守影响），每次照查：
  // 以前按月缓存，太守在月中换了（电脑的回合、战后）缓存就过时，结果取决于本月第一次问的时刻（读档后的一局会走岔）
  function role(x) {
    if ((x.war >= x.intel && x.war >= x.pol) || x.war >= 70) return 'war';
    if (x.pol >= 70 && x.pol <= x.intel) { const g = G(); if (g && g.cities.some(c => c.governor === x.id)) return 'pol'; }
    return x.pol > x.intel ? 'pol' : 'intel';
  }
  const scopeCache = new Map();
  function abilityScope(it) {
    const hit = scopeCache.get(it.id);
    if (hit) return hit;
    const s = new Set();
    scopeCache.set(it.id, s);
    for (const e of it.ability.effects) {
      if (isDuelEffect(e.t)) s.add('duel');
      else if (e.t === 'tacAmp' || e.t === 'tacReflect') s.add('tactic');
      else if (['cmd', 'free', 'income', 'harvest', 'pop', 'loyalAura', 'expMonthly', 'talent'].indexOf(e.t) >= 0) s.add('domestic');
      else if (WORLD_EFFECTS.has(e.t)) s.add('world');
      else s.add('battle');
    }
    return s;
  }
  // 每件物品按 (角色, 是否君主) 预先算好 Σ W × 属性 + 能力分（与逐件现算同样的加法次序，结果逐位相同；§13.7 的成本）
  const preScore = new Map();
  function scoreSum(it, rl, ruler) {
    const w = W[rl];
    let s = 0;
    for (const [k, v] of Object.entries(it.stats || {})) {
      if (typeof v !== 'number') continue;
      s += (w[k] || 0) * v;
      if (ruler) s += (W.ruler[k] || 0) * v;
    }
    if (it.ability) {
      const sc = abilityScope(it);
      s += 6 * TIER[it.rarity] + (SCOPE_BY_ROLE[rl].some(q => sc.has(q)) || (ruler && sc.has('world')) ? 4 : 0);
    }
    return s;
  }
  function scorePre(it) {
    let p = preScore.get(it.id);
    if (p) return p;
    const req = it.req || {};
    p = { rw: req.war || 0, ri: req.intel || 0, rp: req.pol || 0, below: req.below === 'half', shield: isShield(it), ruler: !!req.ruler, pol: (it.stats || {}).pol || 0, v: {} };
    for (const rl of ['war', 'intel', 'pol']) p.v[rl] = [scoreSum(it, rl, false), scoreSum(it, rl, true)];
    // 上界：任何武将对这件的估值都不超过它（半效时负数变大，故两者取大；专属君主物给非君主时 3 × 政治 × (1 | 0.3)）
    let ub = -Infinity;
    for (const rl of ['war', 'intel', 'pol']) for (const v of p.v[rl]) ub = Math.max(ub, v, p.below ? v * 0.5 : v);
    if (p.ruler) ub = Math.max(ub, 3 * p.pol, 3 * p.pol * 0.3);
    p.ub = ub;
    preScore.set(it.id, p);
    return p;
  }
  function score(x, id) {
    if (!id) return 0;
    const it = ITEM.get(id); if (!it) return 0;
    const p = scorePre(it);
    const lw = p.rw && (x.war | 0) < p.rw, li = p.ri && (x.intel | 0) < p.ri, lp = p.rp && (x.pol | 0) < p.rp;
    if ((lw || li || lp) && !p.below) return -1e9;
    if (p.shield && calc(x).style !== 'one') return -1e9;
    const half = lw || li || lp ? 0.5 : 1;
    const rl = role(x), ruler = isRuler(x);
    if (p.ruler && !ruler) return 3 * p.pol * (rl === 'pol' ? 1 : 0.3);
    return p.v[rl][ruler ? 1 : 0] * half;
  }
  // 估值缓存（§13.7：按 (武将, 物品) 缓存，武将的属性 / 君主 / 角色 / 兵器类型变了才重算）
  const scoreCache = new WeakMap();
  function scoreMap(x) {
    const ru = isRuler(x), rl = role(x), st = calc(x).style;
    let c = scoreCache.get(x);
    if (!c || c.war !== x.war || c.intel !== x.intel || c.pol !== x.pol || c.ruler !== ru || c.role !== rl || c.style !== st) { c = { war: x.war, intel: x.intel, pol: x.pol, ruler: ru, role: rl, style: st, map: new Map() }; scoreCache.set(x, c); }
    return c.map;
  }
  function scoreIn(m, x, id) { let v = m.get(id); if (v === undefined) { v = score(x, id); m.set(id, v); } return v; }
  function scoreC(x, id) { return id ? scoreIn(scoreMap(x), x, id) : 0; }
  const curScore = (x, sl) => { const u = x.gear ? x.gear[sl] : null; return u != null ? scoreC(x, instRec(u).id) : 0; };
  const rankOf = x => (isRuler(x) ? 1000 : 0) + x.war + x.intel + x.pol + x.troops / 100;
  function genOrder(f, list) {
    const g = G();
    return (list || g.generalsOf(f)).slice().sort((a, b) => rankOf(b) - rankOf(a) || a.id - b.id);
  }
  // §13.2：每名武将每个装备位，取府库中估值最高、且比现有好 ≥ 5 % 的一件（专属不动；只从府库与本势力城库里拿，不从别人身上拿）
  function aiEquip(f, gensList) {
    const g = G();
    if (!on() || f < 0 || !g.factions[f] || !g.factions[f].alive) return 0;
    const arm = armouryOf(f);
    if (!arm.length) return 0;
    const pool = {};
    for (const sl of SLOTS) pool[sl] = [];
    for (const u of arm) { const r = instRec(u); if (r) pool[ITEM.get(r.id).slot].push(u); }
    let changes = 0;
    for (const x of genOrder(f, gensList)) {
      if (g.absent && g.absent(x)) continue;
      for (const sl of SLOTS) {
        const P = pool[sl];
        if (!P.length) continue;
        const cur = x.gear[sl];
        if (cur != null && instRec(cur).bound) continue;
        const m = scoreMap(x);   // 换装会改兵器类型（单手 / 双手），每个部位取一次
        let best = -1, bs = (cur != null ? scoreIn(m, x, instRec(cur).id) : 0) * CFG.ai.equipGain + 0.01;
        for (let i = 0; i < P.length; i++) { const v = scoreIn(m, x, instRec(P[i]).id); if (v > bs) { bs = v; best = i; } }
        if (best < 0) continue;
        const u = P[best];
        const r = Gear.equip(x, u, { free: true });
        if (!r.ok) continue;
        P.splice(best, 1);
        for (const d of r.displaced) { const rr = instRec(d); if (rr) pool[ITEM.get(rr.id).slot].push(d); }
        changes++;
      }
    }
    ST.equipChanges += changes;
    return changes;
  }
  // §13.4：府库超过 30 件时从最便宜的卖起；没人要的凡品也卖掉
  function aiSell(f, gensList) {
    const g = G();
    const fa = g.factions[f];
    if (!fa.armoury.length) return 0;
    const gens = gensList || g.generalsOf(f);
    let city = null;
    for (const c of g.cities) if (c.owner === f && (!city || c.gold > city.gold)) city = c;
    if (!city) return 0;
    let n = 0;
    const sellOne = u => { const r = instRec(u); if (!r) return false; const it = ITEM.get(r.id); if (!sellable(it, r).ok) return false; city.gold += sellPrice(+u, city); destroy(u); ST.sold++; n++; return true; };
    const wanted = new Map();
    for (const u of fa.armoury.slice()) {
      const r = instRec(u); if (!r) continue;
      const it = ITEM.get(r.id);
      if (it.rarity !== 'common') continue;
      let w = wanted.get(it.id);
      if (w === undefined) { w = gens.some(x => scoreC(x, it.id) > curScore(x, it.slot)); wanted.set(it.id, w); }
      if (!w) sellOne(u);
    }
    if (fa.armoury.length > CFG.ai.armouryMax) {
      const rest = fa.armoury.slice().sort((a, b) => valueOf(instRec(a).id) - valueOf(instRec(b).id));
      for (const u of rest) { if (fa.armoury.length <= CFG.ai.armouryMax) break; sellOne(u); }
    }
    return n;
  }
  // 买装额度：每个电脑势力每月记一次 gearShare() × 本月总收入（SG.AIBudget.income，第 7 相 accrue 记下的），
  // 至多 gearCapMonths 个月；存在 G.items.aiGear[f] = { c: 额度, mi: 记账的月 }（随存档）。玩家不受限。
  // gearShare() = CFG.ai.gearOfPurse × 钱袋份额（SG.AIBudget.share：Decisions.aiPurseShare / ShareByDifficulty）
  function gearShare() {
    const A = CFG.ai;
    if (typeof A.gearShare === 'number') return A.gearShare;
    const AIB = SG.AIBudget, D = SG.Balance && SG.Balance.Decisions;
    const share = AIB && +AIB.share > 0 ? +AIB.share : D && +D.aiPurseShare > 0 ? +D.aiPurseShare : 0.22;
    return A.gearOfPurse * share;
  }
  function gearCredit(f) {
    const g = G(), s = S(), AIB = SG.AIBudget;
    if (!s.aiGear || typeof s.aiGear !== 'object') s.aiGear = {};
    let r = s.aiGear[f];
    if (!r || typeof r !== 'object') r = s.aiGear[f] = { c: 0, mi: -1 };
    if (r.mi !== g.monthIndex) {
      const inc = AIB && AIB.income && typeof AIB.income.get === 'function' ? +AIB.income.get(f) || 0 : 0;
      const k = gearShare();
      r.c = Math.min((+r.c || 0) + k * inc, CFG.ai.gearCapMonths * k * Math.max(1, inc));
      r.mi = g.monthIndex;
    }
    return r;
  }
  // 本势力各城的在城武将：与 g.officersIn(c) 同一判据（同势力、未死、不在外、不在伤病外出），同一次序（g.generals 的次序）
  function officersByCity(f, gensList) {
    const g = G(), out = new Map();
    for (const x of gensList || g.generals) {
      if (x.faction !== f || x.dead || (g.absent && g.absent(x))) continue;
      let a = out.get(x.city); if (!a) out.set(x.city, a = []); a.push(x);
    }
    return out;
  }
  // SG.AIBudget.reserve(c).gold 的同一算式（INTEGRATION §5.1：150 + 8 × 在城武将；数取 Balance.AIBudget），只作预检；
  // 真正的把关仍是 AIBudget.trySpend（tests/items.js §2k 核对两者相等）
  function reserveGold(c, offsBy) {
    const A = (SG.Balance && SG.Balance.AIBudget) || {};
    if (!(A.ReserveBase >= 0) || !(A.ReservePerOfficer >= 0)) return SG.AIBudget.reserve(c).gold;
    return A.ReserveBase + A.ReservePerOfficer * ((offsBy.get(c.id) || []).length);
  }
  // §13.3：每月至多 2 件（珍品算 2 件），经 SG.AIBudget（gear）与买装额度；买估值提升 / 价格最高的一件，Δ > 2
  function aiShop(f, gensList) {
    const g = G(), AIB = SG.AIBudget;
    if (!on() || !AIB || f < 0 || !g.factions[f].alive || f === g.player) return 0;
    const cities = [];
    for (const c of g.cities) if (c.owner === f) cities.push(c);
    const ncity = cities.length;
    const credit = gearCredit(f);   // 买装额度（gearShare() = gearOfPurse × 钱袋份额，每月记一次；钱袋空的月份也记）
    const R = ST.shopRuns;
    R.calls++;
    if (!ncity || credit.c < 40 || (AIB.purseOf && AIB.purseOf(f) < 40)) { R.noMoney++; return 0; }
    let slots = CFG.ai.buysPerMonth, bought = 0;
    const reserves = new Map();
    let purse = Math.min(AIB.purseOf ? AIB.purseOf(f) : Infinity, credit.c);
    // 本势力各城货架上最便宜的一件也买不起：不必排武将、估值（§13.7 的成本；结果与照算相同）
    let cheapest = Infinity;
    for (const c of cities) {
      for (const e of c.shop.stock) if (e.qty > 0 && e.price < cheapest) cheapest = e.price;
      if (c.shop.merchant && c.shop.merchant.until > g.monthIndex) for (const e of c.shop.merchant.stock) if (e.qty > 0 && e.price < cheapest) cheapest = e.price;
    }
    if (purse < cheapest * (1 - CFG.discCap)) { R.tooCheap++; return 0; }
    // 各城的在城武将（= g.officersIn(c)，由本势力的武将表数，免得每城扫一遍全部武将）
    const offsBy = officersByCity(f, gensList);
    let gens = null;
    for (let pass = 0; pass < CFG.ai.buysPerMonth && slots > 0; pass++) {
      // 先按钱筛（钱袋与额度、城的保留金、珍品 / 神品的门槛；最后仍由 SG.AIBudget.trySpend 把关）：买不起的货不必估值
      const cand = [];
      const scan = (c, list, merch) => {
        for (let i = 0; i < list.length; i++) {
          const e = list[i];
          if (!(e.qty > 0)) continue;
          const lo = e.price * (1 - CFG.discCap);
          if (purse < lo) continue;
          const it = ITEM.get(e.id);
          if (!it) continue;
          const t = TIER[it.rarity];
          if (t >= 2 && slots < CFG.ai.rareCost) continue;
          if (t === 2 && !(ncity >= CFG.ai.rareCities || c.gold >= CFG.ai.rareGold)) continue;
          if (t >= 3 && ncity < CFG.ai.epicCities) continue;
          let res = reserves.get(c.id);
          if (res === undefined) { res = reserveGold(c, offsBy); reserves.set(c.id, res); }
          if (c.gold - lo < res) continue;
          cand.push({ c, list, i, e, it, merch });
        }
      };
      for (const c of cities) {
        scan(c, c.shop.stock, false);
        if (c.shop.merchant && c.shop.merchant.until > g.monthIndex) scan(c, c.shop.merchant.stock, true);
      }
      R.cand += cand.length;
      if (!cand.length) { R.noCand++; break; }
      if (!gens) { gens = genOrder(f, gensList).slice(0, CFG.ai.topGens); if (!gens.length) return 0; }
      // 每位武将每个部位的现有估值，与每种货的最大提升（同一种货在几座城只算一次）
      const maps = gens.map(scoreMap);
      // 每位武将某部位的现有估值（用到的部位才算）与其最小值：上界 − 最小值 ≤ minDelta 的货谁也用不上，不必逐人估值
      const curBy = {}, minCur = {};
      const curOf = sl => {
        let a = curBy[sl];
        if (a) return a;
        a = curBy[sl] = new Array(gens.length);
        let m = Infinity;
        for (let j = 0; j < gens.length; j++) { const x = gens[j], u = x.gear ? x.gear[sl] : null; const v = u != null ? scoreIn(maps[j], x, instRec(u).id) : 0; a[j] = v; if (v < m) m = v; }
        minCur[sl] = m;
        return a;
      };
      const gain = new Map();
      const deltaOf = it => {
        let d = gain.get(it.id);
        if (d !== undefined) return d;
        d = 0;
        const cur = curOf(it.slot);
        for (let j = 0; j < gens.length; j++) { const dd = scoreIn(maps[j], gens[j], it.id) - cur[j]; if (dd > d) d = dd; }
        gain.set(it.id, d);
        return d;
      };
      let best = null, bv = 0;
      for (const k of cand) {
        curOf(k.it.slot);
        if (scorePre(k.it).ub - minCur[k.it.slot] <= CFG.ai.minDelta) continue;
        const d = deltaOf(k.it);
        if (d <= CFG.ai.minDelta || d / k.e.price <= bv) continue;   // 以上架价排序（买价 = 上架价 × 本城折扣，至多 −12.5 %）
        bv = d / k.e.price; best = k;
      }
      if (!best) break;
      const { c, list, i, e, it } = best;
      const price = best.merch ? e.price : entryPrice(c, e, f, cityDiscount(c, offsBy.get(c.id) || []));
      if (price > credit.c || !AIB.trySpend(c, price, 'gear')) break;
      credit.c -= price;
      reserves.delete(c.id); purse = Math.min(AIB.purseOf ? AIB.purseOf(f) : Infinity, credit.c);
      mkInst(it.id, 'arm', f);
      if (--e.qty <= 0) list.splice(i, 1);
      if (TIER[it.rarity] >= 2) { const s = S(); (s.cooldown[c.id] = s.cooldown[c.id] || {})[it.id] = g.monthIndex + (TIER[it.rarity] >= 3 ? CFG.restock.epicCooldown : CFG.restock.cooldown); }
      ST.spend += price; ST.buys[it.rarity] = (ST.buys[it.rarity] || 0) + 1; ST.aiBuys++;
      slots -= TIER[it.rarity] >= 2 ? CFG.ai.rareCost : 1;
      bought++;
      aiEquip(f, gensList);
    }
    return bought;
  }

  // ================================================================ 起始装备（§8.4–§8.10）==
  const CIVILIAN = x => x.intel > x.war + 25 && x.war < 55;
  function kitPieces(x, culture) {
    const K = D.kits[culture] || D.kits[SG.cultureBase ? SG.cultureBase(culture) : 'han'] || D.kits.han;
    const out = {};
    if (!K) return out;
    const civ = CIVILIAN(x);
    let w = null;
    if (civ) w = (culture === 'han' ? K.byType.fan : null) || K.byType.staff || K.byType.fan || null;
    if (!w) {
      let t = D.kitWeapon && D.kitWeapon[x.name];
      if (!t) {   // 不在冻结表里的武将（不应发生）：按能力推断
        const h = hash32(x.name);
        t = x.war >= 85 ? ['poleblade', 'spear', 'ji', 'spear', 'axe'][h % 5] : x.intel > x.war + 15 ? 'sword' : ['spear', 'sword', 'dao', 'spear', 'shortji'][h % 5];
      }
      w = K.byType[t] || null;
      if (!w) { const style = (D.types[t] || {}).style || 'one'; for (const tt of D.styleFallback[style] || D.styleFallback.one) if (K.byType[tt]) { w = K.byType[tt]; break; } }
    }
    if (w) out.weapon = w;
    if (civ) { if (K.robe) out.armour = K.robe; if (K.cap) out.helmet = K.cap; }
    else {
      const a = x.war >= 55 ? K.armour : K.armourLight; if (a) out.armour = a;
      if (x.war >= 70 && K.helmet) out.helmet = K.helmet; else if (x.intel > x.war + 25 && K.cap) out.helmet = K.cap;
    }
    if (x.war >= 65 && K.mount) out.mount = K.mount;
    const wi = w && ITEM.get(w);
    if (wi && styleOf(wi) === 'one' && x.war >= 50 && K.shield) out.offhand = K.shield;
    // 设计原型（INTEGRATION §9 的实测所用）另给不持盾的武将（武力 ≥ 50，非文官）一张本文化的弓：副手不空着，电脑才不会把钱都花在凡品弓上
    else if (CFG.kitBow && x.war >= 50 && !civ && K.bow) out.offhand = K.bow;
    return out;
  }
  function setup(g) {
    const prev = SG.G; SG.G = g;
    try {
      ensure(g);
      IDX = { g: null, count: null }; idx();
      const sid = sidOf(g), T = D[sid] || {};
      const byName = new Map(g.generals.map(x => [x.name, x]));
      // 起始装备（凡品）
      for (const x of g.generals) {
        if (x.dead) continue;
        const pieces = kitPieces(x, x.culture || (x.faction >= 0 ? g.factions[x.faction].culture : 'han') || 'han');
        for (const sl of SLOTS) if (pieces[sl] && x.gear[sl] == null) mkInst(pieces[sl], 'gen', x.id);
      }
      // 专属兵器（绑定在开局君主身上）
      for (const f of g.factions) {
        const r = T.rulers && T.rulers[f.key]; const ru = f.ruler >= 0 ? g.generals[f.ruler] : null;
        if (!r || !ru || !ITEM.get(r.item)) continue;
        putStart(ru, r.item, { bound: true, kind: 'ruler' });
      }
      // 招牌装备（替换同位的凡品）
      for (const s of T.signature || []) { const x = byName.get(s.holder); if (x && ITEM.get(s.item)) putStart(x, s.item, { kind: 'signature' }); }
      // 开局府库
      for (const a of T.armoury || []) { const f = g.factionByKey(a.faction); if (f && ITEM.get(a.item)) mkInst(a.item, 'arm', f.id, { kind: 'armoury' }); }
      // 每座城一件本文化的凡品入府库（§8.7）
      for (const c of g.cities) {
        if (c.owner < 0) continue;
        const row = shopRow(c, sid); const pool = row ? (row.common || []).concat(row.commonForeign || []) : [];
        if (!pool.length) continue;
        const k = SG.SeededRandom(hash32(g.seed + '|start-armoury|' + c.id));
        mkInst(pool[Math.floor(k.nextDouble() * pool.length)], 'arm', c.owner, { kind: 'start' });
      }
      // 镇城之宝
      for (const t of T.cityTreasure || []) { const c = g.cityByKey(t.city); if (c && ITEM.get(t.item)) mkInst(t.item, 'city', c.id, { kind: 'cityTreasure' }); }
      // 商店（开局补一次货）
      for (const c of g.cities) { c.shop = { stock: [], merchant: null }; restock(c, true); }
      for (const f of g.factions) if (f.alive && f.id !== g.player) aiEquip(f.id);
      installHooks();
    } finally { SG.G = prev; }
  }
  function putStart(x, id, extra) {
    const it = ITEM.get(id);
    const old = x.gear[it.slot];
    if (old != null) {
      const r = instRec(old);
      if (r.bound) { if (x.faction >= 0) mkInst(id, 'arm', x.faction, extra); return; }
      destroy(old);   // 起始的凡品被招牌 / 专属取代（不留在世上）
    }
    mkInst(id, 'gen', x.id, extra);
    // 双手兵器取代了单手兵器：盾作废
    if (it.slot === 'weapon' && styleOf(it) !== 'one' && x.gear.offhand != null) { const r = instRec(x.gear.offhand); if (r && isShield(ITEM.get(r.id)) && !r.bound) destroy(x.gear.offhand); }
  }

  // ================================================================ 获取：搜索、战利品、阵斩、奖品（§8）==
  function hiddenList(g) { return (D[sidOf(g)] && D[sidOf(g)].hidden) || []; }
  function hiddenIn(c) {
    const g = G(), s = S(); if (!s) return [];
    return hiddenList(g).filter(h => h.city === c.key && s.hiddenFound.indexOf(h.item) < 0 && count(h.item) === 0 && dateOk(h, g));
  }
  function dateOk(h, g) { if (!h.from) return true; const p = String(h.from).split('-'); return g.year * 12 + g.month >= (+p[0]) * 12 + (+p[1]); }
  function hiddenP(h, x, c) {
    const H = CFG.hidden, Mo = SG.Mods;
    const intel = Mo ? Mo.eff(x, 'intel') : eff(x, 'intel');
    const th = abil(x, 'treasureHunt').reduce((m, e) => Math.max(m, 1 + (e.k || 0)), 1);
    const sun = x.name === '孙坚' && h.item === 'chuanguoxi' ? H.sunJianK : 1;
    const map = Mo ? 1 + Mo.z('treasureMapK', c, h.item) : 1;   // ENGAGEMENT 藏宝图（×2 = 提供者返回 1）
    return Math.min(H.pMax, (h.p + intel * h.intelK + raw(x, 'search') / H.searchDiv) * th * sun * map);
  }
  // Commands.searchItem（§8.5）：寻人之后、金粮之前；每件可得的隐藏宝物一次 SG.Random（只在有隐藏宝物的城）
  function searchHidden(c, x) {
    const g = G();
    if (!on() || !c || !x || c.owner < 0 || x.faction !== c.owner) return null;
    for (const h of hiddenIn(c)) {
      const p = hiddenP(h, x, c);
      if (SG.Random.value() < p) return foundHidden(h, c, x);
    }
    return null;
  }
  function foundHidden(h, c, x) {
    const g = G(), s = S(), it = ITEM.get(h.item);
    s.hiddenFound.push(h.item);
    const uid = mkInst(h.item, 'arm', c.owner, { kind: 'hidden' });
    g.addLog(`${x ? x.name + '在' : ''}${c.name}寻得「${displayName(it)}」！`);
    ST.hiddenFound++;
    if (TIER[it.rarity] >= 2) reveal(uid, c.owner, x ? x.name : '', 'hidden');
    if (c.owner !== g.player) aiEquip(c.owner);
    return { item: uid, id: it.id, name: displayName(it), rarity: it.rarity };
  }
  // 电脑寻宝（§13.6）：电脑的城里有隐藏宝物时，每三个月以城中智力最高者搜索一次（种子随机数）
  function aiHiddenSearch() {
    const g = G();
    for (const h of hiddenList(g)) {
      const c = g.cityByKey(h.city);
      if (!c || c.owner < 0 || c.owner === g.player) continue;
      if ((g.monthIndex + c.id) % CFG.hidden.aiEvery !== 0) continue;
      if (S().hiddenFound.indexOf(h.item) >= 0 || count(h.item) > 0 || !dateOk(h, g)) continue;
      const offs = g.officersIn(c); if (!offs.length) continue;
      const x = offs.slice().sort((a, b) => eff(b, 'intel') - eff(a, 'intel') || a.id - b.id)[0];
      if (rng('hidden', h.item).nextDouble() < hiddenP(h, x, c)) foundHidden(h, c, x);
    }
  }
  function clueFor(c) { const h = hiddenIn(c)[0]; return h && h.clue ? h.clue : null; }
  const REVEALS = [];
  function reveal(uid, f, from, how) { const r = instRec(uid); if (!r) return; REVEALS.push({ uid: +uid, id: r.id, to: f, from, how, rarity: ITEM.get(r.id).rarity }); if (REVEALS.length > 20) REVEALS.shift(); }
  // 战利品（§8.2、§8.8）：被斩者 / 处斩者身上的全部装备进 f 的府库；专属兵器的绑定解除
  function spoils(victim, f, how) {
    const g = G(); if (!on() || !victim || !victim.gear) return [];
    const out = [];
    for (const sl of SLOTS) {
      const u = victim.gear[sl]; if (u == null) continue;
      const r = instRec(u); if (!r) { victim.gear[sl] = null; continue; }
      r.bound = false;
      if (f >= 0 && g.factions[f] && g.factions[f].alive) place(u, 'arm', f);
      else { const c = g.cities[victim.city]; if (c) place(u, 'city', c.id); else detach(u); }
      out.push(u);
      if (TIER[ITEM.get(r.id).rarity] >= 2) reveal(u, f, victim.name, how || 'spoils');
    }
    if (f >= 0 && f !== g.player && out.length) aiEquip(f);
    return out;
  }
  // 阵斩（§8.2）：victim 已被判定斩杀（battle-model / 单挑调用）：身亡、战利品、继承
  function slay(victim, byFaction, how) {
    const g = G();
    if (!victim || victim.dead) return [];
    const oldF = victim.faction;
    const wasRuler = isRuler(victim);
    const out = spoils(victim, byFaction, how || 'slain');
    victim.dead = true; victim.troops = 0; victim.faction = -1;
    g.addLog(victim.name + '阵亡。');
    ST.slain++;
    const C = SG.Conquest;
    if (wasRuler && C && typeof C.succession === 'function') C.succession(oldF);
    for (const c of g.cities) if (c.governor === victim.id) g.autoGovernor(c);
    if (SG.Events) SG.Events.emit('general.slain', victim, { faction: oldF, wasRuler, by: byFaction, how });
    return out;
  }
  function slayRule() {
    const st = SG.Settings && SG.Settings.has && SG.Settings.has('items.slay') ? SG.Settings.get('items.slay') : null;
    if (st === 'all') return 'all';
    if (st === 'rulers') return 'rulers';
    return CFG.slayRulersOnly ? 'rulers' : 'all';
  }
  // how: 'critRout'（会心击溃）| 'duelKO'（ctx.big：会心 / 绝技 / 终结技一击）| 'timeout' | 'assassinate'
  function slayChance(victim, how, ctx) {
    if (!on() || !victim || victim.dead) return 0;
    const S2 = CFG.slay;
    let p = how === 'critRout' ? S2.critRout : how === 'duelKO' ? (ctx && ctx.big ? S2.duelKOBig : S2.duelKO) : how === 'assassinate' ? S2.assassinate : 0;
    if (!isRuler(victim)) p = slayRule() === 'all' ? p * CFG.slayOthersK : 0;
    return p;
  }
  // 一次 SG.Random.value()，只在概率 > 0 时取（关掉装备系统时不取）
  function rollSlay(victim, how, byFaction, ctx) {
    const p = slayChance(victim, how, ctx);
    if (!(p > 0)) return false;
    if (SG.Random.value() >= p) return false;
    slay(victim, byFaction, how);
    return true;
  }
  // 缴械释放（§8.8）：俘虏的非专属装备全归 f；他的势力 12 个月更想打你；他对旧主忠诚 +5
  function disarm(x, f) {
    const g = G(); if (!on() || !x || !x.gear) return [];
    const out = [];
    for (const sl of SLOTS) { const u = x.gear[sl]; if (u == null) continue; const r = instRec(u); if (!r || r.bound) continue; place(u, 'arm', f); out.push(u); }
    if (x.faction >= 0) { const s = S(); s.aggro[x.faction + '>' + f] = g.monthIndex + CFG.confiscate.months; if (!isRuler(x)) x.loyalty = Math.min(100, x.loyalty + CFG.confiscate.loyalty); }
    if (f !== g.player) aiEquip(f);
    return out;
  }

  // ---------------------------------------------------------------- 奖池（INTEGRATION §7.2）--
  const NEVER_POOL = new Set(['ruler', 'signature', 'hidden', 'cityTreasure', 'armoury']);
  function shopRaresUnowned(sid, culture, types) {
    return D.items.filter(it => it.rarity === 'rare' && (avail(it, sid) || {}).kind === 'shop' && count(it.id) === 0 && !it.bound
      && (!culture || it.culture === culture) && (!types || types.indexOf(it.type) >= 0));
  }
  // ENGAGEMENT 的奖池（§12.3）：凡品 / 良品 = 本文化、指定类型的商店货；珍品 = 该文化世上没有实例的商店珍品（没有则空）
  function pool(o) {
    o = o || {};
    const g = G(), sid = sidOf(g);
    const types = o.types && o.types.length ? o.types : null, slots = o.slots && o.slots.length ? o.slots : null;
    const okT = it => (!types || types.indexOf(it.type) >= 0) && (!slots || slots.indexOf(it.slot) >= 0);
    if (o.rarity === 'rare') return shopRaresUnowned(sid, o.culture || null, types).filter(okT).map(it => it.id);
    if (o.rarity === 'epic' || o.rarity === 'legendary') return [];
    const r = o.rarity || 'common';
    let list = D.items.filter(it => it.rarity === r && (avail(it, sid) || {}).kind === 'shop' && okT(it));
    const native = o.culture ? list.filter(it => it.culture === o.culture) : list;
    return (native.length ? native : list).map(it => it.id);
  }
  const reqOkBase = (x, it) => { const r = it.req || {}; for (const k of ['war', 'intel', 'pol']) if (r[k] && (x[k] | 0) < r[k]) return false; return true; };
  // 亚军 / 季军的三选一（TOURNAMENT §7.2，ITEMS §8.9）：每件 60 % 本文化、40 % 任意；都过要求；至少一件是他提升最大的那一位
  function offers(x, rarity, n, k) {
    const g = G(), sid = sidOf(g);
    k = k || rng('offers', x.id, rarity);
    n = n || 3;
    const cult = x.culture || 'han';
    const all = D.items.filter(it => it.rarity === rarity && (avail(it, sid) || {}).kind === 'shop' && reqOkBase(x, it));
    const own = all.filter(it => it.culture === cult);
    const out = [];
    let guard = 0;
    while (out.length < n && guard++ < 60) {
      const src = own.length && k.nextDouble() < 0.6 ? own : all;
      if (!src.length) break;
      const it = src[Math.floor(k.nextDouble() * src.length)];
      if (out.indexOf(it.id) < 0) out.push(it.id);
    }
    // 至少一件落在他提升最大的那一位
    const gain = sl => { const best = all.filter(it => it.slot === sl).reduce((m, it) => Math.max(m, score(x, it.id)), 0); return best - (x.gear && x.gear[sl] != null ? score(x, instRec(x.gear[sl]).id) : 0); };
    const top = SLOTS.slice().sort((a, b) => gain(b) - gain(a))[0];
    if (out.length && !out.some(id => ITEM.get(id).slot === top)) {
      const cand = (own.filter(it => it.slot === top).length ? own : all).filter(it => it.slot === top && out.indexOf(it.id) < 0);
      if (cand.length) out[out.length - 1] = cand[Math.floor(k.nextDouble() * cand.length)].id;
    }
    return out;
  }
  // 冠军奖品（TOURNAMENT §7.2）：大会专属 12 件（各一次，权重 3）∪ 世上没有实例的商店珍品（权重 1）；过要求；对他有提升的 ×2；
  // 经典剧本自第三届起 15 % 换成「异域奇珍」（每届至多一件史诗）；池空 → 本文化良品 + 300 金。返回 { id, gold, foreign }（不建实例：award）
  function tourneyPrize(x, place, k, o) {
    const g = G(), sid = sidOf(g), s = S();
    o = o || {};
    k = k || rng('prize', x.id, place);
    if (place === 2 || place === 3) { const r = place === 2 ? 'uncommon' : 'common'; return { offers: offers(x, r, 3, k), gold: 0 }; }
    const T = D[sid].tournament || {};
    const nth = o.count !== undefined ? o.count : (g.tourney && g.tourney.count) | 0;
    if (sid === 'classic' && nth >= 2 && k.nextDouble() < CFG.classicForeignChance) {
      const fp = (T.foreign || []).filter(id => { const it = ITEM.get(id); return it && !it.bound && count(id) === 0 && s.tourneyAwarded.indexOf(id) < 0 && (!o.epicGiven || it.rarity !== 'epic'); });
      if (fp.length) { const id = fp[Math.floor(k.nextDouble() * fp.length)]; return { id, gold: 0, foreign: true }; }
    }
    const cands = [];
    for (const id of T.champion || []) { const it = ITEM.get(id); if (it && !it.bound && s.tourneyAwarded.indexOf(id) < 0 && count(id) === 0 && reqOkBase(x, it)) cands.push([it, 3]); }
    for (const it of shopRaresUnowned(sid)) if (reqOkBase(x, it)) cands.push([it, 1]);
    if (!cands.length) {
      const u = pool({ rarity: 'uncommon', culture: x.culture || 'han' });
      return { id: u.length ? u[Math.floor(k.nextDouble() * u.length)] : null, gold: 300, fallback: true };
    }
    let tot = 0;
    const w = cands.map(([it, wt]) => { const cur = x.gear && x.gear[it.slot] != null ? score(x, instRec(x.gear[it.slot]).id) : 0; const v = wt * (score(x, it.id) > cur ? 2 : 1); tot += v; return v; });
    let r = k.nextDouble() * tot;
    for (let i = 0; i < cands.length; i++) { r -= w[i]; if (r <= 0) return { id: cands[i][0].id, gold: 0 }; }
    return { id: cands[cands.length - 1][0].id, gold: 0 };
  }
  // 发奖 / 赐予：target = 武将（AI 更好就直接装备，否则进其势力府库）或势力编号
  function award(target, id, o) {
    const g = G(), s = S(), it = ITEM.get(id);
    o = o || {};
    if (!on() || !it) return null;
    if (avail(it, sidOf(g)) && avail(it, sidOf(g)).kind === 'ruler') return null;   // 专属兵器永不进奖池
    if (TIER[it.rarity] >= 3 && count(id) > 0) return null;
    const x = typeof target === 'object' ? target : null;
    const f = x ? x.faction : target;
    let uid;
    if (f >= 0) uid = mkInst(id, 'arm', f, { kind: o.why || 'grant' });
    else if (x) uid = mkInst(id, 'gen', x.id, { kind: o.why || 'grant' });
    else return null;
    if (o.why === 'tourney' && s.tourneyAwarded.indexOf(id) < 0) s.tourneyAwarded.push(id);
    ST.grants[o.why || 'grant'] = (ST.grants[o.why || 'grant'] || 0) + 1;
    if (x && x.faction >= 0 && x.faction !== g.player) {
      const cur = x.gear[it.slot];
      if (score(x, id) > (cur != null ? score(x, instRec(cur).id) : 0)) Gear.equip(x, uid, { free: true });
      aiEquip(x.faction);
    } else if (f >= 0 && f !== g.player) aiEquip(f);
    if (TIER[it.rarity] >= 2) reveal(uid, f, x ? x.name : '', o.why || 'grant');
    return uid;
  }
  function grant(f, what, why, culture, k) {
    const g = G();
    if (!on() || f < 0 || !g.factions[f] || !g.factions[f].alive) return null;
    let id = ITEM.has(what) ? what : null;
    if (!id) {
      const cult = culture || g.factions[f].culture || 'han';
      let pl = pool({ rarity: what, culture: what === 'rare' ? null : cult });
      if (!pl.length && what === 'rare') pl = pool({ rarity: 'uncommon', culture: cult });
      if (!pl.length) return null;
      k = k || rng('grant', f, why || '', ST.grants[why] || 0);
      id = pl[Math.floor(k.nextDouble() * pl.length)];
    }
    return award(f, id, { why }) != null ? id : null;
  }
  // 武林大会三冠：金刚杵（G.tourney.wins，INTEGRATION §6.4）
  function titleRelic(x) {
    const g = G(), T = D[sidOf(g)].tournament || {};
    const wins = g.tourney && g.tourney.wins ? g.tourney.wins[x.name] | 0 : 0;
    for (const id of T.title3 || []) if (wins >= 3 && count(id) === 0) return award(x, id, { why: 'tourney' });
    return null;
  }

  // ---------------------------------------------------------------- 赐宝物（§6.6，INTEGRATION §7.1）--
  // 礼物价值倍数（§6.6）：这件东西自己的 gift 属性 ×「以之赏赐」giftK × 送礼者（givers：君主、使者）身上「以宝物赏赐」giftK 的最大值
  function giftK(id, givers) {
    const it = ITEM.get(id); if (!it) return 1;
    let k = (it.stats && it.stats.gift) || 1;
    if (it.ability) for (const e of it.ability.effects) if (e.t === 'giftK' && !e.holder) k *= e.k || 1;
    let h = 1;
    for (const y of [].concat(givers || [])) if (y && y.gear) for (const e of abil(y, 'giftK')) if (e.holder && e.item !== id) h = Math.max(h, e.k || 1);
    return k * h;
  }
  function fits(x, it) {
    const rl = role(x);
    if (rl === 'war') return it.cat === 'weapon' || it.slot === 'armour' || it.slot === 'mount';
    if (rl === 'intel') return it.type === 'milbook' || it.type === 'fan';
    return it.type === 'seal' || it.type === 'tool' || it.type === 'civbook';
  }
  // 赐宝物会顶掉的那件（§2.6）：{ uid, id, cost, refuse, sig, worse } —— 赐出的东西总是装备上，所以先告诉玩家会替换什么
  function giftDisplace(x, uid) {
    const r = instRec(uid), it = r && ITEM.get(r.id);
    if (!x || !it || !x.gear) return null;
    const old = x.gear[it.slot];
    if (old == null || old === +uid) return null;
    const or = instRec(old), oi = or && ITEM.get(or.id);
    if (!oi) return null;
    const tc = takeCost(x, old, uid), a = avail(oi, sidOf(G()));
    return { uid: old, id: oi.id, cost: tc.cost || 0, refuse: tc.refuse || null, sig: !!(a && a.kind === 'signature' && a.holder === x.name), worse: TIER[it.rarity] < TIER[oi.rarity] };
  }
  // o = { roll（测试）, confirm: true（会顶掉招牌装备或更高品级的东西时，玩家已确认）}
  // 返回 { ok, loy（赏赐所得）, before（赐之前）, afterEquip（换装代价之后）, after, eqDelta（换装的忠诚代价，≤ 0）, displaced, fits, line }
  function gift(x, uid, o) {
    o = o || {};
    const g = G(), r = instRec(uid);
    if (!on()) return { ok: false, why: '装备系统未开启。' };
    if (!r) return { ok: false, why: '无此物。' };
    if (!x || x.faction < 0) return { ok: false, why: '只能赏赐本势力的武将。' };
    if (isRuler(x)) return { ok: false, why: '君主无需赏赐。' };
    const it = ITEM.get(r.id);
    if (factionOfInst(uid) !== x.faction) return { ok: false, why: '不是本势力之物。' };
    if (r.loc === 'gen' && r.owner === x.id) return { ok: false, why: x.name + '已经佩着此物。' };
    const dp = giftDisplace(x, uid);
    if (dp && (dp.sig || dp.worse) && !o.confirm) {
      const di = ITEM.get(dp.id);
      return { ok: false, needConfirm: true, displaced: dp, why: `赐出「${displayName(it)}」会替换${x.name}的「${displayName(di)}」` + (dp.cost ? `（忠诚 −${dp.cost}）` : '') + '。' };
    }
    const before = x.loyalty;
    const eq = Gear.equip(x, uid, { free: false, dropShield: true });
    if (!eq.ok) return { ok: false, why: eq.why };
    const afterEquip = x.loyalty;
    let loy = 0, line = '';
    const fit = fits(x, it);
    if (r.gifted == null) {   // 每件实物只算一次（INTEGRATION §7.1）
      const GF = CFG.gift;
      const roll = o.roll !== undefined ? o.roll : SG.Random.rangeInt(0, GF.rollMax + 1);
      const ru = g.ruler(x.faction);
      loy = GF.base + roll + clamp(Math.round(valueOf(it) * giftK(it.id, ru ? [ru] : []) / GF.valueDiv), 0, GF.max);
      if (fit) loy = Math.round(loy * GF.fitK);
      if (it.id === 'chitu' && x.name === '关羽') { loy = Math.max(loy, GF.chituGuanyu); line = '「吾知此马日行千里，今幸得之，若知兄长下落，可一日而见面矣。」'; }
    }
    x.loyalty = Math.min(100, x.loyalty + loy);
    r.gifted = x.id;
    r.giftLoy = x.loyalty - afterEquip;    // 收回时扣回同样多（再赐一次 = 0）；换装的代价不在其中
    const C = SG.Commands;
    if (C && typeof C.record === 'function') C.record('reward', g.cities[x.city], x, { ok: true, before, after: x.loyalty, cost: 0, gift: it.id, giftFits: fit, giftUid: +uid, loy, eqDelta: afterEquip - before });
    const displaced = (eq.displaced || []).map(u => { const q = instRec(u); return q ? q.id : null; }).filter(Boolean);
    return { ok: true, loy: x.loyalty - afterEquip, before, afterEquip, after: x.loyalty, eqDelta: afterEquip - before, displaced, fits: fit, line, item: it, loyaltyDelta: eq.loyaltyDelta };
  }

  // ================================================================ 图鉴（§14.2）==
  function collection(f) {
    const g = G(), s = S() || { seen: {}, owned: {} };
    const pages = [];
    for (const cult of Object.keys(D.cultures)) {
      const items = D.items.filter(it => it.culture === cult);
      const cu = items.filter(it => TIER[it.rarity] <= 1);
      pages.push({ culture: cult, name: D.cultures[cult].name, items: items.map(it => ({ id: it.id, seen: !!s.seen[it.id], owned: !!s.owned[it.id] })),
        complete: cu.length > 0 && cu.every(it => s.owned[it.id]), seen: items.filter(it => s.seen[it.id]).length, total: items.length });
    }
    const done = pages.filter(p => p.complete).length;
    const el = D.items.filter(it => TIER[it.rarity] >= 3);
    return { pages, complete: done, fame: Math.min(CFG.collection.fameMax, done * CFG.collection.famePerPage), total: D.items.length,
      seen: Object.keys(s.seen).length, owned: Object.keys(s.owned).length, epicSeen: el.filter(it => s.seen[it.id]).length, epicTotal: el.length, allEpicSeen: el.every(it => s.seen[it.id]) };
  }
  let colCache = { g: null, mi: -1, v: 0 };
  function collectionFame() {
    const g = G();
    if (colCache.g !== g || colCache.mi !== g.monthIndex) colCache = { g, mi: g.monthIndex, v: collection().fame };
    return colCache.v;
  }

  // ================================================================ 月末与事件 ==
  const ST = { spend: 0, playerSpend: 0, buys: {}, sold: 0, grants: {}, hiddenFound: 0, equipChanges: 0, aiBuys: 0, slain: 0, restockMs: 0, aiMs: 0, aiShopMs: 0,
    shopRuns: { calls: 0, noMoney: 0, tooCheap: 0, noCand: 0, cand: 0 } };
  function monthItems(ctx) {
    const g = G();
    if (!on()) return;
    const t0 = now();
    restockAll(ctx && ctx.news);
    ST.restockMs += now() - t0;
    // 每月装备效果：同城经验（expMonthly）、发现人才（talent）、的卢流言（omen）；死者遗物
    for (const x of g.generals) {
      if (!x.gear) continue;
      if (x.dead) { if (SLOTS.some(sl => x.gear[sl] != null)) spoils(x, -1, 'dead'); continue; }
      if (x.faction < 0) continue;
      for (const e of abil(x, 'expMonthly')) {
        const P = SG.Progress;
        if (P && typeof P.gain === 'function') for (const y of g.generalsIn(x.city, x.faction)) if (y !== x || !e.sameCity) P.gain(y, e.n || 0, 'item');
      }
      for (const e of abil(x, 'talent')) {
        if (rng('talent', x.id).nextDouble() >= (e.p || 0)) continue;
        const h = g.generals.find(y => y.hidden && !y.dead && y.faction < 0 && g.cities[y.city].owner === x.faction);
        if (h) { h.hidden = false; if (x.faction === g.player && ctx) ctx.news.push(`${x.name}慧眼识人，${g.cities[h.city].name}的${h.name}显露了才能。`); }
      }
      for (const e of abil(x, 'omen')) {
        if (isRuler(x) || rng('omen', x.id).nextDouble() >= (e.p || 0)) continue;
        x.loyalty = clamp(x.loyalty + (e.loyalty || 0), 0, 100);
        if (x.faction === g.player && ctx) ctx.news.push(`城中流言：「的卢妨主」——${x.name}心中不安（忠诚 ${e.loyalty}）。`);
      }
    }
    aiHiddenSearch();
  }
  // 自由支出（第 7 相，onSpend 'gear'）：每个电脑势力每月一次：卖掉多余 → 换装 → 买装
  let gensBy = { g: null, mi: -1, map: null };
  function gensOfFaction(f) {
    const g = G();
    if (gensBy.g !== g || gensBy.mi !== g.monthIndex) {
      const map = new Map();
      for (const x of g.generals) if (!x.dead && x.faction >= 0) { if (!map.has(x.faction)) map.set(x.faction, []); map.get(x.faction).push(x); }
      gensBy = { g, mi: g.monthIndex, map };
    }
    return (gensBy.map.get(f) || []).filter(x => x.faction === f && !x.dead);
  }
  // 卖 → 换装 → 买：每个电脑势力每月照跑一遍（不缓存）。估值按物品预算（scorePre）之后这两步很便宜（府库空时立即返回），
  // 以前的「指纹相同就跳过」比这两步本身还贵，而且跳过与照跑不等价（换装之后才多出来的凡品要再跑一遍才卖），读档后的一局会走岔
  function spendGear(f) {
    if (!on()) return;
    const ta = now();
    const gens = gensOfFaction(f);
    aiSell(f, gens); aiEquip(f, gens);
    const tb = now();
    aiShop(f, gens);
    const tc = now();
    ST.aiMs += tc - ta; ST.aiShopMs += tc - tb;
  }
  let hooksInstalled = false;
  function installHooks() {
    if (hooksInstalled) return;
    const A = SG.StrategyAI;
    if (!A || typeof A.on !== 'function') return;
    hooksInstalled = true;
    A.on('items', monthItems, { owner: 'items', when: on, order: 50 });
    A.on('loyAura', loyAuraSpecial, { owner: 'items', when: on, order: 50 });
    A.onSpend('gear', f => spendGear(f), { owner: 'items', when: on });
  }
  // 俘虏：记下俘获者；电脑释放俘虏时 50 % 没收其珍品以上的非专属装备（种子随机数）
  const captors = new Map();
  function installEvents() {
    const E = SG.Events; if (!E) return;
    // 自动结算的守方加成（autoPower：defendCity、castle）只对正在结算的这一仗
    E.on('conquest.prepare', s => { sortie = s || null; });
    E.on('conquest.resolved', s => { if (sortie === s) sortie = null; });
    E.on('conquest.apply', s => { if (sortie === s) sortie = null; });
    E.on('captive.taken', (s, x) => { if (!on() || !s || !x) return; captors.set(x.id, s.attackerWon ? s.attacker : s.defender); });
    E.on('captive.released', x => {
      if (!on() || !x) return;
      const f = captors.has(x.id) ? captors.get(x.id) : -1; captors.delete(x.id);
      const g = G();
      if (f < 0 || f === g.player || !x.gear) return;
      if (rng('confiscate', x.id).nextDouble() >= CFG.confiscate.aiP) return;
      for (const sl of SLOTS) { const u = x.gear[sl]; if (u == null) continue; const r = instRec(u); if (!r || r.bound || TIER[ITEM.get(r.id).rarity] < 2) continue; place(u, 'arm', f); }
      aiEquip(f);
    });
    E.on('captive.executed', (x) => {
      if (!on() || !x) return;
      const g = G();
      const f = captors.has(x.id) ? captors.get(x.id) : (g.cities[x.city] ? g.cities[x.city].owner : -1);
      captors.delete(x.id);
      spoils(x, f, 'executed');
    });
    E.on('faction.death', f => {
      if (!on()) return;
      const g = G(), fa = g.factions[f];
      if (!fa || !fa.armoury || !fa.armoury.length) return;
      const r = fa.ruler >= 0 ? g.generals[fa.ruler] : null;
      const c = r ? g.cities[r.city] : null;
      const to = c && c.owner >= 0 && c.owner !== f ? c.owner : -1;
      for (const u of fa.armoury.slice()) { if (to >= 0) place(u, 'arm', to); else if (c) place(u, 'city', c.id); }
      if (to >= 0 && to !== g.player) aiEquip(to);
    });
    E.on('conquest.apply', (s, o) => {
      if (!on() || !s || !o || !o.won) return;
      const g = G(), st = S();
      if (o.attacker === g.player && s.target && !st.clues[s.target.key]) {
        const clue = clueFor(s.target);
        if (clue) { st.clues[s.target.key] = 1; g.addLog(s.target.name + '：' + clue); NEWS.push(clue); }
      }
    });
    E.on('command', (cmd, res) => {
      if (!on() || cmd !== 'search' || !res || res.outcome === 'item' || res.city == null) return;
      const g = G(), c = g.cities[res.city];
      if (!c || c.owner !== g.player) return;
      const clue = clueFor(c);
      if (clue && rng('clue', c.id, res.gen).nextDouble() < CFG.hidden.cluePFail) { Items.lastClue = clue; g.addLog('城中老人说：' + clue); NEWS.push(clue); }
    });
  }
  const NEWS = [];
  if (SG.Commands) SG.Commands.searchItem = (c, x) => searchHidden(c, x);
  installEvents();

  // 设置：阵斩范围（DESIGN-V4 §20 问题 8，Decisions.slayRulersOnly 为缺省）
  if (SG.Settings && typeof SG.Settings.define === 'function' && !SG.Settings.has('items.slay')) {
    SG.Settings.define({ id: 'items.slay', tab: 'game', order: 60, type: 'seg', label: '阵斩', sub: '战场上会心击溃、单挑击倒时可能阵斩的对象（ITEMS §8.2）',
      options: [['rulers', '只限君主'], ['all', '所有武将']], default: () => (CFG.slayRulersOnly ? 'rulers' : 'all'),
      store: { key: 'sanguozhi2_items_v4', path: 'slay' }, owner: 'items' });
  }

  // ================================================================ 存档与迁移（INTEGRATION §6）==
  function migrate(g, from) { if (!g.items) setup(g); else load(g); }
  function load(g) {
    const prev = SG.G; SG.G = g;
    try { ensure(g); IDX = { g: null, count: null }; idx(); bump(); installHooks(); } finally { SG.G = prev; }
  }

  // ================================================================ 目录校验（§17.1）==
  function validateDuel(it) {
    const out = [];
    for (const k of Object.keys(it.stats || {})) if (/^d[A-Z]/.test(k) && DUEL_ITEM_EFFECTS.stats.indexOf(k) < 0) out.push(it.id + ': duel stat ' + k + ' is not in DUEL_ITEM_EFFECTS');
    if (it.ability) for (const e of it.ability.effects) {
      for (const f of DUEL_ITEM_EFFECTS.forbidden) if (e[f] || e.t === f) out.push(it.id + ': forbidden field ' + f);
      if (!isDuelEffect(e.t)) continue;
      const W2 = DUEL_ITEM_EFFECTS.effects[e.t];
      if (!W2) { out.push(it.id + ': duel effect ' + e.t + ' is not in DUEL_ITEM_EFFECTS'); continue; }
      for (const [k, lim] of Object.entries(W2)) {
        const v = e[k];
        if (v === undefined) continue;
        if (Array.isArray(lim) && typeof lim[0] === 'number' && typeof v === 'number' && (v < lim[0] - 1e-9 || v > lim[1] + 1e-9)) out.push(it.id + ': ' + e.t + '.' + k + ' = ' + v + ' outside [' + lim + ']');
        if (Array.isArray(lim) && typeof lim[0] === 'string' && lim.indexOf(v) < 0) out.push(it.id + ': ' + e.t + '.' + k + ' = ' + v + ' not one of ' + lim.join('/'));
      }
      if (e.t === 'dProjectile' && (e.dmg || 0) * (e.count || 1) > DUEL_ITEM_EFFECTS.effects.dProjectile.hp[1]) out.push(it.id + ': projectile over 4 HP');
      if (e.t === 'dProc' && e.eff === 'knockdown' && !(e.on === 'heavyFull' || (e.p !== undefined && e.p < 1))) out.push(it.id + ': guaranteed knockdown only on full-charge heavies');
    }
    return out;
  }
  function checkCatalogue(data) {
    data = data || D;
    const P = [], st = {};
    const items = data.items;
    const byId = new Map(items.map(x => [x.id, x]));
    const cnt = f => items.filter(f).length;
    const SC = { classic: null, world: null };
    if (SG.Scenarios) for (const sid of ['classic', 'world']) {
      const sc = SG.Scenarios[sid];
      if (!sc) continue;
      SC[sid] = { factions: sc.Factions.map(l => l.split('|')[0]), cities: sc.Cities.map(l => l.split('|')[0]), gens: new Set(sc.Generals.map(l => l.split('|')[0])) };
    }
    st.weapons = cnt(x => x.cat === 'weapon'); st.gear = cnt(x => x.cat === 'gear');
    if (st.weapons < 250) P.push('weapons < 250: ' + st.weapons);
    if (st.gear < 250) P.push('gear < 250: ' + st.gear);
    st.byRarity = {}; for (const r of RARITIES) st.byRarity[r] = cnt(x => x.rarity === r);
    if (st.byRarity.legendary !== 4) P.push('legendary != 4: ' + st.byRarity.legendary);
    if (st.byRarity.epic !== 20) P.push('epic != 20: ' + st.byRarity.epic);
    const cults = Object.keys(data.cultures);
    for (const c of cults) {
      if (!items.some(x => x.culture === c)) P.push('culture without items: ' + c);
      if (!items.some(x => x.culture === c && TIER[x.rarity] >= 3)) P.push('culture without epic / legendary: ' + c);
    }
    const ids = new Set(), names = new Map(), abN = new Map(), abE = new Map(), actN = new Map(), icons = new Map();
    for (const it of items) {
      if (ids.has(it.id)) P.push('dup id ' + it.id); ids.add(it.id);
      if (names.has(it.name)) P.push('dup name ' + it.name); names.set(it.name, it.id);
      if (TIER[it.rarity] >= 2 && !it.ability) P.push('rare+ without ability: ' + it.id);
      if (it.ability) {
        if (it.ability.learnable !== false) P.push('ability must be learnable: false: ' + it.id);
        if (abN.has(it.ability.name)) P.push('dup ability name ' + it.ability.name); abN.set(it.ability.name, it.id);
        const sig = JSON.stringify(it.ability.effects);
        if (abE.has(sig)) P.push('dup ability effects ' + it.id + ' = ' + abE.get(sig)); abE.set(sig, it.id);
        for (const e of it.ability.effects) {
          const allowed = EFFECTS[e.t];
          if (allowed === undefined) { P.push('unknown effect id ' + e.t + ' in ' + it.id); continue; }
          const al = new Set(allowed.split(' ').filter(Boolean));
          for (const k of Object.keys(e)) if (k !== 't' && !al.has(k)) P.push('effect ' + e.t + ' has unknown param ' + k + ' (' + it.id + ')');
          if (e.name) { if (actN.has(e.name)) P.push('dup active name ' + e.name); actN.set(e.name, it.id); }
          if (e.t === 'active' && SG.Specials && SG.Specials.validateItemActive) for (const q of SG.Specials.validateItemActive(e, it)) P.push(q);
          if (e.t === 'activeChoice' && SG.Specials && SG.Specials.validateItemActive) for (const o of e.options || []) for (const q of SG.Specials.validateItemActive(Object.assign({ name: e.name + '·' + o.label }, o), it)) P.push(q);
        }
        if (TIER[it.rarity] >= 2) {
          const ab = it.ability;
          if (!(ab.procFx && ab.procFx.dur <= 0.6 && ab.procFx.drawCalls === 1 && ab.procFx.pooled)) P.push('rare+ without a valid procFx: ' + it.id);
          if (!(ab.sfx && ab.sfx.id && ab.sfx.family)) P.push('rare+ without sfx: ' + it.id);
        }
      }
      for (const q of validateDuel(it)) P.push(q);
      if (it.rarity === 'rare' && !(it.visual && it.visual.model && it.visual.model.signature && it.visual.model.signature.tris <= 200)) P.push('rare without a signature part ≤ 200 tris: ' + it.id);
      if (TIER[it.rarity] >= 2 && it.visual && it.visual.icon) {
        const key = JSON.stringify([it.visual.icon.shape, it.visual.icon.colors, it.visual.model.parts, it.visual.model.signature || null]);
        if (icons.has(key)) P.push('rare+ icon tuple not unique: ' + it.id + ' = ' + icons.get(key)); icons.set(key, it.id);
      }
      for (const [k, v] of Object.entries(it.stats || {})) {
        if (Array.isArray(v)) continue;
        if (typeof v !== 'number' || !isFinite(v)) { P.push('stat not finite ' + it.id + ' ' + k); continue; }
        const sk = data.statKeys[k];
        if (!sk) { P.push('unknown stat key ' + k + ' (' + it.id + ')'); continue; }
        if (typeof sk.cap === 'number' && Math.abs(v) > sk.cap + 1e-9) P.push('stat over its cap: ' + it.id + ' ' + k + ' ' + v + ' > ' + sk.cap);
      }
      // 价格带（§4.1）
      const p = it.price;
      if (it.rarity === 'common' && !(p >= 50 && p <= 100)) P.push('common price outside 50–100: ' + it.id + ' ' + p);
      if (it.rarity === 'uncommon' && !(p >= 190 && p <= 400)) P.push('uncommon price outside 190–400: ' + it.id + ' ' + p);
      if (it.rarity === 'rare' && p != null && !(p >= 840 && p <= 1750) && it.id !== 'dilu') P.push('rare shop price outside 840–1750: ' + it.id + ' ' + p);
      if (it.rarity === 'legendary' && p != null) P.push('legendary must not be sold: ' + it.id);
      if (it.rarity === 'epic' && p != null && !(p >= 6000 && p <= 9000)) P.push('epic price outside 6000–9000: ' + it.id);
      if (it.duel && !(data.duelWeapons[it.duel.key] || data.duelWeapons[it.duel.key + '*'])) P.push('unknown duel key ' + it.duel.key + ' (' + it.id + ')');
      if (it.cat === 'weapon' && it.slot === 'weapon' && !it.duel) P.push('weapon without a duel record: ' + it.id);
      if (!it.scenarios || !it.scenarios.length) P.push('no route in any scenario: ' + it.id);
    }
    // 剧本：每个势力都有专属兵器；专属兵器不在任何奖池；赫拉克勒斯之棍只在世界剧本；每剧本 ≥ 255 / 255（异域奇珍之外）
    st.reach = {};
    for (const sid of ['classic', 'world']) {
      const T = data[sid];
      if (SC[sid]) for (const fk of SC[sid].factions) if (!T.rulers[fk]) P.push(sid + ' faction without ruler-unique: ' + fk);
      for (const [pk, list] of Object.entries(T.tournament || {})) for (const id of list) { const it = byId.get(id); if (!it) P.push('unknown item in tournament pool ' + id); else if (it.bound || (it.availability[sid] || {}).kind === 'ruler') P.push('ruler-unique in a prize pool: ' + id + ' ' + sid + '/' + pk); }
      const reach = items.filter(x => x.availability[sid] && !(x.availability[sid].kind === 'tournament' && x.availability[sid].pool === 'foreign'));
      st.reach[sid] = { weapons: reach.filter(x => x.cat === 'weapon').length, gear: reach.filter(x => x.cat === 'gear').length };
      if (st.reach[sid].weapons < 255) P.push(sid + ': weapons reachable outside the roll < 255: ' + st.reach[sid].weapons);
      if (st.reach[sid].gear < 255) P.push(sid + ': gear reachable outside the roll < 255: ' + st.reach[sid].gear);
      for (const it of items) if (it.bound && it.availability[sid] && it.availability[sid].kind !== 'ruler') P.push('ruler-unique on a non-slaying route: ' + it.id + ' ' + sid);
      // 每座城的凡品池 ≥ 4，含一件兵器与一件甲胄
      for (const [key, s] of Object.entries(data.shops[sid])) {
        const pl = (s.common || []).concat(s.commonForeign || []).map(id => byId.get(id)).filter(Boolean);
        if (pl.length < 4) P.push(sid + ' shop pool < 4: ' + key);
        if (!pl.some(x => x.slot === 'weapon')) P.push(sid + ' shop without a weapon: ' + key);
        if (!pl.some(x => x.slot === 'armour' || x.slot === 'helmet' || (x.slot === 'offhand' && x.cat === 'gear'))) P.push(sid + ' shop without armour: ' + key);
        if (SC[sid] && SC[sid].cities.indexOf(key) < 0) P.push(sid + ' shop for an unknown city ' + key);
      }
      if (SC[sid]) for (const ck of SC[sid].cities) if (!data.shops[sid][ck]) P.push(sid + ' city without a shop row: ' + ck);
      if (SC[sid]) for (const s of T.signature) if (!SC[sid].gens.has(s.holder)) P.push(sid + ' signature holder not in scenario: ' + s.holder);
    }
    const clava = byId.get('clava');
    if (!clava || clava.availability.classic) P.push('赫拉克勒斯之棍 must be world-only');
    // 起始装备表：每个文化的盔甲、轻甲都有；kits 指向的都是凡品
    for (const [c, k] of Object.entries(data.kits)) {
      for (const [slot, id] of Object.entries(k)) { if (slot === 'byType') continue; if (id && !(byId.get(id) && byId.get(id).rarity === 'common')) P.push('kit piece not a common: ' + c + '.' + slot + ' ' + id); }
      for (const [t, id] of Object.entries(k.byType || {})) if (id && !(byId.get(id) && byId.get(id).rarity === 'common' && byId.get(id).type === t)) P.push('kit weapon wrong: ' + c + '.' + t + ' ' + id);
    }
    st.abilities = cnt(x => x.ability); st.actives = cnt(x => x.ability && x.ability.effects.some(e => e.t === 'active' || e.t === 'activeChoice'));
    if (st.actives !== 29) P.push('宝具 items != 29: ' + st.actives);
    return { problems: P, stats: st };
  }
  // 载入时：白名单之外的单挑字段即报错（COMBAT-V4 §15 (15)）
  if (D) { const bad = []; for (const it of D.items) for (const q of validateDuel(it)) bad.push(q); if (bad.length) throw new Error('SG.Items: duel fields outside DUEL_ITEM_EFFECTS: ' + bad.slice(0, 5).join('; ')); }

  // ================================================================ 宝具（specials.js 读）==
  // 武将装备上的主动能力：[{ item, idx, opt, name, kind, params…, fx, extras }]（要求不满足或「仅政治」时没有）
  function actives(x) {
    const out = [];
    for (const e of calc(x).list) {
      if (e.req === 'none' || e.req === 'polOnly' || !e.item.ability) continue;
      e.item.ability.effects.forEach((q, i) => {
        if (q.t === 'active') out.push(Object.assign({}, q, { item: e.id, idx: i, opt: -1, slot: e.slot, glow: e.item.visual && e.item.visual.icon ? e.item.visual.icon.glow : null, half: e.req === 'half' }));
        else if (q.t === 'activeChoice') (q.options || []).forEach((o, j) => out.push(Object.assign({}, o, { t: 'active', name: o.label, choice: q.name, item: e.id, idx: i, opt: j, slot: e.slot, glow: e.item.visual && e.item.visual.icon ? e.item.visual.icon.glow : null, half: e.req === 'half' })));
      });
    }
    return out;
  }

  Object.assign(Items, {
    CFG, SLOTS, SLOT_NAME, RARITIES, TIER, DUEL_ITEM_EFFECTS, EFFECTS, saveKeys: ['items'],
    decide(k, v) { if (v === undefined) delete OVR[k]; else OVR[k] = v; bump(); return dec(k); },
    get data() { return D; },
    setData(d) { setData(d); bump(); },
    get, all: () => (D ? D.items : []), tier: tierOf, isShield, styleOf, displayName, valueOf, giftK,
    rarityInfo: r => (D ? D.rarities[r] : null),
    price: id => { const it = ITEM.get(id); return it ? basePrice(it) : 0; },
    shopCities: (id, sid) => (SHOP_CITIES[sid || sidOf(G())].get(id) || []).slice(),
    inst: instRec, instOf: instView, genOf, count, factionOf: factionOfInst, armoury: armouryOf,
    instancesOf(f) { const g = G(), s = S(); if (!s) return []; return Object.keys(s.inst).filter(u => factionOfInst(+u) === f).map(Number); },
    setup, init: setup, migrate, load, ensure,
    // 商店
    shopTier, slots: slotsOf, cityK, restock, restockAll: () => restockAll(null), entryPrice, sellPrice, sellable: (id, uid) => sellable(ITEM.get(id), uid != null ? instRec(uid) : null),
    canTrade, buy, sell, openMerchant, payCityFor, shopRow: c => shopRow(c),
    // 获取
    searchHidden, hiddenIn, clueFor, hiddenP, spoils, slay, slayChance, rollSlay, slayRule, disarm, award, grant, pool, offers, tourneyPrize, titleRelic,
    takeReveals() { return REVEALS.splice(0, REVEALS.length); }, takeNews() { return NEWS.splice(0, NEWS.length); }, lastClue: null,
    gift, giftDisplace, fits, takeCost: (x, uid, replacement) => takeCost(x, uid, replacement),
    // 外交礼物（§6.6）：把本势力的一件装备送进 t 的府库（专属、名将旧物、镇城之宝不送；从武将身上取下照常计忠诚代价）
    transfer(uid, t, opts) {
      const g = G(), r = instRec(uid);
      if (!on() || !r || !g.factions[t]) return { ok: false, why: '无此物。' };
      const it = ITEM.get(r.id), a = avail(it, sidOf(g));
      if (r.bound || (a && (a.kind === 'signature' || a.kind === 'cityTreasure'))) return { ok: false, why: '此物不能送人。' };
      if (opts && opts.from >= 0 && factionOfInst(uid) !== opts.from) return { ok: false, why: '不是本势力之物。' };
      if (r.loc === 'gen') { const u = Gear.unequip(g.generals[r.owner], it.slot, { toArmoury: true }); if (!u.ok) return u; }
      place(uid, 'arm', t);
      if (t !== g.player) aiEquip(t);
      return { ok: true, item: it };
    },
    // 外交礼物（§6.6）：一件宝物折合的金 = 价值 × giftK（givers = [君主, 使者]，「以宝物赏赐 / 外交」的持有者倍数取最大）
    giftValue(x, givers) { const id = typeof x === 'number' || /^\d+$/.test(String(x)) ? (instRec(x) || {}).id : x; return id ? Math.round(valueOf(id) * giftK(id, givers)) : 0; },
    // 电脑
    score, role, aiEquip, aiShop, aiSell, aiHiddenSearch, monthItems, loyAuraSpecial, spendGear, gearCredit, gearShare, stats: ST,
    // 测试用：电脑买装预检所用的保留金（应与 SG.AIBudget.reserve(c).gold 相等）
    reserveGold: c => reserveGold(c, officersByCity(c.owner)),
    resetStats() { for (const k of Object.keys(ST)) ST[k] = typeof ST[k] === 'object' ? {} : 0; ST.shopRuns = { calls: 0, noMoney: 0, tooCheap: 0, noCand: 0, cand: 0 }; },
    // 图鉴
    markSeen, markOwned, collection,
    // 校验
    checkCatalogue, validateDuel,
    // 宝具
    actives,
    // 世界 / 内政能力的接线点（INTEGRATION 请求里列出调用处；没人调用时它们什么也不做）
    duelLure: x => on() && abil(x, 'duelLure').length > 0,                       // BattleModel.duelAccepts：持有者发起的单挑必定应战
    freeUses, useFree,                                                           // 传国玉玺：每月 1 次不耗令牌的外交
    relFloor,                                                                    // 径路刀：同盟期间两国好感不低于 +40
    scout(x) { return on() ? maxOf(abil(x, 'scout'), 'k') : 0; }, scouted,         // 浑天仪 / 地理学：可查看几城之内敌城的武将与装备
    defendCity: c => (on() ? defendCityK(c) : 0),                                // 坐镇守城：守方防御 × (1 + k)
    captureP, rescueAt,                                                          // Conquest.apply 的俘虏几率；battle-model 主将溃散时记救主
    critTaken: (p, t) => (on() && t ? Gear.battle.critTaken(p, t) : p),
    // 接线标记：调用方接上之后（INTEGRATION 请求）在自己的文件里设 true；没接上时商店 / 人物卡把这些效果标作「暂未生效」，
    // 不让玩家为不起作用的数值掏钱（pendingEffects）
    captureWired: false,   // js/strategy-ai.js 的 Conquest.apply 用 SG.Items.captureP 判被俘：脱身（escape / escapeK / decoy）、救主、擒将
    freeWired: false,      // 指令代码用 freeUses / useFree：传国玉玺每月免令牌外交
    relFloorWired: false,  // 外交 / 好感代码用 relFloor：径路刀的同盟好感下限
    scoutWired: false,     // 情报 / 地图用 scouted：浑天仪、地理学查看敌城
    pendingEffects(x) {
      const it = itemOf(x); if (!it) return [];
      const out = [], add = t => { if (out.indexOf(t) < 0) out.push(t); };
      const cap = !Items.captureWired;
      if (cap && it.stats && it.stats.escape) add('溃散时脱身');
      for (const e of it.ability ? it.ability.effects : []) {
        if (cap && (e.t === 'escapeK' || e.t === 'decoy')) add('溃散时脱身');
        else if (cap && e.t === 'rescue') add('救主');
        else if (cap && e.t === 'capture') add('擒将');
        else if (!Items.freeWired && e.t === 'free') add('免令牌' + ({ diplomacy: '外交' }[e.cmd] || '指令'));
        else if (!Items.relFloorWired && e.t === 'dipPlus' && e.relFloor) add('同盟好感下限');
        else if (!Items.scoutWired && e.t === 'scout') add('查看敌城');
      }
      return out;
    },
    installHooks, providers: PROVIDERS,
  });
  SG.Items = Items;
  SG.Gear = Gear;
})();
