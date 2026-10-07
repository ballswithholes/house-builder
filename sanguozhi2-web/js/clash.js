'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 攻击画面（SG.Clash）
   原作中部队「攻击」时切换到横向交战画面：两军士兵对冲、兵力数字递减。
   这里用独立的低多边形三维场景现代化重制（SG.Gfx.pushScreen 全屏场景栈）。

   公开接口
     await SG.Clash.play({
       attacker: { gen, side, color, troopsBefore, troopsAfter, formation: '鱼鳞', culture },
       defender: { gen, side, color, troopsBefore, troopsAfter, formation: '方圆', culture },
       terrain,           // SG.Terrain 值（防守方所在格）：平原 / 森林 / 山丘 / 山岳 / 河川 / 城墙 / 城门 / 本城
       special: null,     // 可选 { name, color, kind, cry }：冲锋前先播 cutIn 特写，接敌时附带特效与额外伤亡
                          //   kind：'blaze' 火海 / 'storm' 雷击 / 'volley' 箭雨，其余一律为斩击弧光 + 冲击波
       playerSide: 0 | 1 | null,
       speed: 1,          // 可选：本次额外的时间倍率（与 SG.Clash.speed 相乘），如电脑回合传 1.6
     })                   → Promise<{ skipped, disabled, seconds }>（seconds：画面实际播放时长）
       · gen 至少含 name；color 为势力色 '#rrggbb'；formation 可为阵型名或 SG.Defs.formations 下标；
         culture 缺省取 gen.culture，再缺省 'han'（见 DESIGN-V2 §6）：士兵头饰 / 甲衣 / 盾 / 腿、武将头盔 / 披风按文化区分（CULT 表）。
       · 两军势力色相近时自动拉开色相 / 明暗（separateColors），并各配一种识别色（pickAccents：肩甲、腰带、盾缘、盔缨、旗边）。
       · special 可直接传 specials-data 的条目（kind / fx 映射到火、雷、箭雨，其余为斩击）。
       · 攻方永远在左。兵力数字从 troopsBefore 滚动到 troopsAfter；troopsAfter 为 0 视为溃散。
     await SG.Clash.cutIn({ gen, name, color, side, cry, speed })
                          必杀技 / 单挑用的全屏特写（1.1 秒 ÷（SG.Clash.speed × speed），最短 0.7 秒；轻点 / 空格 / 回车可跳过）：
                          头像滑入 + 招式名大字；cry 为可选台词（显示在招式名下方），speed 为可选额外倍率。enabled 为 false 时立即返回。
     SG.Clash.enabled     布尔，默认 true；false 时 play / cutIn 立即返回
     SG.Clash.speed       时间倍率，1 = 正常（全长约 3.7 秒），2 = 快
     SG.Clash.mode        'on' | 'fast' | 'off'（读写；写入时同步 enabled / speed，并存入 localStorage）
     SG.Clash.cycleMode() → 新的 mode（设置按钮「动画：开 / 快 / 关」用）
     SG.Clash.modeLabel() → '开' | '快' | '关'
     SG.Clash.fromBattle(model, a, t, beforeA, beforeT, extra) → play() 的参数
       （BattleController.doAttack 用：a 攻击 t，before* 为 Mdl.attack 之前的兵力）
     SG.Clash.active      正在播放的场景对象或 null（测试用）
     SG.Clash.debug       { freezeAt: null, holdCut: false }：freezeAt 设为秒数时动画停在该时刻；
                          holdCut 为 true 时特写不自动结束（截图用，配合 Web Animations API 定格）

   操作：轻点 / 点击 / 空格 / 回车 跳过（直接显示结果约 0.4 秒后退出）。
   画面期间 <html> 加 sg-clash-on 类：隐藏战场 HUD（.sg-screens 的其他子元素）、世界标签与提示层；其余按键不传给相机。
   多次调用 play 会排队依次播放。
   全部网格、贴图、粒子在退出时释放；士兵用 InstancedMesh 绘制。粒子 / 旗面 / 水面 / 特效材质跨场次常驻
   （只为保住着色器程序缓存，避免每次播放重新编译），renderer.info.memory 的几何体与贴图数在多次播放后不增长。
   装饰性随机一律使用 SG.SeededRandom（按双方姓名与兵力取种子），不影响规则随机数。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  // ------------------------------------------------------------ 时间轴 --
  // 单位：秒（speed = 1）。接敌后有一小段慢镜头（slowW 秒内以 slowK 倍速推进）。
  const TL = {
    march1: 0.62,        // 入场结束
    aim: 0.34,           // 弓手举弓
    volley: [0.46, 0.6], // 左 / 右方放箭
    flight: 0.6,         // 箭的飞行时间
    charge0: 1.06,       // 冲锋开始
    contact: 1.66,       // 接敌
    slowW: 0.16, slowK: 0.42,
    melee1: 2.56,        // 混战结束
    regroup1: 3.08,      // 收队完成（结果画面）
    end: 3.34,           // 自然结束（之后淡出）
  };
  const FADE_IN = 0.2, FADE_OUT = 0.2, SKIP_HOLD = 0.4;
  const ENTER_DX = 2.8;      // 入场时从队形位置后方多远走进来
  const ZC = -0.55;          // 两军纵深中心（three z）
  const FRONT = 2.05;        // 列阵时前排离中线的距离
  const SP_F = 0.54, SP_L = 0.62;
  const HIP = 0.36;
  const GRIP = { x: 0.2, y: 0.5, z: -0.2 };   // three 局部坐标：右手握点
  const FOG = [0.86, 0.84, 0.78];
  const SIDE_COLOR = ['#73bfff', '#ff806b'];
  const FORM_NAMES = ['方圆', '长蛇', '鱼鳞', '鹤翼', '偃月', '锋矢', '雁行', '衡轭'];
  const KIND_NAME = { plain: '平原', forest: '森林', hill: '山丘', mountain: '山岳', river: '河川', wall: '城墙', gate: '城门', castle: '本城' };
  const KAI = '"STKaiti","Kaiti SC","KaiTi","BiauKai","Songti SC","Noto Serif SC",serif,"WenQuanYi Zen Hei"';

  // ------------------------------------------------------------ 小工具 --
  const clamp = M.clamp, clamp01 = M.clamp01;
  function V(x, y, z) { return new THREE.Vector3(x, y, z); }
  function C(r, g, b) { return new THREE.Color(r, g, b); }
  // three 世界坐标 → MeshBuilder 所用的 Unity 坐标（z 取反）
  function P(x, y, z) { return new THREE.Vector3(x, y, -z); }
  function shade(c, k) { return SG.Gfx.shade(c, k); }
  function seg(t, a, b) { return b <= a ? (t >= a ? 1 : 0) : clamp01((t - a) / (b - a)); }
  function sm(t) { return t * t * (3 - 2 * t); }
  function eo(t) { const u = 1 - t; return 1 - u * u * u; }
  function eio(t) { return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; }
  function sstep(e0, e1, x) { return sm(clamp01((x - e0) / (e1 - e0))); }
  // [a, b] 内为 1、两端各有 r 秒渐变的梯形窗
  function trap(t, a, b, r) { return Math.max(0, Math.min(seg(t, a - r * 0.5, a + r * 0.5), 1 - seg(t, b - r * 0.5, b + r * 0.5))); }
  function lerp(a, b, t) { return a + (b - a) * t; }
  function hashStr(s) {
    let h = 2166136261;
    s = String(s);
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
    return h | 0;
  }
  function sfx(name, vol) { try { if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频 */ } }
  function now() { return performance.now() / 1000; }
  function esc(s) { return SG.esc ? SG.esc(s) : String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }
  function hexOf(c) {
    if (typeof c === 'string' && /^#[0-9a-f]{6}$/i.test(c)) return c;
    try { return '#' + SG.Gfx.color(c || '#808080').getHexString(); } catch (e) { return '#808080'; }
  }
  function luma(c) { return 0.3 * c.r + 0.59 * c.g + 0.11 * c.b; }
  function el(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  function vecCross(a, b) { return { x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x }; }
  function vecNorm(a) { const l = Math.hypot(a.x, a.y, a.z) || 1; return { x: a.x / l, y: a.y / l, z: a.z / l }; }

  function terrainKind(t) {
    if (typeof t === 'string') return KIND_NAME[t] ? t : 'plain';
    const T = SG.Terrain || { Plain: 0, Forest: 1, Hill: 2, Mountain: 3, River: 4, Wall: 5, Gate: 6, Castle: 7 };
    switch (t) {
      case T.Forest: return 'forest';
      case T.Hill: return 'hill';
      case T.Mountain: return 'mountain';
      case T.River: return 'river';
      case T.Wall: return 'wall';
      case T.Gate: return 'gate';
      case T.Castle: return 'castle';
      default: return 'plain';
    }
  }
  function formIndex(f) {
    if (typeof f === 'number' && isFinite(f)) return clamp(f | 0, 0, 7);
    const s = String(f || '').replace(/[之阵陣]/g, '');
    const i = FORM_NAMES.indexOf(s);
    return i >= 0 ? i : 0;
  }
  function formName(f) {
    const i = formIndex(f);
    const d = SG.Defs && SG.Defs.formations && SG.Defs.formations[i];
    return (d && d.name) || FORM_NAMES[i];
  }
  function surname(gen) { const n = String((gen && gen.name) || '兵'); return n.charAt(0); }

  // ======================================================= 网格辅助 --
  // 任意方向的方柱（Unity 坐标）：a → b，截面宽 w0 → w1
  function quadOut(mb, a, b, c, d, col, ref) {
    const ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
    const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    const mx = (a.x + c.x) / 2 - ref.x, my = (a.y + c.y) / 2 - ref.y, mz = (a.z + c.z) / 2 - ref.z;
    if (nx * mx + ny * my + nz * mz >= 0) mb.quad(a, b, c, d, col); else mb.quad(a, d, c, b, col);
  }
  function beam(mb, a, b, w0, w1, col) {
    if (w1 === undefined || w1 === null) w1 = w0;
    const d = vecNorm({ x: b.x - a.x, y: b.y - a.y, z: b.z - a.z });
    const ref = Math.abs(d.y) < 0.9 ? { x: 0, y: 1, z: 0 } : { x: 1, y: 0, z: 0 };
    const u = vecNorm(vecCross(d, ref)), v = vecCross(d, u);
    const cs = [[-1, -1], [1, -1], [1, 1], [-1, 1]];
    const A = cs.map(([p, q]) => V(a.x + (u.x * p + v.x * q) * w0 / 2, a.y + (u.y * p + v.y * q) * w0 / 2, a.z + (u.z * p + v.z * q) * w0 / 2));
    const B = cs.map(([p, q]) => V(b.x + (u.x * p + v.x * q) * w1 / 2, b.y + (u.y * p + v.y * q) * w1 / 2, b.z + (u.z * p + v.z * q) * w1 / 2));
    const mid = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2, z: (a.z + b.z) / 2 };
    for (let i = 0; i < 4; i++) {
      const j = (i + 1) % 4;
      if (w1 > 1e-5) quadOut(mb, A[i], A[j], B[j], B[i], shade(col, i % 2 ? -0.07 : 0.03), mid);
      else {
        const n = vecCross({ x: A[j].x - A[i].x, y: A[j].y - A[i].y, z: A[j].z - A[i].z }, { x: b.x - A[i].x, y: b.y - A[i].y, z: b.z - A[i].z });
        const m = { x: (A[i].x + A[j].x + b.x) / 3 - mid.x, y: (A[i].y + A[j].y + b.y) / 3 - mid.y, z: (A[i].z + A[j].z + b.z) / 3 - mid.z };
        if (n.x * m.x + n.y * m.y + n.z * m.z >= 0) mb.tri(A[i], A[j], b, shade(col, i % 2 ? -0.07 : 0.03));
        else mb.tri(A[i], b, A[j], shade(col, i % 2 ? -0.07 : 0.03));
      }
    }
    quadOut(mb, A[0], A[1], A[2], A[3], shade(col, -0.1), mid);
    if (w1 > 1e-5) quadOut(mb, B[0], B[1], B[2], B[3], shade(col, 0.06), mid);
  }
  // 沿 x 轴的扁圆盘（圆盾）：Unity 坐标，中心 c，半径 r（ry 为竖向半径，缺省同 r，椭圆盾用），厚 t
  function discX(mb, c, r, t, col, seg, ry) {
    seg = seg || 8;
    ry = ry || r;
    const ref = { x: c.x, y: c.y, z: c.z };
    for (let i = 0; i < seg; i++) {
      const a0 = i / seg * Math.PI * 2, a1 = (i + 1) / seg * Math.PI * 2;
      const p0 = [Math.cos(a0) * r, Math.sin(a0) * ry], p1 = [Math.cos(a1) * r, Math.sin(a1) * ry];
      const o0 = V(c.x - t / 2, c.y + p0[1], c.z + p0[0]), o1 = V(c.x - t / 2, c.y + p1[1], c.z + p1[0]);
      const i0 = V(c.x + t / 2, c.y + p0[1], c.z + p0[0]), i1 = V(c.x + t / 2, c.y + p1[1], c.z + p1[0]);
      quadOut(mb, o0, o1, i1, i0, shade(col, -0.2), ref);
      const ca = V(c.x - t / 2, c.y, c.z), cb = V(c.x + t / 2, c.y, c.z);
      // 两个盘面（外侧略亮）
      const tri = (p, q, s, k) => {
        const n = vecCross({ x: q.x - p.x, y: q.y - p.y, z: q.z - p.z }, { x: s.x - p.x, y: s.y - p.y, z: s.z - p.z });
        const m = { x: p.x - ref.x, y: 0, z: 0 };
        if (n.x * m.x >= 0) mb.tri(p, q, s, k); else mb.tri(p, s, q, k);
      };
      tri(ca, o0, o1, shade(col, (i % 2) * 0.05));
      tri(cb, i0, i1, shade(col, -0.1));
    }
  }

  // ======================================================= 士兵模型 --
  // 全部以 Unity 坐标建模、面朝 +z（three 中朝 −z）。部件分开以便逐个摆动：
  // 腿（髋部为原点）、身体（含头盔、盾）、兵器（右手握点为原点）。
  // 文化装束（DESIGN-V2 §6）：全部是参数化的方块 / 圆柱拼件，每方只建一次几何体（InstancedMesh 共用）。
  //   hat 士兵头饰 / ghat 武将头盔 / shield 盾形 / armor 甲衣样式 / legs 腿（裤、裸腿、长靴、绑腿）
  //   pants 裤色、hair 发色、beard 胡须、trim 默认镶边色、cloak 武将披风色（缺省为势力色暗部）
  const HAIR_D = [0.12, 0.1, 0.09];
  const CULT = {
    han: { skin: [0.95, 0.79, 0.62], hat: 'cone', ghat: 'g_han', shield: 'rect', armor: 'lamellar', legs: 'trousers' },
    nanman: { skin: [0.7, 0.5, 0.34], hat: 'bun', ghat: 'g_feather', shield: 'rattan', armor: 'rattan', legs: 'bare', trim: [0.92, 0.88, 0.76] },
    wa: { skin: [0.93, 0.77, 0.6], hat: 'mizura', ghat: 'g_wa', shield: 'wood', armor: 'tanko', legs: 'wraps', pants: [0.86, 0.82, 0.72] },
    yi: { skin: [0.68, 0.48, 0.32], hat: 'feather', ghat: 'g_feather', shield: 'round', armor: 'bare', legs: 'bare', trim: [0.95, 0.93, 0.86], tattoo: true },
    korea: { skin: [0.95, 0.8, 0.64], hat: 'plume', ghat: 'g_plume', shield: 'rect', armor: 'lamellar', legs: 'trousers', pants: [0.84, 0.8, 0.7] },
    steppe: { skin: [0.86, 0.68, 0.5], hat: 'fur', ghat: 'g_fur', shield: 'small', armor: 'furcoat', legs: 'boots', pants: [0.42, 0.32, 0.22] },
    seasia: { skin: [0.62, 0.43, 0.28], hat: 'seband', ghat: 'g_crown', shield: 'oval', armor: 'bare', legs: 'bare' },
    tarim: { skin: [0.92, 0.76, 0.6], hat: 'tall', ghat: 'tall', shield: 'round', armor: 'robe', legs: 'boots', pants: [0.5, 0.36, 0.26], trim: [0.85, 0.78, 0.6] },
    kushan: { skin: [0.84, 0.64, 0.48], hat: 'kushan', ghat: 'g_kushan', shield: 'round', armor: 'robe', legs: 'boots', pants: [0.3, 0.24, 0.2] },
    persia: { skin: [0.84, 0.64, 0.48], hat: 'phrygian', ghat: 'g_tiara', shield: 'spara', armor: 'scale', legs: 'boots', pants: [0.46, 0.2, 0.18], beard: true },
    arab: { skin: [0.78, 0.58, 0.42], hat: 'kufiya', ghat: 'kufiya', shield: 'round', armor: 'robe', legs: 'bare', beard: true, trim: [0.93, 0.9, 0.82] },
    roman: { skin: [0.95, 0.8, 0.66], hat: 'crest', ghat: 'g_roman', shield: 'scutum', armor: 'segmentata', legs: 'bare', cloak: [0.72, 0.12, 0.1] },
    celt: { skin: [0.97, 0.84, 0.72], hat: 'limed', ghat: 'limed', shield: 'oval', armor: 'plaid', legs: 'trousers', pants: [0.4, 0.42, 0.3], hair: [0.62, 0.3, 0.12], beard: true },
    german: { skin: [0.97, 0.84, 0.72], hat: 'knot', ghat: 'knot', shield: 'hex', armor: 'furcloak', legs: 'wraps', pants: [0.36, 0.3, 0.22], hair: [0.8, 0.64, 0.34], beard: true },
    sarmatian: { skin: [0.9, 0.74, 0.58], hat: 'spike', ghat: 'g_spike', shield: 'small', armor: 'scale', legs: 'boots', pants: [0.3, 0.26, 0.22] },
  };
  function cultOf(c) { return CULT[c] || CULT.han; }
  const IRON = C(0.42, 0.43, 0.47), IRON_D = C(0.3, 0.31, 0.35), WOOD = C(0.43, 0.3, 0.17), RED = C(0.8, 0.16, 0.12), GOLD = C(0.88, 0.7, 0.28);
  const BRONZE = C(0.7, 0.5, 0.26), FUR = C(0.46, 0.33, 0.2), TAN = C(0.74, 0.62, 0.4), WHITE = C(0.95, 0.94, 0.89);
  function rgbC(a, dflt) { return a ? C(a[0], a[1], a[2]) : dflt; }

  // 头饰：y0 = 头顶，z0 = 头部中心 z，s = 尺寸倍数；acc = 识别色（近色对阵时的缨 / 羽 / 带，可为 null）
  function headgear(mb, hat, team, y0, s, z0, acc, cu) {
    z0 = z0 || 0;
    const at = (x, y, z) => V(x * s, y0 + y * s, z0 + z * s);
    const sz = (x, y, z) => V(x * s, y * s, z * s);
    const hair = rgbC(cu && cu.hair, rgbC(HAIR_D));
    const tassel = acc || RED;
    const cap = () => mb.box(at(0, 0.0, -0.005), sz(0.17, 0.035, 0.165), hair);
    switch (hat) {
      case 'bun':      // 南中：椎髻 + 骨簪 + 红头带
        mb.box(at(0, -0.03, 0), sz(0.17, 0.03, 0.16), acc || RED);
        mb.box(at(0, 0.03, -0.02), sz(0.08, 0.07, 0.08), hair);
        mb.box(at(0, 0.05, -0.02), sz(0.17, 0.018, 0.018), C(0.94, 0.9, 0.78));
        break;
      case 'mizura':   // 倭：美豆良（左右耳侧的发环）
        mb.box(at(0, 0.005, 0), sz(0.17, 0.035, 0.16), hair);
        mb.box(at(-0.1, -0.08, 0), sz(0.04, 0.09, 0.05), hair);
        mb.box(at(0.1, -0.08, 0), sz(0.04, 0.09, 0.05), hair);
        mb.box(at(0, -0.025, 0), sz(0.175, 0.02, 0.165), acc || WHITE);
        break;
      case 'band':
        mb.box(at(0, -0.03, 0), sz(0.17, 0.035, 0.16), acc || shade(team, 0.2));
        mb.box(at(0, 0.005, 0), sz(0.155, 0.03, 0.15), hair);
        break;
      case 'feather':  // 夷洲：头带 + 竖羽
        cap();
        mb.box(at(0, -0.03, 0), sz(0.175, 0.035, 0.165), acc || shade(team, 0.15));
        beam(mb, at(-0.04, -0.01, -0.06), at(-0.07, 0.17, -0.09), 0.035 * s, 0.012 * s, WHITE);
        beam(mb, at(0.0, -0.01, -0.07), at(0.0, 0.2, -0.1), 0.035 * s, 0.012 * s, tassel);
        beam(mb, at(0.04, -0.01, -0.06), at(0.07, 0.17, -0.09), 0.035 * s, 0.012 * s, WHITE);
        break;
      case 'seband':   // 林邑 / 扶南：金箍 + 顶髻
        cap();
        mb.box(at(0, -0.03, 0), sz(0.178, 0.03, 0.168), GOLD);
        mb.cone(at(0, 0.0, -0.01), 0.05 * s, 0.09 * s, 5, hair);
        mb.box(at(0, 0.05, -0.01), sz(0.03, 0.03, 0.03), GOLD);
        break;
      case 'plume':    // 高句丽 / 三韩：铁盔 + 白羽
        mb.cone(at(0, -0.035, 0), 0.115 * s, 0.12 * s, 6, IRON);
        mb.box(at(0, 0.13, -0.01), sz(0.025, 0.14, 0.05), acc || C(0.95, 0.95, 0.9));
        mb.box(at(-0.09, -0.1, 0.02), sz(0.02, 0.08, 0.06), IRON_D);
        mb.box(at(0.09, -0.1, 0.02), sz(0.02, 0.08, 0.06), IRON_D);
        break;
      case 'fur':      // 草原：毡帽 + 毛皮檐
        mb.cylinder(at(0, -0.05, 0), 0.115 * s, 0.1 * s, 0.09 * s, 6, FUR);
        mb.cone(at(0, 0.035, 0), 0.08 * s, 0.07 * s, 6, shade(team, -0.1));
        if (acc) mb.box(at(0, 0.1, 0), sz(0.03, 0.04, 0.03), acc);
        mb.box(at(0, -0.12, -0.08), sz(0.06, 0.12, 0.03), hair);    // 辫
        break;
      case 'tall':     // 西域：白毡尖帽 + 帽檐
        mb.cylinder(at(0, -0.04, 0), 0.105 * s, 0.1 * s, 0.03 * s, 6, C(0.82, 0.76, 0.62));
        mb.cone(at(0, -0.01, 0), 0.095 * s, 0.24 * s, 6, C(0.86, 0.8, 0.66));
        mb.box(at(0, -0.035, 0), sz(0.2, 0.012, 0.19), acc || shade(team, 0.1));
        break;
      case 'kushan':   // 贵霜：深色高尖帽 + 金箍
        mb.cylinder(at(0, -0.045, 0), 0.11 * s, 0.11 * s, 0.035 * s, 6, acc || GOLD);
        mb.cone(at(0, -0.01, -0.01), 0.1 * s, 0.27 * s, 6, shade(team, -0.42));
        break;
      case 'phrygian': // 安息：弗里吉亚软帽（帽尖前倾）+ 护耳垂片
        mb.cylinder(at(0, -0.05, 0), 0.112 * s, 0.105 * s, 0.07 * s, 6, shade(team, -0.3));
        beam(mb, at(0, 0.015, -0.01), at(0, 0.12, 0.07), 0.16 * s, 0.03 * s, shade(team, -0.22));
        mb.box(at(-0.088, -0.12, 0.01), sz(0.02, 0.1, 0.07), shade(team, -0.3));
        mb.box(at(0.088, -0.12, 0.01), sz(0.02, 0.1, 0.07), shade(team, -0.3));
        if (acc) mb.box(at(0, -0.03, 0.002), sz(0.228, 0.02, 0.215), acc);
        break;
      case 'kufiya':   // 阿拉伯：头巾垂肩 + 深色头箍
        mb.box(at(0, -0.01, 0), sz(0.185, 0.05, 0.175), WHITE);
        mb.box(at(0, -0.12, -0.085), sz(0.19, 0.2, 0.025), C(0.9, 0.88, 0.82));
        mb.box(at(-0.092, -0.1, -0.02), sz(0.02, 0.16, 0.12), C(0.9, 0.88, 0.82));
        mb.box(at(0.092, -0.1, -0.02), sz(0.02, 0.16, 0.12), C(0.9, 0.88, 0.82));
        mb.cylinder(at(0, 0.01, 0), 0.1 * s, 0.1 * s, 0.022 * s, 6, acc || C(0.15, 0.12, 0.1));
        break;
      case 'spike':    // 萨尔马提亚：尖顶铁盔 + 锁子护颈
        mb.cone(at(0, -0.04, 0), 0.112 * s, 0.2 * s, 6, IRON);
        mb.box(at(0, -0.1, -0.075), sz(0.16, 0.09, 0.025), IRON_D);
        if (acc) mb.box(at(0, 0.165, 0), sz(0.03, 0.05, 0.03), acc);
        break;
      case 'crest':    // 罗马：高卢式铁盔 + 纵冠 + 护颊 + 宽护颈
        mb.cone(at(0, -0.035, 0), 0.115 * s, 0.11 * s, 6, IRON);
        mb.box(at(0, 0.09, 0), sz(0.03, 0.06, 0.2), tassel);
        mb.box(at(-0.085, -0.09, 0.03), sz(0.02, 0.08, 0.06), IRON_D);
        mb.box(at(0.085, -0.09, 0.03), sz(0.02, 0.08, 0.06), IRON_D);
        mb.box(at(0, -0.075, -0.085), sz(0.2, 0.025, 0.06), IRON_D);
        break;
      case 'limed':    // 喀里多尼亚：石灰竖发 + 长发
        cap();
        for (let i = -1; i <= 1; i++) mb.cone(at(i * 0.05, 0.0, -0.01 - Math.abs(i) * 0.02), 0.035 * s, 0.09 * s, 4, shade(hair, 0.25));
        mb.box(at(0, -0.1, -0.075), sz(0.17, 0.17, 0.035), hair);
        break;
      case 'knot':     // 日耳曼：苏维汇发髻（右侧）+ 长发
        cap();
        mb.box(at(0, -0.1, -0.075), sz(0.17, 0.18, 0.04), hair);
        mb.box(at(0.075, 0.0, -0.01), sz(0.06, 0.06, 0.07), shade(hair, -0.1));
        if (acc) mb.box(at(0, -0.035, 0), sz(0.178, 0.018, 0.168), acc);
        break;
      case 'hair':
        mb.box(at(0, 0.0, -0.01), sz(0.17, 0.04, 0.17), hair);
        mb.box(at(0, -0.1, -0.075), sz(0.17, 0.18, 0.04), hair);
        break;
      case 'turban':
        mb.cylinder(at(0, -0.05, 0), 0.105 * s, 0.11 * s, 0.08 * s, 6, C(0.92, 0.9, 0.84));
        mb.cone(at(0, 0.03, 0), 0.07 * s, 0.05 * s, 6, C(0.9, 0.88, 0.8));
        break;
      // ---- 武将专用 ----
      case 'g_han':    // 汉：金盔 + 雉尾
        mb.cylinder(at(0, -0.04, 0), 0.125, 0.125, 0.035, 6, shade(GOLD, -0.2));
        mb.cone(at(0, -0.01, 0), 0.12, 0.14, 6, GOLD);
        beam(mb, at(0, 0.12, -0.015), at(0, 0.36, -0.215), 0.035, 0.012, tassel);
        beam(mb, at(0.03, 0.12, -0.015), at(0.06, 0.32, -0.235), 0.03, 0.01, shade(tassel, 0.2));
        break;
      case 'g_roman':  // 罗马：铁盔 + 横向红冠（百夫长式）+ 护颊
        mb.cone(at(0, -0.035, 0), 0.122, 0.12, 6, IRON);
        mb.box(at(0, 0.095, 0), V(0.3, 0.075, 0.035), tassel);
        mb.box(at(0, 0.055, 0), V(0.04, 0.03, 0.04), GOLD);
        mb.box(at(-0.09, -0.11, 0.045), V(0.02, 0.09, 0.07), IRON_D);
        mb.box(at(0.09, -0.11, 0.045), V(0.02, 0.09, 0.07), IRON_D);
        mb.box(at(0, -0.1, -0.075), V(0.17, 0.06, 0.03), IRON_D);
        break;
      case 'g_spike':  // 萨尔马提亚：鎏金尖顶分片盔 + 锁子护颈 + 两条飘带
        mb.cylinder(at(0, -0.04, 0), 0.125, 0.125, 0.03, 6, shade(GOLD, -0.25));
        mb.cone(at(0, -0.02, 0), 0.118, 0.24, 6, GOLD);
        for (let i = -1; i <= 1; i += 2) mb.box(at(i * 0.055, 0.04, 0), V(0.012, 0.11, 0.012), shade(GOLD, -0.35));
        mb.box(at(0, -0.13, -0.085), V(0.19, 0.12, 0.03), IRON_D);
        beam(mb, at(-0.04, -0.04, -0.105), at(-0.07, -0.24, -0.315), 0.03, 0.015, WHITE);
        beam(mb, at(0.04, -0.04, -0.105), at(0.07, -0.21, -0.295), 0.03, 0.015, tassel);
        break;
      case 'g_plume':  // 高句丽 / 三韩：鎏金盔 + 高耸白羽
        mb.cylinder(at(0, -0.04, 0), 0.125, 0.125, 0.035, 6, shade(GOLD, -0.2));
        mb.cone(at(0, -0.01, 0), 0.115, 0.13, 6, GOLD);
        beam(mb, at(0, 0.1, 0), at(0, 0.4, -0.03), 0.05, 0.02, acc || C(0.96, 0.95, 0.9));
        beam(mb, at(0.035, 0.1, 0), at(0.07, 0.34, -0.05), 0.035, 0.012, C(0.92, 0.9, 0.84));
        beam(mb, at(-0.035, 0.1, 0), at(-0.07, 0.34, -0.05), 0.035, 0.012, C(0.92, 0.9, 0.84));
        break;
      case 'g_tiara':  // 安息：高圆提亚拉冠（势力色暗部）+ 珠串金箍 + 背后飘带 + 披发
        mb.cylinder(at(0, -0.045, 0), 0.118, 0.112, 0.17, 6, shade(team, -0.38));
        mb.cylinder(at(0, 0.125, 0), 0.112, 0.06, 0.06, 6, shade(team, -0.3));
        mb.cylinder(at(0, -0.045, 0), 0.124, 0.124, 0.035, 6, GOLD);
        mb.cylinder(at(0, 0.09, 0), 0.116, 0.116, 0.022, 6, GOLD);
        for (let i = -2; i <= 2; i++) mb.box(at(i * 0.035, 0.035, 0.11), V(0.018, 0.018, 0.014), WHITE);
        mb.box(at(0, -0.14, -0.08), V(0.18, 0.2, 0.05), hair);
        beam(mb, at(-0.04, -0.03, -0.11), at(-0.08, -0.25, -0.3), 0.03, 0.015, tassel);
        beam(mb, at(0.04, -0.03, -0.11), at(0.08, -0.22, -0.3), 0.03, 0.015, GOLD);
        break;
      case 'g_wa':     // 倭：冲角付胄（前凸的铁盔）+ 宽护颈 + 立饰
        mb.cylinder(at(0, -0.045, 0), 0.128, 0.122, 0.05, 6, IRON_D);
        mb.cone(at(0, 0.0, 0), 0.12, 0.12, 6, IRON);
        beam(mb, at(0, 0.02, 0.02), at(0, -0.02, 0.15), 0.08, 0.02, IRON);
        mb.box(at(0, -0.1, -0.1), V(0.27, 0.035, 0.11), IRON_D);
        mb.box(at(0, -0.14, -0.12), V(0.3, 0.035, 0.11), shade(IRON_D, -0.1));
        mb.box(at(0, 0.13, 0), V(0.02, 0.07, 0.06), acc || GOLD);
        break;
      case 'g_fur':    // 草原：狐皮帽 + 护耳 + 势力色毡顶 + 双辫
        mb.cylinder(at(0, -0.06, 0), 0.13, 0.12, 0.1, 6, FUR);
        mb.cone(at(0, 0.04, 0), 0.1, 0.15, 6, shade(team, -0.05));
        mb.box(at(-0.105, -0.13, 0.0), V(0.03, 0.12, 0.08), shade(FUR, 0.1));
        mb.box(at(0.105, -0.13, 0.0), V(0.03, 0.12, 0.08), shade(FUR, 0.1));
        mb.box(at(0, 0.2, 0), V(0.03, 0.05, 0.03), acc || GOLD);
        mb.box(at(-0.05, -0.18, -0.09), V(0.04, 0.16, 0.03), hair);
        mb.box(at(0.05, -0.18, -0.09), V(0.04, 0.16, 0.03), hair);
        break;
      case 'g_feather':// 南中 / 夷洲首领：金箍 + 扇形羽冠
        mb.box(at(0, -0.03, 0), V(0.18, 0.04, 0.17), GOLD);
        mb.box(at(0, 0.0, -0.005), V(0.17, 0.035, 0.165), hair);
        for (let i = -2; i <= 2; i++) beam(mb, at(i * 0.03, 0.0, -0.06), at(i * 0.09, 0.26 - Math.abs(i) * 0.04, -0.1), 0.045, 0.015, i % 2 ? WHITE : (i ? tassel : shade(team, 0.25)));
        break;
      case 'g_crown':  // 林邑 / 扶南：多层金尖冠
        mb.cylinder(at(0, -0.04, 0), 0.115, 0.11, 0.06, 6, GOLD);
        mb.cylinder(at(0, 0.02, 0), 0.095, 0.075, 0.08, 6, shade(GOLD, 0.08));
        mb.cylinder(at(0, 0.1, 0), 0.065, 0.045, 0.07, 6, GOLD);
        mb.cone(at(0, 0.17, 0), 0.04, 0.14, 5, shade(GOLD, 0.12));
        if (acc) mb.box(at(0, -0.01, 0.105), V(0.04, 0.04, 0.02), acc);
        break;
      case 'g_kushan': // 贵霜王冠：高尖帽 + 金冠带 + 飘带
        mb.cylinder(at(0, -0.045, 0), 0.12, 0.12, 0.045, 6, GOLD);
        mb.cone(at(0, 0.0, -0.01), 0.108, 0.3, 6, shade(team, -0.42));
        beam(mb, at(-0.04, -0.03, -0.11), at(-0.08, -0.23, -0.3), 0.03, 0.015, acc || GOLD);
        beam(mb, at(0.04, -0.03, -0.11), at(0.08, -0.2, -0.3), 0.03, 0.015, acc || GOLD);
        break;
      default:         // 'cone' 汉军铁胄：盔体、帽檐、红缨、顿项
        mb.cylinder(at(0, -0.045, 0), 0.118 * s, 0.118 * s, 0.03 * s, 6, IRON_D);
        mb.cone(at(0, -0.02, 0), 0.112 * s, 0.13 * s, 6, IRON);
        mb.box(at(0, 0.12, 0), sz(0.035, 0.05, 0.035), tassel);
        mb.box(at(0, -0.1, -0.07), sz(0.17, 0.1, 0.03), IRON_D);
        break;
    }
  }

  // 胡须（面朝 +z）：hc = 头部中心，hs = 头部尺寸
  function beardOf(mb, cu, hc, hs) {
    if (!cu.beard) return;
    const hair = rgbC(cu.hair, rgbC(HAIR_D));
    mb.box(V(hc.x, hc.y - hs * 0.36, hc.z + hs * 0.46), V(hs * 0.82, hs * 0.36, hs * 0.16), hair);
    mb.box(V(hc.x, hc.y - hs * 0.16, hc.z + hs * 0.52), V(hs * 0.5, hs * 0.08, hs * 0.06), hair);
  }

  // 甲衣样式：F = 躯干 { y, z, w, h, d }（Unity 坐标，面朝 +z）；trim = 镶边 / 识别色
  function torsoDeco(mb, cu, F, t, trim) {
    const { y, z, w, h, d } = F;
    const fz = z + d / 2 + 0.006, bz = z - d / 2 - 0.006;
    const light = shade(t, 0.16);
    const both = (cx, cy, sw, sh, col, dz) => { dz = dz || 0; mb.box(V(cx, cy, fz + dz), V(sw, sh, 0.02), col); mb.box(V(cx, cy, bz - dz), V(sw, sh, 0.02), shade(col, -0.08)); };
    const OV = fz + 0.014;     // 贴在胸甲外的细节
    switch (cu.armor) {
      case 'scale':        // 鱼鳞甲：前后三排错色甲片
        for (let r = 0; r < 3; r++) {
          const yy = y + h * (0.26 - r * 0.21), ww = w * (0.8 - r * 0.04);
          both(0, yy, ww, h * 0.2, r % 2 ? IRON : BRONZE);
          for (let i = -1; i <= 1; i += 2) mb.box(V(i * ww * 0.25, yy - h * 0.09, OV), V(0.02, 0.02, 0.012), shade(r % 2 ? IRON : BRONZE, -0.3));
        }
        break;
      case 'segmentata':   // 环片甲：铁条绕身（缝隙露出势力色）
        for (let r = 0; r < 3; r++) mb.box(V(0, y + h * (0.32 - r * 0.25), z), V(w + 0.016, h * 0.15, d + 0.016), r % 2 ? IRON_D : IRON);
        mb.box(V(0, y + h * 0.45, z), V(w * 0.55, h * 0.1, d + 0.02), IRON_D);
        break;
      case 'tanko':        // 短甲：铁胸板 + 势力色横带 + 勾玉项链
        both(0, y + h * 0.08, w * 0.8, h * 0.62, IRON);
        both(0, y + h * 0.08, w * 0.82, h * 0.1, light, 0.008);
        for (let i = -2; i <= 2; i++) mb.box(V(i * 0.026, y + h * 0.4 - Math.abs(i) * 0.01, OV), V(0.018, 0.026, 0.014), C(0.2, 0.62, 0.45));
        break;
      case 'robe':         // 长袍：前襟镶边
        both(0, y, w * 0.12, h * 0.95, trim);
        break;
      case 'furcoat':      // 皮袍：毛皮领与前襟
        mb.box(V(0, y + h * 0.43, z), V(w * 0.78, h * 0.16, d + 0.02), FUR);
        both(0, y - h * 0.05, w * 0.14, h * 0.8, FUR);
        break;
      case 'rattan':       // 藤甲：棕黄编织胸甲（纵横两色）
        both(0, y + h * 0.05, w * 0.82, h * 0.7, TAN);
        for (let i = -1; i <= 1; i++) mb.box(V(i * w * 0.24, y + h * 0.05, OV), V(0.014, h * 0.66, 0.012), shade(TAN, -0.25));
        mb.box(V(0, y + h * 0.05, OV), V(w * 0.8, 0.014, 0.012), shade(TAN, -0.25));
        break;
      case 'bare':         // 短衣：金饰 / 贝珠项链
        mb.box(V(0, y + h * 0.42, z + d * 0.2), V(w * 0.6, 0.03, d * 0.75), cu.tattoo ? WHITE : GOLD);
        if (!cu.tattoo) mb.box(V(0, y + h * 0.3, fz), V(0.05, 0.05, 0.015), GOLD);
        break;
      case 'plaid':        // 方格衣 + 金项圈（torc）
        for (let r = 0; r < 2; r++) for (let i = -1; i <= 1; i++) if ((r + i) % 2 === 0) both(i * w * 0.28, y + h * (0.2 - r * 0.36), w * 0.24, h * 0.3, shade(t, -0.32));
        mb.box(V(0, y + h * 0.52, z + d * 0.15), V(w * 0.5, 0.03, d * 0.7), GOLD);
        break;
      case 'furcloak':     // 毛皮披肩（肩与背）
        mb.box(V(0, y + h * 0.4, z - d * 0.1), V(w + 0.05, h * 0.22, d + 0.02), FUR);
        mb.box(V(0, y + h * 0.05, bz - 0.01), V(w * 0.9, h * 0.7, 0.03), shade(FUR, -0.08));
        break;
      default:             // 'lamellar' 札甲：胸甲 + 两道甲片横缝
        both(0, y + h * 0.08, w * 0.76, h * 0.62, light);
        for (let r = 0; r < 2; r++) mb.box(V(0, y + h * (0.16 - r * 0.2), OV), V(w * 0.74, 0.012, 0.012), shade(t, -0.12));
        break;
    }
  }

  // 盾（左臂外侧，盾面法线沿 x）：c = 盾心，trim = 镶边 / 识别色（null 时用文化默认）
  function shieldOf(mb, kind, c, t, trim) {
    const face = shade(t, 0.06), dark = shade(t, -0.4);
    const rim = trim || dark, boss = trim || GOLD;
    const out = (dx) => V(c.x - dx, c.y, c.z);       // 向外（-x）偏移
    switch (kind) {
      case 'round':
        discX(mb, V(c.x + 0.006, c.y, c.z), 0.185, 0.03, rim);
        discX(mb, c, 0.17, 0.035, face);
        mb.box(out(0.025), V(0.02, 0.06, 0.06), boss);
        break;
      case 'small':        // 小圆皮盾：皮色盾面 + 势力色盾心
        discX(mb, c, 0.135, 0.03, C(0.4, 0.28, 0.16));
        discX(mb, out(0.012), 0.09, 0.012, face);
        mb.box(out(0.026), V(0.02, 0.045, 0.045), boss);
        break;
      case 'rattan':       // 藤牌：棕黄编织 + 势力色内圈
        discX(mb, c, 0.18, 0.035, TAN);
        discX(mb, out(0.012), 0.1, 0.012, face);
        mb.box(out(0.028), V(0.02, 0.05, 0.05), trim || C(0.92, 0.88, 0.76));
        break;
      case 'oval':         // 长椭圆盾（凯尔特 / 南海）：竖脊 + 盾心
        discX(mb, V(c.x + 0.006, c.y, c.z), 0.14, 0.03, rim, 10, 0.25);
        discX(mb, c, 0.125, 0.035, face, 10, 0.235);
        mb.box(out(0.022), V(0.015, 0.4, 0.03), boss);
        mb.box(out(0.028), V(0.02, 0.07, 0.07), boss);
        break;
      case 'hex':          // 日耳曼六角盾
        discX(mb, V(c.x + 0.006, c.y, c.z), 0.2, 0.03, rim, 6);
        discX(mb, c, 0.18, 0.035, face, 6);
        mb.box(out(0.026), V(0.02, 0.065, 0.065), boss);
        break;
      case 'scutum':       // 罗马长方大盾：势力色盾面 + 金色边框、竖脊、盾心
        mb.box(c, V(0.04, 0.46, 0.3), face);
        mb.box(out(0.022), V(0.008, 0.46, 0.022), boss);
        mb.box(V(c.x - 0.022, c.y + 0.22, c.z), V(0.008, 0.022, 0.3), trim || GOLD);
        mb.box(V(c.x - 0.022, c.y - 0.22, c.z), V(0.008, 0.022, 0.3), trim || GOLD);
        mb.box(V(c.x - 0.022, c.y, c.z + 0.14), V(0.008, 0.46, 0.02), trim || GOLD);
        mb.box(V(c.x - 0.022, c.y, c.z - 0.14), V(0.008, 0.46, 0.02), trim || GOLD);
        mb.box(out(0.03), V(0.02, 0.08, 0.08), boss);
        break;
      case 'spara':        // 安息藤编立盾：浅黄编条 + 势力色横纹
        mb.box(c, V(0.03, 0.46, 0.24), C(0.78, 0.68, 0.46));
        for (let i = -1; i <= 1; i++) mb.box(V(c.x - 0.018, c.y + i * 0.13, c.z), V(0.008, 0.06, 0.24), face);
        mb.box(V(c.x - 0.018, c.y, c.z + 0.115), V(0.01, 0.46, 0.02), rim);
        mb.box(V(c.x - 0.018, c.y, c.z - 0.115), V(0.01, 0.46, 0.02), rim);
        break;
      case 'wood':         // 倭：木质立盾 + 势力色锯齿纹
        mb.box(c, V(0.035, 0.44, 0.2), shade(WOOD, 0.12));
        for (let i = -1; i <= 1; i++) mb.box(V(c.x - 0.02, c.y + i * 0.13, c.z + (i % 2 ? 0.04 : -0.04)), V(0.008, 0.07, 0.11), face);
        mb.box(V(c.x - 0.02, c.y + 0.205, c.z), V(0.01, 0.03, 0.2), rim);
        break;
      default:             // 'rect' 汉：长方盾
        mb.box(c, V(0.04, 0.4, 0.25), face);
        mb.box(out(0.025), V(0.02, 0.3, 0.08), dark);
        mb.box(out(0.03), V(0.02, 0.07, 0.07), boss);
        mb.box(V(c.x, c.y + 0.205, c.z), V(0.045, 0.02, 0.25), rim);
        mb.box(V(c.x, c.y - 0.205, c.z), V(0.045, 0.02, 0.25), rim);
        break;
    }
  }

  // 士兵身体：acc = 识别色（两军势力色相近时给出，用在肩、腰带、盾缘、盔缨上；否则 null）
  function bodyGeo(team, culture, archer, acc) {
    const mb = new SG.MeshBuilder();
    const cu = cultOf(culture);
    const t = team, mid = shade(t, -0.18), dark = shade(t, -0.4);
    const skin = C(cu.skin[0], cu.skin[1], cu.skin[2]);
    const leather = C(0.32, 0.22, 0.13);
    const trim = acc || rgbC(cu.trim, null);
    const armBare = cu.armor === 'bare' || cu.armor === 'plaid' || cu.armor === 'rattan';
    const arm = armBare ? skin : mid;
    const robe = cu.armor === 'robe' || cu.armor === 'furcoat';
    if (robe) {
      const hem = cu.armor === 'robe' ? 0.2 : 0.27;
      mb.box(V(0, (hem + 0.45) / 2, 0), V(0.31, 0.45 - hem, 0.205), mid);                         // 袍摆
      mb.box(V(0, hem + 0.015, 0), V(0.33, 0.03, 0.215), cu.armor === 'furcoat' ? FUR : (trim || dark));
    } else {
      mb.box(V(0, 0.385, 0), V(0.31, 0.13, 0.2), mid);               // 甲裙
      mb.box(V(0, 0.335, 0.0), V(0.33, 0.04, 0.215), trim || dark);   // 甲裙下缘
    }
    mb.box(V(0, 0.565, 0), V(0.29, 0.26, 0.18), t);                   // 躯干
    torsoDeco(mb, cu, { y: 0.565, z: 0, w: 0.29, h: 0.26, d: 0.18 }, t, trim || GOLD);
    mb.box(V(0, 0.455, 0), V(0.3, 0.045, 0.19), acc || leather);      // 腰带
    mb.box(V(0, 0.455, 0.1), V(0.06, 0.05, 0.02), GOLD);              // 带扣
    if (!armBare && cu.armor !== 'furcloak') {
      const sh = acc || (cu.armor === 'segmentata' ? IRON : dark);
      mb.box(V(-0.185, 0.655, 0), V(0.1, 0.075, 0.18), sh);            // 肩甲
      mb.box(V(0.185, 0.655, 0), V(0.1, 0.075, 0.18), sh);
    } else if (acc) {
      mb.box(V(-0.185, 0.665, 0), V(0.09, 0.04, 0.17), acc);
      mb.box(V(0.185, 0.665, 0), V(0.09, 0.04, 0.17), acc);
    }
    mb.box(V(-0.2, 0.53, 0.03), V(0.07, 0.19, 0.08), arm);            // 左臂
    mb.box(V(0.2, 0.575, 0.04), V(0.07, 0.13, 0.08), arm);            // 右上臂
    mb.box(V(0.2, 0.505, 0.12), V(0.065, 0.065, 0.16), arm);          // 右前臂（前伸到握点）
    if (cu.tattoo) for (const sx of [-0.2, 0.2]) mb.box(V(sx, 0.56, 0.072), V(0.074, 0.018, 0.012), C(0.18, 0.22, 0.3));   // 文身
    mb.box(V(0, 0.71, 0), V(0.08, 0.04, 0.08), skin);                 // 颈
    mb.box(V(0, 0.795, 0.005), V(0.155, 0.145, 0.15), skin);          // 头
    mb.box(V(0, 0.79, 0.081), V(0.11, 0.025, 0.01), shade(skin, -0.45));   // 眉眼的阴影
    beardOf(mb, cu, { x: 0, y: 0.795, z: 0.005 }, 0.15);
    headgear(mb, cu.hat, t, 0.87, 1, 0, acc, cu);
    if (archer) {
      // 箭囊（背后）与箭羽
      mb.box(V(0.06, 0.6, -0.13), V(0.09, 0.3, 0.08), leather);
      for (let i = 0; i < 3; i++) mb.box(V(0.04 + i * 0.022, 0.78, -0.13), V(0.016, 0.07, 0.016), C(0.92, 0.9, 0.85));
    } else shieldOf(mb, cu.shield, V(-0.265, 0.51, 0.07), t, acc);
    return mb.toGeometry();
  }
  // 腿（髋部为原点）：trousers 裤 + 绑腿 + 靴 / bare 裸腿 + 凉鞋 / boots 裤 + 高靴 / wraps 浅色裤 + 交叉绑带
  function legGeo(culture) {
    const mb = new SG.MeshBuilder();
    const cu = cultOf(culture);
    const skin = C(cu.skin[0], cu.skin[1], cu.skin[2]);
    const pants = rgbC(cu.pants, C(0.27, 0.22, 0.19));
    switch (cu.legs) {
      case 'bare':
        mb.box(V(0, -0.12, 0), V(0.095, 0.24, 0.1), skin);
        mb.box(V(0, -0.25, 0), V(0.1, 0.03, 0.105), C(0.4, 0.27, 0.15));
        mb.box(V(0, -0.33, 0.02), V(0.11, 0.06, 0.15), C(0.36, 0.24, 0.13));
        break;
      case 'boots':
        mb.box(V(0, -0.1, 0), V(0.11, 0.2, 0.12), pants);
        mb.box(V(0, -0.27, 0.01), V(0.12, 0.18, 0.135), C(0.2, 0.14, 0.1));
        mb.box(V(0, -0.345, 0.035), V(0.12, 0.04, 0.17), C(0.15, 0.11, 0.09));
        break;
      case 'wraps':
        mb.box(V(0, -0.12, 0), V(0.11, 0.24, 0.12), pants);
        mb.box(V(0, -0.17, 0.0), V(0.115, 0.022, 0.125), C(0.3, 0.22, 0.14));
        mb.box(V(0, -0.24, 0.0), V(0.115, 0.022, 0.125), C(0.3, 0.22, 0.14));
        mb.box(V(0, -0.31, 0.02), V(0.115, 0.1, 0.16), C(0.24, 0.17, 0.11));
        break;
      default:
        mb.box(V(0, -0.12, 0), V(0.105, 0.24, 0.115), pants);
        mb.box(V(0, -0.235, 0), V(0.11, 0.05, 0.12), C(0.72, 0.66, 0.55));   // 绑腿
        mb.box(V(0, -0.31, 0.02), V(0.115, 0.1, 0.16), C(0.15, 0.11, 0.09));  // 靴
        break;
    }
    return mb.toGeometry();
  }
  function spearGeo() {
    const mb = new SG.MeshBuilder();
    mb.box(V(0, 0.27, 0), V(0.024, 1.38, 0.024), WOOD);                  // 杆：y −0.42 → 0.96
    mb.cone(V(0, 0.95, 0), 0.034, 0.15, 4, C(0.88, 0.9, 0.94));          // 矛头
    mb.box(V(0, 0.935, 0), V(0.05, 0.035, 0.05), RED);                    // 红缨
    mb.box(V(0, 0, 0), V(0.055, 0.065, 0.06), C(0.92, 0.76, 0.6));        // 拳
    return mb.toGeometry();
  }
  function bowGeo() {
    const mb = new SG.MeshBuilder();
    const N = 6, R = 0.4;
    for (let i = 0; i < N; i++) {
      const y0 = -R + (i / N) * 2 * R, y1 = -R + ((i + 1) / N) * 2 * R;
      const z0 = 0.14 * (1 - (y0 / R) * (y0 / R)), z1 = 0.14 * (1 - (y1 / R) * (y1 / R));
      beam(mb, V(0, y0, z0), V(0, y1, z1), 0.028, 0.028, C(0.36, 0.22, 0.12));
    }
    beam(mb, V(0, -R, 0), V(0, R, 0), 0.007, 0.007, C(0.9, 0.88, 0.8));   // 弦
    mb.box(V(0, 0, 0.03), V(0.055, 0.065, 0.06), C(0.92, 0.76, 0.6));
    return mb.toGeometry();
  }
  function arrowGeo() {
    const mb = new SG.MeshBuilder();
    beam(mb, V(0, 0, -0.3), V(0, 0, 0.28), 0.018, 0.018, C(0.5, 0.38, 0.22));
    beam(mb, V(0, 0, 0.28), V(0, 0, 0.4), 0.05, 0, C(0.75, 0.76, 0.8));
    const fl = C(0.94, 0.93, 0.88);
    mb.quad(V(0, 0, -0.3), V(0, 0.045, -0.26), V(0, 0.045, -0.16), V(0, 0, -0.18), fl);
    mb.quad(V(0, 0, -0.3), V(0, 0, -0.18), V(0, 0.045, -0.16), V(0, 0.045, -0.26), fl);
    mb.quad(V(0, 0, -0.3), V(0.045, 0, -0.26), V(0.045, 0, -0.16), V(0, 0, -0.18), fl);
    mb.quad(V(0, 0, -0.3), V(0, 0, -0.18), V(0.045, 0, -0.16), V(0.045, 0, -0.26), fl);
    return mb.toGeometry();
  }
  function horseLegGeo() {
    const mb = new SG.MeshBuilder();
    beam(mb, V(0, 0.02, 0), V(0, -0.3, 0.02), 0.1, 0.07, C(1, 1, 1));
    beam(mb, V(0, -0.3, 0.02), V(0, -0.5, 0.0), 0.065, 0.055, C(0.92, 0.92, 0.92));
    mb.box(V(0, -0.525, 0.015), V(0.075, 0.05, 0.09), C(0.28, 0.28, 0.28));
    return mb.toGeometry();
  }
  // 武将骑马像：马身、马鞍披挂（势力色）、骑者（按文化的头盔 / 甲衣 / 披风）、长柄刀、帅旗杆
  function generalGeo(team, culture, horse, weapon, acc) {
    const mb = new SG.MeshBuilder();
    const cu = cultOf(culture);
    const t = team, dark = shade(t, -0.4), mid = shade(t, -0.18), light = shade(t, 0.18);
    const skin = C(cu.skin[0], cu.skin[1], cu.skin[2]);
    const hd = shade(horse, -0.25), mane = luma(horse) > 0.6 ? C(0.85, 0.83, 0.8) : C(0.12, 0.1, 0.09);
    const trim = acc || GOLD;
    // 马
    mb.box(V(0, 0.69, -0.02), V(0.3, 0.28, 0.86), horse);
    mb.box(V(0, 0.71, 0.36), V(0.32, 0.3, 0.2), shade(horse, 0.04));      // 胸
    mb.box(V(0, 0.7, -0.38), V(0.31, 0.27, 0.18), shade(horse, -0.05));   // 臀
    beam(mb, V(0, 0.76, 0.42), V(0, 1.06, 0.62), 0.2, 0.15, horse);          // 颈
    beam(mb, V(0, 1.1, 0.6), V(0, 0.97, 0.88), 0.15, 0.1, shade(horse, 0.04));   // 头
    mb.box(V(0, 0.96, 0.88), V(0.09, 0.07, 0.06), hd);                       // 口鼻
    mb.cone(V(-0.045, 1.14, 0.6), 0.025, 0.07, 4, hd);
    mb.cone(V(0.045, 1.14, 0.6), 0.025, 0.07, 4, hd);
    beam(mb, V(0, 0.87, 0.4), V(0, 1.15, 0.58), 0.06, 0.05, mane);           // 鬃
    beam(mb, V(0, 0.76, -0.47), V(0, 0.4, -0.66), 0.09, 0.04, mane);         // 尾
    // 鞍与披挂（草原 / 萨尔马提亚为毛皮鞍褥）
    mb.box(V(0, 0.85, -0.04), V(0.34, 0.06, 0.4), dark);
    mb.box(V(0, 0.7, -0.04), V(0.335, 0.26, 0.44), t);
    mb.box(V(0, 0.575, -0.04), V(0.34, 0.03, 0.45), trim);
    mb.box(V(0, 0.9, -0.12), V(0.2, 0.06, 0.16), cu.armor === 'furcoat' ? FUR : C(0.36, 0.22, 0.12));
    // 骑者
    const robe = cu.armor === 'robe' || cu.armor === 'furcoat';
    const legC = cu.legs === 'bare' ? skin : (robe ? mid : dark);
    mb.box(V(-0.16, 0.8, 0.06), V(0.09, 0.22, 0.12), legC);                  // 腿
    mb.box(V(0.16, 0.8, 0.06), V(0.09, 0.22, 0.12), legC);
    const boot = cu.legs === 'boots' ? C(0.2, 0.14, 0.1) : C(0.15, 0.11, 0.09);
    mb.box(V(-0.16, 0.68, 0.1), V(0.1, 0.07 + (cu.legs === 'boots' ? 0.05 : 0), 0.15), boot);
    mb.box(V(0.16, 0.68, 0.1), V(0.1, 0.07 + (cu.legs === 'boots' ? 0.05 : 0), 0.15), boot);
    mb.box(V(0, 1.1, -0.06), V(0.3, 0.32, 0.2), t);                          // 躯干
    torsoDeco(mb, cu, { y: 1.1, z: -0.06, w: 0.3, h: 0.32, d: 0.2 }, t, trim);
    mb.box(V(0, 0.95, -0.06), V(0.32, 0.06, 0.21), trim);                    // 腰带
    if (cu.armor !== 'furcloak' && cu.armor !== 'bare') {
      mb.box(V(-0.19, 1.22, -0.06), V(0.11, 0.08, 0.2), cu.armor === 'segmentata' ? IRON : trim);   // 肩甲
      mb.box(V(0.19, 1.22, -0.06), V(0.11, 0.08, 0.2), cu.armor === 'segmentata' ? IRON : trim);
    }
    const cloak = rgbC(cu.cloak, cu.armor === 'furcloak' ? FUR : (cu.armor === 'plaid' ? shade(t, -0.3) : mid));
    beam(mb, V(0, 1.24, -0.17), V(0, 0.84, -0.38), 0.3, 0.38, cloak);         // 披风（罗马将领为红色 paludamentum）
    const arm = (cu.armor === 'bare' || cu.armor === 'plaid') ? skin : mid;
    mb.box(V(-0.2, 1.08, 0.0), V(0.075, 0.2, 0.08), arm);                    // 左臂（持缰）
    mb.box(V(0.21, 1.1, 0.02), V(0.075, 0.18, 0.08), arm);                   // 右臂
    mb.box(V(0.22, 1.0, 0.12), V(0.07, 0.07, 0.14), arm);
    mb.box(V(0, 1.29, -0.05), V(0.09, 0.04, 0.09), skin);
    mb.box(V(0, 1.38, -0.045), V(0.165, 0.155, 0.16), skin);                 // 头
    mb.box(V(0, 1.375, 0.04), V(0.11, 0.025, 0.01), shade(skin, -0.45));
    beardOf(mb, cu, { x: 0, y: 1.38, z: -0.045 }, 0.16);
    // 将盔：按文化区分（DESIGN-V2 §6）；ghat 以 g_ 开头者为武将专用盔
    const gh = cu.ghat || cu.hat;
    headgear(mb, gh, t, 1.46, gh.indexOf('g_') === 0 ? 1 : 1.06, -0.045, acc, cu);
    // 兵器：长柄大刀（右手）
    const wx = 0.24, wz = 0.14;
    beam(mb, V(wx, 0.5, wz), V(wx, 1.95, wz), 0.035, 0.03, C(0.35, 0.22, 0.13));
    if (weapon === 'halberd') {
      beam(mb, V(wx, 1.95, wz), V(wx, 2.2, wz), 0.05, 0, C(0.88, 0.9, 0.94));
      beam(mb, V(wx, 1.86, wz), V(wx, 1.86, wz + 0.2), 0.03, 0.05, C(0.85, 0.87, 0.9));
      beam(mb, V(wx, 1.86, wz), V(wx, 1.86, wz - 0.2), 0.03, 0.05, C(0.85, 0.87, 0.9));
    } else if (weapon === 'spear') {
      beam(mb, V(wx, 1.95, wz), V(wx, 2.25, wz + 0.03), 0.045, 0, C(0.88, 0.9, 0.94));
      beam(mb, V(wx, 2.0, wz), V(wx + 0.02, 2.25, wz + 0.16), 0.03, 0, C(0.85, 0.87, 0.9));
    } else {
      // 偃月刀：弯刃（几段方柱拼成）
      const blade = C(0.86, 0.88, 0.92);
      beam(mb, V(wx, 1.9, wz + 0.02), V(wx, 2.12, wz + 0.1), 0.03, 0.06, blade);
      beam(mb, V(wx, 2.12, wz + 0.1), V(wx, 2.3, wz + 0.06), 0.06, 0.04, blade);
      beam(mb, V(wx, 2.3, wz + 0.06), V(wx, 2.38, wz - 0.04), 0.04, 0.0, blade);
    }
    mb.box(V(wx, 1.9, wz), V(0.06, 0.05, 0.06), acc || RED);
    mb.box(V(wx, 1.0, wz + 0.02), V(0.06, 0.07, 0.07), skin);
    // 帅旗杆（背后左侧）
    beam(mb, V(-0.18, 0.78, -0.3), V(-0.18, 3.0, -0.3), 0.035, 0.03, C(0.3, 0.2, 0.12));
    mb.cone(V(-0.18, 3.0, -0.3), 0.04, 0.14, 4, GOLD);
    beam(mb, V(-0.18, 2.86, -0.3), V(-0.18, 2.86, -1.06), 0.025, 0.025, C(0.3, 0.2, 0.12));
    return mb.toGeometry();
  }
  const HORSES = {
    '吕布': C(0.66, 0.2, 0.1), '关羽': C(0.6, 0.22, 0.12), '赵云': C(0.88, 0.87, 0.84), '公孙瓒': C(0.86, 0.85, 0.82),
    '曹操': C(0.17, 0.15, 0.14), '张飞': C(0.16, 0.14, 0.13), '马超': C(0.85, 0.84, 0.8), '刘备': C(0.78, 0.74, 0.68),
  };
  function horseColor(name) {
    if (HORSES[name]) return HORSES[name].clone();
    const pal = [C(0.5, 0.3, 0.18), C(0.38, 0.23, 0.14), C(0.62, 0.48, 0.3), C(0.22, 0.17, 0.14), C(0.72, 0.68, 0.62), C(0.55, 0.36, 0.22)];
    return pal[Math.abs(hashStr(name)) % pal.length].clone();
  }
  function weaponOf(gen) {
    const n = gen && gen.name;
    if (n === '吕布') return 'halberd';
    if (n === '张飞' || n === '赵云' || n === '马超' || n === '公孙瓒') return 'spear';
    return (gen && gen.war >= 85 && Math.abs(hashStr(n)) % 3 === 0) ? 'spear' : 'glaive';
  }

  // ======================================================= 常驻材质 --
  // 跨场次复用：材质不释放，其着色器程序就一直留在渲染器的缓存里，下次播放不必重新编译
  // （SwiftShader 下每个程序编译要数百毫秒，手机 GPU 也有几十毫秒）。贴图、几何体仍每次释放。
  const MATS = { particle: {}, flag: {}, water: null, fx: {} };
  function particleMaterial(blending, soft) {
    const key = blending + '|' + soft;
    let m = MATS.particle[key];
    if (!m) {
      m = MATS.particle[key] = new THREE.ShaderMaterial({
        uniforms: { uMap: { value: SG.Gfx.softDotTexture }, uScale: { value: 600 }, uSoft: { value: soft } },
        vertexShader: PARTICLE_VS, fragmentShader: PARTICLE_FS,
        transparent: true, depthWrite: false, depthTest: true, blending, fog: false,
      });
    }
    return m;
  }
  // 旗面材质：每方大旗 / 小旗各一个（同一贴图的旗帜共用），贴图每次替换
  function flagMaterial(key, tex) {
    let m = MATS.flag[key];
    if (!m) {
      m = MATS.flag[key] = SG.Gfx.newLowPoly();
      m.side = THREE.DoubleSide;
      m.alphaTest = 0.5;
    }
    m.map = tex;
    return m;
  }
  function waterMaterial() {
    if (!MATS.water) {
      const src = SG.Gfx.water();
      const mat = src.clone();
      for (const k of Object.keys(src.uniforms)) mat.uniforms[k] = src.uniforms[k];   // 与主场景共用时间、雾等
      mat.uniforms.uAlpha = { value: 0.66 };
      mat.uniforms.uAmp = { value: 0.06 };
      MATS.water = mat;
    }
    return MATS.water;
  }
  // 特效网格的发光材质：用完放回空闲表
  function fxMaterial(kind) {
    const free = MATS.fx[kind] || (MATS.fx[kind] = []);
    if (free.length) return free.pop();
    const o = { transparent: true, opacity: 1, blending: THREE.AdditiveBlending, depthWrite: false, fog: false };
    if (kind === 'ring') o.side = THREE.DoubleSide;
    else if (kind === 'shock') o.map = SG.Gfx.ringTexture;
    else if (kind === 'bolt') o.vertexColors = true;
    const m = new THREE.MeshBasicMaterial(o);
    m.userData.fxKind = kind;
    return m;
  }
  function releaseFx(m) {
    const k = m && m.userData && m.userData.fxKind;
    if (k) MATS.fx[k].push(m); else if (m) m.dispose();
  }

  // ======================================================= 旗帜 --
  // 旗面贴图：势力色底、犬牙边、中央圆徽与姓氏
  // accentHex：识别色（近色对阵时给出），用作犬牙边与内框，让两军旗帜一眼可辨
  function bannerTexture(glyph, colorHex, big, accentHex) {
    const W = 128, H = big ? 168 : 104;
    const cv = document.createElement('canvas');
    cv.width = W; cv.height = H;
    const g = cv.getContext('2d');
    const base = SG.Gfx.color(colorHex);
    const lum = luma(base);
    const css = c => '#' + c.getHexString();
    const tooth = big ? 12 : 10;
    // 犬牙边（左、右、下三边），颜色与旗面成对比
    const edge = accentHex ? SG.Gfx.color(accentHex) : lum > 0.62 ? shade(base, -0.55) : (lum < 0.25 ? C(0.88, 0.72, 0.32) : shade(base, -0.45));
    g.fillStyle = css(edge);
    g.beginPath();
    const tw = tooth * 1.4;
    for (let x = 0; x < W; x += tw) { g.moveTo(x, H - tooth); g.lineTo(x + tw / 2, H); g.lineTo(x + tw, H - tooth); }
    for (let y = 0; y < H - tooth; y += tw) {
      g.moveTo(tooth, y); g.lineTo(0, y + tw / 2); g.lineTo(tooth, y + tw);
      g.moveTo(W - tooth, y); g.lineTo(W, y + tw / 2); g.lineTo(W - tooth, y + tw);
    }
    g.fill();
    // 旗面
    const grd = g.createLinearGradient(0, 0, W, H);
    grd.addColorStop(0, css(shade(base, 0.18)));
    grd.addColorStop(0.55, css(base));
    grd.addColorStop(1, css(shade(base, -0.22)));
    g.fillStyle = grd;
    g.fillRect(tooth - 1, 0, W - 2 * tooth + 2, H - tooth + 1);
    g.strokeStyle = css(edge);
    g.lineWidth = 3;
    g.strokeRect(tooth + 4, 5, W - 2 * tooth - 8, H - tooth - 10);
    // 圆徽 + 姓氏
    const cx = W / 2, cy = (H - tooth) / 2, r = Math.min(W - 2 * tooth, H - tooth) * (big ? 0.36 : 0.38);
    if (big) {
      g.fillStyle = '#f4ecd6';
      g.beginPath(); g.arc(cx, cy, r, 0, Math.PI * 2); g.fill();
      g.strokeStyle = css(edge); g.lineWidth = 3; g.stroke();
    }
    g.fillStyle = big ? '#17130f' : (lum > 0.62 ? '#1d1a17' : '#f7efdc');
    g.font = `900 ${Math.round(r * (big ? 1.45 : 1.55))}px ${KAI}`;
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.fillText(glyph, cx, cy + r * 0.06);
    const tex = new THREE.CanvasTexture(cv);
    tex.generateMipmaps = true;
    tex.minFilter = THREE.LinearMipmapLinearFilter;
    tex.anisotropy = 2;
    tex.needsUpdate = true;
    return tex;
  }

  // 会飘动的旗面：局部坐标里位于 y-z 平面，旗杆边在 z = 0，向 +z（身后）展开、自 y = 0 向下垂
  class Flag {
    constructor(tex, w, h, flipU, seed, big, matKey) {
      this.w = w; this.h = h; this.seed = seed; this.big = big;
      const nx = 8, ny = big ? 6 : 4;
      this.nx = nx; this.ny = ny;
      const nv = (nx + 1) * (ny + 1);
      const pos = new Float32Array(nv * 3), uv = new Float32Array(nv * 2), col = new Float32Array(nv * 3);
      for (let j = 0; j <= ny; j++)
        for (let i = 0; i <= nx; i++) {
          const k = j * (nx + 1) + i;
          uv[k * 2] = flipU ? 1 - i / nx : i / nx;
          uv[k * 2 + 1] = 1 - j / ny;
          col[k * 3] = col[k * 3 + 1] = col[k * 3 + 2] = 1;
        }
      const idx = [];
      for (let j = 0; j < ny; j++)
        for (let i = 0; i < nx; i++) {
          const a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
          idx.push(a, c, b, b, c, d);
        }
      const g = new THREE.BufferGeometry();
      g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
      g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
      g.setAttribute('color', new THREE.BufferAttribute(col, 3));
      g.setIndex(idx);
      this.geometry = g;
      this.material = flagMaterial(matKey || 'x', tex);
      this.tex = tex;
      this.mesh = new THREE.Mesh(g, this.material);
      this.mesh.matrixAutoUpdate = false;
      this.mesh.castShadow = true;
      this.mesh.receiveShadow = false;
      this.mesh.frustumCulled = false;
      this.wave(0, 1);
      g.computeBoundingSphere();
    }
    // k：风力（0 = 下垂，1 = 正常飘扬，>1 疾驰）
    wave(t, k) {
      const p = this.geometry.attributes.position.array;
      const nx = this.nx, ny = this.ny, w = this.w, h = this.h, s = this.seed;
      for (let j = 0; j <= ny; j++)
        for (let i = 0; i <= nx; i++) {
          const q = (j * (nx + 1) + i) * 3;
          const u = i / nx, v = j / ny;
          const amp = u * (this.big ? 0.3 + 0.7 * v : 1);
          const ph = t * (6.5 + k * 3) - u * 5.2 - v * 0.8 + s;
          const droop = (1 - Math.min(1, k)) * u;
          p[q] = (Math.sin(ph) * 0.085 + Math.sin(ph * 1.7 + 1.3) * 0.03) * w * amp * (0.4 + 0.6 * Math.min(1.4, k));
          p[q + 1] = -v * h - droop * w * 0.55 - u * w * 0.05;
          p[q + 2] = u * w * (1 - droop * 0.45) * (1 - 0.06 * Math.abs(Math.sin(ph)));
        }
      this.geometry.attributes.position.needsUpdate = true;
      this.geometry.computeVertexNormals();
    }
    dispose() {
      this.geometry.dispose();
      if (this.material.map === this.tex) this.material.map = null;   // 材质常驻，只放开贴图引用
    }
  }

  // ======================================================= 粒子 --
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
  class Pool {
    constructor(max, blending, soft, renderOrder) {
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
      this.material = particleMaterial(blending, soft);
      this.points = new THREE.Points(g, this.material);
      this.points.frustumCulled = false;
      this.points.renderOrder = renderOrder;
    }
    add(p) { if (this.parts.length >= this.max) this.parts.shift(); this.parts.push(p); }
    update(dt, scale) {
      this.material.uniforms.uScale.value = scale;
      const P = this.parts;
      let n = 0;
      for (let i = 0; i < P.length; i++) {
        const p = P[i];
        p.age += dt;
        if (p.age >= p.life) continue;
        p.vy -= p.grav * 9.81 * dt;
        if (p.drag) { const k = Math.max(0, 1 - p.drag * dt); p.vx *= k; p.vy *= k; p.vz *= k; }
        p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
        if (p.floor !== undefined && p.y < p.floor) { p.y = p.floor; p.vy *= -0.2; p.vx *= 0.5; p.vz *= 0.5; }
        P[n++] = p;
      }
      P.length = n;
      for (let i = 0; i < n; i++) {
        const p = P[i], t = p.age / p.life;
        const fade = p.fadeIn > 0 ? Math.min(1, t / p.fadeIn) : 1;
        this.pos[i * 3] = p.x; this.pos[i * 3 + 1] = p.y; this.pos[i * 3 + 2] = p.z;
        this.col[i * 4] = p.r + (p.r1 - p.r) * t; this.col[i * 4 + 1] = p.g + (p.g1 - p.g) * t; this.col[i * 4 + 2] = p.b + (p.b1 - p.b) * t;
        this.col[i * 4 + 3] = p.a * (p.ac === 1 ? 1 - t : 1 - Math.pow(t, p.ac)) * fade;
        this.size[i] = p.size * (1 + (p.sizeEnd - 1) * t);
      }
      this.geometry.setDrawRange(0, n);
      if (n > 0) { this.aPos.needsUpdate = true; this.aCol.needsUpdate = true; this.aSize.needsUpdate = true; }
    }
    clear() { this.parts.length = 0; this.geometry.setDrawRange(0, 0); }
    dispose() { this.geometry.dispose(); }     // 材质常驻（见 particleMaterial）
  }

  // ======================================================= 地形 --
  const RIVER = { x: 1.05, w: 1.7, bank: 1.5, bed: -0.42, surf: -0.1 };
  const WALL_Z = -7.4, GATE_X = 4.6;     // 城墙正面（three z）与城门中心 x
  function makeHeight(kind, seed) {
    const r = SG.SeededRandom(seed);
    const ox = r.nextDouble() * 200, oz = r.nextDouble() * 200;
    const pn = (x, z, f) => M.perlinNoise(ox + x * f, oz + z * f) - 0.465;
    const hilly = kind === 'hill' || kind === 'mountain';
    const walled = kind === 'gate' || kind === 'castle' || kind === 'wall';
    return function h(x, z) {
      const far = sstep(9, 42, -z) ;
      const calm = 1 - 0.55 * (1 - sstep(5, 10, Math.abs(z - ZC))) * (1 - sstep(7, 13, Math.abs(x)));
      let y;
      if (hilly) y = (pn(x, z, 0.1) * 1.4 + pn(x, z, 0.32) * 0.22) * calm + 0.7 * sstep(-3, 7.5, x) - 0.18;
      else if (walled) y = pn(x, z, 0.22) * 0.07;
      else y = (pn(x, z, 0.15) * 0.34 + pn(x, z, 0.5) * 0.05) * calm;
      y += far * ((pn(x, z, 0.03) + 0.25) * (hilly ? 14 : 7) + (hilly ? 2.5 : 0.6));
      y += sstep(16, 34, Math.abs(x)) * (hilly ? 3 : 1.4) * (0.6 + pn(x, z, 0.06));
      if (kind === 'river') {
        const d = Math.abs(x - RIVER.x);
        const bank = sstep(RIVER.w, RIVER.w + RIVER.bank, d);
        y = lerp(RIVER.bed + pn(x, z, 0.8) * 0.04, Math.max(y * 0.6, -0.02) + 0.06, bank);
      }
      return y;
    };
  }
  function groundColor(kind, x, z, y, n) {
    let c;
    const trample = Math.exp(-(x * x) / 10 - ((z - ZC) * (z - ZC)) / 7);
    switch (kind) {
      case 'forest': c = C(0.3, 0.47, 0.25); break;
      case 'hill': c = C(0.53, 0.6, 0.34); break;
      case 'mountain': c = C(0.5, 0.54, 0.38); break;
      case 'gate': case 'castle': case 'wall': c = C(0.6, 0.55, 0.43); break;
      default: c = C(0.44, 0.64, 0.32); break;
    }
    if (kind === 'gate' || kind === 'castle' || kind === 'wall') {
      const grass = C(0.47, 0.6, 0.35);
      c.lerp(grass, sstep(3, 12, Math.abs(z - 2.5)) * 0.6 + n * 0.1);
      if (kind !== 'wall') {
        // 通往城门的夯土大道（两道车辙）与门前石板
        const d = Math.abs(x - GATE_X), w = 1.45 + Math.max(0, z - WALL_Z) * 0.07;
        const road = 1 - sstep(w - 0.35, w + 0.25, d);
        if (road > 0) {
          c.lerp(C(0.55, 0.47, 0.35), road * 0.85);
          const rut = Math.abs(d - w * 0.42);
          if (rut < 0.22) c.lerp(C(0.45, 0.38, 0.28), road * 0.55);
        }
        if (z < WALL_Z + 2.4 && d < 2.3) c.lerp(C(0.66, 0.64, 0.6), 0.75 * (1 - sstep(1.7, 2.3, d)));
      }
      // 墙根阴湿
      c.lerp(C(0.42, 0.42, 0.36), (1 - sstep(WALL_Z, WALL_Z + 1.2, z)) * 0.35);
    } else {
      c.lerp(C(0.58, 0.52, 0.37), trample * 0.42);
      if (n > 0.12) c.lerp(shade(c, kind === 'forest' ? -0.12 : 0.08), 0.6);
      if (n < -0.14) c.lerp(C(0.62, 0.62, 0.36), 0.35);
    }
    if (kind === 'river') {
      const d = Math.abs(x - RIVER.x);
      if (d < RIVER.w + RIVER.bank + 0.5) c = C(0.62, 0.57, 0.42).lerp(C(0.5, 0.46, 0.34), sstep(RIVER.w + RIVER.bank, RIVER.w * 0.4, d));
    }
    if (kind === 'mountain' && y > 3) c.lerp(C(0.58, 0.57, 0.54), sstep(3, 8, y));
    // 远处偏冷偏灰（雾会进一步融合）
    c.lerp(C(0.5, 0.58, 0.46), sstep(14, 45, -z) * 0.5);
    return c;
  }
  function axis(a, b, step, fine, grow) {
    // 中心 ±fine 内用 step，之外按 grow 递增
    const out = [];
    for (let x = 0; x <= b; ) { out.push(x); x += Math.abs(x) < fine ? step : step + (Math.abs(x) - fine) * grow; }
    const neg = [];
    for (let x = -step; x >= a; ) { neg.push(x); x -= Math.abs(x) < fine ? step : step + (Math.abs(x) - fine) * grow; }
    return neg.reverse().concat(out);
  }

  // 生成器：每做完一段 yield 一次，由 ClashScene.buildAsync 分帧执行（单帧 ≤ 约 10ms）
  function* buildTerrain(sc, kind, rnd, defColor) {
    const H = sc.h;
    const mb = new SG.MeshBuilder();
    // 地面网格（three 坐标 x、z）
    const xs = axis(-48, 48, 0.8, 13, 0.25);
    const zs = [];
    // 近处（z > 16）用越来越疏的行一直铺到镜头身后：竖屏 / 宽阵拉远机位时画面下缘不露底
    for (let z = 64; z > -95; ) { zs.push(z); z -= z > 16 ? 0.75 + (z - 16) * 0.3 : z > -9 ? 0.75 : 0.75 + (-9 - z) * 0.16; }
    const pn = (x, z) => M.perlinNoise(x * 0.21 + 11.3, z * 0.21 + 4.7) - 0.465;
    for (let j = 0; j < zs.length - 1; j++) {
      if (j % 6 === 5) yield;
      for (let i = 0; i < xs.length - 1; i++) {
        const x0 = xs[i], x1 = xs[i + 1], z0 = zs[j], z1 = zs[j + 1];   // z0 > z1（近 → 远）
        const a = P(x0, H(x0, z0), z0), b = P(x0, H(x0, z1), z1), c = P(x1, H(x1, z1), z1), d = P(x1, H(x1, z0), z0);
        const cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, cy = (a.y + c.y) / 2;
        const col = groundColor(kind, cx, cz, cy, pn(cx, cz));
        const flip = ((i + j) & 1) === 0;
        if (flip) {
          mb.tri(a, b, c, shade(col, (rnd.nextDouble() - 0.5) * 0.07));
          mb.tri(a, c, d, shade(col, (rnd.nextDouble() - 0.5) * 0.07));
        } else {
          mb.tri(a, b, d, shade(col, (rnd.nextDouble() - 0.5) * 0.07));
          mb.tri(b, c, d, shade(col, (rnd.nextDouble() - 0.5) * 0.07));
        }
      }
    }
    yield;
    const deco = new SG.MeshBuilder();
    const walled = kind === 'gate' || kind === 'castle' || kind === 'wall';
    // 草丛与小花
    const flowers = [C(0.98, 0.95, 0.85), C(1, 0.82, 0.3), C(0.95, 0.55, 0.6), C(0.75, 0.62, 0.95)];
    const blade = (bx, by, bz, h, lean, ang, col) => {
      const ca = Math.cos(ang), sa = Math.sin(ang), w = 0.045;
      const a = P(bx - ca * w, by, bz - sa * w), b = P(bx + ca * w, by, bz + sa * w);
      const tip = P(bx + lean * -sa, by + h, bz + lean * ca);
      deco.tri(a, tip, b, col); deco.tri(a, b, tip, col);
    };
    const tufts = walled ? 140 : kind === 'river' ? 360 : 520;
    for (let k = 0; k < tufts; k++) {
      if (k % 120 === 119) yield;
      const x = (rnd.nextDouble() * 2 - 1) * 14, z = 7 - rnd.nextDouble() * (walled ? 9 : 20);
      if (walled && z < -6.4) continue;
      if (kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w + 0.6) continue;
      const y = H(x, z);
      const gcol = kind === 'forest' ? C(0.26, 0.45, 0.22) : kind === 'hill' ? C(0.5, 0.58, 0.3) : C(0.36, 0.58, 0.26);
      const col = shade(gcol, (rnd.nextDouble() - 0.5) * 0.25);
      const s = 0.8 + rnd.nextDouble() * 0.7;
      for (let j = 0; j < 3; j++)
        blade(x + (rnd.nextDouble() - 0.5) * 0.12, y - 0.02, z + (rnd.nextDouble() - 0.5) * 0.12,
          (0.16 + rnd.nextDouble() * 0.1) * s, (rnd.nextDouble() - 0.5) * 0.14, rnd.nextDouble() * Math.PI, shade(col, j * 0.06));
      if (!walled && z < 2.5 && rnd.nextDouble() < 0.12) {
        const fc = flowers[rnd.next(flowers.length)];
        deco.box(P(x + 0.1, y + 0.06, z + 0.05), V(0.05, 0.035, 0.05), fc, 0.1);
      }
    }
    // 石块
    const rocks = kind === 'hill' || kind === 'mountain' ? 26 : kind === 'river' ? 22 : 8;
    for (let k = 0; k < rocks; k++) {
      let x = (rnd.nextDouble() * 2 - 1) * 16, z = 6 - rnd.nextDouble() * 22;
      if (kind === 'river') { x = RIVER.x + (rnd.nextDouble() < 0.5 ? -1 : 1) * (RIVER.w + 0.4 + rnd.nextDouble() * 1.6); }
      if (Math.abs(x) < 6 && Math.abs(z - ZC) < 3.5) continue;
      if (z > 1.8 && Math.abs(x) < 9) continue;          // 镜头前不放大石头
      if (walled && z < -6.5) continue;
      const r = 0.18 + rnd.nextDouble() * (kind === 'mountain' ? 0.7 : 0.4);
      deco.blob(P(x, H(x, z) + r * 0.25, z), V(r * 1.2, r * 0.75, r), shade(C(0.56, 0.55, 0.52), (rnd.nextDouble() - 0.5) * 0.15), k + 3);
    }
    // 树
    const tree = (x, z, s, pine, col) => SG.Models.tree(deco, P(x, H(x, z) - 0.05, z), s, col || shade(C(0.24, 0.46, 0.25), (rnd.nextDouble() - 0.5) * 0.18), pine, Math.abs(Math.round(x * 13 + z * 7)));
    if (kind === 'forest') {
      for (let k = 0; k < 120; k++) {
        if (k % 30 === 29) yield;
        const x = (rnd.nextDouble() * 2 - 1) * 26, z = -4.4 - rnd.nextDouble() * 26;
        tree(x, z, 2.2 + rnd.nextDouble() * 1.6, rnd.nextDouble() < 0.45);
      }
      // 两侧与近景的树（框住画面）
      for (const [x, z, s] of [[-9.5, 3.5, 3.4], [10.2, 2.8, 3.1], [-12.5, 0.5, 3.6], [12.8, -1.2, 3.8], [-7.6, -3.6, 2.8], [8.4, -4.0, 3.0], [-10.6, 6.4, 3.2], [11.4, 6.0, 3.3]])
        tree(x, z, s, rnd.nextDouble() < 0.4);
      // 落叶
      for (let k = 0; k < 160; k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 12, z = 6 - rnd.nextDouble() * 14, y = H(x, z) + 0.012;
        const lc = [C(0.62, 0.42, 0.18), C(0.72, 0.56, 0.22), C(0.5, 0.36, 0.16)][rnd.next(3)];
        const s = 0.06 + rnd.nextDouble() * 0.05, a = rnd.nextDouble() * 3;
        deco.tri(P(x + Math.cos(a) * s, y, z + Math.sin(a) * s), P(x - Math.sin(a) * s, y, z + Math.cos(a) * s), P(x - Math.cos(a) * s, y, z - Math.sin(a) * s), lc);
      }
    } else if (!walled) {
      const n = kind === 'river' ? 28 : 22;
      for (let k = 0; k < n; k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 30, z = -9 - rnd.nextDouble() * 24;
        if (kind === 'river' && Math.abs(x - RIVER.x) < 4) continue;
        tree(x, z, 2 + rnd.nextDouble() * 1.4, rnd.nextDouble() < 0.4);
      }
      for (const [x, z, s] of [[-12.8, -2.5, 2.8], [13.6, -3.4, 3.0]]) tree(x, z, s, false);
    }
    // 河岸芦苇
    if (kind === 'river') {
      for (let k = 0; k < 90; k++) {
        const sideK = rnd.nextDouble() < 0.5 ? -1 : 1;
        const x = RIVER.x + sideK * (RIVER.w + 0.9 + rnd.nextDouble() * 1.1), z = 6 - rnd.nextDouble() * 22;
        if (Math.abs(z - ZC) < 3.6 && Math.abs(x) < 5.5) continue;
        if (z > 2.2) continue;
        const y = H(x, z);
        for (let j = 0; j < 4; j++)
          blade(x + (rnd.nextDouble() - 0.5) * 0.2, y, z + (rnd.nextDouble() - 0.5) * 0.2, 0.45 + rnd.nextDouble() * 0.35,
            (rnd.nextDouble() - 0.5) * 0.2, rnd.nextDouble() * Math.PI, shade(C(0.58, 0.6, 0.32), (rnd.nextDouble() - 0.5) * 0.2));
      }
    }
    yield;
    // 远山
    const far = new SG.MeshBuilder();
    const hilly = kind === 'hill' || kind === 'mountain';
    for (let k = 0; k < 9; k++) {
      const x = -70 + k * 17 + (rnd.nextDouble() - 0.5) * 8, z = -62 - rnd.nextDouble() * 22;
      const h = (hilly ? 14 : 8) + rnd.nextDouble() * (hilly ? 14 : 7);
      far.cone(P(x, -2, z), 14 + rnd.nextDouble() * 10, h, 7, shade(C(0.5, 0.56, 0.52), (rnd.nextDouble() - 0.5) * 0.1));
      if (hilly && h > 18) far.cone(P(x, -2 + h * 0.72, z), (14 + 10 * 0.5) * 0.28, h * 0.28, 7, C(0.93, 0.94, 0.96));
    }
    // 城墙与城门
    if (walled) { yield; buildWall(deco, kind, rnd, defColor, H); }
    yield;
    const lp = SG.Gfx.lowPoly();
    const ground = SG.Gfx.mesh(mb.toGeometry(), lp, { castShadow: false, receiveShadow: true });
    const decoMesh = SG.Gfx.mesh(deco.toGeometry(), lp, { castShadow: true, receiveShadow: true });
    const farMesh = SG.Gfx.mesh(far.toGeometry(), lp, { castShadow: false, receiveShadow: false });
    sc.scene.add(ground); sc.scene.add(decoMesh); sc.scene.add(farMesh);
    sc.own.push(ground.geometry, decoMesh.geometry, farMesh.geometry);
    // 河水
    if (kind === 'river') {
      const wm = new SG.MeshBuilder();
      const x0 = RIVER.x - RIVER.w - RIVER.bank - 0.2, x1 = RIVER.x + RIVER.w + RIVER.bank + 0.2;
      const nx = 10;
      const zz = [];
      for (let z = 64; z > -95; ) { zz.push(z); z -= z > 16 ? 1.2 + (z - 16) * 0.3 : z > -10 ? 1.2 : 1.2 + (-10 - z) * 0.25; }
      for (let j = 0; j < zz.length - 1; j++)
        for (let i = 0; i < nx; i++) {
          const xa = lerp(x0, x1, i / nx), xb = lerp(x0, x1, (i + 1) / nx);
          const sa = clamp01(Math.abs(xa - RIVER.x) / (RIVER.w + RIVER.bank)), sb = clamp01(Math.abs(xb - RIVER.x) / (RIVER.w + RIVER.bank));
          const ka = C(0.25 + sa * 0.6, 0, 0), kb = C(0.25 + sb * 0.6, 0, 0);
          const y = RIVER.surf;
          const a = P(xa, y, zz[j]), b = P(xa, y, zz[j + 1]), c = P(xb, y, zz[j + 1]), d = P(xb, y, zz[j]);
          wm.tri(a, b, c, ka); wm.tri(a, c, d, ka);
          void kb;
        }
      const water = SG.Gfx.mesh(wm.toGeometry(), waterMaterial(), { castShadow: false, receiveShadow: false });
      water.renderOrder = 1;
      sc.scene.add(water);
      sc.own.push(water.geometry);
    }
  }

  function buildWall(mb, kind, rnd, defColor, H) {
    const Z = WALL_Z, TH = 1.4, WH = 3.3, X0 = -0.6, X1 = 44;
    const stone = C(0.7, 0.67, 0.6);
    const zc = Z - TH / 2;
    const gx = kind === 'wall' ? null : GATE_X;
    // 墙基与墙身
    mb.box(P((X0 + X1) / 2, 0.15, zc), V(X1 - X0, 0.5, TH + 0.25), shade(stone, -0.12));
    mb.box(P((X0 + X1) / 2, WH / 2, zc), V(X1 - X0, WH, TH), stone);
    // 墙砖（正面上不同明暗的块）
    const fz = Z + 0.012;
    for (let x = X0; x < X1 - 0.01; x += 1.25)
      for (let row = 0; row < 4; row++) {
        const y0 = 0.42 + row * 0.7, x0 = x + (row % 2) * 0.6, x1 = Math.min(X1, x0 + 1.2);
        if (x1 <= x0) continue;
        if (gx !== null && Math.abs((x0 + x1) / 2 - gx) < 2.1) continue;
        const col = shade(stone, (rnd.nextDouble() - 0.5) * 0.12 - 0.02);
        mb.quad(P(x0 + 0.04, y0 + 0.04, fz), P(x0 + 0.04, y0 + 0.66, fz), P(x1 - 0.04, y0 + 0.66, fz), P(x1 - 0.04, y0 + 0.04, fz), col);
      }
    // 垛口
    for (let x = X0 + 0.3; x < X1; x += 0.8) {
      if (gx !== null && Math.abs(x - gx) < 1.9) continue;
      mb.box(P(x, WH + 0.2, Z - 0.15), V(0.45, 0.4, 0.3), shade(stone, 0.05));
    }
    mb.box(P((X0 + X1) / 2, WH + 0.04, Z - 0.12), V(X1 - X0, 0.08, 0.3), shade(stone, -0.08));
    // 角楼（左端）
    mb.box(P(X0, 2.05, zc), V(1.9, 4.1, 1.9), shade(stone, -0.05));
    mb.box(P(X0, 4.45, zc), V(1.5, 0.7, 1.5), C(0.62, 0.2, 0.16));
    mb.chineseRoof(P(X0, 4.8, zc), 2.5, 2.5, 0.9, C(0.24, 0.28, 0.36));
    // 城内屋顶
    for (let k = 0; k < 7; k++) {
      const x = 2 + k * 3.4 + rnd.nextDouble() * 1.2, z = Z - 3.5 - rnd.nextDouble() * 5;
      mb.box(P(x, 1.8, z), V(2.2, 3.6, 1.6), C(0.86, 0.8, 0.68));
      mb.chineseRoof(P(x, 3.6, z), 3.0, 2.2, 1.0 + rnd.nextDouble() * 0.4, C(0.27, 0.3, 0.37));
    }
    // 墙根碎石
    for (let k = 0; k < 9; k++) {
      const x = X0 + 1.2 + rnd.nextDouble() * 16;
      if (gx !== null && Math.abs(x - gx) < 2.4) continue;
      const r = 0.12 + rnd.nextDouble() * 0.16;
      mb.blob(P(x, H(x, Z + 0.35) + r * 0.3, Z + 0.25 + rnd.nextDouble() * 0.4), V(r * 1.3, r * 0.8, r), shade(stone, -0.12 + (rnd.nextDouble() - 0.5) * 0.1), k + 11);
    }
    // 拒马：横木上交叉的削尖木桩（城门两侧、墙前）
    const stake = shade(WOOD, 0.08);
    const juma = (cx, cz, len) => {
      const y0 = H(cx, cz);
      beam(mb, P(cx - len / 2, y0 + 0.36, cz), P(cx + len / 2, y0 + 0.36, cz), 0.1, 0.1, shade(WOOD, -0.08));
      const n = Math.max(2, Math.round(len / 0.55));
      for (let i = 0; i < n; i++) {
        const x = cx - len / 2 + 0.2 + (len - 0.4) * (i / (n - 1));
        beam(mb, P(x, y0 + 0.02, cz - 0.42), P(x, y0 + 0.78, cz + 0.4), 0.06, 0.012, stake);
        beam(mb, P(x + 0.04, y0 + 0.02, cz + 0.42), P(x + 0.04, y0 + 0.78, cz - 0.4), 0.06, 0.012, stake);
      }
    };
    const jx = gx === null ? [1.4, 6.8] : [gx - 3.3, gx + 3.3, gx + 8.6];
    for (const x of jx) juma(x, Z + 1.75 + (rnd.nextDouble() - 0.5) * 0.3, 1.9);
    // 倚墙的云梯（攻城痕迹）
    {
      const lx = gx === null ? 9.5 : gx + 6.2, zb = Z + 1.25, zt = Z + 0.12, top = WH - 0.2;
      for (const s of [-0.24, 0.24]) beam(mb, P(lx + s, H(lx, zb) + 0.02, zb), P(lx + s, top, zt), 0.07, 0.06, WOOD);
      for (let i = 1; i < 8; i++) {
        const u = i / 8;
        beam(mb, P(lx - 0.24, lerp(H(lx, zb), top, u), lerp(zb, zt, u)), P(lx + 0.24, lerp(H(lx, zb), top, u), lerp(zb, zt, u)), 0.045, 0.045, shade(WOOD, 0.05));
      }
    }
    // 城头旗帜
    for (let k = 0; k < 6; k++) {
      const x = 1.6 + k * 2.6;
      if (gx !== null && Math.abs(x - gx) < 1.9) continue;
      mb.flag(P(x, WH + 0.1, Z - 0.45), 1.3, 0.55, 0.4, defColor);
    }
    if (gx === null) return;
    // 城门：门洞、门扇与门钉、城门楼
    const red = C(0.62, 0.2, 0.16);
    mb.box(P(gx, 1.85, zc + 0.12), V(3.6, 3.7, TH + 0.5), shade(stone, -0.04));
    const fz2 = Z + 0.38;
    mb.quad(P(gx - 0.75, 0.05, fz2), P(gx - 0.75, 1.75, fz2), P(gx + 0.75, 1.75, fz2), P(gx + 0.75, 0.05, fz2), C(0.12, 0.1, 0.09));
    // 拱顶（三角形近似）
    for (let i = 0; i < 6; i++) {
      const a0 = Math.PI * i / 6, a1 = Math.PI * (i + 1) / 6;
      mb.tri(P(gx, 1.75, fz2), P(gx + Math.cos(a1) * 0.75, 1.75 + Math.sin(a1) * 0.45, fz2), P(gx + Math.cos(a0) * 0.75, 1.75 + Math.sin(a0) * 0.45, fz2), C(0.12, 0.1, 0.09));
    }
    for (const sx of [-1, 1]) {
      mb.box(P(gx + sx * 0.37, 0.95, fz2 + 0.03), V(0.68, 1.8, 0.06), C(0.4, 0.23, 0.12));
      for (let r = 0; r < 4; r++)
        for (let c = 0; c < 3; c++)
          mb.box(P(gx + sx * (0.15 + c * 0.19), 0.3 + r * 0.42, fz2 + 0.07), V(0.05, 0.05, 0.03), GOLD);
    }
    mb.box(P(gx, 3.8, zc + 0.12), V(3.3, 0.2, TH + 0.3), shade(stone, 0.04));
    mb.box(P(gx, 4.4, zc), V(2.9, 1.0, 1.3), red);
    for (let i = -2; i <= 2; i++) mb.box(P(gx + i * 0.68, 4.4, Z + 0.06), V(0.12, 1.0, 0.1), shade(red, -0.25));
    mb.box(P(gx, 4.75, Z + 0.07), V(1.1, 0.34, 0.04), C(0.12, 0.1, 0.09));
    mb.box(P(gx, 4.75, Z + 0.09), V(0.9, 0.24, 0.02), GOLD);
    // 本城：金顶城楼 + 守方大旗（不再叠第二层楼——宽屏 / 手机上第二层会顶进上方 HUD；轮廓与城门同高）
    const castle = kind === 'castle';
    mb.chineseRoof(P(gx, 4.9, zc), 4.0, 2.2, 1.15, castle ? C(0.85, 0.65, 0.2) : C(0.22, 0.26, 0.34));
    if (castle) {
      mb.flag(P(gx + 1.55, 3.9, Z), 1.55, 0.8, 0.52, defColor);
      mb.flag(P(gx - 1.55, 3.9, Z), 1.55, 0.8, 0.52, defColor);
    }
  }

  // ======================================================= 阵型 --
  // 返回 K 个步兵的队形位置 { f（向敌为正，前排为 0）, l（纵深方向）, file }
  function formationSlots(fi, K) {
    let R;
    switch (fi) {
      case 0: R = clamp(Math.round(Math.sqrt(K * 1.2)), 3, 6); break;   // 方圆
      case 1: R = K > 14 ? 3 : 2; break;                                    // 长蛇
      case 2: R = 5; break;                                                 // 鱼鳞
      case 3: R = 7; break;                                                 // 鹤翼
      case 4: R = 6; break;                                                 // 偃月
      case 5: R = 5; break;                                                 // 锋矢
      case 6: R = 6; break;                                                 // 雁行
      default: R = 5; break;                                                // 衡轭
    }
    // 纵深（列数）有上限，免得后排与主将跑出画面
    R = Math.max(R, Math.ceil(K / (fi === 1 ? 6 : 4)));
    R = Math.max(1, Math.min(R, K, 8));
    const F = Math.ceil(K / R);
    const out = [];
    for (let c = 0; c < F; c++) {
      const inFile = Math.min(R, K - c * R);
      for (let r = 0; r < inFile; r++) {
        const rr = r + (R - inFile) / 2;
        const ln = R > 1 ? (rr / (R - 1)) * 2 - 1 : 0;
        let l = (rr - (R - 1) / 2) * SP_L;
        let f = -c * SP_F;
        switch (fi) {
          case 0: f += (Math.abs(ln) > 0.9 && (c === 0 || c === F - 1)) ? -0.18 : 0; break;
          case 1: l += Math.sin(c * 1.3) * 0.3; f *= 1.05; break;
          case 2: f -= Math.abs(ln) * 0.95; if (r % 2) f -= SP_F * 0.5; break;
          case 3: f += (Math.abs(ln) - 1) * 1.25; l *= 1.06; break;
          case 4: f -= ln * ln * 1.15; break;
          case 5: f -= Math.abs(ln) * 1.8; l *= 0.85; break;
          case 6: f += ln * 1.35; break;
          default: if (c >= F / 2) { f -= 0.6; l += 0.31; } break;
        }
        out.push({ f, l, file: c });
      }
    }
    let mx = -Infinity;
    for (const s of out) mx = Math.max(mx, s.f);
    for (const s of out) s.f -= mx;
    // 前排序号：按离敌远近重新编号（file 0 = 最前）
    for (const s of out) s.file = Math.round(-s.f / SP_F);
    return out;
  }

  // ======================================================= 场景 --
  const _m = new THREE.Matrix4(), _m2 = new THREE.Matrix4(), _q = new THREE.Quaternion(), _q2 = new THREE.Quaternion();
  const _e = new THREE.Euler(), _p = new THREE.Vector3(), _one = new THREE.Vector3(1, 1, 1), _zero = new THREE.Vector3(0, 0, 0);
  const _c = new THREE.Color(), _v1 = new THREE.Vector3(), _v2 = new THREE.Vector3(), _fwd = new THREE.Vector3(0, 0, -1);
  const _X = new THREE.Vector3(1, 0, 0);
  const HIDE = new THREE.Matrix4().makeScale(0, 0, 0);

  function norm(side) {
    side = side || {};
    const gen = side.gen || { name: '无名', war: 50 };
    const before = Math.max(0, Math.round(+side.troopsBefore || 0));
    let after = side.troopsAfter === undefined ? before : Math.round(+side.troopsAfter || 0);
    after = clamp(after, 0, before);
    return {
      gen, name: String(gen.name || '无名'), side: side.side === 1 ? 1 : 0,
      color: hexOf(side.color || '#808080'),
      before, after, loss: before - after,
      formation: formIndex(side.formation !== undefined ? side.formation : gen.formation),
      accent: null,     // 识别色，见 pickAccents
      culture: side.culture || gen.culture || (/^(孟获|孟优|祝融|兀突骨|木鹿|朵思|带来)/.test(gen.name || '') ? 'nanman' : 'han'),
    };
  }

  // 两军势力色过于接近时（袁绍 #2f6db5 对曹操 #2c3d8f 等）拉开：较亮的一方再提亮，另一方压暗并转开色相
  function hexRgb(h) { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
  function rgbHsl(r, g, b) {
    r /= 255; g /= 255; b /= 255;
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2;
    if (mx === mn) return [0, 0, l];
    const d = mx - mn, s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
    const h = mx === r ? (g - b) / d + (g < b ? 6 : 0) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [h / 6, s, l];
  }
  function hslHex(h, s, l) {
    h = ((h % 1) + 1) % 1;
    const f = n => { const k = (n + h * 12) % 12, a = s * Math.min(l, 1 - l); return Math.round(255 * (l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)))); };
    return '#' + [f(0), f(8), f(4)].map(v => v.toString(16).padStart(2, '0')).join('');
  }
  // 必杀种类 → 画面特效：火（blaze / fx fire）、雷（storm / fx lightning）、箭雨（volley / fx arrow(s)），其余为斩光
  function specKind(sp) {
    const k = String(sp.kind || ''), fx = String(sp.fx || '');
    if (k === 'blaze' || fx === 'fire') return 'blaze';
    if (k === 'storm' || fx === 'lightning' || fx === 'shock') return 'storm';
    if (k === 'volley' || fx === 'arrows' || fx === 'arrow') return 'volley';
    return k || 'smite';
  }
  const SEP_DIST = 95;
  function separateColors(A, D) {
    const a = hexRgb(A.color), d = hexRgb(D.color);
    const dist = Math.hypot(a[0] - d[0], a[1] - d[1], a[2] - d[2]);
    if (dist >= SEP_DIST) return false;
    const k = 0.6 + 0.4 * (1 - dist / SEP_DIST);
    const ha = rgbHsl(a[0], a[1], a[2]), hd = rgbHsl(d[0], d[1], d[2]);
    const up = ha[2] >= hd[2] ? ha : hd, dn = up === ha ? hd : ha;
    let dh = dn[0] - up[0];
    if (dh > 0.5) dh -= 1; if (dh < -0.5) dh += 1;
    up[2] = clamp(up[2] + 0.12 * k, 0, 0.72);
    dn[2] = clamp(dn[2] - 0.1 * k, 0.14, 1);
    dn[0] += (dh >= 0 ? 1 : -1) * 0.09 * k;
    dn[1] = Math.max(dn[1], 0.35 * k);
    A.color = hslHex(ha[0], ha[1], ha[2]);
    D.color = hslHex(hd[0], hd[1], hd[2]);
    return true;
  }

  // 识别色：两军势力色相近（原始 RGB 距离 < ACC_DIST）时，各给一种与两军颜色都拉得开、彼此也不同的镶边色，
  // 用在士兵的肩甲、腰带、盾缘 / 盾心、盔缨，武将的鞍边、腰带、盔羽，以及旗帜的犬牙边上——混战中也分得清敌我
  const ACC_DIST = 130;
  const ACCENTS = ['#f4ecd8', '#f0b42c', '#1b1815', '#d8342c', '#3cc0c8', '#9be05a'];
  function colorDist(x, y) { const a = hexRgb(x), b = hexRgb(y); return Math.hypot(a[0] - b[0], a[1] - b[1], a[2] - b[2]); }
  function pickAccents(A, D) {
    const best = (avoid) => {
      let pick = ACCENTS[0], sc = -1;
      for (const c of ACCENTS) {
        const m = Math.min(...avoid.map(a => colorDist(c, a)));
        if (m > sc) { sc = m; pick = c; }
      }
      return pick;
    };
    A.accent = best([A.color, D.color]);
    D.accent = best([A.color, D.color, A.accent]);
  }

  class ClashScene {
    constructor(opts) {
      this.opts = opts;
      this.kind = terrainKind(opts.terrain);
      this.A = norm(opts.attacker);
      this.D = norm(opts.defender);
      const d0 = colorDist(this.A.color, this.D.color);
      this.recolored = separateColors(this.A, this.D);
      if (d0 < ACC_DIST) pickAccents(this.A, this.D);
      this.special = opts.special && opts.special.name ? { name: String(opts.special.name), color: hexOf(opts.special.color || '#ffd24d'), kind: specKind(opts.special), cry: opts.special.cry || null } : null;
      this.playerSide = opts.playerSide === 0 || opts.playerSide === 1 ? opts.playerSide : null;
      this.speed = Math.max(0.1, (+SG.Clash.speed || 1) * (+opts.speed || 1));
      const seed = hashStr(this.A.name + '|' + this.D.name + '|' + this.A.before + '|' + this.D.before + '|' + this.kind);
      this.rnd = SG.SeededRandom(seed);
      this.own = [];                 // 需要释放的 geometry / material / texture
      this.t = 0; this.real = 0; this._tPrev = 0;
      this.state = 'run';            // run | hold | out | done
      this.skipped = false;
      this.shake = 0;
      // 系统「减弱动态效果」：不晃镜头、不闪屏
      try { this.calm = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches); } catch (e) { this.calm = false; }
      this.lastHost = 0;
      this.evIdx = 0;
      this.seed = seed;
      this.instanced = [];
      this.temp = [];        // 特效网格 { mesh, t0, life, fn }
    }
    // 分帧构建：每帧最多约 budget 毫秒，返回 Promise
    async buildAsync(budget) {
      const it = this.build(this.seed);
      const b0 = performance.now();
      let f0 = b0;
      this._maxSlice = 0;
      for (;;) {
        const r = it.next();
        const n = performance.now();
        if (r.done) { this._maxSlice = Math.max(this._maxSlice, n - f0); break; }
        if (n - f0 > (budget || 10)) { this._maxSlice = Math.max(this._maxSlice, n - f0); await SG.frame(); f0 = performance.now(); }
      }
      this._buildMs = performance.now() - b0;
    }
    buildSync() { const it = this.build(this.seed); while (!it.next().done) { /* 一次做完 */ } }

    // ---------------------------------------------------------- 构建 --
    *build(seed) {
      const Gfx = SG.Gfx;
      const scene = this.scene = new THREE.Scene();
      scene.background = C(FOG[0], FOG[1], FOG[2]);
      scene.fog = new THREE.Fog(C(FOG[0], FOG[1], FOG[2]), 34, 135);
      const cam = this.camera = new THREE.PerspectiveCamera(34, Gfx.width / Math.max(1, Gfx.height), 0.3, 300);
      scene.add(cam);
      // 光照与主场景一致
      const sun = this.sun = new THREE.DirectionalLight(Gfx.sun ? Gfx.sun.color.clone() : C(1, 0.94, 0.84), Gfx.sun ? Gfx.sun.intensity : 2.7);
      sun.castShadow = true;
      sun.shadow.mapSize.set(SG.isTouch ? 1024 : 2048, SG.isTouch ? 1024 : 2048);
      sun.shadow.bias = -0.0005;
      sun.shadow.normalBias = 0.03;
      const s = sun.shadow.camera;
      s.left = -15; s.right = 15; s.top = 11; s.bottom = -11; s.near = 1; s.far = 90;
      const dir = (Gfx.sunDir || V(0.38, 0.74, 0.55)).clone().normalize();
      sun.position.copy(dir).multiplyScalar(45).add(V(0, 0, ZC));
      sun.target.position.set(0, 0, ZC);
      scene.add(sun); scene.add(sun.target);
      if (Gfx.hemi) {
        const hemi = new THREE.HemisphereLight(Gfx.hemi.color.clone(), Gfx.hemi.groundColor.clone(), Gfx.hemi.intensity);
        scene.add(hemi);
      } else scene.add(new THREE.HemisphereLight(C(0.62, 0.68, 0.8), C(0.32, 0.3, 0.26), 1.76));
      if (Gfx.sky) {
        this.sky = new THREE.Mesh(Gfx.sky.geometry, Gfx.sky.material);
        this.sky.frustumCulled = false;
        this.sky.renderOrder = -100000;
        scene.add(this.sky);
      }
      // 地形
      this.h = makeHeight(this.kind, seed);
      yield* buildTerrain(this, this.kind, SG.SeededRandom(seed ^ 0x5bd1e995), this.D.color);
      // 两军
      this.armies = [this.buildArmy(this.A, 0), this.buildArmy(this.D, 1)];
      this.planCharge();
      this.planArrows();
      yield;
      yield* this.buildMeshes();
      this.planCasualties();
      this.planEvents();
      // 粒子
      this.dust = new Pool(520, THREE.NormalBlending, 0.6, 11);
      this.fx = new Pool(420, THREE.NormalBlending, 1.0, 12);
      this.glow = new Pool(520, THREE.AdditiveBlending, 1.0, 14);
      for (const p of [this.dust, this.fx, this.glow]) scene.add(p.points);
      this.pose(0);
      this.built = true;
    }

    buildArmy(info, si) {
      const rnd = this.rnd;
      const dir = si === 0 ? 1 : -1;
      const n = info.before > 0 ? clamp(Math.round(info.before / 300), 6, 28) : 6;
      const nArch = clamp(Math.round(n * 0.22), 2, 6);
      const nInf = n - nArch;
      const slots = formationSlots(info.formation, nInf);
      let minF = 0;
      for (const sl of slots) minF = Math.min(minF, sl.f);
      const soldiers = [];
      const xFront = -dir * FRONT;
      const mk = (role, f, l, file) => {
        const s = {
          role, f, l, file, si,
          x0: xFront + dir * f, z0: ZC + l,
          ed: rnd.nextDouble() * 0.08 + (-f) * 0.012,
          cd: rnd.nextDouble() * 0.07 + (-f) * 0.03,
          rd: rnd.nextDouble() * 0.12,
          push: 0,
          w1: 7 + rnd.nextDouble() * 5, p1: rnd.nextDouble() * 6.3, w2: 13 + rnd.nextDouble() * 6, p2: rnd.nextDouble() * 6.3,
          w3: 5 + rnd.nextDouble() * 4, p3: rnd.nextDouble() * 6.3, w4: 6 + rnd.nextDouble() * 5, p4: rnd.nextDouble() * 6.3,
          wt: 9 + rnd.nextDouble() * 6, pt: rnd.nextDouble() * 6.3, p0: rnd.nextDouble() * 6.3,
          fallT: Infinity, fallKind: 'melee', fallDir: rnd.nextDouble() < 0.72 ? 1 : -1, kb: 0,
          hits: [], flash: 0, shotDelay: rnd.nextDouble() * 0.1,
          bodyIdx: -1, legIdx: -1, wIdx: -1, flag: null,
        };
        soldiers.push(s);
        return s;
      };
      for (const sl of slots) mk('inf', sl.f, sl.l, sl.file);
      // 弓手：步兵之后一到两排
      const aRows = nArch > 4 ? 2 : 1, per = Math.ceil(nArch / aRows);
      let minAll = minF;
      for (let k = 0; k < nArch; k++) {
        const row = Math.floor(k / per), cnt = Math.min(per, nArch - row * per), i = k - row * per;
        const f = minF - 0.72 - row * 0.5, l = (i - (cnt - 1) / 2) * 0.82 + (row % 2) * 0.41;
        mk('arch', f, l, 99);
        minAll = Math.min(minAll, f);
      }
      // 旗手：步兵最后一排中最靠近中线的一到两名
      const inf = soldiers.filter(s => s.role === 'inf');
      const rear = inf.slice().sort((a, b) => (a.f - b.f) || (Math.abs(a.l) - Math.abs(b.l)));
      const nFlag = n >= 14 ? 2 : 1;
      const flags = [];
      for (const s of rear) {
        if (flags.length >= nFlag) break;
        if (flags.some(o => Math.abs(o.l - s.l) < 1.2)) continue;
        s.role = 'flag'; flags.push(s);
      }
      // 前两排冲进敌阵的深度（交错进入）
      for (const s of soldiers) if (s.role === 'inf' && s.file <= 1) s.push = (s.file === 0 ? 0.18 : 0.06) + rnd.nextDouble() * 0.32;
      const general = {
        x0: xFront + dir * (minAll - 1.2), z0: ZC - 0.3, ed: 0.1,
        p0: rnd.nextDouble() * 6, fallT: Infinity,
      };
      return {
        si, dir, info, n, soldiers, general, xFront, minAll,
        routed: info.before > 0 && info.after <= 0,
        brace: info.formation === 0 || info.formation === 7,
        chargeDX: 0, genDX: 0, cheer: false,
      };
    }

    planCharge() {
      const [L, R] = this.armies;
      // 守方结方圆 / 衡轭时原地坚守，攻方冲过整段距离
      let xc = 0;
      if (R.brace && !L.brace) xc = FRONT - 0.3;
      else if (L.brace && !R.brace) xc = -(FRONT - 0.3);
      L.chargeDX = (xc - 0.27) - L.xFront;
      R.chargeDX = R.xFront - (xc + 0.27);
      for (const A of this.armies) A.genDX = Math.min(0.9, A.chargeDX * 0.4);
      this.xc = xc;
      L.cheer = R.routed && !L.routed; R.cheer = L.routed && !R.routed;
    }

    *buildMeshes() {
      const lp = SG.Gfx.lowPoly();
      const scene = this.scene;
      const inst = (geo, count, opts) => {
        const m = new THREE.InstancedMesh(geo, lp, Math.max(1, count));
        m.count = count;
        m.castShadow = true; m.receiveShadow = true;
        m.frustumCulled = false;
        m.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
        for (let i = 0; i < Math.max(1, count); i++) { m.setMatrixAt(i, HIDE); m.setColorAt(i, (opts && opts.color) || C(1, 1, 1)); }
        if (m.instanceColor) m.instanceColor.setUsage(THREE.DynamicDrawUsage);
        scene.add(m);
        this.own.push(geo);
        this.instanced.push(m);
        return m;
      };
      let nSpear = 0, nBow = 0;
      for (const A of this.armies) {
        const team = SG.Gfx.color(A.info.color);
        const acc = A.info.accent ? SG.Gfx.color(A.info.accent) : null;
        const infList = A.soldiers.filter(s => s.role !== 'arch'), archList = A.soldiers.filter(s => s.role === 'arch');
        A.infMesh = inst(bodyGeo(team, A.info.culture, false, acc), infList.length);
        A.archMesh = inst(bodyGeo(team, A.info.culture, true, acc), archList.length);
        A.legMesh = inst(legGeo(A.info.culture), A.soldiers.length * 2);    // 腿按文化（裤 / 裸腿 / 长靴 / 绑腿），每方一组
        infList.forEach((s, i) => { s.bodyIdx = i; });
        archList.forEach((s, i) => { s.bodyIdx = i; });
        yield;
        A.soldiers.forEach((s, i) => {
          s.legIdx = i * 2;
          if (s.role === 'arch') s.wIdx = nBow++; else s.wIdx = nSpear++;
        });
      }
      this.spearMesh = inst(spearGeo(), nSpear);
      this.bowMesh = inst(bowGeo(), nBow);
      // 武将
      this.horseLegs = inst(horseLegGeo(), 8);
      for (const A of this.armies) {
        const team = SG.Gfx.color(A.info.color);
        const hc = horseColor(A.info.name);
        const g = A.general;
        g.horse = hc;
        g.mesh = SG.Gfx.mesh(generalGeo(team, A.info.culture, hc, weaponOf(A.info.gen), A.info.accent ? SG.Gfx.color(A.info.accent) : null), lp, { castShadow: true, receiveShadow: true });
        g.mesh.matrixAutoUpdate = false;
        g.mesh.frustumCulled = false;
        scene.add(g.mesh);
        this.own.push(g.mesh.geometry);
        for (let i = 0; i < 4; i++) this.horseLegs.setColorAt(A.si * 4 + i, hc);
        // 帅旗与士兵旗
        const glyph = surname(A.info.gen);
        const big = bannerTexture(glyph, A.info.color, true, A.info.accent);
        const small = bannerTexture(glyph, A.info.color, false, A.info.accent);
        this.own.push(big, small);
        g.flag = new Flag(big, 0.74, 0.98, A.dir > 0, A.si * 3.1, true, A.si + 'B');
        scene.add(g.flag.mesh);
        this.own.push(g.flag);
        for (const s of A.soldiers) if (s.role === 'flag') {
          s.flag = new Flag(small, 0.56, 0.4, A.dir > 0, this.rnd.nextDouble() * 6, false, A.si + 's');
          scene.add(s.flag.mesh);
          this.own.push(s.flag);
        }
        yield;
      }
      if (this.horseLegs.instanceColor) this.horseLegs.instanceColor.needsUpdate = true;
      // 箭（特殊箭雨染成必杀技颜色并发光）
      this.arrowMesh = inst(arrowGeo(), this.arrows.length);
      if (this.special) {
        const sc = SG.Gfx.color(this.special.color);
        const glowC = C(0.6 + sc.r * 1.6, 0.6 + sc.g * 1.6, 0.6 + sc.b * 1.6);
        this.arrows.forEach((r, i) => { if (r.spec) this.arrowMesh.setColorAt(i, glowC); });
      }
    }

    // 箭：每方一轮齐射
    planArrows() {
      const rnd = this.rnd;
      this.arrows = [];
      for (const S of this.armies) {
        const E = this.armies[1 - S.si];
        const archers = S.soldiers.filter(s => s.role === 'arch');
        const n = clamp(archers.length * 3, 8, 16) + (this.special && this.special.kind === 'volley' && S.si === 0 ? 18 : 0);
        S.volleyT = TL.volley[S.si];
        for (let i = 0; i < n; i++) {
          const a = archers[i % archers.length];
          const spec = i >= clamp(archers.length * 3, 8, 16);
          const t0 = S.volleyT + a.shotDelay + (spec ? 0.04 + rnd.nextDouble() * 0.16 : rnd.nextDouble() * 0.05);
          const T = TL.flight * (0.92 + rnd.nextDouble() * 0.16);
          this.arrows.push({ S, E, archer: a, t0, T, land: t0 + T, target: null, p0: V(), p1: V(), apex: 0, hit: false, spec, done: false });
        }
      }
    }

    // 伤亡：按兵力损失比例决定倒下人数，分配给箭雨 / 必杀 / 混战
    planCasualties() {
      const rnd = this.rnd;
      this.chunks = [];       // 兵力数字的扣减时刻 { si, t, amount, kind }
      for (const A of this.armies) {
        const E = this.armies[1 - A.si];
        const info = A.info;
        const frac = info.before > 0 ? info.loss / info.before : 0;
        let k = Math.round(A.n * Math.min(frac, A.routed ? 0.6 : 0.9));
        if (A.routed) k = Math.max(k, Math.ceil(A.n * 0.45));
        if (info.loss > 0 && k === 0 && frac >= 0.035) k = 1;
        if (!A.routed) k = Math.min(k, A.n - 2);
        k = Math.max(0, k);
        const special = this.special && A.si === 1 ? this.special : null;
        const shareSpec = special ? 0.45 : 0, shareArrow = special && special.kind === 'volley' ? 0 : (special ? 0.12 : 0.2);
        let kSpec = special ? Math.round(k * 0.45) : 0;
        const incoming = this.arrows.filter(r => r.E === A && !r.spec);
        let kArrow = Math.min(Math.round(k * shareArrow + 0.25), Math.floor(incoming.length / 2), 3);
        if (k - kSpec - kArrow < 0) kArrow = Math.max(0, k - kSpec);
        const kMelee = k - kSpec - kArrow;
        // 选人：箭伤落在中后排步兵 / 弓手，混战伤亡集中在前排
        const pool = A.soldiers.slice();
        const pick = (score) => {
          let best = -1, bs = -Infinity;
          for (let i = 0; i < pool.length; i++) { const v = score(pool[i]) + rnd.nextDouble() * 0.9; if (v > bs) { bs = v; best = i; } }
          return best >= 0 ? pool.splice(best, 1)[0] : null;
        };
        const melee = [], arrowC = [], specC = [];
        for (let i = 0; i < kSpec; i++) { const s = pick(s => (s.role === 'inf' ? 2 : 0) - s.file * 0.5); if (s) specC.push(s); }
        for (let i = 0; i < kMelee; i++) { const s = pick(s => (s.role === 'inf' ? 2.5 : s.role === 'flag' ? -1 : 0) - s.file * 0.9); if (s) melee.push(s); }
        for (let i = 0; i < kArrow; i++) { const s = pick(s => (s.role === 'flag' ? -3 : 0) + (s.file > 0 ? 1 : 0)); if (s) arrowC.push(s); }
        // 箭伤：让对应的箭射中此人
        const spare = incoming.slice();
        for (const s of arrowC) {
          const r = spare.splice(rnd.next(Math.max(1, spare.length)), 1)[0];
          if (!r) continue;
          r.target = s; s.fallT = r.land; s.fallKind = 'arrow';
        }
        // 必杀：volley 型由特殊箭射中，其余在接敌瞬间被震飞
        if (special) {
          const specArrows = this.arrows.filter(r => r.E === A && r.spec);
          specC.forEach((s, i) => {
            if (special.kind === 'volley' && specArrows.length) {
              const r = specArrows[i % specArrows.length];
              if (!r.target) { r.target = s; s.fallT = r.land; } else s.fallT = r.land + 0.05;
              s.fallKind = 'arrow';
            } else {
              s.fallT = this.specialT() + 0.02 + rnd.nextDouble() * 0.12;
              s.fallKind = 'blast'; s.kb = 1.1 + rnd.nextDouble() * 1.1; s.fallDir = 1;
            }
          });
        }
        const m0 = TL.contact + 0.08, m1 = TL.melee1 - 0.2;
        melee.sort((a, b) => a.file - b.file);
        melee.forEach((s, i) => {
          const u = (i + 0.3 + rnd.nextDouble() * 0.5) / Math.max(1, melee.length);
          s.fallT = lerp(m0, m1, Math.pow(u, 1.15));
          s.fallKind = 'melee';
        });
        // 兵力扣减分段（总和 = loss）
        const L = info.loss;
        if (L > 0) {
          const parts = [];
          const arrowLand = incoming.length ? incoming.reduce((m, r) => Math.min(m, r.land), Infinity) + 0.12 : TL.contact;
          if (shareArrow > 0) parts.push({ t: arrowLand, w: shareArrow, kind: 'arrow' });
          if (special) {
            const st = special.kind === 'volley' ? this.arrows.filter(r => r.E === A && r.spec).reduce((m, r) => Math.min(m, r.land), Infinity) + 0.1 : this.specialT() + 0.08;
            parts.push({ t: isFinite(st) ? st : TL.contact, w: shareSpec + (special.kind === 'volley' ? 0.12 : 0), kind: 'special' });
          }
          const rest = 1 - parts.reduce((s, p) => s + p.w, 0);
          const lag = A.si * 0.09;
          parts.push({ t: TL.contact + 0.3 + lag, w: rest * 0.55, kind: 'melee' });
          parts.push({ t: TL.contact + 0.68 + lag, w: rest * 0.45, kind: 'melee' });
          parts.sort((a, b) => a.t - b.t);
          let left = L;
          parts.forEach((p, i) => {
            const amt = i === parts.length - 1 ? left : Math.min(left, Math.round(L * p.w));
            left -= amt;
            if (amt > 0) this.chunks.push({ si: A.si, t: p.t, amount: amt, kind: p.kind });
          });
        }
        A.fallen = A.soldiers.filter(s => isFinite(s.fallT)).length;
        void E;
      }
      this.chunks.sort((a, b) => a.t - b.t);
      // 非致命的受击闪光（前排混战）
      for (const A of this.armies)
        for (const s of A.soldiers)
          if (s.role === 'inf' && s.file <= 1) {
            const n = 1 + this.rnd.next(3);
            for (let i = 0; i < n; i++) s.hits.push(lerp(TL.contact + 0.05, TL.melee1 - 0.1, this.rnd.nextDouble()));
          }
    }
    specialT() { return TL.contact + 0.02; }

    planEvents() {
      const ev = [];
      const add = (t, fn) => ev.push({ t, fn });
      add(0.02, () => sfx('horn', 0.35));
      add(0.12, () => sfx('march', 0.35));
      for (const r of this.arrows) add(r.land, () => this.arrowImpact(r));
      add(TL.contact, () => this.onContact());
      if (this.special) add(this.specialT(), () => this.specialImpact());
      for (const A of this.armies)
        for (const s of A.soldiers) if (isFinite(s.fallT)) add(s.fallT, () => this.onFall(A, s));
      for (const ch of this.chunks) add(ch.t, () => this.onChunk(ch));
      for (let t = TL.contact + 0.1; t < TL.melee1 - 0.05; t += 0.13 + this.rnd.nextDouble() * 0.08) add(t, () => sfx(this.rnd.nextDouble() < 0.6 ? 'duel' : 'hit', 0.22 + this.rnd.nextDouble() * 0.15));
      for (const A of this.armies) if (A.routed) add(TL.melee1 + 0.05, () => this.onRout(A));
      ev.sort((a, b) => a.t - b.t);
      this.events = ev;
    }

    // ---------------------------------------------------------- 动作函数 --
    // 士兵在时刻 t 的位置（不含倒地后的位移）
    soldierXZ(A, s, t, o) {
      const dir = A.dir;
      let x = s.x0, z = s.z0;
      x -= dir * ENTER_DX * (1 - eo(seg(t, s.ed, TL.march1 + s.ed * 0.5)));
      if (s.role !== 'arch') {
        const uc = seg(t, TL.charge0 + s.cd, TL.contact + s.cd * 0.35);
        x += dir * (A.chargeDX + s.push) * uc * uc;
      }
      const jit = s.role === 'arch' ? 0 : seg(t, TL.contact, TL.contact + 0.18) * (1 - seg(t, TL.melee1, TL.melee1 + 0.25));
      const engage = s.file <= 1 ? 1 : 0.4;
      x += dir * jit * engage * (Math.sin(t * s.w1 + s.p1) * 0.13 + Math.sin(t * s.w2 + s.p2) * 0.06);
      z += jit * Math.sin(t * s.w3 + s.p3) * 0.09;
      if (A.routed) {
        const uf = seg(t, TL.melee1 + s.rd, TL.regroup1 + 0.25);
        x -= dir * 8 * Math.pow(uf, 1.4);
      } else if (s.role !== 'arch') {
        x -= dir * 0.85 * eio(seg(t, TL.melee1 + s.rd, TL.regroup1 - 0.04));
      }
      o.x = x; o.z = z;
      o.jit = jit;
    }
    // 步态：相位（腿摆动）、幅度、身体前倾
    gait(A, s, t, o) {
      const segs = s.role === 'arch'
        ? [[s.ed, TL.march1 + s.ed * 0.5, 11, 0.5, -0.05]]
        : [[s.ed, TL.march1 + s.ed * 0.5, 11, 0.5, -0.05],
          [TL.charge0 + s.cd, TL.contact + s.cd * 0.35, 18, 0.75, -0.3],
          [TL.contact, TL.melee1, 6, 0.2, -0.1],
          A.routed ? [TL.melee1 + s.rd, TL.regroup1 + 0.3, 18, 0.75, -0.3] : [TL.melee1 + s.rd, TL.regroup1 - 0.04, 10, 0.36, 0.07]];
      if (s.role === 'arch' && A.routed) segs.push([TL.melee1 + s.rd, TL.regroup1 + 0.3, 18, 0.75, -0.3]);
      let ph = 0, amp = 0, lean = 0, wsum = 0;
      for (const g of segs) {
        ph += g[2] * clamp(t - g[0], 0, g[1] - g[0]);
        const w = trap(t, g[0], g[1], 0.1);
        amp = Math.max(amp, w * g[3]);
        lean += w * g[4]; wsum += w;
      }
      o.ph = ph + s.p0; o.amp = amp; o.lean = wsum > 0 ? lean / Math.max(1, wsum) : 0;
    }

    groundY(x, z) {
      return this.h(x, z);
    }

    // 按时刻 t 摆出全部士兵、武将、箭（纯函数式：跳过时直接摆出结束姿态）
    pose(t) {
      const o = {}, g = {};
      for (const A of this.armies) {
        const yaw0 = A.dir > 0 ? -Math.PI / 2 : Math.PI / 2;
        for (const s of A.soldiers) {
          const dead = t >= s.fallT;
          const tt = dead ? s.fallT : t;
          this.soldierXZ(A, s, tt, o);
          this.gait(A, s, tt, g);
          let x = o.x, z = o.z, lift = 0;
          const fk = dead ? Math.pow(seg(t, s.fallT, s.fallT + (s.fallKind === 'blast' ? 0.5 : 0.34)), 2) : 0;
          let spin = 0;
          if (dead && s.fallKind === 'blast') {
            const u = seg(t, s.fallT, s.fallT + 0.5);
            x -= A.dir * s.kb * eo(u);       // 被震飞：朝本方后方
            lift += 4 * 1.1 * u * (1 - u);
            spin = (1 - u) * u * 4 * 1.6 * (s.p0 > 3.1 ? 1 : -1);
          }
          let y = this.groundY(x, z);
          const bob = Math.abs(Math.sin(g.ph)) * 0.035 * (g.amp / 0.75);
          // 朝向：溃逃时转身
          let yaw = yaw0;
          if (A.routed) yaw += Math.PI * sm(seg(tt, TL.melee1 + s.rd, TL.melee1 + s.rd + 0.2)) * (s.p0 > 3.1 ? 1 : -1);
          yaw += o.jit * Math.sin(tt * s.w4 + s.p4) * 0.28;
          let pitch = g.lean + (o.jit ? Math.sin(tt * s.w2 + s.p1) * 0.08 * o.jit : 0);
          // 胜方欢呼：收队时轻跳
          let cheer = 0;
          if (A.cheer && !dead) cheer = seg(t, TL.melee1 + 0.15, TL.melee1 + 0.3);
          const jump = cheer ? Math.max(0, Math.sin((t - TL.melee1) * 13 + s.p0)) * 0.12 * cheer : 0;
          if (dead) {
            pitch = lerp(pitch, s.fallDir * Math.PI / 2 * 0.96, fk);
            lift += 0.1 * fk;
            if (fk >= 1) pitch += Math.sin(Math.min(1, (t - s.fallT - 0.34) * 6) * Math.PI) * 0.05 * s.fallDir;
          }
          // 身体矩阵 B = T · Ry(yaw) · Rx(pitch)
          _e.set(pitch, yaw, spin, 'YXZ');
          _q.setFromEuler(_e);
          _m.compose(_p.set(x, y + lift + (dead ? 0 : bob + jump), z), _q, _one);
          const body = s.role === 'arch' ? A.archMesh : A.infMesh;
          body.setMatrixAt(s.bodyIdx, _m);
          // 腿
          const swing = dead ? 0.15 * s.fallDir : Math.sin(g.ph) * g.amp;
          for (let k = 0; k < 2; k++) {
            _q2.setFromAxisAngle(_X, k ? -swing : swing);
            _m2.compose(_p.set(k ? 0.065 : -0.065, HIP, 0), _q2, _one);
            _m2.premultiply(_m);
            A.legMesh.setMatrixAt(s.legIdx + k, _m2);
          }
          // 兵器
          let wp, thrust = 0;
          if (s.role === 'arch') {
            const rel = A.volleyT + s.shotDelay;
            const up = sm(seg(tt, TL.aim + s.ed * 0.2, TL.aim + 0.16));
            const down = sm(seg(tt, rel + 0.12, rel + 0.5));
            wp = 0.95 * up * (1 - down) + 0.12 * down - 0.1 * (1 - up) - Math.max(0, 1 - Math.abs(tt - rel) * 12) * 0.12;
            if (A.routed) wp = lerp(wp, 0.5, seg(tt, TL.melee1, TL.melee1 + 0.3));
          } else if (s.role === 'flag') {
            wp = Math.sin(tt * 2.3 + s.p0) * 0.05 - 0.08 * seg(tt, TL.charge0, TL.contact);
          } else {
            wp = Math.sin(tt * 2.1 + s.p0) * 0.04 - 0.06;
            const lower = sm(seg(tt, TL.charge0 - 0.12 + s.cd * 0.4, TL.charge0 + 0.14 + s.cd * 0.4));
            wp = lerp(wp, -1.42, lower);
            if (o.jit > 0) {
              const eng = s.file <= 1 ? 1 : 0.35;
              wp += o.jit * Math.sin(tt * s.w4 + s.pt) * 0.16;
              thrust = o.jit * eng * Math.pow(Math.max(0, Math.sin(tt * s.wt + s.pt)), 3) * 0.34;
            }
            const back = seg(tt, TL.melee1 + s.rd, TL.regroup1);
            if (A.routed) wp = lerp(wp, 0.55, sm(back));
            else if (A.cheer) wp = lerp(wp, -0.12 + Math.sin(t * 9 + s.p0) * 0.12, sm(back));
            else wp = lerp(wp, -0.95, sm(back));
          }
          // 倒地后兵器顺着身体躺平
          if (dead) { wp = lerp(wp, s.role === 'arch' ? 0.3 : 0.12 * s.fallDir, fk); thrust = 0; }
          _q2.setFromAxisAngle(_X, wp);
          _m2.compose(_p.set(GRIP.x, GRIP.y, GRIP.z), _q2, _one);
          if (thrust) { _m.multiply(_m2); _m2.makeTranslation(0, thrust, 0); _m.multiply(_m2); _m2.copy(_m); }
          else _m2.premultiply(_m);
          (s.role === 'arch' ? this.bowMesh : this.spearMesh).setMatrixAt(s.wIdx, _m2);
          if (s.flag) {
            _m.compose(_p.set(0, 0.82, 0), _q.identity(), _one);
            _m.premultiply(_m2);
            s.flag.mesh.matrix.copy(_m);
            s.flag.mesh.matrixWorldNeedsUpdate = true;
            s.flag.k = dead ? 0.15 : 0.75 + g.amp * 0.8;
          }
          // 颜色：受击闪白、阵亡变暗
          let fl = 0;
          if (!dead) for (const ht of s.hits) { const d = t - ht; if (d >= 0 && d < 0.16) fl = Math.max(fl, 1 - d / 0.16); }
          if (dead) fl = Math.max(fl, 1 - (t - s.fallT) / 0.18);
          const darkK = dead ? lerp(1, 0.52, seg(t, s.fallT + 0.1, s.fallT + 0.7)) : 1;
          const ck = darkK * (1 + Math.max(0, fl) * 0.75);
          _c.setRGB(ck, ck, ck);
          body.setColorAt(s.bodyIdx, _c);
          A.legMesh.setColorAt(s.legIdx, _c); A.legMesh.setColorAt(s.legIdx + 1, _c);
          (s.role === 'arch' ? this.bowMesh : this.spearMesh).setColorAt(s.wIdx, _c);
        }
        this.poseGeneral(A, t);
      }
      this.poseArrows(t);
      for (const m of this.instanced) {
        m.instanceMatrix.needsUpdate = true;
        if (m.instanceColor && m !== this.horseLegs) m.instanceColor.needsUpdate = true;
      }
    }

    poseGeneral(A, t) {
      const G = A.general, dir = A.dir;
      const x = this.generalX(A, t);
      const z = G.z0;
      const y = this.groundY(x, z);
      // 步态：入场与冲锋时小跑，溃逃时疾驰
      const moving = trap(t, 0.08, TL.march1 + 0.05, 0.12) * 0.8 + trap(t, TL.charge0 + 0.1, TL.contact + 0.1, 0.12) * 0.6
        + (A.routed ? trap(t, TL.melee1 + 0.12, TL.regroup1 + 0.5, 0.1) : trap(t, TL.melee1 + 0.1, TL.regroup1, 0.1) * 0.4);
      const ph = t * 13 + G.p0;
      const rear = (A.routed ? 0 : 1) * Math.sin(Math.PI * seg(t, TL.contact + 0.02, TL.contact + 0.6)) * 0.36;
      let yaw = dir > 0 ? -Math.PI / 2 : Math.PI / 2;
      if (A.routed) yaw += Math.PI * sm(seg(t, TL.melee1 + 0.12, TL.melee1 + 0.35));
      const pitch = rear + Math.sin(ph * 2) * 0.03 * moving;
      _e.set(pitch, yaw, 0, 'YXZ');
      _q.setFromEuler(_e);
      // 扬蹄时绕后蹄抬起（后蹄在局部 z = +0.4）
      const lift = Math.abs(Math.sin(ph)) * 0.05 * moving + Math.sin(rear) * 0.4;
      _m.compose(_p.set(x, y + lift, z), _q, _one);
      _m2.makeScale(1.12, 1.12, 1.12);
      _m.multiply(_m2);
      G.mesh.matrix.copy(_m);
      G.mesh.matrixWorldNeedsUpdate = true;
      G.x = x; G.y = y; G.z = z;
      // 马腿（前后腿交替）
      const legs = [[-0.1, 0.56, -0.36, 0], [0.1, 0.56, -0.36, 0.6], [-0.1, 0.56, 0.38, 2.6], [0.1, 0.56, 0.38, 3.3]];
      legs.forEach((L, i) => {
        let sw = Math.sin(ph + L[3]) * 0.55 * moving;
        if (rear > 0.05 && i < 2) sw = -0.9 * rear / 0.36 + Math.sin(t * 20 + i) * 0.15;
        _q2.setFromAxisAngle(_X, sw);
        _m2.compose(_p.set(L[0], L[1], L[2]), _q2, _one);
        _m2.premultiply(_m);
        this.horseLegs.setMatrixAt(A.si * 4 + i, _m2);
      });
      // 帅旗：旗杆顶端横杆下（局部 Unity (-0.18, 2.86, -0.3) → three (-0.18, 2.86, 0.3)）
      _m2.compose(_p.set(-0.18, 2.84, 0.32), _q.identity(), _one);
      _m2.premultiply(_m);
      G.flag.mesh.matrix.copy(_m2);
      G.flag.mesh.matrixWorldNeedsUpdate = true;
      G.flag.k = 0.85 + moving * 0.7;
    }

    poseArrows(t) {
      const m = this.arrowMesh;
      if (!m) return;
      this.arrows.forEach((r, i) => {
        if (t < r.t0 || (r.hit && t >= r.land)) { m.setMatrixAt(i, HIDE); return; }
        if (!r.ready) this.aimArrow(r);
        const u = Math.min(1, (t - r.t0) / r.T);
        _v1.lerpVectors(r.p0, r.p1, u);
        _v1.y += r.apex * 4 * u * (1 - u);
        _v2.subVectors(r.p1, r.p0);
        _v2.y += r.apex * 4 * (1 - 2 * u);
        _v2.normalize();
        _q.setFromUnitVectors(_fwd, _v2);
        if (u >= 1) _v1.addScaledVector(_v2, r.water ? 0.4 : 0.14);   // 插入地面
        if (u >= 1 && r.water) { m.setMatrixAt(i, HIDE); return; }
        _m.compose(_v1, _q, _one);
        m.setMatrixAt(i, _m);
      });
    }
    aimArrow(r) {
      r.ready = true;
      const o = {};
      this.soldierXZ(r.S, r.archer, r.t0, o);
      r.p0.set(o.x + r.S.dir * 0.25, this.groundY(o.x, o.z) + 0.95, o.z);
      if (r.target) {
        this.soldierXZ(r.E, r.target, r.land, o);
        r.p1.set(o.x, this.groundY(o.x, o.z) + 0.55, o.z);
        r.hit = true;
      } else {
        // 落在敌阵附近
        const E = r.E;
        let cx = 0, n = 0;
        for (const s of E.soldiers) { this.soldierXZ(E, s, r.land, o); cx += o.x; n++; }
        cx /= Math.max(1, n);
        const x = cx + (this.rnd.nextDouble() - 0.5) * 3.2, z = ZC + (this.rnd.nextDouble() - 0.5) * 4.4;
        r.p1.set(x, this.groundY(x, z), z);
        r.water = this.kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w + 0.6;
      }
      const d = Math.abs(r.p1.x - r.p0.x);
      r.apex = Math.min(2.5, 1.6 + d * 0.22);     // 弧顶不进入画面上方的 HUD 带
    }

    // ---------------------------------------------------------- 特效 --
    emit(pool, pos, n, o) {
      const rnd = this.rnd;
      for (let i = 0; i < n; i++) {
        let px, py, pz, vx, vy, vz;
        if (o.box) {
          px = pos.x + (rnd.nextDouble() - 0.5) * o.box.x; py = pos.y + (rnd.nextDouble() - 0.5) * o.box.y; pz = pos.z + (rnd.nextDouble() - 0.5) * o.box.z;
          const sp = o.speed * (0.55 + rnd.nextDouble() * 0.6), j = o.jitter === undefined ? 0.25 : o.jitter;
          vx = (rnd.nextDouble() - 0.5) * 2 * j + (o.vx || 0); vz = (rnd.nextDouble() - 0.5) * 2 * j; vy = sp;
        } else {
          let x, y, z, d;
          do { x = rnd.nextDouble() * 2 - 1; y = rnd.nextDouble() * 2 - 1; z = rnd.nextDouble() * 2 - 1; d = x * x + y * y + z * z; } while (d > 1 || d < 1e-6);
          d = Math.sqrt(d);
          if (o.up) y = Math.abs(y);
          px = pos.x + x * o.radius; py = pos.y + y * o.radius; pz = pos.z + z * o.radius;
          const sp = o.speed * (o.speedVar ? 1 - o.speedVar * rnd.nextDouble() : 1);
          vx = x / d * sp + (o.vx || 0); vy = y / d * sp + (o.vy || 0); vz = z / d * sp;
        }
        let r = o.color.r, g = o.color.g, b = o.color.b;
        if (o.color2 && rnd.nextDouble() < (o.mix === undefined ? 0.35 : o.mix)) { r = o.color2.r; g = o.color2.g; b = o.color2.b; }
        const ce = o.colorEnd;
        pool.add({
          x: px, y: py, z: pz, vx, vy, vz, age: 0,
          r1: ce ? ce.r : r, g1: ce ? ce.g : g, b1: ce ? ce.b : b,
          life: o.life * (o.lifeVar ? 1 - o.lifeVar * rnd.nextDouble() : 1),
          size: o.size * (o.sizeVar ? 1 - o.sizeVar * rnd.nextDouble() : 1), sizeEnd: o.sizeEnd === undefined ? 0.3 : o.sizeEnd,
          r, g, b, a: o.alpha === undefined ? 1 : o.alpha, grav: o.gravity || 0, drag: o.drag || 0, fadeIn: o.fadeIn || 0,
          ac: o.alphaCurve || 1, floor: o.floor,
        });
      }
    }
    dustAt(x, z, k, big) {
      const y = this.groundY(x, z);
      const wet = this.kind === 'river' && y < RIVER.surf - 0.05;
      if (wet) {
        this.emit(this.fx, V(x, RIVER.surf + 0.03, z), big ? 10 : 4, {
          color: C(0.97, 0.99, 1), color2: C(0.78, 0.9, 0.96), alpha: 0.95 * k, life: 0.6, speed: big ? 2.6 : 1.9, size: 0.2, sizeEnd: 0.55,
          gravity: 0.85, radius: 0.15, up: true, lifeVar: 0.3, speedVar: 0.45,
        });
        this.emit(this.fx, V(x, RIVER.surf + 0.04, z), 1, { color: C(0.9, 0.96, 1), alpha: 0.55 * k, life: 0.5, speed: 0, size: 0.6, sizeEnd: 2.2, radius: 0.05 });
        return;
      }
      const col = this.kind === 'forest' ? C(0.62, 0.57, 0.47) : (this.kind === 'gate' || this.kind === 'castle' || this.kind === 'wall') ? C(0.8, 0.74, 0.62) : C(0.8, 0.74, 0.6);
      this.emit(this.dust, V(x, y + 0.12, z), big ? 3 : 1, {
        color: col, colorEnd: shade(col, 0.12), alpha: 0.5 * k, life: big ? 1.2 : 0.8, speed: big ? 0.9 : 0.45, size: big ? 1.1 : 0.62, sizeEnd: big ? 2.6 : 2.0,
        gravity: -0.02, radius: big ? 0.4 : 0.2, up: true, lifeVar: 0.3, drag: 1.4, fadeIn: 0.08,
      });
    }
    sparks(pos, n, col) {
      this.emit(this.glow, pos, n, {
        color: col || C(1, 0.88, 0.5), color2: C(1, 1, 0.9), colorEnd: C(1, 0.4, 0.1), alpha: 1, life: 0.4, speed: 3.4, size: 0.15, sizeEnd: 0.35,
        gravity: 0.9, radius: 0.08, lifeVar: 0.4, speedVar: 0.5,
      });
      this.emit(this.glow, pos, 2, { color: col || C(1, 0.9, 0.62), alpha: 0.9, life: 0.16, speed: 0, size: 0.8, sizeEnd: 1.8, radius: 0.01 });
    }
    arrowImpact(r) {
      if (!r.ready) this.aimArrow(r);
      if (r.hit) {
        this.sparks(r.p1, 5, C(1, 0.85, 0.6));
        sfx('hit', 0.18);
      } else if (r.water) {
        this.emit(this.fx, V(r.p1.x, RIVER.surf + 0.02, r.p1.z), 5, {
          color: C(0.92, 0.96, 1), alpha: 0.85, life: 0.45, speed: 1.6, size: 0.1, sizeEnd: 0.4, gravity: 1, radius: 0.05, up: true, lifeVar: 0.3,
        });
      } else {
        this.emit(this.dust, V(r.p1.x, r.p1.y + 0.05, r.p1.z), 1, {
          color: C(0.62, 0.56, 0.44), alpha: 0.35, life: 0.6, speed: 0.4, size: 0.35, sizeEnd: 1.6, radius: 0.05, up: true, drag: 1.5,
        });
      }
    }
    contactLine(o) {
      const L = this.armies[0];
      let x = 0, n = 0;
      for (const s of L.soldiers) if (s.role === 'inf' && s.file === 0) { this.soldierXZ(L, s, this.t, o); x += o.x; n++; }
      return n ? x / n + 0.3 : this.xc;
    }
    onContact() {
      this.shake = 1;
      sfx('hit', 0.9); sfx('duel', 0.5);
      // 交锋乐句：跟随当前战斗曲的调与速度（audio.js 内部限 15 秒一次、「快」档用短版）
      if (!this.skipped) { try { if (SG.Sfx && SG.Sfx.stinger) SG.Sfx.stinger('clash', { speed: this.speed }); } catch (e) { /* 无音频 */ } }
      const o = {};
      const xc = this.contactLine(o);
      for (let i = 0; i < 9; i++) {
        const z = ZC + (this.rnd.nextDouble() - 0.5) * 4;
        this.dustAt(xc + (this.rnd.nextDouble() - 0.5) * 0.8, z, 1, true);
        if (i % 2 === 0) this.sparks(V(xc, this.groundY(xc, z) + 0.6 + this.rnd.nextDouble() * 0.4, z), 6);
      }
      this.hud.flash('rgba(255,244,214,.32)');
    }
    onFall(A, s) {
      const o = {};
      this.soldierXZ(A, s, s.fallT, o);
      this.dustAt(o.x - A.dir * 0.3, o.z, 0.9, false);
      if (s.fallKind === 'melee') this.sparks(V(o.x + A.dir * 0.25, this.groundY(o.x, o.z) + 0.6, o.z), 4);
    }
    onChunk(ch) {
      const A = this.armies[ch.si];
      this.hud.loss(ch.si, ch.amount);
      // 世界坐标飘字：在该部队中心上方
      const o = {};
      let x = 0, n = 0;
      for (const s of A.soldiers) if (!(this.t >= s.fallT + 0.6)) { this.soldierXZ(A, s, this.t, o); x += o.x; n++; }
      x = n ? x / n : A.xFront;
      const k = this.chunks.filter(c => c.si === ch.si && c.t < ch.t).length;
      x += (k % 2 ? 0.6 : -0.4) * A.dir;
      // 两军接战后中心会挤在一起：各自的飘字保持在接敌线本方一侧，免得数字叠在一起
      const xc = this.contactLine(o), gap = 1.35;
      x = A.dir > 0 ? Math.min(x, xc - gap) : Math.max(x, xc + gap);
      this.hud.pop(V(x, 2.0 + (k % 3) * 0.32 + ch.si * 0.18, ZC), '−' + ch.amount, ch.kind === 'special');
    }
    onRout(A) {
      sfx('lose', 0.3);
      this.hud.stamp(A.info.name + '部', '溃 散');
    }
    specialImpact() {
      const sp = this.special;
      if (!sp) return;
      const col = SG.Gfx.color(sp.color);
      const D = this.armies[1];
      const o = {};
      let cx = 0, n = 0;
      for (const s of D.soldiers) { this.soldierXZ(D, s, this.t, o); cx += o.x; n++; }
      cx = n ? cx / n : D.xFront;
      this.shake = 1.4;
      this.hud.flash(sp.color, 0.45);
      sfx('magic', 0.7); sfx('hit', 0.9);
      const k = sp.kind;
      if (k === 'blaze') {
        for (let i = 0; i < 16; i++) {
          const x = cx + (this.rnd.nextDouble() - 0.5) * 3.6, z = ZC + (this.rnd.nextDouble() - 0.5) * 3.8, y = this.groundY(x, z);
          this.emit(this.fx, V(x, y + 0.25, z), 10, {
            color: C(1, 0.66, 0.16), color2: C(1, 0.86, 0.4), colorEnd: C(0.7, 0.12, 0.03), alpha: 1, life: 1.1, speed: 1.8, size: 0.95, sizeEnd: 0.3,
            gravity: -0.25, box: { x: 0.8, y: 0.2, z: 0.8 }, jitter: 0.3, lifeVar: 0.35, alphaCurve: 2,
          });
          this.emit(this.glow, V(x, y + 0.4, z), 3, { color: C(1, 0.6, 0.2), alpha: 0.7, life: 0.7, speed: 0.6, size: 1.2, sizeEnd: 0.4, radius: 0.3, gravity: -0.2 });
        }
        this.emit(this.dust, V(cx, 1.6, ZC), 14, { color: C(0.2, 0.18, 0.16), alpha: 0.45, life: 1.8, speed: 0.9, size: 1.2, sizeEnd: 2.6, radius: 1.4, up: true, gravity: -0.04, drag: 0.4, fadeIn: 0.15 });
        return;
      }
      if (k === 'storm') {
        for (let b = 0; b < 3; b++) this.bolt(V(cx + (b - 1) * 1.1 + (this.rnd.nextDouble() - 0.5) * 0.6, 0, ZC + (this.rnd.nextDouble() - 0.5) * 2.4), col, b * 0.07);
        return;
      }
      if (k === 'volley') {
        for (let i = 0; i < 6; i++) this.sparks(V(cx + (this.rnd.nextDouble() - 0.5) * 3, 0.6, ZC + (this.rnd.nextDouble() - 0.5) * 3), 5, shade(col, 0.4));
        return;
      }
      // 默认：巨大的弧形斩光 + 地面冲击波
      const xc = this.contactLine(o);
      this.slash(V(xc - 0.4, 1.1, ZC + 0.9), col);
      this.shockwave(V(xc + 0.9, this.groundY(xc + 0.9, ZC) + 0.06, ZC), col);
      for (let i = 0; i < 6; i++) this.sparks(V(xc + 0.4 + this.rnd.nextDouble() * 1.6, 0.5 + this.rnd.nextDouble() * 0.8, ZC + (this.rnd.nextDouble() - 0.5) * 3.4), 7, shade(col, 0.5));
    }
    slash(pos, col) {
      const mk = (r0, r1, c, op) => {
        const g = new THREE.RingGeometry(r0, r1, 28, 1, -0.95, 1.9);
        const m = fxMaterial('ring');
        m.color.copy(c); m.opacity = op;
        const mesh = new THREE.Mesh(g, m);
        mesh.position.copy(pos);
        mesh.rotation.set(0, 0, -0.35);
        mesh.renderOrder = 15;
        this.scene.add(mesh);
        return mesh;
      };
      const halo = mk(1.6, 3.3, col.clone(), 0.35), outer = mk(2.15, 3.0, col.clone(), 0.85), inner = mk(2.55, 2.82, C(1, 1, 0.95), 0.95);
      for (const [mesh, k] of [[halo, 0.98], [outer, 1], [inner, 1.05]]) {
        this.temp.push({
          mesh, t0: this.t, life: 0.42, fn: (u) => {
            const s = 0.55 + eo(u) * 0.75 * k;
            mesh.scale.set(s, s, 1);
            mesh.rotation.z = -0.35 - u * 0.9;
            mesh.material.opacity = (1 - u * u) * (k > 1 ? 0.95 : k < 1 ? 0.35 : 0.85);
          },
        });
      }
    }
    shockwave(pos, col) {
      const g = new THREE.PlaneGeometry(1, 1);
      const m = fxMaterial('shock');
      m.color.copy(col); m.opacity = 0.9;
      const mesh = new THREE.Mesh(g, m);
      mesh.rotation.x = -Math.PI / 2;
      mesh.position.copy(pos);
      mesh.renderOrder = 13;
      this.scene.add(mesh);
      this.temp.push({ mesh, t0: this.t, life: 0.55, fn: (u) => { const s = 1 + eo(u) * 7; mesh.scale.set(s, s, 1); m.opacity = 0.9 * (1 - u); } });
    }
    bolt(base, col, delay) {
      const mb = new SG.MeshBuilder();
      // 雷从 HUD 带之下的一团云光中劈下（起点不高于约 5.4，免得像是从 HUD 里钻出来）
      const top = 5.4;
      let x = base.x, z = base.z, y = top;
      const rnd = this.rnd;
      while (y > this.groundY(base.x, base.z)) {
        const nx = base.x + (rnd.nextDouble() - 0.5) * 0.9, ny = Math.max(this.groundY(base.x, base.z), y - 0.6 - rnd.nextDouble() * 0.5), nz = z + (rnd.nextDouble() - 0.5) * 0.3;
        beam(mb, V(x, y, z), V(nx, ny, nz), 0.12, 0.1, C(1, 1, 1));
        x = nx; y = ny; z = nz;
        if (ny <= this.groundY(base.x, base.z) + 0.01) break;
      }
      const m = fxMaterial('bolt');
      m.color.copy(shade(col, 0.55)); m.opacity = 0;
      const mesh = new THREE.Mesh(mb.toGeometry(), m);
      mesh.renderOrder = 15;
      this.scene.add(mesh);
      this.temp.push({
        mesh, t0: this.t + delay, life: 0.38, fn: (u) => {
          m.opacity = u < 0 ? 0 : (1 - u) * (0.75 + 0.25 * Math.sin(u * 60));
          if (u > 0 && !mesh.userData.hit) {
            mesh.userData.hit = true;
            this.emit(this.glow, V(base.x, top + 0.2, base.z), 5, { color: shade(col, 0.6), alpha: 0.85, life: 0.4, speed: 0.5, size: 2.6, sizeEnd: 4.2, radius: 0.7, gravity: 0 });
            this.sparks(V(base.x, this.groundY(base.x, base.z) + 0.2, base.z), 12, shade(col, 0.4)); this.dustAt(base.x, base.z, 1, true);
          }
        },
      });
    }

    // ---------------------------------------------------------- 镜头 --
    cameraPose(t) {
      const xc = this.xc;
      // 注视点比两军略高一点：画面上方约 1/6 被 HUD 占去，让军阵落在 HUD 之下的画面中部
      const K = [
        { t: 0, p: [-1.1, 4.0, 18.4], l: [0.2, 2.05, -0.8] },
        { t: 0.7, p: [-0.4, 2.55, 15.0], l: [0, 2.1, -0.8] },
        { t: TL.charge0, p: [-0.3, 2.5, 14.6], l: [0, 2.05, -0.8] },
        { t: TL.contact, p: [xc * 0.45 + 0.2, 2.18, 11.9], l: [xc * 0.5, 1.78, -0.8] },
        { t: TL.melee1, p: [xc * 0.45 + 0.5, 2.1, 11.4], l: [xc * 0.5 + 0.2, 1.72, -0.8] },
        { t: TL.regroup1, p: [xc * 0.3 + 0.2, 2.5, 13.8], l: [xc * 0.3, 2.02, -0.8] },
      ];
      let i = 0;
      while (i < K.length - 2 && t > K[i + 1].t) i++;
      const a = K[i], b = K[i + 1];
      const u = sm(seg(t, a.t, b.t));
      const lx0 = lerp(a.l[0], b.l[0], u), ly = lerp(a.l[1], b.l[1], u), lz = lerp(a.l[2], b.l[2], u);
      const dx = lerp(a.p[0], b.p[0], u) - lx0, dy = lerp(a.p[1], b.p[1], u) - ly, dz = lerp(a.p[2], b.p[2], u) - lz;
      const cam = this.camera;
      const aspect = cam.aspect || 1.78;
      // 横向取景：注视点向两员武将（含帅旗）的中点靠一半；施展必杀时再偏向攻方武将
      const ext = this.armyExtent(t);
      let lx = lerp(lx0, (ext.l + ext.r) / 2, 0.5);
      if (this.special) lx = lerp(lx, ext.gl, 0.22 * trap(t, TL.charge0 - 0.3, TL.melee1, 0.4));
      // 推近不得把两军（尤其两端的武将与帅旗）推出画面：按实际军阵外缘求最小拉远倍数（左右各留约 4%）
      const tanH = Math.tan(cam.fov * M.deg2rad / 2) * aspect * 0.92;
      const need = this.viewNeed(t, lx, lz, dz, tanH);
      const fit = Math.max(this.fit || 1, need);
      // 竖屏：拉远之后再抬高机位俯视，压低天空、让纵深铺开
      const kP = clamp((1.15 - aspect) / 0.6, 0, 1);
      const dist = Math.hypot(dx, dy, dz) * fit;
      cam.position.set(lx + dx * fit, ly + dy * fit + kP * dist * 0.27, lz + dz * fit);
      if (this.shake > 0.001 && !this.calm) {
        const k = this.shake * 0.11;
        cam.position.x += Math.sin(this.real * 71) * k;
        cam.position.y += Math.sin(this.real * 57 + 1.3) * k * 0.7;
      }
      let lookY = ly - kP * 0.35;
      cam.lookAt(lx, lookY, lz);
      // 城门 / 本城：城门楼屋脊不得顶进上方 HUD（宽屏、手机上会被遮成「无顶红楼」）——
      // 投影屋脊，若高于 HUD 下沿就抬高注视点（画面整体下移，脚下空地有余量），最多抬 1.6
      if (this.kind === 'gate' || this.kind === 'castle') {
        const hb = this.hudLineNdc();
        if (hb < 0.98) {
          const tanV = Math.tan(cam.fov * M.deg2rad / 2);
          const dl = Math.hypot(cam.position.x - lx, cam.position.y - lookY, cam.position.z - lz);
          let up = 0;
          for (let it = 0; it < 4; it++) {
            cam.updateMatrixWorld();
            const y = _v2.set(GATE_X, 6.2, WALL_Z - 0.7).project(cam).y;
            const over = y - (hb - 0.03);
            if (over <= 0.004 || up >= 1.6) break;
            up = Math.min(1.6, up + over * tanV * dl * 0.9);
            cam.lookAt(lx, lookY + up, lz);
          }
        }
      }
      // 雾随机位远近平移，拉远时军阵不被雾吞掉
      const fog = this.scene && this.scene.fog;
      if (fog) { const extra = Math.max(0, dist - 19); fog.near = 34 + extra; fog.far = 135 + extra; }
    }
    // HUD 顶栏下沿在 NDC 中的 y（1 = 画面顶）；按视口尺寸缓存，避免每帧强制排版
    hudLineNdc() {
      const W = SG.Gfx.width || window.innerWidth, H = SG.Gfx.height || window.innerHeight, key = W + 'x' + H;
      if (this._hbKey === key) return this._hb;
      const bar = this.hud && this.hud.bar;
      const r = bar && bar.getBoundingClientRect ? bar.getBoundingClientRect() : null;
      if (!r || !r.height || !H) return 1;      // 尚未排版：不缓存，下帧再量
      const cv = SG.Gfx.renderer && SG.Gfx.renderer.domElement && SG.Gfx.renderer.domElement.getBoundingClientRect ? SG.Gfx.renderer.domElement.getBoundingClientRect() : { top: 0, height: H };
      this._hbKey = key;
      this._hb = 1 - 2 * (r.bottom - cv.top) / (cv.height || H);
      return this._hb;
    }
    // 武将 x（与 poseGeneral 同一公式；镜头取景用）
    generalX(A, t) {
      const G = A.general, dir = A.dir;
      let x = G.x0 - dir * ENTER_DX * (1 - eo(seg(t, G.ed, TL.march1 + 0.05)));
      const uc = seg(t, TL.charge0 + 0.1, TL.contact + 0.1);
      x += dir * A.genDX * uc * uc;
      if (A.routed) x -= dir * 9 * Math.pow(seg(t, TL.melee1 + 0.12, TL.regroup1 + 0.3), 1.4);
      else x -= dir * 0.45 * eio(seg(t, TL.melee1 + 0.1, TL.regroup1));
      return x;
    }
    // 取景用的时刻：入场时按列阵位置（让队伍走进画面），溃逃时停在溃散前（不追着逃兵拉远）
    frameT(A, t) { return clamp(t, TL.march1, A.routed ? TL.melee1 + 0.12 : TL.end); }
    armyExtent(t) {
      const [L, R] = this.armies;
      const gl = this.generalX(L, this.frameT(L, t)), gr = this.generalX(R, this.frameT(R, t));
      return { l: gl - 1.75, r: gr + 1.75, gl, gr };
    }
    viewNeed(t, cx, lz, dz, tanH) {
      if (!(dz > 0.1) || !(tanH > 0.01)) return 1;
      let need = 0;
      const o = this._fo || (this._fo = {});
      const test = (x, z) => { const n = (Math.abs(x - cx) / tanH + (z - lz)) / dz; if (n > need) need = n; };
      for (const A of this.armies) {
        const tt = this.frameT(A, t), gx = this.generalX(A, tt), gz = A.general.z0;
        test(gx - A.dir * 1.75, gz - 0.25);     // 帅旗（含旗面飘动）在武将身后
        test(gx + A.dir * 1.0, gz);             // 马头
        for (const s of A.soldiers) { this.soldierXZ(A, s, tt, o); test(o.x - A.dir * 0.3, o.z + 0.25); }
      }
      return need;
    }
    resize(w, h) {
      const aspect = w / Math.max(1, h);
      this.camera.aspect = aspect;
      // 以 16:9 为基准：更窄的屏幕拉远，更宽的屏幕（手机横屏）适当推近；两军外缘的约束见 viewNeed
      this.fit = clamp(1.78 / aspect, 0.86, 1.75);
      this.camera.updateProjectionMatrix();
    }

    // ---------------------------------------------------------- 每帧 --
    update(dt) {
      this.lastHost = performance.now();
      this.advance(dt);
    }
    advance(dt) {
      if (this.state === 'done') return;
      if (!(dt >= 0)) dt = 0;
      dt = Math.min(dt, 0.1);
      this.real += dt;
      const dbg = SG.Clash.debug;
      const tBefore = this.t, wasRun = this.state === 'run';
      if (this.state === 'run' && !this.paused) {
        let t = this.t;
        const k = (t >= TL.contact && t < TL.contact + TL.slowW) ? TL.slowK : 1;
        t += dt * this.speed * k;
        if (dbg && typeof dbg.freezeAt === 'number') t = Math.min(t, dbg.freezeAt);
        if (this.special && !this.cutDone && t >= TL.charge0 - 0.1) {
          t = TL.charge0 - 0.1;
          this.cutDone = true;
          this.paused = true;
          const sp = this.special;
          SG.Clash.cutIn({ gen: this.A.gen, name: sp.name, color: sp.color, side: this.A.side, cry: sp.cry, speed: Math.max(0.1, +this.opts.speed || 1), _inClash: true }).then(() => { this.paused = false; });
        }
        this.setTime(t, true);
        if (this.t >= TL.end) this.finish();
      } else if (this.state === 'hold') {
        // 帧率很低时（dt 被截断）也不让跳过后的停留超过 SKIP_HOLD 实际秒数
        if (this.real >= this.holdUntil || performance.now() >= this.holdWall) this.finish();
      }
      this.shake = Math.max(0, this.shake - dt * 3.2);
      this.cameraPose(this.t);
      // 粒子、旗帜、临时特效：播放中随时间轴推进（慢镜头、特写定格、调试冻结时一并停住）
      const step = wasRun ? Math.max(0, this.t - tBefore) : dt * this.speed;
      const scale = this.pointScale();
      this.dust.update(step, scale); this.fx.update(step, scale); this.glow.update(step, scale);
      for (const A of this.armies) {
        A.general.flag.wave(this.real * Math.min(1.6, this.speed), A.general.flag.k || 1);
        for (const s of A.soldiers) if (s.flag) s.flag.wave(this.real * Math.min(1.6, this.speed) + s.p0, s.flag.k || 1);
      }
      for (let i = this.temp.length - 1; i >= 0; i--) {
        const f = this.temp[i];
        const u = (this.t - f.t0) / f.life;
        if (u >= 1 || this.state !== 'run') { this.scene.remove(f.mesh); f.mesh.geometry.dispose(); releaseFx(f.mesh.material); this.temp.splice(i, 1); continue; }
        f.fn(u);
      }
      if (this.hud) this.hud.update(dt, this, this.state === 'run' ? (this.t - this._tPrev) / this.speed : dt);
      this._tPrev = this.t;
    }
    // 推进时间轴并触发途经的事件（fx = false 时只更新状态，不放特效）
    setTime(t, fx) {
      const prev = this.t;
      this.t = t;
      while (this.evIdx < this.events.length && this.events[this.evIdx].t <= t) {
        const e = this.events[this.evIdx++];
        if (fx) { try { e.fn(); } catch (err) { console.error(err); } }
      }
      if (fx && !this.paused) this.ambient(prev, t);
      this.pose(t);
    }
    // 持续的扬尘、溅水与火花
    ambient(t0, t1) {
      const dt = t1 - t0;
      if (dt <= 0) return;
      const rnd = this.rnd, o = {};
      for (const A of this.armies)
        for (const s of A.soldiers) {
          if (t1 >= s.fallT) continue;
          const charging = s.role !== 'arch' && t1 > TL.charge0 + s.cd && t1 < TL.contact + 0.05;
          const marching = t1 < TL.march1;
          const fleeing = A.routed && t1 > TL.melee1;
          const rate = charging ? 5 : fleeing ? 4 : marching ? 0.9 : 0;
          if (rate && rnd.nextDouble() < rate * dt) { this.soldierXZ(A, s, t1, o); this.dustAt(o.x - A.dir * 0.2, o.z, charging ? 0.8 : 0.5, false); }
        }
      if (t1 > TL.contact && t1 < TL.melee1) {
        const xc = this.contactLine(o);
        const sp = this.special ? shade(SG.Gfx.color(this.special.color), 0.3) : null;
        if (rnd.nextDouble() < 16 * dt) {
          const z = ZC + (rnd.nextDouble() - 0.5) * 4;
          this.sparks(V(xc + (rnd.nextDouble() - 0.5) * 0.8, this.groundY(xc, z) + 0.45 + rnd.nextDouble() * 0.45, z), 4, sp && rnd.nextDouble() < 0.4 ? sp : null);
        }
        if (rnd.nextDouble() < 12 * dt) this.dustAt(xc + (rnd.nextDouble() - 0.5) * 1.8, ZC + (rnd.nextDouble() - 0.5) * 4.2, 0.8, true);
      }
    }
    pointScale() {
      const r = SG.Gfx.renderer;
      const h = r ? r.domElement.height : 720;
      return h / (2 * Math.tan(this.camera.fov * M.deg2rad / 2));
    }
    render(renderer) {
      if (this.sky) this.sky.position.copy(this.camera.position);
      renderer.render(this.scene, this.camera);
    }

    // ---------------------------------------------------------- 流程 --
    skip() {
      if (this.state !== 'run' || !this.built || this.real < 0.25) return;
      this.skipped = true;
      this.paused = false;
      this.cutDone = true;
      if (SG.Clash._cut) SG.Clash._cut.skip();
      this.setTime(TL.regroup1, false);
      this.dust.clear(); this.fx.clear(); this.glow.clear();
      this.hud.final(this);
      for (const A of this.armies) if (A.routed) this.hud.stamp(A.info.name + '部', '溃 散', true);
      this.state = 'hold';
      this.holdUntil = this.real + SKIP_HOLD;
      this.holdWall = performance.now() + SKIP_HOLD * 1000;
    }
    finish() {
      if (this.state === 'out' || this.state === 'done') return;
      this.state = 'out';
      this.hud.final(this);
      this.hud.fadeOut(FADE_OUT, () => { this.state = 'done'; if (this._resolve) this._resolve(); });
    }
    async run() {
      // 先淡入黑幕（战场照常绘制），黑幕下分帧搭建场景、异步编译着色器，再推入全屏场景
      this.hud = new Hud(this);
      const tb = performance.now();
      this.hud.fadeBlack(FADE_IN * 0.75);
      await this.buildAsync(10);
      this.resize(SG.Gfx.width, SG.Gfx.height);
      this.cameraPose(0);
      const r = SG.Gfx.renderer;
      // 有并行编译扩展时异步预编译着色器；没有时（如 SwiftShader）compileAsync 只会同步编译并警告，交给首帧即可
      let par = false;
      try { par = !!(r && r.extensions && r.extensions.has('KHR_parallel_shader_compile')); } catch (e) { /* 忽略 */ }
      if (par && typeof r.compileAsync === 'function') {
        try { await Promise.race([r.compileAsync(this.scene, this.camera), new Promise(res => setTimeout(res, 700))]); } catch (e) { /* 首帧再编译 */ }
      }
      const wait = FADE_IN * 0.75 * 1000 - (performance.now() - tb);
      if (wait > 0) await new Promise(res => setTimeout(res, wait));
      return new Promise(resolve => {
        this._resolve = resolve;
        SG.Gfx.pushScreen(this);
        this.hud.fadeIn(FADE_IN);
        // 宿主主循环未调用 update 时（如独立测试页）自行驱动
        let last = performance.now();
        this.lastHost = last;
        const own = () => {
          if (this.state === 'done') return;
          requestAnimationFrame(own);
          const n = performance.now();
          const dt = (n - last) / 1000;
          last = n;
          if (n - this.lastHost > 250) {
            this.advance(dt);
            if (SG.Gfx.topScreen() === this) SG.Gfx.render();
          }
        };
        requestAnimationFrame(own);
        // 保险：画面长时间没有推进（如页面被隐藏）也会结束，免得战斗流程卡住
        const t0 = performance.now();
        this._guard = setInterval(() => {
          const dbg = SG.Clash.debug;
          if (this.state === 'done' || (dbg && typeof dbg.freezeAt === 'number')) return;
          if (performance.now() - t0 > 60000) { this.state = 'done'; resolve(); }
        }, 1000);
      });
    }
    dispose() {
      clearInterval(this._guard);
      this.state = 'done';
      SG.Gfx.popScreen(this);
      if (this.hud) this.hud.dispose();
      for (const f of this.temp) { if (this.scene) this.scene.remove(f.mesh); f.mesh.geometry.dispose(); releaseFx(f.mesh.material); }
      this.temp.length = 0;
      for (const m of this.instanced) { m.dispose(); }
      for (const p of [this.dust, this.fx, this.glow]) if (p) p.dispose();
      for (const o of this.own) if (o && typeof o.dispose === 'function') o.dispose();
      this.own.length = 0;
      if (this.sun) { this.sun.dispose(); }
      if (this.scene) this.scene.clear();
      this.scene = null;
    }
  }

  // ======================================================= HUD --
  function injectStyle() {
    if (document.getElementById('sg-clash-style')) return;
    const s = document.createElement('style');
    s.id = 'sg-clash-style';
    s.textContent = `
.sg-clash{position:fixed;inset:0;z-index:30;pointer-events:auto;overflow:hidden;cursor:pointer;touch-action:none;
  -webkit-user-select:none;user-select:none;-webkit-tap-highlight-color:transparent;color:#f5eddb;
  font-family:var(--sg-font-body,"PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif,"WenQuanYi Zen Hei");
  --u:clamp(8.5px,min(1.3vw,2.45vh),26px);line-height:1.2;}
.sg-clash *,.sg-clash *::before,.sg-clash *::after{box-sizing:border-box;}
html.sg-clash-on #ui .sg-screens>:not(.sg-clash),html.sg-clash-on #ui .sg-toasts,html.sg-clash-on #ui .sg-labels{visibility:hidden !important;}
.sg-clash-vig{position:absolute;inset:0;pointer-events:none;
  background:radial-gradient(ellipse 75% 70% at 50% 58%,rgba(0,0,0,0) 55%,rgba(10,8,6,.42) 100%);}
.sg-clash-shade{position:absolute;left:0;right:0;top:0;height:calc(var(--u)*11 + env(safe-area-inset-top,0px));pointer-events:none;
  background:linear-gradient(180deg,rgba(8,8,12,.62),rgba(8,8,12,.28) 55%,rgba(8,8,12,0));}
.sg-clash-hud{position:absolute;left:0;right:0;top:0;display:flex;align-items:flex-start;justify-content:center;gap:calc(var(--u)*1.3);
  padding:calc(var(--u)*.7 + env(safe-area-inset-top,0px)) calc(var(--u)*1.1 + env(safe-area-inset-right,0px)) 0 calc(var(--u)*1.1 + env(safe-area-inset-left,0px));
  pointer-events:none;}
.sg-clash-side{position:relative;flex:1 1 0;max-width:calc(var(--u)*36);min-width:0;display:flex;align-items:center;gap:calc(var(--u)*.85);
  height:calc(var(--u)*6.4);padding:0 calc(var(--u)*1.4) 0 calc(var(--u)*.6);transition:transform .45s cubic-bezier(.2,.8,.2,1),opacity .3s ease;}
.sg-clash-side.s1{flex-direction:row-reverse;padding:0 calc(var(--u)*.6) 0 calc(var(--u)*1.4);}
.sg-clash.is-pre>.sg-clash-vig,.sg-clash.is-pre>.sg-clash-shade,.sg-clash.is-pre>.sg-clash-skip{visibility:hidden;}
.sg-clash.is-pre .sg-clash-side.s0{transform:translateX(-115%);opacity:0;}
.sg-clash.is-pre .sg-clash-side.s1{transform:translateX(115%);opacity:0;}
.sg-clash-side::before{content:"";position:absolute;inset:0;border-radius:calc(var(--u)*.5);
  background:linear-gradient(180deg,rgba(38,34,44,.93),rgba(14,13,20,.9));
  border:1px solid rgba(243,201,105,.55);box-shadow:0 calc(var(--u)*.3) calc(var(--u)*1.2) rgba(0,0,0,.45),inset 0 1px 0 rgba(255,255,255,.08);
  transform:skewX(-14deg);transform-origin:50% 100%;}
.sg-clash-side.s1::before{transform:skewX(14deg);}
.sg-clash-side::after{content:"";position:absolute;left:4%;right:4%;bottom:-1px;height:calc(var(--u)*.22);border-radius:2px;
  background:linear-gradient(90deg,transparent,var(--c),transparent);box-shadow:0 0 calc(var(--u)*.6) var(--c);opacity:.9;}
.sg-clash-side>*{position:relative;}
.sg-clash-pic{flex:none;width:calc(var(--u)*5.4);height:calc(var(--u)*5.4);margin-top:calc(var(--u)*.9);border-radius:calc(var(--u)*.5);overflow:hidden;
  background:#14121a;box-shadow:0 0 0 2px var(--fc),0 0 0 3.5px rgba(243,201,105,.85),0 calc(var(--u)*.3) calc(var(--u)*.9) rgba(0,0,0,.55);}
.sg-clash-pic>*{width:100% !important;height:100% !important;margin:0 !important;}
.sg-clash-pic img{display:block;width:100%;height:100%;object-fit:cover;}
.sg-clash-medal{width:100%;height:100%;display:grid;place-items:center;background:var(--fc);
  background:radial-gradient(circle at 34% 28%,color-mix(in srgb,var(--fc) 55%,#fff) 0%,var(--fc) 50%,color-mix(in srgb,var(--fc) 55%,#000) 100%);}
.sg-clash-medal span{font-family:${KAI};font-weight:900;font-size:calc(var(--u)*3.2);color:#fff6e2;line-height:1;
  text-shadow:0 2px 0 rgba(0,0,0,.45),0 0 calc(var(--u)*.6) rgba(0,0,0,.35);}
.sg-clash-meta{flex:1 1 auto;min-width:0;display:flex;flex-direction:column;gap:calc(var(--u)*.25);}
.sg-clash-side.s1 .sg-clash-meta{align-items:flex-end;text-align:right;}
.sg-clash-name{display:flex;align-items:center;gap:calc(var(--u)*.45);white-space:nowrap;}
.sg-clash-side.s1 .sg-clash-name{flex-direction:row-reverse;}
.sg-clash-name b{font-family:${KAI};font-weight:700;font-size:max(15px,calc(var(--u)*1.75));color:#fff4dc;letter-spacing:.04em;
  text-shadow:0 2px 0 rgba(0,0,0,.6);overflow:hidden;text-overflow:ellipsis;}
.sg-clash-name em{font-style:normal;flex:none;font-size:max(11px,calc(var(--u)*.85));font-weight:700;padding:.12em .45em;border-radius:.35em;
  color:#111;background:var(--c);}
.sg-clash-name em.me{background:#f3c969;}
.sg-clash-form{font-size:max(11px,calc(var(--u)*1.0));color:#d8cfbb;white-space:nowrap;}
.sg-clash-form i{font-style:normal;color:#f3c969;margin-right:.3em;}
.sg-clash-tr{flex:none;display:flex;flex-direction:column;align-items:flex-end;gap:calc(var(--u)*.2);min-width:calc(var(--u)*7.4);}
.sg-clash-side.s1 .sg-clash-tr{align-items:flex-start;}
.sg-clash-num{display:flex;align-items:baseline;gap:.25em;font-variant-numeric:tabular-nums;white-space:nowrap;}
.sg-clash-num small{font-size:max(11px,calc(var(--u)*.85));color:#ada392;}
.sg-clash-num b{font-size:max(18px,calc(var(--u)*2.35));font-weight:800;color:#fff8e6;letter-spacing:.02em;
  text-shadow:0 2px 0 rgba(0,0,0,.6);transition:color .2s ease;}
.sg-clash-num.is-hit b{color:#ff7a66;}
.sg-clash-bar{position:relative;width:calc(var(--u)*7.4);height:max(5px,calc(var(--u)*.48));border-radius:3px;background:rgba(0,0,0,.55);
  overflow:hidden;box-shadow:inset 0 0 0 1px rgba(255,255,255,.07);}
.sg-clash-bar i{position:absolute;top:0;bottom:0;left:0;border-radius:3px;}
.sg-clash-side.s1 .sg-clash-bar i{left:auto;right:0;}
.sg-clash-bar .trail{background:#ff5a45;}
.sg-clash-bar .fill{background:var(--c);background:linear-gradient(90deg,var(--c),color-mix(in srgb,var(--c) 60%,#fff));}
.sg-clash-loss{height:max(14px,calc(var(--u)*1.3));font-family:${KAI};font-weight:900;font-size:max(13px,calc(var(--u)*1.25));color:#ff5b4d;
  text-shadow:0 1px 0 #2a0703,0 0 4px rgba(0,0,0,.6);font-variant-numeric:tabular-nums;white-space:nowrap;opacity:0;transition:opacity .2s;}
.sg-clash-loss.on{opacity:1;}
.sg-clash-mid{flex:none;display:flex;flex-direction:column;align-items:center;gap:calc(var(--u)*.3);padding-top:calc(var(--u)*.35);
  transition:transform .4s cubic-bezier(.17,.89,.32,1.3),opacity .3s;}
.sg-clash.is-pre .sg-clash-mid{transform:scale(.3);opacity:0;}
.sg-clash-seal{width:calc(var(--u)*3.6);height:calc(var(--u)*3.6);border-radius:calc(var(--u)*.55);display:grid;place-items:center;
  background:radial-gradient(circle at 35% 30%,#e0523f,#b52a20 60%,#7d1610);color:#fff2e0;font-family:${KAI};font-weight:900;
  font-size:calc(var(--u)*2.4);line-height:1;transform:rotate(-6deg);
  box-shadow:0 0 0 2px rgba(255,220,170,.35) inset,0 calc(var(--u)*.3) calc(var(--u)*.9) rgba(0,0,0,.5);}
.sg-clash-terr{font-size:max(11px,calc(var(--u)*.92));color:#f3c969;letter-spacing:.3em;padding-left:.3em;white-space:nowrap;
  text-shadow:0 1px 2px rgba(0,0,0,.8);}
.sg-clash-pops{position:absolute;inset:0;pointer-events:none;}
.sg-clash-pop{position:absolute;left:0;top:0;will-change:transform;}
.sg-clash-pop span{display:block;transform:translate(-50%,-50%);white-space:nowrap;font-family:${KAI};font-weight:900;
  font-size:max(22px,calc(var(--u)*3.1));color:#ff4a3a;letter-spacing:.02em;
  -webkit-text-stroke:max(1px,calc(var(--u)*.09)) #2b0602;paint-order:stroke fill;
  text-shadow:0 calc(var(--u)*.18) 0 #2b0602,0 0 calc(var(--u)*.8) rgba(0,0,0,.55);
  animation:sg-clash-pop 1.15s cubic-bezier(.2,.8,.2,1) forwards;}
.sg-clash-pop.big span{font-size:max(28px,calc(var(--u)*4.2));color:#ffd24d;}
@keyframes sg-clash-pop{0%{opacity:0;transform:translate(-50%,-50%) scale(2.1);}12%{opacity:1;transform:translate(-50%,-50%) scale(.92);}
  22%{transform:translate(-50%,-50%) scale(1.05);}32%{transform:translate(-50%,-50%) scale(1);}
  75%{opacity:1;}100%{opacity:0;transform:translate(-50%,calc(-50% - var(--u)*3.2)) scale(.96);}}
.sg-clash-stamp{position:absolute;left:50%;top:44%;transform:translate(-50%,-50%);pointer-events:none;display:flex;flex-direction:column;align-items:center;
  gap:calc(var(--u)*.4);opacity:0;}
.sg-clash-stamp.on{animation:sg-clash-stamp .42s cubic-bezier(.2,.9,.25,1.2) forwards;}
.sg-clash-stamp.on.now{animation-duration:.01s;}
.sg-clash-stamp small{font-size:max(13px,calc(var(--u)*1.3));color:#f5eddb;letter-spacing:.2em;text-shadow:0 2px 3px rgba(0,0,0,.8);}
.sg-clash-stamp b{font-family:${KAI};font-weight:900;font-size:max(34px,calc(var(--u)*5.2));line-height:1;color:#fff3e4;padding:.12em .3em .16em;
  border-radius:.12em;background:linear-gradient(180deg,#d24434,#9c1e15);transform:rotate(-5deg);letter-spacing:.06em;
  box-shadow:0 0 0 .06em rgba(255,226,180,.5) inset,0 .1em .4em rgba(0,0,0,.55);text-shadow:0 .04em 0 rgba(0,0,0,.4);}
@keyframes sg-clash-stamp{0%{opacity:0;transform:translate(-50%,-50%) scale(2.4);}60%{opacity:1;transform:translate(-50%,-50%) scale(.94);}
  100%{opacity:1;transform:translate(-50%,-50%) scale(1);}}
.sg-clash-skip{position:absolute;right:calc(var(--u)*1.2 + env(safe-area-inset-right,0px));bottom:calc(var(--u)*1.0 + env(safe-area-inset-bottom,0px));
  pointer-events:none;font-size:max(11px,calc(var(--u)*.95));color:rgba(245,237,219,.82);letter-spacing:.12em;padding:.35em .8em;border-radius:2em;
  background:rgba(10,10,16,.45);border:1px solid rgba(243,201,105,.3);opacity:0;transition:opacity .4s ease .5s;}
.sg-clash-skip i{font-style:normal;color:#f3c969;margin-left:.3em;letter-spacing:-.15em;}
.sg-clash.is-live .sg-clash-skip{opacity:1;}
.sg-clash-mode{position:absolute;left:calc(var(--u)*1.2 + env(safe-area-inset-left,0px));bottom:calc(var(--u)*1.0 + env(safe-area-inset-bottom,0px));
  display:flex;align-items:center;min-height:max(30px,calc(var(--u)*2.4));pointer-events:auto;cursor:pointer;
  font-size:max(11px,calc(var(--u)*.95));color:rgba(245,237,219,.82);letter-spacing:.12em;padding:0 .9em;border-radius:2em;
  background:rgba(10,10,16,.45);border:1px solid rgba(243,201,105,.3);opacity:0;transition:opacity .4s ease .5s;}
.sg-clash-mode b{color:#f3c969;font-weight:700;margin-left:.35em;}
.sg-clash.is-live .sg-clash-mode{opacity:1;}
.sg-clash.is-pre>.sg-clash-mode{visibility:hidden;}
/* 触摸命中区 ≥ 44px：外观不变，四周透明扩展；点偏一点也落在本按钮上（其 pointerdown 不冒泡），不会误触「跳过」 */
.sg-clash-mode::before{content:"";position:absolute;left:-10px;right:-10px;top:50%;height:max(48px,calc(100% + 16px));transform:translateY(-50%);}
@media (hover:hover){.sg-clash-mode:hover{border-color:rgba(243,201,105,.75);color:#fff6e0;}}
.sg-clash-flash{position:absolute;inset:0;pointer-events:none;opacity:0;mix-blend-mode:screen;}
.sg-clash-fade{position:absolute;inset:0;pointer-events:none;background:#0b0a0f;opacity:1;}
@media (max-height:540px){
  .sg-clash{--u:clamp(8px,min(1.3vw,2.5vh),26px);}
  .sg-clash-side{height:calc(var(--u)*6.6);}
  .sg-clash-pic{width:calc(var(--u)*5.6);height:calc(var(--u)*5.6);margin-top:calc(var(--u)*1.0);}
}
/* 竖屏（手机竖握、平板竖放）：每侧改为两行网格（姓名 / 兵力），去掉阵型行，头像缩小 */
@media (max-aspect-ratio:1/1){
  .sg-clash{--u:clamp(8px,min(2.3vw,2.2vh),18px);}
  .sg-clash-hud{gap:calc(var(--u)*.45);padding-left:calc(var(--u)*.5 + env(safe-area-inset-left,0px));padding-right:calc(var(--u)*.5 + env(safe-area-inset-right,0px));}
  .sg-clash-side,.sg-clash-side.s1{display:grid;grid-template-columns:auto minmax(0,1fr);grid-template-rows:auto auto;column-gap:calc(var(--u)*.6);row-gap:calc(var(--u)*.2);
    align-items:center;height:auto;padding:calc(var(--u)*.6) calc(var(--u)*1.1) calc(var(--u)*.5) calc(var(--u)*.6);}
  .sg-clash-side.s1{grid-template-columns:minmax(0,1fr) auto;padding:calc(var(--u)*.6) calc(var(--u)*.6) calc(var(--u)*.5) calc(var(--u)*1.1);}
  .sg-clash-side::before{transform:skewX(-7deg);}
  .sg-clash-side.s1::before{transform:skewX(7deg);}
  .sg-clash-pic{grid-column:1;grid-row:1 / span 2;width:calc(var(--u)*4.4);height:calc(var(--u)*4.4);margin-top:0;}
  .sg-clash-side.s1 .sg-clash-pic{grid-column:2;}
  .sg-clash-meta{grid-column:2;grid-row:1;}
  .sg-clash-side.s1 .sg-clash-meta{grid-column:1;}
  .sg-clash-form,.sg-clash-num small{display:none;}
  .sg-clash-tr{grid-column:2;grid-row:2;min-width:0;align-items:flex-start;}
  .sg-clash-side.s1 .sg-clash-tr{grid-column:1;align-items:flex-end;}
  .sg-clash-bar{width:100%;}
  .sg-clash-name b{font-size:max(14px,calc(var(--u)*1.6));}
  .sg-clash-num b{font-size:max(17px,calc(var(--u)*2.1));}
  .sg-clash-seal{width:calc(var(--u)*3);height:calc(var(--u)*3);font-size:calc(var(--u)*2);}
  .sg-clash-shade{height:calc(var(--u)*13 + env(safe-area-inset-top,0px));}
}
/* ---------------- 必杀特写 ---------------- */
.sg-cutin{position:fixed;inset:0;z-index:40;pointer-events:auto;overflow:hidden;cursor:pointer;touch-action:none;
  -webkit-user-select:none;user-select:none;-webkit-tap-highlight-color:transparent;--u:clamp(8px,min(1.3vw,2.45vh),26px);
  font-family:var(--sg-font-body,"PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif,"WenQuanYi Zen Hei");}
.sg-cutin *{box-sizing:border-box;}
.sg-cutin>*{animation-duration:var(--T);animation-fill-mode:both;animation-timing-function:linear;}
.sg-cutin-dim{position:absolute;inset:0;background:rgba(6,5,10,.62);animation-name:sg-ci-dim;}
.sg-cutin-band{position:absolute;left:-10%;right:-10%;top:30%;height:40%;overflow:hidden;transform-origin:50% 50%;
  background:var(--c);
  background:linear-gradient(180deg,color-mix(in srgb,var(--c) 70%,#000) 0%,var(--c) 30%,color-mix(in srgb,var(--c) 75%,#fff) 50%,var(--c) 70%,color-mix(in srgb,var(--c) 70%,#000) 100%);
  box-shadow:0 0 0 2px rgba(255,236,190,.55),0 0 calc(var(--u)*4) color-mix(in srgb,var(--c) 70%,transparent);animation-name:sg-ci-band;}
.sg-cutin.s1 .sg-cutin-band{animation-name:sg-ci-band1;}
/* 浅色（白 / 米 / 浅金 / 粉）色带：中线不再提白，整体略压暗，让白字台词读得出 */
.sg-cutin.is-light .sg-cutin-band{background:linear-gradient(180deg,color-mix(in srgb,var(--c) 55%,#000) 0%,color-mix(in srgb,var(--c) 82%,#000) 30%,color-mix(in srgb,var(--c) 92%,#000) 50%,color-mix(in srgb,var(--c) 82%,#000) 70%,color-mix(in srgb,var(--c) 55%,#000) 100%);}
.sg-cutin-band canvas{position:absolute;inset:0;width:100%;height:100%;mix-blend-mode:screen;}
.sg-cutin-pic{position:absolute;top:21%;left:5%;height:58%;aspect-ratio:1;background:#14121a;border-radius:calc(var(--u)*.9);overflow:hidden;
  box-shadow:0 0 0 3px var(--c),0 0 0 5px rgba(243,201,105,.9),0 calc(var(--u)*.6) calc(var(--u)*2.2) rgba(0,0,0,.6);animation-name:sg-ci-pic;}
.sg-cutin.s1 .sg-cutin-pic{left:auto;right:5%;animation-name:sg-ci-pic1;}
.sg-cutin-pic>*{width:100% !important;height:100% !important;margin:0 !important;}
.sg-cutin-pic img{display:block;width:100%;height:100%;object-fit:cover;}
.sg-cutin-pic .sg-clash-medal span{font-size:calc(var(--u)*11);}
.sg-cutin-text{position:absolute;top:50%;left:calc(5% + 58vh + 3%);right:3%;transform:translateY(-50%);display:flex;flex-direction:column;align-items:flex-start;
  animation-name:sg-ci-text;}
.sg-cutin.s1 .sg-cutin-text{left:3%;right:calc(5% + 58vh + 3%);align-items:flex-end;}
.sg-cutin-who{font-family:${KAI};font-weight:700;font-size:max(16px,calc(var(--u)*2.0));color:#fff3dc;letter-spacing:.3em;margin:0 0 .1em .2em;
  text-shadow:0 0 2px rgba(10,6,4,.95),0 0 2px rgba(10,6,4,.95),0 2px 0 rgba(0,0,0,.8),0 0 calc(var(--u)*1.2) rgba(0,0,0,.7);}
.sg-cutin-name{position:relative;padding:.08em .5em .16em .3em;white-space:nowrap;}
.sg-cutin-name svg{position:absolute;left:-6%;top:-14%;width:112%;height:128%;overflow:visible;animation:sg-ci-swash var(--T) linear both;}
.sg-cutin-name b{position:relative;display:block;font-family:${KAI};font-weight:700;line-height:1.08;letter-spacing:.08em;
  font-size:max(calc(34px*var(--nk,1)),calc(var(--u)*6.4*var(--nk,1)));
  background:linear-gradient(180deg,#ffffff 0%,#fff4cf 42%,#ffd76a 62%,#f3a93b 100%);-webkit-background-clip:text;background-clip:text;color:transparent;
  filter:drop-shadow(0 0 1px rgba(30,12,4,.95)) drop-shadow(0 0 1px rgba(30,12,4,.9)) drop-shadow(0 calc(var(--u)*.25) 0 rgba(20,8,2,.85)) drop-shadow(0 0 calc(var(--u)*1.1) var(--c));
  animation:sg-ci-name var(--T) linear both;}
.sg-cutin-cry{margin:.35em 0 0 .4em;max-width:100%;font-family:${KAI};font-weight:700;font-size:max(13px,calc(var(--u)*1.55));line-height:1.3;color:#fff6e0;
  letter-spacing:.06em;text-shadow:0 0 2px rgba(10,6,4,.95),0 0 2px rgba(10,6,4,.95),0 2px 0 rgba(0,0,0,.8),0 0 calc(var(--u)*1.2) rgba(0,0,0,.7);animation:sg-ci-cry var(--T) linear both;}
.sg-cutin.s1 .sg-cutin-cry{margin:.35em .4em 0 0;text-align:right;}
.sg-cutin-flash{position:absolute;inset:0;background:#fff;opacity:0;animation-name:sg-ci-flash;pointer-events:none;}
@keyframes sg-ci-dim{0%{opacity:0}9%{opacity:1}84%{opacity:1}100%{opacity:0}}
@keyframes sg-ci-band{0%{transform:translateX(-60%) skewY(-7deg) scaleY(.05);opacity:0}10%{transform:translateX(0) skewY(-7deg) scaleY(1);opacity:1}
  84%{transform:translateX(2%) skewY(-7deg) scaleY(1);opacity:1}100%{transform:translateX(4%) skewY(-7deg) scaleY(.02);opacity:0}}
@keyframes sg-ci-band1{0%{transform:translateX(60%) skewY(7deg) scaleY(.05);opacity:0}10%{transform:translateX(0) skewY(7deg) scaleY(1);opacity:1}
  84%{transform:translateX(-2%) skewY(7deg) scaleY(1);opacity:1}100%{transform:translateX(-4%) skewY(7deg) scaleY(.02);opacity:0}}
@keyframes sg-ci-pic{0%{transform:translateX(-140%) scale(1.15);filter:blur(6px);opacity:0}6%{opacity:1}
  26%{transform:translateX(0) scale(1);filter:blur(0)}84%{transform:translateX(5%) scale(1.03);opacity:1}100%{transform:translateX(9%) scale(1.04);opacity:0}}
@keyframes sg-ci-pic1{0%{transform:translateX(140%) scale(1.15);filter:blur(6px);opacity:0}6%{opacity:1}
  26%{transform:translateX(0) scale(1);filter:blur(0)}84%{transform:translateX(-5%) scale(1.03);opacity:1}100%{transform:translateX(-9%) scale(1.04);opacity:0}}
@keyframes sg-ci-text{0%,14%{opacity:0}18%{opacity:1}84%{opacity:1}100%{opacity:0}}
@keyframes sg-ci-name{0%,15%{transform:scale(1.9);opacity:0;letter-spacing:.5em}30%{transform:scale(.95);opacity:1;letter-spacing:.08em}
  34%{transform:scale(1.04) translateX(-1%)}37%{transform:scale(1) translateX(1%)}40%{transform:none}84%{transform:scale(1.03)}100%{transform:scale(1.06)}}
@keyframes sg-ci-swash{0%,13%{clip-path:inset(-40% 110% -40% -10%)}30%,100%{clip-path:inset(-40% -15% -40% -10%)}}
@keyframes sg-ci-flash{0%,80%{opacity:0}84%{opacity:.55}100%{opacity:0}}
@keyframes sg-ci-cry{0%,26%{opacity:0;transform:translateY(.6em)}36%{opacity:1;transform:none}100%{opacity:1;transform:none}}
.sg-cutin.is-skip>*{animation:none !important;opacity:0 !important;transition:opacity .12s;}
/* 系统「减弱动态效果」：飘字静态显示（全局规则会把它缩成 .01ms 并停在透明），不闪屏，特写只淡入淡出 */
@media (prefers-reduced-motion:reduce){
  .sg-clash-pop span,#ui .sg-clash-pop span{animation:none !important;opacity:1;}
  .sg-clash-flash,.sg-cutin-flash{display:none !important;}
  .sg-cutin>*{animation-name:sg-ci-dim !important;}
  .sg-cutin-name svg,.sg-cutin-name b,.sg-cutin-cry{animation:none !important;}
}
/* 竖屏：头像在上、招式名与台词在色带内全宽排开 */
@media (max-aspect-ratio:1/1){
  .sg-cutin{--u:clamp(8px,min(2.3vw,2.2vh),18px);}
  .sg-cutin-band{top:34%;height:34%;}
  .sg-cutin-pic,.sg-cutin.s1 .sg-cutin-pic{top:10%;height:auto;width:min(60vw,36vh);}
  .sg-cutin-pic{left:6%;}
  .sg-cutin.s1 .sg-cutin-pic{left:auto;right:6%;}
  .sg-cutin-pic .sg-clash-medal span{font-size:min(30vw,18vh);}
  .sg-cutin-text,.sg-cutin.s1 .sg-cutin-text{top:41%;left:5%;right:5%;transform:none;}
  .sg-cutin-name b{font-size:calc(min(var(--u)*6.4,12vw)*var(--nk,1));}
}
`;
    (document.head || document.documentElement).appendChild(s);
  }

  // 头像：SG.Portrait.el 存在时用之，否则用姓氏徽章
  function portraitEl(gen, color, px, flip, mood) {
    if (SG.Portrait && typeof SG.Portrait.el === 'function') {
      try {
        const e = SG.Portrait.el(gen, { size: Math.max(32, Math.round(px)), color, flip: !!flip, mood: mood || 'angry', frame: false });
        if (e && e.nodeType === 1) return e;
      } catch (err) { /* 回退到徽章 */ }
    }
    const m = document.createElement('div');
    m.className = 'sg-clash-medal';
    const s = document.createElement('span');
    s.textContent = surname(gen);
    m.appendChild(s);
    return m;
  }
  function hudParent() {
    const ui = SG.UI;
    try {
      if (ui && typeof ui.layer === 'function') { const l = ui.layer('screens'); if (l) return l; }
      if (ui && ui.layers && ui.layers.screens) return ui.layers.screens;
    } catch (e) { /* 回退 */ }
    return document.body;
  }
  function unitPx(root) {
    const v = parseFloat(getComputedStyle(root).getPropertyValue('--u'));
    if (v > 0) return v;
    return clamp(Math.min(window.innerWidth * 0.013, window.innerHeight * 0.0245), 8.5, 26);
  }

  class Hud {
    constructor(sc) {
      injectStyle();
      this.sc = sc;
      const root = this.root = el('div', 'sg-clash is-pre');
      hudParent().appendChild(root);
      el('div', 'sg-clash-vig', null, root);
      el('div', 'sg-clash-shade', null, root);
      const hud = el('div', 'sg-clash-hud', null, root);
      this.bar = hud;
      const u = unitPx(root);
      this.sides = [];
      const mkSide = (info, k) => {
        const side = el('div', 'sg-clash-side s' + k, null, hud);
        side.style.setProperty('--c', SIDE_COLOR[info.side]);
        side.style.setProperty('--fc', info.color);
        const pic = el('div', 'sg-clash-pic', null, side);
        pic.appendChild(portraitEl(info.gen, info.color, u * 5.6, k === 0, 'angry'));   // 头像默认朝左；左侧攻方镜像朝向敌军
        const meta = el('div', 'sg-clash-meta', null, side);
        const me = sc.playerSide !== null && sc.playerSide === info.side;
        el('div', 'sg-clash-name', `<b>${esc(info.name)}</b><em class="${me ? 'me' : ''}">${me ? '我军' : (info.side === 0 ? '攻方' : '守方')}</em>`, meta);
        el('div', 'sg-clash-form', `<i>◆</i>${esc(formName(info.formation))}之阵`, meta);
        const tr = el('div', 'sg-clash-tr', null, side);
        const num = el('div', 'sg-clash-num', null, tr);
        el('small', null, '兵力', num);
        const b = el('b', null, String(info.before), num);
        const bar = el('div', 'sg-clash-bar', null, tr);
        const trail = el('i', 'trail', null, bar), fill = el('i', 'fill', null, bar);
        const loss = el('div', 'sg-clash-loss', '', tr);
        let max = info.before;
        try { if (SG.maxTroops && info.gen && typeof info.gen.war === 'number') max = Math.max(max, SG.maxTroops(info.gen)); } catch (e) { /* 忽略 */ }
        const S = { info, side, pic, num, b, trail, fill, loss, max: Math.max(1, max), shown: info.before, target: info.before, trailV: info.before, trailHold: 0, lost: 0, hitT: 0 };
        this.setBar(S);
        this.sides.push(S);
        return S;
      };
      mkSide(sc.A, 0);
      const mid = el('div', 'sg-clash-mid', null, hud);
      el('div', 'sg-clash-seal', '战', mid);
      el('div', 'sg-clash-terr', KIND_NAME[sc.kind] || '平原', mid);
      mkSide(sc.D, 1);
      this.pops = el('div', 'sg-clash-pops', null, root);
      this.popList = [];
      this.stampEl = el('div', 'sg-clash-stamp', null, root);
      el('div', 'sg-clash-skip', (SG.isTouch ? '轻触跳过' : '点击 / 空格 跳过') + '<i>▶▶</i>', root);
      // 动画开 / 快 / 关：战斗中也能切换（选「关」时本场立即跳到结果，之后的攻击不再播放）
      const modeBtn = this.modeEl = el('div', 'sg-clash-mode', '', root);
      modeBtn.setAttribute('role', 'button');
      modeBtn.title = '攻击画面：开 / 快 / 关';
      const modeLabel = () => { modeBtn.innerHTML = '动画<b>' + Clash.modeLabel() + '</b>'; };
      modeLabel();
      modeBtn.addEventListener('pointerdown', e => {
        e.preventDefault(); e.stopPropagation();
        const m = Clash.cycleMode();
        modeLabel();
        if (m === 'off') sc.skip();
        else sc.speed = Math.max(0.1, (+Clash.speed || 1) * (+sc.opts.speed || 1));
      });
      this.flashEl = el('div', 'sg-clash-flash', null, root);
      this.fadeEl = el('div', 'sg-clash-fade', null, root);
      this.flashA = 0;
      // 跳过：轻点 / 点击 / 空格 / 回车
      this.onDown = e => { e.preventDefault(); e.stopPropagation(); sc.skip(); };
      this.onKey = e => {
        if (Clash._cut) return;        // 特写画面优先
        const k = e.key;
        if (k === ' ' || k === 'Enter' || k === 'Escape' || k === 'Spacebar') {
          e.preventDefault(); e.stopPropagation();
          if (!e.repeat) sc.skip();
        } else if (!e.ctrlKey && !e.metaKey && !e.altKey) {
          // 其余按键不再传给主画面（相机 WASD / QE 等），免得返回战场时视角已被移动；keyup 照常放行
          e.stopPropagation();
        }
      };
      root.addEventListener('pointerdown', this.onDown);
      root.addEventListener('click', e => { e.preventDefault(); e.stopPropagation(); });
      window.addEventListener('keydown', this.onKey, true);
    }
    setBar(S) {
      S.fill.style.width = (clamp01(S.shown / S.max) * 100).toFixed(2) + '%';
      S.trail.style.width = (clamp01(S.trailV / S.max) * 100).toFixed(2) + '%';
    }
    // 进场前：战场上淡入黑幕
    fadeBlack(sec) {
      const f = this.fadeEl;
      f.style.transition = 'none';
      f.style.opacity = '0';
      void f.offsetWidth;
      f.style.transition = `opacity ${sec}s ease-in`;
      f.style.opacity = '1';
    }
    fadeIn(sec) {
      // 已推入全屏场景（黑幕下）：隐藏战场 HUD、世界标签与提示，退出时恢复
      try { document.documentElement.classList.add('sg-clash-on'); } catch (e) { /* 忽略 */ }
      const f = this.fadeEl;
      f.style.transition = 'none';
      f.style.opacity = '1';
      void f.offsetWidth;
      f.style.transition = `opacity ${sec}s ease-out`;
      f.style.opacity = '0';
      requestAnimationFrame(() => requestAnimationFrame(() => { this.root.classList.remove('is-pre'); this.root.classList.add('is-live'); }));
    }
    fadeOut(sec, done) {
      this.fading = true;
      const f = this.fadeEl;
      this.root.classList.remove('is-live');
      f.style.transition = `opacity ${sec}s ease-in`;
      f.style.opacity = '1';
      let called = false;
      const fin = () => {
        if (called) return;
        called = true;
        done();
        // 退出后黑幕再淡去（此时已回到战场）
        const r = this.root;
        r.style.pointerEvents = 'none';
        for (const c of Array.from(r.children)) if (c !== f) c.style.visibility = 'hidden';
        requestAnimationFrame(() => {
          f.style.transition = `opacity ${sec}s ease-out`;
          f.style.opacity = '0';
        });
        // 移除不依赖 rAF（页面隐藏或掉帧时也能按时清理）
        setTimeout(() => { if (r.parentNode) r.parentNode.removeChild(r); }, sec * 1000 + 120);
      };
      setTimeout(fin, sec * 1000);
    }
    flash(color, a) {
      if (this.sc.calm) return;
      this.flashEl.style.background = color;
      this.flashA = a || 0.35;
    }
    loss(si, amount) {
      const S = this.sides[si];
      S.target = Math.max(S.info.after, S.target - amount);
      S.lost += amount;
      S.loss.textContent = '−' + S.lost;
      S.loss.classList.add('on');
      S.hitT = 0.3;
      S.trailHold = 0.35;
    }
    pop(world, text, big) {
      const w = el('div', 'sg-clash-pop' + (big ? ' big' : ''), null, this.pops);
      el('span', null, esc(text), w);
      this.popList.push({ el: w, world, age: 0 });
    }
    stamp(sub, text, instant) {
      const s = this.stampEl;
      s.innerHTML = `<small>${esc(sub)}</small><b>${esc(text)}</b>`;
      s.classList.add('on');
      if (instant) s.classList.add('now');
    }
    // 直接显示最终结果
    final(sc) {
      for (const S of this.sides) {
        S.target = S.info.after; S.shown = S.info.after; S.trailV = S.info.after;
        S.lost = S.info.loss;
        if (S.lost > 0) { S.loss.textContent = '−' + S.lost; S.loss.classList.add('on'); }
        S.b.textContent = String(S.info.after);
        this.setBar(S);
      }
      if (sc.skipped) { for (const p of this.popList) p.el.remove(); this.popList.length = 0; }
    }
    update(dt, sc, popDt) {
      for (const S of this.sides) {
        if (S.shown > S.target) {
          const d = S.shown - S.target;
          S.shown = Math.max(S.target, S.shown - Math.max(d * dt * 7, dt * 300));
          S.b.textContent = String(Math.round(S.shown));
        }
        if (S.trailHold > 0) S.trailHold -= dt;
        else if (S.trailV > S.shown) S.trailV = Math.max(S.shown, S.trailV - Math.max((S.trailV - S.shown) * dt * 4, dt * 120));
        this.setBar(S);
        if (S.hitT > 0) { S.hitT -= dt; S.num.classList.toggle('is-hit', S.hitT > 0); }
      }
      if (this.flashA > 0) {
        this.flashEl.style.opacity = this.flashA.toFixed(3);
        this.flashA = Math.max(0, this.flashA - dt * 2.6);
        if (this.flashA === 0) this.flashEl.style.opacity = '0';
      }
      // 飘字跟随三维位置
      const cam = sc.camera, r = SG.Gfx.rect || { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
      for (let i = this.popList.length - 1; i >= 0; i--) {
        const p = this.popList[i];
        p.age += popDt;
        p.el.firstChild.style.animationPlayState = popDt > 0 || sc.state !== 'run' ? 'running' : 'paused';
        if (p.age > 1.2) { p.el.remove(); this.popList.splice(i, 1); continue; }
        _v1.copy(p.world).project(cam);
        const x = r.left + (_v1.x * 0.5 + 0.5) * r.width, y = r.top + (-_v1.y * 0.5 + 0.5) * r.height;
        p.el.style.transform = `translate3d(${x.toFixed(1)}px,${y.toFixed(1)}px,0)`;
      }
    }
    dispose() {
      window.removeEventListener('keydown', this.onKey, true);
      this.root.removeEventListener('pointerdown', this.onDown);
      try { document.documentElement.classList.remove('sg-clash-on'); } catch (e) { /* 忽略 */ }
      // 根节点由 fadeOut 的收尾负责移除；未走 fadeOut（异常、提前结束）时直接移除
      if (this.root.parentNode && !this.fading) this.root.parentNode.removeChild(this.root);
    }
  }

  // ======================================================= 特写 cutIn --
  function swashSvg(seed, color) {
    const r = SG.SeededRandom(seed);
    const top = [], bot = [];
    const N = 28;
    for (let i = 0; i <= N; i++) {
      const u = i / N, x = u * 1000;
      // 收笔：最后 ~12% 平滑收细到笔尖（不再是方头）
      const te = Math.min(1, (1 - u) / 0.12), tap = te * te * (3 - 2 * te);
      const w = 40 * (0.55 + 0.45 * Math.sin(Math.min(1, u * 1.08) * Math.PI)) * (u < 0.05 ? 0.5 + u * 10 : 1) * tap;
      const jit = 7 * Math.max(0.25, tap);
      top.push([x, 50 - w + (r.nextDouble() - 0.5) * jit]);
      bot.push([x, 50 + w * (0.9 + r.nextDouble() * 0.2) + (r.nextDouble() - 0.5) * jit]);
    }
    let d = 'M' + top.map(p => p[0].toFixed(0) + ' ' + p[1].toFixed(1)).join(' L');
    d += ' L' + bot.reverse().map(p => p[0].toFixed(0) + ' ' + p[1].toFixed(1)).join(' L') + ' Z';
    // 飞白：右端几道细笔触
    let streaks = '';
    for (let i = 0; i < 7; i++) {
      // 飞白：笔毛散出，末端各自长短不一、向笔尖略收拢
      const y = 16 + i * 11 + (r.nextDouble() - 0.5) * 6, x0 = 640 + r.nextDouble() * 200, x1 = 960 + r.nextDouble() * 130;
      const y1 = y + (50 - y) * 0.4 + (r.nextDouble() - 0.5) * 4;
      streaks += `<path d="M${x0.toFixed(0)} ${y.toFixed(1)} L${x1.toFixed(0)} ${y1.toFixed(1)}" stroke="#0c0806" stroke-width="${(2 + r.nextDouble() * 4).toFixed(1)}" stroke-linecap="round" opacity=".8"/>`;
    }
    return `<svg viewBox="0 0 1000 100" preserveAspectRatio="none" aria-hidden="true">
<path d="${d}" fill="#0c0806" opacity=".88"/>
<path d="${d}" fill="none" stroke="${color}" stroke-width="3" opacity=".55" transform="translate(0 4)"/>${streaks}</svg>`;
  }

  class CutIn {
    constructor(o) {
      injectStyle();
      this.o = o;
      // 时长 = 1.1 秒 ÷（SG.Clash.speed × o.speed）——与 battle-controller.specialCutIn 的换算约定一致；
      // 但最短 0.7 秒（「快」档下 specials.js 另传 1.6 时不会被压到 0.34 秒），保证招式名读得清
      const spd = Math.max(0.1, (+SG.Clash.speed || 1) * (+o.speed || 1));
      const dur = Math.max(0.7, 1.1 / spd);
      this.dur = dur;
      const color = hexOf(o.color || '#ffd24d');
      const side = o.side === 1 ? 1 : 0;
      const root = this.root = el('div', 'sg-cutin s' + side);
      root.style.setProperty('--c', color);
      root.style.setProperty('--fc', color);
      root.style.setProperty('--T', dur + 's');
      { const n = parseInt(color.slice(1), 16), lr = ((n >> 16) & 255) / 255, lg = ((n >> 8) & 255) / 255, lb = (n & 255) / 255;
        if (0.3 * lr + 0.59 * lg + 0.11 * lb > 0.62) root.classList.add('is-light'); }
      el('div', 'sg-cutin-dim', null, root);
      const band = el('div', 'sg-cutin-band', null, root);
      this.canvas = el('canvas', null, null, band);
      const pic = el('div', 'sg-cutin-pic', null, root);
      const ph = Math.round(window.innerHeight * 0.6);
      pic.appendChild(portraitEl(o.gen || { name: '将' }, color, ph, side === 0, 'angry'));
      const tx = el('div', 'sg-cutin-text', null, root);
      el('div', 'sg-cutin-who', esc((o.gen && o.gen.name) || ''), tx);
      const nm = el('div', 'sg-cutin-name', swashSvg(hashStr(o.name || ''), color), tx);
      const nmText = String(o.name || '必杀');
      const nb = el('b', null, esc(nmText), nm);
      // 招式名过长时按字数缩小（以 6 字为满宽）
      const len = Array.from(nmText).length;
      if (len > 6) nb.style.setProperty('--nk', (6 / len).toFixed(3));
      if (o.cry) el('div', 'sg-cutin-cry', '「' + esc(String(o.cry)) + '」', tx);
      el('div', 'sg-cutin-flash', null, root);
      document.body.appendChild(root);
      this.lines = [];
      const rnd = SG.SeededRandom(hashStr((o.name || '') + side));
      for (let i = 0; i < 70; i++) this.lines.push({ y: rnd.nextDouble(), x: rnd.nextDouble(), len: 0.08 + rnd.nextDouble() * 0.3, sp: 1.6 + rnd.nextDouble() * 2.8, w: 0.6 + rnd.nextDouble() * 2.6, a: 0.25 + rnd.nextDouble() * 0.6, white: rnd.nextDouble() < 0.55 });
      this.side = side;
      this.color = SG.Gfx ? SG.Gfx.color(color) : null;
      this.t0 = performance.now();
      this.onDown = e => { e.preventDefault(); e.stopPropagation(); this.skip(); };
      this.onKey = e => {
        const k = e.key;
        if (k === ' ' || k === 'Enter' || k === 'Escape' || k === 'Spacebar') { e.preventDefault(); e.stopPropagation(); if (!e.repeat) this.skip(); }
      };
      root.addEventListener('pointerdown', this.onDown);
      window.addEventListener('keydown', this.onKey, true);
      this.promise = new Promise(res => { this._res = res; });
      sfx('magic', 0.6); sfx('horn', 0.25);
      this.tick = this.tick.bind(this);
      requestAnimationFrame(this.tick);
      if (!(SG.Clash.debug && SG.Clash.debug.holdCut)) this._timer = setTimeout(() => this.end(), dur * 1000 + 30);
    }
    tick() {
      if (this.ended) return;
      const cv = this.canvas;
      const w = cv.clientWidth | 0, h = cv.clientHeight | 0;
      if (w > 0 && h > 0) {
        const dpr = Math.min(1.5, window.devicePixelRatio || 1);
        if (cv.width !== Math.round(w * dpr) || cv.height !== Math.round(h * dpr)) { cv.width = Math.round(w * dpr); cv.height = Math.round(h * dpr); }
        const g = cv.getContext('2d');
        const W = cv.width, H = cv.height;
        g.clearRect(0, 0, W, H);
        const t = (performance.now() - this.t0) / 1000;
        const dir = this.side === 0 ? -1 : 1;
        const tint = this.color ? 'rgba(' + Math.round(Math.min(255, this.color.r * 255 + 110)) + ',' + Math.round(Math.min(255, this.color.g * 255 + 110)) + ',' + Math.round(Math.min(255, this.color.b * 255 + 110)) + ',' : 'rgba(255,240,200,';
        for (const L of this.lines) {
          const x = (((L.x + dir * t * L.sp) % 1.4) + 1.4) % 1.4 - 0.2;
          const x0 = x * W, x1 = (x + L.len * -dir) * W, y = L.y * H;
          const grd = g.createLinearGradient(x0, 0, x1, 0);
          const c = L.white ? 'rgba(255,255,255,' : tint;
          grd.addColorStop(0, c + L.a + ')');
          grd.addColorStop(1, c + '0)');
          g.strokeStyle = grd;
          g.lineWidth = L.w * dpr;
          g.beginPath(); g.moveTo(x0, y); g.lineTo(x1, y); g.stroke();
        }
      }
      requestAnimationFrame(this.tick);
    }
    skip() {
      if (this.ended || performance.now() - this.t0 < 120) return;
      this.root.classList.add('is-skip');
      clearTimeout(this._timer);
      this._timer = setTimeout(() => this.end(), 120);
    }
    end() {
      if (this.ended) return;
      this.ended = true;
      clearTimeout(this._timer);
      window.removeEventListener('keydown', this.onKey, true);
      this.root.removeEventListener('pointerdown', this.onDown);
      if (this.root.parentNode) this.root.parentNode.removeChild(this.root);
      if (Clash._cut === this) Clash._cut = null;
      this._res();
    }
  }

  // ======================================================= 设置 --
  const MODE_KEY = 'sanguozhi2_clash';
  function loadMode() {
    try { const v = window.localStorage && localStorage.getItem(MODE_KEY); if (v === 'on' || v === 'fast' || v === 'off') return v; } catch (e) { /* 忽略 */ }
    return 'on';
  }

  // ======================================================= SG.Clash --
  let queue = Promise.resolve();
  const Clash = {
    enabled: true,
    speed: 1,
    active: null,
    debug: { freezeAt: null, holdCut: false },
    _cut: null,
    TL,
    terrainKind,
    get mode() { return !this.enabled ? 'off' : (this.speed > 1.01 ? 'fast' : 'on'); },
    set mode(m) {
      if (m === 'off') { this.enabled = false; this.speed = 1; }
      else if (m === 'fast') { this.enabled = true; this.speed = 2; }
      else { this.enabled = true; this.speed = 1; }
      try { if (window.localStorage) localStorage.setItem(MODE_KEY, this.mode); } catch (e) { /* 忽略 */ }
    },
    cycleMode() { const order = ['on', 'fast', 'off']; this.mode = order[(order.indexOf(this.mode) + 1) % 3]; return this.mode; },
    modeLabel() { return { on: '开', fast: '快', off: '关' }[this.mode]; },

    play(opts) {
      const job = queue.then(() => this._play(opts || {}));
      queue = job.catch(() => {});
      return job;
    },
    async _play(opts) {
      if (!this.enabled || typeof document === 'undefined' || !SG.Gfx || !SG.Gfx.renderer || !SG.Gfx.pushScreen) return { skipped: false, disabled: true };
      let sc = null;
      try {
        sc = new ClashScene(opts);
        this.active = sc;
        await sc.run();
        return { skipped: sc.skipped, disabled: false, seconds: +sc.real.toFixed(2) };
      } catch (e) {
        console.error(e);
        return { skipped: false, disabled: true, error: String(e) };
      } finally {
        if (sc) { try { sc.dispose(); } catch (e) { console.error(e); } }
        if (this._cut) this._cut.end();
        this.active = null;
      }
    },
    cutIn(o) {
      o = o || {};
      if (!this.enabled || typeof document === 'undefined') return Promise.resolve();
      if (this._cut) this._cut.end();
      try {
        const c = new CutIn(o);
        this._cut = c;
        return c.promise;
      } catch (e) {
        console.error(e);
        return Promise.resolve();
      }
    },
    // 由战场数据组装 play() 参数
    fromBattle(model, a, t, beforeA, beforeT, extra) {
      const g = SG.G;
      const fac = u => {
        const st = model && model.setup;
        const idx = st ? (u.side === 0 ? st.attacker : st.defender) : -1;
        const f = g && g.factions && idx >= 0 ? g.factions[idx] : null;
        return f ? (SG.factionColor ? SG.factionColor(f) : f.color) : '#808080';
      };
      const form = u => (u.form && u.form.name) || u.formation;
      const o = {
        attacker: { gen: a.gen, side: a.side, color: fac(a), troopsBefore: beforeA, troopsAfter: a.troops, formation: form(a), culture: a.gen && a.gen.culture },
        defender: { gen: t.gen, side: t.side, color: fac(t), troopsBefore: beforeT, troopsAfter: t.troops, formation: form(t), culture: t.gen && t.gen.culture },
        terrain: model && model.map ? model.map[t.x][t.y] : 0,
        special: null,
        playerSide: null,
      };
      return Object.assign(o, extra || {});
    },
  };
  (function () {
    const m = loadMode();
    Clash.enabled = m !== 'off';
    Clash.speed = m === 'fast' ? 2 : 1;
  })();
  Clash._Scene = ClashScene;          // 测试用
  Clash._proto = ClashScene.prototype;
  SG.Clash = Clash;
})();
