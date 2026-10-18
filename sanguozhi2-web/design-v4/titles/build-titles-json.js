'use strict';
/* ==========================================================================
   design-v4/titles/build-titles-json.js —— 生成 design-v4/titles.json（官爵目录）
   只读设计数据，不碰游戏代码。用法：node design-v4/titles/build-titles-json.js
   结构说明见 design-v4/TITLES.md §3。本文件的表是唯一来源：改数值或译名后重新生成 titles.json。
   数值（effects）只在 posts 上定义一次；各文化（cultures）只提供名称、原文、出处与印信样式，
   永远不改数值（tests/titles.js 断言：任何文化的同一 post 数值完全相同）。
   ========================================================================== */
const fs = require('fs');
const path = require('path');

// ------------------------------------------------------------------ 品阶 --
// reqStat：家族属性须「大于」此值（玩家原话 > 80 / > 90），0 = 无要求。
// grantGold：授官礼金（一次性，见 TITLES.md §6.3；不设月俸——titles-proto.js 实测月俸会掏空电脑城金、触发「金不足 → 全城忠诚 −3」并加速兼并）。
// effLoy：出奔判定时按 忠诚 + effLoy 计；persuadeRes / discordRes：被拉拢成功率、被离间降幅的减免比例。
const TIERS = [
  { tier: 1, grade: '九品', reqLevel: 1, reqStat: 0, loyGrant: 5, grantGold: 50, effLoy: 4, persuadeRes: 0.10, discordRes: 0.08, revokeLoy: 10, ceremony: 'small' },
  { tier: 2, grade: '七品', reqLevel: 2, reqStat: 0, loyGrant: 8, grantGold: 100, effLoy: 8, persuadeRes: 0.20, discordRes: 0.16, revokeLoy: 14, ceremony: 'small' },
  { tier: 3, grade: '五品', reqLevel: 3, reqStat: 80, loyGrant: 12, grantGold: 200, effLoy: 12, persuadeRes: 0.30, discordRes: 0.24, revokeLoy: 18, ceremony: 'medium' },
  { tier: 4, grade: '三品', reqLevel: 5, reqStat: 80, loyGrant: 16, grantGold: 300, effLoy: 16, persuadeRes: 0.40, discordRes: 0.32, revokeLoy: 22, ceremony: 'medium' },
  { tier: 5, grade: '一品', reqLevel: 7, reqStat: 90, loyGrant: 20, grantGold: 500, effLoy: 20, persuadeRes: 0.50, discordRes: 0.40, revokeLoy: 26, ceremony: 'grand' },
];

// ------------------------------------------------------------ 君主位阶 --
// 国势 P = Σ 己方城 (1 + (土地 + 产业 + 町) / 1500)；P ≥ thresholds[k] → 位阶 R(k+1)。
// 降阶有回差：P < thresholds[k] × hysteresis 才降。caps[R] = 各品阶可授总数（三家共用）。
const RULER_RANKS = {
  power: { perCity: 1, devDiv: 1500 },
  thresholds: [0, 3, 6, 10, 16],
  hysteresis: 0.9,
  caps: { 1: [2, 1, 0, 0, 0], 2: [3, 2, 1, 0, 0], 3: [4, 3, 2, 1, 0], 4: [5, 4, 3, 2, 1], 5: [6, 5, 4, 3, 3] },
};

// ------------------------------------------------------------ 效果键 --
// unit：pct = 小数倍率（0.08 = +8%）；pp = 成功率加点（0.03 = +3 个百分点）；flat = 整数。
// scope：self = 持有者本人（部队 / 执行的指令）；side = 所在一方全军（取一方最大值，不叠加）；
//        city = 持有者主政之城（太守，或君主坐镇时由该城品阶最高的文官代理）；nation = 全势力（取最大值，不叠加）。
const EFFECTS = {
  atk:      { unit: 'pct', scope: 'self', where: 'battle-model.js atkPower', zh: '攻击' },
  def:      { unit: 'pct', scope: 'self', where: 'battle-model.js defFactor', zh: '防御' },
  morale:   { unit: 'flat', scope: 'self', where: 'battle-model.js 开战士气', zh: '开战士气' },
  cmdAtk:   { unit: 'pct', scope: 'side', where: 'atkPower（持有者为主将时，全军）', zh: '主将统率·攻' },
  cmdDef:   { unit: 'pct', scope: 'side', where: 'defFactor（持有者为主将时，全军）', zh: '主将统率·防' },
  critArmy: { unit: 'pp', scope: 'self', where: 'COMBAT-V4 §2 titleCrit(a)（与装备合计 ≤ 6%）', zh: '会心率（部队）' },
  garrison: { unit: 'flat', scope: 'city', where: 'SG.cityDefense（持有者驻守之城，取城中武官最大值）', zh: '镇守城防' },
  train:    { unit: 'flat', scope: 'self', where: 'Commands.train', zh: '训练增幅' },
  critDuel: { unit: 'pp', scope: 'self', where: 'COMBAT-V4 §3 title.critDuel（与装备合计 ≤ 8%）', zh: '会心率（单挑）' },
  dmgDuel:  { unit: 'pct', scope: 'self', where: 'COMBAT-V4 Q3 duelMods.damage（与装备合计 ≤ 6%）', zh: '单挑伤害' },
  tac:      { unit: 'pct', scope: 'self', where: 'battle-model.js tacK（施计者一侧）', zh: '计策威力' },
  tacHit:   { unit: 'pp', scope: 'self', where: 'useTactic1 / tacticChance', zh: '计策成功率' },
  tacRes:   { unit: 'pp', scope: 'side', where: 'useTactic1 / tacticChance（敌计策对本方任一部队）', zh: '识破（全军）' },
  search:   { unit: 'pp', scope: 'self', where: 'Commands.search', zh: '搜索' },
  hire:     { unit: 'pp', scope: 'self', where: 'Commands.hireChance（登用者）', zh: '登用' },
  dip:      { unit: 'pp', scope: 'self', where: 'Commands.allyChance（使者）', zh: '同盟' },
  persuade: { unit: 'pp', scope: 'self', where: 'Commands.persuadeChance（执行者）', zh: '拉拢' },
  discord:  { unit: 'pp', scope: 'self', where: 'Commands.discord（执行者）', zh: '离间' },
  dev:      { unit: 'pct', scope: 'self', where: 'Commands.develop（主持者）', zh: '开发' },
  income:   { unit: 'pct', scope: 'city', where: 'StrategyAI.endMonth 金收入', zh: '本城金收入' },
  harvest:  { unit: 'pct', scope: 'city', where: 'StrategyAI.endMonth 秋收', zh: '本城秋收' },
  recruit:  { unit: 'pct', scope: 'city', where: 'Commands.recruit 每金得兵', zh: '本城征兵' },
  pop:      { unit: 'pct', scope: 'city', where: 'StrategyAI.endMonth 人口增长', zh: '本城人口增长' },
  loyAura:  { unit: 'flat', scope: 'city', where: 'StrategyAI.endMonth 每月忠诚（上限 95，与 ITEMS 的 loyal 同规则）', zh: '本城武将忠诚/月' },
  natIncome:  { unit: 'pct', scope: 'nation', where: 'StrategyAI.endMonth 金收入（全国）', zh: '全国金收入' },
  natHarvest: { unit: 'pct', scope: 'nation', where: 'StrategyAI.endMonth 秋收（全国）', zh: '全国秋收' },
};

// ------------------------------------------------------------ 官职（数值） --
// seats：每势力该职位最多几人（0 = 只受品阶总数限制）。branch：A/B 两种专长（三、四品）；一、二、五品为通才。
const POSTS = [
  // 武官（武力最高）
  { id: 'w1', family: 'war', tier: 1, branch: null, seats: 0, role: '统兵', fx: { atk: 0.03, def: 0.03, garrison: 2, train: 1 } },
  { id: 'w2', family: 'war', tier: 2, branch: null, seats: 0, role: '统兵', fx: { atk: 0.04, def: 0.04, garrison: 4, train: 2, morale: 3 } },
  { id: 'w3a', family: 'war', tier: 3, branch: 'A', seats: 2, role: '统帅', fx: { atk: 0.07, def: 0.07, garrison: 6, train: 3, morale: 4, critArmy: 0.01 } },
  { id: 'w3b', family: 'war', tier: 3, branch: 'B', seats: 2, role: '武勇', fx: { atk: 0.08, def: 0.04, garrison: 4, train: 2, morale: 10, critArmy: 0.01, critDuel: 0.02, dmgDuel: 0.02 } },
  { id: 'w4a', family: 'war', tier: 4, branch: 'A', seats: 2, role: '统帅', fx: { atk: 0.08, def: 0.08, garrison: 8, train: 4, morale: 5, critArmy: 0.01, cmdAtk: 0.02, cmdDef: 0.02 } },
  { id: 'w4b', family: 'war', tier: 4, branch: 'B', seats: 2, role: '武勇', fx: { atk: 0.11, def: 0.06, garrison: 6, train: 3, morale: 12, critArmy: 0.02, critDuel: 0.03, dmgDuel: 0.02, cmdAtk: 0.01 } },
  { id: 'w5', family: 'war', tier: 5, branch: null, seats: 1, role: '元戎', fx: { atk: 0.12, def: 0.12, garrison: 10, train: 5, morale: 10, critArmy: 0.02, critDuel: 0.03, dmgDuel: 0.02, cmdAtk: 0.03, cmdDef: 0.03 } },
  // 谋臣（智力最高）
  { id: 'i1', family: 'intel', tier: 1, branch: null, seats: 0, role: '参谋', fx: { tac: 0.04, tacHit: 0.01, search: 0.03, hire: 0.02, dip: 0.02 } },
  { id: 'i2', family: 'intel', tier: 2, branch: null, seats: 0, role: '参谋', fx: { tac: 0.06, tacHit: 0.02, search: 0.04, hire: 0.03, dip: 0.03, persuade: 0.02, discord: 0.02 } },
  { id: 'i3a', family: 'intel', tier: 3, branch: 'A', seats: 2, role: '军略', fx: { tac: 0.13, tacHit: 0.04, tacRes: 0.03, search: 0.04, hire: 0.03, dip: 0.02 } },
  { id: 'i3b', family: 'intel', tier: 3, branch: 'B', seats: 2, role: '外交', fx: { tac: 0.05, tacHit: 0.01, search: 0.05, hire: 0.06, dip: 0.08, persuade: 0.06, discord: 0.05 } },
  { id: 'i4a', family: 'intel', tier: 4, branch: 'A', seats: 2, role: '军略', fx: { tac: 0.17, tacHit: 0.05, tacRes: 0.05, search: 0.05, hire: 0.04, dip: 0.03, persuade: 0.02 } },
  { id: 'i4b', family: 'intel', tier: 4, branch: 'B', seats: 2, role: '外交', fx: { tac: 0.06, tacHit: 0.03, search: 0.06, hire: 0.10, dip: 0.14, persuade: 0.10, discord: 0.07 } },
  { id: 'i5', family: 'intel', tier: 5, branch: null, seats: 1, role: '帷幄', fx: { tac: 0.22, tacHit: 0.06, tacRes: 0.06, search: 0.06, hire: 0.08, dip: 0.10, persuade: 0.07, discord: 0.06 } },
  // 文官（政治最高）
  { id: 'p1', family: 'pol', tier: 1, branch: null, seats: 0, role: '理事', fx: { dev: 0.05, income: 0.03, harvest: 0.03 } },
  { id: 'p2', family: 'pol', tier: 2, branch: null, seats: 0, role: '理事', fx: { dev: 0.08, income: 0.04, harvest: 0.05, recruit: 0.02, pop: 0.03 } },
  { id: 'p3a', family: 'pol', tier: 3, branch: 'A', seats: 2, role: '民政', fx: { dev: 0.12, income: 0.06, harvest: 0.06, recruit: 0.04, pop: 0.08, loyAura: 1 } },
  { id: 'p3b', family: 'pol', tier: 3, branch: 'B', seats: 2, role: '财政', fx: { dev: 0.06, income: 0.09, harvest: 0.10, recruit: 0.08 } },
  { id: 'p4a', family: 'pol', tier: 4, branch: 'A', seats: 2, role: '民政', fx: { dev: 0.18, income: 0.08, harvest: 0.08, recruit: 0.05, pop: 0.15, loyAura: 1, natIncome: 0.01 } },
  { id: 'p4b', family: 'pol', tier: 4, branch: 'B', seats: 2, role: '财政', fx: { dev: 0.08, income: 0.12, harvest: 0.12, recruit: 0.08, natIncome: 0.015, natHarvest: 0.01 } },
  { id: 'p5', family: 'pol', tier: 5, branch: null, seats: 1, role: '宰辅', fx: { dev: 0.22, income: 0.10, harvest: 0.10, recruit: 0.08, pop: 0.15, loyAura: 2, natIncome: 0.025, natHarvest: 0.02 } },
];

// 价值权重（TITLES.md §7.8）：把各效果折算成「一个点 ≈ 持有者本职效率 +1%」，用于检查三家同品阶大致等值。
// 同品阶各职位的价值须在本品阶均值的 ±20% 内；品阶均值依次约为 7 / 12 / 20 / 32 / 53。
const VALUE_WEIGHTS = { atk: 100, def: 100, cmdAtk: 300, cmdDef: 300, garrison: 0.6, morale: 0.2, critArmy: 50, critDuel: 30, dmgDuel: 60, train: 0.3,
  tac: 60, tacHit: 100, tacRes: 200, search: 30, hire: 50, dip: 60, persuade: 60, discord: 30,
  dev: 60, income: 100, harvest: 60, recruit: 40, pop: 10, loyAura: 3, natIncome: 400, natHarvest: 250 };
const VALUE_BAND = 0.20;

// 各文化的名称表在 cultures.js（同目录）里；本文件只做组装与自检。
const C = require('./cultures.js');

function build() {
  const out = {
    schema: 'sanguozhi2.titles/1',
    version: 1,
    generated: 'design-v4/titles/build-titles-json.js',
    doc: 'design-v4/TITLES.md',
    families: {
      war: { zh: '武官', stat: 'war', statZh: '武力', icon: 'family-war', color: '#c8382c', desc: '统兵、操练、阵战与单挑' },
      intel: { zh: '谋臣', stat: 'intel', statZh: '智力', icon: 'family-intel', color: '#2f6db5', desc: '计策、识破、外交、搜索与登用' },
      pol: { zh: '文官', stat: 'pol', statZh: '政治', icon: 'family-pol', color: '#2f9a55', desc: '开发、税赋、秋收、征兵与安民' },
    },
    tiers: TIERS,
    rulerRanks: RULER_RANKS,
    effects: EFFECTS,
    posts: POSTS,
    valueWeights: VALUE_WEIGHTS,
    valueBand: VALUE_BAND,
    cultures: C.CULTURES,
    factionOverrides: C.FACTION_OVERRIDES,
    cultureFallback: C.FALLBACK,
    sources: C.SOURCES,
  };
  return out;
}

function check(o) {
  const problems = [];
  const ids = o.posts.map(p => p.id);
  for (const p of o.posts) for (const k of Object.keys(p.fx)) if (!o.effects[k]) problems.push(p.id + ' unknown effect ' + k);
  // 同品阶等值
  const val = p => Object.entries(p.fx).reduce((v, [k, x]) => v + (o.valueWeights[k] || 0) * x, 0);
  let prevMean = 0;
  for (let t = 1; t <= 5; t++) {
    const ps = o.posts.filter(p => p.tier === t), vs = ps.map(val), mean = vs.reduce((a, b) => a + b, 0) / vs.length;
    ps.forEach((p, i) => { if (Math.abs(vs[i] / mean - 1) > o.valueBand) problems.push(`value of ${p.id} ${vs[i].toFixed(1)} outside ±${o.valueBand * 100}% of tier ${t} mean ${mean.toFixed(1)}`); });
    if (!(mean > prevMean * 1.4)) problems.push(`tier ${t} mean value ${mean.toFixed(1)} should exceed 1.4 × tier ${t - 1} (${prevMean.toFixed(1)})`);
    prevMean = mean;
  }
  // 三家各品阶都有职位，五品（tier 5）每家恰一个、一席
  for (const fam of ['war', 'intel', 'pol']) for (let t = 1; t <= 5; t++) if (!o.posts.some(p => p.family === fam && p.tier === t)) problems.push(fam + ' lacks tier ' + t);
  for (const p of o.posts.filter(q => q.tier === 5)) if (p.seats !== 1) problems.push(p.id + ' must have exactly 1 seat');
  for (const [code, cu] of Object.entries(o.cultures)) {
    if (!cu.rulerRanks || cu.rulerRanks.length !== 5) problems.push(code + ' rulerRanks must have 5 names');
    for (const id of ids) {
      const e = cu.posts[id];
      if (!e) { if (!cu.partial) problems.push(code + ' lacks ' + id); continue; }
      if (!Array.isArray(e.names) || !e.names.length) problems.push(code + '.' + id + ' has no names');
      for (const n of e.names || []) {
        if (!n.zh || n.zh.length > 7) problems.push(code + '.' + id + ' bad zh ' + JSON.stringify(n.zh));
        if (!/^[ABC]$/.test(n.attest || '')) problems.push(code + '.' + id + ' ' + n.zh + ' attest must be A/B/C');
        for (const s of n.src || []) if (!o.sources[s]) problems.push(code + '.' + id + ' ' + n.zh + ' unknown source ' + s);
        if (n.attest !== 'C' && !(n.src && n.src.length)) problems.push(code + '.' + id + ' ' + n.zh + ' attest ' + n.attest + ' needs a source');
      }
    }
    // 同一文化内名称不得重复（不同职位）
    const seen = new Map();
    for (const id of ids) for (const n of (cu.posts[id] || { names: [] }).names) {
      if (seen.has(n.zh) && seen.get(n.zh) !== id) problems.push(code + ' duplicate name ' + n.zh + ' (' + seen.get(n.zh) + ', ' + id + ')');
      seen.set(n.zh, id);
    }
  }
  for (const [fk, ov] of Object.entries(o.factionOverrides)) for (const id of Object.keys(ov.posts || {})) if (ids.indexOf(id) < 0) problems.push('override ' + fk + ' unknown post ' + id);
  // 每个势力解析后的 21 个默认名互不相同，且不与本势力的君主位阶称号重名
  const W = loadWorld();
  if (W) {
    for (const line of W.Factions) {
      const f = line.split('|'), key = f[0], cult = f[6] || 'han';
      const names = ids.map(id => resolve(o, key, cult, id).names[0].zh);
      const dup = names.filter((z, i) => names.indexOf(z) !== i);
      if (dup.length) problems.push('faction ' + key + ' (' + cult + ') resolves duplicate names ' + dup.join(','));
      const rr = rulerRanks(o, key, cult);
      const clash = names.filter(z => rr.indexOf(z) >= 0);
      if (clash.length) problems.push('faction ' + key + ' post names clash with ruler ranks: ' + clash.join(','));
    }
  }
  return problems;
}

// 名称解析：势力覆盖 → 文化 → 回退文化 → generic（与 js/titles.js 的 nameSet 相同）
function resolve(o, factionKey, culture, id) {
  const ov = o.factionOverrides[factionKey];
  if (ov && ov.posts && ov.posts[id]) return ov.posts[id];
  for (let c = culture, guard = 0; c && guard < 4; guard++) {
    const cu = o.cultures[c];
    if (cu && cu.posts[id]) return cu.posts[id];
    c = o.cultureFallback[c] || (c === 'generic' ? null : 'generic');
  }
  return o.cultures.generic.posts[id];
}
function rulerRanks(o, factionKey, culture) {
  const ov = o.factionOverrides[factionKey];
  if (ov && ov.rulerRanks) return ov.rulerRanks;
  return (o.cultures[culture] || o.cultures.generic).rulerRanks;
}
function loadWorld() {
  try {
    const vm = require('vm');
    const root = path.join(__dirname, '..', '..', 'js');
    const ctx = { window: {} }; ctx.window.SG = ctx.SG = {}; ctx.window.window = ctx.window;
    vm.createContext(ctx);
    for (const f of ['core.js', 'data.js', 'world-data.js']) vm.runInContext(fs.readFileSync(path.join(root, f), 'utf8'), ctx, { filename: f });
    return ctx.window.SG.Scenarios.world;
  } catch (e) { console.log('(world data not loaded: ' + e.message + ')'); return null; }
}

if (require.main === module) {
  const o = build();
  const problems = check(o);
  const file = path.join(__dirname, '..', 'titles.json');
  fs.writeFileSync(file, JSON.stringify(o, null, 1) + '\n');
  const nNames = Object.values(o.cultures).reduce((s, cu) => s + Object.values(cu.posts).reduce((t, e) => t + e.names.length, 0), 0);
  const att = { A: 0, B: 0, C: 0 };
  for (const cu of Object.values(o.cultures)) for (const e of Object.values(cu.posts)) for (const n of e.names) att[n.attest]++;
  console.log(`titles.json: ${o.posts.length} posts, ${Object.keys(o.cultures).length} culture sets, ${nNames} names (attested A ${att.A}, B ${att.B}, C ${att.C}), ${Object.keys(o.sources).length} sources, ${Object.keys(o.factionOverrides).length} faction overrides`);
  if (problems.length) { console.log('PROBLEMS:\n  ' + problems.join('\n  ')); process.exitCode = 1; }
  else console.log('self-check OK');
}
module.exports = { build, check, resolve, rulerRanks, TIERS, RULER_RANKS, EFFECTS, POSTS };
