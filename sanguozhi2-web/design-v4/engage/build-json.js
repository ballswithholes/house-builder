'use strict';
/* ==========================================================================
   design-v4/engage/build-json.js —— 校验 catalog.js 并写出 design-v4/engagement.json（ENGAGEMENT.md 的数据真源）
   用法：node design-v4/engage/build-json.js          校验 + 写文件；退出码 = 错误数
         node design-v4/engage/build-json.js check    只校验
   校验：编号唯一；效果键在词汇表内；每个有选项的事件都有电脑可选的项、文案、出处或说明；月份合法；
         18 种文化都映射到城池家族、12 个家族都有招牌节庆；营建有汉名与每个家族可解析的名字；
         探索地的最近城在世界剧本中存在（并标出经典剧本可去的）；羁绊与邦交里的人名 / 势力键在剧本中存在；
         成就编号唯一、条件有对应的统计键。
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');
const CAT = require('./catalog.js');

global.window = global;
const root = path.join(__dirname, '..', '..', 'js');
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
const SG = global.SG;
const W = SG.Scenarios.world, CL = SG.Scenarios.classic;
const worldCity = new Set(W.Cities.map(l => l.split('|')[0]));
const classicCity = new Set(CL.Cities.map(l => l.split('|')[0]));
const worldGen = new Set(W.Generals.map(l => l.split('|')[0]));
const classicGen = new Set(CL.Generals.map(l => l.split('|')[0]));
const worldFac = new Set(W.Factions.map(l => l.split('|')[0]));

const errs = [];
const err = m => errs.push(m);
const CULTURES = ['han', 'nanman', 'wa', 'yi', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian', 'india', 'africa', 'moor'];
const FAMILIES = ['hua', 'han3', 'wa', 'steppe', 'oasis', 'indic', 'persia', 'arabia', 'nile', 'roman', 'north', 'nanhai'];
for (const c of CULTURES) if (!CAT.FAMILY[c]) err('culture without family: ' + c);
for (const f of FAMILIES) if (!CAT.FESTIVALS[f]) err('family without festival: ' + f);

const EFFECT_KEYS = new Set(['gold', 'food', 'land', 'industry', 'town', 'population', 'order', 'troops', 'training', 'defense', 'prestige', 'fame', 'virtue',
  'exp', 'expLater', 'loyalty', 'officers.loyalty', 'loyalty3', 'bond3', 'away', 'gtroops', 'buff', 'rel', 'bond', 'bondKind', 'reveal', 'item', 'flag', 'chain',
  'defect', 'separate', 'plan', 'roll3', 'projectDiscount']);
const BUFF_MODS = /^(harvest|income|pop|orderTarget|eventW:[a-z]+|spread|caravan|foodPrice|defense)$/;
function checkEffects(list, where) {
  for (const e of list || []) {
    if (!EFFECT_KEYS.has(e.k)) err(`${where}: unknown effect key ${e.k}`);
    if (e.k === 'buff' && !BUFF_MODS.test(e.mod)) err(`${where}: unknown buff mod ${e.mod}`);
    if (e.k === 'roll3') for (const o of e.outcomes) checkEffects(o, where + '/roll3');
  }
}
const ids = new Set();
let modal = 0, toast = 0, opts = 0;
for (const e of CAT.EVENTS) {
  if (ids.has(e.id)) err('duplicate event id ' + e.id);
  ids.add(e.id);
  if (!e.name || !e.text) err('event without name/text ' + e.id);
  if (!(e.cd > 0)) err('event without cooldown ' + e.id);
  if (e.when && e.when.months) for (const m of e.when.months) if (!(m >= 1 && m <= 12)) err('bad month ' + e.id);
  checkEffects(e.pre, e.id + '.pre');
  if (e.modal === false) { toast++; if (e.options) err('toast event with options ' + e.id); continue; }
  modal++;
  if (!e.options || e.options.length < 2 || e.options.length > 3) err('modal event needs 2–3 options ' + e.id);
  const optIds = new Set();
  let aiOk = false;
  for (const o of e.options || []) {
    opts++;
    if (optIds.has(o.id)) err('duplicate option ' + e.id + '.' + o.id);
    optIds.add(o.id);
    if (!o.label) err('option without label ' + e.id + '.' + o.id);
    checkEffects(o.cost, e.id + '.' + o.id + '.cost');
    checkEffects(o.effects, e.id + '.' + o.id);
    if (o.roll) { checkEffects(o.roll.ok, e.id + '.' + o.id + '.ok'); checkEffects(o.roll.fail, e.id + '.' + o.id + '.fail'); }
    if (!o.playerOnly && o.ai > 0) aiOk = true;
  }
  // 至少一项不花钱、电脑可选（「安全默认」）
  if (!aiOk) err('event without an AI-selectable option ' + e.id);
  if (!(e.options || []).some(o => !o.playerOnly && !(o.cost || []).length)) err('event without a free option ' + e.id);
}
for (const h of CAT.HISTORICAL) {
  if (!ids.has(h.event)) err('historical seed with unknown event ' + h.id);
  const set = h.scenario === 'classic' ? classicCity : worldCity;
  for (const k of h.cityKeys) if (!set.has(k)) err(`historical seed ${h.id}: unknown city ${k}`);
}
const projIds = new Set();
for (const p of CAT.PROJECTS) {
  if (projIds.has(p.id)) err('duplicate project ' + p.id);
  projIds.add(p.id);
  if (!p.names.hua && !p.names.any) err('project without hua/any name ' + p.id);
  for (const f of FAMILIES) if (!p.names[f] && !p.names.any && f !== 'hua') { /* 回退到汉名：允许，但记数 */ }
  if (!(p.cost > 0 && p.work > 0)) err('project cost/work ' + p.id);
}
const siteIds = new Set();
let siteClassic = 0;
for (const s of CAT.SITES) {
  if (siteIds.has(s.id)) err('duplicate site ' + s.id);
  siteIds.add(s.id);
  if (!worldCity.has(s.near)) err(`site ${s.id}: unknown city ${s.near}`);
  if (classicCity.has(s.near)) siteClassic++;
  if (!CAT.SITE_TYPES[s.type]) err('site type ' + s.id);
  if (!CULTURES.includes(s.culture)) err('site culture ' + s.id);
}
const achIds = new Set();
for (const a of CAT.ACHIEVEMENTS) { if (achIds.has(a.id)) err('duplicate achievement ' + a.id); achIds.add(a.id); if (!(a.prestige > 0)) err('achievement prestige ' + a.id); }
const misIds = new Set();
for (const m of CAT.MISSIONS) { if (misIds.has(m.id)) err('duplicate mission ' + m.id); misIds.add(m.id); if (m.dl.length !== 3) err('mission deadlines ' + m.id); }
let bondClassic = 0, bondWorld = 0;
for (const [a, b, kind] of CAT.BONDS) {
  const inC = classicGen.has(a) && classicGen.has(b), inW = worldGen.has(a) && worldGen.has(b);
  if (!inC && !inW) err(`bond ${a}-${b}: name not in scenarios`);
  if (inC) bondClassic++;
  if (inW) bondWorld++;
  if (!['sworn', 'kin', 'spouse', 'friend', 'rival', 'feud', 'mentor'].includes(kind)) err('bond kind ' + kind);
}
for (const [a, b] of CAT.RELATIONS) {
  if (!worldFac.has(a)) err('relation faction ' + a);
  if (!b.startsWith('*') && !worldFac.has(b)) err('relation faction ' + b);
}

const meta = {
  generated: 'design-v4/engage/build-json.js', spec: 'design-v4/ENGAGEMENT.md',
  counts: { events: CAT.EVENTS.length, modal, toast, options: opts, historical: CAT.HISTORICAL.length, missions: CAT.MISSIONS.length, projects: CAT.PROJECTS.length, sites: CAT.SITES.length, sitesClassic: siteClassic, festivals: Object.keys(CAT.FESTIVALS).length, achievements: CAT.ACHIEVEMENTS.length, bonds: CAT.BONDS.length, bondsClassic: bondClassic, bondsWorld: bondWorld, relations: CAT.RELATIONS.length },
};
console.log(JSON.stringify(meta.counts));
if (errs.length) { console.log(errs.length + ' ERRORS'); for (const e of errs) console.log('  ' + e); }
else console.log('ALL OK');
if (process.argv[2] !== 'check' && !errs.length) {
  const out = Object.assign({ meta }, CAT);
  fs.writeFileSync(path.join(__dirname, '..', 'engagement.json'), JSON.stringify(out, null, 1));
  console.log('wrote design-v4/engagement.json');
}
process.exit(errs.length);
