'use strict';
/* ==========================================================================
   第四版 Node 测试的公共载入器（W0）
   按 index.html 的 <script> 次序载入「规则层」文件（纯逻辑、无 THREE / DOM）到一个新的 vm 上下文，
   不存在的文件（尚未落地的工作流）自动跳过；视图文件一律不载入（INTEGRATION §11）。

   const { load, RULES, indexScripts } = require('./v4-load.js');
   const SG = load({ v4: true })           // 第四版全套规则（有的都载入）
   const SG = load({ v4: false })          // 只载入第三版的规则文件（core、data、model、strategy-ai、battle-model …）
   const SG = load({ files: [...] })       // 指定文件（按给出的次序）
   选项：extra: [file]（另加，按 index.html 次序插入）、skip: [file]、global: true（载入到 global 而非新上下文）、
         alt: dir（同名文件优先从 alt 目录读，用于与旧版本对照）
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.join(__dirname, '..');
const JS = path.join(ROOT, 'js');

// 规则层文件（Node 可载入）。新工作流的纯逻辑文件也在这里登记（INTEGRATION §11 的前四行 + 战斗规则 + 必杀 / 计策数据）。
const RULES = [
  'core.js', 'registry.js', 'world-geo.js', 'data.js', 'world-data.js',
  'items-data.js', 'titles-data.js', 'engage-data.js', 'tourney-data.js', 'combo-data.js',
  'econ.js',
  'model.js', 'items.js', 'titles.js', 'engage.js', 'tourney.js', 'combos.js', 'migrate-v4.js',
  'strategy-ai.js', 'battle-math.js', 'battle-model.js',
  'specials-data.js', 'specials.js', 'tactics-data.js',
  'settings.js', 'domestic-tiers.js', 'city-layout.js',
];
const V3_RULES = ['core.js', 'world-geo.js', 'data.js', 'world-data.js', 'model.js', 'strategy-ai.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js'];

// index.html 里 <script src="js/…"> 的次序（不含 vendor）
function indexScripts() {
  const html = fs.readFileSync(path.join(ROOT, 'index.html'), 'utf8');
  const out = [];
  const re = /<script\s+[^>]*src="([^"]+)"[^>]*>\s*<\/script>/g;
  let m;
  while ((m = re.exec(html))) out.push(m[1]);
  return out;
}

function orderFiles(wanted) {
  const idx = indexScripts().filter(s => s.indexOf('js/') === 0).map(s => s.slice(3));
  const pos = f => { const i = idx.indexOf(f); return i >= 0 ? i : 1000 + RULES.indexOf(f); };
  return wanted.slice().sort((a, b) => pos(a) - pos(b) || RULES.indexOf(a) - RULES.indexOf(b));
}

function load(opts) {
  opts = opts || {};
  let files;
  if (opts.files) files = opts.files.slice();
  else {
    const base = opts.v4 === false ? V3_RULES : RULES;
    files = orderFiles(base.concat(opts.extra || []).filter((f, i, a) => a.indexOf(f) === i && fs.existsSync(path.join(JS, f))));
  }
  if (opts.skip) files = files.filter(f => opts.skip.indexOf(f) < 0);
  let ctx;
  if (opts.global) { global.window = global; ctx = null; }
  else {
    ctx = { console, Math, JSON, Date, setTimeout, clearTimeout, performance: typeof performance !== 'undefined' ? performance : { now: () => Date.now() } };
    ctx.window = ctx; ctx.global = ctx;
    vm.createContext(ctx);
  }
  for (const f of files) {
    const p = opts.alt && fs.existsSync(path.join(opts.alt, f)) ? path.join(opts.alt, f) : path.join(JS, f);
    const src = fs.readFileSync(p, 'utf8');
    if (ctx) vm.runInContext(src, ctx, { filename: f }); else vm.runInThisContext(src, { filename: f });
  }
  const SG = ctx ? ctx.SG : global.SG;
  Object.defineProperty(SG, '__files', { value: files, enumerable: false, configurable: true });
  Object.defineProperty(SG, '__ctx', { value: ctx, enumerable: false, configurable: true });
  return SG;
}

module.exports = { load, RULES, V3_RULES, indexScripts, ROOT, JS };
