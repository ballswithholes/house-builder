'use strict';
/* ==========================================================================
   tools/build-suites.js — 冻结生成的君主乐章（AUDIO-V4 §A6.8）
   按两个剧本的武将名单顺序运行 SG.MusicSuites.generate()（手写的 42 套除外），把结果写进 js/music-suites-gen.js。
   已冻结的条目原样保留（以后数据有变只追加新武将，从不重掷已有条目——与 COMBAT-V4 §10.4 的连段同一规则）。

   node tools/build-suites.js            写 js/music-suites-gen.js（保留已有条目，追加新武将）
   node tools/build-suites.js --check    不写文件；从头重算，已冻结的条目若会改变、或名单里有武将未冻结则失败（exit 1）
   node tools/build-suites.js --fresh    忽略已冻结的条目从头生成（仅在 v4.0 首次冻结时使用）
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.join(__dirname, '..');
const GEN_FILE = path.join(ROOT, 'js', 'music-suites-gen.js');

function load(withGen) {
  const ctx = { console, Math, JSON, Date, performance: { now: () => Date.now() } };
  ctx.window = ctx; ctx.globalThis = ctx;
  vm.createContext(ctx);
  const files = ['core.js', 'music.js', 'music-inst.js', 'music-han.js', 'music-world.js', 'data.js', 'world-data.js', 'music-suites.js'];
  if (withGen && fs.existsSync(GEN_FILE)) files.push('music-suites-gen.js');
  for (const f of files) vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), ctx, { filename: f });
  return ctx.SG;
}

// 两个剧本的全部武将（经典在前，同名只取第一次），以及势力表（德、君主）
function roster(SG) {
  const out = [], seen = new Set(), fac = {};
  for (const sc of [SG.Scenarios.classic, SG.Scenarios.world]) {
    for (const l of sc.Factions) { const p = l.split('|'); fac[p[0]] = { key: p[0], name: p[1], ruler: p[2], virtue: +p[4], culture: p[6] || (p[0] === 'menghuo' ? 'nanman' : 'han') }; }
  }
  for (const [si, sc] of [[0, SG.Scenarios.classic], [1, SG.Scenarios.world]]) {
    for (const l of sc.Generals) {
      const p = l.split('|');
      if (seen.has(p[0])) continue; seen.add(p[0]);
      const culture = p[9] || (SG.WorldData ? SG.WorldData.cultureOfGeneral(p[0]) : 'han') || 'han';
      out.push({ name: p[0], war: +p[1], intel: +p[2], pol: +p[3], faction: p[4], culture, sex: SG.WorldData.sexOf(p[0]), born: SG.WorldData.bornOf(p[0]), scen: si });
    }
  }
  return { gens: out, fac };
}

// 生成（frozen = 已冻结的条目；fresh 时为空）→ { suites, order, stats }
function build(SG, frozen) {
  const MS = SG.MusicSuites, MU = SG.Music;
  const { gens, fac } = roster(SG);
  const rulers = new Set(Object.values(fac).map(f => f.ruler));
  const suites = {};
  const sigs = [];
  for (const [n, s] of Object.entries(MS.HAND)) { suites[n] = s; sigs.push([n, MS.signature(s.motif)]); }
  const out = {}, stats = { martial: 0, scholar: 0, statesman: 0, rerolls: 0, worst: 0, kept: 0, added: 0, missing: [] };
  for (const g of gens) {
    if (MS.HAND[g.name]) continue;
    const family = MS.FAMILY_OF[g.culture] || 'hua';
    if (frozen && frozen[g.name]) {
      const s = frozen[g.name];
      suites[g.name] = s; out[g.name] = s; sigs.push([g.name, MS.signature(s.motif)]); stats.kept++;
      stats[s.temper] = (stats[s.temper] || 0) + 1;
      continue;
    }
    const f = fac[g.faction];
    const ctx = { family, virtue: f ? f.virtue : 50, mapScale: MU.get(MU.resolve('map', MS.TEMPLATE_CULTURE[family])).scale };
    // 起始君主还要满足调色规则：与已有的同家族起始君主在主奏、速度或调上至少差一项
    const palette = rulers.has(g.name) ? [...rulers].filter(r => suites[r]).map(r => suites[r]) : null;
    const r = MS.generateDistinct(g, ctx, sigs, palette);
    if (!r) { stats.missing.push(g.name); continue; }
    stats.rerolls += r.suite.attempt; stats.worst = Math.max(stats.worst, r.suite.attempt);
    // §A10 house cell: a clansman (same family name, han / korea culture) of his starting faction's ruler keeps the ruler's head
    const f0 = fac[g.faction];
    const founder = f0 && f0.ruler !== g.name && suites[f0.ruler] ? f0.ruler : null;
    if (founder && g.name[0] === founder[0] && /^(han|korea)$/.test(g.culture) && /^(han|korea)$/.test(f0.culture) && !rulers.has(g.name)) {
      const motif = MS.forceHead(r.suite.motif, suites[founder].motif);
      const sg = MS.signature(motif);
      const fsig = MS.signature(suites[founder].motif);
      if (MS.distance(sg, fsig) >= MS.D_MIN_HOUSE && sigs.every(x => x[0] === founder || MS.distance(sg, x[1]) >= MS.D_MIN)) {
        r.suite = Object.assign({}, r.suite, { motif, house: founder }); r.sig = sg; stats.house = (stats.house || 0) + 1;
      } else stats.houseRejected = (stats.houseRejected || 0) + 1;
    }
    suites[g.name] = r.suite; out[g.name] = r.suite; sigs.push([g.name, r.sig]); stats.added++;
    stats[r.suite.temper]++;
  }
  return { suites: out, all: suites, gens, rulers, stats };
}

const FIELDS = ['family', 'motif', 'lead', 'answer', 'tempo', 'tr', 'temper', 'attempt', 'house'];
function entryJSON(s) { const o = {}; for (const k of FIELDS) if (s[k] !== undefined) o[k] = s[k]; return JSON.stringify(o); }

function write(res) {
  const lines = Object.entries(res.suites).map(([n, s]) => '  ' + JSON.stringify(n) + ': ' + entryJSON(s) + ',');
  const txt = `'use strict';
/* ==========================================================================
   生成的君主乐章（冻结）· SG.MusicSuites.GEN —— 由 tools/build-suites.js 写入，切勿手改（AUDIO-V4 §A6.8）
   ${Object.keys(res.suites).length} 名武将（两个剧本名单顺序；手写的 42 套在 js/music-suites.js 的 HAND）。
   字段：family 文化家族 · motif 动机（2 小节 4/4 简谱）· lead / answer 乐器角色 · tempo 速度系数 · tr 移调 · temper 气质 · attempt 重掷次数
   ========================================================================== */
(function () {
  const SG = (typeof window !== 'undefined' ? window : globalThis).SG = (typeof window !== 'undefined' ? window : globalThis).SG || {};
  const MS = SG.MusicSuites = SG.MusicSuites || {};
  MS.GEN = {
${lines.join('\n')}
  };
})();
`;
  fs.writeFileSync(GEN_FILE, txt);
}

function readFrozen() {
  if (!fs.existsSync(GEN_FILE)) return null;
  const SG = load(true);
  const G = SG.MusicSuites.GEN || {};
  return Object.keys(G).length ? JSON.parse(JSON.stringify(G)) : null;
}

function main() {
  const check = process.argv.includes('--check'), fresh = process.argv.includes('--fresh');
  const frozen = fresh ? null : readFrozen();
  const SG = load(false);
  if (check) {
    const res = build(SG, null);          // 从头重算
    const fails = [];
    if (!frozen) fails.push('js/music-suites-gen.js missing or empty');
    else {
      for (const [n, s] of Object.entries(frozen)) {
        const r = res.suites[n];
        if (!r) { fails.push('frozen entry ' + n + ' is no longer a generated general'); continue; }
        if (entryJSON(r) !== entryJSON(s)) fails.push('frozen entry would change: ' + n + '\n    frozen ' + entryJSON(s) + '\n    now    ' + entryJSON(r));
      }
      for (const n of Object.keys(res.suites)) if (!frozen[n]) fails.push('general not frozen yet (run node tools/build-suites.js): ' + n);
    }
    if (res.stats.missing.length) fails.push('generator found no distinct motif for ' + res.stats.missing.join(' '));
    console.log(`build-suites --check: ${Object.keys(res.suites).length} generated (martial ${res.stats.martial} / scholar ${res.stats.scholar} / statesman ${res.stats.statesman}), re-rolls ${res.stats.rerolls}, worst ${res.stats.worst}, house cells ${res.stats.house || 0} (rejected ${res.stats.houseRejected || 0}); frozen ${frozen ? Object.keys(frozen).length : 0}`);
    console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 20).join('\n  ') : 'OK (frozen entries unchanged)');
    process.exitCode = fails.length ? 1 : 0;
    return;
  }
  const res = build(SG, frozen);
  if (res.stats.missing.length) { console.error('generator found no distinct motif for ' + res.stats.missing.join(' ')); process.exitCode = 1; return; }
  write(res);
  console.log(`wrote js/music-suites-gen.js: ${Object.keys(res.suites).length} suites (kept ${res.stats.kept}, added ${res.stats.added}; martial ${res.stats.martial} / scholar ${res.stats.scholar} / statesman ${res.stats.statesman}; re-rolls ${res.stats.rerolls}, worst ${res.stats.worst}; house cells ${res.stats.house || 0}, rejected ${res.stats.houseRejected || 0})`);
}

module.exports = { load, roster, build, entryJSON };
if (require.main === module) main();
