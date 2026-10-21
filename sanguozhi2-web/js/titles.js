'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 官爵（TITLES.md；INTEGRATION §3–§6）
   纯逻辑：无 THREE / DOM，Node 可测（tests/titles.js）。数据：js/titles-data.js（SG.TitlesData，由 tools/gen-titles.js 生成）。
   载入：model.js、items.js 之后，engage.js / migrate-v4.js / strategy-ai.js 之前（INTEGRATION §11）。
   本模块从不取 SG.Random（TITLES §7.7）：开着时随机数流与关着时逐一对应，只有门槛与数量不同。所有战斗与内政数字都以
   「提供者」交给 SG.Mods / SG.Econ（INTEGRATION §3.3），本模块自己从不乘战斗或内政的数。

   开关：SG.Titles.enabled（缺省 true；关掉时不建任何状态、提供者返回 0 / 缺省、电脑不授官，即第四版去掉官爵）
   可调的数一律在 SG.Titles.CFG（一处）；官位、品阶、位阶、名额、礼金、忠诚的数在 titles.json（SG.TitlesData）。
   DESIGN-V4 §20 没有官爵的待决问题；TITLES §14 的开放问题都按推荐默认做（Q2 位阶只管名额、Q3 一人势力无官、Q6 电脑不用许官、
   Q7 礼金先由本城付、Q10 电脑无政绩经验）。

   ---- 存档（TITLES §10.4，INTEGRATION §6.1）----
   gen.office     = { post: 'w4b', name: 2, tier: 4, fam: 'war', since, seq, faction, month, suspended } | 没有该字段
   gen.officeBest = 1..5（本势力授过的最高品阶：每品的授官忠诚只给一次；换势力时清零）| 没有该字段
   gen.officeLog  = [[monthIndex, postId, nameIdx, kind]]（≤ 8 条；kind：grant promote move revoke demote suspend restore lost）
   faction.rank   = 1..5        G.officeSeq = 授官计数（虚衔先后的依据；顶层存档键 'officeSeq'）
   faction.officeCredit = { c, mi }（电脑势力的礼金额度，CFG.ai；第 7 相用过才有）  faction.officeGift = { year, gold }（玩家本年礼金）
   这些字段只在有状态时才出现（授过官的武将才有 office / officeBest / officeLog），关掉官爵的存档与 v4 去掉官爵相同。

   ---- SG.Titles（TITLES §10.2）----
   enabled CFG data FAMS · families(gen) family(gen) base(gen, fam) postsFor(gen) post(id) tier(t)
   eligible(gen, postId, opts?) requirements(gen, postId, f?) → [{ key, ok, text }]      merit(gen, fam?)
   power(f) rank(f) rankInfo(f) → { rank, name, power, next, nextName, unlocks, progress } caps(f) → [5]
   board(f) → [{ tier, grade, used, cap, req, locked, lockWhy, posts: [{ post, name, seats: [{ gen, name, state, why, cand }] }] }]
   officeOf(gen) → { post, tier, fam, branch, role, name, orig, attest, note, rank, seal, nameIdx, since, suspended, active, captive, faction } | null
   nameOf(gen) names(f, postId) rulerRankName(f, rank?) insignia(f, tier) effects(postId) → [{ key, zh, value, text }] effectText(key, v)
   bonus(gen, key) sideBonus(model, side, key) sideRes(model, side) cityAdmin(c) cityBonus(c, key) natBonus(f, key) garrison(c)
   effLoy(gen) persuadeRes(gen) discordRes(gen) holders(f) curTier(gen)
   canGrant(f, gen, postId, nameIdx?, opts?) → { ok, why, cost, payFrom, loyalty, passedOver: [{ gen, loss }], name, nameIdx, kind, reqs }
   grant(f, gen, postId, nameIdx?, opts?) → { ok, why, office, cost, payFrom, loyalty, passedOver, ceremony, kind, line }
   revoke(gen, why?) → { ok, why, loyalty }   demote(gen, postId) → { ok, why, loyalty }   setName(gen, nameIdx) → bool
   canRevoke(gen) / revokeCost(gen) / demoteCost(gen, postId)（确认卡的预告）
   promiseBonus(f, gen) → { postId, tier, add, cost } | null（登用的「许以官职」，TITLES §6.7）
   hireWithPromise(target, recruiter, f, cityId, opts?) → { ok, hired, office, promise }（成功时立即授官）
   candidates(f, postId) → [{ gen, ok, why, merit, stat, cur }]（候选名单，合格者在前）
   validate(gen) validateAll() refresh(f) → { rankFrom, rankTo, suspended, restored }   monthStart() → [新闻]
   aiManage(f) → [授官结果]   aiManageAll()   startCourt()（新局：电脑的开局官职，不收礼金）   aiCredit(f) → 礼金额度 | null
   fillByMerit(f, apply?) → [{ gen, post, kind, cost, payFrom }]
   pending(f) → n（「官制」按钮的角标：有合格人选的空缺数）   history(gen) → [文字]   takeNews() / takeEvents()
   init(G) / migrate(G, from) / load(G, data) / saveKeys ['officeSeq']（SG.MigrateV4 自动发现：TITLES 第二个迁移）
   stats（本局统计：grants gifts passedOver suspends restores revokes demotes aiRefused ms）

   提供者（SG.Mods.provide，owner 'titles'，when = SG.Titles.enabled）：titleAtk titleDef cmdAtk cmdDef titleMorale titleCrit
     titleCritDuel titleDmgDuel titleDev titleCity titleNat titleTrain titleSearch titleHire titleDip titleDiscord titlePersuade
     persuadeRes discordRes titleShield titleLoyAura admin cityDef popGrowth titleTac titleTacHit tacRes
   月末（StrategyAI，INTEGRATION §6.3）：'defections' 之后 validateAll（出奔者去官）· 第 6 相 'titles' = monthStart ·
     第 7 相 onSpend('title') = aiManage（礼金经 SG.AIBudget，kind 'title'）
   事件（SG.Events）：command（登用、拉拢后 validate；拉拢献城与开发后 refresh）、conquest.apply、captive.released / executed、
     faction.succession、faction.death、general.slain；发出 'title.grant' (gen, res)、'title.revoke' (gen, res)、'title.rank' (f, from, to)
   ========================================================================== */
(function () {
  const SG = window.SG;
  const G = () => SG.G;
  const D = () => SG.TitlesData || null;

  // ================================================================ 配置（一处）==
  const CFG = {
    passedOver: { loss: 4, max: 3, gap: 10 },  // §6.1 第 4 条：功绩高出 10 以上的同家族人选每人 −4，至多 3 人
    demote: { base: 4, perTier: 4 },           // §6.4：降 k 品 −(4 + 4k)
    suspendedRevokeK: 0.5,                     // §6.4：罢免虚衔只扣一半（向上取整）
    promisePerTier: 0.05,                      // §6.7：许官 +5 pp × 品阶（登用概率上限 0.95 由 SG.Econ 截断）
    meritLevelK: 3,                            // §8：功绩 = 家族属性 + 3 × 等级
    branch: { warB: 92, warGap: 35, intelPolGap: 8 },   // §8 电脑的分支偏好
    logMax: 8,                                 // §10.4 officeLog 至多 8 条
    aiGuard: 12,                               // 电脑每品阶至多授 12 次（防死循环）
    hintsMax: 3,                               // 月初「某某已可出任五品官」的提示每月至多 3 条
    clock: null,                               // 统计计时的时钟（null = performance.now）
    live: {},                                  // hookLive 的强制值（{ tac: true } …；缺省按载入的模块判断）
    aiNewsTier: 4,                             // 他国三品以上的任命，且与玩家接壤时才见于记录（§9.6）
    // §8 电脑「花多少」（INTEGRATION §5.1 的钱袋之内，官爵自己的规则；与 ITEMS 的买装额度同理）：
    //   礼金额度：每个电脑势力每月记 creditOfPurse × 钱袋份额（SG.AIBudget.share）× 本月总收入，至多 creditCapMonths 个月；
    //   礼金要钱袋与这份额度都够。开局没有官职、钱袋又空，额度不设时电脑把头两年的钱袋几乎全花在礼金上（第 1 年 16 %、
    //   第 2 年 10 % 的总收入，见 tests/titles.js §9），弱小的势力因此早早被吞并；额度把每年的礼金压在总收入的约 5 % 以内
    //   （TITLES §12.1「礼金 ≤ 总收入 5 %」逐年成立，而不只是 25 年的平均）。creditShare：数字时直接当作「总收入的份额」
    //   （测试与对照用）；creditOfPurse = null 且 creditShare = null 时不设额度（只受钱袋限制，即旧行为）
    //   startCourt：新局（GameState.newGame，§8「新局为每个电脑势力跑一次」）电脑势力按功绩任命开局的官职，不收礼金——剧本开始时
    //   他们已在任（开局人人 1 级，只有九品；钱袋为空，照 §8 付礼金一个也授不了）。玩家势力不受影响（§8：玩家从不自动授官）
    ai: { creditOfPurse: 0.23, creditShare: null, creditCapMonths: 24, startCourt: true },
  };
  const FAMS = Object.freeze(['war', 'intel', 'pol']);
  const STAT_ZH = { war: '武力', intel: '智力', pol: '政治' };
  const NUM_ZH = ['零', '一', '二', '三', '四', '五', '六', '七', '八', '九', '十'];
  const CHANGE_KINDS = new Set(['grant', 'promote', 'move', 'revoke', 'demote']);

  // ================================================================ 数据索引 ==
  let DATA = null, POST = {}, TIER = [], BY_TIER = [[], [], [], [], [], []];
  function index() {
    const d = D();
    if (d === DATA) return;
    DATA = d;
    POST = {}; BY_TIER = [[], [], [], [], [], []];
    TIER = d ? d.tiers : [];
    if (!d) return;
    for (const p of d.posts) { POST[p.id] = p; BY_TIER[p.tier].push(p); }
  }
  index();
  const tierOf = t => TIER[t - 1];

  const Titles = { enabled: true };
  const on = () => Titles.enabled !== false && !!D();

  // 统计（tests / 平衡测量用）
  const ST = { grants: 0, aiGrants: 0, gifts: 0, aiGifts: 0, passedOver: 0, aiPassedOver: 0, suspends: 0, restores: 0, revokes: 0, demotes: 0, lost: 0, aiRefused: 0, ms: 0, aiMs: 0, aiCalls: 0, monthMs: 0 };
  const NEWS = [];      // 给玩家的新闻（monthStart 之外产生的：拉拢、攻城之后的位阶变化 ……）
  const EVENTS = [];    // 给界面的事件（升阶仪式、授官仪式）：{ kind: 'rank'|'grant', … }
  // 计时（统计用；测试可设 CFG.clock = 进程 CPU 时间，免受机器负载影响）
  const now = () => (CFG.clock ? CFG.clock() : typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now());
  function emit(name, a, b, c) { const E = SG.Events; if (E) E.emit(name, a, b, c); }

  // ================================================================ 小工具 ==
  const isCaptive = x => !!(SG.Conquest && typeof SG.Conquest.isCaptive === 'function' && SG.Conquest.isCaptive(x));
  const isRuler = x => { const g = G(); return !!g && x.faction >= 0 && g.factions[x.faction] && g.factions[x.faction].ruler === x.id; };
  const mi = () => { const g = G(); return g ? g.monthIndex : 0; };
  const base = (x, fam) => (fam === 'war' ? x.war : fam === 'intel' ? x.intel : x.pol);
  const lvl = x => x.level | 0 || 1;
  function families(x) {
    const m = Math.max(x.war, x.intel, x.pol);
    const out = [];
    for (const f of FAMS) if (base(x, f) === m) out.push(f);
    return out;
  }
  const family = x => families(x)[0];
  // 不建数组的版本（电脑授官的热路径）：fam 是不是他的最高属性之一
  const inFamily = (x, fam) => base(x, fam) === Math.max(x.war, x.intel, x.pol);
  function merit(x, fam) { return (fam ? base(x, fam) : Math.max(x.war, x.intel, x.pol)) + CFG.meritLevelK * lvl(x); }

  // 官职是否仍然有效（没死、没换势力、没当君主）：懒检查，validate 之前效果也已正确
  const valid = x => !!(x && x.office && !x.dead && x.faction >= 0 && x.office.faction === x.faction && !isRuler(x) && POST[x.office.post]);
  // 生效：有效、不是虚衔、不在俘虏中
  const active = x => on() && valid(x) && !x.office.suspended && !isCaptive(x);
  const curTier = x => (valid(x) ? x.office.tier : 0);

  // ---- 每个势力的官职持有者（含虚衔、被俘者；只在授官 / 去官时变）----
  let holderIdx = null, holderG = null;
  function dirty() { holderIdx = null; }
  function holders(f) {
    const g = G();
    if (!g) return [];
    if (!holderIdx || holderG !== g) {
      holderIdx = new Map(); holderG = g;
      for (const x of g.generals) if (x.office && !x.dead && x.faction >= 0) {
        if (!holderIdx.has(x.faction)) holderIdx.set(x.faction, []);
        holderIdx.get(x.faction).push(x);
      }
    }
    // 索引在授官 / 去官时重建；其间换了势力而尚未 validate 的条目由使用处的 valid() / active() 滤掉
    return holderIdx.get(f) || EMPTY;
  }
  const EMPTY = Object.freeze([]);

  // ================================================================ 资格（§2）==
  function eligible(x, postId, opts) {
    index();
    const p = typeof postId === 'string' ? POST[postId] : postId;
    if (!on() || !p || !x || x.dead) return false;
    opts = opts || {};
    if (!opts.asFaction) {
      if (x.faction < 0 || isRuler(x) || isCaptive(x)) return false;
    }
    if (!inFamily(x, p.family)) return false;
    const tr = tierOf(p.tier);
    return lvl(x) >= tr.reqLevel && base(x, p.family) > tr.reqStat;
  }
  function postsFor(x) { index(); const fs = families(x); return DATA ? DATA.posts.filter(p => fs.indexOf(p.family) >= 0) : []; }
  // 本月是否已有任免（一人每月一次；名号更换不算）
  function changedThisMonth(x) {
    const m = mi();
    if (valid(x) && x.office.month === m) return true;
    const L = x.officeLog;
    if (!L || !L.length || lastChangeFaction(x) !== x.faction) return false;
    for (let i = L.length - 1; i >= 0 && L[i][0] === m; i--) if (CHANGE_KINDS.has(L[i][3])) return true;
    return false;
  }
  // 最近一次任免时的势力（罢免后 office 已清空：记在 lastFac 里；读档后按现状）
  function lastChangeFaction(x) { const v = lastFac.get(x.id); return v === undefined ? x.faction : v; }

  // ================================================================ 国势与位阶（§5）==
  function power(f) {
    const g = G(), d = D();
    if (!g || !d) return 0;
    const pw = d.rulerRanks.power;
    let P = 0;
    for (const c of g.cities) if (c.owner === f) P += pw.perCity + (c.land + c.industry + c.town) / pw.devDiv;
    return P;
  }
  function upRank(P) {
    const th = D().rulerRanks.thresholds;
    let up = 1;
    for (let k = 0; k < th.length; k++) if (P >= th[k]) up = k + 1;
    return up;
  }
  function stepRank(stored, P) {
    const RR = D().rulerRanks, th = RR.thresholds;
    const up = upRank(P);
    let r = stored >= 1 && stored <= 5 ? stored : up;
    if (up > r) r = up;
    else while (r > 1 && P < th[r - 1] * RR.hysteresis) r--;
    return r;
  }
  function rank(f) {
    const g = G();
    if (!g || f < 0 || !g.factions[f] || !D()) return 1;
    const fa = g.factions[f];
    return fa.rank >= 1 && fa.rank <= 5 ? fa.rank : upRank(power(f));
  }
  const capsOfRank = r => D().rulerRanks.caps[r] || D().rulerRanks.caps[1];
  function caps(f) { return on() ? capsOfRank(rank(f)).slice() : [0, 0, 0, 0, 0]; }

  // ================================================================ 名称（§3.2）==
  function nameSet(fKey, culture, postId) {
    const d = D();
    const ov = d.factionOverrides[fKey];
    if (ov && ov.posts && ov.posts[postId]) return ov.posts[postId].names;
    let c = culture || 'han';
    for (let guard = 0; c && guard < 4; guard++) {
      const cu = d.cultures[c];
      if (cu && cu.posts && cu.posts[postId]) return cu.posts[postId].names;
      c = d.cultureFallback[c] || (c === 'generic' ? null : d.cultureFallback['*'] || 'generic');
    }
    return d.cultures.generic.posts[postId].names;
  }
  function cultureRec(fKey, culture, field) {
    const d = D();
    const ov = d.factionOverrides[fKey];
    if (ov && ov[field]) return ov[field];
    let c = culture || 'han';
    for (let guard = 0; c && guard < 4; guard++) {
      const cu = d.cultures[c];
      if (cu && cu[field]) return cu[field];
      c = d.cultureFallback[c] || (c === 'generic' ? null : d.cultureFallback['*'] || 'generic');
    }
    return d.cultures.generic[field];
  }
  function facInfo(f) {
    const g = G(), fa = g && f >= 0 ? g.factions[f] : null;
    return { key: fa ? fa.key : '', culture: (fa && fa.culture) || 'han' };
  }
  function names(f, postId) { if (!D()) return []; const fi = facInfo(f); return nameSet(fi.key, fi.culture, postId); }
  function rulerRankName(f, r) {
    if (!D()) return '';
    const fi = facInfo(f);
    const rr = cultureRec(fi.key, fi.culture, 'rulerRanks') || [];
    return rr[(r || rank(f)) - 1] || '';
  }
  function insignia(f, t) { if (!D()) return ''; const fi = facInfo(f); const ins = cultureRec(fi.key, fi.culture, 'insignia') || []; return ins[t - 1] || ''; }
  function cultureOf(f) { return facInfo(f).culture; }
  function nameRec(x) {
    if (!x || !x.office || !POST[x.office.post]) return null;
    const list = names(x.office.faction, x.office.post);
    if (!list.length) return null;
    const i = Math.max(0, Math.min(list.length - 1, x.office.name | 0));
    return list[i];
  }
  function nameOf(x) { if (!valid(x)) return ''; const n = nameRec(x); return n ? n.zh : ''; }
  function postName(f, postId, idx) { const l = names(f, postId); return l.length ? l[Math.max(0, Math.min(l.length - 1, idx | 0))].zh : postId; }
  // 同一官位、同一势力的持有者各用不同的名号（用尽时从头再用）
  function freeNameIdx(f, postId, except, wanted) {
    const list = names(f, postId);
    const used = new Set();
    for (const h of holders(f)) if (h !== except && valid(h) && h.office.post === postId) used.add(h.office.name | 0);
    if (wanted !== undefined && wanted !== null && wanted >= 0 && wanted < list.length && !used.has(wanted)) return wanted;
    for (let i = 0; i < list.length; i++) if (!used.has(i)) return i;
    return 0;
  }

  // ================================================================ 效果（§7）==
  function bonus(x, key) {
    if (!x || !x.office || !active(x)) return 0;
    return POST[x.office.post].fx[key] || 0;
  }
  function sideBonus(model, side, key) {
    if (!on() || !model || typeof model.commander !== 'function') return 0;
    const cu = model.commander(side);
    return cu && cu.alive !== false && !(cu.confused > 0) && cu.gen ? bonus(cu.gen, key) : 0;
  }
  function sideRes(model, side) {
    if (!on() || !model || !Array.isArray(model.units)) return 0;
    let v = 0;
    for (const u of model.units) if (u && u.side === side && u.alive !== false && u.gen) v = Math.max(v, bonus(u.gen, 'tacRes'));
    return v;
  }
  // 城的主政者（INTEGRATION §4.6）：太守；太守是君主时，城中品阶最高的在任文官（品阶、政治、编号）；没有时君主本人；无太守 → null
  function cityAdmin(c) {
    const g = G();
    if (!g || !c || c.governor < 0) return null;
    const gov = g.generals[c.governor];
    if (!gov) return null;
    if (!on() || !isRuler(gov)) return gov;
    let best = null;
    for (const x of holders(c.owner)) {
      if (x.city !== c.id || !x.office || x.office.fam !== 'pol' || !active(x) || (g.absent && g.absent(x))) continue;
      if (!best || x.office.tier > best.office.tier || (x.office.tier === best.office.tier && (x.pol > best.pol || (x.pol === best.pol && x.id < best.id)))) best = x;
    }
    return best || gov;
  }
  function cityBonus(c, key) { const a = cityAdmin(c); return a ? bonus(a, key) : 0; }
  // 全国加成：本势力在任者中最大的一个（从不相加）
  function natBonus(f, key) {
    if (!on() || f < 0) return 0;
    let v = 0;
    for (const x of holders(f)) { const b = bonus(x, key); if (b > v) v = b; }
    return v;
  }
  // 镇守：城中在任武官的城防加成取最大值（INTEGRATION 的提供者 cityDef；SG.cityDefense 读）
  function garrison(c) {
    const g = G();
    if (!on() || !g || !c || c.owner < 0) return 0;
    let v = 0;
    for (const x of holders(c.owner)) {
      if (x.city !== c.id || !x.office || !(POST[x.office.post].fx.garrison > 0)) continue;
      if (g.absent && g.absent(x)) continue;
      const b = bonus(x, 'garrison'); if (b > v) v = b;
    }
    return v;
  }
  // 防叛、拉拢 / 离间抗性：任何官职（含虚衔）都有；被俘中不计
  const held = x => on() && valid(x) && !isCaptive(x);
  function effLoy(x) { return held(x) ? tierOf(x.office.tier).effLoy : 0; }
  function persuadeRes(x) { return held(x) ? tierOf(x.office.tier).persuadeRes : 0; }
  function discordRes(x) { return held(x) ? tierOf(x.office.tier).discordRes : 0; }

  // 钩子是否已接上（界面在没接上的效果后标「未生效」）：计策三项由 js/battle-model.js 读（W5 的 INTEGRATION 补丁），
  // 同盟加成要 同盟 指令传出使者（W8 的使者选择；电脑不结盟）。CFG.live[key] = true / false 可强制；缺省按源码判断。
  function hookLive(key) {
    const forced = CFG.live && CFG.live[key];
    if (forced === true || forced === false) return forced;
    if (key === 'tac' || key === 'tacHit' || key === 'tacRes') {
      const BM = SG.BattleModel, pr = BM && BM.prototype;
      if (!pr) return true;   // 没有战斗模块（例如只测规则）：不判
      const src = String(pr.tacK || '') + String(pr.tacticChance || '') + String(pr.tacHitAdj || '');
      return key === 'tac' ? /titleTac(?!Hit)/.test(src) : /titleTacHit|tacRes/.test(src);
    }
    if (key === 'dip') {
      const SS = SG.StrategyScreen, pr = SS && SS.prototype;
      if (!pr) return true;
      return /envoy/.test(String(pr.cmdDiplomacy || '') + String(pr.cmdAlly || ''));
    }
    return true;
  }

  // 效果的文字（UI）：pct → +8%，pp → +3pp，flat → +6（本城忠诚 +1/月）
  function fmtNum(v) { const r = Math.round(v * 10) / 10; return (r === Math.round(r) ? String(Math.round(r)) : r.toFixed(1)); }
  function effectText(key, v) {
    const e = D() && D().effects[key];
    if (!e) return key + ' ' + v;
    const sign = v >= 0 ? '+' : '−', a = Math.abs(v);
    if (e.unit === 'pct') return e.zh + ' ' + sign + fmtNum(a * 100) + '%';
    if (e.unit === 'pp') return e.zh + ' ' + sign + fmtNum(a * 100) + 'pp';
    return e.zh + ' ' + sign + fmtNum(a) + (key === 'loyAura' ? '/月' : '');
  }
  function effects(postId) {
    index();
    const p = POST[postId];
    if (!p) return [];
    return Object.keys(p.fx).map(k => ({ key: k, zh: D().effects[k] ? D().effects[k].zh : k, unit: D().effects[k] ? D().effects[k].unit : '', scope: D().effects[k] ? D().effects[k].scope : '', value: p.fx[k], text: effectText(k, p.fx[k]), live: hookLive(k) }));
  }

  // ================================================================ 记录 ==
  const lastFac = new Map();   // 武将编号 → 上次 validate 时的势力（换势力 → officeBest 清零；不进存档，读档时按现状重建）
  function logOf(x, kind, postId, nameIdx) {
    if (!x.officeLog) x.officeLog = [];
    x.officeLog.push([mi(), postId, nameIdx | 0, kind]);
    while (x.officeLog.length > CFG.logMax) x.officeLog.shift();
  }
  const isPlayer = f => { const g = G(); return !!g && f >= 0 && f === g.player; };
  function bordersPlayer(f) {
    const g = G();
    if (!g || g.player < 0 || f === g.player) return false;
    for (const c of g.cities) if (c.owner === f) for (const j of c.links) if (g.cities[j].owner === g.player) return true;
    return false;
  }
  function news(line) { NEWS.push(line); }

  // ---- 去官（死、换势力、继位）：不取随机数，不动忠诚 ----
  // 出缺的席位立刻交给等着复任的虚衔者（§6.6「名额空出时最老的先复任」）：官制画面与角标看到的空缺就是真空缺
  function clearOffice(x, why) {
    const o = x.office;
    if (!o) return;
    const name = postName(o.faction, o.post, o.name);
    const wasPlayer = isPlayer(o.faction);
    logOf(x, 'lost', o.post, o.name);
    delete x.office;
    ST.lost++;
    dirty();
    if (why === 'dead' && wasPlayer) news(`${x.name}卒，${name}出缺。`);
    else if (why === 'ruler' && wasPlayer) news(`${x.name}继承大业，${name}出缺。`);
    else if (why === 'faction' && wasPlayer) news(`${x.name}离去，${name}出缺。`);
    const g = G();
    if (!o.suspended && g && g.factions[o.faction] && g.factions[o.faction].alive !== false) enforce(o.faction);
  }
  function validate(x) {
    if (!x || (!x.office && !x.officeBest)) return;   // 从未任官（或 officeBest 已清零）：无事可做
    const g = G();
    if (x.office) {
      const o = x.office;
      if (x.dead) clearOffice(x, 'dead');
      else if (x.faction < 0 || x.faction !== o.faction) clearOffice(x, 'faction');
      else if (g && isRuler(x)) clearOffice(x, 'ruler');
      else if (!POST[o.post]) { delete x.office; dirty(); }
    }
    const prev = lastFac.get(x.id);
    if (prev !== undefined && prev !== x.faction && x.officeBest) x.officeBest = 0;
    if (x.office && x.officeBest === undefined) x.officeBest = x.office.tier;
    lastFac.set(x.id, x.faction);
  }
  function validateAll() { const g = G(); if (!g) return; for (const x of g.generals) validate(x); }

  // ---- 虚衔与复任（§6.6）：每品在任者多于名额时，最新授的先虚衔；名额空出时最老的先复任（须有官位席位）----
  // cause：'rank'（位阶变化，refresh）或缺省（席位空出：出缺、罢免、贬官）——只影响给玩家的新闻措辞
  function enforce(f, cause) {
    const out = { suspended: [], restored: [] };
    if (!on() || f < 0) return out;
    const cap = caps(f), hs = holders(f).filter(valid);
    // 快速判断：没有超额、也没有等着复任的虚衔 → 无事可做
    const nAct = [0, 0, 0, 0, 0, 0]; let nSus = 0;
    for (const x of hs) { if (x.office.suspended) nSus++; else nAct[x.office.tier]++; }
    let over = false;
    for (let t = 1; t <= 5; t++) if (nAct[t] > cap[t - 1]) over = true;
    if (!over && !nSus) return out;
    for (let t = 5; t >= 1; t--) {
      const act = hs.filter(x => x.office.tier === t && !x.office.suspended).sort((a, b) => a.office.seq - b.office.seq);
      while (act.length > cap[t - 1]) {
        const x = act.pop();
        x.office.suspended = true;
        logOf(x, 'suspend', x.office.post, x.office.name);
        out.suspended.push(x); ST.suspends++;
      }
      const sus = hs.filter(x => x.office.tier === t && x.office.suspended).sort((a, b) => a.office.seq - b.office.seq);
      for (const x of sus) {
        if (act.length >= cap[t - 1]) break;
        const p = POST[x.office.post];
        if (p.seats && act.filter(y => y.office.post === p.id).length >= p.seats) continue;
        x.office.suspended = false;
        logOf(x, 'restore', x.office.post, x.office.name);
        act.push(x);
        out.restored.push(x); ST.restores++;
      }
    }
    if (isPlayer(f)) {
      for (const x of out.suspended) news(`国势衰减，${x.name}的${nameOf(x) || postName(f, x.office.post, x.office.name)}暂为虚衔。`);
      for (const x of out.restored) news(cause === 'rank' ? `国势复振，${x.name}重领${nameOf(x)}。` : `席位空出，${x.name}重领${nameOf(x)}。`);
    }
    return out;
  }
  // 位阶：升即升，降有 10 % 回差；变化后整理虚衔
  function seatsLine(r) {
    const c = capsOfRank(r), parts = [];
    for (let t = 5; t >= 1 && parts.length < 2; t--) if (c[t - 1] > 0) parts.push(GRADE(t) + '官' + (NUM_ZH[c[t - 1]] || c[t - 1]) + '人');
    return parts.join('、');
  }
  const GRADE = t => (tierOf(t) ? tierOf(t).grade : t + '');
  const refreshedAt = new Map();   // 势力 → 最近一次 refresh 的月序（第 7 相不重复整理）
  function refresh(f, Pre) {
    const g = G();
    refreshedAt.set(f, mi());
    const res = { rankFrom: 1, rankTo: 1, suspended: [], restored: [] };
    if (!on() || !g || f < 0 || !g.factions[f]) return res;
    const fa = g.factions[f];
    const from = fa.rank >= 1 && fa.rank <= 5 ? fa.rank : upRank(power(f));
    const to = fa.alive === false ? from : stepRank(from, Pre !== undefined ? Pre : power(f));
    fa.rank = to;
    res.rankFrom = from; res.rankTo = to;
    const e = enforce(f, 'rank');
    res.suspended = e.suspended; res.restored = e.restored;
    if (to !== from) {
      const r = g.ruler(f);
      if (isPlayer(f)) {
        if (to > from) news(`${r ? r.name : g.factions[f].name}晋位为${rulerRankName(f, to)}！可授${seatsLine(to)}。`);
        else news(`国势衰减，${r ? r.name : g.factions[f].name}降为${rulerRankName(f, to)}。`);
        EVENTS.push({ kind: 'rank', f, from, to, up: to > from });
      }
      emit('title.rank', f, from, to);
    }
    return res;
  }
  function rankInfo(f) {
    const r = rank(f), P = power(f), th = D() ? D().rulerRanks.thresholds : [0];
    const next = r < 5 ? th[r] : null;
    let unlocks = '';
    if (r < 5) {
      const a = capsOfRank(r), b = capsOfRank(r + 1), parts = [];
      const opened = [], plus = [];
      for (let t = 1; t <= 5; t++) { const dlt = b[t - 1] - a[t - 1]; if (dlt <= 0) continue; if (a[t - 1] === 0) opened.push(t); else plus.push([t, dlt]); }
      if (plus.length && plus.every(q => q[1] === 1) && plus.length + opened.length >= 4) parts.push('各品再增一席');
      else for (const [t, dlt] of plus) parts.push(GRADE(t) + ' +' + dlt);
      for (const t of opened) parts.push('开' + GRADE(t));
      unlocks = parts.join('，');
    }
    const lo = th[r - 1] || 0;
    return { rank: r, name: rulerRankName(f, r), power: P, next, nextName: r < 5 ? rulerRankName(f, r + 1) : '', unlocks, progress: next ? Math.max(0, Math.min(1, (P - lo) / (next - lo))) : 1, caps: capsOfRank(r).slice() };
  }

  // ================================================================ 授官（§6）==
  const giftOf = (t, cur) => tierOf(t).grantGold - (cur ? tierOf(cur).grantGold : 0);
  // 电脑的付款城：与 SG.AIBudget.payer(f, cost, 'title') 结果相同（金减保留额最多的城，同额取编号小者；再用 canSpend 确认），
  // 但每城的保留额只算一次（payer 在排序的比较里反复计算 reserve，世界剧本里占了电脑授官一半以上的时间）
  // resCache：一次电脑授官（meritPlan）之内各城的保留额不变（授官不调动武将），只算一次
  function aiPayer(A, f, cost, resCache) {
    if (typeof A.reserve !== 'function' || typeof A.canSpend !== 'function') return A.payer(f, cost, 'title');
    const g = G();
    let best = null, bv = -Infinity;
    for (const c of g.cities) {
      if (c.owner !== f) continue;
      let r = resCache ? resCache.get(c.id) : undefined;
      if (r === undefined) { r = A.reserve(c).gold; if (resCache) resCache.set(c.id, r); }
      const v = c.gold - r;
      if (v > bv) { bv = v; best = c; }
    }
    return best && A.canSpend(best, cost, 'title') ? best : null;
  }
  // 电脑的礼金额度（CFG.ai，§8）：faction.officeCredit = { c: 额度, mi: 记账的月 }（随存档；只有电脑势力在第 7 相用过才有）。
  // 每月第一次用到时记一次 creditRate() × 本月总收入（SG.AIBudget.income：第 7 相 accrue 记下的），至多 creditCapMonths 个月。
  // 不设额度时返回 null（只受钱袋限制）
  function creditRate() {
    const A = CFG.ai || {};
    if (typeof A.creditShare === 'number') return A.creditShare;
    if (typeof A.creditOfPurse !== 'number') return null;
    const AIB = SG.AIBudget, D = SG.Balance && SG.Balance.Decisions;
    const share = AIB && +AIB.share > 0 ? +AIB.share : D && +D.aiPurseShare > 0 ? +D.aiPurseShare : 0.22;
    return A.creditOfPurse * share;
  }
  function creditOf(f) {
    const k = creditRate();
    if (k === null || !SG.AIBudget) return null;
    const g = G(), fa = g && g.factions[f];
    if (!fa) return null;
    let r = fa.officeCredit;
    if (!r || typeof r !== 'object') r = fa.officeCredit = { c: 0, mi: -1 };
    if (r.mi !== g.monthIndex) {
      const AIB = SG.AIBudget;
      const inc = AIB && AIB.income && typeof AIB.income.get === 'function' ? +AIB.income.get(f) || 0 : 0;
      r.c = Math.min((+r.c || 0) + k * inc, (+(CFG.ai.creditCapMonths) || 24) * k * Math.max(1, inc));
      r.mi = g.monthIndex;
    }
    return r;
  }
  function playerPayer(f, x, cost) {
    const g = G();
    if (!(cost > 0)) return null;
    const own = g.cities[x.city];
    if (own && own.owner === f && own.gold >= cost) return own;
    let best = null;
    for (const c of g.cities) if (c.owner === f && c.gold >= cost && (!best || c.gold > best.gold)) best = c;
    return best;
  }
  function tierActive(f, t, except) { let n = 0; for (const h of holders(f)) if (h !== except && h.office && h.office.tier === t && !h.office.suspended && valid(h)) n++; return n; }
  function postActive(f, postId, except) { let n = 0; for (const h of holders(f)) if (h !== except && h.office && h.office.post === postId && !h.office.suspended && valid(h)) n++; return n; }
  function seatFree(f, p, except) {
    const cap = caps(f)[p.tier - 1];
    if (cap <= 0) return { ok: false, why: '需位阶 ' + firstRankName(f, p.tier), locked: true };
    // 他本人已在这一品（平调）：不另占名额
    const self = except && valid(except) && except.office.tier === p.tier && !except.office.suspended ? except : null;
    if (tierActive(f, p.tier, self) >= cap) return { ok: false, why: `${GRADE(p.tier)}名额已满（${tierActive(f, p.tier, self)} / ${cap}）` + nextSeatHint(f, p.tier) };
    if (p.seats && postActive(f, p.id, except) >= p.seats) return { ok: false, why: '此职名额已满' };
    return { ok: true, why: '' };
  }
  function firstRankName(f, t) { for (let r = 1; r <= 5; r++) if (capsOfRank(r)[t - 1] > 0) return rulerRankName(f, r); return ''; }
  function nextSeatHint(f, t) {
    const r = rank(f), th = D().rulerRanks.thresholds;
    for (let k = r + 1; k <= 5; k++) if (capsOfRank(k)[t - 1] > capsOfRank(r)[t - 1]) return `——国势达到 ${fmtNum(th[k - 1])} 后增加`;
    return '';
  }
  // §2.2 的检查表（UI 次序）：家族、等级、属性、名额、礼金、本月已变动
  function requirements(x, postId, f, opts) {
    index();
    const p = POST[postId];
    const out = [];
    if (!p || !x) return out;
    opts = opts || {};
    if (f === undefined || f === null) f = x.faction;
    const g = G();
    if (!on()) { out.push({ key: 'off', ok: false, text: '官爵制度未开启' }); return out; }
    if (x.dead) out.push({ key: 'dead', ok: false, text: x.name + '已故' });
    if (!opts.asFaction && x.faction !== f) out.push({ key: 'faction', ok: false, text: '不是本势力的武将' });
    if (isRuler(x)) out.push({ key: 'ruler', ok: false, text: '君主不任官职' });
    // 刚以许官登用的俘虏（opts.hired）：Conquest 在处置完全部俘虏后才把他移出待处置名单，许下的官照授（§6.7）
    if (isCaptive(x) && !opts.hired) out.push({ key: 'captive', ok: false, text: '被俘中' });
    const fs = families(x);
    out.push({ key: 'family', ok: fs.indexOf(p.family) >= 0, text: fs.indexOf(p.family) >= 0 ? '最高属性：' + fs.map(q => STAT_ZH[q]).join(' / ') : `最高属性为${fs.map(q => STAT_ZH[q]).join(' / ')}，只能出任${fs.map(q => D().families[q].zh).join(' / ')}` });
    const tr = tierOf(p.tier);
    out.push({ key: 'level', ok: lvl(x) >= tr.reqLevel, text: lvl(x) >= tr.reqLevel ? `等级 ${lvl(x)} / ${tr.reqLevel}` : `需 ${tr.reqLevel} 级（现 ${lvl(x)} 级）` });
    if (tr.reqStat > 0) out.push({ key: 'stat', ok: base(x, p.family) > tr.reqStat, text: base(x, p.family) > tr.reqStat ? `${STAT_ZH[p.family]} ${base(x, p.family)} > ${tr.reqStat}` : `${STAT_ZH[p.family]}需高于 ${tr.reqStat}` });
    const s = seatFree(f, p, x);
    out.push({ key: 'seat', ok: s.ok, text: s.ok ? '有空缺' : s.why, locked: !!s.locked });
    const cur = curTier(x) && x.faction === f ? x.office.tier : 0;
    const cost = cur >= p.tier ? 0 : giftOf(p.tier, cur);
    if (cost > 0 && !opts.ai) {
      const pay = playerPayer(f, x, cost);
      out.push({ key: 'gold', ok: !!pay, text: pay ? `礼金 ${cost} 金（自 ${pay.name}）` : `礼金不足（需 ${cost} 金）` });
    }
    const ch = x.faction === f && changedThisMonth(x);
    out.push({ key: 'month', ok: !ch, text: ch ? '本月已有任免' : '本月可任免' });
    void g;
    return out;
  }

  // 被越级者（§6.1 第 4 条）：同家族、可任此品（不论名额）、现任低于此品、功绩高出 gap 以上 → 至多 max 人，功绩高者先
  function passedOverOf(f, x, p, pool) {
    const g = G();
    const mX = merit(x, p.family);
    const list = [];
    for (const y of pool || g.generalsOf(f)) {
      if (y.faction !== f || y.dead) continue;
      if (y === x || isRuler(y) || isCaptive(y) || families(y).indexOf(p.family) < 0) continue;
      if (curTier(y) >= p.tier) continue;
      const tr = tierOf(p.tier);
      if (!(lvl(y) >= tr.reqLevel && base(y, p.family) > tr.reqStat)) continue;
      const m = merit(y, p.family);
      if (m >= mX + CFG.passedOver.gap) list.push({ gen: y, merit: m });
    }
    list.sort((a, b) => b.merit - a.merit || a.gen.id - b.gen.id);
    return list.slice(0, CFG.passedOver.max).map(q => ({ gen: q.gen, loss: Math.min(q.gen.loyalty, CFG.passedOver.loss) }));
  }

  function canGrant(f, x, postId, nameIdx, opts) {
    index();
    opts = opts || {};
    const p = POST[postId];
    const res = { ok: false, why: '', cost: 0, payFrom: null, loyalty: 0, passedOver: [], name: '', nameIdx: 0, kind: 'grant', reqs: [] };
    if (!on()) { res.why = '官爵制度未开启'; return res; }
    if (!p) { res.why = '没有这个官职'; return res; }
    if (!x || x.dead) { res.why = '此人已故'; return res; }
    if (!opts.trusted) validate(x);
    const cur = valid(x) && x.faction === f ? x.office : null;
    if (cur && cur.post === postId) { res.why = '已任此职'; return res; }
    if (cur && p.tier < cur.tier) { res.why = '降职请用「贬官」'; res.kind = 'demote'; return res; }
    res.kind = !cur ? 'grant' : p.tier > cur.tier ? 'promote' : 'move';
    // 电脑的按功绩授官（meritPlan）已逐条检查过资格、名额与本月任免（opts.trusted）：不再拼检查表的文字
    if (!opts.trusted) {
      const reqs = requirements(x, postId, f, opts);
      res.reqs = reqs;
      const bad = reqs.find(r => !r.ok && r.key !== 'gold');
      if (bad) { res.why = bad.text; return res; }
    }
    res.cost = cur ? (p.tier > cur.tier ? giftOf(p.tier, cur.tier) : 0) : giftOf(p.tier, 0);
    if (opts.free) res.cost = 0;   // 开局的官职（CFG.ai.startCourt）：剧本开始时已在任，不收礼金
    if (res.cost > 0) {
      if (opts.ai) {
        const A = SG.AIBudget, cr = creditOf(f);
        // 钱袋不够时 payer 必然找不到城（INTEGRATION §5.1 canSpend）：先看钱袋，省去逐城计算保留额；礼金额度（CFG.ai）同样
        if (A && typeof A.purseOf === 'function' && A.purseOf(f) < res.cost) res.payFrom = null;
        else if (cr && cr.c < res.cost) res.payFrom = null;
        else res.payFrom = A ? aiPayer(A, f, res.cost, opts.resCache) : playerPayer(f, x, res.cost);
      } else res.payFrom = playerPayer(f, x, res.cost);
      if (!res.payFrom) { res.why = `礼金不足（需 ${res.cost} 金）`; return res; }
    }
    const best = x.officeBest | 0;
    res.loyalty = res.kind === 'move' ? 0 : Math.max(0, tierOf(p.tier).loyGrant - (best ? tierOf(best).loyGrant : 0));
    if (res.kind !== 'move') res.passedOver = passedOverOf(f, x, p, opts.pool);
    res.nameIdx = freeNameIdx(f, postId, x, nameIdx);
    res.name = postName(f, postId, res.nameIdx);
    res.ok = true;
    return res;
  }

  function grant(f, x, postId, nameIdx, opts) {
    index();
    opts = opts || {};
    const t0 = now();
    if (on() && x && !opts.trusted) { validate(x); enforce(f); }   // 复任先于新授（§6.6；电脑的 meritPlan 开头已整理过）
    const c = canGrant(f, x, postId, nameIdx, opts);
    const res = { ok: false, why: c.why, office: null, cost: c.cost, payFrom: c.payFrom ? c.payFrom.id : null, loyalty: c.loyalty, passedOver: c.passedOver, ceremony: null, kind: c.kind, line: '' };
    if (!c.ok) { if (!opts.ai) ST.ms += now() - t0; return res; }
    const g = G(), p = POST[postId], m = mi();
    // 礼金
    if (c.cost > 0) {
      if (opts.ai && SG.AIBudget) {
        SG.AIBudget.spend(c.payFrom, c.cost, 'title');
        const cr = creditOf(f); if (cr) cr.c -= c.cost;
      } else c.payFrom.gold -= c.cost;
      ST.gifts += c.cost; if (opts.ai) ST.aiGifts += c.cost;
      noteGift(f, c.cost);
    }
    // 忠诚（每品只给一次）
    const before = x.officeBest | 0;
    if (c.loyalty > 0) x.loyalty = Math.min(100, x.loyalty + c.loyalty);
    if (c.kind !== 'move') x.officeBest = Math.max(before, p.tier); else x.officeBest = Math.max(before, x.officeBest | 0);
    // 越级者不平
    for (const q of c.passedOver) { q.gen.loyalty = Math.max(0, q.gen.loyalty - CFG.passedOver.loss); ST.passedOver++; if (opts.ai) ST.aiPassedOver++; }
    const prev = valid(x) ? x.office : null;
    if (g.officeSeq === undefined) g.officeSeq = 0;
    const seq = c.kind === 'move' && prev ? prev.seq : ++g.officeSeq;
    x.office = { post: p.id, name: c.nameIdx, tier: p.tier, fam: p.family, since: m, seq, faction: f, month: m, suspended: false };
    lastFac.set(x.id, f);
    logOf(x, c.kind, p.id, c.nameIdx);
    dirty();
    ST.grants++; if (opts.ai) ST.aiGrants++;
    const nm = c.name;
    res.line = c.kind === 'grant' ? `拜${x.name}为${nm}。` : c.kind === 'promote' ? `迁${x.name}为${nm}。` : `改任${x.name}为${nm}。`;
    if (isPlayer(f) || (opts.ai && p.tier >= CFG.aiNewsTier && bordersPlayer(f))) g.addLog((isPlayer(f) ? '' : g.factions[f].name + '：') + res.line);
    res.ok = true; res.why = '';
    res.office = officeOf(x);
    res.ceremony = opts.ceremony || tierOf(p.tier).ceremony;
    res.gen = x.id;
    if (isPlayer(f)) EVENTS.push({ kind: 'grant', gen: x.id, post: p.id, tier: p.tier, ceremony: res.ceremony, line: res.line });
    emit('title.grant', x, res);
    if (!opts.ai) ST.ms += now() - t0;
    return res;
  }

  // 本年礼金（官制画面头上的「本年礼金 450 金」，TITLES §9.2）：玩家势力付过礼金后才有 faction.officeGift = { year, gold }
  // （跨年自动从 0 记起；电脑势力不记，关掉官爵的存档里没有这个字段）
  function noteGift(f, cost) {
    const g = G();
    if (!g || !isPlayer(f) || !(cost > 0) || !g.factions[f]) return;
    const fa = g.factions[f];
    if (!fa.officeGift || fa.officeGift.year !== g.year) fa.officeGift = { year: g.year, gold: 0 };
    fa.officeGift.gold += cost;
  }
  function giftsThisYear(f) {
    const g = G(), fa = g && f >= 0 ? g.factions[f] : null;
    return fa && fa.officeGift && fa.officeGift.year === g.year ? fa.officeGift.gold | 0 : 0;
  }

  function revokeCost(x) {
    if (!valid(x)) return 0;
    const L = tierOf(x.office.tier).revokeLoy;
    return x.office.suspended ? Math.ceil(L * CFG.suspendedRevokeK) : L;
  }
  function canRevoke(x) {
    if (!on()) return { ok: false, why: '官爵制度未开启' };
    if (!x || !valid(x)) return { ok: false, why: '没有官职' };
    if (changedThisMonth(x)) return { ok: false, why: '本月已有任免' };
    return { ok: true, why: '', loyalty: -revokeCost(x) };
  }
  function revoke(x, why) {
    if (x) validate(x);
    const c = canRevoke(x);
    if (!c.ok) return { ok: false, why: c.why, loyalty: 0 };
    const g = G(), o = x.office, f = o.faction;
    const loss = revokeCost(x);
    const nm = nameOf(x) || postName(f, o.post, o.name);
    x.loyalty = Math.max(0, x.loyalty - loss);
    logOf(x, 'revoke', o.post, o.name);
    delete x.office;
    lastFac.set(x.id, f);
    dirty();
    ST.revokes++;
    const line = `罢免${x.name}的${nm}。`;
    if (isPlayer(f)) g.addLog(line);
    enforce(f);
    const res = { ok: true, why: why || '', loyalty: -loss, line, after: x.loyalty };
    emit('title.revoke', x, res);
    return res;
  }
  function demoteCost(x, postId) {
    const p = POST[postId];
    if (!valid(x) || !p || p.tier >= x.office.tier) return 0;
    const k = x.office.tier - p.tier;
    return CFG.demote.base + CFG.demote.perTier * k;
  }
  function demote(x, postId) {
    index();
    const p = POST[postId];
    if (x) validate(x);
    if (!on() || !x || !valid(x) || !p) return { ok: false, why: !p ? '没有这个官职' : '没有官职', loyalty: 0 };
    const o = x.office, f = o.faction;
    if (p.tier >= o.tier) return { ok: false, why: '只能降到更低的品阶', loyalty: 0 };
    if (changedThisMonth(x)) return { ok: false, why: '本月已有任免', loyalty: 0 };
    if (!eligible(x, p)) return { ok: false, why: '不合此职的资格', loyalty: 0 };
    const s = seatFree(f, p, x);
    if (!s.ok) return { ok: false, why: s.why, loyalty: 0 };
    const loss = demoteCost(x, postId);
    x.loyalty = Math.max(0, x.loyalty - loss);
    const g = G(), m = mi();
    if (g.officeSeq === undefined) g.officeSeq = 0;
    const nameIdx = freeNameIdx(f, p.id, x);
    x.office = { post: p.id, name: nameIdx, tier: p.tier, fam: p.family, since: m, seq: ++g.officeSeq, faction: f, month: m, suspended: false };
    logOf(x, 'demote', p.id, nameIdx);
    dirty();
    ST.demotes++;
    const line = `贬${x.name}为${postName(f, p.id, nameIdx)}。`;
    if (isPlayer(f)) g.addLog(line);
    enforce(f);
    return { ok: true, why: '', loyalty: -loss, line, after: x.loyalty };
  }
  // 只换名号（同一官位的另一个空闲名称）：不算任免
  function setName(x, idx) {
    if (!valid(x)) return false;
    const list = names(x.office.faction, x.office.post);
    if (!(idx >= 0 && idx < list.length)) return false;
    for (const h of holders(x.office.faction)) if (h !== x && valid(h) && h.office.post === x.office.post && (h.office.name | 0) === idx) return false;
    x.office.name = idx;
    return true;
  }

  // ---- 许官（§6.7）：此刻能授给他的最高官职（合格、有空缺、礼金付得起；尚未入仕者按本势力计）----
  function promiseBonus(f, x) {
    index();
    if (!on() || !x || x.dead || f < 0) return null;
    if (isRuler(x)) return null;
    const g = G();
    for (let t = 5; t >= 1; t--) {
      for (const fam of families(x)) {
        for (const p of BY_TIER[t]) {
          if (p.family !== fam || !eligible(x, p, { asFaction: true })) continue;
          if (!seatFree(f, p, null).ok) continue;
          const cost = giftOf(t, 0);
          let pay = null;
          for (const c of g.cities) if (c.owner === f && c.gold >= cost && (!pay || c.gold > pay.gold)) pay = c;
          if (!pay) continue;
          return { postId: p.id, tier: t, add: CFG.promisePerTier * t, cost, payFrom: pay.id, name: postName(f, p.id, freeNameIdx(f, p.id, null)) };
        }
      }
    }
    return null;
  }
  function hireWithPromise(target, recruiter, f, cityId, opts) {
    opts = opts || {};
    const pr = opts.promise === false ? null : promiseBonus(f, target);
    const C = SG.Commands;
    const hired = C.hire(target, recruiter, f, cityId, { promise: pr ? pr.add : 0, via: opts.via });
    let office = null;
    if (hired && pr) {
      const r = grant(f, target, pr.postId, undefined, { ceremony: 'small', hired: true });
      if (r.ok) office = r;
    }
    return { ok: !!hired, hired: !!hired, office, promise: pr };
  }

  // ---- 候选名单（§9.3）：本势力该家族的全部武将，合格者在前（功绩排序），不合格的也列出（看得见阶梯）----
  function candidates(f, postId) {
    index();
    const p = POST[postId], g = G();
    if (!p || !g) return [];
    enforce(f);   // 复任先于新授（§6.6）：名单与 grant() 看到同样的名额
    const out = [];
    for (const x of g.generalsOf(f)) {
      if (isRuler(x)) continue;
      const c = canGrant(f, x, postId);
      const fam = families(x).indexOf(p.family) >= 0;
      let why = c.ok ? '可任' : c.why;
      if (!fam) why = `最高属性为${families(x).map(q => STAT_ZH[q]).join(' / ')}`;
      out.push({ gen: x, ok: c.ok, why, merit: merit(x, p.family), stat: base(x, p.family), level: lvl(x), cur: valid(x) ? nameOf(x) : '', famOk: fam, cost: c.cost, check: c });
    }
    out.sort((a, b) => (b.ok - a.ok) || (b.famOk - a.famOk) || b.merit - a.merit || a.gen.id - b.gen.id);
    return out;
  }

  // ================================================================ 官制总览（§9.2）==
  function board(f) {
    index();
    const out = [];
    if (!D()) return out;
    enforce(f);   // 复任先于新授（§6.6）：不把虚衔者要收回的席位画成空缺
    const cap = caps(f), hs = holders(f).filter(valid), g = G();
    const cand = new Map();   // postId → 是否有合格人选
    const anyCand = p => {
      if (cand.has(p.id)) return cand.get(p.id);
      let v = false;
      for (const x of g.generalsOf(f)) if (curTier(x) < p.tier && eligible(x, p) && !changedThisMonth(x)) { v = true; break; }
      cand.set(p.id, v); return v;
    };
    for (let t = 5; t >= 1; t--) {
      const tr = tierOf(t);
      const used = hs.filter(x => x.office.tier === t && !x.office.suspended).length;
      const row = { tier: t, grade: tr.grade, used, cap: cap[t - 1], req: `${tr.reqLevel} 级` + (tr.reqStat ? ` · 属性 > ${tr.reqStat}` : ''), locked: cap[t - 1] <= 0, lockWhy: cap[t - 1] <= 0 ? '需位阶 ' + firstRankName(f, t) : '', posts: [] };
      for (const fam of FAMS) for (const p of BY_TIER[t].filter(q => q.family === fam)) {
        const mine = hs.filter(x => x.office.post === p.id).sort((a, b) => a.office.seq - b.office.seq);
        const seats = mine.map(x => ({ gen: x, name: nameOf(x) || postName(f, p.id, x.office.name), state: x.office.suspended ? 'suspended' : isCaptive(x) ? 'captive' : 'filled', why: x.office.suspended ? '虚衔' : isCaptive(x) ? '被俘中' : '' }));
        const nSeats = p.seats ? p.seats : Math.max(1, mine.length + 1);
        const activeHere = mine.filter(x => !x.office.suspended).length;
        while (seats.length < nSeats) {
          let state = 'free', why = '';
          if (row.locked) { state = 'locked'; why = row.lockWhy; }
          else if (used >= cap[t - 1]) { state = 'full'; why = '名额已满'; }
          else if (p.seats && activeHere >= p.seats) { state = 'full'; why = '此职名额已满'; }
          seats.push({ gen: null, name: '', state, why, cand: state === 'free' && anyCand(p) });
        }
        row.posts.push({ post: p, family: p.family, name: postName(f, p.id, 0), seats });
      }
      out.push(row);
    }
    return out;
  }
  // 「官制」按钮的角标：有合格人选的空缺数（各品阶 min(空缺, 合格人数)）
  function pending(f) {
    index();
    const g = G();
    if (!on() || !g || f < 0) return 0;
    enforce(f);
    const cap = caps(f);
    let n = 0;
    const gens = g.generalsOf(f).filter(x => !isRuler(x) && !isCaptive(x) && !changedThisMonth(x));
    for (let t = 1; t <= 5; t++) {
      const free = cap[t - 1] - tierActive(f, t, null);
      if (free <= 0) continue;
      let k = 0;
      for (const x of gens) {
        if (curTier(x) >= t) continue;
        if (BY_TIER[t].some(p => eligible(x, p) && (!p.seats || postActive(f, p.id, x) < p.seats))) k++;
        if (k >= free) break;
      }
      n += Math.min(free, k);
    }
    return n;
  }
  // 月初的提示（§9.1）：玩家武将新近够格出任更高一品、且该品有空缺
  const hintTier = new Map();
  let lastHints = [];   // 本月的提示（结构化：{ gen, tier, postId, text }；新闻里只有文字，界面可让它点开官制画面的那个席位）
  function hints() {
    const g = G(), f = g ? g.player : -1, out = [];
    lastHints = [];
    if (!on() || f < 0 || !g.factions[f] || !g.factions[f].alive) return out;
    const cap = caps(f);
    for (const x of g.generalsOf(f)) {
      if (isRuler(x)) continue;
      let best = 0;
      for (let t = 5; t >= 1 && !best; t--) if (BY_TIER[t].some(p => eligible(x, p))) best = t;
      const prev = hintTier.get(x.id);
      hintTier.set(x.id, best);
      if (prev === undefined || best <= prev || best <= curTier(x)) continue;
      if (cap[best - 1] - tierActive(f, best, null) <= 0) continue;
      if (out.length < CFG.hintsMax) {
        const text = `${x.name}已可出任${GRADE(best)}官。`;
        const p = BY_TIER[best].find(q => eligible(x, q) && (!q.seats || postActive(f, q.id, x) < q.seats)) || null;
        out.push(text);
        lastHints.push({ gen: x.id, tier: best, postId: p ? p.id : null, text });
      }
    }
    return out;
  }
  function primeHints() { const g = G(); hintTier.clear(); if (!g || g.player < 0) return; for (const x of g.generalsOf(g.player)) { let best = 0; for (let t = 5; t >= 1 && !best; t--) if (BY_TIER[t].some(p => eligible(x, p))) best = t; hintTier.set(x.id, best); } }

  // ================================================================ 月初（§6.5、§6.6；第 6 相）==
  function monthStart() {
    const t0 = now();
    const g = G();
    if (!on() || !g) return [];
    validateAll();
    // 各势力的国势一次算完（O(城)），再逐个 refresh
    const pw = D().rulerRanks.power, Pf = new Float64Array(g.factions.length);
    for (const c of g.cities) if (c.owner >= 0) Pf[c.owner] += pw.perCity + (c.land + c.industry + c.town) / pw.devDiv;
    for (const f of g.factions) if (f.alive) refresh(f.id, Pf[f.id]);
    buildGroups(g);
    const out = takeNews().concat(hints());
    ST.monthMs += now() - t0;
    return out;
  }
  function takeNews() { return NEWS.splice(0, NEWS.length); }
  // 第 6 相结束时按势力分好的武将（第 7 相的电脑授官用；本月之内若有登用、拉拢、攻城则作废，回到逐个扫描）
  let groups = null;
  function buildGroups(g) {
    const map = new Map();
    for (const x of g.generals) if (x.faction >= 0 && !x.dead) { let l = map.get(x.faction); if (!l) map.set(x.faction, l = []); l.push(x); }
    groups = { g, mi: mi(), map };
  }
  function takeEvents() { return EVENTS.splice(0, EVENTS.length); }

  // ================================================================ 电脑授官（§8；第 7 相，礼金经 SG.AIBudget）==
  function prefBranch(x, fam) {
    const B = CFG.branch;
    if (fam === 'war') return x.war >= B.warB || x.war - x.intel >= B.warGap ? 'B' : 'A';
    if (fam === 'intel') return x.pol >= x.intel - B.intelPolGap ? 'B' : 'A';
    return x.war > x.intel ? 'B' : 'A';
  }
  function pickPost(f, x, t) {
    const tr = tierOf(t);
    if (lvl(x) < tr.reqLevel) return null;
    for (const fam of FAMS) {
      if (!inFamily(x, fam) || !(base(x, fam) > tr.reqStat)) continue;
      const pb = prefBranch(x, fam);
      let first = null;
      for (const p of BY_TIER[t]) {
        if (p.family !== fam) continue;
        if (p.seats && postActive(f, p.id, x) >= p.seats) continue;
        if (p.branch === pb) return p;
        if (!first) first = p;
      }
      if (first) return first;
    }
    return null;
  }
  // 按功绩授官（电脑与玩家的「按功绩授官」共用）：从一品往下，三家轮流（本品以上在任最少的一家先），再按功绩
  function meritPlan(f, payMode, skipRefresh) {
    const g = G();
    const done = [];
    if (!skipRefresh) refresh(f);
    const cap = caps(f);
    const A = SG.AIBudget;
    // 电脑：钱袋（或礼金额度，CFG.ai）连最便宜的礼金（50）都付不起时一个也授不了（INTEGRATION §5.1），不必逐品计算
    if (payMode === 'ai' && A && typeof A.purseOf === 'function') {
      let minGift = Infinity;
      for (let t = 1; t <= 5; t++) if (cap[t - 1] > 0) minGift = Math.min(minGift, t === 1 ? tierOf(1).grantGold : tierOf(t).grantGold - tierOf(t - 1).grantGold);
      const cr = creditOf(f);
      if (!(A.purseOf(f) >= minGift) || (cr && !(cr.c >= minGift))) { let free = false; for (let t = 1; t <= 5 && !free; t++) if (cap[t - 1] > tierActive(f, t, null)) free = true; if (free) ST.aiRefused++; return done; }
    }
    // 候选人：非君主、未被俘；每人的现任品阶、本月是否已任免、功绩、够格的最高品阶（不论名额）只算一次
    const resCache = new Map();
    const cands = [];
    const pool = [];
    const grp = payMode !== 'player' && groups && groups.g === g && groups.mi === mi() ? groups.map.get(f) || EMPTY : null;
    for (const x of grp || g.generals) {
      if (x.faction !== f || x.dead) continue;
      pool.push(x);
      if (isRuler(x) || isCaptive(x)) continue;
      let top = 0;
      for (let t = 5; t >= 1 && !top; t--) { const tr = tierOf(t); if (lvl(x) >= tr.reqLevel && Math.max(x.war, x.intel, x.pol) > tr.reqStat) top = t; }
      cands.push({ x, cur: curTier(x), ch: changedThisMonth(x), m: merit(x), top });
    }
    for (let t = 5; t >= 1; t--) {
      if (!(cap[t - 1] > 0)) continue;
      for (let guard = 0; guard < CFG.aiGuard; guard++) {
        if (tierActive(f, t, null) >= cap[t - 1]) break;
        const famCount = { war: 0, intel: 0, pol: 0 };
        for (const h of holders(f)) if (h.office && !h.office.suspended && h.office.tier >= t && valid(h)) famCount[h.office.fam]++;
        let best = null, bm = -1, bp = null, bk = Infinity;
        for (const c of cands) {
          if (c.top < t || c.cur >= t || c.ch) continue;
          const p = pickPost(f, c.x, t);
          if (!p) continue;
          const k = famCount[p.family];
          if (k < bk || (k === bk && (c.m > bm || (c.m === bm && c.x.id < best.x.id)))) { bk = k; bm = c.m; best = c; bp = p; }
        }
        if (!best) break;
        const r = grant(f, best.x, bp.id, undefined, { ai: payMode === 'ai' || payMode === 'start', free: payMode === 'start', trusted: true, pool, resCache });
        if (!r.ok) { if (payMode === 'ai') ST.aiRefused++; break; }   // 礼金付不起（钱袋、礼金额度或城的保留）→ 这一品停手（INTEGRATION §5）
        best.cur = t; best.ch = true;
        done.push(r);
      }
    }
    return done;
  }
  function aiManage(f) {
    const g = G();
    if (!on() || !g || f < 0 || !g.factions[f] || !g.factions[f].alive) return [];
    const t0 = now();
    const r = meritPlan(f, 'ai', refreshedAt.get(f) === mi());   // 第 6 相 monthStart 刚整理过位阶与虚衔：不必再算
    ST.aiMs += now() - t0; ST.aiCalls++;
    return r;
  }
  function aiManageAll() { const g = G(); if (!on() || !g) return; validateAll(); for (const f of g.factions) if (f.alive && f.id !== g.player) aiManage(f.id); }
  // 新局的开局官职（CFG.ai.startCourt，§8 newGame）：同一套按功绩授官，不收礼金（也不动钱袋与礼金额度）；授官忠诚照给、越级者照算
  function startCourt() {
    const g = G();
    if (!on() || !g) return [];
    validateAll();
    const out = [];
    for (const f of g.factions) if (f.alive && f.id !== g.player) for (const r of meritPlan(f.id, 'start')) out.push(r);
    return out;
  }
  // 玩家的「按功绩授官」（§8 委任）：apply = false 时只给出方案（在影子状态上算，算完原样还原）
  function fillByMerit(f, apply) {
    const g = G();
    if (!on() || !g) return [];
    let snap = null;
    if (!apply) {
      snap = {
        gens: g.generals.map(x => [x.office ? Object.assign({}, x.office) : undefined, x.officeBest, x.officeLog ? x.officeLog.map(e => e.slice()) : undefined, x.loyalty, lastFac.get(x.id)]),
        gold: g.cities.map(c => c.gold), seq: g.officeSeq, ranks: g.factions.map(q => q.rank), gifts: g.factions.map(q => (q.officeGift ? Object.assign({}, q.officeGift) : undefined)),
        log: g.log.length, st: Object.assign({}, ST), news: NEWS.length, ev: EVENTS.length,
      };
    }
    const done = meritPlan(f, 'player');
    const plan = done.map(r => ({ gen: g.generals[r.gen], post: r.office ? r.office.post : null, name: r.office ? r.office.name : '', kind: r.kind, cost: r.cost, payFrom: r.payFrom, loyalty: r.loyalty, passedOver: r.passedOver.map(q => ({ gen: q.gen, loss: q.loss })) }));
    if (snap) {
      g.generals.forEach((x, i) => {
        const s = snap.gens[i];
        if (s[0]) x.office = s[0]; else delete x.office;
        if (s[1] === undefined) delete x.officeBest; else x.officeBest = s[1];
        if (s[2] === undefined) delete x.officeLog; else x.officeLog = s[2];
        x.loyalty = s[3];
        if (s[4] === undefined) lastFac.delete(x.id); else lastFac.set(x.id, s[4]);
      });
      g.cities.forEach((c, i) => { c.gold = snap.gold[i]; });
      if (snap.seq === undefined) delete g.officeSeq; else g.officeSeq = snap.seq;
      g.factions.forEach((q, i) => { if (snap.ranks[i] === undefined) delete q.rank; else q.rank = snap.ranks[i]; if (snap.gifts[i] === undefined) delete q.officeGift; else q.officeGift = snap.gifts[i]; });
      g.log.length = Math.min(g.log.length, snap.log);
      Object.assign(ST, snap.st);
      NEWS.length = snap.news; EVENTS.length = snap.ev;
      dirty();
    }
    return plan;
  }

  // ================================================================ 查询（界面）==
  function officeOf(x) {
    if (!x || !valid(x)) return null;
    const o = x.office, p = POST[o.post], n = nameRec(x) || {};
    return {
      post: o.post, tier: o.tier, fam: o.fam, branch: p.branch, role: p.role, grade: GRADE(o.tier), name: n.zh || o.post, orig: n.orig || '', attest: n.attest || 'C',
      note: n.note || '', rank: n.rank || '', seal: n.seal || insignia(o.faction, o.tier), src: n.src || [], nameIdx: o.name | 0, since: o.since, seq: o.seq,
      suspended: !!o.suspended, captive: isCaptive(x), active: active(x), faction: o.faction, familyZh: D().families[o.fam].zh, color: D().families[o.fam].color,
    };
  }
  const KIND_ZH = { grant: '拜', promote: '迁', move: '改任', revoke: '罢免', demote: '贬为', suspend: '虚衔', restore: '复任', lost: '去职' };
  function history(x) {
    if (!x || !x.officeLog) return [];
    return x.officeLog.map(e => {
      const y = Math.floor((e[0] - 1) / 12), mo = e[0] - y * 12;
      const f = x.faction >= 0 ? x.faction : (x.office ? x.office.faction : -1);
      const nm = f >= 0 ? postName(f, e[1], e[2]) : (POST[e[1]] ? POST[e[1]].role : e[1]);
      return `${y}年${mo}月 ${KIND_ZH[e[3]] || e[3]}${e[3] === 'revoke' || e[3] === 'lost' || e[3] === 'suspend' || e[3] === 'restore' ? ' ' : ''}${nm}`;
    });
  }

  // ================================================================ 存档与迁移（INTEGRATION §6.2：TITLES 第二）==
  function rebuild(g) {
    lastFac.clear(); refreshedAt.clear(); groups = null; dirty();
    for (const x of g.generals) lastFac.set(x.id, x.faction);
    const prev = SG.G; SG.G = g;
    try { primeHints(); } finally { SG.G = prev; }
  }
  function setupState(g, newGame) {
    if (g.officeSeq === undefined) g.officeSeq = 0;
    const prev = SG.G; SG.G = g;
    try {
      for (const f of g.factions) if (!(f.rank >= 1 && f.rank <= 5)) f.rank = upRank(power(f.id));
      rebuild(g);
      // §8：新局为每个电脑势力跑一次。钱袋此时为空，照付礼金一个也授不了：CFG.ai.startCourt（缺省开）时按开局官职授（不收礼金）
      if (newGame) { if (CFG.ai && CFG.ai.startCourt) startCourt(); else aiManageAll(); }
    } finally { SG.G = prev; }
  }
  function init(g) { index(); installHooks(); setupState(g, true); }
  function migrate(g) { index(); installHooks(); setupState(g, false); }   // v2 / v3 存档（以及没有官爵状态的 v4 存档）：算位阶，不授官
  function load(g) {
    index();
    installHooks();
    if (g.officeSeq === undefined) g.officeSeq = 0;
    for (const x of g.generals) {
      if (!x.office) continue;
      const o = x.office;
      if (!POST[o.post]) { delete x.office; continue; }
      const p = POST[o.post];
      o.tier = p.tier; o.fam = p.family;
      const n = (D() && x.faction >= 0) ? (() => { const fa = g.factions[o.faction]; return fa ? nameSet(fa.key, fa.culture, o.post).length : 1; })() : 1;
      o.name = Math.max(0, Math.min(n - 1, o.name | 0));
      if (typeof o.seq !== 'number') o.seq = ++g.officeSeq;
      if (o.seq > g.officeSeq) g.officeSeq = o.seq;
    }
    for (const f of g.factions) if (!(f.rank >= 1 && f.rank <= 5)) { const prev = SG.G; SG.G = g; try { f.rank = upRank(power(f.id)); } finally { SG.G = prev; } }
    rebuild(g);
  }

  // ================================================================ 提供者（INTEGRATION §3.3）==
  const PROVIDERS = {
    titleAtk: x => bonus(x, 'atk'),
    titleDef: x => bonus(x, 'def'),
    cmdAtk: (model, side) => sideBonus(model, side, 'cmdAtk'),
    cmdDef: (model, side) => sideBonus(model, side, 'cmdDef'),
    titleMorale: x => bonus(x, 'morale'),
    titleCrit: x => bonus(x, 'critArmy'),
    titleCritDuel: x => bonus(x, 'critDuel'),
    titleDmgDuel: x => bonus(x, 'dmgDuel'),
    titleDev: x => bonus(x, 'dev'),
    titleCity: (a, key) => bonus(a, key),
    titleNat: (f, key) => natBonus(f, key),
    titleTrain: x => bonus(x, 'train'),
    titleSearch: x => bonus(x, 'search'),
    titleHire: x => bonus(x, 'hire'),
    titleDip: x => bonus(x, 'dip'),
    titleDiscord: x => bonus(x, 'discord'),
    titlePersuade: x => bonus(x, 'persuade'),
    persuadeRes: x => persuadeRes(x),
    discordRes: x => discordRes(x),
    titleShield: x => effLoy(x),
    titleLoyAura: c => cityBonus(c, 'loyAura'),
    admin: c => cityAdmin(c),
    cityDef: c => garrison(c),
    popGrowth: c => cityBonus(c, 'pop'),
    titleTac: x => bonus(x, 'tac'),
    titleTacHit: x => bonus(x, 'tacHit'),
    tacRes: (model, side) => sideRes(model, side),
  };
  let providersOff = null;
  function installProviders() {
    if (providersOff || !SG.Mods || typeof SG.Mods.provide !== 'function') return;
    providersOff = SG.Mods.provide(PROVIDERS, { owner: 'titles', when: on });
  }
  installProviders();

  // ---- 月末钩子（strategy-ai.js 在本文件之后载入：新局 / 读档时由 init / migrate / load 装上，只装一次）----
  let hooksInstalled = false;
  function installHooks() {
    if (hooksInstalled) return true;
    const A = SG.StrategyAI;
    if (!A || typeof A.on !== 'function') return false;
    hooksInstalled = true;
    A.on('defections', () => validateAll(), { owner: 'titles', when: on, order: 50 });
    A.on('titles', ctx => { const lines = monthStart(); if (ctx && ctx.news) for (const l of lines) ctx.news.push(l); }, { owner: 'titles', when: on, order: 50 });
    A.onSpend('title', f => aiManage(f), { owner: 'titles', when: on });
    return true;
  }
  let eventsInstalled = false;
  function installEvents() {
    const E = SG.Events;
    if (eventsInstalled || !E) return;
    eventsInstalled = true;
    E.on('command', (cmd, res) => {
      if (!on() || !res) return;
      const g = G(); if (!g) return;
      if (cmd === 'hire' || cmd === 'persuade') groups = null;
      if ((cmd === 'hire' || cmd === 'persuade') && res.target != null && g.generals[res.target]) {
        validate(g.generals[res.target]);
        // 拉拢献城：两边的国势都变了（原势力已不可知，全部刷新一遍，O(势力 × 城)）
        if (cmd === 'persuade' && res.ok && res.surrendered) for (const f of g.factions) if (f.alive) refresh(f.id);
      }
      if (cmd === 'develop' && res.ok !== false && res.f != null && res.f >= 0 && res.f === g.player) refresh(res.f);
    });
    E.on('conquest.apply', (s, info) => {
      if (!on() || !s) return;
      groups = null;
      for (const l of [s.atk, s.def, s.captives]) if (l) for (const x of l) validate(x);
      if (info && info.attacker >= 0) refresh(info.attacker);
      if (info && info.defender >= 0) refresh(info.defender);
    });
    E.on('captive.released', x => { if (on() && x) validate(x); });
    E.on('captive.executed', x => { if (on() && x) validate(x); });
    E.on('general.slain', x => { if (on() && x) validate(x); });
    E.on('general.rehomed', list => { if (on() && list) for (const x of list) validate(x); });
    E.on('faction.succession', (f, heir) => { if (on() && heir) validate(heir); });
    E.on('faction.death', () => { if (on()) { groups = null; validateAll(); } });
  }
  installEvents();

  Object.assign(Titles, {
    CFG, FAMS, STAT_ZH, saveKeys: ['officeSeq'],
    get data() { return D(); },
    post: id => (index(), POST[id] || null), tier: t => (index(), tierOf(t) || null), posts: () => (index(), DATA ? DATA.posts.slice() : []),
    families, family, base, merit, postsFor, eligible, requirements, changedThisMonth,
    power, rank, rankInfo, caps, upRank, stepRank,
    board, pending, candidates,
    officeOf, nameOf, names, postName, rulerRankName, insignia, cultureOf, effects, effectText, history,
    bonus, sideBonus, sideRes, cityAdmin, cityBonus, natBonus, garrison, effLoy, persuadeRes, discordRes, holders: f => holders(f).filter(valid), curTier,
    isActive: x => active(x), isValid: x => valid(x),
    canGrant, grant, revoke, canRevoke, revokeCost, demote, demoteCost, setName, promiseBonus, hireWithPromise,
    validate, validateAll, refresh, enforce, monthStart, hints, takeNews, takeEvents, hookLive, giftsThisYear,
    hintRecords: () => lastHints.slice(),
    seatHint: (f, t) => nextSeatHint(f, t).replace(/^——/, ''),   // 「国势达到 10.0 后增加」（名额已满或未开时；没有更高的位阶时为空）
    aiManage, aiManageAll, startCourt, fillByMerit, prefBranch, pickPost, aiCredit: f => { const r = creditOf(f); return r ? r.c : null; }, creditRate,
    init, migrate, load,
    installHooks, installProviders, providers: PROVIDERS, stats: ST,
    resetStats() { for (const k of Object.keys(ST)) ST[k] = 0; },
    // tests：清掉非存档的缓存（另起一局之前）
    reset() { lastFac.clear(); hintTier.clear(); refreshedAt.clear(); groups = null; NEWS.length = 0; EVENTS.length = 0; lastHints = []; dirty(); },
  });
  // Object.assign 会把 getter 求值成定值：本月提示的记录用真正的 getter（SG.Titles.lastHints）
  Object.defineProperty(Titles, 'lastHints', { get: () => lastHints.slice(), enumerable: true });
  SG.Titles = Titles;
  installHooks();   // strategy-ai.js 在本文件之后载入：这里通常装不上，init / migrate / load（新局与读档）时再装
})();
