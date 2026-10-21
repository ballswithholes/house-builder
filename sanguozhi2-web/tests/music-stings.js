'use strict';
/* ==========================================================================
   tests/music-stings.js — 指令短乐（DOMESTIC-FX §8、§13.2；INTEGRATION §10.3、§13）
   node tests/music-stings.js        退出码 = 失败数
     1. 34 段 × 档 × 12 个家族 = 1 284 个键全部登记，并通过 SG.Music.validate（小节长度、声部、乐器）
     2. 记谱长度：普通 1.7–2.7 s、好 2.6–3.6 s、大成 3.0–4.5 s、失手 1.6–2.6 s；普通 ≤ 好 ≤ 大成；时刻 ≤ 4.5 s
     3. A 段动机各不相同（主奏声部的前四个音）
     4. 每个换过的乐器都在 SG.Music.INST 里；有音高的打击乐在鼓谱轨上带 note
     5. 数据与规格原型 design-v4/domestic/stings.js 逐项相同（STINGS / ROLE / FAMILY_INST / FAMILY_DEG）
     6. sections(key) 的 A / 尾段 / 总长与编译结果一致；快速档 tagKey 只含尾段、可编译、不列入 keys()
     7. 应答声部（同盟、联姻）换成对方家族的乐器；混音：短乐增益 0.85 低于冲锋乐句
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const ROOT = path.join(__dirname, '..');
let fails = 0, checks = 0;
const ok = (c, msg) => { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } };

global.window = global;
const load = f => vm.runInThisContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), { filename: f });
for (const f of ['core.js', 'music.js', 'music-inst.js', 'music-han.js', 'music-world.js']) load(f);
const t0 = Date.now();
load('music-stings.js');
const SG = global.SG, MU = SG.Music, MS = SG.MusicStings;
console.log('== music-stings.js loaded in ' + (Date.now() - t0) + ' ms: ' + MS.ids.length + ' stings, ' + MS.keyCount + ' keys');

console.log('== 1. registration and validation');
ok(MS.ids.length === 34, '34 sting ids (got ' + MS.ids.length + ')');
ok(MS.FAMILIES.length === 12, '12 culture families');
const keys = MU.keys().filter(k => k.indexOf('sting:') === 0);
ok(keys.length === 1284, '1 284 registered keys (got ' + keys.length + ')');
ok(MS.keyCount === 1284, 'keyCount 1 284');
let bad = 0;
for (const k of keys) {
  const e = MU.validate(k);
  if (e.length) { bad++; if (bad <= 5) console.log('    ' + k + ': ' + e.slice(0, 3).join(' / ')); }
}
ok(bad === 0, 'every key validates (' + bad + ' with errors)');

console.log('== 2. notated lengths');
const RANGE = { normal: [1.7, 2.7], good: [2.6, 3.6], great: [3.0, 4.5], miss: [1.6, 2.6] };
const dur = {};
const rows = [];
for (const id of MS.ids) {
  const S = MS.STINGS[id];
  for (const tierName of Object.keys(MS.forms(id))) {
    const k = MS.key(id, tierName);
    const d = MU.duration(k).total;
    dur[id + ':' + tierName] = d;
    rows.push(id.padEnd(17) + tierName.padEnd(7) + d.toFixed(2));
    if (S.event || S.only) ok(d <= 4.5, k + ' moment ≤ 4.5 s (' + d.toFixed(2) + ')');
    else { const [lo, hi] = RANGE[tierName]; ok(d >= lo && d <= hi, k + ` ${d.toFixed(2)} s within ${lo}–${hi}`); }
  }
  const n = dur[id + ':normal'], g = dur[id + ':good'], x = dur[id + ':great'];
  if (n && g) ok(g >= n - 1e-9, id + ' good ≥ normal');
  if (g && x) ok(x >= g - 1e-6, id + ' great ≥ good');
}
console.log('  ' + rows.length + ' tier forms; e.g. ' + rows.slice(0, 3).join(' · '));

console.log('== 3. distinct A motifs');
const seen = new Map();
for (const [id, S] of Object.entries(MS.STINGS)) {
  const lead = Object.values(S.A).find(s => typeof s === 'string' && /[1-7]/.test(s.replace(/![a-z<>]+/g, '').replace(/0:\d/g, '')));
  const sig = (lead || '').replace(/![a-z<>]+/g, '').split(/[\s|]+/).filter(t => /^[#b]?[1-7]/.test(t)).slice(0, 4).join(' ');
  ok(!!sig && !seen.has(sig), 'motif of ' + id + ' (' + sig + ') is unique' + (seen.has(sig) ? ' — equals ' + seen.get(sig) : ''));
  seen.set(sig, id);
}

console.log('== 4. instruments and pitched kit');
const PITCHED = { kenong: 1, kempul: 1, gongageng: 1, dayan: 1, saron: 1, gender: 1, bonang: 1, bianzhong: 1, timpani: 1 };
let unk = 0, noNote = 0;
for (const k of keys) {
  const P = MU.get(k);
  for (const v of Object.values(P.voices)) if (!MU.INST[v.inst]) { unk++; if (unk < 4) console.log('    unknown inst ' + v.inst + ' in ' + k); }
  for (const v of Object.values(P.kit)) { if (!MU.INST[v.inst]) unk++; if (PITCHED[v.inst] && !v.note) noNote++; }
}
ok(unk === 0, 'all substituted instruments exist (' + unk + ' unknown)');
ok(noNote === 0, 'pitched kit instruments carry a note (' + noNote + ' without)');

console.log('== 5. data identical to the spec prototype');
const proto = require(path.join(ROOT, 'design-v4', 'domestic', 'stings.js'));
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
ok(same(proto.STINGS, MS.STINGS), 'STINGS equals design-v4/domestic/stings.js');
ok(same(proto.ROLE, MS.ROLE), 'ROLE equals the prototype');
ok(same(proto.FAMILY_INST, MS.FAMILY_INST), 'FAMILY_INST equals the prototype');
ok(same(proto.FAMILY_DEG, MS.FAMILY_DEG), 'FAMILY_DEG equals the prototype');
for (const id of MS.ids) ok(same(proto.forms(proto.STINGS[id]), MS.forms(id)), id + ' forms equal the prototype');
// the built pieces equal the prototype's except for the added kit notes and the _sting tag
let diff = 0;
for (const id of MS.ids) for (const fam of MS.FAMILIES) {
  const a = proto.build(id, fam), b = MS.build(id, fam);
  for (const k of Object.keys(a)) {
    const B = JSON.parse(JSON.stringify(b[k] || {}));
    delete B._sting; for (const e of Object.values(B.kit || {})) delete e.note;
    if (!same(a[k], B)) { diff++; if (diff < 3) console.log('    differs: ' + k); }
  }
}
ok(diff === 0, 'built pieces equal the prototype apart from kit notes (' + diff + ' differ)');
// interval colour example from the spec (§8.3): develop.town A in wa = 1_ 4_ 5_ 4_ | b6_ 5_ 4_ 1_
ok(MU.get('sting:develop.town@wa').sections.A.yq.replace(/\s+/g, ' ').indexOf("1_ 4_ 5_ 4_ | b6_ 5_ 4_ 1_") >= 0, 'develop.town@wa uses the 都节 colour');

console.log('== 6. sections() and the fast-tier tag keys');
let secBad = 0;
for (const k of keys.filter(k => k.indexOf('@') < 0)) {
  const s = MS.sections(k), C = MU.compile(k);
  let acc = 0, a = 0;
  for (const n of C.form) { if (n === 'A') a += C.sections[n].dur; acc += C.sections[n].dur; }
  const bpm = MS.STINGS[MU.get(k)._sting.id].bpm, spb = 60 / bpm;
  // compile() durations are in seconds or beats depending on the engine: compare via duration()
  const tot = MU.duration(k).total;
  if (Math.abs(s.total - tot) > 1e-6 || Math.abs(s.A + s.tag - tot) > 1e-6) { secBad++; if (secBad < 3) console.log('    ' + k, JSON.stringify(s), tot, acc, a, spb); }
}
ok(secBad === 0, 'sections() A + tag = compiled length for every hua key (' + secBad + ' off)');
let tagBad = 0;
for (const id of MS.ids) for (const t of Object.keys(MS.forms(id))) {
  const tk = MS.tagKey(id, t, 'hua');
  const P = MU.get(tk);
  if (!P || P.form.length !== 1 || MU.validate(tk).length || MU.keys().indexOf(tk) >= 0) tagBad++;
}
ok(tagBad === 0, 'tagKey: one section, validates, hidden from keys() (' + tagBad + ' bad)');
const tw = MS.tagKey('recruit', 'great', 'wa');
ok(/@wa$/.test(tw) && MU.get(tw).form[0] === 'X', 'tagKey for a family: ' + tw);

console.log('== 7. answer voice, gain');
const ka = MS.key('ally', 'great', 'hua', 'roman');
ok(MU.get(ka) && MU.get(ka).voices.an.inst === 'aulos' && MU.get(ka).voices.bl.inst === 'bianzhong', 'ally answer voice in the partner family (roman aulos over our bianzhong)');
ok(MU.validate(ka).length === 0, ka + ' validates');
const km = MS.key('marriage', 'normal', 'wa', 'roman');
ok(MU.get(km) && MU.get(km).voices.an.inst === 'aulos' && MU.get(km).voices.eh.inst === 'shakuhachi', 'marriage@wa with a Roman partner');
ok(MS.key('develop.land', 'miss') === 'sting:develop.land', 'missing tier falls back to normal');
ok(MS.key('nosuch', 'good') === null, 'unknown id → null');
ok(MS.familyOf('celt') === 'north' && MS.familyOf('han') === 'hua' && MS.familyOf('xx') === 'hua', 'familyOf maps the 18 culture codes');
const clash = MU.get('clash');
ok(keys.every(k => MU.get(k).gain === 0.85) && (!clash || !(clash.gain < 0.85)), 'sting gain 0.85, not above the clash stinger');

console.log(`music-stings: ${checks - fails} / ${checks} checks passed`);
console.log(fails ? 'FAIL' : 'ALL OK');
process.exitCode = fails;
