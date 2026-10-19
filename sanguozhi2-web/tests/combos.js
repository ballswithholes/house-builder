'use strict';
/* ==========================================================================
   tests/combos.js — 招式谱数据与 SG.Combos（COMBAT-V4 §10.6、§12）（Node，无依赖，< 2 秒）
   (1) 两个剧本的 499 名武将都有招式谱；C1–C5、R1–R4；kStr ∈ [0.83, 1.03]；65 条手写（来源、终结技变体）；表载入 < 5 ms
   (2) 冻结：与 tests/refs/combo-data.v4.json 对照（改动须登记在 tools/combo-migrations.json）
   (3) SG.Combos.of 确定；兜底（表外武将）不与表里任何一串相同、本次运行内确定、同势力海明距离 ≥ 2
   (4) 伤害归一化：js/combos.js 的 BASE / FIN / SCALE 与 js/duel-game.js 的 STEP 表一致；全中 28 HP 的设计值
   (5) 字形与键位预设；见识（本势力恒全知、markSeen / markAll、describe 的「？」）；熟练星；存档迁移（G.combos）
   用法：node tests/combos.js        退出码 = 失败数
   ========================================================================== */
const path = require('path'), fs = require('fs'), vm = require('vm');
global.window = global;
const JS = path.join(__dirname, '..', 'js');
const load = f => vm.runInThisContext(fs.readFileSync(path.join(JS, f), 'utf8'), { filename: f });
for (const f of ['core.js', 'data.js', 'world-data.js', 'model.js', 'battle-model.js', 'specials-data.js', 'specials.js', 'tactics-data.js']) load(f);
const t0 = process.hrtime.bigint();
load('combo-data.js');
const tLoad = Number(process.hrtime.bigint() - t0) / 1e6;
load('combos.js');
load('migrate-v4.js');
load('duel-game.js');
const SG = global.SG, C = SG.Combos, D = SG.DuelGame;

let fails = 0, checks = 0;
function ok(cond, msg) { checks++; if (!cond) { fails++; console.log('  FAIL: ' + msg); } }
function section(name, fn) { console.log('== ' + name); try { fn(); } catch (e) { fails++; console.log('  FAILED: ' + (e.stack || e)); } }

section('(1) table: coverage, legality, distinctness, kStr', () => {
  const names = new Set();
  for (const sc of ['classic', 'world']) for (const l of SG.Scenarios[sc].Generals) names.add(l.split('|')[0]);
  const missing = [...names].filter(n => !C.has(n));
  ok(names.size === 499 && missing.length === 0, `all ${names.size} generals of both scenarios have a string (missing ${missing.length}: ${missing.slice(0, 5).join(' ')})`);
  const v = C.validate();
  for (const p of v.problems.slice(0, 10)) console.log('  ' + p);
  ok(v.ok, `C1–C5 and R1–R4 hold (${v.problems.length} problems)`);
  ok(v.legal === 790 && v.spare === 291, `790 legal strings, 291 spare (got ${v.legal}, ${v.spare})`);
  ok(v.kStrRange[0] >= 0.83 && v.kStrRange[1] <= 1.03, `kStr in [0.83, 1.03] (got ${v.kStrRange.map(x => x.toFixed(3)).join('–')})`);
  ok(v.r2 === 0 && v.factions >= 70, `R2: no two officers of a starting faction differ by one press (${v.factions} factions)`);
  // 每串第 1 步只能是 L / M（C1）；手写 65 条
  const hand = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'design-v4', 'combat', 'hand-combos.json'), 'utf8')).entries;
  const handOk = hand.filter(h => { const e = C.of(h.name); return e && e.src === 'hand' && e.seq === h.seq && e.finisher.name === h.finisher && e.finisher.variant === h.finisherVariant; });
  ok(hand.length === 65 && handOk.length === 65, `65 hand-made strings with their finisher names and variants (${handOk.length}/${hand.length})`);
  const all = C.all();
  ok(all.filter(e => e.src === 'gen').length === 434, `434 generated entries (got ${all.filter(e => e.src === 'gen').length})`);
  console.log(`  ${all.length} entries; examples: 吕布 ${C.glyphText(C.of('吕布').seq)} ${C.of('吕布').finisher.name}, 关羽 ${C.glyphText(C.of('关羽').seq)} ${C.of('关羽').finisher.name}; table load ${tLoad.toFixed(2)} ms`);
  ok(C.of('吕布').seq === 'MLHMHLHH' && C.of('关羽').seq === 'MMLHMLHH' && C.of('关平').seq.slice(0, 4) === 'MMLH', 'spec strings: 吕布 MLHMHLHH, 关羽 MMLHMLHH, 关平 inherits MMLH');
  ok(tLoad < 5, `table loads in < 5 ms (${tLoad.toFixed(2)} ms)`);
  const fin = { L: 0, M: 0, H: 0 }; for (const e of all) fin[e.seq[7]]++;
  console.log(`  finisher types L/M/H ${fin.L}/${fin.M}/${fin.H} (spec 107/222/170)`);
  ok(fin.L === 107 && fin.M === 222 && fin.H === 170, 'finisher type counts as in COMBAT-V4 §10.3');
});

section('(2) frozen against tests/refs/combo-data.v4.json', () => {
  const ref = JSON.parse(fs.readFileSync(path.join(__dirname, 'refs', 'combo-data.v4.json'), 'utf8')).entries;
  const mf = path.join(__dirname, '..', 'tools', 'combo-migrations.json');
  const allowed = fs.existsSync(mf) ? (JSON.parse(fs.readFileSync(mf, 'utf8')).changes || []).map(c => c.name) : [];
  const cur = SG.ComboData.entries;
  const changed = Object.keys(ref).filter(n => cur[n] !== ref[n] && allowed.indexOf(n) < 0);
  ok(changed.length === 0, `no frozen entry changed or removed without a migration (${changed.slice(0, 5).join(' ')})`);
  ok(Object.keys(cur).length >= Object.keys(ref).length, 'entries are only ever appended');
});

section('(3) of(): deterministic; runtime fallback', () => {
  ok(C.of('张飞') === C.of({ name: '张飞', war: 1 }) && C.of('张飞').seq === 'LLMHMMHH', 'table entries are cached and independent of the passed stats');
  const table = new Set(Object.values(SG.ComboData.entries).map(v => v.split('|')[0]));
  const finNames = new Set(Object.values(SG.ComboData.entries).map(v => v.split('|')[1]));
  const mods = [{ name: '模组甲', war: 88, intel: 40, pol: 30 }, { name: '模组乙', war: 55, intel: 80, pol: 70 }, { name: '模组丙', war: 70, intel: 60, pol: 50 }];
  const a = mods.map(g => C.of(g));
  ok(a.every(e => e && e.src === 'fallback' && !C.legal(e.seq) && !table.has(e.seq)), 'fallbacks are legal and never a string of the table');
  ok(new Set(a.map(e => e.seq)).size === 3 && a.every(e => !finNames.has(e.finisher.name)), 'fallbacks are distinct and their finisher names unique');
  ok(C.of(mods[1]) === a[1], 'a fallback is stable within the session');
  C._reset();
  const b = mods.map(g => C.of(g));
  ok(b.every((e, i) => e.seq === a[i].seq && e.finisher.name === a[i].finisher.name), 'fallbacks are deterministic per session (same query order → same strings)');
  // 同势力（运行时 gen.faction）海明 ≥ 2
  SG.ScenarioData = SG.Scenarios.classic; SG.Random.seed(3);
  const G = SG.G = SG.GameState.newGame('cao');
  C._reset();
  const shu = G.generals.find(g => g.name === '刘备').faction;
  const modGen = { name: '模组丁', war: 77, intel: 60, pol: 60, faction: shu };
  const e = C.of(modGen), mates = G.generals.filter(g => g.faction === shu).map(g => C.of(g).seq);
  ok(mates.length > 3 && mates.every(s => C.hamming(s, e.seq) >= 2), `a fallback keeps Hamming ≥ 2 to its faction's officers (${mates.length} officers)`);
  C._reset();
});

section('(4) damage normalisation agrees with the duel frame data', () => {
  const ST = D.STEP, MV = D.MOVES;
  ok(!!ST && !!MV && !!MV.L && !!MV.M, 'DuelGame exports STEP and the standalone L / M moves');
  if (!ST) return;
  ok(ST.L.dmg === C.BASE.L && ST.M.dmg === C.BASE.M && ST.H.dmg === C.BASE.H && ST.FL.dmg === C.FIN.L && ST.FM.dmg === C.FIN.M && ST.FH.dmg === C.FIN.H, 'BASE / FIN equal the string step damage of §7.2');
  ok(MV.L.dmg === C.BASE.L && MV.M.dmg === C.BASE.M, 'step 1 (neutral L / M) has the same base damage');
  const sc = D.COMBO && D.COMBO.SCALE;
  ok(!!sc && sc.join() === C.SCALE.join() && D.COMBO.FIN_FLOOR === C.FIN_FLOOR && D.COMBO.SPECIAL_FLOOR === C.SPECIAL_FLOOR, 'scaling curve and floors match');
  // 全中 8 击（同武力 76、无随机）：1 / 2 / 3 个重步 → 28.0 / 29.1 / 30.2
  const full = seq => { let d = 0; for (let i = 0; i < 8; i++) { const b = i < 7 ? C.BASE[seq[i]] : C.FIN[seq[7]]; d += D.damage(b * C.kStr(seq), 76, 76) * (i < 7 ? C.SCALE[i] : Math.max(C.FIN_FLOOR, C.SCALE[6])); } return d; };
  for (const [s, want] of [['LMLMHLMH', 28.0], ['LMHLMHLM', 29.12], ['LMHLHMHL', 30.24]]) {
    if (C.legal(s)) { ok(false, 'test string illegal ' + s); continue; }
    ok(Math.abs(full(s) - want) < 0.05, `full string ${s} at equal war 76 deals ${full(s).toFixed(2)} HP (want ${want})`);
  }
});

section('(5) glyphs, knowledge, describe, mastery, save', () => {
  const g = C.glyphs('MMLHMLHH', 'v4');
  ok(g.map(x => x.glyph).join('') === '◆◆●▲◆●▲▲' && g.map(x => x.key).join('') === 'KKJLKJLL', 'v4 glyphs and keys (J 轻 K 中 L 重)');
  ok(C.glyphs('MMLHMLHH', 'v3').map(x => x.key).join('') === 'LLJKLJKK', 'v3 habit preset: K heavy, L medium');
  ok(C.glyphText('MMLHMLHH') === '◆ ◆ ● ▲ ◆ ● ▲ · ▲', 'glyph text with the finisher after ·');
  SG.ScenarioData = SG.Scenarios.classic; SG.Random.seed(5);
  const G = SG.G = SG.GameState.newGame('cao');
  ok(G.combos && typeof G.combos.known === 'object' && typeof G.combos.mastery === 'object', 'new game gets G.combos = { known, mastery } (MigrateV4 init)');
  const me = G.generals.find(x => x.name === '曹操'), foe = G.generals.find(x => x.name === '吕布');
  ok(C.known(G, '曹操') === 255, 'own-faction generals are fully known');
  ok(C.known(G, '吕布') === 0, 'other generals start unknown');
  C.markSeen(G, '吕布', 0); C.markSeen(G, '吕布', 2);
  ok(C.known(G, '吕布') === 5, 'markSeen sets step bits');
  const d = C.describe(foe, G.player, G);
  ok(d.glyphs.join('') === '◆？▲？？？？？' && d.finisher.name === '？' && d.hint, `describe hides unseen steps as ？ (${d.glyphs.join('')})`);
  const dm = C.describe(me, G.player, G);
  console.log(`  describe 曹操: ${dm.glyphs.join(' ')} ${dm.finisher.name}, ≈ ${dm.totals.dmgPct}% of HP vs equal war, ${dm.totals.time ? dm.totals.time.toFixed(2) + ' s base' : '-'}; steps ${dm.steps.map(s => s.dmgPct + '%' + (s.tags[0] || '')).join(' ')}`);
  ok(dm.totals.allKnown && dm.totals.dmgPct >= 20 && dm.totals.dmgPct <= 40 && dm.totals.time >= 1.2 && dm.totals.time <= 1.7, 'own describe: totals ~28% and string time 1.26–1.62 s');
  C.markAll(G, '吕布');
  ok(C.known(G, '吕布') === 255, 'markAll (capture / recruit) makes a string fully known');
  for (let i = 0; i < 4; i++) C.addMastery(G, '曹操', { complete: true, allOnBeat: true });
  ok(C.mastery(G, '曹操').s === 1, '★ after a completed string');
  C.addMastery(G, '曹操', { complete: true, allOnBeat: true });
  ok(C.mastery(G, '曹操').s === 2, '★★ after 5 on-beat completions');
  C.addMastery(G, '曹操', { complete: true, vsKanpa: true });
  ok(C.mastery(G, '曹操').s === 3, '★★★ after completing against 看破');
  // 旧档迁移：没有 G.combos 的存档读入后补齐（空的见识）
  const old = { generals: [] };
  if (SG.MigrateV4) SG.MigrateV4.run(old, 3);
  ok(old.combos && Object.keys(old.combos.known).length === 0, 'a migrated v3 save gets empty combo knowledge');
  ok(SG.GameState.saveKeys().indexOf('combos') >= 0, 'G.combos is a save key');
});

console.log(`${checks} checks`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
