'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 启动与主循环
   ← Core/Game.cs：创建世界、标题画面（地图缓慢环绕）、操作说明、选择君主、战略循环
   SG.Game.boot() 由 index.html 调用。?seed=N 可复现随机数（截图 / 测试用）。
   ========================================================================== */
(function () {
  const SG = window.SG;

  const HelpText = '【操作】拖动平移地图，滚轮或双指缩放，点击城池查看情报。\n【令牌】每月可下达的指令数量取决于所领城池数。\n【出征】选择相邻的敌城与至多五名武将；可亲自指挥战斗或委任电脑。\n【战斗】点选部队移动，相邻时可攻击、施展策略或单挑。击败敌军主将或攻入本城即可获胜，三十日内未能攻下则撤退。';

  function UI() { return SG.UI; }
  function music(kind) { try { if (SG.Sfx) SG.Sfx.music(kind); } catch (e) { /* 无音频 */ } }
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  function item(label, right, enabled, desc) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right === undefined ? null : right, enabled === undefined ? true : enabled, desc === undefined ? null : desc);
    return { label, right: right === undefined ? null : right, enabled: enabled === undefined ? true : enabled, desc: desc === undefined ? null : desc, selected: false };
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

  // ------------------------------------------------------------ 页面行为 --
  // iOS：阻止页面滚动 / 缩放手势（可滚动的列表除外）
  function scrollableAncestor(el) {
    for (let e = el; e && e !== document.body && e.nodeType === 1; e = e.parentElement) {
      const tag = e.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return e;   // 滑块等需要原生拖动
      const cs = getComputedStyle(e);
      const oy = cs.overflowY, ox = cs.overflowX;
      if ((oy === 'auto' || oy === 'scroll') && e.scrollHeight > e.clientHeight + 1) return e;
      if ((ox === 'auto' || ox === 'scroll') && e.scrollWidth > e.clientWidth + 1) return e;
      if (cs.touchAction && cs.touchAction.indexOf('pan') >= 0) return e;
    }
    return null;
  }
  function installGestureGuards(canvas) {
    const opts = { passive: false };
    if (canvas) {
      canvas.addEventListener('touchmove', e => e.preventDefault(), opts);
      canvas.addEventListener('touchstart', e => { if (e.touches && e.touches.length > 1) e.preventDefault(); }, opts);
    }
    document.addEventListener('touchmove', e => {
      if (e.touches && e.touches.length > 1) { e.preventDefault(); return; }
      if (!scrollableAncestor(e.target)) e.preventDefault();
    }, opts);
    for (const ev of ['gesturestart', 'gesturechange', 'gestureend']) document.addEventListener(ev, e => e.preventDefault(), opts);
    document.addEventListener('dblclick', e => e.preventDefault(), opts);
    // 画布只用 pointer 事件：始于画布的触摸一律取消 touchend，
    // 不再生成兼容的 mouse / click 事件。否则点城池后弹出的面板会在手指下
    // 收到同一次点击（幽灵点击），也顺带阻止 iOS 旧版的双击缩放。
    if (canvas) canvas.addEventListener('touchend', e => { if (e.cancelable) e.preventDefault(); }, opts);
  }
  // 首次用户手势：解锁音频
  function installAudioUnlock() {
    const evs = ['pointerdown', 'pointerup', 'touchend', 'mousedown', 'click', 'keydown'];
    const finals = { pointerup: 1, touchend: 1, click: 1, keydown: 1 };
    const fn = e => {
      try { if (SG.Sfx && SG.Sfx.unlock) SG.Sfx.unlock(); } catch (err) { /* 忽略 */ }
      if (finals[e.type]) for (const ev of evs) window.removeEventListener(ev, fn, true);
    };
    for (const ev of evs) window.addEventListener(ev, fn, true);
  }

  // ---------------------------------------------------------------- Game --
  const Game = {
    rig: null,
    map: null,
    battleView: null,
    strategy: null,
    HelpText,
    _last: 0,
    _running: false,
    _errors: 0,

    async boot() {
      if (this._booted) return;
      this._booted = true;
      const loading = document.getElementById('loading');
      try {
        const q = new URLSearchParams(location.search);
        if (q.has('seed')) {
          const n = parseInt(q.get('seed'), 10);
          if (!isNaN(n)) SG.Random.seed(n);
        }
        const app = document.getElementById('app') || document.body;
        SG.Gfx.init(app);
        try { SG.Sfx.init(); } catch (e) { console.warn(e); }
        SG.UI.init();
        installGestureGuards(SG.Gfx.renderer.domElement);
        installAudioUnlock();

        // CameraRig.Create + Focus(60, 0, 50), 95, instant
        this.rig = new SG.CameraRig(SG.Gfx.camera, SG.Gfx.renderer.domElement);
        this.rig.focusMap(60, 50, 95, true);

        // 先用一份临时剧本生成地图，作为标题背景
        SG.G = SG.GameState.newGame('liubei');
        this.map = new SG.MapView();
        // 让载入画面先绘制出来
        await SG.frame(); await SG.frame();
        this.map.build(SG.G);
        this.strategy = new SG.StrategyScreen();
        this.startLoop();
        await SG.frame();
        this._hideLoading(loading);
      } catch (e) {
        console.error(e);
        if (loading) {
          loading.classList.add('sg-error');
          const msg = loading.querySelector('.sg-loading-text') || loading;
          msg.textContent = '无法启动：' + (e && e.message ? e.message : e);
        }
        return;
      }
      await this.titleFlow();
    },

    _hideLoading(el) {
      if (!el) return;
      el.classList.add('sg-done');
      el.style.opacity = '0';
      el.style.pointerEvents = 'none';
      setTimeout(() => { if (el.parentNode) el.parentNode.removeChild(el); }, 700);
    },

    // ------------------------------------------------------------ 渲染循环 --
    startLoop() {
      if (this._running) return;
      this._running = true;
      this._last = performance.now();
      const loop = now => {
        requestAnimationFrame(loop);
        const dt = Math.min(0.1, Math.max(0, (now - this._last) / 1000));
        this._last = now;
        this.frame(dt);
      };
      requestAnimationFrame(loop);
    },

    frame(dt) {
      try {
        const rig = this.rig;
        rig.update(dt);
        if (this.map) this.map.update(dt);
        if (this.battleView) this.battleView.update(dt);
        const top = SG.Gfx.topScreen && SG.Gfx.topScreen();
        if (top && typeof top.update === 'function') top.update(dt);
        SG.Gfx.setShadowFocus(rig.target, rig.distance * 0.85 + 8);
        SG.UI.updateFollowers(SG.Gfx.camera);
        SG.Gfx.render();
      } catch (e) {
        // 每帧都会重复的错误只打印前几次
        if (this._errors++ < 5) console.error(e);
      }
    },

    // ------------------------------------------------------------ 标题 --
    async titleFlow() {
      const rig = this.rig;
      music('title');
      rig.inputEnabled = false;
      const title = h('div', 'sg-title', null, layer('screens'));
      const hero = h('div', 'sg-title-hero', null, title);
      h('div', 'sg-title-seal', '霸', hero);
      h('div', 'sg-title-sub0', '三国志 II', hero);
      h('div', 'sg-title-main', '霸王的大陆', hero);
      h('div', 'sg-title-sub', '群雄逐鹿 · 一统天下', hero);
      const menu = h('div', 'sg-title-menu', null, title);
      let choice = -1;
      SG.UI.button(menu, '新的征程', () => { choice = 0; }, { primary: true });
      const cont = SG.UI.button(menu, '继续征程', () => { choice = 1; });
      cont.disabled = !SG.GameState.hasSave();
      SG.UI.button(menu, '操作说明', () => { choice = 2; });
      h('div', 'sg-title-foot', SG.isTouch ? '拖动平移 · 双指缩放 · 轻触选取' : '拖动平移 · 滚轮缩放 · WASD 移动 · Q / E 旋转', title);

      let ang = 0, last = performance.now();
      for (;;) {
        while (choice < 0) {
          const now = performance.now();
          const dt = Math.min(0.1, (now - last) / 1000);
          last = now;
          ang += dt * 2.5;
          rig.desiredYaw = Math.sin(ang * 0.05) * 25;
          rig.focusMap(58 + Math.sin(ang * 0.03) * 18, 48 + Math.cos(ang * 0.04) * 12, 85);
          await SG.frame();
        }
        if (choice === 2) {
          choice = -1;
          await UI().say(HelpText);
          last = performance.now();
          continue;
        }
        if (choice === 1) {
          let loaded = null;
          try { loaded = SG.GameState.load(); } catch (e) { console.warn(e); loaded = null; }
          if (!loaded) { choice = -1; UI().toast('存档无法读取'); last = performance.now(); continue; }
          SG.G = loaded;
          title.remove();
          break;
        }
        title.remove();
        await this.selectRuler();
        break;
      }
      rig.desiredYaw = 0;
      rig.inputEnabled = true;
      this.map.refresh(SG.G);
      await this.strategy.run();
    },

    // ---------------------------------------------------------- 选择君主 --
    async selectRuler() {
      await UI().say(SG.ScenarioData.Intro);
      const g = SG.G;
      const factions = g.factions.filter(f => f.alive).map((f, i) => ({ f, n: g.cityCount(f.id), i }))
        .sort((a, b) => (b.n - a.n) || (a.i - b.i)).map(o => o.f);
      for (;;) {
        const items = factions.map(f => item(
          `<span style="color:${f.color}">■</span> ${SG.esc(g.ruler(f.id).name)}`,
          `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　德 ${f.virtue}　人望 ${f.fame}`));
        const r = await UI().choose('选择君主　' + SG.ScenarioData.StartYear + '年 · ' + SG.ScenarioData.Title, items, '德越高越易登用人才；人望决定战场上的行动力。', 820);
        if (r < 0) continue;
        const f0 = factions[r];
        const capital = g.citiesOf(f0.id)[0];
        if (capital) {
          const p = SG.mapPos(capital);
          this.rig.focusMap(p.x, p.y, 45);
          this.map.select(capital.id);
        }
        const ok = await UI().confirm('以' + g.ruler(f0.id).name + '开始？', '出阵', '再想想');
        if (!ok) { this.map.select(-1); continue; }
        SG.G = SG.GameState.newGame(f0.key);
        this.map.select(-1);
        break;
      }
    },
  };

  SG.Game = Game;
})();
