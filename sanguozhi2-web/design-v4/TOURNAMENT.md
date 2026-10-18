# 三国志-霸王的世界 · 第四版武林大会设计（TOURNAMENT）

Player request 14 of v4: **on the first month of each year a 武林大会. Each ruler sends up to 5 generals. A grand, epic stadium that features major historical buildings and places. The top 3 win prizes: 1st a random rare weapon / armour / skill / equipment, 2nd a lower tier, 3rd lower still; all three get gold.** This spec covers the event end to end: invitations and the host, who is sent (AI rules and the player's selection screen), the format (draw size, byes, seeding, rounds), how matches are fought (the v4 duel, played, watched or simulated), the rules (no deaths, light injuries), prizes and their weight in the item economy, the ceremony and presentation, and the **万国擂台**, a stadium built from landmarks of the period across the cultures of the game.

Read `DESIGN-V2.md` §1 (common rules), `DESIGN-V3.md` §3–§4, `CONTRACT.md`, and the sibling v4 specs `UI-V4.md` (§12: the tournament's screen layouts), `COMBAT-V4.md` (the duel), `ITEMS.md` (§8.9 prize pools, §2.4 `duelWar`), `ENGAGEMENT.md` (§3.2 prestige, §5 calendar, §13 文会 and hooks), `TITLES.md`, `AUDIO-V4.md` (§A3 fanfares, §B cues) and `CITY-VIEW.md` (§3 culture families, §9 budgets) first. **`INTEGRATION.md`** wins over this file for everything that crosses a system boundary (prize pools, the save and its migration order, the month pipeline, the shared `gen.injury`, registries and the CPU budgets). Where this file conflicts with the other specs, this file wins for the systems it owns: the tournament's schedule, host, entry, format, match rules, injuries, reward amounts, records, the stadium and the tournament's presentation and music. The others win for their systems: ITEMS for which items exist and how they behave, COMBAT-V4 for duel mechanics, ENGAGEMENT for prestige and the calendar, AUDIO-V4 for voices and suites. Platform order: browser first (this repo), then the Unity port (§15). Every visual assumes the v4 art direction from the art pilot (`js/lowpoly.js`, `js/lp-pilot.js`): Lanternvale-style stylized low-poly, cozy Ghibli-warm palette, flat-shaded props and buildings with per-face brightness jitter, soft-lump foliage with the warm-top / cool-teal ramp, ink outlines on buildings, props and figures, soft painted light with warm lantern pools at night. Presentation uses paper, lacquer, brush strokes, ink splashes, gold leaf and vermillion seals, never neon.

Status: design spec only. No game code was changed and nothing was committed. Supporting files in `design-v4/tourney/` (all read-only on the game data):

| File | What it is |
|---|---|
| `tourney/tourney-proto.js` | Reference prototype of the rules in §3–§7: AI entry, seed score, the draw algorithm (bands, byes, faction separation), bracket play with the v3 duel sim, prize gold, campaign runs. Source of every measured number here. Modes: `entrants`, `single [N] [scen]`, `campaign [seeds] [years] [scen]` (env `AWAY=0`, `SEED0`), `bracket [N]` (self-check), `player [N] [scen] [keys]` |
| `tourney/arena-music.js` | The arena music as real `SG.Music` data: 「群英会」 `tourney`, 「月下决」 `tourney-final`, `tourney-ko` (§9.10). `node design-v4/tourney/arena-music.js` validates it (ALL OK) |
| `tourney/render-arena.mjs`, `tourney/render-stats.json`, `tourney/samples/*.opus` | Offline render in Chromium (`tests/music.html` path), loudness stats and listening samples |
| `tourney/stadium-blockout.js`, `.html` | Block-out of the stadium in `SG.LP` (§8): layout, the 16 slots, rotating ring, crowd pegs, banners, lanterns. `node design-v4/tourney/stadium-blockout.js` prints triangle counts; the page renders the shots |
| `tourney/shoot-stadium.mjs`, `tourney/shots/*.png`, `shots/stats.json` | Screenshots (aerial, duel views with three backdrops including phone 844×390, walk-on, night, ceremony) and measured draw calls / drawn triangles |

---

## 0. Key numbers at a glance

| Topic | Rule |
|---|---|
| When | Month 1 of every year from **191** (start year + 1). The host is announced in the month-12 news; the tournament card of month 1 comes after the 正旦 year review and is **outside ENGAGEMENT's R1 card budget** (INTEGRATION §6.5). The player may defer it (「稍后」) and start it any time in month 1; 「结束本月」 runs it first if it is still pending |
| Host | 191: the holder of 洛阳 (classic) / 罗马 (world). Then the eligible living faction with the highest prestige (ENGAGEMENT §3.2), never the same faction within 3 years; its capital is the host city. Host: prestige +20, capital order +5, home banners, its family's gate on the stadium |
| Entry | Every living faction may send **up to 5** of its generals with base 武力 ≥ 60, not captive, not injured. AI: base 武力 ≥ **72** (classic) / **75** (world), best 5 by seed score; a courtesy entry (best general, 武力 ≥ 65) when none qualifies; AI rulers fight only at 武力 ≥ 85 (or 康茂德). The player picks on the 英雄帖 card (recommended five pre-filled) |
| Field (measured, 191) | Classic **46** entrants from 17 / 18 factions → 64 draw, 18 byes. World **71** from 44 / 70 factions → 128 draw, 57 byes (a 7-match play-in) |
| Format | Single elimination. Draw S = 2^⌈log₂ N⌉ (8 … 128); the top S − N seeds get byes; S/4 seeds placed in bands (1–2, 3–4, 5–8, 9–16, 17–32); generals of one faction kept apart; final **best of three**; a third-place match |
| Match | The v4 duel (COMBAT-V4) on the stadium's stage: HP 100, 60 s, KO or time; the higher seed is side a and wins exact ties. Gear (`duelWar`), titles, strings, crits and parries all apply. **No deaths, no capture.** A KO loss injures with 6–22% (−5 effective 武力 for 2 months) |
| Who controls | Each player entrant: 亲自 (play), 观战 (the AI fights him; the player watches) or 自动 (simulated). AI vs AI: simulated in ≤ 15 ms per duel, sliced; any match can be watched and gives the same result (per-match seeded RNG) |
| Prizes | **1st**: a random rare item (75%) or a 武林秘传 rare skill (25%), 400 gold, prestige +50, fame +1, exp +80, loyalty +6, 「武林魁首」 for a year. **2nd**: an uncommon item (choose 1 of 3) or a tier-2 秘传 (15%), 200 gold, +25, exp +50. **3rd**: a common item of his culture (choose 1 of 3), 100 gold, +12, exp +35. **All entrants**: exp +20, +12 per match won. **Three titles** → 金刚杵 (legendary), both scenarios. 赫拉克勒斯之棍 is world-only (slay 康茂德); the classic tournament awards no five-title relic and never a ruler-unique (INTEGRATION §7.2). Numbering: 191 = 第一届, **193 = 第三届** |
| Without fighting | 竞猜 (predict the champion at the last 16: +60 gold, prestige +5), 赞助 (100 / 300 / 800 gold → prestige +3 / +9 / +24), watch any match |
| Stadium | **万国擂台**: 18 × 9 m stone stage in a 23 m sand arena; stands to r 41 m; 16 slots of 22.5°: 12 culture-family sectors (CITY-VIEW §3), each fronted by a landmark **facade** (≤ 7 × 9 m, recognisable below 6 m: inside the duel camera's frame) and crowned by a landmark seen from the air, plus the host's 主门 (default: Han 阙 and the 平乐观 华盖), the **万国之门** (Persepolis' Gate of All Nations — the entrants' gate), the 裁判台 and the 奖台. The ring rotates so the backdrop behind each duel is the fitting sector |
| Budgets | Duel view ≤ 110 k triangles drawn (incl. ink shells, crowd) and ≤ 60 draw calls on desktop; ≤ 70 k / ≤ 45 on phones; aerial ≤ 250 k / ≤ 90. Crowd 2 400 / 900 instanced pegs. Build ≤ 150 ms in ≤ 8 ms slices while the 英雄帖 card is open. Block-out measured: duel view 56–69 k drawn, aerial 145 k |
| Balance (measured, v3 duel sim) | Classic, 200 tournaments: 吕布 29%, 关羽 22.5%, 张飞 15.5%, 马超 7%, 16 different champions; the top seed wins 29%, a top-4 seed 74%; 25% of duels are won by the lower `duelWar`. 48 all-AI classic campaigns × 15 years: factions alive at the end 4.56 with / 4.44 without; prize gold 1.7% of all factions' gross income (world 0.3%); the 金刚杵 is won in 48 / 48 runs, median after 9 tournaments. All v4 on (INTEGRATION §9): every gate passes; 24.9 entrants per classic tournament (the average over 25 years as realms fall), 2.3 injuries |
| Time | An AI-only tournament computes in 0.28 s (classic) / 0.44 s (world) with the v3 sim (v4 ≤ 0.7 / 1.1 s, sliced; exempt from the 60 ms per-faction month cap, INTEGRATION §6.6), shown in ≤ 60 s. A player with five strong entrants plays a median 9–15 duels (8–12 min); 「全部自动」 takes ≤ 90 s with the ceremony |
| Music | Arena piece 「群英会」 (♩112, the 12 culture families' lead instruments take turns in a 24-bar 万国轮奏), the night final 「月下决」, a KO hit; entrants walk on to their ruler's `fanfare`; the champion's ruler's `victory` |

---

## 1. Goals and the shape of the event

### 1.1 Goals

1. **A yearly high point that is not a war.** The one moment of the year when every realm's champions meet in one place. It rewards building a court of fighters, buying gear and practising the duel, and it gives peaceful players (request 13) prestige, items and exp without a battle.
2. **The duel game at its best.** Fights are the v4 duel (COMBAT-V4) with no army context: pure skill, a crowd, a bracket and a story ("关羽 falls to an unseeded Sarmatian").
3. **The world in one picture.** The stadium shows the game's whole world: a Han gate, the Colosseum, Persepolis' gate, Petra, Sanchi, Meroë, the Pharos, Yoshinogari, a 高句丽 wall and the rest, each with its people in the stands.
4. **Fair and short.** It is never a chore. Everything has 自动; the AI-only resolution is quick. Watching never changes a result. Prizes are worth fighting for but never decide a war.

### 1.2 A tournament in twelve beats (player view)

1. **Month 12 news**: 「明年正月，天下英雄会于洛阳。」 with the host's seal. The 年历 ribbon (ENGAGEMENT §16.1) shows crossed spears on month 1.
2. **Month 1, after the 正旦 year review**: the **英雄帖** announcement card, then the entry screen (§4.3; layouts UI-V4 §12.1–12.2): five banners pre-filled with the recommended entrants, each with 亲自 / 观战 / 自动, plus 竞猜 and 赞助. 「报名」, 「稍后」 (do the month's shopping and training first; an agenda slip waits) or 「不参加」.
3. **Opening ceremony** (≤ 14 s, skippable): aerial sweep over the stadium, the 三通鼓 drum rolls, banners of every entrant faction, the host ruler's opening line, the 抽签 (drawing of lots) builds the bracket (§9.1–9.2).
4. **Bracket screen** (签表, UI-V4 §12.3): the round's matches; the player's entrants highlighted; ★ featured AI matches can be marked 观战 (§9.3).
5. **The round plays**: AI matches resolve as a brisk montage (brush strokes and 胜 seals, ~6 per second); the player's matches and the watched ones play in slot order.
6. **A match**: walk-on through the tunnels with the ruler's fanfare and a cry (§9.4); the face-off under UI-V4's match card 对阵; the duel on the stage with the crowd reacting; KO, crowd roar, result card.
7. Repeat for each round (预赛 / 首轮 … 八强, 四强).
8. **Dusk**: the semifinals at golden hour; lanterns are lit.
9. **The final at night**, best of three, to 「月下决」 (§9.7).
10. **The third-place match** before the final (it keeps the final last).
11. **Award ceremony** (≤ 30 s): the podium; the host ruler presents the prizes; the rare item's full-screen reveal; petals; the champion's ruler's victory music; a seal stamps the 英雄榜 (§9.8).
12. **Results card**: every reward line for the player's faction, records broken, then the month banner. The AI's month begins as usual.

---

## 2. Files, modules and ownership

| File | Status | Content |
|---|---|---|
| `js/tourney-data.js` | new | `SG.TourneyData` (§10.1): all numbers of this spec, the round names, slot table, host-gate map, voice/SFX ids. Plain data |
| `js/tourney.js` | new | `SG.Tourney` (§10.2): host, eligibility, AI entry, seed score, draw, rounds, match resolution (sync and sliced), injuries, prizes, records, save fields. Pure logic: no `THREE`, no DOM; Node-testable like `model.js` |
| `js/tourney-flow.js` | new | `SG.TourneyFlow`: the orchestration: `offer()` (announcement card), `run(mode)` (opening → rounds → matches → ceremony → results), the montage, match sequencing (walk-on → match card → `DuelGame.play` → result), prediction and sponsorship, the 3D ceremony beats, AI-only presentation |
| `js/tourney-screen.js` | new (UI-V4 task) | `SG.TourneyScreen`: the 2D screens of UI-V4 §12 (announcement card, entry, bracket with next-match card, match card, duel ribbon, podium, 英雄榜) over `SG.Tourney`; the flow calls them |
| `js/stadium.js` | new | `SG.Stadium` (§10.3): the stadium scene (ring, core, crowd, banners, lanterns, mood), `face(slot)`, camera rigs (opening, walk-on, ceremony), the Arena-compatible duel stage for `DuelGame.play` |
| `js/stadium-kit.js` | new | `SG.StadiumKit`: the 16 slot recipes (facade + crown + shell) and 6 host-gate variants in `SG.LP.MeshBuilder`, at the art pilot's quality (`SG.LPPilot.gateTower` is the bar). Cached in `SG.LP.MeshCache` |
| `js/music-tourney.js` | new | the pieces of `design-v4/tourney/arena-music.js` |
| `tests/tourney.js` | new | Node: rules, draw, determinism, AI, prizes, save, balance runs (§14.1) |
| `tests/tourney.html` | new | bracket / card / ceremony UI against scripted states; phone and desktop layouts |
| `tests/stadium.html` | new | the stadium: every slot, every host gate, shots, budgets, fps (from `design-v4/tourney/stadium-blockout.html`) |
| `js/core.js` | modified | `SG.Random.swap(fn) → prev`, `SG.Random.restore(prev)` (§5.5) |
| `js/duel-game.js` | modified | `opts.mode = 'tourney'`, `opts.stage` (Arena-compatible stage), `opts.label`, tie rule, skip = finish headless, `simulateAsync` (§10.3) |
| `js/model.js` | modified | save fields (§10.4), `Progress.gain(gen, e, 'tourney')` (no change to the function) |
| `js/strategy-ai.js` | modified | `endMonth`: announce at 11 → 12, injury expiry, AI-only fallback when the screen is absent (Node) |
| `js/strategy-screen.js` | modified | month loop hook, 英雄榜 entry, news lines, injured badge in lists |
| `js/items.js` (ITEMS task) | modified | `SG.Items.tourneyPrize(gen, place, rng)`, `offers(gen, rarity, n, rng)`, `eff()` / `duelWar()` subtract injuries |
| `js/specials.js` | modified | `known(gen)` appends `gen.bonusSpecial` |
| `js/audio.js`, voice data (AUDIO-V4 task) | modified | the cues and SFX of §9.10 |
| `index.html` | modified | load `tourney-data.js`, `tourney.js` after `model.js` (and after `items.js`); `stadium-kit.js`, `stadium.js`, `music-tourney.js`, `tourney-screen.js`, `tourney-flow.js` after `lp-pilot.js` / `duel-game.js` |

Parallel work rule (DESIGN-V2 §3): each task edits only its own files and lists the rest under INTEGRATION (§12).

---

## 3. Calendar, host and rotation

### 3.1 Calendar

| When | What |
|---|---|
| `StrategyAI.endMonth` that turns month 11 into 12 | `SG.Tourney.announce(G)`: picks next year's host (§3.2), writes `G.tourney.next = { year, host, city }`, pushes the news line 「明年正月，天下英雄会于{city}。{host}主办第{n}届武林大会。」 (for every faction; the player's news shows it with the host's seal) |
| Month 12 | The 年历 ribbon shows the tournament; the 英雄榜 page shows the coming host. The player can prepare: train, buy gear (ITEMS shops), 切磋 (ENGAGEMENT §13.4), the 演武 yard (COMBAT-V4 §12.4) |
| `endMonth` that turns 12 into 1 | Injuries from last year are long gone. If the announced host is dead or lost its capital, re-pick and add 「会址改于{city}。」 |
| Month 1, after the year review | The tournament card. It is **outside ENGAGEMENT's R1 budget** (INTEGRATION §6.5): it never pushes another scheduled card to month 2, and AI tribute demands and 归附 offers are never generated for month 1, so January shows at most this card and one other. 「报名」 runs the tournament at once; 「稍后」 leaves an agenda slip (UI-V4 §12.1) so the player can first use the month (buy gear, train, move generals); pressing 「结束本月」 with the tournament still pending runs it before the AI's turn. Either way it resolves completely within month 1, before `runAI`. Then `G.tourney.held = year`, `G.tourney.count + 1`, save |
| First tournament | `FIRST_YEAR = StartYear + 1` = **191** in both scenarios (announced in the 190-12 news). The number in every text is `n = G.tourney.count + 1` (191 第一届, 192 第二届, **193 第三届**; INTEGRATION §6.5). The new-game intro adds one line: 「每年正月，天下诸侯遣将会于擂台，是为武林大会。」 |

A browser closed in the middle of a tournament reloads the month-1 save made before it (§3.4): the tournament starts again from the draw. All AI results replay identically (§5.5); only the player's own matches are fought again.

### 3.2 Host selection

```
eligible(f) = f.alive && cityCount(f) ≥ 1 && capital(f) exists           // capital = the ruler's city
            && cityLevel(capital) ≥ 2                                     // the rules table SG.Balance.CityLevel (INTEGRATION §12; 县城 or better), never the view module
            && f ∉ G.tourney.hosts (the last two hosts), unless fewer than 3 factions are eligible
host = argmax over eligible f of  prestige(f)                             // ENGAGEMENT §3.2
       tie-break: cityCount(f), then effFame(f), then lower id
       without ENGAGEMENT (Engage.enabled = false): fame × 10 + cityCount × 5, then lower id
hostCity = capital(host)
```

**The first tournament (191)** is held where the period's own games were: in the classic scenario by the faction that holds **洛阳** (the 平乐观, where 灵帝 held his great review of 188, §8.1; 董卓 at the start), in the world scenario by the faction that holds **罗马** (康茂德's Rome, the year before his own games of 192). If that faction is dead or the city is unowned at the announcement, the normal rule applies (`FIRST_HOST_CITY`, §10.1). From the second tournament on prestige decides, so the host rotates among the leading realms; a peaceful player who gathers prestige hosts too. With equal prestige the tie-break favours the bigger realm (at the game start that would be 刘表 at 襄阳 in the classic scenario and 罗马 in the world).

### 3.3 What hosting gives

| Effect | Amount |
|---|---|
| Prestige | +20 (ENGAGEMENT §3.2 table, same as hosting the 文会) |
| Capital order (民心) | +5 (ENGAGEMENT §3.1) |
| Seeding | none: the home crowd cheers louder (crowd excitement ×1.3 for the host's entrants) |
| Stadium | the 主门 wears the host family's gate (§8.6); the host's colours on all outer-ring poles above the gate; the host ruler in the royal box; the host's name on the banners and the title card: 「第{n}届 武林大会 · {city}」 |
| Ceremony | the host ruler opens the games and presents the prizes (voice §9.10) |
| City view | the host city shows the festival dressing of its family (CITY-VIEW §6.6: banners, lantern strings) in months 12 and 1 |
| Cost | none. Hosting is an honour that comes with prestige; it cannot be bought (open question Q3) |

The title card's subtitle follows the host family: hua 「平乐观大会」 when the host holds 洛阳, otherwise 「武林大会」; roman 「LVDI」; every other family shows the host city's name. The UI title is always 武林大会.

### 3.4 Month flow (strategy-screen.js `run()`)

```
… endMonth → news (month 1: the year review replaces it) → G.save()
if (SG.Tourney && SG.Tourney.due(G())) {          // month 1, year ≥ FIRST_YEAR, G.tourney.held < year
  const r = await SG.TourneyFlow.offer();          // announcement card: 'enter' | 'later' | 'decline'
  if (r !== 'later') { await SG.TourneyFlow.run(r); G().save(); }     // 'decline' = AI-only with the results card
}
UI().banner(year + '年 ' + month + '月', '令牌 …')
…
// 「结束本月」 pressed (before runAI):
if (SG.Tourney && SG.Tourney.due(G())) { await SG.TourneyFlow.run('enter'); G().save(); }     // the agenda slip's tournament
```

In Node (tests, all-AI runs) and whenever `SG.TourneyFlow` is absent, `StrategyAI.endMonth` calls `SG.Tourney.autoAll(G)` at the start of month 1 (no player entrants), so the AI world gets its tournaments without a screen.

---

## 4. Entrants

### 4.1 Eligibility (player and AI)

A general may enter when all hold:

- he serves the faction (`gen.faction === f`), is alive and is not a captive (`Conquest.isCaptive`);
- **base 武力 ≥ 60** (`TourneyData.MIN_WAR`; gear does not count, as for titles and slots);
- he is not injured (`G.monthIndex < gen.injury.until` blocks; injuries last 2 months, so this matters only for edge cases);
- he is not away leading a 商队 (caravan) or a 探索 (expedition) (ENGAGEMENT §9.1, §12.1);
- rulers: the player's ruler may enter at 武力 ≥ 60 (主公亲自下场); AI rulers only at 武力 ≥ 85 or when listed in `RULER_FIGHTERS` (康茂德, who fought as a secutor in the arena: Cassius Dio 73(72).17–20; Herodian 1.15). Nobody dies in the tournament (§6.2), so a ruler risks only an injury.

The entry screen shows every own general with a reason when he cannot enter: 「武力不足 60」, 「负伤未愈」, 「身陷敌营」, 「率商队在外」, 「探索未归」.

### 4.2 AI selection

```
seedScore(gen) = duelWar(gen)                       // ITEMS §2.4: base 武力 + min(3, ⌊gear 武力 / 3⌋), minus injury (§6.3; INTEGRATION §8.3)
               + 0.6 × level                        // DESIGN-V3 levels 1–10
               + 1.5 × min(3, championships(gen))   // past titles
               + 10 × [gen is the reigning champion]
               + 5 × [his faction has a completed 讲武堂 / 演武场 project]   // ENGAGEMENT §8.2 'drill'
aiPick(f):
  cands = eligible generals of f (§4.1, AI ruler rule), sorted by seedScore desc, then id
  pick  = cands with base 武力 ≥ T  (T = 72 classic, 75 world), first 5
  if pick is empty and cands[0] has 武力 ≥ 65: pick = [cands[0]]        // courtesy entry: every realm shows its banner
  ENGAGEMENT §6.3 「求战心切」 card answered 「荐其出战」 → that general is added first (if eligible)
```

No `SG.Random` draws; deterministic. The AI never declines and never keeps generals home: entering costs nothing but the injury risk (§6.3). A rule that kept entrants "away" for month 1 was tried and rejected (§14.4: it slowed AI conquest by 17%).

### 4.3 Player selection: the 英雄帖 card and the entry screen

The scheduled card of month 1 (ENGAGEMENT R1: one scheduled card; answering costs no token). **Layouts are UI-V4's** (§12.1 announcement card, §12.2 entry screen with the five faction banners, `mockups/10-tourney.html?view=entry`); this section fixes their content and behaviour. The sketch below lists the content on one card for brevity.

```
┌──────────────── 英雄帖 · 第3届武林大会 ────────────────┐   vermillion paper scroll, gold-leaf edge, host's seal
│ [stadium vignette: painted miniature of 万国擂台]       │   ink-wash thumbnail rendered from SG.Stadium (cached)
│ 会址 洛阳（董卓主办）· 应邀 18 家 · 预计 46 人           │
│ 卫冕：吕布（二届魁首）       ▸ 英雄榜                    │
│ ── 我方出赛 ──                                          │
│ [肖像] 关羽 武98 Lv6 ●◆▲… 预计种子 2   (亲自|观战|自动) │   five slots; the mode toggle is a 3-way lacquer switch
│ [肖像] 张飞 武99 Lv5 …        预计种子 1   (亲自|观战|自动) │
│ [＋ 选将] [＋] [＋]                                      │
│ ── 不上擂台也能参与 ──                                  │
│ 竞猜 ▸（十六强时开放）   赞助 ▸ 100 / 300 / 800 金      │
│ [一键推荐]  [不参加]  [稍后]  [全部自动]  [报名 ▶]       │
└─────────────────────────────────────────────────────────┘
```

- **Pre-filled** with `defaultPick(player)` = the same rule as the AI (§4.2) but with T = 60, so a small court still fills its slots. The ENGAGEMENT 求战心切 general is pre-filled first and wears a tag 「求战心切」.
- **Picker** (「＋ 选将」): a grid of all own generals (portrait, 武, `duelWar` with gear in green, level, the 8 string seals of COMBAT-V4 §12.1, gear rarity frames, office name from TITLES, the injury bandage). Sorted by seed score. Tap toggles. Ineligible tiles greyed with the reason (§4.1). Phone: two columns, 64 px rows, touch targets ≥ 44 px.
- **Mode per entrant**: 亲自 (the player controls him in every match), 观战 (the AI fights; the match is rendered), 自动 (simulated; only the result is shown). Default: 亲自 for the top two by seed score, 自动 for the others; the choice is remembered per general in `localStorage` (`sanguozhi2_tourney_modes`, try/catch). It can be changed on the bracket screen before each round.
- **预计种子**: the seed this general would get if the field were the AI's picks plus the player's current choice (recomputed on toggle; cheap).
- 「不参加」: no entrants. The tournament still runs AI-only (§5.4); the results card follows, with 「观看决赛」. 「稍后」: an agenda slip; the tournament waits until the player starts it or ends the month (§3.1).
- 「全部自动」: all player entrants become 自动 and every presentation beat is shortened to the minimum (opening 2 s, montage, ceremony 8 s).
- **Estimates** for the screen (UI-V4's recommendation stars, the next-match card's win bar): `SG.Tourney.estimate(G, gen, draft)` and `winChance(a, b)` (§10.2). The duel's measured win curve (v3 sim, 160 duels per point, sides alternated) is logistic in the `duelWar` difference Δ: **p(a) ≈ 1 / (1 + e^(−Δ/6))** (fit RMSE 0.047; Δ 3 → 62–71%, Δ 10 → 77–83%, Δ 15 → 89–99%). Stars: ★★★ when the estimated chance to reach the 八强 is ≥ 50%, ★★ ≥ 20%, ★ ≥ 5%.
- Opening the card starts the stadium build in idle slices (§8.11), so 「报名」 never waits.

### 4.4 Field sizes (measured at 191, `tourney-proto.js entrants`)

| Scenario | Entrants | Factions sending (1 / 2 / 3 / 4 / 5) | Draw | Byes | Rounds |
|---|---|---|---|---|---|
| Classic | 46 (17 of 18 factions) | 6 / 4 / 1 / 1 / 5 | 64 | 18 | 首轮 (14) · 三十二强 · 十六强 · 八强 · 四强 · 决赛 (BO3) + 季军战 |
| World | 71 (44 of 70) | 35 / 2 / 1 / 1 / 5 | 128 | 57 | 预赛 (7) · 首轮 (64 players) · 三十二强 · … · 决赛 (BO3) + 季军战 |

Top seeds (classic): 1 吕布 104 · 2 张飞 103 · 3 关羽 102 · 4 马超 100 · 5 赵云 99 · 6 孙策 98 · 7 张辽 94 · 8 文丑 94 (numbers are `duelWar` with signature gear, ITEMS §8.4). The world adds 兀突骨, 迦梨迦罗 (92) and the strongest of Korea, the steppe, India and Rome further down.

The player's five entrants add up to 5 to these fields; the draw size changes only when N crosses a power of two.

---

## 5. Format

### 5.1 Draw size, seeds, byes

```
N  = number of entrants (if N < 4: no tournament this year; news 「应者寥寥，大会停办」)
S  = max(8, 2^⌈log₂ N⌉), at most 128; if N > 128 the AI entrants with the lowest seed score are dropped
     (never the player's) until N = 128 — measured fields stay far below (§4.4)
K  = S / 4 seeds (2 in a draw of 8)
rank the entrants by seedScore (§4.2) desc, then id; ranks 1..N
standard order: order(2) = [1, 2]; order(2n) = for each x of order(n): x, 2n+1−x      // e.g. order(8) = 1 8 4 5 2 7 3 6
pos(r) = the slot index of rank r in order(S)                                          // slots 0..S−1, pairs (0,1), (2,3) … play round 1
```

1. Seeds 1 and 2 go to `pos(1)`, `pos(2)` (opposite halves: they can meet only in the final).
2. Each further band of seeds — 3–4, 5–8, 9–16, 17–32 — is shuffled (`rng('draw')`) among the band's standard positions; of up to 24 shuffles the one with the lowest **faction collision cost** is kept. This is the tennis method: 3 and 4 land in the two other quarters, 5–8 in the remaining eighths.
3. **Byes**: B = S − N go to ranks 1..B. A seed's bye is the slot paired with his actual slot. If B > K, ranks K+1..B are placed at random into fully empty pairs, each with a bye opposite.
4. The other unseeded entrants fill the remaining slots in random order.
5. **Faction separation**: 400 hill-climb swaps between unseeded slots (never touching byes or seeds) accept a swap only when the cost drops.

```
meetRound(i, j) = 1 + ⌊log₂(i xor j)⌋                          // the round in which slots i and j can first meet
cost = Σ over pairs of same-faction entrants of W[meetRound],   W = [—, 16, 8, 4, 2, 0, 0 …]
```

So two generals of one faction meet in round 1 only when nothing else is possible, and rarely before the quarterfinals. Measured: a residual cost of 12 in both opening fields (for example one second-round and one third-round same-faction pairing). `tourney-proto.js bracket 400`: 400 random fields of 5–128 entrants, ALL OK (deterministic for a seed, everyone placed, byes only opposite the top seeds, never bye against bye, seeds 1 and 2 only in the final, the top four seeds in four different quarters).

### 5.2 Rounds and names

Names follow UI-V4 §12.3's round tabs:

| Round | Name | Notes |
|---|---|---|
| a round 1 with fewer than S/8 matches | 预赛 | the play-in: most entrants have byes (the world's 7 matches in a 128 draw) |
| the first round with S/2 or more players in it | 首轮 | classic: round 1 of the 64 draw (14 matches, 18 byes) |
| 32 players | 三十二强 | |
| 16 | 十六强 | 竞猜 opens here (§7.6) |
| 8 | 八强 | golden hour begins |
| 4 | 四强 | the semifinals; dusk; the two losers play the 季军战 |
| 2 | 决赛 | night, **best of three** (三局两胜); each game a new duel at full HP |
| — | 季军战 | best of one, played before the final |

Sides: the higher seed (lower rank) is always side a (left on screen) and wins exact ties at time-out, matching `BattleModel.duel` and `DuelGame`'s "challenger wins ties". The player's general is shown on the left only when he is the higher seed; the HUD marks him in any case.

### 5.3 How a match is resolved

| Match | Resolution | Presentation |
|---|---|---|
| player entrant (亲自) vs anyone | `DuelGame.play({ mode: 'tourney', playerSide })` | full (walk-on, duel, result) |
| player entrant (观战) or a ★ AI match marked 观战 | `DuelGame.play({ mode: 'tourney', playerSide: null })`, skippable | full |
| player entrant (自动) | `simMatch` | montage line + result card for the player's general |
| AI vs AI | `simMatch` (`DuelGame.simulateAsync`, ≤ 6 ms per slice) | montage line |
| player entrant vs player entrant | the player picks whom to control (default: the higher seed), or 观战 / 自动 | full |
| final (each game) | as above; AI finals are always offered as 观战 | night |

**Order**: within a round, matches are resolved strictly in slot order: AI matches instantly, then the montage pauses at the next player or watched match. The order matters because the AI remembers what it saw (`aiKnow`, §6.4); a fixed order keeps results reproducible.

**Watching never changes a result.** Every match draws from its own seeded stream (§5.5). A watched AI match therefore replays exactly what `simulate` would produce: the rendered loop runs the same fixed-step `Sim` with the same AI, and the view draws only from `SG.SeededRandom` (DESIGN-V2 §1). In tourney mode `DuelScreen.doSkip` finishes the fight headless (`sim.runToEnd()`) instead of v3's "skip = current HP" verdict, so a skipped match also ends with the simulated result. Test: `tests/tourney.html` renders 20 AI matches and compares with `simulate` (§14.3).

### 5.4 AI-only tournaments

When the player declines, has no eligible general, or the screen is absent, `SG.Tourney.autoAll(G)` resolves the whole bracket: 46–71 duels at ≤ 15 ms (v4 target, COMBAT-V4 §15.2 (14)) in ≤ 6 ms slices behind a short presentation: opening (2 s), a bracket that fills round by round (≈ 4 s per round), the final offered as 观战, the ceremony (8 s), the results card. Without the final it takes ≤ 60 s; skipping it all ≤ 10 s.

### 5.5 Determinism and the random-number contract

- **Duel speed**: settings row 48 (单挑速度) does not apply to matches the player fights; they always run at 100 % (UI-V4 §7, INTEGRATION §8.5). Watched matches may be slowed.
- Every tournament decision draws from its own streams: `rng(tag, …k) = SG.SeededRandom(hash32(G.seed, year, 'tourney', tag, …k))`. Tags: `host` (unused: host choice is deterministic), `draw`, `match:<id>:<game>`, `injury:<id>`, `prize:<place>`, `offer:<place>`, `skill:<place>`, `bond`.
- The duel engine draws from `SG.Random`. For every match (simulated, watched or played) the screen calls `prev = SG.Random.swap(mulberry32(hash32(G.seed, year, 'duel', id, game)))` before and `SG.Random.restore(prev)` after. **The main stream is therefore exactly where it was before the tournament**, whatever the player watched, played or skipped. `SG.Random.swap` / `restore` are 6 lines in `core.js` (the `rng` closure is swapped and returned).
- Nothing else may draw from `SG.Random` while a rendered tournament duel runs: the stadium, crowd, petals and camera use `SG.SeededRandom` only.
- With `SG.Tourney.enabled = false` no tournament is announced or held, and every v3 / v4 output is identical (the test of §14.1 (10)).

### 5.6 Time budget

| Item | Value |
|---|---|
| Compute, one AI duel | v3 sim 5.8–6.1 ms (Node, measured); v4 target ≤ 15 ms (COMBAT-V4) |
| Compute, a whole AI tournament | v3: 277 ms classic, 443 ms world; v4 ≤ 0.7 / 1.1 s, sliced at ≤ 6 ms per frame (no frame over 8 ms of JS). **Exempt from the 60 ms per-faction month cap** of `tests/world.js` (INTEGRATION §6.6, §13); the January month transition keeps the ≤ 8 s total |
| Average AI duel length | 28.1 s (classic) / 28.5 s (world); 0% time-outs (v3); COMBAT-V4 target 20–40 s |
| A played match incl. walk-on and result | ≈ 40 s (4 s walk-on, 1.2 s face-off, ~30 s duel, 3–4 s result) |
| Player with five strong entrants | median **9–15 duels** a tournament (classic 董卓: 12, max 22; 孙坚: 11; 袁绍: 10; 刘备 with two entrants: 8; world 董卓: 15) → 8–12 minutes with everything played |
| AI-only, presented | ≤ 60 s (+ the final if watched) |

---

## 6. Rules of the match

### 6.1 Duel parameters (`DuelGame.play({ mode: 'tourney' })`)

| Rule | Tourney mode |
|---|---|
| HP, timer, damage | as COMBAT-V4: HP 100 (+ gear `dHp`), 60 s, damage formula unchanged |
| Acceptance | no `duelAccepts` roll: both have come to fight |
| Rage | starts at 0 every game; specials (绝技) allowed |
| Gear | everything of ITEMS §3.3 applies (`duelWar`, crit, reach, parry, HP); duel abilities of rare+ items (ITEMS §7.4) apply. Army 宝具 do not (there is no army) |
| Titles | TITLES duel bonuses apply (`critDuel`, `dmgDuel`) |
| Strings, crits, parries, lows / overheads | COMBAT-V4 unchanged |
| Ties at time-out | side a (the higher seed) wins |
| Terrain | none: the stadium stage (`opts.stage`, §10.3) |
| HUD label | 「武林大会 · 八强 · 第3场」, seed numbers on the name plates (「[2] 关羽」), BO3 score in the final (「一 : 〇」) |
| Army effects | none: no morale, no rout, no capture, no `duelFinish` |

### 6.2 No deaths, no capture

The tournament is 点到为止: a KO ends the match and the referee (the 裁判台 gong) stops it. Nobody dies, nobody is captured, nobody loses gear, and the ITEMS §8.2 **阵斩 roll never applies** in tournament duels. Rulers can enter safely; their unique weapons cannot be taken here.

### 6.3 Injuries (负伤)

A KO loss can injure. One roll per KO loss on `rng('injury', matchId)`:

```
p = 0.06
  + 0.10 if the KO blow was a crit (COMBAT-V4 §3 `crit`)
  + 0.06 if the KO blow was a string finisher or a special
p = p × 0.5 if he carries a 医药 treasure (ITEMS type `medicine`: 金创药, 神农本草经, 青囊书, 盖伦医书 …)
p = min(p, 0.22); time-out losses never injure
injured → gen.injury = { until: G.monthIndex + 2, war: 5, away: false }        // months 1 and 2; the one shared record (INTEGRATION §6.4)
          if his faction employs a famous healer (华佗; world: 盖伦) → until = G.monthIndex + 1   // 「神医妙手」
```

- Effect while `G.monthIndex < until`: **−5 effective 武力** in battles and duels (ITEMS `eff()` and `duelWar()` subtract `injury.war`; ENGAGEMENT's event injuries use the same record with `away: true`), a bandage chip 「负伤」 on every portrait list, and no tournament entry (irrelevant in practice).
- Expected count: about 8% of losses → ≈ 3–4 injured generals a year in the classic scenario, 5–6 in the world. Too small to change a war; enough to make "send everyone" a decision.
- The loser's final reaction in the duel shows it: he stays down on one knee, and a medic runs onto the stage with a stretcher (2 figures, ≤ 1.5 s, skippable).

### 6.4 Knowledge and practice

- **AI knowledge** (COMBAT-V4 §14.2): one `aiKnow` map per tournament, passed to every match in slot order. Generals watch each other: a string seen in an earlier round is known in the next. Not saved.
- **Player knowledge** (COMBAT-V4 §12.2): every rendered match (played or watched, not skipped) marks the steps the player saw in `G.comboSeen`. The tournament is the best place in the game to scout foreign generals' strings.
- 演武 practice (COMBAT-V4 §12.4) and 切磋 sparring (ENGAGEMENT §13.4) are the preparation; neither gives tournament seeding.

---

## 7. Prizes and rewards

### 7.1 Table

| Place | Item / skill | Gold (to his city) | Prestige (faction) | Fame | Exp | Loyalty | Other |
|---|---|---|---|---|---|---|---|
| **1st 魁首** | random **rare** item (75%) or **武林秘传** rare skill (25%) (§7.2–7.3) | 400 | +50 | +1 | +80 | +6 | 「武林魁首」 badge for a year; `G.tourney.wins + 1`; three titles → 金刚杵 (§7.4); his ruler's `victory` music |
| **2nd 亚军** | **uncommon** item, choose 1 of 3 (85%), or a tier-2 秘传 (15%) | 200 | +25 | — | +50 | +4 | |
| **3rd 季军** | **common** item of his culture, choose 1 of 3 | 100 | +12 | — | +35 | +3 | |
| every entrant | — | — | — | — | +20, +12 per match won (byes do not count) | +2 (non-rulers) | `G.tourney.stats` record |
| host | — | — | +20 | — | — | — | capital order +5 (§3.3) |
| 竞猜 right (player) | — | 60 (capital) | +5 | — | — | — | §7.6 |
| 赞助 (player) | — | −100 / −300 / −800 | +3 / +9 / +24 | — | — | — | §7.6 |

Exp is story exp (outside ENGAGEMENT's routine cap of 20 per month): `Progress.gain(gen, e, 'tourney')`. A champion from round 1 of a 64 draw earns ≈ 20 + 5 × 12 + 12 (final) + 80 = 172 exp, about one level at levels 4–6 and less than one large battle (cap 240). Loyalty never exceeds 100 and rulers get none. Gold goes to the city the general is in (always his faction's).

Bonds (ENGAGEMENT §11, adopted): two finalists of different factions, both with 武力 ≥ 85, become 宿敌 with 30% (`rng('bond')`); finalists of one faction gain affinity +10.

### 7.2 Item draws

**1st place** (`SG.Items.tourneyPrize(gen, 1, rng('prize', 1))`, ITEMS §8.9 with this spec's weights):

```
pool = champion-pool items not yet awarded (12 tournament-only rares, each once; weight 3)
     ∪ shop rares of the scenario not owned by any faction (weight 1; a shop's own stock copy does not count, INTEGRATION §7.2)
     never: ruler-unique, signature, hidden or city-treasure items (both scenarios)
filter: the item's req passes on the champion's base attributes (ITEMS §2.2)
weight × 2 if equipping it raises his ITEMS §13.1 value for its slot
classic, from the 第三届 (193) on: with 15% the draw is replaced by one item of the `foreign` pool (19 items: world-only rares and epics of absent cultures, ruler-uniques filtered out; ≤ 1 epic per tournament; ITEMS §8.9)
pool empty → an uncommon of his culture + 300 gold
skill instead of item: 25% (rng('skill', 1)); if he already has a 秘传, the item is drawn instead
```

The 12 tournament-only rares are the event's own treasures: 干将·莫邪, 太阿剑, 越女剑, 万人刀, 新亭侯刀, 夔牛鼓, 角斗士头盔, 奥林匹亚橄榄冠, 拳斗皮带, 野见宿祢力带, 斯基泰黄金梳, 冒顿鸣镝 (ITEMS §8.9 table). Their weight 3 means they come out first: an estimate (75% item × ≈ 75% tournament-only share while about half of the shop rares are stocked somewhere) gives ≈ 0.55 a year, so about half of them are awarded in the first ten years, and the shop rares carry the pool afterwards (50 in each scenario: the classic one sells the world-only shop rares as 舶来 curios in its trade cities, ITEMS §6.3).

**2nd place**: three distinct **offers** of uncommons (`SG.Items.offers(gen, 'uncommon', 3, rng('offer', 2))`): 60% of each offer from the placer's culture, 40% from any culture; each passes his requirements; at least one is for the slot where he gains most. The player chooses on a three-card table (§9.8); the AI takes the highest valued. **3rd place**: the same with three commons of his culture.

Every prize item goes into the general's slot when it is better (the player is asked 「立即装备？」), else to the faction armoury (ITEMS §2.3). A tournament rare sells at 50% of value (ITEMS §4.3).

### 7.3 武林秘传 (skill prize)

- A **learnable special** (必杀, DESIGN-V3 §4 pool) the winner does not know. 1st place: tier 3 (70%) or tier 4 (30%); 2nd place: tier 2. The `assassinate` kind is excluded (DESIGN-V3's one-assassination rule).
- It goes into a **bonus slot** outside the 武力 slot rules: `gen.bonusSpecial = id`. One per general for life; a second skill prize becomes an item. It does not need levels or 武力 to unlock and is shown in the battle 必杀 menu like any learned special (`SG.Specials.known(gen)` appends it).
- Presentation: a sealed bamboo scroll opened by the host ruler; the special's own cut-in plays once on a training dummy beside the podium.
- Why a battle skill: the tournament's rewards should reach the rest of the game. A champion carries one more trick into his next war.

### 7.4 Championships and relics

- `G.tourney.wins[name]` counts titles (the one count, INTEGRATION §6.4: it replaces ITEMS' `G.items.titles` and resolves TITLES Q8's name clash).
- **3 titles**: the first general to reach three receives **金刚杵** (legendary, ITEMS §9: 「因陀罗之雷」), presented from the shrine niche above the 主门 where it has waited since the first tournament (§8.6). Measured (48 classic campaigns × 15 years): awarded in **48 / 48** runs, a median of **9 tournaments** in (≈ 199); holders: 吕布 23, 张飞 10, 关羽 4, 许褚 3, 典韦 3, 马超 2, others 3. World (12 runs × 12 years, 11 tournaments each): 7 / 12, a median of 6 tournaments in where awarded (吕布 4, 太史慈, 关羽, 马超).
- **No five-title relic.** 赫拉克勒斯之棍 is world-only (ITEMS §9: slay 康茂德); the classic scenario's legendaries are 赤兔, 传国玉玺 and 金刚杵 (INTEGRATION §7.2). `RELICS` (§10.1) has only `title3`.
- After a relic is awarded it is an ordinary singleton (ITEMS §2.3); the niche stays empty and the stadium shows the holder's name on a plaque.

### 7.5 Records and the 英雄榜

`G.tourney.history` keeps the last 40 tournaments: year, host, city, field size, draw, podium (names, factions), the final's score, the prize id, and the biggest upset (winner, loser, `duelWar` gap). `G.tourney.stats[name] = { e, w, best }` (entries, match wins, best place).

The **英雄榜** page (layout UI-V4 §12.8; opened from the announcement card, from **菜单 → 卷宗 → 英雄榜** (the tile in UI-V4 §6's 卷宗 grid; there is no separate 「武林」 menu entry) and from each general's **履历** tab of the character menu) shows:

- 历届魁首: a vertical scroll of years with the champion's portrait, faction seal, host city and final score; three-time champions in gold.
- 记录: most titles, most match wins, longest unbeaten run, biggest upset, most entrants in one year, youngest / oldest champion (from `gen.born`), most podiums by a faction.
- A general's **履历** tab (character menu, UI-V4 §9): entries, wins, best place, titles (`G.tourney.wins`), injuries.
- Achievements are ENGAGEMENT's (天下第一 30 prestige, §14.2); this spec proposes three more: 慧眼识英 (3 correct predictions, +10), 满门英杰 (three of the semifinalists from one's own faction, +30), 以弱胜强 (a player-controlled win against a `duelWar` 15 or more higher, +15).

### 7.6 Taking part without fighting (request 13)

- **竞猜** (prediction): when the 十六强 round is drawn, the player may name one remaining entrant as champion (free, once). Right: 60 gold to the capital, prestige +5, `G.tourney.predictHits + 1`. The card shows the seeds, the head-to-head knowledge (strings seen) and the crowd's favourite (top seed).
- **赞助** (sponsorship), once per tournament: 助银 100 gold → prestige +3; 厚赠 300 → +9 and relation +5 with the host (ENGAGEMENT §3.3); 冠名 800 → +24 and the player's banners flank the 主门 during the final with a line in the opening ceremony. ≈ 33 gold per prestige point, slightly better than a 盛大 festival (40) and never a large source (ENGAGEMENT §17 cap).
- **观战**: any ★ match and every final can be watched; watching also scouts strings (§6.4).

### 7.7 Prize value against the item economy

| Item | Value |
|---|---|
| 1st item (champion pool average) | 1 289 nominal (9 × 1 400, 2 × 840 helmets, 1 × 1 190 bow); shop rares average 1 373 |
| 2nd uncommon / 3rd common (catalogue averages) | 312 / 77 |
| Gold | 700 per tournament |
| Nominal value per tournament | ≈ 2 380 gold-equivalent, split over three factions |
| Share of all factions' gross income (measured, gold only) | classic 1.66–1.71%, world 0.30% (`campaign` mode, 48 / 12 seeds) |
| Share including the items' nominal value | classic ≈ 6%, world ≈ 1% (gold share × 2 380 / 700) |
| For a median 5-city realm (≈ 290 gold / month) | a 1st place is worth ≈ 5.8 months of income, a 2nd ≈ 1.8, a 3rd ≈ 0.6 |
| ITEMS reference | a rare is "≈ 2 years of one median city" (ITEMS §4.4): the champion earns one rare a year |

Reading: the tournament is a large prize for the one faction that wins it and almost nothing in the world economy. A small realm with one great fighter (刘备 with 关羽 and 张飞: a top-3 place in 75% of AI-simulated tournaments, more with a skilled player) gains a rare a year, which is the intended "skill pays" reward; ENGAGEMENT's scripted engaged player keeps all prize gold (文会 + 武林) within its 8% cap (ENGAGEMENT §17.1). The prizes never touch the 24 epic and legendary items except the designed 金刚杵 and the classic 异域奇珍 rule.

---

## 8. The grand stadium: 万国擂台

### 8.1 Concept and historical anchors

The stadium is the game's one place where the whole world meets, so it is built from the world: a ring of **16 slots**, twelve of them given to the twelve culture families of the city view (CITY-VIEW §3: 华夏, 三韩, 倭, 草原, 绿洲, 天竺, 安息, 阿拉伯, 库施·阿克苏姆, 罗马, 北方, 南海), each fronted by one of its landmarks of the period and filled with its people. The land route of the Silk Road runs around the northern half of the ring, the sea route around the southern half, so the ring reads like a map wrapped around the arena: China in the east, Rome in the west, the steppe and the oases to the north, Africa, Arabia and India to the south.

It is a composite and says so (the 英雄榜 page carries a one-line legend 「万国擂台：集天下名迹于一台——游戏取舍」), but every piece is a real structure that stood (or stood in ruin) in 190, or a recorded form of that people. Contests at great venues are attested in every corner of this world, and the stadium borrows from them:

| Tradition | What the stadium takes from it | Source |
|---|---|---|
| Han 角抵戏 at 平乐馆 / 平乐观 | the main gate's name 平乐观; spectators from far around | 汉书·武帝纪: 元封三年春「作角抵戏，三百里内皆来观」; 元封六年夏「京师民观角抵于上林平乐馆」 |
| 灵帝's great review at 平乐观, 188 | the royal box under a tiered 华盖 at the 主门 | 后汉书·孝灵帝纪 中平五年十月 (自称「无上将军」，耀兵于平乐观); 后汉书·何进传 (大坛上建十二重五采华盖) |
| Han 都试 (the annual autumn military test of officers and men) | seeding by merit, the judges' pavilion | Han military examinations, held in autumn |
| Olympia (still held until 393/394) | the olive tree and crown at the podium; the idea of a truce (Q4) | Ancient Olympic Games: wild-olive kotinos, the ekecheiria truce |
| Roman ludi and munera; 康茂德 in the arena (192) | the Colosseum sector; 康茂德 as an AI ruler-entrant | Cassius Dio 73(72).17–22; Herodian 1.15 |
| The Olympics of Antioch, made regular under 康茂德 (date debated: 181 or 212) | the "games of all nations" framing | Downey; Remijsen 2010 |
| 野见宿祢 and 当麻蹶速, the founding legend of sumo | the 倭 crowd's cheer; ITEMS' 野见宿祢力带 prize | 日本书纪·垂仁天皇七年 |
| The Xiongnu's great gathering at 茏城 in the fifth month | the steppe sector's tents | 史记·匈奴列传 |

### 8.2 Layout and sightlines

Coordinates: Y up, the stage centre at the origin; the duel axis is X (fighters at |x| ≤ 7.2 m, `ARENA`), and the duel camera stands at +Z looking −Z (`duel-game.js updateCamera`). Azimuth φ: 0° = −Z (behind the fighters), 90° = +X (east), 180° = +Z, 270° = −X.

| Element | Dimensions |
|---|---|
| Stage (擂台) | 18 × 9 m stone platform, top at y = 0 (the duel ground), 1.2 m above the sand; stone balustrade 0.75 m along the back edge; three steps at the front; a red line along the duel axis |
| Arena floor | sand disc r = 23 m at y = −1.2; a raked ring path at r 11.5–12.3 (the procession track) |
| Facades | each slot's landmark facade stands on the sand at r ≈ 21.5–23.5, **≤ 7 m wide, ≤ 9 m tall, recognisable between 0.5 and 6 m** above the sand |
| Podium wall | r 24.5, top at y = 1.6 (2.8 m above the sand), lanterns along its top |
| Stands | r 25 → 41, 14 rows rising from y 1.6 to 13.5, cross-aisles after rows 5 and 10 |
| Outer arcade | r 42, 16 m high, piers every 5.6° |
| Crowns | each slot's tall landmark element behind the arcade at r 44–64, up to 45 m (the Pharos), seen in the aerial, ceremony and intro shots |
| Sky, ground | warm fog 90–260 m; a meadow disc to r 160 |

**Why the facades are small and in front.** The duel camera (FOV 30°, distance 5.8–15 m, 1.6–2.2 m above the stage, pitched down a few degrees) was measured with `updateCamera`'s own formulas for two fighters 3.4 m apart: desktop 16:9 distance 7.7 m, height 1.9 m, pitch −6.8°; phone 844 × 390 distance 6.4 m, height 1.8 m, pitch −9.1°. The top of the frame then crosses the facade line (r ≈ 22) only **≈ 7.4 m above the sand on desktop and ≈ 6.0 m on phones**, and the outer arcade (r 42) at ≈ 10 m. The stage's back railing hides the facades' lowest 0.3 m. Anything tall behind the stands is out of frame during a fight. So each slot's identity is carried by a facade at the arena's edge whose **recognisable features sit between 0.5 and 6 m above the sand**, with its own crowd on both sides and above it; the facade may rise to 9 m for the walk-on and intro shots, and the tall crown elements serve the establishing, intro and ceremony shots. Measured in the block-out (`shots/duel-roman.png`, `duel-hua.png`, `duel-indic-phone.png`): the facade and two to four rows of crowd fill the upper half of the frame on desktop and phone.

**The rotating ring.** The ring (stands, facades, crowns, crowd, banners, lanterns) is one group that rotates about Y; the stage and sand do not. Before each match the ring is turned (during a cut, never on screen) so that the chosen backdrop slot sits at φ = 0 behind the duel: `ring.rotation.y = φ(slot)`. Backdrop choice:

| Match | Backdrop slot |
|---|---|
| up to the quarterfinals | the higher seed's family sector (world); the classic scenario rotates through the 12 sectors by match index (all entrants are Han). Never the same slot as either of the previous two shown matches (next slot clockwise instead) |
| semifinals | the lower seed's family sector |
| 季军战 | 奖台 (the podium) |
| final | 主门: the host gate, the royal box with the host ruler, the 华盖 and the relic niche |

### 8.3 The sixteen slots

| Slot | φ | Facade (in the duel frame) | Crown (aerial, intro, ceremony) | In 190 AD | Grade | Sources |
|---|---|---|---|---|---|---|
| **主门** (host) | 90° E | default (华夏 host): a pair of stone **阙** after the 太室阙 (118 AD) with eaves and reliefs, the gate hall with red doors and gold nails, the **royal box** above | the **十二重五采华盖** of 灵帝's 188 review (a pole with twelve tiered canopies in the five colours, ≈ 23 m); the relic niche with the 金刚杵 | 阙: built type of the period; 华盖: two years before the game | A | 太室阙; 后汉书·何进传 |
| 华夏 | 67.5° | rammed-earth wall in 版筑 courses with crenels and a drainage spout | the Han frontier wall with reed and tamarisk courses and a **烽燧** beacon tower (smoke by day, fire at night) | standing (Hexi walls from the 2nd c. BC) | A | Waugh, *Yumen Guan* |
| 草原 | 45° | two felt **穹庐** and a row of **deer stones** | the great 穹庐 of the 茏城 gathering with long horse-tail standards | deer stones: 1200–600 BC, standing; 穹庐: Shiji | A / B | 史记·匈奴列传; UNESCO Deer Stone Monuments |
| 绿洲 | 22.5° | the carved rammed-earth front of a cliff city with doorways (交河, capital of 车师前国) | an earthen stupa on a square base (楼兰 / 尼雅 type) | 交河: Han-period city; stupa type 2nd–4th c. | B | 汉书·西域传 |
| **裁判台** | 0° N | two-storey Han pavilion with the judges' table, the **建鼓** drum on its pole, a stepped **漏壶** water clock | the drum tower | Han forms (reliefs) | B | Han pictorial stones |
| 安息 | 337.5° | the great **iwan** of Hatra with two smaller iwans (barrel vaults, mask keystones) | a temple block with stepped merlons | Hatra flourished between Trajan's siege (116/117) and Severus' (198); iwans a Parthian innovation | A | UNESCO 277; Iranica *Hatra* |
| 北方 | 315° | a Celtic timber palisade and a roundhouse with a conical thatch roof | a ring of **Stonehenge** trilithons | standing, Neolithic (not Celtic: shown as "the north's ancient wonder") | B (liberty) | |
| 罗马 | 292.5° | two arcaded storeys of the **Colosseum** (travertine, engaged columns, arches with dark openings) | the attic storey with velarium masts and awning; the **Colossus** re-headed by 康茂德 as Hercules with club and lion | Colosseum inaugurated 80; Commodus fought there in 192 and gave the Colossus his own head | A | Dio 73(72).22.3; *Secutor* |
| **万国之门** | 270° W | Persepolis' **Gate of All Nations**: four tall columns with bull capitals and two pairs of **lamassu** flanking the tunnel the entrants walk out of | the columns themselves (12.5 m in the stadium, 16.5 m in reality) | a ruin since 330 BC; shown restored | A (form) / liberty (state) | *Gate of All Nations* (XPa inscription) |
| 尼罗 | 247.5° | three steep **Meroë** pyramids (≈ 70°) with pylon chapels | the **Pharos** of Alexandria (scaled to 45 m), lit at night as the stadium's beacon | Meroë in use c. 300 BC–350 AD; the Pharos stood c. 280 BC – 14th c. | A | *Pyramids of Meroë*; *Lighthouse of Alexandria* |
| 阿拉伯 | 225° | the two-storey **Khazneh** of Petra carved in a rose rock face (six columns, pediment, tholos) | rose sandstone cliffs | carved in the 1st c. (Aretas IV); Petra in Roman Arabia Petraea from 106 | A | World History Encyclopedia, *Petra* |
| 天竺 | 202.5° | a **Sanchi torana**: two posts, three curved architraves, elephant capitals | the Sanchi dome with drum, harmika and triple parasol | toranas 1st c. BC – 1st c. AD | A | *Sanchi Stupa* |
| **奖台** | 180° S | the three-step podium, two bronze **鼎**, the prize pedestal | an **olive tree** (Olympia's kotinos) and victory pennants | Olympics held until 393/394 | A (symbol) | *Ancient Olympic Games* |
| 南海 | 157.5° | a **干栏** stilt house and a great **Đông Sơn bronze drum** on a platform (the start drum: struck three times) | a Funan brick shrine tower and palms | Đông Sơn drums c. 600 BC – 3rd c. AD; 马援 took 骆越 drums (后汉书·马援传) | A (drum) / B (shrine) | *Dong Son drum*; *Ngọc Lũ drum* |
| 倭 | 135° | the palisade (**城栅**), a **楼观** watchtower and a raised storehouse (Yoshinogari) | the raised-floor main hall | Yayoi; 魏志倭人传 on 卑弥呼's seat: 「居处宫室楼观，城栅严设」 | A | 三国志·魏书·东夷传; Sahara 1992 |
| 三韩 | 112.5° | a 高句丽 dressed-stone fortress wall with a gate | a stepped **积石冢** with pines planted around it | 东夷传 on 高句丽 burial: 「积石为封，列种松柏」 (the big 将军坟 is 5th c. and not shown) | A (text) / B (form) | 三国志·魏书·东夷传 |

Every facade carries the crowd of its family, its factions' banners (§8.8) and three to six lanterns.

### 8.4 Recipes and art direction

All geometry is built with `SG.LP.MeshBuilder` in the Lanternvale style (flat faces, per-face jitter 0.04–0.08, vertex colours, no textures, ink-outline shells on buildings and props, none on sand, crowd, banners' cloth or particles). Palette: `SG.LP.PAL` plus per-slot accents. One recipe per slot returns `{ shell, facade, crown, lamps, anchors }` (`anchors`: banner sockets, crowd rows, camera marks). The block-out (`stadium-blockout.js`) is the layout reference; the finished recipes follow these notes:

| Slot | Shape notes (what makes it read) | Palette | Triangles (facade / crown) |
|---|---|---|---|
| 主门 (hua) | two 阙 with tapered shafts, one-step eaves of grey tile, carved relief bands; gate hall red with 5 × 5 gold nails; royal box: red columns, cream walls, lattice; 华盖: twelve 10-sided canopies blue-red-yellow-white-black from 3.6 m down to 1.4 m radius; relic niche glowing white-gold | `rammed`, `hanRed`, `tileSlate`, `gold`, five colours | 3 000 / 3 500 |
| 华夏 | six 版筑 courses alternating light and dark, crenels, a stone drain spout; beacon: tapered tower, ladder, brush-stroke smoke | `rammed`, `rammedDark`, `#d9c189` reeds | 1 800 / 2 200 |
| 草原 | felt walls cream, roofs ochre, door felts red; deer stones grey with a carved belt and diagonal deer | `#e2d2b0`, `#cdb88a`, stone | 1 600 / 2 400 |
| 绿洲 | earth blocks with soft edges, dark doorways, a vine; stupa: square base, drum, dome | `#c8a878`, `#d8b888` | 1 400 / 1 800 |
| 裁判台 | two roofs with upturned eaves, judges' table with scrolls, 建鼓 with feathered canopy, three bronze vessels of the water clock, a gong | `plaster`, `hanRed`, `brass` | 2 400 / 2 000 |
| 安息 | iwan arch of 7 voussoirs, two small iwans, a band of mask heads, stucco cream | `#d8c8a0`, `#e6d6b4` | 2 200 / 2 400 |
| 北方 | palisade of 17 posts with rope lashing, roundhouse with smoke hole; sarsens grey-lilac | `#8a6a4a`, `#b89a5a`, `stoneCool` | 1 800 / 2 000 |
| 罗马 | two arcade storeys (3 arches each), engaged columns Tuscan / Ionic, cornice; attic with 6 masts and a sail-cloth awning; Colossus bronze with lion skin and club | `#e2d6bc`, `#efe4c8`, `#b07a3a` | 2 600 / 3 500 |
| 万国之门 | four fluted columns (8 sides) with double-bull capitals, lintel; two lamassu: bull body, folded wings in three feather bands, bearded head with tall crown | `#d8cbb0`, `#c6b89a`, `#3a3134` | 3 000 / — |
| 尼罗 | three steep pyramids with stepped edges, pylon chapels with cavetto cornice; Pharos: square tower with windows, octagon, round lantern, a fire sphere (emissive) | `#b9875a`, `#e8dcc4`, fire `#ffb65c` | 1 400 / 2 800 |
| 阿拉伯 | rock face of 4–6 faceted blobs, columns and pediment, tholos with conical roof and urn | `#c9785a`, `#e0a080` | 2 400 / 2 000 |
| 天竺 | torana posts with lion capitals, three curved architraves with scroll ends, small elephant riders; dome 16 sides, railing ring, harmika, three parasols | `#d8b890`, `#ecdcc0` | 2 400 / 2 600 |
| 奖台 | three plinths (2.2 / 1.6 / 1.1 m) marked 一 二 三, red carpet, two tripods with ring handles, the pedestal; olive: LP soft-lump crown silver-green | `#d8cbb0`, `hanRed`, bronze, olive `#9db866` | 1 800 / 1 600 |
| 南海 | stilt house with saddle roof of golden thatch, the drum with a 12-ray star and frogs on its tympanum; brick shrine with corbelled top, palms | `#be9468`, `#c9a04a`, `#b06a4a` | 2 000 / 2 200 |
| 倭 | palisade of split logs, tower with 4 posts and thatch gable, raised store with rat guards; hall with a huge thatch roof | `#8a6a4a`, `#c8a868` | 2 000 / 2 400 |
| 三韩 | wedge-stone courses with "nail" corner stones, gate with wooden lintel; tomb of four stepped stone layers, six pines | `#a99f92`, `#9a9184`, pines `forest` | 1 600 / 2 200 |
| shell (every slot) | podium wall with lantern hooks, 14 stand rows (4 segments each), aisles, outer arcade with piers and cornice, 4 banner sockets | `#bfb4a2`, `#d8cbb0` | ≤ 1 200 |

### 8.5 Choices, rejections and liberties

| Candidate | Decision | Why |
|---|---|---|
| **Taq Kasra** (the Ctesiphon arch) | rejected for the Hatra iwan | Sasanian, dated between the 3rd and 6th centuries (most often Khosrow I, c. 540); not standing in 190. Hatra's iwans are Parthian and at their height in the 2nd century (CITY-VIEW makes the same choice for 泰西封) |
| **Persepolis** | used as the 万国之门, restored | its gate's own inscription names it the "Gate of All Lands" (XPa), the perfect entrants' gate. It was a ruin in 190: shown restored, marked 「游戏取舍」 in the encyclopedia card |
| **Pharos** | used | standing in 190 and a lantern at night: the stadium's natural beacon in a Lanternvale night |
| **Colosseum** vs Pantheon | Colosseum | an arena, and the place where 康茂德 himself fought in 192 |
| Giza pyramids | rejected for Meroë | Meroë's pyramids were still being built for living Kushite kings in 190; Giza stays a background idea (Q5) |
| 阿克苏姆 stelae | not used (nor in CITY-VIEW) | the great carved stelae are 4th century |
| Angkor, Borobudur | rejected | 9th–12th centuries. The Đông Sơn drum and a Funan brick shrine stand for 南海 |
| 将军坟 (高句丽) | rejected | 5th century; the stepped 积石冢 of the 东夷传 is the period form |
| 铜雀台, 黄鹤楼 | rejected | built in 210 and 223 |
| Kanishka's stupa (Peshawar) | alternative for 天竺 | contemporary (2nd c.) but its height and form are uncertain; Sanchi's torana is the most readable gateway in low-poly |
| Temple of Bel, Palmyra | alternative for 阿拉伯 | dedicated in 32 AD; Petra reads better at 7 m |
| Stonehenge | used, flagged | standing, but Neolithic and not Celtic: the card says so |
| Great Wall | used as a crown, not the main gate | the main gate must be the host's; the wall is the 华夏 sector's crown |

### 8.6 The host gate and the relic

The 主门 changes with the host's family; the rest of the ring never changes.

| Variant | Families | Form (period anchor) | Triangles |
|---|---|---|---|
| `hua` | 华夏 | 双阙, gate hall, royal box, 华盖 (§8.3) | ≤ 3 500 + 3 500 |
| `roman` | 罗马 | a single-bay triumphal arch after the Arch of Titus (c. 81), attic inscription 「LVDI」, quadriga silhouette | ≤ 3 500 |
| `persia` | 安息 | a Parthian iwan gate with stepped merlons (Hatra) | ≤ 3 000 |
| `indic` | 天竺 | a large torana with the royal box behind it | ≤ 3 000 |
| `pylon` | 库施·阿克苏姆, 阿拉伯 | a Kushite pylon gate (Lion Temple at Naqa, 1st c.) | ≤ 3 000 |
| `timber` | 草原, 绿洲, 北方, 倭, 三韩, 南海 | a timber gate tower with the family's roof form and colours | ≤ 3 000 |

All variants keep the royal box (the host ruler sits there: an LP figure with the ruler's look), the host's two giant banners, and the **relic niche**: until the 金刚杵 is won it stands in a lit shrine above the gate (soft white-gold glow, slow crackle of tiny ink-line sparks, ITEMS' look description); afterwards a plaque names its holder.

### 8.7 Crowd

- **Pegs**: a 5-sided body (0.62 m) and a block head, 20 triangles, no ink, instanced: **four `InstancedMesh`es, one per quarter of the ring** (each with its own bounding sphere, so the duel view draws one or two of them), with per-instance colour (family palette, §8.3 table colours), `aSide` (0 neutral, 1 supports side a, 2 side b), `aPhase` (seeded). Desktop 2 400 instances (the block-out's 2 900 pegs at 25 triangles measured 71 k triangles; production 2 400 × 20 = 48 k), phone 900 (18 k).
- **Seating**: each family sector seats its own palette; classic games fill the non-han sectors at 50% density with 「胡商与使节」 colours and the han sectors at 100%.
- **Supporters**: when an entrant walks on, the instances near his faction's banners (§8.8) get his side id for the match.
- **Animation in the vertex shader** (no per-instance JS per frame): `y += 0.04 sin(ωt + phase)` idle sway; uniform `uExcite[3]` (neutral, side a, side b) drives standing (+0.35 m) and jumping (`|sin|` × 0.25 m) and raised arms (the head block lifts and widens). Events set the uniforms: hit → excite 0.3 for 0.25 s on the attacker's supporters; crit / finisher → 0.7; parry → everyone freezes 0.3 s then murmurs; KO → all 1.0 for 2 s, then the winner's side stays at 0.6; walk-on → his side 0.6; host entrants × 1.3.
- **Sound** follows the same excitement (§9.10).

### 8.8 Banners per faction

- Every participating faction gets **two banners** in its family's sector on the outer ring top (four sockets per sector; overflow into the neighbouring sector clockwise), plus a hand banner bearer in the opening procession. In the classic scenario the factions' banners are spread around the ring in faction order.
- Banner: pole + cloth (wind-swayed quad strip, 12 triangles) in the faction colour with the ruler's surname glyph (as the duel's `Arena.addBanner`); instanced, with one **glyph atlas** canvas (8 × 9 cells of 64 px, ≤ 72 factions) for the glyph quads: 2 draw calls for all banners.
- The host: 30 outer-ring poles above the 主门 in the host colour and two giant banners (2 × 6 m) on the gate. The sponsor (冠名, §7.6): two banners beside them.
- Win reactions: the winner's banners dip and rise (pole rotation ±12°, 0.6 s).

### 8.9 Light, time of day, lanterns, effects

- **Mood by round**: `SG.LP.Mood` hour = 10 + 8.5 × (round index / index of the 四强 round) for the rounds up to the semifinals (10:00 … 18:30: late morning for the first round, about 16:00 for the 八强, golden hour for the 四强 and the 季军战), then 20:00 for the final and the ceremony (night; the end of the shared 07:00–20:00 clock, INTEGRATION §10.7).
- **Lanterns**: six red ribbed lanterns per slot on the podium wall (96), four on the stage corners, two at each gate; at dusk they light. Nearest 12 (desktop) / 6 (phone) are point lights (`mood.addLamp`), the rest emissive with halo sprites. The Pharos and the beacon tower burn at night (emissive + one point light each, counted in the 12).
- **Effects** (pooled particles, ≤ 2 extra draw calls each): petals in the host colours and gold on a KO in the final and during the ceremony (≤ 600), dust puffs on the stage at knockdowns, smoke from the beacon by day, a slow ink-wash vignette at the edges during the walk-on.
- **Shadows**: the duel keeps its own shadow camera fitted to the stage (16 m); the ring receives but does not cast into the duel's shadow map (cheap and clean).

### 8.10 Budgets and performance

| Item | Desktop target | Phone target | Block-out measured (`shots/stats.json`) |
|---|---|---|---|
| Ring static geometry before ink shells | ≤ 16 × (1 200 + 2 500 + 3 000) + host gate 3 500 ≈ 111 k max, ≈ 85 k typical | medium LOD: crowns −40%, no small props < 150 triangles | 19.2 k (block-out detail) |
| Core (stage, sand, steps, railing) | ≤ 3 000 | same | 364 |
| Crowd | 2 400 × 20 = 48 k in 4 quarter meshes (12 k each; 1–2 drawn in the duel view) | 900 × 20 = 18 k | 71 k desktop / 31 k phone (25-triangle pegs, merged per slot) |
| Drawn in the duel view (frustum-culled per slot, incl. shells, crowd, fighters) | **≤ 110 k** | **≤ 70 k** | 67–69 k desktop, 56 k phone |
| Draw calls in the duel view | **≤ 60** (merged: 2 per visible slot, crowd ≤ 2, banners 2, lanterns 2, core 2, fighters ≤ 12, FX ≤ 10, sky 1) | ≤ 45 | 66–68 (unmerged: 3 meshes + shells per slot) |
| Drawn in the aerial / ceremony | ≤ 250 k, ≤ 90 calls (crowns beyond 90 m at medium LOD) | ≤ 140 k, ≤ 60 | 145 k / 126 calls aerial, 103 k / 99 calls ceremony |
| Point lights | ≤ 12 | ≤ 6 | 12 |
| Shadow map | 2048 (stage-fitted) | 1024 | — |
| JS per frame (crowd uniforms, banners, camera) | ≤ 2 ms | ≤ 3 ms | — |
| Frame rate | 60 fps | ≥ 30 fps | — |
| Geometry memory | ≤ 20 MB | ≤ 12 MB | — |

### 8.11 Build, caching and LOD

- `SG.Stadium.prebuild()` is called when the 英雄帖 card opens: a generator that builds the 16 slot meshes, the host gate and the crowd in ≤ 8 ms slices (`requestIdleCallback`, fallback `setTimeout` with a time check), ≤ 150 ms of work in total on desktop. Recipes cache in `SG.LP.MeshCache` under `stadium|slot|lod|variant`.
- Per slot the static parts (shell + facade + crown) are merged into one mesh (+ its ink shell); each slot is frustum-culled on its own.
- Two LODs: `full` (desktop) and `medium` (phone and aerial beyond 90 m). `SG.Quality.tier = 'low'` (CITY-VIEW §9.4): crowd 400, no shadows, no point lights beyond 4, crowns only for the four special slots.
- The scene lives for the tournament and is disposed after the results card (`geometry.dispose()` for per-tournament meshes; the recipe cache stays for the session).

---

## 9. Presentation and ceremony

### 9.1 Opening ceremony (≤ 14 s, any key or tap skips to the bracket)

| t (s) | Beat | Camera | Sound |
|---|---|---|---|
| 0.0 | fade in from the year review | aerial crane from beyond the 万国之门 over the stands toward the stage, golden morning (2.5 s) | `tourney` intro (三通鼓 on the 大鼓) |
| 2.5 | the Đông Sơn drum and the 建鼓 are struck: three rolls | cut to the 南海 drum, then the 裁判台 (1.5 s) | `tourney.drum3` |
| 4.0 | the procession: one banner bearer per entrant faction walks the sand track; each sector's crowd rises as its banners pass | tracking shot along the track (4 s) | crowd swell |
| 8.0 | the host ruler stands in the royal box | low angle on the 主门, the 华盖 above (3 s) | host `fanfare` overlay; voice `tourney.open` (host ruler) 「群雄毕至，比武开始！」 |
| 11.0 | 抽签: an official shakes bamboo lots from a bronze vessel on the stage; slips fly into a paper bracket that unrolls on screen, seeds first (seals 一 … 十六), then the others with brush strokes | close on the vessel, then the UI bracket takes over (3 s) | `tourney.lots`, `tourney.seal` per seed |
| 14.0 | bracket screen | — | `tourney` A section |

### 9.2 The draw (抽签) animation

The draw is computed before the animation (§5.1). The animation shows it: the paper bracket unrolls from the top; each seed's name is stamped by a vermillion seal in seed order (0.12 s each, at most 16 shown individually); the rest fill in with ink strokes in slot order (8 per second); the player's entrants are stamped last in a larger seal with his faction colour. Byes show as an empty slot marked 「轮空」.

### 9.3 Bracket screen (签表)

- **Layout is UI-V4 §12.3** (`mockups/10-tourney.html`): a three-round window (16 → final on desktop, 8 → final on phones), mirrored halves, the final in the centre with the prize pedestal, a mini-map of the whole draw, round tabs and 我方, and the next-match card with 亲自出战 / 观战 / 只看结果 (defaulting to the entrant's mode, §4.3) and the win bar (`winChance`). This spec adds the montage, the ★ featured matches and the content rules below.
- **Match card**: two portraits (`SG.Portrait`), seed number, name, faction colour chip, `duelWar` (gear bonus in green), office name (TITLES Q4: shown), weapon rarity frame, strings known (dots). After the match: the winner's side stamped 「胜」 in vermillion, 「KO」 / 「判定」, a gold 「会心」 tag for a crit KO, the bandage for an injury.
- **★ featured**: AI matches between two of the top 8 seeds, between 宿敌 (ENGAGEMENT §11), involving an ally, or a likely upset (the lower seed has the better tournament record, `G.tourney.stats`). Tap ★ to toggle 观战.
- Buttons (in UI-V4's action bar): 「开赛」 (play the round), 「快进」 (skip the montage to the next player / watched match), 「全部自动」.
- **Montage**: AI results appear in slot order, ~6 per second, each with a brush stroke along the bracket line and the 胜 seal; on phones the list scrolls with them.

### 9.4 Walk-on and face-off (per rendered match)

| t (s) | Beat | Camera |
|---|---|---|
| 0.0 | the higher seed walks out of the 主门 tunnel, the lower seed out of the 万国之门 | behind the lower seed in the tunnel, the lamassu on both sides (1.2 s) |
| 1.2 | name card: calligraphy name, faction seal, seed, record (「二届魁首」), office, the string seals known; his ruler's `fanfare` overlay (duck 0.35) and his cry `tourney.enter` | 3/4 front hero shot, low angle, crown landmark behind (1.4 s) |
| 2.6 | the opponent's card, same | mirror shot (1.0 s) |
| 3.6 | cut: the ring rotates to the backdrop slot (§8.2) during the cut | — |
| 3.6–4.8 | face-off on the stage: both salute (bow, fist-in-palm or the culture's gesture), the 裁判台 gong | the duel's intro push-in (`introCam`), starting from a low angle that shows the backdrop's crown, then the normal duel framing |

The walk-on ends on the face-off frame, over which UI-V4's **match card 对阵** (§12.4: angry-mood portraits or the 3D figures at the gates, the giant brush 「对」, the round pill) appears: 2.0 s for watched matches; for the player's, it waits for 亲自出战 / 观战 / 只看结果. With 「快进」 or for 自动 matches all of this is skipped. Watched AI matches can be fast-forwarded at ×2 like v3's AI duels; the tournament ribbon over the duel HUD (round, pennants, best-of-three pips) is UI-V4 §12.5.

### 9.5 During the duel

- The duel screen is the v4 duel with `opts.stage = SG.Stadium.duelStage(…)` instead of the terrain arena (§10.3). Everything else is COMBAT-V4.
- The crowd reacts to events (§8.7); banners dip; the commentator line is the HUD's existing big-text ("会心！", "看破！") — no extra text.
- At low HP (≤ 30) of either fighter the crowd's murmur rises and the music's percussion thins (the duel piece's `D` section, AUDIO-V4 §A9).

### 9.6 KO and result

- KO: the v3 / v4 slow motion, then `tourney-ko` (gong, drum, chord, 2.1 s) and the crowd roar; petals in the final.
- Result card (2.5 s, tap to continue): the winner's portrait with 「胜」 seal and 「晋级 八强」, the loser's with 「止步 十六强」; exp lines; an injury line if rolled (「张辽负伤（武力 −5，至三月）」 with the bandage).
- The match's knowledge marks (§6.4) and the bracket update happen here.

### 9.7 The final at night

- After the semifinals and the 季军战 the light falls to night (a 3 s time-lapse over the stadium: lanterns light ring by ring, the Pharos and the beacon catch fire), music `tourney-final` 「月下决」 (bianzhong and humming choir, the theme on cello and erhu, then the full march).
- Best of three; between games a 2 s interlude: the score 「一 : 〇」 painted on a hanging banner over the royal box, the fighters return to their marks, the crowd chants (a two-beat clap pattern on the `frame` drum).
- The backdrop is the 主门 with the host ruler in the royal box and the relic niche glowing.

### 9.8 Award ceremony (≤ 30 s; 8 s under 「全部自动」)

The podium screen (tiers rising, prize cards, the 「天下第一」 seal, counters flying to the top beam) is UI-V4 §12.6, played over the stadium's 奖台 camera; this spec adds the 3D beats, the offer table for the player's 2nd / 3rd placers, the 秘传 scroll, the relic beat and the voices:

| t (s) | Beat |
|---|---|
| 0.0 | the three placers walk to the podium; camera from the sand, low (3 s) |
| 3.0 | the placers in UI-V4's order; for a player placer in 2nd or 3rd the **offer table** slides up first: three item cards (icon, name in rarity colour, compare deltas against his current slot, ITEMS §14.1 `candidates` style), tap one (the AI takes the best); a 15% 秘传 replaces the table for 2nd place |
| 7.0 | the host ruler hands sashes to 3rd and 2nd |
| 11.0 | 1st place: the host ruler raises the prize; the ITEMS full-screen reveal for the rare (or the 秘传 scroll, §7.3); voice `tourney.award` (host ruler) 「天下第一，实至名归！」 and `tourney.win` (champion) 「天下第一！」 |
| 16.0 | the champion's ruler's `victory` (AUDIO-V4 §A3) plays; petals; the olive crown (Olympia) or a gold 印绶 (the Han honour) settles on him — by host family: roman / persia / arabia / nile / north → olive crown, all others → 印绶 |
| 20.0 | the 英雄榜 seal stamps 「第{n}届 天下第一 · {name}」 on a scroll that rolls up |
| 22–30 | the relic beat if he reached three titles: the niche opens, the 金刚杵 descends with its lightning crackle (ITEMS legendary reveal) |

Then the **results card**: every reward of the player's faction (items, gold, prestige, exp, loyalty, injuries), the prediction and sponsorship results, records broken, and 「英雄榜 ▸」. Log lines for the 大事记: 「191年1月 第1届武林大会于洛阳落幕，吕布夺魁，关羽、张飞分列二三。」

### 9.9 Camera moments (summary)

| Moment | Camera | Length |
|---|---|---|
| Establishing | aerial crane, FOV 38°, from 110 m out and 70 m up toward the stage | 2.5 s |
| Drum | static close on the bronze drum | 0.8 s |
| Procession | tracking along the sand track at 2 m height | 4 s |
| Host | low angle on the 主门 and the 华盖 | 3 s |
| Lots | close on the vessel | 1.2 s |
| Walk-on | in the tunnel behind the entrant, then a 3/4 hero shot with the crown behind | 2.4 s |
| Duel intro | the duel's push-in, from a lower angle that shows the backdrop's crown | 1.2 s |
| Duel | the v4 duel camera | — |
| KO | duel KO slow motion | 1.6 s |
| Night falls | slow orbit over the ring as lanterns light | 3 s |
| Podium | from the sand, low; then a slow orbit around the champion | 6–10 s |
| Relic | tilt up to the niche, follow the descent | 4 s |

### 9.10 Music, sound and voices

**Music** (this spec; data in `design-v4/tourney/arena-music.js`, validated with `SG.Music.validate`, rendered in Chromium: 0 NaN):

| Key | Title | Form | Length | Measured |
|---|---|---|---|---|
| `tourney` | 群英会 | 1=D 宫调式, ♩112. Intro 三通鼓 (4 bars, timpani and 大鼓 from sparse to a roll, gong) → **A** the theme on suona over horns, strings and a march on 大鼓 / 堂鼓 / 梆子 (8 bars) → **B 万国轮奏**: the head and the answer of the theme passed around the 12 families' lead instruments in the ring's order (dizi, morin khuur, dombra, ney, chanter, cornu, harp, zurna, bansuri, saron, shinobue, piri), two bars each, over a constant frame-drum groove (24 bars) → **A2** tutti (suona + dizi); loops from A | intro 8.6 s, loop 85.7 s | RMS −19.9 dB, peak 0.57 |
| `tourney-final` | 月下决 | ♩84: bianzhong and humming choir (4 bars) → the theme on cello, the answer on erhu (8 bars, heartbeat drum) → the full march at ♩112 | intro 11.4 s, loop 40 s | RMS −22.4 dB |
| `tourney-ko` | — | one bar: horns and strings chord, gong, 大鼓, timpani | 2.1 s | RMS −17.9 dB |

- The theme (8 bars): `1 1_ 2_ 3 5 | 6 5_ 3_ 5 - | 6 6_ 1'_ 2' 1'_ 6_ | 5 - - 0 | 3 3_ 5_ 6 1' | 2' 1'_ 6_ 5 3_ 2_ | 1 2_ 3_ 5 3_ 2_ | 1 - - 0 |` (`js/music.js` notation). Listening samples: `design-v4/tourney/samples/tourney.opus`, `tourney-final.opus`, `tourney-ko.opus`.
- **Host colour**: for a non-华夏 host, the A / A2 lead voice `su` is swapped for the host family's lead instrument (the same instrument as its B phrase), as AUDIO-V4's lead swap does for suites.
- **Who hears what**: bracket, montage, opening and ceremony: `tourney` (or `tourney-final` after night falls); walk-on: the entrant's ruler `fanfare` as an overlay (duck 0.35, AUDIO-V4 §A3); duels: `duel` with the two dueling motifs (AUDIO-V4 §A9); KO: `tourney-ko` overlay; award: the champion's ruler `victory`.

**SFX** (names for `SG.Sfx.play`; recipes for the audio task):

| Name | Recipe |
|---|---|
| `tourney.crowd` | loop: band-passed noise 300–2 500 Hz, AM 0.2 Hz, + random cheer grains from `crowd.*` (AUDIO-V4 §B5) at 0.3 per second; gain follows crowd excitement (−24 dB idle → −14 dB at KO) |
| `tourney.roar` | `crowd.cheer` × 10 voices, 1.6 s swell |
| `tourney.drum3` | three rolls on `dagu` from slow to fast |
| `tourney.gong` | `luo`, long |
| `tourney.lots` | bamboo slips rattling in bronze (filtered noise bursts, 0.6 s) |
| `tourney.seal` | the existing seal stamp (DOMESTIC-FX), pitched by seed |
| `tourney.petals` | soft paper rustle bed under the petals |

**Voices** (AUDIO-V4 owns rendering and languages; the tournament uses):

| Cue | Speaker | 中文 | Pri | Fallback | Status |
|---|---|---|---|---|---|
| `tourney.open` | host ruler | 群雄毕至，比武开始！ | 3 | `attack` | registered (voice-lines.js) |
| `tourney.enter` | entrant | 天下英雄，尽在此地！ | 2 | `duel` | in AUDIO-V4 |
| `tourney.award` | host ruler | 天下第一，实至名归！ | 3 | `grant` | registered |
| `tourney.win` | champion | 天下第一！ | 3 | `victory` | in AUDIO-V4 |
| `tourney.lose` | loser | 技不如人…… | 1 | `miss` | registered |
| `ui.tourneyAnnounce` | herald (`SG.Voice.profile('herald')`) | 天下英雄，会于平乐！ (non-洛阳 hosts: 天下英雄，会于{city}！) | 2 | `tourney.open` | registered |
| `duel.accept` | opponent on the match card | (COMBAT / AUDIO) | 2 | `duel` | registered |

All tournament cues are merged into `design-v4/audio/voice-lines.js` (AUDIO-V4 §B2, INTEGRATION §10.2); `tourney.open`, `tourney.award` and `tourney.win` are among the start / great cues translated into the 14 A linguas.

### 9.11 UI language

- Paper, lacquer and seals (DOMESTIC-FX's components): the 英雄帖 is a vermillion scroll with gold-leaf edges; buttons are lacquer tablets with a gold border (press: sink 2 px, ink ripple, `ui` click); the bracket is ink on warm paper; seeds are vermillion seals; the player's faction colour marks his lines.
- Touch targets ≥ 44 px; phone layouts at 844 × 390 (one round per view); desktop at 1280 × 720 and 1920 × 1080.
- Reduced motion (`prefers-reduced-motion` or the settings toggle): no camera sweeps (static shots with cross-fades), petals off, montage instant.

---

## 10. Data, APIs and save

### 10.1 `SG.TourneyData` (`js/tourney-data.js`)

```js
SG.TourneyData = {
  version: 1,
  FIRST_YEAR_OFFSET: 1,               // first tournament = StartYear + 1
  MONTH: 1, ANNOUNCE_MONTH: 12,       // announced in the news of month 12
  MAX_PER_FACTION: 5,
  MIN_WAR: 60,                        // entry floor (base 武力)
  AI_WAR: { classic: 72, world: 75 }, // AI recommendation threshold
  COURTESY_WAR: 65,
  AI_RULER_WAR: 85,
  RULER_FIGHTERS: ['康茂德'],
  MIN_ENTRANTS: 4, MAX_DRAW: 128,
  SEED: { level: 0.6, title: 1.5, titleCap: 3, reigning: 10, drillHall: 5 },
  COLLISION_W: [0, 16, 8, 4, 2],
  DRAW_TRIES: 24, SWAPS: 400,
  FINAL_BEST_OF: 3,
  INJURY: { base: 0.06, crit: 0.10, finisher: 0.06, medicine: 0.5, cap: 0.22, war: 5, months: 2, healerMonths: 1, healers: ['华佗', '盖伦'] },
  PRIZES: [
    { place: 1, gold: 400, prestige: 50, fame: 1, exp: 80, loyalty: 6, item: 'rare', skillP: 0.25, skillTier: [[3, 0.7], [4, 0.3]] },
    { place: 2, gold: 200, prestige: 25, fame: 0, exp: 50, loyalty: 4, item: 'uncommon', offers: 3, skillP: 0.15, skillTier: [[2, 1]] },
    { place: 3, gold: 100, prestige: 12, fame: 0, exp: 35, loyalty: 3, item: 'common', offers: 3, skillP: 0 },
  ],
  ENTRY: { exp: 20, expPerWin: 12, loyalty: 2 },
  HOST: { prestige: 20, order: 5, noRepeatYears: 2, minCityLevel: 2 },
  FIRST_HOST_CITY: { classic: 'luoyang', world: 'roma' },   // 191 only (§3.2)
  PRIZE_WEIGHT: { tourneyOnly: 3, shopRare: 1, upgrade: 2 },
  PREDICT: { gold: 60, prestige: 5, opensAt: 16 },
  SPONSOR: [{ id: 'small', gold: 100, prestige: 3 }, { id: 'mid', gold: 300, prestige: 9, relation: 5 }, { id: 'name', gold: 800, prestige: 24, banners: true }],
  RELICS: { title3: 'vajra' },        // no five-title relic: 赫拉克勒斯之棍 is world-only (INTEGRATION §7.2)
  BONDS: { rivalWar: 85, rivalP: 0.30, sameFactionAffinity: 10 },
  ROUND_NAMES: { playIn: '预赛', first: '首轮', 32: '三十二强', 16: '十六强', 8: '八强', 4: '四强', 2: '决赛', third: '季军战' },
  PLAY_IN_FRACTION: 1 / 8,            // a round 1 with fewer than S/8 matches is the 预赛; the next round is the 首轮
  WIN_SCALE: 6,                       // winChance(a, b) = 1 / (1 + exp(−(duelWar a − duelWar b) / 6)); re-measured once after COMBAT P1, then FROZEN (INTEGRATION §8.4)
  HOURS: { first: 10, last: 18.5, final: 20 },   // the event drives the shared day clock (INTEGRATION §10.7: 07:00–20:00; the final at its night end)
  SLOTS: [ /* 16 rows: { id, name, phi, family?, facade, crown, grade, refs } — the table of §8.3 */ ],
  HOST_GATE: { hua: 'hua', roman: 'roman', persia: 'persia', indic: 'indic', nile: 'pylon', arabia: 'pylon', steppe: 'timber', oasis: 'timber', north: 'timber', wa: 'timber', han3: 'timber', nanhai: 'timber' },
  CROWN_STYLE: { roman: 'olive', persia: 'olive', arabia: 'olive', nile: 'olive', north: 'olive' },   // else 印绶
};
```

### 10.2 `SG.Tourney` (`js/tourney.js`, pure logic)

```
SG.Tourney.enabled = true
due(G) → bool                                  enabled, month 1, year ≥ FIRST_YEAR, G.tourney.held < year
announce(G) → { year, host, city } | null      called by endMonth at 11 → 12; writes G.tourney.next; returns a news line
pickHost(G) → factionId                        §3.2 (deterministic; FIRST_HOST_CITY for 191)
eligible(G, gen, forAI?) → { ok, why }         §4.1
seedScore(G, gen) → number                     §4.2
aiPick(G, f) → [gen]; defaultPick(G, f) → [gen]
start(G, playerEntries | null) → T             T = { year, n, host, city, entrants[], S, K, slots[], rounds[], round, aiKnow: Map, predict, sponsor }
                                               playerEntries: [{ gen, mode: 'play' | 'watch' | 'auto' }] (≤ 5)
makeDraw(entrants, rng) → { S, K, slots, n, cost }   §5.1 (the prototype's algorithm)
roundName(T, r) → string
pending(T) → [match]                           the current round's unresolved matches in slot order
                                               match = { id, round, slot, a: entry, b: entry | null, bo: 1|3, games: [], winner, loser, kind }
needsScreen(T, m) → 'play' | 'watch' | null    from the entries' modes and the match's 观战 flag
simMatch(T, m) → m                             AI resolution (sync), all games; SG.Random swapped per game
simMatchAsync(T, m, sliceMs = 6) → Promise<m>
recordGame(T, m, duelResult) → m               a rendered game's result (DuelGame.play's return) into the match
advance(T) → bool                              the round is complete → build the next (3rd-place match after the semifinals)
podium(T) → [entry, entry, entry]
rewards(T) → [{ place, gen, f, gold, prestige, fame, exp, loyalty, prize: { kind: 'item'|'skill', id } | { kind: 'offers', ids: [3] } }]
claim(T, place, itemId)                        the player's pick among offers (AI: highest value)
predict(T, genName); sponsor(T, tier)
apply(T)                                       writes everything: items, gold, prestige (ENGAGEMENT), fame, exp (Progress.gain 'tourney'),
                                               loyalty, injuries, bonds, wins, relics, stats, history, log lines, G.tourney.held = year
autoAll(G) → T                                 AI-only, start → simMatch all → apply (Node and the declined case)
estimate(G, gen, draft) → { seed, pQF, pTitle, stars }   400 seeded Monte-Carlo runs of the provisional draw with winChance (≤ 3 ms)
winChance(a, b) → p                         1 / (1 + exp(−(duelWar(a) − duelWar(b)) / WIN_SCALE)), AI vs AI
injuryWar(gen) → 0 | 5; injured(gen) → bool
championships(name) → n; history(G) → [...]
```

### 10.3 `SG.Stadium` and the duel hook

```
SG.Stadium.prebuild(opts) → Promise          opts = { host: familyId, quality, night }  (sliced build, §8.11)
SG.Stadium.scene() → { scene, ring, core, mood, crowd, banners, lanterns }
SG.Stadium.face(slotId)                      ring.rotation.y = φ(slot)
SG.Stadium.setHour(h)                        mood.set(h); lanterns on after 17:30
SG.Stadium.setBanners(entries)               place faction banners (§8.8), supporter ids for the crowd
SG.Stadium.excite(side, level, seconds)      crowd uniforms (§8.7)
SG.Stadium.rig(name, t) → { pos, target, fov } camera rigs: 'opening' 'drum' 'procession' 'host' 'lots' 'walkA' 'walkB' 'night' 'podium' 'relic'
SG.Stadium.duelStage(scene, { backdrop, colA, colB, glyphA, glyphB, culA, culB, hour }) → stage
   stage implements the duel Arena interface: { group, pal, torches: [], update(t), cheer(winner, t), dispose(),
                                                intro?(camera, k), onEvent?(type, who) }   // events: 'hit' 'crit' 'parry' 'ko' 'special'
SG.Stadium.dispose()
```

`js/duel-game.js` changes (owned by COMBAT-V4's file; listed under INTEGRATION):

- `play(opts)`: `opts.mode === 'tourney'` → no acceptance, tie → side a (unchanged rule), HUD label `opts.label = { round, game, score: [wa, wb], seeds: [sa, sb] }`; `opts.stage` → `DuelScreen` uses it instead of `new Arena(…)` and skips the terrain palettes; it forwards events to `stage.onEvent`; `stage.intro` (if present) replaces the first 1.2 s of `introCam`.
- `doSkip` in tourney mode: `sim.runToEnd()` then the result (§5.3).
- `simulateAsync(genA, genB, opts, sliceMs) → Promise<result>`: the `Sim` stepped until `sliceMs` per animation frame.
- `res.stats` (COMBAT-V4) already carries `crits`; the KO blow's kind (`crit`, `finisher`, `special`) is added as `res.koBy` for the injury roll.

### 10.4 Save data

```js
G.tourney = {
  next: { year, host, city } | null,     // announced
  held: 0,                               // last year held
  hosts: [f, f],                         // the last two hosts (rotation)
  count: 0,                              // tournaments held (the 第{n}届 number = count + 1)
  wins: { [genName]: n },                // championships: the one count (INTEGRATION §6.4)
  reigning: genName | null,
  stats: { [genName]: { e, w, best } },  // entries, match wins, best place
  history: [{ y, host, city, n, S, p: [n1, n2, n3], f: [f1, f2, f3], fin: '2:1', prize, upset: { w, l, gap } }],  // ≤ 40
  predictHits: 0,
}
gen.injury       = { until: monthIndex, war: 5, away: false }   // the shared record (INTEGRATION §6.4); dropped on load when expired
gen.bonusSpecial = 'specialId'                     // optional (§7.3)
```

- About 2–4 KB after 30 years. Save version 4; this spec migrates **fourth**, after ITEMS, TITLES and ENGAGEMENT (INTEGRATION §6.2). v3 saves load with an empty `G.tourney` (`held = year − 1` so a save in month 1 of a later year does not trigger a tournament mid-month; `next` is set at the following month 11).
- The tournament state `T` is never saved (§3.1).

---

## 11. AI

| Decision | Rule |
|---|---|
| Entering | §4.2, deterministic, no `SG.Random` |
| Fighting | the duel AI of COMBAT-V4 with the tournament's `aiKnow`; the same skill as in battles |
| Offers (2nd, 3rd) | the highest ITEMS §13.1 value |
| Equipping prizes | ITEMS §13.2 |
| Hosting | automatic (§3.2) |
| Predicting, sponsoring | never |
| Injuries | none special: the −5 is in `eff()`; the AI's battle choices already read effective attributes |
| Cost | the AI spends no tokens or gold on the tournament |

---

## 12. Integration points (by file and function)

| File | Function | Change |
|---|---|---|
| `core.js` | `SG.Random` | `swap(fn) → prev`, `restore(prev)` |
| `model.js` | `newGeneral`, `GameState` constructor, `toJSON` / `fromJSON` | `injury`, `bonusSpecial`; `G.tourney` (§10.4), migration |
| `strategy-ai.js` | `endMonth` | at month 11 → 12 `SG.Tourney.announce(G)` (news line); drop expired injuries; when `SG.TourneyFlow` is absent and `SG.Tourney.due(G)` after the month turns: `SG.Tourney.autoAll(G)` |
| `strategy-screen.js` | `run()` | §3.4 hook; 英雄榜 entry (UI-V4 §6: 菜单 → 卷宗 → 英雄榜); news formatting with the host seal; 「负伤」 chip in general lists; the character menu's 履历 tab reads `G.tourney.stats` |
| `duel-game.js` | `play`, `DuelScreen` constructor / `doSkip`, `simulate` | §10.3 |
| `items.js` (ITEMS) | `tourneyPrize`, `offers`, `eff`, `duelWar` | §7.2, §6.3; drop `G.items.titles` |
| `specials.js` | `known` | append `gen.bonusSpecial` |
| `engage.js` (ENGAGEMENT) | prestige, fame, relation, order, bonds, achievements, scheduled-card queue | the calls of §7.1 / §7.6 through ENGAGEMENT's API; the scheduled card priority (§3.1) |
| `titles.js` (TITLES) | — | office names on bracket cards (read-only) |
| `audio.js`, voice data (AUDIO-V4) | `music()`, `fanfare()`, `Voice.say` | `tourney*` pieces, SFX, cues of §9.10 |
| `city-view.js` (CITY-VIEW) | festival dressing | the host city's tournament dressing in months 12 and 1 |
| `figure.js` | — | the salute gestures (fist-in-palm, bow, raised arm, hand-on-heart) as short clips; the medic and lot-official figures use existing soldier looks |
| `index.html` | | §2 load order |

---

## 13. Coordination with sibling specs

| Spec | Question | Answer here |
|---|---|---|
| ITEMS Q9 | tournament gold and skill share | gold 400 / 200 / 100 (adopted), skill 25% for 1st (adopted), 15% tier-2 for 2nd (new); 2nd and 3rd choose 1 of 3 offers (changed from a single random draw); tournament-only rares weight 3 |
| ITEMS §15 | `G.items.titles` | moved to `G.tourney.wins` |
| ITEMS §14.3 | prize on a pedestal during the final, 金刚杵 in the main gate | adopted (§8.6, §9.7) |
| ENGAGEMENT §13.5, Q7 | prestige +50 / +25 / +12; finalists' bonds | adopted; host +20 and order +5 as for the 文会 |
| ENGAGEMENT | 讲武堂 seeding +5, 求战心切, 武林 mission, 天下第一 achievement | adopted (§4.2, §7.5); three achievements proposed |
| TITLES Q4 | offices on the bracket; a champion's office ceremony | offices shown on cards; no office ceremony (prizes stay items, skills and honours) |
| TITLES Q8 | name clash | resolved (`G.tourney.wins`) |
| COMBAT-V4 §12.2, §14.2, Q6 | rendered tournament duels teach strings; AI knowledge per tournament | adopted |
| AUDIO-V4 §A3 | the arena piece, fanfares on walk-on, champion's `victory` | §9.10 (pieces written and validated); cues `tourney.open`, `tourney.award`, `tourney.lose` proposed |
| CITY-VIEW §6.5 | landmarks | the stadium uses its own copies of several CITY-VIEW landmarks (Colosseum, Pharos, Hatra, Sanchi, Meroë); the recipes can share code (the 30-m city scale vs the stadium's 7-m facades differ) |
| UI-V4 (requests 1, 9, 11 and the screens of 14) | screen layouts, round names, buttons, voices | UI-V4 §12 owns the layouts of the announcement card, entry screen, bracket, match card, duel ribbon, podium and 英雄榜 (`mockups/10-tourney.html`); this spec adopts its round names (预赛 / 首轮 / … / 四强 / 决赛), its 「稍后」 agenda slip (the tournament can wait within month 1), its per-match buttons 亲自出战 / 观战 / 只看结果, and supplies `estimate` / `winChance` for its stars and win bar. Additions requested from UI-V4: the offer table for 2nd / 3rd placers (§9.8), 竞猜 and 赞助 rows on the entry screen (§7.6), the 「负伤」 chip, the injury line on the result card |
| Character menu (request 9) | per-general record | 履历 tab rows (§7.5; UI-V4 §9.3) |

---

## 14. Test plan and balance targets

### 14.1 `tests/tourney.js` (new, Node, exit code = failures; `--quick`: 3 tournaments, 12 seeds × 6 years for the balance item; part of the ≤ 6 min quick CI, INTEGRATION §13)

1. **Draw** (port of `tourney-proto.js bracket`): 400 random fields of 5–128 entrants: deterministic per seed; S correct; everyone placed; byes only opposite ranks 1..B; never bye vs bye; seeds 1 and 2 meet only in the final; the top four seeds in four quarters; seed bands respected; `seedOrder(8) = 1 8 4 5 2 7 3 6`, `seedOrder(16)` as standard.
2. **Separation**: on 200 fields with 5 factions of 5, same-faction round-1 pairs ≤ 1% of draws; cost never worse than a random draw's.
3. **Eligibility and AI entry**: the §4.1 reasons; the AI never sends > 5, never a captive, never an AI ruler under 85 (except `RULER_FIGHTERS`); courtesy entry rule; deterministic (zero `SG.Random` draws).
4. **RNG contract**: run a full AI tournament; the main `SG.Random` stream's next value equals the value it would have without the tournament; the same `G.seed` and year → identical bracket, results, injuries and prizes; a match simulated twice → identical log.
5. **Formats**: final best of three (2–3 games), the 3rd-place match, round names, play-in naming, N < 4 → no tournament.
6. **Injury probabilities**: 10 000 forced KO losses per case: 6 / 16 / 12 / 22% (and halves with medicine) within ±1 pp; healer shortening; `eff` and `duelWar` −5 while injured, 0 after.
7. **Prizes**: pool weights (10 000 draws: tournament-only share ≈ 3 / (3·12 + shop) per item ±2%), requirement filter, each tournament-only rare awarded once, the empty-pool fallback, 异域奇珍 from 193 at 15% ±2 pp (classic, 2 000 runs), offers distinct and valid, the bonus skill slot (once; tier; no assassinate), 金刚杵 on the third title (both scenarios), no five-title relic and no ruler-unique in any classic pool (INTEGRATION §7.2), relics only once.
8. **Rewards**: gold to the right city; prestige and fame through ENGAGEMENT; exp amounts; loyalty caps; bonds probability (2 000 forced finals: 30% ±3 pp).
9. **Save / load**: round trip of `G.tourney`, `injury`, `bonusSpecial`; v3 migration; a save made in month 1 before the tournament replays the same AI results.
10. **Off switch**: `SG.Tourney.enabled = false` → every v3 / v4 output identical over a 5-year all-AI run (hash, like `tests/world.js --classic-hash`).
11. **Balance runs** (§14.4), classic 48 seeds × 15 years paired with and without, world 24 × 12 (nightly).
12. **Calendar** (INTEGRATION §6.5): the tournament card never takes ENGAGEMENT's R1 slot; numbering `第{count + 1}届` (193 = 第三届 in a new game).
13. **CPU**: the January AI resolution ≤ 1.1 s world (sliced, no slice > 6 ms); `tests/world.js` exempts it from the 60 ms per-faction cap and asserts the rest of the month.

### 14.2 Additions to existing suites

- `tests/duel-bots.js`: tourney mode: tie → side a; no acceptance roll; `simulateAsync` equals `simulate` for 50 seeded pairs; `aiKnow` carried between matches changes AI breaks as COMBAT-V4 §15.2 (12) expects.
- `tests/sim.js`: injured generals' effective 武力 in tactical battles (−5).
- `tests/items.js` (ITEMS): `tourneyPrize` / `offers` contracts.

### 14.3 Browser

- `tests/tourney.html`: card, picker (eligibility reasons), bracket (64 and 128 draws), montage, a scripted played match (duel auto bot), a watched AI match and a skipped one compared with `simulate` (20 matches: identical winners and HP), the final's BO3, the ceremony with offers, the results card, the 英雄榜; layouts 844 × 390, 1024 × 768, 1280 × 720, 1920 × 1080; touch targets ≥ 44 px; reduced motion; no `pageerror` / `console.error`.
- `tests/stadium.html` (from `stadium-blockout.html`): every slot as backdrop, every host gate, every hour; budgets of §8.10 asserted from `renderer.info` (duel view ≤ 110 k / 60 calls desktop, ≤ 70 k / 45 phone; aerial ≤ 250 k / 90); fps ≥ 60 desktop and ≥ 30 on an iPhone-class device over 300 frames; build ≤ 150 ms with no slice over 8 ms; memory after dispose back within 2 MB.

### 14.4 Balance targets (measured with the prototype; targets for the implementation with the v4 duel)

| Measure | Measured (v3 duel sim) | Target (v4) |
|---|---|---|
| Classic field at 191 | 46 entrants, 17 / 18 factions | 35–60, ≥ 15 factions |
| World field at 191 | 71 entrants, 44 / 70 factions | 55–110, ≥ 35 factions |
| Champion distribution, classic opening rosters (200) | 吕布 29.0%, 关羽 22.5%, 张飞 15.5%, 马超 7.0%, 孙坚 4.5%, 赵云 4.5%; 16 different champions | the strongest general wins 20–35%; ≥ 10 different champions per 200 |
| Top seed wins / a top-4 seed wins | 29.0% / 74.0% (classic), 24% / 60% (world) | 20–35% / 55–80% |
| Duels won by the lower `duelWar` | 25.7% (classic), 24.1% (world) | 20–30% |
| Champion's `duelWar` | median 102, min 86 | median ≥ 95 |
| Distinct champions per 15-year classic run | median 7, min 5 | ≥ 5 |
| Most titles by one general in 15 years | median 4, max 8 | median ≤ 5 |
| 金刚杵 (3 titles) | 48 / 48 runs, median after 9 tournaments | awarded in ≥ 80% of 15-year classic runs, median 7–11 tournaments |
| Champion from the largest faction | 27.7% | ≤ 40% |
| Factions alive at the end, with / without (paired seeds) | classic 48 × 15 y: 4.56 / 4.44; world 12 × 12 y: 45.67 / 45.33 | within ±10% |
| Largest faction's city share at the end | classic 46.4% / 46.6%; world 12.0% / 12.3% | within ±3 pp |
| Prize gold, share of all factions' gross income | 1.71% classic, 0.30% world | ≤ 2.5% classic, ≤ 0.6% world |
| Rejected variant: entrants unable to act in month 1 | factions alive 5.19 vs 4.44 (+17%), largest share 43.3% vs 46.6% | — (rejected) |
| Compute, AI tournament (Node) | 277 ms classic, 443 ms world (5.8–6.1 ms per duel) | ≤ 700 / 1 100 ms (v4 ≤ 15 ms per duel), no frame over 8 ms of JS |
| Average AI duel | 28 s, 0% time-outs | 20–40 s, ≤ 10% time-outs (COMBAT-V4) |
| Player duels per tournament with five strong entrants | median 9–15 (max 22–26) | ≤ 15 median |
| Injuries per tournament | ≈ 8% of losses (expected) | classic 2–6, world 3–8 |

### 14.5 Playtest targets

- A first-time player finishes the 英雄帖 card in ≤ 60 s with the pre-filled choice; ≥ 80% understand 亲自 / 观战 / 自动 without help.
- A full tournament with five played entrants takes 8–15 minutes; with 「全部自动」 ≤ 90 s; ≥ 70% of testers say the tournament is "something to look forward to".
- In the duel view ≥ 70% of testers recognise their own culture's sector behind the fighters; ≥ 60% can name three landmarks after one tournament.
- No tester loses track of which fighter is theirs (HUD mark, side, colour).

---

## 15. Unity port (after the browser build is stable)

| Unity file | From |
|---|---|
| `Assets/Sanguo/Scripts/Data/TourneyData.g.cs` | `tools/export-unity-data.js` reads `js/tourney-data.js` |
| `Model/Tourney.cs` | `js/tourney.js`: the same function names in PascalCase, the same integer and float order, the same seeded streams (`hash32`, `SeededRandom`), `SgRandom.Swap/Restore` |
| `Battle/DuelSim.cs`, `Battle/DuelGame.cs` | the tourney mode, the stage hook, `SimulateAsync` (coroutine stepping), skip = run to end |
| `View/Stadium.cs`, `View/StadiumKit.cs` | the recipes on the Lanternvale `MeshBuilder.cs` (the browser's `SG.LP` is its port, so recipes translate line by line); crowd with `Graphics.DrawMeshInstanced` and the same vertex animation in a ShaderGraph / HLSL variant of the LowPoly shader; banners with a glyph atlas `Texture2D` |
| `UI/TourneyScreen.cs` | UGUI card, bracket, ceremony, results, 英雄榜 |
| `Audio` | `tools/export-unity-music.js` exports the three pieces |
| `Tests~/tourney/` | rules harness against JS refs (`node tests/tourney.js --refs` → `Tests~/refs/tourney-v4.json`: draws, AI-only results for 20 seeds, prizes), exact to 1e-9 |

---

## 16. Phases and risks

| Phase | Content | Gate |
|---|---|---|
| P1 rules | `tourney-data.js`, `tourney.js`, `SG.Random.swap`, save fields, `autoAll` in `endMonth`, tests §14.1 | `tests/tourney.js` ALL OK; sim / world / items suites unchanged; after COMBAT P1, `WIN_SCALE` and §14.4 are re-measured once with the v4 duel and frozen (INTEGRATION §8.4) |
| P2 screens | card, bracket, montage, match flow with the **existing duel arena** (terrain `plain`) as a stand-in, ceremony as 2D cards, results, 英雄榜 | `tests/tourney.html`; playable end to end |
| P3 stadium | `stadium-kit.js`, `stadium.js`, the duel stage hook, crowd, banners, lighting, camera rigs | §8.10 budgets on desktop and phone |
| P4 polish | music, SFX, voices, petals, relic beat, host gates, reduced motion | no console errors; playtest targets |
| P5 Unity | §15 | harness equal to JS refs |

| Risk | Mitigation |
|---|---|
| Players skip it because it is long | 「全部自动」 and the mode memory; the median played tournament is 9–15 duels; skipping is always one tap |
| 吕布 or one realm wins every year | measured: the top seed wins 24–29%, ≥ 5 champions per 15 years; a skilled player beats the AI; titles add only +1.5 seed points each |
| Prizes snowball a strong AI | gold 0.3–1.7% of gross income; the champion is from the largest faction 28% of the time |
| Phone performance with a full stadium | the duel view draws only the visible slots; crowd 900 instanced; `low` tier fallback; P2 ships with the plain arena first |
| Determinism breaks when the view draws `SG.Random` | the swap contract (§5.5) plus the watched-vs-simulated test (§14.3) |
| Cultural sensitivity of sacred places used as decoration (stupa, iwan temple, tombs) | each is shown whole and respectfully (no combat on or against them; the stage is separate), with its source card; the cultural review ITEMS Q7 asks for covers these too |

---

## 17. Open questions

- **Q1. Final format.** Best of three (proposed) or best of one like the other rounds? Best of three gives a comeback and the night final its weight; it adds ~1 minute.
- **Q2. Injuries.** Keep them (−5 effective 武力 for 2 months, 6–22% of KO losses) or make the tournament fully harmless? Proposed: keep, they make "send everyone" a choice at almost no balance cost.
- **Q3. Hosting by choice.** Should the player be able to bid for hosting (e.g. 500 gold when within 60% of the top prestige)? Proposed: no in v4; hosting stays an honour of prestige.
- **Q4. A truce.** An Olympic-style truce (ekecheiria) for the host city in month 1 (no attacks on it)? It costs AI aggression one city-month a year; not measured. Proposed: off; ask after playtests.
- **Q5. Classic purists.** The classic scenario shows the same composite stadium (the 异域奇珍 of the Silk Road). A Han-only variant would need 11 more Han regional facades (≈ 30 k triangles of new art). Proposed: not in v4.
- **Q6. Entry limit vs field size.** "Up to 5 per ruler" can give 128 draws in the world. Should the world scenario use regional qualifiers (8 regions → 32) instead of one big draw? Proposed: one draw; the play-in rounds are fast and most players only fight their own matches.
- **Q7. Rulers in the AI.** Only AI rulers with 武力 ≥ 85 and 康茂德 fight. Should more famous fighting rulers be listed (孙坚 already qualifies; 孟获 88 qualifies)?
- **Q8. The duel camera in the stadium.** Keep the standard v4 duel camera (proposed: the facades are designed for it) or lower it slightly to show more of the crowns during fights?
- **Q9. Skill prizes.** A battle 必杀 as the skill prize (proposed) or a duel-only perk? COMBAT-V4 Q9 and TITLES keep duels free of rule-changing perks, so a battle skill is the consistent choice.

---

## 18. Sources

History and monuments (web, checked in this task; where only a secondary source was checked it says so):

- Taq Kasra date (3rd–6th c., most often Khosrow I): [Taq Kasra (Wikipedia)](https://en.wikipedia.org/wiki/Taq_Kasra); O. Kurz, "The Date of the Ṭāq i Kisrā", *JRAS* 1941: [Cambridge](https://resolve.cambridge.org/core/journals/journal-of-the-royal-asiatic-society/article/date-of-the-taq-i-kisra/A2F4B72DCE124DF537A0A7B47024D582); [Madain Project](https://madainproject.com/taq_kasra)
- Hatra (Parthian iwans; sieges of Trajan 116/117 and Severus 198): [UNESCO 277](https://whc.unesco.org/en/list/277); [Encyclopaedia Iranica, Hatra](https://iranicaonline.org/articles/hatra)
- Gate of All Nations (Xerxes; XPa "Gateway of All Lands"; lamassu; burned 330 BC): [Gate of All Nations (Wikipedia)](https://en.wikipedia.org/wiki/Gate_of_All_Nations); [Madain Project](https://madainproject.com/gate_of_all_nations)
- Pharos of Alexandria (c. 280 BC, ≈ 100 m, stood until the 14th c.): [Lighthouse of Alexandria (Wikipedia)](https://en.wikipedia.org/wiki/Lighthouse_of_Alexandria)
- Petra, Khazneh (1st c., Aretas IV; annexed 106): [World History Encyclopedia, The Treasury, Petra](https://www.worldhistory.org/collection/234/20-images-of-petra/4/); [Kingdom of Nabatea timeline](https://worldhistory.org/timeline/Kingdom_of_Nabatea)
- Commodus in the arena (secutor; Colosseum 192): [Secutor (Wikipedia)](https://en.wikipedia.org/wiki/Secutor); [Dickinson College Commentaries](https://dcc.dickinson.edu/node/29588); primary: Cassius Dio 73(72).17–22 (incl. the Colossus, 72.22.3), Herodian 1.15 (not re-checked online)
- Olympics of Antioch under Commodus (date debated): [Ancient Greek Olympic festivals (Wikipedia)](https://en.wikipedia.org/wiki/Ancient_Greek_Olympic_festivals); S. Remijsen, *GRBS* 50 (2010): [Duke GRBS](https://grbs.library.duke.edu/index.php/grbs/article/view/1491)
- Ancient Olympics (kotinos, ekecheiria, end in 393/394): [Ancient Olympic Games (Wikipedia)](https://en.wikipedia.org/wiki/Ancient_Olympic_Games)
- Sanchi toranas (1st c. BC – 1st c. AD): [Sanchi Stupa (Wikipedia)](https://en.wikipedia.org/wiki/Sanchi_Stupa)
- Meroë pyramids (c. 300 BC – 350 AD, ≈ 70°): [Pyramids of Meroë (Wikipedia)](https://en.wikipedia.org/wiki/Pyramids_of_Mero%C3%AB)
- Yoshinogari and the Wei zhi (two watchtowers and a wall like 卑弥呼's seat): M. Sahara 1992, [eHRAF](https://ehrafarchaeology.yale.edu/traditions/ab80/documents/017); [Japan National Tourism Organization](https://www.japan.travel/en/spot/757/); primary 三国志·魏书·东夷传·倭人 「居处宫室楼观，城栅严设」
- 太室阙 (118): [maigoo 太室阙](https://www.maigoo.com/citiao/140797.html); [ifeng 中岳汉三阙](https://news.ifeng.com/c/7fYtcQ2DUa9)
- 灵帝 at 平乐观, 188 (secondary checked): [maigoo 汉灵帝](https://m.maigoo.com/citiao/145125.html); primary 后汉书·孝灵帝纪 中平五年十月 and 何进传 (十二重五采华盖), not re-checked online
- 角抵 at 平乐馆: 汉书·武帝纪 元封三年、六年 (primary, not re-checked online; cited by e.g. [Chengdu Sport University journal 2024](https://cdtyxb.cdsu.edu.cn/EN/Y2024/V50/I5/121))
- 都试 (annual autumn military test): [Hong Kong EDB history resource](https://www.edb.gov.hk/attachment/sc/curriculum-development/kla/pshe/references-and-resources/chinese-history/m4issue3_sc.doc)
- Han frontier walls (rammed earth with reed / tamarisk courses): D. Waugh, [The Yümen Guan](https://faculty.washington.edu/dwaugh/CA/cities/dunhuang/yumen1.htm)
- Xiongnu gatherings at 茏城 (fifth month): [Longcheng, Mongolia (Wikipedia)](https://en.wikipedia.org/wiki/Longcheng,_Mongolia); Shiji 110 translation: [Working with Evidence 3.3](https://digfir-published.macmillanusa.com/workingwithevidenceap/workingwithevidenceap_ch3_4.html)
- Deer stones (1200–600 BC): [Deer stones culture (Wikipedia)](https://en.wikipedia.org/wiki/Deer_stones_culture); [UNESCO 1621](https://whc.unesco.org/ES/list/1621)
- Đông Sơn drums, Ngọc Lũ, 马援: [Dong Son drum (Wikipedia)](https://en.wikipedia.org/wiki/Dong_Son_drum); [Ngọc Lũ drum (Wikipedia)](https://en.wikipedia.org/wiki/Ngoc_Lu_drum)
- Sumo's founding legend (日本书纪·垂仁天皇七年): [Nomi no Sukune (Wikipedia)](https://en.wikipedia.org/wiki/Nomi_no_Sukune)
- 高句丽 burial 「积石为封，列种松柏」: 三国志·魏书·东夷传·高句丽 (primary, not re-checked online)

Game files read: `README.md`, `DESIGN-V2.md`, `DESIGN-V3.md`, `CONTRACT.md`, `js/data.js`, `js/model.js`, `js/duel-game.js`, `js/strategy-ai.js`, `js/strategy-screen.js`, `js/lowpoly.js`, `js/lp-pilot.js`, `js/music.js`, `js/music-han.js`, `design-v4/ITEMS.md` + `items.json`, `COMBAT-V4.md`, `ENGAGEMENT.md`, `TITLES.md`, `AUDIO-V4.md`, `CITY-VIEW.md`.
