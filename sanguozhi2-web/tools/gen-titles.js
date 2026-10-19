'use strict';
/* ==========================================================================
   tools/gen-titles.js — 第四版官爵目录的生成与校验（TITLES.md §3、§10.1、§12.2 第 1 项）
   design-v4/titles.json 是唯一的数据源（21 个官位、18 种文化的官名、11 个势力覆盖、品阶、位阶、效果词典、价值权重、出处）；
   它由设计期的 design-v4/titles/build-titles-json.js（名称表在 design-v4/titles/cultures.js）生成。本工具把它变成游戏读的
   js/titles-data.js，并做 CI 校验。

   用法：
     node tools/gen-titles.js [js]      校验 titles.json，写 js/titles-data.js（SG.TitlesData；缺省模式）
     node tools/gen-titles.js check     只校验（tests/titles.js §1 的全部目录检查；退出码 = 问题数）
     node tools/gen-titles.js build     先用 design-v4/titles/build-titles-json.js 重建 titles.json，再写 js
   作为模块：require('./tools/gen-titles.js') → { load, check, compact, resolveName, rulerRanks, valueOf, factionCultures }

   js/titles-data.js 的内容：SG.TitlesData = titles.json 去掉 generated / doc 两个说明字段，另加
     hash（数据的 md5 前 12 位，存档 / Unity 对照用）与 meta（计数）。名称的原文、出处、注释都保留（官爵详情页要显示）。

   校验（check）：
     1 design-v4/titles/build-titles-json.js 的 check()：效果键、出处、A / B 级须有出处、价值等值（±20 %）、品阶均值递增 ×1.4、
       每个一品官位一席、世界剧本 70 个势力解析后 21 个默认名互不相同且不与本势力的位阶称号重名
     2 本工具另查：品阶表单调（等级、礼金、忠诚、防叛、抗性）、位阶门槛递增、名额表（三、四品官位各 2 席，一品 1 席；
       R5 的一品名额 = 一品官位数）、每个文化的回退链在 4 步内终止、两个剧本（经典带 / 不带世界数据、世界）的每个势力
       都能解析全部 21 个官位与 5 个位阶称号、经典剧本 18 个势力的默认名互不相同、名称长度 ≤ 7、印信 5 个
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const crypto = require('crypto');

const ROOT = path.join(__dirname, '..');
const JSON_PATH = path.join(ROOT, 'design-v4', 'titles.json');
const BUILDER = path.join(ROOT, 'design-v4', 'titles', 'build-titles-json.js');
const OUT_JS = path.join(ROOT, 'js', 'titles-data.js');

function load(file) { return JSON.parse(fs.readFileSync(file || JSON_PATH, 'utf8')); }

// 名称解析（TITLES §3.2；与 js/titles.js 的 nameSet 相同）：势力覆盖 → 文化 → 回退文化（≤ 4 步）→ generic
function resolveName(o, factionKey, culture, postId) {
  const ov = o.factionOverrides[factionKey];
  if (ov && ov.posts && ov.posts[postId]) return ov.posts[postId];
  let c = culture;
  for (let guard = 0; c && guard < 4; guard++) {
    const cu = o.cultures[c];
    if (cu && cu.posts && cu.posts[postId]) return cu.posts[postId];
    c = o.cultureFallback[c] || (c === 'generic' ? null : o.cultureFallback['*'] || 'generic');
  }
  return o.cultures.generic.posts[postId];
}
function rulerRanks(o, factionKey, culture) {
  const ov = o.factionOverrides[factionKey];
  if (ov && ov.rulerRanks) return ov.rulerRanks;
  let c = culture;
  for (let guard = 0; c && guard < 4; guard++) {
    const cu = o.cultures[c];
    if (cu && cu.rulerRanks) return cu.rulerRanks;
    c = o.cultureFallback[c] || (c === 'generic' ? null : o.cultureFallback['*'] || 'generic');
  }
  return o.cultures.generic.rulerRanks;
}
const valueOf = (o, p) => Object.entries(p.fx).reduce((v, [k, x]) => v + (o.valueWeights[k] || 0) * x, 0);

// 两个剧本的势力文化：照 GameState.init 的规则（剧本列，或 SG.WorldData.cultureOfFaction；经典剧本不带世界数据时一律 han）
function factionCultures() {
  const ctx = { console: { log() {}, warn() {}, error: console.error, info() {} }, Math, JSON, Date };
  ctx.window = ctx; ctx.global = ctx;
  vm.createContext(ctx);
  for (const f of ['core.js', 'registry.js', 'world-geo.js', 'data.js', 'world-data.js', 'model.js']) {
    const p = path.join(ROOT, 'js', f);
    if (fs.existsSync(p)) vm.runInContext(fs.readFileSync(p, 'utf8'), ctx, { filename: f });
  }
  const SG = ctx.SG, out = {};
  for (const sid of ['classic', 'world']) {
    SG.ScenarioData = SG.Scenarios[sid];
    SG.Random.seed(1);
    const g = SG.GameState.newGame(SG.ScenarioData.Factions[0].split('|')[0], sid);
    out[sid] = g.factions.map(f => ({ key: f.key, name: f.name, culture: f.culture || 'han' }));
  }
  // 经典剧本不带 world-data.js：所有势力 han（TITLES §7.7）
  out.classicHan = out.classic.map(f => ({ key: f.key, name: f.name, culture: 'han' }));
  return out;
}

function check(o, opts) {
  opts = opts || {};
  const P = [];
  // 1 设计期的校验（同一份规则）
  try {
    delete require.cache[require.resolve(BUILDER)];
    const B = require(BUILDER);
    for (const q of B.check(o)) P.push('design check: ' + q);
  } catch (e) { P.push('design checker failed: ' + (e.message || e)); }
  // 2 结构
  if (o.schema !== 'sanguozhi2.titles/1') P.push('schema ' + o.schema);
  const FAMS = ['war', 'intel', 'pol'];
  for (const f of FAMS) if (!o.families[f] || !/^#[0-9a-f]{6}$/i.test(o.families[f].color || '')) P.push('family ' + f + ' missing or bad colour');
  if (!Array.isArray(o.tiers) || o.tiers.length !== 5) P.push('tiers must be 5');
  else {
    for (let i = 0; i < 5; i++) if (o.tiers[i].tier !== i + 1) P.push('tier index ' + i);
    for (const k of ['reqLevel', 'loyGrant', 'grantGold', 'effLoy', 'persuadeRes', 'discordRes', 'revokeLoy', 'reqStat'])
      for (let i = 1; i < 5; i++) if (!(o.tiers[i][k] >= o.tiers[i - 1][k])) P.push('tiers.' + k + ' not monotone at T' + (i + 1));
    for (const t of o.tiers) if (['small', 'medium', 'grand'].indexOf(t.ceremony) < 0) P.push('tier ' + t.tier + ' ceremony ' + t.ceremony);
  }
  const RR = o.rulerRanks;
  for (let i = 1; i < RR.thresholds.length; i++) if (!(RR.thresholds[i] > RR.thresholds[i - 1])) P.push('rank thresholds not increasing');
  if (!(RR.hysteresis > 0 && RR.hysteresis < 1)) P.push('rank hysteresis');
  for (let r = 1; r <= 5; r++) {
    const c = RR.caps[r];
    if (!Array.isArray(c) || c.length !== 5) { P.push('caps R' + r); continue; }
    if (r > 1) for (let t = 0; t < 5; t++) if (c[t] < RR.caps[r - 1][t]) P.push('caps R' + r + ' T' + (t + 1) + ' shrinks');
  }
  const ids = o.posts.map(p => p.id);
  if (ids.length !== 21) P.push('posts: ' + ids.length + ' (want 21)');
  if (new Set(ids).size !== ids.length) P.push('duplicate post ids');
  for (const p of o.posts) {
    if (FAMS.indexOf(p.family) < 0) P.push(p.id + ' family');
    if (p.tier >= 3 && p.tier <= 4 && p.seats !== 2) P.push(p.id + ' (T' + p.tier + ') must have 2 seats');
    if (p.tier <= 2 && p.seats !== 0) P.push(p.id + ' (T' + p.tier + ') seats must be 0 (tier cap only)');
    if ((p.tier === 3 || p.tier === 4) && ['A', 'B'].indexOf(p.branch) < 0) P.push(p.id + ' branch');
    for (const [k, v] of Object.entries(p.fx)) if (typeof v !== 'number' || !(v > 0)) P.push(p.id + '.' + k + ' must be a positive number');
  }
  const t5 = o.posts.filter(p => p.tier === 5).length;
  if (RR.caps[5][4] !== t5) P.push('R5 has ' + RR.caps[5][4] + ' T5 seats but there are ' + t5 + ' T5 posts');
  // 文化：回退链终止、名称长度、印信
  for (const [code, cu] of Object.entries(o.cultures)) {
    if (cu.insignia && cu.insignia.length !== 5) P.push(code + ' insignia must have 5 entries');
    for (const id of ids) {
      const e = resolveName(o, '', code, id);
      if (!e || !e.names || !e.names.length) P.push(code + ' cannot resolve ' + id);
    }
  }
  for (const c of Object.keys(o.cultureFallback)) {
    let x = c, n = 0;
    while (x && x !== 'generic' && n < 6) { x = o.cultureFallback[x] || 'generic'; n++; }
    if (n > 4) P.push('fallback chain of ' + c + ' longer than 4');
  }
  // 两个剧本的每个势力
  let fc = opts.factions;
  if (!fc) { try { fc = factionCultures(); } catch (e) { P.push('scenario factions not loaded: ' + (e.message || e)); fc = {}; } }
  const counts = {};
  for (const [sid, list] of Object.entries(fc)) {
    counts[sid] = list.length;
    for (const f of list) {
      const names = ids.map(id => { const e = resolveName(o, f.key, f.culture, id); return e && e.names && e.names[0] ? e.names[0].zh : null; });
      if (names.some(z => !z)) { P.push(sid + ' ' + f.key + ' (' + f.culture + ') does not resolve every post'); continue; }
      const dup = names.filter((z, i) => names.indexOf(z) !== i);
      if (dup.length) P.push(sid + ' ' + f.key + ' (' + f.culture + ') duplicate default names ' + dup.join(','));
      const rr = rulerRanks(o, f.key, f.culture);
      if (!rr || rr.length !== 5) P.push(sid + ' ' + f.key + ' ruler ranks');
      else { const clash = names.filter(z => rr.indexOf(z) >= 0); if (clash.length) P.push(sid + ' ' + f.key + ' post names clash with ruler ranks ' + clash.join(',')); }
      for (const id of ids) for (const n of resolveName(o, f.key, f.culture, id).names) if (!n.zh || n.zh.length > 7) P.push(sid + ' ' + f.key + '.' + id + ' bad name ' + JSON.stringify(n.zh));
    }
  }
  return { problems: P, counts };
}

function compact(o) {
  const d = Object.assign({}, o);
  delete d.generated; delete d.doc;
  const body = JSON.stringify(d);
  const hash = crypto.createHash('md5').update(body).digest('hex').slice(0, 12);
  let names = 0;
  for (const cu of Object.values(o.cultures)) for (const e of Object.values(cu.posts)) names += e.names.length;
  let ovNames = 0;
  for (const ov of Object.values(o.factionOverrides)) for (const e of Object.values(ov.posts || {})) ovNames += e.names.length;
  d.hash = hash;
  d.meta = { posts: o.posts.length, cultures: Object.keys(o.cultures).length, overrides: Object.keys(o.factionOverrides).length, names, overrideNames: ovNames, sources: Object.keys(o.sources).length };
  return d;
}

function writeJs(o) {
  const d = compact(o);
  const head = `'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 官爵目录（TITLES.md §3）—— 由 tools/gen-titles.js 从 design-v4/titles.json 生成，请勿手改。
   SG.TitlesData：${d.meta.posts} 个官位 · ${d.meta.cultures} 种文化的官名（${d.meta.names} 个）· ${d.meta.overrides} 个势力覆盖（${d.meta.overrideNames} 个）· ${d.meta.sources} 条出处 · hash ${d.hash}
   载入：world-data.js 之后、econ.js / model.js 之前（INTEGRATION §11）。规则在 js/titles.js（SG.Titles）。
   ========================================================================== */
`;
  const src = head + '(function () {\n  const SG = (window.SG = window.SG || {});\n  SG.TitlesData = ' + JSON.stringify(d) + ';\n})();\n';
  fs.writeFileSync(OUT_JS, src);
  return { bytes: Buffer.byteLength(src), hash: d.hash, meta: d.meta };
}

if (require.main === module) {
  const MODE = (process.argv[2] || 'js').toLowerCase();
  if (MODE === 'build') {
    const cp = require('child_process');
    const r = cp.spawnSync(process.execPath, [BUILDER], { stdio: 'inherit' });
    if (r.status) { console.log('build-titles-json.js failed'); process.exit(r.status || 1); }
  }
  const o = load();
  const r = check(o);
  console.log('titles.json: ' + o.posts.length + ' posts, ' + Object.keys(o.cultures).length + ' cultures, ' + Object.keys(o.factionOverrides).length + ' overrides; factions checked ' + JSON.stringify(r.counts));
  for (let t = 1; t <= 5; t++) {
    const ps = o.posts.filter(p => p.tier === t);
    console.log('  T' + t + ' value: ' + ps.map(p => p.id + ' ' + valueOf(o, p).toFixed(1)).join(' · '));
  }
  if (r.problems.length) console.log('PROBLEMS (' + r.problems.length + '):\n  ' + r.problems.join('\n  '));
  else console.log('check OK');
  if (MODE !== 'check') {
    if (r.problems.length) { console.log('js/titles-data.js NOT written'); }
    else { const w = writeJs(o); console.log('wrote js/titles-data.js (' + (w.bytes / 1024).toFixed(1) + ' KB, hash ' + w.hash + ')'); }
  }
  process.exitCode = Math.min(100, r.problems.length);
}

module.exports = { load, check, compact, resolveName, rulerRanks, valueOf, factionCultures, JSON_PATH, OUT_JS };
