'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战略画面
   ← Strategy/StrategyScreen.cs：顶栏、城池情报、指令与月份循环
   协程 → async 函数。new SG.StrategyScreen().run() → Promise（游戏结束时重载页面）。
   第二版 §4F 调动武将：
     「移动」「输送」的目的地 = 经由己方城池相连可达的所有己方城（SG.Commands.moveTargets），
     列表注明路程（相邻 / 经 N 城）与途经城名，行军动画沿路线逐城前进；
     灰色指令按钮被点按时以提示说明原因（addCmd(name, why, …)、explainCmd）；
     第一次占领新城后弹一次「移动」提示并让「移动」按钮闪烁（moveTip / hintCmd，本机只弹一次）；
     只在「移动」此刻真能用时弹出（有令牌、某座己方城有未行动的武将可调），否则留到下个月月初。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const CINNABAR = '#c8382c';
  const GREY_SPEAKER = '#666673';               // C# new Color(0.4, 0.4, 0.45)
  const LABEL_HIDE_BEYOND = 170;                // C# WorldFollow.hideBeyond = 170
  const LABEL_FADE = 0.18;                      // 城名标签避让时的淡入淡出时长（秒）

  // 屏幕矩形 { l, t, r, b } 是否相交
  function overlaps(a, b) { return a.l < b.r && a.r > b.l && a.t < b.b && a.b > b.t; }

  function G() { return SG.G; }
  function UI() { return SG.UI; }
  function sfx(name, vol) { try { if (SG.Sfx) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频 */ } }
  function click() { try { if (SG.Sfx) SG.Sfx.click(); } catch (e) { /* 无音频 */ } }
  function music(kind) { try { if (SG.Sfx) SG.Sfx.music(kind); } catch (e) { /* 无音频 */ } }
  function item(label, right, enabled, desc) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right === undefined ? null : right, enabled === undefined ? true : enabled, desc === undefined ? null : desc);
    return { label, right: right === undefined ? null : right, enabled: enabled === undefined ? true : enabled, desc: desc === undefined ? null : desc, selected: false };
  }
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  // C# 的 {0:P0}
  function pct(p) { return Math.round(p * 100) + '%'; }
  // C# 的 {0:N0}
  function n0(v) { return Math.round(v).toLocaleString('en-US'); }
  function sum(list, fn) { let s = 0; for (const x of list) s += fn(x); return s; }
  function swatch(color) { return `<span class="sg-dot" style="color:${color}">■</span>`; }
  // 深色势力色作为文字时提亮，保证在漆黑面板上可读（标记 ■● 仍用原色）
  function readable(hex) {
    const c = SG.hexToRgb(hex);
    const L = 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    if (L >= 0.5) return hex;
    const k = M.clamp((0.5 - L) * 1.3, 0, 0.6);
    return SG.rgbToHex(c[0] + (1 - c[0]) * k, c[1] + (1 - c[1]) * k, c[2] + (1 - c[2]) * k);
  }
  function dot(color, glyph) { return `<span class="sg-dot" style="color:${color}">${glyph || '●'}</span>`; }

  // ---------------------------------------------------------- 调动武将（第二版 §4F）--
  // 路程文字：相邻 / 经 N 城（N = 途经的己方城数）
  function routeText(rt) { return rt.hops <= 1 ? '相邻' : '经 ' + (rt.hops - 1) + ' 城'; }
  // 途经城名（最多列 4 座）
  function viaText(g, rt) {
    if (rt.hops <= 1) return null;
    const mid = rt.path.slice(1, -1).map(i => SG.esc(g.cities[i].name));
    return '途经 ' + (mid.length > 4 ? mid.slice(0, 4).join('、') + ' 等 ' + mid.length + ' 城' : mid.join('、'));
  }
  // 第一次占领新城后的提示只弹一次（存于本机；无存储时本次会话内只弹一次）
  const TIP_KEY = 'sanguozhi2_tip_move';
  let tipShownMem = false;
  function tipShown() {
    if (tipShownMem) return true;
    try { return !!(window.localStorage && window.localStorage.getItem(TIP_KEY)); } catch (e) { return false; }
  }
  function markTipShown() {
    tipShownMem = true;
    try { if (window.localStorage) window.localStorage.setItem(TIP_KEY, '1'); } catch (e) { /* 忽略 */ }
  }
  // 灰色指令按钮不用原生 disabled（原生禁用的按钮收不到点按，触屏上轻点还会被“触摸校正”吸到相邻的可用按钮），
  // 而用 aria-disabled="true"：外观与 .sg-btn:disabled 相同，悬停 / 按下无反应，但能接住点按并说明原因。
  function injectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-strategy-cmd-style')) return;
    const st = document.createElement('style');
    st.id = 'sg-strategy-cmd-style';
    st.textContent = `
.sg-cmdgrid .sg-btn[aria-disabled="true"],
.sg-cmdgrid .sg-btn[aria-disabled="true"]:hover,
.sg-cmdgrid .sg-btn[aria-disabled="true"]:active {
  cursor: default;
  opacity: .5;
  color: rgba(245, 237, 219, .8);
  filter: grayscale(.6) brightness(.85);
  box-shadow: none;
  transform: none;
}
.sg-cmdgrid .sg-btn[aria-disabled="true"]:hover { border-color: rgba(243, 201, 105, .34); }
.sg-cmdgrid .sg-btn.primary[aria-disabled="true"]:hover { border-color: rgba(243, 201, 105, .9); }
.sg-cmdgrid .sg-btn.sg-cmd-deny { animation: sg-cmd-deny .38s ease; }
@keyframes sg-cmd-deny { 0%, 100% { translate: 0 0; } 20% { translate: -5px 0; } 40% { translate: 5px 0; } 60% { translate: -3px 0; } 80% { translate: 2px 0; } }
.sg-cmdgrid .sg-btn.sg-cmd-hint { animation: sg-cmd-hint 1.1s ease-in-out 4; }
.sg-cmdgrid .sg-btn.sg-cmd-hint.sg-cmd-deny { animation: sg-cmd-deny .38s ease; }
@keyframes sg-cmd-hint {
  0%, 100% { box-shadow: 0 0 0 0 rgba(243, 201, 105, 0); }
  50% { border-color: #f3c969; box-shadow: 0 0 0 2px rgba(243, 201, 105, .95), 0 0 1.4rem rgba(243, 201, 105, .65); }
}
.sg-toast .sg-cmd-name { color: #f3c969; font-weight: 700; margin-right: .5em; }`;
    document.head.appendChild(st);
  }

  function layer(name) {
    const ui = SG.UI;
    if (ui) {
      if (typeof ui.layer === 'function') { try { const l = ui.layer(name); if (l) return l; } catch (e) { /* 继续 */ } }
      if (ui.layers && ui.layers[name]) return ui.layers[name];
      if (ui[name] && ui[name].nodeType === 1) return ui[name];
    }
    const root = document.getElementById('ui') || document.body;
    return root.querySelector('[data-layer="' + name + '"], .sg-layer-' + name + ', #sg-' + name + ', .sg-' + name) || root;
  }

  // ========================================================== StrategyScreen --
  class StrategyScreen {
    constructor() {
      this.hud = null; this.cityPanel = null; this.cmdGrid = null; this.genList = null;
      this.topLeft = null; this.topCenter = null; this.cityTitle = null; this.cityStats = null;
      this.endBtn = null; this.saveBtn = null;
      this.labels = new Map();        // cityId → { el, handle, cv, w, h, fade, on, seen }
      this.selected = -1;
      this.busy = false;
      this.endMonth = false;
      this._hint = null;              // 正在闪烁的指令按钮 { name, until }
      this._tipPending = false;       // 已取得新城、「移动」提示待弹（等到「移动」可用时）
      this._tipCities = [];           // 新取得的城（提示时优先选中）
      this._onMapTap = (x, y) => this.onMapTap(x, y);
      // 城名标签避让
      this._labelsOn = true;
      this._occ = [];                 // 遮挡标签的界面矩形（顶栏、城池面板）
      this._occAt = -1e9; this._occDirty = true;
      this._sizeAt = -1e9; this._sizeDirty = true;
      this._declutterLast = 0;
      this._compact = false;
    }

    // ---------------------------------------------------------- 主循环 --
    async run() {
      const game = SG.Game;
      this.buildHud();
      game.rig.onTap.push(this._onMapTap);
      music('map');
      const cap = G().cities[G().ruler(G().player).city];
      const p = SG.mapPos(cap);
      game.rig.focusMap(p.x, p.y, 48);
      this.selectCity(cap.id);
      UI().banner(G().year + '年 ' + G().month + '月', G().playerFaction.name + '军');
      for (;;) {
        this.endMonth = false;
        this.refreshAll();
        while (!this.endMonth) await SG.frame();
        this.busy = true;
        this.refreshAll();
        UI().toast('诸侯行动中……', 1.2);
        await SG.wait(0.4);
        const news = [];
        const battles = SG.StrategyAI.runAI(news);
        game.map.refresh(G()); this.refreshAll();
        for (const b of battles) { await this.defend(b); if (!G().playerFaction.alive) break; }
        if (G().playerFaction.alive) for (const n of SG.StrategyAI.endMonth()) news.push(n);
        game.map.refresh(G()); this.refreshAll();
        if (!G().playerFaction.alive) { await this.gameOver(false); return; }
        if (G().cities.every(c => c.owner === G().player)) { await this.gameOver(true); return; }
        if (news.length > 0) await UI().say(news.slice(0, 6).join('\n'));
        try { G().save(); } catch (e) { console.warn(e); }
        UI().banner(G().year + '年 ' + G().month + '月', '令牌 ' + G().tokens + ' 枚', 1.8);
        // 上月取得新城时「移动」还不能用（武将都已行动 / 令牌用完）：月初横幅之后补弹提示
        if (this._tipPending && this.moveUsableCity()) await SG.wait(1.2);
        this.busy = false;
        if (this._tipPending) await this.moveTip(false);
      }
    }

    async gameOver(won) {
      const g = G();
      this.busy = true;
      this.refreshAll();
      if (won) {
        sfx('win');
        UI().banner('天下统一', g.ruler(g.player).name + '终成霸业', 4);
        await SG.wait(2.5);
        await UI().say(`${g.year}年${g.month}月，${g.ruler(g.player).name}平定四海，一统天下。\n麾下武将 ${g.generalsOf(g.player).length} 员，历时 ${g.year - SG.ScenarioData.StartYear} 年。`);
      } else {
        sfx('lose');
        UI().banner('势力灭亡', '霸业未成，身先陨落……', 4);
        await SG.wait(3);
      }
      try {
        if (SG.GameState.deleteSave) SG.GameState.deleteSave();
        else window.localStorage.removeItem('sanguozhi2_save');
      } catch (e) { /* 忽略 */ }
      location.reload();
      await new Promise(() => { /* 等待页面重载 */ });
    }

    onMapTap(x, y) {
      if (UI().anyModal()) return;
      if (this.hud && this.hud.style.display === 'none') return;   // 战斗中（地图隐藏）
      const id = SG.Game.map.pick(x, y);
      this.selectCity(id);
    }

    // ---------------------------------------------------------- 界面 --
    buildHud() {
      const hud = this.hud = SG.UI.screen ? SG.UI.screen('sg-strategy-hud') : h('div', 'sg-screen sg-strategy-hud', null, layer('screens'));

      const top = this.topBar = SG.UI.panel(hud, 'sg-topbar');
      this.topLeft = h('div', 'sg-topbar-left', '', top);
      this.topCenter = h('div', 'sg-topbar-center', '', top);
      const row = h('div', 'sg-topbar-actions', null, top);
      SG.UI.button(row, '势力', () => this.do(() => this.factionInfo()));
      // 指令或月末处理进行中（令牌已用、军粮已扣、尚未交战等）不可记录，避免存下半途的局面
      this.saveBtn = SG.UI.button(row, '记录', () => {
        if (this.busy || this.endMonth) return;
        let ok = false;
        try { ok = G().save() !== false; } catch (e) { ok = false; }
        UI().toast(ok ? '✦ 进度已记录' : '记录失败');
      });
      SG.UI.button(row, '音乐', () => {
        SG.Sfx.setMusic(!SG.Sfx.musicOn);
        UI().toast(SG.Sfx.musicOn ? '音乐 开' : '音乐 关');
      });
      this.endBtn = SG.UI.button(row, '结束本月', () => { if (!this.busy) this.do(() => this.confirmEnd()); }, { primary: true });

      const cp = this.cityPanel = SG.UI.panel(hud, 'sg-citypanel');
      const head = h('div', 'sg-citypanel-head', null, cp);
      this.cityTitle = h('div', 'sg-citypanel-title', '', head);
      const close = SG.UI.button(head, '×', () => this.selectCity(-1));
      close.classList.add('sg-close');
      close.setAttribute('aria-label', '关闭');
      const body = h('div', 'sg-citypanel-body', null, cp);
      this.cityStats = h('div', 'sg-citystats', '', body);
      this.genList = h('div', 'sg-genlist', null, body);
      this.cmdGrid = h('div', 'sg-cmdgrid', null, cp);
      injectStyle();
      cp.style.display = 'none';

      // 顶栏若因窄屏换行变高，城池面板下移到顶栏之下
      const syncTop = () => {
        cp.style.top = '';
        const tb = top.getBoundingClientRect(), hb = hud.getBoundingClientRect();
        if (!(tb.height > 0)) return;
        const need = Math.ceil(tb.bottom - hb.top + 6);
        const css = parseFloat(getComputedStyle(cp).top) || 0;
        if (need > css) cp.style.top = need + 'px';
      };
      this._syncTop = syncTop;
      const relayout = () => { syncTop(); this._occDirty = true; this._sizeDirty = true; };
      if (window.ResizeObserver) new ResizeObserver(relayout).observe(top);
      window.addEventListener('resize', relayout);

      // 地图上的城名
      for (const cv of SG.Game.map.cities.values()) {
        const el = h('div', 'sg-citylabel', SG.esc(cv.city.name), null);
        const handle = SG.UI.follow(el, () => cv.labelPos, { hideBeyond: LABEL_HIDE_BEYOND });
        handle.alpha = 0;             // 首次避让计算后才显示，避免第一帧城名叠在一起
        this.labels.set(cv.city.id, { el, handle, cv, w: 0, h: 0, fade: -1, on: false, seen: false });
      }
      this.startDeclutter();
    }

    setMapLabelsVisible(on) {
      this._labelsOn = !!on;
      for (const l of this.labels.values()) {
        if (l.handle) {
          if (!on) { l.handle.alpha = 0; l.seen = false; }       // 重新显示时由避让逻辑直接定出透明度
        } else l.el.style.visibility = on ? '' : 'hidden';
      }
      if (on) { this._occDirty = true; this.declutterLabels(performance.now(), true); }
    }

    // ---------------------------------------------------------- 城名避让 --
    // 每帧（在主循环投影标签之后）按优先级摆放城名：选中的城 → 我方城池 → 其余（人口多者优先），
    // 与已摆放的标签重叠、或被顶栏 / 城池面板遮住的标签淡出，避免城名互相压住或被界面切掉一半。
    startDeclutter() {
      if (this._declutterOn) return;
      this._declutterOn = true;
      const step = now => {
        requestAnimationFrame(step);
        try { this.declutterLabels(now, false); } catch (e) { if ((this._declutterErr = (this._declutterErr || 0) + 1) <= 3) console.error(e); }
      };
      requestAnimationFrame(step);
    }

    measureLabels(now) {
      for (const l of this.labels.values()) { l.w = l.el.offsetWidth; l.h = l.el.offsetHeight; }
      this._sizeAt = now; this._sizeDirty = false;
    }

    measureOccluders(now) {
      const occ = [];
      const add = (el, toTop) => {
        if (!el || el.style.display === 'none') return;
        const b = el.getBoundingClientRect();
        if (!(b.width > 0 && b.height > 0)) return;
        // 顶栏上方的窄缝也算遮挡：标签伸进去只会露出半截
        occ.push({ l: b.left, t: toTop ? -1e5 : b.top, r: b.right, b: b.bottom });
      };
      if (this.hud && this.hud.style.display !== 'none') { add(this.topBar, true); add(this.cityPanel, false); }
      this._occ = occ; this._occAt = now; this._occDirty = false;
    }

    declutterLabels(now, snap) {
      if (!this.hud || this.labels.size === 0) return;
      const dt = this._declutterLast > 0 ? M.clamp((now - this._declutterLast) / 1000, 0, 0.1) : 0;
      this._declutterLast = now;
      if (!this._labelsOn || this.hud.style.display === 'none') return;
      const Gfx = SG.Gfx;
      if (!Gfx || !Gfx.camera || typeof Gfx.worldToScreen !== 'function') return;
      const g = G();
      if (!g) return;
      // 紧凑模式：视口高度不足 540 且镜头距离超过约 90（带滞后，缩放时不来回切换）
      const dist = SG.Game && SG.Game.rig ? SG.Game.rig.distance : 0;
      const compact = window.innerHeight < 540 && dist > (this._compact ? 86 : 92);
      if (compact !== !!this._compact) {
        this._compact = compact;
        for (const [id, l] of this.labels) this.renderLabel(id, l);
        this._sizeDirty = true;
      }
      if (this._sizeDirty || now - this._sizeAt > 1000) this.measureLabels(now);
      if (this._occDirty || now - this._occAt > 200) this.measureOccluders(now);

      const cand = [];
      for (const [id, l] of this.labels) {
        const s = Gfx.worldToScreen(l.cv.labelPos);
        if (!(s && s.visible && s.dist < LABEL_HIDE_BEYOND && isFinite(s.x) && isFinite(s.y)) || !(l.w > 0)) {
          // 不在画面内（由 follow 自行隐藏）；再次进入画面时直接取目标透明度，不闪现
          l.seen = false; l.on = false;
          continue;
        }
        const c = g.cities[id];
        const rank = id === this.selected ? 0 : c && c.owner === g.player ? 1 : 2;
        cand.push({ l, id, x: s.x, y: s.y, rank, pop: c ? c.population : 0 });
      }
      cand.sort((a, b) => (a.rank - b.rank) || (b.pop - a.pop) || (a.id - b.id));

      const placed = [];
      const k = dt / LABEL_FADE;
      for (const o of cand) {
        const l = o.l;
        // 滞后：已显示的标签真正压住才隐藏；已隐藏的要留出几像素空隙才重新出现，镜头移动时不闪烁
        const pad = l.on ? -1 : 4;
        const hw = l.w / 2, hh = l.h / 2;
        const test = { l: o.x - hw - pad, t: o.y - hh - pad, r: o.x + hw + pad, b: o.y + hh + pad };
        let ok = true;
        for (const q of this._occ) if (overlaps(test, q)) { ok = false; break; }
        if (ok) for (const q of placed) if (overlaps(test, q)) { ok = false; break; }
        if (ok) placed.push({ l: o.x - hw, t: o.y - hh, r: o.x + hw, b: o.y + hh });
        l.on = ok;
        const target = ok ? 1 : 0;
        if (snap || !l.seen || l.fade < 0) l.fade = target;
        else l.fade = target > l.fade ? Math.min(target, l.fade + k) : Math.max(target, l.fade - k);
        l.seen = true;
        if (l.handle) l.handle.alpha = l.fade;
        else l.el.style.visibility = l.fade > 0 ? '' : 'hidden';
      }
    }

    refreshAll() {
      const g = G();
      if (!this.hud) return;
      const f = g.playerFaction;
      this.topLeft.innerHTML = dot(f.color, '■') + SG.esc(g.ruler(g.player).name) + '军';
      const mine = g.citiesOf(g.player);
      const gold = sum(mine, c => c.gold), food = sum(mine, c => c.food);
      const tokens = '●'.repeat(Math.max(0, g.tokens)) + '○'.repeat(Math.max(0, g.tokensFor(g.player) - g.tokens));
      this.topCenter.innerHTML =
        `<span class="sg-chip"><b>${g.year}</b><i>年</i><b>${g.month}</b><i>月</i></span>` +
        `<span class="sg-chip"><i>令牌</i><span class="sg-tokens">${tokens}</span></span>` +
        `<span class="sg-chip"><i>城</i><b>${g.cityCount(g.player)}</b></span>` +
        `<span class="sg-chip"><i>金</i><b>${gold}</b></span>` +
        `<span class="sg-chip"><i>粮</i><b>${food}</b></span>`;
      this.endBtn.disabled = this.busy;
      this.saveBtn.disabled = this.busy || this.endMonth;
      for (const [id, l] of this.labels) {
        this.renderLabel(id, l);
        l.el.classList.toggle('is-selected', id === this.selected);
      }
      this._sizeDirty = true;         // 兵力数字可能改变标签宽度
      this.refreshCityPanel();
    }

    // 城名标签内容；紧凑模式（矮屏且镜头拉远）字号略小、不显示兵力，减少拥挤
    renderLabel(id, l) {
      const g = G();
      const c = g.cities[id];
      const col = c.owner >= 0 ? g.factions[c.owner].color : '#b3b3b3';
      l.el.innerHTML = dot(col, '●') + SG.esc(c.name) + (c.owner === g.player && !this._compact ? `<small>${g.troopsIn(c)}</small>` : '');
      l.el.classList.toggle('is-mine', c.owner === g.player);
      l.el.style.fontSize = this._compact ? '.92rem' : '';
      l.el.style.padding = this._compact ? '.1rem .5rem .14rem .42rem' : '';
    }

    selectCity(id) {
      this.selected = id;
      SG.Game.map.select(id);
      if (id >= 0) click();
      for (const [cid, l] of this.labels) l.el.classList.toggle('is-selected', cid === id);
      this.refreshCityPanel();
    }

    refreshCityPanel() {
      const g = G();
      if (!this.cityPanel) return;
      if (this.selected < 0) {
        if (this.cityPanel.style.display !== 'none') { this.cityPanel.style.display = 'none'; this._occDirty = true; }
        return;
      }
      if (this.cityPanel.style.display === 'none') {
        this.cityPanel.style.display = '';
        if (this._syncTop) this._syncTop();
        this._occDirty = true;
      }
      const c = g.cities[this.selected];
      const mine = c.owner === g.player;
      const owner = c.owner >= 0 ? g.factions[c.owner] : null;
      this.cityTitle.innerHTML = SG.esc(c.name) + '<small>' +
        (owner ? `<span style="color:${readable(owner.color)}">${SG.esc(owner.name)}</span>` : '空城') + '</small>';
      const gov = c.governor >= 0 ? g.generals[c.governor].name : '—';
      const free = g.freeFoundIn(c.id);
      this.cityStats.innerHTML =
        `太守 <b>${SG.esc(gov)}</b>　　人口 ${n0(c.population)}\n` +
        `土地 <b>${c.land}</b>　产业 <b>${c.industry}</b>　町 <b>${c.town}</b>\n` +
        `金 <b>${c.gold}</b>　粮 <b>${c.food}</b>　防御 ${SG.cityDefense(c)}\n` +
        `兵力 <b>${g.troopsIn(c)}</b>　武将 ${g.officersIn(c).length} 人` +
        (free.length > 0 && mine ? `　<span class="sg-good">在野 ${free.length} 人</span>` : '');

      // 武将列表（君主优先，其次兵力）
      const scroll = this.genList.scrollTop;
      this.genList.innerHTML = '';
      const offs = g.officersIn(c).map((x, i) => ({ x, r: g.isRuler(x) ? 1 : 0, i }))
        .sort((a, b) => (b.r - a.r) || (b.x.troops - a.x.troops) || (a.i - b.i)).map(o => o.x);
      for (const gen of offs) {
        const row = h('div', 'sg-genrow', null, this.genList);
        if (SG.Portrait) row.appendChild(SG.Portrait.el(gen, { size: 40 }));
        const tag = g.isRuler(gen) ? '<span class="sg-tag sg-tag-ruler">君</span>' : gen.id === c.governor ? '<span class="sg-tag sg-tag-gov">守</span>' : '';
        h('div', 'sg-genrow-name', tag + '<b>' + SG.esc(gen.name) + '</b>' + (gen.moved ? '<span class="sg-moved">已行动</span>' : ''), row);
        h('div', 'sg-genrow-stats', `武${gen.war} 智${gen.intel} 政${gen.pol}\n兵${gen.troops} 训${gen.training} 忠${g.isRuler(gen) ? 100 : gen.loyalty}`, row);
      }
      this.genList.scrollTop = scroll;

      this.cmdGrid.innerHTML = '';
      if (!mine) return;
      // 每个指令给出“不能用的原因”（null = 可用）：先看是否忙碌，再看规则（城池、武将），最后看令牌
      const C = SG.Commands;
      const busy = this.busy ? (this.endMonth ? '诸侯行动中，请稍候。' : '指令执行中，请稍候。') : null;
      const noTok = g.tokens > 0 ? null : '本月令牌已用完——点「结束本月」进入下个月。';
      const first = (...whys) => { for (const w of whys) if (w) return w; return null; };
      const canAttack = c.links.some(i => g.cities[i].owner !== g.player && !g.allied(g.cities[i].owner, g.player));
      this.addCmd('开发', first(busy, noTok), () => this.do(() => this.cmdDevelop(c)));
      this.addCmd('征兵', first(busy, noTok), () => this.do(() => this.cmdRecruit(c)));
      this.addCmd('训练', first(busy, noTok), () => this.do(() => this.cmdTrain(c)));
      this.addCmd('搜索', first(busy, noTok), () => this.do(() => this.cmdSearch(c)));
      this.addCmd('登用', first(busy, free.length > 0 ? null : '城中没有已发现的在野人才——先用「搜索」寻访。', noTok), () => this.do(() => this.cmdHire(c)));
      this.addCmd('移动', first(busy, C.moveBlocked(c), noTok), () => this.do(() => this.cmdMove(c)));
      this.addCmd('输送', first(busy, C.transportBlocked(c), noTok), () => this.do(() => this.cmdTransport(c)));
      this.addCmd('外交', first(busy, noTok), () => this.do(() => this.cmdDiplomacy(c)));
      this.addCmd('出征', first(busy, canAttack ? null : c.name + '周围没有可攻打的城池（只能攻打相邻的敌城或空城，同盟势力除外）。', noTok), () => this.do(() => this.cmdAttack(c)), true);
      this.addCmd('赏赐', busy, () => this.do(() => this.cmdReward(c)));
      this.addCmd('交易', busy, () => this.do(() => this.cmdTrade(c)));
      this.addCmd('任命', busy, () => this.do(() => this.cmdAppoint(c)));
    }

    // why：不可用的原因（字符串）——按钮显示为灰色（aria-disabled，见 injectStyle），点按时说明原因而不执行；
    // null 为可用。按钮带 data-cmd（指令名），灰色时另带 data-why（原因）。
    addCmd(name, why, action, primary) {
      const b = SG.UI.button(this.cmdGrid, name, () => { if (b._why) this.explainCmd(b); else action(); }, primary ? { primary: true } : undefined);
      b.dataset.cmd = name;
      if (why) {
        b._why = why;
        b.dataset.why = why;
        b.title = why.replace(/<[^>]*>/g, '');
        b.setAttribute('aria-disabled', 'true');
      }
      // 提示闪烁只给可用的按钮（灰色按钮闪金光会让人误以为能用）
      if (!why && this._hint && this._hint.name === name && performance.now() < this._hint.until) b.classList.add('sg-cmd-hint');
      return b;
    }

    explainCmd(b) {
      if (!b || !b._why) return;
      // 闪烁与抖动同用 animation 属性：先停掉闪烁，抖动才看得见
      b.classList.remove('sg-cmd-hint');
      if (this._hint && this._hint.name === b.dataset.cmd) this._hint = null;
      SG.UI.pulse(b, 'sg-cmd-deny');
      // 同一时间只留一条说明，连点不会堆叠
      if (this._denyToast && this._denyToast.parentNode) this._denyToast.parentNode.removeChild(this._denyToast);
      this._denyToast = UI().toast(`<span class="sg-cmd-name">${SG.esc(b.dataset.cmd || '')}</span>${b._why}`, 3.4);
    }
    // 让某个指令按钮闪烁数次以引起注意（提示用）
    hintCmd(name) {
      this._hint = { name, until: performance.now() + 4400 };   // 面板重建时继续闪烁，直到时间到
      if (!this.cmdGrid) return;
      for (const b of this.cmdGrid.children) if (b.dataset && b.dataset.cmd === name && !b._why) SG.UI.pulse(b, 'sg-cmd-hint');
    }

    // 此刻能用「移动」的己方城（有令牌，且城中有未行动的武将可调往相连的己方城）；没有则 null。
    // 依次优先：当前选中的城、新取得的城、其余己方城。
    moveUsableCity() {
      const g = G(), C = SG.Commands;
      if (!g || g.tokens <= 0 || !g.playerFaction.alive) return null;
      const order = [this.selected].concat(this._tipCities, g.citiesOf(g.player).map(c => c.id));
      for (const id of order) {
        const c = id >= 0 ? g.cities[id] : null;
        if (c && c.owner === g.player && !C.moveBlocked(c)) return c;
      }
      return null;
    }
    // 第一次取得新城后弹一次提示：可用「移动」把武将调往其他城池，并让「移动」按钮闪烁。
    // 只在「移动」此刻真能用时弹出（必要时改选一座能用的城）；否则保持待弹，下个月月初再看。
    // fresh：刚取得新城（true）还是延到之后补弹（false），只影响第一句。→ 是否弹出
    async moveTip(fresh) {
      if (tipShown()) { this._tipPending = false; return false; }
      const c = this.moveUsableCity();
      if (!c) return false;
      this._tipPending = false;
      this._tipCities = [];
      markTipShown();
      if (this.selected !== c.id) {
        const p = SG.mapPos(c);
        SG.Game.rig.focusMap(p.x, p.y);
        this.selectCity(c.id);
      }
      this.refreshAll();              // 面板显示此刻的真实状态（「移动」可用）
      await UI().say('【提示】' + (fresh ? '取得了新的城池！' : '领有两座以上相连的城池，可以调动武将了。') + '\n' +
        '用「移动」可把武将调往其他己方城池——只要经由己方城池相连即可，不必相邻（每次消耗 1 枚令牌，可一次调动多人）。\n' +
        '「输送」同样可以在己方城池之间调拨金粮。');
      this.hintCmd('移动');
      return true;
    }

    do(routine) {
      if (this.busy || this.endMonth || UI().anyModal()) return;
      this._wrap(routine);
    }
    async _wrap(routine) {
      const g0 = G();
      const before = new Set(g0.citiesOf(g0.player).map(c => c.id));
      this._hint = null;              // 下达新指令时停止提示闪烁
      let gainedNow = false;
      this.busy = true; this.refreshAll();
      try {
        await routine();
        // 出征攻下 / 敌将献城等使城池增加：第一次时提示「移动」（见 moveTip）
        const g = G();
        if (g.playerFaction.alive && !tipShown()) {
          const gained = g.citiesOf(g.player).filter(c => !before.has(c.id)).map(c => c.id);
          if (gained.length > 0) { gainedNow = true; this._tipPending = true; this._tipCities = gained.concat(this._tipCities); }
        }
      } catch (e) { console.error(e); }
      this.busy = false;
      SG.Game.map.refresh(G());
      this.refreshAll();
      if (this._tipPending && !this.endMonth) {   // 刚点了「结束本月」时不弹，留到月初
        try { await this.moveTip(gainedNow); } catch (e) { console.error(e); }
      }
    }
    useToken() { G().tokens = Math.max(0, G().tokens - 1); }

    async confirmEnd() {
      if (G().tokens > 0) {
        const ok = await UI().confirm('还剩 ' + G().tokens + ' 枚令牌，确定结束本月？', '结束', '继续');
        if (!ok) return;
      }
      this.endMonth = true;
    }

    // ---------------------------------------------------------- 选择武将 --
    async pickGeneral(title, list, info, enabled) {
      const gens = list.slice();
      if (gens.length === 0) { await UI().say('没有可用的武将。'); return null; }
      const items = gens.map(g => item(SG.esc(g.name), info(g), !enabled || enabled(g)));
      const r = await UI().choose(title, items, null, 700);
      return r >= 0 ? gens[r] : null;
    }
    available(c) { return G().officersIn(c).filter(g => !g.moved); }

    // ---------------------------------------------------------- 内政 --
    async cmdDevelop(c) {
      const B = SG.Balance, K = SG.DevKind, C = SG.Commands;
      const kinds = [K.Land, K.Industry, K.Town];
      const desc = ['增加秋收的粮食', '增加每月的金收入', '增加人口与城防'];
      const vals = [c.land, c.industry, c.town];
      const r = await UI().choose('开发（花费 ' + B.DevelopCost + ' 金）',
        kinds.map((k, i) => item(C.devName(k), vals[i] + ' / ' + B.StatMax, c.gold >= B.DevelopCost, desc[i])), '金 ' + c.gold);
      if (r < 0) return;
      const who = await this.pickGeneral('由谁主持？', G().officersIn(c), g => '政治 ' + g.pol);
      if (!who) return;
      this.useToken();
      sfx('coin');
      await UI().say(C.develop(c, who, kinds[r]));
    }

    async cmdRecruit(c) {
      const B = SG.Balance, C = SG.Commands;
      const who = await this.pickGeneral('为谁征兵？', G().officersIn(c), g => `兵 ${g.troops} / ${SG.maxTroops(g)}`, g => g.troops < SG.maxTroops(g));
      if (!who) return;
      const max = C.recruitMax(c, who);
      const n = await UI().pickNumber('征兵人数', Math.min(100, max), max, 100, max, v => '花费 ' + Math.ceil(v / B.TroopsPerGold) + ' 金（现有 ' + c.gold + '）');
      if (!(n > 0)) return;
      this.useToken();
      sfx('march', 0.5);
      await UI().say(C.recruit(c, who, n));
    }

    async cmdTrain(c) {
      const who = await this.pickGeneral('训练哪支部队？', G().officersIn(c), g => `兵 ${g.troops}　训练 ${g.training}`, g => g.troops > 0 && g.training < 100);
      if (!who) return;
      this.useToken();
      await UI().say(SG.Commands.train(who));
    }

    // ---------------------------------------------------------- 人事 --
    async cmdSearch(c) {
      const C = SG.Commands;
      const who = await this.pickGeneral('派谁搜索人才？', this.available(c), g => '智力 ' + g.intel);
      if (!who) return;
      this.useToken();
      const msg = C.search(c, who);
      const found = C.searchFound;
      await UI().say(msg);
      if (found) {
        const ok = await UI().confirm(`登用${found.name}？（武${found.war} 智${found.intel} 政${found.pol}，成功率约 ${pct(C.hireChance(found, who, G().player))}）`, '登用', '暂且作罢');
        if (ok) await this.hireResult(found, who, c);
      }
    }

    async hireResult(target, recruiter, c) {
      if (SG.Commands.hire(target, recruiter, G().player, c.id)) {
        sfx('win', 0.5);
        await UI().say(target.name + '：久闻明公大名，愿效犬马之劳！\n（' + target.name + '加入了我军）', target.name, CINNABAR);
      } else {
        await UI().say(target.name + '：在下另有志向，请回吧。', target.name, GREY_SPEAKER);
      }
    }

    async cmdHire(c) {
      const C = SG.Commands;
      const target = await this.pickGeneral('登用哪位在野人才？', G().freeFoundIn(c.id), g => `武${g.war} 智${g.intel} 政${g.pol}`);
      if (!target) return;
      const who = await this.pickGeneral('派谁前去？', G().officersIn(c), g => `智力 ${g.intel}　成功率约 ${pct(C.hireChance(target, g, G().player))}`);
      if (!who) return;
      this.useToken();
      await this.hireResult(target, who, c);
    }

    async cmdReward(c) {
      const B = SG.Balance;
      const who = await this.pickGeneral('赏赐谁？（' + B.RewardGold + ' 金）', G().officersIn(c).filter(g => !G().isRuler(g)), g => '忠诚 ' + g.loyalty, g => g.loyalty < 100 && c.gold >= B.RewardGold);
      if (!who) return;
      sfx('coin');
      UI().toast(SG.Commands.reward(c, who));
    }

    async cmdAppoint(c) {
      const g = G();
      const who = await this.pickGeneral('任命太守', g.officersIn(c), x => `政${x.pol} 智${x.intel}`);
      if (!who) return;
      if (g.officersIn(c).some(x => g.isRuler(x)) && !g.isRuler(who)) { await UI().say('君主所在之城，由君主亲自坐镇。'); return; }
      c.governor = who.id;
      UI().toast(who.name + '出任' + c.name + '太守');
    }

    // 移动：目的地为经由己方城池相连可达的所有己方城（不必相邻），列表注明路程
    async cmdMove(c) {
      const g = G(), C = SG.Commands;
      const blocked = C.moveBlocked(c);
      if (blocked) { await UI().say(blocked); return; }
      const routes = C.moveTargets(c);
      const r = await UI().choose('移往何处？', routes.map(rt => item(SG.esc(rt.city.name),
        routeText(rt) + '　武将 ' + g.officersIn(rt.city).length + '　兵 ' + g.troopsIn(rt.city), true, viaText(g, rt))),
        '经由己方城池相连即可前往，不必相邻（1 枚令牌）');
      if (r < 0) return;
      const rt = routes[r], dest = rt.city;
      const gens = this.available(c);
      if (gens.length === 0) { await UI().say('本月已无可调动的武将。'); return; }
      const sel = await UI().chooseMany('调动武将至' + dest.name + '（' + routeText(rt) + '）', gens.map(x => item(SG.esc(x.name), '兵 ' + x.troops)), 10, '可一次调动多名武将（消耗 1 枚令牌）');
      if (!sel || sel.length === 0) return;
      this.useToken();
      await this.marchRoute(rt.path, g.playerFaction.color);
      let n = 0;
      for (const i of sel) { C.move(gens[i], dest); if (gens[i].city === dest.id) n++; }
      UI().toast(n + ' 名武将移驻' + dest.name);
    }

    // 沿己方路线逐城行军：相邻 1.2 秒；路线越长每段越快，总长不超过约 2.6 秒
    async marchRoute(path, color) {
      const hops = path.length - 1;
      if (hops <= 0) return;
      const per = Math.min(1.2, 2.6 / hops);
      for (let i = 0; i < hops; i++) await SG.Game.map.march(path[i], path[i + 1], color, per);
    }

    async cmdTransport(c) {
      const g = G(), C = SG.Commands;
      const blocked = C.transportBlocked(c);
      if (blocked) { await UI().say(blocked); return; }
      const routes = C.transportTargets(c);
      const r = await UI().choose('输送至何处？', routes.map(rt => item(SG.esc(rt.city.name),
        routeText(rt) + '　金 ' + rt.city.gold + '　粮 ' + rt.city.food, true, viaText(g, rt))),
        '经由己方城池相连即可送达，不必相邻（1 枚令牌）');
      if (r < 0) return;
      const dest = routes[r].city;
      const gold = await UI().pickNumber('输送金', 0, c.gold, 50, M.idiv(c.gold, 2), null);
      if (gold < 0) return;
      const food = await UI().pickNumber('输送粮', 0, c.food, 500, M.idiv(c.food, 2), null);
      if (food < 0) return;
      if (gold === 0 && food === 0) { UI().toast('未输送任何金粮。'); return; }
      this.useToken();
      UI().toast(C.transport(c, dest, gold, food));
    }

    async cmdTrade(c) {
      const C = SG.Commands;
      const price = C.foodPrice();
      const r = await UI().choose('交易　（每 100 粮 ' + price + ' 金）', [item('买粮', '金 ' + c.gold, c.gold > 0), item('卖粮', '粮 ' + c.food, c.food > 0)]);
      if (r < 0) return;
      if (r === 0) {
        const n = await UI().pickNumber('花多少金买粮？', 0, c.gold, 10, Math.min(c.gold, 200), v => '可得粮 ' + M.idiv(v * 100, price));
        if (n > 0) { sfx('coin'); UI().toast(C.buyFood(c, n)); }
      } else {
        const n = await UI().pickNumber('卖出多少粮？', 0, c.food, 100, Math.min(c.food, 1000), v => '可得金 ' + M.idiv(M.idiv(v * price, 100) * 9, 10));
        if (n > 0) { sfx('coin'); UI().toast(C.sellFood(c, n)); }
      }
    }

    // ---------------------------------------------------------- 外交 --
    async cmdDiplomacy(c) {
      const g = G(), B = SG.Balance, C = SG.Commands;
      const r = await UI().choose('外交策略', [
        item('同盟', null, true, '与他国缔结 ' + B.AllianceMonths + ' 个月的同盟'),
        item('离间', null, true, '降低敌将的忠诚'),
        item('拉拢', null, true, '劝诱敌将倒戈'),
      ]);
      if (r < 0) return;
      if (r === 0) {
        const fs = g.factions.filter(f => f.alive && f.id !== g.player);
        const fi = await UI().choose('与谁结盟？', fs.map(f => item(SG.esc(g.ruler(f.id).name),
          g.allied(f.id, g.player) ? '已同盟' : '城 ' + g.cityCount(f.id) + '　成功率约 ' + pct(C.allyChance(g.player, f.id, B.AllianceGift)),
          !g.allied(f.id, g.player))));
        if (fi < 0) return;
        const gift = await UI().pickNumber('赠送礼金', 0, c.gold, 50, Math.min(c.gold, B.AllianceGift), v => '成功率约 ' + pct(C.allyChance(g.player, fs[fi].id, v)));
        if (gift < 0) return;
        this.useToken();
        await UI().say(C.ally(c, g.player, fs[fi].id, gift));
        return;
      }
      const agent = await this.pickGeneral('派谁执行？', this.available(c), x => '智力 ' + x.intel);
      if (!agent) return;
      const targets = [];
      for (const i of c.links) {
        const x = g.cities[i];
        if (x.owner >= 0 && x.owner !== g.player) for (const o of g.officersIn(x)) if (!g.isRuler(o)) targets.push(o);
      }
      if (targets.length === 0) { await UI().say('邻近城池中没有可以下手的敌将。'); return; }
      let target;
      if (r === 1) target = await this.pickGeneral('离间哪位敌将？', targets, x => g.factions[x.faction].name + '·' + g.cities[x.city].name + '　智' + x.intel + '　忠诚 ' + x.loyalty);
      else target = await this.pickGeneral('拉拢哪位敌将？', targets, x => g.factions[x.faction].name + '·' + g.cities[x.city].name + '　忠诚 ' + x.loyalty + '　成功率约 ' + pct(C.persuadeChance(agent, x, g.player)));
      if (!target) return;
      this.useToken();
      agent.moved = true;
      await UI().say(r === 1 ? C.discord(agent, target) : C.persuade(agent, target, g.player));
    }

    // ---------------------------------------------------------- 战争 --
    async cmdAttack(c) {
      const g = G(), B = SG.Balance;
      const targets = c.links.map(i => g.cities[i]).filter(x => x.owner !== g.player && !g.allied(x.owner, g.player));
      const r = await UI().choose('攻打何处？', targets.map(t => item(SG.esc(t.name),
        (t.owner >= 0 ? g.factions[t.owner].name : '空城') + '　武将 ' + g.officersIn(t).length + '　兵 ' + g.troopsIn(t) + '　防 ' + SG.cityDefense(t))));
      if (r < 0) return;
      const target = targets[r];
      const gens = this.available(c).filter(x => x.troops > 0);
      if (gens.length === 0) { await UI().say('没有可以出征的部队（需有兵力且本月未行动）。'); return; }
      const sel = await UI().chooseMany('出征武将（至多 ' + B.MaxSortieGenerals + ' 名）',
        gens.map(x => item(SG.esc(x.name), `兵${x.troops} 武${x.war} 智${x.intel} 训${x.training}`)), B.MaxSortieGenerals,
        '第一位选中的武将为主将；主将败走则全军撤退。');
      if (!sel || sel.length === 0) return;
      const squad = sel.map(i => gens[i]);
      const troops = sum(squad, x => x.troops);
      const suggest = Math.min(c.food, Math.ceil(troops * 0.14 / 100) * 100 + 200);
      const food = await UI().pickNumber('携带军粮', 0, c.food, 100, suggest,
        v => `约可支撑 ${M.idiv(v, Math.max(1, M.idiv(troops, B.BattleFoodPerTroops)))} 日（兵 ${troops}）`);
      if (food < 0) return;
      this.useToken();
      const setup = SG.Conquest.prepare(g.player, c, target, squad, food, 0);
      sfx('horn');
      await SG.Game.map.march(c, target, g.playerFaction.color, 1.4);
      await this.fight(setup, 0);
    }

    async defend(s) {
      const g = G();
      const p = SG.mapPos(s.target);
      SG.Game.rig.focusMap(p.x, p.y, 45);
      this.selectCity(s.target.id);
      await SG.Game.map.march(s.src, s.target, g.factions[s.attacker].color, 1.4);
      const leader = g.ruler(s.attacker).name === s.atk[0].name ? g.ruler(s.attacker).name : g.factions[s.attacker].name + '军' + s.atk[0].name;
      await UI().say(`急报！${leader}率 ${s.atk.length} 名武将、兵 ${sum(s.atk, x => x.troops)} 来犯${s.target.name}！`);
      await this.fight(s, 1);
    }

    // 战斗与战后处理。playerSide：玩家是攻方 0 还是守方 1
    async fight(s, playerSide) {
      const g = G(), Cq = SG.Conquest;
      if (s.def.length === 0 || s.def.every(d => d.troops <= 0)) {
        Cq.autoResolve(s);
        Cq.apply(s);
        if (s.captives.length > 0) await this.handleCaptives(s, playerSide === 0);
        await UI().say(s.summary);
        return;
      }
      const mode = await UI().choose(s.target.name + '之战', [item('亲自指挥', '在战场上调兵遣将'), item('委任', '由部将自行作战，立即得出结果')], null, 560);
      if (mode === 0) {
        this.hud.style.display = 'none';
        this.setMapLabelsVisible(false);
        try {
          await SG.BattleController.run(s, playerSide);
        } catch (e) {
          // 防御：战斗画面出错时由电脑结算，避免整局卡死
          console.error(e);
          Cq.autoResolve(s);
        }
        this.hud.style.display = '';
        this.setMapLabelsVisible(true);
        music('map');
      } else Cq.autoResolve(s);
      Cq.apply(s);
      const playerWon = s.attackerWon === (playerSide === 0);
      if (s.captives.length > 0) {
        if (playerWon) await this.handleCaptives(s, true);
        else Cq.aiDecideCaptives(s, playerSide === 0 ? s.defender : s.attacker);
      }
      for (const f of g.factions) if (f.alive && g.cityCount(f.id) === 0) g.checkFactionDeath(f.id);
      SG.Game.map.refresh(g);
      this.refreshAll();
      const res = playerSide === 0
        ? (s.attackerWon ? '我军攻陷了' + s.target.name + '！' : '攻城失利，全军撤回' + s.src.name + '。')
        : (s.attackerWon ? s.target.name + '失守了……' : '我军成功守住了' + s.target.name + '！');
      await UI().say(res);
      if (s.attackerWon && s.defender >= 0 && !g.factions[s.defender].alive) await UI().say(g.factions[s.defender].name + '势力就此灭亡。');
    }

    async handleCaptives(s, playerDecides) {
      const g = G(), Cq = SG.Conquest, C = SG.Commands;
      if (!playerDecides) { Cq.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); return; }
      // 玩家只在获胜后处置俘虏，胜方必然据有 s.target（攻方刚攻下 / 守方守住），降将应编入该城
      const holderCity = s.target;
      const recruiter = g.ruler(g.player);
      for (const cap of s.captives.slice()) {
        const isRuler = cap.faction >= 0 && g.factions[cap.faction].ruler === cap.id;
        const r = await UI().choose('俘虏：' + cap.name + (isRuler ? '（君主）' : ''), [
          item('登用', isRuler ? '君主不会投降' : '成功率约 ' + pct(C.hireChance(cap, recruiter, g.player)), !isRuler),
          item('释放', '放其归去'),
          item('处斩', '以绝后患'),
        ], `武${cap.war} 智${cap.intel} 政${cap.pol}`);
        if (r === 0) {
          if (C.hire(cap, recruiter, g.player, holderCity.id)) await UI().say(cap.name + '：败军之将，承蒙不弃，愿降！', cap.name, CINNABAR);
          else { await UI().say(cap.name + '：忠臣不事二主！', cap.name, GREY_SPEAKER); Cq.release(cap); }
        } else if (r === 2) { Cq.execute(cap); await UI().say(cap.name + '被处斩了。'); }
        else Cq.release(cap);
      }
    }

    // ---------------------------------------------------------- 情报 --
    async factionInfo() {
      const g = G();
      const fs = g.factions.filter(f => f.alive).map((f, i) => ({ f, n: g.cityCount(f.id), i }))
        .sort((a, b) => (b.n - a.n) || (a.i - b.i)).map(o => o.f);
      const r = await UI().choose('天下势力', fs.map(f => item(
        swatch(f.color) + SG.esc(g.ruler(f.id).name) + (f.id === g.player ? '（我方）' : g.allied(f.id, g.player) ? '（同盟）' : ''),
        `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　兵 ${sum(g.citiesOf(f.id), c => g.troopsIn(c))}`)), null, 760);
      if (r < 0) return;
      const cap = g.cities[g.ruler(fs[r].id).city];
      const p = SG.mapPos(cap);
      SG.Game.rig.focusMap(p.x, p.y, 45);
      this.selectCity(cap.id);
    }
  }

  SG.StrategyScreen = StrategyScreen;
})();
