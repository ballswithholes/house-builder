'use strict';
/* ==========================================================================
   tests/settings.js — 设置登记表（UI-V4 §7、§19.1；INTEGRATION §10.1）
   总表 53 行（编号唯一、行号 1–53、六页）；拥有者模块以桩代替后全部登记；默认值；经由假 localStorage 往返；
   损坏的 JSON → 默认值；抛错的存储（隐私模式）→ 内存值；export / import 恒等；reset(tab)；非法值被拒；
   拥有者未载入 → 该行不登记不显示；仅触屏 / 仅桌面的行；apply 即时生效；文字速度与长按时长的换算。
   用法：node tests/settings.js        退出码 = 失败数
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

let fails = 0, checks = 0;
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } }
function eq(a, b, msg) { ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function section(name, fn) { console.log('== ' + name); try { fn(); } catch (e) { fails++; console.log('  FAIL (threw) ' + (e.stack || e)); } }

const JS = path.join(__dirname, '..', 'js');
function fakeStorage(opts) {
  opts = opts || {};
  const store = {};
  return {
    store,
    getItem(k) { if (opts.throws) throw new Error('SecurityError'); return k in store ? store[k] : null; },
    setItem(k, v) { if (opts.throws) throw new Error('SecurityError'); store[k] = String(v); },
    removeItem(k) { if (opts.throws) throw new Error('SecurityError'); delete store[k]; },
  };
}
// a fresh context with registry.js + settings.js (+ optional owner stubs)
function boot(storage, opts) {
  opts = opts || {};
  const ctx = { console, Math, JSON, Date, setTimeout, clearTimeout };
  ctx.window = ctx; ctx.global = ctx;
  if (storage !== null) ctx.localStorage = storage;
  vm.createContext(ctx);
  ctx.SG = { isTouch: !!opts.touch };
  for (const f of ['registry.js', 'settings.js']) vm.runInContext(fs.readFileSync(path.join(JS, f), 'utf8'), ctx, { filename: f });
  const SG = ctx.SG;
  if (opts.owners) {
    // stubs of the sibling owners: audio rows as js/audio.js defines them, items' 阵斩, and the modules the bridges look for
    SG.Sfx = { uiOn: true };
    for (const r of SG.UISettings.TABLE) {
      if (r.owner !== 'audio') continue;
      const slider = /master|music$|sfx$|voice$/.test(r.id) && !/voiceLang|voiceFreq/.test(r.id);
      SG.Settings.define({ id: r.id, tab: r.tab, order: r.row, row: r.row, label: r.label, owner: 'audio',
        type: r.id === 'audio.readAloud' ? 'toggle' : slider ? 'slider' : 'seg', min: 0, max: 100, default: r.id === 'audio.readAloud' ? false : slider ? 100 : undefined,
        store: { key: 'sanguozhi2_audio_v4', path: r.id } });
    }
    SG.Settings.define({ id: 'items.slay', tab: 'game', order: 60, type: 'seg', label: '阵斩', options: [['ruler', '只限君主'], ['all', '所有武将'], ['off', '关']], default: 'ruler' });
    SG.Clash = { mode: 'on' };
    SG.DuelGame = {};
    SG.DomesticFx = { setSetting(k, v) { SG.DomesticFx.last = [k, v]; } };
    SG.Engage = { enabled: true };
    SG.Tourney = { enabled: true };
    SG.Gfx = null;
    SG.UISettings.bridge();
  }
  return SG;
}

// ------------------------------------------------------------------------------------------------- 1 --
section('1 the table: 53 rows, unique ids, rows 1–53, six tabs', () => {
  const SG = boot(fakeStorage());
  const T = SG.UISettings.TABLE;
  eq(T.length, 53, 'UI-V4 §7 + INTEGRATION §10.1: 53 rows');
  eq(new Set(T.map(r => r.id)).size, 53, 'ids unique');
  eq(T.map(r => r.row).sort((a, b) => a - b).join(), Array.from({ length: 53 }, (_, i) => i + 1).join(), 'row numbers 1..53');
  eq(SG.UISettings.TABS.join(), 'audio,video,game,camera,controls,access', 'six tabs');
  ok(T.every(r => SG.UISettings.TABS.indexOf(r.tab) >= 0), 'every row on a known tab');
  const counts = {};
  for (const r of T) counts[r.tab] = (counts[r.tab] || 0) + 1;
  eq(JSON.stringify(counts), JSON.stringify({ audio: 10, video: 7, game: 13, camera: 7, controls: 8, access: 8 }), 'rows per tab');
  ok(T.filter(r => r.owner === 'ui').length >= 30, 'UI owns most rows');
  ok(T.every(r => r.owner === 'ui' ? SG.Settings.has(r.id) : true), 'every UI row is defined at load');
  // every UI row has a default in the one config place
  ok(SG.UISettings.UI_ROWS.every(r => r.type === 'button' || SG.UISettings.DEFAULTS[r.id] !== undefined), 'DEFAULTS covers every UI row');
});

// ------------------------------------------------------------------------------------------------- 2 --
section('2 with every owner stubbed all 53 rows are registered; ordered by tab and row', () => {
  const SG = boot(fakeStorage(), { owners: true });
  const R = SG.Settings;
  const missing = SG.UISettings.TABLE.filter(r => !R.has(r.id)).map(r => r.id);
  eq(missing.join(), '', 'no table row missing');
  const n = SG.UISettings.rowCount();
  ok(n.defined >= 54, 'defined incl. items.slay (' + n.defined + ')');
  const rows = R.rows('audio').map(d => d.row);
  eq(rows.join(), '1,2,3,4,5,6,7,8,9,10', 'audio tab in row order');
  const game = R.rows('game').map(d => d.id);
  ok(game.indexOf('clash.mode') < game.indexOf('fx.domestic') && game.indexOf('fx.domestic') < game.indexOf('ui.ceremony'), 'game tab order 18 < 19 < 20');
  // desktop: the touch-only rows 39 / 40 are hidden, edge pan visible
  const vis = R.rows().map(d => d.id);
  ok(vis.indexOf('ui.touchSize') < 0 && vis.indexOf('ui.longPress') < 0 && vis.indexOf('cam.edgePan') >= 0, 'desktop: touch-only rows hidden');
  const SGt = boot(fakeStorage(), { owners: true, touch: true });
  const vt = SGt.Settings.rows().map(d => d.id);
  ok(vt.indexOf('ui.touchSize') >= 0 && vt.indexOf('ui.longPress') >= 0 && vt.indexOf('cam.edgePan') < 0, 'touch: touch rows visible, edge pan hidden');
  eq(SG.UISettings.bridge().length, 0, 'bridge() is idempotent');
});

// ------------------------------------------------------------------------------------------------- 3 --
section('3 owners not loaded: their rows do not appear', () => {
  const SG = boot(fakeStorage());
  SG.UISettings.bridge();
  for (const id of ['clash.mode', 'duel.keys', 'fx.domestic', 'engage.delegate', 'tourney.entry']) ok(!SG.Settings.has(id), id + ' not registered without its owner');
  ok(SG.Settings.has('ui.textSpeed') && SG.Settings.has('cam.shake'), 'UI rows present');
});

// ------------------------------------------------------------------------------------------------- 4 --
section('4 defaults, round trip through localStorage, owner keys', () => {
  const st = fakeStorage();
  let SG = boot(st, { owners: true });
  const R = SG.Settings, U = SG.UISettings;
  for (const r of U.UI_ROWS) if (r.type !== 'button') eq(R.get(r.id), U.DEFAULTS[r.id], 'default ' + r.id);
  eq(R.get('ui.textSpeed'), 'fast', '文字速度 default 快');
  eq(U.textCps(), 80, '快 = 80 字/秒');
  eq(U.longPressMs(), 450, '长按 0.45 秒');
  ok(R.set('ui.textSpeed', 'slow') && R.set('cam.zoomSpeed', 150) && R.set('ui.contrast', true), 'set three rows');
  eq(U.textCps(), 20, '慢 = 20 字/秒');
  const saved = JSON.parse(st.store[U.KEY]);
  eq(saved['ui.textSpeed'], 'slow', 'stored under sanguozhi2_ui_v4');
  eq(saved['cam.zoomSpeed'], 150, 'slider stored');
  ok(R.set('duel.keys', 'v3') && R.set('duel.hints', false), 'bridged duel rows set');
  eq(JSON.parse(st.store.sanguozhi2_duel_v4).keys, 'v3', 'duel row writes the owner key sanguozhi2_duel_v4.keys');
  ok(R.set('fx.domestic', 'fast'), 'fx.domestic set');
  eq(JSON.parse(st.store.sanguozhi2_fx_v4).domestic, 'fast', 'owner key sanguozhi2_fx_v4.domestic');
  eq(SG.DomesticFx.last.join(), 'domestic,fast', 'owner apply called');
  ok(R.set('clash.mode', 'fast'), 'clash mode');
  eq(SG.Clash.mode, 'fast', 'clash bridge applies to SG.Clash.mode');
  SG.Clash.mode = 'off';
  eq(R.get('clash.mode'), 'fast', 'set value wins in memory');
  // a new session reads the stored values
  SG = boot(st, { owners: true });
  eq(SG.Settings.get('ui.textSpeed'), 'slow', 'reload: text speed');
  eq(SG.Settings.get('cam.zoomSpeed'), 150, 'reload: zoom speed');
  eq(SG.Settings.get('ui.contrast'), true, 'reload: contrast');
  eq(SG.Settings.get('duel.keys'), 'v3', 'reload: duel keys');
  eq(SG.Settings.get('clash.mode'), 'on', 'clash mode is the owner\'s own (stub reset to on)');
});

// ------------------------------------------------------------------------------------------------- 5 --
section('5 bad values, corrupted JSON, private mode', () => {
  const st = fakeStorage();
  let SG = boot(st, { owners: true });
  eq(SG.Settings.set('ui.textSpeed', 'warp'), false, 'unknown option refused');
  eq(SG.Settings.set('cam.zoomSpeed', 400), false, 'slider out of range refused');
  eq(SG.Settings.set('ui.contrast', 'yes'), false, 'toggle needs a boolean');
  eq(SG.Settings.get('ui.textSpeed'), 'fast', 'value unchanged');
  st.store[SG.UISettings.KEY] = '{ not json';
  SG = boot(st, { owners: true });
  eq(SG.Settings.get('ui.textSpeed'), 'fast', 'corrupted JSON → default');
  ok(SG.Settings.set('ui.textSpeed', 'mid'), 'writing over corrupted JSON works');
  eq(JSON.parse(st.store[SG.UISettings.KEY])['ui.textSpeed'], 'mid', 'repaired');
  st.store[SG.UISettings.KEY] = JSON.stringify({ 'ui.textSpeed': 'bogus', 'cam.zoomSpeed': 'x' });
  SG = boot(st, { owners: true });
  eq(SG.Settings.get('ui.textSpeed'), 'fast', 'invalid stored value → default');
  eq(SG.Settings.get('cam.zoomSpeed'), 100, 'invalid stored slider → default');
  // private mode: storage throws on every call
  const bad = fakeStorage({ throws: true });
  SG = boot(bad, { owners: true });
  eq(SG.Settings.get('ui.motion'), 'full', 'throwing storage → default');
  ok(SG.Settings.set('ui.motion', 'lite'), 'set works in memory');
  eq(SG.Settings.get('ui.motion'), 'lite', 'in-memory value read back');
  // no storage object at all
  SG = boot(null, { owners: true });
  ok(SG.Settings.set('ui.hand', 'left') && SG.Settings.get('ui.hand') === 'left', 'no localStorage → memory');
});

// ------------------------------------------------------------------------------------------------- 6 --
section('6 export / import identity, reset(tab), subscriptions, apply', () => {
  const st = fakeStorage();
  const SG = boot(st, { owners: true });
  const R = SG.Settings;
  R.set('ui.fps', '30'); R.set('cam.fly', 'cut'); R.set('ui.colorAid', 'rg'); R.set('audio.music', 40);
  const ex = R.export();
  eq(ex['ui.fps'], '30', 'export has fps'); eq(ex['audio.music'], 40, 'export has audio row');
  const SG2 = boot(fakeStorage(), { owners: true });
  SG2.Settings.import(ex);
  eq(JSON.stringify(SG2.Settings.export()), JSON.stringify(ex), 'import(export()) identity');
  let seen = null;
  const off = R.on('cam.fly', v => { seen = v; });
  R.set('cam.fly', 'fast'); eq(seen, 'fast', 'on() fires'); off(); R.set('cam.fly', 'smooth'); eq(seen, 'fast', 'off() stops');
  R.reset('camera');
  eq(R.get('cam.fly'), 'smooth', 'reset(camera) → default');
  eq(R.get('ui.fps'), '30', 'reset(camera) leaves video rows');
  R.reset('video');
  eq(R.get('ui.fps'), '60', 'reset(video)');
  R.set('ui.sfx', false); eq(SG.Sfx.uiOn, false, 'ui.sfx applies to SG.Sfx.uiOn');
  R.set('ui.sfx', true); eq(SG.Sfx.uiOn, true, 'ui.sfx back on');
  SG.UISettings.applyAll();
  ok(true, 'applyAll without DOM does not throw');
  eq(R.get('ui.keymap'), undefined, 'button rows have no value');
  ok(typeof R.def('ui.tips').action.run === 'function' && typeof R.def('ui.keymap').action.run === 'function', 'button actions present');
});

section('7 one-line accessors for the owner modules (shake, AI speed, duel speed, watching, ceremony, flash)', () => {
  const SG = boot(fakeStorage(), { owners: true });
  const U = SG.UISettings, R = SG.Settings;
  eq(U.shakeK(), 1, 'shake: normal → 1'); R.set('cam.shake', 'low'); eq(U.shakeK(), 0.5, 'shake: 减弱 → 0.5'); R.set('cam.shake', 'off'); eq(U.shakeK(), 0, 'shake: 关 → 0');
  eq(U.aiSpeedK(), 1, 'AI speed: default 快 = the current speed'); R.set('ui.aiSpeed', 'normal'); eq(U.aiSpeedK(), 0.75, '正常 → 0.75'); R.set('ui.aiSpeed', 'vfast'); eq(U.aiSpeedK(), 1.5, '极快 → 1.5');
  eq(U.duelTimeK(), 1, 'duel speed: 100 %'); R.set('ui.duelSpeed', '85'); eq(U.duelTimeK(), 0.85, '85 %'); R.set('ui.duelSpeed', '70'); eq(U.duelTimeK(), 0.7, '70 %');
  eq(U.duelTimeK({ tourneyPlayer: true }), 1, 'a tournament match the player fights always runs at 100 % (UI-V4 §7 row 48)');
  eq(U.duelWatch(), 'highlights', 'duel watching default 精彩片段');
  eq(U.ceremonyShort(), false, 'ceremony default 完整'); R.set('ui.ceremony', 'short'); eq(U.ceremonyShort(), true, 'ceremony 简');
  eq(U.noFlash(), false, 'flash default 正常'); R.set('ui.flash', 'none'); eq(U.noFlash(), true, '无闪光');
});

console.log((fails ? 'FAILED ' + fails : 'ALL OK') + ' — ' + checks + ' checks');
process.exitCode = fails;
