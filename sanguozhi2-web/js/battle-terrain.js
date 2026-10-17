'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 战场地形（DESIGN-V3 §2）
   战场地形取自城池在世界地图上的真实周边：以城池为中心，在地图坐标中取一个窗口，采样战略地图
   （js/map-view.js 的 MapView.terrainInfo：高度、坡度、群落、河流、湖海、海岸、道路、林地、农田），
   换算为战场地块。窗口按进攻方来向旋转：出发城 → 目标城的方向对准攻方的布阵边（棋盘 x = 0 的西边），
   攻方从那一边进入，本城在 (W − 3, ⌊H / 2⌋)（与 BattleModel 旧版布局相同：城墙 x = W−5 … W−1、
   y = cy−2 … cy+2，三座城门在西、南、北）。
   可复现：结果只取决于 (剧本的世界地图, 城池, 来向)。取样时高度取程序化的 view.height（在主网格同样的格点上、同样的三角形插值），
     而不是渲染出的地表——后者在建图时按各城当时的规模压平城址、在镜头一侧限高，会随游戏状态与建图时机而变。

   棋盘坐标 → 地图坐标：P = C + (x − cx)·s·d + (y − cy)·s·p
     C 城池地图坐标，d 进攻方向（出发城 → 目标城，单位向量），p = (−d.y, d.x)（d 逆时针 90°，
     棋盘不镜像：自西向东进攻时棋盘 x = 地图东、棋盘 y = 地图北），s = 每格 0.5 地图单位（约 10 公里）。

   地块（SG.Terrain 的原有值，规则不变）+ 新增 Water = 8（深水：湖、海；不可通行，规则上等同山岳，
   Defs.terrainCost 的 default 分支已返回 99；外观为水面）。
     湖海占格子一半以上 → 水域；
     河川：取样栅格上河流中心线经过的格子连成四连通的河道（世界地图上宽于约 1.5 格的大河再加横向一格，河宽 ≤ 2）；
       流到城下的河沿城外一圈（护城河）绕城、从城池背后的东边出棋盘；河道在棋盘中间断头时 3 格内接上河、水或棋盘边；
     桥：道路过河处，按转换前的地图判定——桥面横跨河道（两端陆地、两侧是河或水），长 ≤ 2 格，相距 2 格以内不重复架桥；
       城门前的护城河上必有桥（规则为平原，T.bridges）；
     坡度 + 相对城池的高差 → 山岳 / 山丘（山岳不超过陆地的 28%、山丘不超过 40%）；林地 → 森林；道路上的山岳降为山丘（山口）。
   可玩性保证：城外一圈无山无水、城门前通畅；攻方布阵边（x ≤ 1）山岳降为山丘，至少 6 个可布阵格（不足时把水域改为
   浅滩 = 河川，即「登陆」）；从布阵边、以及前 8 个布阵格各自到城门必有通路（按代价最小的路线把山岳改为山丘、
   水域改为浅滩）；被山岭 / 水域整个围住的谷地（≥ 4 格）开出山口。
   渡海进攻（opts.sea = 海路相连，或来向一侧大半是湖海）：攻方在城前的海岸登陆——海路时来向取最近岸的方向
   （每 10° 试一次，近岸与转角加权，可转到背面：T.turned 最大 ±180°）；陆路而来向一侧大半是水时窗口在 ±100° 内转向。
   攻方一侧沿进攻方向压缩（T.kAtt = max(0.3, 所需)），使布阵边落在岸边；压缩到 0.3 仍不够时，海岸以西的海面改为浅滩（'shallows'）。

   公开接口
     SG.BattleTerrain.Water                       = 8（新增的深水地块）
     SG.BattleTerrain.generate(opts) → T | null   opts：{ city（城池对象或 {x, y} 地图坐标）, fromCity | fromDir, sea, W, H, view }
         fromCity：出发城（城池对象或 {x, y}）；fromDir：攻方来自的方位（度数，地图坐标 0 = 东、90 = 北；或向量 {x, y}，
         由城池指向攻方）。都缺省时视为自西而来。view：MapView 实例（缺省 SG.Game.map）；没有已建成的战略地图时返回 null
         （调用方沿用旧的随机地形）。
     SG.BattleTerrain.applyTo(model, setup) → T | null
         用 setup.target / setup.src（sea = SG.linkIsSea(src, target)）生成并写入 model.map / height / burning / castle / terrain；
         返回 null 时什么也不改。
     SG.BattleTerrain.ascii(T, opts?) → string   调试用的字符图（上 = 棋盘 y 大的一侧）。
     SG.BattleTerrain.rawAscii(view, city, opts?) → string   世界地图窗口（北在上、不旋转）的同一套分类，对照用。
     SG.BattleTerrain.isWater(t) / name(t)       水域判断 / 地块名（含「水域」）
   T = { version, W, H, s, map[x][y], height[x][y]（旧版 model.height 的语义，供旧画面）, castle, gates, atkEdge: 'west',
         atkTiles, defTiles, bridges: [{x, y}], road[x][y]（布尔）, farm[x][y]（0..1）, dir {x, y}, angle（进攻方向，度）, turned,
         city { id, name, x, y }, from { id, name, x, y } | null, h0（城池地面高度，地图单位）, biome（主要群落）, culture,
         grid：画面用的取样栅格（棋盘外各延伸 MX = 16 / MY = 14 格）{ nu, nv, u0, v0, step（格）, h（相对 h0 的高度，地图单位）, slope, water（0 无 1 河 2 湖 3 海）,
                 forest, farm, sand, snow, biome（BIOMES 下标）, road（到道路的距离）, river（到河流的距离，按河宽归一）},
         kAtt, kNeed（所需的压缩，< kAtt 时有浅滩）, notes: ['ford' | 'pass' | 'landing' | 'shallows' | 'moat' | 'joined' | 'turned:…'], ms, seed }
     SG.BattleTerrain.BIOMES                      群落名表（grid.biome 的下标）
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});

  const WATER = 8;
  const BIOMES = ['temperate', 'steppe', 'desert', 'tropical', 'savanna', 'med', 'boreal', 'alpine'];
  const BIOME_IDX = {};
  BIOMES.forEach((b, i) => { BIOME_IDX[b] = i; });
  const S_TILE = 0.5;          // 每格的地图单位
  const MX = 16, MY = 14;      // 棋盘外的取样边距（格）：战场画面的远景（js/battle-view.js 的 EXT_X / EXT_Y）
  const STEP = 0.5;            // 取样间距（格）
  const RIV_T = 0.4;           // 河流中心线距离（按河宽归一）小于此值 → 河川
  const D2R = Math.PI / 180;
  const K_MIN = 0.3;           // 渡海登陆时攻方一侧的最大压缩（1 / 0.3 ≈ 3.3 倍）

  function TR() { return SG.Terrain || { Plain: 0, Forest: 1, Hill: 2, Mountain: 3, River: 4, Wall: 5, Gate: 6, Castle: 7 }; }
  function now() { return typeof performance !== 'undefined' ? performance.now() : Date.now(); }
  function grid(w, h, v) { const a = new Array(w); for (let x = 0; x < w; x++) { a[x] = new Array(h); for (let y = 0; y < h; y++) a[x][y] = v; } return a; }
  function clamp(v, a, b) { return v < a ? a : v > b ? b : v; }
  function hash(a, b) {
    let h = (Math.imul(a | 0, 0x9e3779b1) ^ Math.imul((b | 0) + 0x7f4a7c15, 0x85ebca6b)) >>> 0;
    h ^= h >>> 15; h = Math.imul(h, 0x2c1b3c6d) >>> 0; h ^= h >>> 12;
    return h >>> 0;
  }

  function findView(opts) {
    const v = (opts && opts.view) || (SG.Game && SG.Game.map) || null;
    if (!v || typeof v.terrainInfo !== 'function') return null;
    return v;
  }
  function posOf(c) {
    if (!c) return null;
    if (typeof c.lon === 'number' && typeof c.lat === 'number') {
      if (SG.mapPos) return SG.mapPos(c);
      if (SG.project) return SG.project(c.lon, c.lat);
      return { x: (c.lon - 100) * 5, y: (c.lat - 23) * 5.6 };
    }
    if (typeof c.x === 'number' && typeof c.y === 'number') return { x: c.x, y: c.y };
    return null;
  }
  // 进攻方向（单位向量，出发 → 目标），取整到 1°
  function approach(C, opts) {
    let dx = 1, dy = 0;
    const f = posOf(opts.fromCity);
    if (f && (f.x !== C.x || f.y !== C.y)) { dx = C.x - f.x; dy = C.y - f.y; }
    else if (typeof opts.fromDir === 'number' && isFinite(opts.fromDir)) { dx = -Math.cos(opts.fromDir * D2R); dy = -Math.sin(opts.fromDir * D2R); }
    else if (opts.fromDir && typeof opts.fromDir.x === 'number' && (opts.fromDir.x || opts.fromDir.y)) { dx = -opts.fromDir.x; dy = -opts.fromDir.y; }
    const a = Math.round(Math.atan2(dy, dx) / D2R);
    return ((a % 360) + 360) % 360;
  }

  // 取样栅格：棋盘坐标 u（0 … W−1 为格子中心）∈ [−MX − 0.5, W − 0.5 + MX]，步长 STEP
  // k：攻方一侧（u < cx）沿进攻方向的压缩系数（渡海登陆时 < 1，见 coastDist）
  function sampleGrid(view, C, ang, W, H, cx, cy, k) {
    const d = { x: Math.cos(ang * D2R), y: Math.sin(ang * D2R) }, p = { x: -d.y, y: d.x };
    if (!(k > 0)) k = 1;
    const nu = Math.round((W + 2 * MX) / STEP) + 1, nv = Math.round((H + 2 * MY) / STEP) + 1;
    const u0 = -MX - 0.5, v0 = -MY - 0.5, N = nu * nv;
    const G = {
      nu, nv, u0, v0, step: STEP,
      h: new Float32Array(N), slope: new Float32Array(N), water: new Uint8Array(N), forest: new Float32Array(N),
      farm: new Float32Array(N), sand: new Float32Array(N), snow: new Float32Array(N), biome: new Uint8Array(N),
      road: new Float32Array(N), river: new Float32Array(N), coast: new Float32Array(N),
    };
    for (let j = 0; j < nv; j++) {
      const v = v0 + j * STEP;
      for (let i = 0; i < nu; i++) {
        const u = u0 + i * STEP, a = (u - cx) * S_TILE * (u < cx ? k : 1);
        const mx = C.x + a * d.x + (v - cy) * S_TILE * p.x;
        const my = C.y + a * d.y + (v - cy) * S_TILE * p.y;
        const ti = view.terrainInfo(mx, my);
        if (!ti) return null;
        const o = j * nu + i;
        G.h[o] = ti.h; G.slope[o] = ti.slope;
        G.water[o] = ti.water === 'sea' ? 3 : ti.water === 'lake' ? 2 : ti.water === 'river' ? 1 : 0;
        G.forest[o] = ti.forest || 0; G.farm[o] = ti.farm || 0; G.sand[o] = ti.sand || 0; G.snow[o] = ti.snow || 0;
        G.biome[o] = BIOME_IDX[ti.biome] !== undefined ? BIOME_IDX[ti.biome] : 0;
        G.road[o] = Math.min(ti.road === undefined ? 999 : ti.road, 999); G.river[o] = Math.min(ti.river === undefined ? 99 : ti.river, 99);
        G.coast[o] = ti.coast || 0;
      }
    }
    return G;
  }
  // 渡海进攻：从城池沿来向回溯，第一处连续 0.4 单位以上的湖海的距离（找不到 → −1）
  function coastDist(view, C, ang, maxD, seaOnly) {
    const d = { x: Math.cos(ang * D2R), y: Math.sin(ang * D2R) };
    const wet = t => { const ti = view.terrainInfo(C.x - d.x * t, C.y - d.y * t); return !!ti && (ti.water === 'sea' || (!seaOnly && ti.water === 'lake')); };
    for (let t = 0.4; t <= maxD; t += 0.2) if (wet(t) && wet(t + 0.2) && wet(t + 0.4)) return t;
    return -1;
  }
  // 只看来向一侧（x ≤ 6）的格子中心有多少是湖海：渡海进攻时用来转向
  function attackSideWater(view, C, ang, W, H, cx, cy) {
    const d = { x: Math.cos(ang * D2R), y: Math.sin(ang * D2R) }, p = { x: -d.y, y: d.x };
    let n = 0, w = 0;
    for (let x = 0; x <= 6; x++) for (let y = 0; y < H; y++) {
      const mx = C.x + (x - cx) * S_TILE * d.x + (y - cy) * S_TILE * p.x, my = C.y + (x - cx) * S_TILE * d.y + (y - cy) * S_TILE * p.y;
      const ti = view.terrainInfo(mx, my);
      if (!ti) return 0;
      n++;
      if (ti.water === 'sea' || ti.water === 'lake') w += x <= 1 ? 2 : 1;
      if (x <= 1) n++;
    }
    return n ? w / n : 0;
  }

  // 格子 (x, y) 的 3×3 取样（中心权重 4、边 2、角 1）
  function tileStats(G, x, y) {
    const ci = Math.round((x - G.u0) / G.step), cj = Math.round((y - G.v0) / G.step), nu = G.nu;
    let h = 0, sl = 0, fo = 0, fa = 0, sea = 0, riv = 0, rdMin = 99, road = 999;
    for (let b = -1; b <= 1; b++) for (let a = -1; a <= 1; a++) {
      const o = (cj + b) * nu + ci + a, w = (a ? 1 : 2) * (b ? 1 : 2) / 16;
      h += G.h[o] * w; sl += G.slope[o] * w; fo += G.forest[o] * w; fa += G.farm[o] * w;
      if (G.water[o] >= 2) sea += w; else if (G.water[o] === 1) riv += w;
      if (G.river[o] < rdMin) rdMin = G.river[o];
      if (!a || !b) road = Math.min(road, G.road[o]);
    }
    const oc = cj * nu + ci;
    return { h, slope: sl, forest: fo, farm: fa, sea, riv, rdC: G.river[oc], rdMin, road, biome: G.biome[oc] };
  }

  // 河流中心线经过的格子：取样点到河流的距离（按河宽归一）沿 u 或 v 呈 V 形的局部极小（二阶差 > 0.04，排除与该轴平行的河段），
  // 且在河道内（< 0.75）。axis[x][y]：1 = 横跨方向为 x（河大致沿 y 流）、2 = 横跨方向为 y；0 = 不在中心线上
  function riverCourse(G, W, H) {
    const nu = G.nu, nv = G.nv, R = G.river, Wt = G.water;
    const axis = grid(W, H, 0), best = grid(W, H, 0), on = grid(W, H, 0);
    for (let j = 1; j < nv - 1; j++) for (let i = 1; i < nu - 1; i++) {
      const o = j * nu + i, r = R[o];
      if (r >= 0.75 || Wt[o] >= 2) continue;
      const x = Math.round(G.u0 + i * G.step), y = Math.round(G.v0 + j * G.step);
      if (x < 0 || y < 0 || x >= W || y >= H) continue;
      const a = R[o - 1], b = R[o + 1], c = R[o - nu], d = R[o + nu];
      const cu = r <= a && r < b ? a + b - 2 * r : 0, cv = r <= c && r < d ? c + d - 2 * r : 0;
      const k = Math.max(cu, cv);
      if (k <= 0.04 || k <= best[x][y]) continue;
      best[x][y] = k;
      axis[x][y] = cu >= cv ? 1 : 2;
    }
    return { axis, on };
  }

  function classify(G, W, H, h0) {
    const Tn = TR();
    const map = grid(W, H, Tn.Plain), st = grid(W, H, null), rug = grid(W, H, 0);
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      const s = st[x][y] = tileStats(G, x, y);
      const rel = s.h - h0;
      const r = rug[x][y] = s.slope + 0.1 * Math.max(0, rel - 0.3);
      let t = Tn.Plain;
      if (s.sea >= 0.5) t = WATER;
      else if (s.rdC < RIV_T || s.riv >= 0.45) t = Tn.River;
      else if (r >= 0.36) t = Tn.Mountain;
      else if (r >= 0.2) t = (s.forest > 0.6 && r < 0.25) ? Tn.Forest : Tn.Hill;
      else if (s.forest >= 0.4) t = Tn.Forest;
      map[x][y] = t;
    }
    // 山岳不超过陆地格的 32%：只保留最陡的，其余降为山丘
    const land = [], mts = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (map[x][y] !== WATER) land.push(1);
      if (map[x][y] === Tn.Mountain) mts.push({ x, y, r: rug[x][y] });
    }
    const cap = Math.floor(land.length * 0.28);
    if (mts.length > cap) {
      mts.sort((a, b) => b.r - a.r);
      for (let i = cap; i < mts.length; i++) map[mts[i].x][mts[i].y] = Tn.Hill;
    }
    // 山丘不超过陆地格的 40%：最平缓的降为平原（林地仍为森林）
    const hills = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) if (map[x][y] === Tn.Hill) hills.push({ x, y, r: rug[x][y] });
    const capH = Math.floor(land.length * 0.4);
    if (hills.length > capH) {
      hills.sort((a, b) => b.r - a.r);
      for (let i = capH; i < hills.length; i++) { const q = hills[i]; map[q.x][q.y] = st[q.x][q.y].forest >= 0.4 ? Tn.Forest : Tn.Plain; }
    }
    // 河道：取样栅格上的河流中心线（到河流距离沿某一轴呈 V 形的局部极小）经过的格子连成一条四连通的河道；
    // 世界地图上宽于约 1.5 格的大河再加上横向离中心线较近的一格（河宽 ≤ 2）。中心线以外的河边格子按陆地分类
    // （不出现山岳 / 山丘：河岸）。这样河道不会因为按行 / 列截宽而断成一个个水塘。
    const course = riverCourse(G, W, H);
    const band = (x, y) => x >= 0 && y >= 0 && x < W && y < H && map[x][y] === Tn.River;
    const isR = (x, y) => x >= 0 && y >= 0 && x < W && y < H && course.on[x][y] === 1;
    const wide = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      const ax = course.axis[x][y];
      if (!ax || map[x][y] === WATER) { course.on[x][y] = 0; continue; }
      course.on[x][y] = 1;
      const dx = ax === 1 ? 1 : 0, dy = ax === 1 ? 0 : 1;
      let run = 1;
      for (let k = 1; band(x + dx * k, y + dy * k); k++) run++;
      for (let k = 1; band(x - dx * k, y - dy * k); k++) run++;
      if (run >= 3) {
        const a = band(x + dx, y + dy) ? st[x + dx][y + dy].rdC : 99, b = band(x - dx, y - dy) ? st[x - dx][y - dy].rdC : 99;
        if (Math.min(a, b) < 99) wide.push(a <= b ? [x + dx, y + dy] : [x - dx, y - dy]);
      }
    }
    for (const [x, y] of wide) course.on[x][y] = 1;
    // 斜向相接的河格补成四连通（补离中心线较近的那一格）
    for (let x = 0; x < W - 1; x++) for (let y = 0; y < H - 1; y++) {
      const a = isR(x, y), b = isR(x + 1, y + 1), c = isR(x + 1, y), d = isR(x, y + 1);
      let k = null;
      if (a && b && !c && !d) k = st[x + 1][y].rdC <= st[x][y + 1].rdC ? [x + 1, y] : [x, y + 1];
      else if (c && d && !a && !b) k = st[x][y].rdC <= st[x + 1][y + 1].rdC ? [x, y] : [x + 1, y + 1];
      if (k && map[k[0]][k[1]] !== WATER) course.on[k[0]][k[1]] = 1;
    }
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (course.on[x][y]) { map[x][y] = Tn.River; continue; }
      if (map[x][y] !== Tn.River) continue;
      // 河边（原河带内、不在河道上）：平原或森林
      map[x][y] = st[x][y].forest >= 0.4 ? Tn.Forest : Tn.Plain;
    }
    return { map, st, rug };
  }

  // 代价最小的通路：从布阵边（x = 0）到任一城门；返回路线（含两端）
  // starts：起点格（缺省为 x = 0 整列）
  function cheapestPath(map, W, H, goals, starts) {
    const Tn = TR();
    const cost = t => t === Tn.Plain || t === Tn.Gate ? 1 : t === Tn.Forest || t === Tn.Hill ? 2 : t === Tn.River ? 3 : t === Tn.Mountain ? 10 : t === WATER ? 14 : Infinity;
    const D = new Float64Array(W * H).fill(Infinity), prev = new Int32Array(W * H).fill(-1), done = new Uint8Array(W * H);
    if (starts) for (const q of starts) { const c = cost(map[q.x][q.y]); if (c < Infinity) D[q.y * W + q.x] = c; }
    else for (let y = 0; y < H; y++) { const c = cost(map[0][y]); if (c < Infinity) D[y * W] = c; }
    const goalSet = new Set(goals.map(g => g.y * W + g.x));
    for (;;) {
      let bi = -1, bd = Infinity;
      for (let i = 0; i < W * H; i++) if (!done[i] && D[i] < bd) { bd = D[i]; bi = i; }
      if (bi < 0) return null;
      done[bi] = 1;
      if (goalSet.has(bi)) {
        const path = [];
        for (let k = bi; k >= 0; k = prev[k]) path.push({ x: k % W, y: (k / W) | 0 });
        return path.reverse();
      }
      const x = bi % W, y = (bi / W) | 0;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
        const ni = ny * W + nx;
        if (done[ni]) continue;
        const c = cost(map[nx][ny]);
        if (c === Infinity) continue;
        if (bd + c < D[ni]) { D[ni] = bd + c; prev[ni] = bi; }
      }
    }
  }

  // 与游戏状态无关的地表：MapView 的主网格在建图时按各城当时的规模压平城址（_fitCities）、并在镜头一侧限高，
  // 若直接取渲染出的地表，同一 (城池, 来向) 的战场会随建图时的状态（城池规模、何时建图）而变。
  // 这里在主网格同样的格点上取程序化高度 view.height（只含与状态无关的城址平整），按同样的三角形插值；
  // 取样期间临时以它替换 view.surfaceY（terrainInfo 的高度、坡度、林地、水域都由它导出）。
  function rawSurface(view, C, R) {
    if (typeof view.height !== 'function') return null;
    const m = view._master, res = m && m.res > 0 ? m.res : 0.625, x0 = m ? m.x0 : 0, z0 = m ? m.z0 : 0;
    const i0 = Math.floor((C.x - R - x0) / res), j0 = Math.floor((C.y - R - z0) / res);
    const nx = Math.ceil((C.x + R - x0) / res) - i0 + 1, nz = Math.ceil((C.y + R - z0) / res) - j0 + 1;
    const h = new Float32Array(nx * nz);
    for (let j = 0; j < nz; j++) for (let i = 0; i < nx; i++) h[j * nx + i] = view.height(x0 + (i0 + i) * res, z0 + (j0 + j) * res);
    return function rawSurfaceY(x, z) {
      const fx0 = (x - x0) / res - i0, fz0 = (z - z0) / res - j0;
      const i = Math.floor(fx0), j = Math.floor(fz0);
      if (i < 0 || j < 0 || i >= nx - 1 || j >= nz - 1) return view.height(x, z);
      const fx = fx0 - i, fz = fz0 - j, o = j * nx + i;
      const ha = h[o], hb = h[o + nx], hc = h[o + nx + 1], hd = h[o + 1];
      if (((i + i0 + j + j0) & 1) === 0) {
        if (fz >= fx) return ha + fz * (hb - ha) + fx * (hc - hb);
        return ha + fx * (hd - ha) + fz * (hc - hd);
      }
      if (fx + fz <= 1) return ha + fx * (hd - ha) + fz * (hb - ha);
      return hc + (1 - fx) * (hb - hc) + (1 - fz) * (hd - hc);
    };
  }
  function withSurface(view, surf, fn) {
    if (!surf) return fn();
    const own = Object.prototype.hasOwnProperty.call(view, 'surfaceY'), prev = view.surfaceY;
    view.surfaceY = surf;
    try { return fn(); }
    finally { if (own) view.surfaceY = prev; else delete view.surfaceY; }
  }

  function generate(opts) {
    opts = opts || {};
    const t0 = now();
    const Tn = TR();
    const B = SG.Balance || {};
    const W = opts.W || B.BattleW || 18, H = opts.H || B.BattleH || 13;
    const view = findView(opts);
    const C = posOf(opts.city);
    if (!view || !C || W < 10 || H < 7) return null;
    const cx = W - 3, cy = H >> 1;
    const R = Math.hypot((cx + MX + 2) * S_TILE, (Math.max(cy, H - cy) + MY + 2) * S_TILE) + 1;
    const surf = rawSurface(view, C, R);
    return withSurface(view, surf, () => generateIn(opts, t0, Tn, W, H, view, C, cx, cy));
  }
  function generateIn(opts, t0, Tn, W, H, view, C, cx, cy) {
    let ang = approach(C, opts), kAtt = 1, kNeed = 1, xCoast = -1;
    const ang0 = ang, notes = [];
    // 渡海进攻：来向一侧大半是湖海时，攻方在城前的海岸登陆——攻方一侧沿进攻方向压缩，使布阵边落在岸边；
    // 来向上找不到海岸时，转向最近的可登陆海岸（±100° 内）
    try {
      // 海路（opts.sea）：攻方从海上来，取与名义来向最接近的近岸方向（每 10° 试一次，近岸与转角加权）
      if (opts.sea) {
        let best = null;
        for (let k = 0; k < 36; k++) {
          const a = (ang0 + k * 10) % 360, da = Math.min(k * 10, 360 - k * 10);
          const dc = coastDist(view, C, a, (cx + 1) * S_TILE, true);
          if (dc <= 0) continue;
          const score = dc + da * 0.025;
          if (!best || score < best.score) best = { a, score };
        }
        if (best && best.a !== ang) { ang = best.a; notes.push('turned:' + (((ang - ang0 + 540) % 360) - 180)); }
      }
      const w0 = attackSideWater(view, C, ang, W, H, cx, cy);
      const dc = w0 > 0.5 || opts.sea ? coastDist(view, C, ang, (cx + 1) * S_TILE, !!opts.sea) : -1;
      if (dc > 0) {
        // 压缩不超过 1 / K_MIN 倍（地形不至于被拉得面目全非）；海岸仍在布阵边以内时，岸前的海面改为浅滩（见下）
        kNeed = dc / ((cx - 1) * S_TILE);
        kAtt = clamp(kNeed, K_MIN, 1);
        xCoast = cx - dc / (S_TILE * kAtt);
        notes.push('landing');
      } else if (w0 > 0.55 && !opts.sea) {
        let best = { a: ang, w: w0 };
        for (const k of [20, 40, 60, 80, 100]) {
          for (const sgn of [1, -1]) {
            const a = (ang0 + sgn * k + 360) % 360, w = attackSideWater(view, C, a, W, H, cx, cy);
            if (w < best.w - 0.05) best = { a, w };
          }
          if (best.w < 0.4) break;
        }
        if (best.a !== ang) { ang = best.a; notes.push('turned:' + (((ang - ang0 + 540) % 360) - 180)); }
      }
    } catch (e) { /* 取样失败时按原方向 */ }
    const G = sampleGrid(view, C, ang, W, H, cx, cy, kAtt);
    if (!G) return null;
    const oc = Math.round((cy - G.v0) / STEP) * G.nu + Math.round((cx - G.u0) / STEP);
    const h0 = G.h[oc];
    const { map, st } = classify(G, W, H, h0);

    // 登陆：压缩到 K_MIN 仍不够时，海岸（xCoast）以西的海面为浅滩（河川：可涉水通行）
    if (xCoast > 0.5) {
      let n = 0;
      for (let x = 0; x < W && x < xCoast; x++) for (let y = 0; y < H; y++) if (map[x][y] === WATER) { map[x][y] = Tn.River; n++; }
      if (n) notes.push('shallows');
    }
    // 城池（与 BattleModel 旧版相同的布局）
    const inC = (x, y) => x >= W - 5 && y >= cy - 2 && y <= cy + 2;
    let cutRiver = false, cutEast = false;
    for (let x = W - 5; x < W; x++) for (let y = cy - 2; y <= cy + 2; y++) if (map[x][y] === Tn.River) { cutRiver = true; if (x === W - 1) cutEast = true; }
    for (let x = W - 5; x < W; x++)
      for (let y = cy - 2; y <= cy + 2; y++) {
        const edge = x === W - 5 || y === cy - 2 || y === cy + 2 || x === W - 1;
        map[x][y] = edge ? Tn.Wall : Tn.Plain;
      }
    const gates = [{ x: W - 5, y: cy }, { x: W - 3, y: cy - 2 }, { x: W - 3, y: cy + 2 }];
    for (const g of gates) map[g.x][g.y] = Tn.Gate;
    map[cx][cy] = Tn.Castle;
    // 城外一圈：无山、无深水；西门外的通道
    for (let x = W - 6; x < W; x++)
      for (let y = cy - 3; y <= cy + 3; y++) {
        if (y < 0 || y >= H || inC(x, y)) continue;
        if (map[x][y] === Tn.Mountain) map[x][y] = Tn.Hill;
        else if (map[x][y] === WATER) map[x][y] = Tn.Plain;
      }
    for (let x = W - 8; x < W - 5; x++) if (map[x][cy] === Tn.Mountain) map[x][cy] = Tn.Hill;
    for (const [gx, gy] of [[W - 6, cy], [W - 3, cy - 3], [W - 3, cy + 3]]) if (gy >= 0 && gy < H && (map[gx][gy] === Tn.Mountain || map[gx][gy] === WATER)) map[gx][gy] = Tn.Plain;
    // 流到城下的河：沿城外一圈（护城河）绕城，从东边（城池背后的棋盘边）流出，不在城墙下断成水塘
    const ring = [];
    for (let x = W - 1; x >= W - 6; x--) ring.push({ x, y: cy - 3, along: 'x' });
    for (let y = cy - 2; y <= cy + 2; y++) ring.push({ x: W - 6, y, along: 'y' });
    for (let x = W - 6; x < W; x++) ring.push({ x, y: cy + 3, along: 'x' });
    const isRiv = (x, y) => x < 0 || y < 0 || x >= W || y >= H || map[x][y] === Tn.River || map[x][y] === WATER;
    const ringIdx = [];
    ring.forEach((q, k) => { if (map[q.x][q.y] === Tn.River) ringIdx.push(k); });
    // 环上的河格是河的尽头（城下断流）：城外的河邻 ≤ 1；或两格宽的河头（两格互为河邻、各自另有至多一个河邻）
    const rivN = (x, y, skip) => {
      const out = [];
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const a = x + dx, b = y + dy;
        if (skip && a === skip.x && b === skip.y) continue;
        if (!inC(a, b) && isRiv(a, b)) out.push({ x: a, y: b });
      }
      return out;
    };
    const deadEnd = k => {
      const q = ring[k], N = rivN(q.x, q.y);
      if (N.length <= 1) return true;
      if (N.length !== 2) return false;
      return N.some(r => ring.some(e => e.x === r.x && e.y === r.y) && rivN(r.x, r.y, q).length <= 1);
    };
    if (ringIdx.length && (cutRiver || ringIdx.some(deadEnd))) {
      let a = ringIdx[0], b = ringIdx[ringIdx.length - 1];
      // 只有一处入口（或河本来从城池背后流出）：引到较近的东端出棋盘
      if (a === b || cutEast || deadEnd(a) && deadEnd(b) && b - a < 3) { if (a < ring.length - 1 - b) a = 0; else b = ring.length - 1; }
      for (let k = a; k <= b; k++) { const q = ring[k]; if (map[q.x][q.y] !== WATER) map[q.x][q.y] = Tn.River; }
      notes.push('moat');
    }
    // 河道在棋盘中间断头（世界地图的河在取样时偶有缺口、或被压缩 / 截宽）：沿来向延伸，3 格内接上河、水或棋盘边就补上
    {
      const wetT = (x, y) => x < 0 || y < 0 || x >= W || y >= H || map[x][y] === Tn.River || map[x][y] === WATER;
      const solid = (x, y) => inC(x, y) || map[x][y] === Tn.Wall || map[x][y] === Tn.Gate || map[x][y] === Tn.Castle;
      const D4 = [[1, 0], [-1, 0], [0, 1], [0, -1]];
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        if (map[x][y] !== Tn.River || x === 0 || y === 0 || x === W - 1 || y === H - 1) continue;
        const nb = D4.filter(([dx, dy]) => wetT(x + dx, y + dy));
        if (nb.length !== 1 || map[x + nb[0][0]][y + nb[0][1]] === WATER) continue;
        const dx = -nb[0][0], dy = -nb[0][1];
        let best = null;
        // 直行，或先直行再向两侧偏一格（取到河流中心线较近的一路）
        for (const side of [0, 1, -1]) {
          const path = [];
          let px = x, py = y, ok = false;
          for (let k = 1; k <= 3; k++) {
            if (side && k === 2) { px += dy * side; py += dx * side; } else { px += dx; py += dy; }
            if (px < 0 || py < 0 || px >= W || py >= H) { ok = path.length > 0 || k === 1; break; }
            if (solid(px, py)) break;
            if (map[px][py] === Tn.River || map[px][py] === WATER) { ok = true; break; }
            path.push({ x: px, y: py });
            if (D4.some(([ex, ey]) => { const a = px + ex, b = py + ey; return !(a === x && b === y) && !path.some(q => q.x === a && q.y === b) && a >= 0 && b >= 0 && a < W && b < H && (map[a][b] === Tn.River || map[a][b] === WATER) && !(Math.abs(a - x) + Math.abs(b - y) <= 1); })) { ok = true; break; }
          }
          if (!ok || !path.length) continue;
          const d = path.reduce((a, q) => a + st[q.x][q.y].rdC, 0) / path.length;
          if (!best || path.length < best.path.length || (path.length === best.path.length && d < best.d)) best = { path, d };
        }
        if (best) { for (const q of best.path) map[q.x][q.y] = Tn.River; if (notes.indexOf('joined') < 0) notes.push('joined'); }
      }
    }

    // 道路：山岳降为山丘（山口），过河处架桥
    const road = grid(W, H, false), farm = grid(W, H, 0), bridges = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (inC(x, y)) continue;
      const s = st[x][y];
      farm[x][y] = s.farm;
      if (s.road < 0.2) {
        road[x][y] = true;
        if (map[x][y] === Tn.Mountain) { map[x][y] = Tn.Hill; if (notes.indexOf('pass') < 0) notes.push('pass'); }
      }
    }
    // 桥：按转换前的地图判定（新建的桥不当作陆地），桥面沿一轴横跨河道：两端是陆地、两侧（垂直于桥面）仍是河或水，
    // 桥长 ≤ 2 格（河宽）；离已有的桥 2 格以内不再架桥（每处过河只一座，不会沿河连成长堤或 L 形）。
    // 城门前的护城河上必有桥（西门、南门、北门）。
    const pre = map.map(col => col.slice());
    const wetP = (x, y) => x < 0 || y < 0 || x >= W || y >= H || pre[x][y] === Tn.River || pre[x][y] === WATER;
    const landP = (x, y) => x >= 0 && y >= 0 && x < W && y < H && pre[x][y] !== Tn.River && pre[x][y] !== WATER && pre[x][y] !== Tn.Mountain && pre[x][y] !== Tn.Wall;
    const near = (x, y) => bridges.some(b => Math.max(Math.abs(b.x - x), Math.abs(b.y - y)) <= 2);
    const span = (x, y, axis) => {
      const dx = axis === 'x' ? 1 : 0, dy = 1 - dx, flank = (a, b) => wetP(a - dy, b - dx) && wetP(a + dy, b + dx);
      if (pre[x][y] !== Tn.River || !flank(x, y)) return null;
      if (landP(x - dx, y - dy) && landP(x + dx, y + dy)) return [{ x, y }];
      if (landP(x - dx, y - dy) && x + dx < W && y + dy < H && pre[x + dx][y + dy] === Tn.River && flank(x + dx, y + dy) && landP(x + 2 * dx, y + 2 * dy)) return [{ x, y }, { x: x + dx, y: y + dy }];
      return null;
    };
    const build = (deck, axis) => { for (const q of deck) { map[q.x][q.y] = Tn.Plain; bridges.push({ x: q.x, y: q.y, axis }); } };
    for (const [gx, gy, axis] of [[W - 6, cy, 'x'], [W - 3, cy - 3, 'y'], [W - 3, cy + 3, 'y']]) {
      if (gy < 0 || gy >= H || pre[gx][gy] !== Tn.River) continue;
      build([{ x: gx, y: gy }], axis);
    }
    const cand = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) if (pre[x][y] === Tn.River && road[x][y] && st[x][y].road <= 0.14) cand.push({ x, y, d: st[x][y].road });
    cand.sort((a, b) => a.d - b.d);
    for (const q of cand) {
      if (near(q.x, q.y)) continue;
      const sx = span(q.x, q.y, 'x'), sy = span(q.x, q.y, 'y');
      const deck = sx && sy ? (sx.length <= sy.length ? sx : sy) : sx || sy;
      if (!deck || deck.some(e => near(e.x, e.y))) continue;
      build(deck, deck === sx ? 'x' : 'y');
    }

    // 攻方布阵边
    for (let y = 0; y < H; y++) for (let x = 0; x < 2; x++) if (map[x][y] === Tn.Mountain) map[x][y] = Tn.Hill;
    const spots0 = [];
    for (let i = 0; i < H; i++) {
      const y = (H >> 1) + ((i % 2 === 0) ? (i >> 1) : -((i + 1) >> 1));
      if (y >= 0 && y < H) { spots0.push({ x: 1, y }); spots0.push({ x: 0, y }); }
    }
    const pass = t => t === Tn.Plain || t === Tn.Forest || t === Tn.Hill || t === Tn.River || t === Tn.Gate || t === Tn.Castle;
    let okSpots = spots0.filter(q => pass(map[q.x][q.y])).length;
    for (const q of spots0) {
      if (okSpots >= 6) break;
      if (map[q.x][q.y] === WATER) { map[q.x][q.y] = Tn.River; okSpots++; if (notes.indexOf('landing') < 0) notes.push('landing'); }
    }
    // 通路：从整条布阵边、再从前 8 个布阵格各自出发，代价最小的路线上山岳 → 山丘、水域 → 浅滩（河川）
    const carve = path => {
      if (path) for (const q of path) {
        if (map[q.x][q.y] === Tn.Mountain) { map[q.x][q.y] = Tn.Hill; if (notes.indexOf('pass') < 0) notes.push('pass'); }
        else if (map[q.x][q.y] === WATER) { map[q.x][q.y] = Tn.River; if (notes.indexOf('ford') < 0) notes.push('ford'); }
      }
    };
    carve(cheapestPath(map, W, H, gates));
    for (const q of spots0.filter(q => pass(map[q.x][q.y])).slice(0, 8)) carve(cheapestPath(map, W, H, gates, [q]));
    // 被山岭 / 水域整个围住的谷地（≥ 4 格、到不了城门）：开一条最省的山口或浅滩连出来
    for (let iter = 0; iter < 4; iter++) {
      const reach = new Uint8Array(W * H), st2 = gates.map(g => g);
      for (const g of gates) reach[g.y * W + g.x] = 1;
      while (st2.length) {
        const a = st2.pop();
        for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
          const x = a.x + dx, y = a.y + dy;
          if (x < 0 || y < 0 || x >= W || y >= H || reach[y * W + x] || !pass(map[x][y])) continue;
          reach[y * W + x] = 1; st2.push({ x, y });
        }
      }
      let comp = null;
      const seen = new Uint8Array(W * H);
      for (let x = 0; x < W && !comp; x++) for (let y = 0; y < H && !comp; y++) {
        if (reach[y * W + x] || seen[y * W + x] || !pass(map[x][y]) || inC(x, y)) continue;
        const c = [{ x, y }], q = [{ x, y }];
        seen[y * W + x] = 1;
        while (q.length) {
          const a = q.pop();
          for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
            const xx = a.x + dx, yy = a.y + dy;
            if (xx < 0 || yy < 0 || xx >= W || yy >= H || seen[yy * W + xx] || !pass(map[xx][yy]) || inC(xx, yy)) continue;
            seen[yy * W + xx] = 1; c.push({ x: xx, y: yy }); q.push({ x: xx, y: yy });
          }
        }
        if (c.length >= 4) comp = c;
      }
      if (!comp) break;
      carve(cheapestPath(map, W, H, gates, comp));
    }

    // 旧版 model.height 的语义（平原的微起伏、丘陵 0.45、山岳 1.1、河 −0.15）
    const height = grid(W, H, 0);
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      const t = map[x][y];
      height[x][y] = t === Tn.Mountain ? 1.1 : t === Tn.Hill ? 0.45 : t === Tn.River || t === WATER ? -0.15 : inC(x, y) ? 0.05 : clamp((st[x][y].h - h0) * 0.12, -0.05, 0.12);
    }
    const atkTiles = spots0.filter(q => pass(map[q.x][q.y]));
    const defTiles = [{ x: cx, y: cy }, { x: W - 4, y: cy }, { x: W - 6, y: cy }, { x: W - 4, y: cy - 1 }, { x: W - 4, y: cy + 1 }, { x: W - 2, y: cy - 1 }, { x: W - 2, y: cy + 1 }]
      .filter(q => pass(map[q.x][q.y]));
    // 主要群落
    const bc = new Array(BIOMES.length).fill(0);
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) bc[st[x][y].biome]++;
    let bi = 0; for (let i = 1; i < bc.length; i++) if (bc[i] > bc[bi]) bi = i;
    const city = opts.city || {}, from = opts.fromCity || null, fp = posOf(from);
    let culture = 'han';
    try { if (SG.CultureArt && SG.CultureArt.cultureOfCity && city.name) culture = SG.CultureArt.cultureOfCity(city) || 'han'; } catch (e) { /* 无文化数据 */ }
    const seed = hash(typeof city.id === 'number' ? city.id : Math.round(C.x * 7 + C.y * 13), ang);
    return {
      version: 1, W, H, s: S_TILE, map, height, castle: { x: cx, y: cy }, gates, atkEdge: 'west', atkTiles, defTiles, bridges, road, farm,
      dir: { x: Math.cos(ang * D2R), y: Math.sin(ang * D2R) }, angle: ang, kAtt, kNeed, turned: ang !== ang0 ? (((ang - ang0 + 540) % 360) - 180) : 0,
      city: { id: city.id, name: city.name, x: C.x, y: C.y }, from: fp ? { id: from.id, name: from.name, x: fp.x, y: fp.y } : null,
      h0, biome: BIOMES[bi], culture, grid: G, notes, ms: now() - t0, seed,
    };
  }

  function applyTo(model, setup) {
    if (!model || !setup || !setup.target) return null;
    let T = null;
    const src = setup.src || setup.from || null;
    let sea = false;
    try { sea = !!(src && typeof SG.linkIsSea === 'function' && SG.linkIsSea(src.id, setup.target.id)); } catch (e) { sea = false; }
    try { T = generate({ city: setup.target, fromCity: src, sea, W: model.W, H: model.H }); }
    catch (e) { if (typeof console !== 'undefined') console.warn('BattleTerrain.generate', e); T = null; }
    if (!T) return null;
    model.map = T.map;
    model.height = T.height;
    model.burning = grid(T.W, T.H, 0);
    model.castle = { x: T.castle.x, y: T.castle.y };
    model.terrain = T;
    return T;
  }

  const CH = {};
  function ascii(T, opts) {
    const Tn = TR();
    if (!CH.ok) Object.assign(CH, { ok: 1, [Tn.Plain]: '.', [Tn.Forest]: 'f', [Tn.Hill]: 'h', [Tn.Mountain]: 'M', [Tn.River]: '=', [Tn.Wall]: '#', [Tn.Gate]: 'G', [Tn.Castle]: 'C', [WATER]: '~' });
    const rows = [];
    const br = new Set((T.bridges || []).map(b => b.x + ',' + b.y));
    for (let y = T.H - 1; y >= 0; y--) {
      let s = '';
      for (let x = 0; x < T.W; x++) {
        const t = T.map[x][y];
        let c = CH[t] || '?';
        if (br.has(x + ',' + y)) c = 'b';
        else if (t === Tn.Plain && T.road && T.road[x][y]) c = '+';
        s += c;
      }
      rows.push(s);
    }
    return rows.join('\n');
  }
  // 世界地图窗口（北在上，不旋转），同一套分类；dx0 / dx1：城池以西 / 以东的格数
  function rawAscii(view, city, o) {
    o = o || {};
    const C = posOf(city);
    if (!view || !C) return '';
    const W = o.W || 18, H = o.H || 13, cx = W - 3, cy = H >> 1;
    const G = sampleGrid(view, C, 0, W, H, cx, cy);
    const oc = Math.round((cy - G.v0) / STEP) * G.nu + Math.round((cx - G.u0) / STEP);
    const { map } = classify(G, W, H, G.h[oc]);
    return ascii({ W, H, map, road: null, bridges: [] });
  }

  SG.BattleTerrain = {
    Water: WATER, BIOMES, S_TILE, MX, MY,
    generate, applyTo, ascii, rawAscii,
    isWater(t) { return t === WATER; },
    name(t) { return t === WATER ? '水域' : (SG.Defs && SG.Defs.terrainName ? SG.Defs.terrainName(t) : ''); },
  };
})();
