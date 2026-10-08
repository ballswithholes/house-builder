// 无头地图测试的参考值：在 Node 中载入网页版（three.min.js + world-geo.js + map-view.js …），
// 对经典 / 世界两种构建范围完整构建一次地图，导出投影、地理场、高度、地表颜色、地形网格、树木与海上航线。
// 用法：node Tests~/map/refs.js <输出目录>      （由 Tests~/map/run.sh 调用）
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
const OUT = process.argv[2] || path.resolve(__dirname, '../out/map');
fs.mkdirSync(OUT, { recursive: true });
global.window = global; global.self = global;
const warn = console.warn; console.warn = () => {};   // three.min.js 的弃用提示
for (const f of ['vendor/three.min.js', 'js/core.js', 'js/world-geo.js', 'js/data.js', 'js/world-data.js', 'js/model.js', 'js/art.js', 'js/culture-art.js', 'js/map-view.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, f), 'utf8'), { filename: f });
console.warn = warn;
const SG = global.SG;
SG.Gfx.scene = new THREE.Scene();
const L = [];
const num = v => (typeof v === 'boolean' ? (v ? 1 : 0) : String(v));
const line = (...a) => L.push(a.map(num).join(' '));

// ---- 投影与范围 ----
for (let lon = -20; lon <= 150; lon += 7.3) for (let lat = -5; lat <= 70; lat += 4.1) {
  const p = SG.project(lon, lat), q = SG.unproject(p.x, p.y);
  line('proj', lon, lat, p.x, p.y, q.lon, q.lat);
}
const W = SG.World;
line('bounds', W.x0, W.y0, W.x1, W.y1);
for (const k in W.regions) { const r = W.regions[k]; line('region', k, r.xMin, r.yMin, r.xMax, r.yMax); }

// ---- 两种构建范围 ----
const t0 = Date.now();
for (const which of ['china', 'world']) {
  const g = which === 'world' ? SG.GameState.newGame(Object.keys(SG.FactionInfo)[0], 'world') : SG.GameState.newGame('liubei');
  SG.G = g;
  const mv = new SG.MapView();
  mv.build(g, { region: which });
  const G = mv._geo, T = mv.spec.terrain;
  line('spec', which, g.cities.length, mv.treeCount);
  // 采样点：地形范围内的不规则网格（避开格点对齐）
  const sx = which === 'world' ? 9.37 : 3.71, sz = which === 'world' ? 9.13 : 3.53;
  for (let x = T.x0 + 0.37; x < T.x1; x += sx) for (let z = T.z0 + 0.29; z < T.z1; z += sz) {
    const coast = G.coastDist(x, z), sea = G.inSea(x, z), river = G.riverDist(x, z), rW = G.rW, rD = G.rD;
    const f = Array.from(G.sample(x, z)), ridge = G.ridges.sum(x, z), h = mv.height(x, z), sy = mv.surfaceY(x, z);
    line('pt', x, z, coast, sea, river, rW, rD, ...f, ridge, h, sy);
  }
  // 地表颜色（按网页版的岩石 / 雪色）：高度、坡度取若干组合
  let k = 0;
  for (let x = T.x0 + 1.1; x < T.x1; x += sx * 1.7) for (let z = T.z0 + 0.7; z < T.z1; z += sz * 1.7) {
    const h = [-1, 0.2, 0.8, 1.6, 2.9, 4.2, 6.6, 8.1][k % 8], d = [0, 0.004, 0.012, 0.03][(k >> 3) % 4], jit = [0, 0.03, -0.03][k % 3];
    // groundColor 是模块内部函数：经 _terrainTri 取得（写入的颜色即 groundColor 的结果）。
    // 三角形 a(x−e, h+d, z−e) b(x−e, h−d, z+2e) c(x+2e, h, z−e)：重心 = (x, h, z)，坡度随 d / e 变化
    const P = new Float32Array(9), N = new Float32Array(9), C = new Float32Array(9);
    const rnd = { nextDouble: () => jit / 0.06 + 0.5 };
    const e = 0.01;
    mv._terrainTri(P, N, C, 0, G, rnd, x - e, h + d, z - e, x - e, h - d, z + 2 * e, x + 2 * e, h, z - e, false);
    line('color', x, z, h, d, jit, C[0], C[1], C[2]);
    k++;
  }
  // 地形网格高度（Float32 小端）
  const gr = mv._grid;
  fs.writeFileSync(path.join(OUT, 'hs-' + which + '.bin'), Buffer.from(gr.hs.buffer, gr.hs.byteOffset, gr.hs.byteLength));
  line('grid', which, gr.x0, gr.z0, gr.step, gr.nx, gr.nz);
  // 水面网格的高度（Float32 小端，i 主序）
  {
    const Wt = mv.spec.water, st = Wt.step, wnx = Math.ceil((Wt.x1 - Wt.x0) / st), wnz = Math.ceil((Wt.z1 - Wt.z0) / st);
    const hw = new Float32Array((wnx + 1) * (wnz + 1));
    for (let i = 0; i <= wnx; i++) for (let j = 0; j <= wnz; j++) hw[i * (wnz + 1) + j] = mv.height(Wt.x0 + i * st, Wt.z0 + j * st);
    fs.writeFileSync(path.join(OUT, 'hw-' + which + '.bin'), Buffer.from(hw.buffer));
  }
  // 树木（从实例矩阵读回位置与缩放，按 x, z 排序后比对集合）
  const trees = [];
  const m4 = new THREE.Matrix4(), tp = new THREE.Vector3(), tq = new THREE.Quaternion(), ts = new THREE.Vector3();
  mv.root.traverse(o => {
    if (!o.isInstancedMesh || o.name !== 'Trees') return;
    for (let i = 0; i < o.count; i++) { o.getMatrixAt(i, m4); m4.decompose(tp, tq, ts); trees.push([tp.x, tp.y, -tp.z, ts.x, ts.y]); }
  });
  trees.sort((a, b) => a[0] - b[0] || a[2] - b[2]);
  for (const t of trees) line('tree', which, ...t);
  // 各类网格的三角形数（按对象名累计；实例化网格 = 几何 × 实例数）
  const tris = {};
  mv.root.traverse(o => {
    const g = o.geometry, pos = g && g.getAttribute && g.getAttribute('position');
    if (!pos) return;
    const n = (g.index ? g.index.count : pos.count) / 3 * (o.isInstancedMesh ? o.count : 1);
    tris[o.name] = (tris[o.name] || 0) + n;
  });
  for (const k in tris) line('mesh', which, k, tris[k]);
  // 海上航线
  for (const [key, pts] of mv._lanes) line('lane', which, key, pts.length / 2, ...pts);
}
// 各文化的城池模型顶点数（size 1.2；普通 / 都城；阿克苏姆石碑；萨尔马提亚大城 = 希腊城）
for (const cu of SG.CultureArt.cultures.concat(['han']))
  for (const cap of [false, true]) {
    const g = SG.CultureArt.city(1.2, cu, cap, { key: 'test', town: 300 });
    line('culture', cu, cap, g.getAttribute('position').count);
  }
line('culture', 'arab-aksum', false, SG.CultureArt.city(1.2, 'arab', false, { key: 'aksum', town: 300 }).getAttribute('position').count);
line('culture', 'sarmatian-big', false, SG.CultureArt.city(1.2, 'sarmatian', false, { key: 'test', town: 400 }).getAttribute('position').count);
fs.writeFileSync(path.join(OUT, 'refs.txt'), L.join('\n') + '\n');
console.log('[refs] ' + L.length + ' 行，' + (Date.now() - t0) + ' ms → ' + OUT);
process.exit(0);
