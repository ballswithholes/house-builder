'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 战略地图
   ← View/MapView.cs：程序化生成的地形（海岸线、河流、湖泊、山脉、地形、水面、树木、道路、城池与旗帜、
     势力光圈、云、选择光圈、点选、行军动画）
   第二版（§4G）：地理数据与投影移到 js/world-geo.js，地形可扩展到整个欧亚大陆。
   第三版（DESIGN-V3 §1）：平滑、细致的地形——
     · 高度：1.25 间距的高度栅格 → Catmull-Rom 二倍上采样为 0.625 的主网格 + 细节噪声（平原轻微、丘陵起伏、山地脊状），
       地形范围外为 5.0 间距的远景栅格（双三次插值），两者在边缘 12 单位内过渡。
     · 网格：四叉树分块 LOD（每块 32×32 格，按镜头距离细分，按需在帧间补建，块边有裙边，不露缝）。
     · 着色：数据纹理（法线 + 凹凸遮蔽 + 高度、群落底色 + 林地、沙漠 / 荒芜 / 积雪 / 农田权重）+ SG.Gfx.terrainMaterial 的
       地形着色器（草地、干草、泥土、农田、沙滩、三平面投影的岩石、雪、海床；js/art.js），着色与块的细分级别无关。
     · 水面：按像素的水深（浅处透明青绿、深处蓝）、岸边浪花、涟漪与太阳高光、菲涅尔天空反射。
     · 更多元素：十四种平滑树（各群落数种；近处为模型、远处为公告板）、灌丛、石块、镜头附近的草丛与野花、
       城外村落与农田、河上桥梁（石拱桥 / 木桥）、著名关隘（PASSES）、雪峰、海岸沙滩、平滑的云。
     · 城池：各文化的平滑细致模型（js/culture-art.js，汉地也在其中），远处换成简化模型。
   第三版修订（评审）：
     · 去锯齿：海岸 2.4 单位内压低山脉 / 高原；河谷宽度随两岸高度增加（上缘平缓的岸坡）；高度栅格按坡度加权平滑两遍；
       细节噪声限带（波长 ≥ 2.7）、脊状噪声圆脊；主网格再按曲率去尖两遍（水面与紧邻水面的岸点不动）。
     · 城池：占地压平到城基高度，镜头一侧（−z）的视线楔形内限高（城池不再被前方山脊挡住）；河流流到城墙下不被填平。
     · 关隘：从道路折线上挑选两侧地势最高的鞍部（离城 3.6 以上，史实位置 8 单位内），无路时建在史实位置；
       城墙为贴地的斜切墙段，下坡、遇水、断崖即止。passesSkipped = 未能建成的关隘名。
     · 树木：镜头 18 单位内用特写模型（二级细分树冠 + 叶簇），树冠 / 石块的材质带程序化叶簇 / 石纹明暗与法线起伏。
     · 城池近景、关隘、桥用 SG.Gfx.masonryMaterial()（砖缝、瓦垄、方砖）。
   所有地理计算沿用 Unity 坐标（x 向东，z = 地图 y 向北）；three 坐标 = (x, y, −z)；物体位置用 SG.U(x, y, z)。

   公开接口
     build(state, opts)          同步构建。opts.region = 'auto' | 'china' | 'world'
                                 （auto：所有城池都在旧版中国地图内时用 'china'，否则 'world'）
     buildAsync(state, opts)     → Promise<root>：同样的构建，切成约 opts.sliceMs（默认 8ms）的分片，
                                 opts.onProgress(fraction 0..1, label) 报告进度；opts.traceSteps 记录最长的单步
     refresh(state) select(cityId) pick(screenX, screenY) → cityId | -1
     height(mapX, mapY)          高度函数（未加细节噪声）
     surfaceY(mapX, mapY)        实际渲染的地表高度（L0 地形块的三角形插值）
     terrainInfo(mapX, mapY)     → { h, slope, water: 'sea'|'river'|'lake'|null, depth, forest, farm, sand, snow, rock, biome, river, coast, road }
                                 （第二阶段：战场地形取自城池周边）
     roadDist(mapX, mapY)        到最近陆路的距离
     isForest(x, z, h) update(dt)
     march(fromCity, toCity, colorHex, seconds = 1.6) → Promise（海路沿航线行进）
     root  cities: Map<cityId, { city, group, labelPos, pos, mapX, mapY, size }>
     passes: [{ name, x, z, y }]  已建成的关隘（地图坐标，y = 标签高度）；passesSkipped: [name]
     region ('china' | 'world')   bounds { xMin, yMin, xMax, yMax }（可玩范围，地图坐标；CameraRig.useMap 用它）
     treeCount rockCount houseCount
     buildStats { totalMs, maxSliceMs, maxSliceStage, maxSliceByStage, slices, stages: {阶段: ms}, sync }
     debugOverlay(kind | null)   调试：在地图上叠加 'land' | 'coast' | 'river' | 'plateau' | 'biome' 栅格
   地理数据：SG.WorldGeo.projected()（js/world-geo.js）；页面没有载入 world-geo.js 时退回内置的旧版中国数据（只能构建 'china'）。
   地理场全部栅格化：陆地 = 按行分桶的奇偶规则；海岸 / 河流距离 = 最近要素栅格（种子 + 双向扫描传播，
     查询只算周围四个节点记下的要素）；山脉 = 桶索引；高原、生物群落 = 扫描线填充 + 盒式模糊。
   海路：若 SG.linkIsSea(aId, bId) 存在且为真，两城之间画海上虚线航线（海域栅格上的 A*，绕开陆地），不画道路。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  // 各文化的城池模型在 js/culture-art.js（SG.CultureArt）。页面没有单独载入它时，在解析期于本文件之后同步补载
  // （与 <script> 标签等效）；载不到时所有城池用原模型。
  try {
    const me = typeof document !== 'undefined' && document.currentScript;
    if (!SG.CultureArt && me && me.src && document.readyState === 'loading') {
      document.write('<script src="' + me.src.replace(/map-view\.js([?#].*)?$/, 'culture-art.js') + '"><\/script>');
    }
  } catch (e) { /* 非浏览器环境 */ }

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
  // GLSL 式 smoothstep（a > b 时反向）
  function ss(a, b, x) { let t = (x - a) / (b - a); t = t < 0 ? 0 : t > 1 ? 1 : t; return t * t * (3 - 2 * t); }
  const b8 = v => (v <= 0 ? 0 : v >= 1 ? 255 : Math.round(v * 255));
  // Catmull-Rom 权重
  const _crx = [0, 0, 0, 0], _crz = [0, 0, 0, 0];
  function crw(t, w) {
    const t2 = t * t, t3 = t2 * t;
    w[0] = (-t3 + 2 * t2 - t) / 2; w[1] = (3 * t3 - 5 * t2 + 2) / 2; w[2] = (-3 * t3 + 4 * t2 + t) / 2; w[3] = (t3 - t2) / 2;
    return w;
  }
  // 点集的桶索引：at(x, z) → 半径 radius 内可能命中的点下标列表（或 null）
  function makePointIndex(xs, zs, radius) {
    const cell = 4;
    if (!xs || !xs.length) return null;
    let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
    for (let i = 0; i < xs.length; i++) { x0 = Math.min(x0, xs[i]); x1 = Math.max(x1, xs[i]); z0 = Math.min(z0, zs[i]); z1 = Math.max(z1, zs[i]); }
    x0 -= radius + 1; z0 -= radius + 1; x1 += radius + 1; z1 += radius + 1;
    const nx = Math.ceil((x1 - x0) / cell) + 1, nz = Math.ceil((z1 - z0) / cell) + 1;
    const b = new Array(nx * nz).fill(null);
    for (let i = 0; i < xs.length; i++) {
      const i0 = Math.floor((xs[i] - radius - x0) / cell), i1 = Math.floor((xs[i] + radius - x0) / cell);
      const j0 = Math.floor((zs[i] - radius - z0) / cell), j1 = Math.floor((zs[i] + radius - z0) / cell);
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
      water: { x0: x0 - 60, z0: z0 - 60, x1: x1 + 60, z1: z1 + 60, step: 2.5, tile: 40, band: 20, far: 300 },
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

  // 第三版：按坡度加权的平滑（一遍）。下标 o = a·sa + b·sb；水面点（< 0.06）不动，陆地点不被压到水面以下。
  // dh（与四邻的最大高差）在 lo 以下不动，hi 以上按 amt 向 3×3 邻点的加权均值靠拢
  function* slopeSmooth(h, na, nb, sa, sb, lo, hi, amt) {
    const src = h.slice();
    for (let a = 1; a < na - 1; a++) {
      for (let b = 1; b < nb - 1; b++) {
        const o = a * sa + b * sb, c = src[o];
        if (c < 0.06) continue;
        const n1 = src[o - sa], n2 = src[o + sa], n3 = src[o - sb], n4 = src[o + sb];
        const dh = Math.max(Math.abs(n1 - c), Math.abs(n2 - c), Math.abs(n3 - c), Math.abs(n4 - c));
        if (dh <= lo) continue;
        const d1 = src[o - sa - sb], d2 = src[o - sa + sb], d3 = src[o + sa - sb], d4 = src[o + sa + sb];
        // 紧邻水面的岸点不动（否则岸边被拉低，河岸与海岸变成一圈宽沙滩）
        if (Math.min(n1, n2, n3, n4, d1, d2, d3, d4) < 0.06) continue;
        const avg = (2 * (n1 + n2 + n3 + n4) + d1 + d2 + d3 + d4) / 12;
        const v = c + (avg - c) * ss(lo, hi, dh) * amt;
        h[o] = v < 0.06 ? 0.06 : v;
      }
      if ((a & 31) === 31) yield;
    }
  }
  // 去尖（一遍）：与 8 邻加权均值的偏差（≈ 曲率）在 lo 以下不动，hi 以上按 amt 靠拢；水面点不动
  function* curveSmooth(h, na, nb, sa, sb, lo, hi, amt) {
    const src = h.slice();
    for (let a = 1; a < na - 1; a++) {
      for (let b = 1; b < nb - 1; b++) {
        const o = a * sa + b * sb, c = src[o];
        if (c < 0.06) continue;
        const n1 = src[o - sa], n2 = src[o + sa], n3 = src[o - sb], n4 = src[o + sb], d1 = src[o - sa - sb], d2 = src[o - sa + sb], d3 = src[o + sa - sb], d4 = src[o + sa + sb];
        if (Math.min(n1, n2, n3, n4, d1, d2, d3, d4) < 0.06) continue;
        const avg = (2 * (n1 + n2 + n3 + n4) + d1 + d2 + d3 + d4) / 12;
        const dev = Math.abs(avg - c);
        if (dev <= lo) continue;
        const v = c + (avg - c) * ss(lo, hi, dev) * amt;
        h[o] = v < 0.06 ? 0.06 : v;
      }
      if ((a & 31) === 31) yield;
    }
  }
  // 多项式平滑最小值
  function smin(a, b, k) { const t = Math.max(k - Math.abs(a - b), 0) / k; return Math.min(a, b) - t * t * k * 0.25; }

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
      if (!_landIndex || _landIndex.src !== P) { yield; _landIndex = new EdgeIndex(P.land, 2); _landIndex.src = P; }
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

  // ---------------------------------------------------------- 地表群落 --
  // 第三版：地表不再逐面取色，而是生成「群落底色 + 材质权重」数据纹理，由地形着色器（SG.Gfx.terrainMaterial）
  // 按坡度 / 高度在像素级混合草地、泥土、农田、沙、岩石、雪（见 js/art.js）。底色沿用第二版的群落配色。
  // 第四版：Lanternvale 的绘制地面配色（暖鼠尾草绿的草地、干草黄、沙、苔绿林下……，与试点的地面同一色系）。
  // 只影响底色（群落数据纹理的 RGB、草丛 / 灌丛的颜色），groundInfo 的林地 / 沙漠 / 荒芜 / 积雪 / 群落权重不变（terrainInfo 不受影响）
  const LUSH = [0.53, 0.67, 0.37], DRY = [0.67, 0.67, 0.42], STEPPE = [0.76, 0.68, 0.45];
  const SEABED = [0.8, 0.76, 0.58], FOREST = [0.32, 0.52, 0.28];
  const UPLAND = [0.62, 0.64, 0.42];
  // 修订：热带 / 林地 / 泰加 / 稀树草原拉开（世界全图上热带雨林、温带林、季风稀树草原、荒漠一眼分得出；Lanternvale 的配色只是一片温带草地）
  const DESERT = [0.89, 0.77, 0.54], STRAW = [0.82, 0.74, 0.48], TROPIC = [0.36, 0.62, 0.3], SAVANNA = [0.7, 0.68, 0.38];
  const MED_OLIVE = [0.66, 0.67, 0.42], TAIGA = [0.4, 0.56, 0.4];
  const ALPINE = [0.7, 0.66, 0.5];
  const TROP_FOREST = [0.2, 0.46, 0.22], TAIGA_FOREST = [0.24, 0.4, 0.32];
  function lerpInto(c, b, t) { t = M.clamp01(t); c[0] += (b[0] - c[0]) * t; c[1] += (b[1] - c[1]) * t; c[2] += (b[2] - c[2]) * t; }
  const _grass = [0, 0, 0], _fc = [0, 0, 0];
  // 结果（共享对象，调用方立即使用）：r, g, b 群落底色；forest 林地 0–1；sand 沙漠；barren 荒芜（岩石倾向）；snow 积雪；
  // des / st / tr / bo / med / sav 群落权重（植被、农田用）
  const _gi = { r: 0, g: 0, b: 0, forest: 0, sand: 0, barren: 0, snow: 0, des: 0, st: 0, tr: 0, bo: 0, med: 0, sav: 0, east: 0, river: 99 };
  // 沙漠在河流 / 湖泊两岸让位给绿洲
  function oasis(geo, x, z) { return 0.2 + 0.8 * M.smoothStep(0, 1, M.inverseLerp(1.0, 4.5, geo.riverDist(x, z))); }
  function groundInfo(geo, x, z, h, noForest) {
    const f = geo.sample(x, z);
    const north = M.inverseLerp(10, 95, z);
    const c = _grass;
    c[0] = LUSH[0]; c[1] = LUSH[1]; c[2] = LUSH[2];
    lerpInto(c, DRY, north * 0.85 * f[F_EAST]);
    // 修订：长江以南的湿润亚热带偏青绿（与北方的干草色、热带的浓绿拉开；只是底色）
    lerpInto(c, TROPIC, M.inverseLerp(42, 12, z) * 0.5 * f[F_EAST]);
    lerpInto(c, STEPPE, M.inverseLerp(30, 5, x) * 0.6 * f[F_OLD]);
    const med = f[F_MED], sav = f[F_SAV], st = f[F_STEPPE], bo = f[F_BOREAL], tr = f[F_TROP];
    let des = f[F_DESERT];
    if (med > 0.001) lerpInto(c, MED_OLIVE, med * 0.75);
    if (sav > 0.001) lerpInto(c, SAVANNA, sav * 0.75);
    const rd = geo.riverDist(x, z) / geo.rW;
    const oa = des > 0.001 || st > 0.001 ? 0.2 + 0.8 * M.smoothStep(0, 1, M.inverseLerp(1.0, 4.5, rd * geo.rW)) : 1;
    if (st > 0.001) lerpInto(c, STRAW, st * 0.85 * (0.45 + 0.55 * oa));
    if (bo > 0.001) lerpInto(c, TAIGA, bo * 0.8);
    if (tr > 0.001) lerpInto(c, TROPIC, tr * 0.85);
    if (des > 0.001) { des *= oa; lerpInto(c, DESERT, des); }
    // 林地（与树木分布同一噪声）
    let fw = 0;
    if (!noForest && h >= 0.42 && h < 3.5) {
      const forest = fbm(x * 0.09 + 100, z * 0.09 + 100, 3);
      const thr = 0.56 + des * 0.3 + st * 0.12 + sav * 0.05 - tr * 0.06 - bo * 0.05;
      fw = M.inverseLerp(thr - 0.02, thr + 0.1, forest);
      if (fw > 0) {
        let fc = FOREST;
        if (tr > 0.001 || bo > 0.001) {
          fc = _fc; fc[0] = FOREST[0]; fc[1] = FOREST[1]; fc[2] = FOREST[2];
          lerpInto(fc, TROP_FOREST, tr); lerpInto(fc, TAIGA_FOREST, bo);
        }
        lerpInto(c, fc, fw * 0.75);
      }
    }
    // 高地与高原荒原（旧版范围外）；世界地图里旧版西部高原也按高寒荒原着色
    let wNew = 1 - f[F_OLD], plat = f[F_PLAT] / 3;
    if (geo.world) {
      const wp = M.smoothStep(0, 1, M.inverseLerp(24, 8, x));
      if (wp > 0) { wNew = Math.max(wNew, 0.9 * wp); plat = Math.max(plat, wp); }
    }
    const alp = wNew > 0.001 ? wNew * M.clamp01(plat) : 0;
    if (h > 2.2) lerpInto(c, UPLAND, M.inverseLerp(2.2, 4.0, h) * 0.55);
    if (alp > 0) lerpInto(c, ALPINE, alp * M.inverseLerp(1.5, 3.5, h));
    const snow = 6.2 + 1.6 * wNew;
    const g = _gi;
    g.r = c[0]; g.g = c[1]; g.b = c[2];
    g.forest = fw; g.sand = M.clamp01(des * 1.1);
    g.barren = M.clamp01(alp * 0.7 + des * 0.5 + st * 0.15 + M.inverseLerp(3.0, 6.0, h) * 0.3);
    g.snow = M.clamp01((h - (snow - 0.3)) / 1.2);
    g.des = des; g.st = st; g.tr = tr; g.bo = bo; g.med = med; g.sav = sav; g.east = f[F_EAST]; g.river = rd;
    return g;
  }

  // MeshBuilder.toGeometry() 的分步版：每个顶点属性的转换各占一步（几万个顶点的合并网格一次转完在负载下要 5–10 ms）
  function* sliceGeometry(mb) {
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(mb.pos, 3));
    yield;
    g.setAttribute('normal', new THREE.Float32BufferAttribute(mb.nor, 3));
    yield;
    g.setAttribute('color', new THREE.Float32BufferAttribute(mb.col, 3));
    if (mb.hasUV) g.setAttribute('uv', new THREE.Float32BufferAttribute(mb.uvs, 2));
    yield;
    g.computeBoundingBox();
    g.computeBoundingSphere();
    return g;
  }

  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  const _m4 = new THREE.Matrix4(), _m4b = new THREE.Matrix4(), _col1 = new THREE.Color(1, 1, 1);
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
  // ===================================================== 第三版：地形分块 LOD ==
  // 高度源：地形范围内为主网格（间距 FINE = 0.625：1.25 间距的高度栅格经 Catmull-Rom 二倍上采样 + 细节噪声），
  // 范围外（远景）为 5.0 间距的远景栅格（双三次插值）。四叉树：每块 32×32 格，块边长 20 × 2^级；
  // 镜头到块包围盒的距离 < LOD_K × 块边长时细分（四个子块都已生成才切换）。块边有下垂的裙边，相邻两级之间不露缝。
  // 着色全部来自数据纹理（法线 / 凹凸遮蔽 / 高度、群落色、材质权重），与块的细分级别无关，因此 LOD 切换处没有明暗接缝。
  const GRASS_MAX = 3600, FLOWER_MAX = 500;
  // 第四版：地形的面化强度（0 = 平滑法线，1 = 完全按三角格平面着色）与坡地上的逐面亮度抖动
  const FACET_K = 0.85, FACET_JITTER = 0.09;
  // 第四轮：面化的等边三角格——边长（面积与原 1.25 方格的半格相当）与两条基向量（地图坐标 x, z；与 x 轴成 15° / 75°，
  // 三组格线都不与屏幕横竖平行）。改动时同步 art.js 的 sgFp 矩阵（基向量矩阵的逆）与雪面的重心换算矩阵
  const FACET_SIDE = 1.35, FACET_E1 = [Math.cos(Math.PI / 12), Math.sin(Math.PI / 12)], FACET_E2 = [Math.cos(5 * Math.PI / 12), Math.sin(5 * Math.PI / 12)];
  // 第四版：道具库草丛（高 0.51 米）缩到地图上约 0.2 单位
  const GRASS_LP_K = 1.0, FLOWER_LP_K = 0.7;
  // 绘制地面：草叶笔画的近景距离（满 / 无）、野花点密度、水彩斑块强度
  const PAINT_K = new THREE.Vector4(20, 50, 0.55, 1);
  const FINE = 0.625, NODE_Q = 32, OUTER_RES = 5, LOD_K = 2.0, DEEP_Y = -4.55;
  const N_NONE = 0, N_BUILT = 2, N_EMPTY = 3;
  let _nodeIndex = null, _nodeEdges = null;
  function nodeIndex() {
    if (_nodeIndex) return _nodeIndex;
    const n = NODE_Q, V = n + 1, idx = [];
    for (let j = 0; j < n; j++)
      for (let i = 0; i < n; i++) {
        // a = (i, j) b = (i, j+1) c = (i+1, j+1) d = (i+1, j)（i → x，j → 地图 z）；对角线与 surfaceY 相同
        const a = j * V + i, b = (j + 1) * V + i, c = (j + 1) * V + i + 1, d = j * V + i + 1;
        if (((i + j) & 1) === 0) idx.push(a, c, b, a, d, c);
        else idx.push(a, d, b, b, d, c);
      }
    // 裙边：四条边各 V 个顶点，向下复制；双面
    const edges = [[], [], [], []];
    for (let k = 0; k < V; k++) { edges[0].push(k); edges[1].push(n * V + k); edges[2].push(k * V); edges[3].push(k * V + n); }
    let s = V * V;
    for (const E of edges) {
      for (let k = 0; k < n; k++) {
        const a = E[k], b = E[k + 1], c = s + k + 1, d = s + k;
        idx.push(a, b, c, a, c, d, a, c, b, a, d, c);
      }
      s += V;
    }
    _nodeEdges = edges;
    _nodeIndex = new THREE.BufferAttribute(Uint16Array.from(idx), 1);
    return _nodeIndex;
  }

  class TerrainLOD {
    constructor(view) {
      this.view = view;
      this.group = new THREE.Group();
      this.group.name = 'Terrain';
      this.roots = [];
      this.queue = [];
      this.shown = [];
      this.frame = 0;
      this.builtCount = 0;
      this.maxNodes = 380;
      this.mat = null;
      this.stats = { nodes: 0, tris: 0, built: 0 };
      this._frustum = new THREE.Frustum();
      this._pm = new THREE.Matrix4();
      this._box = new THREE.Box3();
      this._cam = new THREE.Vector3();
      this._want = [];
    }
    // lattice：块网格原点（= 主网格原点）；outerBox / innerBox：地图坐标 { x0, z0, x1, z1 }
    init(lx, lz, outerBox, innerBox) {
      this.inner = innerBox;
      const w = Math.max(outerBox.x1 - outerBox.x0, outerBox.z1 - outerBox.z0);
      const R = Math.max(3, Math.ceil(Math.log2(w / (NODE_Q * FINE) / 2.5)));
      const S = NODE_Q * FINE * Math.pow(2, R);
      for (let kx = Math.floor((outerBox.x0 - lx) / S); kx * S + lx < outerBox.x1; kx++)
        for (let kz = Math.floor((outerBox.z0 - lz) / S); kz * S + lz < outerBox.z1; kz++)
          this.roots.push(this._node(R, lx + kx * S, lz + kz * S, null));
    }
    _node(level, x0, z0, parent) {
      const size = NODE_Q * FINE * Math.pow(2, level), I = this.inner;
      const touches = x0 < I.x1 && x0 + size > I.x0 && z0 < I.z1 && z0 + size > I.z0;
      return {
        level, x0, z0, size, step: size / NODE_Q, minLevel: touches ? 0 : 3, parent, children: null,
        state: N_NONE, mesh: null, minY: parent ? parent.minY : -5, maxY: parent ? parent.maxY : 14, lastUsed: 0, dist: 0,
      };
    }
    _split(nd) {
      const h = nd.size / 2;
      nd.children = [this._node(nd.level - 1, nd.x0, nd.z0, nd), this._node(nd.level - 1, nd.x0 + h, nd.z0, nd),
        this._node(nd.level - 1, nd.x0, nd.z0 + h, nd), this._node(nd.level - 1, nd.x0 + h, nd.z0 + h, nd)];
    }
    // 生成一块的网格（约 1 ms）
    build(nd) {
      const n = NODE_Q, V = n + 1, step = nd.step, G = n + 3, view = this.view;
      const hh = this._tmp || (this._tmp = new Float32Array(G * G));
      let mn = Infinity, mx = -Infinity;
      for (let j = -1; j <= V; j++) {
        const z = nd.z0 + j * step;
        for (let i = -1; i <= V; i++) {
          const h = view._vertexH(nd.x0 + i * step, z);
          hh[(j + 1) * G + i + 1] = h;
          if (i >= 0 && i < V && j >= 0 && j < V) { if (h < mn) mn = h; if (h > mx) mx = h; }
        }
      }
      nd.minY = mn; nd.maxY = mx;
      if (mx < DEEP_Y) { nd.state = N_EMPTY; return; }
      const nv = V * V + 4 * V, P = new Float32Array(nv * 3), N = new Float32Array(nv * 3);
      const inv2 = 1 / (2 * step);
      for (let j = 0; j < V; j++)
        for (let i = 0; i < V; i++) {
          const o = (j * V + i) * 3, q = (j + 1) * G + i + 1;
          const hx = (hh[q + 1] - hh[q - 1]) * inv2, hz = (hh[q + G] - hh[q - G]) * inv2;
          const l = 1 / Math.sqrt(hx * hx + 1 + hz * hz);
          P[o] = nd.x0 + i * step; P[o + 1] = hh[q]; P[o + 2] = -(nd.z0 + j * step);
          N[o] = -hx * l; N[o + 1] = l; N[o + 2] = hz * l;
        }
      const idx = nodeIndex(), sd = 0.25 + step * 0.9;
      let s = V * V;
      for (const E of _nodeEdges) {
        for (let k = 0; k < V; k++) {
          const a = E[k] * 3, o = (s + k) * 3;
          P[o] = P[a]; P[o + 1] = P[a + 1] - sd; P[o + 2] = P[a + 2];
          N[o] = N[a]; N[o + 1] = N[a + 1]; N[o + 2] = N[a + 2];
        }
        s += V;
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(P, 3));
      g.setAttribute('normal', new THREE.BufferAttribute(N, 3));
      g.setIndex(idx);
      g.boundingBox = new THREE.Box3(new THREE.Vector3(nd.x0, mn - sd, -(nd.z0 + nd.size)), new THREE.Vector3(nd.x0 + nd.size, mx, -nd.z0));
      g.boundingSphere = g.boundingBox.getBoundingSphere(new THREE.Sphere());
      const m = new THREE.Mesh(g, this.mat);
      m.name = 'TerrainL' + nd.level;
      // 第四版：地形不投影（Lanternvale 的地面没有投影；山脊在低太阳下投出的锯齿状尖楔很难看，面化的明暗已经交代了起伏），只接收树、城的投影
      m.castShadow = !this.lpTerrain; m.receiveShadow = true;
      m.visible = false;
      m.matrixAutoUpdate = false;
      m.userData.lodNode = true;
      this.group.add(m);
      nd.mesh = m;
      nd.state = N_BUILT;
      this.builtCount++;
    }
    _free(nd) {
      if (nd.mesh) { this.group.remove(nd.mesh); nd.mesh.geometry.dispose(); nd.mesh = null; this.builtCount--; }
      nd.state = N_NONE;
    }
    // 预先生成到 level 级（生成器，每块一步）
    *prebuild(level) {
      const rec = function* (self, nd) {
        if (nd.state === N_NONE) { self.build(nd); yield; }
        if (nd.state === N_EMPTY || nd.level <= Math.max(level, nd.minLevel)) return;
        if (!nd.children) self._split(nd);
        for (const c of nd.children) yield* rec(self, c);
      };
      for (const r of this.roots) yield* rec(this, r);
    }
    _boxOf(nd) {
      const b = this._box;
      b.min.set(nd.x0, nd.minY - 0.5, -(nd.z0 + nd.size));
      b.max.set(nd.x0 + nd.size, nd.maxY + 0.2, -nd.z0);
      return b;
    }
    _select(nd, want) {
      if (nd.state === N_EMPTY) return;
      const b = this._boxOf(nd);
      if (!this._frustum.intersectsBox(b)) return;
      const d = b.distanceToPoint(this._cam);
      if (nd.level > nd.minLevel && d < LOD_K * nd.size) {
        if (!nd.children) this._split(nd);
        let ready = true;
        for (const c of nd.children) if (c.state === N_NONE) { ready = false; c.dist = d; this.queue.push(c); }
        if (ready) { for (const c of nd.children) this._select(c, want); return; }
      }
      if (nd.state === N_BUILT) { want.push(nd); nd.lastUsed = this.frame; }
    }
    // 每帧：选择可见块、按距离补建缺少的细块（每帧最多 budgetMs），超出缓存上限时释放最久未用的块。返回待建块数
    update(camera, budgetMs) {
      this.frame++;
      camera.updateMatrixWorld();
      this._pm.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse);
      this._frustum.setFromProjectionMatrix(this._pm);
      this._cam.copy(camera.position);
      const want = this._want;
      want.length = 0;
      this.queue.length = 0;
      for (const r of this.roots) this._select(r, want);
      for (const nd of this.shown) if (nd.mesh && nd.lastUsed !== this.frame) nd.mesh.visible = false;
      let tris = 0;
      for (const nd of want) { nd.mesh.visible = true; tris += NODE_Q * NODE_Q * 2; }
      const prev = this.shown;
      this.shown = want; this._want = prev;
      this.stats.nodes = want.length; this.stats.tris = tris;
      const pending = this.queue.length;
      if (pending) {
        this.queue.sort((a, b) => a.dist - b.dist || b.level - a.level);
        const t0 = now();
        for (const nd of this.queue) {
          if (nd.state !== N_NONE) continue;
          this.build(nd);
          this.stats.built++;
          if (now() - t0 > budgetMs) break;
        }
      }
      if (this.builtCount > this.maxNodes) this._evict();
      return pending;
    }
    _evict() {
      const list = [];
      const rec = nd => {
        if (!nd.children) return;
        for (const c of nd.children) { if (c.state === N_BUILT && c.lastUsed < this.frame - 30) list.push(c); rec(c); }
      };
      for (const r of this.roots) rec(r);
      list.sort((a, b) => a.lastUsed - b.lastUsed || a.level - b.level);
      for (let k = 0; k < list.length && this.builtCount > this.maxNodes * 0.8; k++) this._free(list[k]);
    }
    dispose() {
      this.group.traverse(o => { if (o.isMesh && o.geometry) o.geometry.dispose(); });
      if (this.mat) this.mat.dispose();
      this.roots = []; this.shown = [];
    }
  }

  // ---------------------------------------------------------- 战略地图的水面 --
  // 第四版（Lanternvale 风格的干净水面）：按像素的水深分三档平涂（浅处青绿 → 蓝 → 深蓝，与 Lanternvale 的溪流 / 池塘同一色系），
  // 两层缓缓流动的「焦散」亮纹（浅处明显、深海很淡）、一条干净的白色岸线 + 一道淡淡的离岸浪线、朝太阳的柔和光带（没有硬高光）；
  // 光照取场景氛围（SG.LP.Mood）的太阳 / 天光、夜色调、黄金时刻提亮与雾，与地面、道具同步变化。网格本身是平的（无接缝）。
  const MAP_WATER_VS = `
varying vec3 vW;
#include <fog_pars_vertex>
void main() {
  vec4 w = modelMatrix * vec4( position, 1.0 );
  vW = w.xyz;
  vec4 mvPosition = viewMatrix * w;
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}`;
  const MAP_WATER_FS = `
uniform float uTime;
uniform sampler2D uCaustic;
uniform sampler2D sgMapN;
uniform sampler2D sgOutC;
uniform vec4 sgXfN;
uniform vec4 sgXfO;
uniform vec4 sgInner;
uniform vec3 uSunDir;
uniform vec3 uShallow;
uniform vec3 uMid;
uniform vec3 uDeep;
uniform vec3 uFoam;
uniform vec4 lpSunC;
uniform vec4 lpSkyC;
uniform vec4 lpGrade;
uniform vec4 lpWarmth;
uniform vec4 lpFog;
uniform vec4 lpNight;
uniform float lpOut;
varying vec3 vW;
#include <common>
#include <fog_pars_fragment>
const vec3 W_LUMA = vec3( 0.3, 0.59, 0.11 );
vec3 wLin( vec3 c ) { return mix( c / 12.92, pow( ( c + 0.055 ) / 1.055, vec3( 2.4 ) ), step( 0.04045, c ) ); }
vec3 wEnc( vec3 c ) { c = max( c, vec3( 0.0 ) ); return lpOut > 0.5 ? mix( c * 12.92, 1.055 * pow( c, vec3( 1.0 / 2.4 ) ) - 0.055, step( 0.0031308, c ) ) : c; }
float sgTerrainH( vec2 p ) {
  vec2 e = min( p - sgInner.xy, sgInner.zw - p );
  float wI = clamp( min( e.x, e.y ) * 0.2, 0.0, 1.0 );
  float h = 0.0;
  if ( wI < 1.0 ) h = ( texture2D( sgOutC, p * sgXfO.zw + sgXfO.xy ).a * 8.0 - 4.0 ) * ( 1.0 - wI );
  if ( wI > 0.0 ) h += ( texture2D( sgMapN, p * sgXfN.zw + sgXfN.xy ).a * 8.0 - 4.0 ) * wI;
  return h;
}
void main() {
  vec2 p = vec2( vW.x, - vW.z );
  float t = uTime;
  float depth = max( 0.0, - sgTerrainH( p ) );
  // 平涂的三档水色（uShallow / uMid / uDeep / uFoam 已是线性色：省掉每像素的 sRGB → 线性换算）
  vec3 c = mix( uShallow, uMid, smoothstep( 0.04, 0.9, depth ) );
  c = mix( c, uDeep, smoothstep( 0.9, 3.4, depth ) );
  // 焦散亮纹：两层缓慢流动，浅处明显
  float k1 = texture2D( uCaustic, p * 0.085 + vec2( t * 0.011, t * 0.006 ) ).r;
  float k2 = texture2D( uCaustic, p * 0.19 + vec2( - t * 0.008, t * 0.012 ) ).r;
  float kk = k1 * 0.6 + k2 * 0.4;
  float shallowK = 1.0 - smoothstep( 0.25, 2.8, depth );
  float big = texture2D( uCaustic, p * 0.011 + vec2( 0.3, 0.7 ) ).r;
  // （线性空间里的明暗：sRGB 中的 ×f 约等于线性中的 ×f²）
  float cf = 0.95 + 0.1 * kk * mix( 0.5, 1.0, shallowK ) + ( big - 0.5 ) * 0.08;
  c *= cf * cf;
  c = mix( c, c * 1.36 + 0.012, smoothstep( 0.64, 0.9, kk ) * mix( 0.1 * smoothstep( 0.45, 0.75, big ), 0.85, shallowK ) );
  // 岸线（干净的白边）+ 一道离岸浪线
  float shore = 1.0 - smoothstep( 0.0, 0.06 + 0.05 * k2, depth );
  float band = smoothstep( 0.8, 0.97, 0.5 + 0.5 * sin( depth * 13.0 - t * 1.3 + k2 * 3.0 ) ) * ( 1.0 - smoothstep( 0.1, 0.42, depth ) );
  float foam = clamp( shore + band * 0.35, 0.0, 1.0 );
  // 光照（朝上的水面：包裹式太阳 + 天光；夜色调、黄金时刻提亮）
  vec3 alb = c;
  vec3 L = normalize( uSunDir );
  vec3 sunC = lpSunC.w > 0.5 ? lpSunC.rgb : vec3( 0.95, 0.88, 0.76 );
  vec3 sky = lpSkyC.w > 0.5 ? lpSkyC.rgb : vec3( 0.3, 0.34, 0.45 );
  float wrap = clamp( ( L.y + 0.45 ) / 1.45, 0.0, 1.0 );
  wrap = wrap * wrap * ( 3.0 - 2.0 * wrap );
  vec3 light = sunC * wrap + sky;
  vec3 moon = alb;
  if ( lpGrade.x > 0.0 ) moon = mix( alb, dot( alb, W_LUMA ) * lpGrade.yzw, clamp( lpGrade.x, 0.0, 1.0 ) );
  vec3 col = moon * light;
  // 朝太阳的柔和光带（反射方向接近太阳处），没有硬高光
  vec3 V = normalize( cameraPosition - vW );
  float sheen = pow( max( dot( reflect( - V, vec3( 0.0, 1.0, 0.0 ) ), L ), 0.0 ), 12.0 ) * ( 1.0 - lpNight.x * 0.8 );
  col += sunC * sheen * ( 0.12 + 0.12 * kk );
  col = mix( col, uFoam * light, foam * 0.85 );
  if ( lpWarmth.x > 0.0 ) { float cl = dot( col, W_LUMA ); col = max( cl + ( col - cl ) * ( 1.0 + lpWarmth.x ), 0.0 ); }
  #ifdef USE_FOG
    float fk = clamp( ( length( vW - cameraPosition ) - fogNear ) / max( fogFar - fogNear, 0.01 ), 0.0, 1.0 ) * lpFog.x;
    col = mix( col, lpFog.y > 0.5 ? fogColor * fogColor : fogColor, fk );
  #endif
  // 修订：浅水（河、湖）不再透出大片奶油色的沙底（河湖读成灰白的青）
  float alpha = mix( 0.72, 0.93, smoothstep( 0.0, 1.6, depth ) );
  alpha = max( alpha, foam * 0.92 );
  gl_FragColor = vec4( wEnc( col ), alpha );
}`;
  // 焦散纹理（Lanternvale WorldTextures.Water：整数频率的正弦叠加、略加扭曲，可平铺）
  let _causticTex = null;
  function causticTexture() {
    if (_causticTex) return _causticTex;
    const n = 128, d = new Uint8Array(n * n * 4), tau = Math.PI * 2;
    for (let y = 0; y < n; y++)
      for (let x = 0; x < n; x++) {
        const u = x / n, v = y / n;
        const w1 = Math.sin(tau * (u * 2 + v) + 1.7 * Math.sin(tau * v * 3));
        const w2 = Math.sin(tau * (-u + v * 3) + 1.3 * Math.sin(tau * u * 2 + 0.5));
        const w3 = Math.sin(tau * (u * 4 + v * 5) + 0.9);
        const k = (w1 + w2) * 0.25 + w3 * 0.1 + 0.5;
        const o = (y * n + x) * 4;
        d[o] = d[o + 1] = d[o + 2] = b8(k); d[o + 3] = 255;
      }
    const t = new THREE.DataTexture(d, n, n, THREE.RGBAFormat);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.magFilter = THREE.LinearFilter; t.minFilter = THREE.LinearMipmapLinearFilter; t.generateMipmaps = true;
    t.needsUpdate = true;
    _causticTex = t;
    return t;
  }
  function srgbLin(r, g, b) {
    const f = c => (c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4));
    return new THREE.Vector3(f(r), f(g), f(b));
  }
  function mapWaterMaterial(tu) {
    const w = SG.Gfx.water().uniforms;   // 共用时间与太阳方向（随氛围转动）
    const mat = new THREE.ShaderMaterial({
      uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, {
        // 线性色（着色器不再逐像素换算；sRGB 原值 0.50,0.78,0.80 / 0.34,0.62,0.71 / 0.21,0.43,0.63 / 0.97,0.97,0.94）
        uShallow: { value: srgbLin(0.5, 0.78, 0.8) },
        uMid: { value: srgbLin(0.34, 0.62, 0.71) },
        uDeep: { value: srgbLin(0.21, 0.43, 0.63) },
        uFoam: { value: srgbLin(0.97, 0.97, 0.94) },
      }]),
      vertexShader: MAP_WATER_VS, fragmentShader: MAP_WATER_FS,
      transparent: true, depthWrite: false, fog: true,
    });
    const u = mat.uniforms;
    u.uTime = w.uTime; u.uSunDir = w.uSunDir;
    u.uCaustic = { value: causticTexture() };
    const G = SG.LP && SG.LP.globals;
    for (const k of ['lpSunC', 'lpSkyC', 'lpGrade', 'lpWarmth', 'lpFog', 'lpNight', 'lpOut']) u[k] = G && G[k] ? G[k] : { value: k === 'lpOut' ? 1 : new THREE.Vector4(0, 0, 0, 0) };
    if (!G) u.lpFog.value.set(1, 0, 0, 0);
    u.sgMapN = tu.sgMapN; u.sgOutC = tu.sgOutC; u.sgXfN = tu.sgXfN; u.sgXfO = tu.sgXfO; u.sgInner = tu.sgInner;
    mat.name = 'MapWater';
    return mat;
  }

  // ---------------------------------------------------------- 海路虚线 --
  // 第三版修订：半宽 0.16 × clamp(镜头距离 / 60, 0.35, 1)（近看变细），镜头距离 12–28 内淡出，略带蓝的白色、不透明度 0.6
  const LANE_VS = `attribute vec2 aOff;
uniform float uW;
varying float vA;
#include <fog_pars_vertex>
void main() {
  vec4 wp = modelMatrix * vec4( position, 1.0 );
  float d = distance( cameraPosition, wp.xyz );
  wp.xz += aOff * uW * clamp( d / 60.0, 0.35, 1.0 );
  vA = smoothstep( 12.0, 28.0, d );
  vec4 mvPosition = viewMatrix * wp;
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}`;
  const LANE_FS = `uniform vec3 uColor;
uniform float uOpacity;
varying float vA;
#include <fog_pars_fragment>
void main() {
  gl_FragColor = vec4( uColor, uOpacity * vA );
  #include <fog_fragment>
}`;
  function seaLaneMaterial() {
    const m = new THREE.ShaderMaterial({
      uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uW: { value: 0.16 }, uColor: { value: new THREE.Color(0.88, 0.93, 0.98) }, uOpacity: { value: 0.62 } }]),
      vertexShader: LANE_VS, fragmentShader: LANE_FS, transparent: true, depthWrite: false, fog: true,
      side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -4,
    });
    m.name = 'SeaLanes';
    return m;
  }

  // ---------------------------------------------------------- 云的淡出 --
  // 网页版补充（C# 的云始终不透明）：云离镜头越近越透明，镜头距离 ≥ CLOUD_FADE_FAR 时不透明，≤ CLOUD_FADE_NEAR 时完全隐去。
  // 标题 / 全图视角（镜头距离 85 以上）视野里的云离镜头 51 以上，几乎不透明；战略视角（45～48）视野里的云离镜头约 15～46，
  // 除画面最上缘远处的淡影外全部隐去，城池上方不再留下半透明的灰色多边形。
  const CLOUD_FADE_NEAR = 44, CLOUD_FADE_FAR = 64;
  const CLOUD_HIDE_Y = 70, CLOUD_SHOW_DY = 28;     // 第三版修订：镜头高度 ≤ 70 时云全部隐去，≥ 98 时全显
  // 云团贴图（128²，程序生成，全部云共用）：径向渐隐 × 两级值噪声（蓬松的边），上亮下灰（RGB），alpha 柔边
  let _cloudTex = null, _cloudMat = null;
  function cloudTexture() {
    if (_cloudTex) return _cloudTex;
    const N = 128, cv = document.createElement('canvas');
    cv.width = cv.height = N;
    const g = cv.getContext('2d'), img = g.createImageData(N, N);
    const h = (x, y) => { const v = Math.sin(x * 127.1 + y * 311.7) * 43758.5453; return v - Math.floor(v); };
    const vn = (x, y) => {
      const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
      return (h(xi, yi) * (1 - u) + h(xi + 1, yi) * u) * (1 - v) + (h(xi, yi + 1) * (1 - u) + h(xi + 1, yi + 1) * u) * v;
    };
    for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
      const px = (x + 0.5) / N * 2 - 1, py = (y + 0.5) / N * 2 - 1;      // py：-1 上 → 1 下
      const r = Math.hypot(px, py * 1.05);
      const nz = vn(px * 3 + 7, py * 3 + 3) * 0.65 + vn(px * 7 + 1, py * 7 + 9) * 0.35;
      let a = 1 - M.smoothStep(0, 1, M.clamp01((r - 0.25 + (nz - 0.5) * 0.5) / 0.7));
      a *= M.clamp01(1.25 - Math.max(0, py) * 0.5);                      // 底部略收
      const lit = M.clamp01(0.5 - py * 0.55 + (nz - 0.5) * 0.35);       // 上亮下灰
      const i = (y * N + x) * 4;
      img.data[i] = Math.round(255 * (0.74 + 0.26 * lit));
      img.data[i + 1] = Math.round(255 * (0.76 + 0.24 * lit));
      img.data[i + 2] = Math.round(255 * (0.82 + 0.18 * lit));
      img.data[i + 3] = Math.round(255 * M.clamp01(a));
    }
    g.putImageData(img, 0, 0);
    _cloudTex = new THREE.CanvasTexture(cv);
    _cloudTex.minFilter = THREE.LinearMipmapLinearFilter;
    return _cloudTex;
  }
  const CLOUD_VS = `attribute vec2 aCorner;
attribute float aSize;
attribute float aShade;
varying vec2 vUv;
varying float vShade;
#include <fog_pars_vertex>
void main() {
  vec4 mvPosition = modelViewMatrix * vec4( position, 1.0 );
  mvPosition.xy += aCorner * aSize;
  vUv = vec2( aCorner.x * 0.5 + 0.5, 0.5 - aCorner.y * 0.5 );
  vShade = aShade;
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}`;
  const CLOUD_FS = `uniform sampler2D uMap;
uniform float uOpacity;
uniform vec3 uTint;
uniform vec4 lpSunC;
uniform vec4 lpSkyC;
uniform vec4 lpGrade;
uniform vec3 uRefSun;
uniform vec3 uRefSky;
varying vec2 vUv;
varying float vShade;
#include <fog_pars_fragment>
void main() {
  vec4 t = texture2D( uMap, vUv );
  vec3 c = t.rgb * uTint * vShade;
  // 第四版：云随氛围（LP.Mood 的太阳 + 天光与正午之比；夜里偏蓝灰）
  if ( lpSunC.w > 0.5 ) {
    vec3 k = ( lpSunC.rgb * 0.6 + lpSkyC.rgb ) / max( uRefSun * 0.6 + uRefSky, vec3( 0.001 ) );
    c *= pow( k, vec3( 1.0 / 2.2 ) );
    if ( lpGrade.x > 0.0 ) c = mix( c, dot( c, vec3( 0.3, 0.59, 0.11 ) ) * lpGrade.yzw, clamp( lpGrade.x, 0.0, 1.0 ) );
  }
  gl_FragColor = vec4( c, t.a * uOpacity );
  #include <fog_fragment>
}`;
  // 每朵云一份材质（各自的不透明度），共用同一着色器程序与贴图
  function cloudMaterial() {
    if (!_cloudMat) {
      _cloudMat = new THREE.ShaderMaterial({
        uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uMap: { value: null }, uOpacity: { value: 1 }, uTint: { value: new THREE.Color(1.0, 0.99, 0.96) } }]),
        vertexShader: CLOUD_VS, fragmentShader: CLOUD_FS,
        transparent: true, depthWrite: false, fog: true,
      });
      _cloudMat.name = 'CloudBillboard';
    }
    const m = _cloudMat.clone();
    m.uniforms.uMap.value = cloudTexture();
    const G = SG.LP && SG.LP.globals;
    if (G) {
      for (const k of ['lpSunC', 'lpSkyC', 'lpGrade']) m.uniforms[k] = G[k];
      const noon = SG.LP.moodAt(12.5), lin = SG.LP.Paint.lin, sl = lin(noon.sunCol), kl = lin(noon.sky);
      m.uniforms.uRefSun = { value: new THREE.Vector3(sl[0] * noon.sunI, sl[1] * noon.sunI, sl[2] * noon.sunI) };
      m.uniforms.uRefSky = { value: new THREE.Vector3(kl[0] * noon.ambI, kl[1] * noon.ambI, kl[2] * noon.ambI) };
    } else {
      m.uniforms.lpSunC = { value: new THREE.Vector4() }; m.uniforms.lpSkyC = { value: new THREE.Vector4() }; m.uniforms.lpGrade = { value: new THREE.Vector4() };
      m.uniforms.uRefSun = { value: new THREE.Vector3(1, 1, 1) }; m.uniforms.uRefSky = { value: new THREE.Vector3(1, 1, 1) };
    }
    return m;
  }

  // ---------------------------------------------------------- 植被与小物件（实例化）--
  // 叶片 / 屋顶等顶点（属性 sgLeaf = 1）乘以实例颜色，树干、墙体保持原色：一种几何体可画出各种颜色
  let _treeMat = null, _grassMat = null;
  const FOLIAGE_VS = '#include <color_vertex>\n#if defined( USE_INSTANCING_COLOR ) && defined( USE_COLOR )\n\tvColor.xyz = color.xyz * mix( vec3( 1.0 ), instanceColor.xyz, sgLeaf );\n#endif';
  // 第三版修订：kind = 'leaf'（树冠：程序化叶簇明暗 + 法线起伏，近看不再是塑料球）、'rock'（石块：颗粒、层理与棱边提亮）或缺省（村舍等）。
  // 细节按模型局部坐标（再加实例位置错开），只作用于 sgLeaf = 1 的部分（树干、屋顶不受影响）
  const LEAF_VS_PARS = 'attribute float sgLeaf;\nvarying float vSgLeaf;\nvarying vec3 vSgLeafP;\n';
  const LEAF_VS_BEGIN = '#include <begin_vertex>\n\tvSgLeaf = sgLeaf;\n\tvSgLeafP = transformed;\n#ifdef USE_INSTANCING\n\tvSgLeafP += instanceMatrix[3].xyz * 0.37;\n#endif';
  const LEAF_FS_PARS = `varying float vSgLeaf;
varying vec3 vSgLeafP;
float sgLN( vec3 p ) { return sin( p.x + sin( p.y * 1.3 ) * 1.2 ) * sin( p.y * 1.1 + sin( p.z * 1.2 ) * 1.2 ) * sin( p.z * 0.9 + sin( p.x * 1.4 ) * 1.2 ); }
`;
  const LEAF_FS_COLOR = `#include <color_fragment>
	vec3 sgLq = vSgLeafP * SG_LEAF_F;
	float sgL1 = sgLN( sgLq ), sgL2 = sgLN( sgLq * 2.63 + 5.3 ), sgL3 = sgLN( sgLq * 6.1 + 1.7 );
#ifdef SG_LEAF
	float sgLc = sgL1 * 0.5 + sgL2 * 0.32 + sgL3 * 0.18;
	diffuseColor.rgb *= mix( 1.0, 0.84 + 0.34 * sgLc + 0.16 * smoothstep( 0.35, 0.85, sgL2 * 0.6 + sgL3 * 0.4 ), vSgLeaf );
#else
	float sgLc = sgL1 * 0.25 + sgL2 * 0.35 + sgL3 * 0.4;
	diffuseColor.rgb *= mix( 1.0, 0.86 + 0.22 * sgLc + 0.1 * sin( vSgLeafP.y * 41.0 + sgL1 * 2.0 ), vSgLeaf );
#endif
`;
  const LEAF_FS_NORMAL = '#include <normal_fragment_maps>\n\tnormal = normalize( normal + vSgLeaf * SG_LEAF_N * vec3( sgL2 + sgL3 * 0.5, sgL1 * 0.6, sgL3 - sgL1 * 0.4 ) );';
  const _vegMats = {};
  function treeMaterial(kind) {
    kind = kind || 'plain';
    if (_vegMats[kind]) return _vegMats[kind];
    const mat = SG.Gfx.newLowPoly();
    const base = mat.onBeforeCompile;
    if (kind !== 'plain') mat.defines = kind === 'leaf' ? { SG_LEAF: '', SG_LEAF_F: '16.0', SG_LEAF_N: '0.42' } : { SG_ROCK: '', SG_LEAF_F: '11.0', SG_LEAF_N: '0.3' };
    mat.onBeforeCompile = function treeCompile(shader, renderer) {
      base.call(this, shader, renderer);
      if (kind === 'plain') { shader.vertexShader = 'attribute float sgLeaf;\n' + shader.vertexShader.replace('#include <color_vertex>', FOLIAGE_VS); return; }
      shader.vertexShader = LEAF_VS_PARS + shader.vertexShader.replace('#include <color_vertex>', FOLIAGE_VS).replace('#include <begin_vertex>', LEAF_VS_BEGIN);
      shader.fragmentShader = LEAF_FS_PARS + shader.fragmentShader.replace('#include <color_fragment>', LEAF_FS_COLOR).replace('#include <normal_fragment_maps>', LEAF_FS_NORMAL);
    };
    mat.name = kind === 'leaf' ? 'TreeSmooth' : kind === 'rock' ? 'RockSmooth' : 'VegSmooth';
    _vegMats[kind] = mat;
    return mat;
  }
  // 草丛 / 野花：同上，另随风轻摆（按顶点高度）
  function grassMaterial() {
    if (_grassMat) return _grassMat;
    const mat = SG.Gfx.newLowPoly();
    const base = mat.onBeforeCompile, time = SG.Gfx.water().uniforms.uTime;
    mat.onBeforeCompile = function grassCompile(shader, renderer) {
      base.call(this, shader, renderer);
      shader.uniforms.sgWindT = time;
      shader.vertexShader = 'attribute float sgLeaf;\nuniform float sgWindT;\n' + shader.vertexShader
        .replace('#include <color_vertex>', FOLIAGE_VS)
        .replace('#include <begin_vertex>', '#include <begin_vertex>\n#ifdef USE_INSTANCING\n\tfloat sgPh = instanceMatrix[3].x * 0.7 + instanceMatrix[3].z * 0.5;\n\ttransformed.x += sin( sgWindT * 1.9 + sgPh ) * transformed.y * 0.22;\n\ttransformed.z += cos( sgWindT * 1.4 + sgPh * 1.3 ) * transformed.y * 0.12;\n#endif');
    };
    mat.name = 'GrassSmooth';
    _grassMat = mat;
    return mat;
  }
  const TRUNK = new THREE.Color(0.38, 0.27, 0.16), BIRCH_BARK = new THREE.Color(0.86, 0.84, 0.78);
  // 合并「保持原色」与「乘实例色」两部分为一个几何体
  function mergeLeaf(keep, tint) {
    const a = keep && keep.count ? keep.toGeometry() : null, b = tint.toGeometry();
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
    return g;
  }
  const _vegGeo = {};
  const TREE_HERO_R = 18;
  // 树种（高约 1）。lod 0 = 近景（平滑细分），1 = 远景（简化）。
  // broad 圆冠阔叶 | broad2 双团阔叶 | pine 松 | spruce 云杉 / 杉 | cypress 柏 | poplar 杨 | palm 棕榈 | acacia 金合欢 | shrub 灌丛
  // willow 柳 | olive 橄榄 | bamboo 竹 | birch 桦 | stonepine 伞松
  function treeGeometry(kind, lod) {
    const key = kind + ':' + lod;
    if (_vegGeo[key]) return _vegGeo[key];
    const tk = new SG.MeshBuilder(), lf = new SG.MeshBuilder();
    const W = new THREE.Color(0.93, 0.93, 0.93), W2 = new THREE.Color(0.82, 0.82, 0.82);
    // lod 2 = 特写（第三版修订：镜头 20 单位以内）：二级细分的树冠 + 三团叶簇（轮廓起伏，不再是八边形），树干、松层更多段
    const hero = lod === 2;
    const d = hero ? 2 : lod ? 0 : 1, ts = hero ? 9 : lod ? 4 : 6, cs = hero ? 18 : lod ? 6 : 10;
    const O = V(0, 0, 0);
    const crown = (c, r, col, seed, rough) => {
      const ro = rough === undefined ? 0.09 : rough;
      // 第三版修订：法线向椭球法线混合（中远景的树冠轮廓仍有起伏，明暗却不再显出一块块小面）
      lf.sphere(c, r, col || W, d, { seed, rough: ro, bottomShade: 0.32, top: 0.08, smoothN: hero ? 0.4 : 0.75 });
      if (!hero || r.x < 0.12) return;
      for (let k = 0; k < 3; k++) {
        const a = seed * 1.7 + k * 2.09, up = 0.25 + 0.3 * ((k + seed) % 2);
        const cc = V(c.x + Math.cos(a) * r.x * 0.62, c.y + up * r.y, c.z + Math.sin(a) * r.z * 0.62);
        lf.sphere(cc, V(r.x * 0.5, r.y * 0.52, r.z * 0.5), k === 1 ? W2 : W, 1, { seed: seed + 7 * k + 1, rough: ro * 0.8, bottomShade: 0.28, top: 0.1 });
      }
    };
    const trunk = (h, r0, col) => tk.lathe(O, [[r0 || 0.065, 0], [(r0 || 0.065) * 0.7, h]], ts, col || TRUNK);
    const tier = (y, r, h, col) => lf.lathe(V(0, y, 0), hero ? [[0, h * 0.12], [r * 0.9, h * 0.035], [r, 0, null, true], [r * 0.72, h * 0.2], [r * 0.46, h * 0.5], [r * 0.2, h * 0.8], [0, h]]
      : [[0, h * 0.1], [r, 0, null, true], [r * 0.45, h * 0.55], [0, h]], cs, col || W);
    if (lod === 1) {
      // 远景：极简形体（无树干；十几到二十几个三角形），近看由 lod 0 替换
      const blob = (c, r, col) => lf.sphere(c, r, col || W, 0, { bottomShade: 0.3, top: 0.1 });
      const spire = (y, r, h, sg) => lf.lathe(V(0, y, 0), [[r, 0, null, true], [0, h]], sg || 5, W);
      switch (kind) {
        case 'pine': spire(0.18, 0.42, 1.0); break;
        case 'spruce': spire(0.1, 0.34, 1.1); break;
        case 'cypress': lf.lathe(V(0, 0.08, 0), [[0.17, 0.15], [0.16, 0.7], [0, 1.25]], 5, W); break;
        case 'poplar': blob(V(0, 0.75, 0), V(0.17, 0.55, 0.17)); break;
        case 'birch': blob(V(0, 0.8, 0), V(0.27, 0.42, 0.27)); break;
        case 'shrub': blob(V(0, 0.13, 0), V(0.3, 0.2, 0.3)); break;
        case 'acacia': case 'stonepine': blob(V(0, kind === 'acacia' ? 0.66 : 0.9, 0), V(0.48, 0.19, 0.44)); break;
        case 'bamboo': blob(V(0, 0.62, 0), V(0.22, 0.42, 0.22)); break;
        case 'palm': {
          const top = V(0.2, 0.92, 0), up = V(0, 1, 0);
          tk.lathe(V(0, 0, 0), [[0.05, 0], [0.04, 0.5]], 3, new THREE.Color(0.5, 0.4, 0.27));
          tk.lathe(V(0.06, 0.5, 0), [[0.04, 0], [0.035, 0.42]], 3, new THREE.Color(0.5, 0.4, 0.27));
          for (let k = 0; k < 5; k++) {
            const a = k * 1.2566 + 0.3, ca = Math.cos(a), sa = Math.sin(a);
            lf.triN(top, V(top.x + ca * 0.5 - sa * 0.1, top.y - 0.2, top.z + sa * 0.5 + ca * 0.1), V(top.x + ca * 0.5 + sa * 0.1, top.y - 0.2, top.z + sa * 0.5 - ca * 0.1), up, up, up, W);
          }
          break;
        }
        default: blob(V(0, 0.66, 0), V(0.44, 0.4, 0.44));
      }
      const g = mergeLeaf(tk, lf);
      _vegGeo[key] = g;
      return g;
    }
    switch (kind) {
      case 'broad': trunk(0.42); crown(V(0, 0.66, 0), V(0.44, 0.4, 0.44), W, 3); break;
      case 'broad2':
        trunk(0.4);
        crown(V(0.06, 0.6, 0), V(0.38, 0.34, 0.38), W, 5);
        crown(V(-0.15, 0.8, 0.06), V(0.28, 0.25, 0.28), W2, 9);
        break;
      case 'pine': trunk(0.3); tier(0.2, 0.44, 0.62, W2); tier(0.55, 0.33, 0.56, W); break;
      case 'spruce': trunk(0.2, 0.05); tier(0.12, 0.34, 0.5, W2); tier(0.42, 0.27, 0.46, W); tier(0.72, 0.19, 0.42, W); break;
      case 'cypress': trunk(0.14, 0.045); lf.lathe(V(0, 0.1, 0), [[0, 0], [0.15, 0.12], [0.19, 0.42], [0.13, 0.85], [0.05, 1.1], [0, 1.2]], hero ? 16 : 8, W); break;
      case 'poplar': trunk(0.24, 0.05); crown(V(0, 0.78, 0), V(0.17, 0.58, 0.17), W, 7, 0.06); break;
      case 'birch': trunk(0.5, 0.045, BIRCH_BARK); crown(V(0, 0.82, 0), V(0.27, 0.42, 0.27), W, 11, 0.12); break;
      case 'shrub': crown(V(0, 0.13, 0), V(0.3, 0.2, 0.3), W, 17, 0.14); break;
      case 'willow':
        trunk(0.4, 0.07);
        crown(V(0, 0.72, 0), V(0.42, 0.34, 0.42), W, 19);
        lf.lathe(V(0, 0.18, 0), [[0.36, 0], [0.45, 0.25], [0.44, 0.5, null, true]], cs, W2);
        break;
      case 'olive':
        tk.tube([V(0, 0, 0), V(0.05, 0.2, 0), V(-0.03, 0.38, 0.02)], [0.06, 0.05, 0.04], ts, TRUNK);
        crown(V(-0.05, 0.52, 0), V(0.34, 0.24, 0.32), W, 23, 0.16);
        crown(V(0.14, 0.44, 0.1), V(0.2, 0.16, 0.2), W2, 29, 0.12);
        break;
      case 'stonepine':
        tk.tube([V(0, 0, 0), V(0.04, 0.4, 0), V(0.02, 0.8, 0)], [0.06, 0.05, 0.04], ts, TRUNK);
        crown(V(0.02, 0.92, 0), V(0.48, 0.2, 0.48), W, 31, 0.12);
        break;
      case 'acacia':
        tk.tube([V(0, 0, 0), V(0.04, 0.3, 0), V(0.16, 0.55, 0.04)], [0.05, 0.04, 0.03], ts, TRUNK);
        tk.tube([V(0.04, 0.3, 0), V(-0.12, 0.56, -0.04)], [0.03, 0.025], ts, TRUNK);
        crown(V(0.02, 0.66, 0), V(0.46, 0.18, 0.42), W, 37, 0.16);
        crown(V(0.2, 0.62, 0.12), V(0.26, 0.13, 0.24), W2, 39, 0.12);
        break;
      case 'bamboo':
        for (let k = 0; k < 5; k++) {
          const a = k * 2.4, r = k ? 0.1 : 0, x = Math.cos(a) * r, z = Math.sin(a) * r, h = 0.8 + (k % 3) * 0.15;
          tk.lathe(V(x, 0, z), [[0.022, 0], [0.016, h]], hero ? 6 : 4, new THREE.Color(0.48, 0.6, 0.3));
          lf.sphere(V(x * 1.6, h - 0.08, z * 1.6), V(0.14, 0.26, 0.14), k % 2 ? W : W2, hero ? 1 : 0, { seed: 41 + k, rough: 0.1, bottomShade: 0.3 });
        }
        break;
      case 'palm': {
        const pts = [V(0, 0, 0), V(0.05, 0.3, 0), V(0.13, 0.62, 0), V(0.24, 0.92, 0)];
        tk.tube(pts, [0.055, 0.047, 0.04, 0.035], ts, new THREE.Color(0.5, 0.4, 0.27));
        const top = pts[3], nf = hero ? 9 : 7, up = V(0, 1, 0);
        for (let k = 0; k < nf; k++) {
          const a = k * Math.PI * 2 / nf + 0.3, ca = Math.cos(a), sa = Math.sin(a);
          const seg = hero ? 5 : 3;
          let prevL = null, prevR = null, prevC = top;
          for (let s = 1; s <= seg; s++) {
            const u = s / seg, L = 0.56 * u, droop = 0.12 * u - 0.38 * u * u, w = 0.12 * Math.sin(Math.PI * Math.min(1, u * 1.15));
            const c = V(top.x + ca * L, top.y + droop, top.z + sa * L);
            const l = V(c.x - sa * w, c.y - 0.02, c.z + ca * w), r = V(c.x + sa * w, c.y - 0.02, c.z - ca * w);
            const n = V(ca * 0.25, 1, sa * 0.25).normalize();
            const col = k % 2 ? W : W2;
            if (!prevL) { lf.triN(prevC, l, c, up, n, n, col); lf.triN(prevC, c, r, up, n, n, col); }
            else { lf.triN(prevL, l, c, n, n, n, col); lf.triN(prevL, c, prevC, n, n, n, col); lf.triN(prevC, c, r, n, n, n, col); lf.triN(prevC, r, prevR, n, n, n, col); }
            prevL = l; prevR = r; prevC = c;
          }
        }
        crown(V(top.x, top.y - 0.03, 0), V(0.07, 0.06, 0.07), W2, 3, 0);
        break;
      }
    }
    const g = mergeLeaf(tk, lf);
    _vegGeo[key] = g;
    return g;
  }
  // 石块（三种外形）：整体乘实例颜色
  function rockGeometry(v, lod) {
    const key = 'rock' + v + ':' + lod;
    if (_vegGeo[key]) return _vegGeo[key];
    const mb = new SG.MeshBuilder();
    const g = new THREE.Color(0.92, 0.92, 0.92);
    const R = [V(0.34, 0.22, 0.28), V(0.26, 0.3, 0.24), V(0.42, 0.15, 0.3)][v];
    mb.sphere(V(0, R.y * 0.55, 0), R, g, lod ? 0 : 1, { seed: v * 13 + 5, rough: 0.24, flat: -0.45, top: 0.1, bottomShade: 0.4 });
    if (!lod && v === 0) mb.sphere(V(0.27, 0.07, 0.12), V(0.14, 0.1, 0.12), g, 1, { seed: 77, rough: 0.2, flat: -0.4, bottomShade: 0.4 });
    const geo = mergeLeaf(null, mb);
    _vegGeo[key] = geo;
    return geo;
  }
  // 草丛：七片弯曲的草叶（两面），叶根暗、叶尖亮；野花：花茎 + 五瓣花（花瓣乘实例颜色）
  function tuftGeometry() {
    if (_vegGeo.tuft) return _vegGeo.tuft;
    const mb = new SG.MeshBuilder();
    const rnd = SG.SeededRandom(91);
    const dark = new THREE.Color(0.5, 0.5, 0.5), lite = new THREE.Color(1.06, 1.06, 1.0);
    for (let k = 0; k < 6; k++) {
      const a = k * 1.05 + rnd.nextDouble() * 0.6, ca = Math.cos(a), sa = Math.sin(a);
      const r0 = 0.02 + rnd.nextDouble() * 0.05, h = 0.13 + rnd.nextDouble() * 0.12, lean = 0.05 + rnd.nextDouble() * 0.07, w = 0.026;
      const bx = ca * r0, bz = sa * r0;
      const b0 = V(bx - sa * w, 0, bz + ca * w), b1 = V(bx + sa * w, 0, bz - ca * w);
      const tip = V(bx + ca * lean, h, bz + sa * lean);
      const nf = V(ca * 0.3, 1, sa * 0.3).normalize(), nb = V(-ca * 0.3, 1, -sa * 0.3).normalize();
      mb.triN(b0, b1, tip, nf, nf, nf, dark, dark, lite);
      mb.triN(b0, b1, tip, nb, nb, nb, dark, dark, lite);
    }
    const g = mergeLeaf(null, mb);
    _vegGeo.tuft = g;
    return g;
  }
  function flowerGeometry() {
    if (_vegGeo.flower) return _vegGeo.flower;
    const stem = new SG.MeshBuilder(), pet = new SG.MeshBuilder(), up = V(0, 1, 0);
    const green = new THREE.Color(0.3, 0.5, 0.2);
    for (const [x, z, h] of [[0, 0, 0.17], [0.07, 0.04, 0.13], [-0.05, 0.06, 0.15]]) {
      stem.triN(V(x - 0.01, 0, z), V(x + 0.01, 0, z), V(x, h, z), V(0, 0.3, 1), V(0, 0.3, 1), up, green);
      stem.triN(V(x + 0.01, 0, z), V(x - 0.01, 0, z), V(x, h, z), V(0, 0.3, -1), V(0, 0.3, -1), up, green);
      const c = V(x, h, z), r = 0.035;
      for (let k = 0; k < 5; k++) {
        const a0 = k * Math.PI * 2 / 5, a1 = a0 + Math.PI * 2 / 5;
        pet.triN(c, V(x + Math.cos(a0) * r, h + 0.006, z + Math.sin(a0) * r), V(x + Math.cos(a1) * r, h + 0.006, z + Math.sin(a1) * r), up, up, up, new THREE.Color(1, 1, 1));
      }
      pet.triN(V(x, h + 0.012, z), V(x + 0.012, h + 0.01, z), V(x, h + 0.01, z + 0.012), up, up, up, new THREE.Color(1.0, 0.9, 0.4));
    }
    const g = mergeLeaf(stem, pet);
    _vegGeo.flower = g;
    return g;
  }
  // ---------------------------------------------------------- 远景树：公告板 --
  // 远处的树画成朝向镜头的贴图面片（2 个三角形）：图集在 Canvas 中程序绘制（各树种的剪影、受光与叶簇斑点），
  // 灰度树冠乘实例颜色；alpha 测试 + alphaToCoverage（多重采样时边缘平滑）。一个区块的远景树（各树种）只要一次绘制。
  const SPRITE_KINDS = ['broad', 'broad2', 'pine', 'spruce', 'cypress', 'poplar', 'birch', 'shrub', 'willow', 'olive', 'stonepine', 'acacia', 'bamboo', 'palm'];
  // 面片宽、高（与 lod 0 模型的外形一致，单位 = 实例缩放 1）
  const SPRITE_SIZE = { broad: [1.0, 1.12], broad2: [1.0, 1.12], pine: [0.95, 1.3], spruce: [0.75, 1.3], cypress: [0.44, 1.38], poplar: [0.42, 1.36], birch: [0.62, 1.28],
    shrub: [0.66, 0.36], willow: [1.0, 1.12], olive: [0.8, 0.82], stonepine: [1.04, 1.14], acacia: [1.0, 0.86], bamboo: [0.62, 1.1], palm: [1.2, 1.1] };
  let _spriteTex = null, _spriteMat = null, _spriteGeo = null;
  function spriteTexture() {
    if (_spriteTex) return _spriteTex;
    const C = 128, cols = 4, rows = 4, cv = document.createElement('canvas');
    cv.width = C * cols; cv.height = C * rows;
    const g = cv.getContext('2d'), rnd = SG.SeededRandom(321);
    // 一团树冠：径向渐变（左上受光、右下背光）+ 叶簇斑点
    const puff = (x, y, rx, ry, k) => {
      const gr = g.createRadialGradient(x - rx * 0.35, y - ry * 0.4, Math.min(rx, ry) * 0.1, x, y, Math.max(rx, ry) * 1.05);
      gr.addColorStop(0, 'rgb(' + [248, 248, 244].map(v => Math.round(v * k)).join(',') + ')');
      gr.addColorStop(0.55, 'rgb(' + [196, 198, 190].map(v => Math.round(v * k)).join(',') + ')');
      gr.addColorStop(1, 'rgb(' + [112, 116, 108].map(v => Math.round(v * k)).join(',') + ')');
      g.fillStyle = gr;
      g.beginPath(); g.ellipse(x, y, rx, ry, 0, 0, Math.PI * 2); g.fill();
    };
    const crown = (cx, cy, rx, ry, n) => {
      for (let i = 0; i < n; i++) {
        const a = rnd.nextDouble() * Math.PI * 2, d = Math.sqrt(rnd.nextDouble()) * 0.45;
        puff(cx + Math.cos(a) * rx * d, cy + Math.sin(a) * ry * d, rx * (0.48 + rnd.nextDouble() * 0.2), ry * (0.48 + rnd.nextDouble() * 0.2), 0.9 + rnd.nextDouble() * 0.1);
      }
      puff(cx, cy, rx * 0.78, ry * 0.8, 1);
    };
    const speckle = (x0, y0, x1, y1, n) => {
      g.save(); g.globalCompositeOperation = 'source-atop';
      for (let i = 0; i < n; i++) {
        const x = x0 + rnd.nextDouble() * (x1 - x0), y = y0 + rnd.nextDouble() * (y1 - y0), r = 1 + rnd.nextDouble() * 2.5;
        g.fillStyle = rnd.nextDouble() < 0.5 ? 'rgba(60,64,58,0.28)' : 'rgba(255,255,250,0.22)';
        g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill();
      }
      g.restore();
    };
    const trunk = (x, y0, y1, w, col) => { g.fillStyle = col || 'rgb(120,92,62)'; g.fillRect(x - w / 2, y1, w, y0 - y1); };
    const tier = (cx, yb, w, h) => {
      const gr = g.createLinearGradient(cx - w / 2, 0, cx + w / 2, 0);
      gr.addColorStop(0, 'rgb(236,238,232)'); gr.addColorStop(0.5, 'rgb(190,194,186)'); gr.addColorStop(1, 'rgb(110,114,106)');
      g.fillStyle = gr;
      g.beginPath(); g.moveTo(cx - w / 2, yb); g.quadraticCurveTo(cx - w * 0.12, yb - h * 0.5, cx, yb - h); g.quadraticCurveTo(cx + w * 0.12, yb - h * 0.5, cx + w / 2, yb);
      g.quadraticCurveTo(cx, yb + h * 0.08, cx - w / 2, yb); g.fill();
    };
    SPRITE_KINDS.forEach((kind, i) => {
      const ox = (i % cols) * C, oy = Math.floor(i / cols) * C;
      g.save(); g.translate(ox, oy);
      g.beginPath(); g.rect(0, 0, C, C); g.clip();
      const W = C, H = C, bx = W / 2, by = H - 2;
      switch (kind) {
        case 'broad': case 'broad2': case 'olive':
          trunk(bx, by, H * 0.45, W * 0.07);
          crown(bx, H * 0.4, W * 0.42, H * 0.34, kind === 'broad2' ? 7 : 5);
          speckle(0, 0, W, H * 0.8, 90);
          break;
        case 'willow':
          trunk(bx, by, H * 0.5, W * 0.08);
          crown(bx, H * 0.38, W * 0.42, H * 0.3, 5);
          g.strokeStyle = 'rgba(150,154,146,0.9)'; g.lineWidth = 2.2;
          for (let k = 0; k < 16; k++) { const x = W * 0.12 + k * W * 0.05; g.beginPath(); g.moveTo(x, H * 0.45); g.lineTo(x + (rnd.nextDouble() - 0.5) * 4, H * (0.66 + rnd.nextDouble() * 0.12)); g.stroke(); }
          speckle(0, 0, W, H * 0.8, 70);
          break;
        case 'pine': trunk(bx, by, H * 0.7, W * 0.07); tier(bx, H * 0.84, W * 0.96, H * 0.5); tier(bx, H * 0.56, W * 0.72, H * 0.52); speckle(0, 0, W, H, 60); break;
        case 'spruce': trunk(bx, by, H * 0.8, W * 0.06); tier(bx, H * 0.9, W * 0.96, H * 0.42); tier(bx, H * 0.66, W * 0.76, H * 0.4); tier(bx, H * 0.44, W * 0.54, H * 0.42); speckle(0, 0, W, H, 50); break;
        case 'cypress': {
          trunk(bx, by, H * 0.85, W * 0.1);
          const gr = g.createLinearGradient(bx - W * 0.4, 0, bx + W * 0.4, 0);
          gr.addColorStop(0, 'rgb(232,234,228)'); gr.addColorStop(0.5, 'rgb(186,190,182)'); gr.addColorStop(1, 'rgb(104,108,100)');
          g.fillStyle = gr; g.beginPath(); g.ellipse(bx, H * 0.5, W * 0.42, H * 0.44, 0, 0, Math.PI * 2); g.fill();
          speckle(0, 0, W, H, 50);
          break;
        }
        case 'poplar': case 'birch':
          trunk(bx, by, H * 0.6, W * (kind === 'birch' ? 0.12 : 0.1), kind === 'birch' ? 'rgb(236,232,222)' : null);
          crown(bx, H * 0.42, W * 0.42, H * 0.38, 5);
          speckle(0, 0, W, H * 0.85, 60);
          break;
        case 'shrub': crown(bx, H * 0.55, W * 0.46, H * 0.4, 6); speckle(0, 0, W, H, 70); break;
        case 'stonepine': case 'acacia':
          g.strokeStyle = 'rgb(120,92,62)'; g.lineWidth = W * 0.05;
          g.beginPath(); g.moveTo(bx, by); g.lineTo(bx + W * 0.03, H * 0.42); g.stroke();
          if (kind === 'acacia') { g.beginPath(); g.moveTo(bx, H * 0.75); g.lineTo(bx - W * 0.2, H * 0.4); g.stroke(); }
          crown(bx, H * (kind === 'acacia' ? 0.3 : 0.24), W * 0.47, H * 0.2, 6);
          speckle(0, 0, W, H * 0.5, 60);
          break;
        case 'bamboo':
          g.strokeStyle = 'rgb(150,180,110)'; g.lineWidth = 2.5;
          for (let k = 0; k < 6; k++) { const x = bx + (k - 2.5) * W * 0.08; g.beginPath(); g.moveTo(x, by); g.quadraticCurveTo(x, H * 0.4, x + (k - 2.5) * 5, H * 0.1); g.stroke(); }
          for (let k = 0; k < 9; k++) puff(bx + (rnd.nextDouble() - 0.5) * W * 0.6, H * (0.15 + rnd.nextDouble() * 0.5), W * 0.12, H * 0.1, 0.95);
          break;
        case 'palm': {
          g.strokeStyle = 'rgb(140,110,76)'; g.lineWidth = W * 0.06;
          g.beginPath(); g.moveTo(bx, by); g.quadraticCurveTo(bx + W * 0.02, H * 0.6, bx + W * 0.12, H * 0.22); g.stroke();
          const tx = bx + W * 0.12, ty = H * 0.22;
          for (let k = 0; k < 9; k++) {
            const a = -Math.PI * 0.95 + k * Math.PI * 0.95 / 4, L = W * 0.46;
            const ex = tx + Math.cos(a) * L, ey = ty + Math.sin(a) * L * 0.5 + L * 0.35;
            g.strokeStyle = k % 2 ? 'rgb(210,214,204)' : 'rgb(170,174,164)'; g.lineWidth = 7;
            g.beginPath(); g.moveTo(tx, ty); g.quadraticCurveTo((tx + ex) / 2, ty - L * 0.15, ex, ey); g.stroke();
          }
          break;
        }
      }
      g.restore();
    });
    const t = new THREE.CanvasTexture(cv);
    t.magFilter = THREE.LinearFilter; t.minFilter = THREE.LinearMipmapLinearFilter; t.generateMipmaps = true;
    t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
    _spriteTex = t;
    return t;
  }
  const SPRITE_VS = `
attribute float aCell;
uniform vec2 uAtlas;
varying vec2 vUv;
varying vec3 vTint;
#include <fog_pars_vertex>
void main() {
  vec3 c = vec3( instanceMatrix[3][0], instanceMatrix[3][1], instanceMatrix[3][2] );
  float sx = length( vec3( instanceMatrix[0][0], instanceMatrix[0][1], instanceMatrix[0][2] ) );
  float sy = length( vec3( instanceMatrix[1][0], instanceMatrix[1][1], instanceMatrix[1][2] ) );
  vec3 R = vec3( viewMatrix[0][0], viewMatrix[1][0], viewMatrix[2][0] );
  vec3 U = normalize( mix( vec3( viewMatrix[0][1], viewMatrix[1][1], viewMatrix[2][1] ), vec3( 0.0, 1.0, 0.0 ), 0.5 ) );
  vec3 p = c + R * position.x * sx + U * position.y * sy;
  vec2 cell = vec2( mod( aCell, uAtlas.x ), floor( aCell / uAtlas.x ) );
  vUv = ( cell + vec2( uv.x, 1.0 - uv.y ) ) / uAtlas;
  vUv.y = 1.0 - vUv.y;
#ifdef USE_INSTANCING_COLOR
  vTint = instanceColor;
#else
  vTint = vec3( 1.0 );
#endif
  vec4 mvPosition = viewMatrix * vec4( p, 1.0 );
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}`;
  const SPRITE_FS = `
uniform sampler2D uMap;
uniform vec3 uLight;
varying vec2 vUv;
varying vec3 vTint;
#include <common>
#include <fog_pars_fragment>
void main() {
  vec4 t = texture2D( uMap, vUv );
  if ( t.a < 0.42 ) discard;
  gl_FragColor = vec4( t.rgb * vTint * uLight, t.a );
  #include <fog_fragment>
}`;
  function spriteMaterial() {
    if (_spriteMat) return _spriteMat;
    _spriteMat = new THREE.ShaderMaterial({
      uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uAtlas: { value: new THREE.Vector2(4, 4) }, uLight: { value: new THREE.Color(1.02, 1.0, 0.94) } }]),
      vertexShader: SPRITE_VS, fragmentShader: SPRITE_FS, fog: true, alphaToCoverage: true,
    });
    _spriteMat.uniforms.uMap = { value: spriteTexture() };
    _spriteMat.name = 'TreeSprites';
    return _spriteMat;
  }
  function spriteGeometry() {
    if (_spriteGeo) return _spriteGeo;
    const g = new THREE.PlaneGeometry(1, 1);
    g.translate(0, 0.5, 0);
    _spriteGeo = g;
    return g;
  }

  // 村舍（屋顶 / 帐顶乘实例颜色）：han 白墙灰瓦 | flat 平顶土屋 | tile 红瓦坡顶 | round 圆形茅屋 | yurt 毡帐 | stilt 高脚屋 | long 长屋
  function houseGeometry(style) {
    const key = 'house:' + style;
    if (_vegGeo[key]) return _vegGeo[key];
    const wall = new SG.MeshBuilder(), roof = new SG.MeshBuilder();
    const W = new THREE.Color(0.95, 0.95, 0.95);
    const gableRoof = (w, d, y, h, e) => {
      const hw = w / 2 + e, hd = d / 2 + e, r0 = V(-hw, y + h, 0), r1 = V(hw, y + h, 0);
      const s0 = V(-hw, y - 0.01, -hd), s1 = V(hw, y - 0.01, -hd), n0 = V(-hw, y - 0.01, hd), n1 = V(hw, y - 0.01, hd);
      roof.quad(s0, r0, r1, s1, W); roof.quad(n1, r1, r0, n0, new THREE.Color(0.8, 0.8, 0.8));
      wall.tri(V(-w / 2, y, d / 2), V(-w / 2, y + h * 0.92, 0), V(-w / 2, y, -d / 2), new THREE.Color(0.86, 0.82, 0.74));
      wall.tri(V(w / 2, y, -d / 2), V(w / 2, y + h * 0.92, 0), V(w / 2, y, d / 2), new THREE.Color(0.86, 0.82, 0.74));
    };
    const door = (w, h, d, c) => wall.box(V(0, h / 2, -d / 2 - 0.004), V(w, h, 0.01), c || new THREE.Color(0.25, 0.18, 0.12));
    switch (style) {
      case 'han':
        wall.box(V(0, 0.1, 0), V(0.34, 0.2, 0.24), new THREE.Color(0.9, 0.87, 0.8));
        door(0.07, 0.12, 0.24);
        gableRoof(0.34, 0.24, 0.2, 0.13, 0.04);
        roof.box(V(0, 0.335, 0), V(0.44, 0.025, 0.03), new THREE.Color(0.7, 0.7, 0.7));
        break;
      case 'tile':
        wall.box(V(0, 0.1, 0), V(0.32, 0.2, 0.24), new THREE.Color(0.93, 0.89, 0.8));
        door(0.07, 0.12, 0.24);
        gableRoof(0.32, 0.24, 0.2, 0.09, 0.03);
        break;
      case 'flat':
        wall.box(V(0, 0.09, 0), V(0.3, 0.18, 0.26), new THREE.Color(0.86, 0.76, 0.58));
        door(0.07, 0.11, 0.26);
        roof.box(V(0, 0.19, 0), V(0.32, 0.03, 0.28), W);
        wall.box(V(0.08, 0.25, 0.05), V(0.12, 0.1, 0.12), new THREE.Color(0.82, 0.72, 0.55));
        break;
      case 'round':
        wall.lathe(V(0, 0, 0), [[0.15, 0], [0.15, 0.11]], 9, new THREE.Color(0.72, 0.62, 0.46));
        roof.lathe(V(0, 0.1, 0), [[0, -0.01], [0.2, 0, null, true], [0.08, 0.13], [0, 0.2]], 9, W);
        door(0.06, 0.09, 0.3);
        break;
      case 'yurt':
        wall.lathe(V(0, 0, 0), [[0.16, 0], [0.16, 0.1]], 10, new THREE.Color(0.92, 0.9, 0.84));
        roof.lathe(V(0, 0.1, 0), [[0.165, 0, null, true], [0.12, 0.06], [0.04, 0.1], [0, 0.105]], 10, W);
        door(0.06, 0.08, 0.32, new THREE.Color(0.62, 0.25, 0.18));
        break;
      case 'stilt':
        for (const sx of [-1, 1]) for (const sz of [-1, 1]) wall.box(V(sx * 0.12, 0.06, sz * 0.09), V(0.025, 0.12, 0.025), new THREE.Color(0.4, 0.3, 0.2));
        wall.box(V(0, 0.18, 0), V(0.3, 0.12, 0.22), new THREE.Color(0.66, 0.52, 0.34));
        gableRoof(0.3, 0.22, 0.24, 0.16, 0.05);
        break;
      case 'long':
        wall.box(V(0, 0.06, 0), V(0.5, 0.12, 0.2), new THREE.Color(0.62, 0.5, 0.36));
        gableRoof(0.5, 0.2, 0.12, 0.17, 0.04);
        break;
    }
    const g = mergeLeaf(wall, roof);
    _vegGeo[key] = g;
    return g;
  }

  // ===================================================== 第四版：植被与小物件（SG.LPProps）==
  // 树、灌丛、石块、村舍、草丛都取自共用道具库 SG.LPProps（Lanternvale 风格：软团树冠上暖下青、平面着色的树干与石块、
  // 墨线），按区块实例化（LP.instances：每种几何体一次绘制 + 一次实例化墨线）。镜头近处（VEG_NEAR 以内）用道具库的远景细节
  // （树约 150–560 三角形，带墨线、投影），更远处换成「冒名顶替」的公告板：开局时把每种树用同一套 LP 材质（含墨线）
  // 在正午光下渲染进一张图集（VEG_KINDS 顺序，8×4 格），公告板着色器再按当前氛围（太阳 / 天光、夜色调、雾）调整明暗——
  // 远近两级的外形与配色一致，不再是圆球或手绘的泡泡树。道具库不可用时退回第三版的模型。
  // 尺寸：地图上的树高约 1 单位（城池尺寸约 1–1.5），按道具库近景模型（种子 0）的高度缩放；墨线宽度不随缩放变化（屏幕像素）。
  const VEG = {
    huai: { r: 'tree_huai', h: 1.24, v: 1 }, elm: { r: 'tree_elm', h: 1.32, v: 1 }, oak: { r: 'tree_oak', h: 1.16, v: 1 },
    pine: { r: 'tree_pine', h: 1.36, v: 1 }, spruce: { r: 'tree_spruce', h: 1.36, v: 1 }, cedar: { r: 'tree_cedar', h: 1.3, v: 1 },
    cypress: { r: 'tree_cypress', h: 1.4, v: 1 }, poplar: { r: 'tree_poplar', h: 1.5, v: 1 }, birch: { r: 'tree_birch', h: 1.3, v: 1 },
    willow: { r: 'tree_willow', h: 1.18, v: 1 }, olive: { r: 'tree_olive', h: 0.95, v: 1 }, umbrella: { r: 'tree_pine', h: 1.36, v: 1, sx: 1.12, sy: 0.75 },
    acacia: { r: 'tree_acacia', h: 1.02, v: 1 }, bamboo: { r: 'tree_poplar', h: 1.0, v: 1, sx: 1.4 }, date: { r: 'palm_date', h: 1.15, v: 1 },
    coconut: { r: 'palm_coconut', h: 1.15, v: 1 }, maple: { r: 'tree_autumn', o: { variant: 'maple' }, h: 1.2, v: 1 },
    blossom: { r: 'tree_blossom', o: { variant: 'peach' }, h: 0.98, v: 1 }, snowpine: { r: 'pine_snow', h: 1.32, v: 1 },
    baobab: { r: 'tree_baobab', h: 1.15, v: 1 }, mangrove: { r: 'tree_mangrove', h: 0.98, v: 1 }, bush: { r: 'bush', h: 0.36, v: 2 },
  };
  // 修订：地图比例下，黄山松（云片）远看像苏铁、竹丛像竿上的棒棒糖——地中海的伞松改用压扁的松（sx / sy：宽、高的额外缩放），
  // 竹丛改用加宽的杨树团（一簇浓密的竹林）
  const VEG_KINDS = Object.keys(VEG);
  const VEG_NEAR = 28;            // 镜头到树的距离 < VEG_NEAR：3D 模型；否则公告板（第三轮：34 → 28，特写视角的帧时间）
  const VEG_WIND = 0.3;           // 风摆（世界单位的摆幅随缩放变小）
  function LP_HASH(x, z) { const v = Math.sin(x * 12.9898 + z * 78.233) * 43758.5453; return v - Math.floor(v); }
  // 群落色 c 相对树种常用色 ref 的实例调色（乘在道具原色上）：只调亮度（−12% … +4%：北方的树不提亮成黄绿）外加一点冷暖（红、蓝反向，绿不动）——
  // 蓝永远不会比绿抬得多（逐通道的比值会把北方阔叶的树冠洗成土黄 / 秋橙）
  const _lum = v => 0.3 * v[0] + 0.59 * v[1] + 0.11 * v[2];
  function lpTint(c, ref) {
    const kk = M.clamp(_lum(c) / Math.max(1e-3, _lum(ref)), 0.88, 1.04);
    const w = M.clamp(c[0] / Math.max(1e-3, c[1]) - ref[0] / Math.max(1e-3, ref[1]), -0.12, 0.12);
    return [kk * (1 + 0.3 * w), kk, kk * (1 - 0.3 * w)];
  }
  function vegReady() { return !!(SG.LP && SG.LP.instances && SG.LPProps && SG.LPProps.geometry && SG.LPProps.has && SG.LPProps.has('tree_huai')); }
  function vegScale(kind) {
    const d = VEG[kind];
    if (d.s) return d.s;
    const g = SG.LPProps.geometry(d.r, Object.assign({ seed: 0, lod: 'near' }, d.o || {}));
    d.s = d.h / Math.max(0.1, g.box.max.y);
    return d.s;
  }
  // 道具库几何体（缓存于道具库，共用，勿释放）。lod 0 = 道具库近景，1 = 远景
  function vegGeo(kind, variant, lod) {
    const d = VEG[kind];
    const r = SG.LPProps.geometry(d.r, Object.assign({ seed: variant, lod: lod ? 'far' : 'near' }, d.o || {}));
    return r.ink || r.plain;
  }
  // 逐实例调色的 LP 材质。three r158 只在顶点着色器里定义 USE_INSTANCING_COLOR，LP 的片元着色器因此读不到实例色
  // （SG.LP.instances 的 tints 不起作用，见报告 INTEGRATION）；这里用同一份 LP 着色器、同一组全局 uniform 另建材质，
  // 并在 defines 里补上该宏——只给一定带 instanceColor 的实例化网格用
  const _tintMats = {};
  function lpTintMaterial(wind) {
    const key = 'w' + wind;
    if (_tintMats[key]) return _tintMats[key];
    const LP = SG.LP, base = LP.material({ windScale: wind });
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.lights, THREE.UniformsLib.fog, {
      uTint: { value: new THREE.Vector3(1, 1, 1) }, uFlash: { value: new THREE.Vector4(1, 1, 1, 0) }, uRimBoost: { value: 0 }, uFogScale: { value: 1 },
      uWindScale: { value: wind }, uFade: { value: 1 }, uLit: { value: -1 },
    }]);
    for (const k in LP.globals) u[k] = LP.globals[k];
    const m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: base.vertexShader, fragmentShader: base.fragmentShader, lights: true, fog: true, vertexColors: true });
    m.defines = Object.assign({}, base.defines || {}, { USE_INSTANCING_COLOR: '' });
    m.name = 'LP LowPoly (tinted instances)';
    m.userData.lpKind = 'lit';
    _tintMats[key] = m;
    return m;
  }
  // 修订：区块合并远景的材质（LP 光照；实例调色已烘进顶点色）。aHide = 1 的顶点（该实例此刻用近景实例化模型）挪出裁剪空间
  const _farMats = {};
  function lpFarMaterial(wind) {
    const key = 'f' + wind;
    if (_farMats[key] !== undefined) return _farMats[key];
    const LP = SG.LP, base = LP.material({ windScale: wind });
    const anchor = 'gl_Position = projectionMatrix * mvPosition;';
    if (base.vertexShader.indexOf(anchor) < 0) return (_farMats[key] = null);
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.lights, THREE.UniformsLib.fog, {
      uTint: { value: new THREE.Vector3(1, 1, 1) }, uFlash: { value: new THREE.Vector4(1, 1, 1, 0) }, uRimBoost: { value: 0 }, uFogScale: { value: 1 },
      uWindScale: { value: wind }, uFade: { value: 1 }, uLit: { value: -1 },
    }]);
    for (const k in LP.globals) u[k] = LP.globals[k];
    const vs = 'attribute float aHide;\n' + base.vertexShader.replace(anchor, anchor + '\n  if ( aHide > 0.5 ) gl_Position = vec4( 0.0, 0.0, 2.0, 1.0 );');
    const m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: vs, fragmentShader: base.fragmentShader, lights: true, fog: true, vertexColors: true });
    m.defines = Object.assign({}, base.defines || {});
    m.name = 'LP LowPoly (merged far)';
    m.userData.lpKind = 'lit';
    return (_farMats[key] = m);
  }
  // LP.mergeGeometries 能合并的几何体（非索引、无 uv / 蒙皮、带 lpSmooth）
  const lpMergeable = g => !!(g && g.attributes.position && g.attributes.lpSmooth && !g.attributes.uv && !g.attributes.skinIndex && !g.index);
  // LP 实例化网格（+ 实例化墨线）；count 由调用方设置（setInstCount 同步墨线）
  function lpInst(geo, n, o) {
    o = o || {};
    const wind = o.wind != null ? o.wind : VEG_WIND;
    const im = SG.LP.instances(geo, Math.max(1, n), {
      material: o.tint === false ? null : lpTintMaterial(wind),
      tints: true, outline: o.outline !== false, castShadow: o.shadow !== false, receiveShadow: true, windScale: o.wind != null ? o.wind : VEG_WIND,
      dynamic: !!o.dynamic, outlineOpts: { windScale: o.wind != null ? o.wind : VEG_WIND, width: o.ink || 2.0 },
    });
    im.userData.sharedGeometry = true;
    if (im.userData.outline) im.userData.outline.userData.sharedGeometry = true;
    im.count = 0;
    if (im.userData.outline) im.userData.outline.count = 0;
    return im;
  }
  function setInstCount(im, n) {
    im.count = n;
    const o = im.userData.outline;
    if (o) o.count = n;
  }
  function setInstBounds(im, sphere) {
    im.boundingSphere = sphere;
    if (im.userData.outline) im.userData.outline.boundingSphere = sphere;
  }

  // ---------------------------------------------------------- 冒名顶替的公告板 --
  // 图集 1024×512（8×4 格，每格 128²），每种树一格：正午的 LP 光照 + 墨线，从前上方 32° 俯视渲染；cells[kind] = { i, w, h, base }
  // （w、h：公告板的宽高，单位 = 道具库的米；base：模型原点（树根）在格内的高度比例）
  let _imp = null;
  const IMP_C = 128, IMP_COLS = 8, IMP_ROWS = 4, IMP_PITCH = 32;
  function vegImpostors() {
    if (!_imp) { const it = vegImpostorsGen(); while (!it.next().done) { /* 同步跑完 */ } }
    return _imp;
  }
  // 分步版（修订：每渲染一格让出一次，地图的分片构建不再因整张图集卡一两百毫秒）
  // 烘焙图集用的场景（正午的太阳 + 天光、正交镜头）
  let _impBake = null;
  function impBake() {
    if (_impBake) return _impBake;
    const LP = SG.LP;
    const scene = new THREE.Scene();
    const noon = LP.moodAt(12.5), lin = LP.Paint.lin;
    const sl = lin(noon.sunCol), kl = lin(noon.sky), gl = lin(noon.ground);
    const sun = new THREE.DirectionalLight(new THREE.Color(sl[0], sl[1], sl[2]), noon.sunI);
    sun.position.set(-0.45, 0.8, 0.55);
    scene.add(sun);
    const hemi = new THREE.HemisphereLight(new THREE.Color(kl[0], kl[1], kl[2]), new THREE.Color(gl[0], gl[1], gl[2]), noon.ambI);
    scene.add(hemi);
    const cam = new THREE.OrthographicCamera(-1, 1, 1, -1, 0.1, 200);
    const pr = IMP_PITCH * Math.PI / 180, dir = new THREE.Vector3(0, Math.sin(pr), Math.cos(pr));
    cam.position.copy(dir).multiplyScalar(60);
    cam.up.set(0, 1, 0);
    cam.lookAt(0, 0, 0);
    cam.updateMatrixWorld();
    _impBake = { scene, cam, noon, sl, kl, pr };
    return _impBake;
  }
  // 修订：烘焙场景的灯光组合与地图不同，第一次渲染要编译一份新的着色器程序（负载下一两百毫秒，卡在一个分片里）。
  // 植被一开始就把它交给 GPU 进程编译（renderer.compile 不等结果），等到烘焙时早已编好
  function vegImpostorPrecompile() {
    if (_imp || !SG.Gfx || !SG.Gfx.renderer || !SG.LP) return;
    const LP = SG.LP, b = impBake(), F = new LP.MeshBuilder(1);
    F.color = [0.5, 0.6, 0.3];
    F.box([0, 0.5, 0], [1, 1, 1]);
    const mesh = LP.mesh(F.toGeometry(), { castShadow: false, receiveShadow: false, outline: true });
    b.scene.add(mesh);
    try { SG.Gfx.renderer.compile(b.scene, b.cam); } catch (e) { /* 编译失败时照常在烘焙时编 */ }
    b.scene.remove(mesh);
    mesh.geometry.dispose();
  }
  function* vegImpostorsGen() {
    if (_imp) return _imp;
    const LP = SG.LP, r = SG.Gfx.renderer;
    const rt = new THREE.WebGLRenderTarget(IMP_C * IMP_COLS, IMP_C * IMP_ROWS, { depthBuffer: true, stencilBuffer: false });
    rt.texture.generateMipmaps = true;
    rt.texture.minFilter = THREE.LinearMipmapLinearFilter;
    rt.texture.magFilter = THREE.LinearFilter;
    rt.texture.name = 'VegImpostors';
    const { scene, cam, noon, sl, kl, pr } = impBake();
    const R = new THREE.Vector3(1, 0, 0), U = new THREE.Vector3(0, Math.cos(pr), -Math.sin(pr));
    const prev = { rt: r.getRenderTarget(), auto: r.autoClear, col: r.getClearColor(new THREE.Color()), a: r.getClearAlpha(), shadow: r.shadowMap.enabled };
    r.setRenderTarget(rt);
    r.setClearColor(0x000000, 0);
    r.clear(true, true, true);
    r.autoClear = false;
    const cells = {};
    const corner = new THREE.Vector3();
    // 图集里也描墨线（按 128 像素的格算线宽：公告板缩到四五十像素时只剩一圈淡淡的暗边，与近处 3D 树的墨线接得上）
    const vw = LP.globals.lpView.value, saveView = vw.clone();
    vw.set(IMP_C, IMP_C, 0.62, 0.9);
    const restore = () => { r.setRenderTarget(prev.rt); r.autoClear = prev.auto; r.setClearColor(prev.col, prev.a); };
    for (let i = 0; i < VEG_KINDS.length; i++) {
      const kind = VEG_KINDS[i];
      const g = vegGeo(kind, 0, 1);
      const b = g.boundingBox || (g.computeBoundingBox(), g.boundingBox);
      let x0 = Infinity, x1 = -Infinity, y0 = Infinity, y1 = -Infinity;
      for (let k = 0; k < 8; k++) {
        corner.set(k & 1 ? b.max.x : b.min.x, k & 2 ? b.max.y : b.min.y, k & 4 ? b.max.z : b.min.z);
        const px = corner.dot(R), py = corner.dot(U);
        x0 = Math.min(x0, px); x1 = Math.max(x1, px); y0 = Math.min(y0, py); y1 = Math.max(y1, py);
      }
      // 正方形取景（格是正方形），留一点边给墨线
      const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, half = Math.max(x1 - x0, y1 - y0) * 0.5 * 1.06;
      cam.left = cx - half; cam.right = cx + half; cam.bottom = cy - half; cam.top = cy + half;
      cam.updateProjectionMatrix();
      const col = i % IMP_COLS, row = Math.floor(i / IMP_COLS);
      rt.viewport.set(col * IMP_C, row * IMP_C, IMP_C, IMP_C);
      rt.scissor.set(col * IMP_C, row * IMP_C, IMP_C, IMP_C);
      rt.scissorTest = true;
      r.setRenderTarget(rt);
      const mesh = LP.mesh(g, { castShadow: false, receiveShadow: false, outline: true });
      scene.add(mesh);
      vw.set(IMP_C, IMP_C, 0.62, 0.9);
      r.render(scene, cam);
      scene.remove(mesh);
      vw.copy(saveView);
      cells[kind] = { i, w: half * 2, h: half * 2, base: (0 - (cy - half)) / (half * 2), ox: (0 - (cx - half)) / (half * 2) - 0.5 };
      // 让出（其间别的画面会渲染：先恢复渲染目标与清屏设置，回来再接着画）
      rt.scissorTest = false;
      restore();
      yield;
      if (_imp) return _imp;
      r.autoClear = false;
    }
    vw.copy(saveView);
    rt.scissorTest = false;
    rt.viewport.set(0, 0, rt.width, rt.height);
    r.setRenderTarget(prev.rt);
    r.autoClear = prev.auto;
    r.setClearColor(prev.col, prev.a);
    const ref = [sl[0] * noon.sunI, sl[1] * noon.sunI, sl[2] * noon.sunI, kl[0] * noon.ambI, kl[1] * noon.ambI, kl[2] * noon.ambI];
    _imp = { rt, tex: rt.texture, cells, ref };
    return _imp;
  }
  const IMP_VS = `
attribute float aCell;
uniform vec2 uAtlas;
uniform float uBase[32];
uniform float uOx[32];
varying vec2 vUv;
varying vec3 vTint;
varying float vDist;
#include <fog_pars_vertex>
void main() {
  vec3 c = vec3( instanceMatrix[3][0], instanceMatrix[3][1], instanceMatrix[3][2] );
  float sx = length( vec3( instanceMatrix[0][0], instanceMatrix[0][1], instanceMatrix[0][2] ) );
  float sy = length( vec3( instanceMatrix[1][0], instanceMatrix[1][1], instanceMatrix[1][2] ) );
  vec3 R = vec3( viewMatrix[0][0], viewMatrix[1][0], viewMatrix[2][0] );
  // 图集是从前上方 32° 渲染的：公告板正对镜头（不再半立），画面不被二次压扁
  vec3 U = vec3( viewMatrix[0][1], viewMatrix[1][1], viewMatrix[2][1] );
  int ci = int( aCell + 0.5 );
  vec3 p = c + R * ( position.x - uOx[ ci ] ) * sx + U * ( position.y - uBase[ ci ] ) * sy;
  vec2 cell = vec2( mod( aCell, uAtlas.x ), floor( aCell / uAtlas.x ) );
  vUv = ( cell + uv ) / uAtlas;
#ifdef USE_INSTANCING_COLOR
  vTint = instanceColor;
#else
  vTint = vec3( 1.0 );
#endif
  vec4 mvPosition = viewMatrix * vec4( p, 1.0 );
  vDist = length( mvPosition.xyz );
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}`;
  const IMP_FS = `
uniform sampler2D uMap;
uniform vec4 lpSunC;
uniform vec4 lpSkyC;
uniform vec4 lpGrade;
uniform vec4 lpWarmth;
uniform vec4 lpFog;
uniform float lpOut;
uniform vec3 uRefSun;
uniform vec3 uRefSky;
varying vec2 vUv;
varying vec3 vTint;
varying float vDist;
#include <common>
#include <fog_pars_fragment>
vec3 iLin( vec3 c ) { return mix( c / 12.92, pow( ( c + 0.055 ) / 1.055, vec3( 2.4 ) ), step( 0.04045, c ) ); }
vec3 iEnc( vec3 c ) { c = max( c, vec3( 0.0 ) ); return lpOut > 0.5 ? mix( c * 12.92, 1.055 * pow( c, vec3( 1.0 / 2.4 ) ) - 0.055, step( 0.0031308, c ) ) : c; }
void main() {
  vec4 t = texture2D( uMap, vUv );
  if ( t.a < 0.5 ) discard;
  // ×0.86（线性）：代替 3D 树受到的环境遮蔽与自身投影（图集是在无影的正午光下烘的，远处的树原本比近处的亮一截）
  vec3 col = iLin( t.rgb / t.a ) * iLin( vTint ) * 0.86;
  // 图集按正午光烘焙：按当前氛围的太阳 + 天光与正午之比调整（夜色调、黄金时刻同 LP）
  if ( lpSunC.w > 0.5 ) {
    vec3 now = lpSunC.rgb * 0.7 + lpSkyC.rgb, ref = uRefSun * 0.7 + uRefSky;
    col *= now / max( ref, vec3( 0.001 ) );
  }
  if ( lpGrade.x > 0.0 ) col = mix( col, dot( col, vec3( 0.3, 0.59, 0.11 ) ) * lpGrade.yzw, clamp( lpGrade.x, 0.0, 1.0 ) );
  if ( lpWarmth.x > 0.0 ) { float cl = dot( col, vec3( 0.3, 0.59, 0.11 ) ); col = max( cl + ( col - cl ) * ( 1.0 + lpWarmth.x ), 0.0 ); }
  #ifdef USE_FOG
    float fk = clamp( ( vDist - fogNear ) / max( fogFar - fogNear, 0.01 ), 0.0, 1.0 ) * lpFog.x;
    col = mix( col, lpFog.y > 0.5 ? iLin( fogColor ) : fogColor, fk );
  #endif
  gl_FragColor = vec4( iEnc( col ), 1.0 );
}`;
  let _impMat = null, _impGeo = null;
  function impostorMaterial() {
    if (_impMat) return _impMat;
    const imp = vegImpostors(), G = SG.LP.globals;
    const base = new Array(32).fill(0), ox = new Array(32).fill(0);
    for (const k of VEG_KINDS) { const c = imp.cells[k]; base[c.i] = c.base; ox[c.i] = c.ox; }
    _impMat = new THREE.ShaderMaterial({
      uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, {
        uAtlas: { value: new THREE.Vector2(IMP_COLS, IMP_ROWS) }, uBase: { value: base }, uOx: { value: ox },
        uRefSun: { value: new THREE.Vector3(imp.ref[0], imp.ref[1], imp.ref[2]) }, uRefSky: { value: new THREE.Vector3(imp.ref[3], imp.ref[4], imp.ref[5]) },
      }]),
      vertexShader: IMP_VS, fragmentShader: IMP_FS, fog: true, alphaToCoverage: true,
    });
    _impMat.uniforms.uMap = { value: imp.tex };
    for (const k of ['lpSunC', 'lpSkyC', 'lpGrade', 'lpWarmth', 'lpFog', 'lpOut']) _impMat.uniforms[k] = G[k];
    _impMat.name = 'TreeImpostors';
    return _impMat;
  }
  function impostorGeometry() {
    if (_impGeo) return _impGeo;
    const g = new THREE.PlaneGeometry(1, 1);
    g.translate(0, 0.5, 0);
    _impGeo = g;
    return g;
  }

  // 道具库的其他配方（石块、村舍、草垛、水井……）：宽度表（米，种子 0 近景）与几何体（lod 1 时没有远景细节就用近景）
  const HOUSE_LP = { han: 'house_han', tile: 'house_med', flat: 'house_mud', round: 'house_thatch', yurt: 'yurt', stilt: 'house_thatch', long: 'house_thatch' };
  const HOUSE_W = { han: 0.46, tile: 0.44, flat: 0.4, round: 0.4, yurt: 0.36, stilt: 0.42, long: 0.5 };
  const ROCK_W = { rock_small: 1.55, rock_large: 2.07, boulder: 3.48, cliff: 3.29, rock_snow: 2.43 };
  const _propW = {};
  function PROP_W(name) {
    if (_propW[name]) return _propW[name];
    const g = SG.LPProps.geometry(name, { seed: 0 });
    return (_propW[name] = Math.max(0.1, g.box.max.x - g.box.min.x, g.box.max.z - g.box.min.z));
  }
  function propGeo(name, variant, lod) {
    const info = SG.LPProps.info(name);
    const r = SG.LPProps.geometry(name, { seed: variant || 0, lod: lod && info && info.lod ? 'far' : 'near' });
    return r.ink || r.plain;
  }
  // 石块的合并远景：除雪石外一律用 rock_large 的远景细节（40 个三角形，按宽度换算到该种石块），远处一块石头只有几个像素；
  // 合并网格按实例复制顶点，世界地图上千块石头省下约十五万个顶点
  function rockFarGeo(name) {
    if (name === 'rock_snow' || !ROCK_W[name]) return propGeo(name, 0, 1);
    return SG.LP.MeshCache.get('map:rockfar:' + name, () => {
      const g = propGeo('rock_large', 0, 1).clone(), f = ROCK_W[name] / ROCK_W.rock_large;
      g.scale(f, f, f);
      return g;
    });
  }
  // 村舍的远景「小房块」：屋身 + 双坡顶（约 30 个三角形），尺寸取道具库模型的包围盒，颜色取配方的墙色、顶色
  const SPECK = {
    house_han: ['#E6DCC4', '#6F757C', 'gable'], house_thatch: ['#DDC9A0', '#C9994A', 'gable'], house_med: ['#EFE6D6', '#C4643E', 'gable'],
    house_mud: ['#CF9C68', '#D9AA76', 'flat'], yurt: ['#EEE8DA', '#E2D8C2', 'cone'], haystack: ['#D9B45C', '#E2C068', 'cone'], well: ['#A29C92', '#7E6A50', 'flat'],
  };
  function speckGeo(name) {
    return SG.LP.MeshCache.get('map:speck:' + name, () => {
      const LP = SG.LP, H = LP.Paint.hex, d = SPECK[name] || SPECK.house_han;
      const b = SG.LPProps.geometry(name, { seed: 0 }).box;
      const w = b.max.x - b.min.x, dp = b.max.z - b.min.z, h = Math.max(0.05, b.max.y), cx = (b.max.x + b.min.x) / 2, cz = (b.max.z + b.min.z) / 2;
      const F = new LP.MeshBuilder(7);
      F.jitter = 0.04; F.ink = 0;
      if (d[2] === 'cone') {
        F.color = H(d[0]); F.cylinder([cx, 0, cz], w * 0.48, w * 0.46, h * 0.45, 6, false, false, false);
        F.color = H(d[1]); F.cylinder([cx, h * 0.45, cz], w * 0.5, 0.0, h * 0.55, 6, false, false, false);
      } else if (d[2] === 'flat') {
        F.color = H(d[0]); F.box([cx, h * 0.5, cz], [w * 0.92, h, dp * 0.92], H(d[1]), 'tfkrl');
      } else {
        const alongX = w >= dp, bw = (alongX ? w : dp) * 0.84, bd = (alongX ? dp : w) * 0.8, wh = h * 0.5;
        F.push();
        F.translate(cx, 0, cz);
        if (!alongX) F.rotate(0, 90, 0);
        F.color = H(d[0]); F.box([0, wh * 0.5, 0], [bw, wh, bd], null, 'fkrl');
        F.color = H(d[1]); F.roof([0, wh, 0], bw, bd, h - wh, bd * 0.12, 0.0001, true);
        F.pop();
      }
      return F.toGeometry();
    });
  }
  // 草丛（道具库 grass，不描墨线）与野花（小簇：花茎 + 五瓣花，花瓣白色乘实例色）
  // 草丛：五片两面的草叶（每片 2 个三角形），叶根暗绿、叶尖暖亮（道具库 grass 的配色，但三角形只有它的四分之一），高约 0.2 单位
  function lpGrassGeometry() {
    return SG.LP.MeshCache.get('map:tuft', () => {
      const LP = SG.LP, H = LP.Paint.hex, F = new LP.MeshBuilder(93);
      F.jitter = 0.05; F.ink = 0; F.wind = 1; F.windGradient = true; F.windY0 = 0; F.windY1 = 0.22;
      const rnd = SG.SeededRandom(91), base = H('#5E8A40'), tip = H('#B4C878'), mid = H('#8DB45A');
      for (let k = 0; k < 5; k++) {
        const a = k * 1.26 + rnd.nextDouble() * 0.6, ca = Math.cos(a), sa = Math.sin(a);
        const r0 = 0.015 + rnd.nextDouble() * 0.04, h = 0.13 + rnd.nextDouble() * 0.1, lean = 0.04 + rnd.nextDouble() * 0.06, w = 0.024;
        const bx = ca * r0, bz = sa * r0;
        const b0 = [bx - sa * w, 0, bz + ca * w], b1 = [bx + sa * w, 0, bz - ca * w], t = [bx + ca * lean, h, bz + sa * lean];
        const nf = LP.V.norm([ca * 0.3, 1, sa * 0.3]), nb = LP.V.norm([-ca * 0.3, 1, -sa * 0.3]);
        const tc = LP.Paint.mix(mid, tip, rnd.nextDouble());
        for (const [n, flip] of [[nf, false], [nb, true]]) {
          const i0 = F.addVertex(b0, n, base), i1 = F.addVertex(b1, n, base), i2 = F.addVertex(t, n, tc);
          if (flip) F.addTri(i0, i2, i1); else F.addTri(i0, i1, i2);
        }
      }
      return F.toGeometry();
    });
  }
  function lpFlowerGeometry() {
    return SG.LP.MeshCache.get('map:flower', () => {
      const LP = SG.LP, H = LP.Paint.hex, F = new LP.MeshBuilder(91);
      F.jitter = 0.04; F.ink = 0; F.wind = 0.6; F.windGradient = true; F.windY0 = 0; F.windY1 = 0.2;
      for (const [x, z, h] of [[0, 0, 0.17], [0.07, 0.04, 0.13], [-0.05, 0.06, 0.15]]) {
        F.color = H('#4E7A3A'); F.blade([x, 0, z], [x, h, z], 0.022);
        F.color = [1, 1, 1]; F.disc([x, h, z], 0.04, 5);
        F.color = H('#F2D45A'); F.disc([x, h + 0.006, z], 0.014, 4);
      }
      return F.toGeometry();
    });
  }

  // 城池的灯（CultureArt 给的灯位按城池模型的比例：地图上把灯池放大，城在夜里成一团暖光）
  const CITY_LAMP_R = 3.4, CITY_LAMP_I = 2.0, CITY_LAMP_H = 1.1;
  // 势力 / 选中光圈的陆地权重（地表高度 → 0 … 1）：水面（高度 0）稍下为 0，岸上 0.09 以上为 1
  function ringLandW(sy) { return M.clamp01((sy + 0.03) / 0.12); }
  // 只夜里亮的灯点亮时的夜色（与 LP.Mood._lampOn 一致）：城灯在地图第一次到这个夜色时才交给氛围
  const LAMP_NIGHT = 0.45;
  // 第四轮：氛围灯池的点光源只在有灯亮着时留在光照里。白天（以及地图隐藏、战场也没点灯时）池里每盏灯的强度都是 0，
  // three 却照样按 6 盏点光源编译 LP 材质、逐像素遍历它们（SwiftShader 下整帧慢约 37%）。改为「有亮灯 → 全池可见，
  // 没有 → 全池隐藏」：只有 0 / 6 盏两种着色器变体，各编译一次以后来回切换只是换程序。挂在这个氛围实例的 update 上
  // （场景钩子每次渲染都调；战场借用主场景时同样生效）。INTEGRATION：应由 lowpoly.js 的 Mood.update 自己做——做了以后这里
  // 只是重复同样的赋值
  function gateLampPool(mood) {
    if (!mood || mood._sgPoolGate || typeof mood.update !== 'function') return;
    const up = mood.update;
    mood._sgPoolGate = true;
    mood.update = function (camera) {
      up.call(this, camera);
      // MapView._prewarmLampPool 的临时点光源：画过一次之后移走（第二次渲染开始时；不依赖地图的 update 是否还在跑）
      const w = this._sgWarm;
      if (w && ++w.renders >= 2) { for (const l of w.lights) { if (l.parent) l.parent.remove(l); if (l.dispose) l.dispose(); } this._sgWarm = null; }
      const pool = this.pool;
      if (!pool || !pool.length) return;
      let any = false;
      for (const L of this.lamps) if (L.light) { any = true; break; }
      for (const l of pool) if (l.visible !== any) l.visible = any;
    };
  }
  // 地图的缺省时刻：温暖的午后（黄金时刻提亮约一半，太阳偏暖）。MapView.setMood / followClock 可改
  const MAP_HOUR = 16.3;
  // 标题画面的飞越：黄昏，城里的灯笼亮起（开局后平滑转回午后）。标题画面 = game.js 的 .sg-title > .sg-title-hero 在页面上
  //（标题背景也有一份临时的 SG.G，不能用它判断；每半秒查一次）
  const TITLE_HOUR = 19.2;
  let _titleT = -1, _titleUp = false, _titleForce = false;
  function titleUp() {
    if (_titleForce) return true;
    const t = now();
    if (t - _titleT > 500) { _titleT = t; _titleUp = !!(SG.Game && typeof document !== 'undefined' && document.querySelector('.sg-title > .sg-title-hero')); }
    return _titleUp;
  }
  const defaultHour = () => (titleUp() ? TITLE_HOUR : MAP_HOUR);
  // 城池（CultureArt.scatter）：镜头到区块 < CITY_LOD 用近景模型，< CITY_INK 描墨线；镜头高于 CITY_OV_Y 时换成合并的远景（全图视角）
  const CITY_LOD = 72, CITY_INK = 38, CITY_OV_Y = 115;
  // 村落：远于 VILLAGE_HIDE 的区块不画（远看只是几个像素）；桥与关隘的墨线只在 PROP_INK 以内
  //（修订：55 → 30——中景里垛口只隔两三个像素，固定像素宽的墨线把关墙画成黑黄相间的斑马纹）
  const VILLAGE_HIDE = 105, PROP_INK = 30;
  // 村落近景（道具库远景细节 + 墨线 + 投影）的半径；更远处用十来个三角形的「小房块」（远看只有几个像素；修订：38 → 24，
  // 小房块按区块合并成一次绘制）
  const VILLAGE_NEAR = 24;
  // 石块、灌丛的近景（墨线）半径；更远处为区块合并的远景（与树的 3D 带同宽）
  const SCATTER_NEAR = VEG_NEAR;
  // 桥、关隘：镜头 PROP_HIDE 以外整块隐去（只剩一两个像素），PROP_SHADOW 以外不投影
  const PROP_HIDE = 130, PROP_SHADOW = 60;
  // 树的墨线：修订后 3D 树一律描墨线（TREE_INK 覆盖整个 3D 带；远处的公告板图集本来就带墨线），
  // 不再出现「近处的 3D 树没墨线、远处的公告板有墨线」的深度倒置
  const TREE_INK = VEG_NEAR * 1.1;
  // LP 旗：旗杆（木色，带金色顶尖）与旗面（白色，乘势力色；略带波浪），尺寸同第三版的旗（杆高 2.2、旗面 0.9 × 0.6）
  function lpFlagGeometry() {
    return SG.LP.MeshCache.get('map:flag', () => {
      const LP = SG.LP, H = LP.Paint.hex, P = new LP.MeshBuilder(51), C = new LP.MeshBuilder(52);
      P.jitter = 0.05; P.aoStrength = 0;
      P.color = H('#6E4B36'); P.cylinder([0, 0, 0], 0.05, 0.035, 2.2, 6);
      P.color = H('#E3B34C'); P.cone([0, 2.2, 0], 0.075, 0.18, 6);
      C.jitter = 0.04; C.aoStrength = 0;
      C.color = [1, 1, 1];
      const seg = 4, x0 = 0.04, x1 = 0.95, yTop0 = 2.15, yTop1 = 2.05, yBot0 = 1.5, yBot1 = 1.45;
      for (let i = 0; i < seg; i++) {
        const u0 = i / seg, u1 = (i + 1) / seg, w = u => Math.sin(u * Math.PI * 1.6) * 0.07 * u;
        const a = [x0 + (x1 - x0) * u0, yTop0 + (yTop1 - yTop0) * u0, w(u0)], b = [x0 + (x1 - x0) * u1, yTop0 + (yTop1 - yTop0) * u1, w(u1)];
        const c = [x0 + (x1 - x0) * u1, yBot0 + (yBot1 - yBot0) * u1, w(u1)], d = [x0 + (x1 - x0) * u0, yBot0 + (yBot1 - yBot0) * u0, w(u0)];
        C.color = LP.Paint.shade([1, 1, 1], i & 1 ? 0.94 : 1);
        C.quadTwoSided ? C.quadTwoSided(a, b, c, d) : (C.quad(a, b, c, d, [0, 0, 1]), C.quad(a, d, c, b, [0, 0, -1]));
      }
      return { pole: P.toGeometry(), cloth: C.toGeometry() };
    });
  }

  // ---------------------------------------------------------- 道路、桥梁、关隘 --
  // 线段集合的桶索引（每段 4 个数 ax, az, bx, bz）
  function makeSegIndex(S, radius) {
    if (!S || !S.length) return null;
    const cell = 4;
    let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
    for (let k = 0; k < S.length; k += 4) {
      x0 = Math.min(x0, S[k], S[k + 2]); x1 = Math.max(x1, S[k], S[k + 2]); z0 = Math.min(z0, S[k + 1], S[k + 3]); z1 = Math.max(z1, S[k + 1], S[k + 3]);
    }
    x0 -= radius + 1; z0 -= radius + 1; x1 += radius + 1; z1 += radius + 1;
    const nx = Math.ceil((x1 - x0) / cell) + 1, nz = Math.ceil((z1 - z0) / cell) + 1, b = new Array(nx * nz).fill(null);
    for (let k = 0; k < S.length; k += 4) {
      const i0 = Math.floor((Math.min(S[k], S[k + 2]) - radius - x0) / cell), i1 = Math.floor((Math.max(S[k], S[k + 2]) + radius - x0) / cell);
      const j0 = Math.floor((Math.min(S[k + 1], S[k + 3]) - radius - z0) / cell), j1 = Math.floor((Math.max(S[k + 1], S[k + 3]) + radius - z0) / cell);
      for (let a = i0; a <= i1; a++) for (let c = j0; c <= j1; c++) { const o = c * nx + a; (b[o] || (b[o] = [])).push(k >> 2); }
    }
    return { at(x, z) { const a = Math.floor((x - x0) / cell), c = Math.floor((z - z0) / cell); return a < 0 || c < 0 || a >= nx || c >= nz ? null : b[c * nx + a]; } };
  }
  // 路面纹理：u 横跨路面（两侧透明渐隐、两道车辙），v 沿路重复
  let _roadTex = null;
  function roadTexture() {
    if (_roadTex) return _roadTex;
    const W = 32, H = 64, d = new Uint8Array(W * H * 4), rnd = SG.SeededRandom(57);
    for (let y = 0; y < H; y++)
      for (let x = 0; x < W; x++) {
        const u = (x + 0.5) / W, o = (y * W + x) * 4;
        const rut = Math.exp(-Math.pow((u - 0.33) / 0.05, 2)) + Math.exp(-Math.pow((u - 0.67) / 0.05, 2));
        const g = 0.9 + 0.1 * Math.sin(y / H * Math.PI * 8 + x * 0.7) * 0.5 + (rnd.nextDouble() - 0.5) * 0.12 - rut * 0.16;
        d[o] = d[o + 1] = d[o + 2] = b8(g);
        d[o + 3] = b8(ss(0.0, 0.24, u) * ss(1.0, 0.76, u) * 0.92);
      }
    const t = new THREE.DataTexture(d, W, H, THREE.RGBAFormat);
    t.wrapS = THREE.ClampToEdgeWrapping; t.wrapT = THREE.RepeatWrapping;
    t.magFilter = THREE.LinearFilter; t.minFilter = THREE.LinearMipmapLinearFilter; t.generateMipmaps = true;
    t.needsUpdate = true;
    _roadTex = t;
    return t;
  }
  // 第四版：绘制土路的纹理（u 横跨路面，v 沿路重复）：暖棕的土、两道较暗的车辙、路中略亮、碎石点、
  // 两侧参差的草边（透明度随草叶起伏渐隐）。颜色为 sRGB（LP 贴花材质乘以光照）
  let _lpRoadTex = null;
  function lpRoadTexture() {
    if (_lpRoadTex) return _lpRoadTex;
    const W = 64, Hh = 256, d = new Uint8Array(W * Hh * 4), rnd = SG.SeededRandom(57);
    const dirt = [0.68, 0.52, 0.35], rutC = [0.55, 0.4, 0.27], crown = [0.75, 0.6, 0.42], grass = [0.55, 0.65, 0.36];
    const edgeL = new Float32Array(Hh), edgeR = new Float32Array(Hh);
    for (let y = 0; y < Hh; y++) {
      const t = y / Hh * Math.PI * 2;
      edgeL[y] = 0.13 + 0.04 * Math.sin(t * 3 + 1.1) + 0.025 * Math.sin(t * 11 + 0.3) + 0.02 * (rnd.nextDouble() - 0.5);
      edgeR[y] = 0.87 + 0.04 * Math.sin(t * 2 + 2.3) + 0.025 * Math.sin(t * 13 + 1.7) + 0.02 * (rnd.nextDouble() - 0.5);
    }
    for (let y = 0; y < Hh; y++)
      for (let x = 0; x < W; x++) {
        const u = (x + 0.5) / W, o = (y * W + x) * 4;
        const rut = Math.exp(-Math.pow((u - 0.35) / 0.035, 2)) + Math.exp(-Math.pow((u - 0.65) / 0.035, 2));
        const cr = Math.exp(-Math.pow((u - 0.5) / 0.08, 2));
        let c = dirt.slice();
        for (let k = 0; k < 3; k++) c[k] += (crown[k] - c[k]) * cr * 0.5 + (rutC[k] - c[k]) * rut * 0.6;
        const n = 0.94 + 0.12 * rnd.nextDouble();
        // 草边：离边缘越近越绿，透明度在边缘参差地渐隐
        const e = Math.min(u - edgeL[y], edgeR[y] - u);
        const g = 1 - ss(0.0, 0.07, e);
        for (let k = 0; k < 3; k++) c[k] = (c[k] + (grass[k] - c[k]) * g * 0.7) * n;
        const a = ss(-0.03, 0.05, e);
        d[o] = b8(c[0]); d[o + 1] = b8(c[1]); d[o + 2] = b8(c[2]); d[o + 3] = b8(a * 0.95);
      }
    // 碎石点
    for (let i = 0; i < 260; i++) {
      const x = Math.floor(W * (0.2 + 0.6 * rnd.nextDouble())), y = Math.floor(Hh * rnd.nextDouble()), dark = rnd.nextDouble() < 0.6;
      for (const [ox, oy] of [[0, 0], [1, 0], [0, 1], [1, 1]]) {
        const o = (((y + oy) % Hh) * W + Math.min(W - 1, x + ox)) * 4;
        if (d[o + 3] < 120) continue;
        const k = dark ? 0.72 : 1.18;
        d[o] = b8(d[o] / 255 * k); d[o + 1] = b8(d[o + 1] / 255 * k); d[o + 2] = b8(d[o + 2] / 255 * k);
      }
    }
    const t = new THREE.DataTexture(d, W, Hh, THREE.RGBAFormat);
    t.wrapS = THREE.ClampToEdgeWrapping; t.wrapT = THREE.RepeatWrapping;
    t.magFilter = THREE.LinearFilter; t.minFilter = THREE.LinearMipmapLinearFilter; t.generateMipmaps = true;
    t.needsUpdate = true;
    _lpRoadTex = t;
    return t;
  }
  // 桥：a、b 为两岸的路面点（地图坐标），deck 为桥面高度；stone = 石拱桥，否则木桥
  function bridgeInto(mb, a, b, deck, stone) {
    const dx = b.x - a.x, dz = b.z - a.z, L = Math.hypot(dx, dz) + 0.5;
    mb.M = new THREE.Matrix4().makeTranslation((a.x + b.x) / 2, deck, (a.z + b.z) / 2).multiply(new THREE.Matrix4().makeRotationY(Math.atan2(dx, dz)));
    const C = (r, g, bb) => new THREE.Color(r, g, bb);
    if (stone) {
      const st = C(0.6, 0.57, 0.51), st2 = C(0.5, 0.47, 0.42);
      mb.box(V(0, -0.05, 0), V(0.62, 0.14, L), st);
      for (const sx of [-1, 1]) mb.box(V(sx * 0.29, 0.07, 0), V(0.05, 0.1, L), C(0.68, 0.65, 0.58));
      const spans = Math.max(1, Math.round((L - 0.5) / 1.7)), sl = (L - 0.5) / spans;
      for (let k = 0; k <= spans; k++) {
        const z = -L / 2 + 0.25 + k * sl;
        if (k > 0 && k < spans) mb.box(V(0, -(deck + 0.4) / 2 - 0.1, z), V(0.56, deck + 0.4, 0.24), st2);
      }
      for (let k = 0; k < spans; k++) {
        const zm = -L / 2 + 0.25 + (k + 0.5) * sl, r = sl / 2, m = 7;
        for (let q = 0; q < m; q++) {
          const t0 = q / m * Math.PI, t1 = (q + 1) / m * Math.PI, tm = (t0 + t1) / 2;
          const y = -deck - 0.05 + Math.sin(tm) * Math.min(deck + 0.02, r * 0.9), z = zm - Math.cos(tm) * r * 0.98;
          mb.box(V(0, Math.min(-0.12, y), z), V(0.58, 0.07, Math.PI * r / m * 1.05), st2);
        }
      }
    } else {
      const wd = C(0.52, 0.4, 0.27), wd2 = C(0.42, 0.32, 0.22);
      mb.box(V(0, -0.03, 0), V(0.56, 0.05, L), wd);
      const n = Math.max(2, Math.round(L / 0.9));
      for (let k = 0; k <= n; k++) {
        const z = -L / 2 + k * L / n;
        for (const sx of [-1, 1]) mb.box(V(sx * 0.27, (0.16 - deck - 0.3) / 2, z), V(0.05, deck + 0.46, 0.05), wd2);
      }
      for (const sx of [-1, 1]) mb.box(V(sx * 0.27, 0.13, 0), V(0.035, 0.035, L), wd2);
    }
    mb.M = null;
  }
  // 关隘：名称、经度、纬度、样式（han = 中原关楼；west = 西方石门）
  const PASSES = [
    ['虎牢关', 113.25, 34.82, 'han'], ['函谷关', 111.0, 34.62, 'han'], ['剑阁', 105.55, 32.2, 'han'], ['散关', 106.95, 34.3, 'han'],
    ['玉门关', 93.86, 40.35, 'han'], ['温泉关', 22.53, 38.8, 'west'], ['奇里乞亚门', 34.78, 37.28, 'west'], ['开伯尔山口', 71.08, 34.1, 'west'],
  ];
  function shade3(c, k) { return new THREE.Color(Math.min(1, c.r * (1 + k)), Math.min(1, c.g * (1 + k)), Math.min(1, c.b * (1 + k))); }
  function passInto(mb, view, x, z, dx, dz, style) {
    const C = (r, g, b) => new THREE.Color(r, g, b);
    const yaw = Math.atan2(dx, dz), y0 = view.surfaceY(x, z), K = 0.78;
    const han = style !== 'west';
    const stone = han ? C(0.66, 0.63, 0.57) : C(0.78, 0.72, 0.6), dark = C(0.16, 0.12, 0.1);
    // 两侧城墙（第三版修订）：贴着地表的斜切墙段（每段 0.45，墙顶高出地面 0.42），伸向两侧山坡；
    // 地面低于关门 0.5 以上（下坡）、遇水或段内高差过大（断崖）即止
    const px = dz, pz = -dx;   // 横向（地图坐标，右手）
    const HT = 0.42, TH = 0.15, SEG = 0.45;
    const crenRot = new THREE.Matrix4().makeRotationY(yaw);
    for (const s of [-1, 1]) {
      let oa = 0.62, ga = view.surfaceY(x + px * s * oa, z + pz * s * oa);
      for (let k = 0; k < 7; k++) {
        const ob = oa + SEG, bx = x + px * s * ob, bz = z + pz * s * ob, gb = view.surfaceY(bx, bz);
        if (gb < 0.05 || gb < y0 - 0.5 || Math.abs(gb - ga) > 0.9) break;
        const ax = x + px * s * oa, az = z + pz * s * oa;
        const P = (wx, wz, y, t) => V(wx + dx * t, y, wz + dz * t);
        const a0 = P(ax, az, ga - 0.35, -TH), a1 = P(ax, az, ga - 0.35, TH), a2 = P(ax, az, ga + HT, TH), a3 = P(ax, az, ga + HT, -TH);
        const b0 = P(bx, bz, gb - 0.35, -TH), b1 = P(bx, bz, gb - 0.35, TH), b2 = P(bx, bz, gb + HT, TH), b3 = P(bx, bz, gb + HT, -TH);
        const sd = s > 0;
        // 两个侧面、墙顶、末端（朝向按 s 翻转，保持外法线）
        if (sd) { mb.quad(a0, a3, b3, b0, stone); mb.quad(a1, b1, b2, a2, stone); mb.quad(a3, a2, b2, b3, shade3(stone, 0.06)); }
        else { mb.quad(a0, b0, b3, a3, stone); mb.quad(a1, a2, b2, b1, stone); mb.quad(a3, b3, b2, a2, shade3(stone, 0.06)); }
        const nb = view.surfaceY(x + px * s * (ob + SEG), z + pz * s * (ob + SEG));
        if (k === 6 || nb < 0.05 || nb < y0 - 0.5 || Math.abs(nb - gb) > 0.9) { if (sd) mb.quad(b0, b3, b2, b1, stone); else mb.quad(b0, b1, b2, b3, stone); }
        // 垛口（外侧）
        for (const f of [0.25, 0.75]) {
          const cx = ax + (bx - ax) * f, cz = az + (bz - az) * f, cy = ga + (gb - ga) * f + HT;
          mb.M = new THREE.Matrix4().makeTranslation(cx - dx * 0.1, cy + 0.05, cz - dz * 0.1).multiply(crenRot);
          mb.box(V(0, 0, 0), V(0.13, 0.1, 0.06), stone);
          mb.M = null;
        }
        oa = ob; ga = gb;
      }
    }
    mb.M = new THREE.Matrix4().makeTranslation(x, y0, z).multiply(new THREE.Matrix4().makeRotationY(yaw)).multiply(new THREE.Matrix4().makeScale(K, K, K));
    mb.box(V(0, -0.32, 0), V(han ? 1.7 : 1.9, 0.6, han ? 0.8 : 0.72), stone, 0);   // 基座（斜坡上不悬空）
    if (han) {
      // 关城：墩台 + 门洞 + 两层关楼
      mb.box(V(0, 0.3, 0), V(1.7, 0.95, 0.8), stone, 0.06);
      for (const f of [-1, 1]) mb.box(V(0, 0.12, f * 0.405), V(0.36, 0.5, 0.02), dark);
      for (let i = -3; i <= 3; i++) if (i) for (const f of [-1, 1]) mb.box(V(i * 0.24, 0.84, f * 0.36), V(0.12, 0.12, 0.08), stone);
      const red = C(0.62, 0.2, 0.16), roof = C(0.24, 0.27, 0.33);
      mb.box(V(0, 0.98, 0), V(1.05, 0.3, 0.5), red);
      mb.chineseRoof(V(0, 1.13, 0), 1.5, 0.82, 0.34, roof);
      mb.box(V(0, 1.35, 0), V(0.62, 0.22, 0.34), red);
      mb.chineseRoof(V(0, 1.46, 0), 0.98, 0.6, 0.3, roof);
      mb.box(V(0.0, 1.0, -0.26), V(0.5, 0.14, 0.02), C(0.12, 0.12, 0.16));   // 匾额
    } else {
      // 石门：拱门 + 两座方塔
      mb.box(V(0, 0.32, 0), V(1.0, 0.95, 0.7), stone, 0.06);
      for (const f of [-1, 1]) mb.box(V(0, 0.16, f * 0.355), V(0.34, 0.56, 0.02), dark);
      for (const sx of [-1, 1]) {
        mb.box(V(sx * 0.68, 0.48, 0), V(0.5, 1.3, 0.62), C(0.72, 0.66, 0.55), 0.06);
        for (const t of [-0.16, 0.16]) for (const f of [-1, 1]) mb.box(V(sx * 0.68 + t, 1.18, f * 0.24), V(0.12, 0.12, 0.12), C(0.72, 0.66, 0.55));
      }
      mb.flag(V(0, 0.8, 0), 0.6, 0.32, 0.22, C(0.66, 0.16, 0.14));
    }
    mb.M = null;
  }
  // 第四版的关隘：与 passInto 相同的位置、朝向与沿坡墙段的取舍，模型换成道具库的城门楼 / 圆塔与墙段（1 米 = 0.1 单位）
  function lpPassInto(out, view, x, z, dx, dz, style, name) {
    const han = style !== 'west', k = 0.1, y0 = view.surfaceY(x, z);
    const yawRoad = Math.atan2(dx, -dz);   // three 坐标：模型 +Z 朝道路方向
    const px = dz, pz = -dx, HT = 0.42, SEG = 0.45;
    const segGeo = SG.LPProps.geometry('wall_segment', { seed: 0, kind: han ? 'rammed' : 'stone', len: 4.9, h: 6.2, merlons: true }).ink;
    const up = new THREE.Vector3(0, 1, 0), fwd = new THREE.Vector3(0, 0, 1);
    for (const s of [-1, 1]) {
      let oa = 0.62, ga = view.surfaceY(x + px * s * oa, z + pz * s * oa);
      for (let q = 0; q < 7; q++) {
        const ob = oa + SEG, bx = x + px * s * ob, bz = z + pz * s * ob, gb = view.surfaceY(bx, bz);
        if (gb < 0.05 || gb < y0 - 0.5 || Math.abs(gb - ga) > 0.9) break;
        const ax = x + px * s * oa, az = z + pz * s * oa;
        // 墙段：长度沿 x，绕 y 转到横向，再按坡度绕本地 z 倾斜；底部埋进坡里
        const wx = (bx - ax), wz = -(bz - az), wl = Math.hypot(wx, wz) || 1;
        const yaw = Math.atan2(-wz / wl, wx / wl), pitch = Math.atan2(gb - ga, SEG);
        const qn = new THREE.Quaternion().setFromAxisAngle(up, yaw).multiply(new THREE.Quaternion().setFromAxisAngle(fwd, pitch));
        const m = new THREE.Matrix4().compose(new THREE.Vector3((ax + bx) / 2, (ga + gb) / 2 + HT - 6.2 * k, -(az + bz) / 2), qn, new THREE.Vector3(k, k, k));
        if (segGeo) out.entries.push({ geo: segGeo, matrix: m });
        oa = ob; ga = gb;
      }
    }
    const gate = han ? SG.LPProps.make('gate_tower', { seed: 0, name: name || '关', hallName: '', lit: 'night', color: '#9a3a2c' }) : null;
    if (gate) {
      gate.scale.setScalar(k * 0.98);
      gate.position.set(x, y0 - 0.05, -z);
      gate.rotation.y = yawRoad;
      out.objects.push(gate);
      const lp = gate.userData.lp;
      if (lp && lp.lamps && lp.lamps.length) {
        gate.updateMatrixWorld(true);
        const c = new THREE.Vector3();
        for (const l of lp.lamps) c.add(new THREE.Vector3(l.pos[0], l.pos[1], l.pos[2]));
        c.multiplyScalar(1 / lp.lamps.length).applyMatrix4(gate.matrixWorld);
        out.lamps.push([c.x, c.y + 0.2, c.z]);
      }
    } else {
      // 西方石门：两座圆塔夹一段带垛口的石墙
      const tg = SG.LPProps.geometry('tower_round', { seed: 1 }).ink, wg = SG.LPProps.geometry('wall_segment', { seed: 0, kind: 'stone', len: 7, h: 7, merlons: true }).ink;
      const rq = new THREE.Quaternion().setFromAxisAngle(up, yawRoad + Math.PI / 2);
      if (wg) out.entries.push({ geo: wg, matrix: new THREE.Matrix4().compose(new THREE.Vector3(x, y0 - 0.15, -z), rq, new THREE.Vector3(k * 1.1, k * 1.1, k * 1.1)) });
      if (tg) for (const sd of [-1, 1]) {
        const tx = x + px * sd * 0.62, tz = z + pz * sd * 0.62;
        out.entries.push({ geo: tg, matrix: new THREE.Matrix4().compose(new THREE.Vector3(tx, Math.min(view.surfaceY(tx, tz), y0) - 0.12, -tz), rq, new THREE.Vector3(k * 1.15, k * 1.15, k * 1.15)) });
      }
    }
  }
  // ---------------------------------------------------------- MapView --
  class MapView {
    constructor() {
      this.root = null;
      this.cities = new Map();
      this.time = 0;
      this._cityX = null; this._cityZ = null;
      this._master = null; this._outer = null; this._lod = null; this._maps = null; this._tex = null;
      this._ownMats = [];
      this._treeTiles = null; this._scatter = null;
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
        // 第三版：山脉 / 高原在海岸 2.4 单位内逐渐压低（免得山体直插海面成为锯齿状的断崖）
        const cr = 0.3 + 0.7 * ss(0, 2.4, coast);
        if (P > 0.001 && wOld < 1) h += (1 - wOld) * P * (0.7 + n * 0.6) * cr;
        // 沙丘
        const des = f[F_DESERT];
        if (des > 0.02) h += des * (fbm(x * 0.3 + 40, z * 0.3 + 40, 2) - 0.45) * 0.8;
        // 山脉
        h += G.ridges.sum(x, z) * cr;
      }
      // 河流与湖泊（宽度系数 rW、河床深度 rD；旧版为 1 与 −0.35）
      // 第三版：河岸宽度随两岸高度增加（山中的河谷平均坡度约 0.8，不再是刀切的峡谷），岸坡为上缘平缓的曲线
      if (!sea) {
        const rd = G.riverDist(x, z) / G.rW;
        const bw = Math.max(1.8, (h + 0.1) * 1.25 / G.rW);
        if (rd < 0.75 + bw) {
          const d = G.rD;
          let target = d;
          if (rd >= 0.75) { const t = (rd - 0.75) / bw; target = M.lerp(d * (0.1 / 0.35), h, t * (2 - t)); }
          h = Math.min(h, target);
        }
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
    // 实际渲染出的地表高度：主网格范围内按 L0 地形块的三角形插值（与网格对角线一致），范围外为远景栅格的双三次插值
    surfaceY(x, z) {
      const m = this._master;
      if (!m) return this.height(x, z);
      const fx0 = (x - m.x0) / m.res, fz0 = (z - m.z0) / m.res;
      const i = Math.floor(fx0), j = Math.floor(fz0);
      if (i < 0 || j < 0 || i >= m.nx - 1 || j >= m.nz - 1) return this._outerH(x, z);
      const fx = fx0 - i, fz = fz0 - j, W = m.nx, h = m.h, o = j * W + i;
      const ha = h[o], hb = h[o + W], hc = h[o + W + 1], hd = h[o + 1];
      if (((i + j) & 1) === 0) {
        if (fz >= fx) return ha + fz * (hb - ha) + fx * (hc - hb);
        return ha + fx * (hd - ha) + fz * (hc - hd);
      }
      if (fx + fz <= 1) return ha + fx * (hd - ha) + fz * (hb - ha);
      return hc + (1 - fx) * (hb - hc) + (1 - fz) * (hd - hc);
    }
    // 地形块顶点的高度：落在主网格格点上时取格点值，否则取远景栅格
    _vertexH(x, z) {
      const m = this._master;
      const fi = (x - m.x0) / m.res, fj = (z - m.z0) / m.res;
      const i = Math.round(fi), j = Math.round(fj);
      if (i >= 0 && j >= 0 && i < m.nx && j < m.nz && Math.abs(fi - i) < 1e-3 && Math.abs(fj - j) < 1e-3) return m.h[j * m.nx + i];
      return this._outerH(x, z);
    }
    // 远景栅格（5.0 间距）的 Catmull-Rom 双三次插值
    _outerH(x, z) {
      const o = this._outer;
      if (!o) return this.height(x, z);
      const fx = M.clamp((x - o.x0) / o.res, 0, o.nx - 1), fz = M.clamp((z - o.z0) / o.res, 0, o.nz - 1);
      const i = Math.min(o.nx - 2, Math.floor(fx)), j = Math.min(o.nz - 2, Math.floor(fz));
      const tx = fx - i, tz = fz - j, W = o.nx, h = o.h;
      const wx = crw(tx, _crx), wz = crw(tz, _crz);
      let s = 0;
      for (let b = 0; b < 4; b++) {
        const jj = Math.max(0, Math.min(o.nz - 1, j - 1 + b)) * W;
        let r = 0;
        for (let a = 0; a < 4; a++) r += wx[a] * h[jj + Math.max(0, Math.min(W - 1, i - 1 + a))];
        s += wz[b] * r;
      }
      return s;
    }
    isForest(x, z, h) { return h >= 0.6 && h < 3.2 && fbm(x * 0.09 + 100, z * 0.09 + 100, 3) > 0.6; }
    // 地图坐标处的地形摘要（第二阶段：战场地形取自城池周边，见报告 INTEGRATION）
    // → { h, slope (0 平 – 1 垂直), water: 'sea' | 'river' | 'lake' | null, depth, forest, farm, sand, snow, rock, biome, river（到河流距离）, coast, road（到道路距离）}
    terrainInfo(x, z) {
      const G = this._geo;
      if (!G || !this._master) return null;
      const h = this.surfaceY(x, z), e = 0.6;
      const hx = (this.surfaceY(x + e, z) - this.surfaceY(x - e, z)) / (2 * e), hz = (this.surfaceY(x, z + e) - this.surfaceY(x, z - e)) / (2 * e);
      const slope = 1 - 1 / Math.sqrt(1 + hx * hx + hz * hz);
      const sea = G.inSea(x, z), coast = G.coastDist(x, z);
      const gi = groundInfo(G, x, z, h, false);
      const rd = gi.river;
      const farm = this._farmAt(x, z, h, gi, slope);
      const water = h < -0.05 ? (sea ? 'sea' : rd < 1.6 ? 'river' : 'lake') : null;
      const B = [['desert', gi.des], ['steppe', gi.st], ['tropical', gi.tr], ['boreal', gi.bo], ['med', gi.med], ['savanna', gi.sav]];
      let biome = 'temperate', bw = 0.35;
      for (const [k, w] of B) if (w > bw) { biome = k; bw = w; }
      if (gi.barren > 0.55 && h > 2.5) biome = 'alpine';
      return {
        h, slope, water, depth: Math.max(0, -h), forest: gi.forest, farm, sand: gi.sand, snow: gi.snow,
        rock: slope > 0.42 || h > 4.2, biome, river: rd, coast: sea ? -coast : coast, road: this.roadDist(x, z),
      };
    }
    // 到最近道路（陆路）的距离；未生成道路时 999
    roadDist(x, z) {
      const R = this._roadIdx;
      if (!R) return 999;
      const list = R.at(x, z);
      if (!list) return 999;
      let best = 999;
      const S = this._roadSegs;
      for (const k of list) { const o = k * 4; best = Math.min(best, segDist(x, z, S[o], S[o + 1], S[o + 2], S[o + 3])); }
      return best;
    }

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
      const budget = opts.sliceMs > 0 ? opts.sliceMs : 8;
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
      const hadRoot = !!this.root;
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
      this._cityIdx10 = this._makeCityIndex(10);
      this._master = null; this._outer = null;
      // 旧地图的释放（大量几何体 / 材质）单独成一步
      if (hadRoot) { this._progLabel = '测绘山川'; this._prog = 0; yield; }
      // 各阶段的进度权重
      const stages = [['geo', '测绘山川', 0.16], ['terrain', '堆砌地形', 0.3], ['water', '江海', 0.04], ['trees', '林木', 0.12],
        ['scatter', '山石村落', 0.06], ['roads', '道路关隘', 0.07], ['cities', '城池', 0.17], ['misc', '云', 0.08]];
      this._stageMs = {};
      let base = 0;
      const run = function* (self, s, gen) {
        const t = now();
        self._progLabel = s[1];
        self._traceTag = '';
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
      yield* run(this, stages[1], this._genTerrain(state));
      yield* run(this, stages[2], this._genWater());
      yield* run(this, stages[3], this._genTrees());
      yield* run(this, stages[4], this._genScatter(state));
      yield* run(this, stages[5], this._genRoads(state));
      yield* run(this, stages[6], this._genCities(state));
      const self = this;
      yield* run(this, stages[7], (function* () {
        yield* self._buildClouds();
        self._buildSelectRing(); self.refresh(state); yield;
        // 第四版：地图的时刻（缺省为温暖的午后；重建地图时沿用之前设定的时刻 / 跟随时钟）
        self._moodVis = undefined;
        gateLampPool(SG.Gfx.mood);
        self._applyMood(self._follow && SG.Clock && SG.Clock.hour ? SG.Clock.hour() : self._moodWant != null ? self._moodWant : defaultHour());
        self._initGrass(); yield;
        // 按当前镜头预先补齐近处的细地形块（之后每帧按需补建）
        const cam = SG.Gfx.camera;
        for (let k = 0; k < 40 && self._lod.update(cam, 6) > 0; k++) { self._sub(0.3 + k / 60); yield; }
      })());
    }
    _sub(frac) { if (this._stage) this._prog = this._stage.base + this._stage.w * M.clamp01(frac); }

    _makeCityIndex(radius) { return makePointIndex(this._cityX, this._cityZ, radius); }
    _nearCity(x, z, r) {
      const list = this._cityIdx && this._cityIdx.at(x, z);
      if (!list) return false;
      for (const i of list) if (Math.hypot(this._cityX[i] - x, this._cityZ[i] - z) < r) return true;
      return false;
    }
    // 到最近城池的距离（只查 3.4 以内；更远返回 99）
    _cityNearDist(x, z) {
      const list = this._cityIdx && this._cityIdx.at(x, z);
      let d = 99;
      if (list) for (const i of list) d = Math.min(d, Math.hypot(this._cityX[i] - x, this._cityZ[i] - z));
      return d;
    }

    _dispose() {
      for (const m of this._marchers) m.cancelled = true;
      SG.Gfx.disposeTree(this.root);
      this.root = null;
      // 云的材质每次构建新建（每朵云一份公告板材质，共用贴图与着色器程序），随云一起释放
      for (const c of this._clouds) {
        c.material.dispose();
        if (c.customDepthMaterial) c.customDepthMaterial.dispose();
        for (const ch of c.children) if (ch.material) ch.material.dispose();
      }
      this._clouds = [];
      if (this._lod) { this._lod.dispose(); this._lod = null; }
      if (this._maps) { for (const k of ['n', 'c', 'm', 'oc', 'om', 'x', 'facet']) if (this._maps[k]) this._maps[k].dispose(); this._maps = null; }
      for (const m of this._ownMats || []) m.dispose();
      this._ownMats = [];
      if (this._lamps && SG.Gfx.removeLamp) for (const l of this._lamps) SG.Gfx.removeLamp(l);
      if (this._glow) { this._glow.b.dispose(); this._glow = null; }
      this._lamps = null; this._lampSpecs = null; this._cityScatter = null; this._lampsOn = undefined; this._poolWarm = false;
      this._endPrewarm();
      this._flags = null; this._rings = null; this._selectRing = null; this._debug = null; this._treeTiles = null; this._scatter = null; this._cityTiles = null; this._inkProps = null;
      this._grassT = null; this._flowerT = null; this._grassKey = null; this._grassJob = null;
      this._master = null; this._outer = null; this._tex = null; this._roadIdx = null; this._roadSegs = null; this._roadLines = null; this._cityBase = null;
      this._lanes = new Map();
      this._restoreFog();
    }

    // ------------------------------------------------------ 地形 --
    // 城外村落的位置（在生成农田纹理之前确定）：每城 1–3 处，优先沿出城的道路，离城 4.2–6.4，平地、不近水
    *_planVillages(state) {
      const out = [], rnd = SG.SeededRandom(29), G = this._geo, CA = SG.CultureArt;
      const n = state.cities.length;
      for (const c of state.cities) {
        const cx = this._cityX[c.id], cz = this._cityZ[c.id];
        const culture = CA ? CA.cultureOfCity(c) : 'han';
        const want = 1 + (c.town > 330 ? 1 : 0) + (c.town > 520 ? 1 : 0);
        const dirs = [];
        for (const li of c.links) if (li >= 0 && li < n && li !== c.id) dirs.push(Math.atan2(this._cityZ[li] - cz, this._cityX[li] - cx));
        while (dirs.length < 7) dirs.push(rnd.nextDouble() * Math.PI * 2);
        let placed = 0;
        for (let k = 0; k < dirs.length && placed < want; k++) {
          const a = dirs[k] + (rnd.nextDouble() - 0.5) * 0.35, r = 4.2 + rnd.nextDouble() * 2.2;
          const side = (rnd.nextDouble() < 0.5 ? -1 : 1) * (0.9 + rnd.nextDouble() * 0.6);
          const hx = cx + Math.cos(a) * r - Math.sin(a) * side, hz = cz + Math.sin(a) * r + Math.cos(a) * side;
          const h = this.height(hx, hz);
          if (h < 0.5 || h > 2.6 || G.inSea(hx, hz) || G.riverDist(hx, hz) / G.rW < 1.8) continue;
          if (Math.abs(this.height(hx + 0.9, hz) - this.height(hx - 0.9, hz)) > 0.45 || Math.abs(this.height(hx, hz + 0.9) - this.height(hx, hz - 0.9)) > 0.45) continue;
          let bad = false;
          for (let q = 0; q < n && !bad; q++) if (q !== c.id && Math.hypot(this._cityX[q] - hx, this._cityZ[q] - hz) < 4.2) bad = true;
          for (const o of out) if (!bad && Math.hypot(o.x - hx, o.z - hz) < 3.2) bad = true;
          if (bad) continue;
          out.push({ x: hx, z: hz, culture, n: 3 + rnd.next(4), yaw: a, city: c.id });
          placed++;
        }
        if ((c.id & 7) === 7) yield;
      }
      this._hamlets = out;
      this._hamletIdx = makePointIndex(out.map(o => o.x), out.map(o => o.z), 3.6);
    }
    // 农田权重 0–1：城郊、村落周围、大河两岸的平原（沙漠中的河谷为灌溉农田）
    _farmAt(x, z, h, gi, slope) {
      if (h < 0.42 || h > 2.6 || slope > 0.22) return 0;
      let w = 0;
      const cl = this._cityIdx10 && this._cityIdx10.at(x, z);
      // 第三版修订：草原上的城（毡帐营地）四周不是农田
      if (cl) for (const i of cl) { const d = Math.hypot(this._cityX[i] - x, this._cityZ[i] - z); w = Math.max(w, ss(8.5, 4.2, d) * ss(1.7, 2.9, d) * (1 - 0.85 * M.clamp01(gi.st * 1.4))); }
      const hl = this._hamletIdx && this._hamletIdx.at(x, z);
      if (hl) for (const i of hl) { const o = this._hamlets[i]; w = Math.max(w, ss(3.4, 1.0, Math.hypot(o.x - x, o.z - z))); }
      const rv = ss(5.0, 1.5, gi.river) * ss(2.0, 1.1, h);
      const k = M.clamp01(0.5 - 0.5 * gi.st - 0.45 * gi.bo + 0.55 * gi.des - 0.2 * gi.tr);
      w = Math.max(w, rv * k);
      return M.clamp01(w * (1 - gi.forest * 0.9) * (1 - gi.sand * 0.8 * (1 - rv)) * ss(0.22, 0.1, slope));
    }

    // 第四版：着色用的附加数据（1.25 间距，与群落纹理对齐）：R = 水田（农田里的稻作：东亚淮河以南、日本、热带），
    // G = 踩出的泥地（城墙外一圈、村落中间）。只影响着色，不影响任何规则数据
    *_paintExtra(state, X, x0, z0, step, w, hgt, mData) {
      const G = this._geo, japanX = SG.project ? SG.project(124.5, 35).x : 999;
      for (let j = 0; j < hgt; j++) {
        const z = z0 + j * step;
        for (let i = 0; i < w; i++) {
          const q = (j * w + i) * 4;
          if (mData[q + 3] > 250) continue;
          const x = x0 + i * step, f = G.sample(x, z);
          const east = f[F_EAST] * (x > japanX ? 0.8 : ss(62, 46, z));
          X[q] = b8(M.clamp01(Math.max(f[F_TROP] * 1.3, east * 1.1) * (1 - f[F_DESERT])));
        }
        if ((j & 63) === 63) yield;
      }
      const worn = (cx, cz, r0, r1, k) => {
        const i0 = Math.max(0, Math.floor((cx - r1 - x0) / step)), i1 = Math.min(w - 1, Math.ceil((cx + r1 - x0) / step));
        const j0 = Math.max(0, Math.floor((cz - r1 - z0) / step)), j1 = Math.min(hgt - 1, Math.ceil((cz + r1 - z0) / step));
        for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
          const d = Math.hypot(x0 + i * step - cx, z0 + j * step - cz), v = k * ss(r1, r0, d), q = (j * w + i) * 4 + 1;
          if (v * 255 > X[q]) X[q] = b8(v);
        }
      };
      for (const c of state.cities) {
        const size = M.lerp(0.9, 1.5, M.inverseLerp(180, 650, c.town));
        worn(this._cityX[c.id], this._cityZ[c.id], size * 1.3 + 0.2, size * 1.3 + 1.3, 0.75);
        if ((c.id & 31) === 31) yield;
      }
      // 修订：村落脚下一片踩实的泥地（半径约 1.3；着色器里泥地也压掉农田，村舍不再落在庄稼上）
      for (const o of this._hamlets || []) worn(o.x, o.z, 0.9, 1.75, 0.62);
      yield;
      // A = 城池压平 / 视线楔形削出的坡（人工的路堑与台地）：着色时不当作岩壁、裸土，仍是草坡
      for (const c of state.cities) {
        const cx = this._cityX[c.id], cz = this._cityZ[c.id], size = M.lerp(0.9, 1.5, M.inverseLerp(180, 650, c.town));
        const R1 = size * 1.3 + 0.35, R2 = R1 + 1.6;
        const i0 = Math.max(0, Math.floor((cx - R2 - 4 - x0) / step)), i1 = Math.min(w - 1, Math.ceil((cx + R2 + 4 - x0) / step));
        const j0 = Math.max(0, Math.floor((cz - 11 - z0) / step)), j1 = Math.min(hgt - 1, Math.ceil((cz + R2 + 1.5 - z0) / step));
        for (let j = j0; j <= j1; j++) for (let i = i0; i <= i1; i++) {
          const dx = x0 + i * step - cx, dz = z0 + j * step - cz, d = Math.hypot(dx, dz);
          let v = ss(R2 + 1.6, R2, d);
          if (dz < 0) v = Math.max(v, ss(R1 + 3.5, R1 + 1.5, Math.abs(dx)) * ss(-11, -8, dz));
          const q = (j * w + i) * 4 + 3;
          if (v * 255 > X[q]) X[q] = b8(v);
        }
        if ((c.id & 31) === 31) yield;
      }
      // B = 沙滩的「附近有水」权重（四邻最低处的高度 0.03 → 0.2 由 1 渐到 0；高于 0.75 的地方用不到，记 1）。
      // 原来着色器每个低地像素取四次高度图；这里按主高度网格（间距 res = step / 2）预先算好
      const m = this._master;
      if (m) {
        const mh = m.h, W = m.nx, Hn = m.nz, r = step / m.res;
        for (let j = 0; j < hgt; j++) {
          const J = Math.min(Hn - 1, Math.round(j * r));
          for (let i = 0; i < w; i++) {
            const I = Math.min(W - 1, Math.round(i * r)), q = (j * w + i) * 4 + 2;
            if (mh[J * W + I] > 0.75) { X[q] = 255; continue; }
            const hmin = Math.min(mh[J * W + Math.max(0, I - 1)], mh[J * W + Math.min(W - 1, I + 1)], mh[Math.max(0, J - 1) * W + I], mh[Math.min(Hn - 1, J + 1) * W + I]);
            X[q] = b8(1 - ss(0.03, 0.2, hmin));
          }
          if ((j & 63) === 63) yield;
        }
      }
    }

    // 第三版：城池占地压平（城基 = 中心高度，半径 size·1.3 + 0.35 内完全压平，再 1.6 单位平滑过渡），
    // 并在镜头一侧（地图 −z，默认偏航 0 时镜头在南）的楔形内限高：限高 = 城基 + 0.2 + 0.72 × 距离 + 横向超出城宽部分的平方，
    // 视线俯角约 43°–50°，城体与城楼不再被前方的山脊挡住
    *_fitCities(state) {
      const m = this._master, mh = m.h, W = m.nx, res = m.res;
      const n = state.cities.length;
      const base = this._cityBase = new Float32Array(n);
      const at = (x, z) => {
        const fx = M.clamp((x - m.x0) / res, 0, m.nx - 1.001), fz = M.clamp((z - m.z0) / res, 0, m.nz - 1.001);
        const i = Math.floor(fx), j = Math.floor(fz), tx = fx - i, tz = fz - j, o = j * W + i;
        return (mh[o] * (1 - tx) + mh[o + 1] * tx) * (1 - tz) + (mh[o + W] * (1 - tx) + mh[o + W + 1] * tx) * tz;
      };
      const TAN = 0.72, AHEAD = 10;
      for (const c of state.cities) {
        const cx = this._cityX[c.id], cz = this._cityZ[c.id];
        const size = M.lerp(0.9, 1.5, M.inverseLerp(180, 650, c.town));
        const y = Math.max(0.3, at(cx, cz));
        base[c.id] = y;
        const R1 = size * 1.3 + 0.35, R2 = R1 + 1.6;
        const i0 = Math.max(0, Math.floor((cx - R2 - 3 - m.x0) / res)), i1 = Math.min(m.nx - 1, Math.ceil((cx + R2 + 3 - m.x0) / res));
        const j0 = Math.max(0, Math.floor((cz - AHEAD - m.z0) / res)), j1 = Math.min(m.nz - 1, Math.ceil((cz + R2 - m.z0) / res));
        for (let j = j0; j <= j1; j++) {
          const z = m.z0 + j * res, dz = z - cz;
          for (let i = i0; i <= i1; i++) {
            const x = m.x0 + i * res, dx = x - cx, o = j * W + i;
            let h = mh[o];
            const d = Math.sqrt(dx * dx + dz * dz);
            // 河流、海湾在城体以外不填平（水面一直流到城墙下）
            if (d < R2 && (h >= 0.06 || d < R1 * 0.8)) h = M.lerp(h, y, ss(R2, R1, d));
            if (dz < 0) {
              const e = Math.max(0, Math.abs(dx) - R1);
              const cap = y + 0.2 + TAN * -dz + e * e * 0.9;
              if (h > cap - 0.6) h = smin(h, cap, 0.6);
            }
            mh[o] = h;
          }
        }
        if ((c.id & 15) === 15) yield;
      }
    }

    *_genTerrain(state) {
      const T = this.spec.terrain, step = T.step, S = this.spec.skirt, G = this._geo;
      this._traceTag = 'terrain.plan';
      yield* this._planVillages(state);
      // 1) 高度栅格（1.25）
      const x0 = T.x0, z0 = T.z0;
      const nx = Math.ceil((T.x1 - x0) / step), nz = Math.ceil((T.z1 - z0) / step), W = nz + 1;
      const hs = new Float32Array((nx + 1) * W);
      this._traceTag = 'terrain.h';
      for (let i = 0; i <= nx; i++) {
        for (let j = 0; j <= nz; j++) {
          hs[i * W + j] = this.height(x0 + i * step, z0 + j * step);
          if ((j & 127) === 127) yield;
        }
        this._sub(0.3 * i / nx);
        yield;
      }
      // 1b) 第三版：按坡度加权的平滑（两遍）：陡坡、尖峰、刀切的岸壁向邻点均值靠拢；平地与水面（海、河、湖）不动
      this._traceTag = 'terrain.smooth';
      for (let pass = 0; pass < 2; pass++) yield* slopeSmooth(hs, nx + 1, nz + 1, W, 1, 0.5, 1.5, 0.8);
      // 2) 远景栅格（5.0），与主网格对齐；落在高度栅格内的点直接取值
      const R = OUTER_RES;
      const ox0 = x0 - Math.ceil((x0 - S.x0) / R) * R, oz0 = z0 - Math.ceil((z0 - S.z0) / R) * R;
      const onx = Math.ceil((S.x1 - ox0) / R) + 1, onz = Math.ceil((S.z1 - oz0) / R) + 1;
      const oh = new Float32Array(onx * onz);
      this._traceTag = 'terrain.outer';
      for (let j = 0; j < onz; j++) {
        const z = oz0 + j * R, hj = Math.round((z - z0) / step);
        for (let i = 0; i < onx; i++) {
          const x = ox0 + i * R, hi = Math.round((x - x0) / step);
          oh[j * onx + i] = hi >= 0 && hj >= 0 && hi <= nx && hj <= nz ? hs[hi * W + hj] : this.height(x, z);
        }
        if ((j & 3) === 3) { this._sub(0.3 + 0.06 * j / onz); yield; }
      }
      this._outer = { x0: ox0, z0: oz0, res: R, nx: onx, nz: onz, h: oh };
      // 3) 主网格（0.625）：Catmull-Rom 二倍上采样（可分离），边缘 12 单位内过渡到远景栅格，再加细节噪声（山地起伏更碎、平原轻微）
      const res = step / 2, mnx = 2 * nx + 1, mnz = 2 * nz + 1, mh = new Float32Array(mnx * mnz);
      const CR = (a, b, c, d) => (9 * (b + c) - a - d) / 16;
      this._traceTag = 'terrain.master';
      for (let j = 0; j <= nz; j++) {
        const row = 2 * j * mnx;
        for (let i = 0; i <= nx; i++) {
          mh[row + 2 * i] = hs[i * W + j];
          if (i < nx) mh[row + 2 * i + 1] = CR(hs[Math.max(0, i - 1) * W + j], hs[i * W + j], hs[(i + 1) * W + j], hs[Math.min(nx, i + 2) * W + j]);
        }
        if ((j & 7) === 7) yield;
      }
      for (let j = 0; j < nz; j++) {
        const r0 = 2 * Math.max(0, j - 1) * mnx, r1 = 2 * j * mnx, r2 = 2 * (j + 1) * mnx, r3 = 2 * Math.min(nz, j + 2) * mnx, ro = (2 * j + 1) * mnx;
        for (let i = 0; i < mnx; i++) mh[ro + i] = CR(mh[r0 + i], mh[r1 + i], mh[r2 + i], mh[r3 + i]);
        if ((j & 7) === 7) yield;
      }
      const X1 = x0 + nx * step, Z1 = z0 + nz * step, BL = 12;
      for (let j = 0; j < mnz; j++) {
        const z = z0 + j * res, row = j * mnx;
        for (let i = 0; i < mnx; i++) {
          const x = x0 + i * res, o = row + i;
          let h = mh[o];
          const e = Math.min(x - x0, X1 - x, z - z0, Z1 - z);
          if (e < BL) h = M.lerp(this._outerH(x, z), h, ss(0, BL, e));
          // 第三版（修订）：细节噪声限带——波长都在 2.7 单位以上（0.625 的网格表达得了），免得混叠成逐点的尖刺；
          // 脊状噪声的脊线圆滑（√(d² + ε)），不再是刀刃
          let a = (0.035 + 0.15 * ss(1.0, 2.8, h) + 0.16 * ss(2.8, 5.5, h)) * ss(0.25, 0.9, h);
          if (a > 0.002 && h >= 0.06) {
            const cd = this._cityNearDist(x, z);
            if (cd < 3.4) a *= ss(1.8, 3.4, cd);
            h += a * 2.2 * ((M.perlinNoise(x * 0.36 + 3.1, z * 0.36 + 7.7) - 0.465) + 0.6 * (M.perlinNoise(x * 0.19 + 11.3, z * 0.19 + 5.1) - 0.465));
            // 山地：圆脊噪声（山脊、沟壑）
            const am = 0.4 * ss(2.2, 5.0, h);
            if (am > 0.01) {
              const d1 = (M.perlinNoise(x * 0.24 + 21.7, z * 0.24 + 9.4) - 0.465) * 3.2;
              const d2 = (M.perlinNoise(x * 0.37 + 4.3, z * 0.37 + 31.9) - 0.465) * 3.2;
              const r = 1 - Math.min(1, Math.sqrt(d1 * d1 + 0.16) - 0.4), r2 = 1 - Math.min(1, Math.sqrt(d2 * d2 + 0.16) - 0.4);
              h += am * ((r * r - 0.45) * 1.6 + (r2 * r2 - 0.45) * 0.45) * (cd < 3.4 ? ss(1.8, 3.4, cd) : 1);
            }
            if (h < 0.06) h = 0.06;
          }
          mh[o] = h;
        }
        if ((j & 1) === 1) { this._sub(0.36 + 0.1 * j / mnz); yield; }
      }
      // 3b) 去尖：曲率大的点（尖峰、折痕）向邻点均值靠拢，两遍
      for (let pass = 0; pass < 2; pass++) { yield* curveSmooth(mh, mnz, mnx, mnx, 1, 0.1, 0.4, 0.8); this._sub(0.46 + 0.02 * pass); }
      this._master = { x0, z0, res, nx: mnx, nz: mnz, h: mh };
      // 3c) 城池：占地压平到城基高度；镜头一侧（−z）的视线楔形内限高，免得山脊挡住城池
      this._traceTag = 'terrain.cities';
      yield* this._fitCities(state);
      // 4) 数据纹理：法线 / 凹凸遮蔽 / 高度（主网格分辨率）
      this._traceTag = 'terrain.normals';
      const nData = new Uint8Array(mnx * mnz * 4);
      const blur = mh.slice();
      yield* boxBlur(blur, mnx, mnz, 3, 2);
      const inv = 1 / (2 * res);
      for (let j = 0; j < mnz; j++) {
        const row = j * mnx, jm = Math.max(0, j - 1) * mnx, jp = Math.min(mnz - 1, j + 1) * mnx;
        for (let i = 0; i < mnx; i++) {
          const o = row + i, q = o * 4;
          const hx = (mh[row + Math.min(mnx - 1, i + 1)] - mh[row + Math.max(0, i - 1)]) * inv;
          const hz = (mh[jp + i] - mh[jm + i]) * inv;
          const l = 1 / Math.sqrt(hx * hx + 1 + hz * hz);
          nData[q] = b8(0.5 - 0.5 * hx * l); nData[q + 1] = b8(0.5 + 0.5 * hz * l);
          nData[q + 2] = b8(0.5 + (mh[o] - blur[o]) * 0.9);
          nData[q + 3] = b8((mh[o] + 4) / 8);
        }
        if ((j & 15) === 15) { this._sub(0.5 + 0.08 * j / mnz); yield; }
      }
      // 群落底色 + 林地（C）、材质权重（M：沙漠、荒芜、积雪、1 − 农田），1.25 间距
      this._traceTag = 'terrain.biome';
      const cw = nx + 1, chh = nz + 1, cData = new Uint8Array(cw * chh * 4), mData = new Uint8Array(cw * chh * 4);
      for (let j = 0; j < chh; j++) {
        const z = z0 + j * step, mj = 2 * j;
        for (let i = 0; i < cw; i++) {
          const x = x0 + i * step, mi = 2 * i, mo = mj * mnx + mi;
          const h = mh[mo];
          const hx = (mh[mj * mnx + Math.min(mnx - 1, mi + 1)] - mh[mj * mnx + Math.max(0, mi - 1)]) * inv;
          const hz = (mh[Math.min(mnz - 1, mj + 1) * mnx + mi] - mh[Math.max(0, mj - 1) * mnx + mi]) * inv;
          const slope = 1 - 1 / Math.sqrt(1 + hx * hx + hz * hz);
          const gi = groundInfo(G, x, z, h, false);
          const farm = h > 0.4 ? this._farmAt(x, z, h, gi, slope) : 0;
          const q = (j * cw + i) * 4;
          cData[q] = b8(gi.r); cData[q + 1] = b8(gi.g); cData[q + 2] = b8(gi.b); cData[q + 3] = b8(gi.forest);
          mData[q] = b8(gi.sand); mData[q + 1] = b8(gi.barren); mData[q + 2] = b8(gi.snow); mData[q + 3] = b8(1 - farm);
          if ((i & 255) === 255) yield;
        }
        this._sub(0.58 + 0.28 * j / chh);
        yield;
      }
      // 远景的底色（A = 高度，供水面计算深浅）与材质权重
      this._traceTag = 'terrain.outerTex';
      const ocData = new Uint8Array(onx * onz * 4), omData = new Uint8Array(onx * onz * 4);
      for (let j = 0; j < onz; j++) {
        const z = oz0 + j * R;
        for (let i = 0; i < onx; i++) {
          const x = ox0 + i * R, h = oh[j * onx + i], q = (j * onx + i) * 4;
          const gi = groundInfo(G, x, z, h, true);
          ocData[q] = b8(gi.r); ocData[q + 1] = b8(gi.g); ocData[q + 2] = b8(gi.b); ocData[q + 3] = b8((h + 4) / 8);
          omData[q] = b8(gi.sand); omData[q + 1] = b8(gi.barren); omData[q + 2] = b8(gi.snow); omData[q + 3] = 255;
        }
        if ((j & 7) === 7) { this._sub(0.86 + 0.04 * j / onz); yield; }
      }
      // 第四版：附加数据（R 水田权重、G 城郊与村落踩出的泥地），1.25 间距；只用于着色
      this._traceTag = 'terrain.extra';
      const xData = new Uint8Array(cw * chh * 4);
      yield* this._paintExtra(state, xData, x0, z0, step, cw, chh, mData);
      // 第四版：固定三角格的面法线：每格两个三角形的法线 xz（three 坐标），着色器按此平面着色山坡。
      // 第四轮：等边三角格（边长 FACET_SIDE，基向量 FACET_E1 / FACET_E2 与 x 轴成 15° / 75°，每格沿 (1,0)–(0,1) 对角线分开），
      // 不再是与坐标轴对齐的方格（镜头不转时陡坡上显出竖直 / 水平的方块）。格点高度取主网格高度（0.625 间距）的双线性插值；
      // 纹理覆盖内区四角在格坐标里的外包框，facetXf.xy = 纹理第一格的格点（世界坐标），与 art.js 的 sgFp 一致
      this._traceTag = 'terrain.facets';
      const [e1x, e1z] = FACET_E1, [e2x, e2z] = FACET_E2, side = FACET_SIDE, fdet = e1x * e2z - e1z * e2x;
      const toLat = (x, z) => [(e2z * (x - x0) - e2x * (z - z0)) / (fdet * side), (-e1z * (x - x0) + e1x * (z - z0)) / (fdet * side)];
      let fu0 = Infinity, fv0 = Infinity, fu1 = -Infinity, fv1 = -Infinity;
      for (const [cx, cz] of [[x0, z0], [x0 + nx * step, z0], [x0, z0 + nz * step], [x0 + nx * step, z0 + nz * step]]) {
        const [u, v] = toLat(cx, cz);
        fu0 = Math.min(fu0, u); fv0 = Math.min(fv0, v); fu1 = Math.max(fu1, u); fv1 = Math.max(fv1, v);
      }
      fu0 = Math.floor(fu0) - 1; fv0 = Math.floor(fv0) - 1;
      const fw = Math.ceil(fu1) + 1 - fu0, fh = Math.ceil(fv1) + 1 - fv0, fData = new Uint8Array(fw * fh * 4);
      const fpx = x0 + side * (fu0 * e1x + fv0 * e2x), fpz = z0 + side * (fu0 * e1z + fv0 * e2z);
      {
        // 格点 (i, j)（相对纹理原点）的世界坐标与高度
        const LW = fw + 1, lx = new Float32Array(LW * (fh + 1)), lz = new Float32Array(LW * (fh + 1)), lh = new Float32Array(LW * (fh + 1));
        const mxMax = mnx - 1, mzMax = mnz - 1;
        for (let j = 0; j <= fh; j++) {
          for (let i = 0; i <= fw; i++) {
            const x = fpx + side * (i * e1x + j * e2x), z = fpz + side * (i * e1z + j * e2z), o = j * LW + i;
            const gx = M.clamp((x - x0) / res, 0, mxMax), gz = M.clamp((z - z0) / res, 0, mzMax);
            const gi = Math.min(mxMax - 1, Math.floor(gx)), gj = Math.min(mzMax - 1, Math.floor(gz)), tx = gx - gi, tz = gz - gj, r0 = gj * mnx + gi;
            lx[o] = x; lz[o] = z;
            lh[o] = (mh[r0] * (1 - tx) + mh[r0 + 1] * tx) * (1 - tz) + (mh[r0 + mnx] * (1 - tx) + mh[r0 + mnx + 1] * tx) * tz;
          }
          if ((j & 63) === 63) yield;
        }
        // 三个格点所在平面的坡度（地图坐标的 dh/dx、dh/dz）→ three 坐标的法线 xz
        const put = (q, a, b, c) => {
          const d1x = lx[b] - lx[a], d1z = lz[b] - lz[a], d2x = lx[c] - lx[a], d2z = lz[c] - lz[a], dh1 = lh[b] - lh[a], dh2 = lh[c] - lh[a];
          const det = d1x * d2z - d1z * d2x, hx = (dh1 * d2z - dh2 * d1z) / det, hz = (d1x * dh2 - d2x * dh1) / det;
          const l = 1 / Math.sqrt(hx * hx + 1 + hz * hz);
          fData[q] = b8(0.5 - 0.5 * hx * l); fData[q + 1] = b8(0.5 + 0.5 * hz * l);
        };
        for (let j = 0; j < fh; j++) {
          for (let i = 0; i < fw; i++) {
            const o00 = j * LW + i, o10 = o00 + 1, o01 = o00 + LW, o11 = o01 + 1, q = (j * fw + i) * 4;
            put(q, o00, o10, o01);        // f.x + f.y < 1
            put(q + 2, o11, o01, o10);    // f.x + f.y ≥ 1
          }
          if ((j & 31) === 31) yield;
        }
      }
      this._tex = { x0, z0, step, w: cw, h: chh, c: cData, m: mData, x: xData };
      const mip = !!(SG.Gfx.renderer && SG.Gfx.renderer.capabilities.isWebGL2);
      const tex = (data, w, h) => {
        const t = new THREE.DataTexture(data, w, h, THREE.RGBAFormat);
        t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
        t.magFilter = THREE.LinearFilter;
        t.minFilter = mip ? THREE.LinearMipmapLinearFilter : THREE.LinearFilter;
        t.generateMipmaps = mip;
        t.needsUpdate = true;
        return t;
      };
      const xf = (gx0, gz0, r, w, h) => new THREE.Vector4((0.5 - gx0 / r) / w, (0.5 - gz0 / r) / h, 1 / (r * w), 1 / (r * h));
      const facetTex = new THREE.DataTexture(fData, fw, fh, THREE.RGBAFormat);
      facetTex.wrapS = facetTex.wrapT = THREE.ClampToEdgeWrapping;
      facetTex.magFilter = facetTex.minFilter = THREE.NearestFilter;
      facetTex.generateMipmaps = false;
      facetTex.needsUpdate = true;
      const maps = this._maps = {
        n: tex(nData, mnx, mnz), c: tex(cData, cw, chh), m: tex(mData, cw, chh), oc: tex(ocData, onx, onz), om: tex(omData, onx, onz),
        x: tex(xData, cw, chh), facet: facetTex,
        xfN: xf(x0, z0, res, mnx, mnz), xfC: xf(x0, z0, step, cw, chh), xfO: xf(ox0, oz0, R, onx, onz), inner: new THREE.Vector4(x0, z0, X1, Z1),
        facetXf: new THREE.Vector4(fpx, fpz, side, 0), facetSz: new THREE.Vector4(1 / fw, 1 / fh, FACET_K, FACET_JITTER),
      };
      // 5) 细节纹理、地形材质、四叉树（预建到第 2 级，之后按镜头补建）
      this._traceTag = 'terrain.detail';
      yield* SG.Gfx.terrainDetailGen();
      if (SG.Gfx.paintDetailGen) { this._traceTag = 'terrain.paint'; yield* SG.Gfx.paintDetailGen(); }
      if (SG.LP && SG.LP.grassDetail) { SG.LP.grassDetail(); yield; }
      const lod = this._lod = new TerrainLOD(this);
      const lpMat = SG.Gfx.lpTerrainMaterial && SG.Gfx.lpTerrainMaterial({ maps, paint: PAINT_K, debug: SG.MapView.debugTerrain || null });
      lod.mat = lpMat || SG.Gfx.terrainMaterial({ maps });
      lod.lpTerrain = !!lpMat;
      lod.init(x0, z0, { x0: S.x0, z0: S.z0, x1: S.x1, z1: S.z1 }, { x0, z0, x1: X1, z1: Z1 });
      this.root.add(lod.group);
      this._traceTag = 'terrain.nodes';
      let k = 0;
      for (const _ of lod.prebuild(2)) { if ((++k & 3) === 0) this._sub(0.9 + Math.min(0.1, k / 600)); yield; }
      // 远海海床：深水下方（与深海地形同高），免得透过水面看到天空
      const mb = new SG.MeshBuilder();
      const bed = new THREE.Color(SEABED[0] * 0.8, SEABED[1] * 0.85, SEABED[2] * 0.9);
      const E = 1600, by = -4.62, cx = (S.x0 + S.x1) / 2, cz = (S.z0 + S.z1) / 2;
      mb.quad(V(cx - E, by, cz - E), V(cx - E, by, cz + E), V(cx + E, by, cz + E), V(cx + E, by, cz - E), bed);
      const bm = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly(), { castShadow: false, receiveShadow: true });
      bm.name = 'SeaBed';
      bm.frustumCulled = false;
      this.root.add(bm);
    }

    *_genWater() {
      const Wt = this.spec.water;
      const step = Wt.step, x0 = Wt.x0, z0 = Wt.z0;
      const nx = Math.ceil((Wt.x1 - x0) / step), nz = Math.ceil((Wt.z1 - z0) / step), W = nz + 1;
      const ex1 = x0 + nx * step, ez1 = z0 + nz * step;
      const hg = new Float32Array((nx + 1) * W);
      this._traceTag = 'water.h';
      for (let i = 0; i <= nx; i++) {
        for (let j = 0; j <= nz; j++) hg[i * W + j] = this.surfaceY(x0 + i * step, z0 + j * step);
        if ((i & 7) === 7) { this._sub(0.5 * i / nx); yield; }
      }
      const mat = mapWaterMaterial(this._lod.mat.userData.terrainUniforms);
      this._ownMats.push(mat);
      let pos = [];
      // 一个格子（Unity 顶点 a(xa,za) b(xa,za+sz) c(xa+sx,za+sz) d(xa+sx,za)），平面 y = 0
      const quad = (xa, za, sx, sz) => {
        pos.push(xa, 0, -za, xa + sx, 0, -(za + sz), xa, 0, -(za + sz), xa, 0, -za, xa + sx, 0, -za, xa + sx, 0, -(za + sz));
      };
      const flush = name => {
        if (!pos.length) return;
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        g.computeBoundingSphere();
        const m = new THREE.Mesh(g, mat);
        m.name = name;
        m.renderOrder = -10;
        m.castShadow = false; m.receiveShadow = false;
        this.root.add(m);
        pos = [];
      };
      const tile = Wt.tile, LAND = 1.2;
      const land = (ia, ja, ib, jb) => hg[ia * W + ja] > LAND && hg[ib * W + jb] > LAND;
      this._traceTag = 'water.tiles';
      for (let ti = 0; ti < nx; ti += tile) {
        for (let tj = 0; tj < nz; tj += tile) {
          for (let i = ti; i < Math.min(nx, ti + tile); i++)
            for (let j = tj; j < Math.min(nz, tj + tile); j++) {
              if (hg[i * W + j] > LAND && hg[(i + 1) * W + j] > LAND && hg[i * W + j + 1] > LAND && hg[(i + 1) * W + j + 1] > LAND) continue;
              quad(x0 + i * step, z0 + j * step, step, step);
            }
          yield;
          flush('Water');
          yield;
        }
        this._sub(0.5 + 0.4 * ti / nx);
      }
      // 外海第一圈（宽 band，沿内圈边界按 step 细分）与最外圈的四个矩形
      const band = Wt.band, far = Wt.far;
      for (let i = 0; i < nx; i++) {
        const xa = x0 + i * step;
        if (!land(i, 0, i + 1, 0)) quad(xa, z0 - band, step, band);
        if (!land(i, nz, i + 1, nz)) quad(xa, ez1, step, band);
      }
      const nb = Math.round(band / step);
      for (let j = -nb; j < nz + nb; j++) {
        const za = z0 + j * step, onEdge = j >= 0 && j < nz;
        if (!(onEdge && land(0, j, 0, j + 1))) quad(x0 - band, za, band, step);
        if (!(onEdge && land(nx, j, nx, j + 1))) quad(ex1, za, band, step);
      }
      const bx0 = x0 - band, bz0 = z0 - band, bx1 = ex1 + band, bz1 = ez1 + band;
      const rect = (ax, az, bx, bz) => {
        const cx = Math.max(1, Math.round((bx - ax) / 20)), cz = Math.max(1, Math.round((bz - az) / 20));
        const sx = (bx - ax) / cx, sz = (bz - az) / cz;
        for (let i = 0; i < cx; i++) for (let j = 0; j < cz; j++) quad(ax + i * sx, az + j * sz, sx, sz);
      };
      rect(bx0 - far, bz0 - far, bx0, bz1 + far);
      rect(bx1, bz0 - far, bx1 + far, bz1 + far);
      rect(bx0, bz0 - far, bx1, bz0);
      rect(bx0, bz1, bx1, bz1 + far);
      flush('WaterFar');
      yield;
    }

    // 植被 / 石块 / 村舍的实例化网格：按 100 单位的区块与种类分组（视锥剔除、远近两级 LOD）
    _instBuckets(tileSize) {
      const T = this.spec.terrain, tile = tileSize || 100, map = new Map();
      return {
        map,
        add(kind, px, py, pz, sx, sy, yaw, r, g, b) {
          const key = Math.floor((px - T.x0) / tile) + ',' + Math.floor((pz - T.z0) / tile) + ',' + kind;
          let bk = map.get(key);
          if (!bk) { bk = { kind, m: [], c: [], cx: (Math.floor((px - T.x0) / tile) + 0.5) * tile + T.x0, cz: (Math.floor((pz - T.z0) / tile) + 0.5) * tile + T.z0 }; map.set(key, bk); }
          bk.m.push(px, py, pz, sx, sy, yaw);
          bk.c.push(r, g, b);
        },
      };
    }
    // 生成实例化网格并登记到 tiles：geoFn(kind, lod) → BufferGeometry。
    // 每个区块 × 种类两个实例化网格（近景 lod 0 / 远景 lod 1），实例按到镜头的距离分到两边（见 _updateInstLOD）
    *_instMeshes(buckets, geoFn, mat, name, tiles, shadow) {
      const q = new THREE.Quaternion(), sc = new THREE.Vector3(), tp = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
      for (const b of buckets.map.values()) {
        const n = b.c.length / 3;
        const mats = new Float32Array(n * 16), cols = Float32Array.from(b.c), xz = new Float32Array(n * 2);
        let r = 0;
        for (let i = 0; i < n; i++) {
          const o = i * 6;
          tp.set(b.m[o], b.m[o + 1], -b.m[o + 2]);
          q.setFromAxisAngle(up, b.m[o + 5]);
          sc.set(b.m[o + 3], b.m[o + 4], b.m[o + 3]);
          _m4.compose(tp, q, sc);
          _m4.toArray(mats, i * 16);
          xz[i * 2] = b.m[o]; xz[i * 2 + 1] = b.m[o + 2];
          r = Math.max(r, Math.hypot(b.m[o] - b.cx, b.m[o + 2] - b.cz));
        }
        const mk = lod => {
          const im = new THREE.InstancedMesh(geoFn(b.kind, lod), mat, n);
          im.name = name + (lod ? 'Far' : '');
          im.userData.sharedGeometry = true;
          im.castShadow = shadow && !lod; im.receiveShadow = true;
          im.instanceMatrix.array.set(mats);
          im.setColorAt(0, _col1);
          im.instanceColor.array.set(cols);
          im.instanceMatrix.needsUpdate = true; im.instanceColor.needsUpdate = true;
          im.computeBoundingSphere();
          im.count = lod ? n : 0;
          this.root.add(im);
          return im;
        };
        tiles.push({ near: mk(0), far: mk(1), n, mats, cols, xz, cx: b.cx, cz: b.cz, r: r + 1, mode: 'far', key: '' });
        yield;
      }
    }
    // 第四版：同上，用 LP 实例化网格（近景带实例化墨线、可投影；远景用道具库的远景细节，不描墨线、不投影）。
    // 实例颜色为乘在道具原色上的调色（≈ 1）
    *_lpInstMeshes(buckets, geoFn, name, tiles, shadow) {
      const q = new THREE.Quaternion(), sc = new THREE.Vector3(), tp = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
      for (const b of buckets.map.values()) {
        const n = b.c.length / 3;
        const mats = new Float32Array(n * 16), cols = Float32Array.from(b.c), xz = new Float32Array(n * 2);
        let r = 0;
        for (let i = 0; i < n; i++) {
          const o = i * 6;
          tp.set(b.m[o], b.m[o + 1], -b.m[o + 2]);
          q.setFromAxisAngle(up, b.m[o + 5]);
          sc.set(b.m[o + 3], b.m[o + 4], b.m[o + 3]);
          _m4.compose(tp, q, sc);
          _m4.toArray(mats, i * 16);
          xz[i * 2] = b.m[o]; xz[i * 2 + 1] = b.m[o + 2];
          r = Math.max(r, Math.hypot(b.m[o] - b.cx, b.m[o + 2] - b.cz));
        }
        const sph = new THREE.Sphere(new THREE.Vector3(b.cx, 1, -b.cz), r + 2);
        const mk = lod => {
          const im = lpInst(geoFn(b.kind, lod), n, { outline: !lod, shadow: shadow && !lod });
          im.name = name + (lod ? 'Far' : '');
          im.instanceMatrix.array.set(mats);
          im.instanceColor.array.set(cols);
          im.instanceMatrix.needsUpdate = true; im.instanceColor.needsUpdate = true;
          setInstBounds(im, sph);
          setInstCount(im, lod ? n : 0);
          this.root.add(im);
          return im;
        };
        tiles.push({ near: mk(0), far: mk(1), n, mats, cols, xz, cx: b.cx, cz: b.cz, r: r + 1, mode: 'far', key: '' });
        yield;
      }
    }
    // 修订：石块、灌丛、村舍——每个 100 单位的区块只有一个合并的远景网格（不论种类，一次绘制；实例调色烘进顶点色），
    // 近景（nearR 以内的实例）照旧是每种一个 LP 实例化网格（墨线；投影按 o.shadow(kind)）。远景合并网格里正用近景模型的实例
    // 由逐顶点的 aHide 挪走（_mergedTileLOD）。nearGeoFn / farGeoFn(kind) → 几何体（共用，勿释放）
    *_lpMergedTiles(buckets, nearGeoFn, farGeoFn, name, tiles, o) {
      o = o || {};
      const wind = o.wind != null ? o.wind : 0, farMat = lpFarMaterial(wind);
      if (!farMat) { yield* this._lpInstMeshes(buckets, (k, lod) => (lod ? farGeoFn(k) : nearGeoFn(k)), name, tiles, !!o.shadow); return; }
      const q = new THREE.Quaternion(), sc = new THREE.Vector3(), tp = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
      const byTile = new Map(), kinds = new Set();
      for (const b of buckets.map.values()) {
        const key = b.cx + ',' + b.cz;
        let t = byTile.get(key);
        if (!t) { t = { cx: b.cx, cz: b.cz, groups: [] }; byTile.set(key, t); }
        t.groups.push(b);
        kinds.add(b.kind);
      }
      // 道具库的几何体第一次用到时才建（每种几十毫秒）：先逐种建好，每种一个分片
      for (const k of kinds) { nearGeoFn(k); yield; farGeoFn(k); yield; }
      for (const t of byTile.values()) {
        let n = 0, r = 0;
        for (const b of t.groups) n += b.c.length / 3;
        const mats = new Float32Array(n * 16), cols = new Float32Array(n * 3), xz = new Float32Array(n * 2), gi = new Uint8Array(n);
        const vstart = new Uint32Array(n), vcount = new Uint32Array(n), entries = [], near = [];
        let i = 0, nv = 0;
        t.groups.forEach((b, g) => {
          const m = b.c.length / 3, fg = farGeoFn(b.kind), fc = lpMergeable(fg) ? fg.attributes.position.count : 0;
          for (let j = 0; j < m; j++, i++) {
            const k6 = j * 6;
            tp.set(b.m[k6], b.m[k6 + 1], -b.m[k6 + 2]);
            q.setFromAxisAngle(up, b.m[k6 + 5]);
            sc.set(b.m[k6 + 3], b.m[k6 + 4], b.m[k6 + 3]);
            const mx = new THREE.Matrix4().compose(tp, q, sc);
            mx.toArray(mats, i * 16);
            cols[i * 3] = b.c[j * 3]; cols[i * 3 + 1] = b.c[j * 3 + 1]; cols[i * 3 + 2] = b.c[j * 3 + 2];
            xz[i * 2] = b.m[k6]; xz[i * 2 + 1] = b.m[k6 + 2]; gi[i] = g;
            r = Math.max(r, Math.hypot(b.m[k6] - t.cx, b.m[k6 + 2] - t.cz));
            if (fc) entries.push({ geo: fg, matrix: mx, tint: [b.c[j * 3], b.c[j * 3 + 1], b.c[j * 3 + 2]] });
            vstart[i] = nv; vcount[i] = fc; nv += fc;
          }
          const im = lpInst(nearGeoFn(b.kind), m, { outline: o.outline !== false, shadow: o.shadow ? !!o.shadow(b.kind) : false, wind: o.wind });
          im.name = name;
          im.visible = false;
          near.push(im);
          this.root.add(im);
        });
        yield;
        let far = null, hide = null;
        if (entries.length) {
          const geo = SG.LP.mergeGeometries(entries);
          hide = new THREE.BufferAttribute(new Uint8Array(geo.attributes.position.count), 1);
          geo.setAttribute('aHide', hide);
          far = new THREE.Mesh(geo, farMat);
          far.name = name + 'Far';
          far.castShadow = false; far.receiveShadow = true;
          far.matrixAutoUpdate = false;
          far.visible = false;
          this.root.add(far);
          this._mergedVerts = (this._mergedVerts || 0) + geo.attributes.position.count;
        }
        const sph = new THREE.Sphere(new THREE.Vector3(t.cx, 1, -t.cz), r + 2);
        for (const im of near) setInstBounds(im, sph);
        tiles.push({ mt: true, near, far, hide, n, mats, cols, xz, gi, vstart, vcount, cx: t.cx, cz: t.cz, r: r + 1, mode: '', key: '', hidden: true });
        yield;
      }
    }
    // 树木的区块网格：每区块每树种一个近景实例化网格（平滑模型，投影），另加一个远景公告板网格（所有树种，一次绘制）
    *_treeMeshes(B) {
      const tiles = new Map(), q = new THREE.Quaternion(), sc = new THREE.Vector3(), tp = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
      for (const b of B.map.values()) {
        const key = b.cx + ',' + b.cz;
        let t = tiles.get(key);
        if (!t) { t = { cx: b.cx, cz: b.cz, groups: [] }; tiles.set(key, t); }
        t.groups.push(b);
      }
      const smat = spriteMaterial(), sgeo = spriteGeometry();
      for (const t of tiles.values()) {
        let n = 0, r = 0;
        for (const b of t.groups) n += b.c.length / 3;
        const mats = new Float32Array(n * 16), smats = new Float32Array(n * 16), cols = new Float32Array(n * 3), cells = new Float32Array(n), xz = new Float32Array(n * 2), kind = new Uint8Array(n);
        const near = [];
        let i = 0;
        t.groups.forEach((b, gi) => {
          const m = b.c.length / 3, cell = SPRITE_KINDS.indexOf(b.kind), sz = SPRITE_SIZE[b.kind] || [1, 1.1];
          for (let k = 0; k < m; k++, i++) {
            const o = k * 6;
            tp.set(b.m[o], b.m[o + 1], -b.m[o + 2]);
            q.setFromAxisAngle(up, b.m[o + 5]);
            sc.set(b.m[o + 3], b.m[o + 4], b.m[o + 3]);
            _m4.compose(tp, q, sc).toArray(mats, i * 16);
            _m4.makeScale(sz[0] * b.m[o + 3], sz[1] * b.m[o + 4], 1).setPosition(tp).toArray(smats, i * 16);
            cols[i * 3] = b.c[k * 3]; cols[i * 3 + 1] = b.c[k * 3 + 1]; cols[i * 3 + 2] = b.c[k * 3 + 2];
            cells[i] = Math.max(0, cell); xz[i * 2] = b.m[o]; xz[i * 2 + 1] = b.m[o + 2]; kind[i] = gi;
            r = Math.max(r, Math.hypot(b.m[o] - t.cx, b.m[o + 2] - t.cz));
          }
          const im = new THREE.InstancedMesh(treeGeometry(b.kind, 0), treeMaterial('leaf'), m);
          im.name = 'Trees';
          im.userData.sharedGeometry = true;
          im.castShadow = true; im.receiveShadow = true;
          im.setColorAt(0, _col1);
          im.count = 0;
          near.push(im);
          this.root.add(im);
        });
        const far = new THREE.InstancedMesh(sgeo, smat, n);
        far.name = 'TreeSprites';
        far.userData.sharedGeometry = true;
        far.castShadow = false; far.receiveShadow = false;
        far.instanceMatrix.array.set(smats);
        far.setColorAt(0, _col1);
        far.instanceColor.array.set(cols);
        const cellAttr = new THREE.InstancedBufferAttribute(cells.slice(), 1);
        cellAttr.setUsage(THREE.DynamicDrawUsage);
        far.geometry = sgeo.clone();
        far.geometry.setAttribute('aCell', cellAttr);
        far.userData.sharedGeometry = false;
        far.instanceMatrix.needsUpdate = true; far.instanceColor.needsUpdate = true;
        far.count = n;
        far.boundingSphere = new THREE.Sphere(new THREE.Vector3(t.cx, 1, -t.cz), r + 2);
        far.geometry.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 0.5, 0), 1);
        this.root.add(far);
        // 近景各树种网格的包围球取整个区块（实例在区块内重新分配）
        for (const im of near) im.boundingSphere = new THREE.Sphere(new THREE.Vector3(t.cx, 1, -t.cz), r + 2);
        this._treeTiles.push({ tree: true, near, hero: near.map(() => null), kinds: t.groups.map(b => b.kind), gcount: t.groups.map(b => b.c.length / 3), far, n, mats, smats, cols, cells, xz, kind, cx: t.cx, cz: t.cz, r: r + 1, mode: 'far', key: '' });
        yield;
      }
    }
    // 第四版：树的区块网格——每区块每（树种, 变体）一个近景实例化网格（道具库远景细节 + 实例化墨线，投影），
    // 另加一个冒名顶替的公告板网格（所有树种，一次绘制）
    *_lpTreeMeshes(B) {
      const tiles = new Map(), q = new THREE.Quaternion(), sc = new THREE.Vector3(), tp = new THREE.Vector3(), up = new THREE.Vector3(0, 1, 0);
      for (const b of B.map.values()) {
        const key = b.cx + ',' + b.cz;
        let t = tiles.get(key);
        if (!t) { t = { cx: b.cx, cz: b.cz, groups: [] }; tiles.set(key, t); }
        t.groups.push(b);
      }
      const imp = vegImpostors(), smat = impostorMaterial(), sgeo = impostorGeometry();
      for (const t of tiles.values()) {
        let n = 0, r = 0;
        for (const b of t.groups) n += b.c.length / 3;
        const mats = new Float32Array(n * 16), smats = new Float32Array(n * 16), cols = new Float32Array(n * 3), cells = new Float32Array(n), xz = new Float32Array(n * 2), kind = new Uint8Array(n);
        const near = [], kinds = [];
        let i = 0;
        for (let gi = 0; gi < t.groups.length; gi++) {
          const b = t.groups[gi];
          const m = b.c.length / 3, [vk, vs] = b.kind.split(':'), variant = +vs, cell = imp.cells[vk], k = vegScale(vk);
          for (let j = 0; j < m; j++, i++) {
            const o = j * 6;
            tp.set(b.m[o], b.m[o + 1], -b.m[o + 2]);
            q.setFromAxisAngle(up, b.m[o + 5]);
            sc.set(b.m[o + 3] * k, b.m[o + 4] * k, b.m[o + 3] * k);
            _m4.compose(tp, q, sc).toArray(mats, i * 16);
            _m4.makeScale(cell.w * b.m[o + 3] * k, cell.h * b.m[o + 4] * k, 1).setPosition(tp).toArray(smats, i * 16);
            cols[i * 3] = b.c[j * 3]; cols[i * 3 + 1] = b.c[j * 3 + 1]; cols[i * 3 + 2] = b.c[j * 3 + 2];
            cells[i] = cell.i; xz[i * 2] = b.m[o]; xz[i * 2 + 1] = b.m[o + 2]; kind[i] = gi;
            r = Math.max(r, Math.hypot(b.m[o] - t.cx, b.m[o + 2] - t.cz));
          }
          const im = lpInst(vegGeo(vk, variant, 1), m, {});
          im.name = 'Trees';
          near.push(im);
          kinds.push(b.kind);
          this.root.add(im);
          yield;
        }
        const far = new THREE.InstancedMesh(sgeo, smat, n);
        far.name = 'TreeSprites';
        far.castShadow = false; far.receiveShadow = false;
        far.instanceMatrix.array.set(smats);
        far.setColorAt(0, _col1);
        far.instanceColor.array.set(cols);
        const cellAttr = new THREE.InstancedBufferAttribute(cells.slice(), 1);
        cellAttr.setUsage(THREE.DynamicDrawUsage);
        far.geometry = sgeo.clone();
        far.geometry.setAttribute('aCell', cellAttr);
        far.instanceMatrix.needsUpdate = true; far.instanceColor.needsUpdate = true;
        far.count = n;
        far.boundingSphere = new THREE.Sphere(new THREE.Vector3(t.cx, 1, -t.cz), r + 2);
        far.geometry.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 0.5, 0), 1);
        this.root.add(far);
        const sph = new THREE.Sphere(new THREE.Vector3(t.cx, 1, -t.cz), r + 2);
        for (const im of near) setInstBounds(im, sph);
        this._treeTiles.push({ tree: true, lp: true, near, hero: near.map(() => null), kinds, gcount: t.groups.map(b => b.c.length / 3), far, n, mats, smats, cols, cells, xz, kind, cx: t.cx, cz: t.cz, r: r + 1, mode: 'far', key: '' });
        yield;
      }
    }
    // 树木：树种随群落变化（每种群落数种），林地按噪声成片，平原上零星散树、河边柳杨、绿洲椰枣、村边果树
    *_genTrees() {
      const rnd = SG.SeededRandom(11);
      const geo = this._geo, D = this.spec.trees;
      // 修订（LP）：树的区块 200 单位（近景是逐实例分配的，区块大只省绘制次数：每区块每树种一次 + 墨线 + 投影）
      const B = this._instBuckets(vegReady() ? 200 : 100);
      let count = 0;
      const PINE = [0.2, 0.42, 0.27], SPRUCE = [0.15, 0.34, 0.25], DEC_S = [0.3, 0.57, 0.25], DEC_N = [0.48, 0.6, 0.28], CAMPHOR = [0.24, 0.5, 0.24];
      const TROP = [0.14, 0.47, 0.18], PALM = [0.32, 0.58, 0.2], OLIVE = [0.52, 0.58, 0.4], CYPRESS = [0.14, 0.31, 0.19], SHRUB = [0.52, 0.52, 0.3];
      const ACACIA = [0.46, 0.52, 0.24], BAMBOO = [0.44, 0.64, 0.3], WILLOW = [0.5, 0.68, 0.32], POPLAR = [0.4, 0.58, 0.25], BIRCH = [0.52, 0.66, 0.3];
      const STONEPINE = [0.24, 0.42, 0.22], MAPLE = [0.78, 0.38, 0.16], CEDAR = [0.13, 0.3, 0.2];
      // 第四版：树种换成道具库的配方（VEG）；群落色以「与该树种常用色之比」作为实例色（夹在 0.8–1.2：北方偏黄、南方偏深）
      const LPV = this._lpVeg = vegReady();
      if (LPV) vegImpostorPrecompile();
      const REF = { broad: DEC_S, broad2: DEC_S, pine: PINE, spruce: SPRUCE, cypress: CYPRESS, poplar: POPLAR, birch: BIRCH, shrub: SHRUB, willow: WILLOW,
        olive: OLIVE, stonepine: STONEPINE, acacia: ACACIA, bamboo: BAMBOO, palm: PALM, dshrub: SHRUB };
      // 修订：按经纬度挑树种——天竺的季风稀树草原用槐 / 栎 / 少量金合欢（不是东非的平顶金合欢 + 猴面包树）；
      // 北纬 36° 以北的绿洲是胡杨（不是椰枣）；荒漠里零散的「树」是灌丛
      const UNP = typeof SG.unproject === 'function' ? SG.unproject : null;
      const lpKind = (kind, c, px, pz, h, hv) => {
        switch (kind) {
          case 'broad': return c === MAPLE ? 'maple' : hv < 0.62 ? 'huai' : 'elm';
          case 'broad2': return 'oak';
          case 'pine': return 'pine';
          case 'spruce': return c === CEDAR ? 'cedar' : h > 2.5 && c === SPRUCE && geo.sample(px, pz)[F_BOREAL] > 0.5 ? 'snowpine' : 'spruce';
          case 'stonepine': return 'umbrella';
          case 'shrub': return 'bush';
          case 'acacia': {
            const g = UNP ? UNP(px, pz) : null;
            if (g && g.lon > 66) return hv < 0.3 ? 'acacia' : hv < 0.7 ? 'huai' : 'oak';
            return hv > 0.94 && (!g || g.lon < 45) ? 'baobab' : 'acacia';
          }
          case 'dshrub': return 'bush';
          case 'palm': {
            const g = UNP ? UNP(px, pz) : null;
            if (g && g.lat > 36) return 'poplar';
            const f = geo.sample(px, pz);
            if (f[F_TROP] > 0.5 && h < 0.9 && hv < 0.3 && geo.coastDist(px, pz) < 1.4) return 'mangrove';
            return f[F_TROP] > 0.35 ? 'coconut' : 'date';
          }
          default: return VEG[kind] ? kind : 'huai';
        }
      };
      const add = (kind, px, py, pz, s, sy, yaw, c, jit) => {
        const k = 1 + jit;
        if (LPV) {
          const hv = LP_HASH(px, pz), vk = lpKind(kind, c, px, pz, py, hv), d = VEG[vk], ref = REF[kind];
          const plain = !ref || vk === 'maple' || vk === 'cedar' || vk === 'snowpine' || vk === 'mangrove' || vk === 'baobab';
          // 荒漠、草原上的灌丛：偏灰的鼠尾草色（逐通道调色——这里要的正是去饱和）
          const t = plain ? [1, 1, 1] : kind === 'dshrub' ? [1.2, 1.04, 1.1] : lpTint(c, ref);
          // 第三轮：阔叶（槐 / 榆 / 栎）在策略镜头下只看得到暖色的树冠顶，午后光里偏橄榄——红稍减、蓝略减，回到试点的中绿
          if (vk === 'huai' || vk === 'elm' || vk === 'oak') { t[0] *= 0.93; t[2] *= 0.95; }
          const variant = d.v > 1 ? Math.floor(LP_HASH(pz + 3.1, px - 1.7) * d.v) % d.v : 0;
          // 当树种的灌丛（稀树草原、荒漠）小一号：道具库的灌丛又宽又矮，与树冠同宽时远看是一团团怪异的大色块
          const ks = kind === 'shrub' || kind === 'dshrub' ? 0.75 : 1;
          B.add(vk + ':' + variant, px, py, pz, s * ks * (d.sx || 1), sy * ks * (d.sy || 1), yaw, Math.min(1.3, t[0] * k), Math.min(1.3, t[1] * k), Math.min(1.3, t[2] * k));
        } else B.add(kind, px, py, pz, s, sy, yaw, Math.min(1, c[0] * k), Math.min(1, c[1] * k), Math.min(1, c[2] * k));
        count++;
      };
      const japanX = SG.project ? SG.project(124.5, 35).x : 999;
      const SP = 0.82;
      const xs = [];
      for (let x = D.x0; x < D.x1; x += SP) xs.push(x);
      this._traceTag = 'trees';
      let cc = 0, ty = now();
      for (let xi = 0; xi < xs.length; xi++) {
        const x = xs[xi];
        for (let z = D.z0; z < D.z1; z += SP) {
          if ((++cc & 63) === 0 && now() - ty > 8) { ty = now(); yield; }
          const px = x + (rnd.nextDouble() - 0.5) * 0.75, pz = z + (rnd.nextDouble() - 0.5) * 0.75;
          const r1 = rnd.nextDouble();
          const h = this.surfaceY(px, pz);
          if (h <= 0.5 || h >= 3.6) continue;
          const f = geo.sample(px, pz);
          const des = f[F_DESERT], st = f[F_STEPPE], sav = f[F_SAV], med = f[F_MED], tr = f[F_TROP], bo = f[F_BOREAL];
          const thr = 0.56 + des * 0.3 + st * 0.12 + sav * 0.05 - tr * 0.06 - bo * 0.05;
          const fw = M.inverseLerp(thr - 0.02, thr + 0.1, fbm(px * 0.09 + 100, pz * 0.09 + 100, 3));
          const forest = r1 < fw * 0.92;
          const pSparse = Math.max(0, 0.022 * (1 - 0.9 * des - 0.6 * st) + 0.03 * sav + 0.02 * med + 0.01 * tr);
          const sparse = !forest && h < 2.6 && r1 > 0.97 - pSparse;
          let rd = 99;
          let oasisTree = false, riverTree = false;
          if (!forest && !sparse) {
            if (h > 1.8 || r1 < 0.9) continue;
            rd = geo.riverDist(px, pz) / geo.rW;
            oasisTree = des > 0.35 && rd > 1.3 && rd < 3.4 && r1 > 0.93;
            riverTree = !oasisTree && rd > 1.4 && rd < 2.6 && r1 > 0.975;
            if (!oasisTree && !riverTree) continue;
          }
          if (this._nearCity(px, pz, 3.2)) continue;
          if (rd === 99) rd = geo.riverDist(px, pz) / geo.rW;
          if (rd < 1.3) continue;
          const e = 0.5, sl = Math.abs(this.surfaceY(px + e, pz) - this.surfaceY(px - e, pz)) + Math.abs(this.surfaceY(px, pz + e) - this.surfaceY(px, pz - e));
          if (sl > 1.1) continue;
          const north = M.inverseLerp(10, 95, pz);
          // 修订：林地里的树大一些（道具库的树比第三版的圆球小，同样的株数成片时读成稀疏的果园）——只改大小，不改位置与株数
          const v = rnd.nextDouble(), s = (0.85 + rnd.nextDouble() * 0.6) * (forest && LPV ? 1.22 : 1), jit = (rnd.nextDouble() - 0.5) * 0.16;
          const hy = Math.sin(px * 12.9898 + pz * 78.233) * 43758.5453;
          const y = h - 0.04, yaw = (hy - Math.floor(hy)) * Math.PI * 2;
          if (oasisTree || (des > 0.5 && v < 0.35)) {
            if (oasisTree || !LPV) add('palm', px, y, pz, s * 1.05, s * 1.05, yaw, PALM, jit);
            else add('dshrub', px, y, pz, s * 1.05, s * 1.05, yaw, SHRUB, jit);
            continue;
          }
          if (riverTree && des < 0.3) {
            if (tr > 0.4) add('palm', px, y, pz, s, s, yaw, PALM, jit);
            else if (med > 0.4) add('poplar', px, y, pz, s, s * 1.1, yaw, POPLAR, jit);
            else add(v < 0.6 ? 'willow' : 'poplar', px, y, pz, s, s, yaw, v < 0.6 ? WILLOW : POPLAR, jit);
            continue;
          }
          if (des > 0.45 || (st > 0.5 && !forest)) { add(st > 0.4 && v < 0.12 ? 'poplar' : LPV ? 'dshrub' : 'shrub', px, y, pz, s, s, yaw, st > 0.4 && v < 0.12 ? POPLAR : SHRUB, jit); continue; }
          if (tr > 0.45) {
            if (v < 0.22) add('palm', px, y, pz, s * 1.1, s * 1.1, yaw, PALM, jit);
            else if (v < 0.27) add('bamboo', px, y, pz, s, s, yaw, BAMBOO, jit);
            else add(v < 0.7 ? 'broad' : 'broad2', px, y, pz, s * 1.1, s * 1.2, yaw, TROP, jit);
            continue;
          }
          if (med > 0.45) {
            if (v < 0.28) add('cypress', px, y, pz, s, s, yaw, CYPRESS, jit);
            else if (v < 0.5) add('stonepine', px, y, pz, s, s, yaw, STONEPINE, jit);
            else add('olive', px, y, pz, s * 0.9, s * 0.8, yaw, OLIVE, jit);
            continue;
          }
          if (sav > 0.45 && !forest) { add(v < 0.75 ? 'acacia' : 'shrub', px, y, pz, s * 1.1, s, yaw, v < 0.75 ? ACACIA : SHRUB, jit); continue; }
          if (bo > 0.4) { add(v < 0.62 ? 'spruce' : v < 0.85 ? 'birch' : 'pine', px, y, pz, s, s * 1.12, yaw, v < 0.62 ? SPRUCE : v < 0.85 ? BIRCH : PINE, jit); continue; }
          // 东亚 / 温带
          if (px > japanX && f[F_EAST] > 0.3) {
            if (v < 0.35) add('pine', px, y, pz, s, s, yaw, PINE, jit);
            else if (v < 0.55) add('spruce', px, y, pz, s, s * 1.15, yaw, CEDAR, jit);
            else if (v < 0.62) add('broad', px, y, pz, s, s, yaw, MAPLE, jit);
            else add(v < 0.8 ? 'broad' : 'broad2', px, y, pz, s, s, yaw, DEC_S, jit);
            continue;
          }
          const dryN = north * f[F_EAST];
          if (h > 2.1) { add(v < 0.6 ? 'pine' : 'spruce', px, y, pz, s, s, yaw, v < 0.6 ? PINE : SPRUCE, jit); continue; }
          if (dryN > 0.45) {
            if (v < 0.3) add('poplar', px, y, pz, s, s * 1.1, yaw, POPLAR, jit);
            else if (v < 0.6) add('pine', px, y, pz, s, s, yaw, PINE, jit);
            else add('broad', px, y, pz, s, s, yaw, DEC_N, jit);
            continue;
          }
          if (z < 40 && v < 0.14) { add('bamboo', px, y, pz, s, s, yaw, BAMBOO, jit); continue; }
          if (v < 0.24) add('pine', px, y, pz, s, s, yaw, PINE, jit);
          else {
            const c = z < 45 ? CAMPHOR : DEC_S;
            const k = M.clamp01(dryN * 1.4);
            add(v < 0.62 ? 'broad' : 'broad2', px, y, pz, s, s, yaw, [c[0] + (DEC_N[0] - c[0]) * k, c[1] + (DEC_N[1] - c[1]) * k, c[2] + (DEC_N[2] - c[2]) * k], jit);
          }
        }
        this._sub(0.8 * xi / xs.length);
        yield;
      }
      // 村边果树
      for (const o of this._hamlets || []) {
        for (let k = 0; k < 3; k++) {
          const a = o.yaw + 1.3 + k * 1.9 + rnd.nextDouble() * 0.6, r = 1.3 + rnd.nextDouble() * 0.5;
          const px = o.x + Math.cos(a) * r, pz = o.z + Math.sin(a) * r, h = this.surfaceY(px, pz);
          if (h < 0.45) continue;
          // 第三版修订：新增文化（india / africa / moor）按 SG.cultureBase 回退：天竺、非洲的村边是椰枣 / 椰树
          const cb = SG.cultureBase ? SG.cultureBase(o.culture) : o.culture, cu = o.culture;
          const dry = cb === 'persia' || cb === 'arab' || cb === 'tarim' || cb === 'kushan';
          let kind = cb === 'seasia' || cb === 'yi' || cb === 'nanman' || cu === 'africa' ? 'palm' : dry ? (k ? 'poplar' : 'palm') : cb === 'roman' || cu === 'moor' ? (k ? 'olive' : 'cypress') : 'broad';
          // 第四版：汉地 / 朝鲜 / 倭的村边是桃李（花树）
          if (LPV && kind === 'broad' && (cb === 'han' || cb === 'korea' || cb === 'wa') && k === 0 && LP_HASH(o.x, o.z) < 0.45) kind = 'blossom';
          const c = kind === 'palm' ? PALM : kind === 'poplar' ? POPLAR : kind === 'olive' ? OLIVE : kind === 'cypress' ? CYPRESS : [0.36, 0.6, 0.27];
          add(kind, px, h - 0.04, pz, 0.75, 0.75, a, c, (rnd.nextDouble() - 0.5) * 0.12);
        }
      }
      this._treeTiles = [];
      this._traceTag = 'trees.mesh';
      if (LPV) {
        // 道具库几何体（每种一步）与冒名顶替图集
        // （近景模型只用来量高度；与远景细节分两步建，各占一个分片）
        for (const k of VEG_KINDS) { vegScale(k); yield; for (let v = 0; v < VEG[k].v; v++) { vegGeo(k, v, 1); yield; } }
        this._traceTag = 'trees.impostors';
        yield* vegImpostorsGen();
        impostorMaterial();
        yield;
        yield* this._lpTreeMeshes(B);
      } else {
        yield* this._treeMeshes(B);
        // 特写模型（lod 2）预先生成（每种一步），首次拉近镜头时不卡顿
        const kinds = new Set();
        for (const t of this._treeTiles) for (const k of t.kinds) kinds.add(k);
        this._traceTag = 'trees.hero';
        for (const k of kinds) { treeGeometry(k, 2); yield; }
      }
      this.treeCount = count;
    }

    // 石块与城外村落
    *_genScatter() {
      const rnd = SG.SeededRandom(23), G = this._geo, D = this.spec.trees, LPV = this._lpVeg;
      const B = this._instBuckets();
      let rocks = 0, ty = now();
      this._mergedVerts = 0;
      const SP = 1.35;
      this._traceTag = 'rocks';
      let xi = 0;
      for (let x = D.x0; x < D.x1; x += SP, xi++) {
        for (let z = D.z0; z < D.z1; z += SP) {
          const px = x + (rnd.nextDouble() - 0.5) * 1.1, pz = z + (rnd.nextDouble() - 0.5) * 1.1, r1 = rnd.nextDouble();
          if (r1 < 0.8) continue;
          const h = this.surfaceY(px, pz);
          if (h < 0.15) continue;
          const e = 0.6, hx = (this.surfaceY(px + e, pz) - this.surfaceY(px - e, pz)) / (2 * e), hz = (this.surfaceY(px, pz + e) - this.surfaceY(px, pz - e)) / (2 * e);
          const slope = 1 - 1 / Math.sqrt(1 + hx * hx + hz * hz);
          const f = G.sample(px, pz);
          const des = f[F_DESERT], st = f[F_STEPPE];
          let p = 0.04 + 0.5 * ss(0.25, 0.55, slope) + 0.35 * ss(2.6, 4.8, h) + 0.25 * des + 0.1 * st;
          if (h < 0.7 && G.coastDist(px, pz) < 1.5) p += 0.25;
          if (h > 5.6) p *= 0.3;
          if (r1 < 1 - p * 0.2) continue;
          if (this._nearCity(px, pz, 2.8)) continue;
          if (G.riverDist(px, pz) / G.rW < 1.2) continue;
          const v = rnd.next(3), s = (0.38 + rnd.nextDouble() * 0.55) * (1 + 0.4 * ss(3, 5, h)), yaw = rnd.nextDouble() * Math.PI * 2;
          const warm = des * 0.8 + st * 0.3, k = 0.8 + rnd.nextDouble() * 0.25;
          const c = [0.5 + 0.12 * warm, 0.48 + 0.03 * warm, 0.45 - 0.08 * warm];
          if (LPV) {
            // 第四版：道具库的平面着色石块（带墨线）：雪线上为雪石，陡坡为崖石，大块为巨石
            const sy = 0.7 + rnd.nextDouble() * 0.5, kind = h > 4.8 ? 'rock_snow' : slope > 0.4 ? 'cliff' : v === 2 ? 'rock_large' : s > 0.75 ? 'boulder' : 'rock_small';
            const w = 0.62 * s * (0.8 + 0.3 * v) * (kind === 'cliff' ? 1.25 : 1), sc = w / ROCK_W[kind];
            B.add(kind, px, h - 0.04 * s, pz, sc, sc * (0.75 + 0.35 * sy), yaw, M.clamp((1 + 0.24 * warm) * k / 0.92, 0.75, 1.25), M.clamp((1 + 0.06 * warm) * k / 0.92, 0.75, 1.25), M.clamp((1 - 0.18 * warm) * k / 0.92, 0.75, 1.25));
          } else B.add('rock' + (v === 2 ? 1 : 0), px, h - 0.06 * s, pz, s * (0.8 + 0.3 * v), s * (0.7 + rnd.nextDouble() * 0.5), yaw, c[0] * k, c[1] * k, c[2] * k);
          rocks++;
        }
        if (now() - ty > 5) { ty = now(); this._sub(0.6 * (x - D.x0) / (D.x1 - D.x0)); yield; }
      }
      // 灌丛：草地、丘陵上零散分布（不在农田、林地、沙漠中心）
      const SB = this._instBuckets(), T = this._tex;
      xi = 0;
      for (let x = D.x0; x < D.x1; x += 1.6, xi++) {
        for (let z = D.z0; z < D.z1; z += 1.6) {
          const px = x + (rnd.nextDouble() - 0.5) * 1.3, pz = z + (rnd.nextDouble() - 0.5) * 1.3, r1 = rnd.nextDouble();
          if (r1 < 0.9) continue;
          const ti = Math.round((px - T.x0) / T.step), tj = Math.round((pz - T.z0) / T.step);
          if (ti < 0 || tj < 0 || ti >= T.w || tj >= T.h) continue;
          const q = (tj * T.w + ti) * 4;
          const farm = 1 - T.m[q + 3] / 255, sand = T.m[q] / 255, snow = T.m[q + 2] / 255, forest = T.c[q + 3] / 255;
          if (farm > 0.25 || snow > 0.3 || forest > 0.5 || sand > 0.7) continue;
          const h = this.surfaceY(px, pz);
          if (h < 0.55 || h > 4.2) continue;
          const p = 0.25 + 0.5 * ss(1.2, 3.0, h) + 0.25 * sand;
          if (r1 < 1 - 0.1 * p) continue;
          if (this._nearCity(px, pz, 2.8) || G.riverDist(px, pz) / G.rW < 1.3) continue;
          const k = 0.85 + rnd.nextDouble() * 0.3, s = 0.55 + rnd.nextDouble() * 0.6;
          const c = [T.c[q] / 255 * 0.62 * k, T.c[q + 1] / 255 * 0.72 * k, T.c[q + 2] / 255 * 0.55 * k];
          if (LPV) {
            const sy = 0.7 + rnd.nextDouble() * 0.5, yaw = rnd.nextDouble() * 6.28, vs = vegScale('bush'), variant = LP_HASH(px, pz) < 0.5 ? 0 : 1;
            const t = lpTint([T.c[q] / 255, T.c[q + 1] / 255, T.c[q + 2] / 255], LUSH);
            SB.add('bush:' + variant, px, h - 0.03, pz, s * 1.15 * vs, s * 1.15 * sy * vs, yaw, t[0] * k, t[1] * k, t[2] * k);
          } else SB.add('shrub', px, h - 0.03, pz, s * 1.2, s * (0.7 + rnd.nextDouble() * 0.5), rnd.nextDouble() * 6.28, c[0], c[1], c[2]);
        }
        if ((xi & 3) === 3) { this._sub(0.4 + 0.2 * (x - D.x0) / (D.x1 - D.x0)); yield; }
      }
      this._scatter = [];
      if (LPV) {
        // 修订：每区块一个合并的远景网格；近景（墨线）只在 SCATTER_NEAR 以内；只有大石、崖石投影
        let b0 = this._scatter.length;
        this._traceTag = 'rocks.mesh';
        yield* this._lpMergedTiles(B, k => propGeo(k, 0, 0), rockFarGeo, 'Rocks', this._scatter, { shadow: k => k === 'boulder' || k === 'cliff' || k === 'rock_large' });
        for (let i = b0; i < this._scatter.length; i++) this._scatter[i].nearR = SCATTER_NEAR;
        b0 = this._scatter.length;
        this._traceTag = 'shrubs.mesh';
        const bushGeo = key => { const [k, v] = key.split(':'); return vegGeo(k, +v, 1); };
        yield* this._lpMergedTiles(SB, bushGeo, bushGeo, 'Shrubs', this._scatter, { wind: VEG_WIND });
        for (let i = b0; i < this._scatter.length; i++) this._scatter[i].nearR = SCATTER_NEAR;
      } else {
        yield* this._instMeshes(B, (kind, lod) => rockGeometry(+kind.slice(4), lod), treeMaterial('rock'), 'Rocks', this._scatter, false);
        yield* this._instMeshes(SB, treeGeometry, treeMaterial('leaf'), 'Shrubs', this._scatter, false);
      }
      this.rockCount = rocks;
      // 村落：每处 3–6 间，按文化取屋型与屋顶色
      const HB = this._instBuckets();
      const STYLE = { han: 'han', korea: 'han', wa: 'round', yi: 'stilt', nanman: 'stilt', seasia: 'stilt', steppe: 'yurt', tarim: 'flat', kushan: 'flat',
        persia: 'flat', arab: 'flat', roman: 'tile', sarmatian: 'tile', celt: 'round', german: 'long', india: 'flat', africa: 'flat', moor: 'flat' };
      const ROOF = {
        han: [[0.36, 0.38, 0.42], [0.3, 0.32, 0.36], [0.42, 0.4, 0.38], [0.62, 0.52, 0.34]], tile: [[0.72, 0.36, 0.24], [0.66, 0.32, 0.22], [0.76, 0.42, 0.28]],
        flat: [[0.86, 0.76, 0.58], [0.8, 0.7, 0.52], [0.9, 0.82, 0.66]], round: [[0.7, 0.6, 0.38], [0.62, 0.54, 0.34]], yurt: [[0.95, 0.93, 0.86], [0.88, 0.84, 0.74]],
        stilt: [[0.66, 0.56, 0.34], [0.58, 0.5, 0.3]], long: [[0.62, 0.54, 0.32], [0.55, 0.48, 0.3]],
      };
      let houses = 0;
      for (const o of this._hamlets || []) {
        const style = STYLE[o.culture] || STYLE[SG.cultureBase ? SG.cultureBase(o.culture) : 'han'] || 'han', pal = ROOF[style];
        const pts = [];
        for (let k = 0; k < o.n * 4 && pts.length < o.n; k++) {
          const a = rnd.nextDouble() * Math.PI * 2, r = Math.sqrt(rnd.nextDouble()) * 1.15;
          const px = o.x + Math.cos(a) * r, pz = o.z + Math.sin(a) * r;
          if (pts.some(p => Math.hypot(p[0] - px, p[1] - pz) < 0.5)) continue;
          const h = this.surfaceY(px, pz);
          if (h < 0.4) continue;
          pts.push([px, pz, h]);
        }
        for (const [px, pz, h] of pts) {
          const c = pal[rnd.next(pal.length)], k = 0.92 + rnd.nextDouble() * 0.16;
          // 修订（LP）：屋向只取顺着 / 垂直于村道两种，不再随机偏转（偏转的一堆小房块远看像散落的杂物）
          const yaw = -o.yaw + Math.PI / 2 + (rnd.next(2) ? 0 : Math.PI / 2) + (rnd.nextDouble() - 0.5) * (LPV ? 0 : 0.3);
          const s = 0.9 + rnd.nextDouble() * 0.3;
          if (LPV) {
            // 汉地村落：瓦房与草房相间（草顶的暖金色让村子在地图上一眼可见，同 Lanternvale 的农舍）
            const hv = LP_HASH(px + 7.7, pz - 3.1), rec = style === 'han' && hv < 0.6 ? 'house_thatch' : HOUSE_LP[style] || 'house_han';
            // 修订：地图比例下每种屋只用一个变体（变化靠朝向、大小、明暗；每个变体都是一次绘制 + 墨线 + 投影）
            const sc = s * (rec === 'house_thatch' && style === 'han' ? HOUSE_W.round : HOUSE_W[style]) / PROP_W(rec), variant = 0;
            HB.add(rec + ':' + variant, px, h - 0.02, pz, sc, sc, yaw + Math.PI, k, k, k);
          } else HB.add(style, px, h - 0.02, pz, s, s, yaw, c[0] * k, c[1] * k, c[2] * k);
          houses++;
        }
        this._sub(0.6 + 0.3 * houses / Math.max(1, (this._hamlets.length * 4)));
      }
      if (LPV) {
        // 村边的草垛与水井（农耕文化的村落）
        const r2 = SG.SeededRandom(31);
        for (const o of this._hamlets || []) {
          const style = STYLE[o.culture] || STYLE[SG.cultureBase ? SG.cultureBase(o.culture) : 'han'] || 'han';
          if (style === 'yurt') continue;
          const n = 1 + r2.next(2);
          for (let k = 0; k < n; k++) {
            const a = r2.nextDouble() * Math.PI * 2, rr = 1.0 + r2.nextDouble() * 0.6, px = o.x + Math.cos(a) * rr, pz = o.z + Math.sin(a) * rr, h = this.surfaceY(px, pz);
            if (h < 0.4) continue;
            const sc = (0.2 + r2.nextDouble() * 0.06) / PROP_W('haystack');
            HB.add('haystack:0', px, h - 0.02, pz, sc, sc * (k & 1 ? 0.85 : 1), r2.nextDouble() * 6.28, 1, 1, 1);
          }
          if (style === 'han' && r2.nextDouble() < 0.6) {
            const h = this.surfaceY(o.x, o.z);
            if (h > 0.4) { const sc = 0.22 / PROP_W('well'); HB.add('well:0', o.x, h - 0.02, o.z, sc, sc, r2.nextDouble() * 6.28, 1, 1, 1); }
          }
        }
        // 村舍近景也用道具库的远景细节（地图上一间屋只有十几个像素宽）；远处的区块整片隐去
        const before = this._scatter.length;
        this._traceTag = 'villages.mesh';
        yield* this._lpMergedTiles(HB, key => { const [k, v] = key.split(':'); return propGeo(k, +v, 1); }, key => speckGeo(key.split(':')[0]), 'Villages', this._scatter, { shadow: k => !/^(haystack|well)/.test(k) });
        for (let i = before; i < this._scatter.length; i++) { this._scatter[i].hideFar = VILLAGE_HIDE; this._scatter[i].nearR = VILLAGE_NEAR; }
      } else yield* this._instMeshes(HB, kind => houseGeometry(kind), treeMaterial(), 'Villages', this._scatter, true);
      this.houseCount = houses;
    }

    // 道路：贴地的柔边路面（纹理带两道车辙，边缘透明渐隐）；过河处架桥（汉、罗马、波斯等为石拱桥，其余为木桥），
    // 水面过宽（> 9）时不画；著名关隘（虎牢关、函谷关、剑阁……）建在附近的道路上。海路照旧画虚线航线
    *_genRoads(state) {
      this._inkProps = [];
      const cities = state.cities, done = new Set(), seaPairs = [];
      const isSea = typeof SG.linkIsSea === 'function' ? SG.linkIsSea : null;
      // 第四版：Lanternvale 的绘制土路（LP 贴花材质：受同一套光照、夜色与灯池；路面纹理带车辙、碎石与草边）
      const LPR = !!(SG.LP && SG.LP.material && this._lpVeg);
      let roadMat;
      if (LPR) {
        roadMat = SG.LP.material({ map: lpRoadTexture(), alpha: true, opacity: 1, depthWrite: false, polygonOffset: -2 });
      } else {
        roadMat = SG.Gfx.newLowPoly();
        roadMat.map = roadTexture();
        roadMat.transparent = true; roadMat.depthWrite = false;
        roadMat.polygonOffset = true; roadMat.polygonOffsetFactor = -1; roadMat.polygonOffsetUnits = -3;
        roadMat.name = 'Roads';
        this._ownMats.push(roadMat);
      }
      const CW = LPR ? 4 : 3;
      let P = [], N = [], UV = [], CO = [];
      const bridges = new SG.MeshBuilder(), lpBridges = [];
      const segs = [], lines = this._roadLines = [];
      const CA = SG.CultureArt;
      const self = this;
      const flush = function* () {
        if (!P.length) return;
        self._traceTag = 'roads.flush';
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(P, 3));
        yield;
        g.setAttribute('normal', new THREE.Float32BufferAttribute(N, 3));
        g.setAttribute('uv', new THREE.Float32BufferAttribute(UV, 2));
        g.setAttribute('color', new THREE.Float32BufferAttribute(CO, CW));
        g.computeBoundingSphere();
        const m = SG.Gfx.mesh(g, roadMat, { castShadow: false, receiveShadow: true });
        m.name = 'Roads';
        self.root.add(m);
        P = []; N = []; UV = []; CO = [];
      };
      const HW = 0.27, STEP = 0.42, LIFT = 0.055;
      const emit = (a, b) => {
        // a, b：{ x, z, y, nx, nz, v }（地图坐标；n = 路面左向单位向量）
        const ay = a.vy !== undefined ? a.vy : a.y, by = b.vy !== undefined ? b.vy : b.y;
        const p = [[a.x + a.nx * HW, ay, a.z + a.nz * HW, 0, a.v], [a.x - a.nx * HW, ay, a.z - a.nz * HW, 1, a.v],
          [b.x + b.nx * HW, by, b.z + b.nz * HW, 0, b.v], [b.x - b.nx * HW, by, b.z - b.nz * HW, 1, b.v]];
        for (const k of [0, 1, 2, 1, 3, 2]) {
          const q = p[k];
          P.push(q[0], q[1], -q[2]); N.push(0, 1, 0); UV.push(q[3], q[4]);
          if (LPR) CO.push(1, 1, 1, 0); else CO.push(0.74, 0.62, 0.45);
        }
      };
      let work = 0, ci = 0;
      for (const c of cities) {
        ci++;
        if (work >= 400) { work = 0; this._sub(0.5 * ci / cities.length); yield; }
        if (P.length > 90000) { yield* flush(); yield; }
        this._traceTag = 'roads';
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
          const n = Math.max(2, Math.ceil(len / STEP));
          const dx = (bx - ax) / len, dy = (by - ay) / len;
          const bend = (M.perlinNoise(c.id, o.id) - 0.5) * len * 0.12;
          // 中心线（略带弯曲），附带地表高度
          const pts = [];
          for (let i = 0; i <= n; i++) {
            const f = i / n, w = Math.sin(f * Math.PI) * bend;
            const px = ax + (bx - ax) * f - dy * w, pz = ay + (by - ay) * f + dx * w;
            pts.push({ x: px, z: pz, h: this.surfaceY(px, pz), y: 0, nx: 0, nz: 0, v: 0, gap: false });
          }
          for (let i = 0; i <= n; i++) {
            const p0 = pts[Math.max(0, i - 1)], p1 = pts[Math.min(n, i + 1)];
            const tx = p1.x - p0.x, tz = p1.z - p0.z, tl = Math.hypot(tx, tz) || 1;
            const p = pts[i];
            p.nx = -tz / tl; p.nz = tx / tl;
            p.y = Math.max(p.h, 0.1) + LIFT;
            p.v = i > 0 ? pts[i - 1].v + Math.hypot(p.x - pts[i - 1].x, p.z - pts[i - 1].z) * 0.7 : 0;
          }
          // 过水：连续的水面点（地表 < 0.06）
          for (let i = 1; i < n; i++) {
            if (pts[i].h >= 0.06) continue;
            let e = i;
            while (e < n && pts[e].h < 0.06) e++;
            const s0 = i - 1, s1 = e;   // 两岸的干点
            const L = Math.hypot(pts[s1].x - pts[s0].x, pts[s1].z - pts[s0].z);
            const mx = (pts[s0].x + pts[s1].x) / 2, mz = (pts[s0].z + pts[s1].z) / 2;
            // 第三版修订：只在真正横渡河流处架桥——两岸点上「离河距离」的梯度与道路方向基本相对（沿河而行、在河道里穿行的
            // 路段不再变成一条条长长的高架桥，改为断开）；湖塘（离河远）照旧
            let crossing = true;
            {
              const G0 = this._geo, e = 0.35;
              if (G0.riverDist(mx, mz) / G0.rW < 3) {
                const rx = (pts[s1].x - pts[s0].x) / (L || 1), rz = (pts[s1].z - pts[s0].z) / (L || 1);
                const gr = (x, z) => { const gx = G0.riverDist(x + e, z) - G0.riverDist(x - e, z), gz = G0.riverDist(x, z + e) - G0.riverDist(x, z - e), l = Math.hypot(gx, gz) || 1; return [gx / l, gz / l]; };
                const g0 = gr(pts[s0].x, pts[s0].z), g1 = gr(pts[s1].x, pts[s1].z);
                crossing = (-(rx * g0[0] + rz * g0[1]) + (rx * g1[0] + rz * g1[1])) / 2 > 0.55;
              }
            }
            if (L <= 5 && crossing && !this._geo.inSea(mx, mz)) {
              const deck = Math.max(pts[s0].h, pts[s1].h, 0.12) + 0.16;
              for (let k = s0; k <= s1; k++) pts[k].y = Math.max(pts[k].y, deck + 0.03);
              const culture = CA ? CA.cultureOfCity(c) : 'han';
              const stone = culture === 'han' || culture === 'roman' || culture === 'persia' || culture === 'korea' || culture === 'kushan' || culture === 'arab' || culture === 'sarmatian' || culture === 'india' || culture === 'africa';
              if (LPR) {
                // 第四版：道具库的石拱桥 / 木桥（按跨度取长度），路面在两岸接上桥头（桥上不画路面）
                lpBridges.push({ a: pts[s0], b: pts[s1], y: Math.max(pts[s0].h, pts[s1].h, 0.1), stone });
                for (let k = s0; k <= s1; k++) { pts[k].onBridge = true; pts[k].vy = Math.max(pts[k].h, 0.1) + LIFT; }
              } else bridgeInto(bridges, pts[s0], pts[s1], deck, stone);
            } else for (let k = s0 + 1; k < s1; k++) pts[k].gap = true;
            i = e;
          }
          for (let i = 0; i < n; i++) {
            if (pts[i].gap || pts[i + 1].gap || (pts[i].onBridge && pts[i + 1].onBridge)) continue;
            emit(pts[i], pts[i + 1]);
          }
          // 中心线分段（供 roadDist 使用）与折线（供关隘定位）
          for (let k = 0; k < n; k += 4) { const q = pts[Math.min(n, k + 4)]; segs.push(pts[k].x, pts[k].z, q.x, q.z); }
          const xz = new Float32Array((n + 1) * 2), gap = new Uint8Array(n + 1);
          for (let k = 0; k <= n; k++) { xz[k * 2] = pts[k].x; xz[k * 2 + 1] = pts[k].z; gap[k] = pts[k].gap || pts[k].h < 0.1 || pts[k].y > pts[k].h + LIFT + 0.01 ? 1 : 0; }
          lines.push({ xz, gap, n: n + 1 });
          work += n + 1;
        }
      }
      yield* flush();
      if (bridges.count) {
        const bm = SG.Gfx.mesh(yield* sliceGeometry(bridges), SG.Gfx.masonryMaterial ? SG.Gfx.masonryMaterial() : SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
        bm.name = 'Bridges';
        this.root.add(bm);
      }
      if (lpBridges.length) {
        this._traceTag = 'roads.bridges';
        const entries = [], BW = 0.52;
        for (const br of lpBridges) {
          const dx = br.b.x - br.a.x, dz = br.b.z - br.a.z, L = Math.hypot(dx, dz) + 0.5;
          const name = br.stone ? 'bridge_stone' : 'bridge_wood', k = BW / (br.stone ? 2.7 : 2.0);
          const len = Math.max(4, Math.round(L / k * 2) / 2);
          const geo = SG.LPProps.geometry(name, { seed: 0, len }).ink;
          if (!geo) continue;
          // 模型的长度沿 x：转到道路方向（three 坐标 (dx, 0, −dz)）
          const m = new THREE.Matrix4().compose(new THREE.Vector3((br.a.x + br.b.x) / 2, br.y - 0.02, -(br.a.z + br.b.z) / 2),
            new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.atan2(dz, dx)), new THREE.Vector3(k, k, k));
          entries.push({ geo, matrix: m });
          yield;
        }
        if (entries.length) {
          // 按 40 单位的块合并（每块一次绘制 + 一次墨线；墨线按块的远近开关）。修订：逐块合并、每块之后让出（一次合并全图的桥要两百毫秒）
          const g = new THREE.Group();
          g.name = 'Bridges';
          this.root.add(g);
          yield* this._chunkBatches(entries, 40, 'Bridges', g);
        }
      }
      // 道路索引（roadDist / terrainInfo）
      this._roadSegs = Float64Array.from(segs);
      this._roadIdx = makeSegIndex(this._roadSegs, 2);
      yield;
      yield* this._genPasses(state);
      // 海路：沿水面绕开陆地的虚线航线
      if (seaPairs.length) {
        yield* this._genSeaMask();
        const DP = [], DO = [];
        for (let k = 0; k < seaPairs.length; k++) {
          const [a, b] = seaPairs[k];
          const path = yield* this._seaPathGen(this._cityX[a], this._cityZ[a], this._cityX[b], this._cityZ[b]);
          this._lanes.set(a + '-' + b, path);
          this._dashes(path, DP, DO);
          this._sub(0.6 + 0.4 * (k + 1) / seaPairs.length);
          yield;
        }
        this._sea = null;   // 只在生成航线时使用
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(DP, 3));
        g.setAttribute('aOff', new THREE.Float32BufferAttribute(DO, 2));
        g.computeBoundingSphere();
        if (g.boundingSphere) g.boundingSphere.radius += 1;
        const mat = seaLaneMaterial();
        this._ownMats.push(mat);
        const m = new THREE.Mesh(g, mat);
        m.name = 'SeaLanes';
        m.renderOrder = 3;
        m.castShadow = false; m.receiveShadow = false;
        this.root.add(m);
      }
    }
    // 静态合并（LP.StaticBatch）按 chunk 单位的块分开建，每块之后让出；块网格登记到 _inkProps（墨线、投影、隐去按远近）
    *_chunkBatches(entries, chunk, name, parent) {
      const bins = new Map();
      for (const e of entries) {
        const key = Math.floor(e.matrix.elements[12] / chunk) + ',' + Math.floor(e.matrix.elements[14] / chunk);
        let b = bins.get(key);
        if (!b) bins.set(key, (b = []));
        b.push(e);
      }
      for (const list of bins.values()) {
        const sb = new SG.LP.StaticBatch({ chunk, name });
        for (const e of list) sb.add(e.geo, e.matrix, { castShadow: true });
        const g = sb.build();
        for (const c of g.children.slice()) { c.name = name; parent.add(c); this._inkProps.push(c); }
        yield;
      }
    }
    // 关隘（第三版修订）：在史实位置 8 单位以内的道路点中挑选——离城 3.6 以上、不在桥上，两侧地势越高（鞍部）、离史实位置越近越好；
    // 附近没有道路（如温泉关）则建在史实位置本身，门向顺着等高线（城墙一侧上山、一侧下到海边）。结果列入 this.passes
    *_genPasses(state) {
      const mb = new SG.MeshBuilder();
      // 第四版：道具库的城门楼（汉式关：匾额写关名）/ 圆塔（西方石门）+ 沿山坡斜切的夯土 / 石墙段（LP，墨线）
      const lpWalls = this._lpVeg && SG.LPProps.has('gate_tower') && SG.LPProps.has('wall_segment') ? { entries: [], objects: [], lamps: [] } : null;
      const list = [];
      this.passes = list;
      this.passesSkipped = [];
      const rise = (x, z, px, pz, y0) => {
        let r = Infinity;
        for (const s of [-1, 1]) {
          let best = -9;
          for (const d of [1.2, 2.0, 2.8]) best = Math.max(best, this.surfaceY(x + px * s * d, z + pz * s * d) - y0);
          r = Math.min(r, best);
        }
        return r;
      };
      for (const p of PASSES) {
        const q = SG.project ? SG.project(p[1], p[2]) : { x: (p[1] - 100) * 5, y: (p[2] - 23) * 5.6 };
        const b = this.bounds;
        if (q.x < b.xMin - 5 || q.x > b.xMax + 5 || q.y < b.yMin - 5 || q.y > b.yMax + 5) continue;
        let best = null, bs = Infinity;
        for (const L of this._roadLines || []) {
          const xz = L.xz;
          for (let k = 1; k < L.n - 1; k++) {
            if (L.gap[k] || L.gap[k - 1] || L.gap[k + 1]) continue;
            const x = xz[k * 2], z = xz[k * 2 + 1], dq = Math.hypot(x - q.x, z - q.y);
            if (dq > 8 || this._cityNearDist(x, z) < 3.6) continue;
            const y0 = this.surfaceY(x, z);
            if (y0 < 0.12) continue;
            let dx = xz[k * 2 + 2] - xz[k * 2 - 2], dz = xz[k * 2 + 3] - xz[k * 2 - 1];
            const l = Math.hypot(dx, dz) || 1; dx /= l; dz /= l;
            const sc = dq * 0.35 - M.clamp(rise(x, z, dz, -dx, y0), -1, 1.6) * 1.4;
            if (sc < bs) { bs = sc; best = { x, z, dx, dz }; }
          }
        }
        if (!best && this.surfaceY(q.x, q.y) > 0.12 && this._cityNearDist(q.x, q.y) >= 3.6) {
          // 无路：门向顺着等高线（梯度的垂直方向）
          const e = 0.8, gx = this.surfaceY(q.x + e, q.y) - this.surfaceY(q.x - e, q.y), gz = this.surfaceY(q.x, q.y + e) - this.surfaceY(q.x, q.y - e);
          const gl = Math.hypot(gx, gz);
          best = gl > 1e-4 ? { x: q.x, z: q.y, dx: -gz / gl, dz: gx / gl } : { x: q.x, z: q.y, dx: 1, dz: 0 };
        }
        if (!best) { this.passesSkipped.push(p[0]); continue; }
        if (lpWalls) lpPassInto(lpWalls, this, best.x, best.z, best.dx, best.dz, p[3] || 'han', p[0]);
        else passInto(mb, this, best.x, best.z, best.dx, best.dz, p[3] || 'han');
        list.push({ name: p[0], x: best.x, z: best.z, y: this.surfaceY(best.x, best.z) + 1.0 });
        yield;
      }
      if (mb.count) {
        const m = SG.Gfx.mesh(yield* sliceGeometry(mb), SG.Gfx.masonryMaterial ? SG.Gfx.masonryMaterial() : SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
        m.name = 'Passes';
        this.root.add(m);
      }
      if (lpWalls && lpWalls.entries.length) {
        const m = new THREE.Group();
        m.name = 'Passes';
        this.root.add(m);
        yield* this._chunkBatches(lpWalls.entries, 40, 'Passes', m);
        for (const o of lpWalls.objects) this._inkProps.push(o);
        for (const o of lpWalls.objects) { o.traverse(c => { if (c.isMesh) c.userData.sharedGeometry = !c.geometry.attributes.uv; }); this.root.add(o); }
        for (const l of lpWalls.lamps) (this._lampSpecs || (this._lampSpecs = [])).push([l, { color: '#ffc27a', intensity: CITY_LAMP_I * 0.8, range: CITY_LAMP_R * 0.7, nightOnly: true, halo: true, haloSize: CITY_LAMP_H * 0.8 }]);
        yield;
      }
    }
    // 全图海域栅格（1 单位一格）：water = 海（含内海；不含河流湖泊，也不含高于 −0.3 的浅滩）；
    // cost = 水面 1、离陆地 2 格以内 1.5、陆地 14。分行生成（可分片）
    *_genSeaMask() {
      const res = 1.0, T = this.spec.water, G = this._geo;
      const X0 = Math.floor(T.x0), Z0 = Math.floor(T.z0);
      const nx = Math.ceil(T.x1 - X0) + 1, nz = Math.ceil(T.z1 - Z0) + 1, N = nx * nz;
      const water = new Uint8Array(N), near = new Uint8Array(N), cost = new Float32Array(N);
      this._traceTag = 'seamask';
      for (let j = 0; j < nz; j++) {
        const z = Z0 + j * res;
        for (let i = 0; i < nx; i++) { const x = X0 + i * res; water[j * nx + i] = G.inSea(x, z) && this.surfaceY(x, z) < -0.3 ? 1 : 0; }
        if ((j & 1) === 1) yield;
      }
      // 陆地向外膨胀 2 格（先横后纵，可分离的最大值滤波）
      // 按行分段（世界图约 100 万格，整张一步在负载下超过 10 ms）
      const tmp = new Uint8Array(N);
      this._traceTag = 'seamask.dilate';
      for (let j = 0; j < nz; j++) {
        for (let i = 0; i < nx; i++) {
          let v = 0;
          for (let d = -2; d <= 2 && !v; d++) { const ii = i + d; if (ii >= 0 && ii < nx && !water[j * nx + ii]) v = 1; }
          tmp[j * nx + i] = v;
        }
        if ((j & 7) === 7) yield;
      }
      yield;
      for (let j = 0; j < nz; j++) {
        for (let i = 0; i < nx; i++) {
          let v = 0;
          for (let d = -2; d <= 2 && !v; d++) { const jj = j + d; if (jj >= 0 && jj < nz && tmp[jj * nx + i]) v = 1; }
          const o = j * nx + i;
          near[o] = v;
          cost[o] = !water[o] ? 14 : v ? 1.5 : 1;
        }
        if ((j & 7) === 7) yield;
      }
      this._sea = { X0, Z0, nx, nz, res, water, cost };
      yield;
    }
    // 海上航线：海域栅格上的 A*（水面代价 1，近岸略高，陆地 14），再按视线拉直、Chaikin 平滑。返回 [x0, z0, x1, z1, ...]
    // 生成器：截取窗口与 A* 搜索中途让出（长航线的窗口可达数万格，一次搜完在负载下要 20 ms 以上）；返回值为航线
    *_seaPathGen(ax, az, bx, bz) {
      this._traceTag = 'seapath';
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
        if ((j & 31) === 31) yield;
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
      let found = false, pops = 0;
      while (heap.length) {
        const o = pop();
        if (closed[o]) continue;
        closed[o] = 1;
        if ((++pops & 255) === 0) yield;
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
        let checks = 0;
        while (a < n2 - 1) {
          let b = n2 - 1;
          while (b > a + 1 && !clear(pts[2 * a], pts[2 * a + 1], pts[2 * b], pts[2 * b + 1])) { b--; if ((++checks & 255) === 0) yield; }
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
    // 第三版修订：顶点只存中线位置，半宽方向放在 aOff（着色器按镜头距离缩放宽度、近处淡出，近看不再是白木板）；
    // 经过陆地的线段不画（A* 允许以高代价穿过陆地）
    _dashes(pts, P, O) {
      const DASH = 1.0, GAP = 0.7, PERIOD = DASH + GAP, geo = this._geo;
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
        if (geo && geo.inSea && !geo.inSea(mx, mz)) return;
        const px = -(ez - sz) / L, pz = (ex - sx) / L;
        const ys = Math.max(0.13, this.surfaceY(sx, sz) + 0.12), ye = Math.max(0.13, this.surfaceY(ex, ez) + 0.12);
        P.push(sx, ys, -sz, ex, ye, -ez, ex, ye, -ez);
        O.push(px, -pz, px, -pz, -px, pz);
        P.push(sx, ys, -sz, ex, ye, -ez, sx, ys, -sz);
        O.push(px, -pz, -px, pz, -px, pz);
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
      const tileSize = 60, T = this.spec.terrain;
      // 区块（60 单位）→ { near, far: MeshBuilder }：城体按区块合并（视锥剔除生效），近景为细致模型（js/culture-art.js），
      // 远景为简化模型（SG.Models.cityInto）；update 中按镜头距离切换
      const bodies = new Map();
      this._cityTiles = [];
      // 一个区块攒到约 2.4 万顶点就先合并成网格（世界剧本一个区块可有十几座城：一次转换顶点数组要十几 ms）
      const BODY_VERTS = 24000;
      const self = this;
      const flushBody = function* (tile, which) {
        self._traceTag = 'cities.flush';
        const mb = tile[which];
        if (!mb.count) return;
        const body = Gfx.mesh(yield* sliceGeometry(mb), which === 'near' && Gfx.masonryMaterial ? Gfx.masonryMaterial() : Gfx.lowPoly(), { castShadow: which === 'near', receiveShadow: which === 'near' });
        body.name = which === 'near' ? 'Cities' : 'CitiesFar';
        body.visible = which === 'far';
        self.root.add(body);
        tile.meshes[which].push(body);
        tile[which] = new SG.MeshBuilder();
      };
      // 旗帜：所有城共用一个实例化网格（颜色 = 势力色，摆动在 update 中）
      // 第四版：LP 旗（墨线）：旗面（白，乘势力色）与旗杆（不调色）两个实例化网格共用同一份实例矩阵
      const lpCities = !!(SG.LP && SG.LP.instances && SG.CultureArt && typeof SG.CultureArt.scatter === 'function' && SG.CultureArt.model);
      let flags;
      if (lpCities) {
        const fg = lpFlagGeometry();
        flags = lpInst(fg.cloth, n, { dynamic: true, wind: 0 });
        flags.name = 'Flags';
        const poles = lpInst(fg.pole, n, { dynamic: true, wind: 0, tint: false });
        poles.name = 'FlagPoles';
        poles.instanceMatrix = flags.instanceMatrix;
        if (poles.userData.outline) poles.userData.outline.instanceMatrix = flags.instanceMatrix;
        poles.instanceColor = null;
        flags.add(poles);
        flags.userData.poles = poles;
        flags.frustumCulled = false; poles.frustumCulled = false;
        if (flags.userData.outline) flags.userData.outline.frustumCulled = false;
        if (poles.userData.outline) poles.userData.outline.frustumCulled = false;
        for (let i = 0; i < n; i++) { flags.setMatrixAt(i, ZERO_MATRIX); flags.setColorAt(i, new THREE.Color(1, 1, 1)); }
        setInstCount(flags, n); setInstCount(poles, n);
      } else {
        flags = new THREE.InstancedMesh(MapView.flagGeometry(), Gfx.newLowPoly(), Math.max(1, n));
        flags.name = 'Flags';
        flags.castShadow = true; flags.receiveShadow = true;
        flags.frustumCulled = false;
        flags.userData.sharedGeometry = true;
        flags.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        for (let i = 0; i < flags.count; i++) { flags.setMatrixAt(i, ZERO_MATRIX); flags.setColorAt(i, new THREE.Color(1, 1, 1)); }
      }
      this._flags = flags;
      const cityList = [];
      this._flagInfo = [];
      // 势力光圈：贴合地表的圆盘，合并为一个网格（RGBA 顶点色）
      // 第四轮：ringLand = 每个顶点的陆地权重（水面上为 0：光圈不再横跨湖面、海湾，云南的褐色光圈不再像穿湖的路）
      const ringPos = [], ringUv = [], ringIdx = [], ringLand = [];
      this._ringRanges = new Map();
      const RING_N = 7, RING_R = 2.9;

      let k = 0;
      for (const c of state.cities) {
        const mx = this._cityX[c.id], mz = this._cityZ[c.id];
        const y = Math.max(0.3, this.surfaceY(mx, mz));
        const size = M.lerp(0.9, 1.5, M.inverseLerp(180, 650, c.town));
        // 城池模型按文化（js/culture-art.js）；汉地与未知文化用原模型，都城变体：汉地为洛阳、长安，其他文化为各势力的初始都城
        const CA = SG.CultureArt;
        const culture = CA ? CA.cultureOfCity(c) : 'han';
        const capital = CA ? CA.isCapital(c, culture) : (c.key === 'luoyang' || c.key === 'changan');
        const yaw = (c.id * 37) % 20 - 10;
        this._traceTag = 'cities';
        // 城体：烘焙到所在区块的合并网格中（Unity 坐标下的平移 × 绕 y 旋转）
        if (lpCities) {
          // 第四版：城池交给 SG.CultureArt.scatter（实例化 + 墨线 + 远近景）；这里只预先生成模型（每城一步，免得一次卡住）
          SG.CultureArt.model(culture, capital, c, { size });
          SG.CultureArt.model(culture, capital, c, { size, lod: 'far' });
          cityList.push({ x: mx, y, z: -mz, yaw: SG.yawToThree(yaw), size, culture, capital, city: c });
        } else {
        const ti = Math.floor((mx - T.x0) / tileSize), tj = Math.floor((mz - T.z0) / tileSize), tk = ti + ',' + tj;
        let tile = bodies.get(tk);
        if (!tile) {
          tile = { near: new SG.MeshBuilder(), far: new SG.MeshBuilder(), meshes: { near: [], far: [] }, cx: T.x0 + (ti + 0.5) * tileSize, cz: T.z0 + (tj + 0.5) * tileSize, lod: 1 };
          bodies.set(tk, tile);
          this._cityTiles.push(tile);
        }
        const xf = new THREE.Matrix4().makeTranslation(mx, y, mz).multiply(rot.makeRotationY(yaw * M.deg2rad));
        tile.near.M = xf;
        if (CA) CA.cityInto(tile.near, size, culture, capital, c);
        else SG.Models.cityInto(tile.near, size, null, capital);
        tile.near.M = null;
        tile.far.M = xf;
        if (CA && CA.cityFarInto) CA.cityFarInto(tile.far, size, culture, capital);
        else SG.Models.cityInto(tile.far, size, null, capital);
        tile.far.M = null;
        if (tile.near.count >= BODY_VERTS) { this._sub(0.8 * k / n); yield; yield* flushBody(tile, 'near'); }
        if (tile.far.count >= BODY_VERTS) { yield* flushBody(tile, 'far'); }
        }

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
            const sy = this.surfaceY(ux, uz);
            ringPos.push(ux, Math.min(Math.max(sy, 0.02), y + 0.45) + 0.12, -uz);
            ringLand.push(ringLandW(sy));
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
        this._sub(0.8 * (++k) / n);
        yield;
      }
      for (const tile of bodies.values()) { yield* flushBody(tile, 'near'); yield; yield* flushBody(tile, 'far'); yield; }
      if (lpCities) {
        this._traceTag = 'cities.scatter';
        const sc = this._cityScatter = SG.CultureArt.scatter(cityList, { chunk: this._cityChunk || 60, lodDist: CITY_LOD, inkDist: CITY_INK, name: 'Cities' });
        this._cityOv = { list: cityList, bins: null, keys: null, k: 0, group: null, on: false };
        sc.group.traverse(o => { if (o.isMesh) o.userData.sharedGeometry = true; });
        this.root.add(sc.group);
        // 投影代理：近景区块的城不自己投影（约 5 千三角形一座），改由同一区块的远景模型（≤ 400）只在阴影图里画一遍
        //（_cityShadowLOD：主画面里实例数临时置 0，three 不发绘制）。阴影是柔边的，远景模型的轮廓足够
        if (sc.scatter && sc.scatter.chunks) for (const c of sc.scatter.chunks) {
          if (!c.near || !c.far) continue;
          c.near.castShadow = false;
          const f = c.far;
          const pb = f.onBeforeRender, pa = f.onAfterRender;
          f.onBeforeRender = function (...a) { pb.apply(this, a); if (this.userData.shadowOnly) { this.userData.n = this.count; this.count = 0; } };
          f.onAfterRender = function (...a) { if (this.userData.shadowOnly) this.count = this.userData.n; pa.apply(this, a); };
        }
        // 城门灯笼、殿前灯、篝火：夜里点亮。地图比例下一座城的几盏灯合成一盏（灯位取平均、灯池放大到约 3.4 单位、一团光晕），
        // 氛围的灯池（SG.Gfx.mood 的 6 盏点光源）因此能照亮离视野中心最近的六座城。
        // 灯在地图第一次入夜（黄昏）时才交给氛围（_ensureLamps）：灯池的点光源一旦建立，白天每个像素也要遍历它们
        this._lampSpecs = this._lampSpecs || [];
        if (SG.Gfx.addLamp && sc.lamps.length) {
          const groups = [];
          for (const l of sc.lamps) {
            let g = null;
            for (const q of groups) if (Math.hypot(q.x / q.n - l.pos[0], q.z / q.n - l.pos[2]) < 2.4) { g = q; break; }
            if (!g) { g = { x: 0, y: 0, z: 0, n: 0, top: -Infinity, col: l.color, flicker: 0 }; groups.push(g); }
            g.x += l.pos[0]; g.y += l.pos[1]; g.z += l.pos[2]; g.n++; g.top = Math.max(g.top, l.pos[1]); g.flicker = Math.max(g.flicker, l.flicker || 0);
          }
          for (const g of groups) this._lampSpecs.push([[g.x / g.n, g.top + 0.25, g.z / g.n], { color: g.col || '#ffd08a', intensity: CITY_LAMP_I, range: CITY_LAMP_R,
            nightOnly: true, halo: true, haloSize: CITY_LAMP_H, flicker: g.flicker * 0.5 }]);
        }
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
      this._ringLand = new Float32Array(ringLand);
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
    // 第四轮：带 RGBA 顶点色（白色，A = 陆地权重：水面上为 0；材质开 vertexColors 时光圈在岸边淡出）
    drapedDisc(cx, cz, r, n, lift, maxY) {
      if (maxY === undefined) maxY = Infinity;
      const pos = [], uv = [], idx = [], col = [];
      for (let i = 0; i <= n; i++)
        for (let j = 0; j <= n; j++) {
          const ux = cx - r + 2 * r * i / n, uz = cz - r + 2 * r * j / n;
          const sy = this.surfaceY(ux, uz);
          pos.push(ux, Math.min(Math.max(sy, 0.02), maxY) + lift, -uz);
          uv.push(i / n, j / n);
          col.push(1, 1, 1, ringLandW(sy));
        }
      for (let i = 0; i < n; i++)
        for (let j = 0; j < n; j++) {
          const a = i * (n + 1) + j, b = a + 1, c = a + n + 2, d = a + n + 1;
          idx.push(a, c, b, a, d, c);
        }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      g.setAttribute('color', new THREE.Float32BufferAttribute(col, 4));
      g.setIndex(idx);
      g.computeBoundingSphere();
      return g;
    }

    // 第三版修订：云改为柔边的公告板云团（每朵 5–8 片朝向镜头的面片，共用一张程序生成的云团贴图：径向渐隐 + 两级噪声、
    // 上亮下灰），不再是不透明的一团团多边形球；不投影；位置更高（y 26–34）；战略视角（镜头低于约 95）整体隐去，
    // 只在标题 / 全图的远景里出现；触屏减半
    *_buildClouds() {
      const rnd = SG.SeededRandom(5);
      const world = this.region === 'world';
      this._cloudBox = world ? { x: 170, z: 115 } : null;
      const n = SG.isTouch ? Math.ceil(this.spec.clouds / 2) : this.spec.clouds;
      for (let i = 0; i < n; i++) {
        const puffs = 5 + rnd.next(4);
        const P = [], Cn = [], S = [], K = [], idx = [];
        for (let k = 0; k < puffs; k++) {
          const u = puffs > 1 ? k / (puffs - 1) : 0.5;
          const ox = (u - 0.5) * 8 + (rnd.nextDouble() - 0.5) * 1.6, oz = (rnd.nextDouble() - 0.5) * 3.2;
          const mid = 1 - Math.abs(u - 0.5) * 2;
          const r = 2.2 + mid * 1.6 + rnd.nextDouble() * 0.9, oy = 0.2 + mid * 1.1 + rnd.nextDouble() * 0.5;
          const shadeK = 0.9 + rnd.nextDouble() * 0.1;
          const base = P.length / 3;
          for (const [cx, cy] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) { P.push(ox, oy, -oz); Cn.push(cx, cy); S.push(r); K.push(shadeK); }
          idx.push(base, base + 1, base + 2, base, base + 2, base + 3);
        }
        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(P, 3));
        geo.setAttribute('aCorner', new THREE.Float32BufferAttribute(Cn, 2));
        geo.setAttribute('aSize', new THREE.Float32BufferAttribute(S, 1));
        geo.setAttribute('aShade', new THREE.Float32BufferAttribute(K, 1));
        geo.setIndex(idx);
        geo.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 1, 0), 12);
        const m = new THREE.Mesh(geo, cloudMaterial());
        m.name = 'Cloud';
        m.castShadow = false; m.receiveShadow = false;
        m.renderOrder = 21;
        let px, py, pz;
        if (world) {
          const b = this._cloudBox, c = this._viewCenter();
          px = c.x + (rnd.nextDouble() * 2 - 1) * b.x; pz = c.z + (rnd.nextDouble() * 2 - 1) * b.z;
        } else { px = rnd.nextDouble() * MapW; pz = rnd.nextDouble() * MapH; }
        py = 26 + rnd.nextDouble() * 8;
        m.position.copy(SG.U(px, py, pz));
        this.root.add(m);
        this._clouds.push(m);
        yield;
      }
    }
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
      mat.vertexColors = true;   // drapedDisc 的顶点 A = 陆地权重：选中光圈在水面上淡出
      const m = new THREE.Mesh(new THREE.BufferGeometry(), mat);
      m.name = 'SelectRing';
      m.renderOrder = 2;
      m.visible = false;
      m.castShadow = false; m.receiveShadow = false;
      this.root.add(m);
      this._selectRing = m;
      this._selectAngle = 0;
    }

    // ------------------------------------------------------ 氛围（第四版）--
    // setMood('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | 小时数 | null)：地图的时刻
    // （SG.LP.Mood：太阳方向与颜色、黄金时刻提亮、夜色调、城中灯笼的灯池）。null = 回到缺省：开局后是温暖的午后（MAP_HOUR），
    // 标题画面（.sg-title > .sg-title-hero 在页面上）是灯笼亮起的黄昏（TITLE_HOUR），两者之间平滑过渡。
    // 只在地图显示时生效：地图隐藏（攻城、野战画面借用主场景）时还原为构建前的氛围，再显示时重新套用。
    // followClock(true)：跟随 SG.Clock.hour()（INTEGRATION §10.7：月初 07:00 → 用完令牌 20:00，电脑回合入夜），平滑过渡；
    // setMood(…) 会关掉跟随。返回当前的氛围状态（SG.Gfx.moodState()）
    // setMood('title')：标题画面的黄昏（加色光晕，不建点光源灯池），game.js 可在标题流程开始时调、离开时 setMood(null)；
    // 不调时按 .sg-title > .sg-title-hero 是否在页面上自动判断
    setMood(what) {
      this._follow = false;
      _titleForce = what === 'title';
      if (_titleForce) what = null;
      this._moodWant = what == null ? null : typeof what === 'number' ? what : (SG.LP && SG.LP.MOOD_HOURS && SG.LP.MOOD_HOURS[what] != null ? SG.LP.MOOD_HOURS[what] : MAP_HOUR);
      return this._applyMood(this._moodWant != null ? this._moodWant : defaultHour());
    }
    followClock(on) {
      _titleForce = false;
      this._follow = on !== false;
      if (this._follow && SG.Clock && typeof SG.Clock.hour === 'function') { this._moodWant = SG.Clock.hour(); this._applyMood(this._moodWant); }
      return this._follow;
    }
    get moodHour() { return this._moodHour != null ? this._moodHour : defaultHour(); }
    _applyMood(h) {
      const Gfx = SG.Gfx;
      if (!Gfx || !Gfx.setMood || !Gfx.mood) return null;
      if (this._moodPrev === undefined) { const st = Gfx.moodState(); this._moodPrev = st ? st.hour : 12.5; }
      this._moodHour = h;
      if (!this.root || this.root.visible) { Gfx.setMood(h); this._ensureLamps(); }
      return Gfx.moodState();
    }
    // 第一次真正入夜（氛围的夜色 > 0.45：只夜里亮的灯这时才点亮，LP.Mood._lampOn）时把城与关的灯交给氛围；
    // 之后一直保留（夜里点亮、白天自动熄灭，地图隐藏时熄灭）。
    // 只在明确要了时刻（setMood / followClock）时建：缺省时刻（标题画面的黄昏）用 _updateGlow 的加色光晕代替
    // （第四轮：原阈值 0.15 在跟随时钟的月初 07:00（夜色 0.21）就建了灯池，整个白天都多付 6 盏点光源）
    _ensureLamps() {
      if (!(this._follow || this._moodWant != null)) return;
      const Gfx = SG.Gfx, st = Gfx && Gfx.moodState && Gfx.moodState();
      if (this._lampSpecs && this._lampSpecs.length && !this._poolWarm) this._prewarmLampPool();
      if (!this._lampSpecs || !this._lampSpecs.length || !st || !(st.night > LAMP_NIGHT) || !Gfx.addLamp) return;
      gateLampPool(Gfx.mood);
      this._lamps = this._lamps || [];
      for (const [pos, o] of this._lampSpecs) { const lamp = Gfx.addLamp(pos, o); if (lamp) this._lamps.push(lamp); }
      this._lampSpecs = null;
      if (this.root && !this.root.visible && Gfx.setLampLit) for (const l of this._lamps) Gfx.setLampLit(l, false);
    }
    // 第四轮：灯池第一次亮起（黄昏）时，所有看得见的 LP 材质都要编译「6 盏点光源」的着色器变体——SwiftShader 下一帧卡 10–20 秒，
    // 真显卡上也有明显的顿挫。在明确要了时刻（进入战略画面、followClock / setMood）时预先编译：临时放进同样多盏强度为 0 的
    // 点光源，renderer.compileAsync 编译场景里的全部材质（有 KHR_parallel_shader_compile 时不阻塞），并保留到画完下一帧
    // （画面不变；驱动 / SwiftShader 在第一次绘制时才真正生成代码）——与原来进战略画面时灯池常驻的第一帧代价相同。
    // 着色器变体只取决于点光源的盏数，之后黄昏亮灯只是换程序。每次构建地图做一次
    _prewarmLampPool() {
      this._poolWarm = true;
      const Gfx = SG.Gfx, r = Gfx && Gfx.renderer, mood = Gfx && Gfx.mood;
      if (!r || !Gfx.scene || !Gfx.camera || !mood || !this.root || !this.root.visible) { this._poolWarm = false; return; }
      const t0 = now();
      const pool = mood.pool || [];
      const n = Math.max(pool.length, Math.min(mood.maxLamps || 6, pool.length + this._lampSpecs.length));
      const vis = pool.map(l => l.visible), tmp = [];
      for (const l of pool) l.visible = false;
      for (let i = 0; i < n; i++) { const l = new THREE.PointLight(0xffffff, 0, 5, 0); l.castShadow = false; Gfx.scene.add(l); tmp.push(l); }
      try {
        if (r.compileAsync) r.compileAsync(Gfx.scene, Gfx.camera).catch(() => {});
        else if (r.compile) r.compile(Gfx.scene, Gfx.camera);
      } catch (e) { /* 预编译失败不影响画面：黄昏时照常按需编译 */ }
      pool.forEach((l, i) => { l.visible = vis[i]; });
      // 画过一次之后由 gateLampPool 的 update 移走
      gateLampPool(mood);
      if (mood._sgWarm) for (const l of mood._sgWarm.lights) if (l.parent) l.parent.remove(l);
      mood._sgWarm = { lights: tmp, renders: 0 };
      this._poolWarmMs = now() - t0;
    }
    // 地图销毁时移走还没画过的预编译光源
    _endPrewarm() {
      const mood = SG.Gfx && SG.Gfx.mood, w = mood && mood._sgWarm;
      if (!w) return;
      for (const l of w.lights) { if (l.parent) l.parent.remove(l); if (l.dispose) l.dispose(); }
      mood._sgWarm = null;
    }
    _updateMood(dt) {
      const Gfx = SG.Gfx;
      if (!Gfx || !Gfx.mood || !this.root) return;
      const vis = this.root.visible;
      if (vis !== this._moodVis) {
        // 显示 / 隐藏切换：地图的时刻 ↔ 构建前的时刻；夜里的城灯只在地图显示时点亮
        this._moodVis = vis;
        if (vis) { Gfx.setMood(this.moodHour); this._ensureLamps(); }
        else if (this._moodPrev !== undefined) Gfx.setMood(this._moodPrev);
        if (this._lamps && Gfx.setLampLit) for (const l of this._lamps) Gfx.setLampLit(l, vis);
      }
      if (!vis) { if (this._focusSet && Gfx.mood) { Gfx.mood.focus = null; this._focusSet = false; } return; }
      this._updateGlow(Gfx.moodState && Gfx.moodState());
      // 灯池给离视野中心最近的城（而不是离镜头最近的——俯视时镜头下方多在画面之外）
      if (this._lamps && this._lamps.length && Gfx.mood) {
        const c = this._viewCenter(), f = this._focus || (this._focus = [0, 0, 0]);
        f[0] = c.x; f[1] = 1; f[2] = -c.z;
        Gfx.mood.focus = f; this._focusSet = true;
      }
      const follow = this._follow && SG.Clock && typeof SG.Clock.hour === 'function';
      if (follow || this._moodWant == null) {
        const want = follow ? SG.Clock.hour() : defaultHour(), cur = this.moodHour;
        let d = want - cur;
        if (d > 12) d -= 24; else if (d < -12) d += 24;
        // 进入标题画面时直接跳到黄昏（标题刚淡入，看不出跳变）；其余（开局转回午后、跟随时钟）平滑过渡
        if (!follow && want === TITLE_HOUR && Math.abs(d) > 0.01) { this._moodHour = want; Gfx.setMood(want); }
        else if (Math.abs(d) > 0.01) {
          // 每秒约 2 小时的平滑过渡（一个月的白天在几步之间看得见地走过；月初的黎明不跳变）；离开标题画面回到午后快一些（每秒 5 小时）
          const step = Math.sign(d) * Math.min(Math.abs(d), Math.max(0.02, (follow ? 2 : 5) * dt));
          this._moodHour = ((cur + step) % 24 + 24) % 24;
          Gfx.setMood(this._moodHour);
          this._ensureLamps();
        }
      }
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
        // 第三轮：无主城的光圈改为偏暗的暖灰（原来的浅灰白在近景里像一道竖直的雾带）
        const col = owned ? SG.Gfx.color(state.factions[c.owner].color) : new THREE.Color(0.46, 0.43, 0.38);
        // 旗帜
        const fi = this._flagInfo[c.id];
        fi.visible = !!owned;
        this._flags.setColorAt(c.id, col);
        // 光圈
        const a = owned ? (c.owner === state.player ? 0.85 : 0.6) : 0.22;
        const rr = this._ringRanges.get(c.id), land = this._ringLand;
        for (let k = 0; k < rr.count; k++) {
          const o = (rr.start + k) * 4;
          arr[o] = col.r; arr[o + 1] = col.g; arr[o + 2] = col.b; arr[o + 3] = a * (land ? land[rr.start + k] : 1);
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
      // 镜头高度低于约 95（战略视角）时云整体淡出
      const cloudG = cam ? M.smoothStep(0, 1, M.clamp01((cam.position.y - CLOUD_HIDE_Y) / CLOUD_SHOW_DY)) : 1;
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
          const a = f * f * (3 - 2 * f) * cloudG;
          c.material.uniforms.uOpacity.value = a;
          c.visible = a > 0.01;
        }
      }
      if (cam && this.root.visible) {
        // 地形分块：选择可见块、补建细块（每帧最多 3 ms；大量缺块时——如镜头瞬移——放宽到 8 ms）
        if (this._lod) this._lod.update(cam, this._lod.queue.length > 24 ? 8 : 3);
        this._updateInstLOD(cam);
        this._updateGrass(cam);
      }
      // 世界地图：镜头拉远时雾随之推远（地图隐藏时——如战斗——恢复原值）
      if (this.region === 'world' && cam && SG.Gfx.scene && SG.Gfx.scene.fog) {
        const fog = SG.Gfx.scene.fog;
        if (!this._fogBase) this._fogBase = { near: fog.near, far: fog.far };
        const k = this.root.visible ? Math.max(1, cam.position.y / 105) : 1;
        fog.near = this._fogBase.near * k; fog.far = this._fogBase.far * k;
      }
      this._updateFlags();
      this._updateMood(dt);
    }
    // 树木 / 石块 / 村舍：按区块离镜头的距离切换近景（平滑）与远景（简化）几何体；镜头很高时整类隐去
    _updateInstLOD(cam) {
      const cy = cam.position.y, cx = cam.position.x, cz = -cam.position.z;
      const hideTrees = cy > (this._treesHidden ? 165 : 180);
      this._treesHidden = hideTrees;
      const hideSmall = cy > 120;
      // 镜头移动约 3 单位以上才重新分配（每次只改动跨越边界的区块）
      const key = Math.round(cx / 4) + ',' + Math.round(cz / 4) + ',' + Math.round(cy / 4);
      // 每帧最多约 2.5 ms：没轮到的区块（key 未更新）下一帧继续
      const t0 = now();
      const apply = (tiles, hide, R0) => {
        if (!tiles) return;
        for (const t of tiles) {
          if (t.mt) { if (!hide && !t.hidden && t.key !== key && now() - t0 > 2.5) continue; this._mergedTileLOD(t, hide, R0, key, cx, cy, cz); continue; }
          if (!hide && t.key !== key && now() - t0 > 2.5) { if (t.tree) { for (const m of t.near) m.visible = true; for (const m of t.hero) if (m) m.visible = true; t.far.visible = true; } else t.near.visible = t.far.visible = true; continue; }
          if (t.tree) { this._treeTileLOD(t, hide, R0, key, cx, cy, cz); continue; }
          t.near.visible = t.far.visible = !hide;
          if (hide || t.key === key) continue;
          t.key = key;
          const dh = Math.hypot(t.cx - cx, t.cz - cz);
          const dmin = Math.hypot(Math.max(0, dh - t.r), cy), dmax = Math.hypot(dh + t.r, cy);
          if (t.hideFar && dmin > t.hideFar) { t.near.visible = t.far.visible = false; continue; }
          const R = t.nearR || R0;
          if (dmin > R) { if (t.mode !== 'far') { t.far.instanceMatrix.array.set(t.mats); t.far.instanceColor.array.set(t.cols); setInstCount(t.far, t.n); setInstCount(t.near, 0); t.far.instanceMatrix.needsUpdate = t.far.instanceColor.needsUpdate = true; t.mode = 'far'; } continue; }
          if (dmax < R) { if (t.mode !== 'near') { t.near.instanceMatrix.array.set(t.mats); t.near.instanceColor.array.set(t.cols); setInstCount(t.near, t.n); setInstCount(t.far, 0); t.near.instanceMatrix.needsUpdate = t.near.instanceColor.needsUpdate = true; t.mode = 'near'; } continue; }
          // 区块跨越近景边界：逐实例分配
          const NM = t.near.instanceMatrix.array, NC = t.near.instanceColor.array, FM = t.far.instanceMatrix.array, FC = t.far.instanceColor.array;
          let a = 0, f = 0;
          const R2 = R * R - cy * cy;
          for (let i = 0; i < t.n; i++) {
            const dx = t.xz[i * 2] - cx, dz = t.xz[i * 2 + 1] - cz;
            if (dx * dx + dz * dz < R2) {
              for (let k = 0; k < 16; k++) NM[a * 16 + k] = t.mats[i * 16 + k];
              NC[a * 3] = t.cols[i * 3]; NC[a * 3 + 1] = t.cols[i * 3 + 1]; NC[a * 3 + 2] = t.cols[i * 3 + 2];
              a++;
            } else {
              for (let k = 0; k < 16; k++) FM[f * 16 + k] = t.mats[i * 16 + k];
              FC[f * 3] = t.cols[i * 3]; FC[f * 3 + 1] = t.cols[i * 3 + 1]; FC[f * 3 + 2] = t.cols[i * 3 + 2];
              f++;
            }
          }
          setInstCount(t.near, a); setInstCount(t.far, f); t.mode = 'split';
          t.near.instanceMatrix.needsUpdate = t.near.instanceColor.needsUpdate = true;
          t.far.instanceMatrix.needsUpdate = t.far.instanceColor.needsUpdate = true;
        }
      };
      apply(this._treeTiles, hideTrees, this._lpVeg ? VEG_NEAR : 42);
      apply(this._scatter, hideSmall, 50);
      // 城池：近景细致模型 / 远景简化模型；镜头很高（全图）时换成按 200 单位合并的远景（_cityOverview）
      if (this._cityScatter && !this._cityOverview(cy)) { this._cityScatter.update(cam); this._cityShadowLOD(); }
      // 桥、关隘：远处不描墨线（固定像素宽的墨线会把几十像素大的模型涂黑）——按对象中心到镜头的距离
      if (this._inkProps) for (const o of this._inkProps) {
        if (!o.userData.inkC) { o.updateMatrixWorld(true); const b = new THREE.Box3().setFromObject(o); o.userData.inkC = b.getCenter(new THREE.Vector3()); o.userData.inkR = b.getSize(new THREE.Vector3()).length() * 0.5; o.userData.inkOn = true; }
        const d = Math.max(0, o.userData.inkC.distanceTo(cam.position) - Math.min(o.userData.inkR, 30)), on = o.userData.inkOn ? d < PROP_INK * 1.08 : d < PROP_INK * 0.92;
        if (on !== o.userData.inkOn) { o.userData.inkOn = on; o.traverse(c => { if (c.name === 'outline' || (c.material && c.material.userData && c.material.userData.lpKind === 'outline')) c.visible = on; }); }
        const vis = o.userData.visOn !== false ? d < PROP_HIDE * 1.05 : d < PROP_HIDE * 0.95;
        if (vis !== (o.userData.visOn !== false)) { o.userData.visOn = vis; o.visible = vis; }
        const sh = o.userData.shOn !== false ? d < PROP_SHADOW * 1.05 : d < PROP_SHADOW * 0.95;
        if (sh !== (o.userData.shOn !== false)) {
          o.userData.shOn = sh;
          o.traverse(c => { if (!c.isMesh) return; if (c.userData.cs0 === undefined) c.userData.cs0 = c.castShadow; c.castShadow = sh && c.userData.cs0; });
        }
      }
      if (this._cityTiles) for (const t of this._cityTiles) {
        const d = Math.hypot(t.cx - cx, t.cz - cz, cy);
        const lod = d < (t.lod ? 92 : 98) ? 0 : 1;
        if (lod === t.lod) continue;
        t.lod = lod;
        for (const m of t.meshes.near) m.visible = lod === 0;
        for (const m of t.meshes.far) m.visible = lod === 1;
      }
    }
    // 缺省时刻（标题画面的黄昏）不建点光源灯池（灯池一旦建立，白天每个像素也要遍历它们：SwiftShader 下慢约 15–20%）：
    // 城与关的灯改用一批加色光晕（LP.HaloBatch，一次绘制）——每处一大团暗金的「灯池」光 + 一颗亮的灯晕；建了真灯就熄掉
    _updateGlow(st) {
      // （第四轮：明确要了时刻（跟随时钟 / setMood）时不用光晕：真灯在夜色 0.45 时才建，之前若先建光晕，黄昏会先冒出一团加色光晕
      // 再换成真灯，还多编译一次光晕材质）
      const want = !(this._lamps && this._lamps.length) && !(this._follow || this._moodWant != null) && !!st && st.night > 0.2;
      if (!this._glow) {
        if (!want || !this._lampSpecs || !this._lampSpecs.length || !SG.LP || !SG.LP.HaloBatch) return;
        const specs = this._lampSpecs.slice(), b = new SG.LP.HaloBatch(specs.length * 2);
        b.points.name = 'CityGlow';
        this.root.add(b.points);
        this._glow = { b, specs, k: -1 };
      }
      const g = this._glow, k = want ? Math.round(M.clamp01((st.night - 0.2) / 0.45) * 40) / 40 : 0;
      if (k === g.k) return;
      g.k = k;
      const H = SG.LP.Paint.hex;
      let i = 0;
      for (const [pos, o] of g.specs) {
        const c = H(o.color || '#ffd08a'), sz = o.haloSize || CITY_LAMP_H;
        g.b.set(i++, [pos[0], pos[1] - 0.2, pos[2]], [c[0] * 0.34 * k, c[1] * 0.26 * k, c[2] * 0.15 * k], sz * 5, k > 0);
        g.b.set(i++, pos, [c[0] * k, c[1] * 0.92 * k, c[2] * 0.78 * k], sz * 1.1, k > 0);
      }
      g.b.commit(i);
    }
    // 全图视角的城：镜头高于 CITY_OV_Y 时所有城本来都是远景模型（≤ 400 三角形，每区块每种模型一次绘制，世界全图约 170 次）；
    // 这时改画按 200 单位合并的远景（同样的几何体、同样的位置，世界全图约十次绘制）。合并网格第一次需要时逐帧建（每帧一块），
    // 建好之前照旧画实例化的城。返回 true = 正在用合并网格
    _cityOverview(cy) {
      const ov = this._cityOv, sc = this._cityScatter;
      if (!ov || !sc || !SG.CultureArt || !SG.LP) return false;
      const want = cy > (ov.on ? CITY_OV_Y * 0.92 : CITY_OV_Y);
      if (!want) {
        if (ov.on) { ov.on = false; sc.group.visible = true; ov.group.visible = false; }
        return false;
      }
      if (ov.on) return true;
      if (!ov.bins) {
        ov.bins = new Map();
        const q = new THREE.Quaternion(), e = new THREE.Euler();
        for (const it of ov.list) {
          const f = SG.CultureArt.model(it.culture, !!it.capital, it.city || null, { size: it.size || 1, lod: 'far' });
          if (!f) continue;
          e.set(0, it.yaw || 0, 0); q.setFromEuler(e);
          const s = it.size || 1, m = new THREE.Matrix4().compose(new THREE.Vector3(it.x, it.y, it.z), q, new THREE.Vector3(s, s, s));
          const key = Math.floor(it.x / 200) + ',' + Math.floor(it.z / 200);
          let b = ov.bins.get(key);
          if (!b) ov.bins.set(key, (b = []));
          for (const g of [f.ink, f.plain]) if (lpMergeable(g)) b.push({ geo: g, matrix: m });
        }
        ov.keys = [...ov.bins.keys()];
        ov.group = new THREE.Group();
        ov.group.name = 'CitiesOverview';
        ov.group.visible = false;
        this.root.add(ov.group);
        return false;
      }
      if (ov.k < ov.keys.length) {
        const list = ov.bins.get(ov.keys[ov.k++]);
        if (list.length) {
          const mesh = SG.LP.mesh(SG.LP.mergeGeometries(list), { outline: false, castShadow: false, receiveShadow: true });
          mesh.name = 'CitiesOverview';
          mesh.matrixAutoUpdate = false;
          ov.group.add(mesh);
        }
        if (ov.k < ov.keys.length) return false;
        ov.bins = new Map();   // 建完：释放清单（保留空表，不再重建）
      }
      ov.on = true; sc.group.visible = false; ov.group.visible = true;
      return true;
    }
    // 城的投影代理（见 _genCities）：近景区块的远景模型可见、投影、在主画面里不画；远景区块照常画、不投影
    _cityShadowLOD() {
      const sc = this._cityScatter && this._cityScatter.scatter;
      if (!sc || !sc.chunks) return;
      for (const c of sc.chunks) {
        if (!c.near || !c.far) continue;
        const proxy = !c.isFar && c.near.visible !== false;
        c.far.userData.shadowOnly = proxy;
        c.far.castShadow = proxy;
        c.far.visible = proxy || !!c.isFar;
      }
    }
    // 合并远景的区块（_lpMergedTiles）：整块在 R 外 → 只画合并网格；整块在 R 内 → 只画近景；跨界 → 逐实例分配，
    // 用近景的实例在合并网格里由 aHide 挪走
    _mergedTileLOD(t, hide, R0, key, cx, cy, cz) {
      if (hide) {
        if (!t.hidden) { if (t.far) t.far.visible = false; for (const m of t.near) m.visible = false; t.hidden = true; }
        return;
      }
      if (t.hidden) { t.hidden = false; t.key = ''; t.mode = ''; }
      if (t.key === key) return;
      t.key = key;
      const dh = Math.hypot(t.cx - cx, t.cz - cz);
      const dmin = Math.hypot(Math.max(0, dh - t.r), cy), dmax = Math.hypot(dh + t.r, cy);
      if (t.hideFar && dmin > t.hideFar) {
        if (t.mode !== 'off') { if (t.far) t.far.visible = false; for (const m of t.near) m.visible = false; t.mode = 'off'; }
        return;
      }
      const R = t.nearR || R0, mode = dmin > R ? 'far' : dmax < R ? 'near' : 'split';
      if (mode !== 'split' && mode === t.mode) return;
      const cnt = t.near.map(() => 0), H = t.hide ? t.hide.array : null, R2 = R * R - cy * cy;
      let anyFar = false, dirty = false;
      for (let i = 0; i < t.n; i++) {
        let isNear = mode === 'near';
        if (mode === 'split') { const dx = t.xz[i * 2] - cx, dz = t.xz[i * 2 + 1] - cz; isNear = dx * dx + dz * dz < R2; }
        if (!t.vcount[i]) isNear = true;   // 不能合并的远景：一律用近景
        if (isNear) {
          const g = t.gi[i], im = t.near[g], a = cnt[g]++;
          const NM = im.instanceMatrix.array, NC = im.instanceColor.array;
          for (let k = 0; k < 16; k++) NM[a * 16 + k] = t.mats[i * 16 + k];
          NC[a * 3] = t.cols[i * 3]; NC[a * 3 + 1] = t.cols[i * 3 + 1]; NC[a * 3 + 2] = t.cols[i * 3 + 2];
        } else anyFar = true;
        if (H && t.vcount[i] && mode !== 'near') {
          const hv = isNear ? 1 : 0, v0 = t.vstart[i];
          if (H[v0] !== hv) { H.fill(hv, v0, v0 + t.vcount[i]); dirty = true; }
        }
      }
      if (dirty) t.hide.needsUpdate = true;
      t.near.forEach((im, k) => {
        setInstCount(im, cnt[k]);
        im.visible = cnt[k] > 0;
        if (cnt[k]) { im.instanceMatrix.needsUpdate = true; im.instanceColor.needsUpdate = true; }
      });
      if (t.far) t.far.visible = anyFar;
      t.mode = mode;
    }
    // 树木区块：镜头 R 以内的树用平滑模型（按树种分到各近景网格），其余写入公告板网格
    _treeTileLOD(t, hide, R, key, cx, cy, cz) {
      for (const m of t.near) m.visible = !hide;
      for (const m of t.hero) if (m) m.visible = !hide;
      t.far.visible = !hide;
      if (hide || t.key === key) return;
      t.key = key;
      const dh = Math.hypot(t.cx - cx, t.cz - cz);
      if (t.lp) {
        const dm = Math.hypot(Math.max(0, dh - t.r), cy), ink = t.ink ? dm < TREE_INK * 1.08 : dm < TREE_INK * 0.92;
        if (ink !== t.ink) { t.ink = ink; for (const m of t.near) if (m.userData.outline) m.userData.outline.visible = ink; }
      }
      const allFar = Math.hypot(Math.max(0, dh - t.r), cy) > R;
      if (allFar && t.mode === 'far') return;
      const R2 = R * R - cy * cy;
      // 特写带（第三版修订）：镜头 20 单位以内的树用 lod 2 模型（各树种的特写网格首次用到时才建）
      const anyHero = !t.lp && Math.hypot(Math.max(0, dh - t.r), cy) < TREE_HERO_R, RH2 = TREE_HERO_R * TREE_HERO_R - cy * cy;
      const cnt = t.near.map(() => 0), hcnt = t.near.map(() => 0);
      const FM = t.far.instanceMatrix.array, FC = t.far.instanceColor.array, FA = t.far.geometry.getAttribute('aCell'), FAa = FA.array;
      let f = 0;
      for (let i = 0; i < t.n; i++) {
        const dx = t.xz[i * 2] - cx, dz = t.xz[i * 2 + 1] - cz;
        if (!allFar && dx * dx + dz * dz < R2) {
          const g = t.kind[i];
          let im, a;
          if (anyHero && dx * dx + dz * dz < RH2) {
            im = t.hero[g];
            if (!im) {
              im = t.hero[g] = new THREE.InstancedMesh(treeGeometry(t.kinds[g], 2), treeMaterial('leaf'), t.gcount[g]);
              im.name = 'TreesHero';
              im.userData.sharedGeometry = true;
              im.castShadow = true; im.receiveShadow = true;
              im.setColorAt(0, _col1);
              im.boundingSphere = t.near[g].boundingSphere;
              this.root.add(im);
            }
            a = hcnt[g]++;
          } else { im = t.near[g]; a = cnt[g]++; }
          const NM = im.instanceMatrix.array, NC = im.instanceColor.array;
          for (let k = 0; k < 16; k++) NM[a * 16 + k] = t.mats[i * 16 + k];
          NC[a * 3] = t.cols[i * 3]; NC[a * 3 + 1] = t.cols[i * 3 + 1]; NC[a * 3 + 2] = t.cols[i * 3 + 2];
        } else {
          for (let k = 0; k < 16; k++) FM[f * 16 + k] = t.smats[i * 16 + k];
          FC[f * 3] = t.cols[i * 3]; FC[f * 3 + 1] = t.cols[i * 3 + 1]; FC[f * 3 + 2] = t.cols[i * 3 + 2];
          FAa[f] = t.cells[i];
          f++;
        }
      }
      t.near.forEach((im, k) => { setInstCount(im, cnt[k]); im.instanceMatrix.needsUpdate = true; im.instanceColor.needsUpdate = true; });
      t.hero.forEach((im, k) => { if (im) { im.count = hcnt[k]; im.instanceMatrix.needsUpdate = true; im.instanceColor.needsUpdate = true; } });
      t.far.count = f;
      t.far.instanceMatrix.needsUpdate = true; t.far.instanceColor.needsUpdate = true; FA.needsUpdate = true;
      t.mode = allFar ? 'far' : 'split';
    }
    // 镜头附近的草丛与野花（每格 0.75，按群落与坡度取舍；镜头移动 3 单位以上时重新铺设）
    _initGrass() {
      const mk = (geo, n, name) => {
        const im = new THREE.InstancedMesh(geo, grassMaterial(), n);
        im.name = name;
        im.userData.sharedGeometry = true;
        im.frustumCulled = false;
        im.castShadow = false; im.receiveShadow = true;
        im.count = 0;
        im.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        im.setColorAt(0, new THREE.Color(1, 1, 1));
        im.instanceColor.setUsage(THREE.DynamicDrawUsage);
        this.root.add(im);
        return im;
      };
      if (this._lpVeg) {
        const lk = (geo, n, name) => {
          const im = lpInst(geo, n, { outline: false, shadow: false, dynamic: true, wind: 1 });
          im.name = name;
          im.frustumCulled = false;
          this.root.add(im);
          return im;
        };
        this._grassT = lk(lpGrassGeometry(), GRASS_MAX, 'Grass');
        this._flowerT = lk(lpFlowerGeometry(), FLOWER_MAX, 'Flowers');
      } else {
        this._grassT = mk(tuftGeometry(), GRASS_MAX, 'Grass');
        this._flowerT = mk(flowerGeometry(), FLOWER_MAX, 'Flowers');
      }
      this._grassKey = null;
    }
    _updateGrass(cam) {
      const gt = this._grassT, ft = this._flowerT, T = this._tex;
      if (!gt || !T) return;
      const vc = this._viewCenter();
      const dist = Math.hypot(cam.position.x - vc.x, cam.position.y, -cam.position.z - vc.z);
      if (dist > 82) { if (gt.count) { setInstCount(gt, 0); setInstCount(ft, 0); this._grassKey = null; this._grassJob = null; } return; }
      const R = M.clamp(dist * 0.62, 12, 34);
      const key = Math.round(vc.x / 4) + ',' + Math.round(vc.z / 4) + ',' + Math.round(R / 4);
      if (key !== this._grassKey) { this._grassKey = key; this._grassJob = this._grassFill(vc.x, vc.z, R); }
      // 重新铺设分摊到几帧（每帧约 2 ms）
      const job = this._grassJob;
      if (!job) return;
      const t0 = now();
      while (now() - t0 < 2) { if (job.next().done) { this._grassJob = null; break; } }
    }
    *_grassFill(cx0, cz0, R) {
      const gt = this._grassT, ft = this._flowerT, T = this._tex, cell = 0.8, lp = !!this._lpVeg;
      const GM = gt.instanceMatrix.array, GC = gt.instanceColor.array, FM = ft.instanceMatrix.array, FC = ft.instanceColor.array;
      let n = 0, nf = 0, work = 0;
      const i0 = Math.floor((cx0 - R) / cell), i1 = Math.ceil((cx0 + R) / cell), j0 = Math.floor((cz0 - R) / cell), j1 = Math.ceil((cz0 + R) / cell);
      const FLOWER = [[0.98, 0.96, 0.9], [1, 0.86, 0.3], [0.92, 0.3, 0.26], [0.72, 0.5, 0.9], [0.98, 0.62, 0.78], [0.5, 0.62, 1]];
      for (let i = i0; i <= i1 && n < GRASS_MAX; i++) {
        for (let j = j0; j <= j1 && n < GRASS_MAX; j++) {
          if (work > 160) { work = 0; yield; }
          let hsh = Math.imul(i * 73856093 ^ j * 19349663, 0x9e3779b1);
          const rn = () => { hsh = Math.imul(hsh ^ (hsh >>> 15), 0x2c1b3c6d); hsh ^= hsh >>> 12; return ((hsh >>> 0) % 10000) / 10000; };
          const x = (i + rn()) * cell, z = (j + rn()) * cell;
          const d = Math.hypot(x - cx0, z - cz0);
          work += 0.08;   // 被跳过的格也计一点工作量，分片更均匀
          if (d > R) continue;
          const ti = Math.round((x - T.x0) / T.step), tj = Math.round((z - T.z0) / T.step);
          if (ti < 0 || tj < 0 || ti >= T.w || tj >= T.h) continue;
          const q = (tj * T.w + ti) * 4;
          const sand = T.m[q] / 255, snow = T.m[q + 2] / 255, farm = 1 - T.m[q + 3] / 255, forest = T.c[q + 3] / 255;
          const p = (1 - sand) * (1 - snow) * (1 - farm * 0.85) * (1 - forest * 0.55);
          if (p < 0.15 || rn() > p * 0.8) continue;
          work++;
          const h = this.surfaceY(x, z);
          if (h < 0.5 || h > 3.4) continue;
          const sl = Math.abs(this.surfaceY(x + 0.4, z) - h) + Math.abs(this.surfaceY(x, z + 0.4) - h);
          if (sl > 0.5) continue;
          const fade = 1 - ss(R * 0.72, R, d);
          const flowerPick = rn();
          const s = (0.75 + rn() * 0.6) * (0.35 + 0.65 * fade) * (1 + forest * 0.3) * (lp ? (flowerPick < 0.035 * (1 - forest) && nf < FLOWER_MAX ? FLOWER_LP_K : GRASS_LP_K) : 1), yaw = rn() * 6.283, c = Math.cos(yaw) * s, sn = Math.sin(yaw) * s;
          const k = 0.92 + rn() * 0.3;
          const flower = flowerPick < (lp ? 0.035 : 0.075) * (1 - forest) && nf < FLOWER_MAX;
          const M4 = flower ? FM : GM, o = (flower ? nf : n) * 16;
          M4[o] = c; M4[o + 1] = 0; M4[o + 2] = -sn; M4[o + 3] = 0;
          M4[o + 4] = 0; M4[o + 5] = s; M4[o + 6] = 0; M4[o + 7] = 0;
          M4[o + 8] = sn; M4[o + 9] = 0; M4[o + 10] = c; M4[o + 11] = 0;
          M4[o + 12] = x; M4[o + 13] = h - 0.02; M4[o + 14] = -z; M4[o + 15] = 1;
          if (flower) {
            const fc = FLOWER[(hsh >>> 3) % FLOWER.length];
            FC[nf * 3] = fc[0]; FC[nf * 3 + 1] = fc[1]; FC[nf * 3 + 2] = fc[2];
            nf++;
          } else {
            if (lp) { GC[n * 3] = M.clamp(T.c[q] / 255 / LUSH[0] * k, 0.75, 1.3); GC[n * 3 + 1] = M.clamp(T.c[q + 1] / 255 / LUSH[1] * k, 0.75, 1.3); GC[n * 3 + 2] = M.clamp(T.c[q + 2] / 255 / LUSH[2] * k, 0.75, 1.3); }
            else { GC[n * 3] = Math.min(1, T.c[q] / 255 * k * 1.08); GC[n * 3 + 1] = Math.min(1, T.c[q + 1] / 255 * k * 1.1); GC[n * 3 + 2] = Math.min(1, T.c[q + 2] / 255 * k); }
            n++;
          }
        }
      }
      setInstCount(gt, n); setInstCount(ft, nf);
      gt.instanceMatrix.needsUpdate = true; gt.instanceColor.needsUpdate = true;
      ft.instanceMatrix.needsUpdate = true; ft.instanceColor.needsUpdate = true;
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
  MapView._internals = { EdgeIndex, FeatureGrid, Geo, classicSpec, worldSpec, geoCache: _geoCache, vegImpostors, VEG, VEG_KINDS };

  SG.MapView = MapView;
})();
