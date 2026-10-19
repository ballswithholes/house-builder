'use strict';
/* ==========================================================================
   tests/suites.js — 君主乐章（AUDIO-V4 §A12、§3.1.1；INTEGRATION §13：地图与战斗循环的动机比例 ≥ 25 %）
   node tests/suites.js [--list] [--quick]
     1. 手写的 42 套：家族与武将文化一致；动机两两距离 ≥ 3（家传 ≥ 2）
     2. 冻结的生成表：tools/build-suites.js --check（已冻结的条目不会改变；名单里每个人都已冻结）；
        499 名武将全部有乐章；距离 ≥ 3；70 位起始君主的调色规则；全部 499 套的确定性哈希
     3. 生成与校验：42 套手写 + 每位起始君主 + 60 名抽样武将：每首曲目 SG.Music.validate 无错、乐器都存在、
        长度（clash 3.0–3.4 s、clash-short 1.5–1.7、victory 12–16、fanfare 3.0–3.9）、动机比例 ≥ 25 %（地图、战斗）
     4. 第 2 小节强拍在接触后 1.60 s（♩150）；clash-crit 的强奏在第 2 拍（0.4 s）
     5. 运行时接口：ensure / resolve / 最多 4 套 / pin / headCell / duel（4 对决斗动机曲目校验无错）/ 冻结表之外的武将运行时生成
     6. 冻结的八度表 OCT 与基础曲目一致；构建耗时
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const crypto = require('crypto');

const ROOT = path.join(__dirname, '..');
const list = process.argv.includes('--list');
const quick = process.argv.includes('--quick');
const fails = [];
const ok = (c, msg) => { if (!c) fails.push(msg); return !!c; };

function load() {
  const ctx = { console, Math, JSON, Date, performance: { now: () => Number(process.hrtime.bigint()) / 1e6 } };
  ctx.window = ctx; ctx.globalThis = ctx;
  vm.createContext(ctx);
  for (const f of ['core.js', 'music.js', 'music-inst.js', 'music-han.js', 'music-world.js', 'data.js', 'world-data.js', 'music-suites.js', 'music-suites-gen.js'])
    vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), ctx, { filename: f });
  return ctx.SG;
}
const SG = load();
const MS = SG.MusicSuites, MU = SG.Music;
const BS = require(path.join(ROOT, 'tools', 'build-suites.js'));
const t0 = Date.now();

// ---------------------------------------------------------------- 6. OCT table --
{
  const votes = {};
  for (const k of MU.keys()) { if (/[:|]/.test(k)) continue; const P = MU.get(k); for (const v of Object.values(P.voices || {})) { (votes[v.inst] = votes[v.inst] || {}); votes[v.inst][v.oct || 0] = (votes[v.inst][v.oct || 0] || 0) + 1; } }
  const out = {};
  for (const [i, o] of Object.entries(votes)) out[i] = +Object.entries(o).sort((a, b) => b[1] - a[1])[0][0];
  for (const [i, o] of Object.entries(out)) ok(MS.OCT[i] === o, `OCT table: ${i} is ${MS.OCT[i]}, base pieces vote ${o} (base music changed: update OCT in js/music-suites.js and re-run tools/build-suites.js --check)`);
}

// ---------------------------------------------------------------- 1. hand suites --
const { gens, fac } = BS.roster(SG);
const byName = Object.fromEntries(gens.map(g => [g.name, g]));
const rulers = new Set(Object.values(fac).map(f => f.ruler));
for (const [n, H] of Object.entries(MS.HAND)) {
  const g = byName[n];
  if (!ok(g, 'hand suite for unknown general ' + n)) continue;
  ok(H.family === (MS.FAMILY_OF[g.culture] || 'hua'), `hand suite ${n}: family ${H.family} ≠ culture ${g.culture} → ${MS.FAMILY_OF[g.culture]}`);
}
const hs = Object.entries(MS.HAND).map(([n, s]) => [n, MS.signature(s.motif)]);
for (let i = 0; i < hs.length; i++) for (let j = i + 1; j < hs.length; j++) {
  const d = MS.distance(hs[i][1], hs[j][1]);
  const house = MS.HAND[hs[i][0]].house === hs[j][0] || MS.HAND[hs[j][0]].house === hs[i][0];
  ok(d >= (house ? MS.D_MIN_HOUSE : MS.D_MIN), `hand motifs too close: ${hs[i][0]} ~ ${hs[j][0]} (d=${d})`);
}

// ---------------------------------------------------------------- 2. frozen table --
{
  const r = require('child_process').spawnSync(process.execPath, [path.join(ROOT, 'tools', 'build-suites.js'), '--check'], { encoding: 'utf8' });
  ok(r.status === 0, 'tools/build-suites.js --check failed:\n' + (r.stdout || '') + (r.stderr || ''));
  console.log('  ' + (r.stdout || '').trim().split('\n').join('\n  '));
}
const suites = {};
for (const g of gens) { const s = MS.suiteOf(g.name); if (ok(s, 'no suite for ' + g.name)) suites[g.name] = s; }
ok(Object.keys(suites).length === gens.length && gens.length >= 499, `suites for every general: ${Object.keys(suites).length} / ${gens.length}`);
ok(Object.keys(MS.runtime).length === 0, 'no scenario general may need a run-time suite: ' + Object.keys(MS.runtime).join(' '));
const sigs = Object.entries(suites).map(([n, s]) => [n, MS.signature(s.motif)]);
let minD = 99, minPair = '';
for (let i = 0; i < sigs.length; i++) for (let j = i + 1; j < sigs.length; j++) {
  const a = sigs[i][0], b = sigs[j][0];
  const house = suites[a].house === b || suites[b].house === a;
  const d = MS.distance(sigs[i][1], sigs[j][1]);
  if (house && d < MS.D_MIN_HOUSE) fails.push(`house motif too close to its founder: ${a} ~ ${b} (d=${d})`);
  if (!house && d < minD) { minD = d; minPair = a + ' ~ ' + b; }
  if (!house && d < MS.D_MIN) fails.push(`motifs too close: ${a} ~ ${b} (d=${d})`);
}
let pairs = 0, same = 0;
{
  const rs = [...rulers].filter(n => suites[n]);
  ok(rs.length === rulers.size, 'every starting ruler has a suite');
  for (let i = 0; i < rs.length; i++) for (let j = i + 1; j < rs.length; j++) {
    const a = suites[rs[i]], b = suites[rs[j]];
    if (a.family !== b.family) continue;
    pairs++;
    if (!MS.paletteDiffers(a, b, a.family)) { same++; fails.push(`palette identical: ${rs[i]} ~ ${rs[j]}`); }
  }
}
const hash = crypto.createHash('sha256').update(JSON.stringify(gens.map(g => [g.name, BS.entryJSON(suites[g.name])]))).digest('hex').slice(0, 16);

// ---------------------------------------------------------------- 3. build + validate --
const toBuild = Object.keys(suites).filter(n => MS.HAND[n] || rulers.has(n));
const others = Object.keys(suites).filter(n => !toBuild.includes(n));
for (let i = 0; i < (quick ? 20 : 60) && others.length; i++) toBuild.push(others[(i * 7919) % others.length]);
const durs = {}, shares = {};
let nKeys = 0, buildMs = 0, buildMax = 0;
for (const name of toBuild) {
  const S = suites[name];
  let pieces;
  const b0 = Number(process.hrtime.bigint()) / 1e6;
  try { pieces = MS.buildSuite(MU, name, S); } catch (e) { fails.push(name + ': build threw ' + e.message); continue; }
  const bm = Number(process.hrtime.bigint()) / 1e6 - b0; buildMs += bm; buildMax = Math.max(buildMax, bm);
  ok(Object.keys(pieces).length === MS.KINDS.length, `${name}: ${Object.keys(pieces).length} pieces (want ${MS.KINDS.length})`);
  for (const [kind, P] of Object.entries(pieces)) {
    const key = 'test:' + name + ':' + kind;
    for (const v of Object.values(P.voices)) ok(MU.INST[v.inst], key + ': unknown inst ' + v.inst);
    for (const v of Object.values(P.kit || {})) ok(MU.INST[v.inst], key + ': unknown kit inst ' + v.inst);
    MU.add(key, P); nKeys++;
    const err = MU.validate(key);
    ok(!(err && err.length), key + ': ' + (err || []).slice(0, 2).join(' / '));
    const d = MU.duration(key);
    (durs[kind] = durs[kind] || []).push(d ? d.total : NaN);
    if (kind === 'map' || kind === 'battle') { const sh = MS.motifShare(P); (shares[kind] = shares[kind] || []).push(sh); ok(sh >= 0.25, key + ` motif-derived bars ${(100 * sh).toFixed(0)} % < 25 %`); }
    if (kind === 'clash' || kind === 'clash-b' || kind === 'clash-crit') ok(d.total > 3.0 && d.total < 3.4, key + ' clash length ' + d.total.toFixed(2));
    if (kind === 'clash-short') ok(d.total > 1.5 && d.total < 1.7, key + ' short length ' + d.total.toFixed(2));
    if (kind === 'victory') ok(d.total > 12 && d.total < 16, key + ' victory length ' + d.total.toFixed(2));
    if (kind === 'fanfare') ok(d.total > 3.0 && d.total < 3.9, key + ' fanfare length ' + d.total.toFixed(2));
    if (kind === 'clash') {   // 4. 第 2 小节强拍 = 接触 + 1.60 s（♩150 两拍 0.4 s）
      const C = MU.compile(key), sec = C.sections[C.form[0]];
      ok(Math.abs(sec.time(4) - 1.6) < 1e-6, key + ' bar 2 downbeat at ' + sec.time(4).toFixed(3) + ' s (want 1.600)');
    }
    if (kind === 'clash-crit') { const C = MU.compile(key), sec = C.sections[C.form[0]]; ok(Math.abs(sec.time(1) - 0.4) < 1e-6, key + ' crit stab on beat 2 at 0.4 s'); }
    if (MU.remove) MU.remove(key); else delete MU.PIECES[key];
  }
  if (list) console.log((MS.HAND[name] ? 'H ' : 'G ') + name.padEnd(8, '　') + ' ' + S.family.padEnd(7) + ' ' + (S.temper || '').padEnd(11) + ' ' + S.lead.padEnd(7) + ' t' + S.tempo + ' tr' + S.tr + '  ' + S.motif);
}

// ---------------------------------------------------------------- 5. runtime API --
{
  ok(MS.ensure('曹操') && MU.get('suite:曹操:map') && MU.get('suite:曹操:clash-crit'), 'ensure registers suite:曹操:*');
  ok(MS.resolve('map', { suite: '曹操' }) === 'suite:曹操:map', 'resolve(map, 曹操)');
  ok(MS.resolve('clash-c', { suite: '曹操' }) === 'suite:曹操:clash-b', 'resolve maps clash-c to clash-b');
  ok(MS.resolve('title', { suite: '曹操' }) === null, 'resolve(title) is not a suite kind');
  ok(MS.resolve('battle', {}) === null, 'resolve without a suite → null');
  MS.pin('曹操');
  for (const n of ['刘备', '孙坚', '董卓', '康茂德', '卑弥呼']) MS.ensure(n);
  const al = MS.alive;
  ok(al.length <= MS.MAX_ALIVE, 'at most 4 suites alive: ' + al.join(' '));
  ok(al.includes('曹操'), 'pinned suite stays registered');
  ok(!MU.get('suite:刘备:map') && al.includes('卑弥呼'), 'oldest unpinned suite unregistered');
  ok(MU.keys().filter(k => /^suite:/.test(k)).length === al.length * MS.KINDS.length, 'registered suite keys = alive × 12');
  const hc = MS.headCell('曹操');
  ok(hc === '6,:0.5 3:0.5 6:1 5:1', 'headCell(曹操) = ' + hc);
  ok(MS.headCell('孙策').split(' ').slice(0, 3).join(' ') === MS.headCell('孙坚').split(' ').slice(0, 3).join(' ').replace(/5:1\.5/, '5:1'), 'house cell: 孙策 keeps 孙坚\'s head');
  const pairsD = [['关羽', '康茂德'], ['曹操', '刘备'], ['孙坚', '董卓'], ['吕布', '张飞']];
  for (const [a, b] of pairsD) {
    const k = MS.duel(a, b);
    if (!ok(k && MU.get(k), 'duel piece ' + a + '|' + b)) continue;
    const err = MU.validate(k); ok(!err.length, k + ': ' + err.slice(0, 2).join(' / '));
    const P = MU.get(k); ok(P.form.indexOf('Ma') >= 0 && P.form.indexOf('Mb') > P.form.indexOf('Ma') && P.loopFrom === 'Ma', k + ' form has Ma … Mb, loop from Ma');
  }
  ok(MU.keys().filter(k => /^suite-duel:/.test(k)).length === 1, 'only the last duel pair stays registered');
  // a general outside the frozen table (a mod): generated at run time, distinct, deterministic
  const mod = { name: '测试武将甲', war: 88, intel: 40, pol: 30, culture: 'roman', sex: 'm', born: 160 };
  const s1 = MS.suiteOf(mod);
  ok(s1 && s1.family === 'roman' && s1.generated, 'run-time suite for a general outside the frozen table');
  ok(MS.ensure(mod) && MU.get('suite:测试武将甲:battle') && MU.validate('suite:测试武将甲:battle').length === 0, 'run-time suite builds and validates');
  const sg = MS.signature(s1.motif);
  ok(sigs.every(x => MS.distance(sg, x[1]) >= MS.D_MIN), 'run-time suite is distinct from all 499');
  MS._resetCache();
  const s2 = MS.suiteOf(mod);
  ok(JSON.stringify(s1) === JSON.stringify(s2), 'run-time suite is deterministic');
}

// house cells (§A10): a generated clansman with house X starts with X's head cell
{
  let n = 0;
  for (const [name, S] of Object.entries(suites)) {
    if (!S.house || MS.HAND[name]) continue;
    n++;
    const h = MS.headCell(S.house), mine = MS.headCell(name);
    ok(h === mine, `house cell: ${name} starts ${mine}, founder ${S.house} ${h}`);
  }
  console.log(`house cells: ${n} generated clansmen keep their ruler's head cell`);
}

// ---------------------------------------------------------------- report --
console.log(`suites: ${Object.keys(suites).length} (${Object.keys(MS.HAND).length} hand-composed, ${Object.keys(MS.GEN).length} generated and frozen); determinism hash ${hash}`);
console.log(`distinctness: min distance ${minD} (${minPair}); threshold ${MS.D_MIN}; starting rulers ${rulers.size}, same-family pairs ${pairs}, palette-identical ${same}`);
console.log(`built ${toBuild.length} suites → ${nKeys} pieces validated; build ${(buildMs / toBuild.length).toFixed(2)} ms per suite (max ${buildMax.toFixed(1)})`);
for (const [k, a] of Object.entries(durs)) { const v = a.filter(x => !isNaN(x)); console.log(`  ${k.padEnd(14)} n=${String(v.length).padStart(3)}  ${Math.min(...v).toFixed(1)}–${Math.max(...v).toFixed(1)} s`); }
for (const [k, a] of Object.entries(shares)) console.log(`  motif-derived share of the ${k} loop: min ${(100 * Math.min(...a)).toFixed(0)} %, median ${(100 * a.slice().sort((x, y) => x - y)[a.length >> 1]).toFixed(0)} % (target ≥ 25 %)`);
console.log(`time ${((Date.now() - t0) / 1000).toFixed(1)} s`);
console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 40).join('\n  ') + (fails.length > 40 ? `\n  … ${fails.length - 40} more` : '') : 'ALL OK');
process.exitCode = fails.length ? 1 : 0;
