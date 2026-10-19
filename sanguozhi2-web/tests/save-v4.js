'use strict';
/* ==========================================================================
   tests/save-v4.js — 存档 v4（INTEGRATION §6.1–§6.2，UI-V4 §6 的存档位）
   v2 / v3 / v4 存档往返；迁移次序 ITEMS → TITLES → ENGAGEMENT → TOURNAMENT → COMBAT / DOMESTIC（显式登记与自动发现）、
   关掉的模块跳过、出错的模块不拦住其他模块；新局初始化；顶层字段只在有状态时写入、读档不丢未知字段；
   4 个存档位的写 / 读 / 删 / 列表、索引丢失时重建、1 MB 假存储的配额错误不动旧档、临时键回退；旧版记录只读。
   用法：node tests/save-v4.js        退出码 = 失败数
   ========================================================================== */
const { load } = require('./v4-load.js');

let fails = 0, checks = 0;
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } }
function eq(a, b, msg) { ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function section(name, fn) { console.log('== ' + name); try { fn(); } catch (e) { fails++; console.log('  FAIL (threw) ' + (e.stack || e)); } }

const SG = load({ v4: true });
const { GameState, StrategyAI, MigrateV4 } = SG;

// localStorage stand-in with an optional quota (characters)
function fakeStorage(limit) {
  const store = {};
  const size = () => Object.keys(store).reduce((s, k) => s + k.length + store[k].length, 0);
  return {
    store,
    getItem: k => (k in store ? store[k] : null),
    setItem(k, v) {
      v = String(v);
      const old = k in store ? k.length + store[k].length : 0;
      if (limit && size() - old + k.length + v.length > limit) { const e = new Error('The quota has been exceeded.'); e.name = 'QuotaExceededError'; throw e; }
      store[k] = v;
    },
    removeItem: k => { delete store[k]; },
    size,
  };
}
function useStorage(st) { if (st) SG.__ctx.localStorage = st; else delete SG.__ctx.localStorage; }
function game(key, seed, months) {
  SG.ScenarioData = SG.Scenarios.classic;
  SG.Random.seed(seed);
  const g = SG.G = GameState.newGame(key || 'liubei', 'classic');
  for (let m = 0; m < (months || 0); m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
  return g;
}

// ------------------------------------------------------------------------------------------------- 1 --
section('1 version 4 JSON: core keys only when every system is off; registered keys saved when present', () => {
  const g = game('cao', 3, 8);
  const d = JSON.parse(JSON.stringify(g));
  eq(d.version, 4, 'version 4'); eq(GameState.SAVE_KEY, 'sanguozhi2_save_v4', 'auto slot key (INTEGRATION §6.1)');
  eq(Object.keys(d).join(), 'version,scenario,year,month,player,tokens,seed,cities,generals,factions,alliance,log', 'no v4 keys without state');
  ok(['items', 'officeSeq', 'engage', 'tourney', 'combos', 'aiBudget'].every(k => GameState.saveKeys().indexOf(k) >= 0), 'INTEGRATION §6.1 keys pre-registered');
  g.tourney = { held: 190, count: 0, wins: { 关羽: 1 }, stats: {}, history: [] };
  g.items = { next: 3, inst: { 1: { id: 'qinggang', loc: 'gen', owner: 5 } } };
  const t = JSON.stringify(g);
  const back = GameState.fromJSON(t);
  eq(JSON.stringify(back), t, 'round trip with v4 keys');
  eq(back.tourney.wins['关羽'], 1, 'G.tourney.wins restored');
  eq(back.migratedFrom, 0, 'v4 save not flagged as migrated');
  // an unknown top-level key (a module this build does not have) survives load + save
  const d2 = JSON.parse(t); d2.futureSystem = { a: 1 };
  const b2 = GameState.fromJSON(JSON.stringify(d2));
  eq(JSON.parse(JSON.stringify(b2)).futureSystem.a, 1, 'unknown top-level key preserved');
  let threw = false; try { GameState.addSaveKey('cities'); } catch (e) { threw = true; } ok(threw, 'core keys cannot be registered');
  delete g.tourney; delete g.items;
});

// ------------------------------------------------------------------------------------------------- 2 --
section('2 migration order (explicit registration in scrambled order), skips, errors, v2 / v3 / v4 hooks', () => {
  const log = [];
  const mk = (id, extra) => Object.assign({
    migrate(G, from) { log.push('m:' + id + ':' + from + ':' + (SG.G === G)); },
    init(G) { log.push('i:' + id + ':' + (SG.G === G)); },
    load(G, data) { log.push('l:' + id + ':' + (data && data.version)); },
  }, extra || {});
  for (const id of ['tourney', 'domestic', 'items', 'combat', 'engage', 'titles']) MigrateV4.register(id, mk(id));
  eq(MigrateV4.list().join(), 'items,titles,engage,tourney,domestic,combat', 'sorted by rank (COMBAT / DOMESTIC share rank 5, registration order)');
  const prevG = SG.G;
  const g = game('liubei', 4, 0);
  eq(log.filter(x => x[0] === 'i').join(), 'i:items:true,i:titles:true,i:engage:true,i:tourney:true,i:domestic:true,i:combat:true', 'newGame: init in order with SG.G = the new game');
  void prevG;
  log.length = 0;
  const text = JSON.stringify(g);
  GameState.fromJSON(text);
  eq(log.join(), 'l:items:4,l:titles:4,l:engage:4,l:tourney:4,l:domestic:4,l:combat:4', 'v4 save: load hooks in order with the data');
  log.length = 0;
  const d3 = JSON.parse(text); d3.version = 3;
  const m3 = GameState.fromJSON(JSON.stringify(d3));
  eq(m3.migratedFrom, 3, 'v3 flagged'); eq(log.join(), 'm:items:3:true,m:titles:3:true,m:engage:3:true,m:tourney:3:true,m:domestic:3:true,m:combat:3:true', 'v3 save: migrate in order');
  log.length = 0;
  const d2 = JSON.parse(text); d2.version = 2;
  for (const x of d2.generals) { delete x.exp; delete x.level; delete x.specials; delete x.tactics; delete x.record; }
  const m2 = GameState.fromJSON(JSON.stringify(d2));
  eq(m2.migratedFrom, 2, 'v2 flagged'); eq(log[0], 'm:items:2:true', 'v2 save: migrate after the v3 growth fill');
  ok(m2.generals.every(x => x.level === 1 && Array.isArray(x.tactics)), 'v2 growth fields filled before the v4 migrators');
  // a switched-off module is skipped; a throwing module is recorded and the rest continue
  MigrateV4.register('titles', mk('titles', { enabled: () => false }));
  MigrateV4.register('engage', mk('engage', { migrate() { throw new Error('boom'); } }));
  log.length = 0; MigrateV4.errors.length = 0;
  GameState.fromJSON(JSON.stringify(d3));
  eq(log.join(), 'm:items:3:true,m:tourney:3:true,m:domestic:3:true,m:combat:3:true', 'disabled titles skipped, throwing engage skipped');
  eq(MigrateV4.errors.length, 1, 'the error is recorded'); eq(MigrateV4.errors[0].id, 'engage', 'error id');
  for (const id of ['tourney', 'domestic', 'items', 'combat', 'engage', 'titles']) MigrateV4.unregister(id);
  MigrateV4.errors.length = 0;
  let threw = false; try { MigrateV4.register('nope', {}); } catch (e) { threw = true; } ok(threw, 'unknown module id throws');
  // auto-discovery of module objects (SG.Items.migrate / init / setup / enabled)
  log.length = 0;
  SG.Items = { enabled: false, migrate: (G, f) => log.push('auto-items:' + f), setup: () => log.push('auto-setup'), saveKeys: ['items'] };
  SG.Tourney = { enabled: () => true, migrate: (G, f) => log.push('auto-tourney:' + f) };
  GameState.fromJSON(JSON.stringify(d3));
  eq(log.join(), 'auto-tourney:3', 'discovered: disabled SG.Items skipped, SG.Tourney migrated');
  SG.Items.enabled = true; log.length = 0;
  game('liubei', 5, 0);
  eq(log.join(), 'auto-setup,auto-tourney:0', 'discovered init: setup() for ITEMS, migrate(G, 0) default for TOURNAMENT');
  delete SG.Items; delete SG.Tourney;
});

// ------------------------------------------------------------------------------------------------- 3 --
section('3 refused saves', () => {
  const g = game('liubei', 6, 0);
  const d = JSON.parse(JSON.stringify(g));
  for (const [v, word] of [[1, '旧存档'], [5, '更新的版本'], [undefined, '旧存档']]) {
    const x = Object.assign({}, d, { version: v });
    let msg = ''; try { GameState.fromJSON(JSON.stringify(x)); } catch (e) { msg = e.message; }
    ok(msg.indexOf(word) >= 0, 'version ' + v + ' refused with “' + word + '” (' + msg + ')');
  }
  const bad = Object.assign({}, d, { scenario: 'nowhere' });
  let msg = ''; try { GameState.fromJSON(JSON.stringify(bad)); } catch (e) { msg = e.message; }
  ok(msg.indexOf('剧本') >= 0, 'unknown scenario refused');
});

// ------------------------------------------------------------------------------------------------- 4 --
section('4 slots: auto + 3 manual, index, load, delete, rebuild', () => {
  const st = fakeStorage(); useStorage(st);
  const g = game('cao', 7, 3);
  ok(g.saveSlot('auto') && g.save() && g.saveSlot(1, { playMs: 12345, thumb: 'data:image/jpeg;base64,xx' }), 'save auto and slot 1');
  StrategyAI.runAI([]); StrategyAI.endMonth();
  ok(g.saveSlot(2) && g.saveSlot('3'), 'save slots 2 and "3"');
  eq(g.saveSlot(4), false, 'slot 4 does not exist');
  ok(st.store['sanguozhi2_save_v4'] && st.store['sanguozhi2_v4_save_1'] && st.store['sanguozhi2_v4_save_2'] && st.store['sanguozhi2_v4_save_3'], 'slot keys');
  ok(!Object.keys(st.store).some(k => k.slice(-4) === '.tmp'), 'no temporary key left behind');
  const L = GameState.listSaves();
  eq(L.map(e => e.slot).join(), 'auto,1,2,3', 'listSaves order');
  const e1 = L.find(e => e.slot === 1);
  eq(e1.factionKey, 'cao', 'index factionKey'); eq(e1.factionName, g.factions[g.player].name, 'index faction name'); eq(e1.ruler, g.ruler(g.player).name, 'index ruler');
  eq(e1.playMs, 12345, 'index playMs'); eq(e1.thumb, 'data:image/jpeg;base64,xx', 'index thumb'); eq(e1.version, 4, 'index version');
  ok(e1.cities >= 1 && e1.generals >= 1 && e1.gold > 0 && e1.savedAt > 0, 'index counts and time');
  const b1 = GameState.loadSlot(1);
  ok(b1 && b1.month === e1.month && JSON.stringify(b1) === st.store['sanguozhi2_v4_save_1'], 'loadSlot(1) round trip');
  // index lost → rebuilt from the slots
  st.removeItem('sanguozhi2_v4_saves');
  const L2 = GameState.listSaves();
  eq(L2.map(e => e.slot).join(), 'auto,1,2,3', 'index rebuilt from the slot keys');
  eq(L2.find(e => e.slot === 2).factionKey, 'cao', 'rebuilt entry summary');
  ok(GameState.deleteSlot(2) && !st.store['sanguozhi2_v4_save_2'], 'deleteSlot(2)');
  eq(GameState.listSaves().map(e => e.slot).join(), 'auto,1,3', 'list after delete');
  // load(): auto first; without auto, the newest manual slot
  ok(GameState.load() && GameState.saveInfo().version === 4, 'load() reads the auto slot');
  st.removeItem('sanguozhi2_save_v4');
  const ln = GameState.load();
  ok(ln && GameState.hasSave(), 'without the auto slot load() reads the newest manual slot');
  useStorage(null);
});

// ------------------------------------------------------------------------------------------------- 5 --
section('5 quota: a save that does not fit leaves the old slot intact (1 MB fake storage)', () => {
  const g = game('liubei', 8, 2);
  const one = JSON.stringify(g).length;
  const st = fakeStorage(Math.floor(one * 2.5)); useStorage(st);   // room for two copies, not three
  ok(g.saveSlot(1), 'first save fits');
  ok(g.saveSlot(2), 'second save fits');
  const before = st.store['sanguozhi2_v4_save_1'];
  StrategyAI.runAI([]); StrategyAI.endMonth();
  eq(g.saveSlot(1), false, 'third copy (the temporary key) does not fit');
  eq(GameState.saveError, '存储空间不足：请删除一份记录', 'quota message (UI-V4 §6)');
  eq(st.store['sanguozhi2_v4_save_1'], before, 'old slot 1 unchanged');
  ok(!st.store['sanguozhi2_v4_save_1.tmp'], 'temporary key removed');
  ok(GameState.loadSlot(1), 'old slot 1 still loads');
  // with 1 MB and the classic save (~57 KB) all four slots fit
  const big = fakeStorage(1 << 20); useStorage(big);
  ok(['auto', 1, 2, 3].every(s => g.saveSlot(s)), 'four classic slots fit in 1 MB');
  // the final write failing after the temporary key: the slot is read from the temporary key
  const t = fakeStorage(); useStorage(t);
  const set = t.setItem.bind(t);
  t.setItem = (k, v) => { if (k === 'sanguozhi2_v4_save_3') throw new Error('disk full'); set(k, v); };
  ok(g.saveSlot(3), 'save reports success when the temporary copy is in place');
  ok(t.store['sanguozhi2_v4_save_3.tmp'] && GameState.loadSlot(3), 'loadSlot falls back to the temporary key');
  useStorage(null);
  eq(g.saveSlot(1), false, 'no storage → false'); eq(GameState.saveError, '此浏览器不允许本地存档', 'no storage message');
});

// ------------------------------------------------------------------------------------------------- 6 --
section('6 legacy saves: v3 / v2 keys are read-only, listed, loaded through migration', () => {
  const st = fakeStorage(); useStorage(st);
  const g = game('sunjian', 9, 4);
  const d = JSON.parse(JSON.stringify(g)); d.version = 3;
  const v3text = JSON.stringify(d);
  st.setItem('sanguozhi2_save_v3', v3text);
  ok(GameState.hasSave() && !GameState.hasLegacySave(), 'a v3 save counts as a save');
  eq(GameState.saveInfo().version, 3, 'saveInfo shows the v3 save');
  const L = GameState.listSaves();
  eq(L.length, 1, 'one entry'); eq(L[0].slot, 'legacy', 'listed as legacy'); eq(L[0].version, 3, 'legacy version 3');
  const m = GameState.loadSlot('legacy');
  ok(m && m.migratedFrom === 3, 'loadSlot(legacy) migrates');
  const m2 = GameState.load();
  ok(m2 && m2.migratedFrom === 3, 'load() migrates the v3 save');
  SG.G = m2; ok(m2.save(), 'save after migration');
  eq(st.store['sanguozhi2_save_v3'], v3text, 'the v3 key is never written');
  eq(GameState.saveInfo().version, 4, 'the v4 auto slot now wins');
  eq(GameState.listSaves().map(e => e.slot).join(), 'auto,legacy', 'auto + legacy listed');
  GameState.deleteSave();
  ok(!st.store['sanguozhi2_save_v4'] && !st.store['sanguozhi2_save_v3'], 'deleteSave removes the auto slot and the legacy keys');
  st.setItem('sanguozhi2_save', '{}');
  ok(GameState.hasLegacySave(), 'only a v1 save → legacy flag');
  eq(GameState.load(), null, 'v1 save not loadable'); ok(GameState.loadError.indexOf('旧存档') >= 0, 'v1 message');
  useStorage(null);
});

// ------------------------------------------------------------------------------------------------- 7 --
section('7 G.aiBudget purse is part of the save (INTEGRATION §5)', () => {
  const g = game('liubei', 10, 0);
  g.player = -1;
  const off = StrategyAI.onSpend('gear', () => {}, { owner: 'test' });
  for (let m = 0; m < 3; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); }
  off();
  ok(g.aiBudget && Object.keys(g.aiBudget.purse).length > 0, 'purse accrued');
  const back = GameState.fromJSON(JSON.stringify(g));
  eq(JSON.stringify(back.aiBudget), JSON.stringify(g.aiBudget), 'purse restored');
});

// ------------------------------------------------------------------------------------------------- 8 --
// A version-4 save written before a module landed (or while it was switched off, INTEGRATION §1.2) holds no state for it.
// MigrateV4.load gives such a module migrate(G, 4) (init(G) when it has no migrate) before its load, in the §6.2 order,
// and lists it in g.systemsAdded (the UI's 'new systems' card). A save that has the state only gets load.
section('8 v4 save without a module state: migrate(G, 4) before load, g.systemsAdded; with the state: load only', () => {
  const g = game('liubei', 12, 2);
  const textNoState = JSON.stringify(g);                       // what every phase-A/B save holds today (no G.items, no G.tourney)
  const log = [];
  MigrateV4.register('tourney', { keys: ['tourney'], migrate(G, from) { log.push('m:tourney:' + from + ':' + !!G.items); G.tourney = { held: G.year - 1, count: 0, wins: {}, stats: {}, history: [] }; }, load(G, d) { log.push('l:tourney:' + (d.tourney ? 'state' : 'none')); } });
  MigrateV4.register('items', { keys: ['items'], migrate(G, from) { log.push('m:items:' + from); G.items = { next: 1, inst: {} }; }, load(G, d) { log.push('l:items:' + (d.items === G.items ? 'state' : 'none')); } });
  MigrateV4.register('titles', { keys: ['officeSeq'], init(G) { log.push('i:titles'); G.officeSeq = 7; }, load(G, d) { log.push('l:titles:' + d.officeSeq); } });
  MigrateV4.register('engage', { load() { log.push('l:engage'); } });                       // no keys: cannot tell, load only
  MigrateV4.register('combat', { keys: ['combos'], enabled: () => false, migrate() { log.push('m:combat'); } });   // switched off: nothing
  const back = GameState.fromJSON(textNoState);
  eq(log.join(), 'm:items:4,l:items:state,i:titles,l:titles:7,l:engage,m:tourney:4:true,l:tourney:state',
    'missing state: migrate(G, 4) (or init) then load, in the INTEGRATION §6.2 order (TOURNAMENT after ITEMS)');
  eq(JSON.stringify(back.systemsAdded), JSON.stringify(['items', 'titles', 'tourney']), 'g.systemsAdded lists the modules that got their state now');
  eq(back.migratedFrom, 0, 'still a version-4 save (migratedFrom 0)');
  ok(back.items && back.tourney && back.officeSeq === 7, 'the module state exists after load');
  ok(Object.getOwnPropertyDescriptor(back, 'systemsAdded').enumerable === false, 'systemsAdded is not saved');
  // saved again: the state is in the save, so the next load calls load only
  log.length = 0;
  const again = GameState.fromJSON(JSON.stringify(back));
  eq(log.join(), 'l:items:state,l:titles:7,l:engage,l:tourney:state', 'with the state: load only');
  eq(again.systemsAdded.length, 0, 'nothing added the second time');
  // a v3 save still migrates with from = 3
  log.length = 0;
  const d3 = JSON.parse(textNoState); d3.version = 3;
  GameState.fromJSON(JSON.stringify(d3));
  eq(log.filter(x => x[0] === 'm' || x[0] === 'i').join(), 'm:items:3,i:titles,m:tourney:3:true', 'v3 save: migrate(G, 3) (init for a module without migrate)');
  for (const id of ['tourney', 'items', 'titles', 'engage', 'combat']) MigrateV4.unregister(id);
});

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
