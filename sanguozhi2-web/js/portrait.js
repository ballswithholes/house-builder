'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 武将头像（DESIGN-V2 §4B）

   原作（FC《霸王的大陆》）头像的现代化：深色背景、四分之三侧面胸像、粗轮廓、
   有限色板 → 代码合成的高分辨率 SVG，赛璐璐分层阴影、轮廓光、发须与甲胄纹样，
   统一的势力色边框。全部在代码里生成，不载入任何外部资源。

   公开接口
   --------------------------------------------------------------------------
   SG.Portrait.url(gen, opts)     → 图片 URL（同步）。已栅格化时为 PNG dataURL；否则当场生成
                                     SVG dataURL（矢量，同样清晰，约 2ms），并在后台排队栅格化。
                                     按 (外貌, 像素尺寸, 颜色, 边框, 镜像, 表情) 缓存。
   SG.Portrait.el(gen, opts)      → <div class="sg-portrait"><img></div>，opts.size 为 CSS 像素；
                                     未缓存时先显示 SVG，栅格化完成后自动换成 PNG。
                                     opts.className 追加类名（.is-dead 灰度）。
   SG.Portrait.cached(gen, opts)  → 已有 URL 时返回，否则 null（不触发生成）
   SG.Portrait.preload(gens, opts)→ Promise，分片生成 + 栅格化（单片预算 10ms），不阻塞主线程。
                                     进入画面前对要显示的武将调用一次，之后 url/el 都是缓存命中。
   SG.Portrait.spec(gen)          → 解析后的外貌参数（调试用；格式见 portrait-data.js 文件头）
   SG.Portrait.svg(gen, opts)     → SVG 源码字符串（每次重新生成）
   SG.Portrait.canvas(gen, opts)  → Promise<HTMLCanvasElement>（给 WebGL 纹理等使用）
   SG.Portrait.clearCache(name?)  → 清除缓存（PortraitData.add 时自动调用）
   SG.Portrait.find(name)         → 按姓名找武将（SG.G 优先，其次剧本）；找不到为 null
   SG.Portrait.html(gen, opts)    → 内嵌 HTML 片段 <span class="sg-portrait sg-portrait-inline"><img></span>
                                     （UI.choose 的 label 等 HTML 字符串用；opts.size 缺省 32）
   SG.Portrait.preloadState(state = SG.G, sizes = [44, 96]) → Promise，预生成全部在世武将
   SG.Portrait.cultures           → 已实现的文化代号列表（DESIGN-V2 §6 全部 15 种）
   SG.Portrait.stats              → { built, rastered, maxSliceMs, totalMs, genMs, encMs } 性能统计

   gen 至少含 { name, war, intel, pol }；可选 { culture, born, sex, faction, color, portrait }。
   也可以直接传姓名字符串（会到 SG.G 或 ScenarioData 里查能力值）。
   gen.portrait 可直接给一份外貌参数（PortraitData 没有该人条目时使用）。
   opts：{ size = 96, scale = devicePixelRatio(≤2), color = 势力色或灰（'#hex' / THREE.Color / {r,g,b}），
           frame = true, flip = false, mood = 'neutral' | 'angry' | 'hurt' | 'win', raster = true }

   渲染管线：外貌参数 → SVG 字符串（同一份标记既可直接作 SVG 显示）→ 自带的 SVG 子集解释器
   用 Path2D 画到 CPU 画布（背景与边框按颜色缓存）→ PNG。比让浏览器逐张解析 SVG 快数倍。

   画面约定：视窗 256×256 单位；脸朝画面左侧（四分之三侧面），主光来自左上前方，
   画面右侧为背光面，右缘有轮廓光。flip = true 时人物水平镜像（边框不变）。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});

  // =============================================================== 工具 ==
  function hashStr(s) {
    let h = 2166136261 >>> 0;
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
    h ^= h >>> 16; h = Math.imul(h, 0x85ebca6b) >>> 0; h ^= h >>> 13; h = Math.imul(h, 0xc2b2ae35) >>> 0; h ^= h >>> 16;
    return h >>> 0;
  }
  // 确定性随机数（mulberry32）。每个“方面”用独立子流，互不干扰
  function Rng(seed) {
    let a = seed >>> 0;
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
      chance(p) { return f() < p; },
      pick(arr) { return arr[Math.floor(f() * arr.length)]; },
      wpick(list) {
        let tot = 0;
        for (const e of list) tot += e[1];
        let x = f() * tot;
        for (const e of list) { x -= e[1]; if (x < 0) return e[0]; }
        return list[list.length - 1][0];
      },
      // 近似正态 [-1, 1]
      norm() { return (f() + f() + f()) / 1.5 - 1; },
    };
  }
  const sub = (name, tag) => Rng(hashStr(name + '\u0001' + tag));
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const hyp = (x, y) => Math.sqrt(x * x + y * y); // 比 Math.hypot 快
  const smooth01 = t => { t = clamp(t, 0, 1); return t * t * (3 - 2 * t); };

  // ----------------------------------------------------------- 颜色 --
  const rgbCache = new Map();
  function rgb(c) {
    if (Array.isArray(c)) return c;
    let v = rgbCache.get(c);
    if (v) return v;
    let h = String(c).trim();
    if (h[0] === '#') h = h.slice(1);
    if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
    const n = parseInt(h.slice(0, 6), 16) || 0;
    v = [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    rgbCache.set(c, v);
    return v;
  }
  const c8 = v => (v < 0 ? 0 : v > 255 ? 255 : Math.round(v));
  function hex(a) { return '#' + ((1 << 24) | (c8(a[0]) << 16) | (c8(a[1]) << 8) | c8(a[2])).toString(16).slice(1); }
  function mix(a, b, t) {
    const x = rgb(a), y = rgb(b);
    return hex([x[0] + (y[0] - x[0]) * t, x[1] + (y[1] - x[1]) * t, x[2] + (y[2] - x[2]) * t]);
  }
  function mul(a, k) { const x = rgb(a); return hex([x[0] * k, x[1] * k, x[2] * k]); }
  function lum(c) { const x = rgb(c); return (0.299 * x[0] + 0.587 * x[1] + 0.114 * x[2]) / 255; }
  // 任意输入（'#hex'、'rgb()'、THREE.Color、{r,g,b}）→ '#rrggbb'
  function normColor(c, fallback) {
    if (!c) return fallback;
    if (typeof c === 'string') {
      if (c[0] === '#') return hex(rgb(c));
      const m = c.match(/rgba?\(([^)]+)\)/);
      if (m) { const p = m[1].split(',').map(parseFloat); return hex([p[0], p[1], p[2]]); }
      return fallback;
    }
    if (typeof c === 'object' && 'r' in c) {
      const k = (c.r <= 1 && c.g <= 1 && c.b <= 1) ? 255 : 1;
      return hex([c.r * k, c.g * k, c.b * k]);
    }
    return fallback;
  }
  // 材质色阶：b 基色 s 阴影 d 暗部 h 高光
  function skinT(b) {
    return {
      b, s: mix(mul(b, 0.83), '#9a3a5a', 0.16), d: mix(mul(b, 0.62), '#4a1830', 0.24),
      h: mix(b, '#fff4e6', 0.45), blush: mix(b, '#e2525e', 0.42), line: mix(mul(b, 0.42), '#2a0c18', 0.45),
      lip: mix(mul(b, 0.86), '#b04454', 0.22),
    };
  }
  function clothT(b) {
    return { b, s: mix(mul(b, 0.68), '#221a40', 0.2), d: mix(mul(b, 0.44), '#0c0818', 0.25), h: mix(b, '#fffaf0', 0.3) };
  }
  function metalT(b) { return { b, s: mix(mul(b, 0.6), '#1c2030', 0.15), d: mul(b, 0.36), h: mix(b, '#ffffff', 0.62) }; }
  function hairT(b) {
    const l = lum(b);
    return {
      b, s: mul(b, 0.6), d: mul(mix(b, '#000', 0.25), 0.5),
      h: l < 0.18 ? mix(b, '#8696b8', 0.34) : mix(b, '#fff6e0', 0.38),
    };
  }
  const INK = '#150e12';
  const GOLD = '#e2b45a';

  // ----------------------------------------------------------- 路径 --
  // 数字 → 一位小数的字符串（手写整数拼接，比浮点数转字符串快得多）
  function Nn(n) { const i = (n / 10) | 0, f = n - i * 10; return f ? i + '.' + f : '' + i; }
  const N = v => { const n = Math.round(v * 10); return n < 0 ? '-' + Nn(-n) : Nn(n); };
  const ps = p => N(p[0]) + ',' + N(p[1]);
  // Catmull-Rom → 三次贝塞尔。点可带第三个分量 k（0 = 尖角，1 = 圆滑）
  function spline(pts, closed) {
    const n = pts.length;
    if (n < 2) return '';
    let d = 'M' + ps(pts[0]);
    if (n === 2) return d + 'L' + ps(pts[1]) + (closed ? 'Z' : '');
    const segs = closed ? n : n - 1;
    for (let i = 0; i < segs; i++) {
      const p1 = pts[i], p2 = pts[(i + 1) % n];
      const p0 = closed ? pts[(i - 1 + n) % n] : pts[Math.max(0, i - 1)];
      const p3 = closed ? pts[(i + 2) % n] : pts[Math.min(n - 1, i + 2)];
      const k1 = (p1[2] === undefined ? 1 : p1[2]) / 6, k2 = (p2[2] === undefined ? 1 : p2[2]) / 6;
      // 控制柄长度不超过本段长度的一半，避免短段旁的长切线造成打圈
      const seg = hyp(p2[0] - p1[0], p2[1] - p1[1]) * 0.5;
      let ax = (p2[0] - p0[0]) * k1, ay = (p2[1] - p0[1]) * k1, bx = (p3[0] - p1[0]) * k2, by = (p3[1] - p1[1]) * k2;
      const la = hyp(ax, ay), lb = hyp(bx, by);
      if (la > seg) { ax *= seg / la; ay *= seg / la; }
      if (lb > seg) { bx *= seg / lb; by *= seg / lb; }
      d += 'C' + ps([p1[0] + ax, p1[1] + ay]) + ' ' + ps([p2[0] - bx, p2[1] - by]) + ' ' + ps(p2);
    }
    return closed ? d + 'Z' : d;
  }
  function poly(pts, closed) { return 'M' + pts.map(ps).join('L') + (closed === false ? '' : 'Z'); }
  function shift(pts, dx, dy) { return pts.map(p => [p[0] + dx, p[1] + dy, p[2]]); }
  function lp(a, b, t) { return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]; }
  function add(a, dx, dy) { return [a[0] + dx, a[1] + dy]; }
  // 变宽笔触：中心线 pts，宽度数组或函数 w(t)。返回闭合路径
  function taper(pts, ws) {
    const n = pts.length, L = [], Rr = [];
    for (let i = 0; i < n; i++) {
      const a = pts[Math.max(0, i - 1)], b = pts[Math.min(n - 1, i + 1)];
      let dx = b[0] - a[0], dy = b[1] - a[1];
      const l = hyp(dx, dy) || 1; dx /= l; dy /= l;
      const w = (typeof ws === 'function' ? ws(n === 1 ? 0 : i / (n - 1)) : typeof ws === 'number' ? ws : ws[i]) / 2;
      L.push([pts[i][0] - dy * w, pts[i][1] + dx * w]);
      Rr.push([pts[i][0] + dy * w, pts[i][1] - dx * w]);
    }
    const w0 = typeof ws === 'function' ? ws(0) : typeof ws === 'number' ? ws : ws[0];
    const w1 = typeof ws === 'function' ? ws(1) : typeof ws === 'number' ? ws : ws[n - 1];
    if (w1 < 0.08) { Rr.pop(); L[n - 1][2] = 0.4; }
    Rr.reverse();
    if (w0 < 0.08) { Rr.pop(); L[0][2] = 0.4; }
    return spline(L.concat(Rr), true);
  }
  // 叶形发绺 / 须绺：根 a、尖 b、根宽 w、弯曲 bend（相对长度的侧偏）
  function leaf(a, b, w, bend) {
    const dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy) || 1;
    const nx = -dy / l, ny = dx / l;
    const c = [(a[0] + b[0]) / 2 + nx * bend * l, (a[1] + b[1]) / 2 + ny * bend * l];
    const h = w / 2;
    return 'M' + ps([a[0] + nx * h, a[1] + ny * h]) + 'Q' + ps([c[0] + nx * h * 0.7, c[1] + ny * h * 0.7]) + ' ' + ps(b) +
      'Q' + ps([c[0] - nx * h * 0.7, c[1] - ny * h * 0.7]) + ' ' + ps([a[0] - nx * h, a[1] - ny * h]) + 'Z';
  }
  // 弯曲的发绺 / 须绺（毛笔笔触）：根宽 w，尖端收细，bend 为侧弯
  function lock(a, b, w, bend) {
    const dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy) || 1;
    const nx = -dy / l, ny = dx / l;
    const m1 = [a[0] + dx * 0.4 + nx * bend * l, a[1] + dy * 0.4 + ny * bend * l];
    const m2 = [a[0] + dx * 0.75 + nx * bend * l * 0.6, a[1] + dy * 0.75 + ny * bend * l * 0.6];
    return taper([a, m1, m2, b], t => w * Math.pow(1 - t, 0.8));
  }
  // 椭圆弧上的点（数学角度，y 向上为正）
  function arcPts(cx, cy, rx, ry, a0, a1, n, k) {
    const out = [];
    for (let i = 0; i <= n; i++) {
      const a = a0 + (a1 - a0) * i / n;
      out.push([cx + rx * Math.cos(a), cy - ry * Math.sin(a), k]);
    }
    return out;
  }
  function ellipse(cx, cy, rx, ry, rot) {
    return `<ellipse cx="${N(cx)}" cy="${N(cy)}" rx="${N(rx)}" ry="${N(ry)}"` + (rot ? ` transform="rotate(${N(rot)} ${N(cx)} ${N(cy)})"` : '');
  }
  function circle(cx, cy, r) { return `<circle cx="${N(cx)}" cy="${N(cy)}" r="${N(r)}"`; }

  // ======================================================== SVG 拼装器 ==
  let uidSeq = 0;
  const LAYERS = ['back', 'collarBack', 'neck', 'body', 'body2', 'head', 'ear', 'face', 'beard', 'hair', 'hat', 'front', 'top'];
  class Svg {
    constructor(o) {
      this.o = o;
      this.uid = 'q' + (uidSeq++).toString(36);
      this.defs = [];
      this.L = {};
      for (const k of LAYERS) this.L[k] = [];
      this.headShade = [];
      this.lw = o.lw; // 主轮廓线宽
    }
    id(n) { return this.uid + n; }
    clip(n, d) { this.defs.push(`<clipPath id="${this.id(n)}"><path d="${d}"/></clipPath>`); return `url(#${this.id(n)})`; }
    add(layer, s) { this.L[layer].push(s); }
    // 填充 + 可选描边
    path(layer, d, fill, sw, extra) {
      this.L[layer].push(`<path d="${d}" fill="${fill}"` + (sw ? ` stroke="${INK}" stroke-width="${N(sw)}"` : '') + (extra || '') + '/>');
    }
    line(layer, d, color, sw, extra) {
      this.L[layer].push(`<path d="${d}" fill="none" stroke="${color}" stroke-width="${N(sw)}"` + (extra || '') + '/>');
    }
    // 带阴影的形状：填充 → 裁剪内阴影 → 轮廓
    shape(layer, d, fill, shadeFn, sw, clipName, fillOp) {
      this.L[layer].push(`<path d="${d}" fill="${fill}"` + (fillOp ? ` opacity="${fillOp}"` : '') + '/>');
      if (shadeFn) {
        const cid = clipName || ('c' + this.defs.length);
        this.L[layer].push(`<g clip-path="${this.clip(cid, d)}">`);
        shadeFn(layer);
        this.L[layer].push('</g>');
      }
      if (sw !== 0) this.L[layer].push(`<path d="${d}" fill="none" stroke="${INK}" stroke-width="${N(sw || this.lw)}"/>`);
    }
  }

  // ======================================================== 文化外观 ==
  // 每种文化：肤色范围、发色、眼色、眼睑（lid：0 深双眼皮 … 1 单眼皮）、鼻梁高度、
  // 发型 / 冠帽 / 衣甲 的加权候选（按角色）、胡须候选、配色、标记与配饰的概率。
  // 角色（role）：warrior 武将 · general 智勇兼备 · strategist 谋士 · official 文官 ·
  //               ruler 君主 · commoner 小吏 / 草莽 · female 女性
  const DARK = [['#16131a', 6], ['#221a18', 3], ['#2c2019', 1]];
  const BROWNEYE = ['#4a2c1a', '#56351e', '#3c2414', '#4e3420'];
  const LIGHTEYE = ['#3a5a7a', '#4a6a8a', '#5a7a5a', '#6a6a5a', '#4a3a2a', '#3a2a1a'];
  const CULT = {
    han: {
      name: '汉', skins: ['#f0caa6', '#eac09a', '#e2b48c', '#d8a77f', '#cb9a72'], warSkin: 0.25,
      hairC: DARK, eyeC: BROWNEYE, lid: 0.55, noseBridge: 0.45, noseW: 1.0,
      hair: { m: [['topknot', 1]], f: [['bun', 3], ['twinbun', 1]] },
      hat: {
        warrior: [['helmet', 5], ['ze', 3], ['headband', 1], ['turban', 1], ['wuguan', 0.6]],
        general: [['helmet', 4], ['ze', 2], ['wuguan', 1], ['crown', 1]],
        strategist: [['jinxian', 3], ['lunjin', 1], ['ze', 2], ['futou', 2]],
        official: [['jinxian', 6], ['ze', 2], ['futou', 1]],
        ruler: [['crown', 4], ['jinxian', 2], ['helmet', 2]],
        commoner: [['turban', 3], ['ze', 3], ['headband', 2], ['futou', 1], ['none', 1]],
        female: [['none', 1]],
      },
      outfit: {
        warrior: [['lamellar', 5], ['plate', 2], ['robearmor', 1]], general: [['plate', 3], ['robearmor', 2], ['lamellar', 2]],
        strategist: [['robe', 1]], official: [['robe', 1]], ruler: [['robe', 2], ['plate', 1]],
        commoner: [['robe', 2], ['lamellar', 2]], female: [['robe', 1]],
      },
      cloth: ['#2c4a7a', '#7a2a2a', '#3a5a3a', '#5a3a6a', '#7a5a2a', '#2a3a4a', '#6a2a3a', '#3a3a3a', '#8a6a3a', '#2a5a5a', '#4a3a2a', '#1e3a5a'],
      metal: ['#7a808c', '#8a7048', '#5a5e68', '#a08048', '#3c3c44', '#7a3028', '#9aa0aa'],
      beard: 'han',
    },
    nanman: {
      name: '南蛮', skins: ['#c48c62', '#b98058', '#a8714c', '#986444', '#8a5a3c'], warSkin: 0.2,
      hairC: [['#141012', 1]], eyeC: BROWNEYE, lid: 0.45, noseBridge: 0.25, noseW: 1.2,
      noses: [['broad', 3], ['flat', 2], ['straight', 1]],
      hair: { m: [['mallet', 3], ['wild', 2], ['long', 1]], f: [['mallet', 1], ['braids', 1]] },
      hat: { warrior: [['feathers', 3], ['none', 2], ['headband', 2]], ruler: [['chief', 1]], female: [['feathers', 2], ['none', 1]], _: [['feathers', 2], ['none', 2], ['headband', 1]] },
      outfit: { warrior: [['pelt', 3], ['rattan', 2], ['bare', 1]], ruler: [['chiefrobe', 1]], female: [['pelt', 1]], _: [['pelt', 2], ['rattan', 1], ['bare', 1]] },
      cloth: ['#8a3a24', '#b8862a', '#4a6a2a', '#7a2a3a', '#2a5a6a', '#a04a2a'],
      metal: ['#8a7048', '#6a5a3a'],
      beards: { young: [['none', 3], ['stubble', 2]], adult: [['short', 3], ['full', 2], ['none', 2], ['stubble', 2], ['bristle', 1]] },
      musts: [['thick', 2], ['droop', 2], ['none', 1]],
      marks: [['warpaint', 0.5]], acc: [['hoops', 0.8], ['bones', 0.6]],
    },
    wa: {
      name: '倭', skins: ['#efcaa4', '#e6bd96', '#dcb088', '#d0a27a'], warSkin: 0.2,
      hairC: [['#16131a', 1]], eyeC: BROWNEYE, lid: 0.7, noseBridge: 0.35, noseW: 1.05,
      hair: { m: [['mizura', 4], ['topknot', 1]], f: [['wahair', 1]] },
      hat: { warrior: [['none', 3], ['headband', 2], ['wahelm', 2]], ruler: [['headband', 1], ['wahelm', 1]], female: [['shaman', 1], ['none', 1]], _: [['none', 3], ['headband', 1]] },
      outfit: { warrior: [['tanko', 3], ['kantoui', 2]], female: [['kantoui', 1]], _: [['kantoui', 3], ['tanko', 1]] },
      cloth: ['#e6dcc4', '#c8b890', '#9a3a2a', '#3a3a5a', '#d8ccb0', '#7a5a3a'],
      metal: ['#5a5e68', '#4a4a50', '#7a6a48'],
      beards: { young: [['none', 3], ['stubble', 1]], adult: [['short', 3], ['full', 2], ['goatee', 1], ['stubble', 2], ['none', 1]] },
      musts: [['thick', 2], ['thin', 2], ['droop', 1]],
      marks: [['tattoo-wa', 0.75]], acc: [['magatama', 0.6]],
    },
    yi: {
      name: '夷洲', skins: ['#d8a676', '#c99666', '#b98656', '#a87a4e'], warSkin: 0.2,
      hairC: [['#141012', 1]], eyeC: BROWNEYE, lid: 0.55, noseBridge: 0.3, noseW: 1.15,
      noses: [['broad', 3], ['straight', 2], ['flat', 1]],
      hair: { m: [['long', 3], ['mallet', 1]], f: [['long', 1]] },
      hat: { _: [['yiband', 4], ['none', 1]] },
      outfit: { _: [['yivest', 3], ['bare', 1]] },
      cloth: ['#b02a24', '#1a1a1a', '#e8dcc0', '#2a4a6a'],
      metal: ['#8a7048'],
      beards: { young: [['none', 1]], adult: [['none', 4], ['stubble', 2], ['goatee', 1]] },
      musts: [['none', 3], ['thin', 1]],
      marks: [['tattoo-yi', 0.75]], acc: [['shells', 0.7], ['hoops', 0.4]],
    },
    korea: {
      name: '朝鲜', skins: ['#f0cba8', '#e8c09c', '#dfb490', '#d6a884'], warSkin: 0.2,
      hairC: [['#16131a', 1]], eyeC: BROWNEYE, lid: 0.75, noseBridge: 0.4, noseW: 1.0,
      hair: { m: [['topknot', 1]], f: [['bun', 2], ['koreanbraid', 1]] },
      hat: { warrior: [['helmet', 3], ['jeolpung', 2]], general: [['helmet', 2], ['birdfeather', 2]], ruler: [['goldcrown', 2], ['birdfeather', 2]], female: [['none', 1]], _: [['jeolpung', 3], ['birdfeather', 2]] },
      outfit: { warrior: [['lamellar', 3], ['scale', 1]], general: [['lamellar', 2], ['jacket', 1]], female: [['jacket', 1]], _: [['jacket', 1]] },
      cloth: ['#c84a2a', '#e8c84a', '#3a5a8a', '#6a3a6a', '#e8e0d0', '#2a6a4a', '#8a2a2a'],
      metal: ['#5a5e68', '#7a808c', '#8a7048'],
      beards: { young: [['none', 3], ['goatee', 1]], adult: [['goatee', 3], ['short', 2], ['long', 1], ['none', 1]] },
      musts: [['thin', 3], ['thick', 1]],
    },
    steppe: {
      name: '草原', skins: ['#e8bc92', '#dcae84', '#d0a078', '#c49470'], warSkin: 0.25, ruddy: 0.6,
      hairC: [['#1a1614', 4], ['#2a1e16', 2], ['#3a2a1c', 1]], eyeC: BROWNEYE, lid: 0.75, noseBridge: 0.42, noseW: 1.05,
      hair: { m: [['kunfa', 4], ['braids', 3]], f: [['braids', 1]] },
      hat: { warrior: [['felt', 3], ['fur', 2], ['none', 2], ['helmet', 1]], ruler: [['fur', 2], ['felt', 2]], female: [['felt', 1], ['none', 1]], _: [['felt', 2], ['none', 2], ['fur', 1]] },
      outfit: { warrior: [['kaftan', 3], ['fur', 2], ['lamellar', 1]], _: [['kaftan', 3], ['fur', 2]] },
      cloth: ['#8a5a3a', '#5a3a2a', '#3a4a5a', '#7a2a24', '#2a4a3a', '#a8743a', '#4a3a5a'],
      metal: ['#5a5e68', '#7a6a48'], cross: -1,
      beards: { young: [['none', 3], ['stubble', 1]], adult: [['goatee', 2], ['short', 2], ['none', 2], ['full', 1]] },
      musts: [['droop', 4], ['thin', 2], ['thick', 1]],
      acc: [['earring', 0.5]],
    },
    seasia: {
      name: '南海', skins: ['#bc8656', '#b07a4a', '#a06c40', '#946238', '#87582f'], warSkin: 0.15,
      hairC: [['#141012', 1]], eyeC: BROWNEYE, lid: 0.4, noseBridge: 0.28, noseW: 1.25,
      noses: [['flat', 2], ['broad', 3], ['straight', 1]],
      hair: { m: [['highbun', 3], ['long', 1]], f: [['highbun', 1]] },
      hat: { ruler: [['tallcrown', 1]], warrior: [['none', 2], ['flowerwrap', 1], ['headband', 1]], female: [['none', 1]], _: [['none', 2], ['flowerwrap', 2]] },
      outfit: { _: [['bare', 3], ['sash', 2]], female: [['sash', 1]] },
      cloth: ['#c8a030', '#a02a3a', '#e8dcc0', '#2a6a5a', '#7a3a8a', '#c85a2a'],
      metal: ['#c8a040'],
      beards: { young: [['none', 1]], adult: [['none', 4], ['goatee', 1], ['stubble', 1]] },
      musts: [['none', 3], ['thin', 2]],
      acc: [['bigears', 0.8], ['goldcollar', 0.5]],
    },
    tarim: {
      name: '西域', skins: ['#f2d0b0', '#ebc6a2', '#e2ba96', '#d8ae8a'], warSkin: 0.2,
      hairC: [['#3a2618', 3], ['#5a3a20', 2], ['#7a4a28', 1], ['#1a1614', 2], ['#8a5a30', 1]], eyeC: ['#3a2a1a', '#4a6a5a', '#6a6a5a', '#5a4a2a', '#4a5a6a'],
      lid: 0.15, noseBridge: 0.75, noseW: 0.95, noses: [['straight', 3], ['aquiline', 2]],
      hair: { m: [['bob', 3], ['long', 1], ['short', 1]], f: [['long', 1], ['braids', 1]] },
      hat: { ruler: [['diadem', 2], ['pointcap', 1]], _: [['pointcap', 3], ['none', 2]] },
      outfit: { warrior: [['kaftan', 2], ['scale', 2]], _: [['kaftan', 3]] },
      cloth: ['#7a3a2a', '#2a5a6a', '#c8a04a', '#5a2a4a', '#e0d0b0', '#3a5a3a'],
      metal: ['#7a808c', '#8a7048'],
      beards: { young: [['none', 3], ['stubble', 1]], adult: [['short', 3], ['goatee', 2], ['full', 1], ['none', 1]] },
      musts: [['curl', 3], ['thin', 2], ['thick', 1]],
      acc: [['earring', 0.3]],
    },
    kushan: {
      name: '贵霜', skins: ['#e6bd96', '#dcb08a', '#cfa27c', '#c49470'], warSkin: 0.2,
      hairC: [['#1a1614', 3], ['#2a1e16', 3], ['#3a2a1c', 1]], eyeC: BROWNEYE, lid: 0.15, noseBridge: 0.9, noseW: 1.0,
      noses: [['aquiline', 3], ['straight', 2], ['bulb', 1]],
      hair: { m: [['curly', 2], ['long', 2]], f: [['long', 1]] },
      hat: { ruler: [['tallhat', 2], ['diadem', 1]], warrior: [['helmet', 2], ['tallhat', 1], ['diadem', 1]], female: [['none', 1]], _: [['tallhat', 2], ['diadem', 1], ['none', 1]] },
      outfit: { warrior: [['kaftan', 2], ['scale', 2]], _: [['kaftan', 3]] },
      cloth: ['#8a2a2a', '#c8903a', '#3a5a3a', '#2a3a6a', '#e0c8a0', '#6a2a5a'],
      metal: ['#8a7048', '#7a808c', '#a08048'],
      beards: { young: [['short', 1], ['none', 1]], adult: [['full', 3], ['curled', 1], ['short', 2], ['long', 1]] },
      musts: [['thick', 3], ['curl', 1]],
      acc: [['earring', 0.5]],
    },
    persia: {
      name: '安息', skins: ['#e2b48c', '#d6a880', '#ca9a72', '#bd8c66'], warSkin: 0.2,
      hairC: [['#16131a', 3], ['#221a18', 3]], eyeC: BROWNEYE, lid: 0.1, noseBridge: 0.95, noseW: 1.0,
      noses: [['aquiline', 3], ['straight', 2]],
      hair: { m: [['bushy', 3], ['curly', 1]], f: [['long', 1]] },
      hat: { ruler: [['tiara', 3], ['diadem', 1]], warrior: [['helmet', 2], ['diadem', 1], ['phrygian', 1]], female: [['diadem', 1]], _: [['diadem', 2], ['phrygian', 2], ['none', 1]] },
      outfit: { warrior: [['scale', 3], ['kaftan', 1]], ruler: [['tunic', 2], ['scale', 1]], female: [['tunic', 1]], _: [['tunic', 2], ['kaftan', 1]] },
      cloth: ['#7a2a5a', '#2a4a7a', '#c8a040', '#8a2a24', '#3a6a5a', '#5a2a6a'],
      metal: ['#7a808c', '#8a7048', '#5a5e68'],
      beards: { young: [['short', 2], ['none', 1]], adult: [['curled', 2], ['long', 2], ['forked', 1], ['full', 2]] },
      musts: [['thick', 2], ['curl', 2]],
      acc: [['earring', 0.6], ['torc', 0.3]],
    },
    arab: {
      name: '阿拉伯', skins: ['#deaa7a', '#d29c6c', '#c48e5e', '#b88254', '#a8744a'], warSkin: 0.2,
      hairC: [['#141012', 1]], eyeC: BROWNEYE, lid: 0.1, noseBridge: 0.9, noseW: 1.0,
      noses: [['aquiline', 3], ['straight', 2]],
      hair: { m: [['curly', 3], ['long', 1]], f: [['long', 1]] },
      hat: { ruler: [['hatra', 1], ['keffiyeh', 1]], female: [['keffiyeh', 1]], _: [['keffiyeh', 3], ['arabturban', 2]] },
      outfit: { warrior: [['robea', 2], ['scale', 2]], _: [['robea', 3]] },
      cloth: ['#e8e0d0', '#8a2a24', '#2a3a5a', '#c8a050', '#3a5a3a', '#5a3a2a'],
      metal: ['#7a808c', '#8a7048'],
      beards: { young: [['short', 2], ['none', 1]], adult: [['full', 3], ['curled', 1], ['short', 2], ['goatee', 1]] },
      musts: [['thick', 3], ['thin', 1]],
    },
    roman: {
      name: '罗马', skins: ['#efcfb0', '#e6c09e', '#dbb28e', '#cfa480'], warSkin: 0.25,
      hairC: [['#2a1e16', 3], ['#3a2a1c', 2], ['#1a1614', 2], ['#5a3a22', 1], ['#8a6a40', 0.5]], eyeC: ['#3a2a1a', '#4a5a6a', '#5a6a4a', '#2a1a10'],
      lid: 0.1, noseBridge: 0.9, noseW: 0.95, noses: [['straight', 3], ['aquiline', 2]],
      hair: { m: [['short', 3], ['curly', 2]], f: [['romanf', 1]] },
      hat: { ruler: [['laurel', 3]], warrior: [['galea', 3], ['none', 1]], general: [['galea', 2], ['none', 1]], female: [['none', 1]], _: [['none', 3], ['laurel', 0.4]] },
      outfit: { warrior: [['segmentata', 2], ['musculata', 1]], general: [['musculata', 2]], ruler: [['musculata', 2], ['toga', 1]], official: [['toga', 2]], strategist: [['toga', 1], ['tunic', 1]], female: [['tunic', 1]], _: [['tunic', 1], ['segmentata', 1]] },
      cloth: ['#a02a24', '#7a2a5a', '#e8e0d0', '#2a3a5a', '#c8a050', '#8a3a2a'],
      metal: ['#b08a50', '#7a808c', '#9aa0aa'],
      beards: { young: [['none', 3], ['stubble', 1]], adult: [['none', 2], ['short', 3], ['full', 1], ['stubble', 1]] },
      musts: [['none', 1], ['thick', 1]], mustWithBeard: true,
    },
    celt: {
      name: '凯尔特', skins: ['#f6dcc6', '#f0d0b8', '#e8c4aa', '#ddb89c'], warSkin: 0.15, freckle: 0.4,
      hairC: [['#a8481e', 3], ['#c86a2a', 2], ['#d8b070', 2], ['#6a4a2a', 2], ['#3a2a1c', 1]], eyeC: LIGHTEYE,
      lid: 0.0, noseBridge: 0.8, noseW: 1.0,
      hair: { m: [['wild', 3], ['spiky', 2], ['braids', 2]], f: [['braids', 2], ['long', 1]] },
      hat: { _: [['none', 5], ['celthelm', 1]], female: [['none', 1]] },
      outfit: { _: [['plaid', 3], ['fur', 1], ['bare', 1]], female: [['plaid', 1]] },
      cloth: ['#3a6a3a', '#7a2a24', '#2a4a7a', '#8a6a2a', '#5a3a5a'],
      metal: ['#b08a50', '#7a808c'],
      beards: { young: [['none', 2], ['stubble', 1]], adult: [['none', 3], ['braided', 1], ['full', 2], ['short', 1]] },
      musts: [['droop', 5], ['thick', 1]], mustAlways: true,
      marks: [['woad', 0.55]], acc: [['torc', 0.7]],
    },
    german: {
      name: '日耳曼', skins: ['#f4d8c0', '#ecccb2', '#e2c0a4', '#d8b498'], warSkin: 0.15, freckle: 0.2,
      hairC: [['#d8b878', 3], ['#c8a060', 2], ['#a87a40', 2], ['#8a5a2a', 2], ['#c06030', 1]], eyeC: LIGHTEYE,
      lid: 0.0, noseBridge: 0.85, noseW: 1.0,
      hair: { m: [['suebian', 4], ['long', 2], ['wild', 1]], f: [['braids', 2], ['long', 1]] },
      hat: { warrior: [['none', 4], ['spangen', 1], ['furcap', 1]], female: [['none', 1]], _: [['none', 4], ['furcap', 1]] },
      outfit: { _: [['fur', 3], ['tunic', 1]] },
      cloth: ['#5a4a3a', '#3a4a3a', '#7a3a2a', '#4a4a5a', '#8a7a5a'],
      metal: ['#7a808c', '#5a5e68'],
      beards: { young: [['short', 1], ['none', 1]], adult: [['full', 3], ['long', 2], ['braided', 1], ['short', 1]] },
      musts: [['thick', 2], ['droop', 2]],
      acc: [['armring', 0.3]],
    },
    sarmatian: {
      name: '萨尔马提亚', skins: ['#f0d0b0', '#e6c4a2', '#dcb894', '#d2ac88'], warSkin: 0.2,
      hairC: [['#5a3a22', 3], ['#3a2a1c', 2], ['#8a6a40', 2], ['#c8a060', 1], ['#1a1614', 1]], eyeC: LIGHTEYE,
      lid: 0.2, noseBridge: 0.85, noseW: 1.0,
      hair: { m: [['long', 3], ['braids', 1]], f: [['braids', 1], ['long', 1]] },
      hat: { warrior: [['conical', 4], ['none', 1]], ruler: [['conical', 2], ['diadem', 1]], female: [['pointcap', 1]], _: [['pointcap', 2], ['conical', 1], ['none', 1]] },
      outfit: { _: [['scale', 4], ['kaftan', 1]] },
      cloth: ['#7a2a24', '#2a4a5a', '#c8a050', '#3a3a3a', '#6a3a5a'],
      metal: ['#7a808c', '#8a7048', '#5a5e68'], cross: -1,
      beards: { young: [['none', 2], ['short', 1]], adult: [['long', 2], ['full', 2], ['short', 1]] },
      musts: [['droop', 4], ['thick', 1]],
      acc: [['earring', 0.5], ['torc', 0.3]],
    },
  };
  // 冠帽默认配色（按类型）
  const HAT_COL = {
    ze: ['#2a2226', '#2a2226', '#7a2424', '#3a2a20', '#2a3040'],
    headband: ['#a02a24', '#2a2a2a', '#c8a030', '#2a4a7a', '#7a2a5a'],
    turban: ['#3a6a3a', '#5a4a3a', '#2a3a5a', '#7a3a2a', '#4a4a4a', '#6a5a2a'],
    futou: ['#5a4a3a', '#3a3a3a', '#6a6050', '#2a3a4a'],
    felt: ['#e0d4b8', '#8a3a2a', '#5a4a3a', '#3a4a5a'],
    pointcap: ['#e8dcc0', '#9a2a24', '#5a4a3a', '#c8a050'],
    tallhat: ['#e8dcc0', '#8a2a2a', '#c8a050'],
    phrygian: ['#9a2a24', '#2a4a7a', '#c8a050', '#5a2a5a'],
    keffiyeh: ['#ece6da', '#e8dcc8', '#d8c8a8'],
    arabturban: ['#ece6da', '#2a3a5a', '#8a2a24', '#c8a050'],
    flowerwrap: ['#c83a4a', '#e8c040', '#2a8a7a', '#e0d8c0'],
  };
  const PLUMES = [['#c0302a', 5], ['#1a1a1a', 1], ['#e8e8e8', 1], ['#2a4aa0', 1], ['#d8a020', 1]];
  const SKIN_TINT = ['#d0806a', '#c8b07a', '#a87050', '#e0a090'];

  // ======================================================== 外貌参数 ==
  const ROLE_AGE = { warrior: [24, 46], general: [28, 50], strategist: [26, 56], official: [30, 62], ruler: [34, 58], commoner: [22, 50], female: [18, 34] };
  let rulerNames = null;
  function isRuler(name) {
    if (!rulerNames) {
      rulerNames = new Set();
      try {
        for (const line of (SG.ScenarioData && SG.ScenarioData.Factions) || []) rulerNames.add(line.split('|')[2]);
      } catch (e) { /* 无剧本数据 */ }
    }
    return rulerNames.has(name);
  }
  function roleOf(war, intel, pol, ruler, sex) {
    if (sex === 'f') return 'female';
    if (ruler) return 'ruler';
    if (war >= 80 && war >= intel + 8) return 'warrior';
    if (war >= 70 && intel >= 70) return 'general';
    if (intel >= 76 && intel >= war + 12) return pol > intel + 4 ? 'official' : 'strategist';
    if (pol >= 72 && pol >= war + 12) return 'official';
    if (war >= 66) return 'warrior';
    return 'commoner';
  }
  // 简写 → 对象：'turban' → { type: 'turban' }
  const OBJ_KEYS = { hat: 'type', outfit: 'type', hair: 'style', beard: 'style', mustache: 'style', eyes: 'shape', brows: 'shape', nose: 'shape', mouth: 'shape', face: null };
  function normEntry(e) {
    const o = {};
    for (const k in e) {
      const v = e[k];
      if (OBJ_KEYS[k] && typeof v === 'string') o[k] = { [OBJ_KEYS[k]]: v };
      else o[k] = v;
    }
    return o;
  }
  function curYear() {
    if (SG.G && SG.G.year) return SG.G.year;
    if (SG.ScenarioData && SG.ScenarioData.StartYear) return SG.ScenarioData.StartYear;
    return 190;
  }

  function pickRole(table, role, r) {
    const list = (table && (table[role] || table._ || table.commoner)) || [['none', 1]];
    return r.wpick(list);
  }
  function defaultSpec(gen, data) {
    const name = gen.name || '无名';
    const culture = CULT[data.culture] ? data.culture : CULT[gen.culture] ? gen.culture : (NANMAN.has(name) ? 'nanman' : 'han');
    const C = CULT[culture];
    const war = gen.war == null ? 50 : gen.war, intel = gen.intel == null ? 50 : gen.intel, pol = gen.pol == null ? 50 : gen.pol;
    const sex = data.sex || gen.sex || 'm';
    const fem = sex === 'f';
    const role = data.role || roleOf(war, intel, pol, isRuler(name), sex);
    const rb = sub(name, 'base'), rf = sub(name, 'face'), rh = sub(name, 'hair'), rc = sub(name, 'cloth'), rx = sub(name, 'extra');
    const ar = ROLE_AGE[role] || [24, 50];
    let age = data.age;
    if (age == null && gen.born) age = curYear() - gen.born;
    if (age == null) age = Math.round(lerp(ar[0], ar[1], Math.pow(rb.f(), 1.3)));
    age = clamp(age, 14, 92);
    const tough = clamp((war - 50) / 50, -1, 1);           // 武勇
    const wise = clamp((intel - 50) / 50, -1, 1);          // 智略
    const old = clamp((age - 40) / 30, 0, 1);
    const martial = role === 'warrior' || role === 'general';
    // 肤色：武将偏深（风吹日晒），并加一点个人色调
    const skinI = clamp(rb.f() * 0.85 + (martial ? C.warSkin : 0) + (fem ? -0.2 : 0), 0, 0.999);
    let skin = C.skins[Math.floor(skinI * C.skins.length)];
    if (rb.chance(0.65)) skin = mix(skin, rb.pick(SKIN_TINT), rb.range(0.04, 0.14));
    const fat = data.fat != null ? data.fat : (rb.chance(0.14) ? rb.range(0.25, 0.6) : 0) * (role === 'warrior' ? 0.7 : 1) * (fem ? 0.3 : 1);
    const face = {
      w: (fem ? 0.95 : 1) * rf.range(0.91, 1.08) + tough * 0.03,
      jaw: fem ? rf.range(0.76, 0.9) : rf.range(0.84, 1.18) + tough * 0.08,
      chin: fem ? rf.range(0.84, 0.95) : rf.range(0.84, 1.16),
      len: fem ? rf.range(0.92, 1.0) : rf.range(0.92, 1.1) + wise * 0.03,
      cheek: rf.range(0, 1) * (fem ? 0.4 : 1),
      hollow: clamp(rf.range(0, 0.5) + old * 0.6 - fat, 0, 1),
      brow: clamp(rf.range(0, 1) * (fem ? 0.2 : 1) + tough * 0.3, 0, 1.4),
    };
    const hairColor = (() => {
      const base = rh.wpick(C.hairC);
      if (age >= 66) return rh.pick(['#e8e4dc', '#d8d4cc', '#f0ece4']);
      if (age >= 52) return mix(base, '#c8c4bc', clamp((age - 50) / 18, 0.3, 0.8));
      if (age >= 44 && rh.chance(0.5)) return mix(base, '#a8a49c', 0.25);
      return base;
    })();
    const eyeShapes = fem ? (war >= 70 ? [['phoenix', 3], ['sharp', 2], ['gentle', 1]] : [['gentle', 3], ['phoenix', 3], ['big', 1.5]])
      : role === 'warrior' ? [['sharp', 4], ['normal', 3], ['round', 2], ['phoenix', 1], ['narrow', 1], ['sleepy', 0.5]]
        : role === 'strategist' || role === 'official' ? [['narrow', 3], ['normal', 3], ['gentle', 2], ['phoenix', 2], ['sharp', 1], ['sleepy', 1]]
          : [['normal', 4], ['sharp', 2], ['gentle', 2], ['narrow', 1], ['round', 1], ['sleepy', 1]];
    const browShapes = fem ? [['thin', 1]]
      : role === 'warrior' ? [['angled', 4], ['bushy', 3], ['knit', 2], ['straight', 1], ['sword', 1]]
        : role === 'strategist' || role === 'official' ? [['thin', 3], ['arched', 3], ['straight', 2], ['angled', 1]]
          : [['straight', 3], ['arched', 2], ['angled', 2], ['bushy', 1]];
    const noseShapes = fem ? [['small', 1]] : (C.noses || [['straight', 5], ['aquiline', 1], ['broad', 2], ['button', 1], ['bulb', 0.4]]);
    // 胡须
    let beard = 'none', must = 'none', beardLen = 1;
    const rB = sub(name, 'beard');
    if (!fem && age >= 18) {
      if (C.beard === 'han') {
        if (role === 'warrior') {
          beard = rB.wpick(age < 26 ? [['none', 3], ['stubble', 3], ['short', 2], ['goatee', 1]] : [['full', 2], ['short', 3], ['bristle', 1], ['long', 1], ['goatee', 2], ['stubble', 1.5], ['none', 1], ['forked', 0.5]]);
          must = beard === 'none' ? rB.wpick([['none', 2], ['thick', 1]]) : rB.wpick([['thick', 3], ['droop', 2], ['thin', 1], ['curl', 0.5]]);
        } else if (role === 'strategist' || role === 'official') {
          beard = rB.wpick(age < 28 ? [['none', 3], ['goatee', 2]] : [['long', 3], ['goatee', 4], ['short', 1], ['forked', 0.5]]);
          must = beard === 'none' ? (rB.chance(0.3) ? 'thin' : 'none') : rB.wpick([['thin', 4], ['droop', 1]]);
        } else if (role === 'ruler' || role === 'general') {
          beard = rB.wpick([['long', 3], ['short', 3], ['goatee', 2], ['full', 1], ['forked', 0.5]]);
          must = rB.wpick([['thin', 3], ['thick', 2], ['droop', 1]]);
        } else {
          beard = rB.wpick(age < 26 ? [['none', 3], ['stubble', 2], ['goatee', 1]] : [['short', 3], ['goatee', 2], ['full', 2], ['stubble', 2], ['none', 1]]);
          must = beard === 'none' ? 'none' : rB.wpick([['thin', 2], ['thick', 2], ['droop', 1]]);
        }
      } else {
        const tb = C.beards || { young: [['none', 1]], adult: [['short', 1]] };
        beard = rB.wpick(age < 25 ? tb.young : tb.adult);
        must = rB.wpick(C.musts || [['thick', 1]]);
        if (C.mustWithBeard && (beard === 'none' || beard === 'stubble')) must = 'none';
        if (!C.mustAlways && beard === 'none' && rB.chance(0.5)) must = 'none';
      }
      beardLen = rB.range(0.8, 1.3) + old * 0.3;
    }
    // 冠帽
    const hatType = pickRole(C.hat, role, rc);
    const hat = { type: hatType, color: HAT_COL[hatType] ? rc.pick(HAT_COL[hatType]) : null, color2: null };
    if (hatType === 'helmet') {
      const vv = culture === 'korea' ? [['plume', 3], ['round', 1]] : culture === 'kushan' || culture === 'persia' ? [['spike', 3], ['round', 1]]
        : culture === 'steppe' ? [['round', 2], ['spike', 1]] : [['plain', 4], ['round', 3], ['spike', 2]].concat(war >= 88 ? [['wing', 1.5], ['horn', 0.6]] : []);
      hat.variant = rc.wpick(vv);
      hat.plume = rc.wpick(PLUMES);
      hat.neck = rc.chance(0.5) ? null : rc.pick(['#5a3a24', '#7a2020', '#2a2a3a', '#4a4030']);
    }
    // 衣甲
    // 女将（武力高的女性）穿本文化的武将衣甲；女性不赤膊
    let outfitType = pickRole(C.outfit, fem && war >= 70 ? 'warrior' : role, rc);
    if (fem && outfitType === 'bare') outfitType = ((C.outfit.female || C.outfit._ || []).map(e => e[0]).find(t => t !== 'bare')) || 'robe';
    const clothC = rc.pick(C.cloth);
    let cloth2 = rc.pick(C.cloth);
    if (cloth2 === clothC) cloth2 = mix(clothC, '#000', 0.4);
    const outfit = { type: outfitType, color: clothC, color2: cloth2, metal: rc.pick(C.metal), cape: role === 'ruler' ? 'faction' : null, cross: C.cross || 1 };
    // 标记与配饰
    const marks = [], acc = [];
    for (const [m, p] of C.marks || []) if (!fem && rx.chance(p)) marks.push(m);
    if (C.freckle && rx.chance(C.freckle)) marks.push('freckles');
    for (const [a, p] of C.acc || []) if (rx.chance(p)) acc.push(a);
    if (martial && rx.chance(0.06)) marks.push('scar');
    if (rx.chance(0.05)) marks.push('mole');
    // 散发几缕（武人）
    const locks = martial && rh.chance(0.3) ? rh.int(1, 2) : 0;
    return {
      name, culture, sex, role, age, war, intel, pol, fat, face, skin,
      blush: C.ruddy ? 1.8 : 1,
      build: fem ? 0.84 : clamp(0.94 + tough * 0.1 + fat * 0.15 + rb.range(-0.04, 0.05), 0.86, 1.2),
      neck: fem ? 0.74 : clamp(0.95 + tough * 0.12 + fat * 0.4, 0.85, 1.45),
      eyes: { shape: rf.wpick(eyeShapes), color: rf.pick(C.eyeC), size: rf.range(0.9, 1.08) * (fem ? 1.08 : 1), lid: C.lid, gap: rf.range(-0.035, 0.035), y: rf.range(-0.035, 0.035) },
      brows: { shape: rf.wpick(browShapes), thick: clamp(rf.range(0.8, 1.15) + tough * 0.15, 0.6, 1.45) * (fem ? 0.7 : 1), color: null, y: rf.range(-0.05, 0.04) },
      nose: { shape: rf.wpick(noseShapes), size: rf.range(0.9, 1.1), len: rf.range(0.92, 1.1), bridge: C.noseBridge, w: C.noseW * rf.range(0.9, 1.12) },
      mouth: { w: rf.range(0.88, 1.1) * (fem ? 0.9 : 1), lips: fem ? 1.2 : rf.range(0.75, 1.15), shape: 'neutral', y: rf.range(-0.03, 0.04) },
      ears: { size: rf.range(0.9, 1.12) },
      hair: { style: rh.wpick(C.hair[sex] || C.hair.m), color: hairColor, vol: rh.range(0.85, 1.15), locks },
      beard: { style: beard, len: beardLen, color: rB.chance(0.15) ? mix(hairColor, '#5a3a20', 0.3) : null },
      mustache: { style: must },
      hat, outfit, acc, marks, weapon: null,
      expr: martial ? rb.wpick([['stern', 3], ['fierce', 2], ['neutral', 2], ['smile', 0.7], ['sly', 0.4]]) : rb.wpick([['neutral', 3], ['calm', 2], ['stern', 1], ['smile', 1], ['sly', 0.6]]),
    };
  }
  const NANMAN = new Set(['孟获', '祝融', '孟优', '兀突骨', '带来洞主', '沙摩柯', '孟节', '朵思大王', '木鹿大王', '金环三结', '董荼那', '阿会喃']);

  // 深合并一层（对象字段逐项覆盖）
  function mergeSpec(base, data) {
    const out = Object.assign({}, base);
    for (const k in data) {
      const v = data[k];
      // 换了冠帽类型时，不沿用默认冠帽的配色（否则紫金冠会染上帻的黑色）
      if (k === 'hat' && v && v.type && base.hat && v.type !== base.hat.type) { out.hat = Object.assign({ type: v.type, color: null, color2: null }, HAT_COL[v.type] ? { color: HAT_COL[v.type][hashStr(base.name + 'hc') % HAT_COL[v.type].length] } : null, v); continue; }
      if (v && typeof v === 'object' && !Array.isArray(v) && base[k] && typeof base[k] === 'object' && !Array.isArray(base[k])) out[k] = Object.assign({}, base[k], v);
      else out[k] = v;
    }
    return out;
  }
  function findGen(name) {
    if (SG.G && SG.G.generals) { const g = SG.G.generals.find(x => x.name === name); if (g) return g; }
    try {
      for (const line of (SG.ScenarioData && SG.ScenarioData.Generals) || []) {
        const p = line.split('|');
        if (p[0] === name) return { name, war: +p[1], intel: +p[2], pol: +p[3] };
      }
    } catch (e) { /* 忽略 */ }
    return { name };
  }
  const specCache = new Map();
  let dataVersion = 0;
  function resolveSpec(gen) {
    if (typeof gen === 'string') gen = findGen(gen);
    gen = gen || { name: '无名' };
    const name = gen.name || '无名';
    // 只有带出生年的人才随年份变老（否则不必每年重画）
    const key = name + '|' + (gen.culture || '') + '|' + (gen.war | 0) + '|' + (gen.intel | 0) + '|' + (gen.pol | 0) + '|' + (gen.born ? gen.born + '@' + curYear() : '') + '|' + (gen.sex || '');
    let sp = specCache.get(key);
    if (sp) return sp;
    const raw = (SG.PortraitData && SG.PortraitData.get && SG.PortraitData.get(name)) || (gen.portrait) || {};
    const data = normEntry(raw);
    sp = mergeSpec(defaultSpec(gen, data), data);
    if (sp.sex === 'f') { sp.beard = { style: 'none' }; sp.mustache = { style: 'none' }; }
    if (!sp.brows.color) sp.brows.color = sp.hair.color;
    if (!sp.beard.color) sp.beard.color = sp.hair.color;
    sp.key = key;
    specCache.set(key, sp);
    return sp;
  }

  // ======================================================== 几何骨架 ==
  // 所有面部位置以 R（半个头宽）为单位，相对 (hx, ey)（头轴、眼线）。
  function geom(sp) {
    const f = sp.face, fem = sp.sex === 'f';
    const fat = sp.fat || 0;
    const R = 38.5 * f.w * (fem ? 0.95 : 1) * (1 + fat * 0.07);
    const L = f.len, jw = f.jaw, cw = f.chin;
    const hx = 136, ey = 121 - (L - 1) * 12;
    const t = 0.40, st = Math.sin(t), ct = Math.cos(t);
    const pe = 0.18, spe = Math.sin(pe), cpe = Math.cos(pe);
    const G = { R, L, hx, ey, t, fem, fat, jw, cw };
    const P = (x, y, k) => [hx + x * R, ey + y * R, k];
    G.P = P;
    // 面部宽度系数（下巴处收窄）
    const wf = y => (y < 0.42 ? 1 : 1 - (1 - 0.58 * cw) * smooth01((y - 0.42) / (1.38 * L - 0.42)));
    // 正面横向偏移 u（-1..1，单位半脸宽）在第 y 行的画面 x
    G.FX = (u, y, dz) => hx + R * wf(y) * Math.sin(Math.asin(clamp(u, -1, 1)) - t) - (dz || 0) * R * st;
    G.FS = (u, y) => wf(y) * Math.cos(Math.asin(clamp(u, -1, 1)) - t); // 横向缩放
    G.F = (u, y, dz) => [G.FX(u, y, dz), ey + y * R];
    // 颅骨椭球：中心 (hx+0.08R, ey-0.5R)，半轴 a 横 / b 竖 / c 前后
    const a = 1.0 * R, b = 1.02 * R, c = 1.22 * R, Cx = hx + 0.08 * R, Cy = ey - 0.5 * R;
    G.ell = { a, b, c, Cx, Cy };
    G.h3 = (lam, bet, s) => {
      s = s || 1;
      const X = a * s * Math.cos(bet) * Math.sin(lam), Y = b * s * Math.sin(bet), Z = c * s * Math.cos(bet) * Math.cos(lam);
      const x = X * ct - Z * st, z = X * st + Z * ct;
      return [Cx + x, Cy - Y * cpe + z * spe, z];
    };
    G.silRx = s => (s || 1) * Math.sqrt(a * a * ct * ct + c * c * st * st);
    G.silRy = s => (s || 1) * b * cpe;
    G.lamFar = Math.atan2(-c * ct, a * st) + Math.PI; // 远侧轮廓经度（负值）
    if (G.lamFar > Math.PI) G.lamFar -= 2 * Math.PI;
    G.lamFar = -Math.abs(Math.atan(c * ct / (a * st)));
    G.lamNear = G.lamFar + Math.PI;
    // 头部轮廓（含颅骨）
    const rx1 = G.silRx(1), ry1 = G.silRy(1);
    const cr = (deg, k) => { const q = deg * Math.PI / 180; return [Cx + rx1 * Math.cos(q), Cy - ry1 * Math.sin(q), k]; };
    const bb = f.brow || 0, ck = f.cheek || 0, hol = f.hollow || 0;
    const fk = fem ? 0.03 : 0;
    G.farC = [
      P(-0.955, -0.58),
      P(-1.0 - bb * 0.035 + fk, -0.30),
      P(-0.955 + fk * 0.3, -0.06, fem ? 1 : 0.7),
      P(-0.99 - ck * 0.05 - fat * 0.03 + fk, 0.24),
      P(-0.93 + hol * 0.07 - fat * 0.12 + fk, 0.55),
      P(-0.82 - (jw - 1) * 0.25 - fat * 0.2 + fk, 0.86 * L),
      P(-0.66 * cw - (jw - 1) * 0.15 - fat * 0.2, 1.12 * L + fat * 0.06),
      P(-0.52 * cw - fat * 0.14, 1.31 * L + fat * 0.1, 0.7),
      P(-0.34 * cw - fat * 0.05, 1.42 * L + fat * 0.13),
    ];
    G.jawC = [
      P(-0.06, 1.43 * L + fat * 0.22),
      P(0.30 * jw + fat * 0.15, 1.23 * L + fat * 0.16),
      P(0.66 * jw + fat * 0.25, 0.86 * L + fat * 0.1, 0.55),
      P(0.90 + fat * 0.1, 0.50),
      cr(-25),
    ];
    G.headPts = [cr(160)].concat(G.farC, G.jawC, [cr(10), cr(45), cr(90), cr(130)]);
    G.headD = spline(G.headPts, true);
    G.chinPt = P(-0.30 * cw, 1.43 * L);
    G.jawAngle = G.jawC[2];
    // 五官位置
    const es = sp.eyes.size || 1, gap = sp.eyes.gap || 0, edy = sp.eyes.y || 0;
    G.eyeY = ey; G.eyeDy = edy * R;
    G.eyeN = { u: 0.42 + gap, y: edy, cx: G.FX(0.42 + gap, edy), w: 0.42 * R * G.FS(0.42 + gap, edy) * es, dir: 1 };
    G.eyeF = { u: -0.42 - gap, y: edy, cx: G.FX(-0.42 - gap, edy) + 0.02 * R, w: 0.42 * R * G.FS(-0.42 - gap, edy) * es, dir: -1 };
    const es2 = sp.ears.size || 1, el2 = sp.ears.lobe || 1;
    const eu0 = 0.52, eu1 = 0.52 + 0.32 * es2, ev0 = -0.22 - (es2 - 1) * 0.1, ev1 = 0.62 * Math.sqrt(L) + (es2 - 1) * 0.35 + (el2 - 1) * 0.3;
    G.ear = { u0: eu0, u1: eu1, v0: ev0, v1: ev1, x0: hx + eu0 * R, x1: hx + eu1 * R, y0: ey + ev0 * R, y1: ey + ev1 * R };
    G.noseY = 0.6 * L * (sp.nose.len || 1); G.mouthY = 0.93 * L + (sp.mouth.y || 0) + fat * 0.04; G.chinY = 1.38 * L + fat * 0.06;
    G.midX = y => G.FX(0, y);
    // 颈
    const nw = clamp(sp.neck || 1, 0.7, 1.22); // 再粗就比头还宽了
    G.neckPts = [P(-0.36 * nw, 1.22 * L, 1), P(-0.46 * nw, 1.9), P(-0.5 * nw, 2.7, 0), P(1.0 * nw, 2.6, 0), P(0.96 * nw, 1.7), P(0.84 * nw, 0.45)];
    G.neckD = spline(G.neckPts, true);
    // 躯干（衣甲共用轮廓）
    const bw = sp.build || 1;
    G.bw = bw; G.nw = nw;
    G.torso = [
      P(-0.48 * nw, 1.78), P(-1.45 * bw, 2.16), P(-2.42 * bw, 2.6, 0.9), P(-2.86 * bw, 3.08), P(-3.1 * bw, 4.4, 0),
      P(3.4 * bw, 4.4, 0), P(3.1 * bw, 2.9), P(2.62 * bw, 2.3, 0.9), P(1.55 * bw, 1.82), P(0.92 * nw, 1.5),
    ];
    G.chestX = hx - 0.55 * R; // 胸口中线
    return G;
  }
  // 头带 / 冠沿：经度从远侧轮廓到近侧轮廓的可见前半圈，纬度 β(λ) = bm + ba·cosλ
  function band(G, bm, ba, s, n) {
    n = n || 16;
    const out = [];
    const l0 = G.lamFar + 0.02, l1 = G.lamNear - 0.02;
    for (let i = 0; i <= n; i++) {
      const lam = l0 + (l1 - l0) * i / n;
      out.push(G.h3(lam, bm + ba * Math.cos(lam), s));
    }
    return out;
  }
  // 冠顶：band 以上的部分（band 点 + 剪影弧，经由顶部）
  function capOutline(G, bandPts, s, topLift) {
    const e = G.ell, rx = G.silRx(s), ry = G.silRy(s) * (topLift || 1);
    const a0 = bandPts[bandPts.length - 1], a1 = bandPts[0];
    const ang = p => Math.atan2(-(p[1] - e.Cy) / ry, (p[0] - e.Cx) / rx);
    let t0 = ang(a0), t1 = ang(a1);
    if (t1 < t0) t1 += Math.PI * 2;
    const arc = arcPts(e.Cx, e.Cy, rx, ry, t0, t1, 10).slice(1, -1);
    return bandPts.concat(arc);
  }

  // ======================================================== 绘制：背景 ==
  function drawBackground(S, sp, o) {
    const fc = o.color;
    const c0 = mix(fc, '#3a3446', 0.6), c1 = mix(fc, '#15111c', 0.82), c2 = '#07060a';
    S.defs.push(`<radialGradient id="${S.id('bg')}" cx="0.4" cy="0.34" r="0.8"><stop offset="0" stop-color="${c0}"/><stop offset="0.55" stop-color="${c1}"/><stop offset="1" stop-color="${c2}"/></radialGradient>`);
    S.bg = `<rect x="0" y="0" width="256" height="256" fill="url(#${S.id('bg')})"/>`;
    // 头后光晕（让暗色头发与背景分开）
    S.bg += circle(138, 104, 92) + ` fill="${mix(fc, '#ffffff', 0.35)}" opacity="0.07"/>`;
    S.bg += circle(132, 100, 62) + ` fill="${mix(fc, '#ffffff', 0.5)}" opacity="0.05"/>`;
    if (o.lod >= 1) {
      // 淡淡的祥云纹
      const cl = mix(fc, '#ffffff', 0.45);
      let d = '';
      const cloud = (x, y, s) => {
        d += `M${N(x)},${N(y)}c${N(-8 * s)},0 ${N(-12 * s)},${N(-10 * s)} ${N(-4 * s)},${N(-14 * s)}c${N(6 * s)},${N(-3 * s)} ${N(12 * s)},${N(2 * s)} ${N(9 * s)},${N(7 * s)}` +
          `M${N(x)},${N(y)}c${N(10 * s)},0 ${N(16 * s)},${N(-12 * s)} ${N(26 * s)},${N(-8 * s)}c${N(8 * s)},${N(3 * s)} ${N(6 * s)},${N(14 * s)} ${N(-2 * s)},${N(12 * s)}c${N(-5 * s)},${N(-1 * s)} ${N(-5 * s)},${N(-7 * s)} 0,${N(-7 * s)}`;
      };
      cloud(36, 70, 1.2); cloud(206, 46, 0.9); cloud(30, 150, 0.8); cloud(214, 132, 1.0);
      S.bg += `<path d="${d}" fill="none" stroke="${cl}" stroke-width="2.2" opacity="0.1"/>`;
    }
  }
  // ======================================================== 绘制：边框 ==
  function drawFrame(S, o) {
    const fc = o.color;
    const lo = o.lod;
    S.defs.push(`<linearGradient id="${S.id('fg')}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${mix(fc, '#ffffff', 0.3)}"/><stop offset="0.45" stop-color="${fc}"/><stop offset="1" stop-color="${mix(fc, '#000000', 0.5)}"/></linearGradient>`);
    S.defs.push(`<linearGradient id="${S.id('gg')}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff1c0"/><stop offset="0.5" stop-color="${GOLD}"/><stop offset="1" stop-color="#8a5a1e"/></linearGradient>`);
    let s = '';
    const bw = lo === 0 ? 13 : 11;
    // 外框：势力色带（挖空中间）
    s += `<path d="M0,0H256V256H0Z M${bw},${bw}V${256 - bw}H${256 - bw}V${bw}Z" fill="url(#${S.id('fg')})" fill-rule="evenodd"/>`;
    // 内阴影
    s += `<rect x="${bw + 1}" y="${bw + 1}" width="${254 - 2 * bw}" height="${254 - 2 * bw}" fill="none" stroke="#000" stroke-opacity="0.5" stroke-width="4"/>`;
    // 色带上的暗纹（回纹意象）
    if (lo >= 2) {
      const dk = mix(fc, '#000', 0.35);
      let d = '';
      const m = bw / 2;
      for (let i = 30; i < 230; i += 14) {
        d += `M${i},${m - 2}h6v4h-3`; d += `M${i},${256 - m + 2}h6v-4h-3`;
        d += `M${m - 2},${i}v6h4v-3`; d += `M${256 - m + 2},${i}v6h-4v-3`;
      }
      s += `<path d="${d}" fill="none" stroke="${dk}" stroke-width="1.1" opacity="0.55"/>`;
    }
    // 金线
    s += `<rect x="1.2" y="1.2" width="253.6" height="253.6" rx="3" fill="none" stroke="${INK}" stroke-width="2.4"/>`;
    s += `<rect x="${bw - 1.2}" y="${bw - 1.2}" width="${258.4 - 2 * bw}" height="${258.4 - 2 * bw}" fill="none" stroke="url(#${S.id('gg')})" stroke-width="${lo === 0 ? 3 : 2.4}"/>`;
    s += `<rect x="${bw - 3}" y="${bw - 3}" width="${262 - 2 * bw}" height="${262 - 2 * bw}" fill="none" stroke="${INK}" stroke-width="1" opacity="0.7"/>`;
    s += `<rect x="3.2" y="3.2" width="249.6" height="249.6" rx="2" fill="none" stroke="#fff" stroke-opacity="0.22" stroke-width="1"/>`;
    if (lo >= 1) {
      // 四角如意云头
      const corner = (x, y, sx, sy) => {
        const t = `translate(${x},${y}) scale(${sx},${sy})`;
        s += `<path transform="${t}" d="M0,0h22c0,0 -2,6 -8,6c3,3 1,9 -4,9c0,5 -6,7 -9,4c-2,4 -1,3 -1,3z" fill="url(#${S.id('gg')})" stroke="${INK}" stroke-width="1.2"/>`;
        s += `<path transform="${t}" d="M5,5c3,-1 6,1 5,4c-1,2 -4,2 -4,0" fill="none" stroke="${INK}" stroke-width="1.1" opacity="0.8"/>`;
      };
      corner(1.5, 1.5, 1, 1); corner(254.5, 1.5, -1, 1); corner(1.5, 254.5, 1, -1); corner(254.5, 254.5, -1, -1);
      // 上下中央宝珠
      const gem = (x, y) => { s += `<path d="M${x},${y - 5}l5,5l-5,5l-5,-5z" fill="url(#${S.id('gg')})" stroke="${INK}" stroke-width="1.1"/>`; };
      gem(128, bw / 2 + 0.5); gem(128, 256 - bw / 2 - 0.5);
    }
    S.frame = s;
    S.win = bw;
  }

  // ======================================================== 绘制：颈 / 头 ==
  function drawNeck(S, G, sp, T) {
    const K = T.skin, R = G.R;
    S.shape('neck', G.neckD, K.s, () => {
      // 迎光的颈前侧
      const P = G.P;
      S.path('neck', spline([P(-0.6, 1.1), P(-0.44 * G.nw, 1.5), P(-0.4 * G.nw, 2.0), P(-0.48, 2.8), P(-0.05, 2.8, 0), P(0.02, 2.0), P(-0.05, 1.5)], true), K.b);
      // 下颌投影
      S.path('neck', spline([P(-0.6, 1.25), P(-0.1, 1.62), P(0.5, 1.6), P(1.1, 1.25), P(1.1, 0.4, 0), P(-0.6, 0.4, 0)], true), K.d, 0, ' opacity="0.75"');
      if (!G.fem && S.o.lod >= 1) {
        // 喉结与胸锁乳突肌
        S.line('neck', spline([P(-0.4 * G.nw, 1.62), P(-0.33 * G.nw, 1.7), P(-0.38 * G.nw, 1.8)]), K.line, 1.2, ' opacity="0.6"');
        S.line('neck', spline([P(0.62, 0.9), P(0.4, 1.6), P(0.05, 2.1)]), K.d, 1.6, ' opacity="0.5"');
      }
    }, S.lw, 'neck');
    // 轮廓光
    rimOn(S, 'neck', G.neckPts, T.rim, 3.2);
  }
  // 轮廓光：形状与其左移副本的偶奇差 → 右缘细月牙
  function rimOn(S, layer, pts, color, w, op) {
    const d = spline(pts, true), d2 = spline(shift(pts, -w, w * 0.25), true);
    const cu = S.clip('rim' + S.defs.length, d);
    S.add(layer, `<g clip-path="${cu}"><path d="${d} ${d2}" fill="${color}" fill-rule="evenodd" opacity="${op || 0.85}"/></g>`);
  }

  function drawHead(S, G, sp, T, X) {
    const K = T.skin, R = G.R, P = G.P, F = G.F, L = G.L;
    const lo = S.o.lod;
    S.add('head', `<path d="${G.headD}" fill="${K.b}"/>`);
    S.add('head', `<g clip-path="${S.clip('hd', G.headD)}">`);
    // 1) 主阴影（背光侧脸颊，明暗交界线绕过颧骨下方）
    const fatK = G.fat;
    const hol = sp.face.hollow || 0;
    // 明暗交界线：年轻、丰润的脸平滑；年长、清瘦的脸在颧骨下内收（骨相）
    const bony = clamp(hol * 0.8 + (sp.age - 28) / 36, 0, 1) * (1 - fatK);
    const term = [
      P(0.46, -1.7, 0), P(0.42, -0.95), P(0.48, -0.55), P(0.53, -0.2), P(0.56, 0.06),
      P(lerp(0.55, 0.5, bony) + fatK * 0.12, 0.3), P(lerp(0.5, 0.34 - hol * 0.1, bony) + fatK * 0.12, 0.5), P(lerp(0.44, 0.24, bony) + fatK * 0.16, 0.72 * L),
      P(lerp(0.36, 0.27, bony) + fatK * 0.12, 0.98 * L), P(lerp(0.22, 0.16, bony), 1.2 * L), P(-0.04, 1.5 * L, 0), P(2, 1.6, 0), P(2, -1.7, 0),
    ];
    S.add('head', `<path d="${spline(term, true)}" fill="${K.s}"/>`);
    // 远侧颊下凹陷（瘦者、老者）
    if (hol > 0.25) S.add('head', `<path d="${spline([P(-0.98, 0.38), P(-0.8, 0.5), P(-0.72, 0.8 * L), P(-0.86, 0.95 * L), P(-1.1, 0.7, 0)], true)}" fill="${K.s}" opacity="${N(clamp(hol, 0, 0.8))}"/>`);
    // 下颌底面
    S.add('head', `<path d="${spline([P(-0.75, 1.28 * L), P(-0.3, 1.36 * L), P(0.3, 1.16 * L), P(0.7, 0.86 * L), P(1.2, 1.0, 0), P(1.0, 1.8, 0), P(-0.8, 1.8, 0)], true)}" fill="${K.d}" opacity="0.55"/>`);
    // 2) 眼窝
    const eN = G.eyeN, eF = G.eyeF;
    S.add('head', `<path d="${spline([F(0.06, -0.24), F(0.3, -0.2), F(0.62, -0.18), F(0.7, -0.05), F(0.5, -0.09), F(0.24, -0.04), F(0.1, 0.12), F(0.02, 0.0)], true)}" fill="${K.s}" opacity="0.9"/>`);
    S.add('head', `<path d="${spline([F(-0.14, -0.2), F(-0.42, -0.2), F(-0.7, -0.16), F(-0.6, -0.06), F(-0.36, -0.08), F(-0.16, -0.02)], true)}" fill="${K.s}" opacity="0.55"/>`);
    // 3) 鼻侧阴影
    const nt = noseGeom(G, sp);
    S.add('head', `<path d="${spline([F(0.04, -0.08), F(0.12, 0.1), F(0.16, 0.36), nt.alaTop, nt.alaBack, F(0.06, 0.4 * L), nt.bridgeMid], true)}" fill="${K.s}"/>`);
    S.add('head', `<path d="${spline([nt.base, add(nt.base, 0.14 * R, 0.02 * R), add(nt.alaBot, 0.08 * R, 0.08 * R), add(nt.base, 0.02 * R, 0.1 * R)], true)}" fill="${K.d}" opacity="0.6"/>`);
    // 4) 高光：额、鼻梁、颧、下巴
    if (lo >= 1) {
      S.add('head', ellipse(G.FX(-0.3, -0.62), G.ey - 0.62 * R, 0.3 * R, 0.13 * R, -8) + ` fill="${K.h}" opacity="0.55"/>`);
      S.add('head', `<path d="${taper([F(-0.06, -0.05), F(-0.04, 0.2, 0.1), F(-0.05, 0.42, 0.22)], t => 0.06 * R * Math.sin(Math.PI * t) + 0.3)}" fill="${K.h}" opacity="0.8"/>`);
      S.add('head', ellipse(G.FX(-0.72, 0.2), G.ey + 0.22 * R, 0.13 * R, 0.07 * R, -30) + ` fill="${K.h}" opacity="0.55"/>`);
      S.add('head', ellipse(G.FX(-0.12, 1.22 * L, 0.08), G.ey + 1.24 * L * R, 0.12 * R, 0.06 * R, -10) + ` fill="${K.h}" opacity="0.5"/>`);
    }
    // 5) 腮红
    const bl = N(clamp((G.fem ? 0.32 : sp.age > 55 ? 0.12 : 0.2) * (sp.blush || 1) * (S.o.mood === 'angry' ? 2.2 : 1), 0, 0.7));
    S.add('head', ellipse(G.FX(0.3, 0.42), G.ey + 0.42 * R, 0.26 * R, 0.13 * R, -10) + ` fill="${K.blush}" opacity="${bl}"/>`);
    S.add('head', ellipse(G.FX(-0.7, 0.42), G.ey + 0.42 * R, 0.12 * R, 0.1 * R) + ` fill="${K.blush}" opacity="${N(bl * 0.8)}"/>`);
    if (fatK > 0.45) {
      // 双下巴：下颌之下再垂一层肉
      const dc = [P(-0.66 - fatK * 0.1, 1.05 * L), P(-0.52, 1.5 * L + fatK * 0.12), P(-0.12, 1.68 * L + fatK * 0.14), P(0.4, 1.52 * L + fatK * 0.1), P(0.72, 1.05 * L, 0)];
      const dcD = spline(dc, true);
      S.add('body2', `<path d="${dcD}" fill="${K.s}"/><g clip-path="${S.clip('dchin', dcD)}"><path d="${spline([P(-0.9, 1.0), P(-0.5, 1.45 * L), P(-0.15, 1.58 * L), P(-0.1, 1.2 * L)], true)}" fill="${K.b}" opacity="0.8"/></g>` +
        `<path d="${spline(dc.slice(0, 4))}" fill="none" stroke="${INK}" stroke-width="${N(S.lw * 0.85)}"/>`);
    }
    if (fatK > 0.35 && lo >= 1) {
      S.add('head', `<path d="${spline([P(-0.62 * G.cw, 1.3 * L + fatK * 0.05), P(-0.3, 1.42 * L + fatK * 0.04), P(0.2, 1.3 * L + fatK * 0.06)])}" fill="none" stroke="${K.line}" stroke-width="1.3" opacity="${N(clamp(fatK, 0, 0.8))}"/>`);
      S.add('head', `<path d="${spline([P(0.42, 0.62), P(0.5, 0.95 * L), P(0.36, 1.18 * L)])}" fill="none" stroke="${K.line}" stroke-width="1.1" opacity="${N(clamp(fatK * 0.7, 0, 0.6))}"/>`);
    }
    S.add('head', '%HEADSHADE%');
    S.add('head', '</g>');
    S.add('head', `<path d="${G.headD}" fill="none" stroke="${INK}" stroke-width="${N(S.lw)}"/>`);
    rimOn(S, 'head', G.headPts, T.rim, 2.6, 0.8);
  }

  // ---------------------------------------------------------------- 耳 --
  function drawEar(S, G, sp, T) {
    if (G.hideEar) return;
    const K = T.skin, R = G.R, P = G.P;
    const x0 = G.ear.u0, x1 = G.ear.u1, y0 = G.ear.v0, y1 = G.ear.v1;
    const w = x1 - x0, h = y1 - y0;
    const E = (u, v, k) => P(x0 + u * w, y0 + v * h, k);
    const outer = [E(0.08, 0.12), E(0.45, -0.02), E(0.92, 0.12), E(1.0, 0.42), E(0.85, 0.72), E(0.62, 0.92), E(0.38, 1.0), E(0.16, 0.9), E(0.08, 0.7), E(-0.05, 0.4)];
    const d = spline(outer, true);
    S.shape('ear', d, K.s, () => {
      S.path('ear', spline([E(0.25, 0.22), E(0.6, 0.18), E(0.78, 0.4), E(0.62, 0.62), E(0.4, 0.68), E(0.3, 0.5)], true), K.d);
      S.path('ear', spline([E(0.18, 0.12), E(0.5, 0.03), E(0.86, 0.16), E(0.94, 0.42), E(0.8, 0.7), E(0.88, 0.42), E(0.78, 0.2), E(0.5, 0.12)], true), K.b, 0, ' opacity="0.9"');
      S.path('ear', spline([E(0.35, 0.78), E(0.6, 0.86), E(0.45, 0.96), E(0.25, 0.9)], true), K.b, 0, ' opacity="0.6"');
    }, S.lw * 0.8);
    S.line('ear', spline([E(0.36, 0.3), E(0.62, 0.32), E(0.62, 0.55), E(0.45, 0.7)]), K.line, 1.1, ' opacity="0.8"');
  }

  // ---------------------------------------------------------------- 眼 --
  const EYE = {
    normal: { w: 1, h: 0.17, tilt: 0.06, low: 0.42, iris: 0.82, crease: 1 },
    sharp: { w: 1.02, h: 0.14, tilt: 0.22, low: 0.32, iris: 0.86, crease: 0.8, angular: 1 },
    phoenix: { w: 1.14, h: 0.12, tilt: 0.42, low: 0.28, iris: 0.92, crease: 0.6, angular: 1 },
    round: { w: 1.0, h: 0.24, tilt: 0.04, low: 0.6, iris: 0.5, crease: 1.2, round: 1 },
    narrow: { w: 1.04, h: 0.1, tilt: 0.12, low: 0.25, iris: 1.0, crease: 0.4, heavy: 1 },
    gentle: { w: 1.0, h: 0.15, tilt: -0.12, low: 0.18, iris: 0.92, crease: 1, smile: 1 },
    big: { w: 1.1, h: 0.2, tilt: 0.14, low: 0.5, iris: 0.78, crease: 1.15, lash: 1 },
    sleepy: { w: 1.0, h: 0.12, tilt: -0.06, low: 0.35, iris: 0.95, crease: 0.9, heavy: 1 },
  };
  function eyeMood(base, mood, isNear, fat) {
    const e = Object.assign({}, base);
    e.innerDrop = 0; e.squint = 1;
    if (fat > 0.4) { e.h *= 1 - (fat - 0.4) * 0.45; e.low *= 0.6; }
    if (mood === 'angry') { e.innerDrop = 0.62; e.h *= 0.95; e.iris = Math.min(e.iris, 0.68); }
    else if (mood === 'hurt') { e.h *= isNear ? 0.22 : 0.7; e.innerDrop = -0.3; }
    else if (mood === 'win') { e.smile = 1; e.low = Math.min(e.low, 0.15); e.h *= 0.85; }
    return e;
  }
  function drawEyes(S, G, sp, T, mood) {
    const base = EYE[sp.eyes.shape] || EYE.normal;
    const patch = (sp.acc || []).includes('eyepatch');
    for (const which of ['F', 'N']) {
      const g = which === 'N' ? G.eyeN : G.eyeF;
      const ex = eyeMood(base, mood, which === 'N', sp.fat || 0);
      if (which === 'N' && patch) { drawEyePatch(S, G, sp, T, g); continue; }
      drawEye(S, G, sp, T, g, ex, which === 'F');
    }
  }
  function drawEye(S, G, sp, T, g, ex, far) {
    const K = T.skin, R = G.R, lo = S.o.lod;
    const dir = g.dir, w = g.w * ex.w, h = ex.h * R * (sp.eyes.size || 1);
    const cy = G.ey + G.eyeDy + (far ? 0.01 * R : 0);
    const cx = g.cx;
    const inner = [cx - dir * w / 2, cy + h * 0.12];
    const outer = [cx + dir * w / 2, cy - h * ex.tilt * 1.6];
    const at = (t, dy) => { const p = lp(inner, outer, t); return [p[0], p[1] + dy]; };
    const up = ex.round ? 1.05 : 0.95;
    const drop = (ex.innerDrop || 0) * h;
    const U = [inner, at(0.18, -h * up * 0.72 + drop * 0.8, 1), at(0.45, -h * up + drop * 0.35), at(0.78, -h * up * (ex.angular ? 0.95 : 0.8)), outer];
    const D = [outer, at(0.72, h * ex.low * 1.1), at(0.4, h * ex.low * (ex.smile ? 0.4 : 1.0)), at(0.12, h * ex.low * 0.5), inner];
    if (ex.smile) { D[1] = at(0.7, h * ex.low * 0.2 - h * 0.12); D[2] = at(0.4, -h * 0.05); }
    const eyeD = spline(U.concat(D.slice(1, -1)), true);
    const cid = 'eye' + (far ? 'f' : 'n');
    S.add('face', `<path d="${eyeD}" fill="#f1eadf"/>`);
    S.add('face', `<g clip-path="${S.clip(cid, eyeD)}">`);
    // 上睑投影
    S.add('face', `<path d="${spline(shift(U, 0, h * 0.4), false)}L${ps(add(outer, 0, -h * 2))}L${ps(add(inner, 0, -h * 2))}Z" fill="#b8a496"/>`);
    // 虹膜
    const ri = h * ex.iris * (ex.round ? 1.42 : 0.9) * (far ? 0.92 : 1);
    const ix = cx + dir * w * 0.02 + (far ? w * 0.06 : w * 0.04), iy = cy - h * (ex.round ? 0.06 : 0.02);
    const ic = sp.eyes.color || '#2a1810';
    const iw = far ? 0.78 : 0.94;
    S.add('face', ellipse(ix, iy, ri * iw, ri) + ` fill="${mix(ic, '#7a5a40', 0.25)}"/>`);
    S.add('face', ellipse(ix, iy - ri * 0.25, ri * iw * 0.95, ri * 0.75) + ` fill="${mul(ic, 0.6)}"/>`);
    S.add('face', ellipse(ix, iy, ri * iw * 0.5, ri * 0.52) + ` fill="#0a0608"/>`);
    S.add('face', ellipse(ix, iy, ri * iw, ri) + ` fill="none" stroke="${mul(ic, 0.4)}" stroke-width="0.9"/>`);
    S.add('face', ellipse(ix - ri * 0.38, iy - ri * 0.4, ri * 0.28, ri * 0.24) + ' fill="#fff" opacity="0.92"/>');
    if (lo >= 1) S.add('face', circle(ix + ri * 0.35, iy + ri * 0.35, ri * 0.12) + ' fill="#fff" opacity="0.7"/>');
    S.add('face', '</g>');
    // 双眼皮褶 / 上睑
    const lwk = S.lw / 2.6;
    if (ex.crease > 0.3 && !(ex.heavy && sp.eyes.lid > 0.8)) {
      const cr = shift(U.slice(1, 4), dir * w * 0.02, -h * (0.42 + ex.crease * 0.18));
      S.line('face', spline(cr), K.line, 1.0 * lwk, ' opacity="0.65"');
    }
    if (ex.heavy) {
      // 厚重眼睑：上睑压低
      S.add('face', `<path d="${spline(U, false)}L${ps(add(outer, 0, -h * 0.7))}L${ps(at(0.45, -h * 1.5))}L${ps(add(inner, 0, -h * 0.6))}Z" fill="${K.s}"/>`);
    }
    // 上睑线（粗）
    const fem = sp.sex === 'f';
    const lidW = (fem ? 3.3 : 2.3) * lwk;
    const ext = add(outer, dir * w * (fem ? 0.22 : 0.08), fem ? -h * 0.55 : h * 0.12);
    const lid = U.concat([ext]);
    S.add('face', `<path d="${taper(lid, t => (t < 0.15 ? lerp(0.5, 1.4, t / 0.15) : t > 0.85 ? lerp(lidW, 0.2, (t - 0.85) / 0.15) : lerp(1.4, lidW, (t - 0.15) / 0.7)))}" fill="${INK}"/>`);
    if (fem && lo >= 1) {
      // 睫毛
      let ld = '';
      for (let i = 0; i < 3; i++) { const p = lp(U[2], outer, 0.35 + i * 0.3); ld += `M${ps(p)}l${N(dir * w * (0.06 + i * 0.03))},${N(-h * (0.45 + i * 0.12))}`; }
      S.line('face', ld, INK, 1.1 * lwk);
    }
    // 下睑
    S.line('face', spline(D.slice(0, 3)), fem ? INK : K.line, (fem ? 1.4 : 1.0) * lwk, fem ? ' opacity="0.55"' : ' opacity="0.75"');
    // 眼袋 / 鱼尾纹（年长）
    if (sp.age >= 44 && lo >= 1) {
      const a = clamp((sp.age - 40) / 30, 0.3, 0.9);
      S.line('face', spline([at(0.15, h * 0.9), at(0.5, h * 1.5), at(0.85, h * 1.0)]), K.line, 0.9 * lwk, ` opacity="${N(a)}"`);
      if (sp.age >= 52) {
        for (let i = 0; i < 2; i++) S.line('face', spline([add(outer, dir * w * 0.12, -h * 0.2 + i * h * 0.6), add(outer, dir * w * 0.3, -h * 0.4 + i * h * 0.9)]), K.line, 0.8 * lwk, ` opacity="${N(a * 0.8)}"`);
      }
    }
  }
  function drawEyePatch(S, G, sp, T, g) {
    const R = G.R, cx = g.cx, cy = G.ey;
    // 系带
    S.line('face', spline([[G.FX(-0.9, -0.62), G.ey - 0.62 * R], [cx, cy - 0.3 * R], [G.hx + 0.95 * R, G.ey - 0.08 * R]]), INK, 2.6);
    S.line('face', spline([[G.FX(-0.9, -0.62), G.ey - 0.62 * R], [cx, cy - 0.3 * R], [G.hx + 0.95 * R, G.ey - 0.08 * R]]), '#3a2a22', 1.4);
    const d = spline([[cx - 0.28 * R, cy - 0.14 * R], [cx + 0.02 * R, cy - 0.24 * R], [cx + 0.3 * R, cy - 0.12 * R], [cx + 0.26 * R, cy + 0.16 * R], [cx - 0.02 * R, cy + 0.24 * R], [cx - 0.24 * R, cy + 0.12 * R]], true);
    S.shape('face', d, '#2a201c', () => {
      S.add('face', ellipse(cx - 0.06 * R, cy - 0.06 * R, 0.16 * R, 0.09 * R, -12) + ' fill="#5a4a40" opacity="0.8"/>');
    }, S.lw * 0.85);
  }

  // ---------------------------------------------------------------- 眉 --
  const BROW = {
    straight: { y0: -0.27, ym: -0.33, y1: -0.3, w: [0.09, 0.08, 0.04] },
    arched: { y0: -0.26, ym: -0.38, y1: -0.28, w: [0.07, 0.07, 0.03] },
    angled: { y0: -0.24, ym: -0.36, y1: -0.42, w: [0.11, 0.09, 0.03], sharp: 1 },
    bushy: { y0: -0.26, ym: -0.35, y1: -0.3, w: [0.14, 0.13, 0.07], bushy: 1 },
    thin: { y0: -0.28, ym: -0.37, y1: -0.3, w: [0.05, 0.045, 0.015] },
    knit: { y0: -0.2, ym: -0.33, y1: -0.36, w: [0.13, 0.1, 0.04], knit: 1 },
    silkworm: { y0: -0.27, ym: -0.42, y1: -0.36, w: [0.08, 0.15, 0.05], sharp: 1 },
    sword: { y0: -0.25, ym: -0.36, y1: -0.46, w: [0.1, 0.1, 0.02], sharp: 1 },
    worried: { y0: -0.36, ym: -0.36, y1: -0.26, w: [0.08, 0.07, 0.03] },
  };
  function drawBrows(S, G, sp, T, mood) {
    let b = Object.assign({}, BROW[sp.brows.shape] || BROW.straight);
    const th = sp.brows.thick || 1;
    let inD = 0;
    if (mood === 'angry') inD = 0.17;
    else if (mood === 'hurt') inD = -0.15;
    else if (sp.expr === 'fierce') inD = 0.05;
    else if (mood === 'win') inD = -0.07;
    const col = mix(sp.brows.color || '#1a1414', INK, 0.15);
    const old = sp.age >= 58;
    for (const side of [-1, 1]) {
      const u0 = side * 0.08, u1 = side * 0.82, um = side * 0.5;
      const by = (sp.brows.y || 0) + (sp.eyes.y || 0) * 0.8;
      const yy = (y, u) => [G.FX(u, y), G.ey + (y + by + (Math.abs(u) < 0.2 ? inD : 0)) * G.R];
      const pts = [yy(b.y0, u0), yy(lerp(b.y0, b.ym, 0.7), side * 0.28), yy(b.ym, um), yy(b.y1, u1)];
      const sc = side > 0 ? 1 : 0.85;
      const ws = b.w.map(v => v * G.R * th * sc);
      const wf = t => (t < 0.5 ? lerp(ws[0], ws[1], t * 2) : lerp(ws[1], ws[2], (t - 0.5) * 2) * (b.sharp && t > 0.9 ? (1 - t) * 10 : 1));
      const d = taper(pts, wf);
      S.add('face', `<path d="${d}" fill="${col}"/>`);
      if ((b.bushy || old) && S.o.lod >= 1) {
        // 眉毛的毛流
        let hd = '';
        for (let i = 0; i < 7; i++) {
          const t = (i + 0.5) / 7, p = lp(pts[Math.floor(t * 3)], pts[Math.min(3, Math.floor(t * 3) + 1)], (t * 3) % 1);
          const wv = wf(t);
          hd += 'M' + ps([p[0] - side * 1, p[1] + wv * 0.5]) + 'L' + ps([p[0] + side * 3.2, p[1] - wv * 0.9]);
        }
        S.line('face', hd, old ? mix(col, '#fff', 0.4) : col, 1.2);
      }
    }
    if ((b.knit || mood === 'angry' || sp.expr === 'fierce') && S.o.lod >= 1) {
      // 眉间竖纹
      const x = G.FX(0, -0.2), y = G.ey - 0.22 * G.R;
      S.line('face', `M${N(x - 1)},${N(y - 4)}q1.5,4 0,8M${N(x + 3)},${N(y - 3)}q1,3 0,6`, T.skin.line, 1.1, ' opacity="0.7"');
    }
  }

  // ---------------------------------------------------------------- 鼻 --
  const NOSE = {
    straight: { len: 1, dz: 0.32, hook: 0, tip: 1, w: 1 },
    aquiline: { len: 1.06, dz: 0.4, hook: 1, tip: 0.95, w: 1 },
    broad: { len: 0.96, dz: 0.28, hook: 0, tip: 1.25, w: 1.25 },
    button: { len: 0.9, dz: 0.24, hook: -0.3, tip: 1.1, w: 0.95 },
    small: { len: 0.86, dz: 0.24, hook: -0.2, tip: 0.85, w: 0.85 },
    bulb: { len: 1.0, dz: 0.34, hook: 0.2, tip: 1.5, w: 1.3 },
    flat: { len: 0.9, dz: 0.2, hook: -0.2, tip: 1.3, w: 1.4 },
  };
  function noseGeom(G, sp) {
    const n = NOSE[sp.nose.shape] || NOSE.straight;
    const s = sp.nose.size || 1, R = G.R, F = G.F;
    const ny = G.noseY * n.len;
    const dz = n.dz * s * (sp.nose.bridge != null ? 0.75 + sp.nose.bridge * 0.5 : 1);
    const nw = (sp.nose.w || 1) * n.w;
    const top = F(0.0, -0.12, 0.05);
    const tip = F(-0.02, ny - 0.04, dz);
    const bridgeMid = F(0.0, ny * 0.45, dz * 0.55 + n.hook * 0.07);
    const base = F(0.02, ny + 0.06, dz * 0.3);
    const alaTop = F(0.14 * nw, ny - 0.1, 0.06);
    const alaBack = F(0.21 * nw, ny + 0.0, 0.0);
    const alaBot = F(0.15 * nw, ny + 0.07, 0.04);
    const nostril = F(0.06 * nw, ny + 0.05, dz * 0.22);
    return { n, s, ny, dz, top, tip, bridgeMid, base, alaTop, alaBack, alaBot, nostril, nw };
  }
  function drawNose(S, G, sp, T) {
    const K = T.skin, R = G.R, lo = S.o.lod;
    const g = noseGeom(G, sp);
    const lwk = S.lw / 2.6;
    // 鼻梁远侧轮廓线（下半段为主）
    const br = G.fem ? [lp(g.bridgeMid, g.tip, 0.35), lp(g.bridgeMid, g.tip, 0.75), add(g.tip, -0.01 * R, 0.0)] : [lp(g.top, g.bridgeMid, 0.45), g.bridgeMid, lp(g.bridgeMid, g.tip, 0.6), add(g.tip, -0.01 * R, 0.0)];
    S.add('face', `<path d="${taper(br, t => lerp(0.2, (G.fem ? 1.4 : 1.9) * lwk, t * t))}" fill="${K.line}" opacity="${G.fem ? 0.6 : 0.85}"/>`);
    // 鼻尖与鼻底
    const tipR = 0.09 * R * g.n.tip;
    const tipCurve = [add(g.tip, -0.005 * R, -tipR * 0.6), add(g.tip, -tipR * 0.35, tipR * 0.25), add(g.tip, tipR * 0.3, tipR * 0.75), lp(g.tip, g.base, 0.7), g.base];
    S.add('face', `<path d="${taper(tipCurve, t => 1.9 * lwk * (1 - t * 0.55))}" fill="${INK}" opacity="0.9"/>`);
    // 鼻翼
    S.line('face', spline([g.alaTop, add(g.alaBack, 0.02 * R, -0.02 * R), g.alaBot, add(g.nostril, 0.04 * R, 0.03 * R)]), K.line, 1.5 * lwk);
    // 鼻孔
    S.add('face', ellipse(g.nostril[0], g.nostril[1], 0.055 * R * g.nw, 0.03 * R, 18) + ` fill="${K.d}"/>`);
    S.add('face', ellipse(g.nostril[0] + 0.01 * R, g.nostril[1] + 0.005 * R, 0.035 * R * g.nw, 0.018 * R, 18) + ` fill="${INK}" opacity="0.85"/>`);
    // 鼻尖高光
    if (lo >= 1) S.add('face', ellipse(g.tip[0] - 0.02 * R, g.tip[1] - 0.04 * R, 0.035 * R * g.n.tip, 0.025 * R, -20) + ` fill="${K.h}" opacity="0.9"/>`);
    G.noseG = g;
  }

  // ---------------------------------------------------------------- 口 --
  function drawMouth(S, G, sp, T, mood) {
    const K = T.skin, R = G.R, F = G.F, lo = S.o.lod;
    const lwk = S.lw / 2.6;
    const my = G.mouthY, mw = (sp.mouth.w || 1);
    let shape = sp.mouth.shape || 'neutral';
    if (sp.expr === 'smile') shape = 'smile';
    if (sp.expr === 'sly') shape = 'smirk';
    if (sp.expr === 'stern' || sp.expr === 'fierce') shape = 'grim';
    if (mood === 'angry') shape = 'shout';
    else if (mood === 'hurt') shape = 'clench';
    else if (mood === 'win') shape = 'grin';
    const cdy = { neutral: 0.0, smile: -0.06, grim: 0.05, shout: 0.06, clench: 0.03, grin: -0.08, smirk: 0 }[shape] || 0;
    const cf = F(-0.34 * mw, my + cdy, 0.05), cn = F(0.34 * mw, my + cdy, 0.0);
    const cc = F(-0.02, my - 0.005, 0.12);
    const fem = G.fem;
    const lipT = (sp.mouth.lips || 1) * 0.1 * R;
    const lipCol = fem ? mix(K.b, '#c23848', 0.55) : K.lip;
    if (shape === 'shout' || shape === 'grin' || shape === 'clench') {
      const open = shape === 'shout' ? 0.4 : shape === 'grin' ? 0.19 : 0.11;
      const top = [cf, F(-0.18 * mw, my - 0.05, 0.1), F(0, my - 0.05, 0.12), F(0.2 * mw, my - 0.04, 0.06), cn];
      const bot = [cn, F(0.18 * mw, my + open * 0.8, 0.06), F(0, my + open, 0.12), F(-0.18 * mw, my + open * 0.85, 0.1)];
      const md = spline(top.concat(bot), true);
      S.add('face', `<path d="${md}" fill="#3a1016"/>`);
      S.add('face', `<g clip-path="${S.clip('mo', md)}">`);
      S.add('face', `<path d="${spline(shift(top, 0, 0.07 * R), false)}L${ps(add(cn, 0, -0.1 * R))}L${ps(add(cf, 0, -0.1 * R))}Z" fill="#efe6d8"/>`);
      if (shape !== 'shout') S.add('face', `<path d="${spline(shift(bot, 0, -0.06 * R), false)}L${ps(add(cf, 0, 0.2 * R))}L${ps(add(cn, 0, 0.2 * R))}Z" fill="#e2d8c8"/>`);
      else S.add('face', ellipse(G.FX(0, my + open * 0.75, 0.1), G.ey + (my + open * 0.75) * R, 0.16 * R, 0.07 * R) + ' fill="#a8404a"/>');
      if (lo >= 1) {
        let td = '';
        for (const u of [-0.12, 0.04, 0.18]) td += 'M' + ps(F(u * mw, my - 0.05, 0.1)) + 'L' + ps(F(u * mw, my + 0.03, 0.1));
        S.line('face', td, '#b0a090', 0.8);
      }
      S.add('face', '</g>');
      S.add('face', `<path d="${md}" fill="none" stroke="${INK}" stroke-width="${N(1.6 * lwk)}"/>`);
      // 下唇
      S.line('face', spline([F(-0.18 * mw, my + open + 0.09, 0.08), F(0.0, my + open + 0.11, 0.1), F(0.16 * mw, my + open + 0.08, 0.04)]), K.line, 1.1 * lwk, ' opacity="0.7"');
    } else {
      const mid = shape === 'smirk' ? F(0.16 * mw, my - 0.03, 0.06) : F(0.17 * mw, my + cdy * 0.3, 0.06);
      const line = [cf, F(-0.17 * mw, my + cdy * 0.3 + 0.01, 0.1), cc, mid, cn];
      // 上唇（阴影面）
      const ul = [cf, F(-0.16 * mw, my - lipT / R * 0.9, 0.1), F(-0.03, my - lipT / R * 0.7, 0.13), F(0.02, my - lipT / R * 0.95, 0.12), F(0.2 * mw, my - lipT / R * 0.8, 0.05), cn];
      S.add('face', `<path d="${spline(ul.concat(line.slice(1, -1).reverse()), true)}" fill="${fem ? mul(lipCol, 0.8) : K.lip}" opacity="${fem ? 1 : 0.75}"/>`);
      // 下唇
      const ll = [cn, F(0.16 * mw, my + lipT / R * 1.3, 0.05), F(-0.04, my + lipT / R * 1.5, 0.12), F(-0.2 * mw, my + lipT / R * 1.1, 0.09), cf];
      S.add('face', `<path d="${spline(line.slice(1, -1).concat(ll), true)}" fill="${fem ? lipCol : mix(K.b, K.lip, 0.45)}"/>`);
      if (lo >= 1) S.add('face', ellipse(G.FX(-0.06, my + 0.09, 0.1), G.ey + (my + lipT / R * 0.8) * R, 0.09 * R, 0.025 * R, -6) + ` fill="${K.h}" opacity="${fem ? 0.75 : 0.5}"/>`);
      // 唇下阴影
      S.add('face', `<path d="${spline([F(-0.16 * mw, my + 0.19, 0.08), F(0, my + 0.22, 0.1), F(0.14 * mw, my + 0.19, 0.04), F(0.02, my + 0.27, 0.1)], true)}" fill="${K.s}" opacity="0.6"/>`);
      // 口缝
      S.add('face', `<path d="${taper(line, t => (0.5 + 1.5 * Math.sin(Math.PI * clamp(t * 1.1, 0, 1))) * lwk)}" fill="${INK}"/>`);
      // 口角
      S.line('face', spline([add(cn, -0.02 * R, -0.01 * R), add(cn, 0.04 * R, (cdy > 0 ? 0.05 : -0.03) * R)]), K.line, 1.1 * lwk);
    }
    // 人中
    if (lo >= 1) S.line('face', spline([F(0.04, G.noseY + 0.12, 0.2), F(0.05, my - 0.12, 0.13)]), K.line, 0.9 * lwk, ' opacity="0.45"');
    // 法令纹
    let nl = sp.age >= 30 ? clamp((sp.age - 26) / 30, 0.15, 0.8) : (shape === 'grin' || shape === 'shout' ? 0.3 : shape === 'smile' && sp.age >= 24 ? 0.2 : 0);
    if (fem) nl = sp.age >= 45 ? nl * 0.5 : 0;
    if (nl > 0 && lo >= 1) {
      const ng = G.noseG;
      S.line('face', spline([add(ng.alaBack, 0.04 * R, -0.04 * R), F(0.42, G.noseY + 0.2, 0), F(0.46 * mw, my + 0.08, 0), F(0.4 * mw, my + 0.3, 0)]), K.line, 1.2 * lwk, ` opacity="${N(nl)}"`);
      if (sp.age >= 45) S.line('face', spline([F(-0.36, G.noseY + 0.1, 0.05), F(-0.52, my, 0.03), F(-0.48, my + 0.22, 0.02)]), K.line, 1.0 * lwk, ` opacity="${N(nl * 0.7)}"`);
    }
    // 下巴沟
    if (lo >= 1 && !fem && sp.age >= 40) S.line('face', spline([F(-0.16, G.chinY - 0.2, 0.08), F(-0.06, G.chinY - 0.17, 0.1), F(0.04, G.chinY - 0.19, 0.08)]), K.line, 0.8 * lwk, ' opacity="0.3"');
  }
  // 皱纹（额头等）
  function drawWrinkles(S, G, sp, T) {
    if (sp.age < 46 || S.o.lod < 1) return;
    const a = clamp((sp.age - 42) / 28, 0.25, 0.8), K = T.skin, R = G.R, F = G.F;
    let d = '';
    for (let i = 0; i < (sp.age > 60 ? 3 : 2); i++) {
      const y = -0.56 - i * 0.12;
      d += spline([F(-0.62, y + 0.03), F(-0.3, y - 0.02), F(0.0, y + 0.01), F(0.28, y - 0.01)]);
    }
    S.headShade.push(`<path d="${d}" fill="none" stroke="${K.line}" stroke-width="1" opacity="${N(a)}"/>`);
  }
  // 疤痕与刺青等
  function drawMarks(S, G, sp, T, mood) {
    const R = G.R, F = G.F;
    for (const m of sp.marks || []) {
      if (m === 'scar') S.line('face', spline([F(-0.7, 0.1), F(-0.6, 0.35), F(-0.5, 0.6)]), '#a04848', 1.6, ' opacity="0.8"');
      if (m === 'mole') S.add('face', circle(G.FX(-0.3, 0.75), G.ey + 0.72 * R, 1.3) + ` fill="${T.skin.line}"/>`);
      if (m === 'scars') {
        S.line('face', spline([F(0.1, -0.7), F(0.3, -0.45), F(0.42, -0.2)]) + spline([F(-0.62, 0.45), F(-0.42, 0.62)]) + spline([F(0.3, 0.6), F(0.45, 0.95)]), '#a04848', 1.5, ' opacity="0.8"');
        S.line('body2', spline([G.P(0.6, 2.4), G.P(1.4, 2.9)]) + spline([G.P(-1.6, 2.6), G.P(-1.0, 3.2)]), '#a04848', 1.6, ' opacity="0.7"');
      }
      if (m === 'scales' && S.o.lod >= 1) {
        let d = '';
        for (let r = 0; r < 3; r++) for (let i = 0; i < 4; i++) { const p = F(-0.75 + i * 0.12 + (r % 2) * 0.06, 0.15 + r * 0.14); d += `M${N(p[0] - 2.5)},${N(p[1])}q2.5,3 5,0`; }
        for (let r = 0; r < 3; r++) for (let i = 0; i < 4; i++) { const p = G.P(0.2 + i * 0.2 + (r % 2) * 0.1, 1.3 + r * 0.18); d += `M${N(p[0] - 3)},${N(p[1])}q3,3.5 6,0`; }
        S.line('face', d, '#3a4a2a', 1.1, ' opacity="0.7"');
      }
    }
    if (mood === 'hurt') {
      // 颊上刀痕、额角流血、冷汗
      S.line('face', spline([F(-0.8, 0.12), F(-0.6, 0.36)]), '#9a1a1a', 2.6);
      S.line('face', spline([F(-0.79, 0.13), F(-0.61, 0.35)]), '#ff8070', 0.9);
      const b0 = F(-0.62, -0.62);
      S.add('face', `<path d="${taper([b0, add(b0, -0.04 * R, 0.25 * R), add(b0, 0.0, 0.5 * R), add(b0, -0.05 * R, 0.78 * R)], t => (0.09 - 0.05 * t) * R)}" fill="#a81c1c" opacity="0.9"/>`);
      const x = G.FX(0.7, -0.55), y = G.ey - 0.6 * R;
      S.add('top', `<path d="M${N(x)},${N(y)}q5,8 0,11q-5,-3 0,-11z" fill="#cfeaff" stroke="${INK}" stroke-width="1"/>`);
    }
  }

  // ======================================================== 发绺 / 须绺 ==
  // “描边垫底”画法：所有发绺先用粗墨线描一遍，再逐个填色 → 并集只留外轮廓。
  // items: [{ d, fill, op }]；lw 为外轮廓线宽
  function clumps(S, layer, items, lw) {
    if (!items.length) return;
    S.add(layer, `<path d="${items.map(i => i.d).join('')}" fill="none" stroke="${INK}" stroke-width="${N(lw * 2)}"/>`);
    for (const it of items) S.add(layer, `<path d="${it.d}" fill="${it.fill}"` + (it.op ? ` opacity="${it.op}"` : '') + '/>');
  }
  // 在折线上按弧长均匀取 n 个点
  function resample(pts, n) {
    const segs = [];
    let tot = 0;
    for (let i = 0; i < pts.length - 1; i++) { const l = hyp(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]); segs.push(l); tot += l; }
    const out = [];
    for (let k = 0; k < n; k++) {
      let d = tot * k / (n - 1), i = 0;
      while (i < segs.length - 1 && d > segs[i]) { d -= segs[i]; i++; }
      out.push(lp(pts[i], pts[i + 1], segs[i] ? clamp(d / segs[i], 0, 1) : 0));
    }
    return out;
  }
  // 外法线（顺时针绕行：右侧向下 → 底部向左 → 左侧向上）
  function normals(pts) {
    return pts.map((p, i) => {
      const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)];
      const dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy) || 1;
      return [dy / l, -dx / l];
    });
  }

  // ======================================================== 胡须 ==
  // 下颌一圈：近侧鬓角 → 下颌角 → 下巴 → 远侧颊
  function jawRing(G) {
    const P = G.P, L = G.L, jw = G.jw, cw = G.cw, fat = G.fat;
    return [
      P(0.5, -0.06), P(0.6, 0.36), P(0.66 * jw + fat * 0.12, 0.86 * L), P(0.3 * jw, 1.23 * L + fat * 0.05),
      P(-0.06, 1.43 * L + fat * 0.1), P(-0.34 * cw, 1.42 * L), P(-0.54 * cw, 1.31 * L), P(-0.68 * cw - (jw - 1) * 0.15 - fat * 0.12, 1.12 * L),
      P(-0.84 - (jw - 1) * 0.25 - fat * 0.14, 0.86 * L), P(-0.92, 0.6),
    ];
  }
  // 颊上的须线（从远侧到近侧，绕开嘴唇）
  function beardInner(G, sp, hi) {
    const P = G.P, F = G.F, my = G.mouthY, mw = sp.mouth.w || 1;
    const up = hi ? -0.12 : 0;
    return [P(-0.92, 0.56 + up), F(-0.52 * mw, my + 0.02, 0.02), F(-0.16, my + 0.22, 0.08), F(0.14, my + 0.2, 0.04), F(0.48 * mw, my - 0.02, 0), P(0.34, 0.42 + up), P(0.44, -0.02)];
  }
  function drawMustache(S, G, sp, T) {
    const st = sp.mustache.style;
    if (!st || st === 'none') return;
    const H = hairT(sp.beard.color || sp.hair.color);
    const R = G.R, F = G.F, my = G.mouthY, ny = G.noseY, mw = sp.mouth.w || 1;
    const lwk = S.lw / 2.6;
    const items = [];
    for (const side of [-1, 1]) {
      const sc = side > 0 ? 1 : 0.64;
      const r0 = F(side * 0.04, ny + 0.13, 0.2);
      const fill = side < 0 ? H.b : H.s;
      if (st === 'thin') {
        items.push({ d: taper([F(side * 0.07, ny + 0.17, 0.18), F(side * 0.26, my - 0.1, 0.1), F(side * 0.44 * mw, my + 0.03, 0.03), F(side * 0.52 * mw, my + 0.24, 0)], t => (1 - t) * 0.075 * R * sc + 0.25), fill });
      } else if (st === 'droop') {
        items.push({ d: taper([r0, F(side * 0.24, my - 0.1, 0.12), F(side * 0.42 * mw, my + 0.02, 0.04), F(side * 0.48 * mw, my + 0.32, 0), F(side * 0.46 * mw, my + 0.6, 0)], t => (t < 0.3 ? lerp(0.08, 0.14, t / 0.3) : lerp(0.14, 0.01, (t - 0.3) / 0.7)) * R * sc), fill });
      } else if (st === 'curl') {
        items.push({ d: taper([r0, F(side * 0.24, my - 0.12, 0.12), F(side * 0.46 * mw, my - 0.05, 0.03), F(side * 0.58 * mw, my - 0.2, 0), F(side * 0.52 * mw, my - 0.3, 0)], t => (t < 0.4 ? lerp(0.1, 0.13, t / 0.4) : lerp(0.13, 0.02, (t - 0.4) / 0.6)) * R * sc), fill });
      } else { // thick / bristle
        const n = st === 'bristle' ? 4 : 3;
        for (let i = 0; i < n; i++) {
          const k = i / (n - 1);
          const a = F(side * (0.04 + k * 0.2), ny + 0.12 + k * 0.08, 0.18 - k * 0.08);
          const b = F(side * (0.3 + k * 0.28) * mw, my + 0.02 + k * 0.12 + (st === 'bristle' ? -0.04 * i : 0), 0.04);
          items.push({ d: leaf(a, b, (0.17 - k * 0.04) * R * sc, side * -0.08), fill });
        }
      }
    }
    clumps(S, 'beard', items, 0.75 * lwk);
    if (S.o.lod >= 1 && st !== 'thin') S.line('beard', spline([F(-0.05, ny + 0.17, 0.18), F(-0.2, my - 0.08, 0.1), F(-0.34 * mw, my, 0.04)]), H.h, 1.0, ' opacity="0.7"');
  }
  function drawBeard(S, G, sp, T) {
    const st = sp.beard.style;
    if (!st || st === 'none') return;
    const H = hairT(sp.beard.color || sp.hair.color);
    const R = G.R, F = G.F, P = G.P, L = G.L, my = G.mouthY, lo = S.o.lod;
    const len = sp.beard.len || 1;
    const lwk = S.lw / 2.6;
    const chinX = G.FX(-0.12, G.chinY, 0.12);
    if (st === 'stubble') {
      const ring = jawRing(G), inner = beardInner(G, sp);
      S.headShade.push(`<path d="${spline(ring.concat(inner), true)}" fill="${H.b}" opacity="0.2"/>`);
      S.headShade.push(`<path d="${spline([F(-0.36, G.noseY + 0.14, 0.1), F(0, G.noseY + 0.12, 0.2), F(0.4, G.noseY + 0.18, 0), F(0.38, my - 0.06, 0), F(0, my - 0.08, 0.12), F(-0.36, my - 0.04, 0.05)], true)}" fill="${H.b}" opacity="0.18"/>`);
      return;
    }
    const items = [];
    const lines = [], hls = [];
    let jawSil = null;
    // ---- 颊须（沿下颌一圈的短绺）
    const jawLen = { short: [0.06, 0.16], full: [0.14, 0.42], bristle: [0.2, 0.5], flowing: [0.12, 0.3], long: [0.08, 0.2], forked: [0.1, 0.24], curled: [0.12, 0.3], braided: [0.12, 0.3] }[st];
    const jawOn = jawLen && (st === 'short' || st === 'full' || st === 'bristle' || sp.beard.jaw !== false && (st === 'flowing' || st === 'curled' || st === 'braided' || sp.beard.jaw));
    if (jawOn) {
      const ring = resample(jawRing(G), st === 'bristle' ? 15 : 13);
      const nm = normals(ring);
      const inner = resample(beardInner(G, sp, st === 'bristle').slice().reverse(), ring.length);
      const n = ring.length;
      const tips = [];
      for (let i = 0; i < n; i++) {
        const t = i / (n - 1);
        const bell = Math.exp(-Math.pow((t - 0.42) / 0.22, 2));
        let l = lerp(jawLen[0], jawLen[1] * len, bell) * R;
        if (st === 'bristle') l *= (i % 2 ? 0.7 : 1.15);
        const down = st === 'bristle' ? 0.3 : 1.7;
        let dx = nm[i][0], dy = nm[i][1] + down;
        const dl = hyp(dx, dy) || 1; dx /= dl; dy /= dl;
        tips.push([ring[i][0] + dx * l, ring[i][1] + dy * l]);
      }
      // 整体外形（尖端成锯齿状的须簇），底色为迎光色
      const outer = [];
      for (let i = 0; i < n; i++) {
        outer.push([tips[i][0], tips[i][1], 0.2]);
        if (i < n - 1) { const m = lp(ring[i], ring[i + 1], 0.5), mt = lp(tips[i], tips[i + 1], 0.5); outer.push(lp(m, mt, st === 'bristle' ? 0.42 : 0.6)); }
      }
      const silD = spline(outer.concat(inner.slice().reverse()), true);
      jawSil = silD;
      items.push({ d: silD, fill: H.b });
      // 须线：沿每簇方向的细线（只画一部分，避免碎）
      for (let i = 0; i < n; i++) {
        const root = lp(ring[i], inner[i], 0.55);
        const lit = ring[i][0] < G.hx - 0.05 * R;
        if (lo >= 1 && (lit || i % 2 === 0)) {
          lines.push(spline([lp(root, tips[i], 0.12), add(lp(root, tips[i], 0.55), i % 2 ? 1.0 : -1.0, 0), lp(root, tips[i], 0.9)]));
          if (lit && i % 2 === 0) hls.push(spline([lp(root, tips[i], 0.22), add(lp(root, tips[i], 0.5), -1.2, 0), lp(root, tips[i], 0.72)]));
        }
      }
    }
    // ---- 下巴的长须 / 山羊须
    if (st === 'goatee' || st === 'long' || st === 'flowing' || st === 'forked' || st === 'curled' || st === 'braided') {
      const blen = (st === 'goatee' ? 0.5 : st === 'flowing' ? 2.55 : st === 'curled' ? 1.3 : st === 'braided' ? 1.0 : 1.35) * len;
      const sway = st === 'flowing' ? -0.22 : st === 'goatee' ? -0.04 : -0.1;
      const wide = st === 'curled' ? 1.25 : st === 'goatee' ? 0.55 : st === 'flowing' ? 1.0 : 0.85;
      const nL = st === 'goatee' ? 3 : st === 'flowing' ? 8 : 6;
      const baseY = G.chinY - (st === 'goatee' ? 0.2 : 0.05);
      const locks = [];
      for (let i = 0; i < nL; i++) {
        const k = nL === 1 ? 0.5 : i / (nL - 1);
        const u = lerp(-0.48, 0.34, k) * wide;
        const root = st === 'goatee' && i === 1 ? F(-0.06, my + 0.2, 0.1) : F(u, baseY - Math.abs(k - 0.5) * 0.3, 0.1);
        let ly = blen * (0.72 + 0.28 * Math.sin(Math.PI * (0.25 + k * 0.6)));
        if (st === 'forked') ly *= (k < 0.5 ? 1.0 : 0.95);
        let tx = chinX + (sway + (k - 0.5) * (st === 'curled' ? 0.9 : 0.35) * wide) * R;
        if (st === 'forked') tx = chinX + (sway + (k < 0.5 ? -0.28 : 0.22)) * R;
        const tip = [tx, G.ey + (G.chinY + ly) * R];
        locks.push({ root, tip, w: (st === 'goatee' ? 0.2 : 0.34 * wide) * R, bend: (k - 0.5) * -0.12 + (st === 'flowing' ? 0.05 * Math.sin(i * 2.1) : 0) });
      }
      // 外形底层：从各绺两侧取点
      for (const lk of locks) items.push({ d: leaf(lk.root, lk.tip, lk.w * 1.25, lk.bend), fill: H.s });
      for (let i = 0; i < locks.length; i++) {
        const lk = locks[i];
        const lit = i < locks.length * 0.6;
        items.push({ d: lock(lp(lk.root, lk.tip, 0.02), lp(lk.root, lk.tip, lit ? 0.98 : 0.92), lk.w * (lit ? 0.85 : 0.7), lk.bend * 1.5 + (i % 2 ? 0.04 : -0.04)), fill: lit ? H.b : mix(H.b, H.s, 0.45) });
        if (lo >= 1) {
          const m = lp(lk.root, lk.tip, 0.5);
          lines.push(spline([lp(lk.root, lk.tip, 0.1), add(m, (i % 2 ? 1.5 : -1.5), 0), lp(lk.root, lk.tip, 0.93)]));
          if (lit) hls.push(spline([add(lp(lk.root, lk.tip, 0.12), -lk.w * 0.18, 0), add(m, -lk.w * 0.22 - 1, 0), add(lp(lk.root, lk.tip, 0.7), -lk.w * 0.1, 0)]));
        }
      }
      if (st === 'braided') {
        // 末端编成辫子
        const top = [chinX - 0.08 * R, G.ey + (G.chinY + blen * 0.8) * R];
        for (let j = 0; j < 4; j++) {
          const c = add(top, -0.02 * R * j, j * 0.16 * R);
          items.push({ d: spline([add(c, -0.1 * R, 0), add(c, 0, -0.05 * R), add(c, 0.1 * R, 0.02 * R), add(c, 0, 0.14 * R)], true), fill: j % 2 ? H.b : H.s });
        }
      }
    }
    clumps(S, 'beard', items, 0.8 * lwk);
    if (jawSil) {
      // 颊须的阴影面：背光的近侧与唇下（赛璐璐两阶）
      const sh = spline([P(0.16, -0.3), P(0.12, 0.5), P(0.02, 1.0 * L), P(-0.2, 1.55 * L), P(-0.3, 2.6, 0), P(2.2, 2.6, 0), P(2.2, -0.3, 0)], true);
      const lip = spline([F(-0.42, my + 0.1, 0.06), F(-0.1, my + 0.3, 0.1), F(0.3, my + 0.22, 0.04), F(0.1, my + 0.5, 0.08), F(-0.3, my + 0.42, 0.06)], true);
      S.add('beard', `<g clip-path="${S.clip('jb', jawSil)}"><path d="${sh}" fill="${H.s}"/><path d="${lip}" fill="${H.s}" opacity="0.7"/></g>`);
    }
    if (st === 'curled' && lo >= 1) {
      // 波斯式卷须：成排的小卷
      let cd = '';
      for (let row = 0; row < 4; row++) for (let j = 0; j < 5; j++) {
        const x = chinX + (-0.55 + j * 0.25 + (row % 2) * 0.12) * R * 1.1, y = G.ey + (G.chinY + 0.15 + row * 0.28) * R;
        cd += `M${N(x + 3.2)},${N(y)}a3.2,3.2 0 1,1 -3.2,-3.2`;
      }
      S.line('beard', cd, H.d, 1.1, ' opacity="0.8"');
    }
    if (lines.length) S.line('beard', lines.join(''), H.d, 1.0, ' opacity="0.6"');
    if (hls.length) S.line('beard', hls.join(''), H.h, 1.2, ' opacity="0.55"');
  }

  // ======================================================== 头发 ==
  function hairline(G, sp) {
    const P = G.P, rec = sp.hair.recede != null ? sp.hair.recede : (sp.age > 55 ? 0.12 : 0);
    const sb = sp.hair.sideburn || 1;
    return [
      P(-0.95, -0.54), P(-0.8, -0.86 - rec), P(-0.44, -0.98 - rec, 1), P(-0.08, -0.97 - rec), P(0.3, -0.86 - rec * 0.8),
      P(0.44, -0.6, 0.6), P(0.47, -0.36), P(0.49, 0.04 * sb, 0.3), P(0.6, 0.06 * sb, 0.3), P(0.64, 0.45), P(0.94, 0.42),
    ];
  }
  // 头发外缘：颅骨椭圆放大
  function hairBack(G, vol, nape, bumps) {
    const e = G.ell, s = 1 + vol;
    const rx = G.silRx(s), ry = G.silRy(s);
    if (!bumps) return [[e.Cx + rx * 0.99, e.Cy + ry * (nape || 0.5)], ...arcPts(e.Cx, e.Cy, rx, ry, -0.2, Math.PI * 0.96, 9)];
    // 卷发：外缘起伏
    const out = [[e.Cx + rx * 0.99, e.Cy + ry * (nape || 0.5)]];
    const n = bumps * 2;
    for (let i = 0; i <= n; i++) {
      const a = -0.2 + (Math.PI * 0.96 + 0.2) * i / n, k = i % 2 ? 1.06 : 0.98;
      out.push([e.Cx + rx * k * Math.cos(a), e.Cy - ry * k * Math.sin(a), i % 2 ? 1 : 0.35]);
    }
    return out;
  }
  function drawHair(S, G, sp, T, hatInfo) {
    const style = sp.hair.style || 'topknot';
    const fn = HAIR[style] || HAIR.topknot;
    if (hatInfo.handled) return;
    fn(S, G, sp, T, hatInfo, hairT(sp.hair.color));
  }
  // 头发主体（发际线以上）
  function hairCap(S, G, sp, T, H, opt) {
    opt = opt || {};
    const R = G.R, P = G.P, lo = S.o.lod;
    const vol = (sp.hair.vol || 1) * (opt.vol || 0.06);
    const hl = opt.hairline || hairline(G, sp);
    const hp = hl.concat(opt.back || hairBack(G, vol, opt.nape, opt.bumps));
    const d = spline(hp, true);
    S.shape('hair', d, H.b, () => {
      S.path('hair', spline([P(0.28, -2.0, 0), P(0.3, -1.1), P(0.55, -0.6), P(0.62, 0.9, 0), P(2, 0.9, 0), P(2, -2, 0)], true), H.s);
      if (opt.shine !== false) {
        // 发丝高光带：上缘顺着头形，下缘锯齿
        const top = [P(-0.88, -0.98), P(-0.5, -1.27), P(0.0, -1.38), P(0.48, -1.3)];
        const lo2 = [];
        for (let i = 0; i <= 8; i++) {
          const t = i / 8, u = lerp(0.42, -0.84, t);
          const yb = -1.24 + Math.pow(Math.abs(t - 0.45) * 1.6, 2) * 0.22;
          lo2.push(P(u, yb + (i % 2 ? 0.12 : 0), i % 2 ? 0 : 0.6));
        }
        S.path('hair', spline(top.concat(lo2), true), H.h, 0, ' opacity="0.8"');
      }
      if (lo >= 1 && opt.strands !== false) {
        let sd = '';
        const tgt = opt.target || P(0.25, -1.55);
        for (let i = 0; i < 10; i++) {
          const a = lp(hl[Math.min(hl.length - 1, 1 + Math.floor(i * 0.5))], hl[Math.min(hl.length - 1, 2 + Math.floor(i * 0.5))], (i * 0.5) % 1);
          const q = lp(a, tgt, 0.55);
          sd += spline([add(a, 0, -1), [q[0] + (i - 4) * 1.6, q[1] - 2], lp(a, tgt, 0.9)]);
        }
        S.line('hair', sd, H.d, 1.0, ' opacity="0.6"');
      }
      if (opt.extra) opt.extra();
    }, S.lw);
    rimOn(S, 'hair', hp, T.rim, 2.4, 0.55);
    S.headShade.push(`<path d="${spline(hl.slice(0, 7).concat(shift(hl.slice(0, 7), 1.5, 0.09 * R).reverse()), true)}" fill="${T.skin.s}" opacity="0.9"/>`);
    return hl;
  }
  // 鬓边垂下的几缕散发
  function templeLocks(S, G, H, n, len) {
    const P = G.P, R = G.R, items = [];
    for (let i = 0; i < n; i++) {
      const a = P(0.42 + i * 0.05, -0.62 + i * 0.05), b = P(0.36 + i * 0.1, -0.1 + len * (0.5 + i * 0.25));
      items.push({ d: leaf(a, b, 0.1 * R, 0.12), fill: i ? H.s : H.b });
    }
    clumps(S, 'hair', items, S.lw * 0.5);
  }
  function bun(S, G, H, x, y, r) {
    const R = G.R;
    const bd = spline([[x - r, y + 0.55 * r], [x - 0.85 * r, y - 0.55 * r], [x + 0.15 * r, y - r], [x + r, y - 0.45 * r], [x + 0.95 * r, y + 0.6 * r]], true);
    S.shape('hair', bd, H.b, () => {
      S.path('hair', spline([[x + 0.15 * r, y - 1.1 * r], [x + 1.1 * r, y - 0.3 * r], [x + r, y + 0.8 * r], [x + 0.3 * r, y + 0.8 * r]], true), H.s);
      S.path('hair', spline([[x - 0.72 * r, y - 0.3 * r], [x - 0.15 * r, y - 0.72 * r], [x + 0.3 * r, y - 0.68 * r], [x - 0.3 * r, y - 0.36 * r]], true), H.h);
      if (S.o.lod >= 1) S.line('hair', spline([[x - 0.6 * r, y + 0.3 * r], [x - 0.2 * r, y - 0.5 * r], [x + 0.5 * r, y - 0.6 * r]]) + spline([[x - 0.3 * r, y + 0.5 * r], [x + 0.2 * r, y - 0.2 * r], [x + 0.8 * r, y - 0.2 * r]]), H.d, 1, ' opacity="0.6"');
    }, S.lw);
    return bd;
  }
  const HAIR = {
    // 汉式束发：梳拢成髻
    topknot(S, G, sp, T, hatInfo, H) {
      hairCap(S, G, sp, T, H);
      if (!hatInfo.coversTop) {
        const R = G.R, bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
        bun(S, G, H, bx, by, 0.36 * R);
        if (!hatInfo.ownsBun) {
          S.path('hair', spline([[bx - 0.36 * R, by + 0.12 * R], [bx, by + 0.2 * R], [bx + 0.35 * R, by + 0.1 * R], [bx + 0.36 * R, by + 0.24 * R], [bx, by + 0.34 * R], [bx - 0.36 * R, by + 0.26 * R]], true), sp.hair.tie || '#7a2a24', 1.2);
          S.line('hair', `M${N(bx - 0.62 * R)},${N(by + 0.02 * R)}L${N(bx + 0.62 * R)},${N(by - 0.12 * R)}`, INK, 3.4);
          S.line('hair', `M${N(bx - 0.6 * R)},${N(by + 0.02 * R)}L${N(bx + 0.6 * R)},${N(by - 0.12 * R)}`, sp.hair.pin || GOLD, 1.8);
        }
      }
      if (sp.hair.locks) templeLocks(S, G, H, sp.hair.locks, 1);
    },
    // 女子高髻：中分，鬓发垂于耳前，簪钗步摇
    bun(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      const hl = [P(-0.95, -0.5), P(-0.78, -0.84), P(-0.42, -0.98, 0.3), P(-0.2, -0.9), P(0.22, -0.84), P(0.44, -0.58), P(0.46, -0.3), P(0.42, 0.3, 0.4), P(0.62, 0.4, 0.4), P(0.66, 0.5), P(1.0, 0.6)];
      hairCap(S, G, sp, T, H, { hairline: hl, vol: 0.12, target: P(0.1, -1.7), shine: true });
      if (!hatInfo.coversTop) {
        const bx = G.hx + 0.05 * R, by = G.ey - 1.72 * R;
        bun(S, G, H, bx + 0.42 * R, by + 0.12 * R, 0.42 * R);
        bun(S, G, H, bx - 0.12 * R, by - 0.05 * R, 0.4 * R);
        // 发钗与步摇
        const pin = sp.hair.pin || GOLD;
        S.line('hair', `M${N(bx - 0.75 * R)},${N(by + 0.25 * R)}L${N(bx + 0.4 * R)},${N(by - 0.35 * R)}`, INK, 3.2);
        S.line('hair', `M${N(bx - 0.73 * R)},${N(by + 0.24 * R)}L${N(bx + 0.4 * R)},${N(by - 0.35 * R)}`, pin, 1.7);
        const fx = bx - 0.72 * R, fy = by + 0.25 * R;
        S.add('hair', circle(fx, fy, 0.11 * R) + ` fill="${sp.hair.flower || '#e0607a'}" stroke="${INK}" stroke-width="1.2"/>`);
        S.add('hair', circle(fx, fy, 0.04 * R) + ` fill="#ffe08a"/>`);
        if (S.o.lod >= 1) {
          S.line('hair', `M${N(fx)},${N(fy)}v${N(0.45 * R)}M${N(fx - 3)},${N(fy)}v${N(0.32 * R)}`, pin, 1.0);
          S.add('hair', circle(fx, fy + 0.48 * R, 1.8) + ` fill="${pin}"/>` + circle(fx - 3, fy + 0.35 * R, 1.5) + ` fill="#c8e0ff"/>`);
        }
      }
      // 耳前鬓发
      clumps(S, 'hair', [
        { d: leaf(P(0.44, -0.6), P(0.4, 0.55), 0.16 * R, 0.08), fill: H.s },
        { d: leaf(P(-0.9, -0.5), P(-0.98, 0.3), 0.14 * R, -0.08), fill: H.b },
      ], S.lw * 0.55);
    },
    twinbun(S, G, sp, T, hatInfo, H) {
      HAIR.bun(S, G, Object.assign({}, sp), T, { coversTop: true }, H);
      const R = G.R;
      bun(S, G, H, G.hx - 0.42 * R, G.ey - 1.65 * R, 0.32 * R);
      bun(S, G, H, G.hx + 0.62 * R, G.ey - 1.5 * R, 0.34 * R);
    },
    // 披发 / 长发（散在肩后）
    long(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      const items = [];
      for (let i = 0; i < 6; i++) {
        const a = P(0.3 + i * 0.16, -0.9 + i * 0.12), b = P(0.8 + i * 0.2, 1.8 + (i % 2) * 0.4);
        items.push({ d: leaf(a, b, 0.5 * R, 0.06), fill: i % 2 ? H.s : H.d });
      }
      items.push({ d: leaf(P(-0.9, -0.7), P(-1.05, 1.0), 0.3 * R, -0.06), fill: H.s });
      clumps(S, 'back', items, S.lw * 0.6);
      hairCap(S, G, sp, T, H, { vol: 0.1, target: P(0.6, -0.5) });
      if (!hatInfo.coversTop && sp.hair.topknot) HAIR.topknot(S, G, sp, T, { coversTop: false, ownsBun: false }, H);
    },
  };

  // ======================================================== 冠帽 ==
  function drawHat(S, G, sp, T) {
    const type = sp.hat.type || 'none';
    const fn = HATS[type];
    if (!fn) return { coversTop: false };
    return fn(S, G, sp, T) || { coversTop: true };
  }
  function bandShadow(S, G, pts, T, k) {
    S.headShade.push(`<path d="${spline(pts.concat(shift(pts, 1, (k || 0.14) * G.R).reverse()), true)}" fill="${T.skin.d}" opacity="0.55"/>`);
  }
  // 条带（两条 band 之间）
  function bandStrip(G, bm0, bm1, ba, s, n) {
    const lo = band(G, bm0, ba, s, n), hi = band(G, bm1, ba, s, n);
    return { lo, hi, pts: lo.concat(hi.slice().reverse()) };
  }
  // 雉尾 / 鹖尾：中线 + 斑纹（stroke-dasharray）
  function feather(S, layer, pts, w, col, bar) {
    const d = taper(pts, t => w * (1 - t * 0.75) + 0.4);
    S.add(layer, `<path d="${d}" fill="${col}" stroke="${INK}" stroke-width="${N(S.lw * 0.75)}"/>`);
    if (S.o.lod >= 1) S.add(layer, `<path d="${spline(pts)}" fill="none" stroke="${bar || mul(col, 0.45)}" stroke-width="${N(w * 0.8)}" stroke-dasharray="${N(w * 0.55)} ${N(w * 0.75)}" stroke-linecap="butt" opacity="0.85"/>`);
    S.add(layer, `<path d="${spline(pts)}" fill="none" stroke="${mix(col, '#fff', 0.5)}" stroke-width="0.8" opacity="0.7"/>`);
  }
  // 红缨
  function tassel(S, layer, G, top, col, n, len, dir) {
    const R = G.R, items = [];
    dir = dir || 1;
    const C = clothT(col);
    for (let i = 0; i < n; i++) {
      const a = -0.5 + i / (n - 1) * 1.6;
      const tip = add(top, dir * Math.cos(a) * len * R * (0.7 + 0.3 * ((i * 7) % 3) / 2), (Math.sin(a) * 0.8 + 0.2) * len * R);
      items.push({ d: leaf(top, tip, 0.22 * R, dir * 0.12), fill: i % 2 ? C.s : C.b });
    }
    clumps(S, layer, items, S.lw * 0.6);
  }
  const HATS = {
    none() { return { coversTop: false }; },
    // 帻：包住发髻的头巾帽（介帻，后部隆起）
    ze(S, G, sp, T, flat) {
      const col = sp.hat.color || '#2a2226';
      const C = clothT(col), R = G.R;
      const s = 1.08;
      const bnd = band(G, 0.16, 0.36, s);
      const cap = capOutline(G, bnd, s, 1.03);
      if (!flat) {
        // 介帻的“屋”：顶后部两片隆起
        const top = G.h3(1.05, 1.2, s);
        const a0 = G.h3(0.2, 1.0, s), a1 = G.h3(1.85, 0.62, s);
        const roof = spline([[a0[0], a0[1], 0], [top[0] - 0.45 * R, top[1] - 0.32 * R], [top[0] - 0.05 * R, top[1] - 0.44 * R, 0.4], [top[0] + 0.3 * R, top[1] - 0.3 * R], [top[0] + 0.55 * R, top[1] - 0.05 * R], [a1[0], a1[1], 0]], true);
        S.shape('hat', roof, C.b, () => {
          S.path('hat', spline([[top[0] - 0.05 * R, top[1] - 0.6 * R], [top[0] + 0.8 * R, top[1] - 0.2 * R], [top[0] + 0.5 * R, top[1] + 0.6 * R], [top[0] - 0.05 * R, top[1] + 0.2 * R]], true), C.s);
          S.line('hat', spline([[top[0] - 0.05 * R, top[1] - 0.44 * R], [top[0] + 0.02 * R, top[1] - 0.05 * R]]), C.d, 1.3);
        }, S.lw);
      }
      S.shape('hat', spline(cap, true), C.b, () => {
        S.path('hat', spline([G.h3(0.95, 0.2, 1.3), G.h3(0.65, 1.2, 1.3), G.h3(2.2, 1.0, 1.3), G.h3(2.2, -0.2, 1.3)], true), C.s);
        S.path('hat', spline(bnd.concat(shift(bnd, 0, -0.22 * R).reverse()), true), C.d, 0, ' opacity="0.65"');
        if (S.o.lod >= 1) S.path('hat', spline([G.h3(-0.6, 0.72, s), G.h3(-0.15, 0.95, s), G.h3(0.3, 1.04, s), G.h3(0.0, 0.88, s)], true), C.h, 0, ' opacity="0.55"');
      }, S.lw);
      bandShadow(S, G, bnd, T);
      return { coversTop: true };
    },
    // 进贤冠：黑漆纱冠 + 前高后低的“展筩”，冠梁数表示品级
    jinxian(S, G, sp, T) {
      const col = sp.hat.color || '#1e1a1e';
      HATS.ze(S, G, Object.assign({}, sp, { hat: { color: col } }), T, true);
      const R = G.R;
      const b0 = G.h3(-0.45, 0.78, 1.1), b1 = G.h3(0.95, 0.92, 1.1);
      const t0 = [b0[0] + 0.02 * R, b0[1] - 1.0 * R], t1 = [b1[0] + 0.42 * R, b1[1] - 0.62 * R];
      const plate = spline([[b0[0], b0[1], 0], [t0[0], t0[1], 0.2], [t0[0] + 0.3 * R, t0[1] - 0.08 * R, 0.3], [t1[0], t1[1], 0.2], [b1[0], b1[1], 0]], true);
      S.shape('hat', plate, mix(col, '#3a3038', 0.6), () => {
        S.path('hat', poly([b1, t1, [t1[0] + 0.3 * R, t1[1] + 0.3 * R], [b1[0] + 0.3 * R, b1[1]]]), mul(col, 0.7));
        const liang = sp.hat.liang || 2;
        let ld = '';
        for (let i = 1; i <= liang; i++) { const k = i / (liang + 1); ld += `M${ps(lp(b0, b1, k))}L${ps(lp([t0[0] + 0.15 * R, t0[1]], t1, k))}`; }
        S.line('hat', ld, '#6a6070', 1.6);
      }, S.lw);
      // 冠缨（系于颔下）
      const st = G.h3(1.15, 0.05, 1.06);
      S.line('hat', spline([st, G.P(0.86, 0.5), G.P(0.72, 1.15)]), INK, 2.4);
      S.line('hat', spline([st, G.P(0.86, 0.5), G.P(0.72, 1.15)]), sp.hat.color2 || '#7a3a30', 1.1);
      return { coversTop: true };
    },
    // 幅巾：软巾裹头，顶上打结，巾尾垂于脑后
    futou(S, G, sp, T) {
      const col = sp.hat.color || '#5a4a3a';
      const C = clothT(col), R = G.R;
      const s = 1.1;
      const bnd = band(G, 0.14, 0.36, s);
      const cap = capOutline(G, bnd, s, 1.1);
      // 巾尾
      const k0 = G.h3(1.9, 0.5, s);
      clumps(S, 'back', [{ d: leaf(k0, G.P(1.55, 1.25), 0.32 * R, 0.1), fill: C.s }, { d: leaf(k0, G.P(1.3, 1.5), 0.3 * R, -0.05), fill: C.b }], S.lw * 0.6);
      S.shape('hat', spline(cap, true), C.b, () => {
        S.path('hat', spline([G.h3(0.9, 0.2, 1.3), G.h3(0.6, 1.3, 1.3), G.h3(2.2, 1.0, 1.3), G.h3(2.2, -0.2, 1.3)], true), C.s);
        let fd = '';
        for (let i = 0; i < 3; i++) fd += spline([G.h3(-0.9 + i * 0.4, 0.55 + i * 0.15, s), G.h3(-0.1 + i * 0.4, 1.0 + i * 0.12, s), G.h3(0.8 + i * 0.3, 1.25, s)]);
        S.line('hat', fd, C.d, 1.3, ' opacity="0.7"');
        if (S.o.lod >= 1) S.path('hat', spline([G.h3(-0.6, 0.7, s), G.h3(-0.2, 0.95, s), G.h3(0.2, 1.05, s), G.h3(-0.05, 0.85, s)], true), C.h, 0, ' opacity="0.5"');
      }, S.lw);
      // 顶结
      const kt = G.h3(0.35, 1.25, s * 1.02);
      clumps(S, 'hat', [{ d: leaf(kt, add(kt, -0.42 * R, -0.3 * R), 0.3 * R, 0.2), fill: C.b }, { d: leaf(kt, add(kt, 0.38 * R, -0.36 * R), 0.3 * R, -0.2), fill: C.s }], S.lw * 0.6);
      S.add('hat', circle(kt[0], kt[1], 0.12 * R) + ` fill="${C.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
      bandShadow(S, G, bnd, T);
      return { coversTop: true };
    },
    // 头巾：裹头的布巾，斜向缠绕，脑后打结垂尾
    turban(S, G, sp, T) {
      const col = sp.hat.color || '#3a6a3a';
      const C = clothT(col), R = G.R;
      const s = 1.13;
      const bnd = band(G, 0.12, 0.38, s);
      const cap = capOutline(G, bnd, s, 1.1);
      const k0 = G.h3(2.05, 0.75, s);
      clumps(S, 'back', [{ d: leaf(k0, G.P(1.7, 1.45), 0.4 * R, 0.12), fill: C.s }, { d: leaf(k0, G.P(1.35, 1.75), 0.36 * R, -0.08), fill: C.b }], S.lw * 0.6);
      S.shape('hat', spline(cap, true), C.b, () => {
        S.path('hat', spline([G.h3(0.95, 0.1, 1.4), G.h3(0.6, 1.3, 1.4), G.h3(2.2, 1.0, 1.4), G.h3(2.2, -0.2, 1.4)], true), C.s);
        // 缠绕的布褶
        for (let i = 0; i < 4; i++) {
          const pts = [];
          for (let j = 0; j <= 10; j++) { const lam = G.lamFar + (G.lamNear - G.lamFar) * j / 10; pts.push(G.h3(lam, 0.46 + i * 0.24 - 0.22 * Math.sin(lam - 0.4) + 0.1 * Math.cos(lam), s * 1.01)); }
          S.path('hat', spline(pts.concat(shift(pts, 0, 0.09 * R).reverse()), true), C.d, 0, ' opacity="0.45"');
          if (S.o.lod >= 1) S.line('hat', spline(shift(pts, 0, -0.05 * R)), C.h, 1.2, ' opacity="0.45"');
        }
      }, S.lw);
      // 额前的布沿
      const fr = bandStrip(G, 0.12, 0.3, 0.38, s * 1.01);
      S.shape('hat', spline(fr.pts, true), C.b, () => {
        S.path('hat', spline(fr.hi.concat(shift(fr.hi, 0, 0.06 * R).reverse()), true), C.h, 0, ' opacity="0.5"');
      }, S.lw * 0.9);
      if (sp.hat.gem) S.add('hat', circle(G.h3(0, 0.62, s * 1.02)[0], G.h3(0, 0.62, s * 1.02)[1], 0.09 * R) + ` fill="${sp.hat.gem}" stroke="${INK}" stroke-width="1.2"/>`);
      const kn = G.h3(1.85, 0.85, s * 1.02);
      S.add('hat', ellipse(kn[0], kn[1], 0.2 * R, 0.16 * R, 20) + ` fill="${C.s}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      bandShadow(S, G, bnd, T, 0.18);
      return { coversTop: true };
    },
    // 抹额：额上一条带子，脑后系结
    headband(S, G, sp, T) {
      const col = sp.hat.color || '#a02a24';
      const C = clothT(col), R = G.R;
      const st = bandStrip(G, 0.08, 0.25, 0.36, 1.05);
      const k0 = G.h3(G.lamNear - 0.15, 0.2, 1.05);
      clumps(S, 'hat', [{ d: leaf(k0, add(k0, 0.65 * R, 0.85 * R), 0.22 * R, 0.15), fill: C.b }, { d: leaf(k0, add(k0, 0.35 * R, 1.1 * R), 0.2 * R, -0.1), fill: C.s }], S.lw * 0.55);
      S.shape('hat', spline(st.pts, true), C.b, () => {
        S.path('hat', spline(st.lo.slice(9).concat(st.hi.slice(9).reverse()), true), C.s);
        S.path('hat', spline(st.hi.slice(0, 10).concat(shift(st.hi.slice(0, 10), 0, 0.05 * R).reverse()), true), C.h, 0, ' opacity="0.55"');
      }, S.lw * 0.9);
      if (sp.hat.gem) { const g = G.h3(0, 0.5, 1.07); S.add('hat', circle(g[0], g[1], 0.1 * R) + ` fill="${sp.hat.gem}" stroke="${INK}" stroke-width="1.2"/>`); }
      bandShadow(S, G, st.lo, T, 0.1);
      return { coversTop: false };
    },
    // 纶巾：青丝软巾，方顶，两条飘带
    lunjin(S, G, sp, T) {
      const col = sp.hat.color || '#2c3a58';
      const C = clothT(col), R = G.R;
      const s = 1.1;
      const bnd = band(G, 0.16, 0.36, s);
      let cap = capOutline(G, bnd, s, 1.42);
      const yTop = Math.min(...cap.map(p => p[1]));
      cap = cap.map((p, i) => (i >= bnd.length ? [p[0], lerp(p[1], yTop, 0.45), p[2]] : p));
      // 飘带
      const k0 = G.h3(1.95, 0.55, s);
      feather(S, 'back', [k0, G.P(1.45, 0.4), G.P(1.75, 1.2), G.P(1.55, 2.1)], 0.2 * R, sp.hat.color2 || '#3a4a6a', 'none');
      feather(S, 'back', [k0, G.P(1.25, 0.6), G.P(1.35, 1.5), G.P(1.1, 2.3)], 0.18 * R, sp.hat.color2 || '#3a4a6a', 'none');
      S.shape('hat', spline(cap, true), C.b, () => {
        S.path('hat', spline([G.h3(0.9, 0.1, 1.5), G.h3(0.7, 1.4, 1.6), [G.hx + 2 * R, yTop - R], G.h3(2.2, -0.2, 1.5)], true), C.s);
        let pd = '';
        for (let i = 0; i < 6; i++) { const lam = -0.8 + i * 0.42; const b0 = G.h3(lam, 0.28 + 0.36 * Math.cos(lam) + 0.04, s); pd += `M${ps(b0)}L${ps([b0[0] + (lam - 0.6) * 0.06 * R, yTop + 0.12 * R])}`; }
        S.line('hat', pd, C.d, 1.3, ' opacity="0.7"');
        S.path('hat', spline(bnd.concat(shift(bnd, 0, -0.2 * R).reverse()), true), C.h, 0, ' opacity="0.35"');
      }, S.lw);
      bandShadow(S, G, bnd, T);
      return { coversTop: true };
    },
    // 武冠（鹖冠）：黑纱笼冠，两侧插鹖尾
    wuguan(S, G, sp, T) {
      const col = sp.hat.color || '#1e1a1e';
      const R = G.R;
      HATS.ze(S, G, Object.assign({}, sp, { hat: { color: col } }), T, true);
      const p0 = G.h3(-0.95, 0.45, 1.14), p1 = G.h3(1.75, 0.2, 1.14);
      const top = G.ey - 2.05 * R;
      const box = spline([p0, [p0[0] - 0.12 * R, top + 0.12 * R, 0.3], [lerp(p0[0], p1[0], 0.5), top - 0.08 * R, 1], [p1[0] + 0.2 * R, top + 0.05 * R, 0.3], p1, [lerp(p0[0], p1[0], 0.55), G.ey - 0.82 * R]], true);
      S.shape('hat', box, '#16121a', () => {
        if (S.o.lod >= 2) {
          let md = '';
          for (let x = p0[0] - 0.2 * R; x < p1[0] + 0.3 * R; x += 0.12 * R) md += `M${N(x)},${N(top - 0.2 * R)}l${N(0.2 * R)},${N(2 * R)}`;
          S.line('hat', md, '#4a4450', 0.6, ' opacity="0.6"');
        }
        S.path('hat', spline([[p1[0] - 0.3 * R, top], [p1[0] + 0.4 * R, top], [p1[0] + 0.4 * R, p1[1]], [p1[0] - 0.1 * R, p1[1]]], true), '#08060a', 0, ' opacity="0.6"');
      }, S.lw, null, 0.82);
      const fc = sp.hat.color2 || '#7a5a3a';
      feather(S, 'hat', [G.h3(1.5, 0.55, 1.15), [p1[0] + 0.05 * R, top - 0.3 * R], [p1[0] + 0.3 * R, top - 1.1 * R]], 0.16 * R, fc);
      feather(S, 'back', [G.h3(-0.9, 0.6, 1.15), [p0[0] - 0.15 * R, top - 0.2 * R], [p0[0] - 0.05 * R, top - 0.9 * R]], 0.12 * R, fc);
      return { coversTop: true };
    },
    // 兜鍪（头盔）。variant：plain 普通 / wing 凤翅 / lion 狮首 / horn 角 / spike 尖顶
    helmet(S, G, sp, T) {
      const v = sp.hat.variant || 'plain';
      const metal = sp.hat.color || sp.outfit.metal || '#8a8f9a';
      const Mt = metalT(metal), R = G.R, P = G.P, lo = S.o.lod;
      const trim = sp.hat.trim || GOLD;
      const s = 1.14;
      const bnd = band(G, 0.08, 0.34, s);
      const cap = capOutline(G, bnd, s, v === 'spike' ? 1.25 : 1.1);
      // 顿项（护颈）：近侧脑后垂到肩，远侧露出一条
      const ng = [G.h3(1.3, 0.05, s), G.h3(1.75, -0.02, s), G.h3(G.lamNear - 0.02, -0.06, s), P(1.48, 0.95), P(1.6, 1.62, 0.4), P(0.98, 1.7, 0.4), P(0.78, 0.8)];
      const ngC = sp.hat.neck || mix(metal, '#3a2a22', 0.4);
      S.shape('back', spline(ng, true), ngC, () => {
        let rd = '';
        for (let i = 1; i < 5; i++) rd += spline([P(0.7, 0.05 + i * 0.36), P(1.15, 0.12 + i * 0.36), P(1.7, 0.1 + i * 0.36)]);
        S.line('back', rd, mul(ngC, 0.55), 1.4);
        S.path('back', spline([P(1.15, -0.2), P(1.7, 0), P(1.7, 1.8), P(1.3, 1.8)], true), mul(ngC, 0.7), 0, ' opacity="0.6"');
      }, S.lw);
      const nf = [G.h3(G.lamFar + 0.05, 0.1, s), P(-1.2, 0.3), P(-1.2, 1.0, 0.4), P(-0.9, 1.05)];
      S.shape('back', spline(nf, true), mul(ngC, 0.8), null, S.lw);
      // 盔钵
      S.shape('hat', spline(cap, true), Mt.b, () => {
        S.path('hat', spline([G.h3(0.85, 0.0, 1.5), G.h3(0.55, 1.3, 1.5), [G.hx + 2.2 * R, G.ey - 2.4 * R], G.h3(2.2, -0.2, 1.5)], true), Mt.s);
        S.path('hat', spline([G.h3(-0.75, 0.5, s), G.h3(-0.45, 1.0, s), G.h3(-0.05, 1.22, s), G.h3(-0.2, 0.95, s), G.h3(-0.55, 0.62, s)], true), Mt.h, 0, ' opacity="0.75"');
        // 盔梁（铆钉竖条）
        let md = '', rv = '';
        for (const lam of [-0.55, -0.05, 0.45, 0.95, 1.45]) {
          const pts = [];
          for (let b = 0.3 + 0.36 * Math.cos(lam) + 0.04; b < 1.45; b += 0.22) pts.push(G.h3(lam, b, s * 1.005));
          md += spline(pts);
          if (lo >= 2) for (const p of pts) rv += circle(p[0], p[1], 0.9) + ` fill="${Mt.h}"/>`;
        }
        S.line('hat', md, Mt.d, 1.6);
        if (rv) S.add('hat', rv);
      }, S.lw);
      // 眉庇与金边
      const vis = band(G, 0.04, 0.34, s * 1.03, 16).slice(2, 13);
      const visD = spline(vis.concat(shift(vis, 0, 0.11 * R).reverse().map((p, i, a) => [p[0] - (i / (a.length - 1) - 0.5) * 0.06 * R, p[1]])), true);
      S.shape('hat', visD, Mt.s, () => { S.path('hat', spline(shift(vis, 0, -0.01 * R).concat(shift(vis, 0, 0.04 * R).reverse()), true), Mt.h, 0, ' opacity="0.7"'); }, S.lw * 0.9);
      S.line('hat', spline(bnd.slice(1, -1)), INK, 3.6);
      S.line('hat', spline(bnd.slice(1, -1)), trim, 1.8);
      bandShadow(S, G, vis, T, 0.2);
      const apex = G.h3(0.6, 1.5, s * (v === 'spike' ? 1.2 : 1.08));
      if (v === 'wing') {
        // 凤翅：两侧上扬的金翅
        const w0 = G.h3(1.3, 0.55, s);
        const wing = [w0, add(w0, 0.3 * R, -0.5 * R), add(w0, 0.75 * R, -1.25 * R), add(w0, 0.95 * R, -1.15 * R), add(w0, 0.6 * R, -0.35 * R), add(w0, 0.3 * R, 0.15 * R)];
        S.shape('hat', spline(wing, true), trim, () => { S.line('hat', spline([add(w0, 0.25 * R, -0.1 * R), add(w0, 0.55 * R, -0.6 * R), add(w0, 0.82 * R, -1.1 * R)]), mul(trim, 0.6), 1.4); }, S.lw * 0.9);
        const w1 = G.h3(-0.85, 0.55, s);
        S.shape('hat', spline([w1, add(w1, -0.2 * R, -0.5 * R), add(w1, -0.4 * R, -1.0 * R), add(w1, -0.25 * R, -0.95 * R), add(w1, 0.05 * R, -0.3 * R)], true), mul(trim, 0.85), null, S.lw * 0.9);
      }
      if (v === 'horn') {
        const h0 = G.h3(0.0, 0.95, s);
        feather(S, 'hat', [h0, add(h0, -0.3 * R, -0.6 * R), add(h0, -0.15 * R, -1.1 * R)], 0.22 * R, trim, 'none');
      }
      if (v === 'lion') lionMask(S, G, sp, s, trim);
      // 盔顶：金顶与红缨
      if (v !== 'lion' || true) {
        tassel(S, 'hat', G, apex, sp.hat.plume || '#c0302a', lo === 0 ? 4 : 7, v === 'lion' ? 1.4 : 1.0, 1);
        S.add('hat', circle(apex[0], apex[1], 0.13 * R) + ` fill="${trim}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
        S.add('hat', `<path d="M${N(apex[0])},${N(apex[1] - 0.12 * R)}l${N(-0.07 * R)},${N(-0.35 * R)}l${N(0.14 * R)},0z" fill="${trim}" stroke="${INK}" stroke-width="${N(S.lw * 0.6)}"/>`);
      }
      return { coversTop: true };
    },
    // 束发金冠：小冠罩在发髻上（前高后低的弧形冠），横插玉簪
    crown(S, G, sp, T) {
      const R = G.R;
      const H = hairT(sp.hair.color);
      HAIR[HAIR[sp.hair.style] ? sp.hair.style : 'topknot'](S, G, sp, T, { coversTop: false, ownsBun: true }, H);
      const bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R, r = 0.44 * R;
      const gc = sp.hat.color || GOLD;
      const Mt = metalT(gc);
      const cr = spline([[bx - r * 1.05, by + r * 0.55, 0.3], [bx - r * 1.08, by - r * 0.2], [bx - r * 0.55, by - r * 1.05], [bx + r * 0.25, by - r * 1.12], [bx + r * 0.9, by - r * 0.5], [bx + r * 1.02, by + r * 0.45, 0.3], [bx, by + r * 0.72]], true);
      S.shape('hat', cr, Mt.b, () => {
        S.path('hat', spline([[bx + r * 0.15, by - r * 1.3], [bx + r * 1.3, by - r * 0.6], [bx + r * 1.2, by + r], [bx + r * 0.3, by + r]], true), Mt.s);
        S.path('hat', spline([[bx - r * 0.85, by - r * 0.1], [bx - r * 0.5, by - r * 0.8], [bx - r * 0.1, by - r * 0.9], [bx - r * 0.55, by - r * 0.45]], true), Mt.h, 0, ' opacity="0.85"');
        if (S.o.lod >= 1) {
          let ld = '';
          for (let i = 1; i <= 3; i++) { const k = i * 0.22; ld += spline([[bx - r * (1.05 - k), by + r * 0.5], [bx - r * (0.7 - k * 0.5), by - r * (0.9 - k)], [bx + r * (0.3 - k * 0.2), by - r * (1.05 - k)], [bx + r * (0.95 - k * 0.6), by + r * 0.4]]); }
          S.line('hat', ld, Mt.d, 1.1, ' opacity="0.8"');
        }
        S.path('hat', spline([[bx - r * 1.1, by + r * 0.35], [bx, by + r * 0.5], [bx + r * 1.1, by + r * 0.3], [bx + r * 1.1, by + r * 0.75], [bx, by + r * 0.9], [bx - r * 1.1, by + r * 0.75]], true), Mt.d, 0, ' opacity="0.55"');
      }, S.lw);
      if (sp.hat.gem !== false) {
        const gem = sp.hat.gem || '#c83040';
        S.add('hat', circle(bx - r * 0.3, by - r * 0.15, r * 0.2) + ` fill="${gem}" stroke="${INK}" stroke-width="1.2"/>` + circle(bx - r * 0.36, by - r * 0.22, r * 0.07) + ' fill="#fff" opacity="0.85"/>');
      }
      if (sp.hat.pearls) for (let i = 0; i < 3; i++) S.add('hat', circle(bx - r * (0.55 - i * 0.5), by - r * (1.0 + (i === 1 ? 0.18 : 0)), r * 0.14) + ` fill="#f4f0e8" stroke="${INK}" stroke-width="1"/>`);
      const pin = sp.hat.pin || '#e8f0e0';
      S.line('hat', `M${N(bx - 2.0 * r)},${N(by + 0.42 * r)}L${N(bx + 2.0 * r)},${N(by - 0.05 * r)}`, INK, 3.6);
      S.line('hat', `M${N(bx - 1.96 * r)},${N(by + 0.42 * r)}L${N(bx + 1.96 * r)},${N(by - 0.05 * r)}`, pin, 2.0);
      return { coversTop: true, handled: true };
    },
    // 紫金冠 + 雉翎（吕布）
    pheasant(S, G, sp, T) {
      const R = G.R;
      const bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
      const fc = sp.hat.color2 || '#c8903a';
      // 两根长雉翎：从冠后升起，向两侧外弯，出画面上缘
      // 两根长雉翎：从冠后升起，向两侧弯成长弧（经典的吕布形象），翎尾垂向两肩之外
      feather(S, 'back', [[bx - 0.2 * R, by - 0.05 * R], [bx - 0.55 * R, by - 0.62 * R], [bx - 1.3 * R, by - 0.98 * R], [bx - 2.2 * R, by - 0.88 * R], [bx - 2.95 * R, by - 0.3 * R], [bx - 3.3 * R, by + 0.5 * R]], 0.26 * R, fc, '#4a2a14');
      feather(S, 'back', [[bx + 0.15 * R, by - 0.05 * R], [bx + 0.5 * R, by - 0.68 * R], [bx + 1.25 * R, by - 1.02 * R], [bx + 2.1 * R, by - 0.92 * R], [bx + 2.75 * R, by - 0.35 * R], [bx + 3.0 * R, by + 0.45 * R]], 0.26 * R, mix(fc, '#fff', 0.1), '#4a2a14');
      return HATS.crown(S, G, Object.assign({}, sp, { hat: Object.assign({ gem: '#7a3aa8', pearls: true }, sp.hat, { color: sp.hat.color || '#d8a83a' }) }), T);
    },
    // 道冠：莲花冠（张鲁等）
    daoist(S, G, sp, T) {
      const R = G.R;
      const H = hairT(sp.hair.color);
      HAIR.topknot(S, G, sp, T, { coversTop: false, ownsBun: true }, H);
      const bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
      const gc = sp.hat.color || '#d8b04a';
      const items = [];
      for (let i = 0; i < 5; i++) {
        const a = Math.PI * (0.15 + i * 0.175);
        items.push({ d: leaf([bx, by + 0.3 * R], [bx + Math.cos(a) * 0.62 * R, by + 0.25 * R - Math.sin(a) * 0.75 * R], 0.36 * R, 0), fill: i % 2 ? mul(gc, 0.8) : gc });
      }
      clumps(S, 'hat', items, S.lw * 0.6);
      S.line('hat', `M${N(bx - 0.8 * R)},${N(by + 0.25 * R)}L${N(bx + 0.8 * R)},${N(by + 0.05 * R)}`, INK, 3.4);
      S.line('hat', `M${N(bx - 0.78 * R)},${N(by + 0.25 * R)}L${N(bx + 0.78 * R)},${N(by + 0.05 * R)}`, '#e8e0c8', 1.8);
      return { coversTop: true, handled: true };
    },
  };
  // 狮首盔的狮面（额前）
  function lionMask(S, G, sp, s, trim) {
    let R = G.R;
    const c = G.h3(-0.05, 0.6, s * 1.05);
    const Mt = metalT(trim);
    R *= 1.3;
    const mane = [];
    for (let i = 0; i < 9; i++) {
      const a = Math.PI * (0.05 + i * 0.11);
      mane.push({ d: leaf(c, add(c, Math.cos(a) * 0.55 * R, -Math.sin(a) * 0.5 * R - 0.05 * R), 0.22 * R, 0.1), fill: i % 2 ? Mt.s : Mt.b });
    }
    clumps(S, 'hat', mane, S.lw * 0.5);
    const face = spline([add(c, -0.26 * R, -0.12 * R), add(c, 0, -0.26 * R), add(c, 0.26 * R, -0.12 * R), add(c, 0.2 * R, 0.16 * R), add(c, 0, 0.26 * R), add(c, -0.2 * R, 0.16 * R)], true);
    S.shape('hat', face, Mt.b, () => { S.path('hat', spline([add(c, 0.05 * R, -0.3 * R), add(c, 0.3 * R, -0.1 * R), add(c, 0.2 * R, 0.3 * R), add(c, 0.05 * R, 0.3 * R)], true), Mt.s); }, S.lw * 0.7);
    S.line('hat', `M${ps(add(c, -0.16 * R, -0.06 * R))}q${N(0.06 * R)},${N(-0.05 * R)} ${N(0.11 * R)},0M${ps(add(c, 0.05 * R, -0.06 * R))}q${N(0.06 * R)},${N(-0.05 * R)} ${N(0.11 * R)},0`, INK, 1.4);
    S.add('hat', `<path d="M${ps(add(c, -0.12 * R, 0.08 * R))}q${N(0.12 * R)},${N(0.12 * R)} ${N(0.24 * R)},0" fill="${INK}" opacity="0.8"/>`);
    S.add('hat', ellipse(c[0] - 0.02 * R, c[1] + 0.02 * R, 0.05 * R, 0.035 * R) + ` fill="${INK}"/>`);
  }

  // ======================================================== 衣甲 ==
  function torsoD(G) { return spline(G.torso, true); }
  function drawBody(S, G, sp, T, o) {
    const type = sp.outfit.type || 'robe';
    const fn = OUTFITS[type] || OUTFITS.robe;
    fn(S, G, sp, T, o);
    if (sp.outfit.cape) mantle(S, G, sp, T, sp.outfit.cape === 'faction' ? o.color : sp.outfit.cape);
  }
  // 交领（右衽）：外襟从近侧颈部斜向远侧下方
  function crossCollar(S, G, sp, T, col, innerCol, wide) {
    const P = G.P, R = G.R;
    const C = clothT(col);
    const cw = 0.3 * (wide || 1);
    const outerL = [P(0.92 * G.nw, 1.4), P(0.62, 1.95), P(-0.2, 2.75), P(-1.25, 4.4)];
    const innerL = [P(-0.5 * G.nw, 1.68), P(-0.3, 2.2), P(0.1, 2.62)];
    S.add('body2', `<path d="${taper(innerL, cw * R * 0.9)}" fill="${C.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
    S.add('body2', `<path d="${taper(shift(outerL, -0.2 * R, -0.05 * R).slice(0, 3), t => cw * R * 0.55)}" fill="${innerCol || '#ece4d4'}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
    S.add('body2', `<path d="${taper(outerL, t => cw * R * (1 + t * 0.4))}" fill="${C.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.9)}"/>`);
    S.add('body2', `<path d="${taper(shift(outerL, 0.1 * R, 0.02 * R), t => cw * R * 0.35)}" fill="${C.s}" opacity="0.8"/>`);
    if (S.o.lod >= 2 && sp.outfit.trimDots !== false) {
      let dd = '';
      for (const p of resample(shift(outerL, -0.02 * R, 0), 9).slice(1)) dd += circle(p[0], p[1], 1.1) + ` fill="${mix(C.b, '#fff', 0.45)}" opacity="0.7"/>`;
      S.add('body2', dd);
    }
  }
  function torsoBase(S, G, sp, T, fill, shade, extra) {
    const d = torsoD(G);
    S.shape('body', d, fill, () => {
      const P = G.P;
      S.path('body', spline([P(0.75, 1.4), P(1.15, 2.4), P(1.25, 3.2), P(1.1, 4.6, 0), P(4, 4.6, 0), P(4, 1.4, 0)], true), shade);
      S.path('body', spline([P(-0.9, 1.6), P(-0.3, 2.35), P(0.6, 2.3), P(1.2, 1.8), P(1.2, 1.2, 0), P(-0.9, 1.2, 0)], true), shade, 0, ' opacity="0.8"');
      if (extra) extra();
    }, S.lw, 'torso');
    rimOn(S, 'body', G.torso, T.rim, 3.4, 0.9);
  }
  // 甲片排（自下而上，上排压住下排），裁剪到躯干
  function lamellae(S, G, Mt, y0, y1, opt) {
    opt = opt || {};
    const R = G.R, lo = S.o.lod;
    const rowH = (opt.rowH || 0.2) * R;
    const xl = G.hx - 2.6 * R, xr = G.hx + 3.0 * R;
    const out = [];
    let row = 0;
    for (let y = y1; y >= y0; y -= rowH, row++) {
      let d = '';
      const pw = (opt.pw || 0.21) * R;
      const off = row % 2 ? pw / 2 : 0;
      for (let x = xl - off; x < xr; x += pw) {
        const k = clamp(0.62 + 0.38 * (x - xl) / (xr - xl), 0.6, 1.1);
        const w = pw * k * 0.96, h = rowH * 1.35;
        if (opt.scale) d += `M${N(x)},${N(y)}h${N(w)}v${N(h * 0.4)}q0,${N(h * 0.6)} ${N(-w / 2)},${N(h * 0.6)}q${N(-w / 2)},0 ${N(-w / 2)},${N(-h * 0.6)}z`;
        else d += `M${N(x)},${N(y)}h${N(w)}v${N(h - w * 0.4)}q0,${N(w * 0.4)} ${N(-w / 2)},${N(w * 0.4)}q${N(-w / 2)},0 ${N(-w / 2)},${N(-w * 0.4)}z`;
      }
      out.push(`<path d="${d}" fill="${row % 2 && opt.alt ? opt.alt : Mt.b}" stroke="${INK}" stroke-width="${lo === 0 ? 1.2 : 0.9}"/>`);
    }
    const P = G.P;
    S.add('body', `<g clip-path="url(#${S.id('torso')})">` + out.join('') +
      `<path d="${spline([P(0.75, 1.4), P(1.15, 2.4), P(1.25, 3.2), P(1.1, 4.6, 0), P(4, 4.6, 0), P(4, 1.4, 0)], true)}" fill="${Mt.d}" opacity="0.5"/>` +
      `<path d="${spline([P(-2.6, 2.4), P(-1.8, 2.2), P(-1.0, 2.5), P(-1.6, 3.3), P(-2.4, 3.4)], true)}" fill="${Mt.h}" opacity="0.2"/>` + '</g>');
  }
  // 披风（盖住两肩，胸前打开）
  function mantle(S, G, sp, T, col) {
    const P = G.P, R = G.R, bw = G.bw;
    const C = clothT(col);
    const near = [P(0.95 * G.nw, 1.42), P(1.7 * bw, 1.78), P(2.75 * bw, 2.3, 0.8), P(3.25 * bw, 3.0), P(3.5 * bw, 4.4, 0), P(1.5, 4.4, 0), P(1.25, 3.3), P(1.0, 2.4)];
    const far = [P(-0.52 * G.nw, 1.72), P(-0.9, 2.3), P(-1.35, 3.3), P(-1.55, 4.4, 0), P(-3.2 * bw, 4.4, 0), P(-3.0 * bw, 3.1), P(-2.55 * bw, 2.55, 0.8), P(-1.5 * bw, 2.08)];
    for (const [pts, sd] of [[near, 1], [far, -1]]) {
      S.shape('body2', spline(pts, true), sd > 0 ? C.s : C.b, () => {
        if (sd < 0) S.path('body2', spline([P(-1.4, 2.6), P(-1.2, 3.3), P(-1.45, 4.5), P(-0.6, 4.5, 0), P(-0.6, 2.3, 0)], true), C.s);
        else S.path('body2', spline([P(1.6, 1.8), P(2.6, 2.3), P(2.2, 2.6), P(1.4, 2.3)], true), C.b, 0, ' opacity="0.7"');
        let fd = '';
        for (let i = 0; i < 3; i++) fd += spline([P(sd * (1.6 + i * 0.5), 2.6 + i * 0.1), P(sd * (1.7 + i * 0.5), 3.4), P(sd * (1.65 + i * 0.55), 4.4)]);
        S.line('body2', fd, C.d, 1.4, ' opacity="0.6"');
      }, S.lw);
    }
    rimOn(S, 'body2', near, T.rim, 3.4, 0.9);
    // 领口扣结
    const k = P(0.15, 2.05);
    S.add('body2', circle(k[0], k[1], 0.16 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>` + circle(k[0] - 1, k[1] - 1, 0.06 * R) + ' fill="#fff6d0"/>');
  }
  const OUTFITS = {
    // 袍服
    robe(S, G, sp, T) {
      const C = clothT(sp.outfit.color || '#3a4a6a');
      const P = G.P;
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        if (S.o.lod >= 1) {
          S.line('body', spline([P(-1.9, 2.9), P(-1.7, 3.5), P(-1.75, 4.3)]) + spline([P(1.9, 2.7), P(2.05, 3.4), P(2.0, 4.3)]), C.d, 1.5, ' opacity="0.7"');
          S.line('body', spline([P(-2.3, 2.75), P(-2.2, 3.1)]), C.h, 1.4, ' opacity="0.6"');
        }
        if (S.o.lod >= 2 && sp.outfit.pattern !== false) {
          // 暗纹团花
          let pd = '';
          for (const [x, y] of [[-2.0, 3.6], [1.9, 3.3], [-0.8, 3.9], [2.6, 4.1]]) { const c = P(x, y); pd += circle(c[0], c[1], 0.32 * G.R) + '/>' + circle(c[0], c[1], 0.16 * G.R) + '/>'; }
          S.add('body', `<g fill="none" stroke="${C.h}" stroke-width="1.2" opacity="0.22">${pd.replace(/\/>/g, '/>')}</g>`);
        }
      });
      crossCollar(S, G, sp, T, sp.outfit.color2 || mix(C.b, '#000', 0.35), sp.outfit.inner);
    },
    // 札甲
    lamellar(S, G, sp, T) {
      const Mt = metalT(sp.outfit.metal || '#7a808c');
      const C = clothT(sp.outfit.color || '#7a2a2a');
      torsoBase(S, G, sp, T, Mt.s, Mt.d);
      lamellae(S, G, Mt, G.ey + 2.1 * G.R, 262);
      scarf(S, G, sp, T, C);
      pauldron(S, G, sp, T, Mt, -1);
      pauldron(S, G, sp, T, Mt, 1);
    },
    // 明光铠：胸前两面护心镜
    plate(S, G, sp, T) {
      const Mt = metalT(sp.outfit.metal || '#8a8f9a');
      const C = clothT(sp.outfit.color || '#8a2a24');
      const P = G.P, R = G.R, lo = S.o.lod;
      const trim = sp.outfit.trim || GOLD;
      torsoBase(S, G, sp, T, Mt.s, Mt.d);
      lamellae(S, G, Mt, G.ey + 3.45 * R, 262, { rowH: 0.17, pw: 0.18 });
      // 胸甲上缘
      const top = [P(-2.0, 2.45), P(-0.8, 2.3), P(0.5, 2.25), P(1.9, 2.35), P(2.1, 3.5, 0), P(-2.1, 3.65, 0)];
      S.shape('body', spline(top, true), Mt.b, () => {
        S.path('body', spline([P(0.9, 2.0), P(2.4, 2.2), P(2.4, 3.8), P(0.9, 3.8)], true), Mt.s);
        S.line('body', spline([P(-0.35, 2.3), P(-0.38, 3.0), P(-0.4, 3.6)]), Mt.d, 1.6);
      }, S.lw * 0.9);
      // 护心镜
      for (const [x, y, rx, ry] of [[-1.35, 3.0, 0.42, 0.5], [0.55, 2.95, 0.62, 0.58]]) {
        const c = P(x, y);
        S.add('body', ellipse(c[0], c[1], rx * R + 2.4, ry * R + 2.4) + ` fill="${trim}" stroke="${INK}" stroke-width="${N(S.lw * 0.9)}"/>`);
        S.add('body', ellipse(c[0], c[1], rx * R, ry * R) + ` fill="${Mt.b}" stroke="${INK}" stroke-width="1"/>`);
        S.add('body', ellipse(c[0] + rx * R * 0.25, c[1] + ry * R * 0.2, rx * R * 0.7, ry * R * 0.7) + ` fill="${Mt.s}" opacity="0.6"/>`);
        S.add('body', ellipse(c[0] - rx * R * 0.35, c[1] - ry * R * 0.35, rx * R * 0.3, ry * R * 0.18, -30) + ` fill="${Mt.h}" opacity="0.9"/>`);
      }
      // 束甲绊（金带）
      S.add('body', `<path d="${taper([P(-2.2, 3.65), P(0, 3.55), P(2.2, 3.5)], 0.14 * R)}" fill="${trim}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
      scarf(S, G, sp, T, C);
      pauldron(S, G, sp, T, Mt, -1, trim);
      pauldron(S, G, sp, T, Mt, 1, trim, lo >= 1);
    },
    // 袍内衬甲：外罩战袍，肩披甲
    robearmor(S, G, sp, T) {
      const C = clothT(sp.outfit.color || '#3a6a3a');
      const Mt = metalT(sp.outfit.metal || '#7a808c');
      const P = G.P, R = G.R;
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        if (S.o.lod >= 1) S.line('body', spline([P(-1.9, 2.9), P(-1.7, 3.5), P(-1.75, 4.3)]) + spline([P(1.9, 2.7), P(2.05, 3.4), P(2.0, 4.3)]), C.d, 1.5, ' opacity="0.7"');
      });
      // 胸前露出甲片
      const v = [P(-0.48 * G.nw, 1.75), P(0.62, 1.95), P(-0.2, 2.75), P(-1.0, 3.5)];
      S.add('body', `<g clip-path="url(#${S.id('torso')})"><path d="${spline([P(-0.5, 1.7), P(0.65, 1.9), P(-0.3, 2.9), P(-1.2, 3.8), P(-1.3, 2.4)], true)}" fill="${Mt.b}"/></g>`);
      crossCollar(S, G, sp, T, sp.outfit.color2 || mix(C.b, '#000', 0.35), Mt.h);
      pauldron(S, G, sp, T, Mt, -1, sp.outfit.trim);
      pauldron(S, G, sp, T, Mt, 1, sp.outfit.trim, S.o.lod >= 1);
      void v;
    },
  };
  // 领巾：绕颈一圈的卷边布巾，近侧打结垂两短尾
  function scarf(S, G, sp, T, C) {
    const P = G.P, R = G.R, nw = G.nw;
    const ring = [P(-0.6 * nw, 1.6), P(-0.25, 1.92), P(0.3, 1.96), P(0.82, 1.72), P(1.02 * nw, 1.42)];
    const d = taper(ring, t => (0.34 - 0.08 * Math.abs(t - 0.4)) * R);
    S.shape('body2', d, C.b, () => {
      S.path('body2', spline([P(0.45, 1.5), P(1.3, 1.2), P(1.3, 2.4), P(0.4, 2.3)], true), C.s);
      S.path('body2', taper(shift(ring.slice(0, 3), 0, -0.08 * R), t => 0.08 * R * Math.sin(Math.PI * t) + 0.3), C.h, 0, ' opacity="0.7"');
      if (S.o.lod >= 1) S.line('body2', spline(shift(ring, 0, 0.06 * R).slice(0, 4)), C.d, 1.1, ' opacity="0.6"');
    }, S.lw * 0.9);
    const k = P(0.42, 2.02);
    clumps(S, 'body2', [
      { d: leaf(add(k, 0.02 * R, 0.08 * R), add(k, 0.3 * R, 0.72 * R), 0.26 * R, -0.12), fill: C.s },
      { d: leaf(add(k, -0.04 * R, 0.08 * R), add(k, -0.12 * R, 0.62 * R), 0.24 * R, 0.1), fill: C.b },
      { d: spline([add(k, -0.18 * R, -0.12 * R), add(k, 0.14 * R, -0.16 * R), add(k, 0.2 * R, 0.1 * R), add(k, -0.14 * R, 0.14 * R)], true), fill: C.b },
    ], S.lw * 0.45);
  }
  function pauldron(S, G, sp, T, Mt, side, trim, beast) {
    const P = G.P, R = G.R, lo = S.o.lod;
    const bw = G.bw;
    const sx = side > 0 ? 2.45 * bw : -2.3 * bw, sy = side > 0 ? 2.45 : 2.7;
    const w = side > 0 ? 1.15 : 0.85;
    for (let i = 2; i >= 0; i--) {
      const yy = sy + i * 0.32;
      const pts = [P(sx - side * w * 0.95, yy - 0.18, 0.5), P(sx, yy - 0.55), P(sx + side * w * 0.95, yy - 0.05), P(sx + side * w * 0.9, yy + 0.32, 0.5), P(sx, yy + 0.12), P(sx - side * w * 0.9, yy + 0.18, 0.5)];
      const d = spline(pts, true);
      S.shape('body2', d, i === 0 ? Mt.b : mix(Mt.b, Mt.s, i * 0.3), () => {
        S.path('body2', spline(shift(pts, side * -0.25 * R, -0.12 * R), true), Mt.h, 0, ' opacity="0.35"');
        S.path('body2', spline(shift(pts, side * 0.45 * R, 0.1 * R), true), Mt.d, 0, ' opacity="0.5"');
      }, S.lw * 0.9);
      if (lo >= 1) S.line('body2', spline([P(sx - side * w * 0.8, yy + 0.08), P(sx, yy - 0.05), P(sx + side * w * 0.8, yy + 0.22)]), trim || GOLD, 1.3, ' opacity="0.9"');
    }
    if (beast) beastBoss(S, G, P(sx - side * 0.1, sy - 0.14), 0.36 * R, trim || GOLD);
  }

  // 兽吞（肩甲上的兽面）：小尺寸时只画金色圆钉
  function beastBoss(S, G, c, r, col) {
    const Mt = metalT(col), lo = S.o.lod;
    if (lo < 2) {
      S.add('body2', circle(c[0], c[1], r * 0.62) + ` fill="${Mt.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>` + circle(c[0] - r * 0.2, c[1] - r * 0.2, r * 0.2) + ` fill="${Mt.h}"/>`);
      return;
    }
    // 鬃毛火焰纹
    const mane = [];
    for (let i = 0; i < 10; i++) {
      const a = Math.PI * 2 * i / 10 + 0.3;
      mane.push({ d: leaf(c, [c[0] + Math.cos(a) * r * 1.25, c[1] + Math.sin(a) * r * 1.15], r * 0.7, 0.18), fill: i % 2 ? Mt.s : Mt.b });
    }
    clumps(S, 'body2', mane, S.lw * 0.45);
    const face = spline([[c[0] - r * 0.78, c[1] - r * 0.45], [c[0], c[1] - r * 0.72], [c[0] + r * 0.78, c[1] - r * 0.45], [c[0] + r * 0.62, c[1] + r * 0.35], [c[0], c[1] + r * 0.8], [c[0] - r * 0.62, c[1] + r * 0.35]], true);
    S.shape('body2', face, Mt.b, () => {
      S.path('body2', spline([[c[0] + r * 0.1, c[1] - r], [c[0] + r, c[1] - r * 0.4], [c[0] + r * 0.6, c[1] + r], [c[0] + r * 0.1, c[1] + r]], true), Mt.s);
    }, S.lw * 0.6);
    // 怒眉、眼、鼻、獠牙
    S.add('body2', `<path d="M${N(c[0] - r * 0.62)},${N(c[1] - r * 0.38)}L${N(c[0] - r * 0.08)},${N(c[1] - r * 0.12)}L${N(c[0] - r * 0.1)},${N(c[1] - r * 0.28)}ZM${N(c[0] + r * 0.62)},${N(c[1] - r * 0.38)}L${N(c[0] + r * 0.08)},${N(c[1] - r * 0.12)}L${N(c[0] + r * 0.1)},${N(c[1] - r * 0.28)}Z" fill="${INK}"/>`);
    S.add('body2', ellipse(c[0] - r * 0.3, c[1] - r * 0.12, r * 0.13, r * 0.08) + ' fill="#c02a20"/>' + ellipse(c[0] + r * 0.3, c[1] - r * 0.12, r * 0.13, r * 0.08) + ' fill="#c02a20"/>');
    S.add('body2', `<path d="M${N(c[0] - r * 0.16)},${N(c[1] + r * 0.02)}h${N(r * 0.32)}l${N(-r * 0.06)},${N(r * 0.2)}h${N(-r * 0.2)}z" fill="${Mt.d}"/>`);
    S.add('body2', `<path d="M${N(c[0] - r * 0.42)},${N(c[1] + r * 0.32)}Q${N(c[0])},${N(c[1] + r * 0.62)} ${N(c[0] + r * 0.42)},${N(c[1] + r * 0.32)}Z" fill="#3a1010" stroke="${INK}" stroke-width="0.8"/>`);
    S.add('body2', `<path d="M${N(c[0] - r * 0.3)},${N(c[1] + r * 0.36)}l${N(r * 0.08)},${N(r * 0.22)}l${N(r * 0.06)},${N(-r * 0.18)}ZM${N(c[0] + r * 0.3)},${N(c[1] + r * 0.36)}l${N(-r * 0.08)},${N(r * 0.22)}l${N(-r * 0.06)},${N(-r * 0.18)}Z" fill="#f4eee0"/>`);
  }

  // ======================================================== 兵器（肩后） ==
  function drawWeapon(S, G, sp, T) {
    const w = sp.weapon;
    if (!w || S.o.lod === 0) return;
    const R = G.R, P = G.P;
    const pole = (x0, y0, x1, y1, col) => {
      S.add('back', `<path d="M${N(x0)},${N(y0)}L${N(x1)},${N(y1)}" stroke="${INK}" stroke-width="${N(0.2 * R + 2)}" fill="none"/>`);
      S.add('back', `<path d="M${N(x0)},${N(y0)}L${N(x1)},${N(y1)}" stroke="${col || '#5a3a24'}" stroke-width="${N(0.2 * R)}" fill="none"/>`);
    };
    const blade = (d, col) => S.add('back', `<path d="${d}" fill="${col || '#c8d0d8'}" stroke="${INK}" stroke-width="${N(S.lw * 0.9)}"/>`);
    const top = P(-2.25, -2.3), bot = P(-1.7, 4.4);
    if (w === 'guandao') {
      pole(bot[0], bot[1], top[0] + 2, top[1] + 0.4 * R, '#3a5a3a');
      const b = top;
      blade(spline([add(b, 2, 0.6 * R), add(b, -0.5 * R, 0.3 * R), add(b, -0.7 * R, -0.5 * R), add(b, -0.2 * R, -1.6 * R, 0), add(b, 0.25 * R, -0.6 * R), add(b, 0.3 * R, 0.4 * R)], true));
      S.line('back', spline([add(b, 0, 0.4 * R), add(b, -0.35 * R, -0.3 * R), add(b, -0.15 * R, -1.2 * R)]), '#fff', 1.4, ' opacity="0.7"');
      S.add('back', ellipse(b[0] + 0.05 * R, b[1] + 0.55 * R, 0.2 * R, 0.16 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1.4"/>`);
      tassel(S, 'back', G, add(b, 0.1 * R, 0.7 * R), '#c0302a', 4, 0.6, 1);
    } else if (w === 'snake') {
      pole(bot[0], bot[1], top[0], top[1], '#2a2a2a');
      const b = top;
      blade(spline([add(b, -0.12 * R, 0.1 * R), add(b, -0.22 * R, -0.3 * R), add(b, 0.05 * R, -0.6 * R), add(b, -0.18 * R, -0.95 * R), add(b, 0.0 * R, -1.4 * R, 0), add(b, 0.1 * R, -0.95 * R), add(b, 0.22 * R, -0.6 * R), add(b, 0.05 * R, -0.3 * R), add(b, 0.12 * R, 0.1 * R)], true));
      tassel(S, 'back', G, add(b, 0, 0.2 * R), '#c0302a', 4, 0.5, 1);
    } else if (w === 'halberd') {
      pole(bot[0], bot[1], top[0], top[1], '#6a2a20');
      const b = top;
      blade(spline([add(b, 0, -1.4 * R, 0), add(b, 0.12 * R, -0.6 * R), add(b, 0.1 * R, 0.1 * R), add(b, -0.1 * R, 0.1 * R), add(b, -0.12 * R, -0.6 * R)], true));
      blade(spline([add(b, -0.08 * R, -0.35 * R), add(b, -0.65 * R, -0.75 * R), add(b, -0.85 * R, -0.25 * R), add(b, -0.55 * R, 0.15 * R, 0), add(b, -0.6 * R, -0.25 * R), add(b, -0.08 * R, 0.0)], true));
      blade(spline([add(b, 0.08 * R, -0.35 * R), add(b, 0.55 * R, -0.75 * R), add(b, 0.75 * R, -0.25 * R), add(b, 0.45 * R, 0.15 * R, 0), add(b, 0.5 * R, -0.25 * R), add(b, 0.08 * R, 0.0)], true));
      tassel(S, 'back', G, add(b, 0, 0.25 * R), '#c0302a', 4, 0.55, 1);
    } else if (w === 'spear') {
      pole(bot[0], bot[1], top[0], top[1], '#5a3a24');
      blade(spline([add(top, 0, -1.2 * R, 0), add(top, 0.16 * R, -0.4 * R), add(top, 0.06 * R, 0.1 * R), add(top, -0.06 * R, 0.1 * R), add(top, -0.16 * R, -0.4 * R)], true));
      tassel(S, 'back', G, add(top, 0, 0.18 * R), sp.weaponColor || '#c0302a', 4, 0.55, 1);
    } else if (w === 'ji') {
      for (const dx of [0, 0.6]) {
        const t2 = add(top, dx * R, 0.3 * R), b2 = add(bot, dx * R, 0);
        pole(b2[0], b2[1], t2[0], t2[1], '#3a2a20');
        blade(spline([add(t2, 0, -0.9 * R, 0), add(t2, 0.12 * R, -0.3 * R), add(t2, -0.12 * R, -0.3 * R)], true));
        blade(spline([add(t2, -0.06 * R, -0.25 * R), add(t2, -0.55 * R, -0.5 * R), add(t2, -0.6 * R, 0.05 * R, 0), add(t2, -0.06 * R, 0.05 * R)], true));
      }
    } else if (w === 'bow') {
      const c = P(-2.4, 0.6);
      S.add('back', `<path d="M${N(c[0] + 0.6 * R)},${N(c[1] - 2.6 * R)}q${N(-1.6 * R)},${N(2.6 * R)} 0,${N(5.2 * R)}" fill="none" stroke="${INK}" stroke-width="${N(0.22 * R + 2)}"/>`);
      S.add('back', `<path d="M${N(c[0] + 0.6 * R)},${N(c[1] - 2.6 * R)}q${N(-1.6 * R)},${N(2.6 * R)} 0,${N(5.2 * R)}" fill="none" stroke="#7a4a24" stroke-width="${N(0.22 * R)}"/>`);
      S.add('back', `<path d="M${N(c[0] + 0.6 * R)},${N(c[1] - 2.6 * R)}v${N(5.2 * R)}" stroke="#e8e0d0" stroke-width="1"/>`);
    } else if (w === 'sword' || w === 'swords') {
      // 背后剑柄从远侧肩头探出
      const hilts = w === 'swords' ? [[-2.05, 2.8, -0.4], [-1.7, 2.8, -0.2]] : [[-1.9, 2.8, -0.32]];
      for (const [hx0, hy0, sl] of hilts) {
        const h0 = P(hx0, hy0), h1 = add(h0, sl * R, -1.5 * R);
        S.add('back', `<path d="M${ps(h0)}L${ps(h1)}" stroke="${INK}" stroke-width="${N(0.2 * R + 2.4)}"/><path d="M${ps(h0)}L${ps(h1)}" stroke="#3a2418" stroke-width="${N(0.2 * R)}"/>`);
        if (S.o.lod >= 2) S.add('back', `<path d="M${ps(h0)}L${ps(h1)}" stroke="#7a5a3a" stroke-width="${N(0.2 * R)}" stroke-dasharray="2 3" stroke-linecap="butt"/>`);
        const g = lp(h0, h1, 0.3);
        S.add('back', `<path d="M${ps(add(g, -0.32 * R, -0.05 * R))}L${ps(add(g, 0.32 * R, 0.05 * R))}" stroke="${INK}" stroke-width="${N(0.14 * R + 2.4)}"/><path d="M${ps(add(g, -0.3 * R, -0.05 * R))}L${ps(add(g, 0.3 * R, 0.05 * R))}" stroke="${GOLD}" stroke-width="${N(0.14 * R)}"/>`);
        S.add('back', circle(h1[0], h1[1], 0.12 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1.3"/>`);
        tassel(S, 'back', G, h1, '#c0302a', 3, 0.45, -1);
      }
    } else if (w === 'axe') {
      pole(bot[0], bot[1], top[0], top[1] + 0.2 * R, '#5a3a24');
      blade(spline([add(top, 0, 0.0), add(top, -0.8 * R, -0.5 * R), add(top, -0.95 * R, 0.4 * R), add(top, -0.8 * R, 1.0 * R), add(top, 0, 0.6 * R)], true));
    } else if (w === 'fan') {
      // 羽扇放在胸前，见 drawFan
    }
  }
  // 羽扇（诸葛亮）：胸前斜持的白鹤羽扇——宽羽层叠成圆润扇面，羽轴细线，扇柄金箍
  function drawFan(S, G, sp, T) {
    if (!(sp.acc || []).includes('fan')) return;
    const R = G.R, P = G.P, lo = S.o.lod;
    const base = P(-1.35, 3.55);
    const feather = (a, b, w) => {
      const dx = b[0] - a[0], dy = b[1] - a[1], l = hyp(dx, dy), nx = -dy / l, ny = dx / l;
      const at = (t, k) => [a[0] + dx * t + nx * w * k, a[1] + dy * t + ny * w * k];
      return spline([a, at(0.25, 0.22), at(0.6, 0.48), at(0.86, 0.36), [b[0], b[1], 0], at(0.86, -0.3), at(0.6, -0.42), at(0.25, -0.2)], true);
    };
    const items = [], spines = [], tipsD = [];
    const n = 9;
    for (let i = 0; i < n; i++) {
      const u = i / (n - 1);
      const ang = -0.78 + u * 1.2;                       // 扇面略向左倾
      const len = 2.05 * R * (0.84 + 0.16 * Math.sin(Math.PI * u));
      const tip = add(base, Math.sin(ang) * len, -Math.cos(ang) * len);
      const fd = feather(base, tip, 0.62 * R);
      items.push({ d: fd, fill: i % 2 ? '#e8e5dc' : '#f8f6f0' });
      tipsD.push(fd);
      spines.push(spline([add(base, Math.sin(ang) * 0.3 * R, -Math.cos(ang) * 0.3 * R), add(base, Math.sin(ang) * len * 0.9, -Math.cos(ang) * len * 0.9)]));
    }
    clumps(S, 'front', items, S.lw * 0.7);
    // 背光一侧的阴影、灰色羽尖（鹤羽）
    const fanD = tipsD.join('');
    S.add('front', `<g clip-path="${S.clip('fan', fanD)}">` +
      `<path d="${spline([add(base, 0.15 * R, 0), add(base, 0.75 * R, -1.3 * R), add(base, 1.1 * R, -2.6 * R, 0), add(base, 2.4 * R, -1.0 * R, 0)], true)}" fill="#c9c4b8" opacity="0.9"/>` +
      `<path d="${spline(arcPts(base[0], base[1], 2.4 * R, 2.4 * R, 2.5, 0.9, 10).concat(arcPts(base[0], base[1], 1.66 * R, 1.66 * R, 0.9, 2.5, 10)), true)}" fill="#6a6660" opacity="0.55"/>` +
      '</g>');
    if (lo >= 1) S.line('front', spines.join(''), '#9a948a', 0.9, ' opacity="0.8"');
    // 扇柄与金箍
    const h0 = add(base, -0.02 * R, -0.1 * R), h1 = add(base, 0.18 * R, 1.0 * R);
    S.add('front', `<path d="M${ps(h0)}L${ps(h1)}" stroke="${INK}" stroke-width="${N(0.2 * R + 2.2)}"/><path d="M${ps(h0)}L${ps(h1)}" stroke="#5a3a22" stroke-width="${N(0.2 * R)}"/>`);
    S.add('front', ellipse(base[0], base[1] - 0.05 * R, 0.2 * R, 0.13 * R, -10) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1.2"/>`);
    // 握扇的手（简化：袖口 + 手指）
    const hand = add(base, 0.12 * R, 0.55 * R);
    const K = T.skin;
    S.add('front', `<path d="${spline([add(hand, -0.28 * R, -0.12 * R), add(hand, 0.05 * R, -0.24 * R), add(hand, 0.32 * R, -0.08 * R), add(hand, 0.3 * R, 0.2 * R), add(hand, -0.05 * R, 0.3 * R), add(hand, -0.3 * R, 0.14 * R)], true)}" fill="${K.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
    if (lo >= 1) S.line('front', spline([add(hand, -0.12 * R, -0.05 * R), add(hand, 0.18 * R, 0.02 * R)]) + spline([add(hand, -0.14 * R, 0.08 * R), add(hand, 0.16 * R, 0.14 * R)]), K.line, 1.0, ' opacity="0.7"');
  }

  // ======================================================== 各文化发型 ==
  // 发辫：沿折线的一串交错发节
  function braid(S, layer, G, pts, w, H, tie) {
    const R = G.R, items = [];
    const seg = resample(pts, Math.max(4, Math.round(pathLen(pts) / (w * 0.85))));
    for (let i = 0; i < seg.length - 1; i++) {
      const a = seg[i], b = seg[i + 1];
      items.push({ d: leaf(lp(a, b, -0.15), lp(a, b, 1.25), w * (1 - i / seg.length * 0.35), i % 2 ? 0.18 : -0.18), fill: i % 2 ? H.b : H.s });
    }
    clumps(S, layer, items, S.lw * 0.55);
    const e = seg[seg.length - 1];
    if (tie) S.add(layer, ellipse(e[0], e[1], w * 0.45, w * 0.3) + ` fill="${tie}" stroke="${INK}" stroke-width="1.2"/>`);
    void R;
  }
  function pathLen(pts) { let l = 0; for (let i = 0; i < pts.length - 1; i++) l += hyp(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]); return l; }
  // 环形发髻（角髪）
  function hairLoop(S, layer, G, c, rx, ry, H) {
    const d = ellipsePath(c[0], c[1], rx, ry) + ellipsePath(c[0] + rx * 0.08, c[1], rx * 0.45, ry * 0.5);
    S.add(layer, `<path d="${d}" fill="${H.b}" fill-rule="evenodd" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
    S.add(layer, `<path d="${spline([[c[0] - rx * 0.8, c[1] - ry * 0.3], [c[0] - rx * 0.3, c[1] - ry * 0.85], [c[0] + rx * 0.2, c[1] - ry * 0.8]])}" fill="none" stroke="${H.h}" stroke-width="1.4" opacity="0.8"/>`);
  }
  function ellipsePath(cx, cy, rx, ry) {
    return `M${N(cx - rx)},${N(cy)}a${N(rx)},${N(ry)} 0 1,0 ${N(2 * rx)},0a${N(rx)},${N(ry)} 0 1,0 ${N(-2 * rx)},0Z`;
  }
  // 中分发际线
  function partLine(G) {
    const P = G.P;
    return [P(-0.95, -0.5), P(-0.8, -0.86), P(-0.46, -0.99, 0.2), P(-0.36, -0.88), P(0.15, -0.86), P(0.42, -0.62), P(0.46, -0.3), P(0.48, -0.06, 0.3), P(0.62, -0.06, 0.3), P(0.66, 0.35), P(0.96, 0.4)];
  }
  Object.assign(HAIR, {
    // 椎髻（南中）：脑后上方一个大髻，骨簪横插
    mallet(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      hairCap(S, G, sp, T, H, { target: P(0.6, -1.4) });
      if (hatInfo.coversTop) return;
      const bx = G.hx + 0.62 * R, by = G.ey - 1.42 * R;
      bun(S, G, H, bx, by, 0.5 * R);
      S.add('hair', `<path d="${taper([[bx - 0.48 * R, by + 0.26 * R], [bx, by + 0.42 * R], [bx + 0.46 * R, by + 0.28 * R]], 0.16 * R)}" fill="${sp.hair.tie || '#a83220'}" stroke="${INK}" stroke-width="1.1"/>`);
      for (const [dx, dy] of [[0, 0], [0.12, 0.28]]) {
        const a = [bx - 0.85 * R + dx * R, by - 0.05 * R + dy * R], b = [bx + 0.75 * R + dx * R, by - 0.5 * R + dy * R];
        S.add('hair', `<path d="${taper([a, lp(a, b, 0.5), b], t => 0.12 * R * (1 - Math.abs(t - 0.5)) + 0.6)}" fill="#efe4c8" stroke="${INK}" stroke-width="1.1"/>`);
      }
    },
    // 蓬乱长发（蛮族、草莽）
    wild(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P, e = G.ell;
      const back = [];
      for (let i = 0; i < 7; i++) back.push({ d: leaf(P(0.2 + i * 0.16, -0.9 + i * 0.12), P(0.7 + i * 0.22 + (i % 2) * 0.1, 1.9 + (i % 3) * 0.35), 0.5 * R, (i % 2 ? 0.08 : -0.04)), fill: i % 2 ? H.s : H.d });
      back.push({ d: leaf(P(-0.85, -0.8), P(-1.15, 1.2), 0.38 * R, -0.08), fill: H.s });
      clumps(S, 'back', back, S.lw * 0.6);
      hairCap(S, G, sp, T, H, { vol: 0.1, target: P(0.4, -0.4) });
      if (!hatInfo.coversTop) {
        const rx = G.silRx(1.1), ry = G.silRy(1.1), items = [];
        for (let i = 0; i < 9; i++) {
          const a = -0.25 + i * 0.4;
          const root = [e.Cx + rx * 0.85 * Math.cos(a), e.Cy - ry * 0.85 * Math.sin(a)];
          const tip = [e.Cx + rx * 1.28 * Math.cos(a + 0.12), e.Cy - ry * 1.22 * Math.sin(a + 0.12)];
          items.push({ d: leaf(root, tip, 0.42 * R, 0.12), fill: i % 2 ? H.b : H.s });
        }
        clumps(S, 'hair', items, S.lw * 0.6);
      }
      // 额前乱发
      const bangs = [];
      for (let i = 0; i < 4; i++) bangs.push({ d: leaf(P(-0.8 + i * 0.3, -1.08), P(-0.75 + i * 0.32 + (i % 2) * 0.08, -0.62 - (i % 2) * 0.08), 0.24 * R, i % 2 ? 0.12 : -0.1), fill: i % 2 ? H.s : H.b });
      clumps(S, 'hair', bangs, S.lw * 0.5);
    },
    // 美豆良（角髪）：中分，两耳旁结成发环
    mizura(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      hairCap(S, G, sp, T, H, { hairline: partLine(G), target: P(0.6, -0.2), vol: 0.05 });
      hairLoop(S, 'hat', G, P(0.66, 0.08), 0.2 * R, 0.26 * R, H);
      hairLoop(S, 'hat', G, P(0.7, 0.55), 0.22 * R, 0.28 * R, H);
      S.add('hat', `<path d="${taper([P(0.5, 0.3), P(0.68, 0.32), P(0.88, 0.3)], 0.1 * R)}" fill="${sp.hair.tie || '#e8e0d0'}" stroke="${INK}" stroke-width="1.1"/>`);
      hairLoop(S, 'back', G, P(-1.04, 0.1), 0.12 * R, 0.24 * R, H);
      hairLoop(S, 'back', G, P(-1.06, 0.55), 0.13 * R, 0.26 * R, H);
      G.hideEar = true;
    },
    // 倭女：中分长直发，耳前垂发
    wahair(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      S.shape('back', spline([P(0.2, -1.2), P(1.2, -0.6), P(1.45, 0.8), P(1.6, 2.6, 0), P(0.4, 2.7, 0), P(0.6, 0.6)], true), H.s, null, S.lw);
      hairCap(S, G, sp, T, H, { hairline: partLine(G), target: P(0.8, 0.2), vol: 0.08 });
      clumps(S, 'front', [
        { d: taper([P(0.46, -0.5), P(0.56, 0.6), P(0.62, 1.6), P(0.5, 2.6)], t => 0.26 * R * (1 - t * 0.5)), fill: H.b },
        { d: taper([P(-0.92, -0.4), P(-1.0, 0.8), P(-1.05, 2.2)], t => 0.2 * R * (1 - t * 0.5)), fill: H.b },
      ], S.lw * 0.55);
      G.hideEar = true;
    },
    // 朝鲜女子：中分，脑后一条长辫系红绳
    koreanbraid(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      hairCap(S, G, sp, T, H, { hairline: partLine(G), target: P(1.0, 0.0), vol: 0.06 });
      braid(S, 'back', G, [P(1.05, 0.3), P(1.25, 1.2), P(1.3, 2.4), P(1.2, 3.3)], 0.32 * R, H, '#c02a30');
    },
    // 髡发：头顶剃净（青色头皮），额前留一小撮，两鬓垂辫
    kunfa(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      if (!hatInfo.coversTop) {
        const hl = hairline(G, sp);
        S.headShade.push(`<path d="${spline(hl.concat(hairBack(G, 0, 0.5)), true)}" fill="#3a4a5a" opacity="0.28"/>`);
        clumps(S, 'hair', [0, 1, 2].map(i => ({ d: leaf(P(-0.62 + i * 0.12, -1.05), P(-0.6 + i * 0.13, -0.78), 0.16 * R, 0.1), fill: i % 2 ? H.s : H.b })), S.lw * 0.5);
      }
      braid(S, 'hat', G, [P(0.5, -0.4), P(0.52, 0.4), P(0.58, 1.2), P(0.62, 1.7)], 0.22 * R, H, sp.hair.tie || '#c8a030');
      braid(S, 'back', G, [P(-0.95, -0.35), P(-1.02, 0.5), P(-1.08, 1.4)], 0.18 * R, H, sp.hair.tie || '#c8a030');
    },
    // 双辫垂肩
    braids(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      hairCap(S, G, sp, T, H, { hairline: sp.sex === 'f' ? partLine(G) : null, target: P(0.6, -0.6) });
      braid(S, 'front', G, [P(0.82, 0.4), P(1.0, 1.3), P(0.95, 2.3), P(0.85, 3.0)], 0.3 * R, H, sp.hair.tie || '#b03028');
      braid(S, 'back', G, [P(-0.9, 0.0), P(-1.15, 1.0), P(-1.22, 2.2)], 0.24 * R, H, sp.hair.tie || '#b03028');
    },
    // 高髻（南海）：头顶一个高髻，金箍
    highbun(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      hairCap(S, G, sp, T, H, { target: P(0.0, -1.7) });
      if (hatInfo.coversTop) return;
      const bx = G.hx - 0.05 * R, by = G.ey - 1.85 * R;
      bun(S, G, H, bx, by, 0.4 * R);
      S.add('hair', `<path d="${taper([[bx - 0.4 * R, by + 0.3 * R], [bx, by + 0.42 * R], [bx + 0.4 * R, by + 0.3 * R]], 0.16 * R)}" fill="${GOLD}" stroke="${INK}" stroke-width="1.1"/>`);
      if (sp.hair.flower !== false) S.add('hair', flower(bx + 0.42 * R, by + 0.25 * R, 0.16 * R, sp.hair.flower || '#f0e0a0'));
    },
    // 齐耳短发（龟兹）：齐眉刘海，两侧垂到下颌
    bob(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      S.shape('back', spline([P(-0.9, -0.6), P(-1.12, 0.2), P(-1.1, 1.0, 0), P(-0.82, 1.0, 0), P(-0.9, 0.2)], true), H.s, null, S.lw);
      const hl = [P(-0.96, -0.55), P(-0.86, -0.64, 0), P(-0.6, -0.62), P(-0.3, -0.64), P(0.0, -0.62), P(0.3, -0.64, 0), P(0.42, -0.55), P(0.42, 0.95, 0), P(0.7, 1.08), P(1.12, 0.95, 0)];
      const back = [[G.ell.Cx + G.silRx(1.08) * 1.0, G.ell.Cy + G.silRy(1.08) * 0.5], ...arcPts(G.ell.Cx, G.ell.Cy, G.silRx(1.08), G.silRy(1.08), -0.2, Math.PI * 0.97, 9)];
      hairCap(S, G, sp, T, H, { hairline: hl, back, strands: false, extra: () => {
        let sd = '';
        for (let i = 0; i < 7; i++) sd += spline([P(-0.8 + i * 0.22, -1.2), P(-0.78 + i * 0.22, -0.9), P(-0.76 + i * 0.22, -0.66)]);
        for (let i = 0; i < 3; i++) sd += spline([P(0.5 + i * 0.18, -0.6), P(0.55 + i * 0.2, 0.2), P(0.55 + i * 0.2, 0.95)]);
        S.line('hair', sd, H.d, 1.0, ' opacity="0.6"');
      } });
      G.hideEar = true;
    },
    // 罗马式短发：贴头，额前短刘海
    short(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      const hl = [P(-0.95, -0.52), P(-0.86, -0.76), P(-0.66, -0.8, 0.3), P(-0.56, -0.74, 0.3), P(-0.42, -0.82, 0.3), P(-0.28, -0.76, 0.3), P(-0.12, -0.84, 0.3), P(0.1, -0.8, 0.3), P(0.32, -0.8), P(0.44, -0.58), P(0.47, -0.36), P(0.49, -0.08, 0.3), P(0.6, -0.08, 0.3), P(0.64, 0.3), P(0.94, 0.32)];
      hairCap(S, G, sp, T, H, { hairline: hl, vol: 0.04, strands: false, shine: false, bumps: hatInfo.coversTop ? 0 : 10, extra: () => {
        if (S.o.lod < 1) return;
        let cd = '', hd = '';
        const rr = sub(sp.name, 'curl');
        for (let i = 0; i < 30; i++) {
          const lam = rr.range(-1.0, 1.7), bet = rr.range(0.3, 1.35);
          const p = G.h3(lam, bet, 1.03);
          const d = `M${N(p[0] + 1.5)},${N(p[1] - 1.5)}q${N(-3)},${N(0.5)} ${N(-3.2)},${N(3.8)}`;
          if (lam < 0.3 && bet > 0.6 && i % 2) hd += d; else cd += d;
        }
        S.line('hair', cd, H.d, 1.2, ' opacity="0.75"');
        S.line('hair', hd, H.h, 1.4, ' opacity="0.85"');
      } });
    },
    // 短卷发：外缘起伏，内有小卷
    curly(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      const hl = [P(-0.95, -0.52), P(-0.86, -0.8), P(-0.6, -0.86, 0.4), P(-0.4, -0.8, 0.4), P(-0.2, -0.88, 0.4), P(0.1, -0.84, 0.4), P(0.32, -0.8), P(0.44, -0.58), P(0.47, -0.36), P(0.49, 0.0, 0.3), P(0.6, 0.0, 0.3), P(0.64, 0.4), P(0.98, 0.4)];
      hairCap(S, G, sp, T, H, { hairline: hl, vol: 0.08, bumps: hatInfo.coversTop ? 0 : 8, strands: false, shine: false, extra: () => {
        if (S.o.lod < 1) return;
        let cd = '', hd = '';
        const rr = sub(sp.name, 'curl');
        for (let i = 0; i < 26; i++) {
          const lam = rr.range(-1.0, 1.9), bet = rr.range(0.1, 1.35);
          const p = G.h3(lam, bet, 1.06);
          const r0 = rr.range(2.2, 3.4);
          cd += `M${N(p[0] + r0)},${N(p[1])}a${N(r0)},${N(r0)} 0 1,0 ${N(-r0)},${N(r0)}`;
          if (lam < 0.4 && bet > 0.5) hd += `M${N(p[0] - r0 * 0.6)},${N(p[1] - r0 * 0.4)}a${N(r0)},${N(r0)} 0 0,1 ${N(r0)},${N(-r0 * 0.5)}`;
        }
        S.line('hair', cd, H.d, 1.2, ' opacity="0.75"');
        S.line('hair', hd, H.h, 1.3, ' opacity="0.8"');
      } });
    },
    // 安息式浓密卷发：脑后与两鬓蓬起
    bushy(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      const puffs = [[0.95, -0.55, 0.42], [1.25, -0.05, 0.46], [1.25, 0.5, 0.44], [0.95, 0.85, 0.4], [1.5, 0.3, 0.32], [-1.02, 0.15, 0.26], [-1.0, 0.5, 0.22]];
      let pd = '';
      for (const [x, y, r] of puffs) { const c = P(x, y); pd += circle(c[0], c[1], r * R) + '/>'; }
      S.add('back', `<g fill="${H.b}" stroke="${INK}" stroke-width="${N(S.lw * 2)}">${pd}</g><g fill="${H.b}">${pd}</g>`);
      if (S.o.lod >= 1) {
        let cd = '';
        for (const [x, y, r] of puffs) for (let k = 0; k < 3; k++) { const c = P(x + Math.cos(k * 2.1) * r * 0.45, y + Math.sin(k * 2.1) * r * 0.45); cd += `M${N(c[0] + 3)},${N(c[1])}a3,3 0 1,0 -3,3`; }
        S.line('back', cd, H.d, 1.2, ' opacity="0.8"');
      }
      HAIR.curly(S, G, sp, T, hatInfo, H);
    },
    // 石灰竖起的尖发（凯尔特）
    spiky(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P, e = G.ell;
      hairCap(S, G, sp, T, H, { vol: 0.06, target: P(0.4, -1.8) });
      if (hatInfo.coversTop) return;
      const rx = G.silRx(1.05), ry = G.silRy(1.05), items = [];
      const tipC = mix(H.b, '#f0e8d0', 0.55);
      for (let i = 0; i < 10; i++) {
        const a = 0.0 + i * 0.32;
        const root = [e.Cx + rx * 0.8 * Math.cos(a), e.Cy - ry * 0.8 * Math.sin(a)];
        const tip = [e.Cx + rx * 1.42 * Math.cos(a - 0.15), e.Cy - ry * 1.45 * Math.sin(a - 0.15)];
        items.push({ d: leaf(root, tip, 0.34 * R, 0.06), fill: i % 2 ? H.b : H.s });
        items.push({ d: leaf(lp(root, tip, 0.6), tip, 0.16 * R, 0.04), fill: tipC });
      }
      clumps(S, 'hair', items, S.lw * 0.55);
    },
    // 苏维汇发髻：头发梳向近侧太阳穴上方打结
    suebian(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      const k = G.h3(1.3, 0.62, 1.08);
      hairCap(S, G, sp, T, H, { target: k, vol: 0.07 });
      const kd = bun(S, G, H, k[0], k[1] - 0.05 * R, 0.3 * R);
      void kd;
      clumps(S, 'hair', [{ d: leaf(add(k, 0.1 * R, 0.1 * R), add(k, 0.5 * R, 0.95 * R), 0.36 * R, 0.1), fill: H.s }, { d: leaf(add(k, 0, 0.1 * R), add(k, 0.25 * R, 1.1 * R), 0.3 * R, -0.08), fill: H.b }], S.lw * 0.55);
      S.add('hair', `<path d="${taper([add(k, -0.28 * R, 0.12 * R), add(k, 0, 0.22 * R), add(k, 0.28 * R, 0.1 * R)], 0.12 * R)}" fill="${sp.hair.tie || '#7a5a3a'}" stroke="${INK}" stroke-width="1"/>`);
    },
    // 罗马女子：中分波浪，脑后挽髻
    romanf(S, G, sp, T, hatInfo, H) {
      const R = G.R, P = G.P;
      hairCap(S, G, sp, T, H, { hairline: partLine(G), target: P(1.0, -0.2), vol: 0.08, extra: () => {
        let wd = '';
        for (let i = 0; i < 4; i++) wd += spline([P(-0.4, -0.95 + i * 0.12), P(0.0, -1.0 + i * 0.16), P(0.4, -0.85 + i * 0.16), P(0.85, -0.6 + i * 0.16)]);
        S.line('hair', wd, H.d, 1.1, ' opacity="0.6"');
      } });
      bun(S, G, H, G.hx + 1.12 * R, G.ey - 0.25 * R, 0.36 * R);
    },
  });
  function flower(x, y, r, col) {
    let s = '';
    for (let i = 0; i < 5; i++) { const a = i * 1.2566; s += circle(x + Math.cos(a) * r * 0.62, y + Math.sin(a) * r * 0.62, r * 0.48) + `/>`; }
    return `<g fill="${col}" stroke="${INK}" stroke-width="0.9">${s}</g>` + circle(x, y, r * 0.34) + ' fill="#e8b030" stroke="#7a4a10" stroke-width="0.6"/>';
  }

  // ======================================================== 各文化冠帽 ==
  // 一般的“冠顶”形状：band 以上，可调高度与平顶
  function capShape(G, bm, ba, s, lift, flat) {
    const bnd = band(G, bm, ba, s);
    let cap = capOutline(G, bnd, s, lift || 1.05);
    if (flat) {
      const yTop = Math.min(...cap.map(p => p[1]));
      cap = cap.map((p, i) => (i >= bnd.length ? [p[0], lerp(p[1], yTop, flat), p[2]] : p));
    }
    return { bnd, cap };
  }
  // 尖顶冠（圆锥）：底边 band，顶点 apex
  function coneShape(G, bm, ba, s, apex, bulge) {
    const bnd = band(G, bm, ba, s);
    const a = bnd[0], b = bnd[bnd.length - 1];
    const mA = lp(a, apex, 0.5), mB = lp(b, apex, 0.5);
    const bl = bulge || 0.12;
    return { bnd, cap: bnd.concat([[mB[0] + bl * G.R, mB[1] - bl * 0.3 * G.R], [apex[0], apex[1], 0.2], [mA[0] - bl * G.R, mA[1] - bl * 0.3 * G.R]]) };
  }
  // 冠顶的通用着色：背光侧 + 迎光高光
  function capShade(S, G, C, lay) {
    lay = lay || 'hat';
    S.path(lay, spline([G.h3(0.9, -0.1, 1.6), G.h3(0.55, 1.4, 1.7), [G.hx + 2.5 * G.R, G.ey - 3 * G.R], G.h3(2.3, -0.3, 1.6)], true), C.s);
    if (S.o.lod >= 1) S.path(lay, spline([G.h3(-0.7, 0.6, 1.12), G.h3(-0.3, 0.95, 1.12), G.h3(0.1, 1.15, 1.12), G.h3(-0.15, 0.85, 1.12)], true), C.h, 0, ' opacity="0.5"');
  }
  // 飘带（两条）
  function ribbons(S, G, from, col, len) {
    const P = G.P, R = G.R;
    len = len || 1;
    feather(S, 'back', [from, add(from, 0.35 * R, 0.5 * R * len), add(from, 0.2 * R, 1.2 * R * len), add(from, 0.45 * R, 1.9 * R * len)], 0.16 * R, col, 'none');
    feather(S, 'back', [from, add(from, 0.6 * R, 0.35 * R * len), add(from, 0.75 * R, 1.0 * R * len), add(from, 1.05 * R, 1.6 * R * len)], 0.14 * R, col, 'none');
    void P;
  }
  // 毛边：沿折线排一圈短毛绺
  function furEdge(S, layer, G, pts, col, len, n) {
    const C = clothT(col), R = G.R, items = [];
    const sm = resample(pts, n || 14), nm = normals(sm);
    for (let i = 0; i < sm.length; i++) {
      const tip = add(sm[i], nm[i][0] * len * R * (i % 2 ? 0.7 : 1), nm[i][1] * len * R * (i % 2 ? 0.7 : 1));
      items.push({ d: leaf(add(sm[i], -nm[i][0] * len * R * 0.6, -nm[i][1] * len * R * 0.6), tip, 0.26 * R, i % 2 ? 0.15 : -0.15), fill: i % 2 ? C.s : C.b });
    }
    clumps(S, layer, items, S.lw * 0.5);
  }
  // 头盔通用：盔钵 + 金边
  function bowl(S, G, sp, T, opt) {
    const R = G.R, lo = S.o.lod;
    const Mt = metalT(opt.metal);
    const s = opt.s || 1.14;
    const sh = opt.cone ? coneShape(G, opt.bm || 0.08, 0.34, s, opt.apex, 0.25) : capShape(G, opt.bm || 0.08, 0.34, s, opt.lift || 1.1);
    S.shape('hat', spline(sh.cap, true), Mt.b, () => {
      S.path('hat', spline([G.h3(0.85, 0.0, 1.5), G.h3(0.55, 1.3, 1.5), [G.hx + 2.2 * R, G.ey - 2.8 * R], G.h3(2.2, -0.2, 1.5)], true), Mt.s);
      S.path('hat', spline([G.h3(-0.75, 0.5, s), G.h3(-0.45, 1.0, s), G.h3(-0.05, 1.22, s), G.h3(-0.2, 0.95, s), G.h3(-0.55, 0.62, s)], true), Mt.h, 0, ' opacity="0.75"');
      if (opt.ribs) {
        let md = '';
        for (const lam of opt.ribs) {
          const pts = [];
          for (let b = (opt.bm || 0.08) + 0.34 * Math.cos(lam) + 0.04; b < 1.5; b += 0.2) pts.push(G.h3(lam, b, s * 1.005));
          if (opt.apex) pts.push(opt.apex);
          md += spline(pts);
        }
        S.add('hat', `<path d="${md}" fill="none" stroke="${INK}" stroke-width="${N(opt.ribW || 3.2)}"/><path d="${md}" fill="none" stroke="${opt.ribC || Mt.h}" stroke-width="${N((opt.ribW || 3.2) * 0.55)}"/>`);
      }
      if (opt.extra) opt.extra(Mt);
    }, S.lw);
    if (opt.trim !== false) {
      S.line('hat', spline(sh.bnd.slice(1, -1)), INK, 4.2);
      S.line('hat', spline(sh.bnd.slice(1, -1)), opt.trim || GOLD, 2.4);
    }
    bandShadow(S, G, sh.bnd, T, 0.16);
    void lo;
    return sh;
  }
  Object.assign(HATS, {
    // 南蛮羽冠：编带 + 一排彩羽
    feathers(S, G, sp, T) {
      const R = G.R;
      const st = bandStrip(G, 0.1, 0.28, 0.36, 1.06);
      const cols = sp.hat.colors || ['#c03028', '#2a6aa0', '#ece4d4', '#d8a020', '#2a8a4a'];
      const roots = st.hi.slice(3, 12);
      roots.forEach((r, i) => {
        const k = i / (roots.length - 1);
        const tip = add(r, (k - 0.55) * 0.9 * R, -(1.1 + Math.sin(k * Math.PI) * 0.5) * R);
        feather(S, i % 2 ? 'back' : 'hat', [r, lp(r, tip, 0.5), tip], 0.17 * R, cols[i % cols.length]);
      });
      S.shape('hat', spline(st.pts, true), sp.hat.color || '#a03020', () => {
        if (S.o.lod >= 1) {
          let zd = '';
          const lo2 = st.lo.slice(1, -1), hi2 = st.hi.slice(1, -1);
          for (let i = 0; i < lo2.length - 1; i++) zd += `M${ps(lp(lo2[i], hi2[i], 0.2))}L${ps(lp(lo2[i + 1], hi2[i + 1], 0.8))}`;
          S.line('hat', zd, GOLD, 1.4);
        }
      }, S.lw * 0.8);
      bandShadow(S, G, st.lo, T, 0.1);
      return { coversTop: false };
    },
    // 孟获：嵌宝紫金冠 + 背后羽扇
    chief(S, G, sp, T) {
      const R = G.R, bx = G.hx + 0.22 * R, by = G.ey - 1.62 * R;
      const cols = ['#c03028', '#e8e0d0', '#2a6aa0', '#d8a020', '#2a8a4a'];
      for (let i = 0; i < 7; i++) {
        const a = Math.PI * (0.15 + i * 0.12);
        feather(S, 'back', [[bx, by], [bx + Math.cos(a) * 0.8 * R, by - Math.sin(a) * 0.9 * R], [bx + Math.cos(a) * 1.7 * R, by - Math.sin(a) * 1.8 * R]], 0.2 * R, cols[i % cols.length]);
      }
      return HATS.crown(S, G, Object.assign({}, sp, { hat: Object.assign({ color: '#d0a040', gem: '#2a9aa0', pearls: true }, sp.hat) }), T);
    },
    // 衝角付冑（倭）：盔前船首形突脊，后垂錣
    wahelm(S, G, sp, T) {
      const R = G.R, P = G.P;
      const metal = sp.hat.color || '#4a4c54';
      const Mt = metalT(metal);
      const ng = [G.h3(1.2, 0.05, 1.14), G.h3(G.lamNear - 0.02, -0.06, 1.14), P(1.55, 0.7), P(1.45, 1.4, 0.4), P(0.8, 1.35, 0.4), P(0.7, 0.4)];
      S.shape('back', spline(ng, true), Mt.s, () => {
        let rd = '';
        for (let i = 1; i < 4; i++) rd += spline([P(0.7, 0.1 + i * 0.36), P(1.15, 0.2 + i * 0.36), P(1.6, 0.18 + i * 0.36)]);
        S.line('back', rd, INK, 1.6);
      }, S.lw);
      bowl(S, G, sp, T, { metal, lift: 1.06, trim: '#c8a050', extra: (M2) => {
        let bd = '';
        for (const b of [0.75, 1.1]) { const pts = band(G, b - 0.34 * 0.5, 0.2, 1.145); bd += spline(pts); }
        S.line('hat', bd, INK, 1.6);
        if (S.o.lod >= 2) for (const b of [0.75, 1.1]) for (const p of band(G, b - 0.17, 0.2, 1.15, 10)) S.add('hat', circle(p[0], p[1], 0.9) + ` fill="${M2.h}"/>`);
      } });
      const b0 = G.h3(-0.2, 0.52, 1.16), up = G.h3(0.5, 1.45, 1.16);
      const keel = poly([b0, add(b0, -0.5 * R, -0.2 * R), add(lp(b0, up, 0.5), -0.35 * R, -0.35 * R), up]);
      S.add('hat', `<path d="${keel}" fill="${Mt.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.85)}"/>`);
      S.add('hat', `<path d="${poly([b0, add(b0, -0.5 * R, -0.2 * R), add(lp(b0, up, 0.5), -0.35 * R, -0.35 * R)], false)}" fill="none" stroke="${Mt.h}" stroke-width="1.2" opacity="0.8"/>`);
      return { coversTop: true };
    },
    // 巫女头饰（卑弥呼）：白布带 + 额前铜镜 + 两侧勾玉垂饰
    shaman(S, G, sp, T) {
      const R = G.R;
      const st = bandStrip(G, 0.12, 0.28, 0.36, 1.06);
      S.shape('hat', spline(st.pts, true), '#ece6d8', () => {
        let td = '';
        const lo2 = st.lo.slice(1, -1), hi2 = st.hi.slice(1, -1);
        for (let i = 0; i < lo2.length - 1; i += 2) td += poly([lo2[i], lp(hi2[i], hi2[i + 1], 0.5), lo2[i + 1]]);
        S.path('hat', td, '#b0302a');
      }, S.lw * 0.8);
      const c = G.h3(0, 0.62, 1.08);
      S.add('hat', circle(c[0], c[1], 0.3 * R) + ` fill="#b08a3a" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>` + circle(c[0], c[1], 0.22 * R) + ' fill="none" stroke="#7a5a20" stroke-width="1.4"/>' + circle(c[0] - 0.08 * R, c[1] - 0.08 * R, 0.08 * R) + ' fill="#f0e0a0" opacity="0.8"/>');
      for (const lam of [-0.95, 1.05]) {
        const a = G.h3(lam, 0.32, 1.07);
        S.line('hat', `M${ps(a)}l0,${N(0.7 * R)}`, '#e8e0c8', 1.2);
        for (let i = 0; i < 3; i++) S.add('hat', magatama(a[0], a[1] + (0.2 + i * 0.22) * R, 0.09 * R, i % 2 ? '#3a9a6a' : '#e8e0c8'));
      }
      bandShadow(S, G, st.lo, T, 0.1);
      return { coversTop: false };
    },
    // 夷洲：贝珠编带 + 羽毛
    yiband(S, G, sp, T) {
      const R = G.R;
      const st = bandStrip(G, 0.08, 0.26, 0.36, 1.06);
      const k = G.h3(1.4, 0.45, 1.08);
      feather(S, 'back', [k, add(k, 0.25 * R, -0.8 * R), add(k, 0.7 * R, -1.6 * R)], 0.2 * R, '#ece8e0', '#1a1a1a');
      feather(S, 'back', [k, add(k, 0.5 * R, -0.6 * R), add(k, 1.2 * R, -1.1 * R)], 0.18 * R, '#2a2a2a', '#ece8e0');
      S.shape('hat', spline(st.pts, true), sp.hat.color || '#b02a24', () => {
        let bd = '';
        const mid = st.lo.map((p, i) => lp(p, st.hi[i], 0.5)).slice(1, -1);
        for (const p of mid) bd += circle(p[0], p[1], 0.06 * R) + '/>';
        S.add('hat', `<g fill="#f4f0e4">${bd}</g>`);
      }, S.lw * 0.8);
      bandShadow(S, G, st.lo, T, 0.1);
      return { coversTop: false };
    },
    // 折风（弁）：小尖帽，颔下系带；birds = 鸟羽冠（两侧插羽）
    jeolpung(S, G, sp, T, birds) {
      const R = G.R, P = G.P;
      const col = sp.hat.color || (birds ? '#3a2a4a' : '#c8b07a');
      const C = clothT(col);
      const apex = [G.hx + 0.05 * R, G.ey - 2.4 * R];
      const sh = coneShape(G, 0.42, 0.3, 1.06, apex, 0.18);
      if (birds) {
        const fc = sp.hat.color2 || '#ece6d8';
        feather(S, 'hat', [G.h3(1.0, 0.85, 1.08), [apex[0] + 0.5 * R, apex[1] + 0.2 * R], [apex[0] + 0.8 * R, apex[1] - 0.7 * R]], 0.2 * R, fc);
        feather(S, 'back', [G.h3(-0.7, 0.85, 1.08), [apex[0] - 0.55 * R, apex[1] + 0.2 * R], [apex[0] - 0.7 * R, apex[1] - 0.6 * R]], 0.16 * R, fc);
      }
      S.shape('hat', spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
      S.line('hat', spline(sh.bnd.slice(1, -1)), INK, 3.6);
      S.line('hat', spline(sh.bnd.slice(1, -1)), C.d, 1.8);
      // 颔下系带
      const a = sh.bnd[2], b = sh.bnd[sh.bnd.length - 3];
      S.line('hat', spline([b, P(0.62, 0.3), P(0.3, 1.3), P(-0.1, 1.48)]), INK, 2.2);
      S.line('hat', spline([b, P(0.62, 0.3), P(0.3, 1.3), P(-0.1, 1.48)]), mix(col, '#000', 0.3), 1.0);
      void a;
      bandShadow(S, G, sh.bnd, T, 0.08);
      return { coversTop: false };
    },
    birdfeather(S, G, sp, T) { return HATS.jeolpung(S, G, sp, T, true); },
    // 新罗金冠：金带 + 三根“出”字形立饰 + 勾玉与金片
    goldcrown(S, G, sp, T) {
      const R = G.R;
      const gc = sp.hat.color || '#e0b440';
      const Mt = metalT(gc);
      const st = bandStrip(G, 0.14, 0.3, 0.36, 1.06);
      const posts = [3, 8, 13].map(i => st.hi[i]);
      for (const p of posts) {
        const h = 1.15 * R;
        let d = `M${ps(p)}l0,${N(-h)}`;
        for (let k = 1; k <= 3; k++) { const y = p[1] - h * k / 3.4; d += `M${N(p[0])},${N(y)}l${N(-0.22 * R)},0l0,${N(-0.18 * R)}M${N(p[0])},${N(y)}l${N(0.22 * R)},0l0,${N(-0.18 * R)}`; }
        S.add('hat', `<path d="${d}" fill="none" stroke="${INK}" stroke-width="${N(0.11 * R + 2.4)}" stroke-linecap="square"/><path d="${d}" fill="none" stroke="${Mt.b}" stroke-width="${N(0.11 * R)}" stroke-linecap="square"/>`);
        if (S.o.lod >= 1) for (let k = 0; k < 3; k++) S.add('hat', magatama(p[0] + (k - 1) * 0.22 * R, p[1] - h * (0.25 + k * 0.25), 0.07 * R, '#3aa06a'));
      }
      S.shape('hat', spline(st.pts, true), Mt.b, () => {
        S.path('hat', spline(st.lo.slice(8).concat(st.hi.slice(8).reverse()), true), Mt.s);
        if (S.o.lod >= 1) { let dd = ''; for (const p of st.lo.slice(1, -1)) dd += circle(p[0], p[1] - 0.06 * R, 0.035 * R) + '/>'; S.add('hat', `<g fill="${Mt.h}">${dd}</g>`); }
      }, S.lw * 0.8);
      // 垂饰
      for (const lam of [-1.0, 1.3]) {
        const a = G.h3(lam, 0.25, 1.06);
        S.line('hat', `M${ps(a)}l${N(lam > 0 ? -0.05 * R : 0.02 * R)},${N(0.9 * R)}`, Mt.b, 1.6);
        S.add('hat', `<path d="${leaf(add(a, 0, 0.85 * R), add(a, 0, 1.2 * R), 0.16 * R, 0)}" fill="${Mt.b}" stroke="${INK}" stroke-width="1"/>`);
      }
      bandShadow(S, G, st.lo, T, 0.1);
      return { coversTop: false };
    },
    // 毡帽：圆顶，翻起的毛皮帽檐，护耳盖住耳朵
    felt(S, G, sp, T) {
      const R = G.R, P = G.P;
      const col = sp.hat.color || '#e0d4b8';
      const C = clothT(col);
      const furC = sp.hat.color2 || '#6a4a2a';
      G.hideEar = true;
      const sh = capShape(G, 0.18, 0.32, 1.12, 1.16);
      // 护耳
      const flapN = [G.h3(0.95, 0.15, 1.12), G.h3(1.75, 0.05, 1.12), P(1.05, 0.75), P(0.72, 1.0, 0.5), P(0.46, 0.75), P(0.44, 0.1)];
      S.shape('hat', spline(flapN, true), clothT(furC).b, () => { S.path('hat', spline([P(0.9, -0.3), P(1.4, 0), P(1.2, 1.2), P(0.8, 1.2)], true), clothT(furC).s); }, S.lw);
      S.shape('back', spline([G.h3(G.lamFar + 0.05, 0.2, 1.12), P(-1.18, 0.3), P(-1.1, 0.95), P(-0.92, 0.9)], true), clothT(furC).s, null, S.lw);
      S.shape('hat', spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
      furEdge(S, 'hat', G, band(G, 0.1, 0.32, 1.16, 12).slice(1, -1), furC, 0.14, 15);
      bandShadow(S, G, sh.bnd, T, 0.16);
      return { coversTop: true };
    },
    // 高毛皮帽（草原 / 日耳曼毛帽 lowCap）
    fur(S, G, sp, T, lowCap) {
      const R = G.R;
      const furC = sp.hat.color || (lowCap ? '#6a4a30' : '#4a3424');
      const C = clothT(furC);
      const sh = capShape(G, 0.08, 0.34, 1.16, lowCap ? 1.12 : 1.45, lowCap ? 0 : 0.45);
      if (!lowCap) {
        const t = G.h3(0.6, 1.4, 1.3);
        S.add('hat', ellipse(t[0], t[1] - 0.1 * R, 0.5 * R, 0.3 * R) + ` fill="${sp.hat.color2 || '#a02a24'}" stroke="${INK}" stroke-width="${N(S.lw)}"/>`);
      }
      S.shape('hat', spline(sh.cap, true), C.b, () => {
        capShade(S, G, C);
        if (S.o.lod >= 1) {
          let fd = '';
          const rr = sub(sp.name, 'fur');
          for (let i = 0; i < 26; i++) { const p = G.h3(rr.range(-1.1, 1.9), rr.range(0.3, 1.4), 1.18); fd += `M${N(p[0])},${N(p[1])}l${N(rr.range(-2, 2))},${N(rr.range(2.5, 4.5))}`; }
          S.line('hat', fd, C.d, 1.2, ' opacity="0.7"');
        }
      }, S.lw);
      furEdge(S, 'hat', G, sh.cap.slice(sh.bnd.length - 1).concat([sh.cap[0]]), furC, 0.08, 16);
      furEdge(S, 'hat', G, sh.bnd.slice(1, -1), furC, 0.1, 13);
      bandShadow(S, G, sh.bnd, T, 0.16);
      return { coversTop: true };
    },
    furcap(S, G, sp, T) { return HATS.fur(S, G, sp, T, true); },
    // 南海诸国的多层尖顶金冠
    tallcrown(S, G, sp, T) {
      const R = G.R;
      const Mt = metalT(sp.hat.color || '#e0b040');
      const st = bandStrip(G, 0.14, 0.3, 0.36, 1.08);
      const top = G.h3(0.3, 1.2, 1.08);
      // 层层收小的金冠
      const tiers = [];
      for (let i = 0; i < 4; i++) {
        const w = (0.95 - i * 0.2) * R, h = 0.36 * R, y = top[1] + 0.05 * R - i * h * 0.85;
        tiers.push(spline([[top[0] - w, y + h * 0.2, 0.2], [top[0] - w * 0.82, y - h * 0.7], [top[0], y - h * 0.9], [top[0] + w * 0.82, y - h * 0.7], [top[0] + w, y + h * 0.2, 0.2], [top[0], y + h * 0.45]], true));
      }
      const spire = [top[0], top[1] - 1.9 * R];
      S.add('hat', `<path d="${poly([[top[0] - 0.14 * R, top[1] - 1.1 * R], spire, [top[0] + 0.14 * R, top[1] - 1.1 * R]])}" fill="${Mt.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      for (let i = tiers.length - 1; i >= 0; i--) {
        S.shape('hat', tiers[i], i % 2 ? Mt.s : Mt.b, () => { S.path('hat', spline([[top[0] + 0.1 * R, top[1] - 3 * R], [top[0] + 1.5 * R, top[1] - 2 * R], [top[0] + 1.2 * R, top[1] + R], [top[0] + 0.25 * R, top[1] + R]], true), Mt.s, 0, ' opacity="0.6"'); }, S.lw * 0.8);
        if (S.o.lod >= 1) S.add('hat', circle(top[0] - 0.1 * R, top[1] - i * 0.3 * R - 0.05 * R, 0.07 * R) + ` fill="${i % 2 ? '#c02a3a' : '#2a9a6a'}" stroke="${INK}" stroke-width="0.8"/>`);
      }
      S.shape('hat', spline(st.pts, true), Mt.b, () => S.path('hat', spline(st.lo.slice(9).concat(st.hi.slice(9).reverse()), true), Mt.s), S.lw * 0.8);
      // 耳侧火焰纹饰片
      const e = G.h3(1.45, 0.3, 1.08);
      S.add('hat', `<path d="${spline([e, add(e, 0.35 * R, -0.25 * R), add(e, 0.25 * R, -0.75 * R, 0.2), add(e, 0.5 * R, -0.45 * R), add(e, 0.45 * R, 0.25 * R), add(e, 0.1 * R, 0.35 * R)], true)}" fill="${Mt.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      bandShadow(S, G, st.lo, T, 0.1);
      return { coversTop: true };
    },
    // 花布裹头
    flowerwrap(S, G, sp, T) {
      HATS.turban(S, G, Object.assign({}, sp, { hat: { color: sp.hat.color || '#c83a4a', noTails: true } }), T);
      const R = G.R;
      const f = G.h3(1.1, 0.55, 1.16);
      S.add('hat', flower(f[0], f[1], 0.2 * R, '#f4ecd8') + flower(f[0] + 0.26 * R, f[1] + 0.22 * R, 0.16 * R, '#f0c040'));
      return { coversTop: true };
    },
    // 尖顶毡帽（塞种 / 西域），帽檐上翻
    pointcap(S, G, sp, T, tall) {
      const R = G.R;
      const col = sp.hat.color || '#e8dcc0';
      const C = clothT(col);
      const apex = tall ? [G.hx + 0.15 * R, G.ey - 3.0 * R] : [G.hx - 0.1 * R, G.ey - 2.55 * R];
      const sh = coneShape(G, 0.14, 0.34, 1.12, apex, 0.3);
      S.shape('hat', spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
      const st = bandStrip(G, 0.08, 0.24, 0.34, 1.15);
      S.shape('hat', spline(st.pts, true), sp.hat.color2 || mix(col, '#7a2a20', 0.6), () => {
        if (S.o.lod >= 1) { let dd = ''; for (const p of st.lo.slice(1, -1)) dd += circle(p[0], p[1] - 0.07 * G.R, 0.04 * G.R) + '/>'; S.add('hat', `<g fill="${GOLD}">${dd}</g>`); }
      }, S.lw * 0.85);
      bandShadow(S, G, st.lo, T, 0.12);
      return { coversTop: true };
    },
    // 贵霜高帽：高圆锥帽 + 王带飘带
    tallhat(S, G, sp, T) {
      const R = G.R;
      HATS.pointcap(S, G, Object.assign({}, sp, { hat: Object.assign({ color2: GOLD }, sp.hat) }), T, true);
      ribbons(S, G, G.h3(1.85, 0.25, 1.14), sp.hat.ribbon || '#e8e0d0', 1.1);
      void R;
      return { coversTop: true };
    },
    // 王带（希腊化 / 安息）：束在发上的带子，脑后两条飘带
    diadem(S, G, sp, T) {
      const R = G.R;
      const col = sp.hat.color || '#ece6d8';
      const st = bandStrip(G, 0.18, 0.3, 0.32, 1.1);
      ribbons(S, G, G.h3(1.8, 0.35, 1.1), col, 1.0);
      S.shape('hat', spline(st.pts, true), col, () => S.path('hat', spline(st.lo.slice(9).concat(st.hi.slice(9).reverse()), true), mul(col, 0.75)), S.lw * 0.75);
      if (sp.hat.gem) { const g = G.h3(0, 0.65, 1.12); S.add('hat', circle(g[0], g[1], 0.09 * R) + ` fill="${sp.hat.gem}" stroke="${INK}" stroke-width="1"/>`); }
      return { coversTop: false };
    },
    // 安息提亚拉：高圆冠，护耳护颈，缀珠与星月
    tiara(S, G, sp, T) {
      const R = G.R, P = G.P;
      const col = sp.hat.color || '#3a2a4a';
      const C = clothT(col);
      G.hideEar = true;
      const flap = [G.h3(0.95, 0.1, 1.14), G.h3(G.lamNear - 0.02, 0.0, 1.14), P(1.35, 1.0), P(1.2, 1.55, 0.4), P(0.62, 1.2, 0.4), P(0.44, 0.2)];
      S.shape('hat', spline(flap, true), C.b, () => { S.path('hat', spline([P(0.9, -0.3), P(1.6, 0), P(1.5, 1.6), P(0.85, 1.6)], true), C.s); }, S.lw);
      S.shape('back', spline([G.h3(G.lamFar + 0.05, 0.15, 1.14), P(-1.22, 0.4), P(-1.18, 1.2), P(-0.95, 1.1)], true), C.s, null, S.lw);
      const sh = capShape(G, 0.08, 0.34, 1.15, 1.5, 0.35);
      S.shape('hat', spline(sh.cap, true), C.b, () => {
        capShade(S, G, C);
        if (S.o.lod >= 1) {
          let pd = '';
          for (let i = 0; i < 7; i++) { const p = G.h3(0.05, 0.6 + i * 0.13, 1.17); pd += circle(p[0], p[1], 0.05 * R) + '/>'; }
          S.add('hat', `<g fill="#f4f0e4" stroke="${INK}" stroke-width="0.6">${pd}</g>`);
          const s0 = G.h3(0.85, 0.9, 1.17);
          S.add('hat', `<path d="M${N(s0[0])},${N(s0[1] - 0.18 * R)}l${N(0.05 * R)},${N(0.13 * R)}l${N(0.13 * R)},${N(0.05 * R)}l${N(-0.13 * R)},${N(0.05 * R)}l${N(-0.05 * R)},${N(0.13 * R)}l${N(-0.05 * R)},${N(-0.13 * R)}l${N(-0.13 * R)},${N(-0.05 * R)}l${N(0.13 * R)},${N(-0.05 * R)}z" fill="${GOLD}" stroke="${INK}" stroke-width="0.8"/>`);
        }
      }, S.lw);
      const st = bandStrip(G, 0.06, 0.2, 0.34, 1.17);
      S.shape('hat', spline(st.pts, true), GOLD, () => {
        let pd = '';
        for (const p of st.lo.map((q, i) => lp(q, st.hi[i], 0.5)).slice(1, -1)) pd += circle(p[0], p[1], 0.05 * R) + '/>';
        if (S.o.lod >= 1) S.add('hat', `<g fill="#f4f0e4">${pd}</g>`);
      }, S.lw * 0.8);
      ribbons(S, G, G.h3(1.85, 0.2, 1.15), sp.hat.ribbon || '#e8e0d0', 1.2);
      bandShadow(S, G, st.lo, T, 0.12);
      return { coversTop: true };
    },
    // 弗里吉亚软帽：帽尖前倾
    phrygian(S, G, sp, T) {
      const R = G.R;
      const col = sp.hat.color || '#9a2a24';
      const C = clothT(col);
      const sh = capShape(G, 0.1, 0.34, 1.13, 1.2);
      const top = G.h3(0.2, 1.4, 1.25);
      const tipPts = [G.h3(-0.6, 1.0, 1.13), [top[0] - 0.85 * R, top[1] - 0.35 * R], [top[0] - 1.05 * R, top[1] + 0.05 * R, 0.3], [top[0] - 0.55 * R, top[1] + 0.05 * R], G.h3(0.6, 1.25, 1.13)];
      S.shape('hat', spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
      S.shape('hat', spline(tipPts, true), C.b, () => S.path('hat', spline([[top[0] - 0.6 * R, top[1] - 0.6 * R], [top[0] + 0.4 * R, top[1]], [top[0] - 0.4 * R, top[1] + 0.3 * R]], true), C.s), S.lw);
      bandShadow(S, G, sh.bnd, T, 0.14);
      return { coversTop: true };
    },
    // 阿拉伯头巾 + 头箍：两侧垂到肩，遮住耳朵
    keffiyeh(S, G, sp, T) {
      const R = G.R, P = G.P;
      const col = sp.hat.color || '#ece6da';
      const C = clothT(col);
      G.hideEar = true;
      const sh = capShape(G, 0.1, 0.34, 1.14, 1.08);
      const drapeN = [sh.bnd[sh.bnd.length - 7], G.h3(G.lamNear - 0.02, 0.0, 1.14), P(1.4, 0.8), P(1.75, 2.1), P(1.2, 2.45, 0.5), P(0.7, 1.7), P(0.52, 0.8), P(0.46, 0.0)];
      S.shape('back', spline([G.h3(G.lamFar + 0.05, 0.3, 1.14), P(-1.25, 0.6), P(-1.6, 2.2), P(-1.0, 2.4), P(-0.95, 1.0)], true), C.s, null, S.lw);
      S.shape('hat', spline(sh.cap, true), C.b, () => capShade(S, G, C), S.lw);
      S.shape('hat', spline(drapeN, true), C.b, () => {
        S.path('hat', spline([P(1.0, -0.5), P(1.9, 0.5), P(1.9, 2.6), P(1.1, 2.6), P(1.0, 1.0)], true), C.s);
        let fd = '';
        for (let i = 0; i < 3; i++) fd += spline([P(0.62 + i * 0.22, 0.3 + i * 0.1), P(0.75 + i * 0.25, 1.2), P(0.95 + i * 0.25, 2.1)]);
        S.line('hat', fd, C.d, 1.3, ' opacity="0.6"');
        if (sp.hat.check) { let cd = ''; for (let i = 0; i < 8; i++) cd += spline([P(0.4 + i * 0.16, 0), P(0.6 + i * 0.16, 2.4)]); S.line('hat', cd, sp.hat.check, 1.3, ' opacity="0.5"'); }
      }, S.lw);
      // 头箍（双圈黑绳）
      for (const off of [0.0, 0.14]) {
        const bd = spline(band(G, 0.32 + off, 0.3, 1.16, 14).slice(1, -1));
        S.line('hat', bd, INK, 4.6);
        S.line('hat', bd, sp.hat.color2 || '#2a2226', 2.8);
      }
      bandShadow(S, G, sh.bnd, T, 0.16);
      return { coversTop: true };
    },
    // 缠头巾（阿拉伯 / 南海）
    arabturban(S, G, sp, T) {
      return HATS.turban(S, G, Object.assign({}, sp, { hat: { color: sp.hat.color || '#ece6da', noTails: true, big: true } }), T);
    },
    // 哈特拉王的高冠（鹰徽）
    hatra(S, G, sp, T) {
      const R = G.R;
      const col = sp.hat.color || '#c8a050';
      const Mt = metalT(col);
      const sh = capShape(G, 0.08, 0.34, 1.14, 1.7, 0.55);
      S.shape('hat', spline(sh.cap, true), Mt.b, () => {
        capShade(S, G, Mt);
        if (S.o.lod >= 1) { let pd = ''; for (let r = 0; r < 3; r++) for (const p of band(G, 0.4 + r * 0.32, 0.2, 1.16, 10).slice(1, -1)) pd += circle(p[0], p[1], 0.04 * R) + '/>'; S.add('hat', `<g fill="#f4f0e4">${pd}</g>`); }
      }, S.lw);
      const c = G.h3(0.05, 0.95, 1.18);
      const wing = s2 => spline([c, add(c, s2 * 0.35 * R, -0.3 * R), add(c, s2 * 0.55 * R, -0.05 * R), add(c, s2 * 0.3 * R, 0.1 * R)], true);
      S.add('hat', `<path d="${wing(-1)}${wing(1)}" fill="${mul(col, 0.6)}" stroke="${INK}" stroke-width="1"/>` + circle(c[0], c[1] - 0.1 * R, 0.08 * R) + ` fill="${mul(col, 0.6)}" stroke="${INK}" stroke-width="1"/>`);
      ribbons(S, G, G.h3(1.85, 0.2, 1.14), '#e8e0d0', 1.0);
      bandShadow(S, G, sh.bnd, T, 0.14);
      return { coversTop: true };
    },
    // 桂冠：两枝月桂叶环头，脑后系带
    laurel(S, G, sp, T) {
      const R = G.R;
      const col = sp.hat.color || '#5a8a3a';
      const items = [];
      const pts = band(G, 0.26, 0.3, 1.1, 14).slice(1, -1);
      pts.forEach((p, i) => {
        const nx = pts[Math.min(pts.length - 1, i + 1)], dx = nx[0] - p[0], dy = nx[1] - p[1];
        const l = hyp(dx, dy) || 1;
        items.push({ d: leaf(p, add(p, dx / l * 0.3 * R - dy / l * 0.18 * R, dy / l * 0.3 * R - 0.2 * R), 0.13 * R, 0.1), fill: col });
        items.push({ d: leaf(p, add(p, dx / l * 0.3 * R + 0.02 * R, dy / l * 0.3 * R + 0.14 * R), 0.13 * R, -0.1), fill: mul(col, 0.75) });
      });
      clumps(S, 'hat', items, S.lw * 0.45);
      ribbons(S, G, G.h3(1.8, 0.2, 1.1), sp.hat.ribbon || '#c02a2a', 0.9);
      return { coversTop: false };
    },
    // 罗马盔：护颊、护颈、马鬃羽冠
    galea(S, G, sp, T) {
      const R = G.R, P = G.P;
      const metal = sp.hat.color || '#b08a50';
      const Mt = metalT(metal);
      G.hideEar = true;
      S.shape('back', spline([G.h3(1.3, 0.05, 1.14), G.h3(G.lamNear, -0.05, 1.14), P(1.75, 0.35), P(1.62, 0.55, 0.4), P(1.1, 0.5)], true), Mt.s, null, S.lw);
      bowl(S, G, sp, T, { metal, lift: 1.08, trim: mix(metal, '#fff', 0.2), extra: () => {
        S.line('hat', spline(band(G, 0.3, 0.3, 1.15, 12).slice(1, -1)), INK, 3);
        S.line('hat', spline(band(G, 0.3, 0.3, 1.15, 12).slice(1, -1)), mix(metal, '#fff', 0.3), 1.4);
      } });
      // 护颊
      const ch = [G.h3(0.9, 0.18, 1.12), G.h3(1.45, 0.1, 1.12), P(0.92, 0.5), P(0.74, 1.0, 0.5), P(0.36, 0.9), P(0.36, 0.25)];
      S.shape('hat', spline(ch, true), Mt.b, () => { S.path('hat', spline([P(0.7, 0), P(1.2, 0), P(1.0, 1.2), P(0.6, 1.2)], true), Mt.s); }, S.lw * 0.9);
      if (S.o.lod >= 1) S.add('hat', circle(G.P(0.6, 0.5)[0], G.P(0.6, 0.5)[1], 0.05 * R) + ` fill="${Mt.h}" stroke="${INK}" stroke-width="0.8"/>`);
      // 羽冠
      const cr = [];
      const crest = sp.hat.plume || '#b02020';
      for (let i = 0; i < 9; i++) {
        const lam = -0.5 + i * 0.27;
        const b = G.h3(lam, 1.25, 1.16);
        cr.push({ d: leaf(b, add(b, 0.12 * R, -0.75 * R + Math.abs(i - 4) * 0.06 * R), 0.32 * R, 0.1), fill: i % 2 ? mul(crest, 0.75) : crest });
      }
      clumps(S, 'hat', cr, S.lw * 0.55);
      return { coversTop: true };
    },
    // 凯尔特铜盔：圆顶 + 顶钮 + 一对小角
    celthelm(S, G, sp, T) {
      const R = G.R;
      const metal = sp.hat.color || '#b08a50';
      const h0 = G.h3(-0.6, 1.0, 1.15), h1 = G.h3(1.2, 1.0, 1.15);
      feather(S, 'back', [h1, add(h1, 0.5 * R, -0.3 * R), add(h1, 0.65 * R, -0.85 * R)], 0.2 * R, '#e8e0c8', 'none');
      bowl(S, G, sp, T, { metal, lift: 1.12, trim: mix(metal, '#fff', 0.2) });
      feather(S, 'hat', [h0, add(h0, -0.4 * R, -0.35 * R), add(h0, -0.5 * R, -0.85 * R)], 0.2 * R, '#e8e0c8', 'none');
      const t = G.h3(0.6, 1.5, 1.15);
      S.add('hat', circle(t[0], t[1], 0.12 * R) + ` fill="${metal}" stroke="${INK}" stroke-width="1.4"/>`);
      return { coversTop: true };
    },
    // 分片尖盔 + 护鼻
    spangen(S, G, sp, T) {
      const R = G.R, F = G.F;
      const metal = sp.hat.color || '#7a808c';
      const apex = [G.hx - 0.05 * R, G.ey - 2.25 * R];
      bowl(S, G, sp, T, { metal, cone: true, apex, ribs: [-0.6, 0.2, 1.0], ribC: '#c8a050', trim: '#c8a050' });
      // 护鼻
      const n0 = G.h3(0.0, 0.55, 1.16), n1 = F(0.0, 0.45, 0.25);
      S.add('hat', `<path d="${taper([n0, n1], t => 0.14 * R * (1 - t * 0.3))}" fill="${metal}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      return { coversTop: true };
    },
    // 萨尔马提亚尖盔 + 锁子护颈
    conical(S, G, sp, T) {
      const R = G.R, P = G.P;
      const metal = sp.hat.color || '#8a8f9a';
      const Mt = metalT(metal);
      G.hideEar = true;
      const mail = [G.h3(0.9, 0.08, 1.14), G.h3(G.lamNear - 0.02, -0.05, 1.14), P(1.4, 0.9), P(1.3, 1.65, 0.4), P(0.62, 1.55, 0.4), P(0.44, 0.9), P(0.44, 0.1)];
      S.shape('hat', spline(mail, true), Mt.s, () => {
        if (S.o.lod >= 1) {
          let md = '';
          for (let i = 0; i < 9; i++) md += `M${ps(P(0.4, -0.1 + i * 0.2))}L${ps(P(1.6, 0.1 + i * 0.2))}`;
          S.add('hat', `<path d="${md}" fill="none" stroke="${Mt.d}" stroke-width="2.2" stroke-dasharray="2 1.4" opacity="0.8"/>`);
        }
        S.path('hat', spline([P(0.95, -0.3), P(1.6, 0.0), P(1.5, 1.8), P(1.0, 1.8)], true), Mt.d, 0, ' opacity="0.45"');
      }, S.lw);
      S.shape('back', spline([G.h3(G.lamFar + 0.05, 0.15, 1.14), P(-1.2, 0.4), P(-1.15, 1.1), P(-0.95, 1.05)], true), Mt.d, null, S.lw);
      const apex = [G.hx + 0.1 * R, G.ey - 2.5 * R];
      bowl(S, G, sp, T, { metal, cone: true, apex, ribs: [-0.5, 0.35, 1.2], ribC: GOLD });
      tassel(S, 'hat', G, apex, sp.hat.plume || '#c0302a', 4, 0.6, 1);
      return { coversTop: true };
    },
  });
  // 头盔补充变体：round 圆钵宽檐 / spike 尖顶 / plume 高羽（高句丽）
  const helmetBase = HATS.helmet;
  HATS.helmet = function (S, G, sp, T) {
    const v = sp.hat.variant || 'plain';
    if (v === 'plain' || v === 'wing' || v === 'lion' || v === 'horn') return helmetBase(S, G, sp, T);
    const R = G.R, P = G.P;
    const metal = sp.hat.color || sp.outfit.metal || '#8a8f9a';
    const ngC = sp.hat.neck || mix(metal, '#3a2a22', 0.4);
    const ng = [G.h3(1.3, 0.05, 1.14), G.h3(G.lamNear - 0.02, -0.06, 1.14), P(1.48, 0.95), P(1.6, 1.62, 0.4), P(0.98, 1.7, 0.4), P(0.78, 0.8)];
    S.shape('back', spline(ng, true), ngC, () => {
      let rd = '';
      for (let i = 1; i < 5; i++) rd += spline([P(0.7, 0.05 + i * 0.36), P(1.15, 0.12 + i * 0.36), P(1.7, 0.1 + i * 0.36)]);
      S.line('back', rd, mul(ngC, 0.55), 1.4);
    }, S.lw);
    S.shape('back', spline([G.h3(G.lamFar + 0.05, 0.1, 1.14), P(-1.2, 0.3), P(-1.2, 1.0, 0.4), P(-0.9, 1.05)], true), mul(ngC, 0.8), null, S.lw);
    const apex = v === 'spike' ? [G.hx + 0.05 * R, G.ey - 2.45 * R] : null;
    bowl(S, G, sp, T, { metal, cone: v === 'spike', apex, lift: 1.12, ribs: [-0.55, 0.2, 0.95], trim: sp.hat.trim });
    if (v === 'round') {
      // 宽檐
      const br = band(G, 0.02, 0.34, 1.2, 16).slice(1, -1);
      S.add('hat', `<path d="${taper(br, t => 0.12 * R * Math.sin(Math.PI * clamp(t * 1.05, 0, 1)) + 0.5)}" fill="${mix(metal, '#fff', 0.1)}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      const top = G.h3(0.6, 1.48, 1.18);
      tassel(S, 'hat', G, top, sp.hat.plume || '#c0302a', S.o.lod === 0 ? 4 : 6, 0.9, 1);
      S.add('hat', circle(top[0], top[1], 0.12 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1.3"/>`);
    } else if (v === 'spike') {
      S.add('hat', `<path d="M${N(apex[0] - 0.08 * R)},${N(apex[1] + 0.1 * R)}L${N(apex[0])},${N(apex[1] - 0.6 * R)}L${N(apex[0] + 0.08 * R)},${N(apex[1] + 0.1 * R)}Z" fill="${GOLD}" stroke="${INK}" stroke-width="1.2"/>`);
      tassel(S, 'hat', G, add(apex, 0, -0.1 * R), sp.hat.plume || '#c0302a', 4, 0.6, 1);
    } else if (v === 'plume') {
      const top = G.h3(0.5, 1.45, 1.16);
      feather(S, 'hat', [top, add(top, -0.1 * R, -0.7 * R), add(top, 0.25 * R, -1.4 * R)], 0.2 * R, sp.hat.plume && sp.hat.plume !== '#c0302a' ? sp.hat.plume : '#ece6d8');
      feather(S, 'hat', [top, add(top, 0.3 * R, -0.6 * R), add(top, 0.75 * R, -1.15 * R)], 0.18 * R, '#c8a050');
    }
    return { coversTop: true };
  };
  // 头巾补充：noTails（不垂巾尾）、big（更大的缠头）
  const turbanBase = HATS.turban;
  HATS.turban = function (S, G, sp, T) {
    if (!sp.hat.noTails && !sp.hat.big) return turbanBase(S, G, sp, T);
    const col = sp.hat.color || '#ece6da';
    const C = clothT(col), R = G.R;
    const s = sp.hat.big ? 1.2 : 1.13;
    const sh = capShape(G, 0.12, 0.38, s, sp.hat.big ? 1.2 : 1.1);
    S.shape('hat', spline(sh.cap, true), C.b, () => {
      capShade(S, G, C);
      for (let i = 0; i < 5; i++) {
        const pts = [];
        for (let j = 0; j <= 10; j++) { const lam = G.lamFar + (G.lamNear - G.lamFar) * j / 10; pts.push(G.h3(lam, 0.4 + i * 0.22 - 0.25 * Math.sin(lam - 0.4) + 0.1 * Math.cos(lam), s * 1.01)); }
        S.path('hat', spline(pts.concat(shift(pts, 0, 0.08 * R).reverse()), true), C.d, 0, ' opacity="0.4"');
      }
    }, S.lw);
    bandShadow(S, G, sh.bnd, T, 0.18);
    return { coversTop: true };
  };
  // 勾玉（逗号形）
  function magatama(x, y, r, col) {
    return `<path d="M${N(x)},${N(y - r)}a${N(r)},${N(r)} 0 1,1 0,${N(2 * r)}q${N(-r * 1.2)},${N(r * 0.2)} ${N(-r * 1.4)},${N(r * 1.4)}q${N(-r * 0.6)},${N(-r * 1.6)} ${N(r * 1.4)},${N(-r * 3.4)}z" fill="${col}" stroke="${INK}" stroke-width="0.8"/>`;
  }

  // ======================================================== 各文化衣甲 ==
  // 裸露的躯干（肌肉线条）
  function bareTorso(S, G, sp, T) {
    const K = T.skin, P = G.P;
    // 女性不赤膊：内穿短衣
    if (sp.sex === 'f') { OUTFITS.tunic(S, G, Object.assign({}, sp, { outfit: Object.assign({}, sp.outfit, { color: sp.outfit.color2 || '#7a3a24' }) }), T); return; }
    torsoBase(S, G, sp, T, K.b, K.s, () => {
      // 斜方肌的背光面、锁骨下与三角肌的阴影（赛璐璐两阶）
      S.path('body', spline([P(0.55, 1.5), P(1.3, 1.85), P(2.3, 2.15), P(2.9, 2.7), P(3.4, 2.4, 0), P(3.4, 1.4, 0)], true), K.s);
      S.path('body', spline([P(-0.35, 2.2), P(-1.0, 2.42), P(-1.7, 2.5), P(-1.1, 2.75), P(-0.4, 2.6)], true), K.s, 0, ' opacity="0.7"');
      S.path('body', spline([P(0.6, 2.12), P(1.4, 2.25), P(2.0, 2.35), P(1.6, 2.65), P(0.7, 2.5)], true), K.s, 0, ' opacity="0.8"');
      S.path('body', spline([P(-2.95, 2.75), P(-2.4, 2.5), P(-2.1, 2.9), P(-2.5, 3.6), P(-3.1, 3.6, 0)], true), K.s, 0, ' opacity="0.55"');
      if (S.o.lod >= 1) {
        // 锁骨、胸肌、三角肌
        S.line('body', spline([P(-0.4, 2.05), P(-1.0, 2.25), P(-1.6, 2.35)]) + spline([P(0.7, 1.95), P(1.4, 2.05), P(2.1, 2.15)]), K.line, 1.6, ' opacity="0.8"');
        S.line('body', spline([P(-2.1, 3.0), P(-1.5, 3.4), P(-0.7, 3.3), P(-0.45, 3.0)]) + spline([P(-0.3, 2.95), P(0.4, 3.3), P(1.4, 3.2), P(1.9, 2.75)]), K.line, 1.5, ' opacity="0.7"');
        S.line('body', spline([P(-0.4, 2.3), P(-0.38, 3.0), P(-0.4, 4.2)]), K.line, 1.1, ' opacity="0.45"');
        S.line('body', spline([P(-2.2, 2.6), P(-2.45, 3.3)]) + spline([P(2.2, 2.4), P(2.5, 3.1)]), K.line, 1.2, ' opacity="0.5"');
      }
      S.path('body', spline([P(-2.6, 2.55), P(-2.0, 2.4), P(-1.6, 2.7), P(-2.2, 3.0)], true), K.h, 0, ' opacity="0.6"');
    });
  }
  function patternSpots(S, G, pts, col, n, seed, stripes) {
    const R = G.R, rr = Rng(seed);
    let d = '';
    const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
    const x0 = Math.min(...xs), x1 = Math.max(...xs), y0 = Math.min(...ys), y1 = Math.max(...ys);
    for (let i = 0; i < n; i++) {
      const x = rr.range(x0, x1), y = rr.range(y0, y1);
      if (stripes) d += `M${N(x)},${N(y)}q${N(0.15 * R)},${N(0.25 * R)} ${N(0.05 * R)},${N(0.55 * R)}`;
      else d += `M${N(x)},${N(y)}m-3,0a3,2.5 0 1,0 6,0a3,2.5 0 1,0 -6,0`;
    }
    return d;
  }
  Object.assign(OUTFITS, {
    bare(S, G, sp, T) { bareTorso(S, G, sp, T); },
    // 兽皮（豹 / 虎）斜披远侧肩
    pelt(S, G, sp, T) {
      const P = G.P, R = G.R;
      if (sp.sex === 'f') OUTFITS.tunic(S, G, Object.assign({}, sp, { outfit: Object.assign({}, sp.outfit, { color: sp.outfit.color2 || '#7a3a24' }) }), T);
      else bareTorso(S, G, sp, T);
      const tiger = sp.outfit.pelt === 'tiger' || (!sp.outfit.pelt && hashStr(sp.name) % 2);
      const pc = tiger ? '#d08a30' : '#d8a848';
      const C = clothT(pc);
      const pts = [P(-0.55 * G.nw, 1.72), P(-1.5 * G.bw, 2.12), P(-2.5 * G.bw, 2.55), P(-3.0 * G.bw, 3.2), P(-3.2 * G.bw, 4.5, 0), P(1.8, 4.5, 0), P(0.6, 3.4), P(-0.1, 2.4)];
      const d = spline(pts, true);
      S.shape('body2', d, C.b, () => {
        S.path('body2', spline([P(-0.4, 2.5), P(0.9, 3.6), P(2.0, 4.6), P(-0.5, 4.6)], true), C.s);
        S.add('body2', `<path d="${patternSpots(S, G, pts, INK, tiger ? 14 : 26, hashStr(sp.name + 'pelt'), tiger)}" fill="${tiger ? 'none' : mul(pc, 0.35)}" stroke="${INK}" stroke-width="${tiger ? 2.6 : 1.1}" opacity="0.85"/>`);
      }, S.lw);
      furEdge(S, 'body2', G, [P(-0.1, 2.4), P(0.6, 3.4), P(1.8, 4.5)], pc, 0.1, 9);
      // 兽爪垂在胸前
      const k = P(-0.2, 2.5);
      S.add('body2', `<path d="${leaf(k, add(k, 0.4 * R, 0.9 * R), 0.4 * R, 0.1)}" fill="${C.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      let cl = '';
      for (let i = 0; i < 3; i++) cl += `M${ps(add(k, (0.3 + i * 0.08) * R, (0.85 - i * 0.05) * R))}l${N(0.04 * R)},${N(0.16 * R)}`;
      S.line('body2', cl, '#f4ecd8', 2);
    },
    // 藤甲：油浸藤条编成，深褐带光泽
    rattan(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.rattan || '#9a7a3a');
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        let wd = '';
        for (let i = -10; i < 16; i++) { wd += `M${ps(P(i * 0.34 - 2, 1.6))}l${N(2.2 * R)},${N(2.8 * R)}`; wd += `M${ps(P(i * 0.34 + 2, 1.6))}l${N(-2.2 * R)},${N(2.8 * R)}`; }
        S.line('body', wd, C.d, 1.4, ' opacity="0.55"');
        let hb = '';
        for (const y of [2.4, 3.05, 3.7]) hb += spline([P(-3.2, y + 0.1), P(-0.5, y - 0.05), P(3.4, y + 0.05)]);
        S.add('body', `<path d="${hb}" fill="none" stroke="${INK}" stroke-width="${N(0.16 * R + 2)}"/><path d="${hb}" fill="none" stroke="${C.s}" stroke-width="${N(0.16 * R)}"/>`);
        S.path('body', spline([P(-2.5, 2.5), P(-1.6, 2.3), P(-1.2, 2.8), P(-2.2, 3.2)], true), C.h, 0, ' opacity="0.45"');
      });
      pauldron(S, G, sp, T, metalT(C.b), -1, mul(C.b, 0.6));
      pauldron(S, G, sp, T, metalT(C.b), 1, mul(C.b, 0.6));
    },
    // 孟获：缨络红锦袍
    chiefrobe(S, G, sp, T) {
      const P = G.P, R = G.R;
      OUTFITS.robe(S, G, Object.assign({}, sp, { outfit: Object.assign({ color: '#a8282a', color2: '#d8a040' }, sp.outfit) }), T);
      // 缨络（成串珠饰）
      for (let k = 0; k < 2; k++) {
        const sw = resample([P(-1.5 + k * 0.2, 2.25), P(-0.4, 2.9 + k * 0.45), P(0.8, 2.75 + k * 0.4), P(1.7 - k * 0.1, 2.1)], 13);
        let bd = '';
        sw.forEach((p, i) => { bd += circle(p[0], p[1], (i % 3 === 1 ? 0.09 : 0.06) * R) + ` fill="${i % 3 === 1 ? '#2aa0a0' : i % 3 === 2 ? '#e8c050' : '#c83040'}" stroke="${INK}" stroke-width="0.8"/>`; });
        S.add('body2', bd);
      }
    },
    // 贯头衣（倭）：麻布，中间开领口
    kantoui(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.color || '#e6dcc4');
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        if (S.o.lod >= 1) {
          S.line('body', spline([P(-1.9, 2.9), P(-1.7, 3.5), P(-1.75, 4.3)]) + spline([P(1.9, 2.7), P(2.05, 3.4), P(2.0, 4.3)]), C.d, 1.4, ' opacity="0.6"');
          // 染色纹样带
          const st = sp.outfit.color2 || '#9a3a2a';
          S.add('body', `<path d="${spline([P(-3, 3.5), P(0, 3.35), P(3.4, 3.45)])}" fill="none" stroke="${st}" stroke-width="${N(0.18 * R)}" opacity="0.85"/>`);
          let td = '';
          for (let i = 0; i < 12; i++) td += `M${ps(P(-2.6 + i * 0.5, 3.48))}l${N(0.12 * R)},${N(-0.1 * R)}l${N(0.12 * R)},${N(0.1 * R)}`;
          S.line('body', td, '#f4ecd8', 1.1, ' opacity="0.8"');
        }
      });
      // 领口
      S.add('body2', `<path d="${spline([P(-0.5 * G.nw, 1.75), P(0.1, 2.15), P(0.95 * G.nw, 1.5)])}" fill="none" stroke="${INK}" stroke-width="${N(S.lw)}"/>`);
    },
    // 短甲（倭，横矧板铆留）
    tanko(S, G, sp, T) {
      const P = G.P, R = G.R;
      const Mt = metalT(sp.outfit.metal || '#4a4c54');
      torsoBase(S, G, sp, T, Mt.s, Mt.d);
      let bd = '', rv = '';
      for (let i = 0; i < 5; i++) {
        const y = 2.3 + i * 0.42;
        bd += spline([P(-3.2, y + 0.1), P(-0.5, y - 0.06), P(3.4, y + 0.04)]);
        if (S.o.lod >= 1) for (let j = 0; j < 14; j++) { const p = P(-2.8 + j * 0.45, y + 0.1 - (j > 5 ? 0.02 : 0)); rv += circle(p[0], p[1], 0.9) + '/>'; }
      }
      S.add('body', `<g clip-path="url(#${S.id('torso')})"><path d="${bd}" fill="none" stroke="${INK}" stroke-width="2.2"/><path d="${bd}" fill="none" stroke="${Mt.h}" stroke-width="0.8" transform="translate(0,1.5)" opacity="0.6"/><g fill="${Mt.h}">${rv}</g></g>`);
      // 颈甲
      const C = clothT(sp.outfit.color || '#8a2a24');
      scarf(S, G, sp, T, C);
      pauldron(S, G, sp, T, Mt, -1, '#c8a050');
      pauldron(S, G, sp, T, Mt, 1, '#c8a050');
    },
    // 夷洲：敞开的织纹背心
    yivest(S, G, sp, T) {
      const P = G.P, R = G.R;
      bareTorso(S, G, sp, T);
      const C = clothT(sp.outfit.color === '#1a1a1a' ? '#e8dcc0' : (sp.outfit.color || '#e8dcc0'));
      const pat = sp.outfit.color2 || '#b02a24';
      for (const [pts, side] of [[[P(0.95 * G.nw, 1.42), P(1.6, 1.8), P(2.65, 2.3), P(3.1, 2.9), P(3.4, 4.5, 0), P(0.9, 4.5, 0), P(0.6, 2.8)], 1], [[P(-0.52 * G.nw, 1.72), P(-1.5, 2.1), P(-2.45, 2.6), P(-2.9, 3.1), P(-3.1, 4.5, 0), P(-1.1, 4.5, 0), P(-0.8, 2.6)], -1]]) {
        S.shape('body2', spline(pts, true), side > 0 ? C.s : C.b, () => {
          let zd = '';
          for (let r = 0; r < 4; r++) { let x = side > 0 ? 0.6 : -3.1; zd += `M${ps(P(x, 2.9 + r * 0.38))}`; for (let i = 0; i < 9; i++) { x += 0.3; zd += `L${ps(P(x, 2.9 + r * 0.38 + (i % 2 ? -0.14 : 0.14)))}`; } }
          S.line('body2', zd, pat, 2.2);
        }, S.lw);
      }
    },
    // 朝鲜：交领短袄（高句丽壁画的圆点纹）
    jacket(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.color || '#c84a2a');
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        const rr = Rng(hashStr(sp.name + 'dots'));
        let dd = '';
        for (let i = 0; i < 36; i++) { const p = P(rr.range(-3, 3.2), rr.range(2.0, 4.3)); dd += circle(p[0], p[1], 0.07 * R) + '/>'; }
        S.add('body', `<g fill="${sp.outfit.dots || mix(C.b, '#1a1010', 0.55)}" opacity="0.75">${dd}</g>`);
      });
      crossCollar(S, G, sp, T, sp.outfit.color2 || '#2a2a3a', '#ece4d4', 1.1);
    },
    // 翻领长袍（草原 / 西域 / 贵霜）：两片三角翻领
    kaftan(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.color || '#8a5a3a');
      const tr = clothT(sp.outfit.color2 || '#c8a050');
      const cross = sp.outfit.cross || 1;
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        if (S.o.lod >= 1) S.line('body', spline([P(-1.9, 2.9), P(-1.7, 3.5), P(-1.75, 4.3)]) + spline([P(1.9, 2.7), P(2.05, 3.4), P(2.0, 4.3)]), C.d, 1.4, ' opacity="0.6"');
      });
      // 内衬
      S.add('body2', `<path d="${spline([P(-0.5 * G.nw, 1.72), P(0.92 * G.nw, 1.45), P(0.35, 2.7, 0)], true)}" fill="${sp.outfit.inner || '#e8dcc0'}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
      // 前襟镶边（直通下摆）
      const edge = cross > 0 ? [P(0.35, 2.6), P(0.1, 3.4), P(-0.2, 4.5)] : [P(0.35, 2.6), P(0.6, 3.4), P(0.85, 4.5)];
      S.add('body2', `<path d="${taper(edge, 0.2 * R)}" fill="${tr.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
      for (const [pts, fill] of [[[P(0.92 * G.nw, 1.42), P(0.38, 2.62, 0), P(1.25, 2.55, 0.3), P(1.2, 1.75)], tr.s], [[P(-0.5 * G.nw, 1.72), P(0.34, 2.62, 0), P(-0.75, 2.75, 0.3), P(-0.9, 2.0)], tr.b]]) {
        S.shape('body2', spline(pts, true), fill, () => {
          if (S.o.lod >= 2) S.line('body2', spline(shift(pts.slice(0, 3), 0, 0.06 * R)), tr.h, 1.2, ' opacity="0.8" stroke-dasharray="2 2"');
        }, S.lw * 0.85);
      }
    },
    // 毛皮大衣：厚毛领
    fur(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.color || '#5a4a3a');
      const furC = sp.outfit.fur || mix(sp.outfit.color2 || '#8a6a4a', '#7a5a3a', 0.5);
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        if (S.o.lod >= 1) S.line('body', spline([P(-1.9, 2.9), P(-1.7, 3.5), P(-1.75, 4.3)]) + spline([P(1.9, 2.7), P(2.05, 3.4), P(2.0, 4.3)]), C.d, 1.4, ' opacity="0.6"');
      });
      S.add('body2', `<path d="${spline([P(-0.5 * G.nw, 1.72), P(0.92 * G.nw, 1.45), P(0.3, 3.2, 0)], true)}" fill="${sp.outfit.inner || '#d8ccb0'}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
      const F = clothT(furC);
      const collar = [P(-0.75 * G.nw, 1.6), P(-1.7, 2.0), P(-2.6, 2.55), P(-1.6, 2.9), P(-0.4, 3.3), P(0.3, 3.3, 0.3), P(1.6, 2.7), P(2.7, 2.3), P(1.6, 1.6), P(1.0 * G.nw, 1.3), P(0.5, 2.6, 0.3), P(-0.3, 2.6, 0.3)];
      S.shape('body2', spline(collar, true), F.b, () => S.path('body2', spline([P(0.6, 1.4), P(2.8, 2.0), P(2.8, 3.0), P(0.6, 3.0)], true), F.s), S.lw);
      furEdge(S, 'body2', G, [P(-2.6, 2.55), P(-1.6, 2.95), P(-0.4, 3.35), P(0.3, 3.35), P(1.6, 2.75), P(2.75, 2.35)], furC, 0.12, 16);
    },
    // 南海：裸身斜披布带，金饰
    sash(S, G, sp, T) {
      const P = G.P, R = G.R;
      if (sp.sex === 'f') OUTFITS.tunic(S, G, Object.assign({}, sp, { outfit: Object.assign({}, sp.outfit, { color: sp.outfit.color2 || '#c8a030' }) }), T);
      else bareTorso(S, G, sp, T);
      const C = clothT(sp.outfit.color || '#c8a030');
      const sh = [P(-1.6, 2.15), P(-0.8, 2.0), P(1.6, 4.5, 0), P(0.5, 4.5, 0), P(-1.9, 2.55)];
      S.shape('body2', spline(sh, true), C.b, () => {
        S.path('body2', spline([P(0.2, 3.3), P(1.8, 4.6), P(0.4, 4.6)], true), C.s);
        if (S.o.lod >= 1) S.line('body2', spline([P(-1.5, 2.2), P(0.9, 4.5)]), GOLD, 1.6, ' opacity="0.9"');
      }, S.lw);
    },
    // 鱼鳞甲（安息、萨尔马提亚、西域）
    scale(S, G, sp, T) {
      const Mt = metalT(sp.outfit.metal || '#7a808c');
      torsoBase(S, G, sp, T, Mt.s, Mt.d);
      lamellae(S, G, Mt, G.ey + 1.85 * G.R, 262, { scale: true, rowH: 0.17, pw: 0.2, alt: mix(Mt.b, Mt.s, 0.35) });
      const C = clothT(sp.outfit.color || '#7a2a24');
      if (sp.culture === 'persia' || sp.culture === 'sarmatian') {
        const P = G.P;
        S.add('body2', `<path d="${taper([P(-0.6 * G.nw, 1.62), P(-0.2, 1.95), P(0.35, 1.98), P(0.85, 1.72), P(1.0 * G.nw, 1.42)], 0.22 * G.R)}" fill="${C.b}" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>`);
      } else scarf(S, G, sp, T, C);
      pauldron(S, G, sp, T, Mt, -1, sp.outfit.trim);
      pauldron(S, G, sp, T, Mt, 1, sp.outfit.trim);
    },
    // 长衫（圆领 / V 领 + 镶边，罗马与波斯的两道竖纹）
    tunic(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.color || '#8a2a24');
      const tr = sp.outfit.color2 || '#c8a050';
      torsoBase(S, G, sp, T, C.b, C.s, () => {
        if (S.o.lod >= 1) S.line('body', spline([P(-1.9, 2.9), P(-1.7, 3.5), P(-1.75, 4.3)]) + spline([P(1.9, 2.7), P(2.05, 3.4), P(2.0, 4.3)]), C.d, 1.4, ' opacity="0.6"');
        if (sp.culture === 'roman' || sp.culture === 'persia' || sp.culture === 'arab') S.add('body', `<path d="${spline([P(-1.2, 2.15), P(-1.25, 4.5)])}${spline([P(0.5, 2.05), P(0.55, 4.5)])}" fill="none" stroke="${tr}" stroke-width="${N(0.16 * R)}" opacity="0.9"/>`);
      });
      const nl = [P(-0.5 * G.nw, 1.72), P(-0.1, 2.3), P(0.35, 2.32), P(0.92 * G.nw, 1.45)];
      S.add('body2', `<path d="${taper(nl, 0.16 * R)}" fill="${tr}" stroke="${INK}" stroke-width="${N(S.lw * 0.75)}"/>`);
      if (S.o.lod >= 2) S.add('body2', `<path d="${spline(nl)}" fill="none" stroke="${mul(tr, 0.6)}" stroke-width="1" stroke-dasharray="1.5 2"/>`);
    },
    // 阿拉伯长袍 + 斗篷
    robea(S, G, sp, T) {
      const white = sp.outfit.color === '#e8e0d0';
      OUTFITS.tunic(S, G, Object.assign({}, sp, { outfit: Object.assign({}, sp.outfit, { color: '#e8e0d0', color2: white ? (sp.outfit.color2 || '#5a3a2a') : sp.outfit.color }) }), T);
      if (!sp.outfit.cape) mantle(S, G, sp, T, white ? (sp.outfit.color2 || '#5a3a2a') : sp.outfit.color);
    },
    // 罗马环片甲
    segmentata(S, G, sp, T) {
      const P = G.P, R = G.R;
      const Mt = metalT(sp.outfit.metal || '#8a8f9a');
      torsoBase(S, G, sp, T, Mt.b, Mt.s);
      let bd = '';
      for (let i = 0; i < 6; i++) { const y = 2.5 + i * 0.36; bd += spline([P(-3.2, y + 0.12), P(-0.5, y - 0.06), P(3.4, y + 0.06)]); }
      S.add('body', `<g clip-path="url(#${S.id('torso')})"><path d="${bd}" fill="none" stroke="${INK}" stroke-width="2.2"/><path d="${bd}" fill="none" stroke="${Mt.h}" stroke-width="1" transform="translate(0,1.6)" opacity="0.7"/></g>`);
      // 胸前铜扣
      if (S.o.lod >= 1) for (const y of [2.62, 2.98]) { const c = P(-0.45, y); S.add('body', `<rect x="${N(c[0] - 3)}" y="${N(c[1] - 2)}" width="6" height="4" fill="#c8a050" stroke="${INK}" stroke-width="0.8"/>`); }
      // 肩部环片
      for (const side of [-1, 1]) {
        const sx = side > 0 ? 2.3 : -2.15, sy = side > 0 ? 2.4 : 2.65;
        for (let i = 3; i >= 0; i--) {
          const w = side > 0 ? 1.05 : 0.8;
          const pts = [P(sx - side * w, sy - 0.3 + i * 0.22, 0.4), P(sx, sy - 0.62 + i * 0.22), P(sx + side * w, sy - 0.18 + i * 0.22, 0.4), P(sx + side * w, sy + 0.04 + i * 0.22), P(sx, sy - 0.4 + i * 0.22), P(sx - side * w, sy - 0.08 + i * 0.22)];
          S.shape('body2', spline(pts, true), i % 2 ? Mt.s : Mt.b, null, S.lw * 0.8);
        }
      }
      scarf(S, G, sp, T, clothT(sp.outfit.color || '#a02a24'));
    },
    // 罗马胸甲（肌肉甲）+ 肩部皮条
    musculata(S, G, sp, T) {
      const P = G.P, R = G.R;
      const Mt = metalT(sp.outfit.metal || '#b08a50');
      // 肩部皮条（pteruges）
      const pc = sp.outfit.color2 || '#e8dcc0';
      for (const side of [-1, 1]) {
        let d = '';
        for (let i = 0; i < 5; i++) { const p = P(side * (side > 0 ? 2.3 : 2.05) + side * (i - 2) * 0.22, side > 0 ? 2.6 : 2.8); d += `M${N(p[0] - 0.09 * R)},${N(p[1])}h${N(0.18 * R)}v${N(0.8 * R)}h${N(-0.18 * R)}z`; }
        S.add('body', `<path d="${d}" fill="${pc}" stroke="${INK}" stroke-width="1.2"/>`);
      }
      const cuir = [P(-0.5 * G.nw, 1.75), P(-1.4 * G.bw, 2.15), P(-2.0 * G.bw, 2.6), P(-2.1, 4.5, 0), P(2.4, 4.5, 0), P(2.25 * G.bw, 2.45), P(1.5 * G.bw, 1.85), P(0.92 * G.nw, 1.5)];
      S.shape('body', spline(cuir, true), Mt.b, () => {
        S.path('body', spline([P(0.75, 1.4), P(1.15, 2.4), P(1.25, 3.2), P(1.1, 4.6, 0), P(4, 4.6, 0), P(4, 1.4, 0)], true), Mt.s);
        S.line('body', spline([P(-2.0, 3.0), P(-1.4, 3.45), P(-0.6, 3.3), P(-0.42, 3.05)]) + spline([P(-0.3, 3.0), P(0.4, 3.35), P(1.4, 3.25), P(2.0, 2.8)]) + spline([P(-0.4, 3.4), P(-0.4, 4.4)]), Mt.d, 2.0);
        S.path('body', spline([P(-1.9, 2.6), P(-1.2, 2.5), P(-0.8, 3.0), P(-1.6, 3.2)], true), Mt.h, 0, ' opacity="0.7"');
      }, S.lw, 'torso');
      rimOn(S, 'body', cuir, T.rim, 3.2, 0.85);
      if (!sp.outfit.cape) mantle(S, G, sp, T, sp.outfit.color || '#a02a24');
    },
    // 托加袍：白袍斜搭，紫边
    toga(S, G, sp, T) {
      const P = G.P, R = G.R;
      const C = clothT(sp.outfit.toga || '#ece6da');
      torsoBase(S, G, sp, T, C.b, C.s);
      const drape = [P(-0.6 * G.nw, 1.7), P(-1.5, 2.1), P(-2.5, 2.6), P(-3.0, 3.2), P(-3.2, 4.5, 0), P(-0.2, 4.5, 0), P(0.6, 3.3), P(1.1, 2.4), P(0.2, 2.6)];
      S.shape('body2', spline(drape, true), C.b, () => {
        let fd = '';
        for (let i = 0; i < 5; i++) fd += spline([P(-1.6 + i * 0.4, 2.3 + i * 0.05), P(-0.9 + i * 0.4, 3.2), P(-0.6 + i * 0.35, 4.4)]);
        S.line('body2', fd, C.d, 1.5, ' opacity="0.6"');
        S.path('body2', spline([P(-0.2, 2.8), P(1.2, 2.3), P(0.8, 4.6), P(-0.4, 4.6)], true), C.s, 0, ' opacity="0.7"');
      }, S.lw);
      S.add('body2', `<path d="${taper([P(1.1, 2.4), P(0.6, 3.3), P(-0.2, 4.5)], 0.14 * R)}" fill="${sp.outfit.color2 || '#6a2a6a'}" stroke="${INK}" stroke-width="${N(S.lw * 0.7)}"/>`);
    },
    // 凯尔特：方格斗篷 + 圆形别针
    plaid(S, G, sp, T) {
      const P = G.P, R = G.R;
      OUTFITS.tunic(S, G, Object.assign({}, sp, { outfit: Object.assign({}, sp.outfit, { color: sp.outfit.color2 || '#6a5a3a', color2: '#8a6a3a' }) }), T);
      const C = clothT(sp.outfit.color || '#3a6a3a');
      const cloak = [P(0.95 * G.nw, 1.42), P(1.7 * G.bw, 1.78), P(2.75 * G.bw, 2.3, 0.8), P(3.25 * G.bw, 3.0), P(3.5 * G.bw, 4.5, 0), P(-0.5, 4.5, 0), P(-0.3, 3.0), P(-0.6 * G.nw, 1.75), P(-1.5, 2.1), P(-2.5, 2.6), P(-2.9, 3.0), P(-2.4, 2.4)];
      S.shape('body2', spline(cloak, true), C.b, () => {
        const ck = sp.outfit.check || '#c8a040';
        let hd = '', vd = '';
        for (let i = 0; i < 10; i++) { hd += `M${ps(P(-3.4, 1.6 + i * 0.32))}l${N(7.2 * R)},${N(0.3 * R)}`; vd += `M${ps(P(-3.2 + i * 0.75, 1.4))}l${N(0.2 * R)},${N(3.4 * R)}`; }
        S.add('body2', `<path d="${hd}${vd}" fill="none" stroke="${ck}" stroke-width="${N(0.1 * R)}" opacity="0.5"/>`);
        S.add('body2', `<path d="${hd}${vd}" fill="none" stroke="${C.d}" stroke-width="${N(0.05 * R)}" opacity="0.6" transform="translate(${N(0.12 * R)},${N(0.12 * R)})"/>`);
        S.path('body2', spline([P(0.75, 1.4), P(1.15, 2.4), P(1.25, 3.2), P(1.1, 4.6, 0), P(4, 4.6, 0), P(4, 1.4, 0)], true), C.s, 0, ' opacity="0.6"');
      }, S.lw);
      const b = P(1.2, 2.2);
      S.add('body2', circle(b[0], b[1], 0.24 * R) + ` fill="none" stroke="${INK}" stroke-width="${N(0.1 * R + 2)}"/>` + circle(b[0], b[1], 0.24 * R) + ` fill="none" stroke="${GOLD}" stroke-width="${N(0.1 * R)}"/>`);
      S.line('body2', `M${ps(add(b, -0.35 * R, -0.25 * R))}L${ps(add(b, 0.35 * R, 0.25 * R))}`, GOLD, 1.6);
    },
  });

  // ======================================================== 配饰 ==
  function catenary(G, a, b, sag, n) {
    const out = [];
    for (let i = 0; i <= n; i++) { const t = i / n; out.push([lerp(a[0], b[0], t), lerp(a[1], b[1], t) + Math.sin(Math.PI * t) * sag]); }
    return out;
  }
  function drawAcc(S, G, sp, T) {
    const acc = sp.acc || [];
    if (!acc.length) return;
    const R = G.R, P = G.P, E = G.ear;
    const lobe = [lerp(E.x0, E.x1, 0.42), E.y1 - 0.02 * R];
    const earVisible = !G.hideEar;
    for (const a of acc) {
      if ((a === 'earring' || a === 'hoops' || a === 'bigears') && earVisible) {
        if (a === 'earring') S.add('ear', `<path d="M${ps(lobe)}l0,${N(0.12 * R)}" stroke="${GOLD}" stroke-width="1.4"/>` + circle(lobe[0], lobe[1] + 0.2 * R, 0.08 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1"/>`);
        else if (a === 'hoops') S.add('ear', circle(lobe[0], lobe[1] + 0.18 * R, 0.2 * R) + ` fill="none" stroke="${INK}" stroke-width="${N(0.07 * R + 2)}"/>` + circle(lobe[0], lobe[1] + 0.18 * R, 0.2 * R) + ` fill="none" stroke="${GOLD}" stroke-width="${N(0.07 * R)}"/>`);
        else S.add('ear', ellipse(lobe[0], lobe[1] + 0.12 * R, 0.13 * R, 0.17 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1.2"/>` + ellipse(lobe[0], lobe[1] + 0.12 * R, 0.06 * R, 0.08 * R) + ' fill="#8a5a20"/>');
      }
      if (a === 'bones' || a === 'magatama' || a === 'shells') {
        const pts = catenary(G, P(-0.95, 2.0), P(1.05, 1.7), (a === 'bones' ? 0.75 : 0.6) * R, 11);
        S.add('body2', `<path d="${spline(pts)}" fill="none" stroke="${INK}" stroke-width="2"/>`);
        pts.slice(1, -1).forEach((p, i) => {
          if (a === 'bones') S.add('body2', `<path d="${leaf(p, add(p, 0.02 * R, 0.3 * R), 0.12 * R, 0.1)}" fill="#efe6d0" stroke="${INK}" stroke-width="1"/>`);
          else if (a === 'magatama') S.add('body2', i % 2 ? magatama(p[0], p[1] + 0.06 * R, 0.08 * R, '#3a9a6a') : circle(p[0], p[1], 0.05 * R) + ' fill="#d8d0b8" stroke="#1a1012" stroke-width="0.7"/>');
          else S.add('body2', ellipse(p[0], p[1] + 0.04 * R, 0.07 * R, 0.05 * R) + ` fill="#f4f0e4" stroke="${INK}" stroke-width="0.8"/>`);
        });
      }
      if (a === 'torc') {
        const pts = [P(-0.62 * G.nw, 1.62), P(-0.2, 1.95), P(0.35, 1.98), P(0.85, 1.74), P(1.02 * G.nw, 1.45)];
        S.add('body2', `<path d="${taper(pts, 0.11 * R)}" fill="${GOLD}" stroke="${INK}" stroke-width="1.2"/>`);
        if (S.o.lod >= 1) S.add('body2', `<path d="${spline(pts)}" fill="none" stroke="#8a5a1e" stroke-width="1" stroke-dasharray="2 2"/>`);
        for (const p of [P(-0.12, 2.05), P(0.22, 2.08)]) S.add('body2', circle(p[0], p[1], 0.1 * R) + ` fill="${GOLD}" stroke="${INK}" stroke-width="1.1"/>`);
      }
      if (a === 'goldcollar') {
        const outer = catenary(G, P(-1.8, 2.3), P(2.0, 1.9), 1.15 * R, 12), inner = catenary(G, P(-0.6, 1.85), P(0.95, 1.55), 0.55 * R, 8);
        S.shape('body2', spline(outer.concat(inner.reverse()), true), GOLD, () => {
          let jd = '';
          for (const p of catenary(G, P(-1.2, 2.1), P(1.5, 1.75), 0.85 * R, 9).slice(1, -1)) jd += circle(p[0], p[1], 0.07 * R) + '/>';
          S.add('body2', `<g fill="#c02a3a" stroke="${INK}" stroke-width="0.7">${jd}</g>`);
        }, S.lw * 0.8);
      }
      if (a === 'mirror') {
        const c = P(-0.5, 3.0);
        S.add('body2', circle(c[0], c[1], 0.36 * R) + ` fill="#b08a3a" stroke="${INK}" stroke-width="${N(S.lw * 0.8)}"/>` + circle(c[0], c[1], 0.26 * R) + ' fill="none" stroke="#7a5a20" stroke-width="1.3"/>' + circle(c[0] - 0.1 * R, c[1] - 0.1 * R, 0.1 * R) + ' fill="#f4e0a0" opacity="0.7"/>');
      }
      if (a === 'knives') {
        // 祝融：鬓插飞刀
        for (let i = 0; i < 3; i++) {
          const b = G.h3(0.9 + i * 0.22, 0.75 + i * 0.05, 1.08), t = add(b, (0.35 + i * 0.05) * R, -0.7 * R);
          S.add('hat', `<path d="${poly([add(b, -0.04 * R, 0), add(lp(b, t, 0.7), -0.07 * R, 0), t, add(lp(b, t, 0.7), 0.07 * R, 0), add(b, 0.04 * R, 0)])}" fill="#d8dee8" stroke="${INK}" stroke-width="1.1"/>`);
          S.add('hat', `<path d="M${ps(b)}l${N(-0.05 * R)},${N(0.18 * R)}" stroke="#8a2a20" stroke-width="2.4"/>`);
        }
      }
      if (a === 'flower') S.add('hat', flower(G.h3(1.05, 0.55, 1.1)[0], G.h3(1.05, 0.55, 1.1)[1], 0.18 * R, '#f06080'));
    }
  }
  // 面部纹样
  function drawFaceMarks(S, G, sp, T) {
    const R = G.R, F = G.F, P = G.P;
    for (const m of sp.marks || []) {
      if (m === 'tattoo-wa') {
        let d = '';
        for (let i = 0; i < 3; i++) d += spline([F(0.28 + i * 0.08, 0.2), F(0.24 + i * 0.1, 0.45), F(0.3 + i * 0.1, 0.7)]);
        for (let i = 0; i < 2; i++) d += spline([F(-0.62 - i * 0.08, 0.22), F(-0.6 - i * 0.08, 0.45), F(-0.66 - i * 0.08, 0.66)]);
        d += spline([F(-0.3, -0.6), F(-0.1, -0.68), F(0.1, -0.6)]);
        S.line('face', d, '#2a3a52', 1.6, ' opacity="0.75"');
      } else if (m === 'tattoo-yi') {
        let d = '';
        for (let i = -1; i <= 1; i++) d += `M${ps(F(i * 0.08 - 0.02, -0.75))}L${ps(F(i * 0.08 - 0.02, -0.45))}`;
        d += spline([F(-0.4, G.chinY - 0.1, 0.05), F(-0.1, G.chinY - 0.04, 0.1), F(0.2, G.chinY - 0.1, 0.05)]);
        d += spline([F(-0.36, G.chinY - 0.2, 0.05), F(-0.1, G.chinY - 0.14, 0.1), F(0.18, G.chinY - 0.2, 0.05)]);
        S.line('face', d, '#22303e', 2.0, ' opacity="0.8"');
      } else if (m === 'woad') {
        const c = F(0.3, 0.42);
        let d = `M${N(c[0])},${N(c[1])}m-1,0a2,2 0 1,1 2,2a4,4 0 1,1 -4,-4a6,6 0 1,1 6,6`;
        const c2 = F(-0.15, -0.62);
        d += `M${N(c2[0])},${N(c2[1])}m-1,0a2,2 0 1,1 2,2a4,4 0 1,1 -4,-4`;
        d += spline([F(-0.75, 0.25), F(-0.62, 0.42), F(-0.72, 0.6)]);
        S.line('face', d, '#2a5aa8', 1.8, ' opacity="0.75"');
      } else if (m === 'warpaint') {
        S.line('face', spline([F(0.12, 0.3), F(0.5, 0.25)]) + spline([F(0.14, 0.42), F(0.52, 0.38)]), '#c8302a', 2.4, ' opacity="0.85"');
        S.line('face', spline([F(-0.5, 0.3), F(-0.75, 0.28)]), '#c8302a', 2.2, ' opacity="0.85"');
        S.line('face', spline([F(-0.2, -0.58), F(0.05, -0.6)]), '#f0ece0', 2.2, ' opacity="0.9"');
      } else if (m === 'freckles' && S.o.lod >= 1) {
        const rr = Rng(hashStr(sp.name + 'fr'));
        let d = '';
        for (let i = 0; i < 18; i++) { const u = rr.range(-0.65, 0.4), y = rr.range(0.15, 0.55); const p = F(u, y, u > -0.2 && u < 0.1 ? 0.15 : 0); d += circle(p[0], p[1], 0.7) + '/>'; }
        S.add('face', `<g fill="${mix(T.skin.b, '#7a3a1a', 0.45)}" opacity="0.7">${d}</g>`);
      }
    }
    void P;
  }

  // ======================================================== 主装配 ==
  function figure(S, G, sp, T, o) {
    drawBody(S, G, sp, T, o);
    drawNeck(S, G, sp, T);
    drawHead(S, G, sp, T);
    const hatInfo = drawHat(S, G, sp, T);
    drawHair(S, G, sp, T, hatInfo);
    drawEar(S, G, sp, T);
    drawWrinkles(S, G, sp, T);
    drawEyes(S, G, sp, T, o.mood);
    drawBrows(S, G, sp, T, o.mood);
    drawNose(S, G, sp, T);
    drawMouth(S, G, sp, T, o.mood);
    drawMarks(S, G, sp, T, o.mood);
    drawBeard(S, G, sp, T);
    drawMustache(S, G, sp, T);
    drawWeapon(S, G, sp, T);
    drawFan(S, G, sp, T);
    drawAcc(S, G, sp, T);
    drawFaceMarks(S, G, sp, T);
  }
  const ZOOM = 1.1;
  // 生成各部分：fig（人物，含镜像 g）、defs（裁剪路径等）、bg / frame（背景与边框，只与颜色和细节档有关）
  function buildParts(sp, o) {
    const S = new Svg(o);
    const G = geom(sp);
    const rimC = mix('#d6ecff', o.color, 0.3);
    const T = { skin: skinT(sp.skin), rim: rimC };
    drawBackground(S, sp, o);
    drawFrame(S, o);
    figure(S, G, sp, T, o);
    const L = S.L;
    const head = L.head.join('').replace('%HEADSHADE%', S.headShade.join(''));
    const inner = L.back.join('') + L.collarBack.join('') + L.neck.join('') + L.body.join('') + L.body2.join('') + head + L.hair.join('') + L.ear.join('') + L.face.join('') + L.beard.join('') + L.hat.join('') + L.front.join('') + L.top.join('');
    const flip = o.flip ? ' transform="matrix(-1 0 0 1 256 0)"' : '';
    // 构图：人物整体以 (132, 150) 为中心放大 ZOOM 倍（原作头像的脸占画面更大）
    const zoom = `translate(${N(132 * (1 - ZOOM))},${N(150 * (1 - ZOOM))}) scale(${ZOOM})`;
    return { S, fig: `<g${flip} stroke-linejoin="round" stroke-linecap="round"><g transform="${zoom}">${inner}</g></g>`, w: o.frame ? S.win : 0 };
  }
  function winClipDef(S, w) {
    return `<clipPath id="${S.id('win')}"><rect x="${w}" y="${w}" width="${256 - 2 * w}" height="${256 - 2 * w}"/></clipPath>`;
  }
  function buildSvg(sp, o) {
    const { S, fig, w } = buildParts(sp, o);
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${o.px}" height="${o.px}" viewBox="0 0 256 256"><defs>${winClipDef(S, w)}${S.defs.join('')}</defs>` +
      `<g clip-path="url(#${S.id('win')})">${S.bg}${fig}</g>` + (o.frame ? S.frame : '') + '</svg>';
  }
  // 只含背景（裁剪到画框内）或只含边框的 SVG：按 (颜色, 像素, 细节档) 缓存成画布
  function chromeSvgs(o) {
    const S = new Svg(o);
    drawBackground(S, null, o);
    drawFrame(S, o);
    const w = o.frame ? S.win : 0;
    const head = `<svg xmlns="http://www.w3.org/2000/svg" width="${o.px}" height="${o.px}" viewBox="0 0 256 256"><defs>${winClipDef(S, w)}${S.defs.join('')}</defs>`;
    return { bg: head + `<g clip-path="url(#${S.id('win')})">${S.bg}</g></svg>`, frame: o.frame ? head + S.frame + '</svg>' : null, w };
  }

  // ======================================================== SVG 子集 → Canvas ==
  // 头像只用到 SVG 的一个小子集（path / ellipse / circle / rect / g，填充、描边、不透明度、
  // 裁剪、简单变换）。直接用 Path2D 画到画布上，省去浏览器为每张图建 SVG 文档的开销（快数倍）。
  const TAG_RE = /<(\/?)([a-zA-Z]+)([^>]*?)(\/?)>/g;
  const ATTR_RE = /([\w:-]+)="([^"]*)"/g;
  function attrsOf(s) {
    const a = {};
    if (!s) return a;
    ATTR_RE.lastIndex = 0;
    let m;
    while ((m = ATTR_RE.exec(s))) a[m[1]] = m[2];
    return a;
  }
  function applyTransform(ctx, tr) {
    const re = /(matrix|translate|scale|rotate)\(([^)]*)\)/g;
    let m;
    while ((m = re.exec(tr))) {
      const v = m[2].split(/[\s,]+/).filter(Boolean).map(Number);
      if (m[1] === 'matrix') ctx.transform(v[0], v[1], v[2], v[3], v[4], v[5]);
      else if (m[1] === 'translate') ctx.translate(v[0], v[1] || 0);
      else if (m[1] === 'scale') ctx.scale(v[0], v[1] == null ? v[0] : v[1]);
      else if (m[1] === 'rotate') {
        const r = v[0] * Math.PI / 180;
        if (v.length >= 3) { ctx.translate(v[1], v[2]); ctx.rotate(r); ctx.translate(-v[1], -v[2]); } else ctx.rotate(r);
      }
    }
  }
  function shapePath(tag, a) {
    let p = null;
    if (tag === 'path') p = new Path2D(a.d);
    else if (tag === 'ellipse') { p = new Path2D(); p.ellipse(+a.cx, +a.cy, Math.max(0, +a.rx), Math.max(0, +a.ry), 0, 0, Math.PI * 2); }
    else if (tag === 'circle') { p = new Path2D(); p.arc(+a.cx, +a.cy, Math.max(0, +a.r), 0, Math.PI * 2); }
    else if (tag === 'rect') {
      p = new Path2D();
      const x = +a.x || 0, y = +a.y || 0, w = +a.width, h = +a.height, r = +a.rx || 0;
      if (r > 0 && p.roundRect) p.roundRect(x, y, w, h, r); else p.rect(x, y, w, h);
    }
    return p;
  }
  // 解析 defs 里的 <clipPath id><path|rect/></clipPath>
  function clipMap(defs) {
    const map = new Map();
    const re = /<clipPath id="([^"]+)">(.*?)<\/clipPath>/g;
    let m;
    while ((m = re.exec(defs))) map.set(m[1], m[2]);
    return { get(id) {
      let v = map.get(id);
      if (typeof v === 'string') {
        const t = /<([a-zA-Z]+)([^>]*?)\/?>/.exec(v);
        v = t ? shapePath(t[1], attrsOf(t[2])) : null;
        map.set(id, v);
      }
      return v || null;
    } };
  }
  function paintMarkup(ctx, markup, clips) {
    const stack = [];
    let st = { fill: '#000', stroke: 'none', sw: 1, alpha: 1, lj: 'miter', lc: 'butt' };
    TAG_RE.lastIndex = 0;
    let m;
    while ((m = TAG_RE.exec(markup))) {
      const close = m[1], tag = m[2];
      if (tag === 'g') {
        if (close) { ctx.restore(); st = stack.pop() || st; continue; }
        const a = attrsOf(m[3]);
        stack.push(st);
        st = Object.assign({}, st);
        ctx.save();
        if (a.transform) applyTransform(ctx, a.transform);
        if (a['clip-path']) {
          const id = a['clip-path'].slice(5, -1);
          const cp = clips.get(id);
          if (cp) ctx.clip(cp);
        }
        if (a.fill) st.fill = a.fill;
        if (a.stroke) st.stroke = a.stroke;
        if (a['stroke-width']) st.sw = +a['stroke-width'];
        if (a['stroke-linejoin']) st.lj = a['stroke-linejoin'];
        if (a['stroke-linecap']) st.lc = a['stroke-linecap'];
        if (a.opacity) st.alpha *= +a.opacity;
        if (m[4]) { ctx.restore(); st = stack.pop(); } // <g/>
        continue;
      }
      if (close || (tag !== 'path' && tag !== 'ellipse' && tag !== 'circle' && tag !== 'rect')) continue;
      const a = attrsOf(m[3]);
      const p = shapePath(tag, a);
      if (!p) continue;
      const tr = a.transform;
      if (tr) { ctx.save(); applyTransform(ctx, tr); }
      const alpha = st.alpha * (a.opacity ? +a.opacity : 1);
      const fill = a.fill || st.fill;
      if (fill !== 'none' && fill[0] !== 'u') {
        ctx.globalAlpha = alpha * (a['fill-opacity'] ? +a['fill-opacity'] : 1);
        ctx.fillStyle = fill;
        ctx.fill(p, a['fill-rule'] === 'evenodd' ? 'evenodd' : 'nonzero');
      }
      const stroke = a.stroke || st.stroke;
      if (stroke !== 'none' && stroke[0] !== 'u') {
        ctx.globalAlpha = alpha * (a['stroke-opacity'] ? +a['stroke-opacity'] : 1);
        ctx.strokeStyle = stroke;
        ctx.lineWidth = a['stroke-width'] ? +a['stroke-width'] : st.sw;
        ctx.lineJoin = a['stroke-linejoin'] || st.lj;
        ctx.lineCap = a['stroke-linecap'] || st.lc;
        const da = a['stroke-dasharray'];
        if (da) ctx.setLineDash(da.split(/[\s,]+/).map(Number));
        ctx.stroke(p);
        if (da) ctx.setLineDash([]);
      }
      if (tr) ctx.restore();
    }
    while (stack.length) { ctx.restore(); stack.pop(); }
    ctx.globalAlpha = 1;
  }

  // ======================================================== 缓存与栅格化 ==
  const DPR = () => Math.min((typeof window !== 'undefined' && window.devicePixelRatio) || 1, 2);
  function factionColorOf(gen) {
    if (gen && gen.color) return gen.color;
    try {
      if (gen && SG.G && SG.G.factions && gen.faction != null && gen.faction >= 0) {
        const f = SG.G.factions[gen.faction];
        if (f && f.color) return f.color;
      }
    } catch (e) { /* 忽略 */ }
    return '#8a8a92';
  }
  function normOpts(gen, opts) {
    opts = opts || {};
    const size = Math.max(16, Math.round(opts.size || 96));
    const px = Math.max(16, Math.round(size * (opts.scale || DPR())));
    const color = normColor(opts.color || factionColorOf(gen), '#8a8a92');
    const lod = px < 90 ? 0 : px < 170 ? 1 : 2;
    return {
      size, px, color, lod,
      frame: opts.frame !== false, flip: !!opts.flip, mood: opts.mood || 'neutral',
      lw: lod === 0 ? 3.4 : lod === 1 ? 2.9 : 2.6,
    };
  }
  const urlCache = new Map();     // key → url（栅格化后为 blob: PNG；之前为 SVG dataURL）
  // genMs：生成 SVG 并录制绘制指令；encMs：栅格化 + PNG 编码（画布在读出时才真正光栅化）
  const stats = { built: 0, rastered: 0, maxSliceMs: 0, totalMs: 0, genMs: 0, encMs: 0 };
  function keyOf(sp, o) { return sp.key + '|' + o.px + '|' + o.color + '|' + (o.frame ? 1 : 0) + (o.flip ? 1 : 0) + '|' + o.mood; }
  function prep(gen, opts) {
    const sp = resolveSpec(gen);
    const o = normOpts(typeof gen === 'string' ? findGen(gen) : gen, opts);
    return { sp, o, key: keyOf(sp, o) };
  }
  function svgUrl(s) { return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(s); }
  const isRaster = u => u.startsWith('data:image/png') || u.startsWith('blob:');
  const canRaster = () => typeof document !== 'undefined' && typeof Path2D !== 'undefined' && typeof Image !== 'undefined';

  // 背景 / 边框画布（浏览器解码 SVG，含渐变）：key → Promise<{bg, frame}>
  const chromeCache = new Map();
  function chrome(o) {
    const k = o.color + '|' + o.px + '|' + o.lod + '|' + (o.frame ? 1 : 0);
    let p = chromeCache.get(k);
    if (p) return p;
    const sv = chromeSvgs(o);
    const toCanvas = s => new Promise((ok, fail) => {
      if (!s) { ok(null); return; }
      const img = new Image();
      img.onload = () => {
        const c = document.createElement('canvas');
        c.width = c.height = o.px;
        c.getContext('2d', { willReadFrequently: true }).drawImage(img, 0, 0, o.px, o.px);
        ok(c);
      };
      img.onerror = () => fail(new Error('portrait chrome'));
      img.src = svgUrl(s);
    });
    p = Promise.all([toCanvas(sv.bg), toCanvas(sv.frame)]).then(([bg, frame]) => ({ bg, frame, w: sv.w }));
    chromeCache.set(k, p);
    if (chromeCache.size > 64) chromeCache.delete(chromeCache.keys().next().value);
    return p;
  }
  // 把一张头像画到 ctx（已就绪的 chrome）
  function paintPortrait(ctx, sp, o, ch) {
    const { S, fig, w } = buildParts(sp, o);
    stats.built++;
    const k = o.px / 256;
    if (ch.bg) ctx.drawImage(ch.bg, 0, 0);
    ctx.save();
    ctx.scale(k, k);
    ctx.beginPath();
    ctx.rect(w, w, 256 - 2 * w, 256 - 2 * w);
    ctx.clip();
    paintMarkup(ctx, fig, clipMap(S.defs.join('')));
    ctx.restore();
    if (ch.frame) ctx.drawImage(ch.frame, 0, 0);
  }

  // 复用的 CPU 画布（按像素尺寸）：避免 GPU 回读（GPU 画布上编码 PNG 要慢数十倍），也省去反复分配
  const scratchMap = new Map();
  function scratch(px) {
    let e = scratchMap.get(px);
    if (!e) {
      const c = document.createElement('canvas');
      c.width = c.height = px;
      e = { c, ctx: c.getContext('2d', { willReadFrequently: true }) };
      scratchMap.set(px, e);
      if (scratchMap.size > 6) scratchMap.delete(scratchMap.keys().next().value);
    }
    return e;
  }
  // 任务队列：时间片内 生成 → 画到 CPU 画布 → 编码 PNG（dataURL）。
  // 单片预算 SLICE 毫秒（超出预算前最多再完成一张），片与片之间让出主线程。
  const queue = [];
  const pending = new Map(); // key → Promise<url>
  let pumping = false;
  const SLICE = 10;
  function enqueue(gen, opts) {
    return new Promise(res => { queue.push({ gen, opts, res }); pump(); });
  }
  let chan = null;
  const chanCbs = [];
  function nextTick(f) {
    if (typeof MessageChannel === 'undefined' || typeof document === 'undefined') { setTimeout(f, 0); return; }
    if (!chan) {
      chan = new MessageChannel();
      chan.port1.onmessage = () => { const g = chanCbs.shift(); if (g) g(); };
    }
    chanCbs.push(f);
    chan.port2.postMessage(0);
  }
  function pump() {
    if (pumping || (!queue.length && !painted)) return;
    pumping = true;
    nextTick(slice);
  }
  const URL_CACHE_MAX = 1500;   // 约 1500 张 × 数十 KB；超出时丢掉最早的
  function remember(key, url) {
    urlCache.delete(key);
    urlCache.set(key, url);
    if (urlCache.size > URL_CACHE_MAX) {
      let n = 100;
      for (const k of urlCache.keys()) { if (n-- <= 0) break; urlCache.delete(k); }
    }
  }
  function finish(key, url, ver) {
    pending.delete(key);
    if (ver === dataVersion) remember(key, url); // 期间 clearCache 过则不回填旧图
    return url;
  }
  // 两步走：一步“生成并录制绘制指令”，一步“栅格化 + PNG 编码”，每步之间检查时间预算，
  // 使不可分割的最小工作单元减半（单片最坏情况更短）。
  let painted = null; // { c, key, ver, done(url) } —— 已画好、待编码的一张
  function encodePainted() {
    const pj = painted;
    painted = null;
    const te = performance.now();
    let url;
    try { url = pj.c.toDataURL('image/png'); stats.rastered++; } catch (e) { console.warn('portrait', e); url = pj.fallback(); }
    stats.encMs += performance.now() - te;
    pj.done(finish(pj.key, url, pj.ver));
  }
  function slice() {
    const t0 = performance.now();
    let waitChrome = null, nJobs = 0, lastPx = 0;
    while ((painted || queue.length) && performance.now() - t0 < SLICE) {
      if (painted) { encodePainted(); continue; }
      const job = queue[0];
      let r;
      try { r = prep(job.gen, job.opts); } catch (e) { queue.shift(); console.warn('portrait', e); job.res(''); continue; }
      const u = urlCache.get(r.key);
      if (u && isRaster(u)) { queue.shift(); job.res(u); continue; }
      const pp = pending.get(r.key);
      if (pp) { queue.shift(); pp.then(job.res); continue; }
      if (!canRaster()) { queue.shift(); const su = svgUrl(buildSvg(r.sp, r.o)); remember(r.key, su); job.res(su); continue; }
      // 背景 / 边框尚未就绪：等它解码完再继续（不占主线程）
      const chP = chrome(r.o);
      if (!chP.ready) { waitChrome = chP; break; }
      queue.shift();
      nJobs++; lastPx = r.o.px;
      const ver = dataVersion;
      let done;
      const p = new Promise(ok => { done = ok; });
      pending.set(r.key, p);
      p.then(job.res);
      const fallback = () => svgUrl(buildSvg(r.sp, r.o));
      try {
        const { c, ctx } = scratch(r.o.px);
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, r.o.px, r.o.px);
        const tg = performance.now();
        paintPortrait(ctx, r.sp, r.o, chP.value);
        stats.genMs += performance.now() - tg;
        // 同步编码放到下一步（toBlob 依赖空闲时段，主线程忙时会被集中强制执行，造成长卡顿）
        painted = { c, key: r.key, ver, done, fallback };
      } catch (e) {
        console.warn('portrait', e);
        done(finish(r.key, fallback(), ver));
      }
    }
    const dt = performance.now() - t0;
    stats.maxSliceMs = Math.max(stats.maxSliceMs, dt);
    stats.totalMs += dt;
    if (dt > 40) { stats.slow = stats.slow || []; if (stats.slow.length < 10) stats.slow.push([Math.round(dt), nJobs, lastPx]); }
    pumping = false;
    if (waitChrome && !painted) waitChrome.then(v => { waitChrome.ready = true; waitChrome.value = v; pump(); }, () => { waitChrome.ready = true; waitChrome.value = { bg: null, frame: null, w: 0 }; pump(); });
    else pump();
  }

  const Portrait = {
    url(gen, opts) {
      const r = prep(gen, opts);
      const u = urlCache.get(r.key);
      if (u) return u;
      const su = svgUrl(buildSvg(r.sp, r.o));
      stats.built++;
      remember(r.key, su);
      if (!(opts && opts.raster === false)) enqueue(gen, opts);
      return su;
    },
    // 已栅格化（或已有 URL）时返回 URL，否则 null（不触发生成）
    cached(gen, opts) { return urlCache.get(prep(gen, opts).key) || null; },
    el(gen, opts) {
      opts = opts || {};
      injectCss();
      const size = Math.round(opts.size || 96);
      const d = document.createElement('div');
      d.className = 'sg-portrait' + (opts.className ? ' ' + opts.className : '');
      d.style.width = d.style.height = size + 'px';
      const img = document.createElement('img');
      img.alt = (typeof gen === 'string' ? gen : gen && gen.name) || '';
      img.draggable = false;
      img.decoding = 'async';
      img.width = img.height = size;
      const r = prep(gen, opts);
      const u = urlCache.get(r.key);
      if (u) img.src = u;
      else {
        // 尚未生成：先放 SVG（矢量，立即可见），栅格化完成后换成 PNG
        img.src = Portrait.url(gen, opts);
        enqueue(gen, opts).then(url => { if (url && isRaster(url) && img.src !== url) img.src = url; });
      }
      d.appendChild(img);
      return d;
    },
    preload(gens, opts) {
      const list = (gens || []).map(g => enqueue(g, opts));
      return Promise.all(list).then(() => undefined);
    },
    spec(gen) { return resolveSpec(gen); },
    svg(gen, opts) { const r = prep(gen, opts); stats.built++; return buildSvg(r.sp, r.o); },
    canvas(gen, opts) {
      return enqueue(gen, opts).then(url => new Promise((ok, fail) => {
        const img = new Image();
        img.onload = () => {
          const px = normOpts(gen, opts).px;
          const c = document.createElement('canvas');
          c.width = c.height = px;
          c.getContext('2d').drawImage(img, 0, 0, px, px);
          ok(c);
        };
        img.onerror = () => fail(new Error('portrait canvas'));
        img.src = url;
      }));
    },
    clearCache(name) {
      specCache.clear();
      for (const [k, u] of urlCache) {
        if (name && !k.startsWith(name + '|')) continue;
        if (u.startsWith('blob:')) try { URL.revokeObjectURL(u); } catch (e) { /* 忽略 */ }
        urlCache.delete(k);
      }
      dataVersion++;
    },
    // ---- 集成辅助 ----
    // 按姓名找武将（当前游戏优先，其次剧本数据）；找不到返回 null
    find(name) {
      if (!name) return null;
      const g = findGen(String(name));
      return g && g.war != null ? g : (SG.PortraitData && SG.PortraitData.get(name) ? g : null);
    },
    // 内嵌用的 HTML 片段（给 UI.choose 的 label 等 HTML 字符串用）
    html(gen, opts) {
      opts = opts || {};
      injectCss();
      const size = Math.round(opts.size || 32);
      const o = Object.assign({ size }, opts);
      const r = prep(gen, o);
      const url = urlCache.get(r.key) || Portrait.url(gen, o);
      const alt = SG.esc ? SG.esc((typeof gen === 'string' ? gen : gen && gen.name) || '') : '';
      return `<span class="sg-portrait sg-portrait-inline${opts.className ? ' ' + opts.className : ''}" style="width:${size}px;height:${size}px"><img src="${url}" alt="${alt}" width="${size}" height="${size}" draggable="false"></span>`;
    },
    // 预生成当前游戏所有在世武将（势力色边框），sizes 为 CSS 像素列表
    preloadState(state, sizes) {
      state = state || SG.G;
      if (!state || !state.generals) return Promise.resolve();
      const gens = state.generals.filter(g => !g.dead);
      const jobs = [];
      for (const size of sizes || [44, 96]) for (const g of gens) jobs.push(enqueue(g, { size, color: factionColorOf(g) }));
      return Promise.all(jobs).then(() => undefined);
    },
    cultures: Object.keys(CULT),
    stats,
    _internal: { hashStr, Rng, resolveSpec, buildSvg, buildParts, normOpts, paintMarkup, clipMap, chrome, paintPortrait, CULT },
  };
  let cssDone = false;
  function injectCss() {
    if (cssDone || typeof document === 'undefined') return;
    cssDone = true;
    const st = document.createElement('style');
    st.id = 'sg-portrait-css';
    st.textContent = '.sg-portrait{display:inline-block;position:relative;flex:none;line-height:0;vertical-align:middle}' +
      '.sg-portrait>img{width:100%;height:100%;display:block;-webkit-user-drag:none;user-select:none;pointer-events:none}' +
      '.sg-portrait.is-dead>img{filter:grayscale(1) brightness(.7)}' +
      '.sg-portrait-inline{margin-right:.45em;border-radius:2px;overflow:hidden}' +
      // 集成位置的尺寸（与 style.css 的徽章尺寸、断点一致）：对话说话人、城池武将列表、战场部队卡
      '.sg-say-speaker>.sg-portrait{width:4.8rem!important;height:4.8rem!important}' +
      '.sg-genrow>.sg-portrait{width:2.5rem!important;height:2.5rem!important}' +
      '.sg-battle-card>.sg-portrait{float:left;width:4.4rem!important;height:4.4rem!important;margin:.15rem .7rem .2rem 0}' +
      '@media (max-height:540px){.sg-say-speaker>.sg-portrait{width:3.9rem!important;height:3.9rem!important}' +
      '.sg-battle-card>.sg-portrait{width:3.4rem!important;height:3.4rem!important}}' +
      '@media (max-width:700px) and (min-height:541px){.sg-say-speaker>.sg-portrait{width:3rem!important;height:3rem!important}}';
    if (!document.getElementById('sg-portrait-css')) document.head.appendChild(st);
  }
  SG.Portrait = Portrait;
})();
