'use strict';
/* ==========================================================================
   tests/voice-lines.js — 台词登记表与语音包（AUDIO-V4 §3.1.2；INTEGRATION §10.2、§13；DESIGN-V4 §19「所有规格提到的台词编号都存在」）
   node tests/voice-lines.js
     1. js/voice-data.js 与 design-v4/audio/voice-lines.js 一致（tools/voice-build/gen-voice-data.js --check）
     2. 每种语言都有 20 条核心台词；每种 A 级语言都有 26 条「开始 / 大成」台词；每条扩展台词的回退链终于核心台词或喊叫
     3. 规格文档里出现的每个台词编号都在登记表里（扫描 design-v4/*.md）
     4. 传令官 / 商人在每种 A 级语言里都找得到声音；每个势力都对应一种语言；名句的语言存在且外语名句有中文释义
     5. 语音包（voice/manifest.js）：每种语言的包都在（或该语言 QA 失败、走回退链）；每个包里核心台词的每个有词的变体都有片段；
        片段的偏移 / 长度在 base64 数据之内；总量与每局下载预算（AUDIO-V4 §B10.6）
     6. 登记表并入 SG.Reg.voiceCues
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ROOT = path.join(__dirname, '..');
const fails = [];
const ok = (c, m) => { if (!c) fails.push(m); return !!c; };

const ctx = { console, Math, JSON, Date, setTimeout, clearTimeout, performance: { now: () => Date.now() } };
ctx.window = ctx; ctx.globalThis = ctx;
vm.createContext(ctx);
for (const f of ['js/core.js', 'js/registry.js', 'js/data.js', 'js/world-data.js', 'js/voice-data.js', 'js/voice-cry.js', 'js/voice.js'])
  vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), ctx, { filename: f });
const manifestFile = path.join(ROOT, 'voice', 'manifest.js');
if (fs.existsSync(manifestFile)) vm.runInContext(fs.readFileSync(manifestFile, 'utf8'), ctx, { filename: 'voice/manifest.js' });
const SG = ctx.SG, VD = SG.VoiceData, MF = SG.VoiceManifest;

// 1. generated data up to date
{
  const r = require('child_process').spawnSync(process.execPath, [path.join(ROOT, 'tools', 'voice-build', 'gen-voice-data.js'), '--check'], { encoding: 'utf8' });
  ok(r.status === 0, 'js/voice-data.js is stale: ' + (r.stdout || '') + (r.stderr || ''));
}
// 2. coverage
for (const id of VD.CORE) ok(VD.CUES[id] && VD.CUES[id].core, 'core cue missing in catalogue: ' + id);
for (const lg of Object.keys(VD.LINGUA)) {
  const L = VD.LINES[lg];
  if (!ok(L, 'no lines for lingua ' + lg)) continue;
  for (const id of VD.CORE) ok(L[id] && L[id].length && L[id].every(s => s), `${lg}: core cue ${id} missing`);
  if (VD.LINGUA[lg].conf === 'A' && lg !== 'zh') for (const id of VD.EXT_IDS) ok(L[id] && L[id][0], `${lg}: start / great cue ${id} missing`);
  ok(VD.LINGUA[lg].script, lg + ': no subtitle script');
  ok(Array.isArray(VD.LINGUA[lg].fb), lg + ': no fallback chain');
}
ok(Object.keys(VD.CUES).length === 123, 'catalogue has ' + Object.keys(VD.CUES).length + ' cues (want 123)');
ok(Object.keys(VD.LINGUA).length === 26, 'linguas: ' + Object.keys(VD.LINGUA).length + ' (want 26)');
for (const [id, c] of Object.entries(VD.CUES)) {
  if (!c.core) {
    let f = c.fb, guard = 0;
    while (f && f[0] !== '~' && !(VD.CUES[f] && VD.CUES[f].core) && guard++ < 6) f = VD.CUES[f] && VD.CUES[f].fb;
    ok(f && (f[0] === '~' || (VD.CUES[f] && VD.CUES[f].core)), `cue ${id}: fallback chain does not end in a core cue or a cry`);
  }
  ok(c.en && c.zh && c.zh.length, 'cue ' + id + ' needs zh + gloss');
  ok([1, 2, 3].includes(c.pri), 'cue ' + id + ' priority ' + c.pri);
  ok(['command', 'shout', 'sigh'].includes(c.del), 'cue ' + id + ' delivery ' + c.del);
  ok(['gen', 'ruler', 'target', 'herald', 'merchant'].includes(c.who), 'cue ' + id + ' speaker ' + c.who);
}
// every lingua: pickLine resolves every cue (through the fallback chain) to a line or a cry
for (const lg of Object.keys(VD.LINGUA)) for (const id of Object.keys(VD.CUES)) {
  let c = id, guard = 0, hit = null;
  while (c && guard++ < 8) { if (c[0] === '~' || (VD.LINES[lg][c] && VD.LINES[lg][c].length)) { hit = c; break; } c = VD.CUES[c] && VD.CUES[c].fb; }
  ok(hit, `${lg}: cue ${id} resolves to nothing`);
}
// 3. spec references (the docs are scanned, like design-v4/audio/voice-lines.js)
let nRefs = 0;
{
  const NOT_CUES = /\.js$|^tourney\.(crowd|roar|drum3|gong|lots|seal|petals)$|^crowd\.|^item\.<|^(build\.start|caravan\.depart|exped\.depart|exped\.return)$|<id>\.great$|^evt\.painting\./;
  const EXPAND = { 'evt.card.<category>': ['nature', 'society', 'court', 'people', 'global'].map(k => 'evt.card.' + k), 'debate.stance.<id>': ['yinjing', 'sheyu', 'bonan'].map(k => 'debate.stance.' + k) };
  const dir = path.join(ROOT, 'design-v4');
  const docs = fs.readdirSync(dir).filter(f => f.endsWith('.md')).map(f => path.join(dir, f));
  const refs = new Set();
  for (const f of docs) for (const m of fs.readFileSync(f, 'utf8').matchAll(/`((?:cmd|ui|title|item|tourney|bat|clash|duel|evt|fest|wenhui|debate|oath|marriage|submit|victory|build|caravan|exped|tribute|truce|hostage|mission|news)\.[A-Za-z.<>_]+)`/g)) refs.add(m[1]);
  for (const r of refs) { if (NOT_CUES.test(r)) continue; for (const id of EXPAND[r] || [r]) { nRefs++; ok(VD.CUES[id], 'cue named in a spec but not in the catalogue: ' + id); } }
  ok(nRefs >= 102, 'spec cue references checked: ' + nRefs + ' (want ≥ 102)');
}
// 4. profiles, factions, signatures
for (const who of Object.keys(VD.PROFILES)) for (const [lg, L] of Object.entries(VD.LINGUA)) if (L.conf === 'A') ok(VD.PROFILES[who].voice.some(v => L.voices[v]), `profile ${who}: no voice in ${lg}`);
for (const sc of ['classic', 'world']) for (const l of SG.Scenarios[sc].Factions) { const k = l.split('|')[0]; ok(VD.FACTION_LINGUA[k] && VD.LINGUA[VD.FACTION_LINGUA[k]], `faction ${k} (${sc}) has no lingua`); }
for (const [cul, lg] of Object.entries(VD.CULTURE_LINGUA)) ok(VD.LINGUA[lg], 'culture ' + cul + ' → unknown lingua ' + lg);
for (const [r, s] of Object.entries(VD.SIGNATURE)) { ok(VD.LINGUA[s.lingua], 'signature lingua unknown ' + r); if (s.lingua !== 'zh') ok(s.zh, 'foreign signature line without Chinese meaning: ' + r); }
ok(Object.keys(VD.SIGNATURE).length === 42, 'signature lines: ' + Object.keys(VD.SIGNATURE).length + ' (want 42)');
// 5. packs
let packStats = '(no voice/manifest.js yet: run the tools/voice-build pipeline)', sessionStats = '';
if (MF) {
  const failed = MF.failed || [];
  let clips = 0, opus = 0, missingVar = 0;
  for (const [lg, L] of Object.entries(VD.LINGUA)) {
    if (failed.includes(lg)) { ok(L.fb && L.fb.length, `${lg} failed QA but has no fallback lingua`); continue; }
    for (const pid of L.packs) {
      const e = MF.packs[pid];
      if (!ok(e, 'pack missing from manifest: ' + pid)) continue;
      const file = path.join(ROOT, e.file);
      if (!ok(fs.existsSync(file), 'pack file missing: ' + e.file)) continue;
      ok(!e.mp3 || fs.existsSync(path.join(ROOT, e.mp3)), 'mp3 pack file missing: ' + e.mp3);
      // load the pack into a scratch context and check its table
      const c2 = { console }; c2.window = c2; vm.createContext(c2);
      vm.runInContext(fs.readFileSync(file, 'utf8'), c2, { filename: e.file });
      const [meta, b64] = c2.SG.VoicePacks._q[0];
      const bytes = Buffer.from(b64, 'base64').length;
      ok(bytes === e.bytes, `${pid}: ${bytes} bytes, manifest says ${e.bytes}`);
      const ents = [];
      for (const arr of Object.values(meta.clips)) for (const x of arr) if (x) ents.push(x);
      for (const x of Object.values(meta.sig || {})) ents.push(x);
      for (const x of ents) ok(x[0] >= 0 && x[0] + x[1] <= bytes && x[2] > 0, `${pid}: clip entry out of range ${JSON.stringify(x)}`);
      // every Ogg Opus clip starts with 'OggS'
      const buf = Buffer.from(b64, 'base64');
      for (const x of ents.slice(0, 50)) ok(buf.slice(x[0], x[0] + 4).toString('latin1') === 'OggS', `${pid}: clip at ${x[0]} is not an Ogg stream`);
      clips += ents.length; opus += bytes;
      const voiceKey = meta.voice;
      for (const id of VD.CORE) {
        const vars = VD.LINES[lg][id] || [];
        vars.forEach((t, i) => {
          if (!/[\p{L}\p{N}]/u.test(t.replace(/~[a-z.]+/g, ''))) return;
          if (voiceKey === 'mBattle' && !(VD.CUES[id].del === 'shout' || VD.CUES[id].scene === 'battle')) return;
          if (!(meta.clips[id] && meta.clips[id][i])) {
            missingVar++;
            // a clip the QA gate dropped is allowed (the runtime takes another variant or the fallback chain); anything else is a pack bug
            if (!(MF.dropped || []).includes(pid + '/' + id + '#' + i)) fails.push(`${pid}: core cue ${id}#${i} has no clip and QA did not drop it`);
          }
        });
      }
    }
  }
  // per-session budget (§B10.6), with the packs the runtime really loads: SG.Voice preloads / says with each SPEAKER's pack
  // (_packsFor = what preload() and say() pick), so the player's lingua costs the packs its generals use (zh: both base voices),
  // plus up to RULES.sessionForeignPacks foreign packs; raw pack bytes as the spec's estimates count them, the base64 script
  // bytes (what the browser downloads) reported beside them
  const R = VD.RULES;
  const roster = [];
  { const seen = new Set(); for (const sc of ['classic', 'world']) for (const l of SG.Scenarios[sc].Generals) { const n = l.split('|')[0]; if (!seen.has(n)) { seen.add(n); roster.push(n); } } }
  const used = {};          // lingua → Set of packs its speakers use
  for (const n of roster) {
    const lg = SG.Voice.usableLingua(SG.Voice.profile(n).lingua) || SG.Voice.profile(n).lingua;
    for (const p of SG.Voice._packsFor(n, true)) (used[lg] = used[lg] || new Set()).add(p);
  }
  const raw = p => (MF.packs[p] || {}).bytes || 0, js = p => (MF.packs[p] || {}).jsBytes || Math.ceil(raw(p) * 4 / 3);
  const own = [...(used.zh || [])];
  ok(own.includes('zh-m') && own.includes('zh-f'), 'the zh speakers use ' + own.join(' ') + ' (want both base voices)');
  // the speaker preload loads ONE pack per speaker (a lingua id still loads all of them)
  ok(SG.Voice._packsFor('曹操').join(' ') === 'zh-m', '_packsFor(曹操) = ' + SG.Voice._packsFor('曹操').join(' ') + ' (want zh-m only)');
  ok(SG.Voice._packsFor('zh').length === 2, '_packsFor(\'zh\') = ' + SG.Voice._packsFor('zh').join(' '));
  const foreignPacks = [...new Set(Object.entries(used).filter(([lg]) => lg !== 'zh').flatMap(([, ps]) => [...ps]))].sort((a, b) => raw(b) - raw(a));
  const worst = foreignPacks.slice(0, R.sessionForeignPacks || 4);
  const sesRaw = own.concat(worst).reduce((s, p) => s + raw(p), 0), sesJs = own.concat(worst).reduce((s, p) => s + js(p), 0);
  ok(sesRaw <= (R.sessionBytes || 1.5 * 1048576), `per-session download ${(sesRaw / 1048576).toFixed(2)} MB raw (${own.concat(worst).join(' + ')}) > ${((R.sessionBytes || 1.5 * 1048576) / 1048576).toFixed(2)} MB`);
  for (const p of foreignPacks) ok(raw(p) <= (R.foreignPackBytes || 0.32 * 1048576), `${p}: ${(raw(p) / 1024).toFixed(0)} KB > the §B10.6 ${((R.foreignPackBytes || 0.32 * 1048576) / 1024).toFixed(0)} KB per foreign pack`);
  const soft = foreignPacks.filter(p => raw(p) > (R.foreignPackSoftBytes || 150 * 1024));
  // the heavier reading: four foreign LINGUAS met with both voices each (e.g. a world game against 卑弥呼's Wa and its men)
  const perLg = Object.entries(used).filter(([lg]) => lg !== 'zh').map(([lg, ps]) => [lg, [...ps].reduce((s, p) => s + raw(p), 0)]).sort((a, b) => b[1] - a[1]).slice(0, 4);
  const lgRaw = own.reduce((s, p) => s + raw(p), 0) + perLg.reduce((s, x) => s + x[1], 0);
  sessionStats = `session (runtime packs): ${own.join(' + ')} + ${worst.join(' + ')} = ${(sesRaw / 1048576).toFixed(2)} MB raw / ${(sesJs / 1048576).toFixed(2)} MB as base64 scripts (budget ${((R.sessionBytes) / 1048576).toFixed(2)} MB raw); ` +
    `4 foreign linguas with both voices (${perLg.map(x => x[0]).join(' ')}) ${(lgRaw / 1048576).toFixed(2)} MB raw (info); foreign packs over §B7.1's 150 KB (listed, not gated): ${soft.map(p => p + ' ' + (raw(p) / 1024).toFixed(0) + ' KB').join(', ') || 'none'}`;
  ok(opus <= 16 * 1048576, 'voice packs over the 16 MB ceiling');
  packStats = `packs ${Object.keys(MF.packs).length}, clips ${clips} (QA dropped ${(MF.dropped || []).length}, core variants missing ${missingVar}), ${MF.totals ? MF.totals.minutes + ' min, ' : ''}Opus ${(opus / 1048576).toFixed(2)} MB raw` +
    (MF.totals && MF.totals.mp3Bytes ? `, MP3 fallback ${(MF.totals.mp3Bytes / 1048576).toFixed(2)} MB` : '') + `; failed linguas: ${failed.join(' ') || 'none'}`;
}
// 5b. the shipped QA report (voice/QA.md, written by qa.py): records the licence override and labels every lingua against the
// §3.3 targets — no lingua whose ASR median misses its target (A ≤ 0.5; zh only with whisper-small ≤ 0.35 or a human listen) says 'ok'
{
  const qf = path.join(ROOT, 'voice', 'QA.md');
  if (MF && ok(fs.existsSync(qf), 'voice/QA.md missing')) {
    const q = fs.readFileSync(qf, 'utf8');
    ok(/^## Licences/m.test(q) && /Built with `--allow-nc`: \*\*(yes|no)\*\*/.test(q), 'voice/QA.md has no licence section / --allow-nc line');
    let rows = 0;
    for (const m of q.matchAll(/^\| (\w+) \| (\w) \| \d+ \| \d+ \| \d+ \| ([\d.]+|—) \(\d+\) \| (yes|no) \| ([^|]+) \|$/gm)) {
      rows++;
      const [, lg, grade, med, , result] = m;
      if (result.trim() !== 'ok') continue;
      ok(lg !== 'zh' || /whisper-small/.test(q.split('\n').find(l => l.startsWith('ASR:')) || ''), 'QA.md: zh says ok without whisper-small');
      ok(grade === 'A' && med !== '—' && +med <= 0.5, `QA.md: ${lg} (grade ${grade}, ASR median ${med}) says ok`);
    }
    ok(rows === Object.keys(VD.LINGUA).length, `QA.md lists ${rows} linguas (want ${Object.keys(VD.LINGUA).length})`);
  }
}
// 6. registry
SG.Voice.init();
ok(SG.Reg.voiceCues.size() === Object.keys(VD.CUES).length, 'SG.Reg.voiceCues has ' + SG.Reg.voiceCues.size() + ' cues');

console.log(`cues: ${Object.keys(VD.CUES).length} (${VD.CORE.length} core, ${VD.EXT_IDS.length} start / great in every A lingua); linguas ${Object.keys(VD.LINGUA).length}; spec references ${nRefs}`);
console.log(packStats);
if (sessionStats) console.log(sessionStats);
console.log(fails.length ? 'FAIL\n  ' + fails.slice(0, 40).join('\n  ') + (fails.length > 40 ? `\n  … ${fails.length - 40} more` : '') : 'ALL OK');
process.exitCode = fails.length ? 1 : 0;
