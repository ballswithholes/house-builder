'use strict';
/* ==========================================================================
   tests/voice-pick.js — 谁用什么声音说什么语言（AUDIO-V4 §3.1.3；§B3、§B4）
   node tests/voice-pick.js
     1. 两个剧本的全部 499 名武将：SG.Voice.profile(gen) 给出有语音包的语言（或其回退链上的语言）与一个基础声音；
        女将在有女声的语言里用 f；pitchK 在 0.86–1.12；两次独立载入结果逐项相同（确定性）
     2. §B3 的规则：起始势力的语言（文化相同）、文化的语言（汉将事鲜卑说汉语、鲜卑人事曹操说蒙古语代）、在野者按所在城细化、
        于阗 / 疏勒的俗语用塞人口音；「一律汉语」设置；传令官 / 商人
     3. duel-bots 名单里的每一对单挑：§B4.2 的区分规则成立（同一基础声音时有效音高至少差 0.05）
     4. pickLine：每位起始君主对每条台词都有可说的话（或程序化喊叫）；同一人同一台词不连续两次用同一变体；名句
     5. 人声不取 SG.Random：在一局经典剧本里跑 3 个月（钩子接上、指令事件照常发出），SG.Random 的调用序列与不接人声时逐位相同
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ROOT = path.join(__dirname, '..');
const fails = [];
const ok = (c, m) => { if (!c) fails.push(m); return !!c; };

function loadVoice(withManifest) {
  const ctx = { console, Math, JSON, Date, setTimeout, clearTimeout, performance: { now: () => Date.now() } };
  ctx.window = ctx; ctx.globalThis = ctx;
  vm.createContext(ctx);
  for (const f of ['js/core.js', 'js/registry.js', 'js/data.js', 'js/world-data.js', 'js/voice-data.js', 'js/voice-cry.js', 'js/voice.js'])
    vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), ctx, { filename: f });
  const mf = path.join(ROOT, 'voice', 'manifest.js');
  if (withManifest && fs.existsSync(mf)) vm.runInContext(fs.readFileSync(mf, 'utf8'), ctx, { filename: 'voice/manifest.js' });
  return ctx.SG;
}
const SG = loadVoice(true);
const V = SG.Voice, VD = SG.VoiceData, MF = SG.VoiceManifest;
const hasPack = id => !MF || !!MF.packs[id];

// ---------------------------------------------------------------- 1. every general --
const gens = [], seen = new Set();
for (const sc of ['classic', 'world']) for (const l of SG.Scenarios[sc].Generals) { const n = l.split('|')[0]; if (!seen.has(n)) { seen.add(n); gens.push(n); } }
ok(gens.length >= 499, 'roster ' + gens.length);
const profs = {};
const byLingua = {};
let female = 0, femaleF = 0, shifted = 0;
for (const n of gens) {
  const p = V.profile(n);
  profs[n] = p;
  const u = V.usableLingua(p.lingua);
  ok(u, `${n}: lingua ${p.lingua} has no usable pack (nor a fallback)`);
  ok(VD.LINGUA[p.lingua], `${n}: unknown lingua ${p.lingua}`);
  ok(p.voice && VD.LINGUA[p.lingua].voices[p.voice], `${n}: no base voice in ${p.lingua}`);
  ok(p.pack && hasPack(p.pack) || (MF && (MF.failed || []).includes(p.lingua)), `${n}: pack ${p.pack} missing`);
  ok(p.pitchK >= 0.86 && p.pitchK <= 1.12, `${n}: pitchK ${p.pitchK}`);
  ok(p.rate >= 0.75 && p.rate <= 1.26, `${n}: rate ${p.rate}`);
  ok(p.presence.f >= 1400 && p.presence.f <= 2800 && Math.abs(p.presence.gain) <= 5, `${n}: presence ${JSON.stringify(p.presence)}`);
  ok(p.grit >= 0 && p.grit <= 0.6, `${n}: grit ${p.grit}`);
  if (p.sex === 'f') { female++; if (VD.LINGUA[p.lingua].voices.f) { femaleF++; ok(p.voice === 'f', `${n}: female general on ${p.voice} although ${p.lingua} has a female voice`); } }
  if (Math.abs(p.rate - p.pitchK) > 1e-3) shifted++;
  byLingua[p.lingua] = (byLingua[p.lingua] || 0) + 1;
}
{
  const SG2 = loadVoice(true);
  const diff = gens.filter(n => JSON.stringify(SG2.Voice.profile(n)) !== JSON.stringify(profs[n]));
  ok(diff.length === 0, 'profiles not deterministic: ' + diff.slice(0, 5).join(' '));
}
// ---------------------------------------------------------------- 2. the lingua rules --
const L = n => V.linguaOf(n);
ok(L('曹操') === 'zh' && L('关羽') === 'zh', 'Han generals speak zh');
ok(L('卑弥呼') === 'ja', '卑弥呼 speaks ja');
ok(L('康茂德') === 'la', '康茂德 speaks la');
ok(L('沃洛吉斯') === 'fa', '沃洛吉斯 speaks fa');
ok(L('胡维色迦') === 'xbc', '胡维色迦 speaks Bactrian');
ok(L('魁头') === 'mn', '魁头 speaks Mongolic');
ok(L('於夫罗') === 'kk', '於夫罗 (Xiongnu) speaks the Turkic stand-in');
ok(L('区连') === 'ms', '区连 speaks Malay (Cham stand-in)');
ok(L('加达拉特') === 'gez', '加达拉特 speaks Geʽez');
ok(L('阿根托科克斯') === 'cy', 'the Caledonian speaks Welsh');
ok(L('菲利默') === 'got' && L('巴洛马尔') === 'got', 'Goths and Marcomanni speak Gothic');
{
  // a Han officer serving a non-Han faction speaks Chinese; a culture's lingua otherwise
  const W = SG.Scenarios.world;
  const fk = {}; for (const l of W.Factions) { const p = l.split('|'); fk[p[0]] = p[6]; }
  let hanAbroad = 0, checked = 0;
  for (const l of W.Generals) {
    const p = l.split('|'); const cul = p[9] || 'han';
    if (p[4] && p[4] !== '-' && fk[p[4]] && fk[p[4]] !== cul) { checked++; const lg = L(p[0]); const want = VD.CULTURE_LINGUA[cul] || 'zh'; if (lg !== want) fails.push(`${p[0]} (${cul} serving ${p[4]}): ${lg}, want ${want}`); if (cul === 'han') hanAbroad++; }
  }
  console.log(`  generals serving a faction of another culture: ${checked} (Han abroad ${hanAbroad}) — all speak their own culture's lingua`);
  // Saka-accented Prakrit
  const kh = W.Generals.map(l => l.split('|')).find(p => p[4] === 'khotan');
  if (kh) { const p = V.profile(kh[0]); ok(p.lingua === 'pra' && p.voice === 'saka', `${kh[0]} (Khotan): ${p.lingua} ${p.voice}, want pra saka`); }
  const kr = W.Generals.map(l => l.split('|')).find(p => p[4] === 'kroraina');
  if (kr) { const p = V.profile(kr[0]); ok(p.lingua === 'pra' && p.voice === 'm', `${kr[0]} (Kroraina): ${p.voice}`); }
}
// 「一律汉语」
V.settings.set('language', 'zh');
ok(L('康茂德') === 'zh' && V.pickLine('康茂德', 'charge').lingua === 'zh', '一律汉语: everyone speaks Chinese');
V.settings.set('language', 'native');
ok(L('康茂德') === 'la', 'back to native');
for (const who of ['herald', 'merchant']) for (const lg of Object.keys(VD.LINGUA)) {
  const p = V.profile(who, lg);
  ok(p.voice && VD.LINGUA[lg].voices[p.voice], `${who} in ${lg}: no voice`);
  if (VD.LINGUA[lg].conf === 'A') ok(p.pack, `${who} in ${lg}: no pack`);
}
// ---------------------------------------------------------------- 3. duel pairs (§B4.2) --
let pairsN = 0, adjusted = 0;
{
  const src = fs.readFileSync(path.join(ROOT, 'tests', 'duel-bots.js'), 'utf8');
  const pairs = new Set();
  for (const m of src.matchAll(/\['([一-鿿]{1,6})',\s*'([一-鿿]{1,6})'/g)) if (seen.has(m[1]) && seen.has(m[2])) pairs.add(m[1] + '|' + m[2]);
  // every classic ruler against his fiercest officer, and the world rulers against their neighbours in the roster
  for (const sc of ['classic', 'world']) {
    const G = SG.Scenarios[sc].Generals.map(l => l.split('|'));
    for (const l of SG.Scenarios[sc].Factions) {
      const p = l.split('|'); const own = G.filter(x => x[4] === p[0] && x[0] !== p[2]).sort((a, b) => +b[1] - +a[1]);
      if (own[0]) pairs.add(p[2] + '|' + own[0][0]);
    }
  }
  for (const pr of pairs) {
    const [a, b] = pr.split('|');
    const pa = V.profile(a), pb = V.profile(b);
    const rb = V.distinctRate(Object.assign({}, pb), [Object.assign({}, pa, { rateEff: pa.rate })]);
    pairsN++;
    if (rb !== pb.rate) adjusted++;
    if (pa.pack === pb.pack) ok(Math.abs(rb - pa.rate) >= 0.05 - 1e-9, `duel ${a} vs ${b}: same voice ${pa.pack}, rates ${pa.rate} / ${rb}`);
  }
}
// ---------------------------------------------------------------- 4. pickLine --
{
  const rulers = new Set();
  for (const sc of ['classic', 'world']) for (const l of SG.Scenarios[sc].Factions) rulers.add(l.split('|')[2]);
  let lines = 0, cries = 0;
  for (const r of rulers) for (const id of Object.keys(VD.CUES)) {
    const p = V.pickLine(r, id);
    if (!ok(p && (p.text || p.cry), `${r}: nothing to say for ${id}`)) continue;
    if (p.text) { lines++; ok(p.clipId && p.pack, `${r} ${id}: a line without a clip id`); if (p.lingua !== 'zh') ok(p.zh, `${r} ${id}: foreign line without Chinese meaning`); } else cries++;
  }
  // never the same variant twice in a row
  let prev = null, rep = 0;
  for (let i = 0; i < 30; i++) { const p = V.pickLine('曹操', 'charge'); if (p.variant === prev) rep++; prev = p.variant; }
  ok(rep === 0, 'same variant twice in a row ' + rep + '×');
  for (const r of Object.keys(VD.SIGNATURE)) { const p = V.pickLine(r, 'sig'); ok(p && p.text && p.clipId === 'sig#' + r, 'signature line for ' + r); }
  console.log(`  starting rulers × cues: ${lines} spoken lines, ${cries} procedural cries`);
}
// ---------------------------------------------------------------- 5. no SG.Random --
let randomOk = '(skipped)';
{
  const { load } = require('./v4-load.js');
  const run = withVoice => {
    const S = load({ v4: true });
    const ctx = S.__ctx;
    if (withVoice) for (const f of ['js/voice-data.js', 'js/voice-cry.js', 'js/voice.js']) vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), ctx, { filename: f });
    const R = S.Random;
    const seq = [];
    for (const k of Object.keys(R)) if (typeof R[k] === 'function') { const o = R[k]; R[k] = function () { const v = o.apply(this, arguments); seq.push(k + ':' + (typeof v === 'number' ? v.toFixed(9) : v)); return v; }; }
    if (R.setSeed) R.setSeed(12345); else if (R.seed) R.seed(12345);
    const g = S.G = S.GameState.newGame('liubei', 'classic');
    if (withVoice) { S.Voice.init(); S.Voice.hooks.install(); }
    for (let m = 0; m < 3; m++) {
      const c = g.citiesOf(g.player)[0];
      const gen = g.officersIn(c)[0];
      if (gen) { S.Commands.develop(c, gen, S.DevKind.Land); S.Commands.train(gen); S.Commands.search(c, gen); }
      S.StrategyAI.endMonth(g);
    }
    return { seq, hash: JSON.stringify(g.cities.map(c => [c.gold, c.food, c.land, c.owner])) };
  };
  try {
    const a = run(false), b = run(true);
    ok(a.seq.length > 0, 'the campaign drew no random numbers (harness broken)');
    ok(a.seq.length === b.seq.length && a.seq.every((x, i) => x === b.seq[i]), `SG.Random sequence changed with voices on (${a.seq.length} vs ${b.seq.length} draws)`);
    ok(a.hash === b.hash, 'state changed with voices on');
    randomOk = `${a.seq.length} SG.Random draws over 3 months, identical with voices on`;
  } catch (e) { fails.push('random harness threw: ' + e.message); }
}

// ---------------------------------------------------------------- 6. hook routing (fake battle / duel classes) --
// the wrappers must call the original, pass its result through, and route the cues of AUDIO-V4 §2 (no audio in Node: 'drop no audio')
let hooksOk = '(skipped)';
const hookDone = new Promise(resolve => {
  try {
    const S = loadVoice(false);
    const calls = [];
    class BM { useTactic(u, t, tgt) { calls.push('useTactic'); return { success: t.ok }; } }
    class BC {
      constructor() { this.M = new BM(); this.playerSide = 0; this.awaitingInput = true; }
      main(s) { calls.push('main'); return Promise.resolve('done'); }
      showCard(u) { calls.push('showCard'); }
      doTactic(u, tac, t) { calls.push('doTactic'); this.M.useTactic(u, tac, t); return Promise.resolve(); }
      aiPhase() { calls.push('aiPhase'); return 7; }
    }
    class Sim { constructor() { this.f = [{ gen: '关羽' }, { gen: '颜良' }]; } emit(e) { calls.push('emit:' + e.type); return e.type; } }
    S.BattleModel = BM; S.BattleController = BC; S.DuelGame = { _Sim: Sim, active: null };
    S.Voice.settings.set ? S.Voice.settings.set('frequency', 'all') : (S.Voice.settings.frequency = 'all');
    S.Voice.init(); S.Voice.hooks.install();
    const bc = new BC();
    const u = { gen: '关羽', side: 0 }, foe = { gen: '颜良', side: 1 };
    ok(bc.aiPhase() === 7, 'wrapped aiPhase returns the original result');
    bc.showCard(u);
    bc.doTactic(u, { name: '伪报', ok: false }, foe);
    const sim = new Sim(); S.DuelGame.active = { sim };
    ok(sim.emit({ type: 'parry', def: 0 }) === 'parry', 'wrapped emit returns the original result');
    sim.emit({ type: 'ko', who: 1, winner: 0 });
    const off = new Sim(); off.emit({ type: 'guardcrush', att: 1 });   // an off-screen simulation: silent
    setTimeout(() => {
      const cues = new Set(S.Voice.log.map(x => x.cue));
      for (const c of ['bat.select', 'bat.tactic', 'bat.tacticFail', 'duel.parry', 'duel.win', 'duel.lose'])
        ok(cues.has(c), 'hook routed no ' + c + ' (log: ' + [...cues].join(' ') + ')');
      ok(!cues.has('duel.guardcrush'), 'an off-screen duel simulation spoke');
      ok(calls.join(' ') === 'aiPhase showCard doTactic useTactic emit:parry emit:ko emit:guardcrush', 'original methods not called in order: ' + calls.join(' '));
      hooksOk = `hooks routed ${[...cues].join(' ')}`;
      resolve();
    }, 2600);
  } catch (e) { fails.push('hook harness threw: ' + e.message); resolve(); }
});

hookDone.then(() => {
console.log(`generals ${gens.length}; by lingua: ${Object.entries(byLingua).sort((a, b) => b[1] - a[1]).map(([k, v]) => k + ' ' + v).join(' · ')}`);
console.log(`female ${female} (${femaleF} on a female voice; others on the mild shift), pitch-shifted voices ${shifted}; duel pairs ${pairsN} (${adjusted} adjusted by the uniqueness rule)`);
console.log('random: ' + randomOk + (MF ? '' : '   (no voice/manifest.js: pack checks skipped)'));
console.log(hooksOk);
console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 40).join('\n  ') + (fails.length > 40 ? `\n  … ${fails.length - 40} more` : '') : 'ALL OK');
process.exitCode = fails.length ? 1 : 0;
});
