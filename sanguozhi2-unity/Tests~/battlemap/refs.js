// 战场地形（第三版 §2，js/battle-terrain.js）的无头参考值：在 Node 中载入网页版（three.min.js + world-geo.js + map-view.js …
// + battle-terrain.js），对经典 / 世界两个剧本完整构建一次战略地图，为全部（城池, 来向）生成战场并导出 C# 移植要逐项对照的结果：
//   每座城池 × 每条连线（来向 = 出发城；海路连线 sea = true）+ 缺省来向（无出发城：自西而来）：
//     T 行：角度、转向、压缩系数、城池地面高度、主要群落、文化、种子、notes
//     A 行：ascii（上 = 棋盘 y 大的一侧，行之间以 / 分隔）；B 行：桥（x,y,轴）；P 行：城门、攻方 / 守方布阵格
//     G 行：取样栅格的尺寸与各字段（Float32 / Uint8 的字节）的 FNV-1a；Q 行：各字段量化到 1e-6 后的 FNV-1a；
//     S 行：height / farm 的和与道路格数
//   每座城池的 rawAscii（世界地图窗口，北在上、不旋转，用实际地表）。
//   城池表（编号、键、经纬度、规模、文化、连线、海路）供 C# 核对剧本数据一致。
// 用法：node Tests~/battlemap/refs.js <输出目录>      （由 Tests~/battlemap/run.sh 调用）
//   BM_DUMP=china:22:21,world:5:-1 …：另把这几张战场的取样栅格（各字段依次为 Float32 / Uint8 的字节）写成 grid-<剧本>-<城>-<来>.bin，
//   C# 在栅格 FNV 不一致时据此报告各字段的最大差与不同的取样点数（诊断用）
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
const OUT = process.argv[2] || path.resolve(__dirname, '../out/battlemap');
fs.mkdirSync(OUT, { recursive: true });
global.window = global; global.self = global;
// Canvas / document 桩（同 Tests~/map/refs.js）
(function () {
  const noop = () => {};
  const grad = { addColorStop: noop };
  const ctx = cv => new Proxy({
    canvas: cv,
    createImageData: (w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }),
    getImageData: (x, y, w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }),
    createRadialGradient: () => grad, createLinearGradient: () => grad, createPattern: () => ({}), measureText: () => ({ width: 10 }),
  }, { get(t, k) { return k in t ? t[k] : noop; }, set(t, k, v) { t[k] = v; return true; } });
  global.document = { createElement: () => { const cv = { width: 1, height: 1, style: {} }; cv.getContext = () => ctx(cv); return cv; }, readyState: 'complete', currentScript: null };
})();
const warn = console.warn; console.warn = () => {};
for (const f of ['vendor/three.min.js', 'js/core.js', 'js/world-geo.js', 'js/data.js', 'js/world-data.js', 'js/model.js', 'js/art.js', 'js/culture-art.js', 'js/map-view.js', 'js/battle-terrain.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, f), 'utf8'), { filename: f });
console.warn = warn;
const SG = global.SG, BT = SG.BattleTerrain;
SG.Gfx.scene = new THREE.Scene();
SG.Gfx.camera = new THREE.PerspectiveCamera(34, 16 / 9, 0.5, 700);
SG.Gfx.camera.position.set(60, 80, 40); SG.Gfx.camera.lookAt(60, 0, -50);
const L = [];
const fnvBytes = typed => { const b = new Uint8Array(typed.buffer, typed.byteOffset, typed.byteLength); let h = 0x811c9dc5; for (let i = 0; i < b.length; i++) { h ^= b[i]; h = Math.imul(h, 0x01000193) >>> 0; } return h >>> 0; };
const DUMP = new Set((process.env.BM_DUMP || '').split(',').filter(x => x));
const FIELDS = ['h', 'slope', 'water', 'forest', 'farm', 'sand', 'snow', 'biome', 'road', 'river', 'coast'];
const t0 = Date.now();
let nMaps = 0, nAll = 0, tGen = 0;   // nMaps = 计时的连线来向战场；nAll = 导出的全部战场（含缺省来向与其余参数形式）
for (const which of ['china', 'world']) {
  const g = which === 'world' ? SG.GameState.newGame(Object.keys(SG.FactionInfo)[0], 'world') : SG.GameState.newGame('liubei');
  SG.G = g;
  const tb = Date.now();
  const mv = new SG.MapView();
  mv.build(g, { region: which });
  L.push(['scenario', which, g.cities.length, Object.keys(SG.FactionInfo)[0], Date.now() - tb].join(' '));
  for (const c of g.cities) {
    const sea = c.links.filter(li => SG.linkIsSea(c.id, li));
    L.push(['city', which, c.id, c.key, c.lon, c.lat, c.town, c.culture || '-', c.links.join(',') || '-', sea.join(',') || '-'].join(' '));
  }
  const dump = (c, from, T) => {
    const key = which + ' ' + c.id + ' ' + (from ? from.id : -1);
    nAll++;
    if (!T) { L.push('T ' + key + ' null'); return; }
    L.push(['T', key, T.angle, T.turned, T.kAtt, T.kNeed, T.h0, T.biome, T.culture, T.seed, T.W, T.H, T.castle.x, T.castle.y, T.dir.x, T.dir.y, T.notes.join(',') || '-'].join(' '));
    L.push('A ' + key + ' ' + BT.ascii(T).split('\n').join('/'));
    L.push('B ' + key + ' ' + (T.bridges.map(b => b.x + ',' + b.y + ',' + b.axis).join(';') || '-'));
    const pl = a => a.map(q => q.x + ',' + q.y).join(';') || '-';
    L.push('P ' + key + ' ' + pl(T.gates) + ' ' + pl(T.atkTiles) + ' ' + pl(T.defTiles));
    const G = T.grid;
    L.push(['G', key, G.nu, G.nv, G.u0, G.v0, G.step].concat(FIELDS.map(k => fnvBytes(G[k]))).join(' '));
    // 量化到 1e-6 的 FNV（Int32 = round(v × 1e6)）：不受末位舍入（如 Math.hypot 与 √(x² + y²) 的 1 ulp 之差）影响
    L.push(['Q', key].concat(FIELDS.map(k => { const a = G[k], q = new Int32Array(a.length); for (let i = 0; i < a.length; i++) q[i] = Math.round(a[i] * 1e6); return fnvBytes(q); })).join(' '));
    if (DUMP.has(key.split(' ').join(':'))) fs.writeFileSync(path.join(OUT, 'grid-' + key.split(' ').join('-') + '.bin'), Buffer.concat(FIELDS.map(k => Buffer.from(G[k].buffer, G[k].byteOffset, G[k].byteLength))));
    let hs = 0, fs2 = 0, rd = 0;
    for (let x = 0; x < T.W; x++) for (let y = 0; y < T.H; y++) { hs += T.height[x][y]; fs2 += T.farm[x][y]; if (T.road[x][y]) rd++; }
    L.push(['S', key, hs, fs2, rd].join(' '));
  };
  for (const c of g.cities) {
    for (const li of c.links) {
      const from = g.cities[li];
      const ts = Date.now();
      const T = BT.generate({ city: c, fromCity: from, sea: SG.linkIsSea(from.id, c.id), view: mv });
      tGen += Date.now() - ts; nMaps++;
      dump(c, from, T);
    }
    dump(c, null, BT.generate({ city: c, view: mv }));
    // 其余参数形式（每 7 座城取一座）：来向为方位角（度）/ 向量、城池为地图坐标 {x, y}（无编号：种子取自坐标）
    if (c.id % 7 === 0) {
      const P = SG.mapPos(c);
      dump(c, { id: -2 }, BT.generate({ city: c, fromDir: 37, view: mv }));
      dump(c, { id: -3 }, BT.generate({ city: c, fromDir: { x: -1, y: 2 }, view: mv }));
      dump(c, { id: -4 }, BT.generate({ city: { x: P.x, y: P.y }, fromDir: 200, sea: true, view: mv }));
    }
    L.push('R ' + which + ' ' + c.id + ' ' + BT.rawAscii(mv, c).split('\n').join('/'));
  }
}
fs.writeFileSync(path.join(OUT, 'refs.txt'), L.join('\n') + '\n');
console.log('[refs] ' + L.length + ' 行，' + nAll + ' 张战场（其中连线来向 ' + nMaps + ' 张，平均生成 ' + (tGen / Math.max(1, nMaps)).toFixed(1) + ' ms），共 ' + (Date.now() - t0) + ' ms → ' + OUT);
process.exit(0);
