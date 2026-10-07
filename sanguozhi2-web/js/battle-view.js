'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战场三维表现
   ← Battle/BattleView.cs（BattleView、UnitVisual、FloatUp）

   棋盘建在 Unity 原点 (400, 0, 0)，每格 T = 2。网格用 SG.MeshBuilder 以
   Unity 坐标构建（与 C# 数字一致），对外的世界坐标均为 three 坐标
   （SG.U(x, y, z) = (x, y, -z)）。
   动画（移动、冲锋、溃散、计策特效）为 async 函数，按帧推进，返回 Promise。
   粒子：自带的轻量系统（THREE.Points + 自定义着色器），在 update(dt) 中推进；
   火焰带点光源闪烁。
   第二版新增：specialFx(u, targets, fx, info) → Promise —— 必杀技特效（风格列表见文件末尾「必杀技特效」一节），
   UnitVisual.tint(color, k) 着色光，以及限时加成的光环与名牌小字。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const T = 2;
  const ORIGIN = { x: 400, y: 0, z: 0 };   // Unity 坐标
  const GRAVITY = 9.81;
  const SIDE_BAR = ['#73bfff', '#ff806b'];  // C#：攻方 (0.45,0.75,1)，守方 (1,0.5,0.42)
  const FIRE_LIGHT = 4;                       // 点光源强度（three，decay 1；C# intensity 3 / range 6 略降以免过曝）
  const FIRE_FLARE = 1.8;                     // 火计起火的第一秒：光源与地面火光加强的倍数
  const HEAT_TINT = new THREE.Color(1, 0.42, 0.1);   // 被火计灼烧的部队染成橙色
  const WHITE = new THREE.Color(1, 1, 1);

  function TR() {
    const t = SG.Terrain;
    return t || { Plain: 0, Forest: 1, Hill: 2, Mountain: 3, River: 4, Wall: 5, Gate: 6, Castle: 7 };
  }
  // Unity 坐标的向量（给 MeshBuilder 用）
  function V(x, y, z) { return new THREE.Vector3(x, y, z); }
  function C(r, g, b) { return new THREE.Color(r, g, b); }
  function shade(c, k) { return SG.Gfx.shade(c, k); }
  function lerpColor(a, b, t) {
    t = M.clamp01(t);
    return C(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
  }
  function sfx(name, vol) {
    try { if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频时忽略 */ }
  }
  function now() { return performance.now() / 1000; }
  function cssHex(c) { return SG.rgbToHex(c.r, c.g, c.b); }
  // 解析 css 颜色（含 rgba 的透明度）
  function parseColor(c) {
    if (c && c.isColor) return { color: c.clone(), a: 1 };
    if (typeof c === 'string') {
      const m = /^\s*rgba?\(([^)]+)\)\s*$/i.exec(c);
      if (m) {
        const p = m[1].split(/[\s,\/]+/).filter(s => s.length).map(parseFloat);
        return { color: C(p[0] / 255, p[1] / 255, p[2] / 255), a: p.length > 3 ? p[3] : 1 };
      }
      if (/^#[0-9a-f]{8}$/i.test(c)) return { color: new THREE.Color(c.slice(0, 7)), a: parseInt(c.slice(7, 9), 16) / 255 };
      return { color: new THREE.Color(c), a: 1 };
    }
    if (Array.isArray(c)) return { color: C(c[0], c[1], c[2]), a: c.length > 3 ? c[3] : 1 };
    if (c && typeof c.r === 'number') return { color: C(c.r, c.g, c.b), a: typeof c.a === 'number' ? c.a : 1 };
    return { color: C(1, 1, 1), a: 1 };
  }
  // ===================================================== 战场的文化外观 --
  // DESIGN-V2 §4「各文化的城池模型、战场城墙与士兵外观」：战场所在城池（setup.target，攻守双方都在此城下交战）
  // 的文化决定城墙、城门、本阵的样式与地表色调。汉地（han）及未知文化保持原样（中式门楼、金顶本阵、温带草地）。
  // 只改装饰几何与顶点色（每场战斗构建一次），不改地形、规则或随机数序列。
  const BattleLook = (function () {
    const STYLE = {
      roman: 'roman', persia: 'persia', arab: 'arab', tarim: 'tarim', kushan: 'kushan', korea: 'korea', wa: 'wa',
      seasia: 'stilt', yi: 'stilt', nanman: 'stilt', celt: 'celt', german: 'german', steppe: 'steppe', sarmatian: 'steppe',
    };
    const GROUND_OF = { persia: 'arid', arab: 'arid', tarim: 'arid', kushan: 'arid', steppe: 'steppe', sarmatian: 'steppe', seasia: 'tropic', yi: 'tropic', nanman: 'tropic' };
    let grounds = null;
    function ground(kind) {
      if (!grounds) grounds = {
        // temperate 与 v1 完全相同
        temperate: { plain: C(0.47, 0.68, 0.36), forest: C(0.33, 0.55, 0.3), hill: C(0.6, 0.62, 0.38), hillBlob: C(0.55, 0.6, 0.35),
          mountain: [C(0.55, 0.53, 0.5), C(0.5, 0.48, 0.45)], snow: true, leaf: C(0.25, 0.5, 0.27), tuft: C(0.34, 0.56, 0.25), flowers: 0.35,
          sur: [C(0.42, 0.6, 0.32), C(0.55, 0.55, 0.45)], surLeaf: C(0.24, 0.46, 0.26), under: C(0.17, 0.2, 0.12), palm: 0 },
        // 沙漠 / 绿洲：沙土地、赭色丘陵、椰枣林
        arid: { plain: C(0.8, 0.7, 0.5), forest: C(0.66, 0.62, 0.42), hill: C(0.76, 0.6, 0.42), hillBlob: C(0.72, 0.56, 0.38),
          mountain: [C(0.64, 0.52, 0.42), C(0.58, 0.47, 0.38)], snow: false, leaf: C(0.3, 0.5, 0.26), tuft: C(0.62, 0.58, 0.34), flowers: 0.06,
          sur: [C(0.78, 0.66, 0.47), C(0.68, 0.56, 0.42)], surLeaf: C(0.32, 0.48, 0.26), under: C(0.3, 0.24, 0.16), palm: 1 },
        // 草原：枯黄的草地
        steppe: { plain: C(0.62, 0.67, 0.4), forest: C(0.42, 0.55, 0.32), hill: C(0.66, 0.64, 0.42), hillBlob: C(0.6, 0.6, 0.38),
          mountain: [C(0.55, 0.53, 0.5), C(0.5, 0.48, 0.45)], snow: true, leaf: C(0.3, 0.48, 0.28), tuft: C(0.58, 0.58, 0.32), flowers: 0.2,
          sur: [C(0.58, 0.62, 0.38), C(0.6, 0.58, 0.46)], surLeaf: C(0.28, 0.44, 0.26), under: C(0.2, 0.2, 0.12), palm: 0 },
        // 南方湿热：深绿，林中夹椰树
        tropic: { plain: C(0.4, 0.66, 0.32), forest: C(0.27, 0.52, 0.26), hill: C(0.52, 0.62, 0.34), hillBlob: C(0.45, 0.6, 0.3),
          mountain: [C(0.5, 0.52, 0.46), C(0.46, 0.48, 0.42)], snow: false, leaf: C(0.2, 0.48, 0.22), tuft: C(0.3, 0.56, 0.22), flowers: 0.4,
          sur: [C(0.36, 0.58, 0.28), C(0.48, 0.55, 0.4)], surLeaf: C(0.2, 0.44, 0.22), under: C(0.15, 0.2, 0.1), palm: 0.4 },
      };
      return grounds[kind] || grounds.temperate;
    }
    function cultureOf(setup) {
      const city = setup && setup.target;
      try { if (SG.CultureArt && SG.CultureArt.cultureOfCity) return SG.CultureArt.cultureOfCity(city) || 'han'; } catch (e) { /* 无文化数据 */ }
      return (city && typeof city.culture === 'string' && city.culture) || 'han';
    }
    function of(setup) {
      const culture = cultureOf(setup);
      return { culture, style: STYLE[culture] || 'han', ground: ground(GROUND_OF[culture] || 'temperate') };
    }

    // ---- 基本形体（与 culture-art.js 同一约定：Unity 坐标，face 按外向 hint 定正反） --
    function face(mb, pts, c, hint) {
      const a = pts[0], b = pts[1], d = pts[2];
      const ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
      const nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
      if (nx * hint.x + ny * hint.y + nz * hint.z < 0) pts = pts.slice().reverse();
      for (let i = 1; i + 1 < pts.length; i++) mb.tri(pts[0], pts[i], pts[i + 1], c);
    }
    function sheet(mb, pts, c) {
      for (let i = 1; i + 1 < pts.length; i++) { mb.tri(pts[0], pts[i], pts[i + 1], c); mb.tri(pts[0], pts[i + 1], pts[i], c); }
    }
    // 双坡顶：中心 (x, y, z)，sx × sz 的底，脊高 h；alongZ = 脊沿 z（山墙朝南北，即朝镜头）
    function gable(mb, x, y, z, sx, sz, h, c, alongZ) {
      const hx = sx / 2, hz = sz / 2;
      if (alongZ) {
        const r0 = V(x, y + h, z - hz), r1 = V(x, y + h, z + hz);
        face(mb, [V(x - hx, y, z - hz), V(x - hx, y, z + hz), r1, r0], shade(c, -0.1), V(-1, 0.6, 0));
        face(mb, [V(x + hx, y, z - hz), r0, r1, V(x + hx, y, z + hz)], c, V(1, 0.6, 0));
        face(mb, [V(x - hx, y, z - hz), r0, V(x + hx, y, z - hz)], shade(c, -0.2), V(0, 0, -1));
        face(mb, [V(x - hx, y, z + hz), V(x + hx, y, z + hz), r1], shade(c, -0.2), V(0, 0, 1));
      } else {
        const r0 = V(x - hx, y + h, z), r1 = V(x + hx, y + h, z);
        face(mb, [V(x - hx, y, z - hz), V(x + hx, y, z - hz), r1, r0], c, V(0, 0.6, -1));
        face(mb, [V(x - hx, y, z + hz), r0, r1, V(x + hx, y, z + hz)], shade(c, -0.12), V(0, 0.6, 1));
        face(mb, [V(x - hx, y, z - hz), r0, V(x - hx, y, z + hz)], shade(c, -0.22), V(-1, 0, 0));
        face(mb, [V(x + hx, y, z - hz), V(x + hx, y, z + hz), r1], shade(c, -0.22), V(1, 0, 0));
      }
    }
    function dome(mb, x, y, z, r, c) {
      const rings = [[1, 0], [0.92, 0.38], [0.7, 0.72], [0.38, 0.93]];
      for (let i = 0; i < rings.length - 1; i++) {
        const [r0, y0] = rings[i], [r1, y1] = rings[i + 1];
        mb.cylinder(V(x, y + y0 * r, z), r0 * r, r1 * r, (y1 - y0) * r, 10, shade(c, i * 0.04), false);
      }
      mb.cone(V(x, y + 0.93 * r, z), 0.38 * r, 0.07 * r + 0.02, 10, shade(c, 0.12));
    }
    function palm(mb, x, y, z, s, seed) {
      const rnd = SG.SeededRandom(seed | 0);
      const h = s * (0.75 + rnd.nextDouble() * 0.3);
      mb.cylinder(V(x, y, z), 0.035 * s, 0.025 * s, h, 4, C(0.54, 0.42, 0.27), false);
      const top = V(x, y + h, z), leaf = C(0.25, 0.48, 0.2);
      for (let i = 0; i < 6; i++) {
        const a = i * Math.PI / 3 + rnd.nextDouble() * 0.4, ca = Math.cos(a), sa = Math.sin(a), L = 0.34 * s;
        sheet(mb, [top, V(x + ca * L * 0.5 - sa * 0.06 * s, y + h + 0.05 * s, z + sa * L * 0.5 + ca * 0.06 * s),
          V(x + ca * L, y + h - 0.13 * s, z + sa * L), V(x + ca * L * 0.5 + sa * 0.06 * s, y + h + 0.05 * s, z + sa * L * 0.5 - ca * 0.06 * s)], shade(leaf, (i % 2) * 0.08));
      }
    }
    // 尖木桩
    function stake(mb, x, y, z, h, c) {
      mb.cylinder(V(x, y, z), 0.06, 0.06, h, 5, c, false);
      mb.cone(V(x, y + h, z), 0.06, 0.16, 5, shade(c, 0.06));
    }
    // 阶梯垛口（西亚土坯城）
    function stepMerlon(mb, x, y, z, s, c) {
      mb.box(V(x, y + 0.11 * s, z), V(0.46 * s, 0.22 * s, 0.46 * s), shade(c, 0.04));
      mb.box(V(x, y + 0.3 * s, z), V(0.26 * s, 0.18 * s, 0.26 * s), shade(c, 0.06));
    }
    // 马尾纛
    function standard(mb, x, y, z, h, c) {
      mb.box(V(x, y + h / 2, z), V(0.05, h, 0.05), C(0.36, 0.26, 0.15));
      mb.cone(V(x, y + h, z), 0.05, 0.16, 5, C(0.85, 0.7, 0.3));
      mb.cylinder(V(x, y + h - 0.55, z), 0.16, 0.03, 0.5, 6, c, false);
    }
    function yurt(mb, x, y, z, r, felt, crown) {
      mb.cylinder(V(x, y, z), r, r, r * 0.7, 10, felt, false);
      mb.cylinder(V(x, y + r * 0.7, z), r * 1.04, r * 0.18, r * 0.45, 10, shade(felt, -0.08), true);
      if (crown) mb.cone(V(x, y + r * 1.15, z), r * 0.2, r * 0.3, 8, crown);
      mb.box(V(x, y + r * 0.27, z - r * 0.98), V(r * 0.38, r * 0.54, 0.04), C(0.66, 0.26, 0.16));   // 门（朝南，朝镜头）
    }

    const COL = {};
    function col(style) {
      if (COL[style]) return COL[style];
      const k = {
        roman: { wall: C(0.82, 0.78, 0.68), roof: C(0.74, 0.33, 0.21), marble: C(0.93, 0.91, 0.86), door: C(0.38, 0.24, 0.14) },
        persia: { wall: C(0.78, 0.64, 0.46), dome: C(0.84, 0.78, 0.66), door: C(0.3, 0.2, 0.13), trim: C(0.24, 0.5, 0.56) },
        arab: { wall: C(0.87, 0.8, 0.63), dome: C(0.95, 0.93, 0.88), door: C(0.34, 0.22, 0.13), trim: C(0.72, 0.56, 0.3) },
        tarim: { wall: C(0.74, 0.62, 0.47), dome: C(0.8, 0.7, 0.53), door: C(0.32, 0.22, 0.14), trim: C(0.6, 0.48, 0.34) },
        kushan: { wall: C(0.7, 0.47, 0.36), dome: C(0.94, 0.92, 0.86), door: C(0.32, 0.2, 0.12), trim: C(0.88, 0.7, 0.3) },
        korea: { wall: C(0.6, 0.6, 0.57), roof: C(0.25, 0.26, 0.29), timber: C(0.56, 0.24, 0.18), door: C(0.3, 0.2, 0.14) },
        wa: { wall: C(0.5, 0.42, 0.3), wood: C(0.52, 0.38, 0.24), thatch: C(0.66, 0.56, 0.36), grass: C(0.42, 0.56, 0.3) },
        stilt: { wall: C(0.48, 0.4, 0.28), wood: C(0.5, 0.38, 0.24), thatch: C(0.5, 0.4, 0.24), grass: C(0.36, 0.56, 0.26) },
        celt: { wall: C(0.46, 0.42, 0.3), wood: C(0.48, 0.35, 0.22), thatch: C(0.68, 0.58, 0.36), grass: C(0.4, 0.56, 0.3), wattle: C(0.64, 0.55, 0.4) },
        german: { wall: C(0.46, 0.42, 0.3), wood: C(0.45, 0.33, 0.21), thatch: C(0.62, 0.54, 0.34), grass: C(0.4, 0.56, 0.3) },
        steppe: { wall: C(0.6, 0.54, 0.38), wood: C(0.5, 0.38, 0.24), felt: C(0.92, 0.9, 0.84), gold: C(0.9, 0.72, 0.3), hair: C(0.16, 0.13, 0.12), grass: C(0.56, 0.6, 0.34) },
      }[style];
      COL[style] = k;
      return k;
    }
    const MUD = { persia: 1, arab: 1, tarim: 1, kushan: 1 };
    const TIMBER = { wa: 1, stilt: 1, celt: 1, german: 1, steppe: 1 };

    // 城墙格：顶面在 h + 0.2（与原模型同高，部队站位不变）。ctx = { run: {x, z} 城墙沿哪个方向延续, out: {x, z} 城外方向（±1 / 0） }
    function wall(mb, style, cx, h, cz, ctx) {
      const k = col(style);
      if (style === 'roman') {
        mb.box(V(cx, h - 0.6, cz), V(T, 1.6, T), k.wall);
        mb.box(V(cx, h - 0.75, cz), V(T + 0.04, 0.08, T + 0.04), shade(k.wall, -0.12));   // 腰线
        for (const [dx, dz] of [[-0.6, -0.6], [0.6, -0.6], [-0.6, 0.6], [0.6, 0.6]]) mb.box(V(cx + dx, h + 0.36, cz + dz), V(0.34, 0.32, 0.34), shade(k.wall, 0.05));
      } else if (MUD[style]) {
        mb.box(V(cx, h - 0.6, cz), V(T, 1.6, T), k.wall);
        for (let s = -1; s <= 1; s += 2) stepMerlon(mb, cx + s * 0.55, h + 0.2, cz + s * 0.55, 1, k.wall);
      } else if (style === 'korea') {
        // 不规则石块的山城墙 + 黑瓦压顶的女墙
        mb.box(V(cx, h - 0.6, cz), V(T, 1.6, T), k.wall);
        mb.box(V(cx - 0.4, h - 0.95, cz - 0.02), V(0.9, 0.35, T + 0.03), shade(k.wall, -0.08));
        mb.box(V(cx + 0.45, h - 0.35, cz + 0.02), V(0.8, 0.3, T + 0.03), shade(k.wall, 0.06));
        for (let s = -1; s <= 1; s += 2) {
          mb.box(V(cx + s * 0.55, h + 0.36, cz + s * 0.55), V(0.48, 0.32, 0.48), shade(k.wall, 0.04));
          mb.box(V(cx + s * 0.55, h + 0.56, cz + s * 0.55), V(0.58, 0.08, 0.58), k.roof);
        }
      } else if (TIMBER[style]) {
        // 夯土垣 + 外沿一道尖木栅（沿城墙走向，立在朝城外的一侧，不挡站在墙上的部队）
        mb.box(V(cx, h - 0.6, cz), V(T, 1.6, T), k.wall);
        mb.box(V(cx, h - 1.1, cz), V(T + 0.03, 0.5, T + 0.03), k.grass);   // 墙脚草皮
        const run = (ctx && ctx.run) || { x: true, z: false }, out = (ctx && ctx.out) || { x: 0, z: -1 };
        const n = 6, sh = 0.5;
        if (run.x || !run.z) { const z = cz + (out.z || -1) * 0.8; for (let i = 0; i < n; i++) stake(mb, cx - T / 2 + (i + 0.5) * T / n, h + 0.2, z, sh + (i % 2) * 0.08, k.wood); }
        if (run.z) { const x = cx + (out.x || -1) * 0.8; for (let i = 0; i < n; i++) stake(mb, x, h + 0.2, cz - T / 2 + (i + 0.5) * T / n, sh + (i % 2) * 0.08, k.wood); }
      } else return false;
      return true;
    }
    // 城门格：门洞沿 x 摆放、正面朝镜头（与 C# 一致）
    function gate(mb, style, cx, h, cz) {
      const k = col(style);
      if (style === 'roman') {
        // 两座圆塔夹拱门
        for (let s = -1; s <= 1; s += 2) {
          const x = cx + s * 0.72;
          mb.cylinder(V(x, h - 0.2, cz), 0.42, 0.42, 2.0, 10, k.wall, false);
          mb.cylinder(V(x, h + 1.8, cz), 0.47, 0.47, 0.14, 10, shade(k.wall, 0.05), true);
          for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2 + Math.PI / 4; mb.box(V(x + Math.cos(a) * 0.36, h + 2.04, cz + Math.sin(a) * 0.36), V(0.17, 0.2, 0.17), shade(k.wall, 0.05)); }
        }
        mb.box(V(cx, h + 1.38, cz), V(1.05, 0.56, 0.62), k.wall);
        mb.box(V(cx, h + 1.08, cz), V(0.9, 0.06, 0.64), shade(k.wall, -0.15));
        mb.box(V(cx, h + 0.55, cz), V(0.86, 1.05, 0.3), k.door);
        return true;
      }
      if (MUD[style]) {
        // 伊万式门楼：两座（arab 为圆）塔楼 + 门上的高拱框，顶上阶梯垛口
        for (let s = -1; s <= 1; s += 2) {
          const x = cx + s * 0.74;
          if (style === 'arab') {
            mb.cylinder(V(x, h - 0.2, cz), 0.4, 0.36, 2.0, 8, k.wall, true);
            mb.box(V(x, h + 1.84, cz), V(0.5, 0.08, 0.5), k.trim);
          } else {
            mb.box(V(x, h + 0.75, cz), V(0.6, 1.9, 0.78), k.wall);
            if (style !== 'tarim') stepMerlon(mb, x, h + 1.7, cz, 0.85, k.wall);
          }
        }
        mb.box(V(cx, h + 1.25, cz), V(0.95, 0.9, 0.64), shade(k.wall, 0.03));
        mb.box(V(cx, h + 1.25, cz - 0.33), V(0.62, 0.62, 0.04), k.trim);   // 拱框
        mb.box(V(cx, h + 0.55, cz), V(0.82, 1.1, 0.34), k.door);
        if (style !== 'tarim') for (let i = -1; i <= 1; i++) stepMerlon(mb, cx + i * 0.3, h + 1.7, cz, 0.55, k.wall);
        return true;
      }
      if (style === 'korea') {
        // 石砌门台 + 小门楼（红木柱、黑瓦）
        mb.box(V(cx, h + 0.35, cz), V(T * 0.96, 1.1, 0.8), k.wall);
        mb.box(V(cx, h + 0.36, cz), V(0.7, 0.86, 0.84), k.door);
        mb.box(V(cx, h + 1.15, cz), V(1.25, 0.5, 0.5), k.timber);
        mb.chineseRoof(V(cx, h + 1.38, cz), 1.9, 1.0, 0.62, k.roof);
        return true;
      }
      if (style === 'wa' || style === 'stilt') {
        // 木门：两柱两横梁，上覆茅草双坡顶（南海为鞍形翘脊）
        for (let s = -1; s <= 1; s += 2) mb.box(V(cx + s * 0.62, h + 0.8, cz), V(0.16, 1.6, 0.16), k.wood);
        mb.box(V(cx, h + 1.5, cz), V(1.7, 0.12, 0.16), shade(k.wood, 0.05));
        mb.box(V(cx, h + 1.22, cz), V(1.4, 0.09, 0.12), k.wood);
        mb.box(V(cx, h + 0.55, cz), V(1.08, 1.0, 0.1), shade(k.wood, -0.12));
        gable(mb, cx, h + 1.56, cz, 1.9, 0.86, 0.68, k.thatch, false);
        if (style === 'stilt') for (let s = -1; s <= 1; s += 2) sheet(mb, [V(cx + s * 0.95, h + 2.24, cz - 0.05), V(cx + s * 1.25, h + 2.5, cz), V(cx + s * 0.95, h + 2.24, cz + 0.05), V(cx + s * 0.8, h + 2.18, cz)], shade(k.thatch, -0.1));
        else for (let s = -1; s <= 1; s += 2) stake(mb, cx + s * 0.86, h - 0.1, cz, 1.0, k.wood);
        return true;
      }
      if (style === 'celt' || style === 'german') {
        // 木构门塔：两座方木塔 + 门上的走道与护栏
        for (let s = -1; s <= 1; s += 2) {
          const x = cx + s * 0.72;
          mb.box(V(x, h + 0.85, cz), V(0.5, 1.9, 0.56), k.wood);
          for (let i = -1; i <= 1; i += 2) stake(mb, x + i * 0.16, h + 1.8, cz, 0.2, shade(k.wood, 0.05));
        }
        mb.box(V(cx, h + 1.4, cz), V(1.0, 0.12, 0.6), shade(k.wood, 0.06));
        mb.box(V(cx, h + 1.62, cz - 0.27), V(1.0, 0.3, 0.05), k.wood);
        mb.box(V(cx, h + 0.6, cz), V(0.94, 1.1, 0.1), shade(k.wood, -0.12));
        return true;
      }
      if (style === 'steppe') {
        // 车阵口：两辆篷车 + 两杆马尾纛
        for (let s = -1; s <= 1; s += 2) {
          const x = cx + s * 0.62;
          mb.box(V(x, h + 0.32, cz), V(0.56, 0.3, 0.9), k.wood);
          mb.box(V(x, h + 0.15, cz - 0.3), V(0.6, 0.3, 0.06), shade(k.wood, -0.2));
          mb.box(V(x, h + 0.15, cz + 0.3), V(0.6, 0.3, 0.06), shade(k.wood, -0.2));
          mb.blob(V(x, h + 0.5, cz), V(0.3, 0.32, 0.46), k.felt, 3 + s);
          standard(mb, x + s * 0.2, h, cz - 0.42, 2.0, k.hair);
        }
        return true;
      }
      return false;
    }
    // 本阵格（城中央）：主体偏北 0.55，部队站在南侧前方；旗帜由调用方照旧插在东北角
    function keep(mb, style, cx, h, cz) {
      const k = col(style), z = cz + 0.55;
      if (style === 'roman') {
        // 列柱神庙：台基、前后两排柱、红瓦三角山墙朝南
        mb.box(V(cx, h + 0.12, z), V(1.7, 0.24, 1.0), shade(k.marble, -0.06));
        mb.box(V(cx, h + 0.28, z), V(1.55, 0.1, 0.9), k.marble);
        mb.box(V(cx, h + 0.7, z + 0.12), V(1.0, 0.75, 0.5), shade(k.marble, -0.04));
        for (let i = 0; i < 4; i++) for (const dz of [-0.36, 0.36]) {
          const x = cx - 0.6 + i * 0.4;
          mb.cylinder(V(x, h + 0.33, z + dz), 0.075, 0.066, 0.76, 6, k.marble, true);
        }
        mb.box(V(cx, h + 1.16, z), V(1.6, 0.16, 0.96), k.marble);
        gable(mb, cx, h + 1.24, z, 1.7, 1.05, 0.42, k.roof, true);
        return true;
      }
      if (style === 'persia' || style === 'arab') {
        // 平顶宫殿 + 正面伊万 + 穹顶
        mb.box(V(cx, h + 0.5, z + 0.05), V(1.45, 0.9, 0.85), k.wall);
        mb.box(V(cx, h + 0.68, z - 0.38), V(0.8, 1.26, 0.14), shade(k.wall, 0.04));
        mb.box(V(cx, h + 0.55, z - 0.455), V(0.46, 0.78, 0.03), k.door);
        mb.box(V(cx, h + 1.33, z - 0.38), V(0.86, 0.06, 0.16), k.trim);
        mb.cylinder(V(cx, h + 0.95, z + 0.12), 0.42, 0.42, 0.14, 10, shade(k.wall, 0.04), false);
        dome(mb, cx, h + 1.09, z + 0.12, 0.42, k.dome);
        for (let s = -1; s <= 1; s += 2) stepMerlon(mb, cx + s * 0.6, h + 0.95, z - 0.26, 0.5, k.wall);
        if (style === 'arab') mb.cone(V(cx, h + 1.5, z + 0.12), 0.04, 0.22, 5, k.trim);
        return true;
      }
      if (style === 'kushan' || style === 'tarim') {
        // 窣堵波（佛塔）：方台、鼓座、覆钵、平头、相轮
        const dc = style === 'kushan' ? k.dome : k.dome;
        mb.box(V(cx, h + 0.15, z), V(1.3, 0.3, 1.1), k.wall);
        mb.box(V(cx, h + 0.36, z), V(1.0, 0.12, 0.9), shade(k.wall, 0.05));
        mb.cylinder(V(cx, h + 0.42, z), 0.44, 0.44, 0.24, 12, shade(dc, -0.05), false);
        dome(mb, cx, h + 0.66, z, 0.44, dc);
        mb.box(V(cx, h + 1.15, z), V(0.2, 0.14, 0.2), k.trim);
        mb.cylinder(V(cx, h + 1.2, z), 0.025, 0.025, 0.55, 4, k.trim, false);
        for (let i = 0; i < 3; i++) mb.cylinder(V(cx, h + 1.32 + i * 0.13, z), 0.15 - i * 0.035, 0.15 - i * 0.035, 0.03, 8, k.trim, true);
        return true;
      }
      if (style === 'korea') {
        mb.box(V(cx, h + 0.18, z), V(1.6, 0.36, 0.8), k.wall);
        mb.box(V(cx, h + 0.75, z + 0.05), V(1.2, 0.8, 0.5), k.timber);
        mb.chineseRoof(V(cx, h + 1.15, z + 0.05), 1.9, 1.0, 0.75, k.roof);
        return true;
      }
      if (style === 'wa' || style === 'stilt') {
        // 高床殿：柱脚、地板、木壁、陡峭的茅草双坡顶（倭：脊端千木交叉；南海：鞍形翘脊）
        for (const dx of [-0.55, 0, 0.55]) for (const dz of [-0.3, 0.3]) mb.box(V(cx + dx, h + 0.3, z + dz), V(0.09, 0.6, 0.09), k.wood);
        mb.box(V(cx, h + 0.64, z), V(1.35, 0.08, 0.8), shade(k.wood, 0.05));
        mb.box(V(cx, h + 0.92, z), V(1.1, 0.5, 0.6), shade(k.wood, 0.12));
        gable(mb, cx, h + 1.15, z, 1.6, 1.0, 0.9, k.thatch, false);
        for (let s = -1; s <= 1; s += 2) {
          const x = cx + s * 0.8;
          if (style === 'wa') {
            sheet(mb, [V(x, h + 2.02, z - 0.02), V(x - 0.02 * s, h + 2.4, z - 0.22), V(x + 0.02 * s, h + 2.4, z - 0.18), V(x, h + 2.02, z + 0.02)], shade(k.wood, -0.05));
            sheet(mb, [V(x, h + 2.02, z + 0.02), V(x - 0.02 * s, h + 2.4, z + 0.22), V(x + 0.02 * s, h + 2.4, z + 0.18), V(x, h + 2.02, z - 0.02)], shade(k.wood, -0.05));
          } else sheet(mb, [V(x, h + 2.05, z - 0.06), V(x + s * 0.36, h + 2.4, z), V(x, h + 2.05, z + 0.06), V(x - s * 0.15, h + 1.99, z)], shade(k.thatch, -0.1));
        }
        return true;
      }
      if (style === 'celt') {
        // 圆形茅屋
        mb.cylinder(V(cx, h, z), 0.62, 0.62, 0.55, 12, k.wattle, false);
        mb.cylinder(V(cx, h + 0.5, z), 0.8, 0.06, 0.9, 12, k.thatch, false);
        mb.box(V(cx, h + 0.24, z - 0.6), V(0.3, 0.48, 0.06), shade(k.wood, -0.1));
        return true;
      }
      if (style === 'german') {
        // 长屋大厅
        mb.box(V(cx, h + 0.35, z), V(1.6, 0.7, 0.8), k.wood);
        gable(mb, cx, h + 0.7, z, 1.75, 0.95, 0.7, k.thatch, false);
        mb.box(V(cx, h + 0.3, z - 0.41), V(0.3, 0.55, 0.04), shade(k.wood, -0.18));
        return true;
      }
      if (style === 'steppe') {
        // 金顶大帐 + 两杆马尾纛
        yurt(mb, cx, h, z, 0.66, k.felt, k.gold);
        for (let s = -1; s <= 1; s += 2) standard(mb, cx + s * 0.7, h, z - 0.4, 1.8, k.hair);
        return true;
      }
      return false;
    }
    return { of, wall, gate, keep, palm };
  })();

  // Unity CanvasScaler（参考 1600×900，match 0.6）对应的界面缩放
  function uiScale() {
    const w = Math.max(1, window.innerWidth || 1600), h = Math.max(1, window.innerHeight || 900);
    return Math.exp(0.4 * Math.log(w / 1600) + 0.6 * Math.log(h / 900));
  }

  // MapView.FlatDisc：带 UV 的水平方片（贴图决定形状）
  const discCache = new Map();
  function flatDisc(r) {
    let g = discCache.get(r);
    if (g) return g;
    const mb = new SG.MeshBuilder();
    mb.flatQuad(V(-r, 0, -r), V(-r, 0, r), V(r, 0, r), V(r, 0, -r), C(1, 1, 1));
    g = mb.toGeometry();
    discCache.set(r, g);
    return g;
  }

  // 移动 / 攻击范围的格子贴图：圆角方块，内部半透明、边缘发亮、外缘柔光
  let tileTex = null;
  function tileTexture() {
    if (tileTex) return tileTex;
    const n = 64, data = new Uint8Array(n * n * 4);
    const half = 0.84, rad = 0.24;
    for (let y = 0; y < n; y++)
      for (let x = 0; x < n; x++) {
        const u = (x + 0.5) / n * 2 - 1, v = (y + 0.5) / n * 2 - 1;
        const qx = Math.abs(u) - (half - rad), qy = Math.abs(v) - (half - rad);
        const d = Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0) - rad;
        let a;
        // 内部填充接近 Unity SoftDot 中心的不透明度（高亮在草地上需清晰可辨）
        if (d < 0) a = 0.62 + 0.3 * M.smoothStep(0, 1, 1 + d / 0.35);
        else a = 0.75 * Math.exp(-d / 0.05);
        a = Math.max(a, Math.exp(-(d / 0.04) * (d / 0.04)));
        const i = (y * n + x) * 4;
        data[i] = data[i + 1] = data[i + 2] = 255;
        data[i + 3] = Math.round(M.clamp01(a) * 255);
      }
    tileTex = new THREE.DataTexture(data, n, n, THREE.RGBAFormat);
    tileTex.wrapS = tileTex.wrapT = THREE.ClampToEdgeWrapping;
    tileTex.magFilter = THREE.LinearFilter;
    tileTex.minFilter = THREE.LinearMipmapLinearFilter;
    tileTex.generateMipmaps = true;
    tileTex.needsUpdate = true;
    return tileTex;
  }

  // GLSL 式 smoothstep（注意 M.smoothStep 是 Unity 语义：在 from..to 间平滑插值）
  function sstep(e0, e1, x) { const t = M.clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }

  // 火舌贴图：上尖下圆的水滴形（第 0 行 = 点精灵顶部，gl_PointCoord 原点在左上）
  let flameTex = null;
  function flameTexture() {
    if (flameTex) return flameTex;
    const n = 64, data = new Uint8Array(n * n * 4);
    const yc = 0.63, r = 0.31, yt = 0.02;
    for (let py = 0; py < n; py++)
      for (let px = 0; px < n; px++) {
        const x = (px + 0.5) / n - 0.5, y = (py + 0.5) / n;
        let d;
        if (y >= yc) d = Math.hypot(x, (y - yc) * 1.08) / r;
        else if (y > yt) d = Math.abs(x) / Math.max(1e-4, r * Math.pow((y - yt) / (yc - yt), 0.8));
        else d = 9;
        let a = 1 - sstep(0.42, 1, d);
        a *= sstep(yt, yt + 0.32, y);           // 火舌尖端渐隐
        const i = (py * n + px) * 4;
        data[i] = data[i + 1] = data[i + 2] = 255;
        data[i + 3] = Math.round(M.clamp01(a) * 255);
      }
    flameTex = new THREE.DataTexture(data, n, n, THREE.RGBAFormat);
    flameTex.wrapS = flameTex.wrapT = THREE.ClampToEdgeWrapping;
    flameTex.magFilter = THREE.LinearFilter;
    flameTex.minFilter = THREE.LinearMipmapLinearFilter;
    flameTex.generateMipmaps = true;
    flameTex.needsUpdate = true;
    return flameTex;
  }

  // ------------------------------------------------------------- 样式 --
  function injectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-battle-view-style')) return;
    const s = document.createElement('style');
    s.id = 'sg-battle-view-style';
    // 层级：部队标签用负 z-index（标签层自身是层叠上下文），伤害飘字（z auto）始终压在最上面；
    // 选中部队 > 主将 > 其他。避让排布每帧再按优先级细排（见 BattleView._layoutLabels）。
    s.textContent = `
.sg-uinfo-anchor{position:absolute;left:0;top:0;width:0;height:0;pointer-events:none;z-index:-30;}
.sg-uinfo-anchor.cmd{z-index:-20;}
.sg-uinfo-anchor.sel{z-index:-10;}
.sg-uinfo-anchor .sg-stem{position:absolute;left:-1px;bottom:0;width:2px;height:0;opacity:0;border-radius:1px;
  background:linear-gradient(0deg,rgba(243,201,105,.25),rgba(243,201,105,.75));box-shadow:0 0 2px rgba(0,0,0,.6);}
.sg-uinfo-anchor .sg-stem::after{content:"";position:absolute;left:-2px;bottom:-3px;width:6px;height:6px;border-radius:50%;
  background:#f3c969;box-shadow:0 0 3px rgba(0,0,0,.7);}
.sg-uinfo{position:absolute;left:0;bottom:.35em;transform:translateX(-50%);box-sizing:border-box;
  font:600 clamp(11px,calc(1.0vmin + .45vmax),16px)/1.2 "PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif,"WenQuanYi Zen Hei";
  color:#f5eddb;white-space:nowrap;min-width:6.6em;padding:.3em .6em .45em 1.0em;text-align:center;
  background:linear-gradient(180deg,rgba(34,31,42,.88),rgba(10,10,16,.82));
  border:1px solid rgba(243,201,105,.42);border-radius:.55em;
  box-shadow:0 .3em .9em rgba(0,0,0,.38),inset 0 1px 0 rgba(255,255,255,.09);
  -webkit-backdrop-filter:blur(3px);backdrop-filter:blur(3px);text-shadow:0 1px 2px rgba(0,0,0,.7);
  transition:opacity .2s ease;}
.sg-uinfo.cmd{border-color:rgba(243,201,105,.85);}
.sg-uinfo .sg-stripe{position:absolute;left:.25em;top:.25em;bottom:.25em;width:.34em;border-radius:.2em;
  box-shadow:0 0 .4em rgba(255,255,255,.18);}
.sg-uinfo .sg-star{color:#f3c969;margin-right:.18em;}
.sg-uinfo .sg-tr{font-size:.8em;font-weight:500;color:#d8cfbb;margin-left:.5em;font-variant-numeric:tabular-nums;}
.sg-uinfo .sg-bar{height:.36em;margin-top:.26em;background:rgba(0,0,0,.5);border-radius:.2em;overflow:hidden;
  box-shadow:inset 0 0 0 1px rgba(255,255,255,.05);}
.sg-uinfo .sg-bar>i{display:block;height:100%;border-radius:.2em;transition:width .35s ease;}
.sg-uinfo.sel{border-color:rgba(255,226,140,.95);box-shadow:0 .3em .9em rgba(0,0,0,.38),0 0 0 1px rgba(243,201,105,.35),inset 0 1px 0 rgba(255,255,255,.09);}
@media (max-height:540px){
  .sg-uinfo{font-size:11px;min-width:0;padding:.22em .5em .34em .9em;border-radius:.45em;
    box-shadow:0 .2em .5em rgba(0,0,0,.35),inset 0 1px 0 rgba(255,255,255,.08);}
  .sg-uinfo .sg-stripe{left:.22em;top:.22em;bottom:.22em;width:.3em;}
  .sg-uinfo .sg-star{margin-right:.08em;}
  .sg-uinfo .sg-tr{display:none;margin-left:.35em;}
  .sg-uinfo.sel .sg-tr{display:inline;}
  .sg-uinfo .sg-bar{height:.32em;margin-top:.2em;}
}
`;
    (document.head || document.documentElement).appendChild(s);
  }

  // ============================================================ 粒子系统 --
  const PARTICLE_VS = `
attribute vec4 aColor;
attribute float aSize;
uniform float uScale;
varying vec4 vColor;
void main() {
  vColor = aColor;
  vec4 mv = modelViewMatrix * vec4( position, 1.0 );
  gl_PointSize = max( 1.0, aSize * uScale / max( 0.05, - mv.z ) );
  gl_Position = projectionMatrix * mv;
}`;
  const PARTICLE_FS = `
uniform sampler2D uMap;
uniform float uSoft;
varying vec4 vColor;
void main() {
  float a = pow( texture2D( uMap, gl_PointCoord ).a, uSoft ) * vColor.a;
  if ( a < 0.004 ) discard;
  gl_FragColor = vec4( vColor.rgb, a );
}`;

  class ParticlePool {
    // blending：THREE.AdditiveBlending（火焰、火花）或 THREE.NormalBlending（烟尘）；map 缺省为柔光点
    constructor(max, blending, soft, renderOrder, map) {
      this.max = max;
      this.parts = [];
      this.pos = new Float32Array(max * 3);
      this.col = new Float32Array(max * 4);
      this.size = new Float32Array(max);
      const g = new THREE.BufferGeometry();
      this.aPos = new THREE.BufferAttribute(this.pos, 3); this.aPos.setUsage(THREE.DynamicDrawUsage);
      this.aCol = new THREE.BufferAttribute(this.col, 4); this.aCol.setUsage(THREE.DynamicDrawUsage);
      this.aSize = new THREE.BufferAttribute(this.size, 1); this.aSize.setUsage(THREE.DynamicDrawUsage);
      g.setAttribute('position', this.aPos);
      g.setAttribute('aColor', this.aCol);
      g.setAttribute('aSize', this.aSize);
      g.setDrawRange(0, 0);
      this.geometry = g;
      this.material = new THREE.ShaderMaterial({
        uniforms: { uMap: { value: map || SG.Gfx.softDotTexture }, uScale: { value: 600 }, uSoft: { value: soft } },
        vertexShader: PARTICLE_VS, fragmentShader: PARTICLE_FS,
        transparent: true, depthWrite: false, depthTest: true, blending, fog: false,
      });
      this.points = new THREE.Points(g, this.material);
      this.points.frustumCulled = false;
      this.points.renderOrder = renderOrder;
    }
    add(p) {
      if (this.parts.length >= this.max) this.parts.shift();
      this.parts.push(p);
    }
    update(dt, scale) {
      this.material.uniforms.uScale.value = scale;
      const P = this.parts;
      let n = 0;
      for (let i = 0; i < P.length; i++) {
        const p = P[i];
        p.age += dt;
        if (p.age >= p.life) continue;
        p.vy -= p.grav * GRAVITY * dt;
        if (p.drag) { const k = Math.max(0, 1 - p.drag * dt); p.vx *= k; p.vy *= k; p.vz *= k; }
        p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
        P[n++] = p;
      }
      P.length = n;
      for (let i = 0; i < n; i++) {
        const p = P[i];
        const t = p.age / p.life;
        const fade = p.fadeIn > 0 ? Math.min(1, t / p.fadeIn) : 1;
        this.pos[i * 3] = p.x; this.pos[i * 3 + 1] = p.y; this.pos[i * 3 + 2] = p.z;
        this.col[i * 4] = p.r + (p.r1 - p.r) * t; this.col[i * 4 + 1] = p.g + (p.g1 - p.g) * t; this.col[i * 4 + 2] = p.b + (p.b1 - p.b) * t;
        // 透明度 1 → 0（C# 渐变为线性；ac > 1 时前半段保持不透明，如火舌 a·(1−t²)）
        this.col[i * 4 + 3] = p.a * (p.ac === 1 ? 1 - t : 1 - Math.pow(t, p.ac)) * fade;
        this.size[i] = p.size * M.lerpUnclamped(1, p.sizeEnd, t);          // 尺寸 1 → sizeEnd
      }
      this.geometry.setDrawRange(0, n);
      if (n > 0) { this.aPos.needsUpdate = true; this.aCol.needsUpdate = true; this.aSize.needsUpdate = true; }
    }
    clear() { this.parts.length = 0; this.geometry.setDrawRange(0, 0); }
    dispose() { this.geometry.dispose(); this.material.dispose(); }
  }

  // 球内随机点
  function inSphere() {
    for (;;) {
      const x = Math.random() * 2 - 1, y = Math.random() * 2 - 1, z = Math.random() * 2 - 1;
      const d = x * x + y * y + z * z;
      if (d <= 1 && d > 1e-6) return { x, y, z, d: Math.sqrt(d) };
    }
  }

  // ============================================================ UnitVisual --
  // 一支部队：武将 + 若干士兵 + 头顶信息
  class UnitVisual {
    constructor(view, unit, col) {
      this.view = view; this.u = unit; this.col = col;
      this.group = new THREE.Group();
      this.group.name = 'Unit_' + unit.gen.name;
      view.group.add(this.group);
      this.material = SG.Gfx.newLowPoly();
      this.mesh = SG.Gfx.mesh(new THREE.BufferGeometry(), this.material, { castShadow: true, receiveShadow: true });
      this.group.add(this.mesh);
      this.ring = SG.Gfx.mesh(flatDisc(0.95), view.sideRing[unit.side === 0 ? 0 : 1], { castShadow: false, receiveShadow: false });
      this.ring.position.y = 0.04;
      this.ring.renderOrder = 2;
      this.group.add(this.ring);
      this.bucket = -1;
      this.pos = view.tile(unit.x, unit.y);      // 逻辑位置（动画写这里）
      this.facing = unit.side === 0 ? 90 : -90;  // Unity 偏航角（度）
      this.yaw = this.facing;
      this.flash = 0;
      this.heat = 0;                             // 火计灼烧的橙色发光（1 → 0）
      this.heatSeed = Math.random() * 10;
      this.confuseRing = null;
      this.labelPos = new THREE.Vector3();
      this.labelAlpha = 1;                       // C# WorldFollow.alphaMul
      this.holdLabel = false;                    // 控制器置 true 时，阵亡部队的标签暂不隐藏（单挑画面播完前）
      // 标签避让：目标偏移 (tdx, tdy↑, tfade) 与平滑后的当前值
      this.lbl = { dx: 0, dy: 0, fade: 1, tdx: 0, tdy: 0, tfade: 1, z: -30, shown: false };
      this.info = null; this.follow = null;
      this.group.position.copy(this.pos);
      this.group.rotation.y = SG.yawToThree(this.yaw);
      this._buildLabel();
      this.refresh();
    }

    get active() { return this.group.visible; }
    // 兼容 C# 写法 V.Vis(u).gameObject.activeSelf
    get gameObject() { const self = this; return { get activeSelf() { return self.group.visible; } }; }
    get transform() { return this.group; }

    _buildLabel() {
      if (typeof document === 'undefined' || !SG.UI || !SG.UI.follow) return;
      injectStyle();
      const u = this.u;
      const anchor = document.createElement('div');
      anchor.className = 'sg-uinfo-anchor';
      const card = document.createElement('div');
      card.className = 'sg-uinfo side' + u.side;
      const stripe = document.createElement('i');
      stripe.className = 'sg-stripe';
      stripe.style.background = cssHex(this.col);
      const name = document.createElement('div');
      name.className = 'sg-nm';
      const bar = document.createElement('div');
      bar.className = 'sg-bar';
      const fill = document.createElement('i');
      fill.style.background = `linear-gradient(90deg, ${SIDE_BAR[u.side === 0 ? 0 : 1]}, ${SG.Gfx.shade(SIDE_BAR[u.side === 0 ? 0 : 1], 0.35).getStyle()})`;
      bar.appendChild(fill);
      card.appendChild(stripe); card.appendChild(name); card.appendChild(bar);
      const stem = document.createElement('i');   // 标签被挤开时连回部队的引线
      stem.className = 'sg-stem';
      anchor.appendChild(stem);
      anchor.appendChild(card);
      this.info = {
        anchor, card, name, fill, stem, lastText: '', lastW: -1, sel: false,
        w: 0, h: 0, gap: 0, dirty: true, tf: '', stemH: -1, z: null,
      };
      this.follow = SG.UI.follow(anchor, () => this.labelPos, { offsetY: 0, hideBeyond: 90 });
      this._updateLabelPos();
    }
    _updateLabelPos() {
      // C#：WorldFollow 偏移 74（参考分辨率像素）≈ 旗顶之上
      this.labelPos.copy(this.group.position);
      this.labelPos.y += 2.25;
    }

    refresh() {
      const u = this.u;
      const b = M.clamp(Math.ceil(u.troops / 350), u.alive ? 1 : 0, 12);
      if (b !== this.bucket) {
        this.bucket = b;
        const mb = new SG.MeshBuilder();
        SG.Models.commander(mb, V(0, 0, 0.35), this.col, 1.5);
        for (let i = 0; i < b; i++) {
          const row = M.idiv(i, 4), cIdx = i % 4;
          const p = V((cIdx - 1.5) * 0.38 + (row % 2) * 0.1, 0, -0.25 - row * 0.36);
          SG.Models.soldier(mb, p, this.col, true, 1.3);
        }
        const old = this.mesh.geometry;
        this.mesh.geometry = mb.toGeometry();
        if (old) old.dispose();
      }
      if (this.info) {
        const I = this.info;
        const text = (u.commander ? '<span class="sg-star">★</span>' : '') + SG.esc(u.gen.name) + '<span class="sg-tr">' + u.troops + '</span>';
        if (text !== I.lastText) { I.name.innerHTML = text; I.lastText = text; I.dirty = true; }
        const max = Math.max(1, SG.maxTroops ? SG.maxTroops(u.gen) : (u.gen.maxTroops || u.troops || 1));
        const w = M.clamp01(u.troops / max);
        if (w !== I.lastW) { I.fill.style.width = (w * 100).toFixed(1) + '%'; I.lastW = w; }
        I.card.classList.toggle('cmd', !!u.commander);
        I.anchor.classList.toggle('cmd', !!u.commander);
        I.card.style.visibility = u.alive ? '' : 'hidden';
      }
      if (u.confused > 0 && !this.confuseRing) {
        this.confuseRing = SG.Gfx.mesh(flatDisc(0.6), SG.Gfx.unlit(C(0.75, 0.5, 1), SG.Gfx.ringTexture, 0.9), { castShadow: false, receiveShadow: false });
        this.confuseRing.position.y = 1.6;
        this.confuseRing.renderOrder = 5;
        this.group.add(this.confuseRing);
      }
      if (u.confused <= 0 && this.confuseRing) {
        this.group.remove(this.confuseRing);
        this.confuseRing.material.dispose();
        this.confuseRing = null;
      }
      this._spRefresh();          // 必杀技的限时加成（光环 + 名牌小字）
    }

    // dir 为 three 世界向量
    face(dir) {
      if (dir.x * dir.x + dir.y * dir.y + dir.z * dir.z > 0.001) this.facing = Math.atan2(dir.x, -dir.z) * M.rad2deg;
    }
    rest() { }
    flashHit() { this.flash = 1; }
    burn() { this.heat = 1; }
    // 选中（光标所在）部队：标签置顶、描边加亮，窄屏时也显示兵力
    setSelected(on) {
      const I = this.info;
      if (!I || I.sel === on) return;
      I.sel = on;
      I.card.classList.toggle('sel', on);
      I.anchor.classList.toggle('sel', on);
      I.dirty = true;
    }

    update(dt, time) {
      const u = this.u;
      this.yaw = M.lerpAngle(this.yaw, this.facing, dt * 10);
      this.group.rotation.y = SG.yawToThree(this.yaw);
      let shake = 0;
      if (this.flash > 0) {
        this.flash = Math.max(0, this.flash - dt * 4);
        shake = Math.sin(time * 80) * 0.03 * this.flash;
      }
      // 受击闪白（C# _Flash）与火计灼烧的橙色闪烁合成到同一个 flash / flashColor 上
      const burn = this.heat > 0 ? M.smoothStep(0, 1, this.heat) * 0.55 * (0.86 + 0.14 * Math.sin(time * 26 + this.heatSeed)) : 0;
      if (this.heat > 0) this.heat = Math.max(0, this.heat - dt * 0.42);
      const fc = this.material.flashColor;
      if (fc && fc.isColor) {
        if (burn > 0) fc.copy(HEAT_TINT).lerp(WHITE, this.flash / (this.flash + burn));
        else if (fc.r !== 1 || fc.g !== 1 || fc.b !== 1) fc.copy(WHITE);
      }
      this.material.flash = Math.max(this.flash, burn);
      this.group.position.set(this.pos.x + shake, this.pos.y, this.pos.z);
      if (this.confuseRing) this.confuseRing.rotation.y -= 200 * M.deg2rad * dt;
      this._spUpdate(dt, time);   // 必杀技：加成光环、着色光
      this.labelAlpha = ((u.alive || this.holdLabel) && this.group.visible) ? (u.acted && u.side === this.view.model.side ? 0.55 : 1) : 0;
      if (this.follow) {
        this._updateLabelPos();
        this.follow.alpha = this.labelAlpha * this.lbl.fade;   // BattleView._layoutLabels 随后按避让结果再设
      }
    }

    dispose() {
      if (this.follow) { try { this.follow.remove(); } catch (e) { /* 忽略 */ } this.follow = null; }
      if (this.info && this.info.anchor.parentNode) this.info.anchor.parentNode.removeChild(this.info.anchor);
      this.info = null;
      if (this.mesh.geometry) this.mesh.geometry.dispose();
      this.material.dispose();
      if (this.confuseRing) this.confuseRing.material.dispose();
      this._spDispose();
      if (this.group.parent) this.group.parent.remove(this.group);
    }
  }

  // ============================================================ BattleView --
  class BattleView {
    constructor(model) {
      this.model = model;
      this.M = model;                 // C# 字段名
      this.T = T;
      this.group = new THREE.Group();
      this.group.name = 'BattleView';
      this.visuals = new Map();       // BUnit → UnitVisual
      this.highlights = [];
      this.hlMats = new Map();
      this.time = 0;
      this.disposed = false;
      this.emitters = [];
      this.burnFx = new Map();        // "x,y" → { emitters }
      this.temp = [];                 // 临时网格（特效）
      this.cursorUnit = null;
      this._lblKey = '';              // 视口尺寸变化时重新量标签
      this._lblMeasureT = 0;
      this.build();
      if (SG.Gfx && SG.Gfx.scene) SG.Gfx.scene.add(this.group);
    }
    get root() { return this.group; }

    get boardCenter() { return SG.U(ORIGIN.x + this.model.W * T / 2, 0, ORIGIN.z + this.model.H * T / 2); }

    topH(x, y) {
      const Tn = TR(), m = this.model;
      switch (m.map[x][y]) {
        case Tn.River: return -0.25;
        case Tn.Wall: return 1.4;
        case Tn.Mountain: return 0.6;
        case Tn.Hill: return 0.45;
        case Tn.Gate: case Tn.Castle: return 0.2;
        default: return 0.08 + m.height[x][y] * 0.6;
      }
    }
    // 格子中心（three 世界坐标）
    tile(x, y) {
      const Tn = TR(), t = this.model.map[x][y];
      const h = t === Tn.River ? -0.05 : t === Tn.Wall ? 1.4 : this.topH(x, y);
      return SG.U(ORIGIN.x + x * T + T / 2, h, ORIGIN.z + y * T + T / 2);
    }

    // ---------------------------------------------------------- 构建 --
    build() {
      const m = this.model, Tn = TR();
      const mb = new SG.MeshBuilder();
      const deco = new SG.MeshBuilder();
      const setup = m.setup || {};
      const rnd = SG.SeededRandom(setup.target ? setup.target.id : 0);
      // 战场所在城池的文化：城墙、城门、本阵样式与地表色调（汉地保持原样）
      const look = this.look = BattleLook.of(setup), gr = look.ground, st = look.style;
      const isWall = (x, y) => x >= 0 && y >= 0 && x < m.W && y < m.H && (m.map[x][y] === Tn.Wall || m.map[x][y] === Tn.Gate);
      const ca = m.castle || { x: m.W >> 1, y: m.H >> 1 };
      const wallCtx = (x, y) => ({ run: { x: isWall(x - 1, y) || isWall(x + 1, y), z: isWall(x, y - 1) || isWall(x, y + 1) },
        out: { x: Math.sign(x - ca.x), z: Math.sign(y - ca.y) } });
      const atkCol = this._factionColor(setup.attacker);
      const defCol = this._factionColor(setup.defender);
      const ox = ORIGIN.x, oz = ORIGIN.z;
      const inset = 0.05, bot = -0.6;
      for (let x = 0; x < m.W; x++)
        for (let y = 0; y < m.H; y++) {
          const t = m.map[x][y];
          const h = this.topH(x, y);
          const px = ox + x * T, pz = oz + y * T;
          const c = BattleView.tileColor(t, rnd.nextDouble(), gr);
          const a = V(px + inset, h, pz + inset), b = V(px + inset, h, pz + T - inset);
          const cc = V(px + T - inset, h, pz + T - inset), d = V(px + T - inset, h, pz + inset);
          mb.quad(a, b, cc, d, c);
          // 侧面（让高低差可见）
          const dark = shade(c, -0.25);
          mb.quad(V(px + inset, bot, pz + inset), a, d, V(px + T - inset, bot, pz + inset), dark);
          mb.quad(V(px + T - inset, bot, pz + inset), d, cc, V(px + T - inset, bot, pz + T - inset), dark);
          mb.quad(V(px + T - inset, bot, pz + T - inset), cc, b, V(px + inset, bot, pz + T - inset), dark);
          mb.quad(V(px + inset, bot, pz + T - inset), b, a, V(px + inset, bot, pz + inset), dark);
          const cx = px + T / 2, cz = pz + T / 2;
          switch (t) {
            case Tn.Forest:
              for (let k = 0; k < 4; k++) {
                const dx = (rnd.nextDouble() - 0.5) * 1.3, dz = (rnd.nextDouble() - 0.5) * 1.3;
                const s = 0.9 + rnd.nextDouble() * 0.5;
                const leaf = shade(gr.leaf, (rnd.nextDouble() - 0.5) * 0.15);
                const pine = rnd.nextDouble() < 0.4;
                // 沙漠绿洲全是椰枣，南方林中夹椰树（不多取随机数，与原序列一致）
                if (gr.palm > 0 && (gr.palm >= 1 || ((k + x * 3 + y * 5) % 5) < gr.palm * 5)) BattleLook.palm(deco, cx + dx, h, cz + dz, s * 1.35, k + x * 7 + y * 13);
                else SG.Models.tree(deco, V(cx + dx, h, cz + dz), s, leaf, pine, k + x * 7 + y * 13);
              }
              break;
            case Tn.Hill:
              deco.blob(V(cx, h - 0.1, cz), V(0.85, 0.35, 0.85), gr.hillBlob, x * 31 + y);
              break;
            case Tn.Mountain:
              deco.cone(V(cx - 0.2, h - 0.1, cz + 0.1), 0.9, 1.9 + rnd.nextDouble() * 0.6, 5, gr.mountain[0]);
              deco.cone(V(cx + 0.45, h - 0.1, cz - 0.35), 0.55, 1.1, 5, gr.mountain[1]);
              deco.cone(V(cx - 0.2, h + 1.25, cz + 0.1), 0.32, 0.9, 5, gr.snow ? C(0.95, 0.96, 0.98) : shade(gr.mountain[0], 0.08));
              break;
            case Tn.Wall:
              if (st !== 'han' && BattleLook.wall(deco, st, cx, h, cz, wallCtx(x, y))) break;
              deco.box(V(cx, h - 0.6, cz), V(T, 1.6, T), C(0.66, 0.63, 0.57));
              for (let k = -1; k <= 1; k += 2) deco.box(V(cx + k * 0.55, h + 0.32, cz + k * 0.55), V(0.45, 0.35, 0.45), C(0.7, 0.67, 0.6));
              break;
            case Tn.Gate: {
              if (BattleLook.gate(deco, st, cx, h, cz)) break;
              // 城门顺着城墙走向摆放（两侧为城墙时旋转 90°）
              // 与 C# 一致：门楼一律沿 x 摆放（红色门面朝向镜头）。东西两侧城墙上的城门也不旋转，
              // 否则红色门身会藏在屋檐和两侧墙头之下，只剩一个深蓝屋顶。
              deco.box(V(cx, h + 0.75, cz), V(T * 0.9, 1.1, 0.4), C(0.62, 0.2, 0.16));
              deco.chineseRoof(V(cx, h + 1.3, cz), 2.2, 1.2, 0.7, C(0.22, 0.26, 0.34));
              break;
            }
            case Tn.Castle:
              if (!BattleLook.keep(deco, st, cx, h, cz)) {
                deco.box(V(cx, h + 0.15, cz + 0.55), V(1.6, 0.3, 0.7), C(0.75, 0.72, 0.64));
                deco.box(V(cx, h + 0.75, cz + 0.6), V(1.2, 0.9, 0.5), C(0.66, 0.22, 0.18));
                deco.chineseRoof(V(cx, h + 1.2, cz + 0.6), 1.9, 1.0, 0.8, C(0.85, 0.65, 0.2));
              }
              deco.flag(V(cx + 0.8, h, cz + 0.8), 2.4, 0.8, 0.55, defCol);
              break;
          }
        }
      // 棋盘底板：格子之间的缝隙向下看时是深色的“砖缝”，而不是透出背景
      {
        const BW = m.W * T, BH = m.H * T, uy = -0.55;
        mb.quad(V(ox, uy, oz), V(ox, uy, oz + BH), V(ox + BW, uy, oz + BH), V(ox + BW, uy, oz), gr.under);
      }
      this.buildGrass(deco);
      const board = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly(), { castShadow: false, receiveShadow: true });
      board.name = 'Board';
      this.group.add(board);
      const decoMesh = SG.Gfx.mesh(deco.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      decoMesh.name = 'Deco';
      this.group.add(decoMesh);
      // 河面
      const wm = new SG.MeshBuilder();
      for (let x = 0; x < m.W; x++) for (let y = 0; y < m.H; y++)
        if (m.map[x][y] === Tn.River) {
          const px = ox + x * T, py = -0.05, pz = oz + y * T;
          wm.flatQuad(V(px, py, pz), V(px, py, pz + T), V(px + T, py, pz + T), V(px + T, py, pz), C(0.4, 0, 0));
        }
      if (wm.count > 0) {
        const river = SG.Gfx.mesh(wm.toGeometry(), SG.Gfx.water(), { castShadow: false, receiveShadow: false });
        river.name = 'River';
        river.renderOrder = 1;
        this.group.add(river);
      }
      this.buildSurroundings(rnd);
      // 光标
      this.cursor = SG.Gfx.mesh(flatDisc(T * 0.62), SG.Gfx.unlit(C(1, 0.85, 0.35), SG.Gfx.ringTexture, 1), { castShadow: false, receiveShadow: false });
      this.cursor.name = 'Cursor';
      this.cursor.renderOrder = 4;
      this.cursor.visible = false;
      this.group.add(this.cursor);
      this.cursorGlow = SG.Gfx.mesh(flatDisc(T * 0.62), SG.Gfx.additive(C(1, 0.78, 0.35)), { castShadow: false, receiveShadow: false });
      this.cursorGlow.material.opacity = 0.32;
      this.cursorGlow.renderOrder = 3;
      this.cursorGlow.visible = false;
      this.group.add(this.cursorGlow);
      this.hlGeo = flatDisc(T * 0.5);
      // 部队脚下的阵营光环（攻方蓝、守方红，与兵力条同色）
      this.sideRing = [0, 1].map(i => {
        const m = SG.Gfx.unlit(SIDE_BAR[i], SG.Gfx.ringTexture, 0.7);
        return m;
      });
      // 粒子与光（绘制顺序：烟尘 → 火底热光 → 火舌 → 火花 / 炽热火芯）
      this.glow = new ParticlePool(1200, THREE.AdditiveBlending, 1.0, 14);   // 火花、法术光点、火芯
      // 火焰：白天场景里纯叠加会被洗成一片亮黄，这里火舌用普通混合 + 水滴形贴图，
      // 底下垫一层叠加的热光，上面再叠炽热的火芯
      this.flame = new ParticlePool(1400, THREE.NormalBlending, 1.0, 13, flameTexture());
      this.heat = new ParticlePool(500, THREE.AdditiveBlending, 1.0, 12);
      this.dust = new ParticlePool(900, THREE.NormalBlending, 0.6, 11);      // 烟尘
      this.group.add(this.dust.points);
      this.group.add(this.heat.points);
      this.group.add(this.flame.points);
      this.group.add(this.glow.points);
      this.lights = [];
      for (let i = 0; i < 2; i++) {
        // C#：color (1,0.5,0.2)、range 6、intensity 3（按 Unity 的衰减曲线换算为 decay 1）
        const l = new THREE.PointLight(C(1, 0.5, 0.2), 0, 6, 1);
        l.castShadow = false;
        l.userData = { busy: false, base: 0, cur: 0, seed: i * 17.3, glow: null };
        this.group.add(l);
        this.lights.push(l);
      }
      // 部队
      for (const u of m.units) this.createUnit(u, u.side === 0 ? atkCol : defCol);
    }

    // 平地上的草丛与小花（独立的随机序列，不影响 C# 的地形随机数顺序）
    buildGrass(deco) {
      const m = this.model, Tn = TR();
      const setup = m.setup || {};
      const rnd = SG.SeededRandom(((setup.target ? setup.target.id : 0) | 0) * 7919 + 17);
      const flowers = [C(0.98, 0.95, 0.85), C(1, 0.82, 0.3), C(0.95, 0.55, 0.6), C(0.75, 0.62, 0.95)];
      const gr = (this.look || BattleLook.of(setup)).ground;
      const blade = (bx, by, bz, h, lean, ang, col) => {
        const ca = Math.cos(ang), sa = Math.sin(ang), w = 0.035;
        const a = V(bx - ca * w, by, bz - sa * w), b = V(bx + ca * w, by, bz + sa * w);
        const tip = V(bx + lean * -sa, by + h, bz + lean * ca);
        deco.tri(a, tip, b, col); deco.tri(a, b, tip, col);
      };
      for (let x = 0; x < m.W; x++)
        for (let y = 0; y < m.H; y++) {
          if (m.map[x][y] !== Tn.Plain) continue;
          const h = this.topH(x, y);
          const cx = ORIGIN.x + x * T + T / 2, cz = ORIGIN.z + y * T + T / 2;
          const tufts = 3 + rnd.next(3);
          for (let k = 0; k < tufts; k++) {
            let dx = (rnd.nextDouble() * 2 - 1) * 0.85, dz = (rnd.nextDouble() * 2 - 1) * 0.85;
            if (Math.abs(dx) < 0.5 && Math.abs(dz) < 0.6) dx = dx < 0 ? -0.5 - rnd.nextDouble() * 0.3 : 0.5 + rnd.nextDouble() * 0.3;
            const col = shade(gr.tuft, (rnd.nextDouble() - 0.5) * 0.2);
            const s = 0.75 + rnd.nextDouble() * 0.5;
            for (let j = 0; j < 3; j++)
              blade(cx + dx + (rnd.nextDouble() - 0.5) * 0.08, h, cz + dz + (rnd.nextDouble() - 0.5) * 0.08,
                (0.13 + rnd.nextDouble() * 0.08) * s, (rnd.nextDouble() - 0.5) * 0.1, rnd.nextDouble() * Math.PI, shade(col, j * 0.05));
          }
          if (rnd.nextDouble() < gr.flowers) {
            const fc = flowers[rnd.next(flowers.length)];
            const n = 1 + rnd.next(3);
            for (let k = 0; k < n; k++) {
              const fx = (rnd.nextDouble() * 2 - 1) * 0.8, fz = (rnd.nextDouble() < 0.5 ? -1 : 1) * (0.62 + rnd.nextDouble() * 0.25);
              deco.box(V(cx + fx, h + 0.05, cz + fz), V(0.06, 0.04, 0.06), fc, 0.1);
              deco.box(V(cx + fx, h + 0.015, cz + fz), V(0.015, 0.05, 0.015), C(0.3, 0.5, 0.22), 0);
            }
          }
        }
    }

    _factionColor(idx) {
      const g = SG.G;
      const f = (g && idx !== undefined && idx >= 0 && g.factions) ? g.factions[idx] : null;
      if (!f) return C(0.5, 0.5, 0.5); // Color.gray
      const hex = SG.factionColor ? SG.factionColor(f) : f.color;
      return SG.Gfx.color(hex || '#808080');
    }

    static tileColor(t, r, gr) {
      const Tn = TR();
      let c;
      switch (t) {
        case Tn.Forest: c = gr ? gr.forest : C(0.33, 0.55, 0.3); break;
        case Tn.Hill: c = gr ? gr.hill : C(0.6, 0.62, 0.38); break;
        case Tn.Mountain: c = C(0.52, 0.5, 0.46); break;
        case Tn.River: c = C(0.55, 0.5, 0.38); break;
        case Tn.Wall: c = C(0.62, 0.6, 0.55); break;
        case Tn.Gate: c = C(0.62, 0.58, 0.5); break;
        case Tn.Castle: c = C(0.7, 0.66, 0.56); break;
        default: c = gr ? gr.plain : C(0.47, 0.68, 0.36); break;
      }
      return shade(c, (r - 0.5) * 0.08);
    }

    buildSurroundings(rnd) {
      const m = this.model;
      const mb = new SG.MeshBuilder();
      const BW = m.W * T, BH = m.H * T;
      const size = 3, x0 = -40, z0 = -30, x1 = BW + 40, z1 = BH + 34;
      const ox = ORIGIN.x, oy = ORIGIN.y, oz = ORIGIN.z;
      const h = (px, pz) => {
        const de = Math.max(Math.max(-px, px - BW), Math.max(-pz, pz - BH));
        const v = Math.max(-0.5, (M.perlinNoise(px * 0.08 + 3, pz * 0.08 + 7) - 0.4) * 2 + Math.max(0, de - 6) * 0.25) - 0.3;
        // 紧贴棋盘的一圈压低到木框之下，避免地形穿过棋盘
        return M.lerpUnclamped(-0.42, v, M.smoothStep(0, 1, (de - 0.4) / 3.6));
      };
      const gr = (this.look || BattleLook.of(m.setup || {})).ground;
      const g0 = gr.sur[0], g1 = gr.sur[1];
      for (let x = x0; x < x1; x += size)
        for (let z = z0; z < z1; z += size) {
          // 完全被棋盘覆盖的格子跳过（部分重叠的保留，以免棋盘边缘露出缝隙）
          const inside = x >= -0.01 && z >= -0.01 && x + size <= BW + 0.01 && z + size <= BH + 0.01;
          if (inside) continue;
          const dEdge = Math.max(Math.max(-x, x - BW), Math.max(-z, z - BH));
          const a = V(ox + x, oy + h(x, z), oz + z), b = V(ox + x, oy + h(x, z + size), oz + z + size);
          const c = V(ox + x + size, oy + h(x + size, z + size), oz + z + size), d = V(ox + x + size, oy + h(x + size, z), oz + z);
          const col = lerpColor(g0, g1, M.inverseLerp(4, 16, (a.y + c.y) / 2 + 2));
          mb.tri(a, b, c, shade(col, (rnd.nextDouble() - 0.5) * 0.08));
          mb.tri(a, c, d, shade(col, (rnd.nextDouble() - 0.5) * 0.08));
          if (rnd.nextDouble() < 0.18 && dEdge > 1.5) {
            const tx = x + 1.5, tz = z + 1.5;
            const ts = 1.2 + rnd.nextDouble(), pine = rnd.nextDouble() < 0.5;
            if (gr.palm > 0 && (gr.palm >= 1 || !pine)) BattleLook.palm(mb, ox + tx, oy + h(tx, tz), oz + tz, ts * 1.4, Math.trunc(x * 13 + z));
            else SG.Models.tree(mb, V(ox + tx, oy + h(tx, tz), oz + tz), ts, gr.surLeaf, pine, Math.trunc(x * 13 + z));
          }
        }
      const sur = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      sur.name = 'Surround';
      this.group.add(sur);
      // 棋盘边框
      const frame = new SG.MeshBuilder();
      const wood = C(0.36, 0.24, 0.14);
      frame.box(V(ox + BW / 2, 0.05, oz - 0.25), V(BW + 1, 0.4, 0.5), wood);
      frame.box(V(ox + BW / 2, 0.05, oz + BH + 0.25), V(BW + 1, 0.4, 0.5), wood);
      frame.box(V(ox - 0.25, 0.05, oz + BH / 2), V(0.5, 0.4, BH), wood);
      frame.box(V(ox + BW + 0.25, 0.05, oz + BH / 2), V(0.5, 0.4, BH), wood);
      const fr = SG.Gfx.mesh(frame.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      fr.name = 'Frame';
      this.group.add(fr);
    }

    createUnit(u, col) {
      const uv = new UnitVisual(this, u, col);
      this.visuals.set(u, uv);
      return uv;
    }
    vis(u) { return this.visuals.get(u) || null; }
    isShown(u) { const v = this.visuals.get(u); return !!(v && v.group.visible); }

    // ---------------------------------------------------------- 高亮 --
    clearHighlights() {
      for (const h of this.highlights) this.group.remove(h);
      this.highlights.length = 0;
    }
    highlight(tiles, cssColor, alpha) {
      if (!tiles) return;
      const pc = parseColor(cssColor);
      const a = alpha === undefined || alpha === null ? pc.a : alpha;
      const key = cssHex(pc.color) + '|' + a.toFixed(3);
      let mat = this.hlMats.get(key);
      if (!mat) {
        mat = SG.Gfx.unlit(pc.color, tileTexture(), a);
        mat.userData.baseOpacity = a;
        this.hlMats.set(key, mat);
      }
      for (const t of tiles) {
        if (!t || !this._inBounds(t.x, t.y)) continue;
        const hl = new THREE.Mesh(this.hlGeo, mat);
        hl.position.copy(this.tile(t.x, t.y));
        hl.position.y += 0.06;
        hl.renderOrder = 2;
        this.group.add(hl);
        this.highlights.push(hl);
      }
    }
    setCursor(u) {
      const on = !!u;
      if (this.cursorUnit !== (u || null)) {
        this.cursorUnit = u || null;
        for (const v of this.visuals.values()) v.setSelected(v.u === this.cursorUnit);
      }
      this.cursor.visible = on;
      this.cursorGlow.visible = on;
      if (on) {
        this.cursor.position.copy(this.tile(u.x, u.y));
        this.cursor.position.y += 0.08;
        this.cursorGlow.position.copy(this.cursor.position);
        this.cursorGlow.position.y -= 0.01;
      }
    }

    _inBounds(x, y) { return x >= 0 && y >= 0 && x < this.model.W && y < this.model.H; }

    tileFromScreen(screenX, screenY) {
      if (!SG.Gfx || !SG.Gfx.camera) return null;
      const ray = SG.Gfx.screenRay(screenX, screenY);
      const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -(ORIGIN.y + 0.3));
      const hit = new THREE.Vector3();
      if (!ray.intersectPlane(plane, hit)) return null;
      const ux = hit.x - ORIGIN.x, uz = -hit.z - ORIGIN.z;
      const x = Math.floor(ux / T), y = Math.floor(uz / T);
      if (!this._inBounds(x, y)) return null;
      return { x, y };
    }

    // ---------------------------------------------------------- 每帧 --
    update(dt) {
      if (this.disposed) return;
      if (!(dt >= 0)) dt = 0;
      dt = Math.min(dt, 0.1);
      this.time += dt;
      const time = this.time;
      if (this.cursor.visible) {
        this.cursor.rotation.y -= 40 * M.deg2rad * dt;
        const s = 1 + Math.sin(time * 4) * 0.035;
        this.cursorGlow.scale.set(s, 1, s);
        this.cursorGlow.material.opacity = 0.26 + Math.sin(time * 4) * 0.08;
      }
      // 高亮轻微呼吸
      if (this.highlights.length) {
        const k = 0.88 + Math.sin(time * 3.2) * 0.12;
        for (const mat of this.hlMats.values()) mat.opacity = mat.userData.baseOpacity * k;
      }
      for (const v of this.visuals.values()) v.update(dt, time);
      this._layoutLabels(dt);
      this._updateBurning();
      this._updateEmitters(dt);
      this._updateLights(dt, time);
      const scale = this._pointScale();
      this.glow.update(dt, scale);
      this.flame.update(dt, scale);
      this.heat.update(dt, scale);
      this.dust.update(dt, scale);
      this._spUpdate(dt);         // 必杀技：镜头震动、花瓣粒子
    }

    // ------------------------------------------------------ 标签避让 --
    // 部队头顶标签互相遮挡时（窄屏上格距只有约 30px）按优先级排布：选中部队 > 主将 > 靠近镜头的部队。
    // 先放的标签留在原位；后放的若与已放的重叠，就在小范围内上移 / 下移 / 左右平移到最近的空位
    // （上移时显示一根引线连回部队），实在放不下则淡化并压到下层。层级同样按优先级排列。
    _layoutLabels(dt) {
      if (typeof document === 'undefined' || !SG.Gfx || typeof SG.Gfx.worldToScreen !== 'function') return;
      const key = (window.innerWidth | 0) + 'x' + (window.innerHeight | 0);
      let remeasure = false;
      if (key !== this._lblKey) { this._lblKey = key; remeasure = true; }
      if (this.time - this._lblMeasureT > 2) { this._lblMeasureT = this.time; remeasure = true; }   // 字体加载后的宽度变化
      const items = [];
      let order = 0;
      for (const v of this.visuals.values()) {
        const I = v.info, L = v.lbl;
        order++;
        if (!I || !v.follow) continue;
        if (!(v.labelAlpha > 0.001)) { L.shown = false; continue; }
        const s = SG.Gfx.worldToScreen(v.labelPos);
        if (!s || !s.visible || !(s.dist < 90) || !isFinite(s.x) || !isFinite(s.y)) { L.shown = false; continue; }
        if (I.dirty || remeasure) {
          I.w = I.card.offsetWidth; I.h = I.card.offsetHeight;
          I.gap = (parseFloat(getComputedStyle(I.card).fontSize) || 12) * 0.35;   // .sg-uinfo 的 bottom:.35em
          I.dirty = false;
        }
        const pr = (v.u === this.cursorUnit ? 2 : 0) + (v.u.commander ? 1 : 0);
        items.push({ v, x: s.x, y: s.y, pr, order });
      }
      // 优先级高的先放；同级时靠近镜头（屏幕上更低）的先放，后面的部队标签自然向上让开
      items.sort((a, b) => (b.pr - a.pr) || (b.y - a.y) || (a.order - b.order));
      const placed = [];
      const m = 2;   // 标签之间至少留 2px
      const hits = (list, l, t, r, b) => {
        for (const p of list) if (l < p.r + m && r > p.l - m && t < p.b + m && b > p.t - m) return true;
        return false;
      };
      const k = Math.min(1, dt * 14), kf = Math.min(1, dt * 8);
      for (let rank = 0; rank < items.length; rank++) {
        const it = items[rank], v = it.v, I = v.info, L = v.lbl;
        const w = I.w, h = I.h;
        const l0 = it.x - w / 2, r0 = it.x + w / 2, b0 = it.y - I.gap, t0 = b0 - h;
        let best = null;
        if (!hits(placed, l0, t0, r0, b0)) best = { dx: 0, dy: 0 };
        else {
          const maxUp = h * 1.6 + 4, maxDown = h * 0.45, maxSide = w * 0.42;
          // 只看附近的标签：候选偏移取“贴着它们的边”的位置
          const near = placed.filter(p => p.l < r0 + maxSide + m && p.r > l0 - maxSide - m && p.t < b0 + maxDown + m && p.b > t0 - maxUp - m);
          const dys = [0], dxs = [0];
          for (const p of near) {
            const up = b0 - p.t + m, down = p.b + m - t0, left = p.l - m - r0, right = p.r + m - l0;
            if (up > 0 && up <= maxUp) dys.push(up);
            if (down > 0 && down <= maxDown) dys.push(-down);
            if (left < 0 && -left <= maxSide) dxs.push(left);
            if (right > 0 && right <= maxSide) dxs.push(right);
          }
          let bestCost = Infinity;
          for (const dy of dys)
            for (const dx of dxs) {
              if (hits(near, l0 + dx, t0 - dy, r0 + dx, b0 - dy)) continue;
              // 上移最自然，下移会挡住自己的部队，平移次之；与上一帧的目标接近者优先（防抖）
              const cost = (dy >= 0 ? dy : -dy * 1.8) + Math.abs(dx) * 1.25
                + (Math.abs(dx - L.tdx) + Math.abs(dy - L.tdy)) * 0.3;
              if (cost < bestCost) { bestCost = cost; best = { dx, dy }; }
            }
        }
        if (best) {
          it.rect = { l: l0 + best.dx, r: r0 + best.dx, t: t0 - best.dy, b: b0 - best.dy };
          placed.push(it.rect);
          L.tdx = best.dx; L.tdy = best.dy; L.tfade = 1; L.z = -(1 + rank);
        } else {
          it.rect = null;
          L.tdx = 0; L.tdy = 0; L.tfade = 0.3; L.z = -(1 + items.length + rank);
        }
      }
      for (const it of items) {
        const v = it.v, I = v.info, L = v.lbl;
        if (!L.shown) { L.dx = L.tdx; L.dy = L.tdy; L.fade = L.tfade; L.shown = true; }
        else {
          L.dx += (L.tdx - L.dx) * k; L.dy += (L.tdy - L.dy) * k; L.fade += (L.tfade - L.fade) * kf;
        }
        const tf = 'translate(-50%,0) translate3d(' + L.dx.toFixed(1) + 'px,' + (-L.dy).toFixed(1) + 'px,0)';
        if (tf !== I.tf) { I.card.style.transform = tf; I.tf = tf; }
        // 引线的落点（部队上方）若被别的标签盖住，就不画，免得看起来连到了别人的标签
        let stemOk = L.dy > 2;
        if (stemOk) for (const p of placed) if (p !== it.rect && it.x > p.l && it.x < p.r && it.y > p.t && it.y - I.gap < p.b) { stemOk = false; break; }
        const stemH = stemOk ? Math.round(L.dy + I.gap) : 0;
        if (stemH !== I.stemH) {
          I.stemH = stemH;
          I.stem.style.height = stemH + 'px';
          I.stem.style.opacity = stemH ? M.clamp01((L.dy - 2) / 6).toFixed(2) : '0';
        }
        if (L.z !== I.z) { I.anchor.style.zIndex = String(L.z); I.z = L.z; }
        v.follow.alpha = v.labelAlpha * L.fade;
      }
    }

    _pointScale() {
      const r = SG.Gfx.renderer, cam = SG.Gfx.camera;
      if (!r || !cam) return 600;
      const h = r.domElement.height || 720;
      return h / (2 * Math.tan(cam.fov * M.deg2rad / 2));
    }

    // ---------------------------------------------------------- 动画 --
    // 按帧推进：fn(t)，t 从 0 递增，最后一帧不一定为 1（与 C# for 循环一致）
    async _loop(seconds, fn) {
      let t = 0, last = now();
      while (t < 1) {
        if (this.disposed) return;
        fn(t);
        await SG.frame();
        const n = now();
        t += Math.min(0.1, n - last) / seconds;
        last = n;
      }
    }
    async _sleep(seconds) {
      const end = now() + seconds;
      while (now() < end) {
        if (this.disposed) return;
        await SG.frame();
      }
    }

    async moveUnit(u, path) {
      const v = this.vis(u);
      if (!v || !path) return;
      sfx('march', 0.4);
      for (let i = 1; i < path.length; i++) {
        const a = this.tile(path[i - 1].x, path[i - 1].y), b = this.tile(path[i].x, path[i].y);
        v.face(b.clone().sub(a));
        await this._loop(0.18, t => {
          v.pos.lerpVectors(a, b, t);
          v.pos.y += Math.abs(Math.sin(t * Math.PI)) * 0.12;
          // 行军扬尘
          if (Math.random() < 0.35) this._dustPuff(v.pos, 0.6);
        });
        v.pos.copy(b);
      }
      v.rest();
    }

    async lunge(a, t) {
      const v = this.vis(a);
      if (!v) return;
      const from = this.tile(a.x, a.y), to = this.tile(t.x, t.y);
      v.face(to.clone().sub(from));
      await this._loop(0.28, k => { v.pos.lerpVectors(from, to, Math.sin(k * Math.PI) * 0.38); });
      v.pos.copy(from);
    }

    hit(u, dmg, big) {
      const v = this.vis(u);
      if (!v) return;
      v.flashHit();
      const p = this.tile(u.x, u.y);
      this.burst(p.clone().add(new THREE.Vector3(0, 0.6, 0)), C(1, 0.9, 0.6), 14, 2.5);
      this.floatText(p.clone().add(new THREE.Vector3(0, 1.6, 0)), '-' + dmg, big ? '#ffd14d' : '#ff8c73', big ? 44 : 34);
    }
    refresh(u) { const v = this.vis(u); if (v) v.refresh(); }

    async rout(u) {
      const v = this.vis(u);
      if (!v) return;
      sfx('lose', 0.25);
      this.smoke(this.tile(u.x, u.y).add(new THREE.Vector3(0, 0.3, 0)));
      const p = v.pos.clone();
      await this._loop(0.6, t => { v.pos.set(p.x, p.y - t * 0.8, p.z); });
      v.group.visible = false;
      if (v.info) v.info.card.style.visibility = 'hidden';
      if (v.follow) v.follow.alpha = 0;
    }

    floatText(worldPos, text, cssColor, size) {
      if (!SG.UI || !SG.UI.floatText) return;
      if (size === undefined) size = 34;
      const col = (cssColor && cssColor.isColor) ? cssHex(cssColor) : cssColor;
      const px = Math.max(15, Math.round(size * uiScale()));
      const p = new THREE.Vector3(worldPos.x, worldPos.y, worldPos.z);
      try { SG.UI.floatText(p, text, col, px); } catch (e) { console.error(e); }
    }

    // ---------------------------------------------------------- 粒子 --
    // C# MakeParticles 的一次性爆发：球形发射、寿命 life、初速 speed、大小 size、重力系数 gravity、半径 radius
    _emit(pool, pos, n, o) {
      const col = o.color;
      for (let i = 0; i < n; i++) {
        let px, py, pz, vx, vy, vz;
        if (o.box) {
          px = pos.x + (Math.random() - 0.5) * o.box.x;
          py = pos.y + (Math.random() - 0.5) * o.box.y;
          pz = pos.z + (Math.random() - 0.5) * o.box.z;
          const sp = o.speed * (0.55 + Math.random() * 0.6);
          const j = o.jitter === undefined ? 0.25 : o.jitter;
          vx = (Math.random() - 0.5) * 2 * j; vz = (Math.random() - 0.5) * 2 * j; vy = sp;
        } else {
          const s = inSphere();
          px = pos.x + s.x * o.radius; py = pos.y + s.y * o.radius; pz = pos.z + s.z * o.radius;
          const sp = o.speed * (o.speedVar ? 1 - o.speedVar * Math.random() : 1);
          vx = s.x / s.d * sp; vy = s.y / s.d * sp; vz = s.z / s.d * sp;
        }
        let r = col.r, g = col.g, b = col.b;
        if (o.color2 && Math.random() < (o.mix === undefined ? 0.35 : o.mix)) { r = o.color2.r; g = o.color2.g; b = o.color2.b; }
        const ce = o.colorEnd;
        pool.add({
          x: px, y: py, z: pz, vx, vy, vz, age: 0,
          r1: ce ? ce.r : r, g1: ce ? ce.g : g, b1: ce ? ce.b : b,
          life: o.life * (o.lifeVar ? 1 - o.lifeVar * Math.random() : 1),
          size: o.size * (o.sizeVar ? 1 - o.sizeVar * Math.random() : 1), sizeEnd: o.sizeEnd === undefined ? 0.3 : o.sizeEnd,
          r, g, b, a: o.alpha === undefined ? 1 : o.alpha, grav: o.gravity || 0, drag: o.drag || 0, fadeIn: o.fadeIn || 0,
          ac: o.alphaCurve || 1,
        });
      }
    }
    // 持续发射器（C# loop 粒子）
    _emitter(pool, pos, rate, o, duration) {
      const e = { pool, pos: pos.clone(), rate, o, acc: 0, end: this.time + duration, dead: false };
      this.emitters.push(e);
      return e;
    }
    _updateEmitters(dt) {
      for (let i = this.emitters.length - 1; i >= 0; i--) {
        const e = this.emitters[i];
        if (e.dead || this.time >= e.end) { this.emitters.splice(i, 1); continue; }
        e.acc += e.rate * dt;
        const n = Math.floor(e.acc);
        if (n > 0) { e.acc -= n; this._emit(e.pool, e.pos, n, e.o); }
      }
    }

    burst(worldPos, cssColor, n, speed) {
      const pc = parseColor(cssColor === undefined ? '#ffffff' : cssColor);
      this._emit(this.glow, worldPos, n === undefined ? 14 : n, {
        color: pc.color, alpha: pc.a, life: 0.5, speed: speed === undefined ? 2.5 : speed, size: 0.25, gravity: 0.6, radius: 0.3,
        lifeVar: 0.3, speedVar: 0.35,
      });
      // 中心一闪
      this._emit(this.glow, worldPos, 2, { color: pc.color, alpha: pc.a * 0.8, life: 0.22, speed: 0, size: 1.3, sizeEnd: 0.4, gravity: 0, radius: 0.05 });
    }
    smoke(worldPos) {
      // C#：颜色 (0.5,0.45,0.4,0.6)、寿命 1.4、30 粒、初速 0.8、大小 0.9、重力 -0.05、半径 0.6
      this._emit(this.dust, worldPos, 30, {
        color: C(0.5, 0.45, 0.4), alpha: 0.6, life: 1.4, speed: 0.8, size: 0.9, sizeEnd: 1.7, gravity: -0.05, radius: 0.6,
        lifeVar: 0.25, sizeVar: 0.3, drag: 0.8, fadeIn: 0.08,
      });
    }
    _dustPuff(pos, k) {
      this._emit(this.dust, pos, 1, {
        color: C(0.62, 0.56, 0.45), alpha: 0.35 * k, life: 0.7, speed: 0.35, size: 0.55, sizeEnd: 1.6, gravity: -0.02, radius: 0.45,
        lifeVar: 0.3, drag: 1.2, fadeIn: 0.1,
      });
    }

    _acquireLight() {
      for (const l of this.lights) if (!l.userData.busy) { l.userData.busy = true; l.userData.token = null; return l; }
      const l = this.lights[0];
      l.userData.busy = true; l.userData.token = null;
      return l;
    }
    _updateLights(dt, time) {
      for (const l of this.lights) {
        const d = l.userData;
        const target = d.busy ? d.base : 0;
        d.cur += (target - d.cur) * Math.min(1, dt * (d.busy ? 12 : 3));
        const flicker = 1 + Math.sin(time * 23 + d.seed) * 0.12 + Math.sin(time * 37.7 + d.seed * 2) * 0.08;
        l.intensity = Math.max(0, d.cur * flicker);
        if (d.glow) {
          // 地面火光：常态 0.32，起火第一秒（光源加强时）约 0.58
          d.glow.material.opacity = M.clamp(d.cur / FIRE_LIGHT, 0, FIRE_FLARE) * 0.32 * flicker;
          const s = 1 + Math.sin(time * 9 + d.seed) * 0.05;
          d.glow.scale.set(s, 1, s);
          if (!d.busy && d.cur < 0.02) {
            this.group.remove(d.glow);
            d.glow.material.dispose();
            d.glow = null;
          }
        }
      }
    }

    // 燃烧中的格子：持续的小火苗与烟（model.burning > 0）
    _updateBurning() {
      const m = this.model;
      if (!m.burning) return;
      const seen = new Set();
      for (let x = 0; x < m.W; x++) for (let y = 0; y < m.H; y++) {
        if (!(m.burning[x][y] > 0)) continue;
        const key = x + ',' + y;
        seen.add(key);
        if (this.burnFx.has(key)) continue;
        const p = this.tile(x, y);
        const flame = this._emitter(this.flame, p.clone().add(new THREE.Vector3(0, 0.15, 0)), 16, {
          color: C(1, 0.66, 0.2), colorEnd: C(0.7, 0.14, 0.04), alpha: 0.9, life: 0.75, speed: 1.1, size: 0.55, sizeEnd: 0.35, gravity: -0.2,
          box: { x: 1.5, y: 0.1, z: 1.5 }, jitter: 0.15, lifeVar: 0.4, sizeVar: 0.4, alphaCurve: 2,
        }, 1e9);
        const smk = this._emitter(this.dust, p.clone().add(new THREE.Vector3(0, 0.9, 0)), 3, {
          color: C(0.24, 0.22, 0.2), alpha: 0.32, life: 2.2, speed: 0.7, size: 0.9, sizeEnd: 2.2, gravity: -0.02,
          box: { x: 1.2, y: 0.2, z: 1.2 }, jitter: 0.2, lifeVar: 0.3, drag: 0.4, fadeIn: 0.15,
        }, 1e9);
        this.burnFx.set(key, [flame, smk]);
      }
      for (const [key, es] of this.burnFx) if (!seen.has(key)) { for (const e of es) e.dead = true; this.burnFx.delete(key); }
    }

    async fireFx(t) {
      sfx('fire');
      const p = this.tile(t.x, t.y);
      const base = p.clone().add(new THREE.Vector3(0, 0.2, 0));
      const up = (y) => p.clone().add(new THREE.Vector3(0, y, 0));
      // 起火的一瞬：橙色闪光 + 火星四溅
      this._emit(this.glow, up(0.6), 2, { color: C(1, 0.55, 0.18), alpha: 0.75, life: 0.32, speed: 0, size: 2.6, sizeEnd: 0.6, gravity: 0, radius: 0.1 });
      this._emit(this.glow, up(0.4), 26, {
        color: C(1, 0.85, 0.45), colorEnd: C(1, 0.35, 0.08), alpha: 1, life: 0.7, speed: 3.6, size: 0.16, sizeEnd: 0.5, gravity: 0.5, radius: 0.5,
        lifeVar: 0.4, speedVar: 0.5,
      });
      // 火底热光（叠加，垫在火舌之下）：让火团有亮度、在黄衣部队和亮草地上也能分辨
      const heat = this._emitter(this.heat, base, 30, {
        color: C(1, 0.56, 0.16), colorEnd: C(0.95, 0.24, 0.04), alpha: 0.9, life: 0.6, speed: 0.9, size: 1.3, sizeEnd: 0.7, gravity: -0.1,
        box: { x: 1.3, y: 0.15, z: 1.3 }, jitter: 0.15, lifeVar: 0.3, alphaCurve: 2,
      }, 3.5);
      // 火舌（普通混合、水滴形）。C#：循环粒子 90/秒、寿命 0.9、初速 1.6、大小 0.7、重力 -0.25、盒形 1.6×0.2×1.6、颜色 (1,0.55,0.15)
      // 这里加大火舌并让它在前半生保持不透明 a·(1−t²)，颜色由黄经橙到暗红
      const flames = this._emitter(this.flame, base, 90, {
        color: C(1, 0.66, 0.16), color2: C(1, 0.84, 0.36), mix: 0.3, colorEnd: C(0.72, 0.12, 0.03), alpha: 1,
        life: 0.9, speed: 1.6, size: 1.15, sizeEnd: 0.3, gravity: -0.25, box: { x: 1.6, y: 0.2, z: 1.6 }, jitter: 0.3, lifeVar: 0.35, sizeVar: 0.3,
        alphaCurve: 2,
      }, 3.5);
      // 炽热火芯（叠加，压在火舌之上，贴近地面）
      const core = this._emitter(this.glow, up(0.35), 36, {
        color: C(1, 0.9, 0.55), colorEnd: C(1, 0.45, 0.08), alpha: 0.85, life: 0.45, speed: 1.0, size: 0.8, sizeEnd: 0.35, gravity: -0.2,
        box: { x: 1.2, y: 0.15, z: 1.2 }, jitter: 0.2, lifeVar: 0.3, alphaCurve: 2,
      }, 3.5);
      const embers = this._emitter(this.glow, base, 16, {
        color: C(1, 0.8, 0.4), colorEnd: C(1, 0.35, 0.1), alpha: 1, life: 1.2, speed: 2.6, size: 0.14, sizeEnd: 0.4, gravity: -0.1,
        box: { x: 1.4, y: 0.2, z: 1.4 }, jitter: 0.6, lifeVar: 0.4,
      }, 3.5);
      // 浓烟柱：从火头上方升起，由黑褐渐淡、越升越大
      const smk = this._emitter(this.dust, up(1.3), 16, {
        color: C(0.15, 0.13, 0.12), colorEnd: C(0.4, 0.38, 0.36), alpha: 0.6, life: 2.4, speed: 1.3, size: 1.0, sizeEnd: 3.0, gravity: -0.03,
        box: { x: 1.0, y: 0.3, z: 1.0 }, jitter: 0.25, lifeVar: 0.3, drag: 0.3, fadeIn: 0.12,
      }, 3.5);
      const tv = this.vis(t);
      if (tv) tv.burn();
      const light = this._acquireLight();
      const own = light.userData.token = {};          // 1 秒内若被别的火计借走，就不再改它
      light.position.copy(p).add(new THREE.Vector3(0, 1, 0));
      light.userData.base = FIRE_LIGHT * FIRE_FLARE;
      if (!light.userData.glow) {
        const g = SG.Gfx.mesh(flatDisc(1.7), SG.Gfx.additive(C(1, 0.44, 0.14)), { castShadow: false, receiveShadow: false });
        g.material.opacity = 0;
        g.renderOrder = 3;
        this.group.add(g);
        light.userData.glow = g;
      }
      light.userData.glow.position.copy(p).add(new THREE.Vector3(0, 0.1, 0));
      await this._sleep(1.0);
      // C#：1 秒后 rateOverTime = 25
      flames.rate = 25; heat.rate = 9; core.rate = 10; embers.rate = 5; smk.rate = 6;
      if (light.userData.token !== own) return;
      light.userData.base = FIRE_LIGHT;
      // C#：Destroy(ps.gameObject, 2.5f) —— 2.5 秒后熄灭
      const token = light.userData.token = {};
      setTimeout(() => { if (light.userData.token === token) light.userData.busy = false; }, 2500);
    }

    async rockFx(t) {
      const p = this.tile(t.x, t.y);
      const rocks = [];
      const mat = SG.Gfx.lowPoly();
      for (let i = 0; i < 6; i++) {
        const mb = new SG.MeshBuilder();
        const r = 0.25 + Math.random() * 0.2;
        mb.blob(V(0, 0, 0), V(r, r, r), C(0.55, 0.52, 0.48), i);
        const go = SG.Gfx.mesh(mb.toGeometry(), mat, { castShadow: true, receiveShadow: true });
        go.position.set(p.x + (Math.random() - 0.5) * 1.4, p.y + 7 + i * 0.8, p.z + (Math.random() - 0.5) * 1.4);
        go.userData.landed = false;
        this.group.add(go);
        this.temp.push(go);
        rocks.push(go);
      }
      let t0 = 0, last = now();
      while (t0 < 0.9) {
        if (this.disposed) return;
        const dt = Math.min(0.1, now() - last);
        last = now();
        for (const r of rocks) {
          if (r.position.y > p.y + 0.2) {
            r.position.y -= dt * 14;
            if (r.position.y <= p.y + 0.2) {
              r.position.y = p.y + 0.2;
              if (!r.userData.landed) { r.userData.landed = true; this._rockImpact(r.position); }
            }
          }
          r.rotation.x += 200 * M.deg2rad * dt;
          r.rotation.y -= 90 * M.deg2rad * dt;
        }
        await SG.frame();
        t0 += dt;
      }
      sfx('rock');
      this.smoke(p);
      await this._sleep(0.3);
      for (const r of rocks) {
        this.group.remove(r);
        r.geometry.dispose();
        const k = this.temp.indexOf(r);
        if (k >= 0) this.temp.splice(k, 1);
      }
    }
    _rockImpact(pos) {
      this._emit(this.dust, pos, 6, {
        color: C(0.58, 0.52, 0.44), alpha: 0.5, life: 0.8, speed: 1.6, size: 0.5, sizeEnd: 1.5, gravity: 0.15, radius: 0.25,
        lifeVar: 0.3, drag: 2.0,
      });
      this._emit(this.glow, pos, 4, { color: C(1, 0.85, 0.6), alpha: 0.7, life: 0.3, speed: 2.4, size: 0.14, gravity: 0.8, radius: 0.1 });
    }

    async magicFx(t, cssColor) {
      sfx('magic', 0.6);
      const pc = parseColor(cssColor === undefined ? '#bf80ff' : cssColor);
      const p = this.tile(t.x, t.y);
      const ring = SG.Gfx.mesh(flatDisc(1.2), SG.Gfx.unlit(pc.color, SG.Gfx.ringTexture, pc.a), { castShadow: false, receiveShadow: false });
      ring.position.copy(p).add(new THREE.Vector3(0, 0.15, 0));
      ring.renderOrder = 4;
      const halo = SG.Gfx.mesh(flatDisc(1.2), SG.Gfx.additive(pc.color), { castShadow: false, receiveShadow: false });
      halo.position.copy(ring.position);
      halo.renderOrder = 3;
      this.group.add(ring); this.group.add(halo);
      this.temp.push(ring, halo);
      // C#：寿命 0.9、40 粒、初速 1.4、大小 0.3、重力 -0.4、半径 0.6
      this._emit(this.glow, p.clone().add(new THREE.Vector3(0, 0.5, 0)), 40, {
        color: pc.color, color2: shade(pc.color, 0.5), mix: 0.3, alpha: 1, life: 0.9, speed: 1.4, size: 0.3, gravity: -0.4, radius: 0.6, lifeVar: 0.3,
      });
      await this._loop(0.9, k => {
        const s = 0.5 + k * 1.4;
        ring.scale.set(s, 1, s);
        ring.rotation.y = -300 * M.deg2rad * k * 0.9;
        halo.scale.set(s * 0.9, 1, s * 0.9);
        halo.material.opacity = 0.5 * (1 - k);
      });
      for (const o of [ring, halo]) {
        this.group.remove(o);
        o.material.dispose();
        const i = this.temp.indexOf(o);
        if (i >= 0) this.temp.splice(i, 1);
      }
    }

    // ---------------------------------------------------------- 释放 --
    dispose() {
      if (this.disposed) return;
      this.disposed = true;
      for (const v of this.visuals.values()) v.dispose();
      this.visuals.clear();
      this.clearHighlights();
      for (const mat of this.hlMats.values()) mat.dispose();
      for (const mat of this.sideRing) mat.dispose();
      this.hlMats.clear();
      this.emitters.length = 0;
      this.burnFx.clear();
      this.glow.dispose(); this.flame.dispose(); this.heat.dispose(); this.dust.dispose();
      if (this._petals) { this._petals.dispose(); this._petals = null; }
      for (const el of [this._flashEl, this._dimEl]) if (el && el.parentNode) el.parentNode.removeChild(el);
      this._flashEl = this._dimEl = null;
      if (this._shkLast && SG.Gfx && SG.Gfx.camera && SG.Gfx.camera.position.equals(this._shkLast.pos)) SG.Gfx.camera.position.sub(this._shkLast.off);
      this._shk = this._shkLast = null;
      for (const l of this.lights) { l.intensity = 0; if (l.dispose) l.dispose(); }
      const shared = new Set([SG.Gfx.lowPoly(), SG.Gfx.water()]);
      this.group.traverse(o => {
        if (o.geometry && !discCacheHas(o.geometry) && o.geometry !== this.hlGeo) o.geometry.dispose();
        if (o.material && !shared.has(o.material) && !(o.isPoints)) {
          if (Array.isArray(o.material)) o.material.forEach(mm => mm.dispose()); else o.material.dispose();
        }
      });
      if (this.group.parent) this.group.parent.remove(this.group);
    }
  }
  function discCacheHas(g) { for (const v of discCache.values()) if (v === g) return true; return false; }


  // ======================================================== 必杀技特效 --
  // 第二版新增（DESIGN-V2 §4D），只增不改：
  //   V.specialFx(u, targets, fx, info) → Promise（约 1–1.5 秒）
  //     u：施展者；targets：受影响的部队（主目标在前）；fx：风格名或 { style, color }；
  //     info（可省略）：{ sp, res（Mdl.useSpecial 的结果）, color, target, onHit(i) }。
  //     命中瞬间调用 info.onHit(i)（i 为 res.hits 的下标）——控制层借此在命中时显示伤害数字；
  //     突击 / 击退的位移按 res.moved / res.pushed 播放，结束时部队已在新格。
  //   风格：slash 斩击弧光 · dragon 青龙 · havoc 无双 · sweep 横扫 · dash 突击残影 · whirl 往来连斩 ·
  //         arrows 箭雨 · arrow 一箭穿杨 · fire 火海 · wind 风助火势 · lightning 雷击 · water 水淹 ·
  //         shock 怒吼冲击波 · aura 金光 · blossom 桃花 · spirit 符咒 · shield 护盾 · heal 治愈之光 ·
  //         poison 毒雾 · shadow 暗影刺杀 · drain 吸魂 · haste 疾风 · claw 猛虎爪痕 · rock 落石（未知风格按 slash）
  //   UnitVisual 另显示限时加成：脚下彩色光环 + 名牌上的小字（守 / 攻 / 毒 / 疾）。
  const SP_GLYPH = {
    slash: '斩', dragon: '龙', havoc: '霸', sweep: '扫', dash: '突', whirl: '闪', arrows: '箭', arrow: '穿', fire: '火', wind: '风',
    lightning: '雷', water: '水', shock: '喝', aura: '令', blossom: '义', spirit: '谋', shield: '守', heal: '愈', poison: '毒',
    shadow: '杀', drain: '收', haste: '疾', claw: '虎', rock: '石',
  };
  const SP_FONT = '"STKaiti","KaiTi","Kaiti SC","Songti SC","Noto Serif SC",serif,"WenQuanYi Zen Hei"';
  const spTexCache = {};
  function spCanvasTex(key, w, h, draw) {
    if (spTexCache[key]) return spTexCache[key];
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    draw(c.getContext('2d'), w, h);
    const t = new THREE.CanvasTexture(c);
    t.generateMipmaps = true;
    t.minFilter = THREE.LinearMipmapLinearFilter;
    t.magFilter = THREE.LinearFilter;
    t.needsUpdate = true;
    spTexCache[key] = t;
    return t;
  }
  // 书法大字：墨色描边 + 白色字芯（材质颜色着色）
  function spGlyphTex(ch) {
    return spCanvasTex('glyph:' + ch, 256, 256, (g, w, h) => {
      g.font = '700 196px ' + SP_FONT;
      g.textAlign = 'center'; g.textBaseline = 'middle';
      g.lineJoin = 'round';
      g.strokeStyle = 'rgba(20,10,6,0.92)'; g.lineWidth = 22;
      g.strokeText(ch, w / 2, h / 2 + 8);
      g.fillStyle = '#ffffff';
      g.fillText(ch, w / 2, h / 2 + 8);
    });
  }
  function spGlyphGlowTex(ch) {
    return spCanvasTex('glow:' + ch, 256, 256, (g, w, h) => {
      g.font = '700 196px ' + SP_FONT;
      g.textAlign = 'center'; g.textBaseline = 'middle';
      g.shadowColor = '#ffffff'; g.shadowBlur = 34;
      g.fillStyle = '#ffffff';
      for (let i = 0; i < 3; i++) g.fillText(ch, w / 2, h / 2 + 8);
    });
  }
  // 符纸：黄纸朱框，竖写「敕令」，下接曲折符脚
  function spTalismanTex() {
    return spCanvasTex('talisman', 64, 160, (g, w, h) => {
      g.fillStyle = '#f2df9a'; g.fillRect(4, 2, w - 8, h - 4);
      g.strokeStyle = '#c3261c'; g.lineWidth = 3; g.strokeRect(9, 7, w - 18, h - 14);
      g.fillStyle = '#c3261c'; g.font = '700 30px ' + SP_FONT; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText('敕', w / 2, 32); g.fillText('令', w / 2, 68);
      g.lineWidth = 3.5; g.lineJoin = 'round'; g.beginPath(); g.moveTo(w / 2, 90);
      for (let i = 0; i < 6; i++) g.lineTo(w / 2 + (i % 2 ? 10 : -10), 96 + i * 9);
      g.stroke();
    });
  }
  // 漩涡：三条渐粗的螺旋臂
  function spSwirlTex() {
    return spCanvasTex('swirl', 256, 256, (g, w) => {
      const c = w / 2;
      g.strokeStyle = '#fff'; g.shadowColor = '#fff'; g.shadowBlur = 6; g.lineCap = 'round';
      for (let arm = 0; arm < 3; arm++) {
        for (let i = 0; i < 48; i++) {
          const t0 = i / 48, t1 = (i + 1) / 48;
          const p = t => { const a = arm * 2.094 + t * 5.2, r = 10 + t * 112; return [c + Math.cos(a) * r, c + Math.sin(a) * r]; };
          const [x0, y0] = p(t0), [x1, y1] = p(t1);
          g.lineWidth = 2 + t0 * 9; g.globalAlpha = 0.35 + t0 * 0.65;
          g.beginPath(); g.moveTo(x0, y0); g.lineTo(x1, y1); g.stroke();
        }
      }
    });
  }
  // 风痕：两端透明、中段明亮的细长光带
  function spStreakTex() {
    return spCanvasTex('streak', 128, 16, (g, w, h) => {
      const gr = g.createLinearGradient(0, 0, w, 0);
      gr.addColorStop(0, 'rgba(255,255,255,0)'); gr.addColorStop(0.7, 'rgba(255,255,255,0.9)'); gr.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = gr; g.fillRect(0, 3, w, h - 6);
    });
  }
  // 符阵：同心圆 + 八卦短划 + 星形
  function spCircleTex() {
    return spCanvasTex('circle', 512, 512, (g, w) => {
      const c = w / 2;
      g.strokeStyle = '#fff'; g.fillStyle = '#fff';
      g.shadowColor = '#fff'; g.shadowBlur = 8;
      const ring = (r, lw) => { g.lineWidth = lw; g.beginPath(); g.arc(c, c, r, 0, Math.PI * 2); g.stroke(); };
      ring(240, 6); ring(222, 2.5); ring(150, 4); ring(92, 2.5);
      for (let i = 0; i < 8; i++) {
        const a = i / 8 * Math.PI * 2;
        for (let k = 0; k < 3; k++) {
          const r0 = 168 + k * 16, broken = ((i >> k) & 1) === 1;
          const half = broken ? 0.07 : 0.16;
          g.lineWidth = 7;
          g.beginPath(); g.arc(c, c, r0, a - 0.16, a - 0.16 + (broken ? half : half * 2)); g.stroke();
          if (broken) { g.beginPath(); g.arc(c, c, r0, a + 0.16 - half, a + 0.16); g.stroke(); }
        }
      }
      g.lineWidth = 3;
      g.beginPath();
      for (let i = 0; i <= 5; i++) { const a = -Math.PI / 2 + i * Math.PI * 4 / 5; const x = c + Math.cos(a) * 140, y = c + Math.sin(a) * 140; if (i === 0) g.moveTo(x, y); else g.lineTo(x, y); }
      g.stroke();
    });
  }
  // 花瓣（点精灵）
  function spPetalTex() {
    return spCanvasTex('petal', 64, 64, (g) => {
      g.fillStyle = '#fff';
      g.beginPath();
      g.moveTo(32, 6);
      g.bezierCurveTo(56, 18, 54, 46, 32, 58);
      g.bezierCurveTo(10, 46, 8, 18, 32, 6);
      g.fill();
      g.globalCompositeOperation = 'destination-out';
      g.beginPath(); g.moveTo(32, 4); g.lineTo(27, 14); g.lineTo(37, 14); g.fill();
    });
  }
  // 十字光（治愈）
  function spPlusTex() {
    return spCanvasTex('plus', 64, 64, (g) => {
      const grd = g.createRadialGradient(32, 32, 2, 32, 32, 30);
      grd.addColorStop(0, 'rgba(255,255,255,1)'); grd.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = grd;
      g.fillRect(26, 6, 12, 52); g.fillRect(6, 26, 52, 12);
    });
  }

  const SP_RIBBON_VS = `
varying vec2 vUv;
void main() { vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }`;
  // 弧光：月牙形刀光（两端尖、中段厚），外缘白热；uSoft > 0 时为柔和的外层辉光
  const SP_ARC_FS = `
uniform vec3 uColor; uniform float uHead; uniform float uLen; uniform float uOpacity; uniform float uSoft;
varying vec2 vUv;
void main() {
  float d = uHead - vUv.x;
  if (d < 0.0 || d > uLen) discard;
  float rel = 1.0 - d / uLen;
  float th = pow(sin(rel * 3.14159), 0.75) * 0.92;
  float y = vUv.y;
  float e = 0.04 + uSoft * 0.35;
  float inside = smoothstep(1.0 - th - e, 1.0 - th + e * 0.5, y) * smoothstep(1.0, 1.0 - e - 0.02, y);
  float hot = smoothstep(1.0 - th * 0.4, 1.0, y) * (1.0 - uSoft);
  float a = inside * uOpacity * (0.6 + 0.4 * rel);
  gl_FragColor = vec4(mix(uColor, vec3(1.0), hot * 0.9), a);
}`;
  // 闪电 / 拖尾：横向高斯，芯白
  const SP_BOLT_FS = `
uniform vec3 uColor; uniform float uOpacity;
varying vec2 vUv;
void main() {
  float x = (vUv.y - 0.5) * 2.0;
  float core = exp(-x * x * 26.0);
  float glow = exp(-x * x * 3.0) * 0.55;
  float along = smoothstep(0.0, 0.08, vUv.x) * smoothstep(1.0, 0.92, vUv.x);
  float a = (core + glow) * uOpacity * mix(0.6, 1.0, along);
  gl_FragColor = vec4(mix(uColor, vec3(1.0), core), a);
}`;
  // 光柱：自下而上渐隐，螺旋条纹流动
  const SP_PILLAR_FS = `
uniform vec3 uColor; uniform float uOpacity; uniform float uTime;
varying vec2 vUv;
void main() {
  float fade = pow(1.0 - vUv.y, 1.5) * smoothstep(0.0, 0.05, vUv.y);
  float st = 0.6 + 0.4 * sin(vUv.x * 6.2831 * 5.0 + vUv.y * 9.0 - uTime * 7.0);
  float a = fade * st * uOpacity;
  gl_FragColor = vec4(mix(uColor, vec3(1.0), 0.45 * fade), a);
}`;
  // 水墙：浪身半透明、浪尖泛白
  const SP_WAVE_FS = `
uniform vec3 uColor; uniform float uOpacity; uniform float uTime;
varying vec2 vUv;
void main() {
  float y = vUv.y + 0.06 * sin(vUv.x * 40.0 + uTime * 9.0);
  float crest = smoothstep(0.62, 0.9, y) * (1.0 - smoothstep(0.93, 1.0, y));
  float body = smoothstep(0.0, 0.2, y) * (1.0 - smoothstep(0.85, 1.0, y)) * (0.55 + 0.25 * sin(vUv.x * 70.0 - uTime * 6.0));
  float edge = sin(clamp(vUv.x, 0.0, 1.0) * 3.14159);
  float a = (body * 0.85 + crest) * edge * uOpacity;
  gl_FragColor = vec4(mix(uColor, vec3(1.0), crest * 0.85), a);
}`;
  // 护盾：菲涅耳边缘 + 上升扫描环 + 六角网格
  const SP_DOME_VS = `
varying vec3 vN; varying vec3 vV; varying vec3 vP;
void main() {
  vec4 mv = modelViewMatrix * vec4(position, 1.0);
  vN = normalize(normalMatrix * normal); vV = normalize(-mv.xyz); vP = position;
  gl_Position = projectionMatrix * mv;
}`;
  const SP_DOME_FS = `
uniform vec3 uColor; uniform float uOpacity; uniform float uScan;
varying vec3 vN; varying vec3 vV; varying vec3 vP;
void main() {
  float f = pow(1.0 - abs(dot(vN, vV)), 2.0);
  float band = exp(-pow((vP.y - uScan) * 10.0, 2.0));
  float ang = atan(vP.z, vP.x) * 6.0 / 3.14159;
  float lat = vP.y * 9.0;
  float hex = max(smoothstep(0.86, 1.0, abs(fract(ang + floor(lat) * 0.5) * 2.0 - 1.0)), smoothstep(0.82, 1.0, abs(fract(lat) * 2.0 - 1.0)));
  float a = (0.08 + f * 0.85 + band * 0.9 + hex * 0.22 * (0.4 + f)) * uOpacity;
  gl_FragColor = vec4(mix(uColor, vec3(1.0), clamp(band * 0.7 + f * 0.3, 0.0, 1.0)), a);
}`;

  // normal = true 时用普通混合：白天明亮的草地上，纯叠加的光会被洗成一片白，实色笔触才看得清
  function spShader(fs, color, extra, vs, normal) {
    const uniforms = Object.assign({ uColor: { value: color.clone() }, uOpacity: { value: 1 }, uTime: { value: 0 } }, extra || {});
    return new THREE.ShaderMaterial({
      uniforms, vertexShader: vs || SP_RIBBON_VS, fragmentShader: fs,
      transparent: true, depthWrite: false, blending: normal ? THREE.NormalBlending : THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false,
    });
  }
  // 弧形带（局部 XY 平面，法线 +z；uv.x 沿弧 0..1，uv.y 内缘 0 → 外缘 1）
  function spArcGeometry(radius, width, start, sweep, segs) {
    const n = segs || 40;
    const pos = [], uv = [], idx = [];
    for (let i = 0; i <= n; i++) {
      const t = i / n, a = start + sweep * t;
      const ca = Math.cos(a), sa = Math.sin(a);
      const r0 = radius - width * 0.5, r1 = radius + width * 0.5;
      pos.push(ca * r0, sa * r0, 0, ca * r1, sa * r1, 0);
      uv.push(t, 0, t, 1);
      if (i < n) { const k = i * 2; idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2); }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setIndex(idx);
    return g;
  }
  // 竖直的弧形墙（局部 XZ 平面上的弧向上拉伸；uv.x 沿弧，uv.y 自下而上）
  function spWallGeometry(radius, height, start, sweep, segs) {
    const n = segs || 40;
    const pos = [], uv = [], idx = [];
    for (let i = 0; i <= n; i++) {
      const t = i / n, a = start + sweep * t;
      const x = Math.cos(a) * radius, z = Math.sin(a) * radius;
      pos.push(x, 0, z, x, height, z);
      uv.push(t, 0, t, 1);
      if (i < n) { const k = i * 2; idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2); }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setIndex(idx);
    return g;
  }
  // 沿折线的带子，朝向镜头（uv.x 沿线，uv.y 横向）
  function spRibbonGeometry(points, width, taper) {
    const cam = SG.Gfx && SG.Gfx.camera ? SG.Gfx.camera.position : new THREE.Vector3(0, 40, 40);
    const pos = [], uv = [], idx = [];
    let len = 0;
    const L = [0];
    for (let i = 1; i < points.length; i++) { len += points[i].distanceTo(points[i - 1]); L.push(len); }
    const tan = new THREE.Vector3(), view = new THREE.Vector3(), side = new THREE.Vector3();
    for (let i = 0; i < points.length; i++) {
      const p = points[i];
      tan.subVectors(points[Math.min(points.length - 1, i + 1)], points[Math.max(0, i - 1)]).normalize();
      view.subVectors(cam, p).normalize();
      side.crossVectors(tan, view).normalize().multiplyScalar(width * 0.5 * (taper ? taper(points.length > 1 ? i / (points.length - 1) : 1) : 1));
      pos.push(p.x - side.x, p.y - side.y, p.z - side.z, p.x + side.x, p.y + side.y, p.z + side.z);
      const t = len > 0 ? L[i] / len : 0;
      uv.push(t, 0, t, 1);
      if (i < points.length - 1) { const k = i * 2; idx.push(k, k + 1, k + 2, k + 1, k + 3, k + 2); }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setIndex(idx);
    return g;
  }
  function spEase(t) { t = M.clamp01(t); return 1 - Math.pow(1 - t, 3); }
  function spBezier(a, b, c, t) {
    const u = 1 - t;
    return new THREE.Vector3(u * u * a.x + 2 * u * t * b.x + t * t * c.x, u * u * a.y + 2 * u * t * b.y + t * t * c.y, u * u * a.z + 2 * u * t * b.z + t * t * c.z);
  }
  function spInjectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-spfx-style')) return;
    const s = document.createElement('style');
    s.id = 'sg-spfx-style';
    s.textContent = `
.sg-spfx-flash{position:fixed;inset:0;z-index:5;pointer-events:none;opacity:0;}
.sg-uinfo .sg-spbs{position:absolute;right:-.45em;top:-.62em;display:flex;gap:.12em;pointer-events:none;}
.sg-uinfo .sg-spb{display:block;padding:0 .26em;border-radius:.3em;font-size:.7em;line-height:1.35;font-weight:700;
  color:#14121a;text-shadow:none;box-shadow:0 0 .35em rgba(0,0,0,.55);}
`;
    (document.head || document.documentElement).appendChild(s);
  }

  // ---------------------------------------------------- UnitVisual：加成显示 --
  const SP_BUFF = {      // 加成种类 → 名牌小字与光环颜色
    def: { ch: '守', col: '#8cc8ff' }, atk: { ch: '攻', col: '#ffcf5a' }, dot: { ch: '毒', col: '#9be06a' }, move: { ch: '疾', col: '#7affd9' },
  };
  function spBuffKinds(u) {
    const out = [];
    for (const m of u.mods || []) {
      if (m.dot > 0 && out.indexOf('dot') < 0) out.push('dot');
      if ((m.def > 1 || m.counter > 1) && out.indexOf('def') < 0) out.push('def');
      if (m.atk > 1 && out.indexOf('atk') < 0) out.push('atk');
      if (m.move > 0 && out.indexOf('move') < 0) out.push('move');
    }
    return out;
  }
  UnitVisual.prototype._spRefresh = function () {
    const u = this.u;
    const kinds = u.alive ? spBuffKinds(u) : [];
    const key = kinds.join(',');
    if (key === (this._spKey || '')) return;
    this._spKey = key;
    if (this.info) {
      spInjectStyle();
      const I = this.info;
      if (!I.spb) { I.spb = document.createElement('span'); I.spb.className = 'sg-spbs'; I.card.appendChild(I.spb); }
      I.spb.innerHTML = kinds.map(k => '<span class="sg-spb" style="background:' + SP_BUFF[k].col + '">' + SP_BUFF[k].ch + '</span>').join('');
      I.spb.style.display = kinds.length ? '' : 'none';
    }
    if (this.buffRing) {
      this.group.remove(this.buffRing);
      this.buffRing.material.dispose();
      this.buffRing = null;
    }
    if (kinds.length) {
      const col = new THREE.Color(SP_BUFF[kinds[0]].col);
      const ring = SG.Gfx.mesh(flatDisc(1.05), SG.Gfx.unlit(col, SG.Gfx.ringTexture, 0.85), { castShadow: false, receiveShadow: false });
      ring.position.y = 0.07;
      ring.renderOrder = 3;
      this.group.add(ring);
      this.buffRing = ring;
    }
  };
  UnitVisual.prototype._spUpdate = function (dt, time) {
    if (this.buffRing) {
      this.buffRing.rotation.y += 60 * M.deg2rad * dt;
      const s = 1 + Math.sin(time * 3 + this.heatSeed) * 0.05;
      this.buffRing.scale.set(s, 1, s);
    }
    // 必杀技的着色光（金光、护盾等）：1 → 0 渐退，盖过受击闪白
    if (this.spTint > 0) {
      this.spTint = Math.max(0, this.spTint - dt * 1.1);
      const k = M.smoothStep(0, 1, this.spTint) * 0.6;
      const fc = this.material.flashColor;
      if (k > this.material.flash && fc && fc.isColor && this.spTintColor) { fc.copy(this.spTintColor); this.material.flash = k; }
    }
  };
  UnitVisual.prototype.tint = function (cssColor, k) {
    this.spTintColor = parseColor(cssColor).color;
    this.spTint = k === undefined ? 1 : k;
  };
  UnitVisual.prototype._spDispose = function () {
    if (this.buffRing) { this.buffRing.material.dispose(); this.buffRing = null; }
  };

  // ---------------------------------------------------- BattleView：工具 --
  const BV = BattleView.prototype;
  BV._spAdd = function (obj, order) {
    if (order !== undefined) obj.renderOrder = order;
    this.group.add(obj);
    this.temp.push(obj);
    return obj;
  };
  BV._spFree = function (obj) {
    if (!obj) return;
    if (obj.parent) obj.parent.remove(obj);
    const i = this.temp.indexOf(obj);
    if (i >= 0) this.temp.splice(i, 1);
    if (obj.geometry && !obj.userData.keepGeo && !discCacheHas(obj.geometry)) obj.geometry.dispose();
    if (obj.material && !obj.userData.keepMat) obj.material.dispose();
  };
  // 按帧推进（与 _loop 相同，但保证最后一帧 fn(1)）
  BV._spAnim = async function (seconds, fn) {
    await this._loop(seconds, fn);
    if (!this.disposed) fn(1);
  };
  BV._spPos = function (u, y) {
    const v = this.vis(u);
    const p = v ? v.pos.clone() : this.tile(u.x, u.y);
    if (y) p.y += y;
    return p;
  };
  BV._spColor = function (c) { return parseColor(c).color; };
  // 镜头震动（在 update 里叠加到相机位置上；相机由 CameraRig 每帧重新摆位）
  BV._spShake = function (amp, dur) {
    if (!this._shk || this._shk.amp * (1 - this._shk.t / this._shk.dur) < amp) this._shk = { amp, dur, t: 0 };
  };
  // 全屏闪光（画布之上、界面之下）
  BV._spFlash = function (cssColor, alpha, dur) {
    if (typeof document === 'undefined') return;
    spInjectStyle();
    if (!this._flashEl) {
      this._flashEl = document.createElement('div');
      this._flashEl.className = 'sg-spfx-flash';
      document.body.appendChild(this._flashEl);
    }
    const el = this._flashEl;
    el.style.background = cssColor;
    if (el.animate) el.animate([{ opacity: alpha }, { opacity: 0 }], { duration: (dur || 0.3) * 1000, easing: 'ease-out' });
  };
  // 场景压暗（雷击、暗杀）：hold 秒后恢复
  BV._spDim = function (alpha, hold) {
    if (typeof document === 'undefined') return;
    spInjectStyle();
    if (!this._dimEl) {
      this._dimEl = document.createElement('div');
      this._dimEl.className = 'sg-spfx-flash';
      this._dimEl.style.background = 'radial-gradient(ellipse at 50% 45%, rgba(10,12,30,.55), rgba(4,4,12,.92))';
      document.body.appendChild(this._dimEl);
    }
    const el = this._dimEl;
    if (el.animate) el.animate([{ opacity: 0 }, { opacity: alpha, offset: 0.15 }, { opacity: alpha, offset: 0.8 }, { opacity: 0 }], { duration: (hold || 1) * 1000, easing: 'ease-in-out' });
  };
  // 点光源一闪（借用空闲的火光灯；没有空闲的就不闪）
  BV._spLight = function (pos, cssColor, peak, dur) {
    const l = this.lights.find(x => !x.userData.busy);
    if (!l) return;
    l.userData.busy = true;
    const token = l.userData.token = {};
    const old = l.color.clone();
    l.color.copy(this._spColor(cssColor));
    l.position.copy(pos);
    l.userData.base = peak;
    l.userData.cur = peak;
    setTimeout(() => {
      if (l.userData.token !== token) return;
      l.userData.busy = false;
      l.color.copy(old);          // 立即还原颜色：随后若被火计借用，火光仍是橙色
    }, (dur || 0.3) * 1000);
  };
  BV._spGlyph = function (pos, ch, cssColor, size) {
    if (typeof document === 'undefined' || !ch) return;
    const col = this._spColor(cssColor);
    const s = (size || 3.4) * 0.56;
    pos = pos.clone();
    pos.y += 0.9;
    const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: spGlyphGlowTex(ch), color: col, transparent: true, blending: THREE.AdditiveBlending, depthTest: false, depthWrite: false, fog: false }));
    const ink = new THREE.Sprite(new THREE.SpriteMaterial({ map: spGlyphTex(ch), color: SG.Gfx.shade(col, 0.35), transparent: true, depthTest: false, depthWrite: false, fog: false }));
    glow.position.copy(pos); ink.position.copy(pos);
    this._spAdd(glow, 30); this._spAdd(ink, 31);
    // 目标在棋盘上方几排时，字的上沿可能出画：每帧把字的上沿投影到屏幕，超出 y≈0.86（NDC）就整体下移
    // （镜头此时仍在缓动对焦，故逐帧检查；只下移不回弹，最低不压到目标头顶以下）
    const cam = SG.Gfx && SG.Gfx.camera;
    const tmp = new THREE.Vector3(), up = new THREE.Vector3(), fwd = new THREE.Vector3();
    const minY = pos.y - 2.3;
    let drop = 0;
    const fit = (y, sc) => {
      if (!cam || !cam.isPerspectiveCamera) return 0;
      cam.updateMatrixWorld();
      up.set(0, 1, 0).applyQuaternion(cam.quaternion);
      fwd.set(0, 0, -1).applyQuaternion(cam.quaternion);
      tmp.set(pos.x, y, pos.z).addScaledVector(up, sc * 0.42);
      const depth = tmp.clone().sub(cam.position).dot(fwd);
      if (depth <= 0.1) return 0;
      const over = tmp.project(cam).y - 0.86;
      if (over <= 0 || up.y < 0.2) return 0;
      return over * depth * Math.tan(cam.fov * Math.PI / 360) / up.y;
    };
    this._spAnim(1.0, t => {
      const pop = t < 0.14 ? 1.5 - 0.5 * spEase(t / 0.14) : 1 + (t - 0.14) * 0.12;
      glow.scale.set(s * pop * 1.15, s * pop * 1.15, 1); ink.scale.set(s * pop, s * pop, 1);
      const a = t < 0.6 ? 1 : 1 - (t - 0.6) / 0.4;
      ink.material.opacity = a * 0.95; glow.material.opacity = a * 0.55 * (t < 0.14 ? 1 : 0.6);
      const y0 = pos.y + t * 0.5;
      drop = Math.min(drop + fit(y0 - drop, s * pop), y0 - minY);
      glow.position.y = ink.position.y = y0 - drop;
    }).then(() => { this._spFree(glow); this._spFree(ink); });
  };
  // 地面光环：r0 → r1 扩散并淡出
  BV._spRing = function (pos, cssColor, r0, r1, dur, opts) {
    const o = opts || {};
    const col = this._spColor(cssColor);
    const ring = SG.Gfx.mesh(flatDisc(1), o.additive ? SG.Gfx.additive(col) : SG.Gfx.unlit(col, o.tex || SG.Gfx.ringTexture, 1), { castShadow: false, receiveShadow: false });
    // normal：普通混合（深色 / 冷色的符阵在明亮草地上保持本色），否则叠加发光
    ring.material.blending = o.normal ? THREE.NormalBlending : THREE.AdditiveBlending;
    ring.position.copy(pos);
    ring.position.y += o.y === undefined ? 0.12 : o.y;
    if (o.vertical) { ring.rotation.x = Math.PI / 2 - 0.85; }
    this._spAdd(ring, 6);
    return this._spAnim(dur || 0.5, t => {
      const k = spEase(t);
      const r = r0 + (r1 - r0) * k;
      ring.scale.set(r, 1, r);
      if (o.spin) ring.rotation.y = t * o.spin;
      ring.material.opacity = (o.alpha === undefined ? 1 : o.alpha) * (1 - Math.pow(t, o.hold ? 3 : 1.4));
    }).then(() => this._spFree(ring));
  };
  // 斩击弧光：在 pos 处、绕镜头方向的平面内扫过（tilt 为弧面倾角，弧度）
  BV._spArc = function (pos, cssColor, o) {
    o = o || {};
    const col = this._spColor(cssColor);
    const r = o.radius || 1.4, w = o.width || 0.5, st = o.start === undefined ? -0.4 : o.start, sw = o.sweep || 2.6;
    // 实色刀光（普通混合）+ 外层叠加辉光
    const mat = spShader(SP_ARC_FS, col, { uHead: { value: 0 }, uLen: { value: o.len || 0.75 }, uSoft: { value: 0 } }, null, !o.glowOnly);
    const m = new THREE.Mesh(spArcGeometry(r, w, st, sw, 48), mat);
    // 外层：深色墨晕（普通混合）——明亮的草地上也能衬出刀光；glowOnly 时改为叠加辉光
    const gmat = o.glowOnly ? spShader(SP_ARC_FS, SG.Gfx.shade(col, 0.25), { uHead: { value: 0 }, uLen: { value: o.len || 0.75 }, uSoft: { value: 1 } })
      : spShader(SP_ARC_FS, SG.Gfx.shade(col, -0.62), { uHead: { value: 0 }, uLen: { value: o.len || 0.75 }, uSoft: { value: 1 } }, null, true);
    const gm = new THREE.Mesh(spArcGeometry(r + w * 0.3, w * 1.9, st, sw, 48), gmat);
    const cam = SG.Gfx.camera;
    for (const x of [m, gm]) {
      x.position.copy(pos);
      if (o.flat) { x.rotation.x = -Math.PI / 2; x.rotation.z = o.tilt || 0; }
      else {
        if (cam) x.lookAt(cam.position);
        x.rotateZ(o.tilt || 0);
        if (o.lean) x.rotateX(o.lean);
      }
    }
    // mid：把弧心挪开，使半径为 mid 的弧的中点正好落在 pos 上（同一 mid 的几道弧同心 → 平行的爪痕）
    if (o.mid) {
      const ma = st + sw / 2;
      const off = new THREE.Vector3(Math.cos(ma) * o.mid, Math.sin(ma) * o.mid, 0).applyQuaternion(m.quaternion);
      m.position.sub(off); gm.position.sub(off);
    }
    // 刀光压在部队之上（不做深度测试），像画面上的一笔
    mat.depthTest = gmat.depthTest = false;
    this._spAdd(gm, 32);
    this._spAdd(m, 33);
    const dur = o.dur || 0.42;
    // stay：笔画画完后整道留在原处再淡出（爪痕）；否则刀光如彗星般扫过
    const draw = o.stay ? 0.3 : 0.55;
    return this._spAnim(dur, t => {
      const head = o.stay ? Math.min(1, t / draw) * Math.max(1, mat.uniforms.uLen.value) : Math.min(1, t / draw) * (1 + mat.uniforms.uLen.value);
      mat.uniforms.uHead.value = head;
      gmat.uniforms.uHead.value = head;
      const a = t < draw ? 1 : 1 - Math.pow((t - draw) / (1 - draw), o.stay ? 2 : 1);
      mat.uniforms.uOpacity.value = a;
      gmat.uniforms.uOpacity.value = a * (o.glowOnly ? 0.55 : 0.42);
      if (o.grow) { const s = 1 + t * o.grow; m.scale.set(s, s, s); gm.scale.set(s, s, s); }
    }).then(() => { this._spFree(m); this._spFree(gm); });
  };
  // 闪电：从 a 到 b 的折线（每 0.06 秒重新生成一次形状）
  BV._spBolt = function (a, b, cssColor, dur, width) {
    const col = this._spColor(cssColor);
    const mat = spShader(SP_BOLT_FS, col);
    const mk = () => {
      const pts = [a.clone()];
      const n = 12;
      const dir = b.clone().sub(a);
      const perp = new THREE.Vector3(-dir.z, 0, dir.x).normalize();
      for (let i = 1; i < n; i++) {
        const t = i / n;
        const p = a.clone().lerp(b, t);
        const j = (1 - Math.abs(t - 0.5) * 1.2) * 0.9;
        p.addScaledVector(perp, (Math.random() - 0.5) * j);
        p.x += (Math.random() - 0.5) * 0.25 * j; p.z += (Math.random() - 0.5) * 0.25 * j;
        pts.push(p);
      }
      pts.push(b.clone());
      return spRibbonGeometry(pts, width || 0.55);
    };
    const m = new THREE.Mesh(mk(), mat);
    this._spAdd(m, 26);
    let lastSwap = 0;
    return this._spAnim(dur || 0.32, t => {
      if (t - lastSwap > 0.18) { lastSwap = t; const old = m.geometry; m.geometry = mk(); old.dispose(); }
      mat.uniforms.uOpacity.value = (t < 0.15 ? 1.4 : 1 - (t - 0.15) / 0.85) * (0.75 + Math.random() * 0.5);
    }).then(() => this._spFree(m));
  };
  // 光柱
  BV._spPillar = function (pos, cssColor, r, h, dur, alpha) {
    const col = this._spColor(cssColor);
    const g = new THREE.CylinderGeometry(r, r * 1.15, h, 28, 1, true);
    g.translate(0, h / 2, 0);
    const mat = spShader(SP_PILLAR_FS, col);
    const m = new THREE.Mesh(g, mat);
    m.position.copy(pos);
    this._spAdd(m, 22);
    const a0 = alpha === undefined ? 1 : alpha;
    return this._spAnim(dur || 1, t => {
      mat.uniforms.uTime.value = t * (dur || 1);
      const k = t < 0.2 ? spEase(t / 0.2) : 1;
      m.scale.set(0.4 + 0.6 * k + t * 0.15, k, 0.4 + 0.6 * k + t * 0.15);
      mat.uniforms.uOpacity.value = a0 * (t < 0.65 ? 1 : 1 - (t - 0.65) / 0.35);
    }).then(() => this._spFree(m));
  };
  // 残影（共享部队网格，只建一个叠加材质）
  BV._spGhost = function (v, cssColor, life, alpha) {
    if (!v) return;
    const mat = new THREE.MeshBasicMaterial({ color: this._spColor(cssColor), transparent: true, opacity: alpha || 0.5, blending: THREE.AdditiveBlending, depthWrite: false, fog: false });
    const m = new THREE.Mesh(v.mesh.geometry, mat);
    m.userData.keepGeo = true;
    m.position.copy(v.group.position);
    m.rotation.copy(v.group.rotation);
    this._spAdd(m, 9);
    const a0 = mat.opacity;
    this._spAnim(life || 0.4, t => { mat.opacity = a0 * (1 - t); }).then(() => this._spFree(m));
  };
  // 沿路径移动的发射头：path(t) → 位置；每帧在头部喷粒子
  BV._spTrail = function (path, dur, perFrame, o) {
    const pool = o.pool || this.glow;
    return this._spAnim(dur, t => {
      const p = path(o.linear ? t : spEase(t));
      this._emit(pool, p, perFrame, o);
      if (o.head) this._emit(this.glow, p, 1, { color: o.headColor || o.color, alpha: 0.9, life: 0.12, speed: 0, size: o.head, sizeEnd: 0.6, gravity: 0, radius: 0.02 });
    });
  };
  // 地面范围格子发光
  BV._spTiles = function (tiles, cssColor, dur, alpha) {
    if (!tiles || !tiles.length) return Promise.resolve();
    const col = this._spColor(cssColor);
    const mat = SG.Gfx.unlit(col, tileTexture(), 0);
    mat.blending = THREE.AdditiveBlending;
    const ms = [];
    for (const t of tiles) {
      if (!this._inBounds(t.x, t.y)) continue;
      const m = new THREE.Mesh(this.hlGeo, mat);
      m.userData.keepGeo = true; m.userData.keepMat = true;
      m.position.copy(this.tile(t.x, t.y)); m.position.y += 0.09;
      this._spAdd(m, 5);
      ms.push(m);
    }
    const a0 = alpha === undefined ? 0.7 : alpha;
    return this._spAnim(dur || 1, t => { mat.opacity = a0 * (t < 0.15 ? t / 0.15 : t > 0.7 ? (1 - t) / 0.3 : 1); })
      .then(() => { for (const m of ms) this._spFree(m); mat.dispose(); });
  };
  BV._spSparks = function (pos, cssColor, n, speed, size) {
    const col = this._spColor(cssColor);
    this._emit(this.glow, pos, n || 24, { color: col, color2: C(1, 1, 1), mix: 0.35, colorEnd: SG.Gfx.shade(col, -0.2), alpha: 1, life: 0.6, speed: speed || 4, size: size || 0.2, sizeEnd: 0.2, gravity: 0.7, radius: 0.25, lifeVar: 0.4, speedVar: 0.5 });
    this._emit(this.glow, pos, 2, { color: col, alpha: 0.9, life: 0.22, speed: 0, size: 2.2, sizeEnd: 0.4, gravity: 0, radius: 0.05 });
  };
  BV._spDust = function (pos, n, speed, col) {
    this._emit(this.dust, pos, n || 12, {
      color: col || C(0.62, 0.56, 0.45), alpha: 0.5, life: 0.9, speed: speed || 2.2, size: 0.7, sizeEnd: 2.2, gravity: 0.05, radius: 0.4,
      lifeVar: 0.3, drag: 2.2, fadeIn: 0.05,
    });
  };
  BV._spUpdate = function (dt) {
    if (this._petals) this._petals.update(dt, this._pointScale());
    const cam = SG.Gfx && SG.Gfx.camera;
    if (!cam) return;
    // 上一帧的震动偏移若没被相机控制器覆盖，先撤回，避免累积
    if (this._shkLast && cam.position.equals(this._shkLast.pos)) cam.position.sub(this._shkLast.off);
    this._shkLast = null;
    const s = this._shk;
    if (s) {
      s.t += dt;
      if (s.t >= s.dur) { this._shk = null; return; }
      const k = s.amp * Math.pow(1 - s.t / s.dur, 2);
      const tt = this.time;
      const off = new THREE.Vector3(Math.sin(tt * 61) * k, Math.sin(tt * 47 + 1.3) * k * 0.6, Math.cos(tt * 53) * k);
      cam.position.add(off);
      this._shkLast = { pos: cam.position.clone(), off };
    }
  };
  BV._spPetalPool = function () {
    if (!this._petals) {
      this._petals = new ParticlePool(500, THREE.NormalBlending, 1.0, 16, spPetalTex());
      this.group.add(this._petals.points);
    }
    return this._petals;
  };

  // ---------------------------------------------------- 必杀技特效：入口 --
  BV.specialFx = async function (u, targets, fx, info) {
    if (this.disposed) return;
    info = info || {};
    const style = typeof fx === 'string' ? fx : (fx && fx.style) || (info.sp && info.sp.fx) || 'slash';
    const color = (fx && typeof fx === 'object' && fx.color) || info.color || (info.sp && info.sp.color) || '#ffd36b';
    const res = info.res || null;
    targets = (targets || []).filter(Boolean);
    const hits = res ? res.hits : [];
    const ctx = {
      u, v: this.vis(u), style, color, res, info, targets,
      target: info.target || targets[0] || u,
      hits,
      hit: i => { if (typeof info.onHit === 'function') { try { info.onHit(i); } catch (e) { console.error(e); } } },
      hitUnit: x => { for (let i = 0; i < hits.length; i++) if (hits[i].unit === x) ctx.hit(i); },
      hitAll: () => { for (let i = 0; i < hits.length; i++) ctx.hit(i); },
      glyph: SP_GLYPH[style] || '斩',
    };
    if (res && res.kind === 'rally' && style === 'aura') ctx.glyph = '励';
    if (res && res.kind === 'command' && style === 'aura') ctx.glyph = '令';
    const fn = this['_sp_' + style] || this._sp_slash;
    try { await fn.call(this, ctx); }
    catch (e) { console.error(e); }
    finally {
      // 位移收尾：突击者与被击退者落到模型所在的格子
      if (res && !this.disposed) {
        const fixPos = x => { const v = this.vis(x); if (v) { v.pos.copy(this.tile(x.x, x.y)); } };
        if (res.moved) fixPos(u);
        for (const p of res.pushed || []) fixPos(p.unit);
      }
    }
  };

  // 受击的通用表现：闪白、火花、轻微震动
  BV._spImpact = function (x, cssColor, big) {
    const v = this.vis(x);
    if (v) v.flashHit();
    const p = this._spPos(x, 0.7);
    this._spSparks(p, cssColor, big ? 34 : 20, big ? 5 : 3.5, big ? 0.26 : 0.2);
    if (big) this._spDust(this._spPos(x, 0.2), 10, 2.6);
  };
  // 施展者的蓄势：脚下光环收缩 + 光点向内聚集
  BV._spCharge = async function (ctx, dur) {
    const p = this._spPos(ctx.u);
    const col = this._spColor(ctx.color);
    if (ctx.v) ctx.v.tint(ctx.color, 0.9);
    this._spRing(p, ctx.color, 2.4, 0.6, dur || 0.3, { alpha: 0.9 });
    for (let i = 0; i < 18; i++) {
      const a = Math.random() * Math.PI * 2, r = 1.6 + Math.random() * 0.8;
      const sp = 1 / (dur || 0.3);
      this.glow.add({
        x: p.x + Math.cos(a) * r, y: p.y + 0.3 + Math.random() * 1.2, z: p.z + Math.sin(a) * r,
        vx: -Math.cos(a) * r * sp, vy: 0.2, vz: -Math.sin(a) * r * sp, age: 0, life: dur || 0.3,
        r: col.r, g: col.g, b: col.b, r1: 1, g1: 1, b1: 1, a: 1, size: 0.22, sizeEnd: 0.5, grav: 0, drag: 0, fadeIn: 0.2, ac: 1,
      });
    }
    await this._sleep(dur || 0.3);
  };
  // 冲向目标再回位（返回命中时刻的 Promise）
  BV._spLunge = async function (ctx, k, dur) {
    const v = ctx.v;
    if (!v || !ctx.target || ctx.target === ctx.u) return;
    const from = v.pos.clone(), to = this._spPos(ctx.target);
    v.face(to.clone().sub(from));
    await this._spAnim(dur || 0.16, t => { v.pos.lerpVectors(from, to, spEase(t) * (k || 0.45)); });
    this._spAnim(0.22, t => { v.pos.lerpVectors(from, to, (1 - t) * (k || 0.45)); }).then(() => v.pos.copy(from));
  };

  // ---- slash 斩击弧光（单体重击）：两道交叉的巨大刀痕（十字斩）留在敌阵上，随后崩散 ----
  BV._sp_slash = async function (ctx) {
    const t = ctx.target;
    await this._spCharge(ctx, 0.22);
    await this._spLunge(ctx, 0.5, 0.14);
    const p = this._spPos(t, 0.9);
    const R = 2.3, sw = 1.25, st = Math.PI / 2 - sw / 2;
    sfx('hit', 0.9);
    this._spArc(p, ctx.color, { radius: R, width: 0.6, start: st, sweep: sw, tilt: -0.85, mid: R, dur: 0.62, len: 1, stay: true });
    this._spArc(p, '#ffffff', { radius: R + 0.1, width: 0.16, start: st, sweep: sw, tilt: -0.85, mid: R, dur: 0.45, len: 1, stay: true });
    this._spImpact(t, ctx.color, false);
    await this._sleep(0.12);
    sfx('hit', 1);
    this._spArc(p, ctx.color, { radius: R, width: 0.6, start: st, sweep: sw, tilt: 0.85, mid: R, dur: 0.6, len: 1, stay: true });
    this._spArc(p, '#ffffff', { radius: R + 0.1, width: 0.16, start: st, sweep: sw, tilt: 0.85, mid: R, dur: 0.43, len: 1, stay: true });
    await this._sleep(0.06);
    this._spImpact(t, ctx.color, true);
    this._spRing(this._spPos(t), ctx.color, 0.4, 2.8, 0.5);
    this._spRing(this._spPos(t), '#ffffff', 0.3, 1.6, 0.3);
    this._spLight(p, ctx.color, 7, 0.3);
    this._spShake(0.26, 0.32);
    this._spFlash('rgba(255,255,255,1)', 0.2, 0.18);
    ctx.hitAll();
    // 刀痕崩散成碎光
    this._emit(this.glow, p, 26, { color: this._spColor(ctx.color), color2: C(1, 1, 1), mix: 0.5, alpha: 1, life: 0.5, speed: 4.5, size: 0.18, sizeEnd: 0.05, gravity: 0.4, radius: 0.6, speedVar: 0.5 });
    this._spGlyph(this._spPos(t, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.55);
  };

  // ---- dragon 青龙：盘旋的龙气自施展者升起扑向敌军，再一记巨大的弧光 ----
  BV._sp_dragon = async function (ctx) {
    const t = ctx.target;
    const col = this._spColor(ctx.color);
    const a = this._spPos(ctx.u, 0.4), b = this._spPos(t, 1.0);
    if (ctx.v) ctx.v.tint(ctx.color, 1);
    this._spRing(this._spPos(ctx.u), ctx.color, 0.5, 2.4, 0.5);
    this._spPillar(this._spPos(ctx.u), ctx.color, 0.7, 4.5, 0.9, 0.55);
    sfx('magic', 0.5);
    // 龙气：先绕施展者盘旋升空（一圈半），再自高空俯冲扑向敌军
    const top = a.clone().add(new THREE.Vector3(-0.9, 3.4, 0));
    const ctrl = top.clone().lerp(b, 0.5); ctrl.y += 2.4;
    const path = k => {
      if (k < 0.5) {
        const q = k / 0.5, ang = q * Math.PI * 3, r = 0.95 * (1 - q * 0.15);
        return a.clone().add(new THREE.Vector3(Math.cos(ang) * r, 0.2 + q * 3.2, Math.sin(ang) * r));
      }
      return spBezier(top, ctrl, b, spEase((k - 0.5) / 0.5) * 0.85 + ((k - 0.5) / 0.5) * 0.15);
    };
    // 龙身：沿路径生长的发光长带（头粗尾细），身后洒落龙鳞光点
    const taper = q => 0.2 + 0.8 * Math.pow(q, 0.7);
    const body = new THREE.Mesh(new THREE.BufferGeometry(), spShader(SP_BOLT_FS, col, null, null, true));
    this._spAdd(body, 24);
    const glowBody = new THREE.Mesh(new THREE.BufferGeometry(), spShader(SP_BOLT_FS, C(0.85, 1, 0.92)));
    this._spAdd(glowBody, 25);
    await this._spAnim(0.7, k => {
      const head = k, tail = Math.max(0, head - 0.42);
      const pts = [];
      for (let i = 0; i <= 36; i++) pts.push(path(tail + (head - tail) * i / 36));
      const og = body.geometry, og2 = glowBody.geometry;
      body.geometry = spRibbonGeometry(pts, 1.1, taper); glowBody.geometry = spRibbonGeometry(pts, 0.4, taper);
      og.dispose(); og2.dispose();
      body.material.uniforms.uOpacity.value = 1.1; glowBody.material.uniforms.uOpacity.value = 0.95;
      const hp = path(head);
      this._emit(this.glow, hp, 5, { color: col, color2: C(0.85, 1, 0.9), mix: 0.3, colorEnd: SG.Gfx.shade(col, -0.3), alpha: 1, life: 0.55, speed: 1.0, size: 0.4, sizeEnd: 0.12, gravity: 0.25, radius: 0.3, lifeVar: 0.3 });
      this._emit(this.glow, hp, 1, { color: C(0.9, 1, 0.95), alpha: 1, life: 0.12, speed: 0, size: 2.4, sizeEnd: 0.6, gravity: 0, radius: 0.02 });
    });
    this._spAnim(0.3, k => { body.material.uniforms.uOpacity.value = 1.1 * (1 - k); glowBody.material.uniforms.uOpacity.value = 0.95 * (1 - k); })
      .then(() => { this._spFree(body); this._spFree(glowBody); });
    sfx('hit', 1);
    this._spArc(b, ctx.color, { radius: 2.1, width: 0.95, tilt: -0.6, sweep: 2.9, start: -0.5, dur: 0.5, len: 0.9 });
    this._spArc(b, '#ffffff', { radius: 1.9, width: 0.25, tilt: -0.6, sweep: 2.7, start: -0.45, dur: 0.38, len: 0.5 });
    this._spImpact(t, ctx.color, true);
    this._spRing(this._spPos(t), ctx.color, 0.5, 3.2, 0.6);
    this._spRing(this._spPos(t), '#ffffff', 0.3, 1.8, 0.35);
    this._spLight(b, ctx.color, 8, 0.35);
    this._spShake(0.3, 0.4);
    this._spFlash('rgba(200,255,220,1)', 0.22, 0.22);
    ctx.hitAll();
    this._spGlyph(this._spPos(t, 3.2), ctx.glyph, ctx.color, 3.8);
    await this._sleep(0.6);
  };

  // ---- havoc 无双：血色巨刃 + 双重冲击波 ----
  BV._sp_havoc = async function (ctx) {
    const t = ctx.target;
    await this._spCharge(ctx, 0.32);
    this._spBolt(this._spPos(ctx.u, 0.2), this._spPos(ctx.u, 3.4), ctx.color, 0.3, 0.5);
    await this._spLunge(ctx, 0.55, 0.12);
    const p = this._spPos(t, 0.9);
    sfx('hit', 1); sfx('rock', 0.5);
    this._spArc(p, ctx.color, { radius: 2.5, width: 1.1, tilt: 0.25, sweep: 3.4, start: -0.9, dur: 0.55, len: 1.0 });
    this._spArc(p, '#ffffff', { radius: 2.3, width: 0.25, tilt: 0.25, sweep: 3.1, start: -0.85, dur: 0.4, len: 0.6 });
    await this._sleep(0.07);
    this._spArc(p, ctx.color, { radius: 1.6, width: 0.6, tilt: -1.2, sweep: 2.4, start: 0.2, dur: 0.45 });
    this._spImpact(t, ctx.color, true);
    const g = this._spPos(t);
    this._spRing(g, ctx.color, 0.4, 3.6, 0.6);
    setTimeout(() => { if (!this.disposed) this._spRing(g, '#ffd0d8', 0.4, 2.6, 0.5); }, 120);
    this._spDust(this._spPos(t, 0.2), 22, 4);
    this._spLight(p, ctx.color, 9, 0.4);
    this._spShake(0.42, 0.55);
    this._spFlash('rgba(255,40,60,1)', 0.3, 0.35);
    ctx.hitAll();
    this._spGlyph(this._spPos(t, 3.3), ctx.glyph, ctx.color, 4.2);
    await this._sleep(0.65);
  };

  // ---- sweep 横扫：贴地的一圈回旋刀光，扫到谁打谁 ----
  BV._sp_sweep = async function (ctx) {
    const c = this._spPos(ctx.u, 0.6);
    await this._spCharge(ctx, 0.2);
    sfx('hit', 0.8);
    const others = ctx.targets.filter(x => x !== ctx.u);
    // 每个目标相对施展者的角度（弧面平躺，绕 y 轴扫过）
    const angle = x => { const p = this._spPos(x); return Math.atan2(-(p.z - c.z), p.x - c.x); };
    const start = others.length ? angle(others[0]) - 0.9 : 0;
    this._spArc(c, ctx.color, { flat: true, radius: 1.7, width: 0.9, start, sweep: Math.PI * 2, dur: 0.6, len: 0.5, grow: 0.25 });
    this._spArc(c, '#ffffff', { flat: true, radius: 1.75, width: 0.25, start, sweep: Math.PI * 2, dur: 0.5, len: 0.3, grow: 0.25 });
    this._spDust(this._spPos(ctx.u, 0.2), 16, 3.4);
    const done = new Set();
    await this._spAnim(0.36, k => {
      const head = start + k * Math.PI * 2;
      for (const x of others) {
        if (done.has(x)) continue;
        let d = angle(x) - start; while (d < 0) d += Math.PI * 2;
        if (head - start >= d) { done.add(x); this._spImpact(x, ctx.color, false); ctx.hitUnit(x); }
      }
    });
    for (const x of others) if (!done.has(x)) { this._spImpact(x, ctx.color, false); ctx.hitUnit(x); }
    ctx.hitAll();
    this._spRing(this._spPos(ctx.u), ctx.color, 0.6, 3.4, 0.5);
    this._spShake(0.2, 0.3);
    this._spGlyph(this._spPos(ctx.u, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.5);
  };

  // ---- dash 突击：残影 + 冲击 + 击退 ----
  BV._sp_dash = async function (ctx) {
    const v = ctx.v, t = ctx.target, res = ctx.res;
    await this._spCharge(ctx, 0.22);
    const from = v ? v.pos.clone() : this._spPos(ctx.u);
    const to = res && res.moved ? this.tile(res.moved.to.x, res.moved.to.y) : from.clone();
    const tp = this._spPos(t);
    if (v) v.face(tp.clone().sub(from));
    sfx('march', 0.7);
    const trail = [];
    let lastGhost = -1;
    await this._spAnim(0.3, k => {
      const e = k * k;
      if (v) v.pos.lerpVectors(from, to, e);
      const cur = v ? v.pos.clone() : from.clone().lerp(to, e);
      trail.push(cur.clone().add(new THREE.Vector3(0, 0.5, 0)));
      if (k - lastGhost > 0.12 && v) { lastGhost = k; this._spGhost(v, ctx.color, 0.45, 0.55); }
      this._spDust(cur.clone().add(new THREE.Vector3(0, 0.15, 0)), 2, 1.2);
      this._emit(this.glow, cur.clone().add(new THREE.Vector3(0, 0.6, 0)), 3, { color: this._spColor(ctx.color), alpha: 0.9, life: 0.35, speed: 0.6, size: 0.35, sizeEnd: 0.1, gravity: 0, radius: 0.35 });
    });
    if (trail.length > 1) {
      const m = new THREE.Mesh(spRibbonGeometry(trail, 1.1), spShader(SP_BOLT_FS, this._spColor(ctx.color)));
      this._spAdd(m, 24);
      this._spAnim(0.4, k => { m.material.uniforms.uOpacity.value = 0.9 * (1 - k); }).then(() => this._spFree(m));
    }
    // 冲击
    const tv = this.vis(t);
    const hitP = from.clone().lerp(tp, 0.5).lerp(tp, 0.5);
    if (v) { const back = v.pos.clone(); await this._spAnim(0.08, k => { v.pos.lerpVectors(back, tp, k * 0.35); }); this._spAnim(0.18, k => { v.pos.lerpVectors(tp, back, 0.65 + 0.35 * k); }).then(() => v.pos.copy(back)); }
    sfx('hit', 1); sfx('rock', 0.4);
    this._spImpact(t, ctx.color, true);
    this._spRing(tp, ctx.color, 0.4, 3, 0.55);
    this._spRing(hitP.setY(tp.y), '#ffffff', 0.3, 1.6, 0.3);
    this._spLight(this._spPos(t, 1), ctx.color, 7, 0.3);
    this._spShake(0.35, 0.4);
    this._spFlash('rgba(255,255,255,1)', 0.18, 0.2);
    ctx.hitAll();
    this._spGlyph(this._spPos(t, 3.1), ctx.glyph, ctx.color);
    const push = res && res.pushed && res.pushed.find(p => p.unit === t);
    if (push && tv) {
      const a = tv.pos.clone(), b = this.tile(push.to.x, push.to.y);
      await this._spAnim(0.25, k => { tv.pos.lerpVectors(a, b, spEase(k)); tv.pos.y += Math.sin(k * Math.PI) * 0.35; this._spDust(tv.pos.clone().add(new THREE.Vector3(0, 0.1, 0)), 1, 1); });
      tv.pos.copy(b);
    } else if (res && res.blocked && tv) {
      this._spSparks(this._spPos(t, 0.5), '#ffb04a', 26, 4.5);
    }
    await this._sleep(0.45);
  };

  // ---- whirl 往来连斩：在敌军之间瞬移连斩，最后回到原位 ----
  BV._sp_whirl = async function (ctx) {
    const v = ctx.v;
    const home = v ? v.pos.clone() : this._spPos(ctx.u);
    await this._spCharge(ctx, 0.2);
    const n = ctx.hits.length || 1;
    const step = M.clamp(0.95 / n, 0.1, 0.22);
    let cur = home.clone();
    for (let i = 0; i < n; i++) {
      const x = ctx.hits[i] ? ctx.hits[i].unit : ctx.target;
      const tp = this._spPos(x);
      const dir = tp.clone().sub(cur); dir.y = 0;
      const off = dir.lengthSq() > 0.01 ? dir.clone().normalize().multiplyScalar(-0.75) : new THREE.Vector3(0.75, 0, 0);
      const side = new THREE.Vector3(-off.z, 0, off.x).multiplyScalar(i % 2 ? 0.7 : -0.7);
      const dest = tp.clone().add(off).add(side);
      const a = cur.clone();
      if (v) { this._spGhost(v, ctx.color, 0.35, 0.5); v.face(tp.clone().sub(dest)); }
      await this._spAnim(step * 0.45, k => {
        const p = a.clone().lerp(dest, spEase(k));
        if (v) v.pos.copy(p);
        this._emit(this.glow, p.clone().add(new THREE.Vector3(0, 0.6, 0)), 2, { color: this._spColor(ctx.color), alpha: 0.9, life: 0.3, speed: 0.3, size: 0.3, sizeEnd: 0.1, gravity: 0, radius: 0.2 });
      });
      cur = dest;
      sfx('hit', 0.7);
      const tl = (i % 2 ? 0.8 : -0.8) + (Math.random() - 0.5) * 0.4;
      this._spArc(this._spPos(x, 0.9), ctx.color, { radius: 1.9, width: 0.42, start: Math.PI / 2 - 0.55, sweep: 1.1, tilt: tl, mid: 1.9, dur: 0.42, len: 1, stay: true });
      this._spArc(this._spPos(x, 0.9), '#ffffff', { radius: 1.97, width: 0.12, start: Math.PI / 2 - 0.55, sweep: 1.1, tilt: tl, mid: 1.9, dur: 0.3, len: 1, stay: true });
      this._spImpact(x, ctx.color, false);
      this._spShake(0.12, 0.15);
      ctx.hit(i);
      await this._sleep(step * 0.55);
    }
    if (v) {
      this._spGhost(v, ctx.color, 0.35, 0.5);
      const a = v.pos.clone();
      await this._spAnim(0.18, k => { v.pos.lerpVectors(a, home, spEase(k)); });
      v.pos.copy(home);
      v.face(this._spPos(ctx.target).sub(home));
    }
    this._spRing(this._spPos(ctx.u), ctx.color, 0.5, 2.6, 0.45);
    this._spGlyph(this._spPos(ctx.u, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.4);
  };

  // ---- arrows 箭雨：扇形齐射，抛物线落入范围 ----
  BV._sp_arrows = async function (ctx) {
    const src = this._spPos(ctx.u, 1.0);
    const col = this._spColor(ctx.color);
    const area = ctx.res && ctx.res.area && ctx.res.area.length ? ctx.res.area : [{ x: ctx.target.x, y: ctx.target.y }];
    this._spTiles(area, ctx.color, 1.2, 0.45);
    await this._spCharge(ctx, 0.18);
    sfx('march', 0.5);
    const N = 42;
    // 箭杆：深色实体（普通混合），箭头：发光的招式色（叠加）——两组实例共用同一套矩阵
    const geo = new THREE.BoxGeometry(0.05, 0.05, 0.9);
    const mat = new THREE.MeshBasicMaterial({ color: 0x2a2018, transparent: true, opacity: 0.95, depthWrite: false, fog: false });
    const inst = new THREE.InstancedMesh(geo, mat, N);
    inst.frustumCulled = false;
    this._spAdd(inst, 24);
    const hgeo = new THREE.BoxGeometry(0.12, 0.12, 0.3); hgeo.translate(0, 0, 0.5);
    const hmat = new THREE.MeshBasicMaterial({ color: SG.Gfx.shade(col, 0.35), transparent: true, opacity: 1, blending: THREE.AdditiveBlending, depthWrite: false, fog: false });
    const heads = new THREE.InstancedMesh(hgeo, hmat, N);
    heads.frustumCulled = false;
    this._spAdd(heads, 25);
    const arrows = [];
    for (let i = 0; i < N; i++) {
      const tl = area[i % area.length];
      const dst = this.tile(tl.x, tl.y).add(new THREE.Vector3((Math.random() - 0.5) * 1.5, 0.15, (Math.random() - 0.5) * 1.5));
      const s = src.clone().add(new THREE.Vector3((Math.random() - 0.5) * 1.2, Math.random() * 0.4, (Math.random() - 0.5) * 1.2));
      arrows.push({ s, d: dst, t0: Math.random() * 0.3, dur: 0.5 + Math.random() * 0.15, h: 2.6 + Math.random() * 1.1, landed: false });
    }
    const dummy = new THREE.Object3D();
    const firstHit = new Set();
    let elapsed = 0;
    await this._loop(1.15, k => {
      elapsed = k * 1.15;
      for (let i = 0; i < N; i++) {
        const a = arrows[i];
        const q = M.clamp01((elapsed - a.t0) / a.dur);
        const p = a.s.clone().lerp(a.d, q); p.y += Math.sin(q * Math.PI) * a.h;
        const q2 = Math.min(1, q + 0.02);
        const p2 = a.s.clone().lerp(a.d, q2); p2.y += Math.sin(q2 * Math.PI) * a.h;
        dummy.position.copy(p);
        if (q > 0 && q < 1) dummy.lookAt(p2);
        dummy.scale.setScalar(q <= 0 ? 0.0001 : q >= 1 ? 0.0001 : 1);
        dummy.updateMatrix();
        inst.setMatrixAt(i, dummy.matrix);
        heads.setMatrixAt(i, dummy.matrix);
        if (q > 0 && q < 1 && Math.random() < 0.3) this._emit(this.glow, p, 1, { color: col, alpha: 0.6, life: 0.2, speed: 0, size: 0.18, sizeEnd: 0.2, gravity: 0, radius: 0.02 });
        if (q >= 1 && !a.landed) {
          a.landed = true;
          this._emit(this.glow, a.d, 3, { color: col, color2: C(1, 1, 1), mix: 0.4, alpha: 1, life: 0.3, speed: 1.6, size: 0.14, gravity: 0.6, radius: 0.1 });
          if (Math.random() < 0.4) this._dustPuff(a.d, 0.8);
          for (const h of ctx.hits) {
            if (firstHit.has(h.unit)) continue;
            if (Math.abs(h.unit.x * T - (a.d.x - ORIGIN.x - T / 2)) < T && Math.abs(h.unit.y * T - (-a.d.z - ORIGIN.z - T / 2)) < T) {
              firstHit.add(h.unit); const vv = this.vis(h.unit); if (vv) vv.flashHit(); ctx.hitUnit(h.unit); sfx('hit', 0.5);
            }
          }
        }
      }
      inst.instanceMatrix.needsUpdate = true;
      heads.instanceMatrix.needsUpdate = true;
    });
    this._spFree(inst); this._spFree(heads);
    ctx.hitAll();
    this._spShake(0.12, 0.2);
    this._spGlyph(this._spPos(ctx.target, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.35);
  };

  // ---- arrow 一箭穿杨：蓄力、一道金光直贯敌阵 ----
  BV._sp_arrow = async function (ctx) {
    const v = ctx.v, t = ctx.target;
    const a = this._spPos(ctx.u, 1.0), b = this._spPos(t, 0.8);
    if (v) v.face(b.clone().sub(a));
    await this._spCharge(ctx, 0.4);
    sfx('duel', 0.6);
    const col = this._spColor(ctx.color);
    const mid = a.clone().lerp(b, 0.5); mid.y += 0.8;
    const pts = [];
    for (let i = 0; i <= 20; i++) pts.push(spBezier(a, mid, b, i / 20));
    const m = new THREE.Mesh(spRibbonGeometry(pts, 0.42), spShader(SP_BOLT_FS, col));
    const mat = m.material;
    mat.uniforms.uOpacity.value = 0;
    this._spAdd(m, 24);
    const head = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.08, 1.3), new THREE.MeshBasicMaterial({ color: SG.Gfx.shade(col, 0.6), transparent: true, blending: THREE.AdditiveBlending, depthWrite: false, fog: false }));
    this._spAdd(head, 25);
    await this._spAnim(0.22, k => {
      const p = spBezier(a, mid, b, k), p2 = spBezier(a, mid, b, Math.min(1, k + 0.05));
      head.position.copy(p); head.lookAt(p2);
      mat.uniforms.uOpacity.value = 1.2 * k;
      this._emit(this.glow, p, 4, { color: col, color2: C(1, 1, 1), mix: 0.5, alpha: 1, life: 0.35, speed: 0.5, size: 0.3, sizeEnd: 0.1, gravity: 0, radius: 0.08 });
    });
    this._spFree(head);
    this._spAnim(0.5, k => { mat.uniforms.uOpacity.value = 1.2 * (1 - k); }).then(() => this._spFree(m));
    sfx('hit', 1);
    const dir = b.clone().sub(a).normalize();
    this._emit(this.glow, b, 30, { color: col, color2: C(1, 1, 1), mix: 0.4, alpha: 1, life: 0.5, speed: 5, size: 0.2, gravity: 0.3, radius: 0.15, speedVar: 0.6 });
    this._emit(this.glow, b.clone().addScaledVector(dir, 0.8), 12, { color: C(1, 1, 1), alpha: 1, life: 0.3, speed: 6, size: 0.16, gravity: 0, radius: 0.05 });
    const tv = this.vis(t); if (tv) tv.flashHit();
    this._spRing(this._spPos(t), ctx.color, 0.3, 2.4, 0.45);
    this._spLight(b, ctx.color, 7, 0.25);
    this._spShake(0.22, 0.3);
    this._spFlash('rgba(255,240,200,1)', 0.16, 0.18);
    ctx.hitAll();
    this._spGlyph(this._spPos(t, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.55);
  };

  // 火海（blaze / wind 共用）：范围内各格同时起火
  BV._spFireField = function (tiles, cssColor, big) {
    const col = this._spColor(cssColor);
    for (const tl of tiles) {
      if (!this._inBounds(tl.x, tl.y)) continue;
      const p = this.tile(tl.x, tl.y);
      const base = p.clone().add(new THREE.Vector3(0, 0.2, 0));
      this._emit(this.glow, p.clone().add(new THREE.Vector3(0, 0.6, 0)), 1, { color: col, alpha: 0.75, life: 0.35, speed: 0, size: 2.6, sizeEnd: 0.7, gravity: 0, radius: 0.1 });
      this._emitter(this.flame, base, big ? 70 : 50, {
        color: C(1, 0.66, 0.16), color2: C(1, 0.84, 0.36), mix: 0.3, colorEnd: C(0.72, 0.12, 0.03), alpha: 1,
        life: 0.8, speed: 1.8, size: 1.1, sizeEnd: 0.3, gravity: -0.25, box: { x: 1.6, y: 0.2, z: 1.6 }, jitter: 0.3, lifeVar: 0.35, sizeVar: 0.3, alphaCurve: 2,
      }, 1.1);
      this._emitter(this.heat, base, 20, {
        color: C(1, 0.56, 0.16), colorEnd: C(0.95, 0.24, 0.04), alpha: 0.9, life: 0.6, speed: 0.9, size: 1.4, sizeEnd: 0.7, gravity: -0.1,
        box: { x: 1.4, y: 0.15, z: 1.4 }, jitter: 0.15, lifeVar: 0.3, alphaCurve: 2,
      }, 1.1);
      this._emitter(this.glow, base, 10, {
        color: C(1, 0.8, 0.4), colorEnd: C(1, 0.35, 0.1), alpha: 1, life: 1.0, speed: 2.8, size: 0.14, sizeEnd: 0.4, gravity: -0.1,
        box: { x: 1.4, y: 0.2, z: 1.4 }, jitter: 0.6, lifeVar: 0.4,
      }, 1.1);
      this._emitter(this.dust, p.clone().add(new THREE.Vector3(0, 1.4, 0)), 7, {
        color: C(0.15, 0.13, 0.12), colorEnd: C(0.4, 0.38, 0.36), alpha: 0.5, life: 2.0, speed: 1.3, size: 1.0, sizeEnd: 3.0, gravity: -0.03,
        box: { x: 1.0, y: 0.3, z: 1.0 }, jitter: 0.25, lifeVar: 0.3, drag: 0.3, fadeIn: 0.12,
      }, 1.1);
    }
  };
  BV._spAreaTiles = function (ctx, r) {
    if (ctx.res && ctx.res.area && ctx.res.area.length) return ctx.res.area;
    const c = ctx.target, out = [];
    for (let dx = -r; dx <= r; dx++) for (let dy = -r; dy <= r; dy++) if (Math.abs(dx) + Math.abs(dy) <= r && this._inBounds(c.x + dx, c.y + dy)) out.push({ x: c.x + dx, y: c.y + dy });
    return out;
  };

  // ---- fire 火海：火球抛射 → 爆燃 → 范围火海 ----
  BV._sp_fire = async function (ctx) {
    const a = this._spPos(ctx.u, 1.2), b = this._spPos(ctx.target, 0.5);
    const area = this._spAreaTiles(ctx, 1);
    this._spTiles(area, ctx.color, 1.4, 0.55);
    await this._spCharge(ctx, 0.25);
    sfx('fire', 0.8);
    const mid = a.clone().lerp(b, 0.5); mid.y += 4;
    await this._spTrail(k => spBezier(a, mid, b, k), 0.42, 6, {
      pool: this.flame, color: C(1, 0.7, 0.2), color2: C(1, 0.9, 0.5), mix: 0.4, colorEnd: C(0.8, 0.15, 0.03), alpha: 1, life: 0.45, speed: 0.6,
      size: 0.9, sizeEnd: 0.2, gravity: -0.2, radius: 0.2, lifeVar: 0.3, alphaCurve: 2, head: 1.5, headColor: C(1, 0.85, 0.5),
    });
    sfx('rock', 0.6); sfx('fire', 0.8);
    this._spSparks(b, '#ffb347', 40, 6, 0.24);
    this._spRing(this._spPos(ctx.target), '#ff9a3c', 0.5, 4, 0.6);
    this._spLight(b, '#ff8030', 10, 0.6);
    this._spShake(0.3, 0.45);
    this._spFlash('rgba(255,140,40,1)', 0.25, 0.35);
    this._spFireField(area, ctx.color, true);
    for (const x of ctx.targets) { const vv = this.vis(x); if (vv && x.side !== ctx.u.side) { vv.burn(); vv.flashHit(); } }
    ctx.hitAll();
    this._spGlyph(this._spPos(ctx.target, 3.2), ctx.glyph, ctx.color, 3.8);
    await this._sleep(0.75);
  };

  // ---- wind 风助火势（借东风）：东风卷过战场，敌阵燃起大火 ----
  BV._sp_wind = async function (ctx) {
    const c = this._spPos(ctx.target, 0.6);
    const area = this._spAreaTiles(ctx, 2);
    this._spTiles(area, '#bfe8ff', 1.6, 0.4);
    if (ctx.v) ctx.v.tint('#bfe8ff', 1);
    this._spPillar(this._spPos(ctx.u), '#bfe8ff', 0.8, 5, 0.9, 0.7);
    sfx('magic', 0.6);
    // 风：自东南向目标的流线
    const wcol = C(0.85, 0.95, 1);
    const from = c.clone().add(new THREE.Vector3(9, 1.5, 5));
    await this._spAnim(0.55, k => {
      for (let i = 0; i < 6; i++) {
        const s = from.clone().add(new THREE.Vector3((Math.random() - 0.5) * 6, Math.random() * 2.5, (Math.random() - 0.5) * 8));
        const vel = c.clone().sub(s).normalize().multiplyScalar(14 + Math.random() * 6);
        this.glow.add({ x: s.x, y: s.y, z: s.z, vx: vel.x, vy: vel.y * 0.3, vz: vel.z, age: 0, life: 0.55, r: wcol.r, g: wcol.g, b: wcol.b, r1: 1, g1: 0.8, b1: 0.5, a: 0.75, size: 0.28, sizeEnd: 0.9, grav: 0, drag: 0.3, fadeIn: 0.1, ac: 1 });
      }
      // 漩涡
      for (let i = 0; i < 3; i++) {
        const ang = k * 18 + i * 2.1, r = 2.6 - k * 1.4;
        this._emit(this.glow, c.clone().add(new THREE.Vector3(Math.cos(ang) * r, k * 2.2, Math.sin(ang) * r)), 1, { color: wcol, alpha: 0.8, life: 0.4, speed: 0.4, size: 0.4, sizeEnd: 0.2, gravity: 0, radius: 0.1 });
      }
    });
    sfx('fire', 0.9); sfx('rock', 0.5);
    this._spRing(this._spPos(ctx.target), '#ffae42', 0.6, 5.5, 0.7);
    this._spRing(this._spPos(ctx.target), '#ffffff', 0.4, 3.0, 0.4);
    this._spLight(c, '#ff8030', 12, 0.7);
    this._spShake(0.32, 0.5);
    this._spFlash('rgba(255,150,50,1)', 0.28, 0.4);
    const fireTiles = ctx.res && ctx.res.burned && ctx.res.burned.length ? ctx.res.burned : ctx.targets.filter(x => x.side !== ctx.u.side).map(x => ({ x: x.x, y: x.y }));
    this._spFireField(fireTiles, ctx.color, true);
    for (const x of ctx.targets) { const vv = this.vis(x); if (vv && x.side !== ctx.u.side) { vv.burn(); vv.flashHit(); } }
    ctx.hitAll();
    this._spGlyph(c.clone().add(new THREE.Vector3(0, 2.8, 0)), ctx.glyph, '#bfe8ff', 4.2);
    await this._sleep(0.8);
  };

  // ---- lightning 雷击：天色骤暗，乌云压顶，雷霆逐一劈落 ----
  BV._sp_lightning = async function (ctx) {
    const area = this._spAreaTiles(ctx, 2);
    const c = this._spPos(ctx.target);
    this._spDim(0.5, 1.5);
    this._spTiles(area, ctx.color, 1.4, 0.4);
    if (ctx.v) ctx.v.tint(ctx.color, 1);
    for (let i = 0; i < 26; i++) {
      this._emit(this.dust, c.clone().add(new THREE.Vector3((Math.random() - 0.5) * 9, 5.2 + Math.random() * 1.2, (Math.random() - 0.5) * 7)), 1, {
        color: C(0.16, 0.17, 0.24), colorEnd: C(0.3, 0.32, 0.4), alpha: 0.75, life: 1.6, speed: 0.4, size: 3.2, sizeEnd: 4.5, gravity: 0, radius: 0.5, fadeIn: 0.25, drag: 1,
      });
    }
    sfx('magic', 0.5);
    await this._sleep(0.3);
    const strike = ctx.targets.filter(x => x.side !== ctx.u.side);
    if (!strike.length) strike.push(ctx.target);
    for (const x of strike) {
      const g = this._spPos(x, 0.3);
      const top = g.clone().add(new THREE.Vector3((Math.random() - 0.5) * 2, 6.5, (Math.random() - 0.5) * 2));
      sfx('rock', 0.7);
      this._spBolt(top, g, ctx.color, 0.36, 0.7);
      this._spBolt(top.clone().add(new THREE.Vector3(0.5, 0, 0.3)), g, '#ffffff', 0.22, 0.32);
      this._spSparks(g, ctx.color, 26, 5, 0.22);
      this._spRing(this._spPos(x), ctx.color, 0.3, 2.4, 0.45);
      this._spLight(g.clone().add(new THREE.Vector3(0, 1.5, 0)), '#cfe6ff', 12, 0.18);
      this._spFlash('rgba(225,240,255,1)', 0.35, 0.16);
      this._spShake(0.25, 0.25);
      const vv = this.vis(x); if (vv) vv.flashHit();
      ctx.hitUnit(x);
      await this._sleep(0.16);
    }
    ctx.hitAll();
    this._spGlyph(c.clone().add(new THREE.Vector3(0, 3.2, 0)), ctx.glyph, ctx.color, 4);
    await this._sleep(0.55);
  };

  // ---- water 水淹：洪水漫过范围（水面 + 涟漪），浪墙席卷，目标处水柱冲天 ----
  BV._sp_water = async function (ctx) {
    const area = this._spAreaTiles(ctx, 1);
    const c = this._spPos(ctx.target);
    const col = this._spColor(ctx.color);
    const deep = SG.Gfx.shade(col, -0.35);
    this._spTiles(area, ctx.color, 1.7, 0.5);
    await this._spCharge(ctx, 0.25);
    sfx('fire', 0.4);
    const geo = spWallGeometry(1, 1, 0, Math.PI * 2, 72);
    const mat = spShader(SP_WAVE_FS, col);
    mat.blending = THREE.NormalBlending;
    const wall = new THREE.Mesh(geo, mat);
    wall.position.copy(c);
    this._spAdd(wall, 20);
    // 水面：深色半透明圆盘，随浪扩张、随后退去
    const pmat = new THREE.MeshBasicMaterial({ color: deep, transparent: true, opacity: 0, depthWrite: false, fog: false, map: SG.Gfx.softDotTexture });
    const pool = new THREE.Mesh(flatDisc(1), pmat);
    pool.position.copy(c); pool.position.y += 0.14;
    this._spAdd(pool, 19);
    const R = (ctx.res && ctx.res.sp ? ctx.res.sp.radius : 1) * T + 1.4;
    const done = new Set();
    let lastRipple = 0;
    await this._spAnim(0.95, k => {
      const r = 0.3 + spEase(k) * R;
      const h = 2.1 * Math.sin(Math.min(1, k * 1.3) * Math.PI) + 0.25;
      wall.scale.set(r, h, r);
      mat.uniforms.uTime.value = k * 2.4;
      mat.uniforms.uOpacity.value = k < 0.7 ? 1 : (1 - k) / 0.3;
      pool.scale.set(r * 1.25, 1, r * 1.25);
      pmat.opacity = 0.75 * Math.min(1, k * 3) * (k < 0.75 ? 1 : (1 - k) / 0.25);
      for (let i = 0; i < 2; i++) {
        const ang = Math.random() * Math.PI * 2;
        const p = c.clone().add(new THREE.Vector3(Math.cos(ang) * r, h * 0.85, Math.sin(ang) * r));
        this._emit(this.glow, p, 3, { color: C(0.85, 0.95, 1), alpha: 0.9, life: 0.55, speed: 2.6, size: 0.24, gravity: 1.3, radius: 0.2 });
      }
      if (k - lastRipple > 0.12) {
        lastRipple = k;
        const ang = Math.random() * Math.PI * 2, rr = Math.random() * r * 0.8;
        this._spRing(c.clone().add(new THREE.Vector3(Math.cos(ang) * rr, 0.06, Math.sin(ang) * rr)), '#e8f6ff', 0.2, 1.3, 0.5, { alpha: 0.7 });
      }
      for (const x of ctx.targets) {
        if (done.has(x) || x.side === ctx.u.side) continue;
        if (this._spPos(x).distanceTo(c) <= r) {
          done.add(x);
          const xp = this._spPos(x);
          this._emit(this.glow, xp.clone().add(new THREE.Vector3(0, 0.4, 0)), 30, { color: C(0.82, 0.93, 1), color2: col, mix: 0.3, alpha: 1, life: 0.75, speed: 4.6, size: 0.3, gravity: 1.6, radius: 0.35, speedVar: 0.4 });
          this._spPillar(xp, ctx.color, 0.55, 2.6, 0.55, 0.8);
          const vv = this.vis(x); if (vv) { vv.flashHit(); vv.tint(ctx.color, 0.8); }
          ctx.hitUnit(x); sfx('hit', 0.5);
        }
      }
    });
    this._spFree(wall); this._spFree(pool);
    ctx.hitAll();
    this._spShake(0.2, 0.3);
    this._spGlyph(c.clone().add(new THREE.Vector3(0, 3.0, 0)), ctx.glyph, ctx.color);
    await this._sleep(0.4);
  };

  // ---- shock 怒吼：吸气 → 三重冲击波 + 竖直声浪，扬尘四散 ----
  BV._sp_shock = async function (ctx) {
    const c = this._spPos(ctx.u);
    const R = (ctx.res && ctx.res.sp ? ctx.res.sp.radius : 2) * T + 1;
    await this._spCharge(ctx, 0.28);
    sfx('horn', 0.9); sfx('rock', 0.6);
    this._spFlash('rgba(255,170,90,1)', 0.22, 0.3);
    this._spShake(0.5, 0.7);
    for (let i = 0; i < 3; i++) setTimeout(() => {
      if (this.disposed) return;
      this._spRing(c, i === 1 ? '#ffffff' : ctx.color, 0.6, R + i * 0.6, 0.6, { alpha: 1 });
      this._spRing(c.clone().add(new THREE.Vector3(0, 1.2, 0)), ctx.color, 0.4, R * 0.7 + i * 0.4, 0.5, { vertical: true, y: 0, alpha: 0.7 });
    }, i * 110);
    for (let i = 0; i < 36; i++) {
      const a = i / 36 * Math.PI * 2;
      this.dust.add({
        x: c.x + Math.cos(a) * 0.8, y: c.y + 0.25, z: c.z + Math.sin(a) * 0.8, vx: Math.cos(a) * 7, vy: 0.4, vz: Math.sin(a) * 7, age: 0, life: 0.9,
        r: 0.66, g: 0.58, b: 0.46, r1: 0.7, g1: 0.65, b1: 0.58, a: 0.55, size: 0.9, sizeEnd: 2.4, grav: 0, drag: 2.6, fadeIn: 0.05, ac: 1,
      });
    }
    const done = new Set();
    await this._spAnim(0.55, k => {
      const r = 0.6 + spEase(k) * R;
      for (const x of ctx.targets) {
        if (done.has(x) || x === ctx.u) continue;
        if (this._spPos(x).distanceTo(c) <= r) { done.add(x); const vv = this.vis(x); if (vv) vv.flashHit(); this._spSparks(this._spPos(x, 0.7), ctx.color, 12, 2.5, 0.18); ctx.hitUnit(x); }
      }
    });
    ctx.hitAll();
    this._spGlyph(this._spPos(ctx.u, 3.1), ctx.glyph, ctx.color, 4);
    await this._sleep(0.55);
  };

  // ---- aura 金光：光柱冲天，脚下光阵展开，友军金光加身 ----
  BV._sp_aura = async function (ctx) {
    const c = this._spPos(ctx.u);
    const col = this._spColor(ctx.color);
    const R = (ctx.res && ctx.res.sp ? ctx.res.sp.radius || 1 : 2) * T + 0.6;
    sfx('horn', 0.7); sfx('magic', 0.5);
    this._spPillar(c, ctx.color, 0.9, 7, 1.2, 1);
    this._spRing(c, ctx.color, 0.5, R, 1.1, { tex: spCircleTex(), spin: 2.2, hold: true, alpha: 0.85 });
    this._spRing(c, ctx.color, 0.3, R + 0.8, 0.7);
    this._spLight(c.clone().add(new THREE.Vector3(0, 2, 0)), ctx.color, 7, 0.8);
    this._spFlash('rgba(255,230,150,1)', 0.15, 0.4);
    if (ctx.v) ctx.v.tint(ctx.color, 1);
    await this._sleep(0.25);
    const allies = ctx.targets.filter(x => x.side === ctx.u.side);
    for (const x of allies) {
      const p = this._spPos(x);
      const vv = this.vis(x); if (vv) vv.tint(ctx.color, 1);
      if (x !== ctx.u) this._spPillar(p, ctx.color, 0.55, 3.2, 0.8, 0.7);
      this._spRing(p, ctx.color, 0.2, 1.6, 0.5);
      this._emitter(this.glow, p.clone().add(new THREE.Vector3(0, 0.2, 0)), 30, { color: col, color2: C(1, 1, 0.9), mix: 0.4, alpha: 1, life: 0.9, speed: 1.6, size: 0.2, sizeEnd: 0.4, gravity: -0.15, box: { x: 1.2, y: 0.2, z: 1.2 }, jitter: 0.2, lifeVar: 0.3 }, 0.7);
    }
    ctx.hitAll();
    this._spGlyph(c.clone().add(new THREE.Vector3(0, 3.4, 0)), ctx.glyph, ctx.color, 4);
    await this._sleep(0.85);
  };

  // ---- blossom 桃花（桃园结义）：桃花纷飞，金光加身 ----
  BV._sp_blossom = async function (ctx) {
    const c = this._spPos(ctx.u);
    const petals = this._spPetalPool();
    const pink = this._spColor(ctx.color);
    sfx('magic', 0.6);
    this._spPillar(c, '#ffe7a8', 0.9, 6, 1.2, 0.7);
    this._spRing(c, ctx.color, 0.5, (ctx.res && ctx.res.sp ? ctx.res.sp.radius : 2) * T + 0.6, 1.1, { tex: spCircleTex(), spin: 1.6, hold: true, alpha: 0.7 });
    this._spLight(c.clone().add(new THREE.Vector3(0, 2, 0)), '#ffc0d8', 6, 0.9);
    if (ctx.v) ctx.v.tint('#ffd0e0', 1);
    await this._spAnim(0.6, k => {
      for (let i = 0; i < 6; i++) {
        const a = Math.random() * Math.PI * 2, r = 0.5 + Math.random() * 3.5;
        const sw = 2.2;
        const shade = Math.random();
        petals.add({
          x: c.x + Math.cos(a) * r, y: c.y + 0.3 + Math.random() * 3, z: c.z + Math.sin(a) * r,
          vx: -Math.sin(a) * sw + (Math.random() - 0.5), vy: 0.6 + Math.random() * 0.8, vz: Math.cos(a) * sw + (Math.random() - 0.5),
          age: 0, life: 1.4 + Math.random() * 0.6, r: M.lerp(pink.r, 1, shade * 0.6), g: M.lerp(pink.g, 1, shade * 0.6), b: M.lerp(pink.b, 1, shade * 0.6),
          r1: 1, g1: 0.75, b1: 0.85, a: 1, size: 0.34 + Math.random() * 0.18, sizeEnd: 0.8, grav: 0.05, drag: 0.6, fadeIn: 0.15, ac: 3,
        });
      }
    });
    const allies = ctx.targets.filter(x => x.side === ctx.u.side);
    for (const x of allies) {
      const p = this._spPos(x);
      const vv = this.vis(x); if (vv) vv.tint('#ffe0a0', 1);
      this._spRing(p, '#ffe0a0', 0.2, 1.6, 0.5);
      this._emit(this.glow, p.clone().add(new THREE.Vector3(0, 0.8, 0)), 16, { color: C(1, 0.88, 0.6), alpha: 1, life: 0.8, speed: 1.4, size: 0.22, gravity: -0.3, radius: 0.5 });
    }
    ctx.hitAll();
    this._spGlyph(c.clone().add(new THREE.Vector3(0, 3.4, 0)), ctx.glyph, ctx.color, 4);
    await this._sleep(0.7);
  };

  // ---- spirit 符咒（奇谋 / 混乱）：朱砂符纸从施展者袖中飞出，每支敌军三张绕身旋转，脚下暗紫漩涡，符燃中术 ----
  BV._sp_spirit = async function (ctx) {
    const col = this._spColor(ctx.color);
    const deep = '#' + SG.Gfx.shade(col, -0.3).getHexString();
    const foes = ctx.targets.filter(x => x.side !== ctx.u.side);
    const list = foes.length ? foes : [ctx.target];
    const src = this._spPos(ctx.u, 1.5);
    if (ctx.v) ctx.v.tint(ctx.color, 1);
    sfx('magic', 0.7);
    this._spRing(this._spPos(ctx.u), ctx.color, 1.4, 0.4, 0.35, { alpha: 0.8 });
    for (const x of list) this._spRing(this._spPos(x), deep, 0.3, 1.75, 1.35, { tex: spSwirlTex(), spin: -8, hold: true, alpha: 0.85, normal: true });
    const tex = spTalismanTex();
    const papers = [];
    for (const x of list) for (let j = 0; j < 3; j++) {
      const m = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, transparent: true, depthWrite: false, fog: false }));
      m.userData.keepGeo = true;
      m.scale.set(0.34, 0.82, 1);
      m.position.copy(src);
      m.material.opacity = 0;
      this._spAdd(m, 22);
      papers.push({ m, x, j, ph: Math.random() * 6, lift: 0.8 + Math.random() * 1.2, side: Math.random() - 0.5 });
    }
    // 1. 飞出：二次曲线弧线，纸片翻飞（scale.x 随正弦翻面），身后拖紫色光点
    await this._spAnim(0.5, t => {
      for (const p of papers) {
        const k = M.clamp01(t * 1.3 - p.j * 0.12), e = spEase(k);
        const dst = this._spPos(p.x, 1.1);
        const mid = src.clone().lerp(dst, 0.5);
        mid.y += 1.4 + p.lift; mid.x += p.side * 1.6; mid.z -= p.side * 1.2;
        const a0 = (1 - e) * (1 - e), a1 = 2 * (1 - e) * e, a2 = e * e;
        p.m.position.set(src.x * a0 + mid.x * a1 + dst.x * a2, src.y * a0 + mid.y * a1 + dst.y * a2, src.z * a0 + mid.z * a1 + dst.z * a2);
        p.m.scale.x = 0.34 * Math.cos(t * 16 + p.ph);
        p.m.material.opacity = Math.min(1, k * 5);
        if (k > 0 && k < 1) this._emit(this.glow, p.m.position, 1, { color: col, alpha: 0.75, life: 0.3, speed: 0.15, size: 0.2, sizeEnd: 0.04, gravity: 0, radius: 0.03 });
      }
    });
    // 2. 绕身：三张符纸绕敌军旋转、缓缓收紧上升
    sfx('magic', 0.4);
    await this._spAnim(0.55, t => {
      for (const p of papers) {
        const c = this._spPos(p.x);
        const a = p.j * 2.094 + t * 10 + p.ph * 0.2, r = 1.0 - t * 0.4;
        p.m.position.set(c.x + Math.cos(a) * r, c.y + 1.1 + t * 0.5 + Math.sin(t * 7 + p.j * 2) * 0.12, c.z + Math.sin(a) * r);
        p.m.scale.x = 0.34 * Math.cos(t * 12 + p.ph);
      }
      for (const x of list) if (Math.random() < 0.6) this._emit(this.glow, this._spPos(x, 0.3 + Math.random() * 1.2), 1, { color: col, color2: C(1, 1, 1), mix: 0.25, alpha: 0.9, life: 0.5, speed: 0.5, size: 0.24, sizeEnd: 0.08, gravity: -0.4, radius: 0.5 });
    });
    // 3. 符燃：纸片化作紫焰与金屑，敌军中术
    for (const p of papers) {
      this._emit(this.glow, p.m.position, 10, { color: col, color2: C(1, 0.85, 0.45), mix: 0.45, alpha: 1, life: 0.55, speed: 1.8, size: 0.26, sizeEnd: 0.05, gravity: -0.3, radius: 0.12, lifeVar: 0.3 });
      this._spFree(p.m);
    }
    for (const x of list) {
      const vv = this.vis(x); if (vv) { vv.flashHit(); vv.tint(ctx.color, 1); }
      this._spRing(this._spPos(x), ctx.color, 0.4, 1.5, 0.45, { alpha: 0.9 });
    }
    this._spFlash('rgba(190,140,255,1)', 0.16, 0.3);
    ctx.hitAll();
    this._spGlyph(this._spPos(ctx.target).add(new THREE.Vector3(0, 3.0, 0)), ctx.glyph, ctx.color);
    await this._sleep(0.55);
  };

  // ---- shield 护盾：每支受益部队升起六角光罩 ----
  BV._sp_shield = async function (ctx) {
    const col = this._spColor(ctx.color);
    sfx('horn', 0.5); sfx('duel', 0.5);
    await this._spCharge(ctx, 0.2);
    const allies = ctx.targets.filter(x => x.side === ctx.u.side);
    if (!allies.length) allies.push(ctx.u);
    const domes = [];
    for (const x of allies) {
      const g = new THREE.SphereGeometry(1, 28, 14, 0, Math.PI * 2, 0, Math.PI / 2);
      const mat = spShader(SP_DOME_FS, col, { uScan: { value: 0 } }, SP_DOME_VS);
      mat.side = THREE.FrontSide;
      const d = new THREE.Mesh(g, mat);
      d.position.copy(this._spPos(x, 0.05));
      this._spAdd(d, 21);
      domes.push(d);
      this._spRing(this._spPos(x), ctx.color, 0.4, 1.9, 0.5);
      const vv = this.vis(x); if (vv) vv.tint(ctx.color, 0.9);
    }
    await this._spAnim(1.0, k => {
      const s = (k < 0.2 ? spEase(k / 0.2) : 1) * 1.45;
      for (const d of domes) {
        d.scale.set(s, s * 0.95, s);
        d.material.uniforms.uScan.value = (k * 1.6) % 1.1;
        d.material.uniforms.uOpacity.value = k < 0.65 ? 1 : (1 - k) / 0.35;
      }
      if (k > 0.15 && k < 0.2) { for (const x of allies) this._spSparks(this._spPos(x, 1.3), '#ffffff', 10, 2, 0.15); }
    });
    for (const d of domes) this._spFree(d);
    ctx.hitAll();
    this._spGlyph(this._spPos(ctx.u, 3.2), ctx.glyph, ctx.color, 3.8);
    await this._sleep(0.3);
  };

  // ---- heal 治愈之光：柔和光柱与上升的十字光点 ----
  BV._sp_heal = async function (ctx) {
    const col = this._spColor(ctx.color);
    const area = this._spAreaTiles(ctx, 1);
    this._spTiles(area, ctx.color, 1.4, 0.4);
    sfx('magic', 0.6);
    if (ctx.v) ctx.v.tint(ctx.color, 0.8);
    // 施展者撒出一道药光飞向目标
    const a = this._spPos(ctx.u, 1), b = this._spPos(ctx.target, 1);
    if (ctx.target !== ctx.u) {
      const mid = a.clone().lerp(b, 0.5); mid.y += 2.2;
      await this._spTrail(k => spBezier(a, mid, b, k), 0.35, 4, { color: col, color2: C(1, 1, 1), mix: 0.4, alpha: 1, life: 0.45, speed: 0.3, size: 0.32, sizeEnd: 0.1, gravity: 0, radius: 0.08, head: 0.9 });
    }
    const allies = ctx.targets.filter(x => x.side === ctx.u.side);
    if (!allies.length) allies.push(ctx.target);
    for (const x of allies) {
      const p = this._spPos(x);
      this._spPillar(p, ctx.color, 0.7, 4, 1.0, 0.6);
      this._spRing(p, ctx.color, 0.3, 1.7, 0.7);
      const vv = this.vis(x); if (vv) vv.tint(ctx.color, 1);
      this._emitter(this.glow, p.clone().add(new THREE.Vector3(0, 0.2, 0)), 18, { color: col, color2: C(1, 1, 1), mix: 0.5, alpha: 1, life: 1.0, speed: 1.3, size: 0.3, sizeEnd: 0.5, gravity: -0.15, box: { x: 1.2, y: 0.2, z: 1.2 }, jitter: 0.15, lifeVar: 0.3 }, 0.8);
    }
    this._spLight(b, ctx.color, 5, 0.8);
    ctx.hitAll();
    this._spGlyph(b.clone().add(new THREE.Vector3(0, 2.4, 0)), ctx.glyph, ctx.color);
    await this._sleep(0.95);
  };

  // ---- poison 毒雾：毒瓶抛出，绿雾翻滚 ----
  BV._sp_poison = async function (ctx) {
    const area = this._spAreaTiles(ctx, 1);
    const col = this._spColor(ctx.color);
    const a = this._spPos(ctx.u, 1.1), b = this._spPos(ctx.target, 0.4);
    const mid = a.clone().lerp(b, 0.5); mid.y += 3.2;
    await this._spCharge(ctx, 0.2);
    await this._spTrail(k => spBezier(a, mid, b, k), 0.4, 3, { color: col, alpha: 1, life: 0.4, speed: 0.2, size: 0.3, sizeEnd: 0.1, gravity: 0, radius: 0.05, head: 0.8, headColor: C(0.75, 1, 0.5) });
    sfx('fire', 0.4);
    this._spTiles(area, ctx.color, 1.3, 0.5);
    this._spRing(this._spPos(ctx.target), ctx.color, 0.4, 3.4, 0.7);
    for (const tl of area) {
      const p = this.tile(tl.x, tl.y);
      this._emitter(this.dust, p.clone().add(new THREE.Vector3(0, 0.4, 0)), 14, {
        color: C(0.42, 0.75, 0.25), color2: C(0.5, 0.32, 0.6), mix: 0.3, colorEnd: C(0.25, 0.4, 0.18), alpha: 0.55, life: 1.3, speed: 0.7, size: 1.2, sizeEnd: 2.6, gravity: -0.02,
        box: { x: 1.6, y: 0.3, z: 1.6 }, jitter: 0.5, lifeVar: 0.3, drag: 0.6, fadeIn: 0.15,
      }, 0.9);
      this._emitter(this.glow, p.clone().add(new THREE.Vector3(0, 0.2, 0)), 10, { color: C(0.7, 1, 0.4), alpha: 0.9, life: 0.8, speed: 1.0, size: 0.16, sizeEnd: 0.4, gravity: -0.2, box: { x: 1.4, y: 0.2, z: 1.4 }, jitter: 0.1 }, 0.9);
    }
    for (const x of ctx.targets) { if (x.side === ctx.u.side) continue; const vv = this.vis(x); if (vv) { vv.flashHit(); vv.tint(ctx.color, 1); } }
    ctx.hitAll();
    this._spGlyph(this._spPos(ctx.target, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.85);
  };

  // ---- shadow 暗影刺杀：施展者化作黑烟，一道暗光掠过，血色十字 ----
  BV._sp_shadow = async function (ctx) {
    const v = ctx.v, t = ctx.target, res = ctx.res;
    const a = this._spPos(ctx.u, 0.6), b = this._spPos(t, 0.8);
    this._spDim(0.45, 1.3);
    sfx('magic', 0.5);
    const dark = C(0.12, 0.08, 0.16);
    this._emit(this.dust, a, 26, { color: dark, colorEnd: C(0.25, 0.2, 0.3), alpha: 0.8, life: 0.9, speed: 1.4, size: 0.9, sizeEnd: 2.0, gravity: -0.05, radius: 0.6, drag: 1.6 });
    if (v) v.group.visible = false;
    const killed = !!(res && res.killed === t);
    try {
    await this._sleep(0.18);
    await this._spTrail(k => a.clone().lerp(b, k), 0.16, 5, { color: this._spColor(ctx.color), alpha: 1, life: 0.3, speed: 0.2, size: 0.45, sizeEnd: 0.1, gravity: 0, radius: 0.1, head: 1.0, headColor: C(1, 0.5, 0.7), linear: true });
    sfx('hit', 1);
    this._spArc(b, '#ff2a4a', { radius: 1.2, width: 0.3, tilt: -0.8, sweep: 2.2, start: -0.1, dur: 0.35, len: 0.5 });
    this._spArc(b, '#ff2a4a', { radius: 1.2, width: 0.3, tilt: 0.8, sweep: 2.2, start: 0.9, dur: 0.35, len: 0.5 });
    if (killed) {
      this._emit(this.dust, this._spPos(t, 0.6), 30, { color: C(0.15, 0.05, 0.08), colorEnd: C(0.3, 0.2, 0.25), alpha: 0.8, life: 1.2, speed: 2, size: 1, sizeEnd: 2.4, gravity: -0.05, radius: 0.5, drag: 1.4 });
      this._spFlash('rgba(255,20,40,1)', 0.35, 0.4);
      this._spShake(0.35, 0.45);
    } else {
      this._spSparks(b, '#ffffff', 18, 3, 0.16);
      this._spShake(0.15, 0.2);
    }
    const tv = this.vis(t); if (tv) tv.flashHit();
    this._spRing(this._spPos(t), ctx.color, 0.3, 2.2, 0.45);
    ctx.hitAll();
    await this._sleep(0.22);
    } finally {
      if (v && !this.disposed) { v.group.visible = true; this._spGhost(v, ctx.color, 0.4, 0.6); this._emit(this.dust, a, 14, { color: dark, alpha: 0.7, life: 0.6, speed: 1, size: 0.7, sizeEnd: 1.6, gravity: 0, radius: 0.5, drag: 2 }); }
    }
    this._spGlyph(this._spPos(t, 3.0), ctx.glyph, killed ? '#ff3355' : ctx.color, killed ? 4 : 3.2);
    await this._sleep(0.5);
  };

  // ---- drain 吸魂：重击后，敌军的魂光汇入施展者 ----
  BV._sp_drain = async function (ctx) {
    const t = ctx.target;
    const col = this._spColor(ctx.color);
    await this._spCharge(ctx, 0.2);
    await this._spLunge(ctx, 0.45, 0.13);
    const b = this._spPos(t, 0.9);
    sfx('hit', 0.9);
    this._spArc(b, ctx.color, { radius: 1.3, width: 0.5, tilt: -0.7, sweep: 2.4, start: -0.2, dur: 0.4 });
    this._spImpact(t, ctx.color, true);
    this._spShake(0.2, 0.25);
    ctx.hitAll();
    await this._sleep(0.15);
    const a = this._spPos(ctx.u, 0.9);
    sfx('magic', 0.6);
    for (let s = 0; s < 3; s++) {
      const mid = a.clone().lerp(b, 0.5); mid.y += 1.5 + s * 0.6; mid.x += (s - 1) * 1.2;
      this._spTrail(k => spBezier(b, mid, a, k), 0.45, 3, { color: col, color2: C(1, 0.85, 0.9), mix: 0.4, alpha: 1, life: 0.4, speed: 0.2, size: 0.32, sizeEnd: 0.1, gravity: 0, radius: 0.06, head: 0.7 });
    }
    await this._sleep(0.45);
    if (ctx.v) ctx.v.tint(ctx.color, 1);
    this._spRing(this._spPos(ctx.u), ctx.color, 1.8, 0.4, 0.35);
    this._spPillar(this._spPos(ctx.u), ctx.color, 0.6, 3, 0.6, 0.6);
    this._spGlyph(this._spPos(ctx.u, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.4);
  };

  // ---- haste 疾风：脚下青色旋风；受令友军身上风痕沿前进方向疾掠而过，身后拖出三重残影 ----
  BV._sp_haste = async function (ctx) {
    const c = this._spPos(ctx.u);
    const col = this._spColor(ctx.color);
    sfx('march', 0.6); sfx('magic', 0.4);
    const R = ((ctx.res && ctx.res.sp ? ctx.res.sp.radius : 2) * T) + 0.6;
    this._spRing(c, ctx.color, 0.5, R, 0.7);
    this._spRing(c, ctx.color, 0.4, 2.2, 1.0, { tex: spSwirlTex(), spin: 10, hold: true, alpha: 0.75 });
    if (ctx.v) ctx.v.tint(ctx.color, 1);
    const allies = ctx.targets.filter(x => x.side === ctx.u.side && x !== ctx.u);
    const list = allies.length ? allies : [ctx.u];
    // 前进方向：朝最近的敌军（没有则朝对方阵地）
    const units = this.model && this.model.units ? this.model.units : [];
    const dirOf = x => {
      let best = null, bd = 1e9;
      for (const e of units) if (e.alive && e.side !== x.side) { const d = Math.abs(e.x - x.x) + Math.abs(e.y - x.y); if (d < bd) { bd = d; best = e; } }
      const p = this._spPos(x);
      const d = best ? this._spPos(best).sub(p) : new THREE.Vector3(x.side === 0 ? 1 : -1, 0, 0);
      d.y = 0;
      if (d.lengthSq() < 1e-6) d.set(1, 0, 0);
      return d.normalize();
    };
    // 风痕：细长的发光带（平躺、长轴沿前进方向），从身后掠到身前
    const geo = new THREE.PlaneGeometry(1.5, 0.07);
    const tex = spStreakTex();
    const streaks = [], dirs = new Map();
    for (const x of list) {
      const d = dirOf(x), side = new THREE.Vector3(d.z, 0, -d.x), up = new THREE.Vector3(0, 1, 0);
      const q = new THREE.Quaternion().setFromRotationMatrix(new THREE.Matrix4().makeBasis(d, side, up));
      for (let i = 0; i < 9; i++) {
        const mat = new THREE.MeshBasicMaterial({ map: tex, color: i % 3 === 0 ? C(1, 1, 1) : col, transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false, side: THREE.DoubleSide, fog: false });
        const m = new THREE.Mesh(geo, mat);
        m.userData.keepGeo = true;
        m.quaternion.copy(q);
        this._spAdd(m, 20);
        streaks.push({ m, x, d, side, t0: i * 0.07 + Math.random() * 0.04, off: (Math.random() - 0.5) * 1.5, h: 0.25 + Math.random() * 1.3 });
      }
      dirs.set(x, d);
    }
    let ghosted = false;
    await this._spAnim(0.95, k => {
      const t = k * 0.95;
      for (const s of streaks) {
        const u = M.clamp01((t - s.t0) / 0.3);
        const p = this._spPos(s.x);
        s.m.position.copy(p).addScaledVector(s.d, -2.0 + u * 4.0).addScaledVector(s.side, s.off);
        s.m.position.y = p.y + s.h;
        s.m.material.opacity = u <= 0 || u >= 1 ? 0 : Math.sin(u * Math.PI) * 0.95;
      }
      if (!ghosted && t > 0.25) {
        ghosted = true;
        for (const x of list) {
          const vv = this.vis(x);
          if (vv) { vv.tint(ctx.color, 1); for (let j = 1; j <= 3; j++) this._spGhostAt(vv, ctx.color, 0.6, 0.5 - j * 0.12, dirs.get(x).clone().multiplyScalar(-0.45 * j)); }
          this._spRing(this._spPos(x), ctx.color, 0.3, 1.8, 0.45);
          this._spDust(this._spPos(x, 0.15), 6, 2.4);
        }
      }
    });
    for (const s of streaks) this._spFree(s.m);
    geo.dispose();
    ctx.hitAll();
    this._spGlyph(c.clone().add(new THREE.Vector3(0, 3.1, 0)), ctx.glyph, ctx.color);
    await this._sleep(0.45);
  };
  // 残影：在部队位置偏移 off 处放一具半透明的同形网格，life 秒内淡出
  BV._spGhostAt = function (v, cssColor, life, alpha, off) {
    if (!v) return;
    const mat = new THREE.MeshBasicMaterial({ color: this._spColor(cssColor), transparent: true, opacity: alpha, blending: THREE.AdditiveBlending, depthWrite: false, fog: false });
    const m = new THREE.Mesh(v.mesh.geometry, mat);
    m.userData.keepGeo = true;
    m.position.copy(v.group.position).add(off);
    m.rotation.copy(v.group.rotation);
    this._spAdd(m, 9);
    this._spAnim(life, t => { mat.opacity = alpha * (1 - t); }).then(() => this._spFree(m));
  };

  // ---- claw 猛虎爪痕：三道平行爪痕撕裂敌阵；以自身为中心的招式（威吓）则兽影扑向周围每支敌军 ----
  BV._spClawMarks = function (pos, cssColor, s) {
    s = s || 1;
    const R = 2.6 * s, sweep = 0.9, tilt = -0.65 + (Math.random() - 0.5) * 0.35;
    const start = Math.PI / 2 - sweep / 2;
    for (let i = 0; i < 3; i++) {
      const r = R + (i - 1) * 0.4 * s;
      setTimeout(() => {
        if (this.disposed) return;
        this._spArc(pos, cssColor, { radius: r, width: 0.42 * s, start, sweep, tilt, mid: R, dur: 0.6, len: 1.0, stay: true });
        this._spArc(pos, '#ffffff', { radius: r + 0.07 * s, width: 0.14 * s, start, sweep, tilt, mid: R, dur: 0.45, len: 1.0, stay: true });
      }, i * 45);
    }
  };
  BV._sp_claw = async function (ctx) {
    const col = this._spColor(ctx.color);
    const selfCentered = !ctx.target || ctx.target === ctx.u;
    if (selfCentered) {
      const c = this._spPos(ctx.u);
      const R = (ctx.res && ctx.res.sp ? ctx.res.sp.radius || 1 : 1) * T + 1;
      await this._spCharge(ctx, 0.3);
      sfx('horn', 0.8); sfx('rock', 0.5);
      this._spRing(c, ctx.color, 0.6, R, 0.55);
      this._spRing(c, '#ffffff', 0.4, R * 0.7, 0.4);
      this._spShake(0.35, 0.5);
      this._spDust(this._spPos(ctx.u, 0.2), 18, 3.5);
      const foes = ctx.targets.filter(x => x.side !== ctx.u.side);
      for (const x of foes) {
        const a = this._spPos(ctx.u, 0.7), b = this._spPos(x, 0.8);
        const mid = a.clone().lerp(b, 0.5); mid.y += 1.2;
        await this._spTrail(k => spBezier(a, mid, b, k), 0.15, 5, { color: col, color2: C(1, 1, 1), mix: 0.3, alpha: 1, life: 0.35, speed: 0.4, size: 0.42, sizeEnd: 0.1, gravity: 0, radius: 0.12, head: 1.6 });
        sfx('hit', 0.7);
        this._spClawMarks(this._spPos(x, 0.9), ctx.color, 0.8);
        this._spImpact(x, ctx.color, false);
        ctx.hitUnit(x);
      }
      ctx.hitAll();
      this._spGlyph(this._spPos(ctx.u, 3.1), ctx.glyph, ctx.color, 4);
      await this._sleep(0.6);
      return;
    }
    const t = ctx.target;
    await this._spCharge(ctx, 0.26);
    sfx('horn', 0.5);
    await this._spLunge(ctx, 0.55, 0.13);
    const p = this._spPos(t, 0.9);
    sfx('hit', 1); sfx('rock', 0.4);
    this._spClawMarks(p, ctx.color, 1.15);
    await this._sleep(0.1);
    this._spImpact(t, ctx.color, true);
    this._spRing(this._spPos(t), ctx.color, 0.4, 3.0, 0.55);
    this._spLight(p, ctx.color, 7, 0.3);
    this._spShake(0.3, 0.4);
    this._spFlash('rgba(255,190,120,1)', 0.2, 0.22);
    ctx.hitAll();
    this._spGlyph(this._spPos(t, 3.2), ctx.glyph, ctx.color, 4);
    await this._sleep(0.6);
  };

  // ---- rock 落石：巨石自山头滚落，砸进范围内各格，尘土飞扬 ----
  BV._sp_rock = async function (ctx) {
    const area = this._spAreaTiles(ctx, 1);
    const col = this._spColor(ctx.color);
    this._spTiles(area, ctx.color, 1.6, 0.45);
    await this._spCharge(ctx, 0.22);
    sfx('rock', 0.5);
    const mat = SG.Gfx.lowPoly();
    const rocks = [];
    const main = ctx.target || area[0];
    const tiles = [main].concat(area.filter(q => q.x !== main.x || q.y !== main.y));
    const n = M.clamp(tiles.length * 2 + 2, 6, 16);
    for (let i = 0; i < n; i++) {
      const tl = tiles[i < 3 ? 0 : (i - 2) % tiles.length];
      const g = this.tile(tl.x, tl.y);
      const r = (i < 3 ? 0.42 : 0.28) + Math.random() * 0.22;
      const mb = new SG.MeshBuilder();
      const sh = 0.45 + Math.random() * 0.15;
      mb.blob(V(0, 0, 0), V(r, r * 0.85, r), C(sh * 1.05, sh, sh * 0.9), i * 7 + 3);
      const go = SG.Gfx.mesh(mb.toGeometry(), mat, { castShadow: true, receiveShadow: false });
      go.userData.keepMat = true;
      const land = g.clone().add(V((Math.random() - 0.5) * 1.3, r * 0.6, (Math.random() - 0.5) * 1.3));
      // 自镜头一侧的高处斜落
      const from = land.clone().add(V(-1.5 + Math.random() * 3, 8 + Math.random() * 2, -2 - Math.random() * 1.5));
      go.position.copy(from);
      go.visible = false;
      this._spAdd(go);
      rocks.push({ go, from, land, tile: tl, delay: i * 0.045 + (i < 3 ? 0 : 0.12), dur: 0.42 + Math.random() * 0.08, landed: false, spin: V(Math.random() * 9, Math.random() * 6, Math.random() * 9) });
    }
    const hitTiles = new Set();
    await this._spAnim(1.15, k => {
      const el = k * 1.15;
      for (const rk of rocks) {
        const q = (el - rk.delay) / rk.dur;
        if (q <= 0) continue;
        rk.go.visible = true;
        if (q < 1) {
          const e = q * q;   // 自由落体：加速
          rk.go.position.lerpVectors(rk.from, rk.land, e);
          rk.go.rotation.set(rk.spin.x * q, rk.spin.y * q, rk.spin.z * q);
          if (Math.random() < 0.6) this._emit(this.dust, rk.go.position, 1, { color: C(0.55, 0.5, 0.44), alpha: 0.35, life: 0.4, speed: 0.1, size: 0.5, sizeEnd: 1.1, gravity: 0, radius: 0.1 });
        } else if (!rk.landed) {
          rk.landed = true;
          rk.go.position.copy(rk.land);
          this._rockImpact(rk.land);
          this._spDust(rk.land, 6, 2.4);
          this._emit(this.glow, rk.land, 6, { color: col, color2: C(1, 0.9, 0.7), mix: 0.5, alpha: 1, life: 0.35, speed: 3, size: 0.16, gravity: 0.8, radius: 0.15 });
          this._spShake(0.16, 0.18);
          const key = rk.tile.x + ',' + rk.tile.y;
          if (!hitTiles.has(key)) {
            hitTiles.add(key);
            sfx('rock', 0.6);
            this._spRing(this.tile(rk.tile.x, rk.tile.y), ctx.color, 0.3, 2.0, 0.4);
            for (const h of ctx.hits) if (h.unit.x === rk.tile.x && h.unit.y === rk.tile.y) { const vv = this.vis(h.unit); if (vv) vv.flashHit(); ctx.hitUnit(h.unit); }
          }
        } else {
          // 落地后略微下沉、渐隐
          const s = M.clamp01(1 - (q - 1) * 1.6);
          rk.go.scale.setScalar(Math.max(0.001, s));
        }
      }
    });
    for (const rk of rocks) this._spFree(rk.go);
    this.smoke(this._spPos(main));
    ctx.hitAll();
    this._spGlyph(this._spPos(main, 3.0), ctx.glyph, ctx.color);
    await this._sleep(0.4);
  };

  BattleView.Origin = ORIGIN;
  BattleView.T = T;

  SG.BattleView = BattleView;
  SG.UnitVisual = UnitVisual;
})();
