'use strict';
/* ==========================================================================
   CITY-VIEW.md reference prototype: building stages, city level, triangle budgets.
   Pure logic (no THREE / DOM). The implementation ports STAGES / stageOf / cityLevel to
   js/city-layout.js unchanged (same thresholds, same hysteresis) so tests can compare.

   node design-v4/domestic/city-stages.js            → stage histograms at game start (classic + world),
                                                      develops needed per stage, triangle estimates and budget checks
   node design-v4/domestic/city-stages.js --json     → the same as JSON (used by the doc tables)

   Exports (module.exports when required from Node):
     FAMILY, FAMILIES, BUILDINGS, HUBS, stageOf(id, ctx, prevStage?), stages(ctx, prev?), cityLevel(st),
     ctxFor(g, city, extra?), triEstimate(st, ctx, tierName)
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');

// ------------------------------------------------------------------ families --
// 18 culture codes → 12 city families (skins inside a family share layout templates and building recipes)
const FAMILY = {
  han: 'hua', nanman: 'hua',            // 华夏（北方 / 江南 / 南中 三种皮肤）
  korea: 'han3',                         // 三韩·高句丽
  wa: 'wa',                              // 倭
  steppe: 'steppe', sarmatian: 'steppe', // 草原（毡帐 / 篷车）
  tarim: 'oasis',                        // 西域绿洲
  kushan: 'indic', india: 'indic',       // 天竺（犍陀罗 / 南亚）
  persia: 'persia',                      // 安息
  arab: 'arabia', moor: 'arabia',        // 阿拉伯（帕尔米拉、萨巴 / 毛里）
  africa: 'nile',                        // 库施 / 阿克苏姆
  roman: 'roman',                        // 罗马
  celt: 'north', german: 'north',        // 北方蛮族（凯尔特 / 日耳曼）
  seasia: 'nanhai', yi: 'nanhai',        // 南海（林邑扶南 / 夷洲）
};
const FAMILIES = ['hua', 'han3', 'wa', 'steppe', 'oasis', 'indic', 'persia', 'arabia', 'nile', 'roman', 'north', 'nanhai'];

// ITEMS.md §6.2 trade hubs (shop tier floor) — by display name
const HUBS = {
  4: ['洛阳', '长安', '罗马', '亚历山大', '安条克', '泰西封', '富楼沙', '巴里加扎', '穆济里斯', '帕尔米拉', '查拉克斯', '蓝氏城', '成都'],
  3: ['番禺', '交趾', '疏勒', '于阗', '撒马尔罕', '塔克西拉', '阿杜利斯', '喔㕭', '马里卜', '襄阳', '大宛', '潘提卡彭', '麦罗埃', '埃德萨', '木鹿'],
  2: ['敦煌', '武威', '廷吉斯', '约尔', '金官', '邪马台', '伊都', '喀里多尼亚', '塔拉', '伊斯塔克尔', '哈特拉'],
};
function hubTier(name) { for (const t of [4, 3, 2]) if (HUBS[t].includes(name)) return t; return 0; }

// ------------------------------------------------------------------ stages --
// Each building: driver(ctx) → value; thresholds t[0..3] → stage 0..4 (shown as 1..5).
// Hysteresis: a stage only drops when the value falls below DOWN × threshold (no flicker when
// recruiting lowers population or a battle lowers troops).
const DOWN = 0.95;
const count = (v, t) => { let n = 0; for (const x of t) if (v >= x) n++; return n; };
const shopTier = ctx => Math.max(1 + count(ctx.c.industry + ctx.c.town, [520, 800, 1100]), hubTier(ctx.c.name));

const BUILDINGS = [
  // id, zh, size class, stat label, driver, thresholds, tri budget per stage (before ink-outline shells), command
  { id: 'walls', zh: '城墙·城门', cls: 'big', stat: '防御 = 20 + 町/12', drv: c => c.def, t: [45, 55, 65, 80], tris: [1800, 2400, 2800, 3000, 3000], cmd: null },
  { id: 'palace', zh: '官署 / 宫城', cls: 'big', stat: '人口（君主驻城 +1 阶）', drv: c => c.c.population, t: [250000, 400000, 600000, 850000], bonus: c => (c.rulerHere ? 1 : 0), tris: [1200, 1700, 2200, 2600, 3000], cmd: 'appoint' },
  { id: 'farms', zh: '农田', cls: 'field', stat: '土地', drv: c => c.c.land, t: [300, 450, 600, 800], tris: [1400, 2000, 2600, 3000, 3000], cmd: 'develop.land' },
  { id: 'granary', zh: '仓廪', cls: 'big', stat: '容量 = 农田阶；存粮 = 粮 /（1.25 次秋收）', drv: c => c.c.land, t: [300, 450, 600, 800], tris: [500, 800, 1200, 1600, 2000], cmd: 'trade' },
  { id: 'market', zh: '市', cls: 'big', stat: '商店等级（产业 + 町、商路枢纽）', drv: c => shopTier(c) * 1000 + c.c.industry + c.c.town, t: [2000, 3000, 4000, 5500], tris: [700, 1300, 1900, 2500, 3000], cmd: 'market' },
  { id: 'shop', zh: '兵器铺', cls: 'big', stat: '商店等级；第 5 阶 = 店中有史诗 / 两件稀有', drv: c => shopTier(c) + (c.shopRare ? 1 : 0), t: [2, 3, 4, 5], tris: [400, 700, 1000, 1400, 1800], cmd: 'market' },
  { id: 'workshop', zh: '工坊·冶铁', cls: 'big', stat: '产业', drv: c => c.c.industry, t: [300, 450, 600, 800], tris: [600, 1100, 1600, 2200, 2800], cmd: 'develop.industry' },
  { id: 'houses', zh: '民居（闾里）', cls: 'inst', stat: '町（密度、层数）；人口（人群）', drv: c => c.c.town, t: [300, 450, 600, 800], tris: [400, 400, 400, 400, 400], cmd: 'develop.town' },
  { id: 'barracks', zh: '兵营', cls: 'big', stat: '城中兵力', drv: c => c.troops, t: [4000, 10000, 20000, 40000], tris: [600, 1100, 1600, 2200, 2800], cmd: 'recruit' },
  { id: 'drill', zh: '校场', cls: 'field', stat: '兵力加权训练度', drv: c => c.training, t: [40, 60, 80, 95], tris: [300, 500, 800, 1100, 1400], cmd: 'train' },
  { id: 'stables', zh: '马厩', cls: 'big', stat: '武力 ≥ 75 的武将数 + 已装备坐骑数（草原 +2）', drv: c => c.cav, t: [1, 3, 5, 8], tris: [500, 900, 1300, 1700, 2200], cmd: 'recruit' },
  { id: 'academy', zh: '学馆', cls: 'big', stat: 'Σ max(0, 智力 − 60) / 10（城中武将）', drv: c => c.wit, t: [1, 2.5, 5, 9], tris: [500, 900, 1400, 1900, 2400], cmd: 'search' },
  { id: 'temple', zh: '祠庙', cls: 'big', stat: '德 / 25 + 人口 / 25 万（都城 +1）', drv: c => c.faith, t: [2, 3, 4.5, 6], tris: [600, 1000, 1500, 2100, 2800], cmd: 'reward' },
  { id: 'tavern', zh: '酒肆', cls: 'small', stat: '町（门前灯笼 = 城中有已发现的在野人才）', drv: c => c.c.town, t: [250, 400, 550, 750], tris: [250, 300, 340, 380, 400], cmd: 'search' },
  { id: 'treasury', zh: '府库', cls: 'big', stat: '金', drv: c => c.c.gold, t: [500, 1500, 4000, 10000], tris: [400, 600, 900, 1200, 1600], cmd: 'reward' },
  { id: 'harbour', zh: '港 / 津渡', cls: 'big', stat: '产业 + 町（枢纽 +1 阶）；只在临海 / 临河城', drv: c => c.c.industry + c.c.town, t: [500, 750, 1000, 1400], bonus: c => (hubTier(c.c.name) >= 3 ? 1 : 0), only: c => c.coastal || c.river, tris: [700, 1200, 1800, 2400, 3000], cmd: 'transport' },
];
const BY_ID = Object.fromEntries(BUILDINGS.map(b => [b.id, b]));

function stageOf(id, ctx, prev) {
  const b = BY_ID[id];
  if (!b) throw new Error('unknown building ' + id);
  if (b.only && !b.only(ctx)) return -1;
  const v = b.drv(ctx);
  let s = count(v, b.t);
  // hysteresis: keep the previous stage while v ≥ DOWN × its threshold
  if (prev !== undefined && prev > s) { const keep = prev > 0 && v >= DOWN * b.t[prev - 1]; if (keep) s = prev; }
  if (b.bonus) s += b.bonus(ctx);
  return Math.min(4, s);
}
function stages(ctx, prev) {
  const o = {};
  for (const b of BUILDINGS) o[b.id] = stageOf(b.id, ctx, prev ? prev[b.id] : undefined);
  return o;
}
// City level 1..5 (header badge): mean of the five "core" buildings, rounded half up
const CORE = ['farms', 'workshop', 'houses', 'walls', 'market'];
const LEVEL_NAMES = ['邑', '县城', '郡城', '大郡', '都会'];
function cityLevel(st) { let s = 0; for (const k of CORE) s += st[k]; return Math.min(4, Math.floor(s / CORE.length + 0.5)); }

// ------------------------------------------------------------------ context --
function ctxFor(g, c, extra) {
  extra = extra || {};
  const offs = g.officersIn(c);
  const troops = offs.reduce((a, x) => a + x.troops, 0);
  const training = troops > 0 ? offs.reduce((a, x) => a + x.troops * x.training, 0) / troops : 0;
  const culture = c.culture || 'han';
  const f = c.owner >= 0 ? g.factions[c.owner] : null;
  const rulerHere = !!(f && offs.some(x => g.isRuler(x)));
  const cav = offs.filter(x => x.war >= 75).length + (extra.mounts || 0) + (FAMILY[culture] === 'steppe' ? 2 : 0);
  const wit = offs.reduce((a, x) => a + Math.max(0, x.intel - 60) / 10, 0);
  const capital = !!extra.capital;
  const faith = (f ? f.virtue : 30) / 25 + c.population / 250000 + (capital ? 1 : 0);
  return {
    g, c, culture, family: FAMILY[culture] || 'hua', offs, troops, training, rulerHere, cav, wit, faith, capital,
    def: 20 + Math.trunc(c.town / 12) + (extra.garrison || 0),
    coastal: !!extra.coastal, river: !!extra.river, shopRare: !!extra.shopRare,
  };
}

// ------------------------------------------------------------------ triangles --
// Instanced / repeated content per stage (houses, props, crowd, trees). Numbers = the CITY-VIEW.md §9 budgets.
// budget = triangles DRAWN per frame including the ink-outline shells (outlineShare = fraction of geometry that is outlined).
const TIER = {
  desktop: { houses: [10, 16, 24, 34, 46], props: [40, 60, 80, 100, 120], propTris: 150, crowd: [10, 18, 28, 40, 56], crowdTris: 300, trees: 30, treeTris: 220, terrain: 9000, budget: 250000, outlineShare: 0.8 },
  phone: { houses: [8, 12, 16, 22, 28], props: [24, 34, 44, 56, 64], propTris: 140, crowd: [6, 10, 14, 18, 24], crowdTris: 260, trees: 18, treeTris: 200, terrain: 6000, budget: 150000, outlineShare: 0.6 },
};
function triEstimate(st, ctx, tierName) {
  const T = TIER[tierName || 'desktop'];
  let tris = T.terrain;
  for (const b of BUILDINGS) {
    const s = st[b.id];
    if (s < 0 || b.id === 'houses') continue;
    tris += b.tris[s];
  }
  const hs = st.houses;
  tris += T.houses[hs] * BY_ID.houses.tris[hs];
  const lvl = cityLevel(st);
  tris += T.props[lvl] * T.propTris;
  tris += T.crowd[Math.min(4, Math.floor(ctx.c.population / 200000))] * T.crowdTris;
  tris += T.trees * T.treeTris;
  // landmark (one per famous city, ≤ 3000) and the capital palace variant
  if (ctx.capital) tris += 3000;
  return { tris: Math.round(tris), drawn: Math.round(tris * (1 + T.outlineShare)), budget: T.budget };
}

// ------------------------------------------------------------------ Node run --
function loadGame() {
  global.window = global;
  const root = path.join(__dirname, '..', '..', 'js');
  const load = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
  for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);
  const SG = global.SG;
  SG.ScenarioData = SG.Scenarios.classic || SG.ScenarioData;
  for (const f of ['model.js', 'strategy-ai.js']) load(f);
  return SG;
}
const CLASSIC_COAST = ['beihai', 'guangling', 'wu', 'kuaiji', 'nanpi', 'beiping'];
const RIVER_CITIES = ['luoyang', 'xiangyang', 'jiangling', 'jiangxia', 'jianye', 'chengdu', 'jiangzhou', 'yongan', 'changsha', 'shouchun', 'xiapi', 'ye', 'puyang', 'changan', 'lujiang', 'yuzhang', 'wan'];

function run(json) {
  const SG = loadGame();
  SG.Random.seed(1);
  const out = {};
  for (const sc of ['classic', 'world']) {
    const firstKey = (SG.Scenarios[sc].Factions[0] || '').split('|')[0];
    const g = SG.G = SG.GameState.newGame(firstKey, sc);
    const sea = new Set();
    for (const l of (SG.Scenarios[sc].SeaLinks || [])) for (const k of l.split('-')) sea.add(k);
    const capitals = new Set();
    for (const f of g.factions) { const r = g.ruler(f.id); if (r) capitals.add(r.city); }
    const hist = {}, lvl = [0, 0, 0, 0, 0], est = [], estMax = [], estPhone = [], estPhoneMax = [];
    for (const b of BUILDINGS) hist[b.id] = [0, 0, 0, 0, 0, 0];
    for (const c of g.cities) {
      const ctx = ctxFor(g, c, { coastal: sea.has(c.key) || CLASSIC_COAST.includes(c.key), river: RIVER_CITIES.includes(c.key), capital: capitals.has(c.id) && (c.key === 'luoyang' || c.key === 'changan' || sc === 'world') });
      const st = stages(ctx);
      for (const b of BUILDINGS) hist[b.id][st[b.id] + 1]++;
      lvl[cityLevel(st)]++;
      est.push(triEstimate(st, ctx, 'desktop').drawn);
      estPhone.push(triEstimate(st, ctx, 'phone').drawn);
      const maxSt = {}; for (const b of BUILDINGS) maxSt[b.id] = st[b.id] < 0 ? -1 : 4;
      const big = Object.assign({}, ctx, { c: Object.assign({}, c, { population: 2000000 }), capital: true });
      estMax.push(triEstimate(maxSt, big, 'desktop').drawn);
      estPhoneMax.push(triEstimate(maxSt, big, 'phone').drawn);
    }
    const q = a => { const s = a.slice().sort((x, y) => x - y); return { min: s[0], med: s[s.length >> 1], p90: s[Math.floor(s.length * 0.9)], max: s[s.length - 1] }; };
    out[sc] = { cities: g.cities.length, hist, level: lvl, drawnDesktop: q(est), drawnPhone: q(estPhone), drawnDesktopMaxCity: q(estMax), drawnPhoneMaxCity: q(estPhoneMax) };
  }
  // develops per stage step: gain = 6 + pol/6 + R(0,4) → mean for pol 50 / 70 / 90
  const gains = [50, 70, 90].map(p => 6 + Math.trunc(p / 6) + 2);
  out.developsPerStep = { 'pol50 gain': gains[0], 'pol70 gain': gains[1], 'pol90 gain': gains[2], steps: [[300, 450], [450, 600], [600, 800], [800, 999]].map(([a, b]) => ({ from: a, to: b, pol50: Math.ceil((b - a) / gains[0]), pol70: Math.ceil((b - a) / gains[1]), pol90: Math.ceil((b - a) / gains[2]) })) };
  // checks
  const fails = [];
  for (const b of BUILDINGS) for (const t of b.tris) if ((b.cls === 'big' || b.cls === 'field') && t > 3000) fails.push(b.id + ' stage over 3000 tris');
  for (const b of BUILDINGS) if (b.cls === 'small' || b.cls === 'inst') for (const t of b.tris) if (t > 400) fails.push(b.id + ' small prop over 400 tris');
  for (const sc of ['classic', 'world']) {
    if (out[sc].drawnDesktopMaxCity.max > TIER.desktop.budget) fails.push(sc + ' all-S5 city over desktop drawn budget');
    if (out[sc].drawnPhoneMaxCity.max > TIER.phone.budget) fails.push(sc + ' all-S5 city over phone drawn budget');
  }
  out.fails = fails;
  if (json) { console.log(JSON.stringify(out, null, 1)); return; }
  for (const sc of ['classic', 'world']) {
    const o = out[sc];
    console.log(`\n== ${sc}: ${o.cities} cities — stage histogram at start (n/a, S1..S5)`);
    for (const b of BUILDINGS) console.log('  ' + b.id.padEnd(9) + o.hist[b.id].join(' / '));
    console.log('  city level S1..S5: ' + o.level.join(' / '));
    console.log('  drawn tris desktop (start) ', JSON.stringify(o.drawnDesktop));
    console.log('  drawn tris phone (start)   ', JSON.stringify(o.drawnPhone));
    console.log('  drawn tris desktop (all S5)', JSON.stringify(o.drawnDesktopMaxCity));
    console.log('  drawn tris phone (all S5)  ', JSON.stringify(o.drawnPhoneMaxCity));
  }
  console.log('\ndevelops per stage step', JSON.stringify(out.developsPerStep));
  console.log(fails.length ? 'FAIL ' + fails.join('; ') : 'ALL OK');
}

module.exports = { FAMILY, FAMILIES, BUILDINGS, HUBS, LEVEL_NAMES, stageOf, stages, cityLevel, ctxFor, triEstimate, TIER, hubTier, shopTier };
if (require.main === module) run(process.argv.includes('--json'));
