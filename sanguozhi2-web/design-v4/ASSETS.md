# 三国志-霸王的世界 · 第四版资产清单（ASSETS）

Every visual and audio asset the nine v4 specs ask for, in one table: what it is, who owns it, how many, its triangle budget, whether it waits for the art pilot's approval, its phase, and its **fallback** (what the game shows until the asset exists, so no system is blocked by art). Geometry is built in code from declarative recipes (`integration/lp-recipe.md`) on `js/lowpoly.js` / `js/lp-props.js` in the Lanternvale style; nothing is downloaded. Budgets are triangles before ink shells (INTEGRATION §12, CITY-VIEW §9.5, TOURNAMENT §8.10).

**Phase** P1 = needed for the feature to ship; P2 = later polish (the feature works on the fallback). **Art gate** = needs the art pilot's approval of the style for that kind of asset first (A) or can start now (—, procedural data or engine work in the already-approved pilot style).

## 1. The list

| Id pattern | What | Owner | Count | Triangles | Art gate | Phase | Fallback |
|---|---|---|---|---|---|---|---|
| `city.bldg.<16>.<family>.<stage>` | the 16 city buildings × 12 families × S1–S5 (CITY-VIEW §4) | CITY-VIEW | 960 (16 × 12 × 5), skins inside a family | big ≤ 3 000, small ≤ 400 | A | P1: **华夏 all 16 × 5** + the five core buildings (城墙 · 官署 · 农田 · 市 · 民居) for the other 11 families; P2: the remaining 11 buildings × 11 families | the hua recipe in the family palette (recipe fallback chain `id.family.skin → id.family → id`) |
| `city.landmark.<29>` | historical landmarks (CITY-VIEW §6.5: 白马寺, 未央宫 …) | CITY-VIEW | 29 | ≤ 3 000 | A | P1: **the landmarks of cities that are a faction capital at game start**; P2: the rest | none (the slot stays a normal building; nothing is missing to the player) |
| `city.house.*`, `city.crowd`, animals, boats, crops | instanced fill (CITY-VIEW §8) | CITY-VIEW | ≈ 12 house variants per family, crowd pegs, 6 animal kinds | ≤ 400 / ≤ 300 | A | P1 | LPProps `house_*`, `cart`, `boat_*`, crops (existing 85 props) |
| `city.project.<13>.<state>` | ENGAGEMENT projects in scaffold / shell / done (ENGAGEMENT §8.2–§8.4) | ENGAGEMENT (model in city-kit) | 13 × 3 states; 12 family names change the done model for 7 of them | **≤ 900, wonder ≤ 2 000**; medium LOD halves | A | P1: the generic scaffold and shell (bamboo / timber / mud-brick by family material) + the hua done models + the wonder for hua (铜雀台) and roman (凯旋门); P2: family done models and the other 10 wonders | the hua done model in the family palette with the family roof part |
| `city.festival.<12>` | festival dressing sets (lantern strings, fire bowls, garlands …; ENGAGEMENT §5.3) | ENGAGEMENT / CITY-VIEW | 12 | ≤ 3 000 per set, instanced | A | P2 | lantern strings (`lantern_hanging`) for every family |
| `city.caoLu` | the 草庐 cottage of the 搜索 vignette | CITY-VIEW | 1 | ≤ 400 | A | P1 | `house_thatch` |
| `stadium.slot.<16>.facade` / `.crown` | the 万国擂台 slots (TOURNAMENT §8.3–§8.4) | TOURNAMENT | 16 facades + 14 crowns | facade 1 400–3 000, crown 1 600–3 500 (table §8.4) | A | P1 (stadium phase P3 of TOURNAMENT): all facades; crowns for the 4 special slots (主门, 裁判台, 万国之门, 奖台); P2: the other 10 crowns | TOURNAMENT P2 ships with the existing duel arena (terrain `plain`) as the stage; the block-out (`tourney/stadium-blockout.js`) is the interim stadium |
| `stadium.gate.<6>` | host gates: hua, roman, persia, indic, pylon (nile + arabia), **timber** (steppe, oasis, north, wa, han3, nanhai share one) | TOURNAMENT | 6 | ≤ 3 500 | A | P1: hua (default) + timber; P2: the other 4 | the hua 阙 gate |
| `stadium.shell`, crowd, banners, lanterns | ring shell, 2 400 / 900 instanced pegs, faction banners | TOURNAMENT | 16 shells, 1 peg, 2 banners | shell ≤ 1 200, peg 25 | — | P1 | block-out shell |
| `item.model.<602>` | every item as a procedural recipe from `items.json visual.model` (fig + palette + parts + signature part) | ITEMS | 602 | weapon high LOD ≤ 1 200, mid ≤ 400 | — (generated from data in the pilot style) | P1 | the v3 figure weapon of the same type |
| `item.part.<79>` | the parts library: 27 base parts + 52 signature parts (ITEMS §5.2) | ITEMS | 79 | ≤ 200 each | A (one review sheet for all) | P1 | the part is skipped (the item shows its base type) |
| `item.bespoke.<39>` | bespoke builders: the 24 epic + legendary items and 15 famous rares (双股剑, 古锭刀, 七支刀 …) | ITEMS | 39 | ≤ 1 200 | A | P1: the 4 legendaries + the 6 signature epics of famous generals (青龙偃月刀, 方天画戟, 丈八蛇矛, 赤兔's look …); P2: the rest | the procedural recipe of the item (still unique: palette + parts + signature part) |
| `item.icon.<602>` | procedural SVG icons, 48 / 96 / 192 px (ITEMS §5.3) | ITEMS | 602 | — | — | P1 | — |
| `item.fx.*` | 16 `procFx` presets for rare abilities (≤ 0.6 s, 1 draw call) + 25 named epic / legendary fx styles (≤ 1.5 s, ≤ 2 draw calls) | ITEMS | 41 | particles | A (style sheet) | P1: the 16 presets; P2: the 25 named styles | the preset of the effect id (`FX_BY_EFFECT`) |
| figure weapons, shields, mounts | new figure ids `ge club hammer bian ankusha vajra falx sword7 crossbow javelins`, shields `pelta hex figure8 hide`, mounts `camel elephant chariot` | ITEMS / art rollout | 17 | per figure budget (≤ 3.1 k per character incl. weapon) | A | P1: weapons and shields; P2: elephant, chariot | the nearest existing weapon / horse |
| `figure.clip.<25>` | COMBAT-V4 §13.1 duel clips × 3 styles (≈ 70 keyframe sets) | COMBAT | 25 | — | — (animation on the existing rig) | P2 (COMBAT P2) | **the fallback mapping of COMBAT §13.1** (nearest existing clip, time-scaled) — P1 ships on it |
| `figure.clip.bow`, `figure.clip.kneel` | one bow and one kneel shared by TITLES (ceremony), TOURNAMENT (salutes, with an arm override for fist-in-palm), ENGAGEMENT (归附, 联姻) | art rollout | 2 | — | — | P1 | a 0.4 s lean (pose blend) |
| duel / board / clash fx | crit flare, ink splash decal, seal stamp, gold slash ribbon (COMBAT §2.5–§2.6, §3) | COMBAT | ≈ 8 | ≤ 2 draw calls each | A | P1 | v3 hit effects + the seal stamp |
| `evt.painting.<n>` | event card vignettes (ENGAGEMENT §16.2) | ENGAGEMENT | **30** (one per event, family tints) — the full 360 (× 12 families) is optional | 2D, generated in code | A | P1: 30; P2: per-family variants where the art pilot wants them | the category colour band and the reporting general's portrait |
| `ui.icon.<≈151>` | the icon registry: UI-V4's ≈ 140 + DOMESTIC-FX's 11 addendum icons (37 command icons in all) | UI / DOMESTIC-FX | ≈ 151 | SVG primitives | A (one sheet) | P1 | the label text on the plate |
| UI textures | paper, lacquer, bronze, brush strokes generated in code (UI-V4 §3.3) | UI | ≈ 12 | — | A | P1 | v3 skin |
| `title.insignia.<5>`, ceremony hall | TITLES §3.4 insignia, §9.5 small hall scene | TITLES | 5 + 1 | hall ≤ 20 k | A | P1: insignia; P2: the grand hall (the small and medium ceremonies use the city-view palace) | the medium ceremony |
| `music.suite.<499>` | ruler suites, generated data + 42 hand motifs (AUDIO-V4 Part A) | AUDIO | 499 | — | — | P1 | v3 regional music |
| `music.sting.<34>` | command and moment stings (DOMESTIC-FX §8, §5.18–§5.28) | DOMESTIC-FX | 34 (1 284 keys) | — | — | P1 | the v3 click / chime |
| `music.tourney.<3>` | 群英会, 月下决, KO (TOURNAMENT §9.10) | TOURNAMENT | 3 | — | — | P1 | the duel piece |
| `voice.clip.*` | voice packs, offline TTS (AUDIO-V4 §B7) | AUDIO | 1 381 clips (≈ 34 min, 4.38 MB Opus) | — | — | P1: zh + the A linguas' core and start / great cues; P2: the remaining extended cues per lingua | procedural formant cries + subtitles (AUDIO §B5) |
| sfx | `ui-*`, `item-*`, `duel-*`, `fx-*`, `crowd-*` recipes (INTEGRATION §10.6) | each owner | ≈ 120 | — | — | P1 | the v3 sfx of the nearest kind |

## 2. Cut points (what is dropped first if the schedule slips)

1. **Landmarks: capitals only.** Only cities that are a faction capital at game start get their landmark (P1); the other landmarks are P2. Nothing in the rules depends on a landmark.
2. **Event paintings: 30, not 360.** One painting per event with the family colour band; per-family variants only where the art pilot asks (ENGAGEMENT Q9).
3. **Host gates: 2 of 6 in P1.** The hua 阙 gate and the shared **timber** gate (already one recipe for six families); roman, persia, indic and pylon gates are P2 and fall back to the hua gate.
4. **COMBAT clips: the fallback mapping ships.** The 25 new duel clips are P2; COMBAT P1 / P2 run on the mapping of COMBAT-V4 §13.1.
5. **Stadium crowns: the four special slots only** (主门, 裁判台, 万国之门, 奖台) in P1; the low quality tier already does this.
6. **Wonders: 2 of 12 in P1** (铜雀台, 凯旋门); the other families' wonder is their done-model in gold trim.
7. **Item bespoke builders: 10 of 39 in P1**; every other item is still unique through its procedural recipe and signature part.
8. **City buildings: hua complete, other families core five** in P1 (the remaining buildings fall back to the hua recipe in the family palette).
9. **Voice: Chinese and the A linguas' core + start / great cues** in P1; the other extended cues fall back through `fb` chains to core lines and cries.

## 3. Budget summary (worst cases, measured or estimated)

| Scene | Desktop | Phone | Source |
|---|---|---|---|
| City view, all-S5 capital with all 13 projects | 215 640 drawn / 250 000 | 128 960 / 150 000 | `node design-v4/domestic/city-stages.js` (ALL OK) |
| Stadium, duel view | ≤ 110 k drawn, ≤ 60 calls (block-out 67–69 k) | ≤ 70 k, ≤ 45 | TOURNAMENT §8.10 |
| Stadium, aerial / ceremony | ≤ 250 k, ≤ 90 calls (block-out 145 k) | ≤ 140 k, ≤ 60 | TOURNAMENT §8.10 |
| Item model, weapon | ≤ 1 200 high, ≤ 400 mid; parts ≤ 200 | same | ITEMS §5.2 |
| Battle clash (unchanged) | ≤ 350 k, ≤ 120 calls | — | COMBAT §1.5 |
