# design-v4/items — catalogue generator (design tooling, not game code)

Regenerate the catalogue and the tables of ITEMS.md:

```
node design-v4/items/dump.js /tmp/scen.json                      # scenario cities / factions / generals from js/world-data.js
node design-v4/items/build.js /tmp/scen.json design-v4/items.json # build + validate (prints VALIDATION OK)
node design-v4/items/gen-md.js design-v4/items.json /tmp/scen.json /tmp/tables.md
# then splice the GENERATED blocks of /tmp/tables.md into ITEMS.src.md's INSERT tags → design-v4/ITEMS.md
```

Tables: `core.js` (rarities, slots, types, stat profiles, palettes), `commons.js` + `commons2.js` (common / uncommon rows),
`specials-a.js` (4 legendary + 20 epic), `specials-b.js` (rare: ruler-uniques, signature, hidden, tournament, shop),
`statkeys.js` (stat dictionary), `orig.js` (original-language names). `est/duel-estimate.js` is the throw-away duel estimate of ITEMS.md §2.4.
The implementation ports build.js / gen-md.js to `tools/gen-items.js` (ITEMS.md §1).
