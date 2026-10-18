'use strict';
// Assemble design-v4/items.json from the tables, compute shop distribution for both scenarios, validate.
// usage: node build.js <scen.json> <out items.json>
const fs = require('fs');
const path = require('path');
const C = require('./core.js');
const { ROWS } = require('./commons.js');
const { MORE } = require('./commons2.js');
for (const k of Object.keys(MORE)) ROWS[k] = (ROWS[k] || []).concat(MORE[k]);
const { L, E } = require('./specials-a.js');
const B = require('./specials-b.js');

const scen = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const OUT = process.argv[3];

const { RARITY, TYPES, PAL, CULTURES, NEIGHBOURS, hash, statsFor, priceFor, round10 } = C;
const RMAP = { c: 'common', u: 'uncommon' };

// ------------------------------------------------------------------ duel base table (duel-game.js WEAPONS + new keys)
const DUEL = {
  guandao: { style: 'pole', reach: 2.0, pow: 1.05 }, snake: { style: 'pole', reach: 2.05, pow: 1 }, ji: { style: 'pole', reach: 2.0, pow: 1.06 },
  spear: { style: 'pole', reach: 2.05, pow: 1 }, poleblade: { style: 'pole', reach: 1.95, pow: 1.07 }, axe: { style: 'pole', reach: 1.88, pow: 1.12 },
  bigblade: { style: 'pole', reach: 1.88, pow: 1.14 }, sword: { style: 'one', reach: 1.78, pow: 1 }, dao: { style: 'one', reach: 1.75, pow: 1 },
  shortji: { style: 'one', reach: 1.8, pow: 1 }, mace: { style: 'one', reach: 1.72, pow: 0.97 }, twinsword: { style: 'dual', reach: 1.72, pow: 0.96 },
  twinji: { style: 'dual', reach: 1.75, pow: 0.97 }, twindao: { style: 'dual', reach: 1.68, pow: 1 }, gladius: { style: 'one', reach: 1.66, pow: 1.03 },
  lance: { style: 'pole', reach: 2.12, pow: 1 }, handaxe: { style: 'one', reach: 1.7, pow: 1.03 }, tachi: { style: 'one', reach: 1.76, pow: 1 },
  'fan*': { style: 'one', reach: 1.55, pow: 0.85, isNew: true }, 'staff*': { style: 'pole', reach: 1.95, pow: 0.88, isNew: true },
  'club*': { style: 'one', reach: 1.78, pow: 1.08, isNew: true }, 'vajra*': { style: 'one', reach: 1.6, pow: 1.05, isNew: true },
};

// short silhouette phrases (Lanternvale low-poly, flat shaded, ink outline)
const TV = {
  spear: '长杆枪矛：六棱杆、菱形矛头', lance: '超长骑矛：细杆、长锥矛头、杆尾配重', ji: '长戟：直刺加横枝', ge: '戈：横出的戈援装于木柄',
  poleblade: '长柄大刀：宽刃、短杆', bigblade: '斩马刀：长柄、阔长刀身', axe: '长柄大斧：半月宽刃', trident: '三叉长兵：三尖刃头',
  falx: '镰形长刀：内弯刃', staff: '杖节：长竹杆、顶饰', sword: '直剑：双刃、剑格', dao: '刀：单刃直背、环首或护手', gladius: '短剑：宽刃三角尖',
  spatha: '长剑：直刃、圆剑首', tachi: '直刀：单刃直身', khanda: '阔剑：宽直刃、圆盘护手', mace: '锤杵：短柄、多棱锤头', club: '棍棒：粗端木瘤',
  handaxe: '单手战斧：窄刃', shortji: '手戟：短柄小戟', bian: '鞭：竹节形铁身', ankusha: '象钩：直刺加弯钩', vajra: '金刚杵：两端五股刃',
  fan: '羽扇：扇形羽面、竹柄', twinsword: '双剑：一对短剑', twindao: '双刀：一对短刀', twinji: '双戟：一对短戟',
  bow: '弓：反曲弓身与箭囊（背负）', longbow: '长弓：与人等高（背负）', crossbow: '弩：弩臂与弩机（挂腰）', throw: '投掷兵器：一束标枪 / 飞刀（背负）',
  shield: '盾：中型盾、盾心凸钉', bigshield: '大盾：长方或长椭圆', buckler: '小盾：圆形、可格开',
  lamellar: '札甲：长方甲片编缀成层', scale: '鳞甲：鳞片上下叠压', mail: '锁子甲：细环纹理（贴图）', segmentata: '环片甲：横向弧形铁片',
  cuirass: '胸甲：整片胸背板', leather: '皮甲：硬皮甲片', rattan: '藤甲：藤条交编', cloth: '布甲：多层布衣', robe: '袍服：宽袖长袍',
  cataphract: '具装：人甲 + 马铠', hide: '兽皮 / 彩绘：披皮或身绘纹样', helm: '盔：圆顶或尖顶、护颈', crest: '缨盔：盔顶冠饰', mask: '面甲盔：覆面',
  capInt: '巾帻：软质头巾', capPol: '冠：漆纱冠梁', crown: '王冠：金饰高冠',
  horse: '战马：鞍鞯、笼头', warhorse: '甲马：马铠', pony: '矮马：短腿长鬃', camel: '骆驼：驼鞍', elephant: '战象：象轿', chariot: '战车：双马轻车',
  milbook: '兵书：卷轴挂于腰间', civbook: '典籍：书卷', seal: '印绶：系于腰带', tool: '器械：随军器具（城中模型）', banner: '旗帜：背旗',
  drum: '鼓角：背负或亲兵持', music: '乐器', jewel: '珍宝：宝匣', medicine: '医药：药囊', amulet: '护符：颈饰', relic: '圣物：小龛',
};
const RARITY_VIS = {
  common: '平涂无光效',
  uncommon: '刃口 / 边缘有一道淡玉绿描光',
  rare: '持续的柔和辉光与轻微粒子，发动能力时有专属特效',
  epic: '强烈辉光、待机粒子与专属挥动拖尾，能力发动时全屏演出',
  legendary: '金红辉光、专属待机动画与全屏演出，持有者立绘镶金边',
};

// ------------------------------------------------------------------ cities
function cityTier(c) {
  const s = c.ind + c.town;
  return s >= 1100 ? 4 : s >= 800 ? 3 : s >= 520 ? 2 : 1;
}
const HUBS = { luoyang: 4, changan: 4, roma: 4, alexandria: 4, antioch: 4, ctesiphon: 4, purushapura: 4, barygaza: 4, muziris: 4, palmyra: 4, charax: 4,
  panyu: 3, jiaozhi: 3, dunhuang: 2, shule: 3, yutian: 3, samarkand: 3, bactra: 4, taxila: 3, adulis: 3, oceo: 3, marib: 3, chengdu: 4, xiangyang: 3, wuwei: 2,
  dayuan: 3, tingis: 2, iol: 2, panticapaeum: 3, geumgwan: 2, yamatai: 2, ito: 2, caledonia: 2, tara: 2, istakhr: 2, hatra: 2, meroe: 3, edessa: 3, merv: 3 };
const SLOTS_BY_TIER = { 1: 4, 2: 6, 3: 8, 4: 10 };
function cityK(c) { return Math.round(Math.max(0.85, Math.min(1.15, 1.2 - (c.ind + c.town) / 2500)) * 100) / 100; }

const CLASSIC_TRADE = {
  steppe: ['beiping', 'jinyang', 'wuwei'], tarim: ['wuwei', 'tianshui', 'changan'], kushan: ['wuwei', 'changan', 'luoyang'], persia: ['changan', 'luoyang'],
  korea: ['beiping', 'beihai'], wa: ['beihai', 'kuaiji'], seasia: ['kuaiji', 'jianye', 'yunnan'], india: ['yunnan', 'jianning', 'kuaiji'], arab: ['luoyang'],
  roman: ['luoyang'], sarmatian: ['wuwei'], yi: ['kuaiji'], nanman: ['jianning', 'yunnan', 'yongan'],
  // INTEGRATION §7.5: the far west reaches the classic map only as curios — Aksumite ivory by the sea road (后汉书·西域传: 166 年大秦使者自日南献象牙),
  // Celtic, Germanic and Mauretanian goods through 大秦 merchants on the Silk Road (game liberty, tagged 舶来)
  africa: ['kuaiji', 'jianye'], celt: ['luoyang'], german: ['luoyang'], moor: ['luoyang'],
};

function prep(sid) {
  const S = scen[sid];
  const cities = S.cities.map(c => Object.assign({}, c, { tier: Math.max(cityTier(c), HUBS[c.key] || 0) }));
  const byCult = {};
  for (const c of cities) (byCult[c.culture] = byCult[c.culture] || []).push(c);
  return { cities, byCult, keys: new Set(cities.map(c => c.key)), gens: new Set(S.gens.map(g => g.name)), factions: S.factions };
}
const SC = { classic: prep('classic'), world: prep('world') };

// ------------------------------------------------------------------ items
const items = [];
const byId = new Map();
function push(it) {
  if (byId.has(it.id)) throw new Error('dup id ' + it.id);
  byId.set(it.id, it); items.push(it);
}

function reqFor(type, rarity, given) {
  const t = RARITY[rarity].tier;
  const r = {};
  if (['bigblade', 'axe', 'falx'].includes(type)) r.war = [50, 55, 60, 65, 70][t];
  if (type === 'milbook') r.intel = [40, 50, 60, 65, 70][t];
  if (type === 'civbook') r.pol = [40, 50, 60, 65, 70][t];
  if (type === 'fan') r.intel = [50, 55, 60, 65, 70][t];
  if (TYPES[type].style === 'shield') r.weaponStyle = 'one';
  if (given) Object.assign(r, given);
  return Object.keys(r).length ? r : null;
}

function duelFor(type, stats, overrideKey) {
  const T = TYPES[type];
  if (T.slot !== 'weapon') return null;
  const key = overrideKey || T.duel;
  const b = DUEL[key];
  if (!b) throw new Error('no duel base for ' + type + ' ' + key);
  const reach = Math.round((b.reach + (stats.dReach || 0)) * 100) / 100;
  const pow = Math.round(b.pow * (1 + (stats.dDmg || 0) / 100) * 1000) / 1000;
  return { key, style: b.style, reach, pow, newKey: !!b.isNew };
}

// motif / look keyword → procedural model part (figure.js attachment vocabulary, ITEMS.md §5.2)
const PARTS = [
  [/龙/, 'dragonHead'], [/虎/, 'tigerHead'], [/狮/, 'lionHead'], [/凤|雀/, 'phoenixCrest'], [/蛇|那伽/, 'serpentCoil'], [/鹰|雕/, 'eagle'],
  [/日轮|太阳|日焰|日纹/, 'sunDisk'], [/月牙|新月|偃月/, 'crescent'], [/宝塔|塔/, 'stupa'], [/铃/, 'bells'], [/缨|旄|马尾|流苏/, 'tassel'],
  [/绫|飘带|红绸|绸/, 'ribbons'], [/宝石|红玉|赤玉|青金|绿松|玛瑙|珍珠|勾玉|玉/, 'gem'], [/链|锁链/, 'chain'], [/金箍|金环|铁箍|箍/, 'rings'],
  [/符|篆|铭|字/, 'inscription'], [/错金|鎏金|包金|金片|金饰|金线|描金/, 'goldInlay'], [/火|焰|余烬/, 'flameFx'], [/雷|电/, 'lightningFx'],
  [/蒸汽|水汽|白汽/, 'steamFx'], [/云纹|卷云|云气|云/, 'cloudPattern'], [/水纹|水波|水光|浪/, 'waterFx'], [/羽/, 'feathers'], [/毛皮|兽皮|狼皮|豹皮|虎皮|狮皮/, 'furWrap'],
  [/骨|牙/, 'boneParts'], [/藤/, 'rattanWeave'], [/环首|环头/, 'ringPommel'], [/卷纹|螺旋/, 'spiralEtch'],
];
function partsFrom(text) {
  const out = [];
  for (const [re, p] of PARTS) if (re.test(text || '') && !out.includes(p)) out.push(p);
  return out.slice(0, 6);
}
function visualFor(it, motif, iconOver, lookOver, figOver) {
  const T = TYPES[it.type];
  const pal = PAL[it.culture];
  const model = { fig: figOver || T.fig || null, torso: T.torso || null, colors: (iconOver && iconOver.colors) || pal.c.slice(0, 3), ink: pal.c[3], motif, parts: partsFrom((motif || '') + (lookOver || '')),
    glow: it.rarity === 'common' ? null : it.rarity === 'uncommon' ? { color: '#9fe0b0', strength: 0.15 } : { color: (iconOver && iconOver.glow) || '#ffffff', strength: { rare: 0.35, epic: 0.6, legendary: 0.85 }[it.rarity] },
    scaleVar: Math.round((0.94 + (hash(it.id) % 13) / 100) * 100) / 100 };
  if (T.slot === 'mount') model.mountKind = it.type;
  if (T.slot === 'offhand' && T.style === 'shield') model.shield = { scutum: 'scutum', bigshield: 'pai', buckler: 'round', shield: 'round' }[it.type] || 'round';
  const look = lookOver || `${TV[it.type]}；${motif}；用色：${pal.metal}、${pal.trim}、${pal.cloth}；${RARITY_VIS[it.rarity]}。`;
  const icon = Object.assign({ shape: T.icon, colors: pal.c.slice(0, 3), glow: it.rarity === 'common' ? null : it.rarity === 'uncommon' ? '#9fe0b0' : null, motif, frame: RARITY[it.rarity].frame }, iconOver || {});
  if (iconOver) icon.frame = RARITY[it.rarity].frame;
  return { model, look, icon };
}

// ---- commons / uncommons
for (const cult of Object.keys(ROWS)) {
  for (const row of ROWS[cult]) {
    const [type, rr, id, name, orig, motif, note, srcKind, refs, opts] = row;
    if (!TYPES[type]) throw new Error('bad type ' + type + ' in ' + id);
    const rarity = RMAP[rr];
    const stats = statsFor(type, rarity, opts);
    const it = {
      id, name, orig: orig || null, slot: TYPES[type].slot, cat: TYPES[type].cat, type, culture: cult, rarity,
      stats, price: priceFor(type, rarity, opts), value: priceFor(type, rarity, opts),
      req: reqFor(type, rarity, null), unique: false, bound: false,
      duel: duelFor(type, stats), availability: null,
      source: { kind: srcKind, note, refs },
      visual: null, ability: null,
    };
    it.visual = visualFor(it, motif);
    push(it);
  }
}

// ---- specials
function addSpecial(o, rarity) {
  const type = o.type;
  if (!TYPES[type]) throw new Error('bad type ' + type + ' ' + o.id);
  let stats = statsFor(type, rarity, null);
  if (o.set) for (const [k, v] of Object.entries(o.set)) stats[k] = v;
  if (o.add) for (const [k, v] of Object.entries(o.add)) stats[k] = (stats[k] || 0) + v;
  for (const k of Object.keys(stats)) if (stats[k] === 0) delete stats[k];
  const shop = o.acq.k === 'shop';
  let price = null;
  if (shop) price = o.acq.price || o.price || priceFor(type, rarity, null);
  const value = rarity === 'legendary' ? 20000 : rarity === 'epic' ? (o.acq.price || 7000) : (o.price || priceFor(type, 'rare', null));
  const it = {
    id: o.id, name: o.name, orig: o.orig || null, slot: TYPES[type].slot, cat: TYPES[type].cat, type, culture: o.culture, rarity,
    stats, price, value,
    req: reqFor(type, rarity, o.req || null),
    unique: !shop, bound: o.acq.k === 'ruler',
    duel: duelFor(type, stats, o.duel),
    availability: null, acq: o.acq,
    source: { kind: o.src, note: o.note, refs: o.refs || [] },
    visual: null,
    ability: { id: 'ab_' + o.id, name: o.ab.n, learnable: false, effects: o.ab.e, desc: o.ab.d, fx: o.ab.fx },
  };
  it.visual = visualFor(it, o.icon && o.icon.motif, o.icon, o.look, o.fig);
  push(it);
}
for (const o of L) addSpecial(o, 'legendary');
for (const o of E) addSpecial(o, 'epic');
for (const list of [B.RULER, B.SIGNATURE, B.HIDDEN, B.TOURNEY, B.SHOP]) for (const o of list) addSpecial(o, 'rare');

// ------------------------------------------------------------------ original-language names
const { origFor } = require('./orig.js');
for (const it of items) if (!it.orig) it.orig = origFor(it);

// ------------------------------------------------------------------ availability
function pickSubset(list, frac, seed, min) {
  const scored = list.map(c => ({ c, h: hash(seed + '|' + c.key) }));
  scored.sort((a, b) => a.h - b.h);
  const n = Math.min(list.length, Math.max(min, Math.round(list.length * frac)));
  return scored.slice(0, n).map(x => x.c.key);
}

// foreign commons travel only so far: tier>=2 cities within NEIGH_DEG (lon/lat degrees) of a city of that culture, metropolises within HUB_DEG
const NEIGH_DEG = 10, HUB_DEG = 25;
function minDist(c, list) { let m = 1e9; for (const o of list) { const d = Math.hypot(c.lon - o.lon, c.lat - o.lat); if (d < m) m = d; } return m; }
function commonAvail(it, sid) {
  const S = SC[sid];
  const native = (S.byCult[it.culture] || []).map(c => c.key);
  const neigh = [];
  const homes = S.byCult[it.culture] || [];
  for (const c of S.cities) {
    if (c.culture === it.culture || c.tier < 2 || !(NEIGHBOURS[c.culture] || []).includes(it.culture) || !homes.length) continue;
    const d = minDist(c, homes);
    if (d <= NEIGH_DEG || (c.tier >= 4 && d <= HUB_DEG)) neigh.push(c.key);
  }
  let trade = [];
  if (sid === 'classic' && native.length === 0) trade = (CLASSIC_TRADE[it.culture] || []).filter(k => S.keys.has(k));
  return { native, neighbour: neigh, trade };
}
function uncommonAvail(it, sid) {
  const S = SC[sid];
  let elig = (S.byCult[it.culture] || []).filter(c => c.tier >= 2);
  if (elig.length < 3) elig = S.byCult[it.culture] || [];
  let native = elig.length ? pickSubset(elig, 0.35, it.id + sid, 1) : [];
  let trade = [];
  if (sid === 'classic' && native.length === 0) {
    const t = (CLASSIC_TRADE[it.culture] || []).filter(k => S.keys.has(k));
    if (t.length) trade = [t[0]];
  }
  // neighbour metropolises (tier 4) occasionally carry foreign uncommons
  const homesU = S.byCult[it.culture] || [];
  const hubs = homesU.length ? S.cities.filter(c => c.tier >= 4 && c.culture !== it.culture && (NEIGHBOURS[c.culture] || []).includes(it.culture) && minDist(c, homesU) <= HUB_DEG) : [];
  const neighbour = hubs.filter(c => hash(it.id + '|hub|' + c.key) % 4 === 0).map(c => c.key);
  return { native, neighbour, trade };
}

function scenOK(acq, sid) {
  if (acq.scen) return acq.scen.includes(sid);
  return true;
}

for (const it of items) {
  const av = { classic: null, world: null };
  for (const sid of ['classic', 'world']) {
    const S = SC[sid];
    if (it.rarity === 'common' || it.rarity === 'uncommon') {
      const a = it.rarity === 'common' ? commonAvail(it, sid) : uncommonAvail(it, sid);
      av[sid] = a.native.length + a.neighbour.length + a.trade.length ? Object.assign({ kind: 'shop' }, a) : null;
      continue;
    }
    let acq = it.acq;
    if (!scenOK(acq, sid)) acq = acq.classic && sid === 'classic' ? acq.classic : null;
    if (!acq) { av[sid] = null; continue; }
    switch (acq.k) {
      case 'shop': {
        let cities = (acq.cities || []).filter(k => S.keys.has(k));
        if (sid === 'classic' && it.acq.classic && it.acq.classic.k === 'shop' && acq === it.acq) cities = it.acq.classic.cities.filter(k => S.keys.has(k));
        if (sid === 'classic' && acq !== it.acq && acq.k === 'shop') cities = acq.cities.filter(k => S.keys.has(k));
        // INTEGRATION §7.5: a world-only shop rare is sold in the classic scenario by the first classic trade city of its culture (舶来, ×1.2)
        let foreign = false;
        if (sid === 'classic' && !cities.length && it.rarity === 'rare' && (CLASSIC_TRADE[it.culture] || []).length) { cities = [CLASSIC_TRADE[it.culture][0]].filter(k => S.keys.has(k)); foreign = cities.length > 0; }
        av[sid] = cities.length ? Object.assign({ kind: 'shop', cities, stock: acq.stock || 1, restockMonths: acq.restockMonths || (it.rarity === 'epic' ? 60 : 24), price: acq.price || it.price }, foreign ? { foreign: true } : {}) : null;
        break;
      }
      case 'ruler': {
        const f = S.factions.find(x => x.key === acq.f);
        av[sid] = f ? { kind: 'ruler', faction: acq.f, holder: f.ruler } : null;
        break;
      }
      case 'sig': av[sid] = S.gens.has(acq.holder) ? { kind: 'signature', holder: acq.holder } : null; break;
      case 'hidden': av[sid] = S.keys.has(acq.city) ? { kind: 'hidden', city: acq.city, p: acq.p, intelK: acq.intelK || 0.0005, from: acq.from || null, clue: acq.clue || null } : null; break;
      case 'city': av[sid] = S.keys.has(acq.city) ? { kind: 'cityTreasure', city: acq.city } : null; break;
      case 'tourney': av[sid] = { kind: 'tournament', pool: acq.pool }; break;
      case 'armoury': av[sid] = S.factions.find(x => x.key === acq.f) ? { kind: 'armoury', faction: acq.f } : null; break;
      default: throw new Error('acq ' + acq.k);
    }
  }
  // world-only foreign rares with no classic route join the classic 'foreign' tournament pool (异域奇珍) — never a ruler-unique:
  // request 3 makes the rulers' unique weapons obtainable only by slaying their holder (INTEGRATION §7.3)
  if (!av.classic && it.rarity === 'rare' && it.acq && ['hidden', 'city', 'sig'].includes(it.acq.k) && !['han', 'nanman'].includes(it.culture)) {
    av.classic = { kind: 'tournament', pool: 'foreign' };
  }
  if (it.bound) for (const sid of ['classic', 'world']) if (av[sid] && av[sid].kind !== 'ruler') av[sid] = null;
  it.availability = av;
  it.scenarios = ['classic', 'world'].filter(s => av[s]);
  delete it.acq;
}

// ------------------------------------------------------------------ shops table
function shopsFor(sid) {
  const S = SC[sid];
  const out = {};
  for (const c of S.cities) {
    out[c.key] = { name: c.name, culture: c.culture, tier: c.tier, slots: SLOTS_BY_TIER[c.tier], priceK: cityK(c), common: [], commonForeign: [], uncommon: [], rare: [], epic: [] };
  }
  for (const it of items) {
    const a = it.availability[sid];
    if (!a || a.kind !== 'shop') continue;
    if (it.rarity === 'common') {
      for (const k of a.native) out[k].common.push(it.id);
      for (const k of a.neighbour.concat(a.trade)) out[k].commonForeign.push(it.id);
    } else if (it.rarity === 'uncommon') {
      for (const k of a.native.concat(a.neighbour, a.trade)) out[k].uncommon.push(it.id);
    } else if (it.rarity === 'rare') { for (const k of a.cities) out[k].rare.push(it.id); }
    else if (it.rarity === 'epic') { for (const k of a.cities) out[k].epic.push(it.id); }
  }
  return out;
}
const shops = { classic: shopsFor('classic'), world: shopsFor('world') };

// ------------------------------------------------------------------ rulers / signature / hidden / tournament tables
function tables(sid) {
  const S = SC[sid];
  const rulers = {};
  for (const f of S.factions) {
    const it = items.find(x => x.availability[sid] && x.availability[sid].kind === 'ruler' && x.availability[sid].faction === f.key);
    rulers[f.key] = it ? { ruler: f.ruler, item: it.id, rarity: it.rarity } : null;
  }
  const signature = items.filter(x => x.availability[sid] && x.availability[sid].kind === 'signature').map(x => ({ holder: x.availability[sid].holder, item: x.id, rarity: x.rarity }));
  const hidden = items.filter(x => x.availability[sid] && x.availability[sid].kind === 'hidden').map(x => Object.assign({ item: x.id, rarity: x.rarity }, x.availability[sid]));
  const cityTreasure = items.filter(x => x.availability[sid] && x.availability[sid].kind === 'cityTreasure').map(x => ({ city: x.availability[sid].city, item: x.id, rarity: x.rarity }));
  const armoury = items.filter(x => x.availability[sid] && x.availability[sid].kind === 'armoury').map(x => ({ faction: x.availability[sid].faction, item: x.id }));
  const tour = { champion: [], title3: [], title5: [], foreign: [] };
  for (const x of items) { const a = x.availability[sid]; if (a && a.kind === 'tournament') tour[a.pool].push(x.id); }
  return { rulers, signature, hidden, cityTreasure, armoury, tournament: tour };
}

// ------------------------------------------------------------------ rare art (INTEGRATION §7.6): a signature part, a proc effect and a sound for every rare
// The parts library grows from the 28 shared parts above to 80: these 52 signature parts are small (≤ 200 triangles at high LOD) and placed
// by socket; every rare gets one tuple (part, socket, count, colour) of its own, picked from its name / motif / look, so no two rares share it.
const SIG_PARTS = [
  [/七星|北斗|星/, 'starInlay', 'blade'], [/波|浪|水纹/, 'waveBack', 'spine'], [/枝|七支/, 'branchTines', 'blade'], [/印|玺|钮/, 'sealKnob', 'pommel'],
  [/镡|格/, 'guardWings', 'guard'], [/缠|绳|绦/, 'hiltWrap', 'grip'], [/鞘/, 'scabbardBands', 'scabbard'], [/槽|血槽|脊/, 'bladeGroove', 'blade'],
  [/孔|穿/, 'bladeHoles', 'blade'], [/镜/, 'mirrorBoss', 'chest'], [/面甲|面具|假面/, 'faceMask', 'face'], [/角/, 'hornPair', 'crest'],
  [/翎|雉尾|长羽/, 'plumeTall', 'crest'], [/冠|鸡冠|梳/, 'crestComb', 'crest'], [/光芒|放射/, 'sunburst', 'boss'], [/莲/, 'lotus', 'pommel'],
  [/螺|海螺/, 'conchShell', 'belt'], [/孔雀/, 'peacockEye', 'tail'], [/雷纹|回纹/, 'thunderGlyph', 'band'], [/卢恩|铭文带|符文/, 'runeBand', 'band'],
  [/项圈|托尔克|torc/, 'torcRing', 'neck'], [/月桂|橄榄|桂/, 'laurel', 'crest'], [/双翼|翼/, 'eagleWings', 'shoulder'], [/鳞/, 'scaleRidge', 'spine'],
  [/象牙/, 'ivoryInlay', 'grip'], [/珠/, 'pearlString', 'band'], [/璧|玉环/, 'jadeDisc', 'pommel'], [/钟|铎/, 'bronzeBell', 'hang'],
  [/旗|幡|幢/, 'bannerTail', 'pole'], [/结|绶/, 'cordKnot', 'hang'], [/鹿/, 'antlerTips', 'crest'], [/马首|马头/, 'horseHead', 'pommel'],
  [/熊/, 'bearClaw', 'hang'], [/狼/, 'wolfTail', 'hang'], [/鱼/, 'fishScale', 'blade'], [/龟/, 'turtleShell', 'boss'],
  [/象/, 'elephantTusk', 'front'], [/驼/, 'camelBell', 'neck'], [/金叶|叶/, 'leafEtch', 'blade'], [/花/, 'flowerRosette', 'boss'],
  [/漆|朱漆/, 'lacquerBand', 'shaft'], [/铜钉|钉/, 'studRow', 'rim'], [/锯|齿/, 'sawEdge', 'blade'], [/钩/, 'hookSpur', 'blade'],
  [/刺|尖/, 'spikeCap', 'tip'], [/环/, 'ringSet', 'shaft'], [/卷轴|竹简|简/, 'slipBundle', 'case'], [/书|经/, 'bookCase', 'case'],
  [/盒|匣|函/, 'reliquary', 'case'], [/香|乳香/, 'incenseCup', 'hang'], [/鼓/, 'drumHead', 'face'], [/弦|琴/, 'stringSet', 'face'],
];
const SOCKET_BY_SLOT = { weapon: ['blade', 'guard', 'grip', 'pommel', 'spine', 'tip', 'shaft'], offhand: ['boss', 'rim', 'face', 'grip'], armour: ['chest', 'shoulder', 'belt', 'band'], helmet: ['crest', 'band', 'face'], mount: ['neck', 'front', 'tail', 'band'], treasure: ['case', 'hang', 'face', 'band'] };
const FX_BY_EFFECT = {
  proc: 'inkBurst', dProc: 'inkBurst', crit: 'goldFlare', dCrit: 'goldFlare', aura: 'brushRing', opening: 'brushRing', cmd: 'sealStamp', free: 'sealStamp',
  income: 'coinArc', harvest: 'grainSwirl', pop: 'lanternRise', loyalAura: 'lanternRise', charge: 'dustStreak', first: 'dustStreak', mobility: 'dustStreak',
  dParry: 'parryFlash', dGuard: 'paperShield', ward: 'paperShield', dmgTaken: 'paperShield', castle: 'paperShield', fame: 'lanternGlow', virtue: 'lanternGlow',
  expBoost: 'scrollUnfurl', expMonthly: 'scrollUnfurl', dipPlus: 'cordKnot', giftK: 'cordKnot', active: 'brushRing', activeChoice: 'brushRing',
  onRout: 'inkBurst', splash: 'inkBurst', sunder: 'crackInk', sunderAll: 'crackInk', counterUp: 'parryFlash', counterGuard: 'paperShield', dRegen: 'leafDrift', regen: 'leafDrift',
  tacAmp: 'scrollUnfurl', escapeK: 'smokePuff', decoy: 'smokePuff', rescue: 'smokePuff', medic: 'leafDrift', talent: 'lanternGlow', treasureHunt: 'lanternGlow',
};
const SFX_BY_CAT = { blade: 'ring-blade', pole: 'whoosh-pole', bow: 'twang', shield: 'thud-wood', armour: 'clank-plate', helmet: 'clink-helm', mount: 'hoof-neigh', book: 'page-flip', seal: 'stamp', instrument: 'pluck', jewel: 'chime', other: 'bell' };
function sfxFamily(it) {
  const T = TYPES[it.type];
  if (it.slot === 'weapon') return T && T.style === 'pole' ? 'pole' : 'blade';
  if (it.slot === 'offhand') return ['bow', 'longbow', 'crossbow', 'throw'].includes(it.type) ? 'bow' : 'shield';
  if (it.slot === 'armour') return 'armour';
  if (it.slot === 'helmet') return 'helmet';
  if (it.slot === 'mount') return 'mount';
  if (['milbook', 'civbook'].includes(it.type)) return 'book';
  if (it.type === 'seal') return 'seal';
  if (['music', 'drum'].includes(it.type)) return 'instrument';
  if (it.type === 'jewel') return 'jewel';
  return 'other';
}
{
  const used = new Set();
  for (const it of items) {
    if (!['rare', 'epic', 'legendary'].includes(it.rarity)) continue;
    const text = it.name + '|' + (it.visual.model.motif || '') + '|' + (it.visual.look || '');
    const colors = it.visual.model.colors || ['#cccccc'];
    if (it.rarity === 'rare') {
      const cands = SIG_PARTS.filter(([re]) => re.test(text)).map(([, p, at]) => [p, at]);
      const sockets = SOCKET_BY_SLOT[it.slot] || ['band'];
      for (const [, p] of SIG_PARTS) if (!cands.some(c => c[0] === p)) cands.push([p, null]);   // fallback: any part, deterministic order below
      const h = hash(it.id) >>> 0;
      let sig = null;
      for (let k = 0; k < cands.length * 6 && !sig; k++) {
        const [p, at0] = cands[k < cands.length ? k : (h + k) % cands.length];
        const at = sockets.includes(at0) ? at0 : sockets[(h + k) % sockets.length];
        const count = /七/.test(it.name) && p === 'starInlay' ? 7 : 1 + ((h >>> 3) + k) % 3;
        const color = colors[((h >>> 5) + k) % colors.length];
        const key = [p, at, count, color].join('|');
        if (!used.has(key)) { used.add(key); sig = { part: p, at, count, color, tris: 120 + (h % 5) * 16 }; }
      }
      it.visual.model.signature = sig;
    }
    const first = (it.ability && it.ability.effects[0]) || { t: 'aura' };
    const pick = it.ability.effects.map(e => FX_BY_EFFECT[e.t]).find(Boolean) || 'brushRing';
    it.ability.procFx = { preset: pick, color: (it.visual.icon && it.visual.icon.glow) || colors[0], dur: Math.min(0.6, 0.35 + ((hash(it.id + 'fx') >>> 0) % 6) * 0.05), drawCalls: 1, pooled: true };
    it.ability.sfx = { id: 'item.' + it.id, family: SFX_BY_CAT[sfxFamily(it)], pitch: 0.9 + ((hash(it.id + 'sfx') >>> 0) % 21) / 100 };
  }
}

// ------------------------------------------------------------------ validation
const problems = [];
function check(cond, msg) { if (!cond) problems.push(msg); }
const count = (f) => items.filter(f).length;
const nWeapons = count(x => x.cat === 'weapon');
const nGear = count(x => x.cat === 'gear');
check(nWeapons >= 250, 'weapons < 250: ' + nWeapons);
check(nGear >= 250, 'gear < 250: ' + nGear);
check(count(x => x.rarity === 'legendary') === 4, 'legendary != 4');
check(count(x => x.rarity === 'epic') === 20, 'epic != 20');
const elCult = new Set(items.filter(x => x.rarity === 'epic' || x.rarity === 'legendary').map(x => x.culture));
for (const c of CULTURES) check(elCult.has(c), 'culture without epic/legendary: ' + c);
const allCult = new Set(items.map(x => x.culture));
for (const c of CULTURES) check(allCult.has(c), 'culture without items: ' + c);
const names = new Map();
for (const it of items) { check(!names.has(it.name), 'dup name ' + it.name + ' ' + it.id + ' / ' + names.get(it.name)); names.set(it.name, it.id); }
const abNames = new Map(); const abSig = new Map();
for (const it of items) {
  if (RARITY[it.rarity].tier >= 2) check(it.ability, 'rare+ without ability ' + it.id);
  if (!it.ability) continue;
  check(!abNames.has(it.ability.name), 'dup ability name ' + it.ability.name + ' ' + it.id);
  abNames.set(it.ability.name, it.id);
  const sig = JSON.stringify(it.ability.effects);
  check(!abSig.has(sig), 'dup ability effects ' + it.id + ' = ' + abSig.get(sig));
  abSig.set(sig, it.id);
  for (const e of it.ability.effects) if (e.name) { check(!abNames.has('A:' + e.name), 'dup active name ' + e.name); abNames.set('A:' + e.name, it.id); }
}
for (const sid of ['classic', 'world']) {
  const T = tables(sid);
  for (const [k, v] of Object.entries(T.rulers)) check(v, sid + ' faction without ruler-unique: ' + k);
  // every shop has at least one weapon and one armour-class common in its native or foreign pool
  for (const [k, s] of Object.entries(shops[sid])) {
    const pool = s.common.concat(s.commonForeign).map(id => byId.get(id));
    check(pool.some(x => x.cat === 'weapon' && x.slot === 'weapon'), sid + ' shop without weapon: ' + k);
    check(pool.some(x => ['armour', 'helmet'].includes(x.slot) || (x.slot === 'offhand' && x.cat === 'gear')), sid + ' shop without armour: ' + k);
    check(pool.length >= 4, sid + ' shop pool < 4: ' + k + ' ' + pool.length);
  }
}
// every rare+ has an acquisition route in at least one scenario; L/E routes
for (const it of items) check(it.scenarios.length > 0, 'no route: ' + it.id);
// INTEGRATION §7.2: no ruler-unique (bound) item on any non-slaying route, in either scenario
for (const it of items) if (it.bound) for (const sid of ['classic', 'world']) check(!it.availability[sid] || it.availability[sid].kind === 'ruler', 'ruler-unique on a non-slaying route: ' + it.id + ' ' + sid);
for (const sid of ['classic', 'world']) for (const pool of Object.values(tables(sid).tournament)) for (const id of pool) check(!byId.get(id).bound, 'ruler-unique in a tournament pool: ' + id + ' ' + sid);
// INTEGRATION §7.3: ≥ 255 weapons and ≥ 255 armour / equipment in EACH scenario, counting only items reachable outside the 15 % 异域奇珍 roll
for (const sid of ['classic', 'world']) {
  const reach = items.filter(x => x.availability[sid] && !(x.availability[sid].kind === 'tournament' && x.availability[sid].pool === 'foreign'));
  check(reach.filter(x => x.cat === 'weapon').length >= 255, sid + ': weapons reachable outside the roll < 255: ' + reach.filter(x => x.cat === 'weapon').length);
  check(reach.filter(x => x.cat === 'gear').length >= 255, sid + ': gear reachable outside the roll < 255: ' + reach.filter(x => x.cat === 'gear').length);
}
// INTEGRATION §7.4: every rare has a signature part of its own, every rare+ ability a proc effect and a sound
for (const it of items) if (it.rarity === 'rare') check(it.visual.model.signature && it.visual.model.signature.tris <= 200, 'rare without signature part: ' + it.id);
for (const it of items) if (['rare', 'epic', 'legendary'].includes(it.rarity)) check(it.ability.procFx && it.ability.procFx.dur <= 0.6 && it.ability.sfx, 'rare+ without procFx / sfx: ' + it.id);
// INTEGRATION §8.2 DUEL_ITEM_EFFECTS whitelist: no unblockable / unparryable flags, projectiles ≤ 4 HP, combo bonus ≤ 3 HP, no super armour
for (const it of items) if (it.ability) for (const e of it.ability.effects) {
  check(!e.unblockable && !e.unparryable && !e.dashUnblockable, 'duel flag not allowed: ' + it.id + ' ' + e.t);
  if (e.t === 'dProjectile') check((e.dmg || 0) * (e.count || 1) <= 4, 'projectile over 4 HP: ' + it.id);
  if (e.t === 'dCombo') check((e.bonusDmg || 0) <= 3 && (e.lightBonus || 0) <= 0.04, 'combo bonus over budget: ' + it.id);
  if (e.t === 'dArmor') check(e.dmgK >= 0.5 && ['heavyStart', 'lightTaken'].includes(e.on), 'dArmor must be a damage reduction: ' + it.id);
  if (e.t === 'dPerfectGuard') check(false, 'dPerfectGuard is replaced by dParry.guardWindow: ' + it.id);
  if (e.t === 'dParry') check((e.window || 0) <= 0.01 && (e.guardWindow || 0) <= 0.02 && (e.counterK || 1.5) <= 1.7, 'dParry over the whitelist: ' + it.id);
  if (e.t === 'dDmgK') check(e.k <= 0.03, 'dDmgK over the gear damage cap: ' + it.id);
  if (e.t === 'dProc' && e.eff === 'knockdown') check(e.on === 'heavyFull' || (e.p || 1) < 1, 'guaranteed knockdown only on full-charge heavies: ' + it.id);
  if (e.t === 'dipPlus') check(!e.noBreak, 'noBreak is forbidden (INTEGRATION §7.6): ' + it.id);
}
for (const it of items) if (it.culture !== 'han') check(!!it.orig, 'foreign item without orig: ' + it.id);
// stats sanity; every single item within the per-general cap of each stat key (statKeys[k].cap, duel crit capDuel)
for (const it of items) for (const [k, v] of Object.entries(it.stats)) { const sk = require('./statkeys.js')[k]; if (sk && typeof v === 'number' && typeof sk.cap === 'number') check(Math.abs(v) <= sk.cap + 1e-9, 'stat over its cap: ' + it.id + ' ' + k + ' ' + v + ' > ' + sk.cap); }
for (const it of items) for (const [k, v] of Object.entries(it.stats)) { if (typeof v === 'number') check(isFinite(v), 'nan stat ' + it.id + k); }

// uncommon minority / rare few
const distrib = { world: {}, classic: {} };
for (const sid of ['classic', 'world']) {
  const n = SC[sid].cities.length;
  const cnt = r => items.filter(x => x.rarity === r && x.availability[sid] && x.availability[sid].kind === 'shop').map(x => {
    const a = x.availability[sid]; return (a.cities || []).length + (a.native || []).length + (a.neighbour || []).length + (a.trade || []).length; });
  const stat = arr => arr.length ? { items: arr.length, minCities: Math.min(...arr), maxCities: Math.max(...arr), avgCities: Math.round(arr.reduce((a, b) => a + b, 0) / arr.length * 10) / 10, avgPctOfCities: Math.round(arr.reduce((a, b) => a + b, 0) / arr.length / n * 1000) / 10 } : null;
  distrib[sid] = { cities: n, common: stat(cnt('common')), uncommon: stat(cnt('uncommon')), rare: stat(cnt('rare')), epic: stat(cnt('epic')) };
}

// ------------------------------------------------------------------ write
const meta = {
  version: 1,
  generator: 'design-v4 items catalogue (scratchpad build.js); tables in ITEMS.md',
  counts: {
    total: items.length, weapons: nWeapons, gear: nGear,
    byRarity: Object.fromEntries(Object.keys(RARITY).map(r => [r, count(x => x.rarity === r)])),
    bySlot: Object.fromEntries(Object.keys(C.SLOTS).map(s => [s, count(x => x.slot === s)])),
    byCulture: Object.fromEntries(CULTURES.map(c => [c, count(x => x.culture === c)])),
    abilities: count(x => x.ability),
    classic: count(x => x.availability.classic), world: count(x => x.availability.world),
  },
  distribution: distrib,
};
const stripped = items.map(it => it);
// starting-kit lookup: culture → item type → first common of that culture (fallback chain: culture → neighbours → han)
const FIG2TYPE = { guandao: 'poleblade', snake: 'spear', halberd: 'ji', swords: 'twinsword', twinji: 'twinji', bigblade: 'bigblade', fengzui: 'poleblade',
  shortji: 'shortji', bawang: 'spear', yajiao: 'spear', hutou: 'spear', yitian: 'sword', guding: 'dao', fan: 'fan', bigaxe: 'axe', poleblade: 'poleblade',
  trident: 'trident', spear: 'spear', pilum: 'spear', lance: 'lance', dao: 'dao', jian: 'sword', gladius: 'gladius', spatha: 'spatha', tachi: 'tachi',
  mace: 'mace', handaxe: 'handaxe', axe: 'axe', twindao: 'twindao', bow: 'bow', khanda: 'khanda', longbow: 'longbow', staff: 'staff' };
const STYLE_FALLBACK = { pole: ['spear', 'lance', 'ji', 'poleblade', 'axe', 'ge', 'trident', 'staff'], one: ['dao', 'sword', 'spatha', 'gladius', 'tachi', 'khanda', 'mace', 'handaxe', 'club'], dual: ['twinsword', 'twindao', 'twinji', 'dao', 'sword'], fan: ['fan', 'staff', 'sword'] };
const kits = {};
for (const cult of CULTURES) {
  const chain = [cult].concat(NEIGHBOURS[cult] || []);   // pieces from outside the culture's trade sphere are skipped (null)
  const k = { byType: {}, armour: null, armourLight: null, helmet: null, cap: null, robe: null, shield: null, bow: null, mount: null };
  const firstCommon = (pred) => { for (const c of chain) { const x = items.find(i => i.rarity === 'common' && i.culture === c && pred(i)); if (x) return x.id; } return null; };
  for (const t of Object.keys(TYPES)) if (TYPES[t].slot === 'weapon') k.byType[t] = firstCommon(i => i.type === t);
  k.armour = firstCommon(i => ['lamellar', 'scale', 'mail', 'segmentata', 'cuirass'].includes(i.type));
  k.armourLight = firstCommon(i => ['leather', 'cloth', 'hide', 'rattan'].includes(i.type));
  k.helmet = firstCommon(i => i.type === 'helm' || i.type === 'crest');
  k.cap = firstCommon(i => i.type === 'capInt' || i.type === 'capPol');
  k.robe = firstCommon(i => i.type === 'robe' || i.type === 'cloth');
  k.shield = firstCommon(i => ['shield', 'buckler', 'bigshield'].includes(i.type));
  k.bow = firstCommon(i => ['bow', 'longbow'].includes(i.type));
  k.mount = cult === 'wa' ? null : firstCommon(i => ['horse', 'pony', 'camel'].includes(i.type));
  kits[cult] = k;
}
const out = {
  meta,
  fig2type: FIG2TYPE, styleFallback: STYLE_FALLBACK, kits,
  rarities: RARITY, slots: C.SLOTS,
  types: Object.fromEntries(Object.entries(TYPES).map(([k, v]) => [k, Object.assign({}, v, { profile: C.P[k] || {} })])),
  duelWeapons: DUEL,
  statKeys: require('./statkeys.js'),
  cultures: Object.fromEntries(CULTURES.map(c => [c, { name: C.CULT_NAME[c], palette: PAL[c].c, neighbours: NEIGHBOURS[c], classicTrade: CLASSIC_TRADE[c] || [] }])),
  items: stripped,
  shops,
  classic: tables('classic'),
  world: tables('world'),
};
fs.writeFileSync(OUT, JSON.stringify(out, null, 1));
console.log(JSON.stringify(meta, null, 1));
if (problems.length) { console.log('PROBLEMS (' + problems.length + '):\n' + problems.join('\n')); process.exitCode = 1; } else console.log('VALIDATION OK');
