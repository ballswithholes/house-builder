'use strict';
/* ==========================================================================
   tools/gen-items.js — 第四版装备目录的生成与校验（ITEMS.md §1、§17.1）
   design-v4/items.json 是唯一的数据源（602 件装备与全部表）；本工具把它变成游戏读的 js/items-data.js，并做 CI 校验。
   设计期的生成器在 design-v4/items/（build.js 从表格生成 items.json，gen-md.js 生成 ITEMS.md 的表格），这里是它们的入口。

   用法：
     node tools/gen-items.js [js]        校验 items.json，写 js/items-data.js（SG.ItemsData；缺省模式）
     node tools/gen-items.js check       只校验（tests/items.js §1 的全部目录检查 + kitWeapon 与当前 figure.js 是否一致；退出码 = 问题数）
     node tools/gen-items.js build       从 design-v4/items/ 的表格重建 design-v4/items.json（先导出剧本数据），再写 js
     node tools/gen-items.js md          重新生成 ITEMS.md 的 GENERATED 表格（gen-md.js + splice-md.js）
     node tools/gen-items.js cs          Unity 的 ItemsData.g.cs —— 第 E 阶段（Unity 移植）再做，现在只打印说明
   选项：--kit-from=figure|duel|none     起始兵器的类型取自 SG.Figure.lookOf（缺省；需要 vendor/three.min.js 与美术文件
                                        能在 Node 下载入）或 SG.DuelGame.lookOf（备用），none = 运行时按能力推断

   js/items-data.js 的内容：SG.ItemsData = { version, hash, rarities, slots, types, statKeys, duelWeapons, cultures,
     items, shops, classic, world, kits, fig2type, styleFallback, kitWeapon, meta }
     与 items.json 的差别：每件装备 availability.<剧本> 里的城市名单（native / neighbour / trade）换成计数
     （名单在 shops 表里，SG.Items 运行时建反查索引，约省 136 KB）；另加 kitWeapon（武将名 → 起始兵器类型，
     ITEMS §8.10：「v3 外观决定兵器类型」，生成时冻结，Node 与浏览器结果一致）；duelWeapons 的 style / reach / pow 取自
     js/duel-game.js 的 WEAPONS（W5 的兵器平衡为准，duelWeaponsSource 记来源），每件兵器自己的 duel.style / reach 随之、
     duel.pow = 表上的 pow × 这件在设计表上的品级倍数（itemDuel）；check 模式报告类型表与每件兵器的漂移。
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const cp = require('child_process');
const crypto = require('crypto');

const ROOT = path.join(__dirname, '..');
const JSON_PATH = path.join(ROOT, 'design-v4', 'items.json');
const OUT_JS = path.join(ROOT, 'js', 'items-data.js');
const ARGS = process.argv.slice(2);
const MODE = (ARGS.find(a => !a.startsWith('--')) || 'js').toLowerCase();
const opt = (k, d) => { const a = ARGS.find(x => x.startsWith('--' + k + '=')); return a ? a.slice(k.length + 3) : d; };

// ------------------------------------------------------------------ a rules context (for check) --
function rulesContext(data) {
  const ctx = { console, Math, JSON, Date, setTimeout, clearTimeout, performance: { now: () => Date.now() } };
  ctx.window = ctx; ctx.global = ctx;
  vm.createContext(ctx);
  const files = ['core.js', 'registry.js', 'world-geo.js', 'data.js', 'world-data.js'];
  for (const f of files) vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), ctx, { filename: f });
  ctx.SG.ItemsData = data;
  for (const f of ['econ.js', 'model.js', 'items.js', 'specials-data.js', 'specials.js']) {
    const p = path.join(ROOT, 'js', f);
    if (fs.existsSync(p)) vm.runInContext(fs.readFileSync(p, 'utf8'), ctx, { filename: f });
  }
  return ctx.SG;
}

// ------------------------------------------------------------------ kit weapons (ITEMS §8.10) --
function scenarioGenerals() {
  const ctx = { console, Math, JSON };
  ctx.window = ctx; ctx.global = ctx;
  vm.createContext(ctx);
  for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), ctx, { filename: f });
  const SG = ctx.SG, out = new Map();
  for (const sid of ['classic', 'world']) {
    for (const l of SG.Scenarios[sid].Generals) {
      const p = l.split('|');
      if (!out.has(p[0])) out.set(p[0], { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], culture: p[9] || SG.WorldData.cultureOfGeneral(p[0]) });
    }
  }
  return Array.from(out.values());
}
function kitWeapons(data, from) {
  if (from === 'none') return { src: 'none', table: {} };
  const gens = scenarioGenerals();
  const tryLoad = files => {
    const ctx = { console: { log() {}, warn() {}, error() {}, info() {} }, Math, JSON, Date, setTimeout, clearTimeout, performance: { now: () => Date.now() } };
    ctx.window = ctx; ctx.global = ctx; ctx.self = ctx;
    ctx.document = { createElement: () => ({ getContext: () => null, style: {}, setAttribute() {}, appendChild() {} }), createElementNS: () => ({ style: {}, setAttribute() {} }), body: { appendChild() {} } };
    ctx.navigator = { userAgent: 'node' };
    vm.createContext(ctx);
    for (const f of files) vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), ctx, { filename: f });
    return ctx.SG;
  };
  const table = {};
  let src = from;
  const fig2type = data.fig2type;
  const DUEL2TYPE = { guandao: 'poleblade', snake: 'spear', ji: 'ji', spear: 'spear', poleblade: 'poleblade', axe: 'axe', bigblade: 'bigblade', sword: 'sword', dao: 'dao',
    shortji: 'shortji', mace: 'mace', twinsword: 'twinsword', twinji: 'twinji', twindao: 'twindao', gladius: 'gladius', lance: 'lance', handaxe: 'handaxe', tachi: 'tachi' };
  if (from === 'figure') {
    try {
      const SG = tryLoad(['vendor/three.min.js', 'js/core.js', 'js/data.js', 'js/world-data.js', 'js/portrait-data.js', 'js/portrait.js', 'js/lowpoly.js', 'js/lp-props.js', 'js/figure.js']);
      for (const g of gens) {
        const w = SG.Figure.lookOf(g).weapon;
        const t = fig2type[w];
        if (t) table[g.name] = t;
      }
    } catch (e) {
      console.log('  (kit) SG.Figure.lookOf not loadable in Node (' + String(e.message).slice(0, 120) + ') — falling back to SG.DuelGame.lookOf');
      src = 'duel';
    }
  }
  if (src === 'duel') {
    const SG = tryLoad(['js/core.js', 'js/data.js', 'js/world-data.js', 'js/duel-game.js']);
    for (const g of gens) { const t = DUEL2TYPE[SG.DuelGame.lookOf(g).weapon]; if (t) table[g.name] = t; }
  }
  return { src, table };
}

// ------------------------------------------------------------------ duel weapons (COMBAT-V4 §15) --
// 单挑兵器表归 js/duel-game.js（W5：兵器平衡之后冻结的 style / reach / pow）；items.json 的 duelWeapons 是设计期的数。
// 生成时以 SG.DuelGame.WEAPONS 为准（同名键，新键 fan* / staff* / club* / vajra* 去掉 * 对上），只改 style / reach / pow；
// duel-game.js 不在或载不进 Node 时保留 items.json 的数（duelWeaponsSource 记来源）
function duelWeaponTable() {
  try {
    const ctx = { console: { log() {}, warn() {}, error() {}, info() {} }, Math, JSON, Date, setTimeout, clearTimeout, performance: { now: () => Date.now() } };
    ctx.window = ctx; ctx.global = ctx; ctx.self = ctx;
    ctx.document = { createElement: () => ({ getContext: () => null, style: {}, setAttribute() {}, appendChild() {} }), createElementNS: () => ({ style: {}, setAttribute() {} }), body: { appendChild() {} } };
    ctx.navigator = { userAgent: 'node' };
    vm.createContext(ctx);
    for (const f of ['js/core.js', 'js/data.js', 'js/world-data.js', 'js/duel-game.js']) vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), ctx, { filename: f });
    const W = ctx.SG.DuelGame && ctx.SG.DuelGame.WEAPONS;
    return W && typeof W === 'object' ? JSON.parse(JSON.stringify(W)) : null;
  } catch (e) { return null; }
}
function duelWeapons(D, W) {
  W = W === undefined ? duelWeaponTable() : W;
  if (!W) return { src: 'items.json', table: D.duelWeapons };
  const table = {};
  for (const [k, v] of Object.entries(D.duelWeapons)) {
    const w = W[k.replace(/\*$/, '')];
    table[k] = w ? Object.assign({}, v, { style: w.style, reach: w.reach, pow: w.pow === undefined ? 1 : w.pow }) : v;
  }
  return { src: 'duel-game.js', table };
}
// 每件兵器自己的 duel.style / reach / pow 跟着类型表走：reach、style 取表上的；pow = 表上的 pow × 这件在设计表上的倍数
// （items.json 里每件的 pow = 设计期类型 pow × (1 + 品级加成)，如枪 1.00 / 1.01 / 1.02 / 1.03），三位小数
const r3 = x => Math.round(x * 1000) / 1000;
function itemDuel(it, D, table) {
  if (!it.duel || !it.duel.key) return it.duel;
  const t = table[it.duel.key], d0 = D.duelWeapons[it.duel.key];
  if (!t || !d0) return it.duel;
  const k = typeof it.duel.pow === 'number' && d0.pow ? it.duel.pow / d0.pow : 1;
  return Object.assign({}, it.duel, { style: t.style, reach: t.reach, pow: r3((t.pow === undefined ? 1 : t.pow) * k) });
}
// 单挑兵器漂移：js/items-data.js 的 duelWeapons（与每件兵器的 duel 数）和当前 duel-game.js 的 WEAPONS 比较
// （W5 再调兵器之后要重跑 node tools/gen-items.js js）
function duelDrift(D) {
  const cur = loadItemsData();
  if (!cur || !cur.duelWeapons) return { problems: ['js/items-data.js missing or without duelWeapons'], changed: [] };
  const W = duelWeaponTable();
  if (!W) return { problems: [], changed: [], skipped: 'js/duel-game.js not loadable in Node' };
  const fresh = duelWeapons(D, W).table, changed = [];
  for (const k of Object.keys(fresh)) {
    const a = cur.duelWeapons[k] || {}, b = fresh[k];
    if (a.style !== b.style || a.reach !== b.reach || a.pow !== b.pow) changed.push(k + ': ' + [a.style, a.reach, a.pow].join('/') + ' → ' + [b.style, b.reach, b.pow].join('/'));
  }
  const curItems = new Map((cur.items || []).map(it => [it.id, it]));
  let nItems = 0;
  for (const it of D.items) {
    if (!it.duel || !it.duel.key) continue;
    nItems++;
    const a = (curItems.get(it.id) || {}).duel || {}, b = itemDuel(it, D, fresh);
    if (a.style !== b.style || a.reach !== b.reach || a.pow !== b.pow) changed.push(it.id + ' (' + it.duel.key + '): ' + [a.style, a.reach, a.pow].join('/') + ' → ' + [b.style, b.reach, b.pow].join('/'));
  }
  return { problems: changed.length ? ['duelWeapons drift vs duel-game.js WEAPONS (' + changed.length + ' keys / items; rerun node tools/gen-items.js js): ' + changed.slice(0, 12).join(', ')] : [], changed, n: Object.keys(fresh).length, nItems };
}

// ------------------------------------------------------------------ runtime data --
function runtimeData(D, kit, duel) {
  duel = duel || { src: 'items.json', table: D.duelWeapons };
  const items = D.items.map(it => {
    const o = Object.assign({}, it);
    const av = {};
    for (const sid of ['classic', 'world']) {
      const a = it.availability[sid];
      if (!a) { av[sid] = null; continue; }
      const b = Object.assign({}, a);
      for (const k of ['native', 'neighbour', 'trade']) if (Array.isArray(b[k])) { b[k + 'N'] = b[k].length; delete b[k]; }
      av[sid] = b;
    }
    o.availability = av;
    if (duel.src === 'duel-game.js' && it.duel) o.duel = itemDuel(it, D, duel.table);
    // giftK 的作用范围（ITEMS §7.2）：「以宝物赏赐 / 外交时价值 ×k」= 持有者送出的任何宝物（holder: true）；「以之赏赐」= 这件本身
    if (it.ability && it.ability.effects.some(e => e.t === 'giftK')) {
      o.ability = Object.assign({}, it.ability, { effects: it.ability.effects.map(e => (e.t === 'giftK' ? Object.assign({}, e, { holder: /以宝物/.test(it.ability.desc || '') }) : e)) });
    }
    return o;
  });
  const out = {
    version: D.meta.version, generator: 'tools/gen-items.js from design-v4/items.json',
    meta: { counts: D.meta.counts, distribution: D.meta.distribution },
    rarities: D.rarities, slots: D.slots, types: D.types, duelWeapons: duel.table, duelWeaponsSource: duel.src, statKeys: D.statKeys, cultures: D.cultures,
    kits: D.kits, fig2type: D.fig2type, styleFallback: D.styleFallback,
    items, shops: D.shops, classic: D.classic, world: D.world,
    kitWeapon: kit.table, kitWeaponSource: kit.src,
  };
  out.hash = crypto.createHash('md5').update(JSON.stringify(out)).digest('hex').slice(0, 12);
  return out;
}
// JSON 放进单引号字符串（JSON.parse 比同样大小的对象字面量解析得快）：只需转义 \ ' 与行分隔符
const lit = json => json.replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/\u2028/g, '\\u2028').replace(/\u2029/g, '\\u2029');
function writeJs(data) {
  const json = JSON.stringify(data);
  const src = `'use strict';
/* ==========================================================================
   js/items-data.js — 生成文件，请勿手改（node tools/gen-items.js；数据源 design-v4/items.json，ITEMS.md §1）
   SG.ItemsData：${data.items.length} 件装备（兵器 ${data.meta.counts.weapons}、甲胄装备 ${data.meta.counts.gear}；传说 ${data.meta.counts.byRarity.legendary}、史诗 ${data.meta.counts.byRarity.epic}），
   两个剧本的商店表、专属 / 招牌 / 隐藏 / 镇城 / 府库 / 大会表、起始装备表（kits、kitWeapon）。hash ${data.hash}
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  SG.ItemsData = JSON.parse('${lit(json)}');
})();
`;
  fs.writeFileSync(OUT_JS, src);
  return Buffer.byteLength(src);
}

// kitWeapon 漂移（ITEMS §8.10）：js/items-data.js 冻结的「武将 → 起始兵器类型」与当前 SG.Figure.lookOf 比较。
// figure.js 归美术推广，改了某个外观的兵器之后要重跑 node tools/gen-items.js js，否则起始兵器与模型对不上。
function loadItemsData() {
  if (!fs.existsSync(OUT_JS)) return null;
  const ctx = { JSON }; ctx.window = ctx; vm.createContext(ctx);
  vm.runInContext(fs.readFileSync(OUT_JS, 'utf8'), ctx, { filename: 'items-data.js' });
  return ctx.SG && ctx.SG.ItemsData;
}
function kitDrift(D) {
  const cur = loadItemsData();
  if (!cur || !cur.kitWeapon) return { problems: ['js/items-data.js missing or without kitWeapon'], changed: [], src: null };
  const kit = kitWeapons(D, cur.kitWeaponSource || 'figure');
  if (kit.src !== (cur.kitWeaponSource || 'figure')) return { problems: [], changed: [], src: kit.src, skipped: 'source ' + kit.src + ' ≠ ' + cur.kitWeaponSource };
  const changed = [];
  const names = new Set(Object.keys(cur.kitWeapon).concat(Object.keys(kit.table)));
  for (const n of names) if (cur.kitWeapon[n] !== kit.table[n]) changed.push(n + ': ' + (cur.kitWeapon[n] || '—') + ' → ' + (kit.table[n] || '—'));
  return { problems: changed.length ? ['kitWeapon drift (' + changed.length + ' generals; rerun node tools/gen-items.js js): ' + changed.slice(0, 12).join(', ')] : [], changed, src: kit.src, n: names.size };
}

function check(D) {
  const SG = rulesContext(D);
  if (!SG.Items || typeof SG.Items.checkCatalogue !== 'function') { console.log('  js/items.js not present: only the data checks of SG.Items can run'); return { problems: ['js/items.js missing'], stats: {} }; }
  return SG.Items.checkCatalogue(D);
}

function main() {
  if (MODE === 'cs') {
    console.log('ItemsData.g.cs (Unity) is phase E of DESIGN-V4 §18 (after the browser build is stable); tools/export-unity-items.js will write it.');
    return 0;
  }
  if (MODE === 'build') {
    const tmp = path.join(require('os').tmpdir(), 'sg-items-scen.json');
    cp.execFileSync(process.execPath, [path.join(ROOT, 'design-v4', 'items', 'dump.js'), tmp], { stdio: 'inherit' });
    cp.execFileSync(process.execPath, [path.join(ROOT, 'design-v4', 'items', 'build.js'), tmp, JSON_PATH], { stdio: 'inherit' });
  }
  if (MODE === 'md') {
    const tmp = path.join(require('os').tmpdir(), 'sg-items-scen.json'), tables = path.join(require('os').tmpdir(), 'sg-items-tables.md');
    cp.execFileSync(process.execPath, [path.join(ROOT, 'design-v4', 'items', 'dump.js'), tmp], { stdio: 'inherit' });
    cp.execFileSync(process.execPath, [path.join(ROOT, 'design-v4', 'items', 'gen-md.js'), JSON_PATH, tmp, tables], { stdio: 'inherit' });
    cp.execFileSync(process.execPath, [path.join(ROOT, 'design-v4', 'items', 'splice-md.js'), tables], { stdio: 'inherit' });
    return 0;
  }
  const D = JSON.parse(fs.readFileSync(JSON_PATH, 'utf8'));
  const t0 = Date.now();
  const rep = check(D);
  for (const [k, v] of Object.entries(rep.stats || {})) console.log('  ' + k + ': ' + (typeof v === 'object' ? JSON.stringify(v) : v));
  if (rep.problems.length) console.log('PROBLEMS (' + rep.problems.length + '):\n  ' + rep.problems.slice(0, 80).join('\n  '));
  else console.log('CATALOGUE OK (' + D.items.length + ' items, ' + (Date.now() - t0) + ' ms)');
  if (MODE === 'check') {
    const kd = kitDrift(D);
    if (kd.skipped) console.log('  kitWeapon drift: skipped (' + kd.skipped + ')');
    else if (kd.problems.length) console.log('PROBLEM: ' + kd.problems[0]);
    else console.log('  kitWeapon drift: none (' + kd.n + ' generals vs ' + kd.src + ')');
    const dd = duelDrift(D);
    if (dd.skipped) console.log('  duelWeapons drift: skipped (' + dd.skipped + ')');
    else if (dd.problems.length) console.log('PROBLEM: ' + dd.problems[0]);
    else console.log('  duelWeapons drift: none (' + dd.n + ' keys and ' + dd.nItems + ' weapons vs duel-game.js)');
    return rep.problems.length + kd.problems.length + dd.problems.length;
  }
  if (rep.problems.length && !ARGS.includes('--force')) { console.log('js/items-data.js not written (fix the problems or pass --force)'); return rep.problems.length; }
  const kit = kitWeapons(D, opt('kit-from', 'figure'));
  const duel = duelWeapons(D);
  const data = runtimeData(D, kit, duel);
  const bytes = writeJs(data);
  console.log(`js/items-data.js written: ${(bytes / 1024).toFixed(0)} KB, hash ${data.hash}, kit weapons ${Object.keys(kit.table).length} (from ${kit.src}), duel weapons from ${duel.src}`);
  return 0;
}
if (require.main === module) process.exitCode = main();
module.exports = { kitWeapons, kitDrift, duelWeapons, duelDrift, itemDuel, runtimeData, loadItemsData, JSON_PATH };
