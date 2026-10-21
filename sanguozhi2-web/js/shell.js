'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 战略画面的外壳（UI-V4 §5）
   顶梁（君主章 · 声望 · 势力匾 · 年历签 · 令箭筒 · 金粮城计数 · 铜钮 武将 官制 势力 大事 | 设置 菜单）、两盏挂灯、
   案头（本月待办 ≤ 4 张纸签）、城池面板（漆框：城名 · 主人 · 城级 · 入城 · 收；纸面账册：开发条、金粮兵防、武将行；
   4 × 4 指令坞 治 · 人 · 运 · 策，每格一个指令印图标，快捷键 1–9 0 - = Z X C V）、结束本月朱印、换月演出、电脑回合卷轴、
   昼夜（SG.Clock → --night）、键盘（G T P H O · Esc 菜单 · Enter 结束本月 · M 静音 · Ctrl/⌘+S 快速记录）。
   由 js/strategy-screen.js 调用；规则一律不碰（只读 SG.G）。

   SG.Shell.build(ss) → hud          ss = StrategyScreen 实例（取 busy / do / 指令处理函数）
   SG.Shell.refresh(g)               顶梁、案头、结束本月、昼夜（计数器有变化时滚动 + 增减签）
   SG.Shell.cityPanel(c)             城池面板的头与账册（指令由 beginDock / addCmd / endDock 填）
   SG.Shell.beginDock() · addCmd(name, why, action, { primary }) → btn · endDock()   固定 16 格；没登记的格显示为灰并说明原因
   SG.Shell.closePanel()             收起城池面板
   SG.Shell.spendToken()             一支令箭从箭筒飞向刚按下的指令印
   SG.Shell.monthTransition(g) → Promise     换月（黄昏 → 月名书写 → 黎明，任意键跳过；减少动态时 0.3 秒淡入淡出）
   SG.Shell.aiScroll(on)             「诸侯行动中」挂轴
   SG.Shell.setNight(v)              --night（0 … 1）
   SG.Shell.occluders() → [元素]     城名避让的遮挡物（顶梁、案头、城池面板、结束本月）
   SG.Shell.playMs()                 本局已游玩的毫秒数（随存档：G.playMs）
   SG.Shell.fitBeam()                顶梁的溢出次序 fit-1 … fit-9（UI-V4 §5.2 / §15.1；窗口尺寸、字号、内容变化时自动重量）
   SG.Shell.toggleMute() · muted     M：音乐、音效、人声与朗读一并静音
   SG.UITest.open(screen, opts) · closeAll() · layoutReport()   版式测试钩子（tests/ui-layout.html；?uitest=1 时记录页面错误）
   类名：.sg-beam .sg-medallion .sg-plaquewrap .sg-cal .sg-quiver .sg-res .sg-beam-acts(.sg-topbar-actions) .sg-lantern .sg-agenda .sg-slip
         .sg-citypanel(.sg-cp-head .sg-cp-ledger .sg-dock(.sg-cmdgrid) .sg-tile) .sg-genrow .sg-endseal .sg-monthfx .sg-aiscroll .sg-dusk
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;
  const G = () => SG.G;
  const UI = () => SG.UI;
  const esc = s => SG.esc(String(s === undefined || s === null ? '' : s));
  const icon = (id, size, opts) => (SG.UIIcons ? SG.UIIcons.svg(id, size, opts) : '');
  const n0 = v => Math.round(v).toLocaleString('en-US');
  // 「万」记数：63,000 → 6.3万，120,000 → 12万；一万以下照常（顶梁的 fit-6）
  const nWan = v => { v = Math.round(v); const a = Math.abs(v); return a >= 10000 ? (v / 10000).toFixed(a >= 100000 ? 0 : 1) + '万' : n0(v); };
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  const sfx = (n, v) => { try { if (SG.UI && SG.UI.sfx) SG.UI.sfx(n, v); } catch (e) { /* 无音频 */ } };
  const reduced = () => !!(SG.Motion && (SG.Motion.reduced || SG.Motion.level === 'off'));
  const motionLite = () => !!(SG.Motion && SG.Motion.level === 'lite');
  const US = id => (SG.UISettings ? SG.UISettings.get(id) : undefined);

  // ------------------------------------------------------------------ 历法 --
  const MONTH_ZH = ['', '正月', '二月', '三月', '四月', '五月', '六月', '七月', '八月', '九月', '十月', '冬月', '腊月'];
  const SEASON = ['', 'spring', 'spring', 'spring', 'summer', 'summer', 'summer', 'autumn', 'autumn', 'autumn', 'winter', 'winter', 'winter'];
  const SEASON_ZH = ['', '孟春', '仲春', '季春', '孟夏', '仲夏', '季夏', '孟秋', '仲秋', '季秋', '孟冬', '仲冬', '季冬'];
  const TERM = ['', '立春', '惊蛰', '清明', '立夏', '芒种', '小暑', '立秋', '白露', '寒露', '立冬', '大雪', '小寒'];
  const DIGIT = '〇一二三四五六七八九';
  const yearZh = y => String(y).split('').map(d => DIGIT[+d] || d).join('');
  const monthZh = m => MONTH_ZH[m] || m + '月';

  // ------------------------------------------------------------------ 指令坞（UI-V4 §5.4）--
  // [组, 组色, [[指令名, 图标, 用令牌]×4]]；指令名与 strategy-screen.js 的 addCmd 名一致
  const DOCK = [
    ['治', 'var(--jade)', [['开发', 'develop', 1], ['营建', 'build', 1], ['征兵', 'recruit', 1], ['训练', 'train', 1]]],
    ['人', 'var(--plum)', [['搜索', 'search', 1], ['登用', 'hire', 1], ['赏赐', 'reward', 0], ['任命', 'appoint', 0]]],
    ['运', 'var(--ochre)', [['移动', 'move', 1], ['输送', 'transport', 1], ['交易', 'trade', 0], ['市场', 'market', 0]]],
    ['策', 'var(--zhu)', [['通商', 'caravan', 1], ['探索', 'explore', 1], ['外交', 'diplomacy', 1], ['出征', 'attack', 1]]],
  ];
  const DOCK_KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0', '-', '=', 'Z', 'X', 'C', 'V'];
  const MISSING = {
    营建: '「营建」随第四版的营建系统开放（尚未载入）。',
    通商: '「通商」随第四版的商队系统开放（尚未载入）。',
    探索: '「探索」随第四版的探索系统开放（尚未载入）。',
    市场: '装备系统未开启，没有市场。',
  };
  const CMD_DESC = {
    开发: '开发土地、产业或町（50 金）', 营建: '兴建工程：市集、水渠、学宫……', 征兵: '为武将招募士兵', 训练: '提高部队的训练度',
    搜索: '寻访在野人才、钱粮与宝物', 登用: '招揽已发现的在野人才', 赏赐: '赏金或赐宝物，提高忠诚', 任命: '任命太守与官爵',
    移动: '调动武将到相连的己方城池', 输送: '在己方城池之间调拨金粮', 交易: '买粮、卖粮', 市场: '兵器、甲胄与宝物',
    通商: '派商队往来获利', 探索: '派人探访名山古迹', 外交: '同盟、离间、拉拢', 出征: '攻打相邻的敌城或空城',
  };

  const Shell = {
    ss: null, hud: null, el: {}, counters: {}, dock: null, cmds: new Map(), lastTile: null,
    dismissed: new Set(), _city: -1, _built: false, _t0: 0, _playMs0: 0, _keyHooked: false,
    DOCK, DOCK_KEYS,

    // ================================================================ 搭建 --
    build(ss) {
      this.ss = ss;
      const hud = this.hud = SG.UI.screen('sg-strategy-hud sg-shell');
      const E = this.el;
      E.dusk = h('div', 'sg-dusk', null, hud);
      // ---- 顶梁 ----
      const beam = E.beam = h('div', 'sg-beam sg-lacquer', null, hud);
      beam.setAttribute('role', 'toolbar');
      beam.setAttribute('aria-label', '顶梁');
      E.medal = h('button', 'sg-medallion', null, beam);
      E.medal.type = 'button';
      E.medal.addEventListener('click', () => { const g = G(); if (!this.ss.busy && SG.CharacterScreen && g) SG.CharacterScreen.open(g.ruler(g.player)); });
      E.plaque = h('div', 'sg-plaquewrap', null, beam);
      E.plaque.addEventListener('click', () => this.ss.do(() => this.ss.factionInfo()));
      const center = E.center = h('div', 'sg-beam-center', null, beam);
      E.cal = h('div', 'sg-cal', null, center);
      E.quiver = h('div', 'sg-quiver', null, center);
      const res = E.res = h('div', 'sg-res', null, center);
      const resFmt = v => (this._compact ? nWan(v) : n0(v));      // 顶梁挤不下时（fit-6）金粮按「万」记
      this.counters.gold = SG.UI.counter(h('span', 'sg-res-c is-gold', null, res), { icon: 'gold', value: 0, sound: 'ui-coins', format: resFmt });
      this.counters.food = SG.UI.counter(h('span', 'sg-res-c is-food', null, res), { icon: 'food', value: 0, sound: 'ui-grain', soundOut: 'ui-grain', format: resFmt });
      this.counters.city = SG.UI.counter(h('span', 'sg-res-c is-city', null, res), { icon: 'city', value: 0, sound: false });
      E.missions = h('div', 'sg-missions sg-hidden', null, center);
      const acts = E.acts = h('div', 'sg-beam-acts sg-topbar-actions', null, beam);
      const ib = (ic, label, key, fn) => SG.UI.button(acts, '', fn, { kind: 'icon', icon: ic, label, hotkey: key });
      E.bGenerals = ib('generals', '武将', 'G', () => this.openGenerals());
      E.bTitles = ib('titles', '官制', 'T', () => this.openTitles());
      E.bFactions = ib('factions', '势力', 'P', () => this.ss.do(() => this.ss.factionInfo()));
      E.bChron = ib('chronicle', '大事', 'H', () => this.ss.do(() => this.ss.chronicle()));
      h('i', 'sg-beam-sep', null, acts);
      E.bSettings = ib('settings', '设置', 'O', () => this.openSettings());
      E.bMenu = ib('menu', '菜单', 'Esc', () => this.openMenu());
      // ---- 挂灯 ----
      E.lan1 = h('div', 'sg-lantern', '<i class="sg-lantern-glow"></i>' + SG.UIArt.lantern(46, false), hud);
      E.lan2 = h('div', 'sg-lantern is-b', '<i class="sg-lantern-glow"></i>' + SG.UIArt.lantern(46, false), hud);
      // ---- 案头 ----
      E.agenda = h('div', 'sg-agenda', null, hud);
      E.agenda.setAttribute('aria-label', '案头：本月待办');
      // ---- 城池面板 ----
      const cp = E.panel = h('div', 'sg-citypanel sg-lacquer', null, hud);
      cp.style.display = 'none';
      const head = E.cpHead = h('div', 'sg-cp-head sg-citypanel-head', null, cp);
      E.cpName = h('div', 'sg-cp-name sg-goldleaf', '', head);
      E.cpOwn = h('div', 'sg-cp-own', '', head);
      h('div', 'sg-cp-sp', null, head);
      E.cpEnter = SG.UI.button(head, '入城', () => this.enterCity(), { icon: 'gate', size: 'small', className: 'sg-cp-enter' });
      E.cpClose = SG.UI.button(head, '', () => this.ss.selectCity(-1), { className: 'sg-close', ariaLabel: '收起城池面板' });
      const body = E.cpBody = h('div', 'sg-cp-body', null, cp);
      E.ledger = h('div', 'sg-cp-ledger sg-paper is-paper', null, body);
      E.dock = h('div', 'sg-dock sg-cmdgrid', null, body);
      E.strip = h('div', 'sg-cp-strip sg-hidden', null, body);
      // ---- 结束本月 ----
      const es = E.endseal = h('div', 'sg-endseal', null, hud);
      E.endBtn = h('button', 'sg-btn k-endseal', '<span class="sg-es-disc">' + icon('endMonth') + '</span><span class="sg-es-rib">结束本月<small class="sg-es-left"></small></span>', es);
      E.endBtn.type = 'button';
      E.endBtn.setAttribute('aria-label', '结束本月');
      E.endBtn.addEventListener('pointerdown', () => sfx('ui-press-wood'));
      E.endBtn.addEventListener('click', e => { if (this.ss.busy || this.ss.endMonth) { SG.UI.deny(E.endBtn, this.ss.busy ? '指令执行中，请稍候。' : null); return; } if (e.detail > 0) E.endBtn.blur(); this.ss.do(() => this.ss.confirmEnd()); });
      // 让城名避让知道这些遮挡物
      this._installKeys();
      // 手机「返回」：关掉最上层的窗口；空地图上打开菜单（UI-V4 §6）
      if (SG.isTouch && SG.UI.installBackGuard) SG.UI.installBackGuard(() => {
        if (!this.hud || this.hud.style.display === 'none' || (SG.Gfx && SG.Gfx.topScreen && SG.Gfx.topScreen()) || this.ss.busy) return;
        if (this._city >= 0) this.ss.selectCity(-1); else this.openMenu();
      });
      this._t0 = performance.now();
      const g = G();
      this._playMs0 = g && g.playMs ? g.playMs : 0;
      // 开局：顶梁落下、计数器从 0 滚起（UI-V4 §13.3）
      if (!reduced()) {
        SG.Motion.play(beam, [{ transform: 'translateY(-70px)' }, { transform: 'none' }], { dur: 650, ease: 'spring' });
        for (const l of [E.lan1, E.lan2]) SG.Motion.play(l, [{ rotate: '12deg' }, { rotate: '0deg' }], { dur: 900, ease: 'spring', fill: 'none' });
      }
      this._built = true;
      this.refresh(g, true);
      if (!this._fitHooked) {
        this._fitHooked = true;
        const later = () => { clearTimeout(this._fitT); this._fitT = setTimeout(() => { this.fitBeam(); }, 120); };
        addEventListener('resize', later);
        // 「界面字号」（设置第 43 行）改变根字号 → 重量顶梁
        try { if (SG.Settings && SG.Settings.on) SG.Settings.on('ui.textScale', later); } catch (e) { /* 忽略 */ }
      }
      setTimeout(() => this.fitBeam(), 0);
      try { if (document.fonts && document.fonts.ready) document.fonts.ready.then(() => this.fitBeam()); } catch (e) { /* 忽略 */ }
      return hud;
    },
    // 顶梁的溢出次序（UI-V4 §5.2 / §15.1）：内容比梁宽时逐级加 fit-1 … fit-9（任何宽度、任何字号都量；窗口尺寸与「界面字号」改变时重量）。
    // 1–5 为规格的次序；之后（130 % 字号的手机 + 安全区、667 宽）先把金粮改记「万」（6），再省略过长的势力名（7），
    // 最后才把粮并入金的说明（8）、藏起势力匾（9）
    FIT: ['fit-1', 'fit-2', 'fit-3', 'fit-4', 'fit-5', 'fit-6', 'fit-7', 'fit-8', 'fit-9'],
    fitBeam() {
      const E = this.el, b = E.beam;
      if (!b || !b.isConnected) return;
      // 横向挤不下；或势力匾（匾 + 说明行）比梁高（宽屏根字号 24 px 时说明行压到梁下的彩画带上）
      const over = () => b.scrollWidth > b.clientWidth + 1 || (E.center && E.center.scrollWidth > E.center.clientWidth + 1) ||
        (E.acts && E.acts.getBoundingClientRect().right > b.getBoundingClientRect().right - 2) ||
        (E.plaque && E.plaque.offsetHeight > b.clientHeight + 1);
      // 量的时候关掉过渡：减少动态时 #ui * 的过渡是 0.01 ms（transition-property 缺省 all），getComputedStyle 在下一帧前仍给旧值，
      // 间距、字号的收紧就量不出来（会一路收到藏粮、藏匾）
      b.classList.add('sg-fitting');
      b.classList.remove(...this.FIT);
      this._setCompact(false);
      for (const k of this.FIT) { if (!over()) break; b.classList.add(k); if (k === 'fit-6') this._setCompact(true); }
      b.dataset.fit = (b.className.match(/fit-\d/g) || []).length;
      b.classList.remove('sg-fitting');
    },
    _setCompact(on) {
      if (!!this._compact === on) return;
      this._compact = on;
      for (const c of [this.counters.gold, this.counters.food]) if (c && c.reformat) c.reformat();
    },
    _fitSoon() {
      if (this._fitRaf) return;
      this._fitRaf = requestAnimationFrame(() => { this._fitRaf = 0; this.fitBeam(); });
    },
    occluders() {
      const E = this.el;
      return [E.beam, E.agenda, E.panel, E.endseal].filter(Boolean);
    },
    playMs() { return this._playMs0 + Math.max(0, performance.now() - this._t0); },
    syncPlayMs() { const g = G(); if (g) { g.playMs = Math.round(this.playMs()); } },

    // ================================================================ 刷新 --
    refresh(g, first) {
      g = g || G();
      if (!this._built || !g || !g.playerFaction) return;
      const E = this.el;
      const f = g.playerFaction, ru = g.ruler(g.player);
      // ---- 君主章 + 声望 ----
      const prestige = typeof f.prestige === 'number' ? f.prestige : null;
      const fameV = SG.Mods && SG.Mods.fame ? SG.Mods.fame(f.id) : f.fame;
      const medKey = (ru ? ru.name : '') + '|' + (SG.UI.rankOf(ru)) + '|' + (prestige === null ? 'f' + fameV : prestige);
      if (E.medal._key !== medKey) {
        E.medal._key = medKey;
        E.medal.innerHTML = (ru ? SG.UI.frame(ru, { size: E.medal.offsetWidth > 60 || !this._short() ? 72 : 46, round: true, mood: 'win' }) : '') +
          '<span class="sg-medallion-pres">' + icon('prestige') + '<b class="sg-num">' + (prestige === null ? fameV : n0(prestige)) + '</b></span>';
        E.medal.setAttribute('aria-label', (ru ? ru.name : '') + ' · 人物');
        SG.UI.tip(E.medal, () => '<b>' + esc(ru ? ru.name : '') + '</b> · ' + (f.rank ? esc(f.rank.name || '') : '君主') + '<br>德 ' + f.virtue + ' · 人望 ' + fameV + (prestige !== null ? ' · 声望 ' + n0(prestige) : '') + '<br><span class="sg-muted">点按：君主的人物菜单</span>');
      }
      // ---- 势力匾 ----
      const fname = ru && f.name === ru.name ? f.name + '军' : f.name;
      const plKey = fname + '|' + f.color;
      if (E.plaque._key !== plKey) {
        E.plaque._key = plKey;
        const scen = g.scenario === 'world' ? '天下大势' : (SG.ScenarioData && SG.ScenarioData.Title) || '董卓专横';
        E.plaque.innerHTML = '<span class="sg-plaque"><i class="sg-flag" style="--fc:' + f.color + '"></i>' + esc(fname) + '</span>' +
          '<span class="sg-plaque-sub">君主 ' + esc(ru ? ru.name : '') + ' · ' + esc(scen) + '</span>';
        E.plaque.setAttribute('role', 'button');
        E.plaque.setAttribute('aria-label', fname + ' · 势力');
        E.plaque.tabIndex = 0;
      }
      // ---- 年历签 ----
      this._renderCal(g);
      // ---- 令箭筒 ----
      const total = Math.max(1, g.tokensFor ? g.tokensFor(g.player) : g.tokens), left = Math.max(0, g.tokens);
      const qKey = total + '|' + left;
      if (E.quiver._key !== qKey) {
        const was = E.quiver._key;
        E.quiver._key = qKey;
        E.quiver.innerHTML = SG.UIArt.quiver(Math.min(10, total), Math.min(10, total) - Math.min(10, left)) + '<span class="sg-quiver-n sg-num">' + left + '<small>/' + total + '</small></span>';
        E.quiver.classList.toggle('is-empty', left <= 0);
        const nCities = g.cityCount(g.player);
        SG.UI.tip(E.quiver, '<b>本月令牌 ' + left + ' / ' + total + '</b><br>城 ' + nCities + ' → 每 2 城 +1 枚（上限 10）');
        if (was && left === total && was !== qKey && !first) this._refillAnim();
      }
      // ---- 金 · 粮 · 城 ----
      const mine = g.citiesOf(g.player);
      let gold = 0, food = 0;
      for (const c of mine) { gold += c.gold; food += c.food; }
      this.counters.gold.set(gold, { delta: !first, instant: first && reduced() });
      this.counters.food.set(food, { delta: !first });
      this.counters.city.set(mine.length, { delta: !first });
      if (!this._resTip) {
        this._resTip = true;
        SG.UI.tip(this.counters.gold.el, () => this._goldTip());
        SG.UI.tip(this.counters.food.el, () => this._foodTip());
        SG.UI.tip(this.counters.city.el, () => '<b>城 ' + G().cityCount(G().player) + '</b>');
      }
      // ---- 铜钮的角标与可用性 ----
      const busy = this.ss.busy;
      const learn = g.generalsOf(g.player).some(x => !x.dead && this._canLearn(x));
      E.bGenerals.setBadge(learn ? '新' : null);
      const titlesOn = !!(SG.TitleUI && SG.Titles && SG.Titles.enabled !== false);
      if (titlesOn) { let n = 0; try { n = SG.TitleUI.badge(g.player); } catch (e) { n = 0; } E.bTitles.setBadge(n > 0 ? n : null); E.bTitles.setEnabled(true); }
      else E.bTitles.setEnabled('官制随第四版的官爵系统开放（尚未载入）。');
      // ---- 结束本月 ----
      E.endBtn.classList.toggle('is-suggest', left <= 0 && !busy);
      E.endBtn.disabled = false;
      E.endBtn.setAttribute('aria-disabled', busy ? 'true' : 'false');
      E.endBtn.querySelector('.sg-es-left').textContent = left > 0 ? '余 ' + left + ' 令' : '';
      // ---- 昼夜、案头 ----
      this._applyClock();
      this.agenda(g);
      // 顶梁内容（计数器位数、年历、令箭数）变了也重量一次（合并到下一帧，量一次版面）
      const fk = innerWidth + '|' + f.name + '|' + E.cal._key + '|' + qKey + '|' + String(gold).length + String(food).length + '|' + document.documentElement.style.getPropertyValue('--sg-text-k');
      if (first || this._fitKey !== fk) { this._fitKey = fk; if (first) this.fitBeam(); else this._fitSoon(); }
      this.syncPlayMs();
    },
    _short() { return window.innerHeight < 560; },
    _canLearn(gen) {
      const P = SG.Progress;
      if (!P || P.enabled === false || typeof P.openings !== 'function') return false;
      try { return P.openings(gen).total > 0 && P.candidates(gen, 1).length > 0; } catch (e) { return false; }
    },
    _goldTip() {
      const g = G();
      const mine = g.citiesOf(g.player);
      let inc = 0;
      try { for (const c of mine) inc += SG.Econ && SG.Econ.cityGold ? SG.Econ.cityGold(c).gold : 0; } catch (e) { inc = 0; }
      const rows = mine.slice(0, 8).map(c => esc(c.name) + ' ' + n0(c.gold)).join('　');
      const b = this.el.beam;
      const folded = b && b.classList.contains('fit-8') ? '<br><b>粮 ' + n0(mine.reduce((s, c) => s + c.food, 0)) + '</b>' : '';
      const cityLine = b && b.classList.contains('fit-3') ? '　城 ' + mine.length : '';
      return '<b>金 ' + n0(mine.reduce((s, c) => s + c.gold, 0)) + '</b>' + (inc ? '　<span class="sg-good">每月约 +' + n0(inc) + '</span>' : '') + cityLine + folded + '<br>' + rows + (mine.length > 8 ? ' …' : '');
    },
    _foodTip() {
      const g = G();
      const mine = g.citiesOf(g.player);
      const B = SG.Balance;
      let h = 0;
      for (const c of mine) h += c.land * (B.FoodPerLand || 0);
      return '<b>粮 ' + n0(mine.reduce((s, c) => s + c.food, 0)) + '</b>' + (h ? '<br>七月秋收约 +' + n0(h) : '') + '<br>' + mine.slice(0, 8).map(c => esc(c.name) + ' ' + n0(c.food)).join('　');
    },
    _renderCal(g) {
      const E = this.el;
      const nx = this._nextThing(g);
      const key = g.year + '|' + g.month + '|' + nx.key;
      if (E.cal._key === key) return;
      const flip = !!E.cal._key && E.cal._key.split('|')[1] !== String(g.month);
      E.cal._key = key;
      const draw = () => {
        E.cal.innerHTML = icon(SEASON[g.month] || 'calendar') + '<div class="sg-cal-d"><div class="sg-cal-yr sg-num">' + g.year + ' 年</div><div class="sg-cal-mo">' + monthZh(g.month) + '</div></div>' +
          '<div class="sg-cal-next">' + icon(nx.icon) + '<span>' + nx.html + '</span></div>';
      };
      if (flip && !reduced() && E.cal.animate) {
        sfx('ui-page');
        SG.Motion.play(E.cal, [{ transform: 'rotate(-1deg) rotateX(0deg)' }, { transform: 'rotate(-1deg) rotateX(-90deg)' }], { dur: 160, fill: 'none' }).then(() => {
          draw();
          SG.Motion.play(E.cal, [{ transform: 'rotate(-1deg) rotateX(90deg)' }, { transform: 'rotate(-1deg) rotateX(0deg)' }], { dur: 160, fill: 'none' });
        });
      } else draw();
      SG.UI.tip(E.cal, () => this._yearStrip(g));
      E.cal.onclick = () => { if (SG.MenuScreen && SG.MenuScreen.open) SG.MenuScreen.open('dossier'); };
    },
    // 年历签的「下一件事」：武林大会（正月）、秋收（七月）、文会（九月）……
    _nextThing(g) {
      const m = g.month;
      const ev = [];
      if (SG.Tourney && SG.Tourney.enabled !== false) ev.push([1, '武林大会', 'tourney']);
      ev.push([7, '秋收', 'harvest']);
      if (SG.Engage && SG.Engage.enabled !== false) ev.push([9, '文会', 'book']);
      for (let d = 0; d < 12; d++) {
        const mm = (m - 1 + d) % 12 + 1;
        const e = ev.find(x => x[0] === mm);
        if (e) return { key: mm + e[1], icon: e[2], html: (d === 0 ? '本月' : monthZh(mm)) + '<br>' + e[1] };
      }
      return { key: 'none', icon: 'calendar', html: '' };
    },
    _yearStrip(g) {
      let s = '<b>' + g.year + ' 年 · ' + monthZh(g.month) + '</b>　' + SEASON_ZH[g.month] + ' · ' + TERM[g.month] + '<br>';
      for (let m = 1; m <= 12; m++) s += '<span class="' + (m === g.month ? 'sg-goldc' : 'sg-muted') + '">' + monthZh(m).replace('月', '') + '</span>' + (m === 7 ? '<small>（秋收）</small>' : m === 1 && SG.Tourney ? '<small>（武林大会）</small>' : '') + (m < 12 ? '·' : '');
      return s;
    },
    _refillAnim() {
      if (reduced()) { sfx('ui-token-refill', 0.8); return; }
      const arrows = this.el.quiver.querySelectorAll('.arrow');
      arrows.forEach((a, i) => { a.style.setProperty('--i', i); });
      this.el.quiver.classList.remove('is-refill'); void this.el.quiver.offsetWidth; this.el.quiver.classList.add('is-refill');
      sfx('ui-token-refill', 0.8);
      setTimeout(() => this.el.quiver.classList.remove('is-refill'), 1200);
    },
    // 昼夜：SG.Clock（INTEGRATION §10.7）→ --night；设置「昼夜光影」关时恒为白天
    _applyClock() {
      let n = 0;
      try { if (US('ui.daynight') !== false && SG.Clock) n = SG.Clock.night(); } catch (e) { n = 0; }
      this.setNight(n);
    },
    setNight(v) {
      v = M.clamp01(+v || 0);
      document.documentElement.style.setProperty('--night', v.toFixed(3));
      document.documentElement.classList.toggle('sg-night', v > 0.3);
      if (this.el.lan1) {
        const lit = v > 0.3;
        if (this.el.lan1._lit !== lit) {
          this.el.lan1._lit = lit;
          for (const l of [this.el.lan1, this.el.lan2]) l.innerHTML = '<i class="sg-lantern-glow"></i>' + SG.UIArt.lantern(46, lit);
        }
      }
    },

    // ================================================================ 案头（UI-V4 §5.5）--
    agenda(g) {
      g = g || G();
      const E = this.el;
      if (!E.agenda || !g) return;
      const mk = g.year * 12 + g.month;
      const slips = [];
      const add = (id, pri, cls, ic, title, line, go, fn) => { if (!this.dismissed.has(mk + ':' + id)) slips.push({ id, pri, cls, ic, title, line, go, fn }); };
      // 1 定期大事（武林大会、文会、节庆……由各系统登记：SG.Shell.agendaSources）
      for (const src of Shell.agendaSources) { try { const r = src(g); if (r) for (const x of [].concat(r)) add(x.id, x.pri || 1, x.cls || 'hot', x.icon || 'calendar', x.title, x.line || '', x.go || '查看', x.open); } catch (e) { /* 忽略 */ } }
      const mineGens = g.generalsOf(g.player).filter(x => !x.dead);
      // 3 可习得新招
      const learners = mineGens.filter(x => this._canLearn(x));
      if (learners.length) {
        add('learn', 3, 'jade', 'level', '可习得新招', '<span class="sg-slip-faces">' + learners.slice(0, 3).map(x => SG.UI.frame(x, { size: 24 })).join('') + '</span>' + (learners.length > 3 ? ' 等 ' + learners.length + ' 人' : ''),
          '查看', () => { if (SG.CharacterScreen) SG.CharacterScreen.open(learners[0], { list: learners, tab: 'skills' }); });
      }
      // 4 官制空缺
      if (SG.TitleUI && SG.Titles && SG.Titles.enabled !== false) {
        let n = 0; try { n = SG.TitleUI.badge(g.player); } catch (e) { n = 0; }
        if (n > 0) add('titles', 4, 'plum', 'titles', '官制空缺 ' + n, '有合格人选可授官', '授官', () => this.openTitles());
      }
      // 5 市中珍品 / 胡商
      if (SG.Items && SG.Items.enabled !== false && g.items) {
        for (const c of g.citiesOf(g.player)) {
          const sh = c.shop;
          if (!sh) continue;
          if (sh.merchant && sh.merchant.stock && sh.merchant.stock.length) { add('merchant' + c.id, 5, '', 'market', '胡商抵达', esc(c.name) + '市中来了一位胡商', '市场', () => this.openMarket(c)); break; }
          const rare = (sh.stock || []).find(e => { try { return SG.Items.tier(e.id) >= 2 && e.qty > 0; } catch (err) { return false; } });
          if (rare) {
            let nm = rare.id; try { nm = SG.Items.get(rare.id).name; } catch (e) { /* 原编号 */ }
            add('rare' + c.id, 5, '', 'market', '市有珍品', esc(c.name) + '市：' + esc(nm), '市场', () => this.openMarket(c));
            break;
          }
        }
      }
      // 7 忠诚堪忧
      const low = mineGens.filter(x => !g.isRuler(x) && x.loyalty < 50).sort((a, b) => a.loyalty - b.loyalty);
      if (low.length) add('loyal', 7, 'warn', 'loyalty', '忠诚堪忧', esc(low[0].name) + ' ' + low[0].loyalty + (low.length > 1 ? ' 等 ' + low.length + ' 人' : ''), '赏赐', () => { if (SG.CharacterScreen) SG.CharacterScreen.open(low[0], { list: low }); });
      slips.sort((a, b) => a.pri - b.pri);
      const show = slips.slice(0, 4), more = slips.length - show.length;
      const key = show.map(s => s.id + s.title + s.line).join('|') + '|' + more;
      if (E.agenda._key === key) return;
      const old = new Set(Array.from(E.agenda.querySelectorAll('.sg-slip')).map(x => x.dataset.id));
      E.agenda._key = key;
      E.agenda.innerHTML = '';
      for (const s of show) {
        const el = h('div', 'sg-slip ' + (s.cls || ''), icon(s.ic) + '<div class="sg-slip-t"><b>' + s.title + '</b><span>' + s.line + '</span></div><span class="sg-slip-go">' + esc(s.go) + ' ›</span>', E.agenda);
        el.dataset.id = s.id;
        el.setAttribute('role', 'button');
        el.tabIndex = 0;
        el.setAttribute('aria-label', String(s.title).replace(/<[^>]*>/g, '') + ' · ' + s.go);
        const x = h('button', 'sg-slip-x', '', el);
        x.type = 'button';
        x.setAttribute('aria-label', '本月不再显示');
        x.innerHTML = icon('x');
        x.addEventListener('click', e => { e.stopPropagation(); this.dismissed.add(mk + ':' + s.id); sfx('ui-page'); this.agenda(G()); });
        const run = () => {
          if (this.ss.busy || SG.UI.anyModal()) return;
          // 手机：案头是 44 px 的图标签，第一次点按展开这一张（238 px），再点才去办
          if (this._short() && !el.classList.contains('is-open')) {
            for (const o of E.agenda.querySelectorAll('.sg-slip.is-open')) o.classList.remove('is-open');
            el.classList.add('is-open'); sfx('ui-page', 0.7);
            return;
          }
          sfx('ui-press-paper'); try { s.fn(); } catch (err) { console.error(err); }
        };
        el.addEventListener('click', run);
        el.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); run(); } });
        // 左滑收起
        let x0 = null;
        el.addEventListener('pointerdown', e => { x0 = e.clientX; });
        el.addEventListener('pointerup', e => { if (x0 !== null && x0 - e.clientX > 60) { this.dismissed.add(mk + ':' + s.id); this.agenda(G()); } x0 = null; });
        if (!old.has(s.id)) {
          if (!reduced()) SG.Motion.play(el, [{ opacity: 0, transform: 'translateX(-24px)' }, { opacity: 1, transform: 'none' }], { dur: 320 });
          if (old.size) sfx('ui-notify', 0.7);
        }
      }
      if (more > 0) h('div', 'sg-slip-more', '另有 ' + more + ' 件', E.agenda);
    },
    agendaSources: [],

    // ================================================================ 城池面板 --
    cityPanel(c) {
      const E = this.el;
      const g = G();
      if (!c) { this.closePanel(); return; }
      const opening = E.panel.style.display === 'none';
      if (opening) {
        E.panel.style.display = '';
        if (!reduced()) SG.Motion.play(E.panel, [{ opacity: 0, transform: 'translateX(24px)' }, { opacity: 1, transform: 'none' }], { dur: 280 });
      } else if (this._city !== c.id && !reduced()) {
        SG.Motion.play(E.cpBody, [{ opacity: 0.35 }, { opacity: 1 }], { dur: 160 });
      }
      const switched = this._city !== c.id;
      this._city = c.id;
      const mine = c.owner === g.player;
      const owner = c.owner >= 0 ? g.factions[c.owner] : null;
      const oru = owner ? g.ruler(owner.id) : null;
      const gov = c.governor >= 0 ? g.generals[c.governor] : null;
      // ---- 头 ----
      if (switched || E.cpName.textContent !== c.name) {
        E.cpName.textContent = c.name;
        if (switched && !reduced()) E.cpName.classList.remove('is-write'), void E.cpName.offsetWidth, E.cpName.classList.add('is-write');
      }
      let lvl = '';
      try { if (SG.Econ && SG.Econ.cityLevel) { const lv = SG.Econ.cityLevel(c); lvl = SG.Econ.levelName(lv) + ' · ' + '一二三四五'[lv] + '级'; } } catch (e) { lvl = ''; }
      E.cpOwn.innerHTML = '<span class="sg-cp-owner">' + (owner ? '<i class="sg-flag" style="--fc:' + owner.color + '"></i><b>' + esc(oru ? oru.name : owner.name) + '</b>' : '<b>空城</b>') +
        (gov ? ' · 太守 <b>' + esc(gov.name) + '</b>' : '') + '</span>' +
        '<span class="sg-cp-lvl">' + icon('city', 16) + (lvl ? esc(lvl) + ' · ' : '') + '<span class="sg-cp-pop">' + Math.round(c.population / 10000) + '万人</span></span>';
      const cvOk = !!(SG.CityView && SG.CityView.open);
      E.cpEnter.setEnabled(cvOk ? true : '城池视图随第四版的城池模型开放（尚未载入）。');
      // ---- 账册 ----
      this._ledger(c, mine);
      // ---- 他国城：只读条 ----
      E.dock.classList.toggle('sg-hidden', !mine);
      E.strip.classList.toggle('sg-hidden', mine);
      if (!mine) this._strip(c);
      this._occDirty = true;
    },
    closePanel() {
      const E = this.el;
      if (!E.panel || E.panel.style.display === 'none') return;
      E.panel.style.display = 'none';
      this._city = -1;
    },
    enterCity() {
      const c = G().cities[this._city];
      if (c && SG.CityView && SG.CityView.open) this.ss.do(() => SG.CityView.open(c));
    },
    _ledger(c, mine) {
      const g = G();
      const L = this.el.ledger;
      const scroll = L.querySelector('.sg-gl') ? L.querySelector('.sg-gl').scrollTop : 0;
      const bar = (k, v, max, col, cls) => '<span class="sg-dv-k">' + k + '</span><span class="sg-dv-v sg-num">' + v + '<small>/' + max + '</small></span>' + SG.UI.brushBar(v / max, col, { cls });
      const B = SG.Balance;
      let dev = bar('土地', c.land, B.StatMax || 999, 'var(--jade)', 'pol') + bar('产业', c.industry, B.StatMax || 999, 'var(--ember)') + bar('町', c.town, B.StatMax || 999, 'var(--amber)');
      if (typeof c.order === 'number') dev += bar('民心', c.order, 100, 'var(--zhu)', 'war');
      const troops = g.troopsIn(c), def = SG.cityDefense ? SG.cityDefense(c) : 0;
      const chips = '<span class="sg-chip-i" data-tip="金">' + icon('gold') + '<b class="sg-num">' + n0(c.gold) + '</b></span><span class="sg-chip-i" data-tip="粮">' + icon('food') + '<b class="sg-num">' + n0(c.food) + '</b></span>' +
        '<span class="sg-chip-i" data-tip="兵">' + icon('troops') + '<b class="sg-num">' + n0(troops) + '</b></span><span class="sg-chip-i" data-tip="防">' + icon('shield') + '<b class="sg-num">' + def + '</b></span>';
      const offs = g.officersIn(c).map((x, i) => ({ x, r: g.isRuler(x) ? 1 : 0, i })).sort((a, b) => (b.r - a.r) || (b.x.troops - a.x.troops) || (a.i - b.i)).map(o => o.x);
      const free = mine ? g.freeFoundIn(c.id) : [];
      // 第四版 TITLES §9.1：君主坐镇、由文官代理时的「主政：荀彧（尚书令）」
      let adm = '';
      try { adm = SG.TitleUI && SG.TitleUI.adminLine ? SG.TitleUI.adminLine(c) : ''; } catch (e) { adm = ''; }
      L.innerHTML = '<div class="sg-dv">' + dev + '</div><div class="sg-chips">' + chips + '</div>' +
        '<div class="sg-gh">武将 ' + offs.length + (free.length ? '<span class="sg-gh-free">在野 ' + free.length + '</span>' : '') + (adm ? '<span class="sg-gh-free sg-cp-admin">' + adm + '</span>' : '') + '<i class="sg-brushline"></i><small>兵 / 训</small></div><div class="sg-gl"></div>';
      // 金粮兵防的说明
      const tips = { 金: () => '<b>金 ' + n0(c.gold) + '</b>' + (SG.Econ && SG.Econ.cityGold ? '<br>每月约 +' + n0(SG.Econ.cityGold(c).gold) : ''), 粮: () => '<b>粮 ' + n0(c.food) + '</b><br>七月秋收', 兵: () => '<b>兵 ' + n0(troops) + '</b><br>城中武将的兵力合计', 防: () => '<b>城防 ' + def + '</b><br>町越大城防越高' };
      L.querySelectorAll('.sg-chip-i').forEach(x => SG.UI.tip(x, tips[x.dataset.tip]));
      // 「武将 n」= 武将录（只看这座城）
      const gh = L.querySelector('.sg-gh');
      if (gh && mine && SG.GeneralsScreen) { gh.classList.add('is-link'); gh.setAttribute('role', 'button'); gh.title = '武将录（' + c.name + '）'; gh.addEventListener('click', () => { if (!this.ss.busy && !SG.UI.anyModal()) SG.GeneralsScreen.open({ city: c.id }); }); }
      const list = L.querySelector('.sg-gl');
      const short = this._short();
      for (const gen of offs) {
        const row = h('div', 'sg-genrow', null, list);
        row.setAttribute('role', 'button');
        row.tabIndex = 0;
        row.setAttribute('aria-label', gen.name + ' · 人物');
        const isR = g.isRuler(gen);
        const tag = isR ? '<span class="sg-tag sg-tag-ruler">君</span>' : gen.id === c.governor ? '<span class="sg-tag sg-tag-gov">守</span>' : '';
        let lv = '';
        try { if (SG.Progress && SG.Progress.expInfo) lv = '<span class="sg-lvseal">' + SG.Progress.expInfo(gen).level + '</span>'; } catch (e) { lv = ''; }
        const nw = mine && this._canLearn(gen) ? '<span class="sg-newtag">新</span>' : '';
        let off = '';
        try { off = SG.TitleUI && SG.TitleUI.officeTag ? SG.TitleUI.officeTag(gen) : ''; } catch (e) { off = ''; }
        if (!off) off = isR ? '君主 · 德 ' + g.playerFaction.virtue : gen.id === c.governor ? '太守' : '—';
        row.innerHTML = SG.UI.frame(gen, { size: short ? 34 : 40 }) +
          '<div class="sg-gr-nm"><b>' + tag + '<span class="sg-gr-name">' + esc(gen.name) + '</span>' + lv + nw + '</b><span class="sg-gr-off">' + off + '</span></div>' +
          '<div class="sg-gr-st"><span class="w"><i>武</i>' + gen.war + '</span><span class="i"><i>智</i>' + gen.intel + '</span><span class="p"><i>政</i>' + gen.pol + '</span></div>' +
          '<div class="sg-gr-tr sg-num">' + n0(gen.troops) + '<small>训 ' + gen.training + '</small></div>' +
          (gen.moved ? '<span class="sg-stamp sg-gr-moved">已行动</span>' : '');
        const open = () => this.ss.showGeneral(gen, offs);
        row.addEventListener('click', open);
        row.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open(); } });
        if (mine) SG.UI.longPress(row, () => this._quickActions(gen, c));
      }
      if (!offs.length) h('div', 'sg-empty', '城中无将', list);
      list.scrollTop = scroll;
    },
    async _quickActions(gen, c) {
      if (this.ss.busy || SG.UI.anyModal()) return;
      const r = await SG.UI.choose(esc(gen.name), [SG.UI.item('人物', '详情'), SG.UI.item('赏赐', null, !G().isRuler(gen)), SG.UI.item('移动', null, true)], null, 420);
      if (r === 0) this.ss.showGeneral(gen);
      else if (r === 1) this.ss.do(() => this.ss.cmdReward(c, gen));
      else if (r === 2) this.ss.do(() => this.ss.cmdMove(c));
    },
    // 他国城：入城观看 · 外交 · 出征（相邻时）· 市场（同盟且相邻时 ×1.15）
    _strip(c) {
      const g = G();
      const S = this.el.strip;
      S.innerHTML = '';
      const ownAdj = c.links.map(i => g.cities[i]).filter(x => x.owner === g.player);
      const allied = c.owner >= 0 && g.allied(c.owner, g.player);
      h('div', 'sg-strip-note', c.owner >= 0 ? (allied ? '同盟势力的城池' : '他国城池 · 只能观看') : '无主空城', S);
      const row = h('div', 'sg-strip-row', null, S);
      if (ownAdj.length && !allied) {
        const from = ownAdj.slice().sort((a, b) => g.troopsIn(b) - g.troopsIn(a))[0];
        SG.UI.button(row, '出征', () => { this.ss.selectCity(from.id); this.ss.do(() => this.ss.cmdAttack(from, c)); }, { kind: 'seal', icon: 'attack', why: g.tokens > 0 ? null : '本月令牌已用完。' });
      }
      const cap = g.cities[g.ruler(g.player).city];
      SG.UI.button(row, '外交', () => this.ss.do(() => this.ss.cmdDiplomacy(cap)), { icon: 'diplomacy', why: g.tokens > 0 ? null : '本月令牌已用完。' });
      if (SG.ShopScreen && SG.Items && SG.Items.enabled !== false && g.items) SG.UI.button(row, allied && ownAdj.length ? '市场 ×1.15' : '市场', () => this.openMarket(c), { icon: 'market', kind: 'paper' });
    },

    // ---- 指令坞：16 格固定位置 ----
    beginDock() { this.cmds.clear(); },
    addCmd(name, why, action, opts) {
      this.cmds.set(name, { why: why || null, action, opts: opts || {} });
    },
    endDock() {
      const g = G();
      const D = this.el.dock;
      const prevKey = D._key;
      const parts = [];
      for (const [gname, , cmds] of DOCK) for (const [name] of cmds) { const e = this.cmds.get(name); parts.push(name + ':' + (e ? (e.why || '') : 'x')); }
      const key = parts.join('|') + '|' + (this.ss._hint ? this.ss._hint.name : '');
      // 结构不变时只更新状态（避免整格重建打断按下的动画）
      if (!D._built) {
        D.innerHTML = '';
        D._tiles = new Map();
        let k = 0;
        for (const [gname, gcol, cmds] of DOCK) {
          const grp = h('span', 'sg-dock-grp', gname, D);
          grp.style.background = gcol;
          grp.setAttribute('aria-hidden', 'true');
          for (const [name, ic, tok] of cmds) {
            const hot = DOCK_KEYS[k++];
            const b = SG.UI.button(D, name, e => this._runTile(b, e), { kind: 'tile', icon: ic, token: !!tok, hotkey: hot, sound: 'none' });
            b.classList.add('sg-tile');
            b.style.setProperty('--gc', gcol);
            b.style.setProperty('--br', (6 + ((k * 2.618) % 3)).toFixed(2) + 's');     // 呼吸周期 6–9 秒（按格错开）
            b.style.setProperty('--bd', (-((k * 1.7) % 6)).toFixed(2) + 's');
            b.dataset.cmd = name;
            b._ic = ic; b._tok = !!tok;
            h('i', 'sg-tile-glow', null, b).setAttribute('aria-hidden', 'true');
            b.addEventListener('pointerdown', ev => { if (ev.button === 0 || ev.button === undefined) { this.lastTile = b; this._pressSound(b); } });
            SG.UI.tip(b, () => '<b>' + name + '</b>' + (hot && !SG.isTouch ? '　<span class="sg-muted">' + hot + '</span>' : '') + '<br>' + (b._why ? b._why : CMD_DESC[name] || ''));
            D._tiles.set(name, b);
          }
        }
        D._built = true;
      }
      if (prevKey === key && !this.ss.busy) return;
      D._key = key;
      const noTok = g.tokens <= 0;
      for (const [name, b] of D._tiles) {
        const e = this.cmds.get(name);
        let why = e ? e.why : (MISSING[name] || '此指令尚未开放。');
        b._action = e ? e.action : null;
        b._why = why;
        if (why) { b.dataset.why = why; b.setAttribute('aria-disabled', 'true'); b.classList.add('is-locked'); }
        else { delete b.dataset.why; b.removeAttribute('aria-disabled'); b.classList.remove('is-locked'); }
        b.classList.toggle('primary-tile', !!(e && e.opts.primary && !why));
        b.classList.toggle('is-notok', b._tok && noTok);
        b.classList.toggle('is-busy', !!this.ss.busy);
        const hint = !why && this.ss._hint && this.ss._hint.name === name && performance.now() < this.ss._hint.until;
        b.classList.toggle('sg-cmd-hint', !!hint);
      }
      // 呼吸的指令印至多 6 格（每行先挑第一格可用的，再按顺序补满）：整屏环境动画 ≤ 12（UI-V4 §5.8 / §13.4）
      const avail = [];
      for (const [name, b] of D._tiles) if (!b._why && !(b._tok && noTok) && !b.classList.contains('primary-tile')) avail.push(b);
      const pick = new Set();
      for (let r = 0; r < DOCK.length; r++) { const b = avail.find(x => DOCK[r][2].some(c => c[0] === x.dataset.cmd)); if (b) pick.add(b); }
      for (const b of avail) { if (pick.size >= 6) break; pick.add(b); }
      for (const [, b] of D._tiles) b.classList.toggle('is-breath', pick.has(b));
    },
    _pressSound(b) {
      if (b._why) return;
      const P = { develop: 'cmd-press-develop', build: 'cmd-press-build', caravan: 'cmd-press-caravan', explore: 'cmd-press-explore', diplomacy: 'cmd-press-diplomacy' };
      const n = 'cmd-press-' + b._ic;
      const has = SG.Sfx && SG.Sfx._recipes && (SG.Sfx._recipes[n] || SG.Sfx._recipes[P[b._ic]]);
      sfx(has ? (SG.Sfx._recipes[n] ? n : P[b._ic]) : 'ui-press-wood');
    },
    _runTile(b, e) {
      if (b._why) { this.ss.explainCmd(b); return; }
      if (b._action) b._action(e);
    },
    tile(name) { return this.el.dock && this.el.dock._tiles ? this.el.dock._tiles.get(name) || null : null; },
    // 指令执行完的结果回响：大成时指令印角上留 4 秒小印（DOMESTIC-FX §7.2）
    echo(name, tier) {
      const b = this.tile(name);
      if (!b || tier !== 'great') return;
      b.classList.add('is-great');
      setTimeout(() => b.classList.remove('is-great'), 4000);
    },

    // ================================================================ 令箭 --
    spendToken() {
      const from = this.el.quiver, to = this.lastTile && this.lastTile.isConnected ? this.lastTile : null;
      sfx('ui-token');
      if (!from || !to || reduced() || !document.body.animate) return;
      const a = from.querySelector('.arrow:not(.spent):last-of-type') || from;
      const r0 = a.getBoundingClientRect(), r1 = to.getBoundingClientRect();
      const fly = h('div', 'sg-token-fly', icon('token'), SG.UI.layer('toasts'));
      const x0 = r0.left + r0.width / 2, y0 = r0.top + r0.height / 2, x1 = r1.left + r1.width / 2, y1 = r1.top + r1.height / 2;
      const mx = (x0 + x1) / 2, my = Math.min(y0, y1) - 60;
      const kf = [];
      for (let i = 0; i <= 10; i++) {
        const t = i / 10, u = 1 - t;
        const x = u * u * x0 + 2 * u * t * mx + t * t * x1, y = u * u * y0 + 2 * u * t * my + t * t * y1;
        kf.push({ transform: 'translate(' + x.toFixed(1) + 'px,' + y.toFixed(1) + 'px) translate(-50%,-50%) rotate(' + (-20 + 60 * t).toFixed(0) + 'deg) scale(' + (1 - 0.3 * t).toFixed(2) + ')', opacity: t > 0.9 ? 0 : 1 });
      }
      const an = fly.animate(kf, { duration: 340, easing: 'cubic-bezier(.3,.6,.4,1)' });
      an.onfinish = () => { fly.remove(); };
    },

    // ================================================================ 换月 --
    // 黄昏（0.6 秒）→ 月名书写（大楷、年 · 季 · 节气、季节粒子）→ 黎明。任意键 / 点按跳过；减少动态 0.3 秒。
    monthTransition(g) {
      g = g || G();
      return new Promise(resolve => {
        const host = SG.UI.layer('toasts');
        const fx = h('div', 'sg-monthfx', null, host);
        const done = () => { if (fx._done) return; fx._done = true; window.removeEventListener('keydown', key, true); fx.removeEventListener('pointerdown', skip); this._applyClock(); fx.classList.add('is-out'); setTimeout(() => fx.remove(), 320); resolve(); };
        let stage = 0;
        const skip = () => { if (stage >= 1) done(); else stage = 1; };
        const key = e => { if (['Enter', ' ', 'Escape', 'Space'].indexOf(e.key) >= 0 || e.code === 'Space') { e.preventDefault(); e.stopPropagation(); done(); } };
        fx.addEventListener('pointerdown', skip);
        window.addEventListener('keydown', key, true);
        const sub = yearZh(g.year) + '年 · ' + SEASON_ZH[g.month] + ' · ' + TERM[g.month];
        fx.innerHTML = '<div class="sg-mfx-wash"></div><div class="sg-mfx-c"><div class="sg-mfx-m">' + monthZh(g.month).split('').map((ch, i) => '<span style="--i:' + i + '">' + ch + '</span>').join('') + '</div><div class="sg-mfx-s">' + esc(sub) + '</div></div>';
        if (reduced()) {
          fx.classList.add('is-reduced');
          sfx('ui-month', 0.7);
          setTimeout(done, 700);
          return;
        }
        // 季节粒子（≤ 26）
        if (US('ui.particles') !== false && !motionLite()) {
          const kind = SEASON[g.month];
          const rng = SG.UIArt.rng(g.year * 12 + g.month);
          let p = '';
          for (let i = 0; i < 26; i++) p += '<i class="sg-mfx-p is-' + kind + '" style="left:' + (rng() * 100).toFixed(1) + '%;top:' + (-10 - rng() * 30).toFixed(1) + '%;--dx:' + ((rng() - 0.5) * 30).toFixed(1) + 'vw;--d:' + (1.6 + rng() * 1.6).toFixed(2) + 's;--dl:' + (rng() * 0.8).toFixed(2) + 's;--r:' + (rng() * 360).toFixed(0) + 'deg"></i>';
          fx.insertAdjacentHTML('beforeend', p);
        }
        this.setNight(0.7);
        setTimeout(() => { stage = 1; fx.classList.add('is-write'); sfx('ui-month', 0.8); }, 120);
        setTimeout(() => { this.setNight(0); }, 1300);
        setTimeout(done, 2600);
      });
    },
    aiScroll(on) {
      const E = this.el;
      if (!on) { if (E.ai) { const a = E.ai; E.ai = null; a.classList.add('is-out'); setTimeout(() => a.remove(), 260); } return; }
      if (E.ai) return;
      const g = G();
      const fs = g.factions.filter(f => f.alive && f.id !== g.player).slice(0, 14);
      const a = E.ai = h('div', 'sg-aiscroll', '<div class="sg-ais-paper"><b>诸侯行动中</b><div class="sg-ais-row">' +
        fs.map((f, i) => '<i class="sg-flag" style="--fc:' + f.color + ';--i:' + i + '"></i>').join('') + '</div></div>', SG.UI.layer('toasts'));
      a.setAttribute('role', 'status');
      sfx('ui-scroll', 0.7);
      this.setNight(1);
    },

    // ================================================================ 铜钮 --
    openGenerals() {
      if (this.ss.busy || SG.UI.anyModal()) return;
      if (SG.GeneralsScreen) SG.GeneralsScreen.open({});
    },
    openTitles() {
      if (!(SG.TitleUI && SG.Titles && SG.Titles.enabled !== false)) { SG.UI.deny(this.el.bTitles, '官制随第四版的官爵系统开放（尚未载入）。'); return; }
      this.ss.do(async () => { try { if (SG.Voice) SG.Voice.say(G().ruler(G().player), 'ui.titleBoard', { pri: 1, scene: 'strategy' }); } catch (e) { /* 无人声 */ } await SG.TitleUI.open(); });
    },
    openSettings() { if (SG.UI.anyModal() && !(SG.MenuScreen && SG.MenuScreen.isOpen && SG.MenuScreen.isOpen())) return; if (SG.SettingsScreen) SG.SettingsScreen.open(); },
    openMenu(page) { if (SG.UI.anyModal()) return; if (SG.MenuScreen) SG.MenuScreen.open(page); },
    openMarket(c) {
      if (!SG.ShopScreen) return;
      this.ss.do(async () => { try { if (SG.Voice) SG.Voice.say('merchant', 'ui.shopOpen', { pri: 1, scene: 'strategy' }); } catch (e) { /* 无人声 */ } await SG.ShopScreen.open({ city: c }); });
    },

    // ================================================================ 键盘（UI-V4 §15.3）--
    _installKeys() {
      if (this._keyHooked) return;
      this._keyHooked = true;
      let spaceAt = 0;
      window.addEventListener('keydown', e => {
        const t = e.target;
        if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
        const k = e.key;
        if (k === 'm' || k === 'M') return;                                     // M 静音：载入时装的全局键（见文件末尾）
        if (!this.hud || this.hud.style.display === 'none' || !this.ss) return;
        if (SG.Gfx && SG.Gfx.topScreen && SG.Gfx.topScreen()) return;            // 单挑、攻击画面等全屏场景
        // Ctrl / ⌘ + S：快速记录（任何时候都拦住浏览器的「保存网页」）
        if ((e.ctrlKey || e.metaKey) && (k === 's' || k === 'S')) {
          e.preventDefault();
          if (!SG.UI.anyModal() && SG.MenuScreen && SG.MenuScreen.quickSave) SG.MenuScreen.quickSave();
          return;
        }
        if (e.ctrlKey || e.metaKey || e.altKey) return;
        if (SG.UI.anyModal() || this.ss.busy) return;
        const up = k.length === 1 ? k.toUpperCase() : k;
        if (up === 'G') { e.preventDefault(); this.openGenerals(); return; }
        if (up === 'T') { e.preventDefault(); this.openTitles(); return; }
        if (up === 'P') { e.preventDefault(); this.ss.do(() => this.ss.factionInfo()); return; }
        if (up === 'H') { e.preventDefault(); this.ss.do(() => this.ss.chronicle()); return; }
        if (up === 'O') { e.preventDefault(); this.openSettings(); return; }
        if (k === 'Escape') {
          e.preventDefault();
          if (this._city >= 0) this.ss.selectCity(-1); else this.openMenu();
          return;
        }
        // Enter / 长按 Space = 结束本月，只在「空地图」上：焦点在按钮、指令印、案头纸签、武将行上时交给它自己（按下它）
        const onCtl = t && t !== document.body && t !== document.documentElement && t.closest && t.closest('button, a, input, select, [role="button"], [role="tab"], [tabindex]');
        if ((k === 'Enter' || k === ' ') && onCtl) return;
        if (k === 'Enter') { e.preventDefault(); this.ss.do(() => this.ss.confirmEnd()); return; }
        if (k === ' ' && !e.repeat) { spaceAt = performance.now(); return; }
        if (k === ' ' && e.repeat && spaceAt && performance.now() - spaceAt > 400) { spaceAt = 0; this.ss.do(() => this.ss.confirmEnd()); return; }
        // 指令坞快捷键（城池面板打开、本方城时）
        const i = DOCK_KEYS.indexOf(up);
        if (i >= 0 && this._city >= 0 && !this.el.dock.classList.contains('sg-hidden')) {
          const name = DOCK.map(d => d[2]).flat()[i][0];
          const b = this.tile(name);
          if (b) { e.preventDefault(); this.lastTile = b; this._pressSound(b); SG.UI.pulse(b, 'is-keypress'); b.click(); }
        }
      });
      window.addEventListener('keyup', e => { if (e.key === ' ') spaceAt = 0; });
    },
    // M：全部静音 / 取消（音乐、音效、人声与朗读；不改设置里的音量）
    toggleMute() {
      const S = SG.Sfx;
      if (!S) return;
      this._muted = !this._muted;
      if (this._muted) { this._wasMusic = S.musicOn; this._wasSound = S.soundOn; S.setMusic(false); S.setSound(false); }
      else { S.setSound(this._wasSound !== false); if (this._wasMusic !== false) S.setMusic(true); }
      // 人声总线（audio.js 的 voiceIn / 混响送出）：静音时增益置 0；正在说的台词与系统朗读一并停下
      try {
        const bus = S.voiceBus ? S.voiceBus() : null;
        const k = this._muted ? 0 : 1;
        if (bus) { const t = bus.ctx.currentTime; for (const n of [bus.input, bus.hall]) if (n && n.gain) { n.gain.cancelScheduledValues(t); n.gain.setValueAtTime(k, t); } }
        if (this._muted && typeof speechSynthesis !== 'undefined' && speechSynthesis.cancel) speechSynthesis.cancel();
      } catch (e) { /* 无音频 */ }
      SG.UI.toast(this._muted ? '静音' : '取消静音', { seconds: 1.4, seal: this._muted ? '静' : '声' });
    },
    get muted() { return !!this._muted; },
  };

  // 武林大会、文会等由各自的模块登记案头来源：SG.Shell.agendaSources.push(g => ({ id, pri, cls, icon, title, line, go, open }))
  SG.Shell = Shell;
  // M：任何画面（标题、载入、战略、战斗、单挑、窗口）都可静音 / 取消（UI-V4 §15.3）——载入脚本时就装上，不等开局搭外壳
  window.addEventListener('keydown', e => {
    const t = e.target;
    if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
    if ((e.key === 'm' || e.key === 'M') && !e.ctrlKey && !e.metaKey && !e.altKey && !e.repeat) Shell.toggleMute();
  });

  // ================================================================ 测试钩子（UI-V4 §17.3、§19.1；tests/ui-layout.html）==
  // SG.UITest.open(screen, opts) → Promise   打开一个画面（见 SCREENS）；opts：{ page, tab, view, gen, wait, noTokens }
  // SG.UITest.closeAll() → Promise             逐层按 Esc 关掉所有窗口与对话
  // SG.UITest.layoutReport(opts?) → { w, h, short, insets, fit, problems: [文字], boxes, small, cut, tiny, clipped, errors }
  //   problems：顶梁 / 案头 / 城池面板 / 结束本月互相覆盖或出安全视口；横向滚动；最上层窗口出安全视口；
  //   可点部件小于 44 × 44（矮屏）/ 32 × 32（桌面）；可点部件被视口或不能滚动的祖先截掉（够不着，cut）；全屏场景的内容出安全视口；
  //   字小于 13 px（矮屏）/ 12.5 px（桌面）；单行标签被截断；页面错误
  // ?uitest=1 时从载入起记下 pageerror / unhandledrejection / console.error（SG.UITest.errors）
  const UITest = SG.UITest = {
    errors: [],
    sleep: ms => new Promise(r => setTimeout(r, ms)),
    finishAnims() { try { for (const a of document.getAnimations()) { const t = a.effect && a.effect.getTiming ? a.effect.getTiming() : null; if (t && t.iterations !== Infinity) a.finish(); } } catch (e) { /* 忽略 */ } },
    async closeAll() {
      for (let i = 0; i < 10 && SG.UI.anyModal(); i++) {
        if (!SG.UI.escapeTop()) { const p = SG.UI.topModal(); if (p) { const c = p.querySelector('.sg-close'); if (c) c.click(); else break; } }
        await UITest.sleep(260);
      }
      return !SG.UI.anyModal();
    },
    ss() { return SG.Game && SG.Game.strategy; },
    capital() { const g = G(); return g.cities[g.ruler(g.player).city]; },
    general(name) { const g = G(); const mine = g.generalsOf(g.player).filter(x => !x.dead); return mine.find(x => x.name === (name || '关羽')) || mine.find(x => !g.isRuler(x)) || g.ruler(g.player); },
    SCREENS: ['strategy', 'city', 'dock', 'agenda', 'month', 'menu', 'settings', 'generals', 'character', 'shop', 'titles', 'tourney'],
    async open(screen, opts) {
      opts = opts || {};
      const ss = UITest.ss(), g = G();
      if (screen !== 'month') await UITest.closeAll();
      const go = fn => { try { const r = fn(); if (r && r.catch) r.catch(e => console.error(e)); } catch (e) { console.error(e); } };
      switch (screen) {
        case 'strategy': if (ss) ss.selectCity(-1); break;
        case 'city': if (ss) ss.selectCity(UITest.capital().id); break;
        case 'dock':
          if (opts.noTokens && g) { g.tokens = 0; }
          if (ss) { ss.selectCity(UITest.capital().id); ss.refreshAll(); }
          break;
        case 'agenda':
          if (!UITest._agendaSrc) {
            UITest._agendaSrc = gg => [
              { id: 't-tourney', pri: 1, cls: 'hot', icon: 'tourney', title: '武林大会', line: '本月于洛阳平乐观举行 · 选派至多 5 人', go: '选将', open() {} },
              { id: 't-event', pri: 2, cls: 'warn', icon: 'chronicle', title: '灾异', line: '东郡蝗灾', go: '查看', open() {} },
              { id: 't-shop', pri: 5, cls: '', icon: 'market', title: '胡商抵达', line: '平原市中来了一位大宛马商', go: '市场', open() {} },
              { id: 't-build', pri: 6, cls: 'jade', icon: 'build', title: '营建落成', line: '平原 · 市集', go: '查看', open() {} },
            ];
            Shell.agendaSources.push(UITest._agendaSrc);
          }
          if (ss) { ss.selectCity(-1); ss.refreshAll(); }
          break;
        case 'month': go(() => Shell.monthTransition(g)); await UITest.sleep(opts.wait || 1300); return true;
        case 'menu': go(() => SG.MenuScreen.open(opts.page || 'save')); break;
        case 'settings': go(() => SG.SettingsScreen.open(opts.tab)); break;
        case 'generals': go(() => SG.GeneralsScreen.open({})); break;
        case 'character': go(() => SG.CharacterScreen.open(opts.gen || UITest.general(opts.name), { tab: opts.tab })); break;
        case 'shop': if (!SG.ShopScreen) return false; go(() => SG.ShopScreen.open({ city: UITest.capital(), tab: opts.tab })); break;
        case 'titles': if (!SG.TitleUI || !SG.Titles || SG.Titles.enabled === false) return false; go(() => SG.TitleUI.open()); break;
        case 'tourney': if (!SG.TourneyScreen) return false; go(() => SG.TourneyScreen.preview(opts.view || 'bracket')); break;
        default: return false;
      }
      await UITest.sleep(opts.wait || 1200);
      UITest.finishAnims();
      return true;
    },
    layoutReport(opts) {
      opts = opts || {};
      const W = innerWidth, H = innerHeight, de = document.documentElement, short = de.classList.contains('sg-short');
      const cs = getComputedStyle(de);
      const insL = parseFloat(cs.getPropertyValue('--sg-sal')) || 0, insR = parseFloat(cs.getPropertyValue('--sg-sar')) || 0;
      UITest.finishAnims();
      const E = Shell.el || {};
      const problems = [], boxes = {};
      const visible = el => {
        if (!el || !el.isConnected) return false;
        const st = getComputedStyle(el);
        if (st.display === 'none' || st.visibility === 'hidden' || +st.opacity === 0) return false;
        if (el.offsetParent === null && st.position !== 'fixed') return false;
        return true;
      };
      // 祖先的裁剪框（overflow 不是 visible 的）与视口求交；scroll = 有能滚动的裁剪祖先（滚出视野的部件还能滚回来）
      const clipRect = el => {
        let r = el.getBoundingClientRect(); let x0 = r.left, y0 = r.top, x1 = r.right, y1 = r.bottom, scroll = false;
        for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
          const st = getComputedStyle(p);
          if (st.overflowX !== 'visible' || st.overflowY !== 'visible') {
            const q = p.getBoundingClientRect(); x0 = Math.max(x0, q.left); y0 = Math.max(y0, q.top); x1 = Math.min(x1, q.right); y1 = Math.min(y1, q.bottom);
            if ((/(auto|scroll)/.test(st.overflowY) && p.scrollHeight > p.clientHeight + 1) || (/(auto|scroll)/.test(st.overflowX) && p.scrollWidth > p.clientWidth + 1)) scroll = true;
          }
        }
        x0 = Math.max(x0, 0); y0 = Math.max(y0, 0); x1 = Math.min(x1, W); y1 = Math.min(y1, H);
        return { w: Math.max(0, x1 - x0), h: Math.max(0, y1 - y0), full: r, scroll };
      };
      // 最上层：窗口 / 对话；没有时看全屏场景（武林大会的对阵、颁奖：.sg-tny-scene）
      const modal = SG.UI.anyModal() ? SG.UI.topModal() : Array.from(document.querySelectorAll('.sg-tny-scene:not(.is-out)')).pop() || null;
      const root = modal || document.getElementById('ui') || document.body;
      // 1 外壳的区块（没有窗口时）
      if (!modal && Shell.hud && Shell.hud.style.display !== 'none') {
        const parts = { beam: E.beam, panel: E.panel, dock: E.dock, endseal: E.endBtn, agenda: E.agenda };
        for (const k of Object.keys(parts)) {
          const el = parts[k];
          if (!visible(el)) continue;
          const r = el.getBoundingClientRect();
          if (k === 'agenda' && r.height < 2) continue;
          boxes[k] = [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)];
          if (r.left < insL - 1 || r.right > W - insR + 1 || r.top < -1 || r.bottom > H + 1) problems.push(k + ' outside the safe viewport ' + JSON.stringify(boxes[k]));
        }
        const hit = (a, b) => a && b && a[0] < b[0] + b[2] && b[0] < a[0] + a[2] && a[1] < b[1] + b[3] && b[1] < a[1] + a[3];
        for (const [a, b] of [['beam', 'panel'], ['beam', 'endseal'], ['panel', 'endseal'], ['agenda', 'panel'], ['agenda', 'endseal'], ['beam', 'agenda']]) if (hit(boxes[a], boxes[b])) problems.push(a + ' overlaps ' + b);
        if (E.beam && visible(E.beam) && (E.beam.scrollWidth > E.beam.clientWidth + 1 || (E.center && E.center.scrollWidth > E.center.clientWidth + 1))) problems.push('beam content overflows (' + E.beam.className + ')');
        for (const ts of document.querySelectorAll('.sg-toast')) { if (!visible(ts)) continue; const r = ts.getBoundingClientRect(), tb = [r.left, r.top, r.width, r.height]; for (const k of ['panel', 'endseal', 'beam']) if (hit(tb, boxes[k])) problems.push('toast overlaps ' + k); }
      }
      if (modal) {
        const r = modal.getBoundingClientRect();
        boxes.window = [Math.round(r.left), Math.round(r.top), Math.round(r.width), Math.round(r.height)];
        // 全屏场景（武林大会的对阵、颁奖）的背景铺满整屏（含刘海两侧）；量它的内容：按钮、立绘卡、奖台、朱印不出安全视口
        if (modal.classList.contains('sg-tny-scene')) {
          for (const c of modal.querySelectorAll('.sg-btn, .sg-tny-vcard, .sg-tny-tier, .sg-tny-tianxia, .sg-tny-cont')) {
            if (!visible(c)) continue;
            const q = c.getBoundingClientRect();
            if (q.left < insL - 1 || q.right > W - insR + 1 || q.top < -1 || q.bottom > H + 1) problems.push('scene content outside the safe viewport: ' + (c.textContent || c.className).trim().slice(0, 10) + ' ' + JSON.stringify([Math.round(q.left), Math.round(q.top), Math.round(q.width), Math.round(q.height)]));
          }
        } else if (r.left < insL - 1 || r.right > W - insR + 1 || r.top < -1 || r.bottom > H + 1) problems.push('window outside the safe viewport ' + JSON.stringify(boxes.window));
      }
      const scrollW = (document.scrollingElement || de).scrollWidth;
      if (scrollW > W + 1) problems.push('horizontal page scroll: ' + scrollW + ' > ' + W);
      // 2 可点部件的大小
      const minT = short ? 44 : 32;
      const small = [], cut = [];
      const SEL = 'button, [role="button"], [role="tab"], [role="switch"], [role="radio"], a[href], input:not([type="hidden"]), select, .sg-slider';
      for (const b of root.querySelectorAll(SEL)) {
        if (!visible(b) || b.closest('.sg-slider') && !b.classList.contains('sg-slider')) continue;
        if (b.closest('[aria-hidden="true"]') || b.closest('.sg-tip')) continue;
        if (b.tagName === 'INPUT' && b.closest('.sg-search')) continue;              // 搜索框量外面的纸签
        const c = clipRect(b);
        const lbl = () => ((b.getAttribute('aria-label') || b.dataset.cmd || b.dataset.tab || b.textContent || b.className || '').trim().replace(/\s+/g, ' ').slice(0, 10));
        // 被视口或不能滚动的祖先截掉一部分：够不着（能滚动的祖先里滚出视野的不算）
        const visK = c.w * c.h / Math.max(1, c.full.width * c.full.height);
        if ((c.full.width - c.w > 2 || c.full.height - c.h > 2) && !c.scroll) cut.push(lbl() + ' ' + Math.round(visK * 100) + '%');
        if (visK < 0.5 || c.w < 2 || c.h < 2) continue;   // 滚出视野（或截掉大半，已记为 cut）
        const w = c.full.width, h = c.full.height;
        if (w < minT - 0.5 || h < minT - 0.5) small.push(lbl() + ' ' + Math.round(w) + '×' + Math.round(h));
      }
      if (cut.length) problems.push(cut.length + ' targets cut off (no scrollable ancestor): ' + cut.slice(0, 8).join(', '));
      if (short) { const t = document.querySelector('.sg-dock .sg-btn.k-tile'); if (!modal && visible(t)) { const r = t.getBoundingClientRect(); if (r.width < 56 - 0.5 || r.height < 56 - 0.5) small.push('dock tile ' + Math.round(r.width) + '×' + Math.round(r.height) + ' < 56×56'); } }
      if (small.length) problems.push(small.length + ' targets < ' + minT + ' px: ' + small.slice(0, 12).join(', '));
      // 3 字号
      const minF = short ? 13 : 12.5;
      const tiny = [];
      const tw = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, { acceptNode: n => (/\S/.test(n.data) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT) });
      let n;
      const seen = new Set();
      while ((n = tw.nextNode())) {
        const el = n.parentElement;
        if (!el || seen.has(el) || el.closest('svg') || el.closest('.sg-tw-hide') || el.closest('.sg-tip')) continue;
        seen.add(el);
        if (!visible(el)) continue;
        const c = clipRect(el);
        if (c.w < 1 || c.h < 1) continue;
        const fs = parseFloat(getComputedStyle(el).fontSize);
        if (fs < minF - 0.05) tiny.push(n.data.trim().slice(0, 8) + ' ' + fs.toFixed(1) + 'px <' + (el.className && el.className.baseVal === undefined ? String(el.className).split(' ')[0] : el.tagName.toLowerCase()) + '>');
      }
      if (tiny.length) problems.push(tiny.length + ' text nodes < ' + minF + ' px: ' + tiny.slice(0, 12).join(', '));
      // 4 单行标签被截断（按钮、页签、木牌、匾、城名、窗口标题）
      const clipped = [];
      for (const el of root.querySelectorAll('.sg-btn, .sg-tab, .sg-seg-o, .sg-plaque, .sg-cp-name, .sg-win-title, .sg-tile-lb, .sg-btn-l, .sg-gr-name')) {
        if (!visible(el)) continue;
        const st = getComputedStyle(el);
        if (st.whiteSpace.indexOf('nowrap') < 0 && st.textOverflow !== 'ellipsis') continue;
        if (st.overflowX === 'visible' && st.textOverflow !== 'ellipsis') continue;
        if (el.scrollWidth > el.clientWidth + 1) clipped.push((el.textContent || '').trim().slice(0, 10) + ' ' + el.scrollWidth + '>' + el.clientWidth);
      }
      if (clipped.length) problems.push(clipped.length + ' clipped labels: ' + clipped.slice(0, 8).join(', '));
      // 5 页面错误
      if (UITest.errors.length) problems.push(UITest.errors.length + ' page errors: ' + UITest.errors.slice(0, 3).join(' | '));
      const fit = E.beam ? (E.beam.className.match(/fit-\d/g) || []).join(' ') : '';
      return { w: W, h: H, short, insets: [insL, insR], fit, window: modal ? (modal.dataset.win || modal.className.split(' ').slice(0, 3).join(' ')) : null, problems, boxes, small, cut, tiny, clipped, errors: UITest.errors.slice() };
    },
  };
  // ?uitest=1：从载入起记下页面错误
  try {
    if (/[?&]uitest=1\b/.test(location.search)) {
      const push = m => { if (UITest.errors.length < 50) UITest.errors.push(String(m).slice(0, 300)); };
      addEventListener('error', e => push('pageerror: ' + (e.message || e)));
      addEventListener('unhandledrejection', e => push('rejection: ' + (e.reason && e.reason.message ? e.reason.message : e.reason)));
      const ce = console.error.bind(console);
      console.error = (...a) => { push('console.error: ' + a.map(x => (x && x.message) || String(x)).join(' ')); ce(...a); };
    }
  } catch (e) { /* 忽略 */ }
})();
