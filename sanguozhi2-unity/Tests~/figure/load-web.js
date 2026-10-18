// 在 Node 中载入网页版人物模型库（vendor/three.min.js + core / data / world-data / portrait-data / portrait / figure）。
// Canvas 用桩（几何与骨架不依赖贴图内容；图集区域只用到布局），供 refs.js 等无头脚本 require。
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
global.window = global; global.self = global;
(function () {
  const noop = () => {};
  const grad = { addColorStop: noop };
  const ctx = cv => new Proxy({
    canvas: cv,
    getImageData: (x, y, w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }),
    createRadialGradient: () => grad, createLinearGradient: () => grad, measureText: () => ({ width: 10 }),
  }, { get(t, k) { return k in t ? t[k] : noop; }, set(t, k, v) { t[k] = v; return true; } });
  global.document = { createElement: () => { const cv = { width: 1, height: 1, style: {} }; cv.getContext = () => ctx(cv); return cv; }, readyState: 'complete', currentScript: null, head: { insertAdjacentHTML: noop }, addEventListener: noop };
})();
const warn = console.warn; console.warn = () => {};
for (const f of ['vendor/three.min.js', 'js/core.js', 'js/data.js', 'js/world-data.js', 'js/portrait-data.js', 'js/portrait.js', 'js/figure.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, f), 'utf8'), { filename: f });
console.warn = warn;
const SG = global.SG;
// 武将数据：与 tests/figures.html 相同（世界剧本的 name / war / intel / pol / culture / 势力色）
const W = SG.Scenarios.world, GEN = {}, facColor = {};
for (const l of W.Factions) { const p = l.split('|'); facColor[p[0]] = p[3]; }
for (const l of W.Generals) { const p = l.split('|'); GEN[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], culture: p[9] || 'han', color: facColor[p[4]] || '#8a8a8a' }; }
const gen = n => GEN[n] || { name: n, war: 80, intel: 60, pol: 50 };
module.exports = { SG, THREE: global.THREE, gen, WEB };
