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
     6. 钩子路由（假的战斗 / 单挑类）：原方法照常调用、返回值原样；§2 的台词编号；§B9 名句：决定性胜利（城破 / 对方灭亡 / 对方君主在阵）、
        外交初见（每位君主一次）、天下统一（gameOver(true)）；非决定性胜利不说名句
     7. 运行时的 §B4.2：单挑双方两句相隔 > 3 秒仍然区分（say 与程序化喊叫都走同一规则），先说的一方保持原声
     8. §B11 程序化喊叫：空闲时段预先渲染（战斗的群呼与主将喊声、单挑双方的 5 种用力声），独立 LRU ≤ RULES.cryCacheBytes，
        预渲染后接敌时的群呼不再渲染
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ROOT = path.join(__dirname, '..');
const fails = [];
const ok = (c, m) => { if (!c) fails.push(m); return !!c; };

function loadVoice(withManifest, clock) {
  const ctx = { console, Math, JSON, Date, setTimeout, clearTimeout, performance: { now: clock || (() => Date.now()) } };
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

// ---------------------------------------------------------------- 7. §B4.2 at run time: a duel pair far apart in time --
let pairOk = '(skipped)';
const pairDone = (async () => {
  let T = 1000;
  const S = loadVoice(true, () => T);
  S.Voice.settings.set('frequency', 'all');
  // pairs whose base voices collide (same pack, rates within 0.02) and pairs whose rendered cry voices collide
  const P = n => S.Voice.profile(n);
  const names = gens.filter(n => P(n).sex === 'm');
  let pair = null, cryPair = null;
  for (let i = 0; i < names.length && !(pair && cryPair); i++) for (let j = i + 1; j < Math.min(names.length, i + 40); j++) {
    const a = P(names[i]), b = P(names[j]);
    if (!pair && a.pack === b.pack && Math.abs(a.rate - b.rate) < 0.02) pair = [names[i], names[j]];
    if (!cryPair && Math.abs(a.pitchK - b.pitchK) < 0.02) cryPair = [names[i], names[j]];
    if (pair && cryPair) break;
  }
  if (!ok(pair && cryPair, 'no colliding pair found for the §B4.2 run-time check')) return;
  // say() settles after the ≤ 120 ms decode wait (no audio in Node: 'drop no audio' carries the rate it would have used)
  const settle = () => new Promise(r => setTimeout(r, 200));
  const last = cue => S.Voice.log.filter(x => x.cue === cue && x.rate !== undefined).slice(-1)[0] || {};
  const o = { scene: 'duel', pair, force: true };
  S.Voice.say(pair[0], 'duel.parry', Object.assign({ side: 0 }, o)); await settle();
  T += 5000;                                                 // 5 s later (performance.now is in ms): outside the 3 s window
  S.Voice.say(pair[1], 'duel.crit', Object.assign({ side: 1 }, o)); await settle();
  const ra = last('duel.parry').rate, rb = last('duel.crit').rate;
  ok(ra === P(pair[0]).rate, `the first of the pair keeps his voice (${ra} vs ${P(pair[0]).rate})`);
  ok(Math.abs(ra - rb) >= 0.05 - 1e-9, `duel ${pair.join(' vs ')} 5 s apart: rates ${ra} / ${rb} (want ≥ 0.05 apart)`);
  // without the pair (a line 5 s later) the second keeps his own voice
  T += 5000;
  S.Voice.say(pair[1], 'duel.crit', { scene: 'duel', side: 1, force: true }); await settle();
  ok(last('duel.crit').rate === P(pair[1]).rate, 'outside a pair and the 3 s window the voice is not moved');
  // cries: the rendered voice (sex, pitchK) of the second moves away too; detune aside
  const oc = { scene: 'duel', pair: cryPair };
  S.Voice.cry(cryPair[0], 'hurt', Object.assign({ side: 0 }, oc));
  T += 5000;
  S.Voice.cry(cryPair[1], 'hurt', Object.assign({ side: 1 }, oc));
  const cl = S.Voice.log.filter(x => x.cue === '~hurt').slice(-2);
  const ka = cl[0] && cl[0].uk, kb = cl[1] && cl[1].uk;
  const pa = P(cryPair[0]).pitchK * ka, pb = P(cryPair[1]).pitchK * kb;
  ok(ka === 1 && Math.abs(pa - pb) >= 0.05 - 1e-9, `duel cries ${cryPair.join(' vs ')}: effective pitch ${pa && pa.toFixed(3)} / ${pb && pb.toFixed(3)} (want ≥ 0.05 apart)`);
  pairOk = `say pair ${pair.join('/')} rate ${ra} → ${rb} (5 s apart); cry pair ${cryPair.join('/')} pitch ${pa.toFixed(3)} → ${pb.toFixed(3)}`;
})().catch(e => { fails.push('pair harness threw: ' + e.message); });
// ---------------------------------------------------------------- 8. §B11 cries ahead, own LRU --
let cryOk = '(skipped)';
const cryDone = new Promise(resolve => {
  try {
    const S = loadVoice(false);
    const mk = (ch, len, sr) => { const d = Array.from({ length: ch }, () => new Float32Array(len)); return { numberOfChannels: ch, length: len, sampleRate: sr, duration: len / sr, getChannelData: i => d[i], copyToChannel(src, i) { d[i].set(src); } }; };
    const node = () => ({ connect() {}, disconnect() {}, start() {}, stop() {}, gain: { value: 1 }, playbackRate: { value: 1 } });
    S.Sfx = { context: { state: 'running', currentTime: 0, sampleRate: 48000, createBuffer: mk, createBufferSource: node, createGain: node } };
    const limit = S.VoiceData.RULES.cryCacheBytes;
    // job timings (the slices): wrap the renderer
    const C = S.VoiceCry; const jobMs = [];
    const oR = C.render; C.render = function () { const t = Date.now(); const r = oR.apply(this, arguments); jobMs.push(Date.now() - t); return r; };
    const oJ = C.crowdJob; C.crowdJob = function () { const j = oJ.apply(this, arguments); const st = j.step; j.step = function () { const t = Date.now(); const r = st.apply(this, arguments); jobMs.push(Date.now() - t); return r; }; return j; };
    const idleWait = () => new Promise(r => { const t = setInterval(() => { if (S.Voice._stats().warmQueued === 0) { clearInterval(t); setTimeout(r, 30); } }, 20); });
    S.Voice.warmBattle({ atk: [{ name: '关羽' }], def: [{ name: '颜良' }] });
    ok(S.Voice._stats().warmQueued >= 18, 'battle warm-up queued ' + S.Voice._stats().warmQueued + ' jobs (want 2 crowds × 9 + 2 cries)');
    idleWait().then(() => {
      const st1 = S.Voice._stats();
      ok(st1.cryKeys.includes('crowd|8:charge:0') && st1.cryKeys.includes('crowd|8:charge:1'), 'battle warm-up built both crowd variants: ' + st1.cryKeys.join(' '));
      ok(st1.cryKeys.includes('cry|关羽|shout') && st1.cryKeys.includes('cry|颜良|shout'), 'battle warm-up built the lead officers\' cries');
      ok(st1.cryBytes <= limit, `cry cache ${st1.cryBytes} B > ${limit}`);
      const before = st1.cryRenders;
      S.Voice.crowd('charge', 8, { variant: 1 });
      ok(S.Voice._stats().cryRenders === before, 'the clash crowd rendered again at contact after the warm-up');
      S.Voice.warmDuel('关羽', '颜良');
      return idleWait().then(() => {
        const st2 = S.Voice._stats();
        const want = ['heavy', 'hurt', 'light', 'charge', 'knockout'].flatMap(k => ['cry|关羽|' + k, 'cry|颜良|' + k]);
        ok(want.every(k => st2.cryKeys.includes(k)), 'duel warm-up missing: ' + want.filter(k => !st2.cryKeys.includes(k)).join(' '));
        ok(st2.cryBytes <= limit, `cry cache ${st2.cryBytes} B > ${limit} after the duel warm-up`);
        const r0 = st2.cryRenders;
        for (const k of ['heavy', 'hurt', 'light', 'charge', 'knockout']) { S.Voice.cry('关羽', k, { scene: 'duel' }); S.Voice.cry('颜良', k, { scene: 'duel' }); }
        ok(S.Voice._stats().cryRenders === r0, 'duel cries rendered again after the warm-up');
        // a crowd evicted by the duel is rebuilt by the next warm-up, never past the cap
        S.Voice.warmBattle(null);
        return idleWait().then(() => {
          const st3 = S.Voice._stats();
          ok(st3.cryBytes <= limit, `cry cache ${st3.cryBytes} B > ${limit} after the re-warm`);
          ok(st3.cryKeys.includes('crowd|8:charge:0'), 're-warm did not rebuild the crowd');
          jobMs.sort((a, b) => a - b);
          cryOk = `warm-up ${st3.warmJobs} jobs (median ${jobMs[jobMs.length >> 1]} ms, max ${jobMs[jobMs.length - 1]} ms per job, Node), cache ${(st2.cryBytes / 1024).toFixed(0)} KB after the duel / ${(st3.cryBytes / 1024).toFixed(0)} KB after the re-warm (cap ${limit / 1024} KB)`;
        });
      });
    }).then(resolve, e => { fails.push('cry warm-up harness: ' + e.message); resolve(); });
  } catch (e) { fails.push('cry harness threw: ' + e.message); resolve(); }
});

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
      main(s) { calls.push('main'); return new Promise(r => { this._end = r; }); }
      showCard(u) { calls.push('showCard'); }
      doTactic(u, tac, t) { calls.push('doTactic'); this.M.useTactic(u, tac, t); return Promise.resolve(); }
      aiPhase() { calls.push('aiPhase'); return 7; }
    }
    class Sim { constructor() { this.f = [{ gen: '关羽' }, { gen: '颜良' }]; } emit(e) { calls.push('emit:' + e.type); return e.type; } }
    class SS { gameOver(won) { calls.push('gameOver'); return 'go'; } }
    S.BattleModel = BM; S.BattleController = BC; S.DuelGame = { _Sim: Sim, active: null }; S.StrategyScreen = SS;
    // a tiny game for the strategy-level hooks: 刘备 (player) vs 曹操
    const gl = [{ id: 0, name: '刘备', faction: 0 }, { id: 1, name: '曹操', faction: 1 }, { id: 2, name: '关羽', faction: 0 }, { id: 3, name: '夏侯惇', faction: 1 }];
    S.G = { player: 0, year: 194, monthIndex: 5, generals: gl, cities: [{ owner: 0 }, { owner: 1 }, { owner: 1 }],
      factions: [{ ruler: 0, alive: true }, { ruler: 1, alive: true }], ruler(f) { return gl[this.factions[f].ruler]; } };
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
    // §B9: a won attack where the foe's ruler defended the city → 城破 line, then the player's ruler's signature
    const E = S.Events;
    const s1 = { attacker: 0, defender: 1, atk: [gl[2]], def: [gl[1], gl[3]] };
    E.emit('conquest.prepare', s1); E.emit('conquest.apply', s1, { attacker: 0, defender: 1, won: true });
    // not decisive: the foe's ruler was not there and his faction lives on
    const s2 = { attacker: 0, defender: 1, atk: [gl[2]], def: [gl[3]] };
    setTimeout(() => { E.emit('conquest.prepare', s2); E.emit('conquest.apply', s2, { attacker: 0, defender: 1, won: true }); }, 700);
    // diplomacy: the first envoy to 曹操 → his fanfare, his signature, his answer; the second → only the answer
    setTimeout(() => E.emit('command', 'ally', { f: 0, gen: 2, target: 1, ok: true }), 1300);
    setTimeout(() => E.emit('command', 'ally', { f: 0, gen: 2, target: 1, ok: false }), 2600);
    // unification
    setTimeout(() => { ok(new SS().gameOver(true) === 'go', 'wrapped gameOver returns the original result'); }, 3400);
    // clash contact during the enemy's phase of the player's battle: only the enemy commander's charge (§B10.3)
    setTimeout(() => {
      const bb = new BC(); bb.M.units = [{ gen: gl[1], commander: true }, { gen: gl[3] }, { gen: gl[2] }];
      bb.main({ attacker: 0, defender: 1, atk: [gl[2]], def: [gl[1], gl[3]] });
      bb.M.side = 1;
      S.Voice.onClashContact({ attacker: { gen: gl[3], side: 1 } });     // an enemy officer: dropped
      S.Voice.onClashContact({ attacker: { gen: gl[1], side: 1 } });     // the enemy commander: speaks
      bb.M.side = 0;
      setTimeout(() => { S.Voice.onClashContact({ attacker: { gen: gl[2], side: 0 } }); bb._end('done'); }, 900);   // the player's own attack
    }, 4200);
    setTimeout(() => {
      const cues = new Set(S.Voice.log.map(x => x.cue));
      for (const c of ['bat.select', 'bat.tactic', 'bat.tacticFail', 'duel.parry', 'duel.win', 'duel.lose'])
        ok(cues.has(c), 'hook routed no ' + c + ' (log: ' + [...cues].join(' ') + ')');
      ok(!cues.has('duel.guardcrush'), 'an off-screen duel simulation spoke');
      ok(calls.join(' ') === 'aiPhase showCard doTactic useTactic emit:parry emit:ko emit:guardcrush gameOver main', 'original methods not called in order: ' + calls.join(' '));
      const ch = S.Voice.log.filter(x => x.cue === 'charge' || (x.cue === 'charge' && x.action === 'drop')).map(x => x.speaker + ':' + (x.why || x.action));
      ok(ch.join(' ') === '夏侯惇:auto-side 曹操:no audio 关羽:no audio', 'clash contact in the enemy phase: ' + ch.join(' ') + ' (want 夏侯惇 dropped, 曹操 and 关羽 voiced)');
      const seq = S.Voice.log.filter(x => ['bat.cityTaken', 'sig', 'cmd.ally.yes', 'cmd.ally.no'].includes(x.cue)).map(x => x.cue + '@' + x.speaker);
      const want = ['bat.cityTaken@关羽', 'sig@刘备', 'bat.cityTaken@关羽', 'sig@曹操', 'cmd.ally.yes@曹操', 'cmd.ally.no@曹操', 'sig@刘备'];
      ok(seq.join(' ') === want.join(' '), 'signature routing: ' + seq.join(' ') + ' (want ' + want.join(' ') + ')');
      hooksOk = `hooks routed ${[...cues].join(' ')}; §B9 ${seq.join(' → ')}`;
      resolve();
    }, 6600);
  } catch (e) { fails.push('hook harness threw: ' + e.message); resolve(); }
});

Promise.all([hookDone, cryDone, pairDone]).then(() => {
console.log(`generals ${gens.length}; by lingua: ${Object.entries(byLingua).sort((a, b) => b[1] - a[1]).map(([k, v]) => k + ' ' + v).join(' · ')}`);
console.log(`female ${female} (${femaleF} on a female voice; others on the mild shift), pitch-shifted voices ${shifted}; duel pairs ${pairsN} (${adjusted} adjusted by the uniqueness rule)`);
console.log('random: ' + randomOk + (MF ? '' : '   (no voice/manifest.js: pack checks skipped)'));
console.log(hooksOk);
console.log('§B4.2 run time: ' + pairOk);
console.log('§B11 cries: ' + cryOk);
console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 40).join('\n  ') + (fails.length > 40 ? `\n  … ${fails.length - 40} more` : '') : 'ALL OK');
process.exitCode = fails.length ? 1 : 0;
});
