# 三国志II 霸王的大陆 · Web port — module contract

This folder is a browser port of the Unity project in `../sanguozhi2-unity/`.
The Unity C# code is the **source of truth** for rules, numbers, text, flows and
looks. Port it faithfully (same formulas, same constants, same Chinese text, same
model shapes and colors), translating Unity idioms as described here. Where the
port needs something Unity gave for free (rendering, input, UI), follow this file.

Every module must match the names below exactly, because modules are written in
parallel and then loaded together.

## Files and load order

`index.html` loads, in this order (classic `<script>` tags, no modules, no bundler):

```
vendor/three.min.js        three.js r158 UMD build → global THREE (already present)
js/core.js                 SG namespace + Unity-compatible helpers (already written — read it)
js/data.js                 Balance, Defs, Terrain, ScenarioData           ← Data/Defs.cs, Data/ScenarioData.cs
js/model.js                GameState, Commands                            ← Model/GameState.cs, Model/Commands.cs
js/strategy-ai.js          Conquest, StrategyAI                           ← Strategy/Conquest.cs, Strategy/StrategyAI.cs
js/battle-model.js         BattleModel, BUnit                             ← Battle/BattleModel.cs
js/audio.js                Sfx                                            ← Core/Sfx.cs
js/ui.js                   UI kit (DOM)                                   ← UI/UIKit.cs
…                          (v4: settings, UI art, shell, portraits — see index.html)
js/portrait.js             Portrait (face data used by figure.js)
js/lowpoly.js              SG.LP: v4 low-poly toolkit (MeshBuilder, materials, outlines, instancing, Mood)   — v4 art
js/lp-props.js             SG.LPProps: shared prop recipes + building parts (needs SG.LP)                   — v4 art
js/figure.js               SG.Figure: generals, soldiers, horses, crowds (needs SG.LP / SG.LPProps)          — v4 art
js/art.js                  Gfx (renderer, scene, lights, sky, materials), MeshBuilder, Models ← View/Art.cs + shaders + Game.SetupWorld
js/culture-art.js          SG.CultureArt: city models per culture (needs SG.LP / SG.LPProps)               — v4 art
js/camera.js               CameraRig                                      ← View/CameraRig.cs
js/map-view.js             MapView                                        ← View/MapView.cs
js/battle-view.js          BattleView, UnitVisual                         ← Battle/BattleView.cs
js/battle-controller.js    BattleController                               ← Battle/BattleController.cs
js/strategy-screen.js      StrategyScreen                                 ← Strategy/StrategyScreen.cs
js/game.js                 Game (boot, title, ruler select, main loop)    ← Core/Game.cs
css/style.css              all styling for the DOM UI
```

Each JS file is wrapped as

```js
'use strict';
(function () {
  const SG = window.SG;
  const M = SG.M;
  // ...
  SG.Something = Something;
})();
```

Never declare top-level `const`/`let`/`class` outside the IIFE (files share one
global scope). Export only via `SG.*`.

The logic files (`data.js`, `model.js`, `strategy-ai.js`, `battle-model.js`) must
not touch `THREE`, `document`, or the DOM, so they can run under Node for tests
(tests set `global.window = global` and load the files with `vm`/`require`).

## Translating C# → JS

- `Mathf.X` → `SG.M.x` (camelCase: `clamp`, `clamp01`, `lerp`, `inverseLerp`,
  `smoothStep`, `roundToInt`, `ceilToInt`, `floorToInt`, `perlinNoise`,
  `lerpAngle`, `repeat`). `Mathf.Min/Max/Abs/Sqrt/Exp/Pow/Sin/Cos` → `Math.*`.
- **C# integer division** (`int / int`, e.g. `c.town / 12`, `troops / Balance.TroopsPerFood`)
  → `M.idiv(a, b)` (truncates toward zero). Check every `/` in the C# source:
  if both operands are `int`, use `M.idiv`. Expressions like `x / 260f` stay float.
- `Random.Range(int a, int b)` (max exclusive) → `SG.Random.rangeInt(a, b)`.
  `Random.Range(float a, float b)` → `SG.Random.rangeFloat(a, b)`.
  `Random.value` → `SG.Random.value()`.
  The C# helper `R(a, b)` in Commands is `Random.Range(a, b + 1)` (inclusive).
- `new System.Random(seed)` → `SG.SeededRandom(seed)` with `.nextDouble()` and
  `.next(max)` / `.next(min, max)` (max exclusive).
- LINQ → array methods. `OrderBy(...).ThenBy(...)` → a stable `sort` with a
  comparator (JS sort is stable). `First()` throws in C#; use `[0]` and handle undefined.
- Coroutines (`IEnumerator`, `yield return X`) → `async` functions with `await`.
  `yield return new WaitForSeconds(s)` → `await SG.wait(s)`. `yield return null`
  → `await SG.frame()`. A coroutine that returns a value through `Ref<T>` returns
  the value directly from the async function instead.
- C# `string.Format` → template literals with the same text. Unity rich text
  (`<color=#xxxxxx>`, `<b>`, `<size=N>`) → HTML (`<span style="color:#xxxxxx">`,
  `<b>`, `<span style="font-size:Npx">` scaled ×0.6 to CSS px is fine).
  UI labels are **HTML strings** generated by our own code; escape any data that
  could contain `<` with `SG.esc` (general names never do, so it's optional).
- `Debug.Log` → `console.log`.

## Coordinates (important)

Unity is left-handed with +z = north; three.js is right-handed. Convention:

- **World position in three = (unityX, unityY, −unityZ).** Use `SG.U(x, y, z)`
  to build a `THREE.Vector3` from Unity coordinates.
- Map coordinates `(mapX, mapY)` (from `City.MapPos`) are Unity `(x, *, z=mapY)`.
- Unity yaw (degrees about +y) → `object.rotation.y = SG.yawToThree(deg)`.
- `MeshBuilder` takes **Unity-convention coordinates** (exactly the numbers in
  the C# code) and converts each vertex with z → −z internally. Because mirroring
  one axis flips winding, the C# triangle order (clockwise-front in Unity) becomes
  counter-clockwise-front in three. So model code from `Art.cs`, `MapView.cs` and
  `BattleView.cs` can be copied verbatim. Normals are computed after conversion.

## Shared data shapes (`model.js`)

Field names are the C# field names (already camelCase in C#).

```js
city    = { id, key, name, lon, lat, land, industry, town, population, gold, food, owner, governor, links: [] }
general = { id, name, war, intel, pol, faction, city, loyalty, troops, training, hidden, dead, formation, moved }
faction = { id, key, name, color /* '#rrggbb' */, ruler, virtue, fame, alive }
```

Helpers replacing C# properties:

- `SG.mapPos(city)` → `{ x: (lon-100)*5, y: (lat-23)*5.6 }`
- `SG.cityDefense(city)` → `20 + M.idiv(city.town, 12)`
- `SG.maxTroops(general)` → `Balance.GeneralTroopBase + general.war * Balance.GeneralTroopPerWar`
- `SG.isFree(general)` → `general.faction < 0 && !general.dead`
- `SG.factionColor(faction)` → the hex string

`SG.GameState` (class). The current game is **`SG.G`** (a plain property,
replaces `GameState.Current`).

```
static newGame(playerFactionKey) → GameState      init()
year, month, player, tokens, seed, cities[], generals[], factions[], alliance[], log[]
get monthIndex
cityByKey(k) factionByKey(k) get playerFaction
citiesOf(f) generalsOf(f) generalsIn(cityId, f) officersIn(city) freeFoundIn(cityId)     → arrays
troopsIn(city) isRuler(g) ruler(f) cityCount(f) tokensFor(f) allied(a, b) setAlliance(a, b, untilMonth)
autoGovernor(city) log(text) checkFactionDeath(f)
save()                         localStorage key 'sanguozhi2_save' (wrap in try/catch; returns bool)
static hasSave() static load() → GameState (calls init) or null
```

`SG.Commands` (object): `devName(kind)`, `develop(c, g, kind)`, `recruitMax(c, g)`,
`recruit(c, g, troops)`, `train(g)`, `search(c, g)` (sets `SG.Commands.searchFound`),
`hireChance(target, recruiter, faction)`, `hire(target, recruiter, faction, cityId)`,
`reward(c, g)`, `move(g, toCity)`, `transport(from, to, gold, food)`, `foodPrice()`,
`buyFood(c, gold)`, `sellFood(c, food)`, `allyChance(f, target, gift)`,
`ally(fromCity, f, target, gift)`, `discord(agent, target)`,
`persuadeChance(agent, target, f)`, `persuade(agent, target, f)`.
`SG.DevKind = { Land: 0, Industry: 1, Town: 2 }`. All return the same Chinese
message strings as C#.

## `data.js`

- `SG.Balance` — every C# `Balance` constant with the **same PascalCase names**.
- `SG.Terrain = { Plain: 0, Forest: 1, Hill: 2, Mountain: 3, River: 4, Wall: 5, Gate: 6, Castle: 7 }`
- `SG.TacticKind = { Fire: 0, Rockfall: 1, Confuse: 2, Inspire: 3 }`
- `SG.Defs.formations` = `[{ name, atk, def, move, reqInt, reqWar, desc }]` (8, same order)
- `SG.Defs.tactics` = `[{ kind, name, reqInt, power, desc }]` (4, same order)
- `SG.Defs.terrainDef(t)`, `terrainCost(t)`, `terrainName(t)`
- `SG.ScenarioData = { StartYear, StartMonth, Title, Intro, Cities: [...], Links: [...], Factions: [...], Generals: [...] }`
  — the same text tables, copied exactly.

## `strategy-ai.js`

`SG.Conquest`: `prepare(attacker, src, target, atkGenerals, food, gold)` → setup,
`autoResolve(s)`, `apply(s)`, `handleRulerLoss(f)`, `succession(f)`,
`aiDecideCaptives(s, winner)`, `release(g)`, `execute(g)`.

```
setup = { attacker, defender, src, target, atk: [], def: [], atkFood, atkGold,
          attackerWon, routed: Set<generalId>, captives: [], summary }
```

`SG.StrategyAI`: `endMonth()` → news string array; `runAI(news)` → array of setups
attacking the player (battles the player must fight).

## `battle-model.js`

`SG.BUnit` class: fields `gen, side, x, y, morale, formation, acted, confused,
commander, alive`; getters/setters `troops` (proxy to `gen.troops`, clamped ≥ 0),
getters `form`, `mobility`.

`SG.BattleModel` class (same members as C#, camelCase):

```
constructor(setup)
setup, W, H, map[x][y], burning[x][y], height[x][y], units[], day, side, ap, food[2],
castle {x, y}, result (0 ongoing, 1 attacker won, 2 defender won), resultReason, dayLog[]
inBounds(x,y) passable(x,y) unitAt(x,y) alive(side)→array commander(side) static dist(a,b)
distances(from{x,y}, mover, limit, moverSide) → 2D array   moveTargets(u) → [{x,y}]
pathTo(u, to{x,y}) → [{x,y}] including start and end       adjacentEnemies(u)
actionPoints(side) canAct(u) spend(u) endSide() → bool (true = new day)
move(u, to) attack(a, t) → {dmg, counter, targetRouted, selfRouted}
tacticUsable(u, t) → {ok, why} tacticRange(t) tacticTargets(u, t) useTactic(u, t, target) → {success, dmg}
duel(a, b) → {accepted, rounds: [{who, dmg, hpA, hpB}], winner}
changeFormation(u, f) formationAllowed(u, f) checkEnd() aiFormation(u)
planFor(u) → {unit, move: {x,y} | null}   planAction(u) → {unit, kind: 'none'|'attack'|'tactic'|'duel', target, tactic}
```

Static `SG.BattleModel.Dirs = [{x:1,y:0},{x:-1,y:0},{x:0,y:1},{x:0,y:-1}]`.
Grid points are plain `{x, y}` objects (compare by fields, not identity).

## `audio.js`

`SG.Sfx`: `init()`, `unlock()` (create/resume the AudioContext; call from the
first user gesture), `click()`, `play(name, vol = 0.8)`, `music(kind)` (kinds:
`'title' | 'map' | 'battle'`), `setMusic(on)`, `musicOn`, `soundOn`. Synthesize
the same sounds as `Sfx.cs` (render the PCM into `AudioBuffer`s, cache them).
Must never throw if audio is unavailable.

## `ui.js` (DOM UI; replaces UGUI)

`SG.UI.init()` creates inside `#ui` (fixed, full screen, above the canvas) four
layers: `labels`, `screens`, `modals`, `toasts` (in that z-order). `screens`,
`modals` and `toasts` respect `env(safe-area-inset-*)`.

Async dialogs (all return Promises; keyboard: Enter/Space confirm or advance,
Escape cancels):

```
say(text, speaker = null, speakerColor = '#c8382c')       → Promise<void>  (typewriter, click/tap to advance)
choose(title, items, info = null, width = 640)            → Promise<index | -1>
chooseMany(title, items, max, info = null)                → Promise<number[] | null>
pickNumber(title, min, max, step, start, describe = null) → Promise<number | -1>
confirm(text, yes = '是', no = '否')                        → Promise<boolean>
toast(html, seconds = 2.2)   banner(text, sub = null, seconds = 2.4)   anyModal() → boolean
```

`items` = `[{ label /* html */, right /* html or null */, enabled = true, desc = null, selected = false }]`,
helper `SG.UI.item(label, right, enabled = true, desc = null)`.

DOM builders: `SG.UI.el(tag, className, html)`, `SG.UI.button(parent, html, onClick, {primary})`
→ `HTMLButtonElement`, `SG.UI.medal(glyph, color, size)` → element,
`SG.UI.bar(value01, color)` → element with `.setValue(v)`, `SG.UI.panel(parent, className)`.

World-anchored labels (city names, unit info, floating damage numbers):

```
SG.UI.follow(element, getWorldPos /* () => THREE.Vector3 */, { offsetY = 0, hideBeyond = 9999 })
  → handle { el, alpha /* settable multiplier */, remove() }
SG.UI.updateFollowers(camera)   called once per frame by the game loop
SG.UI.floatText(worldPos, text, color, sizePx)   rising, fading number/text (1.2 s)
```

`follow` appends the element to the `labels` layer, positions it each frame from
`SG.Gfx.worldToScreen`, hides it when behind the camera or farther than
`hideBeyond`, and never blocks pointer events.

Look: same as the Unity UI — dark lacquer panels (`rgba(18,18,26,.9)`), gold
outline `#f3c969`, cinnabar `#c8382c` primary buttons, Kai/Song serif for titles
(`"STKaiti","Kaiti SC","KaiTi","Songti SC","Noto Serif SC",serif`), sans for
body (`"PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif`).
Buttons ≥ 44px tall for touch. Layout must work at 1280×720, 1920×1080,
iPad 1024×768 and iPhone landscape 844×390 / 932×430.

## `art.js`

`SG.Gfx`:

```
init(container)       create THREE.WebGLRenderer({antialias:true}) appended to container,
                      pixel ratio min(devicePixelRatio, 2), shadows (PCFSoftShadowMap),
                      SRGB output, scene, PerspectiveCamera(34°, near 0.5, far 700),
                      sun DirectionalLight (direction from Unity Euler(48,-35,0), color #fff0d6,
                      casts shadows, 2048 map), HemisphereLight trilight fill (sky/equator/ground
                      colors from Game.cs), linear fog (#dbd6c7, 90 → 320), sky dome
                      (ShaderMaterial port of Sky.shader), resize handling.
renderer scene camera sun
setShadowFocus(worldVec3, radius)   keep the sun's shadow camera around the visible area
worldToScreen(worldVec3) → { x, y, visible, dist }   CSS pixels relative to the viewport
render()
lowPoly()             shared MeshLambertMaterial({ vertexColors: true }) (flat normals come from geometry)
newLowPoly()          a fresh clone (for per-object color tint / flash)
water()               ShaderMaterial port of Water.shader (waves, shore color from vertex color r, fog)
unlit(color, texture) MeshBasicMaterial (transparent, depthWrite false, side Double, polygonOffset)
additive(color)       for particles/glow (AdditiveBlending, soft dot texture)
ringTexture, softDotTexture   CanvasTextures
shade(color, k)       THREE.Color lerped toward white (k>0) or black (k<0)
color(hexOrRgb)       → THREE.Color
mesh(geometry, material, { castShadow = true, receiveShadow = true }) → THREE.Mesh
```

`SG.MeshBuilder` (Unity-convention input coordinates, see above; points are
`THREE.Vector3` or `{x,y,z}`; colors are `THREE.Color`):

```
tri(a, b, c, col) quad(a, b, c, d, col) flatQuad(a, b, c, d, col)   (flatQuad has UVs 0..1)
box(center, size, col, topShade = 0.08) cylinder(bottom, r0, r1, h, seg, col, cap = true)
cone(bottom, r, h, seg, col) blob(center, radius, col, seed = 0)
chineseRoof(baseCenter, w, d, h, col) flag(pole, h, fw, fh, col)
count  toGeometry() → THREE.BufferGeometry (non-indexed, position/normal/color[/uv])
```

`SG.Models`: `tree(mb, p, s, leaf, pine, seed)`, `city(size, banner, capital)` →
geometry, `soldier(mb, p, team, spear, s = 1)`, `commander(mb, p, team, s = 1)`.

## `camera.js`

`SG.CameraRig` class wrapping `SG.Gfx.camera`:

```
target, desired (THREE.Vector3 in world space), distance, desiredDistance, minDist, maxDist,
pitch (52), yaw, desiredYaw, bounds { xMin, yMin, xMax, yMax } in MAP coordinates, inputEnabled,
onTap: array of callbacks (screenX, screenY)
focusMap(mapX, mapY, dist = -1, instant = false)    focus on map coordinates
focusWorld(vec3, dist = -1, instant = false)
update(dt)            same smoothing and pitch-by-distance as CameraRig.cs
```

Input on the renderer canvas only (so DOM UI naturally blocks it): pointer drag
pans along the ground, wheel zoom, two-pointer pinch zoom, tap (no drag)
fires `onTap`, WASD/arrows pan, Q/E rotate.

## `map-view.js`

`SG.MapView` class:

```
build(state) refresh(state) select(cityId) pick(screenX, screenY) → cityId | -1
height(mapX, mapY)  update(dt)  march(fromCity, toCity, colorHex, seconds = 1.6) → Promise
root (THREE.Group, toggle root.visible for battles)
cities: Map<cityId, { city, group, labelPos: THREE.Vector3 (world), pos: THREE.Vector3 (world) }>
```

## `battle-view.js`

`SG.BattleView` class: `constructor(model)` builds the board at Unity origin
(400, 0, 0) with tile size `T = 2`.

```
group  boardCenter (world Vector3)  tile(x, y) → world Vector3  topH(x, y)
clearHighlights() highlight(tiles, cssColor, alpha) setCursor(unit | null)
tileFromScreen(screenX, screenY) → {x,y} | null
moveUnit(u, path) lunge(a, t) rout(u) fireFx(t) rockFx(t) magicFx(t, cssColor)  → Promises
hit(u, dmg, big = false) refresh(u) burst(worldPos, cssColor, n, speed) smoke(worldPos)
floatText(worldPos, text, cssColor, size)  update(dt)  dispose()
```

Unit info labels use `SG.UI.follow`. Particles: a lightweight system of
billboarded sprites or `THREE.Points` with additive material, updated in `update(dt)`.

## `battle-controller.js`, `strategy-screen.js`, `game.js`

- `SG.BattleController.run(setup, playerSide)` → Promise (port of the coroutine).
- `SG.StrategyScreen` class with `run()` → Promise.
- `SG.Game` (object): `boot()`, `rig` (CameraRig), `map` (MapView),
  `battleView` (active BattleView or null), `strategy`.
  `boot()` order: `Gfx.init(#app)`, `Sfx.init()`, `UI.init()`, create rig, temporary
  `GameState.newGame('liubei')` for the title background, `map.build`, start the
  render loop, run the title flow. The loop each frame: `rig.update(dt)`,
  `map.update(dt)`, `battleView?.update(dt)`, `Gfx.setShadowFocus(rig.target, …)`,
  `UI.updateFollowers(camera)`, `Gfx.render()`.
- Returning to the title after victory/defeat: `location.reload()`.
- First user gesture anywhere calls `SG.Sfx.unlock()`.

## Testing hooks

`window.SG` stays reachable from the page so Playwright tests can call
`SG.Game`, `SG.G`, `SG.Commands`, etc. Add `?seed=N` support in `game.js`
(`SG.Random.seed(N)`) for reproducible screenshots.

## v4 art: the Lanternvale low-poly style (SG.LP, SG.LPProps, moods)

Load order (fixed): `portrait.js → lowpoly.js → lp-props.js → figure.js → art.js → culture-art.js → camera.js →
map-view.js → …`. Test pages that use the pilot recipes load `lp-pilot.js` **after** `lp-props.js` (its `Pilot.*`
wrappers build from SG.LPProps). The full API of each module is in its file header; the parts other modules rely on:

- **SG.LP** (`js/lowpoly.js`, version `SG.LP.version` = '1.0'): `MeshBuilder` (transform stack, primitives, `softLump`
  foliage, `append(builder)`, `appendGeometry(lpGeometry, { matrix, ink: 0 | 1 | 'attr', bone })`,
  `toGeometry({ skinned, compact })`); `material(opts)` / `outlineMaterial({ color, width, windScale, fogScale,
  distance: [d0, d1, k] })` (shared instances — per-object looks go through `setLook(obj, look)`, never by editing a
  shared material); `mesh(geo, opts)`, `skinnedMesh`, `lod`; `instances(geo, capacity, { tints, outline, outlineOpts })`
  + `setInstance` / `setInstanceMatrix` / `commitInstances` (per-instance tints are sRGB multipliers applied in the
  fragment shader); `Scatter({ chunk, lodDist, outline })` (`.add`, `.build()`, `.update(camera)`, `.chunks` =
  `[{ center, radius, near, far, isFar }]` for sets with a far geometry, `.meshes`); `StaticBatch`, `mergeGeometries`;
  `BlobShadows`, `blobShadow`; `decal`, `groundDecal`, `decalTexture`, `boardTexture`; `HaloBatch`; globals
  `setTime`, `setWind`, `setViewport`; `Mood` (below). Gamma workflow: LP shaders decode vertex colours as sRGB and
  encode their own output.
- **LP.Mood(scene, opts)**: `set('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | hour)`,
  `state`, `addLamp(pos, { color, intensity, range, nightOnly, halo, haloSize, flicker })`, `setLampLit`, `moveLamp`,
  `removeLamp`, `prewarm(renderer, camera, count?, keep = 1)`, `update(camera)` (scene hook), `dispose()`. Lamps share a
  pool of `maxLamps` point lights (nearest lit lamps win); the pool is hidden while no lamp is lit, so day scenes compile
  shaders without point lights. Add a scene's lamps while building it, before its first render; call `prewarm` once
  for a view that will later reach night (it compiles the lamp-lit variant ahead of the first dusk).
- **SG.Gfx mood** (`js/art.js`, main scene): `SG.Gfx.mood` (LP.Mood driving the existing sun / sky lights; noon is
  the classic look), `setMood(what)` → state, `moodState()`, `addLamp`, `removeLamp`, `setLampLit`,
  `newMood(scene, opts)` (own mood for a full-screen scene such as the duel or the siege clash).
- **SG.LPProps** (`js/lp-props.js`): `names(group)`, `groups`, `info(name)`, `has(name)`, `geometry(name, opts)` →
  `{ ink, plain, lamps, decals, anchors, tris, size }` (cached and shared — never dispose), `make(name, opts)`,
  `addLamps(obj, mood)`, `scatter(sc, …)`, `batch(sb, …)`, `parts.*(M, opts)` (building parts), `clearCache()`.
  Options: `{ seed, lod: 'near' | 'far', color, variant, lit }` plus per-recipe keys; `gate_tower` takes `name`
  (gate plaque) and `hallName` (hall plaque) — the plaques are decals, the mesh is shared between names.
- **Budgets** (triangles): character ≤ ~3.1k (`SG.Figure.budgets`: high 3100, **hero 3200**, mid 1800,
  low / soldier 900, horse 2600 / 1800 / 900), big building ≤ ~3.2k, tree ≤ 1.5k, small prop ≤ 400.
  Characters have real adult proportions (≈ 7.5 heads, heroic generals up to ~7.8).
- **SG.CultureArt** (`js/culture-art.js`): `model(culture, capital, city, { size, lod: 'near' | 'mid' | 'far' })`
  (cached geometry flagged `userData.sharedGeometry` — never dispose), `object(size, culture, capital, city, { lod })`,
  `lamps(size, culture, capital, city)`, `scatter(list, { chunk, lodDist, inkDist, midDist, cull, inkTaper })` →
  `{ group, scatter, lamps, update(camera), setCull(on) }` (call `update(camera)` every frame after the camera rig
  update: per-city frustum culling and LOD rely on it), `modelKey(culture, city, capital)` (e.g. `persia:ctesiphon`),
  `tierOf`, `variantOf`, `cultureOfCity`, `isCapital`, `city(…)`, `cityInto(mb, …)`, `cityFarInto(mb, …)` (without a
  city it reuses the city of the previous `cityInto` call), `clearCache()`.
- **MapView mood**: `setMood('dawn' | 'morning' | 'noon' | 'afternoon' | 'golden' | 'dusk' | 'night' | 'title' | hour |
  null)` (null = default: warm afternoon 16.3, 'title' = the title flyover's lantern-lit dusk; `Game.titleFlow` calls
  `setMood('title')` on entry and `setMood(null)` on leaving), `followClock(on)` (follow `SG.Clock.hour()`; the strategy
  screen turns it on when the 昼夜光影 setting is on), `moodHour` (getter). City lamps join the main mood only after
  the first real dusk (night > 0.45); until then no point lights exist. `BattleView#setMood(what)` sets the board's
  hour on the shared main scene and restores the map's on dispose.

