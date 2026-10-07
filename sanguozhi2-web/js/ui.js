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
    return UI;
  };
  function ensure() { if (!inited) UI.init(); }

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

  // 按钮：点击时播放 click 音效（与 UIKit.Button 相同）
  UI.button = function (parent, html, onClick, opts) {
    opts = opts || {};
    const b = el('button', 'sg-btn' + (opts.primary ? ' primary' : '') + (opts.small ? ' small' : '') + (opts.className ? ' ' + opts.className : ''), html);
    b.type = 'button';
    if (opts.title) b.title = opts.title;
    if (opts.ariaLabel) b.setAttribute('aria-label', opts.ariaLabel);
    if (opts.disabled) b.disabled = true;
    if (onClick) {
      b.addEventListener('click', e => {
        if (b.disabled) return;
        sfxClick();
        onClick(e);
      });
    }
    b.setLabel = function (h) { b.innerHTML = String(h); return b; };
    b.setEnabled = function (on) { b.disabled = !on; return b; };
    if (parent) parent.appendChild(b);
    return b;
  };

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

  UI.item = function (label, right, enabled, desc) {
    return { label, right: right === undefined ? null : right, enabled: enabled === undefined ? true : !!enabled, desc: desc === undefined ? null : desc, selected: false };
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
    const prevFocus = document.activeElement;
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
  // 把元素内所有文字拆为“已显示 + 隐藏占位”，版面从一开始就是完整大小
  function typewriter(root) {
    const texts = [];
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    let n;
    while ((n = walker.nextNode())) texts.push(n);
    const parts = [];
    let total = 0;
    for (const t of texts) {
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
  // 说话者徽章 + 文字（打字机），点击 / Enter / Space 继续
  UI.say = function (text, speaker, speakerColor) {
    ensure();
    if (speaker === undefined) speaker = null;
    if (!speakerColor) speakerColor = UI.colors.cinnabar;
    return new Promise(resolve => {
      const m = openModal('sg-say' + (speaker ? ' has-speaker' : ''), { blockerClass: 'sg-say-modal' });
      const panel = m.panel;
      if (speaker) {
        const sp = el('div', 'sg-say-speaker');
        // 有该武将时显示头像，否则退回姓氏徽章
        const pg = SG.Portrait && SG.Portrait.find(speaker);
        sp.appendChild(pg ? SG.Portrait.el(pg, { size: Math.round(96 * UI.scale()) })
                          : UI.medal(String(speaker).charAt(0), speakerColor, 96));
        sp.appendChild(el('div', 'sg-say-name', SG.esc(speaker)));
        panel.appendChild(sp);
      }
      const body = el('div', 'sg-say-text');
      const inner = el('div', 'sg-say-inner', text === null || text === undefined ? '' : String(text));
      body.appendChild(inner);
      panel.appendChild(body);
      panel.appendChild(el('div', 'sg-say-hint', '▼'));
      const tw = typewriter(inner);
      let full = false, done = false;
      function setFull() {
        if (full) return;
        full = true;
        tw.showAll();
        panel.classList.add('is-full');
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
      // 每帧 2 字（60 帧 / 秒 → 每秒 120 字）
      let shown = 2, last = performance.now();
      if (tw.total <= shown) setFull(); else tw.show(shown);
      function tick(now) {
        if (done || full) return;
        shown += Math.max(0, now - last) / 1000 * 120;
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
  UI.confirm = function (text, yes, no) {
    ensure();
    if (yes === undefined || yes === null) yes = '是';
    if (no === undefined || no === null) no = '否';
    return new Promise(resolve => {
      const m = openModal('sg-confirm', { width: 520 });
      let done = false;
      const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
      const head = el('div', 'sg-dlg-head');
      head.appendChild(el('div', 'sg-dlg-title', text));
      const close = UI.button(head, '×', () => finish(false), { className: 'sg-close', ariaLabel: '关闭' });
      m.panel.appendChild(head);
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

  const MAX_TOASTS = 6;
  UI.toast = function (html, seconds) {
    ensure();
    if (seconds === undefined || seconds === null) seconds = 2.2;
    const t = el('div', 'sg-toast', html);
    L.toastStack.appendChild(t);
    while (L.toastStack.childElementCount > MAX_TOASTS) L.toastStack.removeChild(L.toastStack.firstElementChild);
    const life = Math.max(0.3, seconds);
    let removed = false;
    const remove = () => { if (removed) return; removed = true; if (t.parentNode) t.parentNode.removeChild(t); };
    try {
      if (t.animate) {
        const kf = autoFadeFrames(life, false);
        kf[0].transform = 'translateY(-.6rem)'; kf[1].transform = 'none'; kf[2].transform = 'none'; kf[3].transform = 'translateY(-.3rem)';
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

  SG.UI = UI;
})();
