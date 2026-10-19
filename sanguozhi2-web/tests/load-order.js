'use strict';
/* ==========================================================================
   tests/load-order.js — index.html 的载入顺序（INTEGRATION §11）
   解析 index.html 的 <script src>，断言：每个文件存在、没有重复、每个模块声明的依赖都在它之前载入
   （只检查已经存在的文件：尚未落地的工作流跳过）、game.js 最后；第四版的规则层文件不引用 THREE / document
   （「规则从不引用视图文件」，Node 测试只载入规则层）；js/ 下没有被 index.html 引用的文件列为提示。
   另外把规则层按 index.html 的次序载入一个干净的 vm 上下文，确认能载入、并且 SG.Mods / SG.Econ / SG.AIBudget /
   SG.MigrateV4 / SG.BattleMath / SG.Settings / SG.Clock 都在。
   用法：node tests/load-order.js        退出码 = 失败数
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const { indexScripts, load, RULES, ROOT, JS } = require('./v4-load.js');

let fails = 0, checks = 0;
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } }

// 依赖表：模块 → 必须在它之前载入的文件（不存在的依赖忽略）。INTEGRATION §11 与各规格的「载入」行。
const DEPS = {
  'js/core.js': ['vendor/three.min.js'],
  'js/registry.js': ['js/core.js'],
  'js/world-geo.js': ['js/core.js'],
  'js/data.js': ['js/core.js'],
  'js/world-data.js': ['js/data.js', 'js/world-geo.js'],
  'js/items-data.js': ['js/world-data.js'],
  'js/titles-data.js': ['js/world-data.js'],
  'js/engage-data.js': ['js/world-data.js'],
  'js/tourney-data.js': ['js/world-data.js'],
  'js/combo-data.js': ['js/world-data.js'],
  'js/econ.js': ['js/core.js', 'js/data.js', 'js/world-data.js', 'js/registry.js', 'js/items-data.js', 'js/titles-data.js', 'js/engage-data.js', 'js/tourney-data.js', 'js/combo-data.js'],
  'js/model.js': ['js/data.js', 'js/world-data.js', 'js/econ.js', 'js/registry.js'],
  'js/items.js': ['js/econ.js', 'js/model.js', 'js/items-data.js'],
  'js/titles.js': ['js/econ.js', 'js/model.js', 'js/titles-data.js', 'js/items.js'],
  'js/engage.js': ['js/econ.js', 'js/model.js', 'js/engage-data.js', 'js/items.js', 'js/titles.js'],
  'js/tourney.js': ['js/econ.js', 'js/model.js', 'js/tourney-data.js', 'js/items.js', 'js/engage.js'],
  'js/combos.js': ['js/model.js', 'js/combo-data.js'],
  'js/migrate-v4.js': ['js/model.js', 'js/items.js', 'js/titles.js', 'js/engage.js', 'js/tourney.js', 'js/combos.js'],
  'js/strategy-ai.js': ['js/model.js', 'js/econ.js', 'js/migrate-v4.js'],
  'js/battle-math.js': ['js/econ.js', 'js/strategy-ai.js'],
  'js/battle-model.js': ['js/battle-math.js', 'js/strategy-ai.js', 'js/model.js'],
  'js/music.js': ['js/audio.js'],
  'js/music-inst.js': ['js/music.js'],
  'js/music-han.js': ['js/music-inst.js'],
  'js/music-world.js': ['js/music-han.js'],
  'js/music-suites.js': ['js/music-world.js'],
  'js/music-suites-gen.js': ['js/music-suites.js'],
  'js/music-stings.js': ['js/music-world.js'],
  'js/music-tourney.js': ['js/music-world.js'],
  'js/voice-data.js': ['js/audio.js'],
  'js/voice.js': ['js/voice-data.js'],
  'js/voice-cry.js': ['js/voice.js'],
  'js/settings.js': ['js/registry.js'],
  'js/ui-icons.js': ['js/registry.js'],
  'js/ui.js': ['js/core.js'],
  'js/ui-art.js': ['js/ui.js'],
  'js/shell.js': ['js/ui.js', 'js/settings.js'],
  'js/portrait.js': ['js/portrait-data.js'],
  'js/lp-props.js': ['js/lowpoly.js'],
  'js/lp-pilot.js': ['js/lp-props.js'],
  'js/lp-recipe.js': ['js/lowpoly.js'],
  'js/item-art.js': ['js/culture-art.js'],
  'js/city-layout.js': ['js/model.js', 'js/econ.js'],
  'js/city-kit.js': ['js/lowpoly.js', 'js/lp-props.js'],
  'js/city-life.js': ['js/city-kit.js'],
  'js/city-view.js': ['js/city-layout.js', 'js/city-kit.js', 'js/city-life.js', 'js/camera.js'],
  'js/specials.js': ['js/specials-data.js'],
  'js/duel-game.js': ['js/combo-data.js', 'js/combos.js'],
  'js/stadium-kit.js': ['js/lowpoly.js'],
  'js/stadium.js': ['js/stadium-kit.js', 'js/duel-game.js'],
  'js/domestic-tiers.js': ['js/model.js', 'js/data.js'],
  'js/cmd-icons.js': ['js/ui-icons.js', 'js/domestic-tiers.js'],
  'js/domestic-fx.js': ['js/cmd-icons.js', 'js/ui.js', 'js/city-view.js', 'js/music-stings.js'],
  'js/battle-controller.js': ['js/battle-model.js', 'js/duel-game.js', 'js/specials.js', 'js/tactics-data.js'],
  'js/menu-screen.js': ['js/ui.js', 'js/settings.js'],
  'js/settings-screen.js': ['js/settings.js', 'js/ui.js'],
  'js/generals-screen.js': ['js/ui.js'],
  'js/character-screen.js': ['js/ui.js', 'js/battle-math.js'],
  'js/shop-screen.js': ['js/ui.js', 'js/items.js'],
  'js/engage-screen.js': ['js/ui.js', 'js/engage.js'],
  'js/tourney-screen.js': ['js/ui.js', 'js/tourney.js'],
  'js/tourney-flow.js': ['js/tourney.js', 'js/tourney-screen.js', 'js/duel-game.js'],
  'js/strategy-screen.js': ['js/battle-controller.js', 'js/ui.js', 'js/shell.js', 'js/domestic-fx.js', 'js/tourney-flow.js', 'js/engage-screen.js'],
  'js/game.js': ['js/strategy-screen.js'],
};
// 第四版的规则层文件：不得引用 THREE / document（Node 可载入）
const V4_RULE_FILES = ['registry.js', 'econ.js', 'migrate-v4.js', 'battle-math.js', 'items-data.js', 'titles-data.js', 'engage-data.js', 'tourney-data.js', 'combo-data.js',
  'items.js', 'titles.js', 'engage.js', 'tourney.js', 'combos.js', 'settings.js', 'domestic-tiers.js', 'city-layout.js', 'data.js', 'model.js', 'strategy-ai.js'];

const scripts = indexScripts();
console.log('== index.html: ' + scripts.length + ' scripts');
const pos = new Map();
scripts.forEach((s, i) => { if (pos.has(s)) ok(false, 'duplicate script ' + s); pos.set(s, i); });
for (const s of scripts) ok(fs.existsSync(path.join(ROOT, s)), 'script exists: ' + s);
let edges = 0;
for (const [mod, deps] of Object.entries(DEPS)) {
  if (!pos.has(mod)) continue;
  for (const d of deps) {
    if (!pos.has(d)) continue;
    edges++;
    ok(pos.get(d) < pos.get(mod), d + ' loads before ' + mod);
  }
}
console.log('  ' + edges + ' dependency edges checked');
ok(scripts[scripts.length - 1] === 'js/game.js', 'game.js is the last script');
// every v4 rule file present must be in index.html, and every js file the page loads should be known to DEPS
for (const f of ['registry.js', 'econ.js', 'migrate-v4.js', 'battle-math.js']) ok(pos.has('js/' + f), 'W0 module in index.html: ' + f);
const unknown = scripts.filter(s => s.indexOf('js/') === 0 && !(s in DEPS));
if (unknown.length) console.log('  note: no dependency entry for ' + unknown.join(', '));
const notLoaded = fs.readdirSync(JS).filter(f => f.slice(-3) === '.js' && !pos.has('js/' + f));
if (notLoaded.length) console.log('  note: js files not loaded by index.html: ' + notLoaded.join(', '));

console.log('== rule files never reference views (THREE / document)');
for (const f of V4_RULE_FILES) {
  const p = path.join(JS, f);
  if (!fs.existsSync(p)) continue;
  const src = fs.readFileSync(p, 'utf8').replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
  ok(!/\bTHREE\./.test(src), f + ' does not use THREE');
  ok(!/\bdocument\./.test(src), f + ' does not use document');
}

console.log('== the rule layer loads in index.html order in a clean context');
try {
  const SG = load({ v4: true });
  console.log('  loaded: ' + SG.__files.join(' '));
  for (const k of ['Mods', 'Econ', 'AIBudget', 'MigrateV4', 'BattleMath', 'Settings', 'Clock', 'Events', 'Reg', 'GameState', 'StrategyAI', 'BattleModel']) ok(!!SG[k], 'SG.' + k + ' defined');
  ok(typeof SG.Mods.atkMul === 'function' && typeof SG.Mods.critChance === 'function', 'battle-math attached to SG.Mods');
  const order = SG.__files;
  ok(order.indexOf('econ.js') < order.indexOf('model.js') && order.indexOf('model.js') < order.indexOf('migrate-v4.js') && order.indexOf('migrate-v4.js') < order.indexOf('strategy-ai.js') && order.indexOf('strategy-ai.js') < order.indexOf('battle-math.js') && order.indexOf('battle-math.js') < order.indexOf('battle-model.js'), 'Node load order follows INTEGRATION §11');
  void RULES;
} catch (e) { ok(false, 'rule layer failed to load: ' + (e.stack || e)); }

// SG.Settings: js/registry.js defines the registry right after core.js so rows can be registered before js/settings.js
// (W8, UI-V4 §2) loads; settings.js must extend that object, never replace it (rows defined earlier would be lost).
console.log('== SG.Settings: rows registered before js/settings.js survive it (registry.js ↔ settings.js)');
{
  const sp = path.join(JS, 'settings.js');
  if (!fs.existsSync(sp)) console.log('  js/settings.js not landed yet (W8) — skipped');
  else {
    const vm = require('vm');
    const ctx = { console, Math, JSON, Date, setTimeout, clearTimeout };
    ctx.window = ctx; ctx.self = ctx;
    vm.createContext(ctx);
    try {
      for (const f of ['core.js', 'registry.js']) vm.runInContext(fs.readFileSync(path.join(JS, f), 'utf8'), ctx, { filename: f });
      const S0 = ctx.SG.Settings;
      S0.define({ id: 'loadorder.probe', tab: 'game', order: 999, label: 'probe', type: 'toggle', default: true, owner: 'test' });
      vm.runInContext(fs.readFileSync(sp, 'utf8'), ctx, { filename: 'settings.js' });
      const S1 = ctx.SG.Settings;
      ok(S1 === S0 || (typeof S1.rows === 'function' && S1.rows('game').some(d => d.id === 'loadorder.probe')), 'settings.js keeps the rows registered before it (extends SG.Settings, does not replace it)');
    } catch (e) { console.log('  note: js/settings.js could not be loaded without a DOM (' + (e.message || e) + ') — checked in tests/settings.js'); }
  }
}

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
