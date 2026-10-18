# design-v4/integration — the consolidation's tooling (design only, not game code)

The reference implementation and measurements behind `../INTEGRATION.md`. Nothing here is loaded by the game; the prototypes read the game files and patch them **in memory** with asserted text hooks (a missing anchor throws, so a prototype cannot drift silently from the code).

| File | What it is |
|---|---|
| `econ-proto.js` | `SG.Mods` (caps, battle aggregator, fame / virtue / AP, `effLoy`, `loyAura`, exp), `SG.Econ` (develop, city gold, harvest, troops per gold, training, the five probabilities), `SG.AIBudget` (reserve, purse, order, `canSpend`). The browser port is `js/econ.js` + `js/battle-math.js`; Unity `Econ.cs` / `BattleMath.cs` (INTEGRATION §3–§5, §14) |
| `items-mod.js` | ITEMS rules in fast form on `../items.json`: kits, placements, shops and seeded restock, AI equip and buy (through AIBudget), prize pools (shop rares not owned by any faction), gear providers |
| `titles-mod.js` | TITLES rules on `../titles.json`: eligibility, rank, seats, AI granting in the discretionary phase (gifts through AIBudget), the shared city administrator |
| `tourney-mod.js` | the 武林大会 in fast form: entry, draw, matches on the measured win curve, prizes, injuries (`gen.injury`), the 3-title relic |
| `v4-all-proto.js` | the all-on harness: v3 game + ENGAGEMENT's prototype + the three modules above, every switch on, the endMonth pipeline of INTEGRATION §6.3 |
| `duel-gear-estimate.js` | the duel budget estimate (v3 Sim, best-in-slot sets per rarity, weapon-type parity) |
| `lp-recipe.md` | the declarative LP asset format (art rollout) |
| `results/` | every measured number quoted in INTEGRATION §8–§9. `campaign-*-all-share022.txt` = the adopted configuration (purse 22 %); `*-off_all-share020.txt` = the off variant and the 20 % purse; `*-nocap-first-run.txt` = the uncapped variant of the first run (catalogue before the 35 added commons) |

## Commands (from `sanguozhi2-web/`)

```
node design-v4/integration/v4-all-proto.js campaign classic 48 25        # VARIANTS=off,all[,nocap]  SEED0=0|1000   (≈ 3–6 min)
node design-v4/integration/v4-all-proto.js campaign world 24 15          # ≈ 6 min
node design-v4/integration/v4-all-proto.js player classic 6 20           # SEED0=0|500; player world 4 15
node design-v4/integration/v4-all-proto.js tactical classic 8
node design-v4/integration/v4-all-proto.js stacks classic
node design-v4/integration/v4-all-proto.js refs classic|world           # → results/econ-refs-*.json (test vectors)
node design-v4/integration/duel-gear-estimate.js 300 all|before|adopted|final|loose|weapons
```

Environment: `SHARE` / `CAPM` (purse share, default 0.22, and cap months, default 12, for AIBudget), `VARIANTS`, `SEED0`.

## Other checks the consolidation touched

```
node design-v4/items/build.js /tmp/scen.json design-v4/items.json        # VALIDATION OK (dump.js makes scen.json)
node design-v4/items/gen-md.js design-v4/items.json /tmp/scen.json /tmp/tables.md && node design-v4/items/splice-md.js /tmp/tables.md
node design-v4/domestic/stings.js                                        # 34 stings, 1 284 keys, ALL OK
node design-v4/domestic/city-stages.js                                   # budgets incl. projects, ALL OK
node design-v4/audio/voice-lines.js                                      # 123 cues, doc references, ALL OK
node design-v4/audio/suites.js                                           # motif share ≥ 25 %, ALL OK
node design-v4/audio/gen-md.js                                           # regenerates AUDIO-V4.md
```
