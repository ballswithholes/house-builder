'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 共享登记表（INTEGRATION §10）
   纯逻辑，无 THREE / DOM，Node 可测。紧接 core.js 载入：所有模块（规则、数据、界面）都可以在
   拥有者模块载入之前登记自己的条目，拥有者模块再在此基础上扩展（不要整个替换这些对象）。

   SG.Events                     事件总线（规则层的「之后」钩子：攻城、俘虏、继承、指令……）
     on(name, fn) → off()        emit(name, ...args)        has(name)
     无监听者时 emit 什么也不做；监听者不得取 SG.Random（关掉的系统不取随机数，INTEGRATION §1.2）。
     规则层发出的事件（js/model.js、js/strategy-ai.js）：
       'command'            (cmd, res)            每条指令结束、Commands.lastResult 写好之后（= Commands.onCommand）
       'faction.death'      (fid)                 势力灭亡（GameState.checkFactionDeath）
       'conquest.prepare'   (s)                   出征（Conquest.prepare 之后）
       'conquest.resolved'  (s)                   自动结算之后（Conquest.autoResolve）
       'conquest.apply'     (s, { attacker, defender, won })   归属、撤退、俘虏结算之后（Conquest.apply）
       'captive.taken'      (s, gen)              俘虏登记
       'captive.released'   (gen)                 'captive.executed' (gen, { faction, wasRuler })
       'faction.succession' (fid, heir)           继承（heir = null 表示无人继承）
       'month.phase'        (name, ctx)           月末流程每一相开始时（StrategyAI.endMonth，INTEGRATION §6.3）

   SG.Settings                   设置登记表（UI-V4 §7；第 51–53 行由 ENGAGEMENT / TOURNAMENT 登记）
     define(def) → def           def = { id, tab, order, label, sub, type: 'seg'|'slider'|'toggle'|'button',
                                         options, min, max, step, format, default, store: { key, path }, apply, when, owner, row }
     get(id) → value             set(id, v)                 on(id, fn) → off()
     rows(tab?) → [def]          reset(tab?)                export() → { id: value }      import(obj)
     tabs                        ['audio', 'video', 'game', 'camera', 'controls', 'access']（音频 画面 游戏 镜头 操作 辅助）
     每行的值存在拥有者自己的 localStorage 键（store.key，JSON 对象，store.path 为其中的字段），读写一律 try / catch，
     存不了时退回内存中的值（隐私模式）。js/settings.js（界面）在此之上登记界面自己的行并渲染，不替换本对象。

   SG.Clock                      一天一个时钟（INTEGRATION §10.7；地图光照、城池视图、界面 --night 共用）
     hour() → 0–24               玩家回合：7 + 13 × 已用令牌 / 本月令牌；电脑回合与新闻 20.5；换月 6（黎明）
     monthStart(g, n?)           记下本月令牌数 n（缺省 g.tokens；StrategyAI.endMonth 结束时与开局 / 读档时调用）
     setPhase('day'|'ai'|'news'|'dawn')     set(h) / restore()（自己安排时间的演出：武林大会 10:00–20:00）
     night() → 0…1               由 hour() 推出的夜色（界面 --night 用）

   SG.Reg.<name>                 其余登记表（各拥有者模块的门面在其上扩展）：
     voiceCues  (AUDIO-V4 §B2，INTEGRATION §10.2)   stings (DOMESTIC-FX §8，§10.3)   icons (UI-V4 §3.6，§10.4)
     clips (COMBAT-V4 §13.1，§10.5)                 sfx (§10.6：前缀 ui- item- duel- fx- crowd-，重复即抛错)
     cards (ENGAGEMENT §5.1 定期卡的优先级)
     每个：def(id, value) → value（重复：unique 的表抛错，其余覆盖）、get(id)、has(id)、ids()、list()、size()、
           on(fn) → off()（有新条目时通知）、remove(id)
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});

  // ---------------------------------------------------------------- Events --
  if (!SG.Events) {
    const L = new Map();
    SG.Events = {
      on(name, fn) {
        if (typeof fn !== 'function') throw new Error('SG.Events.on: listener must be a function');
        if (!L.has(name)) L.set(name, []);
        L.get(name).push(fn);
        return () => { const a = L.get(name); if (!a) return; const i = a.indexOf(fn); if (i >= 0) a.splice(i, 1); };
      },
      has(name) { const a = L.get(name); return !!(a && a.length); },
      emit(name, a, b, c, d) {
        const arr = L.get(name);
        if (!arr || !arr.length) return;
        for (const fn of arr.slice()) {
          try { fn(a, b, c, d); } catch (e) { if (typeof console !== 'undefined') console.error('SG.Events ' + name, e); }
        }
      },
      clear(name) { if (name) L.delete(name); else L.clear(); },
    };
  }

  // ---------------------------------------------------------------- generic registry --
  function createRegistry(name, opts) {
    opts = opts || {};
    const map = new Map();
    const subs = [];
    const R = {
      name,
      def(id, value) {
        if (typeof id !== 'string' || !id) throw new Error('SG.Reg.' + name + ': id must be a non-empty string');
        if (opts.check) opts.check(id, value);
        if (map.has(id) && opts.unique) throw new Error('SG.Reg.' + name + ': duplicate id ' + id);
        map.set(id, value === undefined ? true : value);
        for (const fn of subs.slice()) { try { fn(id, map.get(id)); } catch (e) { if (typeof console !== 'undefined') console.error(e); } }
        return map.get(id);
      },
      get(id) { return map.get(id); },
      has(id) { return map.has(id); },
      remove(id) { return map.delete(id); },
      ids() { return Array.from(map.keys()); },
      list() { return Array.from(map.values()); },
      size() { return map.size; },
      on(fn) { subs.push(fn); return () => { const i = subs.indexOf(fn); if (i >= 0) subs.splice(i, 1); }; },
    };
    return R;
  }
  const SFX_PREFIX = ['ui-', 'item-', 'duel-', 'fx-', 'crowd-'];
  SG.Reg = SG.Reg || {};
  SG.Reg.create = createRegistry;
  const regs = {
    voiceCues: { unique: false },
    stings: { unique: false },
    icons: { unique: false },
    clips: { unique: false },
    cards: { unique: false },
    sfx: {
      unique: true,
      check(id) { if (!SFX_PREFIX.some(p => id.indexOf(p) === 0)) throw new Error('SG.Reg.sfx: id ' + id + ' must start with one of ' + SFX_PREFIX.join(' ')); },
    },
  };
  for (const k of Object.keys(regs)) if (!SG.Reg[k]) SG.Reg[k] = createRegistry(k, regs[k]);
  SG.Reg.SFX_PREFIX = SFX_PREFIX;

  // ---------------------------------------------------------------- Settings --
  function storage() {
    try {
      if (typeof localStorage === 'undefined' || !localStorage) return null;
      return localStorage;
    } catch (e) { return null; }
  }
  if (!SG.Settings) {
    const defs = new Map();     // id → def
    const mem = new Map();      // id → value (in memory; the truth when storage is refused)
    const subs = new Map();     // id → [fn]
    const TABS = ['audio', 'video', 'game', 'camera', 'controls', 'access'];
    const TAB_ZH = { audio: '音频', video: '画面', game: '游戏', camera: '镜头', controls: '操作', access: '辅助' };
    const readObj = key => {
      const st = storage(); if (!st) return null;
      try { const t = st.getItem(key); if (!t) return {}; const o = JSON.parse(t); return o && typeof o === 'object' ? o : {}; } catch (e) { return {}; }
    };
    const writeObj = (key, o) => {
      const st = storage(); if (!st) return false;
      try { st.setItem(key, JSON.stringify(o)); return true; } catch (e) { return false; }
    };
    const valid = (d, v) => {
      if (v === undefined || v === null) return false;
      if (d.type === 'seg') return !d.options || d.options.some(o => (Array.isArray(o) ? o[0] : o) === v);
      if (d.type === 'toggle') return typeof v === 'boolean';
      if (d.type === 'slider') return typeof v === 'number' && isFinite(v) && (d.min === undefined || v >= d.min) && (d.max === undefined || v <= d.max);
      return true;
    };
    const stored = d => {
      if (!d.store || !d.store.key) return undefined;
      const o = readObj(d.store.key);
      if (!o) return undefined;
      return d.store.path ? o[d.store.path] : o.value;
    };
    const fire = (id, v) => {
      const d = defs.get(id);
      if (d && typeof d.apply === 'function') { try { d.apply(v); } catch (e) { if (typeof console !== 'undefined') console.error(e); } }
      for (const fn of (subs.get(id) || []).slice()) { try { fn(v, id); } catch (e) { if (typeof console !== 'undefined') console.error(e); } }
    };
    const Settings = {
      TABS, TAB_ZH,
      define(def) {
        if (!def || typeof def.id !== 'string' || !def.id) throw new Error('SG.Settings.define: id required');
        const d = Object.assign({ tab: 'game', order: 100, type: 'seg' }, def);
        if (TABS.indexOf(d.tab) < 0) throw new Error('SG.Settings.define: unknown tab ' + d.tab + ' for ' + d.id);
        defs.set(d.id, d);
        return d;
      },
      has(id) { return defs.has(id); },
      def(id) { return defs.get(id) || null; },
      get(id) {
        const d = defs.get(id);
        if (!d) return mem.has(id) ? mem.get(id) : undefined;
        if (d.type === 'button') return undefined;
        const s = stored(d);
        if (valid(d, s)) return s;
        if (mem.has(id) && valid(d, mem.get(id))) return mem.get(id);
        return typeof d.default === 'function' ? d.default() : d.default;
      },
      set(id, v) {
        const d = defs.get(id);
        if (d && !valid(d, v)) return false;
        mem.set(id, v);
        if (d && d.store && d.store.key) {
          const o = readObj(d.store.key) || {};
          if (d.store.path) o[d.store.path] = v; else o.value = v;
          writeObj(d.store.key, o);
        }
        fire(id, v);
        return true;
      },
      on(id, fn) {
        if (!subs.has(id)) subs.set(id, []);
        subs.get(id).push(fn);
        return () => { const a = subs.get(id); const i = a ? a.indexOf(fn) : -1; if (i >= 0) a.splice(i, 1); };
      },
      // 当前可见的行（when() 为假的行不显示），按 tab 再 order 排列
      rows(tab) {
        const out = [];
        for (const d of defs.values()) {
          if (tab && d.tab !== tab) continue;
          let show = true;
          if (typeof d.when === 'function') { try { show = !!d.when(); } catch (e) { show = false; } }
          if (show) out.push(d);
        }
        out.sort((a, b) => TABS.indexOf(a.tab) - TABS.indexOf(b.tab) || a.order - b.order || (a.row || 0) - (b.row || 0) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
        return out;
      },
      all() { return Array.from(defs.values()); },
      reset(tab) {
        for (const d of defs.values()) {
          if (tab && d.tab !== tab) continue;
          if (d.type === 'button' || d.default === undefined) continue;
          Settings.set(d.id, typeof d.default === 'function' ? d.default() : d.default);
        }
      },
      export() { const o = {}; for (const d of defs.values()) if (d.type !== 'button') o[d.id] = Settings.get(d.id); return o; },
      import(obj) { if (!obj) return; for (const k of Object.keys(obj)) if (defs.has(k)) Settings.set(k, obj[k]); },
    };
    SG.Settings = Settings;
  }
  SG.Reg.settings = SG.Settings;

  // ---------------------------------------------------------------- Clock --
  if (!SG.Clock) {
    let phase = 'day', total = 0, override = null;
    const stack = [];
    const Clock = {
      DAY_START: 7, DAY_LEN: 13, NIGHT: 20.5, DAWN: 6,
      monthStart(g, n) { g = g || SG.G; total = n > 0 ? n | 0 : g ? Math.max(0, g.tokens | 0) : 0; phase = 'day'; },
      setPhase(p) { if (['day', 'ai', 'news', 'dawn'].indexOf(p) < 0) throw new Error('SG.Clock.setPhase: ' + p); phase = p; },
      phase() { return phase; },
      total() { return total; },
      hour() {
        if (override !== null) return override;
        if (phase === 'ai' || phase === 'news') return Clock.NIGHT;
        if (phase === 'dawn') return Clock.DAWN;
        const g = SG.G;
        if (!g || total <= 0) return Clock.DAY_START;
        const used = Math.max(0, Math.min(total, total - (g.tokens | 0)));
        return Clock.DAY_START + Clock.DAY_LEN * used / total;
      },
      // 自己安排时间的演出：set(h) 临时定住时钟，restore() 还原（可嵌套）
      set(h) { stack.push(override); override = +h; },
      restore() { override = stack.length ? stack.pop() : null; },
      // 夜色 0…1：18:00 起渐暗，19:30 全夜；黎明 5:00–7:00 渐亮
      night(h) {
        if (h === undefined) h = Clock.hour();
        if (h >= 19.5 || h < 5) return 1;
        if (h >= 18) return (h - 18) / 1.5;
        if (h < 7) return 1 - (h - 5) / 2;
        return 0;
      },
    };
    SG.Clock = Clock;
  }
})();
