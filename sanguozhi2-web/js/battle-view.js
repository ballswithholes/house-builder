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
   第三版（DESIGN-V3 §1、§2）：战场 18 × 13；地形取自城池周边（js/battle-terrain.js 写入 model.terrain，没有时按地块类型合成），
   平滑的连续地形网格（地形着色器按群落 / 坡度 / 高度混合草地、泥土、农田、沙、岩石、雪，叠加淡格线与棋盘边框；
   棋盘外延伸 16 × 14 格的真实远景，再外为陆地 / 海面的裙边），水面、实例化的圆润树木 / 石块 / 草丛、道路、桥、渡海登陆的船；
   平滑细致的城墙、城门、角楼、本阵（各文化样式，砖石材质）；部队为 js/figure.js 的骑马主将（中精度）+ 实例化士兵
   （低精度、势力色），有待机、行军、攻击、受击、必杀、溃散动作。新增接口：V.groundY(x, z)、V.cultureOf(side)、
   V.stats（构建耗时、三角形数）、UnitVisual.act(kind)、UnitVisual.ghostGeometry()；原有接口与特效不变。
   第四版（DESIGN-V4 §2 美术推广 · 战场）：布景按试点批准的 Lanternvale 画风重画（SG.LP / SG.LPProps；见「第四版：Lanternvale 风格的
   战场布景」一节）——绘制式地面、各群落 / 文化的配方树木与道具（实例化 + 实例化墨线，棋盘上的树按镜头远近换近 / 远景模型）、
   各文化的 LP 城池、桥、登陆船，部队脚下的柔和圆影，夜里城门与本阵的灯。新增接口（可选）：V.setMood('afternoon' | 'golden' | 'dusk' |
   'night' | 小时数 | null)（主场景 SG.Gfx 的时刻，缺省 BattleView.MOOD_HOUR 午后；释放时还原），BattleView.TREE_LOD_DIST / TREE_LOD。
   修订（美术评审）：格线加深加宽；田块、道路、河岸沙改在地形着色器里画；栅栏只沿田边路边、顺格边；挡住部队的树按屏幕投影确认后
   才溶解；近景树按约 5 格见方的块分组（各自视锥剔除、按块切换远近模型；BattleView.PROP_CHUNK_TILES，TREE_LOD_DIST 改为镜头到
   块中心的距离，缺省 20），远景树整片一组。
   第三轮修订：被树挡住的部队周围在树上挖柔和的圆洞（Lanternvale 的 cut-outs；树不再整棵溶解，墨线与影子都在；
   BattleView.TREE_CUTOUTS = false 退回整棵抖动溶解）；城上小亭矮一些、本阵缩小后移（城里的守将看得见）；棋盘外远处的树
   逐棵视锥剔除；石块 / 草垛 / 远景树少几个变体、士兵不投实时阴影（脚下有圆影），绘制次数回到约 180。
   没有载入 js/lowpoly.js / js/lp-props.js 时照旧是第三版的画法。
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
      // 第三版：天竺用贵霜式的砖城与窣堵波本阵；阿克苏姆 / 库施与毛里用阿拉伯式的石 / 土坯城（没有更贴切的现成城墙）
      india: 'kushan', africa: 'arab', moor: 'arab',
    };
    const GROUND_OF = { persia: 'arid', arab: 'arid', tarim: 'arid', kushan: 'arid', steppe: 'steppe', sarmatian: 'steppe', seasia: 'tropic', yi: 'tropic', nanman: 'tropic',
      india: 'tropic', africa: 'arid', moor: 'arid' };
    // 表中没有的文化按 SG.cultureBase（core.js）回退
    const baseOf = c => (SG.cultureBase ? SG.cultureBase(c) : 'han');
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
      const gk = GROUND_OF[culture] || GROUND_OF[baseOf(culture)] || 'temperate';
      return { culture, style: STYLE[culture] || STYLE[baseOf(culture)] || 'han', ground: ground(gk), groundKind: gk };
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
    return { of, wall, gate, keep, palm, col, stepMerlon, stake, standard, gable, MUD, TIMBER };
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

  // ==================================================== 第三版：平滑战场 --
  // DESIGN-V3 §1 / §2：地块不再是平涂的方块，而是一张连续的平滑地形——高度取自 js/battle-terrain.js 对世界地图的取样
  // （model.terrain.grid；没有时由地块类型合成），按地块类型塑形（山岳为圆润的峰群、山丘为缓丘、河川为河道、水域为湖海），
  // 地形着色器（SG.Gfx.terrainMaterial 顶点版）按群落 / 坡度 / 高度混合草地、泥土、农田、沙、岩石、雪，
  // 着色器里再叠加淡淡的格线与棋盘边框（棋盘外的远景略暗）。水面、圆润的树、石块、草丛为实例化网格；
  // 城墙、城门、本阵为平滑细致的模型（砖石材质）。部队用 js/figure.js 的人物模型（骑马的主将 + 实例化的士兵）。
  const BASE_Y = 0.6;              // 平原地面高度（世界坐标；地形着色器在 y < 0.48 处铺沙滩，水边因而是沙岸）
  const WATER_Y = 0.12;            // 水面高度
  const WADE_Y = WATER_Y - 0.08;   // 河川格中部队站立的高度（涉水）
  const SUB_IN = 5, SUB_OUT = 2, SUB_FAR = 1;   // 每格网格分段：棋盘内（含一格外缘）/ 棋盘外 7 格 / 更远
  const EXT_X = 16, EXT_Y = 14;    // 棋盘外延伸的格数（远景，与 battle-terrain 的取样边距一致）
  const MID_EXT = 7;
  const FIG_S = 0.7;               // 人物模型（米）→ 战场（世界单位）：骑马的主将约 1.6、步兵约 1.3（第三版修订：0.6 → 0.7，默认镜头下更易辨认）
  const BIOME_TINT = [[0.38, 0.6, 0.3], [0.6, 0.6, 0.37], [0.8, 0.69, 0.47], [0.25, 0.55, 0.24], [0.6, 0.6, 0.34], [0.5, 0.57, 0.33], [0.3, 0.47, 0.32], [0.55, 0.53, 0.4]];
  const BIOME_ARID = [0, 0.35, 1, 0, 0.3, 0.15, 0, 0.1];       // 各群落的干旱度（草丛、林木的多少）
  const FOREST_TINT = [0.21, 0.4, 0.21], DIRT_TINT = [0.55, 0.45, 0.32];
  const TERRAIN_WATER = 8;          // SG.BattleTerrain.Water（深水：湖、海）
  const COLD = [0.85, 0.85, 0.45, 0, 0, 0.6, 1, 1];               // 各群落山顶积雪的程度（BIOMES 顺序）
  // 平原上的村舍样式（按战场所在城池的文化；表中没有的按 SG.cultureBase 回退）
  const HUT_OF = { han: 'han', korea: 'han', wa: 'han', roman: 'tile', persia: 'flat', arab: 'flat', africa: 'flat', moor: 'flat', tarim: 'flat', kushan: 'flat', india: 'thatch',
    seasia: 'stilt', nanman: 'stilt', yi: 'stilt', celt: 'round', german: 'long', steppe: 'yurt', sarmatian: 'yurt' };
  const FLOWER_COLS = [[1, 0.97, 0.88], [1, 0.84, 0.32], [0.98, 0.6, 0.66], [0.76, 0.64, 0.98], [1, 0.5, 0.36]];

  function sm(e0, e1, x) { const t = M.clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }
  function crw(t, w) { const t2 = t * t, t3 = t2 * t; w[0] = (-t3 + 2 * t2 - t) / 2; w[1] = (3 * t3 - 5 * t2 + 2) / 2; w[2] = (-3 * t3 + 4 * t2 + t) / 2; w[3] = (t3 - t2) / 2; return w; }
  const _wa = [0, 0, 0, 0], _wb = [0, 0, 0, 0];
  // 规则栅格（nx × ny，a[j * nx + i]）的 Catmull-Rom 双三次插值 / 双线性插值；fi, fj 为栅格坐标（越界取边）
  function bicubic(a, nx, ny, fi, fj) {
    fi = fi < 0 ? 0 : fi > nx - 1 ? nx - 1 : fi; fj = fj < 0 ? 0 : fj > ny - 1 ? ny - 1 : fj;
    const i = Math.min(nx - 2, Math.floor(fi)), j = Math.min(ny - 2, Math.floor(fj));
    const wx = crw(fi - i, _wa), wy = crw(fj - j, _wb);
    let s = 0;
    for (let b = 0; b < 4; b++) {
      const jj = Math.max(0, Math.min(ny - 1, j - 1 + b)) * nx;
      let r = 0;
      for (let q = 0; q < 4; q++) r += wx[q] * a[jj + Math.max(0, Math.min(nx - 1, i - 1 + q))];
      s += wy[b] * r;
    }
    return s;
  }
  function bilinear(a, nx, ny, fi, fj) {
    fi = fi < 0 ? 0 : fi > nx - 1.0001 ? nx - 1.0001 : fi; fj = fj < 0 ? 0 : fj > ny - 1.0001 ? ny - 1.0001 : fj;
    const i = Math.floor(fi), j = Math.floor(fj), tx = fi - i, ty = fj - j, o = j * nx + i;
    return (a[o] * (1 - tx) + a[o + 1] * tx) * (1 - ty) + (a[o + nx] * (1 - tx) + a[o + nx + 1] * tx) * ty;
  }
  function fbm2(x, y) { return M.perlinNoise(x, y) * 0.6 + M.perlinNoise(x * 2.03 + 17.1, y * 2.03 + 5.3) * 0.3 + M.perlinNoise(x * 4.1 + 3.7, y * 4.1 + 29.9) * 0.1; }

  // 没有世界地图取样时（旧版随机地形、测试用的假模型）：按地块类型与噪声合成同样格式的取样栅格
  function synthGrid(m, kind) {
    const Tn = TR(), W = m.W, H = m.H, step = 0.5;
    const nu = Math.round((W + 2 * EXT_X) / step) + 1, nv = Math.round((H + 2 * EXT_Y) / step) + 1, N = nu * nv;
    const G = {
      nu, nv, u0: -EXT_X - 0.5, v0: -EXT_Y - 0.5, step, synth: true,
      h: new Float32Array(N), slope: new Float32Array(N), water: new Uint8Array(N), forest: new Float32Array(N), farm: new Float32Array(N),
      sand: new Float32Array(N), snow: new Float32Array(N), biome: new Uint8Array(N), road: new Float32Array(N).fill(999), river: new Float32Array(N).fill(99), coast: new Float32Array(N),
    };
    const bi = { arid: 2, steppe: 1, tropic: 3 }[kind] || 0;
    const sd = (((m.setup && m.setup.target ? m.setup.target.id : 0) | 0) * 13.37) % 100;
    const ca = m.castle || { x: W - 3, y: H >> 1 };
    for (let j = 0; j < nv; j++) for (let i = 0; i < nu; i++) {
      const u = G.u0 + i * step, v = G.v0 + j * step, o = j * nu + i;
      const de = Math.max(-0.5 - u, u - (W - 0.5), -0.5 - v, v - (H - 0.5), 0);
      const n = fbm2(u * 0.11 + sd, v * 0.11 + sd * 0.7);
      G.h[o] = (n - 0.42) * 1.2 + Math.max(0, de - 3) * 0.16 * (0.5 + n);
      G.forest[o] = sm(0.55, 0.68, fbm2(u * 0.16 + 40 + sd, v * 0.16 + 11)) * (bi === 2 ? 0.15 : 1);
      G.biome[o] = bi; G.sand[o] = bi === 2 ? 0.7 : 0;
      G.farm[o] = bi === 2 ? 0 : sm(0.5, 0.62, fbm2(u * 0.2 + 70, v * 0.2 + sd)) * sm(9, 3, Math.hypot(u - ca.x, v - ca.y));
    }
    // 棋盘边上的河格向外延伸成河（远景里河流不在棋盘边缘戛然而止）
    const carve = (x, y, dx, dy) => {
      for (let k = 0; k <= EXT_X * 2 + 2; k++) {
        const u = x + dx * k * 0.5, v = y + dy * k * 0.5 + Math.sin(k * 0.7 + x) * 0.15 * k * 0.1;
        const i = Math.round((u - G.u0) / step), jj = Math.round((v - G.v0) / step);
        for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) {
          const ii = i + a, j2 = jj + b;
          if (ii < 0 || j2 < 0 || ii >= nu || j2 >= nv) continue;
          const d = Math.hypot(a, b) * 0.5;
          const o = j2 * nu + ii;
          G.river[o] = Math.min(G.river[o], d * 0.9);
          if (d < 0.3) G.water[o] = 1;
        }
      }
    };
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      if (m.map[x][y] !== Tn.River) continue;
      if (y === 0) carve(x, y, 0, -1);
      if (y === H - 1) carve(x, y, 0, 1);
      if (x === 0) carve(x, y, -1, 0);
    }
    return G;
  }

  // 战场地形模型：高度函数（格坐标 u, v；格子中心为整数）、取样场、地块遮罩
  function terrainModel(view) {
    const m = view.model, Tn = TR(), W = m.W, H = m.H;
    const Tr = (m.terrain && m.terrain.grid && m.terrain.W === W && m.terrain.H === H) ? m.terrain : null;
    const G = Tr ? Tr.grid : synthGrid(m, view.look.groundKind);
    const h0 = Tr ? Tr.h0 : 0;
    const nu = G.nu, nv = G.nv;
    const gi = u => (u - G.u0) / G.step, gj = v => (v - G.v0) / G.step;
    const ca = m.castle || { x: W - 3, y: H >> 1 };
    const inFort = (x, y) => x >= ca.x - 2 && x <= ca.x + 2 && y >= ca.y - 2 && y <= ca.y + 2;
    const bridges = new Map();
    if (Tr && Tr.bridges) for (const b of Tr.bridges) bridges.set(b.x + ',' + b.y, b);
    // 取样场的世界高度（棋盘外的远景）
    const Hs = new Float32Array(nu * nv);
    for (let o = 0; o < Hs.length; o++) {
      const w = G.water[o], ha = G.h[o], rel = ha - h0, rv = G.river[o];
      let y;
      if (w >= 2) y = WATER_Y - 0.42 - Math.min(1.1, Math.max(0, -ha) * 0.35);
      else if (w === 1 || rv < 0.36) y = WATER_Y - 0.36;
      else {
        // 相对城池地面的起伏；低地（世界地图的海滩带，绝对高度 < 0.5）保持绝对高度，沙滩只出现在真正的海岸 / 河岸
        y = BASE_Y + (rel > 0 ? 3.0 * Math.tanh(rel / 3.0) : Math.max(rel, -0.1));
        const habs = G.synth ? ha + 0.9 : ha;   // 合成的栅格没有绝对高度：视同内陆
        y = M.lerp(Math.max(habs, WATER_Y + 0.02), Math.max(y, 0.5), sm(0.3, 0.6, habs));
        if (rv < 1.1) y = Math.min(y, M.lerp(WATER_Y + 0.1, y, sm(0.36, 1.1, rv)));
      }
      Hs[o] = y;
    }
    // 地块目标高度与遮罩（格子中心为栅格点）
    const N = W * H, tgt = new Float32Array(N), relA = new Float32Array(N);
    const mk = () => new Float32Array(N);
    const mask = { forest: mk(), mountain: mk(), hill: mk(), wet: mk(), plain: mk(), fort: mk(), shoal: mk() };
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      const t = m.map[x][y], o = y * W + x;
      const rel = bilinear(G.h, nu, nv, gi(x), gj(y)) - h0;
      relA[y * W + x] = rel;
      let h;
      if (inFort(x, y)) { h = BASE_Y + 0.06; mask.fort[o] = 1; }
      else if (bridges.has(x + ',' + y)) { h = WATER_Y - 0.4; mask.wet[o] = 1; }
      else switch (t) {
        case Tn.Mountain: h = BASE_Y + 1.3 + M.clamp(rel * 0.15, 0, 0.35); mask.mountain[o] = 1; break;
        case Tn.Hill: h = BASE_Y + 0.32 + M.clamp(rel * 0.1, 0, 0.16); mask.hill[o] = 1; break;
        // 河川；在湖海里的河川格是浅滩（登陆、涉渡）：水浅、看得见沙底
        case Tn.River: { const sea = (G.water[Math.round(gj(y)) * nu + Math.round(gi(x))] | 0) >= 2; h = sea ? WATER_Y - 0.2 : WATER_Y - 0.4; mask.wet[o] = 1; if (sea) mask.shoal[o] = 1; break; }
        case TERRAIN_WATER: h = WATER_Y - 0.9; mask.wet[o] = 1; break;
        case Tn.Forest: h = BASE_Y + M.clamp(rel * 0.45, -0.07, 0.3); mask.forest[o] = 1; break;
        default: h = BASE_Y + M.clamp(rel * 0.45, -0.07, 0.3); mask.plain[o] = 1;
      }
      tgt[o] = h;
    }
    // 第三版修订：水下的地面连续——深水（湖、海）按离岸距离逐格加深（岸边一格浅、三格外最深），再在水格之间做一次 3×3 平滑，
    // 不再是一格一个台阶的深浅方块；河川格保持原深度
    {
      const wetT = (x, y) => x >= 0 && y >= 0 && x < W && y < H && mask.wet[y * W + x] > 0;
      const dist = new Float32Array(N).fill(99);
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        if (!wetT(x, y)) continue;
        let best = 99;
        for (let yy = Math.max(0, y - 4); yy <= Math.min(H - 1, y + 4); yy++) for (let xx = Math.max(0, x - 4); xx <= Math.min(W - 1, x + 4); xx++)
          if (!wetT(xx, yy)) best = Math.min(best, Math.hypot(xx - x, yy - y));
        dist[y * W + x] = best;
      }
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        const o = y * W + x;
        if (m.map[x][y] === TERRAIN_WATER) tgt[o] = WATER_Y - 0.38 - 0.52 * sm(1, 3.2, dist[o]);
      }
      const t2 = tgt.slice();
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        const o = y * W + x;
        if (!wetT(x, y) || m.map[x][y] !== TERRAIN_WATER) continue;
        let sw = 0, sv = 0;
        for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
          if (!wetT(x + dx, y + dy)) continue;
          const w = dx || dy ? 0.6 : 1;
          sw += w; sv += w * t2[(y + dy) * W + x + dx];
        }
        tgt[o] = sv / sw;
      }
    }
    // 山峰与丘顶的隆起：山岳是陡峭的尖峰（主峰 + 副峰，峰顶高出平原约 2.4–3 个单位，雪线以上积雪），
    // 山丘是每格一座圆润的小丘（丘顶高出平原约 0.9，相邻的丘之间有约 0.3 深的鞍部），一眼看得出一格一格的丘陵
    const seed = (Tr ? Tr.seed : ((m.setup && m.setup.target ? m.setup.target.id : 0) | 0) * 7919) | 0;
    const rnd = SG.SeededRandom(seed ^ 0x2b7e1516);
    const bumpAt = new Array(N).fill(null);
    for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
      const o = y * W + x, L = [];
      if (mask.mountain[o]) {
        // 第三版修订：峰高按世界地图上的相对起伏缩放（中原、两河的低山不再是高耸的雪峰）
        const ka = G.synth ? 1 : M.lerp(0.55, 1, sm(0.6, 2.2, relA[o]));
        L.push({ u: x + (rnd.nextDouble() - 0.5) * 0.36, v: y + (rnd.nextDouble() - 0.5) * 0.36, r: 0.92, a: (1.15 + rnd.nextDouble() * 0.55) * ka, peak: true });
        const a = rnd.nextDouble() * Math.PI * 2;
        L.push({ u: x + Math.cos(a) * 0.48, v: y + Math.sin(a) * 0.48, r: 0.58, a: (0.35 + rnd.nextDouble() * 0.35) * ka, peak: true });
      } else if (mask.hill[o]) {
        // 第三版修订：丘更低、位置与大小更随机，相邻的丘连成起伏的岗地（不再是一格一个的「棉被格」）
        const rr = rnd.nextDouble();
        L.push({ u: x + (rnd.nextDouble() - 0.5) * 0.5, v: y + (rnd.nextDouble() - 0.5) * 0.5, r: 0.75 + rr * 0.25, a: 0.26 + rnd.nextDouble() * 0.12 });
      }
      if (L.length) bumpAt[o] = L;
    }
    function heightAt(u, v) {
      let h = bicubic(Hs, nu, nv, gi(u), gj(v));
      const dx = Math.max(-0.5 - u, u - (W - 0.5), 0), dy = Math.max(-0.5 - v, v - (H - 0.5), 0), d = Math.sqrt(dx * dx + dy * dy);
      // 棋盘外的过渡：水面（浅滩 / 深水）只延伸约一格（攻方边的浅滩不再一路伸到画面外）
      const dB = 2.4 - 1.4 * bilinear(mask.wet, W, H, u, v);
      if (d < dB) {
        // 第三版修订：水岸与山脚附近对取样坐标做域扭曲（±0.3 格），岸线与山体轮廓不再沿着格线走；
        // 扭曲量在格子中心为 0（中心的地块类型不变）
        let su = u, sv = v;
        if (d <= 0) {
          const mw = bilinear(mask.wet, W, H, u, v), mm = bilinear(mask.mountain, W, H, u, v), ms = bilinear(mask.shoal, W, H, u, v);
          const k = Math.max(4 * mw * (1 - mw), 4 * mm * (1 - mm), 4 * ms * (1 - ms));
          if (k > 0.01) { su = u + 0.32 * k * (fbm2(u * 0.8 + 3.1, v * 0.8 + 0.7) - 0.5) * 2; sv = v + 0.32 * k * (fbm2(u * 0.8 + 0.3, v * 0.8 + 9.2) - 0.5) * 2; }
        }
        let b = bicubic(tgt, W, H, su, sv);
        const xi = Math.round(u), yi = Math.round(v);
        for (let yy = yi - 1; yy <= yi + 1; yy++) {
          if (yy < 0 || yy >= H) continue;
          for (let xx = xi - 1; xx <= xi + 1; xx++) {
            if (xx < 0 || xx >= W) continue;
            const L = bumpAt[yy * W + xx];
            if (!L) continue;
            for (let k = 0; k < L.length; k++) {
              const q = L[k], e = ((u - q.u) * (u - q.u) + (v - q.v) * (v - q.v)) / (q.r * q.r);
              if (e < 1) { if (q.peak) { const d = Math.max(0, 1 - Math.sqrt(e + 0.006)); b += q.a * Math.pow(d, 1.25) * (1.12 - 0.12 * d); } else b += q.a * (1 - e) * (1 - e); }
            }
          }
        }
        h = d <= 0 ? b : M.lerp(h, b, 1 - sm(0, dB, d));
      }
      // 丘陵：低频起伏把相邻的丘连成岗地
      if (d < 1) {
        const hk = bilinear(mask.hill, W, H, u, v);
        if (hk > 0.02) h += hk * (fbm2(u * 0.35 + 21.7, v * 0.35 + 4.1) - 0.45) * 0.5;
      }
      const amp = 0.05 + 0.24 * sm(BASE_Y + 0.45, BASE_Y + 1.8, h);
      h += (fbm2(u * 0.85 + 13.1, v * 0.85 + 7.7) - 0.5) * amp;
      // 山岳：脊状噪声（山脊、沟壑、岩棱）
      if (d < 1.2 && h > BASE_Y + 0.9) {
        const mk = bilinear(mask.mountain, W, H, u, v);
        if (mk > 0.02) { const r = 1 - Math.abs(M.perlinNoise(u * 1.7 + 5.3, v * 1.7 + 1.9) * 2 - 1); h += mk * (r * r - 0.4) * 0.42 * sm(BASE_Y + 0.9, BASE_Y + 1.8, h); }
      }
      return h;
    }
    const maskAt = (a, u, v) => bilinear(a, W, H, u, v);
    return { m, G, Tr, h0, W, H, gi, gj, Hs, tgt, mask, maskAt, heightAt, inFort, bridges, castle: ca, seed };
  }

  // 非均匀的网格轴：棋盘内（含一格外缘）每格 SUB_IN 段，其外 MID_EXT 格每格 SUB_OUT 段，更远每格 SUB_FAR 段
  function axisSegs(n, ext) {
    const lo = -ext - 0.5, in0 = -1.5, in1 = n + 0.5, hi = n - 0.5 + ext, m0 = Math.max(lo, in0 - MID_EXT), m1 = Math.min(hi, in1 + MID_EXT);
    const segs = [[lo, m0, SUB_FAR], [m0, in0, SUB_OUT], [in0, in1, SUB_IN], [in1, m1, SUB_OUT], [m1, hi, SUB_FAR]];
    let k = 0;
    return segs.filter(q => q[1] > q[0] + 1e-6).map(q => { const n1 = Math.round((q[1] - q[0]) * q[2]); const o = { a: q[0], b: q[1], sub: q[2], k0: k, n: n1 }; k += n1; return o; });
  }
  function terrainAxis(n, ext) {
    const a = [], S = axisSegs(n, ext);
    for (const q of S) for (let k = 0; k < q.n; k++) a.push(q.a + k / q.sub);
    a.push(S[S.length - 1].b);
    return a;
  }
  // 轴上的连续下标（格坐标 → 栅格坐标）
  function axisIndex(S, w) {
    for (const q of S) if (w < q.b || q === S[S.length - 1]) return q.k0 + (w - q.a) * q.sub;
    return -1;
  }

  // 格线与棋盘边框（注入地形着色器；sgBoard = (原点 x, 原点 z, W, H)，格坐标 = (世界 x − 原点, −世界 z − 原点) / 2）
  const GRID_PARS = 'uniform vec4 sgBoard;\nuniform float sgGridK;\n';
  const GRID_GLSL = `
	{
		vec2 bt = ( vec2( vSgW.x, - vSgW.z ) - sgBoard.xy ) * 0.5;
		vec2 dOut = max( max( - bt, bt - sgBoard.zw ), vec2( 0.0 ) );
		float outD = length( dOut );
		float ins = 1.0 - step( 1e-4, outD );
		vec2 g = abs( fract( bt ) - 0.5 );
		vec2 fw = max( fwidth( bt ), vec2( 1e-4 ) );
		vec2 ln2 = 1.0 - smoothstep( vec2( 0.0 ), fw * 1.3 + 0.012, vec2( 0.5 ) - g );
		float ln = max( ln2.x, ln2.y ) * ins;
		float far = 1.0 - smoothstep( 0.35, 0.9, max( fw.x, fw.y ) );
		diffuseColor.rgb *= 1.0 - sgGridK * ln * far;
		// 棋盘外：略暗、略褪色（可玩范围一目了然）；棋盘边缘一道细的暗边
		float o = smoothstep( 0.0, 1.6, outD );
		float lum = dot( diffuseColor.rgb, vec3( 0.3, 0.59, 0.11 ) );
		diffuseColor.rgb = mix( diffuseColor.rgb, mix( diffuseColor.rgb, vec3( lum ), 0.25 ) * 0.84, o );
		vec2 e = min( bt, sgBoard.zw - bt );
		float edge = ( 1.0 - smoothstep( 0.0, fw.x * 1.5 + 0.035, abs( min( e.x, e.y ) ) ) ) * step( - 0.05, min( e.x, e.y ) );
		diffuseColor.rgb *= 1.0 - 0.35 * edge;
	}
`;
  // 棋盘内的地块外观（aBt = (山丘, 山岳, 寒冷度, 0)）：山丘为干草色的赭黄草坡（丘顶亮、坡脚暗），
  // 山岳为三平面投影的岩石（带层理）、坡脚的碎石，雪线以上积雪（寒冷度按群落：热带无雪）
  const BT_GLSL = `
	{
		float sgHl = clamp( vSgBt.x, 0.0, 1.0 ), sgMt = clamp( vSgBt.y, 0.0, 1.0 );
		vec3 c = diffuseColor.rgb;
		if ( sgHl > 0.01 ) {
			float lum = dot( c, vec3( 0.3, 0.59, 0.11 ) );
			vec3 dry = vec3( 0.74, 0.69, 0.43 ) * ( 0.55 + 0.8 * lum ) * ( 0.86 + 0.28 * sgDF.r ) * ( 0.9 + 0.16 * smoothstep( 0.95, 1.45, sgH ) );
			c = mix( c, dry, sgHl * ( 0.3 + 0.3 * sgDM.g ) );
		}
		if ( sgMt > 0.01 ) {
			vec3 bw = sgN * sgN; bw *= bw; bw /= ( bw.x + bw.y + bw.z );
			float r = texture2D( sgDetB, vSgW.zy * 0.31 ).r * bw.x + texture2D( sgDetB, vSgW.xz * 0.31 ).r * bw.y + texture2D( sgDetB, vSgW.xy * 0.31 ).r * bw.z;
			float strata = 0.9 + 0.12 * sin( sgH * 7.0 + r * 4.0 );
			vec3 rock = mix( vec3( 0.36, 0.34, 0.32 ), vec3( 0.47, 0.42, 0.36 ), sgDM.r ) * ( 0.55 + 0.7 * r ) * strata;
			vec3 scree = vec3( 0.52, 0.48, 0.42 ) * ( 0.75 + 0.4 * sgDA.g );
			float base = 1.0 - smoothstep( 1.05, 1.7, sgH + ( sgDA.b - 0.5 ) * 0.3 );
			rock = mix( rock, scree, base * 0.6 );
			c = mix( c, rock, sgMt * ( 0.62 + 0.38 * smoothstep( 1.0, 1.9, sgH ) ) );
			float snow = smoothstep( 0.3, 0.6, vSgBt.z + ( sgDA.a - 0.5 ) * 0.35 + ( r - 0.5 ) * 0.3 ) * ( 1.0 - smoothstep( 0.6, 0.88, sgSlope ) );
			c = mix( c, vec3( 0.86, 0.88, 0.92 ) * ( 0.9 + 0.12 * sgDA.a ), snow );
		}
		diffuseColor.rgb = c;
	}
`;
  function battleTerrainMaterial(W, H) {
    const mat = SG.Gfx.terrainMaterial();
    const base = mat.onBeforeCompile;
    const u = { sgBoard: { value: new THREE.Vector4(ORIGIN.x, ORIGIN.z, W, H) }, sgGridK: { value: 0.16 } };
    mat.userData.boardUniforms = u;
    mat.onBeforeCompile = function battleTerrainCompile(shader, renderer) {
      base.call(this, shader, renderer);
      Object.assign(shader.uniforms, u);
      const key = 'diffuseColor.rgb = sgCol;';
      if (shader.fragmentShader.indexOf(key) >= 0) shader.fragmentShader = GRID_PARS + 'varying vec4 vSgBt;\n' + shader.fragmentShader.replace(key, key + BT_GLSL + GRID_GLSL);
      shader.vertexShader = 'attribute vec4 aBt;\nvarying vec4 vSgBt;\n' + shader.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\n\tvSgBt = aBt;');
    };
    mat.customProgramCacheKey = () => 'sgBattleTerrain';
    mat.name = 'BattleTerrain';
    return mat;
  }

  // 登陆的船（MeshBuilder 坐标：x 东、z 北）：平滑的船壳（12 个横截面 × 8 段，首尾上翘、尖首），甲板、
  // 桅杆与鼓起的帆（阵营色）、两舷各三支桨；yaw 为船首相对正东的偏角
  function boatInto(mb, x0, y0, z0, yaw, sc, team, rnd) {
    const cs = Math.cos(yaw), sn = Math.sin(yaw);
    const P = (lx, ly, lz) => V(x0 + (lx * cs - lz * sn) * sc, y0 + ly * sc, z0 + (lx * sn + lz * cs) * sc);
    const N = (nx, ny, nz) => V(nx * cs - nz * sn, ny, nx * sn + nz * cs);
    const L = 2.1, NS = 12, NA = 8;
    const beam = s => 0.36 * Math.pow(Math.max(0.02, Math.sin(Math.PI * s)), 0.55) + 0.015;
    const depth = s => 0.24 * (0.35 + 0.65 * Math.pow(Math.max(0, Math.sin(Math.PI * s)), 0.5));
    const top = s => 0.1 + 0.22 * Math.pow(2 * s - 1, 2) * (s > 0.5 ? 1.25 : 1);
    const hp = (s, a) => [(s - 0.5) * L, top(s) - depth(s) * Math.sin(a), beam(s) * Math.cos(a)];
    const grid = [];
    for (let i = 0; i <= NS; i++) { const row = []; for (let j = 0; j <= NA; j++) row.push(hp(i / NS, Math.PI * j / NA)); grid.push(row); }
    const nrm = (i, j) => {
      const a = grid[Math.min(NS, i + 1)][j], b = grid[Math.max(0, i - 1)][j], c = grid[i][Math.min(NA, j + 1)], d = grid[i][Math.max(0, j - 1)];
      const ux = a[0] - b[0], uy = a[1] - b[1], uz = a[2] - b[2], vx = c[0] - d[0], vy = c[1] - d[1], vz = c[2] - d[2];
      let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
      const p = grid[i][j], cxl = (p[0]) * 0.6, out = (p[0] - cxl) * nx + (p[1] - 0.05) * ny + p[2] * nz;
      const l = Math.hypot(nx, ny, nz) || 1, sg = out < 0 ? -1 : 1;
      return N(nx / l * sg, ny / l * sg, nz / l * sg);
    };
    const wood = C(0.42, 0.29, 0.17), woodD = C(0.3, 0.2, 0.12), deck = C(0.6, 0.47, 0.31);
    for (let i = 0; i < NS; i++) for (let j = 0; j < NA; j++) {
      const a = grid[i][j], b = grid[i + 1][j], c = grid[i + 1][j + 1], d = grid[i][j + 1];
      const k = j === 0 || j === NA - 1 ? woodD : wood;
      mb.triN(P(...a), P(...b), P(...c), nrm(i, j), nrm(i + 1, j), nrm(i + 1, j + 1), k);
      mb.triN(P(...a), P(...c), P(...d), nrm(i, j), nrm(i + 1, j + 1), nrm(i, j + 1), k);
    }
    const up = N(0, 1, 0);
    for (let i = 0; i < NS; i++) {
      const a = grid[i][0], b = grid[i + 1][0], c = grid[i + 1][NA], d = grid[i][NA], dy = -0.03;
      mb.triN(P(a[0], a[1] + dy, a[2]), P(b[0], b[1] + dy, b[2]), P(c[0], c[1] + dy, c[2]), up, up, up, deck);
      mb.triN(P(a[0], a[1] + dy, a[2]), P(c[0], c[1] + dy, c[2]), P(d[0], d[1] + dy, d[2]), up, up, up, deck);
    }
    // 桅杆、横桁与鼓帆（双面）
    const mx = 0.12, mh = 1.55;
    mb.tube([P(mx, 0.06, 0), P(mx, mh, 0)], [0.035, 0.025], 6, woodD, true);
    mb.tube([P(mx + 0.02, mh - 0.08, -0.55), P(mx + 0.02, mh - 0.08, 0.55)], [0.022, 0.022], 5, woodD, true);
    const sail = shade(team, 0.18), sailB = shade(team, -0.05), SU = 4, SV = 4;
    const sp = (a, b) => [mx + 0.05 + 0.22 * Math.sin(Math.PI * a) * (0.55 + 0.45 * Math.sin(Math.PI * b)), mh - 0.1 - b * 0.95, (a - 0.5) * 1.05 * (1 - 0.12 * b)];
    const sn2 = (a, b) => { const e = 0.01, p0 = sp(a, b), pa = sp(a + e, b), pb = sp(a, b + e);
      const ux = pa[0] - p0[0], uy = pa[1] - p0[1], uz = pa[2] - p0[2], vx = pb[0] - p0[0], vy = pb[1] - p0[1], vz = pb[2] - p0[2];
      let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; const l = Math.hypot(nx, ny, nz) || 1; return [nx / l, ny / l, nz / l]; };
    for (let i = 0; i < SU; i++) for (let j = 0; j < SV; j++) {
      const q = [[i / SU, j / SV], [(i + 1) / SU, j / SV], [(i + 1) / SU, (j + 1) / SV], [i / SU, (j + 1) / SV]];
      const pts = q.map(e => P(...sp(e[0], e[1]))), ns = q.map(e => sn2(e[0], e[1]));
      for (const sg of [1, -1]) {
        const nn = ns.map(e => N(e[0] * sg, e[1] * sg, e[2] * sg)), c = sg > 0 ? sail : sailB;
        mb.triN(pts[0], pts[1], pts[2], nn[0], nn[1], nn[2], c);
        mb.triN(pts[0], pts[2], pts[3], nn[0], nn[2], nn[3], c);
      }
    }
    // 桨：两舷各三支，略有参差
    for (const side of [1, -1]) for (let k = 0; k < 3; k++) {
      const s = 0.3 + k * 0.17, g = hp(s, side > 0 ? 0 : Math.PI), sw = (rnd.nextDouble() - 0.5) * 0.25;
      const a = P(g[0], g[1] + 0.02, g[2]), b = P(g[0] - 0.12 + sw, -0.04, g[2] + side * 0.62);
      mb.tube([a, b], [0.016, 0.016], 4, woodD, true);
      const t = P(g[0] - 0.12 + sw, -0.02, g[2] + side * 0.62), bl = P(g[0] - 0.12 + sw, -0.06, g[2] + side * 0.78);
      mb.tube([t, bl], [0.05, 0.035], 4, wood, true);
    }
  }

  // 棋盘内的树：逐实例的淡出（aFade 0 – 1 → 不透明度 1 – 0.75·aFade），挡住部队时变得半透明。
  // 材质为透明（写深度，renderOrder 5：在水面、高亮、光环之后绘制，透过淡去的树看得见它们与部队）
  function propFadeMaterial() {
    const mat = SG.Gfx.newLowPoly ? SG.Gfx.newLowPoly() : new SG.LowPolyMaterial();
    const base = mat.onBeforeCompile;
    mat.transparent = true;
    mat.depthWrite = true;
    mat.onBeforeCompile = function propFadeCompile(shader, renderer) {
      base.call(this, shader, renderer);
      shader.vertexShader = 'attribute float aFade;\nvarying float vSgFade;\n' + shader.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\n\tvSgFade = aFade;');
      shader.fragmentShader = 'varying float vSgFade;\n' + shader.fragmentShader.replace('#include <color_fragment>', '#include <color_fragment>\n\tdiffuseColor.a *= 1.0 - 0.75 * vSgFade;');
    };
    mat.customProgramCacheKey = () => 'sgPropFade';
    mat.name = 'PropFade';
    return mat;
  }

  // ---- 实例化的树、石、草（模板几何体按种类缓存，各场战斗共用） --
  const PROP_TPL = {};
  // 小件点缀：不随「挡住部队时淡去」处理（本来就矮小）
  const SMALL_PROP = { rock: 1, tuft: 1, bush: 1, scrub: 1, hay: 1, fence: 1, flowers: 1, cairn: 1 };
  function propTemplate(kind) {
    if (PROP_TPL[kind]) return PROP_TPL[kind];
    const mb = new SG.MeshBuilder();
    const bark = C(0.4, 0.29, 0.18);
    switch (kind) {
      // 第三版修订：树冠一律平滑明暗（smoothN：法线向未起伏的椭球法线混合，轮廓仍有起伏），近景二级细分、
      // 远景（棋盘外）一级细分；杨（poplar）与云杉（spruce）不另建模板，用阔叶树 / 松的模板非等比缩放（不增加绘制次数）
      case 'broad':      // 阔叶树：平滑树干 + 圆润树冠（二级细分）+ 顶上一小团
        mb.lathe(V(0, 0, 0), [[0.09, 0], [0.06, 0.55], [0.035, 0.85]], 8, bark);
        mb.sphere(V(0, 0.98, 0), V(0.52, 0.46, 0.52), C(0.36, 0.56, 0.28), 2, { seed: 3, rough: 0.06, bottomShade: 0.3, smoothN: 0.6 });
        mb.sphere(V(0.18, 1.3, -0.08), V(0.28, 0.25, 0.28), C(0.42, 0.62, 0.32), 1, { seed: 7, rough: 0.03, bottomShade: 0.2, smoothN: 0.7 });
        break;
      case 'broadLo':    // 远景阔叶树
        mb.lathe(V(0, 0, 0), [[0.09, 0], [0.05, 0.6]], 5, bark);
        mb.sphere(V(0, 1.0, 0), V(0.55, 0.5, 0.55), C(0.38, 0.58, 0.29), 1, { seed: 5, rough: 0.05, bottomShade: 0.3, smoothN: 0.85 });
        break;
      case 'pine':       // 松：三层圆锥树冠（平滑法线）
      case 'pineLo':
        mb.lathe(V(0, 0, 0), [[0.08, 0], [0.04, 0.6]], 6, bark);
        for (const [y0, r0, h0, c] of [[0.3, 0.52, 0.8, C(0.22, 0.42, 0.26)], [0.72, 0.4, 0.7, C(0.26, 0.47, 0.29)], [1.1, 0.27, 0.58, C(0.3, 0.52, 0.32)]])
          mb.lathe(V(0, y0, 0), [[0, -0.02], [r0 * 0.96, 0.02], [r0, 0.07], [r0 * 0.45, h0 * 0.55], [0, h0]], kind === 'pine' ? 14 : 9, c);
        break;
      case 'palm': {     // 椰枣 / 椰树：弯曲树干 + 弧形羽叶
        const pts = [], rad = [];
        for (let i = 0; i <= 5; i++) { const t = i / 5; pts.push(V(Math.sin(t * 1.3) * 0.22, t * 1.75, 0)); rad.push(0.075 - t * 0.03); }
        mb.tube(pts, rad, 6, C(0.55, 0.43, 0.28), true);
        const top = pts[5];
        for (let i = 0; i < 7; i++) {
          const a = i * Math.PI * 2 / 7, ca = Math.cos(a), sa = Math.sin(a);
          const L = 0.72, mid = V(top.x + ca * L * 0.5, top.y + 0.12, top.z + sa * L * 0.5), tip = V(top.x + ca * L, top.y - 0.28, top.z + sa * L);
          const sx = -sa * 0.13, sz = ca * 0.13, leaf = shade(C(0.27, 0.5, 0.22), (i % 2) * 0.07);
          // 第三版修订：羽叶用平滑的朝上 / 朝下法线（同样的三角形数），不再一片片折面分明；叶基略暗、叶尖略亮
          const nU = V(ca * 0.3, 1, sa * 0.3), nD = V(-ca * 0.3, -1, -sa * 0.3), cB = shade(leaf, -0.14), cT = shade(leaf, 0.08);
          for (const f of [1, -1]) {
            const sd = V(mid.x + sx * f, mid.y - 0.03, mid.z + sz * f);
            for (const n of [nU, nD]) { mb.triN(top, sd, mid, n, n, n, cB, leaf, leaf); mb.triN(mid, sd, tip, n, n, n, leaf, leaf, cT); }
          }
        }
        break;
      }
      case 'cypress':    // 地中海柏：细高的尖塔形（平滑的旋转体，与攻击画面一致）
        mb.lathe(V(0, 0, 0), [[0.07, 0], [0.04, 0.3]], 6, bark);
        mb.lathe(V(0, 0.16, 0), [[0.03, 0], [0.24, 0.12], [0.3, 0.45], [0.26, 0.85], [0.16, 1.18], [0.06, 1.38], [0, 1.44]], 12, C(0.22, 0.4, 0.24));
        break;
      case 'acacia':     // 稀树草原：斜干分叉 + 伞形平顶
      case 'acaciaLo':
        mb.tube([V(0, 0, 0), V(0.05, 0.55, 0), V(0.12, 0.95, 0.03)], [0.07, 0.05, 0.035], 6, bark);
        if (kind === 'acacia') mb.tube([V(0.03, 0.45, 0), V(-0.22, 0.88, 0.06)], [0.035, 0.025], 5, bark);
        // 树冠一级细分（稀树草原一场可有两三百棵近景金合欢，二级细分会多出十几万三角形），明暗靠 smoothN 圆润
        mb.sphere(V(0.1, 1.05, 0), V(0.64, 0.22, 0.58), C(0.4, 0.52, 0.26), 1, { seed: 13, rough: 0.05, bottomShade: 0.4, smoothN: 0.85 });
        break;
      case 'olive':      // 橄榄：扭曲的矮干、灰绿的宽冠（两团）
      case 'oliveLo':
        mb.tube([V(0, 0, 0), V(0.07, 0.24, 0.02), V(-0.02, 0.44, 0.05), V(0.03, 0.56, 0)], [0.085, 0.065, 0.05, 0.04], kind === 'olive' ? 7 : 5, C(0.42, 0.36, 0.28));
        mb.sphere(V(0.02, 0.72, 0), V(0.5, 0.3, 0.46), C(0.45, 0.52, 0.36), kind === 'olive' ? 2 : 1, { seed: 19, rough: 0.1, bottomShade: 0.32, smoothN: kind === 'olive' ? 0.6 : 0.85 });
        if (kind === 'olive') mb.sphere(V(-0.24, 0.66, 0.12), V(0.3, 0.22, 0.28), C(0.48, 0.55, 0.38), 1, { seed: 20, rough: 0.08, bottomShade: 0.3, smoothN: 0.7 });
        break;
      case 'umbrella':   // 石松（地中海伞松）：高直的干、扁平的伞冠
      case 'umbrellaLo':
        mb.tube([V(0, 0, 0), V(0.05, 0.62, 0), V(0.1, 1.18, 0.02)], [0.07, 0.05, 0.035], kind === 'umbrella' ? 6 : 5, bark);
        mb.sphere(V(0.1, 1.28, 0), V(0.6, 0.2, 0.56), C(0.24, 0.42, 0.24), kind === 'umbrella' ? 2 : 1, { seed: 15, rough: 0.06, bottomShade: 0.4, smoothN: kind === 'umbrella' ? 0.65 : 0.9, flat: -0.4 });
        break;
      case 'rock':       // 圆润的石块（单位半径）
        mb.sphere(V(0, 0.35, 0), V(1, 0.72, 0.9), C(0.47, 0.46, 0.43), 1, { seed: 17, rough: 0.16, flat: -0.45, bottomShade: 0.3, top: 0.12 });
        break;
      // ---- 第三版修订：平原格的点缀（不挡格子中心）与偶见的村舍、水井、大车 --
      case 'bush':       // 灌木：两团平滑的叶簇
        mb.sphere(V(0, 0.13, 0), V(0.24, 0.17, 0.21), C(0.36, 0.54, 0.27), 1, { seed: 21, rough: 0.12, bottomShade: 0.35, flat: -0.5, smoothN: 0.6 });
        mb.sphere(V(0.15, 0.1, 0.06), V(0.15, 0.12, 0.14), C(0.4, 0.58, 0.3), 1, { seed: 22, rough: 0.1, bottomShade: 0.3, flat: -0.5, smoothN: 0.6 });
        break;
      case 'scrub':      // 荒漠 / 草原的矮灌丛
        mb.sphere(V(0, 0.07, 0), V(0.2, 0.1, 0.18), C(0.5, 0.52, 0.32), 1, { seed: 23, rough: 0.22, bottomShade: 0.3, flat: -0.4, smoothN: 0.4 });
        break;
      case 'hay':        // 草垛
        mb.lathe(V(0, 0, 0), [[0.15, 0], [0.16, 0.1], [0.14, 0.26], [0.07, 0.38], [0.012, 0.42], [0, 0.43]], 12, C(0.7, 0.62, 0.42));
        mb.lathe(V(0, 0.17, 0), [[0.15, 0], [0.145, 0.03]], 12, C(0.52, 0.44, 0.3));
        break;
      case 'fence': {    // 一段木栅栏：三根桩、两道横杆
        const wd = C(0.47, 0.35, 0.22);
        for (const x of [-0.4, 0, 0.4]) mb.tube([V(x, 0, 0), V(x, 0.26, 0)], [0.022, 0.02], 5, wd, true);
        for (const y of [0.1, 0.2]) mb.tube([V(-0.42, y, 0), V(0.42, y, 0)], [0.013, 0.013], 4, shade(wd, 0.06), false);
        break;
      }
      case 'flowers': {  // 一片野花（白色花瓣，实例色着色）
        const up = V(0, 1, 0);
        for (let k = 0; k < 9; k++) {
          const a = k * 2.4, r = 0.05 + (k % 4) * 0.045, x = Math.cos(a) * r, z = Math.sin(a) * r, y = 0.04 + (k % 3) * 0.025, pr = 0.028;
          for (let q = 0; q < 5; q++) {
            const a0 = q * Math.PI * 2 / 5, a1 = a0 + Math.PI * 2 / 5;
            mb.triN(V(x, y + 0.004, z), V(x + Math.cos(a0) * pr, y, z + Math.sin(a0) * pr), V(x + Math.cos(a1) * pr, y, z + Math.sin(a1) * pr), up, up, up, C(1, 1, 1));
          }
          mb.tube([V(x, 0, z), V(x, y, z)], [0.004, 0.004], 3, C(0.45, 0.62, 0.32), false);
        }
        break;
      }
      case 'cairn':      // 草原上的石堆（敖包）
        mb.sphere(V(0, 0.08, 0), V(0.2, 0.1, 0.18), C(0.56, 0.54, 0.5), 1, { seed: 31, rough: 0.12, flat: -0.4, smoothN: 0.4 });
        mb.sphere(V(0.02, 0.2, 0), V(0.13, 0.08, 0.12), C(0.6, 0.58, 0.54), 1, { seed: 32, rough: 0.12, smoothN: 0.4 });
        mb.sphere(V(0, 0.29, 0.01), V(0.07, 0.06, 0.07), C(0.62, 0.6, 0.56), 1, { seed: 33, rough: 0.1, smoothN: 0.4 });
        mb.tube([V(0, 0.3, 0), V(0, 0.55, 0)], [0.01, 0.008], 4, C(0.45, 0.35, 0.22), false);
        break;
      case 'well': {     // 水井：石砌井圈、两根井柱与小顶
        const st = C(0.62, 0.6, 0.55), wd = C(0.42, 0.3, 0.2);
        mb.lathe(V(0, 0, 0), [[0.17, 0], [0.17, 0.15, null, true], [0.12, 0.15, null, true], [0.12, 0.03]], 12, st);
        for (const x of [-0.15, 0.15]) mb.tube([V(x, 0.1, 0), V(x, 0.45, 0)], [0.016, 0.015], 5, wd, false);
        mb.tube([V(-0.18, 0.43, 0), V(0.18, 0.43, 0)], [0.014, 0.014], 5, wd, false);
        BattleLook.gable(mb, 0, 0.45, 0, 0.44, 0.3, 0.12, C(0.38, 0.34, 0.32), false);
        break;
      }
      case 'cart': {     // 大车：车厢、两只轮子、辕杆
        const wd = C(0.5, 0.37, 0.23);
        mb.box(V(0, 0.2, 0), V(0.46, 0.06, 0.26), wd);
        for (const sz of [-1, 1]) mb.box(V(0, 0.27, sz * 0.13), V(0.46, 0.09, 0.025), shade(wd, -0.08));
        const M0 = mb.M;
        for (const sz of [-1, 1]) {
          mb.M = new THREE.Matrix4().makeTranslation(0, 0.15, sz * 0.16).multiply(new THREE.Matrix4().makeRotationX(Math.PI / 2));
          mb.lathe(V(0, -0.015, 0), [[0.15, 0, null, true], [0.15, 0.03, null, true], [0.04, 0.03], [0.04, 0.05]], 12, shade(wd, -0.15));
        }
        mb.M = M0;
        mb.tube([V(0.22, 0.2, 0.06), V(0.6, 0.06, 0.06)], [0.012, 0.012], 4, wd, false);
        mb.tube([V(0.22, 0.2, -0.06), V(0.6, 0.06, -0.06)], [0.012, 0.012], 4, wd, false);
        mb.box(V(-0.05, 0.3, 0), V(0.3, 0.08, 0.2), C(0.78, 0.68, 0.4));
        break;
      }
      case 'hut_han': case 'hut_tile': case 'hut_flat': case 'hut_yurt': case 'hut_thatch': case 'hut_long': case 'hut_round': case 'hut_stilt': {
        // 村舍（按战场所在城池的文化）：约 0.9 × 0.6 单位，高约 0.8
        const st = kind.slice(4);
        if (st === 'yurt') {
          mb.lathe(V(0, 0, 0), [[0.34, 0], [0.34, 0.24], [0.31, 0.29], [0.14, 0.44], [0.04, 0.48], [0, 0.49]], 14, C(0.88, 0.84, 0.74));
          mb.lathe(V(0, 0.18, 0), [[0.345, 0], [0.345, 0.035]], 14, C(0.62, 0.28, 0.18));
          mb.box(V(0, 0.12, -0.33), V(0.14, 0.24, 0.04), C(0.72, 0.32, 0.14));
        } else if (st === 'round') {
          mb.lathe(V(0, 0, 0), [[0.34, 0], [0.34, 0.3]], 14, C(0.64, 0.54, 0.4));
          mb.lathe(V(0, 0.25, 0), [[0.44, 0], [0.22, 0.24], [0.03, 0.5], [0, 0.52]], 14, C(0.68, 0.57, 0.36));
          mb.box(V(0, 0.13, -0.34), V(0.13, 0.26, 0.03), C(0.3, 0.2, 0.13));
        } else if (st === 'flat') {
          const c = C(0.8, 0.66, 0.48);
          mb.box(V(0, 0.24, 0), V(0.8, 0.48, 0.55), c);
          mb.box(V(-0.18, 0.62, 0.05), V(0.36, 0.28, 0.36), shade(c, 0.03));
          for (const [x, z, w, d] of [[0, -0.265, 0.8, 0.04], [0, 0.265, 0.8, 0.04], [-0.39, 0, 0.04, 0.55], [0.39, 0, 0.04, 0.55]]) mb.box(V(x, 0.52, z), V(w, 0.07, d), shade(c, 0.05));
          mb.box(V(0.2, 0.14, -0.28), V(0.13, 0.28, 0.02), C(0.3, 0.2, 0.13));
        } else if (st === 'long') {
          mb.box(V(0, 0.13, 0), V(1.0, 0.26, 0.5), C(0.46, 0.34, 0.22));
          BattleLook.gable(mb, 0, 0.24, 0, 1.1, 0.78, 0.5, C(0.68, 0.57, 0.36), false);
        } else if (st === 'stilt') {
          for (const sx of [-1, 1]) for (const sz of [-1, 1]) mb.tube([V(sx * 0.3, 0, sz * 0.2), V(sx * 0.3, 0.24, sz * 0.2)], [0.02, 0.02], 4, C(0.4, 0.3, 0.2), false);
          mb.box(V(0, 0.4, 0), V(0.7, 0.3, 0.46), C(0.66, 0.56, 0.38));
          BattleLook.gable(mb, 0, 0.54, 0, 0.9, 0.74, 0.4, C(0.68, 0.57, 0.36), false);
        } else {
          const tile = st === 'tile', th = st === 'thatch';
          const wallC = tile ? C(0.88, 0.84, 0.76) : th ? C(0.74, 0.58, 0.42) : C(0.86, 0.8, 0.68);
          mb.box(V(0, 0.22, 0), V(0.78, 0.44, 0.5), wallC);
          mb.box(V(0.18, 0.14, -0.255), V(0.13, 0.28, 0.02), C(0.3, 0.2, 0.13));
          if (st === 'han') mb.chineseRoof(V(0, 0.42, 0), 0.98, 0.68, 0.3, C(0.3, 0.32, 0.36));
          else BattleLook.gable(mb, 0, 0.42, 0, 0.92, 0.66, th ? 0.36 : 0.24, th ? C(0.68, 0.57, 0.36) : C(0.72, 0.36, 0.24), false);
        }
        break;
      }
      case 'tuft': {     // 草丛：一簇向外弯的宽草叶（双面，每片两节）
        const g = C(0.5, 0.68, 0.34);
        for (let k = 0; k < 5; k++) {
          const a = k * Math.PI * 2 / 5 + (k % 2) * 0.3, ca = Math.cos(a), sa = Math.sin(a), w = 0.055;
          const h = 0.17 + (k % 3) * 0.05, lean = 0.1 + (k % 2) * 0.05;
          const p0 = V(-sa * w, 0, ca * w), p1 = V(sa * w, 0, -ca * w), tp = V(ca * lean, h, sa * lean);
          const c0 = shade(g, -0.12 + (k % 3) * 0.07);
          mb.tri(p0, tp, p1, c0); mb.tri(p0, p1, tp, c0);
        }
        break;
      }
    }
    const geo = mb.toGeometry();
    // 草叶、椰叶：法线朝上（像草片一样整体受光，不显出一片片的折面）
    if (kind === 'tuft' || kind === 'flowers') { const n = geo.attributes.normal; for (let i = 0; i < n.count; i++) n.setXYZ(i, 0, 1, 0); }
    geo.userData.sharedGeometry = true;
    PROP_TPL[kind] = geo;
    return geo;
  }
  // 第三版修订：树种按「战场群落 + 防守城池的文化」选（与 js/clash.js 的布景一致）：
  // 沙漠 / 地中海：椰枣、橄榄、柏、灌丛（沙漠格不长松）；温带：阔叶、松、杨；热带：椰、阔叶；寒带：云杉 / 松；稀树草原：金合欢。
  // 干旱文化（安息、阿拉伯、西域）的城池落在世界地图的温带 / 草原格（河谷绿洲，如泰西封）时，按沙漠绿洲种树（西域是胡杨）；
  // 阿克苏姆 / 库施按稀树草原；天竺、东南亚的温带格按热带。spruce / poplar 是 pine / broad 模板的非等比缩放。
  // 安息、阿拉伯的改写只在北纬 39° 以南：高加索的姆茨赫塔、加巴拉、阿尔塔沙特（文化归安息）是湿润的温带林，按世界地图的群落种树。
  const CULT_TREE_BIOME = { persia: 2, arab: 2, tarim: 2, moor: 5, africa: 4, india: 3, seasia: 3 };
  const ARID_LAT_MAX = 39;
  function treeBiome(bi, cult, lat) {
    const d = CULT_TREE_BIOME[cult] != null ? CULT_TREE_BIOME[cult] : CULT_TREE_BIOME[SG.cultureBase ? SG.cultureBase(cult) : 'han'];
    if (d === 2 && cult !== 'tarim' && lat != null && lat >= ARID_LAT_MAX) return bi;
    if ((d === 2 || d === 4) && (bi === 0 || bi === 1)) return d;
    if (d === 2 && cult !== 'tarim' && (bi === 6 || bi === 7)) return 5;      // 扎格罗斯等干旱山地：橄榄、柏（不是云杉）
    if (d === 3 && bi === 0) return 3;
    if (d === 5 && (bi === 0 || bi === 1)) return 5;
    return bi;
  }
  function treeKinds(biome, near, cult) {
    const B = near ? 'broad' : 'broadLo', O = near ? 'olive' : 'oliveLo', P = near ? 'pine' : 'pineLo', A = near ? 'acacia' : 'acaciaLo', U = near ? 'umbrella' : 'umbrellaLo';
    switch (biome) {
      case 1: return [['poplar', 0.45], [B, 0.4], [P, 0.15]];                                     // 草原：杨、阔叶，偶有松
      case 2: return cult === 'tarim' ? [['poplar', 0.7], [B, 0.3]]                                // 西域绿洲：胡杨（不种橄榄）
        : cult === 'africa' ? [['palm', 0.55], [A, 0.45]]                                           // 努比亚的尼罗河谷：椰枣、金合欢
        : [['palm', 0.6], [O, 0.3], ['cypress', 0.1]];                                             // 沙漠绿洲：椰枣、橄榄、柏
      case 3: return [[B, 0.55], ['palm', 0.45]];                                                 // 热带
      case 4: return cult === 'india' || cult === 'seasia' ? [[A, 0.5], ['palm', 0.3], [B, 0.2]]    // 天竺的干热林：金合欢、椰、阔叶
        : [[A, 0.8], ['palm', 0.2]];                                                                // 稀树草原
      case 5: return [[O, 0.35], ['cypress', 0.2], [U, 0.2], [B, 0.25]];                          // 地中海
      case 6: return [['spruce', 0.6], [P, 0.3], ['poplar', 0.1]];                                 // 寒带
      case 7: return [[P, 0.6], ['spruce', 0.3], [B, 0.1]];                                        // 高山
      default: return [[B, 0.5], [P, 0.3], ['poplar', 0.2]];                                       // 温带
    }
  }
  // 山岳格上的树（沙漠不种）
  const MOUNTAIN_TREE = ['pine', 'pine', null, 'broad', 'acacia', 'olive', 'spruce', 'spruce'];
  // 别名 → [模板（近 / 远）, 横向缩放, 纵向缩放]
  const TREE_ALIAS = { spruce: ['pine', 'pineLo', 0.72, 1.32], poplar: ['broad', 'broadLo', 0.55, 1.45] };
  function pickW(list, r) { let s = 0; for (const e of list) s += e[1]; r *= s; for (const e of list) { r -= e[1]; if (r <= 0) return e[0]; } return list[list.length - 1][0]; }
  // 树冠的群落色（乘在模板顶点色上）
  const LEAF_MUL = [[1, 1, 1], [1.08, 1.02, 0.86], [1.05, 1.0, 0.9], [0.86, 1.04, 0.84], [1.1, 1.02, 0.84], [1.0, 0.97, 0.9], [0.86, 0.94, 0.95], [0.9, 0.92, 0.92]];

  // ---- 城池（城墙、城门、角楼、本阵）：连续的平滑城墙，按文化换样式；砖石材质 --
  // 城墙沿城池外圈格子的中心线（距本阵格中心 2 格）；城门格是城墙上的缺口（两侧门楼，部队站在缺口里，镜头看得见）。
  // 风格族：汉 / 高句丽（灰砖、方垛口、方角楼 + 歇山顶门楼）、罗马（浅石、方垛口、圆塔 + 锥顶红瓦）、
  // 西亚土坯（安息、阿拉伯、西域、贵霜：阶梯垛口、圆塔）、木栅（倭、南海、夷、凯尔特、日耳曼、草原：土垒 + 木栅 + 望楼）。
  function fortFamily(style) {
    if (style === 'roman') return 'roman';
    if (style === 'persia' || style === 'arab' || style === 'tarim' || style === 'kushan') return 'mud';
    if (style === 'wa' || style === 'stilt' || style === 'celt' || style === 'german' || style === 'steppe') return 'timber';
    return 'han';
  }
  function fortColors(style) {
    const k = BattleLook.col(style) || {};
    const fam = fortFamily(style);
    if (fam === 'han') return { stone: style === 'korea' ? C(0.6, 0.6, 0.57) : C(0.6, 0.58, 0.54), cap: C(0.5, 0.48, 0.45), roof: style === 'korea' ? C(0.25, 0.26, 0.29) : C(0.24, 0.27, 0.33), timber: C(0.6, 0.2, 0.15), gold: C(0.86, 0.66, 0.24) };
    if (fam === 'roman') return { stone: k.wall || C(0.82, 0.78, 0.68), cap: shade(k.wall || C(0.82, 0.78, 0.68), -0.1), roof: k.roof || C(0.74, 0.33, 0.21), timber: k.door || C(0.38, 0.24, 0.14), gold: C(0.86, 0.7, 0.36) };
    if (fam === 'mud') return { stone: k.wall || C(0.78, 0.64, 0.46), cap: shade(k.wall || C(0.78, 0.64, 0.46), 0.05), roof: k.dome || C(0.84, 0.78, 0.66), timber: k.door || C(0.3, 0.2, 0.13), gold: k.trim || C(0.24, 0.5, 0.56) };
    return { stone: C(0.44, 0.4, 0.3), cap: C(0.5, 0.42, 0.3), roof: k.thatch || C(0.62, 0.54, 0.34), timber: k.wood || C(0.48, 0.35, 0.22), gold: C(0.8, 0.66, 0.3) };
  }
  // 四边形（Unity 坐标），按 hint（大致的外向法线）自动定正反（MeshBuilder 的材质为单面）
  function qd(mb, a, b, c, d, col, hint) {
    const ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, wx = c.x - a.x, wy = c.y - a.y, wz = c.z - a.z;
    const nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
    if (nx * hint.x + ny * hint.y + nz * hint.z < 0) mb.quad(d, c, b, a, col); else mb.quad(a, b, c, d, col);
  }
  // 墙段：从 a 到 b（Unity 坐标的地面点），截面梯形（下宽 w0、上宽 w1、高 h），外侧方向 out（单位向量，xz）
  function fortWallRun(mb, a, b, h, w0, w1, out, fam, k, gy) {
    const dx = b.x - a.x, dz = b.z - a.z, L = Math.hypot(dx, dz);
    if (L < 0.05) return;
    const tx = dx / L, tz = dz / L, ox = out.x, oz = out.z;
    const P = (s, o, y) => V(a.x + tx * s + ox * o, gy + y, a.z + tz * s + oz * o);
    const OUT = { x: ox, y: 0.15, z: oz }, IN = { x: -ox, y: 0.15, z: -oz }, UP = { x: 0, y: 1, z: 0 }, T0 = { x: -tx, y: 0, z: -tz }, T1 = { x: tx, y: 0, z: tz };
    if (fam === 'timber') {
      // 土垒 + 木栅（尖桩）
      const bank = shade(k.stone, 0.02), bh = h * 0.45;
      qd(mb, P(0, -w0 / 2, 0), P(0, -w1 / 2, bh), P(L, -w1 / 2, bh), P(L, -w0 / 2, 0), shade(bank, -0.06), IN);
      qd(mb, P(0, w0 / 2, 0), P(0, w1 / 2, bh), P(L, w1 / 2, bh), P(L, w0 / 2, 0), bank, OUT);
      qd(mb, P(0, -w1 / 2, bh), P(0, w1 / 2, bh), P(L, w1 / 2, bh), P(L, -w1 / 2, bh), shade(bank, 0.04), UP);
      for (const [s, hn] of [[0, T0], [L, T1]]) { mb.tri(P(s, -w0 / 2, 0), P(s, -w1 / 2, bh), P(s, w0 / 2, 0), bank); mb.tri(P(s, -w0 / 2, 0), P(s, w0 / 2, 0), P(s, -w1 / 2, bh), bank); qd(mb, P(s, -w1 / 2, bh), P(s, w1 / 2, bh), P(s, w0 / 2, 0), P(s, -w0 / 2, 0), bank, hn); }
      const n = Math.max(2, Math.round(L / 0.19));
      for (let i = 0; i <= n; i++) {
        const s = i * L / n, ph = h * (0.95 + ((i * 7) % 5) * 0.025);
        const base = P(s, w1 * 0.25, bh - 0.05);
        mb.smoothCylinder(base, 0.075, 0.07, ph * 0.55, 6, shade(k.timber, ((i * 3) % 4) * 0.03), false);
        mb.lathe(V(base.x, base.y + ph * 0.55, base.z), [[0.07, 0], [0, 0.16]], 6, shade(k.timber, 0.08));
      }
      return;
    }
    // 石 / 砖 / 土坯：墙身（外侧略有收分）、勒脚、墙顶、垛口、内侧女墙
    const cs = k.stone, cap = k.cap;
    qd(mb, P(0, w0 / 2, 0), P(0, w1 / 2, h), P(L, w1 / 2, h), P(L, w0 / 2, 0), cs, OUT);
    qd(mb, P(0, -w0 / 2, 0), P(0, -w1 / 2, h), P(L, -w1 / 2, h), P(L, -w0 / 2, 0), shade(cs, -0.08), IN);
    qd(mb, P(0, -w1 / 2, h), P(0, w1 / 2, h), P(L, w1 / 2, h), P(L, -w1 / 2, h), shade(cap, 0.02), UP);
    qd(mb, P(0, -w0 / 2, 0), P(0, -w1 / 2, h), P(0, w1 / 2, h), P(0, w0 / 2, 0), shade(cs, -0.12), T0);
    qd(mb, P(L, -w0 / 2, 0), P(L, -w1 / 2, h), P(L, w1 / 2, h), P(L, w0 / 2, 0), shade(cs, -0.12), T1);
    qd(mb, P(0, w0 / 2 + 0.05, 0), P(0, w0 / 2 + 0.03, 0.16), P(L, w0 / 2 + 0.03, 0.16), P(L, w0 / 2 + 0.05, 0), shade(cs, -0.16), OUT);   // 勒脚
    qd(mb, P(0, w1 / 2 + 0.04, h - 0.06), P(0, w1 / 2 + 0.04, h), P(L, w1 / 2 + 0.04, h), P(L, w1 / 2 + 0.04, h - 0.06), shade(cap, -0.05), OUT);  // 檐线
    // 垛口
    const n = Math.max(2, Math.round(L / 0.42));
    for (let i = 0; i < n; i++) {
      const s = (i + 0.5) * L / n, c = P(s, w1 / 2 - 0.07, h);
      if (fam === 'mud') BattleLook.stepMerlon(mb, c.x, c.y, c.z, 0.62, cs);
      else mb.box(V(c.x, c.y + 0.13, c.z), V(Math.abs(tx) * 0.24 + Math.abs(tz) * 0.13 + 0.02, 0.26, Math.abs(tz) * 0.24 + Math.abs(tx) * 0.13 + 0.02), shade(cs, 0.04), 0.06);
    }
    // 内侧矮女墙
    const q0 = P(0, -w1 / 2 + 0.05, h), q1 = P(L, -w1 / 2 + 0.05, h);
    mb.box(V((q0.x + q1.x) / 2, gy + h + 0.06, (q0.z + q1.z) / 2), V(Math.abs(tx) * L + 0.1 * Math.abs(tz), 0.12, Math.abs(tz) * L + 0.1 * Math.abs(tx)), shade(cs, -0.04), 0.05);
  }
  // 塔：中心 (x, z)，地面 gy；方塔（汉）带歇山顶亭，圆塔（罗马 / 土坯）带垛口环与锥顶 / 平顶，木望楼
  function fortTower(mb, fam, k, x, z, gy, h, r, roof) {
    if (fam === 'han') {
      mb.box(V(x, gy + h / 2, z), V(r * 2, h, r * 2), k.stone, 0.05);
      mb.box(V(x, gy + 0.08, z), V(r * 2 + 0.12, 0.16, r * 2 + 0.12), shade(k.stone, -0.15));
      for (let i = 0; i < 4; i++) {
        const a = i * Math.PI / 2;
        for (const s of [-0.55, 0, 0.55]) {
          const px = x + Math.cos(a) * (r - 0.06) - Math.sin(a) * s * r, pz = z + Math.sin(a) * (r - 0.06) + Math.cos(a) * s * r;
          mb.box(V(px, gy + h + 0.12, pz), V(0.2, 0.24, 0.2), shade(k.stone, 0.05), 0.06);
        }
      }
      if (roof) {
        // 亭：四根红柱 + 曲面屋顶
        for (const sx of [-1, 1]) for (const sz of [-1, 1]) mb.smoothCylinder(V(x + sx * r * 0.55, gy + h, z + sz * r * 0.55), 0.06, 0.06, 0.55, 6, k.timber, false);
        mb.box(V(x, gy + h + 0.56, z), V(r * 1.25, 0.08, r * 1.25), shade(k.timber, -0.2));
        mb.chineseRoof(V(x, gy + h + 0.6, z), r * 2.2, r * 2.2, 0.6, k.roof);
      }
      return;
    }
    if (fam === 'timber') {
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) mb.smoothCylinder(V(x + sx * r * 0.6, gy, z + sz * r * 0.6), 0.07, 0.06, h + 0.4, 6, k.timber, false);
      mb.box(V(x, gy + h + 0.4, z), V(r * 1.6, 0.1, r * 1.6), shade(k.timber, 0.05));
      for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2; mb.box(V(x + Math.cos(a) * r * 0.78, gy + h + 0.62, z + Math.sin(a) * r * 0.78), V(Math.abs(Math.sin(a)) * r * 1.5 + 0.06, 0.34, Math.abs(Math.cos(a)) * r * 1.5 + 0.06), shade(k.timber, -0.06)); }
      mb.lathe(V(x, gy + h + 0.8, z), [[r * 1.15, 0, null, true], [r * 0.3, 0.55], [0, 0.72]], 10, k.roof);
      return;
    }
    // 圆塔
    mb.lathe(V(x, gy, z), [[r * 1.1, 0], [r * 1.02, 0.25], [r, h], [r * 1.08, h + 0.04, null, true], [r * 1.08, h + 0.12]], 14, k.stone, { capTop: true });
    const n = 10;
    for (let i = 0; i < n; i++) {
      const a = i * Math.PI * 2 / n, px = x + Math.cos(a) * r * 0.95, pz = z + Math.sin(a) * r * 0.95;
      if (fam === 'mud') BattleLook.stepMerlon(mb, px, gy + h + 0.12, pz, 0.5, k.stone);
      else mb.box(V(px, gy + h + 0.24, pz), V(0.2, 0.24, 0.2), shade(k.stone, 0.05), 0.06);
    }
    if (roof && fam === 'roman') mb.lathe(V(x, gy + h + 0.12, z), [[r * 0.82, 0, null, true], [r * 0.35, 0.5], [0, 0.78]], 14, k.roof);
    else if (roof && fam === 'mud') mb.sphere(V(x, gy + h + 0.14, z), V(r * 0.55, r * 0.5, r * 0.55), k.roof, 1, { flat: 0, top: 0.1 });
  }
  // 门楼：城门格上的缺口，两侧门楼 + 门扇（敞开，向内）
  function fortGate(mb, fam, k, gx, gz, along, out, gy, h, style) {
    const tx = along.x, tz = along.z;
    const gap = 0.75;   // 缺口半宽
    const r = fam === 'han' ? 0.5 : fam === 'timber' ? 0.45 : 0.55;
    for (const s of [-1, 1]) {
      const cx = gx + tx * (gap + r * 0.9) * s, cz = gz + tz * (gap + r * 0.9) * s;
      fortTower(mb, fam, k, cx, cz, gy, h + 0.45, r, true);
      // 门扇（敞开、贴在门洞内侧）
      const hx = gx + tx * gap * s - out.x * 0.15, hz = gz + tz * gap * s - out.z * 0.15;
      const lx = hx - out.x * 0.5 - tx * 0.1 * s, lz = hz - out.z * 0.5 - tz * 0.1 * s;
      mb.quad(V(hx, gy, hz), V(hx, gy + h * 0.78, hz), V(lx, gy + h * 0.78, lz), V(lx, gy, lz), shade(k.timber, -0.05));
      mb.quad(V(lx, gy, lz), V(lx, gy + h * 0.78, lz), V(hx, gy + h * 0.78, hz), V(hx, gy, hz), shade(k.timber, -0.18));
    }
    // 门洞两侧的门框与门槛
    mb.box(V(gx, gy + 0.03, gz), V(Math.abs(tx) * gap * 2 + Math.abs(out.x) * 0.9, 0.06, Math.abs(tz) * gap * 2 + Math.abs(out.z) * 0.9), shade(k.cap || k.stone, -0.1));
    if (fam === 'roman' || fam === 'mud') {
      // 拱门上方的拱楣（高处，不挡住门洞里的部队）
      const yb = gy + h + 0.2;
      mb.box(V(gx, yb + 0.12, gz), V(Math.abs(tx) * (gap * 2 + 0.2) + Math.abs(out.x) * 0.5, 0.24, Math.abs(tz) * (gap * 2 + 0.2) + Math.abs(out.z) * 0.5), shade(k.stone, 0.04), 0.05);
    } else if (fam === 'han') {
      // 门额（匾）
      mb.box(V(gx + out.x * 0.3, gy + h + 0.32, gz + out.z * 0.3), V(Math.abs(tx) * 0.62 + Math.abs(out.x) * 0.06, 0.26, Math.abs(tz) * 0.62 + Math.abs(out.z) * 0.06), C(0.2, 0.18, 0.2), 0);
    }
  }
  // 汉式本阵：三层台基、红柱殿身、重檐金顶
  function fortKeepHan(mb, k, cx, gy, cz, capital) {
    const z = cz + 0.45;
    mb.box(V(cx, gy + 0.09, z), V(1.9, 0.18, 1.1), shade(k.stone, 0.12));
    mb.box(V(cx, gy + 0.24, z + 0.02), V(1.66, 0.14, 0.94), shade(k.stone, 0.18));
    mb.box(V(cx, gy + 0.08, z - 0.66), V(0.5, 0.16, 0.3), shade(k.stone, 0.1));   // 前阶
    for (let i = -2; i <= 2; i++) mb.smoothCylinder(V(cx + i * 0.34, gy + 0.31, z - 0.36), 0.045, 0.045, 0.62, 6, k.timber, false);
    mb.box(V(cx, gy + 0.62, z + 0.08), V(1.38, 0.62, 0.6), C(0.66, 0.22, 0.18));
    mb.box(V(cx, gy + 0.6, z - 0.235), V(0.34, 0.5, 0.02), C(0.32, 0.18, 0.12));
    const gold = capital ? C(0.9, 0.7, 0.24) : C(0.85, 0.65, 0.2);
    mb.chineseRoof(V(cx, gy + 0.93, z), 2.1, 1.25, 0.42, gold);
    mb.box(V(cx, gy + 1.15, z), V(1.0, 0.24, 0.48), C(0.62, 0.2, 0.16));
    mb.chineseRoof(V(cx, gy + 1.3, z), 1.55, 0.95, 0.55, gold);
  }
  function buildFort(view, mb, Tm, look, defCol) {
    const m = view.model, ca = Tm.castle, st = look.style, fam = fortFamily(st), k = fortColors(st);
    const gy = BASE_Y + 0.06;
    const cxU = ORIGIN.x + ca.x * T + T / 2, czU = ORIGIN.z + ca.y * T + T / 2, R = 2 * T;
    const h = fam === 'timber' ? 1.25 : 1.45, w0 = fam === 'timber' ? 1.3 : 1.12, w1 = fam === 'timber' ? 0.8 : 0.9;
    // 内院铺地
    const pv = fam === 'timber' ? C(0.5, 0.44, 0.32) : fam === 'mud' ? shade(k.stone, 0.06) : C(0.6, 0.58, 0.53);
    const ir = R - w0 / 2 + 0.05;
    mb.flatQuad(V(cxU - ir, gy + 0.012, czU - ir), V(cxU - ir, gy + 0.012, czU + ir), V(cxU + ir, gy + 0.012, czU + ir), V(cxU + ir, gy + 0.012, czU - ir), pv);
    // 城墙（四边；城门处留缺口）
    const isGate = (x, y) => x >= 0 && y >= 0 && x < m.W && y < m.H && m.map[x][y] === TR().Gate;
    const sides = [
      { a: V(cxU - R, 0, czU - R), b: V(cxU + R, 0, czU - R), out: { x: 0, z: -1 }, gate: isGate(ca.x, ca.y - 2) ? V(cxU, 0, czU - R) : null },
      { a: V(cxU + R, 0, czU - R), b: V(cxU + R, 0, czU + R), out: { x: 1, z: 0 }, gate: isGate(ca.x + 2, ca.y) ? V(cxU + R, 0, czU) : null },
      { a: V(cxU + R, 0, czU + R), b: V(cxU - R, 0, czU + R), out: { x: 0, z: 1 }, gate: isGate(ca.x, ca.y + 2) ? V(cxU, 0, czU + R) : null },
      { a: V(cxU - R, 0, czU + R), b: V(cxU - R, 0, czU - R), out: { x: -1, z: 0 }, gate: isGate(ca.x - 2, ca.y) ? V(cxU - R, 0, czU) : null },
    ];
    const cr = fam === 'han' ? 0.62 : fam === 'timber' ? 0.5 : 0.7;   // 角楼半径（墙段在角楼处收短）
    for (const s of sides) {
      const L = s.a.distanceTo(s.b), tx = (s.b.x - s.a.x) / L, tz = (s.b.z - s.a.z) / L;
      const A = V(s.a.x + tx * cr * 0.6, 0, s.a.z + tz * cr * 0.6), B = V(s.b.x - tx * cr * 0.6, 0, s.b.z - tz * cr * 0.6);
      if (s.gate) {
        const gw = 0.75 + (fam === 'han' ? 0.5 : 0.55) * 1.8;
        const g1 = V(s.gate.x - tx * gw, 0, s.gate.z - tz * gw), g2 = V(s.gate.x + tx * gw, 0, s.gate.z + tz * gw);
        fortWallRun(mb, A, g1, h, w0, w1, s.out, fam, k, gy);
        fortWallRun(mb, g2, B, h, w0, w1, s.out, fam, k, gy);
        fortGate(mb, fam, k, s.gate.x, s.gate.z, { x: tx, z: tz }, s.out, gy, h, st);
      } else fortWallRun(mb, A, B, h, w0, w1, s.out, fam, k, gy);
      // 墙段中间的马面（汉式）
      if (fam === 'han' && !s.gate) {
        const mx = (s.a.x + s.b.x) / 2 + s.out.x * 0.55, mz = (s.a.z + s.b.z) / 2 + s.out.z * 0.55;
        mb.box(V(mx, gy + h / 2, mz), V(Math.abs(s.out.x) * 0.5 + Math.abs(tx) * 0.9, h, Math.abs(s.out.z) * 0.5 + Math.abs(tz) * 0.9), shade(k.stone, -0.03), 0.05);
      }
    }
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) fortTower(mb, fam, k, cxU + sx * R, czU + sz * R, gy, h + 0.3, cr, true);
    // 本阵
    const kh = gy;
    if (st === 'han' || !BattleLook.keep(mb, st, cxU, kh, czU)) fortKeepHan(mb, k, cxU, kh, czU, false);
    mb.flag(V(cxU + 0.85, kh, czU + 0.85), 2.4, 0.8, 0.55, defCol);
  }

  // =========================================== 第四版：Lanternvale 风格的战场布景 --
  // DESIGN-V4 §2（美术推广 · 战场）：按试点批准的画风（SG.LP / SG.LPProps）重画布景——
  //   地面：绘制式的顶点色（各群落的草地四色斑块、森林地面、赭黄的丘坡、拼布般的田块、泥土路、沙岸、按面着色的岩石与积雪）
  //         + LP 的笔触细节纹理；坡地按面平面着色并逐面抖动亮度（Lanternvale 的「手切」山坡），地形不描墨线；
  //         格线、棋盘边框与棋盘外的略暗照旧画在着色器里（LP 光照：午后的暖光、黄金时刻提亮、夜色调、灯池、雾）。
  //   景物：树按群落 / 纬度 / 文化取 SG.LPProps 的配方（槐 · 栎 · 榆 · 柳 · 杨 · 松 · 云杉 · 柏 · 海枣 · 椰 · 橄榄 · 金合欢 · 黄山松 · 竹），
  //         石、灌木、草垛、栅栏（按文化：木栅 / 篱笆 / 石垣 / 竹篱）、水井、大车、村舍（按文化）同样取配方；每种配方一个实例化网格 +
  //         一个实例化墨线；挡在部队与镜头之间的树抖动溶解（换成单独的淡出副本）。草丛、野花、卵石、落叶合成一个不描墨线、随风摆的网格。
  //   城池：SG.LPProps.parts 的夯土（汉）/ 石（罗马、高句丽）/ 土坯（西亚）墙、垛口、勒脚与歇山 / 攒尖顶的角楼、门楼，各文化的本阵；
  //         按米建模，缩放到棋盘（城墙走道 4.1 米 = 1.45 单位，与部队站立的墙顶同高）。桥、登陆的船同样改为 LP。
  //   部队：脚下柔和的圆影（LP.BlobShadows，全部部队一次绘制）。
  // 没有载入 js/lowpoly.js / js/lp-props.js 时（旧测试页）一律退回第三版的画法。
  const LPOK = () => !!(SG.LP && SG.LP.MeshBuilder && SG.LP.material && SG.LP.instances && SG.LPProps && SG.LPProps.geometry);
  const _hx = {};
  const hx = h => _hx[h] || (_hx[h] = SG.LP.Paint.hex(h));
  const mix3 = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  const mul3 = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  // 值噪声（0..1，按整数格点哈希，平滑插值）
  function vnoise(x, z, s) {
    const f = (i, j) => SG.LP.hash01([i, j, s], 17);
    const ix = Math.floor(x), iz = Math.floor(z), tx = x - ix, tz = z - iz, sx = tx * tx * (3 - 2 * tx), sz = tz * tz * (3 - 2 * tz);
    const a = f(ix, iz), b = f(ix + 1, iz), c = f(ix, iz + 1), d = f(ix + 1, iz + 1);
    return (a + (b - a) * sx) * (1 - sz) + (c + (d - c) * sx) * sz;
  }
  // 各群落的草地四色（亮 · 次亮 · 暗 · 最暗；BIOMES 顺序：温带、草原、沙漠、热带、稀树草原、地中海、寒带、高山），
  // 与试点地面一样向 #D2D294 混 16%（暖、柔）
  const MEADOW_HEX = [
    ['#98AE5C', '#AABD68', '#7E9A4C', '#6E8C46'],
    ['#B4AC64', '#C2B870', '#A09A56', '#8E8C4C'],
    ['#D4BE8A', '#DEC896', '#C6AE7A', '#B89E6C'],
    ['#84A852', '#94B65E', '#6C9446', '#5E8640'],
    ['#C2AE66', '#CEBC74', '#AE9C58', '#9E8E50'],
    ['#A6AA62', '#B4B66E', '#929858', '#848A4E'],
    ['#84A060', '#92AC6A', '#728E54', '#64804C'],
    ['#90A666', '#9EB270', '#7E9458', '#70864E'],
  ];
  let MEADOW = null;
  function meadowPal() {
    if (!MEADOW) MEADOW = MEADOW_HEX.map(p => p.map(h => mix3(hx(h), hx('#D2D294'), 0.16)));
    return MEADOW;
  }
  const CROPS_HEX = ['#D8BE6C', '#C9B060', '#B8C46A', '#9EB45E', '#C8A464', '#A8875A', '#D0C484'];
  // 地面一点的绘制色：pal = 群落四色（已按四角混合），n1 / n2 / n3 = 低 / 中 / 高频噪声
  function paintMeadow(pal, n1, n2, n3) {
    let c = mix3(mix3(pal[2], pal[0], sm(0.25, 0.75, n1)), pal[1], sm(0.55, 0.85, n2) * 0.7);
    c = mix3(c, pal[3], sm(0.32, 0.12, n2) * 0.6);
    return mul3(c, 0.93 + n3 * 0.12);
  }

  // ---- 棋盘的 LP 地形材质：SG.LP.material 的着色器（同一套光照）+ 坡地按面着色、逐面抖动 + 格线 / 棋盘边框 / 棋盘外略暗 --
  const BOARD_PLANAR = 0.55, BOARD_DETAIL_K = 0.6;   // 修订：笔触纹理弱一些，不把格线「迷彩」掉
  // 修订：田块、道路与河岸沙在着色器里画（棋盘外的网格稀疏，顶点色会把田块、道路糊成一片锈色的污迹）——
  //   田块：格坐标按 1.35 × 0.95 格分块（逐行错开），块色取 CROPS 的哈希；权重 = 顶点的农田权重（aMat.w 的补数）。
  //   道路：世界地图的道路距离场（G.road，每 0.5 格一个取样）做成一张小贴图，双线性取样后按距离画出边缘清楚的土路。
  //   河岸：水面上方 0.06–0.34 的高度带铺沙色（第三版地形的沙岸）。都在 lpLin 之前、按 sRGB 改写地面色，笔触细节照旧叠上。
  const BOARD_FS_PARS = `uniform vec4 sgBoard;
uniform float sgGridK;
uniform vec4 sgFacet;
uniform sampler2D sgRoadTex;
uniform vec4 sgRoadMap;
uniform vec2 sgRoadInv;
varying vec3 vSgW;
varying vec2 vSgFR;
float sgH21( vec2 p ) { return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) ) * 43758.5453 ); }
float sgN2( vec2 p ) {
  vec2 i = floor( p ), f = fract( p ); f = f * f * ( 3.0 - 2.0 * f );
  return mix( mix( sgH21( i ), sgH21( i + vec2( 1.0, 0.0 ) ), f.x ), mix( sgH21( i + vec2( 0.0, 1.0 ) ), sgH21( i + vec2( 1.0, 1.0 ) ), f.x ), f.y );
}
vec3 sgCrop( float h ) {
    if ( h < 0.1429 ) return vec3( 0.847, 0.745, 0.424 );
    if ( h < 0.2857 ) return vec3( 0.788, 0.690, 0.376 );
    if ( h < 0.4286 ) return vec3( 0.722, 0.769, 0.416 );
    if ( h < 0.5714 ) return vec3( 0.620, 0.706, 0.369 );
    if ( h < 0.7143 ) return vec3( 0.784, 0.643, 0.392 );
    if ( h < 0.8571 ) return vec3( 0.659, 0.529, 0.353 );
    return vec3( 0.816, 0.769, 0.518 );
}
`;
  const BOARD_FS_PAINT = `vec3 sgC = vColor.rgb;
  {
    vec2 tp = ( vec2( vSgW.x, - vSgW.z ) - sgBoard.xy ) * 0.5 - 0.5;
    float n2 = sgN2( tp * 1.6 + 11.0 ), n3 = sgN2( tp * 2.7 + 5.0 );
    if ( vSgFR.x > 0.01 ) {
      vec2 fp = tp / vec2( 1.35, 0.95 );
      float row = floor( fp.y + 0.25 * sgN2( vec2( tp.x * 0.4, 7.3 ) ) );
      fp.x += sgH21( vec2( row, 5.0 ) ) * 0.9 + 0.35 * sgN2( vec2( tp.y * 0.4, 3.1 ) );
      vec2 cell = vec2( floor( fp.x ), row );
      vec3 crop = sgCrop( sgH21( cell ) ) * ( 0.95 + 0.1 * n3 );
      // 棋盘内的淡田块边缘柔和（直边的田块会和格线混淆），棋盘外边缘清楚
      vec2 fe = min( fract( vec2( fp.x, fp.y + 0.25 * sgN2( vec2( tp.x * 0.4, 7.3 ) ) ) ), 1.0 - fract( vec2( fp.x, fp.y + 0.25 * sgN2( vec2( tp.x * 0.4, 7.3 ) ) ) ) );
      vec2 bIn = step( vec2( -0.5 ), tp ) * step( tp, sgBoard.zw - 0.5 );
      float soft = mix( 1.0, smoothstep( 0.0, 0.3, min( fe.x, fe.y ) ), bIn.x * bIn.y );
      // 第三轮修订：坡上不画田块（朝镜头的坡把一行行田压扁，只剩一道道竖边，像条形码）；棋盘内的田块也淡一些
      float sgFlat = smoothstep( 0.82, 0.95, dot( normalize( vNormalV ), normalize( ( viewMatrix * vec4( 0.0, 1.0, 0.0, 0.0 ) ).xyz ) ) );
      sgC = mix( sgC, crop, clamp( vSgFR.x * 0.95 * soft * sgFlat * mix( 1.0, 0.75, bIn.x * bIn.y ), 0.0, 1.0 ) );
    }
    if ( sgRoadMap.w > 0.5 && vSgFR.y > 0.01 ) {
      float rd = texture2D( sgRoadTex, ( ( tp - sgRoadMap.xy ) * sgRoadMap.z + 0.5 ) * sgRoadInv ).r * 0.5;
      // 距离场每 0.5 格（0.25 地图单位）一个取样：双线性插值在两取样之间会把路心「抬高」最多约 0.125，阈值放在其上，路才连续
      float rk = ( 1.0 - smoothstep( 0.1, 0.14, rd + ( n3 - 0.5 ) * 0.03 ) ) * vSgFR.y;
      vec3 d = mix( vec3( 0.749, 0.588, 0.365 ), vec3( 0.796, 0.647, 0.435 ), smoothstep( 0.45, 0.85, n2 ) * 0.6 );
      d = mix( d, vec3( 0.635, 0.478, 0.298 ), smoothstep( 0.62, 0.92, n3 ) * 0.4 );
      sgC = mix( sgC, d, rk );
    }
    float bank = smoothstep( 0.46, 0.2, vSgW.y + ( n2 - 0.5 ) * 0.06 ) * step( 0.1, vSgW.y );
    sgC = mix( sgC, vec3( 0.847, 0.784, 0.604 ) * ( 0.95 + 0.08 * n2 ), bank * 0.85 );
  }
  vec3 albedo = lpLin( sgC ) * uTint;`;
  const BOARD_FS_NORMAL = `vec3 n = normalize( vNormalV );
  // 坡地：屏幕导数求出的面法线（低多边形的「手切」坡面），按坡度从平滑法线过渡过去；逐面的亮度抖动按世界面法线取哈希
  vec3 sgUpV = normalize( ( viewMatrix * vec4( 0.0, 1.0, 0.0, 0.0 ) ).xyz );
  vec3 sgFN = normalize( cross( dFdx( vViewPosition ), dFdy( vViewPosition ) ) );
  if ( dot( sgFN, n ) < 0.0 ) sgFN = - sgFN;
  float sgFK = smoothstep( sgFacet.x, sgFacet.y, 1.0 - clamp( dot( n, sgUpV ), 0.0, 1.0 ) );
  // 缓坡的光照放平一些（起伏的田野不出现大片暗斑），陡坡保留手切的明暗
  n = normalize( mix( mix( n, sgUpV, sgFacet.w * ( 1.0 - sgFK ) ), sgFN, sgFK ) );
  vec3 sgFW = ( vec4( sgFN, 0.0 ) * viewMatrix ).xyz;
  float sgFJ = fract( sin( dot( floor( sgFW * 23.0 + 0.5 ), vec3( 12.9898, 78.233, 37.719 ) ) ) * 43758.5453 ) - 0.5;`;
  const BOARD_FS_ALBEDO = `  albedo *= 1.0 + sgFJ * sgFacet.z * sgFK;
  {
    // 格线与棋盘边框（格坐标 = (世界 x − 原点 x, −世界 z − 原点 z) / 2）；棋盘外略暗、略褪色
    vec2 bt = ( vec2( vSgW.x, - vSgW.z ) - sgBoard.xy ) * 0.5;
    vec2 dOut = max( max( - bt, bt - sgBoard.zw ), vec2( 0.0 ) );
    float outD = length( dOut );
    float ins = 1.0 - step( 1e-4, outD );
    vec2 g = abs( fract( bt ) - 0.5 );
    vec2 fw = max( fwidth( bt ), vec2( 1e-4 ) );
    // 修订：格线加宽、远处晚一些才淡出（暖色草地上格线须清楚可读：这是走格子的战棋盘）
    vec2 ln2 = 1.0 - smoothstep( vec2( 0.0 ), fw * 1.6 + 0.016, vec2( 0.5 ) - g );
    float ln = max( ln2.x, ln2.y ) * ins;
    float sgFar = 1.0 - smoothstep( 0.6, 1.4, max( fw.x, fw.y ) );
    albedo *= 1.0 - sgGridK * ln * sgFar;
    float o = smoothstep( 0.0, 1.6, outD );
    float lum = dot( albedo, vec3( 0.3, 0.59, 0.11 ) );
    albedo = mix( albedo, mix( albedo, vec3( lum ), 0.22 ) * 0.86, o );
    vec2 e = min( bt, sgBoard.zw - bt );
    float edge = ( 1.0 - smoothstep( 0.0, fw.x * 1.5 + 0.035, abs( min( e.x, e.y ) ) ) ) * step( - 0.05, min( e.x, e.y ) );
    albedo *= 1.0 - 0.42 * edge;
  }
`;
  let _boardSrc;
  function boardSource() {
    if (_boardSrc !== undefined) return _boardSrc;
    _boardSrc = null;
    const LP = SG.LP;
    const base = LP.material({ detail: LP.grassDetail(), planar: BOARD_PLANAR, detailK: BOARD_DETAIL_K });
    let vs = base.vertexShader, fs = base.fragmentShader;
    const rep = (s, a, b) => (s && s.indexOf(a) >= 0 ? s.replace(a, b) : null);
    const sway = 'worldPosition.xyz = lpWindSway( worldPosition.xyz, lpWind * uWindScale );';
    vs = rep(vs, sway, sway + '\n  vSgW = worldPosition.xyz;');
    vs = rep(vs, sway, sway + '\n  vSgFR = vec2( 1.0 - aMat.w, aBt.w );');
    vs = rep(vs, 'void main() {', 'attribute vec4 aMat;\nattribute vec4 aBt;\nvarying vec3 vSgW;\nvarying vec2 vSgFR;\nvoid main() {');
    fs = rep(fs, 'vec3 albedo = lpLin( vColor.rgb ) * uTint;', BOARD_FS_PAINT);
    fs = rep(fs, 'vec3 n = normalize( vNormalV );', BOARD_FS_NORMAL);
    fs = rep(fs, '  // emission code (LP.EMIT)', BOARD_FS_ALBEDO + '  // emission code (LP.EMIT)');
    fs = rep(fs, 'void main() {', BOARD_FS_PARS + 'void main() {');
    if (!vs || !fs) { console.warn('BattleView: LP shader anchors not found; using the classic board material'); return null; }
    // 地形不加边缘光（Lanternvale 的地形 rim = 0）
    const rimLine = fs.match(/float rim = pow\([^;]*;/);
    if (rimLine) fs = fs.replace(rimLine[0], 'float rim = 0.0;');
    _boardSrc = { vs, fs, defines: Object.assign({}, base.defines || {}) };
    return _boardSrc;
  }
  // 道路距离场贴图（G.road：地图单位，编码 0..0.5 → 0..255）；没有道路时为 null
  function roadTextureLP(Tm) {
    const G = Tm && Tm.G;
    if (!G || !Tm.Tr || !G.road) return null;
    const nu = G.nu, nv = G.nv, d = new Uint8Array(nu * nv * 4);
    let any = false;
    for (let o = 0; o < nu * nv; o++) { const r = G.road[o]; if (r < 0.3) any = true; const b = Math.round(Math.min(1, Math.max(0, r / 0.5)) * 255); d[o * 4] = d[o * 4 + 1] = d[o * 4 + 2] = b; d[o * 4 + 3] = 255; }
    if (!any) return null;
    const tex = new THREE.DataTexture(d, nu, nv, THREE.RGBAFormat);
    tex.magFilter = tex.minFilter = THREE.LinearFilter;
    tex.generateMipmaps = false;
    tex.needsUpdate = true;
    return { tex, map: new THREE.Vector4(G.u0, G.v0, 1 / G.step, 1), inv: new THREE.Vector2(1 / nu, 1 / nv) };
  }
  let _noRoadTex = null;
  function boardMaterialLP(W, H, road) {
    const src = boardSource();
    if (!src) return null;
    const LP = SG.LP;
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.lights, THREE.UniformsLib.fog, {
      uRimBoost: { value: 0 }, uFogScale: { value: 1 }, uWindScale: { value: 0 }, uFade: { value: 1 }, uLit: { value: -1 }, uPlanar: { value: BOARD_PLANAR }, uDetailK: { value: BOARD_DETAIL_K },
      sgGridK: { value: 0.36 },
    }]);
    // 向量 / 贴图在合并之后再放（UniformsUtils.merge 会复制贴图对象，造成重复上传）
    u.uTint = { value: new THREE.Vector3(1, 1, 1) };
    u.uFlash = { value: new THREE.Vector4(1, 1, 1, 0) };
    u.uDetail = { value: LP.grassDetail() };
    u.sgBoard = { value: new THREE.Vector4(ORIGIN.x, ORIGIN.z, W, H) };
    // 第三轮修订：面法线晚一点接管、逐面亮度抖动减半（规则网格上 ±8% 的逐面抖动在坡上排成一道道竖条，像条形码）
    u.sgFacet = { value: new THREE.Vector4(0.1, 0.34, 0.08, 0.5) };
    if (!road && !_noRoadTex) { _noRoadTex = new THREE.DataTexture(new Uint8Array([255, 255, 255, 255]), 1, 1, THREE.RGBAFormat); _noRoadTex.needsUpdate = true; }
    u.sgRoadTex = { value: road ? road.tex : _noRoadTex };
    u.sgRoadMap = { value: road ? road.map : new THREE.Vector4(0, 0, 1, 0) };
    u.sgRoadInv = { value: road ? road.inv : new THREE.Vector2(1, 1) };
    for (const k in LP.globals) u[k] = LP.globals[k];
    const m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: src.vs, fragmentShader: src.fs, lights: true, fog: true, vertexColors: true });
    m.defines = Object.assign({}, src.defines);
    m.extensions = { derivatives: true };
    m.name = 'BattleBoardLP';
    m.userData.boardUniforms = { sgBoard: u.sgBoard, sgGridK: u.sgGridK };
    return m;
  }

  // ---- 景物配方 --
  // 树：种类 → [[配方, 目标高（棋盘单位，再乘实例的 s）], …]（同种多个配方按位置哈希轮换）
  const LP_TREE = {
    broad: [['tree_huai', 1.62], ['tree_oak', 1.55], ['tree_elm', 1.72]],
    broadT: [['tree_huai', 1.6], ['tree_oak', 1.55]],
    pine: [['tree_pine', 1.95]], spruce: [['tree_spruce', 2.05]], poplar: [['tree_poplar', 2.25]],
    palm: [['palm_date', 1.95]], palmT: [['palm_coconut', 1.9]], cypress: [['tree_cypress', 1.85]], acacia: [['tree_acacia', 1.42]],
    olive: [['tree_olive', 1.2]], umbrella: [['tree_pine_cloud', 1.55]], willow: [['tree_willow', 1.7]], bamboo: [['bamboo', 1.55]],
    snowpine: [['pine_snow', 1.95]],
  };
  const LP_FENCE = { han: 'fence_wood', korea: 'fence_wood', wa: 'fence_bamboo', roman: 'wall_stone', persia: 'wall_stone', arab: 'wall_stone', africa: 'wall_stone', moor: 'wall_stone',
    tarim: 'wall_stone', kushan: 'wall_stone', india: 'fence_bamboo', seasia: 'fence_bamboo', nanman: 'fence_bamboo', yi: 'fence_bamboo', celt: 'fence_wattle', german: 'fence_wattle',
    steppe: 'fence_wattle', sarmatian: 'fence_wattle' };
  const LP_HUT = { han: ['house_han', 0], tile: ['house_med', 0], flat: ['house_mud', 0], yurt: ['yurt', 0], thatch: ['house_thatch', 1], long: ['house_thatch', 0], round: ['house_thatch', 1], stilt: ['house_thatch', 2] };   // 修订：干栏用两坡顶（圆顶在棋盘上像一只只草垛 / 木桶）
  const _lpH = {};
  // 配方的模型高度 / 宽度（近景几何体，按配方 + 种子缓存）
  function lpSize(name, seed) {
    const k = name + '|' + seed;
    if (_lpH[k]) return _lpH[k];
    const g = SG.LPProps.geometry(name, { seed });
    return (_lpH[k] = { w: Math.max(0.01, g.size[0]), h: Math.max(0.01, g.size[1]), d: Math.max(0.01, g.size[2]) });
  }
  const phash = (x, z, s) => SG.LP.hash01([x, s, z], 29);
  const HIDE_M = new THREE.Matrix4().makeScale(0, 0, 0), _fm = new THREE.Matrix4(), _lodV = new THREE.Vector3(), _fv = new THREE.Vector3();
  const _lodF = new THREE.Frustum(), _lodM = new THREE.Matrix4(), _lodS = new THREE.Sphere();
  // 景物墨线宽度（1080p 像素；LP 缺省 2.2）：战场镜头远，物件小，墨线细一些（近景 / 远景模型）
  const INK_W = 1.5, INK_FAR_W = 1.15;
  // ---- 树的「挖洞」（第三轮修订；Lanternvale 的 cut-outs，ThreeD.md §2 / LanternvaleCommon.cginc LV_CutCoverage）--
  // 被树挡住的部队周围开一个柔和的圆洞：屏幕上是圆，半径一半以内全空，往外抖动渐实；树干等明显在部队身后的面保留
  // （墨线不论深浅一起挖，免得掏空的树冠里透出整片墨色）。树不再整棵抖动溶到 35%（也不再换成单独的副本、不丢影子），
  // 体量与墨线都在。所有树的实例化网格用 LP 材质派生的「挖洞」材质（同一套光照与全局量），洞的列表全场共用
  // （最多 CUT_N 个，按挡得最多的部队排）；派生失败时退回原来的整棵溶解。
  const CUT_N = 8;
  const CUT_GLSL = `uniform vec4 uSgCut[ ${CUT_N} ];
float sgCutCov( vec3 p, bool keepBehind ) {
  float cov = 1.0;
  for ( int i = 0; i < ${CUT_N}; i ++ ) {
    vec4 c = uSgCut[ i ];
    if ( c.w <= 0.0 ) continue;
    float dc = max( length( c.xyz ), 1e-4 );
    vec3 dir = c.xyz / dc;
    float along = dot( p, dir );
    if ( along <= 1e-4 ) continue;
    if ( keepBehind && along > dc + 0.35 * c.w ) continue;
    float r = length( p - dir * along ) / along * dc / c.w;
    cov = min( cov, smoothstep( 0.55, 0.85, r ) );
  }
  return cov;
}
`;
  // 洞（视空间：xyz 洞心，w 半径；w = 0 无洞）——所有挖洞材质共用这一个 uniform 对象
  const CUT_U = { value: [] };
  for (let i = 0; i < CUT_N; i++) CUT_U.value.push(new THREE.Vector4(0, 0, 0, 0));
  // 世界坐标的洞（当前棋盘每帧写入）：[{ x, y, z, w }]
  const CUT_STATE = { list: [], gen: 0, seen: -1, cam: new THREE.Matrix4() };
  const _cutMats = new Map(), _cutMatList = [];
  function cutMaterial(src) {
    if (!src || !src.isShaderMaterial) return null;
    if (_cutMats.has(src)) return _cutMats.get(src);
    let m = null;
    try {
      const ink = src.userData && src.userData.lpKind === 'outline';
      let vs = src.vertexShader, fs = src.fragmentShader;
      if (ink) {
        vs = vs.replace('varying float vDist;', 'varying float vDist;\nvarying vec3 vSgV;').replace('vDist = length( mvPosition.xyz );', 'vDist = length( mvPosition.xyz );\n  vSgV = mvPosition.xyz;');
        fs = fs.replace('varying float vDist;', 'varying float vDist;\nvarying vec3 vSgV;');
      }
      const at = fs.indexOf('void main() {');
      if (at >= 0 && (!ink || (vs.indexOf('vSgV = mvPosition.xyz') >= 0 && fs.indexOf('varying vec3 vSgV') >= 0)) && (ink || fs.indexOf('varying vec3 vViewPosition') >= 0)) {
        fs = fs.slice(0, at) + CUT_GLSL + 'void main() {\n  { float sgCv = sgCutCov( ' + (ink ? 'vSgV, false' : '- vViewPosition, true') + ' ); if ( sgCv < 0.999 && sgCv <= lpBayer( gl_FragCoord.xy ) ) discard; }' + fs.slice(at + 13);
        // 与源材质共用同一批 uniform 对象（灯光、雾、LP 全局量、外观的缺省值），另加共用的洞
        const u = Object.assign({}, src.uniforms, { uSgCut: CUT_U });
        m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: vs, fragmentShader: fs, lights: src.lights, fog: src.fog, vertexColors: src.vertexColors, side: src.side });
        m.defines = Object.assign({}, src.defines || {});
        m.name = (src.name || 'LP') + ' cut';
        _cutMatList.push(m);
      }
    } catch (e) { console.warn('BattleView: cut-out material unavailable', e); m = null; }
    _cutMats.set(src, m);
    return m;
  }
  const _cutV = new THREE.Vector3();
  // 画树之前（每帧第一棵、或相机变了时）：把世界坐标的洞换到当前相机的视空间
  function cutBefore(r, s, cam) {
    const S = CUT_STATE;
    if (S.seen === S.gen && S.cam.equals(cam.matrixWorldInverse)) return;
    S.seen = S.gen; S.cam.copy(cam.matrixWorldInverse);
    const arr = CUT_U.value;
    for (let i = 0; i < CUT_N; i++) {
      const c = S.list[i];
      if (c && c.w > 0.01) { _cutV.set(c.x, c.y, c.z).applyMatrix4(cam.matrixWorldInverse); arr[i].set(_cutV.x, _cutV.y, _cutV.z, c.w); } else arr[i].set(0, 0, 0, 0);
    }
    for (const m of _cutMatList) m.uniformsNeedUpdate = true;
  }
  // 树的实例化网格（及其墨线）换成挖洞材质；不可用时返回 false（退回整棵溶解）
  function useCut(im) {
    const lit = cutMaterial(im.material), ol = im.userData.outline, ink = ol ? cutMaterial(ol.material) : null;
    if (!lit || (ol && !ink)) return false;
    im.material = lit; im.onBeforeRender = cutBefore;
    if (ol) { ol.material = ink; ol.onBeforeRender = cutBefore; }
    return true;
  }
  // 部队周围洞的半径（世界单位，洞心在部队半身高处）：一半半径以内全空，盖得住主将、旗与身边的兵
  const CUT_R = 1.8, CUT_Y = 0.85;
  // 一个景物条目（_buildProps 的 lists）→ LP 配方与变换：{ name, seed, lod, opts, sc: 数 | [x, y, z], tint, ink, cast, fade, dy }
  function lpPropSpec(kind, e, ctx) {
    const near = e.near || e.hi, h0 = phash(e.x, e.z, 3);
    const far = !(e.near || e.hi) || /Lo$/.test(kind);
    let base = kind.replace(/Lo$/, '');
    const T = (list, s) => {
      const [name, tH] = list[Math.floor(h0 * list.length) % list.length];
      // 棋盘外远处的树只用一个变体；修订：近景树每块（ctx.chunk）一个变体，相邻块交替（块多了也不多出绘制）
      const seed = far ? 0 : ctx.chunkSeed ? ctx.chunkSeed(e) : Math.floor(phash(e.x, e.z, 7) * 2);
      const sz = lpSize(name, seed), k = tH * s / sz.h;
      return { name, seed, lod: far ? 'far' : 'near', sc: [k * e.ax, k * e.ay, k * e.ax], tint: e.c, ink: !!near, cast: !!near, fade: !!e.near, tree: true };
    };
    switch (base) {
      case 'broad': {
        const bi = e.bi | 0;
        if (ctx.wet && ctx.wet(e) && (bi === 0 || bi === 5) && h0 < 0.35) return T(LP_TREE.willow, e.s);
        if ((bi === 3 || ctx.south) && h0 > 0.78) return T(LP_TREE.bamboo, e.s);
        return T(bi === 3 ? LP_TREE.broadT : LP_TREE.broad, e.s);
      }
      case 'pine': return T((e.bi === 6 || e.bi === 7) && e.y > BASE_Y + 1.6 ? LP_TREE.snowpine : LP_TREE.pine, e.s);
      case 'spruce': case 'poplar': case 'cypress': case 'acacia': case 'olive': case 'umbrella': return T(LP_TREE[base], e.s);
      case 'palm': return T(e.bi === 3 || ctx.south ? LP_TREE.palmT : LP_TREE.palm, e.s);
      case 'rock': {
        // 修订：每种石头一个变体（随机朝向、高矮已够变化），只有巨石投影（第三轮：石头原来分成九组、二十多次绘制）
        const name = e.s < 0.17 ? 'rock_small' : e.s < 0.3 ? 'rock_large' : 'boulder', seed = 0;
        const sz = lpSize(name, seed), k = 1.9 * e.s / sz.w;
        return { name, seed, lod: 'near', sc: [k, k * (0.85 + phash(e.x, e.z, 11) * 0.3), k], tint: e.c, ink: true, cast: name === 'boulder', dy: -0.05 * e.s };
      }
      case 'bush': { const v = h0 < 0.5 ? 'pilot' : h0 < 0.8 ? 'round' : 'flowering'; const k = 0.62 * e.s / lpSize('bush', 0).w; return { name: 'bush', seed: 0, opts: { variant: v }, lod: 'near', sc: k, tint: e.c, ink: true, cast: false }; }
      case 'scrub': { const k = 0.5 * e.s / lpSize('bush', 0).w; return { name: 'bush', seed: 1, opts: { variant: 'round' }, lod: 'near', sc: [k, k * 0.6, k], tint: [e.c[0] * 1.05, e.c[1] * 0.98, e.c[2] * 0.72], ink: true, cast: false }; }
      case 'hay': { const k = 0.4 * e.s / lpSize('haystack', 0).h; return { name: 'haystack', seed: 0, lod: 'near', sc: k, ink: true, cast: true }; }
      // 修订：墨线细一些（栅栏 / 石垣是点缀，不要比树还醒目）
      case 'fence': { const name = ctx.fence; const k = 0.84 * e.s / lpSize(name, 0).w; return { name, seed: 0, lod: 'near', sc: k, ink: true, inkW: 0.9, cast: false }; }
      case 'cairn': { const k = 0.34 / lpSize('rock_small', 1).w; return { name: 'rock_small', seed: 1, lod: 'near', sc: [k, k * 1.4, k], tint: [1.08, 1.06, 1.02], ink: true, cast: false }; }
      case 'well': return { name: 'well', seed: 0, lod: 'near', sc: 0.5 / lpSize('well', 0).w, ink: true, cast: true };
      case 'cart': return { name: 'cart', seed: 0, opts: { color: '#B89A66' }, lod: 'near', sc: 1.0 / lpSize('cart', 0).d, ink: true, cast: true };
      default:
        if (/^hut_/.test(base)) { const [name, seed] = LP_HUT[base.slice(4)] || LP_HUT.han, k = 0.95 * e.s / lpSize(name, seed).w; return { name, seed, lod: 'near', sc: k, ink: true, cast: true }; }
        return null;
    }
  }

  // ---- 草丛 / 野花 / 卵石 / 落叶（合成一个不描墨线、随风摆的网格；与试点的 grassField 相同的做法）--
  const GRASS_HEX = [['#8DB45A', '#A6C46A', '#7AA44E', '#B4C878'], ['#B8B46A', '#C8C078', '#A6A25C', '#D0C888'], ['#C8B47A', '#D4C088', '#B8A46C', '#DCCB94'],
    ['#7EAE50', '#96C060', '#6A9A44', '#A8C870'], ['#C2B468', '#D0C276', '#B0A45C', '#D8CC84'], ['#A8B266', '#B8C072', '#96A05A', '#C4C880'], ['#86A85E', '#98B66A', '#749654', '#A8C078'], ['#90AE66', '#A2BC72', '#80A05A', '#B0C67E']];
  const FLOWER_HEX = ['#F5F0E6', '#F2B8C6', '#F4D35E', '#C9A8FF', '#F08C6C'];
  function grassMeshLP(items, seed) {
    const LP = SG.LP, P = LP.Paint;
    const M = new LP.MeshBuilder(seed || 811);
    M.jitter = 0.08;
    M.windGradient = true; M.wind = 0.5;
    const flowers = FLOWER_HEX.map(hx);
    for (const it of items) {
      const k = it.k;
      if (it.t === 'tuft') {
        const greens = (GRASS_HEX[it.bi | 0] || GRASS_HEX[0]).map(hx);
        M.windY0 = it.y; M.windY1 = it.y + 0.35 * k;
        M.tuft([it.x, it.y, it.z], (0.2 + M.random01() * 0.18) * k, 4 + ((M.random01() * 3) | 0), P.shade(greens[(M.random01() * 4) | 0], it.dim || 1));
      } else if (it.t === 'flowers') {
        M.windY0 = it.y; M.windY1 = it.y + 0.3;
        const fc = it.c || flowers[(M.random01() * flowers.length) | 0];
        for (let i = 0; i < 4; i++) {
          const a = M.random01() * Math.PI * 2, d = Math.sqrt(M.random01()) * 0.22 * k, x = it.x + Math.cos(a) * d, z = it.z + Math.sin(a) * d, fy = (0.12 + M.random01() * 0.12) * k;
          M.color = hx('#5E8E42'); M.segment([x, it.y, z], [x, it.y + fy, z], 0.008, 0.006, 3, false, false);
          M.color = fc; M.emission = 0.06;
          M.push().translate(x, it.y + fy, z).rotate(-20 + M.random01() * 40, M.random01() * 360, 0);
          M.disc([0, 0, 0], 0.05 * k, 5); M.disc([0, -0.003, 0], 0.05 * k, 5, true);
          M.color = hx('#F4D35E'); M.sphere([0, 0.008, 0], 0.016 * k, 4, 2, false);
          M.pop(); M.emission = 0;
        }
      } else if (it.t === 'speck') {
        M.wind = 0;
        M.color = it.c || flowers[(M.random01() * flowers.length) | 0]; M.emission = 0.05;
        M.disc([it.x, it.y + 0.02, it.z], (0.025 + M.random01() * 0.018) * k, 5);
        M.emission = 0; M.wind = 0.5;
      } else if (it.t === 'pebble') {
        M.wind = 0;
        const greys = [hx('#9C948A'), hx('#B3AB9E'), hx('#7F786F')], r = (0.04 + M.random01() * 0.04) * k;
        M.color = P.shade(greys[(M.random01() * 3) | 0], 0.92 + M.random01() * 0.14);
        M.push().translate(it.x, it.y, it.z).rotate(0, M.random01() * 360, 0).scale(1, 1, 0.7 + M.random01() * 0.3);
        M.lathe([[r, 0.004], [r * 0.75, r * 0.45], [0, r * 0.62]], 5, false, false, false);
        M.pop(); M.wind = 0.5;
      } else if (it.t === 'leaf') {
        M.wind = 0;
        const leaves = [hx('#9A6A3A'), hx('#B48A48'), hx('#7E5A36'), hx('#A87F3E')];
        M.color = P.shade(leaves[(M.random01() * 4) | 0], 0.92 + M.random01() * 0.16);
        M.push().translate(it.x, it.y + 0.018, it.z).rotate(0, M.random01() * 360, 0);
        M.disc([0, 0, 0], (0.035 + M.random01() * 0.02) * k, 4);
        M.pop(); M.wind = 0.5;
      }
    }
    if (M.isEmpty) return null;
    const mesh = LP.mesh(M.toGeometry(), { outline: false, castShadow: false });
    mesh.name = 'LP grass';
    return mesh;
  }

  // ---- 城池（LP）：按米建模（城心为原点，+Z 朝镜头），整体缩放 FORT_FS 到棋盘单位 --
  const FORT_FS = 0.35;          // 1 米 = 0.35 单位：城墙走道 4.14 米 = 1.45 单位（部队站立的墙顶）
  // 城上小亭与本阵的尺寸（米）：over 檐口出挑、ch 柱高、gw 门楼亭比墩台宽多少、cw 角楼亭宽 / 角楼半宽、keepZ 本阵中心在城心北多少（单位）、keepS 本阵缩放
  const FORT_TOPS = { over: 0.3, ch: 1.3, gw: -0.4, cw: 1.35, keepZ: 1.05, keepS: 0.85, nearCorner: true };
  function fortStyleLP(style) {
    if (style === 'roman') return { fam: 'roman', wall: 'stone', merlon: hx('#C8BEA8') };
    if (style === 'persia' || style === 'arab' || style === 'tarim' || style === 'kushan') return { fam: 'mud', wall: style === 'kushan' ? 'brick' : 'mud', merlon: hx(style === 'kushan' ? '#B07A5E' : '#C8A47A') };
    if (style === 'korea') return { fam: 'korea', wall: 'stone', merlon: hx('#A8A49A') };
    if (style === 'wa' || style === 'stilt' || style === 'celt' || style === 'german' || style === 'steppe') return { fam: 'timber', wall: 'rammed', merlon: null };
    return { fam: 'han', wall: 'rammed', merlon: PAL_LP().rammed };
  }
  const PAL_LP = () => SG.LP.PAL;
  // 方形的夯土 / 石砌墩台（四面都有层理）：中心 (x, z)，边长 s，高 h
  function lpBlock(M, kind, x, z, s, h, merl, rot) {
    const parts = SG.LPProps.parts;
    M.push().translate(x, 0, z).rotate(0, rot || 0, 0);
    parts.wall(M, { len: s, h, thick: s, kind, batter: 0.04, faces: 'fk' });
    M.push().rotate(0, 90, 0);
    parts.wall(M, { len: s - 0.02, h: h - 0.01, thick: s - 0.02, kind, batter: 0.04, faces: 'fk' });
    M.pop();
    if (merl) {
      const n = Math.max(2, Math.round(s / 1.15)), xs = [];
      for (let i = 0; i < n; i++) xs.push(-s / 2 + (i + 0.5) * s / n);
      for (let k = 0; k < 4; k++) { M.push().rotate(0, k * 90, 0); parts.merlons(M, xs, h, s / 2 - 0.2 - 0.04 * h, [s / n * 0.55, 0.7, 0.4], merl); M.pop(); }
    }
    M.pop();
  }
  // 歇山 / 攒尖的小亭（城楼、角楼）：四柱 + 栏额 + 屋顶，立在 y 高处
  // o：{ over 屋檐出挑（缺省 0.7 米）, ch 柱高（缺省 2.0 米）}
  function lpPavilion(M, x, y, z, w, d, style, pal, lanterns, o) {
    const LP = SG.LP, P = LP.Paint, parts = SG.LPProps.parts, PAL = PAL_LP();
    const hw = w / 2 - 0.25, hd = d / 2 - 0.25, ch = (o && o.ch) || 2.0, over = o && o.over != null ? o.over : 0.7;
    M.color = P.shade(PAL.stone, 0.95); M.box([x, y + 0.08, z], [w, 0.16, d], null, 'tfkrl');
    parts.columns(M, [[x - hw, z - hd], [x + hw, z - hd], [x - hw, z + hd], [x + hw, z + hd]], y + 0.16, y + 0.16 + ch, { r: 0.13 });
    M.color = PAL.hanRedDark; M.box([x, y + ch + 0.05, z + hd], [w - 0.3, 0.22, 0.16], null, 'fbt'); M.box([x, y + ch + 0.05, z - hd], [w - 0.3, 0.22, 0.16], null, 'kbt');
    M.color = P.shade(PAL.hanRed, 0.9); M.box([x, y + 0.75, z], [w - 0.7, 1.15, d - 0.7], null, 'fkrl');
    parts.hanRoof(M, { style, w: w - 0.2, d: d - 0.2, y: y + 0.16 + ch + 0.12, palette: pal || 'slate', detail: 'low', cx: x, cz: z, over });
    if (lanterns) for (const s of [-1, 1]) M.ribLantern([x + s * (hw - 0.1), y + ch - 0.35, z + hd + 0.12], 0.17, 0.4, PAL.vermilion, 'night', PAL.iron, true, 6);
  }
  // 圆塔（罗马石塔 / 西亚土坯塔）：中心 (x, z)，半径 r，高 h；roof：'cone' 瓦锥顶 / 'flat' 垛口平顶 / 'dome'
  function lpRoundTower(M, x, z, r, h, wallC, roof, merl) {
    const LP = SG.LP, P = LP.Paint, PAL = PAL_LP();
    M.push().translate(x, 0, z);
    // 修订：抹泥 / 砖砌的塔身是一整片平静的墙面（不再逐行换色、逐面抖动成马赛克）：两米以上一段，层理画成细的暗环
    const j0 = M.jitter;
    M.jitter = 0.025;
    M.color = P.shade(wallC, 0.86); M.cylinder([0, 0, 0], r * 1.08, r * 1.04, 0.5, 10, false, false, false);
    const rows = Math.max(1, Math.round((h - 0.5) / 2.2));
    M.color = P.shade(wallC, 0.98);
    for (let i = 0; i < rows; i++) M.cylinder([0, 0.5 + (h - 0.5) * i / rows, 0], r, r, (h - 0.5) / rows, 10, false, false, i === rows - 1);
    M.color = P.shade(wallC, 0.9);
    for (let y = 1.25; y < h - 0.6; y += 1.25) M.band([0, 0, 0], r + 0.02, r + 0.02, y - 0.05, y + 0.05, 10);
    M.jitter = j0;
    M.color = P.shade(wallC, 1.05); M.band([0, 0, 0], r + 0.08, r + 0.08, h - 0.12, h + 0.08, 10);
    if (roof === 'cone') { M.color = PAL.terracotta; M.cylinder([0, h + 0.08, 0], r + 0.35, 0, r * 1.25, 10, false, true, false); }
    else if (roof === 'dome') SG.LPProps.parts.dome(M, { c: [0, h + 0.08, 0], r: r * 0.7, drumH: 0.25, color: '#E8E0D0', sides: 10 });
    if (roof !== 'cone' && merl) { for (let i = 0; i < 8; i++) { const a = i / 8 * Math.PI * 2; M.color = P.shade(merl, 0.95 + 0.1 * (i % 2)); M.box([Math.cos(a) * (r - 0.2), h + 0.43, Math.sin(a) * (r - 0.2)], [0.5, 0.7, 0.5], null, 'tfkrl'); } }
    M.pop();
  }
  // 木望楼：四柱、平台、护栏与茅草攒尖顶
  function lpWatchtower(M, x, z, s, h) {
    const LP = SG.LP, P = LP.Paint, PAL = PAL_LP(), hs = s / 2 - 0.2;
    M.color = PAL.timber;
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.segment([x + sx * hs, 0, z + sz * hs], [x + sx * hs * 0.9, h + 1.3, z + sz * hs * 0.9], 0.16, 0.13, 5);
    M.color = PAL.wood; M.box([x, h, z], [s + 0.3, 0.22, s + 0.3], P.shade(PAL.woodLight, 0.95), 'tbfkrl');
    M.color = P.shade(PAL.wood, 0.9);
    for (let k = 0; k < 4; k++) { M.push().translate(x, 0, z).rotate(0, k * 90, 0); M.box([0, h + 0.55, hs + 0.1], [s + 0.2, 0.7, 0.12], null, 'fkt'); M.pop(); }
    SG.LPProps.parts.thatchRoof(M, { w: s, d: s, y: h + 1.3, style: 'hip', over: 0.45, rise: s * 0.75, cx: x, cz: z });
  }
  // 木栅：一排削尖的木桩（沿 X，中心在原点，前方 +Z）
  function lpPalisade(M, len, h) {
    const PAL = PAL_LP(), P = SG.LP.Paint;
    const n = Math.max(3, Math.round(len / 0.42));
    for (let i = 0; i < n; i++) {
      const x = -len / 2 + (i + 0.5) * len / n, hh = h * (0.92 + 0.08 * ((i * 7) % 3));
      M.color = P.shade(i % 2 ? PAL.wood : PAL.timber, 0.95 + 0.1 * ((i * 5) % 3) / 2);
      M.cylinder([x, 0, 0], 0.2, 0.19, hh, 5, false, false, false);
      M.color = P.shade(PAL.woodLight, 0.95); M.cone([x, hh, 0], 0.19, 0.42, 5);
    }
    M.color = PAL.timberDark; M.box([0, h * 0.62, -0.18], [len, 0.16, 0.12], null, 'tfkb');
  }
  /**
   * 城池（LP 版）：城墙、城门（缺口两侧门楼）、角楼、本阵。返回 { geo（描墨线）, plain（地面铺装，不描）, flag: 世界坐标, lamps }
   * 版式与第三版一致（墙沿城池外圈格中心线、墙顶 1.45 单位、门格是缺口），只换画风。
   */
  function buildFortLP(view, Tm, look, defHex) {
    const LP = SG.LP, P = LP.Paint, parts = SG.LPProps.parts, PAL = PAL_LP();
    const m = view.model, ca = Tm.castle, st = look.style, S = fortStyleLP(st);
    const gy = BASE_Y + 0.06;
    const cx = ORIGIN.x + ca.x * T + T / 2, cz = -(ORIGIN.z + ca.y * T + T / 2);
    const FS = FORT_FS, Rm = 2 * T / FS, Hm = 1.45 / FS, w0 = 1.12 / FS, timber = S.fam === 'timber';
    const M = new LP.MeshBuilder(((ca.x * 31 + ca.y) | 0) + 77), Pv = new LP.MeshBuilder(78);
    M.jitter = 0.06; M.aoStrength = 0.22; M.aoHeight = 1.4;
    // 修订：城楼 / 角楼的小亭矮一些、檐口出挑短一些（门楼亭与墩台同宽），本阵缩小并后移——城里的守将不再被屋顶遮住
    // （与第三版同样看得见：tests/battle-view.html?dist=38 的曹仁、夏侯惇，南皮的袁绍、颜良）
    const FT = FORT_TOPS, PAV = { over: FT.over, ch: FT.ch };
    Pv.jitter = 0.05;
    M.push().translate(cx, gy, cz).scale(FS);
    Pv.push().translate(cx, gy + 0.012, cz).scale(FS);
    // 内院铺地（不描墨线）：石板 / 夯土 / 草地
    {
      const ir = Rm - w0 / 2 + 0.2, n = 9, cell = 2 * ir / n;
      const tones = timber ? [hx('#A08A62'), hx('#968058'), hx('#AA9468')] : S.fam === 'mud' ? [hx('#D8C094'), hx('#CEB488'), hx('#E0C89C')] : [hx('#B8B0A0'), hx('#AAA294'), hx('#C4BCAC')];
      for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
        const x0 = -ir + i * cell, z0 = -ir + j * cell, c = P.shade(tones[(i * 3 + j * 5) % 3], 0.94 + 0.1 * SG.LP.hash01([i, j, 5], 3));
        Pv.quad([x0 + 0.03, 0, z0 + 0.03], [x0 + cell - 0.03, 0, z0 + 0.03], [x0 + cell - 0.03, 0, z0 + cell - 0.03], [x0 + 0.03, 0, z0 + cell - 0.03], [0, 1, 0], c);
      }
      Pv.color = P.shade(tones[1], 0.72);
      Pv.quad([-ir, -0.01, -ir], [ir, -0.01, -ir], [ir, -0.01, ir], [-ir, -0.01, ir], [0, 1, 0]);
    }
    const isGate = (x, y) => x >= 0 && y >= 0 && x < m.W && y < m.H && m.map[x][y] === TR().Gate;
    // 四边：out 为三维坐标的外向（+Z 朝镜头），yaw 把局部 +Z 转到外向
    const sides = [
      { ox: 0, oz: 1, yaw: 0, gate: isGate(ca.x, ca.y - 2) },
      { ox: 1, oz: 0, yaw: 90, gate: isGate(ca.x + 2, ca.y) },
      { ox: 0, oz: -1, yaw: 180, gate: isGate(ca.x, ca.y + 2) },
      { ox: -1, oz: 0, yaw: -90, gate: isGate(ca.x - 2, ca.y) },
    ];
    const cr = (timber ? 0.5 : S.fam === 'han' || S.fam === 'korea' ? 0.62 : 0.7) / FS;   // 角楼半宽
    const gap = 0.75 / FS, gr = (timber ? 0.45 : S.fam === 'han' || S.fam === 'korea' ? 0.5 : 0.55) / FS;   // 门洞半宽、门楼半宽
    const run = (x0, x1) => {
      const len = x1 - x0, xm = (x0 + x1) / 2;
      if (len < 0.2) return;
      M.push().translate(xm, 0, 0);
      if (timber) {
        // 土垒（梯形断面的草坡）+ 外沿木栅
        const bh = Hm * 0.45, b0 = w0 / 2 + 0.6, b1 = w0 / 2 - 0.4;
        M.color = hx('#8A7A52');
        M.quad([-len / 2, 0, b0], [len / 2, 0, b0], [len / 2, bh, b1], [-len / 2, bh, b1], [0, 0.6, 1], hx('#8E9A58'));
        M.quad([-len / 2, 0, -b0], [-len / 2, bh, -b1], [len / 2, bh, -b1], [len / 2, 0, -b0], [0, 0.6, -1], hx('#7E8A50'));
        M.quad([-len / 2, bh, -b1], [-len / 2, bh, b1], [len / 2, bh, b1], [len / 2, bh, -b1], [0, 1, 0], hx('#9AA462'));
        M.push().translate(0, bh - 0.1, b1 * 0.55);
        lpPalisade(M, len, Hm * 0.62);
        M.pop();
        // 走道：木板（部队站在墙顶 = Hm）
        M.color = PAL.wood; M.box([0, Hm - 0.12, -0.6], [len, 0.24, 2.0], P.shade(PAL.woodLight, 0.92), 'tfkb');
        for (let i = 0; i < Math.max(2, Math.round(len / 2.4)); i++) { const x = -len / 2 + (i + 0.5) * len / Math.max(2, Math.round(len / 2.4)); M.color = PAL.timber; M.segment([x, bh, -0.6], [x, Hm - 0.2, -0.6], 0.14, 0.12, 5); }
      } else {
        parts.wall(M, { len, h: Hm, thick: w0, kind: S.wall, batter: 0.07 });
        // 外侧垛口、内侧矮女墙、外脚勒脚
        const n = Math.max(2, Math.round(len / 1.25)), xs = [];
        for (let i = 0; i < n; i++) xs.push(-len / 2 + (i + 0.5) * len / n);
        if (S.fam === 'mud') { for (const x of xs) { M.color = P.shade(S.merlon, 0.95 + 0.08 * ((x * 13 | 0) % 2)); M.box([x, Hm + 0.3, w0 / 2 - 0.55], [0.75, 0.6, 0.5], null, 'tfkrl'); M.box([x, Hm + 0.78, w0 / 2 - 0.55], [0.42, 0.36, 0.4], null, 'tfkrl'); } }
        else parts.merlons(M, xs, Hm, w0 / 2 - 0.6, [len / n * 0.55, 0.78, 0.42], S.merlon);
        M.color = P.shade(S.merlon || PAL.stone, 0.9); M.box([0, Hm + 0.17, -w0 / 2 + 0.6], [len, 0.34, 0.3], null, 'tfkb');
        if (S.fam === 'han' || S.fam === 'korea') parts.plinth(M, -len / 2, len / 2, w0 / 2 - 0.05, 0.55);
      }
      M.pop();
    };
    const L = Rm;   // 半边长（墙中心线到城心）
    let flagAt = null;
    // 夜里的灯（世界坐标）：城门外侧门楼下各一盏、本阵门前一盏（SG.Gfx 的灯池，白天不亮）
    const lamps = [];
    const lampAt = (lx, ly, lz, sd) => {
      const r = (sd ? sd.yaw : 0) * Math.PI / 180, x = lx * Math.cos(r) + lz * Math.sin(r) + (sd ? sd.ox * Rm : 0), z = -lx * Math.sin(r) + lz * Math.cos(r) + (sd ? sd.oz * Rm : 0);
      lamps.push([cx + x * FS, gy + ly * FS, cz + z * FS]);
    };
    for (const sd of sides) {
      M.push().translate(sd.ox * Rm, 0, sd.oz * Rm).rotate(0, sd.yaw, 0);
      const a = -L + cr * 0.6, b = L - cr * 0.6;
      if (sd.gate) {
        const gw = gap + gr * 1.8;
        run(a, -gw); run(gw, b);
        // 门楼：缺口两侧的墩台 + 城楼小亭；门扇敞开贴在门洞内侧；缺口上方一道额枋与匾
        for (const s of [-1, 1]) {
          const tx = s * (gap + gr * 0.9), th = (1.45 + 0.45) / FS;
          if (timber) lpWatchtower(M, tx, 0, gr * 2, th - 1.2);
          else if (S.fam === 'han' || S.fam === 'korea') { lpBlock(M, S.wall, tx, 0, gr * 2, th, S.merlon && null, 0); lpPavilion(M, tx, th, 0, gr * 2 + FT.gw, gr * 2 + FT.gw, 'xieshan', S.fam === 'korea' ? 'grey' : 'slate', true, PAV); }
          else lpRoundTower(M, tx, 0, gr * 1.05, th, S.fam === 'roman' ? hx('#C8BEA8') : S.merlon, S.fam === 'roman' ? 'cone' : st === 'arab' ? 'dome' : 'flat', S.merlon);
          // 门扇（敞开、向内）
          M.color = timber ? PAL.timber : hx('#A83E2E');
          M.push().translate(s * gap, 0, -0.4).rotate(0, s * 70, 0);
          M.box([-s * 0.85, 1.55, 0], [1.7, 3.1, 0.14], null, 'tbfkrl');
          M.pop();
        }
        const ly = (1.45 + 0.25) / FS;
        lampAt(0, ly + 0.6, w0 / 2 + 2.2, sd);
        M.color = timber ? PAL.timberDark : PAL.hanRedDark;
        M.box([0, ly, w0 / 2 - 0.4], [2 * gap + 0.6, 0.42, 0.42], null, 'tbfkrl');
        M.color = PAL.lacquer; M.box([0, ly + 0.55, w0 / 2 - 0.25], [2.0, 0.75, 0.12], null, 'tfkrl');
        M.color = P.shade(PAL.stone, 0.9); M.box([0, 0.06, 0], [2 * gap + 0.4, 0.12, w0 * 0.9], null, 'tfkrl');
      } else {
        run(a, b);
        // 汉式墙段中间的马面
        if (S.fam === 'han') { lpBlock(M, S.wall, 0, w0 / 2 + 0.75, 2.6, Hm, null, 0); }
      }
      M.pop();
    }
    // 角楼
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) {
      const x = sx * Rm, z = sz * Rm, th = (1.45 + 0.3) / FS;
      if (timber) lpWatchtower(M, x, z, cr * 2, th - 1.0);
      else if (S.fam === 'han' || S.fam === 'korea') { lpBlock(M, S.wall, x, z, cr * 2, th, S.merlon, 0); if (sz < 0 || FT.nearCorner) lpPavilion(M, x, th, z, cr * FT.cw, cr * FT.cw, 'pyramid', S.fam === 'korea' ? 'grey' : 'slate', false, PAV); }
      else lpRoundTower(M, x, z, cr, th, S.fam === 'roman' ? hx('#C8BEA8') : S.merlon, S.fam === 'roman' ? 'cone' : 'flat', S.merlon);
    }
    // 本阵（城心偏北 0.45 单位，部队站在南侧前方）
    M.push().translate(0, 0, -FT.keepZ / FS).scale(FT.keepS);
    lpKeep(M, st, S);
    M.pop();
    lampAt(0, 2.6 * FT.keepS, -FT.keepZ / FS + 3.2 * FT.keepS, null);
    // 帅旗位置（与第三版同：东北角 (0.85, 0.85) 单位，Unity 北 = 三维 −Z）
    flagAt = { x: cx + 0.85, y: gy, z: cz - 0.85 };
    M.pop(); Pv.pop();
    return { geo: M.isEmpty ? null : M.toGeometry(), plain: Pv.isEmpty ? null : Pv.toGeometry(), flag: flagAt, lamps, tris: M.triangleCount + Pv.triangleCount };
  }
  // 各文化的本阵（米，原点为本阵中心地面，正面朝 +Z）
  function lpKeep(M, st, S) {
    const LP = SG.LP, P = LP.Paint, parts = SG.LPProps.parts, PAL = PAL_LP();
    if (S.fam === 'han' || S.fam === 'korea') {
      const korea = S.fam === 'korea';
      // 三层台基、朱柱殿身、重檐（金顶 / 高句丽灰瓦）
      M.color = PAL.stoneLight; M.box([0, 0.3, 0], [5.6, 0.6, 3.4], P.shade(PAL.stoneLight, 1.04), 'tfkrl');
      M.color = PAL.stone; M.box([0, 0.75, 0.05], [4.9, 0.3, 2.9], P.shade(PAL.stoneLight, 1.0), 'tfkrl');
      M.color = PAL.stone; M.box([0, 0.3, 1.95], [1.5, 0.6, 0.6], null, 'tfrl');
      const y0 = 0.9;
      M.color = korea ? hx('#8E5A44') : PAL.hanRed; M.box([0, y0 + 1.0, -0.15], [3.8, 2.0, 1.8], null, 'fkrl');
      parts.columns(M, [[-1.8, 1.0], [-0.6, 1.0], [0.6, 1.0], [1.8, 1.0]], y0, y0 + 2.0, { r: 0.13 });
      M.color = PAL.lacquer; M.box([0, y0 + 0.8, 0.76], [0.9, 1.6, 0.06], null, 'f');
      parts.window(M, { x: -1.2, y: y0 + 1.2, z: 0.75, w: 0.7, h: 0.7 }); parts.window(M, { x: 1.2, y: y0 + 1.2, z: 0.75, w: 0.7, h: 0.7 });
      const r1 = parts.hanRoof(M, { style: 'hip', w: 4.4, d: 2.6, y: y0 + 2.05, palette: korea ? 'grey' : 'gold', detail: 'mid', over: 0.75, rise: 1.0 });
      M.color = korea ? hx('#8E5A44') : PAL.hanRed; M.box([0, r1.ridgeY - 0.15, 0], [2.6, 1.0, 1.3], null, 'fkrl');
      parts.hanRoof(M, { style: korea ? 'xieshan' : 'hip', w: 3.0, d: 1.6, y: r1.ridgeY + 0.35, palette: korea ? 'grey' : 'gold', detail: 'mid', over: 0.6 });
      return;
    }
    if (S.fam === 'roman') {
      // 列柱神庙：台基、前后两排柱、赤陶瓦三角山墙朝前
      M.color = PAL.stoneLight; M.box([0, 0.35, 0], [5.0, 0.7, 3.4], hx('#E8E2D4'), 'tfkrl');
      const cols = []; for (let i = 0; i < 5; i++) for (const z of [1.25, -1.25]) cols.push([-1.9 + i * 0.95, z]);
      parts.columns(M, cols, 0.7, 3.0, { r: 0.17, color: '#ECE6D8', base: '#D8D0C0', sides: 8 });
      M.color = hx('#E4DCCC'); M.box([0, 1.85, 0], [3.2, 2.3, 1.6], null, 'fkrl');
      M.color = hx('#E8E2D4'); M.box([0, 3.12, 0], [4.9, 0.3, 3.2], null, 'tbfkrl');
      M.push().rotate(0, 90, 0); parts.terracottaRoof(M, { style: 'gable', w: 3.1, d: 4.6, y: 3.27, over: 0.25, rise: 1.0, detail: 'low' }); M.pop();
      return;
    }
    if (S.fam === 'mud') {
      if (st === 'kushan' || st === 'tarim') {
        M.push().scale(0.62); parts.stupa(M, { r: 2.2 }); M.pop();
        return;
      }
      // 平顶宫殿 + 正面伊万 + 穹顶（阿拉伯加尖塔）
      const wc = hx(st === 'arab' ? '#E4D4B0' : '#D8B88A');
      M.color = wc; M.box([0, 1.4, 0.1], [4.6, 2.8, 2.6], null, 'fkrl');
      parts.flatRoof(M, { w: 4.6, d: 2.6, y: 2.8, color: st === 'arab' ? '#E4D4B0' : '#D8B88A', cz: 0.1 });
      M.color = P.shade(wc, 1.05); M.box([0, 2.0, 1.45], [2.4, 4.0, 0.5], null, 'tfrl');
      M.color = hx('#3A2E28'); M.box([0, 1.25, 1.71], [1.3, 2.5, 0.04], null, 'f');
      M.color = hx(st === 'arab' ? '#C9A04A' : '#4E8E96'); M.box([0, 3.6, 1.72], [2.2, 0.3, 0.06], null, 'f');
      parts.dome(M, { c: [0, 3.25, -0.2], r: 1.25, drumH: 0.5, shape: st === 'arab' ? 'hemi' : 'onion', color: st === 'arab' ? '#F0EAE0' : '#5E9C94', drum: '#D8CBB4' });
      return;
    }
    // 木构文化
    if (st === 'steppe') {
      const felt = hx('#EEE6D6');
      M.color = felt; M.cylinder([0, 0, 0], 2.0, 2.0, 1.5, 12, false, false, false);
      M.color = hx('#B8442E'); M.band([0, 0, 0], 2.02, 2.02, 1.0, 1.2, 12);
      M.color = P.shade(felt, 0.95); M.cylinder([0, 1.5, 0], 2.1, 0.5, 1.2, 12, false, true, false);
      M.color = PAL.gold; M.cylinder([0, 2.7, 0], 0.5, 0.15, 0.35, 10, false, true, true);
      M.color = hx('#B8442E'); M.box([0, 0.65, 2.0], [0.8, 1.3, 0.12], null, 'f');
      for (const s of [-1, 1]) { M.color = PAL.timber; M.segment([s * 2.3, 0, 1.4], [s * 2.3, 5.0, 1.4], 0.08, 0.06, 5); M.color = hx('#3A2E28'); M.cylinder([s * 2.3, 3.6, 1.4], 0.12, 0.45, 1.2, 6, false, false, false); }
      return;
    }
    if (st === 'celt') {
      M.color = hx('#A88A62'); M.cylinder([0, 0, 0], 2.0, 2.0, 1.6, 12, false, false, false);
      M.color = hx('#C8A868'); M.cylinder([0, 1.5, 0], 2.6, 0.1, 2.6, 12, false, true, false);
      M.color = PAL.timberDark; M.box([0, 0.75, 1.98], [0.9, 1.5, 0.1], null, 'f');
      return;
    }
    // 倭 / 南海：高床殿；日耳曼：长屋
    if (st === 'german') {
      M.color = PAL.timber; M.box([0, 1.1, 0], [5.4, 2.2, 2.6], null, 'fkrl');
      parts.thatchRoof(M, { w: 5.6, d: 2.6, y: 2.2, style: 'gable', rise: 2.0 });
      M.color = PAL.timberDark; M.box([0, 0.9, 1.32], [1.0, 1.8, 0.06], null, 'f');
      return;
    }
    for (const x of [-2.0, 0, 2.0]) for (const z of [-0.9, 0.9]) { M.color = PAL.timber; M.segment([x, 0, z], [x, 1.8, z], 0.14, 0.12, 5); }
    M.color = PAL.wood; M.box([0, 1.85, 0], [4.8, 0.2, 2.6], P.shade(PAL.woodLight, 0.95), 'tbfkrl');
    M.color = P.shade(PAL.woodLight, 0.92); M.box([0, 2.65, 0], [3.8, 1.4, 1.9], null, 'fkrl');
    parts.thatchRoof(M, { w: 4.0, d: 2.1, y: 3.3, style: 'gable', rise: 2.3, over: 0.55 });
    if (st === 'wa') for (const s of [-1, 1]) { M.color = PAL.timberDark; M.segment([s * 2.5, 5.4, -0.3], [s * 2.9, 6.6, 0.3], 0.07, 0.05, 4); M.segment([s * 2.5, 5.4, 0.3], [s * 2.9, 6.6, -0.3], 0.07, 0.05, 4); }
  }

  // ---- 桥（LP）：木桥（汉、木构文化）/ 石桥（罗马、西亚）；桥面与第三版同高（部队站在桥面上）--
  function bridgeInto(M, b, stone) {
    const LP = SG.LP, P = LP.Paint, PAL = PAL_LP();
    const cx = ORIGIN.x + b.x * T + T / 2, cz = -(ORIGIN.z + b.y * T + T / 2), y = WATER_Y + 0.34;
    const ax = b.axis === 'x', L = T + 0.6, Wd = 1.0;
    M.push().translate(cx, y, cz).rotate(0, ax ? 0 : 90, 0);
    if (stone) {
      M.color = PAL.stoneLight; M.box([0, -0.07, 0], [L, 0.14, Wd], P.shade(PAL.stoneLight, 1.04), 'tbfkrl');
      for (const s of [-1, 1]) { M.color = PAL.stone; M.box([0, 0.09, s * (Wd / 2 - 0.05)], [L, 0.2, 0.1], null, 'tfkrl'); }
      for (const s of [-1, 1]) { M.color = PAL.stoneDark; M.box([s * 0.55, -0.42, 0], [0.3, 0.62, Wd * 0.9], null, 'fkrl'); }
    } else {
      const n = 9;
      for (let i = 0; i < n; i++) { const x = -L / 2 + (i + 0.5) * L / n; M.color = P.shade(i % 2 ? PAL.wood : PAL.woodLight, 0.92 + 0.08 * ((i * 7) % 3) / 2); M.box([x, -0.04, 0], [L / n - 0.02, 0.08, Wd], null, 'tbfkrl'); }
      for (const s of [-1, 1]) {
        M.color = PAL.timber; M.box([0, 0.2, s * (Wd / 2 - 0.04)], [L, 0.06, 0.07], null, 'tbfk');
        for (let i = 0; i < 4; i++) { const x = -L / 2 + 0.15 + i * (L - 0.3) / 3; M.color = PAL.timberDark; M.box([x, 0.08, s * (Wd / 2 - 0.04)], [0.08, 0.26, 0.08], null, 'tfkrl'); }
        M.color = PAL.timberDark; M.segment([s * 0.55, -0.7, -Wd * 0.3], [s * 0.55, -0.05, -Wd * 0.3], 0.07, 0.07, 5); M.segment([s * 0.55, -0.7, Wd * 0.3], [s * 0.55, -0.05, Wd * 0.3], 0.07, 0.07, 5);
      }
    }
    M.pop();
  }

  // ============================================================ UnitVisual --
  // 一支部队：骑马的主将（js/figure.js 中精度模型）+ 若干实例化士兵（BattleView.crowd，低精度、势力色）+ 头顶信息。
  // 动作：待机、行军（沿路径行走 / 小跑）、攻击（突进 + 挥击）、受击、必杀（蓄势）、溃散（倒地）。
  // 没有载入 figure.js 时退回旧版低多边形模型（SG.Models.commander / soldier）。
  const FIG_CULTS = { han: 1, nanman: 1, wa: 1, yi: 1, korea: 1, steppe: 1, seasia: 1, tarim: 1, kushan: 1, persia: 1, arab: 1, roman: 1, celt: 1, german: 1, sarmatian: 1, india: 1, africa: 1, moor: 1 };
  const figCult = c => (FIG_CULTS[c] ? c : SG.cultureBase && FIG_CULTS[SG.cultureBase(c)] ? SG.cultureBase(c) : 'han');
  const MOUNTED_CULTS = { steppe: 1, sarmatian: 1 };
  const _bv = new THREE.Vector3(), _bm = new THREE.Matrix4(), _bi = new THREE.Matrix4();
  class UnitVisual {
    constructor(view, unit, col) {
      this.view = view; this.u = unit; this.col = col;
      this.group = new THREE.Group();
      this.group.name = 'Unit_' + unit.gen.name;
      view.group.add(this.group);
      // 兼容旧接口：受击闪白 / 火计灼烧 / 必杀着色写到 material.flash / flashColor，每帧转给人物模型
      this.material = { flash: 0, flashColor: new THREE.Color(1, 1, 1) };
      this._mesh = null;
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
      this.lbl = { dx: 0, dy: 0, fade: 1, tdx: 0, tdy: 0, tfade: 1, z: -30, shown: false };
      this.info = null; this.follow = null;
      // 第三版：人物模型与动作
      this.fig = null; this.horse = null; this.figRoot = null; this.sold = [];
      this.anim = { t: Math.random() * 4, act: null, actT: 0, moving: 0, gait: 'walk', last: new THREE.Vector3(), routing: false, tintKey: '' };
      this.culture = view.cultureOf(unit.side);
      this.group.position.copy(this.pos);
      this.group.rotation.y = SG.yawToThree(this.yaw);
      this.anim.last.copy(this.pos);
      // 人物模型：BattleView 预载（分片生成面部贴图、士兵动作烘焙）完成后由 figuresReady() 建立；在此之前只显示光环与名牌
      this.figMode = !!(view.crowd && SG.Figure);
      if (!this.figMode) this._useLowPoly();
      this._buildLabel();
      this.refresh();
    }
    _useLowPoly() {
      this.figMode = false;
      this._mesh = SG.Gfx.mesh(new THREE.BufferGeometry(), this._lp = SG.Gfx.newLowPoly(), { castShadow: true, receiveShadow: true });
      this.group.add(this._mesh);
      this.material = this._lp;
      this.bucket = -1;
    }
    figuresReady() {
      if (!this.figMode || this.fig) return;
      try { this._buildFigures(); }
      catch (e) {
        console.error(e);
        if (this.figRoot && this.figRoot.parent) this.figRoot.parent.remove(this.figRoot);
        this.fig = this.horse = this.figRoot = null;
        this._useLowPoly();
      }
      this.bucket = -1;
      this.refresh();
    }

    get active() { return this.group.visible; }
    // 兼容 C# 写法 V.Vis(u).gameObject.activeSelf
    get gameObject() { const self = this; return { get activeSelf() { return self.group.visible; } }; }
    get transform() { return this.group; }
    // 残影等特效用的网格：旧版为部队的合并网格；第三版为主将与战马当前姿势的烘焙几何体（部队局部坐标）
    get mesh() { return this._mesh || { geometry: this.ghostGeometry() }; }

    _buildFigures() {
      const u = this.u, hex = cssHex(this.col);
      const g = this.fig = SG.Figure.general(u.gen, { color: hex, lod: 'mid', mounted: true });
      const h = this.horse = SG.Figure.horse({ color: hex, lod: 'mid', seed: (u.gen.id | 0) % 5, culture: this.culture, general: true,
        armor: this.culture === 'persia' || this.culture === 'sarmatian' ? 'scale' : 'cloth' });
      h.mount(g);
      const root = this.figRoot = new THREE.Group();
      root.name = 'Figures';
      root.add(h.root);
      h.root.rotation.y = Math.PI;              // 模型面朝 +Z，部队前方为局部 −Z
      h.root.position.set(0, 0, -0.36);
      root.scale.setScalar(FIG_S);
      h.root.position.multiplyScalar(1 / FIG_S);
      this.group.add(root);
      // 墨线外壳（名为 outline 或 LP 墨线材质）不投影、不接收影子（figure.js 的请求：外壳投影会把影子撑胖一圈）
      root.traverse(o => { if (o.isMesh && o.name !== 'outline' && !(o.material && o.material.userData && o.material.userData.lpKind === 'outline')) { o.castShadow = true; o.receiveShadow = true; } });
    }
    // 士兵的兵种与队形（部队局部坐标，前方为 −Z）
    _soldierSlots(n) {
      const mounted = !!MOUNTED_CULTS[this.culture];
      const S = [];
      if (mounted) {
        const P = [[-0.5, 0.45], [0.5, 0.45], [-0.5, 1.05], [0.5, 1.05]];
        for (let i = 0; i < n && i < 4; i++) S.push({ x: P[i][0], z: P[i][1], type: 'cavalry', mounted: true });
        return S;
      }
      const P = [[-0.52, 0.22], [0.52, 0.22], [0, 0.4], [-0.6, 0.72], [0.6, 0.72], [0, 0.86]];
      const T6 = ['spear', 'spear', 'sword', 'archer', 'archer', 'sword'];
      for (let i = 0; i < n && i < 6; i++) S.push({ x: P[i][0] + ((i * 37) % 7 - 3) * 0.012, z: P[i][1], type: T6[i], mounted: false });
      return S;
    }
    _syncSoldiers(n) {
      const cr = this.view.crowd;
      if (!cr) return;
      const want = this._soldierSlots(n);
      while (this.sold.length > want.length) { const s = this.sold.pop(); cr.remove(s.id); }
      const hex = cssHex(this.col), cul = figCult(this.culture);
      for (let i = this.sold.length; i < want.length; i++) {
        const w = want[i];
        const id = cr.add({ type: w.type, culture: cul, color: hex, seed: ((this.u.gen.id | 0) + i) % 2, mounted: w.mounted });
        this.sold.push({ id, x: w.x, z: w.z, type: w.type, mounted: w.mounted, clip: '', jitter: (i * 0.37) % 1 });
      }
      this._placeSoldiers(true);
    }
    _placeSoldiers(force) {
      const cr = this.view.crowd;
      if (!cr || !this.sold.length) return;
      const gp = this.group.position, ry = this.group.rotation.y, c = Math.cos(ry), s = Math.sin(ry);
      const key = gp.x.toFixed(3) + ',' + gp.y.toFixed(3) + ',' + gp.z.toFixed(3) + ',' + ry.toFixed(3);
      if (!force && key === this._placeKey) return;
      this._placeKey = key;
      const lift = gp.y - this.view.groundY(gp.x, gp.z);
      for (const q of this.sold) {
        const wx = gp.x + q.x * c + q.z * s, wz = gp.z - q.x * s + q.z * c;
        const wy = this.view.groundY(wx, wz) + Math.max(0, lift);
        cr.setTransform(q.id, wx, wy, wz, ry + Math.PI, FIG_S);
        q.wx = wx; q.wy = wy; q.wz = wz;     // 第四版：脚下圆影的位置
      }
    }
    // 动作：clip ∈ attack | hit | special | rout；dur 秒
    act(kind, dur) {
      const A = this.anim;
      if (A.routing && kind !== 'rout') return;
      if (kind === 'hit' && A.act && (A.act.kind === 'attack' || A.act.kind === 'special') && A.actT < A.act.dur * 0.7) return;
      const style = this.fig ? this.fig.style : 'pole';
      const heavy = Math.random() < 0.4;
      const D = {
        attack: { dur: 0.75, rider: heavy ? 'heavy' : (Math.random() < 0.5 ? 'light1' : 'light2'), horse: 'gallop', sold: q => q.mounted ? 'attack' : q.type === 'archer' ? 'bowShoot' : (Math.random() < 0.5 ? 'light1' : 'heavy') },
        hit: { dur: 0.5, rider: Math.random() < 0.5 ? 'hit' : 'hitB', horse: 'idle', sold: q => q.mounted ? 'hit' : (Math.random() < 0.6 ? 'hit' : 'block') },
        special: { dur: 1.2, rider: 'special', horse: 'rear', sold: q => q.mounted ? 'charge' : 'cheer' },
        rout: { dur: 1.1, rider: 'hit', horse: 'rear', sold: q => q.mounted ? 'die' : (Math.random() < 0.5 ? 'die' : 'knockdown') },
      }[kind];
      if (!D) return;
      if (style === 'bow' && kind === 'attack') D.rider = 'bowShoot';
      A.act = { kind, dur: dur || D.dur, rider: D.rider, horse: D.horse };
      A.actT = 0;
      if (kind === 'rout') A.routing = true;
      const cr = this.view.crowd;
      if (cr) for (const q of this.sold) {
        if (kind === 'hit' && Math.random() < 0.35) continue;
        const clip = D.sold(q);
        q.clip = clip;
        cr.play(q.id, clip, { t0: cr.time - q.jitter * 0.12 });
      }
    }
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
      this.labelPos.y += 2.3;
    }

    refresh() {
      const u = this.u;
      if (this.figMode) {
        const b = !u.alive && !this.anim.routing ? 0 : M.clamp(Math.ceil(u.troops / 900), u.alive ? 1 : 0, 6);
        if (this.fig && b !== this.bucket && !this.anim.routing && this.group.visible) { this.bucket = b; this._syncSoldiers(MOUNTED_CULTS[this.culture] ? Math.ceil(b * 4 / 6) : b); }
      } else {
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
          const old = this._mesh.geometry;
          this._mesh.geometry = mb.toGeometry();
          if (old) old.dispose();
        }
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
        this.confuseRing.position.y = 1.75;
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
      if (dir.x * dir.x + dir.z * dir.z > 0.001) this.facing = Math.atan2(dir.x, -dir.z) * M.rad2deg;
    }
    rest() { this.anim.moving = 0; }
    flashHit() { this.flash = 1; this.act('hit'); }
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
      const u = this.u, A = this.anim;
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
      const mat = this.material;
      const fc = mat.flashColor;
      if (fc && fc.isColor) {
        if (burn > 0) fc.copy(HEAT_TINT).lerp(WHITE, this.flash / (this.flash + burn));
        else if (fc.r !== 1 || fc.g !== 1 || fc.b !== 1) fc.copy(WHITE);
      }
      mat.flash = Math.max(this.flash, burn);
      this._spUpdate(dt, time);   // 必杀技：加成光环、着色光（可能盖过 flash）
      // 位置：贴地（动画只写 pos；跳跃等抬高的部分保留）
      const gy = this.view.groundY(this.pos.x, this.pos.z);
      this.group.position.set(this.pos.x + shake, Math.max(this.pos.y, gy), this.pos.z);
      if (this.confuseRing) this.confuseRing.rotation.y -= 200 * M.deg2rad * dt;
      // 行进速度 → 步态
      const sp = dt > 0 ? Math.hypot(this.pos.x - A.last.x, this.pos.z - A.last.z) / dt : 0;
      A.last.copy(this.pos);
      if (sp > 0.6) A.moving = 0.15; else if (A.moving > 0) A.moving = Math.max(0, A.moving - dt);
      A.t += dt;
      // 阵营光环抬到格子周围的地表之上（坡地上不被地面遮住）
      const tk = u.x + ',' + u.y;
      if (tk !== this._ringKey && A.moving <= 0) { this._ringKey = tk; this.ring.position.y = 0.04 + Math.min(0.45, Math.max(0, this.view._ringY(u.x, u.y, 0.9))); }
      if (this.fig) this._animate(dt, sp, mat);
      this.labelAlpha = ((u.alive || this.holdLabel) && this.group.visible) ? (u.acted && u.side === this.view.model.side ? 0.55 : 1) : 0;
      if (this.follow) {
        this._updateLabelPos();
        this.follow.alpha = this.labelAlpha * this.lbl.fade;   // BattleView._layoutLabels 随后按避让结果再设
      }
    }
    _animate(dt, sp, mat) {
      const A = this.anim, cr = this.view.crowd;
      if (!this.group.visible) { if (this.sold.length) { for (const q of this.sold) cr.remove(q.id); this.sold.length = 0; } return; }
      const moving = A.moving > 0 && !A.routing;
      const gait = sp > 7 ? 'gallop' : sp > 3.2 ? 'trot' : 'walk';
      if (moving) A.gait = gait;
      let act = A.act;
      if (act) { A.actT += dt; if (A.actT >= act.dur && !A.routing) { A.act = act = null; } }
      // 主将与战马
      let hc = 'idle', rc = 'idle', rt = A.t, ht = A.t;
      if (act) { rc = act.rider; rt = Math.min(A.actT, act.dur); hc = act.horse; ht = A.actT; if (act.kind === 'rout') { rt = Math.min(A.actT, 0.45); } }
      else if (moving) { hc = A.gait; }
      if (hc === 'rear' && A.actT > 1.1) hc = 'idle';
      try {
        this.horse.setPose(hc, ht);
        this.fig.setPose(SG.Figure.clips[rc] ? rc : 'idle', rt, { gallop: hc === 'gallop' });
        if (this.fig.update) this.fig.update(dt);
      } catch (e) { /* 片段缺失时保持上一姿势 */ }
      // 着色（受击闪白 / 灼烧 / 必杀光）
      const k = mat.flash > 0.01 ? Math.round(mat.flash * 20) / 20 : 0;
      const key = k ? cssHex(mat.flashColor) + k : '';
      if (key !== A.tintKey) {
        A.tintKey = key;
        try { this.fig.setTint(k ? cssHex(mat.flashColor) : '#ffffff', k * 0.8); this.horse.setTint(k ? cssHex(mat.flashColor) : '#ffffff', k * 0.6); } catch (e) { /* 忽略 */ }
      }
      // 士兵：动作结束后回到待机 / 行军片段
      if (cr && this.sold.length) {
        const base = (q) => q.mounted ? (moving ? (A.gait === 'walk' ? 'walk' : A.gait) : 'idle') : (moving ? (A.gait === 'walk' ? 'walk' : 'run') : 'idle');
        for (const q of this.sold) {
          if (A.routing) continue;
          if (act && q.clip && q.clip !== 'idle' && q.clip !== 'walk' && q.clip !== 'run' && q.clip !== 'trot' && q.clip !== 'gallop') continue;
          const b = base(q);
          if (q.clip !== b) { q.clip = b; cr.play(q.id, b, { phase: q.jitter }); }
        }
        this._placeSoldiers(false);
      }
    }
    // 主将 + 战马当前姿势烘焙为静态几何体（部队局部坐标；残影特效用，调用方负责释放）
    ghostGeometry() {
      const pos = [], idx = [];
      if (this.fig) {
        this.group.updateMatrixWorld(true);
        _bi.copy(this.group.matrixWorld).invert();
        const meshes = [];
        this.figRoot.traverse(o => { if (o.isMesh && o.visible && o.geometry && o.geometry.attributes.position) meshes.push(o); });
        for (const ms of meshes) {
          const g = ms.geometry, P = g.attributes.position, base = pos.length / 3;
          _bm.multiplyMatrices(_bi, ms.matrixWorld);
          const skinned = ms.isSkinnedMesh && typeof ms.applyBoneTransform === 'function';
          if (skinned && ms.skeleton) ms.skeleton.update();
          const n = P.count;
          for (let i = 0; i < n; i++) {
            _bv.fromBufferAttribute(P, i);
            if (skinned) ms.applyBoneTransform(i, _bv);
            _bv.applyMatrix4(_bm);
            pos.push(_bv.x, _bv.y, _bv.z);
          }
          if (g.index) { const I = g.index.array; for (let i = 0; i < I.length; i++) idx.push(I[i] + base); }
          else for (let i = 0; i < n; i++) idx.push(i + base);
        }
      }
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      if (idx.length) geo.setIndex(pos.length / 3 > 65535 ? new THREE.Uint32BufferAttribute(idx, 1) : new THREE.Uint16BufferAttribute(idx, 1));
      return geo;
    }

    dispose() {
      if (this.follow) { try { this.follow.remove(); } catch (e) { /* 忽略 */ } this.follow = null; }
      if (this.info && this.info.anchor.parentNode) this.info.anchor.parentNode.removeChild(this.info.anchor);
      this.info = null;
      if (this._mesh && this._mesh.geometry) this._mesh.geometry.dispose();
      if (this._lp) this._lp.dispose();
      if (this.confuseRing) this.confuseRing.material.dispose();
      this._spDispose();
      const cr = this.view.crowd;
      if (cr) for (const q of this.sold) cr.remove(q.id);
      this.sold.length = 0;
      if (this.figRoot && this.figRoot.parent) this.figRoot.parent.remove(this.figRoot);
      try { if (this.fig) this.fig.dispose(); if (this.horse) this.horse.dispose(); } catch (e) { console.error(e); }
      this.fig = this.horse = this.figRoot = null;
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
      this.hlGeos = new Map();        // "x,y" → 贴合地形的高亮网格
      this.time = 0;
      this.disposed = false;
      this.emitters = [];
      this.burnFx = new Map();        // "x,y" → { emitters }
      this.temp = [];                 // 临时网格（特效）
      this.cursorUnit = null;
      this._lblKey = '';              // 视口尺寸变化时重新量标签
      this._lblMeasureT = 0;
      this.stats = { buildMs: 0, terrainTris: 0, propTris: 0, fortTris: 0, trees: 0, rocks: 0, tufts: 0 };
      const t0 = performance.now();
      this.build();
      this.stats.buildMs = performance.now() - t0;
      if (SG.Gfx && SG.Gfx.scene) SG.Gfx.scene.add(this.group);
      // 第四版：战场的时刻（主场景的 SG.Gfx.mood；缺省为温暖的午后 BattleView.MOOD_HOUR）。战略地图隐藏时会把氛围还原成它构建前的
      // 时刻，所以每帧在 update 里确认一次；释放时还原为开战前的时刻
      const st = SG.Gfx && SG.Gfx.moodState ? SG.Gfx.moodState() : null;
      this._moodPrev = st ? st.hour : null;
      this._moodHour = BattleView.MOOD_HOUR;
      this._applyMood();
    }
    get root() { return this.group; }
    // setMood('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | 小时数 | null)：战场的时刻（null = 缺省的午后）
    setMood(what) {
      const H = SG.LP && SG.LP.MOOD_HOURS;
      this._moodHour = what == null ? BattleView.MOOD_HOUR : typeof what === 'number' ? what : (H && H[what] != null ? H[what] : BattleView.MOOD_HOUR);
      this._applyMood();
      return SG.Gfx && SG.Gfx.moodState ? SG.Gfx.moodState() : null;
    }
    _applyMood() {
      const G = SG.Gfx;
      if (this.disposed || !G || !G.mood || !G.setMood || this._moodHour == null) return;
      const st = G.moodState();
      if (!st || Math.abs(st.hour - this._moodHour) > 1e-3) G.setMood(this._moodHour);
    }

    get boardCenter() { return SG.U(ORIGIN.x + this.model.W * T / 2, 0, ORIGIN.z + this.model.H * T / 2); }

    // 格坐标（u, v；格子中心为整数）↔ 世界坐标
    _uv(wx, wz) { return { u: (wx - ORIGIN.x) / T - 0.5, v: (-wz - ORIGIN.z) / T - 0.5 }; }
    // 地表高度（世界坐标 x, z → y）；水面以下的地方取涉水高度（部队、士兵站在水里而不是河底）
    groundY(wx, wz) {
      if (!this.ter) return 0.08;
      const u = (wx - ORIGIN.x) / T - 0.5, v = (-wz - ORIGIN.z) / T - 0.5;
      const xi = Math.round(u), yi = Math.round(v);
      if (this._bridgeY && xi >= 0 && yi >= 0 && xi < this.model.W && yi < this.model.H) {
        const b = this._bridgeY[yi * this.model.W + xi];
        if (b && Math.abs(u - xi) < 0.55 && Math.abs(v - yi) < 0.55) return b;
      }
      return Math.max(this._gridH(u, v), WADE_Y);
    }
    // 格子中心的地表高度（旧接口名）
    topH(x, y) {
      if (this._tileY) return this._tileY[y * this.model.W + x];
      return 0.08;
    }
    // 格子中心（three 世界坐标）：部队站立处（河川为涉水高度、桥为桥面、城墙为墙顶）
    tile(x, y) {
      const h = this._tileY ? this._tileY[M.clamp(y, 0, this.model.H - 1) * this.model.W + M.clamp(x, 0, this.model.W - 1)] : 0.08;
      return SG.U(ORIGIN.x + x * T + T / 2, h, ORIGIN.z + y * T + T / 2);
    }
    cultureOf(side) {
      const setup = this.model.setup || {};
      let c = null;
      try {
        const city = side === 1 ? setup.target : (setup.src || null);
        if (city && SG.CultureArt && SG.CultureArt.cultureOfCity) c = SG.CultureArt.cultureOfCity(city);
      } catch (e) { c = null; }
      return c || (this.look && this.look.culture) || 'han';
    }

    // ---------------------------------------------------------- 构建 --
    build() {
      const m = this.model, Tn = TR();
      const setup = m.setup || {};
      const look = this.look = BattleLook.of(setup);
      const atkCol = this._factionColor(setup.attacker);
      const defCol = this._factionColor(setup.defender);
      const ph = this.stats.phases = {};
      let tp = performance.now();
      const lap = k => { const n = performance.now(); ph[k] = Math.round(n - tp); tp = n; };
      // 第四版：有 SG.LP / SG.LPProps 时按 Lanternvale 风格画布景（否则第三版）
      this._lp = LPOK();
      const Tm = this.ter = terrainModel(this);
      const W = m.W, H = m.H;
      lap('model');
      this._buildTerrainMesh(); lap('terrain');
      // 各格的站立高度（取渲染出的地表，与网格三角形一致）
      this._tileY = new Float32Array(W * H);
      this._bridgeY = new Float32Array(W * H);
      for (const k of Tm.bridges.keys()) { const [x, y] = k.split(',').map(Number); this._bridgeY[y * W + x] = WATER_Y + 0.34; }
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        const t = m.map[x][y], o = y * W + x;
        let h = this._gridH(x, y);
        if (this._bridgeY[o]) h = this._bridgeY[o];
        else if (t === Tn.River || t === TERRAIN_WATER) h = WADE_Y;
        else if (t === Tn.Wall) h = BASE_Y + 0.06 + 1.45;
        else if (Tm.inFort(x, y)) h = BASE_Y + 0.06;
        this._tileY[o] = Math.max(h, WADE_Y);
      }
      this._buildWater(); lap('water');
      this._buildProps(); lap('props');
      // 城池
      if (this._lp) this._buildFortLP(look, defCol);
      else {
        const fb = new SG.MeshBuilder();
        buildFort(this, fb, Tm, look, defCol);
        this._buildBridges(fb);
        const fortMat = SG.Gfx.masonryMaterial ? SG.Gfx.masonryMaterial(36, 90) : SG.Gfx.lowPoly();
        this._fortMat = fortMat;
        const fort = SG.Gfx.mesh(fb.toGeometry(), fortMat, { castShadow: true, receiveShadow: true });
        fort.name = 'Fort';
        this.stats.fortTris = fb.count / 3;
        this.group.add(fort);
      }
      lap('fort');
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
      this.sideRing = [0, 1].map(i => SG.Gfx.unlit(SIDE_BAR[i], SG.Gfx.ringTexture, 0.7));
      // 粒子与光（绘制顺序：烟尘 → 火底热光 → 火舌 → 火花 / 炽热火芯）
      this.glow = new ParticlePool(1200, THREE.AdditiveBlending, 1.0, 14);   // 火花、法术光点、火芯
      this.flame = new ParticlePool(1400, THREE.NormalBlending, 1.0, 13, flameTexture());
      this.heat = new ParticlePool(500, THREE.AdditiveBlending, 1.0, 12);
      this.dust = new ParticlePool(900, THREE.NormalBlending, 0.6, 11);      // 烟尘
      this.group.add(this.dust.points);
      this.group.add(this.heat.points);
      this.group.add(this.flame.points);
      this.group.add(this.glow.points);
      this.lights = [];
      for (let i = 0; i < 2; i++) {
        const l = new THREE.PointLight(C(1, 0.5, 0.2), 0, 6, 1);
        l.castShadow = false;
        l.userData = { busy: false, base: 0, cur: 0, seed: i * 17.3, glow: null };
        this.group.add(l);
        this.lights.push(l);
      }
      // 部队：人物模型（figure.js）的士兵为同一个实例化士兵群
      this.crowd = null;
      if (SG.Figure && typeof SG.Figure.crowd === 'function') {
        // 第三轮修订：LP 版的士兵脚下已有柔和的圆影（UnitShadows），不再另投实时阴影
        try { this.crowd = SG.Figure.crowd({ lod: 'low', shadow: !this._lp }); this.crowd.root.name = 'Soldiers'; this.group.add(this.crowd.root); }
        catch (e) { console.error(e); this.crowd = null; }
      }
      // 第四版：部队（主将、战马、士兵）脚下柔和的圆影，全部部队一次绘制（每帧在 update 里摆放）
      this._blobs = null;
      if (this._lp && SG.LP.BlobShadows) {
        this._blobs = new SG.LP.BlobShadows(Math.max(16, m.units.length * 8));
        this._blobs.mesh.name = 'UnitShadows';
        this.group.add(this._blobs.mesh);
      }
      lap('fx');
      for (const u of m.units) this.createUnit(u, u.side === 0 ? atkCol : defCol);
      lap('units');
      // 人物模型：分片预载（面部贴图、模板、士兵群动作烘焙）后再建，不卡住开场；this.ready 在部队模型就绪后兑现
      this.ready = this.crowd ? this._prepareFigures() : Promise.resolve(this);
    }
    _prepareFigures() {
      const t0 = performance.now();
      const vis = [...this.visuals.values()];
      const fc = c => (SG.Figure.cultures && SG.Figure.cultures.indexOf(c) >= 0 ? c : 'han');
      const cults = [...new Set(vis.map(v => fc(v.culture)))];
      const specs = [];
      for (const v of vis) for (const q of v._soldierSlots(6)) {
        const sp = { type: q.type, culture: fc(v.culture), seed: ((v.u.gen.id | 0)) % 2, mounted: q.mounted };
        for (const sd of [0, 1]) { const o = Object.assign({}, sp, { seed: sd }); if (!specs.some(e => e.type === o.type && e.culture === o.culture && e.seed === o.seed && e.mounted === o.mounted)) specs.push(o); }
      }
      let p;
      try { p = SG.Figure.preload({ cultures: cults, types: ['spear'], generals: vis.map(v => v.u.gen), generalLod: 'mid', crowd: specs }); }
      catch (e) { console.error(e); p = Promise.resolve(); }
      return p.catch(e => { console.error(e); }).then(() => {
        if (this.disposed) return this;
        const t1 = performance.now();
        for (const v of this.visuals.values()) v.figuresReady();
        this.stats.phases.preload = Math.round(t1 - t0);
        this.stats.phases.figures = Math.round(performance.now() - t1);
        return this._warmShaders();
      });
    }
    // 第三版修订：人物建好后先编译着色器（compileAsync，最多等 1.5 秒）并渲染两帧再兑现 ready——
    // 慢的 GPU 上第一回合开始时人物已经画出来（不再只有光环与名牌）
    async _warmShaders() {
      const t0 = performance.now();
      try {
        const r = SG.Gfx.renderer, sc = SG.Gfx.scene, cam = SG.Gfx.camera;
        if (r && sc && cam) {
          // 没有 KHR_parallel_shader_compile 时 compileAsync 只会告警并退化为同步编译：直接同步编译
          const par = r.extensions && r.extensions.has && r.extensions.has('KHR_parallel_shader_compile');
          if (r.compileAsync && par) await Promise.race([r.compileAsync(sc, cam), SG.wait(1.5)]);
          else r.compile(sc, cam);
        }
      } catch (e) { /* 首帧再编译 */ }
      if (SG.frame) { await SG.frame(); await SG.frame(); }
      this.stats.phases.warm = Math.round(performance.now() - t0);
      return this;
    }

    // 平滑地形网格（非均匀栅格：棋盘内细、棋盘外粗），顶点色 = 群落底色，aMat = (沙, 岩石倾向, 雪, 1 − 农田)
    _buildTerrainMesh() {
      const Tm = this.ter, G = Tm.G, m = this.model, W = m.W, H = m.H;
      const xs = terrainAxis(W, EXT_X), ys = terrainAxis(H, EXT_Y), nx = xs.length, ny = ys.length, N = nx * ny;
      const P = new Float32Array(N * 3), Nn = new Float32Array(N * 3), Cc = new Float32Array(N * 3), Am = new Float32Array(N * 4), Bt = new Float32Array(N * 4);
      const hh = new Float32Array(N);
      for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) hh[j * nx + i] = Tm.heightAt(xs[i], ys[j]);
      // 网格外缘：最外 3 格逐渐压平到外圈「裙边」的高度（远景不露出网格的断面；裙边为陆地或海面，取外缘多数）
      const uMin = xs[0], uMax = xs[nx - 1], vMin = ys[0], vMax = ys[ny - 1];
      const edgeD = (u, v) => Math.min(u - uMin, uMax - u, v - vMin, vMax - v);
      let wet = 0, tot = 0;
      for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) if (edgeD(xs[i], ys[j]) < 1.01) { tot++; if (hh[j * nx + i] < WATER_Y) wet++; }
      const seaSkirt = wet > tot * 0.5, skirtY = seaSkirt ? WATER_Y - 0.7 : BASE_Y - 0.05;
      for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) { const k = sm(0, 3, edgeD(xs[i], ys[j])); if (k < 1) hh[j * nx + i] = M.lerp(skirtY, hh[j * nx + i], k); }
      const nu = G.nu, nv = G.nv, tint = [0, 0, 0], edgeTint = [0, 0, 0];
      let edgeN = 0;
      // 第四版：LP 地形材质与绘制式的顶点色（vec4：sRGB 反照率 + alpha 里的自发光编码 0）
      this._roadTex = this._lp ? roadTextureLP(Tm) : null;
      const lpMat = this._lp ? boardMaterialLP(W, H, this._roadTex) : null;
      const C4 = lpMat ? new Float32Array(N * 4) : null, edge4 = [0, 0, 0];
      const pal = C4 ? meadowPal() : null, pw = [[0, 0, 0], [0, 0, 0], [0, 0, 0], [0, 0, 0]];
      for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) {
        const o = j * nx + i, u = xs[i], v = ys[j], h = hh[o];
        P[o * 3] = ORIGIN.x + (u + 0.5) * T; P[o * 3 + 1] = h; P[o * 3 + 2] = -(ORIGIN.z + (v + 0.5) * T);
        // 法线：栅格差分（非均匀间距）
        const i0 = Math.max(0, i - 1), i1 = Math.min(nx - 1, i + 1), j0 = Math.max(0, j - 1), j1 = Math.min(ny - 1, j + 1);
        const dhdx = (hh[j * nx + i1] - hh[j * nx + i0]) / ((xs[i1] - xs[i0]) * T), dhdz = (hh[j1 * nx + i] - hh[j0 * nx + i]) / ((ys[j1] - ys[j0]) * T);
        const nl = Math.sqrt(dhdx * dhdx + 1 + dhdz * dhdz);
        Nn[o * 3] = -dhdx / nl; Nn[o * 3 + 1] = 1 / nl; Nn[o * 3 + 2] = dhdz / nl;
        // 群落底色（四角群落色的双线性混合）
        const fi = Tm.gi(u), fj = Tm.gj(v);
        const ci = Math.max(0, Math.min(nu - 2, Math.floor(fi))), cj = Math.max(0, Math.min(nv - 2, Math.floor(fj))), tx = M.clamp01(fi - ci), ty = M.clamp01(fj - cj);
        tint[0] = tint[1] = tint[2] = 0;
        for (const [a, b, w] of [[0, 0, (1 - tx) * (1 - ty)], [1, 0, tx * (1 - ty)], [0, 1, (1 - tx) * ty], [1, 1, tx * ty]]) {
          const bc = BIOME_TINT[G.biome[(cj + b) * nu + ci + a]] || BIOME_TINT[0];
          tint[0] += bc[0] * w; tint[1] += bc[1] * w; tint[2] += bc[2] * w;
        }
        const inB = u > -0.5 && u < W - 0.5 && v > -0.5 && v < H - 0.5;
        const fo = inB ? Tm.maskAt(Tm.mask.forest, u, v) : sm(0.3, 0.7, bilinear(G.forest, nu, nv, fi, fj));
        const mt = inB ? Tm.maskAt(Tm.mask.mountain, u, v) : 0;
        const pl = inB ? Tm.maskAt(Tm.mask.plain, u, v) + Tm.maskAt(Tm.mask.forest, u, v) * 0.3 : 1;
        // 农田：棋盘外照世界地图；棋盘内只在城外两三格内淡淡地出现（田块的田埂会和格线混淆）
        const dCa = Math.hypot(u - Tm.castle.x, v - Tm.castle.y);
        const dOut = Math.max(-0.5 - u, u - (W - 0.5), -0.5 - v, v - (H - 0.5), 0);
        // 第三版修订：棋盘内保留约 35% 的淡淡田色（不再是一块与周围农田硬接的绿毯），棋盘边的过渡加宽到 3 格；
        // 草原、沙漠群落不画农田（不再出现台阶状的田块）
        const biF = G.biome[M.clamp(Math.round(fj), 0, nv - 1) * nu + M.clamp(Math.round(fi), 0, nu - 1)] | 0;
        const farm = bilinear(G.farm, nu, nv, fi, fj) * M.clamp01(pl) * (1 - fo) * M.lerp(Math.max(0.35, 0.75 * sm(6.5, 3.2, dCa)), 1, sm(0, 3, dOut)) * (biF === 1 || biF === 2 ? 0.1 : 1);
        const road = Tm.Tr ? 1 - sm(0.05, 0.12, bilinear(G.road, nu, nv, fi, fj)) : 0;
        const fortK = inB ? Tm.maskAt(Tm.mask.fort, u, v) : 0;
        let r = tint[0], g = tint[1], b = tint[2];
        r += (FOREST_TINT[0] - r) * fo * 0.55; g += (FOREST_TINT[1] - g) * fo * 0.55; b += (FOREST_TINT[2] - b) * fo * 0.55;
        // 山岳：荒芜的灰褐（高处、陡处更明显）；山丘：略带赭黄的草坡
        const hl = inB ? Tm.maskAt(Tm.mask.hill, u, v) : 0;
        const bare = Math.max(mt * 0.75, sm(BASE_Y + 1.3, BASE_Y + 2.6, h) * 0.6);
        r += (0.56 - r) * bare; g += (0.52 - g) * bare; b += (0.42 - b) * bare;
        r += (0.58 - r) * hl * 0.25; g += (0.6 - g) * hl * 0.25; b += (0.34 - b) * hl * 0.25;
        const rk = road * (1 - fortK) * (h > WATER_Y + 0.15 ? 1 : 0);
        r += (DIRT_TINT[0] - r) * rk * 0.8; g += (DIRT_TINT[1] - g) * rk * 0.8; b += (DIRT_TINT[2] - b) * rk * 0.8;
        Cc[o * 3] = r; Cc[o * 3 + 1] = g; Cc[o * 3 + 2] = b;
        const ed = edgeD(u, v);
        if (ed < 3.01) { if (ed > 2.5) { edgeTint[0] += r; edgeTint[1] += g; edgeTint[2] += b; edgeN++; } }
        const sand = bilinear(G.sand, nu, nv, fi, fj);
        const bi = G.biome[Math.round(fj) * nu + Math.round(fi)] | 0;
        // 第三版修订：积雪按世界地图的绝对海拔（世界雪线约 6–7.8）：低地战场（中原、两河）的山上不再有雪
        const gSnow = bilinear(G.snow, nu, nv, fi, fj);
        const coldW = G.synth ? 1 : Math.max(gSnow > 0.05 ? 1 : 0, sm(3.4, 5.8, bilinear(G.h, nu, nv, fi, fj)));
        const cold = (bi === 6 || bi === 7 ? 1 : bi === 0 || bi === 1 ? 0.6 : 0) * coldW;
        const snow = Math.max(gSnow, sm(BASE_Y + 3.0, BASE_Y + 3.7, h) * cold);
        Am[o * 4] = sand; Am[o * 4 + 1] = mt * 0.5 + sm(BASE_Y + 1.4, BASE_Y + 2.6, h) * 0.3; Am[o * 4 + 2] = snow * (1 - mt); Am[o * 4 + 3] = 1 - M.clamp01(farm * (1 - rk) * (1 - fortK));
        // 积雪：山岳上高出本格山基约 1.4–1.85 以上的峰顶（寒冷度按群落）
        const peakH = inB && mt > 0.01 ? h - bilinear(Tm.tgt, W, H, u, v) : 0;
        Bt[o * 4] = hl * (1 - rk * 0.6); Bt[o * 4 + 1] = mt; Bt[o * 4 + 2] = (COLD[bi] || 0) * coldW * sm(1.4, 1.85, peakH) * mt;
        if (C4) {
          // 绘制式地面：群落四色（四角混合）的草地斑块 → 森林地面 / 丘坡的赭黄 / 田块 / 泥土路 / 岩石 / 沙 / 雪
          for (const q of pw) q[0] = q[1] = q[2] = 0;
          for (const [a, b, w] of [[0, 0, (1 - tx) * (1 - ty)], [1, 0, tx * (1 - ty)], [0, 1, (1 - tx) * ty], [1, 1, tx * ty]]) {
            const P4 = pal[G.biome[(cj + b) * nu + ci + a]] || pal[0];
            for (let q = 0; q < 4; q++) { pw[q][0] += P4[q][0] * w; pw[q][1] += P4[q][1] * w; pw[q][2] += P4[q][2] * w; }
          }
          const n1 = vnoise(u * 0.5, v * 0.5, 1), n2 = vnoise(u * 1.6, v * 1.6, 2), n3 = vnoise(u * 2.7, v * 2.7, 3);
          let c = paintMeadow(pw, n1, n2, n3);
          if (fo > 0.01) c = mix3(c, mix3(mul3(pw[3], 0.84), hx('#6E6A3C'), 0.12 + 0.22 * n3), fo * 0.62);
          if (hl > 0.01) c = mix3(c, mix3(pw[0], hx('#C8B878'), 0.5), hl * (0.32 + 0.3 * n2) * (1 - rk));
          // 修订：田块（权重 aMat.w）与道路（距离场贴图，Bt.w = 可铺路的权重）改在着色器里画，边缘在任何网格疏密下都清楚
          Bt[o * 4 + 3] = (1 - fortK) * sm(WATER_Y + 0.1, WATER_Y + 0.2, h);
          if (bare > 0.01) c = mix3(c, mul3(mix3(hx('#8E877C'), hx('#AAA294'), n2), 0.92 + 0.12 * n3), M.clamp01(bare * 1.2));
          if (sand > 0.01) c = mix3(c, mul3(hx('#E2CFA0'), 0.95 + 0.08 * n2), M.clamp01(sand));
          const snowK = Math.max(Am[o * 4 + 2], Bt[o * 4 + 2]);
          if (snowK > 0.01) c = mix3(c, mul3(hx('#EEF1F5'), 0.95 + 0.06 * n3), M.clamp01(snowK));
          if (h < WATER_Y) c = mix3(c, mul3(hx('#B4A27C'), 1 - 0.25 * M.clamp01((WATER_Y - h) / 0.9)), M.clamp01((WATER_Y - h) / 0.2));
          C4[o * 4] = M.clamp01(c[0]); C4[o * 4 + 1] = M.clamp01(c[1]); C4[o * 4 + 2] = M.clamp01(c[2]); C4[o * 4 + 3] = 0;
          if (ed > 2.5 && ed < 3.01) { edge4[0] += C4[o * 4]; edge4[1] += C4[o * 4 + 1]; edge4[2] += C4[o * 4 + 2]; }
        }
      }
      const idx = new (N > 65535 ? Uint32Array : Uint16Array)((nx - 1) * (ny - 1) * 6);
      const split = new Uint8Array((nx - 1) * (ny - 1));   // 1：沿 a–d 对角线切分
      let q = 0;
      for (let j = 0; j < ny - 1; j++) for (let i = 0; i < nx - 1; i++) {
        const a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
        // 沿较短的对角线切分（起伏处更平滑）
        if (Math.abs(hh[a] - hh[d]) <= Math.abs(hh[b] - hh[c])) { idx[q++] = a; idx[q++] = b; idx[q++] = d; idx[q++] = a; idx[q++] = d; idx[q++] = c; split[j * (nx - 1) + i] = 1; }
        else { idx[q++] = a; idx[q++] = b; idx[q++] = c; idx[q++] = b; idx[q++] = d; idx[q++] = c; }
      }
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(P, 3));
      geo.setAttribute('normal', new THREE.BufferAttribute(Nn, 3));
      geo.setAttribute('color', C4 ? new THREE.BufferAttribute(C4, 4) : new THREE.BufferAttribute(Cc, 3));
      geo.setAttribute('aMat', new THREE.BufferAttribute(Am, 4));
      geo.setAttribute('aBt', new THREE.BufferAttribute(Bt, 4));
      geo.setIndex(new THREE.BufferAttribute(idx, 1));
      geo.computeBoundingSphere();
      // 外缘 3 格的顶点色渐变到外缘平均色（与裙边一致）
      if (edgeN) { edgeTint[0] /= edgeN; edgeTint[1] /= edgeN; edgeTint[2] /= edgeN; }
      for (let j = 0; j < ny; j++) for (let i = 0; i < nx; i++) {
        const k = sm(0, 3, edgeD(xs[i], ys[j]));
        if (k >= 1) continue;
        const o = j * nx + i;
        for (let c = 0; c < 3; c++) Cc[o * 3 + c] = M.lerp(edgeTint[c], Cc[o * 3 + c], k);
        if (C4 && edgeN) for (let c = 0; c < 3; c++) C4[o * 4 + c] = M.lerp(edge4[c] / edgeN, C4[o * 4 + c], k);
        Am[o * 4] *= k; Am[o * 4 + 1] *= k; Am[o * 4 + 2] *= k; Am[o * 4 + 3] = M.lerp(1, Am[o * 4 + 3], k); Bt[o * 4 + 3] *= k;
      }
      if (C4 && edgeN) { edge4[0] /= edgeN; edge4[1] /= edgeN; edge4[2] /= edgeN; }
      const mat = this._terrainMat = lpMat || battleTerrainMaterial(W, H);
      const mesh = SG.Gfx.mesh(geo, mat, { castShadow: false, receiveShadow: true });
      mesh.name = 'Board';
      this.group.add(mesh);
      // 裙边：网格外的一圈大平面（陆地用同一地形材质、外缘平均色；海面用水面材质）
      {
        const X0 = ORIGIN.x + (uMin + 0.5) * T, X1 = ORIGIN.x + (uMax + 0.5) * T, Z0 = ORIGIN.z + (vMin + 0.5) * T, Z1 = ORIGIN.z + (vMax + 0.5) * T, R = 260;
        const y = seaSkirt ? WATER_Y : skirtY;
        const pos = [], col = [], nor = [], ix = [];
        const pts = [[X0, Z0], [X1, Z0], [X1, Z1], [X0, Z1], [X0 - R, Z0 - R], [X1 + R, Z0 - R], [X1 + R, Z1 + R], [X0 - R, Z1 + R]];
        for (const [x, z] of pts) { pos.push(x, y, -z); nor.push(0, 1, 0); if (seaSkirt) col.push(0, 0, 0); else if (C4) col.push(edge4[0], edge4[1], edge4[2], 0); else col.push(edgeTint[0], edgeTint[1], edgeTint[2]); }
        for (let k = 0; k < 4; k++) { const a = k, b = (k + 1) % 4, c = 4 + k, d = 4 + (k + 1) % 4; ix.push(a, c, d, a, d, b); }
        const sg = new THREE.BufferGeometry();
        sg.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        sg.setAttribute('normal', new THREE.Float32BufferAttribute(nor, 3));
        sg.setAttribute('color', new THREE.Float32BufferAttribute(col, C4 && !seaSkirt ? 4 : 3));
        if (!seaSkirt) sg.setAttribute('aMat', new THREE.Float32BufferAttribute(new Float32Array(8 * 4).map((_, i) => (i % 4 === 3 ? 1 : 0)), 4));
        if (!seaSkirt) sg.setAttribute('aBt', new THREE.Float32BufferAttribute(new Float32Array(8 * 4), 4));
        sg.setIndex(ix);
        sg.computeBoundingSphere();
        const skirt = SG.Gfx.mesh(sg, seaSkirt ? SG.Gfx.water() : mat, { castShadow: false, receiveShadow: !seaSkirt });
        skirt.name = 'Skirt';
        if (seaSkirt) skirt.renderOrder = 1;
        this.group.add(skirt);
        this.stats.skirt = seaSkirt ? 'sea' : 'land';
      }
      this.stats.terrainTris = idx.length / 3;
      this._grid = { xs, ys, nx, ny, hh, split, W, H, sx: axisSegs(W, EXT_X), sy: axisSegs(H, EXT_Y) };
    }
    // 渲染出的地表高度（格坐标 u, v）：在网格三角形上插值（与画面完全一致）；网格外退回高度函数
    _gridH(u, v) {
      const g = this._grid;
      if (!g) return this.ter.heightAt(u, v);
      const fi = axisIndex(g.sx, u), fj = axisIndex(g.sy, v);
      if (!(fi >= 0 && fj >= 0 && fi <= g.nx - 1 && fj <= g.ny - 1)) return this.ter.heightAt(u, v);
      const i = Math.min(g.nx - 2, Math.floor(fi)), j = Math.min(g.ny - 2, Math.floor(fj)), tx = fi - i, ty = fj - j;
      const nx = g.nx, hh = g.hh, a = hh[j * nx + i], b = hh[j * nx + i + 1], c = hh[(j + 1) * nx + i], d = hh[(j + 1) * nx + i + 1];
      if (g.split[j * (nx - 1) + i]) return ty <= tx ? a + (b - a) * tx + (d - b) * ty : a + (c - a) * ty + (d - c) * tx;
      return tx + ty <= 1 ? a + (b - a) * tx + (c - a) * ty : d + (c - d) * (1 - tx) + (b - d) * (1 - ty);
    }

    // 水面：取样栅格（每 0.5 格）上，凡有角点低于水面的方格铺一块水面；顶点色 r = 浅水（岸边浪花）
    _buildWater() {
      const Tm = this.ter, G = Tm.G, nu = G.nu, nv = G.nv;
      const hw = new Float32Array(nu * nv);
      for (let j = 0; j < nv; j++) for (let i = 0; i < nu; i++) hw[j * nu + i] = this._gridH(G.u0 + i * G.step, G.v0 + j * G.step);
      const pos = [], col = [], idx = [], vid = new Int32Array(nu * nv).fill(-1);
      const vert = o => {
        if (vid[o] >= 0) return vid[o];
        const i = o % nu, j = (o / nu) | 0, u = G.u0 + i * G.step, v = G.v0 + j * G.step;
        pos.push(ORIGIN.x + (u + 0.5) * T, WATER_Y, -(ORIGIN.z + (v + 0.5) * T));
        const depth = WATER_Y - hw[o];
        col.push(1 - M.clamp01(depth / 0.9), 0, 0);
        return (vid[o] = pos.length / 3 - 1);
      };
      for (let j = 0; j < nv - 1; j++) for (let i = 0; i < nu - 1; i++) {
        const a = j * nu + i, b = a + 1, c = a + nu, d = c + 1;
        if (Math.min(hw[a], hw[b], hw[c], hw[d]) >= WATER_Y + 0.02) continue;
        const A = vert(a), B = vert(b), Cq = vert(c), D = vert(d);
        idx.push(A, B, D, A, D, Cq);
      }
      if (!idx.length) return;
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      geo.setAttribute('color', new THREE.Float32BufferAttribute(col, 3));
      geo.setIndex(idx);
      geo.computeBoundingSphere();
      const water = SG.Gfx.mesh(geo, SG.Gfx.water(), { castShadow: false, receiveShadow: false });
      water.name = 'Water';
      water.renderOrder = 1;
      this.group.add(water);
      this.stats.waterTris = idx.length / 3;
    }

    // 树、石、草丛（实例化；种类按群落）
    _buildProps() {
      const Tm = this.ter, G = Tm.G, m = this.model, Tn = TR(), W = m.W, H = m.H, nu = G.nu, nv = G.nv;
      const rnd = SG.SeededRandom((Tm.seed ^ 0x5bd1e995) | 0);
      const R = () => rnd.nextDouble();
      const lists = {};                // kind → [{ x, y, z, s, yaw, c: [r,g,b], near }]
      // hi：棋盘外、镜头一侧离棋盘边 3 格以内的树也用近景模板（镜头下方近在眼前，不再露出一级细分的多边形树冠）
      const lp = this._lp;
      const put = (kind, u, v, s, cm, near, sink, hi) => {
        const wx = ORIGIN.x + (u + 0.5) * T, wz = -(ORIGIN.z + (v + 0.5) * T);
        const y = this._gridH(u, v) - (sink || 0.04) * s;
        // 第四版：杨、云杉有自己的 LP 配方（不借阔叶 / 松的模板非等比缩放）
        const al = lp ? null : TREE_ALIAS[kind];
        if (al) kind = near || hi ? al[0] : al[1];
        (lists[kind] || (lists[kind] = [])).push({ x: wx, y, z: wz, s, yaw: R() * Math.PI * 2, c: cm, near, hi: !!hi, u, v, ax: al ? al[2] : 1, ay: al ? al[3] : 1, bi: lp ? biomeAt(u, v) : 0 });
      };
      const cult = (this.look && this.look.culture) || 'han';
      const tgt = (m.setup || {}).target, lat = tgt && typeof tgt.lat === 'number' ? tgt.lat : null;
      const tb = bi => treeBiome(bi, cult, lat);
      const biomeAt = (u, v) => G.biome[M.clamp(Math.round(Tm.gj(v)), 0, nv - 1) * nu + M.clamp(Math.round(Tm.gi(u)), 0, nu - 1)] | 0;
      const leaf = (bi) => { const L = LEAF_MUL[bi] || LEAF_MUL[0], k = 0.86 + R() * 0.28; return [L[0] * k, L[1] * k, L[2] * k]; };
      const occupied = (x, y) => m.units.some(u => u.alive && u.x === x && u.y === y);
      // 棋盘内：森林格的树围成一圈（格子中心留给部队），山岳的石块与松，山丘偶有孤树
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        const t = m.map[x][y];
        if (Tm.inFort(x, y)) continue;
        const bi = biomeAt(x, y);
        if (t === Tn.Forest) {
          const n = 5, a0 = R() * Math.PI;
          for (let k = 0; k < n; k++) {
            const a = a0 + k * Math.PI * 2 / n + (R() - 0.5) * 0.5, r = 0.66 + R() * 0.26;
            put(pickW(treeKinds(tb(bi), true, cult), R()), x + Math.cos(a) * r, y + Math.sin(a) * r, 0.85 + R() * 0.4, leaf(bi), true);
          }
          if (!occupied(x, y) && R() < 0.2) put(pickW(treeKinds(tb(bi), true, cult), R()), x + (R() - 0.5) * 0.3, y + (R() - 0.5) * 0.3, 0.8 + R() * 0.3, leaf(bi), true);
        } else if (t === Tn.Mountain) {
          const n = R() < 0.6 ? 1 : 0;
          for (let k = 0; k < n; k++) { const g = 0.88 + R() * 0.24; put('rock', x + (R() - 0.5) * 0.9, y + (R() - 0.5) * 0.9, 0.2 + R() * 0.22, [g, g * 0.98, g * 0.95], true, 0.25); }
          const mk = MOUNTAIN_TREE[tb(bi)];
          if (mk && R() < 0.6) put(mk, x + (R() - 0.5) * 1.1, y + (R() - 0.5) * 1.1, 0.6 + R() * 0.3, leaf(bi), true);
        } else if (t === Tn.Hill) {
          if (R() < 0.3) put('rock', x + (R() - 0.5) * 1.2, y + (R() - 0.5) * 1.2, 0.12 + R() * 0.12, [1, 0.98, 0.95], true, 0.25);
          // 孤树只种在格子靠北（远离镜头）的一半，部队起始格及其南邻不种（免得挡住主将）
          if (R() < 0.3 && tb(bi) !== 2 && !occupied(x, y) && !occupied(x, y + 1)) { const a = (0.2 + R() * 0.6) * Math.PI; put(pickW(treeKinds(tb(bi), true, cult), R()), x + Math.cos(a) * 0.72, y + Math.sin(a) * 0.72, 0.75 + R() * 0.3, leaf(bi), true); }
        }
        // 草丛（平原、山丘、森林；沙漠稀少）。第三版修订：平原格更密（7 簇、略小）
        if (t === Tn.Plain || t === Tn.Hill || t === Tn.Forest) {
          if (Tm.bridges.has(x + ',' + y)) continue;
          const arid = BIOME_ARID[bi] || 0, n = Math.round((t === Tn.Plain ? 7 : 3) * (1 - arid * 0.7));
          for (let k = 0; k < n; k++) {
            let du = (R() - 0.5) * 1.8, dv = (R() - 0.5) * 1.8;
            if (Math.abs(du) < 0.42 && Math.abs(dv) < 0.42) du = du < 0 ? -0.5 - R() * 0.3 : 0.5 + R() * 0.3;
            const g = arid > 0.5 ? [1.18, 1.05, 0.72] : arid > 0.2 ? [1.08, 1.02, 0.78] : [0.9 + R() * 0.15, 1, 0.85];
            const sc = t === Tn.Plain ? 0.6 + R() * 0.45 : 0.9 + R() * 0.6;
            if (this._gridH(x + du, y + dv) < WATER_Y + 0.12) continue;   // 不长在河里、塘里
            put('tuft', x + du, y + dv, sc, g, true, 0);
          }
        }
        // 第三版修订：平原格的点缀（离格子中心 0.5 格以外，不挡部队）——温带 / 地中海 / 热带：灌木、栅栏、野花、草垛；
        // 草原：石堆、矮灌、碎石；沙漠：矮灌、碎石、水边的椰枣。约 5% 的空格另有一处村舍（按城池文化）、水井或大车
        if (t === Tn.Plain && !Tm.bridges.has(x + ',' + y)) {
          const arid = BIOME_ARID[bi] || 0, o = M.clamp(Math.round(Tm.gj(y)), 0, nv - 1) * nu + M.clamp(Math.round(Tm.gi(x)), 0, nu - 1);
          const onRoad = Tm.Tr && G.road[o] < 0.14;
          const spot = (r0, r1) => { const a = R() * Math.PI * 2, r = r0 + R() * (r1 - r0); return [x + Math.cos(a) * r, y + Math.sin(a) * r]; };
          const dryAt = (u, v) => this._gridH(u, v) > WATER_Y + 0.16;
          const nItems = onRoad ? 1 : 1 + (R() < 0.55 ? 1 : 0) + (R() < 0.25 ? 1 : 0);
          for (let k = 0; k < nItems; k++) {
            const [u, v] = spot(0.52, 0.86);
            if (!dryAt(u, v)) continue;
            const r = R(), db = tb(bi);              // 点缀与树种同按「群落 + 文化」（泰西封的河谷按沙漠绿洲）
            if (db === 1 || db === 4) {              // 草原 / 稀树草原
              if (r < 0.06) put('cairn', u, v, 1, [1, 1, 1], true, 0.02);
              else if (r < 0.5) put('scrub', u, v, 0.8 + R() * 0.5, [1.05, 1.0, 0.8], true, 0.02);
              else if (r < 0.75) { const g = 0.92 + R() * 0.16; put('rock', u, v, 0.07 + R() * 0.07, [g, g, g * 0.97], true, 0.25); }
              else put('tuft', u, v, 1.1 + R() * 0.4, [1.1, 1.03, 0.76], true, 0);
            } else if (db === 2) {                   // 沙漠
              const wetNear = (G.river[o] < 2.2) || (G.water[o] | 0) > 0;
              if (wetNear && r < 0.18) put(cult === 'tarim' ? 'poplar' : 'palm', u, v, (cult === 'tarim' ? 0.75 : 0.55) + R() * 0.25, leaf(bi), true);   // 西域水边是胡杨
              else if (r < 0.6) put('scrub', u, v, 0.7 + R() * 0.5, [1.15, 1.05, 0.78], true, 0.02);
              else { const g = 0.95 + R() * 0.12; put('rock', u, v, 0.06 + R() * 0.07, [g * 1.05, g, g * 0.9], true, 0.25); }
            } else {                                 // 温带、地中海、热带、北方
              if (r < 0.34) put('bush', u, v, 0.75 + R() * 0.5, leaf(bi), true, 0.02);
              else if (r < 0.52 && !onRoad) put('fence', u, v, 0.9 + R() * 0.3, [1, 1, 1], true, 0);
              else if (r < 0.78) { const F = FLOWER_COLS[(R() * FLOWER_COLS.length) | 0]; put('flowers', u, v, 0.9 + R() * 0.5, F, true, 0); }
              else if (r < 0.88 && (G.farm[o] > 0.15 || bi === 0)) put('hay', u, v, 0.65 + R() * 0.3, [1, 1, 1], true, 0.02);
              else { const g = 0.9 + R() * 0.15; put('rock', u, v, 0.06 + R() * 0.07, [g, g, g * 0.97], true, 0.25); }
            }
          }
          // 偶见的较大点缀：放在格角（离中心约 0.65 格），起始时附近一格内无部队
          if (!onRoad && R() < 0.06 && !m.units.some(q => q.alive && Math.abs(q.x - x) <= 1 && Math.abs(q.y - y) <= 1)) {
            const a = (Math.floor(R() * 4) + 0.5) * Math.PI / 2, u = x + Math.cos(a) * 0.66, v = y + Math.sin(a) * 0.66;
            if (dryAt(u, v)) {
              const rr = R(), hutK = 'hut_' + (HUT_OF[this.look.culture] || HUT_OF[SG.cultureBase ? SG.cultureBase(this.look.culture) : 'han'] || 'han');
              if (rr < 0.55) put(hutK, u, v, 0.9 + R() * 0.2, [1, 1, 1], true, 0.02);
              else if (rr < 0.78) put('well', u, v, 1, [1, 1, 1], true, 0.02);
              else put('cart', u, v, 1, [1, 1, 1], true, 0.01);
            }
          }
        }
      }
      // 棋盘外：按世界地图的林地分布种树；陡坡与高处散布石块
      const hw = (u, v) => this._gridH(u, v);
      for (let j = 0; j < nv; j++) for (let i = 0; i < nu; i++) {
        const u = G.u0 + i * G.step + (R() - 0.5) * 0.45, v = G.v0 + j * G.step + (R() - 0.5) * 0.45;
        if (u > -1.0 && u < W && v > -1.0 && v < H) continue;
        const o = j * nu + i, h = hw(u, v);
        if (h < WATER_Y + 0.18) continue;
        const bi = G.biome[o] | 0, f = G.forest[o];
        const sl = G.slope[o] || 0;
        const dOut = Math.max(-0.5 - u, u - (W - 0.5), -0.5 - v, v - (H - 0.5));
        const p = (f > 0.25 ? f * 0.7 : (BIOME_ARID[bi] > 0.5 ? 0.004 : 0.03)) * (dOut > 8 ? 0.45 : 1);
        // 近景模板只给镜头一侧（南边，v < 0）3 格内的树：北、东、西三边离镜头远，用远景模板（森林大城少 3 至 6 万三角形）
        const hiT = dOut < 3 && v < 0;
        if (R() < p && sl < 0.5) put(pickW(treeKinds(tb(bi), hiT, cult), R()), u, v, 0.9 + R() * 0.55, leaf(bi), false, undefined, hiT);
        else if ((sl > 0.32 || h > BASE_Y + 1.6) && R() < 0.035) { const g = 0.9 + R() * 0.2; put('rock', u, v, 0.25 + R() * 0.35, [g, g * 0.98, g * 0.95], false, 0.3); }
        else if (Math.max(Math.abs(u - (W - 1) / 2) - W / 2, Math.abs(v - (H - 1) / 2) - H / 2) < 3 && R() < 0.5 * (1 - BIOME_ARID[bi] * 0.7)) put('tuft', u, v, 1 + R() * 0.5, BIOME_ARID[bi] > 0.3 ? [1.12, 1.04, 0.74] : [0.92, 1, 0.86], false, 0);
      }
      if (lp) {
        this._buildPropsLP(lists, R);
        const Tr0 = Tm.Tr;
        if (Tr0 && Tr0.notes && Tr0.notes.indexOf('landing') >= 0) this._buildBoats();
        return;
      }
      // 实例化网格（棋盘内的树用带抖动淡出的材质：挡在部队与镜头之间时淡去，见 _updatePropFade）
      const mat = SG.Gfx.lowPoly();
      const fadeMat = this._propFadeMat = propFadeMaterial();
      this._fade = { items: [], byTile: new Map() };
      const q = new THREE.Quaternion(), sv = new THREE.Vector3(), pv = new THREE.Vector3(), Y = new THREE.Vector3(0, 1, 0), mm = new THREE.Matrix4(), cc = new THREE.Color();
      let tris = 0;
      this._props = [];
      for (const kind of Object.keys(lists)) {
        for (const near of [true, false]) {
          const L = lists[kind].filter(e => e.near === near);
          if (!L.length) continue;
          const tpl = propTemplate(kind);
          const fade = near && !SMALL_PROP[kind];
          let geo = tpl;
          if (fade) {
            geo = new THREE.BufferGeometry();
            for (const k of Object.keys(tpl.attributes)) geo.setAttribute(k, tpl.attributes[k]);
            if (tpl.index) geo.setIndex(tpl.index);
            const fa = new THREE.InstancedBufferAttribute(new Float32Array(L.length), 1);
            if (fa.setUsage) fa.setUsage(THREE.DynamicDrawUsage);
            geo.setAttribute('aFade', fa);
            for (let k = 0; k < L.length; k++) {
              const e = L[k], it = { attr: fa, k, x: e.x, z: e.z, s: e.s * e.ay, t: 0, cur: 0 };
              this._fade.items.push(it);
              const key = Math.round(e.u) + ',' + Math.round(e.v);
              (this._fade.byTile.get(key) || this._fade.byTile.set(key, []).get(key)).push(it);
            }
          }
          const im = new THREE.InstancedMesh(geo, fade ? fadeMat : mat, L.length);
          im.name = 'Props_' + kind + (near ? '' : '_far');
          if (fade) im.renderOrder = 5;
          for (let k = 0; k < L.length; k++) {
            const e = L[k];
            q.setFromAxisAngle(Y, e.yaw);
            mm.compose(pv.set(e.x, e.y, e.z), q, sv.set(e.s * e.ax, e.s * e.ay * (kind === 'rock' ? 0.8 + (k % 3) * 0.15 : 1), e.s * e.ax));
            im.setMatrixAt(k, mm);
            im.setColorAt(k, cc.setRGB(e.c[0], e.c[1], e.c[2]));
          }
          im.instanceMatrix.needsUpdate = true;
          if (im.instanceColor) im.instanceColor.needsUpdate = true;
          im.castShadow = kind !== 'tuft' && kind !== 'flowers' && near;
          im.receiveShadow = kind !== 'tuft' && kind !== 'flowers';
          im.computeBoundingSphere && im.computeBoundingSphere();
          this.group.add(im);
          this._props.push(im);
          tris += (geo.attributes.position.count / 3) * L.length;
          if (kind === 'rock') this.stats.rocks += L.length; else if (kind === 'tuft') this.stats.tufts += L.length; else if (SMALL_PROP[kind] || /^hut_|well|cart/.test(kind)) this.stats.dress = (this.stats.dress || 0) + L.length; else this.stats.trees += L.length;
        }
      }
      this.stats.propTris = tris;
      // 渡海登陆：布阵边外的海面上泊着攻方的船
      const Tr = Tm.Tr;
      if (Tr && Tr.notes && Tr.notes.indexOf('landing') >= 0) this._buildBoats();
    }
    // 修订：LP 版的栅栏 / 石垣只立在田边、路边（世界地图的农田或道路附近），顺着格边（格内、离格边约 0.08 格）摆放，
    // 不再以随机角度散落在草地上（像一地垃圾）；按位置哈希约留一半。只改摆放，_buildProps 的随机数序列不变
    _lpFenceAt(e, gIdx) {
      const m = this.model, Tm = this.ter, G = Tm.G, Tn = TR();
      if (phash(e.x, e.z, 41) > 0.5) return null;
      const tx = Math.round(e.u), ty = Math.round(e.v);
      if (tx < 0 || ty < 0 || tx >= m.W || ty >= m.H || m.map[tx][ty] !== Tn.Plain || Tm.inFort(tx, ty) || Tm.bridges.has(tx + ',' + ty)) return null;
      const o = gIdx(tx, ty), farm = G.farm[o] || 0, road = Tm.Tr ? G.road[o] : 99;
      if (!(farm > 0.2 || road < 0.4)) return null;
      const du = e.u - tx, dv = e.v - ty, alongV = Math.abs(du) >= Math.abs(dv);
      const u = alongV ? tx + (du < 0 ? -0.42 : 0.42) : tx + M.clamp(du, -0.2, 0.2);
      const v = alongV ? ty + M.clamp(dv, -0.2, 0.2) : ty + (dv < 0 ? -0.42 : 0.42);
      if (this._gridH(u, v) < WATER_Y + 0.16) return null;
      return Object.assign({}, e, { u, v, x: ORIGIN.x + (u + 0.5) * T, z: -(ORIGIN.z + (v + 0.5) * T), y: this._gridH(u, v), yaw: alongV ? Math.PI / 2 : 0 });
    }
    // 第四版：景物用 SG.LPProps 的配方（每个配方 / 种子 / 远近一个实例化网格 + 实例化墨线），草丛等合成一个网格
    _buildPropsLP(lists, R) {
      const LP = SG.LP, Props = SG.LPProps, Tm = this.ter, G = Tm.G, m = this.model, Tn = TR(), W = m.W, H = m.H, nu = G.nu, nv = G.nv;
      const cult = (this.look && this.look.culture) || 'han', base = SG.cultureBase ? SG.cultureBase(cult) : cult;
      const gIdx = (u, v) => M.clamp(Math.round(Tm.gj(v)), 0, nv - 1) * nu + M.clamp(Math.round(Tm.gi(u)), 0, nu - 1);
      const ctx = {
        fence: LP_FENCE[cult] || LP_FENCE[base] || 'fence_wood',
        south: cult === 'seasia' || cult === 'nanman' || cult === 'yi' || base === 'seasia',
        wet: e => { const o = gIdx(e.u, e.v); return G.river[o] < 1.6 || (G.water[o] | 0) > 0; },
      };
      // 修订：近景树按棋盘上的方块分组（BattleView.PROP_CHUNK_TILES 格见方；棋盘外镜头一侧的近景树自成几块）
      const CT = Math.max(2, BattleView.PROP_CHUNK_TILES || 5);
      const chunkXY = e => [Math.floor((e.u + 0.5) / CT), Math.floor((e.v + 0.5) / CT)];
      ctx.chunk = e => chunkXY(e).join(',');
      ctx.chunkSeed = e => { const c = chunkXY(e); return (((c[0] + c[1]) % 2) + 2) % 2; };
      const groups = new Map(), grass = [];
      for (const kind of Object.keys(lists)) for (let e of lists[kind]) {
        if (kind === 'tuft') { grass.push({ t: 'tuft', x: e.x, y: e.y, z: e.z, k: e.s, bi: e.bi, dim: e.near ? 1 : 0.94 }); continue; }
        if (kind === 'flowers') { grass.push({ t: 'flowers', x: e.x, y: e.y, z: e.z, k: e.s, c: e.c }); continue; }
        if (kind === 'fence') { e = this._lpFenceAt(e, gIdx); if (!e) continue; }
        const sp = lpPropSpec(kind, e, ctx);
        if (!sp) continue;
        // 修订：淡出与否不再分组（淡出按实例处理），同一配方少一组绘制
        const key = sp.name + '|' + sp.seed + '|' + sp.lod + '|' + (sp.opts ? JSON.stringify(sp.opts) : '') + '|' + (sp.ink ? 1 : 0) + (sp.cast ? 1 : 0) + '|' + (sp.inkW || 0);
        let g = groups.get(key);
        if (!g) groups.set(key, (g = { sp, items: [] }));
        g.items.push({ e, sp });
        // 树下的落叶（棋盘内）
        if (sp.tree && e.near) for (let k = 0; k < 4; k++) { const a = R() * Math.PI * 2, d = 0.12 + R() * 0.5; const lx = e.x + Math.cos(a) * d, lz = e.z + Math.sin(a) * d; grass.push({ t: 'leaf', x: lx, y: this.groundY(lx, lz), z: lz, k: 1 }); }
      }
      // 平原格上的小花点、道路上的卵石
      for (let x = 0; x < W; x++) for (let y = 0; y < H; y++) {
        const t = m.map[x][y];
        if (Tm.inFort(x, y) || t !== Tn.Plain) continue;
        const o = gIdx(x, y), arid = BIOME_ARID[G.biome[o] | 0] || 0, road = Tm.Tr && G.road[o] < 0.14;
        const n = road ? 0 : Math.round(5 * (1 - arid * 0.8));
        for (let k = 0; k < n; k++) {
          const u = x + (R() - 0.5) * 1.9, v = y + (R() - 0.5) * 1.9;
          const wx = ORIGIN.x + (u + 0.5) * T, wz = -(ORIGIN.z + (v + 0.5) * T), gy = this._gridH(u, v);
          if (gy < WATER_Y + 0.14) continue;
          grass.push({ t: 'speck', x: wx, y: gy, z: wz, k: 1 });
        }
        if (road) for (let k = 0; k < 6; k++) {
          const u = x + (R() - 0.5) * 1.6, v = y + (R() - 0.5) * 1.6;
          const wx = ORIGIN.x + (u + 0.5) * T, wz = -(ORIGIN.z + (v + 0.5) * T), gy = this._gridH(u, v);
          if (gy < WATER_Y + 0.14) continue;
          grass.push({ t: 'pebble', x: wx, y: gy, z: wz, k: 1 });
        }
      }
      this._fade = { items: [], byTile: new Map() };
      this._props = [];
      this._lodSets = []; this._farGroups = []; this._treeFar = null; this._farCull = []; this._farCullCam = null;
      // 第三轮：树用挖洞材质（被挡的部队周围开洞，见 _updatePropFade）；派生不了时退回整棵溶解的副本
      this._cut = BattleView.TREE_CUTOUTS !== false && !!(cutMaterial(LP.material({})) && cutMaterial(LP.outlineMaterial({ width: INK_W })) && cutMaterial(LP.outlineMaterial({ width: INK_FAR_W })));
      const cutTree = x => { if (x && this._cut && !useCut(x)) this._cut = false; return x; };
      const q = new THREE.Quaternion(), sv = new THREE.Vector3(), pv = new THREE.Vector3(), Y = new THREE.Vector3(0, 1, 0), mm = new THREE.Matrix4();
      const chunkRec = new Map();
      const addMesh = x => {
        if (!x) return;
        LP.commitInstances(x, x.count || x.instanceMatrix.count);
        x.computeBoundingSphere();
        if (x.userData.outline) x.userData.outline.boundingSphere = x.boundingSphere;
        this.group.add(x);
        this._props.push(x);
      };
      let tris = 0;
      // 第三轮修订：近景树两个变体（种子）共用一套远景网格（远景只用种子 0 的模型：远看分不出变体），每种树少三次绘制
      const farKey = sp => sp.name + '|' + (sp.opts ? JSON.stringify(sp.opts) : '') + '|' + (sp.cast ? 1 : 0);
      const farCap = new Map(), farSets = new Map();
      for (const { sp, items } of groups.values()) if (sp.tree && sp.lod === 'near') farCap.set(farKey(sp), (farCap.get(farKey(sp)) || 0) + items.length);
      for (const { sp, items } of groups.values()) {
        let gg;
        try { gg = Props.geometry(sp.name, Object.assign({ seed: sp.seed, lod: sp.lod }, sp.opts || {})); } catch (e) { console.error(e); continue; }
        for (const geo of [gg.ink, gg.plain]) if (geo) geo.userData.sharedGeometry = true;
        // 棋盘上（及镜头一侧棋盘外近处）的树另备一套远景（同一组实例变换）
        let fg = null, fs = null;
        if (sp.tree && sp.lod === 'near') {
          const fk = farKey(sp);
          fs = farSets.get(fk);
          if (fs === undefined) {
            let g0 = null;
            try { g0 = Props.geometry(sp.name, Object.assign({ seed: 0, lod: 'far' }, sp.opts || {})); } catch (e) { g0 = null; }
            if (g0 && !(g0.tris < gg.tris * 0.75)) g0 = null;
            fs = null;
            if (g0) {
              for (const geo of [g0.ink, g0.plain]) if (geo) geo.userData.sharedGeometry = true;
              const cap = farCap.get(fk) || items.length;
              const fims = [g0.ink ? cutTree(LP.instances(g0.ink, cap, { tints: true, castShadow: sp.cast, receiveShadow: true, outline: true, outlineOpts: { width: INK_FAR_W }, name: 'LP_' + sp.name + '_lod' })) : null,
                g0.plain ? cutTree(LP.instances(g0.plain, cap, { tints: true, castShadow: false, receiveShadow: true, outline: false, name: 'LP_' + sp.name + '_lodplain' })) : null];
              fs = { fg: g0, fims, fgp: { ims: fims.filter(Boolean), recs: [], dirty: false }, kf: 0 };
              this._farGroups.push(fs.fgp);
            }
            farSets.set(fk, fs);
          }
          if (fs) fg = fs.fg;
        }
        const n = items.length, tint0 = sp.tint || [1, 1, 1];
        const ink = sp.ink ? { width: sp.inkW || (sp.lod === 'far' ? INK_FAR_W : INK_W) } : null;
        const tc = sp.tree ? cutTree : (x => x);
        const mkNear = cnt => [gg.ink ? tc(LP.instances(gg.ink, cnt, { tints: true, castShadow: sp.cast, receiveShadow: true, outline: !!ink, outlineOpts: ink || undefined, name: 'LP_' + sp.name + (sp.lod === 'far' ? '_far' : '') })) : null,
          gg.plain ? tc(LP.instances(gg.plain, cnt, { tints: true, castShadow: false, receiveShadow: true, outline: false, name: 'LP_' + sp.name + '_plain' })) : null];
        const place = (it, k, ims) => {
          const e = it.e, s = sp.sc;
          q.setFromAxisAngle(Y, e.yaw);
          if (Array.isArray(s)) sv.set(s[0], s[1], s[2]); else sv.set(s, s, s);
          mm.compose(pv.set(e.x, e.y + (sp.dy || 0), e.z), q, sv);
          for (const x of ims) if (x) LP.setInstanceMatrix(x, k, mm, tint0);
          return mm.elements.slice();
        };
        const fadeItem = (it, el, refs, chunk) => {
          if (!it.sp.fade) return null;
          const f = { lp: true, refs, chunk, el, tint: sp.tint || null, gg, fg, x: it.e.x, z: it.e.z, s: lpSize(sp.name, sp.seed).h * sv.y / 1.55, t: 0, cur: 0, proxy: null, look: null };
          this._fade.items.push(f);
          const key = Math.round(it.e.u) + ',' + Math.round(it.e.v);
          (this._fade.byTile.get(key) || this._fade.byTile.set(key, []).get(key)).push(f);
          return f;
        };
        if (!fg) {
          // 不分远近：一个实例化网格（+ 墨线）。第三轮修订：棋盘外远处的树不再按方位分四组（常用的镜头距离四边都在画面里，
          // 分组只多出绘制）
          const ims = mkNear(n);
          // 第三轮：棋盘外远处的树按棵做视锥剔除（常用的镜头距离下大多在画面外：只把画面里的几十棵排进实例网格，见 _updateFarCull）
          const cull = sp.tree && sp.lod === 'far' ? { ims: ims.filter(Boolean), els: [], sph: [] } : null;
          items.forEach((it, k) => {
            const el = place(it, k, ims);
            fadeItem(it, el, [{ ims: ims.filter(Boolean), k }], null);
            if (cull) { const h = sv.y * lpSize(sp.name, sp.seed).h; cull.els.push(el); cull.sph.push(el[12], el[13] + h * 0.5, el[14], h * 0.75 + 0.2); }
          });
          for (const x of ims) if (x) { x.count = n; addMesh(x); }
          if (cull) this._farCull.push(cull);
        } else {
          // 修订：远景模型一组网格画整片（拉远看全棋盘时与原来一样少的绘制）；近景模型按棋盘上的方块（ctx.chunk，约 5 格见方）
          // 各一组网格（各有包围球，镜头外、阴影相机外的块不画）。块离镜头近于 BattleView.TREE_LOD_DIST 时显示该块的近景网格，
          // 并把该块的树在远景网格里藏起来（_updateTreeLod）。远近按块各自切换：拉近时只有眼前几块是精细的树
          const fims = fs.fims;
          // 远景网格里的树按块连续存放；有块换成近景时重新紧排（count 变小，近景块的树不再占远景网格的顶点）
          const fgp = fs.fgp;
          const byChunk = new Map();
          items.forEach((it, i) => { const ck = ctx.chunk(it.e); (byChunk.get(ck) || byChunk.set(ck, []).get(ck)).push(i); });
          let kf = fs.kf;
          for (const [ck, idx] of byChunk) {
            let rec = chunkRec.get(ck);
            if (!rec) chunkRec.set(ck, (rec = { center: new THREE.Vector3(), cn: 0, r: 0, pts: [], isFar: true, near: [], farGroups: new Set(), cast: true }));
            rec.farGroups.add(fgp);
            const nims = mkNear(idx.length);
            idx.forEach((i, kn) => {
              const it = items[i];
              const fr = { el: place(it, kf, fims), chunk: rec, f: null, idx: kf++ };
              const el = place(it, kn, nims);
              rec.center.x += it.e.x; rec.center.y += it.e.y; rec.center.z += it.e.z; rec.cn++;
              rec.pts.push(it.e.x, it.e.z, sv.y * lpSize(sp.name, sp.seed).h);
              fr.f = fadeItem(it, el, [{ ims: nims.filter(Boolean), k: kn }, { far: fr, ims: fgp.ims }], rec);
              fgp.recs.push(fr);
            });
            for (const x of nims) if (x) { x.count = idx.length; x.visible = false; x.userData.castShadow0 = x.castShadow; addMesh(x); rec.near.push(x); }
          }
          fs.kf = kf;
        }
        tris += gg.tris * n;
        if (sp.tree) this.stats.trees += n; else if (/^rock|boulder/.test(sp.name)) this.stats.rocks += n; else this.stats.dress = (this.stats.dress || 0) + n;
      }
      // 共用的远景网格：所有变体的树都放好以后再提交
      for (const fs of farSets.values()) if (fs) for (const x of fs.fims) if (x) { x.count = fs.kf; addMesh(x); }
      for (const rec of chunkRec.values()) {
        rec.center.multiplyScalar(1 / Math.max(1, rec.cn));
        for (let i = 0; i < rec.pts.length; i += 3) rec.r = Math.max(rec.r, Math.hypot(rec.pts[i] - rec.center.x, rec.pts[i + 1] - rec.center.z) + rec.pts[i + 2]);
        rec.pts = null;
        this._lodSets.push(rec);
      }
      this.stats.tufts = grass.filter(g => g.t === 'tuft').length;
      // 修订：草丛按棋盘的四个象限分成四个网格（各有包围球，拉近时镜头外的不画）
      const gcx = ORIGIN.x + W / 2 * T, gcz = -(ORIGIN.z + H / 2 * T), gq = [[], [], [], []];
      for (const g of grass) gq[(g.x < gcx ? 0 : 1) + (g.z < gcz ? 0 : 2)].push(g);
      this.stats.grassTris = 0;
      gq.forEach((list, qi) => {
        const gm = list.length ? grassMeshLP(list, ((Tm.seed ^ 0x2545f491) + qi * 7919) | 0) : null;
        if (gm) { gm.name = 'Grass'; this.group.add(gm); tris += gm.geometry.userData.tris || 0; this.stats.grassTris += gm.geometry.userData.tris || 0; }
      });
      this.stats.propTris = tris;
      this.stats.propSets = groups.size;
    }
    // 挡住部队的树：把它的实例藏起来，换成一棵单独的、按 look.fade 抖动溶解的副本（几何体与材质共用，不重编译）。
    // 第三轮修订：只在挖洞材质不可用时（this._cut 为假）才走这条路；平常是在部队周围挖洞（_updatePropFade / cutBefore）
    _lpFade(it) {
      const on = it.cur > 0.01;
      if (on && !it.proxy) {
        // 淡出的副本：用它所在块当前的远近模型，不描墨线（抖动溶解的墨线只是一片噪点，还多一次绘制）
        const LP = SG.LP, g = new THREE.Group(), src = (!it.chunk || it.chunk.isFar) && it.fg ? it.fg : it.gg;
        if (src.ink) g.add(LP.mesh(src.ink, { outline: false, castShadow: false }));
        if (src.plain) g.add(LP.mesh(src.plain, { outline: false, castShadow: false }));
        g.matrixAutoUpdate = false;
        g.matrix.fromArray(it.el);
        g.name = 'FadedTree';
        it.look = LP.setLook(g, { fade: 1, tint: it.tint || undefined });
        this.group.add(g);
        it.proxy = g;
        for (const r of it.refs) {
          const k = r.far ? r.far.idx : r.k;
          if (k >= 0) for (const x of r.ims) { x.setMatrixAt(k, HIDE_M); x.instanceMatrix.needsUpdate = true; }
        }
      }
      // 修订：溶到 35%（Lanternvale 的 OccluderFadeAlpha），树冠的体量还在，抖动的点子不那么碎
      if (it.proxy) it.look.fade = 1 - 0.65 * M.clamp01(it.cur);
      if (!on && it.proxy) {
        this.group.remove(it.proxy);
        it.proxy = null; it.look = null;
        _fm.fromArray(it.el);
        for (const r of it.refs) {
          const k = r.far ? r.far.idx : r.k;     // 近景块里的树不在远景网格里（idx = -1）
          if (k >= 0) for (const x of r.ims) { x.setMatrixAt(k, _fm); x.instanceMatrix.needsUpdate = true; }
        }
      }
    }
    // 第三轮：棋盘外远处的树逐棵视锥剔除——镜头动了才重排（画面里的排在前面，count = 画面里的棵数；网格的包围球仍是整片）
    _updateFarCull() {
      const L = this._farCull, cam = SG.Gfx && SG.Gfx.camera;
      if (!L || !L.length || !cam) return;
      cam.updateMatrixWorld();
      _lodM.multiplyMatrices(cam.projectionMatrix, cam.matrixWorldInverse);
      if (this._farCullCam && this._farCullCam.equals(_lodM)) return;
      (this._farCullCam || (this._farCullCam = new THREE.Matrix4())).copy(_lodM);
      _lodF.setFromProjectionMatrix(_lodM);
      for (const c of L) {
        let k = 0;
        for (let i = 0, n = c.els.length; i < n; i++) {
          _lodS.center.set(c.sph[i * 4], c.sph[i * 4 + 1], c.sph[i * 4 + 2]); _lodS.radius = c.sph[i * 4 + 3];
          if (!_lodF.intersectsSphere(_lodS)) continue;
          _fm.fromArray(c.els[i]);
          for (const x of c.ims) x.setMatrixAt(k, _fm);
          k++;
        }
        for (const x of c.ims) { x.count = k; if (x.userData.outline) x.userData.outline.count = k; x.instanceMatrix.needsUpdate = true; }
      }
    }
    // 棋盘上的树（修订：按块）：块中心到镜头的距离小于 BattleView.TREE_LOD_DIST 时该块用近景网格，远景网格只放其余块的树
    // （有块切换时重新紧排），否则用远景（带回差，不来回跳）。近景块不在镜头视野（含约 3 单位的影子余量）里时不投影
    _updateTreeLod() {
      const sets = this._lodSets, cam = SG.Gfx && SG.Gfx.camera;
      if (!sets || !sets.length || !cam) return;
      const force = BattleView.TREE_LOD, lim = BattleView.TREE_LOD_DIST, cp = cam.position;
      let nFar = 0, changed = false;
      for (const c of sets) {
        let far;
        if (force === 'near' || force === 'far') far = force === 'far';
        else { const d = cp.distanceTo(c.center); far = c.isFar ? d > lim - 2 : d > lim + 2; }
        if (far) nFar++;
        if (far === c.isFar) continue;
        c.isFar = far; changed = true;
        for (const x of c.near) x.visible = !far;
        for (const g of c.farGroups) g.dirty = true;
      }
      if (changed) {
        for (const g of this._farGroups) {
          if (!g.dirty) continue;
          g.dirty = false;
          let k = 0;
          for (const r of g.recs) {
            if (!r.chunk.isFar) { r.idx = -1; continue; }
            r.idx = k;
            const hide = r.f && r.f.proxy;
            if (!hide) _fm.fromArray(r.el);
            for (const x of g.ims) x.setMatrixAt(k, hide ? HIDE_M : _fm);
            k++;
          }
          for (const x of g.ims) { x.count = k; if (x.userData.outline) x.userData.outline.count = k; x.instanceMatrix.needsUpdate = true; }
        }
        // 正在淡出的树换成它所在块的新模型
        for (const it of this._fade.items) if (it.proxy && it.chunk) { this.group.remove(it.proxy); it.proxy = null; it.look = null; this._lpFade(it); }
      }
      this._treeFar = nFar === sets.length;
      // 近景块：视野外（含影子余量）的不投影
      cam.updateMatrixWorld();
      _lodF.setFromProjectionMatrix(_lodM.multiplyMatrices(cam.projectionMatrix, cam.matrixWorldInverse));
      for (const c of sets) {
        if (c.isFar) continue;
        _lodS.center.copy(c.center); _lodS.radius = c.r + 3;
        const cast = _lodF.intersectsSphere(_lodS);
        if (cast === c.cast) continue;
        c.cast = cast;
        for (const x of c.near) if (x.userData.castShadow0 !== false) x.castShadow = cast && x.userData.castShadow0;
      }
    }
    // 第四版：城池、桥、帅旗（LP）
    _buildFortLP(look, defCol) {
      const LP = SG.LP, Tm = this.ter;
      let fort;
      try { fort = buildFortLP(this, Tm, look, cssHex(defCol)); } catch (e) { console.error(e); fort = null; }
      if (fort && fort.geo) {
        const fm = LP.mesh(fort.geo, { castShadow: true });
        fm.name = 'Fort';
        this.group.add(fm);
      }
      if (fort && fort.plain) {
        const pm = LP.mesh(fort.plain, { outline: false, castShadow: false });
        pm.name = 'FortPaving';
        this.group.add(pm);
      }
      this.stats.fortTris = fort ? fort.tris : 0;
      // 夜里城门与本阵的灯（主场景氛围的灯池；构建时加好，之后不重编译着色器；释放时移除）
      this._lamps = [];
      if (fort && fort.lamps && SG.Gfx && SG.Gfx.addLamp && SG.Gfx.mood) {
        for (const p of fort.lamps) { const L = SG.Gfx.addLamp(p, { color: '#ffd08a', intensity: 0.75, range: 3.2, nightOnly: true, haloSize: 0.8, flicker: 0.06 }); if (L) this._lamps.push(L); }
      }
      // 帅旗（守方势力色）
      if (fort && fort.flag) {
        try {
          const fl = SG.LPProps.make('banner', { color: cssHex(defCol), seed: 0 });
          const k = 2.4 / Math.max(0.1, lpSize('banner', 0).h);
          fl.position.set(fort.flag.x, fort.flag.y, fort.flag.z);
          fl.scale.setScalar(k);
          fl.traverse(o => { if (o.geometry) o.geometry.userData.sharedGeometry = true; });
          fl.name = 'FortBanner';
          this.group.add(fl);
        } catch (e) { console.error(e); }
      }
      // 桥（木桥 / 石桥按文化）
      if (Tm.bridges.size) {
        const fam = fortStyleLP(look.style).fam, stone = fam === 'roman' || fam === 'mud';
        const Mb = new LP.MeshBuilder(91);
        Mb.jitter = 0.06;
        for (const b of Tm.bridges.values()) bridgeInto(Mb, b, stone);
        if (!Mb.isEmpty) { const bm = LP.mesh(Mb.toGeometry(), { castShadow: true }); bm.name = 'Bridges'; this.group.add(bm); }
      }
    }
    // 挡在部队与镜头之间的树淡去（地面投影上：部队往镜头方向、树高 / tan(俯角) 以内，横向约 0.8 格以内）
    _updatePropFade(dt) {
      const F = this._fade, cam = SG.Gfx && SG.Gfx.camera;
      if (!F || !F.items.length || !cam) return;
      for (const it of F.items) it.t = 0;
      // 修订：走廊只作粗筛（树在部队与镜头之间、横向约 0.4 格以内），再在屏幕上确认树冠真的盖住部队（投影框相交的比例），
      // 免得部队旁边、河边的树也一起溶掉
      const me = cam.matrixWorld.elements, rx = me[0], rz = me[2];
      const ndc = (x, y, z, o) => { _fv.set(x, y, z).project(cam); o[0] = _fv.x; o[1] = _fv.y; o[2] = _fv.z; return o; };
      const ua = [0, 0, 0], ub = [0, 0, 0], uc = [0, 0, 0], ta = [0, 0, 0], tb = [0, 0, 0], tc = [0, 0, 0];
      for (const v of this.visuals.values()) {
        if (!v.group.visible || (v.u && v.u.alive === false)) continue;
        const p = v.group.position;
        let cx = cam.position.x - p.x, cz = cam.position.z - p.z;
        const l = Math.hypot(cx, cz) || 1; cx /= l; cz /= l;
        // 镜头俯角越大，挡得住部队的树离部队越近：树高 / tan(俯角)
        const tanEl = Math.max(0.3, (cam.position.y - p.y) / l);
        const ti = Math.round((p.x - ORIGIN.x) / T - 0.5), tj = Math.round((-p.z - ORIGIN.z) / T - 0.5);
        let uRect = null;
        v._cutT = 0;
        for (let dj = -3; dj <= 3; dj++) for (let di = -3; di <= 3; di++) {
          const L = F.byTile.get((ti + di) + ',' + (tj + dj));
          if (!L) continue;
          for (const it of L) {
            const ox = it.x - p.x, oz = it.z - p.z, along = ox * cx + oz * cz, perp = Math.abs(ox * cz - oz * cx);
            // 修订（第三轮）：紧挨部队身旁的树（along 略小于 0）也可能盖住部队——屏幕上的重叠与深度测试会把不挡的树筛掉
            if (along < -0.1 * T || along > (2.3 * it.s + 0.5) / tanEl + 0.3 * T) continue;
            const w = 0.4 * T * Math.max(0.8, it.s) + 0.2;
            if (perp >= w) continue;
            if (!uRect) {
              // 部队的屏幕框（宽约 1.4、高约 1.9 单位：骑马的主将与旗）；深度取半身高处
              ndc(p.x - rx * 0.7, p.y, p.z - rz * 0.7, ua); ndc(p.x + rx * 0.7, p.y, p.z + rz * 0.7, ub); ndc(p.x, p.y + 1.9, p.z, uc);
              uRect = { x0: Math.min(ua[0], ub[0]), x1: Math.max(ua[0], ub[0]), y0: Math.min(ua[1], ub[1]), y1: uc[1], d: ndc(p.x, p.y + 0.8, p.z, tc)[2] };
              uRect.area = Math.max(1e-6, (uRect.x1 - uRect.x0) * (uRect.y1 - uRect.y0));
            }
            // 树的屏幕框（树冠：离地 35% 到树顶，宽约树高的 0.8）
            const gy = it.el ? it.el[13] : this.groundY(it.x, it.z), h = it.s * 1.55, hw = 0.4 * h;
            // 深度：树冠朝镜头一侧的点（比树干更靠近镜头）与部队半身处比较
            if (ndc(it.x + cx * 0.4 * h, gy + 0.6 * h, it.z + cz * 0.4 * h, tc)[2] >= uRect.d) continue;
            ndc(it.x - rx * hw, gy + h * 0.35, it.z - rz * hw, ta); ndc(it.x + rx * hw, gy + h * 0.35, it.z + rz * hw, tb); ndc(it.x, gy + h, it.z, tc);
            const ox0 = Math.max(Math.min(ta[0], tb[0]), uRect.x0), ox1 = Math.min(Math.max(ta[0], tb[0]), uRect.x1);
            const oy0 = Math.max(Math.min(ta[1], tb[1]), uRect.y0), oy1 = Math.min(tc[1], uRect.y1);
            if (ox1 <= ox0 || oy1 <= oy0) continue;
            const ov = (ox1 - ox0) * (oy1 - oy0) / uRect.area;
            if (ov > 0.04) {
              const a = M.clamp01((ov - 0.04) / 0.12);
              it.t = Math.max(it.t, a);
              v._cutT = Math.max(v._cutT, a);      // 第三轮：这个部队周围要挖洞
            }
          }
        }
      }
      const k = Math.min(1, (dt || 0.016) * 7);
      // 第三轮：挖洞——被挡住的部队（洞随 _cutCur 张开 / 合上），最多 CUT_N 个，按挡得多的排；树本身不再淡出
      if (this._cut) {
        const L = [];
        for (const v of this.visuals.values()) {
          const tgt = v.group.visible && !(v.u && v.u.alive === false) ? v._cutT || 0 : 0;
          v._cutCur = (v._cutCur || 0) + (tgt - (v._cutCur || 0)) * k;
          if (Math.abs(tgt - v._cutCur) < 0.01) v._cutCur = tgt;
          if (v._cutCur > 0.01) L.push(v);
        }
        L.sort((a, b) => b._cutCur - a._cutCur);
        const list = CUT_STATE.list = [];
        for (let i = 0; i < Math.min(CUT_N, L.length); i++) {
          const v = L[i], e = M.clamp01(v._cutCur), p = v.group.position;
          list.push({ x: p.x, y: p.y + CUT_Y, z: p.z, w: CUT_R * e * e * (3 - 2 * e) });
        }
        CUT_STATE.gen++;
      }
      let dirty = null;
      for (const it of F.items) {
        if (Math.abs(it.t - it.cur) < 0.004) continue;
        it.cur += (it.t - it.cur) * k;
        if (Math.abs(it.t - it.cur) < 0.01) it.cur = it.t;
        if (it.lp) { if (!this._cut) this._lpFade(it); continue; }
        it.attr.array[it.k] = it.cur;
        (dirty || (dirty = new Set())).add(it.attr);
      }
      if (dirty) for (const a of dirty) a.needsUpdate = true;
    }
    _buildBoats() {
      const m = this.model, mb = new SG.MeshBuilder(), col = this._factionColor((m.setup || {}).attacker);
      const rnd = SG.SeededRandom((this.ter.seed ^ 0x51ed270b) | 0);
      let n = 0;
      const placed = [];
      for (let k = 0; k < 18 && n < 4; k++) {
        const u = -1.5 - rnd.nextDouble() * 2.8, v = (m.H >> 1) + (rnd.nextDouble() - 0.5) * (m.H - 3);
        if (this.ter.heightAt(u, v) > WATER_Y - 0.25 || placed.some(q => Math.hypot(q.u - u, q.v - v) < 1.6)) continue;
        placed.push({ u, v });
        const yaw = (rnd.nextDouble() - 0.5) * 0.9 + (v < m.H / 2 ? 0.12 : -0.12);
        const sc = 0.95 + rnd.nextDouble() * 0.2;
        if (this._lp) {
          // 第四版：攻方文化的船（汉 / 东方：沙船；罗马：桨帆船，帆为势力色；西亚、天竺、非洲：三角帆船）
          try {
            const c0 = this.cultureOf(0), b0 = SG.cultureBase ? SG.cultureBase(c0) : c0;
            const name = c0 === 'roman' ? 'galley' : /^(persia|arab|africa|moor|india|kushan)$/.test(c0) || /^(persia|arab|africa|india)$/.test(b0) ? 'dhow' : 'junk';
            const boat = SG.LPProps.make(name, { color: cssHex(col), seed: n });
            const len = Math.max(0.1, lpSize(name, n & 3).w);
            boat.scale.setScalar(2.5 * sc / len);
            boat.position.set(ORIGIN.x + (u + 0.5) * T, WATER_Y - 0.02, -(ORIGIN.z + (v + 0.5) * T));
            boat.rotation.y = -yaw;
            boat.traverse(o => { if (o.geometry) o.geometry.userData.sharedGeometry = true; });
            boat.name = 'Boat';
            this.group.add(boat);
          } catch (e) { console.error(e); }
        } else boatInto(mb, ORIGIN.x + (u + 0.5) * T, WATER_Y, ORIGIN.z + (v + 0.5) * T, yaw, sc, col, rnd);
        n++;
      }
      if (this._lp) { this.stats.boats = n; return; }
      if (!n) return;
      const boats = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      boats.name = 'Boats';
      this.group.add(boats);
      this.stats.boats = n;
    }
    // 桥：道路过河处（规则上是平原格）
    _buildBridges(mb) {
      const Tm = this.ter;
      for (const b of Tm.bridges.values()) {
        const cx = ORIGIN.x + b.x * T + T / 2, cz = ORIGIN.z + b.y * T + T / 2, y = WATER_Y + 0.34;
        const ax = b.axis === 'x';
        const L = T + 0.6, Wd = 1.0;
        const sx = ax ? L : Wd, sz = ax ? Wd : L;
        mb.box(V(cx, y - 0.06, cz), V(sx, 0.12, sz), C(0.62, 0.6, 0.55), 0.04);
        for (const s of [-1, 1]) {
          mb.box(V(cx + (ax ? 0 : s * (Wd / 2 - 0.05)), y + 0.1, cz + (ax ? s * (Wd / 2 - 0.05) : 0)), V(ax ? L : 0.1, 0.2, ax ? 0.1 : L), C(0.56, 0.54, 0.5), 0.06);
          // 桥墩
          mb.box(V(cx + (ax ? s * 0.55 : 0), WATER_Y - 0.3, cz + (ax ? 0 : s * 0.55)), V(ax ? 0.3 : Wd * 0.9, 0.62, ax ? Wd * 0.9 : 0.3), C(0.5, 0.48, 0.44), 0.02);
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
    // 贴合地形的高亮格（每格一个 5×5 的小网格，缓存）
    _hlGeoAt(x, y) {
      const key = x + ',' + y;
      let g = this.hlGeos.get(key);
      if (g) return g;
      const n = 4, pos = [], uv = [], idx = [];
      const c = this.tile(x, y);
      const onWater = c.y <= WADE_Y + 0.001;
      for (let j = 0; j <= n; j++) for (let i = 0; i <= n; i++) {
        const lx = -1 + 2 * i / n, lz = -1 + 2 * j / n;
        const wx = c.x + lx * T * 0.5, wz = c.z - lz * T * 0.5;
        const gy = onWater ? WATER_Y : this.groundY(wx, wz);
        pos.push(lx * T * 0.5, Math.max(gy, WATER_Y) - c.y + 0.07, -lz * T * 0.5);
        uv.push(i / n, j / n);
      }
      for (let j = 0; j < n; j++) for (let i = 0; i < n; i++) {
        const a = j * (n + 1) + i, b = a + 1, cc = a + n + 1, d = cc + 1;
        idx.push(a, b, d, a, d, cc);
      }
      g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
      g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
      g.setIndex(idx);
      g.computeBoundingSphere();
      this.hlGeos.set(key, g);
      return g;
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
        const hl = new THREE.Mesh(this._hlGeoAt(t.x, t.y), mat);
        hl.position.copy(this.tile(t.x, t.y));
        hl.renderOrder = 2;
        this.group.add(hl);
        this.highlights.push(hl);
      }
    }
    // 格子周围（半径 r 世界单位）的最高地表：光标、光环放在它之上，不被坡面挡住
    _ringY(x, y, r) {
      const c = this.tile(x, y);
      let h = c.y;
      for (let k = 0; k < 8; k++) { const a = k * Math.PI / 4; h = Math.max(h, this.groundY(c.x + Math.cos(a) * r, c.z + Math.sin(a) * r)); }
      return h - c.y;
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
        this.cursor.position.y += 0.08 + Math.min(0.5, this._ringY(u.x, u.y, T * 0.55));
        this.cursorGlow.position.copy(this.cursor.position);
        this.cursorGlow.position.y -= 0.01;
      }
    }

    _inBounds(x, y) { return x >= 0 && y >= 0 && x < this.model.W && y < this.model.H; }

    // 屏幕点 → 格子：沿视线步进到地表（起伏的地形上点到的就是看到的那一格）
    // 点选用的表面：地表、水面（点到的是水面而不是河底）、城墙格的墙顶
    _pickY(wx, wz) {
      const g = Math.max(this.groundY(wx, wz), WATER_Y), m = this.model;
      const xi = Math.round((wx - ORIGIN.x) / T - 0.5), yi = Math.round((-wz - ORIGIN.z) / T - 0.5);
      if (this._tileY && xi >= 0 && yi >= 0 && xi < m.W && yi < m.H && m.map[xi][yi] === TR().Wall) return Math.max(g, this._tileY[yi * m.W + xi]);
      return g;
    }
    tileFromScreen(screenX, screenY) {
      if (!SG.Gfx || !SG.Gfx.camera) return null;
      const ray = SG.Gfx.screenRay(screenX, screenY);
      const o = ray.origin, d = ray.direction;
      if (d.y > -1e-4) return null;
      const top = 5.5, t0 = Math.max(0, (o.y - top) / -d.y), t1 = (o.y - (WADE_Y - 0.5)) / -d.y;
      const p = new THREE.Vector3();
      let prev = t0, hit = null;
      const step = 0.35;
      for (let t = t0; t <= t1; t += step) {
        p.copy(o).addScaledVector(d, t);
        if (p.y <= this._pickY(p.x, p.z)) {
          let a = prev, b = t;
          for (let k = 0; k < 10; k++) { const mid = (a + b) / 2; p.copy(o).addScaledVector(d, mid); if (p.y <= this._pickY(p.x, p.z)) b = mid; else a = mid; }
          hit = p.copy(o).addScaledVector(d, b);
          break;
        }
        prev = t;
      }
      if (!hit) return null;
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
      if (this.crowd) this.crowd.setTime(time);
      for (const v of this.visuals.values()) v.update(dt, time);
      this._applyMood();
      this._updateBlobs();
      this._layoutLabels(dt);
      this._updateBurning();
      this._updateEmitters(dt);
      this._updateTreeLod();
      this._updateFarCull();
      this._updatePropFade(dt);
      this._updateLights(dt, time);
      const scale = this._pointScale();
      this.glow.update(dt, scale);
      this.flame.update(dt, scale);
      this.heat.update(dt, scale);
      this.dust.update(dt, scale);
      this._spUpdate(dt);         // 必杀技：镜头震动、花瓣粒子
    }

    // 第四版：部队脚下的圆影（主将与战马一团、每名士兵一团；随部队移动、溃散后随模型隐去）
    _updateBlobs() {
      const B = this._blobs;
      if (!B) return;
      let n = 0;
      const cap = B.capacity;
      for (const v of this.visuals.values()) {
        if (!v.group.visible || n >= cap) continue;
        const p = v.group.position;
        if (v.fig || v._mesh) B.set(n++, p.x, p.y, p.z, 0.62, 0.62, 0.5);
        for (const q of v.sold) if (q.wx !== undefined && n < cap) B.set(n++, q.wx, q.wy, q.wz, 0.24, 0.24, 0.42);
      }
      B.commit(n);
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
          // 第三版修订：先在近处找位置；找不到（布阵边上挤着 4–5 支部队）再放宽到上移 2.6 个标签高、左右各 0.9 个标签宽
          for (const [kUp, kSide] of [[1.6, 0.42], [2.6, 0.9]]) {
            const maxUp = h * kUp + 4, maxDown = h * 0.45, maxSide = w * kSide;
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
            // 组合偏移：贴着两个标签的边（向上再横移）
            const n0 = dys.length;
            for (let a = 1; a < n0; a++) for (let b = 1; b < n0; b++) { const sum = dys[a] + dys[b]; if (dys[a] > 0 && dys[b] > 0 && sum <= maxUp) dys.push(sum); }
            let bestCost = Infinity;
            for (const dy of dys)
              for (const dx of dxs) {
                if (hits(near, l0 + dx, t0 - dy, r0 + dx, b0 - dy)) continue;
                // 上移最自然，下移会挡住自己的部队，平移次之；与上一帧的目标接近者优先（防抖）
                const cost = (dy >= 0 ? dy : -dy * 1.8) + Math.abs(dx) * 1.25
                  + (Math.abs(dx - L.tdx) + Math.abs(dy - L.tdy)) * 0.3;
                if (cost < bestCost) { bestCost = cost; best = { dx, dy }; }
              }
            if (best) break;
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

    // 行军：沿路径（格子中心的 Catharine-Rom 平滑曲线）匀速行进，贴地、按速度换步态，扬尘
    async moveUnit(u, path) {
      const v = this.vis(u);
      if (!v || !path) return;
      sfx('march', 0.4);
      if (path.length < 2) { v.rest(); return; }
      const pts = path.map(p => this.tile(p.x, p.y));
      const n = pts.length - 1, per = 0.21;
      const at = (s, out) => {   // s ∈ [0, n]
        const i = Math.min(n - 1, Math.floor(s)), t = s - i;
        const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(n, i + 2)];
        const t2 = t * t, t3 = t2 * t;
        const f = (a, b, c, d) => 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
        return out.set(f(p0.x, p1.x, p2.x, p3.x), 0, f(p0.z, p1.z, p2.z, p3.z));
      };
      const cur = new THREE.Vector3(), ahead = new THREE.Vector3();
      let t = 0, last = now();
      while (t < 1) {
        if (this.disposed) return;
        const s = t * n;
        at(s, cur); at(Math.min(n, s + 0.12), ahead);
        if (ahead.distanceToSquared(cur) > 1e-5) v.face(ahead.clone().sub(cur));
        const gy = this.groundY(cur.x, cur.z);
        const ti = Math.min(n, Math.round(s)), ty = pts[ti].y;
        v.pos.set(cur.x, Math.max(gy, ty <= WADE_Y + 0.001 ? WADE_Y : gy) + Math.abs(Math.sin(s * Math.PI * 2)) * 0.03, cur.z);
        if (Math.random() < 0.3) this._dustPuff(v.pos, 0.6);
        await SG.frame();
        const nw = now();
        t += Math.min(0.1, nw - last) / (per * n);
        last = nw;
      }
      v.pos.copy(pts[n]);
      v.rest();
    }

    async lunge(a, t) {
      const v = this.vis(a);
      if (!v) return;
      const from = this.tile(a.x, a.y), to = this.tile(t.x, t.y);
      v.face(to.clone().sub(from));
      if (v.act) v.act('attack');
      await this._loop(0.36, k => { v.pos.lerpVectors(from, to, Math.sin(k * Math.PI) * 0.38); });
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

    // 溃散：士兵倒地、战马人立，随后沉入地下消失
    async rout(u) {
      const v = this.vis(u);
      if (!v) return;
      sfx('lose', 0.25);
      this.smoke(this.tile(u.x, u.y).add(new THREE.Vector3(0, 0.3, 0)));
      const p = v.pos.clone();
      if (v.fig) {
        v.act('rout');
        await this._loop(0.75, () => { });
        await this._loop(0.45, t => { v.pos.set(p.x, p.y - t * t * 0.9, p.z); });
      } else await this._loop(0.6, t => { v.pos.set(p.x, p.y - t * 0.8, p.z); });
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
      if (this.crowd) { try { this.crowd.dispose(); } catch (e) { console.error(e); } this.crowd = null; }
      this.clearHighlights();
      for (const mat of this.hlMats.values()) mat.dispose();
      for (const g of this.hlGeos.values()) g.dispose();
      this.hlGeos.clear();
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
      if (this._fortMat) shared.add(this._fortMat);
      for (const im of this._props || []) { if (im.dispose) im.dispose(); if (im.userData && im.userData.outline) im.userData.outline.dispose(); }
      if (this._fade) for (const it of this._fade.items) if (it.proxy) { this.group.remove(it.proxy); it.proxy = null; }
      if (this._blobs) { this._blobs.dispose(); this._blobs = null; }
      CUT_STATE.list = []; CUT_STATE.gen++;      // 第三轮：树上的洞清空（挖洞材质跨棋盘常驻）
      if (this._lamps) { for (const L of this._lamps) SG.Gfx.removeLamp(L); this._lamps = null; }
      if (this._roadTex) { this._roadTex.tex.dispose(); this._roadTex = null; }
      // 第四版：SG.LP 的共用材质（LP LowPoly / Outline / blob shadow，按选项缓存）与 SG.LPProps 的缓存几何体不释放
      const lpShared = mt => !!(mt && mt.userData && (mt.userData.lpKind || /^LP /.test(mt.name || '')));
      this.group.traverse(o => {
        if (o.geometry && !discCacheHas(o.geometry) && o.geometry !== this.hlGeo && !(o.geometry.userData && o.geometry.userData.sharedGeometry)) o.geometry.dispose();
        if (o.material && !shared.has(o.material) && !lpShared(o.material) && !(o.isPoints)) {
          if (Array.isArray(o.material)) o.material.forEach(mm => mm.dispose()); else o.material.dispose();
        }
      });
      if (this.group.parent) this.group.parent.remove(this.group);
      // 还原开战前的时刻（战略地图再显示时会套用它自己的时刻）
      if (this._moodPrev != null && SG.Gfx && SG.Gfx.mood && SG.Gfx.setMood) { try { SG.Gfx.setMood(this._moodPrev); } catch (e) { /* 忽略 */ } }
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
    const own = !v._mesh;   // 第三版：人物模型的残影为当前姿势的烘焙几何体（随残影释放）
    const m = new THREE.Mesh(own ? v.ghostGeometry() : v._mesh.geometry, mat);
    m.userData.keepGeo = !own;
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
      // 贴合地形的格子网格（与 highlight 相同，缓存共用）：山坡、山丘上的光不再一半埋进地里
      const m = new THREE.Mesh(this._hlGeoAt ? this._hlGeoAt(t.x, t.y) : this.hlGeo, mat);
      m.userData.keepGeo = true; m.userData.keepMat = true;
      m.position.copy(this.tile(t.x, t.y)); m.position.y += 0.03;
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
    if (ctx.v && ctx.v.act) ctx.v.act('special');   // 第三版：施展者的必杀动作
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
    if (v.act) v.act('attack');
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
    const own = !v._mesh;
    const m = new THREE.Mesh(own ? v.ghostGeometry() : v._mesh.geometry, mat);
    m.userData.keepGeo = !own;
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
  // 第四版：战场的缺省时刻（温暖的午后，与战略地图的 16.3 时相近；SG.LP.Mood 的小时数）
  BattleView.MOOD_HOUR = 16.0;
  // 第四版：棋盘上的树换用远景模型的镜头距离（游戏缺省 38、可拉近到 12）；TREE_LOD = 'near' | 'far' 强制（测试用）
  // 修订：按块切换——块中心到镜头的距离（世界单位）；BattleView.PROP_CHUNK_TILES：近景树的分块边长（格）
  BattleView.TREE_LOD_DIST = 20;
  // 第三轮：被树挡住的部队周围在树上挖洞（Lanternvale 的 cut-outs）；false = 退回整棵树抖动溶到 35%（构建棋盘时读取）
  BattleView.TREE_CUTOUTS = true;
  BattleView.PROP_CHUNK_TILES = 5;
  BattleView.TREE_LOD = null;

  SG.BattleView = BattleView;
  SG.UnitVisual = UnitVisual;
})();
