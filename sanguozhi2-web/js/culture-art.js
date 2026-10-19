'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 各文化的城池模型（第四版：Lanternvale 风格的低多边形，SG.LP / SG.LPProps）

   第四版（美术推广 · 城池）：全部文化的城池近景与远景模型按试点批准的画风重做——平面着色的木石与建筑、顶点色每面抖动、
   贴地 AO、夯土 / 砖 / 石墙的分层色带与墙脚石、墨线（底座地面不描）、软团树冠（上暖下青）、夜里窗纸 / 灯笼 / 火光自发光
   （MeshBuilder.nightEmission，由场景的 LP.Mood 点亮），并给出灯位（城门灯笼、殿前灯、篝火）供 Mood 加灯池。
   汉地城池沿用试点城门楼的语汇：夯土城墙（版筑分层、深色缝、石砌勒脚、垛口女墙、马面）、朱柱格窗的城楼与歇山灰瓦顶
   （SG.LPProps.parts.hanRoof：筒瓦、瓦当、起翘、鸱吻）、红灯笼；都城为金顶重檐大殿与阙楼。

   约定（与 v3 相同）：size ≈ 占地半径 r，城墙在 ±r，城门朝南；旗帜由 map-view 画在东北角（Unity (0.9r, 0, 0.9r)），该角保持空旷；
   标签高度 size × 1.6 + 0.6 —— 模型最高处不超过约 1.5r。
   底座：方城为约 2.3r × 2.3r 的八角方台（外圈 + 城内两色）；开放聚落（倭、草原、夷洲 / 南中、凯尔特 / 日耳曼、车营、阿克苏姆、
   毛里）为贴着外圈的圆台（踩实的土色、矮斜坡沉进地形），旗帜处补一小块土台。近景与远景用同一套底座规格（BASES），远近切换不变色。
   模型内部按米建模（1 个 size 单位 = 20 m），出图时缩到单位尺寸（size = 1），底座顶面在 y = 0.1。
   三角形（含城墙与底座）：近景每城约 1.5k–4.9k（平均约 3.0k），最大的都城（洛阳 / 长安）约 5.5k；中景（mid）为近景去掉垛口、
   墙缝、灯笼、屋脊饰的版本（城墙城约为近景的 55–70%）；远景 ≤ ~400（夜里有至多 3 块暖色窗光 / 一堆篝火 + 院心的地面暖光）。
   夜里：窗纸、灯笼、门洞自发光；每座城的路端 / 广场 / 庭院有一片地面灯池（glowPool：白天与地面同色，夜里中心发暖光），
   没分到氛围真灯池的城也有一团暖光。
   几何体按（模型、档次、变体、远近）缓存，同类城共用（战略地图上 196 座城只有约 70 份近景几何体）；缓存的几何体标记
   userData.sharedGeometry，只由 CultureArt.clearCache 释放。

   文化 → 模型
     han        汉：夯土方城（版筑色带、马面、垛口）、三座角楼、南门重檐城楼、东西门楼、三层台基的庑殿主殿、坊屋、槐树；
                都城（洛阳、长安）金顶重檐、阙楼
     korea      高句丽 / 三韩：山城（山丘 + 依山的不规则石墙）、灰瓦门楼、山上殿阁（丹青檐下）、草顶 / 瓦顶民居、松
     wa         倭（邪马台）：环壕、木栅、望楼、高床仓库、竖穴住居；都城为带千木的大型高床殿
     steppe     草原：毡帐营地（蒙古包）、车阵、马尾纛、马栏、篝火；都城为金顶大帐
     tarim      西域绿洲：风蚀的夯土城墙、平顶土房、钻天杨、水池与果园、烽燧；大城为佛塔
     seasia     南海（林邑、扶南）：方形木栅、水渠、鞍形草顶的高脚屋（部分山墙朝前）、红砖台基上的三重檐神殿、椰树；
                都城加两座砖塔配殿
     yi、nanman 夷洲 / 南中：竹木栅栏围成的高脚屋村落；南中有铜鼓祭台
     celt       凯尔特：山丘堡垒（土垒 + 木栅）、圆形茅屋；都城为大圆屋
     german     日耳曼：山丘堡垒、长屋；都城为带交叉兽头山墙的大厅
     sarmatian  萨尔马提亚：篷车环、条纹尖顶毡帐、龙旗、马栏的车营；大城（町 ≥ 350）与都城为博斯普鲁斯希腊城
                （白石城墙方塔、卫城丘上的六柱神庙、红瓦民居）
     roman      罗马：石墙圆塔（瓦顶）、双塔夹拱门、红瓦民居、列柱神庙；都城为椭圆竞技场 + 神庙
     persia     安息 / 波斯：土坯城墙（阶梯垛口、半圆扶壁）、釉砖尖拱门楼、穹顶平顶房、阶梯内城上的伊万宫殿；
                都城为三级台上的穹顶伊万宫殿 + 拜火祠；泰西封（模型键 persia:ctesiphon）为泰西封大拱（Taq Kasra）与拜火祠
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
     SG.CultureArt.cityFarInto(mb, size, culture, capital, city?) → mb   远景模型（≤ ~400 三角形，轮廓与配色一致；没给 city 时
         沿用紧挨着的上一次 cityInto 的城——同文化、同档、同尺寸——以便库施 / 希腊城的远景与近景一致）
     SG.CultureArt.city(size, culture, capital, city?) → BufferGeometry  近景的 LP 几何体（墨线部分 + 地面部分合一，测试用）
   第四版新增（战略地图建议用这些：实例化 / 合并、墨线、夜灯）
     SG.CultureArt.model(culture, capital, city?, { lod: 'near' | 'mid' | 'far', size }) → { ink, plain, lamps, tris, key, height }
         单位尺寸（size = 1，城心在原点，three 坐标，城门朝 +Z）的缓存几何体：ink = 描墨线的部分，plain = 不描的部分
         （底座、水面），lamps = [{ pos:[x,y,z], color, intensity, range, nightOnly, haloSize, flicker }]（单位尺寸）。
         同一 key 的几何体共用，勿 dispose。放进 LP.Scatter（{ near, far }，缩放 = size）或 LP.StaticBatch 都可以。
     SG.CultureArt.object(size, culture, capital, city?, { lod }) → THREE.Group（LP.mesh + 墨线；userData.lp.lamps 已按 size
         缩放，可直接 SG.LPProps.addLamps(obj, mood)）
     SG.CultureArt.lamps(size, culture, capital, city?) → 灯位数组（按 size 缩放的模型坐标）
     SG.CultureArt.scatter(list, { chunk, lodDist, inkDist, midDist }) → { group, scatter, lamps, update(camera) }
         战略地图的推荐接法：list = [{ x, y, z（three 坐标）, yaw（弧度）, size, culture, capital, city }]；同一模型键的城
         实例化（LP.Scatter：墨线也实例化），按块切换近景（< midDist，墨线在 inkDist 以内）/ 中景 / 远景（> lodDist）；
         lamps 为世界坐标的灯位（给 SG.Gfx.addLamp / mood.addLamp）
     SG.CultureArt.modelKey(culture, city?, capital?) / tierOf(size, capital, city?) / variantOf(city) / U（每个 size 单位的米数）
     SG.CultureArt.clearCache()                     释放全部缓存的几何体（model / object / scatter 给出的几何体都在其中，勿自行 dispose）
   测试页：tests/culture-art.html（全部文化 × 普通 / 大城 / 都城，近景 / 远景，正午 / 黄昏 / 夜，可旋转；scatter=1 用实例化接法，
   perf=N 为 N 座城的性能场景，nolamps=1 不加氛围灯池）。
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
      // 远景：斜坡与垂直的裙边合成一面（从顶面边缘直接斜到 y = −3.4），每边省两个三角形
      if (o.single) { M.quadColors([a[0], 0, a[1]], [b[0], 0, b[1]], [rb[0], yd, rb[1]], [ra[0], yd, ra[1]], out, [top, top, P.shade(side, 0.8), P.shade(side, 0.8)]); continue; }
      M.quadColors([a[0], 0, a[1]], [b[0], 0, b[1]], [rb[0], yb, rb[1]], [ra[0], yb, ra[1]], out, [top, top, edge, edge]);
      M.quadColors([ra[0], yb, ra[1]], [rb[0], yb, rb[1]], [rb[0], yd, rb[1]], [ra[0], yd, ra[1]], [out[0], 0, out[2]], [edge, edge, P.shade(side, 0.8), P.shade(side, 0.8)]);
    }
  }
  /**
   * 底座规格（近景与远景共用同一套形状与颜色，远近切换时不变色）。
   *   方城（城墙在 ±r）：八角方台 octo(h, chamfer)，外圈 top + 城内 inner 两色；
   *   开放聚落（环壕 / 木栅 / 车阵 / 山丘堡垒）：贴着聚落外圈的圆台（半径 round），整块是踩实的土色（城内 inner 略亮、外圈暗约 5%），
   *   边缘是很矮的斜坡沉进地形——不再是一块抬起的绿色方板（像棋盘格子）。旗帜（map-view 画在 (0.9r, −0.9r)）落在圆台外，
   *   那里补一小块同色的土台（flagFoot），旗杆不至于悬空。
   */
  const BASES = {
    han: { oct: [23.4, 2.4], top: '#BCA780', side: '#8C7556', inner: [18.6, 1.8], innerCol: '#C8B48E' },
    roman: { oct: [23.4, 2.4], top: '#B8AC86', side: '#8C7A5C', inner: [18.8, 1.8], innerCol: '#C9BD9C' },
    persia: { oct: [23.4, 2.4], top: '#C2A67A', side: '#8E7452', inner: [18.8, 1.8], innerCol: '#D0B78C' },
    arab: { oct: [23.4, 3.4], top: '#C9B48A', side: '#907A58', inner: [19, 4], innerCol: '#D6C49C' },
    kushan: { oct: [23.4, 2.4], top: '#C0A47C', side: '#8C7254', inner: [18.8, 1.8], innerCol: '#CDB48C' },
    india: { oct: [23.6, 2.4], top: '#9EA668', side: '#7E7A50', inner: [19.2, 1.8], innerCol: '#C2A47A' },
    korea: { oct: [23.4, 3.0], top: '#9AAA68', side: '#7C7A52', inner: [18.6, 2.4], innerCol: '#B8AA80' },
    tarim: { oct: [23.4, 2.6], top: '#CDB68A', side: '#957A58', inner: [18.4, 2.0], innerCol: '#D6C29A' },
    seasia: { oct: [23.4, 3.0], top: '#8EA460', side: '#6E7A4A', inner: [18.0, 2.4], innerCol: '#A4A66C' },
    greek: { oct: [23.4, 2.4], top: '#BCB28E', side: '#8C7E5E', inner: [18.8, 1.8], innerCol: '#CEC4A2' },
    kush: { oct: [23.4, 2.4], top: '#C4A070', side: '#8C6E4E', inner: [18.8, 1.8], innerCol: '#D0AE80' },
    wa: { round: 22.6, top: '#AE9E76', side: '#7A6E52', inner: 15.2, innerCol: '#B8A67C' },
    steppe: { round: 20.6, top: '#AFA674', side: '#7A7450', inner: 15.4, innerCol: '#B9B07A' },
    'village:yi': { round: 19.8, top: '#AEA072', side: '#76704E', inner: 16.2, innerCol: '#B8AA7A' },
    'village:nanman': { round: 19.8, top: '#AA9A6E', side: '#746A4C', inner: 16.2, innerCol: '#B4A274' },
    'hillfort:celt': { round: 22.2, top: '#8E9658', side: '#6E6E4A' },
    'hillfort:german': { round: 22.2, top: '#8E9658', side: '#6E6E4A' },
    camp: { round: 20.4, top: '#B3A876', side: '#7A7450', inner: 15.6, innerCol: '#BDB27C' },
    aksum: { round: 21.8, top: '#AEA07C', side: '#7A7054', inner: 18.2, innerCol: '#B8AA84' },
    moor: { round: 22.6, top: '#A89A70', side: '#7A6E52' },
  };
  /** 底座（key = 模型键；far = 远景：圆台少几边）。返回城门大路的起点 z（圆台的边） */
  function basePad(K, key, far) {
    const b = BASES[key] || BASES[key.split(':')[0]] || BASES.han;
    if (b.oct) {
      pad(K, octo(b.oct[0], b.oct[1]), { top: b.top, side: b.side, inner: { pts: octo(b.inner[0], b.inner[1]), color: b.innerCol }, bevel: far ? 1.2 : undefined, single: far });
      return 23;
    }
    const n = far ? 12 : 14, off = Math.PI / n;
    pad(K, ngon(n, b.round, b.round, off), { top: b.top, side: b.side, inner: b.inner ? { pts: ngon(n, b.inner, b.inner, off), color: b.innerCol } : null, bevel: 0.6, bevelDrop: 0.25, single: far });
    // 旗脚：旗帜位置（18, −18）的一小块土台（远景不画：那么远看不出旗杆脚）
    if (!far) pad(K, ngon(6, 2.4, 2.4, 0.3).map(p => [p[0] + 18, p[1] - 18]), { top: b.top, side: b.side, bevel: 0.5, bevelDrop: 0.25 });
    return b.round - 0.5;
  }
  /** 底座城内的地面色（灯池、远景的院心光用） */
  function baseInnerCol(key) { const b = BASES[key] || BASES[key.split(':')[0]] || BASES.han; return b.innerCol || b.top; }
  /**
   * 夜里的地面灯池（不描墨线）：贴地的多边形扇，中心夜间自发光 e、边缘 ≈ 0（白天与所在地面同色，看不出）。地图上只有离视野中心
   * 最近的几座城分到氛围的真灯池，其余的城靠它在夜里也有一团暖光。col = 脚下地面的颜色（路端 / 广场 / 城内土色）
   */
  /** 城内的大灯池：底座城内的土色，压在路与广场色块之下（y 0.035），只在露出的地面上发光 */
  function yardGlow(K, key, x, z, r, y) { glowPool(K, x, z, r, baseInnerCol(key), { y: (y || 0) + 0.035, n: 10, e: 0.75 }); }
  function glowPool(K, x, z, r, col, o) {
    o = o || {};
    // 颜色略偏暖（白天几乎看不出，夜里的光是琥珀色而不是灰白）
    const M = K.Mp, c = mix(col, '#F0A050', 0.1), y = o.y != null ? o.y : 0.065, n = o.n || 8, e0 = o.e || 0.85;
    M.nightEmission = e0;
    const ic = M.addVertex([x, y, z], [0, 1, 0], c);
    M.nightEmission = 0.004;
    const rim = [];
    for (let i = 0; i < n; i++) { const a = (i + 0.5) / n * Math.PI * 2; rim.push(M.addVertex([x + Math.cos(a) * r, y, z + Math.sin(a) * r], [0, 1, 0], c)); }
    for (let i = 0; i < n; i++) M.addTri(ic, rim[(i + 1) % n], rim[i]);
    M.nightEmission = 0;
  }
  /**
   * 夜里“灯光洗墙”的四边形：顶点按高度给夜间自发光（y ≤ yLow 的顶点为 e，其余 ≈ 0），白天与普通面一样。城墙内壁的下半段用它：
   * 夜里城里一圈暖色，远看是一座有灯火的城（不加三角形）
   */
  function washQuad(M, a, b, c, d, outward, col, e, yLow) {
    const cc = M.jitterOf(C(col));
    quadE(M, a, b, c, d, outward, [cc, cc, cc, cc], [a, b, c, d].map(p => (p[1] <= yLow + 1e-4 ? e : 0)));
  }
  /**
   * quadColors 加逐顶点的夜间自发光 es[0..3]（同 quadColors 的朝向规则）。只要有一个顶点发光，四个顶点都按“夜间”编码
   * （最小 0.004）——否则插值会穿过“常亮自发光”的编码区，夜里出一道亮边
   */
  function quadE(M, a, b, c, d, outward, cols, es) {
    const sub = (p, q) => [p[0] - q[0], p[1] - q[1], p[2] - q[2]], cr = (u, v) => [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    let n = cr(sub(c, a), sub(d, b)), ca = cols[0], cb = cols[1], ccol = cols[2], cd = cols[3], ea = es[0], eb = es[1], ec = es[2], ed = es[3];
    if (n[0] * outward[0] + n[1] * outward[1] + n[2] * outward[2] < 0) { let t = b; b = d; d = t; t = cb; cb = cd; cd = t; t = eb; eb = ed; ed = t; n = [-n[0], -n[1], -n[2]]; }
    const any = Math.max(ea, eb, ec, ed) > 0, keep = M.nightEmission;
    const v = (p, col, e) => { M.nightEmission = any ? Math.max(0.004, e) : 0; return M.addVertex(p, n, col); };
    const i0 = v(a, ca, ea), i1 = v(b, cb, eb), i2 = v(c, ccol, ec), i3 = v(d, cd, ed);
    M.nightEmission = keep;
    M.addTri(i0, i1, i2); M.addTri(i0, i2, i3);
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
    const ys = [0, hP].concat(o.courses || [hP + (h - hP) * 0.33, hP + (h - hP) * 0.66]).concat([h]).filter((y, i, a) => i === 0 || y > a[i - 1] + 0.05);
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
      // 层间深缝（勒脚之上，断续；6 m 以下的矮墙不画——地图上看不出，省三角形）
      if (i > 1 && h >= 6) {
        let s0 = -X;
        while (s0 < X - 0.5) {
          const s1 = Math.min(X, s0 + 2 + K.r() * 5);
          if (K.r() < 0.78 && !inSkip((s0 + s1) / 2)) {
            const y = y0 + (K.r() - 0.5) * 0.04, hh = 0.035 + K.r() * 0.03;
            if (K.mid) { s0 = s1 + 0.6 + K.r() * 1.5; continue; }
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
      washQuad(M, [-X, 0, -t / 2], [-X, yh, -t / 2], [X, yh, -t / 2], [X, 0, -t / 2], [0, 0, -1], P.shade(pal.inner, 0.92), 0.55, 0);
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
    if (K.mid) { for (let i = 0; i < n; i++) if (!(skipFn && skipFn(x0 + (i + 0.5) * (x1 - x0) / n))) K.r(); return; }
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
        // 圆头垛（印度 / 库施）：方垛 + 楔形顶（前后两面是尖拱形的山面；14 个三角形，地图上读作圆头垛）
        const hb = m.h * 0.62, hw = m.w / 2, yt = y + m.h, yb = y + hb, zf = z + d / 2, zk = z - d / 2;
        M.box([x, y + hb / 2, z], [m.w, hb, d], null, 'fkrl');
        M.quad([x - hw, yb, zf], [x, yt, zf], [x, yt, zk], [x - hw, yb, zk], [-1, 1, 0], P.shade(M.color, 1.06));
        M.quad([x + hw, yb, zk], [x, yt, zk], [x, yt, zf], [x + hw, yb, zf], [1, 1, 0], P.shade(M.color, 1.1));
        M.triangleFacing([x - hw, yb, zf], [x + hw, yb, zf], [x, yt, zf], [0, 0, 1]);
        M.triangleFacing([x - hw, yb, zk], [x + hw, yb, zk], [x, yt, zk], [0, 0, -1]);
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
      // o.glow：城门台正面下部夜里被门洞与灯笼照亮（勒脚最亮，第二层渐暗）
      const G = o.glow && i < 2 ? o.glow : 0, eb = G * (i === 0 ? 1 : 0.8), et = G * (i === 0 ? 0.8 : 0.22);
      if (F.includes('f') && G) quadE(M, [x - W(a), y0 + a, z + Dd(a)], [x + W(a), y0 + a, z + Dd(a)], [x + W(b), y0 + b, z + Dd(b)], [x - W(b), y0 + b, z + Dd(b)], [0, bat, 1], [cb, cb, ct, ct], [eb, eb, et, et]);
      else if (F.includes('f')) q([x - W(a), y0 + a, z + Dd(a)], [x + W(a), y0 + a, z + Dd(a)], [x + W(b), y0 + b, z + Dd(b)], [x - W(b), y0 + b, z + Dd(b)], [0, bat, 1], 1);
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
      const ro = rt * (o.coneOver || 1.22), ch = o.coneH != null ? o.coneH : rt * 1.55 + 0.4;
      M.lathe([[rt * 0.7, h - 0.2], [ro, h + 0.1], [ro * 0.98, h + 0.35], [0, h + ch]], n, false, true, false, [P.shade(C(o.cone), 0.72), P.shade(C(o.cone), 0.8), C(o.cone), P.shade(C(o.cone), 1.12)], (K.seed * 37) % 360);
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
          if (K.mid) continue;
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
    // o.glow：门洞夜里透出门道里的火光（白天仍是深色门洞）
    const dark = H(o.glow ? '#4A3A2E' : '#3A2E28'), ink = M.ink, keepNE = M.nightEmission;
    M.ink = 0;
    const arcPts = [];
    if (kind !== 'square') for (let i = 0; i <= 8; i++) {
      const a = i / 8 * Math.PI, c = Math.cos(a);
      const y = kind === 'pointed' ? rise * Math.pow(Math.max(0, 1 - Math.abs(c)), 0.55) : Math.sin(a) * hw;
      arcPts.push([x + c * hw, y0 + ys + y]);
    }
    // 门洞（深色）
    if (o.glow) M.nightEmission = o.glow;
    M.quad([x - hw, y0, z + 0.02], [x + hw, y0, z + 0.02], [x + hw, y0 + ys, z + 0.02], [x - hw, y0 + ys, z + 0.02], [0, 0, 1], dark);
    for (let i = 0; i < arcPts.length - 1; i++) M.triangleFacing([x, y0 + ys, z + 0.02], [arcPts[i][0], arcPts[i][1], z + 0.02], [arcPts[i + 1][0], arcPts[i + 1][1], z + 0.02], [0, 0, 1], dark);
    M.nightEmission = keepNE;
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
    if (K.mid) { M.ink = ink; return; }
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
    if (!K.mid) {
      // 地图上的灯笼只有几个像素：6 边的纸灯笼（双锥）+ 4 边的金色顶盖，约 30 个三角形
      M.push().translate(x, y, z);
      M.color = PAL.gold;
      M.cylinder([0, hgt * 0.5, 0], r * 0.45, r * 0.4, 0.16, 4, false, false, true);
      M.color = paper; M.nightEmission = 0.95; M.emission = 0;
      M.lathe([[r * 0.5, -hgt * 0.5], [r, 0], [r * 0.5, hgt * 0.5]], 6, false, true, true, [P.shade(paper, 0.92), P.shade(paper, 1.05), paper]);
      M.nightEmission = 0;
      M.pop();
    }
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
    if (K.mid) { q.detail = 'low'; q.ornaments = false; }
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
  /** 草色：把给定的（偏暗的）草色提亮、提纯到 Lanternvale 的金黄草顶（#D7AC62 → #E9C680 一带） */
  const strawC = c => P.hsv(C(c || '#C8A868'), 0, 1.12, 1.16);
  /** 圆锥草顶（圆屋、竖穴、望楼）：外凸的锥面、深色滴水檐、亮顶、顶结；o: { color, sides, rot, knot } */
  function thatchCone(K, x, y, z, r, h, o) {
    const M = K.M, straw = strawC(o && o.color), eave = P.shade(P.mix(straw, H('#8E6A3A'), 0.5), 0.8), top = P.shade(straw, 1.1), n = (o && o.sides) || 10;
    M.push().translate(x, y, z);
    M.lathe([[r * 0.97, -0.42], [r * 1.03, -0.06], [r * 0.86, h * 0.3], [r * 0.6, h * 0.6], [r * 0.27, h * 0.87], [0, h]], n, false, true, false,
      [sh(eave, 0.85), eave, straw, sh(straw, 1.05), top, top], o && o.rot != null ? o.rot : (K.seed * 53) % 360);
    if (!(o && o.knot === false) && n > 4) { M.color = sh(eave, 0.95); M.cylinder([0, h * 0.86, 0], r * 0.13, r * 0.06, h * 0.2, 5, false, false, true); }
    M.pop();
  }
  function thatchLoaf(K, x, y, z, w, d, h, o) {
    // 面包形草顶（脊沿 X），两端收圆；loft 的截面是上凸的半椭圆
    const M = K.M, straw = strawC(o && o.color), eave = P.shade(P.mix(straw, H('#9A7448'), 0.55), 0.88);
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
    if (s > 0.8 && kind !== 'olive' && K.mid) r();
    else if (s > 0.8 && kind !== 'olive') {
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
  /** 民居的瓦色：比主殿 / 城楼的 slate 亮一级（地图上主殿与城楼的屋顶更深更重，是城的视觉中心） */
  let HOUSE_SLATE = null;
  const houseSlate = () => HOUSE_SLATE || (HOUSE_SLATE = { tile: H('#97A3B8'), tileAlt: H('#8996AC'), ridge: H('#626E86') });
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
    if (o.lanterns) for (const s of [-1, 1]) lantern(K, x + s * (w / 2 - 0.4), y0 + h - 1.15, zf + 0.8, { r: 0.8 });
    return roof;
  }

  /**
   * 汉式城门（试点城门楼的地图版）：夯土城台（分层、勒脚、券门、红门门钉、匾）、台上女墙、重檐 / 单檐歇山城楼（朱柱格窗、栏杆）、
   * 两盏红灯笼。(x, z) = 城台中心，yaw = 朝向（0 = 朝南 +Z）
   */
  function hanGate(K, x, z, yaw, o) {
    at(K, x, 0, z, yaw, () => {
      const w = o.w, d = o.d, hb = o.h, M = K.M;
      const t = block(K, 0, 0, 0, w, d, hb, { glow: 0.9, kind: o.kind || 'rammed', bat: 0.1, ys: [1.1, hb * 0.38, hb * 0.7], faces: 'fkrl' });
      onFace(K, 0, d / 2, 0.1, () => {
        const ah = o.ah || 5.4;
        archway(K, 0, 0, 0, { w: o.aw || 3.6, h: ah, kind: 'round', door: '#A83E2E', frame: PAL.stoneLight, studs: true, border: 0.45, glow: 0.85 });
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
        for (const s of [-1, 1]) lantern(K, s * (pw / 2 - 0.4), hb + 0.5 + 3.0, pd / 2 + 0.85, { r: 0.8 });
      } else {
        hanHall(K, 0, hb + 0.5, 0, pw, pd, o.ph || 4.0, { style: 'xieshan', detail: o.detail || 'mid', over: 1.5, rail: true, lanterns: true, palette: o.palette });
      }
    });
  }

  // ---------------------------------------------------------- 地面与土方（不描墨线，像地形）--
  /** 贴地的色块（田、广场、沙地、路；凸多边形） */
  function patch(K, pts, col, y) {
    const M = K.Mp, c = C(col), yy = y != null ? y : 0.03;
    for (let i = 1; i + 1 < pts.length; i++) M.triangleFacing([pts[0][0], yy, pts[0][1]], [pts[i][0], yy, pts[i][1]], [pts[i + 1][0], yy, pts[i + 1][1]], [0, 1, 0], P.shade(c, 0.96 + 0.08 * K.r()));
  }
  function road(K, ax, az, bx, bz, w, col, y) {
    const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz) || 1, nx = -dz / L * w / 2, nz = dx / L * w / 2;
    patch(K, [[ax + nx, az + nz], [bx + nx, bz + nz], [bx - nx, bz - nz], [ax - nx, az - nz]], col || '#CDB48A', y != null ? y : 0.04);
  }
  function ellipsePatch(K, x, z, rx, rz, col, n, y) { patch(K, ngon(n || 10, rx, rz, 0.3).map(p => [p[0] + x, p[1] + z]), col, y); }
  /** 田（垄沟分色的长方块，贴地） */
  function field(K, x, z, w, d, col, rows) {
    const n = rows || 4, c = C(col);
    for (let i = 0; i < n; i++) {
      const z0 = z - d / 2 + i * d / n, z1 = z0 + d / n * 0.82;
      patch(K, [[x - w / 2, z0], [x + w / 2, z0], [x + w / 2, z1], [x - w / 2, z1]], P.shade(c, i % 2 ? 0.92 : 1.04), 0.05);
    }
  }
  /** 土丘 / 台地（椭圆，顶面平，flat = 顶面半径比）：不描墨线；返回顶高 */
  function mound(K, x, z, rx, rz, h, o) {
    o = o || {};
    const M = K.Mp, n = o.sides || 12, top = C(o.top || '#A6B46E'), side = C(o.side || '#8E9C5E'), k = o.flat != null ? o.flat : 0.6;
    M.push().translate(x, 0, z).scale(1, 1, rz / rx);
    M.lathe([[rx * 1.06, -1.4], [rx, 0], [lerp(rx, rx * k, 0.62), h * 0.8], [rx * k, h], [0, h]], n, false, false, false,
      [sh(side, 0.8), side, mix(side, top, 0.45), top, top], (K.seed * 17) % 360);
    M.pop();
    return h;
  }
  /** 环形土垒（不描墨线）：椭圆中心半径 R / Rz、底宽 w、高 h；gap = 南面（+Z）缺口的半角（弧度） */
  function bankRing(K, R, Rz, w, h, o) {
    o = o || {};
    const M = K.Mp, n = o.n || 24, gap = o.gap || 0, col = C(o.color || '#8FA05E'), top = C(o.top || mix(o.color || '#8FA05E', '#D6CF92', 0.32)), foot = sh(col, 0.8);
    const y0 = o.y0 || 0, Q = (a, r, y) => [Math.cos(a) * r, y0 + y, Math.sin(a) * r * Rz / R];
    const ri = R - w / 2, ro = R + w / 2, ti = R - w * 0.16, to = R + w * 0.16, da = Math.PI * 2 / n;
    const inGap = a => gap > 0 && Math.abs(Math.atan2(Math.sin(a - Math.PI / 2), Math.cos(a - Math.PI / 2))) < gap;
    for (let i = 0; i < n; i++) {
      const a0 = i * da + (o.rot || 0), a1 = a0 + da, am = a0 + da / 2, ca = Math.cos(am), sa = Math.sin(am);
      if (inGap(am)) continue;
      M.quadColors(Q(a0, ro, -0.3), Q(a1, ro, -0.3), Q(a1, to, h), Q(a0, to, h), [ca, 0.7, sa], [foot, foot, col, col]);
      M.quad(Q(a0, to, h), Q(a1, to, h), Q(a1, ti, h), Q(a0, ti, h), [0, 1, 0], P.shade(top, 0.97 + 0.06 * K.r()));
      M.quadColors(Q(a0, ti, h), Q(a1, ti, h), Q(a1, ri, -0.3), Q(a0, ri, -0.3), [-ca, 0.7, -sa], [col, col, foot, foot]);
      if (inGap(am - da)) M.quad(Q(a0, ro, -0.3), Q(a0, to, h), Q(a0, ti, h), Q(a0, ri, -0.3), [Math.sin(a0), 0, -Math.cos(a0)], sh(col, 0.9));
      if (inGap(am + da)) M.quad(Q(a1, ro, -0.3), Q(a1, to, h), Q(a1, ti, h), Q(a1, ri, -0.3), [-Math.sin(a1), 0, Math.cos(a1)], sh(col, 0.9));
    }
  }
  /** 椭圆上的点（[[x, z]…]），从 a0 起逆时针 n 个 */
  function ringPts(n, R, Rz, a0) { const out = []; for (let i = 0; i < n; i++) { const a = (a0 || 0) + i * Math.PI * 2 / n; out.push([Math.cos(a) * R, Math.sin(a) * (Rz || R)]); } return out; }
  /** 南门缺口的判定：点 (x, z) 在 +Z 方向半角 gap 之内 */
  const southGap = gap => (x, z) => Math.abs(Math.atan2(x, z)) < gap;

  // ---------------------------------------------------------- 木栅 / 篱 --
  /**
   * 尖头木桩栅栏：沿折线 pts（closed 默认首尾相连），桩距 step，高 h（每根略有高低、明暗）；4 棱圆木，棱角朝外。
   * o: { step, r, color, closed, skip(x, z) → 跳过（门洞）, y0, bamboo（竹：青黄色、平头、两道横绑） }
   */
  function palisade(K, pts, h, o) {
    o = o || {};
    const M = K.M, step = o.step || 1.45, r = o.r || 0.5, wood = C(o.color || (o.bamboo ? '#B8A866' : '#8C6A48')), closed = o.closed !== false;
    const n = pts.length, segs = closed ? n : n - 1, ink = M.ink;
    // 细桩只描很细的墨线：地图上桩距 1.2–1.5 m、桩粗 0.4–0.5 m，满宽的墨线会把整圈栅栏涂成一道黑环（远景的栅环是木色，切换时跳色）
    M.ink = o.ink != null ? o.ink : 0.3;
    for (let i = 0; i < segs; i++) {
      const a = pts[i], b = pts[(i + 1) % n], L = Math.hypot(b[0] - a[0], b[1] - a[1]), m = Math.max(1, Math.round(L / step));
      for (let k = 0; k < m; k++) {
        const t = (k + 0.5) / m, x = lerp(a[0], b[0], t), z = lerp(a[1], b[1], t);
        if (o.skip && o.skip(x, z)) continue;
        const hh = h * (0.88 + 0.24 * K.r());
        M.color = P.shade(wood, 0.86 + 0.22 * K.r());
        M.push().translate(x, o.y0 || 0, z);
        if (o.bamboo) M.lathe([[r, -0.3], [r * 0.95, hh]], 4, false, false, true, null, Math.atan2(z, x) / D2R);
        else M.lathe([[r, -0.3], [r * 0.92, hh], [0, hh + r * 2.4]], 4, false, false, false, null, Math.atan2(z, x) / D2R);
        M.pop();
      }
      if (o.bamboo || o.rail) {
        // 横绑（只画外侧与顶面，贴着桩）
        const dx = b[0] - a[0], dz = b[1] - a[1], nx = dz / (L || 1), nz = -dx / (L || 1), c0 = [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2];
        const s = nx * c0[0] + nz * c0[1] > 0 ? 1 : -1, off = r * 1.05 * s, rc = sh(wood, 0.7);
        for (const yy of o.bamboo ? [h * 0.35, h * 0.75] : [h * 0.62]) {
          if (o.skip && (o.skip(a[0], a[1]) || o.skip(b[0], b[1]))) continue;
          M.quad([a[0] + nx * off, (o.y0 || 0) + yy - 0.14, a[1] + nz * off], [b[0] + nx * off, (o.y0 || 0) + yy - 0.14, b[1] + nz * off], [b[0] + nx * off, (o.y0 || 0) + yy + 0.14, b[1] + nz * off], [a[0] + nx * off, (o.y0 || 0) + yy + 0.14, a[1] + nz * off], [nx * s, 0, nz * s], rc);
        }
      }
    }
    M.ink = ink;
  }
  /** 木门楼（栅门）：两根高柱 + 横梁 + 小草顶（朝 +Z，可 yaw） */
  function woodGate(K, x, z, yaw, w, h, o) {
    o = o || {};
    at(K, x, o.y0 || 0, z, yaw, () => {
      const M = K.M, wood = C(o.color || '#7A5A3E');
      M.color = wood;
      for (const s of [-1, 1]) M.cylinder([s * w / 2, 0, 0], 0.42, 0.36, h, 5, false, false, true);
      M.color = sh(wood, 0.9); M.box([0, h - 0.5, 0], [w + 1.4, 0.5, 0.6], null, 'tfkrl');
      if (o.roof !== false) thatchLoaf(K, 0, h + 0.1, 0, w + 2.2, 2.6, 1.3, { color: o.thatch || '#B89A5E' });
      if (o.torch !== false) for (const s of [-1, 1]) torch(K, s * (w / 2 + 0.7), h * 0.55, 0.5);
    });
  }
  /** 火把（柱上的火焰，常亮小火 + 夜里的灯位） */
  function torch(K, x, y, z, o) {
    const M = K.M;
    M.color = H('#5A4026'); M.cylinder([x, y - 0.9, z], 0.1, 0.12, 0.9, 4, false, false, false);
    M.color = H('#FFB040'); M.emission = 0.9; const ink = M.ink; M.ink = 0;
    M.cone([x, y, z], 0.26, 0.75, 4);
    M.emission = 0; M.ink = ink;
    lamp(K, [x, y + 0.5, z + 0.2], { color: '#FF9A40', range: 7, intensity: 1.2, flicker: 0.25, haloSize: 0.9, nightOnly: true });
  }

  // ---------------------------------------------------------- 各式房屋 --
  /**
   * 平顶房（土坯 / 石 / 砖）：墙身（可带勒脚）、平顶 + 女墙、梁头、门洞、夜里亮的窗；在 (x, z)、朝 yaw。
   * o: { col, roof, plinth, plinthColor, win（正面窗数）, sideWin, beams, parapet, dome（小穹顶色）, arch（拱窗）, door, yaw, storey（二层退台：比例） }
   */
  function flatHouse(K, x, z, w, d, h, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#D8B88A'), roof = C(o.roof || P.shade(c, 1.05));
    at(K, x, o.y0 || 0, z, o.yaw || 0, () => {
      body(K, 0, 0, 0, w, d, h, c, { plinth: o.plinth || 0, plinthColor: o.plinthColor });
      flatTop(K, 0, h, 0, w, d, roof, o.parapet != null ? o.parapet : 0.5);
      const ink = M.ink; M.ink = 0;
      const dw = Math.min(1.3, w * 0.3), dh = Math.min(2.3, h * 0.62);
      if (o.door !== false) {
        const lit = K.r() < 0.5, dc = C(o.door || (lit ? '#A06A40' : '#5E4232'));
        if (lit) M.nightEmission = 0.55;
        if (o.arch) { M.quad([-dw / 2, 0, d / 2 + 0.03], [dw / 2, 0, d / 2 + 0.03], [dw / 2, dh - dw / 2, d / 2 + 0.03], [-dw / 2, dh - dw / 2, d / 2 + 0.03], [0, 0, 1], dc); for (let i = 0; i < 3; i++) { const a0 = i / 3 * Math.PI, a1 = (i + 1) / 3 * Math.PI; M.triangleFacing([0, dh - dw / 2, d / 2 + 0.03], [Math.cos(a0) * dw / 2, dh - dw / 2 + Math.sin(a0) * dw / 2, d / 2 + 0.03], [Math.cos(a1) * dw / 2, dh - dw / 2 + Math.sin(a1) * dw / 2, d / 2 + 0.03], [0, 0, 1], dc); } }
        else M.quad([-dw / 2, 0, d / 2 + 0.03], [dw / 2, 0, d / 2 + 0.03], [dw / 2, dh, d / 2 + 0.03], [-dw / 2, dh, d / 2 + 0.03], [0, 0, 1], dc);
        M.nightEmission = 0;
      }
      M.ink = ink;
      const nw = o.win != null ? o.win : (w > 4.4 ? 2 : 1), wy = Math.min(h - 0.9, Math.max(1.6, h * 0.62));
      for (let i = 0; i < nw; i++) {
        const wx = nw === 1 ? (o.door === false ? 0 : w * 0.3) : lerp(-w * 0.32, w * 0.32, i / (nw - 1));
        windowGlow(K, wx, wy, d / 2, 0.72, 0.78, { arch: o.arch, color: o.winColor });
      }
      if (o.sideWin) windowGlowSide(K, w / 2, wy, 0, 0.55, 0.62, o.winColor);
      if (o.beams) { M.color = H('#6E4B36'); for (let i = 0; i < 3; i++) M.box([lerp(-w / 2 + 0.5, w / 2 - 0.5, i / 2), h - 0.25, d / 2 + 0.18], [0.2, 0.2, 0.36], null, 'tfrl'); }
      if (o.dome) { const dr = Math.min(w, d) * 0.3; M.color = C(o.dome); M.lathe([[dr, h], [dr * 0.92, h + dr * 0.42], [dr * 0.55, h + dr * 0.85], [0, h + dr]], 8, false, false, false, [sh(C(o.dome), 0.92), C(o.dome), sh(C(o.dome), 1.05), sh(C(o.dome), 1.1)]); }
      if (o.storey) {
        const w2 = w * o.storey, d2 = d * 0.55, h2 = 2.4;
        body(K, -w / 2 + w2 / 2 + 0.3, h, -d / 2 + d2 / 2 + 0.3, w2, d2, h2, P.shade(c, 1.02), { plinth: 0 });
        flatTop(K, -w / 2 + w2 / 2 + 0.3, h + h2, -d / 2 + d2 / 2 + 0.3, w2, d2, roof, 0.4);
        windowGlow(K, -w / 2 + w2 / 2 + 0.3, h + 1.3, -d / 2 + d2 + 0.3, 0.5, 0.55, { arch: o.arch, color: o.winColor });
      }
    });
  }
  /** 土坯房的墙色（暖色的变化：土黄、浅灰白、赭、粉褐） */
  const MUDS = ['#D8BC90', '#CBAE80', '#E6D4B2', '#D2A87A', '#DCC0A0'];
  /** 小花园 / 绿地：贴地的草色块 + 一丛灌木（+ 一棵小树） */
  function garden(K, x, z, w, d, o) {
    o = o || {};
    patch(K, [[x - w / 2, z - d / 2], [x + w / 2, z - d / 2], [x + w / 2, z + d / 2], [x - w / 2, z + d / 2]], o.col || '#8FA65C', 0.05);
    if (o.tree) tree(K, x + w * 0.18, z - d * 0.12, o.s || 0.5, o.tree);
    if (o.bush !== false) bush(K, x - w * 0.25, z + d * 0.22, 0.6, 'oak');
  }
  /** 平顶 + 一圈女墙（圆角的土色压顶） */
  function flatTop(K, x, y, z, w, d, roof, ph) {
    const M = K.M, t = 0.3;
    M.color = sh(roof, 0.84);
    M.quad([x - w / 2, y, z - d / 2], [x + w / 2, y, z - d / 2], [x + w / 2, y, z + d / 2], [x - w / 2, y, z + d / 2], [0, 1, 0], sh(roof, 0.84));
    if (!(ph > 0)) return;
    M.color = roof;
    M.box([x, y + ph / 2, z + d / 2 - t / 2], [w, ph, t], P.shade(roof, 1.08), 'tfk');
    M.box([x, y + ph / 2, z - d / 2 + t / 2], [w, ph, t], P.shade(roof, 1.08), 'tfk');
    M.box([x - w / 2 + t / 2, y + ph / 2, z], [t, ph, d - 2 * t], P.shade(roof, 1.08), 'trl');
    M.box([x + w / 2 - t / 2, y + ph / 2, z], [t, ph, d - 2 * t], P.shade(roof, 1.08), 'trl');
  }
  function windowGlowSide(K, x, y, z, w, h, col) {
    K.M.push().translate(x, 0, z).rotate(0, 90, 0);
    windowGlow(K, 0, y, 0, w, h, { color: col });
    K.M.pop();
  }
  /** 地中海式民居（罗马 / 希腊）：白灰墙、石勒脚、红瓦四坡 / 双坡顶、木百叶窗、门 */
  function medHouse(K, x, z, w, d, h, o) {
    o = o || {};
    const M = K.M, c = C(o.col || (K.r() < 0.5 ? '#EFE4CE' : '#E8D6B4'));
    at(K, x, o.y0 || 0, z, o.yaw || 0, () => {
      body(K, 0, 0, 0, w, d, h, c, { plinth: 0.5, plinthColor: '#B4A994' });
      const ink = M.ink; M.ink = 0;
      M.quad([-0.6, 0.5, d / 2 + 0.03], [0.6, 0.5, d / 2 + 0.03], [0.6, Math.min(2.4, h - 0.6), d / 2 + 0.03], [-0.6, Math.min(2.4, h - 0.6), d / 2 + 0.03], [0, 0, 1], H('#7A5038'));
      M.ink = ink;
      const n = w > 5 ? 2 : 1;
      for (let i = 0; i < n; i++) {
        const wx = n === 1 ? w * 0.3 : lerp(-w * 0.32, w * 0.32, i), wy = Math.min(h - 0.9, 2.0) + (h > 5 ? 1.4 : 0);
        windowGlow(K, wx, wy, d / 2, 0.62, 0.82, { color: '#C08A58' });
        M.ink = 0; for (const s of [-1, 1]) M.quad([wx + s * 0.3, wy - 0.38, d / 2 + 0.05], [wx + s * 0.62, wy - 0.38, d / 2 + 0.05], [wx + s * 0.62, wy + 0.38, d / 2 + 0.05], [wx + s * 0.3, wy + 0.38, d / 2 + 0.05], [0, 0, 1], C(o.shutter || '#5E8A7E')); M.ink = ink;
      }
      tileRoof(K, 0, 0, { w, d, y: h, over: 0.55, rise: d * 0.3, pal: 'terracotta', hip: o.hip !== false, gable: c });
    });
  }
  /**
   * 圆屋（凯尔特 / 阿克苏姆 / 南方）：多边形土墙 + 厚草圆锥顶 + 门（夜里门内火光）。o: { wall, thatch, sides, roofH, yaw, porch }
   */
  function roundHut(K, x, z, r, h, o) {
    o = o || {};
    const M = K.M, wall = C(o.wall || '#CDB590'), n = o.sides || 8;
    M.push().translate(x, o.y0 || 0, z); if (o.yaw) M.rotate(0, o.yaw, 0);
    M.color = wall;
    M.lathe([[r, 0], [r * 0.97, h]], n, false, false, false, [sh(wall, 0.88), wall], 180 / n);
    const fz = r * Math.cos(Math.PI / n) * 0.97 + 0.04, dw = Math.min(0.75, r * 0.32);
    M.nightEmission = 0.7; const ink = M.ink; M.ink = 0;
    M.quad([-dw, 0, fz], [dw, 0, fz], [dw, h * 0.92, fz], [-dw, h * 0.92, fz], [0, 0, 1], H('#7A5236'));
    M.nightEmission = 0; M.ink = ink;
    thatchCone(K, 0, h, 0, r * 1.22, o.roofH || r * 1.35, { color: o.thatch, sides: n });
    M.pop();
  }
  /** 竖穴住居 / 草庐（圆锥草顶直接落地，前面一个小门廊） */
  function pitHouse(K, x, z, r, o) {
    o = o || {};
    const M = K.M;
    M.push().translate(x, o.y0 || 0, z); if (o.yaw) M.rotate(0, o.yaw, 0);
    thatchCone(K, 0, 0.3, 0, r, r * 1.15, { color: o.thatch || '#B49A62', sides: 8 });
    // 入口小山墙
    M.color = sh(C(o.thatch || '#B49A62'), 0.9);
    M.quad([-0.7, 0, r * 0.8], [0.7, 0, r * 0.8], [0.3, 1.3, r * 0.62], [-0.3, 1.3, r * 0.62], [0, 0.3, 1]);
    M.nightEmission = 0.7; const ink = M.ink; M.ink = 0;
    M.quad([-0.45, 0, r * 0.82], [0.45, 0, r * 0.82], [0.2, 0.95, r * 0.69], [-0.2, 0.95, r * 0.69], [0, 0.3, 1], H('#6A4630'));
    M.nightEmission = 0; M.ink = ink;
    M.pop();
  }
  /**
   * 高脚屋（南海 / 南中 / 夷洲 / 倭的高床仓）：木柱、楼板、竹木墙、鞍形草顶（两端上翘，sweep）或双坡草顶、梯子。
   * o: { postH, wallH, wall, thatch, sweep（0 = 平脊）, yaw, roofH, over }
   */
  function stiltHouse(K, x, z, w, d, o) {
    o = o || {};
    const M = K.M, ph = o.postH != null ? o.postH : 2.0, wh = o.wallH || 1.9, wood = C(o.wood || '#7A5A3E'), wall = C(o.wall || '#B89A6A');
    at(K, x, o.y0 || 0, z, o.yaw || 0, () => {
      M.color = wood;
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.cylinder([sx * (w / 2 - 0.3), 0, sz * (d / 2 - 0.3)], 0.22, 0.2, ph, 4, false, false, false);
      if (w > 5) for (const sz of [-1, 1]) M.cylinder([0, 0, sz * (d / 2 - 0.3)], 0.22, 0.2, ph, 4, false, false, false);
      M.color = sh(wood, 1.1); M.box([0, ph + 0.15, 0], [w + 0.6, 0.3, d + 0.6], sh(wood, 1.2), 'tfkrl');
      M.color = wall; M.box([0, ph + 0.3 + wh / 2, 0], [w, wh, d], null, 'fkrl');
      M.nightEmission = 0.75; const ink = M.ink; M.ink = 0;
      M.quad([-0.5, ph + 0.3, d / 2 + 0.03], [0.5, ph + 0.3, d / 2 + 0.03], [0.5, ph + 0.3 + wh * 0.82, d / 2 + 0.03], [-0.5, ph + 0.3 + wh * 0.82, d / 2 + 0.03], [0, 0, 1], H('#7A5236'));
      M.nightEmission = 0; M.ink = ink;
      // 梯子
      M.color = sh(wood, 0.9); M.beam([0, 0, d / 2 + 1.4], [0, ph + 0.3, d / 2 + 0.3], 0.9, 0.16, [0, 1, 0]);
      saddleRoof(K, 0, ph + 0.3 + wh, 0, w, d, { thatch: o.thatch, sweep: o.sweep != null ? o.sweep : 1.2, rise: o.roofH || d * 0.62, over: o.over });
    });
    if (o.lamp !== false) lamp(K, [x, (o.y0 || 0) + ph + 1.2, z + d / 2 + 0.8], { color: '#FFB060', range: 6, intensity: 0.9, haloSize: 0.6 });
  }
  /** 鞍形草顶（脊沿 X，两端上翘 sweep 米）：三点截面的 loft，下缘一圈深色草檐 */
  function saddleRoof(K, x, y, z, w, d, o) {
    const M = K.M, straw = strawC(o.thatch || '#B8975A'), eave = P.shade(P.mix(straw, H('#8E6A3A'), 0.5), 0.8), top = P.shade(straw, 1.12);
    const ov = o.over != null ? o.over : 0.45, W = w / 2 + ov, D = d / 2 + ov * 0.8, rise = o.rise, sw = o.sweep || 0;
    const xs = [-1, -0.62, 0, 0.62, 1], rings = xs.map(u => {
      const up = sw * Math.pow(Math.abs(u), 2.2), ex = Math.abs(u) > 0.99 ? sw * 0.3 : 0, X = x + u * (W + ex);
      const yE = y - 0.3 + up * 0.3, yR = y + rise + up, bul = 0.16 * rise;
      return [[X, yE - 0.34, z + D + 0.05], [X, yE, z + D], [X, lerp(yE, yR, 0.52) + bul, z + D * 0.46], [x + u * (W + ex * 1.5), yR, z], [X, lerp(yE, yR, 0.52) + bul, z - D * 0.46], [X, yE, z - D], [X, yE - 0.34, z - D - 0.05]];
    });
    M.loft(rings, true, true, n => (n[1] < -0.3 ? sh(eave, 0.8) : n[1] < 0.3 ? eave : P.mix(straw, top, clamp01((n[1] - 0.3) / 0.6))));
  }
  /** 长屋（日耳曼）：矮木墙 + 面包形草顶（脊沿 X）+ 山墙门；o: { yaw, horns（山墙交叉兽头）} */
  function longhouse(K, x, z, len, w, o) {
    o = o || {};
    const M = K.M, wall = C(o.wall || '#8A6A4A'), h = o.h || 2.4;
    at(K, x, o.y0 || 0, z, o.yaw || 0, () => {
      M.color = wall; M.box([0, h / 2, 0], [len, h, w], null, 'fkrl');
      M.color = sh(wall, 0.8);
      for (let i = 0; i <= 4; i++) M.box([lerp(-len / 2, len / 2, i / 4), h / 2, w / 2 + 0.05], [0.28, h, 0.12], null, 'f');   // 立柱
      M.nightEmission = 0.7; const ink = M.ink; M.ink = 0;
      M.quad([len / 2 + 0.03, 0, -0.6], [len / 2 + 0.03, 0, 0.6], [len / 2 + 0.03, h * 0.85, 0.6], [len / 2 + 0.03, h * 0.85, -0.6], [1, 0, 0], H('#6A4630'));
      M.quad([-0.5, 0, w / 2 + 0.08], [0.5, 0, w / 2 + 0.08], [0.5, h * 0.85, w / 2 + 0.08], [-0.5, h * 0.85, w / 2 + 0.08], [0, 0, 1], H('#6A4630'));
      M.nightEmission = 0; M.ink = ink;
      thatchLoaf(K, 0, h, 0, len + 0.9, w + 1.5, o.roofH || w * 0.72, { color: o.thatch || '#A88C58' });
      if (o.horns) {
        M.color = H('#6A4A30');
        for (const s of [-1, 1]) for (const f of [-1, 1]) M.beam([s * (len / 2 + 0.3), h + (o.roofH || w * 0.72) * 0.75, 0], [s * (len / 2 + 0.5), h + (o.roofH || w * 0.72) + 1.0, f * 0.9], 0.22, 0.22, [s, 0, 0]);
      }
    });
  }
  /** 蒙古包：毡墙（深色绳带）、圆顶、天窗圈、彩门；gold = 金顶大帐 */
  function yurt(K, x, z, r, o) {
    o = o || {};
    const M = K.M, felt = C(o.felt || '#ECE2CC'), roofC = C(o.roof || felt), band = C(o.band || '#8A5A3C'), h = r * 0.62;
    M.push().translate(x, o.y0 || 0, z); if (o.yaw) M.rotate(0, o.yaw, 0);
    M.color = felt;
    M.lathe([[r, 0], [r, h * 0.45], [r, h * 0.55], [r * 0.99, h]], 8, false, false, false, [sh(felt, 0.9), felt, band, felt], 22.5);
    M.lathe([[r * 1.04, h - 0.05], [r * 0.82, h + r * 0.26], [r * 0.38, h + r * 0.44], [r * 0.2, h + r * 0.47]], 8, false, false, false, [sh(roofC, 0.9), roofC, sh(roofC, 1.06), sh(roofC, 1.1)], 22.5);
    M.color = sh(band, 0.8); M.disc([0, h + r * 0.47, 0], r * 0.2, 6);
    const fz = r * Math.cos(Math.PI / 8) + 0.04, ink = M.ink; M.ink = 0;
    M.nightEmission = o.lit === false ? 0 : 0.7;
    M.quad([-0.55, 0, fz], [0.55, 0, fz], [0.55, Math.min(1.8, h * 0.85), fz], [-0.55, Math.min(1.8, h * 0.85), fz], [0, 0, 1], C(o.door || '#C46A3A'));
    M.nightEmission = 0; M.ink = ink;
    if (o.finial) { M.color = PAL.gold; M.cone([0, h + r * 0.47, 0], r * 0.12, r * 0.4, 5); }
    M.pop();
  }
  /** 尖顶帐（萨尔马提亚 / 军帐）：多边圆锥、门帘、顶上木杆；o: { col, stripe, yaw } */
  function coneTent(K, x, z, r, h, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#D8CCB0'), st = C(o.stripe || sh(c, 0.8));
    M.push().translate(x, o.y0 || 0, z); if (o.yaw) M.rotate(0, o.yaw, 0);
    M.color = st;
    M.lathe([[r, 0], [r * 0.97, h * 0.2]], 7, false, false, false, [sh(st, 0.92), st], 90 + 360 / 14);
    M.lathe([[r * 0.97, h * 0.2], [r * 0.78, h * 0.42], [r * 0.45, h * 0.72], [0, h]], 7, false, false, false, [c, c, sh(c, 1.06), sh(c, 1.1)], 90 + 360 / 14);
    M.color = H('#5A4026'); M.segment([0, h - 0.2, 0], [0, h + 0.9, 0], 0.07, 0.05, 4, false, true);
    M.nightEmission = 0.6; const ink = M.ink; M.ink = 0;
    M.triangleFacing([-r * 0.32, 0, r * 0.88], [r * 0.32, 0, r * 0.88], [0, h * 0.45, r * 0.62], [0, 0.4, 1], H('#7A5236'));
    M.nightEmission = 0; M.ink = ink;
    M.pop();
  }
  /** 黑帐篷（游牧，低矮的长方帐，中间撑高）*/
  function blackTent(K, x, z, w, d, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#5A4A3A'), h = o.h || 1.9;
    at(K, x, o.y0 || 0, z, o.yaw || 0, () => {
      const rings = [-1, -0.33, 0.33, 1].map((u, i) => { const peak = i === 1 || i === 2 ? h : h * 0.8; return [[u * w / 2, 0.3, d / 2 + 0.3], [u * w / 2, peak * 0.9, d * 0.22], [u * w / 2, peak, 0], [u * w / 2, peak * 0.9, -d * 0.22], [u * w / 2, 0.3, -d / 2 - 0.3]]; });
      M.loft(rings, true, true, n => P.shade(c, n[1] > 0.6 ? 1.12 : n[1] < -0.1 ? 0.7 : 0.95));
      M.color = H('#6A5240'); for (const s of [-1, 1]) M.segment([s * w * 0.18, 0, d / 2 + 0.25], [s * w * 0.18, h * 0.82, d / 2 + 0.2], 0.07, 0.06, 4, false, false);
    });
  }
  /** 篷车（二轮，弧形篷布）：车床、辕、两个大轮、篷；朝 yaw（辕朝 +X） */
  function wagon(K, x, z, yaw, o) {
    o = o || {};
    const M = K.M, wood = C(o.wood || '#8A6644'), cover = C(o.cover || '#E2D6BA'), L = o.len || 4.2, W = 2.0;
    at(K, x, o.y0 || 0, z, yaw, () => {
      M.color = wood; M.box([0, 1.15, 0], [L, 0.35, W], sh(wood, 1.1), 'tfkrl');
      M.beam([L / 2, 1.0, 0], [L / 2 + 2.0, 0.7, 0], 0.2, 0.18);
      M.color = sh(wood, 0.8);
      for (const s of [-1, 1]) {
        M.push().translate(-0.3, 0.95, s * (W / 2 + 0.08)).rotate(90 * s, 0, 0);
        M.cylinder([0, 0, 0], 0.95, 0.95, 0.18, 6, false, false, true);
        M.pop();
      }
      if (o.cover !== false) {
        const prof = [], n = 5; for (let i = 0; i <= n; i++) { const a = i / n * Math.PI; prof.push([Math.cos(a) * W * 0.52, 1.3 + Math.sin(a) * 1.25]); }
        const rings = [-L * 0.46, L * 0.38].map(xx => prof.map(([zz, yy]) => [xx, yy, zz]));
        M.loft(rings, true, true, nrm => P.shade(cover, nrm[1] > 0.5 ? 1.08 : 0.92));
      }
    });
  }
  /** 小围栏（马栏 / 羊圈）：圆环上的木柱 + 两道横杆（只画外侧面） */
  function corral(K, x, z, r, o) {
    o = o || {};
    const M = K.M, wood = C(o.color || '#8A6A48'), n = o.n || 10, pts = ringPts(n, r, r * (o.rz || 1), 0.2).map(p => [p[0] + x, p[1] + z]), y0 = o.y0 || 0;
    M.color = wood;
    for (const p of pts) M.cylinder([p[0], y0, p[1]], 0.16, 0.14, 1.4, 4, false, false, true);
    for (let i = 0; i < n; i++) { if (o.gate === i) continue; const a = pts[i], b = pts[(i + 1) % n]; for (const y of [0.6, 1.15]) M.beam([a[0], y0 + y, a[1]], [b[0], y0 + y, b[1]], 0.12, 0.12); }
  }
  /** 一匹马（地图尺度的小剪影：身体、颈、头、四腿、尾）—— 马栏里点缀 */
  function horse(K, x, z, yaw, col, y0) {
    const M = K.M, c = C(col || '#7A5236');
    at(K, x, y0 || 0, z, yaw, () => {
      M.color = c;
      M.box([0, 1.25, 0], [1.9, 0.75, 0.62], sh(c, 1.08), 'tfkrl');
      M.beam([0.8, 1.4, 0], [1.2, 2.05, 0], 0.32, 0.4);
      M.box([1.42, 2.0, 0], [0.62, 0.3, 0.3], null, 'tfkrl');
      M.color = sh(c, 0.85);
      for (const [lx, lz] of [[0.75, 0.2], [0.75, -0.2], [-0.75, 0.2], [-0.75, -0.2]]) M.box([lx, 0.5, lz], [0.18, 1.0, 0.18], null, 'fkrl');
      M.color = sh(c, 0.6); M.beam([-0.95, 1.45, 0], [-1.3, 0.75, 0], 0.18, 0.12);
    });
  }
  /** 古典神庙（罗马 / 希腊 / 哈特拉）：阶梯台基、内殿、正面列柱（cols 根）、额枋、山花、红瓦双坡顶（脊朝前后） */
  function temple(K, x, z, w, d, o) {
    o = o || {};
    const M = K.M, stone = C(o.stone || '#EDE6D6'), colH = o.colH || 6.0, cols = o.cols || 6, ph = o.podium || 1.6, yaw = o.yaw || 0;
    at(K, x, o.y0 || 0, z, yaw, () => {
      // 台基两级 + 前阶
      M.color = sh(stone, 0.92); M.box([0, ph * 0.3, 0], [w + 1.2, ph * 0.6, d + 1.2], sh(stone, 1.0), 'tfkrl');
      M.color = sh(stone, 0.96); M.box([0, ph * 0.8, 0], [w, ph * 0.4, d], stone, 'tfkrl');
      M.color = sh(stone, 0.9); for (let i = 0; i < 3; i++) M.box([0, ph * (i + 1) / 3 / 2, d / 2 + 0.5 + (2 - i) * 0.55], [w * 0.6, ph * (i + 1) / 3, 0.55], sh(stone, 1.02), 'tfrl');
      // 内殿（后 2/3）
      const cd = d * 0.62, cz = -d / 2 + cd / 2 + 0.4;
      M.color = C(o.cella || P.shade(stone, 0.97)); M.box([0, ph + colH / 2, cz], [w - 1.4, colH, cd], null, 'fkrl');
      const ink = M.ink; M.ink = 0;
      M.nightEmission = 0.6; M.quad([-0.9, ph, cz + cd / 2 + 0.03], [0.9, ph, cz + cd / 2 + 0.03], [0.9, ph + colH * 0.6, cz + cd / 2 + 0.03], [-0.9, ph + colH * 0.6, cz + cd / 2 + 0.03], [0, 0, 1], H('#8A5A38')); M.nightEmission = 0;
      M.ink = ink;
      // 前廊列柱（+ 两侧各两根）
      const pts = []; for (let i = 0; i < cols; i++) pts.push([lerp(-w / 2 + 0.6, w / 2 - 0.6, i / (cols - 1)), d / 2 - 0.6]);
      if (o.peri) for (const s of [-1, 1]) for (let i = 1; i < 3; i++) pts.push([s * (w / 2 - 0.6), d / 2 - 0.6 - i * (d - 1.2) / 3]);
      for (const [px, pz] of pts) {
        M.color = stone; M.cylinder([px, ph, pz], 0.42, 0.36, colH, 6, false, false, false);
        M.color = sh(stone, 1.06); M.box([px, ph + colH - 0.12, pz], [0.95, 0.24, 0.95], null, 'tfkrl');
      }
      // 额枋 + 山花屋顶（脊沿 Z）
      M.color = sh(stone, 0.98); M.box([0, ph + colH + 0.45, 0], [w + 0.2, 0.9, d + 0.2], null, 'fkrl');
      if (o.frieze) { M.ink = 0; M.quad([-w / 2 - 0.1, ph + colH + 0.25, d / 2 + 0.12], [w / 2 + 0.1, ph + colH + 0.25, d / 2 + 0.12], [w / 2 + 0.1, ph + colH + 0.62, d / 2 + 0.12], [-w / 2 - 0.1, ph + colH + 0.62, d / 2 + 0.12], [0, 0, 1], C(o.frieze)); M.ink = ink; }
      M.push().rotate(0, 90, 0);
      tileRoof(K, 0, 0, { w: d + 0.2, d: w + 0.2, y: ph + colH + 0.9, over: 0.5, rise: w * 0.2, pal: o.roof || 'terracotta', hip: false, gable: sh(stone, 1.04) });
      M.pop();
    });
    lamp(K, [x, (o.y0 || 0) + (o.podium || 1.6) + 1.2, z + d / 2 + 0.6], { color: '#FFB060', range: 9, intensity: 1.1, haloSize: 0.8 });
  }
  /** 伊万（波斯 / 安息 / 哈特拉）：方块殿身 + 正面高起的门框墙（pishtaq）+ 深尖拱洞（夜里内亮）+ 釉砖框；朝 yaw */
  function iwan(K, x, z, w, d, h, o) {
    o = o || {};
    at(K, x, o.y0 || 0, z, o.yaw || 0, () => {
      const M = K.M, kind = o.kind || 'mud';
      block(K, 0, 0, -0.6, w, d - 1.2, h, { kind, ys: [0.9, h * 0.5], bat: 0.02 });
      const pw = Math.min(w, o.pw || w * 0.66), phh = h + (o.raise != null ? o.raise : 2.2);
      block(K, 0, 0, d / 2 - 1.2, pw, 1.6, phh, { kind, ys: [0.9, phh * 0.5], faces: 'fkrl' });
      onFace(K, 0, d / 2 - 0.4, 0, () => {
        M.nightEmission = 0.45;
        archway(K, 0, 0, 0, { w: pw * 0.58, h: phh * 0.78, kind: 'pointed', door: null, frame: o.glaze || '#4E8AA8', border: 0.55 });
        M.nightEmission = 0;
        if (o.glaze !== false) { const ink = M.ink; M.ink = 0; M.quad([-pw / 2 + 0.25, phh - 0.75, 0.05], [pw / 2 - 0.25, phh - 0.75, 0.05], [pw / 2 - 0.25, phh - 0.3, 0.05], [-pw / 2 + 0.25, phh - 0.3, 0.05], [0, 0, 1], C(o.glaze || '#4E8AA8')); M.ink = ink; }
      });
      if (o.merlon !== false) crown(K, 0, phh, d / 2 - 1.2, pw, 1.6, { kind, ph: 0, pt: 0.6, skip: 'krl', merlon: { w: 0.9, h: 1.0, step: 1.9, stepped: true } });
      if (o.dome) { M.color = C(o.dome); const dr = Math.min(w, d) * 0.32; M.lathe([[dr * 1.05, h], [dr, h + 0.6], [dr * 0.9, h + 0.6 + dr * 0.5], [dr * 0.5, h + 0.6 + dr * 0.9], [0, h + 0.6 + dr * 1.0]], 10, false, false, false, [sh(C(o.dome), 0.9), C(o.dome), sh(C(o.dome), 1.04), sh(C(o.dome), 1.08), sh(C(o.dome), 1.12)]); }
    });
    lamp(K, [x, (o.y0 || 0) + 2.0, z + d / 2 + 0.6], { color: '#FFB060', range: 9, intensity: 1.1, haloSize: 0.8 });
  }
  /** 窣堵波（贵霜 / 印度 / 西域）：方台两级、鼓座 + 栏楯带、白色覆钵、平头、伞盖三重；r = 覆钵半径 */
  function stupaLP(K, x, z, r, o) {
    o = o || {};
    const M = K.M, white = C(o.color || '#F0EADC'), stone = C(o.stone || '#CFC2A6'), y0 = o.y0 || 0;
    M.push().translate(x, y0, z);
    M.color = stone; M.box([0, 0.45, 0], [r * 2.7, 0.9, r * 2.7], sh(stone, 1.06), 'tfkrl');
    M.box([0, 1.2, 0], [r * 2.3, 0.6, r * 2.3], sh(stone, 1.06), 'tfkrl');
    M.color = sh(white, 0.95); M.lathe([[r * 1.05, 1.5], [r * 1.05, 2.2], [r * 1.05, 2.3], [r * 1.05, 2.6]], 10, false, false, false, [sh(white, 0.92), sh(stone, 0.85), sh(stone, 0.85), white]);
    const prof = []; for (let i = 0; i <= 4; i++) { const a = i / 4 * Math.PI / 2; prof.push([Math.cos(a) * r, 2.6 + Math.sin(a) * r * 0.95]); } prof[4][0] = 0;
    M.color = white; M.lathe(prof, 10, true, false, false, prof.map((p, i) => P.shade(white, 0.92 + 0.04 * i)));
    const top = 2.6 + r * 0.95;
    M.color = stone; M.box([0, top + 0.3, 0], [r * 0.55, 0.6, r * 0.55], sh(stone, 1.08), 'tfkrl');
    M.color = C(o.gold || PAL.gold); M.segment([0, top + 0.5, 0], [0, top + r * 0.85 + 1.2, 0], 0.09, 0.07, 4, false, false);
    for (let i = 0; i < 3; i++) { const yy = top + 0.95 + i * (0.25 + r * 0.12), rr = r * (0.42 - i * 0.1); M.cylinder([0, yy, 0], rr, rr * 0.8, 0.14, 8, false, true, true); }
    M.pop();
    return { top: y0 + top + r * 0.85 + 1.2 };
  }
  /** 印度塔门（torana）：两根方柱 + 三道两端上卷的横梁；朝 yaw */
  function torana(K, x, z, yaw, s, o) {
    s = s || 1;
    const M = K.M, c = C((o && o.color) || '#D2BC96');
    at(K, x, 0, z, yaw, () => {
      M.color = c;
      for (const sx of [-1, 1]) M.box([sx * 1.3 * s, 2.2 * s, 0], [0.42 * s, 4.4 * s, 0.42 * s], sh(c, 1.08), 'tfkrl');
      for (let i = 0; i < 3; i++) {
        const y = (3.2 + i * 0.75) * s, half = (1.9 + 0.1 * i) * s;
        M.color = P.shade(c, 0.96 + 0.04 * i);
        M.box([0, y, 0], [half * 2, 0.32 * s, 0.36 * s], sh(c, 1.1), 'tfkrl');
        for (const sx of [-1, 1]) M.box([sx * (half + 0.12 * s), y + 0.18 * s, 0], [0.32 * s, 0.6 * s, 0.36 * s], null, 'tfkrl');
      }
    });
  }
  /** 金字塔（库施式陡峭王陵）：方锥（三段色带）、塔顶小平台、东面的小祠堂（塔门形） */
  function pyramid(K, x, z, b, h, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#B98A5E'), hw = b / 2 * Math.SQRT2;
    M.push().translate(x, 0, z);
    M.lathe([[hw, 0], [hw * 0.68, h * 0.32], [hw * 0.36, h * 0.66], [hw * 0.07, h]], 4, false, false, true, [sh(c, 0.9), c, sh(c, 1.05), sh(c, 1.1)], 45);
    // 祠堂：前面（+Z）一个小塔门 + 屋
    M.color = sh(c, 1.04); M.box([0, 1.1, hw * 0.71 + 1.6], [b * 0.42, 2.2, 2.6], sh(c, 1.1), 'tfkrl');
    M.color = sh(c, 0.96); M.taperedBox([0, 0, hw * 0.71 + 3.1], [b * 0.62, 3.0, 0.9], 0.18, 0.1);
    const ink = M.ink; M.ink = 0; M.quad([-0.45, 0, hw * 0.71 + 3.57], [0.45, 0, hw * 0.71 + 3.57], [0.45, 1.8, hw * 0.71 + 3.57], [-0.45, 1.8, hw * 0.71 + 3.57], [0, 0, 1], H('#4A3428')); M.ink = ink;
    M.pop();
  }
  /** 塔门（pylon，埃及 / 库施神庙）：两座收分的梯形塔夹一道门，门上檐口；朝 +Z */
  function pylon(K, x, z, w, h, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#D6B888');
    M.push().translate(x, 0, z);
    for (const s of [-1, 1]) {
      M.color = P.shade(c, s < 0 ? 1 : 0.97);
      M.taperedBox([s * w * 0.29, 0, 0], [w * 0.42, h, 3.2], 0.24, 0.2);
      M.color = sh(c, 1.1); M.box([s * w * 0.29, h + 0.15, 0], [w * 0.42 * 0.78, 0.3, 3.2 * 0.82], null, 'tfkrl');
    }
    M.color = sh(c, 0.94); M.box([0, h * 0.36, 0], [w * 0.2, h * 0.72, 2.4], sh(c, 1.08), 'tfk');
    const ink = M.ink; M.ink = 0;
    M.nightEmission = 0.5; M.quad([-w * 0.06, 0, 1.22], [w * 0.06, 0, 1.22], [w * 0.06, h * 0.55, 1.22], [-w * 0.06, h * 0.55, 1.22], [0, 0, 1], H('#7A5238')); M.nightEmission = 0;
    // 旗杆（塔门前的四根）
    M.ink = ink; M.color = H('#6A4A30');
    for (const s of [-1, 1]) M.segment([s * w * 0.14, 0, 1.9], [s * w * 0.14, h * 1.25, 1.9], 0.1, 0.07, 4, false, false);
    M.pop();
    lamp(K, [x, 2.2, z + 2.4], { color: '#FFB060', range: 8, intensity: 1.1, haloSize: 0.8 });
  }
  /** 阿克苏姆石碑：花岗岩高碑（圆头）、正面刻出的多层假窗与假门、底座石 */
  function stele(K, x, z, h, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#A8A49A'), w = h * 0.12, d = h * 0.07;
    M.push().translate(x, 0, z); if (o.lean) M.rotate(o.lean, 0, 0);
    M.color = sh(c, 0.92); M.box([0, 0.3, 0], [w * 2.2, 0.6, d * 3], sh(c, 1.0), 'tfkrl');
    M.color = c; M.box([0, h / 2, 0], [w, h, d], null, 'fkrl');
    M.push().translate(0, h, 0); M.color = sh(c, 1.08); M.lathe([[w * 0.5, 0], [w * 0.42, w * 0.3], [0, w * 0.5]], 6, false, false, false, null, 0); M.pop();
    const ink = M.ink; M.ink = 0;
    const floors = Math.max(3, Math.round(h / 3.2));
    for (let i = 1; i < floors; i++) {
      const y = 1.2 + i * (h - 2) / floors;
      M.quad([-w * 0.32, y, d / 2 + 0.02], [w * 0.32, y, d / 2 + 0.02], [w * 0.32, y + 0.55, d / 2 + 0.02], [-w * 0.32, y + 0.55, d / 2 + 0.02], [0, 0, 1], sh(c, 0.62));
    }
    M.quad([-w * 0.2, 0.6, d / 2 + 0.02], [w * 0.2, 0.6, d / 2 + 0.02], [w * 0.2, 1.5, d / 2 + 0.02], [-w * 0.2, 1.5, d / 2 + 0.02], [0, 0, 1], sh(c, 0.55));
    M.ink = ink;
    M.pop();
  }
  /** 筒拱顶（支提堂、印度门楼）：沿 Z 的半圆拱（n 段）盖在 w × len 上，正面（+Z）马蹄形拱窗（夜亮） */
  function vaultRoof(K, x, y, z, w, len, o) {
    o = o || {};
    const M = K.M, c = C(o.col || '#B98A5E'), n = 5, R = w / 2 + 0.3;
    const ring = zz => { const r = []; for (let i = 0; i <= n; i++) { const a = i / n * Math.PI; r.push([x + Math.cos(a) * R, y + Math.sin(a) * R * (o.k || 1.05), zz]); } return r; };
    M.loft([ring(z - len / 2), ring(z + len / 2)], false, false, nrm => P.shade(c, 0.9 + 0.18 * clamp01(nrm[1])));
    // 端面（山墙）：拱形墙 + 马蹄窗
    M.color = C(o.gable || sh(c, 1.05));
    for (const s of [-1, 1]) { const r = ring(z + s * len / 2); for (let i = 0; i < n; i++) M.triangleFacing([x, y, z + s * len / 2], r[i], r[i + 1], [0, 0, s]); }
    const ink = M.ink; M.ink = 0; M.nightEmission = 0.8;
    const wr = R * 0.5, wz = z + len / 2 + 0.03;
    M.quad([x - wr, y - 0.2, wz], [x + wr, y - 0.2, wz], [x + wr, y + 0.2, wz], [x - wr, y + 0.2, wz], [0, 0, 1], H('#C08A58'));
    for (let i = 0; i < 4; i++) { const a0 = i / 4 * Math.PI, a1 = (i + 1) / 4 * Math.PI; M.triangleFacing([x, y + 0.2, wz], [x + Math.cos(a0) * wr, y + 0.2 + Math.sin(a0) * wr * 1.15, wz], [x + Math.cos(a1) * wr, y + 0.2 + Math.sin(a1) * wr * 1.15, wz], [0, 0, 1], H('#C08A58')); }
    M.nightEmission = 0; M.ink = ink;
    // 脊（木 / 小尖饰）
    M.color = sh(c, 0.8); M.box([x, y + R * (o.k || 1.05) + 0.08, z], [0.3, 0.2, len + 0.2], null, 'tfkrl');
  }
  /** 椭圆竞技场（罗马都城）：外圈两层拱廊（深色拱洞）、顶层女墙、内圈三级看台、沙地 */
  function amphitheatre(K, x, z, rx, rz, h, o) {
    o = o || {};
    const M = K.M, stone = C(o.stone || '#E2D6BC'), n = 20;
    const E = (a, k, y) => [x + Math.cos(a) * rx * k, y, z + Math.sin(a) * rz * k];
    const tiers = [0, h * 0.42, h * 0.8, h];
    for (let i = 0; i < n; i++) {
      const a0 = i / n * Math.PI * 2, a1 = (i + 1) / n * Math.PI * 2, am = (a0 + a1) / 2, out = [Math.cos(am) / rx, 0, Math.sin(am) / rz];
      for (let t = 0; t < 3; t++) {
        const c = P.shade(stone, (t === 0 ? 0.9 : t === 1 ? 0.97 : 1.03) * (0.96 + 0.08 * K.r()));
        M.quadColors(E(a0, 1, tiers[t]), E(a1, 1, tiers[t]), E(a1, 1, tiers[t + 1]), E(a0, 1, tiers[t + 1]), out, [sh(c, 0.94), sh(c, 0.94), c, c]);
        if (t < 2) {
          // 拱洞（深色、贴面）
          const ink = M.ink; M.ink = 0;
          const k = 1.004, u0 = lerp(a0, a1, 0.28), u1 = lerp(a0, a1, 0.72), yb = tiers[t] + 0.3, yt = tiers[t + 1] - 0.6;
          M.quad(E(u0, k, yb), E(u1, k, yb), E(u1, k, yt), E(u0, k, yt), out, H('#5A4A3E'));
          M.ink = ink;
        }
      }
      M.quad(E(a0, 1, h), E(a1, 1, h), E(a1, 0.9, h), E(a0, 0.9, h), [0, 1, 0], sh(stone, 1.06));
      // 看台：三级
      for (let s = 0; s < 3; s++) {
        const k0 = 0.9 - s * 0.12, k1 = k0 - 0.12, y0 = h - s * h * 0.26, y1 = y0 - h * 0.26;
        M.quad(E(a0, k0, y0), E(a1, k0, y0), E(a1, k0, y1), E(a0, k0, y1), [-Math.cos(am), 0, -Math.sin(am)], P.shade(stone, 0.82 + 0.04 * s));
        M.quad(E(a0, k0, y1), E(a1, k0, y1), E(a1, k1, y1), E(a0, k1, y1), [0, 1, 0], P.shade(stone, 0.95 + 0.03 * s));
      }
    }
    const kk = 0.9 - 3 * 0.12;
    const sand = []; for (let i = 0; i < 12; i++) { const a = i / 12 * Math.PI * 2; sand.push([x + Math.cos(a) * rx * kk, z + Math.sin(a) * rz * kk]); }
    patch(K, sand, '#E2C894', h - 3 * h * 0.26 + 0.02);
  }
  /** 拱门洞的块墙段（城门楼的拱，立在 (x, z)，朝 +Z）：门洞 + 券石；包装 onFace + archway */
  function gateArch(K, x, z, bat, o) { onFace(K, x, z, bat, () => archway(K, 0, 0, 0, o)); }

  // ============================================================== 各文化 --
  const B = {};

  // ---------------------------------------------------------------- 汉 --
  // 夯土方城（四面城墙、马面、垛口），南门重檐城楼，东西门楼，三座角楼（东北角留给旗），三层台基上的庑殿主殿（都城金顶重檐、阙楼），
  // 白墙灰瓦的坊屋，几棵槐树；夜里城楼、主殿的格窗与灯笼亮起
  B.han = function (K, o) {
    const r = U, t = 4.2, h = 8.2, cap = o.cap, big = o.tier >= 1;
    const v = o.variant;
    basePad(K, 'han');
    yardGlow(K, 'han', 0, 4.5, 9.5);
    mark(K, 'pad');
    // 城墙（南墙城门段、东西墙门段不画垛口）
    const W = { h, t, bat: 0.11, kind: 'rammed', plinth: 1.1, courses: [2.6, 4.2, 5.8], panel: [3.6, 7] };
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 5.6, r + 5.6]], bastions: [{ u: r * 0.42, w: 4.6, d: 1.8 }, { u: r * 1.58, w: 4.6, d: 1.8 }] }));
    wallRun(K, -r, -r, r, -r, Object.assign({}, W, { skip: [[r * 1.6, r * 2]], bastions: [{ u: r * 0.6, w: 4.6, d: 1.8 }].concat(big ? [{ u: r * 1.15, w: 4.6, d: 1.8 }] : []) }));
    wallRun(K, -r, -r, -r, r, Object.assign({}, W, { skip: [[r - 3.2, r + 3.2]], bastions: [{ u: r * 0.45, w: 4.6, d: 1.8 }, { u: r * 1.55, w: 4.6, d: 1.8 }] }));
    wallRun(K, r, -r, r, r, Object.assign({}, W, { skip: [[r - 3.2, r + 3.2]], bastions: [{ u: r * 1.55, w: 4.6, d: 1.8 }] }));
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
    hanGate(K, 0, r + 0.6, 0, { w: 14, d: 7.4, h: h + 1.6, aw: 3.6, ah: 5.6, pw: 10.6, pd: 5.2, double: true, detail: cap || big ? 'mid' : 'low', palette: 'slate' });
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
    const hz = -5.5, k = cap || big ? 1.25 : 1.2;
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
        K.M.color = H('#E6DECA'); K.M.box([qx, 5.9, qz], [2.2, 1.4, 2.2], null, 'fkrl');
        hanRoof(K, qx, qz, { style: 'pyramid', w: 2.4, d: 2.4, y: 6.6, over: 0.8, detail: 'low', palette: 'slate', lift: 0.3, finialK: 0.6 });
      }
    } else {
      hanHall(K, 0, y, hz, hw, hd, hh, { style: 'hip', detail: 'mid', over: 1.8, palette: 'slate', door: true, lanterns: true });
    }
    mark(K, 'hall');
    // 坊屋（南半城与两侧，留出中轴大街）
    // 两侧不再放横屋：主殿前留出庭院（夜里的灯池），主殿与城楼是城里最大、最重的两块屋顶
    const houses = [[-11, 9.5, 6.4, 4.4], [11, 9.5, 6.4, 4.4], [-13.2, 3.6, 5.2, 4.2], [13.2, 3.6, 5.2, 4.2]];
    if (big || cap) houses.push([-5.5, 12.5, 4.6, 3.6], [5.5, 12.5, 4.6, 3.6], [-13.2, -14.2, 5.2, 3.8]);
    houses.forEach(([x, z, w, d, side], i) => {
      const hh2 = 3.4 + ((i + v) % 3) * 0.35;
      if (side) at(K, x, 0, z, x < 0 ? 90 : -90, () => hanHouse(K, 0, 0, d, w, hh2, { hip: (i + v) % 2 === 0 }));
      else hanHouse(K, x + (v ? 0.6 : 0), z, w, d, hh2, { hip: (i + v) % 3 === 0, roof: houseSlate() });
    });
    mark(K, 'houses');
    // 槐树
    const trees = cap ? [[-7, -13.5, 0.8], [7, -13.5, 0.85]] : v ? [[-6.5, -13, 0.85], [7.5, -14, 0.8], [-15.5, 13.5, 0.7]] : [[-7, -13.5, 0.8], [7, -13.5, 0.85], [15.5, 14, 0.7]];
    for (const [x, z, s] of trees) tree(K, x, z, s, 'huai');
  };
  // -------------------------------------------------------------- 罗马 --
  // 石砌方城（垛口、勒脚、错缝色带）、红瓦圆锥顶的圆塔、南门双圆塔夹拱门、主街、广场与柱廊、六柱神庙、红瓦民居（insulae）、柏树；
  // 大城加长方会堂（basilica）；都城为椭圆竞技场 + 神庙
  B.roman = function (K, o) {
    const r = U, h = 7.4, cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'roman');
    yardGlow(K, 'roman', 0, 2, 9);
    road(K, 0, 23, 0, -6, 4.2, '#D8CCAE'); road(K, -18, 0, 18, 0, 3.2, '#D8CCAE');
    if (cap) glowPool(K, 0, 3, 4.4, '#D8CCAE'); else glowPool(K, 0, -0.5, 5.5, '#DCD2B8');
    mark(K, 'pad');
    const W = { h, t: 3.4, bat: 0.04, kind: 'stone', plinth: 0.9, courses: [2.5, 4.3, 6.0], panel: [3, 5.5], merlon: { w: 1.4, h: 0.95, step: 3.3 } };
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 6.2, r + 6.2]] }));
    wallRun(K, -r, -r, r, -r, W);
    wallRun(K, -r, -r, -r, r, Object.assign({}, W, { skip: [[r - 2.6, r + 2.6]] }));
    wallRun(K, r, -r, r, r, Object.assign({}, W, { skip: [[r - 2.6, r + 2.6]] }));
    mark(K, 'walls');
    const tw = { kind: 'stone', sides: 8, bands: 3, cone: '#C9785A', coneOver: 1.12, coneH: 5.2 };
    for (const [x, z] of [[-r, r], [r, r], [-r, -r]]) roundTower(K, x, 0, z, 3.1, h + 2.6, tw);
    for (const [x, z] of big || cap ? [[-r, -r * 0.1], [r, r * 0.45], [0, -r]] : [[0, -r]]) roundTower(K, x, 0, z, 2.5, h + 1.6, tw);
    block(K, r, 0, -r, 5, 5, h + 0.4, { kind: 'stone', ys: [0.9, 4.2] });
    crown(K, r, h + 0.4, -r, 5, 5, { kind: 'stone', ph: 0.5, merlon: { w: 1.1, h: 0.85, step: 2.2 } });
    mark(K, 'towers');
    // 南门：拱门块 + 双圆塔（大城 / 都城为双拱）
    {
      const gz = r + 0.4, gw = 10.4;
      block(K, 0, 0, gz, gw, 5, h + 1.2, { glow: 0.9, kind: 'stone', ys: [0.9, 4.2], faces: 'fkrl' });
      const arches = cap || big ? [-1.7, 1.7] : [0];
      for (const ax of arches) gateArch(K, ax, gz + 2.5, 0, { w: arches.length > 1 ? 2.6 : 3.4, h: arches.length > 1 ? 4.3 : 5.0, kind: 'round', door: '#7A5038', frame: '#D9CFB8', border: 0.42, glow: 0.85 });
      crown(K, 0, h + 1.2, gz, gw, 5, { kind: 'stone', ph: 0.5, skip: 'k', merlon: { w: 1.1, h: 0.85, step: 2.3 } });
      for (const s of [-1, 1]) roundTower(K, s * (gw / 2 + 1.6), 0, gz + 0.6, 3.2, h + 3.4, tw);
      for (const s of [-1, 1]) torch(K, s * 2.8, 4.6, gz + 2.75);
      for (const s of [-1, 1]) at(K, s * (r + 0.3), 0, 0, s * 90, () => {
        block(K, 0, 0, 0, 6.4, 4.6, h + 0.8, { kind: 'stone', ys: [0.9, 4.2] });
        gateArch(K, 0, 2.3, 0, { w: 2.6, h: 3.9, kind: 'round', door: '#7A5038', frame: '#D9CFB8', border: 0.36 });
        crown(K, 0, h + 0.8, 0, 6.4, 4.6, { kind: 'stone', ph: 0.4, skip: 'klr', merlon: { w: 1.0, h: 0.8, step: 2.1 } });
      });
    }
    mark(K, 'gates');
    if (cap) {
      amphitheatre(K, -7.2, -6.5, 8.6, 7.0, 6.4, {});
      temple(K, 8.2, -9.5, 7.2, 10.5, { cols: 6, colH: 5.4, podium: 1.5 });
      // 柱廊（广场东南）
      colonnade(K, 6.5, 2.2, 9, 0, 5, 4.0);
    } else {
      temple(K, 0, -11.2, 7.4, 10.5, { cols: 6, colH: 5.6, podium: 1.6 });
      patch(K, [[-7, -5.5], [7, -5.5], [7, 4.5], [-7, 4.5]], '#DCD2B8', 0.05);    // 广场
      colonnade(K, -6.4, -0.5, 9, 90, 4, 3.8);
      if (big) {
        // 会堂：长方殿、红瓦双坡顶、高窗
        at(K, 7.8, 0, -0.5, 90, () => {
          body(K, 0, 0, 0, 10, 6.4, 5.4, '#E8DEC8', { plinth: 0.7, plinthColor: '#B4A994' });
          for (let i = 0; i < 4; i++) windowGlow(K, lerp(-3.6, 3.6, i / 3), 3.6, 3.2, 0.7, 1.1, { arch: true, color: '#C08A58' });
          tileRoof(K, 0, 0, { w: 10, d: 6.4, y: 5.4, over: 0.55, rise: 2.0, pal: 'terracotta', hip: false, gable: '#E8DEC8' });
        });
      } else colonnade(K, 6.4, -0.5, 9, 90, 4, 3.8);
    }
    mark(K, 'civic');
    const houses = cap
      ? [[-12.5, 10.5, 6, 4.6], [12, 10.5, 6.2, 4.6], [5.5, 13, 4.8, 4.0], [-5.5, 13, 4.8, 4.0], [13.5, 2.5, 4.6, 5.6, 1]]
      : [[-12, 10.5, 6, 4.6], [12, 10.5, 6.2, 4.6], [-13, 3.5, 5, 5.0, 1], [13, 4, 5, 5.0, 1], [-12.5, -6.5, 5, 5.6, 1], [-12, -14, 5.6, 4.4], [12.5, -9.5, 4.6, 5.0, 1]];
    if (big && !cap) houses.push([-5.5, 13.2, 4.6, 3.8], [5.5, 13.2, 4.6, 3.8]);
    houses.forEach(([x, z, w, d, side], i) => {
      const hh = 3.6 + ((i + v) % 3) * 0.9;
      if (side) at(K, x, 0, z, x < 0 ? 90 : -90, () => medHouse(K, 0, 0, d, w, hh, { hip: (i + v) % 2 === 0 }));
      else medHouse(K, x + (v ? 0.5 : 0), z, w, d, hh, { hip: (i + v) % 3 !== 0 });
    });
    mark(K, 'houses');
    const trees = cap ? [[-15, -15.5], [3.5, -15.8], [15.5, -2]] : v ? [[-6.5, -15.5], [6.8, -15.5], [-15.5, 15.5], [15.5, -3]] : [[-15, -15.5], [6.5, -16], [15.5, 15], [-15.8, -1]];
    trees.forEach(([x, z], i) => (i % 2 ? tree(K, x, z, 0.75, 'cypress') : tree(K, x, z, 0.7, 'olive')));
  };
  // ------------------------------------------------------- 安息 / 波斯 --
  // 土坯城墙（收分、阶梯垛口、勒脚）与半圆扶壁塔、釉砖尖拱门楼、带小穹顶的平顶房、四分花园（水池 + 柏树）、阶梯内城上的伊万宫殿；
  // 都城为泰西封式大拱（Taq Kasra）+ 拜火祠（四拱亭 + 穹顶）
  B.persia = function (K, o) {
    const r = U, h = 7.8, cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'persia');
    yardGlow(K, 'persia', 0, 9, 8.5);
    road(K, 0, 23, 0, cap ? 2 : -3, 4.0, '#DCC79F');
    glowPool(K, 0, 14.5, 4.0, '#DCC79F');
    mark(K, 'pad');
    const W = { h, t: 4.0, bat: 0.12, kind: 'mud', plinth: 1.0, courses: [2.7, 4.5, 6.2], panel: [3, 6], merlon: { w: 1.4, h: 1.1, step: 3.3, stepped: true }, par: { h: 0.7, t: 0.8 } };
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 6.2, r + 6.2]] }));
    wallRun(K, -r, -r, r, -r, W);
    wallRun(K, -r, -r, -r, r, W);
    wallRun(K, r, -r, r, r, W);
    mark(K, 'walls');
    const tw = { kind: 'mud', sides: 8, bands: 2, taper: 0.1 };
    for (const [x, z] of [[-r, r], [r, r], [-r, -r]]) roundTower(K, x, 0, z, 3.3, h + 1.8, tw);
    for (const [x, z] of big || cap ? [[-r, -1], [r, 3], [-5, -r]] : [[-r, -1], [r, 3]]) roundTower(K, x, 0, z, 2.4, h + 0.6, tw);
    block(K, r, 0, -r, 5, 5, h + 0.2, { kind: 'mud', ys: [1.0, 4.4], bat: 0.08 });
    mark(K, 'towers');
    // 门楼：高块、釉砖尖拱、阶梯垛口，两侧圆塔
    {
      const gz = r + 0.6, gw = 11.6, gh = h + 3.2;
      block(K, 0, 0, gz, gw, 6.4, gh, { glow: 0.9, kind: 'mud', ys: [1.0, 4.6, 8.0], bat: 0.08, faces: 'fkrl' });
      onFace(K, 0, gz + 3.2, 0.08, () => {
        archway(K, 0, 0, 0, { w: 3.8, h: 6.6, kind: 'pointed', door: '#7A4A32', frame: '#3E7CA8', border: 0.6, studs: true, glow: 0.85 });
        const M = K.M, ink = M.ink; M.ink = 0;
        M.quad([-4.6, 8.0, 0.06], [4.6, 8.0, 0.06], [4.6, 8.7, 0.06], [-4.6, 8.7, 0.06], [0, 0, 1], H('#3E7CA8'));
        for (let i = 0; i < 6; i++) M.quad([-4.3 + i * 1.6, 8.15, 0.08], [-3.7 + i * 1.6, 8.15, 0.08], [-3.7 + i * 1.6, 8.55, 0.08], [-4.3 + i * 1.6, 8.55, 0.08], [0, 0, 1], H('#E8C860'));
        M.ink = ink;
      });
      crown(K, 0, gh, gz, gw - gh * 0.16, 6.4 - gh * 0.16, { kind: 'mud', ph: 0, pt: 0.7, merlon: { w: 1.0, h: 1.1, step: 2.0, stepped: true } });
      for (const s of [-1, 1]) roundTower(K, s * (gw / 2 + 1.4), 0, gz + 0.8, 3.0, gh + 0.6, tw);
      for (const s of [-1, 1]) torch(K, s * 2.9, 5.2, gz + 3.55);
    }
    mark(K, 'gates');
    // 泰西封式大拱（Taq Kasra）只给泰西封：其余的安息 / 波斯都城（伊斯塔赫尔、苏萨……）用三级台上带穹顶的伊万宫殿 + 拜火祠
    const landmark = cap && o.landmark;
    if (landmark) {
      taqKasra(K, -2, -8.5, 22, 11, 15.5);
      chaharTaq(K, 11.5, 3.5, 6.4);
      fire(K, 11.5, 0.8, 3.5, { s: 0.7, pit: false });
    } else {
      // 阶梯内城（2–3 级台）+ 伊万宫殿
      const tall = big || cap;
      const levels = tall ? [[24, 13, 1.6], [18.5, 10, 1.6], [13, 7.6, 1.5]] : [[21, 12, 1.5], [15, 9, 1.5]];
      let y = 0;
      levels.forEach(([w, d, hh], i) => {
        block(K, 0, y, -9.5 + i * 0.4 - (tall ? 0 : 0.6), w, d, hh, { kind: 'mud', ys: [hh * 0.5], plinth: 0, bat: 0.05, topColor: '#D9C29A' });
        y += hh;
      });
      const M = K.M;
      M.color = H('#CDB38A');
      for (let i = 0; i < levels.length * 2; i++) M.box([0, (i + 0.5) * 0.75, -9.5 + levels[0][1] / 2 + 2.2 - i * 0.62], [3.6, 0.75, 0.7], H('#DCC6A0'), 'tfrl');
      iwan(K, 0, -10.6, tall ? 11 : 9.5, 6.6, 6.8, { y0: y, kind: 'mud', glaze: '#3E7CA8', dome: tall ? (cap ? '#4E8EB0' : '#5E9CB4') : null });
      if (cap) { chaharTaq(K, 11.5, 3.5, 6.4); fire(K, 11.5, 0.8, 3.5, { s: 0.7, pit: false }); }
    }
    mark(K, 'palace');
    // 四分花园：水池 + 柏树
    if (!landmark) {
      water(K, [[-3.2, 4.4], [3.2, 4.4], [3.2, 7.6], [-3.2, 7.6]], 0.08, '#5E9C9C');
      patch(K, [[-6.2, 2.0], [6.2, 2.0], [6.2, 10.0], [-6.2, 10.0]], '#93A862', 0.045);
      for (const s of cap ? [-1] : [-1, 1]) garden(K, s * 8.6, 6.4, 3.4, 4.6, { tree: 'olive', s: 0.45, bush: false });
      for (const [x, z] of [[-4.8, 3.2], [4.8, 3.2], [-4.8, 8.8], [4.8, 8.8]]) tree(K, x, z, 0.55, 'cypress');
    }
    const houses = landmark
      ? [[-13, 11, 5.6, 4.8], [-13.5, 3.5, 5.0, 4.6], [5.5, 12.5, 4.8, 4.2], [14, 11, 5, 4.6], [-13, -14, 5.2, 4.4]]
      : [[-12.5, 11, 5.6, 4.8], [12.5, 11, 5.6, 4.8], [-13.5, 2.5, 5.0, 5.0], [-14.5, -14.5, 4.6, 4.6], [14, -7, 4.6, 5.0]].concat(cap ? [] : [[13.5, 2.5, 5.0, 5.0]]);
    if ((big || cap) && !landmark) houses.push([-6.5, 13.5, 4.4, 3.8], [6.5, 13.5, 4.4, 3.8]);
    houses.forEach(([x, z, w, d], i) => flatHouse(K, x, z, w, d, 3.4 + ((i + v) % 3) * 0.8, {
      col: MUDS[(i + v) % 5], dome: (i + v) % 3 === 0 ? '#D8C49E' : null, arch: true, beams: (i + v) % 3 === 1, storey: (i + v) % 4 === 2 ? 0.5 : 0,
    }));
    mark(K, 'houses');
    for (const [x, z] of v ? [[-7.5, 13.8], [16, -1.5]] : [[7.5, 14], [-16, -6]]) tree(K, x, z, 0.6, 'cypress');
  };
  /** 泰西封大拱（Taq Kasra）：盲拱券的立面墙、中央巨大的抛物线拱洞、后面的筒拱大厅（沿 −Z） */
  function taqKasra(K, x, z, w, d, h) {
    const M = K.M, c = C('#D6BC92'), cd = sh(c, 0.88);
    M.push().translate(x, 0, z);
    // 立面两翼（分层，盲拱）
    const aw = w * 0.36, wing = (w - aw) / 2;
    for (const s of [-1, 1]) {
      const cx = s * (aw / 2 + wing / 2);
      block(K, cx, 0, 0, wing, 3.2, h * 0.86, { kind: 'buff', ys: [1.0, h * 0.3, h * 0.58], faces: s < 0 ? 'fkl' : 'fkr' });
      const ink = M.ink; M.ink = 0;
      for (let row = 0; row < 3; row++) {
        const y0 = 1.6 + row * h * 0.27, ah = h * 0.18, n = 3 + row;
        for (let i = 0; i < n; i++) {
          const ax = cx - wing / 2 + (i + 0.5) * wing / n, hw = wing / n * 0.28;
          M.quad([ax - hw, y0, 1.63], [ax + hw, y0, 1.63], [ax + hw, y0 + ah, 1.63], [ax - hw, y0 + ah, 1.63], [0, 0, 1], sh(c, 0.7));
          M.triangleFacing([ax - hw, y0 + ah, 1.63], [ax + hw, y0 + ah, 1.63], [ax, y0 + ah + hw * 1.2, 1.63], [0, 0, 1], sh(c, 0.7));
        }
      }
      M.ink = ink;
    }
    // 拱洞 + 筒拱（抛物线截面，loft 沿 −Z）
    const n = 8, R = aw / 2, prof = [];
    for (let i = 0; i <= n; i++) { const t = i / n, xx = lerp(-R, R, t); prof.push([xx, h * (1 - Math.pow(xx / R, 2)) * 0.98]); }
    const outer = prof.map(([xx, yy]) => [xx * 1.18, yy * 1.08 + 0.3]);
    const ring = (pts, zz) => pts.map(([xx, yy]) => [xx, yy, zz]);
    M.loft([ring(outer, 1.6), ring(outer, -d)], false, true, nrm => P.shade(c, 0.86 + 0.2 * clamp01(nrm[1])));
    // 拱口的券面（外圈与内圈之间的立面带）
    for (let i = 0; i < n; i++) M.quad([prof[i][0], prof[i][1], 1.62], [prof[i + 1][0], prof[i + 1][1], 1.62], [outer[i + 1][0], outer[i + 1][1], 1.62], [outer[i][0], outer[i][1], 1.62], [0, 0, 1], P.shade(c, i % 2 ? 1.02 : 0.95));
    // 拱内（深色，夜里内部火光）
    const ink = M.ink; M.ink = 0; M.nightEmission = 0.35;
    for (let i = 0; i < n; i++) M.triangleFacing([0, 0, 1.55], [prof[i][0], prof[i][1], 1.55], [prof[i + 1][0], prof[i + 1][1], 1.55], [0, 0, 1], H('#5A4636'));
    M.nightEmission = 0; M.ink = ink;
    M.pop();
    lamp(K, [x, 3, z + 3.2], { color: '#FFB060', range: 12, intensity: 1.4, haloSize: 1.2 });
  }
  /** 拜火祠（四拱亭 chahar taq）：方台、四面拱门、四角墩、穹顶 */
  function chaharTaq(K, x, z, s) {
    const M = K.M, h = s * 0.85;
    block(K, x, 0, z, s + 1.6, s + 1.6, 0.8, { kind: 'mud', ys: [], plinth: 0 });
    block(K, x, 0.8, z, s, s, h, { kind: 'buff', ys: [0.6, h * 0.5] });
    for (let i = 0; i < 4; i++) at(K, x, 0.8, z, i * 90, () => onFace(K, 0, s / 2, 0, () => archway(K, 0, 0, 0, { w: s * 0.45, h: h * 0.75, kind: 'pointed', door: null, frame: '#E2D2AE', border: 0.3 })));
    M.color = H('#E2D2AE');
    const dr = s * 0.42, y = 0.8 + h;
    M.push().translate(x, 0, z);
    M.lathe([[dr * 1.08, y], [dr, y + 0.5], [dr * 0.92, y + 0.5 + dr * 0.45], [dr * 0.55, y + 0.5 + dr * 0.85], [0, y + 0.5 + dr]], 10, false, false, false, [sh(C('#D6C49E'), 0.9), C('#D6C49E'), C('#E2D2AE'), sh(C('#E2D2AE'), 1.05), sh(C('#E2D2AE'), 1.1)]);
    M.pop();
  }

  // ------------------------------------------------------- 哈特拉 / 阿拉伯 --
  // 圆形石城（多边形城墙、圆塔、南门），中央神域：方形围墙内的双伊万神庙与列柱小庙；平顶房、椰枣树
  B.arab = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant, R = 18.6, h = 6.8, n = 14;
    basePad(K, 'arab');
    yardGlow(K, 'arab', 0, 9, 8);
    road(K, 0, 23, 0, -1, 3.8, '#E0D0AA');
    glowPool(K, 0, 11.5, 3.8, '#E0D0AA');
    mark(K, 'pad');
    const pts = ringPts(n, R, R, Math.PI / 2 + Math.PI / n);
    const W = { h, t: 3.2, bat: 0.06, kind: 'buff', plinth: 0.9, courses: [2.4, 4.2, 5.6], panel: [2.6, 4.4], center: [0, 0], merlon: { w: 1.3, h: 0.9, step: 3.2, stepped: true }, par: { h: 0.7, t: 0.7 } };
    for (let i = 0; i < n - 1; i++) wallRun(K, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], W);
    mark(K, 'walls');
    const tw = { kind: 'buff', sides: 8, bands: 2 };
    for (let i = 0; i < n; i++) {
      const [x, z] = pts[i];
      if (i % 2 && !(x > 10 && z < -10)) roundTower(K, x, 0, z, 2.6, h + 1.4, tw);
    }
    mark(K, 'towers');
    // 南门（最后一段的位置）
    {
      const a = pts[n - 1], b = pts[0], gz = (a[1] + b[1]) / 2;
      block(K, 0, 0, gz, Math.abs(a[0] - b[0]) + 1.2, 5.2, h + 2.2, { glow: 0.9, kind: 'buff', ys: [0.9, 4.0], bat: 0.04 });
      gateArch(K, 0, gz + 2.6, 0.04, { w: 3.2, h: 4.8, kind: 'round', door: '#7A4A32', frame: '#E6D8B6', border: 0.45, studs: true, glow: 0.85 });
      crown(K, 0, h + 2.2, gz, Math.abs(a[0] - b[0]) + 0.8, 4.8, { kind: 'buff', ph: 0, pt: 0.6, merlon: { w: 1.0, h: 0.9, step: 2.0, stepped: true } });
      for (const s of [-1, 1]) roundTower(K, s * (Math.abs(a[0] - b[0]) / 2 + 1.6), 0, gz + 0.4, 2.8, h + 3.2, tw);
      for (const s of [-1, 1]) torch(K, s * 2.6, 4.6, gz + 2.75);
    }
    mark(K, 'gates');
    // 神域围墙（方形、矮、阶梯垛）+ 双伊万 + 列柱小庙
    {
      const tz = -4.5, tw2 = cap ? 21 : 17, td = cap ? 15 : 12.5, Wt = { h: 3.0, t: 1.4, kind: 'buff', plinth: 0.6, courses: [1.8], panel: [3, 5], par: { h: 0.5, t: 0.5 }, merlon: { w: 0.8, h: 0.7, step: 1.8, stepped: true }, center: [0, tz] };
      wallRun(K, -tw2 / 2, tz + td / 2, tw2 / 2, tz + td / 2, Object.assign({}, Wt, { skip: [[tw2 / 2 - 2.4, tw2 / 2 + 2.4]] }));
      wallRun(K, -tw2 / 2, tz - td / 2, -tw2 / 2, tz + td / 2, Wt);
      wallRun(K, tw2 / 2, tz - td / 2, tw2 / 2, tz + td / 2, Wt);
      patch(K, [[-tw2 / 2 + 0.7, tz - td / 2], [tw2 / 2 - 0.7, tz - td / 2], [tw2 / 2 - 0.7, tz + td / 2 - 0.7], [-tw2 / 2 + 0.7, tz + td / 2 - 0.7]], '#E2D4B2', 0.06);
      const iw = cap ? 7.6 : 6.4;
      for (const s of [-1, 1]) iwan(K, s * (iw / 2 + 0.1), tz - td / 2 + 3.6, iw, 7, cap ? 8.2 : 7.2, { kind: 'buff', glaze: false, raise: 1.6, pw: iw });
      if (big || cap) temple(K, cap ? 7.8 : 6.0, tz + 1.8, 4.6, 5.6, { cols: 4, colH: 3.6, podium: 1.0, stone: '#E6D8B8', roof: 'terracotta' });
      else { const M = K.M; M.color = H('#E6D8B8'); for (const s of [-1, 1]) for (const zz of [0, 2.6]) M.cylinder([s * 4.2, 0, tz + zz], 0.36, 0.3, 4.2, 6, false, false, true); }
    }
    mark(K, 'temple');
    const houses = [[-11, 9.5, 5.2, 4.4], [10.5, 9.5, 5.2, 4.4], [-14.2, 0.5, 4.2, 5.0], [14.2, 1.5, 4.2, 5.0], [-11.8, -12.5, 4.6, 4.2], [5, 14.2, 4.0, 3.2]];
    if (big || cap) houses.push([-5, 14.2, 4.0, 3.2], [11, -12, 4.0, 4.2]);
    houses.forEach(([x, z, w, d], i) => flatHouse(K, x, z, w, d, 3.2 + ((i + v) % 3) * 0.7, { col: MUDS[(i + v + 2) % 5], arch: (i + v) % 2 === 0, beams: true, storey: (i + v) % 3 === 2 ? 0.5 : 0 }));
    for (const [x, z] of v ? [[-6.5, 9], [7, 6.5]] : [[-7, 6.5], [6.5, 9.5]]) garden(K, x, z, 3.6, 3.6, { bush: true });
    mark(K, 'houses');
    for (const [x, z] of v ? [[-6.5, 9], [7, 6.5], [-15, -6.5]] : [[-7, 6.5], [6.5, 9.5], [15, -6]]) palm(K, x, z, 0.7, 0, x * 13 + z);
    // 城外三角的椰枣园（东北角留给旗）
    for (const [x, z] of [[-18, 17.5], [18, 17.5], [-18, -17.5]]) { garden(K, x, z, 4.4, 3.8, { bush: false, col: '#97A862' }); palm(K, x - 0.8, z + 0.4, 0.62, 0, x * 3 + z); palm(K, x + 1.2, z - 0.8, 0.55, 0, x * 5 - z); }
  };

  // -------------------------------------------------------------- 贵霜 --
  // 土坯 / 砖城（收分、圆角大堡、方形马面）、白色窣堵波（栏楯、平头、伞盖）与小塔、方形僧院（回廊僧房）、平顶房；
  // 都城为高台上的列柱穹顶宫殿（苏尔赫·科塔尔式）
  B.kushan = function (K, o) {
    const r = U, h = 7.0, cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'kushan');
    yardGlow(K, 'kushan', 2, 8, 8);
    road(K, 0, 23, 0, -2, 3.8, '#D8C29C');
    glowPool(K, 0, 12, 4.2, '#D8C29C');
    mark(K, 'pad');
    const W = { h, t: 3.8, bat: 0.1, kind: 'buff', plinth: 1.0, courses: [2.6, 4.2, 5.6], panel: [3, 6], merlon: { w: 1.3, h: 0.9, step: 2.8 } };
    const bs = (u, w) => ({ u, w: w || 4.2, d: 1.8 });
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 5.4, r + 5.4]], bastions: [bs(r * 0.45), bs(r * 1.55)] }));
    wallRun(K, -r, -r, r, -r, Object.assign({}, W, { bastions: [bs(r * 0.6)] }));
    wallRun(K, -r, -r, -r, r, Object.assign({}, W, { bastions: [bs(r * 0.5), bs(r * 1.5)] }));
    wallRun(K, r, -r, r, r, Object.assign({}, W, { bastions: [bs(r * 1.4)] }));
    mark(K, 'walls');
    for (const [x, z] of [[-r, r], [r, r], [-r, -r]]) roundTower(K, x, 0, z, 3.6, h + 1.4, { kind: 'buff', sides: 9, bands: 3, taper: 0.1 });
    block(K, r, 0, -r, 5, 5, h + 0.2, { kind: 'buff', ys: [1.0, 4.0], bat: 0.08 });
    mark(K, 'towers');
    {
      const gz = r + 0.5;
      block(K, 0, 0, gz, 10.4, 6, h + 2.0, { glow: 0.9, kind: 'buff', ys: [1.0, 4.4], bat: 0.06 });
      gateArch(K, 0, gz + 3, 0.06, { w: 3.2, h: 4.8, kind: 'round', door: '#7A4A32', frame: '#E8DCC0', border: 0.42, glow: 0.85 });
      crown(K, 0, h + 2.0, gz, 10.4 - 0.24, 6 - 0.24, { kind: 'buff', ph: 0.5, merlon: { w: 1.1, h: 0.8, step: 2.2 } });
      // 门上小亭（木柱、平顶）
      const M = K.M; M.color = H('#7A5A3E');
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.cylinder([sx * 2.2, h + 2.5, gz + sz * 1.6], 0.2, 0.18, 2.4, 4, false, false, false);
      flatTop(K, 0, h + 4.9, gz, 5.6, 4.4, C('#D8C09A'), 0.3);
      for (const s of [-1, 1]) torch(K, s * 2.5, 4.4, gz + 3.35);
    }
    mark(K, 'gates');
    if (cap) {
      // 高台宫殿：三级台 + 前阶 + 列柱前廊 + 穹顶
      const M = K.M;
      block(K, 0, 0, -8.5, 20, 13, 1.6, { kind: 'buff', ys: [0.8], plinth: 0, topColor: '#D8C29C' });
      block(K, 0, 1.6, -9.2, 15, 10, 1.6, { kind: 'buff', ys: [0.8], plinth: 0, topColor: '#E0CCA6' });
      M.color = H('#CDB48A'); for (let i = 0; i < 4; i++) M.box([0, (i + 0.5) * 0.8, -2.4 + 1.2 - i * 0.7], [4.2, 0.8, 0.7], H('#DCC6A0'), 'tfrl');
      const y0 = 3.2, hz = -10.4, hh = 4.6;
      M.color = H('#E8DCC4'); M.box([0, y0 + hh / 2, hz - 0.8], [12, hh, 6.4], null, 'fkrl');
      for (let i = 0; i < 5; i++) windowGlow(K, -4.4 + i * 2.2, y0 + 2.4, hz + 2.4, 0.7, 1.4, { arch: true });
      M.color = H('#F0E8D8'); for (let i = 0; i < 6; i++) M.cylinder([-5.5 + i * 2.2, y0, hz + 3.4], 0.34, 0.3, hh, 6, false, false, false);
      M.color = H('#D8C4A0'); M.box([0, y0 + hh + 0.25, hz + 0.4], [13.2, 0.5, 8.8], H('#E2D0AE'), 'tfkrl');
      M.color = H('#ECE6D6'); M.push().translate(0, y0 + hh + 0.5, hz - 0.8);
      M.lathe([[3.2, 0], [3.1, 0.6], [2.7, 2.1], [1.5, 3.4], [0, 3.8]], 10, false, false, false, [H('#D8D0BE'), H('#ECE6D6'), H('#F2ECE0'), H('#F6F2E8'), H('#FAF6EE')]);
      M.pop(); M.color = PAL.gold; M.cone([0, y0 + hh + 0.5 + 3.7, hz - 0.8], 0.3, 1.2, 5);
      lamp(K, [0, y0 + 1.6, hz + 4.2], { color: '#FFB060', range: 9, intensity: 1.1, haloSize: 0.8 });
      stupaLP(K, 11.5, 3.5, 2.6);
    } else {
      stupaLP(K, 0, -8.8, big ? 4.4 : 3.8);
      for (const [x, z] of [[-6.5, -10.5], [6.5, -10.5]]) stupaLP(K, x, z, 1.3);
      // 僧院：四排僧房围合的方院（西侧）
      const vx = -10, vz = -1, vs = big ? 10 : 9;
      for (const [cx, cz, w, d, rot] of [[0, -vs / 2 + 1.2, vs, 2.4, 0], [0, vs / 2 - 1.2, vs, 2.4, 180], [-vs / 2 + 1.2, 0, vs - 4.8, 2.4, 90], [vs / 2 - 1.2, 0, vs - 4.8, 2.4, -90]])
        flatHouse(K, vx + cx, vz + cz, w, d, 3.0, { yaw: rot, col: '#D6BE92', win: 2, door: '#5E4232', parapet: 0.35 });
      tree(K, vx, vz, 0.55, 'bodhi');
    }
    mark(K, 'temple');
    const houses = [[11.5, 10.5, 6, 4.6], [-12, 11, 5.6, 4.4], [13.5, 2, 4.6, 5.4], [13.5, -6.5, 4.6, 4.8]];
    if (big || cap) houses.push([-5.5, 13.5, 4.4, 3.6], [5.5, 13.5, 4.4, 3.6]);
    if (cap) houses.push([-13.5, 1.5, 4.6, 5.2], [-13.5, -14, 5, 4.4]);
    houses.forEach(([x, z, w, d], i) => flatHouse(K, x, z, w, d, 3.2 + ((i + v) % 3) * 0.7, { col: MUDS[(i + v + 1) % 5], beams: true, storey: (i + v) % 3 === 1 ? 0.5 : 0 }));
    garden(K, v ? 7 : -7, v ? 7 : 7.5, 4, 4, { bush: true });
    mark(K, 'houses');
    for (const [x, z] of v ? [[7, 7], [-15.5, -15.5]] : [[-7, 7.5], [16, -14]]) tree(K, x, z, 0.65, 'poplar');
  };

  // -------------------------------------------------------------- 天竺 --
  // 环壕、红砖城墙与半圆马面、筒拱顶门楼、带塔门（torana）的窣堵波、支提堂（筒拱顶、马蹄窗）、平顶 / 拱顶民居、椰树、菩提树；
  // 都城为多层宫殿（支提屋顶）
  B.india = function (K, o) {
    const r = U, h = 6.6, cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'india');
    yardGlow(K, 'india', 0, 8, 8.5);
    ringBand(K, 21.2, 1.8, 0.06, '#5E9C94', 2.6);
    road(K, 0, 23.6, 0, -3, 3.6, '#D2B48A', 0.09);
    glowPool(K, 0, 12, 4.2, '#D2B48A', { y: 0.1 });
    mark(K, 'pad');
    const W = { h, t: 3.4, bat: 0.08, kind: 'redbrick', plinth: 0.9, courses: [2.2, 3.6, 5.0], panel: [3.0, 5.2], merlon: { w: 1.4, h: 1.0, step: 3.3, round: true } };
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 5.2, r + 5.2]] }));
    wallRun(K, -r, -r, r, -r, W);
    wallRun(K, -r, -r, -r, r, W);
    wallRun(K, r, -r, r, r, W);
    mark(K, 'walls');
    const tw = { kind: 'redbrick', sides: 8, bands: 2 };
    for (const [x, z] of [[-r, r], [r, r], [-r, -r]]) roundTower(K, x, 0, z, 3.0, h + 1.2, tw);
    for (const [x, z] of big || cap ? [[-r, 0], [r, 0], [-7, -r], [7, -r]] : [[-r, 0], [r, 0], [0, -r]]) roundTower(K, x * 1.04, 0, z * 1.04, 2.3, h, tw);
    block(K, r, 0, -r, 4.6, 4.6, h + 0.2, { kind: 'redbrick', ys: [0.9, 3.6], bat: 0.06 });
    mark(K, 'towers');
    {
      const gz = r + 0.4, M = K.M;
      block(K, 0, 0, gz, 9.6, 5.6, h + 1.4, { glow: 0.9, kind: 'redbrick', ys: [0.9, 4.0], bat: 0.05 });
      gateArch(K, 0, gz + 2.8, 0.05, { w: 3.0, h: 4.6, kind: 'round', door: '#6E4430', frame: '#E2C8A0', border: 0.4, glow: 0.85 });
      // 门上筒拱顶小楼（木柱 + 支提屋顶）
      M.color = H('#E2D2B4'); M.box([0, h + 1.4 + 1.3, gz], [6.4, 2.6, 3.6], null, 'fkrl');
      for (let i = 0; i < 3; i++) windowGlow(K, -2 + i * 2, h + 1.4 + 1.4, gz + 1.8, 0.7, 1.0, { arch: true });
      M.push().translate(0, 0, gz).rotate(0, 90, 0);
      vaultRoof(K, 0, h + 1.4 + 2.6, 0, 3.6, 7.2, { col: '#B86C4E', gable: '#D8B888' });
      M.pop();
      for (const s of [-1, 1]) torch(K, s * 2.4, 4.3, gz + 3.15);
    }
    mark(K, 'gates');
    if (cap) {
      // 宫殿：两层，平台、列柱廊、上层支提顶
      const M = K.M;
      block(K, 3, 0, -9.5, 18, 10, 1.4, { kind: 'redbrick', ys: [0.7], plinth: 0, topColor: '#D8BC94' });
      M.color = H('#E8D8B8'); M.box([3, 1.4 + 2.2, -10.4], [14, 4.4, 7.4], null, 'fkrl');
      for (let i = 0; i < 6; i++) windowGlow(K, 3 - 6 + i * 2.4, 1.4 + 2.4, -10.4 + 3.7, 0.8, 1.4, { arch: true });
      M.color = H('#C8A070'); for (let i = 0; i < 7; i++) M.cylinder([3 - 7 + i * 14 / 6, 1.4, -10.4 + 4.6], 0.3, 0.26, 4.4, 6, false, false, false);
      M.box([3, 1.4 + 4.6, -10.4 + 0.6], [15, 0.5, 9.2], H('#D8BC94'), 'tfkrl');
      vaultRoof(K, 3, 1.4 + 4.85, -10.6, 8.5, 5.2, { col: '#B86C4E', gable: '#E2C8A0' });
      for (const s of [-1, 1]) vaultRoof(K, 3 + s * 5.4, 1.4 + 4.85, -10.6, 3.2, 4.2, { col: '#A85E44', gable: '#E2C8A0' });
      stupaLP(K, -9, -8.5, 2.8);
      torana(K, -9, -3.2, 0, 0.8);
    } else {
      stupaLP(K, 0, -10, big ? 4.2 : 3.6);
      torana(K, 0, -3.6, 0, big ? 1.05 : 0.95);
      torana(K, 7.2, -10, 90, 0.9);
      // 支提堂（西）
      const M = K.M;
      M.color = H('#E2CCA4'); M.box([-11.5, 2.0, -6], [6, 4, 9], null, 'fkrl');
      M.push().translate(0, 0, 0);
      vaultRoof(K, -11.5, 4.0, -6, 6, 9, { col: '#B86C4E', gable: '#E2CCA4' });
      M.pop();
      lamp(K, [-11.5, 2.2, -1.0], { color: '#FFB060', range: 8, intensity: 1.0, haloSize: 0.7 });
    }
    mark(K, 'temple');
    const houses = [[-11.5, 10.5, 5.6, 4.6], [11.5, 10.5, 5.6, 4.6], [13.2, 1.5, 4.8, 5.2], [12.5, -8, 4.6, 4.8]];
    if (big || cap) houses.push([-5.5, 13.6, 4.4, 3.4], [5.5, 13.6, 4.4, 3.4], [-13.5, 2.5, 4.6, 4.6]);
    houses.forEach(([x, z, w, d], i) => {
      const hh = 3.2 + ((i + v) % 2) * 0.8;
      if ((i + v) % 2) {
        const M = K.M;
        body(K, x, 0, z, w, d, hh, (i + v) % 3 ? '#E2CCA4' : '#D8B890', { plinth: 0.4, plinthColor: '#A88E70' });
        windowGlow(K, x + w * 0.25, hh * 0.6, z + d / 2, 0.55, 0.65, { arch: true });
        M.ink = 0; M.quad([x - 0.5, 0.4, z + d / 2 + 0.03], [x + 0.5, 0.4, z + d / 2 + 0.03], [x + 0.5, 2.2, z + d / 2 + 0.03], [x - 0.5, 2.2, z + d / 2 + 0.03], [0, 0, 1], H('#5E4232')); M.ink = 1;
        M.push().translate(x, 0, z).rotate(0, 90, 0); vaultRoof(K, 0, hh, 0, d, w, { col: '#A85E44', gable: '#D8B890', k: 0.8 }); M.pop();
      } else flatHouse(K, x, z, w, d, hh, { col: '#D8BA90', roof: '#C9A47C', arch: true });
    });
    mark(K, 'houses');
    for (const [x, z] of v ? [[-7, 7.5], [8, 6], [-16, -14]] : [[7.5, 7.5], [-8, 6.5], [-15.5, -15]]) palm(K, x, z, 0.75, 0, x * 7 + z * 3);
    if (!cap) tree(K, v ? 10 : -9.5, v ? -14.5 : -14.5, 0.62, 'bodhi');
  };
  // ------------------------------------------------------- 高句丽 / 三韩 --
  // 山城：后半城的山丘（殿阁在山顶）、沿地势的不规则灰石墙（低矮、垛口）、石台券门 + 灰瓦门楼（丹青檐下）、
  // 瓦顶与草顶（面包形）民居、松树；都城：山顶重檐大殿 + 两座楼阁
  B.korea = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'korea');
    yardGlow(K, 'korea', 0, 8, 8);
    const hy = mound(K, -1.5, -8.5, 15.5, 10.5, 4.2, { flat: 0.52, top: '#A9B274', side: '#8E9A5E' });
    road(K, 0, 23, 0, 2, 3.4, '#CDB88E');
    glowPool(K, 0, 9.5, 4.4, '#CDB88E');
    mark(K, 'pad');
    // 不规则城墙（10 边形，半径 18–20，南面留门）
    const pts = [];
    for (let i = 0; i < 10; i++) { const a = Math.PI / 2 + Math.PI / 10 + i * Math.PI * 2 / 10, rr = 19.2 + (i % 3 === 1 ? 1.2 : i % 3 === 2 ? -0.8 : 0); pts.push([Math.cos(a) * rr, Math.sin(a) * rr * 0.98]); }
    const W = { h: 5.4, t: 2.8, bat: 0.12, kind: 'greystone', plinth: 0.8, courses: [2.0, 3.6], panel: [1.8, 3.4], center: [0, 0], merlon: { w: 1.0, h: 0.8, step: 2.2 }, par: { h: 0.7, t: 0.6 } };
    for (let i = 0; i < 9; i++) wallRun(K, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], W);
    mark(K, 'walls');
    // 南门：石台券门 + 门楼（灰瓦四坡、丹青）
    {
      const a = pts[9], b = pts[0], gz = (a[1] + b[1]) / 2, gw = Math.abs(a[0] - b[0]) + 1.4, M = K.M;
      block(K, 0, 0, gz, gw, 6, 6.2, { glow: 0.9, kind: 'greystone', ys: [0.8, 3.4], bat: 0.08 });
      gateArch(K, 0, gz + 3, 0.08, { w: 3.0, h: 4.4, kind: 'round', door: '#8A3A2C', frame: '#D2CCBC', border: 0.4, studs: true, glow: 0.85 });
      M.color = PAL.stone; M.box([0, 6.45, gz], [gw * 0.7, 0.5, 4.2], sh(PAL.stone, 1.08), 'tfkrl');
      M.color = H('#E8E0CE'); M.box([0, 6.7 + 1.5, gz], [gw * 0.56, 3.0, 3.2], null, 'fkrl');
      lattice(K, 0, 6.7 + 1.6, gz + 1.6, gw * 0.36, 1.4, {});
      M.color = H('#8A3A2C'); for (const s of [-1, 1]) M.cylinder([s * gw * 0.28, 6.7, gz + 1.65], 0.22, 0.2, 3.0, 6, false, false, false);
      hanRoof(K, 0, gz, { style: 'hip', w: gw * 0.56, d: 3.2, y: 9.7, over: 1.3, detail: 'low', palette: 'grey', lift: 0.6, fascia: '#5E8A6A', soffit: false });
      for (const s of [-1, 1]) lantern(K, s * (gw * 0.28 + 0.1), 8.5, gz + 2.2, { r: 0.75 });
    }
    mark(K, 'gates');
    // 山顶殿阁
    const M = K.M;
    const hz = -9.5, hw = cap ? 11.6 : big ? 10.4 : 9.2, hd = cap ? 6.6 : 6.0;
    M.color = H('#CFC8B6'); M.box([-1.5, hy + 0.5, hz], [hw + 3, 1.0, hd + 3], H('#DCD6C6'), 'tfkrl');
    hanHall(K, -1.5, hy + 1.0, hz, hw, hd, 4.2, { style: 'xieshan', detail: 'mid', over: 1.6, palette: 'grey', door: true, lanterns: true, wall: '#E8E0CE' });
    if (cap) {
      M.color = H('#E8E0CE'); M.box([-1.5, hy + 1.0 + 4.2 + 1.4, hz], [hw * 0.68, 2.8, hd * 0.66], null, 'fkrl');
      lattice(K, -1.5, hy + 1.0 + 4.2 + 1.4, hz + hd * 0.33, hw * 0.36, 1.3, {});
      hanRoof(K, -1.5, hz, { style: 'hip', w: hw * 0.68, d: hd * 0.66, y: hy + 1.0 + 4.2 + 2.8, over: 1.4, detail: 'mid', palette: 'grey', lift: 0.55, fascia: '#5E8A6A' });
    }
    if (big || cap) for (const s of [-1, 1]) {
      const px = -1.5 + s * (hw / 2 + 4.2), pz = hz + 2.5;
      M.color = H('#CFC8B6'); M.box([px, hy + 0.4, pz], [4.6, 0.8, 4.6], H('#DCD6C6'), 'tfkrl');
      M_hall_small(K, px, hy + 0.8, pz, 3.4, 3.4, 2.6, false, true);
    }
    mark(K, 'hall');
    // 民居（瓦顶 / 草顶）在前半城
    const houses = [[-11.5, 9.5, 5.8, 4.2, 't'], [11, 9.5, 5.8, 4.2, 'c'], [-13, 2.5, 5.2, 4.0, 'c'], [12.5, 2.5, 5.2, 4.2, 't'], [6.5, 14.8, 4.4, 3.4, 'c']];
    if (big || cap) houses.push([-6.5, 14.8, 4.4, 3.4, 't'], [14.5, -6.5, 4.4, 3.8, 'c']);
    houses.forEach(([x, z, w, d, k], i) => {
      if (k === 't' || (i + v) % 4 === 3) hanHouse(K, x, z, w, d, 3.2, { roof: 'grey', hip: (i + v) % 2 === 0 });
      else {
        body(K, x, 0, z, w, d, 2.5, '#E6DCC4', { plinth: 0.4, plinthColor: '#8E877C' });
        windowGlow(K, x + w * 0.25, 1.5, z + d / 2, 0.6, 0.6, { color: '#D8C08A' });
        M.ink = 0; M.quad([x - 0.5, 0.4, z + d / 2 + 0.03], [x + 0.5, 0.4, z + d / 2 + 0.03], [x + 0.5, 2.1, z + d / 2 + 0.03], [x - 0.5, 2.1, z + d / 2 + 0.03], [0, 0, 1], H('#6E4632')); M.ink = 1;
        thatchLoaf(K, x, 2.5, z, w + 1.0, d + 1.4, 1.6, { color: '#C4A86A' });
      }
    });
    mark(K, 'houses');
    for (const [x, z, s] of v ? [[-13, -9, 0.8], [9.5, -15, 0.75], [15, 8, 0.6], [-15.5, 14, 0.55]] : [[-12.5, -12, 0.8], [8.5, -15.5, 0.7], [-16, 9, 0.6], [15.5, 14.5, 0.55]]) conifer(K, x, z, s, 'pine', x < 10 && z < -6 ? hy * 0.55 : 0);
  };

  // -------------------------------------------------------------- 倭 --
  // 环壕集落（邪马台 / 吉野里式）：外圈水壕、土垒 + 尖头木栅、木门楼、高耸的望楼、高床仓库（草顶）、竖穴住居；
  // 都城：带千木与坚鱼木的大型高床殿
  B.wa = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant;
    const edge = basePad(K, 'wa');
    yardGlow(K, 'wa', 0, 4, 8);
    // 外壕（椭圆环水面）+ 土垒 + 木栅
    waterRing(K, 19.8, 19.0, 2.4, 0.05, '#5E9690', 0.22);
    bankRing(K, 16.6, 16.2, 2.6, 1.0, { n: 22, gap: 0.2, color: '#8C9A5C' });
    road(K, 0, edge, 0, 4, 3.0, '#C8B488');
    glowPool(K, 0, 6, 5, '#C8B488');
    mark(K, 'pad');
    palisade(K, ringPts(22, 16.6, 16.2, 0), 2.6, { y0: 1.0, step: 1.5, r: 0.48, skip: southGap(0.24), color: '#8A6844' });
    mark(K, 'walls');
    woodGate(K, 0, 16.6, 0, 4.0, 4.0, { y0: 0.2 });
    // 望楼（高柱 + 平台 + 小草顶）
    const tower = (x, z, hh) => {
      const M = K.M;
      M.color = H('#7A5A3E');
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.cylinder([x + sx * 1.3, 0, z + sz * 1.3], 0.24, 0.2, hh, 4, false, false, false);
      M.color = H('#8A6A48'); M.box([x, hh, z], [3.6, 0.3, 3.6], H('#9A7A54'), 'tfkrl');
      M.color = H('#9A7A54'); M.box([x, hh + 0.6, z], [3.4, 0.9, 3.4], null, 'fkrl');
      thatchCone(K, x, hh + 1.6, z, 2.6, 2.2, { sides: 4, color: '#B49A62', rot: 45 });
      lamp(K, [x, hh + 1.1, z + 1.9], { color: '#FF9A40', range: 8, intensity: 1.2, flicker: 0.2, haloSize: 0.8 });
    };
    for (const [x, z] of big || cap ? [[-9.5, 11.5], [9.5, 11.5], [-14, -4]] : [[-9.5, 11.5], [9.5, 11.5]]) tower(x, z, 6.4);
    mark(K, 'towers');
    if (cap) {
      // 大型高床殿：千木（山墙交叉）、坚鱼木（脊上横木）、两侧高床仓
      const M = K.M;
      stiltHouse(K, 0, -5.5, 10, 6.6, { postH: 2.6, wallH: 2.6, thatch: '#B49660', sweep: 0, roofH: 4.6, over: 1.2, wood: '#7A5A3E', wall: '#A88660' });
      const ry = 2.6 + 0.3 + 2.6 + 4.6;
      M.color = H('#6A4A30');
      for (const s of [-1, 1]) for (const f of [-1, 1]) M.beam([s * 6.0, ry - 0.6, -5.5], [s * 6.5, ry + 1.6, -5.5 + f * 1.3], 0.24, 0.24, [s, 0, 0]);
      for (let i = 0; i < 5; i++) { M.push().translate(-3.6 + i * 1.8, ry + 0.25, -5.5).rotate(90, 0, 0); M.cylinder([0, -0.9, 0], 0.26, 0.26, 1.8, 5, false, true, true); M.pop(); }
      M.color = PAL.gold; M.box([0, ry + 0.05, -5.5], [10.4, 0.25, 0.4], null, 'tfkrl');
      for (const s of [-1, 1]) stiltHouse(K, s * 9.5, -11, 4.6, 3.8, { postH: 2.0, wallH: 1.8, thatch: '#B89A62', sweep: 0.3, roofH: 3.0, over: 0.8, lamp: false, yaw: 90 });
    } else {
      (big ? [[-6, -9], [1.5, -11], [9, -8]] : [[-5, -9.5], [3.5, -10.5]]).forEach(([x, z], i) => stiltHouse(K, x, z, 4.6, 3.8, { postH: 2.2, wallH: 1.8, thatch: '#B89A62', sweep: 0.3, roofH: 3.0, over: 0.8, lamp: false, yaw: (i + v) % 2 ? 90 : 0 }));
    }
    mark(K, 'granaries');
    const pits = cap ? [[-11, 2], [11, 3], [-6, 8.5], [6, 8.5], [12, -7]] : [[-10.5, 1], [10.5, 2], [-4.5, 5.5], [4.5, 7.5], [-11, -8.5], [10.5, -6.5]];
    if (big && !cap) pits.push([0, 1.5], [-3, -3]);
    pits.forEach(([x, z], i) => pitHouse(K, x + (v ? 0.6 : 0), z, 2.8 + (i % 3) * 0.3, { yaw: (i * 47 + v * 20) % 60 - 30 }));
    fire(K, v ? 2 : -1.5, 0, 3.5, { s: 0.6 });
    mark(K, 'houses');
    for (const [x, z] of v ? [[-15, 10], [15.5, -10]] : [[15, 9.5], [-15.5, -9]]) tree(K, x, z, 0.65, 'oak');
  };
  /** 椭圆环形水面（不描墨线）：中心半径 R / Rz、宽 w，南面缺口 gap（弧度，土桥） */
  function waterRing(K, R, Rz, w, y, col, gap, nSeg, noBanks) {
    const n = nSeg || 24, M = K.Mp, c = C(col), ri = R - w / 2, ro = R + w / 2;
    for (let i = 0; i < n; i++) {
      const a0 = i / n * Math.PI * 2, a1 = (i + 1) / n * Math.PI * 2, am = (a0 + a1) / 2;
      if (gap && Math.abs(Math.atan2(Math.sin(am - Math.PI / 2), Math.cos(am - Math.PI / 2))) < gap) continue;
      const Q = (a, rr) => [Math.cos(a) * rr, y, Math.sin(a) * rr * Rz / R];
      M.quad(Q(a0, ro), Q(a1, ro), Q(a1, ri), Q(a0, ri), [0, 1, 0], P.shade(c, 0.95 + 0.1 * K.r()));
      if (noBanks) continue;
      // 岸（内外两条斜坡）
      const bank = H('#8C7A5A');
      M.quad(Q(a0, ro + 0.9).map((v, k) => (k === 1 ? 0.02 : v)), Q(a1, ro + 0.9).map((v, k) => (k === 1 ? 0.02 : v)), Q(a1, ro), Q(a0, ro), [0, 1, 0], P.shade(bank, 0.95 + 0.1 * K.r()));
      M.quad(Q(a0, ri), Q(a1, ri), Q(a1, ri - 0.9).map((v, k) => (k === 1 ? 0.02 : v)), Q(a0, ri - 0.9).map((v, k) => (k === 1 ? 0.02 : v)), [0, 1, 0], P.shade(bank, 0.95 + 0.1 * K.r()));
    }
  }

  // -------------------------------------------------------------- 草原 --
  // 毡帐营地：车阵（篷车环）、蒙古包、马栏与马、马尾纛、篝火；都城为金顶大帐与卫帐
  B.steppe = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant;
    const edge = basePad(K, 'steppe');
    yardGlow(K, 'steppe', 0, 4, 8);
    road(K, 0, edge, 0, 4, 3.2, '#C8B486');
    glowPool(K, 0, 6, 5, '#C8B486');
    mark(K, 'pad');
    // 车阵：环上的篷车（切向），南面留口
    const nW = big || cap ? 10 : 8;
    for (let i = 0; i < nW; i++) {
      const a = Math.PI / 2 + 0.55 + i * (Math.PI * 2 - 1.1) / (nW - 1), x = Math.cos(a) * 17.2, z = Math.sin(a) * 16.8;
      if (x > 11 && z < -11) continue;
      wagon(K, x, z, -a / D2R + 90, { cover: (i + v) % 3 ? '#E2D6BA' : '#C8A878', len: 4.4 });
    }
    mark(K, 'walls');
    if (cap) {
      yurt(K, 0, -4.5, 6.4, { roof: '#D8A838', felt: '#F2EAD6', band: '#A8402E', door: '#C43A2A', finial: true });
      for (const s of [-1, 1]) yurt(K, s * 8.5, -8, 3.4, { band: '#8A5A3C' });
      for (const s of [-1, 1]) tug(K, s * 4.2, 4.5, 6.5, s < 0 ? '#F2EAD6' : '#2E2A28');
    } else {
      yurt(K, 0, -5.5, big ? 4.6 : 4.0, { roof: '#E6D6B4', band: '#A8402E', door: '#C43A2A' });
      tug(K, 3.8, 0.5, 6.0, '#F2EAD6');
    }
    const ys = cap ? [[-10, 5, 3.0], [10, 4.5, 3.0], [-4, 11.5, 2.6], [12.5, -2, 2.6]] : [[-9.5, 3.5, 3.0], [9.5, 4, 2.8], [-11.5, -6, 2.8], [-3.5, 11, 2.6], [6, 11.5, 2.5]];
    if (big && !cap) ys.push([10.5, -6.5, 2.6], [-6.5, -12.5, 2.5]);
    ys.forEach(([x, z, rr], i) => yurt(K, x + (v ? 0.8 : 0), z, rr, { yaw: (i * 53) % 40 - 20, felt: i % 3 === 2 ? '#E2D4BA' : '#EEE4D0', door: i % 2 ? '#B85A32' : '#4E7AA0' }));
    mark(K, 'yurts');
    corral(K, v ? -12 : 12, v ? -9 : 10.5, 3.6, { n: 9, gate: 2 });
    horse(K, (v ? -12 : 12) - 0.8, (v ? -9 : 10.5) + 0.4, 30, '#7A5236');
    horse(K, (v ? -12 : 12) + 1.2, (v ? -9 : 10.5) - 0.8, -60, '#C8B49A');
    fire(K, v ? -2.5 : 2.5, 0, 4.2, { s: 0.7 });
    mark(K, 'rest');
  };

  // -------------------------------------------------------------- 西域 --
  // 绿洲城：风蚀的夯土城墙（低、垛口残缺、马面）、平顶土房、钻天杨、水渠与水池、果园、烽燧；大城 / 都城为佛塔与佛寺
  B.tarim = function (K, o) {
    const r = 19.5, h = 6.0, cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'tarim');
    yardGlow(K, 'tarim', 0, 12, 7);
    road(K, 0, 23, 0, -2, 3.6, '#E2D0A8');
    glowPool(K, 0, 14.5, 3.6, '#E2D0A8');
    mark(K, 'pad');
    const W = { h, t: 3.6, bat: 0.16, kind: 'mud', plinth: 0.8, courses: [2.2, 3.8], panel: [2.4, 5], merlon: { w: 1.5, h: 0.8, step: 3.4 }, par: { h: 0.6, t: 0.7 } };
    const bs = u => ({ u, w: 4.0, d: 1.6 });
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 4.6, r + 4.6]], bastions: [bs(r * 0.4), bs(r * 1.6)] }));
    wallRun(K, -r, -r, r, -r, Object.assign({}, W, { bastions: [bs(r * 0.7)] }));
    wallRun(K, -r, -r, -r, r, Object.assign({}, W, { bastions: [bs(r)] }));
    wallRun(K, r, -r, r, r, Object.assign({}, W, { bastions: [bs(r * 1.3)] }));
    mark(K, 'walls');
    for (const [x, z] of [[-r, r], [r, r], [-r, -r]]) { block(K, x, 0, z, 5.2, 5.2, h + 1.0, { kind: 'mud', bat: 0.14, ys: [0.8, 3.6] }); crown(K, x, h + 1.0, z, 5.2 - 0.28, 5.2 - 0.28, { kind: 'mud', ph: 0.4, merlon: { w: 1.2, h: 0.7, step: 2.6 } }); }
    block(K, 0, 0, r + 0.4, 8.6, 5.4, h + 1.6, { glow: 0.9, kind: 'mud', bat: 0.12, ys: [0.8, 3.8] });
    gateArch(K, 0, r + 0.4 + 2.7, 0.12, { w: 3.0, h: 4.2, kind: 'round', door: '#7A4A32', frame: '#E2D2B0', border: 0.36, glow: 0.85 });
    crown(K, 0, h + 1.6, r + 0.4, 8.6 - 0.38, 5.4 - 0.38, { kind: 'mud', ph: 0.4, merlon: { w: 1.1, h: 0.7, step: 2.3 } });
    for (const s of [-1, 1]) torch(K, s * 2.3, 4.0, r + 3.2);
    mark(K, 'gates');
    // 烽燧（西北角内侧）
    { const M = K.M; block(K, -13.5, 0, -13.5, 4.2, 4.2, 9.5, { kind: 'mud', bat: 0.12, ys: [0.8, 3.2, 6.4] }); M.color = H('#B89A72'); M.box([-13.5, 9.5 + 0.7, -13.5], [1.8, 1.4, 1.8], null, 'tfkrl'); fire(K, -13.5, 9.5 + 1.4, -13.5, { s: 0.45, pit: false }); }
    // 水渠 + 水池 + 果园
    water(K, [[-18.6, 6.6], [18.6, 6.6], [18.6, 7.6], [-18.6, 7.6]], 0.07, '#5E9C9C');
    water(K, [[2.5, -1.5], [7.5, -1.5], [7.5, 3.0], [2.5, 3.0]], 0.08, '#5E9C9C');
    patch(K, [[-18.6, 8.0], [18.6, 8.0], [18.6, 11.0], [-18.6, 11.0]], '#93A862', 0.045);
    for (let i = 0; i < 6; i++) tree(K, -15 + i * 6 + (v ? 1.5 : 0), 9.4, 0.55, 'poplar');
    mark(K, 'water');
    if (big || cap) {
      stupaLP(K, -2, -8.5, cap ? 3.8 : 3.2, { color: '#E8DCC2', stone: '#CDB490' });
      for (const [cx, cz, w, d, rot] of [[-2, -15, 12, 2.6, 0], [-9.8, -8.5, 10, 2.6, 90], [5.8, -8.5, 10, 2.6, -90]]) flatHouse(K, cx, cz, w, d, 3.0, { yaw: rot, col: '#D6BC92', win: 2, parapet: 0.35 });
    }
    const houses = big || cap
      ? [[-12, 13.5, 5.0, 4.0], [12, 13.5, 5.0, 4.0], [-13.5, 1.5, 4.8, 4.6], [13.5, 0, 4.6, 4.4], [12.5, -9.5, 4.6, 4.6], [-6, 13.6, 4.0, 3.6]]
      : [[-12, 13.5, 5.0, 4.0], [12, 13.5, 5.0, 4.0], [-13, 1.5, 5.2, 4.8], [13.5, 0, 4.6, 4.4], [-5.5, -8, 5.6, 4.6], [4.5, -10, 5.2, 4.4], [12.5, -9.5, 4.6, 4.6], [-4.5, 1.5, 4.6, 4.0]];
    houses.forEach(([x, z, w, d], i) => flatHouse(K, x, z, w, d, 3.0 + ((i + v) % 3) * 0.6, { col: MUDS[(i + v + 3) % 5], beams: (i + v) % 2 === 0, storey: (i + v) % 4 === 1 ? 0.5 : 0, parapet: 0.4 }));
    mark(K, 'houses');
    for (const [x, z] of [[9.5, -1.5], [10, 3], [-9.5, -3]]) tree(K, x, z, 0.5, 'oak');
  };

  // -------------------------------------------------------------- 南海 --
  // 林邑 / 扶南：方形木栅、纵横水渠、鞍形草顶的高脚屋、红砖台基上的多重檐神殿、椰树；都城：更大的砖塔殿 + 两座配殿
  B.seasia = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant, r = 18.4;
    basePad(K, 'seasia');
    yardGlow(K, 'seasia', 0, 2, 8);
    water(K, [[-23, 9.2], [23, 9.2], [23, 11.4], [-23, 11.4]], 0.06, '#5E9C8C');
    water(K, [[-15.2, -18], [-13.0, -18], [-13.0, 9.2], [-15.2, 9.2]], 0.06, '#5E9C8C');
    road(K, 0, 23, 0, -2, 3.0, '#C8B084', 0.08);
    glowPool(K, 0, 3, 4.5, '#C8B084', { y: 0.09 });
    mark(K, 'pad');
    const pts = [[-r, r], [-r, -r], [r, -r], [r, r]];
    palisade(K, pts, 3.0, { step: 1.5, r: 0.46, skip: (x, z) => z > r - 1 && Math.abs(x) < 2.6 || (x > r - 6 && z < -r + 6), color: '#8A6A44', rail: true });
    woodGate(K, 0, r, 0, 4.2, 4.4, { thatch: '#B89858' });
    mark(K, 'walls');
    // 神殿：红砖二级台基 + 殿身 + 三重檐（红瓦）
    {
      const M = K.M, tz = -7.5, s = cap ? 1.25 : big ? 1.1 : 1;
      block(K, 0, 0, tz, 13 * s, 11 * s, 1.4, { kind: 'redbrick', ys: [0.7], plinth: 0, topColor: '#C88A64' });
      block(K, 0, 1.4, tz, 10 * s, 8.4 * s, 1.2, { kind: 'redbrick', ys: [0.6], plinth: 0, topColor: '#D49A70' });
      M.color = H('#B86C4E'); for (let i = 0; i < 4; i++) M.box([0, (i + 0.5) * 0.65, tz + 5.5 * s + 1.5 - i * 0.6], [3.2, 0.65, 0.6], H('#C8805E'), 'tfrl');
      M.color = H('#C67858'); M.box([0, 2.6 + 1.8, tz], [7 * s, 3.6, 5.6 * s], null, 'fkrl');
      M.ink = 0; M.nightEmission = 0.6; M.quad([-0.8, 2.6, tz + 2.8 * s + 0.03], [0.8, 2.6, tz + 2.8 * s + 0.03], [0.8, 5.0, tz + 2.8 * s + 0.03], [-0.8, 5.0, tz + 2.8 * s + 0.03], [0, 0, 1], H('#8A5A38')); M.nightEmission = 0; M.ink = 1;
      let y = 6.2;
      [[9.4, 7.6, 1.6], [7.0, 5.6, 1.5], [4.6, 3.8, 1.6]].forEach(([w, d, rr], i) => {
        tileRoof(K, 0, tz, { w: w * s, d: d * s, y, over: 0.7, rise: rr, pal: 'terracotta', hip: true, curl: true });
        y += rr * 0.72;
        if (i < 2) { M.color = H('#C67858'); M.box([0, y + 0.35, tz], [w * s * 0.62, 0.7, d * s * 0.62], null, 'fkrl'); y += 0.7; }
      });
      M.color = PAL.gold; M.cone([0, y + 0.5, tz], 0.35, 1.8, 5);
      lamp(K, [0, 3.4, tz + 2.8 * s + 0.8], { color: '#FFB060', range: 9, intensity: 1.1, haloSize: 0.8 });
      if (cap) for (const sx of [-1, 1]) { block(K, sx * 11, 0, tz + 2, 4, 4, 5.5, { kind: 'redbrick', ys: [0.7, 2.6], bat: 0.08 }); tileRoof(K, sx * 11, tz + 2, { w: 4, d: 4, y: 5.5, over: 0.5, rise: 2.4, pal: 'terracotta', hip: true }); }
    }
    mark(K, 'temple');
    const hs = cap ? [[-9, 5, 6, 4.4], [8, 4.5, 6, 4.4], [-6, 15, 5, 4.0], [7, 15, 5, 4.0], [12.5, -3, 4.8, 4.2]]
      : [[-9, 5, 6, 4.4], [8.5, 4.5, 6, 4.4], [-6.5, 15, 5, 4.0], [7, 15, 5, 4.0], [12.5, -4, 4.8, 4.2], [-9.5, -14, 4.8, 4.0]];
    if (big && !cap) hs.push([12.5, -12, 4.4, 3.8], [-6, -1.5, 4.4, 3.8]);
    hs.forEach(([x, z, w, d], i) => stiltHouse(K, x + (v ? 0.6 : 0), z, w * 0.82, d * 0.9, { postH: 2.4, wallH: 2.0, yaw: (i + v) % 2 ? 90 + ((i * 29) % 16 - 8) : (i * 31) % 16 - 8, thatch: (i + v) % 2 ? '#B8975A' : '#A8884E', wall: (i + v) % 3 ? '#9A7850' : '#8A6A48', sweep: 1.0, roofH: d * 0.72, lamp: i < 3 }));
    mark(K, 'houses');
    for (const [x, z] of v ? [[-17, 13.5], [-3.5, 7], [16, 7], [16, -15.5], [-17, -6]] : [[17, 13.5], [3.5, 7.5], [-16.5, 6.5], [-11.5, -16], [16.5, -1]]) palm(K, x, z, 0.75, 0, x * 11 + z * 7);
  };
  // ------------------------------------------------------- 夷洲 / 南中 --
  // 竹木栅栏围成的高脚屋村落（夷洲：竹栅、鞍形顶；南中：木栅、铜鼓祭台、芭蕉 / 棕榈）；都城为头人的大屋
  B.village = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant, nan = o.culture === 'nanman';
    const edge = basePad(K, 'village:' + (nan ? 'nanman' : 'yi'));
    yardGlow(K, 'village:' + (nan ? 'nanman' : 'yi'), 0, 2, 8);
    road(K, 0, edge, 0, 2, 2.8, '#C4AE80');
    glowPool(K, 0, 1.5, 5, '#C4AE80');
    mark(K, 'pad');
    const R = 16.8, pts = ringPts(18, R, R * 0.96, 0.17);
    palisade(K, pts, nan ? 2.8 : 2.4, { step: nan ? 1.5 : 1.25, r: nan ? 0.48 : 0.36, skip: (x, z) => southGap(0.2)(x, z) || (x > 10 && z < -10), bamboo: !nan, color: nan ? '#7E5E40' : '#B4A866' });
    woodGate(K, 0, R * 0.96, 0, 3.8, 3.8, { thatch: nan ? '#A8884E' : '#B8975A' });
    mark(K, 'walls');
    const M = K.M;
    if (nan) {
      // 铜鼓祭台：石台 + 铜鼓（青铜绿、鼓面金色星纹）+ 两侧火塘
      M.color = H('#9A9284'); M.box([0, 0.4, -2], [5.4, 0.8, 5.4], H('#AAA294'), 'tfkrl');
      M.color = H('#6E8A62'); M.push().translate(0, 0.8, -2);
      M.lathe([[1.5, 0], [1.25, 0.5], [1.3, 1.1], [1.7, 1.6], [1.75, 1.75]], 10, false, false, true, [H('#5E7A56'), H('#6E8A62'), H('#7E9A6E'), H('#8AA478'), H('#C8A848')]);
      M.pop();
      for (const s of [-1, 1]) fire(K, s * 4.2, 0, -2, { s: 0.55 });
    } else {
      fire(K, 0, 0, -1.5, { s: 0.65 });
      for (const s of [-1, 1]) { M.color = H('#7A5A3E'); M.segment([s * 3, 0, -4], [s * 3, 5.5, -4], 0.22, 0.16, 5, false, true); M.color = H('#C8B490'); M.box([s * 3, 5.2, -4], [0.9, 0.6, 0.5], null, 'tfkrl'); }
    }
    mark(K, 'center');
    const big1 = cap ? [0, -10, 9.5, 6.4] : big ? [0, -10.5, 7.6, 5.2] : null;
    if (big1) stiltHouse(K, big1[0], big1[1], big1[2], big1[3], { postH: 2.4, wallH: 2.3, thatch: nan ? '#A08048' : '#B8975A', wall: nan ? '#86684A' : '#9E8458', sweep: nan ? 0.6 : 1.5, roofH: big1[3] * 0.8, over: 0.9 });
    const hs = [[-9.5, 4, 5, 4.0], [9, 4.5, 5, 4.0], [-6, 10.5, 4.6, 3.6], [6.5, 10.5, 4.6, 3.6], [-11, -5.5, 4.6, 3.8], [10.5, -5, 4.6, 3.8]];
    if (!cap && !big) hs.push([-3.5, -10.5, 4.6, 3.6], [4.5, -10, 4.4, 3.6]);
    if (big && !cap) hs.push([-8.5, -11.5, 4.4, 3.6]);
    hs.forEach(([x, z, w, d], i) => stiltHouse(K, x + (v ? 0.5 : 0), z, w * 0.85, d * 0.9, { postH: 2.0, wallH: 1.8, yaw: ((i + v) % 2 ? 90 : 0) + (i * 37 + v * 13) % 24 - 12, thatch: (i + v) % 2 ? '#B8975A' : '#A8884E', wall: nan ? '#86684A' : '#9E8458', sweep: nan ? 0.5 : 1.1, roofH: d * 0.74, lamp: i < 2 }));
    mark(K, 'houses');
    const trees = v ? [[-14, 10], [14, 9.5], [-14.5, -9], [0, 14.5]] : [[14, 10.5], [-14, 9], [14.5, -9], [-2, 14.5]];
    trees.forEach(([x, z], i) => (i % 2 || !nan ? palm(K, x, z, 0.62, 0, x * 7 + z) : tree(K, x, z, 0.6, 'fig')));
  };

  // ------------------------------------------------------- 凯尔特 / 日耳曼 --
  // 山丘堡垒：圆丘、山脚外圈土垒、山顶内圈土垒 + 木栅、木门楼；凯尔特 = 圆形茅屋（都城大圆屋），日耳曼 = 长屋（都城带交叉兽头的大厅）
  B.hillfort = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant, ger = o.culture === 'german';
    const edge = basePad(K, 'hillfort:' + (ger ? 'german' : 'celt'));
    const hy = mound(K, 0, -1, 18.2, 17.4, 2.6, { flat: 0.84, top: ger ? '#9CA868' : '#A0AE6C', side: '#86965A' });
    bankRing(K, 20.6, 20.0, 2.2, 1.1, { n: 24, gap: 0.2, color: '#86985A' });
    bankRing(K, 14.4, 13.8, 2.2, 1.0, { n: 22, gap: 0.22, y0: hy - 0.1, color: '#8E9E5E' });
    road(K, 0, edge, 0, 15, 2.8, '#B8A47A');
    patch(K, ngon(10, 12.8, 12.2, 0.3).map(p => [p[0], p[1] - 0.6]), ger ? '#A8A070' : '#ACA472', hy + 0.03);
    glowPool(K, 0, 1.5, 7.5, ger ? '#A8A070' : '#ACA472', { y: hy + 0.06, n: 10 });
    mark(K, 'pad');
    palisade(K, ringPts(22, 14.4, 13.8, 0), 2.4, { y0: hy + 0.9, step: 1.5, r: 0.48, skip: (x, z) => southGap(0.24)(x, z), color: ger ? '#7A5A3E' : '#8A6844' });
    woodGate(K, 0, 13.8, 0, 3.6, 3.8, { y0: hy + 0.1, thatch: '#A8884E' });
    mark(K, 'walls');
    const y0 = hy;
    if (ger) {
      if (cap) longhouse(K, 0, -5.5, 15, 6.2, { y0, horns: true, h: 2.8, roofH: 4.6, wall: '#7A5A3E', thatch: '#A08452' });
      const lh = cap ? [[-7.5, 4.5, 10, 4.4, 20], [7.5, 4, 10, 4.4, -15]] : [[0, -6.5, big ? 13 : 12, 5.0, 0], [-7.5, 3.5, 10, 4.4, 25], [7.5, 3.5, 10, 4.4, -20]];
      if (big && !cap) lh.push([0, 9, 9, 4.0, 0]);
      lh.forEach(([x, z, len, w, yaw], i) => longhouse(K, x, z, len, w, { y0, yaw: yaw + (v ? 8 : 0), horns: i === 0 && !cap && big, wall: i % 2 ? '#8A6A4A' : '#7E5E42' }));
    } else {
      if (cap) roundHut(K, 0, -4.5, 5.6, 2.6, { y0, wall: '#C8B08A', thatch: '#B49A5E', sides: 10, roofH: 6.4 });
      const rh = cap ? [[-8, 4.5, 2.8], [8, 4.5, 2.8], [-8.5, -8, 2.6], [8.5, -7.5, 2.6]] : [[0, -5, big ? 4.0 : 3.6], [-7.5, 3.5, 3.0], [7.5, 3.5, 3.0], [-8, -6.5, 2.7], [8, -6.5, 2.7], [0, 6.5, 2.6]];
      if (big && !cap) rh.push([-4, -10.5, 2.4], [5, -10.5, 2.4]);
      rh.forEach(([x, z, rr], i) => roundHut(K, x + (v ? 0.5 : 0), z, rr, 1.9, { y0, yaw: (i * 41) % 60 - 30, wall: i % 2 ? '#C4AC86' : '#B8A07A', thatch: i % 3 ? '#B49A5E' : '#A88C52' }));
    }
    mark(K, 'houses');
    fire(K, v ? 3.5 : -3.5, y0, 0.5, { s: 0.55 });
    const M = K.M;
    // 草垛
    for (const [x, z] of v ? [[10.5, -2], [-11, 0.5]] : [[-10.5, -1.5], [10.5, 0]]) { M.color = H('#C8A858'); M.push().translate(x, y0, z); M.lathe([[1.1, 0], [1.15, 0.9], [0.7, 1.8], [0, 2.2]], 6, false, false, false, [H('#B49448'), H('#C8A858'), H('#D4B464'), H('#DCC070')]); M.pop(); }
    for (const [x, z] of v ? [[-16.5, 12.5], [17, -6.5]] : [[16, 12.5], [-17.5, -7]]) tree(K, x, z, 0.7, 'oak');
  };

  // ------------------------------------------------------- 萨尔马提亚 --
  // 车营：篷车、尖顶毡帐（条纹）、龙旗、马栏、篝火
  B.camp = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant;
    const edge = basePad(K, 'camp');
    yardGlow(K, 'camp', 0, 3, 8);
    road(K, 0, edge, 0, 3, 3.0, '#C8B486');
    glowPool(K, 0, 4, 5, '#C8B486');
    mark(K, 'pad');
    const nW = big ? 9 : 7;
    for (let i = 0; i < nW; i++) {
      const a = Math.PI / 2 + 0.6 + i * (Math.PI * 2 - 1.2) / (nW - 1), x = Math.cos(a) * 16.8, z = Math.sin(a) * 16.4;
      if (x > 10 && z < -10) continue;
      wagon(K, x, z, -a / D2R + 90, { cover: (i + v) % 2 ? '#D8C8A0' : '#C09A68', len: 4.6 });
    }
    mark(K, 'walls');
    const tents = [[0, -6, 4.2, 6.2], [-8.5, 2.5, 3.0, 4.6], [8.5, 2.5, 3.0, 4.6], [-9, -8.5, 2.8, 4.2], [7.5, -8.5, 2.8, 4.2], [-3.5, 9, 2.6, 4.0], [4.5, 9.5, 2.6, 4.0]];
    if (big) tents.push([-12.5, -2, 2.6, 4], [12.5, -1.5, 2.6, 4]);
    tents.forEach(([x, z, rr, hh], i) => coneTent(K, x + (v ? 0.6 : 0), z, rr, hh, { col: i === 0 ? '#F2E8D2' : (i + v) % 2 ? '#EDE3CC' : '#E6D8BC', stripe: i === 0 ? '#A8402E' : ['#4E6E8A', '#C8873A', '#8A4A3A'][(i + v) % 3], yaw: (i * 37) % 50 }));
    standard(K, 3.2, -1.5, 7, '#C84A2E', 'flag');
    tug(K, -3.2, -1.5, 6.4, '#E8E0CC');
    fire(K, 0, 0, 1.5, { s: 0.7 });
    corral(K, v ? 12 : -12, 10.5, 3.4, { n: 9, gate: 6 });
    horse(K, (v ? 12 : -12) + 0.6, 10.2, 120, '#5E4430');
    horse(K, (v ? 12 : -12) - 0.8, 11.2, 20, '#A88A68');
  };
  // 博斯普鲁斯希腊城（潘提卡彭等）：白石城墙、方塔、卫城丘（神庙）、红瓦民居、柏与橄榄
  B.greek = function (K, o) {
    const r = U, h = 6.4, cap = o.cap, v = o.variant;
    basePad(K, 'greek');
    yardGlow(K, 'greek', 2, 8, 8);
    const hy = mound(K, -2, -9.5, 12.5, 8.6, 4.4, { flat: 0.6, top: '#BDB68C', side: '#9C9C6C' });
    road(K, 0, 23, 0, -1, 3.6, '#DCD2B6');
    glowPool(K, 0, 12, 4, '#DCD2B6');
    mark(K, 'pad');
    const W = { h, t: 3.0, bat: 0.05, kind: 'whitestone', plinth: 0.8, courses: [2.2, 4.0, 5.4], panel: [2.4, 4.4], merlon: { w: 1.1, h: 0.85, step: 2.5 } };
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 4.8, r + 4.8]] }));
    wallRun(K, -r, -r, r, -r, W);
    wallRun(K, -r, -r, -r, r, W);
    wallRun(K, r, -r, r, r, W);
    mark(K, 'walls');
    for (const [x, z] of [[-r, r], [r, r], [-r, -r], [-r, 0], [r, 2]]) { block(K, x, 0, z, 5, 5, h + 1.8, { kind: 'whitestone', ys: [0.8, 3.6, 6.0] }); crown(K, x, h + 1.8, z, 5, 5, { kind: 'whitestone', ph: 0.4, merlon: { w: 1.0, h: 0.8, step: 2.2 } }); }
    block(K, 0, 0, r + 0.4, 8.4, 5, h + 1.4, { glow: 0.9, kind: 'whitestone', ys: [0.8, 3.6] });
    gateArch(K, 0, r + 2.9, 0, { w: 3.0, h: 4.4, kind: 'round', door: '#6E4A34', frame: '#F2ECDE', border: 0.38, glow: 0.85 });
    crown(K, 0, h + 1.4, r + 0.4, 8.4, 5, { kind: 'whitestone', ph: 0.4, skip: 'k', merlon: { w: 1.0, h: 0.8, step: 2.0 } });
    for (const s of [-1, 1]) torch(K, s * 2.4, 4.0, r + 3.15);
    mark(K, 'towers');
    temple(K, -2, -10, cap ? 7.6 : 6.6, cap ? 10.5 : 9.0, { y0: hy, cols: 6, colH: cap ? 5.2 : 4.6, podium: 1.0, stone: '#F2ECDE', roof: 'terracotta', frieze: '#5E86A8' });
    const M = K.M; M.color = H('#D8D0BA'); for (let i = 0; i < 5; i++) M.box([-2, (i + 0.5) * 0.88, -9.5 + 8.6 * 0.6 + 1.2 + (4 - i) * 0.9 - 2.4], [3.2, 0.88, 0.9], H('#E6DFCC'), 'tfrl');
    mark(K, 'temple');
    const houses = [[-11.5, 10.5, 5.6, 4.4], [11.5, 10.5, 5.6, 4.4], [-13, 2.0, 4.8, 4.6], [12.5, 2.5, 4.8, 4.6], [5.5, 14, 4.4, 3.4], [-5.5, 14, 4.4, 3.4], [11.5, -7.5, 4.8, 4.6], [5.5, 2.5, 4.6, 4.2]];
    if (cap) houses.push([-6, 3.2, 4.6, 4.0]);
    houses.forEach(([x, z, w, d], i) => medHouse(K, x + (v ? 0.4 : 0), z, w, d, 3.4 + ((i + v) % 3) * 0.6, { hip: (i + v) % 2 === 0, shutter: (i + v) % 3 ? '#5E86A8' : '#7E6A4E' }));
    mark(K, 'houses');
    for (const [x, z, k] of [[-14.5, -14.5, 'cypress'], [7.5, -15, 'olive'], [15.5, -2.5, 'olive'], [-15.8, 6.5, 'cypress']]) tree(K, x, z, 0.65, k);
  };

  // -------------------------------------------------------------- 库施 --
  // 麦罗埃 / 纳帕塔：尼罗河岸（西侧水面）、红褐土坯城墙（圆顶垛）、塔门神庙（塔门 + 列柱厅 + 圣殿）、陡峭的王陵金字塔群、平顶房、椰枣树
  B.kush = function (K, o) {
    const r = U, h = 6.2, cap = o.cap, big = o.tier >= 1, v = o.variant;
    basePad(K, 'kush');
    yardGlow(K, 'kush', 4, 8, 8);
    water(K, [[-23.6, -21], [-21.4, -21], [-21.4, 21], [-23.6, 21]], 0.08, '#4E8E8E');
    patch(K, [[-21.4, -21], [-19.6, -21], [-19.6, 21], [-21.4, 21]], '#8FA65C', 0.07);
    road(K, 0, 23, 0, 0, 3.6, '#DCC098');
    glowPool(K, 0, 11, 4.2, '#DCC098');
    mark(K, 'pad');
    const W = { h, t: 3.4, bat: 0.14, kind: 'mudred', plinth: 0.7, courses: [2.2, 3.8], panel: [2.8, 5.4], merlon: { w: 1.4, h: 1.0, step: 3.3, round: true }, par: { h: 0.6, t: 0.7 } };
    const bs = u => ({ u, w: 3.8, d: 1.6 });
    wallRun(K, -r, r, r, r, Object.assign({}, W, { skip: [[r - 4.8, r + 4.8]], bastions: [bs(r * 0.45), bs(r * 1.55)] }));
    wallRun(K, -r, -r, r, -r, Object.assign({}, W, { bastions: [bs(r * 0.6)] }));
    wallRun(K, -r, -r, -r, r, Object.assign({}, W, { bastions: [bs(r * 0.6), bs(r * 1.4)] }));
    wallRun(K, r, -r, r, r, Object.assign({}, W, { bastions: [bs(r * 1.3)] }));
    for (const [x, z] of [[-r, r], [r, r], [-r, -r]]) block(K, x, 0, z, 4.8, 4.8, h + 0.8, { kind: 'mudred', bat: 0.12, ys: [0.7, 3.4] });
    block(K, 0, 0, r + 0.3, 8.6, 5, h + 1.4, { glow: 0.9, kind: 'mudred', bat: 0.12, ys: [0.7, 3.6] });
    gateArch(K, 0, r + 2.8, 0.12, { w: 3.0, h: 4.2, kind: 'square', door: '#6E4430', frame: '#D8B88E', border: 0.4, glow: 0.85 });
    for (const s of [-1, 1]) torch(K, s * 2.4, 4.0, r + 3.2);
    mark(K, 'walls');
    // 塔门神庙（阿蒙神庙）：塔门、列柱厅（柱 + 平顶）、圣殿
    {
      const M = K.M, tz = cap ? -1.5 : -2.5, s = cap ? 1.15 : 1;
      pylon(K, 0, tz, 11 * s, 7.2 * s, { col: '#D8B888' });
      M.color = H('#E2C89C'); for (const sx of [-1, 1]) for (const sz of [0, 1]) M.cylinder([sx * 2.6 * s, 0, tz - 2.8 - sz * 2.4 * s], 0.45, 0.4, 4.2 * s, 6, false, false, true);
      block(K, 0, 0, tz - 8.4 * s, 9 * s, 5.6 * s, 4.6 * s, { kind: 'buff', ys: [0.8, 2.6], plinth: 0.8 });
      block(K, 0, 0, tz - 13.2 * s, 5.6 * s, 3.8 * s, 5.4 * s, { kind: 'buff', ys: [0.8, 2.6] });
      // 公羊斯芬克斯两列（小）
      M.color = H('#CDB48A'); for (const sx of [-1, 1]) for (let i = 0; i < 3; i++) { M.box([sx * 2.6, 0.5, tz + 3.4 + i * 2.6], [0.8, 1.0, 1.8], H('#D8C098'), 'tfkrl'); M.box([sx * 2.6, 1.2, tz + 4.0 + i * 2.6], [0.6, 0.6, 0.6], null, 'tfkrl'); }
    }
    mark(K, 'temple');
    // 金字塔群（西北）
    const pyr = cap ? [[-12, -12.5, 6.4, 12.5], [-5.5, -15.5, 5.4, 10.5], [-15.5, -4.5, 5.0, 9.5], [-12, 1.5, 4.4, 8.2]] : big ? [[-12, -12.5, 6.0, 11.5], [-5.8, -15.5, 5.0, 9.6], [-15.5, -4.8, 4.8, 9.0]] : [[-12, -12, 5.8, 11], [-5.5, -15.5, 4.8, 9.0]];
    pyr.forEach(([x, z, b, hh]) => pyramid(K, x, z, b, hh, { col: '#B98A5E' }));
    mark(K, 'pyramids');
    const houses = [[-11.5, 11, 5.4, 4.4], [11.5, 11, 5.4, 4.4], [13.2, 2.5, 4.6, 5.0], [12.5, -8.5, 4.6, 4.4], [6.5, 14.2, 4.4, 3.4]];
    if (big || cap) houses.push([-6.5, 14.2, 4.4, 3.4], [-13.5, 5.5, 4.4, 4.0]);
    houses.forEach(([x, z, w, d], i) => flatHouse(K, x, z, w, d, 3.0 + ((i + v) % 3) * 0.6, { col: ['#C89670', '#D4AA80', '#DCC0A0', '#C08A64'][(i + v) % 4], beams: true, parapet: 0.35 }));
    garden(K, v ? 7 : 6.5, v ? 6 : 6.5, 3.4, 3.4, { bush: true });
    mark(K, 'houses');
    for (const [x, z] of v ? [[-19.5, 10], [-19.8, -14], [7, 6], [16, -15]] : [[-19.6, 8], [-19.8, -16], [-7, 6.5], [16.5, -14.5]]) palm(K, x, z, 0.72, 0, x * 5 + z * 9);
  };

  // ------------------------------------------------------------ 阿克苏姆 --
  // 花岗岩石碑林（后排）、阶梯台基上的石砌宫殿（凹凸墙面、“猴头”梁头）、圆形茅屋、干砌石矮墙；都城：更高的石碑与大宫殿
  B.aksum = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant;
    const edge = basePad(K, 'aksum');
    yardGlow(K, 'aksum', -3, 6, 8);
    road(K, 0, edge, 0, 2, 3.2, '#C8B48C');
    glowPool(K, 0, 5, 5, '#C8B48C');
    mark(K, 'pad');
    // 干砌石矮墙（城的外圈，开南门）
    const R = 19;
    const W = { h: 1.8, t: 1.5, kind: 'drystone', plinth: 0, courses: [0.9], panel: [1.2, 2.4], par: null, center: [0, 0], inner: true };
    const pts = ringPts(16, R, R * 0.96, Math.PI / 2 + Math.PI / 16);
    for (let i = 0; i < 15; i++) if (!(pts[i][0] > 9 && pts[i][1] < -9 && pts[i + 1][0] > 9 && pts[i + 1][1] < -9)) wallRun(K, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], W);
    mark(K, 'walls');
    // 石碑林
    const st = cap ? [[-9, -14.5, 20], [-3.5, -15.5, 15], [2, -15.8, 12], [-13.5, -11, 10], [7, -15, 9]] : big ? [[-6.5, -15, 16], [-1.5, -15.8, 12], [-11, -12.5, 9.5], [3.5, -15.5, 8]] : [[-5.5, -15, 14], [-0.5, -15.8, 10], [-10, -12.5, 8]];
    st.forEach(([x, z, hh], i) => stele(K, x, z, hh, { lean: i === 1 ? -3 : 0 }));
    mark(K, 'stelae');
    // 宫殿：阶梯台基 + 凹凸的石墙（木梁带与“猴头”梁头）+ 平顶
    {
      const M = K.M, px = cap ? 4.5 : 5, pz = cap ? -3 : -4, s = cap ? 1.25 : big ? 1.1 : 1;
      block(K, px, 0, pz, 15 * s, 13 * s, 1.0, { kind: 'drystone', ys: [], plinth: 0, topColor: '#C2B694' });
      block(K, px, 1.0, pz, 12 * s, 10 * s, 1.0, { kind: 'drystone', ys: [], plinth: 0, topColor: '#CABE9C' });
      M.color = H('#A89C86'); for (let i = 0; i < 3; i++) M.box([px, (i + 0.5) * 0.7, pz + 6.5 * s + 1.3 - i * 0.65], [3.4, 0.7, 0.65], H('#B8AE98'), 'tfrl');
      const bw = 8 * s, bd = 7 * s, bh = 7.5 * s, y0 = 2.0;
      block(K, px, y0, pz, bw, bd, bh, { kind: 'drystone', ys: [bh * 0.33, bh * 0.36, bh * 0.66, bh * 0.69], plinth: 0, topColor: '#B4A88E' });
      // 木梁带（深色）+ 猴头（梁头）
      M.color = H('#5E4630');
      for (const yy of [bh * 0.345, bh * 0.675]) {
        M.box([px, y0 + yy, pz], [bw + 0.12, 0.32, bd + 0.12], null, 'fkrl');
        for (let i = 0; i < 5; i++) { const xx = px - bw / 2 + (i + 0.5) * bw / 5; M.box([xx, y0 + yy, pz + bd / 2 + 0.2], [0.34, 0.34, 0.3], null, 'tfrl'); }
      }
      for (let i = 0; i < 3; i++) windowGlow(K, px - bw * 0.3 + i * bw * 0.3, y0 + bh * 0.52, pz + bd / 2 + 0.06, 0.7, 0.9, { color: '#C08A58' });
      crown(K, px, y0 + bh, pz, bw, bd, { kind: 'drystone', ph: 0.6, pt: 0.5, merlon: null });
      // 角塔
      for (const sx of [-1, 1]) block(K, px + sx * (bw / 2 + 0.8), y0, pz + bd / 2 + 0.6, 2.6, 2.6, bh * 0.9, { kind: 'drystone', ys: [bh * 0.33, bh * 0.66], plinth: 0 });
      lamp(K, [px, y0 + 1.6, pz + bd / 2 + 1.2], { color: '#FFB060', range: 9, intensity: 1.1, haloSize: 0.8 });
    }
    mark(K, 'palace');
    const huts = cap ? [[-12, 4.5, 3.0], [-6, 11.5, 2.8], [6, 11.5, 2.8], [12.5, 9, 2.8]] : [[-11.5, 3.5, 3.0], [-6.5, 11, 2.8], [5, 11.5, 2.8], [12, 8.5, 2.8], [-13, -4.5, 2.6]];
    if (big && !cap) huts.push([-2, 5, 2.6]);
    huts.forEach(([x, z, rr], i) => roundHut(K, x + (v ? 0.5 : 0), z, rr, 2.0, { yaw: (i * 41) % 50 - 25, wall: i % 2 ? '#B49C7A' : '#A89070', thatch: i % 2 ? '#A88C52' : '#B49A5E' }));
    mark(K, 'houses');
    for (const [x, z, k] of v ? [[15.5, -1, 'olive'], [-15.5, 12, 'oak'], [11, 15, 'olive']] : [[-15.5, -1, 'olive'], [15.5, 13, 'oak'], [-10, 15, 'olive']]) tree(K, x, z, 0.65, k);
  };

  // -------------------------------------------------------------- 毛里 --
  // 中阿特拉斯山前的山头聚落：土石台地、干砌石环墙、方形谷仓堡（agadir，夜里小窗亮）、平顶石屋、台下的游牧黑帐、雪松
  B.moor = function (K, o) {
    const cap = o.cap, big = o.tier >= 1, v = o.variant;
    const edge = basePad(K, 'moor');
    const hy = mound(K, -0.5, -2, 17.8, 16.4, 2.6, { flat: 0.86, top: '#B8A47C', side: '#9C8A64', sides: 14 });
    road(K, 0, edge, 0, 13.5, 2.8, '#C4AE84');
    glowPool(K, 0, 4, 7, '#B8A47C', { y: hy + 0.06, n: 10 });
    mark(K, 'pad');
    // 环墙（台地边，开南门）
    const pts = ringPts(14, 14.6, 13.6, Math.PI / 2 + Math.PI / 14).map(p => [p[0] - 0.5, p[1] - 2]);
    const W = { h: 2.8, t: 1.6, bat: 0.08, kind: 'drystone', plinth: 0, courses: [1.0, 1.9], panel: [1.4, 2.8], par: { h: 0.4, t: 1.0 }, merlon: null, center: [-0.5, -2], y0: hy - 0.2 };
    for (let i = 0; i < 13; i++) wallRun(K, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], W);
    for (const s of [-1, 1]) { const p = s < 0 ? pts[0] : pts[13]; block(K, p[0], hy - 0.2, p[1], 2.4, 2.4, 4.0, { kind: 'drystone', ys: [1.4, 2.6], plinth: 0 }); torch(K, p[0], hy + 3.6, p[1] + 1.4); }
    mark(K, 'walls');
    // 谷仓堡（agadir）
    {
      const M = K.M, gx = v ? 4 : -3, gz = -8, s = cap ? 1.2 : big ? 1.1 : 1, gw = 8.5 * s, gh = 8.5 * s;
      block(K, gx, hy, gz, gw, gw, gh, { kind: 'drystone', ys: [1.2, gh * 0.36, gh * 0.7], bat: 0.08, plinth: 0 });
      crown(K, gx, hy + gh, gz, gw - gh * 0.16, gw - gh * 0.16, { kind: 'drystone', ph: 0.5, pt: 0.7, merlon: { w: 1.0, h: 0.8, step: 2.0 } });
      const ink = M.ink; M.ink = 0;
      for (let row = 0; row < 3; row++) for (let i = 0; i < 4; i++) {
        const xx = gx - gw * 0.3 + i * gw * 0.2, yy = hy + 2.0 + row * gh * 0.24, zz = gz + gw / 2 - 0.08 * (yy - hy) + 0.04;
        M.nightEmission = row === 1 && i % 2 ? 0.7 : 0;
        M.quad([xx - 0.28, yy, zz], [xx + 0.28, yy, zz], [xx + 0.28, yy + 0.5, zz], [xx - 0.28, yy + 0.5, zz], [0, 0.08, 1], row === 1 && i % 2 ? H('#B07A4A') : H('#4A3A2E'));
      }
      M.nightEmission = 0; M.ink = ink;
      M.quad([gx - 0.7, hy, gz + gw / 2 + 0.06], [gx + 0.7, hy, gz + gw / 2 + 0.06], [gx + 0.7, hy + 2.2, gz + gw / 2 - 0.12], [gx - 0.7, hy + 2.2, gz + gw / 2 - 0.12], [0, 0.08, 1], H('#5E4430'));
    }
    mark(K, 'agadir');
    const houses = [[-8, 4, 5.0, 4.4], [7.5, 3.5, 5.0, 4.4], [-9.5, -5, 4.4, 4.4], [9.5, -5.5, 4.4, 4.2], [-3, 8.5, 4.4, 3.6], [4, 9, 4.2, 3.6]];
    if (big || cap) houses.push([-11, 0, 3.8, 3.6]);
    houses.forEach(([x, z, w, d], i) => flatHouse(K, x + (v ? -0.4 : 0), z, w, d, 2.8 + ((i + v) % 3) * 0.5, { y0: hy, col: (i + v) % 2 ? '#B8A68A' : '#C4B092', roof: '#A8987C', parapet: 0.3, win: 1, beams: (i + v) % 2 === 0 }));
    mark(K, 'houses');
    for (const [x, z, yaw] of v ? [[-18.6, 8, 70], [18.6, 8.5, -75]] : [[18.6, 8.5, -70], [-18.6, 7.5, 75], [-19.2, -8, 95]]) blackTent(K, x, z, 4.8, 3.4, { yaw });
    tug(K, 1.5, -1.5, 5.2, '#3A5A8A', hy);
    for (const [x, z, s] of v ? [[-14, -14.5, 0.8], [17.5, -3, 0.7]] : [[-17.5, -3.5, 0.75], [13.5, -15, 0.8]]) conifer(K, x, z, s, 'cedar');
  };
  /** 平放的环形水面（环壕；方环，内半宽 ri、宽 w），南面中段留土桥（半宽 bw） */
  function ringBand(K, ri, w, y, col, bw) {
    const ro = ri + w, seg = pts => water(K, pts, y, col);
    seg([[-ro, -ro], [ro, -ro], [ro, -ri], [-ro, -ri]]);
    seg([[-ro, ri], [-bw, ri], [-bw, ro], [-ro, ro]]);
    seg([[bw, ri], [ro, ri], [ro, ro], [bw, ro]]);
    seg([[-ro, -ri], [-ri, -ri], [-ri, ri], [-ro, ri]]);
    seg([[ri, -ri], [ro, -ri], [ro, ri], [ri, ri]]);
  }
  /** 柱廊（stoa）：一排柱子 + 额枋 + 单坡红瓦顶，长 len，在 (x, z) 朝 yaw（柱列在 +Z 侧） */
  function colonnade(K, x, z, len, yaw, n, h) {
    at(K, x, 0, z, yaw, () => {
      const M = K.M, stone = H('#EDE6D6');
      M.color = sh(stone, 0.9); M.box([0, 0.2, 0], [len + 0.6, 0.4, 3.6], stone, 'tfkrl');
      M.color = H('#E2D6BC'); M.box([0, 0.4 + h / 2, -1.4], [len, h, 0.5], null, 'fk');
      for (let i = 0; i < n; i++) { const px = lerp(-len / 2 + 0.5, len / 2 - 0.5, i / (n - 1)); M.color = stone; M.cylinder([px, 0.4, 1.3], 0.3, 0.26, h, 6, false, false, false); }
      M.color = sh(stone, 1.02); M.box([0, 0.4 + h + 0.3, 0], [len + 0.4, 0.6, 3.4], null, 'fkrl');
      const roof = ROOF.terracotta, y = 0.4 + h + 0.6;
      M.quad([-len / 2 - 0.4, y + 1.2, -1.9], [len / 2 + 0.4, y + 1.2, -1.9], [len / 2 + 0.4, y - 0.1, 2.1], [-len / 2 - 0.4, y - 0.1, 2.1], [0, 1, 0.9], roof.tile);
      for (const s of [-1, 1]) M.triangleFacing([s * (len / 2 + 0.2), y, -1.7], [s * (len / 2 + 0.2), y + 1.15, -1.7], [s * (len / 2 + 0.2), y, 1.9], [s, 0, 0], sh(stone, 0.95));
      M.color = roof.ridge; M.box([0, y + 1.25, -1.9], [len + 0.8, 0.25, 0.3], null, 'tfk');
    });
  }

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
      washQuad(M, [-L / 2, 0, -t / 2], [-L / 2, h, -t / 2], [L / 2, h, -t / 2], [L / 2, 0, -t / 2], [0, 0, -1], pw.inner, 0.55, 0);
      if (!(o && o.noPar)) { M.color = pw.par; M.box([0, h + 0.5, t / 2 - 0.9], [L, 1.0, 0.6], null, 'tfk'); }
      M.pop();
    }
  }
  F.han = function (K, o) {
    const r = U, h = 8.2, M = K.M;
    basePad(K, 'han', true);
    fGlow(K, 'han', 0, 5, 5.5);
    farWalls(K, r, h, 4.2, 'rammed');
    for (const [cx, cz] of [[-1, 1], [1, 1], [-1, -1]]) {
      M.color = walls().rammed.b; M.box([cx * r, (h + 0.6) / 2, cz * r], [6.4, h + 0.6, 6.4], walls().rammed.walk, 'tfkrl');
      M.color = PAL.hanRedDark; M.box([cx * r, h + 1.8, cz * r], [4, 2.4, 4], null, 'fkrl');
      farRoof(K, cx * r, h + 3.0, cz * r, 6.4, 6.4, 2.2, o.cap ? ROOF.gold : ROOF.slate, 'pyramid');
    }
    M.color = walls().rammed.a; M.box([0, (h + 1.6) / 2, r + 0.6], [14, h + 1.6, 7.4], walls().rammed.walk, 'tfkrl');
    M.ink = 0; M.color = H('#4A3A2E'); M.nightEmission = 0.85; M.quad([-1.8, 0, r + 4.32], [1.8, 0, r + 4.32], [1.8, 5.4, r + 4.32], [-1.8, 5.4, r + 4.32], [0, 0, 1]); M.nightEmission = 0; M.ink = 1;
    M.color = PAL.hanRed; M.box([0, h + 1.6 + 2.0, r + 0.6], [10.6, 4.0, 5.2], null, 'fkrl');
    farRoof(K, 0, h + 5.6, r + 0.6, 13.6, 8.2, 4.0, ROOF.slate, 'hip');
    for (const s of [-1, 1]) { M.color = walls().rammed.a; M.box([s * (r + 0.4), (h + 1) / 2, 0], [6.2, h + 1, 9], walls().rammed.walk, 'tfkrl'); }
    // 主殿（与近景同比例：主殿是城的视觉中心）
    const k = o.cap || o.tier >= 1 ? 1.25 : 1.2;
    M.color = H('#D8D0BC'); M.box([0, 1.4, -5.5], [15 * k, 2.8, 10 * k], H('#E6DFCC'), 'tfkrl');
    M.color = PAL.hanRed; M.box([0, 2.8 + 2.3, -5.5], [10.4 * k, 4.6, 6 * k], null, 'fkrl');
    fWin(K, 0, 5.0, -5.5 + 3 * k, 0, 1, 6.4 * k, 2.0, '#D8A868');
    farRoof(K, 0, 7.4, -5.5, 14 * k, 9.6 * k, 4.6, o.cap ? ROOF.gold : ROOF.slate, 'hip');
    for (const [x, z, w, d] of [[-11, 9.5, 6.4, 4.4], [11, 9.5, 6.4, 4.4], [-13.2, 3.6, 5.2, 4.2], [13.2, 3.6, 5.2, 4.2]]) {
      M.color = PAL.plaster; M.box([x, 1.7, z], [w, 3.4, d], null, 'fkrl');
      fWin(K, x, 1.8, z + d / 2, 0, 1, 2.2, 1.4, '#D8A868');
      farRoof(K, x, 3.3, z, w + 1.4, d + 1.4, 1.9, houseSlate(), 'gable');
    }
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

  // ---------------------------------------------------------- 远景构件（每件几个到二十几个三角形）--
  function fBox(K, x, y, z, w, h, d, col, top) { const M = K.M; M.color = C(col); M.box([x, y + h / 2, z], [w, h, d], top ? C(top) : P.shade(C(col), 1.08), 'tfkrl'); }
  /**
   * 远景的夜窗（一块竖直的窗 / 门光，中心 (x, y, z)，朝水平方向 (nx, nz)，宽 w 高 h；白天是深色窗洞，夜里由 Mood 点亮）。
   * 每个远景模型最多 K.glowLeft 块（build 里设为 3，约 6 个三角形）：远处的城在夜里有几点暖光，不至于整片发蓝。
   */
  function fWin(K, x, y, z, nx, nz, w, h, col) {
    if (!(K.glowLeft > 0)) return;
    K.glowLeft--;
    const M = K.M, l = Math.hypot(nx, nz) || 1, ux = -nz / l, uz = nx / l, ink = M.ink;
    M.ink = 0; M.nightEmission = 0.95;
    const c = C(col || '#B88452'), ox = nx / l * 0.06, oz = nz / l * 0.06;
    M.quad([x - ux * w / 2 + ox, y - h / 2, z - uz * w / 2 + oz], [x + ux * w / 2 + ox, y - h / 2, z + uz * w / 2 + oz], [x + ux * w / 2 + ox, y + h / 2, z + uz * w / 2 + oz], [x - ux * w / 2 + ox, y + h / 2, z - uz * w / 2 + oz], [nx / l, 0, nz / l], c);
    M.nightEmission = 0; M.ink = ink;
  }
  /** 远景的院心光：贴地的 6 边扇（城内的土色，中心夜里自发光）——远处的城在夜里从上方也看得见一团暖光，白天看不出 */
  function fGlow(K, key, x, z, r, y, col) { glowPool(K, x, z, r * 1.6, col || baseInnerCol(key), { n: 6, y: y != null ? y : 0.065, e: 0.75 }); }
  /** 圆塔（6 边）：塔身 + 圆锥顶（cone 色）或平顶 */
  function fTower(K, x, z, r, h, col, cone, y0) {
    const M = K.M, c = C(col), y = y0 || 0;
    M.push().translate(x, y, z);
    M.color = c; M.lathe([[r, 0], [r * 0.94, h]], 6, false, false, !cone, [sh(c, 0.9), c], 30);
    if (cone) { M.color = C(cone); M.lathe([[r * 1.2, h], [0, h + r * 1.6]], 6, false, true, false, [sh(C(cone), 0.85), sh(C(cone), 1.1)], 30); }
    M.pop();
  }
  /** 房：墙盒 + 顶（'hip' | 'gable' | 'flat' | 'cone'（圆屋）| 'loaf'） */
  function fHouse(K, x, z, w, d, h, wall, roof, style, y0) {
    const M = K.M, y = y0 || 0;
    if (style === 'cone') {
      M.push().translate(x, y, z); M.color = C(wall); M.lathe([[w / 2, 0], [w / 2, h]], 6, false, false, false, null, 30); M.color = C(roof); M.lathe([[w * 0.62, h - 0.2], [0, h + w * 0.7]], 6, false, true, false, [sh(C(roof), 0.85), sh(C(roof), 1.1)], 30); M.pop();
      if (h > 1.2) { const a = Math.PI / 3, ap = w / 2 * Math.cos(Math.PI / 6); fWin(K, x + Math.cos(a) * ap, y + Math.min(h * 0.42, 1.1), z + Math.sin(a) * ap, Math.cos(a), Math.sin(a), Math.min(1.8, w * 0.3), Math.min(h * 0.8, 2.0)); }
      return;
    }
    M.color = C(wall); M.box([x, y + h / 2, z], [w, h, d], null, 'fkrl');
    if (h > 1.2) fWin(K, x, y + h * 0.5, z + d / 2, 0, 1, Math.min(2.4, w * 0.4), Math.min(1.8, h * 0.45));
    if (style === 'flat') { M.color = C(roof); M.box([x, y + h + 0.2, z], [w + 0.2, 0.4, d + 0.2], null, 'tfkrl'); return; }
    if (style === 'loaf') { const r = C(roof); M.quad([x - w / 2 - 0.4, y + h - 0.3, z + d / 2 + 0.6], [x + w / 2 + 0.4, y + h - 0.3, z + d / 2 + 0.6], [x + w / 2 + 0.2, y + h + d * 0.62, z], [x - w / 2 - 0.2, y + h + d * 0.62, z], [0, 1, 1], r); M.quad([x + w / 2 + 0.4, y + h - 0.3, z - d / 2 - 0.6], [x - w / 2 - 0.4, y + h - 0.3, z - d / 2 - 0.6], [x - w / 2 - 0.2, y + h + d * 0.62, z], [x + w / 2 + 0.2, y + h + d * 0.62, z], [0, 1, -1], sh(r, 0.86)); for (const s of [-1, 1]) M.triangleFacing([x + s * (w / 2 + 0.4), y + h - 0.3, z - d / 2 - 0.6], [x + s * (w / 2 + 0.2), y + h + d * 0.62, z], [x + s * (w / 2 + 0.4), y + h - 0.3, z + d / 2 + 0.6], [s, 0.6, 0], sh(r, 0.93)); return; }
    const pal = typeof roof === 'string' && ROOF[roof] ? ROOF[roof] : { tile: C(roof), tileAlt: sh(C(roof), 0.88), ridge: sh(C(roof), 0.7) };
    farRoof(K, x, y + h, z, w + 1.0, d + 1.0, d * 0.42, pal, style || 'hip');
  }
  /** 远景树：一个软团（20 三角形）+ 4 棱树干；kind 'poplar' / 'cypress' 为细高 */
  function fTree(K, x, z, s, kind, y0) {
    const M = K.M, f = foliage(kind || 'oak'), y = y0 || 0, tall = kind === 'poplar' || kind === 'cypress' || kind === 'pine' || kind === 'cedar';
    M.color = H('#8A6644'); M.cylinder([x, y, z], 0.4 * s, 0.3 * s, 2.2 * s, 4, false, false, false);
    const ramp = LP.canopyRamp(f.top, f.side, f.bottom, y + 1.6 * s, y + (tall ? 9 : 7) * s, { coolMix: 0.58 });
    M.softLump([x, y + (tall ? 5 : 4.4) * s, z], tall ? [1.5 * s, 3.6 * s, 1.5 * s] : [2.7 * s, 2.2 * s, 2.5 * s], 0, (x * 31 + z) % 360, ramp, 0.1);
  }
  /** 远景棕榈：细干 + 5 片叶（单面三角） */
  function fPalm(K, x, z, s) {
    const M = K.M, f = foliage('palm'), h = 6.6 * s;
    M.color = H('#9A7A52'); M.segment([x, 0, z], [x + 0.4 * s, h, z], 0.28 * s, 0.2 * s, 4, false, false);
    for (let i = 0; i < 5; i++) { const a = i / 5 * Math.PI * 2, ca = Math.cos(a), sa = Math.sin(a), L = 3.2 * s, w = 0.8 * s; M.triangleFacing([x + 0.4 * s, h + 0.2, z], [x + 0.4 * s + ca * L - sa * w, h - 1.0 * s, z + sa * L + ca * w], [x + 0.4 * s + ca * L + sa * w, h - 1.0 * s, z + sa * L - ca * w], [0, 1, 0], i % 2 ? f.side : P.mix(f.side, f.top, 0.5)); }
  }
  /** 远景圆环（栅栏 / 环墙 / 土垒）：n 段，每段外壁 + 顶面（+ 内壁）；gap 南口 */
  function fRing(K, R, Rz, h, t, col, o) {
    o = o || {};
    const M = o.plain ? K.Mp : K.M, n = o.n || 12, c = C(col), y0 = o.y0 || 0, gap = o.gap || 0;
    for (let i = 0; i < n; i++) {
      const a0 = i / n * Math.PI * 2 + (o.rot || 0), a1 = a0 + Math.PI * 2 / n, am = (a0 + a1) / 2;
      if (gap && Math.abs(Math.atan2(Math.sin(am - Math.PI / 2), Math.cos(am - Math.PI / 2))) < gap) continue;
      if (o.skip && o.skip(Math.cos(am) * R, Math.sin(am) * Rz)) continue;
      const Q = (a, k, y) => [Math.cos(a) * (R + k * t / 2), y0 + y, Math.sin(a) * (Rz + k * t / 2)];
      M.quadColors(Q(a0, 1, 0), Q(a1, 1, 0), Q(a1, o.bank ? 0.3 : 1, h), Q(a0, o.bank ? 0.3 : 1, h), [Math.cos(am), 0.2, Math.sin(am)], [sh(c, 0.82), sh(c, 0.82), c, c]);
      M.quad(Q(a0, o.bank ? 0.3 : 1, h), Q(a1, o.bank ? 0.3 : 1, h), Q(a1, o.bank ? -0.3 : -1, h), Q(a0, o.bank ? -0.3 : -1, h), [0, 1, 0], sh(c, 1.08));
      M.quad(Q(a0, o.bank ? -0.3 : -1, h), Q(a1, o.bank ? -0.3 : -1, h), Q(a1, -1, 0), Q(a0, -1, 0), [-Math.cos(am), 0.2, -Math.sin(am)], sh(c, 0.9));
    }
  }
  function fMound(K, x, z, rx, rz, h, top, side, flat) {
    const M = K.Mp, t = C(top), s = C(side), k = flat || 0.6;
    M.push().translate(x, 0, z).scale(1, 1, rz / rx);
    M.lathe([[rx, -0.5], [lerp(rx, rx * k, 0.6), h * 0.8], [rx * k, h], [0, h]], 8, false, false, false, [sh(s, 0.85), mix(s, t, 0.5), t, t], 22.5);
    M.pop();
  }
  function fWater(K, pts, col) { water(K, pts, 0.07, col || '#5E9C94'); }
  /** 远景的篝火（常亮的小火锥，4 个三角形）：车营 / 毡帐营地 */
  function fFire(K, x, z, s) {
    const M = K.M, ink = M.ink; s = s || 1;
    M.ink = 0; M.emission = 0.9; M.color = H('#FFB040');
    M.push().translate(x, 0, z); M.lathe([[0.9 * s, 0.1], [0, 2.4 * s]], 4, false, false, false, null, 45); M.pop();
    M.emission = 0; M.ink = ink;
  }

  F.roman = function (K, o) {
    basePad(K, 'roman', true);
    fGlow(K, 'roman', 0, o.cap ? 3 : -0.5, 5);
    farWalls(K, U, 7.4, 3.4, 'stone');
    for (const [x, z] of [[-U, U], [U, U], [-U, -U], [-6.8, U + 1], [6.8, U + 1]]) fTower(K, x, z, 3.0, 10, '#CCC0A6', '#C9785A');
    fBox(K, 0, 0, U + 0.4, 10.4, 8.6, 5, '#D9CEB6');
    if (o.cap) {
      const M = K.M, c = C('#E2D6BC');
      M.push().translate(-7.2, 0, -6.5).scale(1, 1, 7 / 8.6);
      M.color = c; M.lathe([[8.6, 0], [8.6, 6.4], [7.4, 6.4], [5.0, 2.4], [0, 2.4]], 8, false, false, false, [sh(c, 0.88), c, sh(c, 1.06), sh(c, 0.82), H('#E2C894')]);
      M.pop();
      fBox(K, 8.2, 0, -9.5, 7.2, 1.5, 10.5, '#E6DFCC'); fHouse(K, 8.2, -9.5, 6.4, 9.6, 6.8, '#EDE6D6', 'terracotta', 'gable');
    } else {
      fBox(K, 0, 0, -11.2, 7.4, 1.6, 10.5, '#E6DFCC'); fHouse(K, 0, -11.2, 6.6, 9.6, 7.2, '#EDE6D6', 'terracotta', 'gable');
      fBox(K, -6.4, 0, -0.5, 3.6, 4.6, 9, '#E6DFCC', '#C9785A');
    }
    for (const [x, z, w, d] of o.cap ? [[-12, 10.5, 6, 4.6], [12, 10.5, 6.2, 4.6], [13.5, 2.5, 4.6, 5.6]] : [[-12, 10.5, 6, 4.6], [12, 10.5, 6.2, 4.6], [-13, 3.5, 5, 5], [13, 4, 5, 5], [12.5, -9.5, 5, 4.6]]) fHouse(K, x, z, w, d, 4.2, '#EFE4CE', 'terracotta', 'hip');
    if (!o.cap) fTree(K, -15, -15.5, 0.75, 'cypress');
  };
  F.persia = function (K, o) {
    basePad(K, 'persia', true);
    fGlow(K, 'persia', 0, 14.5, 4);
    farWalls(K, U, 7.8, 4.0, 'mud');
    for (const [x, z] of [[-U, U], [U, U], [-U, -U], [-7.2, U + 1.4], [7.2, U + 1.4]]) fTower(K, x, z, 3.1, 9.6, '#D8BA8C');
    fBox(K, 0, 0, U + 0.6, 11.6, 11, 6.4, '#DDBE90');
    const M = K.M; M.ink = 0; M.color = H('#3E7CA8'); M.quad([-2.2, 0, U + 3.85], [2.2, 0, U + 3.85], [2.2, 7, U + 3.85], [-2.2, 7, U + 3.85], [0, 0, 1]); M.ink = 1;
    if (o.cap && o.landmark) {
      for (const s of [-1, 1]) fBox(K, -2 + s * 7.9, 0, -8.5, 6.2, 13, 3.2, '#D6BC92');
      M.color = H('#D6BC92'); const prof = [[-4.7, 0.3], [-3.6, 9.5], [-1.6, 15.2], [1.6, 15.2], [3.6, 9.5], [4.7, 0.3]];
      M.loft([prof.map(([x, y]) => [x - 2, y, -6.9]), prof.map(([x, y]) => [x - 2, y, -19.5])], false, true);
      M.ink = 0; M.color = H('#5A4636'); M.quad([-5.5, 0.2, -6.85], [1.5, 0.2, -6.85], [1.0, 12, -6.85], [-5.0, 12, -6.85], [0, 0, 1]); M.ink = 1;
      fBox(K, 11.5, 0, 3.5, 6.4, 6.2, 6.4, '#E2D2AE'); M.push().translate(11.5, 6.2, 3.5); M.color = H('#E2D2AE'); M.lathe([[2.7, 0], [1.9, 2.0], [0, 2.8]], 6, false, false, false); M.pop();
    } else {
      fBox(K, 0, 0, -9.5, 21, 1.5, 12, '#D2B88E'); fBox(K, 0, 1.5, -9.9, 15, 1.5, 9, '#D9C29A');
      fBox(K, 0, 3, -10.6, 9.5, 7.4, 6.6, '#DDBE90');
      M.ink = 0; M.color = H('#3E7CA8'); M.quad([-2.4, 3, -7.25], [2.4, 3, -7.25], [2.4, 9.2, -7.25], [-2.4, 9.2, -7.25], [0, 0, 1]); M.ink = 1;
      if (o.tier >= 1) { M.push().translate(0, 10.4, -11.2); M.color = H(o.cap ? '#4E8EB0' : '#5E9CB4'); M.lathe([[3.4, 0], [2.6, 2.2], [0, 3.4]], 6, false, false, false); M.pop(); }
      fWater(K, [[-3.2, 4.4], [3.2, 4.4], [3.2, 7.6], [-3.2, 7.6]]);
      if (o.cap) { fBox(K, 11.5, 0, 3.5, 6.4, 6.2, 6.4, '#E2D2AE'); M.push().translate(11.5, 6.2, 3.5); M.color = H('#E2D2AE'); M.lathe([[2.7, 0], [1.9, 2.0], [0, 2.8]], 6, false, false, false); M.pop(); }
    }
    for (const [x, z, w, d] of [[-12.5, 11, 5.6, 4.8], [12.5, 11, 5.6, 4.8], [-13.5, 2.5, 5, 5], [13.5, 2.5, 5, 5], [14, -7, 4.6, 5]]) if (!(o.cap && !o.landmark && x === 13.5)) fHouse(K, x, z, w, d, 4.0, '#D2B486', '#DCC098', 'flat');
    fTree(K, 7.5, 14, 0.6, 'cypress');
  };
  F.arab = function (K, o) {
    basePad(K, 'arab', true);
    fGlow(K, 'arab', 0, 11.5, 3.8);
    fRing(K, 18.6, 18.6, 6.8, 3.2, '#D2AE84', { n: 12, gap: 0.2 });
    for (let i = 0; i < 6; i++) { const a = Math.PI / 2 + Math.PI / 6 + i * Math.PI / 3, x = Math.cos(a) * 18.6, z = Math.sin(a) * 18.6; if (!(x > 10 && z < -10)) fTower(K, x, z, 2.6, 8.2, '#D8B68C'); }
    fBox(K, 0, 0, 18.1, 9.2, 9, 5.2, '#D2AE84');
    const s = o.cap ? 1.2 : 1, M = K.M;
    for (const sx of [-1, 1]) fBox(K, sx * 3.3 * s, 0, -7.5, 6.4 * s, 8.4 * s, 7, '#D8B68C');
    M.ink = 0; M.color = H('#4A3A30'); for (const sx of [-1, 1]) M.quad([sx * 3.3 * s - 1.6, 0, -3.95], [sx * 3.3 * s + 1.6, 0, -3.95], [sx * 3.3 * s + 1.6, 6, -3.95], [sx * 3.3 * s - 1.6, 6, -3.95], [0, 0, 1]); M.ink = 1;
    for (const [x, z, w, d] of [[-11, 9.5, 5.2, 4.4], [10.5, 9.5, 5.2, 4.4], [-14.2, 0.5, 4.2, 5], [14.2, 1.5, 4.2, 5], [-11.8, -12.5, 4.6, 4.2]]) fHouse(K, x, z, w, d, 3.8, '#D6C098', '#DCC8A0', 'flat');
    fPalm(K, -7, 6.5, 0.7); fPalm(K, 6.5, 9.5, 0.7);
  };
  F.kushan = function (K, o) {
    basePad(K, 'kushan', true);
    fGlow(K, 'kushan', 0, 12, 4.2);
    farWalls(K, U, 7.0, 3.8, 'buff');
    for (const [x, z] of [[-U, U], [U, U], [-U, -U]]) fTower(K, x, z, 3.5, 8.4, '#D2AE84');
    fBox(K, 0, 0, U + 0.5, 10.4, 9, 6, '#D2AE84');
    const M = K.M;
    if (o.cap) { fBox(K, 0, 0, -8.5, 20, 3.2, 13, '#D8C29C'); fBox(K, 0, 3.2, -10.4, 13, 5.1, 8.8, '#E8DCC4'); M.push().translate(0, 8.3, -11.2); M.color = H('#F0EADC'); M.lathe([[3.2, 0], [2.4, 2.4], [0, 3.8]], 6, false, false, false); M.pop(); }
    else {
      const r = o.tier ? 4.4 : 3.8;
      fBox(K, 0, 0, -8.8, r * 2.6, 1.6, r * 2.6, '#CFC2A6');
      M.push().translate(0, 1.6, -8.8); M.color = H('#F0EADC'); M.lathe([[r, 0], [r, 1.0], [r * 0.7, r * 0.8 + 1], [0, r + 1]], 8, false, false, false); M.color = PAL.gold; M.cone([0, r + 0.8, 0], 0.3, 2.4, 4); M.pop();
      fBox(K, -10, 0, -1, 9, 3.2, 9, '#D6BE92');
    }
    for (const [x, z, w, d] of [[11.5, 10.5, 6, 4.6], [-12, 11, 5.6, 4.4], [13.5, 2, 4.6, 5.4], [13.5, -6.5, 4.6, 4.8]]) fHouse(K, x, z, w, d, 3.8, '#D2B88E', '#DCC6A0', 'flat');
    fTree(K, -7, 7.5, 0.65, 'poplar');
  };
  F.india = function (K, o) {
    basePad(K, 'india', true);
    fGlow(K, 'india', 0, 12, 4.2, 0.1);
    ringBand(K, 21.2, 1.8, 0.06, '#5E9C94', 2.6);
    farWalls(K, U, 6.6, 3.4, 'redbrick');
    for (const [x, z] of [[-U, U], [U, U], [-U, -U]]) fTower(K, x, z, 2.9, 7.8, '#C67858');
    fBox(K, 0, 0, U + 0.4, 9.6, 8, 5.6, '#C67858');
    const M = K.M, vault = (x, y, z, w, len, col) => { const R = w / 2 + 0.3, ring = zz => [[x - R, y, zz], [x - R * 0.7, y + R * 0.75, zz], [x, y + R * 1.05, zz], [x + R * 0.7, y + R * 0.75, zz], [x + R, y, zz]]; M.color = C(col); M.loft([ring(z - len / 2), ring(z + len / 2)], true, true); };
    if (o.cap) { fBox(K, 3, 0, -10, 18, 6.2, 10, '#E8D8B8'); vault(3, 6.2, -10.6, 8.5, 5.2, '#B86C4E'); }
    else { fBox(K, -11.5, 0, -6, 6, 4, 9, '#E2CCA4'); vault(-11.5, 4, -6, 6, 9, '#B86C4E'); }
    const r = o.cap ? 2.8 : 3.8, sx = o.cap ? -9 : 0;
    fBox(K, sx, 0, o.cap ? -8.5 : -10, r * 2.6, 1.4, r * 2.6, '#CFC2A6');
    M.push().translate(sx, 1.4, o.cap ? -8.5 : -10); M.color = H('#F0EADC'); M.lathe([[r, 0], [r, 0.9], [r * 0.7, r * 0.8 + 0.9], [0, r + 0.9]], 8, false, false, false); M.pop();
    for (const [x, z, w, d] of [[-11.5, 10.5, 5.6, 4.6], [11.5, 10.5, 5.6, 4.6], [13.2, 1.5, 4.8, 5.2], [12.5, -8, 4.6, 4.8]]) fHouse(K, x, z, w, d, 3.6, '#E2CCA4', '#C9A47C', 'flat');
    fPalm(K, 7.5, 7.5, 0.75); fPalm(K, -8, 6.5, 0.75);
  };
  F.korea = function (K, o) {
    basePad(K, 'korea', true);
    fGlow(K, 'korea', 0, 9.5, 4.4);
    fMound(K, -1.5, -8.5, 15.5, 10.5, 4.2, '#A9B274', '#8E9A5E', 0.52);
    fRing(K, 19.2, 18.8, 5.4, 2.8, '#B4AFA2', { n: 10, gap: 0.3, rot: Math.PI / 2 + Math.PI / 10 });
    fBox(K, 0, 0, 18.9, 7.8, 6.2, 6, '#B8B2A4'); fHouse(K, 0, 18.9, 4.4, 3.2, 3.2, '#E8E0CE', 'grey', 'hip', 6.4);
    fBox(K, -1.5, 4.2, -9.5, 12, 1, 9, '#CFC8B6');
    fHouse(K, -1.5, -9.5, o.cap ? 11.6 : 9.6, 6.2, 4.4, '#C9503A', 'grey', 'hip', 5.2);
    for (const [x, z, w, d, rf] of [[-11.5, 9.5, 5.8, 4.2, 'grey'], [11, 9.5, 5.8, 4.2, strawC('#C4A86A')], [-13, 2.5, 5.2, 4, strawC('#C4A86A')], [12.5, 2.5, 5.2, 4.2, 'grey']]) fHouse(K, x, z, w, d, 3.0, '#E6DCC4', rf, rf === 'grey' ? 'hip' : 'loaf');
    fTree(K, -12.5, -12, 0.8, 'pine', 2.3); fTree(K, 15.5, 14.5, 0.55, 'pine');
  };
  F.wa = function (K, o) {
    basePad(K, 'wa', true);
    fGlow(K, 'wa', 0, 6, 5);
    waterRing(K, 19.6, 18.8, 2.4, 0.04, '#5E9690', 0.22, 12, true);
    fRing(K, 16.6, 16.2, 3.4, 0.7, '#8A6844', { n: 12, gap: 0.22 });
    for (const [x, z] of [[-9.5, 11.5], [9.5, 11.5]]) { fBox(K, x, 0, z, 2.8, 7.4, 2.8, '#8A6A48'); fWin(K, x, 6.5, z + 1.4, 0, 1, 1.6, 1.2); fHouse(K, x, z, 3.4, 3.4, 0.01, '#9A7A54', strawC('#B49A62'), 'cone', 7.4); }
    if (o.cap) { fBox(K, 0, 0, -5.5, 10, 5.5, 6.6, '#A88660'); fWin(K, 0, 3.8, -2.2, 0, 1, 2.6, 1.6); fHouse(K, 0, -5.5, 10, 6.6, 0.01, '#A88660', strawC('#B49660'), 'loaf', 5.6); }
    for (const [x, z] of o.cap ? [[-9.5, -11], [9.5, -11]] : [[-5, -9.5], [3.5, -10.5]]) { fBox(K, x, 0, z, 4.6, 4.0, 3.8, '#B89A6A'); fHouse(K, x, z, 4.6, 3.8, 0.01, '#B89A6A', strawC('#B89A62'), 'loaf', 4.0); }
    for (const [x, z] of [[-10.5, 1], [10.5, 2], [-4.5, 5.5], [4.5, 7.5], [-11, -8.5]]) { const M = K.M; M.push().translate(x, 0, z); M.color = strawC('#B49A62'); M.lathe([[2.9, 0], [0, 3.2]], 6, false, false, false, [sh(strawC('#B49A62'), 0.8), sh(strawC('#B49A62'), 1.1)], 30); M.pop(); }
  };
  F.steppe = function (K, o) {
    basePad(K, 'steppe', true);
    fGlow(K, 'steppe', 0, 6, 5);
    for (let i = 0; i < 7; i++) { const a = Math.PI / 2 + 0.55 + i * (Math.PI * 2 - 1.1) / 6, x = Math.cos(a) * 17.2, z = Math.sin(a) * 16.8; if (x > 11 && z < -11) continue; const M = K.M; M.push().translate(x, 0, z).rotate(0, -a / D2R + 90, 0); fBox(K, 0, 0.6, 0, 4.4, 2.2, 2.0, '#D8CCAE', '#E2D6BA'); M.pop(); }
    const ys = o.cap ? [[0, -4.5, 6.4], [-10, 5, 3], [10, 4.5, 3], [-8.5, -8, 3.4], [8.5, -8, 3.4]] : [[0, -5.5, 4.2], [-9.5, 3.5, 3], [9.5, 4, 2.8], [-11.5, -6, 2.8], [-3.5, 11, 2.6], [6, 11.5, 2.5]];
    ys.forEach(([x, z, r], i) => fHouse(K, x, z, r * 2, r * 2, r * 0.62, '#EEE4D0', i === 0 && o.cap ? '#D8A838' : '#E6D6B4', 'cone'));
    fFire(K, 2.5, 4.2, 0.8);
  };
  F.tarim = function (K, o) {
    basePad(K, 'tarim', true);
    fGlow(K, 'tarim', 0, 14.5, 3.6);
    farWalls(K, 19.5, 6.0, 3.6, 'mud');
    for (const [x, z] of [[-19.5, 19.5], [19.5, 19.5], [-19.5, -19.5], [0, 19.9]]) fBox(K, x, 0, z, 5.2, 7.2, 5.2, '#D2B183');
    fBox(K, -13.5, 0, -13.5, 4.2, 9.5, 4.2, '#D2B183');
    fWater(K, [[-18.6, 6.6], [18.6, 6.6], [18.6, 7.6], [-18.6, 7.6]]);
    for (const x of [-12, 1.5, 15]) fTree(K, x, 9.4, 0.55, 'poplar');
    if (o.cap || o.tier) { const M = K.M; fBox(K, -2, 0, -8.5, 9, 1.5, 9, '#CDB490'); M.push().translate(-2, 1.5, -8.5); M.color = H('#E8DCC2'); M.lathe([[3.4, 0], [3.4, 1.0], [2.4, 3.6], [0, 4.4]], 8, false, false, false); M.pop(); }
    for (const [x, z, w, d] of [[-12, 13.5, 5, 4], [12, 13.5, 5, 4], [-13, 1.5, 5.2, 4.8], [13.5, 0, 4.6, 4.4], [12.5, -9.5, 4.6, 4.6], [-4.5, 1.5, 4.6, 4]]) fHouse(K, x, z, w, d, 3.6, '#D2B486', '#DCC098', 'flat');
  };
  F.seasia = function (K, o) {
    basePad(K, 'seasia', true);
    fGlow(K, 'seasia', 0, 3, 4.5, 0.09);
    fWater(K, [[-23, 9.2], [23, 9.2], [23, 11.4], [-23, 11.4]]); fWater(K, [[-15.2, -18], [-13, -18], [-13, 9.2], [-15.2, 9.2]]);
    for (const [x, z, w, d] of [[0, -18.4, 36.8, 0.8], [-18.4, 0, 0.8, 36.8], [18.4, 3, 0.8, 30.8], [-10.6, 18.4, 15.6, 0.8], [10.6, 18.4, 15.6, 0.8]]) fBox(K, x, 0, z, w, 3.2, d, '#8A6A44');
    const s = o.cap ? 1.25 : 1;
    fBox(K, 0, 0, -7.5, 13 * s, 2.6, 11 * s, '#C67858'); fBox(K, 0, 2.6, -7.5, 7 * s, 3.6, 5.6 * s, '#C67858');
    for (const [w, d, y] of [[9.4, 7.6, 6.2], [7, 5.6, 8.0], [4.6, 3.8, 9.8]]) farRoof(K, 0, y, -7.5, w * s, d * s, 1.6, ROOF.terracotta, 'hip');
    for (const [x, z, w, d] of [[-9, 5, 6, 4.4], [8.5, 4.5, 6, 4.4], [-6.5, 15, 5, 4], [7, 15, 5, 4], [12.5, -4, 4.8, 4.2]]) { fBox(K, x, 1.6, z, w, 2.2, d, '#B89A6A'); fWin(K, x, 2.7, z + d / 2, 0, 1, 1.6, 1.2); fHouse(K, x, z, w, d, 0.01, '#B89A6A', strawC('#B8975A'), 'loaf', 3.8); }
    fPalm(K, 17, 13.5, 0.75); fPalm(K, -16.5, 6.5, 0.75);
  };
  F.village = function (K, o) {
    const nan = o.culture === 'nanman';
    basePad(K, 'village:' + (nan ? 'nanman' : 'yi'), true);
    fGlow(K, 'village:' + (nan ? 'nanman' : 'yi'), 0, 1.5, 5);
    fRing(K, 16.8, 16.1, 2.6, 0.7, nan ? '#7E5E40' : '#B4A866', { n: 12, gap: 0.2, skip: (x, z) => x > 10 && z < -10 });
    const hs = [[-9.5, 4, 5, 4], [9, 4.5, 5, 4], [-6, 10.5, 4.6, 3.6], [6.5, 10.5, 4.6, 3.6], [-11, -5.5, 4.6, 3.8], [10.5, -5, 4.6, 3.8]];
    if (o.cap || o.tier) hs.push([0, -10.5, 8.5, 5.8]); else hs.push([-3.5, -10.5, 4.6, 3.6]);
    for (const [x, z, w, d] of hs) { fBox(K, x, 1.4, z, w, 2.0, d, nan ? '#9A7A54' : '#B49C6A'); fWin(K, x, 2.4, z + d / 2, 0, 1, 1.5, 1.1); fHouse(K, x, z, w, d, 0.01, '#B49C6A', strawC(nan ? '#A8884E' : '#B8975A'), 'loaf', 3.4); }
    if (nan) fBox(K, 0, 0, -2, 5.4, 2.4, 5.4, '#9A9284', '#6E8A62');
    fPalm(K, 14, 10.5, 0.62); fPalm(K, -14, 9, 0.62);
  };
  F.hillfort = function (K, o) {
    const ger = o.culture === 'german';
    basePad(K, 'hillfort:' + (ger ? 'german' : 'celt'), true);
    fGlow(K, '', 0, 1.5, 5, 2.66, ger ? '#A8A070' : '#ACA472');
    fMound(K, 0, -1, 18.2, 17.4, 2.6, ger ? '#A8A070' : '#ACA472', '#86965A', 0.84);
    fRing(K, 20.6, 20, 1.1, 2.2, '#86985A', { n: 12, gap: 0.2, bank: true, plain: true });
    fRing(K, 14.4, 13.8, 2.4, 0.6, '#8A6844', { n: 12, gap: 0.24, y0: 3.4 });
    const y = 2.6;
    if (ger) for (const [x, z, len, w, yaw] of o.cap ? [[0, -5.5, 15, 6.2, 0], [-7.5, 4.5, 10, 4.4, 20], [7.5, 4, 10, 4.4, -15]] : [[0, -6.5, 12, 5, 0], [-7.5, 3.5, 10, 4.4, 25], [7.5, 3.5, 10, 4.4, -20]]) { const M = K.M; M.push().translate(x, 0, z).rotate(0, yaw, 0); fBox(K, 0, y, 0, len, 2.0, w, '#7E5E42'); fWin(K, 0, y + 1.0, w / 2, 0, 1, 1.6, 1.3); fHouse(K, 0, 0, len, w + 1.6, 0.01, '#7E5E42', strawC('#A88C58'), 'loaf', y + 2.0); M.pop(); }
    else for (const [x, z, r] of o.cap ? [[0, -4.5, 5.6], [-8, 4.5, 2.8], [8, 4.5, 2.8], [-8.5, -8, 2.6], [8.5, -7.5, 2.6]] : [[0, -5, 3.6], [-7.5, 3.5, 3], [7.5, 3.5, 3], [-8, -6.5, 2.7], [8, -6.5, 2.7], [0, 6.5, 2.6]]) fHouse(K, x, z, r * 2, r * 2, 1.9, '#C4AC86', strawC('#B49A5E'), 'cone', y);
    fTree(K, 16, 12.5, 0.7, 'oak');
  };
  F.camp = function (K, o) {
    basePad(K, 'camp', true);
    fGlow(K, 'camp', 0, 4, 5);
    for (let i = 0; i < 6; i++) { const a = Math.PI / 2 + 0.6 + i * (Math.PI * 2 - 1.2) / 5, x = Math.cos(a) * 16.8, z = Math.sin(a) * 16.4; if (x > 10 && z < -10) continue; const M = K.M; M.push().translate(x, 0, z).rotate(0, -a / D2R + 90, 0); fBox(K, 0, 0.6, 0, 4.6, 2.2, 2.0, '#C8B490', '#D8C8A0'); M.pop(); }
    for (const [x, z, r, h] of [[0, -6, 4.2, 6.2], [-8.5, 2.5, 3, 4.6], [8.5, 2.5, 3, 4.6], [-9, -8.5, 2.8, 4.2], [7.5, -8.5, 2.8, 4.2], [-3.5, 9, 2.6, 4], [4.5, 9.5, 2.6, 4]]) { const M = K.M; M.push().translate(x, 0, z); M.color = H('#D8CCB0'); M.lathe([[r, 0], [0, h]], 6, false, false, false, [H('#B8A88A'), H('#E2D6BC')]); M.pop(); }
    fFire(K, 0, 1.5, 0.8);
  };
  F.greek = function (K, o) {
    basePad(K, 'greek', true);
    fGlow(K, 'greek', 0, 12, 4);
    fMound(K, -2, -9.5, 12.5, 8.6, 4.4, '#BDB68C', '#9C9C6C', 0.6);
    farWalls(K, U, 6.4, 3.0, 'whitestone');
    for (const [x, z] of [[-U, U], [U, U], [-U, -U], [-U, 0], [U, 2]]) fBox(K, x, 0, z, 5, 8.2, 5, '#EDE6D6');
    fBox(K, 0, 0, U + 0.4, 8.4, 7.8, 5, '#EDE6D6');
    fBox(K, -2, 4.4, -10, 7, 5.6, 9.4, '#F2ECDE'); farRoof(K, -2, 10, -10, 7.6, 10, 1.4, ROOF.terracotta, 'hip');
    for (const [x, z, w, d] of [[-11.5, 10.5, 5.6, 4.4], [11.5, 10.5, 5.6, 4.4], [-13, 2, 4.8, 4.6], [12.5, 2.5, 4.8, 4.6], [11.5, -7.5, 4.8, 4.6], [5.5, 2.5, 4.6, 4.2]]) fHouse(K, x, z, w, d, 3.8, '#EFE4CE', 'terracotta', 'hip');
  };
  F.kush = function (K, o) {
    basePad(K, 'kush', true);
    fGlow(K, 'kush', 0, 11, 4.2);
    fWater(K, [[-23.6, -21], [-21.4, -21], [-21.4, 21], [-23.6, 21]], '#4E8E8E');
    farWalls(K, U, 6.2, 3.4, 'mudred');
    for (const [x, z] of [[-U, U], [U, U], [-U, -U], [0, U + 0.3]]) fBox(K, x, 0, z, 4.8, 7.4, 4.8, '#C99068');
    const M = K.M, tz = o.cap ? -1.5 : -2.5;
    for (const s of [-1, 1]) { M.color = H('#D8B888'); M.taperedBox([s * 3.2, 0, tz], [4.6, 7.2, 3.2], 0.24, 0.2); }
    fBox(K, 0, 0, tz - 9, 9, 4.6, 10, '#D2AE84');
    for (const [x, z, b, h] of o.cap ? [[-12, -12.5, 6.4, 12.5], [-5.5, -15.5, 5.4, 10.5], [-15.5, -4.5, 5, 9.5]] : [[-12, -12, 5.8, 11], [-5.5, -15.5, 4.8, 9]]) { M.push().translate(x, 0, z); M.color = H('#B98A5E'); M.lathe([[b / 2 * Math.SQRT2, 0], [0.2, h]], 4, false, false, false, [H('#A87C52'), H('#C89A6A')], 45); M.pop(); }
    for (const [x, z, w, d] of [[-11.5, 11, 5.4, 4.4], [11.5, 11, 5.4, 4.4], [13.2, 2.5, 4.6, 5], [12.5, -8.5, 4.6, 4.4]]) fHouse(K, x, z, w, d, 3.6, '#C89670', '#D4AA80', 'flat');
    fPalm(K, -19.6, 8, 0.72); fPalm(K, -7, 6.5, 0.72);
  };
  F.aksum = function (K, o) {
    basePad(K, 'aksum', true);
    fGlow(K, 'aksum', 0, 5, 5);
    fRing(K, 19, 18.3, 1.8, 1.5, '#A89C86', { n: 12, gap: 0.2, skip: (x, z) => x > 9 && z < -9 });
    for (const [x, z, h] of o.cap ? [[-9, -14.5, 20], [-3.5, -15.5, 15], [2, -15.8, 12]] : [[-5.5, -15, 14], [-0.5, -15.8, 10], [-10, -12.5, 8]]) { fBox(K, x, 0, z, h * 0.12, h, h * 0.07, '#A8A49A', '#B8B4AA'); }
    const px = o.cap ? 4.5 : 5, pz = o.cap ? -3 : -4, s = o.cap ? 1.25 : 1;
    fBox(K, px, 0, pz, 15 * s, 2, 13 * s, '#B4A88E', '#C2B694'); fBox(K, px, 2, pz, 8 * s, 7.5 * s, 7 * s, '#B8AC96', '#B4A88E');
    for (const [x, z, r] of [[-11.5, 3.5, 3], [-6.5, 11, 2.8], [5, 11.5, 2.8], [12, 8.5, 2.8], [-13, -4.5, 2.6]]) fHouse(K, x, z, r * 2, r * 2, 2.0, '#B49C7A', strawC('#A88C52'), 'cone');
    fTree(K, -15.5, -1, 0.65, 'olive');
  };
  F.moor = function (K, o) {
    basePad(K, 'moor', true);
    fGlow(K, '', 0, 5, 4.5, 2.66, '#B8A47C');
    fMound(K, -0.5, -2, 17.8, 16.4, 2.6, '#B8A47C', '#9C8A64', 0.86);
    fRing(K, 14.6, 13.6, 2.8, 1.6, '#B8AC96', { n: 12, gap: 0.24, y0: 2.4 });
    const s = o.cap ? 1.2 : 1;
    fBox(K, -3, 2.6, -8, 8.5 * s, 8.5 * s, 8.5 * s, '#B8AC96', '#A89C86');
    for (const [x, z, w, d] of [[-8, 4, 5, 4.4], [7.5, 3.5, 5, 4.4], [-9.5, -5, 4.4, 4.4], [9.5, -5.5, 4.4, 4.2], [-3, 8.5, 4.4, 3.6]]) fHouse(K, x, z, w, d, 3.2, '#BCA88A', '#A8987C', 'flat', 2.6);
    for (const [x, z] of [[18.6, 8.5], [-18.6, 7.5]]) fBox(K, x, 0, z, 3.4, 1.8, 4.8, '#5A4A3A');
    fTree(K, -17.5, -3.5, 0.75, 'cedar');
  };

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
    else if (m === 'persia' && capital && city && city.key === 'ctesiphon') m = 'persia:ctesiphon';   // 唯一的大拱（Taq Kasra）
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
    // 中景（mid）与近景同一个种子、同一套布局与配色，只是去掉垛口、墙缝、灯笼、屋脊饰、多余的树冠团（远近切换时不跳色）
    const seed = (strHash(mk + ':' + tier + ':' + variant + ':' + (lod === 'mid' ? 'near' : lod)) % 100000) + 1;
    const K = context(seed);
    K.glowLeft = lod === 'far' ? 3 : 0;
    K.mid = lod === 'mid';
    const o = { cap: tier === 2, tier, variant, culture, city, far: lod === 'far', landmark: mk.endsWith(':ctesiphon') };
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
      g.userData.sharedGeometry = true;   // 缓存、多城共用：只由 CultureArt.clearCache 释放（battle-view 等的 dispose 跳过它）
      out[name] = g;
      out.tris += g.userData.tris;
      out.height = Math.max(out.height, g.boundingBox.max.y);
    }
    // 墨线宽为 0 的贴面三角形（窗纸、门洞、墙缝、匾、门钉……）移到不描墨线的部分：墨线外壳（逐三角形再画一遍）少画这些
    if (out.ink) {
      const sp = splitInk0(out.ink);
      if (sp) {
        out.ink = sp.keep;
        out.plain = out.plain ? mergeGeo(out.plain, sp.move) : sp.move;
        for (const g of [out.ink, out.plain]) { g.userData.cultureArt = mk; g.userData.sharedGeometry = true; }
      }
    }
    out.lamps = K.lamps.map(l => Object.assign({}, l, { pos: [l.pos[0] * k, l.pos[1] * k + LIFT, l.pos[2] * k], range: l.range * k, haloSize: l.haloSize * k }));
    return out;
  }
  /** 非索引 LP 几何体里挑出若干三角形（tris = 三角形序号表）组成新几何体（属性与类型不变） */
  function subsetGeo(g, tris) {
    const out = new THREE.BufferGeometry();
    for (const name of Object.keys(g.attributes)) {
      const a = g.attributes[name], is = a.itemSize, per = 3 * is, arr = new a.array.constructor(tris.length * per);
      tris.forEach((t, i) => arr.set(a.array.subarray(t * per, t * per + per), i * per));
      out.setAttribute(name, new THREE.BufferAttribute(arr, is, a.normalized));
    }
    out.computeBoundingBox(); out.computeBoundingSphere();
    out.userData = Object.assign({}, g.userData, { tris: tris.length });
    return out;
  }
  function mergeGeo(a, b) {
    const out = new THREE.BufferGeometry();
    for (const name of Object.keys(a.attributes)) {
      const x = a.attributes[name], y = b.attributes[name];
      if (!y) continue;
      const arr = new x.array.constructor(x.array.length + y.array.length);
      arr.set(x.array, 0); arr.set(y.array, x.array.length);
      out.setAttribute(name, new THREE.BufferAttribute(arr, x.itemSize, x.normalized));
    }
    out.computeBoundingBox(); out.computeBoundingSphere();
    out.userData = Object.assign({}, a.userData, { tris: a.userData.tris + b.userData.tris });
    return out;
  }
  function splitInk0(g) {
    const sm = g.attributes.lpSmooth && g.attributes.lpSmooth.array;
    if (!sm) return null;
    const keep = [], move = [], n = sm.length / 9;
    for (let t = 0; t < n; t++) {
      let zero = true;
      for (let j = t * 9; j < t * 9 + 9 && zero; j++) if (sm[j] !== 0) zero = false;
      (zero ? move : keep).push(t);
    }
    if (!move.length || !keep.length) return null;
    return { keep: subsetGeo(g, keep), move: subsetGeo(g, move) };
  }
  function model(culture, capital, city, opts) {
    if (!ready()) return null;
    opts = opts || {};
    const lod = opts.lod === 'far' ? 'far' : opts.lod === 'mid' ? 'mid' : 'near';
    const mk = modelKey(culture, city, capital);
    const tier = tierOf(opts.size, capital, city);
    const variant = lod === 'far' || tier === 2 ? 0 : variantOf(city);
    const key = mk + '|' + tier + '|' + variant + '|' + lod;
    let m = CACHE.get(key);
    if (!m) {
      m = build(mk, cultureKey(culture), tier, variant, lod, city);
      if (lod === 'mid') {
        // 没有垛口与大屋顶的聚落（营地、村落……）中景省不到一成：直接用近景那两份
        const near = model(culture, capital, city, Object.assign({}, opts, { lod: 'near' }));
        if (near.ink && m.ink && m.tris > near.tris * 0.9) { m.ink = near.ink; m.plain = near.plain; m.tris = near.tris; }
      }
      m.key = key; CACHE.set(key, m);
    }
    return m;
  }

  // ============================================================== 写入 MeshBuilder --
  const _m = new THREE.Matrix4(), _n = new THREE.Matrix3(), _v = new THREE.Vector3();
  /** 把单位尺寸的 LP 几何体按 size 写进 SG.LP.MeshBuilder（它当前的变换下）；plain 部分不描墨线 */
  function appendLP(M, g, size, plain) {
    if (!g) return;
    // 基础库若提供公开的 LP.MeshBuilder#appendGeometry(geo, { matrix, ink: 0 | 1 | 'attr' })（在当前变换下复制一份 LP 几何体），
    // 优先用它；下面直接写 MeshBuilder 内部数组、自己解码自发光的做法只是它缺席时的后备
    if (typeof M.appendGeometry === 'function') { M.appendGeometry(g, { matrix: _m.makeScale(size, size, size), ink: plain ? 0 : 'attr' }); return; }
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
  // map-view 先调 cityInto（带 city）再调 cityFarInto（不带 city）：远景缺 city 时沿用紧挨着的那次近景调用的城（同文化、同档、同尺寸），
  // 这样库施（麦罗埃）/ 阿克苏姆、博斯普鲁斯希腊城 / 车营的远景与近景一致
  let lastNear = null;
  function cityInto(mb, size, culture, capital, city) {
    if (!ready()) return fallbackInto(mb, size);
    lastNear = city ? { city, size, culture, capital: !!capital } : null;
    return writeInto(mb, model(culture, !!capital, city || null, { size }), size);
  }
  function cityFarInto(mb, size, culture, capital, city) {
    if (!ready()) return fallbackInto(mb, size);
    if (!city && lastNear && lastNear.size === size && lastNear.culture === culture && lastNear.capital === !!capital) city = lastNear.city;
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
    // 几何体是缓存共用的：标记 sharedGeometry（SG.Gfx.disposeTree 等跳过），只由 CultureArt.clearCache 释放
    g.traverse(c => { if (c.isMesh) c.userData.sharedGeometry = true; });
    g.scale.setScalar(size);
    // userData.lp.lamps：位置为模型坐标（随 obj 的世界矩阵变换），range / haloSize 已按 size 缩放（LPProps.addLamps 用）
    g.userData.lp = { name: m.key, lamps: m.lamps.map(l => Object.assign({}, l, { range: l.range * size, haloSize: l.haloSize * size })), anchors: {}, tris: m.tris, size: [2.3 * size, m.height * size, 2.3 * size] };
    g.userData.tris = m.tris;
    return g;
  }

  /**
   * 战略地图用：把一批城池放进一个 LP.Scatter —— 同一模型键（文化 × 档次 × 变体）的城共用几何体、实例化绘制（墨线同样实例化），
   * 按块（chunk）切换远近；同时给出世界坐标的灯位（可逐个 SG.Gfx.addLamp / mood.addLamp）。
   * 距离带（镜头到区块）：
   *   < midDist            近景 + 墨线（inkDist 以内）
   *   midDist – lodDist    中景：同一布局、同一配色，去掉垛口 / 墙缝 / 灯笼 / 屋脊饰 / 多余的树冠团（约为近景的 55–70%），不描墨线
   *                        ——城在屏幕上只有几十像素时，这些细节看不出，墨线（固定像素宽）还会把垛口、瓦檐涂成一团黑
   *   > lodDist            远景（≤ ~400 三角形）
   * 中景是在同一个实例化网格上换几何体（近景 ↔ 中景，墨线子网格同换），返回的 scatter.chunks 仍是 { near, far, isFar } 的结构。
   * list = [{ x, y, z（three 坐标，城心地面）, yaw（弧度）, size, culture, capital, city }]；opts = { chunk: 60, lodDist: 90,
   * inkDist: 45（0 = 近景一律描墨线）, midDist（缺省 = inkDist；0 = 不用中景）, name } → { group, scatter, lamps, update(camera) }
   * （每帧或镜头移动后调 update）
   */
  function scatter(list, opts) {
    opts = opts || {};
    if (!ready()) return { group: new THREE.Group(), scatter: null, lamps: [], update() {} };
    const inkDist = opts.inkDist != null ? +opts.inkDist : 45, lodDist = opts.lodDist || 90;
    const midDist = Math.min(opts.midDist != null ? +opts.midDist : inkDist > 0 ? inkDist : lodDist * 0.6, lodDist * 0.95);
    const sc = new LP.Scatter({ chunk: opts.chunk || 60, lodDist, name: opts.name || 'Cities' });
    const lamps = [], midOf = new Map();
    for (const it of list) {
      const cap = !!it.capital, city = it.city || null, s = it.size || 1, yaw = it.yaw || 0;
      const m = model(it.culture, cap, city, { size: s }), f = model(it.culture, cap, city, { size: s, lod: 'far' });
      if (midDist > 0 && m.ink && !midOf.has(m.ink)) {
        const md = model(it.culture, cap, city, { size: s, lod: 'mid' });
        midOf.set(m.ink, md.ink && md.ink !== m.ink ? md.ink : null);
        if (m.plain) midOf.set(m.plain, md.plain && md.plain !== m.plain ? md.plain : null);
      }
      if (m.ink) sc.add({ near: m.ink, far: f.ink || undefined }, it.x, it.y, it.z, yaw, s, null, { castShadow: true });
      if (m.plain) sc.add({ near: m.plain, far: f.plain || undefined }, it.x, it.y, it.z, yaw, s, null, { outline: false, castShadow: false });
      const c = Math.cos(yaw), sn = Math.sin(yaw);
      for (const l of m.lamps) {
        const lx = l.pos[0] * s, ly = l.pos[1] * s, lz = l.pos[2] * s;
        lamps.push(Object.assign({}, l, { pos: [it.x + lx * c + lz * sn, it.y + ly, it.z - lx * sn + lz * c], range: l.range * s, haloSize: l.haloSize * s }));
      }
    }
    const group = sc.build();
    // 墨线部分与不描墨线的部分（中景的贴面较少）都按距离换几何体
    for (const c of sc.chunks) { const mg = midOf.get(c.near.geometry); if (mg) { c.nearGeo = c.near.geometry; c.midGeo = mg; } }
    const _c = new THREE.Vector3(), _p = new THREE.Vector3();
    function update(cam) {
      sc.update(cam);
      if (!cam || !sc.chunks) return;
      cam.getWorldPosition(_p);
      for (const c of sc.chunks) {
        const out = c.near && c.near.userData.outline;
        if (!c.midGeo && !(inkDist > 0 && out)) continue;
        _c.copy(c.center).applyMatrix4(group.matrixWorld);
        const d = Math.max(0, _c.distanceTo(_p) - c.radius * 0.5);
        if (c.midGeo) {
          const mid = c.isMid ? d > midDist * 0.92 : d > midDist * 1.08;
          if (mid !== !!c.isMid) { c.isMid = mid; const g = mid ? c.midGeo : c.nearGeo; c.near.geometry = g; if (out) out.geometry = g; }
        }
        if (inkDist > 0 && out) {
          const on = c.inkOn !== false ? d < inkDist * 1.08 : d < inkDist * 0.92;
          if (on !== (c.inkOn !== false)) { c.inkOn = on; out.visible = on; }
        }
      }
    }
    return { group, scatter: sc, lamps, update };
  }

  SG.CultureArt = {
    scatter,
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
    clearCache() { const seen = new Set(); for (const m of CACHE.values()) for (const g of [m.ink, m.plain]) if (g && !seen.has(g)) { seen.add(g); g.dispose(); } CACHE.clear(); },
  };
})();
