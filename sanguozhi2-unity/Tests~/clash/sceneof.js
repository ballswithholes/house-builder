// 攻击画面的布景取自战场（js/clash.js 的 fromBattle(...).scene = sceneOfBattle）的网页版参考：在 Node 中载入网页版
// （three.min.js + world-geo.js + map-view.js + battle-terrain.js + clash.js，Canvas / document 用桩），经典 / 世界两个剧本各建一次
// 战略地图，为每座城池的第一条连线与全部海路连线（来向 = 连线另一端的城）生成战场，在攻方布阵格（对守方第一个布阵格）与
// 每隔一格的格点（对右邻格）上取布景（攻方汉、守方南蛮，各 1000 兵）：
//   scenario 行：剧本、世界剧本的势力键（C# 照此开新局）
//   S 行：剧本 城 来向城 海路 攻x 攻y 守x 守y → 文化 群落（undefined = 无）海岸（0 / 1）
// 用法：node Tests~/clash/sceneof.js <输出目录>      （由 Tests~/clash/run.sh 调用；写 sceneof.txt）
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
const OUT = process.argv[2] || path.resolve(__dirname, '../out/clash');
fs.mkdirSync(OUT, { recursive: true });
global.window = global; global.self = global;
(function () {
  const noop = () => {};
  const grad = { addColorStop: noop };
  const ctx = cv => new Proxy({
    canvas: cv,
    createImageData: (w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }),
    getImageData: (x, y, w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }),
    createRadialGradient: () => grad, createLinearGradient: () => grad, createPattern: () => ({}), measureText: () => ({ width: 10 }),
  }, { get(t, k) { return k in t ? t[k] : noop; }, set(t, k, v) { t[k] = v; return true; } });
  global.document = { createElement: () => { const cv = { width: 1, height: 1, style: {} }; cv.getContext = () => ctx(cv); return cv; }, readyState: 'complete', currentScript: null, hidden: true };
})();
global.setTimeout = () => 0;
const warn = console.warn; console.warn = () => {};
for (const f of ['vendor/three.min.js', 'js/core.js', 'js/world-geo.js', 'js/data.js', 'js/world-data.js', 'js/model.js', 'js/art.js', 'js/culture-art.js', 'js/map-view.js', 'js/battle-terrain.js', 'js/clash.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, f), 'utf8'), { filename: f });
console.warn = warn;
const SG = global.SG, BT = SG.BattleTerrain;
SG.Gfx.scene = new THREE.Scene();
SG.Gfx.camera = new THREE.PerspectiveCamera(34, 16 / 9, 0.5, 700);
SG.Gfx.camera.position.set(60, 80, 40); SG.Gfx.camera.lookAt(60, 0, -50);
const L = [];
let nMaps = 0, nCoast = 0, nLines = 0;
for (const which of ['china', 'world']) {
  const fk = Object.keys(SG.FactionInfo)[0];
  const g = which === 'world' ? SG.GameState.newGame(fk, 'world') : SG.GameState.newGame('liubei');
  SG.G = g;
  const mv = new SG.MapView();
  mv.build(g, { region: which });
  L.push(['scenario', which, fk].join(' '));
  for (const c of g.cities) {
    const links = c.links.filter((li, i) => i === 0 || SG.linkIsSea(li, c.id));
    for (const li of links) {
      const from = g.cities[li];
      const sea = SG.linkIsSea(from.id, c.id);
      const T = BT.generate({ city: c, fromCity: from, sea, view: mv });
      if (!T) continue;
      nMaps++;
      const model = { terrain: T, map: T.map };
      const pairs = [];
      for (const a of T.atkTiles) pairs.push([a.x, a.y, T.defTiles[0].x, T.defTiles[0].y]);
      for (let x = 0; x < T.W; x += 2) for (let y = 0; y < T.H; y += 2) pairs.push([x, y, Math.min(T.W - 1, x + 1), y]);
      for (const [ax, ay, tx, ty] of pairs) {
        const a = { x: ax, y: ay, side: 0, gen: { name: 'A', culture: 'han' }, troops: 1000 };
        const t = { x: tx, y: ty, side: 1, gen: { name: 'D', culture: 'nanman' }, troops: 1000 };
        const o = SG.Clash.fromBattle(model, a, t, 1000, 1000);
        const s = o.scene;
        if (s.coast) nCoast++;
        L.push(['S', which, c.id, from.id, sea ? 1 : 0, ax, ay, tx, ty, s.culture, s.biome === undefined ? 'undefined' : s.biome, s.coast ? 1 : 0].join(' '));
        nLines++;
      }
    }
  }
}
fs.writeFileSync(path.join(OUT, 'sceneof.txt'), L.join('\n') + '\n');
console.log('maps', nMaps, 'lines', nLines, 'coast', nCoast);
process.exit(0);
