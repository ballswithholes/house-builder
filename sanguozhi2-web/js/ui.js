'use strict';
/* ==========================================================================
   界面工具（移植自 UI/UIKit.cs）：图层、面板、按钮、徽章、进度条、
   对话 say / 列表选择 choose / 多选 chooseMany / 数值 pickNumber / 确认 confirm、
   提示 toast / 横幅 banner、跟随三维坐标的标签 follow 与飘字 floatText。

   - 所有对话框返回 Promise（替代 C# 协程 + Ref<T>）。
   - 键盘：Enter / Space 确认或继续，Escape 取消，方向键在列表中移动。
   - 尺寸参数（choose 的 width、medal 的 size、follow 的 offsetY）沿用 C# 中
     Unity 参考画布（1600×900）的像素值，自动按界面缩放换算（1rem = 20 参考像素）。
   - 样式见 css/style.css，类名说明见 UI-CLASSES.md。
   第三版 §5「头像无处不在」（非战斗阶段）：
     UI.item(label, right, enabled, desc, { avatar: 武将或姓名, avatarOpts })  choose / chooseMany 行首显示头像
     UI.say(text, speaker, color, { mood, sub })   speaker 可为姓名或武将对象；sub 为姓名下的小字
     UI.confirm(text, yes, no, { body })            标题与按钮之间的说明（如头像名片）
     UI.portrait(who, opts) → 头像元素   UI.face(who, opts) → 行内小头像 HTML   UI.genCard(who, opts) → 头像名片 HTML
     UI.faces(html, { gens, skip, factions, max })  在文字里提到的武将（势力名 → 君主）前加行内小头像
     UI.facePx(rem) / UI.faceSizes() / UI.FACE_REM  头像像素尺寸（预生成用）
     打字机效果把行内头像当作一个字，打到时才出现。
   第三版 §4 成长界面：
     UI.notice(title, bodyHtml, { ok, width, className }) → Promise   只有一个按钮的说明框（结构同 confirm：.sg-confirm-btns）
     UI.lvTag(gen) → 「Lv5」小标签 HTML    UI.expBar(pct, cls?) → 经验进度条 HTML（.sg-expbar > i，--v = 0..1）
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const UI = {};
  const L = { root: null, labels: null, screens: null, modals: null, toasts: null, toastStack: null };
  const modalStack = [];
  const followers = new Set();
  let inited = false;
  let uiScale = 0.8;

  // 配色（与 UIKit 相同）
  UI.colors = {
    ink: 'rgba(18,18,26,.9)', ink2: 'rgba(33,31,41,.95)',
    gold: '#f3c969', goldDim: 'rgba(243,201,105,.35)',
    text: '#f5eddb', muted: '#ada392', cinnabar: '#c8382c',
    good: '#73d98c', bad: '#ff7361',
    side0: '#73bfff', side1: '#ff806b',
  };

  // ------------------------------------------------------------- 工具 --
  function sfxClick() { try { if (SG.Sfx && SG.Sfx.click) SG.Sfx.click(); } catch (e) { /* 无音频 */ } }

  // 参考像素 → rem
  function rem(px) { return (px / 20) + 'rem'; }

  // 任意颜色 → CSS 字符串（支持 '#rrggbb'、THREE.Color、{r,g,b}（0..1）、[r,g,b]（0..1））
  function cssColor(c) {
    if (c === null || c === undefined) return '';
    if (typeof c === 'string') return c;
    if (typeof c === 'number') return '#' + (c >>> 0).toString(16).padStart(6, '0');
    if (c.isColor && typeof c.getHexString === 'function') return '#' + c.getHexString();
    if (Array.isArray(c)) return SG.rgbToHex(c[0], c[1], c[2]);
    if (typeof c.r === 'number') {
      if (typeof c.a === 'number' && c.a < 1) return `rgba(${Math.round(M.clamp01(c.r) * 255)},${Math.round(M.clamp01(c.g) * 255)},${Math.round(M.clamp01(c.b) * 255)},${c.a})`;
      return SG.rgbToHex(c.r, c.g, c.b);
    }
    return String(c);
  }
  UI.cssColor = cssColor;
  UI.esc = SG.esc;

  function updateScale() {
    try {
      const fs = parseFloat(getComputedStyle(document.documentElement).fontSize);
      if (fs > 0) uiScale = fs / 20;
    } catch (e) { /* 保持旧值 */ }
  }
  // Unity CanvasScaler 的等效缩放：1 参考像素 = scale() CSS 像素
  UI.scale = function () { return uiScale; };

  // ------------------------------------------------------------- 初始化 --
  UI.init = function () {
    if (inited) return UI;
    inited = true;
    let root = document.getElementById('ui');
    if (!root) {
      root = document.createElement('div');
      root.id = 'ui';
      document.body.appendChild(root);
    }
    L.root = root;
    L.labels = el('div', 'sg-layer sg-labels');
    L.screens = el('div', 'sg-layer sg-screens');
    L.modals = el('div', 'sg-layer sg-modals');
    L.toasts = el('div', 'sg-layer sg-toasts');
    L.toastStack = el('div', 'sg-toast-stack');
    L.toasts.appendChild(L.toastStack);
    root.appendChild(L.labels);
    root.appendChild(L.screens);
    root.appendChild(L.modals);
    root.appendChild(L.toasts);
    UI.layers = { root, labels: L.labels, screens: L.screens, modals: L.modals, toasts: L.toasts };
    updateScale();
    window.addEventListener('resize', updateScale);
    window.addEventListener('orientationchange', () => setTimeout(updateScale, 120));
    window.addEventListener('keydown', onKeyDown, true);
    window.addEventListener('keyup', onKeyUp, true);
    // 记录最近一次输入方式：指针打开的模态关闭后不把焦点还给 HUD 按钮（否则 Space/Enter 会误触 动画 / 委任作战）
    // 画布的指针处理会阻止默认的焦点转移：点在 HUD 之外时，像浏览器原生行为那样让 HUD 按钮失去焦点
    window.addEventListener('pointerdown', e => {
      lastInputPointer = true;
      const a = document.activeElement, t = e.target;
      if (a && a !== document.body && a.closest && a.closest(HUD_SEL) && !(t && t.closest && t.closest(HUD_SEL))) a.blur();
    }, true);
    window.addEventListener('keydown', e => { if (e.key !== 'Shift' && e.key !== 'Control' && e.key !== 'Alt' && e.key !== 'Meta') lastInputPointer = false; }, true);
    return UI;
  };
  function ensure() { if (!inited) UI.init(); }
  let lastInputPointer = false;
  const HUD_SEL = '.sg-battle-actions, .sg-topbar-actions';

  // ---------------------------------------------------------- DOM 构建 --
  function el(tag, className, html) {
    const e = document.createElement(tag || 'div');
    if (className) e.className = className;
    if (html !== undefined && html !== null) e.innerHTML = String(html);
    return e;
  }
  UI.el = el;

  // 全屏容器（放在 screens 层；自身不拦截指针，子元素拦截）
  // 按名称取图层：'labels' | 'screens' | 'modals' | 'toasts'
  UI.layer = function (name) {
    ensure();
    return UI.layers[name] || null;
  };

  UI.screen = function (className) {
    ensure();
    const s = el('div', 'sg-screen' + (className ? ' ' + className : ''));
    L.screens.appendChild(s);
    return s;
  };

  UI.panel = function (parent, className) {
    const p = el('div', 'sg-panel' + (className ? ' ' + className : ''));
    if (parent) parent.appendChild(p);
    return p;
  };

  // ---------------------------------------------------------- 按钮（UI-V4 §4.1）--
  // SG.UI.button(parent, html, onClick, { kind, icon, label, why, badge, sound, hotkey, size, primary, small, className, title, ariaLabel, disabled })
  //   kind：'plaque' 漆牌（缺省）| 'seal' 印钮（= v3 primary）| 'paper' 纸签 | 'ghost' 空框 | 'icon' 铜钮 | 'tile' 指令印
  //   why：不可用的原因（aria-disabled + 锁印；点按时抖动、ui-deny、提示原因）；badge：红蜡角标；hotkey：悬停时显示键帽
  //   声音：按下时 ui-press-wood / -bronze / -paper（按 kind），印钮松开时 ui-stamp + 十滴朱墨；鼠标悬停 ui-hover
  //   btn.setLabel(html) · btn.setEnabled(bool | why) · btn.setBadge(v) · btn.pulse()
  const KIND_SOUND = { plaque: 'ui-press-wood', seal: 'ui-press-wood', ghost: 'ui-press-wood', paper: 'ui-press-paper', icon: 'ui-press-bronze', tile: 'ui-press-wood' };
  function uiSfx(name, vol) { try { if (SG.Sfx && name) SG.Sfx.play(name, vol === undefined ? 0.9 : vol); } catch (e) { /* 无音频 */ } }
  UI.sfx = uiSfx;
  function iconHtml(id, size, opts) { try { return SG.UIIcons ? SG.UIIcons.svg(id, size, opts) : ''; } catch (e) { return ''; } }
  UI.icon = iconHtml;
  UI.button = function (parent, html, onClick, opts) {
    opts = opts || {};
    const isClose = !!(opts.className && /\bsg-close\b/.test(opts.className));
    let kind = opts.kind || (isClose ? 'icon' : opts.primary ? 'seal' : 'plaque');
    const cls = 'sg-btn k-' + kind + (kind === 'seal' ? ' primary' : '') + (opts.small || opts.size === 'small' ? ' small' : '') +
      (opts.size === 'big' ? ' big' : '') + (isClose ? ' k-close' : '') + (opts.className ? ' ' + opts.className : '');
    const b = el('button', cls);
    b.type = 'button';
    b._kind = kind;
    if (kind === 'icon') {
      const ic = opts.icon || (isClose ? 'x' : null);
      const label = opts.label || (isClose ? '收' : '');
      b.innerHTML = ic ? '<span class="sg-ib-face">' + iconHtml(ic) + '</span>' + (label ? '<span class="sg-ib-lbl">' + SG.esc(label) + '</span>' : '')
        : (html === undefined || html === null ? '' : String(html));
      if (!opts.ariaLabel && label) b.setAttribute('aria-label', label);
    } else if (kind === 'tile') {
      b.innerHTML = (opts.token ? '<i class="sg-tile-pip"></i>' : '') + iconHtml(opts.icon || 'help') + '<span class="sg-tile-lb">' + (html === undefined || html === null ? '' : String(html)) + '</span>';
      b._lbl = b.querySelector('.sg-tile-lb');
    } else if (opts.icon) {
      b.innerHTML = iconHtml(opts.icon) + '<span class="sg-btn-l">' + (html === undefined || html === null ? '' : String(html)) + '</span>';
      b._lbl = b.querySelector('.sg-btn-l');
    } else if (html !== undefined && html !== null) b.innerHTML = String(html);
    if (opts.hotkey && !SG.isTouch) b.dataset.key = opts.hotkey;
    if (opts.title) b.title = opts.title;
    if (opts.ariaLabel) b.setAttribute('aria-label', opts.ariaLabel);
    if (opts.disabled) b.disabled = true;
    const pressSnd = opts.sound === 'none' ? null : opts.sound ? ({ wood: 'ui-press-wood', bronze: 'ui-press-bronze', paper: 'ui-press-paper', stamp: 'ui-press-wood' }[opts.sound] || opts.sound) : KIND_SOUND[kind];
    // 按下：下沉 + 声音（按下即响，比 click 灵敏）；松开：回弹（CSS）
    b.addEventListener('pointerdown', e => {
      if (b.disabled || (e.button !== undefined && e.button !== 0)) return;
      b.classList.add('is-press');
      b.style.setProperty('--px', (e.offsetX || 0) + 'px'); b.style.setProperty('--py', (e.offsetY || 0) + 'px');
      if (!b._why && pressSnd) uiSfx(pressSnd);
    });
    const unpress = () => b.classList.remove('is-press');
    b.addEventListener('pointerup', unpress); b.addEventListener('pointerleave', unpress); b.addEventListener('pointercancel', unpress);
    b.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse' && !b.disabled) uiSfx('ui-hover', 0.7); });
    if (onClick) {
      b.addEventListener('click', e => {
        if (b.disabled) return;
        if (b._why) {
          b.classList.remove('sg-cmd-hint');
          UI.deny(b, (b.dataset.cmd ? '<span class="sg-cmd-name">' + SG.esc(b.dataset.cmd) + '</span>' : '') + b._why);
          return;
        }
        // 鼠标 / 触摸点击（detail>0）后，模态层之外的按钮（战斗 HUD、顶栏）不保留键盘焦点，
        // 否则之后的 Space/Enter（或长按 Enter 的自动重复）会再次触发它
        if (e.detail > 0 && !(L.modals && L.modals.contains(b))) b.blur();
        if (e.detail === 0 && pressSnd) uiSfx(pressSnd);       // 键盘触发
        if (kind === 'seal' && opts.sound !== 'none') { uiSfx('ui-stamp'); inkBurst(b, e); }
        else if (kind === 'tile') ringBurst(b);
        onClick(e);
      });
    }
    b.setLabel = function (h) { if (b._lbl) b._lbl.innerHTML = String(h); else b.innerHTML = String(h); return b; };
    b.setEnabled = function (on) {
      if (typeof on === 'string' && on) {
        b.disabled = false; b._why = on; b.dataset.why = on;
        b.setAttribute('aria-disabled', 'true'); b.setAttribute('aria-description', on.replace(/<[^>]*>/g, ''));
        b.classList.add('is-locked');
      } else {
        b._why = null; delete b.dataset.why; b.removeAttribute('aria-disabled'); b.removeAttribute('aria-description');
        b.classList.remove('is-locked');
        b.disabled = !on;
      }
      return b;
    };
    b.setBadge = function (v) { UI.badge(b, v); return b; };
    b.pulse = function () { UI.pulse(b, 'is-pulse'); return b; };
    if (opts.why) b.setEnabled(opts.why);
    if (opts.badge !== undefined && opts.badge !== null && opts.badge !== 0) UI.badge(b, opts.badge);
    if (parent) parent.appendChild(b);
    return b;
  };
  // 不可用时的反馈：抖动 0.38 秒、ui-deny、提示原因（同一时间只留一条）
  let denyToast = null;
  UI.deny = function (target, why) {
    if (target) UI.pulse(target, 'sg-deny');
    uiSfx('ui-deny');
    if (!why) return;
    if (denyToast && denyToast.parentNode) denyToast.parentNode.removeChild(denyToast);
    denyToast = UI.toast(why, { seconds: 3.4, seal: '锁', kind: 'deny' });
  };
  // 红蜡角标：v 为数字或单字；0 / null / '' 移除；变大时跳两下
  UI.badge = function (target, v) {
    if (!target) return null;
    let bd = target.querySelector(':scope > .sg-badge');
    if (v === undefined || v === null || v === 0 || v === '' || v === false) { if (bd) bd.remove(); return null; }
    if (!bd) { bd = el('span', 'sg-badge'); bd.setAttribute('aria-hidden', 'true'); target.appendChild(bd); }
    const old = bd.textContent;
    bd.textContent = String(v);
    if (old !== String(v) && (!(+old >= 0) || +v > +old)) { UI.pulse(bd, 'is-pulse'); if (old) uiSfx('ui-badge', 0.7); }
    return bd;
  };
  // 印钮盖下时的朱墨点（10 滴，300 ms，池化，减少动态时不放）
  const inkPool = [];
  function motionOff() { return !!(SG.Motion && (SG.Motion.reduced || SG.Motion.level === 'off')); }
  function inkBurst(btn, e) {
    if (motionOff() || !btn.animate) return;
    ensure();
    const r = btn.getBoundingClientRect();
    const x = e && e.clientX ? e.clientX : r.left + r.width / 2, y = e && e.clientY ? e.clientY : r.top + r.height / 2;
    for (let i = 0; i < 10; i++) {
      let d = inkPool.pop();
      if (!d) d = el('i', 'sg-ink-drop');
      d.style.left = x + 'px'; d.style.top = y + 'px';
      L.toasts.appendChild(d);
      const a = (i / 10) * Math.PI * 2 + Math.random() * 0.5, dist = 18 + Math.random() * 26, s = 0.5 + Math.random() * 0.8;
      const an = d.animate([{ transform: 'translate(-50%,-50%) scale(' + s + ')', opacity: 0.95 },
        { transform: 'translate(calc(-50% + ' + (Math.cos(a) * dist).toFixed(1) + 'px), calc(-50% + ' + (Math.sin(a) * dist).toFixed(1) + 'px)) scale(' + (s * 0.4).toFixed(2) + ')', opacity: 0 }],
      { duration: 300, easing: 'cubic-bezier(.2,.8,.2,1)' });
      an.onfinish = () => { d.remove(); if (inkPool.length < 40) inkPool.push(d); };
    }
  }
  function ringBurst(btn) {
    if (motionOff()) return;
    const ring = el('i', 'sg-tile-ring');
    btn.appendChild(ring);
    setTimeout(() => ring.remove(), 420);
  }

  // 圆形徽章（武将头像位置的单字）。size 为参考像素（C# 中的数值）
  UI.medal = function (glyph, color, size) {
    if (size === undefined || size === null) size = 96;
    const m = el('div', 'sg-medal');
    m.style.setProperty('--c', cssColor(color || UI.colors.cinnabar));
    m.style.setProperty('--size', rem(size));
    const s = document.createElement('span');
    s.textContent = String(glyph || '').charAt(0);
    m.appendChild(s);
    return m;
  };

  // 进度条：返回元素，带 setValue(v) / setColor(c)
  UI.bar = function (value01, color) {
    const b = el('div', 'sg-bar');
    const f = el('i', 'sg-bar-fill');
    b.appendChild(f);
    b.fill = f;
    b.setValue = function (v) { b.style.setProperty('--v', String(M.clamp01(+v || 0))); return b; };
    b.setColor = function (c) { b.style.setProperty('--c', cssColor(c)); return b; };
    b.setColor(color || UI.colors.gold);
    b.setValue(value01 === undefined ? 1 : value01);
    return b;
  };

  // 列表条目。opts（可选）：{ avatar: 武将对象或姓名（行首显示头像）, avatarOpts: 传给 SG.Portrait.el 的选项（color / mood / className） }
  //   也可以直接给返回的对象设 .avatar
  UI.item = function (label, right, enabled, desc, opts) {
    const it = { label, right: right === undefined ? null : right, enabled: enabled === undefined ? true : !!enabled, desc: desc === undefined ? null : desc, selected: false };
    if (opts && opts.avatar) it.avatar = opts.avatar;
    if (opts && opts.avatarOpts) it.avatarOpts = opts.avatarOpts;
    return it;
  };

  // ------------------------------------------------------------- 头像 --
  // 非战斗阶段所有提到武将的地方都显示头像（DESIGN-V3 §5）。尺寸以 rem 计，换算为当前像素后交给 SG.Portrait
  // （按像素尺寸缓存；game.js 进入战略画面前按 UI.faceSizes() 预生成）。
  const FACE_REM = { item: 2.5, say: 4.8, inline: 1.9, card: 4.4 };
  UI.FACE_REM = FACE_REM;
  // rem → 当前 CSS 像素（取整）
  UI.facePx = function (r) { return Math.max(16, Math.round(r * uiScale * 20)); };
  // 需要预生成的头像尺寸（列表行 / 城池面板、对话说话人）
  UI.faceSizes = function () { return [UI.facePx(FACE_REM.item), UI.facePx(FACE_REM.say)]; };
  function genOf(x) {
    if (!x) return null;
    if (typeof x === 'object') return x;
    const P = SG.Portrait;
    return (P && P.find && P.find(String(x))) || { name: String(x) };
  }
  // 头像元素（无头像模块时退回姓氏徽章）。opts：{ size（CSS 像素）, color, mood, className, medalColor }
  UI.portrait = function (who, opts) {
    opts = opts || {};
    const gen = genOf(who);
    const size = opts.size || UI.facePx(FACE_REM.item);
    const P = SG.Portrait;
    if (P && gen) {
      try {
        const o = { size };
        if (opts.color) o.color = opts.color;
        if (opts.mood) o.mood = opts.mood;
        if (opts.className) o.className = opts.className;
        return P.el(gen, o);
      } catch (e) { /* 退回徽章 */ }
    }
    return UI.medal(gen ? gen.name : '?', opts.medalColor || UI.colors.cinnabar, size / Math.max(0.01, uiScale));
  };
  function avatarEl(it) {
    const wrap = el('span', 'sg-item-face');
    wrap.setAttribute('aria-hidden', 'true');
    wrap.appendChild(UI.portrait(it.avatar, Object.assign({ size: UI.facePx(FACE_REM.item) }, it.avatarOpts || {})));
    return wrap;
  }
  // 行内小头像（HTML 字符串）：放在文字里的武将姓名前。opts：{ size, mood, color }
  UI.face = function (who, opts) {
    const P = SG.Portrait;
    const gen = genOf(who);
    if (!P || !gen) return '';
    opts = opts || {};
    try {
      const o = { size: opts.size || UI.facePx(FACE_REM.inline), className: 'sg-face' };
      if (opts.mood) o.mood = opts.mood;
      if (opts.color) o.color = opts.color;
      return P.html(gen, o);
    } catch (e) { return ''; }
  };
  // 头像名片（HTML 字符串）：头像 + 姓名 + 一行副标题 + 一行数据。俘虏处置、登用确认等对话框的说明区使用。
  // opts：{ sub: 副标题 HTML, stats: 数据 HTML（缺省 武 智 政）, mood }
  UI.genCard = function (who, opts) {
    const gen = genOf(who);
    if (!gen) return '';
    opts = opts || {};
    const P = SG.Portrait;
    let face = '';
    if (P) { try { face = P.html(gen, { size: UI.facePx(FACE_REM.card), className: 'sg-gencard-face', mood: opts.mood }); } catch (e) { face = ''; } }
    const stats = opts.stats !== undefined ? opts.stats
      : (gen.war !== undefined ? `武 <b>${gen.war}</b>　智 <b>${gen.intel}</b>　政 <b>${gen.pol}</b>` : '');
    return '<span class="sg-gencard">' + face + '<span class="sg-gencard-text"><span class="sg-gencard-name">' + SG.esc(gen.name || '') + '</span>' +
      (opts.sub ? '<span class="sg-gencard-sub">' + opts.sub + '</span>' : '') +
      (stats ? '<span class="sg-gencard-stats">' + stats + '</span>' : '') + '</span></span>';
  };

  // 文字中提到的武将（以及势力名 → 其君主）前加行内小头像；每个名字只加第一次出现。
  // 只处理标签之外的文字。与城同名的势力（罗马、夫余、鄯善……）按上下文区分：名字后接「攻陷 / 占领 / 击退 / 军 / 势力 /
  // 继承 / 同意 / 拒绝 / 的」或前接「离开了」时视为势力（配君主头像），否则视为城名（「攻陷了罗马」「罗马无人防守」不配头像）。
  // opts：{ gens: 只给这些武将（或姓名）加头像, skip: 不加头像的姓名（如对话的说话人）, factions = true, max = 8 }
  let faceIdx = null;
  function faceIndex() {
    const g = SG.G;
    if (!g || !g.generals) return null;
    const key = g.generals.length + '|' + (g.factions ? g.factions.length : 0) + '|' + (g.cities ? g.cities.length : 0);
    if (faceIdx && faceIdx.g === g && faceIdx.key === key) return faceIdx;
    const map = new Map();
    for (const x of g.generals) if (x && x.name && x.name.length >= 2 && !map.has(x.name)) map.set(x.name, { gen: x });
    for (const c of g.cities || []) if (c && c.name && c.name.length >= 2 && !map.has(c.name)) map.set(c.name, { city: c });
    for (const f of g.factions || []) {
      if (!f || !f.name || f.name.length < 2) continue;
      const e = map.get(f.name);
      if (!e) map.set(f.name, { fac: f });
      else if (e.city && !e.fac) e.fac = f;   // 与城同名：保留两者，替换时看上下文
    }
    const names = Array.from(map.keys()).sort((a, b) => b.length - a.length);
    const re = names.length ? new RegExp(names.map(n => n.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'g') : null;
    faceIdx = { g, key, map, re };
    return faceIdx;
  }
  const FAC_AFTER = /^(攻陷|占领|击退|未能|军|势力|继承|同意|拒绝|的)/;
  const FAC_BEFORE = /离开了$/;
  UI.faces = function (html, opts) {
    if (html === null || html === undefined) return html;
    html = String(html);
    const idx = SG.Portrait ? faceIndex() : null;
    if (!idx || !idx.re) return html;
    opts = opts || {};
    const g = SG.G;
    const skip = new Set((opts.skip || []).map(x => (x && typeof x === 'object' ? x.name : String(x))));
    const only = opts.gens ? new Set(opts.gens.map(x => (x && typeof x === 'object' ? x.name : String(x)))) : null;
    const facs = opts.factions !== false;
    const max = opts.max === undefined ? 8 : opts.max;
    const used = new Set();
    let n = 0;
    return html.split(/(<[^>]*>|&[#\w]+;)/).map((seg, i) => {
      if (i % 2 === 1 || !seg) return seg;
      return seg.replace(idx.re, (m, off, str) => {
        const e = idx.map.get(m);
        if (!e || n >= max || skip.has(m) || used.has(m)) return m;
        if (e.city) {   // 城名；与城同名的势力只在上下文像势力时配君主头像
          const asFac = e.fac && (FAC_AFTER.test(str.slice(off + m.length, off + m.length + 2)) || FAC_BEFORE.test(str.slice(Math.max(0, off - 3), off)));
          if (!asFac) return m;
        }
        let gen = e.gen;
        if (e.fac) {
          if (!facs || only) return m;
          try { gen = g.ruler(e.fac.id); } catch (err) { gen = null; }
          if (!gen || used.has(gen.name) || skip.has(gen.name)) return m;
          used.add(gen.name);
        } else if (only && !only.has(m)) return m;
        used.add(m);
        n++;
        return UI.face(gen) + m;
      });
    }).join('');
  };

  // 重新触发一次 CSS 动画类（如单挑的 .sg-duel-vs.is-hit）
  UI.pulse = function (element, className) {
    if (!element) return;
    className = className || 'is-hit';
    element.classList.remove(className);
    void element.offsetWidth;
    element.classList.add(className);
  };

  // -------------------------------------------------------------- 模态 --
  // openModal(className, { width, dim, blockerClass }) → { blocker, panel, close(), onKey }
  function openModal(className, opts) {
    ensure();
    opts = opts || {};
    const blocker = el('div', 'sg-modal' + (opts.blockerClass ? ' ' + opts.blockerClass : '') + (opts.dim === false ? ' no-dim' : ''));
    blocker.tabIndex = -1;
    const panel = el('div', 'sg-panel sg-dialog' + (className ? ' ' + className : ''));
    panel.setAttribute('role', 'dialog');
    panel.setAttribute('aria-modal', 'true');
    if (opts.width) panel.style.setProperty('--w', rem(opts.width));
    if (opts.height) panel.style.minHeight = 'min(' + rem(opts.height) + ', 100%)';
    blocker.appendChild(panel);
    L.modals.appendChild(blocker);
    let prevFocus = document.activeElement;
    // 由指针操作打开时，不把焦点还给 HUD 按钮（见 UI.button）
    if (lastInputPointer && prevFocus && prevFocus.closest && prevFocus.closest(HUD_SEL)) prevFocus = null;
    const rec = {
      blocker, panel, onKey: null, closed: false,
      close() {
        if (rec.closed) return;
        rec.closed = true;
        const i = modalStack.indexOf(rec);
        if (i >= 0) modalStack.splice(i, 1);
        const hadFocus = blocker.contains(document.activeElement);
        blocker.classList.add('is-closing');
        setTimeout(() => { if (blocker.parentNode) blocker.parentNode.removeChild(blocker); }, 170);
        if (hadFocus) {
          try {
            if (prevFocus && prevFocus.isConnected && prevFocus !== document.body && !blocker.contains(prevFocus) && typeof prevFocus.focus === 'function') prevFocus.focus({ preventScroll: true });
            else if (document.activeElement && document.activeElement.blur) document.activeElement.blur();
          } catch (e) { /* 忽略 */ }
        }
      },
    };
    modalStack.push(rec);
    return rec;
  }
  UI.openModal = function (className, opts) { return openModal(className, opts); };

  UI.anyModal = function () { return modalStack.length > 0; };

  function focusEl(e) {
    if (!e) return;
    try { e.focus({ preventScroll: true }); } catch (err) { /* 忽略 */ }
  }

  // ------------------------------------------------------------- 键盘 --
  function normKey(e) {
    const k = e.key;
    if (k === ' ' || k === 'Spacebar') return 'Space';
    if (k === 'Esc') return 'Escape';
    if (k === 'Up') return 'ArrowUp';
    if (k === 'Down') return 'ArrowDown';
    if (k === 'Left') return 'ArrowLeft';
    if (k === 'Right') return 'ArrowRight';
    return k;
  }
  const NAV = new Set(['Enter', 'Space', 'Escape', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'PageUp', 'PageDown']);

  function onKeyDown(e) {
    const top = modalStack[modalStack.length - 1];
    if (!top) return;
    if (e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key === 'Tab') { trapTab(e, top.panel); return; }
    const k = normKey(e);
    const isNav = NAV.has(k);
    if ((k === 'Enter' || k === 'Space' || k === 'Escape') && e.repeat) { e.preventDefault(); e.stopPropagation(); return; }
    let handled = false;
    if (top.onKey) {
      try { handled = top.onKey(k, e) === true; } catch (err) { console.error(err); }
    }
    if (handled || isNav) { e.preventDefault(); e.stopPropagation(); }
  }
  function onKeyUp(e) {
    // 阻止按钮在 keyup 时被 Space 再次触发；不阻止冒泡（相机需要 keyup 清除按键）
    if (!modalStack.length) return;
    const k = normKey(e);
    if (k === 'Space' || k === 'Enter') e.preventDefault();
  }

  function focusables(root) {
    return Array.from(root.querySelectorAll('button:not(:disabled), input:not(:disabled), [tabindex]:not([tabindex="-1"])'))
      .filter(x => x.offsetParent !== null || x === document.activeElement);
  }
  function trapTab(e, panel) {
    const list = focusables(panel);
    e.preventDefault();
    e.stopPropagation();
    if (!list.length) return;
    let i = list.indexOf(document.activeElement);
    i = e.shiftKey ? (i <= 0 ? list.length - 1 : i - 1) : (i < 0 || i >= list.length - 1 ? 0 : i + 1);
    focusEl(list[i]);
  }

  // 列表键盘导航（choose / chooseMany 共用）
  function listNav(buttons, enabledAt, getActive, setActive, k) {
    const n = buttons.length;
    if (!n) return false;
    let a = getActive();
    const step = (from, d) => {
      for (let j = 1; j <= n; j++) {
        const idx = ((from + d * j) % n + n) % n;
        if (enabledAt(idx)) return idx;
      }
      return -1;
    };
    let next = -2;
    if (k === 'ArrowDown') next = step(a < 0 ? -1 : a, 1);
    else if (k === 'ArrowUp') next = step(a < 0 ? 0 : a, -1);
    else if (k === 'Home') next = step(-1, 1);
    else if (k === 'End') next = step(n, -1);
    else if (k === 'PageDown') { next = a; for (let j = 0; j < 5; j++) { const s = step(next < 0 ? -1 : next, 1); if (s <= next) break; next = s; } }
    else if (k === 'PageUp') { next = a; for (let j = 0; j < 5; j++) { const s = step(next < 0 ? 0 : next, -1); if (s >= next || s < 0) break; next = s; } }
    if (next === -2) return false;
    if (next >= 0) {
      setActive(next);
      focusEl(buttons[next]);
      try { buttons[next].scrollIntoView({ block: 'nearest' }); } catch (err) { /* 忽略 */ }
    }
    return true;
  }

  // -------------------------------------------------------- 打字机效果 --
  // 把元素内所有文字拆为“已显示 + 隐藏占位”，版面从一开始就是完整大小。
  // 文字中的行内头像（.sg-portrait）算作一个字：打到它时才显示，不会先于前面的文字出现。
  function typewriter(root) {
    const texts = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT | NodeFilter.SHOW_ELEMENT, {
      acceptNode(x) {
        if (x.nodeType === 3) return NodeFilter.FILTER_ACCEPT;
        return x.classList && x.classList.contains('sg-portrait') ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP;
      },
    });
    let n;
    while ((n = walker.nextNode())) texts.push(n);
    const parts = [];
    let total = 0;
    for (const t of texts) {
      if (t.nodeType === 1) {
        t.style.visibility = 'hidden';
        parts.push({ face: t, start: total, shown: 0 });
        total += 1;
        continue;
      }
      const chars = Array.from(t.data);
      if (!chars.length) continue;
      const vis = document.createTextNode('');
      const hid = document.createElement('span');
      hid.className = 'sg-tw-hide';
      hid.textContent = t.data;
      t.parentNode.insertBefore(vis, t);
      t.parentNode.insertBefore(hid, t);
      t.parentNode.removeChild(t);
      parts.push({ chars, vis, hid, start: total, shown: 0 });
      total += chars.length;
    }
    function show(k) {
      for (const p of parts) {
        if (p.face) {
          const on = k > p.start ? 1 : 0;
          if (on !== p.shown) { p.shown = on; p.face.style.visibility = on ? '' : 'hidden'; }
          continue;
        }
        const m = M.clamp(k - p.start, 0, p.chars.length);
        if (m === p.shown) continue;
        p.shown = m;
        p.vis.data = p.chars.slice(0, m).join('');
        p.hid.textContent = p.chars.slice(m).join('');
      }
    }
    return { total, show, showAll() { show(total); } };
  }

  // ---------------------------------------------------------- 对话 say --
  // 说话者头像 + 文字（打字机），点击 / Enter / Space 继续。
  // speaker：姓名或武将对象（null = 旁白）；speakerColor：找不到该武将时姓氏徽章的颜色。
  // opts（可选）：{ mood: 'neutral'|'angry'|'hurt'|'win'（头像表情）, sub: 姓名下的小字 HTML（身份 / 势力）, className }
  UI.say = function (text, speaker, speakerColor, opts) {
    ensure();
    if (speaker === undefined || speaker === '') speaker = null;
    if (!speakerColor) speakerColor = UI.colors.cinnabar;
    opts = opts || {};
    const spObj = speaker && typeof speaker === 'object' ? speaker : null;
    const spName = speaker === null ? null : spObj ? String(spObj.name || '') : String(speaker);
    return new Promise(resolve => {
      const m = openModal('sg-say' + (spName ? ' has-speaker' : '') + (opts.className ? ' ' + opts.className : ''), { blockerClass: 'sg-say-modal' });
      const panel = m.panel;
      if (spName) {
        const sp = el('div', 'sg-say-speaker');
        // 有该武将时显示头像（势力色边框），否则退回姓氏徽章
        const pg = spObj || (SG.Portrait && SG.Portrait.find(spName));
        let face = null;
        if (pg && SG.Portrait) {
          try { face = SG.Portrait.el(pg, opts.mood ? { size: UI.facePx(FACE_REM.say), mood: opts.mood } : { size: UI.facePx(FACE_REM.say) }); } catch (e) { face = null; }
        }
        sp.appendChild(face || UI.medal(spName.charAt(0), speakerColor, 96));
        // 长名（外国人名）收紧字距，再长则允许折行，不挤压对话文字
        const nlen = Array.from(spName).length;
        const nm = el('div', 'sg-say-name' + (nlen >= 7 ? ' is-long is-xlong' : nlen >= 5 ? ' is-long' : ''), SG.esc(spName));
        sp.appendChild(nm);
        if (opts.sub) sp.appendChild(el('div', 'sg-say-sub', opts.sub));
        panel.appendChild(sp);
      }
      const body = el('div', 'sg-say-text');
      const inner = el('div', 'sg-say-inner', text === null || text === undefined ? '' : String(text));
      body.appendChild(inner);
      panel.appendChild(body);
      const hint = el('div', 'sg-say-hint');           // 笔尖「继续」记号（UI-V4 §4.7，取代 ▼）
      hint.setAttribute('aria-hidden', 'true');
      panel.appendChild(hint);
      const tw = typewriter(inner);
      let full = false, done = false;
      // 文字速度（设置第 23 行：慢 20 · 中 40 · 快 80 字/秒 · 即时）与自动推进（第 24 行）
      const US = SG.UISettings;
      const cps = US ? US.textCps() : 120;
      const auto = !!(US && US.get('ui.autoAdvance'));
      function setFull() {
        if (full) return;
        full = true;
        tw.showAll();
        panel.classList.add('is-full');
        if (auto) setTimeout(() => { if (!done) advance(); }, 2500 + 50 * tw.total);
      }
      function advance() {
        if (done) return;
        if (!full) { setFull(); return; }
        done = true;
        sfxClick();
        m.close();
        resolve();
      }
      m.blocker.addEventListener('click', advance);
      m.onKey = k => {
        if (k === 'Enter' || k === 'Space' || k === 'Escape') { advance(); return true; }
        return false;
      };
      focusEl(m.blocker);
      // 按设置的速度逐字显示（即时：一次显示完）
      let shown = 2, last = performance.now();
      if (tw.total <= shown || !isFinite(cps)) setFull(); else tw.show(shown);
      function tick(now) {
        if (done || full) return;
        shown += Math.max(0, now - last) / 1000 * cps;
        last = now;
        if (shown >= tw.total) { setFull(); return; }
        tw.show(Math.floor(shown));
        requestAnimationFrame(tick);
      }
      if (!full) requestAnimationFrame(tick);
    });
  };

  // ------------------------------------------------------- 列表 choose --
  function itemHtml(it, check) {
    return (check ? '<span class="sg-check" aria-hidden="true">◇</span>' : '') +
      '<span class="sg-item-main"><span class="sg-item-label">' + (it.label === undefined || it.label === null ? '' : it.label) + '</span>' +
      (it.desc !== null && it.desc !== undefined ? '<span class="sg-item-desc">' + it.desc + '</span>' : '') + '</span>' +
      (it.right !== null && it.right !== undefined ? '<span class="sg-item-right">' + it.right + '</span>' : '');
  }
  function isEnabled(it) { return !it || it.enabled !== false; }

  // 返回所选索引，取消为 -1
  UI.choose = function (title, items, info, width) {
    ensure();
    items = items || [];
    if (info === undefined) info = null;
    if (!width) width = 640;
    return new Promise(resolve => {
      const m = openModal('sg-choose', { width });
      let done = false;
      const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
      const head = el('div', 'sg-dlg-head');
      head.appendChild(el('div', 'sg-dlg-title', title));
      const close = UI.button(head, '×', () => finish(-1), { className: 'sg-close', ariaLabel: '关闭' });
      m.panel.appendChild(head);
      if (info !== null) m.panel.appendChild(el('div', 'sg-dlg-info', info));
      const list = el('div', 'sg-list');
      list.setAttribute('role', 'listbox');
      let active = -1;
      const buttons = items.map((it, i) => {
        const b = el('button', 'sg-item', itemHtml(it, false));
        if (it && it.avatar) { b.insertBefore(avatarEl(it), b.firstChild); b.classList.add('has-face'); list.classList.add('has-faces'); }
        b.type = 'button';
        b.setAttribute('role', 'option');
        b.disabled = !isEnabled(it);
        b.addEventListener('click', () => { if (!isEnabled(items[i]) || done) return; sfxClick(); finish(i); });
        b.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse' && isEnabled(items[i])) active = i; });
        b.addEventListener('focus', () => { active = i; });
        list.appendChild(b);
        return b;
      });
      m.panel.appendChild(list);
      const enabledAt = i => isEnabled(items[i]);
      m.onKey = k => {
        if (k === 'Escape') { finish(-1); return true; }
        if (k === 'Enter' || k === 'Space') {
          if (document.activeElement === close) { sfxClick(); finish(-1); return true; }
          if (active >= 0 && enabledAt(active)) { sfxClick(); finish(active); }
          return true;
        }
        if (/^[1-9]$/.test(k)) {
          const i = +k - 1;
          if (i < items.length && enabledAt(i)) { sfxClick(); finish(i); }
          return true;
        }
        return listNav(buttons, enabledAt, () => active, v => { active = v; }, k);
      };
      let first = items.findIndex(it => it && it.selected && isEnabled(it));
      if (first < 0) first = items.findIndex(isEnabled);
      if (first >= 0) {
        active = first;
        focusEl(buttons[first]);
        if (first > 0) try { buttons[first].scrollIntoView({ block: 'nearest' }); } catch (e) { /* 忽略 */ }
      } else focusEl(close);
    });
  };

  // ------------------------------------------------------- 多选 chooseMany --
  // 返回排序后的索引数组，取消为 null
  UI.chooseMany = function (title, items, max, info) {
    ensure();
    items = items || [];
    if (info === undefined) info = null;
    return new Promise(resolve => {
      const sel = new Set();
      items.forEach((it, i) => { if (it && it.selected) sel.add(i); });
      const m = openModal('sg-choose sg-choosemany', { width: 700 });
      let done = false;
      const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
      const head = el('div', 'sg-dlg-head');
      head.appendChild(el('div', 'sg-dlg-title', title));
      m.panel.appendChild(head);
      if (info !== null && info !== '') m.panel.appendChild(el('div', 'sg-dlg-info', info));
      const list = el('div', 'sg-list');
      list.setAttribute('role', 'listbox');
      list.setAttribute('aria-multiselectable', 'true');
      let active = -1;
      const buttons = items.map((it, i) => {
        const b = el('button', 'sg-item', itemHtml(it, true));
        if (it && it.avatar) { const ck = b.querySelector('.sg-check'); b.insertBefore(avatarEl(it), ck ? ck.nextSibling : b.firstChild); b.classList.add('has-face'); list.classList.add('has-faces'); }
        b.type = 'button';
        b.setAttribute('role', 'option');
        b.disabled = !isEnabled(it);
        b.addEventListener('click', () => { if (!isEnabled(items[i]) || done) return; sfxClick(); toggle(i); });
        b.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse' && isEnabled(items[i])) active = i; });
        b.addEventListener('focus', () => { active = i; });
        list.appendChild(b);
        return b;
      });
      m.panel.appendChild(list);
      const foot = el('div', 'sg-dlg-foot');
      const count = el('div', 'sg-count');
      foot.appendChild(count);
      foot.appendChild(el('div', 'sg-spacer'));
      const cancel = UI.button(foot, '取消', () => finish(null));
      const ok = UI.button(foot, '确定', () => { if (sel.size > 0) finish(Array.from(sel).sort((a, b) => a - b)); }, { primary: true });
      m.panel.appendChild(foot);
      function toggle(i) {
        if (sel.has(i)) sel.delete(i); else if (sel.size < max) sel.add(i);
        refresh();
      }
      function refresh() {
        buttons.forEach((b, i) => {
          const on = sel.has(i);
          b.classList.toggle('is-on', on);
          b.classList.toggle('is-capped', !on && sel.size >= max);
          b.setAttribute('aria-selected', on ? 'true' : 'false');
          const c = b.querySelector('.sg-check');
          if (c) c.textContent = on ? '◆' : '◇';
        });
        count.innerHTML = '已选 <b>' + sel.size + '</b> / ' + max;
        ok.disabled = sel.size === 0;
      }
      refresh();
      const enabledAt = i => isEnabled(items[i]);
      m.onKey = k => {
        if (k === 'Escape') { finish(null); return true; }
        if (k === 'Space') {
          const a = document.activeElement;
          if (a === ok || a === cancel) { a.click(); return true; }
          if (active >= 0 && enabledAt(active)) { sfxClick(); toggle(active); }
          return true;
        }
        if (k === 'Enter') {
          const a = document.activeElement;
          if (a === cancel) { cancel.click(); return true; }
          if (sel.size > 0) { sfxClick(); finish(Array.from(sel).sort((x, y) => x - y)); }
          return true;
        }
        return listNav(buttons, enabledAt, () => active, v => { active = v; }, k);
      };
      const first = items.findIndex(isEnabled);
      if (first >= 0) { active = first; focusEl(buttons[first]); } else focusEl(cancel);
    });
  };

  // ------------------------------------------------------- 数值 pickNumber --
  // 返回所选数值，取消为 -1。max < min 时提示“数量不足”并返回 -1
  UI.pickNumber = async function (title, min, max, step, start, describe) {
    ensure();
    if (max < min) { await UI.say('数量不足，无法执行。'); return -1; }
    if (!step || step < 1) step = 1;
    if (describe === undefined) describe = null;
    return new Promise(resolve => {
      let val = M.clamp(start, min, max);
      let sliderValue = val;
      const m = openModal('sg-number', { width: 620 });
      let done = false;
      const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
      const head = el('div', 'sg-dlg-head');
      head.appendChild(el('div', 'sg-dlg-title', title));
      m.panel.appendChild(head);
      const row = el('div', 'sg-number-row');
      const minus = UI.button(row, '－', null, { ariaLabel: '减少' });
      const big = el('div', 'sg-number-big');
      row.appendChild(big);
      const plus = UI.button(row, '＋', null, { ariaLabel: '增加' });
      m.panel.appendChild(row);
      const desc = el('div', 'sg-number-desc');
      m.panel.appendChild(desc);
      const range = document.createElement('input');
      range.type = 'range';
      range.className = 'sg-range';
      range.min = String(min); range.max = String(max); range.step = '1';
      range.value = String(val);
      range.setAttribute('aria-label', String(title).replace(/<[^>]*>/g, ''));
      m.panel.appendChild(range);
      m.panel.appendChild(el('div', 'sg-range-ends', '<span>' + min + '</span><span>' + max + '</span>'));
      const foot = el('div', 'sg-dlg-foot');
      foot.appendChild(el('div', 'sg-spacer'));
      UI.button(foot, '取消', () => finish(-1));
      const ok = UI.button(foot, '确定', () => finish(val), { primary: true });
      m.panel.appendChild(foot);

      function show() {
        big.textContent = String(val);
        desc.innerHTML = describe ? String(describe(val)) : '';
      }
      function setFill(v) {
        const p = max > min ? (v - min) / (max - min) * 100 : 100;
        range.style.setProperty('--p', p.toFixed(2) + '%');
      }
      // Slider.onValueChanged：吸附到 step 的整数倍
      function onSlider(f) {
        val = M.clamp(M.roundToInt(f / step) * step, min, max);
        show();
      }
      // 与 Unity 一致：设置滑块值时若有变化会触发 onValueChanged
      function setSlider(v) {
        v = M.clamp(Math.round(v), min, max);
        range.value = String(v);
        setFill(v);
        if (v !== sliderValue) { sliderValue = v; onSlider(v); }
      }
      function upd() { show(); setSlider(val); }
      range.addEventListener('input', () => {
        const f = M.clamp(Math.round(+range.value), min, max);
        sliderValue = f;
        setFill(f);
        onSlider(f);
      });
      range.addEventListener('change', () => { range.value = String(val); setFill(val); sliderValue = val; });

      function dec() { val = Math.max(min, val - step); upd(); }
      function inc() { val = Math.min(max, val + step); upd(); }
      // 按住连续增减
      function holdable(btn, fn) {
        let timer = 0, rep = 0;
        const stop = () => { clearTimeout(timer); clearInterval(rep); timer = 0; rep = 0; };
        btn.addEventListener('pointerdown', e => {
          if (e.button !== undefined && e.button !== 0) return;
          // 触摸指针默认被按钮隐式捕获，手指滑出后不会触发 pointerleave；释放捕获以便滑出即停止
          try { if (btn.hasPointerCapture && btn.hasPointerCapture(e.pointerId)) btn.releasePointerCapture(e.pointerId); } catch (_) { /* 忽略 */ }
          sfxClick();
          fn();
          stop();
          timer = setTimeout(() => { rep = setInterval(fn, 70); }, 380);
          // 在任意位置抬起手指（或对话框已关闭、按钮已移除）时同样停止
          window.addEventListener('pointerup', stop, { once: true });
          window.addEventListener('pointercancel', stop, { once: true });
        });
        // 兜底：指针移出按钮矩形即停止（捕获未能释放时 pointerleave 不可靠）
        btn.addEventListener('pointermove', e => {
          if (!timer && !rep) return;
          const r = btn.getBoundingClientRect();
          if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) stop();
        });
        ['pointerup', 'pointerleave', 'pointercancel', 'blur'].forEach(ev => btn.addEventListener(ev, stop));
        btn.addEventListener('click', e => {
          // 键盘触发（detail === 0）时执行；指针点击已在 pointerdown 中处理
          if (e.detail === 0) { sfxClick(); fn(); }
        });
      }
      holdable(minus, dec);
      holdable(plus, inc);

      m.onKey = k => {
        if (k === 'Escape') { finish(-1); return true; }
        if (k === 'Enter' || k === 'Space') {
          const a = document.activeElement;
          if (a && a !== range && a !== ok && a.classList && a.classList.contains('sg-btn')) { a.click(); return true; }
          sfxClick();
          finish(val);
          return true;
        }
        if (k === 'ArrowLeft' || k === 'ArrowDown') { dec(); return true; }
        if (k === 'ArrowRight' || k === 'ArrowUp') { inc(); return true; }
        if (k === 'PageDown') { val = Math.max(min, val - step * 10); upd(); return true; }
        if (k === 'PageUp') { val = Math.min(max, val + step * 10); upd(); return true; }
        if (k === 'Home') { setSlider(min); return true; }
        if (k === 'End') { setSlider(max); return true; }
        return false;
      };
      setFill(val);
      upd();
      focusEl(ok);
    });
  };

  // ---------------------------------------------------------- 确认 confirm --
  // opts（可选）：{ body: 标题与按钮之间的说明 HTML（如 UI.genCard 名片）, width }
  UI.confirm = function (text, yes, no, opts) {
    ensure();
    if (yes === undefined || yes === null) yes = '是';
    if (no === undefined || no === null) no = '否';
    opts = opts || {};
    return new Promise(resolve => {
      const m = openModal('sg-confirm' + (opts.body ? ' has-body' : ''), { width: opts.width || (opts.body ? 600 : 520) });
      let done = false;
      const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
      const head = el('div', 'sg-dlg-head');
      head.appendChild(el('div', 'sg-dlg-title', text));
      const close = UI.button(head, '×', () => finish(false), { className: 'sg-close', ariaLabel: '关闭' });
      m.panel.appendChild(head);
      if (opts.body) m.panel.appendChild(el('div', 'sg-dlg-info sg-confirm-body', opts.body));
      const row = el('div', 'sg-confirm-btns');
      const bNo = UI.button(row, no, () => finish(false));
      const bYes = UI.button(row, yes, () => finish(true), { primary: true });
      m.panel.appendChild(row);
      m.onKey = k => {
        const a = document.activeElement;
        if (k === 'Escape') { finish(false); return true; }
        if (k === 'Enter' || k === 'Space') {
          sfxClick();
          if (a === bNo || a === close) finish(false); else finish(true);
          return true;
        }
        if (k === 'ArrowLeft' || k === 'ArrowRight' || k === 'ArrowUp' || k === 'ArrowDown') {
          focusEl(a === bYes ? bNo : bYes);
          return true;
        }
        return false;
      };
      focusEl(bYes);
    });
  };

  // 只有一个按钮的说明框（战后成长等）：结构与 confirm 相同（.sg-confirm-btns 内唯一的主按钮），
  // 说明区可滚动。Enter / Space / Escape / × 都是关闭。
  UI.notice = function (title, body, opts) {
    ensure();
    opts = opts || {};
    return new Promise(resolve => {
      const m = openModal('sg-confirm sg-notice has-body' + (opts.className ? ' ' + opts.className : ''), { width: opts.width || 640 });
      let done = false;
      const finish = () => { if (done) return; done = true; m.close(); resolve(); };
      const head = el('div', 'sg-dlg-head');
      head.appendChild(el('div', 'sg-dlg-title', title));
      UI.button(head, '×', finish, { className: 'sg-close', ariaLabel: '关闭' });
      m.panel.appendChild(head);
      if (body) m.panel.appendChild(el('div', 'sg-dlg-info sg-confirm-body sg-notice-body', body));
      const row = el('div', 'sg-confirm-btns');
      const ok = UI.button(row, opts.ok || '确定', finish, { primary: true });
      m.panel.appendChild(row);
      m.onKey = k => {
        if (k === 'Escape' || k === 'Enter' || k === 'Space') { sfxClick(); finish(); return true; }
        return false;
      };
      focusEl(ok);
    });
  };

  // 等级小标签与经验进度条（第三版成长；未载入成长规则时为空串）
  UI.lvTag = function (gen, cls) {
    const P = SG.Progress;
    if (!P || P.enabled === false || !gen) return '';
    let lv = gen.level;
    try { if (typeof P.expInfo === 'function') lv = P.expInfo(gen).level; } catch (e) { /* 用原值 */ }
    if (!(lv >= 1)) return '';
    return '<span class="sg-lv' + (cls ? ' ' + cls : '') + '">Lv<b>' + lv + '</b></span>';
  };
  UI.expBar = function (pct, cls) {
    const v = M.clamp01(+pct || 0);
    return '<span class="sg-expbar' + (cls ? ' ' + cls : '') + '" style="--v:' + v.toFixed(3) + '"><i></i></span>';
  };

  // -------------------------------------------------------------- 提示 --
  // 淡入 0.2 秒、最后 1/3 秒淡出（AutoFade：alpha = clamp01(min(t*5, (life-t)*3))）
  function autoFadeFrames(life, popIn) {
    const fin = Math.min(0.2, life / 3) / life;
    const fout = Math.min(1 / 3, life / 3) / life;
    const a = { opacity: 0 };
    if (popIn) a.transform = 'scale(.94)';
    const b = { opacity: 1, offset: fin };
    if (popIn) b.transform = 'scale(1)';
    const c = { opacity: 1, offset: Math.max(fin, 1 - fout) };
    if (popIn) c.transform = 'scale(1)';
    const d = { opacity: 0 };
    if (popIn) d.transform = 'scale(1)';
    return [a, b, c, d];
  }

  // 提示条（UI-V4 §4.7）：纸条从右侧滑入，左端朱印。UI.toast(html, seconds) 或 UI.toast(html, { seconds, seal: '记'|'成'|…, face: 武将, kind })
  const MAX_TOASTS = 3;
  UI.toast = function (html, secondsOrOpts) {
    ensure();
    const o = secondsOrOpts && typeof secondsOrOpts === 'object' ? secondsOrOpts : { seconds: secondsOrOpts };
    let seconds = o.seconds;
    if (seconds === undefined || seconds === null) seconds = 2.2;
    const t = el('div', 'sg-toast' + (o.kind ? ' is-' + o.kind : ''));
    t.setAttribute('role', 'status');
    let lead = '';
    if (o.seal) lead += '<span class="sg-toast-seal">' + SG.esc(String(o.seal).charAt(0)) + '</span>';
    if (o.face) lead += UI.face(o.face, { size: Math.round(UI.facePx(1.75)) });
    t.innerHTML = lead + '<span class="sg-toast-body">' + (html === undefined || html === null ? '' : String(html)) + '</span>';
    L.toastStack.appendChild(t);
    while (L.toastStack.childElementCount > MAX_TOASTS) L.toastStack.removeChild(L.toastStack.firstElementChild);
    const life = Math.max(0.3, seconds);
    let removed = false;
    const remove = () => { if (removed) return; removed = true; if (t.parentNode) t.parentNode.removeChild(t); };
    try {
      if (t.animate) {
        const kf = autoFadeFrames(life, false);
        const reduced = motionOff();
        kf[0].transform = reduced ? 'none' : 'translateX(1.6rem)'; kf[1].transform = 'none'; kf[2].transform = 'none'; kf[3].transform = reduced ? 'none' : 'translateY(-.3rem)';
        const anim = t.animate(kf, { duration: life * 1000, easing: 'linear', fill: 'both' });
        anim.onfinish = remove;
      }
    } catch (e) { /* 无 WAAPI 时仅定时移除 */ }
    setTimeout(remove, life * 1000 + 80);
    return t;
  };

  // 横幅显示期间压暗世界标签层，避免单位信息卡片透出在横幅文字后面
  let liveBanners = 0;
  function bannerDim(on) {
    liveBanners = Math.max(0, liveBanners + (on ? 1 : -1));
    const dim = liveBanners > 0;
    L.root.classList.toggle('has-banner', dim);
    L.labels.style.transition = 'opacity .2s ease';
    L.labels.style.opacity = dim ? '0.15' : '';
  }

  // 屏幕中央的大字横幅
  UI.banner = function (text, sub, seconds) {
    ensure();
    if (sub === undefined) sub = null;
    if (seconds === undefined || seconds === null) seconds = 2.4;
    const b = el('div', 'sg-banner');
    b.appendChild(el('div', 'sg-banner-text', text));
    if (sub !== null && sub !== '') b.appendChild(el('div', 'sg-banner-sub', sub));
    b.appendChild(el('div', 'sg-banner-line'));
    L.toasts.appendChild(b);
    const life = Math.max(0.3, seconds);
    // 标签层随横幅淡出开始时恢复（与 AutoFade 的最后 1/3 秒同步）
    let dimmed = true;
    bannerDim(true);
    const undim = () => { if (!dimmed) return; dimmed = false; bannerDim(false); };
    setTimeout(undim, Math.max(0, life - Math.min(1 / 3, life / 3)) * 1000);
    let removed = false;
    const remove = () => { if (removed) return; removed = true; undim(); if (b.parentNode) b.parentNode.removeChild(b); };
    try {
      if (b.animate) {
        // 外层淡入淡出，文字 PopIn（0.94 → 1）
        const anim = b.animate(autoFadeFrames(life, false), { duration: life * 1000, easing: 'linear', fill: 'both' });
        anim.onfinish = remove;
        const txt = b.querySelector('.sg-banner-text');
        txt.animate([{ transform: 'scale(.94)', letterSpacing: '.3em' }, { transform: 'scale(1)', letterSpacing: '.12em' }],
          { duration: 420, easing: 'cubic-bezier(.2,.8,.2,1)', fill: 'both' });
      }
    } catch (e) { /* 无 WAAPI */ }
    setTimeout(remove, life * 1000 + 80);
    return b;
  };

  // ------------------------------------------------------- 跟随三维坐标 --
  // follow(element, getWorldPos, { offsetY = 0, hideBeyond = 9999, align = 'center' })
  //   元素中心（align 'center'）/ 底边中点（'bottom'）/ 左上角（'none'）对准投影点；
  //   offsetY 为参考像素，向上为正（同 WorldFollow.offset.y）。
  const ALIGN = { center: ' translate(-50%,-50%)', bottom: ' translate(-50%,-100%)', top: ' translate(-50%,0)', none: '' };
  let _tmpV = null;

  function project(p, camera) {
    const G = SG.Gfx;
    if (G && typeof G.worldToScreen === 'function') return G.worldToScreen(p);
    if (!camera || typeof THREE === 'undefined') return null;
    if (!_tmpV) _tmpV = new THREE.Vector3();
    camera.updateMatrixWorld();
    _tmpV.set(p.x, p.y, p.z).applyMatrix4(camera.matrixWorldInverse);
    const inFront = _tmpV.z < -camera.near;
    _tmpV.set(p.x, p.y, p.z).project(camera);
    return {
      x: (_tmpV.x * 0.5 + 0.5) * window.innerWidth,
      y: (-_tmpV.y * 0.5 + 0.5) * window.innerHeight,
      visible: inFront,
      dist: Math.sqrt((camera.position.x - p.x) ** 2 + (camera.position.y - p.y) ** 2 + (camera.position.z - p.z) ** 2),
    };
  }

  function updateOne(h, camera, now) {
    if (h.float) {
      const t = (now - h.float.t0) / 1000;
      if (t > 1.2) { h.remove(); return; }
      h.extraY = t * 70 * uiScale;
      h.alpha = M.clamp01(1.6 - t * 1.4);
    }
    let p = null;
    try { p = h.getPos ? h.getPos() : null; } catch (e) { p = null; }
    const s = p ? project(p, camera) : null;
    const vis = !!(s && s.visible && s.dist < h.hideBeyond && h.alpha > 0.001 && isFinite(s.x) && isFinite(s.y));
    if (!vis) {
      if (h._vis !== false) { h.el.style.visibility = 'hidden'; h._vis = false; }
      return;
    }
    if (h._vis !== true) { h.el.style.visibility = ''; h._vis = true; }
    const x = s.x, y = s.y - h.offsetY * uiScale - h.extraY;
    if (!(Math.abs(x - h._x) <= 0.05 && Math.abs(y - h._y) <= 0.05)) {
      h._x = x; h._y = y;
      h.el.style.transform = 'translate3d(' + x.toFixed(1) + 'px,' + y.toFixed(1) + 'px,0)' + h.alignT;
    }
    const a = M.clamp01(h.alpha);
    if (a !== h._a) { h._a = a; h.el.style.opacity = a >= 1 ? '' : a.toFixed(3); }
  }

  UI.follow = function (element, getWorldPos, opts) {
    ensure();
    opts = opts || {};
    element.classList.add('sg-follow');
    element.style.position = 'absolute';
    element.style.left = '0';
    element.style.top = '0';
    element.style.pointerEvents = 'none';
    element.style.visibility = 'hidden';
    L.labels.appendChild(element);
    const h = {
      el: element,
      alpha: 1,
      getPos: getWorldPos,
      offsetY: opts.offsetY || 0,
      hideBeyond: opts.hideBeyond === undefined || opts.hideBeyond === null ? 9999 : opts.hideBeyond,
      alignT: ALIGN[opts.align || 'center'] !== undefined ? ALIGN[opts.align || 'center'] : ALIGN.center,
      extraY: 0,
      float: null,
      _vis: false, _x: NaN, _y: NaN, _a: -1,
      removed: false,
      remove() {
        if (h.removed) return;
        h.removed = true;
        followers.delete(h);
        if (element.parentNode) element.parentNode.removeChild(element);
      },
      update() { updateOne(h, SG.Gfx && SG.Gfx.camera, performance.now()); },
    };
    followers.add(h);
    try { if (SG.Gfx && typeof SG.Gfx.worldToScreen === 'function') h.update(); } catch (e) { /* 渲染器未就绪 */ }
    return h;
  };

  // 每帧由主循环调用一次
  UI.updateFollowers = function (camera) {
    if (!followers.size) return;
    const now = performance.now();
    for (const h of Array.from(followers)) {
      try { updateOne(h, camera, now); } catch (e) { console.error(e); h.remove(); }
    }
  };

  UI.followerCount = function () { return followers.size; };

  // 上浮并淡出的文字（伤害数字等），1.2 秒
  UI.floatText = function (worldPos, text, color, sizePx) {
    ensure();
    if (!sizePx) sizePx = Math.round(34 * uiScale);
    const wrap = el('div', 'sg-float');
    const t = el('div', 'sg-float-text');
    t.textContent = String(text);
    t.style.color = cssColor(color || UI.colors.gold);
    t.style.fontSize = sizePx + 'px';
    wrap.appendChild(t);
    const pos = (typeof THREE !== 'undefined')
      ? new THREE.Vector3(worldPos.x, worldPos.y, worldPos.z)
      : { x: worldPos.x, y: worldPos.y, z: worldPos.z };
    const h = UI.follow(wrap, () => pos, {});
    h.float = { t0: performance.now() };
    setTimeout(() => h.remove(), 1600);
    return h;
  };

  // =================================================================== 第四版部件（UI-V4 §4、§13）==
  // ---- 动效 SG.Motion：reduced / level 由设置第 15、44 行写入（js/settings.js）----
  const Motion = SG.Motion = SG.Motion || {};
  if (Motion.reduced === undefined) {
    let r = false;
    try { r = typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; } catch (e) { r = false; }
    Motion.reduced = r;
  }
  if (!Motion.level) Motion.level = Motion.reduced ? 'off' : 'full';
  Motion.DUR = { instant: 80, quick: 140, base: 220, slow: 380, scene: 650 };
  Motion.EASE = { out: 'cubic-bezier(.2,.8,.2,1)', in: 'cubic-bezier(.5,0,.75,0)', stamp: 'cubic-bezier(.3,1.6,.5,1)', spring: 'cubic-bezier(.34,1.56,.64,1)' };
  // play(el, keyframes, { dur, ease, delay, fill }) → Promise（减少动态时立即完成，并直接停在最后一帧）
  Motion.play = function (target, kf, o) {
    o = o || {};
    if (!target) return Promise.resolve();
    if (Motion.reduced || Motion.level === 'off' || !target.animate) {
      const last = kf[kf.length - 1] || {};
      if (o.fill !== 'none') for (const k of Object.keys(last)) if (k !== 'offset' && k !== 'easing') { try { target.style[k] = last[k]; } catch (e) { /* 忽略 */ } }
      return Promise.resolve();
    }
    try {
      const a = target.animate(kf, { duration: o.dur || 220, easing: Motion.EASE[o.ease] || o.ease || Motion.EASE.out, delay: o.delay || 0, fill: o.fill || 'both' });
      return a.finished.then(() => a, () => a);
    } catch (e) { return Promise.resolve(); }
  };
  Motion.seq = async function (steps) { for (const s of steps) await (typeof s === 'function' ? s() : s); };
  // 依次进场：步长 18 ms，总长不超过 216 ms（超出的一起出现）
  Motion.stagger = function (els, fn, step, cap) {
    step = step || 18; cap = cap || 216;
    return Promise.all(Array.from(els).map((x, i) => fn(x, Math.min(i * step, cap), i)));
  };
  Motion.rise = function (els) {
    return Motion.stagger(els, (x, d) => Motion.play(x, [{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }], { dur: 220, delay: d }));
  };

  // 界面字号设置（第 43 行）改变后重新读取根字号
  UI.rescale = function () { updateScale(); };

  // ---- 长按（触屏的「悬停」，设置第 40 行；手指周围画一圈进度）----
  UI.longPress = function (target, fn, opts) {
    opts = opts || {};
    let timer = 0, sx = 0, sy = 0, fired = false;
    const ms = () => (SG.UISettings ? SG.UISettings.longPressMs() : 450);
    target.addEventListener('pointerdown', e => {
      if (e.pointerType === 'mouse' && !opts.mouse) return;
      fired = false; sx = e.clientX; sy = e.clientY;
      clearTimeout(timer);
      timer = setTimeout(() => { fired = true; try { fn(e); } catch (err) { console.error(err); } }, ms());
    });
    const cancel = () => clearTimeout(timer);
    target.addEventListener('pointermove', e => { if (Math.hypot(e.clientX - sx, e.clientY - sy) > 10) cancel(); });
    ['pointerup', 'pointercancel', 'pointerleave'].forEach(ev => target.addEventListener(ev, cancel));
    // 长按触发后吞掉随之而来的 click
    target.addEventListener('click', e => { if (fired) { fired = false; e.stopImmediatePropagation(); e.preventDefault(); } }, true);
  };

  // ---- 纸签提示（UI-V4 §4.7）：鼠标悬停 350 ms 或长按；不遮住目标，留在视口内 ----
  let tipEl = null, tipTimer = 0;
  function hideTip() { clearTimeout(tipTimer); if (tipEl) { tipEl.remove(); tipEl = null; } }
  function showTip(target, html) {
    hideTip();
    if (!target.isConnected) return;
    ensure();
    const t = tipEl = el('div', 'sg-tip', typeof html === 'function' ? html() : html);
    t.setAttribute('role', 'tooltip');
    L.toasts.appendChild(t);
    const r = target.getBoundingClientRect(), tw = t.offsetWidth, th = t.offsetHeight;
    const vw = window.innerWidth, vh = window.innerHeight;
    let x = Math.min(vw - tw - 6, Math.max(6, r.left + r.width / 2 - tw / 2));
    let y = r.bottom + 10;
    if (y + th > vh - 6) y = Math.max(6, r.top - th - 10);
    t.style.left = x + 'px'; t.style.top = y + 'px';
    t.style.setProperty('--tx', Math.max(10, Math.min(tw - 10, r.left + r.width / 2 - x)) + 'px');
  }
  UI.tip = function (target, html) {
    if (!target) return target;
    target._tip = html;
    if (target._tipHooked) return target;
    target._tipHooked = true;
    target.addEventListener('pointerenter', e => {
      if (e.pointerType !== 'mouse') return;
      clearTimeout(tipTimer);
      tipTimer = setTimeout(() => showTip(target, target._tip), 350);
    });
    target.addEventListener('pointerleave', hideTip);
    target.addEventListener('pointerdown', e => { if (e.pointerType === 'mouse') hideTip(); });
    UI.longPress(target, () => { showTip(target, target._tip); setTimeout(hideTip, 2600); });
    return target;
  };
  UI.hideTip = hideTip;

  // ---- 页签 书签（UI-V4 §4.4）：items = [{ id, icon, label, badge }]；← → / Ctrl+Tab 由窗口转发 ----
  UI.tabs = function (parent, items, onChange, opts) {
    opts = opts || {};
    const bar = el('div', 'sg-tabs' + (opts.vertical ? ' is-vertical' : '') + (opts.className ? ' ' + opts.className : ''));
    bar.setAttribute('role', 'tablist');
    const btns = new Map();
    let cur = null;
    const api = {
      el: bar,
      get() { return cur; },
      set(id, quiet) {
        if (!btns.has(id)) return;
        const prev = cur;
        cur = id;
        for (const [k, b] of btns) { const on = k === id; b.classList.toggle('is-on', on); b.setAttribute('aria-selected', on ? 'true' : 'false'); b.tabIndex = on ? 0 : -1; }
        if (!quiet && prev !== id) { uiSfx('ui-tab'); if (onChange) onChange(id, prev); }
      },
      setBadge(id, v) { const b = btns.get(id); if (b) UI.badge(b, v); },
      step(d) {
        const ids = Array.from(btns.keys()).filter(k => !btns.get(k).disabled);
        const i = ids.indexOf(cur);
        api.set(ids[(i + d + ids.length) % ids.length]);
        const b = btns.get(cur); if (b) { try { b.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ } }
      },
      ids() { return Array.from(btns.keys()); },
    };
    for (const it of items) {
      const b = el('button', 'sg-tab' + (it.disabled ? ' is-disabled' : ''));
      b.type = 'button';
      b.setAttribute('role', 'tab');
      b.dataset.tab = it.id;
      b.innerHTML = (it.icon ? iconHtml(it.icon) : '') + '<span class="sg-tab-l">' + it.label + '</span>';
      if (it.disabled) b.disabled = true;
      b.addEventListener('pointerdown', () => uiSfx('ui-press-paper', 0.6));
      b.addEventListener('click', () => api.set(it.id));
      b.addEventListener('keydown', e => {
        const k = e.key;
        if (k === 'ArrowRight' || (opts.vertical && k === 'ArrowDown')) { e.preventDefault(); e.stopPropagation(); api.step(1); }
        else if (k === 'ArrowLeft' || (opts.vertical && k === 'ArrowUp')) { e.preventDefault(); e.stopPropagation(); api.step(-1); }
      });
      if (it.badge) UI.badge(b, it.badge);
      btns.set(it.id, b);
      bar.appendChild(b);
    }
    if (parent) parent.appendChild(bar);
    const first = opts.value !== undefined ? opts.value : items.length ? items[0].id : null;
    if (first !== null) api.set(first, true);
    return api;
  };

  // ---- 分段 木牌：options = [[value, label], …] ----
  UI.segmented = function (parent, options, value, onChange) {
    const w = el('div', 'sg-seg');
    w.setAttribute('role', 'radiogroup');
    const btns = [];
    let cur = value;
    const set = (v, quiet) => {
      cur = v;
      for (const b of btns) { const on = b._v === v; b.classList.toggle('is-on', on); b.setAttribute('aria-checked', on ? 'true' : 'false'); b.tabIndex = on ? 0 : -1; }
      if (!quiet && onChange) onChange(v);
    };
    for (const o of options) {
      const [v, label] = Array.isArray(o) ? o : [o, o];
      const b = el('button', 'sg-seg-o', SG.esc(String(label)));
      b.type = 'button'; b._v = v;
      b.setAttribute('role', 'radio');
      b.addEventListener('pointerdown', () => uiSfx('ui-tab', 0.8));
      b.addEventListener('click', () => { if (cur !== v) set(v); });
      b.addEventListener('keydown', e => {
        const i = btns.indexOf(b);
        if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
          e.preventDefault(); e.stopPropagation();
          const nb = btns[(i + (e.key === 'ArrowRight' ? 1 : -1) + btns.length) % btns.length];
          uiSfx('ui-tab', 0.8); set(nb._v); try { nb.focus({ preventScroll: true }); } catch (err) { /* 忽略 */ }
        }
      });
      btns.push(b); w.appendChild(b);
    }
    set(value, true);
    w.set = v => set(v, true);
    w.get = () => cur;
    if (parent) parent.appendChild(w);
    return w;
  };

  // ---- 印章开关：关 = 空印框，开 = 朱印 ----
  UI.toggle = function (parent, value, onChange, opts) {
    opts = opts || {};
    const t = el('button', 'sg-toggle');
    t.type = 'button';
    t.setAttribute('role', 'switch');
    if (opts.label) t.setAttribute('aria-label', opts.label);
    let on = !!value;
    const draw = () => { t.classList.toggle('is-on', on); t.setAttribute('aria-checked', on ? 'true' : 'false'); t.innerHTML = '<span class="sg-toggle-seal">' + (on ? '开' : '关') + '</span>'; };
    t.addEventListener('click', () => { on = !on; draw(); uiSfx(on ? 'ui-toggle-on' : 'ui-toggle-off'); if (onChange) onChange(on); });
    draw();
    t.set = v => { on = !!v; draw(); };
    t.get = () => on;
    if (parent) parent.appendChild(t);
    return t;
  };

  // ---- 玉珠滑杆：{ min, max, step, value, format, onInput, onCommit, preview, label } ----
  UI.slider = function (parent, o) {
    o = o || {};
    const min = o.min === undefined ? 0 : o.min, max = o.max === undefined ? 100 : o.max, step = o.step || 1;
    const fmt = o.format || (v => String(Math.round(v)));
    const w = el('div', 'sg-slider');
    const groove = el('div', 'sg-slider-groove');
    groove.innerHTML = '<i class="sg-slider-fill"></i><i class="sg-slider-ticks"></i><i class="sg-slider-knob" role="slider" tabindex="0"></i>';
    const knob = groove.querySelector('.sg-slider-knob');
    const val = el('span', 'sg-slider-val');
    w.appendChild(groove); w.appendChild(val);
    if (o.label) knob.setAttribute('aria-label', o.label);
    knob.setAttribute('aria-valuemin', String(min)); knob.setAttribute('aria-valuemax', String(max));
    let v = M.clamp(o.value === undefined ? min : o.value, min, max);
    let lastTick = -1;
    const draw = () => {
      const p = max > min ? (v - min) / (max - min) : 0;
      w.style.setProperty('--v', p.toFixed(4));
      val.textContent = fmt(v);
      knob.setAttribute('aria-valuenow', String(v));
      const tick = Math.floor(p * 10 + 1e-6);
      if (lastTick >= 0 && tick !== lastTick) uiSfx('ui-bead', 0.8);
      lastTick = tick;
    };
    const setV = (nv, commit) => {
      nv = M.clamp(Math.round(nv / step) * step, min, max);
      if (nv !== v) { v = nv; draw(); if (o.onInput) o.onInput(v); }
      if (commit && o.onCommit) o.onCommit(v);
    };
    let drag = false;
    const fromX = x => { const r = groove.getBoundingClientRect(); return min + (max - min) * M.clamp01((x - r.left) / Math.max(1, r.width)); };
    groove.addEventListener('pointerdown', e => {
      drag = true;
      try { groove.setPointerCapture(e.pointerId); } catch (err) { /* 忽略 */ }
      setV(fromX(e.clientX), false);
    });
    groove.addEventListener('pointermove', e => { if (drag) setV(fromX(e.clientX), false); });
    const end = () => { if (!drag) return; drag = false; if (o.onCommit) o.onCommit(v); if (o.preview) { try { o.preview(v); } catch (e) { /* 忽略 */ } } };
    groove.addEventListener('pointerup', end); groove.addEventListener('pointercancel', end);
    knob.addEventListener('keydown', e => {
      const big = (max - min) / 10;
      let d = 0;
      if (e.key === 'ArrowRight' || e.key === 'ArrowUp') d = step;
      else if (e.key === 'ArrowLeft' || e.key === 'ArrowDown') d = -step;
      else if (e.key === 'PageUp') d = big;
      else if (e.key === 'PageDown') d = -big;
      else if (e.key === 'Home') { e.preventDefault(); e.stopPropagation(); setV(min, true); return; }
      else if (e.key === 'End') { e.preventDefault(); e.stopPropagation(); setV(max, true); return; }
      if (!d) return;
      e.preventDefault(); e.stopPropagation();
      setV(v + d, true);
    });
    draw();
    w.set = nv => { v = M.clamp(nv, min, max); lastTick = -1; draw(); };
    w.get = () => v;
    if (parent) parent.appendChild(w);
    return w;
  };

  // ---- 计数器（UI-V4 §4.6）：数字滚动 + 增减签 + 图标弹跳；250 ms 内的变化合并 ----
  UI.counter = function (target, o) {
    o = o || {};
    const fmt = o.format || (v => Math.round(v).toLocaleString('en-US'));
    let shown = o.value || 0, goal = shown, raf = 0, t0 = 0, from = 0, dur = 0, pending = 0, pendT = 0, tagEl = null;
    const num = el('span', 'sg-counter-n', fmt(shown));
    target.classList.add('sg-counter');
    if (o.icon) target.insertAdjacentHTML('afterbegin', iconHtml(o.icon));
    target.appendChild(num);
    const ease = x => 1 - Math.pow(1 - x, 3);
    let lastWrite = 0;
    function frame(now) {
      const k = Math.min(1, (now - t0) / dur);
      const v = from + (goal - from) * ease(k);
      if (k >= 1 || now - lastWrite > 33) { num.textContent = fmt(k >= 1 ? goal : v); lastWrite = now; }
      if (k < 1) raf = requestAnimationFrame(frame); else { raf = 0; shown = goal; }
    }
    function tag(delta) {
      if (!delta) return;
      const now = performance.now();
      if (tagEl && now - pendT < 250) { pending += delta; tagEl.remove(); } else pending = delta;
      pendT = now;
      const t = tagEl = el('span', 'sg-delta ' + (pending > 0 ? 'up' : 'down'), (pending > 0 ? '+' : '−') + Math.abs(Math.round(pending)).toLocaleString('en-US'));
      target.appendChild(t);
      if (Motion.reduced || Motion.level === 'off' || !t.animate) { setTimeout(() => t.remove(), 1200); return; }
      const a = t.animate([{ opacity: 0, transform: 'translate(-50%, 4px)' }, { opacity: 1, transform: 'translate(-50%, -6px)', offset: 0.15 },
        { opacity: 1, transform: 'translate(-50%, -14px)', offset: 0.7 }, { opacity: 0, transform: 'translate(-50%, -18px)' }], { duration: 1200, easing: 'linear' });
      a.onfinish = () => t.remove();
      const ic = target.querySelector('svg');
      if (ic && ic.animate) ic.animate([{ transform: 'scale(1)' }, { transform: 'scale(1.18)' }, { transform: 'scale(1)' }], { duration: 240, easing: 'ease-out' });
    }
    return {
      el: target,
      get value() { return goal; },
      set(v, opts2) {
        opts2 = opts2 || {};
        v = +v || 0;
        if (v === goal) return;
        const delta = v - goal;
        if (opts2.delta !== false) {
          tag(delta);
          if (o.sound !== false) uiSfx(delta > 0 ? (o.sound || 'ui-coins') : (o.soundOut || 'ui-coin-out'), 0.8);
        }
        from = raf ? +String(num.textContent).replace(/[^\d.-]/g, '') || shown : shown;
        goal = v;
        if (Motion.reduced || Motion.level === 'off' || opts2.instant) { shown = v; num.textContent = fmt(v); if (raf) cancelAnimationFrame(raf); raf = 0; return; }
        dur = 1000 * M.clamp(0.35 + 0.12 * Math.log10(Math.abs(delta) + 1), 0.35, 0.9);
        t0 = performance.now();
        if (!raf) raf = requestAnimationFrame(frame);
      },
    };
  };

  // ---- 立绘框（位阶 R1–R5，UI-V4 §4.6）与物品槽 ----
  // 位阶：君主按国势位阶（SG.Titles.rankOf / faction.rank），有官职的按品阶（1–5），其余 R1
  UI.rankOf = function (gen) {
    if (!gen) return 1;
    const g = SG.G;
    try {
      if (g && gen.faction >= 0 && g.isRuler && g.isRuler(gen)) {
        const f = g.factions[gen.faction];
        const r = SG.Titles && SG.Titles.rankOf ? SG.Titles.rankOf(f) : f && f.rank;
        const n = typeof r === 'object' && r ? (r.level || r.rank || r.r) : r;
        if (n >= 1) return Math.max(1, Math.min(5, n | 0));
        return 2;
      }
      const o = gen.office;
      if (o) { const t = typeof o === 'object' ? (o.tier || o.t) : null; if (t >= 1) return Math.max(1, Math.min(5, t | 0)); }
    } catch (e) { /* 忽略 */ }
    return 1;
  };
  // HTML：头像外加位阶框。opts：{ size（px）, rank, mood, round, cls }
  UI.frame = function (gen, opts) {
    opts = opts || {};
    const size = opts.size || UI.facePx(2.5);
    const rank = opts.rank || UI.rankOf(gen);
    let img = '';
    const P = SG.Portrait;
    const gg = genOf(gen);
    if (P && gg) { try { img = P.html(gg, { size: Math.round(size), mood: opts.mood, className: 'sg-frame-img' }); } catch (e) { img = ''; } }
    if (!img) img = '<span class="sg-frame-glyph">' + SG.esc(gg && gg.name ? gg.name.charAt(0) : '?') + '</span>';
    return '<span class="sg-frame r' + rank + (opts.round ? ' round' : '') + (opts.cls ? ' ' + opts.cls : '') + '" style="--s:' + Math.round(size) + 'px">' + img + '</span>';
  };
  const RARITY = ['common', 'uncommon', 'rare', 'epic', 'legend'];
  const SLOT_ICON = { weapon: 'guandao', offhand: 'shield', armour: 'armour', helmet: 'helmet', mount: 'horse', treasure: 'book' };
  const SLOT_ZH = { weapon: '兵器', offhand: '副手', armour: '铠甲', helmet: '头盔', mount: '坐骑', treasure: '宝物' };
  UI.SLOT_ICON = SLOT_ICON; UI.SLOT_ZH = SLOT_ZH;
  UI.rarityClass = function (r) {
    if (typeof r === 'number') return RARITY[Math.max(0, Math.min(4, r))];
    return r === 'legendary' ? 'legend' : RARITY.indexOf(r) >= 0 ? r : 'common';
  };
  // HTML：物品槽。item：装备条目（{ id, rarity, name }）或 null（空槽）；opts：{ size, slot, label, cls }
  UI.slot = function (item, opts) {
    opts = opts || {};
    const size = opts.size || 48;
    if (!item) {
      return '<span class="sg-slot empty' + (opts.cls ? ' ' + opts.cls : '') + '" style="--s:' + size + 'px">' +
        (opts.slot ? '<span class="sg-slot-ghost">' + iconHtml(SLOT_ICON[opts.slot] || 'help') + '</span>' : '') +
        (opts.label ? '<span class="sg-slot-lbl">' + SG.esc(opts.label) + '</span>' : '') + '</span>';
    }
    let ic = '';
    try { if (SG.ShopScreen && SG.ShopScreen.icon) ic = SG.ShopScreen.icon(item.id, Math.round(size * 0.78)); } catch (e) { ic = ''; }
    if (!ic) ic = iconHtml(SLOT_ICON[opts.slot || item.slot] || 'help');
    return '<span class="sg-slot ' + UI.rarityClass(item.rarity) + (opts.cls ? ' ' + opts.cls : '') + '" style="--s:' + size + 'px">' + ic +
      (opts.label ? '<span class="sg-slot-lbl">' + SG.esc(opts.label) + '</span>' : '') + '</span>';
  };
  // HTML：墨笔进度条。v 0..1；opts：{ gear: 金色斜纹的长度 0..1, cls, h }
  UI.brushBar = function (v, color, opts) {
    opts = opts || {};
    return '<span class="sg-bbar' + (opts.cls ? ' ' + opts.cls : '') + '" style="--v:' + M.clamp01(+v || 0).toFixed(3) + ';--c:' + (color || 'var(--war)') +
      (opts.gear ? ';--g:' + M.clamp01(opts.gear).toFixed(3) : '') + '"><i></i>' + (opts.gear ? '<b></b>' : '') + '</span>';
  };

  // ---- 窗口（漆框 + 纸页，UI-V4 §4.3）----
  // SG.UI.window(id, { title, seal, info, icon, width, height, onClose, tabs, tabValue, onTab, className, full })
  //   → { el, head, info, tabs, page, side, foot, close(), onKey, setInfo(html) }
  //   Esc 关闭（onKey 返回 true 表示已处理）；← → / Ctrl+Tab 切换页签；手机全屏；打开时 ui-paper-open，关闭 ui-paper-close
  UI.window = function (id, o) {
    o = o || {};
    ensure();
    const m = openModal('sg-window' + (o.className ? ' ' + o.className : ''), { blockerClass: 'sg-window-modal' });
    m.panel.classList.remove('sg-panel');
    m.panel.dataset.win = id || '';
    if (o.width) m.panel.style.setProperty('--ww', typeof o.width === 'number' ? o.width + 'px' : o.width);
    if (o.height) m.panel.style.setProperty('--wh', typeof o.height === 'number' ? o.height + 'px' : o.height);
    if (o.title) m.panel.setAttribute('aria-label', String(o.title).replace(/<[^>]*>/g, ''));
    const head = el('div', 'sg-win-head');
    if (o.seal) head.appendChild(el('span', 'sg-win-seal', SG.esc(o.seal)));
    const title = el('div', 'sg-win-title', o.title || '');
    head.appendChild(title);
    const info = el('div', 'sg-win-info', o.info || '');
    head.appendChild(info);
    const tools = el('div', 'sg-win-tools');
    head.appendChild(tools);
    let closed = false;
    const rec = {
      el: m.panel, modal: m, head, title, info, tools, tabs: null, page: null, side: null, foot: null, onKey: null,
      setInfo(h) { info.innerHTML = h || ''; },
      close() {
        if (closed) return;
        closed = true;
        uiSfx('ui-paper-close', 0.8);
        m.close();
        if (o.onClose) { try { o.onClose(); } catch (e) { console.error(e); } }
      },
      get closed() { return closed; },
    };
    UI.button(head, '', () => rec.close(), { className: 'sg-close', ariaLabel: '关闭' });
    m.panel.appendChild(head);
    const body = el('div', 'sg-win-body' + (o.sideFirst ? ' side-first' : ''));
    m.panel.appendChild(body);
    if (o.side) { rec.side = el('div', 'sg-win-side'); body.appendChild(rec.side); }
    const pageWrap = el('div', 'sg-win-main');
    body.appendChild(pageWrap);
    if (o.tabs) rec.tabs = UI.tabs(pageWrap, o.tabs, (tid, prev) => { if (o.onTab) o.onTab(tid, prev); }, { value: o.tabValue, className: 'sg-win-tabs' });
    rec.page = el('div', 'sg-page' + (o.tabs ? ' has-tabs' : ''));
    pageWrap.appendChild(rec.page);
    if (o.foot) { rec.foot = el('div', 'sg-win-foot'); m.panel.appendChild(rec.foot); }
    m.onKey = (k, e) => {
      if (rec.onKey) { try { if (rec.onKey(k, e) === true) return true; } catch (err) { console.error(err); } }
      if (k === 'Escape') { rec.close(); return true; }
      if (rec.tabs && (k === 'ArrowLeft' || k === 'ArrowRight')) {
        const a = document.activeElement;
        if (a && (a.tagName === 'INPUT' || (a.closest && a.closest('.sg-seg, .sg-slider, .sg-tabs')))) return false;
        rec.tabs.step(k === 'ArrowRight' ? 1 : -1);
        return true;
      }
      return false;
    };
    uiSfx('ui-paper-open', 0.8);
    if (!Motion.reduced && Motion.level !== 'off') Motion.play(m.panel, [{ opacity: 0, transform: 'scale(.96) translateY(12px)' }, { opacity: 1, transform: 'none' }], { dur: 260 });
    try { const c = head.querySelector('.sg-close'); if (c) c.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ }
    return rec;
  };

  // ---- 手机底部抽屉（UI-V4 §4.3）：{ title, html, onClose } → { el, body, close } ----
  UI.sheet = function (o) {
    o = o || {};
    ensure();
    const m = openModal('sg-sheet', { blockerClass: 'sg-sheet-modal' });
    m.panel.classList.remove('sg-panel');
    m.panel.innerHTML = '<div class="sg-sheet-grab"></div>';
    if (o.title) m.panel.appendChild(el('div', 'sg-sheet-title', o.title));
    const body = el('div', 'sg-sheet-body', o.html || '');
    m.panel.appendChild(body);
    let closed = false;
    const rec = { el: m.panel, body, close() { if (closed) return; closed = true; m.close(); if (o.onClose) o.onClose(); } };
    m.blocker.addEventListener('click', e => { if (e.target === m.blocker) rec.close(); });
    m.onKey = k => { if (k === 'Escape') { rec.close(); return true; } return false; };
    let y0 = null;
    m.panel.addEventListener('pointerdown', e => { if (e.target.classList.contains('sg-sheet-grab') || e.target === m.panel) y0 = e.clientY; });
    m.panel.addEventListener('pointerup', e => { if (y0 !== null && e.clientY - y0 > 40) rec.close(); y0 = null; });
    uiSfx('ui-paper-open', 0.7);
    return rec;
  };

  SG.UI = UI;
})();
