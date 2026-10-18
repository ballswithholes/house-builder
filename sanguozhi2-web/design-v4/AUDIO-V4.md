# 三国志-霸王的世界 · 第四版音频设计（AUDIO-V4）— 君主乐章 · 人声

Player requests 7 (different rulers get different music suites) and 12 (voices for every main-menu action and in battles, by ruler and language), plus the audio half of request 6 (the command stings, already written in `DOMESTIC-FX.md` §8; this file supplies the per-ruler colour those stings asked for).

Read first: `DESIGN-V2.md` §1 (common rules: classic scripts, no network, everything synthesized or generated in code, `SG.Random` for rules), §4A (the music engine contract), §6 (culture codes); `CONTRACT.md`; `js/audio.js`, `js/music.js` header (notation), `js/music-han.js`, `js/music-world.js`; the v4 siblings `DOMESTIC-FX.md` (§8 stings, voice cue ids `cmd.*`), `COMBAT-V4.md` (§1 clash timeline, §2.6 crit beat, §13.3 sound names and voice hooks), `TITLES.md` (ceremony voice keys), `ITEMS.md` (reveal cry), `CITY-VIEW.md` §3 (the 12 culture families). Where this file conflicts with them, this file wins for music pieces, voices, the mix and the audio settings; the other owners win for their rules and pictures. Platform order: browser first, then the Unity port (§5). Every visual mentioned here (subtitle cards, the ruler-select preview) follows the v4 art direction: Lanternvale-style warm low-poly, ink outlines, paper and lantern light.

This file is generated: `node design-v4/audio/gen-md.js` rebuilds it from `AUDIO-V4.src.md` and the data files, so the tables below are the data.

| Companion file (`design-v4/audio/`) | What it is |
|---|---|
| `suites.js` | Part A reference implementation: the 42 hand-composed leitmotifs, the generator, the piece builder. `node design-v4/audio/suites.js` builds suites for all 499 generals of both scenarios, validates every piece with the real `SG.Music.validate`, checks lengths, motif distinctness and the palette rule: **ALL OK** (1 611 pieces). `--list` prints every motif. |
| `render-suites.mjs` | Renders suite pieces in Chromium through `tests/music.html` (`OfflineAudioContext`) → WAV + `render-stats.json` (peak, RMS, NaN) |
| `render-stats.json` | 420 rendered pieces (42 hand suites × 10 kinds): 0 NaN, peak ≤ 0.61 |
| `voice-lines.js` | Part B data: the 26 speech languages (linguas), faction → lingua, the 82-cue catalogue, the core lines in every lingua, signature lines. `node design-v4/audio/voice-lines.js` checks coverage and estimates clips and size: **ALL OK** |
| `tts-eval.json` | The offline TTS experiment of §B6 (85 clips, 26 models): length, synthesis time, Opus size, median F0, Whisper round trip |
| `tts-lab/` | The scripts behind it (Python 3.11 venv: `piper-tts 1.8.0`, `sherpa-onnx 1.13.8`, `pyopenjtalk-plus`, `pypinyin`; model download lists; the formant-bark prototype `formant-bark.js`) |
| `samples/` | 30 listening samples (876 KB Opus): 12 suite excerpts, 18 voice clips including the failures |

---

## 0. Key numbers at a glance

| Item | Value |
|---|---|
| Music suites | **one per ruler**, keyed by the ruler (a general), for all **499** generals (any of them can inherit a realm): **42 hand-composed** (the 18 classic rulers, 4 likely heirs, 20 world rulers), **457 generated** deterministically and frozen into a data file |
| Pieces per suite | 12: `map` · `council` · `battle` · `battle-defend` · `victory` · `defeat` · `fanfare` · `clash` · `clash-b` · `clash-short` · `clash-crit` · `ending` (the unifier's motif opens 「一统天下」); plus the duel's two "dueling motif" calls built per duel |
| What makes a suite the ruler's | a 2-bar leitmotif (stated in a 4-bar map intro, a 2-bar tag at the end of every loop, a 2-bar battle call at the top of every loop, augmented in the defend piece, the whole of victory / defeat / fanfare / clash); the **lead swap** (the template's busiest melody voice plays the ruler's instrument for the whole loop); key ±2 semitones; tempo ×0.92–1.08 |
| Distinctness | motif distance ≥ **3** between any two of the 499 (heir ↔ founder ≥ 2 on purpose: the house cell); measured minimum 3, worst generator re-roll 8; every same-family pair of the 70 starting rulers also differs in lead instrument, tempo (≥ 3 %) or key (324 pairs, 0 identical) |
| Cost | build 0.6 ms per suite, compile ≤ 4.4 ms per piece (sliced ≤ 8 ms by `compileAsync`), data 6 KB hand + ≈ 60 KB generated |
| Render check | 420 pieces rendered in Chromium: **0 NaN**, peak 0.23–0.61, map RMS −25…−21 dB, battle −20…−17 dB (one real bug found and fixed: pitched percussion in a kit needs a `note`) |
| v4 clash stinger | starts on contact, 2 bars at ♩150 = 3.2 s; bar 2 downbeat lands **1.60 s** after contact = v4 `melee1` (1.62 s); `clash-crit` adds the 0.4 s slow beat |
| Speech languages | **26 linguas** for 70 factions and 18 culture codes; confidence A (modern language or modern stand-in) 14, B (classical, well attested) 7, C (attested words only) 5 |
| Cues | **82** (20 core in every lingua, 62 extended with fallback chains); 3 priorities; procedural cries for efforts and where a language has no attested word |
| v4.0 clips | **851** ≈ 17.4 min of speech ≈ **2.24 MB** at Opus 16 kbps (3.0 MB as the base64 JS packs `file://` needs); MP3 fallback 4.2 MB; full extended translation target ≤ 16 MB |
| Recommended voice tech | **(a) offline neural TTS** (Piper / Kokoro / Mimic3 voices via sherpa-onnx mirrors, phonemes injected cross-lingually for Latin, Greek, Gothic, Sanskrit, Prakrit, Ge'ez, Syriac …) for words + **(c) procedural formant cries** for efforts, crowds and fallback; **(b) Web Speech** only as an optional read-aloud for long text |
| Measured TTS | synthesis RTF 0.05–0.25 warm (4 CPU, no GPU); Whisper-base round trip: de / tr / id 0.00, ja 0.00–0.36 (phonemes), ar 0.08, it 0.12, Latin-through-Italian 0.16, zh 0.17–0.23 (pinyin syllables, ≥ 6 syllables); failures found: one Greek model hallucinates 5–8 s, Kokoro Japanese through the sherpa front end, 2-syllable Chinese barks, female → male shifts beyond ×0.85 |
| Opus bitrate | 16 kbps = same intelligibility as 24 / 32 kbps; 12 kbps degrades Latin (0.16 → 0.32) |
| Formant cries | 2–5 ms to render a 0.8 s cry in JS (RT factor 0.002–0.006) |
| Mix | voice bus 0.9; a voice ducks music to 0.5 (−6 dB, 60 ms attack, 400 ms release); voice ≥ 6 dB above ducked battle music, ≥ 10 dB above ducked map music |

---

## 1. Scope, files and ownership

| File | New / changed | Content |
|---|---|---|
| `js/music-suites.js` | new | `HAND`, `generate()`, `buildSuite()` from `design-v4/audio/suites.js`; `SG.MusicSuites` API (§A11); loaded after `music-world.js` and `music-stings.js` |
| `js/music-suites-gen.js` | new, generated | the frozen generated suites (`{ name: { motif, lead, answer, tempo, tr, temper, family } }`), written by `tools/build-suites.js`; never edited by hand |
| `tools/build-suites.js` | new | runs the generator in roster order over both scenarios, keeps every existing entry, appends new generals; `--check` fails if an existing entry would change |
| `js/audio.js` | changed | voice bus, ducking, `setSuite`, suite-aware `music()` and `stinger()`, `fanfare()`, `setVoiceVolume`, the audio settings (§B10); loads the new files like the music files |
| `js/voice-data.js` | new | `LINGUA`, `FACTION_LINGUA`, `CULTURE_LINGUA`, `CUES`, `LINES`, `RULER_TAG`, `SIGNATURE` from `voice-lines.js` |
| `js/voice.js` | new | `SG.Voice` runtime: profile, pack loader, decode cache, scheduler, subtitles (§B7–B10) |
| `js/voice-cry.js` | new | procedural formant cries (port of `tts-lab/formant-bark.js`) |
| `voice/*.js`, `voice/manifest.js`, `voice/LICENSES.md` | new, generated | the voice packs (one per lingua × voice), the manifest, the licence list |
| `tools/voice-build/` | new | `export-lines.js` (Node) → `synth.py`, `qa.py`, `pack.py` (Python, offline, §B7.2) |
| `tests/suites.js`, `tests/voice-lines.js`, `tests/voice-pick.js` | new | Node checks (§3) |
| `tests/music.html` | changed | 「君主乐章」 tab: pick a ruler, hear every piece; offline render check |
| `tests/voice.html` | new | every lingua's cues, subtitles, decode check, ducking and priority checks |
| `js/battle-controller.js`, `js/clash.js`, `js/duel-game.js`, `js/strategy-screen.js`, `js/game.js` | changed | call sites (§2) |
| `tools/export-unity-music.js` | changed | exports suite data and the builder test vectors for the C# port (§5) |

Not here: the tournament's arena music (request 14 task; it uses `fanfare@entrant` and `victory@winner` from this file), the command stings themselves (`DOMESTIC-FX.md` §8; §A10 below gives them the ruler's family and head motif), the settings screen layout (menu task; §B10 gives its audio rows).

---

# Part A — 君主乐章: a music suite per ruler (request 7)

## A1. What v3 has

`SG.Sfx.music(kind)` plays `title`, `map`, `battle`, `battle-defend`, `duel`, `victory`, `defeat`, `ending`, `council`, plus the `clash` overlay. `map` and `battle` have regional variants (`map@wa` … `battle@celt`, 9 regions; `music-world.js`), chosen by `SG.Sfx.setCulture(culture)`; the strategy screen sets the player faction's culture, the battle sets the **foe's** culture. So every Han ruler (the 18 classic ones and 公孙度, 士燮, 朱符) hears the same map piece 「山河」 and the same battle piece 「出阵」, and every Roman, Celtic or steppe ruler shares his region's. The request: different rulers, different music.

## A2. The idea: a suite = leitmotif + palette, wrapped around the family's pieces

Writing a dozen full pieces for each of 70 rulers (≈ 800 pieces, hours of music) is not feasible by hand and would not be good if generated whole. Instead each ruler gets:

1. **A leitmotif** (主题动机): 2 bars of 4/4 in 简谱, 4–9 notes, ending on the suite's final. Hand-composed for 42 rulers (§A5), generated for the rest (§A6).
2. **A palette**: the culture **family** (the 12 of `CITY-VIEW.md` §3: hua, han3, wa, steppe, oasis, indic, persia, arabia, nile, roman, north, nanhai), a **lead** and an **answer** instrument role (flute, flute2, bow, pluck, pluck4, reed, brass1, bell, pad …, resolved per family through `DOMESTIC-FX`'s `FAMILY_INST` table), a **tempo** factor (0.92–1.08) and a **transposition** (−2…+2 semitones).

`buildSuite()` turns the two into real `SG.Music` pieces by wrapping the family's existing arrangements (the v3 pieces are rich, tested and loudness-matched) with motif sections, re-voicing their lead, and by generating the short pieces from the motif alone:

| Piece | Built from | Ruler-specific material |
|---|---|---|
| `map` | family map template (`map`, `map@wa` …) | **Mi**: 4-bar intro, motif on the lead over a tonic–fifth drone, motif repeated on the answer instrument (≈ 12.6 s at ♩76); **Mt**: 2-bar tag at the end of every loop (≈ 6 s per ≈ 130 s loop); **lead swap** (the template's busiest legato voice plays the ruler's lead for the whole loop); key, tempo |
| `council` | hua: 「庙堂」; others: the map template at 0.88 × tempo without drums | Mi + Mt on the pluck, lead swap, key, tempo |
| `battle` | family battle template | **Mc**: 2-bar call at the top of every loop on brass / reed over the family drum and a gong (≈ 3.5 s per ≈ 70 s loop); lead swap to the suite's battle lead (always a loud role, §A7.4); key, tempo |
| `battle-defend` | hua: 「孤城」; others: the battle template at 0.85 × tempo, dyn × 0.88 | **Md**: the motif in augmentation (×2) on the bow over a slow drum |
| `victory` | generated | 6 bars ≈ 13.8 s at ♩104: motif on brass over a drum roll → motif a fourth higher on the lead → held final with gong and cymbal |
| `defeat` | generated | the first four notes augmented ×1.5 on the bow, falling to the battle final, choir pad, ≈ 12–20 s at ♩60 |
| `fanfare` | generated | 2 bars ≈ 3.6 s at ♩132: the head (first 3 beats) on brass, the final held, timpani and gong — the ruler's "calling card" (§A3) |
| `clash`, `clash-b`, `clash-short`, `clash-crit` | generated | the charge call: head of the motif in the battle mode (§A8) |
| duel calls `Ma` / `Mb` | han 「龙争虎斗」 | challenger's motif then defender's motif, each on his own family's lead (§A9) |
| `ending` intro | 「一统天下」 | Mi on the choir (the unifier's motif opens the ending) |

Honest scale of the difference: in a map loop about 6 s of every 130 s is the ruler's tune, plus the 12.6 s opening, plus the timbre, key and tempo of the whole loop; battle loops carry a 3.5 s call per ≈ 70 s plus timbre. That is enough to recognise "my" music and to hear a change of ruler, and it costs no new orchestration. Fully composed suites for the six most-played rulers (曹操, 刘备, 孙坚, 董卓, 康茂德, 卑弥呼: map + battle, written like `music-han.js`) are proposed for v4.1 (open question 6).

## A3. Who hears which piece

| Context | Piece | Notes |
|---|---|---|
| Title screen | `title` (global) | unchanged |
| Ruler selection (`game.js`) | hover / tap a ruler → his `fanfare`; dwell 1.2 s → his `map` (from Mi) with 0.8 s cross-fade; his signature line (§B9) | lets the player hear the difference before choosing |
| Strategy map | `map@<player ruler>` | setting 「音乐风格」: 君主乐章 (default) / 地域 (v3 behaviour) |
| Diplomacy, events, 任命 / 授官 ceremonies | `council@<player ruler>`; when another ruler answers (同盟 yes / no, 离间 caught) his `fanfare` plays as an overlay (duck 0.35) before his voice line | |
| Battle, player attacks | `battle@<player ruler>`; at the start of an **enemy phase** the enemy ruler's `fanfare` as an overlay, at most once every 3 enemy phases | the battle is "ours", the enemy announces himself |
| Battle, player defends | `battle-defend@<player ruler>` | |
| Battle watched, player not involved | `battle@<attacking ruler>` | |
| Clash (攻击画面) | `clash@<attacking side's ruler>` (player's or enemy's) | you hear whose charge it is |
| Army crit in the clash | `clash-crit@<attacker>` | COMBAT-V4 §2.6 |
| Duel (单挑) | `duel` with `Ma` = challenger's motif, `Mb` = defender's motif | §A9 |
| Victory / defeat | `victory@` / `defeat@<player ruler>` | |
| Unification | `ending` with the unifier's Mi on the choir | |
| Succession (新君即位) | the new ruler's `fanfare` + `news.succession` voice line, then the map switches to his suite at the next month start | §A10 |
| 武林大会 (request 14) | the tournament's arena piece; each entrant's walk-on plays his ruler's `fanfare`; the champion's ruler's `victory` | owned by the tournament task |
| Command stings (DOMESTIC-FX §8) | played in the ruler's family; the great tiers that quote 「霸业」 quote the ruler's **head cell** instead | §A10 |
| Enemy / ally rulers elsewhere (news, ally reports) | their `fanfare` before their news line, at most one per month-end news batch | |

When the player's ruler is a generated one, all of the above work the same way.

## A4. Data format

```js
// js/music-suites.js — hand suites (the table of §A5 is this data)
SG.MusicSuites.HAND['曹操'] = {
  motif: '6,_ 3_ 6 5. 3_ | 2 3 6,:2 |',   // 2 bars 4/4, js/music.js note syntax (degrees, octave marks, _ . :N, ornaments)
  lead: 'reed', answer: 'bow',            // roles, resolved per family (FAMILY_INST of domestic/stings.js; hua defaults)
  tempo: 1.04, tr: 0,                     // factor on the template's bpm; semitones
  temper: 'hero',                         // descriptive tag (generated suites: martial / scholar / statesman)
  house: undefined,                       // heir suites name the founder whose head cell they keep (孙策 → 孙坚)
  note: '羽调式，上冲八度后回落，雄心与诡谲',
};
// family comes from the ruler's culture (FAMILY_OF: han / nanman → hua, korea → han3, wa, steppe + sarmatian → steppe,
// tarim → oasis, kushan + india → indic, persia, arab + moor → arabia, africa → nile, roman, celt + german → north,
// seasia + yi → nanhai). Templates: hua → han pieces, han3 → korea, oasis / indic → kushan, nile → arab, north → celt,
// nanhai → seasia, the others their own region.

// js/music-suites-gen.js — generated, frozen (one line per general, roster order)
SG.MusicSuites.GEN = { '尉仇台': { family: 'han3', motif: '1:1 5,:1 1:2 | 6,:1 1:3 |', lead: 'bow', answer: 'pluck', tempo: 1.009, tr: 0, temper: 'statesman' }, … };

// registered piece keys: 'suite:<ruler>:<kind>'   e.g. 'suite:曹操:map', 'suite:卑弥呼:fanfare'
```

Rules: the motif is written in the template's degrees (1 = the template key's "1"); `respell()` keeps each degree number and takes its accidental from the target piece's mode (a degree the mode lacks moves to the nearest scale tone; explicit accidentals such as 董卓's ♯4 are kept), so one motif works in a pentatonic map piece, a Bhairav battle piece and a 12/8 gallop. Compound metres (12/8, 6/8) map one 4/4 beat to a dotted beat and turn dotted pairs and even eighths into long–short (2 : 1) so the motif swings; 3/4 (the Celtic map) gets three bars with the last note lengthened by a beat. Notes crossing a barline are split and tied.

## A5. The 42 hand-composed leitmotifs

Composed for the family's map and battle modes (hua pieces are 宫 pentatonic: map 1 = G, battle 羽 on A). Heirs keep the founder's first three notes (the house cell 家传动机): 孙坚 → 孙策 `5,_ 5,_ 1`, 马腾 → 马超 `6, 3_ 3_`, 刘焉 → 刘璋 `3 5_ 6_`, 公孙度 → 公孙康 `2 5 2'`.

| Ruler | Family | Motif (简谱, 2 bars 4/4) | Lead → instrument | Answer | Tempo | Key | Idea |
|---|---|---|---|---|---|---|---|
| 董卓 | hua | `6,:2 3> #4_ 3_ \| 1 7,_ 6,_ 6,:2 \|` | brass → horns | erhu | ×0.94 | -2 | 低音铜管拖着增四度，暴君的阴影 |
| 袁绍 | hua | `5,. 1_ 3 5 \| 6. 5_ 3 1 \|` | brass1 → horn | bianzhong | ×0.98 | +1 | 四世三公：附点的仪仗，钟声应答 |
| 曹操 | hua | `6,_ 3_ 6 5. 3_ \| 2 3 6,:2 \|` | reed → suona | erhu | ×1.04 | 0 | 羽调式，上冲八度后回落，雄心与诡谲 |
| 刘备 | hua | `3 5 6. 1'_ \| 6 5 3:2 \|` | bow → erhu | dizi | ×0.97 | 0 | 宫调式，二胡的温厚上行，落在角音 |
| 孙坚 | hua | `5,_ 5,_ 1 5. 6_ \| 1' 6 5:2 \|` | brass1 → horn | suona | ×1.05 | -1 | 孙氏家传动机 5, 1 5：四度 + 五度的猛跃 |
| 袁术 | hua | `1'. 6_ 5 3^ \| 2 1 2:2 \|` | reed → suona | yangqin | ×1 | +2 | 华丽下行，倚音虚饰，停在商音不解决 |
| 公孙瓒 | hua | `5_ 5__ 5__ 6_ 5__ 5__ 1' 6 \| 5. 3_ 5:2 \|` | brass1 → horn | dizi | ×1.06 | 0 | 白马义从：长短短的马蹄节奏 |
| 刘表 | hua | `2. 3_ 5 6 \| 5 3 2:2 \|` | pluck4 → qin | xiao | ×0.95 | 0 | 商调式，古琴与箫的平稳级进 |
| 刘焉 | hua | `3 5_ 6_ 3' 2'_ 1'_ \| 6:2 3:2 \|` | flute2 → xiao | bianzhong | ×0.96 | +1 | 角调式，蜀中的神秘上扬 |
| 马腾 | hua | `6, 3_ 3_ 6 3'_ 2'_ \| 1' 6 6:2 \|` | bow → erhu | suona | ×1.04 | -2 | 西凉：羽调式的马步与大跳 |
| 陶谦 | hua | `5 3 2. 1_ \| 2 3 1:2 \|` | pad → sheng | dizi | ×0.93 | 0 | 老成持重，笙的下行 |
| 孔融 | hua | `5 6^ 1' 6_ 5_ \| 3 5 5:2 \|` | pluck4 → qin | xiao | ×0.96 | +2 | 建安文士：琴的倚音与回环 |
| 韩馥 | hua | `2 0_ 3_ 2 1 \| 6, 1 2:2 \|` | flute2 → xiao | zheng | ×0.97 | -1 | 迟疑的休止，商音悬而未决 |
| 张鲁 | hua | `3:2 6 5 \| 3 2 3:2 \|` | bell → bianzhong | xiao | ×0.94 | 0 | 五斗米道：编钟的仪式音型 |
| 张杨 | hua | `5 5_ 6_ 5 1' \| 6 5 5:2 \|` | reed → suona | ruan | ×1 | +1 | 朴实的徵调号子 |
| 刘繇 | hua | `1 3 2 1_ 6,_ \| 5, 6, 1:2 \|` | flute → dizi | zheng | ×0.98 | -1 | 扬州：宫调式的低回 |
| 陆康 | hua | `6, 1 3. 2_ \| 1 6, 6,:2 \|` | bow → erhu | qin | ×0.95 | +2 | 庐江死守：庄重的羽调 |
| 孟获 | hua | `6,_ 6,_ 6,_ 1_ 3 3 \| 2_ 1_ 6,_ 5,_ 6,:2 \|` | reed → suona | erhu | ×1.06 | -2 | 南中：铜鼓般的重复音 |
| 孙策（承孙坚） | hua | `5,_ 5,_ 1 5 1' \| 2'. 1'_ 6 5 \|` | reed → suona | horn | ×1.08 | -1 | 小霸王：家传头三音，更高更急 |
| 刘璋（承刘焉） | hua | `3 5_ 6_ 5 3 \| 2 1 6,:2 \|` | flute2 → xiao | zheng | ×0.93 | +1 | 刘焉的动机向下塌落 |
| 马超（承马腾） | hua | `6, 3_ 3_ 6 6' \| 5'. 3'_ 6:2 \|` | reed → suona | erhu | ×1.08 | -2 | 锦马超：家传马步冲上高八度 |
| 公孙康（承公孙度） | hua | `2 5 2' 6 \| 5 3 2:2 \|` | brass1 → horn | dizi | ×1 | 0 | 承袭辽东的商调 |
| 公孙度 | hua | `2 5 2'. 1'_ \| 6 5 2:2 \|` | brass1 → horn | erhu | ×1 | 0 | 辽东：北地的商调，四度上冲 |
| 士燮 | hua | `3_ 5_ 6 1' 2'_ 1'_ \| 6 5:3 \|` | flute → dizi | zheng | ×0.95 | +2 | 交趾四十年太平：笛子的长句 |
| 卑弥呼 | wa | `3:2 4 6 \| 7^ 6 4 3 \|` | flute → shinobue | koto | ×0.92 | 0 | 都节音阶，篠笛的鬼道咒语 |
| 卑弥弓呼 | wa | `6, 7, 3. 4_ \| 3 7, 6,:2 \|` | flute2 → shakuhachi | koto | ×1.03 | -1 | 狗奴：低沉的尺八与太鼓 |
| 故国川王 | han3 | `6,:2 2 1 \| 3. 2_ 1 6, \|` | flute → daegeum | haegeum | ×0.98 | 0 | 大笒长音，界面调 |
| 首露王 | han3 | `5 3 5 6_ 5_ \| 3 2 1:2 \|` | bow → haegeum | gayageum | ×0.97 | +1 | 卵生的始祖：奚琴与伽倻琴 |
| 魁头 | steppe | `1 5 1':2 \| 6 5 3 5 \|` | bow → morin | overtone | ×1.02 | 0 | 长调的五度、八度大跳 |
| 於夫罗 | steppe | `6, 6,_ 3_ 6 5 \| 3 2_ 1_ 6,:2 \|` | bow → morin | dombra | ×1.04 | -2 | 南匈奴单于：马头琴的流亡之歌 |
| 胡维色迦 | indic | `1 5 4 3 \| 2 3 1:2 \|` | pluck → sitar | bansuri | ×0.98 | 0 | 贵霜：西塔尔的庄严回旋 |
| 耶若室利 | indic | `1 3 5. 6_ \| 5 4 3:2 \|` | flute → bansuri | sitar | ×1 | +2 | 百乘：班苏里的上行 |
| 迦梨迦罗 | indic | `1' 7 6 5_ 6_ \| 5 3 5:2 \|` | reed → suona | sitar | ×1.04 | -1 | 朱罗：筑大堤的英雄 |
| 沃洛吉斯 | persia | `1 3 2. 1_ \| 7, 2 1:2 \|` | flute → ney | kamancheh | ×0.98 | 0 | 安息：奈伊的 Shur 色彩 |
| 戈契赫尔 | persia | `1 5 6. 5_ \| 4 3 2 1 \|` | bow → kamancheh | santur | ×1 | +1 | 波西斯：萨珊前夜 |
| 阿布加尔 | arabia | `1 4. 3_ 2_ 3_ \| 2 1 7,:2 \|` | pluck → oud | ney | ×0.97 | 0 | 埃德萨：乌德的木卡姆 |
| 加达拉特 | nile | `1 5 4 5 \| 3:2 1:2 \|` | reed → aulos | harp | ×1 | 0 | 阿克苏姆：竖琴与阿夫洛斯管 |
| 康茂德 | roman | `1 1_ 5,_ 1 3 \| 5. 4_ 3 1 \|` | brass1 → cornu | aulos | ×1.02 | 0 | 罗马的赫拉克勒斯：号角的竞技场 |
| 阿根托科克斯 | north | `5, 1 1_ 2_ 1 \| 5, 6,_ 7,_ 1:2 \|` | reed → chanter | harp | ×1 | 0 | 喀里多尼亚：风笛 |
| 巴洛马尔 | north | `1 1 5. 4_ \| 3 2 1:2 \|` | brass1 → cornu | whistle | ×1.02 | -1 | 马科曼尼：卢尔号 |
| 菲利默 | north | `1 3 5 5 \| 6 5 3:2 \|` | reed → chanter | harp | ×1 | +1 | 哥特：迁徙之歌 |
| 区连 | nanhai | `3 5 1' 5 \| 3 2 3:2 \|` | flute → suling | saron | ×1.02 | 0 | 林邑：192 年起兵的苏林笛 |

Design notes for the most visible ones: 曹操 rises a sixth and falls back (ambition, cunning), on the suona with the erhu answering; 刘备 is the warm arch on the erhu landing on 角, never a fanfare; 孙坚's family cell is two leaps (4th + 5th) on the horn, 孙策 takes it higher and faster; 董卓 drags a low horn line through an augmented fourth (the only tritone of the set); 袁绍's dotted procession and 袁术's ornamented fall that stops on 商 (a pretender's unresolved cadence) are deliberately a pair; 卑弥呼 is a shinobue incantation in the 都节 scale; 康茂德's motif is a cornu call that turns into an arena fanfare; 沃洛吉斯's ney line is coloured by Shur when the battle respells it.

## A6. The generator (for everyone else)

Deterministic, no `SG.Random`, seeded by `fnv1a(name + '|' + attempt)` with the mulberry32 stream already used by `js/music.js`:

1. **Temperament** from the three attributes: `martial` if 武力 is the highest and ≥ 智力 + 6; `scholar` if 智力 is the highest and ≥ 政治; `statesman` if 政治 is the highest; else martial. (Measured over 457 generated: 248 / 110 / 99.)
2. **Rhythm**: bar 1 from the temperament's bank (`martial`: dotted 1.5 + 0.5, eighth pickups, 0.75 + 0.25 snaps; `scholar`: long notes and gentle eighths; `statesman`: even quarters and halves), bar 2 from a cadence bank (always ends with a note ≥ 2 beats unless the bank says 1 + 3).
3. **Final** by the ruler's 德 (virtue of his faction): ≥ 60 → 宫 or 徵 (pitch classes 0, 7), ≤ 30 → 羽 or 商 (9, 2), else any of 宫 徵 羽; mapped to the nearest degree of the family map mode.
4. **Pitch walk** over the mode's degree list from a start near the final: step ±1 degree, or a leap of 2–3 degrees with probability 0.45 / 0.15 / 0.28 (martial / scholar / statesman); upward bias 0.62 in bar 1, 0.40 in bar 2; register clamped to 5, … 3'' (−7 … +16 semitones from "1"); the last note is the final (an octave up if the line ended high).
5. **Ornaments**: scholar → vibrato `~` on the longest note; martial → accent `>` on the first; female and non-martial → a grace note `^` on the second.
6. **Palette**: lead `martial` reed / brass1 / bow, `scholar` flute / flute2 / pluck4, `statesman` bow / pluck / flute; answer pluck (flute if the lead plucks); tempo martial 1.04, scholar 0.96, statesman 1.00, −0.03 at age ≥ 55, ±0.02 jitter; tr −2…+2.
7. **Distinctness** (re-roll with attempt + 1 until it holds): signature = the first six melodic intervals (signed semitones, capped at ±7) and the first seven durations; distance = Levenshtein(intervals) + ½ Levenshtein(durations); required ≥ **3** against every hand and earlier generated motif (heir ↔ founder ≥ 2). For a starting ruler also the **palette rule**: against every starting ruler of the same family, at least one of lead instrument, tempo (≥ 0.03) or key differs. Measured: min distance 3, total re-rolls 140, worst 8 (cap 64).
8. **Freeze**: `tools/build-suites.js` writes the result into `js/music-suites-gen.js`; later data changes append, never re-roll existing entries (same rule as COMBAT-V4 §10.4 for combos). A general missing from the frozen file (mods) is generated at run time in a stable order after the frozen ones.

## A7. The builder in detail (`buildSuite(MU, ruler, suite)`)

### A7.1 Wrapped pieces

For each of map, council, battle, battle-defend: deep-copy the template piece, then

- `title = ruler + '·' + template title`; `transpose += suite.tr`; `bpm`, every section `bpm` / `bpmTo` × `suite.tempo` (× 0.88 council, × 0.85 defend outside hua); council outside hua drops the drums (`dr` removed), defend multiplies `dyn` by 0.88.
- add voices `mL` (lead, pan +0.08, gain 0.95), `mA` (answer, pan −0.18, gain 0.8), `mP` (drone: strings for hua / han3 / wa / nanhai, kargyraa for steppe, drones elsewhere; octave −1, gain 0.55), `mB` (low strings, −2, 0.6); kit `mD` (family drum), `mG` (family gong).
- add the motif sections (Mi intro, Mt tag, Mc call, Md augmented call) and place them: map / council `form = ['Mi', …template form, 'Mt']` (Mt is inside the loop because the loop runs to the end of the form); battle / defend insert `Mc` / `Md` before the template's `loopFrom` and make it the new `loopFrom`.
- harmony of the inserted sections is a tonic–fifth drone (the template's tonic, computed from `tonic` or the first scale degree) — safe in every mode the engine has; bass on the tonic an octave lower without the fifth.
- octave of an added voice = the octave most used for that instrument across all registered pieces.

### A7.2 Lead swap

The template voice with the most notes among legato instruments (`SG.Music.INST[i].type === 'legato'`) takes the suite's lead instrument for the whole piece (its notation and octave unchanged). Pluck, bell and pad leads do not swap (the template's lead stays), so the swap never puts a sustained line on a plucked sample. Recorded as `P._leadSwap = 'voice:inst'` for the test page.

### A7.3 Pitched percussion

Kit lanes of pitched instruments (`kenong kempul gongageng dayan saron gender bonang bianzhong timpani`) get `note: <tonic>`; without it the engine has no pitch and renders NaN. Found by the Chromium render of the nanhai and indic suites, fixed in the builder, re-rendered clean (§A12).

### A7.4 Battle leads

A battle call must cut through: a suite whose lead is flute, flute2, pluck or pluck4 uses the family `reed` for `Mc` and the battle swap (刘表's qin becomes the suona in battle; 卑弥呼's flute becomes the wa reed, the shinobue). Defend uses the family `bow`.

## A8. Clash stingers on the v4 timeline

`clash.js` calls the stinger at **contact** (`onContact`). COMBAT-V4 §1.3: after contact come the slow-motion 0.24 TL at slowK 0.40 (0.60 s real) and the melee to `melee1`: (3.18 − 1.92 − 0.24) + 0.60 = **1.62 s** after contact at speed 1; then regroup, end and fade ≈ 1.1 s. So the suite stinger is **2 bars at ♩150** (3.2 s): bar 1 = the melee (head of the motif on brass over a drum build), the **downbeat of bar 2 lands 1.60 s after contact** on the rout / melee1 frame with a gong and the brass fifth, bar 2 rings over the regroup and into the board. `audio.js followPlan` keeps choosing the battle tempo's integer ratio but is narrowed to **132–168** (was 112–176) so the bar-2 accent stays within ±0.2 s of melee1 (132 → 1.82 s, 168 → 1.43 s). This replaces COMBAT-V4's suggested `speed × 3.96 / 4.86` workaround.

- `clash` / `clash-b`: two variants (motif head + octave final; brass fifths swelling) rotated as today (`STINGER_GAP` 15 s unchanged).
- `clash-short` (1 bar, 1.6 s): 快 mode (speed 2.5: contact → end ≈ 1.1 s).
- `clash-crit`: the crit beat adds 0.43 s of slow motion (COMBAT-V4 §2.6): beat 1 the head note, beats 2–3 silence (the slow-mo), beat 4 a brass stab + tamtam on the crit impact, bar 2 as `clash`. Played instead of `clash` when `extra.crit`, ignoring `STINGER_GAP`.

API: `SG.Sfx.stinger('clash', { speed, suite: attackerRuler, crit })` — `suite` resolves `suite:<ruler>:clash…`, falling back to the v3 `clash` keys.

## A9. The duel: dueling motifs

The duel keeps 「龙争虎斗」 (one well-tuned piece; its 168 bpm drive is the duel's identity). Two sections are inserted: `Ma` (2 bars) before `A` with the **challenger's** motif and `Mb` (2 bars) before `B` with the **defender's**, each respelled into the duel's 商 mode and played on his own family's lead (reed if his lead plucks), panned −0.3 / +0.3 to match the fighters' sides. A 关羽 vs 康茂德 duel thus trades an erhu phrase and a cornu phrase over the same drums. Loop from `Ma`. Built per pair on demand (the key is `suite-duel:<a>|<b>`; dropped after the duel).

## A10. Succession, houses and the command stings

- When `StrategyAI.succession` installs an heir, `SG.MusicSuites.suiteOf(heir)` is used from the next month start; the news line plays the heir's `fanfare` and `news.succession`. Heirs with a hand suite and a `house` keep the founder's head cell; a generated heir of the same family name as the old ruler (first character equal, both culture han / korea, or the same `FactionInfo` dynasty) gets the founder's **head cell** forced into his first three notes (re-checked for distinctness ≥ 2 against the founder, ≥ 3 against all others).
- DOMESTIC-FX §8.3 asked for this: `SG.Sfx.cmdSting(id, tier, { family, headMotif })` — `family` = the player suite's family (same table, so no change for most rulers); `headMotif` = `SG.MusicSuites.headCell(ruler)` (first three beats, respelled into the sting's mode) replaces the 「霸业」 cell `5,. 1_ 2 5` in the great tiers of 搜索 (talent), 登用, 市场 (legendary) and the minor form in 出征. The rest of every sting is unchanged. This answers DOMESTIC-FX open question 6.
- 同盟 answers: `answerFamily` = the partner ruler's suite family.

## A11. Runtime

```js
SG.MusicSuites.suiteOf(gen | name) → suite record (hand, frozen generated, or generated on the fly)
SG.MusicSuites.ensure(name)        → registers 'suite:<name>:<kind>' for the 12 kinds (≈ 0.6 ms), idempotent
SG.MusicSuites.resolve(kind, { suite, foe }) → key or null   // kinds: map council battle battle-defend victory defeat fanfare clash* ending
SG.MusicSuites.headCell(name)      → notation of the first 3 beats
SG.MusicSuites.duel(a, b)          → key of the dueling-motif duel piece
SG.Sfx.setSuite(name | null)       // the player ruler; null = v3 regional behaviour
SG.Sfx.music(kind, opts)           // unchanged signature; resolves through the suite first when the setting is on
SG.Sfx.fanfare(name, { duck })     // overlay, like stinger(); never interrupts a one-shot victory / defeat
```

- `ensure` is called for the player ruler at game start and on succession, for both sides when a battle starts, for the target when a diplomacy dialog opens. `SG.Music.prepare(key)` pre-renders the samples of the piece about to play (existing ≤ 8 ms slices); the pieces themselves compile with `compileAsync` (measured ≤ 4.4 ms per piece in Node, the 11 non-ending pieces ≈ 11 ms total).
- At most 4 suites are registered at a time (player, current foe, last diplomacy partner, duel pair); older ones are unregistered (their compiled data dropped; samples stay in the shared cache, `SAMPLE_BUDGET` unchanged). Lead swaps only use instruments of the same family, so no new sample sets are needed.
- `audio.js`: `musicImpl(kind)` → if `Sfx.suite` and the setting 君主乐章 → `key = SG.MusicSuites.resolve(kind, …)`; otherwise the v3 `SG.Music.resolve(kind, culture)`. `HOLD_AFTER_ONE_SHOT` and the cross-fades work on the resolved key as today.
- Save data: nothing new (suites are a pure function of the ruler and the frozen data).

## A12. Validation

- **Node** (`node design-v4/audio/suites.js`, becomes `tests/suites.js`): builds the 42 hand suites + every starting ruler + 60 sampled generals (134 suites, 1 611 pieces): `SG.Music.validate` clean (bar lengths, voices, instruments, kit lanes), every instrument exists, lengths: clash 3.0–3.4 s, clash-short 1.5–1.7 s, victory 12–16 s, fanfare 3.0–3.9 s; motif distances and the palette rule as §A6; 4 dueling-motif pieces. Result: **ALL OK** — distances min 3 (董卓 ~ 戈契赫尔), 324 palette pairs distinct.
- **Chromium render** (`node design-v4/audio/render-suites.mjs <dir> 20 <rulers>`): every piece of the 42 hand suites, 20 s (victory / defeat 22 s, clash / fanfare 5 s) → 420 WAVs: **0 NaN**, peak ≤ 0.61, RMS map −25…−21 dB, council −29…−23, battle −20…−17, defend −21…−17, victory −20…−18, defeat −30…−22, fanfare −21…−18, clash −19…−17 dB; render 0.3–1.8 s per piece; `ending` and the +2 dB councils re-rendered for 曹操, 康茂德, 魁头, 区连: 0 NaN, ending −24…−23 dB, councils −27…−22 dB. Targets for `tests/music.html`: no NaN, peak ≤ 0.98, map / council RMS −30…−19 dB, battle / defend −22…−15 dB, short pieces −24…−16 dB. Council pieces outside hua are quieter (no drums): the builder raises their `gain` by +2 dB (× 1.26 when the drums are dropped), added after this render, so their window becomes ≈ −27…−21 dB.
- **Listening**: `samples/music_*.opus` (曹操 / 刘备 / 卑弥呼 / 区连 maps, 康茂德 / 沃洛吉斯 / 魁头 battles, 胡维色迦 defend, 孙坚 victory, 董卓 / 康茂德 fanfares, 曹操 clash).
- **Playtest target**: after one listen to two same-family rulers' map openings, ≥ 70 % of testers tell them apart blind; ≥ 4 of 5 recognise their own ruler's fanfare in a diplomacy dialog without the portrait.

---

# Part B — 人声: voiced barks by ruler, general and language (request 12)

## B1. Principles

- Every strategy command, every main decision and every battle event a player causes has a short voiced line (a bark, 0.4–2.5 s) by the person doing it: the acting general for commands and battle actions, the ruler for war, diplomacy, titles and month end, the other side's ruler or general for their answers.
- Each person speaks **his own language** (§B3), with a voice that fits sex, age and temperament (§B4). Every line is subtitled: the original line and its Chinese meaning (§B10).
- Voices never block input, never delay a vignette, and are rate-limited by priority (§B10.3).
- Wordless efforts (duel grunts, hurt, KO, army shouts) are procedural (§B5), so every general has them at no storage cost.
- Everything works offline from `file://`: no network, no system voices required.

## B2. The cue catalogue

20 **core** cues exist in every lingua; 62 **extended** cues exist in Chinese now and fall back through `fb` to a core cue (or to a procedural cry `~kind`) where a lingua has no translation yet. `SG.Voice.say(gen, cue)` walks the chain. Priority: 3 critical (always plays), 2 important, 1 routine (§B10.3).

| Core cue | 中文 | Gloss | Speaker | Pri | Triggered by |
|---|---|---|---|---|---|
| `ack` | 遵命！/ 末将领命！/ 交给我吧！ | As you command! | gen | 1 | choosing a general for any command |
| `work` | 这就去办！/ 即刻动身！ | On it at once! | gen | 1 | 开发 / 输送 / 交易 start (fallback) |
| `recruit` | 招募义勇！ | Raise volunteers! | gen | 1 | 征兵 start |
| `train` | 操练！/ 列阵！ | Drill! | gen | 1 | 训练 start |
| `search` | 出城寻访。 | I'll go and seek them out. | gen | 1 | 搜索 start |
| `move` | 出发！/ 开拔！ | Move out! | gen | 1 | 移动, battle move |
| `attack` | 全军出击！/ 出征！ | All forces, attack! | ruler | 2 | 出征 confirm |
| `great` | 大功告成！/ 幸不辱命！ | Splendidly done! | gen | 2 | any great tier (DOMESTIC-FX §4) |
| `miss` | 惭愧…… | Forgive me… | gen | 1 | miss tier, failed tactic |
| `charge` | 冲啊！/ 杀！/ 随我冲锋！ | Charge! | gen | 2 | clash contact (attacker), battle attack |
| `retreat` | 撤退！ | Fall back! | gen | 2 | 撤退 command |
| `crit` | 看招！/ 着！ | Take this! | gen | 2 | army crit, duel crit |
| `special` | 接我绝技！ | Behold my art! | gen | 3 | 必杀 cut-in |
| `victory` | 我军大胜！/ 胜了！ | Victory is ours! | gen | 3 | battle won (commander) |
| `defeat` | 可恶…… | Curses… | gen | 3 | battle lost (commander) |
| `duel` | 谁敢与我一战！ | Who dares face me? | gen | 3 | 单挑 challenge |
| `allyYes` | 愿结盟好。 | Let us be allies. | ruler | 2 | 同盟 accepted (target) |
| `allyNo` | 休想！ | Never! | ruler | 2 | 同盟 refused (target) |
| `grant` | 封赏于你！ | I reward you with rank! | ruler | 2 | 授官 ceremony |
| `thanks` | 谢主公隆恩！ | Thank you, my lord! | gen | 2 | 赏赐, 授官 accept, 登用 yes |

Extended cues (DOMESTIC-FX `cmd.*` ids are used verbatim; TITLES' `grant accept promote revoke passedOver rankUp`; COMBAT-V4's `crit finisher parry guardcrush`; ITEMS' reveal cry; the tournament):

| Cue | 中文 (variants) | Gloss | Speaker | Pri | Fallback |
|---|---|---|---|---|---|
| `cmd.develop.start` | 开垦良田！ | Break new fields! | gen | 1 | `work` |
| `cmd.develop.great` | 五谷丰登！ | A bumper harvest! | gen | 2 | `great` |
| `cmd.industry.start` | 起炉鼓铸！ | Fire the furnaces! | gen | 1 | `work` |
| `cmd.industry.great` | 百炼成钢！ | Steel of a hundred foldings! | gen | 2 | `great` |
| `cmd.town.start` | 修缮闾里！ | Mend the wards! | gen | 1 | `work` |
| `cmd.town.great` | 市井繁华！ | The markets are thriving! | gen | 2 | `great` |
| `cmd.recruit.start` | 招募义勇！ | Raise volunteers! | gen | 1 | `recruit` |
| `cmd.recruit.great` | 万众归心！ | Thousands flock to us! | gen | 2 | `great` |
| `cmd.train.start` | 操练！ | Drill! | gen | 1 | `train` |
| `cmd.train.great` | 百战之师！ | An army of a hundred battles! | gen | 2 | `great` |
| `cmd.search.start` | 出城寻访。 | Off to seek talent. | gen | 1 | `search` |
| `cmd.search.none` | 一无所获…… | Nothing found… | gen | 1 | `miss` |
| `cmd.search.talent` | 何人扰我清梦？ | Who disturbs my dreams? | target | 2 | `ack` |
| `cmd.hire.ask` | 先生可愿助我一臂之力？ | Will you lend us your strength? | gen | 1 | `ack` |
| `cmd.hire.yes` | 愿效犬马之劳！ | I will serve you with all I have! | target | 2 | `thanks` |
| `cmd.hire.no` | 恕难从命。 | I must decline. | target | 1 | `allyNo` |
| `cmd.move.start` | 移驻！ | Redeploy! | gen | 1 | `move` |
| `cmd.transport.start` | 押运粮草！ | Escort the supplies! | gen | 1 | `work` |
| `cmd.ally.yes` | 愿结盟好。 | Let us be allies. | target | 2 | `allyYes` |
| `cmd.ally.no` | 休想！ | Never! | target | 2 | `allyNo` |
| `cmd.discord.ok` | 离间之计已成。 | The seeds of discord are sown. | gen | 2 | `great` |
| `cmd.persuade.yes` | 愿效犬马之劳！ | I will serve you! | target | 2 | `thanks` |
| `cmd.attack.start` | 出征！ | To war! | ruler | 2 | `attack` |
| `cmd.reward.thanks` | 谢主公赏赐！ | Thank you for the gift, my lord! | gen | 1 | `thanks` |
| `cmd.trade.start` | 买卖公道！ | A fair price! | gen | 1 | `work` |
| `cmd.appoint.yes` | 定不负所托！ | I will not fail your trust! | gen | 2 | `thanks` |
| `cmd.market.buy` | 好兵器！ | A fine weapon! | gen | 1 | `great` |
| `cmd.endMonth` | 今月之事已毕。 / 传令各城，依计行事。 | This month's work is done. | ruler | 1 | `ack` |
| `ui.generalOpen` | 主公有何吩咐？ / 末将在！ | What are your orders, my lord? | gen | 1 | `ack` |
| `news.succession` | 先主之志，吾当继之！ | I shall carry on my predecessor's cause! | ruler | 3 | `victory` |
| `title.grant` | 封赏于你！ / 加官进爵！ | I raise you in rank! | ruler | 2 | `grant` |
| `title.accept` | 谢主公隆恩！ | Thank you for your grace! | gen | 2 | `thanks` |
| `title.promote` | 更上一层楼！ | Higher still! | gen | 2 | `great` |
| `title.revoke` | 臣……遵命。 | I… obey. | gen | 2 | `miss` |
| `title.passedOver` | 哼…… | Hmph… | gen | 1 | `~grumble` |
| `title.rankUp` | 威加四海！ | Our might spans the four seas! | ruler | 3 | `victory` |
| `item.epic` | 好一件宝物！ | What a treasure! | gen | 2 | `great` |
| `item.legendary` | 神兵在手，天下无敌！ | With this divine weapon, none can stand against me! | gen | 3 | `special` |
| `tourney.enter` | 天下英雄，尽在此地！ | All the heroes under heaven are here! | gen | 2 | `duel` |
| `tourney.win` | 天下第一！ | First under heaven! | gen | 3 | `victory` |
| `bat.select` | 在！ / 何事？ / 请下令！ | Here! | gen | 1 | `ack` |
| `bat.move` | 前进！ / 跟上！ | Advance! | gen | 1 | `move` |
| `bat.attack` | 攻击！ / 杀过去！ | Attack! | gen | 1 | `charge` |
| `bat.tactic` | 看我妙计！ / 中我计了！ | Fall to my stratagem! | gen | 2 | `special` |
| `bat.tacticFail` | 计策被识破了…… | They saw through it… | gen | 1 | `miss` |
| `bat.fire` | 火攻！ | Fire attack! | gen | 2 | `bat.tactic` |
| `bat.enemyRuler` | 敌军主将在此！ | The enemy's lord is here! | gen | 2 | `charge` |
| `bat.reinforce` | 援军到了！ | Reinforcements! | gen | 2 | `great` |
| `bat.lowMorale` | 士气低落…… | Morale is failing… | gen | 1 | `miss` |
| `bat.rout` | 我军败了…… | We are routed… | gen | 2 | `defeat` |
| `bat.captured` | 我……被俘了。 | I am… taken. | gen | 3 | `defeat` |
| `bat.cityTaken` | 城池已破！ | The city has fallen! | gen | 3 | `victory` |
| `bat.hurt` | 呃！ | (pain) | gen | 1 | `~hurt` |
| `clash.crit` | 看招！ | Take this! | gen | 2 | `crit` |
| `duel.accept` | 来得好！ | Come on, then! | target | 3 | `duel` |
| `duel.refuse` | 不与匹夫斗！ | I do not fight common brutes! | target | 2 | `allyNo` |
| `duel.win` | 胜负已分！ | It is decided! | gen | 3 | `victory` |
| `duel.lose` | 我输了…… | I am beaten… | gen | 3 | `defeat` |
| `duel.parry` | 看破！ | Seen through! | gen | 2 | `crit` |
| `duel.guardcrush` | 破！ | Break! | gen | 2 | `crit` |
| `duel.finisher` | 到此为止！ | This ends now! | gen | 3 | `special` |
| `duel.crit` | 着！ | Hit! | gen | 2 | `crit` |

Efforts are always procedural: `light` (轻击 "哈"), `heavy` (重击 "喝啊"), `charge` (蓄力 rising breath), `hurt`, `knockout` (long falling cry), `breath`, `laugh`; army: `crowd.charge` (6–10 detuned voices "呜哦——"), `crowd.cheer`, `crowd.dismay`.

## B3. Who speaks what: the 26 linguas

A **lingua** is a line set plus a phonemizer route plus voices. It is the language of the speaker, chosen by: (1) `FACTION_LINGUA[start faction]` if the general's culture equals that faction's culture; (2) otherwise `CULTURE_LINGUA[culture]` (a Han officer serving 鲜卑 speaks Chinese; a Xianbei serving Cao Cao speaks Mongolic); (3) unaffiliated generals use their culture, refined by their city (south Indian cities → `ta` / `si`, Kush cities → `xmr`, Ériu → `ga`). A captured or hired foreigner keeps his language (subtitles carry the meaning). Setting 「语音语言」 can switch everyone to Chinese.

Historical reasoning, briefly (sources §6): the Kushan court wrote **Bactrian** after Kanishka replaced Greek (Rabatak inscription), while Gandhara and the Tarim wrote **Gandhari Prakrit** in Kharoṣṭhī — the Niya documents of Kroraina (3rd c.) and the Sino-Kharoṣṭhī coins of Khotan ("maharaja yitiraja …") — so 鄯善, 于阗 and 疏勒 speak Prakrit (Khotan and Kashgar with an Iranian, Saka-accented voice), and Kushan barks are Prakrit with Bactrian titles (šao, šaonanošao, bago). The Western Satraps get **Sanskrit** (Rudradaman's Junagadh inscription, c. 150). Xianbei, Wuhuan and Tuoba are (para-)Mongolic, so modern Mongolian stands in; the Xiongnu language is unknown (Yeniseian or Turkic proposals), so a Turkic stand-in (Kazakh). Sarmatian is represented by **Ossetian**, the surviving Scytho-Sarmatian language, but only with a few words. **Ge'ez** for Aksum (royal inscriptions in Ge'ez and Greek). **Meroitic** can be read but is mostly not understood, so Kush uses only decoded words (qore, kdke, Amani, Apedemak). Parthian has no descendant and Middle Persian's descendant is Persian, so both Iranian courts speak formal modern Persian with the attested titles šāhān šāh and pērōz. Aramaic (Syriac of Edessa, Hatran, Elymaic, Mesene) for the Mesopotamian kingdoms; Old South Arabian has too little vocabulary, so Saba / Ḥimyar / Ḥaḍramawt speak Classical Arabic with the god name ʾAlmaqah. **Gothic** (4th-c. Bible, the best-attested East Germanic) for Goths, Vandals and, as a stand-in for Elbe Germanic, the Marcomanni. Welsh and Irish stand in for Brittonic / Pictish and Primitive Irish. Linyi (Champa) was Austronesian (Chamic) → Malay; Funan most likely Mon-Khmer (Austroasiatic) → Vietnamese.

| Code | Lingua | Historical language (190 AD) | Lines written in (stand-in) | Conf. | Phonemizer | Voices (m / f) | Gap |
|---|---|---|---|---|---|---|---|
| `zh` | 汉语 | 东汉洛阳雅言 | 现代普通话（文白相间的古风台词） | A | pinyin | kokoro zm_yunjian / kokoro zf_xiaoyi |  |
| `ja` | 倭语 | 上代日本语（弥生—古坟） | 现代日语（武家口吻） | A | openjtalk | kokoro jm_kumo / kokoro jf_alpha |  |
| `ko` | 韩语 | 夫余—高句丽语、三韩诸语 | 现代韩语（史剧体） | A | espeak:ko | — / ko_KO-kss_low | 无可用男声；KSS 数据为 CC BY-NC-SA |
| `vi` | 扶南语（越语代） | 孟—高棉语（扶南，多数学者之说） | 越南语（同属南亚语系） | A | espeak:vi | — / vi_VN-vais1000 | 无男声 |
| `ms` | 占语（马来语代） | 占语（南岛语系，林邑） | 马来 / 印尼语 | A | espeak:id | — / id_ID-news_tts | 无男声 |
| `mn` | 鲜卑语（蒙古语代） | 准蒙古语（鲜卑、乌桓、拓跋） | 喀尔喀蒙古语 | A | espeak:mn | kk_KZ-issai / — | 借哈萨克语声库读蒙古语音素，可懂度未验证 |
| `kk` | 匈奴语（突厥语代） | 匈奴语（叶尼塞 / 突厥假说，未定） | 哈萨克语 | A | espeak:kk | kk_KZ-issai / — |  |
| `pra` | 犍陀罗俗语 | 尼雅俗语 / 犍陀罗语（鄯善、于阗钱铭）；摩诃剌侘俗语（百乘） | 戏剧俗语（绍罗塞尼）程式句 | B | espeak:hi | ur_PK-fasih / — (Saka accent: fa_IR-amir) | 女声只有 NC 许可（hi priyamvada） |
| `sa` | 梵语 | 古典梵语（西总督鲁德拉达曼朱纳格特铭文） | 古典梵语 | B | espeak:hi | ur_PK-fasih / — |  |
| `txb` | 吐火罗语 | 吐火罗语（车师 / 焉耆 / 龟兹；文献 6 世纪起） | 俗语正文 + 吐火罗乙种词（walo 王、kartse 善、ñakte 诸神） | C | ipa | kk_KZ-issai / — |  |
| `xbc` | 大夏语 | 大夏语（贵霜官方语言，拉巴塔克铭文） | 俗语正文 + 大夏语尊号（šao 王、šaonanošao 王中之王、bago 主） | C | ipa | fa_IR-reza_ibrahim / — |  |
| `ta` | 泰米尔语 | 古泰米尔语（桑伽姆） | 泰米尔语（古雅口吻） | A | espeak:ta | ml_IN-arjun / ml_IN-meera |  |
| `si` | 僧伽罗语 | 僧伽罗俗语（埃卢语） | 僧伽罗语 | A | espeak:si | ml_IN-arjun / — |  |
| `fa` | 波斯语 | 帕提亚语（安息）、中古波斯语（波西斯） | 波斯语（正式体）+ 帕提亚 / 中古波斯尊号（šāhān šāh、pērōz） | A | espeak:fa | fa_IR-amir / — |  |
| `syc` | 阿拉米语 | 叙利亚语（埃德萨）、哈特拉阿拉米语、埃利迈斯阿拉米语 | 古典叙利亚语（拉丁转写 → IPA） | B | ipa | ar_JO-kareem / — |  |
| `ka` | 格鲁吉亚语 | 古格鲁吉亚语（卡特维利语） | 格鲁吉亚语 | A | espeak:ka | — / ka_GE-natia | 无男声 |
| `ar` | 阿拉伯语 | 古阿拉伯语（金达）；萨巴诸国用示巴语 | 古典阿拉伯语 + 示巴语神名尊号（ʾAlmaqah、mlk Sbʾ） | A | espeak:ar | ar_JO-kareem / — |  |
| `gez` | 吉兹语 | 吉兹语（阿克苏姆，与希腊语并用） | 吉兹语（埃塞俄比亚字母，借阿姆哈拉语音系注音） | B | espeak:am | ar_JO-kareem / — |  |
| `xmr` | 麦罗埃语 | 麦罗埃语（文字可读，语义大多未解） | 只用已解读的词：qore 王、kdke 王后、Amani 阿蒙、Apedemak 狮神 | C | ipa | sw_CD-lanfrica / — |  |
| `ber` | 柏柏尔语 | 利比亚—柏柏尔语（巴夸特） | 卡拜尔语少量词（agellid 王、azul、tanemmirt） | C | ipa | ar_JO-kareem / — |  |
| `la` | 拉丁语 | 古典拉丁语 | 古典拉丁语（意大利语声库读拉丁音素） | B | espeak:la | it_IT-riccardo / it_IT-paola |  |
| `grc` | 希腊语 | 通用希腊语（博斯普鲁斯、罗马东方） | 通用希腊语（意大利语声库读古希腊音素） | B | espeak:grc | it_IT-riccardo / it_IT-paola | el_GR-rapunzelina-low 遇希腊文本会胡言 5–8 秒，弃用 |
| `os` | 萨尔马提亚语（奥塞梯语代） | 萨尔马提亚语（雅济吉斯） | 奥塞梯语少量词（хорз 好、размæ 前进、уæлахиз 胜利、бузныг 谢谢、æлдар 主） | C | ipa | kk_KZ-issai / — |  |
| `got` | 哥特语 | 哥特语（乌尔菲拉圣经，4 世纪；马科曼尼为易北日耳曼语） | 哥特语 | B | espeak:is | de_DE-thorsten_emotional#neutral / — (battle: de_DE-thorsten_emotional#angry) |  |
| `cy` | 布立吞语（威尔士语代） | 布立吞语 / 皮克特语（喀里多尼亚） | 威尔士语 | A | espeak:cy | cy_GB-bu_tts#4 / cy_GB-bu_tts#0 |  |
| `ga` | 盖尔语（爱尔兰语代） | 原始爱尔兰语（欧甘铭文） | 爱尔兰语 | A | espeak:ga | cy_GB-bu_tts#4 / cy_GB-bu_tts#0 |  |

| Lingua | Factions (starting) |
|---|---|
| `zh` 汉语 | dong, yuanshao, cao, liubei, sunjian, yuanshu, gongsun, liubiao, liuyan, mateng, taoqian, kongrong, hanfu, zhanglu, zhangyang, liuyao, lukang, menghuo, gongsundu, shixie, zhufu |
| `ko` 韩语 | goguryeo, buyeo, baekje, silla, gaya |
| `ja` 倭语 | yamatai, kuna |
| `mn` 鲜卑语（蒙古语代） | xianbei, dongxianbei, wuhuan, tuoba |
| `kk` 匈奴语（突厥语代） | xiongnu |
| `pra` 犍陀罗俗语 | kroraina, khotan, kashgar, paratarajas, satavahana |
| `txb` 吐火罗语 | jushi |
| `ms` 占语（马来语代） | linyi |
| `vi` 扶南语（越语代） | funan |
| `xbc` 大夏语 | kushan |
| `sa` 梵语 | kshatrapa |
| `ta` 泰米尔语 | chola, pandya, chera |
| `si` 僧伽罗语 | lanka |
| `fa` 波斯语 | parthia, persis |
| `ka` 格鲁吉亚语 | iberia |
| `syc` 阿拉米语 | adiabene, elymais, hatra, osroene, characene |
| `ar` 阿拉伯语 | saba, himyar, hadramawt, kinda |
| `gez` 吉兹语 | aksum |
| `xmr` 麦罗埃语 | kush |
| `ber` 柏柏尔语 | baquates |
| `la` 拉丁语 | roma |
| `grc` 希腊语 | bosporus |
| `os` 萨尔马提亚语（奥塞梯语代） | iazyges |
| `cy` 布立吞语（威尔士语代） | caledonia |
| `ga` 盖尔语（爱尔兰语代） | eriu |
| `got` 哥特语 | marcomanni, goths, vandals |

Confidence: **A** — native-speaker review is enough; **B** — classical language, lines composed from attested grammar and vocabulary (Latin, Greek, Gothic, Sanskrit, Prakrit drama formulas, Syriac, Ge'ez), needs a specialist read; **C** — only attested words and titles plus procedural cries; the subtitle carries the meaning.

## B4. The voice of a general

### B4.1 Base voice

`voices[sex]` of his lingua. Female generals (卑弥呼, 祝融, 蔡琰, 于氏, 神功皇后, 田油津媛, 玛琪亚, 多姆娜, 玛伊莎, 索埃米亚斯, 马梅亚, 阿曼哈利卡, 婆薮, 昌提室利, 罗达格 …, `SG.WorldData.sexOf`) use `f` where the lingua has one. Missing sex (ko, vi, ms, ka have female voices only; mn, kk, pra, sa, si, fa, syc, ar, gez, xmr, ber, os, txb, xbc male only): use the other with a **mild** shift only — female from male `pitchK × 1.12`, presence +2 dB; male from female `pitchK × 0.88`, low shelf +3 dB. Measured: a full female → male shift (formant × 0.86 + pitch × 0.62, total × 0.53) destroys intelligibility (Whisper error → 1.0 for vi, ko, ka, and 0.56 for id), while shifts ≤ 15 % keep it (de / ja / tr unchanged). So ko, vi, ms, ka men sound like low voices, not men, until real male voices exist (open question 2).

### B4.2 Per-general parameters (pure function `SG.Voice.profile(gen)`)

```
age   = currentYear − born   (born unknown: 35; rulers 45)
band  = age < 25 young · < 55 prime · < 70 old · else elder
T     = temperament (§A6 step 1); ruler = isRuler(gen)
h     = fnv1a(name)
pitchK   = B[band] × (1 + ((h & 255)/255 − 0.5) × 0.08),  B = young 1.05 · prime 1.00 · old 0.95 · elder 0.91,  clamp 0.86 … 1.12
           (applied as playbackRate: pitch and length move together, ≤ ±9 %, which reads as a bigger / smaller person)
presence = peaking EQ, f = 1400 + ((h >>> 8) & 255)/255 × 1400 Hz, Q 1.2, gain −3 … +3 dB by (h >>> 16) & 255
body     = low shelf 200 Hz: young −2 · prime 0 · old +1.5 · elder +2.5 dB; martial +1.5 dB
grit     = WaveShaper tanh drive: martial 0.25, old 0.10, + cue shout (battle cues of pri ≥ 2: 0.35), cap 0.6
tremolo  = elder only: gain LFO 5.5 Hz, depth 2.5 %
space    = ruler lines in court cues (attack, allyYes / allyNo, grant, title.*, cmd.endMonth, signature): send 0.18 to a 1.2 s hall
pan      = battle: the unit's screen x mapped to −0.6 … +0.6; duel: the fighter's side ±0.35; strategy: 0
```

Uniqueness: when two speakers with the same base voice are in one scene (duel pair, diplomacy, adjacent units speaking within 3 s) and |ΔpitchK| < 0.05, the second gets × 1.07 (or × 0.93 if that would exceed 1.12). The pack carries one rendering per line per base voice; everything above is runtime DSP (WebAudio nodes; Unity `OnAudioFilterRead`, §5).

### B4.3 Delivery by cue

The offline render uses three delivery presets per line (chosen by the cue's category, not per general): **command** (`length_scale` 1.0), **shout** (battle cues of pri ≥ 2 and war cries: 0.88, then the shout chain: high-pass 120 Hz, compressor 6 : 1, tanh soft clip, +4 dB at 2.5 kHz, 35 / 70 ms slap echo — measured Whisper error unchanged for de, ja, tr), **sigh** (`miss`, `defeat`, `title.revoke`: 1.15, trailing breath). The German Thorsten-emotional model has an `angry` speaker that raises F0 from 106 to 246 Hz on its own — used for Gothic battle cues.

## B5. Procedural cries (formant barks)

`js/voice-cry.js` (port of `tts-lab/formant-bark.js`): Rosenberg glottal pulse + aspiration noise → 4 cascaded Klatt resonators gliding between vowel targets (25 ms smoothing), band-passed noise and bursts for consonants, a shout pitch contour (rise → peak → fall), jitter and age tremolo, tanh saturation. Parameters from the general's profile: F0 base 120 Hz (male) / 210 Hz (female) × pitchK, formants × 1.17 for women, shout amount, roughness (old, martial).

| Kind | Segments (vowel / consonant, s) | Used for |
|---|---|---|
| `light` | h 0.04 · a 0.14 | duel light attack |
| `heavy` | h 0.05 · ə 0.08 · a 0.30 (shout 0.8) | duel heavy release |
| `charge` | h 0.30 rising, a 0.2 | holding K |
| `hurt` | ʔ · ə 0.12 falling | hit taken (duel, `bat.hurt`) |
| `knockout` | a 0.6 falling to ə 0.4, shout 0.6 → 0 | KO |
| `shout` / `roar` | a / o 0.4–0.6, shout 1 | C-language war cries, Gothic `~roar` (Tacitus' barritus) |
| `cheer` / `hm` / `sigh` / `no` / `grumble` / `laugh` | short vowel shapes | C-language fallbacks, `title.passedOver` |
| `crowd.*` | 6–10 cries, detuned ±8 %, staggered 0–120 ms, panned | clash charge, tournament |

Measured in Node: 2–5 ms per 0.8 s cry (RT factor 0.002–0.006), deterministic. Whisper hears nothing intelligible (as intended: "voice-like", not words). Rendered on first use per (speaker, kind, variant) into an `AudioBuffer`, cached (≤ 0.5 MB). Samples: `samples/voice_chong_a.opus`, `voice_ad_arma.opus`.

## B6. Implementation options, measured

### B6.1 (a) Offline neural TTS in this environment

What was possible here: `pip` works; `huggingface.co` is blocked by the egress proxy (Piper's and MMS's own downloads fail), but **GitHub release downloads work**, and the `k2-fsa/sherpa-onnx` `tts-models` release mirrors many Piper, Mimic3 and Kokoro voices as ONNX + tokens + espeak-ng data. Installed: `piper-tts 1.8.0` (embeds espeak-ng with 120+ phonemizer languages including la, grc, ga, gd, cy, am, ti, mn, si, ta, ka, hy), `sherpa-onnx 1.13.8`, `pyopenjtalk-plus` (Japanese), `pypinyin`; 4 CPUs, no GPU. Downloaded and ran 31 voices (≈ 2.6 GB). MMS-TTS (1 100+ languages incl. Latin, Ossetian, Amharic, Tigrinya, Kabyle, Tamil, Khmer, Mongolian) could not be fetched and is **CC BY-NC 4.0** in any case.

Tricks that worked:

- **Cross-lingual phoneme injection**: phonemize the line with espeak-ng in the *source* language and pass the IPA to a voice of a related language through Piper's `[[ … ]]` raw-phoneme block: Latin → Italian (riccardo: Whisper error 0.16), Koine Greek → Italian, Gothic → German / Icelandic, Mongolian → Kazakh, Tamil / Sinhala → Malayalam, Sanskrit / Prakrit → Urdu (MIT-licensed fasih), Ge'ez → Arabic, Old Irish → Welsh; authored IPA for Parthian, Bactrian, Syriac, Ossetian, Meroitic.
- **Japanese**: the sherpa Kokoro front end has no Japanese G2P (it read 「かしこまりました」 as 7.5 s of "Japanese letter"); driving the Kokoro ONNX directly with OpenJTalk phonemes mapped to Kokoro's IPA tokens works (error 0.00 / 0.00 / 0.27 / 0.36 on phonemes).
- **Chinese**: the CC0 `chaowen` Piper voice needs g2pW from Hugging Face; pypinyin (or pinyin written in the line table, which also fixes polyphones like 将 jiāng / jiàng) replaces it.

Measured results (selection of `tts-eval.json`; error = Whisper-base round trip — zh on toneless pinyin syllables, ja on phonemes, others on characters, where script / romanisation mismatches inflate it):

| Clip | Lingua | Model (licence) | Text | Length | Median F0 | ASR heard | Error |
|---|---|---|---|---|---|---|---|
| zh_kokoro_yunjian_long | zh | kokoro-multi-lang-v1_0 (Apache-2.0) | 主公，今年秋收丰盛，可以出兵了。 | 3.172 s | 117 Hz | 主共今年秋秋風上可以出了 | 0.23 syl |
| zh_kokoro_xiaoyi_long | zh | kokoro-multi-lang-v1_0 (Apache-2.0) | 主公，今年秋收丰盛，可以出兵了。 | 3.258 s | 329 Hz | 公,今年秋收,可以出了。 | 0.31 syl |
| zh_chaowen_jiang | zh | zh_CN-chaowen-medium (CC0) | 将军，末将愿往！ | 2.229 s | 174 Hz | 將軍將願望 | 0.17 syl |
| zh_huayan_chong | zh | zh_CN-huayan-medium (Unknown) | 全军突击，冲啊！ | 1.525 s | 235 Hz | 全軍突擊啊 | 0.17 syl |
| zh_fanchen_s5_chong | zh | vits-zh-hf-fanchen-C (unknown) | 全军突击，冲啊！ | 1.523 s | 296 Hz | 《神经土地》 | 0.83 syl |
| zh_huayan_zunming | zh | zh_CN-huayan-medium (Unknown) | 遵命！ | 0.557 s | 286 Hz | 真名 | 0.5 syl |
| ja_kumo_kashikomari | ja | kokoro-direct (Apache-2.0) | かしこまりました！ | 2.05 s | 91 Hz | かしこまりました。 | 0 ph |
| ja_kumo_kakare | ja | kokoro-direct (Apache-2.0) | 者ども、かかれ！ | 2.025 s | 106 Hz | ものども書かれ | 0 ph |
| ja_alpha_himiko | ja | kokoro-direct (Apache-2.0) | 鬼道をもって国を治めん。 | 2.6 s | 276 Hz | 起動を持ってくさめ | 0.27 ph |
| ja_kokoro_kumo_kashikomari | ja | kokoro-multi-lang-v1_0 (Apache-2.0) | かしこまりました！ | 7.492 s | 105 Hz | Japanese Letter | 2.06 ph |
| ko_kss_bunbu | ko | ko_KO-kss_low (KSS dataset CC BY-NC-SA 4.) | 분부대로 하겠습니다! | 1.253 s | 302 Hz | 로 하겠습니다. | 0.33 |
| ko_kss_long | ko | ko_KO-kss_low (KSS dataset CC BY-NC-SA 4.) | 전군, 돌격하라! 적을 물리쳐라! | 2.446 s | 269 Hz | 정 돌하다, 적을 물쳐 | 0.5 |
| vi_vais_long | vi | vi_VN-vais1000-medium (see card) | Toàn quân, xung phong! Đánh tan quân giặc! | 2.084 s | 245 Hz | Tôn quân xung phòng đánh tàn | 0.38 |
| id_news_serbu | id | id_ID-news_tts-medium (See URL) | Siap! Serbu! | 1.37 s | 232 Hz | siap, serbu | 0 |
| kk_issai_alga | kk | kk_KZ-issai-high (CC-BY-4.0) | Алға! Шабуыл! | 1.04 s | 136 Hz | ала шаб | 0.4 |
| mn_via_kk_uragshaa | mn | kk_KZ-issai-high (CC-BY-4.0) | Урагшаа! Довтол! | 1.84 s | 112 Hz | Shaka Tapa | 1 |
| fa_reza_bepish | fa | fa_IR-reza_ibrahim-medium (CC0) | به پیش! حمله! | 1.428 s | 95 Hz | ده پیش حمن | 0.33 |
| ar_kareem_hujum | ar | ar_JO-kareem-medium (See URL) | إلى الأمام! هجوم! | 1.942 s | 110 Hz | إلى الأمام حجوم | 0.08 |
| it_ricc_avanti | it | it_IT-riccardo-x_low (See URL) | Avanti! All'attacco! | 1.848 s | 156 Hz | Aranti al attacco | 0.12 |
| la_via_it_adarma | la | it_IT-riccardo-x_low (See URL) | Ad arma! Impetum facite! | 1.801 s | 155 Hz | Adharma impetun fakite. | 0.16 |
| it_paola_la | la | it_IT-paola-medium (See URL) | Ave, Caesar! Ad arma! | 1.639 s | 210 Hz | awi keisar va darma | 0.4 |
| la_via_el_adarma | la | el_GR-rapunzelina-low (CC0) | Ad arma! Impetum facite! | 1.713 s | 209 Hz | Andraaama tipetonfaket | 0.58 |
| grc_via_el_alalai | grc | el_GR-rapunzelina-low (CC0) | Ἀλαλαί! Ὁρμᾶτε, ὦ ἄνδρες! | 2.796 s | 195 Hz | Αλλά λάι, πορμάται, ο γάν | 0.84 |
| el_rap_embros | el | el_GR-rapunzelina-low (CC0) | Εμπρός! Επίθεση! | 7.727 s | 200 Hz | Ακθύφε, γυρόγραφα, θυφε, θυφ | 2.54 |
| de_thor_vorwaerts | de | de_DE-thorsten-medium (CC0) | Vorwärts! Zum Angriff! | 1.408 s | 106 Hz | Vorwärts zum Angriff. | 0 |
| de_emo_angry_vorwaerts | de | de_DE-thorsten_emotional-medium (CC0) | Vorwärts! Zum Angriff! | 1.753 s | 246 Hz | vorwärts zum Angriff. | 0 |
| non_is_steinn | is | is_IS-steinn-medium (See URL) | Fram! Til orrustu! | 1.499 s | 130 Hz | Tram til ogrust | 0.21 |
| tr_dfki_ileri | tr | tr_TR-dfki-medium (see card) | İleri! Saldırın! | 1.577 s | 100 Hz | İleri saldırın. | 0 |
| sw_lan | sw | sw_CD-lanfrica-medium (See URL) | Mbele! Shambulia! | 1.484 s | 127 Hz | Bel e Shambulia | 0.07 |
| cy_bu_ymlaen | cy | cy_GB-bu_tts-medium (CC-BY) | Ymlaen! I'r gad! | 1.358 s | 202 Hz | a mleini perchad. | 0.64 |
| ka_natia_long | ka | ka_GE-natia-medium (See LICENSE file) | დიახ, ბატონო! გაუმარჯოს! | 2.624 s | 176 Hz | 10. Padona, cao marjos | 1 |
| ml_arjun_ta | ta | ml_IN-arjun-medium (See URL) | முன்னேறு! தாக்கு! | 1.385 s | 136 Hz | முனேர் ταக | 0.64 |
| hi_priyam_sa | sa | hi_IN-priyamvada-medium (see card) | जयतु महाराजः! | 1.301 s | 176 Hz | Jetu Maharajat | 1.18 |
| pra_via_ur_fasih | sa | ur_PK-fasih-medium (mit) | जं देवो आणवेदि। | 1.219 s | 108 Hz | Jandevo Convade | 1.17 |

Reading the numbers honestly:

- Clear wins: German, Turkish, Indonesian, Swahili, Arabic, Italian, Latin-through-Italian, Japanese (Kokoro direct), Icelandic; Chinese and Korean sentences of 6+ syllables.
- Chinese **two-syllable barks** (遵命、开垦) score badly in every model, partly because Whisper-base cannot recognise isolated two-syllable Mandarin without context; they sound plausible but this is the main quality risk: the QA gate (§B7.2) must use a stronger ASR or human ears for zh.
- Several models are unusable: `el_GR-rapunzelina-low` hallucinates 5–8 s of babble on Greek text (it works on injected phonemes, but Greek is routed through Italian instead); Kokoro's sherpa front end for Japanese; the multi-speaker Chinese `fanchen-C` and `vits-zh-ll` are weaker than Kokoro and chaowen.
- Licences decide as much as quality: Kokoro Apache-2.0, Thorsten / chaowen / amir / reza CC0, fasih MIT, issai / iseke / vais1000 / bu_tts CC-BY; KSS Korean, both Hindi IndicTTS voices, the Turkish dfki and `ar_JO-SA_miro` are **non-commercial**; several cards only say "see URL" (riccardo, paola, kareem, natia, arjun, meera, lanfrica, news_tts) and must be checked before shipping (open question 1).
- Speed: after warm-up Piper medium voices synthesize at RTF 0.05–0.15, Kokoro direct ≈ 0.25, Kokoro through sherpa 0.6–1.0, so the whole v4.0 set (≈ 17 min of speech) renders in under 5 minutes on 4 cores.
- Size: Opus mono 16 kbps ≈ 2.2 KB/s with the container; Whisper error at 12 / 16 / 24 / 32 kbps for nine clips: identical at 16–32 kbps; Latin rises from 0.16 to 0.32 at 12 kbps → **16 kbps**.
- Prosody: neural TTS reads, it does not shout; the shout chain and the angry Thorsten speaker help, but battle cries stay calmer than a voice actor. Mitigated by the procedural crowd under them.

### B6.2 (b) Web Speech API

`speechSynthesis.getVoices()` exposes whatever the OS and browser ship: name, lang, `localService`, `default`. Findings (Readium's cross-browser notes, DEV community tests): lists differ between browsers on the same device and between OSes; Chrome fills them asynchronously (`voiceschanged`); Chrome on Android does not list the user's voices; voice packs often need a manual download deep in system settings; `voiceURI` / `name` are not stable identifiers; Chrome's "Google …" voices are network voices (`localService: false`), which breaks the no-network rule. There is no Latin, Ancient Greek, Sanskrit, Prakrit, Gothic, Syriac, Ge'ez, Bactrian or Mongolian voice on mainstream platforms; sex and age can only be guessed from names; pitch / rate parameters behave differently per engine; nothing can be validated ahead of time; the Unity port has no equivalent (it would need SAPI / AVSpeechSynthesizer / Android TextToSpeech plugins, each different again). Verdict: **not for barks**. Offered only as an accessibility option 「朗读」 for long text (month news, reports, item descriptions), restricted to `localService` voices of the UI language (zh), off by default.

### B6.3 (c) Procedural formant barks

Zero storage, deterministic, unlimited variety per general, 2–5 ms per cry, trivial to port to C#; but not intelligible as words. Right for efforts, crowds, C-language fallbacks and as the instant fallback while a pack is still loading.

### B6.4 Recommendation

**(a) for every word + (c) for every non-word + subtitles**, with the TTS run offline by the build pipeline into compact Opus packs that both platforms play. The Web Speech API is an optional read-aloud only. Quality-critical gaps (male ko / vi / ms / ka voices, licence-unclear voices, B / C language review) are addressed by replacing individual voices in the pipeline later: the runtime does not care where a clip came from, so a recorded human line can drop into the same pack.

## B7. The pipeline and the packs

### B7.1 Pack format

The game runs from `file://`, where `fetch` / XHR of local files are blocked in Chromium, so packs are **scripts** (like the music files `audio.js` loads on demand):

```js
// voice/zh-m.js   (one per lingua × base voice; v4.0: 35 packs (34 + the Saka-accented Prakrit voice))
SG.VoicePacks.add({ id: 'zh-m', lingua: 'zh', voice: 'kokoro-multi-lang-v1_0#zm_yunjian', codec: 'opus', sr: 24000,
  clips: { ack: [[0, 2210, 610, -16.1], [2210, 2604, 770, -15.8], …], charge: [...], … } },   // [offset, length, ms, rmsDb] per variant
  'T2dnUwACAAAAAAAAAAB…');   // base64 of the concatenated clips; each clip is a complete Ogg Opus file
```

- One `<script>` per pack, loaded lazily: the player's lingua at game start, the foe's when a battle or diplomacy with another lingua starts (≤ 150 KB each), all others never unless met.
- Decode per clip on demand: base64 → `Uint8Array` slice → `decodeAudioData` (async, off the main thread); LRU cache of decoded `AudioBuffer`s ≤ **6 MB** (≈ 60 s of 24 kHz mono float); on idle after load the 20 core cues of the player's lingua are pre-decoded.
- **Codec probe** at `SG.Voice.init()`: decode a 300-byte embedded Opus clip; if it fails (older Safari: WebKit bugs show `decodeAudioData` lagging behind `<audio>` for Opus), switch to the MP3 packs (`voice/mp3/zh-m.js`, 32 kbps mono 22.05 kHz).
- `voice/manifest.js`: pack ids, sizes, checksums, lingua → packs; `voice/LICENSES.md`: every model and its licence (generated).

### B7.2 Build steps (`tools/voice-build/`, offline, run by a developer, outputs committed)

1. `node tools/voice-build/export-lines.js` → `lines.json`: every (lingua, base voice, cue, variant, text, route, delivery, IPA override) from `js/voice-data.js`.
2. `python3 -I tools/voice-build/synth.py lines.json --models <dir>`: Piper 1.8 for Piper / Mimic3 voices (`[[ IPA ]]` for cross-lingual routes and authored IPA), Kokoro ONNX direct for zh / ja (pinyin / OpenJTalk front ends of `tts-lab/`), sherpa-onnx for Mimic3 Korean; delivery presets of §B4.3.
3. Post: trim to −45 dB (keep 20 ms head, 50 ms tail), loudness: RMS −16 dBFS ± 1, peak ≤ −1 dBFS; shout chain for shout delivery.
4. `qa.py` gates: (i) length sanity: 0.3 s ≤ length ≤ max(0.6 s, 2.5 × expected), expected from characters ÷ the lingua's rate — catches hallucinating models like the Greek one; (ii) ASR round trip (Whisper; base is enough for A languages except zh, where whisper-small or human review is required): error > 0.5 → listed for a human; (iii) clipping, NaN, silence; (iv) licence: a model whose licence is NC or unknown fails the release build unless `--allow-nc` (dev builds).
5. `pack.py`: encode `libopus` 16 kbps mono, `-application voip`, 24 kHz; MP3 fallback with LAME 32 kbps; write the packs, manifest and licences.

Timing: synthesis < 5 min for v4.0 (≈ 17 min of audio, 4 cores), QA ASR ≈ 3 min (base) or ≈ 15 min (small).

### B7.3 Runtime (`js/voice.js`)

```js
SG.Voice.init()                          // settings, manifest, codec probe; never throws
SG.Voice.say(gen, cue, opts) → Promise<{ dur, text, zh, lingua } | null>
    // opts: { pri, pan, shout, force, subtitle = true, scene }  — resolves at the end of the clip; null if dropped / off
SG.Voice.cry(gen, kind, opts)            // procedural efforts (§B5)
SG.Voice.crowd(kind, n, opts)            // army shouts
SG.Voice.profile(gen) → { lingua, voice, pitchK, presence, body, grit, tremolo, pan, space }   // pure, for tests
SG.Voice.preload(linguas) → Promise
SG.Voice.settings                        // §B10.4
SG.Voice.busy → bool
```

Graph per voice (max 2 alive): `AudioBufferSource(playbackRate = pitchK)` → low shelf (body) → peaking (presence) → `WaveShaper` (grit, 2× oversample) → gain (tremolo) → `StereoPanner` → **voice bus** (high-pass 90 Hz → `DynamicsCompressor` −20 dB, 3 : 1, knee 6, attack 3 ms, release 120 ms → gain `0.9 × voiceVolume` → master), plus a send to the hall convolver (shared 1.2 s IR, generated like the music reverb). `say()` never awaits decoding for more than 120 ms: if the clip is not decoded by then, a procedural cry of the cue's category plays instead and the subtitle shows.

## B8. The lines

Core cues in every lingua (`|` separated variants; `~kind` = procedural cry). East Asia and the steppe:

| Cue | 中文 | 倭语 `ja` | 韩语 `ko` | 扶南语（越语代） `vi` | 占语（马来语代） `ms` | 鲜卑语（蒙古语代） `mn` | 匈奴语（突厥语代） `kk` | Gloss |
|---|---|---|---|---|---|---|---|---|
| `ack` | 遵命！ / 末将领命！ / 交给我吧！ | ははっ！ / かしこまりました！ | 분부대로 하겠습니다! | Tuân lệnh! | Siap, Tuanku! | Зарлигаар! | Құп болады! | As you command! |
| `work` | 这就去办！ / 即刻动身！ | ただちに取りかかります！ | 즉시 시행하겠습니다! | Thần đi làm ngay! | Segera dilaksanakan! | Одоо хийнэ! | Қазір орындаймын! | On it at once! |
| `recruit` | 招募义勇！ | 兵を募れ！ | 병사를 모아라! | Chiêu mộ nghĩa binh! | Kumpulkan pasukan! | Цэрэг дайчил! | Әскер жина! | Raise volunteers! |
| `train` | 操练！ / 列阵！ | 鍛えよ！ | 훈련하라! | Luyện binh! | Berlatih! | Бэлтгэл хий! | Жаттық! | Drill! |
| `search` | 出城寻访。 | 人材を探してまいります。 | 인재를 찾아 나서겠습니다. | Thần đi tìm hiền tài. | Hamba pergi mencari orang berbakat. | Хүн олохоор явъя. | Дарын іздеп барамын. | I'll go and seek them out. |
| `move` | 出发！ / 开拔！ | 出立！ | 출발하라! | Xuất phát! | Berangkat! | Мордъё! | Аттан! | Move out! |
| `attack` | 全军出击！ / 出征！ | 全軍、進め！ | 전군, 진격하라! | Toàn quân xuất kích! | Seluruh pasukan, serbu! | Бүх цэрэг, довтол! | Барша әскер, шабуыл! | All forces, attack! |
| `great` | 大功告成！ / 幸不辱命！ | 見事、成し遂げました！ | 큰 공을 세웠습니다! | Đại công cáo thành! | Berhasil gemilang! | Амжилттай! | Ұлы жеңіс! | Splendidly done! |
| `miss` | 惭愧…… | 面目ない…… | 면목이 없습니다…… | Thần hổ thẹn… | Ampun, Tuanku… | Уучлаарай… | Кешіріңіз… | Forgive me… |
| `charge` | 冲啊！ / 杀！ / 随我冲锋！ | かかれ！ / 者ども、かかれ！ | 돌격하라! | Xung phong! | Serbu! | Хурай! / Довтол! | Алға! / Шабуыл! | Charge! |
| `retreat` | 撤退！ | 退け！ | 후퇴하라! | Rút lui! | Mundur! | Ухар! | Шегін! | Fall back! |
| `crit` | 看招！ / 着！ | くらえ！ | 받아라! | Đỡ này! | Rasakan ini! | Энийг ав! | Мә, ал! | Take this! |
| `special` | 接我绝技！ | 我が秘技を見よ！ | 나의 비기를 보아라! | Hãy xem tuyệt kỹ của ta! | Lihatlah jurus hamba! | Миний ур чадварыг хар! | Менің өнерімді көр! | Behold my art! |
| `victory` | 我军大胜！ / 胜了！ | 勝ったぞ！ | 우리가 이겼다! | Ta thắng rồi! | Kita menang! | Бид яллаа! | Жеңдік! | Victory is ours! |
| `defeat` | 可恶…… | 無念…… | 분하다…… | Đáng hận… | Celaka… | Харамсалтай… | Қап… | Curses… |
| `duel` | 谁敢与我一战！ | 我と勝負せよ！ | 누가 나와 겨루겠느냐! | Ai dám đấu với ta! | Siapa berani melawanku! | Хэн надтай тулалдана! | Кім менімен жекпе-жекке шығады! | Who dares face me? |
| `allyYes` | 愿结盟好。 | 盟を結ぼう。 | 동맹을 맺읍시다. | Xin kết đồng minh. | Mari bersekutu. | Холбоотон болъё. | Одақ құрайық. | Let us be allies. |
| `allyNo` | 休想！ | 断る！ | 어림없다! | Đừng hòng! | Tidak sudi! | Үгүй! | Жоқ! | Never! |
| `grant` | 封赏于你！ | そなたに位を授ける！ | 그대에게 벼슬을 내리노라! | Ta phong tước cho ngươi! | Kuberi engkau gelar! | Чамд цол олгоё! | Саған атақ беремін! | I reward you with rank! |
| `thanks` | 谢主公隆恩！ | ありがたき幸せ！ | 성은이 망극하옵니다! | Đội ơn chúa công! | Terima kasih, Tuanku! | Баярлалаа, хаан минь! | Рақмет, тақсыр! | Thank you, my lord! |

South and Central Asia (Prakrit lines follow the Śaurasenī drama formulas — 「जं देवो आणवेदि」, 「जअदु」, 「हद्धी」 are attested; Kroraina's ruler is addressed with the Niya letters' «mahanuava maharaya»):

| Cue | 中文 | 犍陀罗俗语 `pra` | 梵语 `sa` | 泰米尔语 `ta` | 僧伽罗语 `si` | 吐火罗语 `txb` | 大夏语 `xbc` | Gloss |
|---|---|---|---|---|---|---|---|---|
| `ack` | 遵命！ / 末将领命！ / 交给我吧！ | जं देवो आणवेदि! | यथाज्ञापयति देवः! | உத்தரவு, அரசே! | එසේය, දේවයන් වහන්ස! | Walo! | Bago šao! | As you command! |
| `work` | 这就去办！ / 即刻动身！ | दाणिं करेमि! | इदानीमेव करोमि! | உடனே செய்கிறேன்! | දැන්ම කරනවා! | Kartse! | ~hm | On it at once! |
| `recruit` | 招募义勇！ | सेण्णं संगेण्हध! | सैन्यं संगृह्णीत! | படை திரட்டு! | හමුදාව රැස් කරනු! | ~shout | ~shout | Raise volunteers! |
| `train` | 操练！ / 列阵！ | अब्भसह! | अभ्यसत! | பயிற்சி செய்! | පුහුණු වෙනු! | ~shout | ~shout | Drill! |
| `search` | 出城寻访。 | गुणिं अण्णेसामि। | गुणिनमन्विष्यामि। | அறிஞரைத் தேடிச் செல்கிறேன். | දක්ෂයන් සොයා යමි. | ~hm | ~hm | I'll go and seek them out. |
| `move` | 出发！ / 开拔！ | गच्छामो! | प्रस्थीयताम्! | புறப்படு! | පිටත් වෙමු! | ~shout | ~shout | Move out! |
| `attack` | 全军出击！ / 出征！ | सव्वे, पहरह! | सर्वसेने, आक्रमत! | படையே, தாக்கு! | මුළු හමුදාවම, පහර දෙනු! | Walo! ~roar | Šaonanošao! | All forces, attack! |
| `great` | 大功告成！ / 幸不辱命！ | सिद्धं! | सिद्धम्! | பெருவெற்றி! | ජයග්‍රහණයයි! | Kartse! | ~cheer | Splendidly done! |
| `miss` | 惭愧…… | खमीअदु… | क्षम्यताम्… | மன்னியுங்கள்… | සමාවෙන්න… | ~sigh | ~sigh | Forgive me… |
| `charge` | 冲啊！ / 杀！ / 随我冲锋！ | जअदु! / पहरह! | जय! / आक्रमत! | முன்னேறு! / தாக்கு! | ඉදිරියට! | ~roar | Šaonanošao! | Charge! |
| `retreat` | 撤退！ | ओसरह! | अपसरत! | பின்வாங்கு! | පසු බසිනු! | ~shout | ~shout | Fall back! |
| `crit` | 看招！ / 着！ | गेण्ह! | गृहाण! | வாங்கிக்கொள்! | මෙන්න! | ~shout | ~shout | Take this! |
| `special` | 接我绝技！ | पेक्ख मे कोसल्लं! | पश्य मे कौशलम्! | என் கலையைப் பார்! | මගේ හැකියාව බලනු! | Ñakte! | Bago! | Behold my art! |
| `victory` | 我军大胜！ / 胜了！ | विजओ अम्हाणं! | विजयोऽस्माकम्! | நாம் வென்றோம்! | අපි දිනුවා! | Kartse! Kartse! | Šaonanošao! | Victory is ours! |
| `defeat` | 可恶…… | हद्धी… | हा धिक्… | ஐயோ… | අහෝ… | ~sigh | ~sigh | Curses… |
| `duel` | 谁敢与我一战！ | को मए सह जुज्झिस्सदि! | को मया सह योत्स्यते! | என்னுடன் போரிட யார் துணிவார்! | මා සමඟ සටනට කවුද එන්නේ! | ~roar | ~roar | Who dares face me? |
| `allyYes` | 愿结盟好。 | संधिं करेम्ह। | सन्धिं कुर्मः। | நட்புக் கொள்வோம். | මිත්‍ර වෙමු. | Kartse. | ~hm | Let us be allies. |
| `allyNo` | 休想！ | ण कदा वि! | न कदापि! | ஒருபோதும் இல்லை! | කිසිදා නැත! | ~no | ~no | Never! |
| `grant` | 封赏于你！ | पदं दे देमि! | पदं ते ददामि! | உனக்குப் பட்டம் தருகிறேன்! | ඔබට පදවියක් දෙමි! | Walo! | Bago šao! | I reward you with rank! |
| `thanks` | 谢主公隆恩！ | अणुग्गहीदो म्हि! | अनुगृहीतोऽस्मि! | நன்றி, அரசே! | ජය වේවා! | Walo! | Bago šao! | Thank you, my lord! |

Iran, Mesopotamia, Caucasus, Arabia and Africa (Syriac in transliteration, rendered through authored IPA; Saba / Ḥimyar / Ḥaḍramawt rulers prefix ʾAlmaqah, the Parthian and Persian kings šāhanšāh / pērōz, via `RULER_TAG`):

| Cue | 中文 | 波斯语 `fa` | 阿拉米语 `syc` | 格鲁吉亚语 `ka` | 阿拉伯语 `ar` | 吉兹语 `gez` | 麦罗埃语 `xmr` | 柏柏尔语 `ber` | Gloss |
|---|---|---|---|---|---|---|---|---|---|
| `ack` | 遵命！ / 末将领命！ / 交给我吧！ | فرمان شماست! | ʔēn, māry! | დიახ, ბატონო! | سمعاً وطاعة! | እወ፡ እግዚእየ! | Qore! | Ih, agellid! | As you command! |
| `work` | 这就去办！ / 即刻动身！ | هم‌اکنون! | hāšā ʕābed nā! | ახლავე! | حالاً! | ይእዜ እገብር! | ~hm | Ih! | On it at once! |
| `recruit` | 招募义勇！ | سپاه گرد آورید! | kannešw ḥaylā! | შეკრიბეთ ლაშქარი! | اجمعوا الجند! | አስተጋብኡ ሐራ! | ~shout | ~shout | Raise volunteers! |
| `train` | 操练！ / 列阵！ | تمرین کنید! | ʔetdarrašw! | ივარჯიშეთ! | تدرّبوا! | ተመሀሩ! | ~shout | ~shout | Drill! |
| `search` | 出城寻访。 | به جستجوی کاردانان می‌روم. | ʔāzel nā wa-bāʕē nā. | წავალ და მოვძებნი. | سأبحث عن الأكفاء. | አሐውር ወአኀሥሥ። | ~hm | ~hm | I'll go and seek them out. |
| `move` | 出发！ / 开拔！ | برویم! | nēzal! | წავედით! | هيا بنا! | ንሑር! | ~shout | Ɣer zdat! | Move out! |
| `attack` | 全军出击！ / 出征！ | همه، یورش! | kulkon, qrubw! | ყველამ, შეუტიეთ! | يا قوم، احملوا! | ኵልክሙ፡ ተቃተሉ! | Apedemak! | Ɣer zdat! | All forces, attack! |
| `great` | 大功告成！ / 幸不辱命！ | آفرین! | šappīr! | საუცხოოა! | تمّ الأمر! | ሠናይ! | Amani! | Agellid! | Splendidly done! |
| `miss` | 惭愧…… | ببخشید… | šbuq lī… | მაპატიეთ… | عفواً… | ስረይ ሊተ… | ~sigh | ~sigh | Forgive me… |
| `charge` | 冲啊！ / 杀！ / 随我冲锋！ | به پیش! / یورش! | lqrābā! | წინ! / გაუმარჯოს! | إلى الأمام! / احملوا! | ሞዐ! | Apedemak! | Ɣer zdat! | Charge! |
| `retreat` | 撤退！ | عقب‌نشینی! | hpukw! | უკან დაიხიეთ! | تراجعوا! | ተመየጡ! | ~shout | ~shout | Fall back! |
| `crit` | 看招！ / 着！ | بگیر! | sab! | აჰა! | خذها! | ንሣእ! | ~shout | ~shout | Take this! |
| `special` | 接我绝技！ | هنر مرا ببین! | ḥzaw ʔummānūtī! | იხილეთ ჩემი ხელოვნება! | انظر إلى مهارتي! | ርኢ ጥበብየ! | Apedemak! | ~roar | Behold my art! |
| `victory` | 我军大胜！ / 胜了！ | پیروز شدیم! | zkayn! | გავიმარჯვეთ! | انتصرنا! | ሞዕነ! | Amani! Amani! | Agellid! Agellid! | Victory is ours! |
| `defeat` | 可恶…… | دریغا… | way lī… | ვაიმე… | يا للخسارة… | አሌ ሊተ… | ~sigh | ~sigh | Curses… |
| `duel` | 谁敢与我一战！ | کیست که با من نبرد کند؟ | mannū neqrob ʕammī? | ვინ შემებრძოლება? | من يبارزني؟ | መኑ ይትቃተለኒ? | Apedemak! | ~roar | Who dares face me? |
| `allyYes` | 愿结盟好。 | پیمان ببندیم. | nqīm qyāmā. | მოკავშირეები ვიყოთ. | فلنتحالف. | ንግበር ኪዳነ። | Qore. | Azul. | Let us be allies. |
| `allyNo` | 休想！ | هرگز! | lā lʕālam! | არასოდეს! | أبداً! | አልቦ! | ~no | Ala! | Never! |
| `grant` | 封赏于你！ | تو را لقب می‌دهم! | yāheb nā lāk ʔīqārā! | წოდებას გიბოძებ! | أمنحك لقباً! | እሁበከ ክብረ! | Qore! | Agellid! | I reward you with rank! |
| `thanks` | 谢主公隆恩！ | سپاس، شاهنشاه! | ṭaybūtāk, māry! | მადლობა, ბატონო! | شكراً يا مولاي! | ብሩክ፡ ንጉሥ! | Qore! Qore! | Tanemmirt, agellid! | Thank you, my lord! |

Rome, the Bosporus and the north:

| Cue | 中文 | 拉丁语 `la` | 希腊语 `grc` | 哥特语 `got` | 布立吞语（威尔士语代） `cy` | 盖尔语（爱尔兰语代） `ga` | 萨尔马提亚语（奥塞梯语代） `os` | Gloss |
|---|---|---|---|---|---|---|---|---|
| `ack` | 遵命！ / 末将领命！ / 交给我吧！ | Ita, domine! / Fiat! | Ναί, δέσποτα! | Ja, frauja! | Ie, arglwydd! | Sea, a thiarna! | Хорз, æлдар! | As you command! |
| `work` | 这就去办！ / 即刻动身！ | Statim faciam! | Εὐθέως ποιήσω! | Suns taujau! | Ar unwaith! | Láithreach! | Хорз! | On it at once! |
| `recruit` | 招募义勇！ | Milites conscribite! | Στρατολογεῖτε! | Galaþoþ hari! | Casglwch y llu! | Bailígí an slua! | ~shout | Raise volunteers! |
| `train` | 操练！ / 列阵！ | Exercemini! | Γυμνάζεσθε! | Arbaidjiþ! | Ymarferwch! | Cleachtaigí! | ~shout | Drill! |
| `search` | 出城寻访。 | Viros idoneos quaeram. | Ζητήσω ἄνδρας ἀγαθούς. | Sokja mannans godans. | Af i chwilio. | Rachaidh mé ar lorg. | ~hm | I'll go and seek them out. |
| `move` | 出发！ / 开拔！ | Proficiscimur! | Ἄγωμεν! | Gaggam! | Ymaith! | Ar aghaidh linn! | Размæ! | Move out! |
| `attack` | 全军出击！ / 出征！ | Omnes, impetum facite! | Πάντες, ὁρμᾶτε! | Gaggiþ du waihjon! | Pawb, ymosodwch! | Ionsaígí go léir! | Размæ! | All forces, attack! |
| `great` | 大功告成！ / 幸不辱命！ | Optime factum! | Κάλλιστα! | Waila! | Campus! | Ar fheabhas! | Хорз! | Splendidly done! |
| `miss` | 惭愧…… | Ignosce mihi… | Συγγνώμην… | Wai… | Mae'n ddrwg gen i… | Is oth liom… | ~sigh | Forgive me… |
| `charge` | 冲啊！ / 杀！ / 随我冲锋！ | Ad arma! / Feri! | Ἀλαλαί! / Ὁρμᾶτε! | ~roar / Gaggiþ! | I'r gad! | Abú! | Размæ! | Charge! |
| `retreat` | 撤退！ | Receptui canite! | Ἀναχωρεῖτε! | Gawandeiþ! | Encilio! | Cúlaígí! | ~shout | Fall back! |
| `crit` | 看招！ / 着！ | Hoc habe! | Λαβέ! | Nim! | Cymer hwn! | Seo duit! | ~shout | Take this! |
| `special` | 接我绝技！ | Ecce artem meam! | Ἰδοὺ ἡ τέχνη μου! | Saiƕ! | Gwelwch fy nawn! | Féach ar mo chumas! | ~roar | Behold my art! |
| `victory` | 我军大胜！ / 胜了！ | Vicimus! | Νενικήκαμεν! | Sigis! | Buddugoliaeth! | Bua! | Уæлахиз! | Victory is ours! |
| `defeat` | 可恶…… | Vae mihi… | Οἴμοι… | Wai mis… | Gwae fi… | Mo léan… | ~sigh | Curses… |
| `duel` | 谁敢与我一战！ | Quis mecum pugnabit? | Τίς μοι μαχεῖται; | Ƕas weihiþ miþ mis? | Pwy a ymladd â mi? | Cé a throidfidh liom? | ~roar | Who dares face me? |
| `allyYes` | 愿结盟好。 | Foedus faciamus. | Σπονδὰς ποιησώμεθα. | Gawairþi! | Byddwn gyfeillion. | Bímis inár gcairde. | Хорз. | Let us be allies. |
| `allyNo` | 休想！ | Numquam! | Οὐδέποτε! | Ni! | Byth! | Choíche! | Нæ! | Never! |
| `grant` | 封赏于你！ | Te honore orno! | Τιμῶ σε ἀξιώματι! | Giba þus mizdon! | Rhoddaf it urddas! | Bronnaim céim ort! | Æлдар! | I reward you with rank! |
| `thanks` | 谢主公隆恩！ | Gratias tibi ago, domine! | Χάρις σοι, δέσποτα! | Awiliuþ, frauja! | Diolch, arglwydd! | Go raibh maith agat, a thiarna! | Бузныг, æлдар! | Thank you, my lord! |

## B9. Signature lines (the 42 hand-suite rulers)

Played on the ruler-selection screen with his fanfare, after a won decisive battle, on unification and when he is met in diplomacy for the first time. Several are attested or traditional: 曹操 quotes his own 《短歌行》, 孔融 his famous saying, 康茂德 the title on his last coins (HERCVLI ROMANO AVG, 191–192), 区连 the revolt of 192 that founded Lâm Ấp, 故国川王 the relief law of 194, 首露王 the Gujibong founding legend.

| Ruler | Line | Lingua |
|---|---|---|
| 董卓 | 顺我者昌，逆我者亡！ | zh |
| 袁绍 | 四世三公，谁敢不从！ | zh |
| 曹操 | 周公吐哺，天下归心！ | zh |
| 刘备 | 汉室可兴！ | zh |
| 孙坚 | 江东子弟，随我来！ | zh |
| 袁术 | 天命在我！ | zh |
| 公孙瓒 | 白马义从，随我冲锋！ | zh |
| 刘表 | 保境安民，荆州无忧。 | zh |
| 刘焉 | 益州，天府之国。 | zh |
| 马腾 | 西凉铁骑，所向披靡！ | zh |
| 陶谦 | 徐州百姓，托付于君。 | zh |
| 孔融 | 座上客常满，樽中酒不空。 | zh |
| 韩馥 | 冀州……容我三思。 | zh |
| 张鲁 | 五斗米道，济世安民。 | zh |
| 张杨 | 并州儿郎，随我来！ | zh |
| 刘繇 | 扬州之地，岂容外人！ | zh |
| 陆康 | 庐江城在人在！ | zh |
| 孟获 | 南中男儿，谁敢来犯！ | zh |
| 孙策 | 小霸王在此！ | zh |
| 刘璋 | 益州之民，休养生息。 | zh |
| 马超 | 锦马超在此！ | zh |
| 公孙康 | 辽东自守，静观其变。 | zh |
| 公孙度 | 辽东之王，岂受汉制！ | zh |
| 士燮 | 交趾四十年太平。 | zh |
| 卑弥呼 | 鬼道をもって国を治めん。 | ja |
| 卑弥弓呼 | 女王など認めぬ！ | ja |
| 故国川王 | 백성이 나라의 근본이다! | ko |
| 首露王 | 구지봉에 하늘의 뜻이 내렸노라! | ko |
| 魁头 | Мордъё! | mn |
| 於夫罗 | Тәңірі жар болсын! | kk |
| 胡维色迦 | Šaonanošao! | xbc |
| 耶若室利 | जअदु महाराओ! | pra |
| 迦梨迦罗 | காவிரி எம் உயிர்! | ta |
| 沃洛吉斯 | شاهنشاه منم! | fa |
| 戈契赫尔 | پارس از آن ماست! | fa |
| 阿布加尔 | Māry ʔAbgar malkā! | syc |
| 加达拉特 | ንጉሠ አክሱም! | gez |
| 康茂德 | Hercules Romanus sum! | la |
| 阿根托科克斯 | Ni ddaw Rhufain i'r mynyddoedd! | cy |
| 巴洛马尔 | Hails, þiudan! | got |
| 菲利默 | Gaggam! | got |
| 区连 | Kami bebas! | ms |

## B10. Mix, priorities, subtitles, settings

### B10.1 Levels

| Bus / item | Level | Notes |
|---|---|---|
| master | 1.0 | existing |
| music | `0.42 × musicVolume` | existing `MUSIC_VOLUME`; map pieces ≈ −21 dB RMS, battle ≈ −18 dB |
| sfx | `1.0 × sfxVolume`, per-sound 0.5–0.9 | existing |
| voice | `0.9 × voiceVolume` | clips at −16 dBFS RMS |
| stings / fanfares | overlay gain 0.85, duck music to 0.35 | DOMESTIC-FX §8.5 |
| hall send (ruler court lines) | 0.18 | |

### B10.2 Ducking

A voice ducks the music to **0.5** (−6 dB; attack 60 ms, held for the clip, release 400 ms) through the existing `engine.duck(level, sec)`; with a sting or fanfare active the lower duck wins (no stacking). SFX are not ducked except the strategy-screen UI clicks (−3 dB). Result: voice ≥ 10 dB above the ducked map music, ≥ 6 dB above ducked battle music (−16 − 1 dB vs −27…−31 and −23…−26 dB).

### B10.3 Priority and rate limits

- At most **1** voice on the strategy screen, **2** in battle and duel (one per side), 1 crowd layer.
- Pri 3 always plays; it pre-empts lower ones (80 ms fade). Pri 2 plays unless a pri 3 is playing (then queued ≤ 0.6 s, else dropped). Pri 1 plays only if no voice is playing **and** its category cooldown has passed: `bat.select` 2.5 s, `bat.move` / `bat.attack` 3 s, `ack` 1.5 s, all pri 1 together ≥ 1.2 s apart.
- Under 委任, fast modes and AI turns: only pri ≥ 2, and only for the player's side or the enemy commander.
- Variant choice: decorative RNG seeded per (speaker, cue), never the same variant twice in a row; pure decoration, never `SG.Random`.
- `say()` returns the clip length so a vignette can sync its Reveal to the line (DOMESTIC-FX §3.4); vignettes never wait for a voice.

### B10.4 Settings (rows for the menu task's settings screen; `localStorage` key `sanguozhi2_audio_v4`, try / catch)

| Row | Options | Default |
|---|---|---|
| 主音量 / 音乐 / 音效 / 语音 | 0–100 % sliders | 100 / 100 / 100 / 100 |
| 音乐风格 | 君主乐章 · 地域（第三版） | 君主乐章 |
| 语音语言 | 原声（各说其语）· 一律汉语 · 关 | 原声 |
| 语音频率 | 全部 · 精简（仅重要）· 关键（仅关键）| 全部 |
| 字幕 | 原文 + 中文 · 仅中文 · 关 | 原文 + 中文 |
| 朗读（辅助） | 关 · 开（系统语音，仅本地） | 关 |

### B10.5 Subtitles

A speech bubble anchored to the speaker's portrait (`SG.UI.say` with speaker, existing), paper card with an ink outline: first line the original script in the lingua's font stack (CJK, Devanagari, Tamil, Sinhala, Georgian, Ethiopic, Arabic, Greek, Cyrillic, Latin — system fonts only), second line the Chinese meaning in smaller type; C-language cries show only the Chinese line in parentheses. Bubble lifetime = clip + 0.8 s; battle bubbles above the unit; never covers the command grid.

### B10.6 Size budget

| Item | Budget | v4.0 estimate |
|---|---|---|
| Voice packs, Opus 16 kbps, all linguas | ≤ 16 MB raw (≈ 2 h of speech) for the complete translation | 2.24 MB raw, 3.0 MB as base64 scripts (851 clips) |
| MP3 fallback packs | ≤ 30 MB | 4.2 MB raw |
| Per session download | ≤ 1.5 MB | zh 0.47 MB raw + up to 4 foreign packs ≤ 0.15 MB each |
| Decoded voice cache | ≤ 6 MB | LRU |
| Procedural cries | code ≈ 6 KB, cache ≤ 0.5 MB | |
| Music suite data | ≤ 100 KB | 6 KB hand + ≈ 60 KB frozen generated |
| Registered suite pieces | ≤ 4 suites alive | ≈ 25 KB JSON + compiled events per suite |

## B11. Performance budgets

| Item | Budget |
|---|---|
| `say()` to sound, pre-decoded | ≤ 30 ms |
| `say()` to sound, lazy decode | ≤ 120 ms, else procedural cry + subtitle |
| Main-thread work per `say()` | ≤ 1 ms (decode is async) |
| Cry render | ≤ 6 ms per cry on desktop, ≤ 15 ms iPhone class, done in an idle slice ahead of the duel / battle for the participants |
| Suite build / compile | ≤ 1 ms build, compile in ≤ 8 ms slices |
| Audio nodes | ≤ 2 voice chains (8 nodes each) + 1 crowd chain alive |

---

## 2. Integration points

| File · function | Change |
|---|---|
| `js/audio.js` | voice bus and ducking (§B10), `setSuite`, suite resolution in `musicImpl` (§A11), `fanfare()`, `stinger('clash', { suite, crit })`, `followPlan` range 132–168 bpm (§A8), `cmdSting(…, { family, headMotif })` (§A10), settings; load `music-suites.js`, `music-suites-gen.js`, `voice-data.js`, `voice-cry.js`, `voice.js` like the music files |
| `js/game.js` · ruler selection | hover / dwell → `SG.Sfx.fanfare(ruler)`, `SG.Sfx.music('map', { suite: ruler })` preview, `SG.Voice.say(ruler, 'sig')`; on start `SG.Sfx.setSuite(playerRuler)` |
| `js/strategy-screen.js` · `cmd*` handlers | at the vignette's Cue beat `SG.Voice?.say(gen, 'cmd.<id>.start')`; at Reveal: great → `cmd.<id>.great` / `great`, miss → `miss` (cue ids as DOMESTIC-FX §5); 出征 → ruler `attack`; month end → ruler `cmd.endMonth` (pri 1, once per month); 外交 answers → target ruler `fanfare` + `cmd.ally.yes / no`; 登用 / 搜索 → `cmd.hire.*`, `cmd.search.*` |
| `js/strategy-ai.js` · `succession` | return the heir so the screen can play `fanfare` + `news.succession`; `SG.Sfx.setSuite(heir)` if the player's |
| character menu (request 9 task) | first open of a general per session → `ui.generalOpen` |
| `js/battle-controller.js` | `music()` kinds unchanged (resolution moves into audio.js); `SG.MusicSuites.ensure` both rulers at battle start; enemy phase start → enemy `fanfare` (once per 3 phases); unit select / move / attack → `bat.select` / `bat.move` / `bat.attack`; tactic → `bat.tactic` / `bat.fire`, failure `bat.tacticFail`; rout → `bat.rout`; capture → `bat.captured`; city taken → `bat.cityTaken`; result → commander `victory` / `defeat` |
| `js/clash.js` · `onContact` | `SG.Sfx.stinger('clash', { speed, suite: attackerRuler, crit })`; `SG.Voice.crowd('charge', 8, { side })` + attacker general `charge`; crit beat → `clash.crit` (COMBAT-V4 §2.6 hook `SG.Voice?.say(gen, 'crit')` maps to it) |
| `js/specials.js` · `perform` | cut-in → `special` (pri 3) |
| `js/duel-game.js` | music: `SG.MusicSuites.duel(a, b)` instead of `'duel'`; efforts `SG.Voice.cry(gen, 'light' / 'heavy' / 'charge' / 'hurt' / 'knockout')`; COMBAT-V4 hooks `crit` → `duel.crit`, `parry` → `duel.parry`, `guardcrush` → `duel.guardcrush`, `finisher` → `duel.finisher` (subtitle shows the finisher's name); start → challenger `duel`, target `duel.accept`; end → `duel.win` / `duel.lose` |
| TITLES ceremony | `title.grant` (ruler) then `title.accept` (general); `promote`, `revoke`, `passedOver`, `rankUp` as TITLES lists |
| ITEMS reveal | epic → `item.epic`, legendary → `item.legendary` |
| Tournament (request 14) | walk-on → ruler `fanfare` + `tourney.enter`; champion → `tourney.win` + `victory@` his ruler |
| `index.html` | the new scripts after `audio.js` (or none: `audio.js` loads them on demand like the music files) |

## 3. Test plan and targets

### 3.1 Node

1. `tests/suites.js` (= `design-v4/audio/suites.js` check): ALL OK as §A12; plus `tools/build-suites.js --check` (frozen entries unchanged) and a determinism hash of all 499 suites.
2. `tests/voice-lines.js` (= `voice-lines.js` check): every lingua has all 20 core cues; every extended cue's fallback chain ends in a core cue or a cry; every faction maps to a lingua; signature linguas exist.
3. `tests/voice-pick.js` (new): for all 499 generals of both scenarios, `SG.Voice.profile(gen)` returns a lingua with a pack and a base voice; women get `f` voices where the lingua has one; `pitchK` within 0.86–1.12; deterministic across runs; for every duel pair of the duel-bots roster the uniqueness rule holds; `tests/sim.js` state hash unchanged with voices on (no `SG.Random` use).

### 3.2 Browser

- `tests/music.html` 「君主乐章」: choose any ruler → play each of the 11 pieces; `?auto=1` renders the 42 hand suites like `render-suites.mjs` and asserts §A12's ranges (no NaN, peak ≤ 0.98, RMS windows).
- `tests/voice.html`: (1) decode every clip of every pack: no error, decoded length within ±20 ms of the header; (2) force the MP3 path; (3) ducking: an `AnalyserNode` on the music bus drops ≥ 5 dB within 100 ms of a voice start and recovers within 600 ms after its end; (4) priorities: a scripted burst of 10 pri-1 cues in 1 s plays ≤ 1, a pri-3 pre-empts a pri-1 within 100 ms; (5) latency: pre-decoded `say()` ≤ 30 ms to first sample; (6) all subtitles render in their scripts at 844 × 390 without overflow; no `console.error`, no `pageerror`.

### 3.3 Pipeline QA (§B7.2)

Length sanity, ASR gate (A languages error ≤ 0.5; zh with whisper-small ≤ 0.35 on syllables or human review), licence gate, loudness (RMS −16 ± 1 dBFS, peak ≤ −1 dBFS) for every clip; the report lists flagged clips with their audio.

### 3.4 Playtest targets

- Suites: §A12 (70 % blind distinction, 4 of 5 fanfare recognition).
- Voices: no tester lowers 「语音频率」 in the first hour (if more than 1 in 5 do, raise the pri-1 cooldowns by 50 %); ≥ 4 of 5 testers can say which language a Roman, Parthian and Wa general speaks after 30 minutes; native speakers of A languages rate the lines "understandable" (≥ 4 / 5).

## 4. Open questions

1. **Licences.** Korean (KSS data) and the Hindi / Turkish voices tested are non-commercial; eight voices say only "see URL". If the game is ever distributed beyond personal use, Korean needs another voice (recorded, or a commercially licensed one), and the eight must be checked. MMS-TTS would cover Latin, Ossetian, Amharic, Tigrinya, Kabyle, Khmer and Mongolian natively but is CC BY-NC 4.0.
2. **Male voices for ko, vi, ms, ka** (female-only models; DSP cannot cross the gap). Options: ~25 core lines × 4 languages recorded by volunteers (≈ 20 min of studio time each), or newer permissive models when available.
3. **Translation review.** Every B and C line needs a specialist read (Gothic, Syriac, Ge'ez, Prakrit, Sanskrit, archaic Tamil, Bactrian / Tocharian / Meroitic words); A lines a native read. The extended cues (62) are only in Chinese so far.
4. **Mongolian through a Kazakh voice** could not be verified (Whisper's Mongolian is too weak); a Mongolian speaker should listen.
5. **Battle music owner**: the player's suite (proposed) or the foe's region (v3)? A setting could offer both.
6. **Fully composed suites** for the six most-played rulers in v4.1 (map + battle each, ≈ 4 × 100 s of new music).
7. **Opus in Safari**: Ogg Opus support in `decodeAudioData` is reported only for recent Safari; the MP3 fallback packs (+4.2 MB on disk, only downloaded when needed) stay until that is confirmed on iPhone hardware.
8. **Chinese two-syllable barks** are the weakest TTS output; worth re-recording the ten most frequent zh barks (遵命、出发、冲啊 …) by a human if the QA listen confirms it.
9. **Hired foreigners keep their language** (proposed) — immersion versus comprehension; the 「一律汉语」 setting covers players who prefer comprehension.
10. **Default voice frequency**: 全部 or 精简.

## 5. Unity port (after the browser build is stable)

- **Suites**: `MusicSuites.cs` ports `buildSuite()` (string operations on the same notation the C# `MusicEngine` already parses) and the generator; `tools/export-unity-music.js --suites` exports `HAND`, the frozen generated suites and **test vectors** (the built pieces' JSON for the 42 hand suites and 20 sampled generated ones) that the `Tests~/audio` harness compares hash for hash, and renders through the existing C# renderer against the Chromium WAVs (band and RMS tolerances of `Tests~/audio/README.md`).
- **Voices**: the same VPAK content as binary files in `StreamingAssets/voice/*.vpak` (no base64); Opus decoded by **Concentus** (pure C# libopus port) into `AudioClip.Create` PCM, LRU like the web; `VoicePlayer.cs` with two `AudioSource`s and an `OnAudioFilterRead` chain (the same biquads, tanh shaper and tremolo as WebAudio, ported from `voice.js`); ducking through an `AudioMixer` with a **Duck Volume** effect on the Music group fed by a send from the Voice group (attack 60 ms, release 400 ms, −6 dB); `VoiceCry.cs` ports the formant synth (deterministic, compared sample by sample with the Node renderer, max diff < 1e-4). If Concentus proves unsuitable, the pipeline exports per-clip Ogg Vorbis files instead (Unity-native import), same manifest.
- Settings and priorities as §B10, in the shared settings asset.

## 6. Sources

Languages and history:

- Bactrian replacing Greek under Kanishka (Rabatak inscription): [Bactrian language — Wikipedia](https://en.wikipedia.org/wiki/Bactrian_language); [Bactrian Documents from Ancient Afghanistan (Univ. of Tokyo)](https://gengo.l.u-tokyo.ac.jp/hkum/bactrian.html).
- Rudradaman's Sanskrit inscription at Junagadh (c. 150): [Junagadh rock inscription of Rudradaman — Wikipedia](https://en.wikipedia.org/wiki/Junagadh_rock_inscription_of_Rudradaman).
- Niya / Kroraina Prakrit (Kharoṣṭhī documents): [A Translation of the Kharoṣṭhī Documents from Chinese Turkestan (Burrow, Silk Road Seattle)](https://depts.washington.edu/silkroad/texts/niyadocts.html); [Once more on the language of the documents from Niya (CyberLeninka)](https://cyberleninka.ru/article/n/once-more-on-the-language-of-the-documents-from-niya-east-turkestan-and-its-genetic-position); [IDP: Kharoṣṭhī documents](https://idp.bl.uk/wp-content/uploads/2023/09/IDP_S4002_SB.pdf).
- Sino-Kharoṣṭhī coins of Khotan (Gandhari legends): [Numista 458976](https://en.numista.com/458976); [Numista 484188](https://en.numista.com/484188); [The Hotan horse coin (JEAN Digital)](https://jeandigital.asia/article-data-en?id=486).
- Xianbei / Tuoba / Wuhuan languages: [Para-Mongolic languages — Wikipedia](https://en.wikipedia.org/wiki/Para-Mongolic_languages); [Tuoba language — Wikipedia](https://en.wikipedia.org/wiki/Tuoba_language); [Language Log, Tuoba and Xianbei](https://languagelog.ldc.upenn.edu/nll/?p=54658).
- Ge'ez and Greek at Aksum: [Beta maṣāḥǝft, background](https://www.betamasaheft.uni-hamburg.de/about/background.html); [Ethiopian literature — Wikipedia](https://en.wikipedia.org/wiki/Ethiopian_literature); [Bausi, The Ethiopian manuscript culture (CSMC Hamburg)](https://www.csmc.uni-hamburg.de/publications/mc/files/articles/mc04-bausi.pdf).
- Ossetian as the surviving Scytho-Sarmatian language: [Ossetian language — Wikipedia](https://en.wikipedia.org/wiki/Ossetian_language); [Scythian languages — Wikipedia](https://en.wikipedia.org/wiki/Scythian_languages).
- Meroitic (readable script, largely undeciphered language): [British Museum, EA1650](https://www.britishmuseum.org/collection/object/Y_EA1650); [AramcoWorld, The Meroitic Mystery](https://archive.aramcoworld.com/issue/198304/the.meroitic.mystery-from.nubia.the.land.of.kush.a.language.lost.in.history.htm).
- Commodus as Hercules Romanus: [Virtual Catalog of Roman Coins, Commodus](https://www.roman-emperors.sites.luc.edu/commod.htm); [RIC III Commodus 254C (Numisview)](https://numisview.com/reference/coins/roman-empire/denarius/commodus/ric-3-com-254c).
- Khu Liên and the founding of Lâm Ấp (192): [Khu Liên — Wikipedia](https://en.wikipedia.org/wiki/Khu_Li%C3%AAn); [Lâm Ấp — Wikipedia](https://en.wikipedia.org/wiki/L%C3%A2m_%E1%BA%A4p); [Timeline of Champa — Wikipedia](https://en.wikipedia.org/wiki/Timeline_of_Champa).

Technology:

- Piper voices and languages: [rhasspy/piper-voices on Hugging Face](https://huggingface.co/rhasspy/piper-voices) (listing read through the Hugging Face hub tool); mirrors used: [k2-fsa/sherpa-onnx `tts-models` release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models), [`asr-models` release (Whisper)](https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models).
- MMS-TTS coverage and licence: [MMS paper, arXiv 2305.13516](https://arxiv.org/abs/2305.13516); model card `facebook/mms-tts-lat` (CC BY-NC 4.0).
- Kokoro: [hexgrad/Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) (Apache-2.0); [kokoro-onnx](https://github.com/thewh1teagle/kokoro-onnx).
- Web Speech API behaviour across browsers: [Readium, SpeechSynthesis in browsers and OSes](https://readium.org/speech/docs/WebSpeech.html); [DEV, Cross-browser speech synthesis](https://dev.to/jankapunkt/cross-browser-speech-synthesis-the-hard-way-and-the-easy-way-353); [Fluid Infusion Text To Speech API](https://docs.fluidproject.org/infusion/development/TextToSpeechAPI).
- Opus in Safari: [WebKit bug 238546](https://bugs.webkit.org/show_bug.cgi?id=238546); [WebKit bug 226922](https://bugs.webkit.org/show_bug.cgi?id=226922); [WebKit bug 245428](https://bugs.webkit.org/show_bug.cgi?id=245428); [Construct, Opus audio](https://www.construct.net/en/blogs/construct-official-blog-1/opus-audio-construct-891).
- Engine facts: `js/music.js` (notation, `Engine.duck`, `SAMPLE_BUDGET`), `js/audio.js` (`followPlan`, `STINGER_GAP`), `js/clash.js` (`onContact`), `COMBAT-V4.md` §1.3 (v4 timeline), `../sanguozhi2-unity/Tests~/audio/README.md` (C# music engine parity).
