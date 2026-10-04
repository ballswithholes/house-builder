'use strict';
/* ==========================================================================
   世界：大地图生成、地表绘制、精灵、城镇与营寨内部
   ========================================================================== */

const T = { DEEP: 0, SEA: 1, SAND: 2, GRASS: 3, FOREST: 4, HILL: 5, MOUNT: 6, ROAD: 7, BRIDGE: 8, RIVER: 9, GATE: 10 };
const WW = 112, WH = 84;
const GPX = 16; // 地表贴图每格像素

// ================================================================ 大地图生成 ==
function buildWorld() {
  const t = new Uint8Array(WW * WH).fill(T.GRASS);
  const range = new Uint8Array(WW * WH);
  const I = (x, y) => y * WW + x;
  const inb = (x, y) => x >= 0 && y >= 0 && x < WW && y < WH;
  const locAt = new Map();
  for (const l of LOCATIONS) locAt.set(I(l.x, l.y), l);
  const nearLoc = (x, y, r) => LOCATIONS.some(l => Math.abs(l.x - x) <= r && Math.abs(l.y - y) <= r);

  // 海陆
  for (let y = 0; y < WH; y++) for (let x = 0; x < WW; x++) {
    const edge = Math.min(x, WW - 1 - x, y, WH - 1 - y);
    const se = clamp((edge - 1) / 9, 0, 1);
    const v = 0.58 * se + 0.42 * fbm(x * 0.07, y * 0.07, 7);
    if (v < 0.5) t[I(x, y)] = T.SEA;
    else if (edge > 12 && fbm(x * 0.1, y * 0.1, 99, 3) > 0.73 && !nearLoc(x, y, 5)) t[I(x, y)] = T.SEA;
  }

  // 山脉
  const RANGES = [
    [[-3, 26], [20, 25], [40, 27], [60, 26], [80, 25], [100, 27], [115, 26]],
    [[38, 23], [37, 40], [39, 56], [38, 70], [37, 88]],
    [[38, 52], [56, 51], [70, 52], [90, 53], [115, 52]],
    [[-3, 56], [10, 57], [18, 56], [30, 55], [38, 56]],
    [[-3, 42], [10, 41], [18, 42], [30, 43], [38, 42]],
  ];
  for (const pts of RANGES) {
    for (let k = 0; k < pts.length - 1; k++) {
      const [x0, y0] = pts[k], [x1, y1] = pts[k + 1];
      const len = Math.hypot(x1 - x0, y1 - y0);
      for (let s = 0; s <= len; s += 0.25) {
        const px = x0 + (x1 - x0) * s / len, py = y0 + (y1 - y0) * s / len;
        const r = 1.5 + fbm(px * 0.2, py * 0.2, 5) * 1.5;
        for (let yy = Math.floor(py - r); yy <= Math.ceil(py + r); yy++)
          for (let xx = Math.floor(px - r); xx <= Math.ceil(px + r); xx++) {
            if (!inb(xx, yy) || Math.hypot(xx - px, yy - py) > r) continue;
            if (t[I(xx, yy)] === T.SEA && Math.min(xx, WW - 1 - xx, yy, WH - 1 - yy) < 3) continue;
            t[I(xx, yy)] = T.MOUNT; range[I(xx, yy)] = 1;
          }
      }
    }
  }
  // 关口：贯穿山脉的通道
  const GATE_DIR = { g_qingzhou: 'h', g_guangzong: 'h', g_suanzao: 'v', g_sishui: 'h', g_hulao: 'h' };
  for (const id in GATE_DIR) {
    const g = LOC[id], h = GATE_DIR[id] === 'h';
    for (let d = -6; d <= 6; d++) {
      const x = h ? g.x : g.x + d, y = h ? g.y + d : g.y;
      if (range[I(x, y)]) { range[I(x, y)] = 0; t[I(x, y)] = T.GRASS; }
      if (Math.abs(d) <= 1) for (const s of [-1, 1]) {
        const sx = h ? x + s : x, sy = h ? y : y + s;
        t[I(sx, sy)] = T.MOUNT; range[I(sx, sy)] = 1;
      }
    }
    t[I(g.x, g.y)] = T.GATE;
  }
  for (const l of LOCATIONS) if (l.kind === 'bossgate') t[I(l.x, l.y)] = T.GATE;

  // 河流（四连通）
  const RIVERS = [
    [[66, 29], [70, 35], [80, 40], [92, 38], [104, 40], [114, 37]],
    [[44, 56], [50, 62], [48, 70], [53, 78], [52, 88]],
    [[-2, 64], [12, 66], [22, 63], [35, 66]],
  ];
  for (const pts of RIVERS) {
    for (let k = 0; k < pts.length - 1; k++) {
      let [x, y] = pts[k]; const [x1, y1] = pts[k + 1];
      let guard = 0;
      while ((x !== x1 || y !== y1) && guard++ < 500) {
        if (inb(x, y) && !range[I(x, y)] && t[I(x, y)] !== T.SEA && t[I(x, y)] !== T.GATE && !locAt.has(I(x, y))) t[I(x, y)] = T.RIVER;
        const dx = x1 - x, dy = y1 - y;
        if (Math.abs(dx) * (0.7 + hash2(x, y, 41) * 0.6) > Math.abs(dy)) x += Math.sign(dx); else y += Math.sign(dy);
      }
    }
  }

  // 地形
  for (let y = 0; y < WH; y++) for (let x = 0; x < WW; x++) {
    const i = I(x, y);
    if (t[i] !== T.GRASS) continue;
    const arid = x < 38 && y < 56;
    const f = fbm(x * 0.12, y * 0.12, 3, 3), h = fbm(x * 0.09, y * 0.09, 5, 3), m = fbm(x * 0.15, y * 0.15, 11, 3);
    if (m > 0.7) t[i] = T.MOUNT;
    else if (h > (arid ? 0.56 : 0.63)) t[i] = T.HILL;
    else if (f > (arid ? 0.63 : 0.56)) t[i] = T.FOREST;
  }
  // 海岸沙滩
  for (let y = 1; y < WH - 1; y++) for (let x = 1; x < WW - 1; x++) {
    const i = I(x, y);
    if (t[i] !== T.GRASS && t[i] !== T.FOREST && t[i] !== T.HILL) continue;
    let sea = false;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) if (t[I(x + dx, y + dy)] === T.SEA) sea = true;
    if (sea && hash2(x, y, 13) > 0.3) t[i] = T.SAND;
  }
  // 地点周围
  for (const l of LOCATIONS) {
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const x = l.x + dx, y = l.y + dy, i = I(x, y);
      if (range[i] || t[i] === T.GATE) continue;
      if (t[i] !== T.SEA && t[i] !== T.RIVER) t[i] = T.GRASS;
    }
    if (l.kind !== 'gate' && l.kind !== 'bossgate') t[I(l.x, l.y)] = T.GRASS;
  }

  // 官道（A*）
  const cost = i => {
    if (range[i]) return Infinity;
    switch (t[i]) {
      case T.GRASS: return 1; case T.SAND: return 1.5; case T.FOREST: return 2.2; case T.HILL: return 2.8;
      case T.MOUNT: return 14; case T.ROAD: case T.BRIDGE: return 0.35; case T.RIVER: return 7; case T.SEA: return 9;
      case T.GATE: return 1; default: return 30;
    }
  };
  function astar(a, b) {
    const N = WW * WH, g = new Float32Array(N).fill(Infinity), from = new Int32Array(N).fill(-1), closed = new Uint8Array(N);
    const heap = [];
    const push = (i, f) => { heap.push([f, i]); let c = heap.length - 1; while (c > 0) { const p = (c - 1) >> 1; if (heap[p][0] <= heap[c][0]) break; [heap[p], heap[c]] = [heap[c], heap[p]]; c = p; } };
    const pop = () => { const top = heap[0], last = heap.pop(); if (heap.length) { heap[0] = last; let c = 0; for (; ;) { const l = 2 * c + 1, r = l + 1; let m = c; if (l < heap.length && heap[l][0] < heap[m][0]) m = l; if (r < heap.length && heap[r][0] < heap[m][0]) m = r; if (m === c) break;[heap[m], heap[c]] = [heap[c], heap[m]]; c = m; } } return top; };
    const bx = b % WW, by = (b / WW) | 0;
    g[a] = 0; push(a, 0);
    while (heap.length) {
      const [, i] = pop();
      if (closed[i]) continue; closed[i] = 1;
      if (i === b) break;
      const x = i % WW, y = (i / WW) | 0;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = x + dx, ny = y + dy;
        if (!inb(nx, ny)) continue;
        const j = I(nx, ny);
        const c = j === b ? 1 : cost(j) + (hash2(nx, ny, 77) * 0.3);
        if (!isFinite(c)) continue;
        const ng = g[i] + c;
        if (ng < g[j]) { g[j] = ng; from[j] = i; push(j, ng + (Math.abs(nx - bx) + Math.abs(ny - by)) * 0.35); }
      }
    }
    const path = []; let c = b;
    while (c !== -1 && c !== a) { path.push(c); c = from[c]; }
    return c === a ? path : [];
  }
  for (const [a, b] of ROADS) {
    const p = astar(I(LOC[a].x, LOC[a].y), I(LOC[b].x, LOC[b].y));
    for (const i of p) {
      if (locAt.has(i) || t[i] === T.GATE) continue;
      t[i] = (t[i] === T.RIVER || t[i] === T.SEA) ? T.BRIDGE : T.ROAD;
    }
  }

  // 水深
  const depth = new Float32Array(WW * WH);
  const q = [];
  for (let i = 0; i < WW * WH; i++) {
    const w = t[i] === T.SEA || t[i] === T.RIVER || t[i] === T.BRIDGE;
    depth[i] = w ? Infinity : 0;
    if (!w) q.push(i);
  }
  for (let h = 0; h < q.length; h++) {
    const i = q[h], x = i % WW, y = (i / WW) | 0;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const nx = x + dx, ny = y + dy; if (!inb(nx, ny)) continue;
      const j = I(nx, ny);
      if (depth[j] > depth[i] + 1) { depth[j] = depth[i] + 1; q.push(j); }
    }
  }
  for (let i = 0; i < WW * WH; i++) if (t[i] === T.SEA && depth[i] > 3) t[i] = T.DEEP;

  return { t, range, depth, locAt, w: WW, h: WH, I };
}

function zoneAt(x, y) {
  if (y < 26) return 'A';
  if (x > 38) return y < 52 ? 'B' : 'C';
  if (y > 56) return 'D1';
  if (y > 42) return 'D2';
  return 'D3';
}

// ================================================================ 地表贴图 ==
function paintGround(world) {
  const { t, depth } = world;
  const N = WW * WH;
  const fL = new Float32Array(N), fR = new Float32Array(N), fS = new Float32Array(N), fF = new Float32Array(N),
    fH = new Float32Array(N), fM = new Float32Array(N), fD = new Float32Array(N);
  for (let i = 0; i < N; i++) {
    const v = t[i];
    fL[i] = (v === T.SEA || v === T.DEEP || v === T.RIVER || v === T.BRIDGE) ? 0 : 1;
    fR[i] = (v === T.ROAD || v === T.GATE || world.locAt.has(i)) ? 1 : 0;
    fS[i] = v === T.SAND ? 1 : 0;
    fF[i] = v === T.FOREST ? 1 : 0;
    fH[i] = v === T.HILL ? 1 : 0;
    fM[i] = v === T.MOUNT ? 1 : 0;
    fD[i] = Math.min(depth[i], 8);
  }
  // 桥两端的路面要接上
  for (let i = 0; i < N; i++) if (t[i] === T.BRIDGE) fR[i] = 0.6;

  const IW = WW * GPX, IH = WH * GPX;
  const cv = document.createElement('canvas'); cv.width = IW; cv.height = IH;
  const ctx = cv.getContext('2d');
  const img = ctx.createImageData(IW, IH), d = img.data;

  const C = s => hex2rgb(s);
  const grassA = C('#6db347'), grassB = C('#93c957'), grassC = C('#4f9a3c'), forestFloor = C('#3b7434'),
    hillC = C('#a9b862'), mountC = C('#8a8b72'), sandC = C('#ead7a0'), wetSand = C('#c9b47e'),
    roadC = C('#dcc493'), roadEdge = C('#a88d5e'), shallow = C('#5ad2cf'), midW = C('#2c93c6'), deepW = C('#173f7f'),
    foam = C('#eafcff'), aridC = C('#c2b26b'), lushC = C('#3f9a4a'), coolC = C('#6aa86a');

  let o = 0;
  for (let py = 0; py < IH; py++) {
    const v = (py + 0.5) / GPX - 0.5;
    let ty = Math.floor(v); const fy = v - ty;
    const ty0 = clamp(ty, 0, WH - 1), ty1 = clamp(ty + 1, 0, WH - 1);
    for (let px = 0; px < IW; px++, o += 4) {
      const u = (px + 0.5) / GPX - 0.5;
      const tx = Math.floor(u), fx = u - tx;
      const tx0 = clamp(tx, 0, WW - 1), tx1 = clamp(tx + 1, 0, WW - 1);
      const i00 = ty0 * WW + tx0, i10 = ty0 * WW + tx1, i01 = ty1 * WW + tx0, i11 = ty1 * WW + tx1;
      const w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
      const S = f => f[i00] * w00 + f[i10] * w10 + f[i01] * w01 + f[i11] * w11;

      const n1 = vnoise(px * 0.055, py * 0.055, 1), n2 = vnoise(px * 0.014, py * 0.014, 2), gr = hash2(px, py, 3);
      const L = S(fL) + (n1 - 0.5) * 0.42 + (n2 - 0.5) * 0.2;
      let c, cw = null, cl = null;
      if (L < 0.53) {
        const dep = S(fD);
        const k = clamp(dep / 5, 0, 1);
        cw = k < 0.4 ? mixc(shallow, midW, k / 0.4) : mixc(midW, deepW, (k - 0.4) / 0.6);
        const rip = (vnoise(px * 0.08, py * 0.025, 4) - 0.5) * 0.12;
        cw = mixc(cw, rip > 0 ? [255, 255, 255] : [0, 20, 60], Math.abs(rip));
        if (L > 0.41) cw = mixc(cw, foam, clamp((L - 0.41) / 0.09, 0, 1) * 0.85);
      }
      if (L > 0.47) {
        cl = mixc(grassA, grassB, n2);
        cl = mixc(cl, grassC, smoothstep(0.55, 0.8, n1) * 0.5);
        // 区域色调
        const ar = smoothstep(40, 33, u) * smoothstep(58, 52, v);
        if (ar > 0) cl = mixc(cl, aridC, ar * 0.42);
        const lu = smoothstep(52, 60, v) * smoothstep(40, 46, u);
        if (lu > 0) cl = mixc(cl, lushC, lu * 0.25);
        const co = smoothstep(26, 12, v);
        if (co > 0) cl = mixc(cl, coolC, co * 0.25);
        const ff = S(fF); if (ff > 0) cl = mixc(cl, forestFloor, clamp(ff * 1.25, 0, 1));
        const hh = S(fH); if (hh > 0) cl = mixc(cl, hillC, hh * 0.7);
        const mm = S(fM); if (mm > 0) cl = mixc(cl, mountC, clamp(mm * 1.1, 0, 0.9));
        const ss = S(fS); if (ss > 0) cl = mixc(cl, sandC, clamp(ss * 1.3, 0, 1));
        if (L < 0.6) cl = mixc(cl, wetSand, clamp((0.6 - L) / 0.09, 0, 1) * 0.9);
        const R = S(fR) + (n1 - 0.5) * 0.22;
        if (R > 0.38) {
          const rc = mixc(roadC, roadEdge, clamp((0.56 - R) / 0.14, 0, 1));
          cl = mixc(cl, rc, smoothstep(0.38, 0.48, R));
        }
      }
      if (cw && cl) c = mixc(cw, cl, smoothstep(0.47, 0.53, L));
      else c = cw || cl;
      const g = 0.955 + gr * 0.09;
      d[o] = c[0] * g; d[o + 1] = c[1] * g; d[o + 2] = c[2] * g; d[o + 3] = 255;
    }
  }
  ctx.putImageData(img, 0, 0);
  return cv;
}

// ================================================================== 精灵 ==
const Spr = {
  ts: 0, cache: {},
  get(name, ts, w, h, ax, ay, draw) {
    if (this.ts !== ts && Object.keys(this.cache).length > 400) this.cache = {};
    this.ts = ts;
    name += '@' + ts;
    let s = this.cache[name];
    if (!s) {
      const dpr = 1;
      const cv = document.createElement('canvas');
      cv.width = Math.ceil(w * ts * dpr); cv.height = Math.ceil(h * ts * dpr);
      const c = cv.getContext('2d');
      c.scale(ts * dpr, ts * dpr);
      draw(c);
      s = this.cache[name] = { cv, ax: ax * ts, ay: ay * ts, w: w * ts, h: h * ts };
    }
    return s;
  },
};
function blit(ctx, s, x, y, k = 1) {
  ctx.drawImage(s.cv, x - s.ax * k, y - s.ay * k, s.w * k, s.h * k);
}
function ellipse(c, x, y, rx, ry, fill) { c.beginPath(); c.ellipse(x, y, rx, ry, 0, 0, Math.PI * 2); c.fillStyle = fill; c.fill(); }
function rrect(c, x, y, w, h, r) { c.beginPath(); c.roundRect ? c.roundRect(x, y, w, h, r) : c.rect(x, y, w, h); }

function sprTree(ts, v) {
  return Spr.get('tree' + v, ts, 1.2, 1.6, 0.6, 1.45, c => {
    ellipse(c, 0.6, 1.42, 0.36, 0.11, 'rgba(10,30,10,.32)');
    c.fillStyle = '#6b4a2b'; c.fillRect(0.555, 1.0, 0.09, 0.42);
    const hue = [['#9be07a', '#4c9b3e', '#2a6a2c'], ['#b6e27a', '#5fa53c', '#2f6e28'], ['#8fd59a', '#3f8f57', '#225a36']][v % 3];
    const blob = (x, y, r) => {
      const g = c.createRadialGradient(x - r * 0.4, y - r * 0.5, r * 0.1, x, y, r);
      g.addColorStop(0, hue[0]); g.addColorStop(0.55, hue[1]); g.addColorStop(1, hue[2]);
      c.beginPath(); c.arc(x, y, r, 0, Math.PI * 2); c.fillStyle = g; c.fill();
    };
    blob(0.4, 0.98, 0.26); blob(0.8, 0.98, 0.26); blob(0.6, 0.72, 0.36);
    c.globalAlpha = 0.35; ellipse(c, 0.5, 0.58, 0.1, 0.06, '#eaffd0'); c.globalAlpha = 1;
  });
}
function sprPine(ts) {
  return Spr.get('pine', ts, 1, 1.7, 0.5, 1.55, c => {
    ellipse(c, 0.5, 1.52, 0.3, 0.09, 'rgba(10,30,10,.32)');
    c.fillStyle = '#5b3d22'; c.fillRect(0.46, 1.2, 0.08, 0.32);
    for (let k = 0; k < 3; k++) {
      const y = 0.45 + k * 0.28, w = 0.24 + k * 0.1;
      const g = c.createLinearGradient(0.5 - w, 0, 0.5 + w, 0);
      g.addColorStop(0, '#5aa163'); g.addColorStop(0.5, '#2f7a45'); g.addColorStop(1, '#1d4f30');
      c.beginPath(); c.moveTo(0.5, y - 0.35); c.lineTo(0.5 + w, y + 0.32); c.lineTo(0.5 - w, y + 0.32); c.closePath(); c.fillStyle = g; c.fill();
    }
  });
}
function sprMount(ts, v) {
  return Spr.get('mt' + v, ts, 1.7, 1.8, 0.85, 1.6, c => {
    ellipse(c, 0.85, 1.58, 0.72, 0.16, 'rgba(20,20,10,.25)');
    const px = 0.85 + (v === 1 ? -0.12 : v === 2 ? 0.1 : 0), top = v === 2 ? 0.35 : 0.2;
    // 左侧受光面
    c.beginPath(); c.moveTo(0.08, 1.6); c.lineTo(px - 0.1, top + 0.18); c.lineTo(px, top); c.lineTo(px + 0.05, 1.6); c.closePath();
    let g = c.createLinearGradient(0, top, 0, 1.6); g.addColorStop(0, '#c9c3a6'); g.addColorStop(1, '#8f8b6c'); c.fillStyle = g; c.fill();
    // 右侧阴影面
    c.beginPath(); c.moveTo(px, top); c.lineTo(1.62, 1.6); c.lineTo(px + 0.05, 1.6); c.closePath();
    g = c.createLinearGradient(px, 0, 1.6, 0); g.addColorStop(0, '#76735c'); g.addColorStop(1, '#55533f'); c.fillStyle = g; c.fill();
    // 山脊
    c.strokeStyle = 'rgba(255,255,240,.35)'; c.lineWidth = 0.025;
    c.beginPath(); c.moveTo(px, top); c.lineTo(px - 0.18, 0.75); c.lineTo(px - 0.1, 1.1); c.stroke();
    // 积雪
    if (v !== 2) {
      c.beginPath(); c.moveTo(px, top); c.lineTo(px - 0.22, top + 0.38); c.lineTo(px - 0.08, top + 0.3); c.lineTo(px + 0.02, top + 0.42);
      c.lineTo(px + 0.14, top + 0.3); c.lineTo(px + 0.26, top + 0.38); c.closePath(); c.fillStyle = '#f4f7fb'; c.fill();
    }
    // 山脚草
    c.globalAlpha = 0.5;
    g = c.createLinearGradient(0, 1.2, 0, 1.6); g.addColorStop(0, 'rgba(90,140,60,0)'); g.addColorStop(1, 'rgba(90,140,60,.9)');
    c.fillStyle = g; c.fillRect(0.05, 1.2, 1.6, 0.4); c.globalAlpha = 1;
  });
}
function sprHill(ts) {
  return Spr.get('hill', ts, 1.3, 0.9, 0.65, 0.78, c => {
    ellipse(c, 0.68, 0.76, 0.55, 0.12, 'rgba(30,40,10,.22)');
    const g = c.createRadialGradient(0.5, 0.35, 0.05, 0.65, 0.6, 0.6);
    g.addColorStop(0, '#d9e08a'); g.addColorStop(0.6, '#a2b356'); g.addColorStop(1, '#6f8a3a');
    c.beginPath(); c.moveTo(0.1, 0.78); c.quadraticCurveTo(0.35, 0.12, 0.7, 0.22); c.quadraticCurveTo(1.05, 0.3, 1.2, 0.78); c.closePath();
    c.fillStyle = g; c.fill();
  });
}
function sprTuft(ts, v) {
  return Spr.get('tuft' + v, ts, 0.4, 0.3, 0.2, 0.28, c => {
    c.strokeStyle = v ? '#3f8a35' : '#5aa845'; c.lineWidth = 0.03; c.lineCap = 'round';
    for (const [x, h] of [[0.12, 0.18], [0.2, 0.26], [0.28, 0.16]]) { c.beginPath(); c.moveTo(0.2, 0.28); c.quadraticCurveTo(x, 0.2, x - 0.02 + (x - 0.2) * 0.5, 0.28 - h); c.stroke(); }
    if (v === 2) { ellipse(c, 0.1, 0.1, 0.04, 0.04, '#ffe27a'); ellipse(c, 0.3, 0.14, 0.035, 0.035, '#ff9fc0'); }
  });
}

// 中式屋顶
function roof(c, x, y, w, h, col, ridge) {
  const g = c.createLinearGradient(0, y, 0, y + h);
  g.addColorStop(0, shade(col, 0.15)); g.addColorStop(1, shade(col, -0.35));
  c.beginPath();
  c.moveTo(x - w * 0.08, y + h);
  c.quadraticCurveTo(x + w * 0.08, y + h * 0.9, x + w * 0.14, y + h * 0.2);
  c.lineTo(x + w * 0.86, y + h * 0.2);
  c.quadraticCurveTo(x + w * 0.92, y + h * 0.9, x + w * 1.08, y + h);
  c.closePath(); c.fillStyle = g; c.fill();
  c.strokeStyle = 'rgba(0,0,0,.18)'; c.lineWidth = 0.015;
  for (let k = 1; k < 8; k++) { const xx = x + w * (0.1 + k * 0.1); c.beginPath(); c.moveTo(xx, y + h * 0.25); c.lineTo(xx + (xx - (x + w / 2)) * 0.1, y + h * 0.95); c.stroke(); }
  c.fillStyle = ridge || shade(col, -0.5);
  rrect(c, x + w * 0.1, y + h * 0.1, w * 0.8, h * 0.16, 0.02); c.fill();
  // 翘角
  c.beginPath(); c.arc(x + w * 0.1, y + h * 0.16, h * 0.1, 0, Math.PI * 2); c.arc(x + w * 0.9, y + h * 0.16, h * 0.1, 0, Math.PI * 2); c.fill();
}
function flag(c, x, y, col, glyph, wave = 0) {
  c.strokeStyle = '#4a3420'; c.lineWidth = 0.03; c.beginPath(); c.moveTo(x, y); c.lineTo(x, y - 0.5); c.stroke();
  c.beginPath(); c.moveTo(x, y - 0.5); c.quadraticCurveTo(x + 0.15, y - 0.55 + wave, x + 0.3, y - 0.5);
  c.lineTo(x + 0.3, y - 0.3); c.quadraticCurveTo(x + 0.15, y - 0.35 + wave, x, y - 0.3); c.closePath();
  c.fillStyle = col; c.fill();
  if (glyph) { c.fillStyle = 'rgba(255,255,255,.9)'; c.font = `700 0.17px ${CN_SERIF}`; c.textAlign = 'center'; c.textBaseline = 'middle'; c.fillText(glyph, x + 0.15, y - 0.41); }
}
const CN_SERIF = '"Noto Serif SC","Source Han Serif SC","Songti SC","STSong","SimSun","WenQuanYi Zen Hei",serif';
const CN_KAI = '"STKaiti","KaiTi","Kaiti SC","楷体","BiauKai","Noto Serif SC","Songti SC","WenQuanYi Zen Hei",serif';

function sprTown(ts) {
  return Spr.get('town', ts, 2.6, 2.4, 1.3, 2.05, c => {
    ellipse(c, 1.3, 2.02, 1.2, 0.22, 'rgba(0,0,0,.28)');
    // 城内建筑
    c.fillStyle = '#e8dcc2'; c.fillRect(0.55, 0.9, 0.7, 0.5); c.fillRect(1.35, 0.8, 0.7, 0.6);
    roof(c, 0.45, 0.55, 0.9, 0.42, '#3f5a78'); roof(c, 1.25, 0.42, 0.9, 0.45, '#8a3a30');
    // 城墙
    const g = c.createLinearGradient(0, 1.3, 0, 2.0); g.addColorStop(0, '#b9b2a2'); g.addColorStop(1, '#7c7568');
    c.fillStyle = g; c.fillRect(0.2, 1.35, 2.2, 0.65);
    c.fillStyle = '#cfc8b6';
    for (let k = 0; k < 11; k++) c.fillRect(0.22 + k * 0.2, 1.25, 0.12, 0.12);
    c.strokeStyle = 'rgba(0,0,0,.12)'; c.lineWidth = 0.012;
    for (let r = 0; r < 3; r++) for (let k = 0; k < 11; k++) { c.strokeRect(0.2 + k * 0.2 + (r % 2) * 0.1, 1.4 + r * 0.2, 0.2, 0.2); }
    // 城门楼
    c.fillStyle = '#9e3b2f'; c.fillRect(1.0, 1.0, 0.6, 0.4);
    roof(c, 0.88, 0.72, 0.84, 0.36, '#2f3f56', '#d4a84a');
    c.fillStyle = '#2a1d16'; c.beginPath(); c.moveTo(1.12, 2.0); c.lineTo(1.12, 1.68); c.arc(1.3, 1.68, 0.18, Math.PI, 0); c.lineTo(1.48, 2.0); c.fill();
    flag(c, 0.3, 1.3, '#c8342c', '漢'); flag(c, 2.28, 1.3, '#c8342c', '漢');
  });
}
function sprVillage(ts) {
  return Spr.get('village', ts, 2.4, 2.0, 1.2, 1.75, c => {
    ellipse(c, 1.2, 1.72, 1.05, 0.2, 'rgba(0,0,0,.22)');
    const hut = (x, y, w) => {
      c.fillStyle = '#d9c39a'; c.fillRect(x, y, w, w * 0.6);
      c.fillStyle = '#5a3f28'; c.fillRect(x + w * 0.4, y + w * 0.25, w * 0.2, w * 0.35);
      const g = c.createLinearGradient(0, y - w * 0.45, 0, y + 0.05); g.addColorStop(0, '#e8c56a'); g.addColorStop(1, '#a27a35');
      c.beginPath(); c.moveTo(x - w * 0.12, y + 0.04); c.lineTo(x + w / 2, y - w * 0.45); c.lineTo(x + w * 1.12, y + 0.04); c.closePath(); c.fillStyle = g; c.fill();
      c.strokeStyle = 'rgba(90,60,20,.4)'; c.lineWidth = 0.012;
      for (let k = 1; k < 6; k++) { c.beginPath(); c.moveTo(x + w / 2, y - w * 0.45); c.lineTo(x - w * 0.12 + k * w * 0.21, y + 0.04); c.stroke(); }
    };
    hut(0.35, 0.95, 0.6); hut(1.3, 0.8, 0.7); hut(0.85, 1.25, 0.55);
    c.strokeStyle = '#7a5a36'; c.lineWidth = 0.035;
    for (let k = 0; k < 12; k++) { const x = 0.2 + k * 0.18; c.beginPath(); c.moveTo(x, 1.72); c.lineTo(x, 1.55); c.stroke(); }
    c.beginPath(); c.moveTo(0.2, 1.6); c.lineTo(2.2, 1.6); c.stroke();
    // 大桑树
    const g = c.createRadialGradient(1.95, 0.6, 0.05, 2.0, 0.75, 0.4); g.addColorStop(0, '#9fdc78'); g.addColorStop(1, '#3d7d36');
    c.fillStyle = '#6b4a2b'; c.fillRect(1.96, 0.8, 0.07, 0.5);
    c.beginPath(); c.arc(2.0, 0.72, 0.34, 0, Math.PI * 2); c.fillStyle = g; c.fill();
  });
}
function sprFort(ts, cleared) {
  return Spr.get('fort' + (cleared ? 'c' : ''), ts, 2.6, 2.4, 1.3, 2.05, c => {
    ellipse(c, 1.3, 2.02, 1.15, 0.22, 'rgba(0,0,0,.3)');
    // 营帐
    const tent = (x, y, w, col) => {
      const g = c.createLinearGradient(x, 0, x + w, 0); g.addColorStop(0, shade(col, 0.15)); g.addColorStop(1, shade(col, -0.3));
      c.beginPath(); c.moveTo(x, y + w * 0.6); c.lineTo(x + w / 2, y); c.lineTo(x + w, y + w * 0.6); c.closePath(); c.fillStyle = g; c.fill();
      c.fillStyle = 'rgba(0,0,0,.4)'; c.beginPath(); c.moveTo(x + w * 0.42, y + w * 0.6); c.lineTo(x + w / 2, y + w * 0.3); c.lineTo(x + w * 0.58, y + w * 0.6); c.fill();
    };
    tent(0.5, 0.75, 0.7, cleared ? '#c9c2b0' : '#d8b44a'); tent(1.35, 0.65, 0.8, cleared ? '#bdb6a4' : '#cfa43a');
    // 望楼
    c.fillStyle = '#6a4a2a'; c.fillRect(2.0, 0.55, 0.06, 1.0); c.fillRect(2.3, 0.55, 0.06, 1.0);
    c.fillStyle = '#8b6338'; c.fillRect(1.92, 0.45, 0.52, 0.18);
    roof(c, 1.88, 0.22, 0.6, 0.25, '#5b4128');
    // 木栅
    for (let k = 0; k < 13; k++) {
      const x = 0.18 + k * 0.175;
      const g = c.createLinearGradient(x, 0, x + 0.15, 0); g.addColorStop(0, '#a77b4a'); g.addColorStop(1, '#6b4a2a');
      c.fillStyle = g; c.beginPath(); c.moveTo(x, 2.0); c.lineTo(x, 1.42); c.lineTo(x + 0.075, 1.28); c.lineTo(x + 0.15, 1.42); c.lineTo(x + 0.15, 2.0); c.fill();
    }
    c.fillStyle = '#4a3220'; c.fillRect(0.15, 1.6, 2.3, 0.05);
    c.fillStyle = '#20150d'; c.fillRect(1.12, 1.6, 0.36, 0.4);
    if (cleared) { flag(c, 0.3, 1.3, '#2f8f4e', '劉'); flag(c, 2.2, 1.3, '#2f8f4e', '劉'); }
    else { flag(c, 0.3, 1.3, '#f2c230', '黃'); flag(c, 2.2, 1.3, '#f2c230', '黃'); flag(c, 1.25, 0.6, '#f2c230', '天'); }
  });
}
function sprPalace(ts, cleared) {
  return Spr.get('palace' + (cleared ? 'c' : ''), ts, 3.0, 2.9, 1.5, 2.45, c => {
    ellipse(c, 1.5, 2.42, 1.35, 0.25, 'rgba(0,0,0,.3)');
    // 台基
    let g = c.createLinearGradient(0, 2.0, 0, 2.42); g.addColorStop(0, '#d8d2c2'); g.addColorStop(1, '#8f8878');
    c.fillStyle = g; c.fillRect(0.2, 2.0, 2.6, 0.42);
    c.fillStyle = '#e9e3d4'; c.fillRect(0.15, 1.95, 2.7, 0.07);
    // 殿身
    c.fillStyle = '#a8322a'; c.fillRect(0.55, 1.35, 1.9, 0.62);
    c.fillStyle = '#7a1f19'; for (let k = 0; k < 7; k++) c.fillRect(0.6 + k * 0.3, 1.38, 0.06, 0.58);
    c.fillStyle = '#2b1a12'; c.fillRect(1.32, 1.6, 0.36, 0.37);
    roof(c, 0.35, 1.0, 2.3, 0.45, cleared ? '#3c6f58' : '#c9952e', '#7a4a10');
    c.fillStyle = '#a8322a'; c.fillRect(0.95, 0.7, 1.1, 0.35);
    roof(c, 0.8, 0.35, 1.4, 0.42, cleared ? '#3c6f58' : '#c9952e', '#7a4a10');
    flag(c, 0.25, 1.95, cleared ? '#2f8f4e' : '#f2c230', cleared ? '劉' : '黃');
    flag(c, 2.75, 1.95, cleared ? '#2f8f4e' : '#f2c230', cleared ? '劉' : '黃');
  });
}
function sprGate(ts, open, enemy) {
  return Spr.get('gate' + (open ? 'o' : '') + (enemy ? 'e' : ''), ts, 3.0, 2.4, 1.5, 1.95, c => {
    ellipse(c, 1.5, 1.92, 1.4, 0.2, 'rgba(0,0,0,.3)');
    const g = c.createLinearGradient(0, 1.1, 0, 1.95); g.addColorStop(0, '#bdb6a5'); g.addColorStop(1, '#6f695d');
    c.fillStyle = g; c.fillRect(0.0, 1.15, 3.0, 0.8);
    c.fillStyle = '#d2cbb9'; for (let k = 0; k < 15; k++) c.fillRect(0.02 + k * 0.2, 1.05, 0.12, 0.12);
    c.strokeStyle = 'rgba(0,0,0,.12)'; c.lineWidth = 0.012;
    for (let r = 0; r < 4; r++) for (let k = 0; k < 15; k++) c.strokeRect(k * 0.2 + (r % 2) * 0.1, 1.15 + r * 0.2, 0.2, 0.2);
    c.fillStyle = '#9e3b2f'; c.fillRect(0.95, 0.72, 1.1, 0.42);
    roof(c, 0.75, 0.4, 1.5, 0.4, '#2f3f56', '#d4a84a');
    c.fillStyle = open ? '#1a120c' : '#8e2a20';
    c.beginPath(); c.moveTo(1.25, 1.95); c.lineTo(1.25, 1.55); c.arc(1.5, 1.55, 0.25, Math.PI, 0); c.lineTo(1.75, 1.95); c.fill();
    if (!open) { c.fillStyle = '#e0b04a'; for (const [x, y] of [[1.38, 1.6], [1.62, 1.6], [1.38, 1.8], [1.62, 1.8]]) ellipse(c, x, y, 0.025, 0.025, '#e0b04a'); c.fillStyle = '#5a1a14'; c.fillRect(1.49, 1.35, 0.02, 0.6); }
    const fc = enemy ? '#3a2a2a' : (open ? '#2f8f4e' : '#c8342c');
    flag(c, 0.95, 0.75, fc, enemy ? '董' : (open ? '劉' : '漢')); flag(c, 2.05, 0.75, fc, enemy ? '董' : (open ? '劉' : '漢'));
  });
}
function sprBridge(ts, horiz) {
  return Spr.get('br' + (horiz ? 'h' : 'v'), ts, 1.2, 1.2, 0.6, 0.6, c => {
    c.save(); c.translate(0.6, 0.6); if (!horiz) c.rotate(Math.PI / 2);
    c.fillStyle = 'rgba(0,0,0,.25)'; c.fillRect(-0.6, -0.26, 1.2, 0.56);
    for (let k = 0; k < 7; k++) {
      const g = c.createLinearGradient(0, -0.3, 0, 0.3); g.addColorStop(0, '#c99a62'); g.addColorStop(1, '#8a6238');
      c.fillStyle = g; c.fillRect(-0.6 + k * 0.172, -0.32, 0.15, 0.6);
    }
    c.fillStyle = '#5a3d22'; c.fillRect(-0.6, -0.36, 1.2, 0.06); c.fillRect(-0.6, 0.26, 1.2, 0.06);
    c.restore();
  });
}

// --------------------------------------------------------------- 人物绘制 --
const SKIN = '#f3cfa3';
function drawUnit(ctx, x, y, s, lk, dir, phase, o = {}) {
  ctx.save();
  ctx.translate(x, y);
  const k = s * (o.scale || 1);
  ctx.scale(k, k);
  const moving = !!o.moving;
  const sw = moving ? Math.sin(phase * Math.PI * 2) : 0;
  const bob = moving ? Math.abs(sw) * 0.045 : Math.sin(phase * 1.5) * 0.008;
  ellipse(ctx, 0, 0, 0.27, 0.09, 'rgba(0,0,0,.3)');
  if (o.aura) {
    const g = ctx.createRadialGradient(0, -0.5, 0.1, 0, -0.5, 0.75);
    g.addColorStop(0, o.aura); g.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = g; ctx.fillRect(-0.8, -1.3, 1.6, 1.4);
  }
  const body = lk.body || '#777', trim = lk.trim || '#ddd', dark = shade(body, -0.45);
  // 腿
  ctx.fillStyle = dark;
  if (dir === 0 || dir === 3) {
    rrect(ctx, -0.12, -0.22 + Math.max(0, sw) * 0.04, 0.09, 0.22, 0.03); ctx.fill();
    rrect(ctx, 0.03, -0.22 + Math.max(0, -sw) * 0.04, 0.09, 0.22, 0.03); ctx.fill();
  } else {
    rrect(ctx, -0.05 + sw * 0.07, -0.22, 0.09, 0.22, 0.03); ctx.fill();
    rrect(ctx, -0.05 - sw * 0.07, -0.22, 0.09, 0.22, 0.03); ctx.fill();
  }
  ctx.translate(0, -bob);
  const cape = () => {
    if (!lk.cape) return;
    ctx.fillStyle = lk.cape;
    ctx.beginPath();
    const fl = moving ? Math.sin(phase * Math.PI * 4) * 0.03 : 0;
    if (dir === 3) { ctx.moveTo(-0.17, -0.5); ctx.lineTo(0.17, -0.5); ctx.lineTo(0.22 + fl, -0.12); ctx.lineTo(-0.22 - fl, -0.12); }
    else if (dir === 1) { ctx.moveTo(0.02, -0.5); ctx.lineTo(0.14, -0.48); ctx.lineTo(0.3 + fl, -0.14); ctx.lineTo(0.06, -0.16); }
    else if (dir === 2) { ctx.moveTo(-0.02, -0.5); ctx.lineTo(-0.14, -0.48); ctx.lineTo(-0.3 - fl, -0.14); ctx.lineTo(-0.06, -0.16); }
    ctx.closePath(); ctx.fill();
  };
  if (dir !== 3) cape();
  // 身体
  const g = ctx.createLinearGradient(-0.2, 0, 0.2, 0);
  g.addColorStop(0, shade(body, 0.2)); g.addColorStop(1, shade(body, -0.25));
  ctx.fillStyle = g;
  ctx.beginPath(); ctx.moveTo(-0.19, -0.17); ctx.lineTo(0.19, -0.17); ctx.lineTo(0.15, -0.5); ctx.quadraticCurveTo(0, -0.56, -0.15, -0.5); ctx.closePath(); ctx.fill();
  ctx.fillStyle = trim; ctx.fillRect(-0.17, -0.31, 0.34, 0.045);
  if (dir === 0) { ctx.fillStyle = shade(body, 0.35); ctx.beginPath(); ctx.moveTo(-0.06, -0.5); ctx.lineTo(0, -0.38); ctx.lineTo(0.06, -0.5); ctx.fill(); }
  // 手臂
  ctx.fillStyle = shade(body, -0.1);
  const ay = -0.42, as = moving ? sw * 0.04 : 0;
  if (dir !== 2) ellipse(ctx, -0.2, ay + 0.1 + as, 0.06, 0.11, shade(body, -0.1));
  if (dir !== 1) ellipse(ctx, 0.2, ay + 0.1 - as, 0.06, 0.11, shade(body, -0.1));
  if (dir === 3) cape();
  // 头
  const hy = -0.66;
  ctx.beginPath(); ctx.arc(0, hy, 0.16, 0, Math.PI * 2); ctx.fillStyle = SKIN; ctx.fill();
  // 头发底
  ctx.fillStyle = '#1d1712';
  if (dir === 3) { ctx.beginPath(); ctx.arc(0, hy, 0.165, 0, Math.PI * 2); ctx.fill(); }
  else { ctx.beginPath(); ctx.arc(0, hy - 0.02, 0.165, Math.PI * 1.05, Math.PI * 1.95); ctx.fill(); }
  // 脸
  if (dir !== 3) {
    ctx.fillStyle = '#2a1d14';
    const ex = dir === 1 ? -0.07 : dir === 2 ? 0.07 : 0;
    if (dir === 0) { ellipse(ctx, -0.058, hy + 0.01, 0.022, 0.03, '#2a1d14'); ellipse(ctx, 0.058, hy + 0.01, 0.022, 0.03, '#2a1d14'); }
    else ellipse(ctx, ex, hy + 0.01, 0.022, 0.03, '#2a1d14');
    ctx.globalAlpha = 0.35;
    if (dir === 0) { ellipse(ctx, -0.1, hy + 0.06, 0.03, 0.018, '#ff7a7a'); ellipse(ctx, 0.1, hy + 0.06, 0.03, 0.018, '#ff7a7a'); }
    ctx.globalAlpha = 1;
    if (lk.beard) {
      ctx.fillStyle = '#1b1410';
      const bx = dir === 1 ? -0.04 : dir === 2 ? 0.04 : 0;
      ctx.beginPath(); ctx.moveTo(bx - 0.1, hy + 0.06); ctx.quadraticCurveTo(bx, hy + 0.36, bx + 0.1, hy + 0.06); ctx.quadraticCurveTo(bx, hy + 0.12, bx - 0.1, hy + 0.06); ctx.fill();
    }
  }
  // 冠帽
  switch (lk.hat) {
    case 'crown':
      ctx.fillStyle = '#1d1712'; ellipse(ctx, 0, hy - 0.17, 0.07, 0.06, '#1d1712');
      ctx.fillStyle = '#e8c04a'; ctx.fillRect(-0.05, hy - 0.2, 0.1, 0.04); break;
    case 'hood':
      ctx.fillStyle = lk.body; ctx.beginPath(); ctx.arc(0, hy - 0.02, 0.18, Math.PI * 0.95, Math.PI * 2.05); ctx.fill();
      if (dir === 3) { ctx.beginPath(); ctx.arc(0, hy, 0.18, 0, Math.PI * 2); ctx.fill(); }
      break;
    case 'helmet': {
      const hg = ctx.createLinearGradient(-0.18, 0, 0.18, 0); hg.addColorStop(0, '#d6dce6'); hg.addColorStop(1, '#7c8696');
      ctx.fillStyle = hg; ctx.beginPath(); ctx.arc(0, hy - 0.02, 0.18, Math.PI, Math.PI * 2); ctx.fill();
      ctx.fillRect(-0.19, hy - 0.04, 0.38, 0.04);
      ctx.fillStyle = '#d23a34'; ctx.beginPath(); ctx.moveTo(-0.03, hy - 0.2); ctx.quadraticCurveTo(0.02, hy - 0.42, 0.12, hy - 0.36); ctx.quadraticCurveTo(0.04, hy - 0.3, 0.03, hy - 0.2); ctx.fill();
      break;
    }
    case 'scholar':
      ctx.fillStyle = '#222'; rrect(ctx, -0.13, hy - 0.26, 0.26, 0.13, 0.03); ctx.fill();
      ctx.fillRect(-0.17, hy - 0.15, 0.34, 0.03); break;
    case 'scarf':
      ctx.fillStyle = '#f2c230'; ctx.fillRect(-0.17, hy - 0.11, 0.34, 0.07);
      if (dir !== 0) { ctx.beginPath(); ctx.moveTo(dir === 1 ? 0.15 : -0.15, hy - 0.08); ctx.lineTo(dir === 1 ? 0.3 : -0.3, hy + 0.05); ctx.lineTo(dir === 1 ? 0.24 : -0.24, hy + 0.08); ctx.fill(); }
      break;
    case 'bun':
      ellipse(ctx, 0, hy - 0.17, 0.08, 0.07, '#1d1712'); break;
    case 'straw':
      ctx.fillStyle = '#d8b46a'; ctx.beginPath(); ctx.moveTo(-0.28, hy - 0.06); ctx.lineTo(0, hy - 0.3); ctx.lineTo(0.28, hy - 0.06); ctx.closePath(); ctx.fill(); break;
    case 'elder':
      ctx.fillStyle = '#ddd'; ellipse(ctx, 0, hy - 0.16, 0.07, 0.06, '#ddd');
      if (dir !== 3) { ctx.fillStyle = '#eee'; ctx.beginPath(); ctx.moveTo(-0.08, hy + 0.06); ctx.quadraticCurveTo(0, hy + 0.32, 0.08, hy + 0.06); ctx.fill(); }
      break;
    case 'imperial':
      ctx.fillStyle = '#1a1a1a'; ctx.fillRect(-0.2, hy - 0.24, 0.4, 0.05); ctx.fillRect(-0.08, hy - 0.22, 0.16, 0.08);
      ctx.fillStyle = '#e8c04a'; for (let k = -2; k <= 2; k++) ctx.fillRect(k * 0.08 - 0.008, hy - 0.2, 0.016, 0.08); break;
  }
  // 军旗
  if (o.banner) {
    const bx = dir === 2 ? -0.24 : 0.24;
    ctx.strokeStyle = '#4b3520'; ctx.lineWidth = 0.035;
    ctx.beginPath(); ctx.moveTo(bx, -0.2); ctx.lineTo(bx, -1.28); ctx.stroke();
    const w = Math.sin(o.time * 4) * 0.04, dirx = dir === 2 ? -1 : 1;
    ctx.beginPath();
    ctx.moveTo(bx, -1.26);
    ctx.quadraticCurveTo(bx + dirx * 0.2, -1.3 + w, bx + dirx * 0.42, -1.24);
    ctx.lineTo(bx + dirx * 0.42, -0.94);
    ctx.quadraticCurveTo(bx + dirx * 0.2, -1.0 + w, bx, -0.96);
    ctx.closePath();
    ctx.fillStyle = o.banner.color; ctx.fill();
    ctx.strokeStyle = 'rgba(255,220,120,.9)'; ctx.lineWidth = 0.02; ctx.stroke();
    ctx.fillStyle = '#fff'; ctx.font = `700 0.2px ${CN_SERIF}`; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(o.banner.glyph, bx + dirx * 0.21, -1.1 + w * 0.5);
  }
  ctx.restore();
}

// ============================================================ 大地图渲染 ==
const Clouds = [];
function initClouds() {
  const r = mulberry32(88);
  for (let i = 0; i < 9; i++) Clouds.push({ x: r() * WW, y: r() * WH, s: 2.5 + r() * 3.5, seed: i });
}
function cloudSprite(ts, i, s) {
  return Spr.get('cloud' + i, ts, s * 2, s * 1.2, 0, 0, c => {
    const r = mulberry32(i * 31 + 5);
    for (let k = 0; k < 7; k++) {
      const x = s * (0.4 + r() * 1.2), y = s * (0.35 + r() * 0.5), rr = s * (0.25 + r() * 0.25);
      const g = c.createRadialGradient(x, y, 0, x, y, rr);
      g.addColorStop(0, 'rgba(255,255,255,.95)'); g.addColorStop(0.6, 'rgba(255,255,255,.55)'); g.addColorStop(1, 'rgba(255,255,255,0)');
      c.fillStyle = g; c.fillRect(x - rr, y - rr, rr * 2, rr * 2);
    }
  });
}

function renderWorld(ctx, W, H, ts, cam, time, player, flags) {
  const world = Game.world;
  const left = cam.x - W / ts / 2, top = cam.y - H / ts / 2;
  ctx.fillStyle = '#173f7f'; ctx.fillRect(0, 0, W, H);
  // 地表
  ctx.imageSmoothingEnabled = true; ctx.imageSmoothingQuality = 'high';
  {
    let sx = left * GPX, sy = top * GPX, sw = W / ts * GPX, sh = H / ts * GPX;
    let dx = 0, dy = 0, dw = W, dh = H;
    const gw = Game.ground.width, gh = Game.ground.height;
    if (sx < 0) { dx = -sx / GPX * ts; dw -= dx; sw += sx; sx = 0; }
    if (sy < 0) { dy = -sy / GPX * ts; dh -= dy; sh += sy; sy = 0; }
    if (sx + sw > gw) { const over = sx + sw - gw; dw -= over / GPX * ts; sw -= over; }
    if (sy + sh > gh) { const over = sy + sh - gh; dh -= over / GPX * ts; sh -= over; }
    if (sw > 0 && sh > 0) ctx.drawImage(Game.ground, sx, sy, sw, sh, dx, dy, dw, dh);
  }
  const x0 = Math.floor(left) - 2, x1 = Math.ceil(left + W / ts) + 2;
  const y0 = Math.floor(top) - 1, y1 = Math.ceil(top + H / ts) + 3;
  const SX = x => (x - left) * ts, SY = y => (y - top) * ts;
  const { t } = world;
  // 水面波光 & 桥
  for (let y = Math.max(0, y0); y < Math.min(WH, y1); y++) for (let x = Math.max(0, x0); x < Math.min(WW, x1); x++) {
    const v = t[y * WW + x];
    if (v === T.SEA || v === T.DEEP || v === T.RIVER) {
      const h = hash2(x, y, 9);
      if (h < 0.35) {
        const a = Math.sin(time * 1.6 + h * 40) * 0.5 + 0.1;
        if (a > 0) {
          ctx.strokeStyle = `rgba(255,255,255,${a * 0.55})`; ctx.lineWidth = Math.max(1, ts * 0.03);
          const cx = SX(x + 0.2 + h * 1.6), cy = SY(y + 0.3 + hash2(x, y, 10) * 0.5) + Math.sin(time + h * 9) * ts * 0.03;
          ctx.beginPath(); ctx.moveTo(cx - ts * 0.12, cy); ctx.quadraticCurveTo(cx, cy - ts * 0.06, cx + ts * 0.12, cy); ctx.stroke();
        }
      }
    } else if (v === T.BRIDGE) {
      const lr = (x > 0 && [T.ROAD, T.BRIDGE, T.GRASS, T.SAND, T.FOREST, T.HILL].includes(t[y * WW + x - 1])) ||
                 (x < WW - 1 && [T.ROAD, T.BRIDGE].includes(t[y * WW + x + 1]));
      const ud = (y > 0 && [T.ROAD, T.BRIDGE].includes(t[(y - 1) * WW + x])) || (y < WH - 1 && [T.ROAD, T.BRIDGE].includes(t[(y + 1) * WW + x]));
      blit(ctx, sprBridge(ts, ud ? false : lr), SX(x + 0.5), SY(y + 0.5));
    }
  }
  // 按行排序绘制物件（关隘最后绘制，压在山体之上）
  const gates = [];
  const pRow = Math.floor(player.ry + 0.001);
  for (let y = Math.max(0, y0); y < Math.min(WH, y1); y++) {
    for (let x = Math.max(0, x0); x < Math.min(WW, x1); x++) {
      const i = y * WW + x, v = t[i];
      const bx = SX(x + 0.5), by = SY(y + 1);
      const h = hash2(x, y, 21);
      const loc = world.locAt.get(i);
      if (loc) { if (v !== T.GATE) drawLocation(ctx, loc, bx, by, ts, flags); else gates.push([loc, bx, by]); continue; }
      if (v === T.FOREST) {
        const n = 2 + (h > 0.5 ? 1 : 0);
        for (let k = 0; k < n; k++) {
          const ox = (hash2(x, y, 30 + k) - 0.5) * 0.6, oy = -0.15 + k * 0.18 - (n === 3 ? 0.1 : 0);
          const s = 0.72 + hash2(x, y, 40 + k) * 0.25;
          const pine = (x < 38 && y < 56) ? h > 0.3 : h > 0.75;
          blit(ctx, pine ? sprPine(ts) : sprTree(ts, (x + k) % 3), bx + ox * ts, by + oy * ts - ts * 0.05, s);
        }
      } else if (v === T.MOUNT) {
        blit(ctx, sprMount(ts, Math.floor(h * 3)), bx + (hash2(x, y, 22) - 0.5) * ts * 0.25, by + ts * 0.05, world.range[i] ? 1 : 0.85);
      } else if (v === T.HILL) {
        blit(ctx, sprHill(ts), bx + (h - 0.5) * ts * 0.3, by - ts * 0.08, 0.9);
      } else if (v === T.GRASS && h < 0.18) {
        blit(ctx, sprTuft(ts, h < 0.05 ? 2 : h < 0.11 ? 1 : 0), bx + (hash2(x, y, 23) - 0.5) * ts * 0.6, by - ts * (0.2 + hash2(x, y, 24) * 0.5));
      }
    }
    if (y === pRow) drawPlayer(ctx, SX(player.rx + 0.5), SY(player.ry + 1) - ts * 0.08, ts, player, time);
  }
  for (const [loc, bx, by] of gates) drawLocation(ctx, loc, bx, by, ts, flags);
  // 云影与云
  for (const c of Clouds) {
    c.x += 0.0025; if (c.x > WW + 8) c.x = -10;
    const sp = cloudSprite(ts, c.seed, c.s);
    ctx.globalAlpha = 0.13; ctx.filter = 'none';
    ctx.globalCompositeOperation = 'multiply';
    ctx.drawImage(sp.cv, SX(c.x), SY(c.y), sp.w, sp.h);
    ctx.globalCompositeOperation = 'source-over';
  }
  for (const c of Clouds) {
    const sp = cloudSprite(ts, c.seed, c.s);
    ctx.globalAlpha = 0.28;
    ctx.drawImage(sp.cv, SX(c.x - 1.2 - (cam.x - c.x) * 0.08), SY(c.y - 2.6 - (cam.y - c.y) * 0.08), sp.w, sp.h);
  }
  ctx.globalAlpha = 1;
}

function drawLocation(ctx, loc, bx, by, ts, flags) {
  let s;
  switch (loc.kind) {
    case 'town': s = sprTown(ts); break;
    case 'village': s = sprVillage(ts); break;
    case 'fort': s = loc.theme === 'palace' ? sprPalace(ts, flags[BOSSES[loc.boss].flag]) : sprFort(ts, flags[BOSSES[loc.boss].flag]); break;
    case 'gate': s = sprGate(ts, !!flags[loc.need], false); break;
    case 'bossgate': s = sprGate(ts, !!flags[BOSSES[loc.boss].flag], !flags[BOSSES[loc.boss].flag]); break;
  }
  const k = { village: 0.72, town: 0.78, fort: 0.72, gate: 0.72, bossgate: 0.72 }[loc.kind];
  if (s) blit(ctx, s, bx, by + ts * 0.15, loc.theme === 'palace' ? 0.7 : k);
}

function drawPlayer(ctx, x, y, ts, p, time) {
  const lead = Game.leader();
  const lk = GENERALS[lead].look;
  drawUnit(ctx, x, y, ts, lk, p.dir, p.moving ? p.walk : time, { moving: p.moving, banner: { color: '#c8342c', glyph: '劉' }, time });
}

// ============================================================ 城镇 / 营寨 ==
const IT = { GROUND: 0, PAVE: 1, WALL: 2, HOUSE: 3, DOOR: 4, TREE: 5, EXIT: 6, WATER: 7, FLOOR: 8, DWALL: 9, CHEST: 10, FENCE: 11, FLOWER: 12, WELL: 13, TORCH: 14, CARPET: 15 };
const IWALK = new Set([IT.GROUND, IT.PAVE, IT.DOOR, IT.EXIT, IT.FLOOR, IT.FLOWER, IT.CARPET]);

const FAC_INFO = {
  inn:    { name: '宿屋',   sign: '宿', roof: '#2f5a62' },
  weapon: { name: '武器店', sign: '武', roof: '#4a4f5c' },
  armor:  { name: '防具店', sign: '防', roof: '#5a5246' },
  item:   { name: '道具店', sign: '藥', roof: '#3d6b3f' },
  food:   { name: '粮店',   sign: '糧', roof: '#7a5530' },
  tavern: { name: '酒馆',   sign: '酒', roof: '#8a2f2a' },
  gov:    { name: '官府',   sign: '府', roof: '#28324a' },
  house:  { name: '民居',   sign: '',   roof: '#6b5a48' },
};

const NPC_LOOKS = [
  { body: '#6f8fb5', trim: '#e8e0d0', hat: 'bun' },
  { body: '#b58a5a', trim: '#f0e0c0', hat: 'straw' },
  { body: '#8a5a8f', trim: '#e7d6ea', hat: 'scholar' },
  { body: '#9a9a9a', trim: '#eee', hat: 'elder' },
  { body: '#b8433a', trim: '#e9c46a', hat: 'helmet' },
  { body: '#5f9a6a', trim: '#e0f0d0', hat: 'bun' },
];

function genInterior(loc) {
  return (loc.kind === 'town' || loc.kind === 'village') ? genTown(loc) : genDungeon(loc);
}

function genTown(loc) {
  const town = loc.kind === 'town';
  const W = town ? 28 : 22, H = town ? 22 : 17;
  const r = mulberry32(loc.x * 131 + loc.y * 7);
  const t = new Uint8Array(W * H).fill(IT.GROUND);
  const I = (x, y) => y * W + x;
  const border = town ? IT.WALL : IT.FENCE;
  for (let x = 0; x < W; x++) { t[I(x, 0)] = border; t[I(x, H - 1)] = border; }
  for (let y = 0; y < H; y++) { t[I(0, y)] = border; t[I(W - 1, y)] = border; }
  const cx = W >> 1;
  const midY = town ? 10 : 7;
  for (let y = 1; y < H - 1; y++) { t[I(cx - 1, y)] = IT.PAVE; t[I(cx, y)] = IT.PAVE; }
  for (let x = 1; x < W - 1; x++) { t[I(x, midY)] = IT.PAVE; if (town) t[I(x, midY + 1)] = IT.PAVE; }
  t[I(cx - 1, H - 1)] = IT.EXIT; t[I(cx, H - 1)] = IT.EXIT;
  const slots = town
    ? [[2, 3], [7, 3], [16, 3], [21, 3], [2, 13], [7, 13], [16, 13], [21, 13]]
    : [[2, 2], [14, 2], [2, 10], [14, 10]];
  const facs = [...(loc.fac || [])];
  const buildings = [];
  const reserved = new Set();
  slots.forEach(([bx, by], si) => {
    const fac = facs.shift() || (r() < 0.6 ? 'house' : null);
    if (!fac) {
      for (let k = 0; k < 4; k++) { const x = bx + Math.floor(r() * 5), y = by + Math.floor(r() * 4); if (t[I(x, y)] === IT.GROUND) t[I(x, y)] = r() < 0.6 ? IT.TREE : IT.FLOWER; }
      return;
    }
    for (let y = by; y < by + 4; y++) for (let x = bx; x < bx + 5; x++) t[I(x, y)] = IT.HOUSE;
    if (fac !== 'house') t[I(bx + 2, by + 3)] = IT.DOOR;
    buildings.push({ x: bx, y: by, w: 5, h: 4, fac });
    for (let x = bx; x < bx + 5; x++) reserved.add(I(x, by + 4));
  });
  // 前排通道
  for (const b of buildings) for (let x = 1; x < W - 1; x++) reserved.add(I(x, b.y + 4));
  for (let y = 1; y < H - 1; y++) { reserved.add(I(cx - 1, y)); reserved.add(I(cx, y)); }
  // 井
  const wx = cx + 2, wy = midY - 2;
  if (t[I(wx, wy)] === IT.GROUND && !reserved.has(I(wx, wy))) t[I(wx, wy)] = IT.WELL;
  // 树木花草
  for (let k = 0; k < (town ? 26 : 18); k++) {
    const x = 1 + Math.floor(r() * (W - 2)), y = 1 + Math.floor(r() * (H - 2));
    const i = I(x, y);
    if (t[i] !== IT.GROUND || reserved.has(i)) continue;
    t[i] = r() < 0.55 ? IT.TREE : IT.FLOWER;
  }
  // 连通性检查
  const entry = { x: cx, y: H - 2 };
  const reach = bfs(t, W, H, entry.x, entry.y);
  for (const b of buildings) if (b.fac !== 'house' && !reach[I(b.x + 2, b.y + 4)]) {
    for (let i = 0; i < t.length; i++) if (t[i] === IT.TREE) t[i] = IT.FLOWER;
    break;
  }
  // 居民
  const npcs = [];
  const lines = NPC_LINES[loc.id] || ['今天天气不错。'];
  const nN = town ? 6 : 4;
  for (let k = 0, tries = 0; k < nN && tries < 200; tries++) {
    const x = 1 + Math.floor(r() * (W - 2)), y = 1 + Math.floor(r() * (H - 3));
    if (!IWALK.has(t[I(x, y)]) || t[I(x, y)] === IT.DOOR || npcs.some(n => n.x === x && n.y === y) || (x === entry.x && y === entry.y)) continue;
    npcs.push({ x, y, rx: x, ry: y, dir: 0, look: NPC_LOOKS[(k + loc.x) % NPC_LOOKS.length], line: lines[k % lines.length], home: [x, y], next: 1 + r() * 3, walk: 0 });
    k++;
  }
  return { id: loc.id, loc, kind: loc.kind, theme: town ? 'town' : 'village', w: W, h: H, t, buildings, npcs, entry, chests: [], torches: [], dark: 0 };
}

function bfs(t, W, H, sx, sy) {
  const seen = new Uint8Array(W * H), q = [sy * W + sx]; seen[q[0]] = 1;
  for (let h = 0; h < q.length; h++) {
    const i = q[h], x = i % W, y = (i / W) | 0;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
      const j = ny * W + nx; if (seen[j] || !IWALK.has(t[j])) continue;
      seen[j] = 1; q.push(j);
    }
  }
  return seen;
}

function genDungeon(loc) {
  const [cw, ch] = loc.size;
  const W = cw * 2 + 1, H = ch * 2 + 2;
  const r = mulberry32(loc.x * 977 + loc.y * 31);
  const t = new Uint8Array(W * H).fill(IT.DWALL);
  const I = (x, y) => y * W + x;
  const visited = new Uint8Array(cw * ch);
  const sx = cw >> 1, sy = ch - 1, bxc = cw >> 1;
  const stack = [[sx, sy]]; visited[sy * cw + sx] = 1; t[I(sx * 2 + 1, sy * 2 + 1)] = IT.FLOOR;
  while (stack.length) {
    const [x, y] = stack[stack.length - 1];
    const nb = [[1, 0], [-1, 0], [0, 1], [0, -1]].map(([dx, dy]) => [x + dx, y + dy]).filter(([nx, ny]) => nx >= 0 && ny >= 0 && nx < cw && ny < ch && !visited[ny * cw + nx]);
    if (!nb.length) { stack.pop(); continue; }
    const [nx, ny] = nb[Math.floor(r() * nb.length)];
    visited[ny * cw + nx] = 1;
    t[I(nx * 2 + 1, ny * 2 + 1)] = IT.FLOOR;
    t[I(x + nx + 1, y + ny + 1)] = IT.FLOOR;
    stack.push([nx, ny]);
  }
  // 额外通路
  for (let y = 0; y < ch; y++) for (let x = 0; x < cw; x++) {
    if (x < cw - 1 && r() < 0.1) t[I(x * 2 + 2, y * 2 + 1)] = IT.FLOOR;
    if (y < ch - 1 && r() < 0.1) t[I(x * 2 + 1, y * 2 + 2)] = IT.FLOOR;
  }
  // 首领厅
  const bx = bxc * 2 + 1;
  for (let y = 1; y <= 3; y++) for (let x = bx - 2; x <= bx + 2; x++) if (x > 0 && x < W - 1) t[I(x, y)] = IT.CARPET;
  t[I(bx, 4)] = t[I(bx, 4)] === IT.DWALL ? IT.FLOOR : t[I(bx, 4)];
  // 入口
  const ex = sx * 2 + 1;
  t[I(ex, H - 2)] = IT.FLOOR; t[I(ex, H - 1)] = IT.EXIT;
  // 宝箱：死胡同
  const chests = [];
  const deadEnds = [];
  for (let y = 0; y < ch; y++) for (let x = 0; x < cw; x++) {
    const tx = x * 2 + 1, ty = y * 2 + 1;
    if (t[I(tx, ty)] !== IT.FLOOR || (x === sx && y === sy) || y === 0) continue;
    let open = 0; for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) if (IWALK.has(t[I(tx + dx, ty + dy)])) open++;
    if (open === 1) deadEnds.push([tx, ty]);
  }
  for (let k = 0; k < 3 && deadEnds.length; k++) {
    const [x, y] = deadEnds.splice(Math.floor(r() * deadEnds.length), 1)[0];
    t[I(x, y)] = IT.CHEST; chests.push({ x, y, id: `${loc.id}_${k}` });
  }
  // 火把
  const torches = [];
  for (let y = 0; y < H - 1; y++) for (let x = 0; x < W; x++) {
    if (t[I(x, y)] === IT.DWALL && IWALK.has(t[I(x, y + 1)]) && r() < 0.16) { t[I(x, y)] = IT.TORCH; torches.push({ x, y }); }
  }
  // 首领厅两侧火把
  for (const x of [bx - 2, bx + 2]) if (t[I(x, 0)] === IT.DWALL) { t[I(x, 0)] = IT.TORCH; torches.push({ x, y: 0 }); }
  const boss = { x: bx, y: 1, id: loc.boss };
  return { id: loc.id, loc, kind: 'dungeon', theme: loc.theme, w: W, h: H, t, buildings: [], npcs: [], entry: { x: ex, y: H - 2 }, chests, torches, boss,
           dark: loc.theme === 'palace' ? 0.5 : loc.theme === 'fort' ? 0.68 : 0.82 };
}

// 宝箱内容
function chestLoot(id, zone) {
  const r = mulberry32([...id].reduce((a, c) => a * 31 + c.charCodeAt(0), 7));
  const tier = { A: 0, B: 1, C: 2, D1: 3, D2: 3, D3: 4 }[zone] || 0;
  const roll = r();
  if (roll < 0.4) return { gold: Math.round((60 + tier * 180) * (0.8 + r() * 0.5)) };
  const pools = [['shangyao', 'bingshu'], ['liangyao', 'bingshu', 'huoyao'], ['liangyao', 'huanhun', 'huoyao'], ['liangyao', 'xiandan', 'huanhun'], ['xiandan', 'huanhun', 'liangyao']];
  const p = pools[tier];
  return { item: p[Math.floor(r() * p.length)], n: 1 + (r() < 0.4 ? 1 : 0) };
}

// --------------------------------------------------------------- 内部绘制 --
const THEME = {
  town:    { ground: '#cdb98e', ground2: '#bfa97c', pave: '#b8b0a0', pave2: '#a39b8b' },
  village: { ground: '#9cc26a', ground2: '#8db45d', pave: '#c9ab7c', pave2: '#b39566' },
  cave:    { floor: '#4b4036', floor2: '#3f352c', wallTop: '#6b5c4c', wallFace: '#3a2f25', carpet: '#7a2f28' },
  fort:    { floor: '#6b5640', floor2: '#5d4a36', wallTop: '#8b6a44', wallFace: '#4f3a24', carpet: '#9a7a20' },
  palace:  { floor: '#5a4e48', floor2: '#4e433e', wallTop: '#9e3b30', wallFace: '#5e1f19', carpet: '#a3282a' },
};

function renderInteriorBase(m, ts) {
  const cv = document.createElement('canvas');
  cv.width = m.w * ts; cv.height = m.h * ts;
  const c = cv.getContext('2d');
  c.scale(ts, ts);
  const th = THEME[m.theme];
  const at = (x, y) => (x < 0 || y < 0 || x >= m.w || y >= m.h) ? -1 : m.t[y * m.w + x];
  for (let y = 0; y < m.h; y++) for (let x = 0; x < m.w; x++) {
    const v = at(x, y), h = hash2(x, y, m.w);
    c.save(); c.translate(x, y);
    if (m.kind !== 'dungeon') {
      // 地面
      c.fillStyle = h > 0.5 ? th.ground : th.ground2; c.fillRect(0, 0, 1.01, 1.01);
      for (let k = 0; k < 3; k++) { const px = hash2(x, y, k + 3), py = hash2(x, y, k + 9); ellipse(c, px, py, 0.03, 0.02, 'rgba(0,0,0,.08)'); }
      if (v === IT.PAVE || v === IT.EXIT || v === IT.DOOR) {
        c.fillStyle = h > 0.5 ? th.pave : th.pave2; c.fillRect(0, 0, 1.01, 1.01);
        if (m.theme === 'town') {
          c.strokeStyle = 'rgba(60,50,40,.25)'; c.lineWidth = 0.03;
          c.strokeRect(0.02, 0.02, 0.47, 0.47); c.strokeRect(0.51, 0.02, 0.47, 0.47); c.strokeRect(0.02, 0.51, 0.47, 0.47); c.strokeRect(0.51, 0.51, 0.47, 0.47);
        }
      }
      if (v === IT.FLOWER) {
        const cols = ['#ff8fb1', '#ffe066', '#ffffff', '#b28dff'];
        for (let k = 0; k < 5; k++) { const px = 0.15 + hash2(x, y, k + 20) * 0.7, py = 0.15 + hash2(x, y, k + 30) * 0.7; ellipse(c, px, py, 0.06, 0.06, cols[(k + x) % 4]); ellipse(c, px, py, 0.02, 0.02, '#e8a020'); }
      }
      if (v === IT.WALL) {
        const g = c.createLinearGradient(0, 0, 0, 1); g.addColorStop(0, '#a49e90'); g.addColorStop(1, '#7d776b');
        c.fillStyle = g; c.fillRect(0, 0, 1.01, 1.01);
        c.strokeStyle = 'rgba(0,0,0,.18)'; c.lineWidth = 0.03;
        for (let r = 0; r < 3; r++) for (let k = 0; k < 2; k++) c.strokeRect(k * 0.5 + (r % 2) * 0.25, r * 0.333, 0.5, 0.333);
        c.fillStyle = 'rgba(255,255,255,.15)'; c.fillRect(0, 0, 1, 0.08);
      }
      if (v === IT.FENCE) {
        c.strokeStyle = '#7a5a36'; c.lineWidth = 0.08;
        c.beginPath(); c.moveTo(0, 0.45); c.lineTo(1, 0.45); c.moveTo(0, 0.7); c.lineTo(1, 0.7); c.stroke();
        c.fillStyle = '#8b6a42'; c.fillRect(0.12, 0.2, 0.12, 0.7); c.fillRect(0.62, 0.2, 0.12, 0.7);
      }
      if (v === IT.WELL) {
        ellipse(c, 0.5, 0.62, 0.38, 0.28, '#7d776b'); ellipse(c, 0.5, 0.58, 0.3, 0.2, '#25404f');
        c.fillStyle = '#6b4a2b'; c.fillRect(0.12, 0.05, 0.06, 0.55); c.fillRect(0.82, 0.05, 0.06, 0.55);
        roof(c, 0.05, -0.1, 0.9, 0.3, '#5a4a3a');
      }
      if (v === IT.EXIT) { c.fillStyle = 'rgba(255,230,150,.25)'; c.fillRect(0, 0.6, 1, 0.4); }
    } else {
      const wall = v === IT.DWALL || v === IT.TORCH || v === -1;
      if (!wall) {
        c.fillStyle = h > 0.5 ? th.floor : th.floor2; c.fillRect(0, 0, 1.01, 1.01);
        if (m.theme === 'palace') { c.strokeStyle = 'rgba(0,0,0,.25)'; c.lineWidth = 0.025; c.strokeRect(0.02, 0.02, 0.96, 0.96); }
        else if (m.theme === 'fort') { c.strokeStyle = 'rgba(0,0,0,.2)'; c.lineWidth = 0.02; for (let k = 1; k < 4; k++) { c.beginPath(); c.moveTo(0, k * 0.25); c.lineTo(1, k * 0.25); c.stroke(); } }
        else { for (let k = 0; k < 4; k++) ellipse(c, hash2(x, y, k + 1), hash2(x, y, k + 5), 0.08, 0.05, 'rgba(0,0,0,.15)'); }
        if (v === IT.CARPET) {
          c.fillStyle = th.carpet; c.fillRect(0, 0, 1.01, 1.01);
          c.strokeStyle = '#e0b04a'; c.lineWidth = 0.04;
          if (at(x - 1, y) !== IT.CARPET) { c.beginPath(); c.moveTo(0.08, 0); c.lineTo(0.08, 1); c.stroke(); }
          if (at(x + 1, y) !== IT.CARPET) { c.beginPath(); c.moveTo(0.92, 0); c.lineTo(0.92, 1); c.stroke(); }
        }
        if (v === IT.EXIT) { const g = c.createLinearGradient(0, 0, 0, 1); g.addColorStop(0, 'rgba(255,220,150,0)'); g.addColorStop(1, 'rgba(255,220,150,.5)'); c.fillStyle = g; c.fillRect(0, 0, 1, 1); }
        // 上方墙体的立面
        const above = at(x, y - 1);
        if (above === IT.DWALL || above === IT.TORCH) {
          const g = c.createLinearGradient(0, 0, 0, 0.42); g.addColorStop(0, th.wallFace); g.addColorStop(1, shade(th.wallFace, -0.3));
          c.fillStyle = g; c.fillRect(0, 0, 1.01, 0.42);
          if (m.theme === 'palace') { c.fillStyle = '#e0b04a'; c.fillRect(0, 0.34, 1.01, 0.04); }
          c.fillStyle = 'rgba(0,0,0,.25)'; c.fillRect(0, 0.42, 1.01, 0.08);
        }
      } else {
        c.fillStyle = th.wallTop; c.fillRect(0, 0, 1.01, 1.01);
        const g = c.createLinearGradient(0, 0, 1, 1); g.addColorStop(0, 'rgba(255,255,255,.12)'); g.addColorStop(1, 'rgba(0,0,0,.2)');
        c.fillStyle = g; c.fillRect(0, 0, 1.01, 1.01);
        if (m.theme === 'fort') { c.fillStyle = 'rgba(0,0,0,.18)'; for (let k = 0; k < 4; k++) c.fillRect(k * 0.25 + 0.2, 0, 0.03, 1); }
        else if (m.theme === 'cave') { for (let k = 0; k < 3; k++) ellipse(c, hash2(x, y, k + 2), hash2(x, y, k + 6), 0.14, 0.1, 'rgba(0,0,0,.12)'); }
        else { c.fillStyle = 'rgba(224,176,74,.3)'; c.fillRect(0, 0.46, 1, 0.08); }
      }
    }
    if (v === IT.TREE) {
      c.restore();
      const s = sprTree(ts, (x + y) % 3);
      c.save(); c.translate(x + 0.5, y + 0.95); c.scale(0.85, 0.85); c.drawImage(s.cv, -0.6, -1.45, 1.2, 1.6); c.restore();
      continue;
    }
    c.restore();
  }
  // 建筑
  for (const b of m.buildings) drawBuilding(c, b);
  return cv;
}

function drawBuilding(c, b) {
  const fi = FAC_INFO[b.fac];
  const x = b.x, y = b.y, w = b.w, h = b.h;
  ellipse(c, x + w / 2, y + h + 0.05, w * 0.55, 0.25, 'rgba(0,0,0,.25)');
  // 墙面
  const g = c.createLinearGradient(0, y + 1.4, 0, y + h);
  g.addColorStop(0, '#f1e6cf'); g.addColorStop(1, '#d6c6a4');
  c.fillStyle = g; c.fillRect(x + 0.15, y + 1.5, w - 0.3, h - 1.5);
  c.fillStyle = '#7a2a22';
  for (const px of [x + 0.15, x + w - 0.35, x + 1.4, x + w - 1.6]) c.fillRect(px, y + 1.5, 0.2, h - 1.5);
  c.fillStyle = '#5a4030'; c.fillRect(x + 0.1, y + h - 0.12, w - 0.2, 0.12);
  // 窗
  for (const wx of [x + 0.6, x + w - 1.25]) {
    c.fillStyle = '#4a3020'; c.fillRect(wx, y + 2.1, 0.65, 0.6);
    c.strokeStyle = '#c9a86a'; c.lineWidth = 0.04;
    for (let k = 1; k < 4; k++) { c.beginPath(); c.moveTo(wx + k * 0.16, y + 2.1); c.lineTo(wx + k * 0.16, y + 2.7); c.stroke(); }
    c.beginPath(); c.moveTo(wx, y + 2.4); c.lineTo(wx + 0.65, y + 2.4); c.stroke();
  }
  // 门
  if (b.fac !== 'house') {
    c.fillStyle = '#2a1a10'; c.fillRect(x + 2.1, y + 2.25, 0.8, h - 2.25);
    c.fillStyle = shade(fi.roof, 0.1); c.fillRect(x + 2.1, y + 2.25, 0.8, 0.35);
    c.strokeStyle = 'rgba(255,255,255,.3)'; c.lineWidth = 0.03;
    for (let k = 1; k < 4; k++) { c.beginPath(); c.moveTo(x + 2.1 + k * 0.2, y + 2.25); c.lineTo(x + 2.1 + k * 0.2, y + 2.6); c.stroke(); }
  } else {
    c.fillStyle = '#5a3a22'; c.fillRect(x + 2.15, y + 2.4, 0.7, h - 2.4);
  }
  // 屋顶
  roof(c, x - 0.15, y - 0.1, w + 0.3, 1.75, fi.roof, b.fac === 'gov' ? '#d4a84a' : null);
  // 招牌
  if (fi.sign) {
    c.fillStyle = '#2a1a10'; rrect(c, x + 1.95, y + 1.55, 1.1, 0.62, 0.06); c.fill();
    c.strokeStyle = '#e0b04a'; c.lineWidth = 0.04; c.stroke();
    c.fillStyle = '#f6e3a8'; c.font = `700 0.46px ${CN_KAI}`; c.textAlign = 'center'; c.textBaseline = 'middle';
    c.fillText(fi.sign, x + 2.5, y + 1.88);
    // 灯笼
    for (const lx of [x + 0.6, x + w - 0.6]) {
      c.strokeStyle = '#3a2a1a'; c.lineWidth = 0.02; c.beginPath(); c.moveTo(lx, y + 1.5); c.lineTo(lx, y + 1.7); c.stroke();
      const lg = c.createRadialGradient(lx, y + 1.9, 0.02, lx, y + 1.9, 0.2); lg.addColorStop(0, '#ffd27a'); lg.addColorStop(1, '#d2382c');
      ellipse(c, lx, y + 1.9, 0.14, 0.18, lg);
    }
  }
}

function drawChest(ctx, x, y, ts, open) {
  ctx.save(); ctx.translate(x, y); ctx.scale(ts, ts);
  ellipse(ctx, 0, 0.32, 0.32, 0.08, 'rgba(0,0,0,.35)');
  const g = ctx.createLinearGradient(0, -0.2, 0, 0.3); g.addColorStop(0, '#b0702c'); g.addColorStop(1, '#6a3c16');
  ctx.fillStyle = g; rrect(ctx, -0.3, -0.05, 0.6, 0.36, 0.04); ctx.fill();
  ctx.fillStyle = open ? '#2a160a' : '#c8842f';
  rrect(ctx, -0.3, open ? -0.3 : -0.22, 0.6, open ? 0.12 : 0.2, 0.08); ctx.fill();
  ctx.fillStyle = '#f0c24a'; ctx.fillRect(-0.32, -0.06, 0.64, 0.05); ctx.fillRect(-0.04, -0.06, 0.08, 0.16);
  if (!open) { ctx.globalAlpha = 0.6 + Math.sin(performance.now() / 300) * 0.3; ellipse(ctx, 0.18, -0.16, 0.03, 0.03, '#fff'); ctx.globalAlpha = 1; }
  ctx.restore();
}

let darkCanvas = null;
function renderInterior(ctx, W, H, ts, cam, time, player, m, flags) {
  if (!m.cache || m.cache.ts !== ts || m.dirty) { m.cache = { ts, cv: renderInteriorBase(m, ts) }; m.dirty = false; }
  const left = cam.x - W / ts / 2, top = cam.y - H / ts / 2;
  ctx.fillStyle = m.kind === 'dungeon' ? '#07060a' : '#2a2418';
  ctx.fillRect(0, 0, W, H);
  const SX = x => (x - left) * ts, SY = y => (y - top) * ts;
  ctx.drawImage(m.cache.cv, Math.round(SX(0)), Math.round(SY(0)));
  // 出口提示
  for (let y = 0; y < m.h; y++) for (let x = 0; x < m.w; x++) if (m.t[y * m.w + x] === IT.EXIT) {
    const a = 0.4 + Math.sin(time * 3) * 0.3;
    ctx.fillStyle = `rgba(255,232,160,${a})`;
    const cx = SX(x + 0.5), cy = SY(y + 0.55) + Math.sin(time * 3) * ts * 0.05;
    ctx.beginPath(); ctx.moveTo(cx - ts * 0.15, cy - ts * 0.08); ctx.lineTo(cx, cy + ts * 0.08); ctx.lineTo(cx + ts * 0.15, cy - ts * 0.08); ctx.lineWidth = ts * 0.06; ctx.strokeStyle = ctx.fillStyle; ctx.stroke();
  }
  // 宝箱
  for (const ch of m.chests) drawChest(ctx, SX(ch.x + 0.5), SY(ch.y + 0.55), ts, flags['chest_' + ch.id]);
  // 人物（按行）
  const actors = [...m.npcs.map(n => ({ y: n.ry, draw: () => drawUnit(ctx, SX(n.rx + 0.5), SY(n.ry + 0.92), ts, n.look, n.dir, n.moving ? n.walk : time + n.home[0], { moving: n.moving }) }))];
  if (m.boss && !flags[BOSSES[m.boss.id].flag]) {
    const e = BOSSES[m.boss.id].enemies[0];
    const lk = BOSS_LOOK[e] || { body: '#c9952e', trim: '#fff2b0', hat: 'scarf' };
    actors.push({ y: m.boss.y, draw: () => drawUnit(ctx, SX(m.boss.x + 0.5), SY(m.boss.y + 0.95), ts, lk, 0, time, { scale: 1.25, aura: 'rgba(255,80,60,.35)', banner: { color: ENEMIES[e].color, glyph: ENEMIES[e].glyph }, time }) });
  }
  actors.push({ y: player.ry, draw: () => drawPlayer(ctx, SX(player.rx + 0.5), SY(player.ry + 0.92), ts, player, time) });
  actors.sort((a, b) => a.y - b.y).forEach(a => a.draw());

  // 光照
  if (m.dark > 0) {
    if (!darkCanvas) darkCanvas = document.createElement('canvas');
    if (darkCanvas.width !== W || darkCanvas.height !== H) { darkCanvas.width = W; darkCanvas.height = H; }
    const d = darkCanvas.getContext('2d');
    d.globalCompositeOperation = 'source-over';
    d.clearRect(0, 0, W, H);
    d.fillStyle = `rgba(6,5,14,${m.dark})`; d.fillRect(0, 0, W, H);
    d.globalCompositeOperation = 'destination-out';
    const hole = (x, y, r, a) => { const g = d.createRadialGradient(x, y, 0, x, y, r); g.addColorStop(0, `rgba(0,0,0,${a})`); g.addColorStop(0.55, `rgba(0,0,0,${a * 0.7})`); g.addColorStop(1, 'rgba(0,0,0,0)'); d.fillStyle = g; d.fillRect(x - r, y - r, r * 2, r * 2); };
    hole(SX(player.rx + 0.5), SY(player.ry + 0.5), ts * 4.2, 1);
    for (const tc of m.torches) {
      const fl = 1 + Math.sin(time * 9 + tc.x * 3) * 0.05 + Math.sin(time * 13 + tc.y) * 0.04;
      hole(SX(tc.x + 0.5), SY(tc.y + 1.1), ts * 2.6 * fl, 0.9);
    }
    if (m.boss) hole(SX(m.boss.x + 0.5), SY(m.boss.y + 1.5), ts * 3, 0.8);
    ctx.drawImage(darkCanvas, 0, 0);
  }
  // 火把火焰
  for (const tc of m.torches) {
    const x = SX(tc.x + 0.5), y = SY(tc.y + 1.12);
    ctx.fillStyle = '#3a2a1a'; ctx.fillRect(x - ts * 0.04, y - ts * 0.02, ts * 0.08, ts * 0.22);
    const fl = Math.sin(time * 11 + tc.x) * 0.03;
    ctx.globalCompositeOperation = 'lighter';
    const g = ctx.createRadialGradient(x, y - ts * 0.06, 0, x, y - ts * 0.06, ts * 0.9);
    g.addColorStop(0, 'rgba(255,170,60,.45)'); g.addColorStop(1, 'rgba(255,120,30,0)');
    ctx.fillStyle = g; ctx.fillRect(x - ts, y - ts, ts * 2, ts * 2);
    ctx.globalCompositeOperation = 'source-over';
    ctx.fillStyle = '#ffb648';
    ctx.beginPath(); ctx.moveTo(x - ts * 0.07, y); ctx.quadraticCurveTo(x - ts * 0.06, y - ts * 0.16, x + fl * ts, y - ts * (0.26 + fl)); ctx.quadraticCurveTo(x + ts * 0.07, y - ts * 0.12, x + ts * 0.07, y); ctx.fill();
    ctx.fillStyle = '#fff2b0'; ellipse(ctx, x, y - ts * 0.05, ts * 0.03, ts * 0.06, '#fff2b0');
  }
}

const BOSS_LOOK = {
  chengyuanzhi: { body: '#c9952e', trim: '#fff2b0', hat: 'scarf', beard: true },
  zhangbao: { body: '#d7a83a', trim: '#5a3a10', hat: 'scarf', cape: '#8a5a10' },
  zhangliang: { body: '#c98a2e', trim: '#fff2b0', hat: 'scarf', beard: true, cape: '#6a4a10' },
  zhangjiao: { body: '#f0d070', trim: '#8a3a10', hat: 'scarf', beard: true, cape: '#c8962e' },
  dongzhuo: { body: '#7a2030', trim: '#e8c04a', hat: 'imperial', beard: true, cape: '#3a1018' },
};
