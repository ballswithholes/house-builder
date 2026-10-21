'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 启动与主循环（第三版改名，DESIGN-V3 §7）
   ← Core/Game.cs：创建世界、标题画面（地图缓慢环绕）、操作说明、选择君主、战略循环
   SG.Game.boot() 由 index.html 调用。?seed=N 可复现随机数（截图 / 测试用）。
   第二版 §4G：
     新的征程 → 选择剧本（「董卓专横 · 中原」经典 38 城 / 「天下大势 · 世界」）→ 开场白 → 选择君主。
       经典：单一列表（与旧版相同）；世界：先选地域（势力数与几位君主的头像），再选该地域的君主，
       悬停 / 选中时镜头飞往其都城，确认卡片显示势力介绍与「游戏取舍」。
     地图按剧本分片构建（map.buildAsync），载入画面（#loading）显示进度；镜头范围按剧本（useMap）。
     Game.buildMap(region, title) → Promise    Game.ensureMap(scenarioId) → Promise
     Game.lastBuildMs                          最近一次地图构建耗时（毫秒）
   第三版：
     Game.TITLE = '三国志-霸王的世界'；标题画面「三国志」「霸王的世界」两行排版（css .sg-title-*）。
     战斗动画默认开启：启动时一次性清除旧存的攻击画面设定（localStorage 'sanguozhi2_clash'，见 migrateV3），
       标记键 'sanguozhi2_v3_clash_reset'；之后玩家在战场上改的设定照常保存。
     头像预生成尺寸按 SG.UI.faceSizes()（列表行 / 城池面板、对话说话人）。
   ========================================================================== */
(function () {
  const SG = window.SG;

  const TITLE = '三国志-霸王的世界';
  const HelpText = '【操作】拖动平移地图，滚轮或双指缩放，点击城池查看情报。\n【令牌】每月可下达的指令数量取决于所领城池数。灰色指令点按可查看原因。\n【出征】选择相邻的敌城与至多五名武将；可亲自指挥战斗或委任电脑。\n【移动 / 输送】调动武将、调拨金粮：经由己方城池相连的城都可前往，不必相邻（列表注明经过几城），每次 1 枚令牌。己方两城相连即可使用。\n【战斗】点选部队移动，相邻时可攻击、施展策略或单挑。击败敌军主将或攻入本城即可获胜，三十日内未能攻下则撤退。\n【单挑】J 轻击（连按三段）· K 重击（按住蓄力，满蓄破防）· L 格挡 · U 格开（来招前一瞬轻按，成功后反击 ×1.5 不可挡；冷却 2.5 秒，按空露出破绽）· I 绝技（怒气满）。\n【成长】武将参战获得经验、升级；武力越高可学越多、越强的必杀，智力越高可学越多、越强的计策。升级后从 3 个候选中选择，也可在城池面板点选武将「习得新招」。';

  // 触屏版：单挑一节改说右下角的按钮（键盘键位对手机无用；正文也短一些，横屏手机一屏放得下）
  const HelpDuelTouch = '【单挑】右下按钮：轻击（连按三段）· 重击（按住蓄力，满蓄破防）· 格挡 · 格开（来招前一瞬轻点，成功后反击 ×1.5 不可挡；冷却 2.5 秒，点空露出破绽）· 绝技（怒气满）。';
  function helpText() { return SG.isTouch ? HelpText.replace(/【单挑】[^\n]*/, HelpDuelTouch) : HelpText; }
  function UI() { return SG.UI; }
  // 剧本（SG.Scenarios 的键）
  const SCENARIOS = [
    { id: 'classic', name: '董卓专横 · 中原', desc: '原作剧本：董卓专权，关东诸侯并起，逐鹿中原。规则与原作相同。' },
    { id: 'world', name: '天下大势 · 世界', desc: '东起倭国、西至罗马：欧亚大陆各国群雄并起，先选地域再选君主。' },
  ];
  function scenarioName(id) { const s = SCENARIOS.find(x => x.id === id); return s ? s.name : id; }
  function scenarioOf(id) { return SG.Scenarios && SG.Scenarios[id] ? SG.Scenarios[id] : SG.ScenarioData; }
  function portraitHtml(gen, size) {
    try { return SG.Portrait ? SG.Portrait.html(gen, { size }) : ''; } catch (e) { return ''; }
  }
  function swatch(color) { return `<span class="sg-dot" style="color:${color}">■</span>`; }
  // 最上层对话框里的列表行：悬停（鼠标）或获得焦点（键盘）时回调 onHover(index)，稍作去抖；
  // initial（可选）：打开时先对该行回调一次
  function hookListHover(onHover, initial) {
    const ms = document.querySelectorAll('.sg-modals .sg-modal:not(.is-closing)');
    const m = ms[ms.length - 1];
    if (!m) return;
    let timer = 0, last = -1;
    const fire = i => {
      if (i === last) return;
      last = i;
      clearTimeout(timer);
      timer = setTimeout(() => { try { onHover(i); } catch (e) { console.warn(e); } }, 140);
    };
    m.querySelectorAll('.sg-item').forEach((b, i) => {
      b.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse') fire(i); });
      b.addEventListener('focus', () => fire(i));
    });
    if (initial >= 0) fire(initial);
  }
  // 最上层对话框靠左放（地图留在右侧可见，镜头飞行时看得到目的地）
  function sideModal() {
    const ms = document.querySelectorAll('.sg-modals .sg-modal:not(.is-closing)');
    const m = ms[ms.length - 1];
    if (m) m.classList.add('sg-side-modal');
    return m;
  }
  // 对话框右侧的空白区：{ frac 空白占屏宽的比例, dxPx 空白区中心相对屏幕中心的偏移 }
  function freeArea() {
    const vw = window.innerWidth || 1;
    const ms = document.querySelectorAll('.sg-modals .sg-modal.sg-side-modal:not(.is-closing) .sg-dialog');
    const p = ms[ms.length - 1];
    if (!p) return { frac: 1, dxPx: 0 };
    const r = p.getBoundingClientRect();
    const free = vw - r.right;
    if (free < 160) return { frac: 1, dxPx: 0 };
    return { frac: free / vw, dxPx: (r.right + vw) / 2 - vw / 2 };
  }
  // 载入画面：index.html 的 #loading；淡出移除后再次需要时按同样结构重建
  function loadingOverlay(title) {
    let el = document.getElementById('loading');
    if (el && el.classList.contains('sg-done')) { el.removeAttribute('id'); el = null; }
    if (!el) {
      el = document.createElement('div');
      el.id = 'loading';
      el.setAttribute('role', 'status');
      el.setAttribute('aria-live', 'polite');
      el.innerHTML = '<div class="sg-loading-seal">霸</div><div class="sg-loading-title"></div>' +
        '<div class="sg-loading-text"></div><div class="sg-loading-bar"><i></i></div>';
      el.style.opacity = '0';
      document.body.appendChild(el);
      void el.offsetWidth;
      el.style.opacity = '1';
    }
    const t = el.querySelector('.sg-loading-title');
    if (t && title) {
      // 游戏名分两段排版（「三国志」小字 +「霸王的世界」），与 index.html 的载入画面一致
      const m = /^(三国志)-(.+)$/.exec(title);
      if (m) { t.innerHTML = `<span class="sg-lt-pre">${m[1]}</span><span class="sg-lt-dash">-</span>${SG.esc(m[2])}`; t.setAttribute('aria-label', title); }
      else { t.textContent = title; t.removeAttribute('aria-label'); }
    }
    return el;
  }
  // 进度：阶段名 + 百分比；进度条改为确定进度
  function setLoading(el, frac, label) {
    if (!el) return;
    const pct = Math.round(Math.max(0, Math.min(1, frac || 0)) * 100);
    const t = el.querySelector('.sg-loading-text');
    if (t) t.textContent = (label ? label + '…… ' : '正在绘制山河…… ') + pct + '%';
    const bar = el.querySelector('.sg-loading-bar');
    if (bar) {
      bar.classList.add('is-det');
      const i = bar.querySelector('i');
      if (i) i.style.width = pct + '%';
    }
  }
  // 空闲时分片预生成全部在世武将的头像（城池面板 40px、对话 96 参考像素）
  function preloadPortraits() {
    try { if (SG.Portrait && SG.G) SG.Portrait.preloadState(SG.G, SG.UI.faceSizes ? SG.UI.faceSizes() : [40, Math.round(96 * SG.UI.scale())]); } catch (e) { console.warn(e); }
  }
  // 第三版：战斗动画（攻击画面）默认开启。旧版本存下的「关」作废一次——清除 clash.js 的设定键，
  // 并把已按旧设定初始化的 SG.Clash 改回默认（「快」仍是开启动画，予以保留）。只执行一次（标记键）。
  const CLASH_KEY = 'sanguozhi2_clash', V3_MARK = 'sanguozhi2_v3_clash_reset';
  function migrateV3() {
    let ls = null;
    try { ls = window.localStorage; } catch (e) { ls = null; }
    if (!ls) return;
    try {
      if (ls.getItem(V3_MARK)) return;
      const old = ls.getItem(CLASH_KEY);
      if (old !== 'fast') {
        ls.removeItem(CLASH_KEY);
        if (SG.Clash) { SG.Clash.enabled = true; SG.Clash.speed = 1; }
      }
      ls.setItem(V3_MARK, '1');
    } catch (e) { /* 无存储：沿用默认 */ }
  }
  function music(kind) { try { if (SG.Sfx) SG.Sfx.music(kind); } catch (e) { /* 无音频 */ } }
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  function item(label, right, enabled, desc, opts) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right === undefined ? null : right, enabled === undefined ? true : enabled, desc === undefined ? null : desc, opts);
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
  // 矮屏（手机横屏）：html.sg-short（UI-V4 §15.1；CSS 主要用媒体查询，JS 用这个类判断）
  function installShortClass() {
    try {
      const mq = matchMedia('(max-height: 559px)');
      const fn = () => document.documentElement.classList.toggle('sg-short', mq.matches);
      fn();
      if (mq.addEventListener) mq.addEventListener('change', fn); else if (mq.addListener) mq.addListener(fn);
      document.documentElement.classList.toggle('sg-touch', !!SG.isTouch);
    } catch (e) { /* 忽略 */ }
  }
  // 镜头设置（UI-V4 §7 第 28–33 行）施加到这一台 CameraRig 上：camera.js 不改，只在实例上包一层
  //（缩放速度 → 滚轮与双指的倍率；平移速度 / 拖动反转 → _pan；镜头飞行 平滑 · 快速 · 直接切换 → flyTo / focusWorld；
  //  屏幕边缘平移 → Game.frame 里 edgePan(dt)）。读设置时缓存，改了经 SG.Settings.on 更新。
  const CAM = { zoom: 1, pan: 1, invert: false, edge: false, fly: 'smooth' };
  function readCam() {
    const U = SG.UISettings;
    if (!U) return;
    CAM.zoom = Math.max(0.5, Math.min(2, (+U.get('cam.zoomSpeed') || 100) / 100));
    CAM.pan = Math.max(0.5, Math.min(2, (+U.get('cam.panSpeed') || 100) / 100));
    CAM.invert = !!U.get('cam.invert');
    CAM.edge = !!U.get('cam.edgePan') && !SG.isTouch;
    CAM.fly = U.get('cam.fly') || 'smooth';
  }
  function installCamSettings(rig) {
    if (!rig || rig._sgCam) return;
    rig._sgCam = true;
    readCam();
    try { if (SG.Settings && SG.Settings.on) for (const id of ['cam.zoomSpeed', 'cam.panSpeed', 'cam.invert', 'cam.edgePan', 'cam.fly']) SG.Settings.on(id, readCam); } catch (e) { /* 忽略 */ }
    const wheel = rig._wheel, move = rig._move, pan = rig._pan, fly = rig.flyTo, focus = rig.focusWorld;
    // 缩放速度：一次滚轮 / 一段双指的距离倍率按 CAM.zoom 次方放大或减小
    const zoomed = (fn, self, args) => { const d0 = self.desiredDistance; const r = fn.apply(self, args); if (CAM.zoom !== 1 && d0 > 0 && self.desiredDistance !== d0) self.desiredDistance = d0 * Math.pow(self.desiredDistance / d0, CAM.zoom); return r; };
    rig._wheel = function (e) { return zoomed(wheel, this, [e]); };
    rig._move = function (e) { return this._pointers && this._pointers.size >= 2 ? zoomed(move, this, [e]) : move.call(this, e); };
    rig._pan = function (dx, dz) { const k = CAM.pan * (CAM.invert ? -1 : 1); return pan.call(this, dx * k, dz * k); };
    rig.flyTo = function (x, y, dist, opts) {
      if (CAM.fly === 'cut') { this._endFly(); this.focusMap(x, y, dist, true); return Promise.resolve(); }
      const p = fly.call(this, x, y, dist, opts);
      if (CAM.fly === 'fast' && this._fly && !(opts && opts.duration > 0)) this._fly.dur *= 0.5;
      return p;
    };
    rig.focusWorld = function (p, dist, instant) { return focus.call(this, p, dist, instant || CAM.fly === 'cut'); };
    // 屏幕边缘平移（桌面，鼠标贴近边缘 12 px；窗口打开时不动）
    let mx = -1, my = -1;
    window.addEventListener('pointermove', e => { if (e.pointerType === 'mouse') { mx = e.clientX; my = e.clientY; } }, { passive: true });
    document.addEventListener('pointerleave', () => { mx = my = -1; });
    window.addEventListener('blur', () => { mx = my = -1; });
    rig.edgePan = function (dt) {
      if (!CAM.edge || !this.inputEnabled || mx < 0 || this._pressing || (SG.UI && SG.UI.anyModal && SG.UI.anyModal())) return;
      const W = innerWidth, H = innerHeight, m = 12;
      const h = mx <= m ? -1 : mx >= W - 1 - m ? 1 : 0, v = my <= m ? 1 : my >= H - 1 - m ? -1 : 0;
      if (!h && !v) return;
      this._endFly();
      const y = this.yaw * Math.PI / 180, s = dt * this.distance * 0.9 * CAM.pan;
      const ux = h * Math.cos(y) + v * Math.sin(y), uz = -h * Math.sin(y) + v * Math.cos(y);
      this.desired.x += ux * s; this.desired.z -= uz * s;
    };
  }
  // 帧率上限（第 13 行）每帧都要读：缓存，改了再更新（设置的 get 每次都读 localStorage）
  let fpsCap = 60;
  function readFps() { try { fpsCap = SG.UISettings && SG.UISettings.get('ui.fps') === '30' ? 30 : 60; } catch (e) { fpsCap = 60; } }

  // 后台静音（设置第 10 行）：audio.js 在页面隐藏时挂起音频；设为「关」时在它之后再恢复（继续在后台播放）
  function installBackgroundAudio() {
    document.addEventListener('visibilitychange', () => {
      try {
        if (!document.hidden || !SG.Sfx || !SG.Sfx.unlocked || !SG.UISettings || SG.UISettings.get('ui.muteHidden') !== false) return;
        const bus = SG.Sfx.voiceBus ? SG.Sfx.voiceBus() : null;
        if (bus && bus.ctx && bus.ctx.state === 'suspended') bus.ctx.resume().catch(() => {});
      } catch (e) { /* 无音频 */ }
    });
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
    SCENARIOS,
    TITLE,
    lastBuildMs: 0,
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
        // 测试钩子（tests/ui-layout.html）：?insets=47 模拟左右安全区（px）；?start=liubei 跳过标题直接开局（经典剧本）
        if (q.has('insets')) {
          const px = Math.max(0, Math.min(80, parseInt(q.get('insets'), 10) || 0)) + 'px';
          document.documentElement.style.setProperty('--sg-sal', px);
          document.documentElement.style.setProperty('--sg-sar', px);
        }
        migrateV3();
        try { document.title = TITLE; } catch (e) { /* 忽略 */ }
        const app = document.getElementById('app') || document.body;
        SG.Gfx.init(app);
        try { SG.Sfx.init(); } catch (e) { console.warn(e); }
        SG.UI.init();
        // 第四版界面：程序化材质（空闲分片）、设置（登记拥有者的行并施加一遍）、矮屏标记（UI-V4 §15.1）
        try { if (SG.UIArt) SG.UIArt.init(); } catch (e) { console.warn(e); }
        try { if (SG.UISettings) { SG.UISettings.bridge(); SG.UISettings.applyAll(); } } catch (e) { console.warn(e); }
        installShortClass();
        installGestureGuards(SG.Gfx.renderer.domElement);
        installAudioUnlock();
        installBackgroundAudio();

        // CameraRig.Create + Focus(60, 0, 50), 95, instant
        this.rig = new SG.CameraRig(SG.Gfx.camera, SG.Gfx.renderer.domElement);
        installCamSettings(this.rig);
        this.rig.focusMap(60, 50, 95, true);

        // 先用一份临时的经典剧本生成地图，作为标题背景
        if (SG.Scenarios && SG.Scenarios.classic) SG.ScenarioData = SG.Scenarios.classic;
        SG.G = SG.GameState.newGame('liubei');
        this.map = new SG.MapView();
        // 让载入画面先绘制出来
        await SG.frame(); await SG.frame();
        const t0 = performance.now();
        await this.map.buildAsync(SG.G, { onProgress: (p, label) => setLoading(loading, p, label) });
        this.lastBuildMs = performance.now() - t0;
        this.rig.useMap(this.map);
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

    // 按 SG.G 重建地图（region 'china' | 'world'），载入画面显示进度；完成后镜头范围随地图
    async buildMap(region, title) {
      const el = loadingOverlay(title || TITLE);
      setLoading(el, 0, '准备');
      this.map.select(-1);
      try {
        await SG.frame(); await SG.frame();
        const t0 = performance.now();
        await this.map.buildAsync(SG.G, { region, onProgress: (p, label) => setLoading(el, p, label) });
        this.lastBuildMs = performance.now() - t0;
        this.rig.useMap(this.map);
      } finally {
        await SG.frame();
        this._hideLoading(el);
      }
    },
    // 地图与剧本一致（经典 → 中国地图；世界 → 欧亚大陆），不一致时重建
    async ensureMap(sid) {
      const want = sid === 'world' ? 'world' : 'china';
      if (this.map.root && this.map.region === want) { this.rig.useMap(this.map); return; }
      await this.buildMap(want, sid === 'world' ? '天下大势' : TITLE);
    },

    // ------------------------------------------------------------ 渲染循环 --
    startLoop() {
      if (this._running) return;
      this._running = true;
      this._last = performance.now();
      readFps();
      try { if (SG.Settings && SG.Settings.on) SG.Settings.on('ui.fps', readFps); } catch (e) { /* 忽略 */ }
      const loop = now => {
        requestAnimationFrame(loop);
        // 帧率上限 30（设置第 13 行）：隔帧渲染
        if (fpsCap === 30 && now - this._last < 1000 / 31) return;
        const dt = Math.min(0.1, Math.max(0, (now - this._last) / 1000));
        this._last = now;
        this.frame(dt);
      };
      requestAnimationFrame(loop);
    },

    frame(dt) {
      try {
        const rig = this.rig;
        if (rig.edgePan) rig.edgePan(dt);
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
      // 标题画面用地图自己的黄昏（不跟随一局里的时钟）：明确告诉地图（MapView.setMood('title')；离开标题时 setMood(null)，
      // 进入战略画面后由 StrategyScreen 按「昼夜光影」改为跟随时钟）
      try { if (this.map && this.map.setMood) this.map.setMood('title'); } catch (e) { /* 地图无氛围 */ }
      rig.inputEnabled = false;
      // ?start=<势力键>：直接开局（测试 / 版式检查用）
      let quick = null;
      try { quick = new URLSearchParams(location.search).get('start'); } catch (e) { quick = null; }
      if (quick) {
        try {
          SG.ScenarioData = scenarioOf('classic');
          await this.ensureMap('classic');
          SG.G = SG.GameState.newGame(quick, 'classic');
          preloadPortraits();
          rig.desiredYaw = 0;
          rig.inputEnabled = true;
          this.map.refresh(SG.G);
          await this.strategy.run();
          return;
        } catch (e) { console.warn('start=' + quick + ' failed', e); }
      }
      // 第四版标题（UI-V4 §5.11）：左上墨书大字（「霸」放大为朱色）+ 朱印，右侧挂在横梁上的漆木牌；
      // 「继续征程」是一张纸卡（最近一份记录的君主立绘 + 剧本 · 势力 · 年月 · 城数）
      const title = h('div', 'sg-title v4', null, layer('screens'));
      const wash = h('div', 'sg-title-wash', null, title);
      wash.setAttribute('aria-hidden', 'true');
      const hero = h('div', 'sg-title-hero', null, title);
      hero.setAttribute('role', 'heading');
      hero.setAttribute('aria-level', '1');
      hero.setAttribute('aria-label', TITLE);
      const top = h('div', 'sg-title-sub0', '<i></i><span class="sg-title-sgz">三 国 志</span><i></i>', hero);
      top.setAttribute('aria-hidden', 'true');
      const main = h('div', 'sg-title-main', '<span class="sg-title-ba">霸</span><span>王的世界</span>', hero);
      main.setAttribute('aria-hidden', 'true');
      h('div', 'sg-title-seal', '霸', hero).setAttribute('aria-hidden', 'true');
      h('div', 'sg-title-sub', '群雄逐鹿　·　四海归一', hero).setAttribute('aria-hidden', 'true');
      const board = h('div', 'sg-title-board', '<i class="sg-tb-beam"></i>', title);
      const menu = h('div', 'sg-title-menu', null, board);
      let choice = -1;
      const GS = SG.GameState;
      const legacy = !!(GS.hasLegacySave && GS.hasLegacySave());
      // 最近的一份记录（自动 / 手动，按记录时间）
      let newest = null;
      try { for (const e of (GS.listSaves ? GS.listSaves() : [])) if (e && e.slot !== 'legacy' && !e.broken && (!newest || (e.savedAt || 0) > (newest.savedAt || 0))) newest = e; } catch (e) { newest = null; }
      SG.UI.button(menu, '新的征程', () => { choice = 0; }, { kind: 'seal', className: 'sg-tb-new' });
      const info = GS.saveInfo ? GS.saveInfo() : null;
      const cont = SG.UI.button(menu, '', () => { choice = 1; }, { kind: 'paper', className: 'sg-tb-cont' });
      if (newest || info) {
        const e = newest || { scenario: info.scenario, factionName: info.faction, year: info.year, month: info.month };
        const ru = e.ruler ? (SG.UI.frame ? SG.UI.frame({ name: e.ruler }, { size: 46 }) : '') : '';
        cont.innerHTML = ru + '<span class="sg-tb-cl"><b>继续征程</b><small>' + SG.esc(scenarioName(e.scenario).split(' · ')[0] + ' · ' + (e.factionName || '') + ' · ' + e.year + '年' + e.month + '月' + (e.cities ? ' · 城 ' + e.cities : '')) + '</small></span>';
      } else {
        cont.innerHTML = '<span class="sg-tb-cl"><b>继续征程</b><small>' + (legacy ? '旧版存档（新版本无法读取）' : '尚无记录') + '</small></span>';
        cont.setEnabled(legacy ? '旧版（第一版）存档无法读取。' : '还没有记录——先开始新的征程。');
      }
      const hasAny = !!(newest || info || (GS.listSaves && GS.listSaves().length));
      SG.UI.button(menu, '读取记录', () => { choice = 3; }, { kind: 'plaque', icon: 'load', why: hasAny || legacy ? null : '还没有记录。' });
      SG.UI.button(menu, '功业 · 图鉴', () => { choice = 4; }, { kind: 'plaque', icon: 'dossier', className: 'sg-tb-dossier', why: '开局之后在「菜单 → 卷宗」查看功业与图鉴。' });
      SG.UI.button(menu, '设　　置', () => { choice = 5; }, { kind: 'plaque', icon: 'settings' });
      SG.UI.button(menu, '操作说明', () => { choice = 2; }, { kind: 'plaque', icon: 'help' });
      h('div', 'sg-title-foot', '第四版 · 程序生成的山河与人物　　' + (SG.isTouch ? '拖动平移 · 双指缩放 · 轻触选取' : '拖动平移 · 滚轮缩放 · WASD 移动 · Q / E 旋转'), title);
      // 游戏中「读取」：菜单记下档位后重新载入页面，这里直接读入，跳过标题
      let bootSlot = null;
      try { bootSlot = sessionStorage.getItem('sanguozhi2_boot_load'); if (bootSlot) sessionStorage.removeItem('sanguozhi2_boot_load'); } catch (e) { bootSlot = null; }
      if (bootSlot) choice = 6;
      const loadInto = async (loaded) => {
        title.style.display = 'none';
        SG.G = loaded;
        await this.ensureMap(loaded.scenario);
        preloadPortraits();
        title.remove();
        UI().toast('读取进度：' + SG.esc(scenarioName(loaded.scenario)) + '　' + loaded.year + '年' + loaded.month + '月', 2.4);
        if (loaded.migratedFrom === 2) UI().toast('存档已升级：众将从 1 级起步，征战可获经验、习得新招', 3.2);
      };
      let ang = 0, last = performance.now();
      for (;;) {
        while (choice < 0) {
          const now = performance.now();
          const dt = Math.min(0.1, (now - last) / 1000);
          last = now;
          // 空闲环绕（设置第 33 行；减少动态时也停）
          const orbit = !(SG.UISettings && (SG.UISettings.get('cam.idleOrbit') === false || SG.UISettings.reducedMotion()));
          ang += dt * (orbit ? 2.5 : 0);
          rig.desiredYaw = Math.sin(ang * 0.05) * 25;
          rig.focusMap(58 + Math.sin(ang * 0.03) * 18, 48 + Math.cos(ang * 0.04) * 12, 85);
          await SG.frame();
        }
        if (choice === 2) {
          choice = -1;
          if (SG.MenuScreen && SG.MenuScreen.help) await SG.MenuScreen.help();
          else await UI().say(helpText(), null, null, { className: 'sg-help' });   // 字略小：七节一屏放得下
          last = performance.now();
          continue;
        }
        if (choice === 4 || choice === 5) {
          if (choice === 5 && SG.SettingsScreen) await SG.SettingsScreen.open();
          choice = -1;
          last = performance.now();
          continue;
        }
        if (choice === 3 || choice === 6) {
          let res = null;
          if (choice === 6) {
            let loaded = null;
            try { loaded = GS.loadSlot(/^\d$/.test(bootSlot) ? +bootSlot : bootSlot); } catch (e) { loaded = null; }
            if (loaded) res = { loaded };
            else { choice = -1; await UI().say(SG.esc(GS.loadError || '存档无法读取。')); last = performance.now(); continue; }
          } else if (SG.MenuScreen) res = await SG.MenuScreen.open('load', { fromTitle: true });
          if (!res || !res.loaded) { choice = -1; last = performance.now(); continue; }
          await loadInto(res.loaded);
          break;
        }
        if (choice === 1) {
          let loaded = null;
          try { loaded = newest && GS.loadSlot ? GS.loadSlot(newest.slot) : GS.load(); } catch (e) { console.warn(e); loaded = null; }
          if (!loaded) { try { loaded = GS.load(); } catch (e) { loaded = null; } }
          if (!loaded) {
            choice = -1;
            await UI().say(GS.loadError || '存档无法读取。');
            last = performance.now();
            continue;
          }
          await loadInto(loaded);
          break;
        }
        // 新的征程：选择剧本 → 选择君主；中途返回则回到标题
        const sid = await this.chooseScenario();
        if (sid) {
          title.style.display = 'none';
          rig.desiredYaw = 0;
          if (await this.selectRuler(sid)) { title.remove(); break; }
          title.style.display = '';
          rig.desiredYaw = 0;
          rig.focusMap(58, 48, 85, true);
        }
        choice = -1;
        last = performance.now();
      }
      rig.desiredYaw = 0;
      rig.inputEnabled = true;
      try { if (this.map && this.map.setMood) this.map.setMood(null); } catch (e) { /* 地图无氛围 */ }
      this.map.refresh(SG.G);
      await this.strategy.run();
    },

    // ---------------------------------------------------------- 选择剧本 --
    // → 'classic' | 'world' | null（取消）
    async chooseScenario() {
      const list = SCENARIOS.filter(s => s.id === 'classic' || (SG.Scenarios && SG.Scenarios[s.id]));
      if (list.length === 1) return list[0].id;
      const items = list.map(s => {
        const d = scenarioOf(s.id);
        const alive = new Set(d.Generals.map(l => l.split('|')[4]).filter(k => k !== '-'));
        return item(`<span class="sg-scen-name">${SG.esc(s.name)}</span>`,
          `${d.StartYear}年　城 ${d.Cities.length}　势力 ${d.Factions.filter(l => alive.has(l.split('|')[0])).length}`, true, SG.esc(s.desc));
      });
      const r = await UI().choose('选择剧本', items, null, 760);
      return r >= 0 ? list[r].id : null;
    },

    // ---------------------------------------------------------- 选择君主 --
    // 经典：单一列表；世界：先选地域再选君主。→ true（已开局）/ false（返回标题）
    async selectRuler(sid) {
      SG.ScenarioData = scenarioOf(sid);
      // 临时局面：供地图与列表显示（经典剧本沿用标题背景的那一份，随机数序列与旧版相同）
      if (!SG.G || SG.G.scenario !== sid) SG.G = SG.GameState.newGame(SG.ScenarioData.Factions[0].split('|')[0], sid);
      await this.ensureMap(sid);
      // 剧本简介：提到的武将带行内小头像（临时局面已有这些武将）
      await UI().say(SG.UI.faces(SG.esc(SG.ScenarioData.Intro), { factions: false, max: 8 }));
      const f0 = sid === 'world' && SG.ScenarioData.Regions ? await this.pickRulerByRegion() : await this.pickRulerList();
      this.map.select(-1);
      if (!f0) return false;
      SG.G = SG.GameState.newGame(f0.key, sid);
      preloadPortraits();
      return true;
    },

    _focusCapital(g, f, dist) {
      const cap = g.cities[g.ruler(f.id).city] || g.citiesOf(f.id)[0];
      if (!cap) return null;
      const p = SG.mapPos(cap);
      this._focusFree(p.x, p.y, dist);
      this.map.select(cap.id);
      return cap;
    },
    // 对准地图点，使其落在对话框右侧空白区的中央（镜头偏航为 0 时屏幕 x 即地图 x）
    _focusFree(x, y, dist) {
      const cam = SG.Gfx.camera, free = freeArea();
      if (free.dxPx > 0 && cam && cam.fov) {
        const visW = 2 * dist * Math.tan(cam.fov * Math.PI / 360) * cam.aspect;
        x -= free.dxPx / (window.innerWidth || 1) * visW;
      }
      this.rig.focusMap(x, y, dist);
    },

    // 经典剧本：单一列表（与旧版相同）
    async pickRulerList() {
      const g = SG.G;
      const factions = g.factions.filter(f => f.alive).map((f, i) => ({ f, n: g.cityCount(f.id), i }))
        .sort((a, b) => (b.n - a.n) || (a.i - b.i)).map(o => o.f);
      for (;;) {
        const items = factions.map(f => item(
          swatch(f.color) + ' ' + SG.esc(g.ruler(f.id).name),
          `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　德 ${f.virtue}　人望 ${f.fame}`, true, null, { avatar: g.ruler(f.id) }));
        const r = await UI().choose('选择君主　' + SG.ScenarioData.StartYear + '年 · ' + SG.ScenarioData.Title, items, '德越高越易登用人才；人望决定战场上的行动力。', 820);
        if (r < 0) return null;
        const f0 = factions[r];
        this._focusCapital(g, f0, 45);
        const ru = g.ruler(f0.id);
        const ok = await UI().confirm('以' + SG.esc(ru.name) + '开始？', '出阵', '再想想', {
          body: SG.UI.genCard(ru, { mood: 'win', sub: swatch(f0.color) + (f0.name !== ru.name ? SG.esc(f0.name) + '　' : '') + `城 ${g.cityCount(f0.id)}　将 ${g.generalsOf(f0.id).length}　德 ${f0.virtue}　人望 ${f0.fame}` }),
        });
        if (!ok) { this.map.select(-1); continue; }
        return f0;
      }
    },

    // 世界剧本第一步：选择地域（每行：地域名、几位君主的头像、势力数与城数）
    async pickRulerByRegion() {
      const g = SG.G;
      const rows = SG.ScenarioData.Regions.map(r => ({
        r, fs: r.factions.map(k => g.factionByKey(k)).filter(f => f && f.alive)
          .sort((a, b) => (g.cityCount(b.id) - g.cityCount(a.id)) || (a.id - b.id)),
      })).filter(o => o.fs.length > 0);
      let sel = 0;
      for (;;) {
        const items = rows.map(o => {
          const faces = o.fs.slice(0, 5).map(f => portraitHtml(g.ruler(f.id), 30)).join('');
          const more = o.fs.length > 5 ? `<span class="sg-region-more">+${o.fs.length - 5}</span>` : '';
          const it = item(`<span class="sg-region-name">${SG.esc(o.r.name)}</span><span class="sg-region-faces">${faces}${more}</span>`,
            `势力 ${o.fs.length}　城 ${o.fs.reduce((s, f) => s + g.cityCount(f.id), 0)}`, true,
            o.fs.slice(0, 6).map(f => SG.esc(f.name)).join('、') + (o.fs.length > 6 ? ' 等' : ''));
          return it;
        });
        items[sel].selected = true;
        const p = UI().choose('选择地域　' + SG.ScenarioData.StartYear + '年 · ' + SG.ScenarioData.Title, items, '先选地域，再选该地域的君主。', 760);
        const bm = sideModal();
        if (bm) { const panel = bm.querySelector('.sg-dialog'); if (panel) panel.classList.add('sg-region-pick'); }
        hookListHover(i => this._flyToFactions(g, rows[i].fs), sel);
        const r = await p;
        if (r < 0) return null;
        sel = r;
        this._flyToFactions(g, rows[r].fs);
        const f = await this.pickRulerIn(rows[r]);
        if (f) return f;
      }
    },
    // 镜头飞往一组势力的都城（看到全部）
    _flyToFactions(g, fs) {
      let xMin = Infinity, yMin = Infinity, xMax = -Infinity, yMax = -Infinity;
      for (const f of fs) {
        const cap = g.cities[g.ruler(f.id).city];
        if (!cap) continue;
        const p = SG.mapPos(cap);
        xMin = Math.min(xMin, p.x); xMax = Math.max(xMax, p.x); yMin = Math.min(yMin, p.y); yMax = Math.max(yMax, p.y);
      }
      if (!(xMax >= xMin)) return;
      const pad = 14;
      xMin -= pad; yMin -= pad; xMax += pad; yMax += pad;
      this.map.select(-1);
      // 能在对话框右侧空白区看到整个范围的距离（按视口宽高比与俯角估算，同 CameraRig.fitRect）
      const cam = SG.Gfx.camera, rig = this.rig, free = freeArea();
      const t = Math.tan((cam && cam.fov ? cam.fov : 34) * Math.PI / 360);
      const aspect = (cam && cam.aspect > 0 ? cam.aspect : 16 / 9) * free.frac;
      const pitch = ((rig.pitch || 52) + 4) * Math.PI / 180;
      const d = Math.max((yMax - yMin) * Math.sin(pitch) / (2 * t), (xMax - xMin) / (2 * t * aspect)) * 1.05;
      this._focusFree((xMin + xMax) / 2, (yMin + yMax) / 2, Math.max(rig.minDist, Math.min(rig.maxDist, d)));
    },

    // 世界剧本第二步：该地域的君主（头像、势力名、城 / 将 / 德 / 人望）；悬停时镜头飞往都城
    async pickRulerIn(row) {
      const g = SG.G, FI = SG.FactionInfo || {};
      const fs = row.fs;
      for (;;) {
        const items = fs.map(f => {
          const ru = g.ruler(f.id);
          const full = FI[f.key] && FI[f.key].full;
          const fname = f.name !== ru.name && !full ? `<small class="sg-faction-name">${SG.esc(f.name)}</small>` : '';
          return item(swatch(f.color) + ` <b>${SG.esc(ru.name)}</b>${fname}`,
            `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　德 ${f.virtue}　人望 ${f.fame}`, true,
            full ? SG.esc(full) : null, { avatar: ru });
        });
        const p = UI().choose('选择君主　' + row.r.name, items, '德越高越易登用人才；人望决定战场上的行动力。', 760);
        sideModal();
        hookListHover(i => this._focusCapital(g, fs[i], 58), 0);
        const r = await p;
        if (r < 0) { this.map.select(-1); return null; }
        const f0 = fs[r];
        const card = this.factionCard(f0);
        this._focusCapital(g, f0, 45);
        if (await card) return f0;
        this.map.select(-1);
      }
    },

    // 开局确认卡片：君主头像、势力全名、史实介绍、游戏取舍；「出阵」/「再想想」
    factionCard(f) {
      const g = SG.G, FI = (SG.FactionInfo || {})[f.key], ru = g.ruler(f.id);
      return new Promise(resolve => {
        const m = UI().openModal('sg-confirm sg-faction-card', { width: 700 });
        m.blocker.classList.add('sg-side-modal');
        let done = false;
        const finish = v => { if (done) return; done = true; m.close(); resolve(v); };
        const head = h('div', 'sg-dlg-head', null, m.panel);
        h('div', 'sg-dlg-title', '以' + SG.esc(ru.name) + (f.name !== ru.name ? '（' + SG.esc(f.name) + '）' : '') + '开始？', head);
        const close = SG.UI.button(head, '×', () => finish(false), { className: 'sg-close', ariaLabel: '关闭' });
        const body = h('div', 'sg-fcard-body', null, m.panel);
        if (SG.Portrait) { try { body.appendChild(SG.Portrait.el(ru, { size: 112 })); } catch (e) { /* 无头像 */ } }
        const txt = h('div', 'sg-fcard-text', null, body);
        h('div', 'sg-fcard-full', swatch(f.color) + SG.esc(FI && FI.full ? FI.full : f.name), txt);
        h('div', 'sg-fcard-stats', `城 ${g.cityCount(f.id)}　将 ${g.generalsOf(f.id).length}　德 ${f.virtue}　人望 ${f.fame}`, txt);
        if (FI && FI.note) h('div', 'sg-fcard-note', SG.esc(FI.note), txt);
        if (FI && FI.liberty) h('div', 'sg-fcard-liberty', '<b>游戏取舍</b>' + SG.esc(FI.liberty), txt);
        const row = h('div', 'sg-confirm-btns', null, m.panel);
        const bNo = SG.UI.button(row, '再想想', () => finish(false));
        const bYes = SG.UI.button(row, '出阵', () => finish(true), { primary: true });
        m.onKey = k => {
          const a = document.activeElement;
          if (k === 'Escape') { finish(false); return true; }
          if (k === 'Enter' || k === 'Space') { finish(!(a === bNo || a === close)); return true; }
          if (k === 'ArrowLeft' || k === 'ArrowRight') { try { (a === bYes ? bNo : bYes).focus({ preventScroll: true }); } catch (e) { /* 忽略 */ } return true; }
          return false;
        };
        try { bYes.focus({ preventScroll: true }); } catch (e) { /* 忽略 */ }
      });
    },
  };

  SG.Game = Game;
})();
