# design-v4/engage — ENGAGEMENT.md supporting files

| File | Use |
|---|---|
| `catalog.js` | The engagement catalogue as data (events with declarative effects, historical seeds, sage personas, missions, projects, expedition sites, festivals, achievements, starting bonds and relations). The header documents the effect vocabulary. |
| `build-json.js` | `node design-v4/engage/build-json.js` validates the catalogue against the scenario data and writes `design-v4/engagement.json`; `… check` only validates. Exit code = number of errors. |
| `engage-proto.js` | Reference implementation and balance prototype. Patches the game in memory only (the repo files are never written; one hook is injected into the text of `attackNeed` before evaluation). |

Runs used for ENGAGEMENT.md §19 (Node ≥ 18, from the repo root `sanguozhi2-web/`):

```
node design-v4/engage/engage-proto.js campaign classic 48 25                 # all-AI, variants off / on / nodom / dip / proj
SEED0=1000 node design-v4/engage/engage-proto.js campaign classic 48 25      # second seed set
VARIANTS=off,on,dip node design-v4/engage/engage-proto.js campaign world 24 15
node design-v4/engage/engage-proto.js player classic 6 20                    # scripted players ai / v3 / v4
POL=v4 SEED0=500 node design-v4/engage/engage-proto.js player classic 6 20
POL=v3,v4 node design-v4/engage/engage-proto.js player world 4 15
```

Environment switches: `VARIANTS`, `POL`, `PLAYERS`, `SEED0`, `OFFSYS=order,events,rel,need,bonds,domexp,prestige,soft` (turn subsystems off for diagnosis), `NEED_EAGER=1` (the rejected "bad relations make the AI eager" rule), `CAP` (routine exp cap), `RECORD` (troops per order point lost when recruiting), `OSTEP`, `OATH`, `SUBR` / `SUBREL` (归附 thresholds), `FRIENDS_N`, `PERSEED=1` (per-seed defection counts).

Runtime on one core: classic 48 × 25 years × 5 variants ≈ 2 minutes; world 24 × 15 × 3 ≈ 2.5 minutes; the player harnesses 20–80 s each.
