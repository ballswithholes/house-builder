'use strict';
/* ==========================================================================
   tools/gen-engage.js — 第四版玩法目录（ENGAGEMENT.md）的校验与生成
   design-v4/engagement.json 是唯一的数据源（由设计期的 design-v4/engage/catalog.js → build-json.js 写出，勿手改）：
   33 个事件（30 个有选项、3 个喜报、75 个选项）、5 条史事、高士 / 祥瑞 / 献策模板、17 个国策、13 项营建（12 个家族的名字）、
   44 处探索地、12 个节庆（+ 势力覆盖）、48 项功业、42 对初始羁绊、30 对初始邦交。

   用法：
     node tools/gen-engage.js [js]       校验，写 js/engage-data.js（SG.EngageData；缺省模式）
     node tools/gen-engage.js check      只校验（退出码 = 问题数；tests/engage.js §1 调用 check()）
     node tools/gen-engage.js cs [out]   写 Unity 的 EngageData.g.cs（目录表以 JSON 字符串内嵌 + 哈希 + 计数；
                                         缺省 ../sanguozhi2-unity/Assets/Sanguo/Scripts/Data/Generated/EngageData.g.cs）
   作为模块：require('./tools/gen-engage.js') → { load, check, compact, hash, FAMILIES, CULTURES, PLACEHOLDERS }

   js/engage-data.js：SG.EngageData = { events, historical, sages, omens, proposals, missions, missionReward, projects, sites,
     siteTypes, festivals, festivalRule, achievements, bonds, relations, family, river, hash, meta }（ENGAGEMENT §2 的键名）

   校验（check）= design-v4/engage/build-json.js 的全部检查（编号唯一、效果键在词汇表内、buff 的 mod 合法、2–3 个选项、
     每个有选项的事件都有电脑可选的项与一项免费的项、月份合法、18 种文化都有城池家族、12 个家族都有招牌节庆、营建有汉名 / any、
     探索地的最近城在世界剧本中、羁绊人名与邦交势力键在剧本中、成就 / 国策编号唯一且期限 3 档）
   + ENGAGEMENT §22.1 第 1 项：每段文案（含每个家族的变体）的占位符都可解析；高士的城都存在；每项营建在 12 个家族里回退后都有名字；
     每处探索地从至少一个势力的城出发 8 步之内可达；条件 / aiIf / 执行者 / 选项键都在已知词汇内；成就条件键已知；国策目标已知；
     节庆月份 1–12、覆盖的势力键存在；史事的日期在剧本时间内。
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const crypto = require('crypto');

const ROOT = path.join(__dirname, '..');
const JSON_PATH = path.join(ROOT, 'design-v4', 'engagement.json');
const OUT_JS = path.join(ROOT, 'js', 'engage-data.js');
const OUT_CS = path.join(ROOT, '..', 'sanguozhi2-unity', 'Assets', 'Sanguo', 'Scripts', 'Data', 'Generated', 'EngageData.g.cs');

const CULTURES = ['han', 'nanman', 'wa', 'yi', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian', 'india', 'africa', 'moor'];
const FAMILIES = ['hua', 'han3', 'wa', 'steppe', 'oasis', 'indic', 'persia', 'arabia', 'nile', 'roman', 'north', 'nanhai'];
const EFFECT_KEYS = new Set(['gold', 'food', 'land', 'industry', 'town', 'population', 'order', 'troops', 'training', 'defense', 'prestige', 'fame', 'virtue',
  'exp', 'expLater', 'loyalty', 'officers.loyalty', 'loyalty3', 'bond3', 'away', 'gtroops', 'buff', 'rel', 'bond', 'bondKind', 'reveal', 'item', 'flag', 'chain',
  'defect', 'separate', 'plan', 'roll3', 'projectDiscount']);
const BUFF_MODS = /^(harvest|income|pop|orderTarget|eventW:[a-z]+|spread|caravan|foodPrice|defense)$/;
const WHEN_KEYS = new Set(['months', 'riverOrCoast', 'minLand', 'notFamilies', 'families', 'minLat', 'minPop', 'river', 'neighborTrouble', 'minTown', 'minOrder', 'minTier',
  'govPol', 'minVirtue', 'hiddenHere', 'scenario', 'minFame', 'minCities', 'date', 'minAvgOrder', 'minPrestige', 'deserterNear', 'farFaction', 'needPair', 'needGen', 'coastal', 'maxOrder']);
const AIIF_KEYS = new Set(['foodOverHarv', 'goldOverInc', 'goldOver', 'troopsOver', 'orderBelow', 'minVirtue', 'govPol', 'genWar', 'threatened', 'notThreatened']);
const AGENTS = new Set(['gov', 'ruler', 'subject', 'pick:war', 'pick:intel', 'pick:pol', 'pick:any']);
const OPTION_KEYS = new Set(['id', 'label', 'agent', 'cost', 'effects', 'ai', 'aiIf', 'roll', 'need', 'healer', 'opensShop', 'where', 'playerOnly', 'duel', 'win', 'miniGame', 'needTourneyWithin', 'rec']);
const EVENT_KEYS = new Set(['id', 'name', 'cat', 'scope', 'icon', 'w', 'cd', 'disaster', 'when', 'variants', 'text', 'pre', 'options', 'src', 'wPop', 'spread', 'good', 'modal',
  'wOrder', 'note', 'wTown', 'wVirtue', 'personas', 'globalP', 'pairCd']);
const CATS = new Set(['nature', 'society', 'court', 'people', 'global']);
const SCOPES = new Set(['city', 'faction', 'pair', 'general']);
const PAIRS = new Set(['spar', 'oath', 'mentor', 'enemies', 'quarrel']);
// 文案占位符（ENGAGEMENT §6.1）：engage.js 的 fillText 能解析的全部名字
const PLACEHOLDERS = new Set(['city', 'gov', 'ruler', 'persona', 'capital', 'omen', 'faction', 'subject', 'far', 'farRuler', 'item', 'a', 'b', 'place', 'home', 'plan', 'hidden']);
const ACH_CONDS = new Set(['cityStat999', 'cityLevel', 'cityPop', 'allOrder', 'cityOrder', 'harvestTotal', 'projects', 'wonder', 'reliefs', 'goldTotal', 'caravans', 'caravanFamilies',
  'tradeProfit', 'pacts', 'marriages', 'tributaries', 'relAll', 'truces', 'farRel', 'wenhuiWin', 'grandFestivals', 'sages', 'event', 'itemsOwned', 'rareOwned', 'epicOwned',
  'codexPct', 'item', 'sworn', 'mentors', 'gens', 'level', 'titleTier', 'hiddenFound', 'tourneyWin', 'defenses', 'cities', 'expeditions', 'sitesVisited', 'mythSites', 'clueFound']);
const MISSION_GOALS = new Set(['cityStatGain', 'foodMonths', 'hires', 'drilled', 'minOrder', 'caravans', 'relation', 'project', 'expeditions', 'wenhuiPlace', 'tourneyPlace',
  'items', 'titled', 'levels', 'cityLevel', 'defense', 'conquer']);
const BOND_KINDS = new Set(['sworn', 'kin', 'spouse', 'friend', 'rival', 'feud', 'mentor']);

function load(file) { return JSON.parse(fs.readFileSync(file || JSON_PATH, 'utf8')); }

// 剧本数据（core.js、world-geo.js、data.js、world-data.js 载入到独立的 vm 上下文）
let SCEN = null;
function scenarios() {
  if (SCEN) return SCEN;
  const ctx = { console, Math, JSON }; ctx.window = ctx; vm.createContext(ctx);
  for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), ctx, { filename: f });
  const S = ctx.SG.Scenarios;
  const mk = sd => {
    const cities = sd.Cities.map(l => l.split('|')[0]);
    const links = new Map(cities.map(k => [k, []]));
    for (const l of sd.Links.concat(sd.SeaLinks || [])) { const [a, b] = l.split('-'); if (links.has(a) && links.has(b)) { links.get(a).push(b); links.get(b).push(a); } }
    const gens = sd.Generals.map(l => l.split('|'));
    const owned = new Set();
    for (const p of gens) if (p[4] !== '-') owned.add(p[5]);
    return { sd, cities: new Set(cities), links, gens: new Set(gens.map(p => p[0])), facs: new Set(sd.Factions.map(l => l.split('|')[0])), owned, start: sd.StartYear * 12 + sd.StartMonth };
  };
  SCEN = { classic: mk(S.classic), world: mk(S.world) };
  return SCEN;
}
function hops(sc, from, to, max) {
  if (from === to) return 0;
  const seen = new Set([from]); let q = [from];
  for (let h = 1; h <= max && q.length; h++) {
    const nx = [];
    for (const k of q) for (const j of sc.links.get(k) || []) { if (seen.has(j)) continue; if (j === to) return h; seen.add(j); nx.push(j); }
    q = nx;
  }
  return -1;
}

function check(o) {
  o = o || load();
  const errs = [];
  const err = m => errs.push(m);
  const S = scenarios();
  const W = S.world, CL = S.classic;
  // ---- build-json.js 的检查 ----
  for (const c of CULTURES) if (!o.FAMILY[c]) err('culture without family: ' + c);
  for (const f of FAMILIES) if (!o.FESTIVALS[f]) err('family without festival: ' + f);
  const checkEffects = (list, where) => {
    for (const e of list || []) {
      if (!EFFECT_KEYS.has(e.k)) err(`${where}: unknown effect key ${e.k}`);
      if (e.k === 'buff' && !BUFF_MODS.test(e.mod)) err(`${where}: unknown buff mod ${e.mod}`);
      if (e.k === 'roll3') for (const x of e.outcomes || []) checkEffects(x, where + '/roll3');
    }
  };
  const ids = new Set();
  let modal = 0, toast = 0, opts = 0;
  const checkText = (s, where) => {
    if (typeof s !== 'string') return;
    for (const m of s.matchAll(/\{([a-zA-Z]+)\}/g)) if (!PLACEHOLDERS.has(m[1])) err(`${where}: unknown placeholder {${m[1]}}`);
    if (/[{}]/.test(s.replace(/\{[a-zA-Z]+\}/g, ''))) err(`${where}: stray brace`);
  };
  for (const e of o.EVENTS) {
    if (ids.has(e.id)) err('duplicate event id ' + e.id);
    ids.add(e.id);
    for (const k of Object.keys(e)) if (!EVENT_KEYS.has(k)) err(`event ${e.id}: unknown key ${k}`);
    if (!CATS.has(e.cat)) err(`event ${e.id}: unknown category ${e.cat}`);
    if (!SCOPES.has(e.scope)) err(`event ${e.id}: unknown scope ${e.scope}`);
    if (!e.name || !e.text) err('event without name/text ' + e.id);
    if (!(e.cd > 0)) err('event without cooldown ' + e.id);
    if (!e.icon) err('event without icon ' + e.id);
    if (e.when && e.when.months) for (const m of e.when.months) if (!(m >= 1 && m <= 12)) err('bad month ' + e.id);
    for (const k of Object.keys(e.when || {})) if (!WHEN_KEYS.has(k)) err(`event ${e.id}: unknown condition ${k}`);
    if (e.when && e.when.needPair && !PAIRS.has(e.when.needPair)) err(`event ${e.id}: unknown pair kind ${e.when.needPair}`);
    if (e.scope === 'pair' && !(e.when && e.when.needPair)) err(`event ${e.id}: pair event without needPair`);
    if (e.when && e.when.scenario && !S[e.when.scenario]) err(`event ${e.id}: unknown scenario`);
    checkText(e.text, e.id + '.text');
    for (const [fam, v] of Object.entries(e.variants || {})) {
      if (fam !== 'any' && FAMILIES.indexOf(fam) < 0) err(`event ${e.id}: variant for unknown family ${fam}`);
      checkText(v.text, `${e.id}.variants.${fam}`);
      if (!v.name) err(`event ${e.id}: variant ${fam} without name`);
    }
    checkEffects(e.pre, e.id + '.pre');
    if (e.modal === false) { toast++; if (e.options) err('toast event with options ' + e.id); continue; }
    modal++;
    if (!e.options || e.options.length < 2 || e.options.length > 3) err('modal event needs 2–3 options ' + e.id);
    const optIds = new Set();
    let aiOk = false;
    for (const op of e.options || []) {
      opts++;
      if (optIds.has(op.id)) err('duplicate option ' + e.id + '.' + op.id);
      optIds.add(op.id);
      for (const k of Object.keys(op)) if (!OPTION_KEYS.has(k)) err(`option ${e.id}.${op.id}: unknown key ${k}`);
      if (!op.label) err('option without label ' + e.id + '.' + op.id);
      if (op.agent && !AGENTS.has(op.agent)) err(`option ${e.id}.${op.id}: unknown agent ${op.agent}`);
      for (const k of Object.keys(op.aiIf || {})) if (!AIIF_KEYS.has(k)) err(`option ${e.id}.${op.id}: unknown aiIf ${k}`);
      checkEffects(op.cost, e.id + '.' + op.id + '.cost');
      checkEffects(op.effects, e.id + '.' + op.id);
      checkEffects(op.win, e.id + '.' + op.id + '.win');
      if (op.roll) { checkEffects(op.roll.ok, e.id + '.' + op.id + '.ok'); checkEffects(op.roll.fail, e.id + '.' + op.id + '.fail'); if (!op.roll.p) err(`option ${e.id}.${op.id}: roll without p`); }
      for (const c of op.cost || []) if (!((c.k === 'gold' || c.k === 'food' || c.k === 'training') && (c.add !== undefined || c.inc !== undefined || c.harv !== undefined))) err(`option ${e.id}.${op.id}: cost must be gold / food / training`);
      if (!op.playerOnly && op.ai > 0) aiOk = true;
    }
    if (!aiOk) err('event without an AI-selectable option ' + e.id);
    if (!(e.options || []).some(op => !op.playerOnly && !(op.cost || []).length)) err('event without a free option ' + e.id);
  }
  for (const h of o.HISTORICAL) {
    if (!ids.has(h.event)) err('historical seed with unknown event ' + h.id);
    const sc = S[h.scenario];
    if (!sc) { err('historical seed scenario ' + h.id); continue; }
    for (const k of h.cityKeys) if (!sc.cities.has(k)) err(`historical seed ${h.id}: unknown city ${k}`);
    const [y, m] = String(h.date).split('-').map(Number);
    if (!(m >= 1 && m <= 12) || y * 12 + m < sc.start) err(`historical seed ${h.id}: date ${h.date} before the scenario start`);
  }
  // 高士：城存在（任一剧本）
  for (const [cult, list] of Object.entries(o.SAGES)) for (const p of list) for (const k of p.where || []) if (!W.cities.has(k) && !CL.cities.has(k)) err(`sage ${p.name} (${cult}): unknown city ${k}`);
  if (!o.SAGES.any || !o.SAGES.any.length) err('SAGES.any missing');
  if (!o.OMENS.any || !o.OMENS.any.length) err('OMENS.any missing');
  for (const p of o.PROPOSALS) checkEffects(p.effect, 'proposal ' + p.id);
  // 营建
  const projIds = new Set();
  for (const p of o.PROJECTS) {
    if (projIds.has(p.id)) err('duplicate project ' + p.id);
    projIds.add(p.id);
    if (!p.names.hua && !p.names.any) err('project without hua/any name ' + p.id);
    for (const f of FAMILIES) if (!(p.names[f] || p.names.any || p.names.hua)) err(`project ${p.id}: no name for family ${f}`);
    if (!(p.cost > 0 && p.work > 0)) err('project cost/work ' + p.id);
    if (!p.anchor) err('project without city-view anchor ' + p.id);
  }
  if (o.PROJECTS.length !== 13) err('expected 13 projects, got ' + o.PROJECTS.length);
  // 探索地
  const siteIds = new Set();
  let siteClassic = 0;
  const facCities = sc => { const out = new Set(); for (const l of sc.sd.Generals) { const p = l.split('|'); if (p[4] !== '-') out.add(p[5]); } return out; };
  const startsW = facCities(W), startsC = facCities(CL);
  for (const s of o.SITES) {
    if (siteIds.has(s.id)) err('duplicate site ' + s.id);
    siteIds.add(s.id);
    if (!W.cities.has(s.near)) err(`site ${s.id}: unknown city ${s.near}`);
    if (CL.cities.has(s.near)) siteClassic++;
    if (!o.SITE_TYPES[s.type]) err('site type ' + s.id);
    if (!CULTURES.includes(s.culture)) err('site culture ' + s.id);
    let reach = false;
    for (const st of startsW) if (hops(W, st, s.near, 8) >= 0) { reach = true; break; }
    if (!reach) err(`site ${s.id}: not reachable within 8 links from any faction city (world)`);
    if (CL.cities.has(s.near)) { let rc = false; for (const st of startsC) if (hops(CL, st, s.near, 8) >= 0) { rc = true; break; } if (!rc) err(`site ${s.id}: not reachable in classic`); }
  }
  if (o.SITES.length !== 44) err('expected 44 sites, got ' + o.SITES.length);
  if (siteClassic !== 16) err('expected 16 classic sites, got ' + siteClassic);
  for (const [k, t] of Object.entries(o.SITE_TYPES)) {
    if (!(t.gold && t.gold[0] <= t.gold[1])) err('site type gold ' + k);
    if (!(t.item && t.item.rare > 0 && t.item.rare <= 0.06)) err('site type rare chance ' + k);
    if (!(t.mishap >= 0.04 && t.mishap <= 0.15)) err('site type mishap ' + k);
  }
  // 节庆
  for (const [fam, fe] of Object.entries(o.FESTIVALS)) {
    if (FAMILIES.indexOf(fam) < 0) err('festival for unknown family ' + fam);
    if (!(fe.month >= 1 && fe.month <= 12)) err('festival month ' + fam);
    if (['A', 'B', 'C'].indexOf(fe.grade) < 0) err('festival grade ' + fam);
    for (const [fk, ov] of Object.entries(fe.overrides || {})) {
      if (!W.facs.has(fk) && !CL.facs.has(fk)) err(`festival override for unknown faction ${fk}`);
      if (ov.month !== undefined && !(ov.month >= 1 && ov.month <= 12)) err('festival override month ' + fk);
    }
  }
  if (!o.FESTIVAL_RULE.grand || !o.FESTIVAL_RULE.modest || !o.FESTIVAL_RULE.skip) err('festival rule tiers');
  // 成就
  const achIds = new Set();
  for (const a of o.ACHIEVEMENTS) {
    if (achIds.has(a.id)) err('duplicate achievement ' + a.id);
    achIds.add(a.id);
    if (!(a.prestige > 0)) err('achievement prestige ' + a.id);
    const ck = String(a.cond).split(':')[0];
    if (!ACH_CONDS.has(ck)) err(`achievement ${a.id}: unknown condition ${a.cond}`);
  }
  if (o.ACHIEVEMENTS.length !== 48) err('expected 48 achievements, got ' + o.ACHIEVEMENTS.length);
  // 国策
  const misIds = new Set();
  for (const m of o.MISSIONS) {
    if (misIds.has(m.id)) err('duplicate mission ' + m.id);
    misIds.add(m.id);
    if (m.dl.length !== 3) err('mission deadlines ' + m.id);
    if (!MISSION_GOALS.has(m.goal)) err(`mission ${m.id}: unknown goal ${m.goal}`);
  }
  if (o.MISSIONS.length !== 17) err('expected 17 missions, got ' + o.MISSIONS.length);
  const R = o.MISSION_REWARD;
  for (const k of ['goldK', 'prestige', 'exp', 'item', 'fame']) if (!R[k] || R[k].length !== 3) err('mission reward ' + k);
  // 羁绊与邦交
  let bondClassic = 0, bondWorld = 0;
  for (const [a, b, kind] of o.BONDS) {
    const inC = CL.gens.has(a) && CL.gens.has(b), inW = W.gens.has(a) && W.gens.has(b);
    if (!inC && !inW) err(`bond ${a}-${b}: name not in scenarios`);
    if (inC) bondClassic++;
    if (inW) bondWorld++;
    if (!BOND_KINDS.has(kind)) err('bond kind ' + kind);
  }
  for (const [a, b] of o.RELATIONS) {
    if (!W.facs.has(a) && !CL.facs.has(a)) err('relation faction ' + a);
    if (!b.startsWith('*') && !W.facs.has(b) && !CL.facs.has(b)) err('relation faction ' + b);
  }
  if (o.EVENTS.length !== 33) err('expected 33 events, got ' + o.EVENTS.length);
  const counts = { events: o.EVENTS.length, modal, toast, options: opts, historical: o.HISTORICAL.length, missions: o.MISSIONS.length, projects: o.PROJECTS.length, sites: o.SITES.length,
    sitesClassic: siteClassic, festivals: Object.keys(o.FESTIVALS).length, achievements: o.ACHIEVEMENTS.length, bonds: o.BONDS.length, bondsClassic: bondClassic, bondsWorld: bondWorld, relations: o.RELATIONS.length };
  return { problems: errs, counts };
}

// SG.EngageData（ENGAGEMENT §2 的键名；源 JSON 的大写键改成小写驼峰）
function compact(o) {
  return {
    events: o.EVENTS, historical: o.HISTORICAL, sages: o.SAGES, omens: o.OMENS, proposals: o.PROPOSALS, missions: o.MISSIONS, missionReward: o.MISSION_REWARD,
    projects: o.PROJECTS, sites: o.SITES, siteTypes: o.SITE_TYPES, festivals: o.FESTIVALS, festivalRule: o.FESTIVAL_RULE, achievements: o.ACHIEVEMENTS,
    bonds: o.BONDS, relations: o.RELATIONS, family: o.FAMILY, river: o.RIVER,
  };
}
function hash(d) { return crypto.createHash('md5').update(JSON.stringify(d)).digest('hex').slice(0, 12); }

function writeJS(o, res) {
  const d = compact(o);
  const h = hash(d);
  d.hash = h;
  d.meta = { counts: res.counts, source: 'design-v4/engagement.json', generator: 'tools/gen-engage.js' };
  const c = res.counts;
  const src = `'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 玩法目录（ENGAGEMENT.md）—— 由 tools/gen-engage.js 从 design-v4/engagement.json 生成，请勿手改。
   SG.EngageData：${c.events} 个事件（${c.modal} 个有选项、${c.toast} 个喜报、${c.options} 个选项）· ${c.historical} 条史事 · ${c.missions} 个国策 ·
     ${c.projects} 项营建 · ${c.sites} 处探索地（经典 ${c.sitesClassic}）· ${c.festivals} 个节庆 · ${c.achievements} 项功业 · ${c.bonds} 对羁绊 · ${c.relations} 对邦交 · hash ${h}
   载入：world-data.js 之后、econ.js / model.js 之前（INTEGRATION §11）。规则在 js/engage.js（SG.Engage）。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  SG.EngageData = ${JSON.stringify(d)};
})();
`;
  fs.writeFileSync(OUT_JS, src);
  return { file: OUT_JS, bytes: Buffer.byteLength(src), hash: h };
}

function writeCS(o, res, out) {
  const d = compact(o);
  const h = hash(d);
  const json = JSON.stringify(d).replace(/"/g, '""');
  const c = res.counts;
  const src = `// <auto-generated> tools/gen-engage.js cs — from design-v4/engagement.json (ENGAGEMENT.md). Do not edit. </auto-generated>
// ${c.events} events, ${c.missions} missions, ${c.projects} projects, ${c.sites} sites, ${c.festivals} festivals, ${c.achievements} achievements, ${c.bonds} bonds, ${c.relations} relations; hash ${h}
namespace Sanguo.Data
{
    public static class EngageData
    {
        public const string Hash = "${h}";
        public const int Events = ${c.events}, Missions = ${c.missions}, Projects = ${c.projects}, Sites = ${c.sites}, Festivals = ${c.festivals}, Achievements = ${c.achievements}, Bonds = ${c.bonds}, Relations = ${c.relations};
        // The catalogue (the same keys as js/engage-data.js SG.EngageData); Engage.cs parses it once (JsonUtility / MiniJSON).
        public const string Json = @"${json}";
    }
}
`;
  out = out || OUT_CS;
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, src);
  return { file: out, bytes: Buffer.byteLength(src), hash: h };
}

module.exports = { load, check, compact, hash, FAMILIES, CULTURES, PLACEHOLDERS, EFFECT_KEYS, JSON_PATH, OUT_JS };

if (require.main === module) {
  const mode = process.argv[2] || 'js';
  const o = load();
  const res = check(o);
  console.log(JSON.stringify(res.counts));
  if (res.problems.length) { console.log(res.problems.length + ' PROBLEMS'); for (const p of res.problems) console.log('  ' + p); }
  else console.log('ALL OK');
  if (!res.problems.length && mode === 'js') { const w = writeJS(o, res); console.log(`wrote ${path.relative(ROOT, w.file)} (${w.bytes} bytes, hash ${w.hash})`); }
  if (!res.problems.length && mode === 'cs') { const w = writeCS(o, res, process.argv[3]); console.log(`wrote ${w.file} (${w.bytes} bytes, hash ${w.hash})`); }
  process.exit(res.problems.length);
}
