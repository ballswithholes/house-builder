'use strict';
// AUDIO-V4.md is generated: AUDIO-V4.src.md + tables built from suites.js, voice-lines.js and tts-eval.json.
//   node design-v4/audio/gen-md.js   → writes design-v4/AUDIO-V4.md
const fs = require('fs');
const path = require('path');
const S = require('./suites.js');
const V = require('./voice-lines.js');
const { FAMILY_INST } = require('../domestic/stings.js');
const eva = JSON.parse(fs.readFileSync(path.join(__dirname, 'tts-eval.json'), 'utf8')).rows;

const esc = s => String(s === undefined || s === null ? '' : s).replace(/\|/g, '\\|');
const inst = (fam, role) => (FAMILY_INST[fam] || {})[role] || S.HUA[role];
function famOf(name) {
  const m = { '卑弥呼': 'wa', '卑弥弓呼': 'wa', '故国川王': 'han3', '首露王': 'han3', '魁头': 'steppe', '於夫罗': 'steppe', '胡维色迦': 'indic', '耶若室利': 'indic', '迦梨迦罗': 'indic',
    '沃洛吉斯': 'persia', '戈契赫尔': 'persia', '阿布加尔': 'arabia', '加达拉特': 'nile', '康茂德': 'roman', '阿根托科克斯': 'north', '巴洛马尔': 'north', '菲利默': 'north', '区连': 'nanhai' };
  return m[name] || 'hua';
}
const T = {};
// ---- hand motifs
T.hand = ['| Ruler | Family | Motif (简谱, 2 bars 4/4) | Lead → instrument | Answer | Tempo | Key | Idea |', '|---|---|---|---|---|---|---|---|']
  .concat(Object.entries(S.HAND).map(([n, h]) => {
    const f = famOf(n);
    return `| ${n}${h.house ? '（承' + h.house + '）' : ''} | ${f} | \`${esc(h.motif)}\` | ${h.lead} → ${inst(f, h.lead)} | ${inst(f, h.answer)} | ×${h.tempo} | ${h.tr > 0 ? '+' : ''}${h.tr} | ${esc(h.note)} |`;
  })).join('\n');
// ---- linguas
T.lingua = ['| Code | Lingua | Historical language (190 AD) | Lines written in (stand-in) | Conf. | Phonemizer | Voices (m / f) | Gap |', '|---|---|---|---|---|---|---|---|']
  .concat(Object.entries(V.LINGUA).map(([k, L]) => {
    const v = L.voices;
    const short = s => s ? s.replace(/^vits-piper-|^vits-mimic3-|-medium|-x_low|-high|-low|multi-lang-v1_0/g, '').replace(/^kokoro-#/, 'kokoro ') : '—';
    return `| \`${k}\` | ${L.name} | ${esc(L.hist)} | ${esc(L.standIn)} | ${L.conf} | ${L.route} | ${short(v.m)} / ${short(v.f)}${v.mBattle ? ' (battle: ' + short(v.mBattle) + ')' : ''}${v.saka ? ' (Saka accent: ' + short(v.saka) + ')' : ''} | ${esc(L.gap || L.note || '')} |`;
  })).join('\n');
// ---- faction → lingua
{
  const by = {};
  for (const [f, l] of Object.entries(V.FACTION_LINGUA)) (by[l] = by[l] || []).push(f);
  T.factions = ['| Lingua | Factions (starting) |', '|---|---|'].concat(Object.entries(by).map(([l, fs]) => `| \`${l}\` ${V.LINGUA[l].name} | ${fs.join(', ')} |`)).join('\n');
}
// ---- core lines in groups
function coreTable(codes) {
  const head = '| Cue | 中文 | ' + codes.map(c => V.LINGUA[c].name + ' `' + c + '`').join(' | ') + ' | Gloss |';
  const sep = '|' + Array(codes.length + 3).fill('---').join('|') + '|';
  const rows = V.CORE.map(id => '| `' + id + '` | ' + V.LINES.zh[id].join(' / ') + ' | ' + codes.map(c => esc((V.LINES[c][id] || []).join(' / '))).join(' | ') + ' | ' + esc(V.CUES[id].en) + ' |');
  return [head, sep].concat(rows).join('\n');
}
T.coreEast = coreTable(['ja', 'ko', 'vi', 'ms', 'mn', 'kk']);
T.coreSouth = coreTable(['pra', 'sa', 'ta', 'si', 'txb', 'xbc']);
T.coreWest = coreTable(['fa', 'syc', 'ka', 'ar', 'gez', 'xmr', 'ber']);
T.coreEurope = coreTable(['la', 'grc', 'got', 'cy', 'ga', 'os']);
// ---- extended cues
T.extended = ['| Cue | 中文 (variants) | Gloss | Speaker | Pri | Fallback |', '|---|---|---|---|---|---|']
  .concat(Object.entries(V.CUES).filter(([, c]) => !c.core).map(([id, c]) => `| \`${id}\` | ${esc(c.zh.join(' / '))} | ${esc(c.en)} | ${c.who} | ${c.pri} | \`${c.fb}\` |`)).join('\n');
// ---- signature lines
T.signature = ['| Ruler | Line | Lingua |', '|---|---|---|'].concat(Object.entries(V.SIGNATURE).map(([r, s]) => {
  const m = /^([a-z]+):(.*)$/.exec(s); return `| ${r} | ${esc(m ? m[2] : s)} | ${m ? m[1] : 'zh'} |`;
})).join('\n');
// ---- TTS evaluation (selected)
{
  const pick = ['zh_kokoro_yunjian_long', 'zh_kokoro_xiaoyi_long', 'zh_chaowen_jiang', 'zh_huayan_chong', 'zh_fanchen_s5_chong', 'zh_huayan_zunming', 'ja_kumo_kashikomari', 'ja_kumo_kakare', 'ja_alpha_himiko', 'ja_kokoro_kumo_kashikomari',
    'ko_kss_bunbu', 'ko_kss_long', 'vi_vais_long', 'id_news_serbu', 'kk_issai_alga', 'mn_via_kk_uragshaa', 'fa_reza_bepish', 'ar_kareem_hujum', 'it_ricc_avanti', 'la_via_it_adarma', 'it_paola_la', 'la_via_el_adarma',
    'grc_via_el_alalai', 'el_rap_embros', 'de_thor_vorwaerts', 'de_emo_angry_vorwaerts', 'non_is_steinn', 'tr_dfki_ileri', 'sw_lan', 'cy_bu_ymlaen', 'ka_natia_long', 'ml_arjun_ta', 'hi_priyam_sa', 'pra_via_ur_fasih'];
  const by = Object.fromEntries(eva.map(r => [r.id, r]));
  T.eval = ['| Clip | Lingua | Model (licence) | Text | Length | Median F0 | ASR heard | Error |', '|---|---|---|---|---|---|---|---|']
    .concat(pick.filter(k => by[k]).map(k => {
      const r = by[k];
      const lic = r.license || (r.model.startsWith('kokoro') ? 'Apache-2.0' : '');
      return `| ${k} | ${r.lang} | ${r.model.replace(/^vits-piper-|^vits-mimic3-/, '')} (${esc(lic.replace(/https?:\/\/\S+/, 'see card').slice(0, 26))}) | ${esc(r.text)} | ${r.dur} s | ${r.f0_hz || '—'} Hz | ${esc((r.asr_hyp || '').slice(0, 28))} | ${r.asr_err === null || r.asr_err === undefined ? '—' : r.asr_err + (r.asr_unit === 'syllable' ? ' syl' : r.asr_unit === 'phoneme' ? ' ph' : '')} |`;
    })).join('\n');
}
const src = fs.readFileSync(path.join(__dirname, 'AUDIO-V4.src.md'), 'utf8');
const out = src.replace(/<!--TABLE:(\w+)-->/g, (m, k) => { if (!T[k]) throw new Error('no table ' + k); return T[k]; });
fs.writeFileSync(path.join(__dirname, '..', 'AUDIO-V4.md'), out);
console.log('AUDIO-V4.md', out.length, 'chars');
