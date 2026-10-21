'use strict';
/* ==========================================================================
   tools/voice-build/gen-voice-data.js — writes js/voice-data.js (SG.VoiceData) from the AUDIO-V4 Part B data
   (design-v4/audio/voice-lines.js: LINGUA, FACTION_LINGUA, CULTURE_LINGUA, CUES, LINES, PROFILES, RULER_TAG, SIGNATURE)
   plus the runtime tables the spec describes in prose (§B4.3 delivery, §B10.3 rate limits, §B10.4 settings,
   §B10.5 subtitle scripts) and the player-decision defaults (DESIGN-V4 §20 Q13: one CONFIG block).

   node tools/voice-build/gen-voice-data.js           write js/voice-data.js
   node tools/voice-build/gen-voice-data.js --check   exit 1 if js/voice-data.js is stale
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const ROOT = path.join(__dirname, '..', '..');
const V = require(path.join(ROOT, 'design-v4', 'audio', 'voice-lines.js'));
const OUT = path.join(ROOT, 'js', 'voice-data.js');

// ------------------------------------------------------------ extras (runtime data the spec gives in prose) --
// §B4.3 delivery presets per cue category: shout = battle cues of pri ≥ 2 and war cries; sigh = miss / defeat / revoke
const SIGH = ['miss', 'defeat', 'title.revoke', 'cmd.search.none', 'bat.tacticFail', 'bat.lowMorale', 'bat.rout', 'bat.captured', 'duel.lose', 'tourney.lose', 'evt.choice.bad', 'cmd.hire.no'];
const WARCRY = ['attack', 'cmd.attack.start', 'charge', 'retreat', 'crit', 'special', 'victory', 'duel', 'tourney.win', 'bat.cityTaken'];
// scene of a cue: 'battle' (board, clash, duel) / 'court' (ruler's court lines, hall send §B4.2) / 'strategy'
const BATTLE_RE = /^(bat|clash|duel)\./;
const COURT = ['attack', 'allyYes', 'allyNo', 'grant', 'cmd.endMonth', 'cmd.attack.start', 'cmd.ally.yes', 'cmd.ally.no', 'title.grant', 'title.rankUp', 'tourney.open', 'tourney.award', 'news.succession', 'victory.wangdao', 'fest.open', 'mission.done', 'cmd.marriage.start', 'ui.titleBoard', 'ui.generalsOpen', 'ui.monthStart'];
function deliveryOf(id, c) {
  if (SIGH.includes(id)) return 'sigh';
  if (WARCRY.includes(id) || (BATTLE_RE.test(id) && c.pri >= 2)) return 'shout';
  return 'command';
}
// §B10.3 category cooldowns (seconds) for pri-1 cues; UI-V4 §14.3: shell cues ≥ 20 s each, ≤ 1 shell voice per 6 s
const COOLDOWN = { 'bat.select': 2.5, 'bat.move': 3, 'bat.attack': 3, ack: 1.5, 'ui.generalOpen': 20, 'ui.generalsOpen': 20, 'ui.titleBoard': 20, 'ui.shopOpen': 20, 'ui.save': 20, 'ui.monthStart': 20, 'cmd.endMonth': 20 };
// §B10.5 subtitle scripts (system font stacks only)
const SCRIPT = { zh: 'cjk', ja: 'cjk', ko: 'kore', vi: 'latn', ms: 'latn', mn: 'cyrl', kk: 'cyrl', pra: 'deva', sa: 'deva', txb: 'latn', xbc: 'latn', ta: 'taml', si: 'sinh', fa: 'arab', syc: 'latn', ka: 'geor', ar: 'arab', gez: 'ethi', xmr: 'latn', ber: 'latn', la: 'latn', grc: 'grek', os: 'cyrl', got: 'latn', cy: 'latn', ga: 'latn' };
// lingua fallback chain when a lingua's pack is missing or failed QA (§B7.2 "use its fallback chain"): a related lingua, then
// '~' = procedural cries with the subtitle only (the C-language behaviour)
const LINGUA_FB = { zh: [], ja: ['zh'], ko: ['zh'], vi: ['zh'], ms: ['zh'], mn: ['kk', 'zh'], kk: ['mn', 'zh'], pra: ['sa'], sa: ['pra'], txb: ['pra'], xbc: ['pra'],
  ta: ['si'], si: ['ta'], fa: ['ar'], syc: ['ar'], ka: ['fa'], ar: ['syc'], gez: ['ar'], xmr: ['gez'], ber: ['ar'], la: ['grc'], grc: ['la'], os: ['kk'], got: ['la'], cy: ['ga'], ga: ['cy'] };
// packs of v4.0 (§B7.1): one per lingua × base voice — m / f (if the lingua has them), got's angry battle voice, the Saka Prakrit voice
function packsOf(lg, L) {
  const out = [];
  for (const k of ['m', 'f', 'mBattle', 'saka']) if (L.voices[k]) out.push({ id: lg + '-' + k, voice: k, model: L.voices[k] });
  return out;
}
// non-zh lines are short; this is the Chinese meaning shown under a foreign line (variant i → zh[i] or zh[0])
// player decisions (DESIGN-V4 §20, recommended defaults; the task fixed them) — the one place to change them
const CONFIG = {
  frequency: 'essential',      // 语音频率：'all' 全部 · 'essential' 精简 · 'critical' 关键（Q13：default 精简）
  essentialKeepsCommands: false, // 精简 = 「仅重要」 as labelled (AUDIO-V4 §B10.4): priority ≥ 2 only (great results, hire / ally / persuade answers,
                                 // 出征, battle results, duels …); true would also keep the player's routine pri-1 command lines (cmd.*.start, miss)
  language: 'native',          // 语音语言：'native' 原声（各说其语）· 'zh' 一律汉语 · 'off'
  hiredKeepLanguage: true,     // Q9 / Q13: a hired or captured foreigner keeps his language (subtitles carry the meaning)
  subtitles: 'both',           // 字幕：'both' 原文 + 中文 · 'zh' 仅中文 · 'off'
  musicStyle: 'suite',         // 音乐风格：'suite' 君主乐章 · 'region' 地域（第三版）
  readAloud: false,            // 朗读（辅助）：Web Speech, local voices only, off by default (§B6.2)
  volumes: { master: 100, music: 100, sfx: 100, voice: 100 },
};
const RULES = {
  maxVoices: { strategy: 1, battle: 2, duel: 2 },   // §B10.3 (battle / duel: one per side)
  pri1Gap: 1.2, pri2QueueMax: 0.6, preemptFade: 0.08, shellGap: 6,
  duckLevel: 0.5, duckAttack: 0.06, duckRelease: 0.4,   // §B10.2
  voiceBus: 0.9, hallSend: 0.18, decodeWaitMs: 120, cacheBytes: 6 * 1024 * 1024, cryCacheBytes: 512 * 1024,
  bubbleExtra: 0.8,      // §B10.5 bubble lifetime = clip + 0.8 s
  fanfareGap: 3,         // enemy-phase fanfare at most once every 3 enemy phases (§A3)
  monthStartEvery: 3,    // ui.monthStart at most every 3rd month (UI-V4 §14.3)
  // §B10.6 download budget, in RAW pack bytes as the spec's own estimates count them (the base64 scripts are 4/3 of that; which one
  // the budget means is open — DESIGN-V4 §20): the player's lingua (both base voices) + up to sessionForeignPacks foreign packs
  sessionBytes: 1.5 * 1024 * 1024, sessionForeignPacks: 4,
  foreignPackBytes: 0.32 * 1024 * 1024,   // §B10.6 per foreign pack (§B7.1's "≤ 150 KB each" is listed, not gated: the spec disagrees)
  foreignPackSoftBytes: 150 * 1024,
};

// ------------------------------------------------------------ assemble --
const CUES = {};
for (const [id, c] of Object.entries(V.CUES)) {
  const o = { who: c.who, pri: c.pri, zh: c.zh, en: c.en, del: deliveryOf(id, c) };
  if (c.core) o.core = 1;
  if (c.fb) o.fb = c.fb;
  if (c.tpl) o.tpl = c.tpl;
  if (BATTLE_RE.test(id) || WARCRY.includes(id)) o.scene = 'battle';
  if (COURT.includes(id)) o.court = 1;
  if (COOLDOWN[id]) o.cd = COOLDOWN[id];
  if (/^ui\./.test(id)) o.shell = 1;
  CUES[id] = o;
}
const LINGUA = {};
for (const [lg, L] of Object.entries(V.LINGUA)) {
  LINGUA[lg] = { name: L.name, conf: L.conf, route: L.route, voices: L.voices, script: SCRIPT[lg], fb: LINGUA_FB[lg], packs: packsOf(lg, L).map(p => p.id) };
  if (L.gap) LINGUA[lg].gap = L.gap;
}
// Chinese meaning of the 18 foreign signature lines (§B9; the subtitle's second line)
const SIG_ZH = { '卑弥呼': '以鬼道治国。', '卑弥弓呼': '我不认什么女王！', '故国川王': '百姓乃国之根本！', '首露王': '龟旨峰上，天意降临！', '魁头': '出发！',
  '於夫罗': '愿长生天护佑！', '胡维色迦': '王中之王！', '耶若室利': '大王万胜！', '迦梨迦罗': '高韦里河是我们的命！', '沃洛吉斯': '我乃万王之王！',
  '戈契赫尔': '波斯是我们的！', '阿布加尔': '我主阿布加尔王！', '加达拉特': '阿克苏姆之王！', '康茂德': '我乃罗马的赫拉克勒斯！',
  '阿根托科克斯': '罗马休想踏进群山！', '巴洛马尔': '万岁，国王！', '菲利默': '我们走！', '区连': '我们自由了！' };
const SIGNATURE = {};
for (const [r, s] of Object.entries(V.SIGNATURE)) { const m = /^([a-z]+):(.*)$/.exec(s); SIGNATURE[r] = m ? { lingua: m[1], text: m[2], zh: SIG_ZH[r] || '' } : { lingua: 'zh', text: s }; }
for (const [r, s] of Object.entries(SIGNATURE)) if (s.lingua !== 'zh' && !s.zh) throw new Error('signature line without a Chinese meaning: ' + r);
const data = {
  version: 1, CONFIG, RULES, LINGUA, FACTION_LINGUA: V.FACTION_LINGUA, CULTURE_LINGUA: V.CULTURE_LINGUA, SAKA_VOICE: V.SAKA_VOICE,
  CORE: V.CORE, EXT_IDS: V.EXT_IDS, CUES, LINES: V.LINES, PROFILES: V.PROFILES, RULER_TAG: V.RULER_TAG, SIGNATURE,
};

function emit() {
  const j = k => JSON.stringify(data[k]);
  const linesTxt = Object.entries(data.LINES).map(([lg, L]) => '    ' + JSON.stringify(lg) + ': ' + JSON.stringify(L)).join(',\n');
  const cuesTxt = Object.entries(data.CUES).map(([id, c]) => '    ' + JSON.stringify(id) + ': ' + JSON.stringify(c)).join(',\n');
  const linTxt = Object.entries(data.LINGUA).map(([id, c]) => '    ' + JSON.stringify(id) + ': ' + JSON.stringify(c)).join(',\n');
  return `'use strict';
/* ==========================================================================
   人声数据 · SG.VoiceData（AUDIO-V4 乙部 §B2–§B10）—— 由 tools/voice-build/gen-voice-data.js 从
   design-v4/audio/voice-lines.js 生成，切勿手改（改台词改那里，再运行生成器与 tools/voice-build/ 的配音流程）。
     CONFIG        玩家可改的默认值（DESIGN-V4 §20 Q13：语音频率默认「精简」、雇来的外族武将保留自己的语言……）——唯一的一处
     RULES         混音与限流的数字（§B10.2 闪避 0.5 / 60 ms / 400 ms，§B10.3 同时人声上限、冷却……）
     LINGUA        26 种语言：置信度 A / B / C、注音路线、声库（m / f / mBattle / saka）、字幕文字 script、回退链 fb、语音包 packs
     FACTION_LINGUA / CULTURE_LINGUA / SAKA_VOICE   谁说什么语言（§B3）
     CUES          123 条台词编号：说话人 who（gen / ruler / target / herald / merchant）、优先级 pri、中文 zh、释义 en、
                   回退 fb（'~x' = 程序化喊叫）、读法 del（command / shout / sigh，§B4.3）、scene、court（大殿混响）、cd（冷却秒）
     LINES         lingua → cue → [变体]（'~kind' = 程序化喊叫，§B5）
     PROFILES      传令官 / 商人（非武将的说话人）   RULER_TAG   君主的尊号插入语   SIGNATURE   42 位手写乐章君主的名句
   ========================================================================== */
(function () {
  const SG = (typeof window !== 'undefined' ? window : globalThis).SG = (typeof window !== 'undefined' ? window : globalThis).SG || {};
  SG.VoiceData = {
  version: ${data.version},
  CONFIG: ${j('CONFIG')},
  RULES: ${j('RULES')},
  LINGUA: {
${linTxt},
  },
  FACTION_LINGUA: ${j('FACTION_LINGUA')},
  CULTURE_LINGUA: ${j('CULTURE_LINGUA')},
  SAKA_VOICE: ${j('SAKA_VOICE')},
  CORE: ${j('CORE')},
  EXT_IDS: ${j('EXT_IDS')},
  CUES: {
${cuesTxt},
  },
  LINES: {
${linesTxt},
  },
  PROFILES: ${j('PROFILES')},
  RULER_TAG: ${j('RULER_TAG')},
  SIGNATURE: ${j('SIGNATURE')},
  };
})();
`;
}
const txt = emit();
if (process.argv.includes('--check')) {
  const cur = fs.existsSync(OUT) ? fs.readFileSync(OUT, 'utf8') : '';
  if (cur !== txt) { console.log('js/voice-data.js is stale: run node tools/voice-build/gen-voice-data.js'); process.exitCode = 1; } else console.log('js/voice-data.js up to date');
} else {
  fs.writeFileSync(OUT, txt);
  console.log(`wrote js/voice-data.js: ${Object.keys(CUES).length} cues, ${Object.keys(LINGUA).length} linguas, ${Object.values(LINGUA).reduce((s, l) => s + l.packs.length, 0)} packs, ${(txt.length / 1024).toFixed(1)} KB`);
}
module.exports = { data, packsOf };
