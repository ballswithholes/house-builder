'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 内政演出（DOMESTIC-FX §3、§5、§9、§10；第二阶段：二维牌面）
   每条指令的结果先由规则算好（js/model.js 写 Commands.lastResult），再由本文件把「发生了什么」演给玩家看：
   普通 ≈ 2 秒、好 ≈ 3 秒、大成 ≈ 4 秒（上限 4.5 秒），长度就是它的短乐（js/music-stings.js）；任意键 100 ms 内跳到结果。

   舞台（§3.3）：本阶段只有「牌面」card（二维，DOM + SVG + 一张粒子画布）。城池视图（CITY-VIEW §7）落地后
   以 registerStage('city' | 'diorama', factory) 换上三维舞台：导演（本文件）照旧负责时间轴、短乐、结果条、
   数字滚动、印、竖幅、跳过与设置，舞台只负责「动作」。舞台接口：
     factory(ctx) → { el?, hud, open(), beat(name, info), skip(), close() }
       ctx = { fx, res, tier, id, script, T, A, R, fast, rng, root, city, gen, fc }
       beat 名：'cue' 'action' 'reveal' 'good' 'great' 'miss' 'settle'；hud = 结果条 / 印 / 竖幅挂在其上的元素
   演出脚本登记表（§10.1）：define(id, { icon, sting, stage, anchor, shot, chips, forecast, stageBar, banner, miss,
     card(sc), action(d, res), good(d, res), great(d, res), missFx(d, res) })。card(sc) 是二维牌面的动作，
     action / good / great 留给三维舞台（d = SG.CityView.director，CITY-VIEW §7.3）。

   接口（§10.2）
     SG.DomesticFx.play(res, tierInfo?) → Promise<void>   不抛错；tierInfo 缺省 = SG.DomesticTiers.tier(res)
     SG.DomesticFx.skip()                 跳到结果（再按一次立即关闭）        SG.DomesticFx.busy
     SG.DomesticFx.settings / setSetting(k, v)          localStorage 'sanguozhi2_fx_v4'（try / catch）
       { domestic: 'smart'|'full'|'highlights'|'fast'|'off', cityView, quality, plays: { [id]: 次数 } }
     SG.DomesticFx.harvest(records) → Promise   秋收（§9.1）      stageUps(list) → Promise   城池升阶（§9.2）
     SG.DomesticFx.moment(id, res) → Promise    营建落成、商队归来、归附、国策、节庆、文会等时刻（§5.18–§5.28）
     SG.DomesticFx.monthStart(g?) → { harvests, stageUps }   月初要演的东西（G.fxStages 比较；不重复庆祝）
     SG.DomesticFx.define(id, script) · scripts · registerStage(name, factory) · stageFor(res) · formFor(id, tier)
     SG.DomesticFx.timeMap(id, tier, form) → { T, A, R, fast }（不放音乐时的缺省长度）
     SG.DomesticFx.auto（缺省 true）：strategy-screen.js 还没有自己调用 play() 时，从 SG.Events 'command' 与
       'month.phase' 自动演出玩家的指令与月初时刻（同一条记录若已由 play() 演过就不再演；把 auto 设为 false 即关）
     SG.DomesticFx.CFG                    全部时长与预算（一处定义）          SG.DomesticFx.stats   最近一次演出的计时
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = (W.SG = W.SG || {});
  const hasDoc = typeof document !== 'undefined';
  const G = () => SG.G || null;
  const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now()) / 1000;
  const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
  const esc = s => String(s === undefined || s === null ? '' : s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  // ---------------------------------------------------------------- 一处定义的数 --
  const CFG = Object.freeze({
    defaults: Object.freeze({ normal: [2.0, 1.1], good: [3.0, 2.0], great: [4.0, 2.0], miss: [2.2, 1.1] }),   // 不放音乐时 [T, A]（§3.4）
    fastT: Object.freeze({ normal: 1.0, good: 1.3, great: 1.8, miss: 1.0 }),                                 // 快速档（§3.5）
    fastAction: 0.4, actionStart: 0.15, normalRevealK: 0.55, cue: 0.18,
    countUp: 0.7, countUpFast: 0.35, ticksMax: 18, tickSemitones: 1.5,
    ribbonUp: 0.22, settle: 0.35, close: 0.25, closeAfterSkip: 0.4, ignoreInput: 0.15, skipFinalMs: 100,
    maxT: 4.5, smartPlays: 10,
    particlesMax: 400, goodBurst: 100, greatBurst: 140,
    sealGood: 0.14, sealGreat: 0.16, bannerUnroll: 0.3, shakePx: 3, shakeSec: 0.12, lightPulse: 1.2,
    harvestPerCity: 1.1, harvestMaxCities: 4, stageUpsMax: 3, monthBudget: 8,
    card: Object.freeze({ w: 640, h: 360, phoneW: 0.92, maxScale: 1.35 }),
    scene: Object.freeze({ x: 200, y: 74, w: 420, h: 180 }),
    storeKey: 'sanguozhi2_fx_v4',
    safetyExtra: 3.0,
  });

  // 华夏的建筑阶段名（CITY-VIEW §4.3；结果条的「阶段条」与大成竖幅用）
  const STAGE_NAMES = Object.freeze({
    farms: ['垦荒', '阡陌', '沟渠灌溉', '水碓', '沃野千里'], workshop: ['小冶', '冶坊', '水排鼓铸', '铁官', '百炼'],
    houses: ['茅舍', '闾里', '市楼', '坊市', '阛阓'], walls: ['土垣', '夯土城', '砖城', '马面', '瓮城'], market: ['草市', '市集', '大市', '都会', '万国之市'],
  });
  const BLDG_ZH = Object.freeze({ farms: '农田', workshop: '工坊', houses: '民居', walls: '城墙', market: '市' });
  const DEV_BLDG = Object.freeze({ land: 'farms', industry: 'workshop', town: 'houses' });
  const STAGE_ORD = ['第一阶段', '第二阶段', '第三阶段', '第四阶段', '第五阶段'];
  const NUM_ZH = ['', '一', '两', '三', '四', '五', '六', '七', '八', '九', '十'];

  // ---------------------------------------------------------------- 设置 --
  const DEF_SET = { domestic: 'smart', cityView: true, quality: 'auto', plays: {} };
  let settings = null;
  function loadSettings() {
    let o = null;
    try { if (typeof localStorage !== 'undefined') o = JSON.parse(localStorage.getItem(CFG.storeKey) || 'null'); } catch (e) { o = null; }
    settings = Object.assign({}, DEF_SET, o && typeof o === 'object' ? o : {});
    if (!settings.plays || typeof settings.plays !== 'object') settings.plays = {};
    if (['smart', 'full', 'highlights', 'fast', 'off'].indexOf(settings.domestic) < 0) settings.domestic = 'smart';
    return settings;
  }
  function saveSettings() {
    try {
      if (typeof localStorage === 'undefined') return;
      // 同一个键里还有画质（设置画面的「画质」行写 quality）：先读再合并
      let o = null;
      try { o = JSON.parse(localStorage.getItem(CFG.storeKey) || 'null'); } catch (e) { o = null; }
      localStorage.setItem(CFG.storeKey, JSON.stringify(Object.assign({}, o || {}, settings)));
    } catch (e) { /* 隐私模式：只留在内存 */ }
  }
  loadSettings();
  function setSetting(k, v) {
    if (!settings) loadSettings();
    if (k === 'domestic' && ['smart', 'full', 'highlights', 'fast', 'off'].indexOf(v) < 0) return false;
    settings[k] = v;
    saveSettings();
    return true;
  }
  const reduced = () => {
    try {
      if (SG.Motion && (SG.Motion.reduced || SG.Motion.level === 'off')) return true;
      return hasDoc && W.matchMedia ? W.matchMedia('(prefers-reduced-motion: reduce)').matches : false;
    } catch (e) { return false; }
  };
  const noFlash = () => { try { return !!(SG.UISettings && SG.UISettings.get('ui.flash') === 'none') || reduced(); } catch (e) { return reduced(); } };

  // 每条指令演了几次（智能档：前 10 次完整，之后普通 / 失手只放快速）
  function countPlay(id) {
    if (!settings) loadSettings();
    settings.plays[id] = (settings.plays[id] | 0) + 1;
    saveSettings();
  }
  // → 'full' | 'fast' | 'none'（§3.5 的表）
  function formFor(id, tier) {
    if (!settings) loadSettings();
    let mode = settings.domestic;
    if (mode === 'smart' && reduced()) mode = 'fast';
    const plain = tier === 'normal' || tier === 'miss';
    switch (mode) {
      case 'off': return 'none';
      case 'full': return 'full';
      case 'highlights': return plain ? 'fast' : 'full';
      case 'fast': return 'fast';
      default: return plain && (settings.plays[id] | 0) >= CFG.smartPlays ? 'fast' : 'full';
    }
  }

  // ---------------------------------------------------------------- 小工具 --
  function hashStr(s) { let h = 2166136261 >>> 0; s = String(s); for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h | 0; }
  function genOf(x) { if (x && typeof x === 'object') return x; const g = G(); return g && x !== null && x !== undefined && x >= 0 && g.generals ? g.generals[x] || null : null; }
  function cityOf(x) { if (x && typeof x === 'object') return x; const g = G(); return g && x !== null && x !== undefined && x >= 0 && g.cities ? g.cities[x] || null : null; }
  function facOf(f) { const g = G(); return g && f !== null && f !== undefined && f >= 0 && g.factions ? g.factions[f] || null : null; }
  function facColor(f, dflt) { const F = facOf(f); return (F && typeof F.color === 'string' && F.color) || dflt || '#3f7f4a'; }
  function rulerOf(f) { const g = G(); try { return g && f >= 0 ? g.ruler(f) : null; } catch (e) { return null; } }
  function fmt(v) {
    v = Math.round(v);
    const a = Math.abs(v);
    if (a > 100000) return (v / 10000).toFixed(a >= 1000000 ? 0 : 1) + '万';
    return v.toLocaleString('en-US');
  }
  const signed = v => (v > 0 ? '+' : v < 0 ? '−' : '±') + fmt(Math.abs(v));
  const ease3 = u => 1 - Math.pow(1 - u, 3);
  const maxStat = g => (g ? Math.max(g.war | 0, g.intel | 0, g.pol | 0) : 0);
  const sfx = (n, v, o) => { try { if (SG.Sfx && SG.Sfx.play && n) SG.Sfx.play(n, v === undefined ? 0.9 : v, o); } catch (e) { /* 无音频 */ } };
  let warned = 0;
  function warnOnce(e) { if (warned++ < 3) console.warn('DomesticFx:', e && e.message ? e.message : e); }
  function playerF() { const g = G(); return g ? g.player : -1; }
  function familyOfPlayer() {
    try {
      const MS = SG.MusicStings;
      if (SG.MusicSuites && SG.MusicSuites.suiteOf) {
        const g = G(), name = (SG.Sfx && SG.Sfx.suite) || (g && g.player >= 0 ? g.ruler(g.player).name : null);
        const s = name ? SG.MusicSuites.suiteOf(name) : null;
        if (s && s.family) return s.family;
      }
      const F = facOf(playerF());
      return MS && F ? MS.familyOf(F.culture) : 'hua';
    } catch (e) { return 'hua'; }
  }
  function stageOf(bldg, c, prev) {
    try { if (SG.Econ && SG.Econ.stageOf) return SG.Econ.stageOf(bldg, c, prev); } catch (e) { /* 无 Econ */ }
    const T = (SG.Balance && SG.Balance.CityLevel && SG.Balance.CityLevel.T[bldg]) || [300, 450, 600, 800];
    const v = bldg === 'farms' ? c.land : bldg === 'workshop' ? c.industry : bldg === 'houses' ? c.town : 0;
    return T.filter(x => v >= x).length;
  }
  function stageThr(bldg) { return (SG.Balance && SG.Balance.CityLevel && SG.Balance.CityLevel.T[bldg]) || [300, 450, 600, 800]; }
  const CORE = () => (SG.Balance && SG.Balance.CityLevel && SG.Balance.CityLevel.CORE) || ['farms', 'workshop', 'houses', 'walls', 'market'];
  // G.fxStages（§10.3）：每城最近一次演给玩家看的阶段（CityLevel.CORE 的次序，一位一阶）
  function shownStages(c) {
    const g = G();
    if (!g || !c) return null;
    const s = g.fxStages && g.fxStages[c.id];
    if (typeof s !== 'string' || s.length < CORE().length) return null;
    const o = {};
    CORE().forEach((k, i) => { o[k] = +s.charAt(i) || 0; });
    return o;
  }
  function rememberStages(c, st) {
    const g = G();
    if (!g || !c || !st) return;
    if (!g.fxStages || typeof g.fxStages !== 'object') g.fxStages = {};
    g.fxStages[c.id] = CORE().map(k => String(clamp(st[k] | 0, 0, 4))).join('');
  }
  function currentStages(c) {
    const prev = shownStages(c) || undefined;
    const o = {};
    for (const k of CORE()) o[k] = stageOf(k, c, prev ? prev[k] : undefined);
    return o;
  }

  // ================================================================ timeline ==
  // 演出时钟：at(t, fn) 定时回调 · anim(el, keyframes, at, dur, opts) Web Animations · count(...) 数字滚动；
  // skip() 立即执行全部待办、结束全部动画、数字到终值（≤ 100 ms 到达最终画面）
  function Timeline() {
    this.t0 = now(); this.items = []; this.anims = []; this.counters = []; this.skipped = false; this.frames = []; this.maxFrame = 0;
  }
  Timeline.prototype.time = function () { return now() - this.t0; };
  Timeline.prototype.at = function (t, fn) {
    if (this.skipped) { try { fn(); } catch (e) { warnOnce(e); } return; }
    this.items.push({ t, fn });
    this.items.sort((a, b) => a.t - b.t);
  };
  Timeline.prototype.anim = function (el, kf, at, dur, o) {
    if (!el || typeof el.animate !== 'function') return null;
    o = o || {};
    const delay = Math.max(0, (at || 0) - this.time());
    let a = null;
    try {
      a = el.animate(kf, { duration: Math.max(1, (dur || 0.3) * 1000), delay: delay * 1000, easing: o.ease || 'cubic-bezier(.2,.8,.2,1)', fill: o.fill || 'both', iterations: o.iter || 1, direction: o.dir || 'normal' });
    } catch (e) { warnOnce(e); return null; }
    this.anims.push(a);
    if (this.skipped) { try { a.finish(); } catch (e) { /* 忽略 */ } }
    return a;
  };
  // 数字滚动：从 from 到 to，easing 1 − (1 − u)³，整数步；每显示一步一声 fx-tick（至多 18 声、每声升 1.5 半音），终值 fx-land
  Timeline.prototype.count = function (el, from, to, at, dur, o) {
    o = o || {};
    const c = { el, from, to, at, dur: Math.max(0.05, dur), fmt: o.fmt || fmt, last: null, ticks: 0, sound: o.sound !== false, done: false, onDone: o.onDone, onStep: o.onStep };
    const steps = Math.abs(Math.round(to) - Math.round(from));
    c.nTicks = Math.min(CFG.ticksMax, steps);
    if (el) el.textContent = c.fmt(from);
    this.counters.push(c);
    if (this.skipped) this.finishCounter(c);
    return c;
  };
  Timeline.prototype.finishCounter = function (c) {
    if (c.done) return;
    c.done = true;
    if (c.el) c.el.textContent = c.fmt(c.to);
    if (c.onStep) try { c.onStep(1); } catch (e) { /* 忽略 */ }
    if (c.sound && !this.skipped) sfx('fx-land', 0.6);
    if (c.onDone) try { c.onDone(); } catch (e) { /* 忽略 */ }
  };
  Timeline.prototype.tick = function () {
    const t = this.time();
    while (this.items.length && this.items[0].t <= t) { const it = this.items.shift(); try { it.fn(); } catch (e) { warnOnce(e); } }
    for (const c of this.counters) {
      if (c.done || t < c.at) continue;
      const u = clamp((t - c.at) / c.dur, 0, 1);
      const v = Math.round(c.from + (c.to - c.from) * ease3(u));
      if (v !== c.last) {
        c.last = v;
        if (c.el) c.el.textContent = c.fmt(v);
        if (c.onStep) try { c.onStep(u); } catch (e) { /* 忽略 */ }
        const k = c.nTicks ? Math.floor(u * c.nTicks) : 0;
        if (c.sound && k > c.ticks && c.ticks < CFG.ticksMax) { c.ticks = k; sfx('fx-tick', 0.35, { pitch: Math.pow(2, c.ticks * CFG.tickSemitones / 12) }); }
      }
      if (u >= 1) this.finishCounter(c);
    }
  };
  Timeline.prototype.skip = function () {
    if (this.skipped) return;
    this.skipped = true;
    const items = this.items; this.items = [];
    for (const it of items) { try { it.fn(); } catch (e) { warnOnce(e); } }
    for (const a of this.anims) { try { a.finish(); } catch (e) { /* 无限循环的动画：取消 */ try { a.cancel(); } catch (e2) { /* 忽略 */ } } }
    for (const c of this.counters) this.finishCounter(c);
  };
  Timeline.prototype.dispose = function () {
    for (const a of this.anims) { try { a.cancel(); } catch (e) { /* 忽略 */ } }
    this.anims = []; this.items = []; this.counters = [];
  };

  // ================================================================ particles ==
  // 一张画布、一批粒子（≤ 400）：火星、纸屑、金箔、花瓣、尘、墨点、落叶、铜钱、乌鸦……（墨、纸、金、朱，不用霓虹）
  const PRESET = {
    spark: { col: ['#ffd27a', '#ff9a3c', '#ffe9b0'], v: [60, 220], g: 380, life: [0.25, 0.5], size: [1.5, 3], shape: 'dot', up: 0.7 },
    confetti: { col: ['#c8382c', '#e8b54a', '#f3e7cc'], v: [80, 220], g: 220, life: [0.9, 1.6], size: [3, 6], shape: 'rect', up: 0.9, drag: 1.6 },
    gold: { col: ['#f8dc8e', '#e8b54a', '#fff1c8'], v: [40, 140], g: 120, life: [0.8, 1.5], size: [1.4, 3.0], shape: 'leaf', up: 0.6, drag: 1.4 },
    petal: { col: ['#f2b8c6', '#f7d3dc', '#fff1c8'], v: [20, 70], g: 30, life: [1.2, 2.2], size: [2.5, 4.5], shape: 'leaf', up: 0.2, drag: 0.8, wind: 40 },
    dust: { col: ['#cdb48a', '#b89a68', '#e3d0a8'], v: [20, 80], g: -10, life: [0.5, 1.0], size: [3, 7], shape: 'puff', up: 0.4, drag: 2 },
    ink: { col: ['#241a21', '#3a2a24'], v: [80, 260], g: 300, life: [0.3, 0.6], size: [1.5, 4], shape: 'dot', up: 0.5 },
    seal: { col: ['#c8382c', '#8a2418'], v: [90, 240], g: 260, life: [0.25, 0.5], size: [1.5, 3.5], shape: 'dot', up: 0.4 },
    leaf: { col: ['#c99a2e', '#a0703f', '#8fa65e'], v: [40, 120], g: 40, life: [1.0, 1.8], size: [3, 5], shape: 'leaf', up: 0.2, drag: 0.6, wind: 120 },
    coin: { col: ['#e8b54a', '#f8dc8e'], v: [60, 160], g: 420, life: [0.6, 1.0], size: [3, 4.5], shape: 'coin', up: 0.8 },
    grain: { col: ['#e8c25a', '#d9a53a', '#f3e1a0'], v: [30, 110], g: 160, life: [0.7, 1.3], size: [1.5, 3], shape: 'dot', up: 0.7 },
    crow: { col: ['#241a21'], v: [60, 120], g: -40, life: [1.2, 1.8], size: [4, 6], shape: 'bird', up: 1, drag: 0.2 },
    water: { col: ['#8fb6d0', '#cfe4ef'], v: [30, 90], g: 200, life: [0.4, 0.8], size: [1.5, 3], shape: 'dot', up: 0.8 },
    bell: { col: ['#f3c969', '#fff1c8'], v: [20, 60], g: -20, life: [0.8, 1.2], size: [2, 3.5], shape: 'ring', up: 0.5, drag: 1 },
  };
  function Particles(cv, scale, rng) { this.cv = cv; this.ctx = cv ? cv.getContext('2d') : null; this.k = scale; this.list = []; this.rng = rng; this.spawned = 0; }
  Particles.prototype.burst = function (x, y, preset, n, o) {
    const P = PRESET[preset] || PRESET.spark;
    o = o || {};
    const R = this.rng;
    n = Math.max(0, Math.min(n | 0, CFG.particlesMax - this.list.length));
    for (let i = 0; i < n; i++) {
      const ang = o.dir !== undefined ? o.dir + (R() - 0.5) * (o.spread || 1.2) : -Math.PI / 2 + (R() - 0.5) * Math.PI * 2 * (1 - (P.up || 0) * 0.6);
      const v = P.v[0] + R() * (P.v[1] - P.v[0]);
      this.list.push({ x: x + (R() - 0.5) * (o.w || 0), y: y + (R() - 0.5) * (o.h || 0), vx: Math.cos(ang) * v, vy: Math.sin(ang) * v, age: 0, life: P.life[0] + R() * (P.life[1] - P.life[0]),
        s: P.size[0] + R() * (P.size[1] - P.size[0]), col: P.col[Math.floor(R() * P.col.length)], P, rot: R() * 6.28, vr: (R() - 0.5) * 8 });
    }
    this.spawned += n;
  };
  Particles.prototype.step = function (dt) {
    const c = this.ctx;
    if (!c) return;
    c.clearRect(0, 0, this.cv.width, this.cv.height);
    if (!this.list.length) return;
    const k = this.k;
    const out = [];
    for (const p of this.list) {
      p.age += dt;
      if (p.age >= p.life) continue;
      const P = p.P;
      if (P.stopAt && p.age >= P.stopAt) { p.vx = 0; p.vy = 0; }
      p.vy += (P.g || 0) * dt;
      if (P.drag) { p.vx *= 1 - Math.min(0.9, P.drag * dt); p.vy *= 1 - Math.min(0.9, P.drag * dt * 0.5); }
      if (P.wind) p.vx += P.wind * dt * 0.5;
      p.x += p.vx * dt; p.y += p.vy * dt; p.rot += p.vr * dt;
      const a = 1 - Math.pow(p.age / p.life, 2);
      c.globalAlpha = clamp(a, 0, 1);
      c.fillStyle = p.col; c.strokeStyle = p.col;
      const x = p.x * k, y = p.y * k, s = p.s * k;
      switch (P.shape) {
        case 'rect': c.save(); c.translate(x, y); c.rotate(p.rot); c.fillRect(-s, -s * 0.5, s * 2, s); c.restore(); break;
        case 'leaf': c.save(); c.translate(x, y); c.rotate(p.rot); c.beginPath(); c.ellipse(0, 0, s * 1.4, s * 0.6, 0, 0, 6.283); c.fill(); c.restore(); break;
        case 'puff': c.globalAlpha *= 0.5; c.beginPath(); c.arc(x, y, s * (1 + p.age / p.life), 0, 6.283); c.fill(); break;
        case 'coin': c.beginPath(); c.arc(x, y, s, 0, 6.283); c.fill(); c.fillStyle = '#5e3f22'; c.fillRect(x - s * 0.3, y - s * 0.3, s * 0.6, s * 0.6); break;
        case 'ring': c.lineWidth = 1.2 * k; c.beginPath(); c.arc(x, y, s * (1 + 2 * p.age / p.life), 0, 6.283); c.stroke(); break;
        case 'bird': c.lineWidth = 1.6 * k; c.beginPath(); const f = Math.sin(p.age * 18) * 0.6; c.moveTo(x - s, y - s * f); c.quadraticCurveTo(x - s * 0.4, y, x, y + s * 0.2); c.quadraticCurveTo(x + s * 0.4, y, x + s, y - s * f); c.stroke(); break;
        default: c.beginPath(); c.arc(x, y, s, 0, 6.283); c.fill();
      }
      out.push(p);
    }
    c.globalAlpha = 1;
    this.list = out;
  };
  Particles.prototype.fade = function () { for (const p of this.list) p.life = Math.min(p.life, p.age + 0.15); };

  // ================================================================ 样式 ==
  const CSS = `
.dfx-root { position: fixed; inset: 0; z-index: 70; display: grid; place-items: center; pointer-events: auto; cursor: pointer; -webkit-tap-highlight-color: transparent; user-select: none; }
.dfx-dim { position: absolute; inset: 0; background: radial-gradient(120% 90% at 50% 45%, rgba(24,16,12,.35), rgba(12,8,6,.62)); opacity: 0; transition: opacity .25s ease; }
.dfx-root.is-on .dfx-dim { opacity: 1; }
.dfx-root.is-out { pointer-events: none; }
.dfx-root.is-out .dfx-dim { opacity: 0; }
.dfx-card { position: relative; width: 640px; height: 360px; flex: none; border-radius: 14px; overflow: hidden; transform-origin: 50% 50%;
  background: #f3e7cc linear-gradient(180deg, #f6ecd5, #ecdcb8); box-shadow: 0 0 0 2px #241a21, 0 0 0 5px #4a3229, 0 0 0 6px #a8742e, 0 22px 50px -12px rgba(0,0,0,.75);
  font-family: var(--f-display, "STKaiti","Kaiti SC","KaiTi",serif); color: #241a21; }
.dfx-card::before { content: ""; position: absolute; inset: 0; pointer-events: none; opacity: .5; mix-blend-mode: multiply;
  background: radial-gradient(60% 40% at 80% 85%, rgba(110,80,40,.18), transparent), radial-gradient(50% 50% at 15% 30%, rgba(120,90,50,.12), transparent),
  repeating-linear-gradient(97deg, rgba(90,60,30,.035) 0 2px, transparent 2px 7px); }
.dfx-card.is-night { background: #1f2f49 linear-gradient(180deg, #24344f, #17233a); color: #f3e7cc; }
.dfx-card.is-night .dfx-scene-bg { opacity: .0; }
.dfx-head { position: absolute; left: 0; right: 0; top: 0; height: 64px; display: flex; align-items: center; gap: 12px; padding: 0 22px 6px 200px;
  background: var(--dfx-ac, #4f9a76); color: #fff6e2; clip-path: polygon(0 0, 100% 0, 100% 78%, 96% 86%, 88% 80%, 70% 92%, 52% 82%, 30% 94%, 12% 84%, 0 92%); }
.dfx-head::after { content: ""; position: absolute; inset: 0; background: linear-gradient(180deg, rgba(255,255,255,.18), rgba(0,0,0,.12)); pointer-events: none; }
.dfx-title { font-size: 30px; letter-spacing: .12em; text-shadow: 0 2px 0 rgba(36,26,33,.45); white-space: nowrap; }
.dfx-sub { font-size: 16px; opacity: .9; font-family: var(--f-body, sans-serif); letter-spacing: .04em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.dfx-iconbox { position: absolute; left: 22px; top: 70px; width: 160px; height: 160px; display: grid; place-items: center; }
.dfx-scene { position: absolute; left: ${CFG.scene.x}px; top: ${CFG.scene.y}px; width: ${CFG.scene.w}px; height: ${CFG.scene.h}px; overflow: visible; }
.dfx-scene * { transform-box: fill-box; transform-origin: 50% 50%; }
.dfx-scene .vb { transform-box: view-box; }
.dfx-chip b.is-gain { color: #2e6e3a; } .dfx-chip b.is-cost { color: #a8371f; }
.dfx-actors { position: absolute; left: ${CFG.scene.x}px; top: ${CFG.scene.y}px; width: ${CFG.scene.w}px; height: ${CFG.scene.h}px; pointer-events: none; }
.dfx-actor { position: absolute; transform-origin: 50% 100%; }
.dfx-actor .sg-portrait { box-shadow: 0 0 0 2px #241a21, 0 4px 10px rgba(0,0,0,.35); border-radius: 8px; }
.dfx-actor .dfx-plate { position: absolute; left: 50%; bottom: -18px; transform: translateX(-50%); white-space: nowrap; font-size: 14px; padding: 1px 8px; border-radius: 3px;
  background: #241a21; color: #f3e7cc; box-shadow: 0 0 0 1px #a8742e; }
.dfx-pcv { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; }
.dfx-ribbon { position: absolute; left: 50%; bottom: 12px; width: 64%; min-width: 410px; transform: translateX(-50%); padding: 8px 16px 9px; color: #241a21;
  background: #f3e7cc linear-gradient(180deg, #fbf3df, #ead9b2); box-shadow: 0 6px 14px -6px rgba(40,18,8,.6), 0 0 0 1px rgba(36,26,33,.35);
  clip-path: polygon(1% 8%, 4% 0, 30% 5%, 60% 0, 97% 4%, 100% 14%, 99% 50%, 100% 88%, 96% 100%, 70% 95%, 40% 100%, 3% 96%, 0 84%, 1.4% 50%); font-family: var(--f-body, sans-serif); }
.dfx-chips { display: flex; flex-wrap: wrap; justify-content: center; gap: 4px 18px; align-items: baseline; }
.dfx-chip { display: inline-flex; align-items: baseline; gap: 6px; font-size: 15px; white-space: nowrap; }
.dfx-chip > .ico { width: 20px; height: 20px; align-self: center; }
.dfx-chip b { font: 600 22px/1.1 var(--f-num, Georgia, serif); font-variant-numeric: tabular-nums; }
.dfx-chip i { font-style: normal; font: 600 17px/1 var(--f-num, Georgia, serif); color: #2e6e3a; }
.dfx-chip i.is-cost { color: #a8371f; }
.dfx-chip i.is-amber { color: #b36b12; }
.dfx-chip.is-small { font-size: 13px; } .dfx-chip.is-small b { font-size: 16px; }
.dfx-fore { margin-top: 3px; text-align: center; font-size: 14px; color: #4b3b38; opacity: 0; }
.dfx-sbar { position: relative; height: 7px; margin: 5px 6% 0; border-radius: 4px; background: rgba(36,26,33,.14); overflow: hidden; }
.dfx-sbar > i { position: absolute; left: 0; top: 0; bottom: 0; width: 0; border-radius: 4px; background: linear-gradient(90deg, #a8742e, #e8b54a); }
.dfx-sbar.is-up > i { background: linear-gradient(90deg, #c8382c, #f8dc8e); }
.dfx-slbl { text-align: center; font-size: 12.5px; color: #6e5d53; margin-top: 2px; }
.dfx-ribbon.is-miss { filter: grayscale(.85); }
.dfx-miss { text-align: center; font: 24px/1.2 var(--f-display, serif); color: #4b3b38; letter-spacing: .1em; }
.dfx-hint { text-align: center; font-size: 14px; color: #6e5d53; margin-top: 2px; }
.dfx-caption { text-align: center; font-size: 13.5px; color: #4b3b38; margin-top: 2px; }
.dfx-good { position: absolute; right: calc(18% - 30px); bottom: 70px; z-index: 3; width: 44px; height: 44px; border-radius: 50%; display: grid; place-items: center; font: 26px/1 var(--f-display, serif);
  color: #f3c969; background: radial-gradient(circle at 35% 30%, #4a3a44, #2a2430); box-shadow: 0 0 0 2px #f3c969, 0 0 0 4px #2a2430, 0 4px 8px rgba(0,0,0,.4); }
.dfx-seal { position: absolute; right: 22px; top: 76px; width: 72px; height: 72px; display: grid; place-items: center; background: #c8382c; color: #fff4e0;
  font: 30px/1 var(--f-display, serif); letter-spacing: -.02em; writing-mode: vertical-rl; text-orientation: upright; border-radius: 4px 6px 3px 7px;
  box-shadow: inset 0 0 0 3px #fff4e0, inset 0 0 0 5px #c8382c, 0 2px 0 rgba(90,26,18,.6); filter: url(#dfx-rough); }
.dfx-banner { position: absolute; top: 0; left: 70px; width: 64px; height: 252px; z-index: 2; transform-origin: 50% 0; display: grid; place-items: start center; padding-top: 20px;
  background: linear-gradient(90deg, #1d1418, #2f2228 45%, #1d1418); color: #f8dc8e; box-shadow: 0 8px 18px rgba(0,0,0,.45);
  clip-path: polygon(0 0, 100% 0, 100% 94%, 88% 97%, 72% 93%, 50% 100%, 30% 94%, 12% 98%, 0 93%); }
.dfx-banner span { writing-mode: vertical-rl; text-orientation: upright; font: 30px/1.05 var(--f-display, serif); letter-spacing: .08em; text-shadow: 0 2px 0 #000; max-height: 216px; overflow: hidden; white-space: nowrap; }
.dfx-light { position: absolute; inset: 0; pointer-events: none; background: linear-gradient(105deg, transparent 30%, rgba(255,236,190,.55) 50%, transparent 70%); opacity: 0; }
.dfx-warm { position: absolute; inset: 0; pointer-events: none; background: radial-gradient(80% 70% at 50% 40%, rgba(255,196,110,.38), rgba(255,160,60,.12) 60%, transparent); opacity: 0; mix-blend-mode: screen; }
.dfx-vig { position: absolute; inset: 0; pointer-events: none; box-shadow: inset 0 0 60px 18px rgba(36,26,33,.55); opacity: 0; }
.dfx-skiphint { position: absolute; right: 14px; bottom: -26px; font: 12.5px/1 var(--f-body, sans-serif); color: rgba(243,231,204,.78); letter-spacing: .1em; }
.dfx-tag { position: absolute; padding: 2px 8px; border-radius: 3px; background: #c8382c; color: #fff4e0; font: 15px/1.2 var(--f-display, serif); box-shadow: 0 0 0 1.5px #5a1a12; }
html.sg-short .dfx-skiphint, .dfx-root.is-phone .dfx-skiphint { display: none; }
@media (prefers-reduced-motion: reduce) { .dfx-root * { transition: none !important; } }
`;
  function injectCss() {
    if (!hasDoc || document.getElementById('sg-dfx-css')) return;
    const s = document.createElement('style');
    s.id = 'sg-dfx-css';
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
    // 印面的粗糙边（3 % 噪声）
    const f = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    f.setAttribute('width', '0'); f.setAttribute('height', '0'); f.setAttribute('aria-hidden', 'true');
    f.style.position = 'absolute';
    f.innerHTML = '<filter id="dfx-rough"><feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="7"/><feDisplacementMap in="SourceGraphic" scale="2.2"/></filter>';
    (document.body || document.documentElement).appendChild(f);
  }

  // ================================================================ 脚本登记表 ==
  const SCRIPTS = {};
  function define(id, s) { SCRIPTS[id] = Object.assign({ id, icon: id, sting: id, stage: 'city' }, s); return SCRIPTS[id]; }

  // ================================================================ 舞台 ==
  const STAGES = {};
  function registerStage(name, factory) { if (typeof factory === 'function') STAGES[name] = factory; return () => { if (STAGES[name] === factory) delete STAGES[name]; }; }
  function stageFor(res, script) {
    if (formFor(script ? script.id : SG.DomesticTiers ? SG.DomesticTiers.subId(res) : res.cmd, 'great') === 'none') return 'none';
    const s = script || SCRIPTS[SG.DomesticTiers ? SG.DomesticTiers.subId(res) : res.cmd] || {};
    if (s.stage === 'card') return 'card';
    try {
      const CV = SG.CityView;
      if (CV && STAGES.city && settings.cityView !== false && CV.isOpen && CV.isOpen(res.city)) return 'city';
      const q = (SG.Quality && SG.Quality.tier) || 'low';
      if (CV && STAGES.diorama && settings.cityView !== false && q !== 'low' && CV.canBuild && CV.canBuild(res.city)) return 'diorama';
    } catch (e) { /* 退回牌面 */ }
    return 'card';
  }

  // 缺省长度（不放音乐时；放音乐时 T / A 来自短乐）
  function timeMap(id, tier, form) {
    if (form === 'fast') {
      const T = CFG.fastT[tier] || 1.0;
      const R = Math.min(T - 0.5, 0.1 + CFG.fastAction);
      return { T, A: R, R, fast: true, a0: Math.max(0.05, R - CFG.fastAction), a1: R };
    }
    const d = CFG.defaults[tier] || CFG.defaults.normal;
    return finishMap(d[0], d[1], tier, id);
  }
  function finishMap(T, A, tier, id, chance) {
    T = Math.min(CFG.maxT, T);
    let R = A;
    // 确定的指令的普通档只有 A 段：结果在 0.55 T 揭示（§3.4）
    if (tier === 'normal' && A >= T - 1e-6) R = CFG.normalRevealK * T;
    R = clamp(R, 0.5, T - 0.4);
    return { T, A: Math.min(A, T), R, fast: false, a0: CFG.actionStart, a1: R };
  }

  // ================================================================ 短乐 ==
  function startSting(id, tier, fast, opts) {
    try {
      const Sfx = SG.Sfx, MS = SG.MusicStings;
      if (!Sfx || typeof Sfx.cmdSting !== 'function' || !Sfx.musicOn) return null;
      const family = (opts && opts.family) || familyOfPlayer();
      if (MS) {
        MS.ensure(id, family);
        if (fast) MS.tagKey(id, tier, family);
      }
      const r = Sfx.cmdSting(fast ? id + '~tag' : id, tier, { family, answerFamily: opts && opts.answerFamily, fast: !!fast });
      if (!r || !(r.duration > 0)) return null;
      const secs = r.sections || {};
      const offs = Object.keys(secs).map(k => secs[k]).filter(v => v > 0).sort((a, b) => a - b);
      const A = offs.length ? offs[0] : r.duration;
      return { key: r.key, T: r.duration, A };
    } catch (e) { warnOnce(e); return null; }
  }
  function tierSfx(tier) {
    if (tier === 'great') sfx('fx-great', 0.8); else if (tier === 'good') sfx('fx-good', 0.8); else if (tier === 'miss') sfx('fx-miss', 0.7);
  }

  // ================================================================ 记录的整理 ==
  // 一次「移动」会对每名武将写一条记录：合成一条（gens、troops、ids 合计）；拉拢 / 搜索等原样
  function coalesce(list) {
    const out = [];
    for (const r of list) {
      const last = out[out.length - 1];
      if (last && r.cmd === 'move' && last.cmd === 'move' && r.ok !== false && last.ok !== false && r.to === last.to && r.city === last.city && r.month === last.month) {
        last.gens = (last.gens | 0) + (r.gens | 0);
        last.troops = (last.troops | 0) + (r.troops | 0);
        last.ids = (last.ids || []).concat(r.ids || []);
        last.ruler = last.ruler || r.ruler;
        last.frontline = last.frontline || r.frontline;
        last._merged = (last._merged || [last._orig || last]).concat([r]);
        continue;
      }
      out.push(r.cmd === 'move' ? Object.assign({}, r, { _orig: r }) : r);
    }
    return out;
  }
  // 开发：阶段之前 / 之后（G.fxStages 是玩家上次看到的阶段，含回落滞后；§3.2）
  function fillStages(res) {
    if (res.cmd !== 'develop' || res.reason || typeof res.stBefore === 'number') return;
    const c = cityOf(res.city);
    if (!c) return;
    const b = DEV_BLDG[res.kind] || 'farms';
    const shown = shownStages(c);
    const thr = stageThr(b);
    const stB = shown ? shown[b] : thr.filter(x => res.before >= x).length;
    const stA = stageOf(b, c, stB);
    res.stBefore = stB; res.stAfter = stA;
    const cur = shown || currentStages(c);
    cur[b] = stA;
    rememberStages(c, cur);
  }

  // ================================================================ 结果条：每条指令的数字 ==
  // chip：{ icon, label, from, to } 滚动 · { label, delta } 只显示增减 · cost：花费（红、小号）· amber：琥珀色（稀释等）
  function chipsFor(script, res, ctx) {
    try { return (script.chips ? script.chips(res, ctx) : []) || []; } catch (e) { warnOnce(e); return []; }
  }

  // ================================================================ 导演 ==
  let queue = Promise.resolve();
  let current = null;
  const Fx = {
    CFG, STAGE_NAMES, BLDG_ZH, SCRIPTS, STAGES, PRESET,
    busy: false, auto: true, stats: null,
    get settings() { return settings || loadSettings(); },
    setSetting, loadSettings, formFor, stageFor, timeMap, define, registerStage, coalesce,
    get mode() { return (settings || loadSettings()).domestic; },
    play(res, tierInfo, opts) {
      if (res && typeof res === 'object') { res._fxPlayed = true; if (res._orig) res._orig._fxPlayed = true; }
      const p = queue.then(() => run(res, tierInfo, opts || {})).catch(e => { warnOnce(e); });
      queue = p;
      return p;
    },
    skip() { if (current) current.skip(); },
    moment(id, res, opts) { return Fx.play(Object.assign({ cmd: id }, res || {}), null, opts); },
    harvest(records, opts) {
      if (records && typeof records === 'object') try { records._fxPlayed = true; } catch (e) { /* 忽略 */ }
      const g = G();
      const mine = (records || []).filter(h => { const c = g && g.cities ? g.cities[h.city] : null; return !c || !g || c.owner === g.player || (opts && opts.all); });
      if (!mine.length) return Promise.resolve();
      return Fx.play({ cmd: 'harvest', records: mine, _moment: true }, null, opts);
    },
    stageUps(list, opts) {
      if (list && typeof list === 'object') try { list._fxPlayed = true; } catch (e) { /* 忽略 */ }
      list = (list || []).filter(Boolean);
      if (!list.length) return Promise.resolve();
      // 每城一张（至多 3 城），同城的几座建筑列在一起
      const by = new Map();
      for (const s of list) { const k = s.city; if (!by.has(k)) by.set(k, []); by.get(k).push(s); }
      let p = Promise.resolve();
      let n = 0;
      for (const [cid, ups] of by) { if (n++ >= CFG.stageUpsMax) break; p = p.then(() => Fx.play({ cmd: 'stageUp', city: cid, ups, _moment: true }, null, opts)); }
      return p;
    },
    // 月初：比较 G.fxStages，列出这一个月里升阶的建筑（降阶不庆祝，只更新）
    monthStart(g) {
      g = g || G();
      const out = { stageUps: [] };
      if (!g || g.player < 0) return out;
      for (const c of g.citiesOf(g.player)) {
        const shown = shownStages(c);
        const now2 = currentStages(c);
        if (shown) for (const k of CORE()) if (now2[k] > shown[k]) out.stageUps.push({ city: c.id, building: k, from: shown[k], to: now2[k] });
        rememberStages(c, now2);
      }
      return out;
    },
    // 测试页：不经过规则，直接演一条记录（tests/domestic-fx.html）
    preview(res, tierInfo, opts) { return Fx.play(res, tierInfo, Object.assign({ preview: true }, opts || {})); },
  };

  async function run(res, tierInfo, opts) {
    if (!res || !hasDoc || !SG.DomesticTiers) return;
    const DT = SG.DomesticTiers;
    fillStages(res);
    const id = res.cmd === 'search' ? 'search' : DT.subId(res);
    const script = SCRIPTS[id] || SCRIPTS[res.cmd];
    if (!script) return;
    let ti = tierInfo || (res.cmd === 'harvest' && res.records ? harvestTier(res.records) : res.cmd === 'stageUp' ? { tier: 'great', why: ['stage'], stamp: DT.STAMPS.stageUp } : DT.tier(res));
    if (!ti || !ti.tier) return;
    const tier = ti.tier;
    const form = opts.form || formFor(id, tier);
    if (!opts.preview) countPlay(id);
    if (form === 'none') { tierSfx(tier); return; }
    const stageName = opts.stage || stageFor(res, script);
    if (stageName === 'none') { tierSfx(tier); return; }
    Fx.busy = true;
    try { await vignette(res, ti, script, id, form, stageName, opts); }
    catch (e) { warnOnce(e); }
    finally { Fx.busy = false; current = null; }
  }

  function harvestTier(records) {
    const DT = SG.DomesticTiers;
    let best = null;
    for (const h of records) {
      const t = DT.tier(Object.assign({ cmd: 'harvest' }, h));
      h._t = t;
      if (!best || DT.RANK[t.tier] > DT.RANK[best.tier]) best = t;
    }
    return best;
  }

  function vignette(res, ti, script, id, form, stageName, opts) {
    return new Promise(resolve => {
      injectCss();
      if (SG.CmdIcons) SG.CmdIcons.injectCss();
      const tier = ti.tier;
      const g = G();
      const fast = form === 'fast';
      // 时间：短乐就是时钟（§3.4）
      const sting = opts.silent ? null : startSting(script.sting || id, tier, fast, { answerFamily: answerFamilyOf(res) });
      let tm = timeMap(id, tier, form);
      if (sting && !fast) tm = finishMap(sting.T, sting.A, tier, id);
      if (res.cmd === 'harvest' && res.records && !fast) {
        // 秋收的蒙太奇：每城 1.1 秒（至多 4 城），不短于短乐、不长于 4.5 秒
        const n = Math.min(CFG.harvestMaxCities, res.records.length);
        const T = clamp(Math.max(tm.T, n * CFG.harvestPerCity + 1.1), 1.5, CFG.maxT);
        tm = { T, A: T - 1.1, R: T - 1.1, fast: false, a0: CFG.actionStart, a1: T - 1.1 };
      }
      if (opts.T) tm = Object.assign({}, tm, { T: opts.T });
      const T = tm.T, R = tm.R, settleAt = T - CFG.settle;
      const seed = hashStr(res.cmd + '|' + (g ? g.monthIndex : 0) + '|' + (res.city === undefined ? -1 : res.city) + '|' + (g ? (g.tokensFor ? g.tokensFor(g.player) - g.tokens : g.tokens) : 0) + '|' + (res.gen || 0));
      const rnd = SG.SeededRandom ? SG.SeededRandom(seed) : null;
      const rng = rnd ? () => rnd.nextDouble() : Math.random;
      const tl = new Timeline();
      const root = document.createElement('div');
      root.className = 'dfx-root';
      root.setAttribute('role', 'dialog');
      root.setAttribute('aria-label', (SG.CmdIcons ? SG.CmdIcons.label(id) : id) + '：' + ({ great: '大成', good: '佳', normal: '完成', miss: '未果' }[tier] || ''));
      root.innerHTML = '<div class="dfx-dim"></div>';
      document.body.appendChild(root);
      const ctx = { fx: Fx, res, tier, ti, id, script, T, A: tm.A, R, a0: tm.a0, a1: tm.a1, settle: settleAt, fast, rng, root, tl, g,
        city: cityOf(res.city), gen: genOf(res.gen), fc: facColor(res.f !== undefined ? res.f : playerF()), sting, opts };
      // 结果条与竖幅的内容在开场时算好（揭示的那一帧只建 DOM，不查表；§12 每帧 ≤ 1.5 ms）
      ctx.data = {
        chips: chipsFor(script, res, ctx),
        stageBar: script.stageBar && tier !== 'miss' ? safe(() => script.stageBar(res, ctx)) : null,
        forecast: script.forecast && tier !== 'miss' ? safe(() => script.forecast(res, ctx)) : null,
        caption: script.caption && tier !== 'miss' ? safe(() => script.caption(res, ctx)) : null,
        banner: script.banner && tier === 'great' ? safe(() => script.banner(res, ctx)) : null,
        miss: tier === 'miss' ? (script.miss ? safe(() => script.miss(res, ctx)) : null) || {} : null,
      };
      const factory = STAGES[stageName] || STAGES.card;
      let stage = null;
      try { stage = factory(ctx); } catch (e) { warnOnce(e); stage = STAGES.card(ctx); }
      ctx.stage = stage;
      let closed = false, skipped = false;
      const t0 = now();
      const stats = { id, tier, form, stage: stageName, T, R, key: sting ? sting.key : null, start: t0, maxFrameMs: 0, frames: 0, jsMs: 0, skippedAt: null, finalAt: null, closedAt: null, nodes: 0, particles: 0 };
      Fx.stats = stats;
      const cleanup = () => {
        if (closed) return;
        closed = true;
        W.removeEventListener('keydown', onKey, true);
        root.removeEventListener('pointerdown', onPointer);
        root.classList.add('is-out');
        try { if (stage && stage.close) stage.close(); } catch (e) { warnOnce(e); }
        stats.closedAt = now() - t0;
        stats.nodes = root.querySelectorAll('*').length;
        if (ctx.P) stats.particles = ctx.P.spawned;
        setTimeout(() => { tl.dispose(); if (root.parentNode) root.parentNode.removeChild(root); }, CFG.close * 1000);
        if (tier === 'great') { try { if (SG.Shell && SG.Shell.echo && SG.CmdIcons) { const m = SG.CmdIcons.META[id]; if (m && m.dock) SG.Shell.echo(m.dock, 'great'); } } catch (e) { /* 忽略 */ } }
        clearTimeout(safety);
        resolve();
      };
      const skip = () => {
        if (closed) return;
        if (skipped) { cleanup(); return; }
        skipped = true;
        stats.skippedAt = now() - t0;
        tl.skip();
        try { if (stage.skip) stage.skip(); } catch (e) { warnOnce(e); }
        if (ctx.P) ctx.P.fade();
        stats.finalAt = now() - t0;
        setTimeout(cleanup, CFG.closeAfterSkip * 1000);
      };
      current = { skip, ctx };
      const onPointer = e => { if (now() - t0 < CFG.ignoreInput) return; e.preventDefault(); e.stopPropagation(); skip(); };
      const onKey = e => {
        const k = e.key;
        if (k !== 'Enter' && k !== ' ' && k !== 'Escape' && k !== 'Spacebar' && e.code !== 'Space') return;
        e.preventDefault(); e.stopImmediatePropagation();
        if (e.repeat || now() - t0 < CFG.ignoreInput) return;
        skip();
      };
      root.addEventListener('pointerdown', onPointer);
      W.addEventListener('keydown', onKey, true);
      const safety = setTimeout(cleanup, (T + CFG.safetyExtra) * 1000);

      try { stage.open(); } catch (e) { warnOnce(e); }
      requestAnimationFrame(() => root.classList.add('is-on'));
      // ---- 四拍 ----
      try { stage.beat('cue', {}); } catch (e) { warnOnce(e); }
      tl.at(fast ? 0.05 : CFG.actionStart, () => stage.beat('action', { a0: tm.a0, a1: tm.a1 }));
      tl.at(R, () => { stage.beat('reveal', {}); reveal(ctx); });
      if (tier === 'good') tl.at(R + 0.05, () => { stage.beat('good', {}); flourish(ctx); });
      if (tier === 'great') tl.at(R + 0.05, () => { stage.beat('great', {}); spectacle(ctx); });
      if (tier === 'miss') tl.at(R, () => { stage.beat('miss', {}); missFx(ctx); });
      tl.at(settleAt, () => { try { stage.beat('settle', {}); } catch (e) { /* 忽略 */ } });
      tl.at(T, () => { if (!skipped) cleanup(); });
      // ---- 帧循环 ----
      let last = now();
      const frame = () => {
        if (closed) return;
        const a = now();
        tl.tick();
        if (ctx.P) ctx.P.step(Math.min(0.05, a - last));
        last = a;
        const ms = (now() - a) * 1000;
        stats.jsMs += ms; stats.frames++; if (ms > stats.maxFrameMs) { stats.maxFrameMs = ms; stats.maxFrameAt = +(a - t0).toFixed(2); }
        requestAnimationFrame(frame);
      };
      requestAnimationFrame(frame);
    });
  }
  function answerFamilyOf(res) {
    try {
      if (res.cmd !== 'ally' && res.cmd !== 'marriage') return null;
      const F = facOf(res.target);
      return F && SG.MusicStings ? SG.MusicStings.familyOf(F.culture) : null;
    } catch (e) { return null; }
  }

  // ---- 揭示：结果条滑入、数字滚动、预报、阶段条 ----
  function reveal(ctx) {
    const { tl, res, script, tier, fast } = ctx;
    const hud = ctx.stage.hud;
    if (!hud) return;
    const rb = document.createElement('div');
    rb.className = 'dfx-ribbon' + (tier === 'miss' ? ' is-miss' : '');
    hud.appendChild(rb);
    ctx.ribbon = rb;
    const t = tl.time();
    tl.anim(rb, [{ transform: 'translate(-50%, 120%)', opacity: 0 }, { transform: 'translate(-50%, 0)', opacity: 1 }], t, CFG.ribbonUp, { ease: 'cubic-bezier(.2,.9,.3,1.1)' });
    const cu = fast ? CFG.countUpFast : CFG.countUp;
    const D0 = ctx.data;
    if (tier === 'miss') {
      const m = D0.miss || {};
      rb.innerHTML = '<div class="dfx-miss">' + esc(m.title || '未果') + '</div>' + (m.hint ? '<div class="dfx-hint">' + esc(m.hint) + '</div>' : '');
      if (D0.chips.length) appendChips(ctx, rb, D0.chips, t + 0.1, cu, true);
      return;
    }
    appendChips(ctx, rb, D0.chips, t + 0.12, cu);
    // 阶段条（与城市相连的指令）
    const sb = D0.stageBar;
    if (sb) {
      const bar = document.createElement('div'); bar.className = 'dfx-sbar'; bar.innerHTML = '<i></i>';
      const lbl = document.createElement('div'); lbl.className = 'dfx-slbl';
      rb.appendChild(bar); rb.appendChild(lbl);
      const fill = bar.firstChild;
      const pct = v => clamp(v, 0, 1) * 100 + '%';
      fill.style.width = pct(sb.from);
      lbl.textContent = sb.startLabel || sb.label;
      tl.anim(fill, [{ width: pct(sb.from) }, { width: pct(sb.up ? 1 : sb.to) }], t + 0.12, cu, { ease: 'cubic-bezier(.2,.8,.2,1)' });
      tl.at(t + 0.12 + cu, () => {
        if (!sb.up) { lbl.textContent = sb.label; return; }
        bar.classList.add('is-up'); lbl.textContent = sb.upLabel;
        tl.anim(bar, [{ filter: 'brightness(1.8)' }, { filter: 'brightness(1)' }], tl.time(), 0.4);
      });
    }
    const fc = D0.forecast;
    const cap = D0.caption;
    if (cap) { const c = document.createElement('div'); c.className = 'dfx-caption'; c.innerHTML = cap; rb.appendChild(c); }
    if (fc) {
      const f = document.createElement('div'); f.className = 'dfx-fore'; f.innerHTML = fc;
      rb.appendChild(f);
      tl.anim(f, [{ opacity: 0 }, { opacity: 1 }], t + 0.3, 0.3);
    }
  }
  function safe(fn) { try { return fn(); } catch (e) { warnOnce(e); return null; } }
  function appendChips(ctx, rb, chips, at, cu, small) {
    const { tl } = ctx;
    const box = document.createElement('div'); box.className = 'dfx-chips';
    rb.appendChild(box);
    for (const c of chips.slice(0, 4)) {
      const el = document.createElement('span');
      el.className = 'dfx-chip' + (c.cost || small || c.small ? ' is-small' : '');
      const ic = c.icon && SG.UIIcons ? SG.UIIcons.svg(c.icon, 20) : '';
      const hasFT = typeof c.from === 'number' && typeof c.to === 'number';
      const d = hasFT ? c.to - c.from : c.delta;
      el.innerHTML = ic + '<span>' + esc(c.label || '') + '</span>' + (hasFT ? '<span class="dfx-from">' + esc(c.fmt ? c.fmt(c.from) : fmt(c.from)) + '</span><span>→</span><b></b>' : '<b></b>') +
        (c.text !== undefined ? '' : (typeof d === 'number' && (hasFT || c.delta !== undefined) ? '<i class="' + (c.cost || d < 0 ? 'is-cost' : '') + (c.amber ? ' is-amber' : '') + '"></i>' : ''));
      box.appendChild(el);
      const b = el.querySelector('b'), i = el.querySelector('i');
      if (c.text !== undefined) { b.textContent = c.text; continue; }
      if (hasFT) tl.count(b, c.from, c.to, at, cu, { fmt: c.fmt, sound: !c.cost && !c.quiet });
      else if (typeof c.delta === 'number') { b.className = c.cost || c.delta < 0 ? 'is-cost' : 'is-gain'; tl.count(b, 0, c.delta, at, cu, { fmt: v => (c.fmt ? c.fmt(v) : signed(v)), sound: !c.cost && !c.quiet }); }
      if (i && typeof d === 'number') { i.textContent = c.fmt && !hasFT ? '' : signed(d); if (!hasFT) i.remove(); }
    }
  }

  // ---- 好：一小节的光扫、一次粒子、「佳」圆印 ----
  function flourish(ctx) {
    const { tl, stage } = ctx;
    const t = tl.time() + 0.15;
    const hud = stage.hud;
    if (stage.light) tl.anim(stage.light, [{ opacity: 0, transform: 'translateX(-60%)' }, { opacity: 1, offset: 0.4 }, { opacity: 0, transform: 'translateX(60%)' }], t, 0.9, { ease: 'ease-in-out' });
    if (ctx.P && !reduced()) tl.at(t + 0.1, () => ctx.P.burst(CFG.card.w * 0.62, CFG.card.h * 0.5, ctx.goodPreset || 'gold', CFG.goodBurst, { w: 220, h: 60 }));
    tl.at(t + 0.35, () => {
      if (!ctx.ribbon || !hud) return;
      const s = document.createElement('div'); s.className = 'dfx-good'; s.textContent = '佳';
      hud.appendChild(s);
      tl.anim(s, [{ transform: 'scale(1.4)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], tl.time(), CFG.sealGood, { ease: 'cubic-bezier(.3,1.6,.5,1)' });
      if (!reduced()) tl.anim(ctx.ribbon, [{ transform: 'translate(-50%, 0)' }, { transform: 'translate(calc(-50% + 2px), 1px)' }, { transform: 'translate(-50%, 0)' }], tl.time() + CFG.sealGood, 0.1);
      sfx('fx-good', 0.7);
    });
  }
  // ---- 大成：竖幅、朱印、暖光、粒子、墨晕 ----
  function spectacle(ctx) {
    const { tl, stage, ti, res, script } = ctx;
    const t = tl.time() + 0.1;
    const hud = stage.hud;
    const bannerText = ctx.data ? ctx.data.banner : null;
    if (bannerText && hud) {
      const bn = document.createElement('div'); bn.className = 'dfx-banner'; bn.innerHTML = '<span>' + esc(bannerText) + '</span>';
      // 竖幅一行写完：字多时缩小字号（216 px 高的书写区）
      const n = Array.from(String(bannerText)).length;
      if (n > 6) { bn.firstChild.style.fontSize = Math.max(16, Math.floor(196 / n)) + 'px'; bn.firstChild.style.letterSpacing = '0'; }
      hud.appendChild(bn);
      tl.anim(bn, [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }], t, CFG.bannerUnroll, { ease: 'cubic-bezier(.2,.8,.2,1)' });
      tl.at(ctx.settle, () => tl.anim(bn, [{ transform: 'scaleY(1)', opacity: 1 }, { transform: 'scaleY(0.05)', opacity: 0.2 }], tl.time(), 0.3, { fill: 'forwards' }));
    }
    if (stage.warm && !noFlash()) tl.anim(stage.warm, [{ opacity: 0 }, { opacity: 0.6, offset: 0.35 }, { opacity: 0.15 }], t, CFG.lightPulse, { ease: 'ease-out' });
    if (stage.vig) tl.anim(stage.vig, [{ opacity: 0 }, { opacity: 1 }], t, 0.4);
    tl.at(t + 0.3, () => {
      if (!hud) return;
      const s = document.createElement('div'); s.className = 'dfx-seal'; s.textContent = ti.stamp || '大成';
      hud.appendChild(s);
      const red = reduced();
      if (!red) tl.anim(s, [{ transform: 'scale(1.8) rotate(-8deg)', opacity: 0 }, { transform: 'scale(1) rotate(-4deg)', opacity: 1 }], tl.time(), CFG.sealGreat, { ease: 'cubic-bezier(.3,1.6,.5,1)' });
      else s.style.transform = 'rotate(-4deg)';
      if (ctx.P && !red) ctx.P.burst(CFG.card.w - 58, 112, 'seal', 14);
      if (!red && !noFlash() && stage.card) tl.anim(stage.card, [{ transform: stage.cardTransform() }, { transform: stage.cardTransform(CFG.shakePx, -CFG.shakePx * 0.6) }, { transform: stage.cardTransform(-CFG.shakePx * 0.6, CFG.shakePx * 0.4) }, { transform: stage.cardTransform() }], tl.time() + CFG.sealGreat, CFG.shakeSec, { fill: 'none' });
      sfx('fx-seal', 0.9);
      if (!ctx.sting) sfx('fx-great', 0.8);
    });
    if (ctx.P && !reduced()) tl.at(t + 0.5, () => ctx.P.burst(CFG.card.w * 0.62, CFG.card.h * 0.28, ctx.greatPreset || 'gold', CFG.greatBurst, { w: 400, h: 50 }));
  }
  // ---- 失手：去色 0.6 秒、灰色结果条（「未果」与下一步的建议）----
  function missFx(ctx) {
    const { tl, stage } = ctx;
    if (stage.card) tl.anim(stage.card, [{ filter: 'saturate(1)' }, { filter: 'saturate(.8) brightness(.95)', offset: 0.3 }, { filter: 'saturate(1)' }], tl.time(), 0.6, { fill: 'none' });
    if (!ctx.sting) sfx('fx-miss', 0.7);
  }

  // ================================================================ 牌面舞台 ==
  const SVGNS = 'http://www.w3.org/2000/svg';
  function cardStage(ctx) {
    const { root, res, id, tier, tl, script } = ctx;
    const C = CFG.card, S = CFG.scene;
    const card = document.createElement('div');
    card.className = 'dfx-card';
    const ac = id === 'move' ? ctx.fc : SG.CmdIcons ? SG.CmdIcons.accent(id) : '#4f9a76';
    card.style.setProperty('--dfx-ac', ac);
    const title = (script.title ? safe(() => script.title(res, ctx)) : null) || (SG.CmdIcons ? (id.indexOf('.') > 0 ? SG.CmdIcons.label(id.split('.')[0]) + '·' + SG.CmdIcons.label(id) : SG.CmdIcons.label(id)) : id);
    const sub = script.sub ? safe(() => script.sub(res, ctx)) : defaultSub(ctx);
    card.innerHTML = '<div class="dfx-head"><div class="dfx-title">' + esc(title) + '</div><div class="dfx-sub">' + esc(sub || '') + '</div></div>' +
      '<div class="dfx-iconbox"></div><div class="dfx-light"></div><div class="dfx-warm"></div><div class="dfx-vig"></div>' +
      '<div class="dfx-skiphint">点击跳过</div>';
    const svg = document.createElementNS(SVGNS, 'svg');
    svg.setAttribute('class', 'dfx-scene');
    svg.setAttribute('viewBox', '0 0 ' + S.w + ' ' + S.h);
    card.appendChild(svg);
    const actors = document.createElement('div'); actors.className = 'dfx-actors';
    card.appendChild(actors);
    const cv = document.createElement('canvas'); cv.className = 'dfx-pcv';
    const dpr = Math.min(2, W.devicePixelRatio || 1);
    cv.width = Math.round(C.w * dpr); cv.height = Math.round(C.h * dpr);
    card.appendChild(cv);
    root.appendChild(card);
    // 比例：桌面不超过 1.35 倍，手机 92 % 宽
    const vw = W.innerWidth || 1280, vh = W.innerHeight || 720;
    const phone = vh < 520 || vw < 700;
    if (phone) root.classList.add('is-phone');
    const k = Math.min(C.maxScale, (vw * (phone ? C.phoneW : 0.8)) / C.w, (vh * (phone ? 0.9 : 0.8)) / C.h);
    const tf = (dx, dy) => 'scale(' + k.toFixed(4) + ')' + (dx || dy ? ' translate(' + (dx || 0) + 'px,' + (dy || 0) + 'px)' : '');
    card.style.transform = tf();
    if (id === 'discord') card.classList.add('is-night');
    const iconBox = card.querySelector('.dfx-iconbox');
    const icon = SG.CmdIcons ? SG.CmdIcons.card(id, { size: 160, fc: ctx.fc }) : null;
    if (icon) iconBox.appendChild(icon);
    ctx.P = new Particles(cv, dpr, ctx.rng);
    const st = {
      el: card, card, hud: card, light: card.querySelector('.dfx-light'), warm: card.querySelector('.dfx-warm'), vig: card.querySelector('.dfx-vig'),
      cardTransform: tf,
      open() {
        tl.anim(card, [{ transform: tf() + ' translateY(18px)', opacity: 0 }, { transform: tf(), opacity: 1 }], 0, 0.25, { ease: 'cubic-bezier(.2,.8,.2,1)' });
        const sc = makeScene(ctx, svg, actors, card, k);
        ctx.sc = sc;
        if (script.card) { try { script.card(sc); } catch (e) { warnOnce(e); } }
      },
      beat(name, info) {
        if (!icon || !SG.CmdIcons) return;
        if (name === 'cue') SG.CmdIcons.beat(icon, 'cue');
        else if (name === 'action') SG.CmdIcons.beat(icon, 'action', { a: Math.max(0.3, (info.a1 || 1) - (info.a0 || 0)) });
        else if (name === 'reveal') SG.CmdIcons.beat(icon, 'reveal');
        else if (name === 'great') icon.classList.add('is-great');
      },
      skip() { if (icon) { icon.classList.remove('is-cue', 'is-action'); } },
      close() { tl.anim(card, [{ transform: tf(), opacity: 1 }, { transform: tf() + ' translateY(10px)', opacity: 0 }], tl.time(), CFG.close, { fill: 'forwards' }); },
    };
    return st;
  }
  STAGES.card = cardStage;
  function defaultSub(ctx) {
    const c = ctx.city, gn = ctx.gen;
    return [c ? c.name : null, gn ? gn.name : null].filter(Boolean).join(' · ');
  }

  // ================================================================ 场景工具 ==
  // sc：牌面上的一幕。坐标是场景的 420 × 180（地平线 y ≈ 150）；act(u) / tail(u) 把 0–1 映射到动作段 / 揭示之后
  function makeScene(ctx, svg, actors, card, k) {
    const { tl, rng } = ctx;
    const S = CFG.scene;
    const a0 = ctx.a0, a1 = ctx.a1, R = ctx.R, st = ctx.settle;
    const sc = {
      ctx, res: ctx.res, tier: ctx.tier, id: ctx.id, fast: ctx.fast, T: ctx.T, R, a0, a1, settle: st, rng, g: ctx.g, city: ctx.city, gen: ctx.gen, fc: ctx.fc,
      great: ctx.tier === 'great', good: ctx.tier === 'good', miss: ctx.tier === 'miss', svg, actors, card,
      act: u => a0 + clamp(u, 0, 1) * (a1 - a0),
      tail: u => R + clamp(u, 0, 1) * Math.max(0.2, st - R),
      add(markup, parent) {
        const g = document.createElementNS(SVGNS, 'g');
        g.innerHTML = markup;
        (parent || svg).appendChild(g);
        return g;
      },
      anim(el, kf, at, dur, o) { return tl.anim(el, kf, at, dur, o); },
      at(t, fn) { tl.at(t, fn); },
      count(el, from, to, at, dur, o) { return tl.count(el, from, to, at, dur, o); },
      // 场景坐标 → 牌面坐标的粒子
      burst(x, y, preset, n, at, o) {
        const fire = () => { if (ctx.P && !reduced()) ctx.P.burst(S.x + x, S.y + y, preset, n, o); };
        if (at === undefined) fire(); else tl.at(at, fire);
      },
      // 出现：缩放 0 → 1.15 → 1
      pop(el, at, dur) { return tl.anim(el, [{ transform: 'scale(0)', opacity: 0 }, { transform: 'scale(1.15)', opacity: 1, offset: 0.7 }, { transform: 'scale(1)', opacity: 1 }], at, dur || 0.2, { ease: 'ease-out' }); },
      fadeIn(el, at, dur) { return tl.anim(el, [{ opacity: 0 }, { opacity: 1 }], at, dur || 0.25); },
      fadeOut(el, at, dur) { return tl.anim(el, [{ opacity: 1 }, { opacity: 0 }], at, dur || 0.25); },
      move(el, from, to, at, dur, o) { return tl.anim(el, [{ transform: 'translate(' + from[0] + 'px,' + from[1] + 'px)' }, { transform: 'translate(' + to[0] + 'px,' + to[1] + 'px)' }], at, dur, o); },
      text(x, y, s, size, col, o) {
        o = o || {};
        return sc.add('<text x="' + x + '" y="' + y + '" font-size="' + (size || 16) + '" text-anchor="' + (o.anchor || 'middle') + '" fill="' + (col || '#241a21') + '"' +
          (o.weight ? ' font-weight="' + o.weight + '"' : '') + ' font-family="STKaiti,Kaiti SC,KaiTi,Noto Serif SC,serif"' + (o.stroke ? ' stroke="' + o.stroke + '" stroke-width="' + (o.sw || 3) + '" paint-order="stroke"' : '') + '>' + esc(s) + '</text>', o.parent);
      },
      // 界面图标当道具（金、粮、兵、城……）
      prop(icon, x, y, size, parent) {
        const s = SG.UIIcons ? SG.UIIcons.svg(icon, size || 32) : '';
        return sc.add(s.replace('<svg ', '<svg x="' + (x - (size || 32) / 2) + '" y="' + (y - (size || 32) / 2) + '" '), parent);
      },
      cmdIcon(icon, x, y, size, parent) {
        const s = SG.CmdIcons ? SG.CmdIcons.svg(icon, { size: size || 40, fc: ctx.fc }) : '';
        return sc.add(s.replace('<svg ', '<svg x="' + (x - (size || 40) / 2) + '" y="' + (y - (size || 40) / 2) + '" '), parent);
      },
      // 武将头像（DOM；SG.Portrait 不在时画一个带姓的圆牌）
      portrait(gen, o) {
        o = o || {};
        const size = o.size || 72;
        const box = document.createElement('div');
        box.className = 'dfx-actor';
        box.style.left = (o.x - size / 2) + 'px'; box.style.top = (o.y - size) + 'px'; box.style.width = size + 'px'; box.style.height = size + 'px';
        let p = null;
        if (gen && SG.Portrait && SG.Portrait.el) { try { p = SG.Portrait.el(gen, { size, mood: o.mood, color: o.color, flip: !!o.flip }); } catch (e) { p = null; } }
        if (!p) {
          p = document.createElement('div');
          p.className = 'sg-portrait';
          p.style.cssText = 'width:' + size + 'px;height:' + size + 'px;display:grid;place-items:center;background:' + (o.color || '#4a3229') + ';color:#f3e7cc;font:' + Math.round(size * 0.45) + 'px/1 STKaiti,KaiTi,serif';
          p.textContent = gen && gen.name ? gen.name.charAt(0) : '?';
        }
        box.appendChild(p);
        if (o.plate && gen) { const pl = document.createElement('div'); pl.className = 'dfx-plate'; pl.textContent = gen.name; box.appendChild(pl); }
        actors.appendChild(box);
        box._p = p;
        return box;
      },
      tag(text, x, y) {
        const t = document.createElement('div'); t.className = 'dfx-tag'; t.textContent = text;
        t.style.left = x + 'px'; t.style.top = y + 'px';
        actors.appendChild(t);
        return t;
      },
    };
    return sc;
  }

  // ================================================================ 小画（墨线、平涂分面）==
  const INK = '#241a21', PAPER = '#f3e7cc';
  const D = {
    ground(y, col, h) { return '<path d="M-10 ' + y + ' Q105 ' + (y - 6) + ' 210 ' + y + ' T430 ' + y + ' V' + (y + (h || 40)) + ' H-10Z" fill="' + (col || '#d9c08f') + '" opacity=".9"/>'; },
    furrows(y) { let s = ''; for (let i = 0; i < 3; i++) s += '<path d="M-10 ' + (y + i * 11) + ' Q210 ' + (y + i * 11 - 7) + ' 430 ' + (y + i * 11) + '" stroke="#a0703f" stroke-width="3" fill="none" opacity=".55"/>'; return s; },
    seedling(x, y, col) { return '<g class="vb" style="transform-origin:' + x + 'px ' + y + 'px"><path d="M' + x + ' ' + y + ' v-16" stroke="#2f6b45" stroke-width="2.4"/><path d="M' + x + ' ' + (y - 9) + ' q-10 -2 -12 -11 q10 0 12 9z" fill="' + (col || '#4f9a76') + '" stroke="' + INK + '" stroke-width="1.6"/><path d="M' + x + ' ' + (y - 12) + ' q9 -4 11 -12 q-9 1 -11 10z" fill="' + (col === '#e8c25a' ? '#f3d98a' : '#8fd4a8') + '" stroke="' + INK + '" stroke-width="1.6"/></g>'; },
    tile(x, y) { return '<rect x="' + (x - 17) + '" y="' + (y - 4) + '" width="34" height="12" rx="3" fill="#7a5233" stroke="' + INK + '" stroke-width="1.4"/><path d="M' + (x - 13) + ' ' + (y + 1) + ' h26 M' + (x - 13) + ' ' + (y + 5) + ' h26" stroke="#5e3f22" stroke-width="1"/>'; },
    house(x, y, s, roof, lit) {
      s = s || 1; const w = 34 * s, h = 22 * s;
      return '<g><rect x="' + (x - w / 2) + '" y="' + (y - h) + '" width="' + w + '" height="' + h + '" fill="#eadfc4" stroke="' + INK + '" stroke-width="2"/>' +
        '<path d="M' + (x - w / 2 - 7 * s) + ' ' + (y - h + 2) + ' L' + (x - w / 2 + 4) + ' ' + (y - h - 12 * s) + ' H' + (x + w / 2 - 4) + ' L' + (x + w / 2 + 7 * s) + ' ' + (y - h + 2) + 'Z" fill="' + (roof || '#808da6') + '" stroke="' + INK + '" stroke-width="2"/>' +
        '<rect x="' + (x - 5 * s) + '" y="' + (y - 13 * s) + '" width="' + 10 * s + '" height="' + 13 * s + '" fill="#8a2418" stroke="' + INK + '" stroke-width="1.4"/>' +
        (lit ? '<circle class="lan" cx="' + (x + w / 2 - 3) + '" cy="' + (y - h + 8) + '" r="' + 4 * s + '" fill="#ffcf6e" stroke="' + INK + '" stroke-width="1.2"/>' : '') + '</g>';
    },
    storey(x, y, s) { s = s || 1; const w = 26 * s, h = 14 * s; return '<rect x="' + (x - w / 2) + '" y="' + (y - h) + '" width="' + w + '" height="' + h + '" fill="#f3e7cc" stroke="' + INK + '" stroke-width="1.8"/><path d="M' + (x - w / 2 - 5) + ' ' + (y - h + 2) + ' L' + x + ' ' + (y - h - 10 * s) + ' L' + (x + w / 2 + 5) + ' ' + (y - h + 2) + 'Z" fill="#a6b2c8" stroke="' + INK + '" stroke-width="1.8"/>'; },
    soldier(x, y, col, s) { s = s || 1; return '<g><circle cx="' + x + '" cy="' + (y - 17 * s) + '" r="' + 3.6 * s + '" fill="#e9c9a0" stroke="' + INK + '" stroke-width="1.2"/><path d="M' + (x - 5 * s) + ' ' + y + ' L' + (x - 4 * s) + ' ' + (y - 13 * s) + ' H' + (x + 4 * s) + ' L' + (x + 5 * s) + ' ' + y + 'Z" fill="' + (col || '#b89a68') + '" stroke="' + INK + '" stroke-width="1.3"/><path d="M' + (x + 6 * s) + ' ' + (y + 1) + ' V' + (y - 26 * s) + '" stroke="' + INK + '" stroke-width="1.4"/><path d="M' + (x + 6 * s) + ' ' + (y - 26 * s) + ' l-2 4 h4z" fill="#c9d2da" stroke="' + INK + '" stroke-width="1"/></g>'; },
    banner(x, y, col, ch, s) { s = s || 1; return '<g><path d="M' + x + ' ' + y + ' V' + (y - 60 * s) + '" stroke="' + INK + '" stroke-width="2.6"/><path d="M' + (x + 1) + ' ' + (y - 58 * s) + ' Q' + (x + 18 * s) + ' ' + (y - 62 * s) + ' ' + (x + 32 * s) + ' ' + (y - 55 * s) + ' Q' + (x + 26 * s) + ' ' + (y - 45 * s) + ' ' + (x + 31 * s) + ' ' + (y - 35 * s) + ' Q' + (x + 16 * s) + ' ' + (y - 38 * s) + ' ' + (x + 1) + ' ' + (y - 34 * s) + 'Z" fill="' + col + '" stroke="' + INK + '" stroke-width="1.8"/>' + (ch ? '<text x="' + (x + 15 * s) + '" y="' + (y - 40 * s) + '" font-size="' + 14 * s + '" text-anchor="middle" fill="#fff6e2" font-family="STKaiti,KaiTi,serif">' + esc(ch) + '</text>' : '') + '</g>'; },
    cart(x, y, load) { return '<g><rect x="' + (x - 22) + '" y="' + (y - 22) + '" width="40" height="13" rx="2" fill="#8c6a48" stroke="' + INK + '" stroke-width="1.8"/>' + (load ? '<g transform="translate(' + x + ',' + y + ')">' + load + '</g>' : '') + '<g class="wheel vb" style="transform-origin:' + (x - 4) + 'px ' + (y - 6) + 'px"><circle cx="' + (x - 4) + '" cy="' + (y - 6) + '" r="8" fill="#b08860" stroke="' + INK + '" stroke-width="1.8"/><path d="M' + (x - 12) + ' ' + (y - 6) + ' h16 M' + (x - 4) + ' ' + (y - 14) + ' v16" stroke="' + INK + '" stroke-width="1.2"/></g><path d="M' + (x + 18) + ' ' + (y - 16) + ' h14" stroke="' + INK + '" stroke-width="2"/>' + D.ox(x + 42, y) + '</g>'; },
    ox(x, y) { return '<g><ellipse cx="' + x + '" cy="' + (y - 13) + '" rx="12" ry="7" fill="#7a5a3c" stroke="' + INK + '" stroke-width="1.6"/><circle cx="' + (x + 12) + '" cy="' + (y - 17) + '" r="4.5" fill="#7a5a3c" stroke="' + INK + '" stroke-width="1.4"/><path d="M' + (x - 8) + ' ' + (y - 7) + ' v7 M' + (x - 3) + ' ' + (y - 7) + ' v7 M' + (x + 5) + ' ' + (y - 7) + ' v7 M' + (x + 9) + ' ' + (y - 7) + ' v7" stroke="' + INK + '" stroke-width="1.6"/><path d="M' + (x + 10) + ' ' + (y - 21) + ' l-2 -4 M' + (x + 14) + ' ' + (y - 21) + ' l2 -4" stroke="' + INK + '" stroke-width="1.3"/></g>'; },
    sack(x, y, s) { s = s || 1; return '<path d="M' + (x - 8 * s) + ' ' + (y - 2 * s) + ' Q' + (x - 10 * s) + ' ' + (y - 14 * s) + ' ' + (x - 4 * s) + ' ' + (y - 18 * s) + ' L' + x + ' ' + (y - 22 * s) + ' L' + (x + 4 * s) + ' ' + (y - 18 * s) + ' Q' + (x + 10 * s) + ' ' + (y - 14 * s) + ' ' + (x + 8 * s) + ' ' + (y - 2 * s) + ' Z" fill="#e2bd5c" stroke="' + INK + '" stroke-width="1.6"/><path d="M' + (x - 4 * s) + ' ' + (y - 17 * s) + ' h8" stroke="#c8382c" stroke-width="1.8"/>'; },
    coin(x, y, r) { r = r || 6; return '<circle cx="' + x + '" cy="' + y + '" r="' + r + '" fill="#e8b54a" stroke="' + INK + '" stroke-width="1.4"/><rect x="' + (x - r * 0.3) + '" y="' + (y - r * 0.3) + '" width="' + r * 0.6 + '" height="' + r * 0.6 + '" fill="#5e3f22"/>'; },
    ingot(x, y) { return '<path d="M' + (x - 12) + ' ' + y + ' L' + (x - 9) + ' ' + (y - 8) + ' H' + (x + 9) + ' L' + (x + 12) + ' ' + y + 'Z" fill="#8b98a6" stroke="' + INK + '" stroke-width="1.5"/><path d="M' + (x - 8) + ' ' + (y - 6) + ' h14" stroke="#eef2f5" stroke-width="1.2"/>'; },
    jar(x, y) { return '<path d="M' + (x - 9) + ' ' + y + ' Q' + (x - 15) + ' ' + (y - 14) + ' ' + (x - 7) + ' ' + (y - 22) + ' H' + (x + 7) + ' Q' + (x + 15) + ' ' + (y - 14) + ' ' + (x + 9) + ' ' + y + 'Z" fill="#a0703f" stroke="' + INK + '" stroke-width="1.8"/><rect x="' + (x - 8) + '" y="' + (y - 26) + '" width="16" height="5" rx="2" fill="#5e3f22" stroke="' + INK + '" stroke-width="1.4"/>'; },
    walker(x, y, hat) { return '<g><path d="M' + (x - 5) + ' ' + y + ' L' + (x - 3) + ' ' + (y - 16) + ' H' + (x + 3) + ' L' + (x + 5) + ' ' + y + '" fill="#3b4f7a" stroke="' + INK + '" stroke-width="1.5"/><circle cx="' + x + '" cy="' + (y - 20) + '" r="3.8" fill="#e9c9a0" stroke="' + INK + '" stroke-width="1.2"/>' + (hat !== false ? '<path d="M' + (x - 11) + ' ' + (y - 21) + ' L' + x + ' ' + (y - 29) + ' L' + (x + 11) + ' ' + (y - 21) + 'Z" fill="#c99a2e" stroke="' + INK + '" stroke-width="1.4"/>' : '') + '<path d="M' + (x + 7) + ' ' + (y + 1) + ' L' + (x + 10) + ' ' + (y - 22) + '" stroke="#8c6a48" stroke-width="1.8"/></g>'; },
    foot(x, y) { return '<ellipse cx="' + x + '" cy="' + y + '" rx="2.6" ry="1.6" fill="' + INK + '" opacity=".55"/>'; },
    cottage(x, y) { return '<g><rect x="' + (x - 22) + '" y="' + (y - 22) + '" width="44" height="22" fill="#d2b48a" stroke="' + INK + '" stroke-width="2"/><path d="M' + (x - 30) + ' ' + (y - 20) + ' Q' + x + ' ' + (y - 50) + ' ' + (x + 30) + ' ' + (y - 20) + 'Z" fill="#c9a24a" stroke="' + INK + '" stroke-width="2"/><circle cx="' + x + '" cy="' + (y - 12) + '" r="8" fill="#241a21"/>' +
      '<path d="M' + (x + 40) + ' ' + y + ' V' + (y - 50) + ' M' + (x + 48) + ' ' + y + ' V' + (y - 44) + ' M' + (x - 40) + ' ' + y + ' V' + (y - 46) + '" stroke="#4f9a76" stroke-width="3"/></g>'; },
    anvil(x, y) { return '<path d="M' + (x - 26) + ' ' + (y - 22) + ' H' + (x + 20) + ' Q' + (x + 30) + ' ' + (y - 22) + ' ' + (x + 32) + ' ' + (y - 17) + ' H' + (x + 12) + ' L' + (x + 8) + ' ' + (y - 10) + ' V' + (y - 4) + ' H' + (x + 14) + ' V' + y + ' H' + (x - 14) + ' V' + (y - 4) + ' H' + (x - 8) + ' V' + (y - 10) + ' L' + (x - 14) + ' ' + (y - 16) + ' H' + (x - 26) + 'Z" fill="#8b98a6" stroke="' + INK + '" stroke-width="2"/>'; },
    hammer(x, y) { return '<g class="vb" style="transform-origin:' + (x + 34) + 'px ' + (y + 6) + 'px"><path d="M' + x + ' ' + y + ' L' + (x + 34) + ' ' + (y + 6) + '" stroke="#8c6a48" stroke-width="4"/><rect x="' + (x - 8) + '" y="' + (y - 9) + '" width="14" height="18" rx="2" fill="#5e3f22" stroke="' + INK + '" stroke-width="1.6"/></g>'; },
    target(x, y) { return '<g class="vb" style="transform-origin:' + x + 'px ' + (y + 20) + 'px"><path d="M' + x + ' ' + (y + 20) + ' V' + (y + 30) + '" stroke="' + INK + '" stroke-width="2"/><circle cx="' + x + '" cy="' + y + '" r="17" fill="#e3d0a8" stroke="' + INK + '" stroke-width="1.8"/><circle cx="' + x + '" cy="' + y + '" r="10" fill="#a0703f" stroke="' + INK + '" stroke-width="1.4"/><circle cx="' + x + '" cy="' + y + '" r="4" fill="#c8382c"/></g>'; },
    arrow(x, y, len) { return '<path d="M' + x + ' ' + y + ' h' + (len || 26) + '" stroke="' + INK + '" stroke-width="1.6"/><path d="M' + (x + (len || 26)) + ' ' + (y - 3) + ' l5 3 l-5 3z" fill="' + INK + '"/>'; },
    gate(x, y, col) { return '<g><rect x="' + (x - 26) + '" y="' + (y - 44) + '" width="52" height="44" fill="#d9c08f" stroke="' + INK + '" stroke-width="2"/><path d="M' + (x - 10) + ' ' + y + ' V' + (y - 22) + ' Q' + x + ' ' + (y - 32) + ' ' + (x + 10) + ' ' + (y - 22) + ' V' + y + 'Z" fill="#241a21"/><path d="M' + (x - 34) + ' ' + (y - 42) + ' L' + x + ' ' + (y - 60) + ' L' + (x + 34) + ' ' + (y - 42) + 'Z" fill="#808da6" stroke="' + INK + '" stroke-width="2"/>' + (col ? '<path d="M' + (x + 22) + ' ' + (y - 58) + ' v-14 l12 4 l-12 4" fill="' + col + '" stroke="' + INK + '" stroke-width="1.2"/>' : '') + '</g>'; },
    mountains(y, col) { return '<path d="M-10 ' + y + ' L60 ' + (y - 70) + ' L110 ' + (y - 30) + ' L170 ' + (y - 96) + ' L240 ' + (y - 24) + ' L300 ' + (y - 80) + ' L360 ' + (y - 34) + ' L430 ' + (y - 60) + ' V' + y + 'Z" fill="' + (col || '#5f78a8') + '" stroke="' + INK + '" stroke-width="2" opacity=".85"/>'; },
    cloud(x, y, s) { s = s || 1; return '<path d="M' + (x - 24 * s) + ' ' + y + ' Q' + (x - 24 * s) + ' ' + (y - 12 * s) + ' ' + (x - 10 * s) + ' ' + (y - 10 * s) + ' Q' + (x - 4 * s) + ' ' + (y - 22 * s) + ' ' + (x + 10 * s) + ' ' + (y - 14 * s) + ' Q' + (x + 26 * s) + ' ' + (y - 14 * s) + ' ' + (x + 24 * s) + ' ' + y + 'Z" fill="#f3e7cc" stroke="' + INK + '" stroke-width="1.6" opacity=".95"/>'; },
    dial(x, y, r) { return '<g><path d="M' + (x - r) + ' ' + y + ' A' + r + ' ' + r + ' 0 0 1 ' + (x + r) + ' ' + y + '" stroke="#4b3b38" stroke-width="7" fill="none" opacity=".35"/><path class="dial-v" d="M' + (x - r) + ' ' + y + ' A' + r + ' ' + r + ' 0 0 1 ' + (x + r) + ' ' + y + '" stroke="#c8382c" stroke-width="7" fill="none" pathLength="100" stroke-dasharray="100 100"/><g class="dial-n vb" style="transform-origin:' + x + 'px ' + y + 'px"><path d="M' + x + ' ' + y + ' L' + x + ' ' + (y - r + 4) + '" stroke="' + INK + '" stroke-width="2.6"/></g><circle cx="' + x + '" cy="' + y + '" r="4" fill="' + INK + '"/></g>'; },
    camel(x, y) { return '<g><path d="M' + (x - 22) + ' ' + (y - 14) + ' Q' + (x - 22) + ' ' + (y - 26) + ' ' + (x - 12) + ' ' + (y - 26) + ' Q' + (x - 8) + ' ' + (y - 38) + ' ' + (x - 2) + ' ' + (y - 26) + ' Q' + (x + 2) + ' ' + (y - 36) + ' ' + (x + 8) + ' ' + (y - 26) + ' L' + (x + 14) + ' ' + (y - 26) + ' Q' + (x + 16) + ' ' + (y - 38) + ' ' + (x + 22) + ' ' + (y - 38) + ' L' + (x + 24) + ' ' + (y - 32) + ' L' + (x + 20) + ' ' + (y - 30) + ' L' + (x + 18) + ' ' + (y - 16) + 'Z" fill="#e2bd5c" stroke="' + INK + '" stroke-width="1.8"/><path d="M' + (x - 18) + ' ' + (y - 14) + ' v14 M' + (x - 10) + ' ' + (y - 14) + ' v14 M' + (x + 8) + ' ' + (y - 14) + ' v14 M' + (x + 16) + ' ' + (y - 16) + ' v16" stroke="' + INK + '" stroke-width="2"/><rect x="' + (x - 12) + '" y="' + (y - 32) + '" width="12" height="9" rx="2" fill="#3b4f7a" stroke="' + INK + '" stroke-width="1.2"/></g>'; },
    scaffold(x, y, w, h) { let s = ''; for (let i = 0; i <= 4; i++) s += '<path d="M' + (x - w / 2 + i * w / 4) + ' ' + y + ' V' + (y - h) + '" stroke="#c99a2e" stroke-width="2.6"/>'; s += '<path d="M' + (x - w / 2) + ' ' + (y - h / 2) + ' H' + (x + w / 2) + ' M' + (x - w / 2) + ' ' + (y - h + 4) + ' H' + (x + w / 2) + ' M' + (x - w / 2) + ' ' + y + ' L' + (x - w / 4) + ' ' + (y - h / 2) + ' M' + (x + w / 2) + ' ' + y + ' L' + (x + w / 4) + ' ' + (y - h / 2) + '" stroke="#c99a2e" stroke-width="2"/>'; return '<g>' + s + '</g>'; },
    hall(x, y, s) { s = s || 1; return '<g><rect x="' + (x - 46 * s) + '" y="' + (y - 34 * s) + '" width="' + 92 * s + '" height="' + 34 * s + '" fill="#eadfc4" stroke="' + INK + '" stroke-width="2"/><path d="M' + (x - 60 * s) + ' ' + (y - 32 * s) + ' Q' + x + ' ' + (y - 46 * s) + ' ' + (x + 60 * s) + ' ' + (y - 32 * s) + ' L' + (x + 44 * s) + ' ' + (y - 52 * s) + ' H' + (x - 44 * s) + 'Z" fill="#808da6" stroke="' + INK + '" stroke-width="2"/><path d="M' + (x - 30 * s) + ' ' + y + ' V' + (y - 34 * s) + ' M' + (x + 30 * s) + ' ' + y + ' V' + (y - 34 * s) + ' M' + (x - 10 * s) + ' ' + y + ' V' + (y - 34 * s) + ' M' + (x + 10 * s) + ' ' + y + ' V' + (y - 34 * s) + '" stroke="#c8382c" stroke-width="3"/><rect x="' + (x - 46 * s) + '" y="' + y + '" width="' + 92 * s + '" height="6" fill="#b89a68" stroke="' + INK + '" stroke-width="1.6"/></g>'; },
    lanterns(y, n, x0, x1) { let s = '<path d="M' + x0 + ' ' + (y - 6) + ' Q' + ((x0 + x1) / 2) + ' ' + (y + 6) + ' ' + x1 + ' ' + (y - 6) + '" stroke="' + INK + '" stroke-width="1.2" fill="none"/>'; for (let i = 0; i < n; i++) { const x = x0 + (i + 0.5) * (x1 - x0) / n; s += '<ellipse class="lan" cx="' + x + '" cy="' + (y + 6 - Math.abs(i + 0.5 - n / 2) * 1.5) + '" rx="5" ry="6.5" fill="#c8382c" stroke="' + INK + '" stroke-width="1.2"/>'; } return s; },
    ding(x, y) { return '<g><rect x="' + (x - 20) + '" y="' + (y - 28) + '" width="40" height="5" rx="2" fill="#d2a06a" stroke="' + INK + '" stroke-width="1.5"/><path d="M' + (x - 17) + ' ' + (y - 23) + ' H' + (x + 17) + ' Q' + (x + 17) + ' ' + (y - 6) + ' ' + x + ' ' + (y - 4) + ' Q' + (x - 17) + ' ' + (y - 6) + ' ' + (x - 17) + ' ' + (y - 23) + 'Z" fill="#a0703f" stroke="' + INK + '" stroke-width="1.8"/><path d="M' + (x - 12) + ' ' + (y - 5) + ' L' + (x - 16) + ' ' + y + ' M' + (x + 12) + ' ' + (y - 5) + ' L' + (x + 16) + ' ' + y + ' M' + x + ' ' + (y - 4) + ' V' + y + '" stroke="' + INK + '" stroke-width="2.4"/><path d="M' + (x - 14) + ' ' + (y - 28) + ' v-7 h5 v7 M' + (x + 9) + ' ' + (y - 28) + ' v-7 h5 v7" stroke="' + INK + '" stroke-width="2" fill="none"/></g>'; },
    scroll(x, y, w, h, col) { return '<g><rect x="' + (x - w / 2) + '" y="' + (y - h) + '" width="' + w + '" height="' + h + '" fill="' + (col || '#f8f0dc') + '" stroke="' + INK + '" stroke-width="1.8"/><rect x="' + (x - w / 2 - 6) + '" y="' + (y - h - 6) + '" width="' + (w + 12) + '" height="7" rx="3.5" fill="#8c6a48" stroke="' + INK + '" stroke-width="1.4"/></g>'; },
    stamp(x, y, s) { s = s || 1; return '<g><rect x="' + (x - 14 * s) + '" y="' + (y - 12 * s) + '" width="' + 28 * s + '" height="12' + '" rx="2" fill="#3a7fa0" stroke="' + INK + '" stroke-width="1.8"/><path d="M' + (x - 9 * s) + ' ' + (y - 12 * s) + ' Q' + (x - 9 * s) + ' ' + (y - 26 * s) + ' ' + x + ' ' + (y - 27 * s) + ' Q' + (x + 9 * s) + ' ' + (y - 26 * s) + ' ' + (x + 9 * s) + ' ' + (y - 12 * s) + 'Z" fill="#2f7f7a" stroke="' + INK + '" stroke-width="1.8"/><path d="M' + (x + 8 * s) + ' ' + (y - 20 * s) + ' q14 4 18 20" stroke="#4e8c86" stroke-width="3" fill="none"/></g>'; },
  };
  const surname = gn => (gn && gn.name ? gn.name.charAt(0) : '');
  // 缩小的演员仍站在地平线（y = 150）上
  const groundScale = k => '<g transform="translate(0,150) scale(' + k + ') translate(0,-150)">';
  const factionName = f => { const F = facOf(f); return F ? F.name : ''; };

  // ---- 公共的场景片段 ----
  // 一个演员从 x0 走到 x1（脚印 / 车轮）；返回 <g>
  function travel(sc, markup, x0, x1, y, t0, t1, o) {
    o = o || {};
    const g = sc.add('<g>' + markup + '</g>');
    const el = g.firstChild;
    el.style.transform = 'translate(' + x0 + 'px,0)';
    sc.anim(el, [{ transform: 'translate(' + x0 + 'px,0px)' }, { transform: 'translate(' + x1 + 'px,0px)' }], t0, Math.max(0.1, t1 - t0), { ease: o.ease || 'cubic-bezier(.35,.1,.4,1)' });
    if (o.bob) sc.anim(el.firstChild || el, [{ transform: 'translateY(0)' }, { transform: 'translateY(-2px)' }, { transform: 'translateY(0)' }], t0, 0.28, { iter: Math.max(1, Math.round((t1 - t0) / 0.28)) });
    const wheels = el.querySelectorAll('.wheel');
    wheels.forEach(w => sc.anim(w, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(' + (360 * (x1 - x0) / 50) + 'deg)' }], t0, Math.max(0.1, t1 - t0), { ease: o.ease || 'cubic-bezier(.35,.1,.4,1)' }));
    return g;
  }
  function stripLabels(sc, names, y) {
    const n = names.length;
    const xs = names.map((_, i) => n === 1 ? 210 : 40 + i * (340 / (n - 1)));
    sc.add('<path d="M' + xs[0] + ' ' + y + ' L' + xs[n - 1] + ' ' + y + '" stroke="#a0703f" stroke-width="5" stroke-dasharray="2 7" stroke-linecap="round" fill="none"/>');
    names.forEach((nm, i) => { sc.add('<circle cx="' + xs[i] + '" cy="' + y + '" r="6" fill="' + (i === n - 1 ? '#c8382c' : '#f3e7cc') + '" stroke="' + INK + '" stroke-width="1.8"/>'); sc.text(xs[i], y + 24, nm, 15, INK, { stroke: PAPER, sw: 3 }); });
    return xs;
  }
  function routeNames(res) {
    const g = G(), from = cityOf(res.city), to = cityOf(res.to);
    let names = [from ? from.name : '', to ? to.name : ''];
    try {
      const p = g && from && to ? g.routeBetween(from, to, from.owner) : null;
      if (p && p.length >= 2) names = p.map(i => g.cities[i].name);
    } catch (e) { /* 只有两端 */ }
    if (names.length > 5) names = [names[0], names[1], '…', names[names.length - 2], names[names.length - 1]];
    return names.filter((x, i) => x || i === 0);
  }
  // 两件东西从两侧相向而来，成功时在中间系上红绳；失手时退回
  function meet(sc, left, right, o) {
    o = o || {};
    const L = sc.add(left), Rr = sc.add(right);
    const ok = !sc.miss;
    sc.anim(L, [{ transform: 'translateX(-120px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.act(0), sc.act(0.8) - sc.act(0), { ease: 'cubic-bezier(.2,.8,.2,1)' });
    sc.anim(Rr, [{ transform: 'translateX(120px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.act(0.1), sc.act(0.9) - sc.act(0.1), { ease: 'cubic-bezier(.2,.8,.2,1)' });
    if (ok) {
      const knot = sc.add('<g><circle cx="210" cy="' + (o.y || 70) + '" r="9" fill="#c8382c" stroke="' + INK + '" stroke-width="2"/><path d="M204 ' + ((o.y || 70) + 7) + ' q-6 14 -2 26 M216 ' + ((o.y || 70) + 7) + ' q6 14 2 26" stroke="#c8382c" stroke-width="3.5" fill="none"/></g>');
      sc.pop(knot, sc.R, 0.3);
      sc.burst(210, o.y || 70, 'bell', 10, sc.R + 0.1);
    } else {
      sc.anim(L, [{ transform: 'translateX(0)' }, { transform: 'translateX(-70px) rotate(-6deg)', opacity: 0.5 }], sc.R, 0.6, { fill: 'forwards' });
      sc.anim(Rr, [{ transform: 'translateX(0)' }, { transform: 'translateX(70px)', opacity: 0.4 }], sc.R + 0.05, 0.6, { fill: 'forwards' });
    }
    return { L, R: Rr };
  }
  // 头像：灰框 → 势力色（成功时由下往上刷过）
  function recolor(sc, box, color, at) {
    const p = box && box._p;
    if (!p) return;
    p.style.filter = 'grayscale(1) brightness(.9)';
    sc.at(at, () => { p.style.transition = 'filter .5s ease, box-shadow .5s ease'; p.style.filter = 'none'; p.style.boxShadow = '0 0 0 3px ' + color + ', 0 0 0 5px #241a21, 0 4px 10px rgba(0,0,0,.35)'; });
  }
  function dialTo(sc, g, from, to, at, dur) {
    const v = g.querySelector('.dial-v'), n = g.querySelector('.dial-n');
    const a = x => -90 + 180 * clamp(x, 0, 100) / 100;
    if (v) { v.setAttribute('stroke-dasharray', clamp(from, 0, 100) + ' 100'); sc.at(at, () => sc.anim(v, [{ strokeDasharray: clamp(from, 0, 100) + ' 100' }, { strokeDasharray: clamp(to, 0, 100) + ' 100' }], at, dur)); }
    if (n) { n.style.transform = 'rotate(' + a(from) + 'deg)'; sc.anim(n, [{ transform: 'rotate(' + a(from) + 'deg)' }, { transform: 'rotate(' + a(to) + 'deg)' }], at, dur, { ease: 'cubic-bezier(.2,.8,.2,1)' }); }
  }
  // 训练「大成」：士兵的点排成武将的姓（画布取字形的点；字太复杂时排成势力的旗形）
  function glyphPoints(ch, n, w, h) {
    if (!hasDoc || !ch) return null;
    try {
      const cv = document.createElement('canvas'); cv.width = 64; cv.height = 64;
      const c = cv.getContext('2d');
      c.fillStyle = '#000'; c.font = '56px STKaiti, KaiTi, "Noto Serif SC", serif'; c.textAlign = 'center'; c.textBaseline = 'middle';
      c.fillText(ch, 32, 34);
      const d = c.getImageData(0, 0, 64, 64).data;
      const pts = [];
      for (let y = 0; y < 64; y += 2) for (let x = 0; x < 64; x += 2) if (d[(y * 64 + x) * 4 + 3] > 128) pts.push([x / 64 * w, y / 64 * h]);
      if (pts.length < 20) return null;
      const out = [];
      for (let i = 0; i < n; i++) out.push(pts[Math.floor(i * pts.length / n)]);
      return out;
    } catch (e) { return null; }
  }

  // ================================================================ 各条指令（§5）==
  const nm = x => (x && x.name ? x.name : '');
  const cityName = id => { const c = cityOf(id); return c ? c.name : ''; };
  const stageNameOf = (b, s) => (STAGE_NAMES[b] || [])[clamp(s | 0, 0, 4)] || '';

  // ---- 开发（三项共用的结果条）----
  function devChips(res) {
    const lbl = { land: '土地', industry: '产业', town: '町' }[res.kind] || '开发';
    const out = [{ icon: res.kind === 'land' ? 'food' : res.kind === 'industry' ? 'gold' : 'city', label: lbl, from: res.before, to: res.after }];
    const mods = res.mods || {};
    const ZH = { gear: '装备', title: '官职', project: '营建', order: '民心', pact: '盟约', events: '时令' };
    const parts = Object.keys(ZH).filter(k => typeof mods[k] === 'number' && Math.abs(mods[k]) >= 0.005).map(k => ZH[k] + ' ' + (mods[k] > 0 ? '+' : '') + Math.round(mods[k] * 100) + '%');
    out.push({ label: '金', delta: -(res.cost || (SG.Balance ? SG.Balance.DevelopCost : 50)), cost: true });
    if (parts.length) out.push({ label: parts.join('　'), text: '', small: true });
    return out;
  }
  function devStageBar(res) {
    const b = DEV_BLDG[res.kind] || 'farms';
    const t = stageThr(b);
    const stB = typeof res.stBefore === 'number' ? res.stBefore : t.filter(x => res.before >= x).length;
    const stA = typeof res.stAfter === 'number' ? res.stAfter : t.filter(x => res.after >= x).length;
    const lo = stB > 0 ? t[stB - 1] : 0, hi = stB < 4 ? t[stB] : (SG.Balance ? SG.Balance.StatMax : 999);
    const k = v => (v - lo) / Math.max(1, hi - lo);
    const nextNm = stB < 4 ? STAGE_ORD[stB + 1] + '「' + stageNameOf(b, stB + 1) + '」' : '「' + stageNameOf(b, 4) + '」';
    return { from: k(res.before), to: k(res.after), up: stA > stB, label: BLDG_ZH[b] + '　' + Math.round(res.after) + ' / ' + hi + ' → ' + nextNm,
      startLabel: BLDG_ZH[b] + '　' + Math.round(res.before) + ' / ' + hi + ' → ' + nextNm,
      upLabel: BLDG_ZH[b] + ' 升至' + STAGE_ORD[stA] + '「' + stageNameOf(b, stA) + '」' };
  }
  function devBanner(res) {
    const b = DEV_BLDG[res.kind] || 'farms';
    if (res.after >= (SG.Balance ? SG.Balance.StatMax : 999)) return { land: '沃野千里', industry: '百炼成钢', town: '万家灯火' }[res.kind] || '鼎盛';
    return BLDG_ZH[b] + '　' + stageNameOf(b, res.stAfter);
  }
  const gold1 = v => (Math.floor(v * 10) / 10).toFixed(1);
  define('develop.land', {
    anchor: 'farms', shot: { kind: 'bldg', pitch: 38, distK: 3.2, yawFrom: 'gate' }, stageBar: devStageBar, chips: devChips, banner: devBanner,
    forecast(res) {
      const d = res.after - res.before, m = G() ? G().month : 1;
      return (m >= 8 ? '明年秋收' : '秋收') + '预计 <b>+' + Math.floor(d * (SG.Balance ? SG.Balance.FoodPerLand : 2.2)) + '</b> 粮（7 月）';
    },
    voice: { start: 'cmd.develop.start', great: 'cmd.develop.great' },
    card(sc) {
      const res = sc.res, d = Math.max(1, res.after - res.before);
      const m = sc.g ? sc.g.month : 4, autumn = m >= 7 && m <= 9;
      sc.add(D.ground(118, '#d9c08f', 70) + D.furrows(132));
      const k = clamp(Math.round(d / 3), 3, 8);
      const plough = sc.add('<g>' + D.ox(0, 122) + '<path d="M-14 112 l-18 8" stroke="' + INK + '" stroke-width="2"/></g>');
      sc.anim(plough.firstChild, [{ transform: 'translateX(-30px)' }, { transform: 'translateX(440px)' }], sc.act(0), sc.act(1) - sc.act(0), { ease: 'linear' });
      for (let i = 0; i < k; i++) {
        const x = 40 + (i + 0.5) * 340 / k + (sc.rng() - 0.5) * 12, y = 140 + (i % 2) * 8;
        const tile = sc.add(D.tile(x, y));
        sc.fadeIn(tile, sc.act((i + 0.5) / (k + 1)), 0.12);
        const sd = sc.add(D.seedling(x, y - 2, autumn ? '#e8c25a' : '#4f9a76'));
        sc.pop(sd.firstChild, sc.act((i + 0.5) / (k + 1)) + 0.12 + i * 0.06, 0.22);
      }
      if (sc.good || sc.great) {
        for (let i = 0; i < 3; i++) {
          const b = sc.add('<path d="M0 0 q6 -6 12 0 q6 -6 12 0" stroke="#fff" stroke-width="2.4" fill="none"/>');
          sc.anim(b.firstChild, [{ transform: 'translate(' + (120 + i * 60) + 'px,130px) scale(.6)', opacity: 0 }, { transform: 'translate(' + (150 + i * 70) + 'px,40px) scale(1)', opacity: 1, offset: 0.5 }, { transform: 'translate(' + (230 + i * 50) + 'px,' + (20 + i * 8) + 'px) scale(.8)', opacity: 0 }], sc.tail(0.1) + i * 0.1, 1.4);
        }
      }
      if (sc.great) {
        const ch = sc.add('<path d="M-10 104 Q100 96 210 104 T430 102" stroke="#5f9fc8" stroke-width="6" fill="none" stroke-linecap="round" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100"/>');
        sc.anim(ch.firstChild, [{ strokeDashoffset: 100 }, { strokeDashoffset: 0 }], sc.tail(0.05), 0.8, { ease: 'ease-in-out' });
        sc.burst(30, 104, 'water', 30, sc.tail(0.2), { dir: 0, spread: 0.6 });
        sc.burst(210, 60, 'petal', 50, sc.tail(0.3), { w: 380, h: 40 });
      }
    },
  });
  define('develop.industry', {
    anchor: 'workshop', shot: { kind: 'close', pitch: 30 }, stageBar: devStageBar, chips: devChips, banner: devBanner,
    forecast(res) { return '每月金 <b>+' + gold1((res.after - res.before) * (SG.Balance ? SG.Balance.GoldPerIndustry : 0.12)) + '</b>'; },
    voice: { start: 'cmd.industry.start', great: 'cmd.industry.great' },
    card(sc) {
      const res = sc.res, d = Math.max(1, res.after - res.before);
      sc.add(D.ground(146, '#b89a68', 40));
      // 炉：门由暗转亮
      const fur = sc.add('<g><path d="M30 146 V70 Q30 54 52 54 Q74 54 74 70 V146Z" fill="#8a6a4a" stroke="' + INK + '" stroke-width="2.4"/><path d="M44 54 V22 H60 V54" fill="#6e5d53" stroke="' + INK + '" stroke-width="2"/><rect class="door" x="40" y="104" width="24" height="30" rx="10" fill="#3a2a24" stroke="' + INK + '" stroke-width="1.8"/></g>');
      const door = fur.querySelector('.door');
      sc.anim(door, [{ fill: '#3a2a24' }, { fill: '#ff9a3c' }], sc.act(0), 0.3);
      for (let i = 0; i < 4; i++) { const p = sc.add('<circle cx="52" cy="18" r="' + (6 + i * 2) + '" fill="#b7a690" opacity=".55"/>'); sc.anim(p.firstChild, [{ transform: 'translate(0,0)', opacity: 0.6 }, { transform: 'translate(' + (8 + i * 4) + 'px,-30px)', opacity: 0 }], sc.act(0) + i * 0.35, 1.2); }
      sc.add(D.anvil(200, 146));
      const ham = sc.add(D.hammer(176, 96));
      const strikes = 3;
      for (let i = 0; i < strikes; i++) {
        const t = sc.act((i + 0.6) / (strikes + 0.4));
        sc.anim(ham.firstChild, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(-38deg)', offset: 0.55 }, { transform: 'rotate(8deg)', offset: 0.85 }, { transform: 'rotate(0deg)' }], t - 0.22, 0.3, { fill: 'none' });
        sc.burst(196, 124, 'spark', 12, t);
      }
      const k = clamp(Math.round(d / 4), 2, 6);
      for (let i = 0; i < k; i++) {
        const x = 300 + (i % 3) * 26, y = 146 - Math.floor(i / 3) * 9;
        const ig = sc.add(D.ingot(x, y));
        sc.anim(ig.firstChild, [{ transform: 'translateX(60px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.act(0.5) + i * 0.12, 0.25);
        sc.anim(ig.lastChild, [{ transform: 'translateX(60px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.act(0.5) + i * 0.12, 0.25);
      }
      sc.ctx.goodPreset = 'spark';
      if (sc.great) {
        const wh = sc.add('<g class="vb" style="transform-origin:110px 112px"><circle cx="110" cy="112" r="28" fill="none" stroke="#8c6a48" stroke-width="5"/><path d="M82 112 H138 M110 84 V140 M90 92 L130 132 M130 92 L90 132" stroke="#8c6a48" stroke-width="3"/></g>');
        sc.pop(wh.firstChild, sc.tail(0.05), 0.4);
        sc.anim(wh.firstChild, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }], sc.tail(0.15), 1.6, { ease: 'linear', iter: 2 });
        sc.burst(110, 140, 'water', 40, sc.tail(0.2), { w: 50 });
        sc.ctx.greatPreset = 'spark';
      }
    },
  });
  define('develop.town', {
    anchor: 'houses', shot: { kind: 'bldg', pitch: 44 }, stageBar: devStageBar, chips: devChips, banner: devBanner,
    forecast(res) {
      const d = res.after - res.before;
      const def = Math.floor(res.after / 12) - Math.floor(res.before / 12);
      return '每月金 <b>+' + gold1(d * (SG.Balance ? SG.Balance.GoldPerTown : 0.05)) + '</b>' + (def > 0 ? '　城防 <b>+' + def + '</b>' : '') + '　人口增长加快';
    },
    voice: { start: 'cmd.town.start', great: 'cmd.town.great' },
    card(sc) {
      const res = sc.res, d = Math.max(1, res.after - res.before);
      sc.add(D.ground(146, '#cdb48a', 40) + '<path d="M-10 160 H430" stroke="#a0703f" stroke-width="10" opacity=".35"/>');
      const old = [40, 120, 200, 280, 360];
      const k = clamp(Math.round(d / 4), 2, 6);
      const lots = [80, 160, 240, 320, 20, 400].slice(0, k);
      const houses = [];
      for (const x of old) houses.push({ x, g: sc.add(D.house(x, 146, 0.9, '#808da6', true)) });
      lots.forEach((x, i) => {
        const g = sc.add(D.house(x, 150, 0.75, '#a6b2c8', true));
        const parts = g.firstChild.children;
        const t = sc.act(0.15 + i * 0.12);
        // 夯土台基 → 木架 → 屋顶落下（三下）
        sc.anim(parts[0], [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }], t, 0.25, { ease: 'ease-out' });
        sc.anim(parts[1], [{ transform: 'translateY(-30px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], t + 0.25, 0.25, { ease: 'cubic-bezier(.3,1.4,.5,1)' });
        sc.fadeIn(parts[2], t + 0.4, 0.15);
        if (parts[3]) sc.anim(parts[3], [{ fill: '#5a4a40' }, { fill: '#ffcf6e' }], t + 0.55, 0.2);
        sc.burst(x, 150, 'dust', 6, t + 0.5, { w: 20 });
        houses.push({ x, g, new: true });
      });
      const cart = sc.add('<g>' + D.cart(0, 168) + '</g>');
      sc.anim(cart.firstChild, [{ transform: 'translateX(-60px) scale(.6)' }, { transform: 'translateX(420px) scale(.6)' }], sc.act(0.2), 2.4, { ease: 'linear' });
      sc.ctx.goodPreset = 'confetti';
      if (sc.great) {
        houses.filter(h => !h.new).forEach((h, i) => { const s = sc.add(D.storey(h.x, 124, 0.9)); sc.anim(s, [{ transform: 'translateY(20px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.tail(0.05) + i * 0.12, 0.25); });
        const tw = sc.add('<g><rect x="196" y="40" width="28" height="64" fill="#eadfc4" stroke="' + INK + '" stroke-width="2"/><path d="M186 44 L210 22 L234 44Z" fill="#c8382c" stroke="' + INK + '" stroke-width="2"/><circle cx="210" cy="66" r="7" fill="#a0703f" stroke="' + INK + '" stroke-width="1.6"/></g>');
        sc.anim(tw, [{ transform: 'translateY(60px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.tail(0.3), 0.4, { ease: 'cubic-bezier(.3,1.4,.5,1)' });
        sc.ctx.greatPreset = 'confetti';
      }
    },
  });

  // ---- 征兵 ----
  const WAN = ['', '兵满万人', '兵逾两万', '兵逾三万', '兵逾四万', '兵逾五万', '兵逾六万', '兵逾七万', '兵逾八万', '兵逾九万', '兵满十万'];
  define('recruit', {
    anchor: 'barracks', shot: { kind: 'gate.in' },
    chips(res) {
      const out = [{ icon: 'troops', label: '兵', from: res.before, to: res.after }, { icon: 'gold', label: '金', delta: -(res.cost || 0), cost: true }, { label: '人口', delta: -(res.n || 0), cost: true, quiet: true }];
      return out;
    },
    forecast(res) {
      const need = Math.ceil((res.n || 0) / (SG.Balance ? SG.Balance.TroopsPerFood : 40));
      const dil = typeof res.trainingBefore === 'number' && res.trainingAfter < res.trainingBefore ? '　<span style="color:#b36b12">训练 ' + res.trainingBefore + ' → ' + res.trainingAfter + '</span>' : '';
      return '每月耗粮 <b>+' + need + '</b>' + dil;
    },
    banner(res) { const m = Math.floor((res.cityAfter || 0) / 10000); return (res.cityAfter || 0) > (res.cityBefore || 0) && Math.floor((res.cityBefore || 0) / 10000) < m ? WAN[clamp(m, 1, 10)] : nm(genOf(res.gen)) + '部　满员'; },
    voice: { start: 'cmd.recruit.start', great: 'cmd.recruit.great' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.gate(372, 150, sc.fc));
      const rows = clamp(Math.ceil((res.n || 500) / 500), 2, 8);
      for (let r = 0; r < rows; r++) {
        let mk = '';
        for (let j = 0; j < 4; j++) mk += D.soldier(0, 148 - j * 4, '#b89a68', 0.62 + j * 0.03);
        const col = sc.add('<g>' + mk + '</g>');
        const t0 = sc.act(r / (rows + 1) * 0.7), t1 = t0 + Math.max(0.5, sc.act(1) - sc.act(0.3));
        const x0 = -40 - r * 6, x1 = 300 - r * 30;
        sc.anim(col.firstChild, [{ transform: 'translate(' + x0 + 'px,0)' }, { transform: 'translate(' + x1 + 'px,0)' }], t0, t1 - t0, { ease: 'linear' });
        // 进营门时换上势力色的号衣
        sc.at(t1 - 0.05, () => { col.querySelectorAll('path[fill="#b89a68"]').forEach(p => { p.style.transition = 'fill .15s'; p.setAttribute('fill', sc.fc); }); });
      }
      const board = sc.add('<g><rect x="300" y="40" width="58" height="34" rx="3" fill="#8c6a48" stroke="' + INK + '" stroke-width="2"/></g>');
      const tx = sc.text(329, 63, '0', 17, '#fff6e2');
      sc.count(tx.firstChild, 0, res.n || 0, sc.act(0.2), Math.max(0.3, sc.act(1) - sc.act(0.2)), { sound: false, fmt: v => fmt(v) });
      sc.ctx.goodPreset = 'confetti';
      if (sc.good || sc.great) {
        const b = sc.add(D.banner(250, 150, sc.fc, surname(sc.gen), 0.9));
        sc.anim(b, [{ transform: 'translateX(-200px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.tail(0), 0.5);
      }
      if (sc.great) for (let i = 0; i < 6; i++) { const b = sc.add(D.banner(40 + i * 46, 120, i % 2 ? sc.fc : '#c8382c', '', 0.6)); sc.anim(b, [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }], sc.tail(0.1) + i * 0.08, 0.3); }
    },
  });

  // ---- 训练 ----
  define('train', {
    anchor: 'drill', shot: { kind: 'bldg', pitch: 40, orbit: 8 },
    chips(res) {
      const DT = SG.DomesticTiers;
      const rb = DT.trainRank(res.before), ra = DT.trainRank(res.after);
      return [{ icon: 'training', label: '训练', from: res.before, to: res.after }, { label: rb !== ra ? rb + ' → ' + ra : ra, text: '', small: true }];
    },
    forecast(res) {
      const nx = SG.DomesticTiers.nextRank(res.after);
      return nx ? '再练 <b>' + nx.need + '</b> 点升为「' + nx.name + '」' : '已是百战之师';
    },
    banner() { return '百战之师'; },
    voice: { start: 'cmd.train.start', great: 'cmd.train.great' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + '<rect x="12" y="96" width="44" height="54" fill="#b89a68" stroke="' + INK + '" stroke-width="2"/><path d="M8 98 H60" stroke="' + INK + '" stroke-width="3"/>');
      // 点将台上的武将
      const gen = sc.add(D.banner(34, 96, sc.fc, surname(sc.gen), 0.6));
      const rows = clamp(Math.ceil((res.troops || 1000) / 1000), 2, 6);
      const block = sc.add('<g></g>');
      const dots = [];
      let mk = '';
      for (let r = 0; r < rows; r++) for (let c = 0; c < 6; c++) { const x = 110 + c * 22 + (r % 2) * 6, y = 92 + r * 11; dots.push([x, y]); mk += D.soldier(x, y + 8, sc.fc, 0.5); }
      block.innerHTML = '<g>' + mk + '</g>';
      const B = block.firstChild;
      for (let i = 0; i < 2; i++) sc.anim(B, [{ transform: 'translateX(0)' }, { transform: 'translateX(7px)', offset: 0.4 }, { transform: 'translateX(0)' }], sc.act(0.2 + i * 0.4), 0.35, { fill: 'none' });
      sc.add(D.target(330, 104) + D.target(372, 112));
      // 训练度条
      const bar = sc.add('<g><rect x="96" y="160" width="160" height="8" rx="4" fill="rgba(36,26,33,.18)"/><rect class="v" x="96" y="160" width="' + (160 * res.before / 100) + '" height="8" rx="4" fill="#a7743a"/></g>');
      const v = bar.querySelector('.v');
      sc.anim(v, [{ width: (160 * res.before / 100) + 'px' }, { width: (160 * res.after / 100) + 'px' }], sc.act(0.3), Math.max(0.3, sc.act(1) - sc.act(0.3)));
      const rk = sc.text(290, 170, SG.DomesticTiers.trainRank(res.before), 15, INK);
      if (SG.DomesticTiers.trainRank(res.before) !== SG.DomesticTiers.trainRank(res.after)) sc.at(sc.R, () => { rk.firstChild.textContent = SG.DomesticTiers.trainRank(res.after); sc.pop(rk.firstChild, sc.R, 0.25); });
      if (sc.good || sc.great) {
        for (let i = 0; i < 5; i++) { const a = sc.add(D.arrow(0, 0, 22)); sc.anim(a, [{ transform: 'translate(180px,' + (80 + i * 6) + 'px)', opacity: 1 }, { transform: 'translate(' + (305 + (i % 2) * 40) + 'px,' + (100 + (i % 3) * 4) + 'px)', opacity: 1 }], sc.tail(0.05) + i * 0.05, 0.35, { ease: 'cubic-bezier(.4,0,.8,.6)' }); }
      }
      if (sc.great) {
        // 方阵变成姓（画布粒子排字）
        const pts = glyphPoints(surname(sc.gen), 220, 130, 130);
        sc.fadeOut(B, sc.tail(0.05), 0.3);
        sc.at(sc.tail(0.08), () => {
          const P = sc.ctx.P;
          if (!P || reduced()) return;
          const S = CFG.scene;
          const tgt = pts || dots.map(d => [d[0] - 110, d[1] - 60]);
          for (let i = 0; i < tgt.length && P.list.length < CFG.particlesMax; i++) {
            const src = dots[i % dots.length];
            const tx = S.x + 145 + tgt[i][0], ty = S.y + 4 + tgt[i][1];
            P.list.push({ x: S.x + src[0], y: S.y + src[1], vx: (tx - S.x - src[0]) / 0.8, vy: (ty - S.y - src[1]) / 0.8, age: 0, life: 2.2, s: 2.2, col: sc.fc, P: { g: 0, drag: 0, shape: 'dot', stopAt: 0.8 }, rot: 0, vr: 0 });
          }
          P.spawned += tgt.length;
        });
        sc.burst(210, 160, 'dust', 40, sc.tail(0.4), { w: 300 });
      }
    },
  });

  // ---- 搜索（唯一有分支的一幕）----
  define('search', {
    anchor: 'tavern', shot: { kind: 'bldg', pitch: 24 },
    title(res) { return res.outcome === 'talent' ? '搜索·得贤' : res.outcome === 'item' ? '搜索·宝现' : '搜索'; },
    chips(res) {
      if (res.outcome === 'gold') return [{ icon: 'gold', label: '金', delta: res.value }];
      if (res.outcome === 'food') return [{ icon: 'food', label: '粮', delta: res.value }];
      if (res.outcome === 'talent') { const f = genOf(res.found); return f ? [{ label: f.name, text: '', small: false }, { label: '武', text: String(f.war) }, { label: '智', text: String(f.intel) }, { label: '政', text: String(f.pol) }] : []; }
      if (res.outcome === 'item') { const it = itemOf(res.item); return [{ label: it ? it.name : '宝物', text: '' }]; }
      return [];
    },
    forecast(res) {
      if (res.outcome === 'talent') {
        const f = genOf(res.found), g = G();
        let p = null;
        try { const who = genOf(res.gen); if (f && who && g) p = SG.Commands.hireChance(f, who, g.player); } catch (e) { p = null; }
        return f ? '最高能力 <b>' + maxStat(f) + '</b>' + (p !== null ? '　可登用，成功率约 <b>' + Math.round(p * 100) + '%</b>' : '') : null;
      }
      if (res.outcome === 'item') { const it = itemOf(res.item); return it ? rarityZh(it.rarity) + (it.slot ? ' · ' + it.slot : '') : null; }
      const c = cityOf(res.city), g = G();
      if (res.outcome === 'food' && c && g) { const need = Math.max(1, Math.trunc(g.troopsIn(c) / (SG.Balance ? SG.Balance.TroopsPerFood : 40))); return '可支用约 <b>' + Math.max(1, Math.round(res.value / need)) + '</b> 月'; }
      if (res.outcome === 'gold') return '可再开发 <b>' + Math.floor(res.value / (SG.Balance ? SG.Balance.DevelopCost : 50)) + '</b> 次';
      return null;
    },
    miss() { return { title: '一无所获', hint: '人才往往藏于名城：多寻访几次，或换智力高的武将' }; },
    banner(res) { if (res.outcome === 'item') { const it = itemOf(res.item); return '宝现　' + (it ? it.name : ''); } const f = genOf(res.found); return '得遇贤才　' + nm(f); },
    voice: { start: 'cmd.search.start', none: 'cmd.search.none', talent: 'cmd.search.talent' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30));
      sc.add('<g><rect x="6" y="96" width="50" height="54" fill="#eadfc4" stroke="' + INK + '" stroke-width="2"/><path d="M0 98 L31 78 L62 98Z" fill="#808da6" stroke="' + INK + '" stroke-width="2"/><path d="M50 100 v26" stroke="' + INK + '" stroke-width="1.4"/><rect x="44" y="104" width="12" height="18" fill="#c8382c" stroke="' + INK + '" stroke-width="1"/></g>');
      const wk = travel(sc, D.walker(0, 150), 60, 250, 150, sc.act(0), sc.act(1), { bob: true });
      for (let i = 0; i < 7; i++) { const f = sc.add(D.foot(70 + i * 26, 154 + (i % 2) * 4)); sc.fadeIn(f, sc.act(i / 7), 0.15); }
      if (res.outcome === 'none') {
        sc.burst(260, 110, 'leaf', 30, sc.R, { dir: 0, spread: 0.8, w: 40, h: 60 });
        sc.anim(wk.firstChild, [{ transform: 'translate(250px,0) rotate(0deg)' }, { transform: 'translate(250px,0) rotate(-6deg)', offset: 0.3 }, { transform: 'translate(250px,0) rotate(6deg)', offset: 0.6 }, { transform: 'translate(250px,0)' }], sc.R + 0.1, 0.6, { fill: 'none' });
        if (SG.Sfx) sc.at(sc.R, () => sfx('fx-wind', 0.6));
      } else if (res.outcome === 'gold') {
        const j = sc.add(D.jar(300, 150));
        sc.pop(j, sc.R, 0.3);
        sc.burst(300, 124, 'coin', sc.good ? 40 : 12, sc.R + 0.15);
      } else if (res.outcome === 'food') {
        const n = sc.good ? 14 : 6;
        for (let i = 0; i < n; i++) { const s = sc.add(D.sack(280 + (i % 5) * 20, 150 - Math.floor(i / 5) * 14, 0.9)); sc.anim(s, [{ transform: 'translate(-30px,-40px) rotate(-30deg)', opacity: 0 }, { transform: 'translate(0,0) rotate(0)', opacity: 1 }], sc.R + i * 0.05, 0.3, { ease: 'cubic-bezier(.3,1.4,.5,1)' }); }
      } else if (res.outcome === 'talent') {
        const f = genOf(res.found);
        sc.add(D.cottage(330, 150));
        const p = sc.portrait(f, { x: 330, y: 120, size: 78, mood: 'win', plate: true });
        sc.anim(p, [{ transform: 'scale(.2) rotateY(90deg)', opacity: 0 }, { transform: 'scale(1) rotateY(0)', opacity: 1 }], sc.R + 0.1, 0.45, { ease: 'cubic-bezier(.3,1.4,.5,1)' });
        if (maxStat(f) >= 90) { const sh = sc.add('<path d="M300 -10 L360 -10 L350 150 L310 150Z" fill="#fff1c8" opacity=".35"/>'); sc.fadeIn(sh, sc.R + 0.2, 0.5); sc.burst(330, 40, 'crow', 1, sc.R + 0.5); }
      } else if (res.outcome === 'item') {
        const it = itemOf(res.item);
        const pil = sc.add('<rect x="310" y="-20" width="40" height="170" fill="url(#none)" style="fill:rgba(255,236,190,.45)"/>');
        sc.fadeIn(pil, sc.R, 0.4);
        const box = sc.add('<rect x="312" y="128" width="36" height="22" rx="3" fill="#2a2430" stroke="' + INK + '" stroke-width="2"/><rect x="310" y="122" width="40" height="8" rx="2" fill="#4a3a44" stroke="' + INK + '" stroke-width="1.6"/>');
        sc.pop(box, sc.R, 0.25);
        const ic = sc.prop(it && /弓|弩/.test(it.name || '') ? 'shield' : 'guandao', 330, 96, 44);
        sc.anim(ic, [{ transform: 'translateY(30px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.R + 0.2, 0.5);
      }
    },
  });
  function itemOf(id) { try { if (SG.Items && SG.Items.get && id !== null && id !== undefined) return SG.Items.get(id) || null; } catch (e) { /* 无装备 */ } return null; }
  function rarityZh(r) { try { const R = SG.Items && SG.Items.rarityInfo ? SG.Items.rarityInfo(r) : null; if (R && (R.zh || R.name)) return R.zh || R.name; } catch (e) { /* 忽略 */ } return { common: '普通', uncommon: '精良', rare: '稀有', epic: '史诗', legendary: '传说' }[r] || r || ''; }

  // ---- 登用 ----
  define('hire', {
    anchor: 'palace', shot: { kind: 'close' },
    chips(res) { const t = genOf(res.target); return t ? [{ label: t.name, text: '' }, { label: '武', text: String(t.war) }, { label: '智', text: String(t.intel) }, { label: '政', text: String(t.pol) }] : []; },
    forecast(res) {
      const t = genOf(res.target);
      let sp = null;
      try { const s = SG.Specials && SG.Specials.of ? SG.Specials.of(t) : null; sp = s && (s.name || (s[0] && s[0].name)); } catch (e) { sp = null; }
      return (typeof res.p === 'number' ? '成功率 ' + Math.round(res.p * 100) + '%' : '') + (t && typeof t.loyalty === 'number' && res.ok ? '　忠诚 <b>' + t.loyalty + '</b>' : '') + (sp ? '　必杀「' + esc(sp) + '」' : '');
    },
    miss(res) { const t = genOf(res.target); return { title: nm(t) + ' 婉拒', hint: '德高者更易得人：提高德或换智力高的使者' }; },
    banner(res) { return nm(genOf(res.target)) + '　归心'; },
    voice: { ask: 'cmd.hire.ask', yes: 'cmd.hire.yes', no: 'cmd.hire.no' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.hall(210, 146, 1.1));
      const a = sc.portrait(genOf(res.gen), { x: 90, y: 140, size: 70, plate: true });
      const t = sc.portrait(genOf(res.target), { x: 330, y: 140, size: 76, plate: true, flip: true });
      recolor(sc, t, sc.fc, sc.miss ? 99 : sc.R + 0.05);
      sc.anim(a, [{ transform: 'rotate(0)' }, { transform: 'rotate(10deg)', offset: 0.5 }, { transform: 'rotate(0)' }], sc.act(0.1), 0.5, { fill: 'none' });
      const scroll = sc.add('<g><rect x="0" y="0" width="26" height="9" rx="4.5" fill="#f3e7cc" stroke="' + INK + '" stroke-width="1.6"/><path d="M12 0 v9" stroke="#c8382c" stroke-width="2.4"/></g>');
      sc.anim(scroll.firstChild, [{ transform: 'translate(120px,100px)' }, { transform: 'translate(270px,96px)' }], sc.act(0.3), Math.max(0.3, sc.act(1) - sc.act(0.3)));
      if (sc.miss) {
        sc.anim(t._p || t, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(-1)' }], sc.R, 0.3, { fill: 'forwards' });
        sc.anim(scroll.firstChild, [{ transform: 'translate(270px,96px)' }, { transform: 'translate(120px,104px)' }], sc.R + 0.2, 0.5, { fill: 'forwards' });
        const door = sc.add('<rect x="420" y="70" width="80" height="80" fill="#5a3e2c" stroke="' + INK + '" stroke-width="2"/>');
        sc.anim(door, [{ transform: 'translateX(0)' }, { transform: 'translateX(-130px)' }], sc.R + 0.3, 0.4, { fill: 'forwards' });
      } else {
        sc.anim(t, [{ transform: 'rotate(0)' }, { transform: 'rotate(-10deg)', offset: 0.5 }, { transform: 'rotate(0)' }], sc.R, 0.5, { fill: 'none' });
        if (sc.good || sc.great) for (let i = 0; i < 4; i++) { const o = sc.add(D.soldier(150 + i * 30, 150, '#3b4f7a', 0.55)); sc.fadeIn(o, sc.tail(0.1) + i * 0.08, 0.2); }
        if (sc.great) {
          sc.at(sc.tail(0.1), () => { const p = t._p; if (p) p.style.boxShadow = '0 0 0 4px #e8b54a, 0 0 0 6px #241a21, 0 0 24px 6px rgba(248,220,142,.8)'; });
          const ln = sc.add(D.lanterns(40, 6, 120, 300));
          ln.querySelectorAll('.lan').forEach((l, i) => { l.setAttribute('fill', '#5a2a24'); sc.at(sc.tail(0.2) + i * 0.1, () => l.setAttribute('fill', '#ff8a4c')); });
        }
      }
    },
  });

  // ---- 移动 ----
  define('move', {
    anchor: 'gate.out', shot: { kind: 'gate.out' },
    chips(res) {
      const c = cityOf(res.to), g = G();
      if (!c || !g) return [{ label: '→ ' + cityName(res.to), text: '' }];
      const now2 = g.troopsIn(c);
      return [{ label: '→ ' + c.name + '　兵', from: Math.max(0, now2 - (res.troops || 0)), to: now2, icon: 'troops' }, { label: '武将', text: String(res.gens || 1), small: true }];
    },
    forecast(res) { return res.frontline ? '<span style="color:#a8371f">前线</span>：' + esc(cityName(res.to)) + ' 与敌接壤' : (res.hops > 1 ? '途经 ' + (res.hops - 1) + ' 城' : null); },
    banner(res) { return '大军星驰　' + cityName(res.to); },
    voice: { start: 'cmd.move.start' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#d9c08f', 30));
      const names = routeNames(res);
      const xs = stripLabels(sc, names, 128);
      const rows = clamp(Math.ceil((res.troops || 3000) / 3000), 1, 4) * clamp(res.gens || 1, 1, 5);
      const n = clamp(rows, 1, sc.great ? 10 : sc.good ? 7 : 4);
      for (let i = 0; i < n; i++) {
        const t0 = sc.act(0.05 + i * 0.05), t1 = sc.act(1) + i * 0.05;
        travel(sc, D.soldier(0, 120, sc.fc, 0.55), xs[0] - 10 - i * 14, xs[xs.length - 1] - 18 - i * 14, 120, t0, t1);
      }
      travel(sc, D.banner(0, 120, sc.fc, surname(sc.gen), 0.6), xs[0], xs[xs.length - 1] - 4, 120, sc.act(0), sc.act(1));
      if (sc.good || sc.great) { const sg = sc.add('<g><path d="M' + (xs[xs.length - 1] + 18) + ' 150 V104" stroke="' + INK + '" stroke-width="2.4"/><rect x="' + (xs[xs.length - 1] + 4) + '" y="96" width="40" height="16" fill="#8c6a48" stroke="' + INK + '" stroke-width="1.6"/></g>'); sc.pop(sg, sc.tail(0), 0.3); }
      if (sc.great) sc.burst(xs[0], 60, 'crow', 1, sc.tail(0.1), { dir: 0, spread: 0.1 });
    },
  });

  // ---- 输送 ----
  define('transport', {
    anchor: 'granary', shot: { kind: 'bldg' },
    chips(res) {
      const out = [];
      if (res.gold > 0) out.push({ icon: 'gold', label: '金', delta: -res.gold, cost: true });
      if (res.food > 0) out.push({ icon: 'food', label: '粮', delta: -res.food, cost: true });
      const need = Math.max(1, res.destNeed || 0);
      if (res.destNeed > 0) out.push({ label: cityName(res.to) + ' 粮可支', from: Math.floor(res.destFoodBefore / need), to: Math.floor((res.destFoodBefore + res.food) / need), fmt: v => fmt(v) + ' 月' });
      else out.push({ label: '→ ' + cityName(res.to), text: '' });
      return out;
    },
    forecast(res) { return '自 ' + esc(cityName(res.city)) + ' 送往 ' + esc(cityName(res.to)); },
    banner(res) { const need = res.destNeed || 0; const rescue = need > 0 && res.destFoodBefore < 2 * need && res.destFoodBefore + res.food >= 6 * need; return rescue ? '转输' + cityName(res.to) + '　解燃眉之急' : '辎重如山'; },
    voice: { start: 'cmd.transport.start' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#d9c08f', 30));
      const xs = stripLabels(sc, [cityName(res.city), cityName(res.to)], 128);
      const carts = clamp(Math.ceil((res.food || 0) / 2000 + (res.gold || 0) / 500), 1, 6);
      for (let i = 0; i < carts; i++) {
        const load = res.food > 0 && (i % 2 === 0 || !res.gold) ? D.sack(-8, -22, 0.8) + D.sack(6, -22, 0.8) : '<rect x="-14" y="-32" width="22" height="11" rx="2" fill="#5a1a12" stroke="' + INK + '" stroke-width="1.4"/>';
        const t0 = sc.act(i * 0.08), t1 = sc.act(1) + i * 0.05;
        travel(sc, groundScale(0.7) + D.cart(0, 150, load) + '</g>', xs[0] - i * 48, xs[1] - 70 - i * 48, 150, t0, t1);
      }
      if (sc.good || sc.great) for (let i = 0; i < 2; i++) travel(sc, D.soldier(0, 116, sc.fc, 0.55), xs[0] - 20 - i * 100, xs[1] - 40 - i * 100, 116, sc.act(0.05), sc.act(1));
      if (sc.great) { const inset = sc.prop('city', xs[1], 70, 46); sc.pop(inset, sc.tail(0.05), 0.4); sc.burst(xs[1], 80, 'confetti', 40, sc.tail(0.2)); }
    },
  });

  // ---- 外交·同盟 ----
  define('ally', {
    anchor: 'palace', shot: { kind: 'close' },
    title() { return '外交·同盟'; },
    sub(res) { return (cityName(res.city) || '') + ' → ' + factionName(res.target); },
    chips(res) {
      const out = [];
      if (res.gift > 0) out.push({ icon: 'gold', label: '礼金', delta: -res.gift, cost: true });
      if (typeof res.p === 'number') out.push({ label: '成功率', text: Math.round(res.p * 100) + '%', small: true });
      return out;
    },
    forecast(res) {
      const g = G();
      if (!res.ok || !g) return null;
      const n = res.months || (SG.Balance ? SG.Balance.AllianceMonths : 12);
      return '同盟至 <b>' + (g.year + Math.floor((g.month - 1 + n) / 12)) + '</b> 年 <b>' + ((g.month - 1 + n) % 12 + 1) + '</b> 月';
    },
    miss(res) { return { title: factionName(res.target) + ' 拒绝', hint: '礼金越厚、人望越高越易成功' }; },
    banner(res) { return '与' + factionName(res.target) + '结盟'; },
    voice: { yes: 'cmd.ally.yes', no: 'cmd.ally.no' },
    card(sc) {
      const res = sc.res, tc = facColor(res.target, '#8a5fc0');
      sc.add(D.ground(150, '#cdb48a', 30));
      meet(sc, D.banner(150, 150, sc.fc, factionName(res.f !== undefined ? res.f : playerF()).charAt(0), 1.1), D.banner(230, 150, tc, factionName(res.target).charAt(0), 1.1), { y: 92 });
      if (!sc.miss && (sc.good || sc.great)) {
        const r = rulerOf(res.target);
        if (r) { const p = sc.portrait(r, { x: 360, y: 172, size: 60, mood: 'win', color: tc, flip: true }); sc.anim(p, [{ opacity: 0, transform: 'translateY(10px)' }, { opacity: 1, transform: 'translateY(0)' }], sc.tail(0.05), 0.4); }
      }
      if (sc.great) {
        for (let i = 0; i < 4; i++) { const b = sc.add(D.banner(40 + i * 24, 150, i % 2 ? tc : sc.fc, '', 0.5)); sc.pop(b, sc.tail(0.1) + i * 0.06, 0.25); }
        const ln = sc.add(D.lanterns(26, 7, 60, 360)); sc.fadeIn(ln, sc.tail(0.2), 0.4);
      }
    },
  });

  // ---- 外交·离间（牌面，夜色）----
  define('discord', {
    stage: 'card',
    title() { return '外交·离间'; },
    sub(res) { const t = genOf(res.target); return t ? factionName(t.faction) + '·' + t.name : ''; },
    chips(res) { return res.ok ? [{ label: '忠诚', from: (res.loyaltyAfter || 0) + (res.d || 0), to: res.loyaltyAfter || 0, icon: 'loyalty' }] : []; },
    forecast(res) { return res.ok && res.loyaltyAfter < 40 ? '<span style="color:#a8371f">忠诚低于 40，可能出奔</span>' : null; },
    miss(res) { return { title: nm(genOf(res.target)) + ' 识破了离间之计', hint: '智力高于对方时更易成功' }; },
    banner(res) { return nm(genOf(res.target)) + '　离心'; },
    voice: { ok: 'cmd.discord.ok' },
    card(sc) {
      const res = sc.res, t = genOf(res.target), tc = t ? facColor(t.faction, '#8a5fc0') : '#8a5fc0';
      sc.add('<circle cx="380" cy="24" r="12" fill="#f8dc8e" opacity=".85"/>');
      sc.portrait(genOf(res.gen), { x: 46, y: 170, size: 52 });
      const tp = sc.portrait(t, { x: 250, y: 132, size: 92, color: tc, mood: sc.great ? 'angry' : undefined, plate: true });
      const dial = sc.add(D.dial(370, 140, 34));
      const before = (res.loyaltyAfter || 0) + (res.d || 0);
      dialTo(sc, dial, before, res.ok ? res.loyaltyAfter : before, sc.R, 0.7);
      const letter = sc.add('<g><rect x="0" y="0" width="30" height="20" fill="#f3e7cc" stroke="' + INK + '" stroke-width="1.6"/><path d="M0 0 L15 11 L30 0" stroke="' + INK + '" stroke-width="1.4" fill="none"/><circle cx="15" cy="13" r="3.5" fill="#c8382c"/></g>');
      sc.anim(letter.firstChild, [{ transform: 'translate(70px,140px)' }, { transform: 'translate(190px,110px)' }], sc.act(0.1), Math.max(0.3, sc.act(1) - sc.act(0.1)));
      if (sc.miss) {
        sc.anim(letter.firstChild, [{ transform: 'translate(190px,110px)' }, { transform: 'translate(180px,170px) rotate(40deg)', opacity: 0 }], sc.R, 0.5, { fill: 'forwards' });
        sc.burst(205, 120, 'leaf', 8, sc.R);
      } else {
        sc.fadeOut(letter, sc.R - 0.05, 0.2);
        const cr = sc.add('<path d="M218 40 L232 62 L224 78 L242 100 L236 124" stroke="#f3e7cc" stroke-width="2.4" fill="none" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100"/>' + (sc.good || sc.great ? '<path d="M232 62 L252 70 M242 100 L262 98" stroke="#f3e7cc" stroke-width="1.8" fill="none" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100"/>' : ''));
        cr.querySelectorAll('path').forEach((p, i) => sc.anim(p, [{ strokeDashoffset: 100 }, { strokeDashoffset: 0 }], sc.R + 0.1 + i * 0.15, 0.4));
        if (sc.great) { sc.anim(tp, [{ transform: 'translateX(0)' }, { transform: 'translateX(4px) rotate(2deg)' }], sc.tail(0.1), 0.3, { fill: 'forwards' }); sc.ctx.greatPreset = 'ink'; if (res.loyaltyAfter < 40) { const tg = sc.tag('可能出奔', 150, 18); sc.anim(tg, [{ opacity: 0.4 }, { opacity: 1 }], sc.tail(0.1), 0.5, { iter: 4, dir: 'alternate' }); } }
      }
    },
  });

  // ---- 外交·拉拢（牌面）----
  define('persuade', {
    stage: 'card',
    title() { return '外交·拉拢'; },
    sub(res) { const t = genOf(res.target); return t ? nm(t) : ''; },
    chips(res) {
      const t = genOf(res.target);
      if (!res.ok || !t) return [];
      return res.surrendered ? [{ icon: 'city', label: cityName(res.cityId) + ' 归入我方', text: '' }] : [{ icon: 'troops', label: t.name + ' 率兵', text: fmt(t.troops || 0) }];
    },
    forecast(res) { return res.ok ? (res.surrendered ? esc(cityName(res.cityId)) + ' 城归附' : esc(nm(genOf(res.target))) + ' 来投') : null; },
    miss(res) { return { title: nm(genOf(res.target)) + ' 不为所动', hint: '对方忠诚越低越易劝降；派智力高的使者' }; },
    banner(res) { return res.surrendered ? cityName(res.cityId) + '　献城' : nm(genOf(res.target)) + '　倒戈'; },
    voice: { yes: 'cmd.persuade.yes' },
    card(sc) {
      const res = sc.res, t = genOf(res.target), from = res.from !== undefined ? res.from : t ? t.faction : -1;
      const ec = facColor(from, '#8a5fc0');
      const g = G();
      const r = g ? rulerOf(g.player) : null;
      sc.add(D.ground(150, '#cdb48a', 30));
      sc.portrait(r, { x: 70, y: 140, size: 64, color: sc.fc });
      const tp = sc.portrait(t, { x: 260, y: 136, size: 88, color: ec, plate: true });
      const ing = sc.add('<path d="M0 10 Q0 0 8 2 Q12 -6 20 -4 Q28 -6 30 2 Q38 0 38 10Z" fill="#e8b54a" stroke="' + INK + '" stroke-width="1.6"/>');
      sc.anim(ing.firstChild, [{ transform: 'translate(96px,96px)' }, { transform: 'translate(176px,88px)' }], sc.act(0.2), Math.max(0.3, sc.act(1) - sc.act(0.2)));
      // 忠诚表：成功时指针从敌方的忠诚落到 0，再由我方的忠诚刷满（来投后的忠诚）
      const dial = sc.add(D.dial(372, 140, 30));
      const old = typeof res.loyaltyBefore === 'number' ? res.loyaltyBefore : t ? (res.ok ? 50 : t.loyalty) : 50;
      dialTo(sc, dial, old, res.ok ? (t ? t.loyalty : 70) : old, sc.R, 0.6);
      if (sc.miss) sc.anim(tp._p || tp, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(-1)' }], sc.R, 0.3, { fill: 'forwards' });
      else {
        sc.at(sc.R, () => { const p = tp._p; if (p) { p.style.transition = 'box-shadow .5s, filter .5s'; p.style.boxShadow = '0 0 0 3px ' + sc.fc + ', 0 0 0 5px #241a21'; } });
        sc.anim(tp, [{ transform: 'rotate(0)' }, { transform: 'rotate(-8deg)', offset: 0.5 }, { transform: 'rotate(0)' }], sc.R + 0.4, 0.5, { fill: 'none' });
        if (sc.great && res.surrendered) {
          const ct = sc.prop('city', 160, 44, 48); sc.pop(ct, sc.tail(0.05), 0.3);
          const fl = sc.add('<path d="M180 14 v-14 l14 5 l-14 5" fill="' + ec + '" stroke="' + INK + '" stroke-width="1.2"/>');
          sc.at(sc.tail(0.3), () => { const p = fl.firstChild; p.style.transition = 'fill .4s'; p.setAttribute('fill', sc.fc); });
        }
      }
    },
  });

  // ---- 出征 ----
  define('attack', {
    anchor: 'gate.out', shot: { kind: 'gate.out' },
    sub(res) { return (cityName(res.city) || '') + ' → ' + cityName(res.to); },
    chips(res) {
      const days = res.troops > 0 ? Math.floor((res.food || 0) / Math.max(1, Math.floor(res.troops / (SG.Balance ? SG.Balance.BattleFoodPerTroops : 250)))) : 0;
      return [{ icon: 'troops', label: '兵', text: fmt(res.troops || 0) }, { label: '武将', text: String(res.gens || 1), small: true }, { icon: 'food', label: '军粮', text: fmt(res.food || 0) + '（约 ' + days + ' 日）', small: true }];
    },
    forecast(res) { return '攻打 <b>' + esc(cityName(res.to)) + '</b>'; },
    banner(res) {
      const g = G();
      if (res.ruler && g) return rulerOf(g.player).name + '　亲征';
      const w = Math.floor((res.troops || 0) / 10000);
      return '大军' + (w >= 1 && w <= 10 ? NUM_ZH[w] + '万' : '') + '　出师';
    },
    voice: { start: 'cmd.attack.start' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.gate(380, 150, sc.fc));
      const n = clamp(Math.ceil((res.troops || 5000) / 4000), 3, sc.great ? 12 : 8);
      for (let i = 0; i < n; i++) { const s = sc.add(D.soldier(340 - i * 26, 150, sc.fc, 0.6)); sc.fadeIn(s, sc.act(i / n * 0.6), 0.2); }
      // 虎符两半合拢（动作段末拍）
      const L = sc.add('<path d="M150 70 Q150 44 180 42 L210 42 L210 92 L180 92 Q160 92 150 70Z" fill="#e8b54a" stroke="' + INK + '" stroke-width="2.4"/><path d="M168 58 l8 8 M180 52 v14" stroke="' + INK + '" stroke-width="2"/>');
      const Rr = sc.add('<path d="M210 42 L240 42 Q262 44 266 60 L270 72 Q262 92 240 92 L210 92Z" fill="#f8dc8e" stroke="' + INK + '" stroke-width="2.4"/><path d="M236 58 l-8 8 M246 54 v12" stroke="' + INK + '" stroke-width="2"/>');
      sc.anim(L, [{ transform: 'translateX(-90px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.act(0.1), Math.max(0.3, sc.act(0.95) - sc.act(0.1)), { ease: 'cubic-bezier(.6,0,.4,1)' });
      sc.anim(Rr, [{ transform: 'translateX(90px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], sc.act(0.1), Math.max(0.3, sc.act(0.95) - sc.act(0.1)), { ease: 'cubic-bezier(.6,0,.4,1)' });
      sc.burst(210, 66, 'spark', 16, sc.act(0.98));
      if (sc.good || sc.great) for (let i = 0; i < 4; i++) { const b = sc.add(D.banner(60 + i * 70, 150, i % 2 ? '#c8382c' : sc.fc, i === 0 ? surname(sc.gen) : '', 0.7)); sc.anim(b, [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }], sc.tail(0.05) + i * 0.07, 0.3); }
      if (sc.great) { sc.burst(80, 40, 'crow', 7, sc.tail(0.15), { dir: -0.6, spread: 0.6, w: 80 }); sc.burst(200, 150, 'dust', 40, sc.tail(0.1), { w: 360 }); sc.ctx.greatPreset = 'dust'; }
    },
  });

  // ---- 赏赐 ----
  define('reward', {
    anchor: 'palace', shot: { kind: 'close' },
    chips(res) {
      const out = [{ label: '忠诚', from: res.before, to: res.after, icon: 'loyalty' }];
      if (res.gift !== null && res.gift !== undefined) { const it = itemOf(res.gift); out.push({ label: '赐 ' + (it ? it.name : '宝物'), text: '', small: true }); }
      else out.push({ icon: 'gold', label: '金', delta: -(res.cost || (SG.Balance ? SG.Balance.RewardGold : 100)), cost: true });
      return out;
    },
    forecast(res) { return res.after >= 100 ? '<b>誓死效忠</b>' : '距满忠诚 ' + (100 - res.after); },
    banner(res) { return nm(genOf(res.gen)) + '　誓死效忠'; },
    voice: { thanks: 'cmd.reward.thanks' },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.hall(110, 146, 0.9));
      const p = sc.portrait(sc.gen, { x: 320, y: 132, size: 82, plate: true, mood: sc.great ? 'win' : undefined });
      const tray = sc.add('<g><ellipse cx="0" cy="0" rx="26" ry="8" fill="#c8382c" stroke="' + INK + '" stroke-width="1.8"/>' + D.coin(-8, -6, 5) + D.coin(4, -8, 5) + '<rect x="6" y="-14" width="14" height="8" rx="2" fill="#b48ae0" stroke="' + INK + '" stroke-width="1.2"/></g>');
      sc.anim(tray.firstChild, [{ transform: 'translate(140px,118px)' }, { transform: 'translate(250px,110px) rotate(-18deg)' }], sc.act(0.1), Math.max(0.3, sc.act(0.8) - sc.act(0.1)));
      sc.burst(270, 100, 'coin', 10, sc.act(0.85), { dir: 0.3, spread: 0.6 });
      const cord = sc.add('<g><rect x="270" y="160" width="100" height="6" rx="3" fill="rgba(36,26,33,.2)"/><rect class="v" x="270" y="160" width="' + res.before + '" height="6" rx="3" fill="#c8382c"/></g>');
      sc.anim(cord.querySelector('.v'), [{ width: res.before + 'px' }, { width: Math.min(100, res.after) + 'px' }], sc.R, 0.6);
      if (sc.good || sc.great) sc.anim(p, [{ transform: 'rotate(0)' }, { transform: 'rotate(-10deg)', offset: 0.5 }, { transform: 'rotate(0)' }], sc.tail(0.1), 0.5, { fill: 'none' });
      if (sc.great && res.gift !== null && res.gift !== undefined) { const ic = sc.prop('guandao', 320, 30, 40); sc.anim(ic, [{ transform: 'translateY(40px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.tail(0.1), 0.5); }
    },
  });

  // ---- 交易 ----
  define('trade', {
    anchor: 'market', shot: { kind: 'close' },
    title(res) { return res.side === 'sell' ? '交易·卖粮' : '交易·买粮'; },
    chips(res) {
      return res.side === 'sell' ? [{ icon: 'food', label: '粮', delta: -res.food, cost: true }, { icon: 'gold', label: '金', delta: res.goldOut || res.gold }]
        : [{ icon: 'gold', label: '金', delta: -res.gold, cost: true }, { icon: 'food', label: '粮', delta: res.food }];
    },
    forecast(res) {
      const g = G(), m = g ? g.month : 1;
      return '本月粮价 <b>' + res.price + '</b> / 100' + (res.side === 'buy' && !(m >= 7 && m <= 9) ? '　<span style="color:#6e5d53">秋后粮价最低（7–9 月）</span>' : '');
    },
    banner(res) { return res.side === 'sell' ? '高价售粮' : '丰年购粮'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + '<rect x="170" y="14" width="80" height="30" rx="3" fill="#8c6a48" stroke="' + INK + '" stroke-width="2"/>');
      sc.text(210, 37, res.price + ' / 百', 20, '#fff6e2');
      sc.add('<path d="M210 44 V64" stroke="' + INK + '" stroke-width="2.4"/><circle cx="210" cy="66" r="4" fill="#e8b54a" stroke="' + INK + '" stroke-width="1.4"/>');
      const beam = sc.add('<g class="vb" style="transform-origin:210px 70px"><path d="M130 70 H290" stroke="' + INK + '" stroke-width="4"/><path d="M150 70 V86" stroke="' + INK + '" stroke-width="1.6"/>' + D.sack(150, 110, 1.1) + '<path d="M270 70 V90" stroke="' + INK + '" stroke-width="1.6"/><path d="M262 90 H278 L276 104 H264Z" fill="#a0703f" stroke="' + INK + '" stroke-width="1.8"/></g>');
      const B = beam.firstChild;
      sc.anim(B, [{ transform: 'rotate(0)' }, { transform: 'rotate(' + (res.side === 'sell' ? 9 : -9) + 'deg)', offset: 0.45 }, { transform: 'rotate(' + (res.side === 'sell' ? -3 : 3) + 'deg)', offset: 0.75 }, { transform: 'rotate(0)' }], sc.act(0.1), Math.max(0.4, sc.act(1) - sc.act(0.1)));
      const n = clamp(Math.ceil((res.gold || res.goldOut || 100) / 150), 2, 6);
      for (let i = 0; i < n; i++) {
        const sk = sc.add(res.side === 'sell' ? D.sack(0, 150, 0.8) : D.coin(0, 144, 6));
        const c = sc.add(res.side === 'sell' ? D.coin(0, 144, 6) : D.sack(0, 150, 0.8));
        sc.anim(sk, [{ transform: 'translateX(' + (res.side === 'sell' ? 40 : 380) + 'px)' }, { transform: 'translateX(' + (res.side === 'sell' ? 380 : 40) + 'px)' }], sc.act(0.2) + i * 0.08, 0.6);
        sc.anim(c, [{ transform: 'translateX(' + (res.side === 'sell' ? 380 : 40) + 'px)', opacity: 0 }, { transform: 'translateX(' + (res.side === 'sell' ? 60 + i * 14 : 340 - i * 14) + 'px)', opacity: 1 }], sc.act(0.4) + i * 0.08, 0.6);
      }
      if (sc.good || sc.great) { const ex = sc.add(D.sack(res.side === 'sell' ? 60 : 340, 132, 0.9)); sc.anim(ex, [{ transform: 'translateY(-60px) rotate(-40deg)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.tail(0.05), 0.35, { ease: 'cubic-bezier(.3,1.4,.5,1)' }); }
      if (sc.great) for (let i = 0; i < 8; i++) { const s = sc.add(D.sack(res.side === 'sell' ? 30 + (i % 4) * 18 : 310 + (i % 4) * 18, 150 - Math.floor(i / 4) * 14 - 16, 0.8)); sc.anim(s, [{ transform: 'translateY(-50px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.tail(0.15) + i * 0.05, 0.3); }
    },
  });

  // ---- 任命 ----
  define('appoint', {
    anchor: 'palace', shot: { kind: 'close' },
    chips(res) { return [{ label: '太守 ' + nm(sc0(res)), text: '' }, { label: '政治', text: String(res.pol) }].concat(res.prevPol ? [{ label: '前任', text: String(res.prevPol), small: true }] : []); },
    forecast(res) { const c = cityOf(res.city); return c ? esc(c.name) + ' 的开发由太守主持　政治 <b>' + res.pol + '</b>' : null; },
    banner(res) { return nm(genOf(res.gen)) + ' 治 ' + cityName(res.city); },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.hall(210, 120, 1.0));
      const pl = sc.add('<rect x="180" y="52" width="60" height="18" fill="#2a2430" stroke="#e8b54a" stroke-width="1.6"/>');
      const plt = sc.text(210, 66, nm(sc.gen), 13, '#f8dc8e');
      sc.fadeIn(plt, sc.R, 0.4);
      sc.add('<rect x="250" y="128" width="90" height="22" fill="#8c6a48" stroke="' + INK + '" stroke-width="1.8"/><rect x="262" y="118" width="64" height="12" fill="#f8f0dc" stroke="' + INK + '" stroke-width="1.2"/>');
      const seal = sc.add(D.stamp(0, 0, 1.1));
      sc.anim(seal.firstChild, [{ transform: 'translate(294px,40px)' }, { transform: 'translate(294px,118px)', offset: 0.7 }, { transform: 'translate(294px,108px)' }], sc.act(0.2), Math.max(0.35, sc.act(1) - sc.act(0.2)));
      const imp = sc.add('<rect x="282" y="104" width="24" height="14" fill="#c8382c" opacity=".9"/>');
      sc.fadeIn(imp, sc.act(0.85), 0.1);
      sc.burst(294, 116, 'seal', 8, sc.act(0.85));
      const p = sc.portrait(sc.gen, { x: 90, y: 140, size: 70, plate: true });
      sc.anim(p, [{ opacity: 0, transform: 'scale(.6)' }, { opacity: 1, transform: 'scale(1)' }], sc.act(0.9), 0.3);
      void pl;
      if (sc.good || sc.great) for (let i = 0; i < (sc.great ? 6 : 2); i++) { const o = sc.add(D.soldier(150 + (i % 3) * 22 + (i >= 3 ? 90 : 0), 150, '#3b4f7a', 0.55)); sc.anim(o, [{ transform: 'rotate(0)', opacity: 0 }, { transform: 'rotate(12deg)', opacity: 1 }], sc.tail(0.05) + i * 0.06, 0.3); }
    },
  });
  function sc0(res) { return genOf(res.gen); }

  // ---- 市场（ITEMS 自己揭示；这里是城池视图 / 牌面的购入一幕）----
  define('market', {
    anchor: 'shop', shot: { kind: 'close' },
    chips(res) { const it = itemOf(res.item); return [{ label: it ? it.name : '装备', text: '' }, { label: rarityZh(res.rarity), text: '', small: true }].concat(res.price ? [{ icon: 'gold', label: '金', delta: -res.price, cost: true }] : []); },
    banner(res) { const it = itemOf(res.item); return '神兵　' + (it ? it.name : ''); },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + '<path d="M120 40 L210 14 L300 40Z" fill="#3aa5a0" stroke="' + INK + '" stroke-width="2.4"/><path d="M128 40 V150 M292 40 V150" stroke="' + INK + '" stroke-width="3"/>');
      const RC = { common: '#9aa3ad', uncommon: '#5fae6e', rare: '#4a8fdc', epic: '#a464d8', legendary: '#f0a52a' }[res.rarity] || '#9aa3ad';
      const crd = sc.add('<rect x="180" y="58" width="60" height="80" rx="6" fill="#2a2430" stroke="' + RC + '" stroke-width="4"/>');
      sc.pop(crd, sc.R - 0.2, 0.3);
      const ic = sc.prop('guandao', 210, 98, 46);
      sc.anim(ic, [{ transform: 'translateY(20px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.R - 0.1, 0.4);
      if (sc.great) { const pil = sc.add('<rect x="186" y="-20" width="48" height="170" style="fill:rgba(255,236,190,.4)"/>'); sc.fadeIn(pil, sc.tail(0), 0.5); }
    },
  });

  // ---- 授官（TITLES 有自己的仪式；这里是缺省的一幕）----
  define('title', {
    anchor: 'palace', shot: { kind: 'close' },
    chips(res) { return [{ label: (res.titleName || '官职'), text: '' }, { label: '品阶', text: '★'.repeat(clamp(res.titleTier | 0, 1, 5)), small: true }]; },
    banner(res) { return '册封　' + (res.titleName || ''); },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.hall(210, 120, 1.0));
      sc.portrait(sc.gen, { x: 120, y: 150, size: 72, plate: true });
      const cap = sc.add('<g><rect x="250" y="118" width="60" height="14" rx="6" fill="#8a5fc0" stroke="' + INK + '" stroke-width="1.8"/><path d="M258 118 L256 98 Q280 88 302 94 L300 118Z" fill="#2a2430" stroke="' + INK + '" stroke-width="1.8"/></g>');
      sc.pop(cap, sc.act(0.3), 0.3);
      const n = clamp(res.titleTier | 0, 1, 5);
      for (let i = 0; i < n; i++) { const s = sc.text(250 + i * 16, 82, '★', 18, '#f8dc8e', { stroke: INK, sw: 2 }); sc.pop(s.firstChild, sc.R + i * 0.12, 0.25); }
    },
  });

  // ---- 秋收（时刻 §9.1）----
  define('harvest', {
    anchor: 'farms', shot: { kind: 'bldg' },
    title() { return '秋收'; },
    sub() { const g = G(); return g ? g.year + '年 七月' : ''; },
    chips(res) {
      const recs = res.records || [];
      const tot = recs.reduce((a, h) => a + (h.harvest || 0), 0);
      const out = [{ icon: 'food', label: '粮', delta: tot }];
      if (recs.length > CFG.harvestMaxCities) out.push({ label: '另有 ' + (recs.length - CFG.harvestMaxCities) + ' 城', text: '', small: true });
      return out;
    },
    forecast(res) { const recs = res.records || []; return recs.length ? recs.slice().sort((a, b) => b.harvest - a.harvest).slice(0, 3).map(h => esc(cityName(h.city)) + ' +' + fmt(h.harvest)).join('　') : null; },
    miss() { return { title: '收成不佳', hint: '储粮以备：冬春之交粮价最高' }; },
    banner() { return '大有之年'; },
    card(sc) {
      const recs = (sc.res.records || []).slice().sort((a, b) => (SG.DomesticTiers.RANK[(b._t || {}).tier] || 0) - (SG.DomesticTiers.RANK[(a._t || {}).tier] || 0) || b.harvest - a.harvest).slice(0, CFG.harvestMaxCities);
      const n = Math.max(1, recs.length);
      const span = (sc.a1 - sc.a0) / n;
      recs.forEach((h, i) => {
        const t0 = sc.a0 + i * span;
        const grp = sc.add('<g></g>');
        const lean = (h._t || {}).tier === 'miss';
        let mk = D.ground(120, lean ? '#cdbf9a' : '#d9c08f', 60);
        const tiles = 9;
        for (let j = 0; j < tiles; j++) mk += '<rect class="f" x="' + (16 + j * 44) + '" y="128" width="40" height="18" rx="3" fill="#6aa65e" stroke="' + INK + '" stroke-width="1.4"/>';
        grp.innerHTML = mk;
        sc.text(210, 40, cityName(h.city), 24, INK, { stroke: PAPER, sw: 4, parent: grp });
        const amt = sc.text(210, 74, '+0', 22, '#2e6e3a', { stroke: PAPER, sw: 4, parent: grp });
        sc.count(amt.firstChild, 0, h.harvest || 0, t0 + 0.2, Math.min(0.7, span * 0.6), { fmt: v => '+' + fmt(v) + ' 粮', sound: i === 0 });
        grp.querySelectorAll('.f').forEach((f, j) => sc.anim(f, [{ fill: '#6aa65e' }, { fill: lean ? '#c9c08a' : '#e8c25a' }], t0 + 0.05 + j * 0.05, 0.2));
        if (!lean) sc.burst(210, 136, 'grain', 18, t0 + 0.4, { w: 360 });
        grp.style.opacity = '0';
        sc.anim(grp, [{ opacity: 0 }, { opacity: 1, offset: 0.12 }, { opacity: 1, offset: 0.88 }, { opacity: i === n - 1 ? 1 : 0 }], t0, span + (i === n - 1 ? sc.T : 0), { fill: 'forwards' });
      });
    },
  });

  // ---- 城池升阶（时刻 §9.2）----
  define('stageUp', {
    title() { return '城池升阶'; },
    sub(res) { return cityName(res.city); },
    chips(res) { return (res.ups || []).slice(0, 3).map(u => ({ label: (BLDG_ZH[u.building] || u.building) + '　' + stageNameOf(u.building, u.from) + ' → ' + stageNameOf(u.building, u.to), text: '' })); },
    banner(res) { const u = (res.ups || [])[0]; return u ? (BLDG_ZH[u.building] || '') + '　' + stageNameOf(u.building, u.to) : '升格'; },
    card(sc) {
      sc.add(D.cloud(120, 150, 2.2) + D.cloud(300, 156, 2.4));
      const h = sc.add(D.hall(210, 132, 1.2));
      sc.anim(h, [{ transform: 'translateY(70px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.act(0.1), Math.max(0.4, sc.act(1) - sc.act(0.1)), { ease: 'cubic-bezier(.3,1.3,.5,1)' });
      sc.burst(210, 140, 'dust', 30, sc.act(0.6), { w: 160 });
      if (SG.Sfx) sc.at(sc.act(0.5), () => sfx('fx-build', 0.7));
    },
  });

  // ---- 月初（只有短乐；牌面保留给测试页）----
  define('month', { stage: 'card', chips() { return []; }, card(sc) { const b = sc.cmdIcon('month', 210, 90, 90); sc.anim(b, [{ transform: 'rotate(-6deg)' }, { transform: 'rotate(6deg)' }], sc.act(0), 0.5, { iter: 2, dir: 'alternate' }); } });

  // ---- ENGAGEMENT 的新指令与时刻（§5.18–§5.28）----
  define('build', {
    anchor: 'project', shot: { kind: 'bldg' },
    title(res) { return res.done || res.sub === 'done' ? '营建·落成' : '营建'; },
    sub(res) { return [cityName(res.city), res.projectName || res.project || ''].filter(Boolean).join(' · '); },
    chips(res) { return res.done || res.sub === 'done' ? [{ label: res.projectName || '工程', text: '落成' }] : [{ icon: 'gold', label: '金', delta: -(res.cost || 0), cost: true }].concat(res.months ? [{ label: '工期', text: res.months + ' 月', small: true }] : []); },
    banner(res) { return (res.projectName || '工程') + '　落成'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30));
      const h = sc.add(D.hall(210, 146, 1.0));
      const sf = sc.add(D.scaffold(210, 146, 120, 70));
      const done = res.done || res.sub === 'done';
      if (!done) sc.anim(h, [{ clipPath: 'inset(60% 0 0 0)' }, { clipPath: 'inset(35% 0 0 0)' }], sc.act(0.2), 0.8);
      const mal = sc.add(D.hammer(280, 70));
      for (let i = 0; i < 3; i++) { const t = sc.act((i + 0.5) / 3.5); sc.anim(mal.firstChild, [{ transform: 'rotate(0)' }, { transform: 'rotate(-30deg)', offset: 0.5 }, { transform: 'rotate(0)' }], t - 0.15, 0.25, { fill: 'none' }); sc.burst(282, 82, 'dust', 4, t); }
      if (done || sc.great) { sc.anim(sf, [{ transform: 'translateY(0)', opacity: 1 }, { transform: 'translateY(30px)', opacity: 0 }], sc.tail(0.05), 0.5, { fill: 'forwards' }); sc.burst(210, 140, 'ink', 30, sc.tail(0.1), { w: 120 }); sc.burst(210, 140, 'dust', 30, sc.tail(0.1), { w: 160 }); }
    },
  });
  const caravanBack = res => res.sub === 'return' || (res.sub !== 'depart' && res.profit !== undefined && res.profit !== null) || !!res.lost;
  define('caravan', {
    anchor: 'market', shot: { kind: 'gate.out' },
    title(res) { return caravanBack(res) ? '通商·归来' : '通商'; },
    chips(res) {
      const items = Array.isArray(res.items) ? res.items : res.item !== undefined && res.item !== null ? [res.item] : [];
      return caravanBack(res) ? [{ icon: 'gold', label: '利', delta: res.profit || 0 }].concat(items.length ? [{ label: '带回 ' + items.length + ' 件', text: '', small: true }] : [])
        : [{ icon: 'gold', label: '本金', delta: -(res.gold || res.invest || 0), cost: true }];
    },
    miss() { return { title: '商队失散', hint: '路途越远越险：派智力高的领队，或走商路枢纽' }; },
    banner() { return '商队满载而归'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#e2c58f', 30));
      const back = caravanBack(res);
      const xs = stripLabels(sc, back ? [cityName(res.to), cityName(res.city)] : [cityName(res.city), cityName(res.to)], 128);
      for (let i = 0; i < 3; i++) travel(sc, groundScale(0.8) + D.camel(0, 150) + '</g>', xs[0] - i * 40, xs[1] - 40 - i * 40, 150, sc.act(i * 0.1), sc.act(1), { bob: true });
      sc.burst(xs[0], 110, 'bell', 6, sc.act(0.2));
      if (back && !sc.miss) sc.burst(xs[1], 100, 'coin', sc.great ? 40 : 16, sc.R + 0.1);
    },
  });
  define('explore', {
    anchor: 'gate.out', shot: { kind: 'gate.out' },
    title(res) { return res.outcome || res.sub === 'return' ? '探索·归来' : '探索'; },
    chips(res) {
      const out = [];
      if (res.outcome === 'clue' || res.clue) out.push({ label: '藏宝图 · 线索', text: '' });
      if ((res.outcome === 'item' || res.sub === 'return') && res.item !== undefined && res.item !== null) out.push({ label: (itemOf(res.item) || {}).name || '古物', text: '' });
      if (res.prestige) out.push({ icon: 'prestige', label: '声望', delta: res.prestige });
      if (res.gold) out.push({ icon: 'gold', label: '金', delta: res.gold });
      if (!res.outcome && res.sub !== 'return') out.push({ label: '前往 ' + (res.siteName || '名胜'), text: '' });
      return out;
    },
    miss() { return { title: '出师不利', hint: '领队受了点伤，休养几月便好：带上智力或武力高的随从' }; },
    banner(res) { return res.outcome === 'clue' ? '藏宝图　得一线索' : '探幽得宝'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.mountains(150, '#5f78a8') + D.ground(150, '#b7c79a', 30));
      const mist = sc.add('<rect x="-10" y="60" width="440" height="60" fill="#f3e7cc" opacity=".55"/>');
      sc.anim(mist, [{ transform: 'translateX(-30px)' }, { transform: 'translateX(30px)' }], 0, sc.T, { ease: 'linear' });
      for (let i = 0; i < 2; i++) travel(sc, D.walker(0, 150, i === 0), 20 - i * 22, 320 - i * 22, 150, sc.act(0), sc.act(1), { bob: true });
      if (res.outcome === 'item' || res.outcome === 'clue' || res.clue || (res.sub === 'return' && res.item !== undefined && res.item !== null)) { const s = sc.add(D.scroll(340, 120, 34, 26, '#f8f0dc')); sc.pop(s, sc.R, 0.3); sc.burst(340, 100, 'gold', 30, sc.R + 0.1); }
    },
  });
  define('gift', {
    anchor: 'palace', shot: { kind: 'gate.out' },
    sub(res) { return '→ ' + factionName(res.target); },
    chips(res) { return [res.gold ? { icon: 'gold', label: '礼金', delta: -res.gold, cost: true } : null, typeof res.dRel === 'number' ? { label: '邦交', delta: res.dRel } : null].filter(Boolean); },
    banner(res) { return '厚礼交欢　' + factionName(res.target); },
    card(sc) {
      const res = sc.res, tc = facColor(res.target, '#8a5fc0');
      sc.add(D.ground(150, '#cdb48a', 30) + D.banner(360, 150, tc, factionName(res.target).charAt(0), 1));
      const box = travel(sc, '<g transform="translate(0,150)"><rect x="-16" y="-24" width="32" height="20" rx="2" fill="#c8382c" stroke="' + INK + '" stroke-width="1.8"/><rect class="lid" x="-18" y="-30" width="36" height="7" rx="2" fill="#e0604a" stroke="' + INK + '" stroke-width="1.6"/><path d="M0 -30 V-4" stroke="#e8b54a" stroke-width="3"/></g>', 40, 300, 150, sc.act(0), sc.act(1));
      const lid = box.querySelector('.lid');
      sc.anim(lid, [{ transform: 'translateY(0)' }, { transform: 'translateY(-6px)' }], sc.R, 0.3, { fill: 'forwards' });
      sc.burst(300, 120, 'gold', 20, sc.R + 0.1);
    },
  });
  define('marriage', {
    anchor: 'palace', shot: { kind: 'est' },
    sub(res) { return '→ ' + factionName(res.target); },
    chips(res) { return res.cost ? [{ icon: 'gold', label: '聘礼', delta: -res.cost, cost: true }] : []; },
    miss(res) { return { title: factionName(res.target) + ' 婉拒联姻', hint: '邦交越好、国势相当时越易成功' }; },
    banner(res) { return '秦晋之好　' + factionName(res.target); },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.lanterns(26, 8, 30, 390));
      const cups = meet(sc, '<g><path d="M150 80 H182 Q182 104 166 104 Q150 104 150 80Z" fill="#c8382c" stroke="' + INK + '" stroke-width="2"/></g>', '<g><path d="M238 80 H270 Q270 104 254 104 Q238 104 238 80Z" fill="#c8382c" stroke="' + INK + '" stroke-width="2"/></g>', { y: 70 });
      void cups;
      const sed = travel(sc, '<g transform="translate(0,150)"><rect x="-20" y="-34" width="40" height="28" rx="4" fill="#c8382c" stroke="' + INK + '" stroke-width="2"/><path d="M-26 -12 H26" stroke="#8c6a48" stroke-width="3"/></g>', -40, 460, 150, sc.act(0), sc.T, { ease: 'linear' });
      void sed;
      if (sc.great) sc.ctx.greatPreset = 'petal';
    },
  });
  define('tribute', {
    anchor: 'treasury', shot: { kind: 'bldg' },
    title(res) { return res.mode === 'demand' ? '外交·索贡' : '外交·纳贡'; },
    sub(res) { return factionName(res.target); },
    chips(res) { return res.pay ? [{ icon: 'gold', label: res.mode === 'demand' ? '贡金' : '纳金', delta: res.mode === 'demand' ? res.pay : -res.pay, cost: res.mode !== 'demand' }] : []; },
    miss(res) { return { title: factionName(res.target) + ' 拒绝', hint: res.mode === 'demand' ? '对方越弱、我方兵势越盛越易应允' : '对方不屑：先改善邦交' }; },
    banner(res) { return factionName(res.target) + '　来朝'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.ding(210, 120));
      const out = res.mode !== 'demand';
      for (let i = 0; i < 3; i++) travel(sc, groundScale(0.65) + D.cart(0, 150, D.coin(-6, -26, 5) + D.coin(6, -28, 5)) + '</g>', out ? 160 - i * 50 : 460 + i * 50, out ? 460 + i * 30 : 160 + i * 40, 150, sc.act(i * 0.1), sc.act(1));
      if (!sc.miss) sc.burst(210, 100, 'coin', 12, sc.R);
    },
  });
  define('hostage', {
    anchor: 'gate', shot: { kind: 'gate.out' },
    title(res) { return res.back ? '质子·归来' : '质子'; },
    sub(res) { return nm(genOf(res.gen)) + (res.target !== undefined ? ' · ' + factionName(res.target) : ''); },
    chips(res) { return res.back && res.exp ? [{ label: '经验', delta: res.exp }] : []; },
    banner(res) { return nm(genOf(res.gen)) + '　归国'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.gate(60, 150, sc.fc));
      if (res.back) travel(sc, D.walker(0, 150, false), 440, 110, 150, sc.act(0), sc.act(1), { bob: true });
      else travel(sc, D.walker(0, 150, false), 110, 440, 150, sc.act(0.2), sc.T, { bob: true, ease: 'linear' });
    },
  });
  define('truce', {
    anchor: 'walls', shot: { kind: 'gate.in' },
    sub(res) { return factionName(res.target); },
    chips(res) { return res.gold ? [{ icon: 'gold', label: '金', delta: -res.gold, cost: true }] : []; },
    miss(res) { return { title: factionName(res.target) + ' 不允停战', hint: '礼金已作馈赠；双方兵力相当时更易议和' }; },
    banner() { return '息兵罢战'; },
    card(sc) {
      sc.add(D.ground(150, '#cdb48a', 30) + D.gate(210, 150));
      const ji = sc.add('<g class="vb" style="transform-origin:150px 150px"><path d="M150 150 L150 60" stroke="' + INK + '" stroke-width="4"/><path d="M146 60 l4 -12 l4 12z" fill="#c9d2da" stroke="' + INK + '" stroke-width="1.2"/></g>');
      sc.anim(ji.firstChild, [{ transform: 'rotate(0)' }, { transform: 'rotate(' + (sc.miss ? -10 : -70) + 'deg)' }], sc.act(0.3), 0.6, { fill: 'forwards' });
      if (!sc.miss) { const fl = sc.add('<path d="M240 92 V40 M240 42 l22 6 l-22 6" stroke="' + INK + '" stroke-width="2" fill="#fbf3df"/>'); sc.anim(fl, [{ transform: 'translateY(30px)', opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }], sc.R, 0.5); }
    },
  });
  define('tradepact', {
    anchor: 'market', shot: { kind: 'close' },
    sub(res) { return factionName(res.target); },
    chips() { return []; },
    miss(res) { return { title: factionName(res.target) + ' 拒绝互市', hint: '先赠礼改善邦交' }; },
    banner() { return '互市通商'; },
    card(sc) {
      const res = sc.res, tc = facColor(res.target, '#3aa5a0');
      sc.add(D.ground(150, '#cdb48a', 30));
      meet(sc, '<path d="M120 60 H206 L212 72 L204 84 L212 96 L206 108 H120Z" fill="' + sc.fc + '" stroke="' + INK + '" stroke-width="2.4"/>', '<path d="M300 60 H214 L220 72 L212 84 L220 96 L214 108 H300Z" fill="' + tc + '" stroke="' + INK + '" stroke-width="2.4"/>', { y: 128 });
    },
  });
  define('submit', {
    anchor: 'palace', shot: { kind: 'gate.in' },
    sub(res) { return factionName(res.from); },
    chips(res) { return [{ icon: 'city', label: '城', delta: res.cities || 0 }, { label: '武将', delta: res.gens || 0 }]; },
    banner(res) { return factionName(res.from) + '　归附'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + D.hall(210, 120, 1.0));
      const s = sc.add(D.stamp(210, 150, 1.4));
      sc.anim(s, [{ transform: 'translateY(30px)', opacity: 0 }, { transform: 'translateY(-10px)', opacity: 1 }], sc.act(0.2), 0.6);
      const fl = sc.add(D.banner(330, 150, facColor(res.from, '#8a5fc0'), '', 0.9));
      sc.at(sc.R, () => fl.querySelectorAll('path')[1] && fl.querySelectorAll('path')[1].setAttribute('fill', sc.fc));
    },
  });
  define('mission', {
    anchor: 'palace', shot: { kind: 'close' },
    title() { return '国策·功成'; },
    sub(res) { return res.name || res.id || ''; },
    chips(res) { return res.reward && typeof res.reward === 'object' ? Object.keys(res.reward).slice(0, 3).map(k => ({ label: { gold: '金', prestige: '声望', food: '粮' }[k] || k, delta: +res.reward[k] || 0 })) : []; },
    banner() { return '国策功成'; },
    card(sc) {
      const s = sc.add(D.scroll(210, 150, 90, 110));
      sc.anim(s, [{ clipPath: 'inset(0 0 100% 0)' }, { clipPath: 'inset(0 0 0 0)' }], sc.act(0), Math.max(0.3, sc.act(0.7) - sc.act(0)));
      const seal = sc.add('<rect x="226" y="112" width="20" height="20" fill="#c8382c"/>'); sc.pop(seal, sc.act(0.8), 0.2);
      const ck = sc.add('<path d="M260 100 L276 122 L310 60" stroke="#c8382c" stroke-width="8" fill="none" stroke-linecap="round" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100"/>');
      sc.anim(ck.firstChild, [{ strokeDashoffset: 100 }, { strokeDashoffset: 0 }], sc.R, 0.4);
    },
  });
  define('festival', {
    anchor: 'capital', shot: { kind: 'est' },
    title(res) { return (res.grade || res.sub) === 'grand' ? '节庆·盛大' : '节庆'; },
    chips(res) { return res.cost ? [{ icon: 'gold', label: '金', delta: -res.cost, cost: true }] : []; },
    banner() { return '与民同乐'; },
    card(sc) {
      sc.add(D.ground(150, '#cdb48a', 30) + D.lanterns(30, 9, 20, 400));
      for (let i = 0; i < 6; i++) { const d = sc.add(D.soldier(60 + i * 60, 150, i % 2 ? '#c8382c' : '#e8b54a', 0.6)); sc.anim(d, [{ transform: 'translateY(0)' }, { transform: 'translateY(-10px)' }], sc.act(0) + i * 0.07, 0.3, { iter: 6, dir: 'alternate' }); }
      sc.burst(210, 40, 'confetti', 60, sc.R, { w: 380 });
      sc.ctx.goodPreset = 'confetti'; sc.ctx.greatPreset = 'confetti';
    },
  });
  define('wenhui', {
    anchor: 'academy', shot: { kind: 'close' },
    title() { return '文会'; },
    chips(res) { return [{ label: '名次', text: '第' + (NUM_ZH[res.place] || res.place) + '名' }]; },
    banner() { return '文会魁首'; },
    card(sc) {
      const res = sc.res;
      sc.add(D.ground(150, '#cdb48a', 30) + '<rect x="120" y="120" width="180" height="16" rx="3" fill="#8c6a48" stroke="' + INK + '" stroke-width="2"/>');
      for (let i = 0; i < 3; i++) { const s = sc.add(D.scroll(150 + i * 60, 118, 40, 30)); sc.pop(s, sc.act(0.2 + i * 0.2), 0.3); }
      const st = sc.text(210, 70, '第' + (NUM_ZH[res.place] || res.place) + '名', 30, '#c8382c', { stroke: PAPER, sw: 4 });
      sc.pop(st.firstChild, sc.R, 0.3);
    },
  });

  // ================================================================ 自动接线 ==
  // strategy-screen.js 还没有自己调用 play() 时：玩家势力的指令记录（SG.Events 'command'）在下一拍合并后演出；
  // 同一条记录若已经由 play() 演过（W8 接上之后）就不再演。秋收与城池升阶在换月动画结束之后演。
  const AUTO_SKIP = { market: 1, title: 1 };     // 这两样由 ITEMS / TITLES 自己的画面揭示
  let pend = [], pendTimer = 0;
  let inMonthEnd = false;          // StrategyAI.endMonth 正在跑（'month.phase' snapshot … news）：这时写的记录（营建落成、商队归来……）留到换月之后
  function onCommandEvt(cmd, res) {
    if (!Fx.auto || !hasDoc || !res || res.delegated) return;
    const g = G();
    if (!g || res.f !== g.player || g.player < 0 || AUTO_SKIP[cmd]) return;
    if (inMonthEnd) { (monthMoments = monthMoments || []).push(res); return; }
    pend.push(res);
    if (!pendTimer) pendTimer = setTimeout(flushPending, 0);
  }
  let monthMoments = null;
  function flushPending() {
    pendTimer = 0;
    const list = coalesce(pend.filter(r => !r._fxPlayed));
    pend = [];
    for (const r of list) {
      if (r._fxPlayed || (r._orig && r._orig._fxPlayed)) continue;
      Fx.play(r);
    }
  }
  let monthPend = null;
  function onPhase(name, ctx) {
    if (!Fx.auto || !hasDoc || !ctx || !ctx.g) return;
    if (name === 'snapshot') { inMonthEnd = true; return; }
    if (name !== 'news') return;
    const g = ctx.g;
    if (g.player < 0) { inMonthEnd = false; return; }
    const harvests = (ctx.harvests || []).filter(h => { const c = g.cities[h.city]; return c && c.owner === g.player; });
    const ms = Fx.monthStart(g);
    if (ctx.stageUps && Array.isArray(ctx.stageUps)) for (const s of ms.stageUps) ctx.stageUps.push(s);
    monthPend = { harvests, stageUps: ms.stageUps, src: ctx.harvests, srcUps: ctx.stageUps, at: now(), month: ctx.next ? ctx.next.monthIndex : g.monthIndex, moments: null };
    // news 是最后一相：之后（同一任务里）还有记录也算这一月的；下一拍再收
    // 换月动画在 endMonth 返回之后才建（同一任务内）：下一拍再看
    setTimeout(waitMonth, 50);
  }
  function waitMonth() {
    const p = monthPend;
    if (!p) return;
    const busy = document.querySelector('.sg-monthfx') || (SG.BattleController && SG.BattleController.current);
    if (busy && now() - p.at < 20) { setTimeout(waitMonth, 120); return; }
    monthPend = null;
    inMonthEnd = false;
    const moments = coalesce((monthMoments || []).filter(r => !r._fxPlayed));
    monthMoments = null;
    let q = Promise.resolve();
    if (p.harvests.length && !(p.src && p.src._fxPlayed)) q = q.then(() => (p.src && p.src._fxPlayed ? null : Fx.harvest(p.harvests)));
    if (p.stageUps.length && !(p.srcUps && p.srcUps._fxPlayed)) q = q.then(() => (p.srcUps && p.srcUps._fxPlayed ? null : Fx.stageUps(p.stageUps)));
    // 月末写下的时刻（营建落成、商队 / 探索归来、归附、国策、节庆、文会），总长守 8 秒的月初预算（INTEGRATION §6.6）：至多 2 段
    for (const r of moments.slice(0, 2)) q = q.then(() => (r._fxPlayed ? null : Fx.play(r)));
  }
  function attach() {
    try {
      if (SG.Events && SG.Events.on && !Fx._attached) {
        Fx._attached = true;
        SG.Events.on('command', onCommandEvt);
        SG.Events.on('month.phase', onPhase);
      }
    } catch (e) { warnOnce(e); }
  }
  attach();
  // 音乐的预热（§8.1）：开局之后在空闲时把常用的 6 段 A 段采样先渲染好
  let warmed = false;
  function prewarm() {
    if (warmed || !hasDoc) return;
    const MS = SG.MusicStings, Mu = SG.Music;
    if (!G() || !MS || !Mu || !Mu.prepare || !(SG.Sfx && SG.Sfx.musicOn)) { setTimeout(prewarm, 5000); return; }
    warmed = true;
    const fam = familyOfPlayer();
    const ids = ['develop.land', 'develop.industry', 'develop.town', 'train', 'recruit', 'search'];
    let i = 0;
    const next = () => { if (i >= ids.length) return; const k = MS.key(ids[i++], 'normal', fam); try { const pr = Mu.prepare(k); if (pr && pr.then) pr.then(() => setTimeout(next, 200)); else setTimeout(next, 200); } catch (e) { /* 忽略 */ } };
    (W.requestIdleCallback || (f => setTimeout(f, 300)))(next);
  }
  if (hasDoc) setTimeout(prewarm, 4000);
  // 音效的 PCM 第一次用时才合成（fx-great 等要 5–15 ms）：载入后在空闲时以 0 音量各放一次，演出时不再卡一帧
  const WARM_SFX = ['fx-tick', 'fx-land', 'fx-good', 'fx-great', 'fx-seal', 'fx-miss', 'fx-wind', 'fx-build'];
  function warmSfx() {
    const ric = W.requestIdleCallback ? (f => W.requestIdleCallback(f, { timeout: 3000 })) : (f => setTimeout(f, 200));
    let i = 0;
    const next = () => { if (i >= WARM_SFX.length) return; try { if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(WARM_SFX[i], 0); } catch (e) { /* 无音频 */ } i++; ric(next); };
    ric(next);
  }
  if (hasDoc) setTimeout(warmSfx, 1500);

  // 设置画面的「内政演出」行写的是同一个键：读档后同步
  if (hasDoc) W.addEventListener('storage', e => { if (e.key === CFG.storeKey) loadSettings(); });
  Fx.harvestTier = harvestTier;
  Fx.fillStages = fillStages;
  Fx.shownStages = shownStages;
  Fx.currentStages = currentStages;
  Fx.Timeline = Timeline;
  SG.DomesticFx = Fx;
})();
