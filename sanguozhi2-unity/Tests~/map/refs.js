// 无头地图测试的参考值（第三版）：在 Node 中载入网页版（three.min.js + world-geo.js + map-view.js …），
// 对经典 / 世界两种构建范围完整构建一次地图，导出 C# 移植要逐项对照的数据：
//   城池表、投影、地理场、高度 / 实际地表、主网格 / 远景栅格（二进制）、数据纹理（二进制）、TerrainInfo 栅格取样、
//   城外村落、树木 / 石块 / 灌丛 / 村舍（逐个实例）、道路 / 桥 / 关隘、海上航线、地形块四叉树（预建到第 2 级）、
//   各类网格的三角形数、植被与各文化城池模型（近景 / 远景）的顶点数、程序化纹理的校验和。
// 用法：node Tests~/map/refs.js <输出目录>      （由 Tests~/map/run.sh 调用）
'use strict';
const fs = require('fs'), vm = require('vm'), path = require('path');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web');
const OUT = process.argv[2] || path.resolve(__dirname, '../out/map');
fs.mkdirSync(OUT, { recursive: true });
global.window = global; global.self = global;
// Canvas / document 桩（树木公告板与云的贴图在 Canvas 上绘制；参考值不比较这两张贴图）
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
const warn = console.warn; console.warn = () => {};   // three.min.js 的弃用提示
for (const f of ['vendor/three.min.js', 'js/core.js', 'js/world-geo.js', 'js/data.js', 'js/world-data.js', 'js/model.js', 'js/art.js', 'js/culture-art.js', 'js/map-view.js'])
  vm.runInThisContext(fs.readFileSync(path.join(WEB, f), 'utf8'), { filename: f });
console.warn = warn;
const SG = global.SG;
SG.Gfx.scene = new THREE.Scene();
// 构建结束时按镜头补建细块：放一个固定的镜头（C# 不比较这一部分）
SG.Gfx.camera = new THREE.PerspectiveCamera(34, 16 / 9, 0.5, 700);
SG.Gfx.camera.position.set(60, 80, 40); SG.Gfx.camera.lookAt(60, 0, -50);
const L = [];
const num = v => (typeof v === 'boolean' ? (v ? 1 : 0) : v === null || v === undefined ? '-' : String(v));
const line = (...a) => L.push(a.map(num).join(' '));
const fnv = arr => { let h = 0x811c9dc5; for (let i = 0; i < arr.length; i++) { h ^= arr[i]; h = Math.imul(h, 0x01000193) >>> 0; } return h >>> 0; };
const bin = (name, typed) => fs.writeFileSync(path.join(OUT, name), Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength));

// ---- 投影与范围 ----
for (let lon = -20; lon <= 150; lon += 7.3) for (let lat = -5; lat <= 70; lat += 4.1) {
  const p = SG.project(lon, lat), q = SG.unproject(p.x, p.y);
  line('proj', lon, lat, p.x, p.y, q.lon, q.lat);
}
const W = SG.World;
line('bounds', W.x0, W.y0, W.x1, W.y1);
for (const k in W.regions) { const r = W.regions[k]; line('region', k, r.xMin, r.yMin, r.xMax, r.yMax); }
// 程序化纹理（地形细节 A / B、涟漪）
{
  const d = SG.Gfx.terrainDetail();
  line('tex', 'detA', fnv(d.a.image.data)); line('tex', 'detB', fnv(d.b.image.data));
  bin('detA.bin', d.a.image.data); bin('detB.bin', d.b.image.data);
  line('tex', 'ripple', fnv(SG.Gfx.water().uniforms.uRipple.value.image.data));
}
// Perlin（网页版 core.js）
for (let k = 0; k < 400; k++) { const x = (k * 7.37) % 300 - 150 + k * 0.013, y = (k * 3.91) % 200 - 100 - k * 0.007; line('perlin', x, y, SG.M.perlinNoise(x, y)); }

// ---- 两种构建范围 ----
const t0 = Date.now();
for (const which of ['china', 'world']) {
  const g = which === 'world' ? SG.GameState.newGame(Object.keys(SG.FactionInfo)[0], 'world') : SG.GameState.newGame('liubei');
  SG.G = g;
  const mv = new SG.MapView();
  mv.build(g, { region: which });
  const G = mv._geo, T = mv.spec.terrain;
  line('spec', which, g.cities.length, mv.treeCount, mv.rockCount, mv.houseCount, mv._hamlets.length, mv.passes.length);
  for (const c of g.cities) line('city', which, c.id, c.key, c.lon, c.lat, c.town, c.culture || 'han', c.links.join(',') || '-');
  const seas = [];
  for (const c of g.cities) for (const li of c.links) if (c.id < li && SG.linkIsSea(c.id, li)) seas.push(c.id + '-' + li);
  line('sealinks', which, seas.length, ...seas);
  // 采样点：地形范围内的不规则网格（避开格点对齐）
  const sx = which === 'world' ? 9.37 : 3.71, sz = which === 'world' ? 9.13 : 3.53;
  for (let x = T.x0 + 0.37; x < T.x1; x += sx) for (let z = T.z0 + 0.29; z < T.z1; z += sz) {
    const coast = G.coastDist(x, z), sea = G.inSea(x, z), river = G.riverDist(x, z), rW = G.rW, rD = G.rD;
    const f = Array.from(G.sample(x, z)), ridge = G.ridges.sum(x, z), h = mv.height(x, z), sy = mv.surfaceY(x, z);
    line('pt', x, z, coast, sea, river, rW, rD, ...f, ridge, h, sy);
  }
  // TerrainInfo（战场地形的取样源）：可玩范围内的不规则网格
  const B = mv.bounds, tx = which === 'world' ? 7.13 : 2.37, tz = which === 'world' ? 6.91 : 2.29;
  for (let x = B.xMin + 0.21; x < B.xMax; x += tx) for (let z = B.yMin + 0.17; z < B.yMax; z += tz) {
    const t = mv.terrainInfo(x, z);
    line('ti', x, z, t.h, t.slope, t.water, t.depth, t.forest, t.farm, t.sand, t.snow, t.rock, t.biome, t.river, t.coast, t.road);
  }
  // 主网格 / 远景栅格（Float32 小端）与数据纹理
  const m = mv._master, o = mv._outer;
  line('master', which, m.x0, m.z0, m.res, m.nx, m.nz, o.x0, o.z0, o.res, o.nx, o.nz);
  bin('mh-' + which + '.bin', m.h); bin('oh-' + which + '.bin', o.h);
  line('texsize', which, mv._tex.w, mv._tex.h);
  for (const k of ['n', 'c', 'm', 'oc', 'om']) bin('tex' + k + '-' + which + '.bin', mv._maps[k].image.data);
  for (let i = 0; i < g.cities.length; i++) line('citybase', which, i, mv._cityBase[i]);
  // 城外村落
  for (const h of mv._hamlets) line('hamlet', which, h.x, h.z, h.culture, h.n, h.yaw, h.city);
  // 树木（从实例矩阵读回：位置、缩放、颜色、树种）
  const dec = (mats, i) => {
    const e = mats.subarray(i * 16, i * 16 + 16);
    return [e[12], e[13], -e[14], Math.hypot(e[0], e[1], e[2]), Math.hypot(e[4], e[5], e[6]), Math.atan2(e[8], e[0])];
  };
  for (const t of mv._treeTiles) for (let i = 0; i < t.n; i++) {
    const d = dec(t.mats, i);
    line('tree', which, t.kinds[t.kind[i]], ...d.slice(0, 5), t.cols[i * 3], t.cols[i * 3 + 1], t.cols[i * 3 + 2]);
  }
  // 石块 / 灌丛 / 村舍：类别 = 网格名，种类签名 = 近景几何的顶点数
  for (const t of mv._scatter) for (let i = 0; i < t.n; i++) {
    const d = dec(t.mats, i);
    line('scatter', which, t.near.name, t.near.geometry.getAttribute('position').count, ...d.slice(0, 5), t.cols[i * 3], t.cols[i * 3 + 1], t.cols[i * 3 + 2]);
  }
  // 植被几何（近景 lod 0 / 远景 lod 1 / 特写 lod 2）的顶点数
  {
    const t = mv._treeTiles[0];
    if (t) mv._treeTileLOD(t, false, 42, 'refs', t.cx, 2, t.cz);   // 镜头贴近区块中心：建出特写网格
    const seen = new Set();
    for (const tt of mv._treeTiles) tt.near.forEach((im, k) => {
      const key = tt.kinds[k];
      if (seen.has(key)) return;
      seen.add(key);
      line('veg', 'tree', key, 0, im.geometry.getAttribute('position').count);
    });
    if (t) t.hero.forEach((im, k) => { if (im) line('veg', 'tree', t.kinds[k], 2, im.geometry.getAttribute('position').count); });
    const sc = new Set();
    for (const st of mv._scatter) {
      const k = st.near.name + ':' + st.near.geometry.getAttribute('position').count;
      if (sc.has(k)) continue;
      sc.add(k);
      line('veg', st.near.name, st.near.geometry.getAttribute('position').count, 1, st.far.geometry.getAttribute('position').count);
    }
    line('veg', 'grass', 'tuft', 0, mv._grassT.geometry.getAttribute('position').count);
    line('veg', 'grass', 'flower', 0, mv._flowerT.geometry.getAttribute('position').count);
  }
  // 道路、桥、关隘
  line('roads', which, mv._roadLines.length, mv._roadLines.reduce((s, l) => s + l.n, 0), mv._roadSegs.length / 4);
  for (const p of mv.passes) line('pass', which, p.name, p.x, p.z, p.y);
  line('passskip', which, mv.passesSkipped.length, ...mv.passesSkipped);
  // 地形块四叉树：新建一份，预建到第 2 级（与构建时相同），统计各级已建 / 空块
  {
    const lod = new (mv._lod.constructor)(mv);
    lod.mat = mv._lod.mat;
    const S = mv.spec.skirt, X1 = T.x0 + Math.ceil((T.x1 - T.x0) / T.step) * T.step, Z1 = T.z0 + Math.ceil((T.z1 - T.z0) / T.step) * T.step;
    lod.init(T.x0, T.z0, { x0: S.x0, z0: S.z0, x1: S.x1, z1: S.z1 }, { x0: T.x0, z0: T.z0, x1: X1, z1: Z1 });
    for (const _ of lod.prebuild(2)) { /* 同步 */ }
    const st = {};
    const rec = nd => { const k = nd.level + ':' + nd.state; st[k] = (st[k] || 0) + 1; if (nd.children) nd.children.forEach(rec); };
    lod.roots.forEach(rec);
    line('lod', which, lod.roots.length, ...Object.keys(st).sort().map(k => k + '=' + st[k]));
    // 若干块的顶点高度（裙边除外）校验和
    let chk = 0, nn = 0;
    const visit = nd => { if (nd.mesh) { const P = nd.mesh.geometry.getAttribute('position').array; for (let i = 1; i < 33 * 33 * 3; i += 3 * 37) { chk += P[i]; nn++; } } if (nd.children) nd.children.forEach(visit); };
    lod.roots.forEach(visit);
    line('lodsum', which, nn, chk);
    lod.dispose();
  }
  // 各类网格的三角形数（按对象名累计；实例化网格 = 几何 × 实例数）
  const tris = {};
  mv.root.traverse(o => {
    const gg = o.geometry, pos = gg && gg.getAttribute && gg.getAttribute('position');
    if (!pos) return;
    const n = (gg.index ? gg.index.count : pos.count) / 3 * (o.isInstancedMesh ? o.count : 1);
    tris[o.name] = (tris[o.name] || 0) + n;
  });
  for (const k in tris) line('mesh', which, k, tris[k]);
  // 海上航线
  for (const [key, pts] of mv._lanes) line('lane', which, key, pts.length / 2, ...pts);
}
// 各文化的城池模型顶点数（size 1.2；普通 / 都城；近景 cityInto / 远景 cityFarInto）
const MB = () => new SG.MeshBuilder();
for (const cu of SG.CultureArt.cultures.concat(['han']))
  for (const cap of [false, true]) {
    const c = { key: 'test', town: 300 };
    line('culture', cu, cap, SG.CultureArt.cityInto(MB(), 1.2, cu, cap, c).count, SG.CultureArt.cityFarInto(MB(), 1.2, cu, cap).count);
  }
for (const [tag, cu, key, town] of [['arab-aksum', 'arab', 'aksum', 300], ['sarmatian-big', 'sarmatian', 'test', 400], ['africa-kush', 'africa', 'meroe', 300], ['africa-aksum', 'africa', 'aksum', 300]])
  for (const cap of [false, true]) line('culture', tag, cap, SG.CultureArt.cityInto(MB(), 1.2, cu, cap, { key, town }).count, SG.CultureArt.cityFarInto(MB(), 1.2, cu, cap).count);
// 平滑网格工具（MeshBuilder）与常用模型的顶点数
{
  const v = (x, y, z) => new THREE.Vector3(x, y, z);
  const one = f => { const mb = MB(); f(mb); return mb.count; };
  line('mb', 'sphere0', one(mb => mb.sphere(v(0, 0, 0), 1, new THREE.Color(1, 1, 1), 0)));
  line('mb', 'sphere1', one(mb => mb.sphere(v(0, 0, 0), v(1, 2, 1), new THREE.Color(1, 1, 1), 1, { rough: 0.1, seed: 3 })));
  line('mb', 'sphere2', one(mb => mb.sphere(v(0, 0, 0), 1, new THREE.Color(1, 1, 1), 2)));
  line('mb', 'lathe', one(mb => mb.lathe(v(0, 0, 0), [[0.5, 0], [0.4, 1, null, true], [0, 1.4]], 12, new THREE.Color(1, 1, 1), { capTop: true, capBottom: true })));
  line('mb', 'tube', one(mb => mb.tube([v(0, 0, 0), v(0.1, 0.5, 0), v(0.2, 1, 0.1)], [0.1, 0.08, 0.05], 6, new THREE.Color(1, 1, 1), true)));
  line('mb', 'roof', one(mb => mb.chineseRoof(v(0, 0, 0), 1.0, 0.6, 0.32, new THREE.Color(0.3, 0.3, 0.4))));
  line('mb', 'roofSmall', one(mb => mb.chineseRoof(v(0, 0, 0), 0.4, 0.3, 0.2, new THREE.Color(0.3, 0.3, 0.4))));
  line('mb', 'tree', one(mb => SG.Models.tree(mb, v(0, 0, 0), 1, new THREE.Color(0.3, 0.5, 0.2), false, 3)));
  line('mb', 'pine', one(mb => SG.Models.tree(mb, v(0, 0, 0), 1, new THREE.Color(0.3, 0.5, 0.2), true, 3)));
  line('mb', 'city', one(mb => SG.Models.cityInto(mb, 1.2, null, true)));
  // 球面顶点位置校验（rough + smoothN）
  const mb = MB(); mb.sphere(v(0.5, 1, -0.2), v(0.4, 0.3, 0.5), new THREE.Color(0.5, 0.6, 0.3), 1, { seed: 7, rough: 0.12, top: 0.1, bottomShade: 0.3, smoothN: 0.5 });
  let sp = 0, sn = 0, sc = 0;
  for (let i = 0; i < mb.pos.length; i += 3) { sp += mb.pos[i] + 2 * mb.pos[i + 1] - 3 * mb.pos[i + 2]; sn += mb.nor[i] + 2 * mb.nor[i + 1] - 3 * mb.nor[i + 2]; sc += mb.col[i] + 2 * mb.col[i + 1] + 3 * mb.col[i + 2]; }
  line('mbsum', 'sphere', mb.count, sp, sn, sc);
}
fs.writeFileSync(path.join(OUT, 'refs.txt'), L.join('\n') + '\n');
console.log('[refs] ' + L.length + ' 行，' + (Date.now() - t0) + ' ms → ' + OUT);
process.exit(0);
