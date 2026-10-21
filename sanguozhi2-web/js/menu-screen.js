'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 菜单（UI-V4 §6）
   漆框窗口，左栏书签式导航：继续 · 记录 · 读取 · 卷宗 · 设置 · 操作说明 · 返回标题；四个存档位（月初自动 + 三份手动），
   每张存档卡：地图缩略图、君主立绘、剧本 · 年月、城 · 将 · 金、记录时间 · 游玩时长；旧版（第三版 / 第二版）记录在「读取」页
   以「旧版记录」卡出现，经原有的迁移读入。Ctrl / ⌘ + S 快速记录到最近一次手动记录的档位（缺省「一」）。
   存档的读写一律经由 GameState 的存档位接口（js/model.js：saveSlot / loadSlot / listSaves / deleteSlot）。

   SG.MenuScreen.open(page?, opts?) → Promise   page: 'save' | 'load' | 'dossier' | 'help'（缺省 'save'；在标题画面打开时缺省 'load'）
       opts.fromTitle：标题画面（没有进行中的局面：只有 读取 · 设置 · 操作说明）；读取成功时 Promise 兑现为 { loaded: GameState }
   SG.MenuScreen.quickSave() → bool       SG.MenuScreen.keyCard() → Promise（快捷键一览）    SG.MenuScreen.isOpen()
   SG.MenuScreen.autoSave(g?) → { ok, error }   「自动」档（月初、返回标题前），带地图缩略图     SG.MenuScreen.thumbnail() → dataURL | null
   SG.MenuScreen.help(chapter?) → Promise （操作说明窗口）
   SG.SaveSlots（纯逻辑，tests/saves.js）：
     cards(list?) → [{ slot, label, empty, entry, legacy }]（自动 + 一二三，另有旧版记录时追加）
     save(g, slot, { thumb }) → { ok, error }   quickSlot() / setQuickSlot(slot)   label(slot)   fmtPlay(ms)   fmtTime(ts, now)
     storageOk() → bool    scenarioShort(id)
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = (W.SG = W.SG || {});
  const esc = s => (SG.esc ? SG.esc(String(s === undefined || s === null ? '' : s)) : String(s));

  // ================================================================ SG.SaveSlots（纯逻辑）==
  const UI_KEY = 'sanguozhi2_ui_v4';
  const LABEL = { auto: '自动', 1: '一', 2: '二', 3: '三', legacy: '旧版' };
  function storage() { try { return typeof localStorage !== 'undefined' && localStorage ? localStorage : null; } catch (e) { return null; } }
  function readUi() { const st = storage(); if (!st) return {}; try { const o = JSON.parse(st.getItem(UI_KEY) || '{}'); return o && typeof o === 'object' ? o : {}; } catch (e) { return {}; } }
  function writeUi(k, v) { const st = storage(); if (!st) return; try { const o = readUi(); o[k] = v; st.setItem(UI_KEY, JSON.stringify(o)); } catch (e) { /* 隐私模式 */ } }
  let quickMem = null;
  const SaveSlots = {
    SLOTS: ['auto', 1, 2, 3],
    label(slot) { return LABEL[slot] || String(slot); },
    storageOk() {
      const st = storage();
      if (!st) return false;
      try { const k = '__sg_probe'; st.setItem(k, '1'); st.removeItem(k); return true; } catch (e) { return false; }
    },
    list() { const GS = SG.GameState; try { return GS && GS.listSaves ? GS.listSaves() : []; } catch (e) { return []; } },
    // 自动 + 一二三（空档也列出），另有旧版记录时追加一张
    cards(list) {
      list = list || SaveSlots.list();
      const out = SaveSlots.SLOTS.map(s => {
        const e = list.find(x => x && String(x.slot) === String(s)) || null;
        return { slot: s, label: SaveSlots.label(s), empty: !e || !!e.broken, broken: !!(e && e.broken), entry: e };
      });
      const lg = list.find(x => x && x.slot === 'legacy');
      if (lg) out.push({ slot: 'legacy', label: '旧版', empty: false, legacy: true, entry: lg });
      return out;
    },
    quickSlot() { if (quickMem) return quickMem; const v = readUi().lastSlot; return v >= 1 && v <= 3 ? v : 1; },
    setQuickSlot(slot) { if (slot >= 1 && slot <= 3) { quickMem = slot; writeUi('lastSlot', slot); } },
    // 写入：playMs 与缩略图随索引；手动档记为「最近的手动档」（Ctrl+S 用）
    save(g, slot, opts) {
      opts = opts || {};
      const GS = SG.GameState;
      if (!g || !g.saveSlot) return { ok: false, error: '没有可记录的局面。' };
      let ok = false;
      try { ok = g.saveSlot(slot, { playMs: opts.playMs !== undefined ? opts.playMs : g.playMs | 0, thumb: opts.thumb || null }) !== false; } catch (e) { ok = false; }
      if (ok && slot !== 'auto') SaveSlots.setQuickSlot(+slot);
      return ok ? { ok: true, error: null } : { ok: false, error: (GS && GS.saveError) || '记录失败。' };
    },
    fmtPlay(ms) {
      const m = Math.max(0, Math.floor((ms || 0) / 60000));
      const h = Math.floor(m / 60);
      return h > 0 ? h + ' 小时 ' + (m % 60) + ' 分' : (m % 60) + ' 分';
    },
    fmtTime(ts, now) {
      if (!(ts > 0)) return '';
      const d = new Date(ts), n = new Date(now || Date.now());
      const hm = String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
      if (d.toDateString() === n.toDateString()) return '今天 ' + hm;
      const y = new Date(n); y.setDate(n.getDate() - 1);
      if (d.toDateString() === y.toDateString()) return '昨天 ' + hm;
      return (d.getFullYear() !== n.getFullYear() ? d.getFullYear() + '年' : '') + (d.getMonth() + 1) + '月' + d.getDate() + '日';
    },
    scenarioShort(id) { return id === 'world' ? '天下大势' : '董卓专横'; },
  };
  SG.SaveSlots = SaveSlots;
  if (typeof document === 'undefined') return;     // Node（tests/saves.js）只要上面的纯逻辑

  // ================================================================ 画面 ==
  const UI = () => SG.UI;
  const icon = (id, size) => (SG.UIIcons ? SG.UIIcons.svg(id, size) : '');
  const G = () => SG.G;
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  // 地图缩略图：300 × 172 JPEG（质量 0.7），隐藏界面后从 WebGL 画布截取（渲染后立刻读取）
  function thumbnail() {
    try {
      const Gfx = SG.Gfx;
      if (!Gfx || !Gfx.renderer || Gfx.topScreen()) return null;
      Gfx.render();
      const src = Gfx.renderer.domElement;
      const c = document.createElement('canvas');
      c.width = 300; c.height = 172;
      const k = Math.max(300 / src.width, 172 / src.height);
      const w = src.width * k, hh = src.height * k;
      c.getContext('2d').drawImage(src, (300 - w) / 2, (172 - hh) / 2, w, hh);
      const url = c.toDataURL('image/jpeg', 0.7);
      return url.length > 40 && url.length < 40000 ? url : null;
    } catch (e) { return null; }
  }
  function adviser(g) {
    const gs = g.generalsOf(g.player).filter(x => !x.dead);
    const cap = g.ruler(g.player) ? g.ruler(g.player).city : -1;
    const pool = gs.filter(x => x.city === cap && !g.isRuler(x));
    return (pool.length ? pool.reduce((a, b) => (b.intel > a.intel ? b : a)) : g.ruler(g.player)) || null;
  }

  const NAV = [
    ['continue', '继续', 'play'], ['save', '记录', 'save'], ['load', '读取', 'load'], ['dossier', '卷宗', 'dossier'],
    ['settings', '设置', 'settings'], ['help', '操作说明', 'help'],
  ];
  let cur = null;
  const MenuScreen = {
    isOpen() { return !!(cur && !cur.win.closed); },
    open(page, opts) {
      opts = opts || {};
      if (cur && !cur.win.closed) { cur.show(page || cur.page); return cur.promise; }
      const fromTitle = !!opts.fromTitle;
      const ss = SG.Game && SG.Game.strategy;
      if (!fromTitle && ss && (ss.busy || ss.endMonth) && page !== 'help') { UI().deny(null, '指令或月末处理进行中，请稍候再打开菜单。'); return Promise.resolve(null); }
      let resolveP;
      const promise = new Promise(r => { resolveP = r; });
      const win = UI().window('menu', { title: '菜　单', seal: '菜', info: fromTitle ? '' : 'Esc 返回游戏', width: 980, height: 600, side: true, foot: true, className: 'sg-menu', onClose: () => { removeEventListener('resize', moreHint); cur = null; resolveP(st.result || null); } });
      const st = cur = { win, page: null, sel: null, result: null, promise, fromTitle };
      const nav = h('nav', 'sg-menu-nav', null, win.side);
      const navBtns = {};
      for (const [id, label, ic] of NAV) {
        if (fromTitle && (id === 'continue' || id === 'save' || id === 'dossier')) continue;
        const b = UI().button(nav, label, () => {
          if (id === 'continue') { win.close(); return; }
          if (id === 'settings') { if (SG.SettingsScreen) SG.SettingsScreen.open(); return; }
          st.show(id);
        }, { kind: 'ghost', icon: ic, className: 'sg-menu-item', sound: 'paper' });
        navBtns[id] = b;
      }
      h('div', 'sg-menu-navsp', null, nav);
      if (!fromTitle) {
        UI().button(nav, '返回标题', () => MenuScreen.toTitle(), { kind: 'ghost', icon: 'gate', className: 'sg-menu-item is-exit', sound: 'paper' });
        h('div', 'sg-menu-dossier-note', '卷宗：图鉴 · 功业录 · 天下榜 · 大事记 · 国策 · 营建总览 · 商队探索 · 外交', nav);
      }
      // 矮屏的侧栏放不下七个书签时可上下滑动：没滑到底时底边淡出（「返回标题」在下面）
      function moreHint() { const sd = win.side; if (sd) sd.classList.toggle('can-more', sd.scrollHeight - sd.scrollTop - sd.clientHeight > 4); }
      win.side.addEventListener('scroll', moreHint, { passive: true });
      addEventListener('resize', moreHint);
      requestAnimationFrame(moreHint);
      // 页脚：剧本 · 势力 · 年月 · 已游玩
      const g = G();
      if (!fromTitle && g && g.playerFaction) {
        const pm = SG.Shell && SG.Shell.playMs ? SG.Shell.playMs() : g.playMs | 0;
        h('div', 'sg-note', esc(SaveSlots.scenarioShort(g.scenario)) + ' · ' + esc(g.playerFaction.name) + ' · ' + g.year + '年' + g.month + '月 · 已游玩 ' + SaveSlots.fmtPlay(pm) + '　<span class="sg-muted">存档第 4 版</span>', win.foot);
      } else h('div', 'sg-note', '存档存于本机浏览器', win.foot);
      h('div', 'sg-spacer', null, win.foot);
      st.footBtns = h('div', 'sg-menu-foot', null, win.foot);
      st.show = (id) => {
        st.page = id;
        for (const k of Object.keys(navBtns)) navBtns[k].classList.toggle('is-current', k === id);
        st.footBtns.innerHTML = '';
        win.page.innerHTML = '';
        win.page.scrollTop = 0;
        if (id === 'save' || id === 'load') MenuScreen._slots(st, id);
        else if (id === 'dossier') MenuScreen._dossier(st);
        else if (id === 'help') MenuScreen._help(st, win.page);
        if (SG.Motion) SG.Motion.play(win.page, [{ opacity: 0, transform: 'translateX(8px)' }, { opacity: 1, transform: 'none' }], { dur: 140 });
      };
      st.show(page || (fromTitle ? 'load' : 'save'));
      win.onKey = k => {
        if (k === 'ArrowUp' || k === 'ArrowDown') {
          const ids = Object.keys(navBtns).filter(x => x !== 'continue' && x !== 'settings');
          const i = ids.indexOf(st.page);
          st.show(ids[(i + (k === 'ArrowDown' ? 1 : -1) + ids.length) % ids.length]);
          UI().sfx('ui-tab');
          return true;
        }
        // Enter：键盘移到的导航签 / 存档卡片上 = 按下它；否则 = 页脚的朱印钮（记录于「一」/ 读取）——由 SG.UI.window 处理
        return false;
      };
      return promise;
    },

    // ---- 记录 / 读取 ----
    _slots(st, mode) {
      const win = st.win, page = win.page;
      const g = G();
      h('div', 'sg-menu-ph', '<span class="sg-h2">' + (mode === 'save' ? '记录进度' : '读取进度') + '</span><span class="sg-muted">' + (mode === 'save' ? '每月初自动记录一份；手动记录三份' : '自动记录与三份手动记录' + '') + '</span>', page);
      if (!SaveSlots.storageOk()) { h('div', 'sg-empty', '此浏览器不允许本地存档', page); return; }
      const grid = h('div', 'sg-slots', null, page);
      const cards = SaveSlots.cards();
      const now = Date.now();
      let sel = st.sel;
      if (sel === null || sel === undefined || !cards.some(c => String(c.slot) === String(sel) && (mode === 'save' ? c.slot !== 'legacy' && c.slot !== 'auto' : !c.empty))) {
        sel = mode === 'save' ? SaveSlots.quickSlot() : (cards.find(c => !c.empty) || {}).slot;
      }
      const els = [];
      const pick = (slot) => {
        sel = st.sel = slot;
        for (const e of els) e.classList.toggle('is-sel', String(e.dataset.slot) === String(slot));
        renderFoot();
      };
      for (const c of cards) {
        if (mode === 'save' && c.slot === 'legacy') continue;
        const e = c.entry || {};
        const card = h('div', 'sg-slotcard' + (c.empty ? ' is-empty' : '') + (c.slot === 'auto' ? ' is-auto' : '') + (c.legacy ? ' is-legacy' : ''), null, grid);
        card.dataset.slot = c.slot;
        card.tabIndex = 0;
        card.setAttribute('role', 'button');
        const usable = mode === 'save' ? (c.slot !== 'auto' && c.slot !== 'legacy') : !c.empty;
        if (!usable) card.classList.add('is-off');
        h('span', 'sg-slot-tag', esc(c.label), card);
        if (c.empty) {
          h('div', 'sg-slot-emptyl', c.broken ? '损坏的记录' : (c.label === '自动' ? '空 · 月初自动记录' : '空 · 第' + c.label + '份'), card);
        } else {
          const thumb = h('div', 'sg-slot-thumb', null, card);
          if (e.thumb) thumb.style.backgroundImage = 'url("' + e.thumb + '")';
          else thumb.innerHTML = icon(c.legacy ? 'load' : 'factions');
          const info = h('div', 'sg-slot-info', null, card);
          const ru = e.ruler ? { name: e.ruler } : null;
          info.innerHTML = '<div class="sg-slot-who">' + (ru ? UI().frame(ru, { size: 28 }) : '') + '<b class="sg-kai">' + esc(e.factionName || '') + '</b></div>' +
            '<div>' + esc(SaveSlots.scenarioShort(e.scenario)) + ' · <b class="sg-num">' + (e.year || '?') + '年' + (e.month || '?') + '月</b></div>' +
            (c.legacy ? '<div class="sg-muted">旧版记录（第' + (e.version === 2 ? '二' : '三') + '版）· 读取时升级</div>'
              : '<div class="sg-num">城 ' + (e.cities | 0) + ' · 将 ' + (e.generals | 0) + ' · 金 ' + (e.gold | 0).toLocaleString('en-US') + '</div>' +
                '<div class="sg-muted">' + (c.slot === 'auto' ? '月初自动' : '') + (e.savedAt ? (c.slot === 'auto' ? ' · ' : '') + SaveSlots.fmtTime(e.savedAt, now) : '') + (e.playMs ? ' · ' + SaveSlots.fmtPlay(e.playMs) : '') + '</div>');
        }
        const go = () => { if (!usable) { UI().deny(card, mode === 'save' ? (c.slot === 'auto' ? '「自动」档在每月初自动写入。' : '旧版记录只能读取。') : '这一份是空的。'); return; } UI().sfx('ui-press-paper'); pick(c.slot); };
        card.addEventListener('click', go);
        card.addEventListener('dblclick', () => { if (usable && st.primary) st.primary.click(); });
        card.addEventListener('keydown', ev => { if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); go(); } });
        els.push(card);
      }
      const renderFoot = () => {
        const F = st.footBtns;
        F.innerHTML = '';
        const c = cards.find(x => String(x.slot) === String(sel));
        const del = UI().button(F, '删除', async () => {
          if (!c || c.empty) return;
          const ok = await UI().confirm('删除「' + c.label + '」这份记录？', '删除', '取消', { body: '<span class="sg-bad">删除后无法恢复。</span>' });
          if (!ok) return;
          SG.GameState.deleteSlot(c.slot);
          st.show(mode);
        }, { kind: 'ghost' });
        if (!c || c.empty || (mode === 'save' && c.slot === 'auto')) del.setEnabled(false);
        if (mode === 'save') {
          st.primary = UI().button(F, c ? '记录于「' + c.label + '」' : '记录', () => MenuScreen._doSave(st, c), { kind: 'seal' });
          if (!c || c.slot === 'auto' || c.slot === 'legacy' || !G() || (SG.Game.strategy && SG.Game.strategy.busy)) st.primary.setEnabled(c ? (c.slot === 'auto' ? '「自动」档在每月初自动写入。' : '指令执行中，请稍候。') : false);
        } else {
          st.primary = UI().button(F, '读　取', () => MenuScreen._doLoad(st, c), { kind: 'seal' });
          if (!c || c.empty) st.primary.setEnabled(false);
        }
      };
      pick(sel);
    },
    async _doSave(st, c) {
      const g = G();
      if (!g || !c) return;
      if (!c.empty && c.entry) {
        const e = c.entry;
        const ok = await UI().confirm('覆盖「' + c.label + '」？', '覆　盖', '取消', {
          body: '<div class="sg-overwrite"><div><small class="sg-muted">原有</small><br>' + esc(e.factionName) + ' · ' + e.year + '年' + e.month + '月<br><span class="sg-muted">城 ' + (e.cities | 0) + ' · 将 ' + (e.generals | 0) + '</span></div><div class="sg-ow-arrow">→</div>' +
            '<div><small class="sg-muted">现在</small><br>' + esc(g.playerFaction.name) + ' · ' + g.year + '年' + g.month + '月<br><span class="sg-muted">城 ' + g.cityCount(g.player) + ' · 将 ' + g.generalsOf(g.player).length + '</span></div></div>',
        });
        if (!ok) return;
      }
      const r = MenuScreen._write(g, c.slot);
      if (r.ok) st.show('save');
    },
    _write(g, slot) {
      if (SG.Shell && SG.Shell.syncPlayMs) SG.Shell.syncPlayMs();
      const r = SaveSlots.save(g, slot, { thumb: thumbnail(), playMs: g.playMs | 0 });
      if (r.ok) {
        UI().toast('已记录于『' + SaveSlots.label(slot) + '』', { seal: '记', seconds: 2 });
        try { if (SG.Voice) SG.Voice.say(adviser(g), 'ui.save', { pri: 1, scene: 'strategy' }); } catch (e) { /* 无人声 */ }
      } else UI().toast(esc(r.error), { seal: '败', seconds: 3.4, kind: 'deny' });
      return r;
    },
    quickSave() {
      const g = G(), ss = SG.Game && SG.Game.strategy;
      if (!g || !g.playerFaction || (ss && (ss.busy || ss.endMonth))) { UI().toast('指令或月末处理进行中，稍后再记录。', { seal: '记', seconds: 2 }); return false; }
      return MenuScreen._write(g, SaveSlots.quickSlot()).ok;
    },
    async _doLoad(st, c) {
      if (!c || c.empty) return;
      if (!st.fromTitle) {
        const ok = await UI().confirm('读取「' + c.label + '」？', '读　取', '取消', { body: '当前局面中尚未记录的进度将会丢失。' });
        if (!ok) return;
        // 游戏中读档：记下要读的档位，重新载入页面（启动时直接读入，跳过标题）
        try { sessionStorage.setItem('sanguozhi2_boot_load', String(c.slot)); } catch (e) { /* 忽略 */ }
        location.reload();
        return;
      }
      let loaded = null;
      try { loaded = SG.GameState.loadSlot(c.slot); } catch (e) { loaded = null; }
      if (!loaded) { await UI().say(esc(SG.GameState.loadError || '存档无法读取。')); return; }
      st.result = { loaded, slot: c.slot };
      st.win.close();
    },
    async toTitle() {
      const ok = await UI().confirm('返回标题？', '返回标题', '取消', { body: '本月进度会先记录于「自动」。' });
      if (!ok) return;
      const g = G();
      try { if (g && g.saveSlot) MenuScreen.autoSave(g); } catch (e) { /* 忽略 */ }
      location.reload();
    },
    // 「自动」档（月初、返回标题前；UI-V4 §6）：与手动档一样带地图缩略图与游玩时长
    autoSave(g) {
      g = g || G();
      if (!g || !g.saveSlot) return { ok: false, error: '没有可记录的局面。' };
      if (SG.Shell && SG.Shell.syncPlayMs) SG.Shell.syncPlayMs();
      return SaveSlots.save(g, 'auto', { thumb: thumbnail(), playMs: g.playMs | 0 });
    },
    thumbnail() { return thumbnail(); },

    // ---- 卷宗 ----
    _dossier(st) {
      const page = st.win.page;
      h('div', 'sg-menu-ph', '<span class="sg-h2">卷　宗</span><span class="sg-muted">天下的记录与总览</span>', page);
      const grid = h('div', 'sg-dossier', null, page);
      const g = G();
      const items = SG.Items && SG.Items.enabled !== false && g && g.items;
      let book = '';
      try { if (items && SG.Items.collection) { const cl = SG.Items.collection(g.player); book = (cl.seen || 0) + ' / ' + (cl.total || 0); } } catch (e) { book = ''; }
      const T = [
        ['图鉴', 'book', items ? book || '装备图鉴' : null, () => SG.ShopScreen.open({ city: g.cities[g.ruler(g.player).city], tab: 'book' })],
        ['大事记', 'chronicle', g ? (g.log || []).length + ' 条' : '', () => SG.Game.strategy.chronicle()],
        ['天下势力', 'factions', g ? g.factions.filter(f => f.alive).length + ' 家' : '', () => SG.Game.strategy.factionInfo()],
        ['年历', 'calendar', g ? g.year + '年' + g.month + '月' : '', () => UI().notice('年　历', SG.Shell ? SG.Shell._yearStrip(g) : '')],
        ['官制', 'titles', SG.TitleUI ? '官位与人选' : null, () => SG.TitleUI.open()],
        ['功业录', 'level', null], ['天下榜', 'prestige', null], ['英雄榜', 'tourney', SG.TourneyScreen ? '历届武林大会' : null, () => SG.TourneyScreen.hall()],
        ['国策', 'missions', null], ['营建总览', 'build', null], ['商队 · 探索', 'caravan', null], ['外交', 'diplomacy', null],
      ];
      for (const [name, ic, status, fn] of T) {
        const on = status !== null && status !== undefined && fn;
        const t = h('button', 'sg-dtile' + (on ? '' : ' is-off'), icon(ic, 48) + '<b class="sg-kai">' + esc(name) + '</b><span>' + esc(on ? status : '尚未开放') + '</span>', grid);
        t.type = 'button';
        t.addEventListener('click', () => {
          if (!on) { UI().deny(t, '「' + name + '」随第四版的相应系统开放（尚未载入）。'); return; }
          UI().sfx('ui-press-paper');
          st.win.close();
          setTimeout(() => { try { fn(); } catch (e) { console.error(e); } }, 180);
        });
      }
    },

    // ---- 操作说明 ----
    _help(st, page, chapter) {
      const box = h('div', 'sg-help2', null, page);
      const nav = h('div', 'sg-help-nav', null, box);
      const body = h('div', 'sg-help-body', null, box);
      const show = id => {
        for (const b of nav.children) b.classList.toggle('is-on', b.dataset.ch === id);
        const c = HELP.find(x => x[0] === id) || HELP[0];
        body.innerHTML = '<div class="sg-help-h"><span class="sg-sealmark">' + esc(c[1].charAt(0)) + '</span><span class="sg-h2">' + esc(c[1]) + '</span></div>' + (typeof c[2] === 'function' ? c[2]() : c[2]);
        body.scrollTop = 0;
      };
      for (const c of HELP) {
        const b = h('button', 'sg-help-ch', esc(c[1]), nav);
        b.type = 'button';
        b.dataset.ch = c[0];
        b.addEventListener('click', () => { UI().sfx('ui-page'); show(c[0]); });
      }
      show(chapter || HELP[0][0]);
    },
    help(chapter) {
      const win = UI().window('help', { title: '操作说明', seal: '说', width: 900, height: 600, className: 'sg-menu' });
      MenuScreen._help({ win }, win.page, chapter);
      return new Promise(r => { const t = setInterval(() => { if (win.closed) { clearInterval(t); r(); } }, 200); });
    },
    keyCard() {
      return UI().notice('快捷键一览', keyTable(), { width: 760, className: 'sg-keycard' });
    },
  };

  // ---- 操作说明的章节 ----
  const P = s => '<p>' + s + '</p>';
  const kbd = s => '<kbd>' + esc(s) + '</kbd>';
  function keyTable() {
    const rows = [
      ['G · T · P · H · O', '武将 · 官制 · 势力 · 大事 · 设置'], ['Esc', '关闭最上层的窗口；没有窗口时打开菜单'], ['Enter', '窗口里的主要按钮；地图上 = 结束本月'],
      ['1–9 0 - =  Z X C V', '城池面板的十六个指令（按指令坞的行）'], ['M', '静音 / 取消静音'], ['Ctrl / ⌘ + S', '快速记录（最近一次手动记录的档位）'],
      ['← →  Ctrl + Tab', '切换页签'], ['[  ]', '人物菜单：上一位 / 下一位'], ['W A S D  Q E', '地图：平移、旋转'], ['↑ ↓  1–9  Enter  Space', '对话框中的列表'],
    ];
    return '<table class="sg-keys">' + rows.map(r => '<tr><td>' + r[0].split('  ').map(kbd).join(' ') + '</td><td>' + esc(r[1]) + '</td></tr>').join('') + '</table>';
  }
  function duelHelp() {
    const touch = !!SG.isTouch;
    let keys = { L: 'J', M: 'K', H: 'L' };
    try { if (SG.DuelGame && SG.DuelGame.keyLabels) keys = SG.DuelGame.keyLabels(SG.DuelGame.settings()); } catch (e) { /* 缺省 */ }
    return touch ? P('左下摇杆移动：<b>向后</b>即格挡，向下为蹲，向下 + 向后为下段格挡。右下按钮：<b>轻 ● · 中 ◆ · 重 ▲</b>（按住蓄力），轻 + 重同按为跃劈；<b>格开</b>在来招前一瞬轻点，成功后反击。') +
      P('每名武将有自己的八式连段（轻 · 中 · 重），第八式是有名字的终结技；从第三式起，对手可在受击中格开打断。')
      : P(kbd('A') + ' ' + kbd('D') + ' / ' + kbd('←') + ' ' + kbd('→') + ' 移动：<b>后退即格挡</b>，' + kbd('S') + ' 蹲，' + kbd('S') + ' + 后退为下段格挡，' + kbd('W') + ' / 空格 跳。') +
        P(kbd(keys.L) + ' 轻 ● · ' + kbd(keys.M) + ' 中 ◆ · ' + kbd(keys.H) + ' 重 ▲（按住蓄力）；' + kbd(keys.M) + ' + ' + kbd(keys.H) + ' 跃劈；' + kbd('U') + ' 格开（来招前一瞬）；' + kbd('I') + ' 绝技（怒气满）。') +
        P('每名武将有自己的八式连段，第八式是有名字的终结技；从第三式起，对手可在受击中格开打断。会心一击伤害 ×1.5。');
  }
  const HELP = [
    ['intro', '入门', P('你是一方君主。每月有若干枚<b>令牌</b>（城池越多越多），每下达一条指令用掉一枚；用完了就点右下的朱印<b>「结束本月」</b>，诸侯行动，进入下个月。') +
      P('顶梁上：左边是君主章与势力匾，中间是年历、令箭筒（剩余令牌）与金 · 粮 · 城，右边是铜钮 <b>武将 · 官制 · 势力 · 大事 · 设置 · 菜单</b>。') +
      P('左侧的<b>案头</b>列出本月值得做的事，点一下就到。灰色的按钮点一下会说明为什么不能用。')],
    ['map', '地图与城池', P('拖动平移地图，滚轮或双指缩放，' + kbd('Q') + ' ' + kbd('E') + ' 旋转。点城池打开<b>城池面板</b>：城名、城级、开发度（土地 · 产业 · 町）、金粮兵防、城中武将与指令坞。') +
      P('他国城池只能观看；与我方城池相邻时可从这里出征或外交。')],
    ['cmds', '指令', P('指令坞四行十六个指令：<b>治</b> 开发 营建 征兵 训练 · <b>人</b> 搜索 登用 赏赐 任命 · <b>运</b> 移动 输送 交易 市场 · <b>策</b> 通商 探索 外交 出征。右上角有金点的指令用一枚令牌。') +
      P('<b>移动 / 输送</b>：经由己方城池相连的城都可前往，不必相邻。<b>出征</b>：攻打相邻的敌城或空城，可亲自指挥或委任。')],
    ['gens', '武将与成长', P('武将参战获得经验、升级；武力越高可学越多、越强的<b>必杀</b>，智力越高可学越多、越强的<b>计策</b>。升级后从三个候选中选择，也可在人物菜单里「习得新招」。') +
      P('点城池面板里的武将行，或顶梁的「武将」，打开<b>人物菜单</b>：总览 · 装备 · 武艺 · 计略 · 部队 · 官爵 · 关系 · 履历。')],
    ['gear', '装备与市场', P('每座城都有市场（城越大货越好）。在「市场」买卖兵器、甲胄、坐骑与宝物；在人物菜单的「装备」页为武将换装。珍品以上各有不可学的「神兵之能」。') +
      P('赐宝物能提高忠诚，但每件宝物只涨一次。专属兵器只有斩杀其君主才能得到。')],
    ['titles', '官制', P('君主可以授官：官位按武将最高的属性分为武官 · 谋臣 · 文官三系，各五品。官位给战斗、内政或外交的加成，也提高忠诚。顶梁「官制」打开官制总览。')],
    ['battle', '战斗', P('点选部队移动，相邻时可攻击、施展策略或单挑。击败敌军主将或攻入本城即获胜，三十日内未能攻下则撤退。军队攻击可能<b>会心</b>（伤害 ×1.5）。')],
    ['duel', '单挑', duelHelp],
    ['tourney', '武林大会', P('每年正月，天下诸侯各遣至多五名勇士赴会比武：单败淘汰，决赛三局两胜，另有季军战。前三名得金与奖品，冠军另得声望。')],
    ['keys', '快捷键', () => (SG.isTouch ? P('触屏：长按任何按钮显示名称与说明；左滑案头纸签可收起。') : '') + keyTable()],
  ];
  SG.MenuScreen = MenuScreen;
})();
