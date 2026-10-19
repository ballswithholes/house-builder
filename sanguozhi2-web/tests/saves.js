'use strict';
/* ==========================================================================
   tests/saves.js — 菜单的存档位（UI-V4 §6；js/menu-screen.js 的 SG.SaveSlots 纯逻辑 + js/model.js 的存档位接口）
   1 卡片：自动 + 一二三（空档也列出）、旧版记录追加在末尾、损坏的记录显示为空
   2 写入：playMs / 缩略图随索引、手动档成为 Ctrl+S 的快速档（存于 sanguozhi2_ui_v4.lastSlot）、自动档不改快速档
   3 配额：写不下时旧档不动、返回中文原因；不允许本地存档时 storageOk() = false
   4 删除 · 索引丢失时重建 · 读回与原局一致
   5 格式：fmtPlay / fmtTime / label / scenarioShort
   用法：node tests/saves.js        退出码 = 失败数
   ========================================================================== */
const { load } = require('./v4-load.js');

let fails = 0, checks = 0;
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } }
function eq(a, b, msg) { ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function section(name, fn) { console.log('== ' + name); try { fn(); } catch (e) { fails++; console.log('  FAIL (threw) ' + (e.stack || e)); } }

const SG = load({ v4: true, extra: ['menu-screen.js'] });
const { GameState, StrategyAI, SaveSlots } = SG;

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

section('0 module shape', () => {
  ok(!!SaveSlots, 'SG.SaveSlots defined in Node (menu-screen.js returns before the DOM part)');
  ok(!SG.MenuScreen, 'no SG.MenuScreen without a document');
  eq(SaveSlots.SLOTS.join(), 'auto,1,2,3', 'four slots (UI-V4 §6)');
});

section('1 cards: auto + three manual, empty ones listed, legacy appended', () => {
  const st = fakeStorage(); useStorage(st);
  let c = SaveSlots.cards();
  eq(c.length, 4, 'four cards with no saves');
  ok(c.every(x => x.empty), 'all empty');
  eq(c.map(x => x.label).join(), '自动,一,二,三', 'labels');
  const g = game('liubei', 7, 0);
  ok(SaveSlots.save(g, 2, { thumb: 'data:image/jpeg;base64,AAAA', playMs: 3723000 }).ok, 'save slot 2');
  c = SaveSlots.cards();
  eq(c.length, 4, 'still four cards');
  ok(!c[2].empty && c[1].empty && c[3].empty, 'slot 2 filled, others empty');
  eq(c[2].entry.playMs, 3723000, 'playMs in the index');
  eq(c[2].entry.thumb, 'data:image/jpeg;base64,AAAA', 'thumbnail in the index');
  eq(c[2].entry.factionName, g.playerFaction.name, 'faction name');
  // legacy (v3) save appended as a read-only card
  st.setItem('sanguozhi2_save_v3', JSON.stringify(Object.assign(JSON.parse(JSON.stringify(g)), { version: 3 })));
  c = SaveSlots.cards();
  const lg = c.find(x => x.slot === 'legacy');
  ok(!!lg && lg.legacy && c.indexOf(lg) === c.length - 1, 'legacy card appended last');
});

section('2 quick slot: last manual slot, persisted in sanguozhi2_ui_v4; auto does not change it', () => {
  const st = fakeStorage(); useStorage(st);
  const g = game('cao', 3, 1);
  eq(SaveSlots.quickSlot(), 2, 'remembered from section 1 (memory) or default');
  ok(SaveSlots.save(g, 3).ok, 'save slot 3');
  eq(SaveSlots.quickSlot(), 3, 'quick slot follows the last manual save');
  eq(JSON.parse(st.getItem('sanguozhi2_ui_v4')).lastSlot, 3, 'stored under sanguozhi2_ui_v4.lastSlot');
  ok(SaveSlots.save(g, 'auto').ok, 'auto save');
  eq(SaveSlots.quickSlot(), 3, 'auto save leaves the quick slot');
  SaveSlots.setQuickSlot(9);
  eq(SaveSlots.quickSlot(), 3, 'out-of-range quick slot ignored');
});

section('3 quota and refused storage', () => {
  const g = game('liubei', 11, 2);
  const size = JSON.stringify(g).length;
  const st = fakeStorage(Math.round(size * 1.6)); useStorage(st);
  ok(SaveSlots.save(g, 1).ok, 'first save fits');
  const before = st.getItem('sanguozhi2_v4_save_1');
  g.month = (g.month % 12) + 1;
  const r = SaveSlots.save(g, 2);
  ok(!r.ok, 'second save does not fit');
  ok(/存储空间不足/.test(r.error || ''), 'quota error in Chinese: ' + r.error);
  eq(st.getItem('sanguozhi2_v4_save_1'), before, 'old save untouched');
  ok(SaveSlots.storageOk(), 'probe still fits in the quota');
  useStorage(null);
  ok(!SaveSlots.storageOk(), 'no localStorage → storageOk() false');
  const r2 = SaveSlots.save(g, 1);
  ok(!r2.ok && /不允许/.test(r2.error || ''), 'refused storage → reason: ' + r2.error);
  eq(SaveSlots.cards().filter(c => !c.empty).length, 0, 'no cards readable without storage');
});

section('4 delete, index rebuild, load round trip', () => {
  const st = fakeStorage(); useStorage(st);
  const g = game('cao', 5, 3);
  ok(SaveSlots.save(g, 1).ok && SaveSlots.save(g, 'auto').ok, 'two saves');
  const t = JSON.stringify(g);
  st.removeItem('sanguozhi2_v4_saves');       // lose the index
  const c = SaveSlots.cards();
  ok(!c[0].empty && !c[1].empty, 'index rebuilt from the slot keys');
  const back = GameState.loadSlot(1);
  ok(!!back, 'loadSlot(1)');
  eq(JSON.stringify(back), t, 'loaded game equals the saved one');
  GameState.deleteSlot(1);
  ok(SaveSlots.cards()[1].empty, 'slot 1 empty after delete');
  ok(!SaveSlots.cards()[0].empty, 'auto kept');
  // broken JSON in a slot shows as empty / broken, never throws
  st.setItem('sanguozhi2_v4_save_2', '{broken');
  let threw = false;
  try { SaveSlots.cards(); } catch (e) { threw = true; }
  ok(!threw, 'broken slot does not throw');
});

section('5 formatting', () => {
  eq(SaveSlots.fmtPlay(0), '0 分', 'zero');
  eq(SaveSlots.fmtPlay(59 * 60000), '59 分', 'minutes');
  eq(SaveSlots.fmtPlay(6 * 3600000 + 3 * 60000), '6 小时 3 分', 'hours');
  const now = new Date(2026, 9, 19, 21, 30).getTime();
  eq(SaveSlots.fmtTime(new Date(2026, 9, 19, 9, 5).getTime(), now), '今天 09:05', 'today');
  eq(SaveSlots.fmtTime(new Date(2026, 9, 18, 20, 41).getTime(), now), '昨天 20:41', 'yesterday');
  eq(SaveSlots.fmtTime(new Date(2026, 9, 12, 8, 0).getTime(), now), '10月12日', 'this year');
  eq(SaveSlots.fmtTime(new Date(2025, 0, 2, 8, 0).getTime(), now), '2025年1月2日', 'other year');
  eq(SaveSlots.fmtTime(0, now), '', 'no time');
  eq(SaveSlots.label('auto') + SaveSlots.label(1) + SaveSlots.label(3) + SaveSlots.label('legacy'), '自动一三旧版', 'labels');
  eq(SaveSlots.scenarioShort('world'), '天下大势', 'world scenario');
  eq(SaveSlots.scenarioShort('classic'), '董卓专横', 'classic scenario');
});

console.log(fails ? fails + ' FAILED of ' + checks : 'ALL OK — ' + checks + ' checks');
process.exitCode = fails;
