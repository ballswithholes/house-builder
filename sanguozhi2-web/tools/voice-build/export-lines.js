'use strict';
/* ==========================================================================
   tools/voice-build/export-lines.js — step 1 of the offline voice pipeline (AUDIO-V4 §B7.2)
   js/voice-data.js → lines.json: every (pack, lingua, base voice, model, speaker, cue, variant, text, route, delivery,
   authored IPA) the packs need. synth.py renders them, qa.py checks them, pack.py writes voice/*.js.

   node tools/voice-build/export-lines.js [out.json]      (default tools/voice-build/out/lines.json)
   Clip ids: '<cue>#<variant>' (variant = index in LINES[lingua][cue]); 'sig#<ruler>' (signature lines, §B9);
   'rtag.<cue>#0' (ruler interjections RULER_TAG). A variant that is only '~kind' has no clip (procedural cry, §B5);
   '~kind' tokens inside a line are cut out here and added back at run time (voice.js).
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const ROOT = path.join(__dirname, '..', '..');
global.window = global;
require(path.join(ROOT, 'js', 'voice-data.js'));
const D = global.SG.VoiceData;

// ------------------------------------------------------------ authored IPA for the 'ipa' route linguas (§B3, §B7.2) --
// Latin transliteration (Syriac, Bactrian, Tocharian B words, Meroitic, Kabyle) and Ossetian Cyrillic → IPA
const LAT = [
  ['šš', 'ʃː'], ['ḥ', 'ħ'], ['ḫ', 'x'], ['ʕ', 'ʕ'], ['ʔ', 'ʔ'], ['ʾ', 'ʔ'], ['ʿ', 'ʕ'], ['ṭ', 'tˤ'], ['ṣ', 'sˤ'], ['ḍ', 'dˤ'], ['ẓ', 'zˤ'],
  ['š', 'ʃ'], ['ž', 'ʒ'], ['č', 'tʃ'], ['ñ', 'ɲ'], ['ɣ', 'ɣ'], ['Ɣ', 'ɣ'], ['ā', 'aː'], ['ē', 'eː'], ['ī', 'iː'], ['ō', 'oː'], ['ū', 'uː'],
  ['kh', 'x'], ['gh', 'ɣ'], ['sh', 'ʃ'], ['th', 'θ'], ['ph', 'f'], ['ch', 'tʃ'], ['ae', 'ae'],
  ['y', 'j'], ['j', 'ʒ'], ['c', 'ʃ'], ['w', 'w'], ['q', 'q'], ['x', 'x'], ['r', 'r'], ['g', 'ɡ'],
];
const CYR = [['уæ', 'wə'], ['уа', 'wa'], ['гъ', 'ʁ'], ['дз', 'dz'], ['дж', 'dʒ'], ['къ', 'kʼ'], ['пъ', 'pʼ'], ['тъ', 'tʼ'], ['хъ', 'q'], ['цъ', 'tsʼ'], ['чъ', 'tʃʼ'],
  ['а', 'a'], ['æ', 'ə'], ['б', 'b'], ['в', 'v'], ['г', 'ɡ'], ['д', 'd'], ['е', 'e'], ['ё', 'jo'], ['ж', 'ʒ'], ['з', 'z'], ['и', 'i'], ['й', 'j'],
  ['к', 'k'], ['л', 'l'], ['м', 'm'], ['н', 'n'], ['о', 'o'], ['п', 'p'], ['р', 'r'], ['с', 's'], ['т', 't'], ['у', 'u'], ['ф', 'f'],
  ['х', 'χ'], ['ц', 'ts'], ['ч', 'tʃ'], ['ш', 'ʃ'], ['ы', 'ɨ'], ['ь', ''], ['ъ', ''], ['э', 'e'], ['ю', 'ju'], ['я', 'ja']];
// word stress: first syllable for these languages (Ossetian, Tocharian, Meroitic guesses; Syriac penultimate is close enough)
// stress on the first syllable (marked at the start of the word, so multi-character phonemes like tˤ stay whole)
function stress(w) { return /[aeiouəɨ]/.test(w) && w.length > 3 ? 'ˈ' + w : w; }
function toIPA(text, lingua) {
  let s = text.normalize('NFC').toLowerCase();
  const tbl = lingua === 'os' ? CYR : LAT;
  let out = '';
  for (let i = 0; i < s.length;) {
    let hit = null;
    for (const [a, b] of tbl) if (s.startsWith(a, i)) { hit = [a, b]; break; }
    if (hit) { out += hit[1]; i += hit[0].length; continue; }
    const ch = s[i];
    out += /[a-z\s!?.,…\-]/.test(ch) ? ch : (/[̀-ͯ]/.test(ch) ? '' : ch);
    i++;
  }
  out = out.replace(/…/g, '.').replace(/-/g, ' ');
  // a glide after a consonant at the end of a word is a vowel (Syriac māry 'my lord' → maːri, qrubw → qrubu)
  if (lingua !== 'os') out = out.replace(/([^aeiouː\s])j(?=$|[\s!?.,])/g, '$1i').replace(/([^aeiouː\s])w(?=$|[\s!?.,])/g, '$1u');
  return out.split(/(\s+|[!?.,]+)/).map(w => /[a-zəɨ]/.test(w) ? stress(w) : w).join('').trim();
}

// ------------------------------------------------------------ helpers --
function speakText(t) { return String(t).replace(/~[a-z.]+/g, '').replace(/\s+/g, ' ').trim(); }
function hasWords(t) { return /[\p{L}\p{N}]/u.test(speakText(t)); }
function parseVoice(spec) { const i = spec.indexOf('#'); return i < 0 ? { model: spec, speaker: null } : { model: spec.slice(0, i), speaker: spec.slice(i + 1) }; }
function packVoices(lg, L) { return L.packs.map(id => { const k = id.slice(lg.length + 1); return { id, key: k, ...parseVoice(L.voices[k]) }; }); }

function build() {
  const rows = [];
  const add = (pack, lg, v, cue, variant, text, extra) => {
    const t = speakText(text);
    if (!hasWords(t)) return;
    const c = D.CUES[cue] || {};
    const L = D.LINGUA[lg];
    const r = { pack: pack.id, lingua: lg, voice: pack.key, model: pack.model, speaker: pack.speaker, route: L.route, cue, variant, id: cue + '#' + variant, text: t, del: (extra && extra.del) || c.del || 'command', conf: L.conf };
    if (L.route === 'ipa') r.ipa = toIPA(t, lg);
    if (lg === 'got') r.text = t.replace(/ƕ/g, 'hw').replace(/Ƕ/g, 'Hw');
    rows.push(Object.assign(r, extra && extra.zh ? { zh: extra.zh } : {}));
  };
  for (const [lg, L] of Object.entries(D.LINGUA)) {
    const lines = D.LINES[lg] || {};
    for (const pv of packVoices(lg, L)) {
      for (const [cue, vars] of Object.entries(lines)) {
        const c = D.CUES[cue];
        if (!c) continue;
        // got's angry voice: only the battle / shout lines (§B4.3)
        if (pv.key === 'mBattle' && !(c.del === 'shout' || c.scene === 'battle')) continue;
        vars.forEach((t, i) => add(pv, lg, pv, cue, i, t));
      }
      // ruler interjections in the court language (RULER_TAG), male voices only
      if (pv.key === 'm' && D.RULER_TAG[lg]) for (const [cue, t] of Object.entries(D.RULER_TAG[lg])) add(pv, lg, pv, 'rtag.' + cue, 0, t, { del: D.CUES[cue] ? D.CUES[cue].del : 'command' });
    }
  }
  // signature lines (§B9): the ruler's own lingua, his sex's voice
  const FEMALE = ['卑弥呼'];
  for (const [ruler, s] of Object.entries(D.SIGNATURE)) {
    const L = D.LINGUA[s.lingua];
    const pvs = packVoices(s.lingua, L);
    const want = FEMALE.includes(ruler) ? 'f' : 'm';
    const pv = pvs.find(p => p.key === want) || pvs.find(p => p.key === 'm') || pvs[0];
    if (pv) add(pv, s.lingua, pv, 'sig', ruler, s.text, { del: 'shout' });
  }
  for (const r of rows) if (r.cue === 'sig') r.id = 'sig#' + r.variant;
  return rows;
}

const rows = build();
const out = process.argv[2] || path.join(__dirname, 'out', 'lines.json');
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify(rows, null, 0).replace(/\},\{/g, '},\n{'));
const per = {};
for (const r of rows) per[r.pack] = (per[r.pack] || 0) + 1;
console.log(`lines.json: ${rows.length} clips in ${Object.keys(per).length} packs → ${out}`);
console.log(Object.entries(per).map(([k, v]) => k + ' ' + v).join(' · '));
module.exports = { toIPA, build, speakText };
