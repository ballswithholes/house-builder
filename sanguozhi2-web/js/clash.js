'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 攻击画面（SG.Clash）
   原作中部队「攻击」时切换到横向交战画面：两军士兵对冲、兵力数字递减。
   这里用独立的三维场景现代化重制（SG.Gfx.pushScreen 全屏场景栈）。
   第四版（DESIGN-V4 §2 美术推广）：布景按试点批准的 Lanternvale 画风重画（SG.LP / SG.LPProps，见「第四版：Lanternvale 风格的布景」一节），
   每场自己的 LP.Mood（时刻：午后 / 黄金时刻 / 黄昏，跟随战场），士兵与武将脚下有柔和的圆影；接口不变（只新增可选的 mood、scene.city / hour）。
   第三版（DESIGN-V3 §1、§2）：人物改用 js/figure.js 的平滑蒙皮模型——步兵 / 弓手为按兵种 × 文化成组的实例化士兵群
   （前排刀盾、其后长枪、弓手，势力色与盾），旗手双手竖持旗枪，武将为中精度名将（专属兵器、与头像一致的面部）
   骑中精度战马（马衣 / 具装）并插帅旗；动作片段由时间轴驱动（行进、开弓放箭、冲锋 / 疾驰、混战劈砍、受击、
   倒地 / 溃逃、收队 / 欢呼）。地形为平滑网格 + 程序化细节着色（草地、踩踏泥土、农田、河岸沙、岩石、雪），
   圆润的树与石块、砖石城墙与半圆拱城门、曲面屋顶，远处为连绵山脊。预算：≤ 350k 三角形、≤ 120 次绘制。
   依赖：js/figure.js（页面未载入时，首次播放按 clash.js 的路径自动载入）。

   公开接口
     await SG.Clash.play({
       attacker: { gen, side, color, troopsBefore, troopsAfter, formation: '鱼鳞', culture },
       defender: { gen, side, color, troopsBefore, troopsAfter, formation: '方圆', culture },
       terrain,           // SG.Terrain 值（防守方所在格）：平原 / 森林 / 山丘 / 山岳 / 河川 / 城墙 / 城门 / 本城
       special: null,     // 可选 { name, color, kind, cry }：冲锋前先播 cutIn 特写，接敌时附带特效与额外伤亡
                          //   kind：'blaze' 火海 / 'storm' 雷击 / 'volley' 箭雨，其余一律为斩击弧光 + 冲击波
       playerSide: 0 | 1 | null,
       scene: { culture, biome, coast },   // 可选（第三版修订）：布景——防守城池的文化（城墙、村舍）、群落（temperate / steppe / desert /
                          //   tropical / savanna / med / boreal / alpine：地面色、树种）、coast（登陆：攻方身后为沙滩与海）；缺省按防守方文化推测
                          //   第四版另可带 city（城门楼牌匾上的城名）、hour（时刻，fromBattle 取战场当前的时刻）
       mood: 'golden',    // 可选（第四版）：时刻（'afternoon' | 'golden' | 'dusk' | 'night' | 小时数）；缺省 scene.hour → 战场的时刻 → 按种子
       speed: 1,          // 可选：本次额外的时间倍率（与 SG.Clash.speed 相乘），如电脑回合传 1.6
     })                   → Promise<{ skipped, disabled, seconds }>（seconds：画面实际播放时长）
       · gen 至少含 name；color 为势力色 '#rrggbb'；formation 可为阵型名或 SG.Defs.formations 下标；
         culture 缺省取 gen.culture，再缺省 'han'（见 DESIGN-V2 §6）：士兵与武将的冠服、甲衣、盾、兵器按文化区分（js/figure.js）。
       · 两军势力色相近时自动拉开色相 / 明暗（separateColors：士兵、武将、马衣都用拉开后的颜色），并各配一种识别色（pickAccents：旗边与旗心）。
       · special 可直接传 specials-data 的条目（kind / fx 映射到火、雷、箭雨，其余为斩击）。
       · 攻方永远在左。兵力数字从 troopsBefore 滚动到 troopsAfter；troopsAfter 为 0 视为溃散。
     await SG.Clash.cutIn({ gen, name, color, side, cry, speed })
                          必杀技 / 单挑用的全屏特写（1.1 秒 ÷（SG.Clash.speed × speed），最短 0.7 秒；轻点 / 空格 / 回车可跳过）：
                          头像滑入 + 招式名大字；cry 为可选台词（显示在招式名下方），speed 为可选额外倍率。enabled 为 false 时立即返回。
     SG.Clash.prepare({ cultures })  → Promise：可选的预热（战斗开始时调用），后台分片生成这些文化的士兵模板与动作贴图
     SG.Clash.enabled     布尔，默认 true；false 时 play / cutIn 立即返回
     SG.Clash.speed       时间倍率，1 = 正常（全长约 3.7 秒），2 = 快
     SG.Clash.mode        'on' | 'fast' | 'off'（读写；写入时同步 enabled / speed，并存入 localStorage 键 sanguozhi2_clash_v3；
                          缺省 'on'——第三版换了新键，旧版存下的「关」不再生效）
     SG.Clash.cycleMode() → 新的 mode（设置按钮「动画：开 / 快 / 关」用）
     SG.Clash.modeLabel() → '开' | '快' | '关'
     SG.Clash.fromBattle(model, a, t, beforeA, beforeT, extra) → play() 的参数
       （BattleController.doAttack 用：a 攻击 t，before* 为 Mdl.attack 之前的兵力）
     SG.Clash.active      正在播放的场景对象或 null（测试用）
     SG.Clash.debug       { freezeAt: null, holdCut: false }：freezeAt 设为秒数时动画停在该时刻；
                          holdCut 为 true 时特写不自动结束（截图用，配合 Web Animations API 定格）
                          cam: { p: [x, y, z], l: [x, y, z] } 时镜头固定在该处（检查模型细节用）

   操作：轻点 / 点击 / 空格 / 回车 跳过（直接显示结果约 0.4 秒后退出）。
   画面期间 <html> 加 sg-clash-on 类：隐藏战场 HUD（.sg-screens 的其他子元素）、世界标签与提示层；其余按键不传给相机。
   多次调用 play 会排队依次播放。
   全部网格、贴图、粒子、人物在退出时释放；士兵用实例化士兵群绘制。粒子 / 旗面 / 水面 / 地形 / 道具 / 特效材质跨场次常驻
   （只为保住着色器程序缓存，避免每次播放重新编译），renderer.info.memory 的几何体与贴图数在多次播放后不增长
   （js/figure.js 的士兵模板与动作贴图按文化 × 兵种缓存、常驻，有上限）。
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
  const ENTER_DX = 1.8;      // 入场时从队形位置后方多远跑进来（第三版：人物按位移换算步频，距离缩短以免滑步）
  const ZC = -0.55;          // 两军纵深中心（three z）
  const FRONT = 2.05;        // 列阵时前排离中线的距离
  const SP_F = 0.54, SP_L = 0.62;
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

  // ======================================================= 人物模型（js/figure.js） --
  // 第三版：士兵、旗手、骑马的武将全部改用 SG.Figure 的平滑蒙皮模型（DESIGN-V3 §1、§8）：
  //   · 步兵 / 弓手：SG.Figure.crowd 实例化士兵群（低精度，按兵种 × 文化成组，势力色为实例属性；绘制次数 = 组数）；
  //   · 旗手：单独的低精度士兵（SkinnedMesh），双手竖持长枪作旗杆，旗面挂在枪杆上；
  //   · 武将：中精度名将模型（专属兵器、与头像一致的面部）骑中精度战马（马衣为势力色，安息 / 萨尔马提亚为鳞甲具装），
  //     马鞍后插帅旗。
  // 模型单位为米、面朝 +Z；本场景沿用旧单位（士兵约 0.9 高），统一缩放 FIG_S。
  // 动作由时间轴直接求出「片段 + 片段内时刻」（纯函数，跳过时可直接摆出结果）：行进 / 冲锋的步频按位移换算，
  // 因此脚步与移动同步，倒放即为后退。
  const FIG_S = 0.5;
  const BACK = 0.6, GBACK = 0.45;          // 收队时步兵 / 武将后撤的距离
  const FLAG_GRIP = new THREE.Vector3(-0.2, 1.12, 0.24), FLAG_DIR = new THREE.Vector3(0.04, 1, 0.1);   // 旗手持杆（人物局部坐标，米）
  function uniq(a) { return a.filter((v, i) => a.indexOf(v) === i); }
  function mixHex(a, b, k) { return '#' + SG.Gfx.color(a).lerp(SG.Gfx.color(b), k).getHexString(); }
  const WOOD = C(0.43, 0.3, 0.17), GOLD = C(0.88, 0.7, 0.28);
  // 步幅（米 / 周期）与步频上限（周期 / 秒）：位移过快时把步幅放大（略有滑步），步频不超过自然值的 1.3 倍
  const GAIT = {
    walk: { stride: 1.45, dur: 1.0 }, run: { stride: 3.0, dur: 0.66 },
    hwalk: { stride: 1.9, dur: 1.1 }, trot: { stride: 2.6, dur: 0.7 }, gallop: { stride: 5.0, dur: 0.5 },
  };
  // 一段匀加速 / 缓动位移的步态时刻：dist = 已走距离（场景单位），D / T = 全程距离与时长
  function gaitT(g, dist, D, T) {
    const G = GAIT[g];
    const stride = Math.max(G.stride * FIG_S, (D / Math.max(0.05, T)) * G.dur / 1.3);
    return (dist / stride) * G.dur;
  }
  // 名驹与毛色（与旧版相同的人物对应；其余按姓名散列）
  const HORSE_OF = {
    '吕布': { name: '赤兔' }, '关羽': { name: '赤兔' }, '刘备': { name: '的卢' }, '曹操': { name: '绝影' },
    '赵云': { name: '白龙驹' }, '公孙瓒': { coat: '#e8e6e0' }, '马超': { coat: '#d8d4ca' }, '张飞': { coat: '#2a2422' },
  };
  const COATS = ['#7a4a2c', '#5e3a22', '#9a7448', '#38302a', '#b8ae9e', '#8a5634'];
  function horseOpts(info, gen) {
    const n = info.name;
    const o = Object.assign({ lod: 'mid', color: info.color, general: true, seed: Math.abs(hashStr(n)) % 5 }, HORSE_OF[n] || {});
    if (!o.name && !o.coat) o.coat = COATS[Math.abs(hashStr(n)) % COATS.length];
    // 具装：安息、萨尔马提亚为鳞甲；其余为势力色马衣
    o.armor = info.culture === 'persia' || info.culture === 'sarmatian' ? 'scale' : 'cloth';
    void gen;
    return o;
  }
  // 兵种：前排刀盾、其后长枪（罗马全为短剑 + 大盾），弓手单列
  function infType(culture, file) { return culture === 'roman' ? (file % 2 ? 'spear' : 'sword') : (file === 0 ? 'sword' : 'spear'); }
  const TYPE_SEED = { spear: 0, sword: 1, archer: 0 };
  function crowdSpecs(cultures) {
    const out = [];
    for (const c of cultures) for (const ty of ['spear', 'sword', 'archer']) out.push({ type: ty, culture: c, seed: TYPE_SEED[ty] });
    return out;
  }
  // figure.js 未随页面载入时（旧版 index.html），按 clash.js 自身的路径就近载入一次
  const CLASH_SRC = (typeof document !== 'undefined' && document.currentScript && document.currentScript.src) || '';
  let figLoading = null;
  let prepChain = null;      // 进行中的 SG.Clash.prepare（播放前先等它做完）
  function ensureFigure() {
    if (SG.Figure) return Promise.resolve(true);
    if (figLoading) return figLoading;
    figLoading = new Promise(resolve => {
      try {
        const s = document.createElement('script');
        s.src = CLASH_SRC ? CLASH_SRC.replace(/clash\.js(\?.*)?$/, 'figure.js') : 'js/figure.js';
        s.onload = () => resolve(!!SG.Figure);
        s.onerror = () => { figLoading = null; resolve(false); };
        document.head.appendChild(s);
      } catch (e) { figLoading = null; resolve(false); }
    });
    return figLoading;
  }
  // 箭（平滑箭杆、箭镞、三片箭羽），沿 three 的 −z 飞行
  function arrowGeo() {
    const mb = new SG.MeshBuilder();
    mb.lathe(V(0, 0, 0), [[0.011, -0.3], [0.011, 0.28]], 5, C(0.52, 0.4, 0.24));
    mb.lathe(V(0, 0, 0), [[0.024, 0.27], [0.004, 0.42]], 5, C(0.78, 0.79, 0.83));
    const fl = C(0.95, 0.94, 0.9), up = V(0, 1, 0);
    for (let k = 0; k < 3; k++) {
      const a = k * Math.PI * 2 / 3, ca = Math.cos(a), sa = Math.sin(a);
      for (const sd of [1, -1]) {
        const nrm = V(-sa * sd, 0, ca * sd);
        mb.triN(V(0, -0.3, 0), V(ca * 0.045, -0.26, sa * 0.045), V(ca * 0.04, -0.15, sa * 0.04), nrm, nrm, nrm, fl);
        mb.triN(V(0, -0.3, 0), V(ca * 0.04, -0.15, sa * 0.04), V(0, -0.17, 0), nrm, nrm, nrm, fl);
      }
      void up;
    }
    // 建模沿 y 轴，转到 −z（three）方向。第三版人物约 0.93 单位（1.8 米）高：箭缩到约 0.43 单位（约 0.8 米），
    // 箭杆略粗于等比（远景仍看得见），箭羽随之变小
    const g = mb.toGeometry();
    g.scale(0.8, 0.6, 0.8);
    g.rotateX(-Math.PI / 2);
    return g;
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
    else if (kind === 'slash') { o.side = THREE.DoubleSide; o.map = slashTexture(); }
    else if (kind === 'shock') o.map = SG.Gfx.ringTexture;
    else if (kind === 'bolt') o.vertexColors = true;
    const m = new THREE.MeshBasicMaterial(o);
    m.userData.fxKind = kind;
    return m;
  }
  // 斩光贴图：u = 径向（0 内缘 → 1 外缘），v = 沿弧（0 → 1）。外缘内侧一道亮芯，内缘柔和渐隐，两端渐隐
  function slashTexture() {
    if (MATS.slashTex) return MATS.slashTex;
    const W = 64, Hh = 64, cv = document.createElement('canvas');
    cv.width = W; cv.height = Hh;
    const g = cv.getContext('2d'), img = g.createImageData(W, Hh);
    const ss = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };
    for (let y = 0; y < Hh; y++) for (let x = 0; x < W; x++) {
      const u = (x + 0.5) / W, v = (y + 0.5) / Hh;
      const body = ss(0.0, 0.7, u) * (1 - ss(0.9, 1.0, u));
      const core = Math.exp(-Math.pow((u - 0.8) / 0.07, 2));
      const ends = Math.pow(Math.sin(Math.PI * v), 0.6);
      const a = clamp01((body * 0.55 + core) * ends);
      const w = clamp01(0.55 + core * 0.6);
      const i = (y * W + x) * 4;
      img.data[i] = img.data[i + 1] = img.data[i + 2] = Math.round(255 * w * a); img.data[i + 3] = Math.round(255 * a);
    }
    g.putImageData(img, 0, 0);
    const t = new THREE.CanvasTexture(cv);
    t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
    MATS.slashTex = t;
    return t;
  }
  // 月牙几何：外缘半径 r1，内缘自中部 r0 向两端收拢到 r1；wid（0–1）：弧长占比（全长 ±1.05 弧度）
  function slashGeo(r0, r1, wid) {
    const N = 24, A = 1.05 * (wid || 1), pos = [], uv = [], idx = [];
    for (let i = 0; i <= N; i++) {
      const v = i / N, a = -A + 2 * A * v, ri = r1 - (r1 - r0) * Math.pow(Math.sin(Math.PI * v), 0.7);
      pos.push(Math.cos(a) * ri, Math.sin(a) * ri, 0, Math.cos(a) * r1, Math.sin(a) * r1, 0);
      uv.push(0, v, 1, v);
      if (i < N) { const k = i * 2; idx.push(k, k + 1, k + 3, k, k + 3, k + 2); }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setIndex(idx);
    return g;
  }
  function releaseFx(m) {
    const k = m && m.userData && m.userData.fxKind;
    if (k) MATS.fx[k].push(m); else if (m) m.dispose();
  }


  // ---------------------------------------------------------- 第三版：平滑地形与道具材质 --
  // 在 LowPoly 光照（与主场景一致的半兰伯特、三色环境光、阴影）之上加程序化细节（与战略地图同一套细节纹理 SG.Gfx.terrainDetail）。
  // 全部跨场次常驻（着色器程序只编译一次）；时间等 uniform 由场景每帧写入。
  const SHARED_U = { time: { value: 0 }, snow: { value: 99 } };
  // 细节纹理：与战略地图同一份数据，但另建不做各向异性过滤的副本（攻击画面机位低、地面占满画面，各向异性采样太贵）
  function detailTex() {
    if (MATS.det) return MATS.det;
    let d = null;
    try { d = SG.Gfx.terrainDetail(); } catch (e) { return null; }
    if (!d || !d.a) return null;
    const cp = t => { const c = t.clone(); c.anisotropy = 1; c.needsUpdate = true; return c; };
    MATS.det = { a: cp(d.a), b: cp(d.b) };
    return MATS.det;
  }
  function lpBase() { const m = SG.Gfx.newLowPoly(); return { m, base: m.onBeforeCompile }; }
  // 地面：顶点色 = 底色；aGnd = (泥土 / 踩踏 / 道路, 沙, 岩石倾向, 农田)。草地按三级细节纹理明暗、干湿斑块，
  // 泥土与沙按噪声边缘混入，陡坡与高处为三平面投影的岩石，雪线（SHARED_U.snow）以上积雪
  // 低频斑块噪声（周期约 14 单位）在顶点里采样后插值；近处才采样最细一级（远处省一次采样）
  const GROUND_VS = ['attribute vec4 aGnd;', 'uniform sampler2D sgDetA;', 'varying vec4 vCG;', 'varying vec3 vCW;', 'varying vec3 vCN;', 'varying vec4 vCM;', 'varying float vCD;'].join('\n') + '\n';
  const GROUND_VS_MAIN = '#include <project_vertex>\n\tvCG = aGnd;\n\tvCW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;\n\tvCN = normalize( mat3( modelMatrix ) * objectNormal );\n' +
    '\tvCM = texture2D( sgDetA, vCW.xz * 0.071 + vec2( 0.31, 0.67 ) );\n\tvCD = - mvPosition.z;\n';
  const GROUND_FS_PARS = `uniform sampler2D sgDetA;
uniform sampler2D sgDetB;
uniform float sgSnowH;
varying vec4 vCG;
varying vec3 vCW;
varying vec3 vCN;
varying vec4 vCM;
varying float vCD;
vec3 sgCropC( float id ) {
  vec3 c = vec3( 0.46, 0.6, 0.25 );
  c = mix( c, vec3( 0.78, 0.68, 0.33 ), step( 0.24, id ) );
  c = mix( c, vec3( 0.56, 0.44, 0.3 ), step( 0.42, id ) );
  c = mix( c, vec3( 0.36, 0.52, 0.22 ), step( 0.56, id ) );
  c = mix( c, vec3( 0.66, 0.64, 0.32 ), step( 0.72, id ) );
  c = mix( c, vec3( 0.5, 0.63, 0.28 ), step( 0.87, id ) );
  return c;
}
`;
  const GROUND_FS = `#include <color_fragment>
	{
		vec2 p = vCW.xz;
		vec4 dA = texture2D( sgDetA, p * 0.42 );
		vec4 dM = vCM;
		float dF = 0.5;
#ifndef SG_LITE
		if ( vCD < 30.0 ) dF = mix( texture2D( sgDetA, p * 1.6 + vec2( 0.53, 0.19 ) ).r, 0.5, smoothstep( 20.0, 30.0, vCD ) );
#endif
		vec3 n = normalize( vCN );
		float slope = 1.0 - n.y;
		vec3 base = diffuseColor.rgb;
		vec3 col = base * ( 0.8 + 0.42 * ( dA.r * 0.34 + dF * 0.3 + dM.r * 0.36 ) );
		col = mix( col, col * vec3( 1.1, 1.04, 0.8 ), smoothstep( 0.5, 0.8, dM.g ) * 0.5 );
		col = mix( col, col * vec3( 0.8, 0.88, 0.76 ), smoothstep( 0.55, 0.85, dM.a ) * 0.5 );
		// 农田：田块、犁沟、田埂
		if ( vCG.w > 0.01 ) {
			vec4 dB = texture2D( sgDetB, p * 0.045 + vec2( 0.13, 0.71 ) );
			vec3 crop = sgCropC( dB.b ) * ( 0.8 + 0.34 * dB.a );
			crop = mix( crop, vec3( 0.6, 0.52, 0.38 ), step( dB.b, 0.03 ) );
			col = mix( col, crop, smoothstep( 0.25, 0.6, vCG.w + ( dM.r - 0.5 ) * 0.4 ) * ( 1.0 - smoothstep( 0.12, 0.26, slope ) ) );
		}
		// 泥土：踩踏、道路
		float dW = smoothstep( 0.3, 0.72, vCG.x + ( dM.a - 0.5 ) * 0.45 + ( dA.g - 0.5 ) * 0.35 );
		vec3 dirt = mix( vec3( 0.55, 0.46, 0.33 ), base * vec3( 0.95, 0.85, 0.7 ), 0.25 ) * ( 0.76 + 0.46 * dA.g );
		col = mix( col, dirt, dW );
		// 沙：河岸、浅滩
		float sW = smoothstep( 0.3, 0.7, vCG.y + ( dA.b - 0.5 ) * 0.3 );
		vec3 sand = vec3( 0.8, 0.74, 0.57 ) * ( 0.86 + 0.2 * dF + 0.08 * dA.b );
		col = mix( col, sand, sW );
		// 岩石：陡坡与山巅（三平面投影）
		float rW = smoothstep( 0.3, 0.52, slope + ( dM.a - 0.5 ) * 0.22 + vCG.z * 0.35 );
		if ( rW > 0.004 ) {
			// 按主轴选一个投影面（一次采样；陡坡不拉伸）
			vec2 ruv = mix( vCW.xy, vCW.zy, step( abs( n.z ), abs( n.x ) ) );
			ruv = mix( ruv, vCW.xz, step( 0.75, n.y ) );
			float r = texture2D( sgDetB, ruv * 0.11 ).r;
			vec3 rc = mix( vec3( 0.5, 0.48, 0.45 ), vec3( 0.58, 0.5, 0.4 ), dM.r ) * ( 0.58 + 0.72 * r );
			col = mix( col, rc, rW );
		}
		// 雪
		float snW = smoothstep( sgSnowH, sgSnowH + 3.0, vCW.y + ( dM.a - 0.5 ) * 4.0 ) * ( 1.0 - smoothstep( 0.45, 0.75, slope ) );
		col = mix( col, vec3( 0.86, 0.88, 0.92 ) * ( 0.9 + 0.12 * dA.a ), snW );
		diffuseColor.rgb = col;
	}
`;
  function groundMaterial() {
    if (MATS.ground) return MATS.ground;
    const det = detailTex();
    const { m, base } = lpBase();
    const u = { sgDetA: { value: det && det.a }, sgDetB: { value: det && det.b }, sgSnowH: SHARED_U.snow };
    // 触屏设备（手机 GPU、高像素密度）：地面省掉最细一级细节纹理（每像素少一次采样；近处的草地颗粒略粗）
    const lite = !!SG.isTouch;
    m.onBeforeCompile = function clashGround(sh, r) {
      base.call(this, sh, r);
      Object.assign(sh.uniforms, u);
      sh.vertexShader = GROUND_VS + sh.vertexShader.replace('#include <project_vertex>', GROUND_VS_MAIN);
      sh.fragmentShader = (lite ? '#define SG_LITE\n' : '') + GROUND_FS_PARS + sh.fragmentShader.replace('#include <color_fragment>', GROUND_FS);
    };
    m.customProgramCacheKey = () => 'sgClashGround' + (lite ? 'L' : '');
    m.name = 'ClashGround';
    MATS.ground = m;
    return m;
  }
  // 道具（树、石、城墙、屋顶、木件合并为一个网格）：aKind 0 原色 / 1 树叶（叶簇明暗 + 法线起伏）/ 2 石块（颗粒、层理）/
  // 3 砖石墙（按世界坐标的错缝砖、灰缝；平面为方砖）/ 4 筒瓦屋面
  const PROP_VS = 'attribute float aKind;\nvarying float vKind;\nvarying vec3 vPW;\nvarying vec3 vPN;\n';
  const PROP_VS_MAIN = '#include <project_vertex>\n\tvKind = aKind;\n\tvPW = ( modelMatrix * vec4( transformed, 1.0 ) ).xyz;\n\tvPN = normalize( mat3( modelMatrix ) * objectNormal );\n';
  const PROP_FS_PARS = `varying float vKind;
varying vec3 vPW;
varying vec3 vPN;
float sgPN( vec3 p ) { return sin( p.x + sin( p.y * 1.3 ) * 1.2 ) * sin( p.y * 1.1 + sin( p.z * 1.2 ) * 1.2 ) * sin( p.z * 0.9 + sin( p.x * 1.4 ) * 1.2 ); }
float sgPH( vec2 p ) { return fract( sin( dot( p, vec2( 127.1, 311.7 ) ) ) * 43758.5453 ); }
`;
  const PROP_FS = `#include <color_fragment>
	float sgL1 = 0.0, sgL2 = 0.0, sgL3 = 0.0, sgLeafW = 0.0;
	{
		vec3 n = normalize( vPN ), p = vPW;
		float k = 1.0;
		if ( vKind > 0.5 && vKind < 2.5 ) {
			vec3 q = p * ( vKind < 1.5 ? 4.2 : 3.0 );
			sgL1 = sgPN( q ); sgL2 = sgPN( q * 2.63 + 5.3 ); sgL3 = sgPN( q * 6.1 + 1.7 );
			if ( vKind < 1.5 ) {
				float c = sgL1 * 0.5 + sgL2 * 0.32 + sgL3 * 0.18;
				k = 0.82 + 0.36 * c + 0.16 * smoothstep( 0.35, 0.85, sgL2 * 0.6 + sgL3 * 0.4 );
				sgLeafW = 0.45;
			} else {
				float c = sgL1 * 0.25 + sgL2 * 0.35 + sgL3 * 0.4;
				k = 0.84 + 0.24 * c + 0.1 * sin( p.y * 13.0 + sgL1 * 2.0 );
				sgLeafW = 0.28;
			}
		} else if ( vKind > 2.5 && vKind < 3.5 ) {
			float ay = abs( n.y );
			if ( ay < 0.6 ) {
				float u = abs( n.x ) > abs( n.z ) ? p.z : p.x;
				float row = floor( p.y / 0.21 );
				float uu = u / 0.46 + mod( row, 2.0 ) * 0.5;
				float fy = fract( p.y / 0.21 ), fu = fract( uu );
				float mort = 1.0 - smoothstep( 0.04, 0.12, min( fy, 1.0 - fy ) ) * smoothstep( 0.02, 0.06, min( fu, 1.0 - fu ) );
				float h = sgPH( vec2( floor( uu ), row ) );
				k = ( 0.88 + 0.2 * h ) * ( 1.0 - 0.3 * mort ) * ( 0.94 + 0.08 * sgPN( p * 3.1 ) );
			} else {
				vec2 g = p.xz / 0.5;
				vec2 f = abs( fract( g ) - 0.5 );
				k = ( 0.9 + 0.12 * sgPH( floor( g ) ) ) * ( 0.84 + 0.16 * ( 1.0 - smoothstep( 0.42, 0.48, max( f.x, f.y ) ) ) );
			}
		} else if ( vKind > 3.5 ) {
			vec2 t = normalize( vec2( - n.z, n.x ) + vec2( 1e-4, 0.0 ) );
			float u = dot( p.xz, t );
			float roll = 0.5 + 0.5 * sin( u / 0.16 * 6.2832 );
			float fy = fract( p.y / 0.11 );
			k = ( 0.8 + 0.26 * roll * roll ) * ( 1.0 - 0.2 * ( 1.0 - smoothstep( 0.0, 0.25, fy ) ) );
		}
		diffuseColor.rgb *= k;
	}
`;
  const PROP_FS_NORMAL = '#include <normal_fragment_maps>\n\tnormal = normalize( normal + sgLeafW * vec3( sgL2 + sgL3 * 0.5, sgL1 * 0.6, sgL3 - sgL1 * 0.4 ) );';
  function propMaterial() {
    if (MATS.prop) return MATS.prop;
    const { m, base } = lpBase();
    m.onBeforeCompile = function clashProp(sh, r) {
      base.call(this, sh, r);
      sh.vertexShader = PROP_VS + sh.vertexShader.replace('#include <project_vertex>', PROP_VS_MAIN);
      sh.fragmentShader = PROP_FS_PARS + sh.fragmentShader.replace('#include <color_fragment>', PROP_FS).replace('#include <normal_fragment_maps>', PROP_FS_NORMAL);
    };
    m.customProgramCacheKey = () => 'sgClashProp';
    m.name = 'ClashProp';
    MATS.prop = m;
    return m;
  }
  // 草丛 / 芦苇 / 野花（实例化）：aTint 为 1 的顶点乘实例颜色（草叶、花瓣），随风轻摆（按顶点高度）
  function grassMaterial() {
    if (MATS.grass) return MATS.grass;
    const { m, base } = lpBase();
    m.onBeforeCompile = function clashGrass(sh, r) {
      base.call(this, sh, r);
      sh.uniforms.sgWindT = SHARED_U.time;
      sh.vertexShader = 'attribute float aTint;\nuniform float sgWindT;\n' + sh.vertexShader
        .replace('#include <color_vertex>', '#include <color_vertex>\n#if defined( USE_INSTANCING_COLOR ) && defined( USE_COLOR )\n\tvColor.xyz = color.xyz * mix( vec3( 1.0 ), instanceColor.xyz, aTint );\n#endif')
        .replace('#include <begin_vertex>', '#include <begin_vertex>\n#ifdef USE_INSTANCING\n\tfloat sgPh = instanceMatrix[3].x * 0.9 + instanceMatrix[3].z * 0.6;\n\ttransformed.x += sin( sgWindT * 2.1 + sgPh ) * transformed.y * 0.2;\n\ttransformed.z += cos( sgWindT * 1.6 + sgPh * 1.3 ) * transformed.y * 0.12;\n#endif');
    };
    m.customProgramCacheKey = () => 'sgClashGrass';
    m.name = 'ClashGrass';
    MATS.grass = m;
    return m;
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
  // 第三版：平滑地形（索引网格 + 平滑法线 + 地形着色器的程序化细节），圆润的树、平滑石块，砖石城墙、拱形城门与曲面屋顶；
  // 远处为连绵的山脊（同一张网格向后延伸）；草丛 / 芦苇 / 野花为实例化网格（随风轻摆）。
  const RIVER = { x: 1.05, w: 1.7, bank: 1.5, bed: -0.42, surf: -0.1 };
  const WALL_Z = -7.4, GATE_X = 4.6;     // 城墙正面（three z）与城门中心 x
  function isWalled(kind) { return kind === 'gate' || kind === 'castle' || kind === 'wall'; }
  function makeHeight(kind, seed, look) {
    const r = SG.SeededRandom(seed);
    const coast = !!(look && look.coast);
    const ox = r.nextDouble() * 200, oz = r.nextDouble() * 200;
    const pn = (x, z, f) => M.perlinNoise(ox + x * f, oz + z * f) - 0.465;
    const hilly = kind === 'hill' || kind === 'mountain';
    const walled = isWalled(kind);
    return function h(x, z) {
      const far = sstep(9, 42, -z);
      const calm = 1 - 0.55 * (1 - sstep(5, 10, Math.abs(z - ZC))) * (1 - sstep(7, 13, Math.abs(x)));
      let y;
      if (hilly) y = (pn(x, z, 0.1) * 1.4 + pn(x, z, 0.32) * 0.22) * calm + 0.7 * sstep(-3, 7.5, x) - 0.18;
      else if (walled) y = pn(x, z, 0.22) * 0.07;
      else y = (pn(x, z, 0.15) * 0.34 + pn(x, z, 0.5) * 0.05) * calm;
      y += far * ((pn(x, z, 0.03) + 0.25) * (hilly ? 14 : 7) + (hilly ? 2.5 : 0.6));
      y += sstep(16, 34, Math.abs(x)) * (hilly ? 3 : 1.4) * (0.6 + pn(x, z, 0.06));
      // 远山：平滑的脊状噪声
      const rk = sstep(44, 74, -z);
      if (rk > 0) {
        const rn = 1 - Math.min(1, Math.abs(pn(x, z, 0.021) * 2.3));
        y += rk * (rn * rn * (hilly ? 30 : 15) + (pn(x, z, 0.07) + 0.3) * (hilly ? 7 : 3.5));
      }
      if (kind === 'river') {
        // 近处：河床与缓坡河岸；远处：河谷两侧以有限的坡度升起（不在山脊上切出直壁峡谷）
        const d = Math.abs(x - RIVER.x);
        const bank = sstep(RIVER.w, RIVER.w + RIVER.bank, d);
        const near = lerp(RIVER.bed + pn(x, z, 0.8) * 0.04, Math.max(y * 0.6, -0.02) + 0.06, bank);
        const vf = sstep(-6, -26, z);
        if (vf > 0) {
          const valley = lerp(RIVER.bed, 0.06, bank) + Math.max(0, d - RIVER.w - RIVER.bank) * (0.18 + 0.22 * sstep(-20, -70, z)) + Math.max(0, d - 7) * 0.12;
          y = lerp(near, Math.min(y, valley), vf);
        } else y = near;
      }
      if (coast) {
        // 登陆：海岸线以外降为缓坡沙滩与海床（远山也不在海上隆起）
        const d = x - shoreX(z);
        if (d < 5) {
          const beach = Math.max(-1.5, SEA_Y + 0.04 + d * 0.065);
          y = lerp(y, beach, sstep(5, 0.6, d));
        }
      }
      return y;
    };
  }
  // 顶点底色与地表权重 aGnd = (泥土 / 踩踏 / 道路, 沙, 岩石倾向, 农田)
  function groundAttr(kind, x, z, y, n, nf, out, look) {
    let c;
    let dirt = 0, sand = 0, rock = 0, farm = 0;
    const walled = isWalled(kind);
    const trample = Math.exp(-(x * x) / 12 - ((z - ZC) * (z - ZC)) / 8);
    switch (kind) {
      case 'forest': c = C(0.33, 0.5, 0.26); break;
      case 'hill': c = C(0.52, 0.62, 0.33); break;
      case 'mountain': c = C(0.5, 0.56, 0.36); break;
      case 'gate': case 'castle': case 'wall': c = C(0.5, 0.6, 0.35); break;
      default: c = C(0.46, 0.64, 0.31); break;
    }
    c.lerp(C(0.58, 0.68, 0.33), clamp01(n * 1.6) * 0.45);
    // 群落底色（草原枯黄、沙漠沙土、热带深绿、地中海橄榄绿……）
    if (look && look.grass) c.lerp(shade(look.grass, (n - 0.1) * 0.25), kind === 'forest' ? 0.6 : 0.8);
    if (look && look.sandy) sand = Math.max(sand, look.sandy * sstep(-0.05, 0.25, nf + n * 0.5));
    if (walled) {
      // 墙前夯土与车辙大道、门前石板、墙根阴湿
      const earth = (1 - sstep(2, 7, z - WALL_Z)) * sstep(WALL_Z - 2, WALL_Z - 0.5, z);
      dirt = Math.max(dirt, earth * 0.6 + trample * 0.35);
      if (kind !== 'wall') {
        const d = Math.abs(x - GATE_X), w = 1.45 + Math.max(0, z - WALL_Z) * 0.07;
        const road = 1 - sstep(w - 0.35, w + 0.35, d);
        dirt = Math.max(dirt, road);
        if (z < WALL_Z + 2.4 && d < 2.3 && !(look && look.wall === 'palisade')) { const pv = 1 - sstep(1.7, 2.3, d); c.lerp(C(0.64, 0.62, 0.58), pv); dirt *= 1 - pv; rock = -pv; }
      }
      c.lerp(C(0.42, 0.42, 0.36), (1 - sstep(WALL_Z, WALL_Z + 1.2, z)) * 0.35);
    } else {
      dirt = trample * (kind === 'forest' ? 0.55 : 0.7);
      if (kind === 'forest') dirt = Math.max(dirt, sstep(-4, -9, z) * 0.35 * (0.5 + nf));
      if ((kind === 'plain' || kind === 'river') && z < -12 && z > -40 && Math.abs(x) < 46) {
        farm = sstep(-12, -15, z) * (1 - sstep(-34, -40, z)) * (1 - sstep(38, 46, Math.abs(x))) * sstep(-0.05, 0.12, nf - (1 - (look ? look.farm : 1)) * 0.5);
        if (kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w + RIVER.bank + 2.5) farm = 0;
      }
    }
    if (kind === 'river') {
      const d = Math.abs(x - RIVER.x);
      sand = 1 - sstep(RIVER.w + RIVER.bank - 0.3, RIVER.w + RIVER.bank + 0.7, d);
      if (d < RIVER.w) c.lerp(C(0.5, 0.46, 0.36), 0.6);
    }
    if (look && look.coast) {
      // 海滩：海岸线内外约 3 单位为沙，水下也是沙（透出浅海的青绿）
      const d = x - shoreX(z);
      const b = 1 - sstep(1.4, 3.6, d + (nf - 0.1) * 1.5);
      sand = Math.max(sand, b); farm *= sstep(4, 8, d); dirt *= sstep(0.5, 2.5, d);
      if (d < 0.2) c.lerp(C(0.62, 0.58, 0.46), 0.6);
    }
    if (kind === 'mountain') rock = Math.max(rock, sstep(2.5, 7, y) * 0.8);
    if (kind === 'hill') rock = Math.max(rock, sstep(5, 10, y) * 0.5);
    // 远山：高处草色转黄绿、再到岩石
    const hi = sstep(6, 22, y) * sstep(30, 50, -z);
    if (hi > 0) { c.lerp(C(0.55, 0.58, 0.4), hi * 0.7); rock = Math.max(rock, hi * 0.6); }
    // 远处偏冷（雾会进一步融合）
    c.lerp(C(0.48, 0.58, 0.46), sstep(16, 60, -z) * 0.35);
    out.c = c; out.g0 = clamp01(dirt); out.g1 = clamp01(sand); out.g2 = rock; out.g3 = clamp01(farm);
  }
  function axis(a, b, step, fine, grow, cap) {
    // 中心 ±fine 内用 step，之外按 grow 递增（不超过 cap）
    const st = x => Math.min(cap || 1e9, Math.abs(x) < fine ? step : step + (Math.abs(x) - fine) * grow);
    const out = [];
    for (let x = 0; x <= b; ) { out.push(x); x += st(x); }
    const neg = [];
    for (let x = -step; x >= a; ) { neg.push(x); x -= st(x); }
    return neg.reverse().concat(out);
  }

  // 道具网格：按 aKind 分成几个 MeshBuilder，最后合并为一个几何体（一次绘制 + 一次投影）
  const K_PLAIN = 0, K_LEAF = 1, K_ROCK = 2, K_BRICK = 3, K_ROOF = 4;
  function propBuilders() { return [0, 1, 2, 3, 4].map(() => new SG.MeshBuilder()); }
  function mergeProps(mbs) {
    const geos = mbs.map(mb => (mb.pos.length ? mb.toGeometry() : null));
    let n = 0;
    for (const g of geos) if (g) n += g.getAttribute('position').count;
    const pos = new Float32Array(n * 3), nor = new Float32Array(n * 3), col = new Float32Array(n * 3), kind = new Float32Array(n);
    let o = 0;
    geos.forEach((g, k) => {
      if (!g) return;
      const c = g.getAttribute('position').count;
      pos.set(g.getAttribute('position').array, o * 3);
      nor.set(g.getAttribute('normal').array, o * 3);
      col.set(g.getAttribute('color').array, o * 3);
      kind.fill(k, o, o + c);
      o += c;
      g.dispose();
    });
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
    g.setAttribute('color', new THREE.BufferAttribute(col, 3));
    g.setAttribute('aKind', new THREE.BufferAttribute(kind, 1));
    g.computeBoundingBox();
    g.computeBoundingSphere();
    return g;
  }
  // 树：平滑树干、圆润树冠（近景另加三团叶簇，轮廓起伏）；pine 为层叠的松冠，poplar 为高耸的杨
  // （x, y, z 为 three 坐标，s ≈ 树高）
  function treeInto(B, x, y, z, s, type, leaf, seed, near) {
    const tk = B[K_PLAIN], lf = B[K_LEAF];
    const p = P(x, y, z);
    const TR = C(0.38, 0.27, 0.16);
    // 第三版修订：树冠一律 detail 2（中远景也不再露出多边形轮廓），法线向椭球法线混合（明暗圆润）
    const ts = near ? 9 : 6, cs = near ? 16 : 12, d = 2;
    const crown = (cx, cy, cz, r, col, sd) => {
      lf.sphere(V(p.x + cx, p.y + cy, p.z + cz), r, col, d, { seed: sd, rough: 0.09, bottomShade: 0.32, top: 0.08, smoothN: 0.6 });
      if (!near) return;
      for (let k = 0; k < 3; k++) {
        const a = sd * 1.7 + k * 2.09, up = 0.25 + 0.3 * ((k + sd) % 2);
        lf.sphere(V(p.x + cx + Math.cos(a) * r.x * 0.62, p.y + cy + up * r.y, p.z + cz + Math.sin(a) * r.z * 0.62), V(r.x * 0.5, r.y * 0.52, r.z * 0.5),
          shade(col, k === 1 ? -0.06 : 0.04), 1, { seed: sd + 7 * k + 1, rough: 0.07, bottomShade: 0.28, top: 0.1, smoothN: 0.6 });
      }
    };
    if (type === 'palm') {
      // 棕榈 / 椰枣：微弯的环纹树干（石块材质的横纹）+ 9 片下垂的羽状叶
      const lean = (((seed % 7) - 3) * 0.05 + 0.04) * s, n = 6, pts = [], rr = [];
      for (let i = 0; i < n; i++) { const u = i / (n - 1); pts.push(V(p.x + lean * u * u, p.y + u * 0.92 * s, p.z + lean * 0.35 * u * u)); rr.push(lerp(0.06, 0.034, u) * s); }
      B[K_ROCK].tube(pts, rr, 7, C(0.5, 0.42, 0.31), false);
      const top = pts[n - 1];
      lf.sphere(V(top.x, top.y + 0.01 * s, top.z), 0.055 * s, shade(leaf, -0.22), 1, { smoothN: 1 });
      for (let k = 0; k < 9; k++) {
        const a = k * 2.399 + seed * 0.37, L = (0.42 + 0.08 * ((k * 7 + seed) % 3)) * s;
        frondInto(lf, top, Math.cos(a), Math.sin(a), L, (k % 3 === 0 ? 0.2 : 0.13) * s, 0.075 * s, shade(leaf, ((k % 3) - 1) * 0.05));
      }
      return;
    }
    if (type === 'cypress') {
      // 地中海柏树：深绿、细高的尖塔形
      tk.lathe(p, [[0.05 * s, 0], [0.035 * s, 0.16 * s]], ts, TR);
      lf.lathe(V(p.x, p.y + 0.1 * s, p.z), [[0.02 * s, 0], [0.15 * s, 0.1 * s], [0.18 * s, 0.32 * s], [0.14 * s, 0.66 * s], [0.06 * s, 0.9 * s], [0, 1.0 * s]], cs, shade(leaf, -0.14));
      return;
    }
    if (type === 'olive' || type === 'acacia' || type === 'umbrella' || type === 'scrub') {
      // 橄榄（扭曲矮干、灰绿宽冠）/ 金合欢（斜干分叉、伞形平顶）/ 石松（高直干、扁平冠）/ 灌丛
      const sd = seed % 13;
      if (type === 'scrub') { lf.sphere(V(p.x, p.y + 0.12 * s, p.z), V(0.3 * s, 0.2 * s, 0.28 * s), shade(leaf, 0.02), 2, { seed: sd, rough: 0.12, bottomShade: 0.35, flat: -0.4, smoothN: 0.5 }); return; }
      const hy = type === 'olive' ? 0.36 : type === 'acacia' ? 0.62 : 0.66, bend = (sd % 2 ? 1 : -1) * (type === 'olive' ? 0.07 : 0.12) * s;
      tk.tube([V(p.x, p.y, p.z), V(p.x + bend * 0.6, p.y + hy * 0.5 * s, p.z), V(p.x + bend, p.y + hy * s, p.z + bend * 0.3)], [0.07 * s, 0.05 * s, 0.035 * s], ts, type === 'olive' ? C(0.42, 0.36, 0.28) : TR, false);
      if (type === 'acacia') tk.tube([V(p.x + bend * 0.5, p.y + hy * 0.45 * s, p.z), V(p.x - bend * 0.9, p.y + hy * s, p.z - bend * 0.2)], [0.035 * s, 0.025 * s], 6, TR, false);
      const lc = type === 'olive' ? C(0.45, 0.52, 0.36) : leaf;
      const r = type === 'olive' ? V(0.42 * s, 0.27 * s, 0.4 * s) : V(0.5 * s, 0.13 * s, 0.46 * s);
      crown(bend, (hy + (type === 'olive' ? 0.14 : 0.06)) * s, 0, r, lc, seed);
      if (type !== 'umbrella') crown(bend - 0.22 * s, (hy + 0.02) * s, 0.1 * s, V(r.x * 0.62, r.y * 0.8, r.z * 0.6), shade(lc, 0.05), seed + 5);
      return;
    }
    if (type === 'pine') {
      tk.lathe(p, [[0.06 * s, 0], [0.04 * s, 0.32 * s]], ts, TR);
      const tier = (y0, r, h, col) => lf.lathe(V(p.x, p.y + y0 * s, p.z), [[0, h * s * 0.1], [r * s, 0, null, true], [r * s * 0.5, h * s * 0.5], [0, h * s]], cs, col);
      tier(0.2, 0.44, 0.62, shade(leaf, -0.06)); tier(0.52, 0.34, 0.56, leaf); tier(0.8, 0.22, 0.42, shade(leaf, 0.06));
    } else if (type === 'poplar') {
      tk.lathe(p, [[0.05 * s, 0], [0.035 * s, 0.26 * s]], ts, TR);
      crown(0, 0.78 * s, 0, V(0.18 * s, 0.56 * s, 0.18 * s), leaf, seed);
    } else if (type === 'broad2') {
      tk.lathe(p, [[0.065 * s, 0], [0.045 * s, 0.4 * s]], ts, TR);
      crown(0.06 * s, 0.6 * s, 0, V(0.38 * s, 0.34 * s, 0.38 * s), leaf, seed);
      crown(-0.15 * s, 0.82 * s, 0.06 * s, V(0.28 * s, 0.25 * s, 0.28 * s), shade(leaf, 0.06), seed + 3);
    } else {
      tk.lathe(p, [[0.065 * s, 0], [0.045 * s, 0.42 * s]], ts, TR);
      crown(0, 0.66 * s, 0, V(0.44 * s, 0.4 * s, 0.44 * s), leaf, seed);
    }
  }
  function rockInto(B, x, y, z, r, col, seed) {
    B[K_ROCK].sphere(P(x, y, z), V(r * 1.25, r * 0.75, r), col, r > 0.3 ? 2 : 1, { seed, rough: 0.2, flat: -0.35, bottomShade: 0.25, top: 0.08, smoothN: 0.35 });
  }
  // 羽状叶（双面，中脉略折）：o 为叶柄起点（Unity），(ca, sa) 为水平方向，L 长、up 起拱、w 半宽
  function frondInto(mb, o, ca, sa, L, up, w, col) {
    const N = 5, sx = -sa, sz = ca, row = [];
    for (let i = 0; i <= N; i++) {
      const u = i / N, cx = o.x + ca * L * u, cz = o.z + sa * L * u, cy = o.y + up * Math.sin(Math.PI * u * 0.9) - 0.42 * L * u * u;
      const wd = w * Math.pow(Math.sin(Math.PI * Math.min(1, 0.1 + u * 0.9)), 0.75);
      row.push([V(cx + sx * wd, cy - wd * 0.35, cz + sz * wd), V(cx, cy, cz), V(cx - sx * wd, cy - wd * 0.35, cz - sz * wd)]);
    }
    const nU = V(ca * 0.25, 1, sa * 0.25), nD = V(-ca * 0.25, -1, -sa * 0.25), cB = shade(col, -0.12), cT = shade(col, 0.08);
    for (let i = 0; i < N; i++) {
      const A = row[i], Bq = row[i + 1], c0 = i ? col : cB, c1 = i + 1 === N ? cT : col;
      for (const [l, m] of [[0, 1], [1, 2]]) {
        mb.triN(A[l], Bq[l], Bq[m], nU, nU, nU, c0, c1, c1); mb.triN(A[l], Bq[m], A[m], nU, nU, nU, c0, c1, c0);
        mb.triN(A[l], Bq[m], Bq[l], nD, nD, nD, c0, c1, c1); mb.triN(A[l], A[m], Bq[m], nD, nD, nD, c0, c0, c1);
      }
    }
  }

  // ---- 第三版修订：攻击画面按「防守城池的文化 + 当地群落 + 是否登陆」布景（不再一律是中原的砖城、灰瓦村舍与温带树）
  // 城墙：han 中式砖城门楼 / roman 石城方塔赤陶瓦 / mud 土坯城圆塔阶梯垛（安息、阿拉伯、非洲、毛里、西域）/
  //       brick 红砖城与圆顶亭、窣堵波（天竺、贵霜）/ palisade 土垒木栅与木望楼（日耳曼、凯尔特、草原、南方诸族）
  // 村舍：han 灰瓦 / tile 白墙赤陶瓦 / flat 平顶土坯 / yurt 毡帐 / long 长屋茅顶 / round 圆屋锥顶 / stilt 干栏 / thatch 茅屋
  const WALL_STYLE = { han: 'han', korea: 'han', wa: 'han', roman: 'roman', persia: 'mud', arab: 'mud', africa: 'mud', moor: 'mud', tarim: 'mud', kushan: 'brick', india: 'brick',
    seasia: 'palisade', nanman: 'palisade', yi: 'palisade', celt: 'palisade', german: 'palisade', steppe: 'palisade', sarmatian: 'palisade' };
  const HOUSE_STYLE = { han: 'han', korea: 'han', wa: 'han', roman: 'tile', persia: 'flat', arab: 'flat', africa: 'flat', moor: 'flat', tarim: 'flat', kushan: 'flat', india: 'thatch',
    seasia: 'stilt', nanman: 'stilt', yi: 'stilt', celt: 'round', german: 'long', steppe: 'yurt', sarmatian: 'yurt' };
  const BIOME_OF = { roman: 'med', moor: 'med', persia: 'desert', arab: 'desert', tarim: 'desert', africa: 'savanna', kushan: 'steppe', steppe: 'steppe', sarmatian: 'steppe',
    india: 'tropical', seasia: 'tropical', nanman: 'tropical', yi: 'tropical' };
  const BIOME_NAMES = ['temperate', 'steppe', 'desert', 'tropical', 'savanna', 'med', 'boreal', 'alpine'];   // = SG.BattleTerrain.BIOMES
  // grass：地面底色（null = 原温带色）；trees：[树种, 权重]；treeK：树的多寡；farm：农田；snow：雪线系数（0 无雪）；sandy：沙地斑块
  const BIOME_LOOK = {
    temperate: { grass: null, leaf: C(0.28, 0.5, 0.26), fleaf: C(0.25, 0.44, 0.23), tuft: null, trees: [['pine', 34], ['broad', 28], ['broad2', 22], ['poplar', 16]], treeK: 1, farm: 1, snow: 1, sandy: 0, flowers: 1 },
    boreal: { grass: C(0.4, 0.54, 0.32), leaf: C(0.22, 0.42, 0.24), fleaf: C(0.2, 0.38, 0.22), tuft: C(0.38, 0.54, 0.28), trees: [['pine', 70], ['poplar', 15], ['broad', 15]], treeK: 1.1, farm: 0.5, snow: 1, sandy: 0, flowers: 0.6 },
    alpine: { grass: C(0.46, 0.56, 0.34), leaf: C(0.22, 0.42, 0.24), fleaf: C(0.2, 0.38, 0.22), tuft: C(0.44, 0.56, 0.3), trees: [['pine', 80], ['broad', 20]], treeK: 0.8, farm: 0.3, snow: 1.4, sandy: 0, flowers: 0.8 },
    steppe: { grass: C(0.64, 0.65, 0.4), leaf: C(0.34, 0.5, 0.28), fleaf: C(0.3, 0.46, 0.26), tuft: C(0.6, 0.6, 0.34), trees: [['poplar', 50], ['broad', 35], ['scrub', 15]], treeK: 0.35, farm: 0.2, snow: 1, sandy: 0.15, flowers: 0.5 },
    desert: { grass: C(0.78, 0.68, 0.48), leaf: C(0.34, 0.5, 0.24), fleaf: C(0.3, 0.48, 0.22), tuft: C(0.66, 0.6, 0.36), trees: [['palm', 65], ['olive', 15], ['scrub', 20]], treeK: 0.45, farm: 0.35, snow: 0, sandy: 0.5, flowers: 0.1 },
    savanna: { grass: C(0.7, 0.64, 0.38), leaf: C(0.36, 0.5, 0.24), fleaf: C(0.32, 0.46, 0.22), tuft: C(0.7, 0.64, 0.36), trees: [['acacia', 60], ['palm', 20], ['scrub', 20]], treeK: 0.5, farm: 0.3, snow: 0, sandy: 0.25, flowers: 0.3 },
    med: { grass: C(0.56, 0.6, 0.34), leaf: C(0.32, 0.46, 0.25), fleaf: C(0.28, 0.42, 0.22), tuft: C(0.56, 0.58, 0.32), trees: [['cypress', 30], ['olive', 40], ['broad2', 15], ['umbrella', 15]], treeK: 0.8, farm: 1, snow: 0.6, sandy: 0.08, flowers: 0.8 },
    tropical: { grass: C(0.34, 0.58, 0.26), leaf: C(0.2, 0.47, 0.2), fleaf: C(0.16, 0.4, 0.18), tuft: C(0.3, 0.56, 0.22), trees: [['palm', 40], ['broad', 35], ['broad2', 25]], treeK: 1.1, farm: 0.8, snow: 0, sandy: 0, flowers: 1.4 },
  };
  function cultPick(table, c, dflt) {
    if (table[c]) return table[c];
    const b = SG.cultureBase ? SG.cultureBase(c) : 'han';
    return table[b] || dflt;
  }
  // scn = { culture, biome（名称或 BIOMES 下标）, coast }；缺省时取防守方的文化、按文化推测群落
  function sceneLook(scn, defCult) {
    scn = scn || {};
    const culture = String(scn.culture || defCult || 'han');
    let biome = typeof scn.biome === 'number' ? BIOME_NAMES[scn.biome] : scn.biome;
    if (!BIOME_LOOK[biome]) biome = cultPick(BIOME_OF, culture, 'temperate');
    return Object.assign({ culture, biome, wall: cultPick(WALL_STYLE, culture, 'han'), house: cultPick(HOUSE_STYLE, culture, 'han'), coast: !!scn.coast }, BIOME_LOOK[biome]);
  }
  function pickTree(look, rnd) {
    let tot = 0;
    for (const t of look.trees) tot += t[1];
    let u = rnd.nextDouble() * tot;
    for (const t of look.trees) { u -= t[1]; if (u <= 0) return t[0]; }
    return look.trees[0][0];
  }
  // 海岸线（登陆战）：攻方（左）身后为沙滩与海，海岸线向远处斜向右后方伸去
  const SEA_Y = -0.14;
  function shoreX(z) { return -10.6 + Math.min(60, Math.max(0, ZC - z)) * 0.42 - Math.min(20, Math.max(0, z - ZC)) * 0.2 + Math.sin(z * 0.23) * 0.7; }

  // ---- 房屋（three 坐标经 P 转换；y 为地面高度）
  const WHITE = C(0.88, 0.84, 0.76), MUD = C(0.8, 0.66, 0.48), THATCH = C(0.68, 0.57, 0.36), TERRA = C(0.72, 0.36, 0.24), FELT = C(0.88, 0.84, 0.74), DARKW = C(0.3, 0.2, 0.13);
  // 双坡顶：w 沿屋脊、d 为跨度、h 脊高；rz = true 时屋脊沿 z（山墙朝镜头）；wallCol 给出时补两端山墙三角
  function gableInto(R, Wl, x, y0, z, w, d, h, col, wallCol, rz) {
    const M3 = (u, yy, v) => (rz ? P(x + v, yy, z + u) : P(x + u, yy, z + v));
    const Nn = (nu, ny, nv) => (rz ? V(nv, ny, -nu) : V(nu, ny, -nv));
    const hw = w / 2, hd = d / 2, dark = shade(col, -0.16);
    const r0 = M3(-hw, y0 + h, 0), r1 = M3(hw, y0 + h, 0);
    for (const sv of [1, -1]) {
      const e0 = M3(-hw, y0, sv * hd), e1 = M3(hw, y0, sv * hd), n = Nn(0, hd, sv * h);
      R.triN(r0, e0, e1, n, n, n, col, dark, dark);
      R.triN(r0, e1, r1, n, n, n, col, dark, col);
      // 檐下（深色，略厚）
      const t = Math.min(0.06, h * 0.06), u0 = M3(-hw, y0 - t, sv * hd), u1 = M3(hw, y0 - t, sv * hd), no = Nn(0, 0, sv);
      Wl.triN(e0, u0, u1, no, no, no, shade(col, -0.45)); Wl.triN(e0, u1, e1, no, no, no, shade(col, -0.45));
    }
    if (wallCol) for (const su of [1, -1]) {
      const g = hw - Math.min(0.15, w * 0.06), n = Nn(su, 0, 0);
      Wl.triN(M3(su * g, y0, -hd * 0.86), M3(su * g, y0, hd * 0.86), M3(su * g, y0 + h * 0.9, 0), n, n, n, wallCol);
    }
  }
  // 四坡攒尖（金字塔形）
  function pyramidInto(R, x, y0, z, w, d, h, col) {
    const a = P(x, y0 + h, z), hw = w / 2, hd = d / 2, dark = shade(col, -0.14);
    const c = [P(x - hw, y0, z - hd), P(x + hw, y0, z - hd), P(x + hw, y0, z + hd), P(x - hw, y0, z + hd)];
    const n = [V(0, hd, h), V(h, hw, 0), V(0, hd, -h), V(-h, hw, 0)];
    for (let i = 0; i < 4; i++) R.triN(a, c[i], c[(i + 1) % 4], n[i], n[i], n[i], col, dark, dark);
  }
  function domeInto(Wl, x, y, z, r, col, seg) {
    Wl.lathe(P(x, y, z), [[r, 0], [r * 0.98, r * 0.3], [r * 0.8, r * 0.7], [r * 0.45, r * 0.95], [0.04 * r, r * 1.06], [0, r * 1.08]], seg || 14, col);
  }
  function stupaInto(B, x, y, z, s) {
    const W = B[K_PLAIN], c = C(0.9, 0.87, 0.8), gold = C(0.85, 0.68, 0.3);
    W.lathe(P(x, y, z), [[1.5 * s, 0], [1.5 * s, 0.35 * s, null, true], [1.3 * s, 0.4 * s], [1.3 * s, 0.55 * s]], 18, shade(c, -0.08), { capTop: true });
    W.lathe(P(x, y + 0.55 * s, z), [[1.2 * s, 0], [1.18 * s, 0.35 * s], [1.0 * s, 0.8 * s], [0.6 * s, 1.1 * s], [0, 1.22 * s]], 18, c);
    W.box(P(x, y + 1.86 * s, z), V(0.36 * s, 0.24 * s, 0.36 * s), shade(gold, -0.15));
    W.tube([P(x, y + 1.9 * s, z), P(x, y + 2.62 * s, z)], [0.03 * s, 0.022 * s], 6, gold, false);
    for (const [h, r] of [[2.15, 0.3], [2.33, 0.22], [2.48, 0.14]]) W.lathe(P(x, y + h * s, z), [[r * s, 0], [r * s * 0.9, 0.04 * s], [0, 0.05 * s]], 12, gold);
  }
  function houseInto(B, style, x, y, z, rnd, s) {
    s = s || 1;
    const Wl = B[K_PLAIN], R = B[K_ROOF], T = B[K_ROCK];
    const ry = rnd.nextDouble() < 0.5, w = (1.6 + rnd.nextDouble() * 0.6) * s, dd = 1.1 * s;
    switch (style) {
      case 'tile': {
        const hh = 1.05 * s;
        Wl.box(P(x, y + hh / 2, z), V(dd + 0.3 * s, hh, w), WHITE);
        Wl.box(P(x, y + 0.36 * s, z + w / 2 + 0.01), V(0.32 * s, 0.72 * s, 0.04), DARKW);
        gableInto(R, Wl, x, y + hh, z, w + 0.3 * s, dd + 0.65 * s, 0.5 * s, TERRA, WHITE, true);
        break;
      }
      case 'flat': {
        const hh = (1.1 + rnd.nextDouble() * 0.3) * s, c = shade(MUD, (rnd.nextDouble() - 0.5) * 0.1), ph = 0.16 * s, pt = 0.08 * s;
        const box = (bx, by, bw, bh, bd) => {
          Wl.box(P(bx, by + bh / 2, z), V(bw, bh, bd), c);
          Wl.box(P(bx, by + bh + ph / 2, z + bd / 2 - pt / 2), V(bw, ph, pt), shade(c, 0.05));
          Wl.box(P(bx, by + bh + ph / 2, z - bd / 2 + pt / 2), V(bw, ph, pt), shade(c, 0.05));
          Wl.box(P(bx - bw / 2 + pt / 2, by + bh + ph / 2, z), V(pt, ph, bd), shade(c, 0.05));
          Wl.box(P(bx + bw / 2 - pt / 2, by + bh + ph / 2, z), V(pt, ph, bd), shade(c, 0.05));
        };
        box(x, y, w, hh, dd + 0.2 * s);
        if (rnd.nextDouble() < 0.5) box(x - w * 0.22, y + hh, w * 0.45, 0.62 * s, dd * 0.7);
        else if (rnd.nextDouble() < 0.45) domeInto(Wl, x + w * 0.15, y + hh, z, 0.42 * s, C(0.9, 0.88, 0.82));
        Wl.box(P(x + w * 0.2, y + 0.33 * s, z + dd / 2 + 0.11 * s), V(0.3 * s, 0.66 * s, 0.03), DARKW);
        Wl.box(P(x - w * 0.22, y + 0.7 * s, z + dd / 2 + 0.11 * s), V(0.18 * s, 0.2 * s, 0.03), C(0.18, 0.14, 0.12));
        break;
      }
      case 'yurt': {
        const r = 0.78 * s;
        Wl.lathe(P(x, y, z), [[r, 0], [r, 0.5 * s], [r * 0.95, 0.6 * s], [r * 0.45, 0.95 * s], [0.1 * s, 1.06 * s], [0, 1.08 * s]], 16, FELT);
        Wl.lathe(P(x, y + 0.4 * s, z), [[r + 0.012, 0], [r + 0.012, 0.07 * s]], 16, C(0.62, 0.28, 0.18));
        Wl.lathe(P(x, y + 0.7 * s, z), [[r * 0.82, 0], [r * 0.7, 0.06 * s]], 16, C(0.62, 0.28, 0.18));
        Wl.box(P(x, y + 0.28 * s, z + r - 0.03), V(0.34 * s, 0.56 * s, 0.08), C(0.72, 0.32, 0.14));
        break;
      }
      case 'long': {
        const L = 3.0 * s, D = 1.4 * s, hw = 0.6 * s;
        Wl.box(P(x, y + hw / 2, z), V(L, hw, D), C(0.46, 0.34, 0.22));
        Wl.box(P(x + L * 0.2, y + 0.3 * s, z + D / 2 + 0.01), V(0.34 * s, 0.6 * s, 0.04), DARKW);
        gableInto(T, Wl, x, y + hw - 0.05, z, L + 0.3 * s, D + 0.8 * s, 1.25 * s, THATCH, C(0.5, 0.38, 0.25), false);
        break;
      }
      case 'round': {
        const r = 0.85 * s;
        Wl.lathe(P(x, y, z), [[r, 0], [r, 0.72 * s]], 16, C(0.64, 0.54, 0.4));
        Wl.box(P(x, y + 0.3 * s, z + r), V(0.32 * s, 0.6 * s, 0.06), DARKW);
        T.lathe(P(x, y + 0.6 * s, z), [[r + 0.24 * s, 0], [r * 0.6, 0.55 * s], [0.06 * s, 1.18 * s], [0, 1.22 * s]], 16, THATCH);
        break;
      }
      case 'stilt': {
        const L = 2.0 * s, D = 1.3 * s, ph = 0.62 * s, wc = C(0.66, 0.56, 0.38);
        for (const sx of [-1, 1]) for (const sz of [-1, 1]) Wl.tube([P(x + sx * L * 0.42, y, z + sz * D * 0.38), P(x + sx * L * 0.42, y + ph, z + sz * D * 0.38)], [0.05 * s, 0.045 * s], 6, C(0.4, 0.3, 0.2), false);
        Wl.box(P(x, y + ph, z), V(L + 0.2 * s, 0.1 * s, D + 0.3 * s), C(0.46, 0.36, 0.24));
        Wl.box(P(x, y + ph + 0.36 * s, z), V(L, 0.7 * s, D), wc);
        gableInto(T, Wl, x, y + ph + 0.68 * s, z, L + 0.5 * s, D + 0.8 * s, 1.0 * s, THATCH, wc, false);
        break;
      }
      case 'thatch': {
        const hh = 0.9 * s, c = C(0.74, 0.58, 0.42);
        Wl.box(P(x, y + hh / 2, z), V(w * 0.8, hh, dd), c);
        Wl.box(P(x, y + 0.32 * s, z + dd / 2 + 0.01), V(0.3 * s, 0.64 * s, 0.04), DARKW);
        pyramidInto(T, x, y + hh - 0.04, z, w * 0.8 + 0.5 * s, dd + 0.5 * s, 0.85 * s, THATCH);
        break;
      }
      default: {
        Wl.box(P(x, y + 0.5 * s, z), ry ? V(dd, 1.1 * s, w) : V(w, 1.1 * s, dd), C(0.86, 0.8, 0.68));
        R.chineseRoof(P(x, y + 1.05 * s, z), (ry ? dd : w) + 0.5 * s, (ry ? w : dd) + 0.45 * s, 0.62 * s, C(0.3, 0.32, 0.36));
      }
    }
  }
  // 海滩上的小船（登陆）：U 形船壳 + 桅杆
  function boatInto(B, x, y, z, s, ang) {
    const W = B[K_PLAIN], wood = C(0.42, 0.3, 0.2), ca = Math.cos(ang), sa = Math.sin(ang);
    const at = (u, v, h) => P(x + ca * u - sa * v, y + h, z + sa * u + ca * v);
    const NU = 8, NV = 5, L = 1.5 * s, rows = [];
    for (let i = 0; i <= NU; i++) {
      const u = -1 + 2 * i / NU, wd = 0.44 * s * Math.pow(Math.max(0, 1 - u * u), 0.55), dp = 0.36 * s * (1 - 0.4 * u * u), sh = 0.1 * s * u * u, r = [];
      for (let j = 0; j <= NV; j++) {
        const th = Math.PI * j / NV, cx = Math.cos(th), cy = -Math.sin(th);
        r.push({ p: at(u * L, cx * wd, sh + cy * dp + 0.26 * s), n: V(-sa * cx, cy, -ca * cx) });
      }
      rows.push(r);
    }
    const up = V(0, 1, 0), deck = C(0.5, 0.38, 0.26);
    for (let i = 0; i < NU; i++) {
      for (let j = 0; j < NV; j++) {
        const a = rows[i][j], b = rows[i + 1][j], c = rows[i + 1][j + 1], d = rows[i][j + 1], k = shade(wood, j === 0 || j === NV - 1 ? 0.08 : -0.05);
        W.triN(a.p, b.p, c.p, a.n, b.n, c.n, k);
        W.triN(a.p, c.p, d.p, a.n, c.n, d.n, k);
      }
      // 甲板（盖住船壳内侧）
      const a = rows[i][0], b = rows[i + 1][0], c = rows[i + 1][NV], d = rows[i][NV];
      W.triN(a.p, b.p, c.p, up, up, up, deck); W.triN(a.p, c.p, d.p, up, up, up, deck);
    }
    W.tube([at(0.2 * L, 0, 0.1 * s), at(0.2 * L, 0, 1.9 * s)], [0.035 * s, 0.025 * s], 6, wood, false);
    W.tube([at(0.2 * L - 0.6 * s, 0, 1.25 * s), at(0.2 * L + 0.6 * s, 0, 1.25 * s)], [0.02 * s, 0.02 * s], 5, wood, false);
    W.tube([at(0.2 * L - 0.5 * s, 0, 1.2 * s), at(0.2 * L + 0.5 * s, 0, 1.2 * s)], [0.07 * s, 0.06 * s], 6, C(0.86, 0.82, 0.7), true);   // 收起的帆
  }
  // 草丛（6 片弯叶，根暗梢亮，法线朝上，与地面同调）/ 野花（茎 + 五瓣花，花瓣乘实例颜色）
  function tuftGeo() {
    const mb = new SG.MeshBuilder();
    const rnd = SG.SeededRandom(91);
    const dark = C(0.55, 0.55, 0.55), lite = C(1.05, 1.05, 1.0);
    for (let k = 0; k < 7; k++) {
      const a = k * 0.9 + rnd.nextDouble() * 0.6, ca = Math.cos(a), sa = Math.sin(a);
      const r0 = 0.02 + rnd.nextDouble() * 0.05, h = 0.14 + rnd.nextDouble() * 0.12, lean = 0.05 + rnd.nextDouble() * 0.08, w = 0.022;
      const bx = ca * r0, bz = sa * r0;
      const b0 = V(bx - sa * w, 0, bz + ca * w), b1 = V(bx + sa * w, 0, bz - ca * w);
      const m0 = V(bx + ca * lean * 0.4 - sa * w * 0.6, h * 0.55, bz + sa * lean * 0.4 + ca * w * 0.6), m1 = V(bx + ca * lean * 0.4 + sa * w * 0.6, h * 0.55, bz + sa * lean * 0.4 - ca * w * 0.6);
      const tip = V(bx + ca * lean, h, bz + sa * lean);
      const mid = C(0.8, 0.8, 0.78);
      for (const sd of [1, -1]) {
        const nf = V(ca * 0.35 * sd, 1, sa * 0.35 * sd).normalize();
        mb.triN(b0, b1, m1, nf, nf, nf, dark, dark, mid); mb.triN(b0, m1, m0, nf, nf, nf, dark, mid, mid);
        mb.triN(m0, m1, tip, nf, nf, nf, mid, mid, lite);
      }
    }
    const g = mb.toGeometry();
    g.setAttribute('aTint', new THREE.BufferAttribute(new Float32Array(g.getAttribute('position').count).fill(1), 1));
    return g;
  }
  function flowerGeo() {
    const stem = new SG.MeshBuilder(), pet = new SG.MeshBuilder(), up = V(0, 1, 0);
    const green = C(0.32, 0.52, 0.22);
    for (const [x, z, h] of [[0, 0, 0.26], [0.1, 0.06, 0.2], [-0.07, 0.08, 0.23]]) {
      stem.triN(V(x - 0.012, 0, z), V(x + 0.012, 0, z), V(x, h, z), V(0, 0.3, 1), V(0, 0.3, 1), up, green);
      stem.triN(V(x + 0.012, 0, z), V(x - 0.012, 0, z), V(x, h, z), V(0, 0.3, -1), V(0, 0.3, -1), up, green);
      const r = 0.05;
      for (let k = 0; k < 5; k++) {
        const a0 = k * Math.PI * 2 / 5, a1 = a0 + Math.PI * 2 / 5;
        pet.triN(V(x, h, z), V(x + Math.cos(a0) * r, h + 0.01, z + Math.sin(a0) * r), V(x + Math.cos(a1) * r, h + 0.01, z + Math.sin(a1) * r), up, up, up, C(1, 1, 1));
      }
      pet.triN(V(x, h + 0.016, z), V(x + 0.016, h + 0.013, z), V(x, h + 0.013, z + 0.016), up, up, up, C(1.0, 0.86, 0.35));
    }
    const a = stem.toGeometry(), b = pet.toGeometry();
    const na = a.getAttribute('position').count, nb = b.getAttribute('position').count;
    const g = new THREE.BufferGeometry();
    for (const name of ['position', 'normal', 'color']) {
      const arr = new Float32Array((na + nb) * 3);
      arr.set(a.getAttribute(name).array, 0); arr.set(b.getAttribute(name).array, na * 3);
      g.setAttribute(name, new THREE.BufferAttribute(arr, 3));
    }
    const t = new Float32Array(na + nb); t.fill(1, na);
    g.setAttribute('aTint', new THREE.BufferAttribute(t, 1));
    a.dispose(); b.dispose();
    g.computeBoundingSphere();
    return g;
  }

  // 生成器：每做完一段 yield 一次，由 ClashScene.buildAsync 分帧执行（单帧 ≤ 约 10ms）
  function* buildTerrain(sc, kind, rnd, defColor) {
    const H = sc.h, look = sc.look || sceneLook(null, 'han'), coast = look.coast;
    const walled = isWalled(kind), hilly = kind === 'hill' || kind === 'mountain';
    // 离海岸线的距离（非登陆战为无穷大）：树、石、房屋不放进海里
    const dry = coast ? (x, z) => x - shoreX(z) : () => 1e9;
    if (SG.Gfx.terrainDetailGen) { try { yield* SG.Gfx.terrainDetailGen(); } catch (e) { /* 旧版 art.js：无细节纹理 */ } }
    SHARED_U.snow.value = look.snow > 0 ? (kind === 'mountain' ? 13 : kind === 'hill' ? 17 : 99) / Math.max(0.5, look.snow) : 99;
    // ---- 地面：近处 0.5 的平滑网格，向外渐疏（最疏 2.6），向后延伸成远山；看不到的两侧不建三角形
    const xs = axis(-150, 150, 0.5, 12, 0.16, 2.6);
    const zs = [];
    for (let z = 64; z > -126; ) { zs.push(z); z -= z > 16 ? 0.5 + (z - 16) * 0.3 : z > -9 ? 0.5 : Math.min(2.6, 0.5 + (-9 - z) * 0.09); }
    const NX = xs.length, NZ = zs.length;
    const pos = new Float32Array(NX * NZ * 3), colA = new Float32Array(NX * NZ * 3), gnd = new Float32Array(NX * NZ * 4);
    const nA = (x, z) => M.perlinNoise(x * 0.17 + 11.3, z * 0.17 + 4.7) - 0.465, nF = (x, z) => M.perlinNoise(x * 0.09 + 3.1, z * 0.13 + 9.7) - 0.465;
    const ga = {};
    for (let j = 0; j < NZ; j++) {
      if (j % 12 === 11) yield;
      const z = zs[j];
      for (let i = 0; i < NX; i++) {
        const x = xs[i], y = H(x, z), k = j * NX + i;
        pos[k * 3] = x; pos[k * 3 + 1] = y; pos[k * 3 + 2] = z;
        groundAttr(kind, x, z, y, nA(x, z), nF(x, z), ga, look);
        colA[k * 3] = ga.c.r; colA[k * 3 + 1] = ga.c.g; colA[k * 3 + 2] = ga.c.b;
        gnd[k * 4] = ga.g0; gnd[k * 4 + 1] = ga.g1; gnd[k * 4 + 2] = ga.g2; gnd[k * 4 + 3] = ga.g3;
      }
    }
    const lim = z => Math.max(15, 0.8 * (28 - z) + 6);
    const idx = [];
    for (let j = 0; j < NZ - 1; j++) {
      const L = lim(zs[j + 1]);
      for (let i = 0; i < NX - 1; i++) {
        if (Math.min(Math.abs(xs[i]), Math.abs(xs[i + 1])) > L) continue;
        const a = j * NX + i, b = a + 1, c = a + NX, d = c + 1;
        // zs 由近到远（z 递减）：a(x0,z0) b(x1,z0) c(x0,z1) d(x1,z1)，朝上的绕向
        if ((i + j) & 1) idx.push(a, b, c, b, d, c); else idx.push(a, b, d, a, d, c);
      }
    }
    yield;
    const gg = new THREE.BufferGeometry();
    gg.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    gg.setAttribute('color', new THREE.BufferAttribute(colA, 3));
    gg.setAttribute('aGnd', new THREE.BufferAttribute(gnd, 4));
    gg.setIndex(NX * NZ > 65535 ? new THREE.Uint32BufferAttribute(idx, 1) : new THREE.Uint16BufferAttribute(idx, 1));
    gg.computeVertexNormals();
    gg.computeBoundingSphere();
    // 第四版：LP 地面（绘制式顶点色 vec4，alpha = 自发光编码 0）
    let gmat = null;
    if (sc.lp) {
      gmat = lpGroundMaterial();
      if (gmat) {
        const nor = gg.getAttribute('normal').array, NV4 = NX * NZ, C4 = new Float32Array(NV4 * 4), pal = lpMeadow(look.biome), crops = LP_CROPS_HEX.map(lhx), g4 = {};
        const snowH = SHARED_U.snow.value;
        for (let k = 0; k < NV4; k++) {
          if (k % 6000 === 5999) yield;
          g4.g0 = gnd[k * 4]; g4.g1 = gnd[k * 4 + 1]; g4.g2 = gnd[k * 4 + 2]; g4.g3 = gnd[k * 4 + 3];
          const c = lpGroundPaint(kind, pos[k * 3], pos[k * 3 + 2], pos[k * 3 + 1], g4, 1 - nor[k * 3 + 1], look, pal, crops, snowH);
          C4[k * 4] = clamp01(c[0]); C4[k * 4 + 1] = clamp01(c[1]); C4[k * 4 + 2] = clamp01(c[2]); C4[k * 4 + 3] = 0;
        }
        gg.setAttribute('color', new THREE.BufferAttribute(C4, 4));
      }
    }
    const ground = new THREE.Mesh(gg, gmat || groundMaterial());
    ground.receiveShadow = true; ground.castShadow = false;
    sc.scene.add(ground);
    sc.own.push(gg);
    sc.groundTris = idx.length / 3;
    yield;
    // ---- 道具：石块、树、村舍、城墙
    const B = propBuilders(), BF = propBuilders();     // BF：远处（阴影相机之外）的树与村舍，不投影
    const stone = C(0.6, 0.59, 0.56);
    // 第四版：景物取 SG.LPProps 的配方（LP.Scatter 实例化）
    const lp = !!sc.lp, LPS = lp ? (sc.lpSet = new LPSet()) : null;
    const rockAt = (x, y, z, r, col, sd) => (lp ? LPS.rock(x, y - r * 0.12, z, r, sd) : rockInto(B, x, y, z, r, col, sd));
    const rocks = hilly ? 26 : kind === 'river' ? 20 : walled ? 6 : 9;
    for (let k = 0; k < rocks; k++) {
      let x = (rnd.nextDouble() * 2 - 1) * 16, z = 6 - rnd.nextDouble() * 22;
      if (kind === 'river') x = RIVER.x + (rnd.nextDouble() < 0.5 ? -1 : 1) * (RIVER.w + 0.4 + rnd.nextDouble() * 1.6);
      if (Math.abs(x) < 6 && Math.abs(z - ZC) < 3.5) continue;
      if (z > 1.8 && Math.abs(x) < 9) continue;          // 镜头前不放大石头
      if (walled && z < WALL_Z + 0.9) continue;
      if (dry(x, z) < 1.2) continue;
      const r = 0.16 + rnd.nextDouble() * (kind === 'mountain' ? 0.6 : 0.36);
      rockAt(x, H(x, z) + r * 0.12, z, r, shade(stone, (rnd.nextDouble() - 0.5) * 0.14), k + 3);
      if (rnd.nextDouble() < 0.5) rockAt(x + r * 1.3, H(x + r * 1.3, z + r * 0.4), z + r * 0.4, r * 0.45, shade(stone, -0.04), k + 31);
    }
    // 修订（LP）：山岳的两翼、两军身后立几处崖石与巨石（不再与平原一样只是起伏的绿坡）
    if (lp && kind === 'mountain') {
      for (const [x, z, k, nm] of [[-9.6, -8.6, 0.9, 'cliff'], [10.2, -8.2, 1.0, 'cliff'], [-11.8, -6.4, 0.5, 'boulder'], [12.2, -6.8, 0.55, 'boulder'], [-5.6, -13.5, 1.2, 'cliff'], [6.8, -14.8, 1.3, 'cliff'], [-14.5, -16.5, 1.6, 'cliff'], [15.5, -17.5, 1.5, 'cliff']]) {
        if (dry(x, z) < 1.5) continue;
        // 第三轮修订：每处是一簇不规则的多面石块（Lanternvale props_wild 的做法），不再用层叠岩片的「崖石」
        // （拉宽了像一摞摞饼或木箱，不拉宽像一座石塔）：一块大巨石（半埋、随机朝向）+ 旁边两三块大石 / 巨石 + 前面几块碎石；
        // 只用 lhash，地形随机数序列不变
        const sk = FIG_S * 1.6 * k, sd = Math.floor(lhash(x, z, 31) * 2), yaw = lhash(x, z, 32) * 6.283;
        if (nm === 'cliff') {
          const bk = sk * (0.9 + 0.25 * lhash(x, z, 33));
          LPS.add('boulder', { seed: sd, near: true }, x, H(x, z) - 0.3 * bk, z, yaw, [bk * 1.2, bk * (0.95 + 0.25 * lhash(x, z, 35)), bk]);
          const nb = 2 + (lhash(x, z, 34) < 0.5 ? 1 : 0);
          for (let i = 0; i < nb; i++) {
            const a = yaw + 1.2 + i * (4.2 / nb) + (lhash(x, z, 40 + i) - 0.5) * 0.8, d = sk * (1.1 + 0.5 * lhash(x, z, 50 + i));
            const bx = x + Math.cos(a) * d * 1.25, bz = z + Math.sin(a) * d, nm2 = lhash(x, z, 60 + i) < 0.55 ? 'boulder' : 'rock_large';
            const bs = FIG_S * (nm2 === 'boulder' ? 0.62 : 0.95) * k * (0.7 + 0.6 * lhash(x, z, 70 + i));
            if (dry(bx, bz) < 1.2) continue;
            LPS.add(nm2, { seed: (sd + i) & 1, near: true }, bx, H(bx, bz) - 0.12 * bs, bz, lhash(x, z, 80 + i) * 6.283, [bs, bs * (0.85 + 0.3 * lhash(x, z, 85 + i)), bs]);
          }
        } else LPS.add(nm, { seed: sd }, x, H(x, z) - 0.25 * k, z, yaw, sk);
        // 碎石：靠镜头一侧散几块
        const ns = 2 + Math.floor(lhash(x, z, 90) * 3);
        for (let i = 0; i < ns; i++) {
          const a = (lhash(x, z, 91 + i) - 0.5) * 2.4 + Math.PI / 2, d = sk * (1.3 + 0.9 * lhash(x, z, 96 + i));
          const rx = x + Math.cos(a) * d, rz = z + Math.sin(a) * d, rs = FIG_S * k * (0.35 + 0.3 * lhash(x, z, 101 + i));
          if (dry(rx, rz) < 1.2) continue;
          LPS.add('rock_small', { seed: i & 1 }, rx, H(rx, rz) - 0.05 * rs, rz, lhash(x, z, 106 + i) * 6.283, rs);
        }
      }
    }
    // 近景（镜头前 z 3–6.5）的战场遗物：折断的长枪、丢下的圆盾、倒地的军旗、碎石——框住画面下缘，不挡交锋处
    if (lp) {
      const LD = sc._lpDebris = new SG.LP.MeshBuilder(((sc.seed | 0) & 0xffff) + 77), LC = sc._lpCloth = new SG.LP.MeshBuilder(((sc.seed | 0) & 0xffff) + 78);
      LD.jitter = 0.05; LC.jitter = 0.03;
      lpDebris(LD, H, rnd, [sc.A && sc.A.color, sc.D && sc.D.color].filter(c => typeof c === 'string'), dry, (x, y, z, r, sd) => LPS.rock(x, y, z, r, sd), LC);
    } else {
      const W = B[K_PLAIN], metal = C(0.62, 0.62, 0.64), cols = [sc.A && sc.A.color, sc.D && sc.D.color].filter(Boolean);
      const lie = (x, z, len, ang, r, col, tip) => {
        const ca = Math.cos(ang) * len / 2, sa = Math.sin(ang) * len / 2;
        const a = P(x - ca, H(x - ca, z - sa) + r, z - sa), b = P(x + ca, H(x + ca, z + sa) + r + 0.02, z + sa);
        W.tube([a, b], [r, r * 0.9], 5, col, false);
        if (tip) W.tube([b, P(x + ca * 1.12, H(x + ca, z + sa) + r + 0.02, z + sa * 1.12)], [r * 1.3, 0.003], 5, metal, false);
      };
      for (let k = 0; k < 14; k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 12, z = 3.2 + rnd.nextDouble() * 3.4;
        if (dry(x, z) < 1) continue;
        const u = rnd.nextDouble();
        if (u < 0.28) { const br = rnd.nextDouble() < 0.5; lie(x, z, (br ? 0.45 : 0.9) + rnd.nextDouble() * 0.35, rnd.nextDouble() * 3.14, 0.014, shade(WOOD, 0.1), !br); }
        else if (u < 0.5) {
          const y = H(x, z), c = cols.length ? SG.Gfx.color(cols[k % cols.length]) : C(0.5, 0.3, 0.2);
          // 斜倚在地上的圆盾（MeshBuilder.M：先绕水平轴倾斜再平移）
          const pu = P(x, y + 0.08, z);
          W.M = new THREE.Matrix4().makeTranslation(pu.x, pu.y, pu.z).multiply(new THREE.Matrix4().makeRotationFromEuler(new THREE.Euler(0.5 + rnd.nextDouble() * 0.5, rnd.nextDouble() * 6.28, 0, 'YXZ')));
          W.lathe(V(0, 0, 0), [[0.21, -0.012, C(0.3, 0.24, 0.18), true], [0.2, 0.02, null, true], [0.06, 0.04], [0.035, 0.06, metal], [0, 0.065, metal]], 14, shade(c, -0.15));
          W.M = null;
        } else if (u < 0.6 && cols.length) {
          // 倒地的小旗：旗杆 + 平摊在地上的旗面
          const ang = rnd.nextDouble() * 3.14, c = SG.Gfx.color(cols[k % cols.length]);
          lie(x, z, 1.3, ang, 0.016, shade(WOOD, -0.05), false);
          const ca = Math.cos(ang), sa = Math.sin(ang), y = H(x, z) + 0.035, nx = -sa, nz = ca;
          const q = (a, b) => P(x + ca * a + nx * b, y + 0.012 * Math.sin(a * 5 + b * 3), z + sa * a + nz * b);
          const up = V(0, 1, 0);
          for (let i = 0; i < 3; i++) {
            const a0 = 0.1 + i * 0.16, a1 = a0 + 0.16;
            W.triN(q(a0, 0), q(a1, 0), q(a1, 0.36), up, up, up, shade(c, i % 2 ? -0.06 : 0.02)); W.triN(q(a0, 0), q(a1, 0.36), q(a0, 0.36), up, up, up, shade(c, i % 2 ? -0.06 : 0.02));
          }
        } else rockInto(B, x, H(x, z) + 0.02, z, 0.1 + rnd.nextDouble() * 0.14, shade(stone, (rnd.nextDouble() - 0.5) * 0.12), 70 + k);
      }
    }
    yield;
    const leafC = () => shade(kind === 'forest' ? look.fleaf : look.leaf, (rnd.nextDouble() - 0.5) * 0.16);
    const pickT = () => pickTree(look, rnd);
    // 近景框边的树：在本群落的树种里挑「大冠」的一种（温带仍是松 / 阔叶）
    const nearT = () => { const t = pickT(); return t === 'scrub' ? 'broad' : t === 'poplar' ? 'broad' : t; };
    const tree = (x, z, s, type, near) => { if (dry(x, z) < 1.5) return; if (lp) { LPS.tree(x, H(x, z) - 0.05, z, s, type || pickT(), look, Math.abs(Math.round(x * 13 + z * 7)) % 97); return; } treeInto(z < -14 ? BF : B, x, H(x, z) - 0.05, z, s * (type === 'palm' || (!type && look.trees[0][0] === 'palm') ? 1.25 : 1), type || pickT(), leafC(), Math.abs(Math.round(x * 13 + z * 7)) % 97, near); };
    if (kind === 'forest') {
      const nF0 = Math.round(110 * Math.min(1, 0.55 + look.treeK * 0.45));
      for (let k = 0; k < nF0; k++) {
        if (k % 20 === 19) yield;
        const x = (rnd.nextDouble() * 2 - 1) * 26, z = -4.4 - rnd.nextDouble() * 26;
        tree(x, z, 2.2 + rnd.nextDouble() * 1.6, null, false);
      }
      // 两侧与近景的树（框住画面）
      for (const [x, z, s] of [[-9.5, 3.5, 3.4], [10.2, 2.8, 3.1], [-12.5, 0.5, 3.6], [12.8, -1.2, 3.8], [-7.6, -3.6, 2.8], [8.4, -4.0, 3.0], [-10.6, 6.4, 3.2], [11.4, 6.0, 3.3]])
        tree(x, z, s, look.biome === 'temperate' ? (rnd.nextDouble() < 0.4 ? 'pine' : 'broad') : nearT(), true);
      // 林下灌丛
      for (let k = 0; k < 24; k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 14, z = 4 - rnd.nextDouble() * 12;
        if (Math.abs(z - ZC) < 3.4 && Math.abs(x) < 7.5) continue;
        if (dry(x, z) < 1.5) continue;
        if (lp) { LPS.bush(x, H(x, z) - 0.02, z, 0.8 + rnd.nextDouble() * 0.3, look, k % 3 ? 'pilot' : 'round'); continue; }
        B[K_LEAF].sphere(P(x, H(x, z) + 0.05, z), V(0.36, 0.24, 0.32), shade(look.fleaf, 0.03 + (rnd.nextDouble() - 0.5) * 0.15), 2, { seed: k, rough: 0.14, bottomShade: 0.35, flat: -0.4, smoothN: 0.5 });
      }
    } else if (!walled) {
      const n = Math.round((kind === 'river' ? 30 : 24) * look.treeK);
      for (let k = 0; k < n; k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 30, z = -9 - rnd.nextDouble() * 24;
        if (kind === 'river' && Math.abs(x - RIVER.x) < 4) continue;
        if (Math.abs(x) < 9 && z > -16) continue;          // 不在两军交锋处的正后方挡住画面
        tree(x, z, 2 + rnd.nextDouble() * 1.4, null, false);
      }
      for (const [x, z, s] of [[-12.8, -2.5, 2.8], [13.6, -3.4, 3.0]]) tree(x, z, s, look.biome === 'temperate' ? 'broad' : nearT(), true);
      // 田间村舍（平原、河川）：按防守城池的文化（灰瓦、白墙赤陶瓦、平顶土坯、毡帐、长屋、圆屋、干栏、茅屋）
      if (kind === 'plain' || kind === 'river') {
        for (let k = 0; k < 6; k++) {
          const x = -22 + k * 8.5 + (rnd.nextDouble() - 0.5) * 4, z0 = -19 - rnd.nextDouble() * 9;
          if (kind === 'river' && Math.abs(x - RIVER.x) < 6) continue;
          // 修订（LP）：不再是一排同样朝向、同一深度的房子——前后错开 ±4、转 ±0.6 弧度、大小 0.85–1.1，空出一两处，
          // 房边配草垛 / 水井 / 一段篱笆，读作一座小村（按位置哈希，地形的随机数序列不变）
          const z = lp ? z0 + (lhash(x, z0, 22) - 0.5) * 8 : z0;
          if (dry(x, z) < 3) continue;
          if (lp) {
            if (lhash(x, z0, 21) < 0.22) { tree(x + 1.9, z - 0.8, 1.8, look.house === 'flat' ? 'palm' : look.biome === 'temperate' ? 'broad' : nearT(), false); continue; }
            const yaw = (lhash(x, z0, 23) - 0.5) * 1.2, hs = 0.85 + lhash(x, z0, 24) * 0.25;
            LPS.house(look.house, x, H(x, z) - 0.03, z, hs, yaw);
            const c = lhash(x, z0, 25), side = lhash(x, z0, 26) < 0.5 ? -1 : 1, cx = x + side * (2.3 + c * 0.6), cz = z + 1.1 + c * 0.8;
            const dryHouse = look.house === 'flat' || look.house === 'tile';
            if (dry(cx, cz) >= 2) {
              if (c < 0.35 && !dryHouse) LPS.add('haystack', { seed: (c * 10) & 1 }, cx, H(cx, cz) - 0.02, cz, c * 6.28, FIG_S * 0.85);
              else if (c < 0.6) LPS.add('well', { seed: 0 }, cx, H(cx, cz) - 0.02, cz, yaw, FIG_S * 0.85);
              else if (c < 0.9) LPS.add(dryHouse ? 'wall_stone' : 'fence_wood', { seed: 0 }, cx, H(cx, cz) - 0.02, cz, yaw + Math.PI / 2 * side * 0.2, FIG_S * 0.9);
            }
          } else houseInto(BF, look.house, x, H(x, z), z, rnd, 1);
          tree(x + 1.9, z - 0.8, 1.8, look.house === 'flat' ? 'palm' : look.biome === 'temperate' ? 'broad' : nearT(), false);
        }
      }
    } else {
      // 城外两侧的树
      for (const [x, z, s] of [[-12.6, -2.2, 2.9], [-10.4, -5.6, 2.5], [14.4, -3.6, 2.7]]) tree(x, z, s, look.biome === 'temperate' ? 'broad' : nearT(), true);
    }
    // 登陆：海滩上搁浅的小船（攻方身后）
    if (coast) {
      for (const [z, s, a] of [[-4.2, 1.0, 0.5], [-9.5, 1.15, 0.25], [2.8, 0.9, 0.7]]) {
        const x = shoreX(z) - 0.2;
        if (lp) LPS.add('boat_river', { seed: Math.round(z) & 3 }, x, Math.max(SEA_Y - 0.06, H(x, z)) + 0.02, z, -a, 3 * s / 6.38);
        else boatInto(B, x, Math.max(SEA_Y - 0.12, H(x, z)) - 0.06, z, s, a);
      }
    }
    yield;
    // 城墙与城门
    if (walled) {
      if (lp) {
        const dh = typeof defColor === 'string' ? defColor : '#' + SG.Gfx.color(defColor).getHexString();
        const w = buildWallLP(sc, LPS, kind, rnd, dh, H, look);
        if (w.geo) { const wm = SG.LP.mesh(w.geo, { castShadow: true }); wm.name = 'ClashWall'; sc.scene.add(wm); sc.own.push(w.geo); }
        for (const o of w.objs) { sc.scene.add(o); o.traverse(q => { if (q.name === 'LP decal' && q.geometry) { sc.own.push(q.geometry); if (q.material && q.material.uniforms && q.material.uniforms.uMap) sc.own.push(q.material.uniforms.uMap.value); } }); }
        sc.lpLamps = w.lamps;
        if (sc.mood) for (const L of w.lamps) sc.mood.addLamp(L.pos, Object.assign({}, L.o, { pos: undefined, range: (L.o.range || 4.6) * FIG_S * 1.2, haloSize: (L.o.haloSize || 1.4) * FIG_S }));
      } else buildWall(B, kind, rnd, defColor, H, look);
      yield;
    }
    if (lp) {
      LPS.build(sc.scene);
      sc.lpTris = LPS.tris; sc.lpCopies = LPS.n;
      if (sc._lpDebris && !sc._lpDebris.isEmpty) { const dg = sc._lpDebris.toGeometry(), dm = SG.LP.mesh(dg, { castShadow: true }); dm.name = 'ClashDebris'; sc.scene.add(dm); sc.own.push(dg); }
      if (sc._lpCloth && !sc._lpCloth.isEmpty) { const cg = sc._lpCloth.toGeometry(), cm = SG.LP.mesh(cg, { outline: false, castShadow: false }); cm.name = 'ClashDebrisCloth'; sc.scene.add(cm); sc.own.push(cg); }
      sc._lpDebris = null; sc._lpCloth = null;
    }
    for (const [bs, cast] of [[B, true], [BF, false]]) {
      if (!bs.some(mb => mb.pos.length)) continue;
      const pg = mergeProps(bs);
      const props = new THREE.Mesh(pg, propMaterial());
      props.castShadow = cast; props.receiveShadow = true;
      sc.scene.add(props);
      sc.own.push(pg);
    }
    yield;
    // ---- 草丛 / 芦苇 / 野花（实例化，随风轻摆；不投影）
    const tufts = [];
    const nT = walled ? 150 : kind === 'river' ? 380 : kind === 'forest' ? 420 : 480;
    const gcol = look.tuft ? (kind === 'forest' ? shade(look.tuft, -0.08) : look.tuft) : kind === 'forest' ? C(0.3, 0.48, 0.24) : hilly ? C(0.5, 0.6, 0.3) : C(0.4, 0.6, 0.28);
    for (let k = 0; k < nT; k++) {
      const x = (rnd.nextDouble() * 2 - 1) * 15, z = 8 - rnd.nextDouble() * (walled ? 14 : 22);
      if (dry(x, z) < 2.4) continue;
      if (walled && z < WALL_Z + 0.8) continue;
      if (kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w + 0.5) continue;
      if (walled && kind !== 'wall' && Math.abs(x - GATE_X) < 1.6 + Math.max(0, z - WALL_Z) * 0.07) continue;
      const tr = Math.exp(-(x * x) / 12 - ((z - ZC) * (z - ZC)) / 8);
      if (rnd.nextDouble() < tr * 0.75) continue;
      if (z > 3.5 && rnd.nextDouble() < 0.45) continue;    // 镜头前稀一些
      tufts.push([x, H(x, z) - 0.02, z, (0.75 + rnd.nextDouble() * 0.6) * (z > 3.5 ? 0.8 : 1), 1, rnd.nextDouble() * 6.28, shade(gcol, (rnd.nextDouble() - 0.5) * 0.24)]);
    }
    if (kind === 'river') {
      for (let k = 0; k < 110; k++) {
        const sk = rnd.nextDouble() < 0.5 ? -1 : 1;
        const x = RIVER.x + sk * (RIVER.w + 0.7 + rnd.nextDouble() * 1.3), z = 6 - rnd.nextDouble() * 22;
        if (Math.abs(z - ZC) < 3.6 && Math.abs(x) < 5.5) continue;
        if (z > 2.2) continue;
        tufts.push([x, H(x, z) - 0.02, z, 1.0 + rnd.nextDouble() * 0.5, 2.4 + rnd.nextDouble(), rnd.nextDouble() * 6.28, shade(C(0.62, 0.62, 0.34), (rnd.nextDouble() - 0.5) * 0.2)]);
      }
    }
    const inst = (geo, list) => {
      const im = new THREE.InstancedMesh(geo, grassMaterial(), Math.max(1, list.length));
      im.count = list.length;
      const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler(), sv = new THREE.Vector3(), pv = new THREE.Vector3();
      list.forEach((t, i) => {
        q.setFromEuler(e.set(0, t[5], 0));
        m.compose(pv.set(t[0], t[1], t[2]), q, sv.set(t[3], t[3] * t[4], t[3]));
        im.setMatrixAt(i, m);
        im.setColorAt(i, t[6]);
      });
      im.receiveShadow = true; im.castShadow = false;
      im.frustumCulled = false;
      sc.scene.add(im);
      sc.own.push(geo);
      sc.instanced.push(im);
      return im;
    };
    if (tufts.length && !lp) inst(tuftGeo(), tufts);
    let flowerList = [];
    if (!walled) {
      const fl = [C(0.98, 0.95, 0.86), C(1, 0.82, 0.3), C(0.95, 0.55, 0.62), C(0.72, 0.6, 0.95)], list = [];
      for (let k = 0; k < Math.round((kind === 'forest' ? 40 : 70) * look.flowers); k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 14, z = 3 - rnd.nextDouble() * 16;
        if (dry(x, z) < 3) continue;
        if (kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w + RIVER.bank) continue;
        if (Math.abs(x) < 6 && Math.abs(z - ZC) < 3) continue;
        list.push([x, H(x, z) - 0.01, z, 0.8 + rnd.nextDouble() * 0.5, 1, rnd.nextDouble() * 6.28, fl[rnd.next(fl.length)]]);
      }
      if (list.length) { if (lp) flowerList = list; else inst(flowerGeo(), list); }
    }
    // 第四版：草丛 / 芦苇 / 野花合成一个 LP 网格（随风摆，不描墨线）
    if (lp) {
      const FL = LP_FLOWER_HEX.map(lhx);
      const items = tufts.map(t => ({ t: t[4] > 1.5 ? 'reed' : 'tuft', x: t[0], y: t[1] + 0.01, z: t[2], k: t[3] * 1.15, dim: 0.94 + 0.12 * lhash(t[0], t[2], 3) }));
      for (const f of flowerList) items.push({ t: 'flowers', x: f[0], y: f[1], z: f[2], k: f[3] * 1.2, c: FL[Math.floor(lhash(f[0], f[2], 5) * FL.length) % FL.length] });
      for (let k = 0; k < 40; k++) {
        const x = (rnd.nextDouble() * 2 - 1) * 7, z = ZC + (rnd.nextDouble() - 0.5) * 6;
        if (dry(x, z) < 1.5 || (kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w + 0.3)) continue;
        items.push({ t: 'pebble', x, y: H(x, z), z, k: 1 });
      }
      const gm = lpGrassMesh(items, (sc.seed ^ 0x2545f491) | 0, look.biome);
      if (gm) { sc.scene.add(gm); sc.own.push(gm.geometry); sc.grassTris = gm.geometry.userData.tris || 0; }
    }
    yield;
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
          const sa = clamp01(Math.abs(xa - RIVER.x) / (RIVER.w + RIVER.bank));
          const ka = C(0.25 + sa * 0.6, 0, 0);
          const y = RIVER.surf;
          const a = P(xa, y, zz[j]), b = P(xa, y, zz[j + 1]), c = P(xb, y, zz[j + 1]), d = P(xb, y, zz[j]);
          wm.tri(a, b, c, ka); wm.tri(a, c, d, ka);
        }
      const water = SG.Gfx.mesh(wm.toGeometry(), lp ? waterMaterialLP() : waterMaterial(), { castShadow: false, receiveShadow: false });
      water.renderOrder = 1;
      sc.scene.add(water);
      sc.own.push(water.geometry);
    }
    // 海（登陆战）：海岸线以外的水面，岸边浅（顶点色 r → 浪花），远海不做顶点起伏（g = 1）
    if (coast) {
      const wm = new SG.MeshBuilder();
      const zz = [];
      for (let z = 64; z > -126; ) { zz.push(z); z -= z > 16 ? 1.2 + (z - 16) * 0.3 : z > -10 ? 0.9 : 0.9 + (-10 - z) * 0.2; }
      const D = [-160, -70, -34, -18, -10, -6, -3.6, -2.2, -1.2, -0.5, 0];
      const at = (z, d) => P(shoreX(z) + 0.9 + d, SEA_Y, z);
      const col = d => C(Math.pow(clamp01(1 + d / 7), 1.6), d < -30 ? 1 : 0, 0);
      for (let j = 0; j < zz.length - 1; j++)
        for (let i = 0; i < D.length - 1; i++) {
          const a = at(zz[j], D[i]), b = at(zz[j + 1], D[i]), c = at(zz[j + 1], D[i + 1]), d = at(zz[j], D[i + 1]);
          wm.triN(a, b, c, V(0, 1, 0), V(0, 1, 0), V(0, 1, 0), col(D[i]), col(D[i]), col(D[i + 1]));
          wm.triN(a, c, d, V(0, 1, 0), V(0, 1, 0), V(0, 1, 0), col(D[i]), col(D[i + 1]), col(D[i + 1]));
        }
      const sea = SG.Gfx.mesh(wm.toGeometry(), lp ? waterMaterialLP() : waterMaterial(), { castShadow: false, receiveShadow: false });
      sea.renderOrder = 1;
      sc.scene.add(sea);
      sc.own.push(sea.geometry);
    }
  }

  // 城墙：按防守城池的文化（look.wall）——
  //   han：砖石墙身（错缝砖纹）、圆润的垛口、角楼与曲面屋顶；城门为半圆拱门（拱券石、包铁门扇与门钉）与城门楼
  //   roman：浅色方石墙、方垛口、方塔赤陶瓦四坡顶，城门两侧方塔（拱窗）；城内白墙赤陶瓦房
  //   mud：土坯墙、阶梯形垛口、圆塔，城门两侧圆塔；城内平顶土坯房与圆顶
  //   brick：红砖墙、圆头垛口、方塔顶上圆顶亭；城内茅屋与窣堵波
  //   palisade：土垒上的削尖木栅、木望楼（茅草攒尖顶）、木门；城内长屋 / 圆屋 / 毡帐 / 干栏
  const WALL_STONE = { han: C(0.7, 0.67, 0.6), roman: C(0.8, 0.76, 0.67), mud: C(0.8, 0.65, 0.46), brick: C(0.7, 0.42, 0.32) };
  function buildWall(B, kind, rnd, defColor, H, look) {
    const st = (look && look.wall) || 'han';
    if (st === 'palisade') { buildPalisade(B, kind, rnd, defColor, H, look); return; }
    const Z = WALL_Z, TH = 1.4, WH = 3.3, X0 = -0.6, X1 = 44;
    const stone = WALL_STONE[st] || WALL_STONE.han;
    const zc = Z - TH / 2;
    const gx = kind === 'wall' ? null : GATE_X;
    const S = B[K_BRICK], W = B[K_PLAIN], R = B[K_ROOF];
    const castle = kind === 'castle';
    // 墙基（略外扩）与墙身；城门两侧分段，门洞上方另砌
    const seg = (a, b) => {
      if (b - a < 0.05) return;
      S.box(P((a + b) / 2, 0.2, zc + 0.06), V(b - a, 0.5, TH + 0.3), shade(stone, -0.1));
      S.box(P((a + b) / 2, WH / 2, zc), V(b - a, WH, TH), stone);
    };
    if (gx === null) seg(X0, X1); else { seg(X0, gx - 1.8); seg(gx + 1.8, X1); }
    // 垛口与女墙
    const merlon = (x, y, z, k) => {
      const mc = shade(stone, k);
      if (st === 'roman') S.box(P(x, y + 0.26, z), V(0.46, 0.5, 0.3), mc);
      else if (st === 'mud') { S.box(P(x, y + 0.14, z), V(0.5, 0.26, 0.3), mc); S.box(P(x, y + 0.36, z), V(0.28, 0.2, 0.3), mc); S.lathe(P(x, y + 0.46, z), [[0.13, 0], [0.08, 0.06], [0, 0.09]], 8, mc); }
      else if (st === 'brick') { S.box(P(x, y + 0.22, z), V(0.42, 0.4, 0.3), mc); S.lathe(P(x, y + 0.42, z), [[0.21, 0], [0.16, 0.1], [0, 0.17]], 8, shade(mc, 0.03)); }
      else { S.box(P(x, y + 0.21, z), V(0.46, 0.34, 0.3), mc); S.lathe(P(x, y + 0.38, z), [[0.2, 0], [0.12, 0.08], [0, 0.11]], 8, shade(mc, 0.02)); }
    };
    S.box(P((X0 + X1) / 2, WH + 0.04, Z - 0.12), V(X1 - X0, 0.08, 0.3), shade(stone, -0.06));
    for (let x = X0 + 0.3; x < X1; x += st === 'roman' ? 0.85 : 0.8) {
      if (gx !== null && Math.abs(x - gx) < (st === 'han' ? 1.9 : 3.0)) continue;
      merlon(x, WH + 0.04, Z - 0.15, 0.04 + (rnd.nextDouble() - 0.5) * 0.06);
    }
    // 角楼（左端）
    const towerSq = (x, z, w, h) => {
      S.box(P(x, h / 2, z), V(w, h, w), shade(stone, -0.05));
      for (let i = 0; i < 3; i++) for (const sz of [-1, 1]) merlon(x - w * 0.36 + i * w * 0.36, h, z + sz * (w / 2 - 0.15), 0.06);
      for (const sx of [-1, 1]) merlon(x + sx * (w / 2 - 0.15), h, z, 0.06);
    };
    const towerRound = (x, z, r, h) => {
      S.lathe(P(x, 0, z), [[r * 1.1, 0], [r, h - 0.3, null, true], [r * 1.1, h - 0.22, null, true], [r * 1.1, h]], 18, shade(stone, -0.04), { capTop: true });
      for (let k = 0; k < 9; k++) { const a = k / 9 * Math.PI * 2; merlon(x + Math.cos(a) * r * 0.9, h, z + Math.sin(a) * r * 0.9, 0.05); }
    };
    const chhatri = (x, y, z, r) => {
      const c = C(0.86, 0.8, 0.68);
      W.box(P(x, y + 0.06, z), V(r * 2.2, 0.12, r * 2.2), shade(stone, 0.05));
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) W.lathe(P(x + sx * r * 0.8, y + 0.12, z + sz * r * 0.8), [[0.05, 0], [0.05, 0.55]], 6, c);
      W.box(P(x, y + 0.7, z), V(r * 2.3, 0.1, r * 2.3), shade(c, -0.05));
      domeInto(W, x, y + 0.75, z, r * 0.95, c, 12);
      W.tube([P(x, y + 0.75 + r * 1.0, z), P(x, y + 0.95 + r * 1.2, z)], [0.03, 0.01], 5, GOLD, false);
    };
    if (st === 'roman') { towerSq(X0, zc, 2.1, 4.9); pyramidInto(R, X0, 5.3, zc, 2.0, 2.0, 0.85, TERRA); W.box(P(X0, 3.7, zc + 1.06), V(0.34, 0.6, 0.04), C(0.16, 0.13, 0.11)); }
    else if (st === 'mud') towerRound(X0, zc, 1.15, 4.7);
    else if (st === 'brick') { towerSq(X0, zc, 2.0, 4.6); chhatri(X0, 4.6, zc, 0.7); }
    else {
      S.box(P(X0, 2.05, zc), V(1.9, 4.1, 1.9), shade(stone, -0.05));
      W.box(P(X0, 4.45, zc), V(1.5, 0.7, 1.5), C(0.62, 0.2, 0.16));
      for (const dx of [-0.62, 0.62]) for (const dz of [-0.62, 0.62]) W.lathe(P(X0 + dx, 4.1, zc + dz), [[0.07, 0], [0.07, 0.72]], 8, C(0.5, 0.16, 0.12));
      R.chineseRoof(P(X0, 4.8, zc), 2.5, 2.5, 0.9, C(0.24, 0.28, 0.36));
    }
    // 城内屋顶（下半截在墙后看不见，只露出屋顶）
    if (st === 'han') {
      for (let k = 0; k < 7; k++) {
        const x = 2 + k * 3.4 + rnd.nextDouble() * 1.2, z = Z - 3.5 - rnd.nextDouble() * 5;
        W.box(P(x, 1.8, z), V(2.2, 3.6, 1.6), C(0.86, 0.8, 0.68));
        R.chineseRoof(P(x, 3.6, z), 3.0, 2.2, 1.0 + rnd.nextDouble() * 0.4, C(0.27, 0.3, 0.37));
      }
    } else {
      const hs = (look && look.house) || 'flat';
      for (let k = 0; k < 7; k++) {
        const x = 2 + k * 3.4 + rnd.nextDouble() * 1.2, z = Z - 3.5 - rnd.nextDouble() * 5;
        if (k === 3) {
          // 城中的地标：罗马神庙山花 / 安息与阿拉伯的大圆顶 / 天竺的窣堵波
          if (st === 'roman') {
            const y0 = 2.2;
            W.box(P(x, y0 + 0.9, z), V(3.4, 1.8, 2.4), WHITE);
            for (let i = 0; i < 6; i++) W.lathe(P(x - 1.5 + i * 0.6, y0, z + 1.3), [[0.12, 0], [0.1, 1.8]], 8, shade(WHITE, 0.04));
            gableInto(R, W, x, y0 + 1.8, z + 0.2, 3.8, 3.2, 0.7, TERRA, WHITE, true);
          } else if (st === 'mud') {
            W.lathe(P(x, 2.4, z), [[1.2, 0], [1.2, 1.2]], 16, shade(MUD, 0.05));
            domeInto(W, x, 3.6, z, 1.2, look && look.culture === 'persia' ? C(0.36, 0.62, 0.66) : C(0.9, 0.86, 0.78), 18);
          } else stupaInto(B, x, 1.6, z, 1.2);
          continue;
        }
        houseInto(B, hs, x, hs === 'yurt' || hs === 'round' ? 1.9 : 2.1, z, rnd, 1.45);
      }
    }
    // 墙根碎石
    for (let k = 0; k < 9; k++) {
      const x = X0 + 1.2 + rnd.nextDouble() * 16;
      if (gx !== null && Math.abs(x - gx) < 2.4) continue;
      const r = 0.12 + rnd.nextDouble() * 0.16;
      rockInto(B, x, H(x, Z + 0.35) + r * 0.1, Z + 0.3 + rnd.nextDouble() * 0.4, r, shade(stone, -0.12 + (rnd.nextDouble() - 0.5) * 0.1), k + 11);
    }
    wallDressing(W, gx, rnd, defColor, H, WH, st === 'han' ? 1.9 : 3.0);
    if (gx === null) return;
    // 城门台：门洞两侧的墩台、拱顶以上的墙体
    const ar = 0.82, ah = 1.75, fz = Z + 0.38, bz = zc + 0.12, depth = TH + 0.5;
    S.box(P(gx - 1.31, 1.85, bz), V(0.98, 3.7, depth), shade(stone, -0.04));
    S.box(P(gx + 1.31, 1.85, bz), V(0.98, 3.7, depth), shade(stone, -0.04));
    S.box(P(gx, (ah + ar + 3.7) / 2, bz), V(1.64, 3.7 - ah - ar, depth), shade(stone, -0.04));
    // 拱腹（半圆拱，平滑法线朝内，越深越暗）与两侧门洞壁
    const N = 14, dark = C(0.16, 0.13, 0.11), ins = shade(stone, -0.35), front = V(0, 0, -1), ri = ar - 0.01;   // front：Unity −z = three +z（朝镜头）
    for (let i = 0; i < N; i++) {
      const a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N;
      const n0 = V(-Math.cos(a0), -Math.sin(a0), 0), n1 = V(-Math.cos(a1), -Math.sin(a1), 0);
      const A0 = P(gx + Math.cos(a0) * ri, ah + Math.sin(a0) * ri, fz), A1 = P(gx + Math.cos(a1) * ri, ah + Math.sin(a1) * ri, fz);
      const B0 = P(gx + Math.cos(a0) * ri, ah + Math.sin(a0) * ri, fz - depth + 0.2), B1 = P(gx + Math.cos(a1) * ri, ah + Math.sin(a1) * ri, fz - depth + 0.2);
      W.triN(A0, B0, A1, n0, n0, n1, ins, dark, ins);
      W.triN(A1, B0, B1, n1, n0, n1, ins, dark, dark);
    }
    for (const sx of [-1, 1]) {
      const n = V(-sx, 0, 0);
      W.triN(P(gx + sx * ri, 0, fz), P(gx + sx * ri, ah, fz), P(gx + sx * ri, ah, fz - depth + 0.2), n, n, n, ins, ins, dark);
      W.triN(P(gx + sx * ri, 0, fz), P(gx + sx * ri, ah, fz - depth + 0.2), P(gx + sx * ri, 0, fz - depth + 0.2), n, n, n, ins, dark, dark);
    }
    // 拱肩：拱线与门台正面矩形（宽 2·ar、高至 ah + ar）之间的扇形填充
    for (let i = 0; i < N; i++) {
      const a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N;
      const cx = i < N / 2 ? ar : -ar;
      S.triN(P(gx + Math.cos(a0) * ar, ah + Math.sin(a0) * ar, fz), P(gx + cx, ah + ar, fz), P(gx + Math.cos(a1) * ar, ah + Math.sin(a1) * ar, fz), front, front, front, shade(stone, -0.04));
    }
    // 拱券石（浅色半环，略凸出墙面）
    for (let i = 0; i < N; i++) {
      const a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N, r0 = ar, r1 = ar + 0.22;
      const q = (a, r) => P(gx + Math.cos(a) * r, ah + Math.sin(a) * r, fz + 0.04);
      S.triN(q(a0, r0), q(a0, r1), q(a1, r1), front, front, front, shade(stone, 0.1 - (i % 2) * 0.05));
      S.triN(q(a0, r0), q(a1, r1), q(a1, r0), front, front, front, shade(stone, 0.1 - (i % 2) * 0.05));
    }
    // 门扇（深红木门 + 铜门钉；非中式为原木色包铁），退进门洞
    const dz = fz - 0.35, doorC = st === 'han' ? C(0.42, 0.2, 0.11) : C(0.4, 0.28, 0.17);
    for (const sx of [-1, 1]) {
      W.box(P(gx + sx * 0.4, ah * 0.5, dz), V(0.8, ah, 0.08), doorC);
      for (let r = 0; r < 4; r++)
        for (let c = 0; c < 3; c++)
          W.sphere(P(gx + sx * (0.16 + c * 0.2), 0.3 + r * 0.38, dz + 0.05), 0.028, st === 'han' ? GOLD : C(0.36, 0.34, 0.32), 0, { top: 0.2 });
    }
    // 拱顶以内的门扇上半（半圆）
    for (let i = 0; i < N; i++) {
      const a0 = Math.PI * i / N, a1 = Math.PI * (i + 1) / N;
      W.triN(P(gx, ah, dz + 0.04), P(gx + Math.cos(a0) * ar, ah + Math.sin(a0) * ar, dz + 0.04), P(gx + Math.cos(a1) * ar, ah + Math.sin(a1) * ar, dz + 0.04), front, front, front, shade(doorC, -0.1));
    }
    if (st !== 'han') {
      // 城门两侧的塔与门上的女墙（罗马方塔拱窗赤陶顶 / 土坯圆塔 / 红砖方塔圆顶亭）
      for (let x = gx - 1.6; x <= gx + 1.61; x += st === 'roman' ? 0.8 : 0.8) merlon(x, 3.7, Z - 0.05, 0.05);
      for (const sx of [-1, 1]) {
        const tx = gx + sx * 2.35;
        if (st === 'mud') towerRound(tx, zc + 0.2, 1.0, 5.0);
        else {
          towerSq(tx, zc + 0.15, 1.5, 5.2);
          if (st === 'roman') {
            pyramidInto(R, tx, 5.6, zc + 0.15, 1.45, 1.45, 0.7, TERRA);
            for (const wy of [3.0, 4.3]) {
              W.box(P(tx, wy, zc + 0.92), V(0.3, 0.46, 0.03), C(0.16, 0.13, 0.11));
            }
          } else chhatri(tx, 5.2, zc + 0.15, 0.55);
        }
        if (castle) W.flag(P(tx + sx * 0.2, st === 'mud' ? 5.0 : 5.2, Z - 0.3), 1.55, 0.8, 0.52, defColor);
      }
      return;
    }
    // 城门楼：台基、红柱、檐下额枋与匾额、曲面重檐
    const red = C(0.62, 0.2, 0.16);
    S.box(P(gx, 3.8, zc + 0.12), V(3.3, 0.2, TH + 0.3), shade(stone, 0.04));
    W.box(P(gx, 4.4, zc), V(2.9, 1.0, 1.3), shade(red, -0.08));
    for (let i = -2; i <= 2; i++) W.lathe(P(gx + i * 0.68, 3.9, Z + 0.08), [[0.07, 0], [0.065, 1.0]], 8, red);
    W.box(P(gx, 4.82, Z + 0.06), V(3.0, 0.14, 0.1), C(0.2, 0.36, 0.4));
    W.box(P(gx, 4.6, Z + 0.07), V(1.1, 0.34, 0.04), C(0.12, 0.1, 0.09));
    W.box(P(gx, 4.6, Z + 0.09), V(0.9, 0.24, 0.02), GOLD);
    // 本城：金顶城楼 + 守方大旗
    R.chineseRoof(P(gx, 4.9, zc), 4.0, 2.2, 1.15, castle ? C(0.85, 0.65, 0.2) : C(0.22, 0.26, 0.34));
    if (castle) {
      W.flag(P(gx + 1.55, 3.9, Z), 1.55, 0.8, 0.52, defColor);
      W.flag(P(gx - 1.55, 3.9, Z), 1.55, 0.8, 0.52, defColor);
    }
  }
  // 拒马、倚墙的云梯、城头旗帜（各式城墙共用）；top：墙头高度，gap：城门两侧不插旗的半宽
  function wallDressing(W, gx, rnd, defColor, H, top, gap) {
    const Z = WALL_Z;
    const stake = shade(WOOD, 0.08);
    const pole = (a, b, r0, r1, col) => W.tube([a, b], [r0, r1], 6, col, false);
    const juma = (cx, cz, len) => {
      const y0 = H(cx, cz);
      pole(P(cx - len / 2, y0 + 0.36, cz), P(cx + len / 2, y0 + 0.36, cz), 0.055, 0.055, shade(WOOD, -0.08));
      const n = Math.max(2, Math.round(len / 0.55));
      for (let i = 0; i < n; i++) {
        const x = cx - len / 2 + 0.2 + (len - 0.4) * (i / (n - 1));
        pole(P(x, y0 + 0.02, cz - 0.42), P(x, y0 + 0.78, cz + 0.4), 0.035, 0.006, stake);
        pole(P(x + 0.04, y0 + 0.02, cz + 0.42), P(x + 0.04, y0 + 0.78, cz - 0.4), 0.035, 0.006, stake);
      }
    };
    const jx = gx === null ? [1.4, 6.8] : [gx - 3.3, gx + 3.3, gx + 8.6];
    for (const x of jx) juma(x, Z + 1.75 + (rnd.nextDouble() - 0.5) * 0.3, 1.9);
    {
      const lx = gx === null ? 9.5 : gx + 6.2, zb = Z + 1.25, zt = Z + 0.12, tp = top - 0.2;
      for (const s of [-0.24, 0.24]) pole(P(lx + s, H(lx, zb) + 0.02, zb), P(lx + s, tp, zt), 0.04, 0.035, WOOD);
      for (let i = 1; i < 8; i++) {
        const u = i / 8;
        pole(P(lx - 0.24, lerp(H(lx, zb), tp, u), lerp(zb, zt, u)), P(lx + 0.24, lerp(H(lx, zb), tp, u), lerp(zb, zt, u)), 0.025, 0.025, shade(WOOD, 0.05));
      }
    }
    for (let k = 0; k < 6; k++) {
      const x = 1.6 + k * 2.6;
      if (gx !== null && Math.abs(x - gx) < gap) continue;
      W.flag(P(x, top + 0.1, Z - 0.45), 1.3, 0.55, 0.4, defColor);
    }
  }
  // 木栅寨墙（日耳曼、凯尔特、草原、南方诸族）：土垒、削尖的原木、木望楼与木门
  function buildPalisade(B, kind, rnd, defColor, H, look) {
    const Z = WALL_Z, X0 = -0.6, X1 = 44, zc = Z - 0.45;
    const gx = kind === 'wall' ? null : GATE_X;
    const W = B[K_PLAIN], T = B[K_ROCK];
    const earth = C(0.5, 0.42, 0.3), steppeHide = look && (look.culture === 'steppe' || look.culture === 'sarmatian');
    const roofC = steppeHide ? C(0.62, 0.52, 0.4) : THATCH;
    // 土垒
    const bank = (a, b) => { if (b - a > 0.1) T.box(P((a + b) / 2, 0.12, zc + 0.15), V(b - a, 0.5, 1.9), earth); };
    if (gx === null) bank(X0, X1); else { bank(X0, gx - 1.25); bank(gx + 1.25, X1); }
    // 原木（顶端削尖），每根高低、粗细、色泽略不同
    let top = 0;
    for (let x = X0 + 0.12; x < X1; x += 0.25) {
      if (gx !== null && Math.abs(x - gx) < 1.25) continue;
      const h = 2.45 + rnd.nextDouble() * 0.35, r = 0.11 + rnd.nextDouble() * 0.025, c = shade(WOOD, 0.1 + (rnd.nextDouble() - 0.5) * 0.14);
      W.tube([P(x, 0.2, zc), P(x, h, zc)], [r, r * 0.92], 6, c, false);
      W.lathe(P(x, h, zc), [[r * 0.92, 0], [0, 0.28]], 6, shade(c, 0.08));
      top = Math.max(top, h);
    }
    // 横向绑木（正面两道）
    const beam = (a, b, y) => { if (b - a > 0.1) W.tube([P(a, y, Z + 0.06), P(b, y, Z + 0.06)], [0.05, 0.05], 5, shade(WOOD, -0.12), false); };
    for (const y of [0.9, 1.9]) { if (gx === null) beam(X0, X1, y); else { beam(X0, gx - 1.25, y); beam(gx + 1.25, X1, y); } }
    // 望楼：四根立柱、平台、栏杆、攒尖顶（草原为毡顶）
    const post = (x, z, h) => W.tube([P(x, 0, z), P(x, h, z)], [0.08, 0.07], 6, shade(WOOD, -0.05), false);
    const tower = (x, z, w, h) => {
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) post(x + sx * w * 0.42, z + sz * w * 0.42, h + 0.9);
      W.box(P(x, h, z), V(w + 0.2, 0.14, w + 0.2), shade(WOOD, -0.1));
      for (const sz of [-1, 1]) W.box(P(x, h + 0.42, z + sz * (w / 2 + 0.08)), V(w + 0.2, 0.5, 0.06), shade(WOOD, 0.05));
      for (const sx of [-1, 1]) W.box(P(x + sx * (w / 2 + 0.08), h + 0.42, z), V(0.06, 0.5, w + 0.2), shade(WOOD, 0.05));
      pyramidInto(T, x, h + 0.9, z, w + 0.6, w + 0.6, 0.95, roofC);
    };
    tower(X0 + 0.4, zc, 1.5, 3.4);
    // 寨内房屋（只露出屋顶）
    const hs = (look && look.house) || 'long';
    for (let k = 0; k < 7; k++) {
      const x = 2 + k * 3.4 + rnd.nextDouble() * 1.2, z = Z - 3.6 - rnd.nextDouble() * 5;
      const s = hs === 'long' ? 1.3 : hs === 'yurt' ? 1.6 : 1.45;
      houseInto(B, k === 3 && hs !== 'yurt' ? (hs === 'stilt' ? 'stilt' : 'long') : hs, x, 1.25, z, rnd, k === 3 ? s * 1.25 : s);
    }
    wallDressing(W, gx, rnd, defColor, H, top, 2.0);
    if (gx === null) return;
    // 寨门：两座望楼、门上走道、原木门扇
    for (const sx of [-1, 1]) tower(gx + sx * 1.75, zc, 1.3, 3.6);
    W.box(P(gx, 2.95, zc), V(3.6, 0.22, 1.0), shade(WOOD, -0.12));
    W.box(P(gx, 3.3, Z - 0.02), V(3.6, 0.5, 0.08), shade(WOOD, 0.04));
    for (const sx of [-1, 1]) {
      for (let i = 0; i < 4; i++) W.tube([P(gx + sx * (0.15 + i * 0.26), 0.05, Z - 0.2), P(gx + sx * (0.15 + i * 0.26), 2.55, Z - 0.2)], [0.12, 0.12], 6, shade(WOOD, 0.02 + (i % 2) * 0.05), false);
      W.box(P(gx + sx * 0.55, 1.9, Z - 0.05), V(1.05, 0.12, 0.06), shade(WOOD, -0.15));
      W.box(P(gx + sx * 0.55, 0.7, Z - 0.05), V(1.05, 0.12, 0.06), shade(WOOD, -0.15));
    }
    if (kind === 'castle') for (const sx of [-1, 1]) W.flag(P(gx + sx * 1.9, 4.5, Z - 0.3), 1.55, 0.8, 0.52, defColor);
  }
  // ======================================================= 第四版：Lanternvale 风格的布景 --
  // DESIGN-V4 §2（美术推广 · 攻击画面）：按试点批准的画风（SG.LP / SG.LPProps）重画布景，镜头、队形、时间轴与规则不变——
  //   地面：绘制式顶点色（各群落草地四色斑块与战场同一套、踩踏的泥土与道路、拼布般的田块、沙岸、陡坡岩石、雪）+ LP 的笔触细节纹理，
  //         LP 光照（不描墨线、不加边缘光）；草丛 / 野花 / 卵石合成一个随风摆的网格（不描墨线）。
  //   景物：树按群落取 SG.LPProps 的配方（槐 · 栎 · 榆 · 杨 · 松 · 云杉 · 柏 · 橄榄 · 金合欢 · 猴面包树 · 黄山松 · 海枣 · 椰 · 竹），
  //         石、灌木、村舍（按文化）、搁浅的船同样取配方；全部经 LP.Scatter 实例化（墨线也实例化，远处换远景细节）。
  //   城墙：汉地用试点的城门楼（gate_tower）与夯土城墙（city_wall），角楼为夯土墩台 + 攒尖亭；罗马石墙方塔赤陶顶、
  //         西亚土坯墙圆塔、天竺红砖墙圆顶亭、木栅寨（土垒 + 削尖原木 + 望楼）用 SG.LPProps.parts 组合；城头旗、拒马、云梯描墨线。
  //   光照：每场一个 LP.Mood（自己的太阳与天光；原有材质按主场景的方式缩放），时刻缺省取战场当前的时刻（午后），
  //         没有战场时按种子在午后 / 黄金时刻 / 黄昏之间取；天空与水面随之变色。士兵、武将与战马脚下有柔和的圆影。
  // 没有载入 js/lowpoly.js / js/lp-props.js 时一律退回第三版的画法。
  const lpOK = () => !!(SG.LP && SG.LP.MeshBuilder && SG.LP.material && SG.LP.Scatter && SG.LP.BlobShadows && SG.LP.Mood && SG.LPProps && SG.LPProps.geometry && SG.LPProps.parts);
  const _lhx = {};
  const lhx = h => _lhx[h] || (_lhx[h] = SG.LP.Paint.hex(h));
  const lmix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
  const lmul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
  const lhash = (x, z, s) => SG.LP.hash01([x, s, z], 23);
  // 值噪声（0..1）
  function lnoise(x, z, s) {
    const f = (i, j) => SG.LP.hash01([i, j, s], 17);
    const ix = Math.floor(x), iz = Math.floor(z), tx = x - ix, tz = z - iz, sx = tx * tx * (3 - 2 * tx), sz = tz * tz * (3 - 2 * tz);
    const a = f(ix, iz), b = f(ix + 1, iz), c = f(ix, iz + 1), d = f(ix + 1, iz + 1);
    return (a + (b - a) * sx) * (1 - sz) + (c + (d - c) * sx) * sz;
  }
  // 各群落的草地四色（亮 · 次亮 · 暗 · 最暗），与战场（js/battle-view.js）同一套，向 #D2D294 混 16%
  const LP_MEADOW_HEX = {
    temperate: ['#98AE5C', '#AABD68', '#7E9A4C', '#6E8C46'], steppe: ['#B4AC64', '#C2B870', '#A09A56', '#8E8C4C'], desert: ['#D4BE8A', '#DEC896', '#C6AE7A', '#B89E6C'],
    tropical: ['#84A852', '#94B65E', '#6C9446', '#5E8640'], savanna: ['#C2AE66', '#CEBC74', '#AE9C58', '#9E8E50'], med: ['#A6AA62', '#B4B66E', '#929858', '#848A4E'],
    boreal: ['#84A060', '#92AC6A', '#728E54', '#64804C'], alpine: ['#90A666', '#9EB270', '#7E9458', '#70864E'],
  };
  const LP_GRASS_HEX = {
    temperate: ['#8DB45A', '#A6C46A', '#7AA44E', '#B4C878'], steppe: ['#B8B46A', '#C8C078', '#A6A25C', '#D0C888'], desert: ['#C8B47A', '#D4C088', '#B8A46C', '#DCCB94'],
    tropical: ['#7EAE50', '#96C060', '#6A9A44', '#A8C870'], savanna: ['#C2B468', '#D0C276', '#B0A45C', '#D8CC84'], med: ['#A8B266', '#B8C072', '#96A05A', '#C4C880'],
    boreal: ['#86A85E', '#98B66A', '#749654', '#A8C078'], alpine: ['#90AE66', '#A2BC72', '#80A05A', '#B0C67E'],
  };
  const LP_CROPS_HEX = ['#D8BE6C', '#C9B060', '#B8C46A', '#9EB45E', '#C8A464', '#A8875A', '#D0C484'];
  const LP_FLOWER_HEX = ['#F5F0E6', '#F2B8C6', '#F4D35E', '#C9A8FF', '#F08C6C'];
  const _lpPal = {};
  function lpMeadow(biome) {
    const k = LP_MEADOW_HEX[biome] ? biome : 'temperate';
    return _lpPal[k] || (_lpPal[k] = LP_MEADOW_HEX[k].map(h => lmix(lhx(h), lhx('#D2D294'), 0.16)));
  }
  // 一个地面顶点的绘制色（sRGB 0..1）：ga = groundAttr 的地表权重，slope = 1 − 法线 y
  function lpGroundPaint(kind, x, z, y, ga, slope, look, pal, crops, snowH) {
    const n1 = lnoise(x * 0.16, z * 0.16, 1), n2 = lnoise(x * 0.55, z * 0.55, 2), n3 = lnoise(x * 1.3, z * 1.3, 3);
    let c = lmix(lmix(pal[2], pal[0], sstep(0.25, 0.75, n1)), pal[1], sstep(0.55, 0.85, n2) * 0.7);
    c = lmix(c, pal[3], sstep(0.32, 0.12, n2) * 0.6);
    c = lmul(c, 0.93 + n3 * 0.12);
    if (kind === 'forest') c = lmix(c, lmix(lmul(pal[3], 0.86), lhx('#6E6A3C'), 0.12 + 0.22 * n3), 0.5);
    // 田块：按格的拼布（作物色），田埂略暗
    const farm = ga.g3;
    if (farm > 0.02) {
      const fu = Math.floor(x / 3.6 + 0.4 * lnoise(z * 0.2, 3.1, 9)), fv = Math.floor(z / 2.4 + 0.3 * lnoise(x * 0.2, 7.3, 10));
      const crop = lmul(crops[Math.floor(lhash(fu, fv, 13) * crops.length) % crops.length], 0.95 + 0.1 * n3);
      c = lmix(c, crop, clamp01(farm * 0.95));
    }
    // 泥土：踩踏、道路（边缘按噪声起伏）
    const dW = sstep(0.3, 0.72, ga.g0 + (n2 - 0.5) * 0.45 + (n3 - 0.5) * 0.3);
    if (dW > 0.01) {
      let d = lmix(lhx('#AC8155'), lhx('#BE9566'), sstep(0.45, 0.85, n2) * 0.8);
      d = lmix(d, lhx('#8E6744'), sstep(0.62, 0.92, n3) * 0.5);
      c = lmix(c, d, dW);
    }
    // 门前石板
    if (ga.g2 < 0) c = lmix(c, lmul(lmix(lhx('#A8A294'), lhx('#BDB6A6'), n3), 0.96 + 0.08 * n2), clamp01(-ga.g2));
    // 沙：河岸、海滩、沙地斑块
    const sW = sstep(0.3, 0.7, ga.g1 + (n3 - 0.5) * 0.3);
    if (sW > 0.01) c = lmix(c, lmul(lhx('#E2CFA0'), 0.95 + 0.08 * n2), sW);
    // 河床
    if (kind === 'river' && Math.abs(x - RIVER.x) < RIVER.w) c = lmix(c, lhx('#9E8A68'), 0.6);
    // 墙根阴湿
    if (isWalled(kind)) c = lmix(c, lmul(c, 0.82), (1 - sstep(WALL_Z, WALL_Z + 1.2, z)) * 0.6);
    // 远处高地：草色转黄绿
    const hi = sstep(6, 22, y) * sstep(30, 50, -z);
    if (hi > 0) c = lmix(c, lhx('#B4B276'), hi * 0.55);
    // 岩石：陡坡与山巅（按面着色的灰褐）
    const rW = sstep(0.3, 0.52, slope + (n2 - 0.5) * 0.22 + Math.max(0, ga.g2) * 0.35 + hi * 0.25 + (kind === 'mountain' ? 0.34 + 0.2 * sstep(-6, -16, z) : 0));   // 修订：山岳的坡多露岩（第三轮 0.24 → 0.34）
    if (rW > 0.01) c = lmix(c, lmul(lmix(lhx('#8E877C'), lhx('#A89A84'), n1), 0.9 + 0.16 * n3), rW);
    // 雪
    const snW = sstep(snowH, snowH + 3, y + (n2 - 0.5) * 4) * (1 - sstep(0.45, 0.75, slope));
    if (snW > 0.01) c = lmix(c, lmul(lhx('#EEF1F5'), 0.95 + 0.06 * n3), snW);
    return c;
  }
  // 地面材质：SG.LP.material 的着色器（同一套光照、笔触细节纹理），不加边缘光；跨场次常驻
  function lpGroundMaterial() {
    if (MATS.lpGround !== undefined) return MATS.lpGround;
    MATS.lpGround = null;
    const LP = SG.LP;
    const base = LP.material({ detail: LP.grassDetail(), planar: 0.55, detailK: 0.8 });
    let fs = base.fragmentShader;
    const rl = fs.match(/float rim = pow\([^;]*;/);
    if (rl) fs = fs.replace(rl[0], 'float rim = 0.0;');
    const u = THREE.UniformsUtils.merge([THREE.UniformsLib.lights, THREE.UniformsLib.fog, {
      uRimBoost: { value: 0 }, uFogScale: { value: 1 }, uWindScale: { value: 0 }, uFade: { value: 1 }, uLit: { value: -1 }, uPlanar: { value: 0.55 }, uDetailK: { value: 0.8 },
    }]);
    u.uTint = { value: new THREE.Vector3(1, 1, 1) };
    u.uFlash = { value: new THREE.Vector4(1, 1, 1, 0) };
    u.uDetail = { value: LP.grassDetail() };
    for (const k in LP.globals) u[k] = LP.globals[k];
    const m = new THREE.ShaderMaterial({ uniforms: u, vertexShader: base.vertexShader, fragmentShader: fs, lights: true, fog: true, vertexColors: true });
    m.defines = Object.assign({}, base.defines || {});
    m.name = 'ClashGroundLP';
    MATS.lpGround = m;
    return m;
  }
  // 景物：LPProps 的配方经 LP.Scatter 实例化（同一配方 / 种子 / 选项的副本一次绘制 + 一次墨线），远处的块换远景细节
  // 镜头固定在两军前方：远近细节按位置一次定好（z < LP_FAR_Z 用远景细节、不投影），每种几何体只一个实例化网格 + 一个实例化墨线
  const LP_FAR_Z = -14;
  class LPSet {
    constructor() { this.sc = new SG.LP.Scatter({ chunk: 0, name: 'ClashProps' }); this.n = 0; this.tris = 0; }
    add(name, opts, x, y, z, yaw, scale, tint) {
      try {
        // opts.near：远处也用近景模型（山岳两翼的大石簇：远景的石块只剩几个八面体，太大太显眼）
        const far = z < LP_FAR_Z && !(opts && opts.near), o = Object.assign({}, opts || {}, { lod: far ? 'far' : 'near' });
        delete o.near;
        const g = SG.LPProps.geometry(name, o);
        const t = tint || null;
        if (g.ink) this.sc.add(g.ink, x, y, z, yaw, scale, t, { castShadow: g.castShadow && !far, outline: !far });
        if (g.plain) this.sc.add(g.plain, x, y, z, yaw, scale, t, { outline: false, castShadow: g.plainShadow && !far });
        this.n++; this.tris += g.tris;
        return g;
      } catch (e) { console.error(e); return null; }
    }
    size(name, opts) { const g = SG.LPProps.geometry(name, opts || {}); return g.size; }
    // 树：type 为第三版的树种名，s ≈ 树高（场景单位）
    tree(x, y, z, s, type, look, seed) {
      const h = lhash(x, z, 5), b = look.biome;
      let name;
      switch (type) {
        case 'pine': name = b === 'boreal' && h < 0.55 ? 'tree_spruce' : 'tree_pine'; break;
        case 'poplar': name = 'tree_poplar'; break;
        case 'broad2': name = b === 'tropical' ? 'tree_huai' : h < 0.5 ? 'tree_oak' : 'tree_elm'; break;
        case 'palm': name = b === 'tropical' || b === 'savanna' ? 'palm_coconut' : 'palm_date'; break;
        case 'cypress': name = 'tree_cypress'; break;
        case 'olive': name = 'tree_olive'; break;
        case 'acacia': name = h < 0.82 ? 'tree_acacia' : 'tree_baobab'; break;
        case 'umbrella': name = 'tree_pine_cloud'; break;
        case 'scrub': return this.bush(x, y, z, s * 0.42, look, 'round', [1.06, 1.0, 0.8]);
        default: name = b === 'tropical' ? (h < 0.3 ? 'bamboo' : 'tree_huai') : b === 'steppe' || b === 'savanna' ? 'tree_elm' : h < 0.4 ? 'tree_huai' : h < 0.7 ? 'tree_oak' : 'tree_elm';
      }
      const sd = (seed | 0) & 1, sz = this.size(name, { seed: sd }), k = 1.2 * s / Math.max(0.1, sz[1]);
      const dry = b === 'steppe' || b === 'desert' || b === 'savanna' ? [1.06, 1.02, 0.86] : b === 'boreal' || b === 'alpine' ? [0.92, 0.97, 0.98] : [1, 1, 1];
      const v = 0.93 + lhash(x, z, 9) * 0.14;
      return this.add(name, { seed: sd }, x, y, z, lhash(x, z, 7) * 6.283, k, [dry[0] * v, dry[1] * v, dry[2] * v]);
    }
    bush(x, y, z, w, look, variant, tint) {
      const sz = this.size('bush', { variant: variant || 'pilot' }), k = w / Math.max(0.1, sz[0]);
      const v = 0.92 + lhash(x, z, 4) * 0.16, t = tint || (look && (look.biome === 'desert' || look.biome === 'steppe' || look.biome === 'savanna') ? [1.08, 1.02, 0.82] : [1, 1, 1]);
      return this.add('bush', { variant: variant || 'pilot', seed: (lhash(x, z, 3) * 2) | 0 }, x, y, z, lhash(x, z, 8) * 6.283, k, [t[0] * v, t[1] * v, t[2] * v]);
    }
    rock(x, y, z, r, seed) {
      const name = r < 0.3 ? 'rock_small' : r < 0.5 ? 'rock_large' : 'boulder', sd = (seed | 0) & 1, sz = this.size(name, { seed: sd });
      const k = 2.5 * r / Math.max(0.1, sz[0]), g = 0.93 + lhash(x, z, 6) * 0.14;
      return this.add(name, { seed: sd }, x, y - 0.06 * r, z, lhash(x, z, 2) * 6.283, [k, k * (0.8 + lhash(x, z, 1) * 0.3), k], [g, g * 0.99, g * 0.97]);
    }
    // 村舍：第三版的房屋样式 → 配方
    house(style, x, y, z, s, yaw) {
      const H = { han: ['house_han', 0], tile: ['house_med', 0], flat: ['house_mud', 0], yurt: ['yurt', 0], long: ['house_thatch', 0], round: ['house_thatch', 1], stilt: ['house_thatch', 3], thatch: ['house_thatch', 2] }[style] || ['house_han', 0];
      const sd = (H[1] + Math.floor(lhash(x, z, 11) * 2) * 2) & 3;
      return this.add(H[0], { seed: H[0] === 'house_thatch' ? H[1] : sd }, x, y, z, yaw != null ? yaw : (lhash(x, z, 12) - 0.5) * 0.5, FIG_S * 0.9 * (s || 1));
    }
    build(scene) { const g = this.sc.build(); scene.add(g); return g; }
  }
  // 草丛 / 野花 / 卵石：合成一个不描墨线、随风摆的网格（与战场的草同一做法）
  function lpGrassMesh(items, seed, biome) {
    const LP = SG.LP, P = LP.Paint;
    const M = new LP.MeshBuilder(seed || 811);
    M.jitter = 0.08; M.windGradient = true; M.wind = 0.5;
    const greens = (LP_GRASS_HEX[biome] || LP_GRASS_HEX.temperate).map(lhx), reeds = ['#B8B46A', '#A8A45E', '#C4BE78'].map(lhx), flowers = LP_FLOWER_HEX.map(lhx);
    for (const it of items) {
      const k = it.k;
      if (it.t === 'tuft' || it.t === 'reed') {
        M.windY0 = it.y; M.windY1 = it.y + 0.4 * k * (it.t === 'reed' ? 2 : 1);
        const pal = it.t === 'reed' ? reeds : greens;
        if (it.t === 'reed') { M.push().translate(it.x, it.y, it.z).scale(1, 2.2, 1); M.tuft([0, 0, 0], 0.16 * k, 5, P.shade(pal[(M.random01() * 3) | 0], it.dim || 1)); M.pop(); }
        else M.tuft([it.x, it.y, it.z], (0.16 + M.random01() * 0.12) * k, 4 + ((M.random01() * 3) | 0), P.shade(pal[(M.random01() * 4) | 0], it.dim || 1));
      } else if (it.t === 'flowers') {
        M.windY0 = it.y; M.windY1 = it.y + 0.3;
        const fc = it.c || flowers[(M.random01() * flowers.length) | 0];
        for (let i = 0; i < 3; i++) {
          const a = M.random01() * Math.PI * 2, d = Math.sqrt(M.random01()) * 0.14 * k, x = it.x + Math.cos(a) * d, z = it.z + Math.sin(a) * d, fy = (0.1 + M.random01() * 0.1) * k;
          M.color = lhx('#5E8E42'); M.segment([x, it.y, z], [x, it.y + fy, z], 0.006, 0.005, 3, false, false);
          M.color = fc; M.emission = 0.06;
          M.push().translate(x, it.y + fy, z).rotate(-20 + M.random01() * 40, M.random01() * 360, 0);
          M.disc([0, 0, 0], 0.035 * k, 5); M.disc([0, -0.003, 0], 0.035 * k, 5, true);
          M.color = lhx('#F4D35E'); M.sphere([0, 0.006, 0], 0.011 * k, 4, 2, false);
          M.pop(); M.emission = 0;
        }
      } else if (it.t === 'pebble') {
        M.wind = 0;
        const greys = [lhx('#9C948A'), lhx('#B3AB9E'), lhx('#7F786F')], r = (0.03 + M.random01() * 0.035) * k;
        M.color = P.shade(greys[(M.random01() * 3) | 0], 0.92 + M.random01() * 0.14);
        M.push().translate(it.x, it.y, it.z).rotate(0, M.random01() * 360, 0).scale(1, 1, 0.7 + M.random01() * 0.3);
        M.lathe([[r, 0.004], [r * 0.75, r * 0.45], [0, r * 0.62]], 5, false, false, false);
        M.pop(); M.wind = 0.5;
      }
    }
    if (M.isEmpty) return null;
    const mesh = LP.mesh(M.toGeometry(), { outline: false, castShadow: false });
    mesh.name = 'ClashGrass';
    return mesh;
  }
  // 战场遗物（折断的长枪、斜倚的圆盾、倒地的小旗）：LP 网格，描墨线
  // 修订：倒地小旗的旗面画进 CL（不描墨线、不投影的网格，朝上的单面）——在描墨线的网格里，双面旗面朝下的那一面会被
  // 外壳墨线着成墨色、与旗面共面闪烁，近景的小旗成了一块块黑楔子
  function lpDebris(M, H, rnd, cols, dry, rock, CL) {
    const P = SG.LP.Paint, wood = lhx('#8A6440'), metal = lhx('#A8A8B0');
    const lie = (x, z, len, ang, r, col, tip) => {
      const ca = Math.cos(ang) * len / 2, sa = Math.sin(ang) * len / 2;
      const a = [x - ca, H(x - ca, z - sa) + r, z - sa], b = [x + ca, H(x + ca, z + sa) + r + 0.02, z + sa];
      M.color = col; M.segment(a, b, r, r * 0.9, 5);
      if (tip) { M.color = metal; M.segment(b, [x + ca * 1.14, H(x + ca, z + sa) + r + 0.02, z + sa * 1.14], r * 1.4, 0.004, 4); }
    };
    for (let k = 0; k < 14; k++) {
      const x = (rnd.nextDouble() * 2 - 1) * 12, z = 3.2 + rnd.nextDouble() * 3.4;
      if (dry(x, z) < 1) continue;
      const u = rnd.nextDouble();
      if (u < 0.28) { const br = rnd.nextDouble() < 0.5; lie(x, z, (br ? 0.45 : 0.9) + rnd.nextDouble() * 0.35, rnd.nextDouble() * 3.14, 0.016, P.shade(wood, 1.05), !br); }
      else if (u < 0.5) {
        const y = H(x, z), c = cols.length ? SG.LP.Paint.hex(cols[k % cols.length]) : lhx('#80503A');
        M.push().translate(x, y + 0.08, z).rotate(0, rnd.nextDouble() * 360, 0).rotate(30 + rnd.nextDouble() * 28, 0, 0);
        M.color = P.shade(c, 0.85); M.cylinder([0, -0.012, 0], 0.21, 0.2, 0.032, 10, false, true, true);
        M.color = metal; M.sphere([0, 0.03, 0], 0.05, 6, 3, false);
        M.pop();
      } else if (u < 0.6 && cols.length) {
        const ang = rnd.nextDouble() * 3.14, c = SG.LP.Paint.hex(cols[k % cols.length]);
        lie(x, z, 1.3, ang, 0.016, P.shade(wood, 0.95), false);
        const ca = Math.cos(ang), sa = Math.sin(ang), y = H(x, z) + 0.03, nx = -sa, nz = ca;
        const q = (a, b) => [x + ca * a + nx * b, y + 0.012 * Math.sin(a * 5 + b * 3), z + sa * a + nz * b];
        const F = CL || M;
        for (let i = 0; i < 3; i++) { const a0 = 0.1 + i * 0.16, a1 = a0 + 0.16; F.color = P.shade(c, i % 2 ? 0.92 : 1.02); F.quad(q(a0, 0), q(a1, 0), q(a1, 0.36), q(a0, 0.36), [0, 1, 0]); }
      } else if (rock) rock(x, H(x, z), z, 0.1 + rnd.nextDouble() * 0.14, 70 + k);
    }
  }
  // 配方对象（同 SG.LPProps.make：描墨线的网格 + 不描的网格 + 牌匾），牌匾文字可按原文替换（texts：{ 原文: 新文 }）——
  // 城门楼的「城名」选项与配方名同名（LPProps 的 normalize 把 o.name 覆盖成配方名），牌匾在这里改写
  function lpMakeNamed(name, opts, texts) {
    const LP = SG.LP, g = SG.LPProps.geometry(name, opts), root = new THREE.Group();
    if (g.ink) root.add(LP.mesh(g.ink, { castShadow: g.castShadow }));
    if (g.plain) root.add(LP.mesh(g.plain, { outline: false, castShadow: g.plainShadow }));
    for (const d of g.decals) {
      const text = texts && texts[d.text] != null ? texts[d.text] : d.text;
      const dm = LP.decal(LP.boardTexture(text, { w: 448, h: Math.round(448 * d.h / d.w), board: d.board, ink: d.ink, vertical: d.vertical, line: d.line }), d.w, d.h);
      dm.position.set(d.at[0], d.at[1], d.at[2]);
      if (d.yaw) dm.rotation.y = d.yaw * Math.PI / 180;
      if (d.tilt) dm.rotation.x = d.tilt * Math.PI / 180;
      if (d.local) dm.translateZ(d.local[2]);
      root.add(dm);
    }
    root.userData.lp = { name, lamps: g.lamps, anchors: g.anchors, tris: g.tris, size: g.size };
    return root;
  }
  // ---- 城墙（LP，按米建模后缩放 FIG_S；墙正面在 WALL_Z，墙顶约 3.1 单位，城门中心 GATE_X）--
  // 方形墩台（四面有层理 / 砖缝）：中心 (x, z)，边长 s，高 h（米，当前变换下）
  function lpBlock(M, kind, x, z, s, h, merl) {
    const parts = SG.LPProps.parts;
    M.push().translate(x, 0, z);
    parts.wall(M, { len: s, h, thick: s, kind, batter: 0.04, faces: 'fk' });
    M.push().rotate(0, 90, 0); parts.wall(M, { len: s - 0.02, h: h - 0.01, thick: s - 0.02, kind, batter: 0.04, faces: 'fk' }); M.pop();
    if (merl) {
      const n = Math.max(2, Math.round(s / 1.15)), xs = [];
      for (let i = 0; i < n; i++) xs.push(-s / 2 + (i + 0.5) * s / n);
      for (let k = 0; k < 4; k++) { M.push().rotate(0, k * 90, 0); parts.merlons(M, xs, h, s / 2 - 0.2 - 0.04 * h, [s / n * 0.55, 0.7, 0.4], merl); M.pop(); }
    }
    M.pop();
  }
  // 修订：与试点城墙（city_wall）同样层理的夯土墩台——约 0.6 米一层、深浅两色交替、每层上亮下暗，层间手绘的暗缝；
  // 四面收分；顶上垛口。中心 (x, z)，宽 w、深 d、高 h（米，当前变换下）
  function lpCourseBlock(M, x, z, w, d, h, merl) {
    const P = SG.LP.Paint, PAL = SG.LP.PAL, bat = 0.1, parts = SG.LPProps.parts;
    const ys = [0, 0.6];
    for (let y = 1.2; y < h - 0.35; y += 0.6) ys.push(y);
    ys.push(h);
    const face = (y0, y1, e, cols) => {
      const a0 = w / 2 - bat * y0 + e, b0 = d / 2 - bat * y0 + e, a1 = w / 2 - bat * y1 + e, b1 = d / 2 - bat * y1 + e;
      M.quadColors([x - a0, y0, z + b0], [x + a0, y0, z + b0], [x + a1, y1, z + b1], [x - a1, y1, z + b1], [0, bat, 1], cols);
      M.quadColors([x + a0, y0, z - b0], [x - a0, y0, z - b0], [x - a1, y1, z - b1], [x + a1, y1, z - b1], [0, bat, -1], cols);
      M.quadColors([x + a0, y0, z + b0], [x + a0, y0, z - b0], [x + a1, y1, z - b1], [x + a1, y1, z + b1], [1, bat, 0], cols.map(c => P.shade(c, 0.94)));
      M.quadColors([x - a0, y0, z - b0], [x - a0, y0, z + b0], [x - a1, y1, z + b1], [x - a1, y1, z - b1], [-1, bat, 0], cols.map(c => P.shade(c, 0.94)));
    };
    for (let i = 0; i < ys.length - 1; i++) {
      const col = P.shade(i % 2 ? PAL.rammed : P.mix(PAL.rammed, PAL.rammedDark, 0.32), i === 0 ? 0.88 : 1);
      const cb = P.shade(col, 0.93), ct = P.shade(col, 1.02);
      face(ys[i], ys[i + 1], 0, [cb, cb, ct, ct]);
    }
    const seam = P.shade(PAL.rammedDark, 0.9);
    for (let i = 2; i < ys.length - 1; i++) face(ys[i] - 0.014, ys[i] + 0.014, 0.008, [seam, seam, seam, seam]);
    const tw = w / 2 - bat * h, td = d / 2 - bat * h;
    M.color = P.shade(PAL.rammed, 1.04);
    M.quad([x - tw, h, z + td], [x + tw, h, z + td], [x + tw, h, z - td], [x - tw, h, z - td], [0, 1, 0]);
    if (merl) {
      const n = Math.max(2, Math.round(2 * tw / 1.15)), xs = [];
      for (let i = 0; i < n; i++) xs.push(-tw + (i + 0.5) * 2 * tw / n);
      M.push().translate(x, 0, z);
      for (let k = 0; k < 4; k++) { M.push().rotate(0, k * 90, 0); parts.merlons(M, xs, h, tw - 0.2, [2 * tw / n * 0.55, 0.7, 0.4], merl); M.pop(); }
      M.pop();
    }
  }
  // 攒尖 / 歇山小亭（角楼）：四柱、檐额、屋顶，立在 y 高处
  function lpPavilion(M, x, y, z, w, d, style, pal) {
    const P = SG.LP.Paint, parts = SG.LPProps.parts, PAL = SG.LP.PAL;
    const hw = w / 2 - 0.25, hd = d / 2 - 0.25, ch = 2.0;
    M.color = P.shade(PAL.stone, 0.95); M.box([x, y + 0.08, z], [w, 0.16, d], null, 'tfkrl');
    parts.columns(M, [[x - hw, z - hd], [x + hw, z - hd], [x - hw, z + hd], [x + hw, z + hd]], y + 0.16, y + 0.16 + ch, { r: 0.13 });
    M.color = PAL.hanRedDark; M.box([x, y + ch + 0.05, z + hd], [w - 0.3, 0.22, 0.16], null, 'fbt'); M.box([x, y + ch + 0.05, z - hd], [w - 0.3, 0.22, 0.16], null, 'kbt');
    M.color = P.shade(PAL.hanRed, 0.9); M.box([x, y + 0.75, z], [w - 0.7, 1.15, d - 0.7], null, 'fkrl');
    parts.hanRoof(M, { style, w: w - 0.2, d: d - 0.2, y: y + 0.16 + ch + 0.12, palette: pal || 'slate', detail: 'low', cx: x, cz: z, over: 0.7 });
  }
  // 圆塔（土坯 / 石）：roof 'flat' 垛口平顶 / 'cone' 赤陶锥顶 / 'dome' 圆顶
  function lpRoundTower(M, x, z, r, h, wallC, roof, merl) {
    const P = SG.LP.Paint, PAL = SG.LP.PAL;
    M.push().translate(x, 0, z);
    // 修订：抹泥 / 砖砌的塔身是一整片平静的墙面（不再逐行换色、逐面抖动成马赛克）：两米以上一段，层理画成细的暗环
    const j0 = M.jitter;
    M.jitter = 0.025;
    M.color = P.shade(wallC, 0.86); M.cylinder([0, 0, 0], r * 1.08, r * 1.04, 0.5, 12, false, false, false);
    const rows = Math.max(1, Math.round((h - 0.5) / 2.2));
    M.color = P.shade(wallC, 0.98);
    for (let i = 0; i < rows; i++) M.cylinder([0, 0.5 + (h - 0.5) * i / rows, 0], r, r, (h - 0.5) / rows, 12, false, false, i === rows - 1);
    M.color = P.shade(wallC, 0.9);
    for (let y = 1.25; y < h - 0.6; y += 1.25) M.band([0, 0, 0], r + 0.02, r + 0.02, y - 0.05, y + 0.05, 12);
    M.jitter = j0;
    M.color = P.shade(wallC, 1.05); M.band([0, 0, 0], r + 0.08, r + 0.08, h - 0.12, h + 0.08, 12);
    if (roof === 'cone') { M.color = PAL.terracotta; M.cylinder([0, h + 0.08, 0], r + 0.35, 0, r * 1.25, 12, false, true, false); }
    else if (roof === 'dome') SG.LPProps.parts.dome(M, { c: [0, h + 0.08, 0], r: r * 0.7, drumH: 0.25, color: '#E8E0D0', sides: 10 });
    if (roof !== 'cone' && merl) for (let i = 0; i < 9; i++) { const a = i / 9 * Math.PI * 2; M.color = P.shade(merl, 0.95 + 0.1 * (i % 2)); M.box([Math.cos(a) * (r - 0.2), h + 0.4, Math.sin(a) * (r - 0.2)], [0.55, 0.7, 0.5], null, 'tfkrl'); }
    M.pop();
  }
  // 方塔（罗马：石砌、拱窗、赤陶四坡顶；天竺：红砖、顶上圆顶亭）
  function lpSquareTower(M, kind, x, z, s, h, roof, merl, R) {
    const P = SG.LP.Paint, PAL = SG.LP.PAL, parts = SG.LPProps.parts;
    R = R || M;     // 塔顶另画进 R（红砖塔身整体染色时，白色圆顶亭不跟着染）
    lpBlock(M, kind, x, z, s, h, roof === 'chhatri' ? merl : null);
    if (roof === 'tile') {
      R.push().translate(x, 0, z);
      R.color = P.shade(PAL.stoneLight, 0.98); R.box([0, h + 0.1, 0], [s + 0.3, 0.2, s + 0.3], null, 'tbfkrl');
      parts.terracottaRoof(R, { style: 'hip', w: s + 0.4, d: s + 0.4, y: h + 0.2, over: 0.3, rise: s * 0.4, detail: 'low' });
      R.color = lhx('#3A2E28'); for (const wy of [h * 0.55, h * 0.8]) R.box([0, wy, s / 2 + 0.02], [0.6, 0.9, 0.06], null, 'f');
      R.pop();
    } else if (roof === 'chhatri') {
      R.push().translate(x, h, z);
      const c = lhx('#E8DCC4');
      R.color = P.shade(c, 0.95); R.box([0, 0.1, 0], [s * 0.7, 0.2, s * 0.7], null, 'tfkrl');
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) { R.color = c; R.cylinder([sx * s * 0.26, 0.2, sz * s * 0.26], 0.09, 0.09, 1.1, 6, false, false, false); }
      R.color = P.shade(c, 0.92); R.box([0, 1.35, 0], [s * 0.78, 0.18, s * 0.78], null, 'tbfkrl');
      parts.dome(R, { c: [0, 1.44, 0], r: s * 0.3, drumH: 0.1, color: '#ECE4D2', sides: 10, finial: PAL.gold });
      R.pop();
    }
  }
  // 木望楼：四柱（略收）、平台、护栏、茅草（草原：毡）攒尖顶；中心 (x, z)，宽 s，平台高 h（米）
  function lpWatchtower(M, x, z, s, h, felt) {
    const P = SG.LP.Paint, PAL = SG.LP.PAL, hs = s / 2 - 0.2;
    M.color = PAL.timber;
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.segment([x + sx * hs, 0, z + sz * hs], [x + sx * hs * 0.88, h + 1.4, z + sz * hs * 0.88], 0.17, 0.14, 6);
    M.color = PAL.timberDark;
    for (const sx of [-1, 1]) M.segment([x + sx * hs, 0.6, z - hs], [x + sx * hs * 0.94, h * 0.62, z + hs * 0.94], 0.07, 0.07, 4);
    M.color = PAL.wood; M.box([x, h, z], [s + 0.4, 0.24, s + 0.4], P.shade(PAL.woodLight, 0.95), 'tbfkrl');
    M.color = P.shade(PAL.wood, 0.9);
    for (let k = 0; k < 4; k++) { M.push().translate(x, 0, z).rotate(0, k * 90, 0); M.box([0, h + 0.55, hs + 0.15], [s + 0.3, 0.75, 0.12], null, 'fkt'); M.pop(); }
    if (felt) { M.color = lhx('#D8CCB4'); M.cylinder([x, h + 1.4, z], s * 0.78, 0.12, s * 0.7, 8, false, true, false); }
    else SG.LPProps.parts.thatchRoof(M, { w: s + 0.2, d: s + 0.2, y: h + 1.4, style: 'hip', over: 0.5, rise: s * 0.72, cx: x, cz: z });
  }
  // 木栅：一排削尖的原木（沿 X，中心在原点，前方 +Z）
  function lpPalisade(M, len, h) {
    const PAL = SG.LP.PAL, P = SG.LP.Paint;
    const n = Math.max(3, Math.round(len / 0.42));
    for (let i = 0; i < n; i++) {
      const x = -len / 2 + (i + 0.5) * len / n, hh = h * (0.9 + 0.1 * ((i * 7) % 3) / 2);
      M.color = P.shade(i % 2 ? PAL.wood : PAL.timber, 0.95 + 0.1 * ((i * 5) % 3) / 2);
      M.cylinder([x, 0, 0], 0.21, 0.2, hh, 6, false, false, false);
      M.color = P.shade(PAL.woodLight, 0.95); M.cone([x, hh, 0], 0.2, 0.45, 6);
    }
    M.color = PAL.timberDark; for (const y of [h * 0.3, h * 0.65]) M.box([0, y, 0.2], [len, 0.16, 0.1], null, 'tfkb');
  }
  // 拒马（cheval-de-frise）与倚墙的云梯：米，当前变换下，中心在原点
  function lpJuma(M, len) {
    const PAL = SG.LP.PAL, P = SG.LP.Paint;
    M.color = P.shade(PAL.timber, 0.95); M.segment([-len / 2, 0.72, 0], [len / 2, 0.72, 0], 0.11, 0.11, 6);
    const n = Math.max(2, Math.round(len / 1.1));
    for (let i = 0; i < n; i++) {
      const x = -len / 2 + 0.4 + (len - 0.8) * (i / Math.max(1, n - 1));
      M.color = P.shade(PAL.woodLight, 0.92);
      M.segment([x, 0.04, -0.84], [x, 1.56, 0.8], 0.07, 0.015, 5);
      M.segment([x + 0.08, 0.04, 0.84], [x + 0.08, 1.56, -0.8], 0.07, 0.015, 5);
    }
  }
  /**
   * 城墙与城门（LP）：返回 { geo, objs: [THREE.Object3D]（城门楼等配方对象）, lamps: [[x,y,z]], flags: [[x,y,z]] }，
   * 世界坐标（场景单位）。st：han / roman / mud / brick / palisade（look.wall）；kind：gate / castle / wall。
   */
  function buildWallLP(sc, LPS, kind, rnd, defHex, H, look) {
    const LP = SG.LP, P = LP.Paint, PAL = LP.PAL, parts = SG.LPProps.parts, S = FIG_S;
    const st = (look && look.wall) || 'han', castle = kind === 'castle';
    const gx = kind === 'wall' ? null : GATE_X;
    const X0 = -0.6, X1 = 44;
    const M = new LP.MeshBuilder(((sc.seed | 0) & 0xffff) + 501);
    M.jitter = 0.06; M.aoStrength = 0.3; M.aoHeight = 1.2;
    const objs = [], lamps = [], flagsAt = [];
    // 墙后（城内）露出屋顶的房屋：按文化
    const inner = (style) => {
      for (let k = 0; k < 7; k++) {
        const x = 2 + k * 3.4 + rnd.nextDouble() * 1.2, z = WALL_Z - 3.6 - rnd.nextDouble() * 5;
        if (gx !== null && Math.abs(x - gx) < 2.6) continue;
        LPS.house(style, x, 1.25 + rnd.nextDouble() * 0.3, z, 1.15, (rnd.nextDouble() - 0.5) * 0.3);
      }
    };
    const wy = H(X0 + 6, WALL_Z) - 0.05;
    if (st === 'han') {
      // 试点的夯土城墙（正面在本地 z = 2.4 米）与城门楼（城门楼比墙前凸 0.4 米）
      const wallZ = WALL_Z - 2.4 * S, gateZ = wallZ + 0.4 * S;
      const gw = 6.5 * S;
      // 修订：左端角楼左移 5.5（城门与角楼之间露出一段试点城墙）；墙段一律同一长度（取左段能整除的长度，右段向画外延伸），
      // 两个变体各一组实例（LP.Scatter），不再是每段一个网格
      const XC = X0 - 5.5;
      const segs = [];
      if (gx === null) segs.push([XC + 1.1, X1]); else { segs.push([XC + 1.1, gx - gw + 0.15]); segs.push([gx + gw - 0.15, X1]); }
      const len0 = segs[0][1] - segs[0][0], tile = len0 / Math.max(1, Math.round(len0 / (18 * S)));
      const tileM = Math.round(tile / S * 10) / 10, tileU = tileM * S;
      let wi = 0;
      segs.forEach(([a, b], si) => {
        const len = b - a;
        if (len < 0.4) return;
        const n = Math.max(1, si === 0 ? Math.round(len / tileU) : Math.ceil(len / tileU));
        // 左段：从城门一侧往左排（最左一段的端头藏在角楼墩台里）；右段：从城门往右排（末段伸出画外）
        for (let i = 0; i < n; i++) {
          const cx = si === 0 && gx !== null ? b - tileU * (i + 0.5) : a + tileU * (i + 0.5);
          LPS.add('city_wall', { len: tileM, variant: wi++ & 1, color: defHex }, cx, wy, wallZ, 0, S);
        }
      });
      if (gx !== null) {
        const g = lpMakeNamed('gate_tower', { color: defHex, name: (sc.opts.scene && sc.opts.scene.city) || '城门', hallName: castle ? '镇远楼' : '宾阳楼' }, { gate_tower: (sc.opts.scene && sc.opts.scene.city) || '城门' });
        g.position.set(gx, H(gx, WALL_Z) - 0.05, gateZ); g.scale.setScalar(S);
        objs.push(g);
        const L = g.userData.lp && g.userData.lp.lamps;
        if (L) for (const l of L) lamps.push({ pos: [gx + l.pos[0] * S, g.position.y + l.pos[1] * S, gateZ + l.pos[2] * S], o: l });
      }
      // 左端角楼：与城墙同样层理的夯土墩台（比墙高约 1.5 米）+ 攒尖亭
      M.push().translate(XC, wy, wallZ).scale(S);
      lpCourseBlock(M, 0, 0.3, 5.6, 5.6, 5.6, PAL.rammed);
      lpPavilion(M, 0, 5.6, 0.3, 4.2, 4.2, 'pyramid', 'slate');
      M.pop();
      inner('han');
      // 城头旗（守方势力色）
      for (let k = -2; k < 6; k++) { const x = 1.6 + k * 2.6; if (gx !== null && Math.abs(x - gx) < 3.6) continue; flagsAt.push([x, wy + 4.1 * S + 0.05, WALL_Z - 0.55]); }
    } else if (st === 'palisade') {
      const zc = WALL_Z - 0.45;
      // 土垒（梯形断面的草坡）+ 削尖原木 + 横向绑木
      const run = (a, b) => {
        const len = (b - a) / S;
        if (len < 0.5) return;
        M.push().translate((a + b) / 2, H((a + b) / 2, zc) - 0.05, zc).scale(S);
        const b0 = 2.1, b1 = 0.9, bh = 0.9;
        M.quad([-len / 2, 0, b0], [len / 2, 0, b0], [len / 2, bh, b1], [-len / 2, bh, b1], [0, 0.7, 1], lhx('#8E9A58'));
        M.quad([-len / 2, bh, -b1], [-len / 2, bh, b1], [len / 2, bh, b1], [len / 2, bh, -b1], [0, 1, 0], lhx('#9AA462'));
        M.quad([-len / 2, 0, -b0], [-len / 2, bh, -b1], [len / 2, bh, -b1], [len / 2, 0, -b0], [0, 0.7, -1], lhx('#7E8A50'));
        M.push().translate(0, bh - 0.15, 0.1); lpPalisade(M, len, 5.0); M.pop();
        M.pop();
      };
      if (gx === null) run(X0, X1); else { run(X0, gx - 1.25); run(gx + 1.25, X1); }
      const steppe = look && (look.culture === 'steppe' || look.culture === 'sarmatian');
      const tower = (x, z, h) => { M.push().translate(x, H(x, z) - 0.05, z).scale(S); lpWatchtower(M, 0, 0, 3.0, h, steppe); M.pop(); };
      tower(X0 + 0.4, zc - 0.2, 6.6);
      if (gx !== null) {
        for (const sx of [-1, 1]) tower(gx + sx * 2.05, zc - 0.2, 6.8);
        // 寨门：门上走道、原木门扇
        M.push().translate(gx, H(gx, zc) - 0.05, zc).scale(S);
        M.color = PAL.timberDark; M.box([0, 5.9, 0], [7.0, 0.4, 2.0], P.shade(PAL.wood, 0.95), 'tbfkrl');
        M.color = PAL.wood; M.box([0, 6.6, 0.85], [7.0, 1.0, 0.16], null, 'tfkrl');
        for (const sx of [-1, 1]) {
          for (let i = 0; i < 4; i++) { M.color = P.shade(i % 2 ? PAL.wood : PAL.timber, 0.95); M.cylinder([sx * (0.3 + i * 0.52), 0, 0.4], 0.24, 0.24, 5.1, 6, false, true, false); }
          M.color = PAL.timberDark; for (const y of [1.4, 3.8]) M.box([sx * 1.1, y, 0.68], [2.1, 0.24, 0.12], null, 'tfkrl');
        }
        M.pop();
      }
      inner((look && look.house) || 'long');
      for (let k = 0; k < 6; k++) { const x = 1.6 + k * 2.6; if (gx !== null && Math.abs(x - gx) < 3) continue; flagsAt.push([x, wy + 2.7, WALL_Z - 0.4]); }
    } else {
      // 罗马石墙 / 西亚土坯墙 / 天竺红砖墙 / 高句丽石城：墙身（parts.wall）、垛口、墙脚
      const korea = st === 'korea';
      const wk = st === 'roman' || korea ? 'stone' : st === 'mud' ? 'mud' : 'brick';
      const wallC = st === 'roman' ? lhx('#C8BEA8') : korea ? lhx('#A8A49A') : st === 'mud' ? lhx('#C8A47A') : lhx('#B07A5E');
      const Hm = 6.0, Tm = 2.8, zc = WALL_Z - Tm / 2 * S;
      const brickTint = st === 'brick';
      const B2 = brickTint ? new LP.MeshBuilder(((sc.seed | 0) & 0xffff) + 502) : M;
      if (brickTint) { B2.jitter = 0.06; B2.aoStrength = 0.3; B2.aoHeight = 1.2; }
      const run = (a, b) => {
        const len = (b - a) / S;
        if (len < 0.5) return;
        B2.push().translate((a + b) / 2, wy, zc).scale(S);
        parts.wall(B2, { len, h: Hm, thick: Tm, kind: wk, batter: wk === 'mud' ? 0.06 : 0.02 });
        const n = Math.max(2, Math.round(len / 1.3)), xs = [];
        for (let i = 0; i < n; i++) xs.push(-len / 2 + (i + 0.5) * len / n);
        if (st === 'mud') for (const x of xs) { B2.color = P.shade(wallC, 0.95 + 0.08 * ((x * 13 | 0) & 1)); B2.box([x, Hm + 0.3, Tm / 2 - 0.3], [0.8, 0.6, 0.5], null, 'tfkrl'); B2.box([x, Hm + 0.78, Tm / 2 - 0.3], [0.44, 0.36, 0.42], null, 'tfkrl'); }
        else if (st === 'brick') for (const x of xs) { B2.color = P.shade(wallC, 0.95); B2.box([x, Hm + 0.3, Tm / 2 - 0.3], [0.7, 0.6, 0.45], null, 'tfkrl'); B2.color = P.shade(wallC, 1.04); B2.sphere([x, Hm + 0.62, Tm / 2 - 0.3], 0.3, 6, 3, false); }
        else parts.merlons(B2, xs, Hm, Tm / 2 - 0.3, [len / n * 0.55, 0.8, 0.45], wallC);
        if (st === 'roman') parts.plinth(B2, -len / 2, len / 2, Tm / 2 - 0.02, 0.5);
        B2.pop();
      };
      if (gx === null) run(X0, X1); else { run(X0, gx - 2.0); run(gx + 2.0, X1); }
      // 角楼（左端）与城门两侧的塔
      const tower = (x, big) => {
        const tz = zc + 0.1, th = (big ? 8.6 : 8.0);
        const T = st === 'brick' ? B2 : M;
        T.push().translate(x, H(x, tz) - 0.05, tz).scale(S);
        if (T !== M) M.push().translate(x, H(x, tz) - 0.05, tz).scale(S);
        if (st === 'mud') lpRoundTower(T, 0, 0, big ? 2.1 : 2.3, th, wallC, look && look.culture === 'arab' && big ? 'dome' : 'flat', P.shade(wallC, 1.02));
        else if (st === 'roman') lpSquareTower(T, 'stone', 0, 0, big ? 3.2 : 4.0, th, 'tile');
        else if (korea) { const ts = big ? 3.2 : 3.8, tb = th - 2.4; lpBlock(T, 'stone', 0, 0, ts, tb, null); lpPavilion(T, 0, tb, 0, ts - 0.4, ts - 0.4, big ? 'xieshan' : 'pyramid', 'grey'); }
        else lpSquareTower(T, 'brick', 0, 0, big ? 3.2 : 3.8, th, 'chhatri', wallC, M);
        T.pop();
        if (T !== M) M.pop();
      };
      tower(X0, false);
      if (gx !== null) {
        for (const sx of [-1, 1]) tower(gx + sx * 2.45, true);
        // 门洞所在的门台（拱门）
        B2.push().translate(gx, H(gx, zc) - 0.05, zc).scale(S);
        parts.wall(B2, { len: 6.6, h: Hm + 0.2, thick: Tm + 0.6, kind: wk, batter: 0.02 });
        parts.gate(B2, { w: 3.0, h: 4.1, z: (Tm + 0.6) / 2 - 0.02, kind: 'arch', door: st === 'roman' ? '#7A5A3A' : '#8A5A36', studs: st !== 'roman' });
        if (korea) lpPavilion(B2, 0, Hm + 0.2, 0, 6.0, Tm + 0.2, 'xieshan', 'grey');      // 石台上的灰瓦门楼
        else { const n = 5, xs = []; for (let i = 0; i < n; i++) xs.push(-3.3 + (i + 0.5) * 6.6 / n); parts.merlons(B2, xs, Hm + 0.2, (Tm + 0.6) / 2 - 0.3, [0.75, 0.8, 0.45], wallC); }
        B2.pop();
      }
      if (brickTint && !B2.isEmpty) { const bm = LP.mesh(B2.toGeometry(), { castShadow: true, look: { tint: [1.16, 0.8, 0.68] } }); bm.name = 'ClashWallBrick'; objs.push(bm); sc.own.push(bm.geometry); }
      // 城中地标：罗马神庙 / 西亚穹顶殿 / 天竺窣堵波
      const lx = gx !== null ? (gx > 8 ? gx - 6 : gx + 7.5) : 9, lz = WALL_Z - 6.5;
      if (st === 'roman') {
        M.push().translate(lx, 1.2, lz).scale(S);
        M.color = PAL.stoneLight; M.box([0, 0.35, 0], [7.0, 0.7, 4.6], lhx('#E8E2D4'), 'tfkrl');
        const cols = []; for (let i = 0; i < 6; i++) cols.push([-2.75 + i * 1.1, 1.8]);
        parts.columns(M, cols, 0.7, 4.0, { r: 0.22, color: '#ECE6D8', base: '#D8D0C0', sides: 8 });
        M.color = lhx('#E4DCCC'); M.box([0, 2.4, -0.4], [5.2, 3.4, 2.6], null, 'fkrl');
        M.color = lhx('#E8E2D4'); M.box([0, 4.15, 0], [6.8, 0.36, 4.4], null, 'tbfkrl');
        M.push().rotate(0, 90, 0); parts.terracottaRoof(M, { style: 'gable', w: 4.3, d: 6.6, y: 4.33, over: 0.25, rise: 1.3, detail: 'low' }); M.pop();
        M.pop();
      } else if (st === 'mud') LPS.add('dome_hall', { seed: 1 }, lx, 1.0, lz, 0, S * 0.95);
      else if (!korea) LPS.add('stupa', { seed: 0 }, lx, 0.6, lz, 0, S * 0.8);
      inner((look && look.house) || 'flat');
      for (let k = 0; k < 6; k++) { const x = 1.6 + k * 2.6; if (gx !== null && Math.abs(x - gx) < 3.4) continue; flagsAt.push([x, wy + (Hm + 0.4) * S, WALL_Z - 0.6]); }
    }
    // 拒马与倚墙的云梯（各式城墙共用）
    const jx = gx === null ? [1.4, 6.8] : [gx - 3.6, gx + 3.6, gx + 8.6];
    for (const x of jx) { const z = WALL_Z + 1.75 + (rnd.nextDouble() - 0.5) * 0.3; M.push().translate(x, H(x, z), z).scale(S); lpJuma(M, 3.8); M.pop(); }
    {
      const lx = gx === null ? 9.5 : gx + 6.4, zb = WALL_Z + 1.25, zt = WALL_Z + 0.1, tp = (st === 'palisade' ? 2.5 : 3.0);
      M.color = PAL.wood;
      for (const s of [-0.24, 0.24]) M.segment([lx + s, H(lx, zb), zb], [lx + s, tp, zt], 0.04, 0.035, 5);
      M.color = PAL.woodLight;
      for (let i = 1; i < 8; i++) { const u = i / 8; M.segment([lx - 0.24, lerp(H(lx, zb), tp, u), lerp(zb, zt, u)], [lx + 0.24, lerp(H(lx, zb), tp, u), lerp(zb, zt, u)], 0.025, 0.025, 4); }
    }
    // 城头的势力旗（实例化）与本城的大旗
    for (const p of flagsAt) LPS.add('flag', { color: defHex, seed: 0 }, p[0], p[1], p[2], Math.PI / 2, S * 0.62);
    if (castle && gx !== null) for (const sx of [-1, 1]) LPS.add('banner', { color: defHex, seed: 1 }, gx + sx * (st === 'han' ? 3.9 : 3.2), H(gx, WALL_Z + 0.9), WALL_Z + 0.9, 0, S * 0.75);
    return { geo: M.isEmpty ? null : M.toGeometry(), objs, lamps };
  }
  // 时刻：opts.mood（名称或小时数）> opts.scene.hour > 战场正在用的时刻 > 按种子（午后 6 · 黄金时刻 3 · 黄昏 1）
  function clashHour(opts, seed) {
    const H = SG.LP.MOOD_HOURS || {};
    const v = opts.mood != null ? opts.mood : opts.scene && opts.scene.hour != null ? opts.scene.hour : null;
    if (typeof v === 'number' && isFinite(v)) return v;
    if (typeof v === 'string' && H[v] != null) return H[v];
    if (SG.BattleView && SG.Game && (SG.Game.battle || SG.Game.battleView) && SG.Gfx && SG.Gfx.moodState) { const s = SG.Gfx.moodState(); if (s && typeof s.hour === 'number') return s.hour; }
    const r = ((seed >>> 0) % 10);
    return r < 6 ? 16.0 : r < 9 ? 17.6 : 19.1;
  }
  // 天空与水面随本场的时刻（与 SG.Gfx 主场景的 _moodExtras 同一算法；材质为本场的副本 / 常驻的 LP 水面）
  function clashSkyWater(sc, st) {
    const G = SG.Gfx, noon = SG.LP.moodAt(12.5);
    const ratio = (a, b) => [0, 1, 2].map(k => (b[k] > 1e-4 ? a[k] / b[k] : 0));
    const rs = ratio(st.sunCol, noon.sunCol).map(v => v * st.sunI / noon.sunI), rk = ratio(st.sky, noon.sky).map(v => v * st.ambI / noon.ambI);
    const deep = st.deepK || 0, golden = st.golden || 0, sunDir = new THREE.Vector3(st.dir[0], st.dir[1], st.dir[2]).normalize();
    if (sc.sky && G._skyBase && sc.sky.material.uniforms && sc.sky.material.uniforms.uTop) {
      const u = sc.sky.material.uniforms, b = G._skyBase, fog = st.fog || [b.horizon.r, b.horizon.g, b.horizon.b];
      u.uTop.value.setRGB(b.top.r * rk[0], b.top.g * rk[1], b.top.b * rk[2]).lerp(C(0.05, 0.07, 0.17), deep);
      u.uHorizon.value.setRGB(fog[0], fog[1], fog[2]);
      u.uBottom.value.setRGB(b.bottom.r * rk[0], b.bottom.g * rk[1], b.bottom.b * rk[2]);
      u.uSunColor.value.setRGB(b.sun.r * rs[0], b.sun.g * rs[1], b.sun.b * rs[2]).lerp(C(1, 0.72, 0.42), golden * 0.6).multiplyScalar(1 - 0.85 * deep);
      if (u.uSunDir) u.uSunDir.value.copy(sunDir);
    }
    const W = MATS.waterLP, b = G._waterBase;
    if (W && b) {
      const u = W.uniforms;
      u.uSunColor.value.setRGB(b.sun.r * rs[0], b.sun.g * rs[1], b.sun.b * rs[2]);
      u.uAmbient.value.setRGB(b.amb.r * rk[0], b.amb.g * rk[1], b.amb.b * rk[2]);
      u.uSheenColor.value.setRGB(b.sheen.r * rs[0], b.sheen.g * rs[1], b.sheen.b * rs[2]).lerp(C(1, 0.78, 0.5), golden * 0.5);
      u.uSkyColor.value.copy(b.sky).lerp(C(...(st.fog || [b.sky.r, b.sky.g, b.sky.b])), 0.5 * Math.max(golden, deep));
      u.uDeep.value.copy(b.deep).multiplyScalar(1 - 0.55 * deep);
      u.uShallow.value.copy(b.shallow).multiplyScalar(1 - 0.55 * deep);
      u.uSunDir.value.copy(sunDir);
    }
  }
  // 水面（LP 版）：主场景水面材质的常驻副本，颜色 uniform 独立（随本场时刻），时间、波纹、雾与主场景共用
  function waterMaterialLP() {
    if (!MATS.waterLP) {
      const src = SG.Gfx.water();
      const mat = src.clone();
      for (const k of ['uTime', 'uRipple']) if (src.uniforms[k]) mat.uniforms[k] = src.uniforms[k];
      mat.uniforms.uAlpha = { value: 0.7 };
      mat.uniforms.uAmp = { value: 0.06 };
      MATS.waterLP = mat;
    }
    return MATS.waterLP;
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
    R = Math.max(R, Math.ceil(K / (fi === 1 ? 5 : 3)));
    R = Math.max(1, Math.min(R, K, 10));
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
      // 布景：防守城池的文化与当地群落、是否登陆（opts.scene；缺省按防守方文化）
      this.look = sceneLook(opts.scene, this.D.culture);
      if (this.kind === 'river') this.look.coast = false;
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
      this.figs = [];        // 单独的 SG.Figure（旗手、武将、战马），退出时释放
      this.temp = [];        // 特效网格 { mesh, t0, life, fn }
      // 第四版：有 SG.LP / SG.LPProps 时按 Lanternvale 风格布景（否则第三版）
      this.lp = lpOK();
      // 修订（LP）：城墙样式与战场棋盘的城池一致——倭为木栅，高句丽为石城（灰石墙、灰瓦的门楼与角楼）
      if (this.lp && this.look) { const cu = this.look.culture; if (cu === 'wa') this.look.wall = 'palisade'; else if (cu === 'korea') this.look.wall = 'korea'; }
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
        // 第四版：天空材质用本场的副本（颜色随本场的时刻；着色器程序与主场景共用）
        this.sky = new THREE.Mesh(Gfx.sky.geometry, this.lp ? Gfx.sky.material.clone() : Gfx.sky.material);
        if (this.lp) this.own.push(this.sky.material);
        this.sky.frustumCulled = false;
        this.sky.renderOrder = -100000;
        scene.add(this.sky);
      }
      // 第四版：本场的氛围（LP.Mood 驱动本场的太阳与天光：LP 材质得到 Lanternvale 的光照，原有材质按主场景的方式缩放）
      if (this.lp) {
        try {
          const main = Gfx.mood, L = main && main.legacy;
          this.mood = Gfx.newMood(scene, {
            sun, hemi: scene.children.find(o => o.isHemisphereLight), positionSun: true, center: [0, 0, ZC], extent: 15,
            baseDir: main && main.baseDir ? main.baseDir.slice() : null,
            legacy: L ? { sunColor: L.sunColor, sunIntensity: L.sunIntensity, skyColor: L.skyColor, groundColor: L.groundColor, hemiIntensity: L.hemiIntensity } : null,
            fog: { base: FOG.slice(), background: true }, fogMax: 0.85, shadowStrength: 0.7, maxLamps: 6,
          });
          this.hour = clashHour(this.opts, seed);
          this.mood.set(this.hour);
          // 修订：先备好常驻的 LP 水面材质，再按本场时刻着色（否则第一场河 / 海的攻击画面沿用主场景的水色，之后每场沿用上一场的）
          if (SG.Gfx.water) waterMaterialLP();
          clashSkyWater(this, this.mood.state);
        } catch (e) { console.error(e); this.mood = null; }
      }
      // 地形
      this.h = makeHeight(this.kind, seed, this.look);
      yield* buildTerrain(this, this.kind, SG.SeededRandom(seed ^ 0x5bd1e995), this.D.color);
      // 两军
      this.armies = [this.buildArmy(this.A, 0), this.buildArmy(this.D, 1)];
      this.planCharge();
      this.planArrows();
      yield;
      yield* this.buildFigures();
      // 第四版：士兵、旗手、武将与战马脚下的柔和圆影（全部一次绘制，pose 里摆放）
      if (this.lp) {
        let n = 4;
        for (const A of this.armies) n += A.soldiers.length;
        this.blobs = new SG.LP.BlobShadows(n);
        this.blobs.mesh.name = 'ClashShadows';
        scene.add(this.blobs.mesh);
      }
      this.planCasualties();
      this.planActs();
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
      // 第三版修订：兵更多（约多两排纵深），混战像人群而不是一条细线
      const n = info.before > 0 ? clamp(Math.round(info.before / 240) + 3, 6, 36) : 6;
      const nArch = clamp(Math.round(n * 0.2), 2, 6);
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
          type: 'spear', cid: 0, fig: null, flag: null, acts: null, colKey: '',
        };
        soldiers.push(s);
        return s;
      };
      for (const sl of slots) mk('inf', sl.f, sl.l, sl.file).type = infType(info.culture, sl.file);
      // 弓手：步兵之后一到两排
      const aRows = nArch > 4 ? 2 : 1, per = Math.ceil(nArch / aRows);
      let minAll = minF;
      for (let k = 0; k < nArch; k++) {
        const row = Math.floor(k / per), cnt = Math.min(per, nArch - row * per), i = k - row * per;
        const f = minF - 0.72 - row * 0.5, l = (i - (cnt - 1) / 2) * 0.82 + (row % 2) * 0.41;
        mk('arch', f, l, 99).type = 'archer';
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
        s.role = 'flag'; s.type = 'spear'; flags.push(s);
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

    // 士兵群（实例化）、旗手、武将与战马、帅旗、箭
    *buildFigures() {
      const scene = this.scene;
      // 第三轮修订：LP 版的士兵脚下已有柔和的圆影（ClashShadows），不再另投实时阴影（省掉每组一次阴影绘制）
      const crowd = this.crowd = SG.Figure.crowd({ lod: 'low', shadow: !this.lp, capacity: 16 });
      scene.add(crowd.root);
      for (const A of this.armies) {
        const info = A.info;
        A.hex = { base: info.color, flash: mixHex(info.color, '#ffffff', 0.55), dead: mixHex(info.color, '#000000', 0.28) };
        for (const s of A.soldiers) {
          if (s.role === 'flag') continue;
          s.cid = crowd.add({ type: s.type, culture: info.culture, color: info.color, seed: TYPE_SEED[s.type] });
          s.colKey = 'base';
          yield;
        }
        // 旗手：双手竖持长枪，旗面挂在枪头下
        const glyph = surname(info.gen);
        const big = bannerTexture(glyph, info.color, true, info.accent);
        const small = bannerTexture(glyph, info.color, false, info.accent);
        this.own.push(big, small);
        for (const s of A.soldiers) if (s.role === 'flag') {
          const f = s.fig = SG.Figure.soldier({ type: 'spear', culture: info.culture, color: info.color, seed: 2, lod: 'low' });
          f.root.scale.setScalar(FIG_S);
          scene.add(f.root);
          this.figs.push(f);
          const wi = f.weaponInfo || {};
          const top = (wi.tip ? wi.tip[1] : (wi.len || 2.3) - 0.3) - 0.16;
          s.flag = new Flag(small, 0.56 / FIG_S, 0.4 / FIG_S, A.dir > 0, this.rnd.nextDouble() * 6, false, A.si + 's');
          s.flag.mesh.matrix.makeRotationY(Math.PI).setPosition(0, top, 0);
          f.sockets.gripR.add(s.flag.mesh);
          this.own.push(s.flag);
          yield;
        }
        // 武将：中精度名将（专属兵器、与头像一致的面部）骑中精度战马；鞍后左侧插帅旗
        const G = A.general;
        G.horse = SG.Figure.horse(horseOpts(info, info.gen));
        G.fig = SG.Figure.general(info.gen, { lod: 'mid', culture: info.culture, color: info.color, mounted: true });
        G.horse.mount(G.fig);
        G.horse.root.scale.setScalar(FIG_S);
        scene.add(G.horse.root);
        this.figs.push(G.fig, G.horse);
        const pm = new SG.MeshBuilder();
        pm.lathe(V(0, 0, 0), [[0.035, 0], [0.028, 4.1]], 8, C(0.3, 0.2, 0.12));
        pm.lathe(V(0, 4.1, 0), [[0.05, 0], [0.06, 0.05], [0.0, 0.3]], 8, GOLD);
        pm.sphere(V(0, 4.08, 0), 0.06, GOLD, 1);
        G.pole = new THREE.Mesh(pm.toGeometry(), SG.Gfx.lowPoly());
        G.pole.castShadow = true;
        G.pole.position.set(0.3, -0.35, -0.52);
        G.horse.attach(G.pole, 'saddle');
        this.own.push(G.pole.geometry);
        G.flag = new Flag(big, 0.74 / FIG_S, 0.98 / FIG_S, A.dir > 0, A.si * 3.1, true, A.si + 'B');
        G.flag.mesh.matrix.makeRotationY(Math.PI).setPosition(0, 3.96, 0);
        G.pole.add(G.flag.mesh);
        this.own.push(G.flag);
        yield;
      }
      // 箭（特殊箭雨染成必杀技颜色并发光）
      const lp = SG.Gfx.lowPoly();
      const am = this.arrowMesh = new THREE.InstancedMesh(arrowGeo(), lp, Math.max(1, this.arrows.length));
      am.count = this.arrows.length;
      am.castShadow = true; am.receiveShadow = false; am.frustumCulled = false;
      am.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      for (let i = 0; i < Math.max(1, this.arrows.length); i++) { am.setMatrixAt(i, HIDE); am.setColorAt(i, C(1, 1, 1)); }
      if (this.special) {
        const sc = SG.Gfx.color(this.special.color);
        const glowC = C(0.6 + sc.r * 1.6, 0.6 + sc.g * 1.6, 0.6 + sc.b * 1.6);
        this.arrows.forEach((r, i) => { if (r.spec) am.setColorAt(i, glowC); });
      }
      scene.add(am);
      this.own.push(am.geometry);
      this.instanced.push(am);
    }
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
    // 混战动作表（前两排步兵）：横斩、反撩、重劈、格挡交替，受击时插入「受击」
    planActs() {
      const rnd = this.rnd;
      const CLD = { light1: 0.42, light2: 0.42, heavy: 0.9 };
      for (const A of this.armies)
        for (const s of A.soldiers) {
          if (s.role !== 'inf' || s.file > 1) continue;
          const acts = [];
          let tt = TL.contact - 0.08 + rnd.nextDouble() * 0.1 + s.file * 0.14;
          const end = Math.min(s.fallT, TL.melee1 + s.rd);
          while (tt < end) {
            const u = rnd.nextDouble();
            const clip = u < 0.36 ? 'light1' : u < 0.7 ? 'light2' : u < 0.86 ? 'heavy' : 'block';
            const sp = clip === 'heavy' ? 1.55 : clip === 'block' ? 1 : 1.12 + rnd.nextDouble() * 0.3;
            const d = clip === 'block' ? 0.22 + rnd.nextDouble() * 0.16 : CLD[clip] / sp;
            acts.push({ t: tt, clip, sp, d });
            tt += d + rnd.nextDouble() * 0.07;
          }
          for (const h of s.hits) acts.push({ t: h, clip: 'hit', sp: 1.4, d: 0.42 / 1.4 });
          acts.sort((a, b) => a.t - b.t);
          s.acts = acts;
        }
    }

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
      x += dir * jit * engage * (Math.sin(t * s.w1 + s.p1) * 0.09 + Math.sin(t * s.w2 + s.p2) * 0.04);
      z += jit * Math.sin(t * s.w3 + s.p3) * 0.06;
      if (A.routed) {
        const uf = seg(t, TL.melee1 + s.rd, TL.regroup1 + 0.25);
        x -= dir * 8 * Math.pow(uf, 1.4);
      } else if (s.role !== 'arch' && !A.cheer) {
        x -= dir * BACK * eio(seg(t, TL.melee1 + s.rd, TL.regroup1 - 0.04));
      }
      o.x = x; o.z = z;
      o.jit = jit;
    }
    // 步态：相位（腿摆动）、幅度、身体前倾
    // 动作：时刻 t 该士兵播放的片段与片段内时刻（纯函数；步频由位移换算）
    animOf(A, s, t, out) {
      out.clip = 'idle'; out.ct = t + s.p0;
      if (t >= s.fallT) {
        const u = t - s.fallT;
        if (s.fallKind === 'blast') { out.clip = 'knockdown'; out.ct = u * 1.4; }
        else { out.clip = 'die'; out.ct = Math.min(1.4, 0.12 + u * 1.75); }
        return out;
      }
      const m0 = s.ed, m1 = TL.march1 + s.ed * 0.5;
      if (t < m0) return out;
      if (t < m1) { out.clip = 'run'; out.ct = gaitT('run', ENTER_DX * eo(seg(t, m0, m1)), ENTER_DX, m1 - m0) + s.p0 * 0.1; return out; }
      if (A.routed && t >= TL.melee1 + s.rd) {
        const r0 = TL.melee1 + s.rd, r1 = TL.regroup1 + 0.25;
        out.clip = 'run'; out.ct = gaitT('run', 8 * Math.pow(seg(t, r0, r1), 1.4), 8, r1 - r0) + s.p0 * 0.1;
        return out;
      }
      if (s.role === 'arch') {
        const aim = TL.aim + s.ed * 0.2, rel = A.volleyT + s.shotDelay;
        if (t >= aim && t < rel) { out.clip = 'bowDraw'; out.ct = 0.8 * seg(t, aim, rel); }
        else if (t >= rel && t < rel + 0.5) { out.clip = 'bowShoot'; out.ct = t - rel; }
        else if (A.cheer && t > TL.melee1 + 0.15) { out.clip = 'cheer'; out.ct = t - TL.melee1 - 0.15 + s.p0 * 0.2; }
        return out;
      }
      const c0 = TL.charge0 + s.cd, c1 = TL.contact + s.cd * 0.35;
      if (t < c0) {
        // 敌方箭雨落下前后：前两排举盾（举兵器）护身
        const E = this.armies[1 - A.si];
        if (s.role === 'inf' && s.file <= 1 && t > E.volleyT + 0.12 && t < E.volleyT + TL.flight + 0.3) { out.clip = 'block'; out.ct = t - E.volleyT; }
        return out;
      }
      if (t < c1) {
        const D = A.chargeDX + s.push, u = seg(t, c0, c1);
        out.clip = 'run'; out.ct = gaitT('run', D * u * u, D, c1 - c0) + s.p0 * 0.1;
        return out;
      }
      if (t < TL.melee1 + s.rd) {
        if (s.acts) for (let i = s.acts.length - 1; i >= 0; i--) {
          const a = s.acts[i];
          if (t >= a.t) { if (t < a.t + a.d) { out.clip = a.clip; out.ct = (t - a.t) * a.sp; } break; }
        }
        return out;
      }
      if (A.cheer) { out.clip = 'cheer'; out.ct = t - TL.melee1 - s.rd + s.p0 * 0.2; return out; }
      const b0 = TL.melee1 + s.rd, b1 = TL.regroup1 - 0.04;
      if (s.role !== 'arch' && t < b1) { out.clip = 'walk'; out.ct = 10 - gaitT('walk', BACK * eio(seg(t, b0, b1)), BACK, b1 - b0); }
      return out;
    }
    // 武将与战马的动作
    // 骑手动作之间用 o.bclip / o.bt / o.bw（bw = 前一动作的残留权重）交叉淡化，不再在切换处跳帧
    genAnim(A, t, o) {
      const G = A.general;
      o.hclip = 'idle'; o.ht = t + G.p0; o.rclip = 'idle'; o.rt = t + G.p0; o.move = 0;
      o.bclip = null; o.bt = 0; o.bw = 0;
      const fadeFrom = (clip, ct, t0, dur) => { const w = 1 - sm(seg(t, t0, t0 + dur)); if (w > 0.001) { o.bclip = clip; o.bt = ct; o.bw = w; } };
      const m0 = G.ed, m1 = TL.march1 + 0.05;
      if (t >= m0 && t < m1) { o.hclip = 'trot'; o.ht = gaitT('trot', ENTER_DX * eo(seg(t, m0, m1)), ENTER_DX, m1 - m0) + G.p0; o.move = 0.8; }
      // 号令放箭：高举兵器（举起、放下都淡入淡出）
      const a0 = TL.aim - 0.06, a1 = A.volleyT + 0.3;
      if (t > a0 && t < a1) { o.rclip = 'charge'; o.rt = t - TL.aim; fadeFrom('idle', t + G.p0, a0, 0.2); }
      else if (t >= a1) fadeFrom('charge', a1 - TL.aim, a1, 0.22);
      const c0 = TL.charge0 + 0.1, c1 = TL.contact + 0.1;
      if (!A.routed) {
        if (t >= c0 && t < c1) { o.hclip = 'gallop'; o.ht = (t - c0) * 1.15; o.rclip = 'charge'; o.rt = t - c0; o.move = 1; o.bclip = null; o.bw = 0; fadeFrom('idle', t + G.p0, c0, 0.2); }
        // 接敌：武将在本阵后方督战，不对空挥砍——勒马人立，兵器自高举处向前一指号令全军，随后高举兵器激励士卒。
        // heavy 片段 0.42 秒处正是 charge 的举起姿势，从那里接着播到前指（约 0.58 秒）定住，无跳帧
        const p0 = TL.contact - 0.12, q0 = TL.contact + 0.5, e0 = TL.melee1 + 0.1;
        const ptT = Math.min(0.58, 0.42 + (t - p0) * 0.9);
        if (t >= p0 && t < e0) {
          o.rclip = 'heavy'; o.rt = ptT;
          if (t >= q0) { o.rclip = 'cheer'; o.rt = t - q0; fadeFrom('heavy', 0.58, q0, 0.35); }
        }
        if (t >= TL.contact && t < TL.contact + 1.0) { o.hclip = 'rear'; o.ht = (t - TL.contact) * 1.6; }
        if (t >= e0) {
          if (A.cheer) { o.rclip = 'victory'; o.rt = t - e0; fadeFrom('cheer', e0 - q0, e0, 0.3); }
          else {
            fadeFrom('cheer', e0 - q0, e0, 0.3);
            const b1 = TL.regroup1;
            if (t < b1) { o.hclip = 'walk'; o.ht = 10 - gaitT('hwalk', GBACK * eio(seg(t, e0, b1)), GBACK, b1 - e0); }
          }
        }
      } else {
        if (t >= c0 && t < c1) { o.hclip = 'trot'; o.ht = (t - c0) * 1.1; o.move = 0.6; }
        if (t >= TL.melee1 + 0.12) {
          const r0 = TL.melee1 + 0.12, r1 = TL.regroup1 + 0.3;
          o.hclip = 'gallop'; o.ht = gaitT('gallop', 9 * Math.pow(seg(t, r0, r1), 1.4), 9, r1 - r0); o.move = 1;
        }
      }
      return o;
    }
    groundY(x, z) {
      return this.h(x, z);
    }

    // 按时刻 t 摆出全部士兵、武将、箭（纯函数式：跳过时直接摆出结束姿态）
    // 按时刻 t 摆出全部士兵、旗手、武将、箭（纯函数式：跳过时直接摆出结束姿态）
    pose(t) {
      const o = this._po || (this._po = {}), an = this._pa || (this._pa = {});
      const cr = this.crowd;
      const bl = this.blobs;
      let nb = 0;
      cr.setTime(t);
      for (const A of this.armies) {
        const yaw0 = A.dir > 0 ? Math.PI / 2 : -Math.PI / 2;
        for (const s of A.soldiers) {
          const dead = t >= s.fallT;
          const tt = dead ? s.fallT : t;
          this.soldierXZ(A, s, tt, o);
          let x = o.x, lift = 0;
          const z = o.z;
          if (dead && s.fallKind === 'blast') {
            const u = seg(t, s.fallT, s.fallT + 0.5);
            x -= A.dir * s.kb * eo(u);       // 被震飞：朝本方后方
            lift = 4 * 1.1 * u * (1 - u);
          }
          const y = this.groundY(x, z) + lift;
          if (bl) bl.set(nb++, x, y - lift + 0.015, z, dead ? 0.3 : 0.22, dead ? 0.2 : 0.22, lift > 0.05 ? 0.42 * Math.max(0.2, 1 - lift) : 0.42);
          let yaw = yaw0;
          if (A.routed) yaw += Math.PI * sm(seg(tt, TL.melee1 + s.rd, TL.melee1 + s.rd + 0.2)) * (s.p0 > 3.1 ? 1 : -1);
          yaw += o.jit * Math.sin(tt * s.w4 + s.p4) * 0.22;
          this.animOf(A, s, t, an);
          // 颜色：受击闪亮、阵亡变暗
          let key = 'base';
          if (dead) key = t - s.fallT < 0.12 ? 'flash' : t - s.fallT > 0.5 ? 'dead' : 'base';
          else for (const ht of s.hits) { const d = t - ht; if (d >= 0 && d < 0.14) { key = 'flash'; break; } }
          if (s.fig) {
            const f = s.fig;
            f.root.position.set(x, y, z);
            f.root.rotation.set(0, yaw, 0);
            f.setPose(an.clip, an.ct, { ik: false });
            if (an.clip !== 'die' && an.clip !== 'knockdown') f.grip(FLAG_GRIP, FLAG_DIR, 0);
            if (key !== s.colKey) { s.colKey = key; f.setTint('#ffffff', key === 'flash' ? 0.5 : 0); }
            s.flag.k = dead ? 0.15 : an.clip === 'run' ? 1.5 : 0.85;
          } else {
            cr.setTransform(s.cid, x, y, z, yaw, FIG_S);
            cr.play(s.cid, an.clip, { t0: t - an.ct });
            if (key !== s.colKey) { s.colKey = key; cr.setColor(s.cid, A.hex[key]); }
          }
        }
        this.poseGeneral(A, t);
        if (bl) { const G = A.general; bl.set(nb++, G.x, G.y + 0.015, G.z, 0.62, 0.3, 0.46); }
      }
      if (bl) bl.commit(nb);
      this.poseArrows(t);
      if (this.arrowMesh) this.arrowMesh.instanceMatrix.needsUpdate = true;
    }
    poseGeneral(A, t) {
      const G = A.general;
      const x = this.generalX(A, t), z = G.z0, y = this.groundY(x, z);
      let yaw = A.dir > 0 ? Math.PI / 2 : -Math.PI / 2;
      if (A.routed) yaw += Math.PI * sm(seg(t, TL.melee1 + 0.12, TL.melee1 + 0.35));
      const ga = this.genAnim(A, t, this._ga || (this._ga = {}));
      G.horse.root.position.set(x, y, z);
      G.horse.root.rotation.set(0, yaw, 0);
      G.horse.setPose(ga.hclip, ga.ht);
      const bl = this._gbl || (this._gbl = { clip: null, t: 0, w: 1 });
      bl.clip = ga.bclip; bl.t = ga.bt; bl.w = 1 - ga.bw;
      G.fig.setPose(ga.rclip, ga.rt, { gallop: ga.hclip === 'gallop', blend: ga.bw > 0 ? bl : null });
      // 帅旗竖插在鞍后：战马人立（整匹马抬头约 42°）时旗杆只随之倾斜约四分之一，不像硬牌子一样向后倒、滑出画面
      const hb = G.horse.bones;
      G.pole.rotation.x = -(hb.root.rotation.x + hb.body.rotation.x) * 0.75;
      G.x = x; G.y = y; G.z = z;
      G.flag.k = 0.85 + ga.move * 0.7;
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
        if (u >= 1) _v1.addScaledVector(_v2, r.water ? 0.4 : -0.08);   // 插入地面约 0.17（露出约六成箭杆与箭羽）
        if (u >= 1 && r.water) { m.setMatrixAt(i, HIDE); return; }
        _m.compose(_v1, _q, _one);
        m.setMatrixAt(i, _m);
      });
    }
    aimArrow(r) {
      r.ready = true;
      const o = {};
      this.soldierXZ(r.S, r.archer, r.t0, o);
      r.p0.set(o.x + r.S.dir * 0.3, this.groundY(o.x, o.z) + 0.74, o.z);
      if (r.target) {
        this.soldierXZ(r.E, r.target, r.land, o);
        r.p1.set(o.x, this.groundY(o.x, o.z) + 0.6, o.z);
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
    // 斩光（第三版修订）：月牙形弧（外缘圆、内缘向两端收拢），径向 / 沿弧两端渐隐的柔边贴图，加色混合；
    // 弧面正对镜头（不再侧对镜头成一堵半透明的墙），沿弧撒火花
    slash(pos, col) {
      const cam = this.camera;
      const base = new THREE.Quaternion();
      {
        const o = new THREE.Object3D();
        o.position.copy(pos);
        if (cam) o.lookAt(cam.position);
        base.copy(o.quaternion);
      }
      const qz = new THREE.Quaternion(), zAxis = new THREE.Vector3(0, 0, 1);
      const mk = (r0, r1, c, op, wid) => {
        const m = fxMaterial('slash');
        m.color.copy(c); m.opacity = op;
        const mesh = new THREE.Mesh(slashGeo(r0, r1, wid), m);
        mesh.position.copy(pos);
        mesh.quaternion.copy(base).multiply(qz.setFromAxisAngle(zAxis, -0.35));
        mesh.renderOrder = 15;
        this.scene.add(mesh);
        return mesh;
      };
      const halo = mk(1.5, 3.3, col.clone(), 0.4, 1.0), outer = mk(2.1, 3.0, col.clone(), 0.9, 0.85), inner = mk(2.6, 2.9, C(1, 1, 0.95), 1.0, 0.7);
      for (const [mesh, k] of [[halo, 0.98], [outer, 1], [inner, 1.05]]) {
        this.temp.push({
          mesh, t0: this.t, life: 0.46, fn: (u) => {
            const sc = 0.55 + eo(u) * 0.75 * k;
            mesh.scale.set(sc, sc, 1);
            mesh.quaternion.copy(base).multiply(qz.setFromAxisAngle(zAxis, -0.35 - u * 0.9));
            mesh.material.opacity = (1 - u * u) * (k > 1 ? 1.0 : k < 1 ? 0.4 : 0.9);
          },
        });
      }
      // 沿弧的火花
      const right = new THREE.Vector3(1, 0, 0).applyQuaternion(base), up = new THREE.Vector3(0, 1, 0).applyQuaternion(base);
      for (let i = 0; i < 5; i++) {
        const a = -0.8 + i * 0.4 - 0.35, r = 2.4;
        this.sparks(pos.clone().addScaledVector(right, Math.cos(a) * r * 0.9).addScaledVector(up, Math.sin(a) * r * 0.9), 4, shade(col, 0.5));
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
      const dc = SG.Clash.debug && SG.Clash.debug.cam;
      if (dc) { this.camera.position.set(dc.p[0], dc.p[1], dc.p[2]); this.camera.lookAt(dc.l[0], dc.l[1], dc.l[2]); return; }
      // 注视点比两军略高一点：画面上方约 1/6 被 HUD 占去，让军阵落在 HUD 之下的画面中部
      const K = [
        { t: 0, p: [-1.1, 4.0, 18.4], l: [0.2, 2.05, -0.8] },
        { t: 0.7, p: [-0.4, 2.55, 15.0], l: [0, 2.1, -0.8] },
        { t: TL.charge0, p: [-0.3, 2.3, 14.0], l: [0, 1.88, -0.8] },
        // 第三版修订：接敌到混战时机位更低、更近（士兵占满画面中下部，不再是远处的一条细带）
        { t: TL.contact, p: [xc * 0.45 + 0.2, 1.5, 9.8], l: [xc * 0.5, 1.22, -0.8] },
        { t: TL.melee1, p: [xc * 0.45 + 0.5, 1.42, 9.4], l: [xc * 0.5 + 0.2, 1.16, -0.8] },
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
      // 竖屏：拉远之后再抬高机位俯视，压低天空、让纵深铺开
      const kP = clamp((1.15 - aspect) / 0.6, 0, 1);
      // 竖屏（画面很窄）：冲锋到混战期间镜头推向接敌线，只保证前两排在画面内（两端的弓手、武将与帅旗可出画，
      // 头像与姓名在上方 HUD 里），士兵不再只是一条细带
      const wP0 = kP * sm(trap(t, TL.charge0 + 0.35, TL.regroup1 + 0.35, 0.7));
      // 横屏同样在混战时推近：只保证前几排在画面内（两端的武将与帅旗可出画，头像在 HUD 里）
      const wP = Math.max(wP0, (1 - kP) * 0.62 * sm(trap(t, TL.charge0 + 0.3, TL.regroup1 + 0.1, 0.75)));
      if (wP > 0) lx = lerp(lx, this.xc || 0, wP * 0.8);
      // 推近不得把两军（尤其两端的武将与帅旗）推出画面：按实际军阵外缘求最小拉远倍数（左右各留约 4%）
      const tanH = Math.tan(cam.fov * M.deg2rad / 2) * aspect * 0.92;
      const need = this.viewNeed(t, lx, lz, dz, tanH, wP);
      const fit = Math.max(lerp(this.fit || 1, 1, wP), need);
      const dist = Math.hypot(dx, dy, dz) * fit;
      cam.position.set(lx + dx * fit, ly + dy * fit + kP * dist * 0.27 * (1 - 0.5 * wP0), lz + dz * fit);
      if (this.shake > 0.001 && !this.calm) {
        const k = this.shake * 0.11;
        cam.position.x += Math.sin(this.real * 71) * k;
        cam.position.y += Math.sin(this.real * 57 + 1.3) * k * 0.7;
      }
      // 竖屏推近时注视点降到士兵胸口附近（远景时高处的注视点到了近处会把军阵压到画面底部）
      let lookY = ly - kP * 0.35 - wP0 * 0.95;
      cam.lookAt(lx, lookY, lz);
      // 城门 / 本城：城门楼屋脊不得顶进上方 HUD（宽屏、手机上会被遮成「无顶红楼」）——
      // 投影屋脊，若高于 HUD 下沿就抬高注视点（画面整体下移，脚下空地有余量），最多抬 1.6
      // 竖屏推近混战时不做（近景里城楼本就出画，不再是「无顶红楼」）
      if ((this.kind === 'gate' || this.kind === 'castle') && wP < 0.999) {
        const hb = this.hudLineNdc(), upMax = 1.6 * (1 - wP);
        if (hb < 0.98) {
          const tanV = Math.tan(cam.fov * M.deg2rad / 2);
          const dl = Math.hypot(cam.position.x - lx, cam.position.y - lookY, cam.position.z - lz);
          let up = 0;
          for (let it = 0; it < 4; it++) {
            cam.updateMatrixWorld();
            const y = _v2.set(GATE_X, 6.2, WALL_Z - 0.7).project(cam).y;
            const over = y - (hb - 0.03);
            if (over <= 0.004 || up >= upMax) break;
            up = Math.min(upMax, up + over * tanV * dl * 0.9);
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
      else if (!A.cheer) x -= dir * GBACK * eio(seg(t, TL.melee1 + 0.1, TL.regroup1));
      return x;
    }
    // 取景用的时刻：入场时按列阵位置（让队伍走进画面），溃逃时停在溃散前（不追着逃兵拉远）
    frameT(A, t) { return clamp(t, TL.march1, A.routed ? TL.melee1 + 0.12 : TL.end); }
    armyExtent(t) {
      const [L, R] = this.armies;
      const gl = this.generalX(L, this.frameT(L, t)), gr = this.generalX(R, this.frameT(R, t));
      return { l: gl - 1.75, r: gr + 1.75, gl, gr };
    }
    // wCore > 0（竖屏混战）：在「全军」与「前两排」两种取景需求之间插值
    viewNeed(t, cx, lz, dz, tanH, wCore) {
      if (!(dz > 0.1) || !(tanH > 0.01)) return 1;
      let need = 0, core = 0;
      const o = this._fo || (this._fo = {});
      const nOf = (x, z) => (Math.abs(x - cx) / tanH + (z - lz)) / dz;
      const test = (x, z) => { const n = nOf(x, z); if (n > need) need = n; };
      for (const A of this.armies) {
        const tt = this.frameT(A, t), gx = this.generalX(A, tt), gz = A.general.z0;
        test(gx - A.dir * 1.75, gz - 0.25);     // 帅旗（含旗面飘动）在武将身后
        test(gx + A.dir * 1.0, gz);             // 马头
        for (const s of A.soldiers) {
          this.soldierXZ(A, s, tt, o);
          const n = nOf(o.x - A.dir * 0.3, o.z + 0.25);
          if (n > need) need = n;
          if (s.file <= 1 && s.role !== 'arch' && n > core) core = n;
        }
      }
      return wCore > 0 ? lerp(need, core * 1.05, wCore) : need;
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
      SHARED_U.time.value = this.real;
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
    // 测试用：模型与预算统计
    stats() {
      const c = this.crowd ? this.crowd.stats() : null;
      let figTris = 0;
      for (const f of this.figs) figTris += f.triangles || 0;
      return { crowd: c, figs: this.figs.length, figTris, groundTris: this.groundTris || 0, buildMs: this._buildMs, prepMs: this._prepMs };
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
      // 黑幕下准备超过约 0.3 秒（首次遇到的文化、慢设备）时显示「布阵中」，不让玩家对着纯黑画面干等
      const waitTimer = setTimeout(() => { if (this.hud) this.hud.wait(true); }, 300);
      try {
        // 人物模型库：未载入时就近载入；首次遇到的文化在黑幕下分片生成士兵模板与动作贴图（之后常驻）
        if (!(await ensureFigure())) throw new Error('SG.Figure 不可用（js/figure.js 未载入）');
        const tp = performance.now();
        if (prepChain) await prepChain;      // 正在进行的预热（prepare）先做完，免得同一模板并行生成两遍
        await SG.Figure.preload({ cultures: [this.A.culture, this.D.culture], types: [], crowd: crowdSpecs(uniq([this.A.culture, this.D.culture])), budget: 24 });
        this._prepMs = performance.now() - tp;
        await this.buildAsync(40);     // 黑幕下画面不可见：每片多做一些（约 40ms），少等几帧（宿主仍在每帧绘制底下的战场）
      } finally {
        clearTimeout(waitTimer);
      }
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
      if (this.crowd) { this.crowd.dispose(); this.crowd = null; }
      for (const f of this.figs) { try { f.dispose(); } catch (e) { console.error(e); } }
      this.figs.length = 0;
      for (const p of [this.dust, this.fx, this.glow]) if (p) p.dispose();
      for (const o of this.own) if (o && typeof o.dispose === 'function') o.dispose();
      this.own.length = 0;
      // 第四版：实例化景物（几何体为 SG.LPProps 的缓存，不释放）、圆影、本场的氛围（钩子、灯池、光晕）
      if (this.lpSet) { this.lpSet.sc.dispose(); this.lpSet = null; }
      if (this.blobs) { this.blobs.dispose(); this.blobs = null; }
      if (this.mood) { try { this.mood.dispose(); } catch (e) { console.error(e); } this.mood = null; }
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
.sg-clash-wait{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);display:flex;align-items:center;gap:.7em;
  color:#f3c969;font-family:${KAI};font-size:max(15px,calc(var(--u)*1.5));letter-spacing:.3em;white-space:nowrap;opacity:0;transition:opacity .25s ease;}
.sg-clash-wait i{width:1.1em;height:1.1em;border-radius:50%;border:2px solid rgba(243,201,105,.25);border-top-color:#f3c969;animation:sg-clash-spin .9s linear infinite;}
.sg-clash.is-wait .sg-clash-wait{opacity:.92;}
@keyframes sg-clash-spin{to{transform:rotate(360deg);}}
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
      el('div', 'sg-clash-wait', '<i></i><span>布阵中…</span>', this.fadeEl);
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
    // 黑幕上的「布阵中」提示
    wait(on) { this.root.classList.toggle('is-wait', !!on); }
    fadeIn(sec) {
      this.wait(false);
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
  // 第三版：玩家要求「战斗时默认开启动画」——换用新键，旧键里存下的「关」作废一次（DESIGN-V3 §2）
  const MODE_KEY = 'sanguozhi2_clash_v3';
  function loadMode() {
    try {
      const v = window.localStorage && localStorage.getItem(MODE_KEY);
      if (v === 'on' || v === 'fast' || v === 'off') return v;
      // 旧键只沿用「快」（仍是开启动画）；旧的「关」不再生效
      if (window.localStorage && localStorage.getItem('sanguozhi2_clash') === 'fast') return 'fast';
    } catch (e) { /* 忽略 */ }
    return 'on';
  }

  // 由战场（js/battle-terrain.js 的 model.terrain）取布景：防守城池的文化、交战处的群落、是否在海岸登陆
  function sceneOfBattle(model, a, t) {
    const T = model && model.terrain;
    const out = { culture: (T && T.culture) || (t && t.gen && t.gen.culture) || 'han', biome: T && T.biome, coast: false };
    // 第四版：城门楼牌匾的城名、战场当前的时刻（攻击画面与战场同一时刻）
    try { const tg = model && model.setup && model.setup.target; if (tg && tg.name) out.city = String(tg.name); } catch (e) { /* 无 */ }
    try { const st = SG.Gfx && SG.Gfx.moodState ? SG.Gfx.moodState() : null; if (st && typeof st.hour === 'number') out.hour = st.hour; } catch (e) { /* 无 */ }
    try {
      const G = T && T.grid;
      if (G && G.biome && G.water) {
        const idx = (x, y) => { const i = Math.round((x - G.u0) / G.step), j = Math.round((y - G.v0) / G.step); return i >= 0 && j >= 0 && i < G.nu && j < G.nv ? j * G.nu + i : -1; };
        const o = idx(t.x, t.y);
        if (o >= 0 && BIOME_NAMES[G.biome[o]]) out.biome = BIOME_NAMES[G.biome[o]];
        // 攻方身后 3 格内有海（或者是登陆战且攻方站在浅滩 / 水边）→ 海岸布景
        const sea = (x, y) => { const k = idx(x, y); return k >= 0 && G.water[k] === 3; };
        let n = 0;
        for (let dx = -3; dx <= 3; dx++) for (let dy = -3; dy <= 3; dy++) if (dx * dx + dy * dy <= 9 && sea(a.x + dx, a.y + dy)) n++;
        const W8 = SG.BattleTerrain ? SG.BattleTerrain.Water : 8;
        const wet = model.map && model.map[a.x] && (model.map[a.x][a.y] === W8 || (SG.Terrain && model.map[a.x][a.y] === SG.Terrain.River));
        out.coast = n >= 3 || (!!(T.notes && T.notes.indexOf('landing') >= 0) && !!wet);
      }
      // 干旱文化的城池落在世界地图的「温带」（河谷绿洲）时，攻击画面取半干旱的稀树草原（枯草、金合欢、椰枣），不是中原的青草地
      const dflt = cultPick(BIOME_OF, out.culture, 'temperate');
      if (out.biome === 'temperate' && (dflt === 'desert' || dflt === 'savanna')) out.biome = 'savanna';
    } catch (e) { /* 旧战场：只按文化 */ }
    return out;
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
    // 预热（可选）：战斗开始时传入双方文化，提前在后台分片生成士兵模板与动作贴图，首次攻击画面不再等待
    prepare(o) {
      o = o || {};
      // 依次排队：多次调用不会并行生成同一模板；play 播放前也会先等排队中的预热做完
      const p = (prepChain || Promise.resolve()).then(() => ensureFigure()).then(ok => {
        if (!ok || !SG.Figure) return false;
        const cs = uniq((o.cultures || []).filter(Boolean));
        return SG.Figure.preload({ cultures: cs.length ? cs : ['han'], types: [], crowd: crowdSpecs(cs.length ? cs : ['han']), budget: o.budget || 8 }).then(() => true);
      }).catch(() => false);
      prepChain = p;
      p.then(() => { if (prepChain === p) prepChain = null; });
      return p;
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
        scene: sceneOfBattle(model, a, t),
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
  // 游戏内兜底预热：宿主即使没有调用 prepare，游戏启动完成（标题画面）后也在后台分片生成最常见的汉军模板与动作贴图，
  // 首次「攻击」不必在黑幕下等待；动画设为「关」时不做。只在正式游戏页（SG.Game 已启动）进行，测试页不受影响
  if (typeof window !== 'undefined' && typeof document !== 'undefined' && typeof setTimeout === 'function') {
    let tries = 0;
    const warm = () => {
      if (Clash._warmed || ++tries > 40) return;
      const G = SG.Game;
      if (!(G && G._booted && G.strategy && SG.Gfx && SG.Gfx.renderer) || document.hidden) { setTimeout(warm, 1500); return; }
      Clash._warmed = true;
      if (Clash.enabled) Clash.prepare({ cultures: ['han'], budget: 6 });
    };
    setTimeout(warm, 2500);
  }
})();
