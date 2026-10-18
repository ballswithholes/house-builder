'use strict';
/* ==========================================================================
   CITY-VIEW.md §6 reference prototype: layout templates of the 12 city families and the
   deterministic placement (lots for houses, tiles for fields, water side, gate, landmark).
   Pure logic; the implementation ports TEMPLATES / layout() to js/city-templates.js + js/city-layout.js.

   Plot: 160 m × 160 m (templates authored at 120 m and spread ×1.3, sizes unchanged), x east, z north, origin at the centre, ground at y = 0 (terrain relief is added later).
   A slot is [cx, cz, w, d, rot]: centre, width along x, depth along z, rotation in degrees (0 = front faces −z, south).
   Walls: { shape: 'square' | 'round' | 'oval' | 'hill' | 'moat' | 'wagon' | 'grid' | 'canal', rx, rz, gates: ['S', …] }.
   The whole template is rotated in 90° steps so that its WATER side faces the real water (map terrainInfo);
   the gate side then follows (it is never the water side unless the city is a port whose gate is the harbour).

   node design-v4/domestic/city-templates.js     → per family: slot overlap check, lots / tiles counts vs the stage maxima,
                                                   and an ASCII plan of each template (for the doc)
   ========================================================================== */
const PLOT = 80;                 // half size (m): the plot is 160 m × 160 m
const SPREAD = 1.3;              // template coordinates are authored on a 120 m plot and spread ×1.3 (building sizes unchanged)
const LOT = 5;                   // house lot pitch (4 m house + 1 m lane)
const TILE = 8;                  // field tile pitch
const NEED = { lots: 46, tiles: 40 };   // CITY-VIEW §9 desktop maxima (houses S5, farms S5)

// Inside-wall slots are listed first, outside slots after `out:`; `res` = rectangles where house lots may go.
const TEMPLATES = {
  // 华夏: square rammed-earth / brick walls, south gate, axial main street to the 太守府 / 宫城 in the north
  hua: {
    walls: { shape: 'square', rx: 34, rz: 34, gates: ['S'] }, street: [[0, -34], [0, 8]], streetW: 6,
    slots: { palace: [0, 18, 24, 16, 0], treasury: [-15, 27, 8, 6, 0], temple: [-26, 18, 8, 10, 90], academy: [24, 20, 10, 10, 0],
      market: [18, 0, 18, 14, 0], shop: [24, -11, 8, 6, 0], granary: [-20, 0, 14, 10, 0], workshop: [-20, -22, 12, 10, 0],
      barracks: [20, -22, 14, 10, 0], tavern: [-7, -26, 6, 6, 90] },
    out: { drill: [40, -46, 26, 16, 0], stables: [-42, -46, 18, 10, 0], landmark: [-50, 50, 16, 16, 0], cottage: [48, 50, 8, 8, 200] },
    res: [[-31, 8, -9, 14], [9, 8, 31, 12], [-31, -14, -8, -6], [8, -14, 31, -6], [-5, 10, 5, 26], [-31, 25, -21, 31], [9, 26, 31, 31], [-31, -32, -12, -29]],
    water: 'N', fieldRing: [38, 58],
  },
  // 三韩·高句丽: stone hill-fort — the wall follows a ring on a hill, palace on the summit (north), terraced fields below
  han3: {
    walls: { shape: 'hill', rx: 33, rz: 30, gates: ['S', 'E'] }, street: [[0, -30], [0, 4]], streetW: 5,
    slots: { palace: [0, 16, 20, 12, 0], treasury: [-14, 22, 6, 6, 0], temple: [16, 18, 10, 8, 0], academy: [-20, 10, 8, 8, 90],
      market: [-16, -6, 14, 10, 0], shop: [-24, -16, 6, 6, 0], granary: [18, -2, 12, 8, 0], workshop: [20, -16, 10, 8, 0],
      barracks: [8, -20, 12, 8, 0], tavern: [-7, -20, 6, 6, 90] },
    out: { drill: [-40, -44, 22, 14, 0], stables: [42, -42, 14, 10, 0], landmark: [50, 50, 16, 16, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-30, 0, -8, 6], [6, 6, 30, 10], [-28, -27, -10, -12], [5, -28, 28, -26], [-6, 8, 6, 24], [24, -12, 30, 6]],
    water: 'S', fieldRing: [36, 58],
  },
  // 倭: double moat (环壕) and palisade, watchtowers, inner enclosure with the ritual hall; raised storehouses (邸阁) by the market
  wa: {
    walls: { shape: 'moat', rx: 36, rz: 30, gates: ['S', 'W'] }, street: [[0, -30], [0, 6]], streetW: 5,
    slots: { palace: [0, 16, 18, 12, 0], treasury: [-14, 20, 6, 6, 0], temple: [14, 20, 8, 8, 0], academy: [22, 8, 6, 6, 0],
      market: [-18, -6, 14, 10, 0], shop: [-26, -16, 6, 6, 0], granary: [-20, 8, 12, 8, 0], workshop: [20, -14, 10, 8, 0],
      barracks: [12, -22, 10, 8, 0], tavern: [-8, -20, 6, 6, 90] },
    out: { drill: [42, -44, 20, 14, 0], stables: [-44, -44, 16, 10, 0], landmark: [-50, 50, 14, 14, 0], cottage: [48, 48, 8, 8, 200] },
    res: [[-32, -4, -26, 4], [5, 6, 30, 12], [-30, -24, -12, -12], [28, -10, 34, 6], [-6, 6, 6, 22], [4, -28, 30, -24]],
    water: 'S', fieldRing: [40, 58],
  },
  // 草原: no walls — a wagon ring (车阵); the khan's golden tent in the centre, gers in rings, corrals and pasture outside
  steppe: {
    walls: { shape: 'wagon', rx: 30, rz: 30, gates: ['S', 'N'] }, street: [[0, -30], [0, -8]], streetW: 6,
    slots: { palace: [0, 0, 16, 16, 0], treasury: [10, 10, 6, 6, 0], temple: [0, 20, 8, 8, 0], academy: [-12, 12, 6, 6, 0],
      market: [16, -14, 12, 10, 0], shop: [24, -4, 6, 6, 0], granary: [-18, -6, 10, 8, 0], workshop: [-16, -18, 8, 8, 0],
      barracks: [18, 8, 10, 8, 0], tavern: [-8, -22, 6, 6, 90] },
    out: { drill: [40, 40, 24, 16, 0], stables: [-40, 40, 24, 16, 0], landmark: [50, -50, 14, 14, 0], cottage: [-48, -48, 8, 8, 30] },
    res: [[-26, -2, -10, 20], [6, 14, 26, 26], [-26, -26, -12, -12], [4, -28, 14, -20], [-6, 10, -2, 26], [24, 14, 28, 20]],
    water: 'E', fieldRing: [34, 58],
  },
  // 西域绿洲: rammed-earth rectangle with corner towers, the citadel in a corner, poplar-lined canals, vineyards and orchards outside
  oasis: {
    walls: { shape: 'square', rx: 32, rz: 30, gates: ['S', 'E'] }, street: [[0, -30], [0, 0], [32, 0]], streetW: 5,
    slots: { palace: [-18, 18, 18, 14, 0], treasury: [-26, 6, 6, 6, 0], temple: [16, 18, 10, 10, 0], academy: [4, 22, 8, 8, 0],
      market: [16, -8, 16, 10, 0], shop: [26, -18, 6, 6, 0], granary: [-18, -6, 12, 8, 0], workshop: [-18, -20, 10, 8, 0],
      barracks: [12, -22, 12, 8, 0], tavern: [-6, -22, 6, 6, 90] },
    out: { drill: [42, -44, 22, 14, 0], stables: [-42, -44, 16, 10, 0], landmark: [50, 50, 14, 14, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-6, 6, 10, 14], [20, 4, 30, 10], [-29, -28, -24, -12], [-4, -28, 6, -6], [-29, 26, -10, 28], [4, 4, 10, 14]],
    water: 'W', fieldRing: [36, 58],
  },
  // 天竺 (贵霜 / 南亚): Sirkap-style grid with a main avenue, brick wall and moat, torana gates, a stupa court and a tank
  indic: {
    walls: { shape: 'grid', rx: 34, rz: 32, gates: ['S', 'N'] }, street: [[0, -32], [0, 32]], streetW: 7,
    slots: { palace: [-18, 18, 20, 14, 0], treasury: [-26, 6, 6, 6, 0], temple: [18, 18, 14, 14, 0], academy: [18, 4, 10, 8, 0],
      market: [-16, -4, 16, 10, 0], shop: [-24, -14, 6, 6, 0], granary: [18, -8, 12, 8, 0], workshop: [-18, -22, 12, 8, 0],
      barracks: [18, -22, 12, 8, 0], tavern: [-8, -14, 6, 6, 90] },
    out: { drill: [42, -46, 22, 14, 0], stables: [-42, -46, 18, 12, 0], landmark: [50, 50, 16, 16, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-31, 26, -6, 30], [5, 26, 31, 30], [5, -16, 12, 12], [-31, -30, -5, -27], [5, -30, 31, -28], [-31, 4, -5, 10]],
    water: 'E', fieldRing: [38, 58],
  },
  // 安息: round mud-brick city (Hatra / Darabgerd type) — palace and iwan court in the centre, fire temple beside it, bazaar ring
  persia: {
    walls: { shape: 'round', rx: 34, rz: 34, gates: ['S', 'E', 'W'] }, street: [[0, -34], [0, -8]], streetW: 6,
    slots: { palace: [0, 4, 18, 16, 0], treasury: [-12, 14, 6, 6, 0], temple: [14, 14, 10, 10, 0], academy: [0, 22, 10, 6, 0],
      market: [-18, -10, 14, 10, 30], shop: [-26, -2, 6, 6, 0], granary: [20, -8, 12, 8, -30], workshop: [-14, -24, 10, 8, 20],
      barracks: [16, -22, 12, 8, -20], tavern: [-6, -18, 6, 6, 90] },
    out: { drill: [42, -44, 22, 14, 0], stables: [-42, -44, 18, 12, 0], landmark: [50, 50, 14, 14, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-28, 4, -10, 20], [10, 22, 24, 26], [6, -10, 10, 2], [-24, -30, -6, -28], [-2, -30, 8, -24], [22, 4, 30, 6], [-8, 22, -4, 28]],
    water: 'S', fieldRing: [38, 58],
  },
  // 阿拉伯: colonnaded main street (Palmyra) from gate to the great temple, agora and caravan court, cisterns; date palms outside
  arabia: {
    walls: { shape: 'oval', rx: 36, rz: 30, gates: ['S', 'W', 'E'] }, street: [[0, -30], [0, 22]], streetW: 7,
    slots: { palace: [-18, 14, 16, 12, 0], treasury: [-26, 2, 6, 6, 0], temple: [12, 20, 14, 12, 0], academy: [-6, 22, 6, 6, 0],
      market: [18, 2, 14, 12, 0], shop: [26, -10, 6, 6, 0], granary: [-18, -4, 12, 8, 0], workshop: [-16, -18, 10, 8, 0],
      barracks: [16, -18, 12, 8, 0], tavern: [-8, -24, 6, 6, 90] },
    out: { drill: [42, -44, 22, 14, 0], stables: [-42, -44, 18, 12, 0], landmark: [50, 50, 14, 14, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-32, -12, -24, 8], [5, -10, 10, 14], [-6, -28, -2, 12], [22, -28, 30, -24], [28, -4, 34, 10], [-26, 22, -12, 26]],
    water: 'W', fieldRing: [40, 58],
  },
  // 库施·阿克苏姆: temple axis along the river (east–west), palace on a stepped podium, iron furnaces with slag heaps,
  // hafir reservoir; landmark = pyramid field (Meroë) or stele field (Aksum)
  nile: {
    walls: { shape: 'square', rx: 32, rz: 28, gates: ['W', 'S'] }, street: [[-32, 0], [8, 0]], streetW: 6,
    slots: { palace: [-14, 16, 18, 12, 0], treasury: [-26, 18, 6, 6, 0], temple: [20, 0, 14, 18, 90], academy: [4, 18, 8, 8, 0],
      market: [-14, -12, 14, 10, 0], shop: [-24, -20, 6, 6, 0], granary: [4, -16, 10, 8, 0], workshop: [18, -18, 12, 8, 0],
      barracks: [8, 22, 8, 6, 0], tavern: [-4, -8, 6, 6, 90] },
    out: { drill: [42, -44, 22, 14, 0], stables: [-42, -44, 18, 12, 0], landmark: [50, 50, 16, 16, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-30, 4, -6, 9], [-2, 4, 12, 12], [-30, -26, -6, -20], [-4, -26, 10, -22], [10, -10, 14, 10], [14, 22, 30, 26]],
    water: 'N', fieldRing: [36, 58],
  },
  // 罗马: castrum grid — cardo and decumanus cross at the forum (basilica + curia = palace), Capitolium, horrea, macellum,
  // insulae; theatre / amphitheatre landmark outside, aqueduct arriving from the hills
  roman: {
    walls: { shape: 'square', rx: 34, rz: 34, gates: ['S', 'N', 'E', 'W'] }, street: [[0, -34], [0, 34]], street2: [[-34, 0], [34, 0]], streetW: 6,
    slots: { palace: [-14, 12, 18, 14, 0], treasury: [-26, 22, 6, 6, 0], temple: [14, 14, 12, 14, 0], academy: [24, 26, 8, 6, 0],
      market: [14, -12, 14, 12, 0], shop: [26, -6, 6, 6, 0], granary: [-16, -12, 14, 10, 0], workshop: [-24, -26, 10, 8, 0],
      barracks: [20, -26, 12, 8, 0], tavern: [-8, -26, 6, 6, 90] },
    out: { drill: [42, -46, 24, 14, 0], stables: [-42, -46, 18, 12, 0], landmark: [50, 50, 16, 16, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-31, 22, -18, 31], [-12, 21, -4, 31], [4, 23, 18, 31], [26, 4, 31, 18], [-31, 4, -25, 18], [-31, -20, -26, -6], [4, -20, 7, -5], [-14, -31, -12, -24], [9, -31, 13, -22]],
    water: 'S', fieldRing: [38, 58],
  },
  // 北方 (凯尔特 / 日耳曼): oval hill-fort — earth rampart and palisade, chief's hall on the crest, roundhouses / longhouses,
  // four-post granaries, small square fields, sacred grove
  north: {
    walls: { shape: 'hill', rx: 34, rz: 28, gates: ['S', 'E'] }, street: [[0, -28], [0, 4]], streetW: 5,
    slots: { palace: [0, 14, 18, 10, 0], treasury: [-12, 20, 6, 6, 0], temple: [20, 14, 10, 10, 0], academy: [-22, 10, 6, 6, 0],
      market: [-14, -6, 14, 10, 0], shop: [-24, -14, 6, 6, 0], granary: [16, -2, 10, 8, 0], workshop: [18, -14, 10, 8, 0],
      barracks: [6, -20, 10, 8, 0], tavern: [-8, -18, 6, 6, 90] },
    out: { drill: [42, -44, 22, 14, 0], stables: [-42, -44, 18, 12, 0], landmark: [50, 50, 14, 14, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-30, 0, -8, 6], [4, 4, 30, 8], [-28, -24, -12, -12], [10, -26, 28, -20], [-6, 6, 6, 20], [26, -12, 32, 4]],
    water: 'W', fieldRing: [36, 58],
  },
  // 南海 (林邑·扶南 / 夷洲): canal grid (Óc Eo) — stilt houses along canals, temple on a brick platform, market quay,
  // bead and bronze workshops, elephant kraal
  nanhai: {
    walls: { shape: 'canal', rx: 34, rz: 32, gates: ['S', 'E'] }, street: [[0, -32], [0, 8]], streetW: 6,
    slots: { palace: [-16, 18, 18, 12, 0], treasury: [-26, 6, 6, 6, 0], temple: [14, 18, 14, 14, 0], academy: [0, 24, 6, 6, 0],
      market: [16, -4, 14, 10, 0], shop: [26, -14, 6, 6, 0], granary: [-18, -2, 12, 8, 0], workshop: [-18, -18, 10, 8, 0],
      barracks: [14, -22, 12, 8, 0], tavern: [-8, -22, 6, 6, 90] },
    out: { drill: [42, -44, 22, 14, 0], stables: [-42, -44, 20, 14, 0], landmark: [50, 50, 14, 14, 0], cottage: [-48, 48, 8, 8, 160] },
    res: [[-31, 4, -8, 10], [6, 4, 31, 8], [-30, -30, -12, -24], [4, -30, 30, -28], [-6, 10, 6, 20], [24, -10, 31, 2], [-31, 26, -24, 30]],
    water: 'S', fieldRing: [38, 58],
  },
};

// ------------------------------------------------------------------ geometry --
const rectOf = s => ({ x0: s[0] - s[2] / 2, x1: s[0] + s[2] / 2, z0: s[1] - s[3] / 2, z1: s[1] + s[3] / 2 });
function rotRect(r, deg) {   // axis-aligned bound of a rotated rect
  if (!deg || deg % 180 === 0) return r;
  const cx = (r.x0 + r.x1) / 2, cz = (r.z0 + r.z1) / 2, w = r.x1 - r.x0, d = r.z1 - r.z0;
  if (deg % 90 === 0) return { x0: cx - d / 2, x1: cx + d / 2, z0: cz - w / 2, z1: cz + w / 2 };
  const a = Math.abs(deg) * Math.PI / 180, W = Math.abs(w * Math.cos(a)) + Math.abs(d * Math.sin(a)), D = Math.abs(w * Math.sin(a)) + Math.abs(d * Math.cos(a));
  return { x0: cx - W / 2, x1: cx + W / 2, z0: cz - D / 2, z1: cz + D / 2 };
}
const hit = (a, b, pad) => a.x0 < b.x1 + pad && a.x1 > b.x0 - pad && a.z0 < b.z1 + pad && a.z1 > b.z0 - pad;
function insideWalls(walls, x, z, margin) {
  const rx = walls.rx - margin, rz = walls.rz - margin;
  if (walls.shape === 'square' || walls.shape === 'grid' || walls.shape === 'canal') return Math.abs(x) <= rx && Math.abs(z) <= rz;
  return (x * x) / (rx * rx) + (z * z) / (rz * rz) <= 1;
}
function streetRects(T) {
  const out = [];
  for (const line of [T.street, T.street2]) {
    if (!line) continue;
    for (let i = 0; i + 1 < line.length; i++) {
      const [ax, az] = line[i], [bx, bz] = line[i + 1], h = T.streetW / 2;
      out.push({ x0: Math.min(ax, bx) - h, x1: Math.max(ax, bx) + h, z0: Math.min(az, bz) - h, z1: Math.max(az, bz) + h });
    }
  }
  return out;
}

// house lots: LOT grid over all free space inside the walls (not on placed slots / streets, 3 m from the wall);
// ordered by preference: inside a `res` rect first, then by distance to the main street (so the town fills from its spine)
function lots(T, placed) {
  const blocked = (placed || Object.values(T.slots).map(s => rotRect(rectOf(s), s[4]))).concat(streetRects(T));
  const out = [];
  const R = Math.max(T.walls.rx, T.walls.rz);
  for (let x = -R + LOT / 2; x <= R - LOT / 2 + 1e-6; x += LOT) {
    for (let z = -R + LOT / 2; z <= R - LOT / 2 + 1e-6; z += LOT) {
      const r = { x0: x - 2, x1: x + 2, z0: z - 2, z1: z + 2 };
      if (![[r.x0, r.z0], [r.x1, r.z0], [r.x0, r.z1], [r.x1, r.z1]].every(([cx, cz]) => insideWalls(T.walls, cx, cz, 3))) continue;
      if (blocked.some(bb => hit(r, bb, 0.5))) continue;
      out.push([x, z]);
    }
  }
  const inRes = p => (T.res || []).some(([x0, z0, x1, z1]) => p[0] >= x0 && p[0] <= x1 && p[1] >= z0 && p[1] <= z1);
  const st = T.street[0];
  const key = p => (inRes(p) ? 0 : 1000) + Math.abs(p[0] - st[0]) + Math.abs(p[1]) * 0.6;
  out.sort((a, b) => key(a) - key(b));
  return out;
}
// field tiles: TILE grid in the ring between fieldRing[0] and fieldRing[1] (chebyshev distance), not on outside slots,
// not on the gate road corridor; ordered from the water side (irrigation spreads from the river)
function tiles(T) {
  const blocked = Object.values(T.out).map(s => rotRect(rectOf(s), s[4]));
  const [r0, r1] = T.fieldRing;
  const gateS = T.walls.gates;
  const out = [];
  for (let x = -PLOT + TILE / 2; x <= PLOT - TILE / 2; x += TILE) {
    for (let z = -PLOT + TILE / 2; z <= PLOT - TILE / 2; z += TILE) {
      const cheb = Math.max(Math.abs(x), Math.abs(z));
      if (cheb < r0 || cheb > r1) continue;
      const r = { x0: x - TILE / 2 + 0.5, x1: x + TILE / 2 - 0.5, z0: z - TILE / 2 + 0.5, z1: z + TILE / 2 - 0.5 };
      if (blocked.some(b => hit(r, b, 0))) continue;
      // gate roads (5 m corridor)
      if (gateS.includes('S') && z < 0 && Math.abs(x) < 6) continue;
      if (gateS.includes('N') && z > 0 && Math.abs(x) < 6) continue;
      if (gateS.includes('E') && x > 0 && Math.abs(z) < 6) continue;
      if (gateS.includes('W') && x < 0 && Math.abs(z) < 6) continue;
      out.push([x, z]);
    }
  }
  const w = { N: [0, 1], S: [0, -1], E: [1, 0], W: [-1, 0] }[T.water] || [0, 1];
  out.sort((a, b) => (b[0] * w[0] + b[1] * w[1]) - (a[0] * w[0] + a[1] * w[1]));
  return out;
}

// spread a template authored on the 120 m plot: centres, walls, streets, residential prefs and the field ring ×SPREAD
function spread(T0) {
  if (T0._spread) return T0;
  const k = SPREAD, pt = p => [p[0] * k, p[1] * k];
  const sl = o => Object.fromEntries(Object.entries(o).map(([id, s]) => [id, [s[0] * k, s[1] * k, s[2], s[3], s[4]]]));
  return Object.assign({}, T0, {
    _spread: true, walls: Object.assign({}, T0.walls, { rx: T0.walls.rx * k, rz: T0.walls.rz * k }),
    street: T0.street.map(pt), street2: T0.street2 ? T0.street2.map(pt) : undefined,
    slots: sl(T0.slots), out: sl(T0.out), res: (T0.res || []).map(r => r.map(v => v * k)), fieldRing: T0.fieldRing.map(v => v * k),
  });
}

// resolve(T): place every slot at its preferred centre, or the nearest free spot on a 1 m spiral (≤ 24 m away):
// inside slots stay inside the walls (2 m margin) and off the streets, outside slots stay outside the walls and in the plot;
// 1.5 m clearance between buildings. Deterministic; the doc's templates give only preferred centres and sizes.
function resolve(T) {
  T = spread(T);
  const placed = [], res = { slots: {}, out: {} };
  const streets = streetRects(T);
  const ok = (r, where) => {
    if (r.x0 < -PLOT + 1 || r.x1 > PLOT - 1 || r.z0 < -PLOT + 1 || r.z1 > PLOT - 1) return false;
    const corners = [[r.x0, r.z0], [r.x1, r.z0], [r.x0, r.z1], [r.x1, r.z1]];
    if (where === 'in') { if (!corners.every(([x, z]) => insideWalls(T.walls, x, z, 2))) return false; if (streets.some(st => hit(r, st, 0.5))) return false; }
    else { if (corners.some(([x, z]) => insideWalls(T.walls, x, z, -6))) return false; }
    return !placed.some(q => hit(r, q, 1.5));
  };
  for (const where of ['in', 'out']) {
    const src = where === 'in' ? T.slots : T.out;
    for (const [id, s] of Object.entries(src)) {
      let best = null;
      for (let rad = 0; rad <= 24 && !best; rad++) {
        const steps = rad === 0 ? 1 : 8 * rad;
        for (let k = 0; k < steps && !best; k++) {
          const t = (k / steps) * Math.PI * 2;
          const c = [s[0] + Math.round(Math.cos(t) * rad), s[1] + Math.round(Math.sin(t) * rad), s[2], s[3], s[4]];
          const r = rotRect(rectOf(c), c[4]);
          if (ok(r, where)) best = c;
        }
      }
      if (!best) { best = s; res.unplaced = (res.unplaced || []).concat(id); }
      res[where === 'in' ? 'slots' : 'out'][id] = best;
      placed.push(rotRect(rectOf(best), best[4]));
    }
  }
  res.placedRects = placed;
  return res;
}

// rotate a template so that its water side faces `waterSide` ('N' | 'E' | 'S' | 'W' | null)
const SIDES = ['N', 'E', 'S', 'W'];
function turns(from, to) { if (!to) return 0; return (SIDES.indexOf(to) - SIDES.indexOf(from) + 4) % 4; }
function rotPoint([x, z], q) { for (let i = 0; i < q; i++) [x, z] = [z, -x]; return [x, z]; }   // 90° clockwise seen from above

// layout(family, { key, waterSide, coastal, river, capital, landmark }) → placed buildings (all stages; stage chooses the look)
function hash(s) { let h = 2166136261 >>> 0; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h; }
function layout(family, o) {
  const T0 = spread(TEMPLATES[family] || TEMPLATES.hua);
  const Rz = resolve(T0);
  const T = Object.assign({}, T0, { slots: Rz.slots, out: Rz.out });
  const q = turns(T.water, o.waterSide);
  const mirror = (hash(o.key || '') & 1) === 1 && !o.waterSide;     // variety for inland cities: mirror east ↔ west
  const tr = p => { let [x, z] = rotPoint(p, q); if (mirror) x = -x; return [x, z]; };
  const place = (id, s) => { const [x, z] = tr([s[0], s[1]]); return { id, x, z, w: q % 2 ? s[3] : s[2], d: q % 2 ? s[2] : s[3], rot: (s[4] + 90 * q) % 360 }; };
  const out = { family, turns: q, mirror, walls: Object.assign({}, T.walls, { gates: T.walls.gates.map(g => SIDES[(SIDES.indexOf(g) + q) % 4]) }), buildings: [] };
  for (const [id, s] of Object.entries(T.slots)) out.buildings.push(place(id, s));
  for (const [id, s] of Object.entries(T.out)) if (id !== 'landmark' || o.landmark) if (id !== 'cottage') out.buildings.push(place(id, s));
  out.cottage = place('cottage', T.out.cottage);   // spawned only by the 搜索 vignette
  if (o.coastal || o.river) {
    const [hx, hz] = tr([0, T.water === 'N' ? PLOT - 6 : T.water === 'S' ? -(PLOT - 6) : 0].map((v, i) => (T.water === 'E' && i === 0 ? PLOT - 6 : T.water === 'W' && i === 0 ? -(PLOT - 6) : v)));
    out.buildings.push({ id: 'harbour', x: hx, z: hz, w: (q + (T.water === 'E' || T.water === 'W' ? 1 : 0)) % 2 ? 10 : 30, d: (q + (T.water === 'E' || T.water === 'W' ? 1 : 0)) % 2 ? 30 : 10, rot: 0 });
  }
  out.lots = lots(T, Rz.placedRects).map(tr);
  out.tiles = tiles(T).map(tr);
  return out;
}

// ------------------------------------------------------------------ check --
function check() {
  const fails = [];
  const rows = [];
  for (const [fam, Traw] of Object.entries(TEMPLATES)) {
    const T0 = spread(Traw);
    const Rz = resolve(T0);
    if (Rz.unplaced) fails.push(`${fam}: no room for ${Rz.unplaced.join(', ')}`);
    const T = Object.assign({}, T0, { slots: Rz.slots, out: Rz.out });
    const moved = Object.entries(Object.assign({}, T0.slots, T0.out)).filter(([id, s]) => { const r = Rz.slots[id] || Rz.out[id]; return r && (r[0] !== s[0] || r[1] !== s[1]); }).map(([id]) => id);
    const all = Object.entries(T.slots).map(([id, s]) => [id, rotRect(rectOf(s), s[4]), 'in']).concat(Object.entries(T.out).map(([id, s]) => [id, rotRect(rectOf(s), s[4]), 'out']));
    // overlaps (1 m clearance) and bounds
    for (let i = 0; i < all.length; i++) {
      const [id, r, where] = all[i];
      if (r.x0 < -PLOT || r.x1 > PLOT || r.z0 < -PLOT || r.z1 > PLOT) fails.push(`${fam}.${id} outside the plot`);
      if (where === 'in') for (const [cx, cz] of [[r.x0, r.z0], [r.x1, r.z0], [r.x0, r.z1], [r.x1, r.z1]]) if (!insideWalls(T.walls, cx, cz, 1)) { fails.push(`${fam}.${id} crosses the walls`); break; }
      if (where === 'out' && insideWalls(T.walls, (r.x0 + r.x1) / 2, (r.z0 + r.z1) / 2, -4)) fails.push(`${fam}.${id} (outside slot) is inside the walls`);
      for (let j = i + 1; j < all.length; j++) if (hit(r, all[j][1], 1)) fails.push(`${fam}: ${id} overlaps ${all[j][0]}`);
      for (const s of streetRects(T)) if (where === 'in' && hit(r, s, 0.5)) fails.push(`${fam}.${id} blocks the main street`);
    }
    const L = lots(T, Rz.placedRects), F = tiles(T);
    if (L.length < NEED.lots) fails.push(`${fam}: ${L.length} house lots < ${NEED.lots}`);
    if (F.length < NEED.tiles) fails.push(`${fam}: ${F.length} field tiles < ${NEED.tiles}`);
    // a rotated, coastal layout must keep everything in the plot
    for (const ws of SIDES) {
      const lay = layout(fam, { key: 'x', waterSide: ws, coastal: true, landmark: true });
      for (const b of lay.buildings) if (Math.abs(b.x) + b.w / 2 > PLOT + 1e-6 || Math.abs(b.z) + b.d / 2 > PLOT + 1e-6) fails.push(`${fam} turned to ${ws}: ${b.id} outside`);
    }
    rows.push([fam, L.length, F.length, T.walls.shape, T.walls.gates.join(''), moved.join(' ')]);
  }
  console.log('family   lots  tiles  walls   gates  nudged by resolve()');
  for (const r of rows) console.log(r[0].padEnd(8) + String(r[1]).padStart(5) + String(r[2]).padStart(7) + '  ' + r[3].padEnd(7) + ' ' + r[4].padEnd(6) + ' ' + r[5]);
  if (process.argv.includes('--plan')) for (const fam of Object.keys(TEMPLATES)) console.log('\n' + fam + '\n' + plan(fam));
  console.log(fails.length ? 'FAIL\n  ' + fails.join('\n  ') : 'ALL OK');
  process.exitCode = fails.length ? 1 : 0;
}
// ASCII plan, 2 m per character
function plan(fam) {
  const lay = layout(fam, { key: 'plan', waterSide: null, coastal: true, landmark: true });
  const N = 80, g = Array.from({ length: N }, () => Array(N).fill(' '));
  const put = (x, z, ch) => { const i = Math.floor((x + PLOT) / 2), j = Math.floor((PLOT - z) / 2); if (i >= 0 && i < N && j >= 0 && j < N) g[j][i] = ch; };
  for (const [x, z] of lay.tiles) for (let dx = -3; dx <= 3; dx += 2) for (let dz = -3; dz <= 3; dz += 2) put(x + dx, z + dz, '.');
  const T = spread(TEMPLATES[fam]);
  for (const r of streetRects(T)) for (let x = r.x0 + 1; x < r.x1; x += 2) for (let z = r.z0 + 1; z < r.z1; z += 2) put(x, z, ':');
  for (let a = 0; a < 360; a += 1) {
    const t = a * Math.PI / 180;
    if (T.walls.shape === 'square' || T.walls.shape === 'grid' || T.walls.shape === 'canal') {
      const s = Math.max(Math.abs(Math.cos(t)) / T.walls.rx, Math.abs(Math.sin(t)) / T.walls.rz);
      put(Math.cos(t) / s, Math.sin(t) / s, '#');
    } else put(Math.cos(t) * T.walls.rx, Math.sin(t) * T.walls.rz, T.walls.shape === 'wagon' ? 'o' : T.walls.shape === 'moat' ? '~' : '#');
  }
  for (const [x, z] of lay.lots) put(x, z, 'h');
  const CH = { palace: 'P', treasury: '$', temple: 'T', academy: 'A', market: 'M', shop: 'S', granary: 'G', workshop: 'W', barracks: 'B', tavern: 'V', drill: 'D', stables: 'H', landmark: 'L', harbour: 'R' };
  for (const b of lay.buildings) for (let x = b.x - b.w / 2 + 1; x < b.x + b.w / 2; x += 2) for (let z = b.z - b.d / 2 + 1; z < b.z + b.d / 2; z += 2) put(x, z, CH[b.id] || '?');
  return g.map(r => r.join('')).join('\n');
}

module.exports = { TEMPLATES, PLOT, LOT, TILE, layout, lots, tiles, plan, resolve };
if (require.main === module) check();
