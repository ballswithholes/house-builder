# 三国志-霸王的世界 · 第四版内政演出设计（DOMESTIC-FX）

Player request 6 of v4: every domestic command (开发, 搜索, 征兵, 训练, 登用, 移动, 输送, 外交, 赏赐, 交易, 任命, 出征 …) gets a cool, helpful animation with unique fitting music and a unique, recognizable icon; cool button animations; good results get a more exciting animation and sound. This file also carries the presentation half of request 13 (the non-battle game should feel rewarding) for the strategy commands. The stage these vignettes play on, the zoomable city model of request 8, is specified in the sibling file `CITY-VIEW.md`.

Read `DESIGN-V2.md` §1 (common rules), `DESIGN-V3.md`, `CONTRACT.md`, and the sibling v4 specs `CITY-VIEW.md` (stage, buildings, camera), `ITEMS.md` (市场, gifts, hidden items), `COMBAT-V4.md` (clash timing, sound names) and `titles.json` / `TITLES.md` (官爵) first. Where this file conflicts with them, this file wins for command presentation, command icons and buttons, and the command stings; the owners of the other systems win for their rules. Platform order: browser first, then the Unity port (§14). Every visual assumes the v4 art direction from the art pilot (`js/lowpoly.js`, `js/lp-pilot.js`): Lanternvale-style stylized low-poly, cozy Ghibli-warm palette, flat-shaded props and buildings, soft-lump foliage with the warm-top / cool-teal ramp, ink outlines, soft painted light with warm lantern pools at night. Effects are brush strokes, ink splashes, paper, lanterns, gold leaf and vermillion seals, never neon.

Status: design spec only. No game code was changed. The supporting prototypes are in `design-v4/domestic/`. They run under Node against the real game files and are the source of every number quoted here:

| File | What it is |
|---|---|
| `domestic/tiers.js` | Reference implementation of the result tiers (§4) and a Monte-Carlo run: a scripted domestic player, 12 seeds × 18 rulers (8 classic, 10 world) × 60 months on the real rules. `node design-v4/domestic/tiers.js` prints the distribution table of §4.3 and checks the §13 targets. |
| `domestic/stings.js` | All 21 command stings in the `js/music.js` notation, the per-family instrument roles and interval maps (§8). `node design-v4/domestic/stings.js` compiles all 780 keys (21 stings × tiers × 12 culture families) with the real `SG.Music.validate`, prints the durations and checks the §8 targets. |
| `domestic/city-stages.js` | The building-stage thresholds and triangle budgets of `CITY-VIEW.md` (the vignettes read the same stages for the "stage-up" great tier). |

---

## 0. Key numbers at a glance

| Item | Value |
|---|---|
| Commands covered | 12 commands with 17 sub-actions from `js/strategy-screen.js` / `js/model.js` (开发 ×3, 征兵, 训练, 搜索, 登用, 移动, 输送, 外交 ×3, 出征, 赏赐, 交易 ×2, 任命), the v4 commands 市场 (ITEMS) and 授官 (TITLES), and 4 non-command moments (秋收, 城池升阶, 月初, 结束本月) |
| Vignette length (speed 1) | normal **≈ 2.0 s**, good **≈ 3.0 s**, great **≈ 4.0 s** (hard cap 4.5 s), miss ≈ 2.2 s; it always equals the length of its sting (§3.4) |
| Speed setting 「内政演出」 | 智能 (default) · 完整 · 精彩 · 快速 · 关. 快速 = 1.0–1.8 s, reveal only. 智能 = 完整 until a command has played 10 times, then 精彩 for its normal results |
| Skip | any tap / click / Space / Enter / Esc jumps to the final state in ≤ 100 ms; a second tap closes. Never blocks input for more than 0.25 s |
| Result tiers | miss · normal · good · great. Pure functions of the command's result record; **no `SG.Random` draws** (tests/sim.js stays bit-identical) |
| Measured tier rates (§4.3) | 开发 great 11–12 %, good 26–28 %; 训练 great 11.5 %; 征兵 great 13 %; 搜索 miss 53 %, great 3 %; 登用 success 54 %, great 23 % of attempts |
| Stage | in the city view when it is open (CITY-VIEW §7); otherwise a picture-in-picture diorama 「画卷」 of the same city (quality high / medium), or a 2D card (quality low, context loss) |
| Icons | 22 command and sub-action icons + 4 moment icons (26); 64 × 64 SVG on a lacquer seal plate; each has a unique silhouette (legible at 24 px), accent colour, hover micro-loop and card animation |
| Buttons | hover lift 2 px / 120 ms, press 0.94 / 60 ms + ink ripple + per-command press sound, release spring (k 420, c 26) with gold ring; token pip shows cost; 出征 ember pulse |
| Stings | 21 stings, 4 tiers, 12 culture families = 780 validated keys; overlay on the map music (duck to 0.35), transposed to the map piece's tonic. Modes: 徵 (land, transport, harvest), 商 (industry, move, trade), 宫 (town, hire, ally, reward, appoint, market, title, stage-up), 羽 (recruit, search, persuade, attack), 角 (train), 清角 / 变宫 colour (discord) |
| Leitmotif | the title's 「霸业」 cell 5,. 1_ 2 5 sounds in the great tiers of 搜索 (talent found), 登用 and 市场 (legendary); its minor form 6,. 2_ 3 6 opens 出征 |
| Budgets | vignette ≤ 1.5 ms JS / frame, ≤ 400 particles, ≤ 12 extra draw calls, ≤ 40 DOM nodes; first vignette of a kind ≤ 50 ms set-up (pre-warmed in idle time) |

---

## 1. Scope, files and ownership

| File | Status | Content |
|---|---|---|
| `js/domestic-tiers.js` | new | `SG.DomesticTiers.tier(res)` (port of `domestic/tiers.js`), stamp table, training ranks. Pure logic, Node-testable |
| `js/domestic-fx.js` | new | `SG.DomesticFx`: the vignette director (stage selection, timeline, overlay ribbon, count-ups, seals, skip, settings), the 2D card stage, the per-command scripts (§5) |
| `js/cmd-icons.js` | new | `SG.CmdIcons`: the 26 SVG icons (§6.2), their states and CSS animations (§6), the command button component (§7) |
| `js/music-stings.js` | new | the stings of `domestic/stings.js` registered with `SG.Music.add`, plus `SG.MusicStings.key(id, tier, family, answerFamily)` (§8) |
| `js/audio.js` | modified | `SG.Sfx.cmdSting(id, tier, opts) → { key, duration }`, new SFX names (§8.6) |
| `js/model.js` | modified | `Commands.lastResult` records (§3.2); no change to any formula or random draw |
| `js/strategy-screen.js` | modified | command handlers call `SG.DomesticFx.play(res)` before `report()` (§11); command grid uses `SG.CmdIcons.button` |
| `js/strategy-ai.js` | modified | `endMonth` returns the harvest records for the 秋收 moment (§9.1); no rule change |
| `js/city-view.js` and family (CITY-VIEW.md) | new | the stage; exposes `SG.CityView.director` (anchors, shots, actors) used by `domestic-fx.js` |
| `tests/domestic-tiers.js` | new | Node: tier rules, no-RNG check, distribution targets (§13) |
| `tests/domestic-fx.html` | new | every command × tier × stage (city view, diorama, card) with a speed and family switch, phone frame |
| `tests/music.html` | modified | a 「指令短乐」 tab: plays every sting in every family; offline render checks (§13) |

Parallel work rule (DESIGN-V2 §3) still holds: each task edits only its own files and lists the rest under INTEGRATION (§11).

---

## 2. The command catalogue

Read from `js/strategy-screen.js` (`refreshCityPanel` → `addCmd`, the `cmd*` handlers) and `js/model.js` (`Commands`). Token = uses one of the month's command tokens.

| id | Button | Sub-actions | Token | Handler → model | What changes (what the vignette must show) |
|---|---|---|---|---|---|
| `develop` | 开发 | 土地 `develop.land` · 产业 `develop.industry` · 町 `develop.town` | 1 | `cmdDevelop` → `Commands.develop(c, g, kind)` | −50 gold; stat + (6 + pol/6 + R(0,4)), cap 999. Forecast: 土地 → harvest +2.2 per point; 产业 → gold +0.12 / month per point; 町 → gold +0.05 / month, defence +1 per 12, population growth |
| `recruit` | 征兵 | — | 1 | `cmdRecruit` → `Commands.recruit` | −gold (n/5), −population n, general troops + n, training mixes towards 30 |
| `train` | 训练 | — | 1 | `cmdTrain` → `Commands.train` | training + (8 + war/10 + R(0,4)), cap 100 |
| `search` | 搜索 | — | 1 | `cmdSearch` → `Commands.search` (+ 登用 offer) | talent revealed (`searchFound`) · gold 60–220 · food 800–2500 · nothing; v4: hidden item (ITEMS §8.5) |
| `hire` | 登用 | (also after 搜索 and in captive handling) | 1 | `cmdHire` / `hireResult` → `Commands.hire` | target joins (loyalty 60 + virtue/4 …) or refuses |
| `move` | 移动 | — | 1 | `cmdMove` → `Commands.move` ×n, `marchRoute` | generals and their troops change city (route through own cities) |
| `transport` | 输送 | — | 1 | `cmdTransport` → `Commands.transport` | gold / food from city A to city B |
| `diplomacy` | 外交 | 同盟 `ally` · 离间 `discord` · 拉拢 `persuade` | 1 | `cmdDiplomacy` → `ally` / `discord` / `persuade` | alliance 12 months (+gift spent) · target loyalty −3…−25 · target defects (with troops, or with the city) |
| `attack` | 出征 | — | 1 | `cmdAttack` → `Conquest.prepare` → `fight` | army leaves (then march, battle; the battle has its own music and screens) |
| `reward` | 赏赐 | v4 「赐宝物」 (ITEMS §6.6) | 0 | `cmdReward` → `Commands.reward` | −100 gold, loyalty + 8…12 (gift: up to +30) |
| `trade` | 交易 | 买粮 `trade.buy` · 卖粮 `trade.sell` | 0 | `cmdTrade` → `buyFood` / `sellFood` | gold ↔ food at the month's price (8 / 12 / 16 per 100) |
| `appoint` | 任命 | — | 0 | `cmdAppoint` | city governor changes |
| `market` | 市场 (v4) | buy / sell / armoury | 0 | ITEMS §6.7 | gold → item (ITEMS owns the reveal; this file gives icon, button, sting, city-view staging) |
| `title` | 授官 (v4, name pending TITLES.md) | — | per TITLES | TITLES | a title is granted (this file gives icon, button, sting and a default ceremony) |

Moments (no button): `harvest` 秋收 (month 7, `StrategyAI.endMonth`), `stageUp` 城池升阶 (any building reaches a new stage, CITY-VIEW §4.4), `month` 月初, `endMonth` 结束本月 (dusk, §9).

Not covered here (they keep their existing presentation or belong to other tasks): the battle itself, 单挑, captive fates (登用 there reuses the `hire` vignette; 释放 / 处斩 keep their dialogs), the top-bar buttons 势力 / 大事 / 记录 / 音乐 (the main-menu task of requests 1 and 11 owns the shell; §6.4 offers icons for it).

---

## 3. The vignette system

### 3.1 Flow

The rules run first, then the vignette shows what happened, then the existing report dialog speaks:

```
button → command dialogs (unchanged) → useToken → model call (unchanged)
       → res = Commands.lastResult            (§3.2, written by the model call)
       → t = SG.DomesticTiers.tier(res)        (§4, pure)
       → await SG.DomesticFx.play(res, t)      (§3.3–§3.6; resolves at the end or on skip)
       → await this.report(who, msg, cmd, { mood: t.tier === 'great' || t.tier === 'good' ? 'win' : t.tier === 'miss' ? 'hurt' : undefined })
```

- The report dialog appears while the sting's ring-out is still sounding (the overlay keeps playing under the dialog); the report text is unchanged.
- `play` never throws: any error inside a vignette is caught, logged once (`console.warn`, never `console.error`), and the promise resolves, so a command can never be lost to a presentation bug.
- AI factions' commands are never shown (they run inside `StrategyAI.runAI`). Battles keep their own flow; 出征 shows only the departure (§5.12).

### 3.2 The result record `Commands.lastResult`

`model.js` sets `Commands.lastResult` at the end of every command (like the existing `Commands.searchFound`). It is a plain object, written after the state change, with **no extra random draws**: the `roll` fields are the value of the existing `R(0, 4)` the formula already drew, kept in a local. Fields common to all: `{ cmd, city: cityId, gen: genId | null, month: G.monthIndex }`.

| cmd | Extra fields |
|---|---|
| `develop` | `kind` ('land' / 'industry' / 'town'), `roll` (0–4), `before`, `after`, `gain`, `cost` |
| `recruit` | `n`, `cost`, `before`, `after`, `max` (`SG.maxTroops`), `cityBefore`, `cityAfter` (city troops), `trainingBefore`, `trainingAfter` |
| `train` | `roll`, `before`, `after`, `troops`, `max` |
| `search` | `outcome` ('talent' / 'item' / 'gold' / 'food' / 'none'), `value`, `found` (genId), `item` (instance id, v4) |
| `hire` | `ok`, `p` (the chance shown in the dialog), `target` (genId), `via` ('hire' / 'search' / 'captive') |
| `move` | `gens` (count), `ids`, `troops`, `hops`, `to`, `frontline` (destination borders a non-allied enemy), `ruler` |
| `transport` | `to`, `gold`, `food`, `destFoodBefore`, `destNeed` (destination troops / 40 per month) |
| `ally` | `ok`, `p`, `gift`, `target` (faction id), `targetCities`, `ourCities` |
| `discord` | `ok`, `target`, `d`, `loyaltyAfter` |
| `persuade` | `ok`, `target`, `city` (true when the city was surrendered), `p` |
| `attack` | `gens`, `troops`, `ruler`, `to`, `food` |
| `reward` | `roll`, `before`, `after`, `gift` (item instance or null), `giftFits` |
| `trade` | `side` ('buy' / 'sell'), `price`, `gold`, `food`, `goldOut` |
| `appoint` | `gen`, `pol`, `prevPol` (previous governor, or 0) |
| `market` | `item`, `rarity`, `price` (ITEMS) |
| `title` | `titleId`, `titleTier` (1–5, TITLES) |

`stBefore` / `stAfter` (the building stage before and after, CITY-VIEW §4) are filled by `domestic-fx.js` from a snapshot it takes when the command dialog closes (`SG.CityLayout.stages(ctx)` is pure and cheap: 16 comparisons).

### 3.3 The stage: where a vignette plays

```
stageFor(res):
  if settings.fx === 'off'                              → 'none'     (report only; tier SFX only, §8.6)
  if SG.CityView.isOpen(res.city)                        → 'city'     (in place, CITY-VIEW §7)
  if quality ≥ 'medium' && SG.CityView.canBuild(res.city) → 'diorama'  (picture-in-picture 画卷 of that city)
  else                                                   → 'card'     (2D, DOM only)
```

- **city**: the camera leaves the player's view, flies to the command's anchor building (CITY-VIEW §7.2), plays the script and **returns to where the player was** (0.45 s ease) after the report closes. The world-map HUD stays; the city view's own dock dims during the vignette.
- **diorama 「画卷」**: a scroll-shaped frame (two wooden rollers, paper margin with a faint ink landscape) opens from the centre over the world map (0.25 s, rollers unroll horizontally). Inside, the city scene of `CITY-VIEW.md` renders into a scissored viewport: 62 % of the screen width × 58 % height, centred at 45 % height (phone landscape: 78 % × 74 %). The map behind dims to 55 % and blurs (CSS backdrop on the HUD layer; the WebGL map simply keeps its last frame, it is not re-rendered while the diorama runs). The city scene is built in idle time when a city is selected (CITY-VIEW §9.3), so the diorama opens without a build hitch; if it is not ready within 120 ms the card stage is used for this vignette. A small 「入城」 button sits on the lower roller: it opens the full city view at the end of the vignette.
- **card**: a 640 × 360 reference-px paper card (scaled to fit, 92 % width on phones) with a painted header band in the command's accent colour, the command icon at 160 px playing its card animation (§6.3), the actors as portraits (`SG.Portrait.el`, mood by tier) and the same result ribbon. Used on quality low, when WebGL is lost, and for the commands whose subject is not in our city (离间, 拉拢 always use the card, §5.10–5.11).
- `quality` is `SG.Quality.tier` ('high' / 'medium' / 'low'), owned by the city-view module (CITY-VIEW §9.4). Settings can force it.

### 3.4 Timeline grammar

Every vignette has four beats. The times scale with the sting (the sting is the clock): `T` = the sting's notated length from `SG.Sfx.cmdSting(...).duration`; `A` = the length of its section A. With music off the defaults below are used.

| Beat | Starts at | What happens (all commands) |
|---|---|---|
| **Cue** | 0 | the command icon (§6) stamps into the top-left of the stage: scale 1.25 → 1 with a 0.18 s back-out, ink splash ring behind it; the press sound has already played. Camera move to the anchor begins (city stage) |
| **Action** | 0.15 s | the command's own animation (§5): the executing general's figure acts, workers / soldiers / carts move, the world changes. Ends at `A` |
| **Reveal** | `A` (normal: `0.55 T`) | the result ribbon slides up from the stage's lower edge (0.22 s); delta chips count up (§3.6); the forecast line fades in; tier stamp (§3.7) |
| **Settle** | `T − 0.35 s` | everything holds; the stage starts closing at `T` (0.25 s), the report dialog opens on top while the sting rings out |

Default lengths when music is off: normal 2.0 s (A 1.1 s), good 3.0 s (A 2.0 s), great 4.0 s (A 2.0 s), miss 2.2 s (A 1.1 s).

The tiers add, never replace:

| Tier | Extra on top of normal | Sting |
|---|---|---|
| **miss** | Action ends in a soft "nothing" beat (wind, a shrug, a closed door), desaturated grade −20 % for 0.6 s, the ribbon shows 「未果」 in grey ink with one line of encouragement (§5) | A + M (falling, gentle) |
| **normal** | — | A (or A + N for chance commands) |
| **good** | a 1-bar **flourish** after Reveal: warm light sweep across the changed objects, one particle burst (≤ 120), the gold round seal 「佳」 on the ribbon, a second camera move (slow 12° orbit) | A + G |
| **great** | a 2-bar **spectacle**: the city-view object visibly upgrades or the actors celebrate (§5), camera crane-and-push, golden-hour light pulse (Mood +0.25 warmth for 1.2 s), particles ≤ 400, a full-height brush stroke banner with the result in large 楷书, the square vermillion seal with the command's own two characters (§3.7), screen-edge ink vignette | A + X (fuller orchestra, gong / bells ring out) |

### 3.5 Speed setting and skip

Settings key `sanguozhi2_fx_v4` (one JSON object, read and written in try/catch): `{ domestic: 'smart' | 'full' | 'highlights' | 'fast' | 'off', cityView: true, quality: 'auto' | 'high' | 'medium' | 'low', plays: { [cmd]: count } }`. The settings screen (menu task) shows it as 「内政演出」 with the five options, and the city-panel dock shows a small speed chip (⏵ / ⏩ / ✕) that cycles 完整 → 快速 → 关 for quick changes.

| Mode | normal | good | great | miss |
|---|---|---|---|---|
| 完整 full | full | full | full | full |
| 精彩 highlights | fast | full | full | fast |
| 快速 fast | fast | fast | fast + seal | fast |
| 智能 smart (default) | full for the first 10 plays of this command, then fast | full | full | full for 10, then fast |
| 关 off | none | none | none | none |

**Fast** form: no Cue camera move (the stage opens already on the anchor shot), Action shortened to its last 0.4 s, Reveal count-up 0.35 s; lengths normal 1.0 s, good 1.3 s, great 1.8 s, miss 1.0 s. The sting plays only its tag section (G or X; the A section for normal and miss), so it is never sped up or cut mid-note.

**Skip**: pointer down anywhere on the stage, Space, Enter or Esc → `skip()`: all tweens jump to their end values, counters show final numbers, the upgraded building snaps to its new stage, particles fade over 0.15 s, the stage closes 0.4 s later; a second input closes at once. The sting is not cut (it is an overlay and simply continues under the report). Input is never blocked for more than 0.25 s (the Cue).

### 3.6 Result ribbon, counters and the forecast line ("helpful")

The ribbon is a paper strip with torn ends (DOM, `.sg-fx-ribbon`), 64 % of the stage width, holding 1–3 **delta chips** and a **forecast line**:

- Chip: `[icon 20 px] 名称  旧值 → 新值  +Δ` in 22 px figures (tabular), Δ green `#73d98c` for gains, red `#ff7361` for costs; costs (gold spent) are shown as a smaller second chip.
- Count-up: from old to new over `0.7 s` (fast 0.35 s), easing `1 − (1 − u)^3`, integer steps; a soft tick SFX `fx-tick` every displayed step capped at 18 ticks (pitch rising 1.5 semitones per tick), a heavier `fx-land` on the final value. Numbers ≥ 10 000 use 万 formatting (`2.3万`) only above 100 000.
- **Stage bar** (city-linked commands): the building's progress to its next stage (`471 / 600 → 第三阶段「沟渠灌溉」`) as a thin brush bar, filling with the count-up; on a stage-up it fills, flashes and the next stage's name replaces it (great tier).
- **Forecast line** (one line, muted ink, the "helpful" part; formulas from `data.js` `Balance`):

| Command | Forecast text |
|---|---|
| 开发·土地 | 秋收预计 +⌊Δ × 2.2⌋ 粮（7 月）; in months 8–12: 「明年秋收 …」 |
| 开发·产业 | 每月金 +⌊Δ × 0.12 × 10⌋ / 10 |
| 开发·町 | 每月金 +⌊Δ × 0.05 × 10⌋ / 10　城防 +⌊(after/12) − (before/12)⌋　人口增长 +… / 月 |
| 征兵 | 每月耗粮 +⌈n / 40⌉　训练 旧 → 新 (the dilution, in amber) |
| 训练 | the rank 新募 / 操练 / 精练 / 精锐 / 百战 (thresholds 40 / 60 / 80 / 100) and how many points to the next rank |
| 搜索 | talent: best attribute and 「可登用，成功率约 N %」; item: rarity and slot; gold / food: 「可支用 N 月」 |
| 登用 | the new general's 武 / 智 / 政 and the special's name |
| 移动 | 「X 城兵力 旧 → 新」 at the destination |
| 输送 | destination 「粮可支 N 月」 before → after |
| 同盟 | 「同盟至 Y 年 M 月」 |
| 离间 | target loyalty bar, and 「忠诚低于 40，可能出奔」 when < 40 |
| 拉拢 | 「X 率兵 N 来投」 / 「X 城归附」 |
| 出征 | army troops, food days (same text as the dialog) |
| 赏赐 | loyalty bar; at 100: 「誓死效忠」 |
| 交易 | 「本月粮价 N / 100」 and 「秋后粮价最低（7–9 月）」 hint when buying outside those months |
| 任命 | 「太守 政治 N：城中开发由太守主持时 …」 only if TITLES / ITEMS give governor effects; else the governor's 政治 |

### 3.7 Seals and banners

- **good**: a round gold seal 「佳」 (44 px, gold `#f3c969` on dark lacquer) presses onto the ribbon's right end: scale 1.4 → 1, 0.14 s, with a tiny paper shake (2 px).
- **great**: a square vermillion seal (72 px, `#c8382c`, 白文 white-on-red, slightly irregular edges, 3 % noise) slams onto the stage's upper right: scale 1.8 → 1 in 0.16 s (back-out), ink spatter of 14 droplets, 3 px screen shake for 0.12 s, taiko `fx-seal` SFX. Its text is the command's own two characters:

| Command | Seal | | Command | Seal |
|---|---|---|---|---|
| 开发·土地 | 丰穰 | | 移动 | 星驰 |
| 开发·产业 | 百炼 | | 输送 | 辇输 |
| 开发·町 | 殷阜 | | 同盟 | 盟成 |
| 征兵 | 云集 | | 离间 | 离心 |
| 训练 | 精锐 | | 拉拢 | 倒戈 |
| 搜索（人才）| 得贤 | | 出征 | 出师 |
| 搜索（宝物）| 宝现 | | 赏赐 | 恩泽 |
| 登用 | 归心 | | 交易 | 居奇 |
| 任命 | 得人 | | 市场 | 神兵 |
| 授官 | 册封 | | 秋收 | 大有 |
| 城池升阶 | 升格 | | | |

- **great banner**: a vertical brush-stroke banner (ink, 14 % screen width) unrolls from the top in 0.3 s carrying the result in 楷书 (e.g. 「农田　沟渠灌溉」, 「诸葛亮　来投」); it rolls up at Settle.

### 3.8 Determinism

- Rules: the vignette reads `lastResult` and the city snapshot only; it never calls `SG.Random`. `tests/domestic-tiers.js` replays `tests/sim.js` seeds with the FX module loaded and compares the final state hash (§13).
- Decoration (which tiles sprout, crowd paths, particle jitter) uses `SG.SeededRandom(hash(cmd, G.monthIndex, city.id, tokenIndex))`, so the same command in the same month looks the same after a reload.

### 3.9 Accessibility and comfort

- `prefers-reduced-motion: reduce` → the default mode becomes 快速, no screen shake, no camera orbit, flashes off; seals still appear (no scale animation).
- No more than 3 flashes per second, no full-screen white flash; the great light pulse is a warmth change, not a strobe.
- Tier is never told by colour alone: the seals have text, the miss ribbon says 「未果」.
- Camera: one continuous move per beat, max 60°/s yaw, 45°/s pitch, no cuts in the city stage except the optional search reveal (§5.6); the diorama and card never move the main camera.

---

## 4. Result tiers

### 4.1 Rules (universal)

1. A tier is a pure function `tier(res)` of the result record (§3.2). Same record → same tier.
2. **great** is reserved for results the player will remember: a visible city upgrade (stage-up), a milestone (训练 百战 with a near-full army, a city crossing 10 000 troops, loyalty 100 on a top roll), a talent or treasure, beating long odds (登用 at < 20 %), or a large deal. Target 5–16 % of uses for the frequent commands.
3. **good** marks a better-than-usual roll or a smaller milestone (top roll R = 4, crossing a hundred, a rank-up, favourable price). Target 18–40 %.
4. **miss** is only for real failures of chance commands (搜索 found nothing, 登用 / 同盟 refused, 离间 seen through, 拉拢 refused) and for a lean harvest. Its presentation is gentle (§3.4) and always says what to try next.
5. Deterministic commands with player-chosen amounts (征兵, 移动, 输送, 交易, 出征) tier by magnitude and milestones, so a bigger decision gets a bigger show.

### 4.2 Per command

Constants are those of `domestic/tiers.js`; "crosses x" means `before < x ≤ after`.

| Command | miss | good | great |
|---|---|---|---|
| 开发 (each kind) | — | `roll = 4` or the stat crosses a multiple of 100 | the linked building's stage rises (农田 / 工坊 / 民居 thresholds 300 / 450 / 600 / 800, CITY-VIEW §4) or the stat reaches 999 |
| 征兵 | — | `n ≥ 2000` or the general is now full | city troops cross 10 000 / 20 000 / … / 100 000, or full with `n ≥ 3000` |
| 训练 | — | `roll = 4`, reaching 100, or crossing 80 with `roll ≥ 3` | reaching 100 (百战) with troops ≥ 90 % of the general's maximum |
| 搜索 | nothing found | gold ≥ 160 or food ≥ 1900 | a talent revealed (人中龙凤 variant when the best attribute ≥ 90), or a hidden item (ITEMS §8.5) |
| 登用 | refused | best attribute ≥ 75, or chance < 45 % | best attribute ≥ 90, or chance < 20 % |
| 移动 | — | ≥ 3 generals, ≥ 10 000 troops or ≥ 3 cities of route | ≥ 5 generals, or ≥ 20 000 troops into a frontline city |
| 输送 | — | gold ≥ 500 or food ≥ 5000 | gold ≥ 1500, food ≥ 15 000, or a rescue (destination had < 2 months of food and now has ≥ 6) |
| 同盟 | refused | chance < 45 %, or the partner has at least our city count | chance < 25 %, or a great power (≥ 2 × our cities and ≥ 4 cities) |
| 离间 | seen through | loyalty drop ≥ 10 | drop ≥ 18, or the target falls below 40 (may defect) |
| 拉拢 | refused | defects (always at least good) | the city is surrendered (献城), or the defector's best attribute ≥ 85 |
| 出征 | — | ≥ 3 generals with ≥ 12 000 troops, or ≥ 20 000 troops | ≥ 40 000 troops, or the ruler leads 5 generals with ≥ 20 000 |
| 赏赐 | — | `roll = 4`, reaching 100 from ≤ 90, or an item gift | a fitting item gift (ITEMS §6.6) or reaching 100 on a top roll |
| 交易 | — | favourable price (buy at 8, sell at 16) or ≥ 600 gold traded | favourable price and ≥ 1000 gold traded |
| 任命 | — | governor 政治 ≥ 75, or + 15 over the previous governor | 政治 ≥ 90 |
| 市场 | — | rare | epic or legendary |
| 授官 | — | title tier 3 | title tier 4–5 |
| 秋收 | roll < 0.90 (lean year) | roll ≥ 1.08 or land ≥ 700 | roll ≥ 1.10 with land ≥ 600 (丰年) |

The harvest roll is `harvest / (land × FoodPerLand)`, recovered from the record `endMonth` returns (§9.1); no new draw.

### 4.3 Measured distribution

`node design-v4/domestic/tiers.js 12 60` (12 seeds × 18 rulers × 60 months, a scripted domestic player that develops the weakest stat, trains below 100, recruits below 70 % of the maximum, searches, hires, rewards, trades, and tries diplomacy). Percent of uses:

| Command | n | miss | normal | good | great |
|---|---|---|---|---|---|
| 开发·土地 | 4653 | — | 61.7 | 27.4 | 10.9 |
| 开发·产业 | 5385 | — | 60.8 | 27.6 | 11.7 |
| 开发·町 | 5647 | — | 62.5 | 26.0 | 11.5 |
| 征兵 | 1373 | — | 60.7 | 26.8 | 12.5 |
| 训练 | 4589 | — | 51.1 | 37.3 | 11.5 |
| 搜索 | 10 437 | 53.3 | 27.4 | 16.1 | 3.2 |
| 登用 | 614 | 45.6 | 9.8 | 22.0 | 22.6 |
| 移动 | 184 | — | 81.5 | 18.5 | 0.0 |
| 输送 | 158 | — | 58.9 | 30.4 | 10.8 |
| 同盟 | 640 | 38.8 | 5.3 | 33.6 | 22.3 |
| 离间 | 930 | 42.2 | 9.7 | 23.5 | 24.6 |
| 拉拢 | 844 | 85.4 | 0.0 | 7.7 | 6.9 |
| 出征 (departure) | 3414 | — | 56.3 | 36.5 | 7.2 |
| 赏赐 | 1364 | — | 80.7 | 18.7 | 0.6 |
| 交易 | 939 | — | 41.4 | 41.5 | 17.0 |
| 任命 | 34 | — | 32.4 | 47.1 | 20.6 |
| 秋收 (per city) | 1333 | 10.7 | 44.5 | 36.2 | 8.6 |

Reading: the three 开发 kinds and 训练, the commands a player repeats most, sit in the target band (great 11–12 %, good 26–37 %). The chance commands have high success tiers because success itself is rare (同盟 succeeds 61 % of the time in this run but mostly against long odds; 拉拢 succeeds 15 %). 赏赐 is mostly normal by design (it is free and frequent; ITEMS gifts lift it). 移动 shows no great here because the scripted player moves at most 3 generals at a time; a real 5-general redeployment is great.

---

## 5. The commands

Each entry: what the stage shows, per tier; camera; actors; the icon (designed in §6); the sting (notation in `domestic/stings.js`, §8); the press sound; voice cues (request 12, `SG.Voice?.say(gen, cue)`); the card-stage fallback. Times are speed-1 defaults; they scale with the sting (§3.4). Anchors and shots are defined in `CITY-VIEW.md` §7.2: `est` (whole plot), `bldg(id)` (3/4 view of a building), `close(id)`, `gate.in` / `gate.out`, `track(actor)`.

### 5.1 开发·土地 `develop.land`

- **Anchor**: `farms` (fields outside the walls). Shot `bldg('farms')`: pitch 38°, distance 3.2 × field radius + 10 m, yaw from the gate side.
- **Action (0.15–1.1 s)**: the executing general's figure stands at the field edge and points (pose `command`); a pair of oxen pulling a plough (二牛抬杠, CITY-VIEW §5.1) walk one furrow line; behind them a **growth wave** passes over `k = clamp(round(Δ / 3), 3, 8)` new field tiles: soil darkens (tilled, 0.12 s per tile, staggered 60 ms), then seedlings pop up (scale 0 → 1.15 → 1, 0.2 s), soft-lump crops with the warm-top ramp. In autumn months (7–9) the new tiles come up golden; in winter (11–2, north of 32° N) furrows show frost and the seedlings are winter wheat.
- **Reveal**: chip 土地 old → new +Δ, cost chip −50 金, stage bar to the next farm stage, forecast 「秋收预计 +⌊2.2Δ⌋ 粮」.
- **good**: a warm light sweep runs across the new tiles; three white egrets rise from the field and circle once; 「佳」.
- **great (stage-up)**: the farm upgrades in place to its next stage (CITY-VIEW §4.3: e.g. S2 → S3 adds the irrigation channel and the 翻车 square-pallet water lift; water starts flowing along the channel from the river side, 0.8 s); camera cranes up 18° and pushes 15 %; banner 「农田　沟渠灌溉」; seal 丰穰; chaff and petals drift in the light. If the stat reached 999 instead: the whole field ring ripples gold and the banner reads 「沃野千里」.
- **Icon** `develop.land`: a 耒 (two-pronged digging stick) planted in three curved furrows, a jade seedling with two leaves rising beside it. Accent jade `#4f9a76`. Hover: the seedling sways and a third leaf unfolds (0.6 s).
- **Sting** `sting:develop.land`: 徵调式, ♩ = 116, 2/4. Dizi plays the rising "growth" cell 5, 6, 1 2 3 – 2 5 over zheng arpeggios and a muyu walking pulse; good adds sheng and bianzhong; great adds choirOo, dagu and luo with a zheng glissando into high 5. Lengths 2.07 / 3.10 / 4.14 s.
- **Press sound** `cmd-press-develop`: a dry wood knock (mallet on hoe handle) with a soil thud below.
- **Voice**: `cmd.develop.start` (the general: 「开垦良田！」), `cmd.develop.great`.
- **Card**: a field band across the card; the icon's seedling grows into a row of five; counters as above.

### 5.2 开发·产业 `develop.industry`

- **Anchor**: `workshop` (冶铁工坊; Wa: bronze and bead workshop; Rome: fabrica; Kush: iron furnaces, CITY-VIEW §5). Shot `close('workshop')`, pitch 30°, looking into the open front of the forge.
- **Action**: the furnace door glows (emission 0 → 1 over 0.3 s, a lantern-coloured point light 2.5 m range); a smith strikes the anvil three times in the sting's rhythm (beats of section A, synced to the bangzi hits), each strike throwing 12 sparks (additive orange dots, gravity, 0.4 s life); `k = clamp(round(Δ / 4), 2, 6)` iron ingots (or bronze, by family) slide onto the stack outside; smoke from the chimney thickens by one step.
- **Reveal**: 产业 old → new, −50 金, stage bar, forecast 「每月金 +x.x」.
- **good**: the water-powered bellows (水排, if the workshop is at S3+) or a second smith joins; spark burst 120; 「佳」.
- **great (stage-up)**: the workshop grows (S3: the 水排 waterwheel and bellows shed rise and the wheel starts turning; S4: a tall furnace and a casting yard with moulds (铁官); S5: several furnaces and tool racks, and the smoke column glows at night); banner 「工坊　水排鼓铸」 (stage names CITY-VIEW §4.3); seal 百炼.
- **Icon**: an anvil in profile with a hammer raised over it and a three-ray spark. Accent ember `#e07a3a`. Hover: the hammer taps twice and the spark blinks.
- **Sting** `sting:develop.industry`: 商调式, ♩ = 126. Pipa staccato 2 2 5 2' – 1' 6 2 over a bangzi "anvil" pattern on the beats and a low string ostinato; good adds horns; great adds suona, dagu and a tamtam stroke. 1.90 / 2.86 / 3.81 s.
- **Press sound** `cmd-press-industry`: a short metallic tink (anvil) with a small spark crackle.
- **Voice**: `cmd.industry.start` (「起炉鼓铸！」), `cmd.industry.great`.
- **Card**: the anvil icon at 160 px, three hammer strikes with sparks, ingots stacking at the card's bottom edge.

### 5.3 开发·町 `develop.town`

- **Anchor**: `houses` (the residential ward 闾里), shot `bldg('houses')` from above the main street, pitch 44°.
- **Action**: `k = clamp(round(Δ / 4), 2, 6)` new houses rise on reserved lots (CITY-VIEW §6.3): each one builds from the ground in three pops (rammed-earth base, timber frame, roof drop with a puff of dust; 0.25 s each, staggered 90 ms); lanterns at their doors light (night only, or a warm window glow by day); the street crowd gains `ceil(k / 2)` walkers; a vendor's cart rolls along the main street.
- **Reveal**: 町 old → new, −50 金, stage bar, forecast 「每月金 +x.x　城防 +n　人口增长 +n / 月」.
- **good**: a market-day bustle: banners on the street, a burst of paper confetti (red and gold rectangles, 80); 「佳」.
- **great (stage-up)**: the ward densifies: houses gain a second storey in a wave down the street (each 0.12 s), the 町 stage's new landmark appears (S3 a 市楼 drum tower with a flag, S4 a two-storey 坊 gate arch, S5 a lantern street — CITY-VIEW §4.3); walls gain their next stage as well when `cityDefense` crosses a wall threshold (two upgrades in one vignette are chained, 0.5 s apart); banner 「闾里　坊市栉比」; seal 殷阜.
- **Icon**: a two-storey house with a tiled hip roof and a red lantern hanging under the eave, a curl of smoke from the roof. Accent amber `#d9933a`. Hover: the lantern glows and swings; the smoke curls.
- **Sting** `sting:develop.town`: 宫调式, ♩ = 120. Yangqin 1 3 5 3 – 6 5 3 1 chatter with ruan and muyu, dizi answer; good adds sheng and xiaoluo; great adds suona and the full small-percussion "street festival". 2.00 / 3.00 / 4.00 s.
- **Press sound** `cmd-press-town`: two quick mallet taps on a beam (carpentry) and a muyu click.
- **Voice**: `cmd.town.start` (「修缮闾里！」), `cmd.town.great`.
- **Card**: a street of five house silhouettes on the card; new ones pop in; lanterns blink on.

### 5.4 征兵 `recruit`

- **Anchor**: `gate.in` then `barracks` (the camera tracks the column from the gate to the barracks yard).
- **Action**: a drum on the barracks' drum tower is struck (synced to the sting's dagu); a column of recruits in plain clothes, `rows = clamp(ceil(n / 500), 2, 8)` rows of 4 instanced soldier figures (CITY-VIEW §8), walks in through the gate carrying bundles; as they pass the barracks gate they turn into uniformed soldiers in the faction colour (a cloth-colour wipe per figure, 0.15 s); a tally board by the gate counts up.
- **Reveal**: chips 兵 old → new +n (the general), −gold, population −n; forecast 「每月耗粮 +⌈n/40⌉」 and the training dilution in amber (训练 72 → 61).
- **good**: the column is followed by a banner bearer with the general's family name banner (牙旗); recruits cheer (arms up); 「佳」.
- **great**: the city crosses a 10 000 mark: the barracks yard fills, overflowing onto the drill ground, every soldier raises spears at once (one shared pose blend), a ring of banners unfurls around the yard; if the barracks' stage rose, it upgrades (CITY-VIEW §5); banner 「兵满万人」 (or 「兵逾两万」 …); seal 云集.
- **Icon**: a war drum (建鼓) on its stand, seen at three quarters, with two crossed drumsticks and a small triangular pennant 募 on the stand's post. Accent cinnabar `#c8382c`. Hover: the sticks strike once and two pulse rings leave the drumhead.
- **Sting** `sting:recruit`: 羽调式, ♩ = 132. Horns call on 6,+3, the suona answers 6, 3 6, timpani pulse; good adds bo and luo; great adds choir and a tanggu roll. 1.82 / 2.73 / 3.64 s.
- **Press sound** `cmd-press-recruit`: one deep drum tap with a short skin buzz.
- **Voice**: `cmd.recruit.start` (「招募义勇！」), `cmd.recruit.great` (「万众归心！」).
- **Card**: the drum icon beats; a row of small soldier silhouettes marches in from the left and fills the card.

### 5.5 训练 `train`

- **Anchor**: `drill` (校场), shot `bldg('drill')` pitch 40°, slow 8° orbit during Action.
- **Action**: the general's unit stands in a formation block (instanced, `rows = clamp(ceil(troops / 1000), 2, 6)`, 6 per row); they perform two drill moves synced to the erhu staccato (spear thrust, step forward); the general's figure on the reviewing platform raises his weapon (the general's own weapon model from figure.js / ITEMS); a training bar beside the platform fills.
- **Reveal**: 训练 old → new, rank label (新募 / 操练 / 精练 / 精锐 / 百战) with a stamp when the rank changes.
- **good**: an archer line looses one volley into the straw targets (arrow trails as ink strokes; the targets wobble); 「佳」.
- **great (百战)**: the formation shifts into the faction's battle-banner shape (the blocks rearrange in 0.8 s along precomputed slots so that, seen from the crane-up camera, they form the character of the general's family name or, if the name glyph is too complex, the faction's banner emblem), everyone shouts once (choir "喝" in the sting), a dust ring rises; the drill ground upgrades if its stage rose; banner 「百战之师」; seal 精锐.
- **Icon**: a round straw target (侯) with concentric rings, an arrow in the bull and a 戟 (halberd) slanting behind it. Accent bronze `#a7743a`. Hover: the arrow flies in from the left and thunks; the target wobbles.
- **Sting** `sting:train`: 角调式, ♩ = 132. Erhu spiccato 3 3 5 – 3 6 5 like drill commands, tanggu sixteenths, bo on the offbeats, ban on the downbeats; good adds dizi and horns; great adds dagu, a tanggu roll and the choir shout. 1.82 / 2.73 / 3.64 s.
- **Press sound** `cmd-press-train`: a sharp bo tick plus a stamping foot.
- **Voice**: `cmd.train.start` (「操练！」), `cmd.train.great` (「百战之师！」).
- **Card**: target and arrow icon; the training bar fills across the card; the rank name stamps.

### 5.6 搜索 `search`

The one vignette with branching outcomes. The general is shown as the searcher; the outcome is known before the vignette starts (the rules have run), but the Action builds suspense and the Reveal comes on the outcome section of the sting.

- **Anchor**: `tavern` (酒肆) for the start; outcome anchors vary. Shot `bldg('tavern')` at street level (pitch 24°).
- **Action (A, 1 bar ≈ 1.1 s)**: the general in a travelling hat (斗笠) leaves the tavern carrying a paper lantern (by night) or a walking staff (by day), footprints appear behind him on the street, the camera tracks him to the gate.
- **miss (A + M)**: at the gate, a gust of wind blows leaves across the road; the general looks left and right and shrugs (pose `shrug`); grey ribbon 「一无所获」 and 「人才往往藏于名城：多寻访几次，或换智力高的武将」.
- **gold (normal / good)**: cut-less camera pan to a willow by the wall: the general digs and lifts a clay jar; coins glint (12 gold sparks; good: the jar overflows and coins roll, 40 sparks, 「佳」). Chip 金 +v.
- **food (normal / good)**: a forgotten storehouse door at the granary swings open; sacks tumble out (6 / good 14). Chip 粮 +v.
- **talent (great)**: the camera follows the footprints out of the gate, over the field ring, to a thatched cottage (草庐) in a bamboo grove at the edge of the plot (spawned for the vignette, CITY-VIEW §7.1); the door slides open, the hidden general is seated inside (his own figure and look) and rises; the portrait card (`SG.Portrait.el`, mood 'win') flips in beside him with name, 武 / 智 / 政 and his special's name; if his best attribute ≥ 90 the 人中龙凤 variant adds the golden light shaft through the bamboo and a crane taking off. The sting plays the 「霸业」 leitmotif (horn, bianzhong, dizi). Banner 「得遇贤才　X」; seal 得贤. Then the existing 登用 offer dialog appears (`genCard`), and if accepted the `hire` vignette (§5.7) plays starting from the cottage.
- **item (great, v4)**: the general finds a buried case at the item's clue location (ITEMS §8.5 text); the item's model rises on a small light pillar, ITEMS' reveal (§5.5 there) takes over for 1.2 s; seal 宝现.
- **Icon**: a bamboo hat (斗笠) above a small trail of three footprints winding into a mountain-line, a star-glint on the horizon. Accent indigo `#3b4f7a`. Hover: the footprints appear one by one and the glint twinkles.
- **Sting** `sting:search`: 羽调式, ♩ = 108. Xiao 6, 1 3 over qin and a pianissimo string chord (the seeking); outcome bars: N yangqin sparkle, G yangqin and bianzhong with sheng, X the 「霸业」 cell 5,. 1_ 2 5 on horn turning the mode to 宫 (the discovery), M xiao falling 2 1 6, to silence. 2.22 / 3.33 / 4.44 / miss 2.22 s.
- **Press sound** `cmd-press-search`: a lantern handle creak and a small wind chime.
- **Voice**: `cmd.search.start` (「出城寻访。」), `cmd.search.none`, `cmd.search.talent` (the found general: 「何人扰我清梦？」 style lines from the voices task).
- **Card**: the hat icon walks along the card's lower edge leaving footprints; the outcome pops up as a jar, sacks, a portrait in a round window, or falling leaves.

### 5.7 登用 `hire`

- **Anchor**: `palace` guest hall (宾馆, CITY-VIEW §5) for 登用 from the city panel; the 草庐 when continuing from 搜索; the palace courtyard for captives.
- **Action (A ≈ 1.1 s)**: the recruiter bows with clasped hands (拱手, pose `bow`) and offers a scroll (聘书); the target faces him.
- **miss**: the target turns away, the scroll is handed back, a door slides shut; ribbon 「X 婉拒」 and 「德高者更易得人：提高德或换智力高的使者」 (the real factors of `hireChance`).
- **normal / good**: the target returns the bow, the two walk side by side towards the palace (good: the city's officials line the steps and the faction banner dips in salute; 「佳」).
- **great**: as good, then the new general's portrait flips gold, his special-attack name appears in brush letters (from `SG.Specials.of(gen)`), the hall's lanterns light in sequence; the sting plays the 「霸业」 leitmotif on horns and choir; banner 「X　归心」; seal 归心.
- **Icon**: two hands meeting in 拱手 salute, wrapped by a ribbon tying a rolled invitation scroll. Accent gold `#c99a2e`. Hover: the hands bow (rotate 12° and back) and the ribbon flutters.
- **Sting** `sting:hire`: 宫调式, ♩ = 112. Erhu 3 5 greeting over qin; N erhu 6 5 1' with sheng; G an erhu and qin duet with bianzhong; X 「霸业」 on horns with choir; M erhu sighing 3 2 6,. 2.14 / 3.21 / 4.29 / 2.14 s.
- **Press sound** `cmd-press-hire`: a single qin harmonic.
- **Voice**: `cmd.hire.ask` (recruiter), `cmd.hire.yes` / `cmd.hire.no` (target).
- **Card**: two portraits face each other; on success the target's frame recolours from grey to the faction colour.

### 5.8 移动 `move`

- **Anchor**: `gate.out`; then hand-off to the world map's existing `marchRoute` animation (which is helpful: it shows the route city by city).
- **Action**: the moving generals ride out of the gate on horseback (or on foot for families without horses, e.g. Wa), each followed by a short column (`rows = clamp(ceil(troops / 3000), 1, 4)` per general), banners in the faction colour; the gate closes behind them.
- **Reveal**: chip 「→ 目的城　兵 old → new」.
- **good**: the column is longer (all generals ride abreast) and the destination's name appears on a road sign at the gate; 「佳」.
- **great** (massing at the front): war drums on the wall, the column stretches to the horizon, a hawk flies ahead along the road; banner 「大军星驰　→ 城名」; seal 星驰.
- After the vignette the world-map march (`marchRoute`, ≤ 2.6 s) plays as today; in 快速 mode the march is 0.9 s total.
- **Icon**: a forward-leaning 牙旗 banner on a pole over a road that bends from one gate silhouette to another, two chevron footprints on the road. Accent the faction colour (rendered with `currentColor`). Hover: the banner flutters and slides 4 px forward.
- **Sting** `sting:move`: 商调式, ♩ = 116. Ruan walking line 2 3 5 6 – 5 3 2 over pizz and muyu footsteps; good adds a dizi tag; great adds horns and dagu. 2.07 / 3.10 / 4.14 s.
- **Press sound** `cmd-press-move`: two hoof steps.
- **Voice**: `cmd.move.start` (「移驻 X！」).
- **Card**: the banner icon travels left to right across a strip map of the route's city names.

### 5.9 输送 `transport`

- **Anchor**: `granary` (food) or `treasury` (gold), then `gate.out`, then a world-map cart march (new, `SG.Game.map.march` with a cart marker instead of the army marker, same timing rule as `marchRoute`).
- **Action**: ox carts (one per 2 000 food or 500 gold, 1–6) are loaded with sacks / lacquered chests by two porters each and roll out; counters at the source go down as they leave.
- **Reveal**: chips 「金 −g　粮 −f」 at the source; the destination line 「X 城 粮可支 a → b 月」.
- **good**: an escort of riders flanks the carts; 「佳」.
- **great** (rescue or huge): the destination city appears in a small inset (its city view, if cached, or its map model) with people cheering at the gate; banner 「转输 X　解燃眉之急」 (rescue) or 「辎重如山」; seal 辇输.
- **Icon**: a two-wheeled ox cart with grain sacks and a string of coins tied to the side, seen in profile. Accent ochre `#b8862e`. Hover: the wheel turns one revolution and the coins swing.
- **Sting** `sting:transport`: 徵调式, ♩ = 168 in 3 (a 6/8 cart lilt), muyu and hoof "clip-clop" kit, ruan and sheng; good adds dizi; great adds horns, dagu and luo. 2.14 / 3.21 / 4.29 s.
- **Press sound** `cmd-press-transport`: a wooden axle creak.
- **Voice**: `cmd.transport.start` (「押运粮草。」).
- **Card**: carts roll across the card from the source name to the destination name.

### 5.10 外交·同盟 `ally`

- **Anchor**: `palace` (guest hall) → `gate.out` (envoy leaves) → `gate.in` (foreign envoy arrives).
- **Action (A)**: our envoy, carrying the 节 (the envoy's staff with three tiers of yak-tail tassels), leaves through the gate with a gift chest when the gift > 0.
- **Outcome**: a foreign envoy in the **target faction's culture costume and colour** (CITY-VIEW §8 crowd looks) arrives with their banner. Normal / good: the two banners are crossed and tied with a red cord in front of the palace, bells ring; good: the target ruler's portrait appears with mood 'win' and the expiry date; great: the courtyard fills with both factions' banner bearers, lanterns are lit along the avenue, the sting's answer voice is played by the target's family instruments (§8.3: a Roman cornu answering our bianzhong); banner 「与 X 结盟」; seal 盟成.
- **miss**: the foreign envoy bows briefly and leaves; our envoy's 节 tassels droop; ribbon 「X 拒绝」 and 「礼金越厚、人望越高越易成功」.
- **Icon** (外交 button): the 节 staff upright with three tufted tiers, a jade disc (璧) hung from it. Accent plum `#8a5fc0`. Hover: the tassels sway. Sub-action icons in the 外交 menu: 同盟 two crossed pennants knotted together; 离间 a jade disc split by a crack with a whisper-curl beside it; 拉拢 an open hand offering a 马蹄金 ingot towards a helmet.
- **Sting** `sting:ally`: 宫调式, ♩ = 112, ritual colour (bianzhong 1 5 over qin); the answer phrase (voice `an`) is played by the **target's** family instrument; X adds choirOo, strings, dagu and luo; M a falling qin line with a pianissimo sheng on 6,+3. 2.14 / 3.21 / 4.29 / 2.14 s.
- **Press sound** `cmd-press-diplomacy`: one high bianzhong ping.
- **Voice**: `cmd.ally.yes` / `cmd.ally.no` (the target ruler, in his language per the voices task).
- **Card**: two banners approach from the card's two sides; on success they knot.

### 5.11 外交·离间 `discord` and 拉拢 `persuade`

The subject is an enemy general in an enemy city, so both always use the **card stage** (with our city view dimmed behind it when it is open).

- **离间 card**: night palette. Our agent's portrait (small, lower left) slides a sealed letter across the card; the target's portrait (centre, enemy frame colour) shows a loyalty dial. On success the dial falls with the count-down and a hairline crack runs across the portrait frame (good: the crack branches; great: the frame splits and the portrait's mood changes to 'angry', and when loyalty < 40 a small 「可能出奔」 tag pulses); seal 离心. Miss: the target tears the letter in two, the agent's portrait winces; 「X 识破了离间之计」 and 「智力高于对方时更易成功」.
- **拉拢 card**: the target's portrait with a loyalty dial and our ruler's portrait offering the ingot. Success (good): the target's frame recolours from the enemy colour to ours with a wipe (0.5 s) and he bows; great (献城): the card widens into a strip showing the target city's map model and its banner changing colour on the world map behind (the map is refreshed on the real state right after); seal 倒戈. Miss: the portrait turns away.
- **Stings**: `sting:discord`: a 羽 frame with 清角 (4) and 变宫 (7) and a #4 tritone in the stab chord; pizz and muyu whisper, low string tremolo; G a stab and bangzi crack; X stab plus tamtam; M a muyu double tick and pizz falling to 3,. 2.31 / 3.46 / 3.46 / 2.31 s. `sting:persuade`: erhu and pipa in 羽 resolving to 宫 (the target "comes over"); X adds horns and luo. 2.31 / 3.46 / 3.46 / 2.31 s.
- **Voice**: `cmd.discord.ok` (agent), `cmd.persuade.yes` (target 「愿效犬马之劳」).

### 5.12 出征 `attack`

- **Anchor**: `gate.out` (outside the main gate, looking back at the army and the city).
- **Action**: the sortie generals line up in front of the gate on horseback, each with a short column behind; the commander (ruler or first general) rides forward and raises his weapon; in front of the screen the two halves of a gilt-bronze **tiger tally** (虎符) slide together and lock with a click at the sting's downbeat (the call to war).
- **good**: banners of every general unfurl; a war drum on the wall is struck; 「佳」.
- **great**: the army stretches out of the gate in a long column, the commander's horse rears, dust rises, a flock of crows lifts from the trees; banner 「X 亲征」 or 「大军四万　出师」; seal 出师.
- Then the existing `horn` SFX and world-map march (`map.march`, 1.4 s) and the battle flow (the battle music takes over).
- **Icon** (primary button): the tiger tally in profile, gilt, its split line visible down the back, over two crossed 戟. Accent gold on cinnabar. Hover: the two halves part 2 px and snap together.
- **Sting** `sting:attack`: 羽调式, ♩ = 126. Suona 6,. 2 3 6 (the minor form of the 「霸业」 cell) over horns and timpani; good adds bo and luo; great adds choir, tamtam and a tanggu roll. 1.90 / 2.86 / 3.81 s. It is distinct from the clash stinger (`clash`, ♩ 150, 6 3 2' figure).
- **Press sound** `cmd-press-attack`: a bronze clack (the tally halves) with a low horn swell.
- **Voice**: `cmd.attack.start` (the commander's war cry, per culture and language).

### 5.13 赏赐 `reward`

- **Anchor**: `palace` hall (or the treasury when the ruler is not in the city: the governor gives the reward).
- **Action**: the ruler (or governor) hands a lacquer tray with coin strings and a bolt of silk to the kneeling general; coins chime as the tray passes (`coin` SFX); the general's loyalty bar (above his head, a red cord with a 忠 knot) fills.
- **Reveal**: 忠诚 old → new, −100 金 (or the item's icon for 赐宝物).
- **good**: the general rises and bows deeply, a shower of gold leaf flakes (60); 「佳」.
- **great** (fitting item gift, or 100 on a top roll): the gift item is presented on a stand (its model, ITEMS), the general raises it overhead; the court's lanterns brighten; scripted line for special pairs (赤兔 → 关羽, ITEMS §6.6); banner 「X　誓死效忠」; seal 恩泽.
- **Icon**: a lacquered tray seen from above at a tilt, with a coiled string of 五铢 coins and a folded silk bolt. Accent gold `#e0b04a`. Hover: one coin jumps and sparkles.
- **Sting** `sting:reward`: 宫调式, ♩ = 112. Yangqin descending 1' 6 5 3 – 2 3 1 (giving), bianzhong dots; good adds dizi and strings; great adds choirOo and full strings. 2.14 / 3.21 / 4.29 s.
- **Press sound** `cmd-press-reward`: two coins clinking.
- **Voice**: `cmd.reward.thanks` (the general).
- **Card**: the tray icon tips and coins pour into the general's portrait frame; the loyalty cord fills.

### 5.14 交易 `trade.buy` / `trade.sell`

- **Anchor**: `market` (the grain stalls) and `granary`.
- **Action**: at the grain stall a merchant's steelyard (秤, beam and weight) tips; buy: coin strings go from our clerk to the merchant and sacks go onto a cart towards the granary; sell: the reverse. The price board above the stall shows the month's price (8 / 12 / 16) in large numerals.
- **good** (favourable price): the merchant throws an extra sack on top; the crowd turns to look; 「佳」.
- **great** (big favourable deal): the granary's doors open and sacks stack up visibly to the eaves (or coin chests in the treasury); banner 「丰年购粮」 / 「高价售粮」; seal 居奇.
- **Icon**: a steelyard balance with a grain sack on the hook and the weight on the beam, a coin hanging below as the counterweight's tag. Accent bronze `#9a7a4a`. Hover: the beam tips up and settles. Sub-icons: 买粮 a sack with an inward arrow; 卖粮 a coin string with an inward arrow.
- **Sting** `sting:trade`: 商调式, ♩ = 124. A haggling dialogue: pipa asks 2 5 2', erhu answers 1' 6 2, bangzi and ban like counting rods; great adds horns and dagu. 1.94 / 2.90 / 3.87 s.
- **Press sound** `cmd-press-trade`: a bangzi clack and a coin.
- **Card**: the steelyard icon tips; sacks / coins slide between two columns.

### 5.15 任命 `appoint`

- **Anchor**: `palace` (the government office's front steps and its name plaque).
- **Action**: the ruler (or the faction's seal-keeper if the ruler is elsewhere) presents a bronze seal with a tortoise knob and a hanging ribbon (印绶) to the new governor; he takes it and stamps a document on a low table: a red square seal mark appears in close-up; the plaque above the door is re-lettered with his name (brush animation).
- **good** (well-suited): two officials bow from the sides; 「佳」.
- **great** (政治 ≥ 90): the city's officials form two rows on the steps and bow; the palace's lanterns light; banner 「X 治 Y」; seal 得人.
- **Icon**: a square bronze seal with a tortoise knob, a ribbon (绶) looping from the knob, and the red impression of the seal beside it. Accent jade-blue `#3a7fa0` ribbon. Hover: the seal stamps and lifts, leaving a red square.
- **Sting** `sting:appoint`: 宫调式, ♩ = 108. Solo horn 1 3 – 2 6, 1 over bianzhong, a timpani pulse and one deep dagu "seal" stroke at the end of bar 2; good adds sheng and bianzhong; great adds choir and tamtam ring. 2.22 / 3.33 / 3.33 s (+ tamtam tail).
- **Press sound** `cmd-press-appoint`: a soft seal thump on paper.
- **Card**: the seal icon stamps the card; the governor's portrait appears in the impression.

### 5.16 市场 `market` (v4, ITEMS §6)

- ITEMS owns the shop screen and the purchase reveal (ITEMS §5.5). This file adds: the city-view staging (tapping the market or the 兵器铺 building opens the shop; after a purchase the item's model is placed on the shop's display rack in the city view until it is equipped), the button, the icon and the sting.
- **Icon**: a shop awning on two poles with a sword and a small round shield hanging under it. Accent teal `#3aa5a0`. Hover: the awning flaps; the sword glints.
- **Sting** `sting:market`: by rarity: common / uncommon `sting:market` (pipa 1 3 5 1' + yangqin, 2.14 s), rare `:good` (bianzhong and strings, 3.21 s), epic / legendary `:great` (tamtam, choir, the 「霸业」 cell on horns, 4.29 s).
- **Press sound** `cmd-press-market`: a pipa pluck and a wind bell.

### 5.17 授官 `title` (v4, TITLES)

- TITLES owns the rules and the title list. Default ceremony (until TITLES specifies its own): **anchor** `palace` hall; the general kneels, the ruler places the rank cap (进贤冠 for civil families, a plumed helmet for 武官) and hands the ribbon of the title's colour (TITLES family colours: 武官 `#c8382c`, 谋臣 `#2f6db5`, 文官 `#2f9a55`); the title name appears in brush letters with its tier stars. Tier 3 good: officials bow; tier 4–5 great: the hall's banners unroll, choir, seal 册封.
- **Icon**: an official's cap (进贤冠, the slanted front beam) resting on a cushion with a ribbon tail and three small rank stars. Accent plum-gold `#a8742e`. Hover: the stars light one after another.
- **Sting** `sting:title`: 宫调式, ♩ = 112, a rising 1 3 5 6 on bianzhong (promotion) over sheng and qin; great adds choir, tamtam and dagu. 2.14 / 3.21 / 3.21 s.
- **Press sound** `cmd-press-title`: a soft sheng breath with a small bell.

---

## 6. Icons

### 6.1 Design language

- **Canvas**: 64 × 64 viewBox, drawn for display at 40 px (desktop button), 28 px (phone button), 20 px (ribbon chips), 160 px (card stage). Keep a 4 px safe margin; the main silhouette fills 48–52 px.
- **Plate**: the icon sits on the button's lacquer seal plate (§7), not inside its own circle, so icons are free-standing silhouettes.
- **Line**: ink outline `#241a21`, 2.5 px at 64 (scales), round joins, slightly varying width (the outline is a filled path offset from the shape, not a stroke, so it looks brushed; 6 % width wobble along the path baked into the path data). Matches the in-game ink outlines of `SG.LP.outlineMaterial`.
- **Fill**: flat-shaded facets in the Lanternvale manner: each shape has a light facet (upper left, +10 % value), a base facet and a shade facet (lower right, −14 % value, hue shifted 6° towards teal); no gradients except one soft highlight dot on metal and lacquer.
- **Palette tokens** (CSS custom properties, used by both icons and buttons): `--ink #241a21`, `--paper #f5e6c8`, `--cinnabar #c8382c`, `--jade #4f9a76`, `--ochre #c99a2e`, `--gold #f3c969`, `--indigo #3b4f7a`, `--bronze #a7743a`, `--teal #3aa5a0`, `--plum #8a5fc0`, `--ember #e07a3a`, `--amber #d9933a`. Each icon uses ink + paper + its accent + at most one secondary.
- **Silhouette rule**: every icon must be recognizable as a black silhouette at 24 px and differ from every other icon by at least 18 % of pixels at that size (checked in `tests/domestic-fx.html`, §13).
- **Animated parts**: each SVG groups its moving parts (`<g class="ci-a">`, `<g class="ci-b">`) with `transform-box: fill-box; transform-origin` set in the icon definition, animated by CSS keyframes (§6.3). No SMIL.

### 6.2 The set

| id | Motif (silhouette) | Accent | Secondary | Hover micro-loop (≤ 0.7 s) |
|---|---|---|---|---|
| `develop` | 耒 planted in three furrows + jade seedling | jade | ochre (soil) | seedling sways, third leaf unfolds |
| `develop.land` | three furrows + seedling (no 耒), sun-arc behind | jade | ochre | leaf unfolds |
| `develop.industry` | anvil + raised hammer + 3-ray spark | ember | bronze | hammer taps ×2, spark blinks |
| `develop.town` | two-storey hip-roof house + red lantern + smoke curl | amber | cinnabar | lantern glows and swings |
| `recruit` | 建鼓 drum on stand + crossed sticks + 募 pennant | cinnabar | ochre | sticks strike, 2 pulse rings |
| `train` | straw 侯 target + arrow in bull + slanted 戟 | bronze | paper | arrow flies in, target wobbles |
| `search` | 斗笠 hat over a winding footprint trail into a mountain line, star-glint | indigo | paper | footprints appear in turn, glint |
| `hire` | 拱手 clasped hands + ribbon-tied scroll | ochre | cinnabar | hands bow 12°, ribbon flutters |
| `move` | forward-leaning 牙旗 over a road between two gate silhouettes | faction colour | ink | banner flutters, slides 4 px |
| `transport` | ox cart in profile, sacks, coin string | ochre | bronze | wheel turns once, coins swing |
| `diplomacy` | 节 staff with three tassel tiers + jade 璧 | plum | jade | tassels sway |
| `ally` | two crossed pennants knotted with a cord | plum | gold | knot tightens |
| `discord` | jade disc split by a crack + whisper curl | plum | ink | crack zigzags open 2 px |
| `persuade` | open hand offering a 马蹄金 ingot towards a helmet | plum | gold | ingot gleams, helmet tips |
| `attack` | gilt tiger tally (split line) over crossed 戟 | gold | cinnabar | halves part and snap |
| `reward` | lacquer tray with coiled coin string and silk bolt | gold | cinnabar | one coin jumps, sparkle |
| `trade` | steelyard with sack on hook and weight on beam | bronze | ochre | beam tips and settles |
| `trade.buy` / `trade.sell` | sack + inward arrow / coin string + inward arrow | bronze | jade / gold | arrow nudges in |
| `appoint` | tortoise-knob seal + ribbon + red impression | teal-blue | cinnabar | seal stamps, impression appears |
| `market` | awning on poles + hanging sword and round shield | teal | bronze | awning flaps, sword glints |
| `title` | 进贤冠 cap on a cushion + ribbon + 3 stars | plum-gold | gold | stars light in turn |
| `harvest` (moment) | sheaf of millet tied with a red cord + sickle | ochre | gold | sheaf nods |
| `stageUp` (moment) | a roof ridge with 鸱吻 rising out of a cloud scroll | gold | ink | ridge rises 3 px |
| `month` (moment) | a crescent moon over a bell (月初) | paper | bronze | bell swings |
| `endMonth` (shell) | setting sun behind a gate tower | amber | indigo | sun sinks 3 px |

### 6.3 Card animations (160 px, card stage)

The card stage enlarges the icon and plays a longer loop tied to the vignette beats: `cue` (stamp in), `action` (the icon's motif performs the command, e.g. the seedling grows into a row, the hammer strikes three times in the sting rhythm, the cart crosses), `reveal` (the result object appears beside the icon: sacks, coins, a portrait, a crack), `great` (the icon's accent floods the card's header band and the seal slams). Each is a CSS keyframe set named `ci-<id>-<beat>`; durations are set from the vignette's time map via CSS variables (`--fx-a`, `--fx-t`).

### 6.4 Shell icons (offered to the menu task)

For the top bar (requests 1 and 11), in the same language: 势力 (a map scroll with three banner pins), 大事 (a stitched bamboo-slip book), 记录 (a brush over an inkstone), 音乐 (a 琴 seen from above), 设置 (a bronze 规矩 compass-and-square), 菜单 (three stacked bamboo slips), 武将 (a helmet with a writing brush crossed behind it), 结束本月 (`endMonth` above). The menu task may adopt or replace them.

---

## 7. Command buttons

### 7.1 Anatomy

- **Plate**: a lacquer seal plate, rounded square, corner radius 22 % of the side. Four flat bevel facets (top / left lighter +6 %, bottom / right darker −8 %) around a slightly inset face, so the plate reads as a carved low-poly token rather than a flat web button. Face colour `#2a2430` (ink lacquer), rim line 1.5 px `--gold` at 35 % alpha (100 % on hover), inner hairline `rgba(255,240,210,.08)`.
- **Icon**: centred in the upper 70 %; 40 px desktop, 28 px phone.
- **Label**: below the icon, Kai / Song serif (`"STKaiti","Kaiti SC","KaiTi",…`), 15 px desktop / 12 px phone, `--paper`.
- **Token pip**: top-right, a 9 px gold dot for commands that use a token; commands without a token (赏赐, 交易, 任命, 市场) show none. When the month has no tokens left the pips of token commands turn hollow (the existing grey-out still explains why).
- **Size**: 76 × 76 px desktop, 56 × 60 px phone landscape (≥ 44 px touch target), 6 px gaps.
- **Order and groups** in the city panel grid (the shell task owns the exact layout): 内政 开发 征兵 训练 搜索 登用 赏赐 任命 · 调度 移动 输送 交易 市场 · 外交与军事 外交 授官 出征 (出征 last, primary). Desktop hotkeys `1`–`9`, `0`, `-`, `=` for the first 12 in this order, shown as a small keycap in the plate's lower-left corner on hover only (WASD / QE stay with the camera).

### 7.2 States and animations

| State | Visual | Timing |
|---|---|---|
| idle (available) | static; every 6–9 s (seeded per button) a 'breath': scale 1 → 1.03 → 1 | 0.6 s ease-in-out |
| hover / focus | plate lifts −2 px, shadow `0 2px 0` → `0 6px 10px rgba(0,0,0,.35)`, rim to 100 %, the icon plays its micro-loop (§6.2) once, a brush underline draws under the label left → right | 120 ms ease-out; underline 180 ms |
| keyboard focus | as hover + 2 px `--gold` focus ring, offset 2 px | — |
| press (pointer down) | scale 0.94, translateY +1 px, ink ripple from the press point (radial, ink at 25 %), the command's press sound (§5, §8.6) | 60 ms; ripple 300 ms |
| release (activate) | spring back to 1.0 with overshoot 1.04 (spring k = 420, c = 26, ≈ 220 ms); a gold ring expands from the plate (scale 1 → 1.5, alpha 0.8 → 0) | 260 ms; the command dialog opens at 120 ms |
| disabled (has a reason) | icon desaturated 70 %, alpha 0.55, a small lock seal glyph lower right | — |
| denied tap | the existing `sg-cmd-deny` shake (kept), the lock glyph wiggles, a dull `cmd-deny` thud; the explanation toast as today | 0.38 s |
| hint (tutorial / move tip) | the existing `sg-cmd-hint` gold pulse plus a diagonal shimmer sweep across the plate | 1.1 s × 4 |
| primary 出征 | ember glow pulse when attackable targets exist: `box-shadow 0 0 0 → 0 0 12px rgba(200,56,44,.55)` | 2.4 s loop |
| busy (a command is running) | all buttons dim to 0.7 and ignore input (as today); the active one keeps a slow gold rim rotation (conic gradient, 1.6 s / turn) | — |
| result echo | after a great result, the command's button shows a tiny seal of the great stamp for 4 s and the plate's rim flashes once when the city panel is rebuilt | 4 s |

Reduced motion: no breath, no ripple, no ring, no glow pulse; hover only changes the rim and label colour.

### 7.3 Implementation notes

- `SG.CmdIcons.button(parent, id, label, onClick, { primary, token, why })` returns the `HTMLButtonElement` built through `SG.UI.button` (so the existing `data-cmd`, `data-why`, `aria-disabled`, `explainCmd` and `hintCmd` behaviour is kept). It adds `.sg-cmdbtn` and injects the icon SVG once per id (cached string).
- The ripple and ring are CSS (`::after` with a radial gradient, positioned from `--px`, `--py` set on pointerdown). The spring is a CSS `linear()` easing approximating k 420 / c 26 (fallback `cubic-bezier(.34,1.56,.64,1)`).
- Press sounds play on `pointerdown` (not click) for responsiveness; they replace `SG.Sfx.click()` for command buttons only.

---

## 8. Music stings

### 8.1 How they play

- `SG.Sfx.cmdSting(id, tier, { family, answerFamily, fast })` → `{ key, duration, sections: { A, tag } } | null` (null when music is off, the engine is missing or audio is locked). It resolves the key (`sting:<id>[:tier][@family]`, falling back to the `hua` family), and plays it through the engine as an **overlay** (`e.play(key, { overlay: true, fade: 0.15, duck: 0.35 })`), the same mechanism as the clash stinger.
- **Transposition**: like `followPlan` in `audio.js`, the sting is transposed to the tonic of the current map piece (`mainInfo().tonicPc`), so it sits in the key of whatever regional map music is playing. **No tempo following**: the vignette is timed by the sting's own length. **Start**: immediately, unless the main piece's next beat is ≤ 120 ms away, in which case it waits for it (`sync` with a 120 ms window).
- **Ducking**: the map music drops to 0.35 for the sting and returns over 0.6 s after its last note. Consecutive stings within 0.4 s (a fast player) replace each other: the previous one fades in 0.15 s.
- **Pre-warm**: when the city panel opens, `SG.Music.prepare` the A sections of the 6 most-used stings of the player's family in idle slices (≤ 8 ms each); the rest prepare on first use (the first play of a sting may start up to one frame late; never a hitch > 8 ms).
- **Music off**: no sting; the vignette uses its default timing and the tier SFX (§8.6) still play if sound is on.

### 8.2 Structure of every sting

Sections (bars of 2/4 except 输送 and 秋收 in 3): **A** the action (2 bars for deterministic commands, 1 bar for chance commands = the attempt), **N** normal outcome tag (chance commands, 1 bar), **G** good tag (1 bar after a deterministic A; 2 bars for chance commands), **X** great tag (2 bars; talent and legendary moments 3 bars) with fuller orchestration and gongs / bells ringing out, **M** miss (1 bar, gentle, falling). Forms: normal `A` or `A N`; good `A G`; great `A X`; miss `A M`. The A-section motifs are all different (checked: first four pitches of the lead voice).

| Sting | Mode · tonic | ♩ | Meter | Lead and colour | normal / good / great / miss (s) |
|---|---|---|---|---|---|
| `develop.land` 开垦 | 徵 · 5 | 116 | 2 | dizi, zheng, sheng, bianzhong, choirOo, muyu | 2.07 / 3.10 / 4.14 |
| `develop.industry` 冶铸 | 商 · 2 | 126 | 2 | pipa staccato, bangzi anvil, horns, suona | 1.90 / 2.86 / 3.81 |
| `develop.town` 兴市 | 宫 · 1 | 120 | 2 | yangqin, ruan, dizi, muyu, xiaoluo, suona | 2.00 / 3.00 / 4.00 |
| `recruit` 募兵 | 羽 · 6 | 132 | 2 | horns, suona, timpani, dagu, choir | 1.82 / 2.73 / 3.64 |
| `train` 操练 | 角 · 3 | 132 | 2 | erhu spiccato, tanggu, bo, ban, choir shout | 1.82 / 2.73 / 3.64 |
| `search` 寻访 | 羽 · 6 → 宫 | 108 | 2 | xiao, qin, strings; horn 「霸业」 on discovery | 2.22 / 3.33 / 4.44 / 2.22 |
| `hire` 求贤 | 宫 · 1 | 112 | 2 | qin, erhu; horns and choir 「霸业」 | 2.14 / 3.21 / 4.29 / 2.14 |
| `move` 移驻 | 商 · 2 | 116 | 2 | ruan walking, pizz, muyu steps, horns | 2.07 / 3.10 / 4.14 |
| `transport` 转输 | 徵 · 5 | 168 | 3 | ruan, sheng, muyu + hoof clip-clop | 2.14 / 3.21 / 4.29 |
| `ally` 会盟 | 宫 · 1 | 112 | 2 | bianzhong, qin; answer voice in the partner's family | 2.14 / 3.21 / 4.29 / 2.14 |
| `discord` 反间 | 羽 with 清角, 变宫, #4 | 104 | 2 | pizz, xiao whisper, low tremolo, stab | 2.31 / 3.46 / 3.46 / 2.31 |
| `persuade` 说降 | 羽 → 宫 | 104 | 2 | erhu, pipa, horns | 2.31 / 3.46 / 3.46 / 2.31 |
| `attack` 出师 | 羽 · 6 | 126 | 2 | suona 「霸业」 minor, horns, timpani, choir | 1.90 / 2.86 / 3.81 |
| `reward` 恩赏 | 宫 · 1 | 112 | 2 | yangqin falling, bianzhong, strings, choirOo | 2.14 / 3.21 / 4.29 |
| `trade` 市易 | 商 · 2 | 124 | 2 | pipa / erhu haggling, bangzi, ban | 1.94 / 2.90 / 3.87 |
| `appoint` 授印 | 宫 · 1 | 108 | 2 | solo horn, bianzhong, timpani, dagu "seal" | 2.22 / 3.33 / 3.33 |
| `market` 购宝 | 宫 · 1 | 112 | 2 | pipa, yangqin; bianzhong; tamtam, choir, 「霸业」 | 2.14 / 3.21 / 4.29 |
| `title` 册封 | 宫 · 1 | 112 | 2 | sheng, bianzhong rising 1 3 5 6, choir | 2.14 / 3.21 / 3.21 |
| `harvest` 秋成 (moment) | 徵 · 5 | 168 | 3 | dizi, yangqin, sheng, dagu dance; suona | 3.21 / 4.29 / 4.29 / 3.21 |
| `stageUp` 升格 (moment) | 宫 · 1 | 132 | 2 | suona, horns, bianzhong, luo | great 2.73 |
| `month` 朔日 (moment) | 宫 · 1 | 96 | 2 | one bianzhong figure, sheng | 1.25 |

The great tiers of 搜索 (talent), 登用 and 市场 (legendary) quote the title's leitmotif 「霸业」 5,. 1_ 2 5 (`js/music-han.js` header); 出征 opens with its minor form 6,. 2_ 3 6. A player hears the dynasty's theme exactly when the dynasty gains a hero, a treasure, or marches.

### 8.3 Culture families (request 7 coordination)

The sting is played in the player's culture family (the faction culture via `CITY-VIEW` §3's family table: hua, han3, wa, steppe, oasis, indic, persia, arabia, nile, roman, north, nanhai). Two transforms, both in `domestic/stings.js`:

1. **Instrument roles**: every instrument used belongs to a role (flute, flute2, bow, pluck 1–4, bell, reed, brass, pad, drum, wood, gong, cymbal …); each family substitutes per role (all names exist in `js/music-inst.js`):

| Family | flute | bow | pluck | reed | brass | drum | other |
|---|---|---|---|---|---|---|---|
| hua 华夏 | dizi / xiao | erhu | zheng, pipa, yangqin, ruan, qin | suona | horns | dagu, tanggu | bianzhong, sheng, muyu, luo, bo |
| han3 三韩 | daegeum | haegeum | gayageum | piri | horns | buk, janggu | jing, kkwaeng |
| wa 倭 | shinobue / shakuhachi | shakuhachi | koto | shinobue | horns | taiko | — |
| steppe 草原 | whistle / overtone | morin | dombra | suona | horns | shamandrum | kargyraa pad, hoof as wood |
| oasis 绿洲 | ney | kamancheh | pipa, santur, oud | suona | horns | daf, darbuka | drones |
| indic 天竺 | bansuri | bansuri | sitar | suona | horns | bayan, dayan | gender bells, drones |
| persia 安息 | ney | kamancheh | santur, oud | zurna | horns | daf | drones, darbuka |
| arabia 阿拉伯 | ney | kamancheh | oud, qanun | zurna | horns | darbuka, daf | crotala, drones |
| nile 库施·阿克苏姆 | ney | ney | harp | aulos | horns | frame | crotala (sistrum colour), drones |
| roman 罗马 | aulos | aulos | harp (lyre) | aulos | cornu | wardrum, frame | crotala |
| north 北方 | whistle | whistle | harp | chanter | horns, cornu (carnyx) | bodhran | drones |
| nanhai 南海 | suling | suling | saron, gender | suona | horns | kendang | bonang, kempul, kenong, gongageng |

2. **Interval colour**: major intervals above the sting's tonic are bent to the family's mode (minor 羽 stings keep their shape, the tonic never moves): wa M2 → m2, M3 → P4, M6 → m6 (都节 / In); indic M2 → m2, M6 → m6 (Bhairav); persia M2 → m2, M3 → m3, M6 → m6 (Shur); arabia M2 → m2, M6 → m6 (Hijaz); roman M3 → m3 (Dorian); the other families keep the pentatonic. Example: `develop.town` A in wa: `1_ 4_ 5_ 4_ | b6_ 5_ 4_ 1_`.

The 同盟 answer voice uses the **partner's** family. If the ruler-suite task (request 7) gives each ruler a suite with its own palette, `cmdSting` takes the suite's `family` (and optionally a per-suite instrument override table with the same role names) instead of the culture default.

### 8.4 API summary

```
SG.MusicStings.ids                         → the 21 ids
SG.MusicStings.key(id, tier, family?, answerFamily?) → registered key (built and registered on first request, cached)
SG.MusicStings.sections(key)               → { A: seconds, tag: seconds, total }
SG.Sfx.cmdSting(id, tier, { family, answerFamily, fast }) → { key, duration, sections } | null
```

`fast: true` plays the tag alone (§3.5).

### 8.5 Mixing targets

Peak ≤ 0.98 after the master limiter with the map music playing underneath at its duck level; sting RMS 3–6 dB above the ducked music; no sting louder than the clash stinger (gain 0.85 vs 0.9).

### 8.6 New sound effects (`SG.Sfx.play` names; recipes for `audio.js`)

| Name | Recipe (synthesized like the existing SFX) | Used by |
|---|---|---|
| `cmd-press-<id>` (×17) | 40–120 ms transients: wood knock (develop: band-passed noise 900 Hz + 120 Hz thud), anvil tink (inharmonic partials 2.1 / 3.9 / 5.3 kHz, 80 ms), carpentry double tap, drum tap (dagu sample, art 1), bo tick, lantern creak (noise sweep) + chime (3 partials), qin harmonic, two hoof steps, axle creak, bianzhong ping, tally clack (bronze 1.4 kHz inharmonic) + horn swell, two coins, bangzi + coin, seal thump (60 Hz + paper noise), pipa pluck + bell, sheng breath | buttons §7 |
| `cmd-deny` | dull 90 Hz thud with a muted paper flick | denied tap |
| `fx-tick` | 18 ms sine blip 1.8 kHz, pitch per step | counters |
| `fx-land` | 60 ms wood + low thump | final count value |
| `fx-seal` | taiko-like 70 Hz hit + paper slap noise | great seal |
| `fx-good` | two-note bell (5th) 0.4 s | good seal (also when music is off) |
| `fx-great` | gong swell 1.2 s (tamtam sample) + bell | great seal (also when music is off) |
| `fx-miss` | soft breath noise falling, 0.4 s | miss (also when music is off) |
| `fx-build` | three wood knocks + dust hiss | buildings rising in the city view |
| `fx-wind` | filtered noise gust 0.8 s | search miss, transitions |

---

## 9. Moments outside the commands

### 9.1 秋收 harvest (month 7)

- `StrategyAI.endMonth` returns, next to its news strings, `harvests: [{ city, land, harvest, roll }]` for the player's cities (the values it already computes; `roll = harvest / (land × FoodPerLand)`; no new draw).
- Before the month's news dialog, a **harvest montage**: for each player city, best first, up to 4 cities (the rest are summarised in one line): 1.1 s per city in the diorama / city stage: the field ring turns gold in a wave, reapers with sickles, sheaves stacked, carts roll to the granary, the granary's fill level rises (CITY-VIEW §5); chip 粮 +harvest. The tier of the best city chooses the sting: `sting:harvest` (3.2 s), `:good`, `:great` (4.3 s, suona and luo; seal 大有), `:miss` for a lean year (the field is pale, the line 「收成不佳，储粮以备」). Skippable as one unit. In 快速 mode only the totals card (1.2 s).
- This makes 开发·土地 pay off visibly once a year: the montage shows exactly the land the player developed (new tiles from this year's vignettes are tagged and sparkle when reaped).

### 9.2 城池升阶 building stage-up

- Stage-ups caused by a command are shown inside that command's great tier (§5). Stage-ups caused by other changes (population growth raising the palace, troops arriving raising the barracks, gold piling in the treasury, a battle lowering troops, items changing the shop) are collected at month end and shown in the next month's start as a short celebration (`sting:stageUp:great`, 2.7 s; seal 升格) per city, max 3, with the city view's building rising (CITY-VIEW §4.4). Drops in stage are shown silently (no celebration, the building simply looks poorer next time; the stage bar in the city panel turns amber).

### 9.3 月初 month start and 结束本月

- **Month start**: `sting:month` (1.25 s bianzhong) plays under the existing month banner; in the city view the clock jumps to morning (CITY-VIEW §6.6).
- **结束本月**: as tokens are spent the city view's sun moves from morning to dusk (CITY-VIEW §6.6); when the player presses 结束本月 the view slides to dusk, lanterns light along the streets in a wave (0.8 s), then the AI phase and news follow as today. This makes the remaining tokens visible as remaining daylight.

### 9.4 Income

Once a month, after income, the top bar's 金 and 粮 figures count up from the old totals to the new over 0.8 s with `fx-tick`; a gold coin icon pops from each player city's map label (0.4 s, staggered 60 ms, max 12 cities). Losses (salary, food) count down in red. Pure presentation of numbers the game already computes.

---

## 10. Data schemas

### 10.1 Vignette script registry (`js/domestic-fx.js`)

```js
SG.DomesticFx.define('develop.land', {
  icon: 'develop.land',                 // SG.CmdIcons id
  sting: 'develop.land',                // SG.MusicStings id
  stage: 'city',                        // 'city' | 'card' (card-only commands: discord, persuade)
  anchor: 'farms',                      // CITY-VIEW §7.2 anchor id
  shot: { kind: 'bldg', pitch: 38, distK: 3.2, yawFrom: 'gate' },
  chips(res, ctx) { return [{ icon: 'develop.land', label: '土地', from: res.before, to: res.after }, { label: '金', delta: -res.cost, cost: true }]; },
  forecast(res, ctx) { return '秋收预计 +' + Math.floor((res.after - res.before) * SG.Balance.FoodPerLand) + ' 粮'; },
  stageBar: 'farms',                    // building whose progress bar is shown
  action(d, res) {},                    // d = director (CITY-VIEW §7.3): d.actor(), d.tween(), d.burst(), d.grow(), d.wait()
  good(d, res) {},                      // extra flourish
  great(d, res) {},                     // spectacle; default: d.upgrade(stageBar) when stAfter > stBefore
  miss(d, res) {},
  card: { motif: 'grow-row' },          // card-stage animation preset (§6.3)
  voice: { start: 'cmd.develop.start', great: 'cmd.develop.great' },
});
```

### 10.2 Public API

```
SG.DomesticFx.play(res, tierInfo?) → Promise<void>   tierInfo defaults to SG.DomesticTiers.tier(res)
SG.DomesticFx.skip()                               end the running vignette now
SG.DomesticFx.busy                                 true while a vignette runs
SG.DomesticFx.settings / setSetting(k, v)          the sanguozhi2_fx_v4 object (try/catch)
SG.DomesticFx.harvest(records) → Promise<void>     §9.1
SG.DomesticFx.stageUps(list) → Promise<void>       §9.2
SG.DomesticTiers.tier(res) → { tier, why, stamp }  pure
SG.CmdIcons.svg(id, { size, state }) → string      SG.CmdIcons.button(parent, id, label, onClick, opts) → HTMLButtonElement
```

### 10.3 Save data

None in the game save. `G.fxStages` (per city, the last stage vector shown, a 16-digit string) is kept **in the save** only so that §9.2 does not celebrate the same stage-up twice across a reload: about 196 × 17 bytes ≈ 3.3 KB, in the shared v4 save version bump (integration owns the number). Old saves: missing field → initialised from the current stages without celebrations.

---

## 11. Integration points

| File · function | Change |
|---|---|
| `js/model.js` · `Commands.develop / recruit / train / search / hire / reward / move / transport / buyFood / sellFood / ally / discord / persuade` | keep the drawn `R(0,4)` in a local and set `Commands.lastResult = {…}` (§3.2) after the state change. No change to formulas, messages or the order of random draws |
| `js/strategy-screen.js` · each `cmd*` handler | after the model call: `await SG.DomesticFx.play(SG.Commands.lastResult)` (guarded `if (SG.DomesticFx)`), then the existing `report` / `toast`. 赏赐, 任命 and 交易 used toasts; they now show the vignette and keep the toast text as the ribbon's caption (no extra dialog) |
| `js/strategy-screen.js` · `cmdAppoint` | build `lastResult` there (`{ cmd:'appoint', gen, pol, prevPol }`), since 任命 has no model function |
| `js/strategy-screen.js` · `cmdAttack` | `play({ cmd:'attack', … })` before `sfx('horn')` and the march |
| `js/strategy-screen.js` · `cmdMove` / `cmdTransport` | play before `marchRoute`; add the cart march for 输送 |
| `js/strategy-screen.js` · `refreshCityPanel` / `addCmd` | build buttons with `SG.CmdIcons.button` (same `why` contract); add the speed chip; the city panel's 「入城」 button (CITY-VIEW §2) |
| `js/strategy-screen.js` · month flow (around `StrategyAI.endMonth`) | `await SG.DomesticFx.harvest(res.harvests)` in month 7 before `sayNews`; `stageUps` at month start; the income count-up (§9.4) in `refreshAll` |
| `js/strategy-ai.js` · `endMonth` | also return `harvests` (attach as a property of the returned array so existing callers keep working: `news.harvests = [...]`) |
| `js/audio.js` | `cmdSting`, new SFX recipes (§8.6), load `music-stings.js` with the other music files |
| `index.html` | load `domestic-tiers.js` after `model.js`; `music-stings.js` after `music-world.js`; `cmd-icons.js`, `domestic-fx.js` after `ui.js` and the city-view files |
| settings screen (menu task) | 「内政演出」 options and 「画质」 (shared with CITY-VIEW) |

---

## 12. Performance budgets

| Item | Budget |
|---|---|
| JS per frame during a vignette (director, tweens, particles, DOM counters) | ≤ 1.5 ms (desktop), ≤ 3 ms (iPhone class) |
| Extra draw calls (actors, particles, highlight, seal plane) | ≤ 12; particles are one instanced `THREE.Points` / quad batch per vignette, ≤ 400 alive |
| Actors spawned for a vignette | ≤ 40 instanced figures + ≤ 4 full generals (the generals use the city view's mid LOD ≤ 4 k tris) |
| DOM nodes added (ribbon, chips, banner, seal) | ≤ 40, removed at close |
| First play of a vignette kind | ≤ 50 ms set-up, pre-warmed in idle callbacks when the city panel opens (shader variants compiled with `renderer.compile` on the city scene during idle) |
| Diorama | renders the city scene at the medium LOD into a scissored viewport; the world map is not re-rendered while it is open (its last frame stays); target 60 fps desktop, 30 fps iPhone |
| Memory | no textures (vertex colours, SVG icons cached as strings); stings' samples come from the shared sample cache (`SAMPLE_BUDGET`) |
| Audio | a sting adds ≤ 8 legato / poly voices; samples pre-rendered in ≤ 8 ms slices |

---

## 13. Test plan and balance targets

### 13.1 Node: `tests/domestic-tiers.js` (new)

1. **Rules**: one hand-built record per row of §4.2 → the expected tier and stamp (≈ 70 cases), including edges (`before = 99, after = 100`, `roll = 4` at the 999 cap, `p = 0.2`).
2. **No RNG**: run `tests/sim.js`'s classic campaign (seed 7, 5 years) with `lastResult` recording and `DomesticTiers.tier` called on every record; the state JSON hash must equal the run without them (same technique as `tests/world.js` `--classic-hash`).
3. **Distribution**: the Monte-Carlo of `domestic/tiers.js` (12 seeds × 18 rulers × 60 months): for 开发 ×3 and 训练 great 5–16 % and good 18–40 %; no command's great above 30 % (n ≥ 200). Current result: ALL OK (§4.3).
4. **lastResult schema**: every command sets `lastResult.cmd` and the fields of §3.2 (type checks).

### 13.2 Node: `design-v4/domestic/stings.js` → `tests/music-stings.js`

1. All 780 keys validate with `SG.Music.validate` (bar lengths, voices, instruments). Current: ALL OK.
2. Notated lengths: normal 1.7–2.7 s, good 2.6–3.6 s, great 3.0–4.5 s, miss 1.6–2.6 s; normal ≤ good ≤ great for each sting; moments ≤ 4.5 s.
3. Distinct A motifs (first four pitches of the lead voice).
4. Every substituted instrument exists in `SG.Music.INST`.

### 13.3 Browser: `tests/music.html` 「指令短乐」 tab

Offline-render each sting key (hua and two other families) with the map music underneath at duck level: no NaN, peak ≤ 0.98, RMS > −30 dBFS, and the sting ends ≤ 0.6 s after its notated length + ring-out (no hanging notes).

### 13.4 Browser: `tests/domestic-fx.html`

- Grid of every command × tier × stage (city, diorama, card) × speed; `?auto=1` plays all in sequence and reports per vignette: total time (within ±0.1 s of the sting length, or the default when music is off), max frame time (≤ 16 ms desktop, ≤ 33 ms phone emulation), draw calls added (≤ 12), DOM nodes left after close (0), no `console.error`, no `pageerror`.
- **Skip latency**: synthetic pointerdown at random times in each vignette: the stage reaches its final state within 100 ms and closes within 0.5 s.
- **Determinism**: the same record and month render the same decoration (sprouting tiles, crowd paths): compare screenshots of two runs at `Reveal + 0.2 s`.
- **Icons**: renders all 26 icons as silhouettes at 24 px; the pairwise pixel difference ≥ 18 % (§6.1); colour-blind simulation (deuteranopia, protanopia) screenshots for review.
- **Buttons**: hover / press / disabled / denied / hint / primary states at 1280 × 720 and 844 × 390; touch targets ≥ 44 px; keyboard focus order and hotkeys.

### 13.5 Playtest targets (manual, 30-minute sessions, classic and world)

- With the default 智能 mode, a 10-token month takes ≤ 45 s of vignette time in the first hour and ≤ 25 s afterwards (the 10-play rule).
- Players can say, after one month, what each command changed, from the vignette alone (the "helpful" goal): at least 4 of 5 testers name the stat and direction for 开发, 征兵, 训练, 输送.
- No tester turns the setting off in the first 30 minutes; if more than 1 in 5 does, shorten the normal tier by one bar.

---

## 14. Unity port (after the browser build is stable)

- `DomesticTiers.cs` (pure, port of `domestic-tiers.js`, shared tests: the Node rule cases exported as JSON and replayed by the C# harness, like `Tests~/duel`).
- `DomesticFx.cs`: the director as a coroutine with the same time map; ribbon, chips, seals and banners in UGUI (or UI Toolkit) with the same layout and timings; card stage as a UGUI prefab; icons exported from the SVG strings to sprites at 40 / 28 / 20 / 160 px by `tools/export-unity-icons.js` (new, renders through a headless canvas).
- Stings: add the `sting:*` keys to `tools/export-unity-music.js` (the Unity music engine plays the same compiled event lists); the family transforms run at export time (780 keys, ≈ 120 KB of event data).
- The city stage is the Unity `CityView` (CITY-VIEW §12); the diorama is a second camera rendering into a `RenderTexture` shown in the scroll frame.

---

## 15. Open questions

1. **Command names in v4.** 授官 is a placeholder until `TITLES.md` fixes the command's name and where it lives (a city-panel command or the Generals screen). The icon and sting are ready either way.
2. **Smart mode threshold.** 10 full plays per command before normal results go fast is a guess; playtests may prefer 6 or 15.
3. **Diorama on phones.** The PiP needs the medium LOD to hold 30 fps on iPhone-class devices; if the city-view pilot misses that, phones default to the card stage and the diorama becomes desktop / iPad only.
4. **Family-name formation (训练 great).** Forming the general's family-name glyph from soldiers needs a glyph-to-slots table for ~500 surnames (or the 60 most common plus the faction emblem fallback); the emblem-only fallback is cheap and may be enough.
5. **Voices.** The cue ids (`cmd.*`) are proposed here; the voices task (request 12) owns the lines and languages and may merge cues.
6. **Leitmotif density.** 「霸业」 appears in four great tiers; if the music task gives rulers their own suites (request 7), the quote could become each suite's own head motif instead.
7. **Harvest montage length.** Four cities × 1.1 s plus the summary is fine for small realms; for a 30-city empire the summary card alone may be better from 12 cities up.

---

## 16. Sources

Historical details used for vignettes, icons and city props (see `CITY-VIEW.md` §15 for the building research):

- Ox-drawn seed drill (耧车) and its crew: [Louche — Wikipedia](https://en.wikipedia.org/wiki/Louche); [Science and Technology Daily, "Louche: Ancestor of the Modern Seed Drill"](https://www.stdaily.com/web/English/2023-09/12/content_1935388.html); heavy plough with two oxen: [History Collection, Han dynasty influences](https://historycollection.com/15-ways-the-han-dynasty-influenced-modern-china/13/).
- Water-powered bellows of 杜诗 (Nanyang, 31 AD), used for the 工坊 S3 stage and the 水排 prop: [Du Shi — Wikipedia](https://en.wikipedia.org/wiki/Du_Shi); [Wang Zhen (Nong Shu illustration) — Wikipedia](https://en.wikipedia.org/wiki/Wang_Zhen_(inventor)); [China Science and Technology Museum, 水排](https://www.cdstm.cn/theme/nytd/nyjx/201702/t20170215_462033.html).
- 翻车 (square-pallet chain pump) attributed to 毕岚 in the late Eastern Han, improved by 马钧: [中国数字科技馆 · 翻车](https://www.cdstm.cn/theme/nytd/nyjx/201407/t20140710_462032.html).
- Tiger tally 虎符 (two halves, troop mobilization): [Fu (tally) — Wikipedia](https://en.wikipedia.org/wiki/Fu_(tally)); [The Met, Tally in the shape of a tiger](https://www.metmuseum.org/art/collection/search/61524); [Hunan Museum, tiger-shaped tally](https://www.hnmuseum.com/en/gallery/node/2932/0).
- Seal and ribbon 印绶 ranks: [Google Arts & Culture, the gold seal 漢委奴國王](https://artsandculture.google.com/asset/the-gold-seal%E2%80%9Ckan-no-wa-no-na-no-kokuou%E2%80%9D-han-dynasty/-gG-6FNFVDNmvQ); [Chinese Notes, 绶](https://chinesenotes.com/words/25862.html).
- Han military review 都试 (autumn, archery, riding, formations): [Military of the Han dynasty — Wikipedia](https://en.wikipedia.org/wiki/Military_of_the_Han_dynasty); [Wubei zhi excerpt, sourcelibrary.org](https://sourcelibrary.org/book/wubei-zhi-vol-31/page/6992cdde81c9d7efb84d07ca).
- Han granary-tower and watchtower tomb models (仓楼, 望楼) behind the granary and drum-tower props: [The Met, Granary Tower](https://www.metmuseum.org/art/collection/search/52010); [Toledo Museum, granary mingqi](https://emuseum.toledomuseum.org/objects/60543/mingqi-spirit-good-in-the-form-of-a-granary); [The Met, Multistoried Watchtower with Front Court](https://metmuseum.org/art/collection/search/49520); [Freer Gallery, tomb model of a watch tower](https://asia.si.edu/explore-art-culture/collections/search/edanmdm:fsg_F1907.68a-d).
- Music: the pentatonic modes 宫商角徵羽 and the regional modes follow the existing engine's documentation (`js/music.js`, `js/music-world.js` headers: 都节 In scale, Bhairav, Shur, Hijaz, Dorian).
