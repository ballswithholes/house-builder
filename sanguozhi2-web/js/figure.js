'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 人物 / 士兵 / 战马 / 兵器模型库（DESIGN-V3 §1、§8）

   光滑、细致、真三国无双式的程序化模型（不下载任何素材）：
   · 每名人物 = 一个 SkinnedMesh（合并的平滑网格 + 蒙皮权重），标准人形骨架；
     名将另有一个头部网格（按头像外貌参数生成的面部贴图）与一个兵器网格 → 每名武将 3 次绘制，
     每名士兵 1 次绘制（头、兵器、盾都并入同一蒙皮网格）。
   · 共用一套「装具材质」：MeshStandardMaterial + 程序生成的图集（反照率 / 法线 / AO-粗糙度-金属度），
     顶点色染色（图集 alpha = 染色遮罩：1 → 乘顶点色（势力色、金属色、肤色），0 → 保留贴图原色，
     用于金线刺绣、彩绘盾面、面部五官等），程序生成的环境贴图（金属反光），轮廓光（rim）。
   · 平滑法线（索引网格 + 同位顶点法线焊接），无平面着色。
   · 坐标：单位米；人物脚底中心为原点，面朝 +Z，上为 +Y，人物右手在 −X 一侧。武将身高约 1.86 米
     （约 7.5 头身，不含盔缨）。战马同样面朝 +Z。集成时按场景比例缩放 fig.root 即可。
   · 本文件不依赖 art.js；只需要 THREE（r158）与 SG 命名空间。头像模块（SG.Portrait）已载入时，
     名将外貌取自 SG.Portrait.spec(gen)（与头像一致）；否则用内置的简化外貌。

   公开接口
   --------------------------------------------------------------------------
   SG.Figure.preload(opts?)  → Promise。分片生成共用贴图图集、环境贴图、士兵模板（不阻塞主线程，
                               单片预算 12ms，最大单步实测约 35ms / SwiftShader）。opts：{ cultures: [...]（缺省全部 15 种）,
                               types: [...], lods: ['low'], seeds: [0], generals: [gen...]（预生成名将模板与面部贴图）,
                               generalLod, renderer（缺省 SG.Gfx.renderer：提前上传图集贴图）,
                               crowd: [{ type, culture, seed, mounted }]（或 true：cultures × types，骑兵带马）—— 预先烘焙士兵群动作贴图,
                               crowdLod（缺省 'low'）}。单步通常 ≤ 35ms（SwiftShader，CPU 空闲时）。
                               不调用也能用（首次用到时同步生成图集，约 0.3–1 秒；士兵群模板首次 add 时同步烘焙约 0.1 秒/模板）。
   SG.Figure.general(gen, opts) → Figure（名将 / 任意武将）
       gen：{ name, war, intel, pol, culture?, sex?, ... }（或姓名字符串）
       opts：{ culture, color（势力色 '#rrggbb'）, lod: 'high' | 'mid' | 'low'（缺省 'high'）,
               weapon（兵器名，缺省按名将设定 / 外貌 / 能力值）, shield（true / false / 盾名，缺省按文化与兵器）,
               mounted（true：姿势按骑乘，配合 SG.Figure.horse 使用）, shadow（缺省 true）}
   SG.Figure.soldier(opts)   → Figure（士兵）
       opts：{ type: 'spear' | 'sword' | 'archer' | 'cavalry'（骑兵本人；马另建）, culture, color,
               lod: 'low' | 'mid'（缺省 'low'）, seed（同一模板内的外观变体：脸、盔缨、兵器细节）, shadow }
       同 (type, culture, color, lod, 变体) 的士兵共用几何体与材质，只各自持有骨架。
   SG.Figure.horse(opts)     → Horse（战马；也是 Figure 的一种，有 setPose / bones / attach / dispose）
       opts：{ color（马衣 / 马铠的势力色）, coat（毛色 '#hex'，缺省按 seed 选栗 / 枣 / 黑 / 白 / 青）,
               lod, armor: 'cloth' | 'scale' | 'none'（缺省 cloth；安息 / 萨尔马提亚具装用 scale）, culture, seed }
       horse.mount(fig)：把人物放上马鞍（fig.root 成为鞍座骨的子节点，人物自动用骑乘下肢姿势）；
       horse.dismount(fig)。
   SG.Figure.crowd(opts)     → Crowd（实例化士兵群：攻击画面 / 战场的大批士兵用它，绘制次数 = 模板组数，与人数无关）
       opts：{ lod: 'low' | 'mid'（缺省 'low'）, shadow（缺省 true；阴影每组再加一次绘制）, capacity（每组初始容量，自动扩容）,
               renderer（缺省 SG.Gfx.renderer；非 WebGL2 时自动回退为逐个 SkinnedMesh，接口不变）}
       crowd.root                       THREE.Group，加入场景
       const id = crowd.add({ type, culture, color, seed, mounted, horse: { coat, armor, seed } })
                                        mounted: true = 骑兵连马（骑手与战马各一组，同一实例矩阵）
       crowd.setTransform(id, x, y, z, yaw, scale) / crowd.setMatrix(id, m4)    —— crowd.root 局部坐标
       crowd.play(id, clip, { t0, phase, speed })   步兵片段：SG.Figure.crowdClips
                                        （idle walk run light1 light2 heavy block hit knockdown down die cheer bowDraw bowShoot）；
                                        骑兵：SG.Figure.crowdMountClips（idle walk trot gallop attack charge hit die）。
                                        非循环片段播完停在最后一帧（如 die / down 停在倒地）。
       crowd.setTime(seconds)           全局时钟，每帧调用一次（片段时间 = (time − t0) × speed）
       crowd.setColor(id, hex) / crowd.remove(id) / crowd.count / crowd.drawCalls / crowd.triangles / crowd.stats() / crowd.dispose()
       crowd.prepare(specs) → Promise   分片预烘焙（同 preload 的 crowd 项）
       实现：动作按 24fps 烘焙为半精度骨骼矩阵贴图（每帧一行，帧间由纹理单元线性插值），顶点着色器蒙皮；
       势力色为实例属性；站立的士兵贴地压平。每模板烘焙约 0.1 秒、贴图约 0.25MB。
   SG.Figure.shield(name, opts) → THREE.Mesh（独立的盾：scutum / round / oval / pai / wa / rattan；+Z 为盾面）
   SG.Figure.teamMaterial(hex) → 该势力色的共用材质（换势力：fig.mesh.material = teamMaterial(新色)，兵器网格同理）
   SG.Figure.clearCache()    释放无人使用的模板几何体
   SG.Figure.weapon(name, opts) → THREE.Mesh（独立兵器，原点为右手握点，杆 / 刃沿 +Y，刃口朝 +Z）
       mesh.userData.weapon = { id, name（中文名）, style: 'pole'|'one'|'dual'|'bow'|'fan', len, tip: Vector3,
                                off: Vector3（左手握点，双手兵器）}。opts：{ lod, color }。
       兵器名：guandao 青龙偃月刀 · snake 丈八蛇矛 · halberd 方天画戟 · swords 双股剑 · twinji 典韦双铁戟 ·
       bigblade 许褚斩马大刀 · fengzui 黄忠凤嘴刀 · huangbow 黄忠宝雕弓 · shortji 太史慈短戟 · bawang 孙策霸王枪 ·
       yajiao 赵云涯角枪 · hutou 马超虎头湛金枪 · yitian 倚天剑 · guding 古锭刀 · fan 羽扇 · bigaxe 开山大斧 ·
       spear pilum lance(kontos) poleblade axe trident dao jian gladius spatha tachi mace handaxe twindao bow
       （完整列表：SG.Figure.weapons）
   Figure（人物 / 士兵 / 马的实例）
       fig.root            THREE.Group，放进场景、设置位置 / 朝向 / 缩放
       fig.mesh            SkinnedMesh（主体）；fig.head（名将的头部 Mesh 或 null）；fig.weaponMesh / fig.weaponMesh2
       fig.bones           { root, pelvis, spine, chest, neck, head, clavL, upperArmL, foreArmL, handL, clavR, upperArmR,
                             foreArmR, handR, thighL, shinL, footL, thighR, shinR, footR, cape1, cape2, cape3,
                             skirtF, skirtB, plume1, plume2, hair1, shL, shR, dlL, dlR } —— THREE.Bone，可直接改 rotation / quaternion
                           （shL / shR：护肩挂点，自动取上臂旋转的 85%；dlL / dlR：三角肌过渡，50%。由 setPose 维护）
       fig.sockets         { gripR, gripL, shieldL, back, headTop } —— 挂点（Object3D，骨骼的子节点）；马：horse.sockets.saddle
       fig.setPose(clip, t, opts?)
                           按名称采样程序化动作片段（t 为片段内秒数，循环片段自动取模），直接写骨骼；
                           不需要 AnimationMixer，调用方自带时钟。opts：
                             { lower: 片段名（下半身另用一个片段，如骑乘 'ride'）, lowerT, blend: { clip, t, w }（与另一片段混合，
                               w = 0..1 为本片段的权重）, ik: false（不做双手 IK，远处士兵可省 CPU）, gallop: true（骑乘时下半身用疾驰）,
                               clamp: false（关闭贴地）}
                           贴地：未骑乘时，任何姿势下躯干 / 头 / 四肢 / 披风都不穿入 fig.root 的 y = 0 平面（必要时抬高 root 骨）；
                           倒地 / 跪地片段让最低点正好落在地面上，兵器脱手平放在身体右侧；披风、腰裙、盔缨、长发等蒙皮顶点
                           若仍低于地面，着色器把它们压平在地面上（fig.groundClamp = false 可关闭）。两端兵器不插入地面。
                           双手长兵：左手每帧取杆上左臂够得着的点（两手间距 ≥ 0.2 米），整根杆都够不到时把兵器向左肩平移。
       fig.blendPose(clipA, tA, clipB, tB, w)   两个片段线性混合（w = clipB 的权重）
       fig.update(dt)      可选：披风、飘带、盔缨的弹簧随动（每帧调用一次；不调用时按片段时间做程序化摆动）
       fig.clipInfo(clip)  → { dur, loop }
       fig.ikHand(side, pos, quat?)   双骨 IK：把 'R' / 'L' 手腕移到 fig.root 局部坐标 pos（quat 为手的朝向，可缺）
       fig.grip(pos, dirOrQuat, roll?)  把右手兵器的握点放在 pos、杆指向 dir（root 局部坐标），双手兵器左手自动握杆
       fig.attach(obj, boneOrSocket)    把任意 Object3D 挂到骨骼 / 挂点上；fig.detach(obj)
       fig.setWeapon(name) / fig.weaponInfo / fig.style（'pole' | 'one' | 'dual' | 'bow' | 'fan' | 'none'）
       fig.weaponTip(out)  → 兵器尖端的世界坐标（刀光拖尾用）；fig.weaponBase(out)
       fig.setTint(hex, k) 受击闪光等（所有材质的 emissive 叠加；k = 0 关闭）
       fig.triangles / fig.drawCalls / fig.spec（解析后的外观）/ fig.dispose()
   SG.Figure.clips           { name: { dur, loop, desc } } 动作片段目录：
       idle walk run light1 light2 light3 heavy charge block parry hit hitB knockdown down getup victory defeat
       bowDraw bowShoot ride gallop cheer die dash jump special guardbreak
       （武器流派自动适配：同名片段对 pole / one / dual / bow / fan / none 有不同的关键帧）
   SG.Figure.horseClips      idle walk trot gallop rear die
   SG.Figure.toAnimationClip(fig, clip, fps = 30) → THREE.AnimationClip（需要 AnimationMixer 时使用）
   SG.Figure.stats()         → { built, templates, textures, atlasMs, envMs, buildMs, lastBuildMs, tris: {...} }
   SG.Figure.material        共用装具材质（只读）；SG.Figure.setRim(color, strength) 调整轮廓光
   SG.Figure.lookOf(gen, opts) → 解析后的服饰 / 兵器设定（调试用）
   SG.Figure.cultures / SG.Figure.weapons / SG.Figure.soldierTypes

   多边形预算（DESIGN-V3 §1）：high ≤ 15k 三角形（单挑、特写），mid ≤ 4k（战场主将、攻击画面将领），
   low ≤ 900（士兵）。超出预算的模板按 SQUEEZE 逐级减少分段 / 边数后重建（fig.tpl.squeeze = 0..3 为所用级别）。
   SG.Figure.stats().tris 记录各模板实测值。
   色彩：游戏采用 Gamma 工作流（ColorManagement 关闭、线性输出、无色调映射），本模块的颜色与贴图按此调校。
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

  // ======================================================== 程序贴图图集 ==
  // 1024² 图集，128px 为一格。每个区域在 5 个画布通道上绘制：反照率（RGB）、染色遮罩、高度、粗糙度、金属度，
  // 合成为三张贴图：map（RGB + alpha = 染色遮罩）、normalMap（由高度求得）、ORM（R = AO、G = 粗糙度、B = 金属度）。
  // 约定：区域局部坐标 s（0 左 → 1 右）、t（0 下 → 1 上）。对称部件的 s = 0 为中线（镜像轴），故边饰只画在右、上、下三边。
  const ATLAS_N = 1024, CELL = 128;
  const SKIN_EXP = 0.8;
  const REGION_DEFS = [
    ['metal', 2, 2], ['lamellar', 2, 2], ['lamLace', 2, 2], ['scutum', 2, 2], ['shieldRound', 2, 2],
    ['cloth', 2, 1], ['hem', 2, 1], ['brocade', 2, 1], ['plaid', 2, 1], ['fur', 2, 1], ['hair', 2, 1], ['goldEngr', 2, 1],
    ['horse', 2, 1], ['scale', 2, 1], ['tiger', 2, 1],
    ['feather', 1, 2], ['blade', 1, 2],
    ['skin', 1, 1], ['faceA', 1, 1], ['faceB', 1, 1], ['faceC', 1, 1], ['faceD', 1, 1], ['brushed', 1, 1], ['mail', 1, 1],
    ['leather', 1, 1], ['wood', 1, 1], ['wrap', 1, 1], ['tassel', 1, 1], ['gold', 1, 1], ['rattan', 1, 1], ['white', 1, 1],
    ['rivet', 1, 1], ['buckle', 1, 1], ['mirror', 1, 1], ['lion', 1, 1], ['leopard', 1, 1],
  ];
  let ATLAS = null;         // { R: {name: region}, map, normalMap, ormMap }
  function packRegions() {
    const G = ATLAS_N / CELL, used = new Uint8Array(G * G), R = {};
    for (const [name, cw, ch] of REGION_DEFS) {
      let done = false;
      for (let y = 0; y <= G - ch && !done; y++) for (let x = 0; x <= G - cw && !done; x++) {
        let free = true;
        for (let j = 0; j < ch && free; j++) for (let i = 0; i < cw && free; i++) if (used[(y + j) * G + x + i]) free = false;
        if (!free) continue;
        for (let j = 0; j < ch; j++) for (let i = 0; i < cw; i++) used[(y + j) * G + x + i] = 1;
        const px = x * CELL, py = y * CELL, pw = cw * CELL, ph = ch * CELL, ins = 3;
        R[name] = { name, px, py, pw, ph, u0: (px + ins) / ATLAS_N, u1: (px + pw - ins) / ATLAS_N, vb: 1 - (py + ph - ins) / ATLAS_N, vt: 1 - (py + ins) / ATLAS_N };
        done = true;
      }
      if (!done) throw new Error('figure atlas full: ' + name);
    }
    return R;
  }
  // 区域局部坐标 → 图集 UV
  function ruv(r, s, t) { return [r.u0 + clamp01(s) * (r.u1 - r.u0), r.vb + clamp01(t) * (r.vt - r.vb)]; }

  // 值噪声（装饰用，确定性）
  function h2(i, j, seed) {
    let h = (Math.imul(i, 374761393) + Math.imul(j, 668265263) + Math.imul(seed | 0, 1442695041)) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
  }
  function vnoise(x, y, seed) {
    const xi = Math.floor(x), yi = Math.floor(y), xf = x - xi, yf = y - yi;
    const u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
    const a = h2(xi, yi, seed), b = h2(xi + 1, yi, seed), c = h2(xi, yi + 1, seed), d = h2(xi + 1, yi + 1, seed);
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
  }
  function fbm(x, y, oct, seed) { let s = 0, a = 0.5, f = 1, n = 0; for (let i = 0; i < oct; i++) { s += a * vnoise(x * f, y * f, seed + i * 17); n += a; a *= 0.5; f *= 2.03; } return s / n; }

  function grey(v) { const c = Math.round(clamp01(v) * 255); return 'rgb(' + c + ',' + c + ',' + c + ')'; }
  function cssOf(a) { return typeof a === 'string' ? a : Array.isArray(a) ? hex(a) : a; }

  // 在 5 个通道上绘制同一路径。vals：{ a: 颜色, m: 遮罩, h: 高度, r: 粗糙度, mt: 金属度 }，
  // 每项可为数值 / 颜色，或 (ctx) => CanvasGradient
  function Painter(ctxs, r) {
    const P = {
      x: r.px, y: r.py, w: r.pw, h: r.ph, r,
      _each(vals, fn) {
        for (const k of ['a', 'm', 'h', 'r', 'mt']) {
          const v = vals[k];
          if (v === undefined) continue;
          const ctx = ctxs[k];
          if (!ctx) continue;
          ctx.save();
          ctx.beginPath(); ctx.rect(P.x, P.y, P.w, P.h); ctx.clip();
          const st = typeof v === 'function' ? v(ctx, k) : (k === 'a' ? cssOf(v) : grey(v));
          fn(ctx, st);
          ctx.restore();
        }
      },
      fill(path, vals) { P._each(vals, (ctx, st) => { ctx.fillStyle = st; ctx.beginPath(); path(ctx); ctx.fill(); }); },
      stroke(path, vals, lw) { P._each(vals, (ctx, st) => { ctx.strokeStyle = st; ctx.lineWidth = lw || 1; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.beginPath(); path(ctx); ctx.stroke(); }); },
      rect(x, y, w, h, vals) { P.fill(ctx => ctx.rect(P.x + x, P.y + y, w, h), vals); },
      all(vals) { P.rect(-2, -2, P.w + 4, P.h + 4, vals); },
      // 局部坐标的线性 / 径向渐变（在每个通道上分别给定端点值）
      lin(x0, y0, x1, y1, stops) {
        return (ctx, k) => {
          const g = ctx.createLinearGradient(P.x + x0, P.y + y0, P.x + x1, P.y + y1);
          for (const s of stops) g.addColorStop(s[0], k === 'a' ? cssOf(s[1]) : grey(s[1]));
          return g;
        };
      },
      rad(cx, cy, r0, r1, stops) {
        return (ctx, k) => {
          const g = ctx.createRadialGradient(P.x + cx, P.y + cy, r0, P.x + cx, P.y + cy, r1);
          for (const s of stops) g.addColorStop(s[0], k === 'a' ? cssOf(s[1]) : grey(s[1]));
          return g;
        };
      },
    };
    return P;
  }
  // 对每个区域的像素做后处理（噪声等）：fn(lx, ly, I) 其中 I 为该像素在 RGBA 数组中的下标
  function postRegion(R, D, name, fn) {
    const r = R[name];
    for (let y = 0; y < r.ph; y++) for (let x = 0; x < r.pw; x++) fn(x, y, ((r.py + y) * ATLAS_N + r.px + x) * 4, D);
  }
  function addV(arr, I, d) { arr[I] = clamp(arr[I] + d, 0, 255); arr[I + 1] = clamp(arr[I + 1] + d, 0, 255); arr[I + 2] = clamp(arr[I + 2] + d, 0, 255); }
  function mulV(arr, I, k) { arr[I] = clamp(arr[I] * k, 0, 255); arr[I + 1] = clamp(arr[I + 1] * k, 0, 255); arr[I + 2] = clamp(arr[I + 2] * k, 0, 255); }

  // ---------------------------------------------------------- 各区域绘制 --
  const GOLDTHREAD = '#e4bc5c';
  const PAINT = {
    metal(P) {
      P.all({ a: '#d2d4d8', m: 1, h: 0.5, r: 0.3, mt: 1 });
      // 边缘磨亮
      P.all({ a: P.lin(P.w, 0, P.w - 30, 0, [[0, 'rgba(255,255,255,0.35)'], [1, 'rgba(255,255,255,0)']]) });
      // 三边（右、上、下）凸起包边 + 錾刻线 + 卷云纹带
      const W = P.w, H = P.h;
      const edge = (o, lw, vals) => P.stroke(ctx => { ctx.moveTo(P.x - 4, P.y + o); ctx.lineTo(P.x + W - o, P.y + o); ctx.lineTo(P.x + W - o, P.y + H - o); ctx.lineTo(P.x - 4, P.y + H - o); }, vals, lw);
      edge(4, 6, { a: '#e8eaee', h: 0.68, r: 0.22 });
      edge(9, 1.6, { a: '#8a8c92', h: 0.36, r: 0.5 });
      edge(25, 1.6, { a: '#8a8c92', h: 0.36, r: 0.5 });
      // 卷云纹：沿三边排列的小涡旋
      const curl = (cx, cy, s, rot) => P.stroke(ctx => {
        for (let i = 0; i <= 24; i++) {
          const a = rot + i / 24 * TAU * 1.1, rr = s * (1 - i / 30);
          const x = P.x + cx + Math.cos(a) * rr, y = P.y + cy + Math.sin(a) * rr;
          if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
        }
      }, { a: '#94969c', h: 0.38, r: 0.5 }, 1.3);
      for (let x = 6; x < W - 26; x += 16) { curl(x, 17, 5, (x / 16) % 2 ? 0 : PI); curl(x, H - 17, 5, (x / 16) % 2 ? PI : 0); }
      for (let y = 26; y < H - 26; y += 16) curl(W - 17, y, 5, (y / 16) % 2 ? PI / 2 : -PI / 2);
      // 划痕
      const rr = Rng(11);
      for (let i = 0; i < 60; i++) {
        const x = rr.range(0, W), y = rr.range(30, H - 30), l = rr.range(6, 26), a = rr.range(-0.6, 0.6) + (rr.chance(0.5) ? 0 : PI / 2);
        P.stroke(ctx => { ctx.moveTo(P.x + x, P.y + y); ctx.lineTo(P.x + x + Math.cos(a) * l, P.y + y + Math.sin(a) * l); },
          rr.chance(0.6) ? { a: 'rgba(250,252,255,0.5)', r: 0.2 } : { a: 'rgba(60,62,70,0.35)', r: 0.5, h: 0.46 }, rr.range(0.5, 1.1));
      }
    },
    brushed(P) { P.all({ a: '#d0d2d6', m: 1, h: 0.5, r: 0.34, mt: 1 }); },
    lamellar(P) { lamellarPaint(P, false); },
    lamLace(P) { lamellarPaint(P, true); },
    scale(P) {
      P.all({ a: '#2a2a2e', m: 1, h: 0.3, r: 0.6, mt: 1 });
      const sw = 16, step = 10;
      for (let row = Math.ceil(P.h / step) + 1; row >= -1; row--) {
        const y = row * step, off = (row % 2) ? sw / 2 : 0;
        for (let x = -sw; x < P.w + sw; x += sw) {
          const cx = x + off;
          const path = ctx => { ctx.moveTo(P.x + cx - sw / 2, P.y + y - 6); ctx.lineTo(P.x + cx + sw / 2, P.y + y - 6); ctx.lineTo(P.x + cx + sw / 2, P.y + y + 2); ctx.arc(P.x + cx, P.y + y + 2, sw / 2, 0, PI); ctx.closePath(); };
          P.fill(path, { a: P.rad(cx - 2, y + 2, 1, sw * 0.8, [[0, '#eef0f2'], [1, '#9a9ca2']]), h: P.rad(cx, y + 3, 1, sw * 0.7, [[0, 0.78], [1, 0.5]]), r: 0.3, mt: 1, m: 1 });
          P.stroke(ctx => { ctx.moveTo(P.x + cx, P.y + y - 4); ctx.lineTo(P.x + cx, P.y + y + sw / 2); }, { h: 0.66, a: '#f2f4f6' }, 1);
        }
      }
    },
    mail(P) {
      P.all({ a: '#3a3c42', m: 1, h: 0.3, r: 0.5, mt: 1 });
      for (let y = 0; y < P.h + 8; y += 5) for (let x = 0; x < P.w + 8; x += 6) {
        const cx = x + ((y / 5) % 2 ? 3 : 0);
        P.stroke(ctx => ctx.arc(P.x + cx, P.y + y, 2.6, 0, TAU), { a: '#c8cad0', h: 0.7, r: 0.35 }, 1.3);
      }
    },
    cloth(P) { P.all({ a: '#d6d6d6', m: 1, h: 0.5, r: 0.88, mt: 0 }); },
    hem(P) {
      P.all({ a: '#d6d6d6', m: 1, h: 0.5, r: 0.88, mt: 0 });
      const H = P.h, b0 = H - 50, b1 = H - 8;
      P.rect(-2, b0, P.w + 4, b1 - b0, { a: '#9a9a9a', h: 0.48 });
      const g = { a: GOLDTHREAD, m: 0, h: 0.66, r: 0.45, mt: 0.55 };
      P.rect(-2, b0, P.w + 4, 3, g); P.rect(-2, b1 - 3, P.w + 4, 3, g); P.rect(-2, H - 5, P.w + 4, 5, g);
      // 卷云纹（如意云头）
      for (let x = 0; x < P.w + 32; x += 32) {
        const cy = (b0 + b1) / 2;
        P.stroke(ctx => {
          ctx.moveTo(P.x + x - 14, P.y + cy + 6);
          ctx.bezierCurveTo(P.x + x - 14, P.y + cy - 10, P.x + x + 2, P.y + cy - 12, P.x + x + 2, P.y + cy - 2);
          ctx.bezierCurveTo(P.x + x + 2, P.y + cy + 6, P.x + x - 6, P.y + cy + 4, P.x + x - 5, P.y + cy - 1);
          ctx.moveTo(P.x + x + 2, P.y + cy - 2);
          ctx.bezierCurveTo(P.x + x + 4, P.y + cy + 12, P.x + x + 18, P.y + cy + 10, P.x + x + 18, P.y + cy);
        }, g, 2.2);
        P.fill(ctx => ctx.arc(P.x + x + 9, P.y + cy - 11, 2.4, 0, TAU), g);
      }
    },
    brocade(P) {
      P.all({ a: '#c4c4c4', m: 1, h: 0.5, r: 0.82, mt: 0 });
      for (let row = 0; row < 3; row++) for (let x = -32; x < P.w + 64; x += 64) {
        const cx = x + (row % 2 ? 32 : 0), cy = 16 + row * 48;
        P.stroke(ctx => ctx.arc(P.x + cx, P.y + cy, 16, 0, TAU), { a: '#e8e8e8', h: 0.6 }, 2.5);
        for (let k = 0; k < 6; k++) {
          const a = k / 6 * TAU;
          P.fill(ctx => ctx.ellipse(P.x + cx + Math.cos(a) * 9, P.y + cy + Math.sin(a) * 9, 5, 2.6, a, 0, TAU), { a: '#e2e2e2', h: 0.58 });
        }
        P.fill(ctx => ctx.arc(P.x + cx, P.y + cy, 3, 0, TAU), { a: GOLDTHREAD, m: 0, h: 0.62, mt: 0.5, r: 0.5 });
      }
    },
    plaid(P) {
      P.all({ a: '#9a9a9a', m: 1, h: 0.5, r: 0.92, mt: 0 });
      for (let i = 0; i < 4; i++) {
        const o = i * 64;
        P.rect(o + 6, -2, 22, P.h + 4, { a: 'rgba(30,30,30,0.45)' });
        P.rect(-2, (o % 128) + 10, P.w + 4, 22, { a: 'rgba(30,30,30,0.45)' });
        P.rect(o + 40, -2, 8, P.h + 4, { a: 'rgba(255,255,255,0.4)' });
        P.rect(-2, (o % 128) + 46, P.w + 4, 6, { a: 'rgba(255,255,255,0.4)' });
        P.rect(o + 52, -2, 2, P.h + 4, { a: '#d8b040', m: 0 });
        P.rect(-2, (o % 128) + 56, P.w + 4, 2, { a: '#b83028', m: 0 });
      }
    },
    fur(P) {
      P.all({ a: '#a8a8a8', m: 1, h: 0.5, r: 1, mt: 0 });
      const rr = Rng(5);
      for (let i = 0; i < 1600; i++) {
        const x = rr.range(-4, P.w + 4), y = rr.range(-8, P.h + 4), l = rr.range(6, 15), a = PI / 2 + rr.range(-0.5, 0.5);
        const v = rr.range(0.45, 1);
        P.stroke(ctx => { ctx.moveTo(P.x + x, P.y + y); ctx.quadraticCurveTo(P.x + x + Math.cos(a) * l * 0.5 + 2, P.y + y + Math.sin(a) * l * 0.5, P.x + x + Math.cos(a) * l, P.y + y + Math.sin(a) * l); },
          { a: grey(v * 0.9), h: v * 0.7 }, rr.range(1, 2.2));
      }
    },
    hair(P) {
      P.all({ a: '#8a8a8a', m: 1, h: 0.5, r: 0.5, mt: 0 });
      const rr = Rng(7);
      for (let i = 0; i < 700; i++) {
        const x = rr.range(-4, P.w + 4), w = rr.range(-6, 6), v = rr.range(0.55, 1.05);
        P.stroke(ctx => { ctx.moveTo(P.x + x, P.y - 2); ctx.bezierCurveTo(P.x + x + w, P.y + P.h * 0.33, P.x + x - w, P.y + P.h * 0.66, P.x + x + w * 0.5, P.y + P.h + 2); },
          { a: grey(v * 0.88), h: v * 0.6 }, rr.range(0.6, 1.4));
      }
      P.all({ a: P.lin(0, P.h * 0.2, 0, P.h * 0.55, [[0, 'rgba(255,255,255,0)'], [0.5, 'rgba(255,255,255,0.28)'], [1, 'rgba(255,255,255,0)']]), r: P.lin(0, P.h * 0.2, 0, P.h * 0.55, [[0, 0.5], [0.5, 0.36], [1, 0.5]]) });
    },
    goldEngr(P) {
      P.all({ a: '#e6e6e6', m: 1, h: 0.5, r: 0.26, mt: 1 });
      P.rect(-2, 0, P.w + 4, 9, { a: '#f4f4f4', h: 0.7 }); P.rect(-2, P.h - 9, P.w + 4, 9, { a: '#f4f4f4', h: 0.7 });
      P.rect(-2, 9, P.w + 4, 2, { a: '#8a8a8a', h: 0.35 }); P.rect(-2, P.h - 11, P.w + 4, 2, { a: '#8a8a8a', h: 0.35 });
      const cy = P.h / 2;
      for (let x = -20; x < P.w + 40; x += 40) {
        P.stroke(ctx => {
          ctx.moveTo(P.x + x, P.y + cy);
          ctx.bezierCurveTo(P.x + x + 8, P.y + cy - 38, P.x + x + 26, P.y + cy - 30, P.x + x + 22, P.y + cy - 10);
          ctx.bezierCurveTo(P.x + x + 19, P.y + cy + 2, P.x + x + 8, P.y + cy - 4, P.x + x + 12, P.y + cy - 14);
          ctx.moveTo(P.x + x, P.y + cy);
          ctx.bezierCurveTo(P.x + x - 8, P.y + cy + 38, P.x + x - 26, P.y + cy + 30, P.x + x - 22, P.y + cy + 10);
          ctx.bezierCurveTo(P.x + x - 19, P.y + cy - 2, P.x + x - 8, P.y + cy + 4, P.x + x - 12, P.y + cy + 14);
        }, { a: '#8c8c8c', h: 0.32, r: 0.45 }, 3);
        P.fill(ctx => ctx.arc(P.x + x + 20, P.y + cy + 24, 4, 0, TAU), { h: 0.72, a: '#f8f8f8' });
        P.fill(ctx => ctx.arc(P.x + x - 20 + 40, P.y + cy - 24, 4, 0, TAU), { h: 0.72, a: '#f8f8f8' });
      }
    },
    horse(P) { P.all({ a: '#d2d2d2', m: 1, h: 0.5, r: 0.62, mt: 0 }); },
    tiger(P) {
      P.all({ a: '#d88a2a', m: 0, h: 0.5, r: 0.95, mt: 0 });
      P.rect(-2, P.h - 30, P.w + 4, 32, { a: P.lin(0, P.h - 30, 0, P.h - 14, [[0, 'rgba(240,228,200,0)'], [1, '#f0e4c8']]) });
      const rr = Rng(13);
      for (let x = 0; x < P.w + 20; x += rr.range(14, 22)) {
        const w = rr.range(3, 6), sk = rr.range(-10, 10), len = rr.range(50, 100);
        P.fill(ctx => { ctx.moveTo(P.x + x, P.y - 4); ctx.quadraticCurveTo(P.x + x + sk + w, P.y + len * 0.5, P.x + x + sk * 0.3, P.y + len); ctx.quadraticCurveTo(P.x + x + sk - w, P.y + len * 0.5, P.x + x - w, P.y - 4); }, { a: '#1a1410', h: 0.46 });
      }
    },
    leopard(P) {
      P.all({ a: '#d4a85a', m: 0, h: 0.5, r: 0.95, mt: 0 });
      const rr = Rng(17);
      for (let i = 0; i < 46; i++) {
        const x = rr.range(0, P.w), y = rr.range(0, P.h), s = rr.range(4, 7);
        P.stroke(ctx => ctx.ellipse(P.x + x, P.y + y, s, s * 0.8, rr.range(0, PI), 0.3, TAU - 0.6), { a: '#2a1a10' }, 2.2);
        P.fill(ctx => ctx.ellipse(P.x + x, P.y + y, s * 0.5, s * 0.4, 0, 0, TAU), { a: '#b07a38' });
      }
    },
    feather(P) {
      P.all({ a: '#c8a060', m: 0, h: 0.5, r: 0.6, mt: 0 });
      for (let y = -10; y < P.h + 20; y += 22) {
        P.fill(ctx => { ctx.moveTo(P.x - 2, P.y + y + 14); ctx.lineTo(P.x + P.w / 2, P.y + y); ctx.lineTo(P.x + P.w + 2, P.y + y + 14); ctx.lineTo(P.x + P.w + 2, P.y + y + 22); ctx.lineTo(P.x + P.w / 2, P.y + y + 8); ctx.lineTo(P.x - 2, P.y + y + 22); ctx.closePath(); }, { a: '#3a2618', h: 0.46 });
        P.fill(ctx => { ctx.moveTo(P.x - 2, P.y + y + 12); ctx.lineTo(P.x + P.w / 2, P.y + y - 2); ctx.lineTo(P.x + P.w + 2, P.y + y + 12); ctx.lineTo(P.x + P.w + 2, P.y + y + 14); ctx.lineTo(P.x + P.w / 2, P.y + y); ctx.lineTo(P.x - 2, P.y + y + 14); ctx.closePath(); }, { a: '#f0dcb0' });
      }
      P.all({ a: P.lin(0, 0, P.w, 0, [[0, 'rgba(40,24,10,0.5)'], [0.25, 'rgba(40,24,10,0)'], [0.75, 'rgba(40,24,10,0)'], [1, 'rgba(40,24,10,0.5)']]) });
      P.rect(P.w / 2 - 2, -2, 4, P.h + 4, { a: '#f4ecd8', h: 0.64 });
    },
    blade(P) {
      const W = P.w, H = P.h;
      P.all({ a: '#ccd2d8', m: 0.15, h: 0.5, r: 0.16, mt: 1 });
      P.all({ a: P.lin(0, 0, W, 0, [[0, '#f6f8fa'], [0.1, '#e6eaee'], [0.16, '#b8bec6'], [0.5, '#d4dae0'], [0.84, '#b8bec6'], [0.9, '#e6eaee'], [1, '#f6f8fa']]),
        r: P.lin(0, 0, W, 0, [[0, 0.08], [0.12, 0.12], [0.3, 0.2], [0.5, 0.14], [0.7, 0.2], [0.88, 0.12], [1, 0.08]]) });
      // 刃纹（波浪）
      P.stroke(ctx => { for (let y = 0; y <= H; y += 4) { const x = W * 0.2 + Math.sin(y * 0.12) * 3 + Math.sin(y * 0.31) * 1.5; if (y === 0) ctx.moveTo(P.x + x, P.y + y); else ctx.lineTo(P.x + x, P.y + y); } }, { a: 'rgba(255,255,255,0.5)' }, 2);
      P.stroke(ctx => { for (let y = 0; y <= H; y += 4) { const x = W * 0.8 - Math.sin(y * 0.12) * 3 - Math.sin(y * 0.31) * 1.5; if (y === 0) ctx.moveTo(P.x + x, P.y + y); else ctx.lineTo(P.x + x, P.y + y); } }, { a: 'rgba(255,255,255,0.5)' }, 2);
      P.rect(W / 2 - 1, -2, 2, H + 4, { h: 0.62, a: '#eef2f6' });
      // 刃根金错纹（下端 = t 小）
      const g = { a: '#d8b050', m: 0, h: 0.58, r: 0.3, mt: 1 };
      const y0 = H - 46;
      P.stroke(ctx => { ctx.moveTo(P.x + W * 0.3, P.y + y0); ctx.bezierCurveTo(P.x + W * 0.8, P.y + y0 + 8, P.x + W * 0.2, P.y + y0 + 18, P.x + W * 0.7, P.y + y0 + 28); ctx.bezierCurveTo(P.x + W * 0.9, P.y + y0 + 34, P.x + W * 0.5, P.y + y0 + 40, P.x + W * 0.5, P.y + H); }, g, 2.4);
      P.fill(ctx => ctx.arc(P.x + W * 0.5, P.y + y0 - 6, 4, 0, TAU), g);
    },
    // 肤色反照率按 SKIN_EXP 压低（游戏光照下浅肤色不再过曝发白）；名将面部贴图用同一系数，脸与身体一致
    skin(P) { P.all({ a: grey(SKIN_EXP), m: 1, h: 0.5, r: 0.7, mt: 0 }); },
    faceA(P) { paintFaceInto(P, GENERIC_FACES[0]); },
    faceB(P) { paintFaceInto(P, GENERIC_FACES[1]); },
    faceC(P) { paintFaceInto(P, GENERIC_FACES[2]); },
    faceD(P) { paintFaceInto(P, GENERIC_FACES[3]); },
    leather(P) {
      P.all({ a: '#bcbcbc', m: 1, h: 0.5, r: 0.72, mt: 0 });
      const st = { a: '#6a6a6a', h: 0.4 };
      P.stroke(ctx => { ctx.setLineDash([4, 3]); ctx.moveTo(P.x - 2, P.y + 6); ctx.lineTo(P.x + P.w - 6, P.y + 6); ctx.lineTo(P.x + P.w - 6, P.y + P.h - 6); ctx.lineTo(P.x - 2, P.y + P.h - 6); }, st, 1.4);
    },
    wood(P) { P.all({ a: '#c4c4c4', m: 1, h: 0.5, r: 0.66, mt: 0 }); },
    wrap(P) {
      P.all({ a: '#5a5a5a', m: 1, h: 0.3, r: 0.85, mt: 0 });
      for (let x = -P.h; x < P.w + P.h; x += 14) {
        P.fill(ctx => { ctx.moveTo(P.x + x, P.y + P.h + 2); ctx.lineTo(P.x + x + 10, P.y + P.h + 2); ctx.lineTo(P.x + x + 10 + P.h + 4, P.y - 2); ctx.lineTo(P.x + x + P.h + 4, P.y - 2); ctx.closePath(); },
          { a: P.lin(0, 0, 14, 0, [[0, '#a0a0a0'], [0.5, '#e8e8e8'], [1, '#a0a0a0']]), h: 0.7 });
      }
    },
    tassel(P) {
      P.all({ a: '#c8c8c8', m: 1, h: 0.5, r: 0.85, mt: 0 });
      const rr = Rng(19);
      for (let i = 0; i < 260; i++) { const x = rr.range(0, P.w), v = rr.range(0.55, 1); P.stroke(ctx => { ctx.moveTo(P.x + x, P.y - 2); ctx.lineTo(P.x + x + rr.range(-3, 3), P.y + P.h + 2); }, { a: grey(v), h: v * 0.7 }, rr.range(0.7, 1.6)); }
    },
    gold(P) {
      P.all({ a: '#eaeaea', m: 1, h: 0.5, r: 0.26, mt: 1 });
      const rr = Rng(23);
      for (let i = 0; i < 14; i++) {
        const cx = rr.range(0, P.w), cy = rr.range(0, P.h), s = rr.range(8, 16);
        P.stroke(ctx => { for (let k = 0; k <= 30; k++) { const a = k / 30 * TAU * 1.5, r0 = s * (1 - k / 34); const x = P.x + cx + Math.cos(a) * r0, y = P.y + cy + Math.sin(a) * r0; if (!k) ctx.moveTo(x, y); else ctx.lineTo(x, y); } }, { a: '#9a9a9a', h: 0.35 }, 2);
        P.fill(ctx => ctx.arc(P.x + cx, P.y + cy, 2.5, 0, TAU), { h: 0.75, a: '#ffffff' });
      }
    },
    rattan(P) {
      P.all({ a: '#7a7a7a', m: 1, h: 0.3, r: 0.6, mt: 0 });
      const s = 12;
      for (let y = 0; y < P.h; y += s) for (let x = 0; x < P.w; x += s) {
        const hor = ((x + y) / s) % 2 === 0;
        P.fill(ctx => ctx.rect(P.x + x + 1, P.y + y + 1, s - 2, s - 2), { a: P.lin(hor ? 0 : x, hor ? y : 0, hor ? 0 : x + s, hor ? y + s : 0, [[0, '#b8b8b8'], [0.5, '#f0f0f0'], [1, '#b0b0b0']]), h: 0.72 });
      }
    },
    white(P) { P.all({ a: '#ffffff', m: 1, h: 0.5, r: 0.6, mt: 0 }); },
    rivet(P) {
      P.all({ a: '#cfd1d6', m: 1, h: 0.5, r: 0.34, mt: 1 });
      P.rect(-2, 0, P.w + 4, 4, { a: '#eeeeee', h: 0.66 }); P.rect(-2, P.h - 4, P.w + 4, 4, { a: '#eeeeee', h: 0.66 });
      for (const y of [12, P.h - 12]) for (let x = 6; x < P.w; x += 20) P.fill(ctx => ctx.arc(P.x + x, P.y + y, 4.5, 0, TAU), { a: P.rad(x - 1.5, y - 1.5, 0.5, 5, [[0, '#ffffff'], [1, '#8a8c92']]), h: P.rad(x, y, 0, 5, [[0, 0.9], [1, 0.52]]) });
    },
    buckle(P) {
      const c = P.w / 2, R0 = P.w / 2 - 4;
      P.all({ a: '#d8d8d8', m: 1, h: 0.45, r: 0.3, mt: 1 });
      P.fill(ctx => ctx.arc(P.x + c, P.y + c, R0, 0, TAU), { a: P.rad(c - 14, c - 14, 4, R0 * 1.3, [[0, '#ffffff'], [1, '#a8a8a8']]), h: P.rad(c, c, R0 * 0.2, R0, [[0, 0.62], [0.85, 0.66], [1, 0.5]]) });
      P.stroke(ctx => ctx.arc(P.x + c, P.y + c, R0 - 6, 0, TAU), { a: '#808080', h: 0.4 }, 2);
      // 鬃毛花瓣
      for (let k = 0; k < 18; k++) { const a = k / 18 * TAU; P.fill(ctx => ctx.ellipse(P.x + c + Math.cos(a) * (R0 - 15), P.y + c + Math.sin(a) * (R0 - 15), 7, 3.2, a, 0, TAU), { a: '#f0f0f0', h: 0.72 }); }
      // 兽面：眉、眼、鼻、獠牙口环
      const F = { a: '#a0a0a0', h: 0.36 }, B = { a: '#ffffff', h: 0.8 };
      for (const sx of [-1, 1]) {
        P.fill(ctx => ctx.ellipse(P.x + c + sx * 13, P.y + c - 8, 7, 5, sx * 0.3, 0, TAU), B);
        P.fill(ctx => ctx.arc(P.x + c + sx * 13, P.y + c - 8, 2.6, 0, TAU), F);
        P.stroke(ctx => { ctx.moveTo(P.x + c + sx * 4, P.y + c - 16); ctx.quadraticCurveTo(P.x + c + sx * 16, P.y + c - 26, P.x + c + sx * 24, P.y + c - 14); }, B, 3);
      }
      P.fill(ctx => { ctx.moveTo(P.x + c, P.y + c - 6); ctx.lineTo(P.x + c + 7, P.y + c + 8); ctx.lineTo(P.x + c - 7, P.y + c + 8); ctx.closePath(); }, B);
      P.stroke(ctx => ctx.arc(P.x + c, P.y + c + 18, 9, 0.15 * PI, 0.85 * PI), F, 2.5);
      P.stroke(ctx => ctx.arc(P.x + c, P.y + c + 22, 7, 0, TAU), B, 2.4);
    },
    mirror(P) {
      const c = P.w / 2, R0 = P.w / 2 - 3;
      P.all({ a: '#d0d0d0', m: 1, h: 0.45, r: 0.3, mt: 1 });
      P.fill(ctx => ctx.arc(P.x + c, P.y + c, R0, 0, TAU), { a: '#e2e2e2', h: P.rad(c, c, 0, R0, [[0, 0.6], [0.92, 0.62], [1, 0.48]]) });
      P.fill(ctx => ctx.arc(P.x + c, P.y + c, R0 * 0.58, 0, TAU), { a: P.rad(c - 12, c - 14, 2, R0 * 0.7, [[0, '#ffffff'], [1, '#c4c8cc']]), r: 0.07, h: P.rad(c, c, 0, R0 * 0.58, [[0, 0.68], [1, 0.6]]) });
      for (const k of [0.62, 0.8]) P.stroke(ctx => ctx.arc(P.x + c, P.y + c, R0 * k, 0, TAU), { a: '#8a8a8a', h: 0.42 }, 2);
      for (let i = 0; i < 24; i++) { const a = i / 24 * TAU; P.fill(ctx => ctx.arc(P.x + c + Math.cos(a) * R0 * 0.9, P.y + c + Math.sin(a) * R0 * 0.9, 2.4, 0, TAU), { a: '#f6f6f6', h: 0.75 }); }
    },
    lion(P) {
      const c = P.w / 2;
      P.all({ a: '#d6d6d6', m: 1, h: 0.45, r: 0.3, mt: 1 });
      for (let k = 0; k < 26; k++) { const a = k / 26 * TAU, r0 = 30 + (k % 2) * 8; P.stroke(ctx => { ctx.moveTo(P.x + c + Math.cos(a) * 20, P.y + c + Math.sin(a) * 20); ctx.quadraticCurveTo(P.x + c + Math.cos(a + 0.2) * r0, P.y + c + Math.sin(a + 0.2) * r0, P.x + c + Math.cos(a + 0.1) * (r0 + 18), P.y + c + Math.sin(a + 0.1) * (r0 + 18)); }, { a: '#f2f2f2', h: 0.72 }, 6); }
      P.fill(ctx => ctx.ellipse(P.x + c, P.y + c + 4, 26, 30, 0, 0, TAU), { a: P.rad(c - 8, c - 8, 2, 34, [[0, '#ffffff'], [1, '#a8a8a8']]), h: P.rad(c, c, 2, 30, [[0, 0.78], [1, 0.55]]) });
      for (const sx of [-1, 1]) { P.fill(ctx => ctx.ellipse(P.x + c + sx * 11, P.y + c - 6, 6, 3.5, sx * 0.35, 0, TAU), { a: '#6a6a6a', h: 0.35 }); }
      P.fill(ctx => ctx.ellipse(P.x + c, P.y + c + 10, 7, 5, 0, 0, TAU), { a: '#8a8a8a', h: 0.85 });
      P.stroke(ctx => { ctx.moveTo(P.x + c - 12, P.y + c + 22); ctx.quadraticCurveTo(P.x + c, P.y + c + 30, P.x + c + 12, P.y + c + 22); }, { a: '#6a6a6a', h: 0.35 }, 2.5);
    },
    scutum(P) {
      const W = P.w, H = P.h, g = { a: '#e0b444', m: 0, h: 0.62, r: 0.4, mt: 0.8 };
      P.all({ a: '#e8e8e8', m: 1, h: 0.5, r: 0.7, mt: 0 });
      P.stroke(ctx => ctx.rect(P.x + 7, P.y + 7, W - 14, H - 14), g, 6);
      P.stroke(ctx => ctx.rect(P.x + 16, P.y + 16, W - 32, H - 32), g, 1.6);
      const cx = W / 2, cy = H / 2;
      // 雷纹与鹰翼
      for (const sy of [-1, 1]) for (const sx of [-1, 1]) {
        P.stroke(ctx => { ctx.moveTo(P.x + cx + sx * 24, P.y + cy + sy * 6); ctx.bezierCurveTo(P.x + cx + sx * 60, P.y + cy + sy * 10, P.x + cx + sx * 80, P.y + cy + sy * 50, P.x + cx + sx * 96, P.y + cy + sy * 84); }, g, 4);
        for (let k = 0; k < 4; k++) P.stroke(ctx => { ctx.moveTo(P.x + cx + sx * (34 + k * 14), P.y + cy + sy * (8 + k * 9)); ctx.lineTo(P.x + cx + sx * (30 + k * 14), P.y + cy + sy * (30 + k * 14)); }, g, 3);
        P.stroke(ctx => { ctx.moveTo(P.x + cx + sx * 6, P.y + cy + sy * 26); ctx.lineTo(P.x + cx + sx * 16, P.y + cy + sy * 46); ctx.lineTo(P.x + cx + sx * 4, P.y + cy + sy * 52); ctx.lineTo(P.x + cx + sx * 14, P.y + cy + sy * 80); }, g, 3);
      }
      P.rect(cx - 4, 18, 8, H - 36, g);
      P.fill(ctx => ctx.arc(P.x + cx, P.y + cy, 26, 0, TAU), { a: P.rad(cx - 8, cy - 8, 2, 30, [[0, '#f4f6f8'], [1, '#8a8e96']]), m: 0, mt: 1, r: 0.28, h: P.rad(cx, cy, 0, 26, [[0, 0.9], [1, 0.6]]) });
    },
    shieldRound(P) {
      const c = P.w / 2, R0 = c - 3, g = { a: '#d8ac44', m: 0, h: 0.62, r: 0.4, mt: 0.8 };
      P.all({ a: '#e4e4e4', m: 1, h: 0.5, r: 0.75, mt: 0 });
      // 木板纹（染色）
      for (let x = 0; x < P.w; x += 22) P.rect(x, -2, 1.5, P.h + 4, { a: 'rgba(0,0,0,0.18)', h: 0.42 });
      P.stroke(ctx => ctx.arc(P.x + c, P.y + c, R0 - 4, 0, TAU), { a: '#b8b8b8', m: 0, mt: 1, r: 0.35, h: 0.7 }, 8);
      for (let i = 0; i < 20; i++) { const a = i / 20 * TAU; P.fill(ctx => ctx.arc(P.x + c + Math.cos(a) * (R0 - 4), P.y + c + Math.sin(a) * (R0 - 4), 2.6, 0, TAU), { a: '#f0f0f0', m: 0, mt: 1, h: 0.85 }); }
      P.stroke(ctx => ctx.arc(P.x + c, P.y + c, R0 * 0.74, 0, TAU), g, 3);
      for (let i = 0; i < 8; i++) {
        const a = i / 8 * TAU;
        P.stroke(ctx => { ctx.moveTo(P.x + c + Math.cos(a) * 34, P.y + c + Math.sin(a) * 34); ctx.quadraticCurveTo(P.x + c + Math.cos(a + 0.35) * 62, P.y + c + Math.sin(a + 0.35) * 62, P.x + c + Math.cos(a + 0.1) * 84, P.y + c + Math.sin(a + 0.1) * 84); }, g, 3);
      }
      P.fill(ctx => ctx.arc(P.x + c, P.y + c, 30, 0, TAU), { a: P.rad(c - 9, c - 9, 2, 34, [[0, '#f6f6f6'], [1, '#8a8e96']]), m: 0, mt: 1, r: 0.28, h: P.rad(c, c, 0, 30, [[0, 0.95], [1, 0.6]]) });
    },
  };
  function lamellarPaint(P, lace) {
    const plateCol = lace ? '#a6aab2' : '#d8dadf', plateM = lace ? 0 : 1;
    const cord = lace ? { a: '#f0f0f0', m: 1, mt: 0, r: 0.85, h: 0.62 } : { a: '#7a2a22', m: 0, mt: 0, r: 0.85, h: 0.62 };
    P.all({ a: '#26262a', m: plateM, h: 0.28, r: 0.7, mt: 0.5 });
    const pw = 24, step = 27, ph = 34;
    for (let row = Math.ceil(P.h / step); row >= -1; row--) {
      const y = row * step - 4, off = (row % 2) ? pw / 2 : 0;
      for (let x = -pw; x < P.w + pw; x += pw) {
        const x0 = x + off + 0.8, w = pw - 1.6;
        const path = ctx => {
          const X = P.x + x0, Y = P.y + y, r = 5;
          ctx.moveTo(X, Y); ctx.lineTo(X + w, Y); ctx.lineTo(X + w, Y + ph - r); ctx.quadraticCurveTo(X + w, Y + ph, X + w - r, Y + ph);
          ctx.lineTo(X + r, Y + ph); ctx.quadraticCurveTo(X, Y + ph, X, Y + ph - r); ctx.closePath();
        };
        P.fill(path, { a: P.lin(x0, 0, x0 + w, 0, [[0, shade(plateCol, -0.28)], [0.35, shade(plateCol, 0.18)], [1, shade(plateCol, -0.34)]]), m: plateM, mt: 1, r: 0.32,
          h: P.lin(x0, 0, x0 + w, 0, [[0, 0.52], [0.4, 0.76], [1, 0.5]]) });
        P.fill(ctx => ctx.arc(P.x + x0 + w / 2, P.y + y + ph - 7, 2, 0, TAU), { a: '#f4f4f4', h: 0.85, m: plateM });
        P.stroke(ctx => { ctx.moveTo(P.x + x0 + 3, P.y + y + 6); ctx.lineTo(P.x + x0 + w - 3, P.y + y + 6); }, { a: 'rgba(40,40,46,0.5)', h: 0.45 }, 1);
      }
      // 甲片之间的编绳
      P.stroke(ctx => { for (let x = -pw; x < P.w + pw; x += 6) { const yy = P.y + y + 4 + ((x / 6) % 2 ? 3 : -3); if (x === -pw) ctx.moveTo(P.x + x, yy); else ctx.lineTo(P.x + x, yy); } }, cord, 3.2);
    }
  }

  // 后处理：噪声、拉丝、木纹等
  function* postAtlas(R, D) {
    const A = D.a, Hh = D.h, Ro = D.r;
    postRegion(R, D, 'metal', (x, y, I) => { const n = fbm(x * 0.6, y * 0.03, 2, 3) - 0.5; addV(A, I, n * 18); addV(Ro, I, -n * 26 + (h2(x, y, 9) - 0.5) * 8); }); yield;
    postRegion(R, D, 'brushed', (x, y, I) => { const n = fbm(x * 0.7, y * 0.035, 2, 4) - 0.5; addV(A, I, n * 22); addV(Ro, I, -n * 30); }); yield;
    postRegion(R, D, 'rivet', (x, y, I) => { const n = fbm(x * 0.6, y * 0.04, 2, 5) - 0.5; addV(A, I, n * 16); }); yield;
    for (const nm of ['cloth', 'hem', 'brocade', 'plaid']) {
      postRegion(R, D, nm, (x, y, I) => {
        const w = ((x + y) & 1) ? 1 : -1, th = (x % 3 === 0 ? -1 : 0) + (y % 3 === 0 ? -1 : 0);
        const n = fbm(x * 0.05, y * 0.05, 3, 6) - 0.5;
        addV(A, I, w * 3 + th * 4 + n * 22); addV(Hh, I, w * 6 + th * 6);
      });
      yield;
    }
    postRegion(R, D, 'skin', (x, y, I) => { const n = fbm(x * 0.08, y * 0.08, 3, 7) - 0.5, p = h2(x, y, 8) - 0.5; A[I] = clamp(A[I] + n * 10 + 2, 0, 255); A[I + 1] = clamp(A[I + 1] + n * 8, 0, 255); A[I + 2] = clamp(A[I + 2] + n * 6 - 2, 0, 255); addV(Hh, I, p * 1.5); }); yield;
    postRegion(R, D, 'leather', (x, y, I) => { const n = fbm(x * 0.25, y * 0.25, 3, 10) - 0.5; addV(A, I, n * 36); addV(Hh, I, n * 40); addV(Ro, I, n * 30); }); yield;
    postRegion(R, D, 'wood', (x, y, I) => { const g = Math.sin((x + fbm(x * 0.03, y * 0.01, 3, 11) * 40) * 0.55); addV(A, I, g * 16 + (fbm(x * 0.2, y * 0.02, 2, 12) - 0.5) * 30); addV(Hh, I, g * 12); }); yield;
    postRegion(R, D, 'horse', (x, y, I) => { const n = fbm(x * 0.02, y * 0.3, 2, 13) - 0.5, d = fbm(x * 0.04, y * 0.06, 3, 14) - 0.5; addV(A, I, n * 14 + d * 18); addV(Hh, I, n * 16); }); yield;
    postRegion(R, D, 'gold', (x, y, I) => { addV(A, I, (fbm(x * 0.1, y * 0.1, 2, 15) - 0.5) * 16); }); yield;
    postRegion(R, D, 'goldEngr', (x, y, I) => { addV(A, I, (fbm(x * 0.4, y * 0.05, 2, 16) - 0.5) * 12); }); yield;
    postRegion(R, D, 'blade', (x, y, I) => { const n = fbm(x * 0.5, y * 0.02, 2, 18) - 0.5; addV(A, I, n * 10); }); yield;
    postRegion(R, D, 'tiger', (x, y, I) => { const n = fbm(x * 0.15, y * 0.4, 2, 19) - 0.5; addV(A, I, n * 30); addV(Hh, I, n * 30); }); yield;
    postRegion(R, D, 'leopard', (x, y, I) => { const n = fbm(x * 0.15, y * 0.4, 2, 20) - 0.5; addV(A, I, n * 26); addV(Hh, I, n * 26); }); yield;
    postRegion(R, D, 'scutum', (x, y, I) => { const n = fbm(x * 0.04, y * 0.04, 3, 21) - 0.5; addV(A, I, n * 16); }); yield;
    postRegion(R, D, 'shieldRound', (x, y, I) => { const n = fbm(x * 0.04, y * 0.04, 3, 22) - 0.5; addV(A, I, n * 18); }); yield;
  }

  // 图集生成（生成器：每步 yield，便于 preload 分片执行）
  function* genAtlas() {
    const t0 = now();
    const R = packRegions();
    const mk = () => { const c = document.createElement('canvas'); c.width = c.height = ATLAS_N; return c; };
    const cv = { a: mk(), m: mk(), h: mk(), r: mk(), mt: mk() };
    const ctxs = {};
    for (const k in cv) { ctxs[k] = cv[k].getContext('2d', { willReadFrequently: true }); ctxs[k].fillStyle = k === 'a' ? '#808080' : grey(k === 'm' ? 1 : k === 'h' ? 0.5 : k === 'r' ? 0.7 : 0); ctxs[k].fillRect(0, 0, ATLAS_N, ATLAS_N); }
    yield;
    for (const name in R) {
      PAINT[name](Painter(ctxs, R[name]));
      yield;
    }
    const D = {};
    // 分条读回（单次读回整张 1024² 画布在忙碌的页面里可能超过 50ms）
    for (const k in cv) {
      const out = new Uint8ClampedArray(ATLAS_N * ATLAS_N * 4), st = ATLAS_N / 4;
      for (let y = 0; y < ATLAS_N; y += st) { out.set(ctxs[k].getImageData(0, y, ATLAS_N, st).data, y * ATLAS_N * 4); yield; }
      D[k] = out; cv[k].width = 1;
    }
    yield* postAtlas(R, D);
    // 高度 → 法线、AO
    const N = ATLAS_N, NN = N * N;
    const hgt = new Float32Array(NN);
    for (let i = 0; i < NN; i++) { hgt[i] = D.h[i * 4] / 255; if ((i & 262143) === 262143) yield; }
    // 分片的可分离盒式模糊
    function* blur(src, rad, out) {
      const tmp = new Float32Array(NN), k = 1 / (2 * rad + 1);
      for (let y = 0; y < N; y++) { let s = 0; const o = y * N; for (let x = -rad; x <= rad; x++) s += src[o + clamp(x, 0, N - 1)]; for (let x = 0; x < N; x++) { tmp[o + x] = s * k; s += src[o + Math.min(N - 1, x + rad + 1)] - src[o + Math.max(0, x - rad)]; } if ((y & 255) === 255) yield; }
      for (let x = 0; x < N; x++) { let s = 0; for (let y = -rad; y <= rad; y++) s += tmp[clamp(y, 0, N - 1) * N + x]; for (let y = 0; y < N; y++) { out[y * N + x] = s * k; s += tmp[Math.min(N - 1, y + rad + 1) * N + x] - tmp[Math.max(0, y - rad) * N + x]; } if ((x & 255) === 255) yield; }
    }
    const hs = new Float32Array(NN), hb = new Float32Array(NN);
    yield* blur(hgt, 1, hs);
    yield* blur(hgt, 5, hb);
    const map = new Uint8Array(NN * 4), nrm = new Uint8Array(NN * 4), orm = new Uint8Array(NN * 4);
    const S = 10;
    for (let y = 0; y < N; y++) {
      const ty = N - 1 - y;                    // DataTexture 第 0 行 = v 0（画布底边）
      for (let x = 0; x < N; x++) {
        const i = y * N + x, I = i * 4, O = (ty * N + x) * 4;
        map[O] = D.a[I]; map[O + 1] = D.a[I + 1]; map[O + 2] = D.a[I + 2]; map[O + 3] = D.m[I];
        const xl = x > 0 ? i - 1 : i, xr = x < N - 1 ? i + 1 : i, yu = y > 0 ? i - N : i, yd = y < N - 1 ? i + N : i;
        const dx = (hs[xr] - hs[xl]) * S, dy = (hs[yd] - hs[yu]) * S;
        const inv = 1 / Math.sqrt(dx * dx + dy * dy + 1);
        nrm[O] = (-dx * inv * 0.5 + 0.5) * 255; nrm[O + 1] = (dy * inv * 0.5 + 0.5) * 255; nrm[O + 2] = (inv * 0.5 + 0.5) * 255; nrm[O + 3] = 255;
        const cav = Math.max(0, hb[i] - hs[i]);
        orm[O] = clamp(1 - cav * 4, 0.35, 1) * 255; orm[O + 1] = D.r[I]; orm[O + 2] = D.mt[I]; orm[O + 3] = 255;
      }
      if ((y & 63) === 63) yield;
    }
    const tex = (data, srgb) => {
      const t = new THREE.DataTexture(data, N, N, THREE.RGBAFormat, THREE.UnsignedByteType);
      t.generateMipmaps = true; t.minFilter = THREE.LinearMipmapLinearFilter; t.magFilter = THREE.LinearFilter;
      t.anisotropy = 4; t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping; t.needsUpdate = true;
      return t;
    };
    ATLAS = { R, map: tex(map), normalMap: tex(nrm), ormMap: tex(orm) };
    STATS.textures += 3;
    STATS.atlasMs = Math.round(now() - t0);
  }
  function runSync(gen) { for (const _ of gen) { void _; /* 同步跑完 */ } }
  function atlas() { if (!ATLAS) runSync(genAtlas()); return ATLAS; }
  function REG(name) { const A = atlas(); const r = A.R[name]; if (!r) throw new Error('no atlas region ' + name); return r; }

  // ======================================================== 环境贴图与材质 ==
  let ENV = null;
  function envMap() {
    if (ENV) return ENV;
    const t0 = now();
    const W = 256, H = 128;
    const c = document.createElement('canvas'); c.width = W; c.height = H;
    const g = c.getContext('2d');
    const sky = g.createLinearGradient(0, 0, 0, H / 2);
    sky.addColorStop(0, '#4a6aa0'); sky.addColorStop(0.65, '#a8b8cc'); sky.addColorStop(1, '#e6dccb');
    g.fillStyle = sky; g.fillRect(0, 0, W, H / 2);
    const gr = g.createLinearGradient(0, H / 2, 0, H);
    // 地面一侧不要太暗：刀刃 / 金属朝下的面反射地面，过暗会变成黑色剪影
    gr.addColorStop(0, '#9a8a70'); gr.addColorStop(0.25, '#7a6a56'); gr.addColorStop(1, '#5a4e42');
    g.fillStyle = gr; g.fillRect(0, H / 2, W, H / 2);
    // 云带
    const rr = Rng(31);
    for (let i = 0; i < 26; i++) {
      const x = rr.range(0, W), y = rr.range(H * 0.18, H * 0.44), w = rr.range(20, 60), h = rr.range(3, 8);
      g.fillStyle = 'rgba(255,250,240,' + rr.range(0.15, 0.4).toFixed(2) + ')';
      g.beginPath(); g.ellipse(x, y, w, h, 0, 0, TAU); g.fill();
    }
    // 太阳（方位约 40°，仰角约 35°）+ 一侧较暗的天空，让金属有明暗对比
    const sx = W * 0.62, sy = H * 0.3;
    const sun = g.createRadialGradient(sx, sy, 0, sx, sy, 34);
    sun.addColorStop(0, 'rgba(255,252,236,1)'); sun.addColorStop(0.18, 'rgba(255,240,200,0.9)'); sun.addColorStop(1, 'rgba(255,230,180,0)');
    g.fillStyle = sun; g.fillRect(0, 0, W, H);
    const dark = g.createLinearGradient(0, 0, W, 0);
    dark.addColorStop(0, 'rgba(20,24,40,0.35)'); dark.addColorStop(0.3, 'rgba(20,24,40,0)'); dark.addColorStop(0.9, 'rgba(20,24,40,0)'); dark.addColorStop(1, 'rgba(20,24,40,0.35)');
    g.fillStyle = dark; g.fillRect(0, 0, W, H);
    // 地平线一道亮带（金属轮廓更清楚）
    g.fillStyle = 'rgba(255,244,220,0.5)'; g.fillRect(0, H / 2 - 2, W, 3);
    const t = new THREE.CanvasTexture(c);
    t.mapping = THREE.EquirectangularReflectionMapping;
    t.needsUpdate = true;
    ENV = t;
    STATS.textures++;
    STATS.envMs = Math.round(now() - t0);
    return ENV;
  }

  // 轮廓光（所有人物材质共用的 uniform）
  const RIM = { color: { value: null }, k: { value: 0.55 } };
  function patchMaterial(mat, tag) {
    if (!RIM.color.value) RIM.color.value = new THREE.Color(1.0, 0.9, 0.78);
    if (!mat.userData.team) mat.userData.team = { value: new THREE.Color(1, 1, 1) };
    mat.onBeforeCompile = function (sh) {
      // 地面贴合：蒙皮后低于地面的顶点（倒地时的披风、腰裙、盔缨、长发）压平在地面上，不穿地。
      // 每个人物的开关与地面高度写在骨骼贴图的空余位置（第 GROUND_SLOT 个「骨骼」矩阵，没有顶点引用它；
      // 骨骼贴图按 2 的幂分配，34 骨的贴图可容 64 个矩阵），由 Figure 包装的 skeleton.update 每帧写入
      // → 共用材质、不增加绘制与程序，CPU 端的 getVertexPosition / 射线检测不受影响。
      sh.vertexShader = sh.vertexShader.replace('#include <skinning_vertex>', [
        '#include <skinning_vertex>',
        '#ifdef USE_SKINNING',
        '  { mat4 sgG = getBoneMatrix( ' + GROUND_SLOT.toFixed(1) + ' ); if ( sgG[0][0] > 0.5 ) transformed.y = max( transformed.y, sgG[0][1] ); }',
        '#endif'].join('\n'));
      sh.uniforms.uRimColor = RIM.color;
      sh.uniforms.uRimK = RIM.k;
      sh.uniforms.uTeam = mat.userData.team;
      sh.fragmentShader = 'uniform vec3 uRimColor;\nuniform float uRimK;\nuniform vec3 uTeam;\n' + sh.fragmentShader
        .replace('#include <map_fragment>', [
          '#ifdef USE_MAP',
          '  vec4 sgTex = texture2D( map, vMapUv );',
          '  diffuseColor.rgb *= sgTex.rgb;',
          '#endif'].join('\n'))
        .replace('#include <color_fragment>', [
          '#if defined( USE_COLOR_ALPHA )',
          '  vec3 sgVC = mix( vColor.rgb, vColor.rgb * uTeam, vColor.a );',
          '#elif defined( USE_COLOR )',
          '  vec3 sgVC = vColor.rgb;',
          '#endif',
          '#if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA )',
          '  #ifdef USE_MAP',
          '  diffuseColor.rgb *= mix( vec3( 1.0 ), sgVC, sgTex.a );',
          '  #else',
          '  diffuseColor.rgb *= sgVC;',
          '  #endif',
          '#endif'].join('\n'))
        .replace('#include <emissivemap_fragment>', [
          '#include <emissivemap_fragment>',
          '  { float sgR = 1.0 - clamp( dot( normal, normalize( vViewPosition ) ), 0.0, 1.0 );',
          '    totalEmissiveRadiance += uRimColor * ( sgR * sgR * sgR * uRimK ) * ( 0.3 + 0.7 * diffuseColor.rgb ); }'].join('\n'));
    };
    mat.customProgramCacheKey = function () { return 'sgfig-' + (tag || 'kit'); };
    return mat;
  }
  let KIT = null, HEADTEX = null;
  // 共用装具材质：顶点色染色 + 图集
  function kitMaterial() {
    if (KIT) return KIT;
    const A = atlas();
    KIT = patchMaterial(new THREE.MeshStandardMaterial({
      map: A.map, normalMap: A.normalMap, normalScale: new THREE.Vector2(1, 1),
      roughnessMap: A.ormMap, metalnessMap: A.ormMap, aoMap: A.ormMap, aoMapIntensity: 0.8,
      roughness: 1, metalness: 1, envMap: envMap(), envMapIntensity: 0.85, vertexColors: true,
    }), 'kit');
    KIT.name = 'SG.Figure.kit';
    return KIT;
  }
  // 头部（面部贴图）用的平坦法线 / ORM 小贴图：与装具材质同一套着色器特性，共用程序
  function headTextures() {
    if (HEADTEX) return HEADTEX;
    const flat = new Uint8Array(16 * 4), orm = new Uint8Array(16 * 4);
    for (let i = 0; i < 16; i++) { flat.set([128, 128, 255, 255], i * 4); orm.set([255, 178, 0, 255], i * 4); }
    const mk = d => { const t = new THREE.DataTexture(d, 4, 4, THREE.RGBAFormat); t.needsUpdate = true; t.magFilter = t.minFilter = THREE.LinearFilter; return t; };
    HEADTEX = { normal: mk(flat), orm: mk(orm) };
    STATS.textures += 2;
    return HEADTEX;
  }
  function faceMaterial(tex) {
    const H = headTextures();
    const m = patchMaterial(new THREE.MeshStandardMaterial({
      map: tex, normalMap: H.normal, roughnessMap: H.orm, metalnessMap: H.orm, aoMap: H.orm, aoMapIntensity: 0.8,
      roughness: 1, metalness: 1, envMap: envMap(), envMapIntensity: 0.85, vertexColors: true,
    }), 'kit');
    m.name = 'SG.Figure.face';
    return m;
  }
  // 势力色材质（按颜色缓存；共用同一着色器程序，只有 uTeam 不同）
  const TEAM_MATS = new Map();
  function teamMaterial(hexc) {
    const key = hexc ? hex(col(hexc)) : '#ffffff';
    let m = TEAM_MATS.get(key);
    if (m) return m;
    const base = kitMaterial();
    if (key === '#ffffff') { TEAM_MATS.set(key, base); return base; }
    m = base.clone();
    m.userData = {};
    patchMaterial(m, 'kit');
    const c = col(key);
    m.userData.team.value.setRGB(c[0], c[1], c[2]);
    m.name = 'SG.Figure.kit' + key;
    TEAM_MATS.set(key, m);
    return m;
  }
  function setRim(color, strength) {
    if (!RIM.color.value) RIM.color.value = new THREE.Color(1.0, 0.9, 0.78);
    if (color != null) { const c = col(color); RIM.color.value.setRGB(c[0], c[1], c[2]); }
    if (strength != null) RIM.k.value = strength;
  }

  // ======================================================== 网格构建器 ==
  // 索引网格：位置、UV（图集）、顶点色、蒙皮（4 骨）。构建后计算平滑法线，并在同一平滑组内焊接同位顶点的法线。
  // 权重 w：骨骼下标（数字）或 [[骨, 权重], ...]。
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

  class GB {
    constructor() {
      this.P = []; this.UV = []; this.C = []; this.SI = []; this.SW = []; this.I = []; this.G = [];
      this.grp = 0; this.xf = null; this.nv = 0;
    }
    group() { return ++this.grp; }
    // 当前变换（仅作用于位置）：{ m: THREE.Matrix4 }
    v(p, uv, c, w, g) {
      let x = p[0], y = p[1], z = p[2];
      if (this.xf) {
        const e = this.xf.elements;
        const X = e[0] * x + e[4] * y + e[8] * z + e[12], Y = e[1] * x + e[5] * y + e[9] * z + e[13], Z = e[2] * x + e[6] * y + e[10] * z + e[14];
        x = X; y = Y; z = Z;
      }
      this.P.push(x, y, z);
      this.UV.push(uv[0], uv[1]);
      c = c ? (Array.isArray(c) ? c : col(c)) : [1, 1, 1];
      this.C.push(c[0], c[1], c[2], c[3] || 0);
      if (typeof w === 'number') { this.SI.push(w, 0, 0, 0); this.SW.push(1, 0, 0, 0); }
      else if (!w || !w.length) { this.SI.push(0, 0, 0, 0); this.SW.push(1, 0, 0, 0); }
      else {
        // 取权重最大的 4 个并归一化
        const ws = w.length > 4 ? w.slice().sort((a, b) => b[1] - a[1]).slice(0, 4) : w;
        let s = 0; for (const e of ws) s += e[1];
        s = s || 1;
        for (let i = 0; i < 4; i++) { const e = ws[i]; this.SI.push(e ? e[0] : 0); this.SW.push(e ? e[1] / s : 0); }
      }
      this.G.push(g || 0);
      return this.nv++;
    }
    tri(a, b, c) { this.I.push(a, b, c); }
    quad(a, b, c, d) { this.I.push(a, b, c, a, c, d); }
    // 合并另一个构建器（偏移索引）
    append(o) {
      const base = this.nv;
      this.P.push(...o.P); this.UV.push(...o.UV); this.C.push(...o.C); this.SI.push(...o.SI); this.SW.push(...o.SW);
      for (const g of o.G) this.G.push(g ? g + this.grp + 1000 : 0);
      for (const i of o.I) this.I.push(i + base);
      this.nv += o.nv; this.grp += o.grp + 1001;
    }
    get tris() { return this.I.length / 3; }
    build(opts) {
      opts = opts || {};
      const g = new THREE.BufferGeometry();
      const pos = new Float32Array(this.P);
      g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(this.UV), 2));
      g.setAttribute('color', new THREE.BufferAttribute(new Float32Array(this.C), 4));
      if (opts.skin !== false) {
        g.setAttribute('skinIndex', new THREE.BufferAttribute(new Uint16Array(this.SI), 4));
        g.setAttribute('skinWeight', new THREE.BufferAttribute(new Float32Array(this.SW), 4));
      }
      const idx = this.nv > 65535 ? new Uint32Array(this.I) : new Uint16Array(this.I);
      g.setIndex(new THREE.BufferAttribute(idx, 1));
      g.computeVertexNormals();
      this.weld(g);
      g.computeBoundingBox();
      g.computeBoundingSphere();
      return g;
    }
    // 同一平滑组内、位置相同的顶点：法线取平均（消除接缝）
    weld(g) {
      const pos = g.attributes.position.array, nrm = g.attributes.normal.array;
      const map = new Map();
      const q = 1e4;
      for (let i = 0; i < this.nv; i++) {
        const gr = this.G[i];
        if (!gr) continue;
        const k = gr + ':' + Math.round(pos[i * 3] * q) + ',' + Math.round(pos[i * 3 + 1] * q) + ',' + Math.round(pos[i * 3 + 2] * q);
        let l = map.get(k);
        if (!l) map.set(k, (l = []));
        l.push(i);
      }
      for (const l of map.values()) {
        if (l.length < 2) continue;
        let x = 0, y = 0, z = 0;
        for (const i of l) { x += nrm[i * 3]; y += nrm[i * 3 + 1]; z += nrm[i * 3 + 2]; }
        const n = Math.sqrt(x * x + y * y + z * z) || 1;
        for (const i of l) { nrm[i * 3] = x / n; nrm[i * 3 + 1] = y / n; nrm[i * 3 + 2] = z / n; }
      }
    }
  }

  // ---------------------------------------------------------------- 放样 --
  // 截面序列：{ c: 中心, X: 截面局部 x 轴, Z: 截面局部 z 轴（θ = 0 的方向）, rx, rz, sq（超椭圆指数，2 = 椭圆）,
  //             t: 纵向贴图坐标 0..1, w: 权重（或 wf(p) 函数）, col: 颜色, pole: true（收为一点）, off(θ) → 额外径向偏移(米) }
  // o：{ n: 环向分段, reg: 图集区域, mirror: true（u = |θ|/π，前后对称、无接缝）, u0, u1（贴图 u 范围）,
  //      col: 颜色 或 (θ, sec, k) → 颜色, flip: 反转绕序, wf(p, θ, k): 权重函数, arc: [θ0, θ1]（只做一段弧，开放）}
  function superE(s, e) { return Math.sign(s) * Math.pow(Math.abs(s), 2 / e); }
  function loft(gb, secs, o) {
    const n = o.n, reg = o.uvc ? null : REG(o.reg || 'white');
    const grp = o.group || gb.group();
    const arc = o.arc;
    const ring = [];
    const cnt = arc ? n + 1 : n;
    for (let k = 0; k < secs.length; k++) {
      const s = secs[k];
      const row = [];
      const t = s.t != null ? s.t : k / (secs.length - 1);
      if (s.pole) {
        const p = s.c;
        const uv = o.uvc || ruv(reg, o.u0 != null ? (o.u0 + o.u1) / 2 : 0.5, t);
        const w = o.wf ? o.wf(p, 0, k) : (s.w !== undefined ? s.w : o.w);
        const c = typeof o.col === 'function' ? o.col(0, s, k) : (s.col || o.col);
        const id = gb.v(p, uv, c, w, grp);
        for (let i = 0; i < cnt; i++) row.push(id);
        ring.push(row);
        continue;
      }
      const sq = s.sq || o.sq || 2;
      for (let i = 0; i < cnt; i++) {
        const th = arc ? lerp(arc[0], arc[1], i / n) : -PI + (i / n) * TAU;
        const sn = Math.sin(th), cs = Math.cos(th);
        const ex = sq === 2 ? sn : superE(sn, sq), ez = sq === 2 ? cs : superE(cs, sq);
        let rx = s.rx, rz = s.rz;
        let ox = ex * rx, oz = ez * rz;
        if (s.off || o.off) {
          const d = (s.off ? s.off(th, s, k) : 0) + (o.off ? o.off(th, s, k) : 0);
          // 近似法线方向
          const nx = sn / (rx || 1e-4), nz = cs / (rz || 1e-4), nl = Math.sqrt(nx * nx + nz * nz) || 1;
          ox += nx / nl * d; oz += nz / nl * d;
        }
        const p = o.ptf ? o.ptf(th, s, k) : [s.c[0] + s.X[0] * ox + s.Z[0] * oz, s.c[1] + s.X[1] * ox + s.Z[1] * oz, s.c[2] + s.X[2] * ox + s.Z[2] * oz];
        let u;
        if (arc) u = i / n;
        else u = o.mirror === false ? i / n : Math.abs(th) / PI;
        if (o.u0 != null) u = lerp(o.u0, o.u1, u);
        const uv = o.uvc || ruv(reg, u, t);
        const w = o.wf ? o.wf(p, th, k) : (s.w !== undefined ? s.w : o.w);
        const c = typeof o.col === 'function' ? o.col(th, s, k) : (s.col || o.col);
        row.push(gb.v(p, uv, c, w, grp));
      }
      ring.push(row);
    }
    const flip = !!o.flip;
    for (let k = 0; k < ring.length - 1; k++) {
      const A = ring[k], B = ring[k + 1];
      const lim = arc ? n : n;
      for (let i = 0; i < lim; i++) {
        const i1 = arc ? i + 1 : (i + 1) % n;
        const a = A[i], b = A[i1], c = B[i1], d = B[i];
        if (a === b && c === d) continue;
        if (a === b) { flip ? gb.tri(a, d, c) : gb.tri(a, c, d); continue; }
        if (c === d) { flip ? gb.tri(a, c, b) : gb.tri(a, b, c); continue; }
        if (flip) gb.quad(a, d, c, b); else gb.quad(a, b, c, d);
      }
    }
    // 端面
    if (o.capStart && !secs[0].pole) capRing(gb, ring[0], secs[0], reg, grp, !flip, o);
    if (o.capEnd && !secs[secs.length - 1].pole) capRing(gb, ring[ring.length - 1], secs[secs.length - 1], reg, grp, flip, o);
    return ring;
  }
  function capRing(gb, row, s, reg, grp, flip, o) {
    const cg = gb.group();
    const P = gb.P;
    // 端面中心 = 环上顶点的平均（已是变换后的坐标）；端面用独立顶点（硬边），添加时暂时关闭变换
    let cx = 0, cy = 0, cz = 0;
    for (const i of row) { cx += P[i * 3]; cy += P[i * 3 + 1]; cz += P[i * 3 + 2]; }
    cx /= row.length; cy /= row.length; cz /= row.length;
    const sx = gb.xf; gb.xf = null;
    const wOf = q => (o.wf ? o.wf(q, 0, 0) : (s.w !== undefined ? s.w : o.w));
    const ci = row[0];
    const id = gb.v([cx, cy, cz], [gb.UV[ci * 2], gb.UV[ci * 2 + 1]], [gb.C[ci * 4], gb.C[ci * 4 + 1], gb.C[ci * 4 + 2], gb.C[ci * 4 + 3]], wOf([cx, cy, cz]), cg);
    const ids = row.map(i => gb.v([P[i * 3], P[i * 3 + 1], P[i * 3 + 2]], [gb.UV[i * 2], gb.UV[i * 2 + 1]], [gb.C[i * 4], gb.C[i * 4 + 1], gb.C[i * 4 + 2], gb.C[i * 4 + 3]], wOf([P[i * 3], P[i * 3 + 1], P[i * 3 + 2]]), cg));
    gb.xf = sx;
    for (let i = 0; i < ids.length; i++) {
      const a = ids[i], b = ids[(i + 1) % ids.length];
      if (flip) gb.tri(id, b, a); else gb.tri(id, a, b);
    }
  }
  // 沿折线 / 曲线的管（平行移动标架）：pts 点列，r(i, s) 半径（或数组），o 同 loft（另有 rz 比例、twist）
  function frames(pts, up) {
    const N = pts.length, T = [], X = [], Z = [];
    for (let i = 0; i < N; i++) {
      const a = pts[Math.max(0, i - 1)], b = pts[Math.min(N - 1, i + 1)];
      T.push(v3norm(v3sub(b, a)));
    }
    let ref = up || [0, 0, 1];
    if (Math.abs(v3dot(ref, T[0])) > 0.95) ref = [1, 0, 0];
    let x = v3norm(v3cross(ref, T[0]));
    for (let i = 0; i < N; i++) {
      if (i > 0) {
        // 平行移动
        const ax = v3cross(T[i - 1], T[i]), l = v3len(ax);
        if (l > 1e-6) x = v3rot(x, v3mul(ax, 1 / l), Math.asin(clamp(l, -1, 1)) * (v3dot(T[i - 1], T[i]) < 0 ? -1 : 1));
        x = v3norm(v3sub(x, v3mul(T[i], v3dot(x, T[i]))));
      }
      X.push(x);
      Z.push(v3norm(v3cross(x, T[i])));
    }
    return { T, X, Z };
  }
  function tube(gb, pts, o) {
    const F = frames(pts, o.up);
    const secs = [];
    const N = pts.length;
    for (let i = 0; i < N; i++) {
      const s = N > 1 ? i / (N - 1) : 0;
      const r = typeof o.r === 'function' ? o.r(s, i) : Array.isArray(o.r) ? o.r[i] : o.r;
      const rz = r * (o.rz != null ? (typeof o.rz === 'function' ? o.rz(s, i) : o.rz) : 1);
      let X = F.X[i], Z = F.Z[i];
      if (o.twist) { const a = o.twist * s; X = v3rot(X, F.T[i], a); Z = v3rot(Z, F.T[i], a); }
      const sec = { c: pts[i], X, Z, rx: r, rz, t: o.t ? o.t(s) : s, w: typeof o.w === 'function' ? o.w(s, pts[i]) : (o.w || 0) };
      if (r <= 1e-5 && (i === 0 || i === N - 1)) sec.pole = true;
      if (o.col && typeof o.col !== 'function') sec.col = o.col;
      if (o.colS) sec.col = o.colS(s);
      secs.push(sec);
    }
    return loft(gb, secs, o);
  }
  // 绕 Y 轴的旋转体：prof = [[r, y], ...]（自下而上），中心 c，o 同 loft（可给 axis 改变朝向：{ X, Y, Z } 三轴）
  function lathe(gb, prof, o) {
    const c = o.c || [0, 0, 0], AX = o.axes || { X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1] };
    const secs = prof.map((p, k) => {
      const r = p[0], rz = p[2] != null ? p[2] : r;
      const s = { c: v3madd(c, AX.Y, p[1]), X: AX.X, Z: AX.Z, rx: r * (o.sx || 1), rz: rz * (o.sz || 1), t: p[3] != null ? p[3] : k / (prof.length - 1), w: o.w };
      if (r <= 1e-5 && (k === 0 || k === prof.length - 1)) s.pole = true;
      return s;
    });
    return loft(gb, secs, o);
  }
  // 椭球（沿 Y 的纬圈放样），def(p, θ, φ) → 新位置（变形）
  function ellipsoid(gb, c, r, o) {
    const nl = o.nl || 8, secs = [];
    const AX = o.axes || { X: [1, 0, 0], Y: [0, 1, 0], Z: [0, 0, 1] };
    for (let k = 0; k <= nl; k++) {
      const ph = -PI / 2 + PI * (k / nl);
      const y = Math.sin(ph) * r[1], rr = Math.cos(ph);
      const s = { c: v3madd(c, AX.Y, y), X: AX.X, Z: AX.Z, rx: r[0] * rr, rz: r[2] * rr, t: k / nl, w: o.w };
      if (k === 0 || k === nl) s.pole = true;
      secs.push(s);
    }
    return loft(gb, secs, o);
  }

  // ------------------------------------------------------- 双面布片 / 甲片 --
  // fn(s, t) → [x, y, z]（s 横向 0..1，t 纵向 0..1）；构建正反两面（厚度 th，沿曲面法线），并封边。
  // o：{ nu, nv, th, reg, col, colB（背面色）, w: 权重函数 (p, s, t) → w, mirrorS: true（贴图 u = |2s − 1|）,
  //      uv(s, t) → [u, v]（区域内局部坐标）, edge: 封边 }
  function panel(gb, fn, o) {
    const nu = o.nu, nv = o.nv, th = o.th || 0.006, reg = REG(o.reg || 'cloth');
    const P = [], Nn = [];
    for (let j = 0; j <= nv; j++) for (let i = 0; i <= nu; i++) P.push(fn(i / nu, j / nv));
    const at = (i, j) => P[clamp(j, 0, nv) * (nu + 1) + clamp(i, 0, nu)];
    for (let j = 0; j <= nv; j++) for (let i = 0; i <= nu; i++) {
      const du = v3sub(at(i + 1, j), at(i - 1, j)), dv = v3sub(at(i, j + 1), at(i, j - 1));
      let n = v3cross(du, dv);
      if (v3len(n) < 1e-9) n = [0, 0, 1];
      Nn.push(v3norm(n));
    }
    const uvOf = (s, t) => {
      let lc = o.uv ? o.uv(s, t) : [o.mirrorS ? Math.abs(2 * s - 1) : s, t];
      return ruv(reg, lc[0], lc[1]);
    };
    const gF = gb.group(), gBk = gb.group();
    const F = [], B = [];
    const colF = o.col || [1, 1, 1], colB = o.colB || shade(colF, -0.25);
    for (let j = 0; j <= nv; j++) for (let i = 0; i <= nu; i++) {
      const k = j * (nu + 1) + i, p = P[k], n = Nn[k], s = i / nu, t = j / nv;
      const w = typeof o.w === 'function' ? o.w(p, s, t) : (o.w || 0);
      const cf = typeof colF === 'function' ? colF(s, t) : colF;
      const cb = typeof colB === 'function' ? colB(s, t) : colB;
      F.push(gb.v(v3madd(p, n, th / 2), uvOf(s, t), cf, w, gF));
      B.push(gb.v(v3madd(p, n, -th / 2), uvOf(s, t), cb, w, gBk));
    }
    for (let j = 0; j < nv; j++) for (let i = 0; i < nu; i++) {
      const a = j * (nu + 1) + i, b = a + 1, c = a + nu + 2, d = a + nu + 1;
      gb.quad(F[a], F[b], F[c], F[d]);
      gb.quad(B[a], B[d], B[c], B[b]);
    }
    if (o.edge !== false && !gb.noEdges) {
      // 封边：沿四条边连接正反面
      const gE = gb.group();
      const edgeLoop = [];
      for (let i = 0; i <= nu; i++) edgeLoop.push(i);
      for (let j = 1; j <= nv; j++) edgeLoop.push(j * (nu + 1) + nu);
      for (let i = nu - 1; i >= 0; i--) edgeLoop.push(nv * (nu + 1) + i);
      for (let j = nv - 1; j >= 1; j--) edgeLoop.push(j * (nu + 1));
      const ef = [], eb = [];
      for (const k of edgeLoop) {
        const pf = gb.P.slice(F[k] * 3, F[k] * 3 + 3), pb = gb.P.slice(B[k] * 3, B[k] * 3 + 3);
        const uv = [gb.UV[F[k] * 2], gb.UV[F[k] * 2 + 1]];
        const w = typeof o.w === 'function' ? o.w(P[k], 0, 0) : (o.w || 0);
        const ce = typeof colF === 'function' ? colF(0, 0) : (o.colE || colF);
        // 位置已经是变换后的；临时关闭变换
        const sx = gb.xf; gb.xf = null;
        ef.push(gb.v(pf, uv, ce, w, gE)); eb.push(gb.v(pb, uv, ce, w, gE));
        gb.xf = sx;
      }
      const L = edgeLoop.length;
      for (let i = 0; i < L; i++) { const i1 = (i + 1) % L; gb.quad(ef[i], eb[i], eb[i1], ef[i1]); }
    }
    return { F, B, P, N: Nn };
  }

  // ------------------------------------------------------------- 刀刃 --
  // 刃沿 stations 逐段放样，每个面一条带（刃口与脊线为硬边，沿长度方向平滑）。
  // st：[{ p: 中心点, e: 刃口方向（单位）, n: 厚度方向（单位）, we: 向刃口的宽度, wb: 向刃背的宽度, th: 半厚, dbl: 双刃 }]
  function blade(gb, st, o) {
    const reg = REG(o.reg || 'blade');
    const prof = (gb.noEdges || gb.detail === 1) ? (s => s.dbl ? [[s.we, 0], [0, s.th * 1.2], [-s.wb, 0], [0, -s.th * 1.2]] : [[s.we, 0], [-s.wb * 0.8, s.th], [-s.wb, 0], [-s.wb * 0.8, -s.th]]) : s => s.dbl
      ? [[s.we, 0], [s.we * 0.42, s.th * 0.85], [0, s.th], [-s.wb * 0.42, s.th * 0.85], [-s.wb, 0], [-s.wb * 0.42, -s.th * 0.85], [0, -s.th], [s.we * 0.42, -s.th * 0.85]]
      : [[s.we, 0], [s.we * 0.3, s.th * 0.75], [-s.wb * 0.85, s.th], [-s.wb, s.th * 0.55], [-s.wb, -s.th * 0.55], [-s.wb * 0.85, -s.th], [s.we * 0.3, -s.th * 0.75]];
    const rows = st.map(s => prof(s).map(q => v3add(v3add(s.p, v3mul(s.e, q[0])), v3mul(s.n, q[1]))));
    const np = rows[0].length, N = st.length;
    const col = o.col || [1, 1, 1];
    // 绕序：(e, n, 长度方向) 为右手系时需要反转
    const sg = v3dot(v3cross(st[0].e, st[0].n), v3sub(st[N - 1].p, st[0].p)) > 0;
    for (let f = 0; f < np; f++) {
      const g = gb.group(), f1 = (f + 1) % np;
      const ids = [];
      for (let k = 0; k < N; k++) {
        const t = k / (N - 1);
        // u：刃口 → 刃背映射到 0..1
        const uA = f / np, uB = (f + 1) / np;
        ids.push([gb.v(rows[k][f], ruv(reg, uA, t), col, o.w, g), gb.v(rows[k][f1], ruv(reg, uB, t), col, o.w, g)]);
      }
      for (let k = 0; k < N - 1; k++) {
        if (sg) gb.quad(ids[k][0], ids[k][1], ids[k + 1][1], ids[k + 1][0]);
        else gb.quad(ids[k][0], ids[k + 1][0], ids[k + 1][1], ids[k][1]);
      }
    }
    // 根部封口
    if (o.capBase !== false) {
      const g = gb.group();
      const c = gb.v(st[0].p, ruv(reg, 0.5, 0), col, o.w, g);
      const ids = rows[0].map(p => gb.v(p, ruv(reg, 0.5, 0), col, o.w, g));
      for (let i = 0; i < np; i++) { if (sg) gb.tri(c, ids[(i + 1) % np], ids[i]); else gb.tri(c, ids[i], ids[(i + 1) % np]); }
    }
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

  // 体型 → 关节位置（人物坐标，米）
  function makeProps(look) {
    const fem = look.sex === 'f';
    const h = (look.height || 1) * (fem ? 0.94 : 1);
    const k = h;
    const build = clamp(look.build || 1, 0.82, 1.3), fat = clamp(look.fat || 0, 0, 1);
    // 体格差异要拉开（真三国无双式：许褚、典韦、张飞一眼可辨）：肩宽约 ±20%、四肢粗细、髋宽随 build / fat
    const bk = fem ? (build - 0.9) * 0.35 : (build - 1) * 0.85;
    const sw = (fem ? 0.86 : 1) * (1 + bk) * (1 + fat * 0.06);                      // 肩宽
    const hw = (fem ? 1.04 : 1) * (1 + bk * 0.4) * (1 + fat * 0.14);                // 髋宽
    const limb = (fem ? 0.86 : 1) * (1 + bk * 1.05) * (1 + fat * 0.22);             // 四肢粗细
    const J = {};
    J.root = [0, 0, 0];
    J.pelvis = [0, 0.995 * k, 0];
    J.spine = [0, 1.09 * k, -0.006];
    J.chest = [0, 1.27 * k, -0.012];
    J.neck = [0, 1.5 * k, -0.022];
    J.head = [0, 1.585 * k, -0.01];
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
    J.plume1 = [0, 1.86 * k, -0.01];
    J.plume2 = [0, 2.0 * k, -0.07];
    J.hair1 = [0, 1.71 * k, -0.1];
    J.shL = J.upperArmL.slice(); J.shR = J.upperArmR.slice(); J.dlL = J.upperArmL.slice(); J.dlR = J.upperArmR.slice();
    return { J, k, sw, hw, limb, fat, fem, build, armDirL: ad, upL, foL, H: 1.86 * k };
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

  // ------------------------------------------------------------ 权重 --
  // 链式权重：沿关节链（点列）投影，在关节附近 ±blend 米内平滑过渡
  function chainW(p, chain, blend) {
    // chain: [[boneIdx, startJoint], ..., [null, endPoint]]
    let best = 0, bestD = 1e9, bestT = 0;
    for (let i = 0; i < chain.length - 1; i++) {
      const a = chain[i][1], b = chain[i + 1][1], ab = v3sub(b, a), l2 = v3dot(ab, ab) || 1e-9;
      const t = clamp(v3dot(v3sub(p, a), ab) / l2, 0, 1);
      const q = v3madd(a, ab, t), d = v3len(v3sub(p, q));
      if (d < bestD - 1e-6) { bestD = d; best = i; bestT = t; }
    }
    const segs = chain.length - 1;
    const a = chain[best][1], b = chain[best + 1][1], L = v3len(v3sub(b, a)) || 1e-6;
    const dist0 = bestT * L, dist1 = (1 - bestT) * L;
    const bi = chain[best][0];
    if (dist0 < blend && best > 0) {
      const k = 0.5 + 0.5 * (dist0 / blend);
      return [[bi, k], [chain[best - 1][0], 1 - k]];
    }
    if (dist1 < blend && best < segs - 1 && chain[best + 1][0] != null) {
      const k = 0.5 + 0.5 * (dist1 / blend);
      return [[bi, k], [chain[best + 1][0], 1 - k]];
    }
    return [[bi, 1]];
  }
  // 身体各部位的权重函数
  function weightsFor(pr) {
    const J = pr.J;
    const spineChain = [[B.pelvis, [0, J.pelvis[1] - 0.2, 0]], [B.spine, J.spine], [B.chest, J.chest], [B.neck, J.neck], [B.head, J.head], [null, [0, J.head[1] + 0.3, 0]]];
    const arm = S => [[B['clav' + S], J['clav' + S]], [B['upperArm' + S], J['upperArm' + S]], [B['foreArm' + S], J['foreArm' + S]], [B['hand' + S], J['hand' + S]], [null, v3madd(J['hand' + S], [pr.armDirL[0] * (S === 'L' ? 1 : -1), pr.armDirL[1], 0], 0.2)]];
    const leg = S => [[B.pelvis, [J['thigh' + S][0] * 0.5, J.pelvis[1] + 0.1, 0]], [B['thigh' + S], J['thigh' + S]], [B['shin' + S], J['shin' + S]], [B['foot' + S], J['foot' + S]], [null, v3add(J['foot' + S], [0, -0.05, 0.25])]];
    const AL = arm('L'), AR = arm('R'), LL = leg('L'), LR = leg('R');
    return {
      torso: p => {
        const w = chainW(p, spineChain, 0.07);
        // 肩部：靠近肩关节的躯干顶点带一点锁骨 / 上臂权重
        for (const S of ['L', 'R']) {
          const d = v3len(v3sub(p, J['upperArm' + S]));
          if (d < 0.12 && Math.abs(p[0]) > 0.09) { const k = (1 - d / 0.12) * 0.6; w.forEach(e => (e[1] *= 1 - k)); w.push([B['clav' + S], k * 0.45], [B['dl' + S], k * 0.55]); }
        }
        return w;
      },
      // 臂：袖根（肩关节内外约 7cm）经由肩部辅助骨过渡到上臂，避免高举时肩部撕裂
      arm: S => p => {
        const sg = S === 'L' ? 1 : -1, d = [pr.armDirL[0] * sg, pr.armDirL[1], 0];
        const s = v3dot(v3sub(p, J['upperArm' + S]), d);
        if (s < 0.08) {
          const k1 = sstep(-0.075, -0.015, s), k2 = sstep(-0.01, 0.08, s);
          return [[B['clav' + S], 1 - k1], [B['dl' + S], k1 * (1 - k2)], [B['upperArm' + S], k1 * k2]];
        }
        return chainW(p, S === 'L' ? AL : AR, 0.05);
      },
      leg: S => p => {
        const w = chainW(p, S === 'L' ? LL : LR, 0.06);
        return w;
      },
      neck: p => chainW(p, spineChain, 0.05),
    };
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

  // ======================================================== 头部与面部 ==
  // 头部局部坐标：原点 = 头骨（颈顶），面朝 +Z。头部在球面参数 (θ 经度：0 = 正前，φ 纬度：0 = 头中心高度) 上变形；
  // 面部贴图按同一参数映射（经度方向做非线性拉伸，让脸部占更多像素），五官位置在参数空间里定义，几何与贴图严格对齐。
  const HEAD_C = [0, 0.115, 0.0];
  const FEAT = { eyeT: 0.42, eyeP: 0.07, browP: 0.25, noseP: -0.22, mouthP: -0.5, chinP: -1.0, hairP: 0.78 };
  // 纬度参数 φ → 头部局部高度 y（米）。五官锚点：发际、眉、眼、鼻尖、口、下巴
  const PHI_Y = [[-PI / 2, -0.022], [-1.45, -0.012], [-1.2, -0.002], [-1.0, 0.01], [-0.5, 0.045], [-0.22, 0.074], [0.07, 0.112], [0.25, 0.13], [0.78, 0.185], [1.2, 0.222], [PI / 2, 0.236]];
  const Y_BOT = -0.022, Y_TOP = 0.236;
  function Yph(ph) {
    if (ph <= PHI_Y[0][0]) return PHI_Y[0][1];
    for (let i = 0; i < PHI_Y.length - 1; i++) { const a = PHI_Y[i], b = PHI_Y[i + 1]; if (ph <= b[0]) return lerp(a[1], b[1], (ph - a[0]) / (b[0] - a[0])); }
    return Y_TOP;
  }
  function headUV(th, ph) {
    const x = clamp(th / PI, -1, 1);
    const u = 0.5 + 0.5 * Math.sign(x) * Math.pow(Math.abs(x), 0.7);
    return [u, clamp01((Yph(ph) - Y_BOT) / (Y_TOP - Y_BOT))];
  }
  function headParams(sp) {
    sp = sp || {};
    const f = sp.face || {}, fem = sp.sex === 'f', nose = sp.nose || {}, ears = sp.ears || {};
    return {
      fem, fat: sp.fat || 0,
      w: (f.w || 1) * (fem ? 0.95 : 1), len: f.len || 1, jaw: (f.jaw || 1) * (fem ? 0.85 : 1), chin: f.chin || 1, cheek: f.cheek || 0, brow: f.brow != null ? f.brow : 0.4, hollow: f.hollow || 0,
      noseLen: (nose.len || 1) * (nose.size || 1) * (fem ? 0.8 : 1), noseW: (nose.w || 1) * (nose.shape === 'broad' || nose.shape === 'flat' ? 1.3 : nose.shape === 'small' || nose.shape === 'button' ? 0.85 : 1),
      bridge: nose.bridge != null ? nose.bridge : 0.5, aquiline: nose.shape === 'aquiline' ? 1 : 0, flat: nose.shape === 'flat' ? 1 : 0, bulb: nose.shape === 'bulb' ? 1 : 0,
      ear: (ears.size || 1), lips: (sp.mouth && sp.mouth.lips) || 1, size: fem ? 1.03 : 1.07, lodF: 1,
    };
  }
  // 头部截面（y, 半宽, 前缘 z, 后缘 z, 前半部方度）
  const HPROF = [
    [-0.022, 0.0, 0.0, 0.0, 2], [-0.014, 0.034, 0.03, -0.03, 2], [-0.004, 0.042, 0.066, -0.035, 2.1], [0.008, 0.05, 0.09, -0.041, 2.1],
    [0.022, 0.058, 0.093, -0.05, 2.15], [0.04, 0.066, 0.098, -0.063, 2.3], [0.058, 0.071, 0.101, -0.076, 2.42], [0.078, 0.076, 0.098, -0.088, 2.52],
    [0.098, 0.079, 0.096, -0.097, 2.55], [0.118, 0.08, 0.097, -0.104, 2.5], [0.132, 0.08, 0.104, -0.107, 2.5], [0.155, 0.08, 0.1, -0.108, 2.4],
    [0.18, 0.074, 0.088, -0.1, 2.3], [0.205, 0.062, 0.068, -0.084, 2.2], [0.222, 0.045, 0.045, -0.062, 2.1], [0.232, 0.025, 0.022, -0.036, 2], [0.236, 0, 0, 0, 2],
  ];
  function hprofAt(y) {
    const R = HPROF;
    if (y <= R[0][0]) return R[0];
    if (y >= R[R.length - 1][0]) return R[R.length - 1];
    let i = 0;
    while (i < R.length - 2 && R[i + 1][0] < y) i++;
    const a = R[Math.max(0, i - 1)], b = R[i], c = R[i + 1], d = R[Math.min(R.length - 1, i + 2)];
    const t = (y - b[0]) / (c[0] - b[0]), t2 = t * t, t3 = t2 * t;
    const cr = k => 0.5 * (2 * b[k] + (-a[k] + c[k]) * t + (2 * a[k] - 5 * b[k] + 4 * c[k] - d[k]) * t2 + (-a[k] + 3 * b[k] - 3 * c[k] + d[k]) * t3);
    return [y, Math.max(0, cr(1)), Math.max(0, cr(2)), Math.min(0, cr(3)), lerp(b[4], c[4], t)];
  }
  function headPoint(th, ph, Hp) {
    let y = Yph(ph);
    // 下半脸长度（以眼线为界）
    const eyeY = 0.112;
    const yy = y < eyeY ? eyeY - (eyeY - y) / (0.94 + 0.06 * Hp.len) : y;
    const pr = hprofAt(yy);
    const fat = Hp.fat || 0;
    let xw = pr[1] * Hp.w * (1 + fat * 0.08 * (1 - sstep(0.06, 0.14, y)));
    const zf = pr[2], zb = pr[3];
    // 下颌：越往下越收（方下巴收得少）
    const low = 1 - sstep(0.0, 0.075, y);
    const jawK = clamp(1.25 - 0.45 * Hp.jaw + fat * 0.3, 0.6, 1.2);
    xw *= 1 + 0.08 * low * (Hp.jaw - 1);
    const sn = Math.sin(th), cs = Math.cos(th);
    let x, z;
    if (cs >= 0) {
      const sq = pr[4];
      const ex = superE(sn, sq), ez = superE(cs, sq);
      const tap = 1 - 0.46 * low * jawK * Math.pow(cs, 1.2);
      x = ex * xw * tap; z = ez * zf;
    } else { x = sn * xw; z = -cs * zb; }
    // 五官起伏（大多沿 +z）
    const ax = Math.abs(x);
    let dz = 0, dn = 0;
    if (cs > 0) {
      // 细部起伏按 LOD 取舍：高精度（lodF = 1）用细致的鼻、唇、眼窝；中低精度顶点太稀，细部会变成尖刺 / 折痕，
      // 只保留宽缓的鼻梁与眉骨隆起，五官交给面部贴图
      const F = Hp.lodF != null ? Hp.lodF : 1;
      // 鼻：鼻根 → 鼻尖 → 鼻底（特征宽度不小于顶点间距的约 2 倍）
      const tipY = 0.074, rootY = 0.122;
      let nh = 0, sig = 0.016;
      if (y >= tipY && y <= rootY + 0.01) { const t = clamp01((y - tipY) / (rootY - tipY)); nh = lerp(0.024, 0.003 + 0.006 * Hp.bridge, Math.pow(t, 0.85)); sig = lerp(0.0165, 0.0115, t); if (Hp.aquiline) nh += 0.004 * gauss(y, 0.1, 0.01); }
      else if (y < tipY) { nh = 0.024 * (1 - sstep(tipY, tipY - 0.015, y)); sig = 0.0165; }
      if (Hp.flat) nh *= 0.65;
      sig *= Hp.noseW * (1 + Hp.bulb * 0.35 * gauss(y, tipY, 0.01));
      const broad = 0.009 * gauss(x, 0, 0.026) * gauss(y, 0.088, 0.024);
      dz += F * nh * Hp.noseLen * Math.exp(-(x * x) / (sig * sig)) + (1 - F) * broad * Hp.noseLen;
      // 鼻翼
      dz += F * 0.006 * gauss(ax, 0.018 * Hp.noseW, 0.008) * gauss(y, 0.068, 0.008);
      // 眼窝与眼球
      dz += lerp(0.4, 1, F) * (-0.01 * gauss(ax, 0.034, 0.018) * gauss(y, 0.112, 0.012)) + F * 0.004 * gauss(ax, 0.034, 0.011) * gauss(y, 0.111, 0.007);
      // 眉弓
      dz += lerp(0.6, 1, F) * (0.003 + 0.004 * Hp.brow) * gauss(y, 0.129, 0.009) * (1 - sstep(0.05, 0.07, ax)) * (1 - 0.3 * gauss(x, 0, 0.01));
      // 唇、口角、下巴、人中、法令
      const lp = Hp.lips * (Hp.fem ? 1.25 : 1) * F;
      dz += 0.0028 * lp * gauss(x, 0, 0.02) * gauss(y, 0.054, 0.0068) + 0.0038 * lp * gauss(x, 0, 0.018) * gauss(y, 0.037, 0.0075);
      dz += F * (-0.0016 * gauss(x, 0, 0.024) * gauss(y, 0.0455, 0.004) - 0.0024 * gauss(ax, 0.026, 0.008) * gauss(y, 0.046, 0.008));
      dz += 0.006 * (0.6 + 0.4 * Hp.chin) * gauss(x, 0, 0.022) * gauss(y, 0.013, 0.011);
      dz += -0.002 * F * gauss(ax, 0.029, 0.009) * gauss(y, 0.06, 0.013);
      // 颧骨、胖瘦、两颊凹陷（沿截面法线）
      dn += (0.004 + 0.005 * Hp.cheek) * gauss(ax, 0.056, 0.016) * gauss(y, 0.095, 0.012);
      dn += 0.014 * fat * gauss(ax, 0.055, 0.022) * gauss(y, 0.05, 0.025) - 0.006 * Hp.hollow * gauss(ax, 0.052, 0.012) * gauss(y, 0.062, 0.013);
    }
    if (dn) { x += sn * dn; z += cs * dn; }
    const hsz = Hp.size || 1;
    return [x * hsz, y * hsz, (z + dz) * hsz];
  }

  // 头部网格（头部局部坐标）。o：{ nLat, nLon, reg（图集区域：士兵的通用脸）, col, w（权重）}
  function buildHead(gb, Hp, o) {
    const nl = o.nLat, nn = o.nLon, grp = gb.group();
    const reg = o.reg ? REG(o.reg) : null;
    const uvOf = (th, ph) => { const q = headUV(th, ph); return reg ? ruv(reg, q[0], q[1]) : q; };
    const rows = [];
    // 经度分布：高精度时正面加密（θ = π·(a·u + (1 − a)·u³)，正脸列距约为背面的 1/3.5），侧面轮廓仍够圆
    const wa = nn >= 24 ? 0.55 : nn >= 14 ? 0.8 : 1;
    const thOf = i => { const u = -1 + 2 * i / nn; return PI * (wa * u + (1 - wa) * u * u * u); };
    for (let k = 0; k <= nl; k++) {
      // 纬度分布：脸部（-1.2..0.5）加密
      const s = k / nl;
      const ph = -PI / 2 + PI * (s + 0.06 * Math.sin(s * TAU));
      const row = [];
      if (k === 0 || k === nl) {
        const p = headPoint(0, ph, Hp);
        const id = gb.v(p, uvOf(0, ph), o.col, o.w, grp);
        for (let i = 0; i <= nn; i++) row.push(id);
      } else {
        for (let i = 0; i <= nn; i++) {
          const th = thOf(i);
          row.push(gb.v(headPoint(th, ph, Hp), uvOf(th, ph), o.col, o.w, grp));
        }
      }
      rows.push(row);
    }
    for (let k = 0; k < nl; k++) for (let i = 0; i < nn; i++) {
      const a = rows[k][i], b = rows[k][i + 1], c = rows[k + 1][i + 1], d = rows[k + 1][i];
      if (a === b) gb.tri(a, c, d); else if (c === d) gb.tri(a, b, c); else gb.quad(a, b, c, d);
    }
    // 耳朵：扁椭球，贴图取面部贴图耳旁的肤色
    if (o.ears !== false) {
      for (const sd of [1, -1]) {
        const th = sd * PI / 2 * 0.95, ph = -0.07;
        const base = headPoint(th, ph, Hp);
        const sz = 0.023 * Hp.ear;
        ellipsoid(gb, [base[0] - sd * 0.002, base[1] - 0.006 * (Hp.ear - 1), base[2] - 0.012], [0.007, sz * (1 + 0.5 * (Hp.ear - 1)), sz * 0.58],
          { nl: o.nLon >= 24 ? 6 : 4, n: o.nLon >= 24 ? 10 : 6, uvc: uvOf(th, ph), col: o.col, w: o.w });
      }
    }
  }

  // ---------------------------------------------------------- 面部绘制 --
  // F：{ skin, eye: { shape, color, size }, brow: { shape, thick, color }, mouth, beard（胡须样式）, beardColor, hair: { style, color },
  //      marks: [...], acc: [...], expr, age, fem, generic（通用脸：皮肤为白、交给顶点色染色）}
  function paintFaceInto(P, F) {
    const W = P.w, Ht = P.h;
    const E = (th, ph) => { const q = headUV(th, ph); return [P.x + q[0] * W, P.y + (1 - q[1]) * Ht]; };
    const sxAt = th => { const d = 0.01; return (E(th + d, 0)[0] - E(th - d, 0)[0]) / (2 * d); };
    const sy = Math.abs(E(0, FEAT.eyeP + 0.02)[1] - E(0, FEAT.eyeP - 0.02)[1]) / 0.04;
    const skin = F.generic ? grey(SKIN_EXP) : hex(col(F.skin || '#e8c09a').map(v => v * SKIN_EXP));
    const fem = !!F.fem, age = F.age || 30;
    const sk = col(skin);
    const dark = (k, a) => 'rgba(' + Math.round(sk[0] * 255 * k) + ',' + Math.round(sk[1] * 255 * k * 0.92) + ',' + Math.round(sk[2] * 255 * k * 0.88) + ',' + a + ')';
    P.all({ a: skin, m: 1, h: 0.5, r: 0.68, mt: 0 });
    // 暖色：双颊、鼻、耳；冷色：眼窝；下巴下方阴影
    const blob = (th, ph, rw, rh, color) => { const c = E(th, ph); const g = (ctx) => { const gr = ctx.createRadialGradient(c[0], c[1], 0, c[0], c[1], Math.max(rw, rh)); gr.addColorStop(0, color); gr.addColorStop(1, color.replace(/[\d.]+\)$/, '0)')); return gr; }; P.fill(ctx => ctx.ellipse(c[0], c[1], rw, rh, 0, 0, TAU), { a: g }); };
    const bl = (F.blush || 1) * (fem ? 1.4 : 1);
    for (const s of [1, -1]) {
      blob(s * 0.75, -0.25, sxAt(0.75) * 0.32, sy * 0.2, 'rgba(210,90,80,' + (0.16 * bl).toFixed(3) + ')');
      blob(s * FEAT.eyeT, FEAT.eyeP, sxAt(FEAT.eyeT) * 0.26, sy * 0.12, dark(0.7, 0.35));
      blob(s * PI / 2, -0.06, sxAt(1.5) * 0.25, sy * 0.25, 'rgba(200,90,80,0.16)');
    }
    blob(0, FEAT.noseP + 0.02, sxAt(0.05) * 0.08, sy * 0.1, 'rgba(210,100,90,0.14)');
    blob(0, -1.2, sxAt(0.3) * 0.9, sy * 0.25, dark(0.75, 0.4));
    // 胡茬 / 须根
    const bs = F.beard || 'none';
    if (!fem && bs !== 'none') {
      const amt = bs === 'stubble' ? 0.32 : 0.2;
      const bc = col(F.beardColor || '#2a2020');
      const bcs = 'rgba(' + Math.round(bc[0] * 160 + 40) + ',' + Math.round(bc[1] * 160 + 50) + ',' + Math.round(bc[2] * 160 + 70) + ',' + amt + ')';
      P.fill(ctx => {
        const pts = [[-1.25, -0.25], [-0.9, -0.42], [-0.45, -0.38], [-0.2, -0.33], [0, -0.32], [0.2, -0.33], [0.45, -0.38], [0.9, -0.42], [1.25, -0.25], [1.4, -0.7], [0.8, -1.3], [0, -1.45], [-0.8, -1.3], [-1.4, -0.7]];
        pts.forEach((q, i) => { const c = E(q[0], q[1]); if (i) ctx.lineTo(c[0], c[1]); else ctx.moveTo(c[0], c[1]); });
        ctx.closePath();
      }, { a: bcs });
    }
    // 头发（发际线以上；髡发只留两侧与后脑）
    const hs = (F.hair && F.hair.style) || 'topknot', hcol = F.hair && F.hair.color ? hex(F.hair.color) : '#1c1816';
    if (hs !== 'bald') {
      const front = hs === 'kunfa' ? 1.2 : FEAT.hairP - ((F.hair && F.hair.recede) || 0) * -0.25;
      const line = th => {
        const a = Math.abs(th);
        if (a < 0.95) return front + (hs === 'kunfa' ? 0 : 0.06 * Math.cos(th * 5) * gauss(a, 0.5, 0.3));      // 美人尖 / 额角
        if (a < 1.45) return lerp(front - 0.15, 0.08, sstep(0.95, 1.45, a));
        if (a < 1.75) return lerp(0.08, 0.15, sstep(1.45, 1.75, a));      // 鬓角
        return lerp(0.1, -0.55, sstep(1.75, 2.6, a));
      };
      P.fill(ctx => {
        const N = 48;
        for (let i = 0; i <= N; i++) { const th = -PI + TAU * i / N; const c = E(th, line(th)); if (i) ctx.lineTo(c[0], c[1]); else ctx.moveTo(c[0], c[1]); }
        const c1 = E(PI, PI / 2), c0 = E(-PI, PI / 2);
        ctx.lineTo(c1[0], c1[1]); ctx.lineTo(c0[0], c0[1]); ctx.closePath();
      }, { a: hcol, m: F.generic ? 0 : 1, r: 0.5 });
      // 发丝
      const rr = Rng(hashStr(hcol));
      for (let i = 0; i < (W > 200 ? 260 : 60); i++) {
        const th = rr.range(-PI, PI), ph0 = line(th), l = rr.range(0.08, 0.25);
        const a = E(th, ph0 + l), b = E(th + rr.range(-0.05, 0.05), ph0 + 0.01);
        P.stroke(ctx => { ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); }, { a: rr.chance(0.5) ? 'rgba(255,255,255,0.12)' : 'rgba(0,0,0,0.25)' }, W > 200 ? 1 : 0.6);
      }
    }
    // 皱纹（年长）
    if (age > 44) {
      const k = clamp01((age - 44) / 25);
      for (const q of [0.5, 0.58]) P.stroke(ctx => { const a = E(-0.45, q), b = E(0, q + 0.02), c = E(0.45, q); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); }, { a: dark(0.6, 0.35 * k) }, W > 200 ? 1.4 : 0.8);
      for (const s of [1, -1]) {
        P.stroke(ctx => { const a = E(s * 0.17, FEAT.noseP - 0.02), b = E(s * 0.27, -0.42), c = E(s * 0.25, -0.58); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); }, { a: dark(0.6, 0.45 * k) }, W > 200 ? 1.6 : 0.8);
        for (let j = 0; j < 3; j++) P.stroke(ctx => { const a = E(s * 0.6, FEAT.eyeP + 0.02 - j * 0.04), b = E(s * 0.7, FEAT.eyeP + 0.03 - j * 0.06); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); }, { a: dark(0.6, 0.3 * k) }, W > 200 ? 1 : 0.6);
      }
    }
    // 眼睛
    const ey = F.eye || {}, shape = ey.shape || 'normal', esz = ey.size || 1;
    const patch = (F.acc || []).includes('eyepatch');
    const lineC = '#1c120e';
    const SH = { normal: [1, 1, 0.05, 1], sharp: [1.05, 0.8, 0.12, 1.15], phoenix: [1.1, 0.78, 0.2, 1.2], round: [0.95, 1.35, 0, 0.9], narrow: [1.05, 0.6, 0.06, 1.1], gentle: [1, 1, -0.06, 0.85], big: [1.08, 1.25, 0, 0.95], sleepy: [1, 0.68, -0.04, 1] };
    const sh = SH[shape] || SH.normal;
    for (const s of [1, -1]) {
      const cx = s * FEAT.eyeT, cy = FEAT.eyeP;
      const hw = 0.19 * sh[0] * esz, hh = 0.05 * sh[1] * esz * (fem ? 1.12 : 1), tilt = sh[2];
      const inner = E(s * (FEAT.eyeT - hw), cy - 0.01), outer = E(s * (FEAT.eyeT + hw), cy + tilt * 0.5 + 0.005);
      const top = E(cx + s * hw * 0.1, cy + hh), bot = E(cx, cy - hh * 0.7);
      if (patch && s > 0) {
        P.fill(ctx => ctx.ellipse(E(cx, cy)[0], E(cx, cy)[1], sxAt(cx) * hw * 1.45, sy * hh * 2.6, 0.1, 0, TAU), { a: '#141214', m: 0, r: 0.4 });
        continue;
      }
      const almond = ctx => { ctx.moveTo(inner[0], inner[1]); ctx.quadraticCurveTo(top[0], top[1] - sy * hh * 0.9 * sh[3], outer[0], outer[1]); ctx.quadraticCurveTo(bot[0], bot[1] + sy * hh * 0.6, inner[0], inner[1]); ctx.closePath(); };
      P.fill(almond, { a: '#e6ddd0', m: 0, r: 0.25 });
      const ic = E(cx + s * hw * 0.05, cy + 0.005), ir = Math.min(sxAt(cx) * hw * 0.5, sy * hh * 1.25);
      P._each({ a: hex(ey.color || '#3a2618'), m: 0, r: 0.2 }, (ctx, st) => { ctx.save(); ctx.beginPath(); almond(ctx); ctx.clip(); ctx.fillStyle = st; ctx.beginPath(); ctx.arc(ic[0], ic[1], ir, 0, TAU); ctx.fill(); ctx.restore(); });
      P._each({ a: '#0c0808' }, (ctx, st) => { ctx.save(); ctx.beginPath(); almond(ctx); ctx.clip(); ctx.fillStyle = st; ctx.beginPath(); ctx.arc(ic[0], ic[1], ir * 0.45, 0, TAU); ctx.fill(); ctx.fillStyle = 'rgba(255,255,255,0.9)'; ctx.beginPath(); ctx.arc(ic[0] - ir * 0.3, ic[1] - ir * 0.35, ir * 0.22, 0, TAU); ctx.fill(); ctx.restore(); });
      // 上眼睑粗线 + 下眼睑细线 + 双眼皮线
      const lw = Math.max(1, W / 170);
      P.stroke(ctx => { ctx.moveTo(inner[0], inner[1]); ctx.quadraticCurveTo(top[0], top[1] - sy * hh * 0.9 * sh[3], outer[0] + s * lw, outer[1] - lw * 0.4); }, { a: lineC, m: 0 }, lw * 1.8);
      P.stroke(ctx => { ctx.moveTo(inner[0], inner[1]); ctx.quadraticCurveTo(bot[0], bot[1] + sy * hh * 0.6, outer[0], outer[1]); }, { a: 'rgba(60,30,24,0.6)', m: 0 }, lw * 0.7);
      P.stroke(ctx => { const a = E(s * (FEAT.eyeT - hw * 0.6), cy + hh * 1.5), b = E(cx, cy + hh * 2.2), c = E(s * (FEAT.eyeT + hw * 0.85), cy + hh * 1.2 + tilt * 0.4); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); }, { a: dark(0.55, 0.6) }, lw * 0.7);
      if (fem) P.stroke(ctx => { ctx.moveTo(top[0], top[1] - sy * hh * 0.5); ctx.lineTo(outer[0] + s * lw * 3, outer[1] - lw * 2.5); }, { a: lineC, m: 0 }, lw);
    }
    if (patch) {
      // 眼罩系带：从左眼斜过额头绕到脑后
      P.stroke(ctx => { const a = E(-PI, 0.55), b = E(-0.9, 0.62), c = E(0.1, 0.5), d = E(FEAT.eyeT, FEAT.eyeP), e = E(1.2, -0.05), f = E(PI, 0.0); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); ctx.lineTo(d[0], d[1]); ctx.quadraticCurveTo(e[0], e[1], f[0], f[1]); }, { a: '#141214', m: 0 }, Math.max(1.5, W / 90));
    }
    // 眉
    const bw = F.brow || {}, bshape = bw.shape || 'straight', bth = (bw.thick || 1), bcol = hex(bw.color || hcol);
    const expr = F.expr || 'neutral';
    const BR = { straight: [0, 0, 0, 1], arched: [0.03, 0.05, 0, 0.9], angled: [-0.02, 0.0, 0.1, 1.05], bushy: [0, 0.02, 0.03, 1.5], thin: [0.01, 0.03, 0.01, 0.55], knit: [-0.04, -0.02, 0.09, 1.15],
      silkworm: [0.02, 0.06, 0.05, 1.5], sword: [-0.015, -0.01, 0.13, 1.15], worried: [0.06, 0.0, -0.05, 0.95] };
    const br = BR[bshape] || BR.straight;
    const fierce = expr === 'fierce' ? 0.035 : expr === 'stern' ? 0.015 : 0;
    for (const s of [1, -1]) {
      const by = FEAT.browP + (bw.y || 0);
      const i0 = E(s * 0.12, by - br[0] * 0 + 0.0 - fierce + (br[0] < 0 ? br[0] : 0)), m0 = E(s * 0.38, by + br[1] + 0.03), o0 = E(s * 0.66, by + br[2] - 0.02);
      const t = sy * 0.035 * bth * br[3] * (fem ? 0.7 : 1);
      P.fill(ctx => {
        ctx.moveTo(i0[0], i0[1] - t * 0.9); ctx.quadraticCurveTo(m0[0], m0[1] - t * 1.4, o0[0], o0[1] - t * 0.25);
        ctx.quadraticCurveTo(m0[0], m0[1] + t * 0.15, i0[0], i0[1] + t * 0.7); ctx.closePath();
      }, { a: bcol, m: 0, r: 0.6 });
      if (br[3] > 1.1 && W > 200) {
        const rr = Rng(hashStr(bshape) + s);
        for (let i = 0; i < 26; i++) { const k = rr.f(), a = [lerp(i0[0], o0[0], k), lerp(i0[1], o0[1], k) - t * 0.5 - Math.sin(k * PI) * t * 0.6]; P.stroke(ctx => { ctx.moveTo(a[0], a[1] + t * 0.6); ctx.lineTo(a[0] + s * rr.range(2, 6), a[1] - t * rr.range(0.2, 0.9)); }, { a: bcol, m: 0 }, 1.2); }
      }
    }
    // 鼻孔与鼻梁阴影
    // 鼻孔：小而淡（远看不成「猪鼻」）；鼻翼两侧一抹阴影勾出鼻形（中低精度几何没有鼻翼时靠它）
    for (const s of [1, -1]) {
      const c = E(s * 0.11, FEAT.noseP - 0.065); P.fill(ctx => ctx.ellipse(c[0], c[1], sxAt(0.11) * 0.034, sy * 0.014, s * 0.45, 0, TAU), { a: dark(0.45, 0.5) });
      blob(s * 0.2, FEAT.noseP + 0.02, sxAt(0.2) * 0.06, sy * 0.1, dark(0.7, 0.22));
    }
    P.stroke(ctx => { const a = E(-0.1, 0.18), b = E(-0.11, FEAT.noseP + 0.02); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); }, { a: dark(0.75, 0.35) }, Math.max(1, W / 120));
    // 嘴
    const mo = F.mouth || {}, mw = 0.3 * (mo.w || 1) * (fem ? 0.85 : 1);
    const smile = expr === 'smile' ? 0.03 : expr === 'sly' ? 0.015 : expr === 'fierce' || expr === 'stern' ? -0.02 : 0;
    const my = FEAT.mouthP + (mo.y || 0);
    const lc = fem ? '#a8323c' : mix(skin, '#8a3a32', 0.55);
    P.fill(ctx => { const a = E(-mw, my + smile * 0.6), b = E(0, my - 0.075 * (mo.lips || 1)), c = E(mw, my + smile * (expr === 'sly' ? 1.6 : 0.6)); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); ctx.quadraticCurveTo(E(0, my)[0], E(0, my)[1], a[0], a[1]); }, { a: fem ? lc : hex(mix(lc, skin, 0.35)), m: 0, r: 0.4 });
    P.fill(ctx => { const a = E(-mw, my + smile * 0.6), b = E(0, my + 0.055), c = E(mw, my + smile * 0.6); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); ctx.quadraticCurveTo(E(0, my + 0.01)[0], E(0, my + 0.01)[1], a[0], a[1]); }, { a: hex(mix(lc, skin, fem ? 0.1 : 0.45)), m: 0, r: 0.4 });
    P.stroke(ctx => { const a = E(-mw * 1.05, my + smile), b = E(0, my - 0.005), c = E(mw * 1.05, my + smile * (expr === 'sly' ? 2 : 1)); ctx.moveTo(a[0], a[1]); ctx.quadraticCurveTo(b[0], b[1], c[0], c[1]); }, { a: '#3a1a16', m: 0 }, Math.max(1, W / 140));
    // 面部标记
    const marks = F.marks || [];
    const mk = (color, lw, pts) => P.stroke(ctx => pts.forEach((q, i) => { const c = E(q[0], q[1]); if (i) ctx.lineTo(c[0], c[1]); else ctx.moveTo(c[0], c[1]); }), { a: color, m: 0 }, lw);
    const L = Math.max(1, W / 110);
    if (marks.includes('scar')) mk('rgba(150,60,55,0.85)', L, [[-0.85, 0.2], [-0.55, -0.15], [-0.4, -0.38]]);
    if (marks.includes('scars')) { mk('rgba(150,60,55,0.8)', L, [[0.5, 0.35], [0.8, -0.2]]); mk('rgba(150,60,55,0.8)', L, [[-0.3, -0.6], [-0.65, -0.85]]); }
    if (marks.includes('mole')) { const c = E(0.32, -0.36); P.fill(ctx => ctx.arc(c[0], c[1], L * 1.2, 0, TAU), { a: '#3a2218', m: 0 }); }
    if (marks.includes('woad')) for (const s of [1, -1]) { mk('rgba(40,70,160,0.85)', L * 1.6, [[s * 0.25, 0.42], [s * 0.6, 0.2], [s * 0.85, -0.25]]); mk('rgba(40,70,160,0.8)', L * 1.2, [[s * 0.45, -0.3], [s * 0.7, -0.55], [s * 0.5, -0.8]]); }
    if (marks.includes('warpaint')) for (const s of [1, -1]) { mk('rgba(190,40,30,0.85)', L * 2.5, [[s * 0.2, -0.05], [s * 0.85, -0.12]]); mk('rgba(240,235,220,0.85)', L * 1.4, [[s * 0.25, -0.18], [s * 0.8, -0.26]]); }
    if (marks.includes('tattoo-wa')) for (const s of [1, -1]) { for (let j = 0; j < 3; j++) mk('rgba(40,40,60,0.75)', L, [[s * (0.55 + j * 0.08), 0.05], [s * (0.62 + j * 0.08), -0.35]]); }
    if (marks.includes('tattoo-yi')) { for (let j = 0; j < 4; j++) mk('rgba(30,40,70,0.75)', L, [[-0.4 + j * 0.25, 0.55], [-0.4 + j * 0.25, 0.4]]); mk('rgba(30,40,70,0.75)', L, [[-0.3, -0.75], [0, -0.85], [0.3, -0.75]]); }
    if (marks.includes('freckles')) { const rr = Rng(5); for (let i = 0; i < 30; i++) { const c = E(rr.range(-0.7, 0.7), rr.range(-0.35, 0.0)); P.fill(ctx => ctx.arc(c[0], c[1], L * 0.6, 0, TAU), { a: 'rgba(150,80,40,0.45)' }); } }
    if (marks.includes('scales')) { for (let i = 0; i < 12; i++) { const c = E(-0.9 + (i % 4) * 0.12, 0.3 - Math.floor(i / 4) * 0.12); P.stroke(ctx => ctx.arc(c[0], c[1], L * 2, 0, PI), { a: 'rgba(60,90,70,0.6)', m: 0 }, L * 0.8); } }
  }
  // 士兵用的 4 张通用脸（图集）
  const GENERIC_FACES = [
    { generic: true, eye: { shape: 'normal' }, brow: { shape: 'straight', thick: 1.1, color: '#2a221e' }, beard: 'none', expr: 'stern', hair: { style: 'topknot', color: '#1c1816' } },
    { generic: true, eye: { shape: 'sharp' }, brow: { shape: 'angled', thick: 1.2, color: '#2a221e' }, beard: 'short', beardColor: '#2a2020', expr: 'fierce', hair: { style: 'topknot', color: '#1c1816' } },
    { generic: true, eye: { shape: 'narrow' }, brow: { shape: 'bushy', thick: 1.2, color: '#2a221e' }, beard: 'full', beardColor: '#2a2020', expr: 'stern', age: 46, hair: { style: 'topknot', color: '#1c1816' } },
    { generic: true, eye: { shape: 'round' }, brow: { shape: 'knit', thick: 1.3, color: '#2a221e' }, beard: 'stubble', beardColor: '#2a2020', expr: 'fierce', marks: ['scar'], hair: { style: 'topknot', color: '#1c1816' } },
  ];
  // 名将面部贴图（按外貌缓存）
  const FACE_CACHE = new Map();
  function faceTexture(sp, size) {
    const key = (sp.key || sp.name || '?') + '|' + size;
    let t = FACE_CACHE.get(key);
    if (t) { t.userData.refs++; return t; }
    const c = document.createElement('canvas'); c.width = c.height = size;
    const ctx = c.getContext('2d');
    const P = Painter({ a: ctx }, { px: 0, py: 0, pw: size, ph: size });
    paintFaceInto(P, faceDesc(sp));
    t = new THREE.CanvasTexture(c);
    t.anisotropy = 4;
    t.userData = { refs: 1, key };
    FACE_CACHE.set(key, t);
    STATS.textures++;
    return t;
  }
  function releaseFace(t) {
    if (!t || !t.userData || !t.userData.key) return;
    if (--t.userData.refs <= 0) { FACE_CACHE.delete(t.userData.key); t.dispose(); STATS.textures--; }
  }
  // 外貌参数 → 面部绘制描述
  function faceDesc(sp) {
    return {
      skin: sp.skin, fem: sp.sex === 'f', age: sp.age, expr: sp.expr, blush: sp.blush,
      eye: { shape: sp.eyes && sp.eyes.shape, color: sp.eyes && sp.eyes.color, size: sp.eyes && sp.eyes.size },
      brow: { shape: sp.brows && sp.brows.shape, thick: sp.brows && sp.brows.thick, color: sp.brows && sp.brows.color, y: sp.brows && sp.brows.y ? sp.brows.y * 0.4 : 0 },
      mouth: sp.mouth, beard: sp.beard && sp.beard.style, beardColor: sp.beard && sp.beard.color,
      hair: { style: sp.hair && sp.hair.style, color: sp.hair && sp.hair.color, recede: sp.hair && sp.hair.recede },
      marks: sp.marks || [], acc: sp.acc || [],
    };
  }

  // ======================================================== 外观设定 ==
  // 外貌参数（头像）→ 三维服饰层：躯干甲、腰裙、袖、护肩、护臂、腿、胫甲、靴、披风、前垂、领巾、腰带、冠帽、兵器、盾。
  const CULTURES = ['han', 'nanman', 'wa', 'yi', 'korea', 'steppe', 'seasia', 'tarim', 'kushan', 'persia', 'arab', 'roman', 'celt', 'german', 'sarmatian'];
  // 衣甲类型 → 各层（未写的层取缺省）
  const OUTFIT = {
    lamellar: { torso: 'lamLace', skirt: 'lamellar', skirtLen: 0.58, sleeves: 'cloth', pauldron: 'lamellar', bracer: 'metal', legs: 'trousers', greave: 'none', boots: 'boot', tabard: true, collar: 'scarf', belt: true, mirror: true },
    plate: { torso: 'plate', skirt: 'lamellar', skirtLen: 0.6, sleeves: 'cloth', pauldron: 'plate', bracer: 'metal', legs: 'trousers', greave: 'metal', boots: 'boot', tabard: true, collar: 'scarf', belt: true, mirror: true },
    robearmor: { torso: 'cloth', chest: 'plate', skirt: 'robe', skirtLen: 0.86, sleeves: 'cloth', pauldron: 'plate', bracer: 'metal', legs: 'trousers', boots: 'boot', tabard: true, collar: 'scarf', belt: true },
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
  };
  // 冠帽类型映射到三维头饰构建器（未专门实现的归入相近类型）
  const HAT_MAP = {
    none: 'none', helmet: 'helmet', ze: 'ze', jinxian: 'jinxian', futou: 'futou', turban: 'turban', headband: 'headband', lunjin: 'lunjin', wuguan: 'ze',
    crown: 'crown', pheasant: 'pheasant', daoist: 'crown', feathers: 'feathers', chief: 'chief', wahelm: 'wahelm', shaman: 'shaman', yiband: 'headband',
    jeolpung: 'jeolpung', birdfeather: 'jeolpung', goldcrown: 'goldcrown', felt: 'felt', fur: 'furhat', furcap: 'furhat', tallcrown: 'tallcrown',
    flowerwrap: 'turban', pointcap: 'pointcap', tallhat: 'tallhat', diadem: 'diadem', tiara: 'tiara', phrygian: 'phrygian', keffiyeh: 'keffiyeh',
    arabturban: 'turban', hatra: 'tallhat', laurel: 'laurel', galea: 'galea', celthelm: 'celthelm', spangen: 'spangen', conical: 'conical',
  };
  // 名将：三维专属设定（兵器、披风、前垂、饰物）
  const SIG = {
    '关羽': { weapon: 'guandao', cape: '#24653b', tabard: '#2f7d4a', metal: '#c8a050', beardLen: 1.35 },
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
  };
  const CULT_SHIELD = { han: 'pai', nanman: 'rattan', wa: 'wa', yi: 'round', korea: 'round', steppe: 'round', seasia: 'round', tarim: 'round', kushan: 'round', persia: 'round', arab: 'round', roman: 'scutum', celt: 'oval', german: 'round', sarmatian: 'round' };

  // 简化外貌（没有头像模块时）
  function fallbackSpec(gen) {
    const name = gen.name || '无名', r = Rng(hashStr(name));
    const war = gen.war == null ? 60 : gen.war, intel = gen.intel == null ? 50 : gen.intel;
    const cult = gen.culture || 'han';
    const role = war >= 70 ? 'warrior' : intel >= 75 ? 'strategist' : 'general';
    const skins = { han: '#e2b48c', nanman: '#a8714c', seasia: '#a06c40', roman: '#e6c09e', celt: '#f0d0b8', german: '#ecccb2', arab: '#c48e5e', persia: '#ca9a72' };
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
    if (sig.sleeves) L.sleeves = sig.sleeves;
    if (sig.pauldron) L.pauldron = sig.pauldron;
    // 披风 / 前垂颜色
    const capeSpec = sig.cape || of.cape || (L.cape ? 'faction' : null);
    if (capeSpec) look.C.cape = capeSpec === 'faction' ? faction : capeSpec;
    if (L.tabard) look.C.tabard = sig.tabard || (of.color && ot !== 'lamellar' ? of.color : faction);
    if (sig.beardLen) look.beard.len = sig.beardLen;
    // 女性与文官：去掉多余的甲
    if (look.sex === 'f' && (ot === 'robe')) { L.legs = 'trousers'; }
    // 兵器缺省
    if (!look.weapon) {
      const cw = CULT_WEAPON[culture] || CULT_WEAPON.han;
      const r = Rng(hashStr(name + 'w'));
      if ((sp.acc || []).includes('fan')) look.weapon = 'fan';
      else look.weapon = r.pick(war >= 80 ? cw.hi : war >= 60 ? cw.mid : cw.lo);
    }
    // 弓：主兵器为刀剑，背弓
    if (look.weapon === 'bow') { look.weapon = culture === 'han' ? 'dao' : (CULT_WEAPON[culture] || CULT_WEAPON.han).lo[0]; look.backBow = true; look.quiver = true; }
    if (opts.shield) look.shield = opts.shield === true ? (CULT_SHIELD[culture] || 'round') : opts.shield;
    return look;
  }

  // --------------------------------------------------------- 士兵外观 --
  // 各文化士兵：躯干 / 腰裙 / 冠帽 / 腿 / 盾；按兵种（枪 spear、刀盾 sword、弓 archer、骑 cavalry）微调
  const SOLDIER = {
    han: { outfit: 'lamellar', hat: { type: 'helmet', variant: 'round' }, archer: { outfit: 'jacket', hat: { type: 'ze' } }, skin: '#e2b48c', hair: '#1c1816', metal: '#5c626e' },
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
  };
  const SOLDIER_WEAPON = {
    spear: { han: 'spear', nanman: 'spear', wa: 'spear', yi: 'spear', korea: 'spear', steppe: 'spear', seasia: 'spear', tarim: 'spear', kushan: 'lance', persia: 'lance', arab: 'spear', roman: 'pilum', celt: 'spear', german: 'spear', sarmatian: 'lance' },
    sword: { han: 'dao', nanman: 'dao', wa: 'tachi', yi: 'dao', korea: 'dao', steppe: 'dao', seasia: 'dao', tarim: 'jian', kushan: 'spatha', persia: 'spatha', arab: 'spatha', roman: 'gladius', celt: 'spatha', german: 'handaxe', sarmatian: 'spatha' },
    cavalry: { han: 'spear', nanman: 'spear', wa: 'spear', yi: 'spear', korea: 'spear', steppe: 'spear', seasia: 'spear', tarim: 'lance', kushan: 'lance', persia: 'lance', arab: 'spear', roman: 'spear', celt: 'spear', german: 'spear', sarmatian: 'lance' },
  };
  function lookOfSoldier(opts) {
    const type = opts.type || 'spear', culture = CULTURES.includes(opts.culture) ? opts.culture : 'han';
    const S = SOLDIER[culture], T = S[type] || {};
    const factionHex = opts.color || '#8a2a24';
    const faction = TEAM;
    const seed = (opts.seed | 0) % 3;
    const r = Rng(hashStr(culture + type) + seed * 7919);
    const ot = T.outfit || S.outfit;
    const L = Object.assign({}, OUTFIT[ot]);
    // 士兵简化：不用胫甲 / 护心镜 / 飘带
    L.greave = 'none'; L.mirror = false;
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
      weapon: type === 'archer' ? 'bow' : SOLDIER_WEAPON[type === 'cavalry' ? 'cavalry' : type][culture],
      shield: type === 'sword' ? CULT_SHIELD[culture] : (culture === 'roman' && type === 'spear' ? 'scutum' : null),
      quiver: type === 'archer', lod: opts.lod || 'low',
    };
    if (L.cape && culture === 'roman') look.C.cape = '#a02a24';
    else if (L.cape) look.C.cape = shade(faction, -0.2);
    if (culture === 'celt' || culture === 'german' || culture === 'nanman' || culture === 'seasia' || culture === 'yi') look.beard.style = seed === 0 ? 'none' : 'full';
    if (culture === 'roman') look.beard.style = seed === 2 ? 'short' : 'none';
    return look;
  }

  // ======================================================== 身体与衣甲 ==
  const LODP = {
    high: { torN: 22, torK: 20, limbN: 16, armK: 14, legK: 16, head: [24, 32], pn: 6, pv: 9, hand: [7, 10], foot: 12, ring: 20, tube: 10, detail: 2 },
    mid: { torN: 12, torK: 8, limbN: 8, armK: 6, legK: 6, head: [12, 14], pn: 3, pv: 4, hand: [4, 6], foot: 6, ring: 14, tube: 6, detail: 1 },
    low: { torN: 10, torK: 6, limbN: 8, armK: 4, legK: 4, head: [6, 10], pn: 2, pv: 2, hand: [3, 4], foot: 4, ring: 12, tube: 4, detail: 0 },
  };
  // 预算（DESIGN-V3 §1）与超预算时的逐级压缩：先减纵向分段 / 甲片网格，再减环向边数
  const TRI_BUDGET = { high: 15000, mid: 4000, low: 900 };
  const SQUEEZE = {
    high: [{ armK: 12, legK: 14, pv: 8 }, { armK: 11, legK: 12, pn: 5, pv: 7, limbN: 14 }, { armK: 10, legK: 11, pn: 5, pv: 7, limbN: 12, torN: 20 }],
    mid: [{ armK: 5, legK: 5, pn: 2, pv: 3, torK: 7, ring: 12, hand: [3, 5] }, { armK: 4, legK: 4, pn: 2, pv: 3, torK: 7, ring: 12, hand: [3, 5], limbN: 7, torN: 11, head: [10, 12] },
      { armK: 4, legK: 4, pn: 2, pv: 3, torK: 6, ring: 10, hand: [3, 5], limbN: 6, torN: 10, head: [9, 12], tube: 5 }],
    low: [{ legK: 3, head: [6, 9], ring: 10 }, { legK: 3, head: [6, 8], ring: 10, limbN: 7, torN: 9 }, { legK: 3, head: [6, 8], ring: 8, limbN: 6, torN: 8, foot: 4 }],
  };
  // 躯干截面（y, 半宽, 半厚, 中心 z, 方度）
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
  // 躯干表面点：θ = 0 正前，off 为沿法线的外扩（米），mus 为肌肉起伏强度
  function torsoPt(R, y, th, off, mus, fem) {
    const r = ringAt(R, y), sn = Math.sin(th), cs = Math.cos(th);
    const ex = superE(sn, r.sq), ez = superE(cs, r.sq);
    let nx = sn / r.rx, nz = cs / r.rz;
    const nl = Math.sqrt(nx * nx + nz * nz) || 1; nx /= nl; nz /= nl;
    let d = off || 0;
    if (mus) {
      const yk = y;
      const a = Math.abs(th);
      d += mus * (0.011 * gauss(a, 0.48, 0.32) * gauss(yk, 1.335, 0.05)                 // 胸肌
        - 0.004 * gauss(a, 0.5, 0.35) * gauss(yk, 1.27, 0.012)                          // 胸肌下缘
        + 0.004 * gauss(a, 0.13, 0.1) * (0.5 + 0.5 * Math.cos((yk - 1.08) / 0.05 * PI)) * sstep(1.0, 1.06, yk) * (1 - sstep(1.2, 1.26, yk))   // 腹肌
        - 0.005 * gauss(a, 0, 0.05) * sstep(1.0, 1.05, yk) * (1 - sstep(1.24, 1.28, yk))  // 腹白线
        + 0.006 * gauss(a, 2.55, 0.35) * gauss(yk, 1.37, 0.06)                          // 肩胛
        - 0.005 * gauss(a, PI, 0.12) * sstep(1.0, 1.1, yk)                              // 脊沟
        + 0.006 * gauss(a, 1.25, 0.3) * gauss(yk, 1.2, 0.07));                          // 腹外斜
    }
    if (fem) d += 0.032 * gauss(Math.abs(th), 0.46, 0.3) * gauss(y, 1.33, 0.055);
    return [ex * r.rx + nx * d, y, r.zc + ez * r.rz + nz * d];
  }
  function torsoNormal(R, y, th) { const r = ringAt(R, y); let nx = Math.sin(th) / r.rx, nz = Math.cos(th) / r.rz; const l = Math.hypot(nx, nz) || 1; return [nx / l, 0, nz / l]; }

  // 构建上下文
  function makeCtx(look) {
    const lod = look._lodP || LODP[look.lod] || LODP.high;
    const pr = makeProps(look);
    const W = weightsFor(pr);
    const R = torsoProfile(pr);
    return { look, lod, pr, W, R, gb: new GB(), C: look.C, L: look.L, hi: lod.detail };
  }
  // 链式权重（按参数 t）：list = [[bone, t0], [bone, t1], ...]
  function wT(t, list) {
    if (t <= list[0][1]) return [[list[0][0], 1]];
    for (let i = 0; i < list.length - 1; i++) {
      const a = list[i], b = list[i + 1];
      if (t <= b[1]) { const k = smooth((t - a[1]) / (b[1] - a[1])); return [[a[0], 1 - k], [b[0], k]]; }
    }
    return [[list[list.length - 1][0], 1]];
  }
  const REG_OF = { lamLace: 'lamLace', lamellar: 'lamellar', plate: 'lamellar', scale: 'scale', segm: 'cloth', muscle: 'metal', tanko: 'cloth', rattan: 'rattan', cloth: 'cloth', robe: 'brocade', bare: 'skin', fur: 'fur', mail: 'mail' };

  // ----------------------------------------------------------- 躯干 --
  function buildTorso(X) {
    const { gb, lod, pr, R, C, L, W } = X;
    const t = L.torso || 'cloth';
    const reg = REG_OF[t] || 'cloth';
    const bare = t === 'bare', mus = (t === 'bare' ? 1 : t === 'muscle' ? 1.4 : 0) * (1 + clamp((pr.build - 1) * 4, 0, 0.9)) * (1 - pr.fat * 0.5);
    const cc = t === 'bare' ? C.skin : t === 'lamLace' ? C.cloth : (t === 'lamellar' || t === 'plate' || t === 'scale' || t === 'muscle' || t === 'mail') ? C.metal
      : t === 'rattan' ? '#c8a060' : t === 'fur' ? '#8a6a4a' : (t === 'segm' || t === 'tanko') ? (X.look.culture === 'roman' ? '#a02a24' : C.cloth) : C.cloth;
    const y0 = R[0].y, y1 = R[R.length - 1].y + 0.01 * pr.k;
    const n = lod.torK, secs = [];
    for (let k = 0; k <= n; k++) {
      const y = lerp(y0, y1, Math.pow(k / n, 0.95));
      const r = ringAt(R, y);
      secs.push({ c: [0, y, r.zc], X: [1, 0, 0], Z: [0, 0, 1], rx: r.rx, rz: r.rz, sq: r.sq, t: k / n });
    }
    loft(gb, secs, { n: lod.torN, reg: (t === 'segm' || t === 'tanko') && X.hi === 0 ? 'rivet' : reg, col: col((t === 'segm' || t === 'tanko') && X.hi === 0 ? C.metal : cc), wf: W.torso, ptf: (th, s) => torsoPt(R, s.c[1], th, 0, mus, pr.fem), capStart: X.hi >= 1 });
    // 环片甲 / 短甲：一圈圈叠压的金属带
    if ((t === 'segm' || t === 'tanko') && X.hi >= 1) {
      const bands = t === 'segm' ? [[1.04, 1.1], [1.1, 1.16], [1.16, 1.22], [1.22, 1.28], [1.29, 1.36], [1.37, 1.45]] : [[1.0, 1.08], [1.09, 1.17], [1.18, 1.26], [1.27, 1.35], [1.36, 1.44]];
      for (const [a, b] of bands) bandRing(X, a * pr.k, b * pr.k, 0.012, 0.004, 'rivet', C.metal, mus * 0, W.torso);
      if (t === 'segm') {
        // 胸前搭扣板与肩带
        for (const s of [1, -1]) shellPanel(X, { th0: s * 0.05, th1: s * 0.55, y0: 1.36, y1: 1.44, off: 0.02, reg: 'rivet', col: C.metal, th: 0.005 });
      }
    }
    // 明光铠：双圆护胸 + 背甲
    if (t === 'plate' || L.chest === 'plate') chestPlates(X);
    if (t === 'muscle') { bandRing(X, 1.0 * pr.k, 1.03 * pr.k, 0.016, 0.006, 'goldEngr', C.trim, 0, W.torso); bandRing(X, 1.47 * pr.k, 1.5 * pr.k, 0.012, 0.004, 'goldEngr', C.trim, 0, W.torso); }
    // 护心镜（札甲）
    if (L.mirror && t !== 'plate' && X.hi >= 1) mirrorDisc(X, 0, 1.33, 0.058, 0.026);
    // 交领（袍服）
    if (L.collar === 'cross' || t === 'robe') crossCollar(X);
    // 胸前斜挎的虎皮 / 豹皮 / 绶带 / 托加
    if (L.pelt) sashBand(X, L.pelt === 'leopard' ? 'leopard' : 'tiger', '#ffffff', 0.15, 0.016);
    if (L.sash) sashBand(X, 'hem', C.cloth2, 0.07, 0.01);
    if (L.toga) sashBand(X, 'hem', '#ece6d8', 0.3, 0.02);
    // 女性赤膊类衣装：加抹胸
    if (pr.fem && (t === 'bare' || t === 'rattan')) bandRing(X, 1.25 * pr.k, 1.39 * pr.k, 0.01, 0.002, L.pelt ? (L.pelt === 'leopard' ? 'leopard' : 'tiger') : 'cloth', L.pelt ? '#ffffff' : (C.cloth2 || C.cloth), 0, W.torso);
    if (L.vest) for (const s of [1, -1]) shellPanel(X, { th0: s * 0.35, th1: s * 2.9, y0: 1.06, y1: 1.47, off: 0.012, reg: 'cloth', col: C.cloth, th: 0.006 });
  }
  // 贴在躯干表面的甲片（双面布片）
  function shellPanel(X, o) {
    const { gb, lod, pr, R, W } = X;
    const k = pr.k;
    return panel(gb, (s, t) => {
      const th = lerp(o.th0, o.th1, s), y = lerp(o.y0, o.y1, t) * k;
      return torsoPt(R, y, th, (o.off || 0.01) + (o.bulge ? o.bulge * Math.sin(PI * s) * Math.sin(PI * t) : 0), o.mus || 0, pr.fem);
    }, { nu: o.nu || Math.max(2, lod.pn), nv: o.nv || Math.max(2, Math.round(lod.pv * 0.6)), th: o.th || 0.006, reg: o.reg, col: col(o.col), colB: shade(o.col, -0.4), w: p => W.torso(p), mirrorS: o.mirrorS, uv: o.uv });
  }
  // 一圈带子（腰带、甲带、包边）：y0..y1，外扩 off，下缘多外扩 flare
  function bandRing(X, y0, y1, off, flare, reg, colr, mus, wf) {
    const { gb, lod, R, pr } = X;
    const secs = [];
    const low = X.hi === 0;
    const yy = low ? [y0, y1] : [y0, y0 + (y1 - y0) * 0.15, y1 - (y1 - y0) * 0.15, y1];
    const oo = low ? [off + flare * 0.6, off] : [off * 0.55 + flare, off + flare * 0.6, off, off * 0.55];
    for (let i = 0; i < yy.length; i++) secs.push({ c: [0, yy[i], 0], X: [1, 0, 0], Z: [0, 0, 1], rx: 1, rz: 1, t: i / (yy.length - 1), off2: oo[i] });
    loft(gb, secs, { n: lod.torN, reg, col: col(colr), wf: wf || X.W.torso, ptf: (th, s) => torsoPt(R, s.c[1], th, s.off2, 0, pr.fem) });
  }
  function chestPlates(X) {
    const { C, hi } = X;
    for (const s of [1, -1]) {
      shellPanel(X, { th0: s * 0.06, th1: s * 1.2, y0: 1.2, y1: 1.43, off: 0.016, bulge: 0.008, reg: 'metal', col: C.metal, th: 0.008 });
      if (hi >= 1) mirrorDisc(X, s * 0.55, 1.315, 0.05, 0.03);
    }
    // 背甲
    shellPanel(X, { th0: 1.9, th1: TAU - 1.9, y0: 1.18, y1: 1.44, off: 0.014, reg: 'metal', col: C.metal, th: 0.008 });
    // 胸前横带（束甲绊）
    bandRing(X, 1.185 * X.pr.k, 1.215 * X.pr.k, 0.03, 0.002, 'leather', shade(C.cloth2 || C.cloth, -0.1), 0, X.W.torso);
  }
  // 圆镜：在躯干表面 (θ, y) 处的扁圆盘
  function mirrorDisc(X, th, y, r, off) {
    const { gb, lod, R, pr, C } = X;
    const yy = y * pr.k;
    const c = torsoPt(R, yy, th, off, 0, pr.fem), n = torsoNormal(R, yy, th);
    const AX = axesFromY(n);
    const prof = [[r, -0.008, null, 0.0], [r * 1.02, 0.0, null, 0.03], [r * 0.96, 0.006, null, 0.1], [r * 0.6, 0.009, null, 0.5], [0, 0.011, null, 1]];
    const start = gb.nv;
    lathe(gb, prof, { c, axes: AX, n: lod.ring, reg: 'mirror', col: col(C.trim === GOLD ? '#e8e0c8' : C.metal), w: X.W.torso(c) });
    fixDiscUV(gb, REG('mirror'), c, AX, r, start);
  }
  // 由一个方向（作为 Y）构造正交轴，满足 Z × X = Y
  function axesFromY(Y) {
    Y = v3norm(Y);
    let ref = Math.abs(Y[1]) < 0.9 ? [0, 1, 0] : [0, 0, 1];
    const Xa = v3norm(v3cross(ref, Y)), Za = v3cross(Xa, Y);
    return { X: Xa, Y, Z: Za };
  }
  // 把最近构建的圆盘顶点改为平面投影 UV
  function fixDiscUV(gb, reg, c, AX, r, from) {
    const start = from != null ? from : gb._lastLatheStart;
    if (start == null) return;
    for (let i = start; i < gb.nv; i++) {
      const p = [gb.P[i * 3] - c[0], gb.P[i * 3 + 1] - c[1], gb.P[i * 3 + 2] - c[2]];
      const u = 0.5 + 0.5 * v3dot(p, AX.X) / (r * 1.04), v = 0.5 + 0.5 * v3dot(p, AX.Z) / (r * 1.04);
      const q = ruv(reg, u, v);
      gb.UV[i * 2] = q[0]; gb.UV[i * 2 + 1] = q[1];
    }
  }
  function crossCollar(X) {
    const { C } = X;
    // 左襟压右襟：从左肩斜到右腰
    const path = [[0.55, 1.5], [0.25, 1.4], [-0.15, 1.27], [-0.55, 1.13]];
    bandPath(X, path, 0.045, 0.014, 'hem', C.cloth2 || '#e8e0d0');
    bandPath(X, [[-0.55, 1.5], [-0.3, 1.42], [-0.08, 1.36]], 0.04, 0.009, 'hem', C.cloth2 || '#e8e0d0');
  }
  // 沿躯干表面路径（[θ, y] 点列）的一条带子
  function bandPath(X, path, width, off, reg, colr) {
    const { gb, lod, R, pr, W } = X;
    const k = pr.k;
    const seg = Math.max(3, (path.length - 1) * (lod.detail + 1) * 2);
    const at = s => { const f = s * (path.length - 1), i = Math.min(path.length - 2, Math.floor(f)), t = f - i; return [lerp(path[i][0], path[i + 1][0], t), lerp(path[i][1], path[i + 1][1], t) * k]; };
    panel(gb, (s, t) => {
      const a = at(t), b = at(Math.min(1, t + 0.02)), a2 = at(Math.max(0, t - 0.02));
      // 横向：在 (θ, y) 平面中垂直于路径方向
      const dth = b[0] - a2[0], dy = b[1] - a2[1];
      const rr = ringAt(R, a[1]).rx;
      const l = Math.hypot(dth * rr, dy) || 1;
      const nth = -dy / l / rr * width, ny = dth * rr / l * width;
      const th = a[0] + (s - 0.5) * nth * 2, y = a[1] + (s - 0.5) * ny * 2;
      return torsoPt(R, y, th, off, 0, pr.fem);
    }, { nu: 1, nv: seg, th: 0.005, reg, col: col(colr), w: p => W.torso(p), uv: (s, t) => [t, s] });
  }
  function sashBand(X, reg, colr, width, off) {
    // 从右腰前 → 左肩 → 背后右腰（闭合一圈的斜带）
    const path = [[-0.75, 1.05], [-0.1, 1.24], [0.45, 1.42], [1.1, 1.5], [1.7, 1.49], [2.4, 1.38], [2.95, 1.2], [3.6, 1.05]];
    bandPath(X, path, width, off, reg, colr);
  }

  // ------------------------------------------------------------- 颈 --
  function buildNeck(X) {
    const { gb, lod, pr, W, C, look } = X;
    const k = pr.k;
    // 颈：上细（比下颌窄，不像柱子）、下接斜方肌；颈前略后移，下颌在前方悬出
    const r = 0.056 * (look.spec && look.spec.neck ? clamp(0.82 + 0.22 * look.spec.neck, 0.92, 1.25) : 1) * (pr.fem ? 0.82 : 1) * (1 + (pr.sw - 1) * 0.35) * k;
    const secs = [];
    const ys = X.hi === 0 ? [1.5, 1.63] : [1.5, 1.545, 1.59, 1.63];
    ys.forEach((y, i) => secs.push({ c: [0, y * k, (-0.02 + i * 0.004) * k], X: [1, 0, 0], Z: [0, 0, 1], rx: r * (i === 0 ? 1.3 : i === 1 ? 1.06 : 1), rz: r * (i === 0 ? 1.12 : 0.96), t: i / (ys.length - 1) }));
    loft(gb, secs, { n: Math.max(6, lod.limbN), reg: 'skin', col: col(C.skin), wf: W.neck });
    const cl = X.L.collar;
    if ((cl === 'scarf' || cl === 'fur' || cl === 'gold') && X.hi >= 1) {
      // 领巾 / 毛领 / 金项圈：贴着躯干颈根表面外扩（与躯干同一曲面 → 不再穿插出锯齿边），上缘一道圆边围住颈根
      const reg = cl === 'fur' ? 'fur' : cl === 'gold' ? 'goldEngr' : 'cloth';
      const cc = cl === 'fur' ? '#8a6a48' : cl === 'gold' ? GOLD : (X.look.kind === 'general' ? (C.tabard || C.faction) : C.faction);
      const th = cl === 'fur' ? 0.022 : 0.012;
      bandRing(X, (cl === 'gold' ? 1.44 : 1.462) * k, 1.545 * k, th, cl === 'gold' ? 0.01 : 0.006, reg, cc, 0, W.neck);
      const top = ringAt(X.R, 1.545 * k);
      tube(gb, ringPts([0, 1.545 * k, top.zc], [1, 0, 0], [0, 0, 1], top.rx + th, top.rz + th, Math.max(8, lod.limbN)), { r: (cl === 'fur' ? 0.014 : 0.008) * k, n: X.hi >= 2 ? 6 : 4, reg, col: col(cc), w: W.neck([0, 1.545 * k, 0]), closed: true });
    }
  }

  // ------------------------------------------------------------- 臂 --
  function armPt(pr, S, s) {
    const sg = S === 'L' ? 1 : -1, J = pr.J;
    const d = [pr.armDirL[0] * sg, pr.armDirL[1], 0], L = pr.upL + pr.foL;
    return v3madd(J['upperArm' + S], d, s * L);
  }
  const ARM_R = [[-0.12, 0.0, 0.0], [-0.08, 0.05, 0.052], [-0.02, 0.064, 0.066], [0.08, 0.062, 0.064], [0.22, 0.055, 0.056], [0.35, 0.05, 0.054], [0.48, 0.043, 0.045],
    [0.53, 0.04, 0.042], [0.6, 0.044, 0.045], [0.75, 0.039, 0.036], [0.9, 0.032, 0.03], [1.0, 0.029, 0.026]];
  function armR(s) {
    // 肩头：半球形收口（旧版为 2cm 的平锥，手臂下垂时露出平顶方角）
    if (s < -0.02) { const q = Math.sqrt(Math.max(0, 1 - Math.pow((s + 0.02) / 0.1, 2))); return [0.064 * q, 0.066 * q]; }
    for (let i = 0; i < ARM_R.length - 1; i++) {
      const a = ARM_R[i], b = ARM_R[i + 1];
      if (s <= b[0]) { const t = smooth((s - a[0]) / (b[0] - a[0])); return [lerp(a[1], b[1], t), lerp(a[2], b[2], t)]; }
    }
    return [ARM_R[ARM_R.length - 1][1], ARM_R[ARM_R.length - 1][2]];
  }
  function buildArm(X, S) {
    const { gb, lod, pr, W, C, L } = X;
    const sg = S === 'L' ? 1 : -1, k = pr.k * pr.limb;
    const d = [pr.armDirL[0] * sg, pr.armDirL[1], 0], Z = [0, 0, 1], Xa = v3cross(d, Z);
    const sl = L.sleeves || 'cloth';
    const segs = sl === 'bare' || (X.hi === 0 && sl === 'short') ? [['skin', -0.12, 1, 1]] : sl === 'short' ? [['cloth', -0.12, 0.3, 1.28], ['skin', 0.3, 1, 1]] :
      sl === 'mail' ? [['mail', -0.12, 0.62, 1.12], ['skin', 0.62, 1, 1]] : sl === 'wide' ? [['cloth', -0.12, 1, 1.35]] : [['cloth', -0.12, 1, 1.22]];
    const musc = sl === 'bare' || sl === 'short' ? 1 : 0;
    for (const [reg, s0, s1, sc] of segs) {
      const n = X.hi === 0 ? Math.max(1, Math.round(3 * (s1 - s0))) : Math.max(2, Math.round(lod.armK * (s1 - s0)));
      // 截面：肩头半球处加密，其余均匀
      const cap = s0 <= -0.11 ? (X.hi >= 2 ? [-0.12, -0.112, -0.098, -0.078, -0.052, -0.02] : X.hi === 1 ? [-0.12, -0.1, -0.055, -0.02] : [-0.12, -0.06, -0.02]) : null;
      const ss = [];
      if (cap) { for (const c of cap) ss.push(c); const m = Math.max(1, Math.round(n * (s1 + 0.02) / (s1 - s0)) - (X.hi === 0 ? 1 : 0)); for (let i = 1; i <= m; i++) ss.push(lerp(-0.02, s1, i / m)); }
      else for (let i = 0; i <= n; i++) ss.push(lerp(s0, s1, i / n));
      const secs = [];
      for (let i = 0; i < ss.length; i++) {
        const s = ss[i];
        let [rx, rz] = armR(s);
        let c = armPt(pr, S, s);
        let g = sc;
        if (sl === 'wide' && reg === 'cloth') {
          // 广袖：自肘起外张、下垂
          const f = sstep(0.35, 1.0, s);
          g = sc + f * 1.6;
          c = v3add(c, [0, -f * 0.05 * pr.k, f * 0.01]);
        }
        if (reg === 'cloth' && i === ss.length - 1 && s1 < 1) g *= 1.1;
        const pole = s <= -0.115;
        secs.push({ c, X: Xa, Z, rx: pole ? 0 : rx * k * g, rz: pole ? 0 : rz * k * g, t: (s - s0) / (s1 - s0), pole });
      }
      const ccol = reg === 'skin' ? C.skin : reg === 'mail' ? C.metal : (sl === 'wide' ? C.cloth : (X.look.kind === 'general' ? C.cloth : shade(C.faction, -0.15)));
      loft(gb, secs, { n: lod.limbN, reg, col: col(ccol), wf: W.arm(S),
        off: musc && reg === 'skin' ? (th, sec) => { const s = (v3dot(v3sub(sec.c, pr.J['upperArm' + S]), d)) / (pr.upL + pr.foL); const mk = 1 + clamp((pr.build - 1) * 5, 0, 1); return k * mk * (0.008 * gauss(th, 0, 0.7) * gauss(s, 0.3, 0.1) + 0.006 * gauss(Math.abs(th), PI, 0.8) * gauss(s, 0.24, 0.12) + 0.01 * gauss(s, 0.04, 0.07) + 0.005 * gauss(s, 0.66, 0.08)); } : null });
      if (reg === 'cloth' && s1 < 1) {
        // 袖口包边
        tube(gb, ringPts(armPt(pr, S, s1), Xa, Z, armR(s1)[0] * k * sc * 1.1, armR(s1)[1] * k * sc * 1.1, lod.limbN), { r: 0.006 * pr.k, n: 4, reg: 'goldEngr', col: col(C.trim), w: W.arm(S)(armPt(pr, S, s1)), closed: true });
      }
    }
    // 护臂
    const br = X.hi === 0 ? 'none' : (L.bracer || 'none');
    if (br !== 'none') {
      const reg = br === 'leather' ? 'leather' : br === 'gold' ? 'gold' : 'metal';
      const cc = br === 'leather' ? C.leather : br === 'gold' ? GOLD : C.metal;
      const s0 = br === 'gold' ? 0.86 : 0.6, s1 = 0.97;
      const n = Math.max(2, Math.round(lod.armK * 0.5));
      const secs = [];
      for (let i = 0; i <= n; i++) {
        const s = lerp(s0, s1, i / n);
        const [rx, rz] = armR(s);
        const fl = br === 'gold' ? 1.15 : 1.18 + 0.12 * sstep(0.85, 0.97, s);
        secs.push({ c: armPt(pr, S, s), X: Xa, Z, rx: rx * k * fl + 0.004, rz: rz * k * fl + 0.004, t: (s - s0) / (s1 - s0) });
      }
      loft(gb, secs, { n: lod.limbN, reg, col: col(cc), wf: W.arm(S), u0: 0, u1: 0.98 });
      if (X.hi >= 1 && br === 'metal') for (const s of [s0, s1]) tube(gb, ringPts(armPt(pr, S, s), Xa, Z, armR(s)[0] * k * 1.24 + 0.005, armR(s)[1] * k * 1.24 + 0.005, lod.limbN), { r: 0.0045 * pr.k, n: 4, reg: 'goldEngr', col: col(C.trim), w: W.arm(S)(armPt(pr, S, s)), closed: true });
    }
    buildHand(X, S);
  }
  // 截面环的点列（闭合，用于包边管）
  function ringPts(c, Xa, Z, rx, rz, n) {
    const pts = [];
    for (let i = 0; i <= n; i++) { const a = i / n * TAU; pts.push(v3add(c, v3add(v3mul(Xa, Math.sin(a) * rx), v3mul(Z, Math.cos(a) * rz)))); }
    return pts;
  }
  function buildHand(X, S) {
    const { gb, lod, pr, C, L, look } = X;
    const m = socketRest(pr, S);
    const save = gb.xf;
    gb.xf = m;
    const gloved = look.kind === 'general' && L.bracer === 'metal';
    const reg = gloved ? 'leather' : 'skin', cc = gloved ? DLEATHER : C.skin;
    const k = pr.k * (pr.fem ? 0.9 : 1) * (0.94 + 0.06 * pr.limb);
    const w = B['hand' + S];
    if (X.hi >= 2) {
      // 高精度：握拳的手 = 手背 / 掌（圆角块）+ 四指（绕握柄卷曲的圆管，指根埋入掌块）+ 拇指（压在食指上）
      // 握点坐标：握柄沿 +Y 穿过原点，+Z 沿前臂向指尖，手背在 −X（右手）/ +X（左手）一侧
      const sb = S === 'L' ? 1 : -1;            // 手背所在的 X 符号
      const R = 0.026 * k, fl = [0.0105, 0.0112, 0.0108, 0.0095];
      ellipsoid(gb, [sb * 0.017 * k, -0.002 * k, -0.034 * k], [0.017 * k, 0.043 * k, 0.046 * k], { nl: 6, n: 10, reg, col: col(cc), w, sq: 3.4 });
      for (let i = 0; i < 4; i++) {
        const y = (0.03 - i * 0.02) * k, rf = fl[i] * k, pts = [];
        const a0 = -0.95, a1 = 2.45 - i * 0.05;     // 自指根（手背前方）经 +Z 绕到掌心一侧
        for (let j = 0; j <= 5; j++) { const a = lerp(a0, a1, j / 5), rr = R + rf * 0.25 * (1 - j / 5); pts.push([-sb * Math.sin(a) * rr, y, Math.cos(a) * rr - 0.002 * k]); }
        pts[0] = [sb * 0.012 * k, y, -0.006 * k];
        tube(gb, pts, { r: q => rf * (1 - 0.18 * q), n: 6, reg, col: col(cc), w, up: [0, 1, 0], capEnd: true });
      }
      // 拳心（空手时填住握柄留下的孔）
      ellipsoid(gb, [0, -0.004 * k, 0], [0.016 * k, 0.036 * k, 0.016 * k], { nl: 4, n: 6, reg, col: col(cc), w });
      // 拇指：自掌根（掌心侧、拇指侧）绕过握柄压在食指第二节上
      const tp = [[-sb * 0.006 * k, 0.034 * k, -0.06 * k], [-sb * 0.024 * k, 0.04 * k, -0.034 * k], [-sb * 0.03 * k, 0.046 * k, -0.006 * k], [-sb * 0.018 * k, 0.047 * k, 0.016 * k]];
      tube(gb, tp, { r: q => (0.012 - 0.002 * q) * k, n: 6, reg, col: col(cc), w, up: [0, 1, 0], capEnd: true });
    } else {
      ellipsoid(gb, [0, 0, -0.014 * k], [0.03 * k, 0.047 * k, 0.052 * k], { nl: lod.hand[0], n: lod.hand[1], reg, col: col(cc), w, sq: X.hi >= 1 ? 3.2 : 2 });
      // 拇指压在握柄上
      if (X.hi >= 1) tube(gb, [[0.018 * k, 0.03 * k, -0.045 * k], [0.03 * k, 0.04 * k, -0.012 * k], [0.022 * k, 0.046 * k, 0.012 * k]], { r: s => (0.013 - 0.003 * s) * k, n: 6, reg, col: col(cc), w });
    }
    gb.xf = save;
  }

  // ------------------------------------------------------------- 腿 --
  const LEG_R = [[-0.05, 0.09, 0.09], [0.0, 0.098, 0.098], [0.12, 0.092, 0.094], [0.3, 0.08, 0.082], [0.45, 0.064, 0.066], [0.5, 0.058, 0.062], [0.58, 0.06, 0.064],
    [0.68, 0.062, 0.068], [0.82, 0.05, 0.05], [1.0, 0.04, 0.042]];
  function legR(s) {
    for (let i = 0; i < LEG_R.length - 1; i++) { const a = LEG_R[i], b = LEG_R[i + 1]; if (s <= b[0]) { const t = smooth((s - a[0]) / (b[0] - a[0])); return [lerp(a[1], b[1], t), lerp(a[2], b[2], t)]; } }
    return [LEG_R[LEG_R.length - 1][1], LEG_R[LEG_R.length - 1][2]];
  }
  function legPt(pr, S, s) {
    const J = pr.J, h = J['thigh' + S], kn = J['shin' + S], an = J['foot' + S];
    if (s <= 0.5) return v3lerp(h, kn, s / 0.5);
    return v3lerp(kn, an, (s - 0.5) / 0.5);
  }
  function legFrame(pr, S, s) {
    const J = pr.J;
    const d = s <= 0.5 ? v3norm(v3sub(J['shin' + S], J['thigh' + S])) : v3norm(v3sub(J['foot' + S], J['shin' + S]));
    const Z = v3norm(v3sub([0, 0, 1], v3mul(d, d[2])));
    return { X: v3cross(d, Z), Z, d };
  }
  function buildLeg(X, S) {
    const { gb, lod, pr, W, C, L, look } = X;
    const k = pr.k * pr.limb;
    const lg = L.legs || 'trousers';
    const bootTop = L.boots === 'boot' ? 0.74 : 2;
    const segs = [];
    if (lg === 'bare') segs.push(['skin', -0.05, 1, 1, C.skin]);
    else if (lg === 'hakama') { segs.push(['cloth', -0.05, 0.56, 1.45, C.cloth2]); segs.push(['skin', 0.56, 1, 1, C.skin]); }
    else segs.push([L.legsReg || 'cloth', -0.05, Math.min(1, bootTop + 0.04), 1.28, L.legsReg === 'plaid' ? C.cloth : (look.kind === 'general' ? (C.trousers || shade(C.cloth, -0.55)) : shade(C.cloth2, 0.05))]);
    for (const [reg, s0, s1, sc, cc] of segs) {
      const n = Math.max(2, Math.round(lod.legK * (s1 - s0)));
      const secs = [];
      for (let i = 0; i <= n; i++) {
        const s = lerp(s0, s1, i / n);
        const F = legFrame(pr, S, s);
        let [rx, rz] = legR(s);
        let g = sc;
        // 裤：大腿根贴身（不穿出腰裙），膝下宽松后收进靴
        if (reg !== 'skin') g = lerp(1.02, sc, sstep(0.3, 0.6, s)) * (1 - 0.15 * sstep(0.55, 1, s)) + (lg === 'hakama' ? 0.15 * sstep(0.3, 0.5, s) * (1 - sstep(0.5, 0.56, s)) : 0);
        secs.push({ c: legPt(pr, S, s), X: F.X, Z: F.Z, rx: rx * k * g, rz: rz * k * g, t: (s - s0) / (s1 - s0) });
      }
      loft(gb, secs, { n: lod.limbN, reg, col: col(cc), wf: W.leg(S), off: reg === 'skin' ? (th, sec, i) => { const s = lerp(s0, s1, i / n); return k * (0.008 * gauss(s, 0.66, 0.08) * gauss(Math.abs(th), PI * 0.75, 0.7) + 0.006 * gauss(s, 0.22, 0.12) * gauss(th, 0, 0.8)); } : null });
    }
    // 靴
    if (L.boots === 'boot') {
      const n = X.hi === 0 ? 1 : Math.max(2, Math.round(lod.legK * 0.3)), secs = [];
      for (let i = 0; i <= n; i++) {
        const s = lerp(bootTop, 1.0, i / n), F = legFrame(pr, S, s);
        const [rx, rz] = legR(s);
        const g = 1.18 + (i === 0 ? 0.08 : 0);
        secs.push({ c: legPt(pr, S, s), X: F.X, Z: F.Z, rx: rx * k * g + 0.004, rz: rz * k * g + 0.004, t: i / n });
      }
      loft(gb, secs, { n: lod.limbN, reg: 'leather', col: col(DLEATHER), wf: W.leg(S) });
      if (X.hi >= 1) { const F = legFrame(pr, S, bootTop); const [rx, rz] = legR(bootTop); tube(gb, ringPts(legPt(pr, S, bootTop), F.X, F.Z, rx * k * 1.27 + 0.004, rz * k * 1.27 + 0.004, lod.limbN), { r: 0.007 * pr.k, n: 4, reg: 'leather', col: col(shade(DLEATHER, 0.2)), w: W.leg(S)(legPt(pr, S, bootTop)), closed: true }); }
    }
    // 胫甲 + 护膝
    if (L.greave === 'metal') {
      const s0 = 0.54, s1 = 0.9;
      panel(gb, (u, v) => {
        const s = lerp(s0, s1, v), F = legFrame(pr, S, s), [rx, rz] = legR(s);
        const th = lerp(-1.7, 1.7, u), g = 1.32;
        return v3add(legPt(pr, S, s), v3add(v3mul(F.X, Math.sin(th) * (rx * k * g + 0.006)), v3mul(F.Z, Math.cos(th) * (rz * k * g + 0.008))));
      }, { nu: Math.max(2, lod.pn), nv: Math.max(2, lod.pv - 3), th: 0.006, reg: 'metal', col: col(C.metal), w: p => W.leg(S)(p), mirrorS: true });
      const kp = v3add(legPt(pr, S, 0.5), [0, 0.0, 0.065 * k]);
      ellipsoid(gb, kp, [0.045 * k, 0.05 * k, 0.025 * k], { nl: Math.max(3, lod.hand[0] - 2), n: lod.hand[1], reg: 'metal', col: col(C.metal), w: W.leg(S)(kp) });
    }
    // 凉鞋绑带
    if (L.boots === 'sandal' && X.hi >= 1) for (const s of [0.8, 0.88, 0.96]) { const F = legFrame(pr, S, s); const [rx, rz] = legR(s); tube(gb, ringPts(legPt(pr, S, s), F.X, F.Z, rx * k * 1.04 + 0.002, rz * k * 1.04 + 0.002, lod.limbN), { r: 0.004 * pr.k, n: 3, reg: 'leather', col: col(LEATHER), w: W.leg(S)(legPt(pr, S, s)), closed: true }); }
    buildFoot(X, S);
  }
  function buildFoot(X, S) {
    const { gb, lod, pr, C, L } = X;
    const k = pr.k, a = pr.J['foot' + S];
    const bare = L.boots === 'sandal' || L.boots === 'bare';
    const reg = bare ? 'skin' : L.boots === 'shoe' ? 'cloth' : 'leather', cc = bare ? C.skin : L.boots === 'shoe' ? '#2a2420' : DLEATHER;
    const st = X.hi === 0 ? [[-0.07, 0, 0, 0.03], [-0.03, 0.046, 0.06, 0.055], [0.1, 0.05, 0.032, 0.03], [0.17, 0, 0, 0.022]]
      : [[-0.07, 0, 0, 0.03], [-0.058, 0.036, 0.04, 0.042], [-0.02, 0.046, 0.066, 0.06], [0.04, 0.05, 0.05, 0.045], [0.1, 0.05, 0.032, 0.03], [0.15, 0.043, 0.024, 0.024], [0.175, 0, 0, 0.022]];
    const secs = st.map(([z, rx, rz, cy], i) => ({ c: [a[0], cy * k, a[2] + z * k], X: [-1, 0, 0], Z: [0, 1, 0], rx: rx * k * (bare ? 0.92 : 1.06), rz: rz * k * (bare ? 0.9 : 1.05), t: i / (st.length - 1), pole: rx === 0, sq: 2.6 }));
    const fw = p => (p[1] > 0.075 * k ? [[B['foot' + S], 0.6], [B['shin' + S], 0.4]] : [[B['foot' + S], 1]]);
    loft(gb, secs, { n: X.hi === 0 ? 6 : Math.max(6, lod.foot), reg, col: col(cc), wf: fw });
  }

  // ------------------------------------------------------------- 腰裙 --
  // 一片裙甲：θ0..θ1，从腰（yTop）到下摆（yBot），下摆外张 flare；权重：上 = 骨盆，下 = 大腿 / 前后垂骨
  function skirtPanel(X, o) {
    const { gb, lod, pr, R, W } = X;
    const k = pr.k;
    return panel(gb, (s, t) => {
      const th = lerp(o.th0, o.th1, s);
      const y = lerp(o.yTop, o.yBot, t) * k - (o.curve || 0) * Math.pow(Math.abs(Math.sin(th)), 2) * t * k;
      const top = torsoPt(R, Math.max(R[0].y + 0.02, o.yTop * k), th, o.off, 0, false);
      const fl = (o.flare || 0.06) * Math.pow(t, 1.15) * k;
      const n = torsoNormal(R, o.yTop * k, th);
      const wav = o.wave ? o.wave * Math.sin(s * PI * (o.waves || 3)) * t : 0;
      return [top[0] + n[0] * (fl + wav), y, top[2] + n[2] * (fl + wav)];
    }, { nu: o.nu || Math.max(2, lod.pn), nv: o.nv || Math.max(2, lod.pv - 2), th: o.th || 0.007, reg: o.reg, col: col(o.col), colB: shade(o.col, -0.4), w: o.w, uv: o.uv, mirrorS: o.mirrorS, edge: o.edge });
  }
  function legSideW(S, top, bot) {
    return (p, s, t) => { const T = clamp01(t); return [[B.pelvis, 1 - T * bot], [B['thigh' + S], T * bot]]; };
  }
  function buildSkirt(X) {
    const { gb, lod, pr, C, L, look } = X;
    const sk = L.skirt || 'none';
    const top = 1.04, len = L.skirtLen || 0.55, bot = top - len;
    const lowL = lod.detail === 0;
    if (sk === 'lamellar' || sk === 'rattan' || sk === 'scale') {
      const reg = sk === 'rattan' ? 'rattan' : sk === 'scale' ? 'scale' : (L.torso === 'lamLace' ? 'lamLace' : 'lamellar');
      const cc = sk === 'rattan' ? '#c8a060' : reg === 'lamLace' ? C.cloth : C.metal;
      const P4 = [[0.06, 1.62, 'L', 0.75], [-1.62, -0.06, 'R', 0.75], [1.52, 3.1, 'L', 0.55], [-3.1, -1.52, 'R', 0.55]];
      for (const [a, b, S, wk] of P4) skirtPanel(X, { th0: a, th1: b, yTop: top, yBot: bot, off: 0.016, flare: 0.07, curve: 0.04, reg, col: cc, w: legSideW(S, 0, wk), nv: Math.max(2, lod.pv - 2) });
      // 腰甲上缘的包边
      if (!lowL) bandRing(X, (top - 0.02) * pr.k, (top + 0.03) * pr.k, 0.024, 0.003, 'goldEngr', C.trim, 0, X.W.torso);
    } else if (sk === 'robe' || sk === 'coat' || sk === 'sarong') {
      const reg = sk === 'sarong' ? 'brocade' : 'hem';
      const cc = look.L.torso === 'cloth' && L.chest === 'plate' ? C.cloth : C.cloth;
      const fl = sk === 'robe' ? 0.12 : sk === 'coat' ? 0.07 : 0.03;
      const yb = sk === 'robe' ? Math.max(0.1, bot) : bot;
      const wk = sk === 'robe' ? 0.45 : 0.6;
      // 左右两片（左压右），各自随同侧大腿
      skirtPanel(X, { th0: -0.25, th1: PI + 0.25, yTop: top + 0.02, yBot: yb, off: 0.014, flare: fl, reg, col: cc, w: legSideW('L', 0, wk), uv: (s, t) => [s, 1 - t], wave: X.hi >= 1 ? 0.008 : 0, waves: 5, nu: Math.max(3, lod.pn + 2) });
      skirtPanel(X, { th0: -PI - 0.25, th1: 0.25, yTop: top + 0.02, yBot: yb, off: 0.008, flare: fl, reg, col: cc, w: legSideW('R', 0, wk), uv: (s, t) => [s, 1 - t], wave: X.hi >= 1 ? 0.008 : 0, waves: 5, nu: Math.max(3, lod.pn + 2) });
    } else if (sk === 'tunic' || sk === 'kilt' || sk === 'bands' || sk === 'pteruges') {
      const tun = sk === 'tunic' || sk === 'pteruges' || L.tunic;
      if (tun) {
        const cc = look.culture === 'roman' ? '#a02a24' : C.cloth;
        const yb = sk === 'pteruges' ? top - 0.5 : bot;
        skirtPanel(X, { th0: -0.15, th1: PI + 0.15, yTop: top, yBot: yb, off: 0.008, flare: 0.05, reg: 'hem', col: cc, w: legSideW('L', 0, 0.6), uv: (s, t) => [s, 1 - t] });
        skirtPanel(X, { th0: -PI - 0.15, th1: 0.15, yTop: top, yBot: yb, off: 0.004, flare: 0.05, reg: 'hem', col: cc, w: legSideW('R', 0, 0.6), uv: (s, t) => [s, 1 - t] });
      }
      if (sk === 'pteruges') {
        // 皮条裙：一圈窄皮条
        const n = lowL ? 6 : 14;
        for (let i = 0; i < n; i++) {
          const a = -PI + (i + 0.5) / n * TAU, w = TAU / n * 0.46;
          const S = Math.sin(a) >= 0 ? 'L' : 'R';
          skirtPanel(X, { th0: a - w, th1: a + w, yTop: top + 0.01, yBot: top - 0.3, off: 0.03, flare: 0.04, reg: 'leather', col: shade(C.leather, 0.15), w: legSideW(S, 0, 0.5), nu: 1, nv: lowL ? 1 : 3, th: 0.005 });
        }
      } else if (sk === 'kilt') {
        const reg = L.pelt === 'tiger' ? 'tiger' : L.pelt === 'leopard' ? 'leopard' : 'cloth';
        const cc = reg === 'cloth' ? C.cloth : '#ffffff';
        skirtPanel(X, { th0: -0.7, th1: 0.7, yTop: top, yBot: bot, off: 0.014, flare: 0.04, reg, col: cc, w: (p, s, t) => wT(t, [[B.pelvis, 0], [B.skirtF, 0.7]]) });
        skirtPanel(X, { th0: PI - 0.9, th1: PI + 0.9, yTop: top, yBot: bot + 0.04, off: 0.014, flare: 0.04, reg, col: cc, w: (p, s, t) => wT(t, [[B.pelvis, 0], [B.skirtB, 0.7]]) });
        for (const S of ['L', 'R']) { const sg = S === 'L' ? 1 : -1; skirtPanel(X, { th0: sg > 0 ? 0.6 : -2.4, th1: sg > 0 ? 2.4 : -0.6, yTop: top, yBot: top - 0.22, off: 0.01, flare: 0.04, reg, col: cc, w: legSideW(S, 0, 0.5) }); }
      } else if (sk === 'bands') {
        // 草摺：多层横带
        for (let j = 0; j < (X.hi === 0 ? 2 : 4); j++) {
          const yt = top - j * (X.hi === 0 ? 0.2 : 0.1);
          skirtPanel(X, { th0: -PI, th1: PI, yTop: yt, yBot: yt - 0.12, off: 0.02 + j * 0.022, flare: 0.02, reg: 'rivet', col: C.metal, w: (p, s, t) => [[B.pelvis, 1 - j * 0.12], [p[0] > 0 ? B.thighL : B.thighR, j * 0.12]], nu: Math.max(4, lod.torN), nv: 1, th: 0.005 });
        }
      }
    }
    // 前垂（势力色、金线下摆）与后垂
    if (C.tabard && L.tabard) {
      const ttop = top + 0.01, tb = 0.42;
      skirtPanel(X, { th0: -0.42, th1: 0.42, yTop: ttop, yBot: tb, off: sk === 'lamellar' || sk === 'scale' ? 0.045 : 0.03, flare: 0.08, reg: 'hem', col: C.tabard, uv: (s, t) => [Math.abs(2 * s - 1), 1 - t],
        w: (p, s, t) => wT(t, [[B.pelvis, 0], [B.skirtF, 0.35]]), nu: Math.max(2, lod.pn - 1), nv: Math.max(2, lod.pv - 1), th: 0.006 });
      if (look.kind === 'general') skirtPanel(X, { th0: PI - 0.5, th1: PI + 0.5, yTop: ttop, yBot: tb + 0.06, off: sk === 'lamellar' || sk === 'scale' ? 0.045 : 0.03, flare: 0.08, reg: 'hem', col: shade(C.tabard, -0.1), uv: (s, t) => [Math.abs(2 * s - 1), 1 - t],
        w: (p, s, t) => wT(t, [[B.pelvis, 0], [B.skirtB, 0.35]]), nu: Math.max(2, lod.pn - 1), nv: Math.max(2, lod.pv - 1), th: 0.006 });
    }
    // 腰带
    if (L.belt) buildBelt(X, top);
  }
  function buildBelt(X, top) {
    const { gb, lod, pr, R, C, L, W } = X;
    const k = pr.k, sash = L.belt === 'sash';
    const y0 = (top - 0.035) * k, y1 = (top + 0.035) * k;
    bandRing(X, y0, y1, sash ? 0.03 : 0.034, 0.002, sash ? 'cloth' : 'leather', sash ? (C.cloth2 || C.faction) : (X.look.kind === 'general' ? '#3a2a22' : C.leather), 0, W.torso);
    if (!sash && X.hi >= 1) {
      // 兽面带扣
      const c = torsoPt(R, (y0 + y1) / 2, 0, 0.05, 0, pr.fem), n = torsoNormal(R, (y0 + y1) / 2, 0);
      const AX = axesFromY(n);
      const r = 0.05 * k * (X.look.kind === 'general' ? 1.1 : 0.8);
      const start = gb.nv;
      lathe(gb, [[r, -0.012, null, 0], [r * 1.02, 0, null, 0.1], [r * 0.9, 0.009, null, 0.3], [r * 0.4, 0.016, null, 0.7], [0, 0.019, null, 1]], { c, axes: AX, n: lod.ring, reg: 'buckle', col: col(GOLD), w: W.torso(c) });
      fixDiscUV(gb, REG('buckle'), c, AX, r, start);
    } else if (sash && X.hi >= 1) {
      // 腰结与垂带
      const c = torsoPt(R, (y0 + y1) / 2, -0.35, 0.035, 0, pr.fem);
      for (const [dx, len] of [[0.0, 0.32], [0.035, 0.26]]) {
        panel(gb, (s, t) => [c[0] - 0.02 + dx + s * 0.05 + t * 0.02, c[1] - t * len * k, c[2] + 0.01 + t * 0.03], { nu: 1, nv: 3, th: 0.004, reg: 'hem', col: col(C.cloth2 || C.faction), uv: (s, t) => [s, 1 - t], w: (p, s, t) => wT(t, [[B.pelvis, 0], [B.skirtF, 0.5]]) });
      }
    }
  }

  // ------------------------------------------------------------- 护肩 --
  function buildPauldron(X, S) {
    const { gb, lod, pr, C, L } = X;
    const tp = L.pauldron || 'none';
    if (tp === 'none') return;
    const sg = S === 'L' ? 1 : -1, k = pr.k * (0.9 + 0.1 * pr.limb);
    const J = pr.J['upperArm' + S];
    const d = [pr.armDirL[0] * sg, pr.armDirL[1], 0];
    // 外上方向（垂直于手臂）
    const out = v3norm(v3sub([sg, 0.55, 0], v3mul(d, v3dot([sg, 0.55, 0], d))));
    const fwd = [0, 0, 1];
    // 护肩刚性挂在肩部辅助骨上（所有甲片同一权重 → 手臂高举时整体随动，不会散成碎片）
    const wpf = (a) => (p) => B['sh' + S];
    const lnv = X.hi === 0 ? 1 : Math.max(2, lod.pv - 4);
    const lame = (a0, a1, r0, r1, half, reg, cc, wk, thk, c0) => panel(gb, (s, t) => {
      const ph = (c0 || 0) + lerp(-half, half, s), al = lerp(a0, a1, t) * k;
      // 围绕手臂轴旋转「外上」方向：ph 正 = 向前
      const dir = v3add(v3mul(out, Math.cos(ph)), v3mul(v3norm(v3cross(d, out)), Math.sin(ph) * -sg));
      const r = lerp(r0, r1, Math.pow(t, 1.4)) * k;
      return v3add(v3madd(J, d, al), v3mul(dir, r));
    }, { nu: Math.max(3, lod.pn + 1), nv: lnv, th: thk || 0.007, reg, col: col(cc), colB: shade(cc, -0.45), w: wpf(wk), mirrorS: true });
    if (tp === 'plate' || tp === 'lion') {
      lame(-0.07, 0.05, 0.075, 0.098, 1.6, tp === 'lion' ? 'gold' : 'metal', tp === 'lion' ? GOLD : C.metal, 0.6, 0.009);
      lame(0.0, 0.12, 0.088, 0.104, 1.5, 'metal', C.metal, 0.85);
      if (X.hi >= 2) lame(0.07, 0.19, 0.092, 0.1, 1.4, 'metal', C.metal, 1);
      // 兽面（狮子吞肩）
      if (tp === 'lion' && X.hi >= 1) {
        const c = v3add(v3madd(J, d, 0.0), v3mul(out, 0.105 * k)), AX = axesFromY(out), r = 0.05 * k, start = gb.nv;
        lathe(gb, [[r, -0.01, null, 0], [r * 1.02, 0, null, 0.1], [r * 0.85, 0.012, null, 0.35], [r * 0.3, 0.022, null, 0.8], [0, 0.024, null, 1]], { c, axes: AX, n: lod.ring, reg: 'lion', col: col(GOLD), w: wpf(0.7)(c) });
        fixDiscUV(gb, REG('lion'), c, AX, r, start);
      }
    } else if (tp === 'lamellar' || tp === 'scale' || tp === 'rattan') {
      const reg = tp === 'rattan' ? 'rattan' : tp === 'scale' ? 'scale' : (L.torso === 'lamLace' ? 'lamLace' : 'lamellar');
      const cc = tp === 'rattan' ? '#c8a060' : reg === 'lamLace' ? C.cloth : C.metal;
      lame(-0.06, 0.1, 0.075, 0.105, 1.55, reg, cc, 0.7);
      if (X.hi >= 1) lame(0.06, 0.2, 0.088, 0.104, 1.45, reg, cc, 1);
    } else if (tp === 'segm' || tp === 'bands') {
      const n = X.hi >= 1 ? 4 : 2;
      for (let i = 0; i < n; i++) lame(-0.06 + i * 0.045, -0.06 + i * 0.045 + 0.06, 0.074 + i * 0.006, 0.084 + i * 0.006, 1.5, 'rivet', C.metal, 0.6 + i * 0.1, 0.005);
    } else if (tp === 'fur') {
      lame(-0.07, 0.08, 0.08, 0.11, 1.7, 'fur', '#8a6a48', 0.7, 0.02);
    } else if (tp === 'pteruges') {
      const n = X.hi >= 1 ? 5 : 3;
      for (let i = 0; i < n; i++) {
        const a = lerp(-1.2, 1.2, (i + 0.5) / n);
        lame(0.02, 0.17, 0.068, 0.078, 0.2, 'leather', shade(C.leather, 0.15), 1, 0.005, a);
      }
    }
  }

  // ------------------------------------------------------------- 披风 --
  function buildCape(X) {
    const { gb, lod, pr, C, look } = X;
    if (!C.cape) return;
    const k = pr.k, sw = pr.sw;
    const reg = look.L.capeReg || 'hem';
    const yb = look.kind === 'soldier' ? 0.62 : 0.3;
    panel(gb, (s, t) => {
      const a = lerp(-1.3, 1.3, s);
      const top = [Math.sin(a) * 0.19 * sw * k, (1.475 - 0.03 * (1 - Math.cos(a))) * k, (-Math.cos(a) * 0.13 - 0.03) * sw * k];
      const bw = 0.44 * k;
      const bot = [lerp(-bw, bw, s), yb * k, -0.3 * k];
      const p = v3lerp(top, bot, Math.pow(t, 0.9));
      p[2] += -0.07 * Math.sin(PI * t) * k - (look.L.skirt === 'lamellar' ? 0.02 : 0);
      p[2] += (X.hi >= 1 ? Math.sin(s * PI * 7) * 0.022 * t * k : 0);
      return p;
    }, { nu: X.hi >= 2 ? 12 : X.hi >= 1 ? 5 : 3, nv: X.hi >= 2 ? 12 : X.hi >= 1 ? 5 : 2, th: 0.007, reg, col: col(C.cape), colB: shade(C.cape, -0.45), uv: (s, t) => [Math.abs(2 * s - 1), 1 - t],
      w: (p, s, t) => wT(t, [[B.chest, 0.02], [B.cape1, 0.12], [B.cape2, 0.5], [B.cape3, 0.9]]) });
  }
  // 飘带：自腰后两侧垂下
  function buildRibbons(X) {
    const { gb, pr, look } = X;
    if (!look.ribbons || X.hi < 1) return;
    const k = pr.k;
    for (const sg of [1, -1]) {
      panel(gb, (s, t) => {
        const x = sg * (0.1 + 0.08 * t) * k + (s - 0.5) * 0.06 * k;
        return [x, (1.02 - t * 0.85) * k, (-0.17 - 0.2 * t - 0.03 * Math.sin(t * 6 + sg)) * k];
      }, { nu: 1, nv: X.hi >= 2 ? 10 : 4, th: 0.003, reg: 'cloth', col: col(look.ribbons), w: (p, s, t) => wT(t, [[B.pelvis, 0], [B.skirtB, 0.15], [B.cape3, 0.7]]) });
    }
  }
  // 箭壶、背弓
  function buildBackGear(X) {
    const { gb, lod, pr, look } = X;
    const k = pr.k;
    if (look.quiver) {
      const a = [0.1 * k, 1.05 * k, -0.17 * k], b = [-0.12 * k, 1.5 * k, -0.2 * k];
      tube(gb, [a, v3lerp(a, b, 0.5), b], { r: s => (0.042 - 0.006 * s) * k, n: Math.max(5, lod.tube), reg: 'leather', col: col(LEATHER), w: B.chest, capStart: true });
      if (X.hi >= 1) for (let i = 0; i < 5; i++) {
        const o = [(-0.012 + (i % 3) * 0.012) * k, 0, (-0.01 + Math.floor(i / 3) * 0.02) * k];
        const p0 = v3add(b, o), p1 = v3add(p0, v3mul(v3norm(v3sub(b, a)), 0.1 * k));
        tube(gb, [p0, p1], { r: s => 0.012 * k * (s > 0.3 ? 1 : 0.4), rz: 0.25, n: 3, reg: 'feather', col: [1, 1, 1], w: B.chest });
      }
    }
    if (look.backBow) {
      const pts = [];
      for (let i = 0; i <= 8; i++) { const t = i / 8; pts.push([lerp(0.3, -0.28, t) * k, lerp(0.95, 1.6, t) * k, (-0.2 - 0.07 * Math.sin(PI * t)) * k]); }
      tube(gb, pts, { r: s => (0.012 - 0.006 * Math.abs(s - 0.5)) * k, n: 5, reg: 'wood', col: col('#5a3418'), w: B.chest });
    }
  }

  // ======================================================== 冠帽、发、须 ==
  // 均在头部局部坐标中构建（gb.xf = 头骨绑定位置），权重为头骨（盔缨 / 长发用 plume / hair 骨）。
  function hairline(th, style, recede) {
    const a = Math.abs(th), front = (style === 'kunfa' ? 1.2 : FEAT.hairP) + (recede || 0) * 0.25;
    if (a < 0.95) return front + (style === 'kunfa' ? 0 : 0.06 * Math.cos(th * 5) * gauss(a, 0.5, 0.3));
    if (a < 1.45) return lerp(front - 0.15, 0.08, sstep(0.95, 1.45, a));
    if (a < 1.75) return lerp(0.08, 0.15, sstep(1.45, 1.75, a));
    return lerp(0.1, -0.55, sstep(1.75, 2.6, a));
  }
  function headN(p) { return v3norm(v3sub(p, HEAD_C)); }
  // 头部外壳：在头部参数面 φ ∈ [φ0(θ), π/2] 上外扩（盔体、帽、发壳）
  // o：{ phi0(θ), off(θ, s) 外扩, sy（顶部拔高）, nK, n, reg, col, w, apex（尖顶高度）, flat（压扁顶部）}
  function headShell(X, Hp, o) {
    const { gb, lod } = X;
    const nK = o.nK || Math.max(3, Math.round(lod.head[0] * 0.45)), n = o.n || Math.max(8, lod.head[1]);
    const secs = [];
    for (let k = 0; k <= nK; k++) secs.push({ c: [0, 0, 0], X: [1, 0, 0], Z: [0, 0, 1], rx: 1, rz: 1, t: k / nK, pole: k === nK, kk: k / nK });
    const ptf = (th, s) => {
      const u = s.kk;
      const p0 = o.phi0(th), ph = lerp(p0, PI / 2 - 0.001, Math.pow(u, o.pw || 0.85));
      const p = headPoint(th, ph, Hp);
      const nn = headN(p);
      const d = o.off(th, u, ph);
      const q = v3madd(p, nn, d);
      // 拔高：以头中心高度为界向上等比拉伸（顶部保持圆润）
      if (o.sy && q[1] > 0.12) q[1] = 0.12 + (q[1] - 0.12) * (1 + o.sy / 0.125);
      if (o.apex) q[1] += o.apex * Math.pow(u, 3);
      if (o.tilt) q[2] += o.tilt * Math.pow(Math.max(0, q[1] - 0.12) / 0.2, 2);
      return q;
    };
    // 顶点 pole 位置
    const top = ptf(0, { kk: 1 });
    secs[nK].c = top;
    return loft(gb, secs, { n, reg: o.reg, col: col(o.col), w: o.w != null ? o.w : B.head, ptf, mirror: o.mirror });
  }
  // 沿头部表面一圈的带子（发带、抹额、冠圈、盔沿）：phi(θ) 高度，off 外扩，r 管半径
  function headRing(X, Hp, phi, off, r, reg, cc, o) {
    const { gb, lod } = X;
    const n = X.hi === 0 ? 8 : Math.max(10, lod.head[1]), pts = [];
    o = o || {};
    const a0 = o.a0 != null ? o.a0 : -PI, a1 = o.a1 != null ? o.a1 : PI;
    for (let i = 0; i <= n; i++) { const th = lerp(a0, a1, i / n); const p = headPoint(th, phi(th), Hp); pts.push(v3madd(p, headN(p), off)); }
    return tube(gb, pts, { r, rz: o.rz || 1, n: X.hi === 0 ? 3 : (o.tn || Math.max(4, lod.tube - 2)), reg, col: col(cc), w: o.w != null ? o.w : B.head, up: [0, 1, 0] });
  }
  function headAt(Hp, th, ph, off) { const p = headPoint(th, ph, Hp); return v3madd(p, headN(p), off || 0); }

  // 盔缨（红缨）：自 p 处向上再下垂的一簇马尾
  function tassel(X, p, size, cc, w) {
    const { gb, lod } = X;
    const s = size;
    const prof = X.hi === 0 ? [[0.005, 0], [0.05 * s, 0.05 * s], [0.07 * s, -0.04 * s], [0.006, -0.01]]
      : [[0.005, 0], [0.011, 0.02], [0.03 * s, 0.05 * s], [0.055 * s, 0.045 * s], [0.07 * s, 0.015 * s], [0.07 * s, -0.04 * s], [0.05 * s, -0.07 * s], [0.02 * s, -0.04 * s], [0.006, -0.005]];
    prof.reverse();
    lathe(gb, prof.map((q, i) => [q[0], q[1], null, i / (prof.length - 1)]), { c: p, n: Math.max(6, lod.ring - 4), reg: 'tassel', col: col(cc), w: w != null ? w : [[B.plume1, 1]] });
  }
  // 羽毛 / 雉翎：沿曲线的扁带（贴图 feather），宽度 w0 → w1
  function featherStrip(X, pts, w0, w1, reg, cc, wfn, nv) {
    const { gb } = X;
    const F = frames(pts, [0, 0, 1]);
    const N = pts.length;
    const at = t => { const f = t * (N - 1), i = Math.min(N - 2, Math.floor(f)), u = f - i; return { p: v3lerp(pts[i], pts[i + 1], u), x: v3norm(v3lerp(F.X[i], F.X[i + 1], u)) }; };
    panel(gb, (s, t) => { const a = at(t); return v3madd(a.p, a.x, (s - 0.5) * lerp(w0, w1, t)); }, { nu: 1, nv: nv || Math.max(4, (N - 1) * 2), th: 0.003, reg: reg || 'feather', col: col(cc || '#ffffff'), colB: col(cc || '#ffffff'), w: wfn, uv: (s, t) => [s, t] });
  }
  function bez(p0, p1, p2, p3, n) { const out = []; for (let i = 0; i <= n; i++) { const t = i / n, u = 1 - t; out.push([0, 1, 2].map(k => u * u * u * p0[k] + 3 * u * u * t * p1[k] + 3 * u * t * t * p2[k] + t * t * t * p3[k])); } return out; }

  function buildHeadgear(X, Hp) {
    const { gb, lod, look, C } = X;
    const hat = look.hat || { type: 'none' };
    const tp = hat.type || 'none';
    const hc = hat.color ? col(hat.color) : null;
    const metal = hat.color || C.metal, plume = hat.plume || C.plume || RED;
    const hi = X.hi;
    const cover = { none: 0, headband: 0, diadem: 0, laurel: 0, crown: 0.3, pheasant: 0.3, feathers: 0, chief: 0, shaman: 0, goldcrown: 0 }[tp];
    const res = { coversTop: cover == null || cover > 0.5, coversBack: ['helmet', 'galea', 'wahelm', 'keffiyeh', 'conical', 'spangen', 'celthelm', 'tiara', 'felt', 'furhat', 'turban'].includes(tp) };
    const rim = (front, back) => th => lerp(back, front, (1 + Math.cos(th)) / 2);
    const bowl = (o) => headShell(X, Hp, Object.assign({ phi0: rim(0.3, -0.3), off: (th, u) => 0.014 + 0.01 * u, reg: 'metal', col: metal }, o));
    const apexY = (sy, ap) => headAt(Hp, 0, PI / 2 - 0.001, 0.024)[1] + (sy || 0) + (ap || 0);
    if (tp === 'helmet') {
      const v = hat.variant || 'plain';
      const spike = v === 'spike' ? 0.09 : 0;
      bowl({ phi0: rim(0.3, -0.25), sy: 0.012, apex: spike, reg: 'metal', col: metal });
      // 盔沿
      if (hi >= 1) headRing(X, Hp, rim(0.3, -0.25), 0.017, 0.007, 'goldEngr', C.trim);
      // 竖棱
      if (hi >= 2) for (let i = 0; i < 4; i++) {
        const th = i * PI / 2 + PI / 4, pts = [];
        for (let j = 0; j <= 5; j++) { const ph = lerp(rim(0.3, -0.25)(th), PI / 2 - 0.02, j / 5); const p = headAt(Hp, th, ph, 0.026 + 0.004 * j / 5); p[1] += 0.012 * Math.pow(j / 5, 2) + spike * Math.pow(j / 5, 3); pts.push(p); }
        tube(gb, pts, { r: 0.004, n: 4, reg: 'gold', col: col(C.trim), w: B.head });
      }
      // 顿项（护颈）
      const ng = hat.neck || (look.kind === 'general' ? shade(C.cloth2 || '#3a2a2a', -0.1) : null);
      panel(gb, (s, t) => {
        const th = lerp(1.15, TAU - 1.15, s);
        const p = headAt(Hp, th, rim(0.3, -0.25)(th) + 0.02, 0.018);
        const n = headN(p);
        return [p[0] + n[0] * 0.04 * t, p[1] - t * 0.1, p[2] + n[2] * 0.05 * t];
      }, { nu: Math.max(3, lod.pn + 1), nv: Math.max(1, lod.pv - 6), th: 0.006, reg: ng ? 'cloth' : 'lamellar', col: col(ng || metal), w: (p, s, t) => [[B.head, 1 - 0.4 * t], [B.neck, 0.4 * t]] });
      // 护耳
      if (hi >= 1) for (const sg of [1, -1]) panel(gb, (s, t) => {
        const th = sg * lerp(0.95, 1.65, s);
        const p = headAt(Hp, th, rim(0.3, -0.25)(th) + 0.01, 0.02 + 0.006 * t);
        return [p[0] + sg * 0.008 * t, p[1] - t * 0.085, p[2] + 0.006 * t];
      }, { nu: 2, nv: 2, th: 0.006, reg: 'metal', col: col(metal), w: B.head });
      // 眉庇
      panel(gb, (s, t) => { const th = lerp(-0.85, 0.85, s); const p = headAt(Hp, th, 0.3, 0.02); const n = headN(p); return [p[0] + n[0] * 0.03 * t, p[1] - 0.004 * t, p[2] + n[2] * 0.035 * t]; },
        { nu: Math.max(2, lod.pn), nv: 1, th: 0.005, reg: 'goldEngr', col: col(C.trim), w: B.head, mirrorS: true });
      const top = [0, apexY(0.012, spike), HEAD_C[2] - 0.004];
      if (v === 'wing' && hi >= 1) for (const sg of [1, -1]) {
        featherStrip(X, bez(headAt(Hp, sg * 1.45, 0.45, 0.02), [sg * 0.13, 0.27, -0.01], [sg * 0.18, 0.33, -0.05], [sg * 0.2, 0.4, -0.1], 6), 0.07, 0.02, 'gold', C.trim, B.head, 6);
      }
      if (v === 'horn' && hi >= 0) for (const sg of [1, -1]) tube(gb, bez(headAt(Hp, sg * 1.2, 0.6, 0.015), [sg * 0.13, 0.25, 0.02], [sg * 0.16, 0.33, 0.04], [sg * 0.13, 0.4, 0.07], 6), { r: s => 0.014 * (1 - s) + 0.002, n: 6, reg: 'gold', col: col('#e8e0c8'), w: B.head });
      if (v === 'lion') {
        // 狮首（马超）：盔前一张浮雕狮面 —— 扁平面板（狮面贴图）+ 隆起的口鼻、眉骨、红宝石眼、双耳，四周一圈鬃毛簇
        const N = v3norm([0, 0.3, 1]), c = headAt(Hp, 0, 0.6, 0.028), AX = axesFromY(N), r = 0.056, start = gb.nv;
        const up = v3norm(v3sub([0, 1, 0], v3mul(N, N[1]))), sd = v3cross(up, N);
        const at = (x, y, z) => v3add(c, v3add(v3mul(sd, x), v3add(v3mul(up, y), v3mul(N, z))));
        lathe(gb, [[r, -0.01, null, 0], [r * 1.03, 0, null, 0.1], [r * 0.92, 0.007, null, 0.3], [r * 0.55, 0.011, null, 0.65], [0, 0.012, null, 1]], { c, axes: AX, n: lod.ring, reg: 'lion', col: col(GOLD), w: B.head });
        fixDiscUV(gb, REG('lion'), c, AX, r, start);
        const g = col(GOLD), gd = col(shade(GOLD, -0.25));
        ellipsoid(gb, at(0, -0.016, 0.016), [0.021, 0.015, 0.014], { nl: hi >= 1 ? 5 : 3, n: hi >= 1 ? 8 : 6, reg: 'gold', col: g, w: B.head, axes: { X: sd, Y: up, Z: N } });    // 口鼻
        ellipsoid(gb, at(0, -0.004, 0.026), [0.009, 0.006, 0.007], { nl: 3, n: 6, reg: 'gold', col: gd, w: B.head, axes: { X: sd, Y: up, Z: N } });                         // 鼻头
        if (hi >= 1) {
          for (const sg of [1, -1]) {
            tube(gb, [at(sg * 0.006, 0.014, 0.014), at(sg * 0.02, 0.02, 0.016), at(sg * 0.034, 0.015, 0.01)], { r: q => 0.006 * (1 - 0.5 * q), n: 5, reg: 'gold', col: g, w: B.head });   // 眉骨
            ellipsoid(gb, at(sg * 0.02, 0.008, 0.014), [0.006, 0.004, 0.004], { nl: 3, n: 6, reg: 'white', col: col('#d02828'), w: B.head });                                     // 宝石眼
            tube(gb, [at(sg * 0.04, 0.036, 0.0), at(sg * 0.046, 0.052, -0.004)], { r: q => 0.01 * (1 - q) + 0.002, n: 5, reg: 'gold', col: g, w: B.head });                     // 耳
            tube(gb, [at(sg * 0.012, -0.03, 0.016), at(sg * 0.014, -0.042, 0.012)], { r: q => 0.004 * (1 - q) + 0.001, n: 4, reg: 'white', col: col('#f0ead8'), w: B.head });  // 獠牙
          }
          const nm = hi >= 2 ? 14 : 9;
          for (let i = 0; i < nm; i++) {
            const a = PI * 0.15 + (i + 0.5) / nm * PI * 1.7, dx = Math.cos(a), dy = Math.sin(a) * 0.95;
            const p0 = at(dx * r * 0.85, dy * r * 0.85, -0.004), p1 = at(dx * r * 1.32, dy * r * 1.32 + 0.004, -0.018);
            tube(gb, [p0, v3lerp(p0, p1, 0.5), p1], { r: q => 0.011 * (1 - q) + 0.0015, rz: 0.6, n: 5, reg: 'gold', col: g, w: B.head });
          }
        }
      }
      if (v === 'plume' || look.culture === 'korea') { featherStrip(X, bez(top, [0, top[1] + 0.12, top[2] - 0.02], [0, top[1] + 0.22, top[2] - 0.08], [0, top[1] + 0.3, top[2] - 0.16], 6), 0.05, 0.012, 'tassel', plume, (p, s, t) => wT(t, [[B.plume1, 0], [B.plume2, 0.8]])); }
      else {
        if (hi >= 1) tube(gb, [top, [top[0], top[1] + 0.03, top[2]]], { r: 0.008, n: 6, reg: 'gold', col: col(C.trim), w: B.head });
        tassel(X, [top[0], top[1] + (hi >= 1 ? 0.03 : 0.005), top[2]], look.kind === 'general' ? 1.15 : 0.85, plume);
      }
      res.coversTop = true;
    } else if (tp === 'galea') {
      bowl({ phi0: rim(0.32, -0.15), sy: 0.01, reg: 'metal', col: hat.color || (look.kind === 'soldier' ? '#b8bcc4' : BRONZE) });
      headRing(X, Hp, rim(0.32, -0.15), 0.018, 0.008, 'gold', BRONZE);
      // 护颈外翻
      panel(gb, (s, t) => { const th = lerp(1.6, TAU - 1.6, s); const p = headAt(Hp, th, -0.15 + 0.03, 0.02); const n = headN(p); return [p[0] + n[0] * 0.06 * t, p[1] - 0.03 * t, p[2] + n[2] * 0.06 * t]; },
        { nu: Math.max(2, lod.pn), nv: 1, th: 0.006, reg: 'metal', col: col(hat.color || '#b8bcc4'), w: B.head });
      // 护颊
      if (hi >= 1) for (const sg of [1, -1]) panel(gb, (s, t) => { const th = sg * lerp(0.75, 1.55, s); const p = headAt(Hp, th, lerp(0.15, -0.65, t), 0.016 + 0.004 * t); return p; },
        { nu: 2, nv: Math.max(1, lod.pv - 6), th: 0.006, reg: 'metal', col: col(BRONZE), w: B.head });
      // 冠饰（纵向马鬃）
      const pts = [], AX = [];
      const N = Math.max(4, lod.head[0] - 8);
      const secs = [];
      for (let i = 0; i <= N; i++) {
        const a = lerp(0.65, -0.2, i / N) * PI / 2 + (i / N) * 0 ;
        const ph = lerp(0.95, 1.5, Math.sin(PI * i / N) * 0) ;
        const th = i / N < 0.5 ? 0 : PI;
        const pp = i / N;
        const phi = pp < 0.5 ? lerp(0.85, PI / 2, pp * 2) : lerp(PI / 2, 0.6, (pp - 0.5) * 2);
        const thh = pp < 0.5 ? 0 : PI;
        const p = headAt(Hp, thh, phi, 0.034);
        p[1] += 0.01;
        const o = headN(p);
        secs.push({ c: v3madd(p, o, 0.03), X: [1, 0, 0], Z: o, rx: 0.011, rz: 0.04 * Math.sin(PI * clamp(pp * 1.1, 0, 1)) + 0.008, t: pp });
        void a; void ph; void th; pts.push(p); AX.push(o);
      }
      loft(gb, secs, { n: hi >= 1 ? 6 : 4, reg: 'tassel', col: col(plume || RED), w: B.head });
      res.coversTop = true;
    } else if (tp === 'ze' || tp === 'jinxian' || tp === 'futou' || tp === 'lunjin' || tp === 'turban') {
      // 纶巾缺省为青色（与头像一致），帻露出发髻
      const cc = hat.color || (tp === 'turban' ? '#3a6a3a' : tp === 'lunjin' ? '#3a5a8c' : '#22202a');
      const tall = tp === 'lunjin' ? 0.045 : tp === 'futou' ? 0.025 : tp === 'turban' ? 0.03 : 0.006;
      headShell(X, Hp, { phi0: rim(0.42, -0.1), off: (th, u) => 0.01 + (tp === 'turban' ? 0.018 : 0.006) * u, sy: tall, tilt: tp === 'lunjin' ? -0.03 : 0, reg: 'cloth', col: cc });
      if (tp === 'lunjin') for (const [f, b] of hi >= 1 ? [[0.7, 0.35], [0.98, 0.7], [1.22, 1.0]] : [[0.85, 0.5]]) headRing(X, Hp, rim(f, b), 0.016 + 0.004 * f, 0.0045, 'cloth', '#d8e2ee', { rz: 0.6, tn: 4 });
      if (tp === 'ze') res.showBun = true;
      if (tp === 'turban') {
        for (const [f, b, o] of [[0.42, -0.08, 0.02], [0.62, 0.12, 0.028]]) headRing(X, Hp, rim(f, b), o, 0.013, 'cloth', shade(cc, -0.08));
        // 头巾结与垂带
        const kp = headAt(Hp, PI, 0.15, 0.03);
        ellipsoid(gb, kp, [0.025, 0.02, 0.02], { nl: 4, n: 6, reg: 'cloth', col: col(cc), w: B.head });
        if (hi >= 1) for (const sg of [1, -1]) featherStrip(X, bez(kp, [kp[0] + sg * 0.03, kp[1] - 0.06, kp[2] - 0.03], [kp[0] + sg * 0.05, kp[1] - 0.14, kp[2] - 0.05], [kp[0] + sg * 0.06, kp[1] - 0.24, kp[2] - 0.06], 5), 0.05, 0.035, 'cloth', cc, (p, s, t) => wT(t, [[B.head, 0], [B.hair1, 0.6]]));
      } else if (tp === 'jinxian') {
        // 进贤冠：前高后低的冠梁
        const f = headAt(Hp, 0, 0.95, 0.02), bk = headAt(Hp, PI, 1.25, 0.02);
        panel(gb, (s, t) => [lerp(-0.035, 0.035, s), lerp(f[1] + 0.07, bk[1] + 0.02, t), lerp(f[2] - 0.0, bk[2] + 0.02, t)], { nu: 2, nv: 2, th: 0.008, reg: 'cloth', col: col(cc), w: B.head });
        headRing(X, Hp, rim(0.42, -0.1), 0.012, 0.006, 'cloth', shade(cc, 0.2));
      } else if (tp === 'futou' && hi >= 1) {
        const kp = headAt(Hp, PI, 0.4, 0.02);
        for (const sg of [1, -1]) featherStrip(X, bez(kp, [kp[0] + sg * 0.02, kp[1] - 0.05, kp[2] - 0.02], [kp[0] + sg * 0.03, kp[1] - 0.12, kp[2] - 0.03], [kp[0] + sg * 0.035, kp[1] - 0.2, kp[2] - 0.03], 4), 0.03, 0.025, 'cloth', cc, (p, s, t) => wT(t, [[B.head, 0], [B.hair1, 0.6]]));
      } else if (tp === 'lunjin' && hi >= 1) {
        const kp = headAt(Hp, PI, 0.6, 0.02);
        for (const sg of [1, -1]) featherStrip(X, bez(kp, [kp[0] + sg * 0.03, kp[1] - 0.06, kp[2] - 0.03], [kp[0] + sg * 0.04, kp[1] - 0.16, kp[2] - 0.05], [kp[0] + sg * 0.05, kp[1] - 0.3, kp[2] - 0.06], 5), 0.025, 0.022, 'cloth', shade(cc, 0.15), (p, s, t) => wT(t, [[B.head, 0], [B.hair1, 0.6]]));
      }
      res.coversTop = true;
    } else if (tp === 'headband' || tp === 'diadem') {
      const cc = tp === 'diadem' ? (hat.color || GOLD) : (hat.color || RED);
      headRing(X, Hp, rim(0.56, 0.3), 0.006, 0.011, tp === 'diadem' ? 'goldEngr' : 'cloth', cc, { rz: 0.4 });
      if (hat.gem || tp === 'diadem') ellipsoid(gb, headAt(Hp, 0, 0.56, 0.014), [0.011, 0.013, 0.006], { nl: 4, n: 6, reg: 'white', col: col(hat.gem || '#2a6db5'), w: B.head });
      if (hi >= 1) { const kp = headAt(Hp, PI, 0.3, 0.015); for (const sg of [1, -1]) featherStrip(X, bez(kp, [kp[0] + sg * 0.03, kp[1] - 0.04, kp[2] - 0.04], [kp[0] + sg * 0.05, kp[1] - 0.12, kp[2] - 0.08], [kp[0] + sg * 0.06, kp[1] - 0.24, kp[2] - 0.12], 5), 0.03, 0.02, tp === 'diadem' ? 'cloth' : 'cloth', tp === 'diadem' ? '#e8e0d0' : cc, (p, s, t) => wT(t, [[B.head, 0], [B.hair1, 0.6]])); }
    } else if (tp === 'crown' || tp === 'pheasant' || tp === 'goldcrown' || tp === 'tallcrown') {
      const cc = hat.color || GOLD;
      if (tp === 'pheasant' || tp === 'goldcrown') {
        headRing(X, Hp, rim(0.75, 0.45), 0.012, 0.014, 'goldEngr', cc, { rz: 0.45 });
        // 前额金饰
        const c = headAt(Hp, 0, 0.92, 0.02), AX = axesFromY(v3norm([0, 0.5, 1])), r = 0.035, start = gb.nv;
        lathe(gb, [[r, -0.008, null, 0], [r, 0, null, 0.1], [r * 0.6, 0.012, null, 0.5], [0, 0.016, null, 1]], { c, axes: AX, n: lod.ring, reg: 'buckle', col: col(cc), w: B.head });
        fixDiscUV(gb, REG('buckle'), c, AX, r, start);
        ellipsoid(gb, v3add(c, [0, 0.0, 0.016]), [0.008, 0.008, 0.006], { nl: 4, n: 6, reg: 'white', col: col(hat.gem || '#c8302a'), w: B.head });
      }
      if (tp === 'pheasant') {
        // 雉翎：两根长尾羽，自冠两侧向上向后弯
        for (const sg of [1, -1]) {
          const p0 = headAt(Hp, sg * 0.35, 1.0, 0.02);
          featherStrip(X, bez(p0, [p0[0] + sg * 0.06, p0[1] + 0.25, p0[2] + 0.02], [p0[0] + sg * 0.2, p0[1] + 0.5, p0[2] - 0.15], [p0[0] + sg * 0.36, p0[1] + 0.6, p0[2] - 0.42], hi >= 2 ? 14 : 7),
            0.034, 0.012, 'feather', '#ffffff', (p, s, t) => wT(t, [[B.head, 0], [B.plume1, 0.15], [B.plume2, 0.6]]), hi >= 2 ? 16 : 6);
        }
      }
      if (tp === 'goldcrown' && hi >= 0) {
        for (let i = -1; i <= 1; i++) { const p0 = headAt(Hp, i * 0.55, 0.8, 0.025); tube(gb, [p0, [p0[0], p0[1] + 0.12, p0[2] + 0.01]], { r: 0.007, n: 4, reg: 'gold', col: col(cc), w: B.head }); }
      }
      if (tp === 'tallcrown') {
        const c = [0, apexY(0, 0) - 0.04, HEAD_C[2]];
        lathe(gb, [[0.08, 0], [0.085, 0.03], [0.06, 0.05], [0.065, 0.08], [0.04, 0.12], [0.042, 0.15], [0.02, 0.2], [0.004, 0.26]], { c, n: lod.ring, reg: 'gold', col: col(cc), w: B.head });
        res.coversTop = true;
      }
    } else if (tp === 'feathers' || tp === 'chief') {
      headRing(X, Hp, rim(0.6, 0.35), 0.012, 0.012, 'cloth', hat.color || '#b8862a', { rz: 0.5 });
      const n = tp === 'chief' ? (hi >= 1 ? 9 : 5) : (hi >= 1 ? 6 : 3);
      const cols = hat.colors || ['#d8a020', '#c03028', '#ece4d4'];
      for (let i = 0; i < n; i++) {
        const a = lerp(-1.1, 1.1, n > 1 ? i / (n - 1) : 0.5) + PI * (tp === 'chief' ? 0 : 0);
        const p0 = headAt(Hp, a, 0.6, 0.02), d = headN(p0);
        const L = (tp === 'chief' ? 0.3 : 0.2) * (1 - 0.3 * Math.abs(a));
        featherStrip(X, [p0, v3add(p0, [d[0] * 0.04, L * 0.5, d[2] * 0.04 - 0.02]), v3add(p0, [d[0] * 0.09, L, d[2] * 0.06 - 0.06])], 0.03, 0.014, 'feather', cols[i % cols.length], B.head, 3);
      }
    } else if (tp === 'wahelm') {
      bowl({ phi0: rim(0.3, -0.25), sy: 0.02, reg: 'rivet', col: hat.color || '#3a3c44' });
      headRing(X, Hp, rim(0.3, -0.25), 0.018, 0.006, 'metal', '#3a3c44');
      // 衝角（前方鸟喙状脊）
      const secs = [];
      if (hi >= 1) { for (let i = 0; i <= 4; i++) { const t = i / 4; const p = headAt(Hp, 0, lerp(0.3, 1.2, t), 0.03); p[2] += 0.035 * (1 - t); p[1] += 0.01 * t; secs.push({ c: p, X: [1, 0, 0], Z: headN(p), rx: 0.006, rz: 0.012, t }); }
        loft(gb, secs, { n: 4, reg: 'metal', col: col('#3a3c44'), w: B.head }); }
      for (let j = 0; j < (hi >= 1 ? 3 : 1); j++) panel(gb, (s, t) => { const th = lerp(1.0, TAU - 1.0, s); const p = headAt(Hp, th, -0.2 + 0.02, 0.022 + j * 0.012); const n = headN(p); return [p[0] + n[0] * 0.03 * t, p[1] - j * 0.03 - t * 0.04, p[2] + n[2] * 0.035 * t]; },
        { nu: Math.max(3, lod.pn), nv: 1, th: 0.004, reg: 'rivet', col: col('#3a3c44'), w: (p, s, t) => [[B.head, 1 - j * 0.15], [B.neck, j * 0.15]] });
      res.coversTop = true;
    } else if (tp === 'jeolpung') {
      headShell(X, Hp, { phi0: rim(0.55, 0.2), off: (th, u) => 0.01 + 0.004 * u, apex: 0.07, reg: 'cloth', col: hat.color || '#c84a2a' });
      for (const sg of [1, -1]) featherStrip(X, bez(headAt(Hp, sg * 1.3, 0.6, 0.02), [sg * 0.1, 0.3, -0.01], [sg * 0.11, 0.38, -0.03], [sg * 0.1, 0.46, -0.05], 4), 0.025, 0.01, 'feather', '#ffffff', B.head, 4);
      res.coversTop = true;
    } else if (tp === 'felt' || tp === 'furhat' || tp === 'pointcap' || tp === 'tallhat' || tp === 'phrygian' || tp === 'tiara' || tp === 'conical' || tp === 'spangen' || tp === 'celthelm') {
      const cc = hat.color || { felt: '#e0d4b8', furhat: '#5a4a3a', pointcap: '#e8dcc0', tallhat: '#e8dcc0', phrygian: '#9a2a24', tiara: '#2a2a5a', conical: '#8a909a', spangen: '#8a909a', celthelm: BRONZE }[tp];
      const metalH = tp === 'conical' || tp === 'spangen' || tp === 'celthelm';
      const ap = { felt: 0.03, furhat: 0.03, pointcap: 0.16, tallhat: 0.13, phrygian: 0.08, tiara: 0.1, conical: 0.12, spangen: 0.08, celthelm: 0.02 }[tp];
      headShell(X, Hp, { phi0: rim(0.38, -0.12), off: (th, u) => 0.013 + (metalH ? 0.006 : 0.008) * u, apex: ap, tilt: tp === 'phrygian' ? 0.09 : tp === 'pointcap' ? -0.03 : 0, reg: metalH ? 'metal' : 'cloth', col: cc, pw: tp === 'tallhat' ? 0.5 : 0.85 });
      if (tp === 'furhat' || tp === 'felt') headRing(X, Hp, rim(0.38, -0.12), 0.022, tp === 'furhat' ? 0.024 : 0.012, tp === 'furhat' ? 'fur' : 'cloth', tp === 'furhat' ? '#8a6a48' : shade(cc, -0.2));
      if (tp === 'tallhat' || tp === 'tiara' || tp === 'conical' || tp === 'spangen') headRing(X, Hp, rim(0.38, -0.12), 0.016, 0.008, 'goldEngr', GOLD);
      if (tp === 'celthelm') { const top = [0, apexY(0, 0.02), HEAD_C[2]]; ellipsoid(gb, top, [0.015, 0.02, 0.015], { nl: 4, n: 6, reg: 'gold', col: col(BRONZE), w: B.head }); }
      if (tp === 'felt' || tp === 'tiara' || tp === 'conical' || tp === 'spangen') {
        // 护耳 / 护颈垂片（锁子 / 毡）
        const reg = tp === 'felt' ? 'cloth' : tp === 'tiara' ? 'cloth' : 'mail';
        panel(gb, (s, t) => { const th = lerp(0.9, TAU - 0.9, s); const p = headAt(Hp, th, rim(0.38, -0.12)(th) + 0.02, 0.018); const n = headN(p); return [p[0] + n[0] * 0.035 * t, p[1] - t * 0.11, p[2] + n[2] * 0.04 * t]; },
          { nu: Math.max(3, lod.pn + 1), nv: Math.max(1, lod.pv - 6), th: 0.006, reg, col: col(reg === 'mail' ? '#8a909a' : cc), w: (p, s, t) => [[B.head, 1 - 0.4 * t], [B.neck, 0.4 * t]] });
      }
      if (tp === 'spangen' && hi >= 1) tube(gb, [headAt(Hp, 0, 0.35, 0.02), headAt(Hp, 0, 0.0, 0.035)], { r: 0.006, n: 4, reg: 'metal', col: col('#8a909a'), w: B.head });
      if (tp === 'tiara' && hi >= 1) for (let i = 0; i < 5; i++) { const p = headAt(Hp, PI, lerp(0.5, 1.4, i / 4), 0.03 + 0.05 * i / 4); p[1] += 0.1 * Math.pow(i / 4, 2); ellipsoid(gb, p, [0.006, 0.014, 0.014], { nl: 3, n: 5, reg: 'gold', col: col(GOLD), w: B.head }); }
      res.coversTop = true;
    } else if (tp === 'keffiyeh') {
      const cc = hat.color || '#ece6da';
      headShell(X, Hp, { phi0: rim(0.5, -0.4), off: (th, u) => 0.012 + 0.006 * u, reg: 'cloth', col: cc });
      panel(gb, (s, t) => { const th = lerp(1.0, TAU - 1.0, s); const p = headAt(Hp, th, rim(0.5, -0.4)(th) + 0.04, 0.016); const n = headN(p); return [p[0] + n[0] * 0.06 * t, p[1] - t * 0.18, p[2] + n[2] * 0.06 * t - 0.02 * t]; },
        { nu: Math.max(3, lod.pn + 1), nv: Math.max(2, lod.pv - 5), th: 0.006, reg: 'cloth', col: col(cc), w: (p, s, t) => [[B.head, 1 - 0.6 * t], [B.neck, 0.6 * t]] });
      headRing(X, Hp, rim(0.75, 0.45), 0.018, 0.009, 'cloth', '#1c1a1e');
      if (hi >= 1) headRing(X, Hp, rim(0.66, 0.36), 0.019, 0.008, 'cloth', '#1c1a1e');
      res.coversTop = true; res.coversBack = true;
    } else if (tp === 'laurel') {
      const n = hi >= 1 ? 18 : 8;
      for (let i = 0; i < n; i++) {
        const th = lerp(-PI * 0.88, PI * 0.88, i / (n - 1)), p = headAt(Hp, th, lerp(0.72, 0.5, Math.abs(th) / PI), 0.018);
        ellipsoid(gb, p, [0.007, 0.018, 0.006], { nl: 3, n: 5, reg: 'gold', col: col('#d8b850'), w: B.head, axes: axesFromY(v3norm([Math.cos(th) * 0.4, 0.8, -Math.sin(th) * 0.4])) });
      }
    } else if (tp === 'shaman') {
      headRing(X, Hp, rim(0.62, 0.3), 0.014, 0.012, 'cloth', '#e8dcc0', { rz: 0.5 });
      const c = headAt(Hp, 0, 0.75, 0.03), AX = axesFromY(v3norm([0, 0.3, 1])), r = 0.04, start = gb.nv;
      lathe(gb, [[r, -0.006, null, 0], [r, 0, null, 0.1], [r * 0.8, 0.006, null, 0.4], [0, 0.008, null, 1]], { c, axes: AX, n: lod.ring, reg: 'mirror', col: col(BRONZE), w: B.head });
      fixDiscUV(gb, REG('mirror'), c, AX, r, start);
    }
    return res;
  }

  // ------------------------------------------------------------- 头发 --
  function buildHair(X, Hp, cov) {
    const { gb, lod, look } = X;
    const hs = (look.hair && look.hair.style) || 'topknot';
    if (hs === 'bald') return;
    const hc = look.hair.color || '#1c1816', vol = (look.hair.vol || 1);
    const recede = look.hair.recede || 0;
    // 发壳（被盔帽完全盖住时省略）
    if (!cov.coversTop) {
      const thick = { short: 0.006, bob: 0.012, curly: 0.014, bushy: 0.02, wild: 0.024, spiky: 0.012, kunfa: 0.004, romanf: 0.012 }[hs] || 0.01;
      headShell(X, Hp, {
        phi0: th => hairline(th, hs, recede), reg: 'hair', col: hc, pw: 0.9,
        off: (th, u, ph) => (0.003 + thick * vol * sstep(0, 0.25, ph - hairline(th, hs, recede))) * (hs === 'curly' || hs === 'wild' ? 1 + 0.3 * Math.sin(th * 7) * Math.sin(ph * 9) : 1),
      });
    }
    const top = headAt(Hp, PI, 1.25, 0.01);
    const bunAt = (p, r, tie) => {
      ellipsoid(gb, p, r, { nl: Math.max(4, lod.hand[0]), n: Math.max(6, lod.hand[1]), reg: 'hair', col: col(hc), w: B.head });
      if (X.hi >= 1 && tie !== null) headRingSmall(X, p, r[0] * 1.02, r[1] * -0.55, tie || '#c03028');
    };
    if (hs === 'topknot' || hs === 'highbun' || hs === 'mallet') {
      if (cov.coversTop && hs === 'topknot' && !cov.showBun) return;
      const p = hs === 'mallet' ? headAt(Hp, PI, 0.75, 0.04) : headAt(Hp, PI, cov.showBun ? 1.47 : 1.38, cov.showBun ? 0.034 : 0.03);
      if (hs !== 'mallet') p[1] += hs === 'highbun' ? 0.05 : cov.showBun ? 0.04 : 0.025;
      const crown = look.hat && look.hat.type === 'crown';
      bunAt(p, hs === 'mallet' ? [0.045, 0.042, 0.042] : [0.036, 0.032, 0.036], crown ? null : look.hair.tie);
      if (crown) {
        // 束发冠：包住发髻的金冠 + 横簪
        lathe(gb, [[0.03, -0.022], [0.042, -0.012], [0.044, 0.012], [0.036, 0.03], [0.03, 0.036]].map((q, i) => [q[0], q[1], null, i / 4]), { c: p, n: Math.max(8, X.lod.ring), reg: 'goldEngr', col: col((look.hat && look.hat.color) || GOLD), w: B.head, sz: 0.85 });
        if (look.hat.gem) ellipsoid(gb, [p[0], p[1] + 0.0, p[2] + 0.04], [0.008, 0.008, 0.005], { nl: 3, n: 5, reg: 'white', col: col(look.hat.gem), w: B.head });
      }
      if (X.hi >= 1 && (hs === 'topknot' || crown)) tube(gb, [[p[0] - (crown ? 0.075 : 0.05), p[1] + 0.008, p[2]], [p[0] + (crown ? 0.075 : 0.05), p[1] + 0.008, p[2]]], { r: crown ? 0.005 : 0.0035, n: 4, reg: 'gold', col: col(look.hair.pin || GOLD), w: B.head });
    } else if (hs === 'bun' || hs === 'romanf' || hs === 'twinbun') {
      if (hs === 'twinbun') for (const sg of [1, -1]) bunAt(headAt(Hp, sg * 1.1, 1.1, 0.03), [0.03, 0.03, 0.03], look.hair.tie);
      else bunAt(headAt(Hp, PI, 0.55, 0.045), [0.042, 0.038, 0.035], look.hair.tie);
    } else if (hs === 'mizura') {
      for (const sg of [1, -1]) {
        const c = headAt(Hp, sg * 1.5, -0.25, 0.025);
        tube(gb, bez([c[0], c[1] + 0.06, c[2] - 0.005], [c[0] + sg * 0.05, c[1] + 0.04, c[2]], [c[0] + sg * 0.05, c[1] - 0.06, c[2]], [c[0] + sg * 0.005, c[1] - 0.05, c[2] - 0.005], X.hi >= 1 ? 6 : 3), { r: 0.014, n: X.hi >= 1 ? 6 : 4, reg: 'hair', col: col(hc), w: B.head });
        if (X.hi >= 1) tube(gb, [[c[0] + sg * 0.03, c[1] - 0.004, c[2] - 0.01], [c[0] + sg * 0.03, c[1] + 0.012, c[2] - 0.01]], { r: 0.016, n: 6, reg: 'cloth', col: col('#e8dcc0'), w: B.head });
      }
    } else if (hs === 'suebian') {
      const c = headAt(Hp, -1.9, 0.5, 0.04);
      ellipsoid(gb, c, [0.03, 0.036, 0.034], { nl: X.hi === 0 ? 3 : 5, n: X.hi === 0 ? 5 : 7, reg: 'hair', col: col(hc), w: B.head });
      tube(gb, bez(c, [c[0] - 0.02, c[1] - 0.04, c[2] - 0.02], [c[0] - 0.02, c[1] - 0.09, c[2] - 0.04], [c[0] - 0.01, c[1] - 0.14, c[2] - 0.05], X.hi === 0 ? 2 : 5), { r: s => 0.02 * (1 - s) + 0.006, n: X.hi === 0 ? 4 : 6, reg: 'hair', col: col(hc), w: (s) => [[B.head, 1 - s * 0.5], [B.hair1, s * 0.5]] });
    }
    // 长发 / 辫 / 髡发两鬓
    if (hs === 'long' || hs === 'wahair' || hs === 'wild' || hs === 'bushy' || hs === 'bob' || hs === 'romanf') {
      const len = { long: 0.28, wahair: 0.36, wild: 0.18, bushy: 0.12, bob: 0.07, romanf: 0.05 }[hs] * (look.sex === 'f' ? 1.2 : 1);
      if (!(cov.coversBack && len < 0.15)) panel(gb, (s, t) => {
        const th = lerp(1.6, TAU - 1.6, s);
        const p = headAt(Hp, th, lerp(0.2, -0.45, t), 0.012 + 0.012 * vol);
        const n = headN(p);
        const yy = t < 0.5 ? p[1] : lerp(p[1], p[1] - len, (t - 0.5) * 2);
        return [p[0] * (1 + 0.15 * t) + n[0] * 0.01 * t, t < 0.5 ? p[1] : yy, p[2] + n[2] * 0.02 * t - (t > 0.5 ? (t - 0.5) * 0.06 : 0)];
      }, { nu: Math.max(3, lod.pn + 1), nv: Math.max(2, lod.pv - 4), th: 0.008, reg: 'hair', col: col(hc), w: (p, s, t) => wT(t, [[B.head, 0.3], [B.hair1, 0.9]]), uv: (s, t) => [s, 1 - t] });
    }
    if (hs === 'braids' || hs === 'koreanbraid' || hs === 'kunfa') {
      for (const sg of (hs === 'koreanbraid' ? [0] : [1, -1])) {
        const p0 = hs === 'kunfa' ? headAt(Hp, sg * 1.35, 0.2, 0.02) : headAt(Hp, sg * 2.2, -0.1, 0.02);
        const L = hs === 'kunfa' ? 0.14 : 0.3;
        const pts = bez(p0, [p0[0] + sg * 0.02, p0[1] - L * 0.3, p0[2] - 0.02], [p0[0] + sg * 0.03, p0[1] - L * 0.7, p0[2] - 0.0], [p0[0] + sg * 0.03, p0[1] - L, p0[2] + 0.02], X.hi === 0 ? 2 : 6);
        tube(gb, pts, { r: s => (0.012 - 0.004 * s) * (X.hi === 0 ? 1.1 : 1 + 0.25 * Math.sin(s * 40)), n: X.hi === 0 ? 4 : 6, reg: 'hair', col: col(hc), w: s => wT(s, [[B.head, 0.2], [B.hair1, 1]]) });
      }
    }
  }
  function headRingSmall(X, c, r, dy, cc) {
    const pts = [];
    for (let i = 0; i <= 10; i++) { const a = i / 10 * TAU; pts.push([c[0] + Math.sin(a) * r, c[1] + dy, c[2] + Math.cos(a) * r]); }
    tube(X.gb, pts, { r: 0.004, n: 4, reg: 'cloth', col: col(cc), w: B.head });
  }

  // ------------------------------------------------------------- 胡须 --
  function buildBeard(X, Hp) {
    const { gb, lod, look } = X;
    const st = (look.beard && look.beard.style) || 'none';
    const bc = look.beard.color || look.hair.color || '#1c1816', len = look.beard.len || 1;
    const ms = (look.mus && look.mus.style) || 'none';
    if (look.sex === 'f') return;
    // 下颌须壳：θ ∈ [−w, w]，φ 从脸颊 / 下唇下方 到下巴之下
    const shell = (w, thick, extra, bristle) => {
      const topPhi = th => { const a = Math.abs(th) / w; return lerp(FEAT.mouthP - 0.13, -0.12, sstep(0.25, 1, a)); };
      panel(gb, (s, t) => {
        const th = lerp(-w, w, s), ph = lerp(topPhi(th), -1.48, t);
        const p = headPoint(th, ph, Hp), n = headN(p);
        const edge = Math.min(sstep(0, 0.25, t), sstep(0, 0.12, Math.min(s, 1 - s)));
        let d = 0.002 + thick * edge;
        if (bristle) d *= 1 + 0.45 * Math.sin(th * 13) * Math.sin(ph * 11);
        const q = v3madd(p, n, d);
        q[1] -= extra * Math.pow(t, 2) * gauss(th, 0, 0.9);
        q[2] += extra * 0.4 * Math.pow(t, 2) * gauss(th, 0, 0.9);
        return q;
      }, { nu: X.hi === 0 ? 3 : Math.max(3, lod.pn + 2), nv: Math.max(2, lod.pv - 4), th: 0.004, reg: 'hair', col: col(bc), colB: col(shade(bc, -0.3)), w: B.head, uv: (s, t) => [s, 1 - t] });
    };
    // 垂须：自下巴垂下的锥形（美髯 / 长须 / 分叉）
    const hang = (L, w0, fork, curl) => {
      const c = headPoint(0, -1.05, Hp);
      const N = X.hi >= 2 ? 10 : X.hi >= 1 ? 5 : 3;
      for (const f of (fork ? [-1, 1] : [0])) {
        const pts = [];
        for (let i = 0; i <= N; i++) { const t = i / N; pts.push([c[0] + f * 0.018 * t, c[1] + 0.01 - L * t, c[2] + 0.012 + 0.03 * Math.sin(t * 2.4) + (curl ? 0.01 * Math.sin(t * 12) : 0)]); }
        tube(gb, pts, { r: s => (w0 * Math.sin(PI * Math.min(1, 0.25 + s * 0.85)) * (1 - s * 0.6) + 0.002) * (fork ? 0.7 : 1), rz: 0.55, n: Math.max(5, lod.tube), reg: 'hair', col: col(bc), up: [0, 0, 1],
          w: s => (L > 0.2 ? wT(s, [[B.head, 0.25], [B.neck, 0.7]]) : B.head) });
      }
    };
    // 簇：自下颌向外下方伸出的尖锥（虬髯 / 满腮胡的蓬松轮廓）
    const tufts = (n, L, r0, spread) => {
      for (let i = 0; i < n; i++) {
        const a = lerp(-spread, spread, n > 1 ? i / (n - 1) : 0.5), ph = lerp(-0.95, -1.35, Math.abs(a) / Math.max(spread, 1e-3));
        const p = headPoint(a, ph, Hp), nn = headN(p), dir = v3norm([nn[0] * 0.9, -0.75 - 0.2 * Math.cos(a * 2), nn[2] * 0.7]);
        const Ln = L * (1 - 0.25 * Math.abs(a) / Math.max(spread, 1e-3)) * (0.85 + 0.3 * ((i * 0.618) % 1));
        tube(gb, [v3madd(p, nn, -0.004), v3madd(p, dir, Ln * 0.5), v3madd(p, dir, Ln)], { r: q => r0 * (1 - q) + 0.0012, rz: 0.7, n: X.hi >= 2 ? 5 : 4, reg: 'hair', col: col(bc), w: B.head, up: [0, 0, 1] });
      }
    };
    if (st === 'goatee') { hang(0.05 * len, 0.018, false); }
    else if (st === 'short') shell(1.35, 0.008 * len, 0.004, false);
    else if (st === 'full' || st === 'bristle' || st === 'curled' || st === 'braided') {
      shell(1.42, 0.016 * len, 0.03 * len, st === 'bristle');
      if (X.hi >= 1 && (st === 'bristle' || st === 'full')) tufts(X.hi >= 2 ? 9 : 5, (st === 'bristle' ? 0.05 : 0.04) * len, st === 'bristle' ? 0.011 : 0.013, 1.2);
      if (st === 'braided') hang(0.12 * len, 0.016, false, true);
    }
    else if (st === 'long' || st === 'flowing' || st === 'forked') {
      shell(1.3, 0.008 * len, 0.008, false);
      hang((st === 'flowing' ? 0.3 : st === 'forked' ? 0.16 : 0.15) * len, st === 'flowing' ? 0.04 : 0.028, st === 'forked', false);
      // 美髯两侧的鬓须：自腮边贴着脸颊汇入主须（不再单独悬空）
      if (st === 'flowing' && X.hi >= 1) for (const sg of [1, -1]) { const p0 = headPoint(sg * 0.75, -0.7, Hp), c0 = headPoint(0, -1.05, Hp); tube(gb, [v3madd(p0, headN(p0), -0.003), [lerp(p0[0], c0[0], 0.55), p0[1] - 0.07 * len, p0[2] + 0.025], [sg * 0.012, c0[1] - 0.12 * len, c0[2] + 0.035]], { r: s => 0.013 * (1 - s) + 0.004, rz: 0.55, n: 5, reg: 'hair', col: col(bc), w: s => wT(s, [[B.head, 0.3], [B.neck, 0.9]]) }); }
    }
    // 八字胡
    if (ms !== 'none' && X.hi >= 0) {
      for (const sg of [1, -1]) {
        const a = headPoint(sg * 0.06, FEAT.noseP - 0.14, Hp), b = headPoint(sg * 0.3, FEAT.mouthP + 0.06, Hp), c = headPoint(sg * 0.44, ms === 'droop' ? FEAT.mouthP - 0.32 : ms === 'curl' ? FEAT.mouthP + 0.12 : FEAT.mouthP - 0.06, Hp);
        const off = p => v3madd(p, headN(p), 0.004);
        const thk = ms === 'thin' ? 0.0035 : ms === 'bristle' ? 0.008 : 0.006;
        tube(gb, [off(a), off(v3lerp(a, b, 0.5)), off(b), off(c)], { r: s => thk * (1 - s * 0.6), rz: 0.6, n: 4, reg: 'hair', col: col(bc), w: B.head });
      }
    }
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
  };
  const DUAL_BASE = { swords: 'jian', twinji: 'ji1', shortji: 'sji1', twindao: 'dao' };

  function W_ctx(gb, lod, w, colMul) { return { gb, lod, w: w == null ? B.handR : w, hi: lod.detail }; }
  function shaft(K, y0, y1, r, reg, cc, taper) {
    tube(K.gb, K.hi === 0 ? [[0, y0, 0], [0, y1, 0]] : [[0, y0, 0], [0, (y0 + y1) / 2, 0], [0, y1, 0]], { r: s => r * (1 - (taper || 0) * s) * (K.hi === 0 ? 1.15 : 1), n: K.hi === 0 ? 4 : Math.max(5, K.lod.tube), reg, col: col(cc), w: K.w, capStart: K.hi >= 1, capEnd: K.hi >= 1 });
  }
  function ringAtY(K, y, r, h, reg, cc) {
    if (K.hi === 0) return;
    lathe(K.gb, [[r * 0.85, y - h / 2, null, 0], [r, y - h * 0.3, null, 0.25], [r, y + h * 0.3, null, 0.75], [r * 0.85, y + h / 2, null, 1]], { n: Math.max(6, K.lod.tube), reg: reg || 'goldEngr', col: col(cc || GOLD), w: K.w });
  }
  function spikeAt(K, y, len, r, cc, down) {
    if (K.hi === 0) return;
    const prof = down ? [[0, y - len], [r * 0.5, y - len * 0.6], [r, y - 0.01], [r * 0.9, y]] : [[r * 0.9, y], [r, y + 0.01], [r * 0.5, y + len * 0.6], [0, y + len]];
    lathe(K.gb, prof, { n: Math.max(5, K.lod.tube), reg: 'brushed', col: col(cc || STEEL), w: K.w, capStart: !down, capEnd: down });
  }
  // 缨：套在杆上向下垂的马尾（y 处，长 len）
  function shaftTassel(K, y, len, r, cc) {
    const prof = (K.hi === 0 ? [[0.012, y + 0.01], [r, y - len * 0.45], [r * 0.4, y - len], [0.012, y - len * 0.7]] : [[0.012, y + 0.01], [r * 0.6, y - len * 0.15], [r, y - len * 0.45], [r * 0.85, y - len * 0.85], [r * 0.4, y - len], [0.012, y - len * 0.7]]).reverse();
    lathe(K.gb, prof, { n: K.hi === 0 ? 4 : Math.max(6, K.lod.tube + 2), reg: 'tassel', col: col(cc || RED), w: K.w });
  }
  // 刃：沿 +Y 的站点（y、刃口宽、刃背宽、半厚、z 偏移）
  function bladeY(K, pts, dbl, cc) {
    const st = pts.map(q => ({ p: [0, q[0], q[4] || 0], e: [0, 0, 1], n: [1, 0, 0], we: q[1], wb: q[2], th: q[3], dbl }));
    // 弯刃：刃口方向随中线切向旋转
    for (let i = 0; i < st.length; i++) {
      const a = st[Math.max(0, i - 1)].p, b = st[Math.min(st.length - 1, i + 1)].p;
      const T = v3norm(v3sub(b, a));
      st[i].e = v3norm(v3cross([1, 0, 0], T).map(v => -v));
      if (st[i].e[2] < 0) st[i].e = st[i].e.map(v => -v);
    }
    blade(K.gb, st, { col: col(cc || '#ffffff'), w: K.w });
  }
  function dragonHead(K, y, cc, s) {
    s = s || 1;
    const gb = K.gb;
    ellipsoid(gb, [0, y, 0.01 * s], [0.032 * s, 0.05 * s, 0.04 * s], { nl: Math.max(4, K.lod.hand[0]), n: Math.max(6, K.lod.hand[1]), reg: 'gold', col: col(cc || GOLD), w: K.w });
    // 吻部（朝刃口方向）与双角
    tube(gb, [[0, y + 0.01 * s, 0.03 * s], [0, y + 0.03 * s, 0.07 * s]], { r: t => (0.022 - 0.01 * t) * s, n: 6, reg: 'gold', col: col(cc || GOLD), w: K.w, capEnd: true });
    if (K.hi >= 1) for (const sg of [1, -1]) tube(gb, bez([sg * 0.015 * s, y + 0.03 * s, -0.01 * s], [sg * 0.03 * s, y + 0.06 * s, -0.03 * s], [sg * 0.03 * s, y + 0.09 * s, -0.05 * s], [sg * 0.02 * s, y + 0.12 * s, -0.06 * s], 4), { r: t => 0.007 * (1 - t) + 0.002, n: 4, reg: 'gold', col: col(cc || GOLD), w: K.w });
  }

  function buildWeaponInto(gb, id, lod, o) {
    o = o || {};
    const K = W_ctx(gb, lod, o.w);
    const def = WEAPONS[id] || WEAPONS.jian;
    let info = { id, name: def.name, style: def.style, len: 1, tip: [0, 1, 0], off: null, base: [0, 0.1, 0] };
    const SH = o.shaft || '#3a2418';
    switch (DUAL_BASE[id] || id) {
      case 'guandao': {
        shaft(K, -0.9, 1.02, 0.017, 'wood', '#2a3a2c');
        spikeAt(K, -0.9, 0.1, 0.017, GOLD, true);
        for (const y of [-0.82, 0.25, 0.95]) ringAtY(K, y, 0.021, 0.04);
        shaftTassel(K, 1.0, 0.16, 0.05, '#c8302a');
        dragonHead(K, 1.07, '#5aa070');
        bladeY(K, [[1.1, 0.06, 0.02, 0.009, 0], [1.22, 0.1, 0.022, 0.008, -0.005], [1.36, 0.14, 0.02, 0.007, -0.012], [1.5, 0.15, 0.018, 0.006, -0.025], [1.62, 0.12, 0.015, 0.005, -0.045], [1.72, 0.06, 0.01, 0.004, -0.07], [1.78, 0.0, 0.0, 0.002, -0.1]], false);
        // 刀背倒钩
        if (K.hi >= 1) tube(gb, bez([0, 1.3, -0.02], [0, 1.31, -0.06], [0, 1.34, -0.085], [0, 1.38, -0.095], 4), { r: t => 0.01 * (1 - t) + 0.001, rz: 0.4, n: 4, reg: 'blade', col: [1, 1, 1], w: K.w });
        info = Object.assign(info, { len: 1.78, tip: [0, 1.7, 0.02], base: [0, 1.15, 0], off: [0, 0.6, 0] });
        break;
      }
      case 'snake': {
        shaft(K, -1.0, 1.42, 0.016, 'wood', '#1e1a1e');
        spikeAt(K, -1.0, 0.08, 0.016, GOLD, true);
        for (const y of [-0.9, 0.3, 1.35]) ringAtY(K, y, 0.02, 0.035);
        shaftTassel(K, 1.4, 0.15, 0.045, '#c8302a');
        ringAtY(K, 1.43, 0.022, 0.05, 'gold', GOLD);
        const pts = [];
        for (let i = 0; i <= 12; i++) { const t = i / 12; pts.push([1.46 + t * 0.52, 0.04 * (1 - t * 0.85), 0.04 * (1 - t * 0.85), 0.006, 0.03 * Math.sin(t * PI * 3) * (1 - t * 0.6)]); }
        pts[12][1] = pts[12][2] = 0;
        bladeY(K, pts, true);
        info = Object.assign(info, { len: 1.98, tip: [0, 1.95, 0], base: [0, 1.46, 0], off: [0, 0.65, 0] });
        break;
      }
      case 'halberd': {
        shaft(K, -0.95, 1.38, 0.017, 'wood', '#5a1a1a');
        spikeAt(K, -0.95, 0.09, 0.017, GOLD, true);
        for (const y of [-0.85, 0.2, 1.28]) ringAtY(K, y, 0.021, 0.04);
        ringAtY(K, 1.37, 0.026, 0.06, 'gold', GOLD);
        bladeY(K, [[1.4, 0.03, 0.03, 0.008], [1.5, 0.036, 0.036, 0.007], [1.65, 0.03, 0.03, 0.006], [1.78, 0.014, 0.014, 0.005], [1.86, 0, 0, 0.002]], true);
        // 双月牙
        for (const sg of [1, -1]) {
          const st = [];
          for (let i = 0; i <= 8; i++) {
            const a = lerp(-1.25, 1.25, i / 8), R = 0.13;
            const c = [0, 1.36 + Math.sin(a) * R, sg * (0.035 + Math.cos(a) * R * 0.75)];
            const e = v3norm([0, Math.sin(a) * 0.75, sg * Math.cos(a)]);
            const wa = 0.045 * (1 - Math.pow(Math.abs(a) / 1.25, 2)) + 0.004;
            st.push({ p: c, e, n: [1, 0, 0], we: wa, wb: 0.012, th: 0.006, dbl: false });
          }
          st[0].we = st[8].we = 0.003;
          blade(gb, st, { col: col(GOLD === '#e0b050' ? '#f0f0f0' : '#fff'), w: K.w });
          tube(gb, [[0, 1.36, 0], [0, 1.36, sg * 0.05]], { r: 0.012, n: 5, reg: 'gold', col: col(GOLD), w: K.w });
        }
        // 彩带
        if (K.hi >= 1) for (const sg of [1, -1]) featherStrip({ gb, lod }, bez([sg * 0.01, 1.34, 0], [sg * 0.03, 1.25, 0.02], [sg * 0.04, 1.12, 0.0], [sg * 0.05, 0.98, -0.03], 5), 0.035, 0.03, 'cloth', '#c8302a', K.w, 5);
        info = Object.assign(info, { len: 1.86, tip: [0, 1.82, 0], base: [0, 1.4, 0], off: [0, 0.62, 0] });
        break;
      }
      case 'bigblade': case 'fengzui': case 'poleblade': {
        const big = id === 'bigblade', feng = id === 'fengzui';
        const top = big ? 0.72 : 1.0;
        shaft(K, big ? -0.55 : -0.85, top + 0.02, big ? 0.02 : 0.017, 'wood', big ? '#2a2420' : '#3a2418');
        spikeAt(K, big ? -0.55 : -0.85, 0.08, 0.018, STEEL, true);
        ringAtY(K, top, 0.03, 0.06, 'gold', feng ? GOLD : BRONZE);
        if (feng) dragonHead(K, top + 0.06, GOLD, 0.8);
        if (!big) shaftTassel(K, top - 0.02, 0.13, 0.045, feng ? '#c8302a' : '#c8302a');
        const L = big ? 0.78 : 0.62, W0 = big ? 0.13 : 0.08, W1 = big ? 0.2 : 0.12;
        const curve = feng ? 0.12 : 0.04;
        const pts = [];
        for (let i = 0; i <= 8; i++) { const t = i / 8; pts.push([top + 0.04 + t * L, lerp(W0, W1, Math.sin(t * PI * 0.8)) * (t > 0.85 ? (1 - t) / 0.15 : 1), big ? 0.03 : 0.018, big ? 0.011 : 0.008, -curve * t * t]); }
        bladeY(K, pts, false, big ? '#c8ccd2' : '#ffffff');
        info = Object.assign(info, { len: top + 0.04 + L, tip: [0, top + L, -curve * 0.8], base: [0, top + 0.1, 0], off: [0, big ? 0.45 : 0.55, 0] });
        break;
      }
      case 'spear': case 'bawang': case 'yajiao': case 'hutou': case 'lance': case 'pilum': case 'trident': {
        const lance = id === 'lance', pil = id === 'pilum';
        const top = lance ? 1.6 : pil ? 0.9 : 1.32;
        const sc = id === 'yajiao' ? '#e4e4ec' : id === 'hutou' ? '#d8c8a0' : id === 'bawang' ? '#4a1a1a' : SH;
        shaft(K, lance ? -1.4 : pil ? -0.7 : -0.95, top + 0.02, lance ? 0.019 : 0.016, 'wood', sc);
        if (!pil) spikeAt(K, lance ? -1.4 : -0.95, 0.07, 0.016, id === 'hutou' ? GOLD : STEEL, true);
        const fit = id === 'hutou' ? GOLD : id === 'yajiao' ? SILVER : id === 'bawang' ? GOLD : BRONZE;
        if (K.hi >= 1 && !pil) for (const y of [-0.85, 0.3]) ringAtY(K, y, 0.019, 0.03, 'goldEngr', fit);
        ringAtY(K, top, 0.022, 0.05, 'gold', fit);
        if (id === 'yajiao' || id === 'hutou') dragonHead(K, top + 0.04, id === 'hutou' ? GOLD : '#e8eaf0', 0.75);
        if (!lance && !pil) shaftTassel(K, top - 0.01, id === 'spear' ? 0.09 : 0.14, id === 'spear' ? 0.032 : 0.045, id === 'yajiao' || id === 'hutou' ? '#f0f0f0' : '#c8302a');
        const b0 = top + (id === 'yajiao' || id === 'hutou' ? 0.1 : 0.03);
        if (pil) {
          tube(gb, [[0, top, 0], [0, top + 0.45, 0]], { r: 0.006, n: 4, reg: 'brushed', col: col(IRON), w: K.w });
          bladeY(K, [[top + 0.44, 0.012, 0.012, 0.008], [top + 0.5, 0, 0, 0.002]], true);
          info.len = top + 0.5;
        } else if (id === 'trident') {
          bladeY(K, [[b0, 0.025, 0.025, 0.007], [b0 + 0.15, 0.03, 0.03, 0.006], [b0 + 0.3, 0, 0, 0.002]], true);
          for (const sg of [1, -1]) { const st = []; for (let i = 0; i <= 5; i++) { const t = i / 5; st.push({ p: [0, b0 + t * 0.2, sg * (0.03 + 0.05 * Math.sin(t * PI * 0.6))], e: [0, 0, sg], n: [1, 0, 0], we: 0.015 * (1 - t) + 0.002, wb: 0.008 * (1 - t) + 0.002, th: 0.005, dbl: true }); } blade(gb, st, { col: [1, 1, 1], w: K.w }); }
          info.len = b0 + 0.3;
        } else {
          const L = lance ? 0.22 : id === 'bawang' ? 0.4 : id === 'spear' ? 0.26 : 0.36, Wd = lance ? 0.022 : id === 'bawang' ? 0.05 : 0.034;
          bladeY(K, [[b0, Wd * 0.5, Wd * 0.5, 0.007], [b0 + L * 0.3, Wd, Wd, 0.007], [b0 + L * 0.7, Wd * 0.7, Wd * 0.7, 0.006], [b0 + L, 0, 0, 0.002]], true, id === 'hutou' ? '#f0d890' : '#ffffff');
          info.len = b0 + L;
        }
        info = Object.assign(info, { tip: [0, info.len - 0.04, 0], base: [0, b0, 0], off: [0, lance ? 0.7 : 0.6, 0] });
        break;
      }
      case 'bigaxe': case 'axe': {
        const big = id === 'bigaxe';
        const top = big ? 1.05 : 0.95;
        shaft(K, -0.6, top + 0.08, 0.019, 'wood', '#2a2018');
        spikeAt(K, -0.6, 0.06, 0.018, STEEL, true);
        ringAtY(K, top - 0.15, 0.024, 0.04, 'gold', BRONZE);
        spikeAt(K, top + 0.08, 0.14, 0.018, STEEL);
        // 斧刃：朝 +Z 的半月形
        const st = [];
        for (let i = 0; i <= 8; i++) {
          const a = lerp(-1.1, 1.1, i / 8), R = big ? 0.2 : 0.14;
          st.push({ p: [0, top + Math.sin(a) * R * 0.75, 0.03 + Math.cos(a) * R * 0.25], e: v3norm([0, Math.sin(a) * 0.5, Math.cos(a)]), n: [1, 0, 0], we: (big ? 0.1 : 0.07) * Math.cos(a * 0.9), wb: 0.03, th: 0.01, dbl: false });
        }
        blade(gb, st, { col: col('#d8dce2'), w: K.w });
        info = Object.assign(info, { len: top + 0.22, tip: [0, top, 0.2], base: [0, top - 0.1, 0], off: [0, 0.45, 0] });
        break;
      }
      case 'ji1': case 'sji1': {
        const short = id === 'shortji';
        const top = short ? 0.45 : 0.55;
        shaft(K, -0.25, top + 0.02, 0.016, 'wrap', short ? '#5a2a1a' : '#2a2a2e');
        spikeAt(K, -0.25, 0.05, 0.016, STEEL, true);
        ringAtY(K, top, 0.022, 0.04, 'gold', short ? GOLD : IRON);
        bladeY(K, [[top + 0.03, 0.022, 0.022, 0.008], [top + 0.1, 0.03, 0.03, 0.007], [top + (short ? 0.22 : 0.26), 0, 0, 0.002]], true, short ? '#ffffff' : '#b8bcc4');
        const st = [];
        for (let i = 0; i <= 6; i++) { const a = lerp(-1.2, 1.0, i / 6), R = short ? 0.085 : 0.11; st.push({ p: [0, top - 0.02 + Math.sin(a) * R, 0.025 + Math.cos(a) * R * 0.7], e: v3norm([0, Math.sin(a) * 0.7, Math.cos(a)]), n: [1, 0, 0], we: 0.04 * (1 - Math.pow(Math.abs(a + 0.1) / 1.1, 2)) + 0.004, wb: 0.012, th: 0.007, dbl: false }); }
        blade(gb, st, { col: col(short ? '#ffffff' : '#b8bcc4'), w: K.w });
        tube(gb, [[0, top - 0.04, 0], [0, top - 0.04, -0.07]], { r: t => 0.01 * (1 - t) + 0.002, n: 4, reg: 'brushed', col: col(IRON), w: K.w });
        if (short) shaftTassel(K, top - 0.01, 0.09, 0.03, '#c8302a');
        info = Object.assign(info, { len: top + 0.26, tip: [0, top + 0.22, 0], base: [0, top, 0] });
        break;
      }
      case 'jian': case 'yitian': case 'gladius': case 'spatha': case 'dao': case 'guding': case 'tachi': {
        const curved = id === 'dao' || id === 'guding', glad = id === 'gladius', ring = id === 'dao' || id === 'guding' || id === 'tachi';
        const L = glad ? 0.5 : id === 'spatha' ? 0.78 : id === 'yitian' ? 0.86 : 0.76;
        const gripC = glad ? '#d8c8a0' : id === 'yitian' ? '#5a1a1a' : '#2a2018';
        tube(gb, K.hi === 0 ? [[0, -0.11, 0], [0, 0.07, 0]] : [[0, -0.11, 0], [0, -0.02, 0], [0, 0.07, 0]], { r: 0.015, n: K.hi === 0 ? 4 : Math.max(5, K.lod.tube), reg: glad ? 'wood' : 'wrap', col: col(gripC), w: K.w });
        const fit = id === 'yitian' || id === 'guding' ? GOLD : glad || id === 'spatha' ? BRONZE : '#b8a070';
        if (ring && K.hi >= 1) tube(gb, ringPts([0, -0.15, 0], [0, 0, 1], [0, 1, 0], 0.03, 0.03, 10), { r: 0.006, n: 4, reg: 'gold', col: col(fit), w: K.w });
        else if (K.hi >= 1) ellipsoid(gb, [0, -0.125, 0], [0.024, 0.02, 0.024], { nl: 4, n: 6, reg: 'gold', col: col(fit), w: K.w });
        // 护手
        if (K.hi === 0) ellipsoid(gb, [0, 0.075, 0], [0.022, 0.012, 0.045], { nl: 2, n: 4, reg: 'gold', col: col(fit), w: K.w });
        else if (glad || id === 'spatha') ellipsoid(gb, [0, 0.075, 0], [0.025, 0.014, 0.04], { nl: 4, n: 6, reg: 'wood', col: col(gripC), w: K.w });
        else if (ring) lathe(gb, [[0.0, 0.065], [0.035, 0.066], [0.036, 0.075], [0.0, 0.077]], { n: 8, reg: 'gold', col: col(fit), w: K.w, sz: 1.3 });
        else {
          ellipsoid(gb, [0, 0.075, 0], [0.024, 0.018, 0.06], { nl: 4, n: 6, reg: 'gold', col: col(fit), w: K.w });
          if (K.hi >= 1) for (const sg of [1, -1]) tube(gb, bez([0, 0.075, sg * 0.05], [0, 0.065, sg * 0.07], [0, 0.085, sg * 0.08], [0, 0.1, sg * 0.075], 3), { r: 0.006, n: 4, reg: 'gold', col: col(fit), w: K.w });
        }
        const w0 = glad ? 0.03 : curved ? 0.022 : 0.021;
        const pts = [];
        const N = K.hi >= 1 ? 6 : 2;
        for (let i = 0; i <= N; i++) {
          const t = i / N;
          const wt = glad ? (t < 0.75 ? w0 * (1 - 0.15 * t) : w0 * (1 - t) / 0.25 * 0.85) : (t < 0.88 ? w0 * (1 - 0.15 * t) : w0 * 0.87 * (1 - t) / 0.12);
          pts.push([0.08 + t * L, curved ? wt * 1.4 : wt, curved ? wt * 0.35 : wt, curved ? 0.007 : 0.006, curved ? -0.04 * t * t : 0]);
        }
        bladeY(K, pts, !curved && id !== 'tachi');
        if ((id === 'jian' || id === 'yitian') && K.hi >= 1) shaftTassel({ gb, lod, w: K.w, hi: K.hi }, -0.14, 0.12, 0.022, '#c8302a');
        info = Object.assign(info, { len: 0.08 + L, tip: [0, 0.06 + L, curved ? -0.03 : 0], base: [0, 0.12, 0] });
        break;
      }
      case 'mace': {
        shaft(K, -0.14, 0.5, 0.017, 'wrap', '#3a2418');
        lathe(gb, [[0.018, 0.48], [0.036, 0.51], [0.05, 0.56], [0.052, 0.62], [0.04, 0.67], [0.015, 0.7], [0, 0.71]], { n: 16, reg: 'brushed', col: col('#8a8e98'), w: K.w, off: (th) => 0.016 * Math.pow(Math.max(0, Math.cos(th * 3)), 4) });
        ringAtY(K, 0.49, 0.022, 0.03, 'gold', BRONZE);
        info = Object.assign(info, { len: 0.7, tip: [0, 0.62, 0], base: [0, 0.5, 0] });
        break;
      }
      case 'handaxe': {
        shaft(K, -0.12, 0.5, 0.016, 'wood', '#5a3a20');
        const st = [];
        for (let i = 0; i <= 5; i++) { const t = i / 5; st.push({ p: [0, 0.38 + t * 0.12, 0.02 + 0.02 * t], e: [0, -0.5 + t * 0.8, 1], n: [1, 0, 0], we: 0.04 + 0.06 * t * t, wb: 0.02, th: 0.008, dbl: false }); }
        st.forEach(s => (s.e = v3norm(s.e)));
        blade(gb, st, { col: col('#d0d4d8'), w: K.w });
        info = Object.assign(info, { len: 0.52, tip: [0, 0.45, 0.1], base: [0, 0.42, 0] });
        break;
      }
      case 'fan': {
        tube(gb, [[0, -0.08, 0], [0, 0.08, 0]], { r: 0.012, n: 6, reg: 'gold', col: col('#c8a050'), w: K.w });
        ellipsoid(gb, [0, 0.1, 0], [0.02, 0.02, 0.02], { nl: 4, n: 6, reg: 'gold', col: col(GOLD), w: K.w });
        // 羽面：扁平椭圆（白羽）
        panel(gb, (s, t) => { const a = (s - 0.5) * PI * 0.62, r = 0.03 + t * 0.27; return [Math.sin(a) * r * 0.05 + 0.01 * Math.sin(s * PI * 9) * t, 0.1 + Math.cos(a) * r, Math.sin(a) * r]; }, { nu: K.hi >= 1 ? 9 : 3, nv: K.hi >= 1 ? 4 : 2, th: 0.008, reg: 'tassel', col: col('#f4f2ee'), colB: col('#e8e4dc'), w: K.w, uv: (s, t) => [s, t] });
        if (K.hi >= 1) for (let i = 0; i < 7; i++) { const a = (i / 6 - 0.5) * PI * 0.6; tube(gb, [[0, 0.1, 0], [Math.sin(a) * 0.005, 0.1 + Math.cos(a) * 0.27, Math.sin(a) * 0.27]], { r: 0.0025, n: 3, reg: 'gold', col: col('#e8d8a8'), w: K.w }); }
        info = Object.assign(info, { len: 0.4, tip: [0, 0.38, 0], base: [0, 0.12, 0] });
        break;
      }
      case 'bow': case 'huangbow': {
        // 反曲弓：沿 ±Y 两臂，弓背朝 +Z，弦在 −Z 一侧
        const gold = id === 'huangbow';
        const half = gold ? 0.62 : 0.55, pts = [];
        const N = K.hi >= 1 ? 12 : 6;
        for (let i = 0; i <= N; i++) { const t = i / N * 2 - 1, a = Math.abs(t); pts.push([0, t * half, 0.06 * (1 - a * a) - 0.05 * Math.pow(a, 6) * 1.2 + (a > 0.85 ? -0.02 * (a - 0.85) / 0.15 : 0)]); }
        tube(gb, pts, { r: s => (0.013 - 0.007 * Math.abs(2 * s - 1)) * (gold ? 1.1 : 1), rz: 0.8, n: Math.max(5, K.lod.tube), reg: gold ? 'gold' : 'wood', col: col(gold ? '#c89a48' : '#5a3418'), w: K.w });
        tube(gb, [[0, -0.06, 0.05], [0, 0.06, 0.05]], { r: 0.017, n: 6, reg: 'wrap', col: col('#3a2418'), w: K.w });
        // 弦（细管）
        tube(gb, [pts[0], [0, 0, pts[0][2] - 0.005], pts[N]], { r: 0.0018, n: 3, reg: 'white', col: col('#e8e0d0'), w: K.w });
        info = Object.assign(info, { len: half, tip: [0, half, 0], base: [0, 0, 0], string: [0, 0, pts[0][2]], nockTop: pts[N], nockBot: pts[0] });
        break;
      }
      default: return buildWeaponInto(gb, 'jian', lod, o);
    }
    return info;
  }

  // 盾：局部 +Z 为盾面朝外，+Y 沿前臂（朝手）方向；原点在前臂处
  function buildShieldInto(gb, id, lod, colr, w) {
    const K = { gb, lod, w: w == null ? B.foreArmL : w, hi: lod.detail };
    const fc = col(colr || '#8a2a24');
    if (id === 'scutum') {
      const W0 = 0.62, H0 = 1.02, Rc = 0.5;
      panel(gb, (s, t) => { const a = (s - 0.5) * W0 / Rc; return [Math.sin(a) * Rc, (t - 0.5) * H0 + 0.02, Math.cos(a) * Rc - Rc + 0.07]; }, { nu: K.hi >= 1 ? 6 : 3, nv: K.hi >= 1 ? 4 : 2, th: 0.02, reg: 'scutum', col: fc, colB: col('#6a4a30'), w: K.w, uv: (s, t) => [s, t] });
      const c = [0, 0.02, 0.09], AX = axesFromY([0, 0, 1]), r = 0.07, start = gb.nv;
      lathe(gb, [[r, -0.004, null, 0], [r * 0.9, 0.01, null, 0.3], [r * 0.5, 0.03, null, 0.7], [0, 0.036, null, 1]], { c, axes: AX, n: Math.max(6, lod.ring - 4), reg: 'brushed', col: col('#c8ccd2'), w: K.w, capStart: true });
      void start;
      return { r: 0.5 };
    }
    if (id === 'wa' || id === 'pai') {
      const W0 = id === 'wa' ? 0.4 : 0.46, H0 = id === 'wa' ? 0.92 : 0.76;
      panel(gb, (s, t) => [(s - 0.5) * W0, (t - 0.5) * H0, 0.06 - 0.06 * Math.pow(2 * s - 1, 2)], { nu: 3, nv: 2, th: 0.02, reg: id === 'wa' ? 'brocade' : 'wood', col: id === 'wa' ? col('#7a2a1a') : fc, colB: col('#5a3a24'), w: K.w, uv: (s, t) => [s, t] });
      const c = [0, 0.02, 0.085], AX = axesFromY([0, 0, 1]), r = id === 'wa' ? 0.08 : 0.11, start = gb.nv;
      lathe(gb, [[r, -0.006, null, 0], [r, 0.0, null, 0.1], [r * 0.85, 0.012, null, 0.35], [r * 0.3, 0.02, null, 0.8], [0, 0.022, null, 1]], { c, axes: AX, n: Math.max(6, lod.ring - 4), reg: id === 'wa' ? 'mirror' : 'buckle', col: col(GOLD), w: K.w });
      fixDiscUV(gb, REG(id === 'wa' ? 'mirror' : 'buckle'), c, AX, r, start);
      return { r: H0 / 2 };
    }
    // 圆盾 / 椭圆盾 / 藤牌
    const oval = id === 'oval', rat = id === 'rattan';
    const R0 = oval ? 0.26 : rat ? 0.33 : 0.32;
    const c = [0, 0, 0.07], AX = axesFromY([0, 0, 1]), start = gb.nv;
    const reg = rat ? 'rattan' : 'shieldRound';
    lathe(gb, [[R0, -0.02, null, 0], [R0 * 1.02, 0.0, null, 0.04], [R0 * 0.95, 0.012, null, 0.1], [R0 * 0.5, 0.03, null, 0.5], [0, 0.04, null, 1]], { c, axes: AX, n: Math.max(8, lod.ring), reg, col: rat ? col('#c8a060') : fc, w: K.w, sx: 1, sz: oval ? 1.8 : 1, capStart: true });
    if (!rat) fixDiscUV(gb, REG(reg), c, AX, R0, start); else fixDiscUV(gb, REG(reg), c, AX, R0, start);
    if (oval) { for (let i = start; i < gb.nv; i++) { /* 椭圆：UV 已按圆投影，纵向拉伸可接受 */ } }
    return { r: oval ? R0 * 1.8 : R0 };
  }

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
  const HORSE_COATS = [['#7a4a2a', '#1c1614'], ['#8a4a22', '#5a2a14'], ['#2a2422', '#141010'], ['#e4e0d8', '#d8d4cc'], ['#a8a49c', '#3a3634'], ['#b08a5a', '#3a2a1a']];
  const HORSE_NAMED = { '赤兔': ['#a83220', '#4a1410'], '的卢': ['#e8e4dc', '#3a3634'], '绝影': ['#2a2422', '#141010'], '爪黄飞电': ['#e8e0c8', '#c8a050'], '白龙驹': ['#f0eee8', '#e0dcd4'] };

  function buildHorseGeometry(opts) {
    const lod = LODP[opts.lod || 'mid'] || LODP.mid;
    const k = 1.0;
    const J = horseJoints(k);
    const gb = new GB();
    if (lod.detail === 0) gb.noEdges = true;
    const r = Rng(hashStr('horse' + (opts.seed || 0)));
    const named = HORSE_NAMED[opts.name];
    const coatPair = opts.coat ? [opts.coat, opts.mane || shade(opts.coat, -0.6)] : named || HORSE_COATS[(opts.seed || 0) % HORSE_COATS.length];
    const coat = col(coatPair[0]), mane = col(coatPair[1]);
    const fc = TEAM;
    const armor = opts.armor || 'cloth';
    const hi = lod.detail;
    const N = lod.detail >= 2 ? 18 : lod.detail >= 1 ? 10 : 7;
    // 躯干
    const bodyS = [[-0.88, 0, 0, 1.22], [-0.84, 0.15, 0.17, 1.22], [-0.74, 0.27, 0.3, 1.18], [-0.56, 0.32, 0.36, 1.15], [-0.3, 0.31, 0.35, 1.11], [0.0, 0.315, 0.36, 1.08], [0.25, 0.305, 0.37, 1.1], [0.45, 0.285, 0.375, 1.14], [0.58, 0.23, 0.32, 1.18], [0.66, 0.13, 0.18, 1.24], [0.7, 0, 0, 1.26]];
    const bw = p => (p[2] > 0.3 ? [[HB.chest, 1]] : p[2] > 0.1 ? [[HB.chest, (p[2] - 0.1) / 0.2], [HB.body, 1 - (p[2] - 0.1) / 0.2]] : [[HB.body, 1]]);
    const tailW = p => (p[2] < -0.78 ? [[HB.body, 0.6], [HB.tail1, 0.4]] : null);
    loft(gb, bodyS.map(([z, rx, ry, cy], i) => ({ c: [0, cy * k, z * k], X: [-1, 0, 0], Z: [0, 1, 0], rx: rx * k, rz: ry * k, t: i / (bodyS.length - 1), pole: rx === 0, sq: 2.2 })),
      { n: N + 2, reg: 'horse', col: coat, wf: p => tailW(p) || bw(p),
        off: (th, s) => { const z = s.c[2]; return (0.02 * gauss(z, -0.5, 0.15) * gauss(Math.abs(th), 0.9, 0.5) + 0.018 * gauss(z, 0.42, 0.12) * gauss(Math.abs(th), 1.2, 0.5) - 0.015 * gauss(z, 0.0, 0.3) * gauss(Math.abs(th), PI, 0.6)) * k; } });
    // 颈
    const nk = [[0, 1.2, 0.38], [0, 1.4, 0.56], [0, 1.6, 0.72], [0, 1.78, 0.86], [0, 1.88, 0.94]];
    const nr = [[0.17, 0.34], [0.135, 0.32], [0.11, 0.25], [0.09, 0.19], [0.082, 0.15]];
    const nF = frames(nk, [0, 1, 0]);
    const neckW = p => { const t = clamp01((p[1] - 1.3) / 0.55); return wT(t, [[HB.chest, 0], [HB.neck1, 0.25], [HB.neck2, 0.6], [HB.head, 1]]); };
    loft(gb, nk.map((c, i) => { const d = nF.T[i], up = v3norm(v3sub([0, 0.3, -1].map(v => -v), v3mul(d, v3dot([0, -0.3, 1], d)))); const Zs = v3norm(v3sub([0, 1, -0.6], v3mul(d, v3dot([0, 1, -0.6], d)))); return { c: v3mul(c, k), X: v3cross(d, Zs), Z: Zs, rx: nr[i][0] * k, rz: nr[i][1] * 0.55 * k, t: i / 4 }; }),
      { n: N, reg: 'horse', col: coat, wf: neckW });
    // 头：自后脑向前下方
    const hd = [[0, 1.9, 0.9], [0, 1.88, 0.98], [0, 1.8, 1.1], [0, 1.68, 1.24], [0, 1.58, 1.33], [0, 1.53, 1.37]];
    const hr = [[0.08, 0.1], [0.1, 0.13], [0.092, 0.115], [0.072, 0.09], [0.068, 0.082], [0.035, 0.045]];
    const hF = frames(hd, [0, 1, 0]);
    loft(gb, hd.map((c, i) => { const d = hF.T[i], Zs = v3norm(v3sub([0, 1, 0.3], v3mul(d, v3dot([0, 1, 0.3], d)))); return { c: v3mul(c, k), X: v3cross(d, Zs), Z: Zs, rx: hr[i][0] * k, rz: hr[i][1] * k, t: i / 5, pole: i === 5 && false }; }),
      { n: N, reg: 'horse', col: coat, w: HB.head, capEnd: true, capStart: true });
    // 耳、眼、鼻孔
    for (const s of [1, -1]) {
      tube(gb, [[s * 0.05 * k, 1.95 * k, 0.93 * k], [s * 0.065 * k, 2.03 * k, 0.9 * k], [s * 0.07 * k, 2.09 * k, 0.88 * k]], { r: t => (0.022 * (1 - t) + 0.002) * k, rz: 0.55, n: 5, reg: 'horse', col: coat, w: HB.head });
      ellipsoid(gb, [s * 0.088 * k, 1.84 * k, 1.04 * k], [0.012 * k, 0.016 * k, 0.02 * k], { nl: 4, n: 6, reg: 'white', col: col('#140e0c'), w: HB.head });
      ellipsoid(gb, [s * 0.04 * k, 1.56 * k, 1.36 * k], [0.012 * k, 0.014 * k, 0.01 * k], { nl: 3, n: 5, reg: 'white', col: col('#1a1210'), w: HB.head });
    }
    // 鬃
    panel(gb, (s, t) => { const p = v3lerp(nk[1], [0, 1.95, 0.9], t); return [lerp(-0.012, 0.03, s) * k + 0.02 * k * t, (p[1] + 0.13 - 0.06 * t + 0.0 * s) * k - s * 0.1 * k, (p[2] - 0.06 * (1 - t) - 0.02) * k]; },
      { nu: 1, nv: hi >= 1 ? 6 : 3, th: 0.03 * k, reg: 'hair', col: mane, w: p => neckW(p), uv: (s, t) => [s, t] });
    // 腿
    const legs = [['F', 'L'], ['F', 'R'], ['H', 'L'], ['H', 'R']];
    for (const [fh, S] of legs) {
      const up = J['up' + fh + S], lo = J['lo' + fh + S], ft = J['ft' + fh + S], sg = S === 'L' ? 1 : -1;
      const hoof = [ft[0], 0.0, ft[2] + 0.04 * k];
      const top = fh === 'F' ? [up[0] * 0.9, 1.22 * k, up[2] - 0.04 * k] : [up[0] * 0.9, 1.22 * k, up[2] + 0.05 * k];
      let pts = [top, up, v3lerp(up, lo, 0.35), v3lerp(up, lo, 0.75), lo, v3lerp(lo, ft, 0.5), ft, [ft[0], 0.11 * k, ft[2] + 0.025 * k]];
      let rr = fh === 'F' ? [0.15, 0.105, 0.088, 0.066, 0.058, 0.043, 0.054, 0.046] : [0.17, 0.14, 0.105, 0.075, 0.064, 0.045, 0.055, 0.046];
      if (hi === 0) { pts = [pts[0], pts[1], pts[4], pts[6], pts[7]]; rr = [rr[0], rr[1], rr[4], rr[6], rr[7]]; }
      const chain = fh === 'F' ? [[HB.chest, top], [HB['upF' + S], up], [HB['loF' + S], lo], [HB['ftF' + S], ft], [null, hoof]] : [[HB.body, top], [HB['upH' + S], up], [HB['loH' + S], lo], [HB['ftH' + S], ft], [null, hoof]];
      tube(gb, pts, { r: (s, i) => rr[i] * k, rz: 1.1, n: hi === 0 ? 5 : Math.max(6, N - 4), reg: 'horse', col: (s => (s > 0.78 && (opts.seed || 0) % 3 === 1 ? col('#ece8e0') : coat))(0), wf: p => chainW(p, chain, 0.05), up: [0, 0, 1] });
      // 蹄
      lathe(gb, hi === 0 ? [[0.06 * k, 0.0], [0.046 * k, 0.105 * k]] : [[0.056 * k, 0.0], [0.062 * k, 0.006 * k], [0.054 * k, 0.075 * k], [0.044 * k, 0.105 * k]], { c: [hoof[0], 0, hoof[2] - 0.01 * k], n: hi === 0 ? 5 : Math.max(6, N - 4), reg: 'leather', col: col('#262220'), w: HB['ft' + fh + S], capStart: true });
    }
    // 尾
    const tp = [[0, 1.3, -0.82], [0, 1.22, -0.92], [0, 1.0, -0.98], [0, 0.78, -0.98], [0, 0.6, -0.96]].map(p => v3mul(p, k));
    tube(gb, tp, { r: s => (0.045 + 0.05 * Math.sin(s * PI * 0.9)) * k, rz: 0.55, n: Math.max(5, N - 6), reg: 'hair', col: mane, w: s => wT(s, [[HB.body, 0], [HB.tail1, 0.15], [HB.tail2, 0.6]]), capEnd: true });
    // ---- 马具 ----
    const sad = J.saddle;
    // 鞍
    panel(gb, (s, t) => { const a = lerp(-1.25, 1.25, s); const z = lerp(-0.22, 0.2, t) * k; const y = (1.43 + 0.05 * (Math.pow(2 * t - 1, 2))) * k; return [Math.sin(a) * 0.3 * k, y - (1 - Math.cos(a)) * 0.16 * k, z]; },
      { nu: hi >= 1 ? 6 : 3, nv: hi >= 1 ? 4 : 2, th: 0.03, reg: 'leather', col: col('#4a2a1a'), w: HB.body, mirrorS: true });
    if (hi >= 1) for (const zz of [-0.22, 0.2]) tube(gb, (() => { const pts = []; for (let i = 0; i <= 8; i++) { const a = lerp(-1.3, 1.3, i / 8); pts.push([Math.sin(a) * 0.31 * k, (1.47 + (zz > 0 ? 0.05 : 0.04) - (1 - Math.cos(a)) * 0.16) * k, zz * k]); } return pts; })(), { r: 0.014 * k, n: 5, reg: 'gold', col: col(GOLD), w: HB.body });
    // 马衣（鞍下垂到腹下的布）或具装鳞甲
    for (const s of [1, -1]) {
      if (armor === 'none') break;
      const scale = armor === 'scale';
      panel(gb, (u, t) => {
        const z = lerp(scale ? -0.75 : -0.45, scale ? 0.6 : 0.35, u) * k;
        const ring = ringAtHorse(bodyS, z / k);
        const a = lerp(0.35, 1.75, t);
        const yb = ring.cy + Math.cos(a) * ring.ry * 1.06, xb = Math.sin(a) * ring.rx * 1.08 + 0.012;
        return [s * xb * k, yb * k - (t > 0.9 ? (t - 0.9) * 0.3 : 0), z];
      }, { nu: hi >= 1 ? 8 : 3, nv: hi >= 1 ? 4 : 2, th: 0.008, reg: scale ? 'scale' : 'hem', col: scale ? col(opts.metal || '#8a909a') : fc, colB: shade(TEAM, -0.5), uv: (u, t) => [u, 1 - t],
        w: p => bw(p) });
    }
    // 胸带
    if (hi >= 1) tube(gb, (() => { const pts = []; for (let i = 0; i <= 10; i++) { const a = lerp(-1.6, 1.6, i / 10); pts.push([Math.sin(a) * 0.26 * k, (1.15 + Math.cos(a) * 0.08 - 0.05) * k, (0.5 + Math.cos(a) * 0.14) * k]); } return pts; })(), { r: 0.018 * k, rz: 0.4, n: 4, reg: 'leather', col: col('#5a1a14'), w: HB.chest });
    if (hi >= 1) for (const zz of [0.62]) ellipsoid(gb, [0, 1.2 * k, zz * k], [0.035 * k, 0.035 * k, 0.015 * k], { nl: 4, n: 6, reg: 'gold', col: col(GOLD), w: HB.chest });
    // 络头与缰绳
    if (hi >= 1) {
      for (const [a, b] of [[[0, 1.6, 1.28], 0.075], [[0, 1.86, 0.94], 0.1]]) { void b; }
      tube(gb, ringPts([0, 1.62 * k, 1.27 * k], [1, 0, 0], v3norm([0, 0.6, -0.8]), 0.072 * k, 0.09 * k, 10), { r: 0.008 * k, n: 3, reg: 'leather', col: col('#3a1a14'), w: HB.head });
      tube(gb, ringPts([0, 1.88 * k, 0.95 * k], [1, 0, 0], v3norm([0, 0.2, 1]), 0.1 * k, 0.11 * k, 10), { r: 0.008 * k, n: 3, reg: 'leather', col: col('#3a1a14'), w: HB.head });
      for (const s of [1, -1]) tube(gb, [[s * 0.065 * k, 1.56 * k, 1.28 * k], [s * 0.15 * k, 1.42 * k, 0.75 * k], [s * 0.16 * k, 1.5 * k, 0.28 * k]], { r: 0.006 * k, n: 3, reg: 'leather', col: col('#3a1a14'), w: s2 => wT(s2, [[HB.head, 0], [HB.neck2, 0.4], [HB.chest, 1]]) });
    }
    // 马面（护额甲）
    if (opts.chamfron || armor === 'scale' || opts.general) {
      panel(gb, (s, t) => { const d = hd[1].map((v, i) => lerp(v, hd[4][i], t)); const th = lerp(-1.2, 1.2, s); const R = lerp(0.135, 0.095, t); return [Math.sin(th) * R * 0.95 * k, (d[1] + Math.cos(th) * R * 0.62) * k + 0.018, (d[2] + Math.cos(th) * R * 0.5) * k + 0.01]; },
        { nu: hi >= 1 ? 4 : 2, nv: hi >= 1 ? 4 : 2, th: 0.006, reg: 'metal', col: col(opts.metal || '#a8aeb8'), w: HB.head, mirrorS: true });
      if (hi >= 1) tassel({ gb, lod, hi }, [0, 1.98 * k, 0.94 * k], 0.7, opts.plume || '#c8302a', HB.head);
    }
    // 镫
    if (hi >= 1) for (const s of [1, -1]) { tube(gb, [[s * 0.3 * k, 1.36 * k, 0.0], [s * 0.33 * k, 1.0 * k, 0.02 * k]], { r: 0.006 * k, n: 3, reg: 'leather', col: col('#3a1a14'), w: HB.body }); tube(gb, ringPts([s * 0.33 * k, 0.96 * k, 0.03 * k], [0, 0, 1], [0, 1, 0], 0.04 * k, 0.035 * k, 8), { r: 0.006 * k, n: 3, reg: 'gold', col: col(BRONZE), w: HB.body }); }
    return { gb, J, k };
  }
  function ringAtHorse(S, z) {
    for (let i = 0; i < S.length - 1; i++) { const a = S[i], b = S[i + 1]; if (z <= b[0]) { const t = (z - a[0]) / (b[0] - a[0] || 1); return { rx: lerp(a[1], b[1], t), ry: lerp(a[2], b[2], t), cy: lerp(a[3], b[3], t) }; } }
    const l = S[S.length - 1]; return { rx: l[1], ry: l[2], cy: l[3] };
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
  // 构建模板；超出该 LOD 的三角形预算时按 SQUEEZE 逐级压缩重建
  function buildTemplate(look) {
    const t0 = now();
    const base = LODP[look.lod] || LODP.high, budget = TRI_BUDGET[look.lod] || 15000;
    let tpl = null;
    for (let lv = 0; lv <= SQUEEZE[look.lod || 'high'].length; lv++) {
      look._lodP = lv ? Object.assign({}, base, SQUEEZE[look.lod || 'high'][lv - 1]) : base;
      tpl = buildTemplateOnce(look);
      tpl.squeeze = lv;
      if (tpl.tris <= budget) break;
      if (lv < SQUEEZE[look.lod || 'high'].length) { for (const g of [tpl.geometry, tpl.headGeo, tpl.weaponGeo, tpl.shieldGeo]) if (g) g.dispose(); STATS.templates--; }
    }
    tpl.ms = now() - t0; STATS.lastBuildMs = tpl.ms;
    return tpl;
  }
  function buildTemplateOnce(look) {
    const t0 = now();
    const X = makeCtx(look);
    const { gb, pr, lod } = X;
    if (lod.detail === 0) gb.noEdges = true;
    gb.detail = lod.detail;
    const parts = {};
    let last = 0;
    const mark = name => { parts[name] = (parts[name] || 0) + gb.tris - last; last = gb.tris; };
    buildTorso(X); mark('torso'); buildNeck(X); mark('neck');
    for (const S of ['L', 'R']) { buildArm(X, S); mark('arms'); buildLeg(X, S); mark('legs'); buildPauldron(X, S); mark('pauldrons'); }
    buildSkirt(X); mark('skirt'); buildCape(X); mark('cape'); buildRibbons(X); buildBackGear(X); mark('back');
    const Hp = headParams(look.spec || { face: {}, nose: {}, ears: {} });
    Hp.lodF = lod.head[1] >= 24 ? 1 : lod.head[1] >= 14 ? 0.3 : 0;
    const hm = new THREE.Matrix4().makeTranslation(pr.J.head[0], pr.J.head[1], pr.J.head[2]);
    gb.xf = hm;
    const cov = buildHeadgear(X, Hp); mark('headgear');
    buildHair(X, Hp, cov); mark('hair');
    buildBeard(X, Hp); mark('beard');
    const tpl = { look, pr, lod: look.lod, boneInverses: makeBoneInverses(pr), refs: 0, key: look.key, parts };
    const weaponId = look.weapon || 'jian';
    const wdef = WEAPONS[weaponId] || WEAPONS.jian;
    tpl.style = wdef.style;
    if (look.kind === 'soldier') {
      // 头、兵器、盾并入同一蒙皮网格（一次绘制）
      buildHead(gb, Hp, { nLat: lod.head[0], nLon: lod.head[1], reg: look.face || 'faceA', col: col(look.skin), w: B.head, ears: lod.detail >= 1 }); mark('head');
      const side = wdef.style === 'bow' ? 'L' : 'R';
      gb.xf = socketRest(pr, side);
      tpl.weapon = buildWeaponInto(gb, weaponId, lod, { w: B['hand' + side] }); mark('weapon');
      if (look.shield) { gb.xf = shieldRest(pr); tpl.shieldInfo = buildShieldInto(gb, look.shield, lod, look.C.faction, B.foreArmL); tpl.hasShield = true; mark('shield'); }
      gb.xf = null;
    } else {
      gb.xf = null;
      const hgb = new GB();
      buildHead(hgb, Hp, { nLat: lod.head[0], nLon: lod.head[1], col: [1, 1, 1], w: 0, ears: true });
      tpl.headGeo = hgb.build({ skin: false });
      tpl.faceSize = look.lod === 'high' ? 512 : look.lod === 'mid' ? 256 : 128;
      const wgb = new GB();
      wgb.detail = lod.detail;
      tpl.weapon = buildWeaponInto(wgb, weaponId, lod, { w: 0 });
      tpl.weaponGeo = wgb.build({ skin: false });
      if (look.shield) { const sgb = new GB(); tpl.shieldInfo = buildShieldInto(sgb, look.shield, lod, look.C.faction, 0); tpl.shieldGeo = sgb.build({ skin: false }); }
    }
    tpl.geometry = gb.build();
    tpl.tris = gb.tris + (tpl.headGeo ? tpl.headGeo.index.count / 3 : 0) + (tpl.weaponGeo ? tpl.weaponGeo.index.count / 3 * (wdef.style === 'dual' ? 2 : 1) : 0) + (tpl.shieldGeo ? tpl.shieldGeo.index.count / 3 : 0);
    tpl.ms = now() - t0;
    STATS.templates++;
    STATS.buildMs += tpl.ms; STATS.lastBuildMs = tpl.ms;
    const tk = (look.kind === 'soldier' ? look.type + '/' + look.culture : look.name) + '@' + look.lod;
    STATS.tris[tk] = Math.round(tpl.tris);
    if (!STATS.parts) STATS.parts = {};
    STATS.parts[tk] = parts;
    return tpl;
  }
  function getTemplate(look, key) {
    let t = TEMPLATES.get(key);
    if (!t) { look.key = key; t = buildTemplate(look); TEMPLATES.set(key, t); }
    t.refs++;
    return t;
  }
  function releaseTemplate(t) {
    if (!t) return;
    t.refs--;
    // 士兵模板常驻（复用率高）；名将模板在无人使用时释放
    if (t.refs <= 0 && t.look.kind !== 'soldier') {
      TEMPLATES.delete(t.key);
      for (const g of [t.geometry, t.headGeo, t.weaponGeo, t.shieldGeo]) if (g) g.dispose();
    }
  }

  // ======================================================== Figure ==
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
      const mat = teamMaterial(opts.color || tpl.look.factionHex);
      this.mesh = new THREE.SkinnedMesh(tpl.geometry, mat);
      this.mesh.name = 'body';
      this.mesh.add(bones[0]);
      this.mesh.bind(this.skeleton, new THREE.Matrix4());
      // 地面贴合开关（见材质的 skinning_vertex 补丁）：站在地面上时开启，骑乘时关闭；写在骨骼贴图的空余矩阵里
      this.groundClamp = true;
      const fig = this, sk = this.skeleton, skUpdate = sk.update, GI = GROUND_SLOT * 16;
      sk.update = function () {
        skUpdate.call(this);
        const m = this.boneMatrices;
        if (m.length >= GI + 16) { m[GI] = fig.groundClamp && !fig.mounted ? 1 : 0; m[GI + 1] = 0.006; }
      };
      this.mesh.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 1.0 * pr.k, 0), (this.style === 'pole' ? 2.3 : 1.7) * pr.k);
      const shadow = opts.shadow !== false;
      this.mesh.castShadow = shadow; this.mesh.receiveShadow = false;
      this.root.add(this.mesh);
      this.meshes = [this.mesh];
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
      const hs = new THREE.Object3D(); hs.name = 'headTop'; hs.position.set(0, 0.26 * pr.k, 0); this.bones.head.add(hs); this.sockets.headTop = hs;
      // 名将：头部、兵器、盾为独立网格
      this.head = null; this.weaponMesh = null; this.weaponMesh2 = null; this.shieldMesh = null;
      if (tpl.headGeo) {
        this._face = faceTexture(tpl.look.spec || {}, tpl.faceSize);
        this._faceMat = faceMaterial(this._face);
        this.head = new THREE.Mesh(tpl.headGeo, this._faceMat);
        this.head.name = 'head'; this.head.castShadow = shadow;
        this.bones.head.add(this.head);
        this.meshes.push(this.head);
      }
      if (tpl.weaponGeo) {
        const side = this.style === 'bow' ? 'L' : 'R';
        this.weaponMesh = new THREE.Mesh(tpl.weaponGeo, mat);
        this.weaponMesh.name = 'weapon'; this.weaponMesh.castShadow = shadow;
        this.sockets['grip' + side].add(this.weaponMesh);
        this.meshes.push(this.weaponMesh);
        if (this.style === 'dual') {
          this.weaponMesh2 = new THREE.Mesh(tpl.weaponGeo, mat);
          this.weaponMesh2.name = 'weapon2'; this.weaponMesh2.castShadow = shadow;
          this.sockets.gripL.add(this.weaponMesh2);
          this.meshes.push(this.weaponMesh2);
        }
      }
      if (tpl.shieldGeo) {
        this.shieldMesh = new THREE.Mesh(tpl.shieldGeo, mat);
        this.shieldMesh.castShadow = shadow;
        this.sockets.shieldL.add(this.shieldMesh);
        this.meshes.push(this.shieldMesh);
      }
      this.hasShield = !!(tpl.shieldGeo || tpl.hasShield);
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
      return this;
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
      pt(B.head, 0, 0.115, 0.01, 0.112); pt(B.neck, 0, 0.04, 0, 0.06);
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
    setWeapon(name) {
      if (!WEAPONS[name] || this.kind === 'soldier') return this;
      if (this.weaponMesh) { this.weaponMesh.parent.remove(this.weaponMesh); }
      if (this.weaponMesh2) { this.weaponMesh2.parent.remove(this.weaponMesh2); }
      if (this._ownWeaponGeo) this._ownWeaponGeo.dispose();
      const wgb = new GB();
      const lod = this.tpl.look._lodP || LODP[this.tpl.lod] || LODP.high;
      this.weaponInfo = buildWeaponInto(wgb, name, lod, { w: 0 });
      this._ownWeaponGeo = wgb.build({ skin: false });
      this.style = WEAPONS[name].style;
      const mat = this._ownMat || this.mesh.material;
      this.weaponMesh = new THREE.Mesh(this._ownWeaponGeo, mat); this.weaponMesh.castShadow = this.mesh.castShadow;
      this.sockets[this.style === 'bow' ? 'gripL' : 'gripR'].add(this.weaponMesh);
      this.weaponMesh2 = null;
      if (this.style === 'dual') { this.weaponMesh2 = new THREE.Mesh(this._ownWeaponGeo, mat); this.sockets.gripL.add(this.weaponMesh2); }
      this.meshes = [this.mesh, this.head, this.weaponMesh, this.weaponMesh2, this.shieldMesh].filter(Boolean);
      this.drawCalls = this.meshes.length;
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
    // 受击闪光：每个人物用独立的材质副本（共用着色器程序）
    setTint(hex, k) {
      if (!this._ownMat) {
        this._ownMat = ownCopy(this.mesh.material);
        this.mesh.material = this._ownMat;
        for (const m of [this.weaponMesh, this.weaponMesh2, this.shieldMesh]) if (m) m.material = this._ownMat;
      }
      const c = col(hex || '#ffffff');
      this._ownMat.emissive.setRGB(c[0] * (k || 0), c[1] * (k || 0), c[2] * (k || 0));
      if (this._faceMat) this._faceMat.emissive.copy(this._ownMat.emissive);
      return this;
    }
    dispose() {
      if (this._disposed) return;
      this._disposed = true;
      if (this.root.parent) this.root.parent.remove(this.root);
      if (this.mounted) this.mounted.dismount(this);
      this.skeleton.dispose();
      if (this._faceMat) this._faceMat.dispose();
      if (this._face) releaseFace(this._face);
      if (this._ownMat) this._ownMat.dispose();
      if (this._ownWeaponGeo) this._ownWeaponGeo.dispose();
      releaseTemplate(this.tpl);
    }
  }
  function ownCopy(base) {
    const m = base.clone();
    m.userData = {};
    patchMaterial(m, 'kit');
    if (base.userData && base.userData.team && base.userData.team.value && base.userData.team.value.isColor) m.userData.team.value.copy(base.userData.team.value);
    return m;
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
      const lod = opts.lod || 'mid';
      const key = [lod, opts.coat, opts.armor, opts.seed | 0, opts.name, !!opts.general, opts.metal, opts.plume].join('|');
      let t = HTPL.get(key);
      if (!t) {
        const t0 = now();
        const r = buildHorseGeometry(Object.assign({}, opts, { lod }));
        t = { key, geometry: r.gb.build(), J: r.J, k: r.k, refs: 0, tris: r.gb.tris, inv: HBONES.map(n => new THREE.Matrix4().makeTranslation(-r.J[n][0], -r.J[n][1], -r.J[n][2])) };
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
      this.mesh = new THREE.SkinnedMesh(t.geometry, teamMaterial(opts.color || '#8a2a24'));
      this.mesh.add(bones[0]); this.mesh.bind(this.skeleton, new THREE.Matrix4());
      this.mesh.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 1.1, 0), 1.9);
      this.mesh.castShadow = opts.shadow !== false;
      this.root.add(this.mesh);
      this.meshes = [this.mesh];
      this.sockets = { saddle: this.bones.saddle };
      this.riders = [];
      this.triangles = t.tris; this.drawCalls = 1;
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
    setTint(hex, k) {
      if (!this._ownMat) { this._ownMat = ownCopy(this.mesh.material); this.mesh.material = this._ownMat; }
      const c = col(hex || '#ffffff'); this._ownMat.emissive.setRGB(c[0] * (k || 0), c[1] * (k || 0), c[2] * (k || 0));
      return this;
    }
    dispose() {
      if (this._disposed) return;
      this._disposed = true;
      for (const f of this.riders.slice()) this.dismount(f);
      if (this.root.parent) this.root.parent.remove(this.root);
      this.skeleton.dispose();
      if (this._ownMat) this._ownMat.dispose();
      const t = this.tpl;
      if (--t.refs <= 0) { HTPL.delete(t.key); t.geometry.dispose(); }
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
      set('neck1', -4 + s * 2); set('neck2', 6 + s * 2); set('head', 14 - s * 2); set('tail1', 20, Math.sin(ph * 2) * 10); set('tail2', 15, Math.sin(ph * 2 + 1) * 12);
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
    const h = new Horse(Object.assign({}, hopts, { shadow: false }));
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

  // 实例化材质：装具材质 + 烘焙蒙皮 + 实例势力色
  const CROWD_VS_PARS = [
    'uniform sampler2D uSgBake;', 'uniform vec2 uSgSize;', 'uniform float uSgTime;', 'uniform float uSgFps;', 'uniform float uSgGround;',
    'attribute vec4 skinIndex;', 'attribute vec4 skinWeight;', 'attribute vec4 aSgAnim;', 'attribute vec3 aSgTeam;', 'varying vec3 vSgTeam;',
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
    '  return sgBone( v, skinIndex.x ) * skinWeight.x + sgBone( v, skinIndex.y ) * skinWeight.y + sgBone( v, skinIndex.z ) * skinWeight.z + sgBone( v, skinIndex.w ) * skinWeight.w;',
    '}'].join('\n');
  function crowdVertex(src) {
    return src.replace('#include <common>', '#include <common>\n' + CROWD_VS_PARS)
      .replace('#include <skinbase_vertex>', 'mat4 sgSkin = sgSkinMat();\nvSgTeam = aSgTeam;')
      .replace('#include <skinnormal_vertex>', 'objectNormal = ( sgSkin * vec4( objectNormal, 0.0 ) ).xyz;')
      .replace('#include <skinning_vertex>', 'transformed = ( sgSkin * vec4( transformed, 1.0 ) ).xyz;\nif ( uSgGround > 0.5 ) transformed.y = max( transformed.y, 0.006 );');
  }
  function crowdUniforms(bake, ground) {
    return { uSgBake: { value: bake.tex }, uSgSize: { value: new THREE.Vector2(bake.W, bake.rows) }, uSgTime: { value: 0 }, uSgFps: { value: BAKE_FPS }, uSgGround: { value: ground ? 1 : 0 } };
  }
  function crowdMaterial(U) {
    const base = kitMaterial();
    const m = base.clone();
    m.userData = {};
    m.name = 'SG.Figure.crowd';
    const RIMu = RIM;
    m.onBeforeCompile = function (sh) {
      Object.assign(sh.uniforms, U, { uRimColor: RIMu.color, uRimK: RIMu.k });
      sh.vertexShader = crowdVertex(sh.vertexShader);
      sh.fragmentShader = 'uniform vec3 uRimColor;\nuniform float uRimK;\nvarying vec3 vSgTeam;\n' + sh.fragmentShader
        .replace('#include <map_fragment>', ['#ifdef USE_MAP', '  vec4 sgTex = texture2D( map, vMapUv );', '  diffuseColor.rgb *= sgTex.rgb;', '#endif'].join('\n'))
        .replace('#include <color_fragment>', [
          '#if defined( USE_COLOR_ALPHA )', '  vec3 sgVC = mix( vColor.rgb, vColor.rgb * vSgTeam, vColor.a );',
          '#elif defined( USE_COLOR )', '  vec3 sgVC = vColor.rgb;', '#endif',
          '#if defined( USE_COLOR ) || defined( USE_COLOR_ALPHA )', '  #ifdef USE_MAP', '  diffuseColor.rgb *= mix( vec3( 1.0 ), sgVC, sgTex.a );', '  #else', '  diffuseColor.rgb *= sgVC;', '  #endif', '#endif'].join('\n'))
        .replace('#include <emissivemap_fragment>', ['#include <emissivemap_fragment>',
          '  { float sgR = 1.0 - clamp( dot( normal, normalize( vViewPosition ) ), 0.0, 1.0 );',
          '    totalEmissiveRadiance += uRimColor * ( sgR * sgR * sgR * uRimK ) * ( 0.3 + 0.7 * diffuseColor.rgb ); }'].join('\n'));
    };
    m.customProgramCacheKey = () => 'sgcrowd';
    return m;
  }
  function crowdDepthMaterial(U) {
    const m = new THREE.MeshDepthMaterial({ depthPacking: THREE.RGBADepthPacking });
    m.onBeforeCompile = function (sh) { Object.assign(sh.uniforms, U); sh.vertexShader = crowdVertex(sh.vertexShader).replace('vSgTeam = aSgTeam;', ''); sh.vertexShader = sh.vertexShader.replace('varying vec3 vSgTeam;', ''); };
    m.customProgramCacheKey = () => 'sgcrowd-depth';
    return m;
  }

  // 一组（同一几何体 + 同一烘焙）
  class CrowdGroup {
    constructor(crowd, geo, bake, cap, ground, name) {
      this.crowd = crowd; this.bake = bake; this.cap = cap; this.count = 0; this.ids = [];
      const g = new THREE.BufferGeometry();
      for (const k of ['position', 'normal', 'uv', 'color', 'skinIndex', 'skinWeight']) if (geo.attributes[k]) g.setAttribute(k, geo.attributes[k]);
      g.setIndex(geo.index);
      g.boundingSphere = new THREE.Sphere(new THREE.Vector3(0, 1, 0), 2.5);
      this.anim = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4); this.anim.setUsage(THREE.DynamicDrawUsage);
      this.team = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3); this.team.setUsage(THREE.DynamicDrawUsage);
      g.setAttribute('aSgAnim', this.anim); g.setAttribute('aSgTeam', this.team);
      this.geo = g;
      this.U = crowdUniforms(bake, ground);
      this.U.uSgTime = crowd._time;     // 所有组共用同一时钟
      this.mat = crowdMaterial(this.U);
      this.mesh = new THREE.InstancedMesh(g, this.mat, cap);
      this.mesh.count = 0; this.mesh.frustumCulled = false; this.mesh.name = 'Crowd:' + name;
      this.mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      this.mesh.castShadow = crowd.shadow; this.mesh.receiveShadow = false;
      if (crowd.shadow) this.mesh.customDepthMaterial = crowdDepthMaterial(this.U);
      crowd.root.add(this.mesh);
      this.tris = geo.index.count / 3;
    }
    grow() {
      const cap = this.cap * 2;
      const a = new THREE.InstancedBufferAttribute(new Float32Array(cap * 4), 4); a.array.set(this.anim.array); a.setUsage(THREE.DynamicDrawUsage);
      const t = new THREE.InstancedBufferAttribute(new Float32Array(cap * 3), 3); t.array.set(this.team.array); t.setUsage(THREE.DynamicDrawUsage);
      this.geo.setAttribute('aSgAnim', a); this.geo.setAttribute('aSgTeam', t); this.anim = a; this.team = t;
      const old = this.mesh;
      const m = new THREE.InstancedMesh(this.geo, this.mat, cap);
      m.instanceMatrix.array.set(old.instanceMatrix.array); m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      m.count = this.count; m.frustumCulled = false; m.name = old.name; m.castShadow = old.castShadow; m.customDepthMaterial = old.customDepthMaterial;
      this.crowd.root.remove(old); old.dispose && old.dispose(); this.crowd.root.add(m);
      this.mesh = m; this.cap = cap;
    }
    alloc(id) { if (this.count >= this.cap) this.grow(); const i = this.count++; this.ids[i] = id; this.mesh.count = this.count; return i; }
    free(i) {
      const last = --this.count;
      if (i !== last) {
        const M = this.mesh.instanceMatrix.array; M.copyWithin(i * 16, last * 16, last * 16 + 16);
        this.anim.array.copyWithin(i * 4, last * 4, last * 4 + 4); this.team.array.copyWithin(i * 3, last * 3, last * 3 + 3);
        const mv = this.ids[last]; this.ids[i] = mv; this.crowd._moved(mv, this, i);
      }
      this.ids.length = this.count; this.mesh.count = this.count;
      this.mesh.instanceMatrix.needsUpdate = true; this.anim.needsUpdate = true; this.team.needsUpdate = true;
    }
    dispose() { this.crowd.root.remove(this.mesh); this.geo.dispose(); this.mat.dispose(); if (this.mesh.customDepthMaterial) this.mesh.customDepthMaterial.dispose(); }
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
      const key = ['S', look.type, look.culture, look.lod, look.seed].join('|');
      const it = { id, mounted: !!o.mounted, color: col(o.color || '#8a2a24'), parts: [], clip: 'idle', t0: 0, speed: 1, m: _cm() };
      if (!this.instanced) return this._addFallback(it, o, id);
      const tpl = getTemplate(look, key);
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
      if (!t) { const h = new Horse(Object.assign({}, ho, { shadow: false })); t = h.tpl; t.refs++; h.dispose(); this._htpls.set(hkey, t); }
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
      it.color = col(hexc);
      if (it.fig) { it.fig.mesh.material = teamMaterial(hexc); return this; }
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
    _addFallback(it, o, id) {
      it.fig = soldier(Object.assign({}, o, { lod: this.lod, type: o.mounted ? 'cavalry' : o.type, shadow: this.shadow }));
      this.root.add(it.fig.root);
      if (o.mounted) { it.horse = horse(Object.assign({ lod: 'low', color: o.color, seed: (o.seed | 0) % 2 }, o.horse || {})); this.root.add(it.horse.root); it.horse.mount(it.fig); }
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
  // 预烘焙（生成器，按片段分片）：士兵规格 { type, culture, seed, mounted, horse }
  function* bakeCrowdGen(o, lod) {
    lod = lod || 'low';
    const look = lookOfSoldier(Object.assign({}, o, { lod, type: o.mounted ? 'cavalry' : o.type }));
    const key = ['S', look.type, look.culture, look.lod, look.seed].join('|');
    const tpl = getTemplate(look, key);
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
  function generalKey(look, opts) {
    const sp = look.spec || {};
    return ['G', look.name, look.culture, look.lod, look.weapon, look.shield || '', sp.key || '', opts.mounted ? 'm' : ''].join('|');
  }
  function general(gen, opts) {
    opts = opts || {};
    const look = lookOfGeneral(gen, opts);
    const tpl = getTemplate(look, generalKey(look, opts));
    const fig = new Figure(tpl, Object.assign({ color: look.factionHex }, opts));
    return fig;
  }
  function soldier(opts) {
    opts = opts || {};
    const look = lookOfSoldier(opts);
    const key = ['S', look.type, look.culture, look.lod, look.seed].join('|');
    const tpl = getTemplate(look, key);
    return new Figure(tpl, Object.assign({ color: look.factionHex }, opts));
  }
  function horse(opts) { return new Horse(opts || {}); }
  function weapon(name, opts) {
    opts = opts || {};
    const lod = LODP[opts.lod || 'high'] || LODP.high;
    const gb = new GB();
    const info = buildWeaponInto(gb, WEAPONS[name] ? name : 'jian', lod, { w: 0 });
    const g = gb.build({ skin: false });
    const m = new THREE.Mesh(g, teamMaterial(opts.color));
    m.name = 'Weapon:' + info.id;
    m.castShadow = opts.shadow !== false;
    m.userData.weapon = { id: info.id, name: info.name, style: info.style, len: info.len, tip: new THREE.Vector3(...info.tip), base: new THREE.Vector3(...info.base), off: info.off ? new THREE.Vector3(...info.off) : null };
    m.userData.tris = gb.tris;
    m.userData.dispose = () => g.dispose();
    return m;
  }
  function shield(name, opts) {
    opts = opts || {};
    const lod = LODP[opts.lod || 'high'] || LODP.high;
    const gb = new GB();
    buildShieldInto(gb, name || 'round', lod, TEAM, 0);
    const g = gb.build({ skin: false });
    const m = new THREE.Mesh(g, teamMaterial(opts.color));
    m.userData.tris = gb.tris;
    return m;
  }

  // 预生成：图集、环境贴图、士兵模板（分片，单片约 12ms）
  let preloading = null;
  function preload(opts) {
    if (preloading && !opts) return preloading;
    opts = opts || {};
    const cultures = opts.cultures || CULTURES;
    const types = opts.types || SOLDIER_TYPES;
    const lods = opts.lods || ['low'];
    const seeds = opts.seeds || [0];
    const budget = opts.budget || 12;
    const t0 = now();
    const jobs = [];
    if (!ATLAS) jobs.push({ gen: genAtlas(), name: 'atlas' });
    jobs.push({ fn: () => { envMap(); kitMaterial(); headTextures(); } });
    // 有渲染器时提前上传贴图（避免首次绘制时卡顿）
    const rr = opts.renderer || (SG.Gfx && SG.Gfx.renderer);
    if (rr && rr.initTexture) jobs.push({ fn: () => { const A = atlas(); for (const t of [A.map, A.normalMap, A.ormMap]) rr.initTexture(t); } });
    for (const lod of lods) for (const c of cultures) for (const ty of types) for (const sd of seeds) jobs.push({ name: 'soldier ' + c + '/' + ty, fn: () => { const f = soldier({ type: ty, culture: c, lod, seed: sd }); f.dispose(); } });
    if (opts.generals) for (const g of opts.generals) jobs.push({ fn: () => { /* 面部贴图与模板（随后由调用方使用） */ const f = general(g, { lod: opts.generalLod || 'mid' }); f.dispose(); } });
    // 士兵群的动作烘焙：opts.crowd = [{ type, culture, seed, mounted }]（或 true：所选文化 × 兵种，seed 0，骑兵带马）
    if (opts.crowd) {
      const specs = opts.crowd === true ? [].concat(...cultures.map(c => types.map(ty => ({ type: ty, culture: c, seed: 0, mounted: ty === 'cavalry' })))) : opts.crowd;
      for (const o of specs) jobs.push({ gen: bakeCrowdGen(o, opts.crowdLod || 'low'), name: 'crowd ' + o.culture + '/' + o.type + (o.mounted ? '+horse' : '') });
    }
    preloading = runJobs(jobs, budget).then(() => { STATS.preloadMs = Math.round(now() - t0); return STATS; });
    return preloading;
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
    let tris = 0;
    return Object.assign({}, STATS, { templateCount: TEMPLATES.size, horseTemplates: HTPL.size, faces: FACE_CACHE.size, materials: TEAM_MATS.size, totalTris: tris });
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
    for (const t of TEMPLATES.values()) if (t.refs <= 0) { for (const g of [t.geometry, t.headGeo, t.weaponGeo, t.shieldGeo]) if (g) g.dispose(); TEMPLATES.delete(t.key); }
  }
  const clipsOut = {};
  for (const k in CL) clipsOut[k] = { dur: CL[k].dur, loop: CL[k].loop, desc: CL[k].desc };

  SG.Figure = {
    general, soldier, horse, weapon, shield, preload, stats, toAnimationClip, clearCache, crowd,
    crowdClips: CROWD_CLIPS.slice(), crowdMountClips: Object.keys(MOUNT_CLIPS),
    clips: clipsOut, horseClips: HCLIPS,
    cultures: CULTURES.slice(), weapons: Object.keys(WEAPONS).map(id => ({ id, name: WEAPONS[id].name, style: WEAPONS[id].style })),
    soldierTypes: SOLDIER_TYPES.slice(), boneNames: BONES.slice(), horseBoneNames: HBONES.slice(),
    get material() { return kitMaterial(); },
    teamMaterial, setRim,
    lookOf(gen, opts) { return lookOfGeneral(gen, opts || {}); },
    soldierLook(opts) { return lookOfSoldier(opts || {}); },
    atlas() { return atlas(); },
    _internal: { genAtlas, bakeCrowdGen, GB, loft, tube, lathe, panel, ellipsoid, blade, headPoint, headUV, paintFaceInto, faceDesc, makeProps, LODP, STANCE, CL, compiled, TEMPLATES },
  };
})();
