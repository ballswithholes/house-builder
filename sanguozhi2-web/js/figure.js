'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 人物 / 士兵 / 战马 / 兵器模型库（第四版美术：DESIGN-V4，低多边形 Lanternvale 风格）

   画风（玩家批准的试点 js/lp-pilot.js：关羽、汉军枪兵、战马）：
   · 风格化低多边形 —— 温暖、吉卜力式的配色，粗壮易读的剪影；平面（分面）着色，顶点色（不画贴图），每面亮度抖动 0.04–0.08；
     Lanternvale 的柔和绘画式光照（SG.LP 着色器：读取场景的太阳 / 天光 / 灯，黄金时刻提亮、夜色调、灯池、雾）；
     人物描墨线（反向外壳，逐顶点线宽：眼、鼻、嘴等五官细线，高光无线）；布带、缨、鬃、尾、长发随风轻摆。
   · 真实成人比例（玩家指定）：约 7.5 头身（英武的名将最多约 7.8，女性 7.3）；脸靠粗眉、带高光的眼、楔形鼻、胡须、
     发髻与冠帽的剪影来读，而不是大头。
   · FFX 式层叠衣甲：衣、甲排、腰带、束带 / 腰结、前后垂、护肩叠片、披风、飘带；18 种文化的衣冠、发须，名将专属造型与兵器。
   · 刚性蒙皮：每个部件在一根骨上；铰接部件（袍裙侧片、披风、胡须、长发、鬃尾）按顶点位置分骨。
   · 几何由 SG.LP（js/lowpoly.js）的 MeshBuilder 生成；材质为 SG.LP.material（共用一个程序）+ 墨线外壳 SG.LP.outlineMaterial。
     游戏里 lowpoly.js 在本文件之前载入；单独的测试页没载入时本文件按自身路径就近异步载入，preload() 等它完成。
   · 单个人物：势力色烘进顶点色（模板按 外观 × LOD × 势力色 缓存）；士兵群（实例化）：势力色为实例属性，只乘在势力色部件上
     （几何体的 lpTeam 遮罩），同一模板任意势力共用一次绘制 + 一次墨线绘制。
   · 坐标：单位米；人物脚底中心为原点，面朝 +Z，上为 +Y，人物右手在 −X 一侧。武将身高约 1.86 米 × 体型系数。战马同样面朝 +Z。
   · 色彩：游戏采用 Gamma 工作流（ColorManagement 关闭、线性输出、无色调映射）；LP 着色器自行编码 sRGB。

   公开接口（第三版起不变；第四版的变化在【】里）
   --------------------------------------------------------------------------
   SG.Figure.preload(opts?)  → Promise。分片生成士兵模板、名将模板与士兵群动作烘焙（不阻塞主线程，单片预算 12ms）。
                               【先等 js/lowpoly.js 就绪】opts：{ cultures: [...]（缺省全部 18 种）, types: [...], lods: ['low'], seeds: [0],
                               generals: [gen...], generalLod, crowd: [{ type, culture, seed, mounted }]（或 true）, crowdLod（缺省 'low'）, budget }。
                               【不再生成贴图图集 / 环境贴图 / 面部贴图】
   SG.Figure.general(gen, opts) → Figure（名将 / 任意武将）
       gen：{ name, war, intel, pol, culture?, sex?, ... }（或姓名字符串）
       opts：{ culture, color（势力色 '#rrggbb'）, lod: 'high' | 'mid' | 'low'（缺省 'high'）, weapon, shield（true / false / 盾名）,
               mounted, shadow（投影，缺省 true）, blob（脚下的柔和圆影，缺省 false：有太阳阴影的场景不需要） }
   SG.Figure.soldier(opts)   → Figure（士兵）
       opts：{ type: 'spear' | 'sword' | 'archer' | 'cavalry', culture, color, lod: 'low' | 'mid'（缺省 'low'）, seed, shadow, blob }
       同 (type, culture, color, lod, 变体) 的士兵共用几何体，只各自持有骨架。
   SG.Figure.horse(opts)     → Horse（战马；setPose / bones / attach / mount / dispose）
       opts：{ color（马衣 / 具装镶边的势力色）, coat（毛色 '#hex'）, mane, lod, armor: 'cloth' | 'scale' | 'none', culture, seed,
               name（名马：赤兔 的卢 绝影 爪黄飞电 白龙驹）, general（面帘 + 大红缨）, metal, plume, blob }
       horse.mount(fig) / horse.dismount(fig)。
   SG.Figure.crowd(opts)     → Crowd（实例化士兵群：绘制次数 = 模板组数 × 2（本体 + 墨线），与人数无关；投影每组再一次）
       opts：{ lod: 'low' | 'mid'（缺省 'low'）, shadow（缺省 true）, capacity, renderer }
       crowd.root · crowd.add({ type, culture, color, seed, mounted, horse: { coat, armor, seed } }) → id
       crowd.setTransform(id, x, y, z, yaw, scale) / crowd.setMatrix(id, m4) · crowd.play(id, clip, { t0, phase, speed })
       （步兵片段 SG.Figure.crowdClips；骑兵 SG.Figure.crowdMountClips）· crowd.setTime(seconds) · crowd.setColor(id, hex) ·
       crowd.remove(id) · crowd.count / drawCalls / triangles / stats() / dispose() · crowd.prepare(specs) → Promise
       实现：动作按 24fps 烘焙为半精度骨骼矩阵贴图，顶点着色器蒙皮（LP 光照着色器 + 实例化墨线，同一贴图）；站立的士兵贴地压平。
   SG.Figure.shield(name, opts) → THREE.Mesh（独立的盾：scutum / round / oval / pai / wa / rattan / long / hide；+Z 为盾面；带墨线）
   SG.Figure.teamMaterial(hex) → 【LP 共用材质（势力色已烘进顶点色；换势力色请重建人物）】
   SG.Figure.clearCache()    释放无人使用的模板几何体
   SG.Figure.weapon(name, opts) → THREE.Mesh（独立兵器，原点为右手握点，杆 / 刃沿 +Y，刃口朝 +Z；带墨线）
       mesh.userData.weapon = { id, name, style: 'pole'|'one'|'dual'|'bow'|'fan', len, tip, base, off }。opts：{ lod, shadow }
       兵器名：guandao 青龙偃月刀 · snake 丈八蛇矛 · halberd 方天画戟 · swords 双股剑 · twinji 典韦双铁戟 · bigblade 许褚斩马大刀 ·
       fengzui 黄忠凤嘴刀 · huangbow 黄忠宝雕弓 · shortji 太史慈短戟 · bawang 孙策霸王枪 · yajiao 赵云涯角枪 · hutou 马超虎头湛金枪 ·
       yitian 倚天剑 · guding 古锭刀 · fan 羽扇 · bigaxe 开山大斧 · spear pilum lance poleblade axe trident dao jian gladius spatha
       tachi mace handaxe twindao bow khanda longbow staff（完整列表：SG.Figure.weapons）
   Figure（人物 / 士兵 / 马的实例）
       fig.root            THREE.Group（骨架、本体网格、兵器都在它下面）
       fig.mesh            SkinnedMesh（LP 材质；子网格 'outline' = 墨线外壳）；fig.head = null【头并入本体】；
                           fig.weaponMesh / fig.weaponMesh2（名将：独立兵器网格，各带墨线；士兵的兵器烘进本体）
       fig.meshes          本体、兵器及其墨线外壳（drawCalls = meshes.length）
       fig.bones           { root, pelvis, spine, chest, neck, head, clavL … handR, thighL … footR, cape1–3, skirtF, skirtB, plume1, plume2,
                             hair1, shL, shR, dlL, dlR }（头关节在寰椎处）
       fig.sockets         { gripR, gripL, shieldL, back, headTop }；马：horse.sockets.saddle
       fig.setPose(clip, t, opts?)   opts：{ lower, lowerT, blend: { clip, t, w }, ik, gallop, clamp, lean（false：不做长兵外倾） }
                           静止 / 行进片段里长兵向外、向后倾，杆子不挡脸（Lanternvale PoleHold；SG.Figure.POLE_LEAN）。
       fig.blendPose(clipA, tA, clipB, tB, w) · fig.update(dt)（披风 / 盔缨 / 长发的弹簧）· fig.clipInfo(clip)
       fig.ikHand(side, pos, quat?) · fig.grip(pos, dirOrQuat, roll?) · fig.attach(obj, boneOrSocket) · fig.detach(obj)
       fig.setWeapon(name) / fig.weaponInfo / fig.style · fig.weaponTip(out) · fig.weaponBase(out)
       fig.setTint(hex, k) 受击闪光等：【LP 外观（SG.LP.setLook）：墨线变色加粗为主的辉光 + 少量整体闪色；k = 0 关闭】
       fig.triangles / fig.drawCalls / fig.spec / fig.dispose()
   SG.Figure.clips · SG.Figure.horseClips · SG.Figure.toAnimationClip(fig, clip, fps) · SG.Figure.stats()
   SG.Figure.material【= LP 共用材质】· SG.Figure.setRim(color, strength) · SG.Figure.lookOf(gen, opts) · SG.Figure.soldierLook(opts)
   SG.Figure.cultures / weapons / soldierTypes / boneNames / horseBoneNames · SG.Figure.budgets（三角形预算）

   多边形预算（第四版，含兵器与盾，墨线外壳不计）：high ≤ 3.1k（名将 ≤ 3.6k），mid ≤ 1.8k，low（士兵）≤ 900；
   战马 high ≈ 2.5k，mid ≈ 1.6k，low ≤ 900。超出时按 SQUEEZE 逐级降低细节重建（tpl.squeeze）。SG.Figure.stats().tris 记录实测值。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const PI = Math.PI, TAU = Math.PI * 2, D2R = Math.PI / 180;
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());

  // =============================================================== 工具 ==
  function hashStr(s) {
    s = String(s);
    let h = 2166136261 >>> 0;
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
    h ^= h >>> 16; h = Math.imul(h, 0x85ebca6b) >>> 0; h ^= h >>> 13; h = Math.imul(h, 0xc2b2ae35) >>> 0; h ^= h >>> 16;
    return h >>> 0;
  }
  function Rng(seed) {
    let a = (seed >>> 0) || 1;
    const f = function () {
      a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
    return {
      f,
      range(a0, b0) { return a0 + (b0 - a0) * f(); },
      int(a0, b0) { return a0 + Math.floor(f() * (b0 - a0 + 1)); },
      pick(arr) { return arr[Math.floor(f() * arr.length) % arr.length]; },
      chance(p) { return f() < p; },
      wpick(list) { let s = 0; for (const e of list) s += e[1]; let r = f() * s; for (const e of list) { r -= e[1]; if (r <= 0) return e[0]; } return list[list.length - 1][0]; },
    };
  }
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const sstep = (e0, e1, x) => { const t = clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); };
  const gauss = (x, m, s) => { const d = (x - m) / s; return Math.exp(-d * d); };
  const smooth = t => t * t * (3 - 2 * t);

  // 颜色：'#rrggbb' → [r, g, b]（0..1，Gamma 工作流下直接作线性值使用）
  const colCache = new Map();
  function col(c) {
    if (c == null) return [1, 1, 1];
    if (Array.isArray(c)) return c;
    if (typeof c === 'number') return [((c >> 16) & 255) / 255, ((c >> 8) & 255) / 255, (c & 255) / 255];
    if (c.isColor) return [c.r, c.g, c.b];
    if (typeof c === 'object' && 'r' in c) return [c.r, c.g, c.b];
    let v = colCache.get(c);
    if (v) return v;
    let h = String(c).replace('#', '');
    if (h.length === 3) h = h.split('').map(x => x + x).join('');
    const n = parseInt(h.slice(0, 6), 16) || 0;
    v = [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
    colCache.set(c, v);
    return v;
  }
  // 第 4 分量 = 势力色遮罩（1：最终颜色再乘材质的势力色 uTeam），见 TEAM
  function mix(a, b, t) { a = col(a); b = col(b); const r = [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)]; if (a.length > 3 || b.length > 3) r.push(Math.max(a[3] || 0, b[3] || 0)); return r; }
  // k > 0 提亮（向白）、k < 0 压暗
  function shade(c, k) { c = col(c); if (k >= 0) return mix(c, [1, 1, 1], k); const r = [c[0] * (1 + k), c[1] * (1 + k), c[2] * (1 + k)]; if (c.length > 3) r.push(c[3]); return r; }
  // 势力色占位：顶点色为白、遮罩为 1，渲染时乘以材质的势力色（同一模板可用于任何势力）
  const TEAM = [1, 1, 1, 1];
  function hex(c) { c = col(c); const h = v => Math.round(clamp01(v) * 255).toString(16).padStart(2, '0'); return '#' + h(c[0]) + h(c[1]) + h(c[2]); }
  function lum(c) { c = col(c); return c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11; }
  // 饱和度调整
  function sat(c, k) { c = col(c); const l = lum(c); return [clamp01(l + (c[0] - l) * k), clamp01(l + (c[1] - l) * k), clamp01(l + (c[2] - l) * k)]; }

  // 常用金属色 / 布色
  const STEEL = '#9aa2ae', DSTEEL = '#5c626e', IRON = '#4a4e58', GOLD = '#e0b050', BRONZE = '#b08850', SILVER = '#d4d8e0', BLACKM = '#2c2e36';
  const LEATHER = '#6a4a30', DLEATHER = '#3e2c20', WOOD = '#7a5634', DWOOD = '#4a3020', RED = '#b02a24', WHITE = '#ece8e0', BLACK = '#1c1a1e';

  // 统计
  const STATS = { built: 0, templates: 0, textures: 0, atlasMs: 0, envMs: 0, buildMs: 0, lastBuildMs: 0, tris: {} };


  // 向量小工具
  const _v3 = () => new THREE.Vector3();
  function v3add(a, b) { return [a[0] + b[0], a[1] + b[1], a[2] + b[2]]; }
  function v3sub(a, b) { return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]; }
  function v3mul(a, k) { return [a[0] * k, a[1] * k, a[2] * k]; }
  function v3dot(a, b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
  function v3cross(a, b) { return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]; }
  function v3len(a) { return Math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]); }
  function v3norm(a) { const l = v3len(a) || 1; return [a[0] / l, a[1] / l, a[2] / l]; }
  function v3lerp(a, b, t) { return [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)]; }
  function v3madd(a, b, k) { return [a[0] + b[0] * k, a[1] + b[1] * k, a[2] + b[2] * k]; }
  // 绕任意轴旋转向量
  function v3rot(v, axis, ang) {
    const c = Math.cos(ang), s = Math.sin(ang), k = v3dot(axis, v), cr = v3cross(axis, v);
    return [v[0] * c + cr[0] * s + axis[0] * k * (1 - c), v[1] * c + cr[1] * s + axis[1] * k * (1 - c), v[2] * c + cr[2] * s + axis[2] * k * (1 - c)];
  }


  // ===================================================== 低多边形工具包接入 ==
  // 几何体由 SG.LP（js/lowpoly.js）的 MeshBuilder 生成，材质为 LP 的 Lanternvale 光照着色器 + 墨线外壳。
  // 游戏里 lowpoly.js 在 figure.js 之前载入；单独的测试页没载入时，本文件按自身路径就近载入一次（异步），
  // preload() 会等它载入完成（battle-view / clash / duel 都先 preload 再建人物）。
  const FIG_SRC = (typeof document !== 'undefined' && document.currentScript && document.currentScript.src) || '';
  let LP_WAIT = null;
  function ensureLP() {
    if (SG.LP && SG.LP.MeshBuilder) return Promise.resolve(true);
    if (LP_WAIT) return LP_WAIT;
    LP_WAIT = new Promise(res => {
      if (typeof document === 'undefined') { res(false); return; }
      const s = document.createElement('script');
      s.src = FIG_SRC ? FIG_SRC.replace(/figure\.js(\?.*)?$/, 'lowpoly.js') : 'js/lowpoly.js';
      s.onload = () => res(!!(SG.LP && SG.LP.MeshBuilder));
      s.onerror = () => res(false);
      (document.head || document.documentElement).appendChild(s);
    });
    return LP_WAIT;
  }
  if (typeof document !== 'undefined' && !(SG.LP && SG.LP.MeshBuilder)) ensureLP();
  let LP_READY = false;
  function LPX() {
    const L = SG.LP;
    if (!L || !L.MeshBuilder) throw new Error('SG.Figure：需要先载入 js/lowpoly.js（SG.LP）');
    if (!LP_READY) {
      LP_READY = true;
      // 游戏是 Gamma 工作流（渲染器线性输出）：LP 着色器自行编码 sRGB（SG.Gfx 也会设；单独的测试页靠这里）
      L.setOutputEncoded(true);
    }
    return L;
  }

  // 带势力色遮罩的 MeshBuilder：颜色数组带 .t = 1 的部件（势力色衣甲、旗、缨……）逐顶点记 1，
  // toGeometry 多一个 lpTeam 属性（士兵群的实例势力色只乘在这些顶点上；单个人物直接把势力色烘进顶点色）
  let _FB = null;
  function FigBuilder() {
    if (_FB) return _FB;
    const Base = LPX().MeshBuilder;
    _FB = class FigMeshBuilder extends Base {
      constructor(seed) { super(seed); this.TM = []; this._lock = -1; }
      // LP 的 lathe（及以它为基础的 segment / sphere / cylinder / band …）在内部用 Paint.mix 重设颜色（不带 .t）：
      // 期间锁住进入时的势力色遮罩
      set color(c) { this._c = c; this._tm = this._lock >= 0 ? this._lock : c && c.t ? 1 : 0; }
      lathe(...a) { const keep = this._lock; this._lock = this._tm || 0; try { super.lathe(...a); } finally { this._lock = keep; this._tm = this._c && this._c.t ? 1 : 0; } }
      get color() { return this._c; }
      addVertex(p, n, c) { const i = super.addVertex(p, n, c); this.TM.push(this._tm || 0); return i; }
      append(o) { super.append(o); while (this.TM.length < this.P.length / 3) this.TM.push(0); }
      toGeometry(o) {
        const g = super.toGeometry(o), T = this.T, n = T.length;
        let any = 0;
        const tm = new Uint8Array(n);
        for (let t = 0; t < n; t++) { const v = this.TM[T[t]] ? 255 : 0; tm[t] = v; any |= v; }
        if (any) g.setAttribute('lpTeam', new THREE.BufferAttribute(tm, 1, true));
        return g;
      }
    };
    return _FB;
  }
  // 颜色（sRGB 0..1 数组）· 势力色（.t = 1 的数组）
  const C3 = c => { const a = col(c); return [a[0], a[1], a[2]]; };
  function tcol(base, k) { const a = [clamp01(base[0] * k), clamp01(base[1] * k), clamp01(base[2] * k)]; a.t = 1; return a; }
  // 很深的颜色在 LP 光照下成一团墨：提到暖深色（Lanternvale BipedKit.HairTone 的做法），floor = 最低亮度
  function lift(c, floor, warm) {
    c = C3(c);
    const v = Math.max(c[0], c[1], c[2]);
    if (v >= floor) return c;
    const k = floor / Math.max(0.02, v), w = warm == null ? 0.3 : warm;
    const up = c.map(x => clamp01(x * k));
    return [lerp(up[0], floor, w * (1 - v / floor)), lerp(up[1], floor * 0.82, w * (1 - v / floor)), lerp(up[2], floor * 0.68, w * (1 - v / floor))];
  }
  function hairTone(c) {
    c = C3(c);
    const v = Math.max(c[0], c[1], c[2]), lifted = 0.22 + 0.5 * v;
    if (v >= lifted) return c;
    const k = lifted / Math.max(0.02, v);
    const up = c.map(x => clamp01(x * k));
    const t = 0.36 * (lifted - v) / lifted;
    return [lerp(up[0], lifted, t), lerp(up[1], lifted * 0.8, t), lerp(up[2], lifted * 0.66, t)];
  }
  // 势力色：Lanternvale 的柔和饱和度（略降饱和、略提亮），单个人物与士兵群用同一换算
  function teamTone(hexc) {
    const L = SG.LP, c = C3(hexc || '#8a2a24');
    const t = L ? L.Paint.hsv(c, 0, 0.9, 1.12) : c;
    return lift(t, 0.3, 0.1);
  }
  const PALF = {
    gold: '#D9A441', goldDark: '#9A6A2A', goldLight: '#EDC46A', bronze: '#B98A4E', silver: '#D6DCE4',
    iron: '#68707E', ironDark: '#4E5562', ironSeam: '#3A3F4A', steel: '#C9D2DA', bladeBack: '#A9B9C6',
    leather: '#6E3B2C', leatherDark: '#4A3426', boots: '#45302A', sole: '#2E211C', cream: '#EADFC4', wood: '#8C6A48', woodDark: '#5A3E2A',
    red: '#C24E34', tassel: '#D2402E', jade: '#8FD4A8', white: '#EEEAE0', black: '#2A2426', fur: '#8A6A4A', furLight: '#B09070',
    rattan: '#C8A060', rattanDark: '#9A7440', lining: '#3A3236', eyeDark: '#2A2024', catch: '#FFFFF6', gauntlet: '#4A3426',
  };

  // 共用材质（LP.material / LP.outlineMaterial 按选项缓存，所有人物共用一个程序）
  function teamMaterial() { return LPX().material({}); }
  function setRim(color, strength) {
    const L = LPX(), g = L.globals && L.globals.lpRim;
    if (!g) return;
    const c = col(color || '#ffd8a8'), k = strength == null ? 1 : strength;
    g.value.set(c[0] * k, c[1] * k, c[2] * k, g.value.w);       // 场景的氛围（LP.Mood）每次渲染前会重设
  }

  // ======================================================== 人形骨架 ==
  // 绑定姿势：A 字（双臂外展 40°，肘直），全部骨骼在绑定时旋转为单位（局部轴 = 人物轴），
  // 因此片段里的欧拉角都是相对绑定姿势、绕父骨轴的旋转：x 正 = 前俯（脊柱）/ 后屈（膝），y = 左右转身，z = 侧倾。
  const BONES = ['root', 'pelvis', 'spine', 'chest', 'neck', 'head',
    'clavL', 'upperArmL', 'foreArmL', 'handL', 'clavR', 'upperArmR', 'foreArmR', 'handR',
    'thighL', 'shinL', 'footL', 'thighR', 'shinR', 'footR',
    'cape1', 'cape2', 'cape3', 'skirtF', 'skirtB', 'plume1', 'plume2', 'hair1', 'shL', 'shR', 'dlL', 'dlR'];
  const PARENT = {
    root: null, pelvis: 'root', spine: 'pelvis', chest: 'spine', neck: 'chest', head: 'neck',
    clavL: 'chest', upperArmL: 'clavL', foreArmL: 'upperArmL', handL: 'foreArmL',
    clavR: 'chest', upperArmR: 'clavR', foreArmR: 'upperArmR', handR: 'foreArmR',
    thighL: 'pelvis', shinL: 'thighL', footL: 'shinL', thighR: 'pelvis', shinR: 'thighR', footR: 'shinR',
    cape1: 'chest', cape2: 'cape1', cape3: 'cape2', skirtF: 'pelvis', skirtB: 'pelvis', plume1: 'head', plume2: 'plume1', hair1: 'head',
    // 肩部辅助骨（与上臂同位，旋转取上臂的一半）：护肩整体刚性挂在它上面，躯干肩部与袖根的权重过渡也经过它，
    // 手臂高举时不撕裂、不出现甲片碎裂
    shL: 'clavL', shR: 'clavR', dlL: 'clavL', dlR: 'clavR',
  };
  // sh：护肩挂点（跟随上臂旋转的 85%，护肩贴着上臂但不随上臂扭转撕裂）；dl：三角肌过渡（50%，躯干肩部与袖根）
  const SH_K = 0.85, DL_K = 0.5;
  const B = {};
  BONES.forEach((n, i) => { B[n] = i; });
  // 地面贴合标志所在的骨骼贴图位置（紧接最后一根骨骼；战马骨骼更少，该位置恒为 0）
  const GROUND_SLOT = BONES.length;
  const ARM_A = 40 * D2R;      // 绑定姿势的手臂外展角


  // ----------------------------------------------------------- 头部比例 --
  // 真实成人的头身比（玩家指定）：头顶 → 下巴 = 身高 / 头身数（士兵 7.5，英武的名将最多约 7.8，女性 7.3），头宽约 0.155 米、
  // 深约 0.19 米（蛋形，不是球）；脸靠五官（粗眉、带高光的眼、楔形鼻、胡须、发髻与冠帽的剪影）而不是大头来读。
  // 单位头：球的下半收窄成下颌（q = 环的球面高度，下巴 −1 → 头顶 +1），按 (θ, 环) 塑出方下颌、前突的下巴、颧骨、眉弓、
  // 略平的太阳穴（与 js/lp-pilot.js 的试点同一算法）。列每 360/S 度一条（θf = 90° − j·360/S，0 = 正前）。
  const HEAD_TILT = -12, JAW_FROM = 0.3, JAW_DROP = 0.12, EYE_TURN = 30;
  const HEAD_QS = {
    12: [-1, -0.95, -0.84, -0.68, -0.47, -0.26, -0.06, 0.14, 0.42, 0.68, 0.9, 1],
    9: [-1, -0.92, -0.74, -0.47, -0.2, 0.1, 0.44, 0.78, 1],
    7: [-1, -0.86, -0.56, -0.2, 0.2, 0.62, 1],
  };
  function headGrid(chin, shape, nq, S) {
    const Q = HEAD_QS[nq] || HEAD_QS[12];
    const narrow = clamp(0.2 - (chin - 1) * 0.3, 0.06, 0.4), sh = Object.assign({ jaw: 0.1, chin: 0.06, cheek: 0.05, brow: 0.04, temple: 0.04 }, shape || {});
    const ys = [], rad = [];
    for (const q of Q) {
      let y = q, r = Math.sqrt(Math.max(0, 1 - q * q));
      if (q < 0) { const t = clamp01((-q - JAW_FROM) / (1 - JAW_FROM)); r *= 1 - narrow * t * t * (3 - 2 * t); y = q * (1 + JAW_DROP * q * q); }
      ys.push(y);
      const row = [];
      for (let j = 0; j < S; j++) {
        let tf = 90 - j * 360 / S; tf = ((tf + 540) % 360) - 180;
        const at = Math.abs(tf);
        const g = 1 + sh.jaw * gauss(q, -0.72, 0.17) * gauss(at, 75, 32) + sh.chin * gauss(q, -0.9, 0.11) * gauss(tf, 0, 24)
          + sh.cheek * gauss(q, -0.24, 0.15) * gauss(at, 55, 22) + sh.brow * gauss(q, 0.12, 0.1) * gauss(at, 20, 36)
          - sh.temple * gauss(q, 0.36, 0.2) * gauss(at, 90, 24);
        row.push(r > 0 ? r * g : 0);
      }
      rad.push(row);
    }
    return { ys, rad, S, Q };
  }
  // 单位头经过前倾（HEAD_TILT）后的范围（按轴缩放之前）
  function headExtents(G) {
    const a = HEAD_TILT * D2R, ca = Math.cos(a), sa = Math.sin(a), S = G.S;
    const e = { x0: 1e9, x1: -1e9, y0: 1e9, y1: -1e9, z0: 1e9, z1: -1e9 };
    G.ys.forEach((y, i) => G.rad[i].forEach((r, j) => {
      const t = j * 2 * PI / S, x = Math.cos(t) * r, z = Math.sin(t) * r;
      const yy = y * ca - z * sa, zz = y * sa + z * ca;
      e.x0 = Math.min(e.x0, x); e.x1 = Math.max(e.x1, x); e.y0 = Math.min(e.y0, yy); e.y1 = Math.max(e.y1, yy); e.z0 = Math.min(e.z0, zz); e.z1 = Math.max(e.z1, zz);
    }));
    return e;
  }
  // 外貌 → 头的尺寸与形状
  function headSpec(look, H, fem) {
    const sp = look.spec || {}, F = sp.face || {};
    const war = sp.war != null ? sp.war : 60, intel = sp.intel != null ? sp.intel : 50;
    let tall = look.kind === 'soldier' ? 7.5 : fem ? 7.3 : 7.42 + 0.38 * clamp((war - 72) / 26, 0, 1) - (intel > war + 15 ? 0.04 : 0);
    if ((look.age || 30) >= 60) tall -= 0.05;
    const sz = 1 + 0.3 * (H / 1.8 - 1), fat = look.fat || 0;
    const jaw = F.jaw || 1;
    return {
      tall,
      W: (fem ? 0.146 : 0.155) * sz * clamp(F.w || 1, 0.92, 1.08) * (1 + 0.07 * fat),
      D: 0.19 * sz * (fem ? 0.96 : 1) * clamp(F.len || 1, 0.96, 1.05),
      chin: clamp((fem ? 1.0 : 1.25) + (jaw - 1) * 1.3 + 0.15 * fat, 0.85, 1.65),
      shape: { jaw: clamp(0.1 + (jaw - 1) * 0.25 + 0.05 * fat, 0.02, 0.2), chin: 0.06 * clamp(F.chin || 1, 0.7, 1.3), cheek: 0.05 + 0.03 * (F.cheek || 0) - 0.04 * (F.hollow || 0) + 0.04 * fat,
        brow: 0.04 + 0.025 * (F.brow || 0), temple: 0.04 },
    };
  }

  // 体型 → 关节位置（人物坐标，米）
  function makeProps(look) {
    const fem = look.sex === 'f';
    const h = (look.height || 1) * (fem ? 0.94 : 1);
    const k = h;
    const build = clamp(look.build || 1, 0.82, 1.3), fat = clamp(look.fat || 0, 0, 1);
    // 体格差异要拉开（许褚、典韦、张飞一眼可辨）：肩宽约 ±20%、四肢粗细、髋宽随 build / fat
    const bk = fem ? (build - 0.9) * 0.35 : (build - 1) * 0.85;
    const sw = (fem ? 0.86 : 1) * (1 + bk) * (1 + fat * 0.06);                      // 肩宽
    const hw = (fem ? 1.04 : 1) * (1 + bk * 0.4) * (1 + fat * 0.14);                // 髋宽
    const limb = (fem ? 0.86 : 1) * (1 + bk * 1.05) * (1 + fat * 0.22);             // 四肢粗细
    const H = 1.86 * k;
    const J = {};
    J.root = [0, 0, 0];
    J.pelvis = [0, 0.995 * k, 0];
    J.spine = [0, 1.09 * k, -0.006];
    J.chest = [0, 1.27 * k, -0.012];
    J.neck = [0, 1.5 * k, -0.022];
    // 头关节在寰椎处（头内、约耳垂高）：头部绕它点头 / 转头，不会离开脖子
    const hs = headSpec(look, H, fem);
    const G = headGrid(hs.chin, hs.shape, 12, 12), ex = headExtents(G);
    const headH = H / hs.tall, Ry = headH / (ex.y1 - ex.y0), Rz = hs.D / (ex.z1 - ex.z0), Rx = hs.W / (ex.x1 - ex.x0);
    const headC = [0, H - ex.y1 * Ry, 0.004];
    J.head = [0, headC[1] - 0.5 * Ry, headC[2] - 0.15 * Rz];
    const ad = [Math.sin(ARM_A), -Math.cos(ARM_A), 0];
    const upL = 0.3 * k, foL = 0.265 * k;
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R';
      J['clav' + S] = [0.035 * s * k, 1.455 * k, 0.0];
      J['upperArm' + S] = [0.188 * s * k * sw, 1.462 * k, -0.026];
      const d = [ad[0] * s, ad[1], 0];
      J['foreArm' + S] = v3madd(J['upperArm' + S], d, upL);
      J['hand' + S] = v3madd(J['foreArm' + S], d, foL);
      J['thigh' + S] = [0.092 * s * k * hw, 0.965 * k, 0];
      J['shin' + S] = [0.1 * s * k * hw, 0.53 * k, 0.012];
      J['foot' + S] = [0.106 * s * k * hw, 0.088 * k, -0.018];
    }
    J.cape1 = [0, 1.47 * k, -0.15 * sw];
    J.cape2 = [0, 1.1 * k, -0.2 * sw];
    J.cape3 = [0, 0.7 * k, -0.22 * sw];
    J.skirtF = [0, 1.02 * k, 0.12 * hw];
    J.skirtB = [0, 1.02 * k, -0.13 * hw];
    J.plume1 = [0, H + 0.01, headC[2] - 0.02];
    J.plume2 = [0, H + 0.14 * k, headC[2] - 0.08];
    J.hair1 = [0, headC[1] - 0.3 * Ry, headC[2] - 0.92 * Rz];
    J.shL = J.upperArmL.slice(); J.shR = J.upperArmR.slice(); J.dlL = J.upperArmL.slice(); J.dlR = J.upperArmR.slice();
    const head = { tall: hs.tall, H: headH, W: hs.W, D: hs.D, chin: hs.chin, shape: hs.shape, Rx, Ry, Rz, c: headC, pivot: J.head.slice() };
    return { J, k, sw, hw, limb, fat, fem, build, armDirL: ad, upL, foL, H, head };
  }

  // 握点挂点：右手握点在手腕沿前臂方向 7cm 处；挂点 +Y = 兵器杆方向（拇指一侧，绑定时朝前 +Z），
  // +Z = 指节方向（绑定时沿前臂向外下），+X = Y × Z。
  function gripLocal(pr, side) {
    const s = side === 'L' ? 1 : -1;
    const d = [pr.armDirL[0] * s, pr.armDirL[1], 0];
    const Y = [0, 0, 1], Z = d, X = v3cross(Y, Z);
    const m = new THREE.Matrix4().makeBasis(new THREE.Vector3(...X), new THREE.Vector3(...Y), new THREE.Vector3(...Z));
    const q = new THREE.Quaternion().setFromRotationMatrix(m);
    const p = v3mul(d, 0.072 * pr.k);
    return { p, q, X, Y, Z };
  }
  // 绑定姿势下挂点的人物坐标变换（把兵器 / 拳头几何放到绑定位置用）
  function socketRest(pr, side) {
    const g = gripLocal(pr, side);
    const J = pr.J['hand' + side];
    const m = new THREE.Matrix4().compose(new THREE.Vector3(J[0] + g.p[0], J[1] + g.p[1], J[2] + g.p[2]), g.q, new THREE.Vector3(1, 1, 1));
    return m;
  }

  // 每个模板共用的骨骼绑定逆矩阵（绑定旋转为单位 → 逆矩阵只有平移）
  function makeBoneInverses(pr) {
    return BONES.map(n => new THREE.Matrix4().makeTranslation(-pr.J[n][0], -pr.J[n][1], -pr.J[n][2]));
  }
  function makeBones(pr) {
    const bones = BONES.map(n => { const b = new THREE.Bone(); b.name = n; return b; });
    BONES.forEach((n, i) => {
      const p = PARENT[n];
      const J = pr.J[n], PJ = p ? pr.J[p] : [0, 0, 0];
      bones[i].position.set(J[0] - PJ[0], J[1] - PJ[1], J[2] - PJ[2]);
      if (p) bones[B[p]].add(bones[i]);
    });
    return bones;
  }

  // ======================================================== 外观设定 ==
  // 外貌参数（头像）→ 三维服饰层：躯干甲、腰裙、袖、护肩、护臂、腿、胫甲、靴、披风、前垂、领巾、腰带、冠帽、兵器、盾。
  // 第三版加 india（天竺）、africa（阿克苏姆、库施）、moor（毛里）；表里没有的文化按 SG.cultureBase（core.js）回退
  const CULTURES = ['han', 'nanman', 'wa', 'yi', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian', 'india', 'africa', 'moor'];
  const cultBase = c => (SG.cultureBase ? SG.cultureBase(c) : 'han');
  // 文化 → 本表中有条目的代号（先查自身，再查回退文化，最后 han）
  function cultOf(c, table) { return table[c] ? c : table[cultBase(c)] ? cultBase(c) : 'han'; }
  // 衣甲类型 → 各层（未写的层取缺省）
  const OUTFIT = {
    lamellar: { torso: 'lamLace', skirt: 'lamellar', skirtLen: 0.58, sleeves: 'cloth', pauldron: 'lamellar', bracer: 'metal', legs: 'trousers', greave: 'none', boots: 'boot', tabard: true, collar: 'scarf', belt: true, mirror: true },
    plate: { torso: 'plate', skirt: 'lamellar', skirtLen: 0.6, sleeves: 'cloth', pauldron: 'plate', bracer: 'metal', legs: 'trousers', greave: 'metal', boots: 'boot', tabard: true, collar: 'scarf', belt: true, mirror: true },
    // 袍甲（试点关羽）：札甲 + 护心镜，外罩前襟敞开的袍（米色镶边的领襟、立领），袍裙开衩到膝、两侧甲裙，袍袖到肘、护臂与皮手套，朱红束带打结垂下
    robearmor: { torso: 'lamellar', openRobe: true, chest: 'plate', skirt: 'splitrobe', skirtLen: 0.62, sleeves: 'elbow', pauldron: 'plate', bracer: 'metal', legs: 'trousers', boots: 'boot', tabard: false, collar: 'stand', belt: true, sashKnot: true },
    robe: { torso: 'robe', skirt: 'robe', skirtLen: 0.95, sleeves: 'wide', pauldron: 'none', bracer: 'none', legs: 'trousers', boots: 'shoe', belt: 'sash', collar: 'cross' },
    pelt: { torso: 'bare', pelt: 'tiger', skirt: 'kilt', skirtLen: 0.45, sleeves: 'bare', pauldron: 'none', bracer: 'gold', legs: 'bare', boots: 'sandal', belt: true },
    rattan: { torso: 'rattan', skirt: 'rattan', skirtLen: 0.5, sleeves: 'bare', pauldron: 'rattan', bracer: 'leather', legs: 'bare', boots: 'sandal', belt: true },
    bare: { torso: 'bare', skirt: 'kilt', skirtLen: 0.42, sleeves: 'bare', pauldron: 'none', bracer: 'leather', legs: 'trousers', boots: 'boot', belt: true },
    chiefrobe: { torso: 'bare', pelt: 'leopard', skirt: 'robe', skirtLen: 0.8, sleeves: 'bare', pauldron: 'fur', bracer: 'gold', legs: 'bare', boots: 'sandal', belt: true, cape: true },
    kantoui: { torso: 'cloth', skirt: 'tunic', skirtLen: 0.5, sleeves: 'short', pauldron: 'none', bracer: 'none', legs: 'hakama', boots: 'sandal', belt: 'sash' },
    tanko: { torso: 'tanko', skirt: 'bands', skirtLen: 0.5, sleeves: 'cloth', pauldron: 'bands', bracer: 'metal', legs: 'hakama', boots: 'boot', belt: true },
    yivest: { torso: 'bare', vest: true, skirt: 'kilt', skirtLen: 0.4, sleeves: 'bare', pauldron: 'none', bracer: 'none', legs: 'bare', boots: 'bare', belt: true },
    jacket: { torso: 'cloth', skirt: 'coat', skirtLen: 0.62, sleeves: 'cloth', pauldron: 'none', bracer: 'leather', legs: 'trousers', boots: 'boot', belt: true, collar: 'cross' },
    kaftan: { torso: 'cloth', skirt: 'coat', skirtLen: 0.66, sleeves: 'cloth', pauldron: 'none', bracer: 'leather', legs: 'trousers', boots: 'boot', belt: true, collar: 'fur', trimFur: true },
    fur: { torso: 'fur', skirt: 'coat', skirtLen: 0.6, sleeves: 'cloth', pauldron: 'fur', bracer: 'leather', legs: 'trousers', boots: 'boot', belt: true, collar: 'fur' },
    sash: { torso: 'bare', sash: true, skirt: 'sarong', skirtLen: 0.82, sleeves: 'bare', pauldron: 'none', bracer: 'gold', legs: 'bare', boots: 'bare', belt: 'sash', collar: 'gold' },
    scale: { torso: 'scale', skirt: 'scale', skirtLen: 0.68, sleeves: 'mail', pauldron: 'scale', bracer: 'metal', legs: 'trousers', boots: 'boot', belt: true },
    tunic: { torso: 'cloth', skirt: 'tunic', skirtLen: 0.55, sleeves: 'cloth', pauldron: 'none', bracer: 'none', legs: 'trousers', boots: 'boot', belt: true },
    robea: { torso: 'robe', skirt: 'robe', skirtLen: 0.95, sleeves: 'cloth', pauldron: 'none', bracer: 'none', legs: 'trousers', boots: 'shoe', belt: 'sash', cape: true },
    segmentata: { torso: 'segm', skirt: 'pteruges', skirtLen: 0.48, sleeves: 'short', pauldron: 'segm', bracer: 'none', legs: 'bare', boots: 'sandal', belt: true, cape: true, tunic: true, collar: 'scarf' },
    musculata: { torso: 'muscle', skirt: 'pteruges', skirtLen: 0.5, sleeves: 'short', pauldron: 'pteruges', bracer: 'none', legs: 'bare', greave: 'metal', boots: 'boot', belt: 'sash', cape: true, tunic: true },
    toga: { torso: 'robe', skirt: 'robe', skirtLen: 0.92, sleeves: 'short', pauldron: 'none', bracer: 'none', legs: 'bare', boots: 'sandal', belt: false, toga: true },
    plaid: { torso: 'cloth', skirt: 'tunic', skirtLen: 0.45, sleeves: 'cloth', pauldron: 'none', bracer: 'gold', legs: 'trousers', legsReg: 'plaid', boots: 'shoe', belt: true, cape: true, capeReg: 'plaid' },
    // 第三版：天竺腰布 + 帔帛（裸上身、臂钏、金项圈、赤足）· 絮甲（菱格绗缝的棉甲）· 阿克苏姆白麻袍（托加式披巾）·
    //         库施流苏披肩王袍 · 毛里连帽斗篷
    dhoti: { torso: 'bare', sash: true, skirt: 'sarong', skirtLen: 0.6, sleeves: 'bare', pauldron: 'none', bracer: 'gold', legs: 'bare', boots: 'bare', belt: 'sash', collar: 'gold', armlet: true },
    quilted: { torso: 'quilt', skirt: 'coat', skirtLen: 0.62, sleeves: 'short', pauldron: 'none', bracer: 'leather', legs: 'trousers', boots: 'sandal', belt: 'sash', collar: 'scarf', armlet: true },
    shamma: { torso: 'robe', skirt: 'robe', skirtLen: 0.9, sleeves: 'short', pauldron: 'none', bracer: 'none', legs: 'bare', boots: 'sandal', belt: false, toga: true, sash: true, white: true },
    meroitic: { torso: 'robe', skirt: 'robe', skirtLen: 0.95, sleeves: 'short', pauldron: 'none', bracer: 'gold', legs: 'bare', boots: 'sandal', belt: 'sash', sash: true, collar: 'gold', white: true },
    burnous: { torso: 'cloth', skirt: 'tunic', skirtLen: 0.6, sleeves: 'cloth', pauldron: 'none', bracer: 'none', legs: 'trousers', boots: 'sandal', belt: true, cape: true, collar: 'scarf' },
  };
  // 冠帽类型映射到三维头饰构建器（未专门实现的归入相近类型）
  const HAT_MAP = {
    none: 'none', helmet: 'helmet', ze: 'ze', jinxian: 'jinxian', futou: 'futou', turban: 'turban', headband: 'headband', lunjin: 'lunjin', wuguan: 'ze',
    crown: 'crown', pheasant: 'pheasant', daoist: 'crown', feathers: 'feathers', chief: 'chief', wahelm: 'wahelm', shaman: 'shaman', yiband: 'headband',
    jinze: 'jinze', jeolpung: 'jeolpung', birdfeather: 'jeolpung', goldcrown: 'goldcrown', felt: 'felt', fur: 'furhat', furcap: 'furhat', tallcrown: 'tallcrown',
    flowerwrap: 'turban', pointcap: 'pointcap', tallhat: 'tallhat', diadem: 'diadem', tiara: 'tiara', phrygian: 'phrygian', keffiyeh: 'keffiyeh',
    arabturban: 'turban', hatra: 'tallhat', laurel: 'laurel', galea: 'galea', celthelm: 'celthelm', spangen: 'spangen', conical: 'conical',
    pagri: 'pagri', mukuta: 'mukuta', jewelband: 'diadem', sinhalacrown: 'bellcrown', kushcrown: 'kushcrown', aksumcrown: 'aksumcrown', featherband: 'featherband',
  };
  // 名将：三维专属设定（兵器、披风、前垂、饰物）
  const SIG = {
    '关羽': { weapon: 'guandao', metal: '#D9A441', beardLen: 1.35, hat: 'jinze', cloth: '#3F7F4A', skin: '#B9483A' },
    '张飞': { weapon: 'snake', tabard: '#2a2a30', cape: '#3a1f1a', metal: '#4a4c56' },
    '吕布': { weapon: 'halberd', cape: '#b02a24', tabard: '#b02a24', metal: '#d8a848', ribbons: '#d03028', pauldron: 'lion' },
    '赵云': { weapon: 'yajiao', cape: '#eef0f4', tabard: '#f4f4f8', metal: '#c8ced8', ribbons: '#e8eef8' },
    '马超': { weapon: 'hutou', cape: '#f0f0f4', tabard: '#e8e8f0', metal: '#d4d8e0', pauldron: 'lion' },
    '刘备': { weapon: 'swords' },
    '曹操': { weapon: 'yitian', cape: 'faction' },
    '孙策': { weapon: 'bawang', cape: '#c8382c', tabard: '#c8382c' },
    '孙权': { weapon: 'jian', cape: '#c8382c' },
    '孙坚': { weapon: 'guding', cape: '#b02a24' },
    '典韦': { weapon: 'twinji', sleeves: 'bare', pauldron: 'lion' },
    '许褚': { weapon: 'bigblade' },
    '黄忠': { weapon: 'fengzui', backBow: true, quiver: true },
    '太史慈': { weapon: 'shortji' },
    '夏侯惇': { weapon: 'spear', cape: 'faction' },
    '夏侯渊': { weapon: 'dao', backBow: true, quiver: true },
    '徐晃': { weapon: 'bigaxe' },
    '诸葛亮': { weapon: 'fan' },
    '周瑜': { weapon: 'jian', cape: '#c8382c' },
    '甘宁': { weapon: 'dao', bells: true },
    '祝融': { weapon: 'twindao' },
    '孟获': { weapon: 'bigaxe' },
    '张辽': { weapon: 'poleblade' }, '颜良': { weapon: 'poleblade' }, '文丑': { weapon: 'spear' }, '庞德': { weapon: 'poleblade' },
    '魏延': { weapon: 'poleblade' }, '关平': { weapon: 'poleblade' }, '周仓': { weapon: 'poleblade' }, '姜维': { weapon: 'spear' }, '张郃': { weapon: 'spear' },
    '凌统': { weapon: 'twindao' }, '华雄': { weapon: 'bigaxe' }, '董卓': { weapon: 'jian' }, '袁绍': { weapon: 'jian', cape: 'faction' }, '公孙瓒': { weapon: 'spear', cape: '#f0f0f4' },
    '高顺': { weapon: 'spear' }, '纪灵': { weapon: 'trident' }, '张绣': { weapon: 'spear' }, '潘凤': { weapon: 'bigaxe' }, '吕蒙': { weapon: 'dao' }, '周泰': { weapon: 'dao' },
    '兀突骨': { weapon: 'mace' }, '沙摩柯': { weapon: 'mace' }, '孟优': { weapon: 'mace' }, '带来洞主': { weapon: 'spear' },
  };
  // 头像外貌参数中的兵器 → 三维兵器
  const PWEAPON = { guandao: 'guandao', snake: 'snake', halberd: 'halberd', spear: 'spear', ji: 'twinji', bow: 'bow', sword: 'jian', swords: 'swords', axe: 'bigaxe' };
  // 文化缺省：兵器（按 武力 高低）与盾
  const CULT_WEAPON = {
    han: { hi: ['spear', 'poleblade', 'spear', 'halberd'], mid: ['dao', 'jian', 'spear'], lo: ['jian'] },
    nanman: { hi: ['bigblade', 'mace', 'spear'], mid: ['mace', 'dao'], lo: ['dao'] },
    wa: { hi: ['tachi', 'spear'], mid: ['tachi'], lo: ['tachi'] },
    yi: { hi: ['spear'], mid: ['spear', 'dao'], lo: ['dao'] },
    korea: { hi: ['spear', 'dao'], mid: ['dao'], lo: ['jian'] },
    steppe: { hi: ['spear', 'dao'], mid: ['dao'], lo: ['dao'] },
    seasia: { hi: ['spear', 'dao'], mid: ['dao'], lo: ['dao'] },
    tarim: { hi: ['jian', 'spear'], mid: ['jian'], lo: ['jian'] },
    kushan: { hi: ['lance', 'spatha'], mid: ['spatha'], lo: ['spatha'] },
    persia: { hi: ['lance', 'mace'], mid: ['spatha', 'mace'], lo: ['spatha'] },
    arab: { hi: ['spear', 'spatha'], mid: ['spatha'], lo: ['spatha'] },
    roman: { hi: ['gladius', 'spatha'], mid: ['gladius'], lo: ['gladius'] },
    celt: { hi: ['spatha'], mid: ['spatha'], lo: ['spatha'] },
    german: { hi: ['handaxe', 'spear'], mid: ['spear', 'handaxe'], lo: ['handaxe'] },
    sarmatian: { hi: ['lance'], mid: ['lance', 'spatha'], lo: ['spatha'] },
    india: { hi: ['khanda', 'spear'], mid: ['khanda'], lo: ['khanda'] },
    africa: { hi: ['spear', 'spatha'], mid: ['spear'], lo: ['spatha'] },
    moor: { hi: ['spear'], mid: ['spear', 'spatha'], lo: ['spatha'] },
  };
  const CULT_SHIELD = { han: 'pai', nanman: 'rattan', wa: 'wa', yi: 'round', korea: 'round', steppe: 'round', seasia: 'round', tarim: 'round', kushan: 'round', persia: 'round', arab: 'round', roman: 'scutum', celt: 'oval', german: 'round', sarmatian: 'round',
    india: 'long', africa: 'hide', moor: 'round' };

  // 简化外貌（没有头像模块时）
  function fallbackSpec(gen) {
    const name = gen.name || '无名', r = Rng(hashStr(name));
    const war = gen.war == null ? 60 : gen.war, intel = gen.intel == null ? 50 : gen.intel;
    const cult = gen.culture || 'han';
    const role = war >= 70 ? 'warrior' : intel >= 75 ? 'strategist' : 'general';
    const skins = { han: '#e2b48c', nanman: '#a8714c', seasia: '#a06c40', roman: '#e6c09e', celt: '#f0d0b8', german: '#ecccb2', arab: '#c48e5e', persia: '#ca9a72', india: '#a8724a', africa: '#6c4026', moor: '#b07e54' };
    return {
      name, culture: cult, sex: gen.sex || 'm', role, age: 34, war, intel, pol: gen.pol || 50, fat: 0, face: {}, skin: skins[cult] || '#e2b48c', build: 1 + (war - 60) / 300,
      eyes: { shape: r.pick(['normal', 'sharp', 'narrow']), color: '#3a2618' }, brows: { shape: r.pick(['straight', 'angled', 'bushy']), thick: 1.1, color: '#221a18' },
      nose: {}, mouth: {}, ears: {}, hair: { style: cult === 'roman' ? 'short' : 'topknot', color: '#221a18' },
      beard: { style: r.pick(['none', 'short', 'goatee', 'full']), len: 1, color: '#221a18' }, mustache: { style: 'thin' },
      hat: { type: role === 'strategist' ? 'jinxian' : cult === 'roman' ? 'galea' : 'helmet', variant: 'round', plume: '#c0302a' },
      outfit: { type: role === 'strategist' ? 'robe' : cult === 'roman' ? 'segmentata' : 'lamellar', color: '#7a2a2a', color2: '#3a2a2a', metal: '#7a808c' },
      acc: [], marks: [], weapon: null, expr: 'stern', key: 'fb:' + name,
    };
  }
  function resolveGen(gen) {
    if (typeof gen === 'string') {
      const n = gen;
      gen = null;
      if (SG.Portrait && SG.Portrait.find) gen = SG.Portrait.find(n);
      gen = gen || { name: n };
    }
    return gen || { name: '无名' };
  }
  function specOf(gen) {
    if (SG.Portrait && SG.Portrait.spec) { try { return SG.Portrait.spec(gen); } catch (e) { /* 回退 */ } }
    return fallbackSpec(gen);
  }

  // 武将外观
  function lookOfGeneral(gen, opts) {
    gen = resolveGen(gen);
    opts = opts || {};
    const g2 = opts.culture && !gen.culture ? Object.assign({}, gen, { culture: opts.culture }) : gen;
    const sp = specOf(g2);
    const name = sp.name || gen.name;
    const sig = SIG[name] || {};
    const culture = opts.culture || sp.culture || 'han';
    const factionHex = opts.color || gen.color || '#8a2a24';
    const faction = TEAM;
    const of = sp.outfit || {}, ot = OUTFIT[of.type] ? of.type : 'lamellar';
    const L = Object.assign({}, OUTFIT[ot]);
    const war = sp.war != null ? sp.war : 60;
    const metal = sig.metal || of.metal || (ot === 'plate' ? '#9aa0aa' : '#6a6e78');
    const cloth = of.color || faction;
    const look = {
      kind: 'general', name, culture, sex: sp.sex || 'm', age: sp.age || 32, spec: sp, faction, factionHex,
      height: (sp.sex === 'f' ? 1 : 1.0 + clamp((war - 70) / 600, -0.03, 0.05)) * (name === '许褚' || name === '典韦' ? 1.03 : 1),
      build: sp.build || 1, fat: sp.fat || 0,
      skin: sp.skin || '#e2b48c',
      hair: Object.assign({ style: 'topknot', color: '#1c1816' }, sp.hair),
      beard: Object.assign({ style: 'none', len: 1 }, sp.beard), mus: Object.assign({ style: 'none' }, sp.mustache),
      hat: Object.assign({}, sp.hat || {}, { type: HAT_MAP[(sp.hat && sp.hat.type) || 'none'] || 'none', ptype: (sp.hat && sp.hat.type) || 'none' }),
      acc: sp.acc || [], marks: sp.marks || [],
      C: {
        faction, cloth, cloth2: of.color2 || shade(cloth, -0.35), metal, trim: of.trim || GOLD,
        leather: culture === 'han' ? DLEATHER : LEATHER, skin: sp.skin || '#e2b48c',
        cape: null, tabard: null, plume: (sp.hat && sp.hat.plume) || RED, trousers: of.trousers || null,
      },
      L,
      weapon: opts.weapon || sig.weapon || PWEAPON[sp.weapon] || null,
      shield: null, lod: opts.lod || 'high', sig,
      backBow: !!sig.backBow, quiver: !!sig.quiver, ribbons: sig.ribbons || null,
    };
    if (of.pelt) L.pelt = of.pelt;
    // 白麻袍（阿克苏姆 / 库施）：袍身白色，原衣色作披巾 / 镶边；毛里斗篷：衣色作斗篷，第二色作内衫
    if (L.white) { look.C.cloth = of.toga || '#ece6d8'; look.C.cloth2 = of.color && !/^#e[0-9a-f]e[0-9a-f]d[0-9a-f]$/i.test(of.color) ? of.color : (of.color2 || '#a01818'); }
    if (ot === 'burnous') { look.C.cape = of.color || '#e8e0cc'; look.C.cloth = of.color2 || '#c8b48c'; }
    // 袈裟 / 白衣（第三版的僧人：龙树、提婆、童受、伊兰戈）：头像里 outfit.toga 为袍色
    if (ot === 'toga' && of.toga) look.C.cloth = of.toga;
    if ((culture === 'africa' || culture === 'india') && L.torso === 'bare' && L.legs === 'trousers') { L.legs = 'bare'; L.boots = 'sandal'; }
    // 第三版修订：安息 / 萨尔马特将领（头像里是长衫）在三维里披鳞甲（铁甲骑兵贵族）；贵霜长袍加鳞片护肩、护胸与护臂——
    // 不再是一件纯色的「睡衣」
    {
      const cb = cultBase(culture);
      if ((cb === 'persia' || cb === 'sarmatian') && (ot === 'tunic' || ot === 'jacket')) Object.assign(L, { torso: 'scale', pauldron: 'scale', skirt: 'scale', skirtLen: 0.7, sleeves: 'mail', bracer: 'metal' });
      if (cb === 'kushan' && ot === 'kaftan') Object.assign(L, { pauldron: 'scale', chest: 'plate', bracer: 'metal' });
    }
    if (sig.sleeves) L.sleeves = sig.sleeves;
    if (sig.pauldron) L.pauldron = sig.pauldron;
    if (sig.hat) look.hat.type = sig.hat;
    if (sig.cloth) look.C.cloth = sig.cloth;
    if (sig.skin) { look.skin = sig.skin; look.C.skin = sig.skin; }
    // 披风 / 前垂颜色
    const capeSpec = sig.cape || of.cape || (L.cape ? 'faction' : null);
    // 自带披挂的第三版衣装（帔帛、白麻披巾、流苏披肩、斗篷）不再加君主的势力色披风（与头像一致）
    if (capeSpec && !(capeSpec === 'faction' && (ot === 'burnous' || ot === 'dhoti' || ot === 'shamma' || ot === 'meroitic'))) look.C.cape = capeSpec === 'faction' ? faction : capeSpec;
    // 库施王袍：外披衣色的流苏披肩（与阿克苏姆的白麻袍区分开）
    if (ot === 'meroitic') look.C.cape = of.color || '#a01818';
    if (L.tabard) look.C.tabard = sig.tabard || (of.color && ot !== 'lamellar' ? of.color : faction);
    if (sig.beardLen) look.beard.len = sig.beardLen;
    // 女性与文官：去掉多余的甲
    if (look.sex === 'f' && (ot === 'robe')) { L.legs = 'trousers'; }
    // 兵器缺省
    if (!look.weapon) {
      const cw = CULT_WEAPON[cultOf(culture, CULT_WEAPON)];
      const r = Rng(hashStr(name + 'w'));
      if ((sp.acc || []).includes('fan')) look.weapon = 'fan';
      else look.weapon = r.pick(war >= 80 ? cw.hi : war >= 60 ? cw.mid : cw.lo);
    }
    // 弓：主兵器为刀剑，背弓
    if (look.weapon === 'bow') { look.weapon = culture === 'han' ? 'dao' : CULT_WEAPON[cultOf(culture, CULT_WEAPON)].lo[0]; look.backBow = true; look.quiver = true; }
    // 天竺的「长剑」是坎达剑
    if (look.weapon === 'jian' && culture === 'india' && !sig.weapon && !opts.weapon) look.weapon = 'khanda';
    // 第三版修订：光头、着袈裟 / 白衣的僧人（龙树、提婆、伊兰戈）持锡杖，不佩刀剑
    if (look.hair.style === 'bald' && ot === 'toga' && !sig.weapon && !opts.weapon && !sp.weapon) { look.weapon = 'staff'; look.backBow = false; look.quiver = false; }
    if (opts.shield) look.shield = opts.shield === true ? (CULT_SHIELD[cultOf(culture, CULT_SHIELD)] || 'round') : opts.shield;
    return look;
  }

  // --------------------------------------------------------- 士兵外观 --
  // 各文化士兵：躯干 / 腰裙 / 冠帽 / 腿 / 盾；按兵种（枪 spear、刀盾 sword、弓 archer、骑 cavalry）微调
  const SOLDIER = {
    // 汉：枪兵 = 试点的汉军枪兵（红抹额、发髻，札甲护肩，米色裤与褐色绑腿）；刀盾手与骑兵戴盔；弓手着短衣、帻
    han: { outfit: 'lamellar', hat: { type: 'helmet', variant: 'round' }, spear: { outfit: 'lamellar', hat: { type: 'headband', color: '#C2402E' } }, archer: { outfit: 'jacket', hat: { type: 'ze' } }, skin: '#e2b48c', hair: '#1c1816', metal: '#5c626e' },
    nanman: { outfit: 'rattan', hat: { type: 'feathers' }, archer: { outfit: 'bare', hat: { type: 'headband' } }, skin: '#a8714c', hair: '#141012', hairStyle: 'mallet', marks: ['warpaint'] },
    wa: { outfit: 'tanko', hat: { type: 'wahelm' }, archer: { outfit: 'kantoui', hat: { type: 'none' } }, skin: '#e6bd96', hair: '#16131a', hairStyle: 'mizura', metal: '#4a4e58' },
    yi: { outfit: 'yivest', hat: { type: 'headband' }, skin: '#c99666', hair: '#141012', hairStyle: 'long', marks: ['tattoo-yi'] },
    korea: { outfit: 'lamellar', hat: { type: 'helmet', variant: 'plume' }, archer: { outfit: 'jacket', hat: { type: 'jeolpung' } }, skin: '#e8c09c', hair: '#16131a', metal: '#5a5e68' },
    steppe: { outfit: 'kaftan', hat: { type: 'felt' }, cavalry: { outfit: 'lamellar', hat: { type: 'helmet', variant: 'round' } }, skin: '#dcae84', hair: '#1a1614', hairStyle: 'kunfa', metal: '#5a5e68' },
    seasia: { outfit: 'sash', hat: { type: 'none' }, skin: '#a06c40', hair: '#141012', hairStyle: 'highbun' },
    tarim: { outfit: 'kaftan', hat: { type: 'pointcap' }, cavalry: { outfit: 'scale', hat: { type: 'helmet', variant: 'spike' } }, skin: '#ebc6a2', hair: '#3a2618', hairStyle: 'bob' },
    kushan: { outfit: 'kaftan', hat: { type: 'tallhat' }, cavalry: { outfit: 'scale', hat: { type: 'helmet', variant: 'spike' } }, skin: '#dcb08a', hair: '#2a1e16', hairStyle: 'long' },
    persia: { outfit: 'scale', hat: { type: 'conical' }, archer: { outfit: 'tunic', hat: { type: 'phrygian' } }, skin: '#d6a880', hair: '#16131a', hairStyle: 'bushy', metal: '#7a808c' },
    arab: { outfit: 'robea', hat: { type: 'keffiyeh' }, cavalry: { outfit: 'scale', hat: { type: 'keffiyeh' } }, skin: '#c48e5e', hair: '#141012', hairStyle: 'curly' },
    roman: { outfit: 'segmentata', hat: { type: 'galea' }, archer: { outfit: 'tunic', hat: { type: 'conical' } }, cavalry: { outfit: 'scale', hat: { type: 'galea' } }, skin: '#e6c09e', hair: '#2a1e16', hairStyle: 'short', metal: '#9aa0aa' },
    celt: { outfit: 'plaid', hat: { type: 'none' }, spear: { outfit: 'plaid', hat: { type: 'celthelm' } }, skin: '#f0d0b8', hair: '#a8481e', hairStyle: 'wild', marks: ['woad'] },
    german: { outfit: 'fur', hat: { type: 'none' }, cavalry: { outfit: 'tunic', hat: { type: 'spangen' } }, skin: '#ecccb2', hair: '#c8a060', hairStyle: 'suebian' },
    sarmatian: { outfit: 'scale', hat: { type: 'conical' }, skin: '#e6c4a2', hair: '#5a3a22', hairStyle: 'long', metal: '#7a808c' },
    // 天竺：缠头、腰布赤足（刀盾手持长盾与坎达剑），枪兵与骑兵着絮甲，弓手用竹长弓（桑奇、阿马拉瓦蒂浮雕）
    india: { outfit: 'dhoti', hat: { type: 'pagri' }, spear: { outfit: 'quilted', hat: { type: 'pagri' } }, cavalry: { outfit: 'quilted', hat: { type: 'pagri' } }, archer: { outfit: 'dhoti', hat: { type: 'headband' } },
      skin: '#a8724a', hair: '#141012', hairStyle: 'long', longbow: true },
    // 阿克苏姆 / 库施：白麻袍、长枪与长皮盾；弓手赤膊、额带插羽、长弓（“弓之国”）
    africa: { outfit: 'shamma', hat: { type: 'none' }, archer: { outfit: 'bare', hat: { type: 'featherband' } }, cavalry: { outfit: 'shamma', hat: { type: 'headband' } },
      skin: '#6c4026', hair: '#0c0a0a', hairStyle: 'tightcurl', longbow: true },
    // 毛里：短衣斗篷、标枪与小圆盾、螺旋卷发
    moor: { outfit: 'burnous', hat: { type: 'none' }, archer: { outfit: 'tunic', hat: { type: 'none' } }, skin: '#b07e54', hair: '#141012', hairStyle: 'ringlets' },
  };
  const SOLDIER_WEAPON = {
    spear: { han: 'spear', nanman: 'spear', wa: 'spear', yi: 'spear', korea: 'spear', steppe: 'spear', seasia: 'spear', tarim: 'spear', kushan: 'lance', persia: 'lance', arab: 'spear', roman: 'pilum', celt: 'spear', german: 'spear', sarmatian: 'lance', india: 'spear', africa: 'spear', moor: 'spear' },
    sword: { han: 'dao', nanman: 'dao', wa: 'tachi', yi: 'dao', korea: 'dao', steppe: 'dao', seasia: 'dao', tarim: 'jian', kushan: 'spatha', persia: 'spatha', arab: 'spatha', roman: 'gladius', celt: 'spatha', german: 'handaxe', sarmatian: 'spatha', india: 'khanda', africa: 'spatha', moor: 'spatha' },
    cavalry: { han: 'spear', nanman: 'spear', wa: 'spear', yi: 'spear', korea: 'spear', steppe: 'spear', seasia: 'spear', tarim: 'lance', kushan: 'lance', persia: 'lance', arab: 'spear', roman: 'spear', celt: 'spear', german: 'spear', sarmatian: 'lance', india: 'spear', africa: 'spear', moor: 'spear' },
  };
  function lookOfSoldier(opts) {
    const type = opts.type || 'spear', culture = cultOf(opts.culture, SOLDIER);
    const S = SOLDIER[culture], T = S[type] || {};
    const factionHex = opts.color || '#8a2a24';
    const faction = TEAM;
    const seed = (opts.seed | 0) % 3;
    const r = Rng(hashStr(culture + type) + seed * 7919);
    const ot = T.outfit || S.outfit;
    const L = Object.assign({}, OUTFIT[ot]);
    // 士兵简化：不用胫甲 / 护心镜 / 飘带
    L.greave = 'none'; L.mirror = false;
    if ((culture === 'africa' || culture === 'india') && L.torso === 'bare' && L.legs === 'trousers') { L.legs = 'bare'; L.boots = 'sandal'; }
    if (type === 'archer') { L.pauldron = 'none'; L.tabard = false; }
    const hat = Object.assign({ color: null }, T.hat || S.hat);
    hat.type = HAT_MAP[hat.type] || hat.type;
    const metal = S.metal || '#6a6e78';
    const look = {
      kind: 'soldier', type, culture, name: '', sex: 'm', age: 30, spec: null, faction, factionHex, seed,
      height: 0.97 + r.range(-0.02, 0.02), build: 1 + r.range(-0.04, 0.06), fat: 0,
      skin: hex(mix(S.skin, r.pick(['#d8a080', '#f0d0b0', '#b88060']), r.range(0, 0.18))),
      hair: { style: S.hairStyle || 'topknot', color: S.hair || '#1c1816' },
      beard: { style: ['none', 'short', 'full', 'stubble'][seed % 4], len: 1, color: S.hair }, mus: { style: 'none' },
      hat, acc: [], marks: S.marks || [], face: 'face' + 'ABCD'[(seed + (culture.length % 2)) % 4],
      C: { faction, cloth: faction, cloth2: shade(faction, -0.4), metal, trim: culture === 'han' || culture === 'korea' ? '#b89048' : BRONZE, leather: LEATHER, skin: S.skin, cape: null, tabard: L.tabard ? faction : null, plume: faction },
      L,
      weapon: type === 'archer' ? (S.longbow ? 'longbow' : 'bow') : SOLDIER_WEAPON[type === 'cavalry' ? 'cavalry' : type][culture],
      shield: type === 'sword' ? CULT_SHIELD[culture] : (culture === 'roman' && type === 'spear' ? 'scutum' : null),
      quiver: type === 'archer', lod: opts.lod || 'low',
    };
    if (L.cape && culture === 'roman') look.C.cape = '#a02a24';
    else if (L.cape) look.C.cape = shade(faction, -0.2);
    // 白麻袍的士兵：袍白、镶边 / 披肩为势力色（势力色仍清楚可辨）
    if (L.white) { look.C.cloth = '#e8e2d2'; look.C.cloth2 = faction; }
    if (culture === 'india' || culture === 'africa') look.beard.style = seed === 1 ? 'short' : 'none';
    if (culture === 'celt' || culture === 'german' || culture === 'nanman' || culture === 'seasia' || culture === 'yi') look.beard.style = seed === 0 ? 'none' : 'full';
    if (culture === 'roman') look.beard.style = seed === 2 ? 'short' : 'none';
    return look;
  }


  // 躯干截面（y, 半宽, 半厚, 中心 z, 方度）：双手 IK 的身体避让用（_avoidBody）
  function torsoProfile(pr) {
    const base = [
      [0.84, 0.112, 0.094, 0.000, 2.1], [0.9, 0.146, 0.11, -0.004, 2.3], [0.97, 0.166, 0.118, -0.008, 2.4], [1.04, 0.16, 0.112, -0.004, 2.3],
      [1.11, 0.149, 0.106, 0.002, 2.2], [1.19, 0.155, 0.111, 0.006, 2.2], [1.27, 0.168, 0.12, 0.01, 2.3], [1.34, 0.182, 0.126, 0.012, 2.4],
      // 肩线：方度降低（不再是方箱肩），1.485–1.54 为斜方肌坡，颈根更粗 → 脖子不显长
      [1.4, 0.19, 0.121, 0.004, 2.45], [1.445, 0.178, 0.107, -0.008, 2.35], [1.485, 0.148, 0.093, -0.016, 2.2], [1.515, 0.106, 0.077, -0.02, 2.05], [1.545, 0.07, 0.065, -0.022, 2.0],
    ];
    const k = pr.k;
    return base.map(([y, rx, rz, zc, sq]) => {
      const up = sstep(1.05, 1.4, y);
      let X = rx * lerp(pr.hw, pr.sw, up), Z = rz * lerp(1 + pr.fat * 0.08, 0.86 + 0.14 * pr.build + pr.fat * 0.06, up), C = zc;
      if (y > 1.5) X = lerp(X, rx * (1 + (pr.sw - 1) * 0.5), sstep(1.5, 1.545, y));        // 颈根随体格只放宽一半
      const belly = gauss(y, 1.12, 0.12) * pr.fat;
      X += belly * 0.045; Z += belly * 0.1; C += belly * 0.07;
      if (pr.fem) { X *= 1 - 0.12 * gauss(y, 1.13, 0.07); Z *= 1 - 0.06 * gauss(y, 1.13, 0.07); }
      return { y: y * k, rx: X * k, rz: Z * k, zc: C * k, sq };
    });
  }
  function ringAt(R, y) {
    if (y <= R[0].y) return R[0];
    if (y >= R[R.length - 1].y) return R[R.length - 1];
    let i = 0;
    while (i < R.length - 2 && R[i + 1].y < y) i++;
    const a = R[Math.max(0, i - 1)], b = R[i], c = R[i + 1], d = R[Math.min(R.length - 1, i + 2)];
    const t = (y - b.y) / (c.y - b.y);
    const cr = (p0, p1, p2, p3) => { const t2 = t * t, t3 = t2 * t; return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3); };
    return { y, rx: cr(a.rx, b.rx, c.rx, d.rx), rz: cr(a.rz, b.rz, c.rz, d.rz), zc: cr(a.zc, b.zc, c.zc, d.zc), sq: lerp(b.sq, c.sq, t) };
  }

  // ======================================================== 低多边形人形工具（Kit） ==
  // 按《Lanternvale》BipedKit 的做法（经 js/lp-pilot.js 的试点调定）在本文件的人形骨架上建模：每个部件刚性蒙皮在一根骨上，
  // 铰接部件（袍裙侧片、披风、胡须、长发）按顶点位置分骨（MeshBuilder.boneBy）；面朝 +Z、Y 向上、人物左侧为 +X（右手在 −X）。
  // 绑定姿势：A 字（双臂外展 40°，肘直），全部骨骼绑定旋转为单位。
  // LOD：high（单挑、特写；≤ 3.1k 三角形，名将 ≤ 3.6k）、mid（战场主将、攻击画面将领；≤ 1.8k）、low（士兵；≤ 900）。
  const LODK = {
    high: { lim: 7, thigh: 8, tor: 8, hq: 12, hs: 12, eye: 2, ears: 1, pan: 3, det: 2, rnd: 8, fist: [7, 4], sph: [7, 4] },
    mid: { lim: 6, thigh: 6, tor: 8, hq: 9, hs: 10, eye: 1, ears: 1, pan: 2, det: 1, rnd: 6, fist: [5, 2], sph: [5, 2] },
    low: { lim: 5, thigh: 5, tor: 6, hq: 7, hs: 8, eye: 1, ears: 0, pan: 2, det: 0, rnd: 6, fist: [5, 2], sph: [5, 2] },
  };
  const TRI_BUDGET = { high: 3100, hero: 3600, mid: 1800, low: 900 };
  // 超出预算时逐级压缩（细节层次降级，边数减少）
  const SQUEEZE = {
    high: [{ eye: 2, det: 2, pan: 2, rnd: 7 }, { lim: 6, thigh: 6, det: 1, rnd: 7, sph: [6, 3] }, { hq: 9, hs: 10, lim: 6, thigh: 6, det: 1, pan: 2, rnd: 6, sph: [6, 3], fist: [6, 3] }],
    mid: [{ lim: 5, thigh: 5, tor: 7, sph: [5, 2], fist: [5, 2] }, { hq: 8, hs: 9, lim: 5, thigh: 5, tor: 6, rnd: 5, sph: [4, 2], fist: [4, 2] },
      { hq: 7, hs: 8, lim: 4, thigh: 5, tor: 6, rnd: 5, pan: 1, ears: 0, sph: [4, 2], fist: [4, 2] }, { hq: 7, hs: 8, lim: 5, thigh: 5, tor: 6, det: 0, rnd: 5, sph: [4, 2], fist: [4, 2] }],
    low: [{ rnd: 5, sph: [4, 2], fist: [4, 2] }, { lim: 4, thigh: 5, rnd: 5, sph: [4, 2], fist: [4, 2], tor: 6 }, { lim: 4, thigh: 4, tor: 6, rnd: 5, pan: 1, sph: [4, 2], fist: [4, 2], hs: 8, hq: 7 }],
  };

  class Kit {
    constructor(look, pr, D, team, seed) {
      const L = LPX();
      this.L = L; this.P = L.Paint; this.V = L.V;
      this.look = look; this.pr = pr; this.D = D;
      this.team = team;
      const FB = FigBuilder();
      const M = this.M = new FB(seed || 1);
      M.jitter = 0.05;
      const J = this.J = pr.J, k = this.k = pr.k;
      this.BI = B;
      const b = this.bulk = clamp(pr.build * (pr.fem ? 0.9 : 1), 0.84, 1.3), fat = this.fat = pr.fat;
      this.fem = pr.fem;
      // 头：按 pr.head 的尺寸（与 LOD 无关），网格按 LOD 取环 / 列数
      const HS = pr.head;
      this.headG = headGrid(HS.chin, HS.shape, D.hq, D.hs);
      const ex = headExtents(this.headG);
      this.headH = HS.H;
      this.Rx = HS.W / (ex.x1 - ex.x0); this.Ry = HS.H / (ex.y1 - ex.y0); this.Rz = HS.D / (ex.z1 - ex.z0);
      this.Rzu = this.Rz / 0.96;
      this.R = this.Rx * (look.kind === 'soldier' ? 1 : 1.02);
      this.crownY = pr.H;
      this.headC = [0, this.crownY - ex.y1 * this.Ry, 0.004];
      this.headCY = this.headC[1] - 0.03 * this.Ry;
      this.headPivot = HS.pivot;
      this.upL = pr.upL; this.foL = pr.foL;
      this.hipY = J.thighL[1]; this.pelvisY = J.pelvis[1] + 0.03 * k; this.spineY = J.spine[1]; this.chestY = J.chest[1];
      this.shoulderY = J.upperArmL[1]; this.shoulderX = Math.abs(J.upperArmL[0]); this.neckY = J.neck[1];
      this.waistR = 0.13 * k * b * (1 + 0.5 * fat) * (pr.fem ? 0.88 : 1);
      this.chestR = 0.165 / 0.188 * this.shoulderX * lerp(1, b, 0.35) * (1 + 0.12 * fat);
      this.shoulderR = 0.9 * this.shoulderX;
      this.hipR = 0.138 * k * pr.hw * Math.pow(b, 0.3);
      this.depthK = 0.74 + 0.1 * fat;
      const lk = pr.limb / (pr.fem ? 0.86 : 1);
      this.armR = 0.054 * k * lk * (pr.fem ? 0.84 : 1); this.foreR = 0.045 * k * lk * (pr.fem ? 0.86 : 1);
      this.thighR = 0.08 * k * lk * (pr.fem ? 0.92 : 1); this.shinR = 0.057 * k * lk * (pr.fem ? 0.9 : 1);
      this.footLen = 0.25 * k * lerp(1, b, 0.4) * (pr.fem ? 0.9 : 1); this.footW = 0.1 * k * lerp(1, b, 0.5) * (pr.fem ? 0.9 : 1);
      this.parts = {}; this._last = 0;
    }
    bone(n) { this.M.bone = B[n]; return this; }
    /** Tapered limb a → b with optional end caps (caps are fans of `n` triangles: the low LOD leaves hidden ends open). */
    seg(a, b, ra, rb, n, capA = true, capB = true) {
      const M = this.M, axis = this.V.sub(b, a), len = this.V.len(axis);
      if (len < 1e-5) return;
      M.aim(a, axis); M.lathe([[ra, 0], [rb, len]], n, false, capA, capB); M.pop();
    }
    /** Inward-facing lining of an open tube around Y (sleeve openings): profile [[r, y]…]; no ink (the hull's inner wall stays hidden). */
    innerTube(prof, n, col) {
      const M = this.M, keepInk = M.ink, P = (r, y, a) => [Math.cos(a) * r, y, Math.sin(a) * r];
      M.ink = 0; M.color = col;
      for (let i = 0; i < prof.length - 1; i++) for (let j = 0; j < n; j++) {
        const a0 = j * TAU / n, a1 = (j + 1) * TAU / n, am = (a0 + a1) / 2, [r0, y0] = prof[i], [r1, y1] = prof[i + 1];
        M.quad(P(r0, y0, a0), P(r0, y0, a1), P(r1, y1, a1), P(r1, y1, a0), [-Math.cos(am), 0, -Math.sin(am)]);
      }
      M.ink = keepInk;
    }
    /** Cloth / trim strip: with thickness at high detail, one outward quad (2 triangles, no ink) below that. */
    strap(a, b, w0, w1, facing, thick) {
      const M = this.M, V = this.V;
      if (this.D.det >= 2) { M.strip(a, b, w0, w1, facing, thick); return; }
      const axis = V.sub(b, a); let side = V.cross(axis, facing);
      if (V.dot(side, side) < 1e-12) return;
      side = V.norm(side);
      let n = V.norm(V.cross(side, axis)); if (V.dot(n, facing) < 0) n = V.neg(n);
      const lift = V.mul(n, (thick || 0.01) * 0.5), keepInk = M.ink;
      M.ink = 0;
      M.quad(V.add(V.madd(a, side, -w0 / 2), lift), V.add(V.madd(a, side, w0 / 2), lift), V.add(V.madd(b, side, w1 / 2), lift), V.add(V.madd(b, side, -w1 / 2), lift), n);
      M.ink = keepInk;
    }
    mark(name) { const t = this.M.triangleCount; this.parts[name] = (this.parts[name] || 0) + t - this._last; this._last = t; }
    /** Arm frame: origin at the shoulder joint, the arm hanging along local −Y (A-pose). */
    armFrame(s) { const S = s > 0 ? 'L' : 'R'; this.M.push().translate(this.J['upperArm' + S]).rotate(0, 0, s * 40); return this; }
    sides(n) { return Math.max(3, Math.round(n)); }

    // ------------------------------------------------ body
    /** Torso radius profiles (pelvis / spine / chest), shared by armour bands, sashes and capes. */
    torsoProfiles() {
      const u = this.k;
      const pel = [[this.hipR * 0.82, this.hipY - 0.085 * u], [this.hipR, this.hipY - 0.02 * u], [this.hipR * 0.98, this.pelvisY + 0.02 * u], [this.waistR * 0.98, this.pelvisY + 0.06 * u]];
      const sp = [[this.waistR, this.pelvisY - 0.01 * u], [this.waistR * 1.04 * (1 + 0.25 * this.fat), (this.pelvisY + this.spineY) / 2 + 0.02 * u], [this.waistR * 1.02, this.spineY + 0.06 * u]];
      const ch = [[this.waistR, this.spineY], [this.chestR, this.chestY], [this.chestR * 1.02, this.shoulderY - 0.07 * u], [this.shoulderR, this.shoulderY - 0.012 * u], [this.shoulderR * 0.84, this.shoulderY + 0.024 * u], [0.084 * u, this.neckY + 0.035 * u]];
      return { pel, sp, ch };
    }
    /** Radius of the torso surface at height y (before the depth squash). */
    torsoR(y) {
      const T = this._prof || (this._prof = this.torsoProfiles());
      const rIn = (pr, y2) => { if (y2 <= pr[0][1]) return pr[0][0]; for (let i = 0; i + 1 < pr.length; i++) if (y2 <= pr[i + 1][1]) return lerp(pr[i][0], pr[i + 1][0], clamp01((y2 - pr[i][1]) / (pr[i + 1][1] - pr[i][1]))); return pr[pr.length - 1][0]; };
      if (y < this.pelvisY) return rIn(T.pel, y);
      if (y < this.spineY) return rIn(T.sp, y);
      return rIn(T.ch, y);
    }
    /** A point on the torso surface (θ degrees from the front towards +X), lifted `off` metres outwards. */
    torsoPt(th, y, off = 0) {
      const t = th * D2R, r = this.torsoR(y) * Math.cos(PI / this.D.tor) + off, dk = this.depthK;
      return [Math.sin(t) * r, y, Math.cos(t) * r * dk + (y > this.spineY ? 0 : 0)];
    }
    torsoBone(y) { return y < this.pelvisY + 0.02 * this.k ? B.pelvis : y < this.spineY + 0.06 * this.k ? B.spine : B.chest; }
    torso(top, pelvis, opts) {
      const M = this.M, T = this._prof = this.torsoProfiles(), n = this.D.tor, o = opts || {};
      // o.vested（甲排盖住腰到胸）：低细节层只建骨盆与肩颈两段（中段藏在甲排里面）
      const hide = !!o.vested && this.D.det <= 1;
      this.bone('pelvis'); M.color = pelvis;
      M.body([0, 0, 0], T.pel, n, this.depthK * 1.06, true, false);
      if (!hide) { this.bone('spine'); M.color = o.belly || top; M.body([0, 0, 0], T.sp, n, this.depthK, false, false); }
      this.bone('chest'); M.color = top;
      M.push().scale(1, 1, this.depthK);
      M.lathe(hide ? T.ch.slice(2) : T.ch, n, false, false, true, o.ringCols || null, 180 / n);
      M.pop();
    }
    /** Neck: from inside the shoulders up into the skull behind the jaw (top hidden in the head). */
    neck(c, thick = 1) {
      const k = this.k, fem = this.fem ? 0.84 : 1, sp = this.look.spec && this.look.spec.neck ? clamp(0.85 + 0.2 * this.look.spec.neck, 0.92, 1.25) : 1;
      const rb = 0.062 * k * thick * fem * sp * lerp(1, this.bulk, 0.7), rt = 0.056 * k * thick * fem * sp * lerp(1, this.bulk, 0.5);
      const top = [0, this.headPivot[1] + 0.03 * this.Ry, this.headPivot[2]];
      this.bone('neck'); this.M.color = c;
      this.seg([0, this.shoulderY - 0.03 * k, -0.02 * k], top, rb, rt, this.sides(this.D.lim), false, false);
      this.neckTop = top;
    }
    /** Upper arm, forearm and fist (cols: [upper, lower, hand]). */
    arm(s, upper, lower, hand, o) {
      o = o || {};
      const M = this.M, S = s > 0 ? 'L' : 'R', k = this.k, n = this.D.lim;
      this.armFrame(s);
      this.bone('upperArm' + S); M.color = upper;
      const lean = this.D.det === 0;
      if (!o.noShoulder && !(lean && o.covered)) M.sphere([0, -0.012 * k, 0], this.armR * 1.15, n, this.D.sph[1]);
      if (!o.noUpper) this.seg([0, 0, 0], [0, -this.upL, 0], this.armR * (o.upK || 1), this.armR * 0.86 * (o.upK || 1), n, !lean && !!o.noShoulder, true);
      this.bone('foreArm' + S); M.color = o.elbow || lower;
      if (!o.noElbow && this.D.det >= 1) M.sphere([0, -this.upL, 0], this.foreR * 1.05, Math.max(4, n - 1), Math.max(2, this.D.sph[1] - 1));
      M.color = lower;
      this.seg([0, -this.upL, 0], [0, -this.upL - this.foL + 0.01 * k, 0], this.foreR * (o.foK || 1), this.foreR * 0.82 * (o.foK || 1), o.foreSides || n, !lean && (o.noElbow || this.D.det < 1), false);
      M.pop();
      this.fist(s, hand, o.fistK);
    }
    /** Fist and thumb. */
    fist(s, hand, lowK) {
      const M = this.M, S = s > 0 ? 'L' : 'R', k = this.k * 1.1 * lerp(1, this.bulk, 0.5) * (this.fem ? 0.88 : 1), F = this.D.fist;
      this.armFrame(s);
      this.bone('hand' + S); M.color = hand;
      const wy = -this.upL - this.foL;
      M.sphere([0, wy - 0.045 * k, 0.01 * k], [0.04 * k, 0.052 * k, 0.046 * k], lowK ? Math.max(4, F[0] - 1) : F[0], lowK ? Math.max(2, F[1] - 1) : F[1]);
      if (this.D.det >= 2) M.sphere([-s * 0.024 * k, wy - 0.03 * k, 0.034 * k], [0.016 * k, 0.024 * k, 0.016 * k], Math.max(4, F[0] - 2), Math.max(2, F[1] - 1));
      M.pop();
    }
    /** Ring on the forearm (bracer / cuff), from..to along it (0 elbow … 1 wrist). */
    cuff(s, c, from, to, kk, sidesK) {
      const M = this.M, S = s > 0 ? 'L' : 'R';
      this.armFrame(s); this.bone('foreArm' + S); M.color = c;
      const y0 = -this.upL - this.foL * from, y1 = -this.upL - this.foL * to;
      M.segment([0, y0, 0], [0, y1, 0], lerp(this.foreR, this.foreR * 0.82, from) * kk, lerp(this.foreR, this.foreR * 0.82, to) * kk, sidesK || this.D.lim);
      M.pop();
    }
    /** Ring on the upper arm (armlet / sleeve hem), at 0 (shoulder) … 1 (elbow). */
    armRing(s, c, from, to, kk) {
      const M = this.M, S = s > 0 ? 'L' : 'R';
      this.armFrame(s); this.bone('upperArm' + S); M.color = c;
      M.segment([0, -this.upL * from, 0], [0, -this.upL * to, 0], this.armR * lerp(1, 0.86, from) * kk, this.armR * lerp(1, 0.86, to) * kk, this.D.lim);
      M.pop();
    }
    leg(s, thigh, shin, boot, bootTop = 0.55, sole, o) {
      o = o || {};
      const M = this.M, S = s > 0 ? 'L' : 'R', J = this.J, k = this.k, V = this.V;
      const hip = [J['thigh' + S][0], this.hipY + 0.03 * k, J['thigh' + S][2]], knee = J['shin' + S], ankle = J['foot' + S];
      const tk = o.thighK || 1, sk = o.shinK || 1;
      this.bone('thigh' + S); M.color = thigh;
      const lean = this.D.det === 0;
      this.seg(hip, knee, this.thighR * tk, this.thighR * 0.76 * tk, o.thighSides || this.D.thigh, false, true);
      this.bone('shin' + S); M.color = o.kneeCol || thigh;
      if (o.knee !== false && this.D.det >= 1) M.sphere(V.add(knee, [0, 0, 0.004]), this.thighR * 0.78 * Math.max(tk * 0.9, sk), Math.max(4, this.D.lim - 1), Math.max(2, this.D.sph[1] - 1));
      M.color = shin;
      this.seg(knee, V.add(ankle, [0, 0.02 * k, 0]), this.shinR * sk, this.shinR * 0.78 * sk, this.D.lim, true, !lean && bootTop <= 0);
      this.bootTop = null;
      if (bootTop > 0) {
        const top = V.lerp(ankle, knee, bootTop);
        M.color = boot;
        this.seg(top, V.add(ankle, [0, -0.005, 0]), this.shinR * 1.18 * Math.max(1, sk * 0.9), this.shinR * 1.02 * Math.max(1, sk * 0.9), this.D.lim, this.D.det >= 2, false);
        this.bootTop = top;
      }
      this.bone('foot' + S);
      this.shoe(ankle, this.footLen, this.footW, o.footCol || boot, sole || this.P.shade(o.footCol || boot, 0.6), o);
    }
    /** Boot / shoe (UnitMeshKit.Shoe): ankle at `ankle`, sole on y = 0, toe to +Z. */
    shoe(ankle, len, width, color, sole, o) {
      const M = this.M, keep = M.color;
      const zb = ankle[2] - len * 0.28, zm = ankle[2] + len * 0.38, zt = ankle[2] + len * 0.72;
      const h = Math.max(ankle[1] + width * 0.25, len * 0.36), hm = len * 0.26, ht = len * 0.2;
      const w = width * 0.5, wt = width * 0.46, x = ankle[0];
      M.color = color;
      if (this.D.det === 0) {
        // low: a tapered block for the foot and a toe cap
        M.push().translate(x, 0.012, (zb + zm) / 2);
        M.taperedBox([0, 0, 0], [width, h - 0.012, zm - zb], 0.12, 0.25);
        M.pop();
        M.box([x, ht * 0.45 + 0.012, (zm + zt) / 2], [wt * 2, ht * 0.9, zt - zm + 0.01], null, 'tfrl');
      } else {
        const b0 = [x - w, 0.012, zb], b1 = [x + w, 0.012, zb], b2 = [x + wt, 0.012, zm], b3 = [x - wt, 0.012, zm];
        const t0 = [x - w * 0.92, h, zb + 0.01], t1 = [x + w * 0.92, h, zb + 0.01], t2 = [x + wt, hm, zm], t3 = [x - wt, hm, zm];
        M.quad(t0, t1, t2, t3, [0, 1, 0.5]);
        M.quad(b0, b1, t1, t0, [0, 0, -1]);
        M.quad(b1, b2, t2, t1, [1, 0, 0]);
        M.quad(b3, b0, t0, t3, [-1, 0, 0]);
        M.sphere([x, ht * 0.62, (zm + zt) / 2], [wt * 1.02, ht * 0.75, (zt - zm) * 0.62 + 0.01], this.D.det >= 2 ? 6 : 5, 2, true);
        M.box([x, hm * 0.45, zm - 0.004], [wt * 2, hm * 0.9, 0.012]);
      }
      if (!(o && o.noSole) && this.D.det >= 1) {
        M.color = sole;
        M.box([x, 0.012, (zb + zt) / 2 + 0.005], [width * 1.04, 0.024, zt - zb + 0.04], null, this.D.det >= 2 ? 'tbfkrl' : 'tfkrl');
      }
      M.color = keep;
    }

    // ------------------------------------------------ head & face
    hv(x, y, z) { return [x * this.Rx, y * this.Ry, z * this.Rzu]; }
    headAt(x, y, z) { return this.V.add(this.headC, this.hv(x, y, z)); }
    headShape(c, cheekShade = 1) {
      const M = this.M, G = this.headG, n = G.ys.length, S = G.S, V = this.V, P = this.P;
      const P0 = G.ys.map((y, i) => G.rad[i].map((r, j) => { const t = j * 2 * PI / S; return [Math.cos(t) * r, y, Math.sin(t) * r]; }));
      const N0 = P0.map(row => row.map(() => [0, 0, 0]));
      const tris = [];
      for (let i = 0; i < n - 1; i++) for (let j = 0; j < S; j++) {
        const j1 = (j + 1) % S, A = [i, j], Bq = [i + 1, j], Cc = [i + 1, j1], Dd = [i, j1];
        if (G.rad[i + 1][0] > 1e-5) tris.push([A, Bq, Cc]);
        if (G.rad[i][0] > 1e-5) tris.push([A, Cc, Dd]);
      }
      for (const t of tris) {
        const [a, b, cc] = t.map(([i, j]) => P0[i][j]), fn = V.cross(V.sub(b, a), V.sub(cc, a));
        for (const [i, j] of t) { const s = N0[i][j]; s[0] += fn[0]; s[1] += fn[1]; s[2] += fn[2]; }
      }
      for (let j = 0; j < S; j++) { N0[0][j] = [0, -1, 0]; N0[n - 1][j] = [0, 1, 0]; }
      const idx = [];
      this.bone('head');
      M.push().translate(this.headC).scale(this.Rx, this.Ry, this.Rz).rotate(HEAD_TILT, 0, 0);
      M.color = c;
      for (let i = 0; i < n; i++) {
        const cc = P.shade(c, lerp(cheekShade, 1, sstep(-0.05, 0.25, G.ys[i])));
        idx.push(P0[i].map((p, j) => M.addVertex(p, V.norm(N0[i][j]), cc)));
      }
      M.pop();
      for (const t of tris) {
        const [a, b, cc] = t.map(([i, j]) => P0[i][j]), fn = V.cross(V.sub(b, a), V.sub(cc, a));
        const m = V.mul(V.add(V.add(a, b), cc), 1 / 3);
        const ids = t.map(([i, j]) => idx[i][j]);
        if (V.dot(fn, m) >= 0) M.addTri(ids[0], ids[1], ids[2]); else M.addTri(ids[0], ids[2], ids[1]);
      }
    }
    headRadius(y, tf) {
      const G = this.headG, ys = G.ys, S = G.S, n = ys.length;
      if (y <= ys[0] || y >= ys[n - 1]) return 0;
      let i = 0; while (i < n - 2 && y > ys[i + 1]) i++;
      const u = (y - ys[i]) / (ys[i + 1] - ys[i]);
      const step = 2 * PI / S, f = ((PI / 2 - tf) / step % S + S) % S, j0 = Math.floor(f) % S, j1 = (j0 + 1) % S;
      const chord = (r0, r1) => {
        const a0 = PI / 2 - j0 * step, a1 = PI / 2 - (j0 + 1) * step;
        const p0 = [r0 * Math.sin(a0), r0 * Math.cos(a0)], p1 = [r1 * Math.sin(a1), r1 * Math.cos(a1)], ud = [Math.sin(tf), Math.cos(tf)];
        const e = [p1[0] - p0[0], p1[1] - p0[1]], den = ud[0] * e[1] - ud[1] * e[0];
        return Math.abs(den) < 1e-9 ? r0 : (p0[0] * e[1] - p0[1] * e[0]) / den;
      };
      return lerp(chord(G.rad[i][j0], G.rad[i][j1]), chord(G.rad[i + 1][j0], G.rad[i + 1][j1]), u);
    }
    insideHead(p) {
      const c = this.headC;
      const d = [(p[0] - c[0]) / this.Rx, (p[1] - c[1]) / this.Ry, (p[2] - c[2]) / this.Rz];
      const a = -HEAD_TILT * D2R, ca = Math.cos(a), sa = Math.sin(a);
      const y = d[1] * ca - d[2] * sa, z = d[1] * sa + d[2] * ca, rho = Math.hypot(d[0], z);
      return rho <= this.headRadius(y, Math.atan2(d[0], z));
    }
    /** The point on the front of the face at x half-widths (Rx) and y half-heights (Ry, from headCY), lifted `lift` feature units. */
    facePoint(x, y, lift = 0) {
      const px = x * this.Rx, py = this.headCY + y * this.Ry;
      let lo = this.headC[2], hi = lo + 1.5 * this.Rz;
      if (!this.insideHead([px, py, lo])) return [px, py, lo + lift * this.R];
      for (let i = 0; i < 20; i++) { const mid = (lo + hi) / 2; if (this.insideHead([px, py, mid])) lo = mid; else hi = mid; }
      return [px, py, lo + lift * this.R];
    }
    /** A point on the side of the head at angle th (deg, 0 = front, + towards +X) and height y (Ry from headCY), lifted. */
    sidePoint(th, y, lift = 0) {
      const t = th * D2R, dir = [Math.sin(t), 0, Math.cos(t)], c = [0, this.headCY + y * this.Ry, this.headC[2]];
      let lo = 0, hi = 1.6 * Math.max(this.Rx, this.Rz);
      for (let i = 0; i < 20; i++) { const mid = (lo + hi) / 2; if (this.insideHead([c[0] + dir[0] * mid, c[1], c[2] + dir[2] * mid])) lo = mid; else hi = mid; }
      return [c[0] + dir[0] * (lo + lift * this.R), c[1], c[2] + dir[2] * (lo + lift * this.R)];
    }
    ears(c, big) {
      const M = this.M, R = this.R, D = this.D;
      this.bone('head'); M.color = c;
      const kk = big ? 1.35 : 1;
      for (const s of [1, -1]) {
        M.push().translate(this.headAt(s * 0.98, -0.2 - (big ? 0.08 : 0), -0.1)).rotate(-8, s * -18, 0);
        M.sphere([0, 0, 0], [0.2 * R * kk, 0.36 * R * kk * (big ? 1.2 : 1), 0.24 * R], D.det >= 2 ? 6 : 5, D.det >= 2 ? 3 : 2, true);
        M.pop();
      }
    }
    /** Hair cap over the cranium (BipedKit.HairCap): hairline φ (deg from the top) at the forehead / ears / nape. */
    hairCap(c, front = 58, side = 100, back = 122, puff = 1.08, lineFn = null, topFn = null, np = 2) {
      const M = this.M;
      this.bone('head'); M.color = c;
      const C0 = this.headAt(0, 0.02, -0.01), RR = this.hv(puff, puff, puff * 0.98);
      const line = lineFn || (th => { const a = Math.abs(th); if (a < 55) return lerp(front, front + 10, a / 55); if (a < 110) return lerp(front + 10, side, (a - 55) / 55); return lerp(side, back, (a - 110) / 70); });
      M.shell(C0, RR, -180, 180, this.D.hs, topFn || 0, line, np);
      this.hairC = C0; this.hairR = RR; this.hairLine = line;
    }
    hairPoint(th, ph, k = 1) {
      const c = this.hairC, r = this.hairR, t = th * D2R, p = ph * D2R, s = Math.sin(p);
      return [c[0] + r[0] * k * s * Math.sin(t), c[1] + r[1] * k * Math.cos(p), c[2] + r[2] * k * s * Math.cos(t)];
    }
    /** A shell over the head (caps, helmets, wraps): centre and radii in head units, hairline fn(θ) → φ. */
    capShell(c, puff, line, np, topFn, opts) {
      const M = this.M, o = opts || {};
      this.bone('head'); M.color = c;
      const C0 = this.headAt(0, 0.02 + (o.dy || 0), -0.01 + (o.dz || 0)), RR = this.hv(puff * (o.kx || 1), puff * (o.ky || 1), puff * 0.98 * (o.kz || 1));
      M.shell(C0, RR, -180, 180, o.nt || this.D.hs, topFn || 0, line, np || 2);
      this.capC = C0; this.capR = RR; this.capLine = line;
      return { c: C0, r: RR, line };
    }
    capPoint(th, ph, k = 1) {
      const c = this.capC, r = this.capR, t = th * D2R, p = ph * D2R, s = Math.sin(p);
      return [c[0] + r[0] * k * s * Math.sin(t), c[1] + r[1] * k * Math.cos(p), c[2] + r[2] * k * s * Math.cos(t)];
    }
    /** Eyes: style 'round' | 'almond' | 'narrow'; a dark eye shape, the iris low in it and a bright catch-light (thin ink). */
    eyes(iris, brow, style = 'round', browTilt = 0, y = -0.03, spread = 0.4, size = 1, eyeTilt = 0, browK = 1, o) {
      const M = this.M, R = this.R, keepJ = M.jitter, keepE = M.emission, P = this.P, D = this.D;
      o = o || {};
      this.bone('head'); M.jitter = 0;
      const h = style === 'narrow' ? 0.12 : style === 'almond' ? 0.145 : 0.2, wd = style === 'narrow' ? 0.17 : style === 'almond' ? 0.165 : 0.13;
      for (const s of [1, -1]) {
        const patch = o.patch && s === 1;          // 夏侯惇：左目
        const p = this.facePoint(s * spread, y, 0);
        M.push().translate(this.V.add(p, [0, 0, -0.036 * R])).rotate(0, s * EYE_TURN, s * eyeTilt);
        if (patch) {
          // 眼罩（夏侯惇）：深色皮罩盖住右眼
          M.ink = 0.6; M.color = P.hex ? C3(PALF.black) : [0.16, 0.13, 0.14];
          M.sphere([0, 0.01 * R, 0.02 * R], [0.28 * R, 0.22 * R, 0.09 * R], D.eye >= 2 ? 7 : 5, D.eye >= 2 ? 3 : 2);
          M.ink = 1;
        } else {
          M.ink = 0.4;
          M.color = P.shade(iris, 0.42);
          M.sphere([0, 0, 0], [wd * R * size, h * R * size, 0.065 * R], D.eye >= 2 ? 7 : D.det >= 1 ? 5 : 4, D.eye >= 2 ? 3 : 2);
          if (D.eye >= 2) { M.color = iris; M.sphere([0, -h * 0.35 * R * size, 0.012 * R], [wd * 0.75 * R * size, h * 0.45 * R * size, 0.06 * R], 6, 2); }
          M.emission = 0.55; M.color = [1, 1, 0.97]; M.ink = 0;
          if (D.det >= 1) M.sphere([s * 0.035 * R * size, h * 0.38 * R * size, 0.05 * R], 0.045 * R * size * (style === 'narrow' ? 0.85 : 1) * (D.eye >= 2 ? 1 : 1.25), D.eye >= 2 ? 4 : 3, 2);
          M.emission = keepE; M.ink = 1;
        }
        M.pop();
        if (brow) {
          const dy = (style === 'narrow' ? 0.24 : style === 'almond' ? 0.27 : 0.3) * Math.max(1, size * 0.9) * R / this.Ry;
          const bp = this.facePoint(s * spread * 1.04, y + dy + (o.browY || 0), 0.01);
          M.color = brow; M.ink = 0.7;
          M.push().translate(bp).rotate(0, s * EYE_TURN, s * browTilt);
          M.box([0, 0, 0], [0.36 * R * browK * (o.browW || 1), 0.085 * R * browK * (o.browH || 1), 0.07 * R], null, D.det >= 2 ? 'tbfkrl' : D.det >= 1 ? 'tbfrl' : 'tf');
          if (browK > 1.2 && D.det >= 2) M.box([s * 0.22 * R * browK, 0.03 * R * browK, -0.012 * R], [0.13 * R * browK, 0.055 * R * browK, 0.055 * R], null, 'tbfrl');
          M.pop(); M.ink = 1;
        }
      }
      M.jitter = keepJ;
    }
    nose(c, size = 1, y = -0.42, proj = 0.32, w = 0.2) {
      const M = this.M, R = this.R, V = this.V;
      this.bone('head'); M.color = c; M.ink = 0.6;
      const root = this.facePoint(0, y + 0.42, 0.03);
      const tip = this.facePoint(0, y, proj * size), base = this.facePoint(0, y - 0.1, 0.12 * size);
      const wing = s => this.facePoint(s * w * size, y - 0.05, 0.05 * size);
      const wl = wing(1), wr = wing(-1);
      const out = (a, b, cc, hint) => { const n = V.cross(V.sub(b, a), V.sub(cc, a)); return V.dot(n, hint) >= 0 ? n : V.neg(n); };
      M.triangleFacing(root, wl, tip, out(root, wl, tip, [1, 0.2, 1]));
      M.triangleFacing(root, tip, wr, out(root, tip, wr, [-1, 0.2, 1]));
      M.triangleFacing(wl, base, tip, out(wl, base, tip, [0.4, -1, 0.6]));
      M.triangleFacing(tip, base, wr, out(tip, base, wr, [-0.4, -1, 0.6]));
      if (this.D.det >= 1) M.sphere(V.add(tip, [0, -0.025 * R, -0.035 * R]), [0.1 * R * size, 0.085 * R * size, 0.08 * R * size], 5, 2);
      M.ink = 1;
    }
    mouth(c, y = -0.68, width = 0.3, curve = 0.06) {
      const M = this.M, R = this.R;
      this.bone('head'); M.color = c; M.ink = 0.3;
      const Pt = t => this.facePoint(t * width * 0.5, y + curve * t * t, 0.015);
      if (this.D.det >= 2) {
        this.seg(Pt(-1), Pt(-0.35), 0.024 * R, 0.028 * R, 4, true, false);
        this.seg(Pt(-0.35), Pt(0.35), 0.028 * R, 0.028 * R, 4, false, false);
        this.seg(Pt(0.35), Pt(1), 0.028 * R, 0.024 * R, 4, false, true);
      } else this.seg(Pt(-1), Pt(1), 0.03 * R, 0.03 * R, 3, false, false);
      M.ink = 1;
    }

    // ------------------------------------------------ garments
    /** Skirt panel on a bone (hinged at the hip on a thigh bone: vertices above the hip joint stay on the pelvis). */
    skirtPanel(bone, th0, th1, nt, prof, outer, lining, hem, hemH = 0.05, depthK = 0.86) {
      const M = this.M, start = M.vertexCount;
      this.bone(bone);
      M.panel([0, 0, 0], th0, th1, Math.max(1, nt), prof, depthK, outer, lining, 0.01);
      if (hem) {
        const last = prof[prof.length - 1], prev = prof[prof.length - 2];
        const t = hemH / Math.max(0.01, prev[1] - last[1]);
        const top = [lerp(last[0], prev[0], t) + 0.006, last[1] + hemH], bot = [last[0] + 0.006, last[1] - 0.002];
        M.panel([0, 0, 0], th0, th1, Math.max(1, nt), [top, bot], depthK, hem, null);
      }
      this.hinge(start, bone);
    }
    hinge(start, bone, dy = 0.0) {
      if (!/^thigh/.test(bone)) return;
      const yH = this.J[bone][1] + dy * this.k, bi = B[bone], bp = B.pelvis;
      this.M.boneBy(start, p => (p[1] > yH ? bp : bi));
    }
  }

  // ----------------------------------------------------------- 调色 --
  // 外观颜色 → sRGB 数组；势力色（TEAM 占位，第 4 分量 = 1）→ 带 .t 的势力色（士兵群：白色 × 实例势力色）
  function shT(c, k) { const a = [clamp01(c[0] * k), clamp01(c[1] * k), clamp01(c[2] * k)]; if (c.t) a.t = 1; return a; }
  function paletteOf(kit) {
    const look = kit.look, C = look.C, team = kit.team;
    const res = (c, floor) => {
      if (c == null) return null;
      if (Array.isArray(c) && c.length >= 4 && c[3] === 1) return tcol(team, (c[0] + c[1] + c[2]) / 3);
      return lift(c, floor == null ? 0.2 : floor);
    };
    const P = kit.P;
    const metal0 = res(C.metal, 0.34);
    // 金属偏冷（Lanternvale 的铁色：灰中带蓝），金色金属保持暖色
    const warmMetal = metal0[0] > metal0[2] * 1.25;
    const metal = warmMetal ? metal0 : P.mix(metal0, [0.42, 0.46, 0.53], 0.25);
    const X = {
      team: tcol(team, 1),
      skin: C3(look.skin || '#e2b48c'),
      cloth: res(C.cloth), cloth2: res(C.cloth2, 0.16) || null,
      trim: res(C.trim || PALF.gold, 0.4),
      metal, metalDark: shT(metal, 0.78), metalLight: shT(metal, 1.16), seam: shT(metal, 0.58),
      leather: res(C.leather || PALF.leather, 0.22), leatherDark: C3(PALF.leatherDark),
      boots: C3(PALF.boots), sole: C3(PALF.sole),
      gold: C3(PALF.gold), goldDark: C3(PALF.goldDark), goldLight: C3(PALF.goldLight),
      cream: C3(PALF.cream), white: C3(PALF.white), lining: C3(PALF.lining),
      hair: hairTone(look.hair && look.hair.color ? look.hair.color : '#1c1816'),
      cape: res(C.cape), tabard: res(C.tabard), plume: res(C.plume || PALF.tassel, 0.3),
    };
    if (!X.cloth2) X.cloth2 = shT(X.cloth, 0.62);
    X.clothDark = shT(X.cloth, 0.8);
    X.inner = shT(X.cloth, 0.82);
    X.skinDark = P.shade(X.skin, 0.84);
    X.beard = hairTone(look.beard && look.beard.color ? look.beard.color : (look.hair && look.hair.color) || '#1c1816');
    X.trousers = C.trousers ? res(C.trousers) : look.kind === 'general' ? shT(X.cloth, 0.55) : shT(X.cloth2, 1.05);
    if (X.cape) X.capeIn = shT(X.cape, 0.62);
    if (X.tabard) X.tabardDark = shT(X.tabard, 0.72);
    return X;
  }

  // ----------------------------------------------------------- 腿脚 --
  function robeOverBare(L) { return (L.legs || 'trousers') === 'bare' && (L.skirt === 'robe' || L.skirt === 'coat' || L.skirt === 'sarong') && (L.skirtLen || 0) >= 0.58; }
  function legsLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D, V = kit.V, J = kit.J;
    const lg = L.legs || 'trousers', bt = L.boots || 'boot', gen = look.kind === 'general';
    const robeSk = robeOverBare(L);
    const cult = look.culture;
    // 汉 / 高句丽 / 倭的士兵：米色裤 + 褐色绑腿（试点的汉军枪兵）
    const wraps = !gen && lg === 'trousers' && bt === 'boot' && (cult === 'han' || cult === 'korea' || cult === 'wa' || cult === 'yi' || cult === 'nanman');
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R';
      let thighC, shinC, tk = 1, sk = 1;
      if (lg === 'bare') { thighC = robeSk ? X.inner : X.skin; shinC = X.skin; }
      else if (lg === 'hakama') { thighC = shinC = X.cloth2; tk = 1.45; sk = 1.6; }
      else if (wraps) { thighC = shinC = X.cream; }
      else { thighC = shinC = L.legsReg === 'plaid' ? X.cloth : X.trousers; tk = 1.06; sk = 1.06; }
      const foot = bt === 'boot' ? X.boots : bt === 'shoe' ? C3('#3A2E2A') : X.skin;
      const top = bt === 'boot' && !wraps ? (gen ? 0.62 : 0.56) : 0;
      kit.leg(s, thighC, shinC, X.boots, top, bt === 'sandal' ? X.leather : bt === 'bare' ? X.skinDark : null,
        { knee: D.det >= 2 || lg === 'bare', thighK: tk, shinK: sk, footCol: foot, noSole: bt === 'bare', thighSides: gen ? Math.max(5, D.thigh - 2) : D.thigh });
      if (top > 0 && kit.bootTop && D.det >= 1) {
        kit.bone('shin' + S); M.color = gen ? X.gold : shT(X.boots, 1.25);
        M.band(kit.bootTop, kit.shinR * 1.26, kit.shinR * 1.24, -0.03 * k, 0.005 * k, D.lim);
      }
      if (wraps) {
        kit.bone('shin' + S); M.color = C3('#8C6A4C');
        const kn = J['shin' + S], an = J['foot' + S];
        M.segment(V.lerp(an, kn, 0.72), V.add(an, [0, 0.01, 0]), kit.shinR * 1.18, kit.shinR * 1.06, D.lim);
        if (D.det >= 1) { M.color = C3('#78593F'); M.band(V.lerp(an, kn, 0.4), kit.shinR * 1.18, kit.shinR * 1.18, -0.012, 0.012, D.lim); }
      }
      if (L.legsReg === 'plaid' && D.det >= 1) {
        // 格纹：大腿与小腿上两道深色横带
        kit.bone('thigh' + S); M.color = X.cloth2;
        M.band(V.lerp(J['thigh' + S], J['shin' + S], 0.45), kit.thighR * 0.96 * tk, kit.thighR * 0.92 * tk, -0.025 * k, 0.025 * k, D.thigh);
        kit.bone('shin' + S);
        M.band(V.lerp(J['shin' + S], J['foot' + S], 0.35), kit.shinR * 1.1, kit.shinR * 1.08, -0.02 * k, 0.02 * k, D.lim);
      }
      if (lg === 'hakama' && D.det >= 1) {
        kit.bone('shin' + S); M.color = shT(X.cloth2, 0.8);
        M.band(V.lerp(J['foot' + S], J['shin' + S], 0.08), kit.shinR * 1.25, kit.shinR * 1.3, -0.01, 0.03 * k, D.lim);
      }
      if (bt === 'sandal' && D.det >= 1) {
        kit.bone('shin' + S); M.color = X.leather;
        for (const f of [0.12, 0.3]) M.band(V.lerp(J['foot' + S], J['shin' + S], f), kit.shinR * 0.98, kit.shinR * 0.98, -0.008, 0.008, D.lim);
      }
      if (L.greave === 'metal') {
        kit.bone('shin' + S);
        const kn = J['shin' + S], an = J['foot' + S];
        const a = V.lerp(an, kn, 0.95), b = V.lerp(an, kn, 0.32);
        M.color = X.metal;
        M.push().translate(V.lerp(a, b, 0.5)).scale(1.18 * sk, 1, 1.1 * sk);
        M.shell([0, 0, 0.004], [kit.shinR * 1.15, V.len(V.sub(a, b)) * 0.55, kit.shinR * 1.2], -80, 80, Math.max(3, D.pan + 1), 45, 135, 1);
        M.pop();
        if (D.det >= 1) { M.color = X.metalLight; M.sphere(V.add(kn, [0, 0.01, kit.thighR * 0.7]), [kit.thighR * 0.55, kit.thighR * 0.6, kit.thighR * 0.35], D.sph[0], D.sph[1]); }
      }
    }
    kit.mark('legs');
  }

  // ----------------------------------------------------------- 躯干 --
  // 甲片横排（札甲 / 鱼鳞 / 环片 / 短甲 / 藤 / 絮甲）：一圈圈带子，两色交替，排间暗缝；按顶点高度分在 spine / chest 上
  function vestRows(kit, o) {
    const M = kit.M, k = kit.k, D = kit.D;
    const rows = Math.max(2, D.det >= 1 ? o.rows : Math.ceil(o.rows * 0.5));
    const yH = o.yBot != null ? o.yBot : kit.pelvisY + 0.075 * k, yT = o.yTop != null ? o.yTop : kit.shoulderY - 0.03 * k;
    // 低细节层：甲排之间不留缝（中段躯干不建，缝里会透空）
    const seam = D.det <= 1 ? 0 : o.seam != null ? o.seam : 0.006, RK = o.RK || 1.085, n = o.sides || D.tor;
    const v0 = M.vertexCount;
    M.bone = B.chest;
    const keepJ = M.jitterAll; if (o.jitterAll) M.jitterAll = true;
    for (let i = 0; i < rows; i++) {
      const y0 = lerp(yH, yT, i / rows) + seam, y1 = lerp(yH, yT, (i + 1) / rows) - seam;
      M.color = typeof o.colorOf === 'function' ? o.colorOf(i) : (i % 2 ? o.a : o.b);
      const r0 = kit.torsoR(y0) * RK * (o.flare ? 1 + o.flare * (1 - i / rows) : 1), r1 = kit.torsoR(y1) * RK;
      if (o.arc) M.panel([0, 0, 0], o.arc[0], o.arc[1], o.arcN || 3, [[r1, y1], [r0, y0]], kit.depthK, M.color, null);
      else M.band([0, 0, 0], r0, r1, y0, y1, n, kit.depthK);
    }
    M.jitterAll = keepJ;
    if (o.hem) { M.color = o.hem; M.band([0, 0, 0], kit.torsoR(yH) * (RK + 0.02), kit.torsoR(yH) * (RK + 0.02), yH - 0.026 * k, yH + seam, n, kit.depthK); }
    if (o.lace && D.det >= 1) {
      M.color = o.lace;
      const fz = y => kit.torsoR(y) * RK * Math.cos(PI / n) * kit.depthK + 0.006;
      const lys = [yH - 0.01 * k, kit.spineY + 0.06 * k, kit.chestY, yT];
      for (let i = 0; i < lys.length - 1; i++) {
        const w = 0.012 * k;
        M.quad([-w, lys[i], fz(lys[i])], [w, lys[i], fz(lys[i])], [w, lys[i + 1], fz(lys[i + 1])], [-w, lys[i + 1], fz(lys[i + 1])], [0, 0, 1]);
      }
    }
    const ySplit = kit.spineY + 0.06 * k;
    M.boneBy(v0, p => (p[1] < ySplit - 1e-4 ? B.spine : B.chest));
    return { yH, yT };
  }
  // 沿躯干表面的一条带（交领、斜挎的皮 / 绶带 / 托加）：path = [[θ 度, y], …]，带宽 w，带色 cols（逐段可不同）
  function torsoStrip(kit, path, w, col, off, thick) {
    const M = kit.M, V = kit.V, k = kit.k, n = kit.D.tor;
    const r0 = (y, th) => { const r = kit.torsoR(y), dk = kit.depthK; const rr = r * lerp(Math.cos(PI / n), 1, 0.55) + (off != null ? off : 0.012 * k); const t = th * D2R; return [Math.sin(t) * rr, y, Math.cos(t) * rr * dk + (1 - dk) * 0]; };
    const v0 = M.vertexCount;
    const pts = path.map(([th, y]) => r0(y, th));
    for (let i = 0; i < pts.length - 1; i++) {
      const a = pts[i], b = pts[i + 1], mid = V.mul(V.add(a, b), 0.5);
      const out = [mid[0], 0, mid[2]];
      M.color = Array.isArray(col) && Array.isArray(col[0]) ? col[i % col.length] : col;
      M.strip(a, b, w, w, out, thick || 0.012);
    }
    M.boneBy(v0, p => kit.torsoBone(p[1]));
  }
  // 贴着躯干的连续披带（托加、绶带、兽皮斜挎）：沿 [θ 度, y] 路径按 Catmull-Rom 细分，每个截面在躯干表面外 off 处取宽 w 的横线，
  // 相邻截面连成一条不断的带（外面 + 深一点的里面），按顶点高度分骨。cols：逐段颜色（循环），sub：每段细分数
  function drapeBand(kit, path, w, cols, off, sub) {
    const M = kit.M, V = kit.V, k = kit.k, dk = kit.depthK, n = kit.D.tor;
    const at = (th, y) => { const r = kit.torsoR(y) * lerp(Math.cos(PI / n), 1, 0.6) + off, t = th * D2R; return [Math.sin(t) * r, y, Math.cos(t) * r * dk]; };
    const cr = (a, b, c, d, t) => { const t2 = t * t, t3 = t2 * t; return 0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3); };
    const pts = [];
    sub = Math.max(1, sub || 2);
    for (let i = 0; i < path.length - 1; i++) {
      const p0 = path[Math.max(0, i - 1)], p1 = path[i], p2 = path[i + 1], p3 = path[Math.min(path.length - 1, i + 2)];
      for (let j = 0; j < sub; j++) { const t = j / sub; pts.push(at(cr(p0[0], p1[0], p2[0], p3[0], t), cr(p0[1], p1[1], p2[1], p3[1], t))); }
    }
    const last = path[path.length - 1]; pts.push(at(last[0], last[1]));
    const v0 = M.vertexCount, cs = Array.isArray(cols[0]) ? cols : [cols];
    const L = [], R = [], Nn = [];
    for (let i = 0; i < pts.length; i++) {
      const p = pts[i], tg = V.norm(V.sub(pts[Math.min(pts.length - 1, i + 1)], pts[Math.max(0, i - 1)]));
      const out = V.norm([p[0], 0, p[2] / Math.max(0.3, dk)]);
      const side = V.norm(V.cross(tg, out));
      L.push(V.madd(p, side, -w / 2)); R.push(V.madd(p, side, w / 2)); Nn.push(out);
    }
    for (let i = 0; i < pts.length - 1; i++) {
      const c = cs[Math.floor(i / sub) % cs.length], o = V.norm(V.add(Nn[i], Nn[i + 1]));
      M.color = c;
      M.quad(L[i], R[i], R[i + 1], L[i + 1], o);
      if (kit.D.det >= 1) { const inn = V.mul(o, -0.006); M.quad(V.add(L[i], inn), V.add(R[i], inn), V.add(R[i + 1], inn), V.add(L[i + 1], inn), V.neg(o), shT(c, 0.7)); }
    }
    M.boneBy(v0, p => kit.torsoBone(p[1]));
  }
  function torsoLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D, V = kit.V, P = kit.P;
    const t = L.torso || 'cloth', gen = look.kind === 'general';
    const robeSk = robeOverBare(L);
    const armour = { lamLace: 1, lamellar: 1, plate: 1, scale: 1, muscle: 1, mail: 1, segm: 1, tanko: 1, rattan: 1, quilt: 1, fur: 1 }[t];
    // 底色：赤膊 = 肤色（裙内为衬布），布衣 = 衣色；甲衣下的衬衣在缝里露出
    const top = t === 'bare' ? X.skin : t === 'muscle' ? X.metal : armour ? (t === 'segm' && look.culture === 'roman' ? C3('#A8402E') : X.seam) : X.cloth;
    const pel = t === 'bare' ? (robeSk || L.skirt === 'kilt' || L.skirt === 'sarong' ? X.inner : X.cloth) : t === 'muscle' ? X.metal : armour ? X.clothDark : X.cloth;
    const vested = { lamLace: 1, lamellar: 1, plate: 1, scale: 1, tanko: 1, rattan: 1, quilt: 1, fur: 1 }[t] === 1;
    kit.torso(top, pel, { belly: t === 'bare' && kit.fat > 0.3 ? P.shade(X.skin, 0.97) : null, vested });
    kit._torsoTop = top;
    const RK = 1.085;
    if (L.openRobe) {
      // 敞襟里露出的甲：金 / 铁的横排（试点关羽的金札甲）
      const cs = [X.metalDark, X.metal, X.metal, X.metalLight, X.metal, shT(X.metalDark, 0.85)];
      vestRows(kit, { rows: D.det >= 2 ? 6 : 4, colorOf: i => cs[i % cs.length], hem: D.det >= 2 ? X.metalDark : null, seam: 0.003, arc: D.det >= 2 ? null : [-48, 48], yBot: D.det >= 2 ? null : kit.pelvisY + 0.05 * k });
    } else if (t === 'lamLace' || t === 'lamellar') {
      vestRows(kit, { rows: 6, a: t === 'lamLace' ? shT(X.metal, 0.92) : X.metal, b: t === 'lamLace' ? X.metalLight : X.metalDark, hem: gen ? X.trim : X.cream, lace: t === 'lamLace' ? shT(X.cloth, 0.95) : X.seam });
    } else if (t === 'plate') {
      vestRows(kit, { rows: 4, a: X.metal, b: X.metalDark, hem: X.trim, lace: null, seam: 0.004 });
    } else if (t === 'scale') {
      vestRows(kit, { rows: 8, a: X.metal, b: X.metalLight, hem: X.trim, seam: 0.004, RK: 1.09 });
    } else if (t === 'segm') {
      vestRows(kit, { rows: 6, a: X.metalLight, b: X.metal, seam: 0.008, RK: 1.1, yBot: kit.pelvisY + 0.06 * k, yTop: kit.chestY + 0.06 * k });
      // 胸前与肩上的护板
      M.color = X.metal;
      for (const s of [1, -1]) {
        const y0 = kit.chestY + 0.07 * k, y1 = kit.shoulderY - 0.005 * k;
        M.bone = B.chest;
        M.panel([0, 0, 0], s > 0 ? 8 : -62, s > 0 ? 62 : -8, D.pan, [[kit.torsoR(y1) * 1.1, y1], [kit.torsoR(y0) * 1.1, y0]], kit.depthK, X.metal, null);
      }
    } else if (t === 'tanko') {
      vestRows(kit, { rows: 5, a: X.metalDark, b: shT(X.metalDark, 0.85), seam: 0.007, RK: 1.1, hem: X.seam });
      if (D.det >= 2) { M.color = X.metalLight; for (const y of [kit.spineY, kit.chestY]) for (const th of [-30, 0, 30]) M.sphere(kit.torsoPt(th, y, 0.014 * k), 0.008 * k, 4, 2); }
    } else if (t === 'rattan') {
      vestRows(kit, { rows: 6, a: C3(PALF.rattan), b: C3(PALF.rattanDark), seam: 0.004, RK: 1.09, jitterAll: true });
    } else if (t === 'quilt') {
      vestRows(kit, { rows: 5, a: X.cloth, b: shT(X.cloth, 0.86), seam: 0.004, RK: 1.08, hem: X.cloth2 });
    } else if (t === 'fur') {
      vestRows(kit, { rows: 4, a: C3(PALF.fur), b: C3(PALF.furLight), seam: 0.002, RK: 1.1, jitterAll: true });
    } else if (t === 'muscle') {
      // 胸肌甲：铜甲上的胸肌与腹肌浮雕，腰与领口金边
      M.bone = B.chest; M.color = X.metalLight;
      for (const s of [1, -1]) M.sphere(kit.torsoPt(s * 24, kit.chestY + 0.07 * k, -0.035 * k), [0.085 * k, 0.065 * k, 0.05 * k], D.sph[0], D.sph[1]);
      if (D.det >= 1) { M.bone = B.spine; M.color = X.metal; for (const y of [kit.spineY + 0.02 * k, kit.spineY + 0.09 * k]) M.sphere(kit.torsoPt(0, y, -0.012 * k), [0.05 * k, 0.03 * k, 0.025 * k], 5, 2); }
      M.bone = B.pelvis; M.color = X.trim;
      M.band([0, 0, 0], kit.waistR * 1.06, kit.waistR * 1.1, kit.pelvisY - 0.01 * k, kit.pelvisY + 0.03 * k, D.tor, kit.depthK);
    } else if (t === 'bare') {
      // 胸肌：两块略深的扁圆（肤色），按体格；女性：抹胸（见下）
      if (!kit.fem && D.det >= 1) {
        M.bone = B.chest; M.color = P.shade(X.skin, 0.95);
        for (const s of [1, -1]) M.sphere(kit.torsoPt(s * 25, kit.chestY + 0.07 * k, -0.04 * k), [0.08 * k, 0.055 * k, 0.045 * k], D.sph[0], D.sph[1]);
      }
      if (kit.fat > 0.3) { M.bone = B.spine; M.color = X.skin; M.sphere([0, kit.spineY - 0.02 * k, kit.waistR * kit.depthK * 0.55], [kit.waistR * 0.9, 0.12 * k, kit.waistR * 0.7], D.sph[0], D.sph[1]); }
    } else if (t === 'robe' || t === 'cloth') {
      if (kit.fat > 0.3) { M.bone = B.spine; M.color = X.cloth; M.sphere([0, kit.spineY - 0.02 * k, kit.waistR * kit.depthK * 0.5], [kit.waistR * 0.95, 0.13 * k, kit.waistR * 0.75], D.sph[0], D.sph[1]); }
    }
    if (kit.fem && (t === 'bare' || t === 'rattan' || t === 'cloth' || t === 'robe')) {
      // 女性胸型：两团柔和的圆；赤膊类衣装加抹胸
      M.bone = B.chest; M.color = t === 'bare' ? (L.pelt ? C3(L.pelt === 'leopard' ? '#D8A040' : '#D87A2A') : X.cloth2) : t === 'rattan' ? C3(PALF.rattan) : X.cloth;
      for (const s of [1, -1]) M.sphere(kit.torsoPt(s * 22, kit.chestY + 0.05 * k, -0.035 * k), [0.06 * k, 0.055 * k, 0.05 * k], D.sph[0], D.sph[1]);
      if (t === 'bare') M.band([0, 0, 0], kit.torsoR(kit.chestY) * 1.08, kit.torsoR(kit.chestY + 0.1 * k) * 1.08, kit.chestY, kit.chestY + 0.1 * k, D.tor, kit.depthK);
    }
    // 明光铠：双圆护胸（金边）+ 束甲绊；护心镜
    if (t === 'plate' || L.chest === 'plate') {
      M.bone = B.chest;
      for (const s of [1, -1]) {
        const c = kit.torsoPt(s * 26, kit.chestY + 0.06 * k, 0.018 * k);
        M.push().translate(c).rotate(90, s * 22, 0);
        M.color = gen ? X.goldLight : X.metalLight; M.cylinder([0, -0.012, 0], 0.062 * k, 0.056 * k, 0.022, D.rnd, false, false, true);
        if (D.det >= 2) { M.color = gen ? X.goldDark : X.metalDark; M.cylinder([0, 0.01, 0], 0.026 * k, 0.02 * k, 0.012, Math.max(5, D.rnd - 2), false, false, true); }
        M.pop();
      }
      M.color = X.leather; M.bone = B.chest;
      M.band([0, 0, 0], kit.torsoR(kit.chestY - 0.03 * k) * 1.12, kit.torsoR(kit.chestY - 0.01 * k) * 1.12, kit.chestY - 0.05 * k, kit.chestY - 0.02 * k, D.tor, kit.depthK);
    } else if (L.mirror && D.det >= 1) {
      M.bone = B.chest;
      const c = kit.torsoPt(0, kit.chestY + 0.05 * k, 0.022 * k);
      M.push().translate(c).rotate(90, 0, 0);
      M.color = X.goldLight; M.cylinder([0, -0.012, 0], 0.06 * k, 0.054 * k, 0.022, D.rnd);
      M.color = X.goldDark; M.cylinder([0, 0.01, 0], 0.024 * k, 0.018 * k, 0.012, Math.max(5, D.rnd - 2));
      M.pop();
    }
    // 袍甲：甲外罩前襟敞开的袍（深色衬里，远肩的内侧不会从襟口透空），领襟镶米色边
    if (L.openRobe) {
      const robe = X.cloth, lin = shT(X.cloth, 0.75), trim = kit.P.luma(X.cloth2 || [0, 0, 0]) > 0.6 ? X.cloth2 : X.cream;
      const dk = kit.depthK * 1.02, nr = D.det >= 2 ? 7 : 5;
      M.bone = B.chest;
      M.panel([0, 0, 0], 36, 324, nr, [[0.096 * k, kit.neckY + 0.045 * k], [kit.shoulderR * 0.9, kit.shoulderY + 0.03 * k], [kit.shoulderR * 1.08, kit.shoulderY - 0.012 * k], [kit.chestR * 1.1, kit.chestY], [kit.waistR * 1.12, kit.spineY]], dk, robe, D.det >= 1 ? lin : null, 0.012);
      M.bone = B.spine;
      M.panel([0, 0, 0], 36, 324, nr, [[kit.waistR * 1.12, kit.spineY + 0.002], [kit.waistR * 1.14, kit.pelvisY + 0.03 * k]], dk, robe, D.det >= 1 ? lin : null, 0.012);
      const edge = (th, r, y) => { const a = th * D2R; return [r * Math.sin(a), y, r * Math.cos(a) * dk]; };
      M.color = trim;
      for (const s of [1, -1]) {
        const a = edge(s * 38, 0.1 * k, kit.neckY + 0.045 * k), a2 = edge(s * 38, kit.shoulderR * 0.93, kit.shoulderY + 0.028 * k), b = edge(s * 38, kit.shoulderR * 1.1, kit.shoulderY - 0.02 * k), c = edge(s * 38, kit.chestR * 1.12, kit.chestY), d = edge(s * 38, kit.waistR * 1.14, kit.spineY);
        const out = [s * 0.6, 0.2, 1];
        M.bone = B.chest;
        if (D.det >= 2) { M.strip(a, a2, 0.034 * k, 0.038 * k, out, 0.016); M.strip(a2, b, 0.038 * k, 0.04 * k, out, 0.016); M.strip(b, c, 0.04 * k, 0.04 * k, out, 0.016); }
        else { kit.strap(a, b, 0.036 * k, 0.04 * k, out, 0.016); kit.strap(b, c, 0.04 * k, 0.04 * k, out, 0.016); }
        kit.strap(c, d, 0.04 * k, 0.036 * k, out, 0.016);
        M.bone = B.spine;
        kit.strap(d, edge(s * 38, kit.waistR * 1.16, kit.pelvisY + 0.03 * k), 0.036 * k, 0.036 * k, out, 0.016);
      }
      kit._robeTrim = trim;
    }
    // 交领（袍服 / 短衣 cross）：左襟压右襟，沿躯干表面的一条浅色宽带
    if (L.collar === 'cross' || t === 'robe') {
      const c2 = L.white ? X.cloth2 : (gen ? (X.cloth2 && kit.P.luma(X.cloth2) > 0.45 ? X.cloth2 : X.cream) : X.cream);
      const nY = kit.neckY + 0.03 * k, sY = kit.shoulderY - 0.05 * k;
      torsoStrip(kit, [[32, nY], [12, sY], [-18, kit.chestY - 0.02 * k], [-55, kit.spineY + 0.04 * k]], 0.045 * k, c2);
      if (D.det >= 1) torsoStrip(kit, [[-32, nY], [-14, sY], [-2, kit.chestY + 0.06 * k]], 0.038 * k, c2);
    }
    // 斜挎：虎皮 / 豹皮（条纹 / 斑点两色交替）、绶带、托加
    const sashPath = [[-45, kit.pelvisY + 0.02 * k], [-8, kit.spineY + 0.08 * k], [22, kit.chestY + 0.06 * k], [55, kit.shoulderY - 0.01 * k], [105, kit.shoulderY + 0.005 * k], [160, kit.shoulderY - 0.04 * k], [215, kit.chestY], [270, kit.spineY + 0.04 * k], [315, kit.pelvisY + 0.02 * k]];
    const bandSub = D.det >= 2 ? 3 : D.det >= 1 ? 2 : 1;
    if (L.pelt) drapeBand(kit, sashPath, 0.13 * k, L.pelt === 'leopard' ? [C3('#D8A848'), C3('#E2B860'), C3('#3A2A1E')] : [C3('#D8782A'), C3('#2A2220'), C3('#E08A34')], 0.016 * k, bandSub);
    if (L.sash && !L.toga) drapeBand(kit, sashPath, 0.065 * k, X.cloth2, 0.014 * k, bandSub);
    if (L.toga) {
      const tc = L.white ? X.cloth : C3('#ECE6D8');
      drapeBand(kit, sashPath, 0.22 * k, [tc, kit.P.shade(tc, 0.94)], 0.02 * k, bandSub);
    }
    // 彝族背心：前开的短背心
    if (L.vest) {
      M.bone = B.chest;
      const y1 = kit.shoulderY - 0.01 * k, y0 = kit.spineY;
      M.panel([0, 0, 0], 26, 334, D.tor, [[kit.torsoR(y1) * 1.1, y1], [kit.torsoR(kit.chestY) * 1.1, kit.chestY], [kit.torsoR(y0) * 1.1, y0]], kit.depthK, X.cloth, X.clothDark, 0.008);
      if (D.det >= 1) torsoStrip(kit, [[27, y1], [27, y0]], 0.025 * k, X.cloth2, 0.02 * k);
    }
    kit.mark('torso');
  }

  // ----------------------------------------------------------- 颈与领 --
  function neckLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D;
    kit.neck(kit.P.shade(X.skin, 0.88), look.kind === 'general' ? 1.04 : 1.06);
    const cl = L.collar, gen = look.kind === 'general', t = L.torso || 'cloth';
    const ny = kit.neckY;
    M.bone = B.chest;
    if (cl === 'scarf') {
      M.color = gen ? (X.tabard || X.team) : X.team;
      M.push().translate(0, ny + 0.03 * k, -0.012).rotate(10, 0, 0).scale(1, 1, 0.92).translate(0, -ny - 0.03 * k, 0);
      M.lathe([[0.112 * k, ny - 0.015 * k], [0.104 * k, ny + 0.035 * k], [0.084 * k, ny + 0.065 * k]], D.tor, false, false, false, null, 180 / D.tor);
      M.pop();
      if (D.det >= 1 && !gen) { M.sphere([0.02 * k, ny - 0.0 * k, 0.1 * k], [0.03 * k, 0.025 * k, 0.02 * k], 5, 2); }
    } else if (cl === 'fur') {
      M.color = C3(PALF.furLight);
      const keep = M.jitterAll; M.jitterAll = true;
      M.push().translate(0, ny + 0.0 * k, -0.01).scale(1, 0.6, 0.86);
      M.torus([0, 0, 0], 0.115 * k, 0.04 * k, D.det >= 1 ? 10 : 7, 4);
      M.pop(); M.jitterAll = keep;
    } else if (cl === 'gold') {
      M.color = X.gold;
      M.push().translate(0, ny - 0.02 * k, 0.0).rotate(14, 0, 0).scale(1, 1, 0.84);
      M.lathe([[0.17 * k, -0.035 * k], [0.12 * k, 0.012 * k], [0.1 * k, 0.02 * k]], D.tor, false, false, false, null, 180 / D.tor);
      M.pop();
    } else if (cl === 'stand') {
      // 立领：从袍里立起、绕到颈后（米色）
      M.color = kit._robeTrim || X.cream;
      M.push().translate(0, ny + 0.06 * k, -0.012).rotate(14, 0, 0).scale(1, 1, 0.9).translate(0, -ny - 0.06 * k, 0);
      M.lathe([[0.102 * k, ny + 0.03 * k], [0.086 * k, ny + 0.1 * k]], D.det >= 1 ? 8 : 6, false, false, false, null, 22.5);
      M.pop();
    } else if (t !== 'bare' && t !== 'muscle' && !(L.toga)) {
      // 立领（衣领向前倾：后颈高、喉前低）
      M.color = t === 'robe' || t === 'cloth' ? X.cloth : gen ? X.cloth : X.team;
      M.push().translate(0, ny + 0.05 * k, -0.012).rotate(14, 0, 0).scale(1, 1, 0.92).translate(0, -ny - 0.05 * k, 0);
      M.lathe([[0.096 * k, ny + 0.025 * k], [0.078 * k, ny + 0.085 * k]], D.tor, false, false, false, null, 180 / D.tor);
      M.pop();
    }
    kit.mark('neck');
  }

  // ----------------------------------------------------------- 手臂 --
  function armsLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D;
    const sl = L.sleeves || 'cloth', gen = look.kind === 'general', br = L.bracer || 'none';
    const sleeve = gen ? X.cloth : (L.torso === 'bare' ? X.cloth : X.team);
    const hand = gen && br === 'metal' ? X.leatherDark : X.skin;
    const mus = sl === 'bare' ? 1 + clamp((kit.pr.build - 1) * 2.5, 0, 0.35) : 1;
    // 肩头被护肩 / 短袖 / 锁子袖盖住：低细节层不建肩球
    const covered = (L.pauldron && L.pauldron !== 'none') || sl === 'short' || sl === 'mail';
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R';
      if (sl === 'bare') kit.arm(s, X.skin, X.skin, hand, { upK: mus, foK: mus * 0.98, covered });
      else if (sl === 'short') {
        kit.arm(s, X.skin, X.skin, hand, { upK: mus, covered });
        kit.armFrame(s); kit.bone('upperArm' + S); M.color = sleeve;
        M.lathe([[kit.armR * 1.35, -kit.upL * 0.5], [kit.armR * 1.28, 0.02 * k]], D.lim, false, false, false);
        if (D.det >= 1) { M.color = gen ? X.trim : shT(sleeve, 0.75); M.band([0, 0, 0], kit.armR * 1.36, kit.armR * 1.38, -kit.upL * 0.52, -kit.upL * 0.44, D.lim); }
        M.pop();
      } else if (sl === 'mail') {
        kit.arm(s, X.metal, X.skin, hand, { elbow: X.metal, covered });
        kit.armFrame(s); kit.bone('upperArm' + S); M.color = X.metalDark;
        M.lathe([[kit.armR * 1.3, -kit.upL * 1.02], [kit.armR * 1.18, -kit.upL * 0.7]], D.lim, false, false, false);
        M.pop();
      } else if (sl === 'elbow') {
        // 袍袖到肘（深色衬里、米色袖口），护臂到腕，深色皮手套
        const trim = kit._robeTrim || X.cream, ns = D.det >= 1 ? 7 : 5;
        kit.arm(s, X.cloth, trim, br === 'metal' ? C3(PALF.gauntlet) : X.skin, { noShoulder: true, noUpper: true, noElbow: true, foreSides: Math.max(5, D.lim - 1), fistK: 0.62 });
        kit.armFrame(s); kit.bone('upperArm' + S);
        M.color = X.cloth;
        M.lathe([[kit.armR * 1.95, -kit.upL * 0.98], [kit.armR * 1.55, -kit.upL * 0.45], [kit.armR * 1.3, 0.02 * k]], ns, false, false, false);
        kit.innerTube([[kit.armR * 1.9, -kit.upL * 0.97], [kit.armR * 1.5, -kit.upL * 0.45]], ns, shT(X.cloth, 0.7));
        M.color = trim;
        M.band([0, 0, 0], kit.armR * 1.98, kit.armR * 2.02, -kit.upL * 1.02, -kit.upL * 0.94, ns);
        M.pop();
      } else if (sl === 'wide') {
        kit.arm(s, X.cloth, X.cloth, X.skin, { noShoulder: true, noUpper: true, noElbow: true, foreSides: Math.max(4, D.lim - 1) });
        kit.armFrame(s); kit.bone('upperArm' + S); M.color = X.cloth;
        M.lathe([[kit.armR * 1.75, -kit.upL * 1.0], [kit.armR * 1.45, -kit.upL * 0.45], [kit.armR * 1.25, 0.02 * k]], D.lim, false, false, false);
        kit.bone('foreArm' + S); M.color = X.cloth;
        // 广袖：自肘起外张、下垂到腕下（暗色衬里，浅色袖口）
        M.push().translate(0, -kit.upL, 0).rotate(0, 0, -s * 8);
        M.lathe([[kit.foreR * 3.2, -kit.foL * 1.06], [kit.foreR * 2.5, -kit.foL * 0.55], [kit.armR * 1.6, 0.0]], D.lim + 1, false, false, false);
        // 袖里（朝内的衬里）：从袖口看进去是衬里色，不是墨线外壳的内壁（一团黑）
        kit.innerTube([[kit.foreR * 3.1, -kit.foL * 1.05], [kit.foreR * 2.42, -kit.foL * 0.55], [kit.armR * 1.52, -0.01]], D.lim + 1, shT(X.cloth, 0.62));
        if (D.det >= 1) { M.color = X.cloth2 && kit.P.luma(X.cloth2) > 0.35 ? X.cloth2 : X.cream; M.band([0, 0, 0], kit.foreR * 3.24, kit.foreR * 3.28, -kit.foL * 1.08, -kit.foL * 0.99, D.lim + 1); }
        M.pop(); M.pop();
      } else {
        // 布袖（衣色）：袖口一道镶边
        kit.arm(s, sleeve, sleeve, hand, { covered });
        if (D.det >= 1) {
          kit.armFrame(s); kit.bone('upperArm' + S); M.color = sleeve;
          M.lathe([[kit.armR * 1.25, -kit.upL * 0.95], [kit.armR * 1.18, 0.01]], D.lim, false, false, false);
          M.pop();
        }
        if (br === 'none') kit.cuff(s, gen ? X.trim : shT(sleeve, 0.78), 0.86, 0.97, 1.22);
      }
      if (br === 'metal') { kit.cuff(s, X.metal, 0.14, 0.86, 1.3); if (D.det >= 1) kit.cuff(s, gen ? X.goldDark : X.metalDark, 0.86, 0.99, 1.4); }
      else if (br === 'gold') kit.cuff(s, X.gold, 0.8, 0.97, 1.24);
      else if (br === 'leather') kit.cuff(s, X.leather, 0.35, 0.92, 1.22);
      if (L.armlet && (sl === 'bare' || sl === 'short') && D.det >= 1) kit.armRing(s, X.gold, sl === 'short' ? 0.56 : 0.34, sl === 'short' ? 0.66 : 0.44, 1.12 * mus);
    }
    kit.mark('arms');
  }

  // ----------------------------------------------------------- 护肩 --
  function pauldronsLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D, V = kit.V, J = kit.J;
    const tp = L.pauldron || 'none', gen = look.kind === 'general';
    if (tp === 'none') return;
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R', sh = J['upperArm' + S];
      kit.bone('sh' + S);
      if (tp === 'plate' || tp === 'lion') {
        // 三层叠甲护肩（FFX 式）：每层一片 120° 的圆壳，逐层外移下移，暗色下缘
        const r = kit.armR * 1.58 * (gen ? 1 : 0.9), lames = D.det >= 2 ? 3 : 2;
        const c = tp === 'lion' ? X.gold : X.metal, cd = tp === 'lion' ? X.goldDark : X.metalDark, cl = tp === 'lion' ? X.goldLight : X.metalLight;
        M.push().translate(V.add(sh, [s * 0.03 * k, 0.03 * k, -0.004]));
        for (let i = lames - 1; i >= 0; i--) {
          const kk = 1 - i * 0.08, n = i === 0 ? D.rnd + 2 : D.rnd;
          M.push().translate(s * i * 0.015 * k, -i * 0.05 * k, 0).rotate(0, 0, -s * (20 + i * 10));
          const rad = [r * kk, r * 0.82 * kk, r * 0.96 * kk];
          M.color = i === 1 ? shT(c, 0.93) : i === 0 ? cl : c;
          if (i === 0) M.shell([0, 0, 0], rad, -180, 180, n, 0, 68, D.det >= 2 ? 2 : 1); else M.shell([0, 0, 0], rad, -180, 180, n, 36, 68, 1);
          M.color = cd;
          M.shell([0, 0, 0], rad, -180, 180, n, 68, 84, 1);
          if (i === lames - 1) M.shell([0, 0, 0], V.mul(rad, 0.95), -180, 180, n, 40, 84, 1, true);
          M.pop();
        }
        if (tp === 'lion' && D.det >= 1) {
          // 兽面吞肩：金色面盘、深色眼与口
          M.push().translate(s * r * 0.92, -0.01 * k, 0).rotate(0, 0, -s * 90);
          M.color = X.goldLight; M.cylinder([0, -0.01, 0], r * 0.42, r * 0.36, 0.024, D.rnd);
          M.color = X.goldDark; M.cylinder([0, 0.012, 0], r * 0.18, r * 0.12, 0.016, Math.max(5, D.rnd - 2));
          M.pop();
        }
        M.pop();
      } else if (tp === 'lamellar' || tp === 'scale' || tp === 'rattan') {
        const c = tp === 'rattan' ? C3(PALF.rattan) : X.metal, c2 = tp === 'rattan' ? C3(PALF.rattanDark) : X.metalDark;
        for (let i = 0; i < (D.det >= 1 ? 2 : 1); i++) {
          M.color = i ? c2 : c;
          M.push().translate(V.add(sh, [s * (0.012 + i * 0.024) * k, (0.03 - i * 0.055) * k, -0.006])).rotate(0, 0, s * (26 + i * 10));
          M.sphere([0, 0, 0], [(0.1 - i * 0.01) * k, 0.05 * k, (0.1 - i * 0.01) * k], D.sph[0], D.sph[1]);
          M.pop();
        }
      } else if (tp === 'fur') {
        M.color = C3(PALF.fur);
        const keep = M.jitterAll; M.jitterAll = true;
        M.push().translate(V.add(sh, [s * 0.02 * k, 0.03 * k, -0.01])).rotate(0, 0, s * 24);
        M.sphere([0, 0, 0], [0.11 * k, 0.065 * k, 0.11 * k], D.sph[0], D.sph[1]);
        M.pop(); M.jitterAll = keep;
      } else if (tp === 'segm' || tp === 'bands') {
        const n = D.det >= 1 ? 4 : 2;
        for (let i = 0; i < n; i++) {
          M.color = i % 2 ? X.metal : X.metalLight;
          M.push().translate(V.add(sh, [s * (0.01 + i * 0.012) * k, (0.035 - i * 0.032) * k, -0.006])).rotate(0, 0, -s * (16 + i * 9));
          const rr = kit.armR * (1.42 + i * 0.05);
          M.shell([0, 0, 0], [rr, rr * 0.7, rr * 0.95], -180, 180, D.rnd, i === 0 ? 0 : 40, 72, 1);
          M.pop();
        }
      } else if (tp === 'pteruges') {
        M.color = shT(X.leather, 1.15);
        const n = D.det >= 1 ? 4 : 3;
        for (let i = 0; i < n; i++) {
          const a = lerp(-50, 50, (i + 0.5) / n) * D2R, r = kit.armR * 1.3;
          const p0 = V.add(sh, [s * (Math.cos(a) * r * 0.4 + 0.02 * k), -0.02 * k, Math.sin(a) * r]);
          M.strip(p0, V.add(p0, [s * 0.03 * k, -0.13 * k, Math.sin(a) * 0.02 * k]), 0.032 * k, 0.03 * k, [s, 0.1, Math.sin(a)], 0.006);
        }
      }
    }
    kit.mark('pauldrons');
  }

  // ----------------------------------------------------------- 腰裙 --
  function skirtLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D, J = kit.J;
    const sk = L.skirt || 'none', gen = look.kind === 'general';
    const top = kit.pelvisY + 0.01 * k, len = L.skirtLen || 0.55, hemY0 = Math.max(0.1, 1.04 - len) * k;
    const gap = 0.012, nt = D.pan, nts = D.pan + 1;
    const lined = D.det >= 1;
    const hemH = 0.05 * k;
    const prof = (yb, f, f0) => [[kit.hipR * (f0 || 1.12) + gap, top], [kit.hipR * 1.2 + gap, Math.max(yb + 0.02, kit.hipY - 0.04 * k)], [kit.hipR * f + gap, yb]];
    const four = (yb, f, outer, lining, hem, fB) => {
      kit.skirtPanel('skirtF', -30, 30, nt, prof(yb, f), outer, lined ? lining : null, hem, hemH);
      kit.skirtPanel('skirtB', 148, 212, nt, prof(yb, f * (fB || 1.04)), outer, lined ? lining : null, hem, hemH);
      kit.skirtPanel('thighL', 32, 146, nts, prof(yb, f * 1.02), outer, lined ? lining : null, hem, hemH);
      kit.skirtPanel('thighR', -146, -32, nts, prof(yb, f * 1.02), outer, lined ? lining : null, hem, hemH);
    };
    if (sk === 'splitrobe') {
      // 开衩的袍裙到膝下（深色衬里、米色下摆），两侧三排甲裙（随大腿）
      const robe = X.cloth, lin = shT(X.cloth, 0.75), trim = kit._robeTrim || X.cream, hemY = 0.42 * k;
      const prof2 = f => [[kit.hipR * 1.14, top], [kit.hipR * 1.32, kit.hipY - 0.2 * k], [kit.hipR * 1.5 * f, hemY]];
      kit.skirtPanel('skirtF', -30, 30, nt, prof2(1), robe, lined ? lin : null, trim, 0.06 * k);
      kit.skirtPanel('skirtB', 148, 212, nt, prof2(1.04), robe, lined ? lin : null, trim, 0.06 * k);
      kit.skirtPanel('thighL', 32, 146, nts, prof2(1.02), robe, lined ? lin : null, trim, 0.06 * k);
      kit.skirtPanel('thighR', -146, -32, nts, prof2(1.02), robe, lined ? lin : null, trim, 0.06 * k);
      const rows = D.det >= 1 ? 3 : 2;
      for (const s of [1, -1]) {
        const bn = s > 0 ? 'thighL' : 'thighR';
        for (let i = 0; i < rows; i++) {
          const yy = rows === 3 ? [kit.pelvisY, kit.hipY - 0.06 * k, kit.hipY - 0.17 * k, kit.hipY - 0.29 * k] : [kit.pelvisY, kit.hipY - 0.12 * k, kit.hipY - 0.29 * k];
          const r0 = kit.hipR * (1.27 + i * 0.05), r1 = kit.hipR * (1.34 + i * 0.05);
          kit.skirtPanel(bn, s > 0 ? 58 : -122, s > 0 ? 122 : -58, Math.max(2, nt), [[r0, yy[i] + 0.01], [r1, yy[i + 1]]], i % 2 ? X.metalDark : X.metal, lined ? X.metalDark : null, null, 0, 0.9);
        }
      }
    } else if (sk === 'robe' || sk === 'coat' || sk === 'sarong' || sk === 'tunic') {
      const cc = X.cloth, hem = sk === 'sarong' ? X.cloth2 : (gen ? (L.white ? X.cloth2 : X.trim) : shT(cc, 0.75));
      const yb = sk === 'tunic' ? Math.max(hemY0, J.shinL[1] + 0.02 * k) : hemY0;
      const fl = sk === 'robe' ? 1.55 : sk === 'coat' ? 1.42 : sk === 'sarong' ? 1.22 : 1.32;
      four(yb, fl, cc, X.inner, hem);
    } else if (sk === 'lamellar' || sk === 'scale' || sk === 'rattan') {
      // 甲裙：一层布裙 + 三排甲片（两侧随大腿）
      const yb = Math.max(hemY0, J.shinL[1] - 0.04 * k);
      four(yb, 1.38, X.cloth, X.inner, gen ? X.trim : null);
      const a = sk === 'rattan' ? C3(PALF.rattan) : X.metal, b = sk === 'rattan' ? C3(PALF.rattanDark) : sk === 'scale' ? X.metalLight : X.metalDark;
      const rows = D.det >= 1 ? 3 : 2;
      for (const s of [1, -1]) {
        const bn = s > 0 ? 'thighL' : 'thighR';
        for (let i = 0; i < rows; i++) {
          const y0 = top - i * (top - (kit.hipY - 0.29 * k)) / rows, y1 = top - (i + 1) * (top - (kit.hipY - 0.29 * k)) / rows;
          const r0 = kit.hipR * (1.27 + i * 0.05) + gap, r1 = kit.hipR * (1.34 + i * 0.05) + gap;
          kit.skirtPanel(bn, s > 0 ? 50 : -130, s > 0 ? 130 : -50, Math.max(2, nt), [[r0, y0 + 0.01], [r1, y1]], i % 2 ? b : a, lined && i === rows - 1 ? X.seam : null, null, 0, 0.9);
        }
      }
      if (gen && D.det >= 1) { M.bone = B.skirtF; const y0 = top, y1 = kit.hipY - 0.2 * k; kit.skirtPanel('skirtF', -24, 24, 2, [[kit.hipR * 1.3 + gap, y0], [kit.hipR * 1.36 + gap, y1]], a, null, null); }
    } else if (sk === 'kilt') {
      const pc = L.pelt ? C3(L.pelt === 'leopard' ? '#D8A848' : '#D8782A') : X.cloth;
      const yb = kit.hipY - 0.3 * k;
      four(yb, 1.3, pc, X.inner, L.pelt ? C3('#2A2220') : shT(pc, 0.75));
    } else if (sk === 'bands') {
      // 草摺：多层横排铁板，逐层外扩
      four(Math.max(hemY0, J.shinL[1] + 0.06 * k), 1.3, X.cloth, X.inner, null);
      const rows = D.det >= 1 ? 4 : 2;
      for (let j = 0; j < rows; j++) {
        const y0 = top - j * 0.085 * k * (4 / rows), y1 = y0 - 0.08 * k * (4 / rows);
        const r0 = kit.hipR * (1.22 + j * 0.07) + gap, r1 = kit.hipR * (1.27 + j * 0.07) + gap;
        const c = j % 2 ? X.metalDark : X.metal;
        kit.skirtPanel('thighL', 20, 160, nts, [[r0, y0], [r1, y1]], c, null, null);
        kit.skirtPanel('thighR', -160, -20, nts, [[r0, y0], [r1, y1]], c, null, null);
        kit.skirtPanel('skirtF', -22, 22, 1, [[r0, y0], [r1, y1]], c, null, null);
        kit.skirtPanel('skirtB', 158, 202, 1, [[r0, y0], [r1, y1]], c, null, null);
      }
    } else if (sk === 'pteruges') {
      // 罗马：短衣（红）+ 一圈皮条
      const tun = look.culture === 'roman' ? C3('#B04030') : X.cloth;
      four(J.shinL[1] + 0.12 * k, 1.3, tun, shT(tun, 0.8), null);
      const n = D.det >= 1 ? 12 : 8;
      const lc = shT(X.leather, 1.2);
      for (let i = 0; i < n; i++) {
        const a = -180 + (i + 0.5) * 360 / n, w = 360 / n * 0.42;
        const bn = Math.abs(a) < 30 ? 'skirtF' : Math.abs(a) > 150 ? 'skirtB' : a > 0 ? 'thighL' : 'thighR';
        kit.skirtPanel(bn, a - w, a + w, 1, [[kit.hipR * 1.24 + gap, top + 0.005], [kit.hipR * 1.34 + gap, kit.hipY - 0.26 * k]], lc, null, null);
      }
    }
    // 前垂 / 后垂（势力色、镶边）：挂在前后垂骨上
    if (X.tabard && L.tabard) {
      const off = sk === 'lamellar' || sk === 'scale' ? 1.42 : 1.3, tb = gen ? 0.44 * k : Math.max(0.5 * k, J.shinL[1]);
      kit.skirtPanel('skirtF', -22, 22, Math.max(1, nt - 1), [[kit.hipR * off + gap, top + 0.005], [kit.hipR * (off + 0.12) + gap, tb]], X.tabard, lined ? X.tabardDark : null, gen ? X.trim : shT(X.tabard, 0.7), 0.04 * k);
      if (gen) kit.skirtPanel('skirtB', 156, 204, Math.max(1, nt - 1), [[kit.hipR * off + gap, top + 0.005], [kit.hipR * (off + 0.14) + gap, tb + 0.06 * k]], shT(X.tabard, 0.9), lined ? X.tabardDark : null, X.trim, 0.04 * k);
    }
    kit.mark('skirt');
  }

  // ----------------------------------------------------------- 腰带 --
  function beltLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D, V = kit.V;
    if (!L.belt) return;
    const gen = look.kind === 'general', sash = L.belt === 'sash';
    const bz = kit.waistR * 1.2 * kit.depthK * 1.06;
    kit.bone('pelvis');
    if (sash) {
      M.color = X.cloth2 || X.team;
      M.band([0, 0, 0], kit.waistR * 1.22, kit.waistR * 1.2, kit.pelvisY - 0.04 * k, kit.pelvisY + 0.03 * k, D.tor + 2, kit.depthK * 1.06);
      if (D.det >= 1) {
        // 腰结与两条垂带（随前垂骨）
        const knot = [0.07 * k, kit.pelvisY - 0.02 * k, bz + 0.015];
        M.sphere(knot, [0.042 * k, 0.034 * k, 0.03 * k], 5, 2);
        kit.bone('skirtF');
        M.curvedStrip(V.add(knot, [-0.01, -0.02, 0.005]), V.add(knot, [0.0, -0.16 * k, 0.04]), V.add(knot, [0.03 * k, -0.36 * k, 0.03]), 0.055 * k, 0.045 * k, [0, 0, 1], 0.012, 2);
        M.curvedStrip(V.add(knot, [0.015, -0.02, 0.0]), V.add(knot, [0.06 * k, -0.14 * k, 0.03]), V.add(knot, [0.085 * k, -0.3 * k, 0.015]), 0.045 * k, 0.036 * k, [0.3, 0, 1], 0.012, 2);
      }
    } else {
      M.color = gen ? C3('#6E3B2C') : X.leather;
      M.band([0, 0, 0], kit.waistR * 1.2, kit.waistR * 1.2, kit.pelvisY - 0.005 * k, kit.pelvisY + 0.055 * k, D.tor + 2, kit.depthK * 1.06);
      M.color = gen ? X.goldLight : X.gold;
      M.box([0, kit.pelvisY + 0.025 * k, bz + 0.012], [(gen ? 0.1 : 0.06) * k, (gen ? 0.08 : 0.05) * k, 0.028]);
      if (gen && D.det >= 1) {
        M.color = X.goldDark; M.box([0, kit.pelvisY + 0.027 * k, bz + 0.03], [0.05 * k, 0.03 * k, 0.012]);
        M.color = X.gold; for (const s of [1, -1]) M.box([s * kit.waistR * 0.95, kit.pelvisY + 0.025 * k, bz * 0.72], [0.05 * k, 0.05 * k, 0.04]);
      }
      // 名将：朱红束带压在皮带下（关羽式）
      if (gen && L.torso !== 'robe' && D.det >= 1 && (L.tabard || L.torso === 'plate' || L.torso === 'lamLace' || L.openRobe)) {
        M.color = C3(PALF.red);
        kit.bone('pelvis');
        M.band([0, 0, 0], kit.waistR * 1.24, kit.waistR * 1.22, kit.pelvisY - 0.04 * k, kit.pelvisY - 0.004 * k, D.tor + 2, kit.depthK * 1.06);
        if (L.sashKnot) {
          // 左前的结与两条垂带（随前垂骨）
          const knot = [0.07 * k, kit.pelvisY - 0.03 * k, bz + 0.02];
          M.sphere(knot, [0.045 * k, 0.035 * k, 0.03 * k], 5, 2, false);
          kit.bone('skirtF');
          const v0 = M.vertexCount;
          const sg = D.det >= 2 ? 2 : 1;
          M.curvedStrip(V.add(knot, [-0.01, -0.02, 0.005]), V.add(knot, [0.0, -0.18 * k, 0.04]), V.add(knot, [0.035 * k, -0.4 * k, 0.03]), 0.06 * k, 0.05 * k, [0, 0, 1], 0.012, sg);
          M.curvedStrip(V.add(knot, [0.015, -0.02, 0.0]), V.add(knot, [0.06 * k, -0.15 * k, 0.03]), V.add(knot, [0.09 * k, -0.33 * k, 0.015]), 0.05 * k, 0.04 * k, [0.3, 0, 1], 0.012, sg);
          M.windBy(v0, p => 0.3 * clamp01((knot[1] - p[1]) / (0.4 * k)));
          kit.bone('pelvis');
        }
      }
    }
    kit.mark('belt');
  }

  // ----------------------------------------------------------- 披风、飘带、背具 --
  function capeLP(kit, X) {
    const M = kit.M, look = kit.look, L = look.L, k = kit.k, D = kit.D, J = kit.J;
    if (!X.cape) return;
    const gen = look.kind === 'general';
    const yb = (gen ? 0.32 : 0.62) * k;
    const sy = kit.shoulderY + 0.02 * k, sr = kit.shoulderR * 1.04;
    const rows = D.det >= 2 ? 5 : D.det >= 1 ? 3 : 2;
    const prof = [];
    for (let i = 0; i <= rows; i++) {
      const t = i / rows, y = lerp(sy, yb, Math.pow(t, 0.95));
      const r = lerp(sr, 0.3 * k * (gen ? 1.05 : 0.9), Math.pow(t, 0.7)) + 0.06 * k * Math.sin(PI * t) * 0.5;
      prof.push([r, y]);
    }
    const v0 = M.vertexCount;
    M.bone = B.chest;
    M.panel([0, 0, -0.02 * k], 112, 248, D.det >= 2 ? 6 : 4, prof, 0.9, X.cape, X.capeIn, 0.012);
    // 肩上的披风领（小披肩），挂在胸骨上
    if (D.det >= 2) {
      M.color = shT(X.cape, 0.9);
      M.panel([0, 0, -0.015 * k], 70, 290, 8, [[0.11 * k, kit.neckY + 0.03 * k], [sr * 1.02, sy + 0.005 * k], [sr * 1.06, sy - 0.05 * k]], kit.depthK * 1.15, shT(X.cape, 0.9), X.capeIn, 0.01);
    }
    // 分骨：肩上随胸，往下依次 cape1 / cape2 / cape3（骨链，越往下摆动越大）
    const y1 = J.cape1[1] - 0.06 * k, y2 = J.cape2[1], y3 = J.cape3[1];
    M.boneBy(v0, p => (p[1] > y1 ? B.chest : p[1] > y2 ? B.cape1 : p[1] > y3 ? B.cape2 : B.cape3));
    M.windBy(v0, p => 0.12 * clamp01((sy - p[1]) / (sy - yb)));
    kit.mark('cape');
  }
  function ribbonsLP(kit, X) {
    const M = kit.M, look = kit.look, k = kit.k, D = kit.D, V = kit.V;
    if (!look.ribbons || D.det < 1) return;
    const c = C3(look.ribbons);
    const v0 = M.vertexCount;
    for (const s of [1, -1]) {
      M.color = c;
      const a = [s * 0.11 * k, kit.pelvisY, -kit.waistR * kit.depthK - 0.02 * k];
      const m = V.add(a, [s * 0.05 * k, -0.4 * k, -0.12 * k]), b = V.add(a, [s * 0.1 * k, -0.85 * k, -0.18 * k]);
      M.curvedStrip(a, m, b, 0.06 * k, 0.045 * k, [0, 0, -1], 0.006, D.det >= 2 ? 4 : 2);
    }
    M.boneBy(v0, p => (p[1] > kit.pelvisY - 0.08 * k ? B.pelvis : p[1] > kit.J.cape3[1] ? B.skirtB : B.cape3));
    M.windBy(v0, p => 0.5 * clamp01((kit.pelvisY - p[1]) / (0.8 * k)));
    kit.mark('ribbons');
  }
  function backGearLP(kit, X) {
    const M = kit.M, look = kit.look, k = kit.k, D = kit.D, V = kit.V;
    if (look.quiver) {
      kit.bone('chest');
      const a = [0.1 * k, 1.05 * k, -0.19 * k], b = [-0.12 * k, 1.5 * k, -0.21 * k];
      M.color = X.leather;
      M.segment(a, b, 0.042 * k, 0.036 * k, D.lim);
      if (D.det >= 1) { M.color = shT(X.leather, 0.75); M.band(V.lerp(a, b, 0.92), 0.04 * k, 0.04 * k, -0.01, 0.01, D.lim); }
      // 箭羽
      const dir = V.norm(V.sub(b, a));
      M.color = X.white;
      const nA = D.det >= 1 ? 4 : 2;
      for (let i = 0; i < nA; i++) {
        const o = [(-0.014 + (i % 2) * 0.028) * k, 0, (-0.01 + Math.floor(i / 2) * 0.02) * k];
        const p0 = V.add(b, o);
        M.blade(p0, V.add(p0, V.mul(dir, 0.1 * k)), 0.022 * k, [1, 0, 0]);
      }
      // 斜挎的箭袋带
      if (D.det >= 1) torsoStrip(kit, [[-150, 1.05 * k], [-110, kit.chestY], [-60, kit.shoulderY - 0.03 * k], [0, kit.chestY + 0.08 * k], [40, kit.spineY + 0.04 * k]], 0.028 * k, X.leather, 0.016 * k);
    }
    if (look.backBow) {
      kit.bone('chest');
      M.color = C3('#5A3418');
      const a = [0.3 * k, 0.95 * k, -0.21 * k], c = [0, 1.28 * k, -0.3 * k], b = [-0.28 * k, 1.6 * k, -0.21 * k];
      M.curve(a, c, b, 0.012 * k, 0.008 * k, D.det >= 1 ? 4 : 3, 4);
    }
    kit.mark('back');
  }

  // ======================================================== 头、脸、发、须、冠帽 ==
  // 脸从头像外貌参数（SG.Portrait.spec）而来：肤色、眼形与眼色、眉形与粗细、鼻形、嘴、耳、胡须、疤、眼罩、面纹。
  const EYE_MAP = {
    normal: ['almond', 0, 1], sharp: ['almond', 8, 1], phoenix: ['narrow', 10, 1.18], round: ['round', 0, 1.12], narrow: ['narrow', 2, 1],
    gentle: ['almond', -5, 1], big: ['round', 0, 1.06], sleepy: ['narrow', -6, 0.95],
  };
  const BROW_MAP = {
    straight: [0, 1], arched: [-6, 1], angled: [16, 1.05], bushy: [6, 1.45], thin: [2, 0.75], knit: [20, 1.15], silkworm: [18, 1.45], sword: [18, 1.2], worried: [-14, 1],
  };
  const NOSE_MAP = { straight: [1, 0.32, 0.2], aquiline: [1.08, 0.42, 0.19], broad: [1.08, 0.28, 0.28], button: [0.86, 0.26, 0.2], small: [0.8, 0.26, 0.17], bulb: [1.2, 0.36, 0.27], flat: [0.95, 0.2, 0.28] };
  function rimFn(front, side, back) {
    return th => { const a = Math.abs(th); if (a < 55) return lerp(front, lerp(front, side, 0.2), a / 55); if (a < 110) return lerp(lerp(front, side, 0.2), side, (a - 55) / 55); return lerp(side, back, (a - 110) / 70); };
  }
  function faceLP(kit, X) {
    const M = kit.M, look = kit.look, sp = look.spec || {}, D = kit.D, R = kit.R, P = kit.P, V = kit.V;
    const acc = look.acc || [], marks = look.marks || [];
    // 脸：眉下的颊面略暗（眉弓受光多）
    kit.headShape(X.skin, 0.92);
    if (D.ears) kit.ears(X.skin, acc.includes('bigears') || (sp.ears && sp.ears.size >= 1.3));
    const eyesS = sp.eyes || {}, browsS = sp.brows || {}, expr = sp.expr || look.expr || 'neutral';
    let em = EYE_MAP[eyesS.shape] || (look.kind === 'soldier' ? ['almond', 0, 1.1] : ['almond', 0, 1]);
    if (look.sex === 'f' && !eyesS.shape) em = ['round', 0, 1.06];
    const bm = BROW_MAP[browsS.shape] || [look.kind === 'soldier' ? -4 : 4, 1.1];
    let tilt = bm[0] + (expr === 'fierce' ? 7 : expr === 'stern' ? 3 : expr === 'smile' ? -3 : 0);
    const iris = lift(eyesS.color || '#3a2618', 0.16);
    const brow = hairTone(browsS.color || (look.hair && look.hair.color) || '#221a18');
    const size = em[2] * clamp(eyesS.size || 1, 0.88, 1.15) * (look.kind === 'soldier' ? 1.1 : 1.15);
    const thick = clamp(browsS.thick || 1, 0.6, 1.5);
    kit.eyes(iris, brow, em[0], tilt, -0.03 + (eyesS.y || 0) * 2, 0.41 + (eyesS.gap || 0) * 2, size, em[1], bm[1] * (0.75 + 0.3 * thick) * (look.sex === 'f' ? 0.75 : 1),
      { patch: acc.includes('eyepatch'), browH: look.sex === 'f' ? 0.7 : 1 });
    const nm = NOSE_MAP[(sp.nose && sp.nose.shape) || 'straight'] || NOSE_MAP.straight;
    kit.nose(P.shade(X.skin, 0.84), nm[0] * clamp((sp.nose && sp.nose.size) || 1, 0.85, 1.2) * (look.sex === 'f' ? 0.85 : 1), -0.42, nm[1], nm[2] * clamp((sp.nose && sp.nose.w) || 1, 0.85, 1.25));
    // 嘴：有长须 / 满腮胡时藏在须下
    const bst = (look.beard && look.beard.style) || 'none';
    const mshape = (sp.mouth && sp.mouth.shape) || (expr === 'smile' ? 'smile' : expr === 'fierce' || expr === 'stern' ? 'grim' : 'neutral');
    if (!(bst === 'full' || bst === 'bristle' || bst === 'flowing' || bst === 'long' || bst === 'curled' || bst === 'braided') || look.sex === 'f') {
      const lip = look.sex === 'f' ? P.mix(P.shade(X.skin, 0.72), [0.75, 0.28, 0.26], 0.45) : P.mix(P.shade(X.skin, 0.62), [0.55, 0.25, 0.22], 0.25);
      kit.mouth(lip, -0.68, 0.3 * clamp((sp.mouth && sp.mouth.w) || 1, 0.85, 1.2), mshape === 'smile' ? 0.08 : mshape === 'grim' ? -0.05 : mshape === 'smirk' ? 0.03 : 0.02);
    }
    // 面部标记
    const mark = (x0, y0, x1, y1, w, c, ink) => {
      const a = kit.facePoint(x0, y0, 0.03), b = kit.facePoint(x1, y1, 0.03);
      M.color = c; M.ink = ink == null ? 0.3 : ink;
      M.strip(a, b, w * R, w * R, [(x0 + x1) * 0.5, 0, 1], 0.012 * R);
      M.ink = 1;
    };
    kit.bone('head');
    if (marks.includes('scar') || marks.includes('scars')) mark(0.5, 0.28, 0.22, -0.4, 0.07, P.mix(X.skin, [0.95, 0.85, 0.8], 0.4));
    if (marks.includes('scars')) mark(-0.18, 0.35, -0.5, -0.2, 0.06, P.mix(X.skin, [0.95, 0.85, 0.8], 0.4));
    if (marks.includes('mole') && D.det >= 1) { M.color = P.shade(X.skin, 0.45); M.ink = 0.2; M.sphere(kit.facePoint(-0.32, -0.55, 0.02), 0.035 * R, 4, 2); M.ink = 1; }
    if (marks.includes('warpaint')) for (const s of [1, -1]) { mark(s * 0.22, -0.2, s * 0.62, -0.26, 0.07, C3('#B83A2A')); if (D.det >= 1) mark(s * 0.25, -0.34, s * 0.6, -0.4, 0.06, C3('#B83A2A')); }
    if (marks.includes('woad')) for (const s of [1, -1]) { mark(s * 0.2, 0.05, s * 0.55, -0.45, 0.08, C3('#3A5A9A')); }
    if (marks.includes('tattoo-wa') || marks.includes('tattoo-yi')) for (const s of [1, -1]) { mark(s * 0.18, -0.2, s * 0.58, -0.05, 0.05, C3('#2A3A4A')); if (D.det >= 1) mark(s * 0.2, -0.36, s * 0.55, -0.48, 0.05, C3('#2A3A4A')); }
    if (marks.includes('cheekscars') && D.det >= 1) for (const s of [1, -1]) for (let i = 0; i < 3; i++) mark(s * 0.3, -0.12 - i * 0.12, s * 0.62, -0.14 - i * 0.12, 0.035, P.shade(X.skin, 0.7));
    if (marks.includes('tilaka') && D.det >= 1) { M.color = C3('#C8282A'); M.ink = 0.2; M.sphere(kit.facePoint(0, 0.36, 0.02), 0.04 * R, 4, 2); M.ink = 1; }
    if (acc.includes('eyepatch')) {
      // 眼罩的系带：额上斜过头侧
      M.color = C3(PALF.black);
      M.shell(kit.headAt(0, 0.02, -0.01), kit.hv(1.04, 1.04, 1.02), 20, 150, D.det >= 1 ? 6 : 3, th => lerp(60, 92, clamp01((th - 20) / 120)) - 4, th => lerp(60, 92, clamp01((th - 20) / 120)) + 4, 1);
    }
    kit.mark('face');
  }

  // ----------------------------------------------------------- 冠帽 --
  // 返回 { top: 帽沿线 φ(θ)（盖住头顶时）, bun: 露出发髻, back: 盖住后脑, ears: 盖住耳 }
  function feather(kit, pts, w0, w1, c, bones, windK) {
    const M = kit.M, V = kit.V, v0 = M.vertexCount;
    for (let i = 0; i < pts.length - 1; i++) {
      const a = pts[i], b = pts[i + 1], t0 = i / (pts.length - 1), t1 = (i + 1) / (pts.length - 1);
      M.color = Array.isArray(c[0]) ? c[i % c.length] : c;
      const side = V.cross(V.sub(b, a), [0, 0, 1]);
      M.strip(a, b, lerp(w0, w1, t0), lerp(w0, w1, t1), V.cross(side, V.sub(b, a)), 0.008);
    }
    if (bones) { const y0 = pts[0][1], y1 = pts[pts.length - 1][1], d0 = pts[0]; M.boneBy(v0, p => { const t = V.len(V.sub(p, d0)) / Math.max(1e-3, V.len(V.sub(pts[pts.length - 1], d0))); return t < 0.2 ? B.head : t < 0.6 ? B.plume1 : B.plume2; }); void y0; void y1; }
    if (windK) M.windBy(v0, p => windK * clamp01(V.len(V.sub(p, pts[0])) / Math.max(1e-3, V.len(V.sub(pts[pts.length - 1], pts[0])))));
  }
  function horseTassel(kit, at, size, c) {
    // 缨：一簇向下散开的马尾（顶上一颗金钮）
    const M = kit.M, k = kit.k, D = kit.D, v0 = M.vertexCount;
    M.color = c;
    M.push().translate(at);
    if (D.det === 0) M.lathe([[0.006 * k * size, 0.012 * k * size], [0.04 * k * size, -0.05 * k * size], [0.01 * k * size, -0.12 * k * size]].reverse(), 5, false, false, true);
    else if (D.det === 1) M.lathe([[0.006 * k * size, 0.012 * k * size], [0.035 * k * size, -0.025 * k * size], [0.036 * k * size, -0.08 * k * size], [0.008 * k * size, -0.12 * k * size]].reverse(), 5, false, false, true);
    else M.lathe([[0.006 * k * size, 0.012 * k * size], [0.03 * k * size, -0.015 * k * size], [0.04 * k * size, -0.06 * k * size], [0.03 * k * size, -0.1 * k * size], [0.008 * k * size, -0.12 * k * size]].reverse(), Math.max(5, D.rnd), false, true, true);
    M.pop();
    M.windBy(v0, p => 0.35 * clamp01((at[1] - p[1]) / (0.1 * k * size)));
  }
  function headgearLP(kit, X) {
    const M = kit.M, look = kit.look, hat = look.hat || { type: 'none' }, tp = hat.type || 'none', D = kit.D, k = kit.k, R = kit.R, V = kit.V, P = kit.P;
    const gen = look.kind === 'general';
    const res = { top: null, bun: false, back: false, ears: false, under: 0 };
    const hc = hat.color ? lift(hat.color, 0.18) : null;
    const metal = hc || X.metal, plume = hat.plume ? lift(hat.plume, 0.25) : X.plume;
    const np = D.det >= 2 ? 3 : 2;
    const crownAt = (dy) => kit.headAt(0, 1.08 + (dy || 0), -0.04);
    kit.bone('head');
    const ring = (c, line, w, puff) => { M.color = c; M.shell(kit.capC, V.mul(kit.capR, puff || 1.03), -180, 180, D.hs, th => line(th) - w, th => line(th) + 1, 1); };
    if (tp === 'helmet' || tp === 'wahelm') {
      const wa = tp === 'wahelm', v = hat.variant || 'plain';
      const line = rimFn(64, 92, wa ? 108 : 104);
      kit.capShell(wa ? (hc || C3('#3A3C44')) : metal, 1.17, line, np);
      if (D.det >= 1) ring(wa ? C3('#2E3036') : X.trim, line, 7, 1.035);
      // 眉庇
      M.color = wa ? C3('#2E3036') : X.trim;
      M.shell(kit.capC, V.mul(kit.capR, 1.1), -60, 60, Math.max(2, D.hs / 3), th => line(th) - 3, th => line(th) + 6, 1);
      // 顿项（护颈）：盔后自盔沿外张下垂，名将为布色、士兵为甲片
      const nk = gen ? (X.cloth2 || X.clothDark) : wa ? C3('#3A3C44') : X.metalDark;
      const cy = kit.capC[1], rb = kit.capR[0];
      const yA = cy + kit.capR[1] * Math.cos(line(180) * D2R) + 0.01 * k;
      const rows = wa ? (D.det >= 1 ? 3 : 2) : 1;
      for (let i = 0; i < rows; i++) {
        const y0 = yA - i * 0.035 * k, y1 = y0 - (wa ? 0.045 : 0.1) * k;
        M.panel([0, 0, kit.capC[2]], 95, 265, Math.max(3, D.pan + 2), [[rb * (1.02 + i * 0.1), y0], [rb * (1.2 + i * 0.12), y1]], kit.capR[2] / kit.capR[0], i % 2 ? shT(nk, 0.85) : nk, D.det >= 1 ? X.lining : null, 0.006);
      }
      if (wa && D.det >= 1) {
        // 衝角：前方的鸟喙状脊
        M.color = C3('#3A3C44');
        M.push().translate(kit.capPoint(0, 40, 1.03)).rotate(-55, 0, 0);
        M.sphere([0, 0.03 * k, 0], [0.015 * k, 0.07 * k, 0.03 * k], 5, 3);
        M.pop();
      }
      if (!wa) {
        const top = kit.capPoint(0, 0, 1);
        if (v === 'spike') { M.color = metal; M.cone(V.add(top, [0, -0.01 * k, 0]), 0.03 * k, 0.13 * k, Math.max(5, D.rnd - 1)); }
        if (v === 'wing' && D.det >= 1) for (const s of [1, -1]) {
          const a = kit.capPoint(s * 85, 52, 1.0);
          M.color = X.trim;
          M.curvedStrip(a, V.add(a, [s * 0.05 * k, 0.08 * k, -0.01 * k]), V.add(a, [s * 0.07 * k, 0.17 * k, -0.06 * k]), 0.07 * k, 0.02 * k, [s, 0.1, 0.2], 0.008, D.det >= 2 ? 3 : 1);
        }
        if (v === 'horn') for (const s of [1, -1]) {
          const a = kit.capPoint(s * 70, 40, 1.0);
          M.color = C3('#E8E0C8');
          M.curve(a, V.add(a, [s * 0.07 * k, 0.05 * k, 0.02 * k]), V.add(a, [s * 0.05 * k, 0.15 * k, 0.07 * k]), 0.016 * k, 0.003 * k, D.det >= 1 ? 3 : 2, 5);
        }
        if (v === 'lion') {
          // 狮首盔（马超）：盔前金色兽面 + 一圈鬃簇
          const c = kit.capPoint(0, 42, 1.04);
          M.push().translate(c).rotate(-48, 0, 0);
          M.color = X.goldLight; M.cylinder([0, -0.006, 0], 0.05 * k, 0.045 * k, 0.018, D.rnd);
          M.color = X.goldDark; M.sphere([0, 0.012, -0.006 * k], [0.02 * k, 0.012, 0.016 * k], 5, 2);
          if (D.det >= 1) { M.color = C3('#C82828'); for (const s of [1, -1]) M.sphere([s * 0.018 * k, 0.012, 0.016 * k], 0.006 * k, 4, 2); }
          M.color = X.gold;
          if (D.det >= 1) for (let i = 0; i < 7; i++) { const a = (i / 6 - 0.5) * 2.6; M.spike([Math.sin(a) * 0.046 * k, 0, Math.cos(a) * 0.046 * k], [Math.sin(a), 0.3, Math.cos(a)], 0.012 * k, 0.03 * k, 4); }
          M.pop();
        }
        if (v === 'plume' || look.culture === 'korea') {
          const t0 = V.add(top, [0, 0.005, 0]);
          feather(kit, [t0, V.add(t0, [0, 0.1 * k, -0.02 * k]), V.add(t0, [0, 0.18 * k, -0.08 * k]), V.add(t0, [0, 0.22 * k, -0.17 * k])], 0.05 * k, 0.012 * k, plume, true, 0.3);
        } else {
          M.color = X.trim; if (D.det >= 1 || v === 'spike') M.cylinder(V.add(top, [0, v === 'spike' ? 0.08 * k : -0.006 * k, 0]), 0.011 * k, 0.009 * k, 0.03 * k, 5, false, false, D.det >= 1);
          horseTassel(kit, V.add(top, [0, (v === 'spike' ? 0.08 : 0.0) * k + 0.028 * k, 0]), gen ? 1.15 : 0.85, plume);
        }
        if (D.det >= 1) for (const s of [1, -1]) {
          // 护耳
          M.color = metal;
          M.shell(kit.capC, V.mul(kit.capR, 1.02), s > 0 ? 70 : -110, s > 0 ? 110 : -70, 2, th => line(th) - 2, th => line(th) + 26, 1);
        }
      }
      res.top = line; res.back = true; res.ears = true;
    } else if (tp === 'galea' || tp === 'celthelm') {
      const gal = tp === 'galea', line = rimFn(70, 96, 100);
      const c = hc || (gal ? (look.kind === 'soldier' ? C3('#B8BCC4') : C3(PALF.bronze)) : C3(PALF.bronze));
      kit.capShell(c, 1.15, line, np);
      ring(gal ? C3(PALF.bronze) : shT(c, 0.8), line, 6, 1.04);
      // 护颊
      M.color = gal ? C3(PALF.bronze) : c;
      for (const s of [1, -1]) M.shell(kit.capC, V.mul(kit.capR, 1.03), s > 0 ? 55 : -110, s > 0 ? 110 : -55, 2, th => line(th) - 2, th => line(th) + 42, 1);
      // 护颈外翻
      M.panel([0, 0, kit.capC[2]], 120, 240, Math.max(2, D.pan), [[kit.capR[0] * 1.0, kit.capC[1] + kit.capR[1] * Math.cos(100 * D2R)], [kit.capR[0] * 1.25, kit.capC[1] + kit.capR[1] * Math.cos(100 * D2R) - 0.035 * k]], kit.capR[2] / kit.capR[0], gal ? c : shT(c, 0.9), null);
      if (gal) {
        // 冠饰：纵向的红色马鬃
        const pts = [];
        for (let i = 0; i <= (D.det >= 1 ? 4 : 2); i++) { const n = D.det >= 1 ? 4 : 2, a = lerp(-55, 70, i / n); pts.push(kit.capPoint(0, Math.abs(a), 1.08 + 0.12 * Math.sin(PI * i / n)).map((v2, j) => (j === 2 && a < 0 ? v2 : v2))); }
        for (let i = 0; i < pts.length; i++) { const a = lerp(-55, 70, i / (pts.length - 1)); pts[i] = kit.capPoint(a < 0 ? 0 : 180, Math.abs(a), 1.0); pts[i][1] += 0.035 * k; }
        M.color = plume;
        for (let i = 0; i < pts.length - 1; i++) M.strip(pts[i], pts[i + 1], 0.075 * k, 0.075 * k, [1, 0, 0], 0.022 * k);
      } else { M.color = C3(PALF.bronze); M.sphere(kit.capPoint(0, 0, 1.04), [0.02 * k, 0.025 * k, 0.02 * k], 5, 2); }
      res.top = line; res.back = true; res.ears = true;
    } else if (tp === 'ze' || tp === 'jinxian' || tp === 'futou' || tp === 'lunjin' || tp === 'turban') {
      const cc = hc || (tp === 'turban' ? C3('#3A6A3A') : tp === 'lunjin' ? C3('#3A5A8C') : C3('#2E2A34'));
      const line = tp === 'ze' || tp === 'jinxian' ? rimFn(54, 84, 104) : tp === 'turban' ? rimFn(52, 82, 108) : rimFn(52, 86, 106);
      const puff = tp === 'turban' ? 1.16 : tp === 'lunjin' ? 1.1 : 1.09;
      kit.capShell(cc, puff, line, np, null, { ky: tp === 'lunjin' ? 1.14 : tp === 'futou' ? 1.06 : 1 });
      if (D.det >= 1 || tp !== 'ze') ring(shT(cc, 0.82), line, tp === 'turban' ? 12 : 8, 1.035);
      if (tp === 'ze' || tp === 'jinxian') res.bun = true;
      if (tp === 'turban' || tp === 'futou' || tp === 'lunjin') {
        if (tp === 'turban') { M.color = shT(cc, 0.9); M.shell(kit.capC, V.mul(kit.capR, 1.06), -180, 180, D.hs, th => line(th) - 30, th => line(th) - 16, 1); }
        if (tp === 'lunjin' && D.det >= 1) for (const f of [-24, -40]) ring(C3('#D8E2EE'), th => line(th) + f, 3, 1.02);
        // 脑后的结与两条垂带（随头发骨摆动）
        const kp = kit.capPoint(180, line(180) - 18, 1.02);
        M.color = cc; M.sphere(kp, [0.03 * k, 0.026 * k, 0.026 * k], 5, 2);
        if (D.det >= 1) {
          kit.bone('hair1');
          const v0 = M.vertexCount;
          for (const s of [1, -1]) M.curvedStrip(V.add(kp, [s * 0.01, -0.01, -0.01]), V.add(kp, [s * 0.04 * k, -0.08 * k, -0.04 * k]), V.add(kp, [s * 0.06 * k, (tp === 'lunjin' ? -0.24 : -0.16) * k, -0.05 * k]), 0.045 * k, 0.03 * k, [s * 0.3, 0, -1], 0.008, 2);
          M.windBy(v0, p => 0.45 * clamp01((kp[1] - p[1]) / (0.15 * k)));
          kit.bone('head');
        }
      }
      if (tp === 'jinxian') {
        // 进贤冠：前高后低的冠梁（斜板）
        M.color = cc;
        const f = kit.capPoint(0, 28, 1.0);
        M.push().translate(f).rotate(-28, 0, 0);
        M.box([0, 0.055 * k, -0.04 * k], [0.065 * k, 0.11 * k, 0.012 * k]);
        if (D.det >= 1) { M.color = shT(cc, 1.35); M.box([0, 0.11 * k, -0.04 * k], [0.07 * k, 0.012 * k, 0.016 * k]); }
        M.pop();
      }
      res.top = line; res.back = tp === 'turban' || tp === 'futou';
    } else if (tp === 'pagri' || tp === 'mukuta' || tp === 'aksumcrown') {
      const cc = tp === 'pagri' ? (hc || C3('#ECE6D8')) : lift(hat.color2 || (tp === 'mukuta' ? '#A8302A' : '#ECE6D8'), 0.2);
      const line = rimFn(50, 84, 106);
      kit.capShell(cc, 1.2, line, np, null, { ky: tp === 'aksumcrown' ? 1.04 : 1.12 });
      for (const f of D.det >= 1 ? [0, -16, -32] : [-10]) { M.color = shT(cc, f % 32 === 0 ? 0.9 : 0.96); M.shell(kit.capC, V.mul(kit.capR, 1.04), -180, 180, D.hs, th => line(th) + f - 12, th => line(th) + f, 1); }
      if (tp === 'pagri') {
        const kp = kit.capPoint(0, 22, 1.08);
        M.color = cc; M.sphere(kp, [0.045 * k, 0.04 * k, 0.035 * k], D.sph[0], D.sph[1]);
        M.color = X.gold; M.sphere(kit.capPoint(0, 42, 1.08), [0.018 * k, 0.02 * k, 0.008 * k], 5, 2);
        if (D.det >= 1) { M.color = lift(hat.gem || '#C02A3A', 0.3); M.sphere(V.add(kit.capPoint(0, 42, 1.08), [0, 0, 0.008 * k]), 0.008 * k, 4, 2); }
      } else {
        const gc = hc && tp !== 'pagri' ? lift(hat.color, 0.4) : X.gold;
        ring(gc, th => line(th) - 22, 9, 1.07);
        const plates = tp === 'mukuta' ? [[-40, 0.08, 0.05], [40, 0.08, 0.05], [0, 0.13, 0.07]] : (D.det >= 1 ? [[-70, 0.05, 0.028], [-35, 0.06, 0.03], [0, 0.075, 0.03], [35, 0.06, 0.03], [70, 0.05, 0.028]] : [[-35, 0.06, 0.03], [0, 0.075, 0.03], [35, 0.06, 0.03]]);
        M.color = gc;
        for (const [th, h, w] of plates) {
          const p0 = kit.capPoint(th, line(th) - 26, 1.07), t = th * D2R;
          M.push().translate(p0).rotate(-12, th, 0);
          M.triangleFacing([-w * k * 0.5, 0, 0], [w * k * 0.5, 0, 0], [0, h * k, -0.01 * k], [0, 0.2, 1]);
          M.triangleFacing([-w * k * 0.5, 0, -0.006], [0, h * k, -0.016 * k], [w * k * 0.5, 0, -0.006], [0, 0.2, -1]);
          M.pop();
          void t;
        }
        if (tp === 'mukuta' && D.det >= 1) { M.color = lift(hat.gem || '#C02A3A', 0.3); M.sphere(V.add(kit.capPoint(0, line(0) - 26, 1.08), [0, 0.045 * k, 0.01 * k]), 0.01 * k, 4, 2); }
      }
      res.top = line; res.back = true; res.ears = true;
    } else if (tp === 'kushcrown') {
      // 库施：贴头的深色小帽、窄金王带、额前一对昂首的眼镜蛇、脑后两条飘带
      const capC = lift(hat.color2 && lum(hat.color2) < 0.3 ? hat.color2 : '#2A2422', 0.16);
      const line = rimFn(56, 94, 120);
      kit.capShell(capC, 1.08, line, np);
      const gc = hc ? lift(hat.color, 0.4) : X.gold;
      ring(gc, th => line(th) - 8, 5, 1.04);
      M.color = gc;
      for (const s of [1, -1]) {
        const a = kit.capPoint(s * 10, line(0) - 10, 1.05);
        M.curve(a, V.add(a, [0, 0.02 * k, 0.012 * k]), V.add(a, [s * 0.004, 0.045 * k, 0.006 * k]), 0.006 * k, 0.004 * k, 2, 4);
        M.sphere(V.add(a, [s * 0.004, 0.05 * k, 0.008 * k]), [0.012 * k, 0.016 * k, 0.005 * k], 5, 2);
      }
      res.top = line; res.back = true;
    } else if (tp === 'bellcrown' || tp === 'tallcrown') {
      const cc = hc || X.gold, c = crownAt(-0.32);
      M.color = cc;
      M.push().translate(c);
      if (tp === 'bellcrown') M.lathe([[0.088 * k, 0], [0.09 * k, 0.03 * k], [0.084 * k, 0.06 * k], [0.07 * k, 0.09 * k], [0.05 * k, 0.115 * k], [0.026 * k, 0.135 * k], [0.012 * k, 0.15 * k], [0.008 * k, 0.19 * k], [0.001, 0.22 * k]], D.rnd + 2, false, false, false);
      else M.lathe([[0.08 * k, 0], [0.085 * k, 0.03 * k], [0.06 * k, 0.05 * k], [0.065 * k, 0.08 * k], [0.04 * k, 0.12 * k], [0.042 * k, 0.15 * k], [0.02 * k, 0.2 * k], [0.001, 0.26 * k]], D.rnd + 2, false, false, false);
      if (D.det >= 1) { M.color = X.goldDark; M.band([0, 0, 0], 0.092 * k, 0.092 * k, 0.02 * k, 0.035 * k, D.rnd + 2); }
      M.pop();
      res.top = rimFn(62, 82, 90); res.back = false;
    } else if (tp === 'headband' || tp === 'diadem' || tp === 'featherband' || tp === 'shaman') {
      const cc = tp === 'diadem' ? (hc || X.gold) : tp === 'shaman' ? C3('#E8DCC0') : (hc || C3(PALF.red));
      kit.capC = kit.headAt(0, 0.02, -0.01); kit.capR = kit.hv(1.09, 1.09, 1.07);
      const line = rimFn(52, 74, 92);
      ring(cc, line, tp === 'diadem' ? 9 : 10, 1.0);
      if (hat.gem || tp === 'diadem') { M.color = lift(hat.gem || '#2A6DB5', 0.3); M.sphere(kit.capPoint(0, line(0) - 5, 1.02), [0.013 * k, 0.015 * k, 0.007 * k], 5, 2); }
      if (tp === 'shaman') { M.color = C3(PALF.bronze); M.push().translate(kit.capPoint(0, line(0) - 8, 1.03)).rotate(80, 0, 0); M.cylinder([0, 0, 0], 0.04 * k, 0.036 * k, 0.008, D.rnd); M.pop(); }
      if (tp === 'featherband') { const p0 = kit.capPoint(70, line(70) - 5, 1.02); feather(kit, [p0, V.add(p0, [0.01 * k, 0.08 * k, -0.01 * k]), V.add(p0, [0.015 * k, 0.16 * k, -0.03 * k]), V.add(p0, [0.02 * k, 0.22 * k, -0.05 * k])], 0.03 * k, 0.012 * k, lift(hat.feather || '#F0ECE0', 0.3), false, 0.2); }
      if (D.det >= 1 && tp !== 'shaman') {
        const kp = kit.capPoint(180, line(180) - 5, 1.02);
        kit.bone('hair1');
        const v0 = M.vertexCount; M.color = tp === 'diadem' ? C3('#E8E0D0') : cc;
        for (const s of [1, -1]) M.curvedStrip(kp, V.add(kp, [s * 0.03 * k, -0.06 * k, -0.04 * k]), V.add(kp, [s * 0.05 * k, -0.15 * k, -0.06 * k]), 0.03 * k, 0.02 * k, [s * 0.3, 0, -1], 0.006, 2);
        M.windBy(v0, p => 0.45 * clamp01((kp[1] - p[1]) / (0.14 * k)));
        kit.bone('head');
      }
    } else if (tp === 'crown' || tp === 'pheasant' || tp === 'goldcrown') {
      const cc = hc || X.gold;
      if (tp === 'pheasant' || tp === 'goldcrown') {
        kit.capC = kit.headAt(0, 0.02, -0.01); kit.capR = kit.hv(1.1, 1.1, 1.08);
        const line = rimFn(44, 62, 76);
        ring(cc, line, 10, 1.0);
        M.color = X.goldLight; M.push().translate(kit.capPoint(0, line(0) - 6, 1.04)).rotate(70, 0, 0);
        M.cylinder([0, 0, 0], 0.03 * k, 0.026 * k, 0.012, D.rnd); M.pop();
        M.color = lift(hat.gem || '#C8302A', 0.3); M.sphere(kit.capPoint(0, line(0) - 6, 1.1), 0.01 * k, 4, 2);
        if (tp === 'pheasant') {
          // 雉翎：两根长尾羽，自冠前两侧先向上再向后外方扫开（S 形），黑白相间的羽节，随盔缨骨摆动
          for (const s of [1, -1]) {
            const p0 = kit.capPoint(s * 25, line(25) - 10, 1.05);
            const n = D.det >= 2 ? 9 : D.det >= 1 ? 6 : 4, pts = [];
            for (let i = 0; i <= n; i++) {
              const t = i / n, u = 1 - t;
              const c1 = V.add(p0, [s * 0.1 * k, 0.26 * k, 0.02 * k]), c2 = V.add(p0, [s * 0.34 * k, 0.58 * k, -0.2 * k]), e = V.add(p0, [s * 0.52 * k, 0.66 * k, -0.62 * k]);
              pts.push([0, 1, 2].map(q => u * u * u * p0[q] + 3 * u * u * t * c1[q] + 3 * u * t * t * c2[q] + t * t * t * e[q]));
            }
            feather(kit, pts, 0.022 * k, 0.006 * k, [C3('#F2EEE4'), C3('#3A2E2A'), C3('#E8D8B8')], true, 0.25);
          }
        } else {
          // 新罗金冠：三支「出」字形树枝金片
          M.color = cc;
          for (const th of D.det >= 1 ? [-50, 0, 50] : [0]) {
            const p0 = kit.capPoint(th, line(th) - 10, 1.05);
            M.push().translate(p0).rotate(-8, th, 0);
            M.box([0, 0.07 * k, 0], [0.012 * k, 0.14 * k, 0.004]);
            if (D.det >= 1) for (const y of [0.05, 0.1]) for (const s of [1, -1]) M.box([s * 0.022 * k, y * k, 0], [0.034 * k, 0.008 * k, 0.004]);
            M.pop();
          }
        }
        res.top = null;
      }
      res.crown = tp === 'crown';
    } else if (tp === 'feathers' || tp === 'chief') {
      kit.capC = kit.headAt(0, 0.02, -0.01); kit.capR = kit.hv(1.09, 1.09, 1.07);
      const line = rimFn(50, 70, 86);
      ring(hc || C3('#B8862A'), line, 10, 1.0);
      const n = tp === 'chief' ? (D.det >= 1 ? 9 : 5) : (D.det >= 1 ? 6 : 3);
      const cols = (hat.colors || ['#D8A020', '#C03028', '#ECE4D4']).map(c => lift(c, 0.3));
      for (let i = 0; i < n; i++) {
        const a = lerp(-75, 75, n > 1 ? i / (n - 1) : 0.5);
        const p0 = kit.capPoint(a, line(a) - 6, 1.04), d = V.norm(V.sub(p0, kit.capC));
        const L = (tp === 'chief' ? 0.3 : 0.2) * (1 - 0.3 * Math.abs(a) / 90) * k;
        feather(kit, [p0, V.add(p0, [d[0] * 0.04 * k, L * 0.5, d[2] * 0.04 * k - 0.02 * k]), V.add(p0, [d[0] * 0.09 * k, L, d[2] * 0.06 * k - 0.06 * k])], 0.03 * k, 0.012 * k, cols[i % cols.length], false, 0.25);
      }
    } else if (tp === 'jeolpung' || tp === 'pointcap' || tp === 'tallhat' || tp === 'phrygian' || tp === 'tiara' || tp === 'conical' || tp === 'spangen' || tp === 'felt' || tp === 'furhat') {
      const def = { felt: '#E0D4B8', furhat: '#5A4A3A', pointcap: '#E8DCC0', tallhat: '#E8DCC0', phrygian: '#9A2A24', tiara: '#2A2A5A', conical: '#8A909A', spangen: '#8A909A', jeolpung: '#C84A2A' }[tp];
      const cc = hc || lift(def, 0.22), metalH = tp === 'conical' || tp === 'spangen';
      const line = rimFn(tp === 'jeolpung' ? 50 : 62, 90, 104);
      kit.capShell(cc, metalH ? 1.16 : 1.14, line, np);
      const top = kit.capPoint(0, 0, 1);
      const ap = { felt: 0.02, furhat: 0.02, pointcap: 0.17, tallhat: 0.13, phrygian: 0.08, tiara: 0.11, conical: 0.12, spangen: 0.09, jeolpung: 0.09 }[tp] * k;
      M.color = cc;
      if (tp === 'tallhat' || tp === 'tiara') { M.push().translate(V.add(top, [0, -0.04 * k, 0])); M.lathe([[kit.capR[0] * 0.85, 0], [kit.capR[0] * 0.8, ap * 0.6], [kit.capR[0] * 0.55, ap + 0.04 * k], [0.001, ap + 0.05 * k]], D.rnd + 1, false, false, false); M.pop(); }
      else if (tp === 'phrygian') { M.curve(V.add(top, [0, -0.03 * k, -0.01 * k]), V.add(top, [0, 0.07 * k, -0.01 * k]), V.add(top, [0, 0.07 * k, 0.07 * k]), kit.capR[0] * 0.75, 0.02 * k, D.det >= 1 ? 3 : 2, D.rnd); }
      else if (tp === 'pointcap') { M.curve(V.add(top, [0, -0.04 * k, 0]), V.add(top, [0, 0.12 * k, -0.01 * k]), V.add(top, [0, 0.17 * k, -0.07 * k]), kit.capR[0] * 0.78, 0.008 * k, D.det >= 1 ? 3 : 2, D.rnd); }
      else if (ap > 0.04 * k) { M.push().translate(V.add(top, [0, -0.045 * k, 0])); M.lathe([[kit.capR[0] * 0.82, 0], [0.001, ap + 0.04 * k]], D.rnd + 1, false, false, false); M.pop(); }
      if (tp === 'felt' || tp === 'furhat') {
        M.color = tp === 'furhat' ? C3(PALF.fur) : shT(cc, 0.8);
        const keep = M.jitterAll; if (tp === 'furhat') M.jitterAll = true;
        M.shell(kit.capC, V.mul(kit.capR, tp === 'furhat' ? 1.1 : 1.05), -180, 180, D.hs, th => line(th) - 16, th => line(th) + 2, 1);
        M.jitterAll = keep;
      }
      if (tp === 'tallhat' || tp === 'tiara' || tp === 'conical' || tp === 'spangen') ring(X.gold, line, 6, 1.03);
      if (tp === 'spangen' && D.det >= 1) {
        M.color = X.gold;
        for (const th of [0, 90, 180, 270]) { const a = kit.capPoint(th, line(th) - 4, 1.03), b = V.add(top, [0, ap * 0.9, 0]); M.segment(a, b, 0.006 * k, 0.005 * k, 4); }
        M.color = cc; M.box(V.add(kit.capPoint(0, line(0) + 4, 1.06), [0, -0.035 * k, 0.004]), [0.014 * k, 0.07 * k, 0.008 * k]);
      }
      if (tp === 'jeolpung') for (const s of [1, -1]) { const p0 = kit.capPoint(s * 80, 40, 1.02); feather(kit, [p0, V.add(p0, [s * 0.02 * k, 0.1 * k, -0.01 * k]), V.add(p0, [s * 0.03 * k, 0.2 * k, -0.04 * k])], 0.025 * k, 0.01 * k, C3('#F4F2EE'), false, 0.2); }
      if (tp === 'felt' || tp === 'tiara' || tp === 'conical' || tp === 'spangen') {
        // 护耳 / 护颈垂片（毡 / 锁子）
        const c2 = tp === 'conical' || tp === 'spangen' ? C3('#7A808A') : cc;
        const yA = kit.capC[1] + kit.capR[1] * Math.cos(line(180) * D2R);
        M.panel([0, 0, kit.capC[2]], 75, 285, Math.max(3, D.pan + 2), [[kit.capR[0] * 1.0, yA + 0.01 * k], [kit.capR[0] * 1.14, yA - 0.1 * k]], kit.capR[2] / kit.capR[0], c2, D.det >= 1 ? X.lining : null, 0.006);
        res.ears = true; res.back = true;
      }
      res.top = line;
    } else if (tp === 'keffiyeh') {
      const cc = hc || C3('#ECE6DA');
      const line = rimFn(56, 98, 112);
      kit.capShell(cc, 1.12, line, np);
      const yA = kit.capC[1] + kit.capR[1] * Math.cos(100 * D2R);
      M.panel([0, 0, kit.capC[2]], 62, 298, Math.max(4, D.pan + 3), [[kit.capR[0] * 1.02, yA + 0.02 * k], [kit.capR[0] * 1.3, yA - 0.12 * k], [kit.capR[0] * 1.55, yA - 0.2 * k]], kit.capR[2] / kit.capR[0], cc, D.det >= 1 ? shT(cc, 0.8) : null, 0.006);
      ring(C3('#2A262A'), th => line(th) - 22, 6, 1.05);
      if (D.det >= 1) ring(C3('#2A262A'), th => line(th) - 30, 5, 1.05);
      res.top = line; res.back = true; res.ears = true;
    } else if (tp === 'jinze') {
      // 巾帻（试点关羽）：高裹露出额头、深色帻边、脑后一团结与两条短飘带、额前金托翠玉
      const cc = hc || X.cloth, band = shT(cc, 0.75);
      const line = th => { const a = Math.abs(th); if (a < 55) return lerp(48, 56, a / 55); if (a < 110) return lerp(56, 84, (a - 55) / 55); return lerp(84, 106, (a - 110) / 70); };
      kit.capShell(cc, 1.1, line, np);
      ring(band, line, 14, 1.03);
      const bun = kit.capPoint(180, 40, 1.02), bunR = kit.hv(0.46, 0.42, 0.44);
      M.color = cc; M.sphere(bun, bunR, D.det >= 1 ? 6 : 5, D.det >= 1 ? 3 : 2, false);
      if (D.det >= 1) {
        M.color = band; M.band(V.add(bun, kit.hv(0, -0.08, 0.04)), bunR[0] * 0.86, bunR[0] * 0.88, 0, 0.12 * kit.Ry, 7, bunR[2] / bunR[0]);
        kit.bone('hair1'); M.color = cc;
        const v0 = M.vertexCount;
        for (const s of [1, -1]) {
          const a = V.add(bun, kit.hv(s * 0.2, -0.24, -0.26)), m = V.add(a, [s * 0.04 * k, -0.06 * k, -0.03 * k]), t = V.add(a, [s * 0.09 * k, -0.12 * k, -0.026 * k]);
          M.strip(a, m, 0.055 * k, 0.036 * k, [s * 0.3, 0, -1], 0.009);
          M.strip(m, t, 0.036 * k, 0.016 * k, [s * 0.95, 0.1, -0.45], 0.009);
        }
        const ribTop = bun[1] - 0.24 * kit.Ry;
        M.windBy(v0, p => 0.5 * clamp01((ribTop - p[1]) / (0.12 * k)));
        kit.bone('head');
      }
      const brow = kit.capPoint(0, line(0) - 6, 1.06);
      M.color = X.goldLight; M.sphere(brow, [0.21 * R, 0.17 * R, 0.1 * R], D.det >= 1 ? 6 : 4, D.det >= 1 ? 3 : 2, false);
      if (D.det >= 1) { M.color = C3(PALF.jade); M.emission = 0.15; M.sphere(V.add(brow, [0, 0, 0.06 * R]), [0.13 * R, 0.1 * R, 0.06 * R], 5, 2, false); M.emission = 0; }
      res.top = line; res.back = false; res.wrapBun = true;
    } else if (tp === 'laurel') {
      kit.capC = kit.headAt(0, 0.02, -0.01); kit.capR = kit.hv(1.1, 1.1, 1.08);
      const n = D.det >= 1 ? 16 : 8;
      M.color = C3('#8AA850');
      for (let i = 0; i < n; i++) {
        const th = lerp(-150, 150, i / (n - 1)), p = kit.capPoint(th, lerp(52, 66, Math.abs(th) / 150), 1.03);
        M.color = i % 2 ? C3('#8AA850') : C3('#C8B850');
        M.push().translate(p).rotate(0, th, i % 2 ? 30 : -30);
        M.sphere([0, 0, 0], [0.008 * k, 0.018 * k, 0.006 * k], 4, 2);
        M.pop();
      }
    }
    kit.mark('headgear');
    return res;
  }

  // ----------------------------------------------------------- 头发 --
  function hairLP(kit, X, cov) {
    const M = kit.M, look = kit.look, hs = (look.hair && look.hair.style) || 'topknot', D = kit.D, k = kit.k, R = kit.R, V = kit.V;
    const hc = X.hair, fem = look.sex === 'f';
    const recede = (look.hair && look.hair.recede) || 0;
    kit.bone('head');
    if (hs === 'bald') {
      if (look.hair && look.hair.tuft) { M.color = hc; M.sphere(kit.headAt(0, 0.95, -0.35), kit.hv(0.18, 0.12, 0.18), 5, 2); }
      kit.mark('hair'); return;
    }
    const front = (hs === 'kunfa' ? 80 : 58) + recede * 14;
    const thick = { short: 1.06, bob: 1.09, curly: 1.11, bushy: 1.13, wild: 1.13, spiky: 1.08, kunfa: 1.05, romanf: 1.08, tightcurl: 1.065, ringlets: 1.1 }[hs] || 1.075;
    const back = { long: 140, wahair: 140, bob: 132, wild: 136, bushy: 132, ringlets: 132 }[hs] || 124;
    // 盖住头顶的冠帽：头发只在帽沿下露一圈（鬓角、耳上、后颈）
    const line = cov.top ? (th => Math.max(cov.top(th) + 4, 60)) : null;
    const hl = th => { const a = Math.abs(th); if (a < 55) return lerp(front, front + 10, a / 55); if (a < 110) return lerp(front + 10, 100, (a - 55) / 55); return lerp(100, back, (a - 110) / 70); };
    if (line) {
      // 帽下的头发：从帽沿内略上 到 发际线
      const lo = th => Math.max(line(th) - 14, 8);
      const hi = th => Math.max(hl(th), line(th) + 3);
      kit.hairCap(hc, front, 100, back, 1.065, hi, lo, D.det >= 1 ? 2 : 1);
    } else {
      kit.hairCap(hc, front, 100, back, thick, hl, null, D.det >= 2 ? 4 : D.det >= 1 ? 3 : 2);
    }
    // 鬓角：贴在脸侧的扁片
    if (D.det >= 1 && hs !== 'kunfa' && !fem) for (const s of [1, -1]) { M.color = hc; M.strip(kit.facePoint(s * 0.9, 0.34, -0.045), kit.facePoint(s * 0.88, -0.2, -0.035), 0.24 * R, 0.08 * R, [s, 0, 0.3], 0.008); }
    const bunAt = (p, r, tie) => {
      M.color = hc; M.sphere(p, r, D.sph[0], D.sph[1]);
      if (tie && D.det >= 1) { M.color = tie; M.band(V.add(p, [0, -r[1] * 0.5, 0]), r[0] * 0.86, r[0] * 0.92, 0, r[1] * 0.4, Math.max(5, D.sph[0]), r[2] / r[0]); }
    };
    const tie = look.hair && look.hair.tie ? lift(look.hair.tie, 0.25) : (look.kind === 'soldier' && look.culture === 'han' ? C3('#C2402E') : null);
    if (hs === 'topknot' || hs === 'highbun' || hs === 'mallet') {
      if (!(cov.top && !cov.bun && !cov.crown && hs === 'topknot')) {
        let p = hs === 'mallet' ? kit.headAt(0, 0.35, -1.0) : kit.headAt(0, hs === 'highbun' ? 1.18 : 1.02, -0.42);
        if (cov.bun) p = kit.headAt(0, 1.12, -0.38);
        const r = hs === 'mallet' ? kit.hv(0.5, 0.42, 0.46) : hs === 'highbun' ? kit.hv(0.4, 0.5, 0.4) : kit.hv(0.38, 0.32, 0.36);
        bunAt(p, r, cov.crown ? null : tie);
        if (cov.crown) {
          // 束发冠：包住发髻的金冠 + 横簪
          M.color = look.hat && look.hat.color ? lift(look.hat.color, 0.4) : X.gold;
          M.push().translate(p);
          M.lathe([[r[0] * 1.05, -r[1] * 0.7], [r[0] * 1.2, -r[1] * 0.2], [r[0] * 1.15, r[1] * 0.5], [r[0] * 0.9, r[1] * 0.9]], Math.max(6, D.rnd), false, true, false);
          M.pop();
          if (look.hat.gem && D.det >= 1) { M.color = lift(look.hat.gem, 0.3); M.sphere(V.add(p, [0, 0, r[2] * 1.2]), 0.012 * k, 4, 2); }
        }
        if (D.det >= 1 && (hs === 'topknot' || cov.crown)) { M.color = look.hair && look.hair.pin ? lift(look.hair.pin, 0.35) : X.gold; M.segment(V.add(p, [-r[0] * (cov.crown ? 2 : 1.6), 0, 0]), V.add(p, [r[0] * (cov.crown ? 2 : 1.6), 0, 0]), 0.005 * k, 0.005 * k, 4); }
      }
    } else if (hs === 'bun' || hs === 'romanf' || hs === 'twinbun') {
      if (hs === 'twinbun') for (const s of [1, -1]) bunAt(kit.headAt(s * 0.62, 0.85, -0.25), kit.hv(0.3, 0.3, 0.3), tie);
      else if (!cov.back) bunAt(kit.headAt(0, 0.3, -1.02), kit.hv(0.42, 0.36, 0.34), tie);
    } else if (hs === 'mizura') {
      // 美豆良：两侧耳旁绾成的环
      for (const s of [1, -1]) {
        const c = kit.headAt(s * 1.02, -0.3, -0.12);
        M.color = hc;
        M.curve(V.add(c, [0, 0.07 * k, 0]), V.add(c, [s * 0.06 * k, 0.0, 0.01]), V.add(c, [0, -0.07 * k, 0]), 0.016 * k, 0.014 * k, D.det >= 1 ? 3 : 2, 5);
        if (D.det >= 1) { M.color = C3('#E8DCC0'); M.band(V.add(c, [s * 0.025 * k, 0, 0]), 0.018 * k, 0.018 * k, -0.01 * k, 0.01 * k, 5); }
      }
    } else if (hs === 'suebian') {
      const c = kit.headAt(-0.72, 0.62, -0.45);
      M.color = hc; M.sphere(c, kit.hv(0.32, 0.36, 0.34), D.sph[0], D.sph[1]);
      if (D.det >= 1) M.curve(c, V.add(c, [-0.02 * k, -0.06 * k, -0.02 * k]), V.add(c, [-0.01 * k, -0.13 * k, -0.04 * k]), 0.018 * k, 0.006 * k, 2, 4);
    } else if (hs === 'spiky' || hs === 'wild' || hs === 'curly' || hs === 'tightcurl' || hs === 'bushy') {
      // 毛躁 / 卷发：发壳上一圈小团或尖簇
      if (!cov.top) {
        const n = D.det >= 2 ? 12 : D.det >= 1 ? 8 : 4;
        for (let i = 0; i < n; i++) {
          const th = (i / n) * 360 - 180 + 15, ph = 30 + (i % 3) * 22;
          const p = kit.hairPoint(th, ph, 1.0);
          M.color = kit.P.shade(hc, 0.92 + 0.12 * ((i * 7) % 3) / 2);
          if (hs === 'spiky' || hs === 'wild') M.spike(p, V.norm(V.sub(p, kit.hairC)), 0.035 * k, (hs === 'wild' ? 0.07 : 0.05) * k, 4);
          else M.sphere(p, (hs === 'tightcurl' ? 0.024 : 0.032) * k, 4, 2);
        }
      }
    }
    // 长发 / 披发 / 辫 / 髡发的鬓辫：后脑垂下（下部随头发骨摆动）
    if (hs === 'long' || hs === 'wahair' || hs === 'wild' || hs === 'bushy' || hs === 'bob' || hs === 'ringlets' || (fem && (hs === 'bun' || hs === 'topknot'))) {
      const len = ({ long: 0.3, wahair: 0.42, wild: 0.2, bushy: 0.14, bob: 0.08, ringlets: 0.16, bun: 0.06, topknot: 0.06 }[hs] || 0.1) * (fem ? 1.25 : 1) * k;
      if (!(cov.back && len < 0.18 * k)) {
        const v0 = M.vertexCount, y0 = kit.headCY - 0.1 * kit.Ry;
        const sp = D.det >= 1 ? 5 : 3, pts = [];
        M.color = hc;
        const prof = [[kit.Rx * 1.02, y0 + 0.05 * k], [kit.Rx * 1.12, y0 - len * 0.45], [kit.Rx * (hs === 'bob' ? 1.05 : 1.2), y0 - len]];
        M.panel([0, 0, kit.headC[2] - 0.02 * k], 105, 255, sp, prof, kit.Rz / kit.Rx * 1.05, hc, kit.P.shade(hc, 0.7), 0.012);
        void pts;
        M.boneBy(v0, p => (p[1] > y0 - 0.04 * k ? B.head : B.hair1));
        M.windBy(v0, p => 0.25 * clamp01((y0 - p[1]) / len));
        if (hs === 'ringlets' && D.det >= 1) for (let i = 0; i < 5; i++) { const th = lerp(120, 240, i / 4) * D2R; M.color = kit.P.shade(hc, 0.9); M.segment([Math.sin(th) * kit.Rx * 1.15, y0 - 0.02, kit.headC[2] + Math.cos(th) * kit.Rz * 1.1], [Math.sin(th) * kit.Rx * 1.2, y0 - len - 0.03 * k, kit.headC[2] + Math.cos(th) * kit.Rz * 1.15], 0.014 * k, 0.01 * k, 4); }
      }
    }
    if (hs === 'braids' || hs === 'koreanbraid' || hs === 'kunfa') {
      for (const s of hs === 'koreanbraid' ? [0] : [1, -1]) {
        const p0 = hs === 'kunfa' ? kit.headAt(s * 0.95, -0.05, 0.05) : hs === 'koreanbraid' ? kit.headAt(0, -0.2, -1.0) : kit.headAt(s * 0.75, -0.3, -0.75);
        const L = (hs === 'kunfa' ? 0.15 : 0.32) * k;
        const v0 = M.vertexCount; M.color = hc;
        const n = D.det >= 1 ? 4 : 1;
        let prev = p0;
        for (let i = 1; i <= n; i++) {
          const t = i / n, p = V.add(p0, [s * 0.02 * k * t, -L * t, (hs === 'kunfa' ? 0.01 : -0.03) * k * t]);
          kit.seg(prev, p, 0.016 * k * (1 - 0.3 * (t - 1 / n)), 0.016 * k * (1 - 0.3 * t), D.det >= 1 ? 5 : 4, D.det >= 1 && i === 1, D.det >= 1 || i === n);
          prev = p;
        }
        if (D.det >= 1) { M.color = tie || C3(PALF.red); M.sphere(prev, 0.012 * k, 4, 2); }
        M.boneBy(v0, p => (p[1] > p0[1] - 0.06 * k ? B.head : B.hair1));
        M.windBy(v0, p => 0.25 * clamp01((p0[1] - p[1]) / L));
      }
    }
    // 头饰：花（帽沿 / 发上）
    if ((look.acc || []).includes('flower') && D.det >= 1) { M.color = C3('#F2B8C6'); M.sphere(kit.headAt(0.85, 0.55, -0.1), [0.03 * k, 0.03 * k, 0.012 * k], 5, 2); M.color = C3('#F4D35E'); M.sphere(kit.headAt(0.9, 0.55, -0.08), 0.01 * k, 4, 2); }
    kit.mark('hair');
  }

  // ----------------------------------------------------------- 胡须 --
  function beardLP(kit, X) {
    const M = kit.M, look = kit.look, D = kit.D, k = kit.k, R = kit.R, V = kit.V, J = kit.J, P = kit.P;
    if (look.sex === 'f') return;
    const st = (look.beard && look.beard.style) || 'none', ms = (look.mus && look.mus.style) || 'none';
    const len = clamp((look.beard && look.beard.len) || 1, 0.6, 1.6);
    const bc = X.beard, top = P.shade(bc, 1.25);
    kit.bone('head');
    // 长髯（关羽等，试点原样）：椭球壳
    const shell = (th, ph0, ph1, c, puff) => { M.color = c; M.shell(kit.headAt(0, -0.33, 0.02), kit.hv(1.03 * (puff || 1), 0.86 * (puff || 1), 1.0 * (puff || 1)), -th, th, D.det >= 1 ? 8 : 5, ph0, ph1, D.det >= 1 ? 2 : 1); };
    // 贴脸的胡须壳：在头面上按角度 th（0 = 正前）与高度 y 取点、向外抬 lift，前缘停在下唇之下（嘴露出来），
    // 两侧沿腮升到鬓角；最底一行收进下巴下面（被脖子挡住）。lift 自上而下变厚 → 胡子有体积而不是一张面具。
    const hug = (o) => {
      // 低模分段少、弦会切进脸里：抬得更高
      const thM = o.th || 84, nt = D.det >= 2 ? 10 : D.det >= 1 ? 8 : 6, nr = D.det >= 2 ? 3 : D.det >= 1 ? 2 : 1, lk = D.det >= 1 ? 1 : 1.7;
      const yTop = th => lerp(o.front != null ? o.front : -0.8, o.side != null ? o.side : -0.08, clamp01((Math.abs(th) - 14) / 64));
      // 一行（低模）时底边停在下巴上（再往下会收到脖子轴心、整片藏进下巴里）
      const yBot = th => lerp(o.bot != null ? o.bot : nr > 1 ? -1.13 : -1.03, nr > 1 ? -0.95 : -0.9, clamp01((Math.abs(th) - 40) / 50));
      const rows = [];
      for (let i = 0; i <= nr; i++) {
        const u = i / nr, row = [];
        for (let j = 0; j <= nt; j++) {
          const th = lerp(-thM, thM, j / nt), edge = Math.min(j, nt - j) === 0 ? 0.35 : 1;
          row.push(kit.sidePoint(th, lerp(yTop(th), yBot(th), Math.pow(u, 0.85)), lk * lerp(o.l0, o.l1, u) * (i === 0 ? 1 : edge)));
        }
        rows.push(row);
      }
      const c0 = [0, kit.headCY - 0.5 * kit.Ry, kit.headC[2] - 0.2 * kit.Rz];
      for (let i = 0; i < nr; i++) for (let j = 0; j < nt; j++) {
        const a = rows[i][j], b = rows[i][j + 1], cc = rows[i + 1][j + 1], d = rows[i + 1][j];
        const m = V.mul(V.add(V.add(a, b), V.add(cc, d)), 0.25);
        M.quad(a, b, cc, d, V.sub(m, c0), P.shade(o.c, lerp(1.12, 0.86, (i + 0.5) / nr)));
      }
      return rows;
    };
    const chestFront = kit.chestR * kit.depthK + 0.045 * k;
    // 一绺须：五边的锥形放样，尖端前卷，上三分之一随头、其余随胸
    const lock = (root, tip, w0, seed, curl) => {
      const ctrl = [(root[0] + tip[0]) / 2, lerp(root[1], tip[1], 0.42), Math.max(root[2], tip[2]) + 0.035 * k];
      const bez = t => { const u = 1 - t; return [u * u * root[0] + 2 * u * t * ctrl[0] + t * t * tip[0], u * u * root[1] + 2 * u * t * ctrl[1] + t * t * tip[1], u * u * root[2] + 2 * u * t * ctrl[2] + t * t * tip[2] + 0.035 * k * t * t * t]; };
      const N = D.det >= 2 ? 4 : 2, rings = [], sides = D.det >= 1 ? 5 : 4;
      for (let i = 0; i <= N; i++) {
        const t = i / N, p = bez(t), q = bez(Math.min(1, t + 0.02)), pp = bez(Math.max(0, t - 0.02));
        const T = V.norm(V.sub(q, pp)), Xa = V.norm(V.cross(T, [0, 0, 1])), U = V.cross(Xa, T);
        const r = w0 * Math.pow(1 - t, 0.75) * (i === 0 ? 1 : 1 + 0.08 * Math.sin(seed + i * 1.7)) + (i === N ? 0.002 : 0);
        const ring = [];
        for (let j = 0; j < sides; j++) { const a = j * TAU / sides + PI / 2; ring.push(V.add(p, V.add(V.mul(Xa, Math.cos(a) * r), V.mul(U, Math.sin(a) * r * 0.6)))); }
        rings.push(ring);
      }
      const start = M.vertexCount;
      for (let i = 0; i < N; i++) {
        const tone = lerp(1, 0.74, i / Math.max(1, N - 1));
        M.loft([rings[i], rings[i + 1]], i === 0, i === N - 1, n => P.shade(n[1] > 0.3 || n[2] > 0.75 ? top : bc, tone));
      }
      if (curl && D.det >= 1) { M.color = bc; M.sphere(tip, 0.03 * k, 4, 2); }
      const split = root[1] - (root[1] - tip[1]) / 3;
      M.boneBy(start, p => (p[1] > split || tip[1] > J.neck[1] ? B.head : B.chest));
    };
    if (st === 'stubble') hug({ c: P.mix(X.skin, bc, 0.4), l0: 0.006, l1: 0.012, th: 78, front: -0.78, side: -0.3 });
    else if (st === 'short') hug({ c: bc, l0: 0.03, l1: 0.075, th: 82, front: -0.8, side: -0.12 });
    else if (st === 'goatee') lock(kit.headAt(0, -0.95, 0.6), kit.headAt(0, -1.35 - 0.15 * len, 0.62), 0.22 * R, 2);
    else if (st === 'full' || st === 'bristle' || st === 'curled' || st === 'braided') {
      hug({ c: bc, l0: 0.04, l1: 0.15 + 0.04 * len, th: 86, front: -0.8, side: -0.06 });
      // 下巴前垂下的一团（与壳重叠，不留缝）
      const chin = kit.facePoint(0, -1.0, 0.1);
      M.color = bc; M.sphere(V.add(chin, [0, -0.012 * k * len, 0.004 * k]), [0.5 * kit.Rx, (0.2 + 0.16 * len) * kit.Ry, 0.32 * kit.Rzu], D.sph[0], D.sph[1]);
      if (st === 'bristle') {
        // 虬髯（张飞）：自下颌向外下方伸出的尖簇
        const n = D.det >= 2 ? 9 : D.det >= 1 ? 6 : 3;
        for (let i = 0; i < n; i++) {
          const a = lerp(-80, 80, n > 1 ? i / (n - 1) : 0.5) * D2R;
          const p = kit.sidePoint(a / D2R, -0.72 - 0.3 * Math.cos(a), 0.12);
          const dir = V.norm([Math.sin(a) * 0.9, -0.8, Math.cos(a) * 0.6]);
          M.color = P.shade(bc, 0.9 + 0.2 * (i % 2));
          M.spike(p, dir, 0.035 * k, 0.07 * k * len, 4);
        }
      }
      const chinLow = V.add(chin, [0, -(0.2 + 0.16 * len) * kit.Ry * 0.7, 0]);
      if (st === 'braided') lock(chinLow, V.add(chinLow, [0, -0.11 * k * len, 0.03 * k]), 0.2 * R, 4);
      if (st === 'curled' && D.det >= 1) for (const s of [1, 0, -1]) { M.color = top; M.sphere(V.add(chinLow, [s * 0.32 * kit.Rx, 0.1 * kit.Ry * Math.abs(s), 0.012 * k]), 0.03 * k, 4, 2); }
    } else if (st === 'long' || st === 'flowing' || st === 'forked') {
      shell(66, 112, 160, bc);
      const tipY = st === 'flowing' ? J.chest[1] - 0.02 * k - (len - 1) * 0.08 * k : kit.neckY - 0.05 * k * len;
      const rootC = kit.headAt(0, -0.95, 0.62), tipC = [0, tipY, Math.max(chestFront, rootC[2])];
      if (st === 'forked') for (const s of [1, -1]) lock(kit.headAt(s * 0.18, -0.95, 0.6), [s * 0.04 * k, tipY + 0.04 * k, tipC[2]], 0.26 * R, 2 + s);
      else lock(rootC, tipC, (st === 'flowing' ? 0.4 : 0.32) * R, 1);
      if (st === 'flowing' && D.det >= 1) {
        // 美髯：两侧较短的两绺，左右张开 ±12°
        const dir = V.sub(tipC, rootC), L = V.len(dir);
        for (const s of [1, -1]) {
          const root = kit.headAt(s * 0.34, -0.87, 0.58), a = s * 12 * D2R, d = V.mul(V.norm(dir), L * 0.7);
          const tip = V.add(root, [d[0] * Math.cos(a) - d[1] * Math.sin(a), d[0] * Math.sin(a) + d[1] * Math.cos(a), d[2]]);
          tip[2] = Math.max(tip[2], chestFront + 0.01);
          lock(root, tip, 0.34 * R, 3 + s);
        }
      }
    }
    // 八字胡（tilt：thin / droop / thick / bristle / curl）
    const shellBeard = st === 'full' || st === 'bristle' || st === 'curled' || st === 'braided' || st === 'long' || st === 'flowing' || st === 'forked';
    if ((ms !== 'none' || st === 'flowing' || st === 'long' || st === 'full') && !(D.det === 0 && shellBeard)) {
      const m = ms === 'none' ? 'thin' : ms;
      kit.bone('head'); M.color = m === 'thin' ? bc : top;
      const r0 = (m === 'thick' || m === 'bristle' ? 0.11 : 0.085) * R, r1 = (m === 'curl' ? 0.04 : 0.03) * R;
      for (const s of [1, -1]) {
        const end = m === 'droop' ? V.add(kit.facePoint(s * 0.5, -1.0, 0.05), [0, -0.03 * k, 0]) : m === 'curl' ? kit.facePoint(s * 0.62, -0.5, 0.06) : kit.facePoint(s * 0.52, -0.78, 0.05);
        M.curve(kit.facePoint(s * 0.05, -0.56, 0.06), kit.facePoint(s * 0.42, -0.62, 0.07), end, r0, r1, D.det >= 2 ? 3 : 2, D.det >= 2 ? 4 : 3);
      }
    }
    kit.mark('beard');
  }

  // ----------------------------------------------------------- 饰物 --
  function accessoriesLP(kit, X) {
    const M = kit.M, look = kit.look, acc = look.acc || [], D = kit.D, k = kit.k, V = kit.V;
    if (!acc.length) return;
    kit.bone('head');
    if ((acc.includes('earring') || acc.includes('hoops')) && D.det >= 1) {
      M.color = X.gold;
      for (const s of [1, -1]) { const p = kit.headAt(s * 1.0, -0.55, -0.1); if (acc.includes('hoops')) { M.push().translate(p).rotate(0, 90, 0); M.torus([0, 0, 0], 0.016 * k, 0.004 * k, 6, 3); M.pop(); } else M.sphere(p, 0.008 * k, 4, 2); }
    }
    const beads = (n, col2, r) => {
      kit.bone('chest');
      for (let i = 0; i < n; i++) {
        const a = lerp(-60, 60, n > 1 ? i / (n - 1) : 0.5);
        const y = kit.neckY - 0.02 * k - 0.07 * k * Math.cos(a * D2R * 1.4);
        M.color = Array.isArray(col2[0]) ? col2[i % col2.length] : col2;
        M.sphere(kit.torsoPt(a, y, 0.012 * k + r), r, 4, 2);
      }
    };
    if (acc.includes('torc') || acc.includes('goldcollar')) {
      kit.bone('chest'); M.color = X.gold;
      M.push().translate(0, kit.neckY - 0.01 * k, -0.01).rotate(12, 0, 0).scale(1, 1, 0.86);
      M.torus([0, 0, 0], 0.1 * k, 0.012 * k, D.det >= 1 ? 10 : 6, 3);
      M.pop();
    }
    if (acc.includes('hara') && D.det >= 1) beads(7, [X.gold, C3('#C02A3A'), X.gold, C3('#2A8A6A')], 0.012 * k);
    if (acc.includes('magatama') && D.det >= 1) beads(7, [C3('#E8DCC0'), C3('#3AA060')], 0.012 * k);
    if (acc.includes('shells') && D.det >= 1) beads(7, [C3('#F4ECE0'), C3('#D8703A')], 0.012 * k);
    if (acc.includes('bones') && D.det >= 1) beads(5, C3('#E8E0C8'), 0.014 * k);
    if (acc.includes('garland') && D.det >= 1) beads(9, [lift(look.spec && look.spec.garland || '#F0A030', 0.4), C3('#F4D35E')], 0.015 * k);
    if (acc.includes('mirror') && D.det >= 1 && !look.L.mirror) {
      kit.bone('chest'); M.color = C3(PALF.bronze);
      M.push().translate(kit.torsoPt(0, kit.chestY + 0.03 * k, 0.02 * k)).rotate(90, 0, 0); M.cylinder([0, 0, 0], 0.045 * k, 0.04 * k, 0.01, D.rnd); M.pop();
    }
    kit.mark('acc');
  }

  // ======================================================== 兵器与盾 ==
  // 兵器局部坐标：原点 = 右手握点，杆 / 刃沿 +Y（尖端方向），刃口朝 +Z，厚度方向 X。
  const WEAPONS = {
    guandao: { name: '青龙偃月刀', style: 'pole' }, snake: { name: '丈八蛇矛', style: 'pole' }, halberd: { name: '方天画戟', style: 'pole' },
    swords: { name: '双股剑', style: 'dual' }, twinji: { name: '双铁戟', style: 'dual' }, bigblade: { name: '斩马大刀', style: 'pole' },
    fengzui: { name: '凤嘴刀', style: 'pole' }, huangbow: { name: '宝雕弓', style: 'bow' }, shortji: { name: '短戟', style: 'dual' },
    bawang: { name: '霸王枪', style: 'pole' }, yajiao: { name: '涯角枪', style: 'pole' }, hutou: { name: '虎头湛金枪', style: 'pole' },
    yitian: { name: '倚天剑', style: 'one' }, guding: { name: '古锭刀', style: 'one' }, fan: { name: '羽扇', style: 'fan' },
    bigaxe: { name: '开山大斧', style: 'pole' }, poleblade: { name: '大刀', style: 'pole' }, trident: { name: '三尖刀', style: 'pole' },
    spear: { name: '长枪', style: 'pole' }, pilum: { name: '重标枪', style: 'pole' }, lance: { name: '长矛', style: 'pole' },
    dao: { name: '环首刀', style: 'one' }, jian: { name: '长剑', style: 'one' }, gladius: { name: '短剑', style: 'one' }, spatha: { name: '长剑', style: 'one' },
    tachi: { name: '直刀', style: 'one' }, mace: { name: '骨朵', style: 'one' }, handaxe: { name: '战斧', style: 'one' }, axe: { name: '大斧', style: 'pole' },
    twindao: { name: '双刀', style: 'dual' }, bow: { name: '弓', style: 'bow' },
    khanda: { name: '坎达剑', style: 'one' }, longbow: { name: '长弓', style: 'bow' },
    staff: { name: '锡杖', style: 'pole' },
  };
  const DUAL_BASE = { swords: 'jian', twinji: 'ji1', shortji: 'sji1', twindao: 'dao' };

  // ======================================================== 兵器与盾（低多边形） ==
  // 兵器局部坐标：原点 = 右手握点，杆 / 刃沿 +Y（尖端方向），刃口朝 +Z，厚度方向 X（与 v3 的尺寸一致：单挑的判定距离按它校准）。
  // det：0（士兵 / 远景）、1（中）、2（单挑特写）。
  function wpnKit(M, det) {
    const sides = det >= 2 ? 6 : det >= 1 ? 5 : 4;
    const K = {
      M, det, sides,
      col(c) { M.color = Array.isArray(c) ? c : C3(c); return K; },
      shaft(y0, y1, r, c, taper) { K.col(c); M.push().translate(0, y0, 0); M.lathe([[r * (det === 0 ? 1.15 : 1), 0], [r * (1 - (taper || 0)) * (det === 0 ? 1.15 : 1), y1 - y0]], sides, false, det >= 1, det >= 1); M.pop(); },
      ring(y, r, h, c) { if (det === 0) return; K.col(c || PALF.gold); M.band([0, 0, 0], r, r, y - h / 2, y + h / 2, sides); },
      spike(y, len, r, c, down) { if (det === 0 && len < 0.06) return; K.col(c || PALF.steel); M.spike([0, y, 0], [0, down ? -1 : 1, 0], r, len, Math.max(4, sides - 1)); },
      tassel(y, len, r, c) {
        const v0 = M.vertexCount;
        K.col(c || PALF.tassel);
        M.push().translate(0, y, 0);
        if (det === 0) M.lathe([[0.012, 0.01], [r, -len * 0.45], [0.006, -len]].reverse(), 4, false, false, true);
        else M.lathe([[0.012, 0.01], [r * 0.7, -len * 0.18], [r, -len * 0.5], [r * 0.6, -len * 0.88], [0.006, -len]].reverse(), sides, false, true, true);
        M.pop();
        M.windBy(v0, p => 0.25 * clamp01((y - p[1]) / len));
      },
      // 刃：沿 +Y 的站点 [y, 刃口宽, 刃背宽, 半厚, z 偏移]；dbl：双刃（菱形截面）
      blade(st, dbl, c, back) {
        const rings = st.map(([y, we, wb, th, z]) => {
          const zz = z || 0;
          return dbl ? [[0, y, zz + we], [th, y, zz], [0, y, zz - wb], [-th, y, zz]] : [[th, y, zz - wb], [0, y + 0.004, zz + we], [-th, y, zz - wb]];
        });
        K.col(c || PALF.steel);
        M.loft(rings, true, true);
        if (back && det >= 1 && !dbl) {
          K.col(PALF.bladeBack);
          for (let i = 0; i < st.length - 2; i += 2) M.beam([0, st[i][0], (st[i][4] || 0) - st[i][2] - 0.003], [0, st[i + 2][0], (st[i + 2][4] || 0) - st[i + 2][2] - 0.003], 0.024, 0.016, [0, 0, -1]);
        }
      },
      dragon(y, c, s) {
        s = s || 1;
        K.col(c || '#56A878');
        M.sphere([0, y, 0.01 * s], [0.034 * s, 0.05 * s, 0.045 * s], det >= 1 ? 6 : 4, det >= 1 ? 3 : 2);
        M.segment([0, y + 0.005 * s, 0.035 * s], [0, y + 0.03 * s, 0.09 * s], 0.024 * s, 0.01 * s, 5);
        if (det >= 2) for (const sg of [1, -1]) M.curve([sg * 0.015 * s, y + 0.03 * s, -0.01 * s], [sg * 0.03 * s, y + 0.07 * s, -0.03 * s], [sg * 0.02 * s, y + 0.11 * s, -0.06 * s], 0.008 * s, 0.002, 2, 4);
      },
      arcBlade(cy, cz, R, a0, a1, wFn, th, c, n) {
        // 弧形刃（月牙 / 斧刃 / 戟枝）：沿圆弧放样，刃口朝外
        const rings = [];
        for (let i = 0; i <= n; i++) {
          const a = lerp(a0, a1, i / n), w = wFn(i / n);
          const ca = Math.cos(a), sa = Math.sin(a);
          const p = [0, cy + sa * R, cz + ca * R], o = [0, sa, ca];
          rings.push([[th, p[1] - o[1] * 0.012, p[2] - o[2] * 0.012], [0, p[1] + o[1] * w, p[2] + o[2] * w], [-th, p[1] - o[1] * 0.012, p[2] - o[2] * 0.012]]);
        }
        K.col(c || PALF.steel);
        M.loft(rings, true, true);
      },
    };
    return K;
  }
  function buildWeaponLP(M, id, det) {
    const def = WEAPONS[id] || WEAPONS.jian;
    const K = wpnKit(M, det);
    const keepJ = M.jitter; M.jitter = 0.035;
    let info = { id, name: def.name, style: def.style, len: 1, tip: [0, 1, 0], off: null, base: [0, 0.1, 0] };
    const base = DUAL_BASE[id] || id;
    switch (base) {
      case 'guandao': {
        // 青龙偃月刀：青色长杆、金箍、龙吞口、红缨、宽阔的偃月刃（刀背倒钩、蓝钢刀背）
        K.shaft(-0.9, 1.06, 0.021, '#2E5E44', 0.08);
        for (const y of [-0.82, 0.25, 0.95]) K.ring(y, 0.026, 0.05, PALF.gold);
        K.spike(-0.9, 0.12, 0.022, PALF.gold, true);
        K.tassel(1.04, 0.17, 0.046, PALF.tassel);
        K.dragon(1.1, '#56A878', 1);
        const st = [[1.12, 0.07, 0.02, 0.011, 0], [1.24, 0.14, 0.025, 0.011, 0], [1.38, 0.19, 0.03, 0.011, 0], [1.52, 0.2, 0.045, 0.011, 0], [1.64, 0.16, 0.07, 0.01, 0], [1.74, 0.09, 0.1, 0.008, 0], [1.81, -0.04, 0.14, 0.002, 0]];
        K.blade(det >= 1 ? st : [st[0], st[2], st[4], st[6]], false, '#E4EAEE', true);
        if (det >= 1) { K.col(PALF.bladeBack); M.spike([0, 1.34, -0.03], [0, 0.4, -1], 0.016, 0.09, 4); }
        info = Object.assign(info, { len: 1.8, tip: [0, 1.72, 0.06], base: [0, 1.15, 0], off: [0, 0.6, 0] });
        break;
      }
      case 'snake': {
        // 丈八蛇矛：深色长杆、金箍、红缨、蜿蜒的蛇形矛刃
        K.shaft(-1.0, 1.44, 0.019, '#3A3036', 0.05);
        K.spike(-1.0, 0.08, 0.018, PALF.gold, true);
        for (const y of [-0.9, 0.3, 1.36]) K.ring(y, 0.023, 0.04, PALF.gold);
        K.tassel(1.42, 0.16, 0.046, PALF.tassel);
        K.col(PALF.gold); M.cylinder([0, 1.42, 0], 0.024, 0.02, 0.05, K.sides);
        const n = det >= 2 ? 10 : det >= 1 ? 7 : 4, st = [];
        for (let i = 0; i <= n; i++) { const t = i / n; st.push([1.47 + t * 0.52, 0.042 * (1 - t * 0.9) + (i === n ? 0 : 0.002), 0.042 * (1 - t * 0.9), 0.007 * (1 - t * 0.5), 0.032 * Math.sin(t * PI * 3) * (1 - t * 0.6)]); }
        K.blade(st, true, PALF.steel);
        info = Object.assign(info, { len: 1.99, tip: [0, 1.95, 0], base: [0, 1.47, 0], off: [0, 0.65, 0] });
        break;
      }
      case 'halberd': {
        // 方天画戟：朱杆、金箍、矛尖 + 两侧月牙刃、红色彩带
        K.shaft(-0.95, 1.4, 0.02, '#7A2A24', 0.05);
        K.spike(-0.95, 0.09, 0.019, PALF.gold, true);
        for (const y of [-0.85, 0.2, 1.28]) K.ring(y, 0.024, 0.04, PALF.gold);
        K.col(PALF.gold); M.cylinder([0, 1.33, 0], 0.03, 0.024, 0.08, K.sides);
        K.blade([[1.41, 0.03, 0.03, 0.009], [1.52, 0.04, 0.04, 0.008], [1.66, 0.032, 0.032, 0.007], [1.86, 0, 0, 0.002]], true, PALF.steel);
        for (const sg of [1, -1]) {
          K.arcBlade(1.37, sg * 0.04, 0.12, sg > 0 ? -1.15 : PI + 1.15, sg > 0 ? 1.15 : PI - 1.15, t => 0.05 * (1 - Math.pow(2 * t - 1, 2)) + 0.004, 0.006, '#E8EEF2', det >= 1 ? 6 : 3);
          K.col(PALF.gold); M.segment([0, 1.37, 0], [0, 1.37, sg * 0.05], 0.012, 0.012, 4);
        }
        if (det >= 1) {
          const v0 = M.vertexCount; K.col(PALF.tassel);
          for (const sg of [1, -1]) M.curvedStrip([sg * 0.012, 1.33, 0], [sg * 0.03, 1.22, 0.02], [sg * 0.05, 1.0, -0.03], 0.035, 0.028, [1, 0, 0], 0.006, 3);
          M.windBy(v0, p => 0.4 * clamp01((1.33 - p[1]) / 0.33));
        }
        info = Object.assign(info, { len: 1.86, tip: [0, 1.82, 0], base: [0, 1.4, 0], off: [0, 0.62, 0] });
        break;
      }
      case 'bigblade': case 'fengzui': case 'poleblade': {
        const big = id === 'bigblade', feng = id === 'fengzui';
        const top = big ? 0.72 : 1.0;
        K.shaft(big ? -0.55 : -0.85, top + 0.02, big ? 0.024 : 0.02, big ? '#3A3230' : '#5A3E2A', 0.05);
        K.spike(big ? -0.55 : -0.85, 0.08, 0.02, PALF.steel, true);
        K.col(feng ? PALF.gold : PALF.bronze); M.cylinder([0, top - 0.02, 0], 0.03, 0.026, 0.06, K.sides);
        if (feng) K.dragon(top + 0.06, PALF.gold, 0.8);
        if (!big) K.tassel(top - 0.02, 0.13, 0.045, PALF.tassel);
        const L = big ? 0.78 : 0.62, W0 = big ? 0.13 : 0.08, W1 = big ? 0.2 : 0.12, curve = feng ? 0.12 : 0.04;
        const n = det >= 1 ? 6 : 3, st = [];
        for (let i = 0; i <= n; i++) { const t = i / n; st.push([top + 0.04 + t * L, lerp(W0, W1, Math.sin(t * PI * 0.8)) * (t > 0.85 ? Math.max(0.05, (1 - t) / 0.15) : 1), big ? 0.03 : 0.02, big ? 0.012 : 0.009, -curve * t * t]); }
        K.blade(st, false, big ? '#C8CCD2' : '#E4EAEE', true);
        info = Object.assign(info, { len: top + 0.04 + L, tip: [0, top + L, -curve * 0.8], base: [0, top + 0.1, 0], off: [0, big ? 0.45 : 0.55, 0] });
        break;
      }
      case 'spear': case 'bawang': case 'yajiao': case 'hutou': case 'lance': case 'pilum': case 'trident': {
        const lance = id === 'lance', pil = id === 'pilum';
        const top = lance ? 1.6 : pil ? 0.9 : 1.32;
        const sc = id === 'yajiao' ? '#E4E4EC' : id === 'hutou' ? '#D8C8A0' : id === 'bawang' ? '#6A2A24' : PALF.wood;
        K.shaft(lance ? -1.4 : pil ? -0.7 : -0.95, top + 0.02, lance ? 0.021 : 0.018, sc, 0.06);
        if (!pil) K.spike(lance ? -1.4 : -0.95, 0.07, 0.018, id === 'hutou' ? PALF.gold : PALF.steel, true);
        const fit = id === 'hutou' ? PALF.gold : id === 'yajiao' ? PALF.silver : id === 'bawang' ? PALF.gold : PALF.bronze;
        if (det >= 1 && !pil) for (const y of [-0.85, 0.3]) K.ring(y, 0.021, 0.03, fit);
        K.col(fit); M.cylinder([0, top - 0.02, 0], 0.024, 0.02, 0.05, K.sides, false, det >= 1, det >= 1);
        if (id === 'yajiao' || id === 'hutou') K.dragon(top + 0.04, id === 'hutou' ? PALF.gold : '#E8EAF0', 0.75);
        if (!lance && !pil) K.tassel(top - 0.01, id === 'spear' ? 0.1 : 0.15, id === 'spear' ? 0.036 : 0.046, id === 'yajiao' || id === 'hutou' ? '#F0F0F0' : PALF.tassel);
        const b0 = top + (id === 'yajiao' || id === 'hutou' ? 0.1 : 0.03);
        if (pil) {
          K.col(PALF.iron); M.segment([0, top, 0], [0, top + 0.45, 0], 0.007, 0.006, 4);
          K.blade([[top + 0.44, 0.014, 0.014, 0.009], [top + 0.5, 0, 0, 0.002]], true);
          info.len = top + 0.5;
        } else if (id === 'trident') {
          K.blade([[b0, 0.025, 0.025, 0.008], [b0 + 0.15, 0.03, 0.03, 0.007], [b0 + 0.3, 0, 0, 0.002]], true);
          for (const sg of [1, -1]) { K.col(PALF.steel); M.curve([0, b0, 0], [0, b0 + 0.06, sg * 0.08], [0, b0 + 0.2, sg * 0.07], 0.012, 0.003, det >= 1 ? 3 : 2, 4); }
          info.len = b0 + 0.3;
        } else {
          const L = lance ? 0.22 : id === 'bawang' ? 0.4 : id === 'spear' ? 0.3 : 0.36, Wd = lance ? 0.024 : id === 'bawang' ? 0.05 : 0.04;
          K.blade([[b0, Wd * 0.45, Wd * 0.45, 0.009], [b0 + L * 0.3, Wd, Wd, 0.008], [b0 + L * 0.7, Wd * 0.7, Wd * 0.7, 0.007], [b0 + L, 0, 0, 0.002]], true, id === 'hutou' ? '#F0D890' : PALF.steel);
          info.len = b0 + L;
        }
        info = Object.assign(info, { tip: [0, info.len - 0.04, 0], base: [0, b0, 0], off: [0, lance ? 0.7 : 0.6, 0] });
        break;
      }
      case 'staff': {
        // 锡杖：木杖、铜箍、杖头为桃形铜环套小环
        const top = 1.25;
        K.shaft(-0.95, top, 0.018, '#7A5A34', 0.1);
        K.ring(-0.9, 0.02, 0.04, PALF.bronze); K.ring(top - 0.02, 0.021, 0.05, PALF.bronze);
        K.col(PALF.bronze);
        M.push().translate(0, top + 0.1, 0).rotate(0, 90, 90).scale(1, 1, 1.45);
        M.torus([0, 0, 0], 0.075, 0.008, det >= 1 ? 12 : 8, 4);
        M.pop();
        if (det >= 1) for (const sx of [-1, 1]) for (const yy of [0.06, 0.15]) { M.push().translate(0, top + yy, sx * 0.075).rotate(0, 0, 90); M.torus([0, 0, 0], 0.02, 0.004, 6, 3); M.pop(); }
        info = Object.assign(info, { len: top + 0.22, tip: [0, top + 0.2, 0], base: [0, top - 0.3, 0], off: [0, 0.6, 0] });
        break;
      }
      case 'bigaxe': case 'axe': {
        const big = id === 'bigaxe', top = big ? 1.05 : 0.95;
        K.shaft(-0.6, top + 0.08, 0.021, '#4A3A2E', 0.04);
        K.spike(-0.6, 0.06, 0.02, PALF.steel, true);
        K.ring(top - 0.15, 0.026, 0.04, PALF.bronze);
        K.spike(top + 0.08, 0.14, 0.02, PALF.steel);
        // 斧刃：朝 +Z 的半月
        const R = big ? 0.2 : 0.14;
        K.arcBlade(top, 0.03 - R * 0.25 + 0.02, R * 0.75, -1.1, 1.1, t => (big ? 0.12 : 0.09) * Math.cos((t - 0.5) * 2) * 0.9, 0.012, '#D8DCE2', det >= 1 ? 6 : 3);
        K.col('#5A5E68'); M.box([0, top, 0.02], [0.03, R * 0.9, 0.06]);
        info = Object.assign(info, { len: top + 0.22, tip: [0, top, 0.2], base: [0, top - 0.1, 0], off: [0, 0.45, 0] });
        break;
      }
      case 'ji1': case 'sji1': {
        const short = id === 'shortji', top = short ? 0.45 : 0.55;
        K.shaft(-0.25, top + 0.02, 0.018, short ? '#6A2A1A' : '#3A3236');
        K.spike(-0.25, 0.05, 0.018, PALF.steel, true);
        K.col(short ? PALF.gold : PALF.iron); M.cylinder([0, top - 0.02, 0], 0.024, 0.02, 0.04, K.sides);
        K.blade([[top + 0.03, 0.022, 0.022, 0.009], [top + 0.1, 0.032, 0.032, 0.008], [top + (short ? 0.22 : 0.26), 0, 0, 0.002]], true, short ? '#E4EAEE' : '#B8BCC4');
        K.arcBlade(top - 0.02, 0.025, short ? 0.085 : 0.11, -1.2, 1.0, t => 0.045 * (1 - Math.pow(Math.abs(t * 2 - 1.1), 2)) + 0.004, 0.008, short ? '#E4EAEE' : '#B8BCC4', det >= 1 ? 5 : 3);
        K.col(PALF.iron); M.spike([0, top - 0.04, 0], [0, 0, -1], 0.01, 0.07, 4);
        if (short) K.tassel(top - 0.01, 0.09, 0.03, PALF.tassel);
        info = Object.assign(info, { len: top + 0.26, tip: [0, top + 0.22, 0], base: [0, top, 0] });
        break;
      }
      case 'jian': case 'yitian': case 'gladius': case 'spatha': case 'dao': case 'guding': case 'tachi': {
        const curved = id === 'dao' || id === 'guding', glad = id === 'gladius', ring = id === 'dao' || id === 'guding' || id === 'tachi';
        const L = glad ? 0.5 : id === 'spatha' ? 0.78 : id === 'yitian' ? 0.86 : 0.76;
        const gripC = glad ? '#D8C8A0' : id === 'yitian' ? '#7A2A24' : '#3A2E26';
        K.col(gripC); M.segment([0, -0.11, 0], [0, 0.07, 0], 0.016, 0.015, K.sides);
        const fit = id === 'yitian' || id === 'guding' ? PALF.gold : glad || id === 'spatha' ? PALF.bronze : '#C8B07A';
        K.col(fit);
        if (ring && det >= 1) { M.push().translate(0, -0.145, 0).rotate(0, 90, 90); M.torus([0, 0, 0], 0.028, 0.006, 8, 3); M.pop(); }
        else M.sphere([0, -0.125, 0], [0.024, 0.02, 0.024], det >= 1 ? 6 : 4, 2);
        // 护手
        if (glad || id === 'spatha') { K.col(glad ? '#D8C8A0' : '#6A4A30'); M.sphere([0, 0.075, 0], [0.026, 0.016, 0.042], det >= 1 ? 6 : 4, 2); }
        else if (ring) { M.cylinder([0, 0.064, 0], 0.036, 0.036, 0.012, det >= 1 ? 8 : 5); }
        else { M.box([0, 0.075, 0], [0.024, 0.02, 0.12]); if (det >= 2) for (const sg of [1, -1]) M.sphere([0, 0.075, sg * 0.064], 0.012, 4, 2); }
        const w0 = glad ? 0.032 : curved ? 0.024 : 0.022;
        const n = det >= 1 ? 5 : 2, st = [];
        for (let i = 0; i <= n; i++) {
          const t = i / n;
          const wt = glad ? (t < 0.75 ? w0 * (1 - 0.15 * t) : w0 * (1 - t) / 0.25 * 0.85) : (t < 0.86 ? w0 * (1 - 0.15 * t) : w0 * 0.87 * Math.max(0.03, (1 - t) / 0.14));
          st.push([0.08 + t * L, curved ? wt * 1.4 : wt, curved ? wt * 0.35 : wt, curved ? 0.008 : 0.007, curved ? -0.045 * t * t : 0]);
        }
        K.blade(st, !curved && id !== 'tachi', id === 'yitian' ? '#EEF2F6' : PALF.steel, curved);
        if ((id === 'jian' || id === 'yitian') && det >= 1) K.tassel(-0.14, 0.12, 0.024, PALF.tassel);
        info = Object.assign(info, { len: 0.08 + L, tip: [0, 0.06 + L, curved ? -0.03 : 0], base: [0, 0.12, 0] });
        break;
      }
      case 'khanda': {
        // 天竺坎达剑：直身双刃、向尖端渐宽、钝圆头；圆盘护手、护指弯条、盘形柄头
        const L = 0.84;
        K.col('#3A2418'); M.segment([0, -0.1, 0], [0, 0.06, 0], 0.015, 0.014, K.sides);
        K.col(PALF.gold);
        M.cylinder([0, -0.128, 0], 0.036, 0.03, 0.016, det >= 1 ? 8 : 5);
        M.cylinder([0, 0.058, 0], 0.034, 0.034, 0.014, det >= 1 ? 8 : 5);
        if (det >= 1) M.curve([0, 0.068, 0.04], [0, -0.02, 0.1], [0, -0.112, 0.032], 0.005, 0.005, 3, 4);
        const n = det >= 1 ? 6 : 3, st = [];
        for (let i = 0; i <= n; i++) { const t = i / n, wt = t < 0.86 ? 0.019 + 0.012 * t : 0.029 * Math.sqrt(Math.max(0, 1 - Math.pow((t - 0.86) / 0.14, 2))); st.push([0.08 + t * L, Math.max(0.003, wt), Math.max(0.003, wt), 0.006]); }
        K.blade(st, true);
        info = Object.assign(info, { len: 0.08 + L, tip: [0, 0.08 + L, 0], base: [0, 0.12, 0] });
        break;
      }
      case 'mace': {
        K.shaft(-0.14, 0.5, 0.018, '#3A2418');
        K.ring(0.49, 0.024, 0.03, PALF.bronze);
        K.col('#8A8E98');
        M.push().translate(0, 0.6, 0);
        M.lathe([[0.02, -0.12], [0.045, -0.08], [0.055, -0.02], [0.05, 0.04], [0.02, 0.09], [0.001, 0.1]], det >= 1 ? 8 : 5, false, false, false);
        if (det >= 1) { K.col('#A8ACB6'); for (let i = 0; i < 6; i++) { const a = i * PI / 3; M.box([Math.sin(a) * 0.05, 0, Math.cos(a) * 0.05], [0.014 + Math.abs(Math.sin(a)) * 0.0, 0.11, 0.014]); } }
        M.pop();
        info = Object.assign(info, { len: 0.7, tip: [0, 0.62, 0], base: [0, 0.5, 0] });
        break;
      }
      case 'handaxe': {
        K.shaft(-0.12, 0.52, 0.017, '#6A4A2A');
        K.col('#D0D4D8');
        M.loft([[[0.008, 0.38, 0.0], [0, 0.4, 0.13], [-0.008, 0.38, 0.0]], [[0.008, 0.5, 0.0], [0, 0.54, 0.14], [-0.008, 0.5, 0.0]]].map(r => r), true, true);
        M.box([0, 0.46, 0.0], [0.03, 0.1, 0.04]);
        info = Object.assign(info, { len: 0.54, tip: [0, 0.47, 0.12], base: [0, 0.42, 0] });
        break;
      }
      case 'fan': {
        // 羽扇：金柄、扇形白羽（羽根略暗）
        K.col('#C8A050'); M.segment([0, -0.08, 0], [0, 0.08, 0], 0.012, 0.012, 5);
        K.col(PALF.gold); M.sphere([0, 0.1, 0], 0.02, 5, 2);
        const n = det >= 1 ? 9 : 5, r1 = 0.3;
        for (let i = 0; i < n; i++) {
          const a0 = (i / n - 0.5) * PI * 0.62, a1 = ((i + 1) / n - 0.5) * PI * 0.62;
          const c0 = [0, 0.1, 0], p0 = [Math.sin(a0) * 0.008, 0.1 + Math.cos(a0) * r1, Math.sin(a0) * r1], p1 = [Math.sin(a1) * 0.008, 0.1 + Math.cos(a1) * r1, Math.sin(a1) * r1];
          M.color = i % 2 ? C3('#F4F2EE') : C3('#E8E4DC');
          M.triangleFacing(c0, p0, p1, [1, 0, 0]); M.triangleFacing(c0, p1, p0, [-1, 0, 0]);
        }
        info = Object.assign(info, { len: 0.4, tip: [0, 0.38, 0], base: [0, 0.12, 0] });
        break;
      }
      case 'bow': case 'huangbow': case 'longbow': {
        // 反曲弓：沿 ±Y 两臂，弓背朝 +Z，弦在 −Z 一侧；黄忠宝雕弓金饰；长弓竹色
        const gold = id === 'huangbow', lb = id === 'longbow';
        const half = gold ? 0.62 : lb ? 0.74 : 0.55, n = det >= 1 ? 8 : 4, pts = [];
        for (let i = 0; i <= n; i++) { const t = i / n * 2 - 1, a = Math.abs(t); pts.push([0, t * half, lb ? 0.07 * (1 - a * a) : 0.06 * (1 - a * a) - 0.06 * Math.pow(a, 6) + (a > 0.85 ? -0.02 * (a - 0.85) / 0.15 : 0)]); }
        K.col(gold ? '#C89A48' : lb ? '#A8884A' : '#6A4020');
        for (let i = 0; i < n; i++) { const t0 = Math.abs(i / n * 2 - 1), t1 = Math.abs((i + 1) / n * 2 - 1); M.segment(pts[i], pts[i + 1], 0.013 - 0.007 * t0, 0.013 - 0.007 * t1, 4); }
        K.col('#3A2418'); M.segment([0, -0.06, 0.055], [0, 0.06, 0.055], 0.017, 0.017, 5);
        if (gold && det >= 1) { K.col(PALF.goldLight); for (const s of [1, -1]) M.sphere(pts[s > 0 ? n : 0], 0.014, 4, 2); }
        K.col('#E8E0D0'); M.segment(pts[0], pts[n], 0.002, 0.002, 3);
        info = Object.assign(info, { len: half, tip: [0, half, 0], base: [0, 0, 0], string: [0, 0, pts[0][2]], nockTop: pts[n], nockBot: pts[0] });
        break;
      }
      default: M.jitter = keepJ; return buildWeaponLP(M, 'jian', det);
    }
    M.jitter = keepJ;
    return info;
  }

  // 盾：局部 +Z 为盾面朝外，+Y 沿前臂（朝手）方向；原点在前臂处。face = 盾面色（势力色），rim = 边
  function buildShieldLP(M, id, det, X) {
    const keepJ = M.jitter; M.jitter = 0.04;
    const team = X ? X.team : tcol([0.55, 0.2, 0.18], 1), trim = X ? X.trim : C3(PALF.gold);
    const sides = det >= 2 ? 12 : det >= 1 ? 10 : 7;
    let info = { r: 0.32 };
    const disc = (R, cz, face, rim, boss, sy, rings) => {
      M.push().translate(0, 0, cz).rotate(90, 0, 0).scale(1, 1, sy || 1);
      M.color = face;
      M.lathe([[R, -0.018], [R * 1.02, 0], [R * 0.95, 0.012], [R * 0.5, 0.03], [0.001, 0.04]], sides, false, true, false, null, 180 / sides);
      if (rim) { M.color = rim; M.band([0, 0, 0], R * 1.03, R * 1.03, -0.02, 0.006, sides); }
      if (rings && det >= 1) { M.color = rings; M.band([0, 0, 0], R * 0.66, R * 0.62, 0.02, 0.028, sides); }
      if (boss) { M.color = boss; M.sphere([0, 0.04, 0], [R * 0.2, 0.03, R * 0.2], det >= 1 ? 6 : 4, 2); }
      M.pop();
    };
    if (id === 'scutum') {
      // 罗马长方弧形盾：势力色盾面、金边、钢盾心与金色饰条
      const W0 = 0.62, H0 = 1.02, Rc = 0.5, a = W0 / Rc / 2 / D2R;
      M.color = team;
      M.panel([0, 0.02, 0.07 - Rc], -a, a, det >= 1 ? 4 : 2, [[Rc, H0 / 2], [Rc, -H0 / 2]], 1, team, C3('#6A4A30'), 0.02);
      if (det >= 1) { M.color = trim; for (const y of [H0 / 2 - 0.01, -H0 / 2 + 0.01]) M.panel([0, 0.02, 0.07 - Rc], -a, a, 4, [[Rc + 0.006, y + 0.015], [Rc + 0.006, y - 0.015]], 1, trim, null); M.panel([0, 0.02, 0.07 - Rc], -3, 3, 1, [[Rc + 0.005, H0 / 2], [Rc + 0.005, -H0 / 2]], 1, shT(trim, 0.9), null); }
      M.color = C3('#C8CCD2'); M.push().translate(0, 0.02, 0.08).rotate(90, 0, 0); M.lathe([[0.07, -0.004], [0.06, 0.012], [0.03, 0.03], [0.001, 0.036]], det >= 1 ? 8 : 5, false, true, false); M.pop();
      info = { r: 0.5 };
    } else if (id === 'wa' || id === 'pai' || id === 'long') {
      const W0 = id === 'wa' ? 0.4 : id === 'long' ? 0.4 : 0.46, H0 = id === 'wa' ? 0.92 : id === 'long' ? 0.98 : 0.76;
      const face = id === 'wa' ? C3('#8A3A2A') : team, back = C3('#5A3A24');
      const cols = det >= 1 ? 3 : 2;
      M.color = face;
      // 竖向略弯的木盾（宽向弯 6 厘米）；天竺长盾顶边圆拱
      const P0 = (s, t) => { const e = 1 - Math.pow(2 * s - 1, 2); return [(s - 0.5) * W0, (t - 0.5) * H0 + (id === 'long' ? Math.pow(t, 4) * 0.11 * e : 0), 0.06 * e]; };
      for (let i = 0; i < cols; i++) for (let j = 0; j < 2; j++) {
        const s0 = i / cols, s1 = (i + 1) / cols, t0 = j / 2, t1 = (j + 1) / 2;
        M.color = face;
        M.quad(P0(s0, t0), P0(s1, t0), P0(s1, t1), P0(s0, t1), [0, 0, 1]);
        M.color = back;
        M.quad(V0(P0(s0, t0)), V0(P0(s1, t0)), V0(P0(s1, t1)), V0(P0(s0, t1)), [0, 0, -1]);
      }
      // 边框
      M.color = id === 'wa' ? C3('#2A2426') : trim;
      const edge = [[0, 0], [1, 0], [1, 1], [0, 1]];
      for (let i = 0; i < 4; i++) { const a = P0(...edge[i]), b = P0(...edge[(i + 1) % 4]); M.strip(a, b, 0.03, 0.03, [0, 0, 1], 0.03); }
      if (det >= 1) {
        M.color = id === 'wa' ? C3('#E8D8B0') : shT(face, 1.25);
        if (id === 'pai') { M.push().translate(0, 0.0, 0.07).rotate(90, 0, 0); M.cylinder([0, 0, 0], 0.11, 0.1, 0.012, sides); M.pop(); M.color = C3(PALF.goldLight); M.push().translate(0, 0, 0.085).rotate(90, 0, 0); M.cylinder([0, 0, 0], 0.045, 0.035, 0.012, 7); M.pop(); }
        else if (id === 'wa') { for (const y of [-0.22, 0, 0.22]) M.strip([-W0 * 0.42, y, 0.066], [W0 * 0.42, y, 0.066], 0.03, 0.03, [0, 0, 1], 0.006); }
        else { M.color = C3(PALF.gold); M.sphere([0, 0.04, 0.09], [0.05, 0.05, 0.02], 6, 2); }
      }
      info = { r: H0 / 2 };
    } else {
      // 圆盾 / 椭圆盾 / 藤牌 / 皮盾（阿克苏姆、库施的长皮盾）
      const oval = id === 'oval' || id === 'hide', rat = id === 'rattan';
      const R0 = oval ? 0.26 : rat ? 0.33 : 0.32;
      const face = rat ? C3(PALF.rattan) : id === 'hide' ? C3('#A07A4E') : team;
      disc(R0, 0.06, face, rat ? C3(PALF.rattanDark) : id === 'hide' ? C3('#6A4A2A') : trim, rat ? null : C3(id === 'hide' ? '#5A3A20' : '#C8CCD2'), id === 'hide' ? 2.1 : oval ? 1.8 : 1, rat ? C3(PALF.rattanDark) : id === 'hide' ? null : shT(face, 1.3));
      if (id === 'hide' && det >= 1) { M.color = C3('#6A4A2A'); M.segment([0, -R0 * 1.9, 0.105], [0, R0 * 1.9, 0.105], 0.012, 0.012, 4); }
      info = { r: oval ? R0 * 1.8 : R0 };
    }
    M.jitter = keepJ;
    return info;
  }
  function V0(p) { return [p[0], p[1], p[2] - 0.02]; }

  // ======================================================== 战马 ==
  const HBONES = ['root', 'body', 'chest', 'neck1', 'neck2', 'head', 'tail1', 'tail2',
    'upFL', 'loFL', 'ftFL', 'upFR', 'loFR', 'ftFR', 'upHL', 'loHL', 'ftHL', 'upHR', 'loHR', 'ftHR', 'saddle'];
  const HPARENT = { root: null, body: 'root', chest: 'body', neck1: 'chest', neck2: 'neck1', head: 'neck2', tail1: 'body', tail2: 'tail1',
    upFL: 'chest', loFL: 'upFL', ftFL: 'loFL', upFR: 'chest', loFR: 'upFR', ftFR: 'loFR', upHL: 'body', loHL: 'upHL', ftHL: 'loHL', upHR: 'body', loHR: 'upHR', ftHR: 'loHR', saddle: 'body' };
  const HB = {};
  HBONES.forEach((n, i) => { HB[n] = i; });
  function horseJoints(k) {
    const J = {
      root: [0, 0, 0], body: [0, 1.15, -0.1], chest: [0, 1.18, 0.38], neck1: [0, 1.36, 0.5], neck2: [0, 1.62, 0.74], head: [0, 1.86, 0.92],
      tail1: [0, 1.3, -0.8], tail2: [0, 0.95, -0.9], saddle: [0, 1.47, -0.02],
    };
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R';
      J['upF' + S] = [0.15 * s, 1.02, 0.42]; J['loF' + S] = [0.15 * s, 0.55, 0.45]; J['ftF' + S] = [0.15 * s, 0.2, 0.46];
      J['upH' + S] = [0.17 * s, 0.98, -0.5]; J['loH' + S] = [0.17 * s, 0.58, -0.7]; J['ftH' + S] = [0.16 * s, 0.2, -0.64];
    }
    for (const n in J) J[n] = J[n].map(v => v * k);
    return J;
  }

  // ======================================================== 战马（低多边形） ==
  // 按 js/lp-pilot.js 试点的战马（Lanternvale CreatureKit 的敦实）：深胸低腹、上粗下细的腿（膝 / 飞节 / 球节隆起、外撇的蹄）、
  // 圆头大眼（高光）、竖耳、整片鬃毛（下缘分绺、两色、随风）、一体的马尾；势力色马衣与鞍、胸前红缨。具装（scale）：
  // 颈、胸、身、臀披鳞甲排（两色交替）+ 势力色镶边；名将的马（general）加面帘与红缨。刚性蒙皮在战马骨架上。
  const HLOD = {
    high: { n: 12, sides: 10, neck: 8, leg: 7, legLo: 6, hoof: 7, mane: 6, det: 2 },
    mid: { n: 9, sides: 8, neck: 7, leg: 6, legLo: 5, hoof: 6, mane: 4, det: 1 },
    low: { n: 7, sides: 7, neck: 6, leg: 5, legLo: 4, hoof: 5, mane: 3, det: 0 },
  };
  const HCOATS = [
    ['#A85A36', '#6B3A26', '#4A2A1E'], ['#8A4A2A', '#3A2620', '#2A1E1A'], ['#4A3E3C', '#2E2628', '#262022'],
    ['#DCD8D0', '#B4B0A8', '#ECE8E0'], ['#B08A5A', '#5A4430', '#3A2A1A'], ['#A8A49C', '#6A6660', '#4A4644'],
  ];
  const HNAMED = { '赤兔': ['#B03A24', '#6A2014', '#4A1410'], '的卢': ['#E4E0D8', '#B8B4AC', '#4A4644'], '绝影': ['#3A3236', '#262022', '#1E1A1C'], '爪黄飞电': ['#E8E0C8', '#C8B890', '#C8A050'], '白龙驹': ['#F0EEE8', '#D8D4CC', '#E0DCD4'] };
  function buildHorseLP(opts, mode) {
    const L = LPX(), P = L.Paint, V = L.V;
    const D = HLOD[opts.lod] || HLOD.mid;
    const FB = FigBuilder();
    const M = new FB(77 + (opts.seed | 0));
    M.jitter = 0.05;
    const bone = n => { M.bone = HB[n]; };
    // 皮带 / 白鼻梁：特写（det 2）用有厚度的带；中、远景用一片朝外的四边形（2 个三角形，不描线）
    const strap = (a, b, w0, w1, facing, thick) => {
      if (D.det >= 2) { M.strip(a, b, w0, w1, facing, thick); return; }
      const axis = V.sub(b, a); let side = V.cross(axis, facing);
      if (V.dot(side, side) < 1e-10) return;
      side = V.norm(side);
      const n = V.norm(V.cross(side, axis)), lift = V.mul(V.dot(n, facing) < 0 ? V.neg(n) : n, (thick || 0.01) * 0.5);
      const keepInk = M.ink; M.ink = 0;
      M.quad(V.add(V.madd(a, side, -w0 / 2), lift), V.add(V.madd(a, side, w0 / 2), lift), V.add(V.madd(b, side, w1 / 2), lift), V.add(V.madd(b, side, -w1 / 2), lift), facing);
      M.ink = keepInk;
    };
    const named = HNAMED[opts.name];
    let coatS = named || HCOATS[(opts.seed || 0) % HCOATS.length];
    if (opts.coat) coatS = [opts.coat, shade(opts.coat, -0.35), opts.mane || shade(opts.coat, -0.55)];
    const coat = C3(coatS[0]), points = C3(coatS[1]), mane = hairTone(coatS[2]), mane2 = P.shade(mane, 1.2);
    const coatDark = P.shade(coat, 0.84), belly = P.mix(coat, C3('#E8C9A0'), 0.2), markC = C3('#EFE2C6');
    const team = mode === 'crowd' ? [1, 1, 1] : teamTone(opts.color || '#8a2a24');
    const cloth = tcol(team, 1), clothDark = tcol(team, 0.78), trim = C3('#EBDDB0');
    const tasselC = C3(opts.plume ? opts.plume : PALF.tassel), gold = C3(PALF.gold), saddleC = C3('#5A3426');
    const armor = opts.armor || 'cloth';
    const sockHind = (opts.seed | 0) % 3 === 1 || opts.name === '的卢', blaze = (opts.seed | 0) % 2 === 0 || !!named;
    // ---- 躯干：一颗分面的豆形，后半随 body、前半随 chest（重叠）；背线在鞍下，腹部低垂圆润
    const z0 = -0.98, z1 = 0.76;
    const section = z => {
      const s = clamp01((z - z0) / (z1 - z0)), u = s * 2 - 1;
      const e = Math.pow(Math.max(0, 1 - Math.pow(Math.abs(u), 2.4)), 1 / 2.4);
      const withers = Math.exp(-Math.pow((s - 0.74) / 0.12, 2)), croup = Math.exp(-Math.pow((s - 0.2) / 0.12, 2));
      const cy = lerp(1.1, 1.14, sstep(0.2, 0.8, s)) + 0.03 * withers + 0.025 * croup;
      const w = 0.345 * e * lerp(1.0, 0.95, s);
      return { cy, w, top: 0.34 * e * (1 + 0.1 * withers + 0.06 * croup), bottom: 0.44 * e * (1 - 0.05 * Math.exp(-Math.pow((s - 0.5) / 0.2, 2))) };
    };
    const n = D.n, zs = [];
    for (let i = 0; i <= n; i++) zs.push(lerp(z0, z1, 0.5 - 0.5 * Math.cos(PI * i / n)));
    const mid = Math.round(n / 2), sides = D.sides;
    const piece = (b, from, to) => {
      bone(b);
      const rings = [];
      for (let i = from; i <= to; i++) {
        const sc = section(zs[i]);
        let kk = 1;
        if (b === 'body' && i > mid) kk = 1 - 0.3 * sstep(0, 2, i - mid);
        if (b === 'chest' && i < mid) kk = 1 - 0.3 * sstep(0, 2, mid - i);
        const ring = [];
        for (let j = 0; j < sides; j++) { const a = j * TAU / sides, c = Math.cos(a); ring.push([Math.sin(a) * sc.w * kk, sc.cy + c * (c >= 0 ? sc.top : sc.bottom) * kk, zs[i]]); }
        rings.push(ring);
      }
      const keepJ = M.jitter; M.jitter = 0.03;
      M.loft(rings, from === 0, to === n, nrm => (nrm[1] < -0.6 ? belly : nrm[1] > 0.75 ? P.shade(coat, 1.04) : coat));
      M.jitter = keepJ;
    };
    piece('body', 0, Math.min(n, mid + 2));
    piece('chest', Math.max(0, mid - 2), n);
    // ---- 颈：粗、拱，胸前深
    const neckPts = [[0, 1.26, 0.52], [0, 1.46, 0.64], [0, 1.65, 0.77], [0, 1.81, 0.87], [0, 1.9, 0.91]];
    const neckW = [0.17, 0.145, 0.12, 0.1, 0.09], neckH = [0.31, 0.24, 0.18, 0.14, 0.12];
    const frame = (pts, i) => { const t = V.norm(V.sub(pts[Math.min(pts.length - 1, i + 1)], pts[Math.max(0, i - 1)])); return V.norm(V.cross([1, 0, 0], t)); };
    const ns = D.neck;
    const neckRings = neckPts.map((c, i) => { const up = frame(neckPts, i), ring = []; for (let j = 0; j < ns; j++) { const a = j * TAU / ns; ring.push(V.add(V.add(c, [Math.sin(a) * neckW[i], 0, 0]), V.mul(up, Math.cos(a) * neckH[i] * (Math.cos(a) > 0 ? 1 : 0.85)))); } return ring; });
    bone('neck1'); M.color = coat; M.loft(neckRings.slice(0, 3), true, false);
    bone('neck2'); M.color = coat; M.loft(neckRings.slice(1, 5), false, false);
    // ---- 头：宽圆的颅骨与下颚、渐细的脸、奶白口鼻（鼻孔）、白鼻梁；侧向的大眼（高光）、竖耳
    bone('head');
    const HJ = [0, 1.86, 0.92];
    M.push().translate(HJ).scale(1.15).translate(V.neg(HJ));
    const skull = [0, 1.87, 0.97];
    M.color = coat;
    M.sphere(skull, [0.118, 0.125, 0.14], D.det >= 2 ? 9 : D.det >= 1 ? 8 : 7, D.det >= 2 ? 5 : 4, true);
    if (D.det >= 1) M.sphere([0, 1.95, 0.9], [0.09, 0.07, 0.1], D.det >= 2 ? 7 : 6, D.det >= 2 ? 4 : 3, true);
    const faceAxis = [[0, 1.86, 1.04], [0, 1.77, 1.14], [0, 1.67, 1.23], [0, 1.6, 1.29]];
    const faceW = [0.1, 0.088, 0.078, 0.072], faceH = [0.105, 0.09, 0.08, 0.075], fs = D.det >= 1 ? 8 : 6;
    const faceRings = faceAxis.map((c, i) => { const up = frame(faceAxis, i), ring = []; for (let j = 0; j < fs; j++) { const a = j * TAU / fs; ring.push(V.add(V.add(c, [Math.sin(a) * faceW[i], 0, 0]), V.mul(up, Math.cos(a) * faceH[i]))); } return ring; });
    M.color = coat;
    M.loft(faceRings, false, false, nrm => (nrm[1] < -0.55 ? coatDark : coat));
    const muzzle = C3('#C9A88C');
    M.color = P.mix(muzzle, coat, 0.25);
    M.sphere([0, 1.565, 1.315], [0.085, 0.078, 0.088], D.det >= 2 ? 8 : D.det >= 1 ? 7 : 6, D.det >= 2 ? 4 : 3, true);
    if (D.det >= 1) {
      M.color = C3('#3A241C');
      for (const s of [1, -1]) { M.push().translate(s * 0.042, 1.585, 1.392).rotate(-20, s * 22, 0); M.sphere([0, 0, 0], [0.018, 0.024, 0.008], 5, 2, false); M.pop(); }
      M.color = P.shade(muzzle, 0.7); M.box([0, 1.512, 1.37], [0.07, 0.008, 0.02], null, 'tfrl');
    }
    if (blaze) {
      const bl = faceRings.map((r, i) => V.add(r[Math.round(fs / 2)], V.mul(frame(faceAxis, i), -0.001)));
      M.color = markC;
      for (let i = 0; i < bl.length - 1; i++) strap(bl[i], bl[i + 1], lerp(0.045, 0.034, i / 3), lerp(0.045, 0.034, (i + 1) / 3), V.neg(frame(faceAxis, i)), 0.004);
    }
    // 眼：深色圆片、虹膜、白色高光
    const keepJ = M.jitter; M.jitter = 0;
    for (const s of [1, -1]) {
      M.push().translate(s * 0.098, 1.885, 1.05).rotate(-8, s * 48, 0);
      M.ink = 0.5; M.color = C3('#1E1612'); M.sphere([0, 0, 0], [0.036, 0.038, 0.016], D.det >= 2 ? 7 : 5, D.det >= 2 ? 3 : 2, false);
      if (D.det >= 2) { M.color = C3('#4A2E22'); M.sphere([0, -0.008, 0.008], [0.024, 0.022, 0.012], 6, 2, false); }
      M.emission = 0.55; M.ink = 0; M.color = [1, 1, 0.96]; if (D.det >= 1) M.sphere([s * 0.01, 0.014, 0.015], 0.009, 4, 2, false);
      M.emission = 0; M.ink = 1;
      M.pop();
    }
    M.jitter = keepJ;
    for (const s of [1, -1]) {
      M.aim([s * 0.065, 2.0, 0.9], [s * 0.28, 1, 0.12]);
      M.push().scale(1, 1, 0.6);
      M.color = coat; M.cone([0, 0, 0], 0.045, 0.145, 4);
      if (D.det >= 1) { M.color = coatDark; M.cone([0, 0.01, 0.012], 0.028, 0.1, 4); }
      M.pop(); M.pop();
    }
    // 额发
    let ws = M.vertexCount;
    M.color = mane;
    M.curvedStrip([0, 2.02, 0.88], [0.015, 2.0, 0.98], [0.03, 1.93, 1.03], 0.075, 0.035, [0, 0.6, 1], 0.03, D.det >= 1 ? 2 : 1);
    M.windBy(ws, p => 0.35 * clamp01((2.02 - p[1]) / 0.09));
    // 耳后的小红缨（名将：面帘 + 大缨）
    if (opts.general || armor === 'scale' || opts.chamfron) {
      const pc = opts.metal ? lift(opts.metal, 0.35) : C3(PALF.bronze);
      M.color = pc;
      const a = faceRings[0][Math.round(fs / 2)], b = faceRings[3][Math.round(fs / 2)];
      strap(V.add(a, [0, 0.03, 0.01]), V.add(b, [0, 0.0, 0.012]), 0.11, 0.07, [0, 0.6, 1], 0.012);
      if (D.det >= 1) { M.color = gold; M.sphere(V.add(V.lerp(a, b, 0.25), [0, 0.03, 0.03]), 0.022, 5, 2); }
    }
    M.color = gold; M.sphere([0, 2.02, 0.84], 0.02, 5, 2, false);
    ws = M.vertexCount;
    M.color = tasselC;
    const big = opts.general ? 1.6 : 1;
    M.push().translate(0, 2.02, 0.84).rotate(-24, 0, 0);
    M.lathe([[0.008, 0], [0.026 * big, 0.026 * big], [0.028 * big, 0.055 * big], [0.011, 0.09 * big]], D.det >= 1 ? 6 : 4, false, true, true);
    M.pop();
    M.windBy(ws, p => 0.3 * clamp01((p[1] - 2.02) / 0.1));
    // 络头：鼻革、颊革、额带、金色圆饰
    if (D.det >= 1) {
      M.color = saddleC;
      M.push().translate(0, 1.66, 1.225).rotate(-50, 0, 0); M.band([0, 0, 0], 0.088, 0.088, -0.018, 0.018, D.det >= 2 ? 8 : 6, 1.15); M.pop();
      for (const s of [1, -1]) {
        M.color = saddleC;
        strap([s * 0.11, 1.95, 0.87], [s * 0.086, 1.68, 1.2], 0.024, 0.024, [s, 0, 0], 0.012);
        strap([s * 0.11, 1.95, 0.87], [0, 1.99, 0.95], 0.024, 0.024, [0, 0.5, 1], 0.012);
        if (D.det >= 2) { M.color = gold; M.sphere([s * 0.104, 1.76, 1.1], 0.02, 5, 2, false); }
      }
    }
    M.pop();
    // ---- 鬃：颈上一道鬃脊（菱形截面，顶上深色）+ 左侧整片下缘分绺的鬃帘（两色）+ 右侧短鬃；按位置分在 neck1 / neck2，随风
    const crestAt = t => {
      const f = t * 4, i0 = Math.min(3, Math.floor(f)), u = f - i0;
      const c = V.lerp(neckPts[i0], neckPts[i0 + 1], u), up = V.neg(frame(neckPts, Math.min(4, Math.round(f))));
      return { p: V.add(c, V.mul(up, lerp(neckH[i0], neckH[i0 + 1], u) * 0.9)), up };
    };
    const zSplit = lerp(neckPts[0][2], neckPts[4][2], 0.48), crestYAt = z => crestAt(clamp01((z - neckPts[0][2]) / (neckPts[4][2] - neckPts[0][2]))).p[1];
    const ms = M.vertexCount;
    bone('neck1');
    const curtain = (sd, locks, lenK, inner) => {
      const cols = locks * 2, top = [], midO = [], bot = [], inn = [];
      for (let j = 0; j <= cols; j++) {
        const t = lerp(0.04, 0.97, j / cols), { p } = crestAt(t);
        const len = lerp(0.34, 0.2, t) * lenK * (j % 2 ? 1 : 0.55) * (j === 0 || j === cols ? 0.6 : 1);
        const tp = V.add(p, [sd * (0.045 + 0.012 * M.random01()), -0.02 - 0.015 * M.random01(), 0]);
        top.push(tp); midO.push(V.add(tp, [sd * (0.06 + 0.02 * (j % 2)), -len * 0.45, -0.01]));
        bot.push(V.add(tp, [sd * (0.085 + 0.02 * (j % 2)), -len, -0.03 - 0.015 * (j % 2)])); inn.push(V.add(tp, [-sd * 0.03, -len * 0.25, 0]));
      }
      for (let j = 0; j < cols; j++) {
        const cc = (j >> 1) % 2 ? mane2 : mane, out = [sd, 0.2, 0];
        M.color = cc;
        M.quad(top[j], top[j + 1], midO[j + 1], midO[j], out, P.shade(cc, 1 + (M.random01() - 0.5) * 0.06));
        M.quad(midO[j], midO[j + 1], bot[j + 1], bot[j], out, P.shade(cc, 0.94 + (M.random01() - 0.5) * 0.06));
        if (inner) { M.quad(top[j], top[j + 1], inn[j + 1], inn[j], [-sd, 0, 0], P.shade(mane, 0.8)); M.quad(inn[j], inn[j + 1], bot[j + 1], bot[j], [-sd, -0.3, 0], P.shade(mane, 0.75)); }
      }
    };
    curtain(1, D.mane, 1, D.det >= 1);
    curtain(-1, Math.max(2, D.mane - 1), 0.42, false);
    {
      const rings = [], nr = D.det >= 1 ? 6 : 4;
      for (let i = 0; i <= nr; i++) {
        const t = i / nr, { p, up } = crestAt(t), w = lerp(0.075, 0.06, t) * (i === 0 || i === nr ? 0.6 : 1);
        const h = 0.075 * (i === 0 ? 0.5 : 1) + 0.012 * Math.sin(i * 2.1);
        rings.push([V.add(V.add(p, [w, 0, 0]), V.mul(up, -0.025)), V.add(p, V.mul(up, h)), V.add(V.add(p, [-w, 0, 0]), V.mul(up, -0.025)), V.add(p, V.mul(up, -0.07))]);
      }
      M.loft(rings, true, true, nrm => (nrm[1] > 0.55 ? mane : mane2));
    }
    M.boneBy(ms, q => (q[2] < zSplit ? HB.neck1 : HB.neck2));
    M.windBy(ms, q => 0.45 * clamp01((crestYAt(q[2]) - q[1]) / 0.3));
    // ---- 腿：上粗下细的车削，膝 / 飞节 / 球节隆起，下段深色；后腿白袜（按种子）
    const leg = (fh, S) => {
      const s = S === 'L' ? 1 : -1, front = fh === 'F', x0 = (front ? 0.15 : 0.17) * s;
      const lo = front ? [x0, 0.55, 0.45] : [x0, 0.58, -0.7], ft = front ? [x0, 0.2, 0.46] : [0.16 * s, 0.2, -0.64];
      const sock = !front && sockHind, low = sock ? markC : points;
      bone('up' + fh + S); M.color = coat;
      if (front) { M.segment([x0, 1.0, 0.43], [x0, 0.8, 0.44], 0.105, 0.1, D.leg, false, false); M.segment([x0, 0.8, 0.44], V.add(lo, [0, 0.03, 0]), 0.1, 0.066, D.leg, false, false); }
      else {
        M.segment([x0, 0.98, -0.56], lo, 0.115, 0.066, D.leg, false, false);
        if (D.det >= 1) M.sphere(V.add(V.lerp([x0, 0.98, -0.56], lo, 0.4), [s * 0.01, 0, -0.045]), [0.07, 0.12, 0.075], D.leg, D.det >= 2 ? 3 : 2, true);
      }
      bone('lo' + fh + S); M.color = front ? coat : coatDark;
      if (D.det >= 1) M.sphere(lo, front ? [0.068, 0.074, 0.07] : [0.07, 0.08, 0.075], D.legLo, D.det >= 2 ? 3 : 2, false);
      if (!front && D.det >= 2) { M.color = coatDark; M.sphere(V.add(lo, [0, 0.02, -0.05]), [0.04, 0.05, 0.04], 5, 2, false); }
      M.color = points;
      M.segment(V.add(lo, [0, -0.03, 0]), V.add(ft, [0, 0.03, 0]), 0.05, 0.045, D.legLo, false, false);
      bone('ft' + fh + S); M.color = low;
      if (D.det >= 1) M.sphere(ft, [0.06, 0.062, 0.066], D.legLo, D.det >= 2 ? 3 : 2, false);
      M.segment(ft, [ft[0], 0.105, ft[2] + 0.04], 0.052, 0.056, D.legLo, false, false);
      M.color = C3('#3A2A22');
      M.push().translate(ft[0], 0, ft[2] + 0.045);
      M.lathe([[0.078, 0], [0.074, 0.03], [0.058, 0.115]], D.hoof, false, D.det >= 1, true);
      M.pop();
    };
    for (const fh of ['F', 'H']) for (const S of ['L', 'R']) leg(fh, S);
    // ---- 尾：短尾根 + 三绺（远景一绺）五边形放样，散开，尖端错落，随风
    ws = M.vertexCount;
    bone('tail1');
    const dockRing = (c, w, d, k5) => { const r = [], nn = k5 || 5; for (let j = 0; j < nn; j++) { const a = j * TAU / nn; r.push([c[0] + Math.sin(a) * w, c[1], c[2] + Math.cos(a) * d]); } return r; };
    M.color = mane;
    M.loft([dockRing([0, 1.38, -0.86], 0.06, 0.06), dockRing([0, 1.24, -1.0], 0.085, 0.07)], true, false);
    const lockCols = [mane2, mane, mane2];
    (D.det >= 1 ? [-1, 0, 1] : [0]).forEach((f, li) => {
      const a = f * 9 * D2R, len = 0.74 + (f === 0 ? 0.06 : f > 0 ? -0.06 : 0);
      const pt = t => [Math.sin(a) * len * t + f * 0.025 * Math.sin(PI * t), 1.25 - len * t * Math.cos(a), -1.01 - 0.11 * t - 0.05 * Math.sin(PI * t)];
      const wk = D.det >= 1 ? 1 : 1.5;
      const ts = [0, 0.3, 0.62, 1], ws_ = [0.062, 0.08, 0.064, 0.014], ds = [0.06, 0.078, 0.062, 0.012];
      M.loft(ts.map((t, i) => dockRing(pt(t), ws_[i] * wk, ds[i] * wk, D.det >= 1 ? 5 : 4)), false, true, nrm => P.shade(lockCols[li] || mane, nrm[0] * f > 0.3 ? 1.1 : 1));
    });
    M.boneBy(ws, q => (q[1] > 1.06 ? HB.tail1 : HB.tail2));
    M.windBy(ws, q => 0.6 * clamp01((1.2 - q[1]) / 0.7));
    // ---- 马具：势力色马衣（米色镶边、金钉）、鞍、肚带、胸带与红缨；具装：鳞甲排
    bone('body');
    const clothPt = (z, deg, off) => { const sc = section(z), a = deg * D2R, c = Math.cos(a); return [Math.sin(a) * (sc.w + off), sc.cy + c * (c >= 0 ? sc.top : sc.bottom) + off * Math.sign(c), z]; };
    if (armor === 'scale') {
      // 具装：身侧、臀、颈下的鳞甲排（两色交替），势力色下缘
      const rowsZ = D.det >= 1 ? [-0.86, -0.62, -0.38, -0.14, 0.1, 0.34, 0.56] : [-0.86, -0.38, 0.1, 0.56];
      const metal = opts.metal ? lift(opts.metal, 0.35) : C3('#8A909A'), metalD = P.shade(metal, 0.8);
      const angs = D.det >= 1 ? [0, 30, 60, 90, 112] : [0, 50, 100];
      for (const s of [1, -1]) for (let i = 0; i < rowsZ.length - 1; i++) for (let kk = 0; kk < angs.length - 1; kk++) {
        const off = 0.03, a = clothPt(rowsZ[i], s * angs[kk], off), b = clothPt(rowsZ[i], s * angs[kk + 1], off), c = clothPt(rowsZ[i + 1], s * angs[kk + 1], off), d = clothPt(rowsZ[i + 1], s * angs[kk], off);
        const zc = (rowsZ[i] + rowsZ[i + 1]) / 2, out = V.sub(V.mul(V.add(V.add(a, b), V.add(c, d)), 0.25), [0, section(zc).cy, zc]);
        const hem = kk === angs.length - 2;
        M.color = hem ? cloth : (kk % 2 ? metal : metalD);
        M.quad(a, b, c, d, out, hem ? cloth : P.shade(kk % 2 ? metal : metalD, 1 + (M.random01() - 0.5) * 0.08));
        if (hem && D.det >= 1) { const inn = V.mul(V.norm(out), -0.014); M.quad(V.add(a, inn), V.add(b, inn), V.add(c, inn), V.add(d, inn), V.neg(out), clothDark); }
      }
      // 颈甲：颈下两侧的甲帘
      bone('neck1');
      for (const s of [1, -1]) {
        M.color = metal;
        const a = V.add(neckPts[0], [s * neckW[0] * 1.05, 0.05, 0.0]), b = V.add(neckPts[2], [s * neckW[2] * 1.15, 0.0, 0.02]);
        M.strip(a, b, 0.2, 0.14, [s, -0.2, 0.2], 0.012);
      }
      bone('body');
    } else if (armor !== 'none') {
      const cz = D.det >= 1 ? [-0.44, -0.24, -0.04, 0.16, 0.34] : [-0.44, -0.04, 0.34], angs = D.det >= 1 ? [0, 26, 52, 78, 100, 112] : [0, 56, 112];
      for (const s of [1, -1]) for (let i = 0; i < cz.length - 1; i++) for (let kk = 0; kk < angs.length - 1; kk++) {
        const hem = kk === angs.length - 2, off = 0.028;
        const a = clothPt(cz[i], s * angs[kk], off), b = clothPt(cz[i], s * angs[kk + 1], off), c = clothPt(cz[i + 1], s * angs[kk + 1], off), d = clothPt(cz[i + 1], s * angs[kk], off);
        const zc = (cz[i] + cz[i + 1]) / 2, out = V.sub(V.mul(V.add(V.add(a, b), V.add(c, d)), 0.25), [0, section(zc).cy, zc]);
        const cc = hem ? trim : (i === 0 || i === cz.length - 2) && kk > 0 ? clothDark : cloth;
        M.color = cc;
        M.quad(a, b, c, d, out, cc.t ? tcol(team, (cc === cloth ? 1 : 0.78) * (1 + (M.random01() - 0.5) * 0.06)) : P.shade(cc, 1 + (M.random01() - 0.5) * 0.06));
        if (kk >= angs.length - 3) { const inn = V.mul(V.norm(out), -0.014); M.color = clothDark; M.quad(V.add(a, inn), V.add(b, inn), V.add(c, inn), V.add(d, inn), V.neg(out), clothDark); }
      }
      if (D.det >= 1) for (const s of [1, -1]) for (const z of [cz[0], cz[cz.length - 1]]) for (let kk = 0; kk < angs.length - 1; kk++) {
        const a = clothPt(z, s * angs[kk], 0.028), b = clothPt(z, s * angs[kk + 1], 0.028);
        const inn = V.mul(V.norm([a[0], a[1] - section(z).cy, 0]), -0.014);
        M.color = trim; M.quad(a, b, V.add(b, inn), V.add(a, inn), [0, 0, Math.sign(z)], trim);
      }
      if (D.det >= 2) { M.color = gold; for (const s of [1, -1]) for (const z of [-0.34, -0.14, 0.06, 0.26]) M.sphere(clothPt(z, s * 106, 0.04), 0.024, 4, 2, false); }
    }
    // 鞍：鞍座、前后鞍桥（金边）
    const sy = section(-0.05).cy + section(-0.05).top;
    M.color = saddleC;
    M.push().translate(0, sy - 0.02, -0.05);
    M.taperedBox([0, 0, 0], [0.34, 0.08, 0.5], 0.15, 0.08);
    M.color = P.shade(saddleC, 1.15);
    M.push().translate(0, 0.02, 0.24).rotate(-18, 0, 0); M.taperedBox([0, 0, 0], [0.3, 0.11, 0.09], 0.45, 0.2); M.pop();
    M.push().translate(0, 0.02, -0.25).rotate(20, 0, 0); M.taperedBox([0, 0, 0], [0.36, 0.13, 0.09], 0.4, 0.2); M.pop();
    if (D.det >= 1) { M.color = gold; M.box([0, 0.125, 0.255], [0.16, 0.022, 0.05]); M.box([0, 0.145, -0.275], [0.21, 0.022, 0.05]); }
    M.pop();
    if (D.det >= 1) {
      M.color = saddleC;
      const sc = section(0.12), gp = a => { a *= D2R; return [Math.sin(a) * (sc.w + 0.012), sc.cy + Math.cos(a) * (sc.bottom + 0.012), 0.12]; };
      for (const s of [1, -1]) for (let i = 0; i < 3; i++) { const a0 = s * lerp(108, 180, i / 3), a1 = s * lerp(108, 180, (i + 1) / 3); strap(gp(a0), gp(a1), 0.06, 0.06, V.sub(gp((a0 + a1) / 2), [0, sc.cy, 0.12]), 0.01); }
      // 镫
      for (const s of [1, -1]) { M.color = saddleC; M.segment([s * 0.3, 1.36, 0.0], [s * 0.33, 1.0, 0.02], 0.008, 0.008, 3, false, false); M.color = C3(PALF.bronze); M.push().translate(s * 0.33, 0.97, 0.02).rotate(0, 0, 90); M.torus([0, 0, 0], 0.035, 0.007, D.det >= 2 ? 6 : 5, D.det >= 2 ? 3 : 2); M.pop(); }
    }
    // 胸带与三颗红缨
    bone('chest');
    const bc = [];
    for (let i = 0; i <= 6; i++) { const a = lerp(-62, 62, i / 6) * D2R, z = 0.63 + Math.cos(a) * 0.1, sc = section(Math.min(z, 0.72)); bc.push([Math.sin(a) * (sc.w + 0.03), sc.cy + 0.02 - Math.cos(a) * 0.06, z + 0.02]); }
    M.color = saddleC;
    for (let i = 0; i < 6; i += D.det >= 1 ? 1 : 2) strap(bc[i], bc[Math.min(6, i + (D.det >= 1 ? 1 : 2))], 0.06, 0.06, V.add([bc[i][0], 0, 1], [0, 0, 0.5]), 0.016);
    for (const i of D.det >= 1 ? [2, 3, 4] : [3]) {
      const p = bc[i];
      M.color = gold; M.sphere(V.add(p, [0, -0.02, 0.012]), 0.022, 5, 2, false);
      const v0 = M.vertexCount; M.color = tasselC;
      M.push().translate(p[0], 0, p[2] + 0.015);
      M.lathe([[0.012, p[1] - 0.03], [0.042, p[1] - 0.08], [0.036, p[1] - 0.16], [0.012, p[1] - 0.2]].reverse(), D.det >= 1 ? 5 : 4, false, true, true);
      M.pop();
      M.windBy(v0, q => 0.3 * clamp01((p[1] - q[1]) / 0.2));
    }
    return M;
  }

  // ======================================================== 动作片段 ==
  // 关键帧字段（全部可缺，缺省取该流派的站姿）：
  //   hip [dx,dy,dz] 骨盆位移（米） · rot [x,y,z] 整体（root 骨）旋转（度） · pel / sp / ch / nk / hd 脊柱各节欧拉角（度）
  //   tL / tR 大腿欧拉角 · kL / kR 膝屈曲（度，正 = 向后屈） · fL / fR 脚欧拉角
  //   g [x,y,z] 右手握点（root 局部坐标） · w [偏航, 俯仰, 横滚] 兵器朝向（度；俯仰正 = 尖端向前倒，偏航正 = 转向人物左侧）
  //   o [x,y,z] 左手（单手兵器 / 双持 / 盾 / 弓）· ow 左手兵器 / 盾 / 弓的朝向 · eR / eL 肘的指向提示 · twL 左前臂扭转（度）
  //   cape 披风额外扬起 0..1 · e 本段缓动：'io'（缺省）| 'i' | 'o' | 'l'
  //   gnd 贴地 0..1（1 = 最低点落在地面上）· gw 兵器落地平放 0..1 · capeF 披风 / 后垂压向身体 0..1（倒地时）
  const STANCE = {
    pole: { hip: [0, -0.06, 0], rot: [0, 0, 0], pel: [0, 18, 0], sp: [3, -6, 0], ch: [5, -10, 0], nk: [0, -2, 0], hd: [-4, -6, 0],
      tL: [-16, 0, 9], kL: 28, fL: [-12, -10, -6], tR: [10, 0, -9], kR: 18, fR: [-6, 10, 6],
      g: [-0.27, 1.0, 0.1], w: [-6, 30, -90], o: [0.2, 0.85, 0.08], ow: [0, 30, 0], eR: [-0.8, -0.4, -0.4], eL: [0.35, -0.5, -0.7], twL: 0, cape: 0, sh: 0 },
    one: { hip: [0, -0.05, 0], rot: [0, 0, 0], pel: [0, 10, 0], sp: [3, -4, 0], ch: [4, -6, 0], nk: [0, 0, 0], hd: [-4, -4, 0],
      tL: [-14, 0, 8], kL: 24, fL: [-10, -8, -5], tR: [8, 0, -8], kR: 16, fR: [-6, 8, 5],
      g: [-0.27, 0.96, 0.16], w: [12, 128, -90], o: [0.2, 0.85, 0.08], ow: [0, 20, 0], eR: [-0.6, -0.5, -0.6], eL: [0.35, -0.5, -0.7], twL: 0, cape: 0 },
    bow: { hip: [0, -0.03, 0], rot: [0, 0, 0], pel: [0, 0, 0], sp: [2, 0, 0], ch: [2, 0, 0], nk: [0, 0, 0], hd: [-2, 0, 0],
      tL: [-8, 0, 6], kL: 12, fL: [-4, -6, -4], tR: [6, 0, -6], kR: 10, fR: [-4, 6, 4],
      g: [-0.24, 0.98, 0.06], w: [0, 0, 0], o: [0.3, 0.98, 0.12], ow: [0, 10, 0], eR: [-0.8, -0.5, -0.3], eL: [0.8, -0.5, -0.2], twL: 0, cape: 0 },
  };
  STANCE.dual = Object.assign({}, STANCE.one, { g: [-0.3, 1.02, 0.18], w: [10, 55, -80], o: [0.3, 1.02, 0.18], ow: [-10, 55, 80] });
  STANCE.fan = Object.assign({}, STANCE.one, { pel: [0, 0, 0], ch: [2, 0, 0], hd: [-2, 0, 0], tL: [-6, 0, 4], kL: 8, fL: [-2, -4, -2], tR: [4, 0, -4], kR: 6, fR: [-2, 4, 2], hip: [0, -0.015, 0],
    g: [-0.22, 1.22, 0.24], w: [0, 18, -90], o: [0.12, 1.02, -0.1], ow: [0, 0, 0] });
  STANCE.none = Object.assign({}, STANCE.one, { g: [-0.25, 1.05, 0.18], w: [0, 70, -90], o: [0.25, 1.05, 0.18], ow: [0, 70, 90] });
  // 片段：dur 秒、loop、各流派关键帧（函数：s = 站姿，返回 [[t, {覆盖}], ...]）
  const CL = {};
  function defClip(name, dur, loop, desc, byStyle) { CL[name] = { dur, loop, desc, byStyle }; }
  const add = (a, b) => a.map((v, i) => v + (b[i] || 0));

  defClip('idle', 2.4, true, '待机（呼吸）', {
    _: s => [[0, {}], [1.2, { hip: add(s.hip, [0, -0.008, 0]), ch: add(s.ch, [2, 0, 0]), g: add(s.g, [0, -0.01, 0]), w: add(s.w, [0, 2, 0]) }], [2.4, {}]],
  });
  defClip('block', 1.0, true, '格挡（按住）', {
    pole: s => [[0, { sh: 1, g: [-0.24, 1.18, 0.26], w: [80, 82, 0], hip: [0, -0.1, -0.02], ch: [10, 0, 0], hd: [-8, 0, 0], kL: 38, kR: 28 }], [1, { sh: 1, g: [-0.24, 1.2, 0.26], w: [80, 82, 0], hip: [0, -0.1, -0.02], ch: [10, 0, 0], hd: [-8, 0, 0], kL: 38, kR: 28 }]],
    one: s => [[0, { g: [-0.12, 1.25, 0.32], w: [60, 30, -90], o: [0.2, 1.2, 0.3], ow: [-40, 30, 0], hip: [0, -0.09, 0], ch: [8, 0, 0], kL: 34, kR: 24, twL: -40 }], [1, { g: [-0.12, 1.26, 0.32], w: [60, 30, -90], o: [0.2, 1.21, 0.3], ow: [-40, 30, 0], hip: [0, -0.09, 0], ch: [8, 0, 0], kL: 34, kR: 24, twL: -40 }]],
    dual: s => [[0, { g: [-0.1, 1.28, 0.3], w: [70, 30, -90], o: [0.1, 1.2, 0.3], ow: [-70, 30, 90], hip: [0, -0.09, 0], ch: [8, 0, 0], kL: 34, kR: 24 }], [1, { g: [-0.1, 1.29, 0.3], w: [70, 30, -90], o: [0.1, 1.21, 0.3], ow: [-70, 30, 90], hip: [0, -0.09, 0], ch: [8, 0, 0], kL: 34, kR: 24 }]],
    bow: s => [[0, { o: [0.14, 1.3, 0.3], ow: [0, 10, -60], g: [-0.1, 1.15, 0.25], hip: [0, -0.06, 0] }], [1, { o: [0.14, 1.31, 0.3], ow: [0, 10, -60], g: [-0.1, 1.16, 0.25], hip: [0, -0.06, 0] }]],
  });
  defClip('parry', 0.36, false, '格开：快速拨开来招', {
    pole: s => [[0, {}], [0.06, { sh: 1, g: [-0.2, 1.2, 0.28], w: [70, 75, 0], hip: [0, -0.09, 0], e: 'o' }], [0.14, { sh: 1, g: [-0.38, 1.32, 0.18], w: [-30, 45, -20], ch: [4, -25, 0], hip: [0, -0.08, 0], e: 'o' }], [0.36, {}]],
    one: s => [[0, {}], [0.06, { g: [-0.1, 1.28, 0.32], w: [60, 25, -90], e: 'o' }], [0.14, { g: [-0.42, 1.36, 0.2], w: [-40, 10, -60], ch: [4, -25, 0], e: 'o' }], [0.36, {}]],
    dual: s => [[0, {}], [0.06, { g: [-0.1, 1.28, 0.3], w: [70, 30, -90], o: [0.12, 1.24, 0.3], ow: [-70, 30, 90], e: 'o' }], [0.14, { g: [-0.42, 1.34, 0.18], w: [-40, 10, -60], o: [0.42, 1.3, 0.16], ow: [40, 10, 60], e: 'o' }], [0.36, {}]],
  });
  defClip('light1', 0.42, false, '轻击一：右向左横斩', {
    pole: s => [[0, {}], [0.1, { sh: 1, g: [-0.42, 1.2, -0.04], w: [-75, 88, 0], ch: [4, -35, 0], pel: [0, -10, 0], e: 'o' }], [0.2, { sh: 1, g: [0.2, 1.12, 0.36], w: [70, 92, 0], ch: [6, 30, 0], pel: [0, 30, 0], hip: [0, -0.08, 0.06], e: 'i' }], [0.42, {}]],
    one: s => [[0, {}], [0.1, { g: [-0.46, 1.32, -0.02], w: [-80, 80, -10], ch: [2, -32, 0], o: [0.25, 1.1, 0.25], e: 'o' }], [0.2, { g: [0.24, 1.18, 0.4], w: [80, 88, 0], ch: [6, 30, 0], pel: [0, 22, 0], hip: [0, -0.07, 0.06], o: [0.3, 1.0, -0.05], e: 'i' }], [0.42, {}]],
    dual: s => [[0, {}], [0.1, { g: [-0.46, 1.32, -0.02], w: [-80, 80, -10], ch: [2, -32, 0], e: 'o' }], [0.2, { g: [0.24, 1.18, 0.4], w: [80, 88, 0], ch: [6, 30, 0], o: [0.45, 1.05, -0.05], ow: [40, 60, 60], e: 'i' }], [0.42, {}]],
    bow: s => [[0, {}], [0.12, { o: [0.0, 1.25, 0.45], ow: [0, 70, -90], ch: [6, 20, 0], e: 'i' }], [0.42, {}]],
  });
  defClip('light2', 0.42, false, '轻击二：左向右反手上撩', {
    pole: s => [[0, {}], [0.1, { sh: 1, g: [0.08, 0.92, 0.28], w: [70, 120, 0], ch: [4, 28, 0], pel: [0, 26, 0], e: 'o' }], [0.21, { sh: 1, g: [-0.4, 1.38, 0.22], w: [-70, 50, 0], ch: [-4, -30, 0], pel: [0, -8, 0], hip: [0, -0.05, 0.05], e: 'i' }], [0.42, {}]],
    one: s => [[0, {}], [0.1, { g: [0.16, 0.95, 0.3], w: [80, 120, 0], ch: [4, 26, 0], e: 'o' }], [0.21, { g: [-0.42, 1.45, 0.24], w: [-70, 40, 0], ch: [-4, -30, 0], hip: [0, -0.05, 0.05], e: 'i' }], [0.42, {}]],
    dual: s => [[0, {}], [0.1, { o: [0.42, 1.32, 0.0], ow: [80, 80, 10], ch: [2, 32, 0], e: 'o' }], [0.2, { o: [-0.22, 1.18, 0.4], ow: [-80, 88, 0], g: [-0.45, 1.05, -0.05], ch: [6, -30, 0], e: 'i' }], [0.42, {}]],
  });
  defClip('light3', 0.6, false, '轻击三：突进刺击', {
    pole: s => [[0, {}], [0.14, { sh: 1, g: [-0.25, 1.15, -0.2], w: [6, 92, -90], ch: [0, -20, 0], pel: [0, -12, 0], hip: [0, -0.06, -0.06], kL: 34, e: 'o' }], [0.26, { sh: 1, g: [-0.08, 1.28, 0.55], w: [6, 90, -90], ch: [12, 10, 0], pel: [0, 10, 0], hip: [0, -0.12, 0.22], tL: [-40, 0, 6], kL: 50, tR: [24, 0, -6], kR: 10, cape: 0.6, e: 'i' }], [0.6, {}]],
    one: s => [[0, {}], [0.14, { g: [-0.3, 1.2, -0.12], w: [0, 92, -90], ch: [0, -22, 0], hip: [0, -0.06, -0.05], e: 'o' }], [0.26, { g: [-0.1, 1.3, 0.62], w: [0, 92, -90], ch: [12, 12, 0], hip: [0, -0.12, 0.22], tL: [-40, 0, 6], kL: 50, tR: [24, 0, -6], kR: 10, o: [0.3, 1.05, -0.1], cape: 0.6, e: 'i' }], [0.6, {}]],
    dual: s => [[0, {}], [0.14, { g: [-0.28, 1.3, 0.0], w: [-60, 70, 0], o: [0.28, 1.3, 0.0], ow: [60, 70, 0], hip: [0, -0.06, -0.05], e: 'o' }], [0.26, { g: [-0.08, 1.22, 0.52], w: [20, 95, -90], o: [0.08, 1.22, 0.52], ow: [-20, 95, 90], ch: [12, 0, 0], hip: [0, -0.12, 0.2], tL: [-40, 0, 6], kL: 50, tR: [24, 0, -6], kR: 10, cape: 0.6, e: 'i' }], [0.6, {}]],
  });
  defClip('heavy', 0.9, false, '重击：高举过顶后劈下（起手明显）', {
    pole: s => [[0, {}], [0.42, { sh: 1, g: [-0.27, 1.68, -0.05], w: [111, 51, -90], ch: [-10, -14, 0], pel: [0, -6, 0], hd: [-8, 10, 0], hip: [0, -0.03, -0.04], eR: [-0.9, 0.1, -0.4], eL: [0.8, 0.2, -0.4], cape: 0.3, e: 'io' }], [0.56, { sh: 1, g: [-0.06, 1.08, 0.45], w: [8, 108, -90], ch: [24, 4, 0], hd: [8, 0, 0], hip: [0, -0.18, 0.14], tL: [-44, 0, 8], kL: 62, tR: [26, 0, -8], kR: 24, cape: 0.8, e: 'i' }], [0.9, {}]],
    one: s => [[0, {}], [0.42, { g: [-0.27, 1.76, -0.02], w: [0, -30, -90], o: [0.25, 1.2, 0.2], ch: [-12, -14, 0], hd: [-10, 12, 0], eR: [-0.9, 0.1, -0.4], cape: 0.3 }], [0.56, { g: [-0.06, 1.08, 0.5], w: [0, 106, -90], ch: [24, 4, 0], hip: [0, -0.18, 0.14], tL: [-44, 0, 8], kL: 62, tR: [26, 0, -8], kR: 24, o: [0.32, 1.0, -0.1], cape: 0.8, e: 'i' }], [0.9, {}]],
    dual: s => [[0, {}], [0.42, { g: [-0.25, 1.76, 0.02], w: [0, -30, -90], o: [0.25, 1.76, 0.02], ow: [0, -30, 90], ch: [-12, 0, 0], hd: [-12, 0, 0], eR: [-0.9, 0.1, -0.4], eL: [0.9, 0.1, -0.4], cape: 0.3 }], [0.56, { g: [-0.12, 1.08, 0.48], w: [0, 106, -90], o: [0.12, 1.08, 0.48], ow: [0, 106, 90], ch: [24, 0, 0], hip: [0, -0.18, 0.14], tL: [-44, 0, 8], kL: 62, tR: [26, 0, -8], kR: 24, cape: 0.8, e: 'i' }], [0.9, {}]],
  });
  defClip('charge', 1.0, true, '蓄力（重击起手的定格）', {
    _: (s, st) => { const k = CL.heavy.byStyle[st] ? CL.heavy.byStyle[st](s)[1][1] : {}; return [[0, k], [0.5, Object.assign({}, k, { hip: add(k.hip || s.hip, [0, -0.01, 0]) })], [1, k]]; },
  });
  defClip('hit', 0.42, false, '受击（向后仰）', {
    _: s => [[0, {}], [0.07, { ch: [-14, 8, 4], sp: [-6, 0, 0], hd: [-22, 10, 0], hip: [0, -0.04, -0.08], g: add(s.g, [0.02, 0.06, -0.08]), w: add(s.w, [0, -12, 0]), o: s.o ? add(s.o, [0.04, 0.06, -0.08]) : null, cape: 0.3, e: 'o' }], [0.42, {}]],
  });
  defClip('hitB', 0.42, false, '受击（向前弯）', {
    _: s => [[0, {}], [0.07, { ch: [22, -6, -4], sp: [8, 0, 0], hd: [18, -8, 0], hip: [0, -0.07, 0.04], g: add(s.g, [0, -0.08, 0.06]), o: s.o ? add(s.o, [0, -0.08, 0.06]) : null, e: 'o' }], [0.42, {}]],
  });
  // 仰卧：gnd = 1 让背部落在地面上（不穿地），gw = 1 兵器脱手平放在身体右侧，capeF = 1 披风压在身下
  const LIE = { rot: [-88, 0, 0], hip: [0, -0.86, 0.0], ch: [0, 0, 0], sp: [0, 0, 0], hd: [-10, 25, 0], tL: [-10, 0, 14], kL: 20, tR: [-2, 0, -10], kR: 8, fL: [20, 0, 0], fR: [30, 0, 0], g: [-0.5, 1.0, 0.05], w: [90, -100, 0], o: [0.5, 0.45, 0.02], ow: [0, 0, 0], pel: [0, 0, 0], eR: [-0.6, 0, 0.8], eL: [0.6, 0, 0.8], sh: 0, gnd: 1, gw: 1, capeF: 1 };
  defClip('knockdown', 1.0, false, '击倒（仰面倒地）', {
    _: s => [[0, {}], [0.18, { rot: [-25, 0, 0], hip: [0, -0.12, -0.2], ch: [-20, 0, 0], hd: [-25, 0, 0], tL: [-40, 0, 10], kL: 40, tR: [-20, 0, -10], kR: 30, g: [-0.45, 1.2, -0.15], o: [0.45, 1.2, -0.15], cape: 0.8, sh: 0, gw: 0.15, e: 'o' }], [0.55, Object.assign({}, LIE, { hip: [0, -0.82, 0.02], e: 'i' })], [0.75, Object.assign({}, LIE, { hip: [0, -0.84, 0.01] })], [1.0, LIE]],
  });
  defClip('down', 2.0, true, '倒地', { _: s => [[0, LIE], [1, Object.assign({}, LIE, { ch: [3, 0, 0] })], [2, LIE]] });
  defClip('getup', 0.8, false, '起身', {
    _: s => [[0, LIE], [0.3, { rot: [-30, 0, 0], hip: [0, -0.5, -0.1], ch: [30, 0, 0], tL: [-80, 0, 10], kL: 120, tR: [-30, 0, -10], kR: 90, fL: [30, 0, 0], g: [-0.35, 0.6, 0.2], o: [0.35, 0.4, 0.3], sh: 0, gnd: 1, gw: 0.6, capeF: 0.5, e: 'o' }], [0.55, { hip: [0, -0.3, 0], ch: [25, 0, 0], tL: [-60, 0, 8], kL: 90, tR: [-20, 0, -8], kR: 60, fL: [30, 0, 0], sh: 0.5 }], [0.8, {}]],
  });
  defClip('victory', 2.0, false, '胜利姿势', {
    pole: s => { const F = { g: [-0.3, 1.82, 0.12], w: [8, 12, -90], o: [0.27, 1.12, 0.26], ow: [0, 70, 0], sh: 0, ch: [-10, -8, 0], hd: [-16, 12, 0], hip: [0, -0.02, 0], tL: [-8, 0, 12], kL: 8, tR: [6, 0, -12], kR: 6, cape: 0.5 };
      return [[0, {}], [0.35, { sh: 1, g: [-0.2, 1.55, 0.15], w: [0, 10, -90], ch: [-6, 0, 0], hd: [-10, 0, 0] }], [0.7, { sh: 1, g: [-0.25, 1.65, 0.05], w: [180, 20, -90], ch: [-6, 20, 0] }], [1.1, F], [2.0, Object.assign({}, F, { cape: 0.25 })]]; },
    one: s => [[0, {}], [0.4, { g: [-0.25, 1.95, 0.15], w: [0, -10, -90], ch: [-8, 0, 0], hd: [-14, 0, 0], o: [0.3, 1.05, 0.05] }], [1.0, { g: [-0.22, 1.98, 0.18], w: [0, -5, -90], ch: [-10, -10, 0], hd: [-16, 10, 0], o: [0.18, 1.05, 0.05], tL: [-6, 0, 12], kL: 6, tR: [4, 0, -12], kR: 4 }], [2.0, { g: [-0.22, 1.98, 0.18], w: [0, -5, -90], ch: [-10, -10, 0], hd: [-16, 10, 0], o: [0.18, 1.05, 0.05], tL: [-6, 0, 12], kL: 6, tR: [4, 0, -12], kR: 4 }]],
  });
  defClip('defeat', 1.6, false, '败北（单膝跪地）', {
    _: s => { const K = { hip: [0, -0.48, -0.05], ch: [28, 0, 0], sp: [10, 0, 0], hd: [30, 0, 0], tL: [-80, 0, 6], kL: 85, fL: [0, 0, 0], tR: [8, 0, -6], kR: 110, fR: [40, 0, 0], g: [-0.3, 0.86, 0.36], w: [0, 5, -90], o: [0.25, 0.85, 0.3], ow: [0, 30, 0], sh: 0, gnd: 1 }; return [[0, {}], [0.6, Object.assign({}, K, { hip: [0, -0.42, -0.05], ch: [20, 0, 0], gnd: 0.6, e: 'o' })], [1.6, K]]; },
  });
  defClip('cheer', 1.2, true, '欢呼（举兵器）', {
    _: s => [[0, { g: [-0.25, 1.85, 0.1], w: [0, 0, -90], o: s.o ? [0.3, 1.8, 0.15] : null, ch: [-8, 0, 0], hd: [-14, 0, 0], hip: [0, -0.02, 0] }], [0.6, { g: [-0.28, 2.0, 0.12], w: [0, -8, -90], o: s.o ? [0.32, 1.95, 0.15] : null, ch: [-10, 0, 0], hd: [-18, 0, 0], hip: [0, 0.01, 0] }], [1.2, { g: [-0.25, 1.85, 0.1], w: [0, 0, -90], o: s.o ? [0.3, 1.8, 0.15] : null, ch: [-8, 0, 0], hd: [-14, 0, 0], hip: [0, -0.02, 0] }]],
  });
  const FACEDOWN = { rot: [86, 0, 0], hip: [0, -0.86, 0.0], ch: [0, 0, 0], sp: [0, 0, 0], hd: [-30, 30, 0], tL: [4, 0, 10], kL: 25, tR: [10, 0, -8], kR: 10, fL: [-40, 0, 0], fR: [-50, 0, 0], g: [-0.5, 1.0, -0.05], w: [90, -100, 0], o: [0.5, 0.95, -0.1], ow: [0, 0, 0], pel: [0, 0, 0], eR: [-0.6, 0, -0.8], eL: [0.6, 0, -0.8], sh: 0, gnd: 1, gw: 1, capeF: 1 };
  defClip('die', 1.4, false, '阵亡（跪倒后扑地）', {
    _: s => [[0, {}], [0.15, { ch: [-18, 0, 0], hd: [-25, 0, 0], hip: [0, -0.05, -0.04], g: add(s.g, [0, 0.1, -0.1]), cape: 0.4, e: 'o' }], [0.6, { hip: [0, -0.48, 0], ch: [30, 0, 0], hd: [20, 0, 0], tL: [-70, 0, 8], kL: 100, tR: [-60, 0, -8], kR: 100, fL: [40, 0, 0], fR: [40, 0, 0], g: [-0.3, 0.7, 0.35], w: [-20, 60, 0], o: [0.3, 0.7, 0.3], sh: 0, gnd: 1, gw: 0.3 }], [1.0, Object.assign({}, FACEDOWN, { e: 'i' })], [1.4, FACEDOWN]],
  });
  defClip('guardbreak', 0.6, false, '破防踉跄', {
    _: s => [[0, {}], [0.1, { ch: [-18, 12, 0], hd: [-20, 0, 0], hip: [0, -0.04, -0.12], g: [-0.5, 1.5, -0.1], w: [-60, 20, -90], o: [0.5, 1.45, -0.1], cape: 0.5, e: 'o' }], [0.6, {}]],
  });
  defClip('jump', 0.8, false, '跳跃（原地：调用方负责高度）', {
    _: s => [[0, {}], [0.1, { hip: [0, -0.2, 0], kL: 70, kR: 60, tL: [-40, 0, 8], tR: [-30, 0, -8], ch: [15, 0, 0] }], [0.22, { hip: [0, 0.02, 0], kL: 10, kR: 10, tL: [-10, 0, 6], tR: [6, 0, -6] }], [0.45, { hip: [0, -0.08, 0], kL: 90, kR: 80, tL: [-60, 0, 10], tR: [-40, 0, -10], ch: [8, 0, 0], cape: 0.6 }], [0.7, { hip: [0, -0.16, 0], kL: 50, kR: 45 }], [0.8, {}]],
  });
  defClip('special', 1.4, false, '绝技：旋身横扫 + 跃起劈斩', {
    pole: s => [[0, {}], [0.15, { sh: 1, g: [-0.45, 1.25, -0.05], w: [-90, 90, 0], ch: [4, -40, 0], hip: [0, -0.1, 0], e: 'o' }], [0.45, { sh: 1, rot: [0, 180, 0], g: [0.45, 1.25, 0.1], w: [90, 90, 0], ch: [4, 30, 0], hip: [0, -0.12, 0], cape: 1 }], [0.75, { sh: 1, rot: [0, 360, 0], g: [-0.45, 1.25, -0.05], w: [-90, 90, 0], ch: [4, -30, 0], hip: [0, -0.1, 0], cape: 1 }], [0.98, { sh: 1, g: [-0.27, 1.7, -0.05], w: [111, 51, -90], ch: [-12, -14, 0], hd: [-8, 10, 0], hip: [0, 0.1, 0], kL: 40, kR: 40, rot: [0, 360, 0], eR: [-0.9, 0.1, -0.4], eL: [0.8, 0.2, -0.4], cape: 0.6 }], [1.1, { sh: 1, g: [-0.06, 1.05, 0.5], w: [8, 108, -90], ch: [26, 0, 0], hip: [0, -0.2, 0.15], tL: [-46, 0, 8], kL: 64, tR: [26, 0, -8], kR: 26, rot: [0, 360, 0], cape: 1, e: 'i' }], [1.4, { sh: 1, rot: [0, 360, 0] }]],
  });
  // 步态（程序化）
  defClip('walk', 1.0, true, '行走', null);
  defClip('run', 0.66, true, '奔跑', null);
  defClip('dash', 0.5, true, '冲刺', null);
  defClip('ride', 1.2, true, '骑乘（慢步）', null);
  defClip('gallop', 0.5, true, '骑乘（疾驰）', null);
  defClip('bowDraw', 0.8, false, '开弓（结束时满弓）', {
    _: s => [[0, {}], [0.3, { o: [0.12, 1.42, 0.5], ow: [-90, 0, 0], g: [-0.02, 1.42, 0.42], ch: [0, -70, 0], pel: [0, -50, 0], hd: [0, 60, 0], e: 'o' }], [0.8, { o: [0.12, 1.42, 0.55], ow: [-90, 0, 0], g: [-0.28, 1.48, -0.02], ch: [0, -72, 0], pel: [0, -50, 0], hd: [0, 64, 0], eR: [-0.6, 0.2, -0.8] }]],
  });
  defClip('bowShoot', 0.5, false, '放箭', {
    _: s => [[0, { o: [0.12, 1.42, 0.55], ow: [-90, 0, 0], g: [-0.28, 1.48, -0.02], ch: [0, -72, 0], pel: [0, -50, 0], hd: [0, 64, 0], eR: [-0.6, 0.2, -0.8] }], [0.08, { o: [0.12, 1.42, 0.56], ow: [-90, 0, 0], g: [-0.4, 1.5, -0.12], ch: [0, -72, 0], pel: [0, -50, 0], hd: [0, 64, 0], eR: [-0.6, 0.2, -0.8], e: 'o' }], [0.5, {}]],
  });
  const STYLE_FALLBACK = { pole: ['pole', '_'], one: ['one', '_'], dual: ['dual', 'one', '_'], fan: ['one', '_'], none: ['one', '_'], bow: ['bow', 'one', '_'] };
  const FIELDS3 = ['hip', 'rot', 'pel', 'sp', 'ch', 'nk', 'hd', 'tL', 'fL', 'tR', 'fR', 'g', 'w', 'o', 'ow', 'eR', 'eL'];
  // gnd：贴地（1 = 最低点落在地面上，0 = 只防止穿地）· gw：兵器落地平放（倒地片段）· capeF：披风 / 后垂贴身（仰卧时）
  const FIELDS1 = ['kL', 'kR', 'twL', 'cape', 'sh', 'gnd', 'gw', 'capeF'];
  // 编译片段：合并站姿，得到完整关键帧
  const COMPILED = new Map();
  function compiled(name, style) {
    const key = name + '|' + style;
    let c = COMPILED.get(key);
    if (c) return c;
    const def = CL[name];
    if (!def || !def.byStyle) return null;
    const s = STANCE[style] || STANCE.one;
    let fn = null;
    for (const k of STYLE_FALLBACK[style] || ['_']) if (def.byStyle[k]) { fn = def.byStyle[k]; break; }
    if (!fn) fn = def.byStyle.pole || def.byStyle.one;
    const keys = fn(s, style).map(([t, o]) => {
      const f = {};
      for (const k of FIELDS3) f[k] = o[k] !== undefined ? (o[k] && o[k].slice ? o[k].slice() : o[k]) : (s[k] && s[k].slice ? s[k].slice() : s[k]);
      for (const k of FIELDS1) f[k] = o[k] !== undefined ? o[k] : s[k];
      // 风格换算：扇 / 空手不用双手杆；弓的左手持弓
      if (style !== 'pole' && f.o == null) f.o = (s.o || [0.25, 1.0, 0.1]).slice();
      f.e = o.e || 'io';
      return { t, f };
    });
    c = { dur: def.dur, loop: def.loop, keys };
    COMPILED.set(key, c);
    return c;
  }
  function ease(e, t) { return e === 'l' ? t : e === 'i' ? t * t : e === 'o' ? 1 - (1 - t) * (1 - t) : t * t * (3 - 2 * t); }
  // 采样到姿势对象（各字段数组），out 复用
  function newPoseObj() { const p = {}; for (const k of FIELDS3) p[k] = [0, 0, 0]; for (const k of FIELDS1) p[k] = 0; p.hasO = true; return p; }
  function samplePose(c, t, out) {
    const K = c.keys, n = K.length;
    if (c.loop) t = ((t % c.dur) + c.dur) % c.dur; else t = clamp(t, 0, c.dur);
    let i = 0;
    while (i < n - 2 && K[i + 1].t <= t) i++;
    const a = K[i], b = K[Math.min(n - 1, i + 1)];
    const u = b.t > a.t ? ease(b.f.e, clamp01((t - a.t) / (b.t - a.t))) : 1;
    for (const k of FIELDS3) {
      const A = a.f[k], Bv = b.f[k], o = out[k];
      if (A == null && Bv == null) { out[k + 'N'] = true; continue; }
      out[k + 'N'] = false;
      const AA = A || Bv, BB = Bv || A;
      o[0] = lerp(AA[0], BB[0], u); o[1] = lerp(AA[1], BB[1], u); o[2] = lerp(AA[2], BB[2], u);
    }
    for (const k of FIELDS1) out[k] = lerp(a.f[k] || 0, b.f[k] || 0, u);
    return out;
  }
  // 程序化步态
  function gaitPose(name, t, style, out) {
    const c = compiled('idle', style);
    samplePose(c, 0, out);
    const dur = CL[name].dur, ph = ((t % dur) + dur) % dur / dur * TAU;
    const sn = Math.sin(ph), cs = Math.cos(ph);
    if (name === 'ride' || name === 'gallop') {
      const g = name === 'gallop';
      out.hip[0] = 0; out.hip[1] = (g ? 0.025 : 0.01) * Math.abs(Math.sin(ph)) - 0.02; out.hip[2] = 0;
      out.pel[0] = 0; out.pel[1] = 0; out.pel[2] = 0;
      out.sp[0] = (g ? 14 : 4) + sn * (g ? 4 : 1.5); out.ch[0] = g ? 10 : 2; out.ch[1] = 0; out.hd[0] = g ? -12 : -3; out.hd[1] = 0;
      for (const [T, Kk, F, sg] of [['tL', 'kL', 'fL', 1], ['tR', 'kR', 'fR', -1]]) { out[T][0] = -62; out[T][1] = sg * 4; out[T][2] = sg * 24; out[Kk] = 78; out[F][0] = -10; out[F][1] = 0; out[F][2] = -sg * 10; }
      out.g[0] = -0.24; out.g[1] = 1.1 + (g ? 0.02 * sn : 0); out.g[2] = 0.22;
      out.w[0] = 10; out.w[1] = g ? 72 : 35; out.w[2] = -90;
      if (style === 'pole') { out.w[0] = 18; out.w[1] = g ? 80 : 42; }
      if (!out.oN && style !== 'pole') { out.o[0] = 0.12; out.o[1] = 1.1; out.o[2] = 0.32; }
      out.cape = g ? 0.9 : 0.25;
      return out;
    }
    const run = name === 'run' || name === 'dash', dash = name === 'dash';
    const A = dash ? 46 : run ? 38 : 24, kb = dash ? 85 : run ? 70 : 30;
    out.hip[1] = (run ? -0.06 : -0.03) - (run ? 0.035 : 0.015) * Math.abs(cs) + (run ? 0.02 : 0);
    out.hip[2] = 0;
    out.pel[1] = sn * (run ? 8 : 6); out.pel[0] = 0;
    out.sp[0] = run ? 10 : 2; out.ch[0] = run ? (dash ? 16 : 10) : 3; out.ch[1] = -sn * (run ? 10 : 6); out.hd[0] = run ? -10 : -3; out.hd[1] = sn * 3;
    out.tL[0] = -sn * A - (run ? 10 : 4); out.tL[2] = 4; out.tR[0] = sn * A - (run ? 10 : 4); out.tR[2] = -4;
    out.kL = 8 + Math.max(0, cs) * kb + (run ? 12 : 4); out.kR = 8 + Math.max(0, -cs) * kb + (run ? 12 : 4);
    out.fL[0] = -out.tL[0] * 0.5 - out.kL * 0.4 + 10; out.fR[0] = -out.tR[0] * 0.5 - out.kR * 0.4 + 10;
    out.fL[1] = out.fR[1] = 0; out.fL[2] = out.fR[2] = 0;
    out.tL[1] = out.tR[1] = 0;
    // 兵器：奔跑时拖在身后（长兵）/ 握在体侧
    if (run) {
      if (style === 'pole') { out.g[0] = -0.3; out.g[1] = 1.02 + 0.02 * sn; out.g[2] = -0.05; out.w[0] = 20; out.w[1] = 100; out.w[2] = -90; }
      else { out.g[0] = -0.3; out.g[1] = 1.02 + 0.05 * sn; out.g[2] = -0.05 + 0.15 * -sn; out.w[0] = 0; out.w[1] = 110; out.w[2] = -90; if (out.o) { out.o[0] = 0.28; out.o[1] = 1.05 + 0.04 * -sn; out.o[2] = 0.15 * sn; } }
    } else {
      out.g[2] += 0.05 * -sn * (style === 'pole' ? 0.3 : 1);
      if (out.o && style !== 'pole') out.o[2] += 0.08 * sn;
    }
    out.cape = run ? (dash ? 1 : 0.7) : 0.15;
    return out;
  }

  // ======================================================== IK 与姿势应用 ==
  const _q = () => new THREE.Quaternion();
  const TMP = {};
  function tmp() {
    if (TMP.ok) return TMP;
    TMP.ok = true;
    TMP.e = new THREE.Euler(); TMP.q1 = _q(); TMP.q2 = _q(); TMP.q3 = _q(); TMP.q4 = _q(); TMP.m = new THREE.Matrix4(); TMP.m2 = new THREE.Matrix4();
    TMP.v1 = _v3(); TMP.v2 = _v3(); TMP.v3 = _v3(); TMP.v4 = _v3(); TMP.v5 = _v3(); TMP.v6 = _v3();
    TMP.X = new THREE.Vector3(1, 0, 0); TMP.Y = new THREE.Vector3(0, 1, 0); TMP.Z = new THREE.Vector3(0, 0, 1);
    return TMP;
  }
  function qEuler(q, a, order) { const T = tmp(); T.e.set((a[0] || 0) * D2R, (a[1] || 0) * D2R, (a[2] || 0) * D2R, order || 'YXZ'); return q.setFromEuler(T.e); }
  // 兵器朝向：先绕 Y 偏航，再绕 X 俯仰，最后绕自身 Y 横滚
  let QW_T = null;
  function qWeapon(q, w) {
    const T = tmp();
    if (!QW_T) QW_T = new THREE.Quaternion();
    q.setFromAxisAngle(T.Y, (w[0] || 0) * D2R);
    QW_T.setFromAxisAngle(T.X, (w[1] || 0) * D2R); q.multiply(QW_T);
    QW_T.setFromAxisAngle(T.Y, (w[2] || 0) * D2R); q.multiply(QW_T);
    return q;
  }

  // ======================================================== 模板（共用几何体） ==
  // 模板 = 某外观 × LOD × 势力色（单个人物：势力色烘进顶点色）或「士兵群」（势力色为实例属性，lpTeam 遮罩）的几何体。
  const TEMPLATES = new Map();
  function shieldLocal(pr) {
    const d = [pr.armDirL[0], pr.armDirL[1], 0], out = [Math.cos(ARM_A), Math.sin(ARM_A), 0];
    const Y = d, Z = out, X = v3cross(Y, Z);
    const q = new THREE.Quaternion().setFromRotationMatrix(new THREE.Matrix4().makeBasis(new THREE.Vector3(...X), new THREE.Vector3(...Y), new THREE.Vector3(...Z)));
    return { p: v3add(v3mul(d, pr.foL * 0.45), v3mul(out, 0.035)), q };
  }
  function shieldRest(pr) {
    const s = shieldLocal(pr), J = pr.J.foreArmL;
    return new THREE.Matrix4().compose(new THREE.Vector3(J[0] + s.p[0], J[1] + s.p[1], J[2] + s.p[2]), s.q, new THREE.Vector3(1, 1, 1));
  }
  const WDET = { high: 2, mid: 1, low: 0 };
  // 独立兵器几何体（名将的兵器网格、SG.Figure.weapon、setWeapon）：按 (兵器, 细节) 缓存
  const WGEO = new Map();
  function weaponGeo(id, det) {
    const key = id + '|' + det;
    let w = WGEO.get(key);
    if (!w) {
      const M = new (FigBuilder())(11);
      const info = buildWeaponLP(M, WEAPONS[id] ? id : 'jian', det);
      const geo = M.toGeometry();
      geo.computeBoundingBox();
      w = { geo, info, tris: geo.userData.tris, range: [geo.boundingBox.min.y, geo.boundingBox.max.y] };
      WGEO.set(key, w);
    }
    return w;
  }
  function buildTemplate(look, mode, hexc) {
    const t0 = now();
    const lodName = LODK[look.lod] ? look.lod : 'high';
    const pr = makeProps(look);
    const hero = look.kind === 'general' && !!(SIG[look.name] || (look.spec && look.spec.war >= 90));
    const budget = lodName === 'high' ? (hero ? TRI_BUDGET.hero : TRI_BUDGET.high) : lodName === 'mid' ? TRI_BUDGET.mid : TRI_BUDGET.low;
    const team = mode === 'crowd' ? [1, 1, 1] : teamTone(hexc || look.factionHex);
    const levels = SQUEEZE[lodName];
    let tpl = null;
    for (let lv = 0; lv <= levels.length; lv++) {
      const D = lv ? Object.assign({}, LODK[lodName], levels[lv - 1]) : LODK[lodName];
      tpl = buildTemplateOnce(look, pr, D, lodName, team);
      tpl.squeeze = lv; tpl.budget = budget;
      if (tpl.tris <= budget) break;
      if (lv < levels.length) { tpl.geometry.dispose(); STATS.templates--; }
    }
    tpl.ms = now() - t0; STATS.lastBuildMs = tpl.ms;
    const tk = (look.kind === 'soldier' ? look.type + '/' + look.culture : look.name) + '@' + lodName;
    STATS.tris[tk] = tpl.tris;
    if (!STATS.parts) STATS.parts = {};
    STATS.parts[tk] = tpl.parts;
    return tpl;
  }
  function buildTemplateOnce(look, pr, D, lodName, team) {
    const t0 = now();
    const seed = (hashStr((look.name || '') + '|' + look.culture + '|' + (look.type || '') + '|' + (look.seed | 0)) % 9973) + 1;
    const kit = new Kit(look, pr, D, team, seed);
    const X = kit.X = paletteOf(kit);
    const M = kit.M;
    legsLP(kit, X); torsoLP(kit, X); neckLP(kit, X); armsLP(kit, X); skirtLP(kit, X); beltLP(kit, X); pauldronsLP(kit, X);
    capeLP(kit, X); ribbonsLP(kit, X); backGearLP(kit, X);
    faceLP(kit, X);
    const cov = headgearLP(kit, X);
    hairLP(kit, X, cov); beardLP(kit, X); accessoriesLP(kit, X);
    const tpl = { look, pr, lod: lodName, boneInverses: makeBoneInverses(pr), refs: 0, key: look.key, parts: kit.parts, D };
    const weaponId = look.weapon || 'jian';
    const wdef = WEAPONS[weaponId] || WEAPONS.jian;
    tpl.style = wdef.style;
    const wdet = Math.min(WDET[lodName], D.det);
    let wtris = 0;
    if (look.kind === 'soldier') {
      // 士兵：兵器与盾烘进同一个蒙皮网格（一次绘制 + 一次墨线）
      const side = wdef.style === 'bow' ? 'L' : 'R';
      kit.bone('hand' + side);
      M.push().setMatrix(M.matrix.clone().multiply(socketRest(pr, side)));
      tpl.weapon = buildWeaponLP(M, weaponId, wdet);
      M.pop();
      kit.mark('weapon');
      tpl.weaponRange = weaponGeo(weaponId, 0).range;
    } else {
      const w = weaponGeo(weaponId, wdet);
      tpl.weapon = w.info; tpl.weaponGeo = w.geo; tpl.weaponRange = w.range;
      wtris = w.tris * (wdef.style === 'dual' ? 2 : 1);
    }
    if (look.shield) {
      kit.bone('foreArmL');
      M.push().setMatrix(M.matrix.clone().multiply(shieldRest(pr)));
      tpl.shieldInfo = buildShieldLP(M, look.shield, wdet, X);
      M.pop();
      tpl.hasShield = true;
      kit.mark('shield');
    }
    tpl.geometry = M.toGeometry({ skinned: true });
    tpl.tris = tpl.geometry.userData.tris + wtris;
    tpl.ms = now() - t0;
    STATS.templates++;
    STATS.buildMs += tpl.ms;
    return tpl;
  }
  function getTemplate(look, key, mode, hexc) {
    let t = TEMPLATES.get(key);
    if (!t) { look.key = key; t = buildTemplate(look, mode, hexc); t.key = key; t.mode = mode; TEMPLATES.set(key, t); }
    t.refs++;
    return t;
  }
  function releaseTemplate(t) {
    if (!t) return;
    t.refs--;
    // 士兵群模板常驻（复用率高）；名将与单个士兵（势力色烘进顶点）的模板在无人使用时释放
    if (t.refs <= 0 && t.mode !== 'crowd') {
      TEMPLATES.delete(t.key);
      t.geometry.dispose();
    }
  }

  // ======================================================== Figure ==
  // Lanternvale 的 PoleHold（试点 SG.LPPilot.POLE_LEAN）：静止 / 行进片段里，烘在右手上的长兵在人物坐标里向外横滚 roll 度、
  // 向后 back 度，杆子立在头侧、不把脸劈成两半；出招片段保持原来的兵器线。on = false 关闭（SG.Figure.POLE_LEAN）。
  const POLE_LEAN = { on: true, roll: 20, back: -24, clips: new Set(['idle', 'walk', 'run', 'ride', 'gallop', 'march', 'stand']) };
  const SOCK = {};
  const DEF_O = [0.27, 1.0, 0.1], DEF_OW = [0, 0, 0], DEF_OFF = [0, 0.5, 0];
  // 双手 IK 只需要的骨链（父骨在前）
  const FK_LIST = ['root', 'pelvis', 'spine', 'chest', 'clavL', 'upperArmL', 'foreArmL', 'handL', 'clavR', 'upperArmR', 'foreArmR', 'handR'].map(n => B[n]);
  const FK_PAR = BONES.map(n => (PARENT[n] ? B[PARENT[n]] : -1));
  class Figure {
    constructor(tpl, opts) {
      opts = opts || {};
      this.tpl = tpl;
      this.kind = tpl.look.kind;
      this.spec = tpl.look;
      this.style = tpl.style;
      this.weaponInfo = tpl.weapon;
      this.root = new THREE.Group();
      this.root.name = 'Figure:' + (tpl.look.name || tpl.look.type || '');
      const pr = tpl.pr;
      this.pr = pr;
      const bones = makeBones(pr);
      this._bones = bones;
      this.bones = {};
      BONES.forEach((n, i) => { this.bones[n] = bones[i]; });
      this.restPos = bones.map(b => b.position.clone());
      this.skeleton = new THREE.Skeleton(bones, tpl.boneInverses);
      const L = LPX();
      const shadow = opts.shadow !== false;
      // 骨架挂在 root 下（不挂在网格下：外观 setLook 只作用于本体网格与它的墨线，骑在马上的人物不受马的外观影响）
      this.root.add(bones[0]);
      this.mesh = L.skinnedMesh(tpl.geometry, this.skeleton, { castShadow: shadow, receiveShadow: false });
      this.mesh.name = 'body';
      // 地面压平：LP 材质不做（倒地时布料、缨低于地面的部分被地面挡住）；保留字段以兼容
      this.groundClamp = true;
      this.root.add(this.mesh);
      this.meshes = [this.mesh, this.mesh.userData.outline].filter(Boolean);
      const HS = pr.head;
      this._headPt = [(HS.c[0] - pr.J.head[0]) / pr.k, (HS.c[1] - pr.J.head[1]) / pr.k, (HS.c[2] - pr.J.head[2]) / pr.k];
      this._headR = 0.5 * HS.H / pr.k;
      // 挂点
      this.sockets = {};
      for (const S of ['L', 'R']) {
        const g = gripLocal(pr, S), o = new THREE.Object3D();
        o.name = 'grip' + S; o.position.set(g.p[0], g.p[1], g.p[2]); o.quaternion.copy(g.q);
        this.bones['hand' + S].add(o); this.sockets['grip' + S] = o;
        this['_grip' + S] = g;
      }
      const sl = shieldLocal(pr), so = new THREE.Object3D();
      so.name = 'shieldL'; so.position.set(sl.p[0], sl.p[1], sl.p[2]); so.quaternion.copy(sl.q);
      this.bones.foreArmL.add(so); this.sockets.shieldL = so;
      const bk = new THREE.Object3D(); bk.name = 'back'; bk.position.set(0, 0.18 * pr.k, -0.17 * pr.k); this.bones.chest.add(bk); this.sockets.back = bk;
      const hs = new THREE.Object3D(); hs.name = 'headTop'; hs.position.set(0, HS.c[1] - pr.J.head[1] + 0.55 * HS.H, HS.c[2] - pr.J.head[2]); this.bones.head.add(hs); this.sockets.headTop = hs;
      // 名将：兵器为独立网格（单挑按判定距离缩放兵器、取尖端轨迹）；头与盾并入本体
      this.head = null; this.weaponMesh = null; this.weaponMesh2 = null; this.shieldMesh = null;
      this._shadow = shadow;
      if (tpl.weaponGeo) this._makeWeaponMeshes(tpl.weaponGeo);
      this.hasShield = !!tpl.hasShield;
      this._wRange = tpl.weaponRange || null;
      if (opts.blob) {
        this.blob = L.blobShadow(0.42 * pr.k, 0.4 * pr.k, 0.5, 0.5);
        Object.defineProperty(this.blob, 'castShadow', { get: () => false, set: () => {}, configurable: true });
        this.root.add(this.blob);
      }
      this._look = {};
      attachLook(this.mesh, this._look);
      for (const m of [this.weaponMesh, this.weaponMesh2]) if (m) attachLook(m, this._look);
      this.mounted = null;
      this._P = newPoseObj(); this._P2 = newPoseObj(); this._PL = newPoseObj();
      this._R = torsoProfile(pr);
      this._hasCape = !!tpl.look.C.cape;
      this._skirtFl = /robe|coat|sarong/.test(tpl.look.L.skirt || '') ? 0.12 : (tpl.look.L.skirt && tpl.look.L.skirt !== 'none') ? 0.07 : 0.02;
      this._Wp = bones.map(() => new THREE.Vector3()); this._Wq = bones.map(() => new THREE.Quaternion());
      this._sec = { cape: 0, capeV: 0, plume: 0, plumeV: 0, skF: 0, skB: 0, hair: 0, hairV: 0, t: 0, live: false, tgtCape: 0, tgtPlume: 0, tgtHair: 0 };
      this._time = 0;
      this.triangles = Math.round(tpl.tris);
      this.drawCalls = this.meshes.length;
      STATS.built++;
      this.setPose('idle', 0);
    }
    clipInfo(name) { const c = CL[name]; return c ? { dur: c.dur, loop: c.loop, desc: c.desc } : null; }
    // ------------------------------------------------------------ 姿势 --
    _sample(name, t, out) {
      const c = CL[name] ? CL[name] : CL.idle;
      if (!c.byStyle) return gaitPose(name in CL ? name : 'idle', t, this.style, out);
      const cc = compiled(name, this.style) || compiled('idle', this.style);
      return samplePose(cc, t, out);
    }
    setPose(name, t, opts) {
      opts = opts || {};
      const P = this._sample(name, t || 0, this._P);
      if (opts.blend && opts.blend.clip) {
        const Q = this._sample(opts.blend.clip, opts.blend.t || 0, this._P2);
        blendPoses(P, Q, 1 - clamp01(opts.blend.w != null ? opts.blend.w : 0.5));
      }
      const lowerName = opts.lower || (this.mounted ? (opts.gallop || name === 'gallop' ? 'gallop' : 'ride') : null);
      if (lowerName && lowerName !== name) {
        const L = this._sample(lowerName, opts.lowerT != null ? opts.lowerT : (t || 0), this._PL);
        for (const k of ['hip', 'pel', 'tL', 'tR', 'fL', 'fR']) for (let i = 0; i < 3; i++) P[k][i] = L[k][i];
        P.kL = L.kL; P.kR = L.kR;
        if (this.mounted) { P.rot[0] = 0; P.rot[2] = 0; }
      }
      this._clipName = name; this._clipT = t;
      this._apply(P, opts);
      // 长兵外倾（Lanternvale PoleHold）：静止 / 行进片段里杆子向外、向后倾，不挡脸
      // 只在人物自己画本体时外倾：本体材质被换掉（如 js/lp-pilot.js 把 figure.js 当骨架用、自己再做 poleLean）时不重复外倾
      const own = this.mesh && this.mesh.material && this.mesh.material.userData && this.mesh.material.userData.lpBase;
      if (this.style === 'pole' && opts.lean !== false && POLE_LEAN.on && own) {
        let f = POLE_LEAN.clips.has(name) ? 1 : 0;
        if (opts.blend && opts.blend.clip) { const w = clamp01(opts.blend.w != null ? opts.blend.w : 0.5); f = f * w + (POLE_LEAN.clips.has(opts.blend.clip) ? 1 - w : 0); }
        if (f > 0.001) this._poleLean(f);
      }
      return this;
    }
    // 右手（兵器烘在它上面）在人物坐标里转 roll 度向外、back 度向后：qH' = qR·qL·qR⁻¹·qH
    _poleLean(f) {
      const T = tmp(), bn = this._bones;
      this._fk();
      const Wq = this._Wq;
      const qL = T.q1.setFromEuler(T.e.set(POLE_LEAN.back * f * D2R, 0, POLE_LEAN.roll * f * D2R, 'XZY'));
      const qW = T.q2.copy(Wq[B.root]).multiply(qL).multiply(T.q3.copy(Wq[B.root]).invert()).multiply(Wq[B.handR]);
      bn[B.handR].quaternion.copy(T.q3.copy(Wq[B.foreArmR]).invert().multiply(qW));
    }
    blendPose(a, ta, b, tb, w) { return this.setPose(a, ta, { blend: { clip: b, t: tb, w: 1 - w } }); }
    _apply(P, opts) {
      const T = tmp(), bn = this._bones, pr = this.pr;
      // 躯干与腿
      qEuler(bn[B.root].quaternion, P.rot, 'YXZ');
      bn[B.root].position.set(0, 0, 0);
      bn[B.pelvis].position.copy(this.restPos[B.pelvis]).add(T.v1.set(P.hip[0], P.hip[1], P.hip[2]));
      qEuler(bn[B.pelvis].quaternion, P.pel);
      qEuler(bn[B.spine].quaternion, P.sp);
      qEuler(bn[B.chest].quaternion, P.ch);
      qEuler(bn[B.neck].quaternion, P.nk);
      qEuler(bn[B.head].quaternion, P.hd);
      qEuler(bn[B.thighL].quaternion, P.tL, 'XZY'); qEuler(bn[B.thighR].quaternion, P.tR, 'XZY');
      bn[B.shinL].quaternion.setFromAxisAngle(T.X, P.kL * D2R); bn[B.shinR].quaternion.setFromAxisAngle(T.X, P.kR * D2R);
      qEuler(bn[B.footL].quaternion, P.fL, 'XZY'); qEuler(bn[B.footR].quaternion, P.fR, 'XZY');
      // 锁骨：手举高时耸肩（举过头顶时最多约 30°，减轻上臂相对绑定姿势的转角）
      const gy = P.g[1] - pr.J.upperArmR[1];
      bn[B.clavR].quaternion.setFromAxisAngle(T.Z, -clamp(gy * 70, 0, 30) * D2R);
      const shk = this.style === 'pole' ? clamp01(P.sh || 0) : 0;
      const oy = Math.max(P.o && !P.oN ? P.o[1] : 1.0, shk * (P.g[1] + 0.25)) - pr.J.upperArmL[1];
      bn[B.clavL].quaternion.setFromAxisAngle(T.Z, clamp(oy * 70, 0, 30) * D2R);
      this._secondary(P);
      // 贴地：身体任何部位不穿入地面（倒地 / 跪地片段同时让最低点落在地面上）
      if (!this.mounted && opts.clamp !== false) this._ground(P);
      if (opts.ik !== false) this._arms(P);
      // 肩部辅助骨：取上臂局部旋转的一半
      bn[B.shL].quaternion.identity().slerp(bn[B.upperArmL].quaternion, SH_K);
      bn[B.shR].quaternion.identity().slerp(bn[B.upperArmR].quaternion, SH_K);
      bn[B.dlL].quaternion.identity().slerp(bn[B.upperArmL].quaternion, DL_K);
      bn[B.dlR].quaternion.identity().slerp(bn[B.upperArmR].quaternion, DL_K);
    }
    // 全身正向运动学（mesh 空间）
    _fkAll() {
      const bn = this._bones, Wp = this._Wp, Wq = this._Wq;
      for (let i = 0; i < bn.length; i++) {
        const b = bn[i], j = FK_PAR[i];
        if (j < 0) { Wq[i].copy(b.quaternion); Wp[i].copy(b.position); continue; }
        Wq[i].copy(Wq[j]).multiply(b.quaternion);
        Wp[i].copy(b.position).applyQuaternion(Wq[j]).add(Wp[j]);
      }
    }
    _ground(P) {
      const T = tmp(), Wp = this._Wp, Wq = this._Wq, k = this.pr.k;
      this._fkAll();
      let mn = 1e9;
      const pt = (i, x, y, z, r) => { const v = T.v1.set(x * k, y * k, z * k).applyQuaternion(Wq[i]).add(Wp[i]); const h = v.y - r * k; if (h < mn) mn = h; };
      pt(B.pelvis, 0, 0, 0, 0.125); pt(B.pelvis, 0.11, -0.03, 0, 0.1); pt(B.pelvis, -0.11, -0.03, 0, 0.1);
      pt(B.spine, 0, 0.06, 0.005, 0.115); pt(B.chest, 0, 0.07, 0, 0.13); pt(B.chest, 0.15, 0.15, -0.01, 0.075); pt(B.chest, -0.15, 0.15, -0.01, 0.075);
      pt(B.head, this._headPt[0], this._headPt[1], this._headPt[2], this._headR); pt(B.neck, 0, 0.04, 0, 0.06);
      for (const S of ['L', 'R']) {
        const th = B['thigh' + S], sh = B['shin' + S], ft = B['foot' + S];
        const a = Wp[th], b = Wp[sh], c = Wp[ft];
        let y = (a.y + b.y) / 2 - 0.08 * k; if (y < mn) mn = y;
        y = b.y - 0.062 * k; if (y < mn) mn = y;
        y = (b.y + c.y) / 2 - 0.055 * k; if (y < mn) mn = y;
        pt(ft, 0, 0, 0, 0.045); pt(ft, 0, -0.078, -0.06, 0.008); pt(ft, 0, -0.07, 0.16, 0.012);
      }
      if (this._hasCape) { pt(B.cape2, 0, 0, -0.02, 0.02); pt(B.cape3, 0, 0, -0.03, 0.02); pt(B.cape3, 0, -0.38, -0.07, 0.015); }
      const g = clamp01(P.gnd || 0);
      const lift = g * -mn + (1 - g) * Math.max(0, -mn);
      if (Math.abs(lift) > 1e-5) this._bones[B.root].position.y = lift;
      this._lift = lift;
    }
    _fk() {
      const bn = this._bones, Wp = this._Wp, Wq = this._Wq;
      for (let k = 0; k < FK_LIST.length; k++) {
        const i = FK_LIST[k], b = bn[i], j = FK_PAR[i];
        if (j < 0) { Wq[i].copy(b.quaternion); Wp[i].copy(b.position); continue; }
        Wq[i].copy(Wq[j]).multiply(b.quaternion);
        Wp[i].copy(b.position).applyQuaternion(Wq[j]).add(Wp[j]);
      }
    }
    // 双骨 IK：S 侧手腕到 wrist（root 局部），手朝向 handQ，肘部提示 pole，前臂扭转 tw（度）
    _ik(S, wrist, handQ, pole, tw) {
      const T = tmp(), bn = this._bones, Wp = this._Wp, Wq = this._Wq;
      const iC = B['clav' + S], iU = B['upperArm' + S], iF = B['foreArm' + S], iH = B['hand' + S];
      const Sp = Wp[iU];
      const ru = bn[iF].position, rf = bn[iH].position;
      const a = ru.length(), b = rf.length();
      const d = T.v1.copy(wrist).sub(Sp);
      let dist = d.length();
      const dir = dist > 1e-6 ? d.multiplyScalar(1 / dist) : d.set(0, -1, 0);
      dist = clamp(dist, Math.abs(a - b) + 1e-3, a + b - 1e-4);
      const pl = T.v2.set(pole[0], pole[1], pole[2]);
      pl.addScaledVector(dir, -pl.dot(dir));
      if (pl.lengthSq() < 1e-8) pl.set(S === 'L' ? 1 : -1, -0.3, -0.3).addScaledVector(dir, -dir.dot(T.v6.set(S === 'L' ? 1 : -1, -0.3, -0.3)));
      pl.normalize();
      const cosA = clamp((a * a + dist * dist - b * b) / (2 * a * dist), -1, 1), sinA = Math.sqrt(1 - cosA * cosA);
      const E = T.v3.copy(Sp).addScaledVector(dir, a * cosA).addScaledVector(pl, a * sinA);
      const Tc = T.v4.copy(Sp).addScaledVector(dir, dist);
      const a1 = T.v5.copy(E).sub(Sp).normalize();
      const f1 = T.v6.copy(Tc).sub(E).normalize();
      const beta = Math.acos(clamp(a1.dot(f1), -1, 1));
      let n1x = a1.y * f1.z - a1.z * f1.y, n1y = a1.z * f1.x - a1.x * f1.z, n1z = a1.x * f1.y - a1.y * f1.x;
      let nl = Math.hypot(n1x, n1y, n1z);
      if (nl < 1e-4) { n1x = -(a1.y * pl.z - a1.z * pl.y); n1y = -(a1.z * pl.x - a1.x * pl.z); n1z = -(a1.x * pl.y - a1.y * pl.x); nl = Math.hypot(n1x, n1y, n1z) || 1; }
      n1x /= nl; n1y /= nl; n1z /= nl;
      // 绑定姿势的臂轴与屈肘轴
      const a0 = SOCK['a0' + S] || (SOCK['a0' + S] = ru.clone().normalize());
      const n0 = SOCK['n0' + S] || (SOCK['n0' + S] = new THREE.Vector3().crossVectors(a0, T.Z).normalize());
      const c0 = SOCK['c0' + S] || (SOCK['c0' + S] = new THREE.Vector3().crossVectors(a0, n0));
      const c1x = a1.y * n1z - a1.z * n1y, c1y = a1.z * n1x - a1.x * n1z, c1z = a1.x * n1y - a1.y * n1x;
      // Qu = Mdes · Mrestᵀ
      const m = T.m.set(
        a1.x * a0.x + n1x * n0.x + c1x * c0.x, a1.x * a0.y + n1x * n0.y + c1x * c0.y, a1.x * a0.z + n1x * n0.z + c1x * c0.z, 0,
        a1.y * a0.x + n1y * n0.x + c1y * c0.x, a1.y * a0.y + n1y * n0.y + c1y * c0.y, a1.y * a0.z + n1y * n0.z + c1y * c0.z, 0,
        a1.z * a0.x + n1z * n0.x + c1z * c0.x, a1.z * a0.y + n1z * n0.y + c1z * c0.y, a1.z * a0.z + n1z * n0.z + c1z * c0.z, 0,
        0, 0, 0, 1);
      const Qu = T.q1.setFromRotationMatrix(m);
      bn[iU].quaternion.copy(Wq[iC]).invert().multiply(Qu);
      const qf = bn[iF].quaternion.setFromAxisAngle(n0, beta);
      if (tw) qf.multiply(T.q2.setFromAxisAngle(a0, tw * D2R));
      const Qf = T.q2.copy(Qu).multiply(qf);
      bn[iH].quaternion.copy(Qf).invert().multiply(handQ);
    }
    _arms(P) {
      const T = tmp();
      const A = this._A || (this._A = { Qw: new THREE.Quaternion(), hR: new THREE.Quaternion(), hL: new THREE.Quaternion(), hS: new THREE.Quaternion(), Qo: new THREE.Quaternion(), giR: this._gripR.q.clone().invert(), giL: this._gripL.q.clone().invert(),
        gpR: new THREE.Vector3(...this._gripR.p), gpL: new THREE.Vector3(...this._gripL.p), G: new THREE.Vector3(), wR: new THREE.Vector3(), wL: new THREE.Vector3(), wS: new THREE.Vector3(), t: new THREE.Vector3(), flip: new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), PI) });
      this._fk();
      const rootQ = this._Wq[B.root];
      const st = this.style;
      // 右手：兵器朝向 → 手的朝向 → 手腕目标
      const rootP = this._Wp[B.root];
      const Qw = A.Qw.copy(rootQ).multiply(qWeapon(T.q4, P.w));
      A.G.set(P.g[0], P.g[1], P.g[2]);
      if (!this.mounted) this._avoidBody(A.G, P);
      A.G.applyQuaternion(rootQ).add(rootP);
      const gw = this.mounted ? 0 : clamp01(P.gw || 0);
      if (gw > 0.001) this._groundWeapon(A, Qw, gw);
      if (!this.mounted && A.G.y < 0.04) A.G.y = 0.04;
      if (!this.mounted && gw < 0.999) this._weaponAboveGround(A.G, Qw);
      const handR = A.hR.copy(Qw).multiply(A.giR);
      A.wR.copy(A.G).sub(A.t.copy(A.gpR).applyQuaternion(handR));
      this._ik('R', A.wR, handR, P.eR, 0);
      // 左手：长兵按 sh（0..1）在「自由位置」与「握杆」之间过渡；其他流派为自由位置（盾 / 副手兵器 / 弓）
      const o = P.o && !P.oN ? P.o : DEF_O;
      const handL = A.hL.copy(rootQ).multiply(qWeapon(T.q1, P.ow || DEF_OW)).multiply(A.giL);
      const op = A.wL.set(o[0], o[1], o[2]);
      if (!this.hasShield || st === 'bow') this._avoidBody(op, P);
      const wristL = op.applyQuaternion(rootQ).add(rootP);
      if (!this.mounted && wristL.y < 0.04) wristL.y = 0.04;
      wristL.sub(A.t.copy(A.gpL).applyQuaternion(handL));
      const sh = st === 'pole' ? clamp01(P.sh != null ? P.sh : 1) : 0;
      if (sh > 0.001) {
        this._fk();
        // 实际握点（IK 可能够不到目标）→ 杆上的左手位置：取杆上离理想握距最近、且左臂够得着的一点
        const Gp = A.G.copy(A.gpR).applyQuaternion(this._Wq[B.handR]).add(this._Wp[B.handR]);
        const off = this.weaponInfo && this.weaponInfo.off ? this.weaponInfo.off : DEF_OFF;
        const hS = A.hS.copy(Qw).multiply(A.flip).multiply(A.giL);
        // 腕点 = 杆上点 − 握点偏移（随兵器朝向）：在腕点上求可及区间
        const wo = A.wS.copy(A.gpL).applyQuaternion(hS);
        Gp.sub(wo);
        const dir = A.t.set(0, 1, 0).applyQuaternion(Qw);
        // 整根杆都在左臂够不到的地方：把兵器（右手握点）朝左肩平移刚好够得着的距离，再解一次右臂
        {
          const S = this._Wp[B.upperArmL], R = this._reachL();
          const b = (Gp.x - S.x) * dir.x + (Gp.y - S.y) * dir.y + (Gp.z - S.z) * dir.z;
          const tc = clamp(-b, 0.2, 1.2);
          const cx = Gp.x + dir.x * tc - S.x, cy = Gp.y + dir.y * tc - S.y, cz = Gp.z + dir.z * tc - S.z, dd = Math.hypot(cx, cy, cz);
          if (dd > R) {
            const k = -(dd - R * 0.98) / dd;
            A.G.x += cx * k; A.G.y += cy * k; A.G.z += cz * k;
            A.wR.copy(A.G).sub(T.v6.copy(A.gpR).applyQuaternion(handR));
            this._ik('R', A.wR, handR, P.eR, 0);
            this._fk();
            Gp.copy(A.gpR).applyQuaternion(this._Wq[B.handR]).add(this._Wp[B.handR]).sub(wo);
          }
        }
        const t = this._shaftT(Gp, dir, off[1] * 0.8);
        const wS = A.wS.copy(Gp).addScaledVector(dir, t);
        if (sh >= 0.999) { handL.copy(hS); wristL.copy(wS); }
        else { const k = sh * sh * (3 - 2 * sh); handL.slerp(hS, k); wristL.lerp(wS, k); }
      }
      this._ik('L', wristL, handL, P.eL, P.twL || 0);
    }
    // 左手在杆上的位置（沿杆距右手握点 t 米）：在左臂可及的区间内取最接近 tPref 的值；两手至少相距 0.2 米
    _shaftT(Gp, dir, tPref) {
      const Wp = this._Wp, S = Wp[B.upperArmL];
      const R = this._reachL();
      const rx = Gp.x - S.x, ry = Gp.y - S.y, rz = Gp.z - S.z;
      const b = rx * dir.x + ry * dir.y + rz * dir.z, c = rx * rx + ry * ry + rz * rz - R * R;
      const wi = this.weaponInfo, hiT = wi && wi.base ? Math.max(0.3, wi.base[1] - 0.12) : 0.9, loT = -0.75;
      const disc = b * b - c;
      if (disc < 0) return clamp(-b, 0.2, hiT);              // 够不到：取杆上离肩最近的点（IK 伸直）
      const r0 = -b - Math.sqrt(disc), r1 = -b + Math.sqrt(disc);
      // 候选区间：[0.2, hiT] 与 [loT, −0.2]（左手在右手之后，如反握）
      let best = null, bd = 1e9;
      for (const [a0, a1] of [[0.2, hiT], [loT, -0.2]]) {
        const lo = Math.max(a0, r0), hi = Math.min(a1, r1);
        if (lo > hi) continue;
        const t = clamp(tPref, lo, hi), d = Math.abs(t - tPref) + (a0 < 0 ? 0.15 : 0);
        if (d < bd) { bd = d; best = t; }
      }
      return best != null ? best : clamp(-b, 0.2, hiT);
    }
    // 兵器两端不插入地面：低于地面的一端绕握点抬起，刚好贴地（如跪地时拄着的长兵、起身时的刀尖）
    _weaponAboveGround(G, Qw) {
      const T = tmp();
      if (this._wRange == null) {
        const g = this.tpl.weaponGeo;
        if (g) { if (!g.boundingBox) g.computeBoundingBox(); this._wRange = [g.boundingBox.min.y, g.boundingBox.max.y]; }
        else { const wi = this.weaponInfo || { len: 1 }; this._wRange = [this.style === 'pole' ? -0.85 : -0.12, wi.len || 1]; }
      }
      for (const y0 of this._wRange) {
        const e = T.v5.set(0, y0, 0).applyQuaternion(Qw), L = Math.abs(y0);
        if (L < 0.05) continue;
        const floor = 0.015 - G.y;
        if (e.y >= floor) continue;
        const cur = Math.asin(clamp(e.y / L, -1, 1)), tgt = Math.asin(clamp(floor / L, -1, 1));
        const ax = T.v6.set(-e.z, 0, e.x);            // e × 上：绕此轴正转使 e 抬向上方
        if (ax.lengthSq() < 1e-8) ax.set(1, 0, 0).applyQuaternion(this._Wq[B.root]);
        ax.normalize();
        Qw.premultiply(T.q3.setFromAxisAngle(ax, tgt - cur));
      }
    }
    _reachL() { return (this._armReach || (this._armReach = this._bones[B.foreArmL].position.length() + this._bones[B.handL].position.length())) * 0.985; }
    // 空手 / 副手目标不进入身体（含腰裙外张）：root 局部坐标，就地修改
    _avoidBody(v, P) {
      const k = this.pr.k, y = v.y;
      if (y < 0.5 * k || y > 1.47 * k) return v;
      const r = ringAt(this._R, y);
      const below = clamp01((1.04 * k - y) / (0.5 * k));
      const pad = 0.05 * k + this._skirtFl * k * below + 0.02;
      // 骨盆偏航：在骨盆坐标中判断
      const a = -(P.pel[1] || 0) * D2R, ca = Math.cos(a), sa = Math.sin(a);
      const x = v.x * ca + v.z * sa, z = -v.x * sa + v.z * ca - r.zc;
      // 髋以下：两腿（含裤子外张）比躯干剖面宽
      let rx = r.rx, rz = r.rz;
      if (y < 1.0 * k) { const pr = this.pr, lr = 0.1 * k * pr.limb * 1.22, hk = sstep(1.0 * k, 0.9 * k, y); rx = Math.max(rx, lerp(rx, Math.abs(pr.J.thighL[0]) + lr, hk)); rz = Math.max(rz, lerp(rz, lr, hk)); }
      const ex = rx + pad, ez = rz + pad, q = (x / ex) * (x / ex) + (z / ez) * (z / ez);
      if (q >= 1) return v;
      const f = 1 / Math.sqrt(Math.max(q, 1e-6));
      const nx = x * f, nz = z * f + r.zc;
      v.x = nx * ca - nz * sa; v.z = nx * sa + nz * ca;
      return v;
    }
    // 倒地时兵器平放在身体右侧的地面上：改写右手握点 A.G 与兵器朝向 Qw（按 w 混合）
    _groundWeapon(A, Qw, w) {
      const T = tmp(), Wp = this._Wp, Wq = this._Wq, k = this.pr.k;
      const side = T.v2.set(-1, 0, 0).applyQuaternion(Wq[B.root]); side.y = 0;
      const along = T.v3.set(0, 1, 0).applyQuaternion(Wq[B.root]); along.y = 0;
      if (side.lengthSq() < 1e-4) side.set(-1, 0, 0); side.normalize();
      if (along.lengthSq() < 1e-4) along.set(0, 0, -1); along.normalize();
      const sp = Wp[B.upperArmR];
      const G2 = T.v4.copy(sp).addScaledVector(side, 0.3 * k).addScaledVector(along, -0.18 * k);
      G2.y = 0.045;
      const Y = T.v5.copy(along).addScaledVector(side, 0.22).normalize();
      const Z = T.v6.set(0, 1, 0).cross(Y).normalize();
      const X = T.v1.copy(Y).cross(Z);
      const q = T.q3.setFromRotationMatrix(T.m2.makeBasis(X, Y, Z));
      Qw.slerp(q, w);
      A.G.lerp(G2, w);
    }
    // 披风、前后垂、盔缨、长发：程序化随动（调用 update(dt) 时改用弹簧）
    _secondary(P) {
      const bn = this._bones, s = this._sec;
      const tt = (this._clipT || 0);
      const lean = (P.sp[0] + P.ch[0]) * D2R;
      const capeT = clamp(P.cape || 0, 0, 1.2) * 0.9 + 0.05 + Math.max(0, lean) * 0.6;
      s.tgtCape = capeT;
      s.tgtPlume = -(P.hd[0] + P.ch[0]) * D2R * 0.3 + (P.cape || 0) * 0.35;
      s.tgtHair = Math.max(0, -(P.hd[0]) * D2R * 0.4) + (P.cape || 0) * 0.3;
      const fl = Math.sin(tt * 5.3) * 0.03 + Math.sin(tt * 8.1 + 1) * 0.02 * (P.cape || 0);
      const cape = s.live ? s.cape : capeT;
      const cf = clamp01(P.capeF || 0);
      s.capeF = cf;
      bn[B.cape1].rotation.set(lerp(cape * 0.6 + fl, -0.2, cf), 0, 0);
      bn[B.cape2].rotation.set(lerp(cape * 0.35 + fl * 1.5, -0.12, cf), 0, 0);
      bn[B.cape3].rotation.set(lerp(cape * 0.25 + fl * 2, -0.06, cf), Math.sin(tt * 3.1) * 0.05 * (1 - cf), 0);
      const fwd = Math.min(0, P.tL[0], P.tR[0]) * D2R, back = Math.max(0, P.tL[0], P.tR[0]) * D2R;
      bn[B.skirtF].rotation.set(fwd * 0.75, 0, 0);
      bn[B.skirtB].rotation.set(lerp(back * 0.6 + (P.cape || 0) * 0.15, -0.3, cf), 0, 0);
      const pl = s.live ? s.plume : s.tgtPlume;
      bn[B.plume1].rotation.set(pl + Math.sin(tt * 4.3) * 0.04, 0, Math.sin(tt * 3.3) * 0.04);
      bn[B.plume2].rotation.set(pl * 0.8 + Math.sin(tt * 6.1) * 0.06, 0, 0);
      const hr = s.live ? s.hair : s.tgtHair;
      bn[B.hair1].rotation.set(hr, 0, 0);
    }
    update(dt) {
      const s = this._sec, bn = this._bones;
      if (!(dt > 0)) return this;
      dt = Math.min(dt, 0.05);
      s.live = true;
      const spring = (x, v, tgt, k, d) => { v += ((tgt - x) * k - v * d) * dt; x += v * dt; return [x, v]; };
      [s.cape, s.capeV] = spring(s.cape, s.capeV, s.tgtCape, 60, 9);
      [s.plume, s.plumeV] = spring(s.plume, s.plumeV, s.tgtPlume, 90, 8);
      [s.hair, s.hairV] = spring(s.hair, s.hairV, s.tgtHair, 70, 9);
      s.cape = clamp(s.cape, -0.2, 1.4);
      this._time += dt;
      const fl = Math.sin(this._time * 5.3) * 0.03, cf = s.capeF || 0;
      bn[B.cape1].rotation.x = lerp(s.cape * 0.6 + fl, -0.2, cf);
      bn[B.cape2].rotation.x = lerp(s.cape * 0.35 + fl * 1.5, -0.12, cf);
      bn[B.cape3].rotation.x = lerp(s.cape * 0.25 + fl * 2, -0.06, cf);
      bn[B.plume1].rotation.x = s.plume + Math.sin(this._time * 4.3) * 0.04;
      bn[B.plume2].rotation.x = s.plume * 0.8;
      bn[B.hair1].rotation.x = s.hair;
      return this;
    }
    // 直接设定右手兵器：pos（root 局部）、方向（Vector3 或四元数）、横滚（度）
    grip(pos, dirOrQuat, roll) {
      const P = this._P;
      P.g[0] = pos.x; P.g[1] = pos.y; P.g[2] = pos.z;
      if (dirOrQuat && dirOrQuat.isQuaternion) {
        const e = new THREE.Euler().setFromQuaternion(dirOrQuat, 'YXZ');
        P.w[0] = e.y / D2R; P.w[1] = e.x / D2R; P.w[2] = e.z / D2R;
      } else if (dirOrQuat) {
        const d = dirOrQuat.clone().normalize();
        P.w[0] = Math.atan2(d.x, d.z) / D2R; P.w[1] = Math.acos(clamp(d.y, -1, 1)) / D2R; P.w[2] = roll || 0;
      }
      this._arms(P);
      return this;
    }
    ikHand(side, pos, quat) {
      this._fk();
      const S = side === 'L' ? 'L' : 'R';
      const q = quat || new THREE.Quaternion().copy(this._Wq[B['hand' + S]]);
      this._ik(S, pos, q, S === 'L' ? [0.8, -0.5, -0.3] : [-0.8, -0.5, -0.3], 0);
      return this;
    }
    attach(obj, where) {
      const t = typeof where === 'string' ? (this.sockets[where] || this.bones[where]) : where;
      (t || this.root).add(obj);
      return obj;
    }
    detach(obj) { if (obj && obj.parent) obj.parent.remove(obj); return obj; }
    // 兵器网格（LP 材质 + 墨线外壳）：右手握点（弓：左手）；双持时左手一把同样的
    _makeWeaponMeshes(geo) {
      const L = LPX(), side = this.style === 'bow' ? 'L' : 'R';
      for (const m of [this.weaponMesh, this.weaponMesh2]) if (m && m.parent) m.parent.remove(m);
      this.weaponMesh = L.mesh(geo, { castShadow: this._shadow, receiveShadow: false });
      this.weaponMesh.name = 'weapon';
      this.sockets['grip' + side].add(this.weaponMesh);
      this.weaponMesh2 = null;
      if (this.style === 'dual') {
        this.weaponMesh2 = L.mesh(geo, { castShadow: this._shadow, receiveShadow: false });
        this.weaponMesh2.name = 'weapon2';
        this.sockets.gripL.add(this.weaponMesh2);
      }
      const body = [this.mesh, this.mesh.userData.outline];
      this.meshes = body.concat([this.weaponMesh, this.weaponMesh2].filter(Boolean).reduce((a, m) => a.concat([m, m.userData.outline]), [])).filter(Boolean);
      this.drawCalls = this.meshes.length;
      if (this._look) for (const m of [this.weaponMesh, this.weaponMesh2]) if (m) attachLook(m, this._look);
    }
    setWeapon(name) {
      if (!WEAPONS[name] || this.kind === 'soldier') return this;
      const det = Math.min(WDET[this.tpl.lod] != null ? WDET[this.tpl.lod] : 2, this.tpl.D ? this.tpl.D.det : 2);
      const w = weaponGeo(name, det);
      this.weaponInfo = w.info;
      this.style = WEAPONS[name].style;
      this._wRange = w.range;
      this._makeWeaponMeshes(w.geo);
      this.setPose(this._clipName || 'idle', this._clipT || 0);
      return this;
    }
    weaponTip(out) {
      out = out || new THREE.Vector3();
      const wi = this.weaponInfo, m = this.weaponMesh;
      const tip = wi ? wi.tip : [0, 1, 0];
      out.set(tip[0], tip[1], tip[2]);
      if (m) { m.updateWorldMatrix(true, false); return out.applyMatrix4(m.matrixWorld); }
      const s = this.sockets[this.style === 'bow' ? 'gripL' : 'gripR'];
      s.updateWorldMatrix(true, false);
      return out.applyMatrix4(s.matrixWorld);
    }
    weaponBase(out) {
      out = out || new THREE.Vector3();
      const wi = this.weaponInfo, base = wi ? wi.base : [0, 0.1, 0];
      out.set(base[0], base[1], base[2]);
      const s = this.weaponMesh || this.sockets[this.style === 'bow' ? 'gripL' : 'gripR'];
      s.updateWorldMatrix(true, false);
      return out.applyMatrix4(s.matrixWorld);
    }
    // 受击闪光、格开的冷光、反击窗口的金光……：LP 外观（共用材质，逐物体写入），以墨线变色加粗为主，正面少量闪色
    setTint(hex, k) {
      tintLook(this._look, hex, k, 1);
      return this;
    }
    // 显示 / 隐藏（含兵器网格；骑在马上时随马）
    get visible() { return this.root.visible; }
    set visible(v) { this.root.visible = !!v; }
    dispose() {
      if (this._disposed) return;
      this._disposed = true;
      if (this.root.parent) this.root.parent.remove(this.root);
      if (this.mounted) this.mounted.dismount(this);
      this.skeleton.dispose();
      releaseTemplate(this.tpl);
    }
  }
  // 着色外观（第三版的 setTint 是「自发光 = 颜色 × k」，黑色 = 无效果）：强度 = k × 颜色的最大分量，色相 = 归一化的颜色。
  // 外观对象常驻（见 attachLook）：强度 > 0 时墨线变成该色并加粗（辉光轮廓）、正面少量闪色、rim 提亮；强度 = 0 时这些字段归零。
  // 单挑每帧调用（颜色常为黑 = 关闭）。
  function tintLook(look, hex, k, flashK) {
    const L = LPX();
    const c = col(hex == null ? '#ffffff' : hex);
    const mx = Math.max(c[0], c[1], c[2]);
    const s = clamp01((+k || 0) * Math.min(1.5, mx));
    if (s <= 0.004) { look.flash = 0; look.ink = null; look.inkWidth = null; look.rim = 0; return look; }
    const h = [c[0] / mx, c[1] / mx, c[2] / mx];
    look.flash = 0.3 * s * (flashK == null ? 1 : flashK);
    look.flashColor = h;
    const ink = L.INK, ki = Math.min(1, s * 1.4);
    look.ink = [lerp(ink[0], h[0], ki), lerp(ink[1], h[1], ki), lerp(ink[2], h[2], ki)];
    look.inkWidth = 1 + 1.4 * s;
    look.rim = 0.6 * s;
    return look;
  }
  // 没有 LP 氛围（SG.LP.Mood）的场景：沿用旧版物理比例的灯（强度 × π，如攻击画面、单挑画面自建的太阳 / 天光）。LP 着色器把
  // 灯的强度当作 Lanternvale 的线性值，会亮约 π 倍、发白；这里按场景的灯判断，人物与士兵群的反照率乘 1/π（= 旧版 Lambert 的曝光），
  // 有氛围（全局覆盖量生效）或灯本身就是 Lanternvale 量级的场景不变。各场景接入 SG.Gfx.newMood 后自动失效。
  const LEGACY_TINT = [0.6, 0.6, 0.6];        // sRGB 0.6 = 线性 1/π（外观的 tint 按 sRGB 解码）
  const LEGACY_K = 1 / Math.PI;
  function sceneLightK(renderer, scene) {
    const L = SG.LP;
    if (!L || !scene || L.globals.lpSunC.value.w > 0.5) return 1;
    const ud = scene.userData, fr = renderer && renderer.info ? renderer.info.render.frame : 0;
    let c = ud.__sgLightK;
    if (c && c.f === fr) return c.k;
    if (!c || !c.lights || (fr - c.scan) > 90 || fr < c.scan) {
      const lights = [];
      scene.traverse(o => { if (o.isLight && (o.isDirectionalLight || o.isHemisphereLight || o.isAmbientLight)) lights.push(o); });
      c = ud.__sgLightK = { f: -1, k: 1, lights, scan: fr };
    }
    let sun = 0, amb = 0, ds = false, hs = false;
    for (const o of c.lights) {
      if (!o.visible) continue;
      const m = Math.max(o.color.r, o.color.g, o.color.b) * o.intensity;
      if (o.isDirectionalLight) { if (!ds) { sun = m; ds = true; } }
      else if (o.isHemisphereLight) { if (!hs) { amb += m; hs = true; } }
      else amb += m;
    }
    c.f = fr; c.k = sun + amb > 2.6 ? LEGACY_K : 1;
    return c.k;
  }
  // 常驻外观 + 场景曝光补偿：LP.setLook 之后包一层 onBeforeRender（先按场景定 tint，再交给 LP 写入共用材质）
  function attachLook(mesh, look) {
    if (!mesh) return;
    LPX().setLook(mesh, look);
    const lb = mesh.onBeforeRender;
    mesh.onBeforeRender = function (r, sc, cam, g, m) { look.tint = sceneLightK(r, sc) === 1 ? null : LEGACY_TINT; lb.call(this, r, sc, cam, g, m); };
  }
  function blendPoses(P, Q, w) {
    // P = lerp(P, Q, w)
    for (const k of FIELDS3) { const a = P[k], b = Q[k]; if (P[k + 'N'] && !Q[k + 'N']) { a[0] = b[0]; a[1] = b[1]; a[2] = b[2]; P[k + 'N'] = false; continue; } if (Q[k + 'N']) continue; a[0] = lerp(a[0], b[0], w); a[1] = lerp(a[1], b[1], w); a[2] = lerp(a[2], b[2], w); }
    for (const k of FIELDS1) P[k] = lerp(P[k] || 0, Q[k] || 0, w);
    return P;
  }


  // ======================================================== 战马实例 ==
  const HTPL = new Map();
  class Horse {
    constructor(opts) {
      opts = opts || {};
      const lod = HLOD[opts.lod] ? opts.lod : 'mid';
      const crowdMode = !!opts._crowd;
      const key = [lod, opts.coat, opts.mane, opts.armor, opts.seed | 0, opts.name, !!opts.general, opts.metal, opts.plume, crowdMode ? '*' : (opts.color || '#8a2a24')].join('|');
      let t = HTPL.get(key);
      if (!t) {
        const t0 = now();
        const J = horseJoints(1);
        const M = buildHorseLP(Object.assign({}, opts, { lod }), crowdMode ? 'crowd' : 'single');
        const geometry = M.toGeometry({ skinned: true });
        t = { key, geometry, J, k: 1, refs: 0, tris: geometry.userData.tris, inv: HBONES.map(n => new THREE.Matrix4().makeTranslation(-J[n][0], -J[n][1], -J[n][2])), crowd: crowdMode };
        t.ms = now() - t0;
        STATS.buildMs += t.ms; STATS.templates++;
        STATS.tris['horse@' + lod] = t.tris;
        HTPL.set(key, t);
      }
      t.refs++;
      this.tpl = t;
      this.kind = 'horse';
      this.root = new THREE.Group(); this.root.name = 'Horse';
      const bones = HBONES.map(n => { const b = new THREE.Bone(); b.name = n; return b; });
      HBONES.forEach((n, i) => { const p = HPARENT[n], J = t.J[n], PJ = p ? t.J[p] : [0, 0, 0]; bones[i].position.set(J[0] - PJ[0], J[1] - PJ[1], J[2] - PJ[2]); if (p) bones[HB[p]].add(bones[i]); });
      this._bones = bones; this.bones = {}; HBONES.forEach((n, i) => { this.bones[n] = bones[i]; });
      this.restPos = bones.map(b => b.position.clone());
      this.skeleton = new THREE.Skeleton(bones, t.inv);
      const L = LPX();
      // 骨架挂在 root 下：马的外观（setTint）不影响骑在鞍座骨上的人物
      this.root.add(bones[0]);
      this.mesh = L.skinnedMesh(t.geometry, this.skeleton, { castShadow: opts.shadow !== false, receiveShadow: false });
      this.mesh.name = 'horse';
      this.root.add(this.mesh);
      this.meshes = [this.mesh, this.mesh.userData.outline].filter(Boolean);
      if (opts.blob) {
        this.blob = L.blobShadow(0.58, 1.3, 0.5, 0.55);
        Object.defineProperty(this.blob, 'castShadow', { get: () => false, set: () => {}, configurable: true });
        this.root.add(this.blob);
      }
      this.sockets = { saddle: this.bones.saddle };
      this.riders = [];
      this.triangles = t.tris; this.drawCalls = this.meshes.length;
      this._look = {};
      attachLook(this.mesh, this._look);
      STATS.built++;
      this.setPose('idle', 0);
    }
    clipInfo(n) { return HCLIPS[n] || null; }
    setPose(name, t) {
      const c = HCLIPS[name] || HCLIPS.idle;
      const tt = c.loop ? (((t || 0) % c.dur) + c.dur) % c.dur : clamp(t || 0, 0, c.dur);
      horsePose(this, name in HCLIPS ? name : 'idle', tt, c.dur);
      // 骑手同步
      for (const f of this.riders) if (f._clipName) f.setPose(f._clipName, f._clipT, { gallop: name === 'gallop' });
      return this;
    }
    update() { return this; }
    mount(fig) {
      if (!fig || this.riders.includes(fig)) return this;
      this.bones.saddle.add(fig.root);
      fig.root.position.set(0, -fig.pr.J.pelvis[1] + 0.08, -0.04);
      fig.root.rotation.set(0, 0, 0);
      fig.mounted = this;
      this.riders.push(fig);
      fig.setPose(fig._clipName || 'idle', fig._clipT || 0);
      return this;
    }
    dismount(fig) {
      const i = this.riders.indexOf(fig);
      if (i >= 0) this.riders.splice(i, 1);
      if (fig.root.parent === this.bones.saddle) this.bones.saddle.remove(fig.root);
      fig.mounted = null;
      return this;
    }
    attach(obj, where) { const t = typeof where === 'string' ? (this.sockets[where] || this.bones[where]) : where; (t || this.root).add(obj); return obj; }
    detach(obj) { if (obj && obj.parent) obj.parent.remove(obj); return obj; }
    setTint(hex, k) { tintLook(this._look, hex, k, 1); return this; }
    dispose() {
      if (this._disposed) return;
      this._disposed = true;
      for (const f of this.riders.slice()) this.dismount(f);
      if (this.root.parent) this.root.parent.remove(this.root);
      this.skeleton.dispose();
      const t = this.tpl;
      if (--t.refs <= 0 && !t.crowd) { HTPL.delete(t.key); t.geometry.dispose(); }
    }
  }
  const HCLIPS = { idle: { dur: 3.0, loop: true, desc: '站立' }, walk: { dur: 1.1, loop: true, desc: '慢步' }, trot: { dur: 0.7, loop: true, desc: '快步' },
    gallop: { dur: 0.5, loop: true, desc: '袭步' }, rear: { dur: 1.6, loop: false, desc: '人立' }, die: { dur: 1.5, loop: false, desc: '倒地' } };
  function horsePose(h, name, t, dur) {
    const b = h.bones, ph = t / dur * TAU, rp = h.restPos;
    const set = (n, x, y, z) => b[n].rotation.set((x || 0) * D2R, (y || 0) * D2R, (z || 0) * D2R);
    for (const n of HBONES) b[n].rotation.set(0, 0, 0);
    b.body.position.copy(rp[HB.body]);
    b.root.rotation.set(0, 0, 0); b.root.position.set(0, 0, 0);
    const leg = (L, ang, knee, fet) => { set('up' + L, ang); set('lo' + L, knee); set('ft' + L, fet); };
    if (name === 'idle') {
      const s = Math.sin(ph);
      set('neck1', -4 + s * 2); set('neck2', 6 + s * 2); set('head', 14 - s * 2); set('tail1', 2, Math.sin(ph * 2) * 10); set('tail2', 2, Math.sin(ph * 2 + 1) * 12);   // 尾自然下垂（试点 TAIL_REST）
      b.body.position.y += Math.sin(ph * 2) * 0.004;
      leg('FL', 0, 0, 0); leg('FR', 0, 0, 0); leg('HL', 0, 0, 0); leg('HR', 4, -6, 4);
      return;
    }
    if (name === 'walk' || name === 'trot' || name === 'gallop') {
      const g = name === 'gallop', tr = name === 'trot';
      const off = g ? { HL: 0, HR: 0.1, FR: 0.42, FL: 0.52 } : tr ? { FL: 0, HR: 0, FR: 0.5, HL: 0.5 } : { HL: 0, FL: 0.25, HR: 0.5, FR: 0.75 };
      const A = g ? 42 : tr ? 26 : 18;
      for (const L of ['FL', 'FR', 'HL', 'HR']) {
        const p = ph - off[L] * TAU, s = Math.sin(p), c = Math.cos(p);
        const lift = Math.max(0, c);
        if (L[0] === 'F') leg(L, -s * A + (g ? -6 : 0), lift * (g ? 95 : tr ? 70 : 45), -lift * (g ? 30 : 20) + 5);
        else leg(L, -s * A * 0.9 + (g ? 6 : 0), -lift * (g ? 60 : tr ? 40 : 25), lift * (g ? 50 : 30));
      }
      const bob = g ? 0.06 : tr ? 0.035 : 0.015;
      b.body.position.y += Math.abs(Math.sin(ph * (g ? 1 : 2))) * bob - bob * 0.5;
      set('body', g ? Math.sin(ph) * 5 : 0);
      set('neck1', g ? -10 + Math.sin(ph + 1) * 8 : -6 + Math.sin(ph * 2) * 3); set('neck2', g ? 4 : 6); set('head', g ? 10 : 14 + Math.sin(ph * 2) * 3);
      set('tail1', g ? 45 : 22, Math.sin(ph) * 8); set('tail2', g ? 25 : 18, Math.sin(ph + 1) * 10);
      if (name === 'walk') { b.tail1.rotation.x -= 18 * D2R; b.tail2.rotation.x -= 14 * D2R; }
      return;
    }
    if (name === 'rear') {
      const k = sstep(0, 0.45, t) * (1 - sstep(1.15, 1.6, t));
      b.root.rotation.x = -k * 42 * D2R;
      b.root.position.z = -k * 0.5; b.root.position.y = k * 0.25;
      leg('FL', -k * 60, k * 110, -k * 30); leg('FR', -k * 40, k * 95, -k * 30);
      leg('HL', k * 38, -k * 20, k * 10); leg('HR', k * 30, -k * 15, k * 10);
      set('neck1', -k * 20); set('head', k * 30); set('tail1', 10 + k * 30);
      return;
    }
    if (name === 'die') {
      const k = sstep(0.2, 1.1, t), kk = sstep(0, 0.4, t);
      b.root.rotation.z = k * 82 * D2R; b.root.position.y = k * 0.05; b.root.position.x = -k * 0.25;
      leg('FL', -kk * 30 + k * 20, kk * 90 - k * 40, 0); leg('FR', -kk * 20, kk * 60, 0); leg('HL', kk * 20, -kk * 30, 0); leg('HR', k * 10, -k * 10, 0);
      set('neck1', k * 30, 0, -k * 20); set('head', k * 20); set('tail1', 10);
    }
  }

  // ======================================================== 士兵群（实例化） ==
  // 同一模板（兵种 × 文化 × 外观变体 × LOD）的所有士兵 = 一个 InstancedMesh（一次绘制，投影再一次）：
  // 动作预先烘焙成骨骼矩阵贴图（每帧一行，每骨 3 个 RGBA 浮点像素 = 3×4 矩阵），顶点着色器按实例的
  // 片段起始行 / 帧数 / 起始时间 / 速度取两帧插值后蒙皮；势力色为实例属性（同一模板任意势力共用一次绘制）。
  // 骑兵：骑手与战马各自成组，同一实例矩阵；骑手的动作在战马坐标中烘焙（随马背起伏）。
  const BAKE_FPS = 24;
  const CROWD_CLIPS = ['idle', 'walk', 'run', 'light1', 'light2', 'heavy', 'block', 'hit', 'knockdown', 'down', 'die', 'cheer', 'bowDraw', 'bowShoot'];
  // 骑兵组合：名称 → [战马片段, 骑手片段, 时长, 循环]
  // rt：骑手上半身取固定时刻（静止姿势）；缺省时骑手片段铺满整段
  const MOUNT_CLIPS = { idle: ['idle', 'idle', 2.4, true], walk: ['walk', 'idle', 1.1, true, 0.3], trot: ['trot', 'idle', 0.7, true, 0.3], gallop: ['gallop', 'idle', 1.0, true, 0.3],
    attack: ['trot', 'light1', 0.7, false], charge: ['gallop', 'light3', 1.0, true, 0.27], hit: ['idle', 'hit', 0.6, false], die: ['die', 'hit', 1.5, false] };
  const BAKES = new Map();
  // 烘焙：poser(clip, t) 摆好姿势后，读取 bones[i].matrixWorld × inv[i]（容器根在原点）
  function drain(gen) { let r; do { r = gen.next(); } while (!r.done); return r.value; }
  function bakeFrames(list, bones, inv, poser, rootObj) { return drain(bakeFramesGen(list, bones, inv, poser, rootObj)); }
  function* bakeFramesGen(list, bones, inv, poser, rootObj) {
    const nb = bones.length;
    let rows = 0;
    const table = {};
    // 循环片段多存一行（= 第 0 帧），硬件线性插值跨越首尾时无缝
    for (const c of list) { const n = Math.max(2, Math.round(c.dur * BAKE_FPS) + (c.loop ? 0 : 1)); table[c.name] = { row: rows, n, dur: c.dur, loop: c.loop }; rows += n + (c.loop ? 1 : 0); }
    const W = nb * 3, data = new Float32Array(W * rows * 4);
    const m = new THREE.Matrix4();
    for (const c of list) {
      const e = table[c.name];
      for (let f = 0; f < e.n + (c.loop ? 1 : 0); f++) {
        const t = c.loop ? (f % e.n) / e.n * c.dur : Math.min(c.dur, f / (e.n - 1) * c.dur);
        poser(c.name, t);
        rootObj.updateMatrixWorld(true);
        for (let i = 0; i < nb; i++) {
          m.multiplyMatrices(bones[i].matrixWorld, inv[i]);
          const me = m.elements, o = ((e.row + f) * W + i * 3) * 4;
          // 三行：(m00 m01 m02 m03) (m10 …) (m20 …)；elements 为列主序
          data[o] = me[0]; data[o + 1] = me[4]; data[o + 2] = me[8]; data[o + 3] = me[12];
          data[o + 4] = me[1]; data[o + 5] = me[5]; data[o + 6] = me[9]; data[o + 7] = me[13];
          data[o + 8] = me[2]; data[o + 9] = me[6]; data[o + 10] = me[10]; data[o + 11] = me[14];
        }
        if (f % 10 === 9) yield;
      }
      yield;
    }
    // 半精度浮点（WebGL2 核心支持线性过滤）：帧间插值由纹理单元完成，每个骨骼矩阵只取 3 个像素
    const half = new Uint16Array(data.length), toH = THREE.DataUtils.toHalfFloat;
    for (let i = 0; i < data.length; i++) { half[i] = toH(data[i]); if ((i & 65535) === 65535) yield; }
    const tex = new THREE.DataTexture(half, W, rows, THREE.RGBAFormat, THREE.HalfFloatType);
    tex.minFilter = tex.magFilter = THREE.LinearFilter; tex.generateMipmaps = false; tex.needsUpdate = true;
    STATS.textures++;
    return { tex, table, rows, W, bytes: half.byteLength };
  }
  function bakeSoldier(look, key, tpl) { return BAKES.get('S|' + key) || drain(bakeSoldierGen(look, key, tpl)); }
  function* bakeSoldierGen(look, key, tpl) {
    const bk = 'S|' + key;
    let b = BAKES.get(bk);
    if (b) return b;
    let ms = 0, t0 = now();
    const f = new Figure(tpl, { shadow: false });
    tpl.refs++;
    const list = CROWD_CLIPS.map(n => ({ name: n, dur: CL[n].dur, loop: CL[n].loop }));
    const g = bakeFramesGen(list, f._bones, tpl.boneInverses, (c, t) => f.setPose(c, t), f.root);
    for (;;) { const r = g.next(); if (r.done) { b = r.value; break; } ms += now() - t0; yield; t0 = now(); }
    f.dispose();
    b.ms = ms + now() - t0; STATS.bakeMs = (STATS.bakeMs || 0) + b.ms;
    BAKES.set(bk, b);
    return b;
  }
  function bakeMounted(look, key, tpl, hopts, hkey) { return BAKES.get('M|' + key + '|' + hkey) || drain(bakeMountedGen(look, key, tpl, hopts, hkey)); }
  function* bakeMountedGen(look, key, tpl, hopts, hkey) {
    const bk = 'M|' + key + '|' + hkey;
    let b = BAKES.get(bk);
    if (b) return b;
    const t0 = now();
    const f = new Figure(tpl, { shadow: false });
    tpl.refs++;
    const h = new Horse(Object.assign({}, hopts, { shadow: false, _crowd: true }));
    h.mount(f);
    h.root.updateMatrixWorld(true);
    const list = Object.keys(MOUNT_CLIPS).map(n => ({ name: n, dur: MOUNT_CLIPS[n][2], loop: MOUNT_CLIPS[n][3] }));
    // 循环组合的时长取各循环周期的整数倍：战马片段直接用 t；骑手上半身 / 下半身步态按整周期缩放，首尾无缝
    const fit = (t, D, d) => t / D * d * Math.max(1, Math.round(D / d));
    const pose = (c, t) => {
      const M = MOUNT_CLIPS[c], D = M[2];
      h.setPose(M[0], M[3] ? t : Math.min(t, HCLIPS[M[0]].dur));
      const gal = M[0] === 'gallop', gd = CL[gal ? 'gallop' : 'ride'].dur;
      const rc = CL[M[1]], rt = M[4] != null ? M[4] : rc.loop ? fit(t, D, rc.dur) : Math.min(rc.dur, t / D * rc.dur);
      f.setPose(M[1], rt, { gallop: gal, lowerT: fit(t, D, gd) });
    };
    const rider = yield* bakeFramesGen(list, f._bones, tpl.boneInverses, pose, h.root);
    const horseB = yield* bakeFramesGen(list, h._bones, h.tpl.inv, pose, h.root);
    h.dispose(); f.dispose();
    b = { rider, horse: horseB, horseTpl: null, ms: now() - t0 };
    STATS.bakeMs = (STATS.bakeMs || 0) + b.ms;
    BAKES.set(bk, b);
    return b;
  }

  // 实例化材质：LP 光照着色器（与单个人物同一套 Lanternvale 光照）+ 烘焙蒙皮 + 实例势力色（只乘在 lpTeam 遮罩的顶点上）；
  // 墨线外壳同样实例化（共用实例矩阵与动作属性）
  const CROWD_VS_PARS = [
    'uniform sampler2D uSgBake;', 'uniform vec2 uSgSize;', 'uniform float uSgTime;', 'uniform float uSgFps;', 'uniform float uSgGround;',
    'attribute vec4 skinIndex;', 'attribute vec4 skinWeight;', 'attribute vec4 aSgAnim;',
    'mat4 sgBone( float v, float bi ) {',
    '  float dx = 1.0 / uSgSize.x, x = ( floor( bi + 0.5 ) * 3.0 + 0.5 ) * dx;',
    '  vec4 r0 = texture2D( uSgBake, vec2( x, v ) ), r1 = texture2D( uSgBake, vec2( x + dx, v ) ), r2 = texture2D( uSgBake, vec2( x + dx * 2.0, v ) );',
    '  return mat4( r0.x, r1.x, r2.x, 0.0, r0.y, r1.y, r2.y, 0.0, r0.z, r1.z, r2.z, 0.0, r0.w, r1.w, r2.w, 1.0 );',
    '}',
    'mat4 sgSkinMat() {',
    '  float n = abs( aSgAnim.y );',
    '  float f = max( 0.0, ( uSgTime - aSgAnim.z ) * uSgFps * aSgAnim.w );',
    '  f = aSgAnim.y > 0.0 ? mod( f, n ) : min( f, n - 1.0 );',
    '  float v = ( aSgAnim.x + f + 0.5 ) / uSgSize.y;',
    '  return sgBone( v, skinIndex.x ) * skinWeight.x;',
    '}'].join('\n');
  // 刚性蒙皮（每顶点一骨，权重 1）：只取 skinIndex.x
  function crowdVertex(src, team) {
    const pars = CROWD_VS_PARS + (team ? '\nattribute float lpTeam;\nattribute vec3 aSgTeam;\nvarying vec3 vSgTeam;' : '');
    const s = src.replace('#include <skinning_pars_vertex>', pars)
      .replace('#include <skinbase_vertex>', 'mat4 sgSkin = sgSkinMat();' + (team ? '\nvSgTeam = mix( vec3( 1.0 ), aSgTeam, lpTeam );' : ''))
      .replace('#include <skinnormal_vertex>', 'objectNormal = ( sgSkin * vec4( objectNormal, 0.0 ) ).xyz;')
      .replace('#include <skinning_vertex>', 'transformed = ( sgSkin * vec4( transformed, 1.0 ) ).xyz;\nif ( uSgGround > 0.5 ) transformed.y = max( transformed.y, 0.006 );');
    if (s.indexOf('sgSkinMat()') < 0 || s.indexOf('sgSkin * vec4( transformed') < 0) throw new Error('SG.Figure：士兵群着色器补丁失败（LP 着色器结构变了）');
    return s;
  }
  function crowdUniforms(bake, ground) {
    return { uSgBake: { value: bake.tex }, uSgSize: { value: new THREE.Vector2(bake.W, bake.rows) }, uSgTime: { value: 0 }, uSgFps: { value: BAKE_FPS }, uSgGround: { value: ground ? 1 : 0 } };
  }
  // 从 LP 的共用材质派生：同一套着色器源码与全局量（氛围、风、视口）共用同一组 uniform 对象
  function lpDerived(base, U, vs, fs, name) {
    const L = LPX(), G = L.globals;
    const u = THREE.UniformsUtils.clone(base.uniforms);
    for (const k in G) if (k in u) u[k] = G[k];
    Object.assign(u, U);
    const m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: vs, fragmentShader: fs, lights: base.lights, fog: base.fog, vertexColors: base.vertexColors, side: base.side });
    m.defines = Object.assign({}, base.defines || {});
    m.name = name;
    return m;
  }
  function crowdMaterial(U) {
    const base = LPX().material({});
    const fs = 'varying vec3 vSgTeam;\n' + base.fragmentShader.replace('vec3 albedo = lpLin( vColor.rgb ) * uTint;', 'vec3 albedo = lpLin( vColor.rgb * vSgTeam ) * uTint;');
    if (fs.indexOf('vColor.rgb * vSgTeam') < 0) throw new Error('SG.Figure：士兵群着色器补丁失败（LP 片元着色器结构变了）');
    return lpDerived(base, U, crowdVertex(base.vertexShader, true), fs, 'SG.Figure.crowd');
  }
  function crowdOutlineMaterial(U) {
    const base = LPX().outlineMaterial({});
    return lpDerived(base, U, crowdVertex(base.vertexShader, false), base.fragmentShader, 'SG.Figure.crowdOutline');
  }
  function crowdDepthMaterial(U) {
    const m = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
    m.onBeforeCompile = function (sh) { Object.assign(sh.uniforms, U); sh.vertexShader = crowdVertex(sh.vertexShader, false); };
    m.customProgramCacheKey = () => 'sgcrowd-lp-depth';
    return m;
  }

  // 一组（同一几何体 + 同一烘焙）：本体 + 墨线两个 InstancedMesh，共用实例矩阵与实例属性
  class CrowdGroup {
    constructor(crowd, geo, bake, cap, ground, name) {
      this.crowd = crowd; this.bake = bake; this.cap = cap; this.count = 0; this.ids = [];
      const g = new THREE.BufferGeometry();
      for (const k of ['position', 'normal', 'color', 'lpWind', 'lpSmooth', 'skinIndex', 'skinWeight', 'lpTeam']) if (geo.attributes[k]) g.setAttribute(k, geo.attributes[k]);
      if (!geo.attributes.lpTeam) g.setAttribute('lpTeam', new THREE.BufferAttribute(new Uint8Array(geo.attributes.position.count), 1, true));
      g.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 1, 0), 2.5);
      this.anim = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4); this.anim.setUsage(THREE.DynamicDrawUsage);
      this.team = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3); this.team.setUsage(THREE.DynamicDrawUsage);
      g.setAttribute('aSgAnim', this.anim); g.setAttribute('aSgTeam', this.team);
      this.geo = g;
      this.U = crowdUniforms(bake, ground);
      this.U.uSgTime = crowd._time;     // 所有组共用同一时钟
      this.mat = crowdMaterial(this.U);
      this.outMat = crowdOutlineMaterial(this.U);
      this.depthMat = crowd.shadow ? crowdDepthMaterial(this.U) : null;
      this.name = name;
      this._makeMeshes(cap, null);
      this.tris = geo.userData.tris || geo.attributes.position.count / 3;
    }
    _makeMeshes(cap, old) {
      const m = new THREE.InstancedMesh(this.geo, this.mat, cap);
      m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      if (old) m.instanceMatrix.array.set(old.instanceMatrix.array);
      m.count = this.count; m.frustumCulled = false; m.name = 'Crowd:' + this.name;
      m.castShadow = this.crowd.shadow; m.receiveShadow = false;
      const mat = this.mat;
      m.onBeforeRender = (r, sc) => { const kk = sceneLightK(r, sc); if (mat.uniforms.uTint.value.x !== kk) { mat.uniforms.uTint.value.setScalar(kk); mat.uniformsNeedUpdate = true; } };
      if (this.depthMat) m.customDepthMaterial = this.depthMat;
      const o = new THREE.InstancedMesh(this.geo, this.outMat, cap);
      o.instanceMatrix = m.instanceMatrix;
      o.count = this.count; o.frustumCulled = false; o.castShadow = false; o.receiveShadow = false; o.name = 'outline';
      o.raycast = () => {};
      m.add(o);
      m.userData.outline = o;
      if (old) { this.crowd.root.remove(old); }
      this.crowd.root.add(m);
      this.mesh = m; this.outline = o;
    }
    grow() {
      const cap = this.cap * 2;
      const a = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4); a.array.set(this.anim.array); a.setUsage(THREE.DynamicDrawUsage);
      const t = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3); t.array.set(this.team.array); t.setUsage(THREE.DynamicDrawUsage);
      this.geo.setAttribute('aSgAnim', a); this.geo.setAttribute('aSgTeam', t); this.anim = a; this.team = t;
      const old = this.mesh;
      this.cap = cap;
      this._makeMeshes(cap, old);
      if (old.dispose) old.dispose();
    }
    _setCount() { this.mesh.count = this.count; this.outline.count = this.count; }
    alloc(id) { if (this.count >= this.cap) this.grow(); const i = this.count++; this.ids[i] = id; this._setCount(); return i; }
    free(i) {
      const last = --this.count;
      if (i !== last) {
        const M = this.mesh.instanceMatrix.array; M.copyWithin(i * 16, last * 16, last * 16 + 16);
        this.anim.array.copyWithin(i * 4, last * 4, last * 4 + 4); this.team.array.copyWithin(i * 3, last * 3, last * 3 + 3);
        const mv = this.ids[last]; this.ids[i] = mv; this.crowd._moved(mv, this, i);
      }
      this.ids.length = this.count; this._setCount();
      this.mesh.instanceMatrix.needsUpdate = true; this.anim.needsUpdate = true; this.team.needsUpdate = true;
    }
    dispose() { this.crowd.root.remove(this.mesh); this.geo.dispose(); this.mat.dispose(); this.outMat.dispose(); if (this.depthMat) this.depthMat.dispose(); }
  }

  const _cm = () => new THREE.Matrix4();
  class Crowd {
    constructor(opts) {
      opts = opts || {};
      this.lod = opts.lod || 'low';
      this.shadow = opts.shadow !== false;
      this.cap = opts.capacity || 32;
      this.root = new THREE.Group(); this.root.name = 'SG.Figure.crowd';
      this._time = { value: 0 };
      this.groups = new Map(); this.items = new Map(); this._next = 1;
      const rr = opts.renderer || (SG.Gfx && SG.Gfx.renderer);
      this.instanced = !(rr && rr.capabilities && rr.capabilities.isWebGL2 === false) && opts.instanced !== false;
      this._m = _cm(); this._q = new THREE.Quaternion(); this._s = new THREE.Vector3(); this._p = new THREE.Vector3(); this._Y = new THREE.Vector3(0, 1, 0);
    }
    get time() { return this._time.value; }
    // 预先烘焙将要加入的士兵模板（分片，不卡主线程）→ Promise；之后 add() 不再同步烘焙
    prepare(specs, budget) { return runJobs(specs.map(o => ({ gen: bakeCrowdGen(o, this.lod) })), budget || 12); }
    // 全局时钟（秒），每帧调用一次
    setTime(t) { this._time.value = t; if (!this.instanced) for (const it of this.items.values()) this._poseFallback(it); return this; }
    _group(key, mk) { let g = this.groups.get(key); if (!g) { g = mk(); this.groups.set(key, g); } return g; }
    // 加入一名士兵：{ type, culture, color, seed, mounted（骑兵带马）, horse: { coat, armor, seed } }
    add(o) {
      o = o || {};
      const id = this._next++;
      const look = lookOfSoldier(Object.assign({ lod: this.lod }, o, { lod: this.lod, type: o.mounted ? 'cavalry' : o.type }));
      const key = crowdKey(look);
      const it = { id, mounted: !!o.mounted, color: teamTone(o.color || '#8a2a24'), parts: [], clip: 'idle', t0: 0, speed: 1, m: _cm(), o };
      if (!this.instanced) return this._addFallback(it, o, id);
      const tpl = getTemplate(look, key, 'crowd');
      if (o.mounted) {
        const ho = crowdHorseOpts(o, look, this.lod);
        const hkey = crowdHorseKey(ho);
        const htpl = this._horseTpl(ho, hkey);
        const bk = bakeMounted(look, key, tpl, ho, hkey);
        const gR = this._group('R|' + key + '|' + hkey, () => new CrowdGroup(this, tpl.geometry, bk.rider, this.cap, false, key + ' 骑手'));
        const gH = this._group('H|' + hkey + '|' + key, () => new CrowdGroup(this, htpl.geometry, bk.horse, this.cap, false, '战马'));
        it.parts.push({ g: gR, i: gR.alloc(id) }, { g: gH, i: gH.alloc(id) });
        it.table = bk.rider.table;
      } else {
        const bk = bakeSoldier(look, key, tpl);
        const g = this._group('F|' + key, () => new CrowdGroup(this, tpl.geometry, bk, this.cap, true, key));
        it.parts.push({ g, i: g.alloc(id) });
        it.table = bk.table;
      }
      tpl.refs--;   // 组持有几何体；模板常驻（士兵）
      this.items.set(id, it);
      for (const p of it.parts) { p.g.team.array.set(it.color, p.i * 3); p.g.team.needsUpdate = true; }
      this.play(id, 'idle', { phase: (id * 0.618034) % 1 });
      this.setTransform(id, 0, 0, 0, 0);
      return id;
    }
    _horseTpl(ho, hkey) {
      if (!this._htpls) this._htpls = new Map();
      let t = this._htpls.get(hkey);
      if (!t) { const h = new Horse(Object.assign({}, ho, { shadow: false, _crowd: true })); t = h.tpl; t.refs++; h.dispose(); this._htpls.set(hkey, t); }
      return t;
    }
    _moved(id, g, i) { const it = this.items.get(id); if (it) for (const p of it.parts) if (p.g === g) p.i = i; }
    // 位置与朝向（yaw 弧度，绕 Y；scale 缩放）
    setTransform(id, x, y, z, yaw, scale) {
      const it = this.items.get(id); if (!it) return this;
      const s = scale || 1;
      this._q.setFromAxisAngle(this._Y, yaw || 0);
      it.m.compose(this._p.set(x, y, z), this._q, this._s.set(s, s, s));
      return this.setMatrix(id, it.m);
    }
    setMatrix(id, m) {
      const it = this.items.get(id); if (!it) return this;
      if (it.m !== m) it.m.copy(m);
      if (it.fig) { const r = it.horse ? it.horse.root : it.fig.root; it.m.decompose(r.position, r.quaternion, r.scale); return this; }
      for (const p of it.parts) { p.g.mesh.setMatrixAt(p.i, it.m); p.g.mesh.instanceMatrix.needsUpdate = true; }
      return this;
    }
    // 播放片段：opts { t0（起始时刻，缺省当前时钟）, phase（0..1，循环片段的随机相位）, speed }
    // 步兵片段：CROWD_CLIPS；骑兵：idle walk trot gallop attack charge hit die
    play(id, clip, opts) {
      const it = this.items.get(id); if (!it) return this;
      opts = opts || {};
      const tb = it.table;
      const e = tb && (tb[clip] || tb.idle);
      it.clip = tb && tb[clip] ? clip : 'idle'; it.speed = opts.speed || 1;
      it.t0 = opts.t0 != null ? opts.t0 : this._time.value - (opts.phase || 0) * (e ? e.dur : 1);
      if (!this.instanced) return this;
      for (const p of it.parts) {
        const b = p.g.bake.table[it.clip] || p.g.bake.table.idle;
        p.g.anim.array.set([b.row, b.loop ? b.n : -b.n, it.t0, it.speed], p.i * 4);
        p.g.anim.needsUpdate = true;
      }
      return this;
    }
    clipOf(id) { const it = this.items.get(id); return it ? it.clip : null; }
    clipDur(clip, mounted) { return mounted ? (MOUNT_CLIPS[clip] || MOUNT_CLIPS.idle)[2] : (CL[clip] || CL.idle).dur; }
    setColor(id, hexc) {
      const it = this.items.get(id); if (!it) return this;
      it.color = teamTone(hexc);
      if (it.fig) {
        // 回退路径（逐个 SkinnedMesh）：势力色烘在顶点色里，换色 = 重建这名士兵（及其战马）
        const m = it.m.clone(), clip = it.clip, t0 = it.t0, speed = it.speed;
        it.fig.dispose(); if (it.horse) it.horse.dispose();
        it.o = Object.assign({}, it.o, { color: hexc });
        this._buildFallback(it, it.o);
        it.clip = clip; it.t0 = t0; it.speed = speed;
        this.setMatrix(it.id, m);
        return this;
      }
      for (const p of it.parts) { p.g.team.array.set(it.color, p.i * 3); p.g.team.needsUpdate = true; }
      return this;
    }
    remove(id) {
      const it = this.items.get(id); if (!it) return this;
      if (it.fig) { it.fig.dispose(); if (it.horse) it.horse.dispose(); }
      for (const p of it.parts) p.g.free(p.i);
      this.items.delete(id);
      return this;
    }
    get count() { return this.items.size; }
    get drawCalls() { if (!this.instanced) { let n = 0; for (const it of this.items.values()) n += (it.fig ? it.fig.drawCalls : 0) + (it.horse ? 1 : 0); return n; } let n = 0; for (const g of this.groups.values()) if (g.count) n++; return n; }
    get triangles() { let n = 0; if (!this.instanced) { for (const it of this.items.values()) n += (it.fig ? it.fig.triangles : 0) + (it.horse ? it.horse.triangles : 0); return n; } for (const g of this.groups.values()) n += g.tris * g.count; return n; }
    stats() { const s = { soldiers: this.items.size, groups: 0, drawCalls: this.drawCalls, triangles: this.triangles, bakeBytes: 0 }; for (const g of this.groups.values()) { s.groups++; s.bakeBytes += g.bake.bytes; } return s; }
    dispose() {
      for (const id of [...this.items.keys()]) this.remove(id);
      for (const g of this.groups.values()) g.dispose();
      this.groups.clear();
      if (this._htpls) for (const t of this._htpls.values()) { if (--t.refs <= 0) { HTPL.delete(t.key); t.geometry.dispose(); } }
      if (this.root.parent) this.root.parent.remove(this.root);
    }
    // ---- 回退（WebGL1）：每名士兵一个 SkinnedMesh，按同一接口驱动
    _buildFallback(it, o) {
      it.fig = soldier(Object.assign({}, o, { lod: this.lod, type: o.mounted ? 'cavalry' : o.type, shadow: this.shadow }));
      this.root.add(it.fig.root);
      it.horse = null;
      if (o.mounted) { it.horse = horse(Object.assign({ lod: 'low', color: o.color, seed: (o.seed | 0) % 2 }, o.horse || {})); this.root.add(it.horse.root); it.horse.mount(it.fig); }
    }
    _addFallback(it, o, id) {
      this._buildFallback(it, o);
      it.table = o.mounted ? MOUNT_CLIPS : null;
      this.items.set(id, it);
      this.play(id, 'idle', { phase: (id * 0.618034) % 1 });
      return id;
    }
    _poseFallback(it) {
      const t = (this._time.value - it.t0) * it.speed;
      if (it.horse) { const M = MOUNT_CLIPS[it.clip] || MOUNT_CLIPS.idle; it.horse.setPose(M[0], t); it.fig.setPose(M[1], M[4] != null ? M[4] : t, { gallop: M[0] === 'gallop' }); }
      else it.fig.setPose(CL[it.clip] ? it.clip : 'idle', t);
    }
  }
  function crowd(opts) { return new Crowd(opts); }
  function crowdKey(look) { return ['S', look.type, look.culture, look.lod, look.seed, '*'].join('|'); }
  // 预烘焙（生成器，按片段分片）：士兵规格 { type, culture, seed, mounted, horse }
  function* bakeCrowdGen(o, lod) {
    lod = lod || 'low';
    const look = lookOfSoldier(Object.assign({}, o, { lod, type: o.mounted ? 'cavalry' : o.type }));
    const key = crowdKey(look);
    const tpl = getTemplate(look, key, 'crowd');
    yield;
    if (!o.mounted) yield* bakeSoldierGen(look, key, tpl);
    else {
      const ho = crowdHorseOpts(o, look, lod);
      yield* bakeMountedGen(look, key, tpl, ho, crowdHorseKey(ho));
    }
    tpl.refs--;
  }
  function crowdHorseOpts(o, look, lod) { return Object.assign({ lod: lod === 'mid' ? 'mid' : 'low', seed: (o.seed | 0) % 2, armor: (o.horse && o.horse.armor) || (look.culture === 'persia' || look.culture === 'sarmatian' ? 'scale' : 'cloth') }, o.horse || {}); }
  function crowdHorseKey(ho) { return [ho.lod, ho.coat, ho.armor, ho.seed | 0].join('|'); }

  // ======================================================== 公开接口 ==
  const SOLDIER_TYPES = ['spear', 'sword', 'archer', 'cavalry'];
  // 单个人物的模板键：外观 × LOD × 势力色（势力色烘进顶点色）
  function generalKey(look) {
    const sp = look.spec || {};
    return ['G', look.name, look.culture, look.lod, look.weapon, look.shield || '', sp.key || '', String(look.factionHex || '').toLowerCase()].join('|');
  }
  function general(gen, opts) {
    opts = opts || {};
    const look = lookOfGeneral(gen, opts);
    const tpl = getTemplate(look, generalKey(look), 'single', look.factionHex);
    return new Figure(tpl, Object.assign({ color: look.factionHex }, opts));
  }
  function soldier(opts) {
    opts = opts || {};
    const look = lookOfSoldier(opts);
    const key = ['S', look.type, look.culture, look.lod, look.seed, String(look.factionHex).toLowerCase()].join('|');
    const tpl = getTemplate(look, key, 'single', look.factionHex);
    return new Figure(tpl, Object.assign({ color: look.factionHex }, opts));
  }
  function horse(opts) { return new Horse(opts || {}); }
  // 独立兵器：每次新建几何体（调用方可以释放：mesh.userData.dispose()）
  function weapon(name, opts) {
    opts = opts || {};
    const id = WEAPONS[name] ? name : 'jian';
    const det = WDET[opts.lod || 'high'] != null ? WDET[opts.lod || 'high'] : 2;
    const M = new (FigBuilder())(11);
    const info = buildWeaponLP(M, id, det);
    const g = M.toGeometry();
    const m = LPX().mesh(g, { castShadow: opts.shadow !== false, receiveShadow: false });
    m.name = 'Weapon:' + info.id;
    m.userData.weapon = { id: info.id, name: info.name, style: info.style, len: info.len, tip: new THREE.Vector3(...info.tip), base: new THREE.Vector3(...info.base), off: info.off ? new THREE.Vector3(...info.off) : null };
    m.userData.tris = g.userData.tris;
    m.userData.dispose = () => g.dispose();
    return m;
  }
  // 独立的盾（+Z 为盾面）：盾面为势力色（opts.color）
  function shield(name, opts) {
    opts = opts || {};
    const det = WDET[opts.lod || 'high'] != null ? WDET[opts.lod || 'high'] : 2;
    const M = new (FigBuilder())(13);
    const X = { team: tcol(teamTone(opts.color || '#8a2a24'), 1), trim: C3(PALF.gold) };
    const info = buildShieldLP(M, name || 'round', det, X);
    const g = M.toGeometry();
    const m = LPX().mesh(g, { castShadow: opts.shadow !== false, receiveShadow: false });
    m.name = 'Shield:' + (name || 'round');
    m.userData.shield = info;
    m.userData.tris = g.userData.tris;
    m.userData.dispose = () => g.dispose();
    return m;
  }

  // 预生成：士兵模板、名将模板、士兵群动作烘焙（分片，单片约 12ms）；先等 lowpoly.js 就绪
  let preloading = null;
  function preload(opts) {
    if (preloading && !opts) return preloading;
    opts = opts || {};
    const t0 = now();
    const p = ensureLP().then(ok => {
      if (!ok) throw new Error('SG.Figure：js/lowpoly.js（SG.LP）载入失败');
      LPX();
      const cultures = opts.cultures || CULTURES;
      const types = opts.types || SOLDIER_TYPES;
      const lods = opts.lods || ['low'];
      const seeds = opts.seeds || [0];
      const budget = opts.budget || 12;
      const jobs = [];
      // 共用材质与程序（LP 着色器）提前编译：有渲染器时
      const rr = opts.renderer || (SG.Gfx && SG.Gfx.renderer);
      void rr;
      for (const lod of lods) for (const c of cultures) for (const ty of types) for (const sd of seeds) jobs.push({ name: 'soldier ' + c + '/' + ty, fn: () => { const f = soldier({ type: ty, culture: c, lod, seed: sd }); f.dispose(); } });
      if (opts.generals) for (const g of opts.generals) jobs.push({ name: 'general', fn: () => { const f = general(g, { lod: opts.generalLod || 'mid' }); f.dispose(); } });
      // 士兵群的动作烘焙：opts.crowd = [{ type, culture, seed, mounted }]（或 true：所选文化 × 兵种，seed 0，骑兵带马）
      if (opts.crowd) {
        const specs = opts.crowd === true ? [].concat(...cultures.map(c => types.map(ty => ({ type: ty, culture: c, seed: 0, mounted: ty === 'cavalry' })))) : opts.crowd;
        for (const o of specs) jobs.push({ gen: bakeCrowdGen(o, opts.crowdLod || 'low'), name: 'crowd ' + o.culture + '/' + o.type + (o.mounted ? '+horse' : '') });
      }
      return runJobs(jobs, budget);
    }).then(() => { STATS.preloadMs = Math.round(now() - t0); return STATS; });
    if (!opts || Object.keys(opts).length === 0) preloading = p;
    return p;
  }
  function runJobs(jobs, budget) {
    return new Promise(resolve => {
      const step = () => {
        const s0 = now();
        while (jobs.length && now() - s0 < budget) {
          const j = jobs[0], j0 = now();
          if (j.gen) { const r = j.gen.next(); if (r.done) jobs.shift(); }
          else { j.fn(); jobs.shift(); }
          const jm = now() - j0; if (jm > (STATS.maxStepMs || 0)) { STATS.maxStepMs = Math.round(jm); STATS.maxStepJob = j.name || (j.gen ? 'gen' : 'fn'); }
        }
        STATS.maxSliceMs = Math.max(STATS.maxSliceMs || 0, Math.round(now() - s0));
        if (jobs.length) setTimeout(step, 0);
        else resolve(STATS);
      };
      step();
    });
  }

  function stats() {
    return Object.assign({}, STATS, { templateCount: TEMPLATES.size, horseTemplates: HTPL.size, weaponGeos: WGEO.size, bakes: BAKES.size, faces: 0, materials: 0 });
  }
  // 程序化片段 → THREE.AnimationClip（需要 AnimationMixer 时）
  function toAnimationClip(fig, clip, fps) {
    fps = fps || 30;
    const info = fig.clipInfo(clip) || { dur: 1 };
    const n = Math.max(2, Math.round(info.dur * fps) + 1);
    const times = new Float32Array(n);
    const bones = fig._bones;
    const qv = bones.map(() => new Float32Array(n * 4));
    const pv = new Float32Array(n * 3), rv = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      const t = i / (n - 1) * info.dur;
      times[i] = t;
      fig.setPose(clip, t);
      bones.forEach((b, j) => { b.quaternion.toArray(qv[j], i * 4); });
      (fig.bones.pelvis || bones[1]).position.toArray(pv, i * 3);
      bones[0].position.toArray(rv, i * 3);          // root：贴地抬升（倒地片段）
    }
    const tracks = bones.map((b, j) => new THREE.QuaternionKeyframeTrack(b.name + '.quaternion', times, qv[j]));
    tracks.push(new THREE.VectorKeyframeTrack((fig.bones.pelvis || bones[1]).name + '.position', times, pv));
    tracks.push(new THREE.VectorKeyframeTrack(bones[0].name + '.position', times, rv));
    return new THREE.AnimationClip(clip, info.dur, tracks);
  }
  function clearCache() {
    for (const t of [...TEMPLATES.values()]) if (t.refs <= 0) { if (t.geometry) t.geometry.dispose(); TEMPLATES.delete(t.key); }
    for (const t of [...HTPL.values()]) if (t.refs <= 0) { t.geometry.dispose(); HTPL.delete(t.key); }
  }
  const clipsOut = {};
  for (const k in CL) clipsOut[k] = { dur: CL[k].dur, loop: CL[k].loop, desc: CL[k].desc };

  SG.Figure = {
    general, soldier, horse, weapon, shield, preload, stats, toAnimationClip, clearCache, crowd,
    crowdClips: CROWD_CLIPS.slice(), crowdMountClips: Object.keys(MOUNT_CLIPS),
    clips: clipsOut, horseClips: HCLIPS,
    cultures: CULTURES.slice(), weapons: Object.keys(WEAPONS).map(id => ({ id, name: WEAPONS[id].name, style: WEAPONS[id].style })),
    soldierTypes: SOLDIER_TYPES.slice(), boneNames: BONES.slice(), horseBoneNames: HBONES.slice(),
    // 第四版：所有人物共用 SG.LP 的 Lanternvale 光照材质（势力色烘进顶点色；士兵群为实例属性）
    get material() { return LPX().material({}); },
    teamMaterial, setRim,
    lookOf(gen, opts) { return lookOfGeneral(gen, opts || {}); },
    soldierLook(opts) { return lookOfSoldier(opts || {}); },
    // 第三版的贴图图集已取消（顶点色）；保留接口返回 null
    atlas() { return null; },
    ready: ensureLP,
    POLE_LEAN,
    budgets: { high: TRI_BUDGET.high, hero: TRI_BUDGET.hero, mid: TRI_BUDGET.mid, low: TRI_BUDGET.low, soldier: TRI_BUDGET.low, horseHigh: 2600, horseMid: 1800, horseLow: 900 },
    _internal: { bakeCrowdGen, makeProps, headSpec, headGrid, Kit, LODK, SQUEEZE, TRI_BUDGET, buildWeaponLP, buildShieldLP, buildHorseLP, weaponGeo, STANCE, CL, compiled, TEMPLATES, HTPL, WGEO, BAKES, paletteOf, teamTone },
  };
})();
