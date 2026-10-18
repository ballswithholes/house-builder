# 三国志 · 霸王的世界 — v4 integration contract (INTEGRATION.md)

The nine v4 specs (`ITEMS.md`, `TITLES.md`, `ENGAGEMENT.md`, `TOURNAMENT.md`, `COMBAT-V4.md`, `DOMESTIC-FX.md`, `CITY-VIEW.md`, `UI-V4.md`, `AUDIO-V4.md`) were written in parallel. Each one is correct on its own; together they stacked bonuses, paid from the same gold twice, wrote the same state under different names and scheduled two cards into one month. This file is the result of the consolidation review (27 findings). **It wins over the nine specs for everything that crosses a system boundary**: the caps and the order in which bonuses combine (`SG.Mods`), the economy formulas (`SG.Econ`), the AI's spending (`SG.AIBudget`), the save format and migration, the month pipeline, shared registries, the load order and the test matrix. Each spec still wins for its own content (which items exist, which titles, which events, how a duel plays). The specs were edited to agree with this file and link here.

Reference implementation (design tooling, not game code): `design-v4/integration/` — `econ-proto.js` (SG.Mods / SG.Econ / SG.AIBudget as they will be ported), `items-mod.js`, `titles-mod.js`, `tourney-mod.js` (the three systems' rules in fast form), `v4-all-proto.js` (the all-on campaign harness), `duel-gear-estimate.js`. Results in `integration/results/`. Platform order: browser first, then Unity (§14). Every visual follows the Lanternvale art direction of the art pilot (`js/lowpoly.js`, `js/lp-pilot.js`); nothing in this file changes art.

---

## 0. Key numbers at a glance

| Topic | Decision |
|---|---|
| Battle caps | product of all positive non-level multipliers ≤ **×1.60** atk, ≤ **×1.60** def; negatives floor **×0.50**; start morale bonus ≤ **+25**; crit add ≤ **+6 pp**, crit multiplier add ≤ **+0.30**. 老练 (level) stays outside the cap |
| Domestic caps | 开发 **+80 %**; city income **−10 … +35 %** (event buffs separately, floor −50 %); harvest **−20 … +35 %**; 征兵 troops per gold **+40 %**; 训练 flat **+12**; 搜索 p ≤ 0.90, 登用 ≤ 0.95, 同盟 / 离间 / 拉拢 ≤ 0.90; fame bonus (prestige + events + gear) **≤ +10** total; virtue bonus −10 … +15; exp multiplier **≤ +40 %**, applied before the routine cap of 20 / month |
| AI budget | city reserve **R(c) = 150 + 8 × officers gold, 2 months of food**; discretionary purse = **22 %** of gross income (20 % left the world's gear spending at 7.8 %, under ITEMS' 8–15 % band), capped at 12 months; fixed order recruit → develop → tribute → event → title → gear → project → festival; nothing discretionary below R(c) |
| Gates (all switches on, measured) | classic 2 × 48 × 25 y: factions alive y10 **4.77** vs 4.94 off and **4.48** vs 4.29 (gate ≥ off − 0.8); first faction > 25 %: 2.08 vs 2.33 y and 2.00 vs 2.08 (≥ off − 0.5); defections / seed 1.54 vs 2.04 and 3.27 vs 6.31 (≤ off + 2). World 24 × 15 y: alive y15 **43.42** vs 44.29 (≥ off − 2). Tactical: attacker win −0.2 pp (≤ 4), damage ×0.999 (≤ 1.35). Scripted peaceful player: survival y20 89 / 100 % vs v3 83 / 94 % (≥ v3 − 8 pp); 王道 14 / 19 % classic (10–40 %; world 8 %, n = 24, not gated) — **all pass** |
| AI spending (share of gross income) | classic: gear 11.2–11.4 %, titles 2.0 %, projects 5.8 %, festivals 0.6 %, tribute 0.05 % → 19.5–19.7 %; world: 8.5 / 1.8 / 7.0 / 1.1 / 0.7 → 18.4 %; cities below 0 after a discretionary spend: **0** |
| Duel gear | one **all-in duel budget per fighter**: dmg % + 0.4 × (HP + ability HP + guard HP) ≤ **6**; ability HP ≤ 4; `duelWar = war + min(3, ⌊gear 武力 / 3⌋)`. Legendary best-in-slot vs ungeared at equal 武力: **69–73 %** (was 99–100 %); gear never bridges 15 武力 (23 %) |
| Save | one version **4** (`sanguozhi2_save_v4`), migration order ITEMS → TITLES → ENGAGEMENT → TOURNAMENT → COMBAT / DOMESTIC; `gen.injury = { until, war, away }`; `G.tourney.wins` |
| Month-start budget | the non-interactive beats of a month start (year review, seals, stage-ups, harvest montage, transition) ≤ **8 s** in total, the rest become agenda slips or news lines; ≤ 60 ms CPU per AI faction-month; the January tournament is exempt from the 60 ms cap (≤ 1.1 s world, sliced) |
| CI | quick suite ≤ **6 min**, nightly ≤ **45 min** (§13) |

---

## 1. Authority, files and ownership

### 1.1 Who owns what

| Shared concern | Owner (implementation) | File | The specs keep |
|---|---|---|---|
| caps, battle multiplier aggregation, fame / virtue / AP accessors, `effLoy`, `loyAura`, exp multiplier | integration | `js/econ.js` (`SG.Mods`), `js/battle-math.js` (battle part, used by `battle-model.js` and `clash.js`) | their stat numbers as **providers** (§3.3) |
| develop / income / harvest / recruit / train / probability formulas | integration | `js/econ.js` (`SG.Econ`) | which bonus applies when |
| AI spending | integration | `js/econ.js` (`SG.AIBudget`) | each AI's choice of *what* to buy |
| save version 4, migration | integration | `js/model.js` `GameState.save/load` + `js/migrate-v4.js` | their own fields (§6.1) |
| the month pipeline | integration | `js/strategy-ai.js` `endMonth` (one ordered list of phases, §6.3) | the content of each phase |
| registries: settings, voice cues, music stings, UI icons, figure clips, scheduled cards | UI (settings, icons), AUDIO (cues), DOMESTIC-FX (stings), COMBAT (clips), ENGAGEMENT (cards) | §10 | their entries |
| load order | integration | `index.html` (§11) | — |
| test matrix and CI budget | integration | `tests/run-all.js` (§13) | their suites |

### 1.2 The switches

Every v4 system can be switched off independently, which is how the gates of §9 are measured and how a bug can be isolated in the field. A switched-off system creates no state, draws no random numbers and its providers return 0.

| Switch | Default | Off means |
|---|---|---|
| `SG.Items.enabled` | true | no gear, shops, prizes; `eff(gen, k) = gen[k]` |
| `SG.Titles.enabled` | true | no offices; `admin(c)` = governor |
| `SG.Engage.enabled` (+ `.events .order .prestige .projects .bonds .diplomacy .trade .exped .festivals .wenhui`) | true | the v3 economy |
| `SG.Tourney.enabled` | true | no 武林大会 |
| `SG.Crit.enabled` (COMBAT-V4 §2) | true | no army crits |
| `SG.Progress.enabled` (v3) | true | levels off, crit level term 0 |
| `SG.DomesticFx.mode` | 'smart' | 'off' = v3 dialogs (presentation only, no rule) |
| `SG.Mods.CAPS` | §3.1 | tests may override per instance (`create()` copies the table) |

---

## 2. The all-on prototype

`node design-v4/integration/v4-all-proto.js <mode> <scenario> [seeds] [years]` loads the v3 game files in Node, text-patches them in memory with asserted hooks (every patch throws if its anchor text is missing, so the harness cannot silently drift from the code), loads ENGAGEMENT's prototype from its own source with ~18 asserted patches, and plugs in `items-mod.js`, `titles-mod.js`, `tourney-mod.js` through `econ-proto.js`. Modes:

| Mode | What | Runtime |
|---|---|---|
| `campaign classic 48 25` | all-AI, variants off / all / nocap (`VARIANTS`, `SEED0`) | 194 s (2 variants), 367 s (3) |
| `campaign world 24 15` | the same, world map | 339 s |
| `player classic 6 20` / `player world 4 15` | ENGAGEMENT §19.3's scripted peaceful player, policy v3 vs all | 108 s / 310 s |
| `tactical classic 8` | year-8 all-on armies from 8 campaigns, 480 battles per mode, battle effects off / on / onNoCrit | — |
| `stacks classic` | the capped and uncapped extremes (§8) | 1 s |
| `refs classic` / `refs world` | `SG.Econ` / `SG.Mods` test vectors (`results/econ-refs-*.json`, 557 vectors each, 24 of them stacked battle vectors) for `tests/econ.js --refs` and Unity | 1–5 s |

Approximations, stated once: ITEMS abilities are not simulated one by one in campaigns — a battle-scope ability counts as an attack stand-in inside the cap (rare 3 %, epic 7 %, legendary 10 %, total ≤ 12 %); domestic and world abilities (command bonuses, income, harvest, loyalty aura, fame, virtue, diplomacy, exp) are applied exactly. Tournament matches use the measured win curve `p = 1 / (1 + e^(−Δ duelWar / 6))` instead of the duel engine (TOURNAMENT §4.3). Everything else is the real v3 code plus the spec rules.

---

## 3. `SG.Mods` — caps and the order of combination

### 3.1 The cap table (`SG.Mods.CAPS`, `rules` data in `js/econ.js`)

| Key | Value | What it caps |
|---|---|---|
| `atk`, `def` | 1.60 | product of the positive battle factors of §3.2 (gear × title / commander × bond × aura × conditional) |
| `negFloor` | 0.50 | product of the negative factors (feud, cursed items, confusion) never below ×0.5 |
| `morale` | 25 | start-morale bonus (gear + title) |
| `critAdd`, `critMul` | 0.06, 0.30 | army crit chance from gear + title; crit multiplier added by gear |
| `develop` | 0.80 | 开发: gear (by kind) + title + project |
| `incomeLo`, `incomeHi` | −0.10, +0.35 | city gold: admin title + national title + admin's items + projects + trade pact + order term |
| `eventIncomeFloor` | −0.50 | the separate event-buff factor |
| `harvestLo`, `harvestHi` | −0.20, +0.35 | harvest: titles + items + projects + events + order |
| `recruit` | 0.40 | 征兵 troops per gold |
| `train` | 12 | 训练 flat bonus |
| `search`, `hire`, `ally`, `discord`, `persuade` | 0.90, 0.95, 0.90, 0.90, 0.90 | the final clamp of each probability (one clamp, after every term) |
| `fameBonus` | 10 | prestige fame (⌊prestige / 150⌋) + event fame + gear fame, **one cap for every use** (action points, diplomacy, hire) |
| `virtueLo`, `virtueHi` | −10, +15 | event + gear virtue |
| `exp` | 0.40 | ITEMS exp stat + `expBoost` abilities |
| `routineCap` | 20 | ENGAGEMENT §4.1 routine domestic exp per general per month (after the multiplier) |
| `loyAuraCap` | 95 | loyalty auras never lift above 95 |

The table is data, not constants in code: Unity reads the same values from the generated rules (§14), and `tests/econ.js` asserts the JS and the generated tables agree.

### 3.2 Battle: one aggregator (`js/battle-math.js`)

```
atkMul(u, model, cond) = capped(pos, neg, CAPS.atk)
  pos / neg from four groups, each group ONE factor (1 + x):
    1 gear        ITEMS atk %                                  (≤ 0.40 by ITEMS §2.5)
    2 title       TITLES personal atk + the commander's cmdAtk (one sum)
    3 bond        ENGAGEMENT bonds and 义愤 (−0.08 … +0.08)
    4 aura        ITEMS battle auras and battle abilities (≤ 0.12)
  + conditional terms passed by the caller (terrain-specific item effects, COMBAT conditions)
capped(pos, neg, cap) = min(cap, max(1, Π pos)) × max(CAPS.negFloor, min(1, Π neg))
defMul(u, model)      the same with def keys
the v3 terms stay where they are: level (老练 1 + 0.04 × (lvl − 1)), formation, terrain, tactic, morale, troops
```

- `battle-model.js` `atkPower` multiplies by `atkMul`, `defFactor` by `defMul`; `clash.js` reads the same two numbers for its labels. No spec multiplies a battle number anywhere else.
- **Army crits** (COMBAT-V4 §2): `critChance(u) = clamp(0.02 + 0.0015 × max(0, war − 50) + 0.004 × (vetLevel − 1) + FORM_CRIT[formation] + min(0.06, gear + title) + (morale ≥ 90 ? 0.01 : 0) − (confused ? 1 : 0), 0, 0.25)`; `critK(u) = 1.5 + min(0.30, gear critMul)`. **`vetLevel`** is the 老练 level with non-battle exp at half weight (`Progress.levelFor(exp − 0.5 × dexp)`, ENGAGEMENT §4.4), the same level that drives 老练, so domestic work raises crits only half as fast as fighting. The crit roll is drawn first in `attack()` so the order of the existing draws is unchanged when crits are off.
- `startMorale(u, base) = min(100, base + min(25, gear + title))`.
- Formation requirements (ITEMS §2.4, COMBAT) read **base** attributes, never `eff()`: gear does not unlock formations.

### 3.3 Providers

`js/econ.js` takes its numbers from providers `P.*` that each module registers (`SG.Mods.provide({ gearAtk: …, titleAtk: … })`). A missing provider contributes 0, so any subset of v4 can be on. The provider names are those of `econ-proto.js` (`gearAtk`, `gearDef`, `titleAtk`, `cmdAtk`, `bondAtk`, `auraAtk`, `gearMorale`, `titleMorale`, `gearCrit`, `titleCrit`, `gearCritMul`, `vetLevel`, `gearDev`, `titleDev`, `project`, `titleCity`, `titleNat`, `itemCity`, `order`, `pactIncome`, `eventIncome`, `eventHarvest`, `gearRecruit`, `gearTrain`, `titleTrain`, `gearSearch`, `titleSearch`, `gearHire`, `titleHire`, `gearDip`, `titleDip`, `rel`, `gearDiscord`, `titleDiscord`, `gearPersuade`, `titlePersuade`, `persuadeRes`, `prestigeFame`, `eventFame`, `gearFame`, `eventVirtue`, `gearVirtue`, `expBoost`, `titleShield`, `bondShield`, `titleLoyAura`, `itemLoyAura`, `eff`, `admin`, `baseFame`, `baseVirtue`).

### 3.4 Faction and general accessors

| Accessor | Rule |
|---|---|
| `Mods.fame(f)` | `clamp(base fame + min(10, prestige fame + event fame + gear fame), 0, 100)`. Every v3 read of `faction.fame` goes through it (action points, diplomacy, 登用). ENGAGEMENT's `syncFV` no longer writes `faction.fame`; it writes `fameEv` |
| `Mods.virtue(f)` | `clamp(base + clamp(event + gear, −10, +15), 0, 100)` |
| `Mods.actionPoints(f)` | v3 formula on `Mods.fame(f)` |
| `Mods.effLoy(gen)` | `loyalty + max(title shield, bond shield)` — the larger shield, never the sum. Used by the v3 defection check and by 拉拢 |
| `Mods.loyAura(c)` | `max(title loyAura, item loyAura)` of the city's administrator, applied after defections, never above 95 |
| `Mods.exp(gen, amt)` | `round(amt × (1 + min(0.40, exp stat + expBoost)))`, **then** ENGAGEMENT's routine cap (20 / month for routine domestic exp). Battle exp keeps the v3 path with the same multiplier |

---

## 4. `SG.Econ` — the one domestic pipeline

Every formula takes the v3 random roll as an argument; `SG.Econ` never draws. `Commands.*` and `StrategyAI` call these functions, so the player and the AI use the same numbers. Integers are rounded once, at the end, with `M.roundToInt` (`Math.round` on gain); Unity uses the same order (§14).

### 4.1 开发 `Econ.develop(c, g, kind, roll)`

```
g0   = DevelopBase + ⌊eff(g, pol) / DevelopPolDiv⌋ + roll          // roll = the v3 R(0, 4)
pct  = min(0.80, gear(kind) + title dev + project dev)             // ITEMS kind stats (land / ind / town), TITLES §7.1, ENGAGEMENT projects (铁官 +10 % industry)
gain = round(g0 × (1 + pct)),  stat = min(999, stat + gain)
```

### 4.2 City gold `Econ.cityGold(c)`

```
b    = industry × GoldPerIndustry + town × GoldPerTown
pct  = clamp(admin title income + national title income + admin's item income + projects + trade pact 3 % + (order − 50) / 1000, −0.10, +0.35)
ev   = max(−0.50, Σ temporary event buffs)                           // plague, festival boom … (ENGAGEMENT §6)
gold = roundToInt(b × (1 + pct) × (1 + ev))
```

The ENGAGEMENT order term lives here (it replaced the separate "income × 民心" multiplier, ENGAGEMENT §3.1). 铁官 (ENGAGEMENT §8.2) is **+10 % troops per gold** inside the recruit cap (§4.4), no longer "−10 % gold cost".

### 4.3 Harvest `Econ.harvest(c, roll)`

```
h0  = land × FoodPerLand × roll                                      // roll = the v3 rangeFloat(0.85, 1.15), kept as rollRaw
pct = clamp(titles + items + projects + events + order term, −0.20, +0.35)
h   = roundToInt(h0 × (1 + pct))
```

DOMESTIC-FX tiers the harvest on **`rollRaw`**, never on the result (§4.7).

### 4.4 Troops and training

```
troopsPerGold(c, g) = TroopsPerGold × (1 + min(0.40, max(gear of g, gear of admin) + admin title recruit + projects (铁官 +0.10)))
trainGain(g, roll, c) = TrainBase + ⌊eff(g, war) / 10⌋ + roll + min(12, gear + title + project (讲武堂 +3))
```

ENGAGEMENT's 讲武堂 drill (+40 training on new recruits) and the order cost of recruiting (−⌈n / 3000⌉ order) stay in `Commands.recruit`.

### 4.5 Probabilities (one clamp, at the end)

| Function | Formula (v3 on effective attributes, every v4 term added, then one clamp) |
|---|---|
| `searchP(c, g)` | `min(0.90, 0.22 + eff intel / 260 + virtue / 420 + gear + title + project (学宫 +0.06))` |
| `hireP(t, r, f, promise)` | `clamp(0.32 + virtue / 220 + eff intel(r) / 450 − (t.war + t.intel + t.pol) / 900 − (t in a faction ? t.loyalty / 160 : 0) + gear + title + promise, 0.05, 0.95)` |
| `allyP(f, t, gift, envoy)` | `clamp(0.25 + virtue / 300 + gift / 1200 + (fame f − fame t) / 300 + (cities > 2× ? 0.15 : 0) + max(ruler's gear dip, envoy's gear dip) + envoy's title dip + relation / 400, 0.05, 0.90)` |
| `discordP(a, t)` | `clamp(0.4 + (eff intel a − eff intel t) / 100 + gear + title, 0.1, 0.90) × (1 − t's discord resistance)` (TITLES) |
| `persuadeP(a, t, f)` | `clamp(v3 product + gear + title, 0, 0.90) × (1 − 0.1 × t's office tier)` (TITLES persuade resistance); rulers 0 |

### 4.6 The city administrator (one rule for ITEMS and TITLES)

`admin(c)` = the governor; when the governor is the ruler, the best active 文官 (pol family) office-holder stationed in the city (tier, then 政治, then id); otherwise the ruler. No governor → no administrator (city bonuses 0). City-scope item effects (`itemCity`, `itemLoyAura`) and title city effects read the administrator; national title effects (`titleNat`) take the **max** over the faction's holders, never the sum.

### 4.7 `Commands.lastResult.mods` and tiers

Every command writes `Commands.lastResult` (DOMESTIC-FX §3.2) with the Econ record:

```js
{ cmd, city, gen, month, before, after, roll, rollRaw, g0, pct, gain, mods: { gear, title, project, order, pact, events }, tier }
```

- DOMESTIC-FX shows each non-zero `mods` entry as a chip (「装备 +12 %」「官职 +10 %」「营建 +10 %」); the forecast uses the same function.
- **Tiers use the pre-multiplier values**: 开发 good = `roll = 4` or `before + g0` crosses a multiple of 100; great = the *visible* building stage rises or the stat reaches 999. 秋收 lean = `rollRaw < 0.90`, good = `rollRaw ≥ 1.04`, great = `rollRaw ≥ 1.10` with land ≥ 600. Measured all-on (scripted player, §9.3): 开发 great 10.1–10.9 %, good 24.5–25.3 %; 秋收 lean 15–17 %, good 25–30 %, great 7–10 %. Modifiers never inflate tiers.

### 4.8 Reference vectors

`node design-v4/integration/v4-all-proto.js refs classic|world` → `results/econ-refs-<scen>.json` (develop 249, cityGold 35, harvest 35, recruit 59, search / hire / discord / persuade 83, ally 54, fame 18, stacked battle 24). `tests/econ.js --refs` replays them against `js/econ.js`; the Unity harness (`Tests~/econ`) replays the same file against `Econ.cs` / `BattleMath.cs` and must match to 1e-9 (floats) and exactly (integers).

---

## 5. `SG.AIBudget` — one reserve, one purse, one order

The four specs each let the AI spend "spare" gold (ITEMS shops, TITLES 礼金, ENGAGEMENT projects / festivals / tribute, TOURNAMENT nothing). Separately each was safe; together they spent the same spare gold four times and pushed cities below 0. Now:

### 5.1 Rules

```
reserve(c)  = { gold: 150 + 8 × officers in c, food: 2 × ⌊troops in c / TroopsPerFood⌋ }   // R(c): never spent by anything discretionary
floor(c)    = 2 × SalaryPerGeneral × officers                                            // the floor for tribute and pact payments
accrue(f, income)   purse[f] += 0.22 × gross gold income of f this month; purse ≤ 12 months of that
canSpend(c, amount, kind):
  tribute, pact     → c.gold − amount ≥ floor(c)
  everything else   → c.gold − amount ≥ reserve(c).gold
  discretionary     → and purse[c.owner] ≥ amount
spend(c, amount, kind)  c.gold −= amount; discretionary kinds also purse −= amount; stats
payer(f, amount, kind)  the own city with the most gold above its reserve that can pay
```

- **Kinds and order** (fixed, one pass per faction per month; the discretionary kinds run in phase 7 of §6.3): `recruit` → `develop` (the v3 AI's own commands, unchanged, before the phase) → `tribute` (ENGAGEMENT §10.5, floor rule) → `event` (AI answers to event cards) → **discretionary**: `title` (TITLES §8 礼金) → `gear` (ITEMS §13.3) → `project` (ENGAGEMENT §8.6) → `festival` (ENGAGEMENT §5.3).
- Discretionary kinds: `title`, `gear`, `project`, `festival`. The purse is the only budget; each spec's own "spare gold" heuristics now decide *what* to buy within `canSpend`.
- Food: no v4 AI purchase spends food; transfers and festivals check `reserve(c).food`.
- The player is never limited by `AIBudget`; the player's reserve is shown as advice only (UI-V4 §5).

### 5.2 Asserts (`tests/econ.js` §AIBudget, and the all-AI runs)

1. No city ends a discretionary spend below 0 gold (`stats.below0 == 0`). Measured: 0 in every run.
2. Discretionary spending ≤ 0.22 × gross income + the starting purse (measured classic 19.5–19.7 %, world 18.4 % of gross).
3. Gear spending in ITEMS' **8–15 %** band of gross income (classic 11.2–11.4 %, world 8.5 %; with a 20 % purse the world was 7.8 %, which is why the share is 22 %); title gifts 1–4 % (2.0 / 1.8 %); projects 3–9 % (5.8 / 7.0 %); festivals ≤ 2 % (0.6 / 1.1 %).
4. Defections with everything on ≤ off + 2 per seed (measured: classic 1.54 vs 2.04 and 3.27 vs 6.31, world 2.17 vs 3.50).
5. `refused` counts are reported, never asserted (a refusal is the budget working).
6. The order is fixed: a test faction with a purse for exactly one item buys the title gift, not the gear.

---

## 6. Save, migration, the month pipeline and shared state

### 6.1 Save version 4

`SAVE_KEY = 'sanguozhi2_save_v4'`, `SAVE_VERSION = 4`. One bump for all of v4 (integration owns the number). `GameState.load` accepts versions 2, 3 and 4; versions 2 and 3 run `SG.MigrateV4.run(G)` after the v3 migration.

| Owner | Fields (as each spec defines them, with the consolidated names) |
|---|---|
| ITEMS §15 | `G.items = { next, inst, hiddenFound, singletonsGiven, tourneyAwarded, cooldown, seen }` (no `titles` field), `gen.gear`, `faction.armoury`, `city.shop = { stock, merchant }` (was `caravan`, §7.5) |
| TITLES §10.4 | `gen.office`, `gen.officeBest`, `gen.officeLog`, `faction.rank`, `G.officeSeq` |
| ENGAGEMENT §20 | `city.order`, `city.eng`, `faction.prestige`, `faction.eng`, `gen.dexp`, `gen.away` (kinds `caravan`, `exped`, `hostage`, `leave` — **not** `injured`), `G.engage` |
| TOURNAMENT §10 | `G.tourney = { held, count, next, hosts, wins, stats, history, predictHits, ... }`; **`G.tourney.wins[name]`** is the one count of titles (ITEMS' `G.items.titles` and TITLES Q8's clash are gone) |
| COMBAT-V4 §16 | `G.combos = { known, mastery }`, `gen.crits` counters |
| DOMESTIC-FX / UI / AUDIO | nothing in the save (settings are per device, §10.1) |
| shared | **`gen.injury = { until, war, away }`** (§6.4) |

### 6.2 Migration order

`SG.MigrateV4.run(G)` calls, in this order, each module's `migrate(G)` (a module that is switched off is skipped):

1. **ITEMS** — starting kits (§8.10), signature, ruler-uniques, armouries, city treasures, shop stock for the current month. First because TITLES' AI valuation and ENGAGEMENT's prestige read gear.
2. **TITLES** — `rank` computed, no offices (the AI grants at the next month start).
3. **ENGAGEMENT** — order from the start formula, prestige 0, starting relations and bonds, `dexp = 0`.
4. **TOURNAMENT** — `G.tourney = { held: month > 1 ? year : year − 1, count: 0, wins: {}, stats: {}, history: [] }`; a migrated save's first tournament is its 第一届 (§6.5).
5. **COMBAT / DOMESTIC** — combo knowledge empty; nothing for DOMESTIC-FX.

`GameState.migratedFrom` = the old version; the UI shows one card listing the new systems.

### 6.3 The month pipeline (`StrategyAI.endMonth`, in this order)

| # | Phase | Owner |
|---|---|---|
| 0 | bond shields snapshot; gross income per faction (for the purse) | integration |
| 1 | v3 income (`Econ.cityGold`), harvest in month 7 (`Econ.harvest`), salaries, food, the v3 gold < 0 rule, population | v3 + Econ |
| 2 | ITEMS month end: restock (ITEMS §6.4, seeded), item effects with a monthly tick (`expMonthly`, `talent`, omens) | ITEMS |
| 3 | ENGAGEMENT month end: order drift, projects tick, caravans / expeditions, missions, events (the AI's answers: kind `event`), tribute and pact payments (`AIBudget` floor rule), the player's festival card, 文会 in month 9 | ENGAGEMENT |
| 4 | injuries and `away` expiry; defections (v3 rule on `Mods.effLoy`: the larger of the TITLES and ENGAGEMENT shields) | v3 + Mods |
| 5 | `loyAura` (the larger of title and item aura, ≤ 95) | Mods |
| 6 | `Titles.monthStart()` — rank, suspensions, validity | TITLES |
| 7 | **discretionary phase**, per AI faction: `accrue` → `Titles.aiManage` (`title`) → `Items.aiEquip` + `Items.aiShop` (`gear`) → `Engage.aiProjects` (`project`) → festival decisions (`festival`) | AIBudget |
| 8 | month 11 → 12: `Tourney.announce`; month 1 of a new year: the tournament (AI-only resolution if the player is not involved, else the scheduled card, §6.5) | TOURNAMENT |
| 9 | `syncFV` (fame / virtue events), `rank` refresh after conquests, news assembled (`harvests`, `stageUps`) | ENGAGEMENT / TITLES |

`StrategyAI.runAI` keeps its v3 role (the AI factions' commands and wars); TITLES no longer calls `aiManage` there (TITLES §8 was edited). This is the lead reviewer's order (income → ITEMS → ENGAGEMENT → defections → `loyAura` → `Titles.monthStart` → tournament → news) with the discretionary phase added before the tournament. The all-on prototype ran the v3 defection check inside v3's `endMonth`, i.e. before ENGAGEMENT's month end (one month earlier for event loyalty changes); `tests/v4-all.js` re-measures the gates on the order above.

### 6.4 Unified general state

- **`gen.injury = { until, war, away }`** — one record for every injury: tournament KO (TOURNAMENT §6.3: `war 5`, 2 months, `away: false`), events and expeditions (ENGAGEMENT: `away: true` when the general cannot act, e.g. 坠马 3 months). `eff()` and `duelWar()` subtract `war` while `G.monthIndex < until`; `away: true` also removes him from commands and tournament entry. ENGAGEMENT's `gen.away.kind = 'injured'` is retired.
- `gen.away` keeps ENGAGEMENT's other kinds (caravan, expedition, hostage, leave).
- **`G.tourney.wins[name]`** counts tournament titles (used by ITEMS' relic rule, the seed score, the 英雄榜, the 履历 tab).
- `gen.dexp` (ENGAGEMENT) is the domestic exp; `vetLevel` = `levelFor(exp − 0.5 × dexp)` drives 老练 and the army crit level term.

### 6.5 The calendar and scheduled cards

- **The tournament is outside ENGAGEMENT's R1 budget.** Month 1 may show the 武林大会 card *and* one other scheduled card. Its card and the 正旦 year review are an annual ceremony, not an interruption.
- **Month-1 AI demands and 归附 offers** are generated for month 2 (ENGAGEMENT §5.1, §10.5): `endMonth` of December never creates a tribute demand or an 归附 offer due in January. An 归附 offer **already pending** from an earlier month still comes first, and the tournament follows it in the same month. The test (`tests/engage.js` §6) runs 20 classic years and asserts that in every month 1 with `Tourney.due` the tournament card is offered, that no January shows more than the tournament + one card, and that no demand or 归附 is created with `due.month === 1`.
- Tournament numbering: `n = G.tourney.count + 1` (tournaments actually held, so a skipped year never leaves a gap). In a new game `FIRST_YEAR = StartYear + 1 = 191` in both scenarios, so **191 = 第一届, 193 = 第三届**. Texts use `第{n}届` everywhere (TOURNAMENT §9, UI-V4 §12); the mockups' 「第四届 … 193年」 was wrong.
- Scheduled-card priorities (ENGAGEMENT §5.1): 归附 > tribute demand > 武林大会 (month 1, outside R1) > 文会 (month 9) > culture festival > AI offers. The 胡商 merchant comes on the **ENGAGEMENT event card 「行商过境」** (§7.5), not a scheduled card.

### 6.6 Month-start time budget

| Budget | Target | Measured (Node, prototype) |
|---|---|---|
| **non-interactive beats at month start** (year review, achievement seals, stage-up celebrations, the harvest montage, the month transition) | **≤ 8 s in total**; whatever does not fit becomes an agenda slip or a news line (UI-V4 §5.7) | (browser measurement in P1) |
| compute of the transition (AI turn + v4 phases), reference phone | hidden under the transition animation, JS slices ≤ 8 ms | (browser measurement in P1) |
| per AI faction, v4 phases | ≤ 60 ms | discretionary phase ≈ 1–3 ms |
| January tournament, AI-only resolution | exempt from the 60 ms cap; ≤ 1.1 s world (sliced over frames, TOURNAMENT §14) | prototype fast resolution 5.7 ms classic / 9.0 ms world; the duel-engine resolution is budgeted at 1.1 s |

---

## 7. Cross-system item rules

### 7.1 Gifts (赏赐 with an item)

The loyalty bonus of gifting an item applies **once per item instance** (`inst.gifted`, `inst.giftLoy`): giving the same 青釭剑 back and forth gives nothing after the first time. Taking an item back from a general who received it as a gift costs the same loyalty the gift gave (`giftLoy`, ITEMS §6.6). Test: `tests/items.js` §2 gift-cycling (10 cycles → loyalty change = first gift − take-back, never more).

### 7.2 Ruler-unique weapons and prize pools

- **Prize pools** (tournament prizes, 文会 rewards, tier-3 mission rewards, expedition finds, event grants): `rare` = the shop rares of the scenario **not owned by any faction** (a prize copy never empties a shop; a shop keeps its own stock copy); `uncommon` / `common` = shop items of the winner's culture. Ruler-unique, signature, hidden and city-treasure items are **never** in a pool.
- The **classic tournament never awards ruler-uniques** (the filter is `availability.kind === 'ruler'` / `bound`, not the data's `unique` flag, which means "singleton").
- **赫拉克勒斯之棍** (the 5-title relic) is **world-only**; the classic scenario has three legendaries (赤兔, 传国玉玺, 金刚杵). `build.js check` asserts it per scenario.

### 7.3 Classic catalogue counts

The classic scenario has **≥ 255 weapons and ≥ 255 gear pieces available outside the 15 % random roll** (shops, kits, routes): measured 256 / 273 with the rebuilt catalogue (602 items: 310 weapons, 292 gear; 273 common, 133 uncommon, 172 rare, 20 epic, 4 legendary). The 30 added Han weapons (铜铍, 长铩, 酋矛, 夷矛, 棘矜 … 环首短刀) and 5 regional ones come with sources (ITEMS §12.3, §20). World-only shop rares are sold in classic as 舶来 curios in the trade cities (会稽 / 建业 for Africa, 洛阳 for Celt / German / Moor goods) with `foreign: true`.

### 7.4 Rare art

Every rare item has a **signature part** (≤ 200 triangles, from a parts library of 79 parts, socketed by slot), a **procFx** (one pooled effect, ≤ 0.6 s, 1 draw call) and an **sfx** (`{ id, family, pitch }`); every rare has a 演示 (demo) in the character menu. Silhouette test: each rare's silhouette differs from its base type by ≥ 10 % of pixels (ITEMS §5.2 / §5.4, `tests/items.html`).

### 7.5 The 胡商 merchant

One trigger only: the ENGAGEMENT event card 「行商过境」 (ENGAGEMENT §6.3, §9.3), drawn from ENGAGEMENT's own seeded stream. ITEMS no longer rolls its own caravan event; the card opens a temporary stock in the city's shop, stored as **`city.shop.merchant = { until, stock }`** (renamed from `caravan`, which is ENGAGEMENT's trade caravan). AI factions get the stock only when their card's ★ option is 「看看货物」, and buy from it through `SG.AIBudget` (kind `gear`).

### 7.6 Special abilities with no-break or relation effects

`径路刀`'s former `noBreak` (alliances cannot break) is replaced by: alliance length **+12 months** and the relation floor **+40** with that partner while equipped by the ruler or envoy. `noBreak` is forbidden by `build.js check`. 卍纹铜剑 is renamed in data to **吉祥纹铜剑** pending the player's choice (ITEMS Q7).

---

## 8. The duel and the balance of stacks

### 8.1 Stacks (measured, `stacks classic`)

| Case | Uncapped | Capped |
|---|---|---|
| 关羽 L10, 金刚杵 + best defensive gear + 赤兔, 大将军, sworn brother in the army | atk ×1.581, def ×1.471 (the cap 1.6 does not bind) | same |
| exchange vs an unequipped L1 general (老练² × atk × def) | ×4.30 (老练 alone ×1.85) | ×4.30 |
| crit | 23.8 % ×1.50 | same |
| 开发·产业 by 荀彧 (政治 98) with the best domestic kit, 丞相, 铁官 | 24 → 38 (×1.58) | same (cap 0.80 not reached) |
| city income Σ (title 0.10, gear 0.25, projects 0.11, order 0.05) | +51 % | **+35 %** |
| fame bonus prestige 2 000 + 玉玺 | +28 (AP 7) | **+10** (AP 6 = v3) |
| strongest realistic stack on the map at year 10 (all-AI) | atk ×1.43, def ×1.23 (max ×1.52 / ×1.32; world ×1.48 / ×1.43) | |

The battle cap protects against data mistakes; realistic stacks stay below it. The ×4.30 exchange ratio of a hand-built perfect general is the open question Q1 (§15).

### 8.2 Duel item effects: a whitelist

The duel (COMBAT-V4) reads only these item fields (`DUEL_ITEM_EFFECTS`, COMBAT-V4 §3): `duelWar` (ITEMS §2.4), `dHp`, `dDmg`, `crit` (duel crit), `dParry` (window seconds), `dGuard` (guard chip pp), and the ability effects `dArmor { on, dmgK }`, `dProc { on, kind }`, `dCombo { bonus }`, `dRegen`, `dLifesteal`, `dCounter { k }`, `dRage`, `dLowHp`, `dProjectile { hp }`. Removed: `dashUnblockable`, `unblockable`, `unparryable`, `dPerfectGuard` (now `dParry { guardWindow: 0.02 }`), `noBreak`. `build.js check` rejects any other duel key.

### 8.3 The duel budget

```
duelWar(gen) = min(110, war + min(3, ⌊gear 武力 / 3⌋)) − injury
per fighter:  dmg % + 0.4 × (gear HP + ability HP + 0.5 × guard pp) ≤ 6        // 5 HP ≈ 2 % damage
              dmg % = gear dDmg + TITLES dmgDuel + ENGAGEMENT 宿敌 rival bonus (+4 %), the sum ≤ 6 % (COMBAT's old duel-damage cap)
              gear HP ≤ 10, dmg ≤ 3 %, ability HP ≤ 4, dParry ≤ 0.01 s, duel crit counted ≤ 3 pp
the duel trims HP first, then damage, when the budget is exceeded (deterministic, data order)
TITLES' dmgDuel (≤ 2 %) and critDuel (≤ 3 pp) are added to the gear terms before the budget is applied
```

Measured with the v3 duel Sim (AI vs AI, 300 duels per cell, sides alternated; `integration/duel-gear-estimate.js`):

| Best-in-slot of rarity vs ungeared, equal 武力 60 / 80 / 95 | Before (ITEMS as written) | Final |
|---|---|---|
| common | 58 / 55 / 58 % | 58 / 55 / 58 % |
| uncommon | 64 / 60 / 62 % | 70 / 66 / 68 % |
| rare | 93 / 87 / 88 % | 60 / 63 / 63 % |
| epic | 97 / 98 / 99 % | 76 / 67 / 67 % |
| legendary | 99 / 100 / 100 % | **69 / 72 / 73 %** |
| 武力 65 + legendary BiS vs 80 ungeared | 94 % | **23 %** |

Reading: the budget is reached by a full uncommon set; beyond that, rarity buys `duelWar` (+1 → +3), abilities' feel and looks, not more HP. The noise of a cell is ±2.9 pp (300 duels). The looser alternative (budget 8, gear HP 12, ability HP 6) gives legendary 75 / 77 / 75 % and is kept in the script for Q3 (`results/duel-gear-estimate-final_loose.txt`).

### 8.4 Weapon parity and WIN_SCALE

Weapon-type parity in the v3 Sim at equal 武力 80, each type vs the sword: pole weapons 39–47 %, 刀 58 %, 短戟 55 %, 双刀 57 %, gladius **65 %** — nine types outside 45–55 %. COMBAT-V4 P1 retunes the per-type reach / power (COMBAT §15, duel-bots section 15) to bring every type into 45–55 % **before** the tournament's `WIN_SCALE` (the 6 of the win curve) is measured again; after COMBAT P1, `WIN_SCALE` is **frozen** and TOURNAMENT's balance numbers are re-measured once with it.

### 8.5 Tournament duels and the speed setting

Settings row 48 (单挑速度 100 / 85 / 70 %) slows the whole duel, AI included; because the AI's reactions are in simulation time, the slowed human gets more real reaction time. So in **tournament matches the player fights (亲自)** the row does not apply (they run at 100 %; the entry screen says so). Watched AI matches may be slowed (no advantage), simulated ones are untimed. Battle duels keep the row (no prize at stake beyond the battle, and accessibility comes first there).

---

## 9. Measured results of the all-on prototype

### 9.1 Campaign gates

| Gate | Target | Classic s0 (off / all / nocap) | Classic s1000 (off / all) | Pass |
|---|---|---|---|---|
| factions alive, year 10 | ≥ off − 0.8 | 4.94 / **4.77** / 5.23 | 4.29 / **4.48** | yes |
| first faction > 25 % of cities (median) | ≥ off − 0.5 y | 2.33 / 2.08 / 2.25 y | 2.08 / 2.00 y | yes |
| defections per seed | ≤ off + 2 (the reviewer's assert; the gate is + 4) | 2.04 / 1.54 / 1.75 | 6.31 / 3.27 | yes |
| cascades (≥ 10 defections) | — | 3 / 2 / 2 of 48 | 8 / 4 of 48 | |
| v3 "gold < 0" penalties per seed | — | 1.7 / 2.3 / 3.6 | 5.4 / 4.3 | (v3 rule; AIBudget never causes one) |
| level avg y10 / y25 | — | 2.97 / 4.03; 3.26 / 5.75 | | |
| battle AP y25 | — | 4.76 / 5.16 / 5.94 | | the fame cap holds AP |
| fame bonus avg y25 | ≤ 10 | — / 9.63 / 31.95 | | the cap binds by year 15 |

World (24 seeds × 15 y, off / all): alive y15 **44.29 / 43.42** (≥ off − 2: yes); largest faction 24.2 / 25.0 %; defections 3.50 / 2.17; levels y15 2.74 / 5.42.

Final catalogue (602 items), purse share 0.22; the off variants and the share-0.20 all variants are in `results/*-off_all-share020.txt` (classic 4.65 / 5.17 alive y10, world 44.13 y15: the gates pass at 0.20 too). The `nocap` column is from the first run (catalogue before the consolidation's 35 added commons).

### 9.2 Tactical (sim.js (7g) style)

Year-8 all-on armies, 480 battles per mode: attacker win 45.6 % (v4 battle effects off) → 45.4 % (on) → 45.4 % (on without crits); shift **−0.2 pp** (≤ 4); troops lost per battle ×0.999 (≤ 1.35); crit rate 7.0 % of attacks (earlier army sets: −0.4 / −1.5 pp, crit 7.9 / 8.2 %).

### 9.3 The scripted peaceful player

| | Classic s0 (v3 / all) | Classic s500 | World |
|---|---|---|---|
| survival y20 (world y15) | 83 % / 89 % | 94 % / 100 % | 92 % / 96 % |
| cities (survivors) y20 | 2.3 / 2.2 | 2.2 / 2.3 | 1.9 / 2.6 |
| income y20 | 347 / 317 | 328 / 334 | 300 / 372 |
| 王道 (prestige 1 500, all held 6 months) | 14 %, median 14.2 y | 19 %, 12.4 y | 8 %, 12.3 y (2 of 24 runs; 17 % at share 0.20: noise) |
| 开发 tiers (great / good) | 10.1 / 24.8 % | 10.5 / 25.3 % | 10.9 / 24.5 % |
| 秋收 tiers on rollRaw (lean / good / great) | 17.1 / 28.4 / 8.8 % | 15.3 / 30.2 / 7.3 % | 16.0 / 25.2 / 10.1 % |

秋收 good: the old rule (`rollRaw ≥ 1.08 or land ≥ 700`) gave 41–46 % because v4 lands grow past 700; the consolidated rule (`rollRaw ≥ 1.04`, no land term) gives **25–30 %** (analytically 36.7 % of rolls ≥ 1.04 minus the great share).

### 9.4 Economy detail (classic s0, all)

Income modifier median +1.1 %, the +35 % cap reached in 0.00 % of city-months; develop modifier median 0, max 0.38; 搜索 p max 0.885; items bought (48 × 25 y): 11 681 common, 10 035 uncommon, 2 164 rare, 0 epic; sold 17 817; grants 文会 3 600, tournament 3 426, events 577; hidden found 285; titles granted 14 959; tournaments 1 200 (24.9 entrants, 2 768 injuries, 金刚杵 awarded in 48 / 48 runs, 5.7 ms each); strongest realistic stack at year 10 atk ×1.43, def ×1.23 (max ×1.52 / ×1.32).

---

## 10. Registries

### 10.1 Settings rows (UI-V4 §7, `SG.Settings.define`)

Rows 1–50 as UI-V4 §7, plus:

| # | Tab | Row | Options | Default | Owner · key |
|---|---|---|---|---|---|
| 51 | 游戏 | 内政委任 | 小事委任 · 全部询问 | 小事委任 | ENGAGEMENT · `sanguozhi2_engage_v4.delegate` (R2) |
| 52 | 游戏 | 节庆照此办理 | button 「清除记忆」 (lists the remembered festival choice) | — | ENGAGEMENT · `faction.eng.festivalAuto` (in the save, so the button edits the save) |
| 53 | 游戏 | 武林大会 | 每次询问 · 自动派遣（推荐五人） · 只看结果 | 每次询问 | TOURNAMENT · `sanguozhi2_tourney_v4.entry` |

Row 48 (单挑速度) is read by tournament duels too (§8.5).

### 10.2 Voice cues

All cues proposed by ITEMS, TITLES, ENGAGEMENT, TOURNAMENT, COMBAT and DOMESTIC-FX are merged into `design-v4/audio/voice-lines.js` (AUDIO-V4 §B2), each with a fallback cue so a missing clip never silences a moment. The 26 "start / great" cues players hear most are translated into the 14 A-grade linguas (machine-assisted drafts, ASR-gated). The catalogue grows from 82 to **123 cues**; clips **851 → 1 381** (≈ 34 min of speech, **4.38 MB** at Opus 16 kbps, ≈ 5.9 MB as base64 packs), still far inside AUDIO-V4's 16 MB ceiling. `SG.Voice.profile('herald' | 'merchant')` gives the tournament herald and the 胡商 a non-general voice. `node design-v4/audio/voice-lines.js` checks every cue id used by a spec exists (102 references in the 10 docs), every A lingua has the 26 cues, every profile has a voice in every A lingua: ALL OK.

### 10.3 Music stings

`SG.MusicStings.ids` = **34 ids** (DOMESTIC-FX §8: the 21 of v4.0 plus `build`, `caravan`, `explore`, `gift`, `marriage`, `tribute`, `hostage`, `truce`, `tradepact`, `submit`, and the moments `mission`, `festival`, `wenhui`); 1 284 keys over 4 tiers × 12 families, validated by `node design-v4/domestic/stings.js`. The festival sting is played per culture family (`festival@family`).

### 10.4 UI icons

`SG.UIIcons` holds ≈ 151 icons: UI-V4's ≈ 140 plus DOMESTIC-FX §6's 11 new command icons (营建, 通商, 探索, 赠礼, 联姻, 贡赋, 质子, 停战, 互市, 归附, 国策).

### 10.5 Figure clips

`js/figure.js` gains COMBAT-V4 §13.1's 25 clips (with fallback mapping) plus **one bow and one kneel** shared by TITLES (ceremony), TOURNAMENT (salutes) and ENGAGEMENT (归附, 联姻). TOURNAMENT's fist-in-palm salute uses the bow clip with an arm override.

### 10.6 Sound effects

`SG.Audio` sfx ids are registered by owner prefix: `ui-*` (UI-V4 §14), `item-*` (ITEMS rare sfx, §7.4), `duel-*` (COMBAT), `fx-*` (DOMESTIC-FX), `crowd-*` (TOURNAMENT). A duplicate id throws at registration.

### 10.7 The day clock

One clock for the map light (UI-V4 §3.8), the city view (CITY-VIEW §6.6) and the shell's `--night`: `SG.Clock.hour()` = **07:00 at the month's start → 20:00 when the last token is used** (`7 + 13 × tokensUsed / tokensTotal`), 20:30 (night) during the AI phase and the news, dawn at the month transition. UI-V4's former 08:00–18:00 proposal is replaced. Events that stage their own day (the tournament: rounds 10:00–18:30, the final and ceremony at 20:00, TOURNAMENT §8.9) set the clock while they run and restore it afterwards.

---

## 11. `index.html` load order

```
vendor/three.min.js, core.js, world-geo.js, data.js, world-data.js,
items-data.js, titles-data.js, engage-data.js, tourney-data.js, combo-data.js,            // generated data
econ.js,                                                                                  // SG.Mods / SG.Econ / SG.AIBudget (no providers yet)
model.js, items.js, titles.js, engage.js, tourney.js, combos.js, migrate-v4.js,          // rules; each registers its providers
strategy-ai.js, battle-math.js, battle-model.js,
audio.js, music.js, music-inst.js, music-han.js, music-world.js, music-suites.js, music-suites-gen.js, music-stings.js, music-tourney.js,
voice-data.js, voice.js, voice-cry.js,
settings.js, ui-icons.js, ui.js, ui-art.js, shell.js,
portrait-data.js, portrait.js, lowpoly.js, lp-props.js, lp-pilot.js, figure.js, art.js, culture-art.js, item-art.js,
camera.js, map-view.js, city-kit.js, city-layout.js, city-life.js, city-view.js,
battle-terrain.js, battle-view.js, clash.js, specials-data.js, specials.js, tactics-data.js, duel-game.js,
stadium-kit.js, stadium.js,
domestic-tiers.js, cmd-icons.js, domestic-fx.js,
battle-controller.js, menu-screen.js, settings-screen.js, generals-screen.js, character-screen.js, shop-screen.js, engage-screen.js,
tourney-screen.js, tourney-flow.js, strategy-screen.js, game.js
```

Rules: data before rules; `econ.js` before every module that registers providers; rules never reference view files (Node tests load only the first two lines plus `strategy-ai.js`, `battle-math.js`, `battle-model.js`); `tests/load-order.js` parses `index.html` and asserts each module's declared dependencies are loaded before it.

---

## 12. City level as rules data

Rules read city stages (ENGAGEMENT: the wonder needs city level ≥ 郡城, prestige for each new level; ITEMS: the shop tier is the market stage), so the thresholds cannot live in a view module. **`SG.Balance.CityLevel`** (in `js/data.js`, exported to Unity with the other balance tables) holds:

```js
SG.Balance.CityLevel = {
  CORE: ['farms', 'workshop', 'houses', 'walls', 'market'],     // city level = round-half-up mean of the five core stages, index 0–4
  NAMES: ['邑', '县城', '郡城', '大郡', '都会'],                   // ENGAGEMENT's level 1–5 = index + 1
  T: { farms: [300, 450, 600, 800], workshop: [300, 450, 600, 800], houses: [300, 450, 600, 800], walls: [45, 55, 65, 80], market: [2000, 3000, 4000, 5500] },
  DOWN: 0.95,                                                   // hysteresis
  SHOP: { sum: [520, 800, 1100] },                              // shop tier = max(1 + count(产业 + 町 ≥ …), hub tier)
  HUBS: { 4: [...], 3: [...], 2: [...] },                       // ITEMS §6.2 trade hubs (the hub boost: a hub's shop tier — and with it its market stage — never falls below its hub tier; a tier ≥ 3 hub's harbour is one stage higher)
};
```

`js/city-layout.js` (CITY-VIEW), `js/domestic-tiers.js` (DOMESTIC-FX), `js/items.js` (shop tier) and `js/engage.js` (levels, the wonder) all read this table; `domestic/city-stages.js` is its reference. CITY-VIEW keeps the view-only buildings (palace, barracks, stables, academy, temple, tavern, treasury, harbour) in its own module because no rule reads them.

City-view triangles include ENGAGEMENT's projects (finding 14): **≤ 900 triangles per project, the wonder ≤ 2 000**, the medium LOD (phone tier) halves them; `triEstimate(st, ctx, tier)` takes `ctx.projects`. A capital with every stage at S5 and all 13 projects draws 215 640 triangles on desktop (budget 250 000) and 128 960 on phones (150 000): `node design-v4/domestic/city-stages.js` → ALL OK.

---

## 13. Test matrix and CI budget

| Suite | Quick (every push) | Nightly | Owner |
|---|---|---|---|
| `tests/econ.js` (caps, pipeline, AIBudget asserts, `--refs` vectors) | all | all | integration |
| `tests/v4-all.js` (the all-on campaign gates of §9) | `--quick`: classic 12 seeds × 6 y, world 4 × 6 y; gates relaxed by the noise of that size | classic 2 × 48 × 25 y, world 24 × 15 y, player 6 × 20 / 4 × 15, tactical 8 | integration |
| `tests/items.js` (catalogue, rules incl. gift cycling, economy) | §1–§2 | §3 all-AI | ITEMS |
| `tests/titles.js` | all | + 100 world years | TITLES |
| `tests/engage.js` (R1 incl. month 1 + 2, events, projects) | `--quick` | full | ENGAGEMENT |
| `tests/tourney.js` | `--quick` (3 tournaments) | 200 tournaments | TOURNAMENT |
| `tests/duel-bots.js` (incl. section 15 items and parity) | `--quick` (60 duels per cell) | 300 per cell | COMBAT |
| `tests/sim.js` | existing quick | (7g) all-on | v3 + COMBAT |
| `tests/domestic-tiers.js`, `tests/music-stings.js` (34 stings, 1 284 keys) | all | all | DOMESTIC-FX |
| `tests/suites.js` (≥ 25 % motif-derived bars), `tests/voice-lines.js` | all | all | AUDIO |
| `tests/city-view.html`, `tests/ui-v4.html`, `tests/ui-layout.html` (844 × 390 with 47 px insets), `tests/items.html`, `tests/stadium.html` (headless, budgets) | smoke | full shots | CITY-VIEW, UI, ITEMS, TOURNAMENT |
| `tools/lp-recipes.js check` (every recipe × family × stage × LOD: budget, tokens, hash) | changed recipes | all | art rollout |
| `tests/load-order.js`, `tests/save-v4.js` (v2 / v3 / v4 saves round-trip, migration order) | all | all | integration |

Budget: **quick ≤ 6 min**, **nightly ≤ 45 min** on the CI machine. Every suite with a long mode has `--quick`. The January tournament's AI resolution is exempt from the 60 ms per-faction month cap and has its own (≤ 1.1 s world).

---

## 14. Unity port (after the browser build is stable)

| Unity | From |
|---|---|
| `Model/Econ.cs` | `js/econ.js` (`Mods`, `Econ`, `AIBudget`), same operation order, the cap table from generated rules |
| `Battle/BattleMath.cs` | `js/battle-math.js` |
| `Model/MigrateV4.cs` | `js/migrate-v4.js` (the same order) |
| `Tests~/econ` | replays `integration/results/econ-refs-*.json`: floats to 1e-9, integers exact |

---

## 15. Open questions

1. **Product cap for battle stacks.** The per-side caps never bind for realistic stacks (year-10 maximum ×1.52 / ×1.39), but a hand-built perfect general reaches an exchange ratio of ×4.30 against an unequipped level-1 general (老练 alone ×1.85). Add a product cap `atk × def ≤ 2.0`? (Proposed: no; it only matters for one or two generals in a game.)
2. ~~AI gear spending in the world scenario~~ — resolved by a 22 % purse (world 8.5 %, classic 11.2–11.4 %, all gates pass). Open instead: should the purse **grow with the AI's difficulty** (e.g. 18 / 22 / 26 % for 易 / 中 / 难)?
3. **Duel budget**: 6 (final) vs 8 (looser, rarity matters a little more in duels).
4. **吉祥纹铜剑** (renamed from 卍纹铜剑) — keep the safe name or restore the original with a note?
5. **Classic 异域奇珍**: world-only shop rares sold in classic trade cities as curios — keep, or limit to 3 per game?
6. **WIN_SCALE freeze** after COMBAT P1 — accept that the tournament's measured champion shares will change once?
7. **Translations** of the ~26 start / great cues into the 14 A linguas are machine-assisted with the AUDIO ASR gate — acceptable without native review for v4.0?
