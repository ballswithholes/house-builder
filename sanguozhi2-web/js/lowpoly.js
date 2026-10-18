'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 低多边形风格工具包（SG.LP）1.0 —— Lanternvale 风格的生产版

   画风：与《Lanternvale》一致的风格化低多边形 —— 温暖、吉卜力式的配色，粗壮易读的剪影，圆形物体 6–12 边，岩石 / 木头 /
   建筑 / 道具平面（分面）着色，树冠是平滑的「软团」（抖动二十面体 + 精确椭球法线 + 全树冠的上暖下青色阶），顶点色（不画贴图），
   每面亮度抖动 0.04–0.08、近地面 AO ~0.3，角色 / 生物 / 道具描墨线（地形、草、贴花、粒子不描），柔和的绘画式光照：
   黄金时刻提亮，夜晚冷蓝灰的夜色调与琥珀色灯池，远景雾，柔和圆影，树叶 / 布 / 头发随风轻摆。人物是真实成人比例（≈ 7.5 头身）。
   预算：人物 ≤ ~3.1k 三角形，大建筑 ≤ ~3.2k，树 ≤ 1.5k，小道具 ≤ 400，草丛 ≤ 60。

   本文件移植 Lanternvale 的 MeshBuilder / Materials3D / SceneLighting / DayNight 与 LowPoly、Outline、Shadow 着色器到 three.js r158。
   · 坐标：Y 向上、右手系，1 单位 = 1 米。人物 / 生物面朝 +Z（右手在 −X，与 figure.js 一致）；道具正面朝 +Z（本游戏默认镜头从 +Z 看向 −Z）。
   · 色彩：游戏是 Gamma 工作流（ColorManagement 关闭、渲染器线性输出、无色调映射）。LP 着色器把顶点色 / 雾色按 sRGB 解码，在线性空间
     算光照，再自己编码回 sRGB（LP.setOutputEncoded(true)，SG.Gfx 已设好）。灯光颜色视为「线性」：用 LP.Mood 设置时自动转换；
     自建 three 灯光时用 LP.lightColor('#hex')。
   · 灯光：读取 three 的 DirectionalLight（太阳 / 月亮，第 0 盏可投影）、HemisphereLight（天空 / 地面）、PointLight（灯笼 / 火）的 uniform，
     按 Lanternvale 的模型计算：包裹式太阳漫反射、半球环境光、点光柔和衰减、rim、自发光、夜色调、暖色、线性雾（带上限）。
     场景的 LP.Mood 在该场景渲染前推送它的全局量、渲染后复原（两个场景——战略地图与单挑画面——互不串味）。

   接口（其他模块依赖的公开 API，1.0 起保持稳定）
   --------------------------------------------------------------------------
   建模  const M = new SG.LP.MeshBuilder(seed)
     变换栈：push() / pop() / translate(x,y,z | [x,y,z]) / rotate(xDeg,yDeg,zDeg)（Unity 欧拉顺序 Z→X→Y）/ rotateQ(q) / scale(s | x,y,z) /
             aim(at, dir)（+Y 指向 dir 的新坐标系，记得 pop）/ setMatrix(m4)。镜像（负缩放）安全。
     状态：  color = [r,g,b]（sRGB 0..1，SG.LP.Paint.hex('#..')）· emission 0..1（常亮自发光：火、晶石）· nightEmission 0..1（只在夜里亮：
             窗纸、灯笼纸——白天不亮，由场景氛围点亮；优先于 emission）· wind（风摆权重）+ windGradient / windY0 / windY1 ·
             bone（刚性蒙皮骨骼）· ink（墨线宽度系数，0 = 不描）· jitter（每面亮度抖动）· jitterAll（显式颜色也抖动）· aoStrength / aoHeight
     基本体：box(center, size, topColor?, faces?) · boxOn · taperedBox · roof · lathe(profile, sides, smooth?, capB?, capT?, ringColors?, angleDeg?) ·
             cylinder · cone · sphere · capsule · blob · facetBlob(…, colorOf(normal)) · segment(a, b, ra, rb, sides?, smooth?, caps?) · disc · plane ·
             torus · extrude(poly[[x,z]..], y0, y1) · blade(base, tip, width, side?) · triangle · triangleFacing · quad · quadColors · quadTwoSided · append
     辅助：  shell · panel · band · body · flat(pts[[x,y]..], thick) · strip · curvedStrip · spike · curve · loft(rings, capS?, capE?, colorOf?) · fan ·
             obox · beam(a, b, w, h, up?) · rope · tuft · paperLantern / ribLantern(c, r, h, paper, lit, cap, tassel?, sides?, dayGlow?)（lit: true | false | 'night'）
     逐顶点：boneBy(start, p => bone) · windBy(start, p => w)
     树叶：  softLump(center, radii, detail, yawDeg, ramp, lumpiness?)；SG.LP.foliage(bucket, base, hueStep?, tune?)、SG.LP.canopyRamp(top, side, bottom, y0, y1, tune?)、
             SG.LP.FOLIAGE_TUNE、SG.LP.byNormal(top, side, bottom, from?)
     输出：  M.toGeometry({ skinned?, compact? }) → 非索引 BufferGeometry：position、normal、color（vec4：sRGB 反照率 + alpha 里的自发光编码 LP.EMIT）、
             lpWind、lpSmooth（平滑法线，长度 = 墨线宽度系数）[、skinIndex / skinWeight]；compact（默认）= 法线 / lpSmooth 用 Int8、颜色用 Uint8
             （每顶点 26 字节，原来 56）。geometry.userData.tris。M.triangleCount / vertexCount / random01() / range(a, b)
     SG.LP.Paint（hex / shade / mix / hsv / lin / luma）· SG.LP.PAL（共用调色板）· SG.LP.MeshCache.get(key, build) · SG.LP.hash01 · SG.LP.V（vec3）
   材质（共用实例，按选项缓存；不要改共用材质，逐物体外观用 look，逐实例颜色用 instanceColor）
     SG.LP.material({ side, tint, rim, fogScale, windScale, flash, lit, detail + planar + detailK, map, alpha + opacity, depthWrite, polygonOffset })
     SG.LP.outlineMaterial({ color, width (1080p 下的像素，默认 2.2), windScale, fogScale })   SG.LP.INK / INK_GOLD
     SG.LP.setLook(obj, look | null) → 给整棵子树（含墨线）设外观：{ tint, flash 0..1, flashColor, fade 0..1（抖动溶解；1 = 实）, rim,
             fogScale, windScale, lit（夜窗：true / false / 0..1）, opacity, ink（墨线色，如 SG.LP.INK_GOLD 表示选中）, inkWidth（倍数） }；
             同一个 look 对象可随时改字段（绘制时读取）。SG.LP.getLook(obj)
   网格  SG.LP.mesh(geo, opts?) → Mesh（opts.outline 默认 true：子网格 = 墨线外壳；castShadow / receiveShadow / look）
         SG.LP.skinnedMesh(geo, skeleton, opts?) → SkinnedMesh（绑定已有骨架；墨线同样蒙皮）
         SG.LP.lod(near, far, distance) → THREE.LOD · SG.LP.trisOf(obj)
   实例化 SG.LP.instances(geo, capacity, opts) → InstancedMesh（+ 共享 instanceMatrix 的实例化墨线子网格；opts.tints = 逐实例颜色）
         SG.LP.setInstance(im, i, x, y, z, yaw, scale, tint?) · setInstanceMatrix(im, i, m4, tint?) · commitInstances(im, count?)
         new SG.LP.Scatter({ chunk, lodDist }) .add(geo | { near, far }, x, y, z, yaw, scale, tint?, { outline, farOutline, farShadow, castShadow })
             .build() → Group（每种几何体每块一个实例化网格 + 一个实例化墨线）.update(camera)（按块切换近 / 远景）
   合并  new SG.LP.StaticBatch({ chunk }) .add(geo, m4, { outline, castShadow, tint }) / .addAt(geo, x, y, z, yaw, scale) / .addObject(obj) .build()
         SG.LP.mergeGeometries([{ geo, matrix, tint }]) → 一个紧凑几何体（独一无二的静态道具：城中建筑；重复的东西用 Scatter）
   影子  SG.LP.blobShadow(rx, rz, strength?, softness?) → Mesh；new SG.LP.BlobShadows(capacity) .set(i, x, y, z, rx, rz, strength) .commit(n) .mesh（一次绘制）
   贴花  SG.LP.decal(texture, w, h) → 竖立的牌匾 / 招牌 · SG.LP.groundDecal(texture, w, d, { color, opacity, y, rotation }) → 贴地（半透明，受光）
         SG.LP.decalTexture('dirt' | 'path' | 'scorch' | 'puddle' | 'leaves' | 'flowers' | 'moss' | 'snow' | 'sand' | 'mud' | 'shadow', seed?)
         SG.LP.boardTexture(text, { w, h, board, ink, line, vertical }) · SG.LP.grassDetail()（地面的笔触细节）
   光晕  SG.LP.halo(color, size) → Sprite · new SG.LP.HaloBatch(capacity)（Mood 用它把所有灯的光晕画成一次）
   全局  SG.LP.globals · setTime(s)（风摆时钟，SG.Gfx.render 每帧设）· setWind(strength, speed) · setViewport(w, h, refH = 1080, minPx)（SG.Gfx.resize 设）·
         setOutputEncoded(bool) · setShadowStrength(k) · lightColor(hex) · resetGlobals()
   氛围  const mood = new SG.LP.Mood(scene, { center, extent, shadow, shadowSize, shadowStrength, sun, hemi（接管已有的灯）, legacy（与原有材质共用灯时：
             正午保持它们的原样）, baseDir（正午太阳方向）, fog: { base, golden, dusk, night, background }, fogMax, maxLamps: 8 })
         mood.set('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | 小时数) → mood；mood.state = { hour, night, golden, dir, … }
         mood.addLamp([x,y,z], { color, intensity, range, nightOnly, halo, haloSize, flicker }) → lamp · setLampLit · moveLamp · removeLamp
             所有灯共享 maxLamps 盏点光源（离镜头 / mood.focus 最近的亮灯优先），加灯不会重编译着色器；每盏灯都有光晕
         mood.apply() / mood.update(camera)（场景钩子自动调用）· mood.dispose()
         主场景的氛围：SG.Gfx.mood（见 js/art.js：SG.Gfx.setMood / addLamp / newMood）
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const LP = (SG.LP = SG.LP || {});
  const D2R = Math.PI / 180;
  const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const sstep = t => { t = clamp01(t); return t * t * (3 - 2 * t); };

  // ------------------------------------------------------------- vec3 --
  const V = {
    add: (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]],
    sub: (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]],
    mul: (a, k) => [a[0] * k, a[1] * k, a[2] * k],
    madd: (a, b, k) => [a[0] + b[0] * k, a[1] + b[1] * k, a[2] + b[2] * k],
    dot: (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2],
    cross: (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]],
    len: a => Math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]),
    norm: a => { const l = Math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]); return l > 1e-12 ? [a[0] / l, a[1] / l, a[2] / l] : [0, 1, 0]; },
    lerp: (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t],
    scale3: (a, s) => [a[0] * s[0], a[1] * s[1], a[2] * s[2]],
    neg: a => [-a[0], -a[1], -a[2]],
  };
  LP.V = V;
  const UP = [0, 1, 0], DOWN = [0, -1, 0], FWD = [0, 0, 1], BACK = [0, 0, -1], RIGHT = [1, 0, 0], LEFT = [-1, 0, 0];

  // ------------------------------------------------------------ Paint --
  const Paint = {
    hex(h) {
      h = String(h).replace('#', '');
      if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
      const n = parseInt(h, 16);
      return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
    },
    shade: (c, k) => [clamp01(c[0] * k), clamp01(c[1] * k), clamp01(c[2] * k)],
    mix: (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)],
    hsv(c, hueShiftDeg, sat = 1, val = 1) {
      const r = c[0], g = c[1], b = c[2];
      const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
      let h = 0;
      if (d > 1e-9) {
        if (mx === r) h = ((g - b) / d) % 6; else if (mx === g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
        h /= 6; if (h < 0) h += 1;
      }
      let s = mx > 1e-9 ? d / mx : 0, v = mx;
      h = (((h + hueShiftDeg / 360) % 1) + 1) % 1;
      s = clamp01(s * sat); v = clamp01(v * val);
      const i = Math.floor(h * 6), f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
      switch (i % 6) { case 0: return [v, t, p]; case 1: return [q, v, p]; case 2: return [p, v, t]; case 3: return [p, q, v]; case 4: return [t, p, v]; default: return [v, p, q]; }
    },
    lin: c => c.map(v => (v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4))),
    luma: c => c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11,
  };
  LP.Paint = Paint;
  const P = Paint;

  // ------------------------------------------------------------- hash --
  function hash01(d, seed) {
    const x = Math.round(d[0] * 1000) | 0, y = Math.round(d[1] * 1000) | 0, z = Math.round(d[2] * 1000) | 0;
    let h = (Math.imul(x, 73856093) ^ Math.imul(y, 19349663) ^ Math.imul(z, 83492791) ^ Math.imul(seed | 0, 2654435761 | 0)) >>> 0;
    h ^= h >>> 13; h = Math.imul(h, 0x5bd1e995) >>> 0; h ^= h >>> 15;
    return (h & 0xffff) / 65535;
  }
  LP.hash01 = hash01;

  // ------------------------------------------------------- icosphere --
  const ICO_F = [0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1];
  const ICO_CACHE = new Map();
  function ico(detail) {
    detail = Math.max(0, Math.min(3, detail | 0));
    if (ICO_CACHE.has(detail)) return ICO_CACHE.get(detail);
    const t = (1 + Math.sqrt(5)) / 2;
    const v = [[-1, t, 0], [1, t, 0], [-1, -t, 0], [1, -t, 0], [0, -1, t], [0, 1, t], [0, -1, -t], [0, 1, -t], [t, 0, -1], [t, 0, 1], [-t, 0, -1], [-t, 0, 1]].map(V.norm);
    let faces = [];
    for (let i = 0; i < ICO_F.length; i += 3) faces.push([v[ICO_F[i]], v[ICO_F[i + 1]], v[ICO_F[i + 2]]]);
    for (let d = 0; d < detail; d++) {
      const next = [];
      for (const f of faces) {
        const ab = V.norm(V.mul(V.add(f[0], f[1]), 0.5)), bc = V.norm(V.mul(V.add(f[1], f[2]), 0.5)), ca = V.norm(V.mul(V.add(f[2], f[0]), 0.5));
        next.push([f[0], ab, ca], [f[1], bc, ab], [f[2], ca, bc], [ab, bc, ca]);
      }
      faces = next;
    }
    ICO_CACHE.set(detail, faces);
    return faces;
  }
  LP.ico = ico;

  /**
   * Emission code in the vertex colour's alpha (LP.EMIT): always-on emission e → a = 0.498·e (0 … 0.498); night-only
   * emission e (MeshBuilder.nightEmission: windows, lantern paper) → a = 0.502 + 0.498·e. The shader decodes both; the
   * scene's mood (or a look's `lit`) scales the night-only kind from 0 by day to 1 at night.
   */
  const EMIT = { day: 0.498, night0: 0.502 };
  function encodeEmission(e) { return e < 0 ? EMIT.night0 + EMIT.day * clamp01(-e) : EMIT.day * clamp01(e); }
  LP.EMIT = EMIT;
  LP.encodeEmission = encodeEmission;

  /** Underside squash of soft foliage lumps (unit-sphere y below 0 is multiplied by this). */
  const SOFT_UNDER = 0.8;

  // ====================================================== MeshBuilder ==
  const _v = new THREE.Vector3(), _e = new THREE.Euler(), _q = new THREE.Quaternion(), _m = new THREE.Matrix4();

  class MeshBuilder {
    constructor(seed = 1) {
      this.P = []; this.N = []; this.C = []; this.E = []; this.W = []; this.B = []; this.T = []; this.K = [];
      this.stack = [];
      this.m = new THREE.Matrix4();
      this.nm = new THREE.Matrix3();
      this.mirrored = false;
      this.rng = ((((Math.imul(seed | 0, 2654435761 | 0) >>> 0) + 12345) >>> 0) | 1) >>> 0;
      // state for the next primitives
      this.color = [1, 1, 1];
      this.emission = 0;
      // nightEmission > 0: the next vertices glow only at night (windows, lantern paper): the scene's mood scales them from
      // 0 by day to 1 at night (LP.Mood → lpNight), or per object with a look { lit: true | false }; it takes precedence
      // over emission. Stored in this.E as a negative number (−e); toGeometry encodes both kinds in the colour's alpha.
      this.nightEmission = 0;
      this.wind = 0;
      this.windGradient = false;
      this.windY0 = 0; this.windY1 = 1;
      this.bone = 0;
      // ink: width factor of the ink outline on the next vertices (1 = full; small face features 0.3–0.7; 0 = none)
      this.ink = 1;
      this.jitter = 0;
      // jitterAll: per-face jitter also on explicitly coloured faces (quad / triangle with a colour argument) — buildings
      // pass a colour per course / tile / block, and Lanternvale's walls and roofs are never one exact tone
      this.jitterAll = false;
      this.aoStrength = 0; this.aoHeight = 0.5;
      this.softParts = [];
    }
    get vertexCount() { return this.P.length / 3; }
    get triangleCount() { return this.T.length / 3; }
    get isEmpty() { return this.T.length === 0; }

    // ------------------------------------------------ transform stack
    setMatrix(m) { this.m.copy(m); this.nm.getNormalMatrix(this.m); this.mirrored = this.m.determinant() < 0; return this; }
    get matrix() { return this.m; }
    push() { this.stack.push(this.m.clone()); return this; }
    pop() { this.setMatrix(this.stack.length ? this.stack.pop() : _m.identity()); return this; }
    translate(x, y, z) { if (Array.isArray(x)) { z = x[2]; y = x[1]; x = x[0]; } return this.setMatrix(this.m.clone().multiply(_m.makeTranslation(x, y, z))); }
    /** Degrees; Unity's Quaternion.Euler order (z, then x, then y). */
    rotate(x, y, z) { if (Array.isArray(x)) { z = x[2]; y = x[1]; x = x[0]; } _e.set(x * D2R, y * D2R, z * D2R, 'YXZ'); return this.setMatrix(this.m.clone().multiply(_m.makeRotationFromEuler(_e))); }
    rotateQ(q) { return this.setMatrix(this.m.clone().multiply(_m.makeRotationFromQuaternion(q))); }
    scale(x, y, z) { if (Array.isArray(x)) { z = x[2]; y = x[1]; x = x[0]; } if (y === undefined) { y = x; z = x; } return this.setMatrix(this.m.clone().multiply(_m.makeScale(x, y, z))); }
    /** Starts a frame at `at` whose +Y points along `dir` (pop when done). */
    aim(at, dir) {
      this.push().translate(at);
      const l = V.len(dir);
      if (l > 1e-8) { _v.set(dir[0] / l, dir[1] / l, dir[2] / l); _q.setFromUnitVectors(new THREE.Vector3(0, 1, 0), _v); this.rotateQ(_q); }
      return this;
    }

    random01() {
      let r = this.rng;
      r ^= r << 13; r >>>= 0; r ^= r >>> 17; r ^= r << 5; r >>>= 0;
      this.rng = r;
      return (r & 0xffffff) / 16777216;
    }
    range(a, b) { return a + (b - a) * this.random01(); }

    // ------------------------------------------------ low level
    faceColor() {
      const c = this.color;
      if (this.jitter <= 0) return c;
      const k = 1 + (this.random01() * 2 - 1) * this.jitter;
      return [c[0] * k, c[1] * k, c[2] * k];
    }
    /** An explicit face colour, jittered when jitterAll is on. */
    jitterOf(c) {
      if (!this.jitterAll || this.jitter <= 0) return c;
      const k = 1 + (this.random01() * 2 - 1) * this.jitter;
      return [c[0] * k, c[1] * k, c[2] * k];
    }
    addVertex(local, localNormal, c) {
      const e = this.m.elements, x = local[0], y = local[1], z = local[2];
      const px = e[0] * x + e[4] * y + e[8] * z + e[12], py = e[1] * x + e[5] * y + e[9] * z + e[13], pz = e[2] * x + e[6] * y + e[10] * z + e[14];
      const n = this.nm.elements, a = localNormal[0], b = localNormal[1], d = localNormal[2];
      let nx = n[0] * a + n[3] * b + n[6] * d, ny = n[1] * a + n[4] * b + n[7] * d, nz = n[2] * a + n[5] * b + n[8] * d;
      const l = Math.sqrt(nx * nx + ny * ny + nz * nz);
      if (l > 1e-12) { nx /= l; ny /= l; nz /= l; } else { nx = 0; ny = 1; nz = 0; }
      let r = c[0], g = c[1], bb = c[2];
      if (this.aoStrength > 0 && this.aoHeight > 0) {
        const k = lerp(1 - this.aoStrength, 1, clamp01(py / this.aoHeight));
        r *= k; g *= k; bb *= k;
      }
      let w = this.wind;
      if (this.windGradient) {
        let span = this.windY1 - this.windY0;
        if (Math.abs(span) < 1e-3) span = 1e-3;
        w = this.wind * clamp01((py - this.windY0) / span);
      }
      this.P.push(px, py, pz);
      this.N.push(nx, ny, nz);
      this.C.push(clamp01(r), clamp01(g), clamp01(bb));
      this.E.push(this.nightEmission > 0 ? -clamp01(this.nightEmission) : clamp01(this.emission));
      this.W.push(w);
      this.B.push(this.bone | 0);
      this.K.push(this.ink);
      return this.P.length / 3 - 1;
    }
    addTri(i0, i1, i2) {
      if (this.mirrored) this.T.push(i0, i2, i1); else this.T.push(i0, i1, i2);
    }
    /** Flat triangle, counter-clockwise seen from its front (three.js); the normal is cross(b − a, c − a). */
    triangle(a, b, c, col) {
      col = col ? this.jitterOf(col) : this.faceColor();
      const n = V.cross(V.sub(b, a), V.sub(c, a));
      const i0 = this.addVertex(a, n, col), i1 = this.addVertex(b, n, col), i2 = this.addVertex(c, n, col);
      this.addTri(i0, i1, i2);
    }
    /** Flat triangle facing `outward` (the winding is fixed to match). */
    triangleFacing(a, b, c, outward, col) {
      if (V.dot(V.cross(V.sub(b, a), V.sub(c, a)), outward) < 0) this.triangle(a, c, b, col); else this.triangle(a, b, c, col);
    }
    /** Flat quad a-b-c-d (a convex loop), facing `outward`. */
    quad(a, b, c, d, outward, col) {
      // the normal from the diagonals stays right when two corners meet (a quad that closes into a triangle at a pole)
      let n = V.cross(V.sub(c, a), V.sub(d, b));
      if (V.dot(n, n) < 1e-16) n = V.cross(V.sub(b, a), V.sub(c, a));
      if (V.dot(n, outward) < 0) { const t = b; b = d; d = t; n = V.neg(n); }
      col = col ? this.jitterOf(col) : this.faceColor();
      const i0 = this.addVertex(a, n, col), i1 = this.addVertex(b, n, col), i2 = this.addVertex(c, n, col), i3 = this.addVertex(d, n, col);
      this.addTri(i0, i1, i2);
      this.addTri(i0, i2, i3);
    }
    /**
     * Flat quad with a colour per corner (cols[0..3] for a, b, c, d): soft painted gradients — a damp streak fading into
     * the wall, a course darkening towards its foot. Facing `outward`; no jitter.
     */
    quadColors(a, b, c, d, outward, cols) {
      let n = V.cross(V.sub(c, a), V.sub(d, b));
      if (V.dot(n, n) < 1e-16) n = V.cross(V.sub(b, a), V.sub(c, a));
      let ca = cols[0], cb = cols[1], cc = cols[2], cd = cols[3];
      if (V.dot(n, outward) < 0) { let t = b; b = d; d = t; t = cb; cb = cd; cd = t; n = V.neg(n); }
      const i0 = this.addVertex(a, n, ca), i1 = this.addVertex(b, n, cb), i2 = this.addVertex(c, n, cc), i3 = this.addVertex(d, n, cd);
      this.addTri(i0, i1, i2);
      this.addTri(i0, i2, i3);
    }
    /** Both sides of a flat quad (thin cloth, leaves, paper, banners). */
    quadTwoSided(a, b, c, d) {
      const n = V.cross(V.sub(b, a), V.sub(c, a));
      this.quad(a, b, c, d, n);
      this.quad(a, b, c, d, V.neg(n));
    }

    // ------------------------------------------------ primitives
    /**
     * Axis-aligned box (in the current transform). topColor tints the top face only. faces (optional) lists the faces to
     * build — 't' top, 'b' bottom, 'f' front (+Z), 'k' back (−Z), 'r' right (+X), 'l' left (−X) — to skip faces that are
     * never seen (the bottom of a block standing on the ground, the back of a panel against a wall).
     */
    box(center, size, topColor, faces) {
      const h = V.mul(size, 0.5), c = center, F = faces || 'tbfkrl';
      const Pt = (x, y, z) => [c[0] + x * h[0], c[1] + y * h[1], c[2] + z * h[2]];
      const keep = this.color;
      if (topColor) this.color = topColor;
      if (F.includes('t')) this.quad(Pt(-1, 1, -1), Pt(-1, 1, 1), Pt(1, 1, 1), Pt(1, 1, -1), UP);
      this.color = keep;
      if (F.includes('b')) this.quad(Pt(-1, -1, -1), Pt(1, -1, -1), Pt(1, -1, 1), Pt(-1, -1, 1), DOWN);
      if (F.includes('f')) this.quad(Pt(-1, -1, 1), Pt(1, -1, 1), Pt(1, 1, 1), Pt(-1, 1, 1), FWD);
      if (F.includes('k')) this.quad(Pt(-1, -1, -1), Pt(-1, 1, -1), Pt(1, 1, -1), Pt(1, -1, -1), BACK);
      if (F.includes('r')) this.quad(Pt(1, -1, -1), Pt(1, 1, -1), Pt(1, 1, 1), Pt(1, -1, 1), RIGHT);
      if (F.includes('l')) this.quad(Pt(-1, -1, -1), Pt(-1, -1, 1), Pt(-1, 1, 1), Pt(-1, 1, -1), LEFT);
    }
    boxOn(bottom, size, topColor) { this.box([bottom[0], bottom[1] + size[1] * 0.5, bottom[2]], size, topColor); }
    /** Box whose top is narrowed (taper 0 = box … 1 = pyramid). */
    taperedBox(bottom, size, taperX, taperZ) {
      if (taperZ === undefined || taperZ < 0) taperZ = taperX;
      const bx = size[0] * 0.5, bz = size[2] * 0.5, tx = bx * (1 - taperX), tz = bz * (1 - taperZ), y = size[1];
      const o = bottom, A = (x, yy, z) => [o[0] + x, o[1] + yy, o[2] + z];
      const b0 = A(-bx, 0, -bz), b1 = A(bx, 0, -bz), b2 = A(bx, 0, bz), b3 = A(-bx, 0, bz);
      const t0 = A(-tx, y, -tz), t1 = A(tx, y, -tz), t2 = A(tx, y, tz), t3 = A(-tx, y, tz);
      this.quad(t0, t3, t2, t1, UP);
      this.quad(b0, b1, b2, b3, DOWN);
      this.quad(b0, b1, t1, t0, BACK);
      this.quad(b1, b2, t2, t1, RIGHT);
      this.quad(b2, b3, t3, t2, FWD);
      this.quad(b3, b0, t0, t3, LEFT);
    }
    /** Gable roof over a width (X) × depth (Z) wall top at `bottom`, ridge `height` above it along X. */
    roof(bottom, width, depth, height, overhang = 0.15, thickness = 0.08, gables = true) {
      const gx = width * 0.5, gz = depth * 0.5, hx = gx + overhang, hz = gz + overhang;
      const drop = height * overhang / Math.max(0.01, gz);
      const R = x => [bottom[0] + x, bottom[1] + height, bottom[2]];
      const E = (x, s) => [bottom[0] + x, bottom[1] - drop, bottom[2] + s * hz];
      const t = [0, -thickness, 0], T = p => V.add(p, t);
      this.quad(E(-hx, -1), R(-hx), R(hx), E(hx, -1), [0, 1, -1]);
      this.quad(E(-hx, 1), E(hx, 1), R(hx), R(-hx), [0, 1, 1]);
      this.quad(T(E(-hx, -1)), T(E(hx, -1)), T(R(hx)), T(R(-hx)), [0, -1, 1]);
      this.quad(T(E(-hx, 1)), T(R(-hx)), T(R(hx)), T(E(hx, 1)), [0, -1, -1]);
      this.quad(E(-hx, -1), E(hx, -1), T(E(hx, -1)), T(E(-hx, -1)), BACK);
      this.quad(E(-hx, 1), T(E(-hx, 1)), T(E(hx, 1)), E(hx, 1), FWD);
      this.quad(E(-hx, -1), T(E(-hx, -1)), T(R(-hx)), R(-hx), LEFT);
      this.quad(E(-hx, 1), R(-hx), T(R(-hx)), T(E(-hx, 1)), LEFT);
      this.quad(E(hx, -1), R(hx), T(R(hx)), T(E(hx, -1)), RIGHT);
      this.quad(E(hx, 1), T(E(hx, 1)), T(R(hx)), R(hx), RIGHT);
      if (!gables) return;
      const apex = height - thickness, b = bottom;
      this.triangleFacing([b[0] - gx, b[1], b[2] - gz], [b[0] - gx, b[1] + apex, b[2]], [b[0] - gx, b[1], b[2] + gz], LEFT);
      this.triangleFacing([b[0] + gx, b[1], b[2] - gz], [b[0] + gx, b[1] + apex, b[2]], [b[0] + gx, b[1], b[2] + gz], RIGHT);
    }
    /**
     * Surface of revolution about local Y. profile = [[radius, y], …] bottom → top. smooth = shared normals;
     * flat = faceted. ringColors: one colour per profile point.
     */
    lathe(profile, sides, smooth = false, capBottom = true, capTop = true, ringColors = null, angleOffsetDeg = 0) {
      if (!profile || profile.length < 2) return;
      sides = Math.max(3, sides | 0);
      const a0 = angleOffsetDeg * D2R, n = profile.length;
      const Ring = (i, j) => { const a = a0 + j * Math.PI * 2 / sides, p = profile[i]; return [Math.cos(a) * p[0], p[1], Math.sin(a) * p[0]]; };
      const RingCol = i => (ringColors && i < ringColors.length ? ringColors[i] : this.color);
      if (smooth) {
        const pn = [];
        for (let i = 0; i < n; i++) {
          let tx = 0, ty = 0;
          if (i > 0) { const dx = profile[i][0] - profile[i - 1][0], dy = profile[i][1] - profile[i - 1][1], l = Math.hypot(dx, dy) || 1; tx += dx / l; ty += dy / l; }
          if (i < n - 1) { const dx = profile[i + 1][0] - profile[i][0], dy = profile[i + 1][1] - profile[i][1], l = Math.hypot(dx, dy) || 1; tx += dx / l; ty += dy / l; }
          const l = Math.hypot(tx, ty);
          pn.push(l > 1e-8 ? [ty / l, -tx / l] : [1, 0]);
        }
        const idx = [];
        for (let i = 0; i < n; i++) {
          const c = RingCol(i), row = [];
          for (let j = 0; j <= sides; j++) {
            const a = a0 + j * Math.PI * 2 / sides;
            row.push(this.addVertex(Ring(i, j % sides), [Math.cos(a) * pn[i][0], pn[i][1], Math.sin(a) * pn[i][0]], c));
          }
          idx.push(row);
        }
        // a ring of radius 0 (a pole) makes one triangle of each quad zero-area: skip those (spheres carried 2 × sides of
        // them for nothing)
        for (let i = 0; i < n - 1; i++)
          for (let j = 0; j < sides; j++) {
            const a = idx[i][j], b = idx[i + 1][j], c = idx[i + 1][j + 1], d = idx[i][j + 1];
            if (profile[i + 1][0] >= 1e-5) this.addTri(a, b, c);
            if (profile[i][0] >= 1e-5) this.addTri(a, c, d);
          }
      } else {
        const keep = this.color;
        for (let i = 0; i < n - 1; i++) {
          this.color = P.mix(RingCol(i), RingCol(i + 1), 0.5);
          for (let j = 0; j < sides; j++) {
            const a = Ring(i, j), b = Ring(i + 1, j), c = Ring(i + 1, j + 1), d = Ring(i, j + 1);
            const mx = (a[0] + b[0] + c[0] + d[0]) / 4, mz = (a[2] + b[2] + c[2] + d[2]) / 4;
            const ml = Math.hypot(mx, mz), rx = ml > 1e-5 ? mx / ml : 0, rz = ml > 1e-5 ? mz / ml : 0;
            const dr = profile[i + 1][0] - profile[i][0], dy = profile[i + 1][1] - profile[i][1];
            const out = [rx * dy, -dr, rz * dy];
            if (V.dot(out, out) < 1e-10) continue;
            if (profile[i][0] < 1e-5) this.triangleFacing(a, b, c, out);
            else if (profile[i + 1][0] < 1e-5) this.triangleFacing(a, c, d, out);
            else this.quad(a, b, c, d, out);
          }
        }
        this.color = keep;
      }
      const keepC = this.color;
      if (capBottom && profile[0][0] > 1e-5) {
        this.color = RingCol(0);
        const ctr = [0, profile[0][1], 0];
        for (let j = 0; j < sides; j++) this.triangleFacing(ctr, Ring(0, j), Ring(0, j + 1), DOWN);
      }
      if (capTop && profile[n - 1][0] > 1e-5) {
        this.color = RingCol(n - 1);
        const ctr = [0, profile[n - 1][1], 0];
        for (let j = 0; j < sides; j++) this.triangleFacing(ctr, Ring(n - 1, j + 1), Ring(n - 1, j), UP);
      }
      this.color = keepC;
    }
    cylinder(bottom, rBottom, rTop, height, sides, smooth = false, capBottom = true, capTop = true) {
      this.push().translate(bottom);
      this.lathe([[rBottom, 0], [rTop, height]], sides, smooth, capBottom, capTop);
      this.pop();
    }
    cone(bottom, radius, height, sides, smooth = false) { this.cylinder(bottom, radius, 0, height, sides, smooth, true, false); }
    /** Ellipsoid (UV sphere) with the given radii. */
    sphere(center, radii, sides = 10, rings = 7, smooth = true) {
      if (!Array.isArray(radii)) radii = [radii, radii, radii];
      rings = Math.max(2, rings | 0);
      const prof = [];
      for (let i = 0; i <= rings; i++) { const phi = Math.PI * i / rings; prof.push([Math.sin(phi), -Math.cos(phi)]); }
      prof[0][0] = 0; prof[rings][0] = 0;
      this.push().translate(center).scale(radii);
      this.lathe(prof, sides, smooth, false, false);
      this.pop();
    }
    /** Capsule along Y: total height (≥ 2r) standing on `bottom`. */
    capsule(bottom, radius, height, sides = 10, smooth = true) {
      height = Math.max(height, radius * 2);
      const cap = 3, prof = [];
      for (let i = 0; i <= cap; i++) { const a = Math.PI * 0.5 * i / cap; prof.push([Math.sin(a) * radius, radius - Math.cos(a) * radius]); }
      for (let i = 0; i <= cap; i++) { const a = Math.PI * 0.5 * i / cap; prof.push([Math.cos(a) * radius, height - radius + Math.sin(a) * radius]); }
      prof[0] = [0, 0]; prof[prof.length - 1] = [0, height];
      this.push().translate(bottom);
      this.lathe(prof, sides, smooth, false, false);
      this.pop();
    }
    /** Faceted organic blob: a jittered icosphere — rocks, bushes, clouds, moss. */
    blob(center, radii, detail = 1, jitter = 0.15, seed = 0, flattenBottom = 0) {
      if (!Array.isArray(radii)) radii = [radii, radii, radii];
      const D = dir => {
        const k = 1 + (hash01(dir, seed) * 2 - 1) * jitter;
        const p = [dir[0] * k * radii[0], dir[1] * k * radii[1], dir[2] * k * radii[2]];
        if (flattenBottom > 0 && p[1] < 0) p[1] *= 1 - flattenBottom;
        return V.add(center, p);
      };
      for (const f of ico(detail)) {
        const a = D(f[0]), b = D(f[1]), c = D(f[2]);
        this.triangleFacing(a, b, c, V.sub(V.mul(V.add(V.add(a, b), c), 1 / 3), center));
      }
    }
    /** Faceted blob coloured per face by its normal (colorOf(normal) → colour). */
    facetBlob(center, radii, detail, jitter, seed, flattenBottom, colorOf) {
      if (!Array.isArray(radii)) radii = [radii, radii, radii];
      const D = dir => {
        const k = 1 + (hash01(dir, seed) * 2 - 1) * jitter;
        const p = [dir[0] * k * radii[0], dir[1] * k * radii[1], dir[2] * k * radii[2]];
        if (flattenBottom > 0 && p[1] < 0) p[1] *= 1 - flattenBottom;
        return V.add(center, p);
      };
      for (const f of ico(detail)) {
        const a = D(f[0]), b = D(f[1]), c = D(f[2]);
        let n = V.cross(V.sub(b, a), V.sub(c, a));
        const out = V.sub(V.mul(V.add(V.add(a, b), c), 1 / 3), center);
        if (V.dot(n, out) < 0) n = V.neg(n);
        const base = colorOf ? colorOf(V.norm(n)) : this.color;
        const k = 1 + (this.random01() * 2 - 1) * this.jitter;
        this.triangleFacing(a, b, c, out, [base[0] * k, base[1] * k, base[2] * k]);
      }
    }
    /** Tapered round limb from a to b (arms, legs, branches, rails, staves). */
    segment(a, b, ra, rb, sides = 6, smooth = false, caps = true) {
      const axis = V.sub(b, a), len = V.len(axis);
      if (len < 1e-5) return;
      this.aim(a, axis);
      this.lathe([[ra, 0], [rb, len]], sides, smooth, caps, caps);
      this.pop();
    }
    /** Flat disc in the XZ plane facing +Y (or −Y with down). */
    disc(center, radius, sides = 12, down = false) {
      for (let j = 0; j < sides; j++) {
        const a0 = j * Math.PI * 2 / sides, a1 = (j + 1) * Math.PI * 2 / sides;
        this.triangleFacing(center, V.add(center, [Math.cos(a0) * radius, 0, Math.sin(a0) * radius]), V.add(center, [Math.cos(a1) * radius, 0, Math.sin(a1) * radius]), down ? DOWN : UP);
      }
    }
    plane(center, sizeX, sizeZ, twoSided = false) {
      const x = sizeX * 0.5, z = sizeZ * 0.5, c = center;
      const a = [c[0] - x, c[1], c[2] - z], b = [c[0] - x, c[1], c[2] + z], cc = [c[0] + x, c[1], c[2] + z], d = [c[0] + x, c[1], c[2] - z];
      this.quad(a, b, cc, d, UP);
      if (twoSided) this.quad(a, b, cc, d, DOWN);
    }
    /** Ring/torus around Y (rims, belts, halos). */
    torus(center, radius, tube, segments = 12, sides = 5) {
      const Pt = (a, b) => V.add(center, [Math.cos(a) * (radius + Math.cos(b) * tube), Math.sin(b) * tube, Math.sin(a) * (radius + Math.cos(b) * tube)]);
      for (let i = 0; i < segments; i++) {
        const a0 = i * Math.PI * 2 / segments, a1 = (i + 1) * Math.PI * 2 / segments, am = (a0 + a1) / 2;
        const rc = V.add(center, [Math.cos(am) * radius, 0, Math.sin(am) * radius]);
        for (let j = 0; j < sides; j++) {
          const b0 = j * Math.PI * 2 / sides, b1 = (j + 1) * Math.PI * 2 / sides;
          const p00 = Pt(a0, b0), p10 = Pt(a1, b0), p11 = Pt(a1, b1), p01 = Pt(a0, b1);
          const mid = V.mul(V.add(V.add(p00, p10), V.add(p11, p01)), 0.25);
          this.quad(p00, p10, p11, p01, V.sub(mid, rc));
        }
      }
    }
    /** Vertical prism of a convex polygon [[x, z], …] from y0 to y1. */
    extrude(polygon, y0, y1, capBottom = true, capTop = true) {
      const n = polygon.length;
      if (n < 3) return;
      let cx = 0, cz = 0;
      for (const p of polygon) { cx += p[0]; cz += p[1]; }
      cx /= n; cz /= n;
      for (let i = 0; i < n; i++) {
        const p = polygon[i], q = polygon[(i + 1) % n];
        this.quad([p[0], y0, p[1]], [q[0], y0, q[1]], [q[0], y1, q[1]], [p[0], y1, p[1]], [(p[0] + q[0]) / 2 - cx, 0, (p[1] + q[1]) / 2 - cz]);
      }
      const ct = [cx, y1, cz], cb = [cx, y0, cz];
      for (let i = 0; i < n; i++) {
        const p = polygon[i], q = polygon[(i + 1) % n];
        if (capTop) this.triangleFacing(ct, [p[0], y1, p[1]], [q[0], y1, q[1]], UP);
        if (capBottom) this.triangleFacing(cb, [p[0], y0, p[1]], [q[0], y0, q[1]], DOWN);
      }
    }
    /** Thin double-sided leaf/blade from base to tip (grass, leaves, flames, hair strands). */
    blade(basePoint, tip, width, sideways) {
      const axis = V.sub(tip, basePoint);
      let side = sideways || V.cross(axis, FWD);
      if (V.dot(side, side) < 1e-8) side = V.cross(axis, RIGHT);
      side = V.mul(V.norm(side), width * 0.5);
      const mid = V.madd(basePoint, axis, 0.45), n = V.cross(axis, side);
      const bm = V.madd(basePoint, side, -0.6), bp = V.madd(basePoint, side, 0.6), mm = V.sub(mid, side), mp = V.add(mid, side);
      for (const o of [n, V.neg(n)]) {
        this.triangleFacing(bm, mm, tip, o);
        this.triangleFacing(bm, tip, bp, o);
        this.triangleFacing(bp, tip, mp, o);
      }
    }
    /** Adds another builder's geometry (already in its own root space) through the current transform. */
    append(other) {
      if (!other) return;
      const base = this.P.length / 3, e = this.m.elements, n = this.nm.elements;
      for (let i = 0; i < other.P.length; i += 3) {
        const x = other.P[i], y = other.P[i + 1], z = other.P[i + 2];
        this.P.push(e[0] * x + e[4] * y + e[8] * z + e[12], e[1] * x + e[5] * y + e[9] * z + e[13], e[2] * x + e[6] * y + e[10] * z + e[14]);
        const a = other.N[i], b = other.N[i + 1], d = other.N[i + 2];
        const nn = V.norm([n[0] * a + n[3] * b + n[6] * d, n[1] * a + n[4] * b + n[7] * d, n[2] * a + n[5] * b + n[8] * d]);
        this.N.push(nn[0], nn[1], nn[2]);
      }
      this.C.push(...other.C); this.E.push(...other.E); this.W.push(...other.W); this.B.push(...other.B);
      for (let i = 0; i < other.P.length / 3; i++) this.K.push(other.K && other.K[i] != null ? other.K[i] : 1);
      for (let i = 0; i < other.T.length; i += 3) this.addTri(other.T[i] + base, other.T[i + 1] + base, other.T[i + 2] + base);
    }

    // ------------------------------------------------ kit helpers (UnitMeshKit / PropKit)
    /** Part of an ellipsoid surface (θ0..θ1 around Y from +Z towards +X, φ from phTop to phBottom(θ), degrees). */
    shell(c, r, th0, th1, nt, phTop, phBottom, np, inward = false) {
      const pb = typeof phBottom === 'function' ? phBottom : () => phBottom;
      const pt = typeof phTop === 'function' ? phTop : () => phTop;
      const On = (th, ph) => { th *= D2R; ph *= D2R; const s = Math.sin(ph); return [c[0] + r[0] * s * Math.sin(th), c[1] + r[1] * Math.cos(ph), c[2] + r[2] * s * Math.cos(th)]; };
      for (let j = 0; j < nt; j++) {
        const ta = lerp(th0, th1, j / nt), tb = lerp(th0, th1, (j + 1) / nt), ba = pb(ta), bb = pb(tb), ta0 = pt(ta), tb0 = pt(tb);
        for (let i = 0; i < np; i++) {
          const u0 = i / np, u1 = (i + 1) / np;
          const pa0 = lerp(ta0, ba, u0), pa1 = lerp(ta0, ba, u1), pb0 = lerp(tb0, bb, u0), pb1 = lerp(tb0, bb, u1);
          const a = On(ta, pa0), b = On(tb, pb0), cc = On(tb, pb1), d = On(ta, pa1);
          const m = V.mul(V.add(V.add(a, b), V.add(cc, d)), 0.25);
          let n = [(m[0] - c[0]) / (r[0] * r[0]), (m[1] - c[1]) / (r[1] * r[1]), (m[2] - c[2]) / (r[2] * r[2])];
          if (inward) n = V.neg(n);
          if (pa0 < 0.01 && pb0 < 0.01) this.triangleFacing(a, cc, d, n); else this.quad(a, b, cc, d, n);
        }
      }
    }
    /** Garment panel: θ0..θ1 (deg) of a surface of revolution through prof [[r, y]…] (top → bottom), squashed by depthK, with a lining. */
    panel(c, th0, th1, nt, prof, depthK, outer, lining, thick = 0.008) {
      const keep = this.color;
      for (let layer = 0; layer < 2; layer++) {
        if (layer === 1 && !lining) break;
        this.color = layer === 0 ? outer : lining;
        const dr = layer === 0 ? 0 : thick;
        const Rg = (p, th) => { const rr = Math.max(0.001, p[0] - dr); return [c[0] + rr * Math.sin(th), c[1] + p[1], c[2] + rr * Math.cos(th) * depthK]; };
        for (let j = 0; j < nt; j++) {
          const ta = lerp(th0, th1, j / nt) * D2R, tb = lerp(th0, th1, (j + 1) / nt) * D2R, tm = (ta + tb) / 2;
          let radial = [Math.sin(tm), 0, Math.cos(tm) / Math.max(0.2, depthK)];
          if (layer === 1) radial = V.neg(radial);
          for (let i = 0; i < prof.length - 1; i++) this.quad(Rg(prof[i], ta), Rg(prof[i], tb), Rg(prof[i + 1], tb), Rg(prof[i + 1], ta), radial);
        }
      }
      this.color = keep;
    }
    /** Open-ended tube around Y (belts, cuffs, collars); depthK squashes it front-back. */
    band(c, r0, r1, y0, y1, sides = 10, depthK = 1) {
      this.push().translate(c).scale(1, 1, depthK);
      this.lathe([[r0, y0], [r1, y1]], sides, false, false, false, null, 180 / sides);
      this.pop();
    }
    /** Elliptic lathe around Y with a front-back squash (torsos, robes): flat front face. */
    body(c, prof, sides, depthK, capBottom = true, capTop = true, smooth = false) {
      this.push().translate(c).scale(1, 1, depthK);
      this.lathe(prof, sides, smooth, capBottom, capTop, null, 180 / sides);
      this.pop();
    }
    /** Flat convex shape in the XY plane (pts [[x, y]…]) with thickness along Z (shields, blades, plates). */
    flat(pts, thickness) {
      this.push().rotate(-90, 0, 0);
      this.extrude(pts, -thickness * 0.5, thickness * 0.5);
      this.pop();
    }
    /** Cloth strip with thickness from a to b, width w0 → w1, broad faces towards `facing`. */
    strip(a, b, w0, w1, facing, thick = 0.012) {
      const axis = V.sub(b, a);
      if (V.dot(axis, axis) < 1e-8) return;
      let side = V.cross(axis, facing);
      if (V.dot(side, side) < 1e-8) side = V.cross(axis, UP);
      if (V.dot(side, side) < 1e-8) side = V.cross(axis, RIGHT);
      side = V.norm(side);
      let n = V.norm(V.cross(side, axis));
      if (V.dot(n, facing) < 0) { n = V.neg(n); side = V.neg(side); }
      const h = V.mul(n, thick * 0.5);
      const a0 = V.madd(a, side, -w0 / 2), a1 = V.madd(a, side, w0 / 2), b0 = V.madd(b, side, -w1 / 2), b1 = V.madd(b, side, w1 / 2);
      const pl = p => V.add(p, h), mi = p => V.sub(p, h);
      this.quad(pl(a0), pl(a1), pl(b1), pl(b0), n);
      this.quad(mi(a0), mi(a1), mi(b1), mi(b0), V.neg(n));
      this.quad(pl(a1), mi(a1), mi(b1), pl(b1), side);
      this.quad(pl(a0), mi(a0), mi(b0), pl(b0), V.neg(side));
      const ax = V.norm(axis);
      this.quad(pl(b0), pl(b1), mi(b1), mi(b0), ax);
      this.quad(pl(a0), pl(a1), mi(a1), mi(a0), V.neg(ax));
    }
    curvedStrip(a, ctrl, b, w0, w1, facing, thick = 0.012, segs = 3) {
      let prev = a, prevW = w0;
      for (let i = 1; i <= segs; i++) {
        const t = i / segs, u = 1 - t;
        const p = [u * u * a[0] + 2 * u * t * ctrl[0] + t * t * b[0], u * u * a[1] + 2 * u * t * ctrl[1] + t * t * b[1], u * u * a[2] + 2 * u * t * ctrl[2] + t * t * b[2]];
        const w = lerp(w0, w1, t);
        this.strip(prev, p, prevW, w, facing, thick);
        prev = p; prevW = w;
      }
    }
    spike(at, dir, radius, length, sides = 5) { this.aim(at, dir); this.cone([0, 0, 0], radius, length, sides); this.pop(); }
    /** Curved horn/branch: tapered segments along a quadratic curve a → ctrl → b. */
    curve(a, ctrl, b, r0, r1, segs = 4, sides = 6) {
      let prev = a, prevR = r0;
      for (let i = 1; i <= segs; i++) {
        const t = i / segs, u = 1 - t;
        const p = [u * u * a[0] + 2 * u * t * ctrl[0] + t * t * b[0], u * u * a[1] + 2 * u * t * ctrl[1] + t * t * b[1], u * u * a[2] + 2 * u * t * ctrl[2] + t * t * b[2]];
        const r = lerp(r0, r1, t);
        this.segment(prev, p, prevR, Math.max(0.002, r), sides, false, true);
        prev = p; prevR = r;
      }
    }
    /** Skin between successive cross-section rings (same point count); caps fan the end rings. */
    loft(rings, capStart = true, capEnd = true, colorOf = null) {
      const count = rings.length;
      if (count < 2) return;
      const n = rings[0].length, cents = rings.map(r => V.mul(r.reduce((s, p) => V.add(s, p), [0, 0, 0]), 1 / n));
      for (let i = 0; i < count - 1; i++) {
        const A = rings[i], B = rings[i + 1], axis = V.sub(cents[i + 1], cents[i]), al = V.dot(axis, axis);
        for (let j = 0; j < n; j++) {
          const k = (j + 1) % n;
          if (V.len(V.sub(A[j], A[k])) < 1e-5 && V.len(V.sub(B[j], B[k])) < 1e-5) continue;
          const mid = V.mul(V.add(V.add(A[j], A[k]), V.add(B[j], B[k])), 0.25);
          const t = al > 1e-8 ? clamp01(V.dot(V.sub(mid, cents[i]), axis) / al) : 0;
          const out = V.sub(mid, V.madd(cents[i], axis, t));
          let col;
          if (colorOf) {
            let nrm = V.cross(V.sub(A[k], A[j]), V.sub(B[k], A[j]));
            if (V.dot(nrm, out) < 0) nrm = V.neg(nrm);
            const c0 = colorOf(V.norm(nrm)), jk = 1 + (this.random01() * 2 - 1) * this.jitter;
            col = [c0[0] * jk, c0[1] * jk, c0[2] * jk];
          }
          this.quad(A[j], A[k], B[k], B[j], out, col);
        }
      }
      if (capStart) this.fan(rings[0], cents[0], V.sub(cents[0], cents[1]), colorOf);
      if (capEnd) this.fan(rings[count - 1], cents[count - 1], V.sub(cents[count - 1], cents[count - 2]), colorOf);
    }
    fan(ring, c, outward, colorOf = null) {
      const col = colorOf ? colorOf(V.norm(outward)) : this.faceColor();
      for (let j = 0; j < ring.length; j++) {
        const p = ring[j], q = ring[(j + 1) % ring.length];
        if (V.len(V.sub(p, q)) < 1e-5) continue;
        this.triangleFacing(c, p, q, outward, col);
      }
    }
    /** Oriented box (center, size, THREE.Quaternion). */
    obox(center, size, quat, topColor) {
      this.push().translate(center).rotateQ(quat);
      this.box([0, 0, 0], size, topColor);
      this.pop();
    }
    /** Rectangular beam from a to b (w across, h along upHint). */
    beam(a, b, w, h, upHint) {
      const axis = V.sub(b, a), len = V.len(axis);
      if (len < 1e-5) return;
      const z = V.mul(axis, 1 / len);
      let up = upHint || UP;
      if (Math.abs(V.dot(V.norm(up), z)) > 0.98) up = Math.abs(z[0]) < 0.9 ? RIGHT : FWD;
      const x = V.norm(V.cross(up, z)), y = V.cross(z, x);
      const m = new THREE.Matrix4().makeBasis(new THREE.Vector3(...x), new THREE.Vector3(...y), new THREE.Vector3(...z));
      m.setPosition(...V.mul(V.add(a, b), 0.5));
      this.push().setMatrix(this.m.clone().multiply(m));
      this.box([0, 0, 0], [w, h, len]);
      this.pop();
    }
    /** Sagging rope / cord from a to b. */
    rope(a, b, sag, radius, segments = 6, sides = 5) {
      let prev = a;
      for (let i = 1; i <= segments; i++) {
        const t = i / segments, p = V.add(V.lerp(a, b, t), [0, -sag * 4 * t * (1 - t), 0]);
        this.segment(prev, p, radius, radius, sides, false, true);
        prev = p;
      }
    }
    /** Little grass tuft of closed thin cones (safe with outlines). */
    tuft(at, height, blades, col) {
      const keep = this.color;
      for (let i = 0; i < blades; i++) {
        const a = (i + this.random01() * 0.5) * Math.PI * 2 / blades;
        const tip = V.add(at, [Math.cos(a) * height * 0.35, height * (0.7 + 0.3 * this.random01()), Math.sin(a) * height * 0.35]);
        this.color = P.shade(col, 0.9 + 0.2 * this.random01());
        this.segment(V.add(at, [Math.cos(a) * 0.03, 0, Math.sin(a) * 0.03]), tip, height * 0.08, 0.004, 3, false, false);
      }
      this.color = keep;
    }
    /** Ribbed paper lantern centred on c (height h, radius r) with caps and a tassel; glows when lit. */
    paperLantern(c, r, h, paper, lit, cap, tassel, sides = 8) {
      const keepC = this.color, keepE = this.emission, keepN = this.nightEmission, keepJ = this.jitter;
      this.jitter = 0.03;
      // lit: warm papers turn honey-gold, coloured (red) papers glow a deeper orange of their own hue; 'night' = by night only
      const warmPaper = P.luma(paper) > 0.6, night = lit === 'night';
      this.color = lit && !night ? (warmPaper ? P.mix(paper, PAL.glow, 0.55) : P.mix(paper, PAL.glowDeep, 0.35)) : paper;
      this.emission = lit && !night ? (warmPaper ? 0.95 : 0.85) : 0;
      this.nightEmission = night ? (warmPaper ? 0.95 : 0.85) : 0;
      this.push().translate(c);
      this.lathe([[r * 0.58, -h * 0.5], [r * 0.9, -h * 0.3], [r, 0], [r * 0.9, h * 0.3], [r * 0.58, h * 0.5]], sides, false, true, true);
      this.emission = 0; this.nightEmission = 0;
      this.color = cap;
      this.cylinder([0, h * 0.5 - 0.01, 0], r * 0.62, r * 0.5, h * 0.12, sides, false, false, true);
      this.cylinder([0, -h * 0.5 - h * 0.1, 0], r * 0.5, r * 0.62, h * 0.11, sides, false, true, false);
      this.color = tassel || PAL.vermilion;
      this.cylinder([0, -h * 0.5 - h * 0.32, 0], r * 0.08, r * 0.22, h * 0.22, Math.min(5, sides), false, true, false);
      this.pop();
      this.color = keepC; this.emission = keepE; this.nightEmission = keepN; this.jitter = keepJ;
    }

    // ------------------------------------------------ soft foliage (PropNature.SoftLump + PropKit.SoftenParts)
    /**
     * Soft foliage lump: a jittered icosphere (detail 1 = 80 tris: a lumpy silhouette) SHADED smooth: in toGeometry its
     * vertices get the ellipsoid's analytic normals and per-vertex colours from the canopy ramp at their height (darker
     * towards the lump's own underside so neighbouring lumps separate, plus faint painterly noise and AO).
     */
    softLump(c, r, detail, yawDeg, ramp, lumpiness = 0.1) {
      if (!Array.isArray(r)) r = [r, r, r];
      const seed = Math.round(c[0] * 97 + c[1] * 31 + c[2] * 13 + yawDeg);
      const rot = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, yawDeg * D2R, 0));
      const start = this.vertexCount;
      const D = dir => {
        const k = 1 + (hash01(dir, seed) * 2 - 1) * lumpiness;
        const p = [dir[0] * k * r[0], dir[1] * k * r[1], dir[2] * k * r[2]];
        if (p[1] < 0) p[1] *= SOFT_UNDER;
        _v.set(p[0], p[1], p[2]).applyQuaternion(rot);
        return [c[0] + _v.x, c[1] + _v.y, c[2] + _v.z];
      };
      const white = [1, 1, 1];
      for (const f of ico(detail)) {
        const a = D(f[0]), b = D(f[1]), d = D(f[2]);
        if (V.dot(V.cross(V.sub(b, a), V.sub(d, a)), V.sub(V.mul(V.add(V.add(a, b), d), 1 / 3), c)) < 0) this.triangle(a, d, b, white);
        else this.triangle(a, b, d, white);
      }
      const trs = new THREE.Matrix4().compose(new THREE.Vector3(...c), rot, new THREE.Vector3(...r));
      this.softParts.push({ start, end: this.vertexCount, ramp, toUnit: this.m.clone().multiply(trs).invert(), jitter: this.jitter, ao: this.aoStrength, aoHeight: this.aoHeight });
    }
    _soften() {
      for (const part of this.softParts) {
        const e = part.toUnit.elements;
        for (let i = part.start; i < part.end; i++) {
          const x = this.P[i * 3], y = this.P[i * 3 + 1], z = this.P[i * 3 + 2];
          const ux = e[0] * x + e[4] * y + e[8] * z + e[12], uy = e[1] * x + e[5] * y + e[9] * z + e[13], uz = e[2] * x + e[6] * y + e[10] * z + e[14];
          const gx = ux, gy = uy < 0 ? uy / (SOFT_UNDER * SOFT_UNDER) : uy, gz = uz;
          // normal = (toUnit³ˣ³)ᵀ · g
          const n = V.norm([e[0] * gx + e[1] * gy + e[2] * gz, e[4] * gx + e[5] * gy + e[6] * gz, e[8] * gx + e[9] * gy + e[10] * gz]);
          this.N[i * 3] = n[0]; this.N[i * 3 + 1] = n[1]; this.N[i * 3 + 2] = n[2];
          const t = clamp01((uy + SOFT_UNDER) / (1 + SOFT_UNDER));
          let k = lerp(0.84, 1.04, sstep(t));
          k *= 1 + (hash01([x * 1.7, y * 1.7, z * 1.7], 7) * 2 - 1) * part.jitter * 0.5;
          if (part.ao > 0 && part.aoHeight > 0) k *= lerp(1 - part.ao, 1, clamp01(y / part.aoHeight));
          const c = part.ramp(y);
          this.C[i * 3] = clamp01(c[0] * k); this.C[i * 3 + 1] = clamp01(c[1] * k); this.C[i * 3 + 2] = clamp01(c[2] * k);
        }
      }
      this.softParts = [];
    }

    // ------------------------------------------------ output
    /**
     * Non-indexed BufferGeometry: position, normal, color (vec4: sRGB albedo + emission code in alpha, see LP.EMIT), lpWind,
     * lpSmooth (smoothed normal for the ink outline, its length = the ink width factor), and with { skinned: true } rigid
     * skinIndex / skinWeight (100 % on each vertex's bone). opts.compact (default true) stores normal / lpSmooth as
     * normalized Int8 and color as normalized Uint8 (26 bytes a vertex instead of 56); { compact: false } keeps floats.
     */
    toGeometry(opts) {
      opts = opts || {};
      this._soften();
      const compact = opts.compact !== false;
      const nv = this.P.length / 3;
      // smoothed normals: sum of the normals of coincident vertices
      const sum = new Map(), keys = new Array(nv);
      for (let i = 0; i < nv; i++) {
        const k = Math.round(this.P[i * 3] * 2000) + ',' + Math.round(this.P[i * 3 + 1] * 2000) + ',' + Math.round(this.P[i * 3 + 2] * 2000);
        keys[i] = k;
        const s = sum.get(k);
        if (s) { s[0] += this.N[i * 3]; s[1] += this.N[i * 3 + 1]; s[2] += this.N[i * 3 + 2]; } else sum.set(k, [this.N[i * 3], this.N[i * 3 + 1], this.N[i * 3 + 2]]);
      }
      const T = this.T, nt = T.length;
      const pos = new Float32Array(nt * 3), wind = new Float32Array(nt);
      const nor = compact ? new Int8Array(nt * 3) : new Float32Array(nt * 3);
      const sm = compact ? new Int8Array(nt * 3) : new Float32Array(nt * 3);
      const col = compact ? new Uint8Array(nt * 4) : new Float32Array(nt * 4);
      const qn = compact ? v => Math.max(-127, Math.min(127, Math.round(v * 127))) : v => v;
      const qc = compact ? v => Math.round(clamp01(v) * 255) : v => v;
      const skinned = !!opts.skinned;
      const si = skinned ? new Uint16Array(nt * 4) : null, sw = skinned ? new Float32Array(nt * 4) : null;
      for (let t = 0; t < nt; t++) {
        const i = T[t];
        pos[t * 3] = this.P[i * 3]; pos[t * 3 + 1] = this.P[i * 3 + 1]; pos[t * 3 + 2] = this.P[i * 3 + 2];
        nor[t * 3] = qn(this.N[i * 3]); nor[t * 3 + 1] = qn(this.N[i * 3 + 1]); nor[t * 3 + 2] = qn(this.N[i * 3 + 2]);
        col[t * 4] = qc(this.C[i * 3]); col[t * 4 + 1] = qc(this.C[i * 3 + 1]); col[t * 4 + 2] = qc(this.C[i * 3 + 2]); col[t * 4 + 3] = qc(encodeEmission(this.E[i]));
        wind[t] = this.W[i];
        // lpSmooth: the smoothed normal, its length the vertex's ink width factor (MeshBuilder.ink)
        const s = V.mul(V.norm(sum.get(keys[i])), this.K[i] != null ? clamp01(this.K[i]) : 1);
        sm[t * 3] = qn(s[0]); sm[t * 3 + 1] = qn(s[1]); sm[t * 3 + 2] = qn(s[2]);
        if (skinned) { si[t * 4] = this.B[i]; sw[t * 4] = 1; }
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      g.setAttribute('normal', new THREE.BufferAttribute(nor, 3, compact));
      g.setAttribute('color', new THREE.BufferAttribute(col, 4, compact));
      g.setAttribute('lpWind', new THREE.BufferAttribute(wind, 1));
      g.setAttribute('lpSmooth', new THREE.BufferAttribute(sm, 3, compact));
      if (skinned) { g.setAttribute('skinIndex', new THREE.BufferAttribute(si, 4)); g.setAttribute('skinWeight', new THREE.BufferAttribute(sw, 4)); }
      g.computeBoundingBox(); g.computeBoundingSphere();
      g.userData.tris = nt / 3;
      g.userData.lp = true;
      return g;
    }
    clear() {
      this.P = []; this.N = []; this.C = []; this.E = []; this.W = []; this.B = []; this.T = []; this.K = []; this.stack = []; this.softParts = [];
      this.setMatrix(new THREE.Matrix4());
    }
    /**
     * Re-binds the vertices added since vertex index `start` (rigid skinning, per vertex): boneOf([x, y, z]) → bone index
     * (positions in the builder's root space). A part can then hinge across bones — a robe panel's top row on the hips and
     * the rest on the thigh, a beard's upper third on the head and the rest on the chest — instead of one rigid piece.
     */
    boneBy(start, boneOf) {
      for (let i = start; i < this.P.length / 3; i++) this.B[i] = boneOf([this.P[i * 3], this.P[i * 3 + 1], this.P[i * 3 + 2]]) | 0;
    }
    /** Sets the wind weight of the vertices added since `start` from their position (hair and tails: 0 at the root, 1 at the tips). */
    windBy(start, weightOf) {
      for (let i = start; i < this.P.length / 3; i++) this.W[i] = weightOf([this.P[i * 3], this.P[i * 3 + 1], this.P[i * 3 + 2]]);
    }
    /**
     * Han paper lantern (灯笼): a squashed 8-sided sphere whose vertical ribs alternate in tone (jittered), gold caps top and
     * bottom, a short tassel strip under it (sways). Day: a faint glow (emission dayGlow); lit: the paper glows a deeper
     * orange of its own hue. Centred on c; r = radius, h = height of the paper body.
     */
    ribLantern(c, r, h, paper, lit, cap, tassel, sides = 8, dayGlow = 0.2) {
      const keepC = this.color, keepE = this.emission, keepN = this.nightEmission, keepJ = this.jitter, keepW = this.wind, keepG = this.windGradient;
      this.jitter = 0;
      // lit: true = always glowing; 'night' = glows by night only (the mood lights it); false = a faint day glow
      const night = lit === 'night';
      const body = lit && !night ? P.mix(paper, PAL.glowDeep, 0.3) : paper;
      const prof = [[r * 0.5, -h * 0.5], [r * 0.88, -h * 0.3], [r, 0], [r * 0.88, h * 0.3], [r * 0.5, h * 0.5]];
      this.push().translate(c);
      this.emission = lit && !night ? 0.85 : dayGlow;
      this.nightEmission = night ? 0.85 : 0;
      for (let j = 0; j < sides; j++) {
        const a0 = j * Math.PI * 2 / sides, a1 = (j + 1) * Math.PI * 2 / sides;
        const k = (j % 2 ? 0.86 : 1.0) * (1 + (this.random01() * 2 - 1) * 0.05);
        for (let i = 0; i < prof.length - 1; i++) {
          const p0 = prof[i], p1 = prof[i + 1];
          const A = [Math.cos(a0) * p0[0], p0[1], Math.sin(a0) * p0[0]], B = [Math.cos(a0) * p1[0], p1[1], Math.sin(a0) * p1[0]];
          const Cc = [Math.cos(a1) * p1[0], p1[1], Math.sin(a1) * p1[0]], D = [Math.cos(a1) * p0[0], p0[1], Math.sin(a1) * p0[0]];
          const am = (a0 + a1) / 2, out = [Math.cos(am) * (p1[1] - p0[1]), -(p1[0] - p0[0]), Math.sin(am) * (p1[1] - p0[1])];
          this.quad(A, B, Cc, D, out, P.shade(body, k * (i === 0 || i === prof.length - 2 ? 0.9 : 1)));
        }
      }
      this.emission = 0; this.nightEmission = 0;
      this.color = cap;
      this.cylinder([0, h * 0.5 - 0.01, 0], r * 0.56, r * 0.42, h * 0.13, sides, false, false, true);
      this.cylinder([0, -h * 0.5 - h * 0.12, 0], r * 0.42, r * 0.56, h * 0.13, sides, false, true, false);
      // tassel: a short strip that sways
      this.color = tassel || cap;
      this.windGradient = false; this.wind = 0.6;
      this.strip([0, -h * 0.5 - h * 0.2, 0], [0, -h * 0.5 - h * 0.62, 0], r * 0.22, r * 0.3, [0, 0, 1], r * 0.06);
      this.pop();
      this.color = keepC; this.emission = keepE; this.nightEmission = keepN; this.jitter = keepJ; this.wind = keepW; this.windGradient = keepG;
    }
  }
  LP.MeshBuilder = MeshBuilder;

  // ------------------------------------------------- shared palette --
  // Lanternvale's PropKit palette (warm creams, sage/moss, dusty blues, terracotta, honey-gold) + a Han set.
  const H = P.hex;
  const PAL = {
    cream: H('#F4E8D0'), plaster: H('#EEDDBF'), plasterWarm: H('#F0D2A8'), timber: H('#6E4B36'), timberDark: H('#553A2B'), wood: H('#9A7050'), woodLight: H('#BE9468'),
    stone: H('#A99F92'), stoneLight: H('#C6BDAE'), stoneDark: H('#867D74'), stoneCool: H('#A19EA9'),
    moss: H('#7D9A55'), mossLight: H('#9DB866'), sage: H('#A8BC8A'), leaf: H('#6E9A4E'), leafDark: H('#4E7440'), forest: H('#3F6146'),
    terracotta: H('#C9785A'), terracottaDark: H('#A95E46'), teal: H('#5E9C94'), tileBlue: H('#6E8FBF'), dustyBlue: H('#8FA7C4'),
    honey: H('#E8B04F'), glow: H('#FFD583'), glowDeep: H('#FFB65C'), glassDark: H('#56637A'),
    iron: H('#4F4A55'), gold: H('#E3B34C'), brass: H('#C9A04A'), vermilion: H('#D9553A'), rope: H('#D9C189'), paper: H('#F8EFDC'),
    earth: H('#8C6E52'), ink: H('#3A2F42'), charcoal: H('#3B3330'),
    // Han (三国) additions, same softness
    rammed: H('#E2CFA6'), rammedDark: H('#C8B184'), brick: H('#B9A58C'), brickDark: H('#9C8B78'), hanRed: H('#C9503A'), hanRedDark: H('#9E3B2E'),
    tileSlate: H('#7486A0'), tileSlateDark: H('#5B6A82'), tileWarm: H('#8E8580'), lacquer: H('#3A3134'), jade: H('#5FA07A'),
  };
  LP.PAL = PAL;

  /** Foliage palette per seed bucket: { top, side, bottom } (PropNature.Foliage). */
  // Lanternvale's own values were tuned for its prop sheets; in this game's top-down view a crown lit that warm and bright
  // reads paler than the meadow ("bubbles"), so the in-game defaults keep the top at the foliage brightness (topVal 1.0,
  // Lanternvale 1.08), the warm sun-lit tip a touch of yellow (warmMix 0.10, Lanternvale 0.22) and pull the underside well
  // into teal (coolMix 0.55 towards #2F5A5C, Lanternvale 0.25 towards #355A63). Pass Lanternvale's values to get its exact look.
  LP.FOLIAGE_TUNE = { topVal: 1.0, warmMix: 0.10, warmVal: 1.0, coolMix: 0.55, cool: '#2F5A5C' };
  LP.foliage = function (b, base, hueStep = 7, tune) {
    const T = Object.assign({}, LP.FOLIAGE_TUNE, tune || {});
    const side = P.hsv(base, (b - 1.5) * hueStep, 1, 1);
    const top = P.hsv(P.mix(side, H('#DCE07C'), 0.26), 0, 0.95, T.topVal);
    const bottom = P.hsv(P.mix(side, H('#3C5A5A'), 0.45), 0, 1, 0.85);
    return { top, side, bottom };
  };
  /** Canopy-wide vertical ramp: cool teal underneath → foliage colour → warm sun-lit top over [y0, y1]. */
  LP.canopyRamp = function (top, side, bottom, y0, y1, tune) {
    const T = Object.assign({}, LP.FOLIAGE_TUNE, tune || {});
    const warm = P.hsv(P.mix(top, H('#F2E38A'), T.warmMix), -2, 1, T.warmVal);
    const cool = P.mix(bottom, H(T.cool), T.coolMix);
    return y => {
      const t = clamp01((y - y0) / Math.max(0.01, y1 - y0));
      if (t < 0.45) return P.mix(cool, side, sstep(t / 0.45));
      if (t < 0.8) return P.mix(side, top, sstep((t - 0.45) / 0.35));
      return P.mix(top, warm, (t - 0.8) / 0.2);
    };
  };
  /** Faceted-blob colouring by normal (rocks: top / side / bottom). */
  LP.byNormal = (top, side, bottom, from = 0.3) => n => (n[1] > from ? P.mix(side, top, clamp01((n[1] - from) / (1 - from))) : n[1] < -0.3 ? bottom : side);

  // ------------------------------------------------------- MeshCache --
  const CACHE = new Map();
  LP.MeshCache = {
    get(key, build) { let g = CACHE.get(key); if (!g) { g = build(); if (g) CACHE.set(key, g); } return g; },
    has: key => CACHE.has(key),
    clear() { for (const g of CACHE.values()) g.dispose(); CACHE.clear(); },
  };

  // ======================================================= materials ==
  // Shader globals (Lanternvale's SceneLighting globals, minus what three's lights carry). The per-scene ones (marked *)
  // are pushed by that scene's LP.Mood just before the scene renders and reset to their defaults right after it, so two
  // scenes with different moods (the strategy map and a duel screen) never leak into each other.
  const G = {
    lpMisc: { value: new THREE.Vector4(0, 0.06, 1.6, 0) },      // x*: night glow boost of emission, y: wind strength (m), z: wind speed, w: time (s)
    lpRim: { value: new THREE.Vector4(1, 0.92, 0.78, 3) },      // *rgb: rim colour (linear), a: rim power
    lpGrade: { value: new THREE.Vector4(0, 0.8, 0.92, 1.22) },  // *x: night grade amount, yzw: tint of the graded luma
    lpWarmth: { value: new THREE.Vector4(0, 0, 0, 0) },         // *x: golden lift, y: lamp amber pull, z: lamp moon mask
    lpFog: { value: new THREE.Vector4(0.85, 1, 0, 0) },         // *x: max fog amount, y: 1 = fog colour is sRGB (decode)
    lpShadow: { value: new THREE.Vector4(0.7, 0, 0, 0) },       // *x: how much a cast shadow darkens the sun term
    lpNight: { value: new THREE.Vector4(0, 0, 0, 0) },          // *x: night-only emission scale (0 by day … 1 at night)
    lpSunC: { value: new THREE.Vector4(1, 1, 1, 0) },           // *rgb: sun / moon colour × intensity (linear) used for directional light 0 when a = 1
    lpSkyC: { value: new THREE.Vector4(1, 1, 1, 0) },           // *rgb: sky ambient × intensity (linear) used for hemisphere light 0 when a = 1
    lpGroundC: { value: new THREE.Vector4(0, 0, 0, 0) },        // *rgb: ground ambient × intensity (with lpSkyC.a)
    lpView: { value: new THREE.Vector4(1920, 1080, 1, 1) },     // xy: viewport (px), z: outline px per unit width (= h / 1080), w: minimum ink width (px)
    lpOut: { value: 1 },                                        // 1: encode linear → sRGB in the shader (Gamma workflow / linear output)
  };
  LP.globals = G;
  const SCENE_KEYS = ['lpRim', 'lpGrade', 'lpWarmth', 'lpShadow', 'lpNight', 'lpSunC', 'lpSkyC', 'lpGroundC'];
  const G_DEFAULT = {};
  for (const k of SCENE_KEYS) G_DEFAULT[k] = G[k].value.clone();
  G_DEFAULT.glow = 0; G_DEFAULT.fogMax = 0.85; G_DEFAULT.fogSrgb = 1;
  /** Back to the defaults of the per-scene globals (LP.Mood calls it after its scene rendered). */
  LP.resetGlobals = function () {
    for (const k of SCENE_KEYS) G[k].value.copy(G_DEFAULT[k]);
    G.lpMisc.value.x = G_DEFAULT.glow; G.lpFog.value.x = G_DEFAULT.fogMax; G.lpFog.value.y = G_DEFAULT.fogSrgb;
  };
  LP.setTime = t => { G.lpMisc.value.w = t; };
  LP.setWind = (strength, speed) => { G.lpMisc.value.y = strength; if (speed != null) G.lpMisc.value.z = speed; };
  /** Viewport in drawing-buffer pixels (ink width reference: refHeight px = full width; minPx: the thinnest ink line). */
  LP.setViewport = (w, h, refHeight = 1080, minPx) => { G.lpView.value.set(w, h, h / refHeight, minPx != null ? minPx : G.lpView.value.w); };
  LP.setOutputEncoded = enc => { G.lpOut.value = enc === false ? 0 : 1; };
  LP.setShadowStrength = k => { G.lpShadow.value.x = k; };
  LP.lightColor = hex => { const c = P.lin(Array.isArray(hex) ? hex : P.hex(hex)); return new THREE.Color(c[0], c[1], c[2]); };

  const GLSL_COMMON = /* glsl */`
uniform vec4 lpMisc;
uniform vec4 lpView;
uniform float lpOut;
uniform float uFade;
vec3 lpLin( vec3 c ) { return mix( c / 12.92, pow( ( c + 0.055 ) / 1.055, vec3( 2.4 ) ), step( 0.04045, c ) ); }
vec3 lpEnc( vec3 c ) { c = max( c, vec3( 0.0 ) ); return lpOut > 0.5 ? mix( c * 12.92, 1.055 * pow( c, vec3( 1.0 / 2.4 ) ) - 0.055, step( 0.0031308, c ) ) : c; }
// Gentle wind sway in world space (Y up; the ground is XZ); weight 0 at the base of a plant, 1 at its tips.
vec3 lpWindSway( vec3 wp, float w ) {
  if ( w <= 0.0 ) return wp;
  float t = lpMisc.w * lpMisc.z;
  float s = sin( t + wp.x * 0.37 + wp.z * 0.21 ) + 0.45 * sin( t * 2.3 + wp.x * 0.93 + 1.7 );
  float a = lpMisc.y * w;
  wp.x += s * a;
  wp.z += 0.35 * s * a;
  return wp;
}
// 4 × 4 ordered dither threshold (Lanternvale's _Fade dissolve): 1/32 … 31/32
float lpBayer( vec2 p ) {
  vec2 q = mod( floor( p ), 4.0 );
  float a = mod( q.x, 2.0 ), b = mod( q.y, 2.0 ), c = floor( q.x * 0.5 ), d = floor( q.y * 0.5 );
  float fine = a * 2.0 + b * 3.0 - 4.0 * a * b, coarse = c * 2.0 + d * 3.0 - 4.0 * c * d;
  return ( fine * 4.0 + coarse + 0.5 ) / 16.0;
}
`;

  const LP_VERT = /* glsl */`
#include <common>
#include <skinning_pars_vertex>
#include <shadowmap_pars_vertex>
#include <fog_pars_vertex>
${GLSL_COMMON}
attribute float lpWind;
uniform float uWindScale;
varying vec3 vViewPosition;
varying vec3 vNormalV;
varying vec4 vColor;
#ifdef USE_INSTANCING_COLOR
  varying vec3 vInstTint;
#endif
#ifdef LP_DETAIL
  uniform float uPlanar;
  varying vec2 vDetailUv;
#endif
#ifdef LP_MAP
  varying vec2 vMapUv;
#endif
void main() {
  #include <beginnormal_vertex>
  #include <skinbase_vertex>
  #include <skinnormal_vertex>
  #include <defaultnormal_vertex>
  vNormalV = normalize( transformedNormal );
  #include <begin_vertex>
  #include <skinning_vertex>
  vec4 worldPosition = vec4( transformed, 1.0 );
  #ifdef USE_INSTANCING
    worldPosition = instanceMatrix * worldPosition;
  #endif
  worldPosition = modelMatrix * worldPosition;
  worldPosition.xyz = lpWindSway( worldPosition.xyz, lpWind * uWindScale );
  vec4 mvPosition = viewMatrix * worldPosition;
  gl_Position = projectionMatrix * mvPosition;
  vViewPosition = - mvPosition.xyz;
  vColor = color;
  #ifdef USE_INSTANCING_COLOR
    vInstTint = instanceColor;
  #endif
  #ifdef LP_DETAIL
    vDetailUv = worldPosition.xz * uPlanar;
  #endif
  #ifdef LP_MAP
    vMapUv = uv;
  #endif
  #include <shadowmap_vertex>
  #include <fog_vertex>
}
`;

  const LP_FRAG = /* glsl */`
#include <common>
#include <packing>
#include <lights_pars_begin>
#include <shadowmap_pars_fragment>
#include <fog_pars_fragment>
${GLSL_COMMON}
uniform vec4 lpRim;
uniform vec4 lpGrade;
uniform vec4 lpWarmth;
uniform vec4 lpFog;
uniform vec4 lpShadow;
uniform vec4 lpNight;
uniform vec4 lpSunC;
uniform vec4 lpSkyC;
uniform vec4 lpGroundC;
uniform vec3 uTint;
uniform vec4 uFlash;
uniform float uRimBoost;
uniform float uFogScale;
uniform float uLit;
varying vec3 vViewPosition;
varying vec3 vNormalV;
varying vec4 vColor;
#ifdef USE_INSTANCING_COLOR
  varying vec3 vInstTint;
#endif
#ifdef LP_DETAIL
  uniform sampler2D uDetail;
  uniform float uDetailK;
  varying vec2 vDetailUv;
#endif
#ifdef LP_MAP
  uniform sampler2D uMap;
  varying vec2 vMapUv;
#endif
#ifdef LP_ALPHA
  uniform float uOpacity;
#endif
const vec3 LV_LUMA = vec3( 0.3, 0.59, 0.11 );
const vec3 LV_LAMP_AMBER = vec3( 1.3, 1.0, 0.5 );
const vec3 LV_GLOW = vec3( 1.0, 0.66, 0.3 );
// the sun's soft painterly ramp: wrapped diffuse eased in
float lpSunRamp( vec3 n, vec3 l ) {
  float wrap = clamp( ( dot( n, l ) + 0.45 ) / 1.45, 0.0, 1.0 );
  return wrap * wrap * ( 3.0 - 2.0 * wrap );
}
void main() {
  #ifdef LP_FADE
    if ( uFade < 0.999 && uFade <= lpBayer( gl_FragCoord.xy ) ) discard;
  #endif
  vec3 n = normalize( vNormalV );
  #ifdef DOUBLE_SIDED
    n = gl_FrontFacing ? n : - n;
  #endif
  vec3 albedo = lpLin( vColor.rgb ) * uTint;
  #ifdef USE_INSTANCING_COLOR
    albedo *= lpLin( vInstTint );
  #endif
  float alpha = 1.0;
  #ifdef LP_DETAIL
    // painted detail (Lanternvale's planar _MainTex on terrain): a tileable multiplier around 1 (texel 0.5 = 1)
    albedo *= mix( vec3( 1.0 ), texture2D( uDetail, vDetailUv ).rgb * 2.0, uDetailK );
  #endif
  #ifdef LP_MAP
    // a painted decal (sign boards, plaques, ground decals): the texture's sRGB colour times the vertex colour, lit
    vec4 lpTex = texture2D( uMap, vMapUv );
    albedo *= lpLin( lpTex.rgb );
    #ifdef LP_ALPHA
      alpha = lpTex.a * uOpacity;
      if ( alpha < 0.004 ) discard;
    #endif
  #endif
  // emission code (LP.EMIT): a < 0.5 always-on, a ≥ 0.5 night-only (scaled by the mood's lpNight.x or the look's uLit)
  float ea = vColor.a;
  float emitNight = step( 0.5, ea );
  float emission = mix( ea / 0.498, max( ea - 0.502, 0.0 ) / 0.498 * ( uLit >= 0.0 ? uLit : lpNight.x ), emitNight );
  vec3 pos = - vViewPosition;
  vec3 viewDir = normalize( vViewPosition );
  vec3 sunC = vec3( 0.0 );     // sun / moon (shadowed)
  vec3 sunU = vec3( 0.0 );     // unshadowed, for the golden-hour share
  vec3 amb = ambientLightColor;
  vec3 points = vec3( 0.0 );
  #if NUM_DIR_LIGHTS > 0
  #pragma unroll_loop_start
  for ( int i = 0; i < NUM_DIR_LIGHTS; i ++ ) {
    {
      vec3 lpDc = directionalLights[ i ].color;
      #if UNROLLED_LOOP_INDEX == 0
        if ( lpSunC.a > 0.5 ) lpDc = lpSunC.rgb;
      #endif
      float lpR = lpSunRamp( n, directionalLights[ i ].direction );
      float lpS = 1.0;
      #if defined( USE_SHADOWMAP ) && ( UNROLLED_LOOP_INDEX < NUM_DIR_LIGHT_SHADOWS )
        if ( receiveShadow && lpShadow.x > 0.001 ) lpS = getShadow( directionalShadowMap[ i ], directionalLightShadows[ i ].shadowMapSize, directionalLightShadows[ i ].shadowBias, directionalLightShadows[ i ].shadowRadius, vDirectionalShadowCoord[ i ] );
      #endif
      sunU += lpDc * lpR;
      sunC += lpDc * lpR * mix( 1.0, lpS, lpShadow.x );
    }
  }
  #pragma unroll_loop_end
  #endif
  #if NUM_HEMI_LIGHTS > 0
  #pragma unroll_loop_start
  for ( int i = 0; i < NUM_HEMI_LIGHTS; i ++ ) {
    {
      float lpH = dot( n, hemisphereLights[ i ].direction ) * 0.5 + 0.5;
      vec3 lpSky = hemisphereLights[ i ].skyColor, lpGround = hemisphereLights[ i ].groundColor;
      #if UNROLLED_LOOP_INDEX == 0
        if ( lpSkyC.a > 0.5 ) { lpSky = lpSkyC.rgb; lpGround = lpGroundC.rgb; }
      #endif
      amb += mix( lpGround, lpSky, lpH );
    }
  }
  #pragma unroll_loop_end
  #endif
  #if NUM_POINT_LIGHTS > 0
  #pragma unroll_loop_start
  for ( int i = 0; i < NUM_POINT_LIGHTS; i ++ ) {
    {
      vec3 lpD = pointLights[ i ].position - pos;
      float lpD2 = max( dot( lpD, lpD ), 0.0001 );
      float lpRg = pointLights[ i ].distance > 0.0 ? pointLights[ i ].distance : 10.0;
      float lpAtt = clamp( 1.0 - lpD2 / ( lpRg * lpRg ), 0.0, 1.0 );
      lpAtt *= lpAtt;
      float lpPl = clamp( ( dot( n, lpD * inversesqrt( lpD2 ) ) + 0.6 ) / 1.6, 0.0, 1.0 );
      points += pointLights[ i ].color * ( lpAtt * lpPl );
    }
  }
  #pragma unroll_loop_end
  #endif
  vec3 base = sunC + amb;
  vec3 light = base + points;
  // night grade: under moon and sky light colours drift towards a cool blue-grey; lamplight keeps the full colour
  vec3 moonlit = albedo;
  if ( lpGrade.x > 0.0 ) moonlit = mix( albedo, dot( albedo, LV_LUMA ) * lpGrade.yzw, clamp( lpGrade.x, 0.0, 1.0 ) );
  vec3 col = moonlit * base;
  vec3 lampAlbedo = albedo;
  if ( lpWarmth.y > 0.0 || lpWarmth.z > 0.0 ) {
    lampAlbedo = mix( albedo, dot( albedo, LV_LUMA ) * LV_LAMP_AMBER, clamp( lpWarmth.y, 0.0, 1.0 ) );
    float pl = dot( points, LV_LUMA );
    float share = pl / max( pl + dot( base, LV_LUMA ), 0.0001 );
    float z = clamp( lpWarmth.z, 0.0, 1.0 );
    col = mix( col, vec3( dot( col, LV_LUMA ) ), clamp( z * share * 4.0, 0.0, 1.0 ) ) * ( 1.0 - 0.5 * z * share );
  }
  col += lampAlbedo * points;
  if ( lpWarmth.x > 0.0 ) {
    float sunShare = clamp( dot( sunU, LV_LUMA ) / max( dot( light, LV_LUMA ), 0.0001 ), 0.0, 1.0 );
    float cl = dot( col, LV_LUMA );
    col = max( cl + ( col - cl ) * ( 1.0 + lpWarmth.x * sunShare ), 0.0 );
  }
  float rim = pow( 1.0 - clamp( dot( n, viewDir ), 0.0, 1.0 ), max( lpRim.a, 0.5 ) );
  col += lpRim.rgb * rim * ( 0.22 * clamp( dot( light, LV_LUMA ), 0.0, 1.0 ) + uRimBoost );
  // night-only emitters (windows, lantern paper) glow a warm honey of their own luma; always-on ones keep their albedo
  float lpMx = max( max( albedo.r, albedo.g ), albedo.b );
  vec3 emitAlb = mix( albedo, LV_GLOW * max( lpMx, 0.35 ), 0.4 * emitNight );
  col = mix( col, emitAlb * ( 1.0 + lpMisc.x ), clamp( emission, 0.0, 1.0 ) );
  #ifdef USE_FOG
    float lpDist = length( vViewPosition );
    float lpFogK = clamp( ( lpDist - fogNear ) / max( fogFar - fogNear, 0.01 ), 0.0, 1.0 ) * lpFog.x * uFogScale;
    col = mix( col, lpFog.y > 0.5 ? lpLin( fogColor ) : fogColor, lpFogK );
  #endif
  col = mix( col, lpLin( uFlash.rgb ), clamp( uFlash.a, 0.0, 1.0 ) );
  gl_FragColor = vec4( lpEnc( col ), alpha );
}
`;

  const OUT_VERT = /* glsl */`
#include <common>
#include <skinning_pars_vertex>
#include <fog_pars_vertex>
${GLSL_COMMON}
attribute float lpWind;
attribute vec3 lpSmooth;
uniform float uWindScale;
uniform float uWidth;
varying float vDist;
void main() {
  vec3 objectNormal = lpSmooth;
  #include <skinbase_vertex>
  #include <skinnormal_vertex>
  #include <begin_vertex>
  #include <skinning_vertex>
  vec4 worldPosition = vec4( transformed, 1.0 );
  mat4 lpModel = modelMatrix;
  #ifdef USE_INSTANCING
    lpModel = modelMatrix * instanceMatrix;
  #endif
  worldPosition = lpModel * worldPosition;
  worldPosition.xyz = lpWindSway( worldPosition.xyz, lpWind * uWindScale );
  vec4 mvPosition = viewMatrix * worldPosition;
  vec4 pos = projectionMatrix * mvPosition;
  // lpSmooth's length = the vertex's ink width factor (1 on most geometry; 0 = no ink)
  float ink = min( length( objectNormal ), 1.0 );
  vec3 nw = ink > 0.02 ? normalize( mat3( lpModel ) * objectNormal ) : vec3( 0.0 );
  vec2 nc = ( projectionMatrix * viewMatrix * vec4( nw, 0.0 ) ).xy;
  float l = length( nc );
  if ( l > 0.00001 ) {
    float px = max( uWidth * lpView.z, lpView.w ) * ink;
    pos.xy += ( nc / l ) * ( px * 2.0 / lpView.xy ) * pos.w;
  }
  gl_Position = pos;
  vDist = length( mvPosition.xyz );
  #include <fog_vertex>
}
`;
  const OUT_FRAG = /* glsl */`
#include <common>
#include <fog_pars_fragment>
${GLSL_COMMON}
uniform vec4 lpFog;
uniform vec3 uInk;
uniform float uFogScale;
varying float vDist;
void main() {
  #ifdef LP_FADE
    if ( uFade < 0.999 && uFade <= lpBayer( gl_FragCoord.xy ) ) discard;
  #endif
  vec3 col = lpLin( uInk );
  #ifdef USE_FOG
    float k = clamp( ( vDist - fogNear ) / max( fogFar - fogNear, 0.01 ), 0.0, 1.0 ) * lpFog.x * uFogScale;
    col = mix( col, lpFog.y > 0.5 ? lpLin( fogColor ) : fogColor, k );
  #endif
  gl_FragColor = vec4( lpEnc( col ), 1.0 );
}
`;

  function withGlobals(u) { for (const k in G) u[k] = G[k]; return u; }
  const MATS = new Map();
  const toRGB = c => (c == null ? null : Array.isArray(c) ? c : c.isColor ? [c.r, c.g, c.b] : typeof c === 'number' ? [((c >> 16) & 255) / 255, ((c >> 8) & 255) / 255, (c & 255) / 255] : P.hex(c));
  LP.toRGB = toRGB;
  /**
   * The LowPoly material, shared per option set: { side, tint, rim, fogScale, windScale, flash: [r,g,b,a], lit (−1 = the
   * mood decides, 0, 1), detail + planar + detailK (painted planar detail), map (decal texture), alpha + opacity
   * (transparent decal: alpha from the map), depthWrite, polygonOffset }. Never modify a shared material: per-object
   * looks go through LP.setLook, per-instance tints through instanceColor.
   */
  LP.material = function (opts) {
    opts = opts || {};
    const side = opts.side != null ? opts.side : THREE.FrontSide;
    const tint = toRGB(opts.tint) || [1, 1, 1];
    const fogScale = opts.fogScale != null ? opts.fogScale : 1, windScale = opts.windScale != null ? opts.windScale : 1;
    const lit = opts.lit === true ? 1 : opts.lit === false ? 0 : opts.lit != null ? +opts.lit : -1;
    const alpha = !!opts.alpha && !!opts.map, opacity = opts.opacity != null ? opts.opacity : 1;
    const key = ['lp', side, tint.join(','), opts.rim || 0, fogScale, windScale, opts.flash ? opts.flash.join(',') : '', lit,
      opts.detail ? opts.detail.uuid + ':' + (opts.planar || 0.25) + ':' + (opts.detailK != null ? opts.detailK : 1) : '', opts.map ? opts.map.uuid : '',
      alpha ? 'a' + opacity : '', opts.depthWrite === false ? 'nodw' : '', opts.polygonOffset || '', opts.fade ? 'fade' : ''].join('|');
    let m = MATS.get(key);
    if (m) return m;
    const tl = P.lin(tint), flash = opts.flash || [1, 1, 1, 0];
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.lights, THREE.UniformsLib.fog, {
      uTint: { value: new THREE.Vector3(tl[0], tl[1], tl[2]) }, uFlash: { value: new THREE.Vector4(flash[0], flash[1], flash[2], flash[3] || 0) },
      uRimBoost: { value: opts.rim || 0 }, uFogScale: { value: fogScale }, uWindScale: { value: windScale }, uFade: { value: 1 }, uLit: { value: lit },
    }]);
    if (opts.detail) { u.uDetail = { value: opts.detail }; u.uPlanar = { value: opts.planar || 0.25 }; u.uDetailK = { value: opts.detailK != null ? opts.detailK : 1 }; }
    if (opts.map) u.uMap = { value: opts.map };
    if (alpha) u.uOpacity = { value: opacity };
    m = new THREE.ShaderMaterial({ uniforms: withGlobals(u), vertexShader: LP_VERT, fragmentShader: LP_FRAG, lights: true, fog: true, vertexColors: true, side });
    m.defines = {};
    if (opts.detail) m.defines.LP_DETAIL = 1;
    if (opts.map) m.defines.LP_MAP = 1;
    if (alpha) m.defines.LP_ALPHA = 1;
    if (opts.fade) m.defines.LP_FADE = 1;
    if (alpha) { m.transparent = true; m.depthWrite = false; }
    if (opts.depthWrite === false) m.depthWrite = false;
    if (opts.polygonOffset) { m.polygonOffset = true; m.polygonOffsetFactor = opts.polygonOffset; m.polygonOffsetUnits = opts.polygonOffset; }
    m.name = 'LP LowPoly';
    m.userData.lpKind = 'lit';
    m.userData.lpBase = { tint: [tl[0], tl[1], tl[2]], flash: [flash[0], flash[1], flash[2], flash[3] || 0], rim: opts.rim || 0, fog: fogScale, wind: windScale, lit, opacity };
    m.userData.lpOpts = Object.assign({}, opts);
    m.userData.lpFade = !!opts.fade;
    MATS.set(key, m);
    return m;
  };
  /** Ink outline: inverted hull of constant pixel width along the smoothed normal (BackSide). { color, width, windScale, fogScale } */
  LP.outlineMaterial = function (opts) {
    opts = opts || {};
    const ink = toRGB(opts.color) || LP.INK;
    const width = opts.width != null ? opts.width : 2.2;
    const windScale = opts.windScale != null ? opts.windScale : 1, fogScale = opts.fogScale != null ? opts.fogScale : 1;
    const key = ['out', ink.join(','), width, windScale, fogScale, opts.fade ? 'fade' : ''].join('|');
    let m = MATS.get(key);
    if (m) return m;
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.fog, {
      uInk: { value: new THREE.Vector3(ink[0], ink[1], ink[2]) }, uWidth: { value: width }, uWindScale: { value: windScale }, uFogScale: { value: fogScale }, uFade: { value: 1 },
    }]);
    m = new THREE.ShaderMaterial({ uniforms: withGlobals(u), vertexShader: OUT_VERT, fragmentShader: OUT_FRAG, fog: true, side: THREE.BackSide });
    if (opts.fade) m.defines = { LP_FADE: 1 };
    m.name = 'LP Outline';
    m.userData.lpKind = 'outline';
    m.userData.lpBase = { ink: ink.slice(), width, wind: windScale, fog: fogScale };
    m.userData.lpOpts = Object.assign({}, opts);
    m.userData.lpFade = !!opts.fade;
    MATS.set(key, m);
    return m;
  };
  /** Default ink colour (Materials3D.Ink). */
  LP.INK = [0.14, 0.10, 0.13];
  /** Gold ink for hovered / selected things (Lanternvale's highlight outline). */
  LP.INK_GOLD = [0.98, 0.78, 0.3];

  // ------------------------------------------------------------ looks --
  // Per-object looks on shared materials (Lanternvale's Materials3D.Look / MaterialPropertyBlock): an object with a look
  // writes its values into the shared material's uniforms just before it draws (onBeforeRender, uniformsNeedUpdate) and
  // puts the material's base values back right after (onAfterRender), so objects without a look need no hook at all.
  const _lin3 = [0, 0, 0];
  function linInto(c) { const l = P.lin(c); _lin3[0] = l[0]; _lin3[1] = l[1]; _lin3[2] = l[2]; return _lin3; }
  function lookRGB(look, field) {
    const src = look[field];
    if (src == null) return null;
    const cache = look.__c || (look.__c = {});
    if (cache[field] !== src) { cache[field] = src; cache[field + '_v'] = toRGB(src); }
    return cache[field + '_v'];
  }
  function applyLook(mat, look) {
    const u = mat.uniforms, b = mat.userData.lpBase;
    if (!b) return;
    if (mat.userData.lpKind === 'outline') {
      const ink = (look && lookRGB(look, 'ink')) || b.ink;
      u.uInk.value.set(ink[0], ink[1], ink[2]);
      u.uWidth.value = b.width * (look && look.inkWidth != null ? look.inkWidth : 1);
      u.uWindScale.value = b.wind * (look && look.windScale != null ? look.windScale : 1);
      u.uFogScale.value = b.fog * (look && look.fogScale != null ? look.fogScale : 1);
      u.uFade.value = look && look.fade != null ? look.fade : 1;
    } else {
      const t = look && lookRGB(look, 'tint');
      if (t) { const l = linInto(t); u.uTint.value.set(b.tint[0] * l[0], b.tint[1] * l[1], b.tint[2] * l[2]); } else u.uTint.value.set(b.tint[0], b.tint[1], b.tint[2]);
      const fa = look && look.flash ? +look.flash : 0;
      if (fa > 0) { const fc = lookRGB(look, 'flashColor') || [1, 1, 1]; u.uFlash.value.set(fc[0], fc[1], fc[2], fa); } else u.uFlash.value.set(b.flash[0], b.flash[1], b.flash[2], b.flash[3]);
      u.uRimBoost.value = b.rim + (look && look.rim ? look.rim : 0);
      u.uFogScale.value = b.fog * (look && look.fogScale != null ? look.fogScale : 1);
      u.uWindScale.value = b.wind * (look && look.windScale != null ? look.windScale : 1);
      u.uFade.value = look && look.fade != null ? look.fade : 1;
      u.uLit.value = look && look.lit != null ? (look.lit === true ? 1 : look.lit === false ? 0 : +look.lit) : b.lit;
      if (u.uOpacity) u.uOpacity.value = b.opacity * (look && look.opacity != null ? look.opacity : 1);
    }
    mat.uniformsNeedUpdate = true;
  }
  function lookBefore(r, s, c, g, mat) { if (mat && mat.userData.lpBase) applyLook(mat, this.userData.lpLook); }
  function lookAfter(r, s, c, g, mat) { if (mat && mat.userData.lpBase) applyLook(mat, null); }
  const NOOP_RENDER = THREE.Object3D.prototype.onBeforeRender;
  /**
   * Sets (or with null clears) a look on an object and every LP mesh under it, outlines included:
   * { tint: [r,g,b] | '#hex' (multiplies), flash: 0..1, flashColor, fade: 0..1 (dither dissolve; 1 = solid), rim: boost,
   *   fogScale, windScale, lit: true | false | 0..1 (night-only emitters), opacity, ink: colour, inkWidth: × }.
   * The same look object is shared by the whole subtree; change its fields at any time (they are read when it draws).
   */
  /** The dither-dissolve twin of an LP material (same look, compiled with LP_FADE): only objects whose look fades use it. */
  function fadeTwin(m) {
    if (m.userData.lpFade) return m;
    if (!m.userData.fadeTwin) {
      const o = Object.assign({}, m.userData.lpOpts || {}, { fade: true });
      m.userData.fadeTwin = m.userData.lpKind === 'outline' ? LP.outlineMaterial(o) : LP.material(o);
    }
    return m.userData.fadeTwin;
  }
  LP.setLook = function (obj, look) {
    const fades = !!look && look.fade != null;
    obj.traverse(o => {
      if (!o.material || !o.material.userData || !o.material.userData.lpBase || o.userData.lpNoLook) return;
      o.userData.lpLook = look || null;
      o.onBeforeRender = look ? lookBefore : NOOP_RENDER;
      o.onAfterRender = look ? lookAfter : NOOP_RENDER;
      // a fading look draws with the LP_FADE twin (the dissolve's discard stays out of every other draw)
      if (fades && !o.material.userData.lpFade && !o.material.userData.lpOpts.material) { o.userData.lpPlainMat = o.material; o.material = fadeTwin(o.material); }
      else if (!fades && o.userData.lpPlainMat) { o.material = o.userData.lpPlainMat; o.userData.lpPlainMat = null; }
    });
    return look || null;
  };
  /** The look an object carries (or null). */
  LP.getLook = obj => (obj && obj.userData.lpLook) || null;

  // ------------------------------------------------------------ meshes --
  /** Mesh with the LowPoly material and (opts.outline !== false) an ink-outline child sharing the geometry. opts.look: a look. */
  LP.mesh = function (geo, opts) {
    opts = opts || {};
    const mesh = new THREE.Mesh(geo, opts.material || LP.material(opts));
    mesh.castShadow = opts.castShadow !== false;
    mesh.receiveShadow = opts.receiveShadow !== false;
    if (opts.outline !== false) {
      const o = new THREE.Mesh(geo, LP.outlineMaterial(opts.outlineOpts || {}));
      o.castShadow = false; o.receiveShadow = false; o.name = 'outline';
      o.raycast = () => {};
      mesh.add(o);
      mesh.userData.outline = o;
    }
    mesh.userData.tris = geo.userData.tris || 0;
    if (opts.look) LP.setLook(mesh, opts.look);
    return mesh;
  };
  /** SkinnedMesh bound to an existing skeleton (bind matrix identity: geometry authored in the skeleton's bind pose). */
  LP.skinnedMesh = function (geo, skeleton, opts) {
    opts = opts || {};
    const mesh = new THREE.SkinnedMesh(geo, opts.material || LP.material(opts));
    mesh.bind(skeleton, new THREE.Matrix4());
    mesh.castShadow = opts.castShadow !== false;
    mesh.receiveShadow = opts.receiveShadow !== false;
    mesh.frustumCulled = false;
    if (opts.outline !== false) {
      const o = new THREE.SkinnedMesh(geo, LP.outlineMaterial(opts.outlineOpts || {}));
      o.bind(skeleton, new THREE.Matrix4());
      o.castShadow = false; o.receiveShadow = false; o.frustumCulled = false; o.name = 'outline';
      o.raycast = () => {};
      mesh.add(o);
      mesh.userData.outline = o;
    }
    mesh.userData.tris = geo.userData.tris || 0;
    if (opts.look) LP.setLook(mesh, opts.look);
    return mesh;
  };
  /** Near / far levels (THREE.LOD; the renderer picks the level by camera distance). */
  LP.lod = function (near, far, distance = 60) {
    const l = new THREE.LOD();
    l.addLevel(near, 0);
    if (far) l.addLevel(far, distance);
    l.name = 'LP lod';
    return l;
  };
  /** Triangles of an object (LP meshes, outlines not counted). */
  LP.trisOf = function (obj) {
    let t = 0;
    obj.traverse(m => {
      if (!m.isMesh || m.name === 'outline' || !m.geometry) return;
      const g = m.geometry, n = g.userData.tris || (g.index ? g.index.count / 3 : g.attributes.position.count / 3);
      t += m.isInstancedMesh ? n * m.count : n;
    });
    return t;
  };

  // ======================================================== instancing ==
  const _im = new THREE.Matrix4(), _iq = new THREE.Quaternion(), _ip = new THREE.Vector3(), _is = new THREE.Vector3(), _ie = new THREE.Euler(), _ic = new THREE.Color();
  /**
   * InstancedMesh with the LowPoly material and (opts.outline !== false) an instanced ink hull as its child, sharing the
   * same instanceMatrix (one upload, two draws for any number of copies). opts: the LP.material options plus { outline,
   * outlineOpts, castShadow, receiveShadow, tints: true (per-instance tint via instanceColor), dynamic: true }.
   * Fill with LP.setInstance(...) and finish with LP.commitInstances(mesh, count).
   */
  LP.instances = function (geo, capacity, opts) {
    opts = opts || {};
    const im = new THREE.InstancedMesh(geo, opts.material || LP.material(opts), capacity);
    im.count = 0;
    im.castShadow = opts.castShadow !== false;
    im.receiveShadow = opts.receiveShadow !== false;
    if (opts.dynamic) im.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
    if (opts.tints) {
      const a = new Float32Array(capacity * 3); a.fill(1);
      im.instanceColor = new THREE.InstancedBufferAttribute(a, 3);
      if (opts.dynamic) im.instanceColor.setUsage(THREE.DynamicDrawUsage);
    }
    if (opts.outline !== false) {
      const o = new THREE.InstancedMesh(geo, LP.outlineMaterial(opts.outlineOpts || {}), capacity);
      o.instanceMatrix = im.instanceMatrix;
      o.count = 0;
      o.castShadow = false; o.receiveShadow = false; o.name = 'outline';
      o.raycast = () => {};
      im.add(o);
      im.userData.outline = o;
    }
    im.userData.tris = geo.userData.tris || 0;
    im.name = opts.name || 'LP instances';
    return im;
  };
  /** Places copy i: position, yaw (radians), uniform or [x, y, z] scale, optional tint ([r,g,b] sRGB multiplier / '#hex'). */
  LP.setInstance = function (im, i, x, y, z, yaw, scale, tint) {
    _ie.set(0, yaw || 0, 0); _iq.setFromEuler(_ie);
    const s = scale == null ? 1 : scale;
    if (Array.isArray(s)) _is.set(s[0], s[1], s[2]); else _is.set(s, s, s);
    _im.compose(_ip.set(x, y, z), _iq, _is);
    im.setMatrixAt(i, _im);
    if (tint != null && im.instanceColor) { const t = toRGB(tint); im.instanceColor.setXYZ(i, t[0], t[1], t[2]); }
  };
  /** Copy i from a THREE.Matrix4 (and an optional tint). */
  LP.setInstanceMatrix = function (im, i, m, tint) {
    im.setMatrixAt(i, m);
    if (tint != null && im.instanceColor) { const t = toRGB(tint); im.instanceColor.setXYZ(i, t[0], t[1], t[2]); }
  };
  /** After placing: uploads, sets the count (default: unchanged) on the mesh and its outline, refreshes the culling bounds. */
  LP.commitInstances = function (im, count) {
    if (count != null) im.count = count;
    im.instanceMatrix.needsUpdate = true;
    if (im.instanceColor) im.instanceColor.needsUpdate = true;
    im.boundingSphere = null; im.boundingBox = null;
    const o = im.userData.outline;
    if (o) { o.count = im.count; o.boundingSphere = null; o.boundingBox = null; }
    return im;
  };

  /**
   * Scatter: many copies of a few geometries (trees, bushes, rocks, fences, crowds of props) as instanced meshes with
   * instanced outlines — one draw (+ one ink draw) per geometry per chunk. opts: { chunk: metres (0 = one set per
   * geometry; > 0 splits copies into square chunks so off-screen chunks are culled; 48–80 m suits a battle map), lodDist:
   * metres (chunks farther than this from the camera use the far geometry), name }.
   *   sc.add(geo | { near, far }, x, y, z, yaw, scale, tint, o)   o: { outline (true), farOutline (false), farShadow (false),
   *                                                                 castShadow (true),
   *                                                                 receiveShadow (true), tints (auto) }
   *   sc.addMatrix(geo, matrix4, tint, o) · sc.build() → THREE.Group (sc.group) · sc.update(camera) (LOD; call per frame
   *   when far geometries are used) · sc.dispose() · sc.stats → { copies, meshes, sets }
   */
  class Scatter {
    constructor(opts) {
      opts = opts || {};
      this.group = new THREE.Group();
      this.group.name = opts.name || 'LP scatter';
      this.chunk = opts.chunk || 0;
      this.lodDist = opts.lodDist || 70;
      this.sets = new Map();
      this.chunks = [];
      this.meshes = [];
      this.stats = { copies: 0, meshes: 0, sets: 0 };
    }
    add(geo, x, y, z, yaw, scale, tint, o) {
      _ie.set(0, yaw || 0, 0); _iq.setFromEuler(_ie);
      const s = scale == null ? 1 : scale;
      if (Array.isArray(s)) _is.set(s[0], s[1], s[2]); else _is.set(s, s, s);
      _im.compose(_ip.set(x, y, z), _iq, _is);
      return this.addMatrix(geo, _im, tint, o);
    }
    addMatrix(geo, m, tint, o) {
      o = o || {};
      const near = geo.isBufferGeometry ? geo : geo.near, far = geo.isBufferGeometry ? null : geo.far || null;
      if (!near) return this;
      const outline = o.outline !== false, cast = o.castShadow !== false, recv = o.receiveShadow !== false;
      const key = near.uuid + '|' + (far ? far.uuid : '') + '|' + (outline ? 1 : 0) + (cast ? 1 : 0) + (recv ? 1 : 0) + (o.farOutline ? 1 : 0) + '|' + (o.material ? o.material.uuid : '');
      let set = this.sets.get(key);
      if (!set) { set = { near, far, outline, cast, recv, farOutline: !!o.farOutline, farShadow: !!o.farShadow, material: o.material || null, items: [], tinted: false }; this.sets.set(key, set); }
      const t = tint != null ? toRGB(tint) : null;
      if (t) set.tinted = true;
      set.items.push({ e: m.elements.slice(), t });
      this.stats.copies++;
      return this;
    }
    build() {
      for (const set of this.sets.values()) {
        const groups = new Map();
        for (const it of set.items) {
          const k = this.chunk > 0 ? Math.floor(it.e[12] / this.chunk) + ',' + Math.floor(it.e[14] / this.chunk) : '0';
          let g = groups.get(k);
          if (!g) { g = []; groups.set(k, g); }
          g.push(it);
        }
        for (const list of groups.values()) {
          const mk = (geo, outline) => {
            const im = LP.instances(geo, list.length, { outline, tints: set.tinted, castShadow: set.cast, receiveShadow: set.recv, material: set.material });
            list.forEach((it, i) => { _im.fromArray(it.e); im.setMatrixAt(i, _im); if (set.tinted) { const t = it.t || [1, 1, 1]; im.instanceColor.setXYZ(i, t[0], t[1], t[2]); } });
            LP.commitInstances(im, list.length);
            im.computeBoundingSphere();
            if (im.userData.outline) im.userData.outline.boundingSphere = im.boundingSphere;
            this.group.add(im);
            this.meshes.push(im);
            this.stats.meshes++;
            return im;
          };
          const nearM = mk(set.near, set.outline);
          if (set.far) {
            const farM = mk(set.far, set.farOutline);
            farM.castShadow = !!set.farShadow;
            farM.visible = false;
            this.chunks.push({ center: nearM.boundingSphere.center.clone(), radius: nearM.boundingSphere.radius, near: nearM, far: farM, isFar: false });
          }
        }
      }
      this.stats.sets = this.sets.size;
      this.sets.clear();
      return this.group;
    }
    /** LOD: chunks farther than lodDist (+ hysteresis) from the camera show their far geometry. */
    update(camera) {
      if (!this.chunks.length || !camera) return;
      const cp = camera.getWorldPosition ? camera.getWorldPosition(_ip) : camera;
      const d0 = this.lodDist;
      for (const c of this.chunks) {
        _is.copy(c.center).applyMatrix4(this.group.matrixWorld);
        const d = Math.max(0, _is.distanceTo(cp) - c.radius * 0.5);
        const far = c.isFar ? d > d0 * 0.9 : d > d0 * 1.1;
        if (far !== c.isFar) { c.isFar = far; c.near.visible = !far; c.far.visible = far; }
      }
    }
    dispose() {
      for (const m of this.meshes) { m.dispose(); if (m.userData.outline) m.userData.outline.dispose(); }
      if (this.group.parent) this.group.parent.remove(this.group);
      this.meshes = []; this.chunks = [];
    }
  }
  LP.Scatter = Scatter;

  // ======================================================= static merge ==
  const _nm3 = new THREE.Matrix3();
  /**
   * Merges LP geometries (each placed by a THREE.Matrix4, optionally tinted [r,g,b]) into one compact non-indexed
   * geometry with the same attributes (position, normal, color, lpWind, lpSmooth). Mirrored placements keep their
   * winding. Geometries with uv (decals) or skinning cannot be merged (skipped).
   */
  LP.mergeGeometries = function (entries) {
    let n = 0;
    const list = entries.filter(e => e.geo && e.geo.attributes.position && e.geo.attributes.lpSmooth && !e.geo.attributes.uv && !e.geo.attributes.skinIndex && !e.geo.index);
    for (const e of list) n += e.geo.attributes.position.count;
    const pos = new Float32Array(n * 3), nor = new Int8Array(n * 3), col = new Uint8Array(n * 4), wind = new Float32Array(n), sm = new Int8Array(n * 3);
    const v = new THREE.Vector3();
    let o = 0, tris = 0;
    for (const e of list) {
      const g = e.geo, A = g.attributes, cnt = A.position.count, m = e.matrix || _im.identity();
      _nm3.getNormalMatrix(m);
      const flip = m.determinant() < 0, t = e.tint ? toRGB(e.tint) : null;
      const rd = (attr, i, k) => attr.normalized ? (attr.array instanceof Int8Array ? Math.max(attr.array[i * attr.itemSize + k] / 127, -1) : attr.array[i * attr.itemSize + k] / 255) : attr.array[i * attr.itemSize + k];
      for (let i = 0; i < cnt; i++) {
        // mirrored: swap the 2nd and 3rd vertex of each triangle
        const src = flip ? (i % 3 === 1 ? i + 1 : i % 3 === 2 ? i - 1 : i) : i;
        const d = o + i;
        v.set(A.position.array[src * 3], A.position.array[src * 3 + 1], A.position.array[src * 3 + 2]).applyMatrix4(m);
        pos[d * 3] = v.x; pos[d * 3 + 1] = v.y; pos[d * 3 + 2] = v.z;
        v.set(rd(A.normal, src, 0), rd(A.normal, src, 1), rd(A.normal, src, 2)).applyMatrix3(_nm3).normalize();
        nor[d * 3] = Math.round(v.x * 127); nor[d * 3 + 1] = Math.round(v.y * 127); nor[d * 3 + 2] = Math.round(v.z * 127);
        v.set(rd(A.lpSmooth, src, 0), rd(A.lpSmooth, src, 1), rd(A.lpSmooth, src, 2));
        const len = Math.min(1, v.length());
        v.applyMatrix3(_nm3); if (v.lengthSq() > 1e-12) v.normalize().multiplyScalar(len);
        sm[d * 3] = Math.round(v.x * 127); sm[d * 3 + 1] = Math.round(v.y * 127); sm[d * 3 + 2] = Math.round(v.z * 127);
        for (let k = 0; k < 4; k++) {
          let c = rd(A.color, src, k);
          if (t && k < 3) c *= t[k];
          col[d * 4 + k] = Math.round(clamp01(c) * 255);
        }
        wind[d] = A.lpWind ? A.lpWind.array[src] : 0;
      }
      o += cnt;
      tris += cnt / 3;
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('normal', new THREE.BufferAttribute(nor, 3, true));
    g.setAttribute('color', new THREE.BufferAttribute(col, 4, true));
    g.setAttribute('lpWind', new THREE.BufferAttribute(wind, 1));
    g.setAttribute('lpSmooth', new THREE.BufferAttribute(sm, 3, true));
    g.computeBoundingBox(); g.computeBoundingSphere();
    g.userData.tris = tris;
    g.userData.lp = true;
    return g;
  };

  /**
   * StaticBatch: unique static props (city buildings, walls, market clutter) merged per square chunk into one inked mesh
   * and one plain mesh (per shadow setting): a handful of draws per chunk whatever the prop count, culled per chunk.
   * Use LP.Scatter instead for many copies of the same geometry (trees): merging copies costs memory, instancing does not.
   *   const sb = new LP.StaticBatch({ chunk: 32 }); sb.add(geo, matrix4, { outline, castShadow, receiveShadow, tint });
   *   sb.addAt(geo, x, y, z, yaw, scale, o); sb.addObject(obj) (an LP mesh / group: its LP meshes, at their world matrices);
   *   const group = sb.build();
   */
  class StaticBatch {
    constructor(opts) {
      opts = opts || {};
      this.chunk = opts.chunk || 32;
      this.group = new THREE.Group();
      this.group.name = opts.name || 'LP static batch';
      this.bins = new Map();
      this.extra = [];
      this.stats = { items: 0, meshes: 0, tris: 0 };
    }
    add(geo, matrix, o) {
      o = o || {};
      if (!geo) return this;
      const m = matrix ? matrix.clone() : new THREE.Matrix4();
      if (geo.attributes.uv || geo.attributes.skinIndex || geo.index || !geo.attributes.lpSmooth) {
        const mesh = LP.mesh(geo, { outline: false, castShadow: false, map: o.map, material: o.material });
        mesh.matrixAutoUpdate = false; mesh.matrix.copy(m);
        this.extra.push(mesh);
        return this;
      }
      const cx = Math.floor(m.elements[12] / this.chunk), cz = Math.floor(m.elements[14] / this.chunk);
      const key = cx + ',' + cz + '|' + (o.outline === false ? 'p' : 'i') + (o.castShadow === false ? 0 : 1) + (o.receiveShadow === false ? 0 : 1);
      let bin = this.bins.get(key);
      if (!bin) { bin = { outline: o.outline !== false, cast: o.castShadow !== false, recv: o.receiveShadow !== false, list: [] }; this.bins.set(key, bin); }
      bin.list.push({ geo, matrix: m, tint: o.tint || null });
      this.stats.items++;
      return this;
    }
    addAt(geo, x, y, z, yaw, scale, o) {
      _ie.set(0, yaw || 0, 0); _iq.setFromEuler(_ie);
      const s = scale == null ? 1 : scale;
      if (Array.isArray(s)) _is.set(s[0], s[1], s[2]); else _is.set(s, s, s);
      return this.add(geo, _im.compose(_ip.set(x, y, z), _iq, _is), o);
    }
    /** Adds the LP meshes of an object tree (outline children are implied by their parents). */
    addObject(obj, o) {
      obj.updateMatrixWorld(true);
      obj.traverse(m => {
        if (!m.isMesh || m.isInstancedMesh || m.isSkinnedMesh || m.name === 'outline' || !m.geometry || !m.material || !m.material.userData.lpBase) return;
        if (m.material.transparent || m.geometry.attributes.uv) {
          const c = m.clone(); c.matrixAutoUpdate = false; c.matrix.copy(m.matrixWorld); this.extra.push(c); return;
        }
        this.add(m.geometry, m.matrixWorld, Object.assign({ outline: !!m.userData.outline, castShadow: m.castShadow, receiveShadow: m.receiveShadow }, o || {}));
      });
      return this;
    }
    build() {
      for (const bin of this.bins.values()) {
        const geo = LP.mergeGeometries(bin.list);
        const mesh = LP.mesh(geo, { outline: bin.outline, castShadow: bin.cast, receiveShadow: bin.recv });
        mesh.matrixAutoUpdate = false;
        mesh.name = 'LP batch chunk';
        this.group.add(mesh);
        this.stats.meshes++;
        this.stats.tris += geo.userData.tris;
      }
      for (const m of this.extra) this.group.add(m);
      this.bins.clear();
      return this.group;
    }
    dispose() {
      this.group.traverse(m => { if (m.isMesh && m.name === 'LP batch chunk') m.geometry.dispose(); });
      if (this.group.parent) this.group.parent.remove(this.group);
    }
  }
  LP.StaticBatch = StaticBatch;

  // ------------------------------------------------------ blob shadow --
  const SHADOW_VERT = /* glsl */`
#include <common>
#include <fog_pars_vertex>
varying vec2 vUv;
varying float vK;
#ifdef USE_INSTANCING
  attribute float lpStrength;
#endif
void main() {
  vUv = uv;
  vK = 1.0;
  vec4 p = vec4( position, 1.0 );
  #ifdef USE_INSTANCING
    p = instanceMatrix * p;
    vK = lpStrength;
  #endif
  vec4 mvPosition = modelViewMatrix * p;
  gl_Position = projectionMatrix * mvPosition;
  #include <fog_vertex>
}
`;
  const SHADOW_FRAG = /* glsl */`
#include <common>
#include <fog_pars_fragment>
uniform vec4 uColor;
uniform float uSoftness;
varying vec2 vUv;
varying float vK;
void main() {
  float d = length( vUv * 2.0 - 1.0 );
  float a = 1.0 - smoothstep( 1.0 - uSoftness, 1.0, d );
  a *= uColor.a * vK;
  #ifdef USE_FOG
    a *= 1.0 - smoothstep( fogNear, fogFar, vFogDepth );
  #endif
  gl_FragColor = vec4( uColor.rgb, a );
}
`;
  const SHADOW_GEO = (() => { const g = new THREE.PlaneGeometry(1, 1); g.rotateX(-Math.PI / 2); return g; })();
  const SHADOW_MATS = new Map();
  function shadowMaterial(strength, softness) {
    const key = strength.toFixed(3) + '|' + softness.toFixed(3);
    let m = SHADOW_MATS.get(key);
    if (m) return m;
    m = new THREE.ShaderMaterial({
      uniforms: THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uColor: { value: new THREE.Vector4(0.12, 0.09, 0.16, strength) }, uSoftness: { value: softness } }]),
      vertexShader: SHADOW_VERT, fragmentShader: SHADOW_FRAG, transparent: true, depthWrite: false, fog: true,
      polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2,
    });
    m.name = 'LP blob shadow';
    SHADOW_MATS.set(key, m);
    return m;
  }
  /** Soft contact (blob) shadow lying on y = 0.01: radii rx × rz metres (shared material per strength / softness). */
  LP.blobShadow = function (rx, rz, strength = 0.42, softness = 0.65) {
    const s = new THREE.Mesh(SHADOW_GEO, shadowMaterial(strength, softness));
    s.scale.set(rx * 2, 1, rz * 2);
    s.position.y = 0.01;
    s.renderOrder = 1;
    s.castShadow = false; s.receiveShadow = false;
    s.name = 'blobShadow';
    s.userData.lpNoLook = true;
    s.raycast = () => {};
    return s;
  };
  /**
   * Many blob shadows in one draw (crowds, armies, scattered props): bs = new LP.BlobShadows(capacity, { softness });
   * bs.set(i, x, y, z, rx, rz, strength 0..1) · bs.commit(count) · bs.mesh (add it to the scene).
   */
  class BlobShadows {
    constructor(capacity, opts) {
      opts = opts || {};
      this.capacity = capacity;
      const m = shadowMaterial(1, opts.softness != null ? opts.softness : 0.6);
      this.mesh = new THREE.InstancedMesh(SHADOW_GEO, m, capacity);
      this.strength = new THREE.InstancedBufferAttribute(new Float32Array(capacity), 1);
      if (opts.dynamic !== false) { this.mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage); this.strength.setUsage(THREE.DynamicDrawUsage); }
      // the strength rides on the shared plane geometry as a per-instance attribute: give each batch its own geometry
      this.geo = SHADOW_GEO.clone();
      this.geo.setAttribute('lpStrength', this.strength);
      this.mesh.geometry = this.geo;
      this.mesh.count = 0;
      this.mesh.renderOrder = 1;
      this.mesh.castShadow = false; this.mesh.receiveShadow = false;
      this.mesh.frustumCulled = opts.frustumCulled === true;
      this.mesh.name = 'LP blob shadows';
      this.mesh.raycast = () => {};
    }
    set(i, x, y, z, rx, rz, strength = 0.42) {
      _im.makeScale(rx * 2, 1, rz * 2); _im.setPosition(x, (y || 0) + 0.01, z);
      this.mesh.setMatrixAt(i, _im);
      this.strength.array[i] = strength;
    }
    commit(count) {
      if (count != null) this.mesh.count = Math.min(count, this.capacity);
      this.mesh.instanceMatrix.needsUpdate = true;
      this.strength.needsUpdate = true;
      this.mesh.boundingSphere = null;
    }
    dispose() { this.geo.dispose(); this.mesh.dispose(); if (this.mesh.parent) this.mesh.parent.remove(this.mesh); }
  }
  LP.BlobShadows = BlobShadows;

  // ------------------------------------------------- painted detail --
  let grassTex = null;
  /**
   * A tileable painted-grass detail (for LP.material({ detail, planar })): short blade strokes, lighter warm and darker
   * cool, on a neutral 0.5 field (the shader multiplies the vertex colour by texel × 2), so the ground keeps its
   * vertex-coloured meadow / road colours and gains Lanternvale's brushy texture.
   */
  LP.grassDetail = function (size = 512, seed = 3) {
    if (grassTex) return grassTex;
    const c = document.createElement('canvas'); c.width = c.height = size;
    const x = c.getContext('2d');
    x.fillStyle = 'rgb(128,128,128)'; x.fillRect(0, 0, size, size);
    let r = (seed * 2654435761) >>> 0;
    const rnd = () => { r ^= r << 13; r >>>= 0; r ^= r >>> 17; r ^= r << 5; r >>>= 0; return (r & 0xffffff) / 16777216; };
    // soft blotches
    for (let i = 0; i < 70; i++) {
      const px = rnd() * size, py = rnd() * size, rad = 40 + rnd() * 90, v = rnd() < 0.5 ? 106 : 146;
      for (const ox of [-size, 0, size]) for (const oy of [-size, 0, size]) {
        const g = x.createRadialGradient(px + ox, py + oy, 0, px + ox, py + oy, rad);
        g.addColorStop(0, `rgba(${v},${v + 2},${v - 4},0.35)`); g.addColorStop(1, `rgba(${v},${v},${v},0)`);
        x.fillStyle = g; x.fillRect(px + ox - rad, py + oy - rad, rad * 2, rad * 2);
      }
    }
    // blade strokes
    x.lineCap = 'round';
    for (let i = 0; i < 1100; i++) {
      const px = rnd() * size, py = rnd() * size, len = 18 + rnd() * 34, a = -Math.PI / 2 + (rnd() - 0.5) * 1.2;
      const light = rnd() < 0.55;
      x.strokeStyle = light ? `rgba(${168 + rnd() * 20 | 0},${168 + rnd() * 16 | 0},${118 + rnd() * 12 | 0},0.6)` : `rgba(${84 + rnd() * 14 | 0},${96 + rnd() * 14 | 0},${98 + rnd() * 12 | 0},0.55)`;
      x.lineWidth = 2.5 + rnd() * 3;
      for (const ox of [-size, 0, size]) for (const oy of [-size, 0, size]) {
        x.beginPath(); x.moveTo(px + ox, py + oy); x.quadraticCurveTo(px + ox + Math.cos(a) * len * 0.5 + 2, py + oy + Math.sin(a) * len * 0.5, px + ox + Math.cos(a) * len, py + oy + Math.sin(a) * len); x.stroke();
      }
    }
    grassTex = new THREE.CanvasTexture(c);
    grassTex.wrapS = grassTex.wrapT = THREE.RepeatWrapping;
    grassTex.anisotropy = 4;
    grassTex.colorSpace = THREE.NoColorSpace;
    return grassTex;
  };

  // ------------------------------------------------------ glow halo --
  let haloTex = null;
  function haloTexture() {
    if (haloTex) return haloTex;
    const c = document.createElement('canvas'); c.width = c.height = 64;
    const x = c.getContext('2d'), g = x.createRadialGradient(32, 32, 0, 32, 32, 32);
    g.addColorStop(0, 'rgba(255,255,255,1)'); g.addColorStop(0.25, 'rgba(255,255,255,0.55)'); g.addColorStop(0.6, 'rgba(255,255,255,0.12)'); g.addColorStop(1, 'rgba(255,255,255,0)');
    x.fillStyle = g; x.fillRect(0, 0, 64, 64);
    haloTex = new THREE.CanvasTexture(c);
    return haloTex;
  }
  /** Additive glow sprite (one lantern halo; LP.Mood draws all its lamps' halos as one point batch instead). */
  LP.halo = function (color, size) {
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: haloTexture(), color: new THREE.Color(...toRGB(color)), blending: THREE.AdditiveBlending, depthWrite: false, transparent: true, opacity: 0.7, fog: false }));
    s.scale.set(size, size, 1);
    s.renderOrder = 2;
    return s;
  };
  const HALO_VERT = /* glsl */`
#include <common>
#include <fog_pars_vertex>
attribute vec3 lpColor;
attribute vec2 lpSize;      // x: size (m), y: on (0 / 1)
uniform float uScale;
varying vec3 vC;
void main() {
  vec4 mvPosition = modelViewMatrix * vec4( position, 1.0 );
  gl_Position = projectionMatrix * mvPosition;
  gl_PointSize = lpSize.y > 0.5 ? clamp( lpSize.x * uScale / max( - mvPosition.z, 0.1 ), 0.0, 512.0 ) : 0.0;
  vC = lpColor;
  #include <fog_vertex>
}
`;
  const HALO_FRAG = /* glsl */`
#include <common>
#include <fog_pars_fragment>
uniform sampler2D uMap;
uniform float uOpacity;
varying vec3 vC;
void main() {
  float a = texture2D( uMap, gl_PointCoord ).a * uOpacity;
  #ifdef USE_FOG
    a *= 1.0 - 0.85 * smoothstep( fogNear, fogFar, vFogDepth );
  #endif
  gl_FragColor = vec4( vC, a );
}
`;
  const _dbs = new THREE.Vector2();
  /** A batch of additive halo points (one draw for any number of lanterns): set(i, pos, rgb, size, on) · commit(count). */
  class HaloBatch {
    constructor(capacity) {
      this.capacity = capacity;
      this.geo = new THREE.BufferGeometry();
      this.pos = new THREE.BufferAttribute(new Float32Array(capacity * 3), 3).setUsage(THREE.DynamicDrawUsage);
      this.col = new THREE.BufferAttribute(new Float32Array(capacity * 3), 3).setUsage(THREE.DynamicDrawUsage);
      this.size = new THREE.BufferAttribute(new Float32Array(capacity * 2), 2).setUsage(THREE.DynamicDrawUsage);
      this.geo.setAttribute('position', this.pos); this.geo.setAttribute('lpColor', this.col); this.geo.setAttribute('lpSize', this.size);
      // the texture goes in after the merge (UniformsUtils.merge clones textures, and a cloned CanvasTexture is never uploaded)
      const hu = THREE.UniformsUtils.merge([THREE.UniformsLib.fog, { uScale: { value: 500 }, uOpacity: { value: 0.7 } }]);
      hu.uMap = { value: haloTexture() };
      this.mat = new THREE.ShaderMaterial({
        uniforms: hu,
        vertexShader: HALO_VERT, fragmentShader: HALO_FRAG, blending: THREE.AdditiveBlending, transparent: true, depthWrite: false, fog: true,
      });
      this.points = new THREE.Points(this.geo, this.mat);
      this.points.frustumCulled = false;
      this.points.renderOrder = 2;
      this.points.name = 'LP halos';
      this.points.raycast = () => {};
      const self = this;
      this.points.onBeforeRender = function (renderer, scene, camera) {
        renderer.getDrawingBufferSize(_dbs);
        self.mat.uniforms.uScale.value = _dbs.y * 0.5 * (camera.projectionMatrix.elements[5] || 1);
      };
      this.geo.setDrawRange(0, 0);
    }
    set(i, p, rgb, size, on) {
      this.pos.setXYZ(i, p[0], p[1], p[2]); this.col.setXYZ(i, rgb[0], rgb[1], rgb[2]); this.size.setXY(i, size, on ? 1 : 0);
    }
    commit(count) {
      if (count != null) this.geo.setDrawRange(0, Math.min(count, this.capacity));
      this.pos.needsUpdate = true; this.col.needsUpdate = true; this.size.needsUpdate = true;
    }
    dispose() { this.geo.dispose(); this.mat.dispose(); if (this.points.parent) this.points.parent.remove(this.points); }
  }
  LP.HaloBatch = HaloBatch;

  // ------------------------------------------------------------ decal --
  function quadGeometry(pos, nor, uv, emission, rgb) {
    const nv = pos.length / 3;
    const col = new Float32Array(nv * 4), sm = new Float32Array(nv * 3);
    for (let i = 0; i < nv; i++) {
      col[i * 4] = rgb ? rgb[0] : 1; col[i * 4 + 1] = rgb ? rgb[1] : 1; col[i * 4 + 2] = rgb ? rgb[2] : 1;
      col[i * 4 + 3] = encodeEmission(emission || 0);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(new Float32Array(pos), 3));
    g.setAttribute('normal', new THREE.BufferAttribute(new Float32Array(nor), 3));
    g.setAttribute('color', new THREE.BufferAttribute(col, 4));
    g.setAttribute('lpWind', new THREE.BufferAttribute(new Float32Array(nv), 1));
    g.setAttribute('lpSmooth', new THREE.BufferAttribute(sm, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(uv), 2));
    g.computeBoundingBox(); g.computeBoundingSphere();
    g.userData.tris = nv / 3;
    return g;
  }
  /**
   * Painted decal (Lanternvale's signs): a w × h quad in the XY plane facing +Z, centred on the origin, carrying a texture
   * (sRGB, opaque) through the LowPoly lighting — no ink, no shadow. Place it a few millimetres in front of its board.
   */
  LP.decal = function (texture, w, h, opts) {
    opts = opts || {};
    const x = w / 2, y = h / 2;
    const g = quadGeometry([-x, -y, 0, x, -y, 0, x, y, 0, -x, -y, 0, x, y, 0, -x, y, 0], [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1],
      [0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1], opts.emission, null);
    const m = LP.mesh(g, { map: texture, outline: false, castShadow: false });
    m.name = 'LP decal';
    return m;
  };
  /**
   * Ground decal (paths, scorch marks, puddles, flower beds, leaf litter): a w × d quad lying on the ground (XZ, facing
   * up, 1.5 cm above it), its texture's alpha feathering it into the ground, lit like everything else (night grade, lamp
   * pools, fog). opts: { color: tint, opacity, y, rotation (radians), emission }. Textures: LP.decalTexture(kind).
   */
  LP.groundDecal = function (texture, w, d, opts) {
    opts = opts || {};
    const x = w / 2, z = d / 2;
    const g = quadGeometry([-x, 0, z, x, 0, z, x, 0, -z, -x, 0, z, x, 0, -z, -x, 0, -z], [0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0],
      [0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1], opts.emission, opts.color ? toRGB(opts.color) : null);
    const m = new THREE.Mesh(g, LP.material({ map: texture, alpha: true, opacity: opts.opacity != null ? opts.opacity : 1, polygonOffset: -2 }));
    m.position.y = opts.y != null ? opts.y : 0.015;
    if (opts.rotation) m.rotation.y = opts.rotation;
    m.renderOrder = 1;
    m.castShadow = false; m.receiveShadow = true;
    m.name = 'LP ground decal';
    m.raycast = () => {};
    return m;
  };
  const DECAL_TEX = new Map();
  /**
   * Painted ground-decal textures (RGBA canvas, soft feathered edge), cached per kind and seed: 'dirt' (a worn patch),
   * 'path' (a trodden strip, tile along its length), 'scorch' (burn mark), 'puddle', 'leaves' (litter), 'flowers' (a
   * flower bed), 'moss', 'snow', 'sand', 'mud', 'shadow' (a soft dark blot: canopy shade on the ground).
   */
  LP.decalTexture = function (kind, seed = 1, size = 256) {
    const key = kind + '|' + seed + '|' + size;
    if (DECAL_TEX.has(key)) return DECAL_TEX.get(key);
    const c = document.createElement('canvas'); c.width = c.height = size;
    const x = c.getContext('2d');
    let r = ((seed + 7) * 2654435761) >>> 0;
    const rnd = () => { r ^= r << 13; r >>>= 0; r ^= r >>> 17; r ^= r << 5; r >>>= 0; return (r & 0xffffff) / 16777216; };
    const S = size, H2 = S / 2;
    const blot = (cx, cy, rad, rgba, soft = 1) => { const g = x.createRadialGradient(cx, cy, 0, cx, cy, rad); g.addColorStop(0, rgba); g.addColorStop(Math.max(0, 1 - soft), rgba); g.addColorStop(1, rgba.replace(/[\d.]+\)$/, '0)')); x.fillStyle = g; x.beginPath(); x.arc(cx, cy, rad, 0, Math.PI * 2); x.fill(); };
    const pal = {
      dirt: [[150, 116, 80], [128, 96, 64], [170, 138, 98]], path: [[168, 132, 92], [146, 112, 76], [186, 152, 108]], scorch: [[44, 36, 34], [70, 56, 48], [30, 26, 26]],
      puddle: [[92, 118, 140], [110, 140, 160], [80, 100, 120]], leaves: [[160, 108, 58], [190, 140, 70], [130, 90, 54]], flowers: [[96, 140, 70], [80, 120, 60], [110, 150, 80]],
      moss: [[96, 138, 70], [120, 160, 84], [78, 116, 62]], snow: [[236, 240, 246], [220, 228, 238], [248, 250, 252]], sand: [[222, 200, 150], [206, 182, 132], [236, 216, 170]],
      mud: [[104, 82, 62], [86, 68, 54], [120, 96, 72]], shadow: [[40, 44, 60], [40, 44, 60], [40, 44, 60]],
    }[kind] || [[140, 110, 80], [120, 90, 66], [160, 130, 96]];
    const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;
    if (kind === 'path') {
      // a strip along V (tile along the length): soft edges left / right, worn tracks, stones
      for (let i = 0; i < 40; i++) { const px = H2 + (rnd() - 0.5) * S * 0.35, py = rnd() * S; for (const oy of [-S, 0, S]) blot(px, py + oy, S * (0.18 + rnd() * 0.12), rgba(pal[i % 3], 0.5)); }
      const g = x.createLinearGradient(0, 0, S, 0);
      g.addColorStop(0, 'rgba(0,0,0,1)'); g.addColorStop(0.18, 'rgba(0,0,0,0)'); g.addColorStop(0.82, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(0,0,0,1)');
      x.globalCompositeOperation = 'destination-out'; x.fillStyle = g; x.fillRect(0, 0, S, S); x.globalCompositeOperation = 'source-over';
      for (let i = 0; i < 26; i++) { const px = H2 + (rnd() - 0.5) * S * 0.5, py = rnd() * S; x.fillStyle = rgba([150 + rnd() * 30 | 0, 144 + rnd() * 26 | 0, 132 + rnd() * 20 | 0], 0.9); x.beginPath(); x.ellipse(px, py, 3 + rnd() * 4, 2 + rnd() * 3, rnd() * 3, 0, Math.PI * 2); x.fill(); }
    } else if (kind === 'puddle') {
      blot(H2, H2, S * 0.46, rgba(pal[0], 0.85), 0.25);
      for (let i = 0; i < 6; i++) blot(H2 + (rnd() - 0.5) * S * 0.4, H2 + (rnd() - 0.5) * S * 0.3, S * (0.12 + rnd() * 0.12), rgba(pal[1], 0.5));
      blot(H2 - S * 0.1, H2 - S * 0.12, S * 0.12, 'rgba(220,234,240,0.55)');
    } else if (kind === 'shadow') {
      blot(H2, H2, S * 0.48, rgba(pal[0], 0.55), 0.7);
      for (let i = 0; i < 10; i++) blot(H2 + (rnd() - 0.5) * S * 0.6, H2 + (rnd() - 0.5) * S * 0.6, S * (0.05 + rnd() * 0.06), 'rgba(255,240,200,0.0)');
    } else {
      // a soft irregular blot made of overlapping blotches, plus specks
      const n = kind === 'scorch' ? 18 : 26;
      for (let i = 0; i < n; i++) {
        const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * S * 0.24;
        blot(H2 + Math.cos(a) * d, H2 + Math.sin(a) * d, S * (0.12 + rnd() * 0.14), rgba(pal[i % 3], kind === 'scorch' ? 0.55 : 0.42));
      }
      if (kind === 'scorch') for (let i = 0; i < 30; i++) { const a = rnd() * Math.PI * 2, d = S * (0.1 + rnd() * 0.32); x.strokeStyle = rgba(pal[2], 0.5); x.lineWidth = 1 + rnd() * 2; x.beginPath(); x.moveTo(H2, H2); x.lineTo(H2 + Math.cos(a) * d, H2 + Math.sin(a) * d); x.stroke(); }
      const speck = { leaves: [[200, 120, 50], [220, 170, 70], [150, 80, 40], [180, 60, 40]], flowers: [[245, 240, 230], [242, 184, 198], [244, 211, 94], [201, 168, 255], [240, 140, 108]], moss: [[150, 190, 100]], dirt: [[110, 84, 60], [190, 160, 120]], snow: [[255, 255, 255]], sand: [[190, 160, 110]], mud: [[70, 54, 44]] }[kind];
      if (speck) for (let i = 0; i < (kind === 'flowers' ? 140 : 90); i++) {
        const a = rnd() * Math.PI * 2, d = Math.sqrt(rnd()) * S * 0.36, px = H2 + Math.cos(a) * d, py = H2 + Math.sin(a) * d, sz = kind === 'flowers' ? 3 + rnd() * 3 : 2 + rnd() * 4;
        x.fillStyle = rgba(speck[(rnd() * speck.length) | 0], 0.95);
        x.beginPath(); x.ellipse(px, py, sz, sz * (0.5 + rnd() * 0.5), rnd() * 3, 0, Math.PI * 2); x.fill();
      }
    }
    const t = new THREE.CanvasTexture(c);
    t.anisotropy = 4;
    if (kind === 'path') { t.wrapT = THREE.RepeatWrapping; }
    DECAL_TEX.set(key, t);
    return t;
  };
  const TEXT_TEX = new Map();
  /**
   * A plaque / sign board painted on a canvas: `text` in a brush (楷书) font on a board colour, an inner gold line.
   * opts: { w, h (px), board, ink (text colour), line, vertical (one glyph per row) }. Cached by its arguments.
   */
  LP.boardTexture = function (text, opts) {
    opts = opts || {};
    const key = text + '|' + JSON.stringify(opts);
    if (TEXT_TEX.has(key)) return TEXT_TEX.get(key);
    const W = opts.w || 448, Hh = opts.h || 128;
    const cv = document.createElement('canvas'); cv.width = W; cv.height = Hh;
    const x = cv.getContext('2d');
    x.fillStyle = opts.board || '#3A2A24'; x.fillRect(0, 0, W, Hh);
    const line = opts.line || null;
    if (line) { x.strokeStyle = line; x.lineWidth = Math.max(2, Hh * 0.03); const m = Hh * 0.08; x.strokeRect(m, m, W - 2 * m, Hh - 2 * m); }
    const chars = Array.from(text), n = chars.length;
    const font = s => `bold ${s}px "KaiTi", "STKaiti", "Kaiti SC", "Kaiti TC", "BiauKai", "AR PL UKai CN", "Noto Serif CJK SC", "Source Han Serif SC", "SimSun", "Songti SC", "WenQuanYi Zen Hei", serif`;
    x.fillStyle = opts.ink || '#F0C25A'; x.textAlign = 'center'; x.textBaseline = 'middle';
    if (opts.vertical) {
      const cell = Hh / (n + 0.4), s = Math.min(W * 0.72, cell * 0.86);
      x.font = font(Math.round(s));
      chars.forEach((c, i) => x.fillText(c, W / 2, cell * (i + 0.7)));
    } else {
      const cell = W / (n + 0.5), s = Math.min(Hh * 0.7, cell * 0.84);
      x.font = font(Math.round(s));
      chars.forEach((c, i) => x.fillText(c, cell * (i + 0.75), Hh / 2 + s * 0.04));
    }
    const t = new THREE.CanvasTexture(cv);
    t.anisotropy = 4;
    TEXT_TEX.set(key, t);
    return t;
  };

  // ============================================================ Mood ==
  // DayNight keyframes (Lanternvale). Colours are sRGB; the controller converts them to linear for the lights.
  const C = (r, g, b) => [r, g, b];
  const KEYS = [
    { h: 0, sun: C(0.55, 0.66, 1), sunI: 0.5, sky: C(0.22, 0.27, 0.55), ground: C(0.10, 0.10, 0.20), ambI: 0.6, glow: 1, night: 1 },
    { h: 4.6, sun: C(0.55, 0.66, 1), sunI: 0.5, sky: C(0.22, 0.27, 0.55), ground: C(0.10, 0.10, 0.20), ambI: 0.6, glow: 1, night: 1 },
    { h: 6.0, sun: C(1, 0.68, 0.54), sunI: 0.58, sky: C(0.60, 0.54, 0.76), ground: C(0.40, 0.30, 0.34), ambI: 0.74, glow: 0.55, night: 0.55 },
    { h: 7.4, sun: C(1, 0.84, 0.67), sunI: 0.84, sky: C(0.62, 0.64, 0.80), ground: C(0.44, 0.37, 0.33), ambI: 0.75, glow: 0.15, night: 0.12 },
    { h: 9.5, sun: C(1, 0.93, 0.80), sunI: 0.95, sky: C(0.62, 0.68, 0.82), ground: C(0.42, 0.38, 0.34), ambI: 0.75, glow: 0, night: 0 },
    { h: 15.5, sun: C(1, 0.91, 0.76), sunI: 0.93, sky: C(0.63, 0.67, 0.80), ground: C(0.43, 0.38, 0.33), ambI: 0.75, glow: 0, night: 0 },
    { h: 17.8, sun: C(1, 0.82, 0.54), sunI: 1.0, sky: C(0.50, 0.62, 0.84), ground: C(0.46, 0.42, 0.28), ambI: 0.75, glow: 0.22, night: 0.22 },
    { h: 19.4, sun: C(1, 0.82, 0.58), sunI: 0.72, sky: C(0.44, 0.54, 0.86), ground: C(0.36, 0.34, 0.30), ambI: 0.75, glow: 0.62, night: 0.6 },
    { h: 20.9, sun: C(0.55, 0.66, 1), sunI: 0.475, sky: C(0.22, 0.27, 0.55), ground: C(0.10, 0.10, 0.20), ambI: 0.6, glow: 0.95, night: 0.94 },
    { h: 24, sun: C(0.55, 0.66, 1), sunI: 0.5, sky: C(0.22, 0.27, 0.55), ground: C(0.10, 0.10, 0.20), ambI: 0.6, glow: 1, night: 1 },
  ];
  const SUNRISE = 5.9, SUNSET = 19.5;
  /** Named moods → hour. */
  const MOOD_HOURS = { dawn: 7.0, morning: 9.5, day: 12.5, noon: 12.5, afternoon: 15.5, golden: 17.8, gold: 17.8, dusk: 19.4, evening: 19.4, night: 22.5, midnight: 0.5 };
  LP.MOOD_HOURS = MOOD_HOURS;
  function goldenAt(hour) {
    hour = ((hour % 24) + 24) % 24;
    const dusk = sstep((hour - 15.6) / 1.6) * (1 - sstep((hour - 19.4) / 1.2));
    const dawn = sstep((hour - 5.5) / 0.9) * (1 - sstep((hour - 7.4) / 1.4));
    return Math.max(dusk, dawn * 0.6);
  }
  const AMBER = [1, 0.78, 0.46];
  /** A lamp's colour at a night factor: warm whites deepen to amber (DayNight.LampColor). */
  function lampColor(authored, night) {
    if (night <= 0) return authored;
    const r = authored[0], g = authored[1], b = authored[2], mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
    let h = 0;
    if (d > 1e-9) { if (mx === r) h = ((g - b) / d) % 6; else if (mx === g) h = (b - r) / d + 2; else h = (r - g) / d + 4; h *= 60; if (h < 0) h += 360; }
    const s = mx > 0 ? d / mx : 0;
    let warm = (1 - clamp01(Math.abs(h - 40) / 30)) * (1 - clamp01((s - 0.5) / 0.25));
    if (s < 0.05) warm = 1;
    return P.mix(authored, AMBER, clamp01(night) * warm * 0.7);
  }
  LP.lampColor = lampColor;
  LP.goldenAt = goldenAt;
  /** The DayNight values at an hour (sun direction in this game's world: Y up, the default view looking into −Z). */
  function moodAt(hour) {
    hour = ((hour % 24) + 24) % 24;
    let i = 0;
    while (i < KEYS.length - 2 && KEYS[i + 1].h <= hour) i++;
    const a = KEYS[i], b = KEYS[i + 1];
    const t = sstep((hour - a.h) / Math.max(1e-4, b.h - a.h));
    const night = lerp(a.night, b.night, t);
    const golden = goldenAt(hour);
    // the sun's path: rises on the left (−X), culminates in front of the scene (camera side), sets on the right (+X)
    const dayT = (hour - SUNRISE) / (SUNSET - SUNRISE);
    const az = lerp(-100, 100, clamp01(dayT)) * D2R;
    const elev = (dayT > 0 && dayT < 1 ? Math.sin(dayT * Math.PI) * 62 : 0) - 4;
    let sx = Math.sin(az), sy = -Math.max(0.3, Math.cos(az));
    const hl = Math.hypot(sx, sy); sx /= hl; sy /= hl;
    // elevation floor: low suns would throw shadows many times an object's height across the map (Lanternvale has no
    // cast shadows at all); at golden hour the floor (26°) keeps a shadow within ~2× the height of what casts it
    const le = Math.max(elev, lerp(14, 26, golden)) * D2R;
    const sunLV = [sx * Math.cos(le), sy * Math.cos(le), -Math.sin(le)];
    const moonLV = [0.47, -0.56, -0.68];
    const moonW = sstep((night - 0.35) / 0.45);
    const dLV = V.norm(V.lerp(sunLV, moonLV, moonW));
    const dir = V.norm([dLV[0], -dLV[2], -dLV[1]]);
    return {
      hour, night, golden, moonW, dir,
      sunCol: P.mix(a.sun, b.sun, t), sunI: lerp(a.sunI, b.sunI, t) * (1 + 0.36 * golden),
      sky: P.mix(a.sky, b.sky, t), ground: P.mix(a.ground, b.ground, t), ambI: lerp(a.ambI, b.ambI, t) * (1 - 0.23 * golden),
      glow: lerp(a.glow, b.glow, t),
    };
  }
  LP.moodAt = moodAt;
  const NOON = moodAt(12.5);
  const FOG_DEFAULTS = { golden: [0.95, 0.80, 0.62], dusk: [0.58, 0.50, 0.62], night: [0.16, 0.19, 0.33] };

  /**
   * Mood: DayNight → three lights + the LP shader globals of ONE scene (pushed just before that scene renders, reset
   * right after). World convention: Y up; the default view looks into −Z.
   *   new LP.Mood(scene, opts)   opts: { center: [x,y,z], extent: 20, shadow: true, shadowSize: 2048, shadowStrength: 0.7,
   *     sun / hemi: existing lights to drive instead of creating new ones; legacy: { sunColor, sunIntensity, skyColor,
   *     groundColor, hemiIntensity, equator: THREE.Color, equatorBase, shadowUniform: { value }, shadowBase } — the noon
   *     look of non-LP materials sharing these lights (the game's Lambert / terrain shaders): other hours scale it by
   *     Lanternvale's ratios, while LP materials get Lanternvale's own values through override globals;
   *     baseDir: [x,y,z] the sun direction at noon (the whole sun path is rotated to match); positionSun (true unless
   *     sun is given); fog: { base: [r,g,b], golden?, dusk?, night?, background: true } — fog (and background) colour
   *     per mood, in the scene's colour space; fogMax: 0.85; maxLamps: 8 (point lights in the pool); hook: true }
   *   mood.set('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | hour) → mood
   *   mood.addLamp([x,y,z], { color: '#ffe2a6', intensity: 0.9, range: 5.85, nightOnly: true, halo: true, haloSize: 1.4,
   *     flicker: 0 (fires: 0.15–0.3) }) → lamp   ·   mood.setLampLit(lamp, on) · mood.moveLamp(lamp, [x,y,z]) · mood.removeLamp(lamp)
   *     Lamps share a pool of maxLamps point lights (the nearest lit lamps to the camera, or to mood.focus, win), so
   *     adding lamps never recompiles shaders and any number of lanterns stays cheap; every lamp keeps its halo.
   *   mood.state = { hour, night, golden, moonW, dir, sunCol, sunI, sky, ground, ambI, glow, grade, shadowK }
   *   mood.apply() (push the globals now) · mood.update(camera) (lamp pool + flicker; the scene hook calls both) · mood.dispose()
   */
  class Mood {
    constructor(scene, opts) {
      opts = opts || {};
      this.scene = scene;
      this.opts = opts;
      this.center = opts.center || [0, 0, 0];
      this.extent = opts.extent || 20;
      this.legacy = opts.legacy || null;
      this.ownSun = !opts.sun; this.ownHemi = !opts.hemi;
      this.positionSun = opts.positionSun != null ? opts.positionSun : !opts.sun;
      this.shadowOn = opts.shadow !== false;
      this.shadowDay = opts.shadowStrength != null ? opts.shadowStrength : 0.7;
      this.baseDir = opts.baseDir ? V.norm(Array.isArray(opts.baseDir) ? opts.baseDir : [opts.baseDir.x, opts.baseDir.y, opts.baseDir.z]) : null;
      this.fogMax = opts.fogMax != null ? opts.fogMax : 0.85;
      this.fog = opts.fog || null;
      this.maxLamps = opts.maxLamps || 8;
      this.focus = null;
      if (opts.sun) this.sun = opts.sun;
      else {
        this.sun = new THREE.DirectionalLight(0xffffff, 1);
        this.sun.name = 'LP sun';
        this.sun.castShadow = this.shadowOn;
        const sz = opts.shadowSize || 2048;
        this.sun.shadow.mapSize.set(sz, sz);
        const cam = this.sun.shadow.camera, e = this.extent;
        cam.left = -e; cam.right = e; cam.top = e; cam.bottom = -e; cam.near = 0.5; cam.far = e * 6;
        this.sun.shadow.bias = -0.0006;
        this.sun.shadow.normalBias = 0.02;
        this.sun.shadow.radius = 3;
        this.sun.target.position.set(...this.center);
        scene.add(this.sun, this.sun.target);
      }
      if (opts.hemi) this.hemi = opts.hemi;
      else {
        this.hemi = new THREE.HemisphereLight(0xffffff, 0x888888, 1);
        this.hemi.name = 'LP sky';
        scene.add(this.hemi);
      }
      this.lamps = [];
      this.pool = null;
      this.halos = null;
      this.state = null;
      // the globals this scene pushes
      this.u = {
        glow: 0, rim: new THREE.Vector4(1, 0.92, 0.78, 3), grade: new THREE.Vector4(0, 0.8, 0.92, 1.22), warmth: new THREE.Vector4(), shadow: this.shadowDay,
        night: 0, sun: new THREE.Vector4(1, 1, 1, 0), sky: new THREE.Vector4(1, 1, 1, 0), ground: new THREE.Vector4(0, 0, 0, 0),
      };
      this._hooked = false;
      if (opts.hook !== false) this.hook();
    }
    /** Installs the scene hooks (push the globals before the scene renders, reset them after). */
    hook() {
      if (this._hooked) return this;
      const sc = this.scene, self = this;
      this._prevB = sc.onBeforeRender; this._prevA = sc.onAfterRender;
      sc.onBeforeRender = function (r, s, cam, rt) { self._prevB.call(this, r, s, cam, rt); self.apply(); self.update(cam); };
      sc.onAfterRender = function (r, s, cam) { self._prevA.call(this, r, s, cam); LP.resetGlobals(); };
      this._hooked = true;
      return this;
    }
    /** Pushes this scene's globals (the scene hook does it before every render). */
    apply() {
      const u = this.u;
      G.lpMisc.value.x = u.glow;
      G.lpRim.value.copy(u.rim);
      G.lpGrade.value.copy(u.grade);
      G.lpWarmth.value.copy(u.warmth);
      G.lpShadow.value.x = u.shadow;
      G.lpNight.value.x = u.night;
      G.lpSunC.value.copy(u.sun);
      G.lpSkyC.value.copy(u.sky);
      G.lpGroundC.value.copy(u.ground);
      G.lpFog.value.x = this.fogMax;
      G.lpFog.value.y = 1;
      return this;
    }
    /** 'dawn' (7) | 'morning' (9.5) | 'noon' (12.5) | 'afternoon' (15.5) | 'golden' (17.8) | 'dusk' (19.4) | 'night' (22.5) | an hour. */
    set(what) {
      const hour = typeof what === 'number' ? what : MOOD_HOURS[what] != null ? MOOD_HOURS[what] : 12.5;
      const m = moodAt(hour);
      const { night, golden, moonW } = m;
      let dir = m.dir;
      if (this.baseDir) {
        // rotate the whole sun / moon path so that noon falls on baseDir (the scene's authored light)
        const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(...NOON.dir), new THREE.Vector3(...this.baseDir));
        const v = new THREE.Vector3(...dir).applyQuaternion(q);
        dir = [v.x, v.y, v.z];
      }
      const nightK = sstep((night - 0.3) / 0.5), deepK = sstep((night - 0.62) / 0.3);
      const grade = 0.38 * sstep((night - 0.5) / 0.42);
      const u = this.u;
      // lights
      const c = this.center;
      if (this.positionSun) {
        this.sun.position.set(c[0] + dir[0] * this.extent * 3, c[1] + dir[1] * this.extent * 3, c[2] + dir[2] * this.extent * 3);
        this.sun.target.position.set(...c);
        this.sun.updateMatrixWorld(); this.sun.target.updateMatrixWorld();
      }
      const sunL = P.lin(m.sunCol), skyL = P.lin(m.sky), gndL = P.lin(m.ground);
      if (this.legacy) {
        // the shared lights keep the scene's own (legacy) look at noon, scaled by Lanternvale's ratios at other hours;
        // LP materials read Lanternvale's values through the override globals
        const L = this.legacy;
        const ratio = (a, b) => a.map((v, k) => (b[k] > 1e-4 ? v / b[k] : 0));
        const rs = ratio(m.sunCol, NOON.sunCol), rk = ratio(m.sky, NOON.sky), rg = ratio(m.ground, NOON.ground);
        const lc = toRGB(L.sunColor || [1, 1, 1]), ls = toRGB(L.skyColor || [1, 1, 1]), lg = toRGB(L.groundColor || [0.5, 0.5, 0.5]);
        this.sun.color.setRGB(lc[0] * rs[0], lc[1] * rs[1], lc[2] * rs[2]);
        this.sun.intensity = (L.sunIntensity != null ? L.sunIntensity : 1) * m.sunI / NOON.sunI;
        this.hemi.color.setRGB(ls[0] * rk[0], ls[1] * rk[1], ls[2] * rk[2]);
        this.hemi.groundColor.setRGB(lg[0] * rg[0], lg[1] * rg[1], lg[2] * rg[2]);
        this.hemi.intensity = (L.hemiIntensity != null ? L.hemiIntensity : 1) * m.ambI / NOON.ambI;
        if (L.equator && L.equatorBase) { const e = toRGB(L.equatorBase); L.equator.setRGB(e[0] * (rk[0] + rg[0]) / 2, e[1] * (rk[1] + rg[1]) / 2, e[2] * (rk[2] + rg[2]) / 2); }
        u.sun.set(sunL[0] * m.sunI, sunL[1] * m.sunI, sunL[2] * m.sunI, 1);
        u.sky.set(skyL[0] * m.ambI, skyL[1] * m.ambI, skyL[2] * m.ambI, 1);
        u.ground.set(gndL[0] * m.ambI, gndL[1] * m.ambI, gndL[2] * m.ambI, 1);
      } else {
        // linear colours × intensity, as SceneLighting.Working
        this.sun.color.setRGB(sunL[0], sunL[1], sunL[2]); this.sun.intensity = m.sunI;
        this.hemi.color.setRGB(skyL[0], skyL[1], skyL[2]); this.hemi.groundColor.setRGB(gndL[0], gndL[1], gndL[2]); this.hemi.intensity = m.ambI;
        u.sun.w = 0; u.sky.w = 0; u.ground.w = 0;
      }
      u.glow = m.glow;
      const rim = P.mix(P.mix(m.sunCol, [1, 1, 1], 0.45), [0.62, 0.72, 1], clamp01(night));
      const rl = P.lin(rim).map(v => v * (1 + 0.6 * golden));
      u.rim.set(rl[0], rl[1], rl[2], 3);
      u.grade.set(grade, 0.8, 0.92, 1.22);
      u.warmth.set(golden * 0.12, 0.45 * nightK, 0.8 * deepK, 0);
      u.night = sstep((night - 0.35) / 0.4);
      // cast shadows stay a soft hint of this game's shadow maps, never Lanternvale-foreign black shapes: 0.7 by day,
      // 0.45 and blurrier in the long golden-hour light, fading to 0.15 under the moon and off once the moon rules (the
      // shadow map then stops updating; castShadow never toggles, so no shader recompiles)
      let shadowK = lerp(lerp(this.shadowDay, 0.45, golden), 0.15, nightK);
      if (moonW >= 0.98 || !this.shadowOn) shadowK = 0;
      u.shadow = shadowK;
      if (this.sun.shadow) {
        this.sun.shadow.radius = lerp(3, 6, Math.max(golden, moonW));
        this.sun.shadow.autoUpdate = shadowK > 0;
        if (shadowK > 0) this.sun.shadow.needsUpdate = true;
        // a mood that owns its sun drops the moon's shadow map altogether (cheaper shaders; one recompile at the day / night
        // switch); a shared sun (SG.Gfx) never toggles castShadow, so the classic materials never recompile
        if (this.ownSun && this.opts.toggleShadow !== false) this.sun.castShadow = this.shadowOn && shadowK > 0;
      }
      if (this.legacy && this.legacy.shadowUniform) this.legacy.shadowUniform.value = (this.legacy.shadowBase != null ? this.legacy.shadowBase : 0.55) * (shadowK / Math.max(0.01, this.shadowDay));
      // fog / background
      if (this.fog && this.scene.fog) {
        const f = this.fog, base = toRGB(f.base || [this.scene.fog.color.r, this.scene.fog.color.g, this.scene.fog.color.b]);
        if (!f.base) f.base = base;
        let fc = P.mix(base, toRGB(f.golden || FOG_DEFAULTS.golden), 0.3 * golden);
        fc = P.mix(fc, toRGB(f.dusk || FOG_DEFAULTS.dusk), 0.55 * sstep((night - 0.15) / 0.4) * (1 - deepK));
        fc = P.mix(fc, toRGB(f.night || FOG_DEFAULTS.night), deepK);
        this.scene.fog.color.setRGB(fc[0], fc[1], fc[2]);
        if (f.background !== false && this.scene.background && this.scene.background.isColor) this.scene.background.setRGB(fc[0], fc[1], fc[2]);
        m.fog = fc;
      }
      this.state = Object.assign(m, { dir, grade, shadowK, nightK, deepK });
      this._lampsDirty = true;
      for (const L of this.lamps) if (L.onLit) L.onLit(this._lampOn(L));
      if (this._hooked === false) this.apply();
      return this;
    }
    get hour() { return this.state ? this.state.hour : 12.5; }
    _lampOn(L) { return L.lit && (!L.nightOnly || (this.state ? this.state.night : 0) > 0.45); }
    _ensurePool(need) {
      // the pool grows with the lamps up to maxLamps (each growth changes the light count once: add a scene's lamps when
      // it is built, before its first render, and no shader recompiles later)
      if (!this.pool) this.pool = [];
      const want = Math.min(this.maxLamps, need);
      for (let i = this.pool.length; i < want; i++) {
        const light = new THREE.PointLight(0xffffff, 0, 5, 0);
        light.castShadow = false;
        light.name = 'LP lamp ' + i;
        this.scene.add(light);
        this.pool.push(light);
      }
    }
    /** A lantern / fire light (nightOnly: off by day). Returns the lamp record. */
    addLamp(pos, opts) {
      opts = opts || {};
      this._ensurePool(this.lamps.length + 1);
      const L = {
        pos: [pos[0], pos[1], pos[2]], color: toRGB(opts.color || '#ffe2a6'), intensity: opts.intensity != null ? opts.intensity : 0.9,
        range: opts.range || 5.85, nightOnly: opts.nightOnly !== false, lit: opts.lit !== false, flicker: opts.flicker || 0,
        halo: opts.halo !== false, haloSize: opts.haloSize || 1.4, onLit: opts.onLit || null, light: null, phase: (this.lamps.length * 2.399) % 6.283,
      };
      this.lamps.push(L);
      this._lampsDirty = true;
      if (L.halo) this._growHalos();
      if (L.onLit && this.state) L.onLit(this._lampOn(L));
      return L;
    }
    setLampLit(L, on) { L.lit = !!on; this._lampsDirty = true; if (L.onLit) L.onLit(this._lampOn(L)); }
    moveLamp(L, pos) { L.pos[0] = pos[0]; L.pos[1] = pos[1]; L.pos[2] = pos[2]; this._lampsDirty = true; }
    removeLamp(L) { const i = this.lamps.indexOf(L); if (i >= 0) { this.lamps.splice(i, 1); this._lampsDirty = true; } }
    _growHalos() {
      const need = this.lamps.length;
      if (this.halos && this.halos.capacity >= need) return;
      const cap = Math.max(16, need * 2);
      if (this.halos) this.halos.dispose();
      this.halos = new HaloBatch(cap);
      this.scene.add(this.halos.points);
    }
    /** Lamp pool (the nearest lit lamps get the point lights), flicker, halos. The scene hook calls it every render. */
    update(camera) {
      if (!this.pool || !this.state) return;
      const night = this.state.night, t = G.lpMisc.value.w;
      const on = this.lamps.filter(L => this._lampOn(L));
      let chosen = on;
      if (on.length > this.pool.length) {
        const f = this.focus || (camera ? camera.getWorldPosition(_ip) : null);
        if (f) {
          const fx = f.x != null ? f.x : f[0], fy = f.y != null ? f.y : f[1], fz = f.z != null ? f.z : f[2];
          for (const L of on) L._d = (L.pos[0] - fx) ** 2 + (L.pos[1] - fy) ** 2 + (L.pos[2] - fz) ** 2;
          chosen = on.slice().sort((a, b) => a._d - b._d);
        }
        chosen = chosen.slice(0, this.pool.length);
      }
      for (const L of this.lamps) L.light = null;
      for (let i = 0; i < this.pool.length; i++) {
        const light = this.pool[i], L = chosen[i];
        if (!L) { light.intensity = 0; continue; }
        L.light = light;
        const col = lampColor(L.color, night);
        light.color.setRGB(...P.lin(col));
        const fl = L.flicker ? 1 + L.flicker * (0.6 * Math.sin(t * 11.3 + L.phase) + 0.4 * Math.sin(t * 6.7 + L.phase * 1.7)) : 1;
        light.intensity = L.intensity * fl;
        light.distance = L.range * (1 + 0.22 * clamp01(night));
        if (light.position.x !== L.pos[0] || light.position.y !== L.pos[1] || light.position.z !== L.pos[2]) { light.position.set(L.pos[0], L.pos[1], L.pos[2]); light.updateMatrixWorld(); }
      }
      if (this.halos && (this._lampsDirty || this.lamps.some(L => L.flicker))) {
        let k = 0;
        const show = night > 0.45;
        for (const L of this.lamps) {
          if (!L.halo) continue;
          const col = lampColor(L.color, night);
          const fl = L.flicker ? 1 + 0.5 * L.flicker * Math.sin(t * 9.1 + L.phase) : 1;
          this.halos.set(k++, L.pos, col, L.haloSize * fl, show && this._lampOn(L));
        }
        this.halos.commit(k);
      }
      this._lampsDirty = false;
    }
    dispose() {
      if (this._hooked) { this.scene.onBeforeRender = this._prevB; this.scene.onAfterRender = this._prevA; this._hooked = false; }
      if (this.pool) for (const l of this.pool) this.scene.remove(l);
      if (this.halos) this.halos.dispose();
      if (this.ownSun) { this.scene.remove(this.sun, this.sun.target); this.sun.dispose && this.sun.dispose(); }
      if (this.ownHemi) this.scene.remove(this.hemi);
      this.pool = null; this.halos = null; this.lamps = [];
    }
  }
  LP.Mood = Mood;
  LP.version = '1.0';
})();
