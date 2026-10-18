# 三国志-霸王的世界 · 第四版玩法与回报设计（ENGAGEMENT-V4）

Player request 13 of v4: **make the non-battle game more fun and rewarding, so the player does not always need to fight.** This spec also carries the engagement side of request 3 (items found outside shops), request 8 (city building projects that show in the zoomed city), request 10 (experience for domestic work, which titles need) and request 14 (the yearly calendar around the 武林大会, and a non-combat counterpart, the 文会).

Read `DESIGN-V2.md` §1 (common rules), `DESIGN-V3.md` §4 (levels), `CONTRACT.md`, and the sibling v4 specs `ITEMS.md`, `TITLES.md`, `DOMESTIC-FX.md` and `COMBAT-V4.md` first. Where this file conflicts with them, this file wins for the systems it owns: 民心 (city order), 声望 (prestige), 邦交 (relations and pacts beyond the v3 alliance), 羁绊 (relationships between generals), random events, 国策 (missions), 营建 (city projects), 商队 (trade caravans sent by the player), 探索 (expeditions), 文会, festivals, 功业 (achievements), victory paths and the score, and **domestic experience** (this answers `TITLES.md` §11, Q1 and Q10). The others win for their systems: ITEMS for items, shops and the visiting 胡商 / 海商; TITLES for offices; DOMESTIC-FX for how commands look and sound; the city-view spec (`CITY-VIEW.md`, referenced by DOMESTIC-FX) for models; the tournament spec for the rules of the 武林大会; COMBAT-V4 for duels. Platform order: browser first (this repo), then the Unity port (§23). Every visual assumes the v4 art direction from the art pilot (Lanternvale: stylized low-poly, cozy Ghibli-warm, flat-shaded props and buildings, soft-lump foliage with the warm-top / cool-teal ramp, ink outlines, soft painted light with warm lantern pools at night). Cards are paper and lacquer, effects are brush strokes, ink splashes, gold leaf and vermillion seals, never neon.

Status: design spec only. No game code was changed and nothing was committed. Supporting files:

| File | What it is |
|---|---|
| `design-v4/engagement.json` | **The catalogue**, generated from `engage/catalog.js`: 33 events (30 with choices, 3 good-news toasts, 75 options) with declarative effects, 5 historical seeds, sage personas, 17 mission templates, 13 projects with names for 12 city families, 44 expedition sites, 12 festivals (+ faction overrides), 48 achievements, 42 starting bonds, 30 starting relations. Do not hand-edit |
| `design-v4/engage/catalog.js` | The same data as JS (the only place it is written). The header defines the effect vocabulary |
| `design-v4/engage/build-json.js` | Validates the catalogue (unique ids, known effect keys, 2–3 options per card, a free AI-selectable option on every card, valid months, city / general / faction keys exist in the scenarios, every culture has a family and every family a festival) and writes `engagement.json`. `node design-v4/engage/build-json.js` |
| `design-v4/engage/README.md` | How to run the builder and the prototype, the environment switches and the exact commands behind §19 |
| `design-v4/engage/engage-proto.js` | Reference implementation and balance prototype. It patches the game in memory only (one hook is injected into the source text of `attackNeed` before evaluation; the files on disk are untouched). It runs all-AI campaigns with the systems off / on and scripted players, and is the source of every measured number in this file (§19) |

---

## 0. Key numbers at a glance

| Topic | Rule |
|---|---|
| Loop | Month: events (0–1 card) → commands → projects, caravans, expeditions tick → end month → festival / 文会 / tournament when due → news. Year: 正旦 review (年鉴) and 武林大会 in month 1, 庙会 in 3, harvest in 7, 文会 in 9, a culture festival once a year |
| Interruption budget | ≤ 1 random event card + ≤ 1 scheduled card per month (outside battles and the player's own commands). Everything else is a toast or a badge. Answering never costs a token |
| 民心 order | 0–100 per city. Start 45 + 德/5 + 町/150 (30–75). Drifts to target 45 + 德/5 + buildings (+2 per month up, −1 down). 征兵 −1 per 3 000 recruits. Effects: income × (0.95 + order/1000), harvest × (1 + (order − 50)/1000), population growth × (0.5 + order/100), bandit and volunteer weights |
| 声望 prestige | Unbounded score per faction. Sources: achievements, festivals, missions, projects, city levels, 文会 / 武林 placings, diplomacy, conquest (+5 per city, +20 per faction destroyed). Converts to 人望 (fame) +1 per 150, at most +10 together with event fame. Event 德 (virtue) bucket ±10 |
| Domestic exp | TITLES §11 table (开发 6/8, 征兵 / 训练 3, 搜索 4/12, 登用 10/2, 同盟 12/3, 离间 / 拉拢 6–15/2) + 政绩 3 per month for each governor (+1 at order ≥ 75), + 3 to a project supervisor, + 2 in a city with a 书院. **Cap 20 per general per month.** Story exp (events, missions, 文会, caravans, expeditions, mentors) outside the cap. **老练 (battle veterancy) counts non-battle exp at half** (`vetLevel`, §4.4) |
| Events | 33 kinds (nature, society, court, people, global), 9 regional variants (飓风, 马贼, 海寇 …), 7 named sage personas (5 with unique outcomes), 5 dated historical seeds. One draw per faction per month, p = min(0.55, 0.28 + 0.025 × cities): **4.2 per faction-year** classic, 3.1 world. AI resolves them with the same rules. Net value for the AI ≈ +0.3 … +0.8% of gross gold income |
| 国策 missions | Player only. 3 slots; 2 offers every 3 months. 17 templates × 3 tiers; deadlines 6–24 months; reward 0.4 / 0.8 / 1.2 months of faction income, prestige 10 / 20 / 35, exp 20 / 35 / 50, item chance (tier 2: 25% uncommon; tier 3: 60% uncommon, 15% rare). No penalty for failure |
| 营建 projects | 13 projects (水利, 屯田, 市集, 驿道, 书院, 演武场, 城墙, 祠庙, 常平仓, 冶铁工坊, 港口, 烽燧, 奇观), 250–2 000 gold, 60–300 work (progress 10 + 监工政治/8 per month → 4–24 months), one at a time per city, permanent, visible in the city view (scaffold → finished model), +10 prestige each (奇观 +50 and +5 per month). City level-ups (邑 → 都会) +10 × level prestige |
| 商队 caravans | 1 token, a leader and 100–800 gold; destination ≤ 6 hops through friendly or neutral land; margin 0.04 + 0.02/hop + 0.05 foreign culture + 0.02/market tier + (政治 − 50)/800 (+ market, trade pact), cap 0.30; 2–4 months; loss risk 2%/hop scaled by escort and order. Brings home items 35–65% of trips |
| 外交 | Relation −100 … +100 per pair (drifts to 0: one step per 2 months inside ±30, per 3 months beyond). New: 赠礼, 联姻 (48-month pact), 纳贡 / 索贡 (12-month pact), 质子, 停战 (12 months), 通商协定 (24 months), 归附 (a small neighbour joins peacefully). Pacts are non-aggression for the AI. Relation ≥ 60 also stops AI attacks. **Bad relations never make the AI more aggressive** (tested and rejected, §19.2) |
| 羁绊 bonds | 结义, 亲族, 夫妻, 知己, 师徒, 宿敌, 不和. 42 historical bonds at start (刘关张 结义, 孟获–祝融, 甘宁–凌统 不和 …). Same-army effects ±4%, loyalty protection, mentor +4 exp per month. Oath at affinity ≥ 70 |
| 探索 expeditions | 1 token, a leader, 100 + 25 × hops gold; 44 named sites (16 in the classic map) (泰山, 昆仑, 楼兰故城, 犍陀罗佛塔, 波斯波利斯, 吉萨金字塔, 德尔斐, 条顿堡森林 …); 2–6 months; items (rare 2.5–6%), exp 30 + 10 × hops, prestige, treasure-map clues for ITEMS hidden items, mishaps 4–15% |
| 文会 | Month 9, every faction sends up to 3 scholars; 3 rounds (经义, 辞赋, 策论 with the player's optional 论辩 mini-game). Prizes: 1st a rare book / instrument + 300 gold + 50 prestige + fame +1; 2nd uncommon + 150 + 25; 3rd common + 80 + 12; all entrants 20 exp |
| Festivals | One culture festival per year (腊祭·大傩, 东盟, 茏城大会, 行像, 因陀罗节, 诺鲁孜, 农神节 …, 12 families, sources graded A / B / C). 盛大 (30 + 15 × cities gold, ≤ 400): all cities order +6, capital loyalty +3, prestige +10 (×1.5 with a temple); 从简: a third of the cost, +2 / +3; 免: nothing. Optional 投壶 mini-game |
| 功业 achievements | 48 in 8 groups, each +5 … +80 prestige (a few +1 fame or virtue). The scripted engaged player unlocks 0.5–1.9 a year: 18–21 by year 20 (classic), 14 by year 15 (world) |
| Victory | 霸业 (unify, v3). **王道**: prestige ≥ 1 500, average order ≥ 75, two 大郡 or one 都会, every neighbour at relation ≥ 30 or under a pact (and ≥ 4 factions), no attack started for 24 months, all held 6 months. 天命 (classic): 传国玉玺 + 洛阳 or 长安 + prestige 2 000. 万国来朝 (world): pacts with 12 factions in 5 regions + 8 000 trade profit + prestige 2 000. 天下榜 score and an optional 30-year score game |
| Balance (all-AI, §19) | Classic 2 × 48 seeds × 25 y: factions alive at year 10 off 4.9 / 4.3 → on 5.0 / 4.9; median time to the first faction > 25% of cities 2.3 / 2.1 y → 1.9 / 2.2 y. World 24 seeds × 15 y: alive at year 15 44.3 → 43.5 (44.0 with AI tribute). Defection cascades 11 → 9 of 96 games |
| Balance (scripted peaceful player, §19) | Classic 2 × 36 games × 20 y: survival 81–89% (v3 domestic-only 83%); prestige 715–866 at year 10, 2 400–3 135 at year 20; 王道 in 22–25% of runs (median 11–13 y). World 24 × 15 y: survival 100% (v3 92%), 3.0 cities at year 15 (v3 1.9), 王道 12.5% (median 12.7 y). Engagement income (missions, caravans, prizes, events, expeditions) 25–43% of the base income, spending (gifts, projects, festivals, supplies, dowries) 35–42%: the systems roughly pay for themselves and turn gold into cities, levels, items, prestige and a victory path |
| RNG | Every rule here draws from its own seeded streams (`SG.SeededRandom(hash(G.seed, monthIndex, tag, …))`), never from `SG.Random`. With `SG.Engage.enabled = false` every v3 / v4 output is identical |

---

## 1. Goals, the reward loop and the rules against busywork

### 1.1 Goals

1. **Every month offers a decision that is not war.** A flood to answer, a caravan to send, a festival to fund, a scholar to send to the 文会, a marriage to arrange, a temple half built.
2. **Peace is a real strategy.** A player can grow (development, 营建, trade, 归附, 献城), defend (walls, beacons, pacts, tribute, truces) and win (王道, 天命, 万国来朝, the score) without starting a war.
3. **Rewards are visible.** The city view grows buildings and crowds; generals level up and get titles; items arrive with a reveal; seals stamp the 功业录; the year review shows the year's story.
4. **No chores.** The rules in §1.3 are testable and tested (§22).
5. **One world for everyone.** The AI lives under the same 民心, events, relations and bonds, so the world stays coherent and the balance is measured, not assumed (§18, §19).

### 1.2 The reward loop

```
 month start ─► news ─► [event card ≤1] ─► [scheduled card ≤1: festival / 文会 / tournament / AI proposal]
      │                                                     │
      ▼                                                     ▼
 commands (tokens): 开发 征兵 训练 搜索 登用 移动 输送 外交* 出征 赏赐 交易 任命 市场 授官 营建 通商 探索
      │      every command: vignette (DOMESTIC-FX) + domestic exp (§4) + mission progress (§7)
      ▼
 end month ─► income (× 民心) ─► projects / caravans / expeditions tick ─► missions pay ─► achievements stamp
      │
      ▼
 year: 正旦 年鉴 + 武林大会 (1) · 庙会 (3) · 秋收 (7) · 文会 (9) · culture festival (its month)
      │
      ▼
 long goals: city levels 邑 → 都会 · titles 九品 → 一品 · 图鉴 · 功业录 · 天下榜 · 王道 / 天命 / 万国来朝
```

Short loop (minutes): a card, a command, a vignette, a number going up. Middle loop (a season, 3–6 months): a project finishing, a caravan coming home, a mission paying, a festival. Long loop (years): city levels, titles, the 文会 crown, bonds, achievements, a victory path.

### 1.3 Rules against busywork (each is checked by `tests/engage.js` §6)

| # | Rule |
|---|---|
| R1 | **Interruption budget**: at most 1 random event card and 1 scheduled card per month, outside battles and the player's own commands. Overflow becomes a line in the month news and takes the recommended option |
| R2 | Every card has a **★ recommended** option (= what the AI would choose, §6.5) and a forecast line ("预计：粮 −264、民心 +8、声望 +4"). Setting 「内政委任」 (on by default for minor cards) answers minor cards with ★ and lists them in the news; chains, deserters, 归附, oaths, marriages and demands always ask |
| R3 | **No upkeep**: projects, caravans and expeditions run by themselves; missions track and pay automatically; festivals remember 「以后照此办理」 |
| R4 | **No punishment for ignoring**: missions expire silently; achievements never decay; an unanswered card (closed with ×) takes the safe free option, never the worst one |
| R5 | **Tokens only for commands**: answering cards, festivals, 文会 picks and claiming rewards cost no token. New commands (营建, 通商, 探索, 外交 sub-actions) cost 1 token like the v3 commands |
| R6 | **Repetition limits**: per-event cooldowns 6–60 months per city or faction; at most one disaster per city per 12 months; the same event never twice for a faction within 6 months |
| R7 | **Short**: every card is ≤ 3 taps; every pull screen closes with one tap or Esc; every animation is skippable (DOMESTIC-FX §3.5) |
| R8 | **Pull, not push**: the 国策 board, 营建 list, 商队 list, 功业录, 人物关系 and 天下榜 are screens with badges; nothing opens them by itself except the yearly 年鉴 |

Measured with the scripted engaged player (§19.3): about half of all months show **no card at all** (53–54%); ≈ 3 random cards and ≈ 3.4 scheduled cards (festival, 文会, tournament, AI offers and demands, 归附) a year; the 1 + 1 cap deferred something in ≈ 2% of months; no month showed more than two cards.

---

## 2. Files, modules and ownership

| File | Status | Content |
|---|---|---|
| `design-v4/engagement.json` | new (this task) | catalogue; single source of truth |
| `tools/gen-engage.js` | new | port of `engage/build-json.js`: modes `check`, `js` (writes `js/engage-data.js`), `cs` (Unity `EngageData.g.cs`) |
| `js/engage-data.js` | new, generated | `SG.EngageData = { events, historical, sages, omens, proposals, missions, missionReward, projects, sites, siteTypes, festivals, festivalRule, achievements, bonds, relations, family, river }` (≈ 60 KB) |
| `js/engage.js` | new | `SG.Engage`: state, 民心, 声望, relations and pacts, bonds, the event engine (draw, resolve, effects), festivals, 文会 scoring, missions, projects, caravans, expeditions, achievements, victory and score, AI hooks. **Pure logic, no `THREE`, no DOM**, Node-testable like `model.js`. Port of `engage/engage-proto.js` §§ 状态 … 成就 |
| `js/engage-screen.js` | new | DOM: event cards, 国策 board, 营建 / 通商 / 探索 dialogs, 外交 screen, 人物关系 graph, 文会 hall and 论辩 mini-game, 投壶, 功业录, 年鉴, 天下榜, top-bar widgets. Registers vignettes with `SG.DomesticFx.define` for the new commands (§21) |
| `tests/engage.js` | new | Node: catalogue, rules, RNG contract, anti-busywork rules, campaign and player balance (§22) |
| `tests/engage.html` | new | every card, screen and mini-game against scripted states; phone frame |
| `js/model.js` | modified | `Commands` hooks (domestic exp, recruit order, `allyChance` relation term), `GameState.generalsIn` skips away generals, `Progress.vetLevel`, save fields (§20) |
| `js/strategy-ai.js` | modified | `endMonth` calls `SG.Engage.monthEnd(news)` after income; `attackNeed` adds `SG.Engage.needK(f, t)`; AI target filter reads `g.allied` (which now includes pacts); conquest hooks |
| `js/battle-model.js` | modified | bond effects (§11.3) in `atkPower` / `defFactor` / morale; `Progress.vetK` uses `vetLevel` |
| `js/duel-game.js` | modified | rival bonus (+4% damage between 宿敌), friendly sparring mode (no consequences) |
| `js/strategy-screen.js` | modified | month-start event phase, new commands, top-bar widgets, year review |
| `js/game.js` | modified | title screen: the cross-game medal shelf (§14.4); new-game option 「三十年之约」 (score game) |
| `index.html` | modified | load `engage-data.js`, `engage.js` after `model.js`; `engage-screen.js` after `strategy-screen.js` |

Parallel work rule (DESIGN-V2 §3) still holds: each task edits only its own files and lists the rest under INTEGRATION (§21).

### 2.1 `SG.Engage` API (pure logic)

```
enabled, version
init(G) · migrate(G)                                   new game · v3 save (§20)
monthEnd(news) → { harvests, stageUps, sealed: [achId], cardsQueued }      called from StrategyAI.endMonth (§21)
monthStartCards(f) → [card]                            ≤ 1 random + ≤ 1 scheduled (R1); card = { kind, id, city, actors, options: [{ id, label, cost, forecast, chance, rec }] }
resolve(card, optionId, agentId?) → { effects: [{ k, from, to }], roll, tier }    applies the effects; tier feeds DOMESTIC-FX
onCommand(cmd, lastResult)                              domestic exp, order (征兵), counters
order(c) · orderTarget(c) · prestige(f) · prestigeName(f) · fameBonus(f) · virtueBonus(f)
rel(a, b) · addRel(a, b, d) · atWar(a, b) · pacts(f) · pactOf(a, b, kind?) · noAttack(a, b) · needK(f, t)
chance(kind, f, t, opts) · propose(kind, f, t, opts) → { ok, text, pact }       kind ∈ gift ally marriage tribute demand truce trade
bond(a, b) · bondsOf(gen) · addAff(a, b, d) · battleBonus(unit, model) → { atk, def }
buildable(c) → [{ id, ok, why, months }] · startProject(c, id, supId) · project(c)
caravanTargets(c, leader) → [{ city, hops, margin, risk, months, expected }] · sendCaravan(c, leader, city, invest) · caravans(f)
sites(f, c) → [{ site, hops, months, cost, ok, why }] · sendExpedition(c, leader, siteId) · expeditions(f)
missionOffers(f) · acceptMission(f, offerId) · cancelMission(f, id) · missions(f)
wenhuiEntrants(f) · setWenhuiEntrants(f, ids) · debate(state, stance) → state     (mini-game kernel, deterministic)
achievements(f) → [{ id, done, month }] · victory(f) → { wangdao, tianming, wanguo } · score(f) · leaderboard()
vetLevel(gen)
```

All functions are deterministic for a given state and month (their rolls come from `rng(tag, …)`), so the screens can show exact chances and the tests can replay months.

---

## 3. New core state

### 3.1 民心 (city order)

`c.order`, integer 0–100, saved.

```
start(c)    = clamp(round(45 + virtue(owner)/5 + c.town/150), 30, 75)        // empty city: 45
target(c)   = clamp(45 + effVirtue(owner)/5 + Σ project.orderTarget + Σ buff orderTarget + (admin(c).pol ≥ 80 ? 3 : 0), 0, 95)
monthly     : order < target → order = min(target, order + 2);  order > target → order − 1
recruit n   : order −= ceil(n / 3000)                               // 征兵 3 000 → −1
conquered   : order = max(15, order − 15);   siege repelled: order −= 3
income      : c.gold += round(baseIncome × (order/1000 − 0.05))     // ±5 %, 50 = neutral
harvest     : + round(land × FoodPerLand × (order − 50)/1000)       // ±5 %
population  : natural growth × (0.5 + order/100)                    // ×0.5 … ×1.5
```

- `admin(c)` is the TITLES city administrator (governor, or the best 文官 when the ruler governs). Project bonuses (§8.2): 水利 +2, 驿道 +3, 祠庙 +8, 奇观 +5 (capital).
- Events and festivals add or subtract directly (§6, §5.3). Every change is shown in the city panel (a 民心 bar with a lantern icon; amber below 40, red below 25) and in the city view (CITY-VIEW: crowd density and mood, lanterns lit at night above 60, shuttered stalls below 30).
- Measured (§19.1): AI cities average 33–39 in the war-heavy first five years, 51–54 at year 10 and 61–62 at year 25 (67–69 with AI projects); the net income effect for the AI is within ±0.1% (zero-sum by design). A peaceful player's cities climb from 62 to 81–84 by year 20.

### 3.2 声望 (prestige)

`faction.prestige`, integer ≥ 0, saved. It is the score of renown. It never decays and is never spent.

| Source | Prestige |
|---|---|
| Conquest of a city held by a faction | +5 (+20 more when that faction is destroyed) |
| Siege repelled when the attackers had ≥ the defenders' troops | +2 |
| A city reaches a level for the first time (§8.5: 邑 1, 县城 2, 郡城 3, 大郡 4, 都会 5) | +10 × (level − 1): +10 / +20 / +30 / +40 |
| Project completed / 奇观 completed / 奇观 standing | +10 / +50 / +5 per month |
| Festival 盛大 / 从简 (×1.5 with a 祠庙 or 奇观) | +10 / +3 |
| 文会 1st / 2nd / 3rd; host | +50 / +25 / +12; +20 |
| 武林大会 1st / 2nd / 3rd | +50 / +25 / +12 (proposal to the tournament spec) |
| Mission tier 1 / 2 / 3 | +10 / +20 / +35 |
| Achievement | +5 … +80 (§14) |
| Alliance / truce / marriage concluded | +5 / +5 / +20 (both sides for a marriage) |
| Tribute received | +1 per month paid |
| 归附 (a faction joins you) | +30 |
| Events | per card, −5 … +25 (§6.3) |
| Breaking a pact by attacking its partner | −20 (marriage −50) |

**What prestige does**

```
fameBonus(f)  = min(10, floor(prestige / 150) + eventFame(f))         // eventFame: +1 cards, 文会 crown, missions tier 3
effFame(f)    = clamp(f.fame + fameBonus(f) + gearFame(f), 0, 100)   // gearFame: ITEMS §3.5 (≤ +20)
effVirtue(f)  = clamp(f.virtue + clamp(eventVirtue(f), −10, 10) + gearVirtue(f), 0, 100)
```

- `effFame` / `effVirtue` replace `faction.fame` / `virtue` everywhere v3 reads them (battle action points `2 + fame/20`, 搜索, 登用, 外交, 出奔), through the same accessors ITEMS introduces (`SG.Items.fame(f)` / `virtue(f)`; this spec adds its bonus inside them). The stored fields never change. With the cap of +10, prestige adds at most one battle action point to a faction.
- Gift and diplomacy: relation gains × min(1.5, 1 + prestige/2000); marriage chance + min(0.10, prestige/5000); 归附 chance + prestige/40 000.
- Unlocks: 远方来献 needs 250, 祥瑞 needs 100; victory thresholds (§15).
- Names (shown under the ruler's portrait in the top bar): 无闻 < 100 · 小有名气 100 · 闻名州郡 300 · 名动一方 600 · 天下闻名 1 000 · 四海归心 1 500 · 万世景仰 3 000.

Measured (§19): the average surviving AI faction has ≈ 525–570 at year 5, 1 175–1 230 at year 10 and 2 520–2 765 at year 25 in the classic scenario (conquest heavy), ≈ 273 / 546 / 751 at years 5 / 10 / 15 in the world (many small factions). A scripted peaceful player has ≈ 220–320 at year 5, 715–1 145 at year 10, 1 900 at year 15 (world) and 2 400–3 135 at year 20 (classic).

### 3.3 邦交 (relations) and pacts

`G.rel` is an `Int8Array` of n × n (symmetric; 70 × 70 = 4.9 KB in the world), values −100 … +100. `G.pacts` is a list of `{ a, b, kind, since, until, payer?, pay?, hostage? }`.

| Change | Relation |
|---|---|
| Start | 0, except the 30 historical pairs of `engagement.json` `relations` (董卓 vs every 中原 lord −40 for the coalition; 刘备–公孙瓒 +40, classmates under 卢植; 孙坚–刘表 −30; 袁绍–袁术 −30; 罗马–安息 −40 after the war of 161–166; 邪马台–狗奴 −50 …) |
| Attack (Conquest.prepare) | −25 between the two, and war memory: `atWar(a, b)` for 12 months |
| City conquered from b | −15 more |
| 离间 / 拉拢 against b | −5 / −8 |
| Gift (§10.2) | + min(25, gold / (8 + 2 × cities(b)) × envoyK × prestigeK) |
| Alliance concluded | +10; 停战 +15; 联姻 +30; 纳贡 +10; 通商协定 +5 |
| Caravan arriving safely | +3 |
| Events | `rel` effects (§6) |
| Drift | −30 … +30: one step toward 0 every second month; above +30: one step toward +30 every third month; below −30: one step toward −30 every third month (so a gift lasts, and friendship above +30 is cheap to keep) |
| Pact floors while active | 联姻 50, 同盟 30, 纳贡 / 停战 / 通商 20 |

Effects:

```
allyChance(f, t, gift)   = v3 + rel(f, t)/400                         // −0.25 … +0.25, clamp 0.05–0.9 as v3
noAttack(a, b)           = allied(a, b) || pact(a, b) ∈ { 联姻, 纳贡, 停战 } || rel(a, b) ≥ 60
```

- The AI's target filter (`actCity`) and `recheckBattle` use `g.allied(a, b) || Engage.noAttack(a, b)`, so friendly and pact partners are never attacked by the AI. `GameState.allied` itself keeps its v3 meaning (the alliance matrix), so the player's 出征 list still shows pact partners, marked 「背约」 with a confirmation that states the cost (§10.10). (The prototype patched `allied` directly; the effect on the AI is the same.)
- **Rejected**: making the AI more eager against bad relations (`attackNeed − 0.2` at relation ≤ −50) halved the time to the first dominant faction in all-AI runs (§19.2). Bad relations only withhold friendship. The one exception is a player who refuses a tribute demand (§10.5): that AI needs 0.15 less troop ratio to attack the player for 12 months.

### 3.4 羁绊 (relationships between generals)

`G.bonds`: a map `'idA-idB' → { k: kind | null, a: affinity −100 … 100, m: mentorId }` (sparse; only pairs that met).

| Kind | How it forms | Effects |
|---|---|---|
| 结义 sworn | the 结义 card when affinity ≥ 70 (max 3 per group, §11.2); 刘关张 at start | same army: attack and defence +4% each; one routed → the others morale +10 and attack +6% for the rest of the battle (义愤); defection check uses loyalty + 30; enemy 拉拢 × 0.5; if one is persuaded away, the others −15 loyalty and 25% follow |
| 亲族 kin | data only (父子, 兄弟, 叔侄 …) | defection check + 15; enemy 拉拢 × 0.7 |
| 夫妻 spouse | data only (孟获–祝融 …) | as 知己 × 1.5; defection + 15 |
| 知己 friend | affinity ≥ 50 (automatic, toast) | same city: loyalty +1 per month up to 90; same army: attack +2% |
| 师徒 mentor | the 拜师 card (mentor best attribute ≥ 80 and level ≥ 5; student ≤ mentor − 3 levels) | same city: student +4 exp per month (story exp); student battle exp +20% in the mentor's army; once a year 25% chance the student may learn the mentor's newest learnable special / tactic if he has a slot and meets its requirement (`Progress.learn`) |
| 宿敌 rival | the 切磋 card between near-equal ≥ 85 warriors (30%), being passed over for a title (TITLES hook, affinity −10), data | same faction: both +15% battle exp; same army: attack +3%; a higher title for one → the other −5 loyalty. Opposite sides: duels always accepted between them, +4% duel damage |
| 不和 feud | affinity ≤ −50; data (甘宁–凌统, 程普–周瑜 at start …) | same army: attack and defence −4%; same city: loyalty −1 per month while above 60 (never pushes anyone below 60 by itself); slander cards |

Affinity changes: +1 per month while two officers of one faction share a city (up to 60); +4 per pair when they win a battle in the same army; event effects (±5 … ±20); −0.5 per month apart (unnamed bonds only). A 知己 bond drops back below 30, a 不和 above −30.

---

## 4. Domestic experience (answers TITLES §11, Q1 and Q10)

### 4.1 Routine exp (cap 20 per general per month, AI included)

| Source | Exp to |
|---|---|
| 开发 | the presiding general 6 (+2 if the gain ≥ 20) |
| 征兵 / 训练 | the general whose troops 3 |
| 搜索 | the searcher 4; 12 if a person or treasure is found |
| 登用 | the recruiter 10 on success, 2 on failure |
| 外交 (同盟, 联姻, 纳贡, 停战, 通商, 赠礼) | the envoy 12 on success, 3 on failure |
| 离间 / 拉拢 | the agent 6 / 15 on success, 2 on failure |
| **政绩** (each month, `StrategyAI.endMonth`) | each governor 3 if the city's order ≥ 50, +1 more at ≥ 75 (+ title tier when TITLES is loaded, TITLES Q10) |
| 营建 supervisor | 3 per month while present |
| 书院 | every officer with 智力 ≥ 60 in the city 2 per month |
| 输送 / 移动 / 交易 / 赏赐 / 任命 / 市场 | 0 |

### 4.2 Story exp (outside the cap)

Event effects (`exp` 4–60), missions 20 / 35 / 50, 文会 (20 to every entrant; +80 / +50 / +35 to the top three), caravans (leader 10 + 4 × hops, 5 when lost), expeditions (30 + 10 × hops), mentor (+4 per month to the student), 游学 (+50 on return).

### 4.3 Pace

A busy governor reaches level 3 in about 10 months, level 5 in about 2.5 years and level 7 in about 4.5 years (TITLES §11). Measured in all-AI classic campaigns: the average level at year 10 is 3.8 (v3: 3.0), level 7+ generals 9 (v3: 1); at year 25 5.3 (v3: 3.3). The scripted peaceful player's officers average level 3.5 (classic) / 4.9 (world) at year 10 and 5.8–5.9 at year 20 (classic; world 6.3 at year 15), without fighting (v3 domestic-only: 1.1).

### 4.4 老练 counts non-battle exp at half

Levels from domestic work unlock titles and learning slots exactly as battle levels do. In battle, though, 老练 (+4% attack, defence and tactic power per level, DESIGN-V3 §4) should reward fighting. So each general keeps `dexp` (exp gained outside battles, saved), and:

```
vetLevel(gen) = Progress.levelFor(gen.exp − 0.5 × gen.dexp)
Progress.vetK(gen) = 1 + VET_PER_LEVEL × (min(vetLevel(gen), MAX_LEVEL) − 1)
```

Measured: the average 老练 multiplier of all AI generals at year 10 is ×1.095 with this rule against ×1.110 with full weight and ×1.079 in v3 (year 25: ×1.137 / ×1.171 / ×1.090). Tests (7g) / (7h) of `tests/sim.js` re-run with domestic exp on (§22.2).

---

## 5. Calendar and month flow

### 5.1 Month phases (player)

1. `StrategyAI.endMonth` (income × 民心, harvest, projects / caravans / expeditions tick, missions pay, AI events resolved, festival / 文会 / tournament when due) → the month news (DOMESTIC-FX §9 harvest montage, stage-ups).
2. **Achievements** stamp in the news panel (seal animation, ≤ 3 shown, the rest as a line).
3. **Event card** for the player's faction, if drawn (§6.1).
4. **Scheduled card** if due, in priority order: 归附 offer > tribute demand > 武林大会 (month 1) > 文会 (month 9) > culture festival > AI alliance / marriage / trade offer > mission offer badge (never a card). Lower ones wait a month or become news lines (R1).
5. Month banner (`令牌 N 枚`), commands.

The AI's events, festivals and diplomacy are resolved inside `endMonth` with the same rules (§6.5, §18).

### 5.2 The year

| Month | What | Owner |
|---|---|---|
| 1 | 正旦·年鉴 (year review, §16.4) and 武林大会 | this spec / tournament spec |
| 3 | 庙会 (all own markets +2 slots, −10% prices) | ITEMS §14.2 |
| 7 | 秋收 montage | DOMESTIC-FX §9.1 |
| 9 | 文会 (§13) | this spec |
| festival month | the faction's culture festival (§5.3) | this spec |
| any | events, missions, projects, caravans, expeditions | this spec |

### 5.3 Festivals

Each faction has **one signature festival** a year, by the city family of its culture (`engagement.json` `festivals`, with faction overrides). Grades: **A** attested for that people near 190 AD; **B** right culture, other period or region (the detail page says so); **C** designed to fill a gap (marked 「设计补足」 in the card's footnote).

| Family (cultures) | Festival | Month | Grade | Source note |
|---|---|---|---|---|
| hua (汉, 南中) | 腊祭·大傩 | 12 | A | 续汉书·礼仪志: the day before 腊, the great exorcism 大傩 drives out pestilence; 腊 sacrifice to the hundred gods |
| han3 (高句丽, 濊, 马韩 …) | 东盟 (夫余: 迎鼓 12; 百济: 五月祭 B) | 10 | A | 三国志·东夷传: 高句丽 "以十月祭天，国中大会，名曰东盟"; 濊 舞天 in the tenth month; 夫余 迎鼓 |
| steppe (匈奴, 乌桓, 鲜卑, 萨尔马提亚) | 茏城大会 (鲜卑: 饶乐水大会, month 3) | 5 | A | 史记·匈奴列传: the first-month gathering at the 单于庭, the great gathering at 茏城 in the fifth month (ancestors, heaven and earth, spirits), autumn census at 蹛林; 后汉书·乌桓鲜卑列传 for the 鲜卑 spring gathering on the 饶乐水 |
| wa (倭) | 祈年祭 | 2 | C | no festival is named in 魏志倭人传; a planting rite |
| oasis (西域) | 行像 | 4 | B | 法显 on 于阗 (5th century): image processions from the first of the fourth month |
| indic (天竺, 贵霜) | 因陀罗节 (贵霜, 西总督: 佛诞节 B) | 4 | B | Silappatikaram and Manimekalai: the 28-day Indra festival of Puhar in Cittirai (texts c. 3rd century or later); 森古图万 of the 哲罗 is the epic's patron king |
| persia (安息, 波西斯 …) | 诺鲁孜 | 3 | B | Nowruz with Arsacid-era references (Vologases I) and Sasanian practice; Mehragan in autumn is the alternative |
| arabia | 贝勒大祭 | 4 | B | the Palmyrene cult of Bel (temple dedicated AD 32); Nisan festivals |
| nile (库施, 阿克苏姆) | 涨水节 | 8 | C | the Nile flood and the nilometer |
| roman | 农神节 Saturnalia | 12 | A | 17 December onward, 7 days, small gifts (sigillaria), masters serve slaves (Macrobius) |
| north (凯尔特, 日耳曼) | 萨莫尼奥斯月 (日耳曼: 大地母神巡游, month 4) | 11 | B | the Coligny calendar (bronze, late 2nd century) names Samonios; Tacitus, Germania 40: while Nerthus' wagon travels, no one goes to war and iron is locked away |
| nanhai (林邑, 扶南, 夷洲) | 水节 | 4 | C | a monsoon-eve water and boat festival |

Card (scheduled, R1): three options and a checkbox 「以后照此办理」:

| Option | Cost (capital's gold) | Effect |
|---|---|---|
| 盛大 | 30 + 15 × cities, max 400 | every city order +6 (×1.5 with a 祠庙 in it); non-ruler officers in the capital loyalty +3; prestige +10 (×1.5 with a 祠庙 or 奇观); the optional 投壶 mini-game (§13.3) for up to +2 more loyalty to 3 generals; achievement count |
| 从简 | a third | order +2, prestige +3 |
| 免 | — | nothing (no penalty, R4) |

The Wa, Nile and Nanhai festivals keep the same numbers; only the art and the music sting differ. Presentation: the capital switches to its festival dressing in the city view (lantern strings and banners for 腊祭, fire bowls for 诺鲁孜, flower garlands for 因陀罗节, wax-figure stalls and torch processions for Saturnalia, oak wreaths for 萨莫尼奥斯, a drum circle for 东盟 …); the sting is the culture family's festival variant (music task). AI: 盛大 when the capital has ≥ 3 × the cost, else 从简 when it keeps its reserve (§6.5), else 免.

The universal 正旦 (month 1) is not a festival card; it is the year review (§16.4).

---

## 6. Events

### 6.1 Engine

```
monthEnd, for each living faction f (after income, before festivals):
  k = rng('evt', f.id)                                  // SG.SeededRandom(hash(seed, monthIndex, 'evt', f.id))
  if k() ≥ min(0.55, 0.28 + 0.025 × cities(f)) → no event
  candidates = for each event e (not global):
      scope city:   for each own city c with cooldown(c, e) passed, no disaster in c within 12 months if e is a disaster, when(e, c) true
                    → weight(e, c)
      other scopes: once, at the capital, if cooldown(f, e) passed and when(e, capital) true
                    → e.w × (needPair ? 0.5 : 1) × max(1, √cities(f))
  pick one by weight (k); resolve it (§6.2)
global events (彗星 / 日食): k = rng('global'); p = 1/40 per month; every faction resolves it
historical seeds (§6.6): on their date, each listed city owned by someone fires with its p
```

```
weight(e, c) = e.w
             × (e.wOrder == 'low'  ? 1.6 − order/100 : 1)        // bandits
             × (e.wOrder == 'high' ? order/65 : 1)               // volunteers
             × (e.wPop ? clamp(population/400 000, 0.3, 3) : 1)  // plague
             × (e.wTown ? town/600 : 1)                          // fire
             × (e.wVirtue ? 1 + virtue/100 : 1)                  // sages
             × Π buff 'eventW:<id>' (堤防 ×0.3 for floods)
             × Π project disasterK (水利: flood, drought ×0.4)
```

- **Conditions** (`when`): months, city families (in and out), latitude (locusts north of 28° in China), river / coast (CITY-VIEW flags), minimum land / town / population / order / market tier, faction virtue / fame / prestige / cities, the governor's 政治, a hidden talent in the city, a neighbour in trouble (war or disaster within 2 months), the average order of the faction, a far faction of another culture. Pair and general events need a fitting pair or general (§11.2).
- Cooldown per (city, event) or (faction, event): 6–60 months (`cd`).
- **Effects** use the declarative vocabulary in the header of `engage/catalog.js` (`add`, `pct`, `inc` = × the city's monthly income, `harv` = × the city's expected harvest, `range`). City keys, faction keys (prestige, fame, virtue), general keys (exp, loyalty, away, troops), buffs with a duration or "until the harvest", relations, bonds, reveal (hidden talents), items (ITEMS pools, §12.3), flags (plague), chains.
- Card text is written once per event with placeholders (`{city}`, `{gov}`, `{ruler}`, `{persona}`, `{a}`, `{b}` …) and per-family variants (`variants`: 飓风 instead of 水灾 in the south seas, 马贼 on the steppe, 海寇 in the north …).

An entry of `engagement.json` (the flood, abbreviated):

```json
{ "id": "flood", "name": "水灾", "cat": "nature", "scope": "city", "icon": "wave-breach", "w": 6, "cd": 24, "disaster": true,
  "when": { "months": [5, 6, 7, 8, 9], "riverOrCoast": true, "minLand": 250, "notFamilies": ["nile"] },
  "variants": { "nanhai": { "name": "飓风", "text": "海上飓风登岸，咸潮倒灌田庐。" } },
  "text": "连日暴雨，{city}外河水决堤，田庐漂没。",
  "pre": [{ "k": "land", "pct": -0.05 }, { "k": "food", "pct": -0.08 }, { "k": "population", "pct": -0.01 }, { "k": "order", "add": -5 }],
  "options": [
    { "id": "relief", "label": "开仓赈济", "agent": "gov", "cost": [{ "k": "food", "harv": -0.12 }],
      "effects": [{ "k": "order", "add": 8 }, { "k": "virtue", "add": 1 }, { "k": "prestige", "add": 4 }, { "k": "exp", "add": 8 }, { "k": "population", "pct": 0.005 }],
      "ai": 2, "aiIf": { "foodOverHarv": 0.5 } },
    { "id": "levee", "label": "征发民夫，修筑堤防", "agent": "gov", "cost": [{ "k": "gold", "inc": -2 }],
      "effects": [{ "k": "land", "pct": 0.03 }, { "k": "buff", "mod": "eventW:flood", "v": 0.3, "months": 36 }, { "k": "order", "add": -2 }, { "k": "exp", "add": 6 }], "ai": 1, "aiIf": { "goldOverInc": 4 } },
    { "id": "none", "label": "听之任之", "effects": [{ "k": "order", "add": -3 }], "ai": 0.5 } ] }
```

### 6.2 Resolution

```
pre effects (the disaster itself) → option chosen (player card or AI) → cost → roll (if any) → ok / fail effects → option effects
roll p = clamp(base + Σ stat × coef of the option's agent + virtue × coef, 0.05, cap ≤ 0.95)
agent: gov (the city's administrator) · ruler · best of the city by 武力 (+ troops) / 智力 / 政治 / max(智, 政) · the event's subject
```

An option is offered only if its cost fits the city's gold and food. For the AI (and for ★ in the player's card), the city must also keep a reserve of 60 + 6 × officers gold and two months of its troops' food (without it, AI cities spent down to 0 and triggered the v1 rule "gold < 0 → every officer −3 loyalty", starting defection cascades; §19.2). An option that would send away the last officer of a city, or an officer of a threatened city, is not offered (without this the world scenario lost 3.5 factions by year 15).

### 6.3 Catalogue

`w` = base weight, `cd` = cooldown (months). ★ = the AI's usual choice. Amounts: `inc` = × city monthly income, `harv` = × expected harvest.

| Event | Cat | When | w / cd | Disaster (pre) | Options (★ = AI, condition) |
|---|---|---|---|---|---|
| 水灾 flood (飓风 南海, 春汛 草原) | nature | months 5–9, river or coast, land ≥ 250 | 6 / 24 | land −5%, food −8%, pop −1%, order −5 | ★开仓赈济 (food −0.12 harv; order +8, 德 +1, prestige +4, exp 8, pop +0.5%; ★ if food > 0.5 harv) · 修筑堤防 (gold −2 inc; land +3%, flood weight ×0.3 for 36 months, order −2) · 听之任之 (order −3) |
| 旱灾 drought | nature | months 4–7, not steppe / Nile | 5 / 24 | harvest −20% this year, order −3 | 开渠引水 (gold −3 inc; 政治 roll 0.35 + 政/200: harvest +15% or +5%) · 设坛祈雨 (gold −1 inc; 40%: harvest +10%, order +3, prestige +3) · 减免赋税 (income −30% for 3 months; order +7, 德 +1) |
| 蝗灾 locusts | nature | months 6–8, north of 28° (中原, 草原, 西域, 三韩, 安息, 阿拉伯, 尼罗, 天竺) | 4 / 36 | harvest −30%, food −5% | ★发兵捕蝗 (gold −1 inc, training −5; harvest +20%, order +3; needs 1 000 troops) · 祭祀禳灾 (gold −1 inc; 35%: +15%) · 无可奈何 |
| 疫病 plague | nature | population ≥ 150 000; weight × population | 3 / 36 | pop −3%, troops −3%, order −6; spreads 25% per month to each linked city for 3 months unless it has a 祠庙 | ★延医施药 (gold −2 inc; stops it, pop +1.5%, order +5, 德 +1, prestige +5; half price and more with a healer: 华佗, 张机, 盖伦 persona or a medicine item) · 闭城隔离 (income −50% for 2 months; stops it) · 悬符厌之 (gold −0.5 inc; 30%) |
| 地震 earthquake | nature | any | 1 / 60 | town −3%, defence −6 for 12 months, order −4 | ★发帑修缮 (gold −2 inc; town +4%, defence back, order +3) · 待来年再修 |
| 丰年 bumper | nature | months 6–7, land ≥ 400 | 4 / 24 | harvest +15% | 广积粮 · ★平价粜粮 (gold +1.5 inc, food −0.1 harv) · 庆丰会 (gold −0.5 inc; order +6, prestige +4, officers +2 loyalty) |
| 白灾 dzud | nature | months 12–2, steppe | 5 / 24 | troops −4%, food −10%, order −4 | ★分畜赈济 (food −0.1 harv; order +6, 德 +1, prestige +3) · 南下就食 (gold +2 inc, food +0.08 harv, neighbours relation −10) · 熬过此冬 |
| 尼罗河泛滥 | nature | month 8, Nile family, river | 8 / 11 | roll: 25% low (harvest −20%), 35% normal, 40% high (+15%) | ★献祭河神 (gold −0.5 inc; +7%, order +2) · 听天由命 |
| 瑞雪 (toast) | nature | months 12–1, northern families | 3 / 12 | next harvest +5%, order +1 | — |
| 山贼 bandits (劫道游骑, 马贼, 海寇, 海盗) | society | weight × (1.6 − order/100) | 4 / 18 | — | ★发兵讨伐 (best warrior; roll 0.35 + 武/160 + troops/20 000, cap 0.95: loot +1.5 inc, order +5, exp 15, prestige +2; fail: −8% of his troops, order −2, income −8% for 4 months) · 遣使招安 (gold −2 inc; roll 0.30 + max(智, 政)/200 + 德/300: 400–900 troops join, order +3, and a hidden 武 ≥ 60 / 智 < 60 talent nearby is revealed, e.g. 张燕, 严白虎, 周仓) · 闭城自守 (income −15% for 4 months, order −4) |
| 流民 refugees | society | a linked city had war or disaster within 2 months | 5 / 12 | — | ★安置屯垦 (food −0.08 harv, gold −1 inc; pop +3%, land +10, order +3, 德 +1) · 编为新兵 (600–1 200 troops at training 25, order −2; ★ if threatened) · 闭门不纳 (德 −1) |
| 豪族相争 clans | society | town ≥ 350 | 3 / 24 | — | ★依法裁断 (政治 roll 0.30 + 政/150: order +6, exp 12, prestige +3; fail order −3, governor loyalty −2) · 偏袒大族 (gold +2 inc, order −5, 德 −1) · 设宴调停 (gold −1 inc, order +3) |
| 义士来投 volunteers | society | order ≥ 65; weight × order/65 | 2.5 / 18 | — | ★收为部曲 (300–800 troops at training 45) · 赐金遣归 (order +4, prestige +2) |
| 行商过境 merchant (海舶来港, 商船入港) | society | market tier ≥ 2 | 4 / 12 | — | 看看他们的货物 (opens the ITEMS §6.5 caravan stock; this card is how a 胡商 / 海商 arrives) · ★课以关市之税 (gold +1 inc, origin relation −2) · 盛情款待 (gold −0.5 inc; origin relation +5, caravan margin +0.05 for 12 months) |
| 火灾 fire | society | town ≥ 400; weight × town/600 | 2 / 36 | town −3%, gold −5% | ★赈灾重建 (gold −1.5 inc; town +4.5%, order +3) · 令灾民自行修葺 (order −2) |
| 民谣颂德 (toast) | society | governor 政治 ≥ 75, order ≥ 70 | 2 / 24 | prestige +5, governor exp 10, loyalty +3 | — |
| 游方高士 sage | court | weight × (1 + virtue/100); personas §6.4 | 3 / 18 | — | ★延为上宾 (gold −2 inc; a chosen general exp +40, +20 more if 智 ≥ 70; prestige +5) · 问以天下大计 (prestige +8; reveals a hidden talent within 2 links, else a bonus mission) · 礼送出境 (gold −30, 德 +1) |
| 荐贤 (toast) | court | virtue ≥ 60, hidden talent in the city | 2 / 12 | the talent is revealed | — |
| 天子诏使 | court | classic, fame ≥ 55, ≥ 3 cities, until 219 | 1.5 / 24 | — | ★奉表进贡 (capital gold −3 inc; prestige +25, fame +1, relation +5 with the region; ★ whenever affordable with the reserve) · 婉言谢绝 |
| 彗星 / 日食 (global) | global | 1/40 per month, all factions | — / 36 | — | 下罪己诏 (德 +1, all cities order +2; ★ if 德 ≥ 60) · 祭天禳灾 (capital gold −1 inc; all cities order +3, prestige +2) · 天象不足畏 (all cities order −2) |
| 祥瑞 omen (白鹿, 甘露, 凤凰, 麒麟, 嘉禾; 雄鹰 …) | court | average order ≥ 70, prestige ≥ 100 | 1.5 / 24 | — | ★布告天下 (prestige +20, fame +1, neighbours relation −3) · 谦让不受 (德 +2, prestige +5; ★ if 德 ≥ 70) |
| 降将来投 deserter | court | a neighbouring non-ruler general of a faction at relation ≤ 0, loyalty ≤ 45, 武 or 智 ≥ 60, not alone in his city | 3 / 12 | — | ★纳之 (he joins with half his troops, loyalty 55 + 德/5; his lord −15) · 婉拒 (relation +5) |
| 远方来献 | court | prestige ≥ 250, a faction of another culture ≥ 4 links away | 1.5 / 18 | — | 厚加回赐 (gold −2 inc; an item of their culture, relation +15, prestige +8) · 受礼 (the item, relation +3) |
| 谗言 slander | people | a pair at affinity ≤ −30 | 2 / 12 | — | 查明真相 (ruler 智 roll 0.4 + 智/200) · ★置之不理 (victim −3 loyalty) · 使二人分驻两城 (moves one, no token) |
| 切磋武艺 spar | people | two 武 ≥ 70 non-rulers in one city | 3 / 6 (pair 24) | — | ★击鼓助兴 (both exp +15, affinity +10; 30% rivalry if both ≥ 85 within 6) · 亲临观战 (player only: a friendly duel in the duel game, §13.4) |
| 宴饮 banquet | people | ruler's city | 2 / 6 | — | ★投壶为乐 (gold −0.3 inc; 3 lowest-loyalty officers +2 … +5, affinity +5; optional mini-game) · 饮罢早散 (+1) |
| 求学 study | people | a 智 ≥ 65 general of level ≤ 6 | 2 / 12 | — | ★准其游学 (away 2 months, +50 exp, loyalty +4; not the last officer, not a threatened city) · 不准 (loyalty −3) |
| 思乡 homesick | people | a general of another city family than the ruler's | 1.5 / 24 | — | ★厚赐慰问 (gold −1 inc, loyalty +6) · 准其归省 (away 2 months, +10) · 四海为家 (−5) |
| 求战心切 restless | people | 武 ≥ 80, no battle for 12 months | 2 / 12 | — | 许以先锋 (+3, and +20% exp in his next battle) · ★荐其出战武林大会 (if within 6 months: +5 and he is picked) · 静候时机 (−4) |
| 结义 oath | people | a pair at affinity ≥ 70, neither in a full sworn group | 4 / 12 | — | ★设宴见证 (gold −100; 结义, both +5 loyalty, prestige +5) · 改日再议 |
| 拜师 mentor | people | an eligible pair in one city (§3.4) | 3 / 12 | — | ★行拜师礼 (gold −50; 师徒, student exp +10) · 各司其职 |
| 争功 quarrel | people | a 宿敌 pair | 2 / 12 | — | ★设宴调解 (ruler roll 0.4 + 政/200 + 德/400: affinity +20, +2 loyalty each; fail −10) · 赏罚分明 (one +5, other −6, affinity −15) |
| 献策 proposal | court | a 智 ≥ 85 general | 2 / 18 | — | ★依计而行 (one of 屯田 (−40% cost of the next 屯田), 远交近攻 (+15 with a far faction, −5 neighbours), 广积粮 (food −20% to buy for 3 months), 选练精兵 (capital officers training +5); exp 15) · 容后再议 |

### 6.4 Personas (sages) and their special outcomes

| Persona | Where / when | Special |
|---|---|---|
| 许劭 · 月旦评 | 汝南 and neighbours, until 194 (he died in 195) | 品题: the ruler and two generals are judged: fame +1, each judged general exp +20 and loyalty +3, prestige +10. For 曹操 the line is 「清平之奸贼，乱世之英雄」 (后汉书·许劭传; 孙盛《异同杂语》 has 「治世之能臣，乱世之奸雄」) and fame +2. Achievement 月旦品题 |
| 司马徽 · 水镜先生 | 襄阳 / 宛 / 江陵, until 207 | 荐伏龙凤雏: if 诸葛亮 or 庞统 are hidden in an own or neighbouring city, they are revealed (hire still needed). Quote 「儒生俗士，岂识时务？识时务者在乎俊杰。此间自有伏龙、凤雏」 (三国志·诸葛亮传 注引《襄阳记》). Achievement 伏龙凤雏 |
| 郑玄 · 经学大师 | 北海 / 平原, until 200 | 讲学: every 智 ≥ 60 officer in the city +30 exp; the next 书院 −30% cost |
| 管宁 · 辽东高士 | 北平 / 襄平 | as the generic sage, +德 1 |
| 于吉 · 琅邪道士 | 吴 / 会稽 / 建业, until 200 | 符水: the city is immune to plague for 12 months, order +10 |
| 张机 · 长沙名医; 盖伦 (Galen, Rome / Ephesus) | 长沙, 宛; 罗马, 以弗所 | doctor: in a plague city the 延医 option costs half; otherwise exp +40 to a general and +1 virtue |
| culture generics | everywhere else | 诡辩家 (Rome), 麻葛 (Persia), 婆罗门 / 沙门 (India), 萨满 (steppe), 德鲁伊 / 女先知 (north), 巫女 (Wa), 隐者 (others) |

### 6.5 The AI's choice

```
aiChoose(options): among affordable options (cost + reserve, §6.2), the highest ai × (aiIf satisfied ? 2 : 1)
aiIf keys: foodOverHarv k (food > k × harv), goldOverInc k, goldOver n, troopsOver n, orderBelow n, minVirtue n,
           govPol n, genWar n (a general with 武 ≥ n in the city), threatened, notThreatened
```

The player's ★ is the same choice. The scripted "engaged" player of the prototype picks by expected value (§19.3).

### 6.6 Historical seeds (classic unless noted)

| Seed | Date | Cities | p | Text and source |
|---|---|---|---|---|
| 兴平蝗灾 | 194-06 | 濮阳, 陈留, 许昌, 平原, 小沛 | 0.7 | 「蝗虫起，百姓大饿」— 三国志·武帝纪, 兴平元年: 曹操 and 吕布 broke off at 濮阳 for lack of grain |
| 江淮旱饥 | 197-05 | 寿春, 庐江, 广陵, 汝南 | 0.6 | 袁术's lands in famine (三国志·袁术传) |
| 赤壁大疫 | 208-11 | 江陵, 江夏, 长沙 | 0.6 | 「大疫，吏士多死者」— 三国志·武帝纪, 建安十三年 |
| 建安大疫 | 217-02 | 许昌, 邺, 洛阳, 陈留 | 0.7 | 曹植《说疫气》「建安二十二年，疠气流行，家家有僵尸之痛，室室有号泣之哀」; four of the 建安七子 died that year |
| 安敦宁大疫余波 (world) | 190-03 | 罗马, 阿奎莱亚, 亚历山大 | 0.5 | the Antonine plague's recurrence in 189 (Cassius Dio: up to 2 000 deaths a day in Rome) |

A seed fires the named event in each listed city (cooldowns and the disaster gap still apply); the card shows the period line in grey and a 「史」 seal.

### 6.7 Measured

All-AI classic, 96 seeds × 25 years (§19.1): 4.2 events per faction-year (world 3.1). Mix: 山贼 16%, 流民 12%, 行商 11%, 疫病 8%, 彗星 7%, 豪族 6%, 旱灾 6%, 高士 4%, 水灾 4%, the rest ≤ 3% each. Net value for the AI (gold equivalent: gold + 0.12 × food + 2.6 × development points + 0.2 × troops + order × 0.3 × income) is **+0.3 … +0.8% of gross gold income**; the scripted engaged player, which picks by expected value, gets +4.0 … +5.2% from its own cards. Target band: AI −1 … +2%; a thoughtful player +2 … +6%.

---

## 7. 国策 (ruler's missions)

### 7.1 Rules

- Player only (the AI does not need goals; its economy is untouched by missions).
- **Three active slots.** Every 3 months, and whenever a slot frees, the court offers **two** missions; the player takes one or none (one tap; the badge on the 国策 widget pulses, no card, R8). A sage's 问以天下大计 adds a third offer once.
- Each mission names its city (if any), goal, deadline and reward, and shows a live progress bar. Completion is checked at month end; the reward is paid at once with a seal stamp in the next month's news (R3). Expiry is silent (R4). The player may cancel a mission (frees the slot, no cost).
- Offers are drawn by weights that follow the realm's weak spots (`weight` in `engagement.json`): low order → 安民, low food → 广积粮, few officers → 求贤, room to develop → 兴修, threatened border → 固守. At most one military mission (拓土) in every three offers, and only after the player has attacked at least once in the last 24 months, so missions never push a peaceful player into war.

### 7.2 Templates (17)

| Id | Name | Goal (tier 1 / 2 / 3) | Deadline (months) |
|---|---|---|---|
| m_dev | 兴修 | raise the city's weakest stat by 60 / 100 / 150 | 8 / 12 / 18 |
| m_grain | 广积粮 | total food ≥ 6 / 10 / 14 months of troop consumption + 2 000 | 6 / 10 / 14 |
| m_talent | 求贤 | hire 1 / 2 / 3 generals (or the named hidden talent hinted by a sage) | 6 / 12 / 18 |
| m_train | 练兵 | 1 / 2 / 4 officers with ≥ 3 000 troops at training ≥ 90 | 6 / 9 / 12 |
| m_order | 安民 | every city order ≥ 60 / 70 / 80 | 8 / 12 / 18 |
| m_trade | 通商 | 1 / 2 / 4 caravans home (tier 3: one to another city family) | 8 / 12 / 18 |
| m_ally | 结好 | relation ≥ 40 / 60 / 80 with a named faction (or a pact with it) | 8 / 12 / 18 |
| m_build | 营建 | finish the named project in the named city | 10 / 14 / 20 |
| m_explore | 探索 | 1 / 2 / 2 expeditions (tier 3: one in another region) | 8 / 12 / 18 |
| m_wenhui | 文会 | place top 3 / top 2 / win at the next 文会 | until month 9 |
| m_tourney | 武林 | place top 3 / top 2 / win at the next 武林大会 | until month 1 |
| m_collect | 集珍 | obtain 2 commons / 3 uncommons / 1 rare | 8 / 12 / 18 |
| m_title | 授官 | 2 officers of tier ≥ 1 / 3 of tier ≥ 2 / 2 of tier ≥ 3 (needs TITLES) | 8 / 12 / 18 |
| m_level | 历练 | 2 generals at level 3 / 3 at level 4 / 3 at level 5 | 8 / 12 / 18 |
| m_city | 名城 | a city reaches 郡城 / 大郡 / 都会 | 10 / 16 / 24 |
| m_defend | 固守 | every border city defence ≥ 50 / 60 / 70 (walls, titles, 镇守) | 6 / 10 / 14 |
| m_conquer | 拓土 | take 1 / 1 / 2 cities (military; see §7.1) | 6 / 9 / 12 |

### 7.3 Rewards

```
gold     = round(k[tier] × factionIncomeAtOffer),  k = 0.4 / 0.8 / 1.2           // months of the faction's base income
prestige = 10 / 20 / 35;   exp (to the mission city's administrator, else the ruler) = 20 / 35 / 50
item     = tier 2: 25 % an uncommon of the ruler's culture; tier 3: 15 % a rare (ITEMS: a shop rare with no instance), else 60 % an uncommon
fame     = tier 3: +1 (event fame bucket)
```

Measured (§19.3): the scripted player (who takes offers blindly) completes 1.15–1.3 missions a year, worth 8.0–9.6% of its gross gold income. Target 4–10%. A first version paid 1.5 / 2.5 / 4 months of income (18.6% of gross) and was cut.

### 7.4 UI

The 国策 widget in the top bar: three bamboo-slip pips (empty, in progress with a fill, done with a red seal). Tapping opens the 国策 board: a lacquer tray with up to three slips (title, goal, a progress bar, the deadline as remaining months, the reward with icons) and the two offers (portrait of the proposing advisor, his line, 「受命」/「暂缓」). A completed slip flips to show the seal 「功成」 and the reward counting up (gold coins arc into the treasury icon, 0.8 s; sting `mission:good`, DOMESTIC-FX §8).

---

## 8. 营建 (city building projects) — ties to the city view (request 8)

### 8.1 Rules

- Command 「营建」 in the city panel: **1 token**; pick a project the city can build (requirements shown, unmet ones greyed with the reason) and a supervisor (default: the officer with the highest 政治 in the city; the list shows the months it will take).
- Gold is paid up front from the city. One active project per city; any number of cities in parallel.
- `progress += 10 + supervisor.pol / 8` per month (政治 80 → 20 per month) while the supervisor is in the city and still in the faction; 5 per month otherwise (the workers carry on slowly). The supervisor gains 3 routine exp per month (§4.1). He is free to do other commands; leaving the city only slows the work.
- A project is done when `progress ≥ work`. It is permanent and belongs to the city: a conquered city keeps its buildings for the new owner (cities become prizes, and the AI's hoarded gold turns into visible cities, §8.6).
- 奇观 (wonder): capital only, once per faction ever, city level ≥ 郡城 (3).
- The 献策 屯田 card gives the next 屯田 −40%; 郑玄's 讲学 the next 书院 −30%.

### 8.2 Catalogue (13)

| Id | Name (hua) | Cost | Work | Requires | Effect (in the city) | City-view anchor |
|---|---|---|---|---|---|---|
| irrigation | 陂塘水渠 | 400 | 100 | land ≥ 300 | harvest +12%; flood and drought weight ×0.4; order target +2 | farms (a weir and channels across the field ring) |
| tuntian | 屯田 | 300 | 80 | ≥ 3 000 troops in the city | troops in the city eat 25% less; harvest +5% | farms (soldiers' huts and ox-ploughs at the edge) |
| market | 大市 | 500 | 100 | 产业 + 町 ≥ 600 | income +6%; ITEMS shop tier +1 (max 4); caravans from here +0.04 margin | market (a second row of stalls, a drum tower) |
| roads | 驿道与传舍 | 350 | 80 | — | caravans from here: risk ×0.5, 1 month faster; income +2%; order target +3 | walls (a paved road with a post station at the gate) |
| academy | 学宫 | 450 | 100 | 町 ≥ 350 | 智 ≥ 60 officers +2 exp per month; 搜索 +6 pp; hidden talents within 1 link revealed with 3% per month; 文会 score +5 for the faction | academy |
| drill | 讲武堂 | 350 | 80 | — | 训练 +3; new recruits arrive at training 40 instead of 30; tournament seeding +5 | drill ground (archery butts, a viewing stand) |
| walls | 重城 | 500 | 120 | 町 ≥ 300 | city defence +10 (stacks with titles' 镇守) | walls (taller walls, corner towers) |
| temple | 宗庙 | 400 | 90 | — | order target +8; plague does not spread into the city and costs half; festivals ×1.5 here | temple |
| granary | 常平仓 | 300 | 70 | — | disaster food losses ×0.5; 交易 here buys 15% cheaper and sells 15% dearer | granary |
| forge | 铁官 | 450 | 100 | 产业 ≥ 400 | 开发·产业 +10%; shop +1 uncommon slot; 征兵 −10% gold | workshop |
| harbour | 津渡船坞 | 500 | 110 | coastal or river | sea caravans from here (+0.06 margin on sea legs); income +3% | harbour |
| beacons | 烽燧亭障 | 250 | 60 | a border city | warning: an AI attack on this city is announced one month ahead (an 急报 card with the enemy's portrait and troops); auto-resolve defence ×1.05 | walls (watchtowers along the approach roads) |
| wonder | 铜雀台 … | 2 000 | 300 | capital, once, city level ≥ 3 | prestige +5 per month; festivals ×1.5; capital order target +5; hosts the 文会 when the faction leads in prestige | palace |

Projects are a mid-game sink: their percentage bonuses pay back slowly while cities can still be developed (one 开发·产业 of 50 gold pays itself back in about 2 years), and well once a city's stats near 999, where 开发 stops working. Their real value is what development cannot buy: disaster resistance, order, defence, exp, items, prestige and the look of the city.

### 8.3 Names by city family

The numbers never change; the name, model and icon do (`names` in `engagement.json`; missing entries fall back to `any`, then to the hua name).

| Project | hua | roman | persia | indic | steppe | others (examples) |
|---|---|---|---|---|---|---|
| irrigation | 陂塘水渠 (芍陂, 都江堰 style) | 引水渠 (aqueduct) | 坎儿井 (qanat) | 蓄水池 (tank) | 引水渠 | 水坝 (arabia; the 马里卜 dam), 水车与渠 (nile), 坎儿井 (oasis) |
| market | 大市 | 广场集市 (forum market) | 巴扎 | 集市 | — | 商队客栈 (arabia) |
| roads | 驿道与传舍 | 罗马大道 | 御道驿站 (royal road) | — | 驿马站 | |
| academy | 学宫 | 学园 | 书院 | 精舍 (vihara) | — | 佛寺讲堂 (oasis) |
| temple | 宗庙 | 神殿 | 火庙 | 佛塔 | 祭天坛 | 大神殿 (arabia), 阿佩德马克神庙 (nile), 圣林 (north), 祭殿 (wa), 祭坛 (han3), 神祠 (nanhai) |
| walls | 重城 | 城墙与塔楼 | — | — | 木栅营垒 | 土垒木墙 (north) |
| wonder | **铜雀台** (built by 曹操 at 邺 in 210) | 凯旋门 | 王宫大殿 | 大塔 (stupa) | 龙城金帐 | 大佛寺 (oasis), 贝勒神殿 (arabia), 金字塔 (nile, after Meroë's), 巨石圣环 (north), 大墓 (wa), 东盟祭坛 (han3), 金塔 (nanhai) |

### 8.4 In the city view

- **Construction**: the anchor building gets scaffolding at 0–25% of the work (bamboo poles and reed mats for hua, timber cranes for roman, mud-brick stacks for persia / arabia / nile …), half-built shells at 25–75%, the finished model with a fresh-paint tint and ribbons for 3 months after 100%. Workers (4–10 instanced figures) carry loads along a path when the camera is near.
- **Completion** is a great-tier vignette (DOMESTIC-FX §4) of the 营建 command: the scaffold drops in a puff of ink dust, the new model rises (DOMESTIC-FX's `d.upgrade`), a seal 「落成」, the sting `build:great`. It plays at the start of the next month like a stage-up (DOMESTIC-FX §9.2).
- The city panel lists finished projects as small lacquer plaques under the city name and the active one with a progress ring and months left.
- Budgets: a project adds at most 2 500 triangles (wonder 3 000; CITY-VIEW §9 budgets) and one draw call per kind (instancing).

### 8.5 City levels and prosperity goals

City level (邑 1, 县城 2, 郡城 3, 大郡 4, 都会 5) is the CITY-VIEW header badge (`cityLevel` in `domestic/city-stages.js`: the rounded mean of the farm, workshop, houses, walls and market stages). Each level first reached by a city gives prestige +10 × (level − 1), a seal in the news and the achievements 郡城气象 / 大郡繁华 / 天下名都. The city view shows the level as the gate's lantern count and banner.

### 8.6 AI building (AI-relevant, on by default)

Each AI city without a project, once in 6 months on average (`rng('aiproj', city) < 1/6`), starts the first buildable project of its list when its gold ≥ cost + 600: border cities 城墙 → 烽燧 → 演武场 → 市集 → 水利; inner cities 市集 → 水利 → 祠庙 → 常平仓 → 书院. Measured (§19.1): about 175 projects per classic game over 25 years (the AI's hoard after year 5 becomes buildings), campaign pace unchanged, city order +6 at year 25, the AI's border cities harder to take by +10 defence. Without it the AI's cities in the city view would look the same in year 20 as in year 5.

---

## 9. Trade

### 9.1 商队 (caravans the player sends)

- Command 「通商」 (city panel): **1 token**, a leader (not the ruler; never the last officer of the city), an investment and a destination.
- **Active caravans** ≤ 1 + floor(cities / 4), max 4. **Investment** 100 … 200 + 150 × market tier of the origin (with a 市集 project +1 tier), max 800.
- **Destination**: a city of another faction within 6 links, every city on the way owned by you, an ally or pact partner, nobody, or a faction at relation ≥ 0 and not at war with you. Own cities of another culture also count at half margin.

```
hops = links on the route (a sea link counts 1; a sea leg needs a harbour at either end or adds +1 hop)
m    = 0.04 + 0.02 × hops + 0.05 × [destination's city family ≠ origin's] + 0.02 × (destination market tier − 1)
       + (leader.pol − 50)/800 + 0.04 [origin 市集] + 0.05 [trade pact with the destination's owner] + 0.05 [行商 hosted, 12 months]
       + 0.06 × sea legs [harbour]                                    → cap 0.30
risk = clamp(0.02 × hops × (1 − leader.war/200) × (1.3 − origin.order/100), 0, 0.30) × (origin 驿道 ? 0.5 : 1)
time = 1 + ceil(hops / 2) months (− 1 with 驿道, min 1)
return: safe → gold += invest × (1 + m) to the origin (or the capital if it was lost); relation +3 with the destination's owner;
              foreign → prestige +2; 35 % + 5 % × hops: one item from the destination's market (80 % common, 19 % uncommon, 1 % rare)
        lost (risk) → gold += invest × 0.5; leader exp 5
leader exp 10 + 4 × hops
```

- The leader is **away** for the trip (`g.away = { kind: 'caravan', until, home }`; away generals are skipped by `generalsIn`, so they are not in the city, not in battles, cannot be captured, draw no salary).
- Map: a camel train (land; ox carts in hua, mule trains in the north, elephants in indic) or a junk / dhow / Roman merchantman (sea) walks the route in the map view; tapping it shows the cargo, the return month and the risk. Return: coins arc into the city label; an item reveal if goods came (ITEMS §5.5).
- Measured (§19.3): the scripted player runs 2.2–3.2 caravans a year; their **net profit is 7–18.5% of its gross base income** (target 8–20%; small, hub-adjacent classic realms are at the top, world realms far from hubs at the bottom). A first formula (0.05 + 0.03/hop + 0.08 foreign, cap 0.45) gave 18–20% in an earlier build, where faster relation drift closed many routes; it was cut to the current one before the drift was slowed.

### 9.2 通商协定 (trade pact)

外交 sub-action, 1 token: needs relation ≥ 10 and no war; p = clamp(0.35 + rel/200 + envoy.pol/400, 0.10, 0.90); 24 months, renewable. Effects: caravans to the partner's cities +0.05 margin; both factions' cities adjacent to the other's +3% income; relation floor 20; relation +5.

### 9.3 Markets

The visiting merchants (胡商 / 海商, ITEMS §6.5) arrive through the 行商过境 card (§6.3), so they count against the interruption budget and can be taxed or hosted instead. 庙会 (month 3) and shop restocks are ITEMS'. 交易 keeps the v3 food prices; the 常平仓 project and the 广积粮 plan adjust them.

---

## 10. Diplomacy beyond alliances

### 10.1 The 外交 screen

外交 (city panel, the city sends the envoy) opens a screen instead of the v3 three-item list: the factions grouped by region (v3 `groupedFactions`), each row with the ruler's portrait, a relation bar (−100 … +100, colour from vermillion to jade), the pacts as small seals (盟 / 婚 / 贡 / 停 / 商), war memory (a crossed-blades glyph for 12 months) and the AI's mood line (「袁绍对我军颇为忌惮」). Tap a faction → its actions, each with its chance and cost before confirming. Every action costs **1 token** and uses an **envoy** (picker; 智力 / 政治 and the TITLES 外交 branch and ITEMS `dip` gear count).

```
envoyK = 0.8 + max(envoy.intel, envoy.pol) / 250                     // 0.8 … 1.2
prestigeK = min(1.5, 1 + prestige / 2000)
```

### 10.2 赠礼 (gift)

Gold (≥ 50) or an item (ITEMS §6.6 value × giftK in gold): Δrel = min(25, round(gold / (8 + 2 × cities(target)) × envoyK × prestigeK)), halved if the same target got a gift within 6 months. 150 gold to a 3-city lord with a good envoy ≈ +13. Envoy exp 12.

### 10.3 同盟 (alliance)

v3 rule and chance, + rel/400, needs relation ≥ −20. Success: +10 relation, prestige +5, floor 30 while it lasts.

### 10.4 联姻 (marriage alliance)

- Needs relation ≥ 30 or an alliance; each pair once; each faction at most 3 marriages in a game; both have a living ruler.
- Cost (betrothal gifts 聘礼): 300 + 50 × own cities gold.
- p = clamp(0.20 + rel/250 + effVirtue/400 + (their cities ≤ yours ? 0.10 : 0) + min(0.10, prestige/5000) + hostage (§10.6), 0.05, 0.85).
- Success: a **婚盟** pact for 48 months (non-aggression; v3 alliance included), relation +30 and floor 50, both rulers' factions prestige +20, effVirtue +2 (event bucket). The card is abstract («以宗室之女妻之»): no general moves; the text picks the side by the rulers (刘备 and 孙权's house is the model: 孙夫人, 209).
- Steppe and Han: the card is titled 和亲 (the Han–Xiongnu precedent of 刘敬: an imperial princess and yearly gifts). Rome: «联姻» with a client king.

### 10.5 纳贡 and 索贡 (tribute)

- **纳贡 (offer tribute)**: you pay X gold per month for 12 months (default 8% of your base income, slider 5–20%), the receiver agrees not to attack you (non-aggression pact). p = clamp(0.40 + 2 × X / receiverIncome + rel/300, 0.05, 0.95). Payment is taken each month from your richest city; if you cannot pay, the pact ends and relation −20.
- **索贡 (demand tribute)**: needs your troops ≥ 2 × theirs and a common border. p = clamp(0.15 + 0.15 × (ratio − 2) + effFame/400 + rel/400, 0.05, 0.75). Accepted: they pay 8% of their income to you for 12 months, both sides under non-aggression; you get +1 prestige per month paid. Refused: relation −20.
- **AI demands of the player** (a scheduled card): an AI neighbour with ≥ 2.5 × the player's troops and relation ≤ 20, at most once a year (p 1/12 per month): 「岁贡金 X，否则兵戎相见」 (X = 8% of the player's income). Accept → a tribute pact. Refuse → relation −15 and for 12 months that AI needs 0.15 less troop ratio to attack the player (the only aggression bonus in this spec).
- **AI to AI** (on by default): a faction next to a neighbour with ≥ 2.5 × its troops, at relation ≥ −30 and not at war, offers tribute with 12% per month; accepted with clamp(0.5 + rel/200, 0.1, 0.9). Measured: 5–6 tribute pacts per classic game, ≈ 145 per world game (many small kingdoms paying great powers); campaign pace unchanged (§19.1). The news shows them («车师向鲜卑纳贡»).
- History: the 卑弥呼 mission of 239 to 魏 (tribute, the title 亲魏倭王 and gifts in return); Commodus' peace of 180 with the Marcomanni and Quadi (Cassius Dio 73.2: hostages, grain, recruits); the Han–Xiongnu 和亲 gifts.

### 10.6 质子 (hostages)

An option on 联姻, 停战, 纳贡 and 同盟 offers: send a non-ruler general (not the last officer of his city). Chance + 0.10 + max(war, intel, pol)/600. He lives in the partner's capital, away (§9.1) for the pact's duration. When the pact ends in peace he comes home with +20 exp (见闻) and +5 loyalty. If **you** break the pact (attack the partner), he becomes the partner's captive (v3 captive dialog for the AI: 50% released, 50% executed or hired). If the **partner** breaks it, he comes home and the partner's effVirtue −10. AI requests a hostage when its acceptance chance is below 0.5 (the card asks the player whom to send). History: 曹操 demanded a 任子 of 孙权 in 202 and 周瑜 talked him out of it (江表传); Commodus' Marcomannic hostages.

### 10.7 停战 (truce)

With a faction at war with you (an attack either way within 12 months): gold G (slider), p = clamp(0.20 + G / (6 × their income) + rel/300 + 0.2 × clamp(yourTroops/theirTroops − 1, −0.5, 0.5) + hostage, 0.05, 0.80). Success: 12-month non-aggression, relation +15, prestige +5. Failure keeps the gold (it was a gift: relation + Δgift/2).

### 10.8 归附 (peaceful submission, 让州)

A small faction may join a strong, virtuous neighbour without war:

```
for each AI faction H with ≤ 3 cities, monthly:
  candidates F adjacent to H with troops(F) ≥ 2 × troops(H), effVirtue(F) ≥ 50, and (rel(F, H) ≥ 50 or a marriage, or H paying F tribute for ≥ 12 months)
  p(F) = clamp(0.01 + (rel − 50)/1000 + 0.005 × min(10, ratio − 2) + effVirtue/2000 + prestige(F)/40 000, 0, 0.06)
  the best F rolls once (rng('submit', H))
  F = player → a scheduled card (top priority): 「韩馥愿以冀州相让」 accept / decline (decline: relation +5)
  F = AI → only AI-to-AI when `Engage.aiSubmit` is on (off in v4, §24 Q4)
accept: every city of H changes owner (order +5), every general of H joins F (loyalty 60 + effVirtue/5, H's ruler +10, range 50–95),
        H dies without war, F prestige +30, achievement count; no further 归附 to F for 36 months (digestion)
```

History: 韩馥 gave 冀州 to 袁绍 (191); 刘琮 surrendered 荆州 to 曹操 (208); 士燮 submitted to 孙权 (210); 张鲁 to 曹操 (215). Measured (§19.3): the scripted peaceful player receives 0.1–0.4 submissions per 20-year classic game and 0.75 per 15-year world game (the world has many one-city kingdoms; without the 36-month digestion rule it was 1.2). This and 献城 (v3 拉拢) are how a peaceful player grows.

### 10.9 AI proposals to the player

- Alliance offer from an AI at relation ≥ 40 (8% per month): accept / decline, free. Marriage offer at relation ≥ 60 (2% per month). Trade pact offer at relation ≥ 30 (4% per month). All are scheduled cards (≤ 1 a month).
- Demands (§10.5), 归附 (§10.8).
- Measured: 0.5–0.7 alliance offers a year for the scripted peaceful player (marriage and trade offers were not simulated).

### 10.10 Betrayal

Attacking a pact partner ends the pact at once: marriage effVirtue −15, prestige −50, relation −80 with the partner and −20 with its allies; tribute / truce effVirtue −8, prestige −20. A hostage of the betrayer is lost (§10.6). The AI never breaks pacts (it skips pact partners as targets).

---

## 11. Relationships between generals (羁绊)

### 11.1 Starting bonds

42 pairs from history and the novel (`engagement.json` `bonds`, with sources), for example: 刘备–关羽–张飞 结义; 关羽–关平, 孙坚–孙策, 马腾–马超, 马超–马岱, 曹操 with 夏侯惇 / 夏侯渊 / 曹仁 / 曹洪, 荀彧–荀攸, 糜竺–糜芳, 蒯良–蒯越 亲族; 孟获–祝融 夫妻; 孙策–周瑜 知己 (同年，独相友善); 诸葛亮–徐庶, 诸葛亮–庞统, 关羽–张辽, 颜良–文丑, 张郃–高览 知己; 程普–周瑜 不和 (程普 later: 「与周公瑾交，若饮醇醪，不觉自醉」, so a reconciliation can grow it into friendship); 甘宁–凌统 不和; 张飞–吕布, 马超–张飞, 马超–许褚, 孙策–太史慈, 关羽–黄忠 宿敌; 吕布–董卓 不和 (192); 卑弥呼–卑弥弓呼 不和 (world).

### 11.2 How bonds grow (pair cards of §6.3)

- **Living together**: two officers of one faction in one city gain +1 affinity per month up to 60 (records are created for the first six officers of each city, so storage stays small: ≈ 1 500 pairs in the world).
- **Fighting together**: +4 for each pair in a winning army.
- **Cards**: 切磋 +10 (30% rivalry for near-equal ≥ 85 warriors), 宴饮 +5, 调解 +20 / −10, 赏罚分明 −15, 谗言 −10.
- **Oath**: at affinity ≥ 70, the 结义 card (100 gold) makes them sworn; a sworn group holds at most 3, so a third general can join a pair. 拜师 needs the mentor and student rules of §3.4; a mentor has at most 2 students, a student 1 mentor.
- Measured (§19.3): the scripted player sees 0.1–0.25 oaths and 0.2–0.6 mentorships per 10 years; human players who hold banquets and watch sparring see more. Target: an oath every 10–40 years of play, a mentorship every 5–25.

### 11.3 Effects

As in §3.4. In tactical battles (`battle-model.js`): `atkPower(u) ×= 1 + bondAtk(u)`, `defFactor(u) ×= 1 + bondDef(u)` where the sums are over bonded generals alive on the same side (sworn +4% / +4%, friend +2% attack, rival in the same army +3% attack, feud −4% / −4%), capped at +8% / −8%. 义愤: when a sworn brother's unit is routed, each other sworn brother on the field gets morale +10 and attack +6% until the battle ends (once per battle). Auto-resolved battles are unaffected (the campaign balance does not move). Duels (`duel-game.js`): between 宿敌, +4% damage both ways and the duel is always accepted.

### 11.4 UI: 人物关系

A tab in the character menu (request 9's screen; this spec owns the content) and a 「关系」 button in the Generals list: a portrait graph of the faction's generals with ink lines (red double line 结义, gold 夫妻, green 知己, blue dashed 师徒 with an arrow, grey 亲族, purple 宿敌, black jagged 不和), affinity on hover / long-press, and a timeline of how each bond formed («193年 切磋武艺», «195年 桃园之义»). Enemy generals with bonds to yours appear at the edge with their faction colour.

---

## 12. 探索 (expeditions)

### 12.1 Rules

- Command 「探索」 (city panel of a city): **1 token**, a leader (not the ruler, not the last officer), supplies 100 + 25 × hops gold. One expedition at a time (two from 8 cities).
- Destination: one of 44 named **sites** (`engagement.json` `sites`), each tied to its nearest city; reachable if that city is within 8 links without crossing a faction at war with you. A site can be visited again after 24 months.
- Time: 2 + ceil(hops / 2) months, max 6. The leader is away (§9.1).
- On return (one roll stream per expedition, `rng('exped', id)`):

```
mishap (site type, 4–15 %): the leader is injured (away 2 more months), exp 15, nothing else
gold   = uniform(type range) × (1 + hops/4)          // 20–220 base
item   : rare min(6 %, type rare + 0.5 % × hops) · else uncommon (10–15 %) · else common (20–30 %)  — pools by site culture and type (§12.3)
exp    = 30 + 10 × hops to the leader
prestige = type prestige (3–15) + 10 for a 传说 (myth) site
virtue +1 for 圣地 (holy) sites
talent : 6 %, a hidden general somewhere is revealed (the leader «闻其名»)
clue   : 古迹 (ruin) 8 %: a 藏宝图 for an unfound ITEMS hidden item in that region (§12.4)
```

### 12.2 Site types

| Type | Gold (base) | Item chances (common / uncommon / rare) | Item pool types | Exp stat | Prestige | Mishap |
|---|---|---|---|---|---|---|
| 名山 mountain | 40–100 | 25 / 12 / 3% | medicine, amulet, music | 智 | 5 | 10% |
| 古迹 ruin | 60–150 | 30 / 15 / 4% | relic, milbook, civbook, seal, weapon | 智 | 8 | 8% |
| 荒原 wild | 30–80 | 30 / 14 / 3.5% | mount, jewel, medicine | 武 | 4 | 15% |
| 海外 sea | 80–200 | 30 / 15 / 4% | jewel, relic | 武 | 6 | 14% |
| 圣地 holy | 20–60 | 20 / 12 / 3% | relic, amulet, civbook | 政 | 15 | 4% |
| 矿山 mine | 100–220 | 25 / 10 / 2.5% | weapon, armour, jewel | 武 | 3 | 12% |

### 12.3 Sites and item pools

| Site | Type | Near | | Site | Type | Near |
|---|---|---|---|---|---|---|
| 泰山 | holy | 北海 | | 波斯波利斯废墟 | ruin | 伊斯塔克尔 |
| 昆仑 (传说) | mountain | 于阗 | | 巴比伦废墟 | ruin | 泰西封 |
| 蓬莱 (传说) | sea | 北海 | | 贝希斯敦 | ruin | 埃克巴坦那 |
| 嵩山 | mountain | 洛阳 | | 乳香之地 | wild | 扎法尔 |
| 峨眉 | mountain | 成都 | | 马里卜大坝 | ruin | 马里卜 |
| 会稽禹穴 | ruin | 会稽 | | 麦罗埃金字塔 | ruin | 麦罗埃 |
| 骊山 | ruin | 长安 | | 吉萨金字塔 | ruin | 亚历山大 |
| 洞庭君山 | mountain | 长沙 | | 特洛伊遗址 | ruin | 以弗所 |
| 南中瘴林 | wild | 云南 | | 德尔斐 | holy | 拜占庭 |
| 合浦珠池 | sea | 合浦 | | 奥林匹亚 | holy | 拜占庭 |
| 长白山 | mountain | 国内城 | | 迦太基旧港 | ruin | 迦太基 |
| 出云 | holy | 投马 | | 赫拉克勒斯之柱 (传说) | sea | 廷吉斯 |
| 楼兰故城 | ruin | 楼兰 | | 锡岛 | mine | 伦迪尼乌姆 |
| 天山 | mountain | 龟兹 | | 条顿堡森林 | ruin | 科洛尼亚 |
| 金山 | mine | 坚昆 | | 赫西尼亚森林 | wild | 马科曼尼 |
| 贰师城牧场 | wild | 大宛 | | 扶南金洲 | sea | 喔㕭 |
| 犍陀罗佛塔 | holy | 富楼沙 | | 阿特拉斯山 (传说) | mountain | 巴夸特 |
| 恒河 | holy | 华氏城 | | 宝石山 | mine | 阿努拉陀 |
| 华山 | mountain | 长安 | | 庐山 | mountain | 豫章 |
| 九嶷山舜陵 (传说) | holy | 零陵 | | 青城山 | holy | 成都 |
| 白马寺 | holy | 洛阳 | | 祁连山 | mine | 武威 |
| 曲阜孔林 | holy | 小沛 | | 秦岭栈道 | wild | 汉中 |

**44 sites; 16 lie on the classic map** (泰山, 蓬莱, 嵩山, 峨眉, 会稽禹穴, 骊山, 洞庭君山, 南中瘴林 and the last eight rows). The world map has all 44.

**Item pools** (`SG.Items.pool({ culture, types, rarity })`, ITEMS owns the call): commons and uncommons of the site's culture and the type's pool types; rares from the ITEMS rule for tournament and mission rares — a shop rare of that culture that currently has no instance in the world, else an uncommon + 150 gold. Expeditions never give epics or legendaries (those stay with rulers, hidden spots, the tournament and the one shop epic).

### 12.4 藏宝图 (treasure map) clues

A clue names one unfound ITEMS hidden item (§8.5 of ITEMS) in the site's region and its city. While the player owns the map, that item's search probability in that city is multiplied by 2 (still capped at 0.40), and the city panel shows a small map icon. It ties expeditions to 搜索 and to ITEMS' clue lines.

### 12.5 UI

The expedition command opens a map overlay: the sites as small ink vignettes (mountain, ruin, wave, temple, mine, steppe) with a lantern on those in reach, the months and supplies; on start a small party with a banner walks out of the city on the map; on return a «探索记» card: a painted panel of the site (one per type × family, art pilot), the leader's portrait, what was found (item reveal through ITEMS §5.5, gold, exp, the clue), and a one-line travelogue per site from `engagement.json` (e.g. 楼兰: «城郭巍然，人民散亡，唯余胡杨与流沙»). The sites visited light up in the 功业录 map page.

Measured (§19.3): 0.5–1.0 expeditions a year for the scripted player; 1.9–2.9 items a year from all non-shop routes (caravans, expeditions, missions, 文会, tournament) of which rare 0.23–0.43; expedition gold 2.1–3.6% of gross against supplies of 2.9–5.8% (expeditions are paid for in gold and repaid in items, exp and prestige).

---

## 13. 文会 and other non-combat competitions

### 13.1 文会 (the scholars' contest, month 9)

- **Host**: the living faction with the highest prestige that has a 书院 or the 奇观; if none has one, the highest prestige. The host's capital holds it; the host gets prestige +20 and capital order +5. Names by the host's family: hua 「鸿都文会」 (after the 鸿都门学 of 178) or 「白虎观论经」 when the host holds 洛阳 (after the 白虎观 conference of 79); roman 「卡皮托利努斯竞赛」 (Domitian's Agon Capitolinus of 86, with poetry and music contests and an oak crown); indic 「论议大会」 (śāstrārtha debates); persia 「麻葛论道」; others 「文会」.
- **Entrants**: every living faction sends up to **3** generals with 智力 ≥ 70 or 政治 ≥ 70 who are not away. The player picks on the scheduled card (default: the top three by 智 + 政; one tap to accept). The AI sends its top three.
- **Rounds** (one roll stream, `rng('wenhui')`):

```
经义 = 0.5 pol + 0.5 intel + 2 × level + 5 [own 书院] + U(0, 20)
辞赋 = 0.7 intel + 0.3 pol + 2 × level + 5 [own 书院] + U(0, 30)
策论 = 0.6 intel + 0.4 pol + 2 × level + 5 [own 书院] + U(0, 25) + debate (player's scholars: −12 … +12 from 论辩, §13.2; auto: 0)
total = 经义 + 辞赋 + 策论; ties: higher level, then lower id
```

- **Prizes**:

| Place | Item | Gold (to the capital) | Prestige | Exp | Other |
|---|---|---|---|---|---|
| 1st | a rare 典籍 / 兵书 / 乐器 (ITEMS: shop rare of those types and the winner's or host's culture with no instance; else an uncommon + 150 gold) | 300 | +50 | +80 | fame +1; the winner's name on a banner in the host city's academy for a year; achievement 文采风流 |
| 2nd | an uncommon of those types (winner's culture) | 150 | +25 | +50 | |
| 3rd | a common of those types | 80 | +12 | +35 | |
| all entrants | | | | +20 | |

- Measured (§19.3): the scripted player wins 1.0–1.7 times per 10 years in the classic scenario (18 factions) and 1.1 in the world (70 factions, but many without scholars); top 3 2.6–3.9 times per 10 years (classic).

### 13.2 论辩 (debate mini-game, optional, ≈ 40 s)

Offered when a player's scholar reaches the 策论 round; 「自动」 skips it (bonus 0).

- Five exchanges against the strongest rival scholar. Each side picks a stance: **引经** (cite the classics, power = 政治), **设喻** (argue by analogy, power = (智力 + 政治)/2), **驳难** (rebut, power = 智力). 驳难 beats 引经, 引经 beats 设喻, 设喻 beats 驳难.
- Winner of an exchange: 10 + (own power − opponent power)/10 points (at least 4). Same stance: the higher power gets 6, the other 3.
- The rival chooses with weights (power of the stance)² + 400 from `rng('debate', …)`. Before each exchange a **tell** line appears (「对方抚须，似欲引《春秋》」), true 60% of the time, so reading the opponent matters.
- A meter fills by one per exchange won; when full (2 wins) the brush glows: 「妙语」 doubles the next exchange's points (once).
- Result: debate bonus = clamp((your points − rival points)/3, −12, +12) added to 策论.
- Art: two scholars on mats facing each other in the hall, brush strokes write the stance characters in the air, the audience sways; sting by the host's culture family; voices per the voices task (request 12).

### 13.3 投壶 (pot-throwing mini-game, optional, ≈ 15 s)

Used by the 宴饮 card and 盛大 festivals. Three arrows: a swinging angle meter and a power meter (timed taps), a wind streamer; a hit in the mouth scores 2, an ear (贯耳) 3, a leaning arrow (依竿) 1 (names after 礼记·投壶). Score 0–9 → the three lowest-loyalty officers of the host city get +2 / +3 / +4 / +5 loyalty (0–2 / 3–4 / 5–6 / 7–9). 「自动」 = score 4.

### 13.4 Friendly sparring (切磋)

The 切磋 card's 「亲临观战」 starts a duel in the duel game with `mode: 'friendly'`: no capture, no death, no morale or army effect, crits and combos as COMBAT-V4. Winner exp +25 and loyalty +3, loser +15, affinity +10. The player chooses which side to play (or watches the AI duel at ×2). It is also the 「演武」 practice yard's content hook (COMBAT-V4 §12.4).

### 13.5 武林大会 (request 14) hooks

The tournament spec owns the event (brackets, stadium, rules). This spec adds:

- **Prestige** +50 / +25 / +12 for the champion's / second's / third's faction (to the tournament spec as the prestige line of its prizes; ITEMS §8.9 gives the item side and the gold 400 / 200 / 100).
- **Bonds**: two finalists of different factions, both with 武力 ≥ 85, become 宿敌 with 30%; finalists of one faction gain affinity +10.
- The 求战心切 card's 「荐其出战」 option, the 武林 mission, the 天下第一 achievement and the 演武场 project's +5 seeding.
- Calendar: the tournament is the scheduled card of month 1 (§5.1); the year review (§16.4) plays first.

---

## 14. 功业 (achievements and milestones)

### 14.1 Rules

- 48 achievements in 8 groups (`engagement.json` `achievements`). Each is checked at month end by a pure condition on counters (`G.engage.counters`) and state; unlocked once per game; never lost (R4).
- Reward: prestige (+5 … +80), a few also +1 fame or +1 virtue (event buckets). No gold (achievements are recognition, not income).
- Presentation: a seal stamps the month news (ink splash, a gong, ≤ 3 per month, the rest as one line); the 功业录 screen (§16.3) shows them as a scroll of seals by group with the date and the deed.

### 14.2 List

| Group | Achievements (prestige) |
|---|---|
| 内政 dom | 尽地力 a stat 999 (20) · 郡城气象 a 郡城 (10) · 大郡繁华 a 大郡 (25) · 天下名都 a 都会 (50) · 百万之城 a city of 1 000 000 (40) · 政通人和 all cities order ≥ 80 (40, fame +1) · 路不拾遗 a city at 95 (20) · 仓廪实 20 000 food harvested (15) · 鸠工庀材 first project (10) · 百废俱兴 10 projects (40) · 奇观落成 the wonder (80, fame +1) · 救民水火 5 reliefs (20, virtue +1) |
| 经济 eco | 府库充盈 5 000 gold earned (15) · 富可敌国 20 000 (40) · 初通商旅 first caravan (5) · 商贾云集 20 caravans (30) · 丝路驼铃 caravans to 4 city families (40) · 陶朱之富 10 000 trade profit (50) |
| 外交 dip | 合纵 3 pacts at once (20) · 秦晋之好 a marriage (20) · 四夷宾服 3 tributaries (50) · 天下归心 every faction at relation ≥ 30 (60, fame +1) · 化干戈为玉帛 3 truces (15) · 万里通使 relation ≥ 60 with a faction ≥ 8 links away (30) |
| 文治 cul | 文采风流 win a 文会 (30) · 文坛盟主 win 3 (60, fame +1) · 与民同乐 5 grand festivals (20) · 礼贤下士 host 5 sages (20) · 月旦品题 (15) · 伏龙凤雏 (25) |
| 收藏 col | 奇珍满库 10 items (10) · 稀世之珍 a rare (15) · 神品 an epic (40) · 博物 图鉴 50% (40) · 受命于天 the 传国玉玺 (80, fame +2) |
| 人物 ppl | 桃园之义 an oath (15) · 薪火相传 2 mentorships (15) · 人才济济 30 generals (30) · 百战名将 a level-10 general (25) · 位极人臣 a 一品 office (30, needs TITLES) · 野无遗贤 10 hidden talents found (30) |
| 武功 mil | 天下第一 win the 武林大会 (30) · 金城汤池 repel 5 sieges (20) · 据有十城 10 cities (30) |
| 探索 exp | 跋山涉水 first expedition (5) · 足迹天下 10 sites (40) · 寻仙访道 3 legendary sites (30) · 按图索骥 a treasure map (20) |

Measured (§19.3): the scripted player unlocks 0.5–1.9 a year (≈ 1 a year from year 5): 18–21 by year 20 in the classic scenario, 14 by year 15 in the world; the prestige from achievements is about a quarter of its total. Target: ≥ 0.5 in the first year for the scripted player (a human who sends the first caravan, project and expedition gets 2–3 «first» achievements in year 1), 0.5–1.5 a year after, never more than 3 in a month.

### 14.3 Milestone moments

Some achievements and thresholds also trigger a short scene: a city's first 都会 (the city view's banner rises, the governor bows to the ruler), the wonder's completion (fireworks of paper lanterns), the 天下归心 condition (envoys from every friendly faction file into the capital, their portraits in procession). Each is ≤ 6 s and skippable.

### 14.4 Cross-game medals

The title screen gets a 功业阁 shelf: a medal for each achievement ever unlocked on this device (`localStorage['sanguozhi2_feats_v4']`, try/catch, per scenario), and the four victory seals (霸业, 王道, 天命, 万国来朝). Purely cosmetic.

---

## 15. Victory paths and the score

### 15.1 霸业 (unification)

v3 rule (every city), unchanged.

### 15.2 王道 (the Kingly Way)

All of these, held for **6 consecutive months**:

| Condition | Value |
|---|---|
| Prestige | ≥ 1 500 |
| Order | the average over own cities ≥ 75 |
| Prosperity | two cities at 大郡 or one at 都会 (§8.5) |
| Neighbours | every faction that borders you at relation ≥ 30 or under a pact (alliance, marriage, tribute, truce, trade), and at least 4 factions so |
| Peace | you started no attack on a faction for 24 months (taking empty cities and receiving 归附 or 献城 do not count) |

The top bar shows a 王道 tracker (five small seals that light up; the run of months as a ring). When all are met for 6 months: the **天下归心** ending: the rulers at relation ≥ 0 send envoys with tribute to your capital, a painted ending card, the 王道 seal, the final score (§15.5), and the choice 「功成身退」 (end) or 「继续经营」 (continue; the seal stays).

Measured (§19.3): 22–25% of the scripted peaceful games reach it within 20 years in the classic scenario (median 11–13 years) and 12.5% within 15 years in the world (median 12.7 years). A thoughtful human should reach it in 10–15 years from a small realm. It is out of reach for a constant warmonger (peace and neighbours) and slow for a conqueror (order).

### 15.3 天命 (the Mandate; classic scenario)

The ruler holds the 传国玉玺 (ITEMS legendary; found by 搜索 in 洛阳 from 191, or taken), the faction owns 洛阳 or 长安, prestige ≥ 2 000 and effFame ≥ 90, held 6 months → the 受禅 ending (the Han emperor abdicates in your favour). It mixes the military and the peaceful paths.

### 15.4 万国来朝 (all nations at court; world scenario)

Pacts (alliance, marriage, trade, or tribute received) with ≥ 12 factions in ≥ 5 regions at the same time, ≥ 8 000 gold of caravan profit in the game, and prestige ≥ 2 000, held 6 months → the 万国来朝 ending (envoys of every culture with their portraits and gifts). Target: an engaged world player from a central position (Kushan, Parthia, 于阗, Palmyra) in 12–20 years.

### 15.5 天下榜 (the score)

```
score(f) = 20 × cities + Σ (land + industry + town) / 100 + prestige / 10 + Σ levels of its generals + 10 × active pacts
```

A pull screen ranks all living factions (top 10 and yours) with ruler portraits and a sparkline of the last 10 years. It is the AI's prestige league as well (the AI earns prestige through conquest and its festivals, §3.2).

**三十年之约** (new-game option, off by default): the game ends after 30 years (or at any victory) with a final score card: rank among factions, score, grade 甲上 … 丙下 by rank (1st 甲上; top 3 甲; top 25% 乙; else 丙; the + / − by the score against the scenario's reference curve), the year's chronicle and the achievements.

---

## 16. UI and presentation (Lanternvale)

### 16.1 Main interface additions

The main interface and the menu / settings buttons belong to requests 1 and 11. This spec asks for these elements and owns their content:

| Element | Place | Content |
|---|---|---|
| 声望 seal | top bar, next to the ruler's portrait | the prestige number and its name (§3.2); tap → 天下榜 |
| 国策 slips | top bar | three pips (§7.4); badge when offers wait |
| 年历 ribbon | top bar, after the date | the month with the next scheduled thing as a small icon (lantern = festival, brush = 文会, crossed spears = tournament, sheaf = harvest) and «三月 · 庙会» on hover |
| 王道 tracker | top bar, collapsible | five seals and the run ring (§15.2); only shown once two conditions are met |
| City panel | 民心 bar, active project ring, plaques of finished projects, caravan / expedition icons when one is out from this city |
| Generals list | a 「关系」 button (§11.4); bond icons beside portraits (red knot 结义, green leaf 知己, blue brush 师徒, purple flame 宿敌, black crack 不和) |
| Menu | 功业录, 天下榜, 国策, 营建总览, 商队 / 探索 list, 外交 screen |

### 16.2 Event card

A paper card (640 × 360 reference px; phones 92% width) with a header band in the category's colour (nature indigo, society ochre, court vermillion, people jade, global deep purple), a painted vignette (one per event × city family where the art pilot provides it, else per event; painted in the Lanternvale palette with soft light), the reporting general's portrait (the city's administrator, or the pair for people cards), the text with highlighted numbers, and 2–3 option buttons. Each button shows its cost chips and its forecast; ★ marks the recommended one; failing options (chance rolls) show the chance as a small ring. A footnote line gives the history or the source note when the card has one (「史」 seal for seeds). Answering plays a 1–2 s result vignette (the DOMESTIC-FX card stage, tier by the result: disaster mitigated → good, the chance option succeeded → great) and its sting. Keyboard 1 / 2 / 3, Esc = the safe free option (R4).

### 16.3 Other screens

- **国策 board** §7.4. **营建** dialog: the city's buildable projects as cards with the city-view thumbnail of the finished model, cost, months with the chosen supervisor, effect lines; the overview lists all cities' projects.
- **商队** dialog: a route on the mini-map, destinations sorted by expected profit per month with risk rings; a ledger of trips (date, route, invest, return).
- **探索** overlay §12.5. **外交** screen §10.1. **人物关系** §11.4. **文会 hall** §13.1–13.2.
- **功业录**: a long hand-scroll with seals by group; a map page with visited sites and the trade routes run.

### 16.4 正旦 · 年鉴 (the year review, month 1)

The one pushed screen of the year (it replaces the month news of month 1, ≤ 20 s, skippable): an illustrated almanac page with the year's numbers (cities, income, food, prestige gained, order), the three best moments of the year chosen from the log (a city level, a project, a 文会 win, an achievement, a marriage, a great battle), the general of the year (most exp gained) with his portrait and line, the missions completed, the coming year's calendar (festival month, 文会, tournament) and the mission offers waiting.

### 16.5 Sound, music and voices

Every new command (营建, 通商, 探索, the 外交 sub-actions) gets an icon, button behaviour and a sting from DOMESTIC-FX's system: this spec gives the content (§21); DOMESTIC-FX's §6–§8 rules apply (unique silhouette, 4 tiers, culture families). Festival stings and the 文会 / 投壶 music are culture-family pieces for the music task (request 7: ruler suites; the festival piece can be the faction suite's dance movement). Voice cue ids for request 12: `evt.card.<category>`, `evt.choice.good` / `.bad`, `fest.open`, `wenhui.win`, `debate.stance.<id>`, `oath.swear`, `marriage.accept`, `submit.offer`, `victory.wangdao`.

### 16.6 Comfort

All cards and screens work at 844 × 390 (two-column layouts), 1024 × 768 and 1920 × 1080; touch targets ≥ 44 px; text never below 13 px; reduced-motion mode replaces flying coins, ink splashes and camera moves with fades; colour is never the only signal (relation bars have numbers, bonds have shapes).

---

## 17. Economy impact and the reward loop in numbers

### 17.1 Sources and sinks of gold (scripted engaged player; shares of its gross base gold income over the game)

| Flow | Classic (2 × 36 games, 20 y) | World (24 games, 15 y) | Target | Notes |
|---|---|---|---|---|
| Base income (industry, town) | 100%: 102 → 302–467 a month over 20 years (v3 domestic script 127 → 347) | 100%: 121 → 459 by year 15 (v3 128 → 300) | — | v4 grows more through 归附 / 献城 and order, less through 开发 (tokens go to the new commands) |
| 民心 adjustment | ≈ +1% | ≈ +1% | −2 … +3% | peaceful realms keep order high |
| Events (gold equivalent, own cards) | +4.0 … +4.4% | +5.2% | +2 … +6% | choices by expected value |
| 国策 rewards | +8.0 … +8.9% | +9.2% | 4 … 10% | |
| 商队 net profit | +14.5 … +18.5% | +7.6% | 8 … 20% | hub-adjacent classic realms high, remote world realms low |
| 探索 gold | +3.1 … +3.6% | +2.1% | ≤ 4% | |
| 文会 / 武林 prize gold | +7.8 … +8.0% | +1.0% | ≤ 8% | a small classic court with famous scholars and warriors wins often |
| **Income from engagement** | **≈ 38 … 43%** | **≈ 25%** | | |
| Gifts | −13.0 … −13.8% | −15.7% | the player's choice | the script gifts every neighbour below +35 |
| Projects | −11.7 … −13.5% | −18.8% | −10 … −25% | 0.8–1.8 a year |
| Expedition supplies | −5.2 … −5.8% | −2.9% | | |
| Marriages, truces, tribute paid | −3.2 … −4.0% | −3.6% | | |
| Festivals | −1.3% | −1.3% | −1 … −4% | 盛大 0.4–0.5 times a year |
| **Spending on engagement** | **≈ 35 … 38%** | **≈ 42%** | | |

Net: the engagement systems roughly pay for themselves (classic +2 … +5%, world −17%, which the world realm repays in cities: 3.0 against 1.9). What they buy is what v3 could not: a peaceful realm with 1–1.5 more cities than the v3 script, generals 4–5 levels higher, ≈ 2 400–3 100 prestige, 14–21 achievements, ≈ 2–3 items a year and a victory path.

### 17.2 Other currencies

| Currency | Sources | Sinks / uses |
|---|---|---|
| Exp | routine (cap 20 / month), story | levels → titles (TITLES), slots (v3), 老练 at half (§4.4) |
| Prestige | §3.2 | fame (+10 max), diplomacy multipliers, unlocks, victory, score |
| Items | caravans 35–65% of trips, expeditions, missions, 文会, tournament, events | ITEMS (equip, gift, 图鉴) |
| Relation | gifts, pacts, events, caravans | non-aggression, pacts, 归附, victory |
| Order | drift, events, festivals, projects | income, harvest, growth, events, victory |

---

## 18. AI relevance

| System | AI uses it | How | Measured effect (§19) |
|---|---|---|---|
| 民心 | yes | same formulas; AI recruits lower it | AI income within ±0.1%; order 33–39 early, 61–62 late |
| Events | yes | same draw, `aiChoose` with reserve | +0.3 … +0.8% of AI gross; pace unchanged |
| Historical seeds | yes | same | — |
| Festivals | yes | 盛大 at 3× cost, else 从简 with reserve | prestige |
| Domestic exp | yes | same table and cap | average level 3.8 vs 3.0 at year 10; 老练 ×1.095 vs ×1.079 |
| Prestige → fame | yes | same | ≤ +10 fame, ≤ +1 battle AP |
| Relations and pacts | yes | AI skips pact partners and friends (≥ 60) as targets; bad relations never add aggression | pace unchanged (the rejected eager rule doubled it) |
| AI–AI tribute | yes (on) | §10.5 | 5–6 per classic game, ≈ 145 per world game; pace unchanged |
| AI → player proposals and demands | yes | §10.5, §10.9 | 0.5–0.7 alliance offers a year to a friendly player |
| 归附 | to the player only (AI–AI off) | §10.8 | 0.1–0.75 per player game |
| Bonds | yes | loyalty protection, battle bonuses (tactical only) | fewer defections in cascades; campaigns unaffected (auto-resolve ignores bonds) |
| 营建 | yes (on) | §8.6 | 175 projects per classic game; pace unchanged |
| 文会 / 武林 | yes | AI entrants | prestige and items flow to strong courts |
| Missions, caravans, expeditions, achievements, victory paths, mini-games | no | player-facing | — |

---

## 19. Measured results (prototype)

All numbers from `design-v4/engage/engage-proto.js` at the final configuration. Campaign outcomes are chaotic (one defection cascade or one lucky siege changes a run), so classic results are given for two independent sets of 48 seeds (1–48 and 1001–1048), whose difference is the noise floor.

### 19.1 All-AI campaigns (`campaign classic 48 25`, `SEED0=1000 … campaign classic 48 25`, `campaign world 24 15`)

Variants: **off** (v3 rules, this spec disabled), **on** (everything AI-relevant in §18 except AI tribute and AI projects), **no dom** (on without domestic exp), **+tribute** (on + AI–AI tribute), **+projects** (on + AI building).

| Metric | off | on | no dom | +tribute | +projects | Target |
|---|---|---|---|---|---|---|
| Classic: factions alive y5 / y10 / y25, set A | 6.5 / 4.9 / 4.6 | 6.5 / 5.0 / 4.6 | 6.6 / 5.1 / 4.8 | 6.7 / 5.0 / 4.7 | 6.3 / 4.9 / 4.5 | y10 ≥ 4.0 and ≥ off − 0.8 |
| Classic: same, set B | 6.3 / 4.3 / 3.9 | 6.7 / 4.9 / 4.6 | 6.7 / 4.7 / 4.4 | 6.8 / 4.8 / 4.6 | 6.5 / 5.0 / 4.6 | |
| Classic: median years to the first faction > 25% (A / B) | 2.3 / 2.1 | 1.9 / 2.2 | 2.1 / 2.2 | 2.1 / 2.3 | 2.2 / 2.2 | ≥ off − 0.5 and ≥ 1.5 |
| Classic: largest faction's cities at y10 (A / B) | 17.1 / 18.0 | 16.0 / 16.5 | 16.2 / 17.7 | 16.9 / 16.3 | 17.4 / 16.4 | ≤ off + 3 |
| Classic: games with a defection cascade (≥ 10 in a game), of 96 | 11 | 9 | 11 | 6 | 13 | ≤ off + 4 |
| Classic: events per faction-year; net value | — | 4.2; +0.5% | 4.2; +0.6% | 4.2; +0.4% | 4.2; +0.8% | AI −1 … +2% |
| Classic: average order y3 / y10 / y25 | — | 33 / 51 / 61–62 | 33–34 / 51–53 / 62 | 33–34 / 51–53 / 62 | 33–34 / 54 / 67–69 | 25 … 75 |
| Classic: average prestige of living factions y5 / y10 / y25 | — | 547–562 / 1 197–1 217 / 2 638–2 764 | 544–571 / 1 209–1 226 / 2 667–2 694 | 524–538 / 1 174–1 215 / 2 523–2 675 | 555–565 / 1 266–1 273 / 2 979–3 128 | — |
| Classic: average general level y10 / y25 | 3.0 / 3.3 | 3.8 / 5.3 | 3.2 / 4.0 | 3.8 / 5.2 | 3.8 / 5.4 | — |
| Classic: average 老练 multiplier y10 / y25, full weight → §4.4 half weight | 1.079 / 1.090 | 1.110 / 1.171 → **1.095 / 1.137** | 1.089 / 1.119 → 1.083 / 1.108 | 1.112 / 1.170 → 1.096 / 1.136 | 1.112 / 1.175 → 1.095 / 1.139 | half weight ≤ off + 0.03 at y10 |
| World: factions alive y5 / y10 / y15 | 50.5 / 45.5 / 44.3 | 49.6 / 44.6 / 43.5 | — | 50.4 / 45.4 / 44.0 | — | y15 ≥ off − 2 |
| World: Rome's fewest cities in any game at y3 / y5 | 21 / 20 | 22 / 18 | — | 21 / 21 | — | world.js: ≥ 6 / ≥ 12 |
| World: events per faction-year; net value | — | 3.1; +0.3% | — | 3.1; +0.3% | — | |
| World: average order y5 / y15; prestige y5 / y10 / y15 | — | 47 / 57; 273 / 546 / 751 | — | 47 / 56; 283 / 566 / 790 | — | |
| AI–AI tribute pacts per game | — | — | — | classic 5–6, world ≈ 145 | — | |
| AI projects per classic game | — | — | — | — | ≈ 175 | |

### 19.2 What was tried and rejected

| Variant | Result | Decision |
|---|---|---|
| Bad relations (≤ −50) make the AI 0.2 less demanding to attack | classic alive y10 2.8 vs 5.5 off, first > 25% at 1.5 y vs 3.6 y (8 seeds × 10 y) | rejected; bad relations only withhold friendship (env `NEED_EAGER=1` reproduces it) |
| Recruiting −1 order per 1 500 troops, drift ±1 | AI order averaged 25–30, income −2% | −1 per 3 000, drift +2 up / −1 down |
| 不和 drains 1 loyalty a month without a floor | defections 4.5 vs 2.2 per seed; 吕布 left 董卓 within three years in most seeds | floor 60 (the slander cards remain) |
| AI event options without a reserve | AI cities spent to 0 gold → the v1 rule −3 loyalty to all → cascades | reserve 60 + 6 × officers and 2 months of food |
| 游学 / 归省 could take the last officer | world: alive y15 41.8 vs 45.3 | never the last officer, never from a threatened city |
| Missions paying 1.5 / 2.5 / 4 months | 18.6% of the player's gross | 0.4 / 0.8 / 1.2 |
| Caravans 0.05 + 0.03/hop + 0.08 foreign, cap 0.45 | 18–20% of gross | 0.04 + 0.02 + 0.05, cap 0.30 |
| Prestige +8 per conquest, +4 per siege repelled | AI prestige 1 400 at y10 vs ≈ 400 for a peaceful player | +5 / +2 (only against ≥ equal troops), + city levels, projects, festivals |
| 王道 needing 3 大郡 and 50% of all factions as friends | 0–1 of 36 scripted games in 20 years | 2 大郡 or 1 都会, all neighbours + 4 factions |

### 19.3 Scripted players (`player classic 6 20`, `POL=v4 SEED0=500 … player classic 6 20`, `player world 4 15`)

Policies: **ai** (the player's faction played by the v3 AI), **v3** (a domestic script with v3 commands only: develop, recruit and train when threatened, search, hire, reward, occupy empty neighbours, 拉拢 lone garrisons), **v4** (the same script plus the systems of this spec: event choices by expected value, missions taken blindly, projects, caravans, expeditions, gifts, truces, marriages, trade pacts, festivals, 文会, tribute when threatened, accepting AI offers and 归附). Rulers: classic 刘备, 曹操, 孔融, 孙坚, 陶谦, 孟获; world 高句丽, 邪马台, 于阗, 百乘, 喀里多尼亚, 萨巴. v4 classic is given for two seed sets (A / B).

| Metric | ai | v3 | v4 |
|---|---|---|---|
| Classic survival y5 / y10 / y20 | 19 / 19 / 14% | 97 / 94 / 83% | A 97 / 94 / 89%, B 100 / 97 / 81% |
| Classic cities (survivors) y10 / y20 | 8.9 / 9.8 | 2.1 / 2.3 | A 1.9 / 2.1, B 2.3 / 3.1 |
| Classic base income per month y10 / y20 | 788 / 1 100 | 285 / 347 | A 203 / 302, B 247 / 467 |
| Classic average level y10 / y20 | 2.9 / 3.0 | 1.1 / 1.1 | 3.5 / 5.8–5.9 |
| Classic prestige y5 / y10 / y20 | — | — | 218–225 / 715–866 / 2 405–3 135 |
| Classic achievements y5 / y10 / y20 | — | — | 2.9 / 7.7–8.1 / 18.0–21.4 |
| Classic order y10 / y20 | — | — | 69–70 / 81–84 |
| Classic per year: missions done / caravans / expeditions / projects / items (rare) | — | — | 1.15–1.24 / 3.0–3.2 / 1.0 / 0.8–1.2 / 2.7–2.9 (0.29–0.43) |
| Classic per 20 y: empty cities / 献城 / 归附 | — | (not counted) | 0.67 / 0.36–0.50 / 0.11–0.36 |
| Classic 文会 wins / top 3 per 10 y; 武林 wins per 10 y | — | — | 1.0–1.7 / 2.6–3.9; 1.1–1.3 |
| Classic oaths / mentorships per 10 y | — | — | 0.17–0.24 / 0.42–0.57 |
| Classic cards: months with none; random / scheduled cards a year; months the cap deferred something | — | — | 52–54%; 3.0–3.1 / 3.4–3.5; 2.1–2.4% |
| Classic 王道 within 20 y (prestige 1 500) | — | — | A 9 / 36 (25%), median 13.3 y; B 8 / 36 (22%), median 11.3 y |
| World survival y15 | — | 92% | 100% |
| World cities y15 / base income y15 | — | 1.9 / 300 | 3.0 / 459 |
| World 归附 per 15 y | — | — | 0.75 (1.21 without the 36-month digestion rule) |
| World prestige y10 / y15; achievements y15 | — | — | 1 144 / 1 898; 14.1 |
| World cards: months with none; random / scheduled a year | — | — | 53%; 3.0 / 3.6 |
| World 王道 within 15 y (prestige 1 500) | — | — | 3 / 24 (12.5%), median 12.7 y |

Two notes on reading it: the ai policy shows how deadly the v3 world is for a faction that fights like the AI (the scripted defenders hold better because they recruit and train only against threats); and the scripted v4 player takes missions and uses systems blindly, so a human's ceiling is higher.


---

## 20. Save data (v4)

One save-version bump shared with the other v4 specs (integration owns the number). New fields:

```js
city.order = 0…100
city.eng   = { buffs: [{ mod, v, until }], flags: { plague: until, … }, cd: { [eventId]: monthIndex }, lastDis, proj: { id, work, sup } | null, done: [projectId…], maxLv }
faction.prestige = int
faction.eng      = { fameEv, virtueEv, cd: {…}, recent: {…}, refused: { [factionId]: until }, lastDemand, festivalAuto: 'grand'|'modest'|'skip'|null }
gen.dexp = int                          // non-battle exp (§4.4)
gen.away = { kind: 'caravan'|'exped'|'hostage'|'leave'|'injured', until, home, ref } | null
G.engage = {
  rel: [int…] (n × n, saved as a run-length string), war: [monthIndex…] (n × n, sparse map 'a-b' → monthIndex),
  pacts: [{ a, b, kind, since, until, payer, pay, hostage }],
  bonds: { 'a-b': { k, a, m, since } },  // only pairs with |a| ≥ 1 or a kind
  missions: [{ id, tier, city, since, until, done, start: {…}, rewardBase }],
  caravans: [{ id, from, to, hops, inv, m, risk, until, leader, foreign, back }],
  exped: [{ id, site, hops, until, leader, back }],
  counters: { 'f:key': n }, ach: [achievementId…], visited: [siteId…], visitedAt: { siteId: monthIndex },
  clues: [{ item, city }], vlog: { … }, log: [{ m, kind, ref }], seq,
}
```

- Size: world ≈ 25 KB (relations 5 KB as RLE, bonds ≈ 1 500 pairs × 20 B, counters, logs capped at 200 entries).
- Migration from v3 saves: `Engage.migrate(G)` sets order from the start formula, prestige 0, the starting relations and bonds, `dexp = 0`; `GameState.migratedFrom` notes it.
- `Engage.enabled = false` (tests, comparison): no state is created and every hook returns its v3 value.

---

## 21. Integration points (by file and function)

| File | Function | Change |
|---|---|---|
| `model.js` | `Commands.develop / train / recruit / search / hire / discord / persuade` | after the v3 body: `SG.Engage?.onCommand(cmd, res)` → domestic exp, order (recruit), counters; `Commands.lastResult` (DOMESTIC-FX) is the input |
| `model.js` | `Commands.allyChance` | `+ Engage.rel(f, t)/400` before the clamp |
| `model.js` | `Commands.ally` | success → `Engage.addRel(f, t, 10)`, prestige +5 |
| `strategy-ai.js` | `actCity` target filter, `threat()`, `recheckBattle` | `!g.allied(n.owner, f) && !Engage.noAttack(n.owner, f)` (pacts, relation ≥ 60): partners are neither targets nor threats |
| `strategy-screen.js` | `cmdAttack` | pact partners stay in the list with a 「背约」 tag and a cost confirmation |
| `model.js` | `GameState.generalsIn` | skip generals with `away.until > monthIndex` |
| `model.js` | `Progress.gain(gen, n, why)` | `why !== 'battle'` → `gen.dexp += added`; `Progress.vetK` uses `Engage.vetLevel(gen)` |
| `strategy-ai.js` | `endMonth` | before the defection loop: `Engage.loyaltyShield()` (sworn / kin / spouse, restored after); after income: `Engage.monthEnd(news)` (order, income and harvest adjustments, projects, caravans, expeditions, missions, events, festivals, 文会, AI diplomacy and projects, achievements) |
| `strategy-ai.js` | `attackNeed(f, t)` | `+ Engage.needK(f, t)` (only the tribute-refusal −0.15) |
| `strategy-ai.js` | `Conquest.prepare / apply` | war memory, relation changes, pact breaking, order on conquest, prestige, shared-victory affinity, `_lastBattle` |
| `strategy-ai.js` | `SG.cityDefense` | `+ Engage.defense(c)` (walls +10, quake buffs) — TITLES adds its `garrison` in the same function |
| `battle-model.js` | `atkPower`, `defFactor`, morale at start, rout handler | bond terms (§11.3) |
| `duel-game.js` | `Fighter` | rival +4% damage; `mode: 'friendly'` |
| `strategy-screen.js` | `run()` | month start: `Engage.monthStartCards()` → event card, scheduled card (R1); year review in month 1 |
| `strategy-screen.js` | `refreshCityPanel` | commands 营建, 通商, 探索; 外交 opens the new screen; 民心 bar, project ring |
| `strategy-screen.js` | `buildHud` | 声望 seal, 国策 slips, 年历 ribbon, 王道 tracker (with the main-interface task) |
| `strategy-screen.js` | `cmdTrade` | 常平仓 prices |
| `domestic-fx.js` | `define` | new scripts: `build.start` / `build.done` (anchor = the project's), `caravan.depart` / `caravan.return`, `exped.depart` / `exped.return`, `gift`, `marriage`, `tribute`, `truce`, `tradepact`, `submit`; tiers: caravan great when profit ≥ 300 or a rare came home; expedition great on a rare or a clue; marriage always great |
| `game.js` | title, new game | 功业阁 shelf; 三十年之约 option |
| ITEMS (`items.js`) | `pool(...)`, `fame(f)`, `virtue(f)`, search | the pools of §12.3; prestige and event buckets inside effFame / effVirtue; the treasure-map ×2 in the hidden-item roll |
| TITLES (`titles.js`) | passed-over hook | `Engage.addAff(passed, holder, −10)` |
| tournament spec | prizes | prestige +50 / +25 / +12; finalist bonds |

---

## 22. Test plan and balance targets

### 22.1 `tests/engage.js` (new, Node, exit code = failures; `--quick` uses 12 / 6 seeds)

1. **Catalogue**: everything `build-json.js` checks; plus every text placeholder resolves for every family variant; every persona's cities exist; every project has a name for each of the 12 families after fallback; every site is reachable from at least one start position.
2. **Rules** (table tests): order start / target / drift / recruit / conquest; income, harvest and growth multipliers; prestige sources and the fame / virtue buckets and caps; relation changes, drift and pact floors; gift, alliance, marriage, tribute, demand, truce and trade-pact chances; hostage outcomes; betrayal; 归附 eligibility and probability; bond formation thresholds and caps; domestic exp table and the cap of 20; `vetLevel`; mission goal checks and rewards; project progress, requirements and effects; caravan margin, risk, time and returns; expedition outcomes; 文会 scoring and prizes; festival options; achievements; 王道 / 天命 / 万国来朝 conditions; score.
3. **RNG contract**: a counter around `SG.Random.value / rangeInt / rangeFloat` reads 0 inside every `SG.Engage.*` call during a 5-year all-AI run. With `Engage.enabled = false`, the 5-year classic all-AI game's JSON is md5-identical to the game without `engage.js` loaded (as `tests/world.js` §3).
4. **Campaign balance**: the targets of §19.1 (classic 2 × 48 seeds × 25 years, world 24 seeds × 15 years; ≈ 5 minutes).
5. **Player balance**: the scripted players of §19.3 with these targets (averaged over the two classic seed sets): v4 survival at year 20 ≥ v3 − 8 pp (classic) and ≥ v3 (world); v4 base income at year 20 ≥ 0.8 × v3 and v4 base income + net engagement gold ≥ v3; missions 4–10%, caravans 8–20%, expedition gold ≤ 5%, prize gold ≤ 8% of gross; achievements ≥ 0.5 in year 1 on average and 0.5–1.5 a year after; 王道 reached in 10–40% of v4 runs within 20 years (classic) with a median of 10–17 years, and never before year 6.
6. **Anti-busywork** (in the v4 harness, counting what the player would see): ≤ 1 random card and ≤ 1 scheduled card in any month; random cards 3–7 a year for a 1–5 city realm; ≥ 35% of months without any card; no event twice for the faction within 6 months; no disaster twice in a city within 12 months; every card has a free AI-selectable option; no card offers an option the city cannot pay for.
7. **Performance**: `Engage.monthEnd` ≤ 3 ms (classic) and ≤ 8 ms (world) CPU per month; `tests/world.js`'s 60 ms per-month cap still passes with Engage on.
8. **Save**: round trip; v3 save migration; `enabled = false` writes no Engage fields.

### 22.2 Changes to existing tests

- `tests/sim.js` (7f) compares progression on and off and expects identical city ownership: it runs with `SG.Engage.enabled = false` (events read levels: the 求学 card, mentors, the 文会 scores). (7g) / (7h) run twice, Engage off and on (domestic exp, `vetLevel`); their v3 bands stay (damage per battle ≤ 1.35 × off, learned-special ratios). New section **(9) engagement**: a 12-seed short version of §22.1 items 3 and 4, and a tactical test of bonds: armies with two sworn brothers vs none (same generals otherwise) win 2–5 pp more often; armies with a 不和 pair 2–5 pp less.
- `tests/world.js` gets `--engage` (loads `engage-data.js`, `engage.js`, the battle files) and checks the same region, Rome and timing assertions plus alive at year 15 ≥ off − 2.
- `tests/duel-bots.js`: a mirror match between 宿敌 (both +4% damage) stays 47–53%; a one-sided +4% (for the test only) wins 52–56%.

### 22.3 Browser

`tests/engage.html`: every event card in every family variant, every screen and mini-game with scripted states; 844 × 390 and 1280 × 720; touch targets ≥ 44 px; reduced motion; no `console.error`; the debate and 投壶 mini-games at 60 fps desktop / 30 phone.

---

## 23. Unity port (after the browser build is stable)

| Unity file | Change |
|---|---|
| `Assets/Sanguo/Scripts/Data/EngageData.g.cs` (new) | generated by `tools/gen-engage.js cs` from `engagement.json` (events as structs with effect lists, missions, projects, sites, festivals, achievements, bonds, relations) |
| `Assets/Sanguo/Scripts/Model/Engage.cs` (new) | rules port of `js/engage.js`; the same seeded streams (`SeededRandom` + the same FNV hash of the same strings), so the C# harness reproduces the JS numbers |
| `GameState.cs`, `Commands.cs`, `StrategyAI.cs`, `Conquest.cs`, `BattleModel.cs`, `Progress.cs`, `DuelSim.cs` | hooks of §21 |
| `UI/EventCard.cs`, `UI/MissionBoard.cs`, `UI/BuildDialog.cs`, `UI/CaravanDialog.cs`, `UI/ExpeditionOverlay.cs`, `UI/DiplomacyScreen.cs`, `UI/RelationGraph.cs`, `UI/WenhuiHall.cs`, `UI/DebateGame.cs`, `UI/TouhuGame.cs`, `UI/AchievementScroll.cs`, `UI/YearReview.cs`, `UI/Leaderboard.cs` (new) | UGUI versions of §16 |
| `Tests~/engage/` | catalogue checks and the rule harness against JS references (`node tests/engage.js --refs`), exact to 1e-9 |

---

## 24. Open questions

- **Q1. Domestic exp and 老练.** This spec owns domestic exp (TITLES Q1) with TITLES' table and cap 20, plus 政绩 (TITLES Q10: 3 + 1 at order ≥ 75, + title tier). The half-weight 老练 rule (§4.4) is new: does the combat owner accept it, or prefer full weight?
- **Q2. 民心 strength.** ±5% income and ±5% harvest keep the AI economy neutral. Should order matter more (±10%) for the player's feel, at the cost of a re-tune?
- **Q3. AI–AI tribute and AI projects** are on by default (measured neutral). Keep them on, or off for the first release?
- **Q4. AI–AI 归附** is off: it would let strong AI courts absorb small ones without war and change world pace. Turn it on later with its own tests?
- **Q5. Marriage text.** The marriage card is abstract (no general moves). Should famous marriages (孙夫人 → 刘备) be scripted with named women where history names them?
- **Q6. Expedition items.** Should ITEMS add a small pool of expedition-only rares (one per site, e.g. a 楼兰 tablet, a Delphic tripod) rather than drawing from unowned shop rares?
- **Q7. Tournament prestige and finalists' rivalry** are proposals to the tournament spec.
- **Q8. 王道 thresholds** (prestige 1 500, order 75) are calibrated on a blind script; playtests may want 2 000 for classic and a separate number for the world.
- **Q9. Event art.** One painted vignette per event × 12 families is 30 × 12 = 360 paintings; the minimum is one per event (30) with family tints. Which does the art pilot prefer?
- **Q10. Festival grades C** (倭, 尼罗河, 南海) are designed fills. Do the writers want them named as such in the UI, or presented without the grade?
- **Q11. 文会 frequency.** Once a year in month 9 next to the harvest (month 7). Every two years would make it rarer and grander.
- **Q12. The 三十年之约** grading curve needs playtest data (the reference curve is the median score of the scripted v4 player).

---

## 25. Sources

The session's proxy refused WebFetch (DNS) for every site, so facts were checked through WebSearch result pages and the sources they cite, plus standard references; where the search could not confirm a detail it is marked B or C in `engagement.json`, or worded with care here.

- **东夷传 festivals**: 三国志·魏书·东夷传 (濊: 「常用十月节祭天，昼夜饮酒歌舞，名之为舞天」; 高句丽 东盟; 夫余 迎鼓). [Eastern Ye](https://en.wikipedia.org/wiki/Eastern_Ye); [Sacrifice to Heaven](https://en.wikipedia.org/wiki/Sacrifice_to_Heaven); [고구려의 역사적 정체성 (教保 scholar)](https://scholar.kyobobook.co.kr/article/detail/4010023450524).
- **匈奴 gatherings**: 史记·匈奴列传 (正月 单于庭, 五月 茏城, 秋 蹛林). [松下宪一《匈奴の龍城》 (埼玉大学)](https://sucra.repo.nii.ac.jp/record/2000581/files/P0000369.pdf); [Chinese Notes 史记 110](https://chinesenotes.com/analysis/shiji/shiji110_analysis.html). 鲜卑 spring gathering on the 饶乐水: 后汉书·乌桓鲜卑列传 (from memory; not confirmed by the search, grade A kept with this note).
- **月旦评**: 后汉书·许劭传 (「清平之奸贼，乱世之英雄」) and 孙盛《异同杂语》 (「治世之能臣，乱世之奸雄」). [中国作家网](https://www.chinawriter.com.cn/n1/2023/0807/c442005-40051897.html); [The News Lens](https://www.thenewslens.com/article/68491).
- **司马徽**: 三国志·蜀书·诸葛亮传 裴注引《襄阳记》. [中华文化 诸葛亮 (chiculture.org.hk)](https://chiculture.org.hk/sites/mainsite/files/2021-11/0104_zhugeliang_pdf_cn.pdf).
- **194 locusts / 208 and 217 plagues**: 三国志·武帝纪 (兴平元年, 建安十三年); 曹植《说疫气》. [The Paper on 建安疫](https://www.thepaper.cn/newsDetail_forward_5914280); [Positions Politics: Cao Zhi, The Plague Airs](https://positionspolitics.org/cao-zhi-192-232-the-plague-airs/); [中国医学伦理学 2024](https://cjournal.hep.com.cn/1001-8565/CN/10.12026/j.issn.1001-8565.2024.03.19).
- **Antonine plague**: [Antonine Plague](https://en.wikipedia.org/wiki/Antonine_Plague); [World History Encyclopedia](https://www.worldhistory.org/Antonine_Plague/).
- **上巳 and 大傩**: 续汉书 (后汉书)·礼仪志 (「是月上巳，官民皆絜于东流水上……为大絜」; 先腊一日大傩). [儒家中国](https://www.rujiazg.com/article/20401); [zdic 春禊](https://www.zdic.net/hant/春禊).
- **屯田 196, 芍陂, 铜雀台 210**: 三国志·武帝纪 and 刘馥传 (from memory; search pages: [163.com on 枣祗](https://c.m.163.com/news/a/L6JS7IUP0521WC6C.html), [Anfeng Pond (淮南)](https://www.huainan.gov.cn/HUAINANCHINA/News/1260666136.html)).
- **Hostage 202**: 三国志·吴书·周瑜传 注引《江表传》. [维基文库 三国志 卷54](https://zh.wikisource.org/wiki/三國志/卷54).
- **和亲**: 史记·刘敬叔孙通列传 / 汉书·娄敬传. [The Paper](https://www.thepaper.cn/newsDetail_forward_28022456).
- **卑弥呼 239**: 三国志·东夷传·倭人. [Himiko](https://en.wikipedia.com/wiki/Himiko); [広島大学](https://ir.lib.hiroshima-u.ac.jp/52335/files/42213).
- **Commodus' peace of 180**: Cassius Dio 73.2 (terms from memory; the search confirmed the peace and its date). [Commodus](https://wikipedia.com/wiki/Commodus).
- **Palmyrene caravans**: honorific inscriptions for synodiarchs (Soadu 132, Nesa 142 / 150, Taimarsu 193, Yaddai 210/211). [AGRW: Taimarsu 193](https://philipharland.com/greco-roman-associations/honors-for-taimarsu-the-caravan-chief-by-a-caravan-193-ce/); [Palmyra database, Oldenburg](https://palmyra.uni-oldenburg.de/attestation/597).
- **Muziris papyrus**: [De Romanis, Topoi Suppl. 15](https://art.torvergata.it/bitstream/2108/192383/2/DeRomanis_Topoi-Suppl15.pdf); [ancientportsantiques](https://www.ancientportsantiques.com/wp-content/uploads/Documents/PLACES/IndOc-Gulf/MuzirisPapyrus-Romanis2012.pdf).
- **Agon Capitolinus**: [Capitoline Games](https://en.wikipedia.org/wiki/Capitoline_Games); [Imperium Romanum](https://imperiumromanum.pl/en/curiosities/capitoline-agon-and-albanian-agon/).
- **Pythian music contests** (background for 文会 abroad): Pausanias 10.7. [Perseus 10.7](https://cts.perseids.org/read/greekLit/tlg0525/tlg001/perseus-eng2/10.7.1-10.7.7).
- **Saturnalia**: [Saturnalia](https://en.wikipedia.org/wiki/Saturnalia); [Ashmolean Latin inscriptions blog](https://blogs.ashmolean.org/latininscriptions/2014/12/12/on-the-feast-of-saturnalia-my-master-gave-to-me/index.html).
- **Coligny calendar**: [Coligny calendar](https://en.wikipedia.org/wiki/Coligny_calendar).
- **Nerthus**: Tacitus, Germania 40. [Nerthus](https://en.wikipedia.org/wiki/Nerthus).
- **Indra festival**: Silappatikaram, Manimekalai. [Tamil Arts Academy (Nagaswamy)](https://tamilartsacademy.com/books/Mirror/chapter_14.html); [Indianetzone](https://www.indianetzone.com/24/indra_vizha_festival_sangam_age.htm).
- **Nowruz / Mehragan**: [Encyclopaedia Iranica: Mehragan](https://iranicaonline.org/articles/mehragan); [Iranologie: Nowruz in history](https://iranologie.com/the-history-page/nowruz-in-history/).
- **鸿都门学 178, 白虎观 79**: 后汉书·蔡邕传; [Cambridge, Early China: the White Tiger Hall conference](https://cambridge.org/core/journals/early-china/article/abs/way-to-the-white-tiger-hall-conference-evidence-gleaned-from-the-formation-process-of-the-baihu-tong/1D3348F1AD6BFD23B7894A2665157B94).
- **投壶**: 礼记·投壶 (standard reference).
- **Peaceful submissions**: 韩馥 → 袁绍 (191), 刘琮 → 曹操 (208), 士燮 → 孙权 (210), 张鲁 → 曹操 (215): 三国志 (standard references).
