# preview3d: offline 3D previews of Lanternvale

A .NET 8 console tool that renders Lanternvale's 3D world without Unity. It compiles the game's own code by relative
path: Core data, MeshBuilder/World3D/Materials3D/SceneLighting, the prop library, MapTerrain/MapBackdrop/MapSky/
DayNight, and the unit recipes, rigs and animator. That code runs against a managed `UnityEngine` stand-in
(`Stubs/`) and is drawn by a software rasterizer (`Render/`) that reproduces the Lanternvale shaders.

Game sources are never edited for the tool's sake. When the game calls something the tool lacks, add it to
`Stubs/UnityEngine.cs` or `Stubs/GameStubs.cs`.

## Run

```
Tools/preview3d/preview3d.sh map lanternvale out.png                  # the player's view at the spawn, noon
Tools/preview3d/preview3d.sh map lanternvale out.png --hour 22.5      # night: lit windows, lamps, halos
Tools/preview3d/preview3d.sh map whisperwood out.png --at 25,9 --flags shepherd_hunt
Tools/preview3d/preview3d.sh units units.png all                      # one sheet per unit group
Tools/preview3d/preview3d.sh units warriors.png char_warrior,cr_bandit
Tools/preview3d/preview3d.sh props props.png all --views gq
```

`preview3d.sh` builds the tool in Release when its sources change (the log is in `bin/build.log`) and runs it. It
finds `Assets/Lanternvale/Resources` by searching upwards, or you can pass `--root <Lanternvale dir>`. A
1600×900 map at 2× supersampling takes about 5 s.

### `map <mapId> <out.png>`

| option | meaning |
|---|---|
| `--hour H` | time of day 0–24. Fixed-time maps (the shrine is at dusk) keep their own hour unless you set this. Default is the world clock, 12.5. |
| `--zoom Z` | CameraRig zoom, i.e. half the view height at the look-at point: 2.6 … 10.4, default 6.2. |
| `--yaw D` | camera yaw, ±45°. |
| `--at x,y` | look-at point in map metres. Default: the spawn given by `--spawn id`, else `default`. |
| `--size WxH`, `--ss N` | output size (default 1600x900) and supersampling (1–4, default 2). |
| `--player key` | the party leader's model at the spawn (default `char_warrior`). |
| `--flags a,b` / `--flags '*'` | story flags to treat as set, which shows NPCs, encounters and chests gated by `requireFlag`. |
| `--markers auto` / `--markers npc:kind[+main],…` | quest markers (`QuestMarker3D`) over the NPCs: `auto` runs the game's marker rules against `--flags`, `--quests q,q=stage,q=done` and `--level N` (default 5); a list names `ready`, `available`, `progress` or `later` per NPC. |
| `--no-units`, `--no-halos`, `--no-ink` | leave those out. |
| `--gamma` | use the gamma colour pipeline instead of linear. |

The camera is CameraRig's maths (`Scene/CameraMath.cs`): FOV 38°, distance = Zoom / tan 19°, pitch eased from
24° to 58° between distances 7 and 30 (curve 0.7), and the look-at clamped to the map bounds.

### `units <out.png> [keys|group|all]`

One row per unit model, with 7 columns:

1. **game**: the game camera at the default zoom, unit facing right as with `SetFacing(+1)`.
2. **3/4 idle**
3. **walk**: the frame of one cycle at 3.4 m/s with the widest foot spread.
4. **wind-up**: attack at 0.13 s.
5. **strike**: attack at `AttackHitTime`.
6. **shoot**: at `ShootReleaseTime`.
7. **cast**: at `CastReleaseTime`.

Each unit stands beside a 1.75 m slate reference figure on a 1 m checker. Poses come from stepping the real
`UnitAnimator` at 60 fps (`Scene/UnitPoser.cs` replays UnitView's Animate/ApplyBodyTransform/UpdateLook/
UpdateGround). The rigidly skinned mesh is skinned on the CPU: each vertex follows bone.localToWorld × bindpose.

Groups are `chars comps npcs trainers foes beasts pets demons static`. `all` writes `<out>_<group>.png`.

| option | meaning |
|---|---|
| `--view sheet` | the 7 columns above (default). |
| `--view game` | every column at the game camera: idle and walk facing right and left (`SetFacing(±1)`), then wind-up, strike, shoot and cast facing right (`--facing -1`: left). Use it to check faces and weapon holds in the action poses. |
| `--view spin --pose P` | one pose (`idle walk wind strike shoot cast`) at nine facings relative to the camera, from the game camera's pitch (or `--pitch deg`): in combat a unit faces its target, any way. |
| `--facing 1\|-1` | the facing of the action columns in `--view game`. |
| `--pitch deg` | the camera pitch of `--view spin`. |
| `key#N`, `key@npcId` | a look variation: `npc_child#2`, or `npc_child@child_nell` for the one that NPC id gets in the game (UnitModels.StableVariant). |

Other options are `--tile px`, `--ss N`, `--hour H` and `--speed m/s`.

The animator reads the camera yaw (`UnitAnimInput.ViewYaw`): staff and spear holds lean away from the camera on the
holding hand's side, so a sheet shows the hold the game shows from the same camera.

### `props <out.png> [keys|all]`

Contact sheets of `PropModels.Create`, with stats. Each model is shown with its nav-collider ellipse (red), its light
anchors (magenta) and a 1.75 m person.

- Views (`--views`): `g` game, `q` 3/4, `t` top, `f` front, `b` back.
- `--lit 0|1` switches lamps and windows, `--open` opens chest lids, and `--stats` prints stats only.
- Shading uses the meshes' vertex normals (faceted parts stay faceted, soft foliage is smooth), as in map renders.
- The footprint check (front violation vs the collider ellipse) skips ground parts, i.e. child renderers a model
  leaves out of `PropModel.Renderers` (Old Kusu's plaza paving); the stats line marks them `(ground)`.

## Faithful vs approximated

**Faithful:**
- Map building follows MapView.BuildAll: seeds, transforms, tints, light anchors, night gating, halos, merged
  shadows, decals (the real MapTerrain.BuildDecals), waymarkers (the shared Waymarker, side-exit posts fitted clear of
  props with Waymarker.FitPosts) and occluder cut-outs (the shared PropOccluder): every hole fully open, around each
  unit an occluder hides and around the camera's look-at point; there is no cursor, so no cursor or hover cut.
- The terrain's detail layer (`_DetailTex`: shrine gravel, Whisperwood leaf litter; MapTerrain.DetailTexture).
- Units are placed like GameFlow.Views (NPC sprite/flip and look variant, enemy size and facing towards the leader;
  SetFacing is screen-right/left for `--yaw`, via UnitFacing.SideYaw).
- Terrain, backdrop and sky come from the real classes.
- Lighting is DayNight.ApplyTo → SceneLighting (the 16 strongest point lights near the focus), with the night grade
  (`_LV_Grade` from DayNight.NightGrade) and amber, wider lamplight at night (DayNight.LampColor / LampRange).
- Warmth (`_LV_Warmth` from DayNight.Warmth: x = golden-hour saturation lift of sunlit colours, y = lamplit colours
  leaning to amber, z = a lamp pool greying out the moon fill under it) and the golden-hour haze (fog towards
  DayNight.GoldenHaze by DayNight.Golden, fog starting nearer), as MapView.ApplyMood sets them.
- Lit parts swap at night through the props' `SetLit` (lamps, windows, campfires, Old Kusu's paper lanterns).
- LV_Shade: wrapped sun, hemisphere ambient, point lights, rim, emission, fog.
- Linear colour space.
- Dithered `_Fade` and `_Cut0`…`_Cut3` (LV_CutCoverage: LowPoly keeps surfaces behind the cut's centre, the ink hull
  is cut at any depth).
- Units are posed for the map camera's yaw (`UnitPoser.CameraYaw`): idle heads turn to the camera, static models face
  it, and blob shadows come from the shared UnitShadow.Blob.
- Inverted-hull ink: 2.2 px at 1080p for maps, 1.9 px for units relative to their on-screen size.

**Approximated:**
- One frame at t = 0: no wind sway, no flicker (averaged), no particles (fireflies, leaves, mist, embers), no FX,
  rings, nameplates or UI.
- `Mathf.PerlinNoise` is a stand-in gradient noise, so terrain blotches differ in detail from the game.
- Dither is box-filtered by the supersampling, so it reads as translucency.
- Texture filtering is a trilinear-style mip pick, not anisotropic.

## Files

| path | contents |
|---|---|
| `Program.cs` | modes and options |
| `PropSheet.cs` | props mode, `SheetImage` |
| `UnitSheet.cs` | units mode |
| `Scene/MapScene.cs` | MapView.BuildAll replica |
| `Scene/UnitPoser.cs` | unit body + animator driver |
| `Scene/Collector.cs` | renderers → draw calls, CPU skinning |
| `Scene/CameraMath.cs` | CameraRig |
| `Render/` | rasterizer, G-buffer, LV_Shade, ink, PNG I/O |
| `Stubs/` | UnityEngine and game stand-ins |

The committed previews in `Docs/previews3d/` are re-rendered by `Tools/preview3d/render_previews.sh`, which takes
an optional output directory.
