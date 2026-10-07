'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战略地图
   ← View/MapView.cs：程序化生成的低多边形地形（海岸线、河流、湖泊、山脉、
     地形块、水面、树木、道路、城池与旗帜、势力光圈、云、选择光圈、点选、行军动画）
   第二版（§4G）：地理数据与投影移到 js/world-geo.js，地形可扩展到整个欧亚大陆。
   所有地理计算沿用 Unity 坐标（x 向东，z = 地图 y 向北），网格直接写入类型数组
   （与 MeshBuilder 相同的 z 取反与三角形顺序）；物体位置用 SG.U(x, y, z)。

   公开接口
     build(state, opts)          同步构建（与旧版相同）。opts.region = 'auto' | 'china' | 'world'
                                 （auto：所有城池都在旧版中国地图内时用 'china'，否则 'world'）
     buildAsync(state, opts)     → Promise<root>：同样的构建，切成约 opts.sliceMs（默认 12ms）的分片（单步 ≤ 15ms），
                                 opts.onProgress(fraction 0..1, label) 报告进度；opts.traceSteps 记录最长的单步
     refresh(state) select(cityId) pick(screenX, screenY) → cityId | -1
     height(mapX, mapY) surfaceY(mapX, mapY) isForest(x, z, h) update(dt)
     march(fromCity, toCity, colorHex, seconds = 1.6) → Promise（海路沿航线行进）
     root  cities: Map<cityId, { city, group, labelPos, pos, mapX, mapY, size }>
     region ('china' | 'world')   bounds { xMin, yMin, xMax, yMax }（可玩范围，地图坐标；CameraRig.useMap 用它）
     treeCount  buildStats { totalMs, maxSliceMs, maxSliceStage, maxSliceByStage, slices, stages: {阶段: ms}, sync }
     debugOverlay(kind | null)   调试：在地图上叠加 'land' | 'coast' | 'river' | 'plateau' | 'biome' 栅格
   地理数据：SG.WorldGeo.projected()（js/world-geo.js）；页面没有载入 world-geo.js 时退回内置的旧版中国数据
     （只能构建 'china'，与旧版完全相同）。'china' 范围内的地形公式、配色、树木、云与旧版相同。
   地理场全部栅格化：陆地 = 按行分桶的奇偶规则；海岸 / 河流距离 = 最近要素栅格（种子 + 双向扫描传播，
     查询只算周围四个节点记下的要素）；山脉 = 桶索引；高原、生物群落 = 扫描线填充 + 盒式模糊。
   地形分块（经典 32×32、世界 40×40 格；视锥剔除），深海格子不出网格；树木按区块与树种做实例化网格（镜头很高时隐藏）。
   海路：若 SG.linkIsSea(aId, bId) 存在且为真，两城之间画海上虚线航线（海域栅格上的 A*，绕开陆地），不画道路。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const MapW = 112, MapH = 100;

  // ---------------------------------------------------------- 小工具 --
  function segDist(px, py, ax, ay, bx, by) {
    const abx = bx - ax, aby = by - ay;
    const t = M.clamp01(((px - ax) * abx + (py - ay) * aby) / Math.max(1e-4, abx * abx + aby * aby));
    const dx = px - (ax + abx * t), dy = py - (ay + aby * t);
    return Math.sqrt(dx * dx + dy * dy);
  }
  function fbm(x, y, oct) {
    if (oct === undefined) oct = 4;
    let s = 0, a = 0.5, f = 1, n = 0;
    for (let i = 0; i < oct; i++) { s += a * M.perlinNoise(x * f + 31.7 * i, y * f + 17.3 * i); n += a; a *= 0.5; f *= 2.03; }
    return s / n;
  }
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());

  // 让出主线程（MessageChannel 不受 setTimeout 4ms 下限影响）
  const _yq = [];
  let _mc = null;
  try {
    _mc = new MessageChannel();
    _mc.port1.onmessage = () => { const r = _yq.shift(); if (r) r(); };
  } catch (e) { _mc = null; }
  function yieldTask() {
    return new Promise(r => { if (_mc) { _yq.push(r); _mc.port2.postMessage(0); } else setTimeout(r, 0); });
  }

  // ---------------------------------------------------------- 旧版地理（后备）--
  // 页面没有载入 js/world-geo.js 时使用：旧版中国地图的海岸、河流、湖泊、山脉，
  // 换成与 SG.WorldGeo.projected() 相同的格式（陆地环 = 旧版海域多边形的补集；海岸距离只算海岸线本身）。
  function LL(lon, lat) { return [(lon - 100) * 5, (lat - 23) * 5.6]; }
  function llLine(a) { const out = []; for (let i = 0; i < a.length; i += 2) out.push(...LL(a[i], a[i + 1])); return Float64Array.from(out); }
  let _legacy = null;
  function legacyProjected() {
    if (_legacy) return _legacy;
    const coast = llLine([124, 41.5, 121.5, 40.8, 119.6, 39.9, 118.3, 39.1, 117.7, 38.8, 118.0, 38.2, 118.9, 37.6, 119.4, 37.1, 120.6, 37.7,
      122.6, 37.3, 121.2, 36.6, 120.2, 35.9, 119.3, 35.0, 120.4, 34.3, 120.9, 32.8, 121.9, 31.6, 121.0, 30.7, 122.0, 29.8, 121.5, 28.6,
      120.4, 27.3, 119.6, 26.0, 118.6, 24.6, 117.0, 23.5, 114.8, 22.6, 113.4, 22.2, 111.2, 21.5, 109.6, 21.3, 108.5, 21.6, 107.0, 21.2, 106.0, 19.0]);
    const land = Array.from(coast).concat([30, -400, -500, -400, -500, 500, coast[0], 500]);
    const rivers = [
      [99, 35.4, 101.5, 36.0, 103.8, 36.1, 105.8, 37.4, 106.6, 39.4, 108.4, 40.6, 110.9, 40.3, 111.4, 38.7, 110.6, 36.4, 110.3, 34.7, 112.4, 34.9, 114.6, 35.0, 116.0, 36.2, 117.4, 37.3, 118.9, 37.75],
      [99, 26.8, 101.5, 26.5, 103.2, 27.6, 104.6, 28.8, 106.5, 29.55, 108.4, 30.7, 110.3, 31.0, 111.4, 30.7, 112.3, 30.2, 113.2, 29.5, 114.3, 30.55, 115.6, 29.8, 117.0, 30.5, 118.4, 31.6, 119.5, 32.2, 121.0, 31.7, 121.9, 31.5],
      [106.4, 33.1, 108.5, 32.8, 110.8, 32.6, 112.1, 32.05, 112.6, 31.2, 113.6, 30.7, 114.3, 30.55],
      [112.5, 32.4, 114.4, 32.4, 116.4, 32.5, 118.0, 33.0, 119.3, 33.6, 120.4, 34.2],
      [111.0, 25.6, 112.6, 26.9, 113.0, 28.2, 112.9, 29.3],
      [114.9, 25.8, 115.0, 27.3, 115.9, 28.7, 116.2, 29.3],
    ];
    const ridges = [[104.5, 34.0, 111.8, 34.0], [113.6, 35.0, 114.6, 40.0], [105.5, 32.3, 110.0, 31.6], [109.5, 25.4, 116.0, 24.9],
      [116.2, 26.0, 118.6, 28.6], [115.0, 40.6, 119.0, 40.4], [110.9, 36.0, 111.6, 39.2], [98.0, 39.0, 103.2, 37.0], [113.6, 31.6, 116.4, 31.1],
      [109.0, 30.2, 110.6, 31.6], [108.0, 27.0, 110.4, 29.4]];
    const all = Float64Array.from([-2000, -2000, 2000, -2000, 2000, 2000, -2000, 2000]);
    _legacy = {
      legacy: true,
      land: [Float64Array.from(land)], landNames: ['旧版中国'], coast: [coast],
      rivers: rivers.map(r => ({ w: 1, d: -0.35, xy: llLine(r) })),
      lakes: [[112.8, 29.3, 3.2], [116.3, 29.1, 2.6], [120.0, 31.0, 1.5]].map(l => { const p = LL(l[0], l[1]); return { x: p[0], y: p[1], r: l[2] }; }),
      ridges: ridges.map(r => ({ a: 2.6, b: 2.4, w: 6.5, xy: llLine(r) })),
      plateaus: [], biomes: { desert: [], steppe: [], tropical: [], savanna: [], med: [], boreal: [] },
      chinaOld: [all], eastAsia: [all],
    };
    return _legacy;
  }
  function geoData() { return SG.WorldGeo && SG.WorldGeo.projected ? SG.WorldGeo.projected() : legacyProjected(); }

  // ---------------------------------------------------------- 构建范围 --
  // 'china'：旧版中国地图（各范围与旧版完全相同）；'world'：SG.World 全图
  function classicSpec() {
    return {
      key: 'china',
      bounds: { xMin: 0, yMin: 0, xMax: MapW, yMax: MapH },
      terrain: { x0: -26, z0: -24, x1: MapW + 26, z1: MapH + 22, step: 1.25, chunk: 32 },
      skirt: { x0: -210, z0: -210, x1: MapW + 210, z1: MapH + 210, step: 5, tile: 36 },
      water: { x0: -60, z0: -60, x1: MapW + 60, z1: MapH + 60, step: 2.5, tile: 24, band: 20, far: 300 },
      trees: { x0: -10, z0: -6, x1: MapW + 10, z1: MapH + 6 },
      tile: 80,
      fine: 1.0, coarse: 4, fieldRes: 2,
      clouds: 9,
    };
  }
  function worldSpec() {
    const W = SG.World;
    const x0 = Math.floor(W.x0 / 5) * 5, x1 = Math.ceil(W.x1 / 5) * 5, z0 = Math.floor(W.y0 / 5) * 5, z1 = Math.ceil(W.y1 / 5) * 5;
    return {
      key: 'world',
      bounds: { xMin: W.x0, yMin: W.y0, xMax: W.x1, yMax: W.y1 },
      terrain: { x0: x0 - 40, z0: z0 - 40, x1: x1 + 40, z1: z1 + 40, step: 1.25, chunk: 40 },
      skirt: { x0: x0 - 230, z0: z0 - 230, x1: x1 + 230, z1: z1 + 230, step: 6, tile: 30 },
      water: { x0: x0 - 60, z0: z0 - 60, x1: x1 + 60, z1: z1 + 60, step: 2.5, tile: 24, band: 20, far: 300 },
      trees: { x0: x0 - 8, z0: z0 - 8, x1: x1 + 8, z1: z1 + 8 },
      tile: 100,
      fine: 1.0, coarse: 4, fieldRes: 2,
      clouds: 18,
    };
  }

  // ===================================================== 地理场（栅格化）==
  // 1) 点在陆地内：投影后的陆地环按行分桶，精确的奇偶规则交点计数
  class EdgeIndex {
    constructor(rings, band) {
      let ne = 0;
      for (const r of rings) ne += r.length >> 1;
      const E = new Float64Array(ne * 4);
      let k = 0, y0 = Infinity, y1 = -Infinity;
      for (const r of rings) {
        const n = r.length >> 1;
        for (let i = 0; i < n; i++) {
          const j = (i + 1) % n;
          E[k++] = r[2 * i]; E[k++] = r[2 * i + 1]; E[k++] = r[2 * j]; E[k++] = r[2 * j + 1];
          y0 = Math.min(y0, r[2 * i + 1]); y1 = Math.max(y1, r[2 * i + 1]);
        }
      }
      this.E = E; this.ne = ne; this.band = band;
      this.y0 = Math.floor(y0) - 1;
      this.nb = Math.ceil((y1 + 1 - this.y0) / band) + 1;
      const cnt = new Int32Array(this.nb + 1);
      const lo = e => Math.floor((Math.min(E[e * 4 + 1], E[e * 4 + 3]) - this.y0) / band);
      const hi = e => Math.floor((Math.max(E[e * 4 + 1], E[e * 4 + 3]) - this.y0) / band);
      for (let e = 0; e < ne; e++) for (let b = lo(e); b <= hi(e); b++) cnt[b + 1]++;
      for (let b = 0; b < this.nb; b++) cnt[b + 1] += cnt[b];
      const idx = new Int32Array(cnt[this.nb]), fill = cnt.slice(0, this.nb);
      for (let e = 0; e < ne; e++) for (let b = lo(e); b <= hi(e); b++) idx[fill[b]++] = e;
      this.start = cnt; this.idx = idx;
    }
    inside(x, y) {
      const b = Math.floor((y - this.y0) / this.band);
      if (b < 0 || b >= this.nb) return false;
      const E = this.E, idx = this.idx;
      let c = false;
      for (let p = this.start[b], q = this.start[b + 1]; p < q; p++) {
        const e = idx[p] * 4;
        const yi = E[e + 1], yj = E[e + 3];
        if ((yi > y) !== (yj > y)) {
          const xi = E[e], xj = E[e + 2];
          if (x < (xj - xi) * (y - yi) / (yj - yi) + xi) c = !c;
        }
      }
      return c;
    }
  }

  // 2) 最近要素栅格（线段 / 圆）：在节点上记下最近的要素编号（种子 + 四遍扫描传播），
  //    查询时取周围四个节点各自的最近要素，精确计算点到要素的距离——比逐段循环快得多，又几乎精确。
  //    要素 F：每个 5 个数 ax, ay, bx, by, r（r > 0 为圆，a = b = 圆心）
  function featDist(F, k, px, py) {
    const o = k * 5, ax = F[o], ay = F[o + 1], abx = F[o + 2] - ax, aby = F[o + 3] - ay;
    const L = abx * abx + aby * aby;
    let t = L > 1e-12 ? ((px - ax) * abx + (py - ay) * aby) / Math.max(1e-4, L) : 0;
    t = t < 0 ? 0 : t > 1 ? 1 : t;
    const dx = px - (ax + abx * t), dy = py - (ay + aby * t);
    const d = Math.sqrt(dx * dx + dy * dy) - F[o + 4];
    return d > 0 ? d : 0;
  }
  class FeatureGrid {
    constructor(F, nf, x0, z0, x1, z1, res) {
      this.F = F; this.nf = nf; this.res = res;
      this.x0 = x0; this.z0 = z0;
      this.nx = Math.ceil((x1 - x0) / res) + 1; this.nz = Math.ceil((z1 - z0) / res) + 1;
      this.feat = null;
      this.last = -1;
    }
    *build() {
      const { F, nf, nx, nz, x0, z0, res } = this;
      const N = nx * nz;
      const feat = new Int32Array(N).fill(-1);
      const dist = new Float32Array(N).fill(Infinity);
      const xm0 = x0 - 3 * res, zm0 = z0 - 3 * res, xm1 = x0 + (nx + 2) * res, zm1 = z0 + (nz + 2) * res;
      const touch = (k, i, j) => {
        if (i < 0 || j < 0 || i >= nx || j >= nz) return;
        const o = j * nx + i, d = featDist(F, k, x0 + i * res, z0 + j * res);
        if (d < dist[o]) { dist[o] = d; feat[o] = k; }
      };
      // 种子：沿每个要素取样，写入周围 3×3 个节点
      for (let k = 0; k < nf; k++) {
        const o = k * 5, ax = F[o], ay = F[o + 1], bx = F[o + 2], by = F[o + 3], r = F[o + 4];
        if (Math.max(ax, bx) + r < xm0 || Math.min(ax, bx) - r > xm1 || Math.max(ay, by) + r < zm0 || Math.min(ay, by) - r > zm1) continue;
        if (r > 0) {
          const R = r + 1.5 * res;
          const i0 = Math.floor((ax - R - x0) / res), i1 = Math.ceil((ax + R - x0) / res);
          const j0 = Math.floor((ay - R - z0) / res), j1 = Math.ceil((ay + R - z0) / res);
          for (let i = i0; i <= i1; i++) for (let j = j0; j <= j1; j++) touch(k, i, j);
        } else {
          const L = Math.hypot(bx - ax, by - ay), n = Math.max(1, Math.ceil(L / (res * 0.5)));
          for (let s = 0; s <= n; s++) {
            const px = ax + (bx - ax) * s / n, py = ay + (by - ay) * s / n;
            const ci = Math.round((px - x0) / res), cj = Math.round((py - z0) / res);
            if (ci < -2 || cj < -2 || ci > nx + 1 || cj > nz + 1) continue;
            for (let di = -1; di <= 1; di++) for (let dj = -1; dj <= 1; dj++) touch(k, ci + di, cj + dj);
          }
        }
        if ((k & 63) === 63) yield;
      }
      // 传播：上→下（左→右、右→左），下→上（右→左、左→右）
      const relax = (o, q, px, py) => {
        const k = feat[q];
        if (k < 0 || k === feat[o]) return;
        const d = featDist(F, k, px, py);
        if (d < dist[o]) { dist[o] = d; feat[o] = k; }
      };
      for (let j = 0; j < nz; j++) {
        const py = z0 + j * res, row = j * nx;
        for (let i = 0; i < nx; i++) {
          const o = row + i, px = x0 + i * res;
          if (i > 0) relax(o, o - 1, px, py);
          if (j > 0) {
            relax(o, o - nx, px, py);
            if (i > 0) relax(o, o - nx - 1, px, py);
            if (i < nx - 1) relax(o, o - nx + 1, px, py);
          }
        }
        for (let i = nx - 2; i >= 0; i--) relax(row + i, row + i + 1, x0 + i * res, py);
        if ((j & 1) === 1) yield;
      }
      for (let j = nz - 1; j >= 0; j--) {
        const py = z0 + j * res, row = j * nx;
        for (let i = nx - 1; i >= 0; i--) {
          const o = row + i, px = x0 + i * res;
          if (i < nx - 1) relax(o, o + 1, px, py);
          if (j < nz - 1) {
            relax(o, o + nx, px, py);
            if (i < nx - 1) relax(o, o + nx + 1, px, py);
            if (i > 0) relax(o, o + nx - 1, px, py);
          }
        }
        for (let i = 1; i < nx; i++) relax(row + i, row + i - 1, x0 + i * res, py);
        if ((j & 1) === 1) yield;
      }
      this.feat = feat;
    }
    covers(x, z) {
      return x >= this.x0 && z >= this.z0 && x <= this.x0 + (this.nx - 1) * this.res && z <= this.z0 + (this.nz - 1) * this.res;
    }
    // 点到最近要素的距离（无要素时 999）；this.last = 最近要素编号
    query(x, z) {
      const feat = this.feat, nx = this.nx, F = this.F;
      let i = Math.floor((x - this.x0) / this.res), j = Math.floor((z - this.z0) / this.res);
      if (i < 0) i = 0; else if (i > nx - 2) i = nx - 2;
      if (j < 0) j = 0; else if (j > this.nz - 2) j = this.nz - 2;
      const o = j * nx + i;
      const k0 = feat[o], k1 = feat[o + 1], k2 = feat[o + nx], k3 = feat[o + nx + 1];
      let best = 999, bk = -1, d;
      if (k0 >= 0) { d = featDist(F, k0, x, z); if (d < best) { best = d; bk = k0; } }
      if (k1 >= 0 && k1 !== k0) { d = featDist(F, k1, x, z); if (d < best) { best = d; bk = k1; } }
      if (k2 >= 0 && k2 !== k0 && k2 !== k1) { d = featDist(F, k2, x, z); if (d < best) { best = d; bk = k2; } }
      if (k3 >= 0 && k3 !== k0 && k3 !== k1 && k3 !== k2) { d = featDist(F, k3, x, z); if (d < best) { best = d; bk = k3; } }
      this.last = bk;
      return best;
    }
  }

  // 3) 山脉：桶索引（每条山脉取到各段的最近距离，exp(−d²/w) 衰减，与旧版公式相同）
  class RidgeIndex {
    constructor(ridges, cell) {
      this.ridges = ridges; this.cell = cell;
      let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
      const R = ridges.map(r => Math.sqrt(r.w * Math.log(1e7)) + 0.5);
      ridges.forEach((r, id) => {
        for (let i = 0; i < r.xy.length; i += 2) {
          x0 = Math.min(x0, r.xy[i] - R[id]); x1 = Math.max(x1, r.xy[i] + R[id]);
          z0 = Math.min(z0, r.xy[i + 1] - R[id]); z1 = Math.max(z1, r.xy[i + 1] + R[id]);
        }
      });
      this.x0 = x0; this.z0 = z0;
      this.nx = Math.max(1, Math.ceil((x1 - x0) / cell)); this.nz = Math.max(1, Math.ceil((z1 - z0) / cell));
      this.b = new Array(this.nx * this.nz).fill(null);
      ridges.forEach((r, id) => {
        const seen = new Set();
        for (let s = 0; s + 3 < r.xy.length; s += 2) {
          const ax = r.xy[s], ay = r.xy[s + 1], bx = r.xy[s + 2], by = r.xy[s + 3];
          const i0 = Math.floor((Math.min(ax, bx) - R[id] - x0) / cell), i1 = Math.floor((Math.max(ax, bx) + R[id] - x0) / cell);
          const j0 = Math.floor((Math.min(ay, by) - R[id] - z0) / cell), j1 = Math.floor((Math.max(ay, by) + R[id] - z0) / cell);
          for (let i = Math.max(0, i0); i <= Math.min(this.nx - 1, i1); i++)
            for (let j = Math.max(0, j0); j <= Math.min(this.nz - 1, j1); j++) {
              const o = j * this.nx + i;
              if (seen.has(o)) continue;
              seen.add(o);
              (this.b[o] || (this.b[o] = [])).push(id);
            }
        }
      });
      for (const l of this.b) if (l) l.sort((a, c) => a - c);
    }
    sum(x, z) {
      const i = Math.floor((x - this.x0) / this.cell), j = Math.floor((z - this.z0) / this.cell);
      if (i < 0 || j < 0 || i >= this.nx || j >= this.nz) return 0;
      const list = this.b[j * this.nx + i];
      if (!list) return 0;
      let h = 0, rf = -1;
      for (let q = 0; q < list.length; q++) {
        const r = this.ridges[list[q]], xy = r.xy;
        let d2 = Infinity;
        for (let s = 0; s + 3 < xy.length; s += 2) {
          const d = segDist(x, z, xy[s], xy[s + 1], xy[s + 2], xy[s + 3]);
          if (d * d < d2) d2 = d * d;
        }
        const k = Math.exp(-d2 / r.w);
        if (k < 1e-7) continue;
        if (rf < 0) rf = fbm(x * 0.2, z * 0.2, 2);
        h += k * (r.a + rf * r.b);
      }
      return h;
    }
  }

  // 4) 平滑场（高原、生物群落、旧版模型权重）：多边形扫描线填充 + 三遍盒式模糊，双线性取样
  const F_OLD = 0, F_EAST = 1, F_PLAT = 2, F_DESERT = 3, F_STEPPE = 4, F_TROP = 5, F_SAV = 6, F_MED = 7, F_BOREAL = 8, NF = 9;
  function fillRing(arr, nx, nz, x0, z0, res, ring, value) {
    const n = ring.length >> 1;
    let ymin = Infinity, ymax = -Infinity;
    for (let i = 0; i < n; i++) { ymin = Math.min(ymin, ring[2 * i + 1]); ymax = Math.max(ymax, ring[2 * i + 1]); }
    const j0 = Math.max(0, Math.ceil((ymin - z0) / res)), j1 = Math.min(nz - 1, Math.floor((ymax - z0) / res));
    const xs = [];
    for (let j = j0; j <= j1; j++) {
      const y = z0 + j * res;
      xs.length = 0;
      for (let a = 0, b = n - 1; a < n; b = a++) {
        const ya = ring[2 * a + 1], yb = ring[2 * b + 1];
        if ((ya > y) !== (yb > y)) xs.push(ring[2 * a] + (y - ya) * (ring[2 * b] - ring[2 * a]) / (yb - ya));
      }
      xs.sort((p, q) => p - q);
      for (let k = 0; k + 1 < xs.length; k += 2) {
        const i0 = Math.max(0, Math.ceil((xs[k] - x0) / res)), i1 = Math.min(nx - 1, Math.floor((xs[k + 1] - x0) / res));
        for (let i = i0; i <= i1; i++) { const o = j * nx + i; if (arr[o] < value) arr[o] = value; }
      }
    }
  }
  function* boxBlur(arr, nx, nz, r, passes) {
    if (r <= 0) return;
    const tmp = new Float32Array(Math.max(nx, nz));
    const w = 2 * r + 1;
    for (let p = 0; p < passes; p++) {
      for (let j = 0; j < nz; j++) {
        const row = j * nx;
        let s = 0;
        for (let k = -r; k <= r; k++) s += arr[row + Math.min(nx - 1, Math.max(0, k))];
        for (let i = 0; i < nx; i++) {
          tmp[i] = s / w;
          s += arr[row + Math.min(nx - 1, i + r + 1)] - arr[row + Math.max(0, i - r)];
        }
        for (let i = 0; i < nx; i++) arr[row + i] = tmp[i];
        if ((j & 31) === 31) yield;
      }
      for (let i = 0; i < nx; i++) {
        let s = 0;
        for (let k = -r; k <= r; k++) s += arr[Math.min(nz - 1, Math.max(0, k)) * nx + i];
        for (let j = 0; j < nz; j++) {
          tmp[j] = s / w;
          s += arr[Math.min(nz - 1, j + r + 1) * nx + i] - arr[Math.max(0, j - r) * nx + i];
        }
        for (let j = 0; j < nz; j++) arr[j * nx + i] = tmp[j];
        if ((i & 31) === 31) yield;
      }
    }
  }

  // 地理场集合；按构建范围缓存（标题背景与正式开局共用）
  const _geoCache = new Map();
  let _landIndex = null;
  class Geo {
    constructor(spec) {
      this.spec = spec;
      const pad = 4;
      const Wt = spec.water, Sk = spec.skirt;
      this.fineBox = { x0: Wt.x0 - pad, z0: Wt.z0 - pad, x1: Wt.x1 + pad, z1: Wt.z1 + pad };
      this.coarseBox = { x0: Sk.x0 - 8, z0: Sk.z0 - 8, x1: Sk.x1 + 8, z1: Sk.z1 + 8 };
      this.f = new Float32Array(NF);
      this.rW = 1; this.rD = -0.35;
      this.ready = false;
      this.world = spec.key === 'world';
    }
    *build() {
      const P = geoData();
      this.legacy = !!P.legacy;
      if (!_landIndex || _landIndex.src !== P) { _landIndex = new EdgeIndex(P.land, 2); _landIndex.src = P; }
      this.land = _landIndex;
      yield;
      // 海岸要素：所有陆地环的边（旧版后备数据只算海岸折线本身）
      let n = 0, k = 0, CF;
      if (P.coast) {
        for (const r of P.coast) n += (r.length >> 1) - 1;
        CF = new Float64Array(n * 5);
        for (const r of P.coast)
          for (let i = 0; i + 3 < r.length; i += 2) { CF[k++] = r[i]; CF[k++] = r[i + 1]; CF[k++] = r[i + 2]; CF[k++] = r[i + 3]; CF[k++] = 0; }
      } else {
        for (const r of P.land) n += r.length >> 1;
        CF = new Float64Array(n * 5);
        for (const r of P.land) {
          const m = r.length >> 1;
          for (let i = 0; i < m; i++) {
            const j = (i + 1) % m;
            CF[k++] = r[2 * i]; CF[k++] = r[2 * i + 1]; CF[k++] = r[2 * j]; CF[k++] = r[2 * j + 1]; CF[k++] = 0;
          }
        }
      }
      const B = this.fineBox, C = this.coarseBox, sp = this.spec;
      this.coastFine = new FeatureGrid(CF, n, B.x0, B.z0, B.x1, B.z1, sp.fine);
      yield* this.coastFine.build();
      this.coastCoarse = new FeatureGrid(CF, n, C.x0, C.z0, C.x1, C.z1, sp.coarse);
      yield* this.coastCoarse.build();
      // 河流要素：河段 + 湖（圆）；附带每个要素的宽度、深度
      let nr = P.lakes.length;
      for (const r of P.rivers) nr += (r.xy.length >> 1) - 1;
      const RF = new Float64Array(nr * 5), RW = new Float32Array(nr), RD = new Float32Array(nr);
      k = 0;
      let q = 0;
      for (const r of P.rivers) {
        for (let i = 0; i + 3 < r.xy.length; i += 2) {
          RF[k++] = r.xy[i]; RF[k++] = r.xy[i + 1]; RF[k++] = r.xy[i + 2]; RF[k++] = r.xy[i + 3]; RF[k++] = 0;
          RW[q] = r.w; RD[q] = r.d; q++;
        }
      }
      for (const l of P.lakes) { RF[k++] = l.x; RF[k++] = l.y; RF[k++] = l.x; RF[k++] = l.y; RF[k++] = l.r; RW[q] = 1; RD[q] = -0.35; q++; }
      this.RW = RW; this.RD = RD;
      this.riverFine = new FeatureGrid(RF, nr, B.x0, B.z0, B.x1, B.z1, sp.fine);
      yield* this.riverFine.build();
      this.riverCoarse = new FeatureGrid(RF, nr, C.x0, C.z0, C.x1, C.z1, sp.coarse);
      yield* this.riverCoarse.build();
      this.ridges = new RidgeIndex(P.ridges, 8);
      yield;
      yield* this._buildFields(P);
      this.ready = true;
    }
    *_buildFields(P) {
      const res = this.spec.fieldRes, C = this.coarseBox;
      const nx = Math.ceil((C.x1 - C.x0) / res) + 1, nz = Math.ceil((C.z1 - C.z0) / res) + 1;
      this.fx0 = C.x0; this.fz0 = C.z0; this.fres = res; this.fnx = nx; this.fnz = nz;
      const fields = this.fields = [];
      for (let i = 0; i < NF; i++) fields.push(new Float32Array(nx * nz));
      const fill = (fi, rings, v) => { for (const r of rings) fill1(fi, r, r.v > 0 ? r.v * v : v); };
      const fill1 = (fi, r, v) => fillRing(fields[fi], nx, nz, C.x0, C.z0, res, r, v);
      const cells = u => Math.max(1, Math.round(u / res));
      // 旧版模型权重
      fill(F_OLD, P.chinaOld, 1);
      yield* boxBlur(fields[F_OLD], nx, nz, cells(4), 3);
      // 东亚干旱色权重（日本减半）
      fill(F_EAST, P.eastAsia, 1);
      if (!P.legacy) {
        const japanX = SG.project(129.8, 35).x;
        for (let i = 0; i < nx; i++) if (C.x0 + i * res > japanX) for (let j = 0; j < nz; j++) fields[F_EAST][j * nx + i] *= 0.45;
      }
      yield* boxBlur(fields[F_EAST], nx, nz, cells(6), 3);
      // 高原（取各高原高度的最大值）
      P.plateaus.forEach(p => fill1(F_PLAT, p.xy, p.h));
      yield;
      yield* boxBlur(fields[F_PLAT], nx, nz, cells(4), 3);
      // 生物群落
      const B = P.biomes;
      const blurs = [[F_DESERT, B.desert, 4], [F_STEPPE, B.steppe, 4], [F_TROP, B.tropical, 6], [F_SAV, B.savanna, 6], [F_MED, B.med, 4]];
      for (const [fi, rings, r] of blurs) {
        fill(fi, rings, 1);
        yield;
        yield* boxBlur(fields[fi], nx, nz, cells(r), 3);
      }
      // 北方针叶林：按纬度（斯堪的纳维亚、西欧更靠北才开始）
      if (P.legacy) return;
      const yMin = Math.min(SG.project(0, 48).y, SG.project(120, 48).y);
      const bo = fields[F_BOREAL];
      for (let i = 0; i < nx; i++) {
        const x = C.x0 + i * res;
        for (let j = 0; j < nz; j++) {
          const y = C.z0 + j * res;
          if (y < yMin) continue;
          const g = SG.unproject(x, y);
          const thr = g.lon >= 25 ? 52 : g.lon <= 8 ? 58.5 : 58.5 - (g.lon - 8) / 17 * 6.5;
          bo[j * nx + i] = M.smoothStep(0, 1, M.inverseLerp(thr, thr + 5, g.lat));
        }
        if ((i & 15) === 15) yield;
      }
    }

    coastDist(x, z) { return this.coastFine.covers(x, z) ? this.coastFine.query(x, z) : this.coastCoarse.query(x, z); }
    inSea(x, z) { return !this.land.inside(x, z); }
    // 到最近河流 / 湖泊的距离；this.rW / this.rD = 该河的宽度系数、河床深度
    riverDist(x, z) {
      const g = this.riverFine.covers(x, z) ? this.riverFine : this.riverCoarse;
      const d = g.query(x, z);
      if (g.last >= 0) { this.rW = this.RW[g.last]; this.rD = this.RD[g.last]; } else { this.rW = 1; this.rD = -0.35; }
      return d;
    }
    // 双线性取样全部平滑场，写入 this.f（共享数组，调用方立即使用）
    sample(x, z) {
      const f = this.f, nx = this.fnx, nz = this.fnz;
      let fx = (x - this.fx0) / this.fres, fz = (z - this.fz0) / this.fres;
      let i = Math.floor(fx), j = Math.floor(fz);
      if (i < 0) { i = 0; fx = 0; } else if (i > nx - 2) { i = nx - 2; fx = nx - 1; }
      if (j < 0) { j = 0; fz = 0; } else if (j > nz - 2) { j = nz - 2; fz = nz - 1; }
      const tx = fx - i, tz = fz - j;
      const o = j * nx + i;
      const w00 = (1 - tx) * (1 - tz), w10 = tx * (1 - tz), w01 = (1 - tx) * tz, w11 = tx * tz;
      const F = this.fields;
      for (let k = 0; k < NF; k++) {
        const a = F[k];
        f[k] = a[o] * w00 + a[o + 1] * w10 + a[o + nx] * w01 + a[o + nx + 1] * w11;
      }
      return f;
    }
  }
  function* geoFor(spec, out) {
    let g = _geoCache.get(spec.key);
    if (!g || !g.ready || g.legacy !== !!geoData().legacy) {
      g = new Geo(spec);
      yield* g.build();
      _geoCache.set(spec.key, g);
    }
    out.geo = g;
  }

  // ---------------------------------------------------------- 地表颜色 --
  const LUSH = [0.36, 0.62, 0.3], DRY = [0.66, 0.64, 0.38], STEPPE = [0.62, 0.55, 0.36];
  const SEABED = [0.7, 0.66, 0.5], SAND = [0.86, 0.8, 0.6], FOREST = [0.22, 0.45, 0.24];
  // ROCK / SNOW 比 C#（0.58,0.56,0.53 / 0.95,0.96,0.98）略暗：网页端没有色调映射，
  // 半兰伯特太阳光 + 三色环境光的总辐照度可达 1.3，原值会让西部雪原整片截断成纯白、看不出低多边形面。
  const UPLAND = [0.56, 0.52, 0.4], ROCK = [0.55, 0.53, 0.5], SNOW = [0.88, 0.9, 0.94];
  // 第二版的世界群落色
  const DESERT = [0.84, 0.72, 0.47], STRAW = [0.72, 0.66, 0.41], TROPIC = [0.2, 0.55, 0.22], SAVANNA = [0.62, 0.61, 0.34];
  const MED_OLIVE = [0.53, 0.58, 0.34], TAIGA = [0.27, 0.45, 0.31];
  // 世界高原（青藏、帕米尔、伊朗……）：高寒草甸 / 荒原的黄褐色，雪线比旧版西部高原高（只在旧版范围外）
  const ALPINE = [0.6, 0.55, 0.4], _rock = [0, 0, 0];
  const TROP_FOREST = [0.12, 0.4, 0.16], TAIGA_FOREST = [0.15, 0.32, 0.23];
  function lerpInto(c, b, t) { t = M.clamp01(t); c[0] += (b[0] - c[0]) * t; c[1] += (b[1] - c[1]) * t; c[2] += (b[2] - c[2]) * t; }
  const _gc = [0, 0, 0], _grass = [0, 0, 0], _fc = [0, 0, 0], _gcOut = { r: 0, g: 0, b: 0 };
  // 沙漠在河流 / 湖泊两岸让位给绿洲
  function oasis(geo, x, z) { return 0.2 + 0.8 * M.smoothStep(0, 1, M.inverseLerp(1.0, 4.5, geo.riverDist(x, z))); }
  function groundColor(geo, x, z, h, slope, jitter, noForest) {
    const f = geo.sample(x, z);
    const north = M.inverseLerp(10, 95, z);
    const grass = _grass;
    grass[0] = LUSH[0]; grass[1] = LUSH[1]; grass[2] = LUSH[2];
    lerpInto(grass, DRY, north * 0.85 * f[F_EAST]);
    lerpInto(grass, STEPPE, M.inverseLerp(30, 5, x) * 0.6 * f[F_OLD]);
    const med = f[F_MED], sav = f[F_SAV], st = f[F_STEPPE], bo = f[F_BOREAL], tr = f[F_TROP];
    let des = f[F_DESERT];
    if (med > 0.001) lerpInto(grass, MED_OLIVE, med * 0.75);
    if (sav > 0.001) lerpInto(grass, SAVANNA, sav * 0.75);
    const oa = des > 0.001 || st > 0.001 ? oasis(geo, x, z) : 1;
    if (st > 0.001) lerpInto(grass, STRAW, st * 0.85 * (0.45 + 0.55 * oa));
    if (bo > 0.001) lerpInto(grass, TAIGA, bo * 0.8);
    if (tr > 0.001) lerpInto(grass, TROPIC, tr * 0.85);
    if (des > 0.001) { des *= oa; lerpInto(grass, DESERT, des); }
    const c = _gc;
    const src = h < -0.15 ? SEABED : h < 0.42 ? SAND : grass;
    c[0] = src[0]; c[1] = src[1]; c[2] = src[2];
    const forest = noForest ? 0 : fbm(x * 0.09 + 100, z * 0.09 + 100, 3);
    const thr = 0.56 + des * 0.3 + st * 0.12 + sav * 0.05 - tr * 0.06 - bo * 0.05;
    if (h >= 0.42 && h < 3.5 && forest > thr) {
      let fc = FOREST;
      if (tr > 0.001 || bo > 0.001) {
        fc = _fc; fc[0] = FOREST[0]; fc[1] = FOREST[1]; fc[2] = FOREST[2];
        lerpInto(fc, TROP_FOREST, tr); lerpInto(fc, TAIGA_FOREST, bo);
      }
      lerpInto(c, fc, M.inverseLerp(thr, thr + 0.08, forest));
    }
    // 旧版范围（wOld = 1）与旧版完全相同；范围外的高原：岩色偏向高寒荒原，雪线抬高 1.6
    let wNew = 1 - f[F_OLD], plat = f[F_PLAT] / 3;
    if (geo.world) {
      // 世界地图：旧版西部高原（x < 22）也按高寒荒原着色，与西边的青藏高原连成一体（经典剧本不受影响）
      const wp = M.smoothStep(0, 1, M.inverseLerp(24, 8, x));
      if (wp > 0) { wNew = Math.max(wNew, 0.9 * wp); plat = Math.max(plat, wp); }
    }
    const alp = wNew > 0.001 ? wNew * M.clamp01(plat) : 0;
    if (h > 2.2) lerpInto(c, UPLAND, M.inverseLerp(2.2, 3.6, h));
    if (h > 3.6 || slope > 0.62) {
      let rock = ROCK;
      if (alp > 0) { rock = _rock; rock[0] = ROCK[0]; rock[1] = ROCK[1]; rock[2] = ROCK[2]; lerpInto(rock, ALPINE, alp * (1 - M.inverseLerp(0.55, 0.8, slope))); }
      lerpInto(c, rock, Math.max(M.inverseLerp(3.6, 5, h), M.inverseLerp(0.62, 0.8, slope)));
    }
    const snow = 6.2 + 1.6 * wNew;
    if (h > snow) lerpInto(c, SNOW, M.inverseLerp(snow, snow + 1, h));
    // Art.Shade(c, jitter)
    if (jitter >= 0) { _gcOut.r = c[0] + (1 - c[0]) * jitter; _gcOut.g = c[1] + (1 - c[1]) * jitter; _gcOut.b = c[2] + (1 - c[2]) * jitter; }
    else { const k = 1 + jitter; _gcOut.r = c[0] * k; _gcOut.g = c[1] * k; _gcOut.b = c[2] * k; }
    return _gcOut;
  }

  // 平面着色三角形直接写入类型数组（与 MeshBuilder.tri 相同：Unity 坐标 z 取反，按 a, c, b 输出，法线 = (C−A)×(B−A)）
  function emitTri(P, N, C, o, ax, ay, az, bx, by, bz, cx, cy, cz, col) {
    az = -az; bz = -bz; cz = -cz;
    const ux = cx - ax, uy = cy - ay, uz = cz - az, vx = bx - ax, vy = by - ay, vz = bz - az;
    let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    const l = Math.sqrt(nx * nx + ny * ny + nz * nz);
    if (l > 1e-12) { nx /= l; ny /= l; nz /= l; } else { nx = 0; ny = 1; nz = 0; }
    P[o] = ax; P[o + 1] = ay; P[o + 2] = az; P[o + 3] = cx; P[o + 4] = cy; P[o + 5] = cz; P[o + 6] = bx; P[o + 7] = by; P[o + 8] = bz;
    for (let k = 0; k < 9; k += 3) { N[o + k] = nx; N[o + k + 1] = ny; N[o + k + 2] = nz; C[o + k] = col.r; C[o + k + 1] = col.g; C[o + k + 2] = col.b; }
  }
  function triGeometry(P, N, C, count) {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(count * 3 === P.length ? P : P.slice(0, count * 3), 3));
    g.setAttribute('normal', new THREE.BufferAttribute(count * 3 === N.length ? N : N.slice(0, count * 3), 3));
    g.setAttribute('color', new THREE.BufferAttribute(count * 3 === C.length ? C : C.slice(0, count * 3), 3));
    g.computeBoundingBox();
    g.computeBoundingSphere();
    return g;
  }

  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  const _m4 = new THREE.Matrix4(), _m4b = new THREE.Matrix4();
  const ZERO_MATRIX = new THREE.Matrix4().makeScale(0, 0, 0);
  const _sphere = new THREE.Sphere(), _hit = new THREE.Vector3(), _cloudPos = new THREE.Vector3();
  const _plane05 = new THREE.Plane(new THREE.Vector3(0, 1, 0), -0.5);
  const _plane0 = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const _ray = new THREE.Ray(), _fwd = new THREE.Vector3();

  // ---------------------------------------------------------- 地形材质 --
  // LowPoly 材质 + 高光肩部：最终颜色的最大通道超过 KNEE 后按指数曲线平滑趋近 1（保持色相），
  // 向阳的亮面（雪原、沙滩）不再截断为纯白，相邻三角面仍保留明暗差。KNEE 以下与 LowPoly 完全一致。
  const TERRAIN_KNEE = '0.8';
  const TERRAIN_SHOULDER =
    '\tfloat sgPeak = max( max( outgoingLight.r, outgoingLight.g ), outgoingLight.b );\n' +
    '\tif ( sgPeak > ' + TERRAIN_KNEE + ' ) outgoingLight *= ( ' + TERRAIN_KNEE + ' + ( 1.0 - ' + TERRAIN_KNEE + ' ) * ( 1.0 - exp( ( ' + TERRAIN_KNEE +
    ' - sgPeak ) / ( 1.0 - ' + TERRAIN_KNEE + ' ) ) ) ) / sgPeak;\n' +
    '\t#include <opaque_fragment>';
  // 新建一个带高光肩部的 LowPoly 材质（地形、云共用同一着色器程序，各自的 uniform 独立）
  function shoulderMaterial(name) {
    const mat = SG.Gfx.newLowPoly();
    const lowPolyCompile = mat.onBeforeCompile;
    // 包一层新函数：three 以 onBeforeCompile.toString() 作程序缓存键，因此得到独立于普通 LowPoly 的着色器程序
    mat.onBeforeCompile = function terrainCompile(shader, renderer) {
      lowPolyCompile.call(this, shader, renderer);
      shader.fragmentShader = shader.fragmentShader.replace('#include <opaque_fragment>', TERRAIN_SHOULDER);
    };
    mat.name = name;
    return mat;
  }
  // ---------------------------------------------------------- 云的淡出 --
  // 网页版补充（C# 的云始终不透明）：云离镜头越近越透明，镜头距离 ≥ CLOUD_FADE_FAR 时不透明，≤ CLOUD_FADE_NEAR 时完全隐去。
  // 标题 / 全图视角（镜头距离 85 以上）视野里的云离镜头 51 以上，几乎不透明；战略视角（45～48）视野里的云离镜头约 15～46，
  // 除画面最上缘远处的淡影外全部隐去，城池上方不再留下半透明的灰色多边形。
  const CLOUD_FADE_NEAR = 41, CLOUD_FADE_FAR = 55;
  // 云影随云体一起淡出：阴影深度遍按 3×3 有序抖动矩阵丢弃片元（按阴影贴图像素），隐去的云不再在地面上投下看不到来源的暗斑。
  // 用 3×3 是因为 three 的 PCFSoftShadowMap 恰好对周围 3×3 个阴影像素做盒式平均（两端按亚像素比例分摊权重），
  // 周期为 3 的图案在任何位置都正好平均成 保留数 / 9，接收面上是平滑的浅影而不是网点（10 级浓淡）。
  const CLOUD_SHADOW_PARS =
    'uniform float sgShadowDensity;\n' +
    'float sgDither3( vec2 p ) {\n' +
    '\tvec2 c = floor( p ) - 3.0 * floor( p / 3.0 );\n' + // gl_FragCoord 在像素中心（k + 0.5），取模不会落在边界上
    '\tvec3 row = c.y < 0.5 ? vec3( 0.0, 7.0, 3.0 ) : ( c.y < 1.5 ? vec3( 6.0, 5.0, 2.0 ) : vec3( 4.0, 1.0, 8.0 ) );\n' +
    '\tfloat m = c.x < 0.5 ? row.x : ( c.x < 1.5 ? row.y : row.z );\n' +
    '\treturn ( m + 0.5 ) / 9.0;\n' +
    '}\n';
  const CLOUD_SHADOW_DISCARD =
    '#include <clipping_planes_fragment>\n' +
    '\tif ( sgDither3( gl_FragCoord.xy ) >= sgShadowDensity ) discard;';
  function cloudShadowMaterial() {
    const mat = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
    mat.name = 'CloudShadowDepth';
    const density = { value: 1 };
    mat.userData.shadowDensity = density;
    // 所有云共用同一函数源码 → 同一着色器程序；uniform 对象每朵云各自一份
    mat.onBeforeCompile = function cloudShadowCompile(shader) {
      shader.uniforms.sgShadowDensity = density;
      shader.fragmentShader = CLOUD_SHADOW_PARS +
        shader.fragmentShader.replace('#include <clipping_planes_fragment>', CLOUD_SHADOW_DISCARD);
    };
    return mat;
  }

  let _terrainMat = null;
  function terrainMaterial() {
    if (!_terrainMat) _terrainMat = shoulderMaterial('TerrainLowPoly');
    return _terrainMat;
  }

  // ---------------------------------------------------------- 树木（实例化）--
  // 树叶顶点（属性 sgLeaf = 1）乘以实例颜色，树干保持原色：一种几何体可画出各种叶色
  let _treeMat = null;
  function treeMaterial() {
    if (_treeMat) return _treeMat;
    const mat = SG.Gfx.newLowPoly();
    const base = mat.onBeforeCompile;
    mat.onBeforeCompile = function treeCompile(shader, renderer) {
      base.call(this, shader, renderer);
      shader.vertexShader = 'attribute float sgLeaf;\n' + shader.vertexShader.replace('#include <color_vertex>',
        '#include <color_vertex>\n#if defined( USE_INSTANCING_COLOR ) && defined( USE_COLOR )\n\tvColor.xyz = color.xyz * mix( vec3( 1.0 ), instanceColor.xyz, sgLeaf );\n#endif');
    };
    mat.name = 'TreeLowPoly';
    _treeMat = mat;
    return mat;
  }
  const TRUNK = new THREE.Color(0.38, 0.27, 0.16);
  const _treeGeo = {};
  // kind：broad（阔叶，两种外形）| pine | palm | shrub | cypress
  function treeGeometry(kind) {
    if (_treeGeo[kind]) return _treeGeo[kind];
    const trunk = new SG.MeshBuilder(), leaf = new SG.MeshBuilder();
    const W = new THREE.Color(1, 1, 1), W2 = new THREE.Color(0.88, 0.88, 0.88);
    const p = V(0, 0, 0);
    if (kind === 'broad0' || kind === 'broad1') {
      trunk.cylinder(p, 0.06, 0.05, 0.35, 4, TRUNK, false);
      leaf.blob(V(0, 0.65, 0), V(0.42, 0.4, 0.42), W, kind === 'broad0' ? 3 : 11);
    } else if (kind === 'pine') {
      trunk.cylinder(p, 0.06, 0.05, 0.35, 4, TRUNK, false);
      leaf.cone(V(0, 0.25, 0), 0.42, 0.7, 6, W2);
      leaf.cone(V(0, 0.6, 0), 0.32, 0.6, 6, W);
    } else if (kind === 'cypress') {
      trunk.cylinder(p, 0.05, 0.04, 0.2, 4, TRUNK, false);
      leaf.cone(V(0, 0.12, 0), 0.19, 1.15, 6, W);
    } else if (kind === 'shrub') {
      leaf.blob(V(0, 0.16, 0), V(0.3, 0.2, 0.3), W, 5);
    } else if (kind === 'palm') {
      // 略弯的树干 + 六片下垂的羽叶（正反两面）
      let x = 0;
      for (let k = 0; k < 4; k++) {
        trunk.cylinder(V(x, k * 0.24, 0), 0.05 - k * 0.004, 0.046 - k * 0.004, 0.25, 4, TRUNK, false);
        x += 0.02 + k * 0.015;
      }
      const top = V(x, 0.98, 0);
      for (let k = 0; k < 6; k++) {
        const a = k * Math.PI / 3 + 0.3, ca = Math.cos(a), sa = Math.sin(a);
        const mid = V(top.x + ca * 0.28, top.y + 0.1, top.z + sa * 0.28), tip = V(top.x + ca * 0.58, top.y - 0.2, top.z + sa * 0.58);
        const ml = V(mid.x - sa * 0.11, mid.y, mid.z + ca * 0.11), mr = V(mid.x + sa * 0.11, mid.y, mid.z - ca * 0.11);
        const c = k % 2 ? W : W2;
        leaf.tri(top, ml, tip, c); leaf.tri(top, tip, mr, c);
        leaf.tri(top, tip, ml, c); leaf.tri(top, mr, tip, c);
      }
      leaf.blob(V(top.x, top.y - 0.02, 0), V(0.07, 0.06, 0.07), W2, 2);
    }
    const a = trunk.count ? trunk.toGeometry() : null, b = leaf.toGeometry();
    const na = a ? a.getAttribute('position').count : 0, nb = b.getAttribute('position').count;
    const g = new THREE.BufferGeometry();
    for (const name of ['position', 'normal', 'color']) {
      const arr = new Float32Array((na + nb) * 3);
      if (a) arr.set(a.getAttribute(name).array, 0);
      arr.set(b.getAttribute(name).array, na * 3);
      g.setAttribute(name, new THREE.BufferAttribute(arr, 3));
    }
    const lf = new Float32Array(na + nb);
    lf.fill(1, na);
    g.setAttribute('sgLeaf', new THREE.BufferAttribute(lf, 1));
    g.computeBoundingBox();
    g.computeBoundingSphere();
    if (a) a.dispose();
    b.dispose();
    _treeGeo[kind] = g;
    return g;
  }

  // ---------------------------------------------------------- MapView --
  class MapView {
    constructor() {
      this.root = null;
      this.cities = new Map();
      this.time = 0;
      this._cityX = null; this._cityZ = null;
      this._grid = null;
      this._geo = null;
      this._clouds = [];
      this._selected = -1;
      this._selectRing = null;
      this._flags = null;
      this._rings = null;
      this._marchers = new Set();
      this._lanes = new Map();
      this._build = null;
      this.region = 'china';
      this.bounds = { xMin: 0, yMin: 0, xMax: MapW, yMax: MapH };
      this.buildStats = null;
      this._prog = 0; this._progLabel = '';
    }

    // ------------------------------------------------------ 高度 --
    // Height(x, z)：地图坐标（Unity x / z）
    height(x, z) {
      const G = this._geo;
      const coast = G.coastDist(x, z);
      const sea = G.inSea(x, z);
      let h;
      if (sea) h = -0.4 - Math.min(coast, 12) * 0.35;
      else h = 0.35 + Math.min(coast, 10) * 0.08;
      const n = fbm(x * 0.045, z * 0.045);
      if (!sea) h += (n - 0.4) * 2.4;
      const f = G.sample(x, z);
      const wOld = f[F_OLD];
      // 旧版西部高原（只在中国本部起作用），与世界高原按权重混合
      if (wOld > 0) {
        let p = M.smoothStep(0, 1, M.inverseLerp(22, 2, x)) * 4.5 * (0.7 + n * 0.6);
        p += M.smoothStep(0, 1, M.inverseLerp(30, 8, x)) * M.smoothStep(0, 1, M.inverseLerp(10, 40, z)) * 2.0;
        // 世界地图：旧版的西部高原只保留青藏东缘（约北纬 25.5°–39.5°），
        // 云南、河西走廊以北不再是一整片雪原，与世界数据的青藏高原接续（经典剧本不受影响）
        if (this.region === 'world') p *= M.smoothStep(0, 1, M.inverseLerp(8, 20, z)) * M.smoothStep(0, 1, M.inverseLerp(96, 84, z));
        h += wOld >= 1 ? p : p * wOld;
      }
      if (!sea) {
        const P = f[F_PLAT];
        if (P > 0.001 && wOld < 1) h += (1 - wOld) * P * (0.7 + n * 0.6);
        // 沙丘
        const des = f[F_DESERT];
        if (des > 0.02) h += des * (fbm(x * 0.3 + 40, z * 0.3 + 40, 2) - 0.45) * 0.8;
        // 山脉
        h += G.ridges.sum(x, z);
      }
      // 河流与湖泊（宽度系数 rW、河床深度 rD；旧版为 1 与 −0.35）
      const rd = G.riverDist(x, z) / G.rW;
      if (rd < 2.2 && !sea) {
        const d = G.rD;
        const target = rd < 0.75 ? d : M.lerp(d * (0.1 / 0.35), h, (rd - 0.75) / 1.45);
        h = Math.min(h, target);
      }
      // 城市周围平整
      const ci = this._cityIdx;
      if (ci) {
        const list = ci.at(x, z);
        if (list) {
          const cx = this._cityX, cz = this._cityZ;
          for (let q = 0; q < list.length; q++) {
            const i = list[q];
            const d = Math.sqrt((x - cx[i]) * (x - cx[i]) + (z - cz[i]) * (z - cz[i]));
            if (d < 2.6) { const k = M.smoothStep(1, 0, Math.max(0, d - 1.2) / 1.4); h = M.lerp(h, M.clamp(h, 0.5, 1.2), k); }
          }
        }
      }
      return h;
    }
    // 实际渲染出的地表高度（按地形三角形插值）；超出地形网格时退回 height()
    surfaceY(x, z) {
      const g = this._grid;
      if (!g) return this.height(x, z);
      const fx0 = (x - g.x0) / g.step, fz0 = (z - g.z0) / g.step;
      const i = Math.floor(fx0), j = Math.floor(fz0);
      if (i < 0 || j < 0 || i >= g.nx || j >= g.nz) return this.height(x, z);
      const fx = fx0 - i, fz = fz0 - j, W = g.nz + 1, hs = g.hs;
      const ha = hs[i * W + j], hb = hs[i * W + j + 1], hc = hs[(i + 1) * W + j + 1], hd = hs[(i + 1) * W + j];
      if (((i + j) & 1) === 0) {
        if (fz >= fx) return ha + fz * (hb - ha) + fx * (hc - hb);
        return ha + fx * (hd - ha) + fz * (hc - hd);
      }
      if (fx + fz <= 1) return ha + fx * (hd - ha) + fz * (hb - ha);
      return hc + (1 - fx) * (hb - hc) + (1 - fz) * (hd - hc);
    }
    isForest(x, z, h) { return h >= 0.6 && h < 3.2 && fbm(x * 0.09 + 100, z * 0.09 + 100, 3) > 0.6; }

    // ------------------------------------------------------ 构建 --
    _spec(state, opts) {
      let r = opts && opts.region;
      if (!r || r === 'auto') {
        r = 'china';
        for (const c of state.cities) {
          const p = SG.mapPos ? SG.mapPos(c) : { x: (c.lon - 100) * 5, y: (c.lat - 23) * 5.6 };
          if (p.x < -20 || p.x > MapW + 20 || p.y < -20 || p.y > MapH + 20) { r = 'world'; break; }
        }
      }
      // 没有载入 world-geo.js 时只能构建旧版中国地图
      return r === 'world' && SG.World && SG.WorldGeo ? worldSpec() : classicSpec();
    }
    build(state, opts) {
      this._cancelBuild();
      const t0 = now();
      const it = this._steps(state, opts || {});
      while (!it.next().done) { /* 同步跑完 */ }
      this.buildStats = { totalMs: now() - t0, maxSliceMs: now() - t0, slices: 1, stages: this._stageMs, sync: true };
      return this.root;
    }
    async buildAsync(state, opts) {
      opts = opts || {};
      this._cancelBuild();
      const token = { cancelled: false };
      this._build = token;
      const budget = opts.sliceMs > 0 ? opts.sliceMs : 12;
      const it = this._steps(state, opts);
      const tStart = now();
      let t0 = tStart, maxSlice = 0, maxStage = '', slices = 0, lastPaint = tStart;
      const sliceBy = {};
      const report = () => { if (opts.onProgress) { try { opts.onProgress(M.clamp01(this._prog), this._progLabel); } catch (e) { console.warn(e); } } };
      report();
      const steps = [];   // 调试：最长的单步（opts.traceSteps）
      for (;;) {
        const ts = opts.traceSteps ? now() : 0;
        const r = it.next();
        if (r.done) break;
        const t = now();
        if (opts.traceSteps && t - ts > 8) steps.push([Math.round(t - ts), this._progLabel, this._traceTag || '']);
        if (t - t0 >= budget) {
          if (t - t0 > maxSlice) { maxSlice = t - t0; maxStage = this._progLabel; }
          if (!(sliceBy[this._progLabel] >= t - t0)) sliceBy[this._progLabel] = Math.round(t - t0);
          slices++;
          report();
          // 约每 100ms 等一帧，让载入画面重绘进度
          if (t - lastPaint > 100 && typeof requestAnimationFrame === 'function' && !(typeof document !== 'undefined' && document.hidden)) {
            await new Promise(res => requestAnimationFrame(() => res()));
            lastPaint = now();
          } else await yieldTask();
          if (token.cancelled) { it.return(); return null; }
          t0 = now();
        }
      }
      maxSlice = Math.max(maxSlice, now() - t0); slices++;
      this._prog = 1; report();
      this._build = null;
      this.buildStats = { totalMs: now() - tStart, maxSliceMs: maxSlice, maxSliceStage: maxStage, maxSliceByStage: sliceBy, longSteps: opts.traceSteps ? steps.sort((a, b) => b[0] - a[0]).slice(0, 12) : undefined, slices, stages: this._stageMs, sync: false };
      return this.root;
    }
    _cancelBuild() { if (this._build) { this._build.cancelled = true; this._build = null; } }

    *_steps(state, opts) {
      if (this.root) this._dispose();
      const spec = this._spec(state, opts);
      this.spec = spec;
      this.region = spec.key;
      this.bounds = Object.assign({}, spec.bounds);
      const Gfx = SG.Gfx;
      this.root = new THREE.Group();
      this.root.name = 'StrategyMap';
      Gfx.scene.add(this.root);
      this.cities = new Map();
      this._cityX = state.cities.map(c => SG.mapPos ? SG.mapPos(c).x : (c.lon - 100) * 5);
      this._cityZ = state.cities.map(c => SG.mapPos ? SG.mapPos(c).y : (c.lat - 23) * 5.6);
      this._cityIdx = this._makeCityIndex(3.4);
      this._grid = null;
      // 各阶段的进度权重
      const stages = [['geo', '测绘山川', 0.24], ['terrain', '堆砌地形', 0.3], ['skirt', '远景', 0.04], ['water', '江海', 0.1],
        ['trees', '林木', 0.14], ['roads', '道路航线', 0.05], ['cities', '城池', 0.1], ['misc', '云', 0.03]];
      this._stageMs = {};
      let base = 0;
      const run = function* (self, s, gen) {
        const t = now();
        self._progLabel = s[1];
        self._stage = { base, w: s[2] };
        self._prog = base;
        yield* gen;
        base += s[2];
        self._prog = base;
        self._stageMs[s[0]] = Math.round(now() - t);
      };
      const out = {};
      yield* run(this, stages[0], geoFor(spec, out));
      this._geo = out.geo;
      yield* run(this, stages[1], this._genTerrain());
      yield* run(this, stages[2], this._genSkirt());
      yield* run(this, stages[3], this._genWater());
      yield* run(this, stages[4], this._genTrees(state));
      yield* run(this, stages[5], this._genRoads(state));
      yield* run(this, stages[6], this._genCities(state));
      const self = this;
      yield* run(this, stages[7], (function* () { self._buildClouds(); self._buildSelectRing(); self.refresh(state); yield; })());
    }
    _sub(frac) { if (this._stage) this._prog = this._stage.base + this._stage.w * M.clamp01(frac); }

    _makeCityIndex(radius) {
      const cx = this._cityX, cz = this._cityZ, cell = 4;
      if (!cx || !cx.length) return null;
      let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
      for (let i = 0; i < cx.length; i++) { x0 = Math.min(x0, cx[i]); x1 = Math.max(x1, cx[i]); z0 = Math.min(z0, cz[i]); z1 = Math.max(z1, cz[i]); }
      x0 -= radius + 1; z0 -= radius + 1; x1 += radius + 1; z1 += radius + 1;
      const nx = Math.ceil((x1 - x0) / cell) + 1, nz = Math.ceil((z1 - z0) / cell) + 1;
      const b = new Array(nx * nz).fill(null);
      for (let i = 0; i < cx.length; i++) {
        const i0 = Math.floor((cx[i] - radius - x0) / cell), i1 = Math.floor((cx[i] + radius - x0) / cell);
        const j0 = Math.floor((cz[i] - radius - z0) / cell), j1 = Math.floor((cz[i] + radius - z0) / cell);
        for (let a = i0; a <= i1; a++) for (let c = j0; c <= j1; c++) { const o = c * nx + a; (b[o] || (b[o] = [])).push(i); }
      }
      return {
        at(x, z) {
          const a = Math.floor((x - x0) / cell), c = Math.floor((z - z0) / cell);
          if (a < 0 || c < 0 || a >= nx || c >= nz) return null;
          return b[c * nx + a];
        },
      };
    }
    _nearCity(x, z, r) {
      const list = this._cityIdx && this._cityIdx.at(x, z);
      if (!list) return false;
      for (const i of list) if (Math.hypot(this._cityX[i] - x, this._cityZ[i] - z) < r) return true;
      return false;
    }

    _dispose() {
      for (const m of this._marchers) m.cancelled = true;
      SG.Gfx.disposeTree(this.root);
      this.root = null;
      // 云的材质每次构建新建（每朵云的主材质、阴影深度材质，以及各朵云共用的 CloudDepth 前置遍材质），随云一起释放
      for (const c of this._clouds) {
        c.material.dispose();
        if (c.customDepthMaterial) c.customDepthMaterial.dispose();
        for (const ch of c.children) if (ch.material) ch.material.dispose();
      }
      this._clouds = [];
      this._flags = null; this._rings = null; this._selectRing = null; this._debug = null; this._treeMeshes = null;
      this._lanes = new Map();
      this._restoreFog();
    }

    *_genTerrain() {
      const T = this.spec.terrain, step = T.step;
      const x0 = T.x0, z0 = T.z0;
      const nx = Math.ceil((T.x1 - x0) / step), nz = Math.ceil((T.z1 - z0) / step);
      const W = nz + 1;
      const hs = new Float32Array((nx + 1) * W);
      for (let i = 0; i <= nx; i++) {
        for (let j = 0; j <= nz; j++) hs[i * W + j] = this.height(x0 + i * step, z0 + j * step);
        this._sub(0.6 * i / nx);
        yield;
      }
      this._grid = { x0, z0, step, nx, nz, hs };
      const rnd = SG.SeededRandom(7);
      const mat = terrainMaterial();
      const geo = this._geo;
      // 分块，避免单个网格过大，并让视锥剔除生效；四角都在深海（−4.6，远海海床同高）的格子不画
      const chunk = T.chunk, DEEP = -4.59;
      const nChunks = Math.ceil(nx / chunk) * Math.ceil(nz / chunk);
      let done = 0;
      for (let ci = 0; ci < nx; ci += chunk)
        for (let cj = 0; cj < nz; cj += chunk) {
          const iEnd = Math.min(nx, ci + chunk), jEnd = Math.min(nz, cj + chunk);
          let quads = 0;
          for (let i = ci; i < iEnd; i++) for (let j = cj; j < jEnd; j++) {
            if (hs[i * W + j] > DEEP || hs[i * W + j + 1] > DEEP || hs[(i + 1) * W + j + 1] > DEEP || hs[(i + 1) * W + j] > DEEP) quads++;
          }
          if (quads > 0) {
            const P = new Float32Array(quads * 18), N = new Float32Array(quads * 18), C = new Float32Array(quads * 18);
            let o = 0;
            for (let i = ci; i < iEnd; i++)
              for (let j = cj; j < jEnd; j++) {
                const ha = hs[i * W + j], hb = hs[i * W + j + 1], hc = hs[(i + 1) * W + j + 1], hd = hs[(i + 1) * W + j];
                if (ha <= DEEP && hb <= DEEP && hc <= DEEP && hd <= DEEP) continue;
                const xa = x0 + i * step, xb = xa + step, za = z0 + j * step, zb = za + step;
                if (((i + j) & 1) === 0) {
                  o = this._terrainTri(P, N, C, o, geo, rnd, xa, ha, za, xa, hb, zb, xb, hc, zb, false);
                  o = this._terrainTri(P, N, C, o, geo, rnd, xa, ha, za, xb, hc, zb, xb, hd, za, false);
                } else {
                  o = this._terrainTri(P, N, C, o, geo, rnd, xa, ha, za, xa, hb, zb, xb, hd, za, false);
                  o = this._terrainTri(P, N, C, o, geo, rnd, xa, hb, zb, xb, hc, zb, xb, hd, za, false);
                }
              }
            const m = SG.Gfx.mesh(triGeometry(P, N, C, o / 3), mat, { castShadow: true, receiveShadow: true });
            m.name = 'Terrain';
            this.root.add(m);
          }
          this._sub(0.6 + 0.4 * (++done) / nChunks);
          yield;
        }
    }
    // 一个地形三角形（Unity 坐标 a, b, c）：按重心取色，写入 P/N/C；返回新的写入位置
    _terrainTri(P, N, C, o, geo, rnd, ax, ay, az, bx, by, bz, cx, cy, cz, noForest) {
      const mx = (ax + bx + cx) / 3, my = (ay + by + cy) / 3, mz = (az + bz + cz) / 3;
      const ux = bx - ax, uy = by - ay, uz = bz - az, vx = cx - ax, vy = cy - ay, vz = cz - az;
      const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
      const slope = 1 - Math.abs(ny / (Math.sqrt(nx * nx + ny * ny + nz * nz) || 1));
      const col = groundColor(geo, mx, mz, my, slope, (rnd.nextDouble() - 0.5) * 0.06, noForest);
      emitTri(P, N, C, o, ax, ay, az, bx, by, bz, cx, cy, cz, col);
      return o + 9;
    }

    // 远景地形：主地形网格之外的粗网格，使远处与旋转视角下不露出空洞（略低于主网格，重叠处被遮住）
    *_genSkirt() {
      const g = this._grid, S = this.spec.skirt;
      const fx0 = g.x0, fz0 = g.z0, fx1 = g.x0 + g.nx * g.step, fz1 = g.z0 + g.nz * g.step;
      const step = S.step, X0 = S.x0, Z0 = S.z0;
      const nx = Math.ceil((S.x1 - X0) / step), nz = Math.ceil((S.z1 - Z0) / step), W = nz + 1;
      const inner = (i, j) => {
        const xa = X0 + i * step, za = Z0 + j * step;
        return xa >= fx0 && xa + step <= fx1 && za >= fz0 && za + step <= fz1;
      };
      const hs = new Float32Array((nx + 1) * W).fill(NaN);
      const H = (i, j) => {
        let h = hs[i * W + j];
        if (h !== h) { h = this.height(X0 + i * step, Z0 + j * step) - 0.3; hs[i * W + j] = h; }
        return h;
      };
      const rnd = SG.SeededRandom(17);
      const geo = this._geo, DEEP = -4.89, tile = S.tile;
      const nTiles = Math.ceil(nx / tile) * Math.ceil(nz / tile);
      let done = 0;
      for (let ti = 0; ti < nx; ti += tile)
        for (let tj = 0; tj < nz; tj += tile) {
          const cells = [];
          for (let i = ti; i < Math.min(nx, ti + tile); i++)
            for (let j = tj; j < Math.min(nz, tj + tile); j++) {
              if (inner(i, j)) continue;
              const a = H(i, j), b = H(i, j + 1), c = H(i + 1, j + 1), d = H(i + 1, j);
              if (a <= DEEP && b <= DEEP && c <= DEEP && d <= DEEP) continue;
              cells.push(i, j);
            }
          if (cells.length) {
            const n = cells.length >> 1;
            const P = new Float32Array(n * 18), N = new Float32Array(n * 18), C = new Float32Array(n * 18);
            let o = 0;
            for (let q = 0; q < cells.length; q += 2) {
              const i = cells[q], j = cells[q + 1];
              const xa = X0 + i * step, za = Z0 + j * step, xb = xa + step, zb = za + step;
              const ha = H(i, j), hb = H(i, j + 1), hc = H(i + 1, j + 1), hd = H(i + 1, j);
              if (((i + j) & 1) === 0) {
                o = this._terrainTri(P, N, C, o, geo, rnd, xa, ha, za, xa, hb, zb, xb, hc, zb, true);
                o = this._terrainTri(P, N, C, o, geo, rnd, xa, ha, za, xb, hc, zb, xb, hd, za, true);
              } else {
                o = this._terrainTri(P, N, C, o, geo, rnd, xa, ha, za, xa, hb, zb, xb, hd, za, true);
                o = this._terrainTri(P, N, C, o, geo, rnd, xa, hb, zb, xb, hc, zb, xb, hd, za, true);
              }
            }
            const m = SG.Gfx.mesh(triGeometry(P, N, C, o / 3), terrainMaterial(), { castShadow: false, receiveShadow: true });
            m.name = 'TerrainFar';
            this.root.add(m);
          }
          this._sub((++done) / nTiles);
          yield;
        }
      // 远海海床：深水下方（与深海地形 −4.6 同高），免得透过水面看到天空
      const mb = new SG.MeshBuilder();
      const bed = new THREE.Color(SEABED[0], SEABED[1], SEABED[2]);
      const E = 1600, by = -4.62, cx = (S.x0 + S.x1) / 2, cz = (S.z0 + S.z1) / 2;
      mb.quad(V(cx - E, by, cz - E), V(cx - E, by, cz + E), V(cx + E, by, cz + E), V(cx + E, by, cz - E), bed);
      const bm = SG.Gfx.mesh(mb.toGeometry(), terrainMaterial(), { castShadow: false, receiveShadow: true });
      bm.name = 'SeaBed';
      bm.frustumCulled = false;
      this.root.add(bm);
    }

    *_genWater() {
      const Wt = this.spec.water;
      const shore = h => M.clamp01((h + 2.2) / 2.2);
      const step = Wt.step, x0 = Wt.x0, z0 = Wt.z0;
      const nx = Math.ceil((Wt.x1 - x0) / step), nz = Math.ceil((Wt.z1 - z0) / step), W = nz + 1;
      const ex1 = x0 + nx * step, ez1 = z0 + nz * step;
      const hg = new Float32Array((nx + 1) * W);
      for (let i = 0; i <= nx; i++) {
        for (let j = 0; j <= nz; j++) hg[i * W + j] = this.height(x0 + i * step, z0 + j * step);
        this._sub(0.7 * i / nx);
        yield;
      }
      const mat = SG.Gfx.water();
      // 顶点属性：r = 岸边系数（BuildWater 的顶点色）；g = 1 表示不做波浪起伏（内圈网格之外的远海，
      // 这样远海大格子与细网格拼接处不会因起伏不同而露缝）
      let pos = [], col = [];
      // 一个格子：Unity 顶点 a(xa,za) b(xa,za+sz) c(xa+sx,za+sz) d(xa+sx,za)，三角形 (a,b,c)(a,c,d)，镜像后反向输出
      const quad = (xa, za, sx, sz, ca, cb, cc, cd) => {
        pos.push(xa, 0, -za, xa + sx, 0, -(za + sz), xa, 0, -(za + sz), xa, 0, -za, xa + sx, 0, -za, xa + sx, 0, -(za + sz));
        col.push(ca[0], ca[1], 0, cc[0], cc[1], 0, cb[0], cb[1], 0, ca[0], ca[1], 0, cd[0], cd[1], 0, cc[0], cc[1], 0);
      };
      const flush = name => {
        if (!pos.length) return;
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
        g.computeBoundingSphere();
        const m = new THREE.Mesh(g, mat);
        m.name = name;
        m.renderOrder = -10; // Queue Transparent-10
        m.castShadow = false; m.receiveShadow = false;
        this.root.add(m);
        pos = []; col = [];
      };
      const tile = Wt.tile;
      for (let ti = 0; ti < nx; ti += tile) {
        for (let tj = 0; tj < nz; tj += tile) {
          for (let i = ti; i < Math.min(nx, ti + tile); i++)
            for (let j = tj; j < Math.min(nz, tj + tile); j++) {
              const h00 = hg[i * W + j], h01 = hg[i * W + j + 1], h11 = hg[(i + 1) * W + j + 1], h10 = hg[(i + 1) * W + j];
              // 四角都远高于水面的格子略过以节省顶点
              if (h00 > 1.2 && h10 > 1.2 && h01 > 1.2 && h11 > 1.2) continue;
              quad(x0 + i * step, z0 + j * step, step, step, [shore(h00), 0], [shore(h01), 0], [shore(h11), 0], [shore(h10), 0]);
            }
          flush('Water');
          yield;
        }
        this._sub(0.7 + 0.25 * ti / nx);
        yield;
      }
      // 外海第一圈：宽 20，沿内圈边界按 2.5 细分（与内圈边界顶点完全重合）
      const band = Wt.band, far = Wt.far, nb = Math.round(band / step);
      const FAR = [0, 1];
      const hAtI = (i, j) => hg[i * W + j];
      const inA = (i, j) => [shore(hAtI(i, j)), 0];
      const land = (ia, ja, ib, jb) => hAtI(ia, ja) > 1.2 && hAtI(ib, jb) > 1.2;
      for (let i = 0; i < nx; i++) {
        const xa = x0 + i * step;
        if (!land(i, 0, i + 1, 0)) quad(xa, z0 - band, step, band, FAR, inA(i, 0), inA(i + 1, 0), FAR);
        if (!land(i, nz, i + 1, nz)) quad(xa, ez1, step, band, inA(i, nz), FAR, FAR, inA(i + 1, nz));
      }
      for (let j = -nb; j < nz + nb; j++) {
        const za = z0 + j * step;
        const onEdge = j >= 0 && j < nz;
        // 边界上（0 ≤ 行号 ≤ nz）的顶点取岸边系数，其余为远海
        const in0 = j >= 0 && j <= nz, in1 = j + 1 >= 0 && j + 1 <= nz;
        if (!(onEdge && land(0, j, 0, j + 1))) quad(x0 - band, za, band, step, FAR, FAR, in1 ? inA(0, j + 1) : FAR, in0 ? inA(0, j) : FAR);
        if (!(onEdge && land(nx, j, nx, j + 1))) quad(ex1, za, band, step, in0 ? inA(nx, j) : FAR, in1 ? inA(nx, j + 1) : FAR, FAR, FAR);
      }
      // 最外圈：四个互不重叠的矩形，约 20 的格子
      const bx0 = x0 - band, bz0 = z0 - band, bx1 = ex1 + band, bz1 = ez1 + band;
      const rect = (ax, az, bx, bz) => {
        const cx = Math.max(1, Math.round((bx - ax) / 20)), cz = Math.max(1, Math.round((bz - az) / 20));
        const sx = (bx - ax) / cx, sz = (bz - az) / cz;
        for (let i = 0; i < cx; i++) for (let j = 0; j < cz; j++) quad(ax + i * sx, az + j * sz, sx, sz, FAR, FAR, FAR, FAR);
      };
      rect(bx0 - far, bz0 - far, bx0, bz1 + far);
      rect(bx1, bz0 - far, bx1 + far, bz1 + far);
      rect(bx0, bz0 - far, bx1, bz0);
      yield;
      rect(bx0, bz1, bx1, bz1 + far);
      flush('WaterFar');
      yield;
    }

    // 树木：按区块（tile）与树种分组的实例化网格；树种与叶色随群落变化（中国本部与旧版相同）
    *_genTrees() {
      const rnd = SG.SeededRandom(11);
      const geo = this._geo, D = this.spec.trees, tileSize = this.spec.tile, T = this.spec.terrain;
      const buckets = new Map();
      let count = 0;
      const add = (kind, px, py, pz, s, sy, yaw, color) => {
        const key = Math.floor((px - T.x0) / tileSize) + ',' + Math.floor((pz - T.z0) / tileSize) + ',' + kind;
        let b = buckets.get(key);
        if (!b) { b = { kind, m: [], c: [] }; buckets.set(key, b); }
        b.m.push(px, py, pz, s, sy, yaw);
        b.c.push(color.r, color.g, color.b);
        count++;
      };
      const C = (r, g, b) => new THREE.Color(r, g, b);
      const PINE = C(0.2, 0.42, 0.28), DEC_S = C(0.3, 0.58, 0.26), DEC_N = C(0.5, 0.6, 0.28);
      const BOREAL = C(0.15, 0.36, 0.25), TROP = C(0.14, 0.48, 0.18), PALM = C(0.3, 0.58, 0.2), OLIVE = C(0.47, 0.54, 0.34);
      const CYPRESS = C(0.15, 0.33, 0.2), SHRUB = C(0.55, 0.52, 0.3), ACACIA = C(0.42, 0.5, 0.22);
      const xs = [], classic = this.region === 'china';
      for (let x = D.x0; x < D.x1; x += 1.15) xs.push(x);
      for (let xi = 0; xi < xs.length; xi++) {
        const x = xs[xi];
        for (let z = D.z0; z < D.z1; z += 1.15) {
          const px = x + (rnd.nextDouble() - 0.5) * 0.9, pz = z + (rnd.nextDouble() - 0.5) * 0.9;
          // 经典地图按 height() 判定（与旧版相同）；世界地图用便宜得多的 surfaceY()
          const h = classic ? this.height(px, pz) : this.surfaceY(px, pz);
          if (h <= 0.5 || h >= 3.2) continue;
          const f = geo.sample(px, pz);
          const des = f[F_DESERT], st = f[F_STEPPE], sav = f[F_SAV], med = f[F_MED], tr = f[F_TROP], bo = f[F_BOREAL];
          const biome = des + st + sav + med + tr + bo > 0.02;
          const thr = biome ? 0.6 + des * 0.4 + st * 0.15 + sav * 0.04 - tr * 0.07 - bo * 0.05 : 0.6;
          const forest = h >= 0.6 && fbm(px * 0.09 + 100, pz * 0.09 + 100, 3) > thr;
          const pSparse = biome ? Math.max(0, 0.025 * (1 - 0.9 * des - 0.6 * st) + 0.035 * sav + 0.02 * med) : 0.025;
          const sparse = h < 2.5 && rnd.nextDouble() < pSparse;
          let oasisTree = false;
          if (!forest && !sparse && des > 0.35 && h < 1.6) {
            const rd = geo.riverDist(px, pz) / geo.rW;
            oasisTree = rd > 1.3 && rd < 3.2 && rnd.nextDouble() < 0.3;
          }
          if (!forest && !sparse && !oasisTree) continue;
          if (this._nearCity(px, pz, 3.4)) continue;
          if (geo.riverDist(px, pz) < 1.4) continue;
          const north = M.inverseLerp(10, 95, pz);
          // 随机数的取用顺序与旧版相同（针叶判定 → 大小 → 色差），中国本部的树木位置与旧版一致；朝向取位置散列
          const v = rnd.nextDouble();
          const s = 0.9 + rnd.nextDouble() * 0.6;
          const jit = (rnd.nextDouble() - 0.5) * 0.12;
          const hy = Math.sin(px * 12.9898 + pz * 78.233) * 43758.5453;
          const y = (classic ? this.surfaceY(px, pz) : h) - 0.05, yaw = (hy - Math.floor(hy)) * Math.PI * 2;
          if (oasisTree || (des > 0.5 && v < 0.35)) { add('palm', px, y, pz, s * 1.05, s * 1.05, yaw, SG.Gfx.shade(PALM, jit)); continue; }
          if (des > 0.45 || (st > 0.5 && !forest)) { add('shrub', px, y, pz, s, s, yaw, SG.Gfx.shade(SHRUB, jit)); continue; }
          if (tr > 0.45) {
            if (v < 0.3) add('palm', px, y, pz, s * 1.1, s * 1.1, yaw, SG.Gfx.shade(PALM, jit));
            else add(v < 0.65 ? 'broad0' : 'broad1', px, y, pz, s * 1.1, s * 1.2, yaw, SG.Gfx.shade(TROP, jit));
            continue;
          }
          if (med > 0.45) {
            if (v < 0.3) add('cypress', px, y, pz, s, s, yaw, SG.Gfx.shade(CYPRESS, jit));
            else add(v < 0.65 ? 'broad0' : 'broad1', px, y, pz, s * 0.8, s * 0.6, yaw, SG.Gfx.shade(OLIVE, jit));
            continue;
          }
          if (sav > 0.45 && !forest) { add('broad1', px, y, pz, s * 1.25, s * 0.55, yaw, SG.Gfx.shade(ACACIA, jit)); continue; }
          if (bo > 0.4) { add('pine', px, y, pz, s, s * 1.15, yaw, SG.Gfx.shade(BOREAL, jit)); continue; }
          const pine = v < 0.25 + north * 0.6 * f[F_EAST] || h > 2;
          if (pine) add('pine', px, y, pz, s, s, yaw, SG.Gfx.shade(PINE, jit));
          else add(count & 1 ? 'broad1' : 'broad0', px, y, pz, s, s, yaw, SG.Gfx.shade(SG.Gfx.lerpColor(DEC_S, DEC_N, north * f[F_EAST]), jit));
        }
        if ((xi & 1) === 1) { this._sub(0.9 * xi / xs.length); yield; }
      }
      const mat = treeMaterial();
      this._treeMeshes = [];
      this._treesHidden = false;
      const q = new THREE.Quaternion(), sc = new THREE.Vector3(), tp = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
      const col = new THREE.Color();
      for (const b of buckets.values()) {
        const n = b.c.length / 3;
        const im = new THREE.InstancedMesh(treeGeometry(b.kind), mat, n);
        im.name = 'Trees';
        im.userData.sharedGeometry = true;
        im.castShadow = true; im.receiveShadow = true;
        for (let i = 0; i < n; i++) {
          const o = i * 6;
          tp.set(b.m[o], b.m[o + 1], -b.m[o + 2]);
          q.setFromAxisAngle(up, b.m[o + 5]);
          sc.set(b.m[o + 3], b.m[o + 4], b.m[o + 3]);
          _m4.compose(tp, q, sc);
          im.setMatrixAt(i, _m4);
          col.setRGB(b.c[i * 3], b.c[i * 3 + 1], b.c[i * 3 + 2]);
          im.setColorAt(i, col);
        }
        im.instanceMatrix.needsUpdate = true;
        if (im.instanceColor) im.instanceColor.needsUpdate = true;
        im.computeBoundingSphere();
        this.root.add(im);
        this._treeMeshes.push(im);
        yield;
      }
      this.treeCount = count;
      yield;
    }

    *_genRoads(state) {
      const mb = new SG.MeshBuilder();
      const col = new THREE.Color(0.78, 0.68, 0.5);
      const done = new Set();
      const cities = state.cities;
      const seaPairs = [];
      const isSea = typeof SG.linkIsSea === 'function' ? SG.linkIsSea : null;
      for (const c of cities) {
        if ((c.id & 7) === 7) yield;
        for (const li of c.links) {
          const o = cities[li];
          if (!o) continue;
          const key = Math.min(c.id, o.id) + '-' + Math.max(c.id, o.id);
          if (done.has(key)) continue;
          done.add(key);
          let sea = false;
          if (isSea) { try { sea = !!isSea(c.id, o.id); } catch (e) { sea = false; } }
          if (sea) { seaPairs.push([Math.min(c.id, o.id), Math.max(c.id, o.id)]); continue; }
          const ax = this._cityX[c.id], ay = this._cityZ[c.id], bx = this._cityX[o.id], by = this._cityZ[o.id];
          const len = Math.hypot(bx - ax, by - ay);
          const n = Math.ceil(len / 0.9);
          const dx = (bx - ax) / len, dy = (by - ay) / len;
          const sx = -dy * 0.22, sy = dx * 0.22;
          const bend = (M.perlinNoise(c.id, o.id) - 0.5) * len * 0.12;
          let prevL = null, prevR = null;
          for (let i = 0; i <= n; i++) {
            const f = i / n;
            // 道路略带弯曲
            const w = Math.sin(f * Math.PI) * bend;
            const px = ax + (bx - ax) * f - dy * w, py = ay + (by - ay) * f + dx * w;
            const h = Math.max(0.12, this.surfaceY(px, py)) + 0.07;
            const l = V(px + sx, h, py + sy), r = V(px - sx, h, py - sy);
            if (i > 0) mb.quad(prevL, l, r, prevR, col);
            prevL = l; prevR = r;
          }
        }
      }
      if (mb.count > 0) {
        const mat = SG.Gfx.newLowPoly();
        mat.polygonOffset = true; mat.polygonOffsetFactor = -1; mat.polygonOffsetUnits = -3;
        const m = SG.Gfx.mesh(mb.toGeometry(), mat, { castShadow: false, receiveShadow: true });
        m.name = 'Roads';
        this.root.add(m);
      }
      yield;
      // 海路：沿水面绕开陆地的虚线航线
      if (seaPairs.length) {
        yield* this._genSeaMask();
        const P = [];
        for (let k = 0; k < seaPairs.length; k++) {
          const [a, b] = seaPairs[k];
          const path = this._seaPath(this._cityX[a], this._cityZ[a], this._cityX[b], this._cityZ[b]);
          this._lanes.set(a + '-' + b, path);
          this._dashes(path, P);
          this._sub(0.2 + 0.8 * (k + 1) / seaPairs.length);
          yield;
        }
        this._sea = null;   // 只在生成航线时使用
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(P, 3));
        g.computeBoundingSphere();
        const mat = new THREE.MeshBasicMaterial({
          color: new THREE.Color(0.97, 0.94, 0.84), transparent: true, opacity: 0.85, depthWrite: false,
          side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -4,
        });
        const m = new THREE.Mesh(g, mat);
        m.name = 'SeaLanes';
        m.renderOrder = 3;
        m.castShadow = false; m.receiveShadow = false;
        this.root.add(m);
      }
    }
    // 全图海域栅格（1 单位一格）：water = 海（含内海；不含河流湖泊，也不含高于 −0.3 的浅滩）；
    // cost = 水面 1、离陆地 2 格以内 1.5、陆地 14。分行生成（可分片）
    *_genSeaMask() {
      const res = 1.0, T = this.spec.water, G = this._geo;
      const X0 = Math.floor(T.x0), Z0 = Math.floor(T.z0);
      const nx = Math.ceil(T.x1 - X0) + 1, nz = Math.ceil(T.z1 - Z0) + 1, N = nx * nz;
      const water = new Uint8Array(N), near = new Uint8Array(N), cost = new Float32Array(N);
      for (let j = 0; j < nz; j++) {
        const z = Z0 + j * res;
        for (let i = 0; i < nx; i++) { const x = X0 + i * res; water[j * nx + i] = G.inSea(x, z) && this.surfaceY(x, z) < -0.3 ? 1 : 0; }
        if ((j & 7) === 7) yield;
      }
      // 陆地向外膨胀 2 格（先横后纵，可分离的最大值滤波）
      const tmp = new Uint8Array(N);
      for (let j = 0; j < nz; j++) for (let i = 0; i < nx; i++) {
        let v = 0;
        for (let d = -2; d <= 2 && !v; d++) { const ii = i + d; if (ii >= 0 && ii < nx && !water[j * nx + ii]) v = 1; }
        tmp[j * nx + i] = v;
      }
      yield;
      for (let j = 0; j < nz; j++) for (let i = 0; i < nx; i++) {
        let v = 0;
        for (let d = -2; d <= 2 && !v; d++) { const jj = j + d; if (jj >= 0 && jj < nz && tmp[jj * nx + i]) v = 1; }
        near[j * nx + i] = v;
      }
      for (let o = 0; o < N; o++) cost[o] = !water[o] ? 14 : near[o] ? 1.5 : 1;
      this._sea = { X0, Z0, nx, nz, res, water, cost };
      yield;
    }
    // 海上航线：海域栅格上的 A*（水面代价 1，近岸略高，陆地 14），再按视线拉直、Chaikin 平滑。返回 [x0, z0, x1, z1, ...]
    _seaPath(ax, az, bx, bz) {
      const res = 1.0, margin = 28;
      const X0 = Math.floor(Math.min(ax, bx) - margin), Z0 = Math.floor(Math.min(az, bz) - margin);
      const nx = Math.ceil((Math.max(ax, bx) + margin - X0) / res) + 1, nz = Math.ceil((Math.max(az, bz) + margin - Z0) / res) + 1;
      const N = nx * nz;
      // 从全图海域栅格（_genSeaMask）截取窗口；栅格外视为陆地
      const S = this._sea, water = new Uint8Array(N), cost = new Float32Array(N).fill(14);
      const oi = Math.round((X0 - S.X0) / res), oj = Math.round((Z0 - S.Z0) / res);
      for (let j = 0; j < nz; j++) {
        const gj = j + oj;
        if (gj < 0 || gj >= S.nz) continue;
        for (let i = 0; i < nx; i++) {
          const gi = i + oi;
          if (gi < 0 || gi >= S.nx) continue;
          const g = gj * S.nx + gi;
          water[j * nx + i] = S.water[g]; cost[j * nx + i] = S.cost[g];
        }
      }
      const cell = (x, z) => M.clamp(Math.round((z - Z0) / res), 0, nz - 1) * nx + M.clamp(Math.round((x - X0) / res), 0, nx - 1);
      const start = cell(ax, az), goal = cell(bx, bz);
      const gx = goal % nx, gz = (goal / nx) | 0;
      const gs = new Float32Array(N).fill(Infinity), from = new Int32Array(N).fill(-1), closed = new Uint8Array(N);
      // 二叉堆
      const heap = [], hf = [];
      const push = (o, f) => {
        heap.push(o); hf.push(f);
        let i = heap.length - 1;
        while (i > 0) { const p = (i - 1) >> 1; if (hf[p] <= hf[i]) break; [heap[p], heap[i]] = [heap[i], heap[p]]; [hf[p], hf[i]] = [hf[i], hf[p]]; i = p; }
      };
      const pop = () => {
        const top = heap[0], lo = heap.pop(), lf = hf.pop();
        if (heap.length) {
          heap[0] = lo; hf[0] = lf;
          let i = 0;
          for (;;) {
            const l = 2 * i + 1, r = l + 1;
            let m = i;
            if (l < heap.length && hf[l] < hf[m]) m = l;
            if (r < heap.length && hf[r] < hf[m]) m = r;
            if (m === i) break;
            [heap[m], heap[i]] = [heap[i], heap[m]]; [hf[m], hf[i]] = [hf[i], hf[m]]; i = m;
          }
        }
        return top;
      };
      gs[start] = 0;
      push(start, 0);
      const DIRS = [[1, 0, 1], [-1, 0, 1], [0, 1, 1], [0, -1, 1], [1, 1, Math.SQRT2], [1, -1, Math.SQRT2], [-1, 1, Math.SQRT2], [-1, -1, Math.SQRT2]];
      let found = false;
      while (heap.length) {
        const o = pop();
        if (closed[o]) continue;
        closed[o] = 1;
        if (o === goal) { found = true; break; }
        const i = o % nx, j = (o / nx) | 0;
        for (const [di, dj, dl] of DIRS) {
          const ii = i + di, jj = j + dj;
          if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) continue;
          const q = jj * nx + ii;
          if (closed[q]) continue;
          const g = gs[o] + dl * (cost[o] + cost[q]) * 0.5;
          if (g < gs[q]) { gs[q] = g; from[q] = o; push(q, g + Math.hypot(ii - gx, jj - gz)); }
        }
      }
      let pts = [];
      if (found) {
        for (let o = goal; o >= 0; o = from[o]) pts.push(X0 + (o % nx) * res, Z0 + ((o / nx) | 0) * res);
        const rev = [];
        for (let k = pts.length - 2; k >= 0; k -= 2) rev.push(pts[k], pts[k + 1]);
        pts = rev;
        pts[0] = ax; pts[1] = az; pts[pts.length - 2] = bx; pts[pts.length - 1] = bz;
        // 视线拉直：两点之间几乎全是水面（经过的陆地合计不超过 3 单位，如离港的一小段）时省去中间点。
        // 不能把整段陆地也算作“通畅”，否则港口到内陆城（如亚历山大—阿克苏姆）会被拉成一条横穿陆地的直线
        const clear = (x0, z0, x1, z1) => {
          const n = Math.ceil(Math.hypot(x1 - x0, z1 - z0) / (res * 0.5));
          let land = 0;
          for (let s = 1; s < n; s++) if (!water[cell(x0 + (x1 - x0) * s / n, z0 + (z1 - z0) * s / n)] && ++land > 6) return false;
          return true;
        };
        const out = [pts[0], pts[1]];
        let a = 0;
        const n2 = pts.length >> 1;
        while (a < n2 - 1) {
          let b = n2 - 1;
          while (b > a + 1 && !clear(pts[2 * a], pts[2 * a + 1], pts[2 * b], pts[2 * b + 1])) b--;
          out.push(pts[2 * b], pts[2 * b + 1]);
          a = b;
        }
        pts = out;
      } else pts = [ax, az, bx, bz];
      // Chaikin 平滑两遍（端点不动）
      for (let it = 0; it < 2 && pts.length >= 6; it++) {
        const o = [pts[0], pts[1]];
        for (let k = 0; k + 3 < pts.length; k += 2) {
          const x0 = pts[k], z0 = pts[k + 1], x1 = pts[k + 2], z1 = pts[k + 3];
          o.push(x0 * 0.75 + x1 * 0.25, z0 * 0.75 + z1 * 0.25, x0 * 0.25 + x1 * 0.75, z0 * 0.25 + z1 * 0.75);
        }
        o.push(pts[pts.length - 2], pts[pts.length - 1]);
        pts = o;
      }
      return pts;
    }
    // 沿折线生成虚线（三角形，three 坐标），离城池 2.4 以内不画。
    // 按弧长逐段（周期 DASH + GAP）取点：循环次数只取决于航线长度，不会因浮点累加卡死
    _dashes(pts, P) {
      const DASH = 1.0, GAP = 0.7, W = 0.16, PERIOD = DASH + GAP;
      const n = pts.length >> 1;
      if (n < 2) return;
      const cum = new Float64Array(n);
      for (let k = 1; k < n; k++) cum[k] = cum[k - 1] + Math.hypot(pts[2 * k] - pts[2 * k - 2], pts[2 * k + 1] - pts[2 * k - 1]);
      const total = cum[n - 1];
      if (!(total > 0) || !isFinite(total)) return;
      const ax = pts[0], az = pts[1], bx = pts[2 * n - 2], bz = pts[2 * n - 1];
      let seg = 0;
      const at = s => {
        while (seg < n - 2 && cum[seg + 1] < s) seg++;
        const L = cum[seg + 1] - cum[seg], t = L > 1e-9 ? Math.min(1, Math.max(0, (s - cum[seg]) / L)) : 0;
        return [pts[2 * seg] + (pts[2 * seg + 2] - pts[2 * seg]) * t, pts[2 * seg + 1] + (pts[2 * seg + 3] - pts[2 * seg + 1]) * t];
      };
      const quad = (sx, sz, ex, ez) => {
        const L = Math.hypot(ex - sx, ez - sz);
        if (L < 1e-6) return;
        const mx = (sx + ex) / 2, mz = (sz + ez) / 2;
        if (Math.hypot(mx - ax, mz - az) <= 2.4 || Math.hypot(mx - bx, mz - bz) <= 2.4) return;
        const px = -(ez - sz) / L * W, pz = (ex - sx) / L * W;
        const ys = Math.max(0.13, this.surfaceY(sx, sz) + 0.12), ye = Math.max(0.13, this.surfaceY(ex, ez) + 0.12);
        P.push(sx + px, ys, -(sz + pz), ex + px, ye, -(ez + pz), ex - px, ye, -(ez - pz));
        P.push(sx + px, ys, -(sz + pz), ex - px, ye, -(ez - pz), sx - px, ys, -(sz - pz));
      };
      const nd = Math.ceil(total / PERIOD);
      for (let d = 0; d < nd; d++) {
        const s0 = d * PERIOD, s1 = Math.min(total, s0 + DASH);
        if (s1 <= s0) break;
        // 起点、途经的折线顶点、终点
        let p = at(s0);
        const seg0 = seg;
        for (let k = seg0 + 1; k < n - 1 && cum[k] < s1; k++) { quad(p[0], p[1], pts[2 * k], pts[2 * k + 1]); p = [pts[2 * k], pts[2 * k + 1]]; }
        const e = at(s1);
        quad(p[0], p[1], e[0], e[1]);
      }
    }

    *_genCities(state) {
      const Gfx = SG.Gfx;
      const rot = new THREE.Matrix4();
      const n = state.cities.length;
      const tileSize = this.spec.tile, T = this.spec.terrain;
      const bodies = new Map();   // 区块 → MeshBuilder（城体按区块合并，视锥剔除生效）
      // 旗帜：所有城共用一个实例化网格（颜色 = 势力色，摆动在 update 中）
      const flags = new THREE.InstancedMesh(MapView.flagGeometry(), Gfx.newLowPoly(), Math.max(1, n));
      flags.name = 'Flags';
      flags.castShadow = true; flags.receiveShadow = true;
      flags.frustumCulled = false;
      flags.userData.sharedGeometry = true;
      flags.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      for (let i = 0; i < flags.count; i++) { flags.setMatrixAt(i, ZERO_MATRIX); flags.setColorAt(i, new THREE.Color(1, 1, 1)); }
      this._flags = flags;
      this._flagInfo = [];
      // 势力光圈：贴合地表的圆盘，合并为一个网格（RGBA 顶点色）
      const ringPos = [], ringUv = [], ringIdx = [];
      this._ringRanges = new Map();
      const RING_N = 10, RING_R = 2.9;

      let k = 0;
      for (const c of state.cities) {
        const mx = this._cityX[c.id], mz = this._cityZ[c.id];
        const y = Math.max(0.3, this.height(mx, mz));
        const size = M.lerp(0.9, 1.5, M.inverseLerp(180, 650, c.town));
        const capital = c.key === 'luoyang' || c.key === 'changan';
        const yaw = (c.id * 37) % 20 - 10;
        // 城体：烘焙到所在区块的合并网格中（Unity 坐标下的平移 × 绕 y 旋转）
        const tk = Math.floor((mx - T.x0) / tileSize) + ',' + Math.floor((mz - T.z0) / tileSize);
        let mb = bodies.get(tk);
        if (!mb) { mb = new SG.MeshBuilder(); bodies.set(tk, mb); }
        mb.M = new THREE.Matrix4().makeTranslation(mx, y, mz).multiply(rot.makeRotationY(yaw * M.deg2rad));
        SG.Models.cityInto(mb, size, null, capital);
        mb.M = null;

        const group = new THREE.Group();
        group.name = 'City_' + c.name;
        group.position.copy(SG.U(mx, y, mz));
        group.rotation.y = SG.yawToThree(yaw);
        group.userData.cityId = c.id;
        const anchor = new THREE.Object3D();
        anchor.name = 'Label';
        anchor.position.set(0, size * 1.6 + 0.6, 0);
        group.add(anchor);
        this.root.add(group);
        group.updateMatrixWorld(true);

        this._flagInfo[c.id] = {
          local: new THREE.Matrix4().makeTranslation(size * 0.9, 0, -size * 0.9),
          phase: SG.Random.value() * 10,
          visible: false,
        };

        // 光圈
        const start = ringPos.length / 3, idx0 = ringIdx.length;
        for (let i = 0; i <= RING_N; i++)
          for (let j = 0; j <= RING_N; j++) {
            const ux = mx - RING_R + 2 * RING_R * i / RING_N, uz = mz - RING_R + 2 * RING_R * j / RING_N;
            // 贴地，但不爬上相邻山坡（高出城基的部分留在地形下面，与 Unity 的平面圆盘一致）
            ringPos.push(ux, Math.min(Math.max(this.surfaceY(ux, uz), 0.02), y + 0.45) + 0.12, -uz);
            ringUv.push(i / RING_N, j / RING_N);
          }
        for (let i = 0; i < RING_N; i++)
          for (let j = 0; j < RING_N; j++) {
            const a = start + i * (RING_N + 1) + j, b = a + 1, cc = a + RING_N + 2, d = a + RING_N + 1;
            ringIdx.push(a, cc, b, a, d, cc);
          }
        this._ringRanges.set(c.id, { start, count: (RING_N + 1) * (RING_N + 1), idx0 });

        this.cities.set(c.id, {
          city: c, group, anchor, size,
          pos: SG.U(mx, y, mz),
          labelPos: SG.U(mx, y + size * 1.6 + 0.6, mz),
          mapX: mx, mapY: mz,
        });
        if ((++k & 1) === 0) { this._sub(0.8 * k / n); yield; }
      }
      for (const mb of bodies.values()) {
        const body = Gfx.mesh(mb.toGeometry(), Gfx.lowPoly());
        body.name = 'Cities';
        this.root.add(body);
        yield;
      }
      this.root.add(flags);

      const rg = new THREE.BufferGeometry();
      rg.setAttribute('position', new THREE.Float32BufferAttribute(ringPos, 3));
      rg.setAttribute('uv', new THREE.Float32BufferAttribute(ringUv, 2));
      const rc = new THREE.Float32BufferAttribute(new Float32Array(ringPos.length / 3 * 4), 4);
      rc.setUsage(THREE.DynamicDrawUsage);
      rg.setAttribute('color', rc);
      rg.setIndex(ringIdx);
      rg.computeBoundingSphere();
      const ringMat = new THREE.MeshBasicMaterial({
        map: Gfx.ringTexture, vertexColors: true, transparent: true, depthWrite: false,
        side: THREE.DoubleSide, fog: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -4,
      });
      const rings = new THREE.Mesh(rg, ringMat);
      rings.name = 'Territory';
      rings.renderOrder = 1;
      this.root.add(rings);
      this._rings = rings;
      yield;
    }

    static flagGeometry() {
      if (MapView._flagGeo) return MapView._flagGeo;
      const mb = new SG.MeshBuilder();
      mb.box(V(0, 1.1, 0), V(0.08, 2.2, 0.08), new THREE.Color(0.35, 0.25, 0.15));
      const a = V(0.04, 2.15, 0), b = V(0.95, 2.05, 0), c = V(0.95, 1.45, 0), d = V(0.04, 1.5, 0);
      const w = new THREE.Color(1, 1, 1);
      mb.quad(a, b, c, d, w); mb.quad(a, d, c, b, w);
      mb.cone(V(0, 2.2, 0), 0.08, 0.16, 5, new THREE.Color(0.9, 0.75, 0.3));
      MapView._flagGeo = mb.toGeometry();
      return MapView._flagGeo;
    }
    // FlatDisc(r)：带 UV 的方形面片（Unity 坐标，y = 0）
    static flatDisc(r) {
      const mb = new SG.MeshBuilder();
      const w = new THREE.Color(1, 1, 1);
      mb.flatQuad(V(-r, 0, -r), V(-r, 0, r), V(r, 0, r), V(r, 0, -r), w);
      return mb.toGeometry();
    }
    // 贴合地表的方形面片（地图坐标中心 cx, cz；半径 r；n×n 格；抬高 lift；地表高度上限 maxY）
    drapedDisc(cx, cz, r, n, lift, maxY) {
      if (maxY === undefined) maxY = Infinity;
      const pos = [], uv = [], idx = [];
      for (let i = 0; i <= n; i++)
        for (let j = 0; j <= n; j++) {
          const ux = cx - r + 2 * r * i / n, uz = cz - r + 2 * r * j / n;
          pos.push(ux, Math.min(Math.max(this.surfaceY(ux, uz), 0.02), maxY) + lift, -uz);
          uv.push(i / n, j / n);
        }
      for (let i = 0; i < n; i++)
        for (let j = 0; j < n; j++) {
          const a = i * (n + 1) + j, b = a + 1, c = a + n + 2, d = a + n + 1;
          idx.push(a, c, b, a, d, c);
        }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      g.setIndex(idx);
      g.computeBoundingSphere();
      return g;
    }

    _buildClouds() {
      const rnd = SG.SeededRandom(5);
      // 只写深度的前置遍：半透明的云只显示最外层的面，不会透出内部互相穿插的云团
      const depthMat = new THREE.MeshBasicMaterial({ colorWrite: false, transparent: true, depthWrite: true });
      depthMat.name = 'CloudDepth';
      const world = this.region === 'world';
      // 世界地图：云在镜头周围 ±CLOUD_BOX 的范围内循环（数量与地图大小无关）
      this._cloudBox = world ? { x: 170, z: 115 } : null;
      const n = this.spec.clouds;
      for (let i = 0; i < n; i++) {
        const mb = new SG.MeshBuilder();
        const puffs = 4 + rnd.next(4);
        for (let k = 0; k < puffs; k++) {
          const ox = (rnd.nextDouble() - 0.5) * 7, oy = rnd.nextDouble() * 0.8, oz = (rnd.nextDouble() - 0.5) * 3;
          const r = 1.4 + rnd.nextDouble() * 1.6;
          mb.blob(V(ox, oy, oz), V(r, r * 0.6, r * 0.85), new THREE.Color(1, 1, 1), k);
        }
        // 白云（C# _Emission 0.35）：向阳面的亮度超过 1，同样走高光肩部，云顶不会截断成一整片纯白。
        // 每朵云一个材质与一个阴影深度材质：镜头拉近（战略视角）时离镜头近的云连同云影一起淡出隐去，
        // 不再整片挡住城池（见 update 与 CLOUD_FADE_NEAR / CLOUD_FADE_FAR）
        const mat = shoulderMaterial('CloudLowPoly');
        mat.emission = 0.35;
        mat.color.setRGB(0.74, 0.75, 0.78); // _Color：略压暗，让云的明暗面不被截断成一片白
        mat.transparent = true;
        const geo = mb.toGeometry();
        const m = SG.Gfx.mesh(geo, mat, { castShadow: true, receiveShadow: false });
        m.name = 'Cloud';
        m.customDepthMaterial = cloudShadowMaterial();
        m.renderOrder = 21;
        const pre = new THREE.Mesh(geo, depthMat);
        pre.name = 'CloudDepth';
        pre.renderOrder = 20;
        pre.castShadow = false; pre.receiveShadow = false;
        pre.userData.sharedGeometry = true;
        m.add(pre);
        let px, py, pz;
        if (world) {
          const b = this._cloudBox, c = this._viewCenter();
          px = c.x + (rnd.nextDouble() * 2 - 1) * b.x; pz = c.z + (rnd.nextDouble() * 2 - 1) * b.z;
          py = 15 + rnd.nextDouble() * 5;
        } else { px = rnd.nextDouble() * MapW; py = 15 + rnd.nextDouble() * 5; pz = rnd.nextDouble() * MapH; } // 与旧版相同的取数顺序
        m.position.copy(SG.U(px, py, pz));
        this.root.add(m);
        this._clouds.push(m);
      }
    }
    // 镜头视线与地面（y = 0）的交点，地图坐标 { x, z }
    _viewCenter() {
      const cam = SG.Gfx && SG.Gfx.camera;
      const b = this.bounds;
      const fallback = { x: (b.xMin + b.xMax) / 2, z: (b.yMin + b.yMax) / 2 };
      if (!cam) return fallback;
      cam.getWorldDirection(_fwd);
      _ray.set(cam.position, _fwd);
      if (!_ray.intersectPlane(_plane0, _hit)) return fallback;
      return { x: _hit.x, z: -_hit.z };
    }

    _buildSelectRing() {
      const tex = SG.Gfx.ringTexture.clone();
      tex.needsUpdate = true;
      tex.center.set(0.5, 0.5);
      const mat = SG.Gfx.unlit([1, 0.85, 0.35, 0.95], tex);
      mat.polygonOffsetFactor = -3; mat.polygonOffsetUnits = -6;
      const m = new THREE.Mesh(new THREE.BufferGeometry(), mat);
      m.name = 'SelectRing';
      m.renderOrder = 2;
      m.visible = false;
      m.castShadow = false; m.receiveShadow = false;
      this.root.add(m);
      this._selectRing = m;
      this._selectAngle = 0;
    }

    // ------------------------------------------------------ 更新 --
    refresh(state) {
      if (!this._rings) return;
      const colAttr = this._rings.geometry.getAttribute('color');
      const arr = colAttr.array;
      for (const cv of this.cities.values()) {
        const c = state.cities[cv.city.id] || cv.city;
        cv.city = c;
        const owned = c.owner >= 0 && state.factions[c.owner];
        const col = owned ? SG.Gfx.color(state.factions[c.owner].color) : new THREE.Color(0.75, 0.75, 0.72);
        // 旗帜
        const fi = this._flagInfo[c.id];
        fi.visible = !!owned;
        this._flags.setColorAt(c.id, col);
        // 光圈
        const a = owned ? (c.owner === state.player ? 0.85 : 0.6) : 0.25;
        const rr = this._ringRanges.get(c.id);
        for (let k = 0; k < rr.count; k++) {
          const o = (rr.start + k) * 4;
          arr[o] = col.r; arr[o + 1] = col.g; arr[o + 2] = col.b; arr[o + 3] = a;
        }
      }
      colAttr.needsUpdate = true;
      if (this._flags.instanceColor) this._flags.instanceColor.needsUpdate = true;
      this._updateFlags();
    }

    select(cityId) {
      this._selected = cityId;
      const ring = this._selectRing;
      if (!ring) return;
      const cv = cityId >= 0 ? this.cities.get(cityId) : null;
      if (!cv) { ring.visible = false; return; }
      ring.geometry.dispose();
      ring.geometry = this.drapedDisc(cv.mapX, cv.mapY, 3.4, 14, 0.18, cv.pos.y + 0.45);
      ring.visible = true;
    }

    // 视口 CSS 像素 → 城池 id（无则 -1）
    pick(sx, sy) {
      if (!this.root || this.cities.size === 0) return -1;
      const ray = SG.Gfx.screenRay(sx, sy);
      let best = -1, bestD = Infinity;
      for (const cv of this.cities.values()) {
        _sphere.center.set(cv.pos.x, cv.pos.y + 1, cv.pos.z);
        _sphere.radius = 2.6;
        if (ray.intersectSphere(_sphere, _hit)) {
          const d = _hit.distanceTo(ray.origin);
          if (d < bestD && d < 1000) { bestD = d; best = cv.city.id; }
        }
      }
      if (best >= 0) return best;
      // 无碰撞时选最近的城
      if (ray.intersectPlane(_plane05, _hit)) {
        let bc = null, bd = Infinity;
        for (const cv of this.cities.values()) {
          const d = cv.pos.distanceTo(_hit);
          if (d < bd) { bd = d; bc = cv; }
        }
        if (bc && bd < 4) return bc.city.id;
      }
      return -1;
    }

    update(dt) {
      if (!this.root) return;
      if (!(dt >= 0)) dt = 0;
      this.time += dt;
      const ring = this._selectRing;
      if (ring && ring.visible) {
        const s = 1 + Math.sin(this.time * 3) * 0.06;
        this._selectAngle += dt * 25;
        const map = ring.material.map;
        map.repeat.set(1 / s, 1 / s);
        map.rotation = -this._selectAngle * M.deg2rad;
      }
      const cam = SG.Gfx && SG.Gfx.camera;
      const box = this._cloudBox;
      const vc = box && this._clouds.length ? this._viewCenter() : null;
      for (const c of this._clouds) {
        c.position.x += 0.6 * dt;
        c.position.z -= 0.15 * dt;
        if (box) {
          // 世界地图：在镜头周围的方框内循环
          const dx = c.position.x - vc.x, dz = -c.position.z - vc.z;
          if (dx > box.x) c.position.x -= 2 * box.x; else if (dx < -box.x) c.position.x += 2 * box.x;
          if (dz > box.z) c.position.z += 2 * box.z; else if (dz < -box.z) c.position.z -= 2 * box.z;
        } else {
          if (c.position.x > MapW + 30) c.position.x = -30;
          if (-c.position.z > MapH + 30) c.position.z = 30;
        }
        // 离镜头越近越透明：标题 / 全图视角几乎不透明，战略视角里离镜头近的云完全隐去（不留半透明的灰色残影）。
        // 云影浓度取不透明度的平方：云体半透明时影子已很淡，隐去时影子同时消失，不在地面上留下无来由的暗斑
        if (cam) {
          c.getWorldPosition(_cloudPos);
          const f = M.clamp01((_cloudPos.distanceTo(cam.position) - CLOUD_FADE_NEAR) / (CLOUD_FADE_FAR - CLOUD_FADE_NEAR));
          const a = f * f * (3 - 2 * f);
          const shown = a > 0.01;
          c.material.opacity = a;
          c.visible = shown;
          c.castShadow = a * a > 1 / 18; // 浓度不足半格时 3×3 抖动图案已全部丢弃，不必再画
          c.customDepthMaterial.userData.shadowDensity.value = a * a;
        }
      }
      // 镜头很高时（只有世界地图能拉到那么远）不画树木：树在那个距离只有一两个像素，却占三角形总数的大半
      if (this._treeMeshes && cam) {
        const hide = cam.position.y > (this._treesHidden ? 165 : 180);
        if (hide !== this._treesHidden) { this._treesHidden = hide; for (const m of this._treeMeshes) m.visible = !hide; }
      }
      // 世界地图：镜头拉远时雾随之推远（地图隐藏时——如战斗——恢复原值）
      if (this.region === 'world' && cam && SG.Gfx.scene && SG.Gfx.scene.fog) {
        const fog = SG.Gfx.scene.fog;
        if (!this._fogBase) this._fogBase = { near: fog.near, far: fog.far };
        const k = this.root.visible ? Math.max(1, cam.position.y / 105) : 1;
        fog.near = this._fogBase.near * k; fog.far = this._fogBase.far * k;
      }
      this._updateFlags();
    }
    _restoreFog() {
      if (this._fogBase && SG.Gfx && SG.Gfx.scene && SG.Gfx.scene.fog) {
        SG.Gfx.scene.fog.near = this._fogBase.near; SG.Gfx.scene.fog.far = this._fogBase.far;
      }
      this._fogBase = null;
    }

    _updateFlags() {
      const flags = this._flags;
      if (!flags) return;
      for (const cv of this.cities.values()) {
        const fi = this._flagInfo[cv.city.id];
        if (!fi.visible) { flags.setMatrixAt(cv.city.id, ZERO_MATRIX); continue; }
        const wave = Math.sin(this.time * 1.7 + fi.phase) * 14;
        _m4b.makeRotationY(SG.yawToThree(wave));
        _m4.multiplyMatrices(cv.group.matrixWorld, fi.local).multiply(_m4b);
        flags.setMatrixAt(cv.city.id, _m4);
      }
      flags.instanceMatrix.needsUpdate = true;
    }

    // 行军动画：一支小部队沿道路（海路则沿航线）移动。from / to 为城池对象或 id
    async march(from, to, colorHex, seconds) {
      if (seconds === undefined) seconds = 1.6;
      const a = this.cities.get(typeof from === 'number' ? from : from.id);
      const b = this.cities.get(typeof to === 'number' ? to : to.id);
      if (!a || !b || !this.root) return;
      const team = SG.Gfx.color(colorHex);
      const mb = new SG.MeshBuilder();
      SG.Models.commander(mb, V(0, 0, 0), team, 2.4);
      for (let i = 0; i < 6; i++) SG.Models.soldier(mb, V((i % 3 - 1) * 0.55, 0, -0.9 - Math.floor(i / 3) * 0.55), team, true, 2.2);
      const army = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly());
      army.name = 'Army';
      // 航线（若有）：按弧长取点
      const ia = a.city.id, ib = b.city.id;
      let lane = this._lanes.get(Math.min(ia, ib) + '-' + Math.max(ia, ib)) || null;
      let cum = null;
      if (lane) {
        if (ia > ib) { const r = []; for (let k = lane.length - 2; k >= 0; k -= 2) r.push(lane[k], lane[k + 1]); lane = r; }
        cum = [0];
        for (let k = 2; k < lane.length; k += 2) cum.push(cum[cum.length - 1] + Math.hypot(lane[k] - lane[k - 2], lane[k + 1] - lane[k - 1]));
      }
      const at = k => {
        if (!lane) return [a.mapX + (b.mapX - a.mapX) * k, a.mapY + (b.mapY - a.mapY) * k, Math.atan2(b.mapX - a.mapX, b.mapY - a.mapY)];
        const L = cum[cum.length - 1] * k;
        let s = 1;
        while (s < cum.length - 1 && cum[s] < L) s++;
        const t = cum[s] > cum[s - 1] ? (L - cum[s - 1]) / (cum[s] - cum[s - 1]) : 0;
        const x0 = lane[2 * s - 2], z0 = lane[2 * s - 1], x1 = lane[2 * s], z1 = lane[2 * s + 1];
        return [x0 + (x1 - x0) * t, z0 + (z1 - z0) * t, Math.atan2(x1 - x0, z1 - z0)];
      };
      army.rotation.y = SG.yawToThree(Math.atan2(b.mapX - a.mapX, b.mapY - a.mapY) * M.rad2deg);
      const root = this.root;
      root.add(army);
      const token = { cancelled: false };
      this._marchers.add(token);
      const place = t => {
        const k = M.smoothStep(0, 1, t);
        const p = at(k);
        const y = Math.max(0.2, this.surfaceY(p[0], p[1])) + Math.abs(Math.sin(t * 30)) * 0.08;
        army.position.set(p[0], y, -p[1]);
        if (lane) army.rotation.y = SG.yawToThree(p[2] * M.rad2deg);
      };
      let t = 0, last = performance.now();
      place(0);
      while (t < 1 && !token.cancelled) {
        place(t);
        await SG.frame();
        const n = performance.now();
        t += Math.min(0.25, (n - last) / 1000) / seconds;
        last = n;
      }
      this._marchers.delete(token);
      root.remove(army);
      army.geometry.dispose();
    }

    // ------------------------------------------------------ 调试 --
    // 在地图上叠加栅格：'land'（陆 / 海）| 'coast'（海岸距离）| 'river'（河流距离）| 'plateau' | 'biome'；null 关闭
    debugOverlay(kind) {
      if (this._debug) { this.root.remove(this._debug); this._debug.geometry.dispose(); this._debug.material.map.dispose(); this._debug.material.dispose(); this._debug = null; }
      if (!kind || !this.root || !this._geo) return null;
      const T = this.spec.terrain, res = this.region === 'world' ? 1.0 : 0.5;
      const nx = Math.ceil((T.x1 - T.x0) / res), nz = Math.ceil((T.z1 - T.z0) / res);
      const cv = document.createElement('canvas');
      cv.width = nx; cv.height = nz;
      const ctx = cv.getContext('2d'), img = ctx.createImageData(nx, nz), D = img.data, G = this._geo;
      for (let j = 0; j < nz; j++)
        for (let i = 0; i < nx; i++) {
          const x = T.x0 + (i + 0.5) * res, z = T.z1 - (j + 0.5) * res;
          let r = 0, g = 0, b = 0, a = 150;
          if (kind === 'land') { if (G.inSea(x, z)) { b = 255; g = 80; } else { g = 200; r = 60; } }
          else if (kind === 'coast') { const d = G.coastDist(x, z), s = G.inSea(x, z); const v = Math.min(1, d / 12); r = s ? 0 : 255 * v; g = 255 * (1 - v); b = s ? 255 * v : 0; if (d < 0.4) { r = g = b = 255; } }
          else if (kind === 'river') { const d = G.riverDist(x, z) / G.rW; const v = Math.min(1, d / 6); r = 255 * v; g = 255 * v; b = 255; a = d < 2.2 ? 220 : 90; }
          else if (kind === 'plateau') { const f = G.sample(x, z); r = Math.min(255, f[F_PLAT] * 40); g = f[F_OLD] * 255; b = Math.min(255, G.ridges.sum(x, z) * 50); }
          else if (kind === 'biome') {
            const f = G.sample(x, z);
            r = 255 * Math.min(1, f[F_DESERT] + f[F_STEPPE] * 0.7 + f[F_SAV] * 0.5 + f[F_MED] * 0.4);
            g = 255 * Math.min(1, f[F_TROP] + f[F_STEPPE] * 0.6 + f[F_MED] * 0.5 + f[F_BOREAL] * 0.5);
            b = 255 * Math.min(1, f[F_BOREAL] + f[F_EAST] * 0.5);
          }
          const o = (j * nx + i) * 4;
          D[o] = r; D[o + 1] = g; D[o + 2] = b; D[o + 3] = a;
        }
      ctx.putImageData(img, 0, 0);
      const tex = new THREE.CanvasTexture(cv);
      tex.magFilter = THREE.NearestFilter; tex.minFilter = THREE.LinearFilter; tex.generateMipmaps = false;
      const w = nx * res, hgt = nz * res;
      const geo = new THREE.PlaneGeometry(w, hgt);
      geo.rotateX(-Math.PI / 2);
      const mat = new THREE.MeshBasicMaterial({ map: tex, transparent: true, depthWrite: false, depthTest: false, fog: false });
      const m = new THREE.Mesh(geo, mat);
      m.position.set(T.x0 + w / 2, 9, -(T.z0 + hgt / 2));
      m.renderOrder = 50;
      m.name = 'DebugOverlay';
      this.root.add(m);
      this._debug = m;
      return m;
    }
  }
  MapView.MapW = MapW;
  MapView.MapH = MapH;
  MapView._flagGeo = null;
  MapView.FlatDisc = MapView.flatDisc;
  // 测试 / 调试用
  MapView._internals = { EdgeIndex, FeatureGrid, Geo, classicSpec, worldSpec, geoCache: _geoCache };

  SG.MapView = MapView;
})();
