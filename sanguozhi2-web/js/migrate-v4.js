'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 存档 v4 的迁移与新局初始化（INTEGRATION §6.1–§6.2）
   一个版本号 4 管全部第四版；各工作流登记自己的迁移函数，这里按固定次序调用：
     1 ITEMS（items）→ 2 TITLES（titles）→ 3 ENGAGEMENT（engage）→ 4 TOURNAMENT（tourney）→ 5 COMBAT（combat）/ DOMESTIC（domestic）
   （ITEMS 先行：TITLES 的电脑估值与 ENGAGEMENT 的声望要读装备。）
   载入：model.js 与各规则模块之后（INTEGRATION §11：… combos.js, migrate-v4.js, strategy-ai.js …）；
   模块也可以在本文件之前登记（SG.MigrateV4 不存在时先放进 SG.__migrateQueue = [[id, def], …]）。

   两种接法（任选其一；显式登记优先）：
   (a) 自动发现：模块对象 SG.Items / SG.Titles / SG.Engage / SG.Tourney / SG.Combos / SG.DomesticFx 上的
       migrate(G, from)、init(G)（或 setup(G)）、load(G, data)、enabled（布尔或函数）、saveKeys（数组）
   (b) SG.MigrateV4.register(id, def)     id ∈ ORDER；def = {
       migrate(G, from)   读 v2 / v3 存档时补齐本模块的状态（from = 2 | 3）
       init(G)            新局（GameState.newGame 末尾）；缺省 = migrate(G, 0)
       load(G, data)      读 v4 存档之后重建本模块的非存档数据（缓存、Map …）；data = 存档的 JSON 对象
       enabled()          返回假时整模块跳过（关掉的系统不建任何状态，INTEGRATION §1.2）
       keys: ['items']    本模块的顶层存档字段（G.items …）；登记到 GameState.addSaveKey
     }
   SG.MigrateV4.run(G, from)  → [id…]   v2 / v3 存档：按次序调用 migrate；G.migratedFrom 由 GameState 设定
   SG.MigrateV4.init(G)       → [id…]   新局
   SG.MigrateV4.load(G, data) → [id…]   v4 存档
   SG.MigrateV4.ORDER / RANK / list() / unregister(id)
   每个调用期间 SG.G 临时指向 G（模块的工具函数大多读 SG.G），结束后还原。
   一个模块抛错时记入 SG.MigrateV4.errors 并继续下一个（存档仍可读）；tests/save-v4.js 断言次序与跳过。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const ORDER = Object.freeze(['items', 'titles', 'engage', 'tourney', 'combat', 'domestic']);
  const RANK = Object.freeze({ items: 1, titles: 2, engage: 3, tourney: 4, combat: 5, domestic: 5 });
  const DISCOVER = Object.freeze({ items: 'Items', titles: 'Titles', engage: 'Engage', tourney: 'Tourney', combat: 'Combos', domestic: 'DomesticFx' });
  const mods = new Map();   // id → def
  let seq = 0;

  // 显式登记的模块，加上按名字发现的模块对象（没有显式登记时）
  function discovered(id) {
    const o = SG[DISCOVER[id]];
    if (!o || typeof o !== 'object' && typeof o !== 'function') return null;
    if (typeof o.migrate !== 'function' && typeof o.init !== 'function' && typeof o.setup !== 'function' && typeof o.load !== 'function') return null;
    return {
      _seq: 1000 + ORDER.indexOf(id),
      migrate: typeof o.migrate === 'function' ? (G, from) => o.migrate(G, from) : undefined,
      init: typeof o.init === 'function' ? G => o.init(G) : typeof o.setup === 'function' ? G => o.setup(G) : undefined,
      load: typeof o.load === 'function' ? (G, d) => o.load(G, d) : undefined,
      enabled: () => (typeof o.enabled === 'function' ? !!o.enabled() : o.enabled !== false),
      keys: Array.isArray(o.saveKeys) ? o.saveKeys : null,
    };
  }
  function sorted() {
    const out = [];
    for (const id of ORDER) {
      const d = mods.get(id) || discovered(id);
      if (!d) continue;
      if (Array.isArray(d.keys) && SG.GameState && SG.GameState.addSaveKey) for (const k of d.keys) SG.GameState.addSaveKey(k);
      out.push({ id, d });
    }
    return out.sort((a, b) => RANK[a.id] - RANK[b.id] || a.d._seq - b.d._seq);
  }
  function active(d) {
    if (typeof d.enabled !== 'function') return d.enabled !== false;
    try { return !!d.enabled(); } catch (e) { return false; }
  }
  function each(G, fnName, args) {
    const done = [];
    const prev = SG.G;
    SG.G = G;
    try {
      for (const { id, d } of sorted()) {
        if (!active(d)) continue;
        let fn = d[fnName];
        if (fnName === 'init' && typeof fn !== 'function' && typeof d.migrate === 'function') fn = g => d.migrate(g, 0);
        if (typeof fn !== 'function') continue;
        try { fn.apply(d, [G].concat(args || [])); done.push(id); } catch (e) {
          MigrateV4.errors.push({ id, phase: fnName, error: e });
          if (typeof console !== 'undefined') console.error('SG.MigrateV4 ' + fnName + ' ' + id, e);
        }
      }
    } finally { SG.G = prev; }
    return done;
  }

  const MigrateV4 = {
    ORDER, RANK,
    errors: [],
    register(id, def) {
      if (!(id in RANK)) throw new Error('SG.MigrateV4.register: unknown module id ' + id + ' (expected one of ' + ORDER.join(', ') + ')');
      if (!def || typeof def !== 'object') throw new Error('SG.MigrateV4.register: def must be an object');
      def._seq = mods.has(id) ? mods.get(id)._seq : ++seq;
      mods.set(id, def);
      if (Array.isArray(def.keys) && SG.GameState && SG.GameState.addSaveKey) for (const k of def.keys) SG.GameState.addSaveKey(k);
      return def;
    },
    unregister(id) { return mods.delete(id); },
    list() { return sorted().map(x => x.id); },
    has(id) { return mods.has(id) || !!discovered(id); },
    run(G, from) { return each(G, 'migrate', [from | 0]); },
    init(G) { return each(G, 'init'); },
    load(G, data) { return each(G, 'load', [data]); },
  };
  SG.MigrateV4 = MigrateV4;
  // 早于本文件登记的模块
  if (Array.isArray(SG.__migrateQueue)) { for (const [id, def] of SG.__migrateQueue) MigrateV4.register(id, def); SG.__migrateQueue = null; }
})();
