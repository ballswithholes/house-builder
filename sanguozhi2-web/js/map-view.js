'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战略地图
   ← View/MapView.cs：程序化生成的低多边形中国地形（海岸线、河流、湖泊、山脉、
     地形块、水面、树木、道路、城池与旗帜、势力光圈、云、选择光圈、点选、行军动画）
   所有地理计算沿用 Unity 坐标（x 向东，z = 地图 y 向北），
   网格交给 MeshBuilder 转换；物体位置用 SG.U(x, y, z)。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const MapW = 112, MapH = 100;
  function LL(lon, lat) { return { x: (lon - 100) * 5, y: (lat - 23) * 5.6 }; }

  // ---------------------------------------------------------- 地理数据 --
  const Coast = [
    LL(124, 41.5), LL(121.5, 40.8), LL(119.6, 39.9), LL(118.3, 39.1), LL(117.7, 38.8), LL(118.0, 38.2), LL(118.9, 37.6),
    LL(119.4, 37.1), LL(120.6, 37.7), LL(122.6, 37.3), LL(121.2, 36.6), LL(120.2, 35.9), LL(119.3, 35.0), LL(120.4, 34.3),
    LL(120.9, 32.8), LL(121.9, 31.6), LL(121.0, 30.7), LL(122.0, 29.8), LL(121.5, 28.6), LL(120.4, 27.3), LL(119.6, 26.0),
    LL(118.6, 24.6), LL(117.0, 23.5), LL(114.8, 22.6), LL(113.4, 22.2), LL(111.2, 21.5), LL(109.6, 21.3), LL(108.5, 21.6),
    LL(107.0, 21.2), LL(106.0, 19.0),
  ];
  const Rivers = [
    // 黄河
    [LL(99, 35.4), LL(101.5, 36.0), LL(103.8, 36.1), LL(105.8, 37.4), LL(106.6, 39.4), LL(108.4, 40.6), LL(110.9, 40.3), LL(111.4, 38.7), LL(110.6, 36.4), LL(110.3, 34.7), LL(112.4, 34.9), LL(114.6, 35.0), LL(116.0, 36.2), LL(117.4, 37.3), LL(118.9, 37.75)],
    // 长江
    [LL(99, 26.8), LL(101.5, 26.5), LL(103.2, 27.6), LL(104.6, 28.8), LL(106.5, 29.55), LL(108.4, 30.7), LL(110.3, 31.0), LL(111.4, 30.7), LL(112.3, 30.2), LL(113.2, 29.5), LL(114.3, 30.55), LL(115.6, 29.8), LL(117.0, 30.5), LL(118.4, 31.6), LL(119.5, 32.2), LL(121.0, 31.7), LL(121.9, 31.5)],
    // 汉水
    [LL(106.4, 33.1), LL(108.5, 32.8), LL(110.8, 32.6), LL(112.1, 32.05), LL(112.6, 31.2), LL(113.6, 30.7), LL(114.3, 30.55)],
    // 淮河
    [LL(112.5, 32.4), LL(114.4, 32.4), LL(116.4, 32.5), LL(118.0, 33.0), LL(119.3, 33.6), LL(120.4, 34.2)],
    // 湘江
    [LL(111.0, 25.6), LL(112.6, 26.9), LL(113.0, 28.2), LL(112.9, 29.3)],
    // 赣江
    [LL(114.9, 25.8), LL(115.0, 27.3), LL(115.9, 28.7), LL(116.2, 29.3)],
  ];
  const Lakes = [[112.8, 29.3, 3.2], [116.3, 29.1, 2.6], [120.0, 31.0, 1.5]].map(l => { const p = LL(l[0], l[1]); return { x: p.x, y: p.y, r: l[2] }; });
  // 山脉：起点、终点
  const Ridges = [
    [104.5, 34.0, 111.8, 34.0],   // 秦岭
    [113.6, 35.0, 114.6, 40.0],   // 太行
    [105.5, 32.3, 110.0, 31.6],   // 大巴山
    [109.5, 25.4, 116.0, 24.9],   // 南岭
    [116.2, 26.0, 118.6, 28.6],   // 武夷
    [115.0, 40.6, 119.0, 40.4],   // 燕山
    [110.9, 36.0, 111.6, 39.2],   // 吕梁
    [98.0, 39.0, 103.2, 37.0],    // 祁连
    [113.6, 31.6, 116.4, 31.1],   // 大别山
    [109.0, 30.2, 110.6, 31.6],   // 巫山
    [108.0, 27.0, 110.4, 29.4],   // 武陵山
  ].map(r => { const a = LL(r[0], r[1]), b = LL(r[2], r[3]); return { ax: a.x, ay: a.y, bx: b.x, by: b.y }; });

  // 海域多边形：海岸线 + 东侧远点。
  // （C# 以 (220,-40) 收尾，会把地图以南的远处判为陆地；这里沿 x=30 向南延伸，使南海与外海连成一片。地图范围内结果相同。）
  const SeaPoly = Coast.concat([{ x: 30, y: -400 }, { x: 500, y: -400 }, { x: 500, y: 500 }, { x: Coast[0].x, y: 500 }]);

  // ---------------------------------------------------------- 高度场 --
  function segDist(px, py, ax, ay, bx, by) {
    const abx = bx - ax, aby = by - ay;
    const t = M.clamp01(((px - ax) * abx + (py - ay) * aby) / Math.max(1e-4, abx * abx + aby * aby));
    const dx = px - (ax + abx * t), dy = py - (ay + aby * t);
    return Math.sqrt(dx * dx + dy * dy);
  }
  function inSea(px, py) {
    const poly = SeaPoly;
    let inside = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
      const pi = poly[i], pj = poly[j];
      if ((pi.y > py) !== (pj.y > py) && px < (pj.x - pi.x) * (py - pi.y) / (pj.y - pi.y) + pi.x) inside = !inside;
    }
    return inside;
  }
  function coastDist(px, py) {
    let d = 999;
    for (let i = 0; i < Coast.length - 1; i++) d = Math.min(d, segDist(px, py, Coast[i].x, Coast[i].y, Coast[i + 1].x, Coast[i + 1].y));
    return d;
  }
  function riverDist(px, py) {
    let d = 999;
    for (const r of Rivers) for (let i = 0; i < r.length - 1; i++) d = Math.min(d, segDist(px, py, r[i].x, r[i].y, r[i + 1].x, r[i + 1].y));
    for (const l of Lakes) d = Math.min(d, Math.max(0, Math.hypot(px - l.x, py - l.y) - l.r));
    return d;
  }
  function fbm(x, y, oct) {
    if (oct === undefined) oct = 4;
    let s = 0, a = 0.5, f = 1, n = 0;
    for (let i = 0; i < oct; i++) { s += a * M.perlinNoise(x * f + 31.7 * i, y * f + 17.3 * i); n += a; a *= 0.5; f *= 2.03; }
    return s / n;
  }

  // ---------------------------------------------------------- 地表颜色 --
  const LUSH = [0.36, 0.62, 0.3], DRY = [0.66, 0.64, 0.38], STEPPE = [0.62, 0.55, 0.36];
  const SEABED = [0.7, 0.66, 0.5], SAND = [0.86, 0.8, 0.6], FOREST = [0.22, 0.45, 0.24];
  // ROCK / SNOW 比 C#（0.58,0.56,0.53 / 0.95,0.96,0.98）略暗：网页端没有色调映射，
  // 半兰伯特太阳光 + 三色环境光的总辐照度可达 1.3，原值会让西部雪原整片截断成纯白、看不出低多边形面。
  const UPLAND = [0.56, 0.52, 0.4], ROCK = [0.55, 0.53, 0.5], SNOW = [0.88, 0.9, 0.94];
  function lerpInto(c, b, t) { t = M.clamp01(t); c[0] += (b[0] - c[0]) * t; c[1] += (b[1] - c[1]) * t; c[2] += (b[2] - c[2]) * t; }
  const _gc = [0, 0, 0], _gcOut = new THREE.Color();
  function groundColor(x, z, h, slope, jitter, noForest) {
    const north = M.inverseLerp(10, 95, z);
    const grass = [LUSH[0], LUSH[1], LUSH[2]];
    lerpInto(grass, DRY, north * 0.85);
    lerpInto(grass, STEPPE, M.inverseLerp(30, 5, x) * 0.6);
    const c = _gc;
    const src = h < -0.15 ? SEABED : h < 0.42 ? SAND : grass;
    c[0] = src[0]; c[1] = src[1]; c[2] = src[2];
    const forest = noForest ? 0 : fbm(x * 0.09 + 100, z * 0.09 + 100, 3);
    if (h >= 0.42 && h < 3.5 && forest > 0.56) lerpInto(c, FOREST, M.inverseLerp(0.56, 0.64, forest));
    if (h > 2.2) lerpInto(c, UPLAND, M.inverseLerp(2.2, 3.6, h));
    if (h > 3.6 || slope > 0.62) lerpInto(c, ROCK, Math.max(M.inverseLerp(3.6, 5, h), M.inverseLerp(0.62, 0.8, slope)));
    if (h > 6.2) lerpInto(c, SNOW, M.inverseLerp(6.2, 7.2, h));
    // Art.Shade(c, jitter)
    if (jitter >= 0) { _gcOut.setRGB(c[0] + (1 - c[0]) * jitter, c[1] + (1 - c[1]) * jitter, c[2] + (1 - c[2]) * jitter); }
    else { const k = 1 + jitter; _gcOut.setRGB(c[0] * k, c[1] * k, c[2] * k); }
    return _gcOut;
  }

  const V = (x, y, z) => new THREE.Vector3(x, y, z);
  const _m4 = new THREE.Matrix4(), _m4b = new THREE.Matrix4();
  const ZERO_MATRIX = new THREE.Matrix4().makeScale(0, 0, 0);
  const _sphere = new THREE.Sphere(), _hit = new THREE.Vector3(), _cloudPos = new THREE.Vector3();
  const _plane05 = new THREE.Plane(new THREE.Vector3(0, 1, 0), -0.5);

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

  // ---------------------------------------------------------- MapView --
  class MapView {
    constructor() {
      this.root = null;
      this.cities = new Map();
      this.time = 0;
      this._cityX = null; this._cityZ = null;
      this._grid = null;
      this._clouds = [];
      this._selected = -1;
      this._selectRing = null;
      this._flags = null;
      this._rings = null;
      this._marchers = new Set();
    }

    // ------------------------------------------------------ 高度 --
    // Height(x, z)：地图坐标（Unity x / z）
    height(x, z) {
      const coast = coastDist(x, z);
      const sea = inSea(x, z);
      let h;
      if (sea) h = -0.4 - Math.min(coast, 12) * 0.35;
      else h = 0.35 + Math.min(coast, 10) * 0.08;
      const n = fbm(x * 0.045, z * 0.045);
      if (!sea) h += (n - 0.4) * 2.4;
      // 西部高原
      h += M.smoothStep(0, 1, M.inverseLerp(22, 2, x)) * 4.5 * (0.7 + n * 0.6);
      h += M.smoothStep(0, 1, M.inverseLerp(30, 8, x)) * M.smoothStep(0, 1, M.inverseLerp(10, 40, z)) * 2.0;
      // 山脉
      if (!sea) {
        let rf = -1;
        for (const r of Ridges) {
          const d = segDist(x, z, r.ax, r.ay, r.bx, r.by);
          const k = Math.exp(-(d * d) / 6.5);
          if (k < 1e-7) continue;
          if (rf < 0) rf = fbm(x * 0.2, z * 0.2, 2);
          h += k * (2.6 + rf * 2.4);
        }
      }
      // 河流与湖泊
      const rd = riverDist(x, z);
      if (rd < 2.2 && !sea) {
        const target = rd < 0.75 ? -0.35 : M.lerp(-0.1, h, (rd - 0.75) / 1.45);
        h = Math.min(h, target);
      }
      // 城市周围平整
      const cx = this._cityX, cz = this._cityZ;
      if (cx)
        for (let i = 0; i < cx.length; i++) {
          const d = Math.sqrt((x - cx[i]) * (x - cx[i]) + (z - cz[i]) * (z - cz[i]));
          if (d < 2.6) { const k = M.smoothStep(1, 0, Math.max(0, d - 1.2) / 1.4); h = M.lerp(h, M.clamp(h, 0.5, 1.2), k); }
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
    build(state) {
      if (this.root) this._dispose();
      const Gfx = SG.Gfx;
      this.root = new THREE.Group();
      this.root.name = 'StrategyMap';
      Gfx.scene.add(this.root);
      this.cities = new Map();
      this._cityX = state.cities.map(c => SG.mapPos ? SG.mapPos(c).x : (c.lon - 100) * 5);
      this._cityZ = state.cities.map(c => SG.mapPos ? SG.mapPos(c).y : (c.lat - 23) * 5.6);
      this._buildTerrain();
      this._buildSkirt();
      this._buildWater();
      this._buildTrees(state);
      this._buildRoads(state);
      this._buildCities(state);
      this._buildClouds();
      this._buildSelectRing();
      this.refresh(state);
      return this.root;
    }

    _dispose() {
      for (const m of this._marchers) m.cancelled = true;
      SG.Gfx.disposeTree(this.root);
      this.root = null;
      // 云的材质每次构建新建（每朵云的主材质、阴影深度材质，以及九朵云共用的 CloudDepth 前置遍材质），随云一起释放
      for (const c of this._clouds) {
        c.material.dispose();
        if (c.customDepthMaterial) c.customDepthMaterial.dispose();
        for (const ch of c.children) if (ch.material) ch.material.dispose();
      }
      this._clouds = [];
      this._flags = null; this._rings = null; this._selectRing = null;
    }

    _buildTerrain() {
      const step = 1.25;
      const x0 = -26, z0 = -24, x1 = MapW + 26, z1 = MapH + 22;
      const nx = Math.ceil((x1 - x0) / step), nz = Math.ceil((z1 - z0) / step);
      const W = nz + 1;
      const hs = new Float32Array((nx + 1) * W);
      for (let i = 0; i <= nx; i++) for (let j = 0; j <= nz; j++) hs[i * W + j] = this.height(x0 + i * step, z0 + j * step);
      this._grid = { x0, z0, step, nx, nz, hs };
      const rnd = SG.SeededRandom(7);
      const mat = terrainMaterial();
      // 分块，避免单个网格过大，并让视锥剔除生效
      const chunk = 32;
      for (let ci = 0; ci < nx; ci += chunk)
        for (let cj = 0; cj < nz; cj += chunk) {
          const mb = new SG.MeshBuilder();
          for (let i = ci; i < Math.min(nx, ci + chunk); i++)
            for (let j = cj; j < Math.min(nz, cj + chunk); j++) {
              const a = V(x0 + i * step, hs[i * W + j], z0 + j * step);
              const b = V(x0 + i * step, hs[i * W + j + 1], z0 + (j + 1) * step);
              const c = V(x0 + (i + 1) * step, hs[(i + 1) * W + j + 1], z0 + (j + 1) * step);
              const d = V(x0 + (i + 1) * step, hs[(i + 1) * W + j], z0 + j * step);
              const flip = ((i + j) & 1) === 0;
              if (flip) { this._terrainTri(mb, a, b, c, rnd); this._terrainTri(mb, a, c, d, rnd); }
              else { this._terrainTri(mb, a, b, d, rnd); this._terrainTri(mb, b, c, d, rnd); }
            }
          const m = SG.Gfx.mesh(mb.toGeometry(), mat, { castShadow: true, receiveShadow: true });
          m.name = 'Terrain';
          this.root.add(m);
        }
    }
    _terrainTri(mb, a, b, c, rnd, noForest) {
      const cx = (a.x + b.x + c.x) / 3, cy = (a.y + b.y + c.y) / 3, cz = (a.z + b.z + c.z) / 3;
      const ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
      const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
      const slope = 1 - Math.abs(ny / (Math.sqrt(nx * nx + ny * ny + nz * nz) || 1));
      mb.tri(a, b, c, groundColor(cx, cz, cy, slope, (rnd.nextDouble() - 0.5) * 0.06, noForest));
    }

    // 远景地形：主地形网格之外的粗网格，使远处与旋转视角下不露出空洞（略低于主网格，重叠处被遮住）
    _buildSkirt() {
      const g = this._grid;
      const fx0 = g.x0, fz0 = g.z0, fx1 = g.x0 + g.nx * g.step, fz1 = g.z0 + g.nz * g.step;
      const step = 5, X0 = -210, Z0 = -210, X1 = MapW + 210, Z1 = MapH + 210;
      const nx = Math.ceil((X1 - X0) / step), nz = Math.ceil((Z1 - Z0) / step), W = nz + 1;
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
      const mb = new SG.MeshBuilder();
      for (let i = 0; i < nx; i++)
        for (let j = 0; j < nz; j++) {
          if (inner(i, j)) continue;
          const xa = X0 + i * step, za = Z0 + j * step;
          const a = V(xa, H(i, j), za), b = V(xa, H(i, j + 1), za + step), c = V(xa + step, H(i + 1, j + 1), za + step), d = V(xa + step, H(i + 1, j), za);
          if (((i + j) & 1) === 0) { this._terrainTri(mb, a, b, c, rnd, true); this._terrainTri(mb, a, c, d, rnd, true); }
          else { this._terrainTri(mb, a, b, d, rnd, true); this._terrainTri(mb, b, c, d, rnd, true); }
        }
      // 远海海床：远景地形之外的深水下方，免得透过水面看到天空
      const bed = new THREE.Color(SEABED[0], SEABED[1], SEABED[2]);
      const E = 1200, by = -5.2;
      mb.quad(V(-E, by, -E), V(-E, by, E), V(E, by, E), V(E, by, -E), bed);
      const m = SG.Gfx.mesh(mb.toGeometry(), terrainMaterial(), { castShadow: false, receiveShadow: true });
      m.name = 'TerrainFar';
      this.root.add(m);
    }

    _buildWater() {
      const pos = [], col = [];
      const shore = h => M.clamp01((h + 2.2) / 2.2);
      const step = 2.5;
      const x0 = -60, z0 = -60, x1 = MapW + 60, z1 = MapH + 60;
      const nx = Math.ceil((x1 - x0) / step), nz = Math.ceil((z1 - z0) / step), W = nz + 1;
      const ex1 = x0 + nx * step, ez1 = z0 + nz * step;
      // 顶点属性：r = 岸边系数（BuildWater 的顶点色）；g = 1 表示不做波浪起伏（内圈网格之外的远海，
      // 这样远海大格子与细网格拼接处不会因起伏不同而露缝）
      const onInner = (x, z) => x >= x0 - 1e-4 && x <= ex1 + 1e-4 && z >= z0 - 1e-4 && z <= ez1 + 1e-4;
      const hCache = new Map();
      const hAt = (x, z) => {
        const k = x.toFixed(3) + ',' + z.toFixed(3);
        let h = hCache.get(k);
        if (h === undefined) { h = this.height(x, z); hCache.set(k, h); }
        return h;
      };
      const attr = (x, z, r) => {
        if (r !== undefined) return [r, 0];
        return onInner(x, z) ? [shore(hAt(x, z)), 0] : [0, 1];
      };
      // 一个格子：Unity 顶点 a(xa,za) b(xa,za+sz) c(xa+sx,za+sz) d(xa+sx,za)，三角形 (a,b,c)(a,c,d)，镜像后反向输出
      const quad = (xa, za, sx, sz, ra, rb, rc, rd) => {
        const A = [xa, 0, -za], B = [xa, 0, -(za + sz)], Cc = [xa + sx, 0, -(za + sz)], D = [xa + sx, 0, -za];
        const ca = attr(xa, za, ra), cb = attr(xa, za + sz, rb), cc = attr(xa + sx, za + sz, rc), cd = attr(xa + sx, za, rd);
        pos.push(...A, ...Cc, ...B, ...A, ...D, ...Cc);
        col.push(ca[0], ca[1], 0, cc[0], cc[1], 0, cb[0], cb[1], 0, ca[0], ca[1], 0, cd[0], cd[1], 0, cc[0], cc[1], 0);
      };
      const hg = new Float32Array((nx + 1) * W);
      for (let i = 0; i <= nx; i++) for (let j = 0; j <= nz; j++) {
        const h = this.height(x0 + i * step, z0 + j * step);
        hg[i * W + j] = h;
        hCache.set((x0 + i * step).toFixed(3) + ',' + (z0 + j * step).toFixed(3), h);
      }
      for (let i = 0; i < nx; i++)
        for (let j = 0; j < nz; j++) {
          const h00 = hg[i * W + j], h01 = hg[i * W + j + 1], h11 = hg[(i + 1) * W + j + 1], h10 = hg[(i + 1) * W + j];
          // 四角都远高于水面的格子略过以节省顶点
          if (h00 > 1.2 && h10 > 1.2 && h01 > 1.2 && h11 > 1.2) continue;
          quad(x0 + i * step, z0 + j * step, step, step, shore(h00), shore(h01), shore(h11), shore(h10));
        }
      // 外海第一圈：宽 20，沿内圈边界按 2.5 细分（与内圈边界顶点完全重合）
      const band = 20, far = 300, nb = Math.round(band / step);
      const land = (xa, za, xb, zb) => onInner(xa, za) && onInner(xb, zb) && hAt(xa, za) > 1.2 && hAt(xb, zb) > 1.2;
      for (let i = 0; i < nx; i++) {
        const xa = x0 + i * step;
        if (!land(xa, z0, xa + step, z0)) quad(xa, z0 - band, step, band);
        if (!land(xa, ez1, xa + step, ez1)) quad(xa, ez1, step, band);
      }
      for (let j = -nb; j < nz + nb; j++) {
        const za = z0 + j * step;
        if (!land(x0, za, x0, za + step)) quad(x0 - band, za, band, step);
        if (!land(ex1, za, ex1, za + step)) quad(ex1, za, band, step);
      }
      // 最外圈：四个互不重叠的矩形，约 20 的格子
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
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
      g.computeBoundingSphere();
      const m = new THREE.Mesh(g, SG.Gfx.water());
      m.name = 'Water';
      m.renderOrder = -10; // Queue Transparent-10
      m.castShadow = false; m.receiveShadow = false;
      this.root.add(m);
    }

    _buildTrees(state) {
      const rnd = SG.SeededRandom(11);
      let mb = new SG.MeshBuilder();
      let count = 0;
      const flush = () => {
        const m = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly());
        m.name = 'Trees';
        this.root.add(m);
        mb = new SG.MeshBuilder();
      };
      const cx = this._cityX, cz = this._cityZ;
      for (let x = -10; x < MapW + 10; x += 1.15)
        for (let z = -6; z < MapH + 6; z += 1.15) {
          const px = x + (rnd.nextDouble() - 0.5) * 0.9, pz = z + (rnd.nextDouble() - 0.5) * 0.9;
          const h = this.height(px, pz);
          const forest = this.isForest(px, pz, h);
          const sparse = h > 0.5 && h < 2.5 && rnd.nextDouble() < 0.025;
          if (!forest && !sparse) continue;
          let nearCity = false;
          for (let i = 0; i < cx.length; i++) if (Math.hypot(cx[i] - px, cz[i] - pz) < 3.4) { nearCity = true; break; }
          if (nearCity) continue;
          if (riverDist(px, pz) < 1.4) continue;
          const north = M.inverseLerp(10, 95, pz);
          const pine = rnd.nextDouble() < 0.25 + north * 0.6 || h > 2;
          const leaf = pine ? new THREE.Color(0.2, 0.42, 0.28) : SG.Gfx.lerpColor(new THREE.Color(0.3, 0.58, 0.26), new THREE.Color(0.5, 0.6, 0.28), north);
          const s = 0.9 + rnd.nextDouble() * 0.6;
          const lc = SG.Gfx.shade(leaf, (rnd.nextDouble() - 0.5) * 0.12);
          SG.Models.tree(mb, V(px, this.surfaceY(px, pz) - 0.05, pz), s, lc, pine, count);
          if (++count % 900 === 0) flush();
        }
      if (mb.count > 0) flush();
      this.treeCount = count;
    }

    _buildRoads(state) {
      const mb = new SG.MeshBuilder();
      const col = new THREE.Color(0.78, 0.68, 0.5);
      const done = new Set();
      const cities = state.cities;
      for (const c of cities)
        for (const li of c.links) {
          const o = cities[li];
          if (!o) continue;
          const key = Math.min(c.id, o.id) + '-' + Math.max(c.id, o.id);
          if (done.has(key)) continue;
          done.add(key);
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
      const mat = SG.Gfx.newLowPoly();
      mat.polygonOffset = true; mat.polygonOffsetFactor = -1; mat.polygonOffsetUnits = -3;
      const m = SG.Gfx.mesh(mb.toGeometry(), mat, { castShadow: false, receiveShadow: true });
      m.name = 'Roads';
      this.root.add(m);
    }

    _buildCities(state) {
      const Gfx = SG.Gfx;
      const bodies = new SG.MeshBuilder();
      const rot = new THREE.Matrix4();
      const n = state.cities.length;
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

      for (const c of state.cities) {
        const mx = this._cityX[c.id], mz = this._cityZ[c.id];
        const y = Math.max(0.3, this.height(mx, mz));
        const size = M.lerp(0.9, 1.5, M.inverseLerp(180, 650, c.town));
        const capital = c.key === 'luoyang' || c.key === 'changan';
        const yaw = (c.id * 37) % 20 - 10;
        // 城体：烘焙到合并网格中（Unity 坐标下的平移 × 绕 y 旋转）
        bodies.M = new THREE.Matrix4().makeTranslation(mx, y, mz).multiply(rot.makeRotationY(yaw * M.deg2rad));
        SG.Models.cityInto(bodies, size, null, capital);
        bodies.M = null;

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
      }
      const body = Gfx.mesh(bodies.toGeometry(), Gfx.lowPoly());
      body.name = 'Cities';
      this.root.add(body);
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
      for (let i = 0; i < 9; i++) {
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
        const px = rnd.nextDouble() * MapW, py = 15 + rnd.nextDouble() * 5, pz = rnd.nextDouble() * MapH;
        m.position.copy(SG.U(px, py, pz));
        this.root.add(m);
        this._clouds.push(m);
      }
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
      for (const c of this._clouds) {
        c.position.x += 0.6 * dt;
        c.position.z -= 0.15 * dt;
        if (c.position.x > MapW + 30) c.position.x = -30;
        if (-c.position.z > MapH + 30) c.position.z = 30;
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
      this._updateFlags();
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

    // 行军动画：一支小部队沿道路移动。from / to 为城池对象或 id
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
      army.rotation.y = SG.yawToThree(Math.atan2(b.mapX - a.mapX, b.mapY - a.mapY) * M.rad2deg);
      const root = this.root;
      root.add(army);
      const token = { cancelled: false };
      this._marchers.add(token);
      const place = t => {
        const k = M.smoothStep(0, 1, t);
        const x = a.mapX + (b.mapX - a.mapX) * k, z = a.mapY + (b.mapY - a.mapY) * k;
        const y = Math.max(0.2, this.surfaceY(x, z)) + Math.abs(Math.sin(t * 30)) * 0.08;
        army.position.set(x, y, -z);
      };
      let t = 0, last = performance.now();
      place(0);
      while (t < 1 && !token.cancelled) {
        place(t);
        await SG.frame();
        const now = performance.now();
        t += Math.min(0.25, (now - last) / 1000) / seconds;
        last = now;
      }
      this._marchers.delete(token);
      root.remove(army);
      army.geometry.dispose();
    }
  }
  MapView.MapW = MapW;
  MapView.MapH = MapH;
  MapView._flagGeo = null;
  MapView.FlatDisc = MapView.flatDisc;

  SG.MapView = MapView;
})();
