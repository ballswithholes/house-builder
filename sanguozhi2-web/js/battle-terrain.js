'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 战场地形（DESIGN-V3 §2）
   战场地形取自城池在世界地图上的真实周边：以城池为中心，在地图坐标中取一个窗口，采样战略地图
   （js/map-view.js 的 MapView.terrainInfo：高度、坡度、群落、河流、湖海、海岸、道路、林地、农田），
   换算为战场地块。窗口按进攻方来向旋转：出发城 → 目标城的方向对准攻方的布阵边（棋盘 x = 0 的西边），
   攻方从那一边进入，本城在 (W − 3, ⌊H / 2⌋)（与 BattleModel 旧版布局相同：城墙 x = W−5 … W−1、
   y = cy−2 … cy+2，三座城门在西、南、北）。结果只取决于 (城池, 来向)，可复现。

   棋盘坐标 → 地图坐标：P = C + (x − cx)·s·d + (y − cy)·s·p
     C 城池地图坐标，d 进攻方向（出发城 → 目标城，单位向量），p = (−d.y, d.x)（d 逆时针 90°，
     棋盘不镜像：自西向东进攻时棋盘 x = 地图东、棋盘 y = 地图北），s = 每格 0.5 地图单位（约 10 公里）。

   地块（SG.Terrain 的原有值，规则不变）+ 新增 Water = 8（深水：湖、海；不可通行，规则上等同山岳，
   Defs.terrainCost 的 default 分支已返回 99；外观为水面）。
     湖海占格子一半以上 → 水域；河流中心线经过的格子 → 河川（宽度不超过 2 格，斜向相接处补成四连通）；
     道路过河处 → 桥（规则为平原，T.bridges）；坡度 + 相对城池的高差 → 山岳 / 山丘（山岳不超过陆地的 32%）；
     林地 → 森林；道路上的山岳降为山丘（山口）。
   可玩性保证：城外一圈无山无水、城门前通畅；攻方布阵边（x ≤ 1）山岳降为山丘，至少 6 个可布阵格（不足时把水域改为
   浅滩 = 河川，即「登陆」）；从布阵边到城门必有通路（按代价最小的路线把山岳改为山丘、水域改为浅滩）。
   若来向一侧几乎全是海（渡海进攻），窗口在 ±100° 内转向最近的可登陆海岸（T.turned 为转过的角度）。

   公开接口
     SG.BattleTerrain.Water                       = 8（新增的深水地块）
     SG.BattleTerrain.generate(opts) → T | null   opts：{ city（城池对象或 {x, y} 地图坐标）, fromCity | fromDir, W, H, seed, view }
         fromCity：出发城（城池对象或 {x, y}）；fromDir：攻方来自的方位（度数，地图坐标 0 = 东、90 = 北；或向量 {x, y}，
         由城池指向攻方）。都缺省时视为自西而来。view：MapView 实例（缺省 SG.Game.map）；没有已建成的战略地图时返回 null
         （调用方沿用旧的随机地形）。
     SG.BattleTerrain.applyTo(model, setup) → T | null
         用 setup.target / setup.src 生成并写入 model.map / height / burning / castle / terrain；返回 null 时什么也不改。
     SG.BattleTerrain.ascii(T, opts?) → string   调试用的字符图（上 = 棋盘 y 大的一侧）。
     SG.BattleTerrain.rawAscii(view, city, opts?) → string   世界地图窗口（北在上、不旋转）的同一套分类，对照用。
     SG.BattleTerrain.isWater(t) / name(t)       水域判断 / 地块名（含「水域」）
   T = { version, W, H, s, map[x][y], height[x][y]（旧版 model.height 的语义，供旧画面）, castle, gates, atkEdge: 'west',
         atkTiles, defTiles, bridges: [{x, y}], road[x][y]（布尔）, farm[x][y]（0..1）, dir {x, y}, angle（进攻方向，度）, turned,
         city { id, name, x, y }, from { id, name, x, y } | null, h0（城池地面高度，地图单位）, biome（主要群落）, culture,
         grid：画面用的取样栅格 { nu, nv, u0, v0, step（格）, h（相对 h0 的高度，地图单位）, slope, water（0 无 1 河 2 湖 3 海）,
                 forest, farm, sand, snow, biome（BIOMES 下标）, road（到道路的距离）, river（到河流的距离，按河宽归一）},
         notes: ['ford' | 'pass' | 'landing' | 'turned:…'], ms, seed }
     SG.BattleTerrain.BIOMES                      群落名表（grid.biome 的下标）
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});

  const WATER = 8;
  const BIOMES = ['temperate', 'steppe', 'desert', 'tropical', 'savanna', 'med', 'boreal', 'alpine'];
  const BIOME_IDX = {};
  BIOMES.forEach((b, i) => { BIOME_IDX[b] = i; });
  const S_TILE = 0.5;          // 每格的地图单位
  const MX = 10, MY = 9;       // 棋盘外的取样边距（格）：战场画面的远景
  const STEP = 0.5;            // 取样间距（格）
  const RIV_T = 0.4;           // 河流中心线距离（按河宽归一）小于此值 → 河川
  const D2R = Math.PI / 180;

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
  function coastDist(view, C, ang, maxD) {
    const d = { x: Math.cos(ang * D2R), y: Math.sin(ang * D2R) };
    const wet = t => { const ti = view.terrainInfo(C.x - d.x * t, C.y - d.y * t); return !!ti && (ti.water === 'sea' || ti.water === 'lake'); };
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
      else if (s.forest >= 0.48) t = Tn.Forest;
      map[x][y] = t;
    }
    // 山岳不超过陆地格的 32%：只保留最陡的，其余降为山丘
    const land = [], mts = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (map[x][y] !== WATER) land.push(1);
      if (map[x][y] === Tn.Mountain) mts.push({ x, y, r: rug[x][y] });
    }
    const cap = Math.floor(land.length * 0.32);
    if (mts.length > cap) {
      mts.sort((a, b) => b.r - a.r);
      for (let i = cap; i < mts.length; i++) map[mts[i].x][mts[i].y] = Tn.Hill;
    }
    // 河宽不超过 2 格（垂直于河道方向的连续河格只留离中心线最近的两格）
    const isR = (x, y) => x >= 0 && y >= 0 && x < W && y < H && map[x][y] === Tn.River;
    const drop = [];
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (!isR(x, y)) continue;
      let x0 = x, x1 = x, y0 = y, y1 = y;
      while (isR(x0 - 1, y)) x0--; while (isR(x1 + 1, y)) x1++;
      while (isR(x, y0 - 1)) y0--; while (isR(x, y1 + 1)) y1++;
      const runX = x1 - x0 + 1, runY = y1 - y0 + 1;
      const vert = runY <= runX;               // 河道大致沿 x 走 → 宽度在 y 方向
      const L = vert ? runY : runX;
      if (L <= 2) continue;
      const run = [];
      if (vert) for (let k = y0; k <= y1; k++) run.push({ x, y: k, d: st[x][k].rdC });
      else for (let k = x0; k <= x1; k++) run.push({ x: k, y, d: st[k][y].rdC });
      run.sort((a, b) => a.d - b.d);
      const me = run.findIndex(e => e.x === x && e.y === y);
      if (me >= 2) drop.push({ x, y });
    }
    for (const q of drop) map[q.x][q.y] = Tn.Plain;
    // 斜向相接的河格补成四连通（补离中心线较近的那一格）
    for (let x = 0; x < W - 1; x++) for (let y = 0; y < H - 1; y++) {
      const a = isR(x, y), b = isR(x + 1, y + 1), c = isR(x + 1, y), d = isR(x, y + 1);
      if (a && b && !c && !d) { const k = st[x + 1][y].rdC <= st[x][y + 1].rdC ? [x + 1, y] : [x, y + 1]; if (map[k[0]][k[1]] !== WATER) map[k[0]][k[1]] = Tn.River; }
      else if (c && d && !a && !b) { const k = st[x][y].rdC <= st[x + 1][y + 1].rdC ? [x, y] : [x + 1, y + 1]; if (map[k[0]][k[1]] !== WATER) map[k[0]][k[1]] = Tn.River; }
    }
    return { map, st, rug };
  }

  // 代价最小的通路：从布阵边（x = 0）到任一城门；返回路线（含两端）
  function cheapestPath(map, W, H, goals) {
    const Tn = TR();
    const cost = t => t === Tn.Plain || t === Tn.Gate ? 1 : t === Tn.Forest || t === Tn.Hill ? 2 : t === Tn.River ? 3 : t === Tn.Mountain ? 10 : t === WATER ? 14 : Infinity;
    const D = new Float64Array(W * H).fill(Infinity), prev = new Int32Array(W * H).fill(-1), done = new Uint8Array(W * H);
    for (let y = 0; y < H; y++) { const c = cost(map[0][y]); if (c < Infinity) D[y * W] = c; }
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
    let ang = approach(C, opts), kAtt = 1;
    const ang0 = ang, notes = [];
    // 渡海进攻：来向一侧大半是湖海时，攻方在城前的海岸登陆——攻方一侧沿进攻方向压缩，使布阵边落在岸边；
    // 来向上找不到海岸时，转向最近的可登陆海岸（±100° 内）
    try {
      const w0 = attackSideWater(view, C, ang, W, H, cx, cy);
      const dc = w0 > 0.5 ? coastDist(view, C, ang, (cx + 1) * S_TILE) : -1;
      if (dc > 0) {
        kAtt = clamp((dc + 0.3) / ((cx - 0.5) * S_TILE), 0.12, 1);
        notes.push('landing');
      } else if (w0 > 0.55) {
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

    // 城池（与 BattleModel 旧版相同的布局）
    const inC = (x, y) => x >= W - 5 && y >= cy - 2 && y <= cy + 2;
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
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (map[x][y] !== Tn.River || !road[x][y] || st[x][y].road > 0.14) continue;
      // 桥两端须是陆地（沿某一轴）
      const land = (a, b) => a >= 0 && b >= 0 && a < W && b < H && map[a][b] !== Tn.River && map[a][b] !== WATER && map[a][b] !== Tn.Mountain;
      const ax = land(x - 1, y) && land(x + 1, y), ay = land(x, y - 1) && land(x, y + 1);
      const bx = land(x - 1, y) && map[x + 1] && map[x + 1][y] === Tn.River && land(x + 2, y);
      const by = land(x, y - 1) && map[x][y + 1] === Tn.River && land(x, y + 2);
      if (ax || ay) { map[x][y] = Tn.Plain; bridges.push({ x, y, axis: ax ? 'x' : 'y' }); }
      else if (bx) { map[x][y] = Tn.Plain; map[x + 1][y] = Tn.Plain; bridges.push({ x, y, axis: 'x' }, { x: x + 1, y, axis: 'x' }); }
      else if (by) { map[x][y] = Tn.Plain; map[x][y + 1] = Tn.Plain; bridges.push({ x, y, axis: 'y' }, { x, y: y + 1, axis: 'y' }); }
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
    // 通路：代价最小的路线上，山岳 → 山丘、水域 → 浅滩（河川）
    const path = cheapestPath(map, W, H, gates);
    if (path) for (const q of path) {
      if (map[q.x][q.y] === Tn.Mountain) { map[q.x][q.y] = Tn.Hill; if (notes.indexOf('pass') < 0) notes.push('pass'); }
      else if (map[q.x][q.y] === WATER) { map[q.x][q.y] = Tn.River; if (notes.indexOf('ford') < 0) notes.push('ford'); }
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
      dir: { x: Math.cos(ang * D2R), y: Math.sin(ang * D2R) }, angle: ang, kAtt, turned: ang !== ang0 ? (((ang - ang0 + 540) % 360) - 180) : 0,
      city: { id: city.id, name: city.name, x: C.x, y: C.y }, from: fp ? { id: from.id, name: from.name, x: fp.x, y: fp.y } : null,
      h0, biome: BIOMES[bi], culture, grid: G, notes, ms: now() - t0, seed,
    };
  }

  function applyTo(model, setup) {
    if (!model || !setup || !setup.target) return null;
    let T = null;
    try { T = generate({ city: setup.target, fromCity: setup.src || setup.from || null, W: model.W, H: model.H }); }
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
