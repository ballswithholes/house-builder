'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 各文化的城池模型（第四版：Lanternvale 风格的低多边形，SG.LP / SG.LPProps）

   第四版（美术推广 · 城池）：全部文化的城池近景与远景模型按试点批准的画风重做——平面着色的木石与建筑、顶点色每面抖动、
   贴地 AO、夯土 / 砖 / 石墙的分层色带与墙脚石、墨线（底座地面不描）、软团树冠（上暖下青）、夜里窗纸 / 灯笼 / 火光自发光
   （MeshBuilder.nightEmission，由场景的 LP.Mood 点亮），并给出灯位（城门灯笼、殿前灯、篝火）供 Mood 加灯池。
   汉地城池沿用试点城门楼的语汇：夯土城墙（版筑分层、深色缝、石砌勒脚、垛口女墙、马面）、朱柱格窗的城楼与歇山灰瓦顶
   （SG.LPProps.parts.hanRoof：筒瓦、瓦当、起翘、鸱吻）、红灯笼；都城为金顶重檐大殿与阙楼。

   约定（与 v3 相同）：size ≈ 占地半径 r，底座约 2.3r × 2.3r，城墙在 ±r，城门朝南；旗帜由 map-view 画在东北角
   （Unity (0.9r, 0, 0.9r)），该角保持空旷；标签高度 size × 1.6 + 0.6 —— 模型最高处不超过约 1.5r。
   模型内部按米建模（1 个 size 单位 = 20 m），出图时缩到单位尺寸（size = 1），底座顶面在 y = 0.1。
   三角形：近景每城约 2.5k–4.8k，大都城 ≤ ~6k（含城墙）；远景 ≤ ~400。几何体按（模型、档次、变体）缓存，同类城共用。

   文化 → 模型
     han        汉：夯土方城（版筑色带、马面、垛口）、三座角楼、南门重檐城楼、东西门楼、三层台基的庑殿主殿、坊屋、槐树；
                都城（洛阳、长安）金顶重檐、阙楼
     korea      高句丽 / 三韩：山城（山丘 + 依山的不规则石墙）、灰瓦门楼、山上殿阁（丹青檐下）、草顶 / 瓦顶民居、松
     wa         倭（邪马台）：环壕、木栅、望楼、高床仓库、竖穴住居；都城为带千木的大型高床殿
     steppe     草原：毡帐营地（蒙古包）、车阵、马尾纛、马栏、篝火；都城为金顶大帐
     tarim      西域绿洲：风蚀的夯土城墙、平顶土房、钻天杨、水池与果园、烽燧；大城为佛塔
     seasia     南海（林邑、扶南）：高脚屋（鞍形屋顶）、木栅、水渠、砖台多重顶神殿、椰树
     yi、nanman 夷洲 / 南中：竹木栅栏围成的高脚屋村落；南中有铜鼓祭台
     celt       凯尔特：山丘堡垒（土垒 + 木栅）、圆形茅屋；都城为大圆屋
     german     日耳曼：山丘堡垒、长屋；都城为带交叉兽头山墙的大厅
     sarmatian  萨尔马提亚：篷车与尖顶帐的车营；都城与大城（潘提卡彭等博斯普鲁斯希腊城）为白墙、卫城丘与神庙
     roman      罗马：石墙圆塔（瓦顶）、双塔夹拱门、红瓦民居、列柱神庙；都城为椭圆竞技场 + 神庙
     persia     安息 / 波斯：土坯城墙（阶梯垛口、半圆扶壁）、釉砖尖拱门楼、穹顶平顶房、三层阶梯内城上的伊万宫殿；
                都城为泰西封式大拱（Taq Kasra）与拜火祠
     arab       哈特拉 / 阿拉伯：圆形石城、圆塔、双伊万神庙与列柱、平顶房、椰枣树
     kushan     贵霜：砖城圆角堡、白色窣堵波（栏楯、平头、伞盖）、僧院；都城为高台列柱穹顶宫殿
     india      天竺：环壕、红砖城墙与半圆马面、筒拱顶门楼、带塔门（torana）的窣堵波、支提拱顶宫殿与民居、椰树、菩提树
     africa     非洲：库施（麦罗埃、纳帕塔、卡拉诺格）= 土砖城、塔门（pylon）神庙、陡峭的王陵金字塔、椰枣树、尼罗河岸；
                阿克苏姆（其余）= 花岗岩石碑、阶梯台基上的石砌宫殿（“猴头”梁端）、圆形茅屋、干砌石矮墙
     moor       毛里（巴夸特）：土石台地、干砌石环墙、方形谷仓堡（agadir）、平顶石屋、游牧帐篷、雪松
     其他未列出的文化按 SG.cultureBase（core.js）回退

   公开接口
     SG.CultureArt.cultures                         已有模型的文化代号
     SG.CultureArt.cultureOfCity(city)              city.culture → SG.WorldData.cultureOfCity(city.key) → 'han'
     SG.CultureArt.isCapital(city, culture?)        汉地：洛阳、长安；其他文化：世界剧本各势力的初始都城（SG.FactionInfo）
     SG.CultureArt.cityInto(mb, size, culture, capital, city?) → mb
         写入一个 MeshBuilder：SG.LP.MeshBuilder（第四版：按它当前的变换写入，带墨线宽度、夜间自发光、风摆）或
         原有的 SG.MeshBuilder（Unity 坐标，可带 mb.M；只有形状与顶点色——要墨线与夜灯请改用下面的 LP 接口）
     SG.CultureArt.cityFarInto(mb, size, culture, capital, city?) → mb   远景模型（≤ ~400 三角形，轮廓与配色一致）
     SG.CultureArt.city(size, culture, capital, city?) → BufferGeometry  近景的 LP 几何体（墨线部分 + 地面部分合一，测试用）
   第四版新增（战略地图建议用这些：实例化 / 合并、墨线、夜灯）
     SG.CultureArt.model(culture, capital, city?, { lod: 'near' | 'far', size }) → { ink, plain, lamps, tris, key, height }
         单位尺寸（size = 1，城心在原点，three 坐标，城门朝 +Z）的缓存几何体：ink = 描墨线的部分，plain = 不描的部分
         （底座、水面），lamps = [{ pos:[x,y,z], color, intensity, range, nightOnly, haloSize, flicker }]（单位尺寸）。
         同一 key 的几何体共用，勿 dispose。放进 LP.Scatter（{ near, far }，缩放 = size）或 LP.StaticBatch 都可以。
     SG.CultureArt.object(size, culture, capital, city?, { lod }) → THREE.Group（LP.mesh + 墨线；userData.lp.lamps 已按 size
         缩放，可直接 SG.LPProps.addLamps(obj, mood)）
     SG.CultureArt.lamps(size, culture, capital, city?) → 灯位数组（按 size 缩放的模型坐标）
     SG.CultureArt.modelKey(culture, city?) / tierOf(size, capital, city?) / variantOf(city) / U（每个 size 单位的米数）
   测试页：tests/culture-art.html（全部文化 × 普通 / 大城 / 都城，近景 / 远景，正午 / 黄昏 / 夜，可旋转）。
   ========================================================================== */
(function () {
  const SG = window.SG;
  if (!SG || SG.CultureArt) return;   // 防止重复载入（map-view.js 在 index.html 未载入本文件时会补载）

  // 第四版依赖 js/lowpoly.js 与 js/lp-props.js。页面没有载入它们时（旧测试页），在解析期于本文件之后同步补载
  try {
    const me = typeof document !== 'undefined' && document.currentScript;
    if (me && me.src && document.readyState === 'loading') {
      const base = me.src.replace(/culture-art\.js([?#].*)?$/, '');
      if (!SG.LP) document.write('<script src="' + base + 'lowpoly.js"><\/script>');
      if (!SG.LPProps) document.write('<script src="' + base + 'lp-props.js"><\/script>');
    }
  } catch (e) { /* 非浏览器环境 */ }

  const U = 20;              // 每个 size 单位的米数：城墙在 ±U，底座半宽约 1.17U
  const LIFT = 0.1;          // 底座顶面高出城心原点（与 v3 的台基一致）
  const D2R = Math.PI / 180;
  const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  let LP = null, PP = null, P = null, H = null, PAL = null, ROOF = null;
  function ready() {
    if (LP) return true;
    if (!SG.LP || !SG.LP.MeshBuilder) return false;
    LP = SG.LP; P = LP.Paint; H = P.hex; PAL = LP.PAL;
    PP = SG.LPProps && SG.LPProps.parts ? SG.LPProps.parts : null;
    ROOF = PP && PP.ROOF_PAL ? PP.ROOF_PAL : null;
    return true;
  }
  const C = c => (Array.isArray(c) ? c : H(c));
  const mix = (a, b, t) => P.mix(C(a), C(b), t);
  const sh = (c, k) => P.shade(C(c), k);
  function rng(seed) {
    let s = (seed >>> 0) || 1;
    return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return (s & 0xffffff) / 16777216; };
  }
  function strHash(s) { let h = 2166136261 >>> 0; s = String(s || ''); for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h; }

  // ============================================================== 调色 --
  // 墙：a / b 两种版筑色（相邻层交替），plinth 勒脚石，par 女墙，walk 墙顶铺石，seam 层间深缝，inner 内壁
  let WALLS = null;
  function walls() {
    if (WALLS) return WALLS;
    const rammed = PAL.rammed, rammedB = mix(PAL.rammed, PAL.rammedDark, 0.32);
    WALLS = {
      rammed: { a: rammed, b: rammedB, plinth: PAL.stone, par: mix(PAL.brick, PAL.rammed, 0.3), walk: mix(PAL.stoneLight, PAL.stone, 0.5), seam: sh(PAL.rammedDark, 0.88), inner: sh(rammedB, 0.95), lip: 0.08 },
      stone: { a: H('#D9CEB6'), b: H('#CCC0A6'), plinth: H('#A99F92'), par: H('#DCD2BC'), walk: H('#B8AE9C'), seam: H('#9C907C'), inner: H('#C4B89E'), lip: 0.06 },
      greystone: { a: H('#BDB8AA'), b: H('#ADA89A'), plinth: H('#8E877C'), par: H('#C2BDB0'), walk: H('#A39E92'), seam: H('#7E786E'), inner: H('#A8A396'), lip: 0.06 },
      whitestone: { a: H('#EDE6D6'), b: H('#E2DAC8'), plinth: H('#B4AA98'), par: H('#F2ECDE'), walk: H('#CFC6B4'), seam: H('#B8AE9C'), inner: H('#DCD3C0'), lip: 0.05 },
      mud: { a: H('#DDBE90'), b: H('#D2B183'), plinth: H('#B89870'), par: H('#E2C597'), walk: H('#C8A87C'), seam: H('#B8946A'), inner: H('#CBA97C'), lip: 0.07 },
      mudred: { a: H('#C99068'), b: H('#BC845E'), plinth: H('#9E7454'), par: H('#D09A70'), walk: H('#B07E5A'), seam: H('#946A4C'), inner: H('#B9845E'), lip: 0.07 },
      buff: { a: H('#D2AE84'), b: H('#C6A078'), plinth: H('#A89276'), par: H('#D8B68C'), walk: H('#BCA07E'), seam: H('#A48462'), inner: H('#C09C74'), lip: 0.07 },
      redbrick: { a: H('#C67858'), b: H('#B86C4E'), plinth: H('#9E8A74'), par: H('#CC8060'), walk: H('#A88E76'), seam: H('#94573E'), inner: H('#B47052'), lip: 0.07 },
      drystone: { a: H('#B8AC96'), b: H('#A89C86'), plinth: H('#948874'), par: H('#BEB29C'), walk: H('#A09480'), seam: H('#847866'), inner: H('#A89C88'), lip: 0.1 },
    };
    return WALLS;
  }

  // ============================================================== 场景上下文 --
  // K = { M（描墨线）, Mp（不描：底座、水面）, lamps, r（布局随机数）, seed }
  function mark(K, name) {
    if (!K.parts) return;
    const n = K.M.triangleCount + K.Mp.triangleCount;
    K.parts[name] = (K.parts[name] || 0) + n - K.lastMark;
    K.lastMark = n;
  }
  function context(seed) {
    const M = new LP.MeshBuilder(seed | 0);
    M.jitter = 0.06; M.jitterAll = true; M.aoStrength = 0.3; M.aoHeight = 3.2;
    const Mp = new LP.MeshBuilder((seed | 0) + 17);
    Mp.jitter = 0.04; Mp.jitterAll = true; Mp.aoStrength = 0; Mp.ink = 0;
    return { M, Mp, lamps: [], r: rng(seed * 7919 + 13), seed, parts: {}, lastMark: 0 };
  }
  // 灯位（米）：灯笼 / 窗 / 篝火的点光源，nightOnly = 只在夜里亮
  function lamp(K, pos, o) {
    const e = K.M.m.elements, x = pos[0], y = pos[1], z = pos[2];   // 当前变换下的位置（at() 里的局部坐标 → 模型坐标）
    const w = [e[0] * x + e[4] * y + e[8] * z + e[12], e[1] * x + e[5] * y + e[9] * z + e[13], e[2] * x + e[6] * y + e[10] * z + e[14]];
    K.lamps.push(Object.assign({ color: '#FFB866', intensity: 1.4, range: 9, nightOnly: true, haloSize: 1.1, flicker: 0 }, o || {}, { pos: w }));
  }

  // ============================================================== 基本构件（米，three 坐标，+Z = 南 = 城门）--
  /** 在 (x, z) 处、朝 yaw（度，0 = 正面朝 +Z）的局部坐标系里画 fn。 */
  function at(K, x, y, z, yaw, fn) {
    K.M.push().translate(x, y, z); if (yaw) K.M.rotate(0, yaw, 0);
    K.Mp.push().translate(x, y, z); if (yaw) K.Mp.rotate(0, yaw, 0);
    try { fn(); } finally { K.M.pop(); K.Mp.pop(); }
  }
  /** 多边形（[[x, z]…]）的点 */
  function ngon(n, rx, rz, off) { const pts = []; for (let i = 0; i < n; i++) { const a = (off || 0) + i * Math.PI * 2 / n; pts.push([Math.cos(a) * rx, Math.sin(a) * rz]); } return pts; }
  function octo(h, c) { return [[-h + c, -h], [h - c, -h], [h, -h + c], [h, h - c], [h - c, h], [-h + c, h], [-h, h - c], [-h, -h + c]]; }
  /**
   * 底座（不描墨线）：outline 为顶面轮廓（[[x, z]…]，凸多边形），顶面 y = 0，外缘一圈斜坡（bevel）再垂直下探到 y = -3.4
   * （埋进坡地）。inner（可选）= 城内铺地的轮廓与颜色，在顶面上分两色。
   */
  function pad(K, outline, o) {
    const M = K.Mp, top = C(o.top), side = C(o.side || mix(o.top, '#8C6E52', 0.55)), n = outline.length, bev = o.bevel != null ? o.bevel : 1.4;
    const cx = outline.reduce((s, p) => s + p[0], 0) / n, cz = outline.reduce((s, p) => s + p[1], 0) / n;
    const grow = (p, d) => { const dx = p[0] - cx, dz = p[1] - cz, l = Math.hypot(dx, dz) || 1; return [p[0] + dx / l * d, p[1] + dz / l * d]; };
    const rim = outline.map(p => grow(p, bev));
    const inner = o.inner ? o.inner.pts : null;
    if (inner && inner.length === n) {
      const ic = C(o.inner.color);
      for (let i = 1; i + 1 < n; i++) M.triangleFacing([inner[0][0], 0, inner[0][1]], [inner[i][0], 0, inner[i][1]], [inner[i + 1][0], 0, inner[i + 1][1]], [0, 1, 0], P.shade(ic, 0.97 + 0.06 * K.r()));
      for (let i = 0; i < n; i++) { const j = (i + 1) % n; M.quad([inner[i][0], 0, inner[i][1]], [outline[i][0], 0, outline[i][1]], [outline[j][0], 0, outline[j][1]], [inner[j][0], 0, inner[j][1]], [0, 1, 0], P.shade(top, 0.96 + 0.08 * K.r())); }
    } else {
      for (let i = 1; i + 1 < n; i++) M.triangleFacing([outline[0][0], 0, outline[0][1]], [outline[i][0], 0, outline[i][1]], [outline[i + 1][0], 0, outline[i + 1][1]], [0, 1, 0], P.shade(top, 0.97 + 0.06 * K.r()));
    }
    const yb = -(o.bevelDrop != null ? o.bevelDrop : 0.7), yd = -3.4, edge = mix(top, side, 0.45);
    for (let i = 0; i < n; i++) {
      const j = (i + 1) % n, a = outline[i], b = outline[j], ra = rim[i], rb = rim[j];
      const out = [ra[0] + rb[0] - a[0] - b[0], 0.6, ra[1] + rb[1] - a[1] - b[1]];
      M.quadColors([a[0], 0, a[1]], [b[0], 0, b[1]], [rb[0], yb, rb[1]], [ra[0], yb, ra[1]], out, [top, top, edge, edge]);
      M.quadColors([ra[0], yb, ra[1]], [rb[0], yb, rb[1]], [rb[0], yd, rb[1]], [ra[0], yd, ra[1]], [out[0], 0, out[2]], [edge, edge, P.shade(side, 0.8), P.shade(side, 0.8)]);
    }
  }
  /** 平放的水面（不描墨线，略带自身的明暗）：多边形 pts，y */
  function water(K, pts, y, col) {
    const M = K.Mp, c = C(col || '#5E9C94');
    for (let i = 1; i + 1 < pts.length; i++) M.triangleFacing([pts[0][0], y, pts[0][1]], [pts[i][0], y, pts[i][1]], [pts[i + 1][0], y, pts[i + 1][1]], [0, 1, 0], P.shade(c, 0.96 + 0.08 * K.r()));
  }
  /** 环壕（方环水面）：内半宽 ri，外半宽 ro，水面 y（略低于地面）；南面中段留土桥 */
  function moatRing(K, ri, ro, y, col, bridge) {
    const bw = bridge || 0;
    const seg = (pts) => water(K, pts, y, col);
    seg([[-ro, -ro], [ro, -ro], [ro, -ri], [-ro, -ri]]);
    seg([[-ro, ri], [-bw, ri], [-bw, ro], [-ro, ro]]);
    seg([[bw, ri], [ro, ri], [ro, ro], [bw, ro]]);
    seg([[-ro, -ri], [-ri, -ri], [-ri, ri], [-ro, ri]]);
    seg([[ri, -ri], [ro, -ri], [ro, ri], [ri, ri]]);
    // 壕岸：内外两圈的斜坡（土色），让水面像挖下去的
    const M = K.Mp, bank = H('#8C7A5A'), d = 0.9;
    const ring = (h, s) => [[-h, -h], [h, -h], [h, h], [-h, h]];
    for (const [h, s] of [[ri, -1], [ro, 1]]) {
      const r0 = ring(h), r1 = ring(h + s * d);
      for (let i = 0; i < 4; i++) { const j = (i + 1) % 4; M.quad([r0[i][0], y, r0[i][1]], [r0[j][0], y, r0[j][1]], [r1[j][0], 0.02, r1[j][1]], [r1[i][0], 0.02, r1[i][1]], [0, 1, 0], P.shade(bank, 0.95 + 0.1 * K.r())); }
    }
  }

  /**
   * 一段城墙（a → b，外侧自动朝离开 center 的一侧）：分层版筑 / 砖 / 石（每层若干段，自下而上由暗到亮的渐变）、
   * 层间断续的深缝、石砌勒脚、内壁、墙顶铺石、女墙与垛口。o：{ h, t, bat（外壁收分 m/m）, kind（WALLS 键）,
   * courses（层高数组，不含 0 与 h）, plinth（勒脚高）, panel: [最短, 最长]（每段长）, par: { h, t } | null,
   * merlon: { w, h, step, stepped（阶梯垛）, round } | null, skip: [[u0, u1]…]（沿墙的米数，从 a 起，不画垛口与外壁的范围）,
   * bastions: [{ u, w, d }]（马面）, ends, inner（默认 true）, center }
   */
  function wallRun(K, ax, az, bx, bz, o) {
    const M = K.M, pal = walls()[o.kind || 'rammed'];
    let dx = bx - ax, dz = bz - az;
    const L = Math.hypot(dx, dz);
    if (L < 0.1) return;
    const c0 = o.center || [0, 0];
    let mx = (ax + bx) / 2, mz = (az + bz) / 2, flip = false;
    if (-dz * (mx - c0[0]) + dx * (mz - c0[1]) < 0) { dx = -dx; dz = -dz; flip = true; }
    const yaw = Math.atan2(-dz / L, dx / L) / D2R;
    const h = o.h, t = o.t, bat = o.bat || 0, X = L / 2, hP = o.plinth != null ? o.plinth : 1.1;
    const T = y => t / 2 - bat * y;
    const skip = (o.skip || []).map(([u0, u1]) => flip ? [X - u1, X - u0] : [u0 - X, u1 - X]);
    const inSkip = x => skip.some(([a, b]) => x > a && x < b);
    const ys = [0, hP].concat(o.courses || [hP + (h - hP) * 0.33, hP + (h - hP) * 0.66]).concat([h]);
    const pmin = o.panel ? o.panel[0] : 3, pmax = o.panel ? o.panel[1] : 6.5;
    M.push().translate(mx, o.y0 || 0, mz).rotate(0, yaw, 0);
    // 外壁：逐层逐段
    for (let i = 0; i < ys.length - 1; i++) {
      const y0 = ys[i], y1 = ys[i + 1], plinth = i === 0 && hP > 0;
      const base = plinth ? pal.plinth : i % 2 ? pal.a : pal.b;
      let x0 = -X;
      while (x0 < X - 0.01) {
        let x1 = Math.min(X, x0 + lerp(pmin, pmax, K.r()) * (plinth ? 0.6 : 1));
        if (X - x1 < pmin * 0.5) x1 = X;
        for (const [a, b] of skip) { if (x0 < a - 0.01 && x1 > a) x1 = a; else if (x0 < b - 0.01 && x1 > b && x0 >= a - 0.01) x1 = b; }
        const mid = (x0 + x1) / 2;
        if (!inSkip(mid)) {
          const k = 0.95 + 0.1 * K.r(), cb = P.shade(base, k * (plinth ? 0.96 : 0.92)), ct = P.shade(base, k * (plinth ? 1.02 : 1.03));
          const dzP = plinth ? 0.18 : 0;
          M.quadColors([x0, y0, T(y0) + dzP], [x1, y0, T(y0) + dzP], [x1, y1, T(y1) + dzP], [x0, y1, T(y1) + dzP], [0, bat, 1], [cb, cb, ct, ct]);
          if (plinth) M.quad([x0, y1, T(y1)], [x1, y1, T(y1)], [x1, y1, T(y1) + dzP], [x0, y1, T(y1) + dzP], [0, 1, 0], P.shade(base, 1.08));
        }
        x0 = x1;
      }
      // 层间深缝（勒脚之上，断续）
      if (i > 1) {
        let s0 = -X;
        while (s0 < X - 0.5) {
          const s1 = Math.min(X, s0 + 2 + K.r() * 5);
          if (K.r() < 0.78 && !inSkip((s0 + s1) / 2)) {
            const y = y0 + (K.r() - 0.5) * 0.04, hh = 0.035 + K.r() * 0.03;
            M.ink = 0;
            M.quad([s0, y - hh, T(y - hh) + 0.01], [s1, y - hh, T(y - hh) + 0.01], [s1, y + hh, T(y + hh) + 0.01], [s0, y + hh, T(y + hh) + 0.01], [0, bat, 1], pal.seam);
            M.ink = 1;
          }
          s0 = s1 + 0.6 + K.r() * 1.5;
        }
      }
    }
    // 内壁（两色两段）与墙顶
    if (o.inner !== false) {
      const yh = (h + hP) / 2;
      M.quad([-X, 0, -t / 2], [-X, yh, -t / 2], [X, yh, -t / 2], [X, 0, -t / 2], [0, 0, -1], P.shade(pal.inner, 0.92));
      M.quad([-X, yh, -t / 2], [-X, h, -t / 2], [X, h, -t / 2], [X, yh, -t / 2], [0, 0, -1], pal.inner);
    }
    const par = o.par === undefined ? { h: 1.0, t: Math.min(0.8, t * 0.22) } : o.par;
    const zt = T(h), zIn = par ? zt - par.t : zt;
    M.quad([-X, h, -t / 2], [X, h, -t / 2], [X, h, zIn], [-X, h, zIn], [0, 1, 0], pal.walk);
    if (o.ends) for (const s of [-1, 1]) M.quad([s * X, 0, -t / 2], [s * X, 0, T(0)], [s * X, h, T(h)], [s * X, h, -t / 2], [s, 0, 0], P.shade(pal.b, 0.9));
    if (par) {
      M.color = pal.par;
      M.box([0, h + par.h / 2, zt - par.t / 2], [L, par.h, par.t], P.shade(pal.par, 1.06), o.ends ? 'tfkrl' : 'tfk');
      const m = o.merlon === undefined ? { w: 1.4, h: 0.9, step: 3.6 } : o.merlon;
      if (m) merlonRow(K, -X, X, h + par.h, zt - par.t / 2, m, pal.par, inSkip);
    }
    // 马面
    for (const b of o.bastions || []) {
      const u = flip ? X - b.u : b.u - X;
      if (inSkip(u)) continue;
      bastion(K, u, b.w, b.d, h, T, bat, ys.length > 4 ? [ys[0], ys[1], ys[Math.floor(ys.length / 2)], h] : ys, pal, par, o.merlon === undefined ? { w: 1.4, h: 0.9, step: 3.6 } : o.merlon);
    }
    M.pop();
  }
  /** 一排垛口（局部 X 从 x0 到 x1，墙顶 y，中心 z）；m = { w, h, step, stepped, round } */
  function merlonRow(K, x0, x1, y, z, m, col, skipFn) {
    const M = K.M, n = Math.max(1, Math.floor((x1 - x0) / m.step)), d = m.d || 0.7, ink = M.ink;
    M.ink = 0.5;
    for (let i = 0; i < n; i++) {
      const x = x0 + (i + 0.5) * (x1 - x0) / n;
      if (skipFn && skipFn(x)) continue;
      M.color = P.shade(col, 0.95 + 0.1 * K.r());
      if (m.stepped) {
        // 阶梯垛（波斯 / 安息）：两级台阶
        M.box([x, y + m.h * 0.3, z], [m.w, m.h * 0.6, d], null, 'tfkrl');
        M.box([x, y + m.h * 0.8, z], [m.w * 0.5, m.h * 0.4, d * 0.9], null, 'tfkrl');
      } else if (m.round) {
        M.box([x, y + m.h * 0.35, z], [m.w, m.h * 0.7, d], null, 'fkrl');
        M.push().translate(x, y + m.h * 0.7, z).rotate(90, 0, 0);
        M.cylinder([0, -d / 2, 0], m.w / 2, m.w / 2, d, 6, false, true, true);
        M.pop();
      } else M.box([x, y + m.h / 2, z], [m.w, m.h, d], P.shade(col, 1.07), 'tfkrl');
    }
    M.ink = ink;
  }
  /** 马面（墙外突出的方台，同墙一样分层），局部坐标：中心 u，宽 w，深 d */
  function bastion(K, u, w, d, h, T, bat, ys, pal, par, m) {
    const M = K.M, x0 = u - w / 2, x1 = u + w / 2;
    for (let i = 0; i < ys.length - 1; i++) {
      const y0 = ys[i], y1 = ys[i + 1], base = i === 0 ? pal.plinth : i % 2 ? pal.a : pal.b, k = 0.96 + 0.08 * K.r();
      const cb = P.shade(base, k * 0.92), ct = P.shade(base, k * 1.03);
      const zf = y => T(y) + d;
      M.quadColors([x0, y0, zf(y0)], [x1, y0, zf(y0)], [x1, y1, zf(y1)], [x0, y1, zf(y1)], [0, bat, 1], [cb, cb, ct, ct]);
      for (const s of [-1, 1]) { const x = s < 0 ? x0 : x1; M.quad([x, y0, T(y0)], [x, y0, zf(y0)], [x, y1, zf(y1)], [x, y1, T(y1)], [s, 0, 0], P.shade(base, k * 0.9)); }
    }
    M.quad([x0, h, T(h)], [x1, h, T(h)], [x1, h, T(h) + d], [x0, h, T(h) + d], [0, 1, 0], pal.walk);
    if (par) {
      M.color = pal.par;
      M.box([u, h + par.h / 2, T(h) + d - par.t / 2], [w, par.h, par.t], P.shade(pal.par, 1.06), 'tfkrl');
      if (m) merlonRow(K, x0, x1, h + par.h, T(h) + d - par.t / 2, Object.assign({}, m, { step: Math.min(m.step, w / 2) }), pal.par);
    }
  }
  /**
   * 方台 / 方塔：分层、收分（bat）的方块（底 w × d），四面各层一块渐变面、顶面；o: { kind, ys（层高，不含 0）, bat, top, faces（'fkrl' 子集）}
   */
  function block(K, x, y0, z, w, d, h, o) {
    const M = K.M, pal = walls()[o.kind || 'rammed'], bat = o.bat || 0;
    const ys = [0].concat(o.ys || [o.plinth != null ? o.plinth : 1.0, h * 0.5]).concat([h]).filter((v, i, a) => i === 0 || v > a[i - 1] + 0.05);
    const W = y => w / 2 - bat * y, Dd = y => d / 2 - bat * y, F = o.faces || 'fkrl';
    for (let i = 0; i < ys.length - 1; i++) {
      const a = ys[i], b = ys[i + 1], base = i === 0 && o.plinth !== 0 ? pal.plinth : i % 2 ? pal.a : pal.b, k = 0.95 + 0.1 * K.r();
      const cb = P.shade(base, k * 0.92), ct = P.shade(base, k * 1.03);
      const q = (p0, p1, p2, p3, n, sideK) => M.quadColors(p0, p1, p2, p3, n, [P.shade(cb, sideK), P.shade(cb, sideK), P.shade(ct, sideK), P.shade(ct, sideK)]);
      if (F.includes('f')) q([x - W(a), y0 + a, z + Dd(a)], [x + W(a), y0 + a, z + Dd(a)], [x + W(b), y0 + b, z + Dd(b)], [x - W(b), y0 + b, z + Dd(b)], [0, bat, 1], 1);
      if (F.includes('k')) q([x + W(a), y0 + a, z - Dd(a)], [x - W(a), y0 + a, z - Dd(a)], [x - W(b), y0 + b, z - Dd(b)], [x + W(b), y0 + b, z - Dd(b)], [0, bat, -1], 0.96);
      if (F.includes('r')) q([x + W(a), y0 + a, z + Dd(a)], [x + W(a), y0 + a, z - Dd(a)], [x + W(b), y0 + b, z - Dd(b)], [x + W(b), y0 + b, z + Dd(b)], [1, bat, 0], 0.93);
      if (F.includes('l')) q([x - W(a), y0 + a, z - Dd(a)], [x - W(a), y0 + a, z + Dd(a)], [x - W(b), y0 + b, z + Dd(b)], [x - W(b), y0 + b, z - Dd(b)], [-1, bat, 0], 0.93);
    }
    if (o.top !== false) M.quad([x - W(h), y0 + h, z - Dd(h)], [x + W(h), y0 + h, z - Dd(h)], [x + W(h), y0 + h, z + Dd(h)], [x - W(h), y0 + h, z + Dd(h)], [0, 1, 0], o.topColor ? C(o.topColor) : pal.walk);
    return { tw: W(h) * 2, td: Dd(h) * 2, top: y0 + h };
  }
  /** 方台顶上一圈女墙垛口（四边，可跳过某些边：skip 'f' / 'k' / 'l' / 'r'） */
  function crown(K, x, y, z, w, d, o) {
    const M = K.M, pal = walls()[o.kind || 'rammed'], ph = o.ph != null ? o.ph : 0.9, pt = o.pt || 0.6, m = o.merlon === undefined ? { w: 1.2, h: 0.85, step: 2.6 } : o.merlon;
    const sides = [['f', [x, z + d / 2 - pt / 2], w, 0], ['k', [x, z - d / 2 + pt / 2], w, 0], ['r', [x + w / 2 - pt / 2, z], d - 2 * pt, 90], ['l', [x - w / 2 + pt / 2, z], d - 2 * pt, 90]];
    for (const [s, c, len, rot] of sides) {
      if (o.skip && o.skip.includes(s)) continue;
      M.push().translate(c[0], y, c[1]).rotate(0, rot, 0);
      M.color = pal.par;
      if (ph > 0) M.box([0, ph / 2, 0], [len, ph, pt], P.shade(pal.par, 1.06), 'tfkrl');
      if (m) merlonRow(K, -len / 2, len / 2, ph, 0, Object.assign({ d: pt }, m), pal.par);
      M.pop();
    }
  }
  /**
   * 圆塔：平面着色的多边形塔身（sides 边，分层色带、收分），顶部可为垛口环（merlons）、圆锥瓦顶（cone: 颜色）、穹顶（dome: 颜色）
   * 或平顶。o: { kind, sides, bands（层数）, cone, dome, merlons, taper }
   */
  function roundTower(K, x, y0, z, r, h, o) {
    const M = K.M, pal = walls()[o.kind || 'stone'], n = o.sides || 10, tp = o.taper != null ? o.taper : 0.06, bands = o.bands || 3;
    const prof = [], cols = [];
    for (let i = 0; i <= bands; i++) { const y = i === 0 ? 0 : i === 1 ? Math.min(h * 0.18, 1.2) : lerp(Math.min(h * 0.18, 1.2), h, (i - 1) / (bands - 1)); prof.push([r * (1 + tp) - r * tp * 2 * (y / h), y]); cols.push(i === 0 ? pal.plinth : i % 2 ? pal.a : pal.b); }
    M.push().translate(x, y0, z);
    // 分段 lathe：每层一种色（平面着色）
    for (let i = 0; i < bands; i++) {
      const k = 0.96 + 0.08 * K.r();
      M.color = P.shade(cols[i === 0 ? 0 : i + 1 - (i % 2)] || pal.a, k);
      M.lathe([prof[i], prof[i + 1]], n, false, false, false, [P.shade(cols[i] || pal.a, 0.93 * k), P.shade(cols[i] || pal.a, 1.03 * k)], (K.seed * 37) % 360);
    }
    const rt = prof[prof.length - 1][0];
    if (o.cone) {
      M.color = C(o.cone);
      const ro = rt * 1.22;
      M.lathe([[rt * 0.7, h - 0.2], [ro, h + 0.1], [ro * 0.98, h + 0.35], [0, h + rt * 1.55 + 0.4]], n, false, true, false, [P.shade(C(o.cone), 0.72), P.shade(C(o.cone), 0.8), C(o.cone), P.shade(C(o.cone), 1.12)], (K.seed * 37) % 360);
    } else if (o.dome) {
      M.color = C(o.dome);
      M.cylinder([0, h, 0], rt * 1.04, rt * 1.04, 0.35, n, false, false, true);
      const dp = []; for (let i = 0; i <= 4; i++) { const a = i / 4 * Math.PI / 2; dp.push([Math.cos(a) * rt * 0.92, h + 0.35 + Math.sin(a) * rt * 0.85]); } dp[4][0] = 0;
      M.lathe(dp, n, true, false, false, dp.map((p, i) => P.shade(C(o.dome), 0.92 + i * 0.03)));
    } else {
      // 平顶 + 垛口环
      M.color = pal.walk; M.disc([0, h, 0], rt * 0.98, n);
      if (o.merlons !== false) {
        const nm = o.merlons || Math.max(5, Math.round(rt * 2.2));
        for (let i = 0; i < nm; i++) {
          const a = (i + 0.5) / nm * Math.PI * 2, mw = Math.min(1.1, rt * 2.6 / nm * 0.62);
          M.color = P.shade(pal.par, 0.95 + 0.1 * K.r());
          M.push().translate(Math.cos(a) * (rt - 0.32), h, Math.sin(a) * (rt - 0.32)).rotate(0, -a / D2R + 90, 0);
          M.box([0, 0.45, 0], [mw, 0.9, 0.62], P.shade(pal.par, 1.06), 'tfkrl');
          M.pop();
        }
      }
    }
    M.pop();
    return { top: y0 + h, r: rt };
  }

  /**
   * 拱门洞（画在朝 +Z 的立面 z 上，底边中点 (x, y0)）：深色门洞（半圆 / 尖拱 / 方门）、两扇门（门色，中缝）、券石圈、
   * 门槛。o: { w, h, kind: 'round' | 'pointed' | 'square', door, frame（券石色）, studs, recess }
   */
  function archway(K, x, y0, z, o) {
    const M = K.M, w = o.w, hw = w / 2, kind = o.kind || 'round', hh = o.h;
    const rise = kind === 'pointed' ? hw * 1.35 : kind === 'square' ? 0 : hw, ys = Math.max(0.2, hh - rise);
    const dark = H('#3A2E28'), ink = M.ink;
    M.ink = 0;
    const arcPts = [];
    if (kind !== 'square') for (let i = 0; i <= 8; i++) {
      const a = i / 8 * Math.PI, c = Math.cos(a);
      const y = kind === 'pointed' ? rise * Math.pow(Math.max(0, 1 - Math.abs(c)), 0.55) : Math.sin(a) * hw;
      arcPts.push([x + c * hw, y0 + ys + y]);
    }
    // 门洞（深色）
    M.quad([x - hw, y0, z + 0.02], [x + hw, y0, z + 0.02], [x + hw, y0 + ys, z + 0.02], [x - hw, y0 + ys, z + 0.02], [0, 0, 1], dark);
    for (let i = 0; i < arcPts.length - 1; i++) M.triangleFacing([x, y0 + ys, z + 0.02], [arcPts[i][0], arcPts[i][1], z + 0.02], [arcPts[i + 1][0], arcPts[i + 1][1], z + 0.02], [0, 0, 1], dark);
    // 门扇
    if (o.door !== null) {
      const dc = C(o.door || '#A83E2E'), dh = ys + (kind === 'square' ? 0 : rise * 0.32), inset = hw * 0.1;
      for (const s of [-1, 1]) {
        const x0 = x + s * 0.06, x1 = x + s * (hw - inset);
        M.quad([Math.min(x0, x1), y0, z + 0.05], [Math.max(x0, x1), y0, z + 0.05], [Math.max(x0, x1), y0 + dh, z + 0.05], [Math.min(x0, x1), y0 + dh, z + 0.05], [0, 0, 1], P.shade(dc, s < 0 ? 1 : 0.94));
        if (o.studs) for (let r = 0; r < 3; r++) M.quad([x + s * hw * 0.45 - 0.09, y0 + dh * (0.25 + r * 0.25) - 0.09, z + 0.07], [x + s * hw * 0.45 + 0.09, y0 + dh * (0.25 + r * 0.25) - 0.09, z + 0.07], [x + s * hw * 0.45 + 0.09, y0 + dh * (0.25 + r * 0.25) + 0.09, z + 0.07], [x + s * hw * 0.45 - 0.09, y0 + dh * (0.25 + r * 0.25) + 0.09, z + 0.07], [0, 0, 1], PAL.gold);
      }
    }
    M.ink = ink;
    // 券石 / 门框
    const fr = C(o.frame || PAL.stoneLight), bw = o.border || Math.max(0.35, w * 0.12);
    if (kind === 'square') {
      M.color = fr;
      for (const s of [-1, 1]) M.box([x + s * (hw + bw / 2), y0 + hh / 2, z + 0.06], [bw, hh, 0.12], null, 'frl');
      M.box([x, y0 + hh + bw / 2, z + 0.06], [w + bw * 2, bw, 0.14], null, 'tfrl');
    } else {
      for (let i = 0; i < arcPts.length - 1; i++) {
        const a = arcPts[i], b = arcPts[i + 1];
        const n0 = [(a[0] - x) / (Math.hypot(a[0] - x, a[1] - y0 - ys) || 1), (a[1] - y0 - ys) / (Math.hypot(a[0] - x, a[1] - y0 - ys) || 1)];
        const n1 = [(b[0] - x) / (Math.hypot(b[0] - x, b[1] - y0 - ys) || 1), (b[1] - y0 - ys) / (Math.hypot(b[0] - x, b[1] - y0 - ys) || 1)];
        const col = i === 3 || i === 4 ? P.shade(fr, 1.1) : P.shade(fr, i % 2 ? 0.94 : 1.02);
        M.quad([a[0], a[1], z + 0.04], [b[0], b[1], z + 0.04], [b[0] + n1[0] * bw, b[1] + n1[1] * bw, z + 0.04], [a[0] + n0[0] * bw, a[1] + n0[1] * bw, z + 0.04], [0, 0, 1], col);
      }
      for (const s of [-1, 1]) M.quad([x + s * hw, y0, z + 0.04], [x + s * (hw + bw), y0, z + 0.04], [x + s * (hw + bw), y0 + ys, z + 0.04], [x + s * hw, y0 + ys, z + 0.04], [0, 0, 1], P.shade(fr, 0.95));
    }
  }
  /** 在收分（bat）的立面上画（立面底边在局部 z = zf，向上内收）：把坐标系转到立面上，fn 里按 z = 0 画 */
  function onFace(K, x, z, bat, fn) {
    K.M.push().translate(x, 0, z).rotate(-Math.atan(bat) / D2R, 0, 0);
    try { fn(); } finally { K.M.pop(); }
  }
  /** 格窗（朝 +Z 的立面上）：夜里亮的窗纸 + 深色窗框横竖棂（贴面，不描墨线） */
  function lattice(K, x, y, z, w, h, o) {
    const M = K.M, ink = M.ink, paper = C((o && o.paper) || '#E9D7AE'), frame = C((o && o.frame) || '#5A3426');
    M.ink = 0;
    M.nightEmission = (o && o.glow) || 0.9;
    M.quad([x - w / 2, y - h / 2, z + 0.02], [x + w / 2, y - h / 2, z + 0.02], [x + w / 2, y + h / 2, z + 0.02], [x - w / 2, y + h / 2, z + 0.02], [0, 0, 1], paper);
    M.nightEmission = 0;
    const b = Math.max(0.08, w * 0.07);
    M.quad([x - b / 2, y - h / 2, z + 0.04], [x + b / 2, y - h / 2, z + 0.04], [x + b / 2, y + h / 2, z + 0.04], [x - b / 2, y + h / 2, z + 0.04], [0, 0, 1], frame);
    if (!(o && o.noCross)) M.quad([x - w / 2, y - b / 2, z + 0.04], [x + w / 2, y - b / 2, z + 0.04], [x + w / 2, y + b / 2, z + 0.04], [x - w / 2, y + b / 2, z + 0.04], [0, 0, 1], frame);
    M.quad([x - w / 2 - b, y - h / 2 - b, z + 0.03], [x + w / 2 + b, y - h / 2 - b, z + 0.03], [x + w / 2 + b, y - h / 2, z + 0.03], [x - w / 2 - b, y - h / 2, z + 0.03], [0, 0, 1], frame);
    M.ink = ink;
  }
  /** 一扇窗（夜里亮，白天是深色的窗洞 / 木百叶）：圆拱或方，朝 +Z */
  function windowGlow(K, x, y, z, w, h, o) {
    const M = K.M, ink = M.ink;
    M.ink = 0;
    M.nightEmission = 0.9;
    const c = C((o && o.color) || '#C8925A');
    M.quad([x - w / 2, y - h / 2, z + 0.02], [x + w / 2, y - h / 2, z + 0.02], [x + w / 2, y + h / 2, z + 0.02], [x - w / 2, y + h / 2, z + 0.02], [0, 0, 1], c);
    if (o && o.arch) for (let i = 0; i < 4; i++) { const a0 = i / 4 * Math.PI, a1 = (i + 1) / 4 * Math.PI; M.triangleFacing([x, y + h / 2, z + 0.02], [x + Math.cos(a0) * w / 2, y + h / 2 + Math.sin(a0) * w / 2, z + 0.02], [x + Math.cos(a1) * w / 2, y + h / 2 + Math.sin(a1) * w / 2, z + 0.02], [0, 0, 1], c); }
    M.nightEmission = 0;
    if (o && o.sill) { M.quad([x - w / 2 - 0.12, y - h / 2 - 0.14, z + 0.04], [x + w / 2 + 0.12, y - h / 2 - 0.14, z + 0.04], [x + w / 2 + 0.12, y - h / 2, z + 0.04], [x - w / 2 - 0.12, y - h / 2, z + 0.04], [0, 0, 1], C(o.sill)); }
    M.ink = ink;
  }
  /** 小灯笼（6 边纸灯笼，夜里亮，金色上下盖）+ 灯位 */
  function lantern(K, x, y, z, o) {
    const M = K.M, r = (o && o.r) || 0.42, hgt = r * 1.5, paper = C((o && o.color) || PAL.hanRed);
    M.push().translate(x, y, z);
    M.color = PAL.gold;
    M.cylinder([0, hgt * 0.5, 0], r * 0.45, r * 0.45, 0.12, 6, false, false, true);
    M.color = paper; M.nightEmission = 0.95; M.emission = 0;
    M.lathe([[r * 0.5, -hgt * 0.5], [r, -hgt * 0.15], [r, hgt * 0.15], [r * 0.5, hgt * 0.5]], 6, false, true, true, [paper, P.shade(paper, 1.05), P.shade(paper, 1.05), paper]);
    M.nightEmission = 0;
    M.color = PAL.gold; M.cylinder([0, -hgt * 0.5 - 0.1, 0], r * 0.42, r * 0.42, 0.1, 6, false, true, false);
    M.pop();
    if (!(o && o.noLamp)) lamp(K, [x, y - 0.2, z + 0.25], { color: (o && o.light) || '#FFB060', range: (o && o.range) || 8, intensity: 1.3, haloSize: r * 2.6 });
  }
  /** 火盆 / 篝火（火焰常亮 —— 夜里更显眼；灯位闪烁） */
  function fire(K, x, y, z, o) {
    const M = K.M, s = (o && o.s) || 1;
    if (!(o && o.pit === false)) {
      M.color = H('#7E776E');
      for (let i = 0; i < 6; i++) { const a = i / 6 * Math.PI * 2; M.box([x + Math.cos(a) * 0.75 * s, y + 0.15 * s, z + Math.sin(a) * 0.75 * s], [0.42 * s, 0.3 * s, 0.42 * s], null, 'tfkrl'); }
    }
    M.color = H('#FFB040'); M.emission = 0.9; M.ink = 0;
    M.cone([x, y + 0.1, z], 0.55 * s, 1.5 * s, 5);
    M.color = H('#FFE08A'); M.cone([x + 0.05, y + 0.1, z + 0.08], 0.3 * s, 0.95 * s, 4);
    M.emission = 0; M.ink = 1;
    lamp(K, [x, y + 1.2 * s, z], { color: '#FF9A40', range: 9 * s, intensity: 1.6, flicker: 0.25, nightOnly: false, haloSize: 1.4 * s });
  }

  // ---------------------------------------------------------- 屋顶 --
  /**
   * 简式瓦顶（民居）：双坡（脊沿 X）或四坡，两层瓦带（下层略暗，带深色台阶面）、正脊、山墙三角；
   * o: { w, d, y, over, rise, pal（ROOF_PAL 键或 { tile, tileAlt, ridge }）, hip, gable（山墙色）, curl（翘角，汉式） }
   */
  function tileRoof(K, x, z, o) {
    const M = K.M, pal = typeof o.pal === 'string' ? ROOF[o.pal] : o.pal || ROOF.slate;
    const W = o.w / 2 + (o.over != null ? o.over : 0.7), D = o.d / 2 + (o.over != null ? o.over : 0.7), y = o.y, rise = o.rise != null ? o.rise : o.d * 0.42;
    const yR = y + rise, lift = o.curl ? 0.35 : 0, hip = !!o.hip, Lr = hip ? Math.max(0.2, W - D * 0.9) : W;
    const yE = y - 0.12;
    // 每坡两层：下层（檐）与上层；层脚抬起，露出深色台阶
    for (const f of [1, -1]) {
      const P0 = u => [x + lerp(-W, W, u), yE + lift * Math.pow(Math.abs(u * 2 - 1), 3), z + f * D];
      const Pm = u => [x + lerp(-W, W, u) * lerp(1, Lr / W, 0.5), lerp(yE, yR, 0.5) - rise * 0.04, z + f * D * 0.5];
      const P1 = u => [x + lerp(-Lr, Lr, u), yR, z];
      const a = P0(0), b = P0(1), c = Pm(1), d = Pm(0), e = P1(1), g = P1(0);
      M.quad(a, b, c, d, [0, 1, f], P.shade(pal.tileAlt || pal.tile, 0.97 + 0.06 * K.r()));
      const st = 0.16;
      M.quad([d[0], d[1] + st, d[2]], [c[0], c[1] + st, c[2]], e, g, [0, 1, f], P.shade(pal.tile, 0.98 + 0.06 * K.r()));
      M.ink = 0.4; M.quad(d, c, [c[0], c[1] + st, c[2]], [d[0], d[1] + st, d[2]], [0, -0.3, f], P.shade(pal.tile, 0.62)); M.ink = 1;
      if (o.fascia) { M.ink = 0.5; M.quad([a[0], a[1] - 0.22, a[2]], [b[0], b[1] - 0.22, b[2]], b, a, [0, 0, f], C(o.fascia)); M.ink = 1; }
    }
    if (hip) {
      for (const s of [-1, 1]) {
        const e0 = [x + s * W, yE + lift, z - D], e1 = [x + s * W, yE + lift, z + D], tp = [x + s * Lr, yR, z];
        M.triangleFacing(e0, e1, tp, [s, 0.7, 0], P.shade(pal.tile, 0.92));
      }
    } else {
      const gc = C(o.gable || PAL.plaster);
      for (const s of [-1, 1]) {
        M.triangleFacing([x + s * (W - 0.05), yE, z - D * 0.98], [x + s * (W - 0.05), yR - 0.05, z], [x + s * (W - 0.05), yE, z + D * 0.98], [s, 0, 0], P.shade(gc, 0.94));
      }
    }
    // 正脊
    M.color = pal.ridge;
    M.box([x, yR + 0.12, z], [Lr * 2 + 0.3, 0.34, 0.42], P.shade(pal.ridge, 1.15), 'tfkrl');
    if (o.curl) for (const s of [-1, 1]) M.spike([x + s * (Lr + 0.1), yR + 0.2, z], [s * 0.6, 1, 0], 0.17, 0.75, 4);
    return { ridgeY: yR + 0.3, eaveY: yE };
  }
  /**
   * 汉式大屋顶：SG.LPProps.parts.hanRoof（试点城门楼的屋顶，歇山 / 庑殿 / 悬山 / 攒尖），中心 (x, z)，檐口高 o.y。
   * 地图上的城是缩微模型：屋顶按 1/k 的尺寸建再放大 k 倍（缺省 k = 1.7），瓦垄、脊兽、鸱吻随之加粗，远看仍清楚；
   * 不画檐下彩画（俯视看不到），举折减半（避免檐下穿出）。
   */
  function hanRoof(K, x, z, o) {
    if (!PP || !PP.hanRoof) return tileRoof(K, x, z, Object.assign({ hip: o.style !== 'gable', pal: o.palette || 'slate', curl: true }, o));
    const k = o.k || 1.7, M = K.M, ink = M.ink;
    M.push().translate(x, o.y, z);
    if (o.yaw) M.rotate(0, o.yaw, 0);
    M.scale(k);
    M.ink = o.ink != null ? o.ink : 0.6;
    const q = Object.assign({ soffit: false }, o, { cx: 0, cz: 0, yaw: undefined, k: undefined, y: 0, w: o.w / k, d: o.d / k });
    for (const key of ['over', 'rise', 'lift', 'sag']) if (o[key] != null) q[key] = o[key] / k;
    if (q.over == null) q.over = 0.9;
    const D = q.d / 2 + q.over, rise = q.rise != null ? q.rise : D * 1.05;
    if (q.sag == null) q.sag = rise * 0.05;
    const r = PP.hanRoof(M, q);
    M.pop();
    M.ink = ink;
    return { ridgeY: o.y + r.ridgeY * k, eaveY: o.y, W: r.W * k, D: r.D * k };
  }
  /** 茅草顶：圆锥（圆屋）/ 面包形长顶（长屋）—— 厚厚的草檐一圈深色，顶部亮 */
  function thatchCone(K, x, y, z, r, h, o) {
    const M = K.M, straw = C((o && o.color) || '#C8A868'), dark = P.shade(P.mix(straw, H('#8E6A3A'), 0.55), 0.85), n = (o && o.sides) || 8;
    M.push().translate(x, y, z);
    M.lathe([[r * 0.96, -0.32], [r * 1.02, 0.02], [r * 0.62, h * 0.42], [r * 0.2, h * 0.86], [0, h]], n, false, true, false, [dark, P.shade(straw, 0.92), straw, P.shade(straw, 1.08), P.shade(straw, 1.12)], (K.seed * 53) % 360);
    M.pop();
  }
  function thatchLoaf(K, x, y, z, w, d, h, o) {
    // 面包形草顶（脊沿 X），两端收圆；loft 的截面是上凸的半椭圆
    const M = K.M, straw = C((o && o.color) || '#C8A868'), eave = P.shade(P.mix(straw, H('#9A7448'), 0.6), 0.9);
    const hw = w / 2, hd = d / 2, xs = [-1, -0.82, -0.45, 0.45, 0.82, 1], ss = [0.25, 0.75, 1, 1, 0.75, 0.25];
    const rings = xs.map((u, i) => { const s = ss[i], r = []; r.push([x + u * hw, y - 0.3, z - hd * s]); for (let k = 0; k <= 6; k++) { const th = Math.PI * k / 6; r.push([x + u * hw, y + (h * Math.pow(Math.sin(th), 1.3)) * s, z - hd * Math.cos(th) * s]); } r.push([x + u * hw, y - 0.3, z + hd * s]); return r; });
    M.loft(rings, true, true, n => (n[1] < -0.2 ? P.shade(eave, 0.8) : n[1] < 0.4 ? eave : P.mix(straw, P.shade(straw, 1.12), (n[1] - 0.4) / 0.6)));
  }

  // ---------------------------------------------------------- 树 --
  let FOLIAGE = null;
  function foliage(kind) {
    if (!FOLIAGE) FOLIAGE = {};
    if (FOLIAGE[kind]) return FOLIAGE[kind];
    const base = { huai: '#376634', oak: '#4E7A3A', poplar: '#5C8A3A', cypress: '#3F6446', cedar: '#3A5C40', pine: '#3E6446', palm: '#4E8A3C', olive: '#7A8E5A', fig: '#3F7A3A', bodhi: '#3F7A3A', willow: '#6E9A4E' }[kind] || '#4E7A3A';
    const f = LP.foliage(1, H(base), 7, { topVal: 0.96, coolMix: 0.58 });
    return (FOLIAGE[kind] = f);
  }
  /** 小树（地图尺度）：多边形树干 + 1–3 个软团树冠（canopyRamp：上暖下青）；kind: huai / oak / poplar / cypress / olive / bodhi */
  function tree(K, x, z, s, kind, y0) {
    const M = K.M, f = foliage(kind || 'huai'), r = K.r;
    y0 = y0 || 0;
    const bark = H(kind === 'olive' ? '#8A7E6C' : '#8A6644');
    M.color = bark;
    if (kind === 'poplar' || kind === 'cypress') {
      const h = (kind === 'poplar' ? 9 : 7.5) * s, w = (kind === 'poplar' ? 1.5 : 1.15) * s;
      M.cylinder([x, y0, z], 0.32 * s, 0.24 * s, 1.6 * s, 5, false, false, false);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, y0 + 1.2 * s, y0 + h, { coolMix: 0.58 });
      M.wind = 0.25; M.windGradient = true; M.windY0 = y0 + 1; M.windY1 = y0 + h;
      M.softLump([x, y0 + h * 0.52, z], [w, h * 0.46, w], 0, (x * 31 + z * 17) % 360, ramp, 0.08);
      M.wind = 0; M.windGradient = false;
      return;
    }
    const h = 3.0 * s, cr = (kind === 'olive' ? 2.3 : kind === 'bodhi' ? 3.4 : 2.7) * s;
    M.segment([x, y0, z], [x + 0.15 * s, y0 + h, z - 0.1 * s], 0.42 * s, 0.26 * s, 5, false, false);
    const ramp = LP.canopyRamp(f.top, f.side, f.bottom, y0 + h - cr * 0.4, y0 + h + cr * 1.6, { coolMix: 0.58 });
    M.wind = 0.25; M.windGradient = true; M.windY0 = y0 + h * 0.8; M.windY1 = y0 + h + cr * 1.6;
    M.softLump([x, y0 + h + cr * 0.55, z], [cr, cr * 0.82, cr * 0.95], 0, (x * 31 + z * 17) % 360, ramp, 0.12);
    if (s > 0.8 && kind !== 'olive') {
      M.softLump([x + cr * 0.62, y0 + h + cr * 0.25, z + cr * 0.2 * (r() - 0.5)], [cr * 0.6, cr * 0.5, cr * 0.6], 0, (x * 13 + z) % 360, ramp, 0.12);
      M.softLump([x - cr * 0.55, y0 + h + cr * 0.4, z - cr * 0.15], [cr * 0.55, cr * 0.48, cr * 0.55], 0, (x * 7 + z * 3) % 360, ramp, 0.12);
    }
    M.wind = 0; M.windGradient = false;
  }
  /** 针叶树（雪松 / 松）：2–3 层平面着色的圆锥 */
  function conifer(K, x, z, s, kind, y0) {
    const M = K.M, f = foliage(kind || 'pine');
    y0 = y0 || 0;
    M.color = H('#6B4C3B');
    M.cylinder([x, y0, z], 0.3 * s, 0.22 * s, 1.6 * s, 5, false, false, false);
    const tiers = kind === 'cedar' ? [[1.2, 3.4, 2.4], [3.0, 2.6, 2.2], [4.6, 1.6, 2.0]] : [[1.3, 2.4, 3.2], [3.0, 1.8, 2.8], [4.6, 1.15, 2.4]];
    M.wind = 0.2; M.windGradient = true; M.windY0 = y0 + 1; M.windY1 = y0 + 7 * s;
    tiers.forEach(([y, r, h], i) => {
      const ramp = [P.shade(f.bottom, 0.9), f.side, P.mix(f.side, f.top, 0.6 + i * 0.15)];
      M.color = f.side;
      M.lathe([[r * s, (y - 0.1) * s + y0], [r * s * 0.92, (y + 0.15) * s + y0], [0, (y + h) * s + y0]], 6, false, true, false, ramp, i * 30 + x * 7);
    });
    M.wind = 0; M.windGradient = false;
  }
  /** 棕榈（椰枣 / 椰子）：弯干 + 6–7 片下垂的羽叶（闭合的薄条，墨线细） */
  function palm(K, x, z, s, y0, seed) {
    const M = K.M, f = foliage('palm'), r = rng(seed || (x * 97 + z * 31));
    y0 = y0 || 0;
    const h = (6.2 + r() * 1.6) * s, lean = (r() - 0.5) * 1.4 * s;
    const top = [x + lean, y0 + h, z + lean * 0.3];
    M.color = H('#9A7A52');
    M.segment([x, y0, z], [x + lean * 0.3, y0 + h * 0.5, z + lean * 0.1], 0.3 * s, 0.25 * s, 5, false, false);
    M.segment([x + lean * 0.3, y0 + h * 0.5, z + lean * 0.1], top, 0.25 * s, 0.2 * s, 5, false, false);
    const nf = 7, ink = M.ink;
    M.ink = 0.55; M.wind = 0.35;
    for (let i = 0; i < nf; i++) {
      const a = i / nf * Math.PI * 2 + r() * 0.4, ca = Math.cos(a), sa = Math.sin(a), L = (3.0 + r() * 0.6) * s, w = 0.55 * s;
      const p1 = [top[0] + ca * L * 0.55, top[1] + 0.35 * s, top[2] + sa * L * 0.55], p2 = [top[0] + ca * L, top[1] - 1.2 * s, top[2] + sa * L];
      const side = [-sa * w, 0, ca * w], col = P.shade(i % 2 ? f.side : P.mix(f.side, f.top, 0.5), 0.95 + 0.1 * r());
      M.quad(top, [p1[0] + side[0], p1[1], p1[2] + side[2]], p1, [p1[0] - side[0], p1[1], p1[2] - side[2]], [0, 1, 0], col);
      M.quad([p1[0] - side[0], p1[1], p1[2] - side[2]], p1, [p1[0] + side[0], p1[1], p1[2] + side[2]], p2, [ca * 0.3, 1, sa * 0.3], P.shade(col, 0.9));
    }
    M.ink = ink; M.wind = 0;
    M.color = H('#7A6A30'); M.sphere([top[0], top[1] - 0.15 * s, top[2]], [0.4 * s, 0.35 * s, 0.4 * s], 5, 3, false);
  }
  /** 灌木丛（软团，贴地） */
  function bush(K, x, z, s, kind, y0) {
    const f = foliage(kind || 'oak'), y = y0 || 0;
    const ramp = LP.canopyRamp(f.top, f.side, f.bottom, y, y + 1.8 * s, { coolMix: 0.58 });
    K.M.softLump([x, y + 0.6 * s, z], [1.3 * s, 0.9 * s, 1.1 * s], 0, (x * 7 + z * 11) % 360, ramp, 0.14);
  }

  // ---------------------------------------------------------- 旗帜 --
  /** 竿旗 / 三角旗 / 马尾纛（tail）/ 罗马军旗（vexillum）：随风摆（旗面 wind） */
  function standard(K, x, z, h, col, kind, y0) {
    const M = K.M, y = y0 || 0, c = C(col);
    M.color = H('#5A4026');
    M.segment([x, y, z], [x, y + h, z], 0.13, 0.1, 5, false, true);
    if (kind === 'tail') return tug(K, x, z, h, col, y0);
    M.wind = 0.4; M.windGradient = true; M.windY0 = y + h - 2.6; M.windY1 = y + h;
    if (kind === 'vexillum') {
      M.color = H('#5A4026'); M.box([x, y + h - 0.4, z], [2.0, 0.16, 0.16]);
      M.color = c; M.quad([x - 0.9, y + h - 0.5, z + 0.06], [x + 0.9, y + h - 0.5, z + 0.06], [x + 0.9, y + h - 2.6, z + 0.06], [x - 0.9, y + h - 2.6, z + 0.06], [0, 0, 1]);
      M.quad([x - 0.9, y + h - 0.5, z - 0.06], [x + 0.9, y + h - 0.5, z - 0.06], [x + 0.9, y + h - 2.6, z - 0.06], [x - 0.9, y + h - 2.6, z - 0.06], [0, 0, -1]);
      M.color = PAL.gold; M.cone([x, y + h, z], 0.3, 0.7, 4);
    } else {
      M.color = c;
      const a = [x + 0.1, y + h - 0.1, z], b = [x + 2.4, y + h - 0.5, z], d = [x + 0.1, y + h - 1.7, z];
      M.triangleFacing(a, b, d, [0, 0, 1]); M.triangleFacing(a, d, b, [0, 0, -1]);
    }
    M.wind = 0; M.windGradient = false;
  }
  function tug(K, x, z, h, col, y0) {
    // 马尾纛：竿顶的金色尖与一束下垂的马尾（lathe 平移到竿顶）
    const M = K.M, y = y0 || 0, c = C(col);
    M.color = H('#5A4026'); M.segment([x, y, z], [x, y + h, z], 0.13, 0.1, 5, false, true);
    M.push().translate(x, y + h - 0.35, z);
    M.color = c; M.wind = 0.3; M.windGradient = true; M.windY0 = y + h - 2.6; M.windY1 = y + h - 0.4;
    M.lathe([[0.12, 0], [0.75, -0.8], [0.5, -1.9], [0.08, -2.3]], 6, false, true, true, [c, P.shade(c, 1.05), P.shade(c, 0.92), P.shade(c, 0.85)]);
    M.wind = 0; M.windGradient = false;
    M.color = PAL.gold; M.cone([0, 0.1, 0], 0.25, 0.75, 5);
    M.pop();
  }

  // ---------------------------------------------------------- 房屋 --
  /** 墙体盒子（不画底面、可不画顶面）：勒脚石带 + 墙身；返回墙顶 y */
  function body(K, x, y0, z, w, d, h, col, o) {
    const M = K.M, plinthH = o && o.plinth != null ? o.plinth : 0.45, c = C(col);
    if (plinthH > 0) { M.color = C((o && o.plinthColor) || PAL.stone); M.box([x, y0 + plinthH / 2, z], [w + 0.3, plinthH, d + 0.3], P.shade(C((o && o.plinthColor) || PAL.stone), 1.08), 'tfkrl'); }
    M.color = c;
    M.box([x, y0 + plinthH + (h - plinthH) / 2, z], [w, h - plinthH, d], P.shade(c, 1.04), (o && o.top) ? 'tfkrl' : 'fkrl');
    return y0 + h;
  }
  /** 汉式坊屋：白墙、木柱、格窗（夜亮）、门、灰瓦顶（双坡或四坡，汉式翘角） */
  function hanHouse(K, x, z, w, d, h, o) {
    const M = K.M, plaster = C((o && o.plaster) || (K.r() < 0.5 ? PAL.plaster : '#EDE2CC')), timber = PAL.timber;
    body(K, x, 0, z, w, d, h, plaster);
    // 前檐木枋 + 两根角柱（贴面）
    M.ink = 0;
    M.quad([x - w / 2, h - 0.45, z + d / 2 + 0.02], [x + w / 2, h - 0.45, z + d / 2 + 0.02], [x + w / 2, h, z + d / 2 + 0.02], [x - w / 2, h, z + d / 2 + 0.02], [0, 0, 1], timber);
    for (const s of [-1, 1]) M.quad([x + s * w / 2 - (s > 0 ? 0.32 : 0), 0.45, z + d / 2 + 0.02], [x + s * w / 2 + (s < 0 ? 0.32 : 0), 0.45, z + d / 2 + 0.02], [x + s * w / 2 + (s < 0 ? 0.32 : 0), h, z + d / 2 + 0.02], [x + s * w / 2 - (s > 0 ? 0.32 : 0), h, z + d / 2 + 0.02], [0, 0, 1], timber);
    // 门（深色木）
    M.quad([x - 0.6, 0.45, z + d / 2 + 0.03], [x + 0.6, 0.45, z + d / 2 + 0.03], [x + 0.6, Math.min(h - 0.6, 2.6), z + d / 2 + 0.03], [x - 0.6, Math.min(h - 0.6, 2.6), z + d / 2 + 0.03], [0, 0, 1], H('#6E4632'));
    M.ink = 1;
    if (w > 4.2) for (const s of [-1, 1]) lattice(K, x + s * w * 0.3, Math.min(h - 1.2, 2.2), z + d / 2, Math.min(1.2, w * 0.18), 1.0, {});
    const r = tileRoof(K, x, z, { w, d, y: h, over: 0.8, rise: d * 0.42, pal: (o && o.roof) || 'slate', hip: o && o.hip, curl: true, gable: plaster, fascia: o && o.fascia });
    return r;
  }

  /** 汉式殿 / 楼（城楼、角楼、主殿）：台面、朱柱、格窗前檐、后墙、汉式大屋顶（parts.hanRoof）+ 檐下灯笼 */
  function hanHall(K, x, y0, z, w, d, h, o) {
    const M = K.M, red = PAL.hanRed, redD = PAL.hanRedDark, wall = C(o.wall || PAL.plaster);
    // 后墙与山墙（实墙）、前檐退进的格窗墙
    M.color = wall; M.box([x, y0 + h / 2, z - 0.2], [w - 0.4, h, d - 0.8], P.shade(wall, 1.04), 'krl');
    M.color = redD; M.box([x, y0 + h - 0.35, z], [w, 0.7, d], null, 'fkrl');   // 额枋（朱）
    const n = o.bays || Math.max(3, Math.round(w / 3.2)), zf = z + d / 2;
    // 前檐格窗墙（退进 0.6）
    M.color = wall; M.quad([x - w / 2 + 0.2, y0, zf - 0.6], [x + w / 2 - 0.2, y0, zf - 0.6], [x + w / 2 - 0.2, y0 + h - 0.7, zf - 0.6], [x - w / 2 + 0.2, y0 + h - 0.7, zf - 0.6], [0, 0, 1], P.shade(wall, 0.92));
    for (let i = 0; i < n; i++) {
      const cx = x - w / 2 + (i + 0.5) * w / n, bw = w / n;
      if (o.door && i === (n >> 1)) { M.ink = 0; M.quad([cx - bw * 0.32, y0, zf - 0.56], [cx + bw * 0.32, y0, zf - 0.56], [cx + bw * 0.32, y0 + h * 0.72, zf - 0.56], [cx - bw * 0.32, y0 + h * 0.72, zf - 0.56], [0, 0, 1], H('#7A2E22')); M.ink = 1; continue; }
      lattice(K, cx, y0 + h * 0.48, zf - 0.6, bw * 0.66, h * 0.58, {});
    }
    // 朱柱（前檐 n+1 根，后檐两角）
    M.color = red;
    for (let i = 0; i <= n; i++) { const cx = x - w / 2 + i * w / n; M.cylinder([cx, y0, zf - 0.15], 0.26, 0.24, h - 0.7, 6, false, false, false); }
    if (o.rail) {
      // 栏杆（城楼前廊）
      M.color = red; M.box([x, y0 + 0.75, zf + 0.25], [w + 0.4, 0.16, 0.16], null, 'tfkrl'); M.box([x, y0 + 0.35, zf + 0.25], [w + 0.4, 0.12, 0.12], null, 'fk');
    }
    const roof = hanRoof(K, x, z, { style: o.style || 'xieshan', w, d, y: y0 + h, over: o.over || 1.5, detail: o.detail || 'mid', palette: o.palette || 'slate', rise: o.rise, lift: o.lift != null ? o.lift : 0.45, fascia: PAL.hanRed });
    if (o.lanterns) for (const s of [-1, 1]) lantern(K, x + s * (w / 2 - 0.4), y0 + h - 1.05, zf + 0.7, { r: 0.44 });
    return roof;
  }

  /**
   * 汉式城门（试点城门楼的地图版）：夯土城台（分层、勒脚、券门、红门门钉、匾）、台上女墙、重檐 / 单檐歇山城楼（朱柱格窗、栏杆）、
   * 两盏红灯笼。(x, z) = 城台中心，yaw = 朝向（0 = 朝南 +Z）
   */
  function hanGate(K, x, z, yaw, o) {
    at(K, x, 0, z, yaw, () => {
      const w = o.w, d = o.d, hb = o.h, M = K.M;
      const t = block(K, 0, 0, 0, w, d, hb, { kind: o.kind || 'rammed', bat: 0.1, ys: [1.1, hb * 0.38, hb * 0.7], faces: 'fkrl' });
      onFace(K, 0, d / 2, 0.1, () => {
        const ah = o.ah || 5.4;
        archway(K, 0, 0, 0, { w: o.aw || 3.6, h: ah, kind: 'round', door: '#A83E2E', frame: PAL.stoneLight, studs: true, border: 0.45 });
        // 匾（深色木板金框、浅色匾心）
        M.color = H('#3E2A22'); M.box([0, ah + 0.95, 0.08], [2.2, 0.8, 0.16], null, 'tfrl');
        M.ink = 0; M.quad([-0.92, ah + 0.68, 0.17], [0.92, ah + 0.68, 0.17], [0.92, ah + 1.22, 0.17], [-0.92, ah + 1.22, 0.17], [0, 0, 1], H('#E8D9A8')); M.ink = 1;
      });
      crown(K, 0, hb, 0, t.tw, t.td, { kind: o.kind || 'rammed', ph: 0.8, pt: 0.6, skip: 'k', merlon: { w: 1.2, h: 0.8, step: 3.0 } });
      // 城楼
      const pw = o.pw || w * 0.72, pd = o.pd || d * 0.62;
      M.color = PAL.stone; M.box([0, hb + 0.25, 0], [pw + 1.2, 0.5, pd + 1.2], P.shade(PAL.stone, 1.08), 'tfkrl');
      if (o.double) {
        hanHall(K, 0, hb + 0.5, 0, pw, pd, 4.2, { style: 'hip', detail: 'low', over: 1.7, rail: true, lanterns: false, palette: o.palette, rise: 1.6 });
        M.color = PAL.hanRedDark; M.box([0, hb + 4.7 + 1.4, 0], [pw * 0.72, 2.8, pd * 0.72], null, 'fkrl');
        lattice(K, 0, hb + 4.7 + 1.3, pd * 0.36, pw * 0.42, 1.4, {});
        hanRoof(K, 0, 0, { style: 'xieshan', w: pw * 0.72, d: pd * 0.72, y: hb + 4.7 + 2.8, over: 1.5, detail: o.detail || 'mid', palette: o.palette || 'slate', lift: 0.5, fascia: PAL.hanRed });
        for (const s of [-1, 1]) lantern(K, s * (pw / 2 - 0.4), hb + 0.5 + 3.1, pd / 2 + 0.75, { r: 0.46 });
      } else {
        hanHall(K, 0, hb + 0.5, 0, pw, pd, o.ph || 4.0, { style: 'xieshan', detail: o.detail || 'mid', over: 1.5, rail: true, lanterns: true, palette: o.palette });
      }
    });
  }

  // ============================================================== 各文化 --
  const B = {};

  // ---------------------------------------------------------------- 汉 --
  // 夯土方城（四面城墙、马面、垛口），南门重檐城楼，东西门楼，三座角楼（东北角留给旗），三层台基上的庑殿主殿（都城金顶重檐、阙楼），
  // 白墙灰瓦的坊屋，几棵槐树；夜里城楼、主殿的格窗与灯笼亮起
  B.han = function (K, o) {
    const r = U, t = 4.2, h = 8.2, cap = o.cap, big = o.tier >= 1;
    const v = o.variant;
    pad(K, octo(23.4, 2.4), { top: '#BCA780', side: '#8C7556', inner: { pts: octo(18.6, 1.8), color: '#C8B48E' } });
    mark(K, 'pad');
    // 城墙（南墙城门段、东西墙门段不画垛口）
    const W = { h, t, bat: 0.11, kind: 'rammed', plinth: 1.1, courses: [2.6, 4.2, 5.8], panel: [3, 6] };
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 5.6, r + 5.6]], bastions: [{ u: r * 0.42, w: 4.6, d: 1.8 }, { u: r * 1.58, w: 4.6, d: 1.8 }] }));
    wallRun(K, -r, -r, r, -r, Object.assign({}, W, { skip: [[r * 1.6, r * 2]], bastions: [{ u: r * 0.6, w: 4.6, d: 1.8 }].concat(big ? [{ u: r * 1.15, w: 4.6, d: 1.8 }] : []) }));
    wallRun(K, -r, -r, -r, r, Object.assign({}, W, { skip: [[r - 3.2, r + 3.2]], bastions: [{ u: r * 0.45, w: 4.6, d: 1.8 }, { u: r * 1.55, w: 4.6, d: 1.8 }] }));
    wallRun(K, r, -r, r, r, Object.assign({}, W, { skip: [[r - 3.2, r + 3.2], [0, r * 0.4]], bastions: [{ u: r * 1.55, w: 4.6, d: 1.8 }] }));
    mark(K, 'walls');
    // 角楼（西南、东南、西北）；东北角为矮台（旗帜）
    for (const [cx, cz] of [[-1, 1], [1, 1], [-1, -1]]) {
      block(K, cx * r, 0, cz * r, 6.4, 6.4, h + 0.6, { kind: 'rammed', bat: 0.1, ys: [1.1, 4.8] });
      M_hall_small(K, cx * r, h + 0.6, cz * r, 4.2, 4.2, 2.6, cap);
    }
    block(K, r, 0, -r, 5.2, 5.2, h + 0.2, { kind: 'rammed', bat: 0.1, ys: [1.1, 3.8, 6.4] });
    crown(K, r, h + 0.2, -r, 5.2 - 0.2 * 2.2, 5.2 - 0.2 * 2.2, { kind: 'rammed', ph: 0.7 });
    mark(K, 'towers');
    // 南门（都城重檐），东西门
    hanGate(K, 0, r + 0.6, 0, { w: 14, d: 7.4, h: h + 1.6, aw: 3.6, ah: 5.6, pw: 10.6, pd: 5.2, double: cap || big, palette: 'slate' });
    for (const s of [-1, 1]) {
      at(K, s * (r + 0.4), 0, 0, s * 90, () => {
        const tb = block(K, 0, 0, 0, 9, 6.2, h + 1.0, { kind: 'rammed', bat: 0.1, ys: [1.1, 4.8] });
        onFace(K, 0, 3.1, 0.1, () => archway(K, 0, 0, 0, { w: 3.0, h: 4.6, kind: 'round', door: '#A83E2E', frame: PAL.stoneLight, studs: false, border: 0.4 }));
        crown(K, 0, h + 1.0, 0, tb.tw, tb.td, { kind: 'rammed', ph: 0.7, skip: 'klr', merlon: { w: 1.1, h: 0.75, step: 3.0 } });
        M_hall_small(K, 0, h + 1.0, 0, 5.6, 3.6, 2.8, false, true);
      });
    }
    mark(K, 'gates');
    // 主殿：三层台基 + 殿 + 庑殿顶（都城：金顶重檐）
    const hz = -5.5, k = cap ? 1.15 : big ? 1.06 : 1;
    let y = 0;
    for (const [w, d, hh] of [[17, 12, 1.0], [14.4, 10, 0.9], [12, 8.2, 0.9]]) {
      K.M.color = H('#D8D0BC'); K.M.box([0, y + hh / 2, hz], [w * k, hh, d * k], H('#E6DFCC'), 'tfkrl'); y += hh;
    }
    K.M.color = H('#CFC6B0');
    for (let i = 0; i < 3; i++) { const sh0 = 0.93 * (i + 1); K.M.box([0, sh0 / 2, hz + 6.0 * k + 0.9 * (2 - i) + 0.45], [3.4, sh0, 0.9], H('#E2DACA'), 'tfrl'); }   // 前阶
    const hw = 10.4 * k, hd = 6.0 * k, hh = 4.6;
    if (cap) {
      hanHall(K, 0, y, hz, hw, hd, hh, { style: 'hip', detail: 'low', over: 1.9, palette: 'gold', door: true, rise: 2.0, lanterns: true });
      K.M.color = PAL.hanRedDark; K.M.box([0, y + hh + 1.5, hz], [hw * 0.74, 3.0, hd * 0.72], null, 'fkrl');
      lattice(K, 0, y + hh + 1.5, hz + hd * 0.36, hw * 0.4, 1.5, {});
      hanRoof(K, 0, hz, { style: 'hip', w: hw * 0.74, d: hd * 0.72, y: y + hh + 3.0, over: 1.6, detail: 'mid', palette: 'gold', lift: 0.5, fascia: PAL.hanRed });
      // 阙楼一对
      for (const s of [-1, 1]) {
        const qx = s * 6.5, qz = 7.5;
        block(K, qx, 0, qz, 2.8, 2.8, 5.2, { kind: 'stone', ys: [0.8, 2.8], bat: 0.05 });
        hanRoof(K, qx, qz, { style: 'hip', w: 2.8, d: 2.8, y: 5.2, over: 0.8, detail: 'low', palette: 'slate', lift: 0.3, ornaments: false, soffit: false });
        K.M.color = H('#E6DECA'); K.M.box([qx, 6.9, qz], [1.8, 1.4, 1.8], null, 'fkrl');
        hanRoof(K, qx, qz, { style: 'pyramid', w: 1.8, d: 1.8, y: 7.6, over: 0.6, detail: 'low', palette: 'slate', lift: 0.25, finialK: 0.6 });
      }
    } else {
      hanHall(K, 0, y, hz, hw, hd, hh, { style: 'hip', detail: 'mid', over: 1.8, palette: 'slate', door: true, lanterns: true });
    }
    mark(K, 'hall');
    // 坊屋（南半城与两侧，留出中轴大街）
    const houses = [[-11, 9.5, 6.4, 4.4], [11, 9.5, 6.4, 4.4], [-11.5, 2.5, 5.6, 4.2], [11.5, 2.5, 5.6, 4.2], [-13, -6, 4.6, 6.0, 1], [13, -9, 4.6, 5.2, 1]];
    if (big || cap) houses.push([-5.5, 12.5, 4.6, 3.6], [5.5, 12.5, 4.6, 3.6], [-12.5, -13.5, 5.2, 3.8]);
    houses.forEach(([x, z, w, d, side], i) => {
      const hh2 = 3.4 + ((i + v) % 3) * 0.35;
      if (side) at(K, x, 0, z, x < 0 ? 90 : -90, () => hanHouse(K, 0, 0, d, w, hh2, { hip: (i + v) % 2 === 0 }));
      else hanHouse(K, x + (v ? 0.6 : 0), z, w, d, hh2, { hip: (i + v) % 3 === 0 });
    });
    mark(K, 'houses');
    // 槐树
    const trees = v ? [[-6.5, -13, 0.85], [7.5, -14, 0.8], [-15.5, 13.5, 0.7]] : [[-7, -13.5, 0.8], [7, -13.5, 0.85], [15.5, 14, 0.7]];
    for (const [x, z, s] of trees) tree(K, x, z, s, 'huai');
  };
  // 角楼 / 东西门楼上的小楼：朱柱红墙 + 灰瓦四坡顶（低细节）
  function M_hall_small(K, x, y0, z, w, d, h, gold, gateLike) {
    const M = K.M;
    M.color = PAL.stone; M.box([x, y0 + 0.2, z], [w + 1.0, 0.4, d + 1.0], P.shade(PAL.stone, 1.08), 'tfkrl');
    M.color = PAL.hanRedDark; M.box([x, y0 + 0.4 + h / 2, z], [w, h, d], null, 'fkrl');
    lattice(K, x, y0 + 0.4 + h * 0.5, z + d / 2, w * 0.5, h * 0.55, {});
    if (gateLike) { M.color = PAL.hanRed; for (const s of [-1, 1]) M.cylinder([x + s * (w / 2 + 0.05), y0 + 0.4, z + d / 2 + 0.05], 0.22, 0.2, h, 6, false, false, false); }
    hanRoof(K, x, z, { style: 'hip', w, d, y: y0 + 0.4 + h, over: 1.2, detail: 'low', palette: gold ? 'gold' : 'slate', lift: 0.4, fascia: PAL.hanRed });
  }

  // ============================================================== 远景 --
  const F = {};
  // 远景的简化构件：方墙（只有外壁 + 顶面）、方块、坡顶
  function farWalls(K, r, h, t, pal, o) {
    const M = K.M, pw = walls()[pal];
    for (const [ax, az, bx, bz] of [[-r, r, r, r], [r, -r, -r, -r], [-r, -r, -r, r], [r, r, r, -r]]) {
      const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz), yaw = Math.atan2(-dz / L, dx / L) / D2R;
      M.push().translate((ax + bx) / 2, 0, (az + bz) / 2).rotate(0, yaw, 0);
      M.quadColors([-L / 2, 0, t / 2], [L / 2, 0, t / 2], [L / 2, h, t / 2 - 0.6], [-L / 2, h, t / 2 - 0.6], [0, 0.1, 1], [pw.plinth, pw.plinth, pw.a, pw.a]);
      M.quad([-L / 2, h, -t / 2], [L / 2, h, -t / 2], [L / 2, h, t / 2 - 0.6], [-L / 2, h, t / 2 - 0.6], [0, 1, 0], pw.walk);
      M.quad([-L / 2, 0, -t / 2], [-L / 2, h, -t / 2], [L / 2, h, -t / 2], [L / 2, 0, -t / 2], [0, 0, -1], pw.inner);
      if (!(o && o.noPar)) { M.color = pw.par; M.box([0, h + 0.5, t / 2 - 0.9], [L, 1.0, 0.6], null, 'tfk'); }
      M.pop();
    }
  }
  F.han = function (K, o) {
    const r = U, h = 8.2, M = K.M;
    pad(K, octo(23.4, 2.4), { top: '#BCA780', side: '#8C7556', inner: { pts: octo(18.6, 1.8), color: '#C8B48E' }, bevel: 1.2 });
    farWalls(K, r, h, 4.2, 'rammed');
    for (const [cx, cz] of [[-1, 1], [1, 1], [-1, -1]]) {
      M.color = walls().rammed.b; M.box([cx * r, (h + 0.6) / 2, cz * r], [6.4, h + 0.6, 6.4], walls().rammed.walk, 'tfkrl');
      M.color = PAL.hanRedDark; M.box([cx * r, h + 1.8, cz * r], [4, 2.4, 4], null, 'fkrl');
      farRoof(K, cx * r, h + 3.0, cz * r, 6.4, 6.4, 2.2, ROOF.slate, 'pyramid');
    }
    M.color = walls().rammed.a; M.box([0, (h + 1.6) / 2, r + 0.6], [14, h + 1.6, 7.4], walls().rammed.walk, 'tfkrl');
    M.ink = 0; M.color = H('#3A2E28'); M.quad([-1.8, 0, r + 4.32], [1.8, 0, r + 4.32], [1.8, 5.4, r + 4.32], [-1.8, 5.4, r + 4.32], [0, 0, 1]); M.ink = 1;
    M.color = PAL.hanRed; M.box([0, h + 1.6 + 2.0, r + 0.6], [10.6, 4.0, 5.2], null, 'fkrl');
    farRoof(K, 0, h + 5.6, r + 0.6, 13.6, 8.2, 4.0, o.cap ? ROOF.slate : ROOF.slate, 'hip');
    for (const s of [-1, 1]) { M.color = walls().rammed.a; M.box([s * (r + 0.4), (h + 1) / 2, 0], [6.2, h + 1, 9], walls().rammed.walk, 'tfkrl'); }
    // 主殿
    const k = o.cap ? 1.15 : 1;
    M.color = H('#D8D0BC'); M.box([0, 1.4, -5.5], [15 * k, 2.8, 10 * k], H('#E6DFCC'), 'tfkrl');
    M.color = PAL.hanRed; M.box([0, 2.8 + 2.3, -5.5], [10.4 * k, 4.6, 6 * k], null, 'fkrl');
    farRoof(K, 0, 7.4, -5.5, 14 * k, 9.6 * k, 4.6, o.cap ? ROOF.gold : ROOF.slate, 'hip');
    for (const [x, z, w, d] of [[-11, 9.5, 6.4, 4.4], [11, 9.5, 6.4, 4.4], [-11.5, 2.5, 5.6, 4.2], [11.5, 2.5, 5.6, 4.2]]) {
      M.color = PAL.plaster; M.box([x, 1.7, z], [w, 3.4, d], null, 'fkrl');
      farRoof(K, x, 3.3, z, w + 1.4, d + 1.4, 1.9, ROOF.slate, 'gable');
    }
    farTree(K, -7, -13.5, 0.8); farTree(K, 7, -13.5, 0.85);
  };
  /** 远景坡顶：hip / gable / pyramid（每面一块） */
  function farRoof(K, x, y, z, w, d, rise, pal, style) {
    const M = K.M, W = w / 2, D = d / 2, Lr = style === 'pyramid' ? 0 : style === 'gable' ? W : Math.max(0.2, W - D);
    const t = pal.tile, ta = pal.tileAlt || pal.tile;
    M.quad([x - W, y, z + D], [x + W, y, z + D], [x + Lr, y + rise, z], [x - Lr, y + rise, z], [0, 1, 1], t);
    M.quad([x + W, y, z - D], [x - W, y, z - D], [x - Lr, y + rise, z], [x + Lr, y + rise, z], [0, 1, -1], ta);
    if (style === 'gable') for (const s of [-1, 1]) M.triangleFacing([x + s * W, y, z - D], [x + s * W, y + rise, z], [x + s * W, y, z + D], [s, 0, 0], PAL.plaster);
    else for (const s of [-1, 1]) M.triangleFacing([x + s * W, y, z - D], [x + s * Lr, y + rise, z], [x + s * W, y, z + D], [s, 0.7, 0], P.shade(t, 0.92));
    M.color = pal.ridge; if (Lr > 0.3) M.box([x, y + rise + 0.1, z], [Lr * 2 + 0.2, 0.3, 0.4], null, 'tfkrl');
  }
  function farTree(K, x, z, s, kind) {
    const M = K.M, f = foliage(kind || 'huai');
    M.color = H('#8A6644'); M.cylinder([x, 0, z], 0.4 * s, 0.3 * s, 3 * s, 4, false, false, false);
    const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2 * s, 8 * s, { coolMix: 0.58 });
    M.softLump([x, 4.6 * s, z], [2.8 * s, 2.3 * s, 2.6 * s], 0, (x * 31 + z) % 360, ramp, 0.12);
  }

  // ============================================================== 模型表与缓存 --
  const MODEL_OF = {
    han: 'han', roman: 'roman', persia: 'persia', arab: 'arab', kushan: 'kushan', wa: 'wa', korea: 'korea', steppe: 'steppe',
    tarim: 'tarim', seasia: 'seasia', yi: 'village', nanman: 'village', celt: 'hillfort', german: 'hillfort', sarmatian: 'sarmatian',
    india: 'india', africa: 'africa', moor: 'moor',
  };
  const KUSH = new Set(['meroe', 'napata', 'karanog']);
  const cultureKey = c => (MODEL_OF[c] ? c : (SG.cultureBase && MODEL_OF[SG.cultureBase(c)] ? SG.cultureBase(c) : 'han'));
  /** 模型键：文化 → 模型（含城的特例：库施 / 阿克苏姆、博斯普鲁斯希腊城 / 萨尔马提亚车营） */
  function modelKey(culture, city, capital) {
    const c = cultureKey(culture);
    let m = MODEL_OF[c];
    if (m === 'africa') m = city && KUSH.has(city.key) ? 'kush' : 'aksum';
    else if (m === 'sarmatian') m = capital || (city && city.town >= 350) ? 'greek' : 'camp';
    else if (m === 'village' || m === 'hillfort') m = m + ':' + c;
    return m;
  }
  /** 档次：0 = 普通，1 = 大城（size ≥ 1.22，约町 ≥ 430），2 = 都城 */
  function tierOf(size, capital, city) {
    if (capital) return 2;
    if (size != null) return size >= 1.22 ? 1 : 0;
    return city && city.town >= 430 ? 1 : 0;
  }
  function variantOf(city) { return city && city.key ? strHash(city.key) & 1 : 0; }

  const CACHE = new Map();
  /** 建一个模型（米），收尾：缩到单位尺寸、抬到底座顶 y = LIFT */
  function build(mk, culture, tier, variant, lod, city) {
    const seed = (strHash(mk + ':' + tier + ':' + variant + ':' + lod) % 100000) + 1;
    const K = context(seed);
    const o = { cap: tier === 2, tier, variant, culture, city, far: lod === 'far' };
    const fnName = mk.split(':')[0];
    const fn = lod === 'far' ? (F[fnName] || F.han) : (B[fnName] || B.han);
    fn(K, o);
    mark(K, 'rest');
    const out = { ink: null, plain: null, lamps: [], tris: 0, height: 0, parts: K.parts };
    const k = 1 / U;
    for (const [name, M] of [['ink', K.M], ['plain', K.Mp]]) {
      if (M.isEmpty) continue;
      const g = M.toGeometry();
      const pa = g.attributes.position.array;
      for (let i = 0; i < pa.length; i += 3) { pa[i] *= k; pa[i + 1] = pa[i + 1] * k + LIFT; pa[i + 2] *= k; }
      g.attributes.position.needsUpdate = true;
      g.computeBoundingBox(); g.computeBoundingSphere();
      g.userData.cultureArt = mk;
      out[name] = g;
      out.tris += g.userData.tris;
      out.height = Math.max(out.height, g.boundingBox.max.y);
    }
    out.lamps = K.lamps.map(l => Object.assign({}, l, { pos: [l.pos[0] * k, l.pos[1] * k + LIFT, l.pos[2] * k], range: l.range * k, haloSize: l.haloSize * k }));
    return out;
  }
  function model(culture, capital, city, opts) {
    if (!ready()) return null;
    opts = opts || {};
    const lod = opts.lod === 'far' ? 'far' : 'near';
    const mk = modelKey(culture, city, capital);
    const tier = tierOf(opts.size, capital, city);
    const variant = lod === 'far' || tier === 2 ? 0 : variantOf(city);
    const key = mk + '|' + tier + '|' + variant + '|' + lod;
    let m = CACHE.get(key);
    if (!m) { m = build(mk, cultureKey(culture), tier, variant, lod, city); m.key = key; CACHE.set(key, m); }
    return m;
  }

  // ============================================================== 写入 MeshBuilder --
  const _m = new THREE.Matrix4(), _n = new THREE.Matrix3(), _v = new THREE.Vector3();
  /** 把单位尺寸的 LP 几何体按 size 写进 SG.LP.MeshBuilder（它当前的变换下）；plain 部分不描墨线 */
  function appendLP(M, g, size, plain) {
    if (!g) return;
    const A = g.attributes, pos = A.position.array, nor = A.normal.array, col = A.color.array, wind = A.lpWind ? A.lpWind.array : null, sm = A.lpSmooth ? A.lpSmooth.array : null;
    const nScale = A.normal.normalized ? 1 / 127 : 1, cScale = A.color.normalized ? 1 / 255 : 1, sScale = A.lpSmooth && A.lpSmooth.normalized ? 1 / 127 : 1;
    M.push().scale(size);
    const e = M.m.elements, ne = M.nm.elements, base = M.P.length / 3, n = pos.length / 3;
    for (let i = 0; i < n; i++) {
      const x = pos[i * 3], y = pos[i * 3 + 1], z = pos[i * 3 + 2];
      M.P.push(e[0] * x + e[4] * y + e[8] * z + e[12], e[1] * x + e[5] * y + e[9] * z + e[13], e[2] * x + e[6] * y + e[10] * z + e[14]);
      const a = nor[i * 3] * nScale, b = nor[i * 3 + 1] * nScale, c = nor[i * 3 + 2] * nScale;
      let nx = ne[0] * a + ne[3] * b + ne[6] * c, ny = ne[1] * a + ne[4] * b + ne[7] * c, nz = ne[2] * a + ne[5] * b + ne[8] * c;
      const l = Math.hypot(nx, ny, nz) || 1; M.N.push(nx / l, ny / l, nz / l);
      M.C.push(col[i * 4] * cScale, col[i * 4 + 1] * cScale, col[i * 4 + 2] * cScale);
      const ea = col[i * 4 + 3] * cScale;
      M.E.push(ea >= 0.5 ? -Math.max(0, (ea - 0.502) / 0.498) : ea / 0.498);
      M.W.push(wind ? wind[i] : 0);
      M.B.push(0);
      M.K.push(plain ? 0 : sm ? Math.min(1, Math.hypot(sm[i * 3], sm[i * 3 + 1], sm[i * 3 + 2]) * sScale) : 1);
    }
    for (let i = 0; i < n; i += 3) M.addTri(base + i, base + i + 1, base + i + 2);
    M.pop();
  }
  /** 写进原有的 SG.MeshBuilder（Unity 坐标、可带 mb.M）：形状、法线与顶点色 */
  function appendLegacy(mb, g, size) {
    if (!g) return;
    const A = g.attributes, pos = A.position.array, nor = A.normal.array, col = A.color.array;
    const nScale = A.normal.normalized ? 1 / 127 : 1, cScale = A.color.normalized ? 1 / 255 : 1;
    const Mx = mb.M;
    if (Mx) _n.setFromMatrix4(Mx);
    const Pp = mb.pos, N = mb.nor, K = mb.col, UV = mb.uvs, n = pos.length / 3;
    for (let i = 0; i < n; i++) {
      // three（模型）→ Unity（z 取反）→ mb.M → three
      let x = pos[i * 3] * size, y = pos[i * 3 + 1] * size, z = -pos[i * 3 + 2] * size;
      let nx = nor[i * 3] * nScale, ny = nor[i * 3 + 1] * nScale, nz = -nor[i * 3 + 2] * nScale;
      if (Mx) {
        _v.set(x, y, z).applyMatrix4(Mx); x = _v.x; y = _v.y; z = _v.z;
        _v.set(nx, ny, nz).applyMatrix3(_n).normalize(); nx = _v.x; ny = _v.y; nz = _v.z;
      }
      Pp.push(x, y, -z); N.push(nx, ny, -nz);
      K.push(col[i * 4] * cScale, col[i * 4 + 1] * cScale, col[i * 4 + 2] * cScale);
      UV.push(0, 0);
    }
  }
  const isLP = mb => !!(SG.LP && SG.LP.MeshBuilder && mb instanceof SG.LP.MeshBuilder);
  function writeInto(mb, m, size) {
    if (!m) return mb;
    if (isLP(mb)) { appendLP(mb, m.ink, size, false); appendLP(mb, m.plain, size, true); }
    else { appendLegacy(mb, m.plain, size); appendLegacy(mb, m.ink, size); }
    return mb;
  }
  /** LP 不可用时（极少：页面在载入后才动态加入本文件）的最简模型：底座、四墙、主殿 */
  function fallbackInto(mb, s) {
    if (isLP(mb) || !mb.box) return mb;
    const v = (x, y, z) => new THREE.Vector3(x, y, z), c1 = new THREE.Color(0.74, 0.7, 0.62), c2 = new THREE.Color(0.6, 0.3, 0.24);
    mb.box(v(0, 0.05, 0), v(s * 2.3, 0.1, s * 2.3), new THREE.Color(0.62, 0.58, 0.5));
    for (const [x, z, w, d] of [[0, -s, s * 2, 0.2 * s], [0, s, s * 2, 0.2 * s], [-s, 0, 0.2 * s, s * 2], [s, 0, 0.2 * s, s * 2]]) mb.box(v(x, 0.1 + 0.2 * s, z), v(w, 0.4 * s, d), c1);
    mb.box(v(0, 0.1 + 0.2 * s, 0.2 * s), v(0.6 * s, 0.4 * s, 0.4 * s), c2);
    return mb;
  }

  // ============================================================== 接口 --
  let capitals = null;
  function worldCapitals() {
    if (capitals) return capitals;
    capitals = new Set();
    try { const FI = SG.FactionInfo; if (FI) for (const k of Object.keys(FI)) if (FI[k] && FI[k].capital) capitals.add(FI[k].capital); } catch (e) { /* 无世界数据 */ }
    return capitals;
  }
  function cultureOfCity(city) {
    if (!city) return 'han';
    if (typeof city.culture === 'string' && city.culture) return city.culture;
    try { if (SG.WorldData && city.key && SG.Scenarios && SG.G && SG.G.scenario === 'world') return SG.WorldData.cultureOfCity(city.key) || 'han'; } catch (e) { /* 忽略 */ }
    return 'han';
  }
  function isCapital(city, culture) {
    if (!city) return false;
    if (city.key === 'luoyang' || city.key === 'changan') return true;
    const c = culture || cultureOfCity(city);
    return c !== 'han' && worldCapitals().has(city.key);
  }
  function cityInto(mb, size, culture, capital, city) {
    if (!ready()) return fallbackInto(mb, size);
    return writeInto(mb, model(culture, !!capital, city || null, { size }), size);
  }
  function cityFarInto(mb, size, culture, capital, city) {
    if (!ready()) return fallbackInto(mb, size);
    return writeInto(mb, model(culture, !!capital, city || null, { size, lod: 'far' }), size);
  }
  function scaledLamps(m, size) {
    return m ? m.lamps.map(l => Object.assign({}, l, { pos: [l.pos[0] * size, l.pos[1] * size, l.pos[2] * size], range: l.range * size, haloSize: l.haloSize * size })) : [];
  }
  function object(size, culture, capital, city, opts) {
    if (!ready()) return new THREE.Group();
    const m = model(culture, !!capital, city || null, Object.assign({ size }, opts || {}));
    const g = new THREE.Group();
    g.name = 'City ' + m.key;
    if (m.ink) g.add(LP.mesh(m.ink, { castShadow: true }));
    if (m.plain) g.add(LP.mesh(m.plain, { outline: false, castShadow: false }));
    g.scale.setScalar(size);
    // userData.lp.lamps：位置为模型坐标（随 obj 的世界矩阵变换），range / haloSize 已按 size 缩放（LPProps.addLamps 用）
    g.userData.lp = { name: m.key, lamps: m.lamps.map(l => Object.assign({}, l, { range: l.range * size, haloSize: l.haloSize * size })), anchors: {}, tris: m.tris, size: [2.3 * size, m.height * size, 2.3 * size] };
    g.userData.tris = m.tris;
    return g;
  }

  SG.CultureArt = {
    cultures: Object.keys(MODEL_OF).filter(k => k !== 'han'),
    cultureOfCity, isCapital, cityInto, cityFarInto,
    city(size, culture, capital, city) {
      if (!ready()) { const mb = new SG.MeshBuilder(); fallbackInto(mb, size); return mb.toGeometry(); }
      const M = new LP.MeshBuilder(1);
      writeInto(M, model(culture, !!capital, city || null, { size }), size);
      return M.toGeometry();
    },
    model(culture, capital, city, opts) { return model(culture, !!capital, city || null, opts); },
    object,
    lamps(size, culture, capital, city) { return ready() ? scaledLamps(model(culture, !!capital, city || null, { size }), size) : []; },
    modelKey(culture, city, capital) { return modelKey(culture, city || null, !!capital); },
    tierOf, variantOf, U, LIFT,
    clearCache() { for (const m of CACHE.values()) { if (m.ink) m.ink.dispose(); if (m.plain) m.plain.dispose(); } CACHE.clear(); },
  };
})();
