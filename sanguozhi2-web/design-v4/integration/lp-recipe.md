# LP recipes — the declarative asset format for v4 (lp-recipe.md)

v4 asks for a few thousand low-poly assets in the Lanternvale style: 16 city buildings × 12 culture families × 5 stages (CITY-VIEW §4), 29 landmarks, 13 ENGAGEMENT projects in three build states with 12 family names (ENGAGEMENT §8.3), 16 stadium slots with facade and crown plus 6 host gates (TOURNAMENT §8.3–§8.6), 79 item parts and the bespoke item builders (ITEMS §5.2, INTEGRATION §7.4), DOMESTIC-FX vignette props. The art rollout already has the imperative layer — `js/lowpoly.js` (`SG.LP.MeshBuilder`, 1.0) and `js/lp-props.js` (`SG.LPProps.recipe(name, { build(o) })`, 85 recipes and the building `parts`). This file adds a **declarative layer on top of it** so that these assets are data: budgeted, validated, cached, reviewed and ported to Unity the same way. It changes no art direction; every op draws through the existing MeshBuilder and LPProps parts, so the look stays the art pilot's.

Owner: the art rollout (`js/lp-recipe.js`, new). Users: CITY-VIEW (`js/city-kit.js`), ENGAGEMENT projects (through city-kit), TOURNAMENT (`js/stadium-kit.js`), ITEMS (`js/item-art.js`), DOMESTIC-FX (vignette props). Reference: INTEGRATION §12 (budgets), `design-v4/ASSETS.md` (the asset list and its cut points).

---

## 1. A recipe

```js
SG.LPRecipe.define('city.project.irrigation', {
  kind: 'project',                 // building | landmark | project | facade | crown | gate | part | prop
  budget: { tris: 900, medium: 0.5 },          // triangles before ink shells; the medium LOD keeps ≤ 50 %
  ink: 1, jitter: 0.06, ao: 0.3,               // defaults for every op (Lanternvale: buildings and props inked)
  palette: 'family',                           // 'family' (the culture family's table), 'pal' (SG.LP.PAL) or an inline map
  states: ['scaffold', 'shell', 'done'],       // optional named states (projects: ENGAGEMENT §8.4; buildings: S1 … S5)
  params: { length: 14, gates: 2 },            // overridable by the caller (ctx.params)
  ops: [
    { op: 'group', at: [0, 0, 0], ops: [
      { op: 'extrude', poly: [[-7, -0.6], [7, -0.6], [7, 0.6], [-7, 0.6]], y0: -0.3, y1: 0, color: 'water', ink: 0 },
      { op: 'repeat', n: '$gates', step: [6, 0, 0], from: [-3, 0, 0], ops: [
        { op: 'part', part: 'gate', opts: { kind: 'square', w: 1.2, h: 1.0 }, color: 'timber' } ] } ] },
    { op: 'when', state: 'scaffold', ops: [ { op: 'ref', id: 'part.scaffold.bamboo', at: [0, 0, 0], params: { w: 14, h: 2 } } ] },
    { op: 'when', state: ['shell', 'done'], ops: [ { op: 'box', at: [0, 0.6, -1.4], size: [14, 1.2, 0.5], color: 'earth' } ] },
    { op: 'when', state: 'done', lod: 'full', ops: [ { op: 'prop', name: 'reeds', at: [5, 0, 1], opts: { seed: 3 } } ] },
  ],
  anchors: { work: [0, 0, 1.5], camera: [0, 3, 9] },            // CITY-VIEW §7.1 anchors, DOMESTIC-FX shots
  lamps: [],                                                     // [{ pos, color, range, nightOnly }] like LPProps
  tags: ['hua'], notes: '芍陂 / 都江堰 style weir and channels (ENGAGEMENT §8.2)',
});
```

- **Top-level fields**: `kind`, `budget` (`tris`, optional `medium` fraction, optional `drawCalls` for kits that are not merged), `ink` / `jitter` / `ao` defaults, `palette`, `states`, `params`, `ops`, `anchors`, `lamps`, `decals` (signboards through `SG.LP.boardTexture`), `tags`, `notes` and `sources` (shown on the review sheet; landmarks and facades keep their research note).
- **Determinism**: a recipe is a pure function of `(id, ctx)` with `ctx = { family, skin, state | stage, seed, lod, params, color }`; the seed feeds `new SG.LP.MeshBuilder(seed)` exactly as LPProps does (`seed & 3` = four variant buckets).

## 2. Ops

Every op maps to one MeshBuilder call or one structural step; arguments are the MeshBuilder argument names.

| Op | Maps to | Notes |
|---|---|---|
| `box`, `taperedBox`, `roof`, `lathe`, `cylinder`, `cone`, `sphere`, `capsule`, `blob`, `facetBlob`, `segment`, `disc`, `plane`, `torus`, `extrude`, `blade`, `loft`, `beam`, `rope`, `curve`, `strip`, `flat`, `softLump`, `paperLantern`, `ribLantern` | `M.<op>(…)` | `color` is a palette token or `#hex`; `ink`, `jitter`, `emission`, `nightEmission`, `wind` override the state for this op only |
| `part` | `SG.LPProps.parts.<part>(M, opts)` | `hanRoof`, `thatchRoof`, `terracottaRoof`, `flatRoof`, `dome`, `stupa`, `wall`, `gate`, `tower`, `window`, `door`, `plinth`, `merlons`, `columns`, `banner` |
| `prop` | `SG.LPProps.geometry(name, opts)` appended (ink and plain parts kept apart) | the 85 existing props; plain parts (crops, water, flowers) stay un-inked |
| `ref` | another recipe built into this builder | cycles rejected; `params` override |
| `group` | `M.push(); translate / rotate / scale; …; M.pop()` | `at`, `rot` (degrees, Unity order Z→X→Y as MeshBuilder), `scale` |
| `repeat` | n × `group` | `n`, `step` (vector) or `around: { r, from, to }` (a ring), `jitterYaw` (seeded) |
| `mirror` | the ops again with scale −1 on an axis | MeshBuilder handles negative scale |
| `when` | include the ops if the condition holds | `state`, `stage` (number or `[min, max]`), `family`, `skin`, `lod` ('full' / 'medium'), `param` expression |
| `fn` | a registered builder `SG.LPRecipe.fn(name, (M, ctx) => {})` | the escape hatch for bespoke shapes (七支刀's branches, 金刚杵's prongs); its triangles still count in the budget |

Values may be numbers, vectors, palette tokens or `'$name'` (a param) and `'$name * k'` / `'$a + $b'` (a tiny expression grammar: `+ − × ÷`, params, numbers; no function calls).

## 3. Palettes, families and skins

- `palette: 'family'` resolves tokens through the culture family's table (`SG.CityLayout.CITY_SKIN` / CITY-VIEW §5: `timber`, `plaster`, `roofTile`, `earth`, `stone`, `water`, `accent`, `bronze`, `hanRed` …), then `SG.LP.PAL`, then `#hex`. A missing token fails the check.
- A family variant of a recipe is either a `when: { family }` branch inside it or a separate id with the family suffix (`city.bldg.market.persia`). The resolver tries `id.family.skin`, `id.family`, `id` in that order — that is the **fallback chain** ASSETS.md relies on (a family without its own model shows the generic one in its palette).

## 4. Stages and states

Buildings use `stage` 0–4 (S1–S5, CITY-VIEW §4.2's grammar); projects use the states `scaffold` (0–25 % of the work), `shell` (25–75 %), `done` (ENGAGEMENT §8.4); stadium slots use `facade` / `crown` as two recipes. The city view asks `SG.LPRecipe.build(id, { stage })` once per stage change and caches the result (`SG.LP.MeshCache` key = id + the ctx hash).

## 5. Budgets, LOD and validation

- `budget.tris` is the hard limit **before ink shells** for the `full` LOD; `medium` defaults to 0.6 (CITY-VIEW §9.5; projects 0.5, INTEGRATION §12). The medium build drops ops with `lod: 'full'` and every op whose own triangle count is < 150 (TOURNAMENT §8.10's rule), and must reach the fraction.
- `tools/lp-recipes.js check` (Node, no WebGL; MeshBuilder runs headless as in `tests/lowpoly`): builds every recipe × every family × every state / stage × both LODs and fails on: triangles over budget; an unknown op, part, prop, palette token or param; a `ref` cycle; anchors or lamps outside the bounding box + 1 m; NaN vertices; a result that differs from the stored hash (`tests/refs/lp-recipes.json`; regenerate on purpose with `--update`). It prints the triangle table the doc tables quote (CITY-VIEW §9.5, TOURNAMENT §8.4, ITEMS §5.2).
- Review sheets: `tests/lp-recipes.html?kind=project&family=persia` renders a contact sheet (noon / golden / night, outline on / off), the art pilot's comparison method (`art-pilot/tools/make_final.sh`).

## 6. Output and runtime

`SG.LPRecipe.build(id, ctx) → { ink, plain, lamps, decals, anchors, tris, size }` — the same record as `SG.LPProps.geometry`, so `SG.LPProps.make / scatter / batch` and `SG.LP.StaticBatch` accept recipe output unchanged. Builds are pure and sliced by the callers (CITY-VIEW §9.3, TOURNAMENT §8.11: ≤ 8 ms slices).

## 7. Unity port

Recipes are data: `tools/export-unity-recipes.js` writes them as JSON (`Data/Generated/LPRecipes.json`); `Art/LPRecipe.cs` interprets the same ops on the Unity `MeshBuilder.cs` (the browser's `SG.LP` is that file's port, so ops translate one to one). `fn` builders are ported by hand, one C# method per name. `Tests~/lp-recipes` builds every recipe in C# and compares triangle counts exactly and a quantised vertex hash with `tests/refs/lp-recipes.json`.

## 8. Migration

The 85 LPProps recipes stay imperative (they are done and approved); `{ op: 'prop' }` reuses them. New v4 assets are written as declarative recipes; a bespoke shape that resists data is a `fn` with its budget still checked. Item models (ITEMS §5.2) are generated from `items.json`'s `visual.model` into recipes at load (`kind: 'part'` for the 79 parts, the signature part placed at its socket), so the 602 items are 602 small recipes built on demand and cached.
