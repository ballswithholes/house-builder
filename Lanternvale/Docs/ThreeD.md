# Lanternvale 3D — presentation contract

The game is moving from a 2.5D sprite diorama to **full 3D, stylized low-poly**: a BG3-style tilted perspective camera
that rotates and zooms, 3D terrain and backdrops, procedurally modelled characters with real walk cycles, soft painted
lighting with lanterns at night. **Rules, data, session, UI (IMGUI) and game flow do not change.** Only the
presentation layer (world, units, FX, camera) is rebuilt behind its existing APIs.

There is no 3D artist and no asset store: **every model is built in code** with `MeshBuilder`. The look must still be
appealing: cozy, whimsical, Ghibli-warm palettes, clean ink outlines, soft light, gentle wind, warm lanterns at night.
Characters follow a **Final Fantasy X-inspired** design language (expressive heads and hair, layered outfits with
belts/sashes/shoulder pieces, distinct class silhouettes and weapons), rendered as stylized low-poly.

## 1. World conventions (`Scripts/Game/Rendering3D/World3D.cs`) — read this first

* **Ground = the XY plane** (as in the rules/nav grid): x along the map `0..width`, y = depth `0..depth`
  (0 = front edge nearest the default camera). 1 unit = 1 m.
* **UP IS −Z.** A point `h` m above ground point `(x, y)` is `(x, y, −h)` = `World3D.At(ground, h)`.
* A `Vector2` world position is a ground point; Unity's implicit `Vector2 → Vector3` gives `(x, y, 0)`, which is
  correct. Anything in the air is an explicit `Vector3` with negative z. `World3D.Up * h` lifts a point.
* **Models are authored in normal Unity local space** (Y up, +Z forward, +X right) and stood up with
  `World3D.Upright` / `World3D.Yaw(deg)`: local +Y → world −Z, local +Z → world +Y (into the scene).
  `Yaw(0)` faces into the scene, `Yaw(90)` faces right (+X), `Yaw(−90)` left, `Yaw(180)` towards the camera.
  `World3D.Facing(dir2D)` / `YawOf(dir2D)` convert ground directions.
* **Props face the camera:** author their front (door, sign face, chest lock) towards local **−Z**; MapView stands
  them with `World3D.Upright`. Characters are authored facing local **+Z** and turned with `Yaw`.
* The walkable ground inside the map rect (`x∈[0,W]`, `y∈[0,D]`) is **flat at z = 0** (ground previews, decals,
  shadows and rings lie on it). Terrain may rise only outside it (hills behind, banks/edges around) — never above
  z = 0 inside the rect.
* **Do not use** Unity `Light` components, skyboxes, `Terrain`, physics, `RenderSettings` lighting/fog or anything that
  assumes Y-up. Lighting is `SceneLighting` + our shaders.

## 2. Rendering

* **Pipeline:** the built-in render pipeline (the editor/build step `LanternvaleMenu.Configure3D` unassigns any URP
  asset). Our shaders have no LightMode tag, so they also render under URP if a project still has it; never depend on
  a pipeline.
* **Shaders** (`Resources/Shaders`, always in builds; shared code in `LanternvaleCommon.cginc`):

| Shader | Use |
|---|---|
| `Lanternvale/LowPoly` | opaque vertex-coloured models; vertex alpha = emission; uv1.x = wind weight; optional painted `_MainTex` (mesh UVs, or world-XY planar with `_PlanarScale` for terrain); per renderer `_Tint`, `_Flash`, `_Fade` (dither dissolve), `_Rim`, `_FogScale`, `_WindScale`; `_Cull` |
| `Lanternvale/Outline` | ink outline (inverted hull, constant pixel width) — add as the **last** material: `Materials3D.WithOutline()` |
| `Lanternvale/Additive` | glows, sparks, magic (SpriteRenderer, particles, meshes), fogged out with distance |
| `Lanternvale/Shadow` | soft blob shadow quad on the ground (`MeshCache.AddShadow`) |
| `Lanternvale/LitTransparent` | painted textures with alpha, lit (ground decals, foliage cards, water) |
| `Lanternvale/Sky` | unlit vertex-coloured sky dome / far backdrop |

  Add your own shaders next to them if you need one (same rules: `#include "LanternvaleCommon.cginc"`, no pipeline
  includes, no LightMode tag, `#pragma target 3.0`). Unity's `Sprites/Default` is fine for alpha-blended billboards.
* **Materials** (`Materials3D`): shared, never instanced per object. Per-object looks go through
  `Materials3D.Look` (one MaterialPropertyBlock per renderer: Tint, Flash, Fade, Rim, FogScale, WindScale,
  OutlineColor, OutlineWidth). `Materials3D.Textured(tex, planarScale)`, `LitTransparent(tex)`, `AdditiveFor(tex)`.
* **Lighting** (`SceneLighting`): globals pushed each frame by `SceneLightingDriver` — sun direction (to the sun,
  up = −Z) / colour / intensity, sky & ground ambient, fog colour/start/end/max, rim colour, `NightGlow` (emission
  boost), wind strength/speed, and up to 16 point lights (`SceneLighting.Add(pos, color, intensity, range)` →
  handle; set `Enabled`, move `Position`, `Remove` when done). The world owns the mood (DayNight → SceneLighting);
  FX/units may add short-lived lights (spell glows, casting hands) and must remove them.
* **Vertex colours are sRGB** (the shaders convert). Emission = vertex alpha, so **MeshBuilder writes alpha =
  `Emission`** (0 lit, 1 glowing). Never feed a mesh colour alpha of 1 by accident.

## 3. Modelling (`MeshBuilder`, `Paint`, `MeshCache`)

`MeshBuilder` builds flat-shaded low-poly meshes through a transform stack (`Push/Translate/Rotate/Scale/Pop`).
State for the next primitives: `Color`, `Emission`, `Wind` (+ `WindGradient`, `WindY0/WindY1`), `Bone`, `Jitter`
(per-face brightness variation — 0.04–0.1 gives a hand-painted feel), `AOStrength/AOHeight` (darker near the ground).
Primitives: `Box`, `BoxOn`, `TaperedBox`, `Roof` (gable, ridge along X), `Lathe` (profile of (radius, y) bottom→top,
smooth or faceted, ring colours), `Cylinder`, `Cone`, `Sphere` (ellipsoid), `Capsule`, `Blob` (jittered icosphere:
rocks, bushes, canopies, clouds), `Segment` (tapered limb between two points), `Disc`, `Plane`, `Torus`, `Extrude`
(vertical prism of a convex polygon), `Blade` (double-sided leaf/grass/flame), low-level `Triangle`, `TriangleFacing`,
`Quad(a,b,c,d, outward)`, `QuadTwoSided`, and `Append(other builder)`. `ToMesh(name)` → mesh with smoothed normals
in the tangents (for the outline); `ToMesh(name, bindposes)` → rigidly skinned (each vertex 100% on its `Bone`).
`Paint.Hex/Shade/Mix/Hsv` for palettes. Build each distinct mesh once and share it: `MeshCache.Get(key, build)`.
`MeshCache.GroundQuad` (unit quad on the ground plane), `MeshCache.AddShadow(parent, rx, ry)` (blob shadow).

**Style guide.** Chunky readable silhouettes; 6–12 sided round things; faceted (flat) shading for nature and props,
smooth for faces/bodies where it reads better; saturated-but-soft palettes (warm creams, sage and moss greens, dusty
blues, terracotta roofs, honey-gold lantern light); per-face `Jitter` 0.04–0.08; `AOStrength` ~0.3 on props; ink
outline on characters, creatures and props (not on terrain, grass, decals, particles). Keep triangle counts sane:
character ≤ 3k tris, big building ≤ 3k, tree ≤ 1.5k, small prop ≤ 400, grass tuft ≤ 60.

## 4. Ownership (who edits what) — never edit another builder's files

| Builder | Owns (may create new files under these folders) | Must keep working |
|---|---|---|
| **Characters** | `Scripts/Game/Units/**` | the `UnitView` public API (§5) |
| **World** | `Scripts/Game/World/**` except `World/Props/**`; `Rendering/Lighting2D.cs`, `World/LightDriver.cs`, `World/SwayManager.cs`, `World/Silhouettes.cs`, `World/AmbientParticles.cs`, `World/DayNight.cs` (rewrite or delete) | the `MapView` / `MapObject` / `DayNight` public API (§6) |
| **Props** | `Scripts/Game/World/Props/**` (implements `partial class PropModels`: `TryBuild`, `TryHas`) | `PropModel` / `PropModels` interface (§7) |
| **Camera & FX** | `Scripts/Game/Rendering/CameraRig.cs`, `Scripts/Game/Fx/**`, `World/PresentationArt.cs`, `World/PresentationHost.cs`, `UI/Hud/NameplatesHud.cs` (projection only) | `CameraRig`, `FxSystem`, previews, `FloatingText` APIs (§8) |
| **Lead (integration)** | `Rendering3D/**`, `Flow/**`, `UI/**` (except above), `Editor/**`, shaders listed in §2, docs | — |

If you need a change outside your files (a foundation fix, a new API on another builder's class), **do not make it**:
work around it in your files and list the request in your final report. Small bugs found in `Rendering3D/**` or the
shaders: describe them precisely in the report (file, line, fix).

`Lighting2D`, `SortingOrders`, `Silhouettes`, `SwayManager` and `LightDriver` are 2D-era code; once nothing of yours
uses them, say so in your report (the lead deletes them at integration). `PresentationArt`'s procedural textures stay
available for FX.

## 5. UnitView (Characters)

Keep every public member (see `Docs/PresentationAPI.md` §3), now in 3D:

* `static UnitView Create(string spriteKey, float height, Color ringColor)` — spriteKey selects the model recipe:
  `char_<class>` (player classes), `comp_<name>` (companions: kael, lys, seren, rook, pip, torvan, aldric, morwen —
  see `Resources/Data/content/companions.json` for their classes/looks), `npc_*` (elder, innkeeper, merchant, smith,
  villager_a/b, child, guard, trainer, spirit), creatures `cr_*` (wolf, wolf_blighted, boar, spider, mossling,
  mossling_shaman, bandit, bandit_archer, bandit_hexer, bandit_chief, hollow_wisp, hollow_spirit, hollow_treant,
  hollow_warden (≈4.5 m boss), training_dummy), pets `pet_*` (wolf, cat, boar, bear, owl), demons `demon_*` (imp,
  voidwalker, succubus, felhunter, infernal), totems `totem_*` (earth, fire, water, air). `height` > 0 scales the
  model to that height (creature `size`), ≤ 0 = the model's natural height. Unknown keys get a sensible generic model.
* Anchors: `FeetPosition`/`Position` (Vector2 ground), **`CenterPosition`, `HeadPosition`, `NameplatePosition`
  are `Vector3` world points** (z = −height; follow the animated body; lowered when lying).
* Picking: **`static UnitView PickScreen(Vector2 screen, bool includeDead = false)`** and
  **`bool HitTestScreen(Vector2 screen, out float depth)`** (screen pixels, bottom-left origin; depth = distance from
  the camera, smaller = in front). Use the projected model bounds (a slightly inset screen rect of the body is fine).
  `Bounds` (Rect) may be removed.
* Movement: `Teleport`, `MoveAlong(path, speed, onArrive)` (both overloads), `StopMoving`, `IsMoving`,
  `RemainingPathLength()`, `Knockback`, **`FaceTowards(Vector2)` turns smoothly to any direction**, `SetFacing(±1)`
  (= face +X / −X), `Facing` (±1: the sign of the facing's x). Turning is smooth (yaw slerp), never a snap.
* **Walk cycle** (the user explicitly complained the old walking looked bad): proper procedural gait — legs swing
  with knee bend and foot lift, arms counter-swing, hips/shoulders counter-rotate, a two-bump-per-stride vertical bob,
  slight forward lean scaling with speed, cadence and stride matched to the actual movement speed (feet must not
  skate), smooth blend in/out between idle and walk (no pops), run posture above ~3.5 m/s. Quadrupeds use a
  diagonal-pair gait; spiders an alternating tetrapod; wisps/spirits float and bob; the treant lumbers; the warden is
  slow and heavy. Footstep dust puffs are welcome (FxSystem.Puff at the feet, low alpha, not every step).
* One-shots (return durations; keep the timing constants `AttackHitTime` 0.22, `ShootReleaseTime` 0.2,
  `CastReleaseTime` 0.45): `PlayAttack(towards)` (weapon swing fitting the weapon), `PlayShoot()`/`PlayShoot(towards)`
  (bow draw & release / throw), `PlayCast(color)` (arms raise, hand glow), `PlayHit()` (recoil + white flash),
  `PlayDodge()`, `PlayDeath()` (falls, then dissolves with `_Fade`; caller Disposes afterwards), `PlayDowned()` (lies
  down, stays), `PlayRevive()`.
* States: `SetSelected`, `SetHovered` (rim + gold outline), `SetTargetable(Color?)` (pulsing ground ring + outline
  colour), `SetActiveTurn` (ground ring + ripple), `SetStealthed` (dither to ~45 %), `SetTint`, `SetPolymorphed`
  (a sheep model), `SetCasting(color, progress)` / `StopCasting()` (ground rune + hand glow + a small point light),
  `SetVisible`, `SetSprite(key)` (rebuild the model), `RingColor`, `DisplayName`, `Tag`, `UnitId`, `FadesOccluders`,
  `Floating`, `IsDead`, `IsDowned`, …, `Dispose()`.
* Ground rings / selection discs lie on the ground plane (z ≈ −0.01) — the procedural ring textures in
  `PresentationArt` already exist; draw them with `Sprites/Default` or `Materials3D.AdditiveFor`.
* Blob shadow under every unit. One update loop for all units (`UnitViewSystem`), no per-unit `Update`.
* Performance: ≤ 2 draw calls per unit body (e.g. one rigidly-skinned `SkinnedMeshRenderer` + outline material, or
  a few `MeshRenderer` parts sharing the LowPoly material); meshes cached per recipe; no per-frame allocations.

## 6. MapView (World)

Keep the public API (`Docs/PresentationAPI.md` §1–2): `MapView.Build(def, flagTest)`, `Current`, `Def`, `DayNight`,
`Bounds`, `Objects/Chests/Transitions/Regions/Interactables`, `Find`, **`PickScreen(Vector2 screen, bool
includeRegions = false)`** (front-most prop/chest model under the screen point, then the transition/region ground
rect under the cursor), `Pick(Vector2 world, bool includeRegions)` (ground-point version, may stay), `SetHighlighted`
(rim + gold outline, pulsing), `ClearHighlights`, `SetChestOpen` (lid opens with a little bounce + sparkles),
`SetVisible`, `SetLocked`, `RefreshFlags`, `SetPropArt`, `SetLanternLit` / `SetAllLanternsLit` (glass glows, warm
light fades in, sparkles when animated), `LanternCount`, `RectOf`, `Dispose()`. `MapObject.LabelPosition` is a
**`Vector3`** world point just above the object. Set `CameraRig.Instance.Bounds` (ground rect the camera's look-at
point may roam: about x `−2..W+2`, y `−2..D+2`).

What a map is in 3D (all from `MapDef`, see `Docs/WorldAPI.md` / `DataSchema.md`):
* **Terrain:** flat walkable ground (z = 0) over the map rect plus a margin, painted with the map's ground texture
  (`Resources/Art/Ground/<def.ground>`, `Materials3D.Textured(tex, 1/groundTile)` planar) and vertex-colour
  variation; beyond the rect the land rolls into hills (behind, y > D), gentle banks in front (y < 0) and continues
  left/right so a camera yawed ±40° never sees the world's edge. Soft colour transition, no seams.
* **Backdrop** from `def.layers` (art keys tell you what they were): `bg_mountains_far` → distant low-poly mountain
  ridges (fogged, bluish), `bg_hills_far` → rolling hills, `bg_village_far` → distant cottages/roofs on the hills,
  `bg_forest_far/near` → tree lines, `bg_shrine_cliffs` → cliffs climbing with dark lanterns, `bg_clouds` → soft
  cloud blobs drifting (`scrollSpeed`). Everything sits in real 3D space behind/around the play strip (y > D) so it
  parallaxes naturally with the camera.
* **Sky:** a sky dome (`Lanternvale/Sky`) with the map's `skyTop → skyBottom` gradient, tinted by time of day; sun /
  moon discs and stars at night (additive). Camera `backgroundColor` = horizon colour as a fallback.
* **Props:** `PropModels.Create(art, seed)` (Props builder), placed at `pos` (`World3D.At`), `scale`, `flip` (mirror
  X), `tint` (`Look.Tint`), soft blob shadow, `light` → `SceneLighting` point light at the anchor/offset (`flicker`,
  `nightOnly`), `sway` (wind is in the vertex weights; set `Look.WindScale`). Tall props between the camera and a unit
  with `FadesOccluders` dither-fade (`Look.Fade` ≈ 0.35). **Decals** (`decal_*`: paths, flower beds, blight) are the
  painted PNGs on flat ground quads (`Materials3D.LitTransparent`) just above z = 0. **Foreground** (`fg_*`) → 3D
  ferns/grass/stones/flowers along the front edge (from PropModels).
* **Chests** (closed/open), **transition markers** (a glowing waymarker/arch + chevron at the map edge, warm light;
  locked = dim violet), **regions** (rects only).
* **Day/night:** `DayNight` keeps its maths/API (`Hour`, `NightFactor`, `Phase`, `SetHour`, `WorldHour`,
  `HoursPerSecond`, `Paused`, `HourOf`, `PhaseOf`, `Changed`); it now drives `SceneLighting`: sun direction/colour
  (low warm at dawn/dusk, high soft white by day, cool moonlight at night), sky/ground ambient, fog colour = sky
  horizon, `NightGlow` up at night, lantern/lamp lights on at night (`nightOnly`). Map `ambientColor ×
  ambientIntensity` multiplies. Fixed-time maps (shrine: dusk) keep their mood.
* **Ambient particles** (`AmbientDef`: fireflies, pollen, leaves, mist, rain, embers) as camera-near 3D billboards
  (`Additive` for glowing ones, `Sprites/Default` for leaves/mist), pooled, around the camera's look-at point.
* Performance: static props without scripts; meshes cached per (art, seed bucket); one `LateUpdate` for fades,
  flicker, markers and particles; keep ≤ ~600 draw calls on the biggest map.

## 7. Prop library (Props)

`partial class PropModels` in `World/Props/`: implement `static partial void TryBuild(string artKey, int seed, ref
PropModel model)` and `static partial void TryHas(string artKey, ref bool has)` (the interface and a placeholder are
in `PropModels.cs` — keep that file's public surface; you may edit it). A `PropModel` has `Root` (upright at the ground
pivot, `rotation = World3D.Upright`; children/meshes in Y-up model space), `Height`, `Radius`, `LocalBounds`,
`Renderers`, `LightAnchors` (model space), `Lid` (chests), `SetLit` (lanterns, lamp posts, campfire, windows), `Sways`.

Keys to model (map counts in brackets): props — cottage_a/b/c, inn, smithy (forge glow), shop_stall (awning, goods),
windmill (sails turn slowly), well, fence, lamp_post, spirit_lantern & spirit_lantern_dark (same model, `SetLit`;
stone tōrō-style lantern with paper/glass glowing honey-gold), tree_oak/pine/birch/dead, **tree_great** (Old Kusu, a
huge camphor tree ~25 m with a shimenawa rope and paper charms — the village landmark), bush_a/b, rock_large/small,
stump, log, cart, barrel, crate, hay, signpost, noticeboard, bench, campfire (`SetLit`: flames + embers), tent,
mushrooms, ruin_pillar, ruin_arch, shrine_gate (torii-like), spirit_statue (fox/kitsune), blight_crystal (violet
emissive crystals), banner (cloth sways), bridge; chests `prop_chest` (+ `_open`); foreground fg_ferns, fg_grass_a/b,
fg_stones_a/b, fg_flowers_a/b. Sizes: use believable real-world sizes (person = 1.75 m; cottage ridge ≈ 6 m, inn ≈ 8 m,
windmill ≈ 11 m, oak ≈ 8 m, pine ≈ 11 m), but the **solid footprint must fit the prop's nav collider**
(`collider.w` × `collider.h` ellipse in the map JSON, e.g. cottages 5.0 × 2.2, inn 7.0 × 2.6, well 1.8 × 0.9, trees
trunk-sized) so units never walk through walls: buildings may extend backwards (+local Z, away from the camera) past
the collider but their front face must not stick out in front of it, and roofs/eaves/canopies may overhang. Wind
weights on foliage/grass/cloth (`Wind` + `WindGradient`), `Sways = true` for those. `seed` should vary colours (roof
tints, flower colours, foliage hue) and small shape details.

## 8. Camera & FX (Camera & FX)

* **CameraRig** (keep: `Instance`, `Create()`, `Cam`, `Follow`, `Bounds` (ground rect), `Focus(Vector2?)`,
  `ResetPan`, `SnapToTarget`, `Shake`, `Zoom`, `AllowManualPan`, `MouseWorld` (ground point under the mouse),
  `ScreenToWorld(Vector2 screen) → Vector2` (ray ∩ ground plane z = 0), `WorldToScreen(Vector3)`,
  `WorldToGui(Vector3)`, `IsInFront`, `ScreenRay`, `LookAtPoint`, `ToUnity/ToVec2`). **BG3-style perspective rig**:
  look-at point on the ground, distance/zoom with the wheel (≈ 7–30 m), pitch rising with distance (≈ 40° close →
  60° far), yaw rotation (middle-mouse drag and/or keys that are free — check `GameInput`/hotkey usage first) limited
  to about ±45° around the default view (looking into +Y), WASD/arrow pan relative to the view yaw if those keys are
  free, smooth follow, edge-aware bounds clamp, shake. Near ≈ 0.3, far ≈ 600 (backdrop mountains), FOV ≈ 35–40°,
  `allowMSAA`, `QualitySettings.antiAliasing = 4` at runtime if lower. Up = −Z (`transform.rotation =
  Quaternion.LookRotation(forward, World3D.Up)`).
* **FxSystem** (keep every public call; signatures now: `Projectile(Vector3 from, Vector3 to, …)`,
  `Beam(Vector3, Vector3, …)`, `MoveBeam(int, Vector3, Vector3)`, `Impact(Vector3, …)`, `Slash(Vector3 pos, Vector3
  dir)`, `Sparkles(Vector3, …)`, `Puff(Vector3, …)`; ground effects keep `Vector2`: `Burst`, `HealSparkles`,
  `AuraPulse`, `GroundRing`). Rewrite internals in 3D: camera-facing billboards for airborne particles (sparks, smoke,
  flashes), real 3D projectiles (arrow mesh flying an arc and pointing along its velocity; glowing bolts with trails),
  beams as camera-facing ribbons between 3D points, ground effects flat on the ground plane (z ≈ −0.02), short-lived
  `SceneLighting` lights on impacts/bursts/bolts. An airborne effect given a ground point (z = 0) should rise from
  the ground (e.g. dust, charge puffs) — do not "fix" heights at call sites.
* **Previews** (`ShowCircle/Cone/Line/Path/MoveRange/Hide/HideAll/IsShowing`) on the ground plane, readable on the
  painted terrain (soft fill + crisp bright outline; draw above the ground, under units).
* **FloatingText** is already converted to `Vector3` (rises along −Z). Keep it; polish if needed (skip points behind the
  camera, scale with distance a little).
* **NameplatesHud** projection: `ToGui(rig, Vector3)` already; hide plates whose point is behind the camera.

## 9. Verification (everyone)

* `LV_BUILD_DIR=/tmp/claude-<you> Tools/check.sh unity` must print `compile OK` twice (editor + player) — it compiles
  every script against Unity reference assemblies. (In a git worktree, copy the reference DLLs first if the cache is
  missing: `mkdir -p Tools/.cache && cp -r /home/user/house-builder/Lanternvale/Tools/.cache/unityrefs Tools/.cache/`.)
* There is **no Unity editor here**: nothing can be run or looked at. Be rigorous instead: re-read your geometry
  code for winding/orientation (use the `outward` hints), units (metres), the −Z-up convention, and per-frame
  allocations. Mentally walk through one frame of each animation.
* Shader code cannot be compiled here either: keep any new shader minimal and conventional (CG, UnityCG.cginc,
  `#pragma target 3.0`), and double-check syntax.
* Report: what you built, the public API you kept/changed, requests for other owners, and known gaps.
