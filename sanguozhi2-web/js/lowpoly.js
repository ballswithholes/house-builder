'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 低多边形风格工具包（SG.LP）—— 美术试点（Lanternvale 风格）

   目标画风：与《Lanternvale》一致的风格化低多边形 —— 温暖、吉卜力式的配色，粗壮易读的剪影，
   圆形物体 6–12 边，岩石 / 木头 / 建筑 / 道具用平面（分面）着色，树冠是平滑的「软团」
   （抖动的二十面体 + 精确的椭球法线 + 全树冠的上暖下冷色阶），顶点色（不画贴图），
   每面亮度抖动 0.04–0.08、近地面 AO ~0.3，角色 / 生物 / 道具描深色墨线（地形、草、贴花、粒子不描），
   柔和的绘画式光照：黄金时刻提亮，夜晚整体调成冷蓝灰而灯笼周围是暖琥珀色光池，远景雾，单位下有柔和的圆形影子，
   树叶 / 布 / 头发随风轻摆。

   本文件是 Lanternvale 工具链（MeshBuilder.cs、Materials3D.cs、SceneLighting.cs、LanternvaleCommon.cginc、
   LowPoly / Outline / Shadow 着色器、PropKit / PropNature 的软团与色阶）在 three.js r158 上的移植。
   与 Lanternvale 不同之处（按本游戏的约定改写）：
   · 坐标：Y 向上、右手系（Lanternvale 世界是「上 = −Z」，模型本身同样是 Y 向上 +Z 朝前 —— 模型数据可直接照搬）。
     人物 / 生物面朝 +Z，人物右手在 −X（与 figure.js 一致）；道具正面朝向镜头一侧由场景决定。
   · 灯光：继续用 three.js 的灯光与阴影贴图（DirectionalLight = 太阳 / 月亮，可投影；HemisphereLight = 天空 / 地面环境光；
     PointLight = 灯笼 / 火），但着色器不走 three 的 PBR，而是读取 three 的灯光 uniform，
     按 Lanternvale 的光照模型计算：包裹式太阳漫反射（平滑过渡的绘画式明暗）、半球环境光、点光源的柔和衰减、
     轮廓光（rim）、顶点 alpha 自发光（夜晚增强）、线性距离雾（带最大雾量）、夜色调（_LV_Grade）与暖色（_LV_Warmth）。
     阴影贴图只压暗太阳项，强度可调（Lanternvale 本身没有投影；默认 0.7，保持柔和）。
   · 色彩空间：游戏是 Gamma 工作流（ColorManagement 关闭、线性输出、无色调映射）。Lanternvale 在 Unity 的 Linear
     色彩空间里计算光照，所以本着色器自己完成：顶点色 / 雾色按 sRGB → 线性解码，光照在线性空间计算，最后自己编码回 sRGB
     输出（不依赖渲染器的 outputColorSpace）。灯光颜色视为「已是线性」：用 SG.LP.Mood 设置灯光时会自动转换；
     自己建灯时用 SG.LP.lightColor('#hex') 取得线性颜色。若渲染器输出为 sRGB（three 默认），先调用 SG.LP.setOutputEncoded(false)。

   公开接口
   --------------------------------------------------------------------------
   const mb = new SG.LP.MeshBuilder(seed)
     变换栈：mb.push() / pop() / translate(x,y,z | [x,y,z]) / rotate(xDeg,yDeg,zDeg)（Unity 的欧拉顺序 Z→X→Y）/ rotateQ(q) /
             scale(s | x,y,z) / aim(at, dir)（+Y 指向 dir 的新坐标系，记得 pop）/ setMatrix(m4)。镜像（负缩放）安全。
     状态：  mb.color = [r,g,b]（sRGB 0..1，可用 SG.LP.Paint.hex('#..')）· emission 0..1（自发光，写入顶点 alpha）·
             wind（风摆权重）+ windGradient / windY0 / windY1（按建模根坐标的高度渐变）· bone（刚性蒙皮骨骼序号）·
             jitter（每面亮度抖动）· aoStrength / aoHeight（贴地变暗）
     基本体：box(center, size, topColor?, faces?)（faces 如 'tfrl'：只建看得见的面）· boxOn(bottom, size, topColor?) · taperedBox(bottom, size, tx, tz?) ·
             roof(bottom, width, depth, height, overhang?, thickness?, gables?)（硬山，屋脊沿 X）·
             lathe(profile[[r,y],...], sides, smooth?, capBottom?, capTop?, ringColors?, angleOffsetDeg?) ·
             cylinder(bottom, rB, rT, h, sides, smooth?, capB?, capT?) · cone(bottom, r, h, sides) · sphere(c, r|[rx,ry,rz], sides?, rings?, smooth?) ·
             capsule(bottom, r, h, sides?) · blob(c, radii, detail?, jitter?, seed?, flattenBottom?) ·
             segment(a, b, ra, rb, sides?, smooth?, caps?) · disc(c, r, sides?, down?) · plane(c, sx, sz, twoSided?) ·
             torus(c, radius, tube, segments?, sides?) · extrude(polygon[[x,z]..], y0, y1) · blade(base, tip, width, side?) ·
             triangle(a,b,c,col?) · triangleFacing(a,b,c,outward) · quad(a,b,c,d,outward) · quadTwoSided(a,b,c,d) · append(other)
     角色 / 道具辅助（UnitMeshKit / PropKit 的移植）：shell · panel · band · body · flat · strip · curvedStrip · spike · curve ·
             loft(rings, capStart?, capEnd?) · fan · obox(center, size, quat) · beam(a, b, w, h, upHint?) · rope(a, b, sag, r, segs?) ·
             tuft(at, height, blades, col) · paperLantern(c, r, h, paper, lit, cap) ·
             ribLantern(c, r, h, paper, lit, cap, tassel?, sides?, dayGlow?)（汉式灯笼：扁圆、竖肋明暗相间、金顶金底、流苏随风）
     逐顶点改写：boneBy(startVertex, p => 骨骼序号)（同一部件跨骨骼铰接：袍裙上排挂骨盆、其余挂大腿；胡须上三分之一挂头、其余挂胸）·
             windBy(startVertex, p => 风摆权重)（头发 / 尾巴：根部 0、梢部 1）
     树叶：  mb.softLump(center, radii, detail, yawDeg, ramp, lumpiness?)（平滑着色的软团：精确椭球法线 + 树冠色阶 + 自身上亮下暗）
     输出：  mb.toGeometry({ skinned: true }) → 非索引 BufferGeometry：position、normal（平面法线；软团为椭球法线）、
             color（vec4：rgb = sRGB 反照率，a = 自发光）、lpWind（风摆权重）、lpSmooth（平滑法线，墨线用）、
             skinIndex / skinWeight（刚性：每个顶点 100% 绑在 bone 上）。geometry.userData.tris = 三角形数。
             mb.triangleCount / vertexCount / random01() / range(a, b)
   SG.LP.Paint   hex('#rrggbb') · shade(c, k) · mix(a, b, t) · hsv(c, hueShiftDeg, sat?, val?) · lin(c)（sRGB→线性）
   SG.LP.canopyRamp(top, side, bottom, y0, y1, tune?) → (y) => colour；SG.LP.foliage(bucket, base, hueStep?, tune?) → { top, side, bottom }
     SG.LP.FOLIAGE_TUNE = { topVal, warmMix, warmVal, coolMix, cool }：本游戏俯视镜头下的默认值（树冠顶不再比草地亮、底部更偏青），
     传入 Lanternvale 原值 { topVal: 1.08, warmMix: 0.22, warmVal: 1.04, coolMix: 0.25, cool: '#355A63' } 即得其原样
   SG.LP.MeshCache.get(key, build) → 共用几何体
   材质（共用，按选项缓存）
     SG.LP.material(opts?)        低多边形材质：{ side: THREE.FrontSide | DoubleSide, tint, rim, fogScale, windScale }
     SG.LP.outlineMaterial(opts?) 墨线（反向外壳，BackSide，沿平滑法线外推恒定像素宽度，1080p 下约 2.2px）：
                                  { color: '#241a21', width: 2.2, windScale }
     SG.LP.mesh(geo, opts?)       → THREE.Mesh（opts.outline 默认 true：子网格 = 同一几何体的墨线外壳；castShadow / receiveShadow）
     SG.LP.skinnedMesh(geo, skeleton, opts?) → THREE.SkinnedMesh（绑定到已有骨架，例如 figure.js 的人物 / 战马骨架；墨线同样蒙皮）
     SG.LP.blobShadow(rx, rz, strength?, softness?) → 地面上的柔和圆形影子（Mesh，平放在 y = 0.01）
     SG.LP.globals                全局 uniform（时间、风、夜色调、暖色、夜间自发光、rim、雾量上限、阴影强度、视口）
     SG.LP.setTime(seconds)       风摆时钟；SG.LP.setViewport(w, h, refHeight = 1080) 墨线像素宽度的参照（每次渲染前 / 改变视口时）
     SG.LP.lightColor(hex)        → THREE.Color（线性），给自建的 three 灯光用
   氛围
     const mood = new SG.LP.Mood(scene, { center:[x,y,z], extent: 20, shadow: true, shadowSize: 2048, shadowStrength: 0.7 })
     mood.set('noon' | 'golden' | 'dusk' | 'night' | 小时数)  —— 按 Lanternvale DayNight 的关键帧：太阳方向 / 颜色 / 强度、
          天空与地面环境光、rim、夜间自发光、夜色调、暖色、雾色（scene.fog 若存在）。
          投影只是柔和的提示：白天 0.7，黄昏 0.45 且更柔（半径 6，太阳仰角不低于 26°，影长 ≤ 约 2 倍物高），
          入夜渐降到 0.15，月光下（moonW ≥ 0.98）不再投影（Lanternvale 没有投影）
     mood.addLamp([x,y,z], { color: '#ffe2a6', intensity: 0.9, range: 5.85, nightOnly: true, halo: true }) → { light, halo }
          入夜时灯光转为琥珀色、照得更远（LampColor / LampRange），白天关闭（nightOnly）
     mood.sun / mood.hemi / mood.lamps / mood.state
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

  /** Underside squash of soft foliage lumps (unit-sphere y below 0 is multiplied by this). */
  const SOFT_UNDER = 0.8;

  // ====================================================== MeshBuilder ==
  const _v = new THREE.Vector3(), _e = new THREE.Euler(), _q = new THREE.Quaternion(), _m = new THREE.Matrix4();

  class MeshBuilder {
    constructor(seed = 1) {
      this.P = []; this.N = []; this.C = []; this.E = []; this.W = []; this.B = []; this.T = [];
      this.stack = [];
      this.m = new THREE.Matrix4();
      this.nm = new THREE.Matrix3();
      this.mirrored = false;
      this.rng = ((((Math.imul(seed | 0, 2654435761 | 0) >>> 0) + 12345) >>> 0) | 1) >>> 0;
      // state for the next primitives
      this.color = [1, 1, 1];
      this.emission = 0;
      this.wind = 0;
      this.windGradient = false;
      this.windY0 = 0; this.windY1 = 1;
      this.bone = 0;
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
      this.E.push(clamp01(this.emission));
      this.W.push(w);
      this.B.push(this.bone | 0);
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
        for (let i = 0; i < n - 1; i++)
          for (let j = 0; j < sides; j++) {
            const a = idx[i][j], b = idx[i + 1][j], c = idx[i + 1][j + 1], d = idx[i][j + 1];
            this.addTri(a, b, c); this.addTri(a, c, d);
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
      const keepC = this.color, keepE = this.emission, keepJ = this.jitter;
      this.jitter = 0.03;
      // lit: warm papers turn honey-gold, coloured (red) papers glow a deeper orange of their own hue
      const warmPaper = P.luma(paper) > 0.6;
      this.color = lit ? (warmPaper ? P.mix(paper, PAL.glow, 0.55) : P.mix(paper, PAL.glowDeep, 0.35)) : paper;
      this.emission = lit ? (warmPaper ? 0.95 : 0.85) : 0;
      this.push().translate(c);
      this.lathe([[r * 0.58, -h * 0.5], [r * 0.9, -h * 0.3], [r, 0], [r * 0.9, h * 0.3], [r * 0.58, h * 0.5]], sides, false, true, true);
      this.emission = 0;
      this.color = cap;
      this.cylinder([0, h * 0.5 - 0.01, 0], r * 0.62, r * 0.5, h * 0.12, sides, false, false, true);
      this.cylinder([0, -h * 0.5 - h * 0.1, 0], r * 0.5, r * 0.62, h * 0.11, sides, false, true, false);
      this.color = tassel || PAL.vermilion;
      this.cylinder([0, -h * 0.5 - h * 0.32, 0], r * 0.08, r * 0.22, h * 0.22, Math.min(5, sides), false, true, false);
      this.pop();
      this.color = keepC; this.emission = keepE; this.jitter = keepJ;
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
     * Non-indexed BufferGeometry: position, normal, color (vec4: sRGB albedo + emission), lpWind, lpSmooth (smoothed
     * normal for the ink outline), and with { skinned: true } rigid skinIndex / skinWeight (100 % on each vertex's bone).
     */
    toGeometry(opts) {
      opts = opts || {};
      this._soften();
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
      const pos = new Float32Array(nt * 3), nor = new Float32Array(nt * 3), col = new Float32Array(nt * 4), wind = new Float32Array(nt), sm = new Float32Array(nt * 3);
      const skinned = !!opts.skinned;
      const si = skinned ? new Uint16Array(nt * 4) : null, sw = skinned ? new Float32Array(nt * 4) : null;
      for (let t = 0; t < nt; t++) {
        const i = T[t];
        pos[t * 3] = this.P[i * 3]; pos[t * 3 + 1] = this.P[i * 3 + 1]; pos[t * 3 + 2] = this.P[i * 3 + 2];
        nor[t * 3] = this.N[i * 3]; nor[t * 3 + 1] = this.N[i * 3 + 1]; nor[t * 3 + 2] = this.N[i * 3 + 2];
        col[t * 4] = this.C[i * 3]; col[t * 4 + 1] = this.C[i * 3 + 1]; col[t * 4 + 2] = this.C[i * 3 + 2]; col[t * 4 + 3] = this.E[i];
        wind[t] = this.W[i];
        const s = V.norm(sum.get(keys[i]));
        sm[t * 3] = s[0]; sm[t * 3 + 1] = s[1]; sm[t * 3 + 2] = s[2];
        if (skinned) { si[t * 4] = this.B[i]; sw[t * 4] = 1; }
      }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
      g.setAttribute('color', new THREE.BufferAttribute(col, 4));
      g.setAttribute('lpWind', new THREE.BufferAttribute(wind, 1));
      g.setAttribute('lpSmooth', new THREE.BufferAttribute(sm, 3));
      if (skinned) { g.setAttribute('skinIndex', new THREE.BufferAttribute(si, 4)); g.setAttribute('skinWeight', new THREE.BufferAttribute(sw, 4)); }
      g.computeBoundingBox(); g.computeBoundingSphere();
      g.userData.tris = nt / 3;
      return g;
    }
    clear() {
      this.P = []; this.N = []; this.C = []; this.E = []; this.W = []; this.B = []; this.T = []; this.stack = []; this.softParts = [];
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
      const keepC = this.color, keepE = this.emission, keepJ = this.jitter, keepW = this.wind, keepG = this.windGradient;
      this.jitter = 0;
      const body = lit ? P.mix(paper, PAL.glowDeep, 0.3) : paper;
      const prof = [[r * 0.5, -h * 0.5], [r * 0.88, -h * 0.3], [r, 0], [r * 0.88, h * 0.3], [r * 0.5, h * 0.5]];
      this.push().translate(c);
      this.emission = lit ? 0.85 : dayGlow;
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
      this.emission = 0;
      this.color = cap;
      this.cylinder([0, h * 0.5 - 0.01, 0], r * 0.56, r * 0.42, h * 0.13, sides, false, false, true);
      this.cylinder([0, -h * 0.5 - h * 0.12, 0], r * 0.42, r * 0.56, h * 0.13, sides, false, true, false);
      // tassel: a short strip that sways
      this.color = tassel || cap;
      this.windGradient = false; this.wind = 0.6;
      this.strip([0, -h * 0.5 - h * 0.2, 0], [0, -h * 0.5 - h * 0.62, 0], r * 0.22, r * 0.3, [0, 0, 1], r * 0.06);
      this.pop();
      this.color = keepC; this.emission = keepE; this.jitter = keepJ; this.wind = keepW; this.windGradient = keepG;
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
  // Globals pushed once per frame (SceneLighting's shader globals, minus what three's lights already carry).
  const G = {
    lpMisc: { value: new THREE.Vector4(0, 0.06, 1.6, 0) },      // x: night glow, y: wind strength (m), z: wind speed, w: time (s)
    lpRim: { value: new THREE.Vector4(1, 0.92, 0.78, 3) },      // rgb: rim colour (linear), a: rim power
    lpGrade: { value: new THREE.Vector4(0, 0.8, 0.92, 1.22) },  // x: amount, yzw: tint of the graded luma
    lpWarmth: { value: new THREE.Vector4(0, 0, 0, 0) },         // x: golden lift, y: lamp amber pull, z: lamp moon mask
    lpFog: { value: new THREE.Vector4(0.85, 1, 0, 0) },         // x: max fog amount, y: 1 = fog colour is sRGB (decode)
    lpShadow: { value: new THREE.Vector4(0.7, 0, 0, 0) },       // x: how much a cast shadow darkens the sun term
    lpView: { value: new THREE.Vector4(1920, 1080, 1, 0) },     // xy: viewport (px), z: outline px per unit width (= h / 1080)
    lpOut: { value: 1 },                                        // 1: encode linear → sRGB in the shader (Gamma workflow / linear output)
  };
  LP.globals = G;
  LP.setTime = t => { G.lpMisc.value.w = t; };
  LP.setWind = (strength, speed) => { G.lpMisc.value.y = strength; if (speed != null) G.lpMisc.value.z = speed; };
  LP.setViewport = (w, h, refHeight = 1080) => { G.lpView.value.set(w, h, h / refHeight, 0); };
  LP.setOutputEncoded = enc => { G.lpOut.value = enc === false ? 0 : 1; };
  LP.setShadowStrength = k => { G.lpShadow.value.x = k; };
  LP.lightColor = hex => { const c = P.lin(Array.isArray(hex) ? hex : P.hex(hex)); return new THREE.Color(c[0], c[1], c[2]); };

  const GLSL_COMMON = /* glsl */`
uniform vec4 lpMisc;
uniform vec4 lpView;
uniform float lpOut;
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
uniform vec3 uTint;
uniform vec4 uFlash;
uniform float uRimBoost;
uniform float uFogScale;
varying vec3 vViewPosition;
varying vec3 vNormalV;
varying vec4 vColor;
#ifdef LP_DETAIL
  uniform sampler2D uDetail;
  uniform float uDetailK;
  varying vec2 vDetailUv;
#endif
#ifdef LP_MAP
  uniform sampler2D uMap;
  varying vec2 vMapUv;
#endif
const vec3 LV_LUMA = vec3( 0.3, 0.59, 0.11 );
const vec3 LV_LAMP_AMBER = vec3( 1.3, 1.0, 0.5 );
// the sun's soft painterly ramp: wrapped diffuse eased in
float lpSunRamp( vec3 n, vec3 l ) {
  float wrap = clamp( ( dot( n, l ) + 0.45 ) / 1.45, 0.0, 1.0 );
  return wrap * wrap * ( 3.0 - 2.0 * wrap );
}
void main() {
  vec3 n = normalize( vNormalV );
  #ifdef DOUBLE_SIDED
    n = gl_FrontFacing ? n : - n;
  #endif
  vec3 albedo = lpLin( vColor.rgb ) * uTint;
  #ifdef LP_DETAIL
    // painted detail (Lanternvale's planar _MainTex on terrain): a tileable multiplier around 1 (texel 0.5 = 1)
    albedo *= mix( vec3( 1.0 ), texture2D( uDetail, vDetailUv ).rgb * 2.0, uDetailK );
  #endif
  #ifdef LP_MAP
    // a painted decal (sign boards, plaques): the texture's sRGB colour times the vertex colour, lit like everything else
    albedo *= lpLin( texture2D( uMap, vMapUv ).rgb );
  #endif
  float emission = vColor.a;
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
      float lpR = lpSunRamp( n, directionalLights[ i ].direction );
      float lpS = 1.0;
      #if defined( USE_SHADOWMAP ) && ( UNROLLED_LOOP_INDEX < NUM_DIR_LIGHT_SHADOWS )
        if ( receiveShadow ) lpS = getShadow( directionalShadowMap[ i ], directionalLightShadows[ i ].shadowMapSize, directionalLightShadows[ i ].shadowBias, directionalLightShadows[ i ].shadowRadius, vDirectionalShadowCoord[ i ] );
      #endif
      sunU += directionalLights[ i ].color * lpR;
      sunC += directionalLights[ i ].color * lpR * mix( 1.0, lpS, lpShadow.x );
    }
  }
  #pragma unroll_loop_end
  #endif
  #if NUM_HEMI_LIGHTS > 0
  #pragma unroll_loop_start
  for ( int i = 0; i < NUM_HEMI_LIGHTS; i ++ ) {
    {
      float lpH = dot( n, hemisphereLights[ i ].direction ) * 0.5 + 0.5;
      amb += mix( hemisphereLights[ i ].groundColor, hemisphereLights[ i ].skyColor, lpH );
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
  col = mix( col, albedo * ( 1.0 + lpMisc.x ), clamp( emission, 0.0, 1.0 ) );
  #ifdef USE_FOG
    float lpDist = length( vViewPosition );
    float lpFogK = clamp( ( lpDist - fogNear ) / max( fogFar - fogNear, 0.01 ), 0.0, 1.0 ) * lpFog.x * uFogScale;
    col = mix( col, lpFog.y > 0.5 ? lpLin( fogColor ) : fogColor, lpFogK );
  #endif
  col = mix( col, lpLin( uFlash.rgb ), clamp( uFlash.a, 0.0, 1.0 ) );
  gl_FragColor = vec4( lpEnc( col ), 1.0 );
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
  vec3 nw = normalize( mat3( lpModel ) * objectNormal );
  vec2 nc = ( projectionMatrix * viewMatrix * vec4( nw, 0.0 ) ).xy;
  float l = length( nc );
  if ( l > 0.00001 ) {
    float px = uWidth * lpView.z;
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
  /** The LowPoly material (shared per option set). */
  LP.material = function (opts) {
    opts = opts || {};
    const side = opts.side != null ? opts.side : THREE.FrontSide;
    const tint = opts.tint || [1, 1, 1];
    const key = ['lp', side, tint.join(','), opts.rim || 0, opts.fogScale != null ? opts.fogScale : 1, opts.windScale != null ? opts.windScale : 1, opts.flash ? opts.flash.join(',') : '', opts.detail ? opts.detail.uuid + ':' + (opts.planar || 0.25) + ':' + (opts.detailK != null ? opts.detailK : 1) : '', opts.map ? opts.map.uuid : ''].join('|');
    let m = MATS.get(key);
    if (m) return m;
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.lights, THREE.UniformsLib.fog, {
      uTint: { value: new THREE.Vector3(...P.lin(tint)) }, uFlash: { value: new THREE.Vector4(...(opts.flash || [1, 1, 1, 0])) },
      uRimBoost: { value: opts.rim || 0 }, uFogScale: { value: opts.fogScale != null ? opts.fogScale : 1 }, uWindScale: { value: opts.windScale != null ? opts.windScale : 1 },
    }]);
    if (opts.detail) { u.uDetail = { value: opts.detail }; u.uPlanar = { value: opts.planar || 0.25 }; u.uDetailK = { value: opts.detailK != null ? opts.detailK : 1 }; }
    if (opts.map) u.uMap = { value: opts.map };
    m = new THREE.ShaderMaterial({ uniforms: withGlobals(u), vertexShader: LP_VERT, fragmentShader: LP_FRAG, lights: true, fog: true, vertexColors: true, side });
    if (opts.detail || opts.map) { m.defines = {}; if (opts.detail) m.defines.LP_DETAIL = 1; if (opts.map) m.defines.LP_MAP = 1; }
    m.name = 'LP LowPoly';
    MATS.set(key, m);
    return m;
  };
  /** Ink outline: inverted hull of constant pixel width along the smoothed normal (BackSide). */
  LP.outlineMaterial = function (opts) {
    opts = opts || {};
    const ink = opts.color ? (Array.isArray(opts.color) ? opts.color : P.hex(opts.color)) : LP.INK;
    const width = opts.width != null ? opts.width : 2.2;
    const key = ['out', ink.join(','), width, opts.windScale != null ? opts.windScale : 1, opts.fogScale != null ? opts.fogScale : 1].join('|');
    let m = MATS.get(key);
    if (m) return m;
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.fog, {
      uInk: { value: new THREE.Vector3(...ink) }, uWidth: { value: width }, uWindScale: { value: opts.windScale != null ? opts.windScale : 1 }, uFogScale: { value: opts.fogScale != null ? opts.fogScale : 1 },
    }]);
    m = new THREE.ShaderMaterial({ uniforms: withGlobals(u), vertexShader: OUT_VERT, fragmentShader: OUT_FRAG, fog: true, side: THREE.BackSide });
    m.name = 'LP Outline';
    MATS.set(key, m);
    return m;
  };
  /** Default ink colour (Materials3D.Ink). */
  LP.INK = [0.14, 0.10, 0.13];

  /** Mesh with the LowPoly material and (opts.outline !== false) an ink-outline child sharing the geometry. */
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
      mesh.add(o);
      mesh.userData.outline = o;
    }
    mesh.userData.tris = geo.userData.tris || 0;
    return mesh;
  };

  // ------------------------------------------------------ blob shadow --
  const SHADOW_FRAG = /* glsl */`
uniform vec4 uColor;
uniform float uSoftness;
varying vec2 vUv;
void main() {
  float d = length( vUv * 2.0 - 1.0 );
  float a = 1.0 - smoothstep( 1.0 - uSoftness, 1.0, d );
  gl_FragColor = vec4( uColor.rgb, uColor.a * a );
}
`;
  const SHADOW_GEO = (() => { const g = new THREE.PlaneGeometry(1, 1); g.rotateX(-Math.PI / 2); return g; })();
  /** Soft contact (blob) shadow lying on y = 0.01: radii rx × rz metres. */
  LP.blobShadow = function (rx, rz, strength = 0.42, softness = 0.65) {
    const m = new THREE.ShaderMaterial({
      uniforms: { uColor: { value: new THREE.Vector4(0.12, 0.09, 0.16, strength) }, uSoftness: { value: softness } },
      vertexShader: 'varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }',
      fragmentShader: SHADOW_FRAG, transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2,
    });
    const s = new THREE.Mesh(SHADOW_GEO, m);
    s.scale.set(rx * 2, 1, rz * 2);
    s.position.y = 0.01;
    s.renderOrder = 1;
    s.castShadow = false; s.receiveShadow = false;
    s.name = 'blobShadow';
    return s;
  };

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
  /** Additive glow sprite (lantern halos at night). */
  LP.halo = function (color, size) {
    const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: haloTexture(), color: new THREE.Color(...(Array.isArray(color) ? color : P.hex(color))), blending: THREE.AdditiveBlending, depthWrite: false, transparent: true, opacity: 0.7, fog: false }));
    s.scale.set(size, size, 1);
    s.renderOrder = 2;
    return s;
  };

  // ------------------------------------------------------------ decal --
  /**
   * Painted decal (Lanternvale's signs): a w × h quad in the XY plane facing +Z, centred on the origin, carrying a texture
   * (sRGB, opaque) through the LowPoly lighting — no ink, no shadow. Place it a few millimetres in front of its board.
   */
  LP.decal = function (texture, w, h, opts) {
    opts = opts || {};
    const x = w / 2, y = h / 2;
    const pos = new Float32Array([-x, -y, 0, x, -y, 0, x, y, 0, -x, -y, 0, x, y, 0, -x, y, 0]);
    const nor = new Float32Array(18), sm = new Float32Array(18);
    for (let i = 0; i < 6; i++) { nor[i * 3 + 2] = 1; sm[i * 3 + 2] = 1; }
    const col = new Float32Array(24).fill(1);
    for (let i = 0; i < 6; i++) col[i * 4 + 3] = opts.emission || 0;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
    g.setAttribute('color', new THREE.BufferAttribute(col, 4));
    g.setAttribute('lpWind', new THREE.BufferAttribute(new Float32Array(6), 1));
    g.setAttribute('lpSmooth', new THREE.BufferAttribute(sm, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array([0, 0, 1, 0, 1, 1, 0, 0, 1, 1, 0, 1]), 2));
    g.computeBoundingBox(); g.computeBoundingSphere();
    g.userData.tris = 2;
    const m = LP.mesh(g, { map: texture, outline: false, castShadow: false });
    m.name = 'LP decal';
    return m;
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
  // DayNight keyframes (Lanternvale), evaluated at the moods the pilot shows. Colours are sRGB; the controller converts
  // them to linear for three's lights (SceneLighting.Working).
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

  /**
   * Mood: DayNight → three lights + the LP globals. World convention of this game: Y up; the default view looks into
   * −Z (the scene's depth), so Lanternvale's ground direction (x, y = into the scene) maps to (x, −z), up (−Z) to +Y.
   */
  class Mood {
    constructor(scene, opts) {
      opts = opts || {};
      this.scene = scene;
      this.center = opts.center || [0, 0, 0];
      this.extent = opts.extent || 20;
      this.sun = new THREE.DirectionalLight(0xffffff, 1);
      this.sun.name = 'LP sun';
      this.shadowOn = opts.shadow !== false;
      this.shadowDay = opts.shadowStrength != null ? opts.shadowStrength : 0.7;
      this.sun.castShadow = this.shadowOn;
      const sz = opts.shadowSize || 2048;
      this.sun.shadow.mapSize.set(sz, sz);
      const cam = this.sun.shadow.camera, e = this.extent;
      cam.left = -e; cam.right = e; cam.top = e; cam.bottom = -e; cam.near = 0.5; cam.far = e * 6;
      this.sun.shadow.bias = -0.0006;
      this.sun.shadow.normalBias = 0.02;
      this.sun.shadow.radius = 3;
      this.sun.target.position.set(...this.center);
      this.hemi = new THREE.HemisphereLight(0xffffff, 0x888888, 1);
      this.hemi.name = 'LP sky';
      scene.add(this.sun, this.sun.target, this.hemi);
      this.lamps = [];
      this.state = null;
      this.fogBase = opts.fogColor || null;
    }
    /** 'noon' (12.5) | 'golden' (17.8) | 'dusk' (19.4) | 'night' (22.5) | 'dawn' (7) | an hour. */
    set(what) {
      const hour = typeof what === 'number' ? what : { noon: 12.5, day: 12.5, golden: 17.8, gold: 17.8, dusk: 19.4, night: 22.5, dawn: 7.0 }[what] || 12.5;
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
      let dLV = V.norm(V.lerp(sunLV, moonLV, moonW));
      const dir = V.norm([dLV[0], -dLV[2], -dLV[1]]);
      const sunCol = P.mix(a.sun, b.sun, t);
      const sunI = lerp(a.sunI, b.sunI, t) * (1 + 0.36 * golden);
      const sky = P.mix(a.sky, b.sky, t), ground = P.mix(a.ground, b.ground, t);
      const ambI = lerp(a.ambI, b.ambI, t) * (1 - 0.23 * golden);
      const glow = lerp(a.glow, b.glow, t);
      const nightK = sstep((night - 0.3) / 0.5), deepK = sstep((night - 0.62) / 0.3);
      const grade = 0.38 * sstep((night - 0.5) / 0.42);
      // lights (linear colours × intensity, as SceneLighting.Working)
      const c = this.center;
      this.sun.position.set(c[0] + dir[0] * this.extent * 3, c[1] + dir[1] * this.extent * 3, c[2] + dir[2] * this.extent * 3);
      this.sun.target.position.set(...c);
      this.sun.color.setRGB(...P.lin(sunCol)); this.sun.intensity = sunI;
      this.hemi.color.setRGB(...P.lin(sky)); this.hemi.groundColor.setRGB(...P.lin(ground)); this.hemi.intensity = ambI;
      G.lpMisc.value.x = glow;
      const rim = P.mix(P.mix(sunCol, [1, 1, 1], 0.45), [0.62, 0.72, 1], clamp01(night));
      const rl = P.lin(rim).map(v => v * (1 + 0.6 * golden));
      G.lpRim.value.set(rl[0], rl[1], rl[2], 3);
      G.lpGrade.value.set(grade, 0.8, 0.92, 1.22);
      G.lpWarmth.value.set(golden * 0.12, 0.45 * nightK, 0.8 * deepK, 0);
      // cast shadows stay a soft hint of this game's shadow maps, never Lanternvale-foreign black shapes: 0.7 by day,
      // 0.45 and blurrier in the long golden-hour light, fading to 0.15 under the moon (its light is fill, not a sun)
      const shadowK = lerp(lerp(this.shadowDay, 0.45, golden), 0.15, nightK);
      G.lpShadow.value.x = shadowK;
      this.sun.shadow.radius = lerp(3, 6, Math.max(golden, moonW));
      this.sun.castShadow = this.shadowOn && moonW < 0.98;
      for (const L of this.lamps) this._lamp(L, night);
      this.state = { hour, night, golden, dir, sunCol, sunI, sky, ground, ambI, glow, grade, shadowK, moonW };
      return this;
    }
    _lamp(L, night) {
      const on = !L.nightOnly || night > 0.45;
      const col = lampColor(L.color, night);
      L.light.color.setRGB(...P.lin(col));
      L.light.intensity = on ? L.intensity : 0;
      L.light.distance = L.range * (1 + 0.22 * clamp01(night));
      if (L.halo) { L.halo.visible = on && night > 0.45; L.halo.material.color.setRGB(...col); }
      if (L.onLit) L.onLit(on);
    }
    /** A lantern / fire light (nightOnly: off by day). Returns the record { light, halo }. */
    addLamp(pos, opts) {
      opts = opts || {};
      const light = new THREE.PointLight(0xffffff, 0, opts.range || 5.85, 0);
      light.position.set(...pos);
      light.castShadow = false;
      this.scene.add(light);
      const L = { light, color: Array.isArray(opts.color) ? opts.color : P.hex(opts.color || '#ffe2a6'), intensity: opts.intensity != null ? opts.intensity : 0.9, range: opts.range || 5.85, nightOnly: opts.nightOnly !== false, onLit: opts.onLit || null };
      if (opts.halo !== false) { L.halo = LP.halo(L.color, opts.haloSize || 1.4); L.halo.position.set(...pos); this.scene.add(L.halo); }
      this.lamps.push(L);
      if (this.state) this._lamp(L, this.state.night);
      return L;
    }
  }
  LP.Mood = Mood;
  LP.version = 'pilot-2';
})();
