# Lanternvale — Presentation API (world, units, FX, audio)

Unity layer under `Assets/Lanternvale/Scripts/Game/{World,Units,Fx,Audio,Rendering,Rendering3D}`, namespace
`Lanternvale.Game`. Presentation only: nothing here contains rules logic.

> **3D.** The presentation is full 3D (stylized low-poly, procedural models, own shaders and lighting). The
> binding contract — conventions, shaders, `SceneLighting`, `MeshBuilder`, and the 3D form of every API below — is
> **[`ThreeD.md`](ThreeD.md)**; where this file's 2D-era wording differs, ThreeD.md wins. The API semantics below
> still hold, with these changes:
> * Coordinates: the ground is the XY plane (as before) and **up is −Z**; `World3D.At(ground, height)`.
>   `UnitView.CenterPosition/HeadPosition/NameplatePosition` and `MapObject.LabelPosition` are `Vector3`.
> * Picking is screen-space: `UnitView.PickScreen(screen)` / `HitTestScreen(screen, out depth)`,
>   `MapView.PickScreen(screen, includeRegions)`. `UnitView.Bounds` is now the ground footprint.
> * `UnitView.SetFacing(±1)` faces screen-right / screen-left for the current camera yaw (`CameraRig.Yaw` ± 125°, i.e.
>   `UnitFacing.Bias` 35° towards the camera for a 3/4 view; re-applied when the camera turns, see
>   `UnitFacing.SideYaw`); `FaceTowards` turns to any direction. Static models (totems, the dummy) are set down facing
>   the camera (`UnitFacing.StaticYaw`) and ignore facing requests; idle bipeds turn their heads towards the camera
>   (`UnitAnimInput.ViewYaw`). Bow users have string and arrow bones (`BB.StringA/StringB/ArrowR`, `BB.Count` 31); the
>   blob shadow's parameters come from `UnitShadow.Blob`.
> * Occluding props no longer fade as a whole: they open soft round cut-outs around hidden units, the hovered object,
>   the cursor's ground point and the camera's focus (`MapView.OccluderCutOuts`, default true; ThreeD.md §6). `UnitView.SetVariant(int)` / `Variant` give generic villagers and children a stable look per NPC.
> * `FxSystem` airborne effects take `Vector3` (`Projectile`, `Beam`, `MoveBeam`, `Impact`, `Slash`, `Sparkles`,
>   `Puff`); ground effects keep `Vector2`. There are no sorting-order constants any more (render queues layer the
>   ground overlays). `FloatingText` takes `Vector3`.
> * `CameraRig` is a perspective rig (Zoom keeps its units: half the view height at the look-at point); see
>   ThreeD.md §8 and the controls in the README.
> * `Lighting2D`, `Silhouettes`, `SwayManager`, `LightDriver` and the QA `DioramaPreview` are gone; lights are
>   `SceneLighting` point lights, wind is in the shaders.

| File | Contents |
|---|---|
| `World/MapView.cs` | `MapView` (builds/destroys a diorama), `MapObject`, `MapObjectKind` |
| `World/DayNight.cs` | `DayNight` time-of-day → light/sky/overlay maths |
| `World/AmbientParticles.cs` | fireflies, leaves, pollen, mist, rain (+splashes), embers |
| `World/SwayManager.cs` | one manager for every swaying transform (wind) |
| `World/LightDriver.cs` | `AnimatedLight`, `Light2DSetter` (allocation-free Light2D animation) |
| `World/PresentationArt.cs` | procedural sprites (glow, rings, cone, arrow, bolt, slash, sheep…), unlit material, helpers |
| `World/Silhouettes.cs` | white fill/outline silhouettes of any sprite (hover outlines, hit flashes), `TextureReadback` |
| `World/PresentationHost.cs` | hidden persistent GameObject hosting the managers; `Cam`, `ViewRect()` |
| `World/DioramaPreview.cs` | developer QA tool (maps, units, FX, lighting, audio) |
| `Units/UnitView.cs` | `UnitView` (any character/creature) + `UnitViewSystem` (single update loop) |
| `Fx/FxSystem.cs`, `Fx/FxPreviews.cs` | pooled effects + persistent targeting previews |
| `Fx/FloatingText.cs` | world-anchored combat text (IMGUI) |
| `Audio/GameAudio.cs`, `Sfx.cs`, `SfxSynth.cs` | volumes, init, synthesized sound effects |
| `Audio/Music.cs`, `MusicEngine.cs` | generative score (moods + crossfades) |

---

## 1. MapView

```csharp
MapView view = MapView.Build(mapDef, flags.Test);   // flagTest optional: chest requireFlag (visibility), transition requireFlag (locked)
// CameraRig.Instance.Bounds is set automatically to view.Bounds (x 0..width, y -3..depth+layers, capped at depth+11)
view.SetAllLanternsLit(flags.IsSet("lanterns_rekindled"));     // on load: silent switch
Music.PlayForMap(mapDef.music);
...
view.Dispose();                                      // destroys everything, unregisters sway entries, restores scene global lights
```

Static: `MapView.Current`, tunables `ForegroundParallax` (1.15), `ForegroundFadeAlpha` (0.35),
`OccluderFadeAlpha` (0.35; 1 disables occluder handling), `OccluderCutOuts` (true: cut-outs instead of whole-prop fades),
`VerticalParallax` (0.4), `HighlightColor`.

Members:

| Member | Notes |
|---|---|
| `MapDef Def`, `DayNight DayNight`, `Rect Bounds`, `Color HazeColor` | |
| `List<MapObject> Objects, Chests, Transitions, Regions, Interactables` | `Interactables` = props with an `interact` id (signs, lanterns, notice boards…) |
| `MapObject Find(string id)` | by prop `interact` id, chest id, transition id or region id |
| `MapObject Pick(Vector2 world, bool includeRegions = false)` | front-most prop/chest under the point (sprite rect), then transitions (ground rect), then regions |
| `SetHighlighted(string id, bool on)` | prop/chest: warm outline + brighten (pulsing); transition: brighter, larger marker |
| `ClearHighlights()` | |
| `SetChestOpen(string id, bool open = true, bool animate = true)` | swaps to `<art>_open` (or `prop_chest_open`), pop + sparkles. Play `Sfx.Play("chest_open", pos)` yourself |
| `SetVisible(string id, bool)`, `SetLocked(string id, bool)` | hide a chest/prop/marker; locked transitions are dim grey-violet |
| `RefreshFlags(Func<string,bool> test)` | re-evaluates chest/transition `requireFlag` |
| `SetPropArt(string id, string art)` | swap a prop's art (keeps highlight working) |
| `SetLanternLit(string interactId, bool lit, bool animate = true)` | `prop_spirit_lantern` ↔ `_dark`, warm light on/off, halo, sparkle burst when lit |
| `SetAllLanternsLit(bool lit, bool animate = false)` | every spirit lantern on the map |
| `LanternCount`, `RectOf(id)` | |

`MapObject`: `Id, Kind (Prop|Chest|Transition|Region), Position (pivot or centre), Rect (world), LabelPosition
(where UI should draw a label/prompt), Label (transition label / prop text / region text), Prop|Chest|Transition|Region
(the defs), Visible, Opened, Locked, Highlighted, IsLantern, LanternLit`.

What a map contains (all from `MapDef`):
* **Sky** — camera-fixed vertical gradient `skyTop → skyBottom` (also `Camera.backgroundColor`), tinted by time of
  day on cycling maps; stars and a moon fade in at night. Fixed-time maps keep their authored sky.
* **Parallax layers** — `SpriteDrawMode.Tiled` horizontally, re-tiled around the view each frame (a few tiles, not the
  whole map), `parallax` 0 = camera-fixed … 1 = world, bottom at `y`, `height`, `tint`, `scrollSpeed`. Orders
  `Background + i*10`. Layers sink slightly (behind the ground) when the camera looks towards the front.
* **Ground** — tiled `groundTile` metres per tile over the ground rect plus margins (−6 m sides, −7 m front), tinted
  `groundTint`; a ~2 m feathered strip of the same texture dissolves the horizon edge into the layers, plus a soft
  horizon haze band. **Decals** (`decal_*` props) lie flat at `SortingOrders.Decal`.
* **Props** — y-sorted (`ForY(pos.y)`), `scale`, `flip`, `tint`; soft ground shadow if the manifest says `shadow`;
  `sway`; `light` → `Lighting2D.AddPointLight` (offset × scale, mirrored with flip; `flicker`, `nightOnly`);
  spirit lanterns get a breathing halo (and a default warm light when lit and no `light` is authored).
  Props ≥ 2.5 m tall fade to 50% while a unit stands behind them (3D: they open a soft cut-out around it instead,
  ThreeD.md §6).
* **Foreground** — `SortingOrders.Foreground`, parallax 1.15, fade to 35% when a unit's body overlaps them.
* **Chests** (closed/open art), **transition markers** (glowing chevron pointing at the nearest map edge, or a rune
  circle mid-map), **regions** (rects only).
* **Day/night** — URP: one Global Light 2D driven by `DayNight` (any global light already in the scene is disabled
  while the map lives). Unlit projects: a camera-fixed overlay at `SortingOrders.NightOverlay`; glows/fireflies/FX draw
  above it so lanterns "punch through"; foreground sprites get an equivalent multiply tint.
* **Ambient particles** from `AmbientDef`.

## 2. DayNight

```csharp
var dn = view.DayNight;
dn.Hour; dn.NightFactor (0 day … 1 night); dn.Phase  // "dawn" | "day" | "dusk" | "night" → IDialogueContext.TimeOfDay
dn.SetHour(21f);                // cycling maps: moves the world clock; fixed maps: override until rebuilt
DayNight.WorldHour              // shared clock for maps with ambient.dayNightCycle (save/restore it)
DayNight.HoursPerSecond = 1/60f // 1 game hour per real minute (default)
DayNight.Paused = true;         // freeze during dialogue/combat if desired
DayNight.HourOf("dusk") → 18.7; DayNight.PhaseOf(h)
event Action<DayNight> Changed
```
Phases: dawn [5,8) peach/lavender, day [8,17.5) warm white, dusk [17.5,20.5) rose/amber, night deep blue.
Map `ambientColor × ambientIntensity` multiplies the light.
3D additions: `dn.NightGrade` / `DayNight.NightGradeTint` (the `_LV_Grade` night grade MapView pushes),
`DayNight.LampColor(authored, night)` (warm-white/yellow lamps and windows deepen to amber at night; coloured lights
keep their hue) and `DayNight.LampRange(range, night)` (+22 % reach at full night).

## 3. UnitView

```csharp
var u = UnitView.Create("char_mage", 0f, ringColor);   // height ≤ 0 → manifest height; creatures: CreatureDef.size
u.Teleport(pos); u.DisplayName = "Lys"; u.Tag = unitId; u.SetFacing(-1);
u.MoveAlong(navPath.Points, 3.2f, () => {...});         // List<Vector2> or IReadOnlyList<Vec2>; callback after arrival
float t = u.PlayAttack(target.FeetPosition);            // lunge; blow lands at UnitView.AttackHitTime (0.22 s)
u.Dispose();
```

| Group | API |
|---|---|
| Registry | `UnitView.All`, `UnitView.Pick(Vector2 world, bool includeDead = false)` (front-most body under point) |
| Anchors | `FeetPosition`/`Position`, `CenterPosition`, `HeadPosition`, `NameplatePosition`, `Bounds` (picking rect), `Height`, `Facing` |
| Movement | `Teleport`, `MoveAlong(path, speed, onArrive)`, `StopMoving`, `IsMoving`, `RemainingPathLength()`, `Knockback(to, dur=0.35)`, `FaceTowards`, `SetFacing(±1)` (screen-right/left for the current camera yaw) |
| One-shots (return duration s) | `PlayAttack(towards)`, `PlayShoot()`/`PlayShoot(towards)` (release at `ShootReleaseTime` 0.2), `PlayCast(color)` (release at `CastReleaseTime` 0.45), `PlayHit()` (white flash + shake, overlaps others), `PlayDodge()`, `PlayDeath()` (falls, fades out; Dispose afterwards), `PlayDowned()` (lies down, stays), `PlayRevive()` |
| States | `SetSelected`, `SetHovered` (outline + brighten), `SetTargetable(Color? c)` (pulsing ring + outline colour; null clears), `SetActiveTurn` (ring + ripple), `SetStealthed` (40% alpha), `SetTint(Color)` (white clears), `SetPolymorphed(bool)` (sheep), `SetCasting(color, progress 0..1)` / `StopCasting()` (chest glow + ground rune), `SetVisible`, `SetSprite(key)`, `SetVariant(int)` (look variation of `npc_villager_a/b` and `npc_child`, 16 each; other keys ignore it; kept across `SetSprite`) |
| Data | `DisplayName`, `Tag`, `UnitId`, `RingColor`, `FadesOccluders` (default true), `Floating` (auto for `*wisp*`), `IsDead`, `IsDowned`, `IsSelected`, `IsHovered`, `IsActiveTurn`, `IsPolymorphed`, `Variant` |

Ring priority: targetable > active turn > selected > hovered. Art is assumed to face **right** (+x); `MapNpcDef.flip`
→ `SetFacing(-1)`. `onArrive` is not called when a move is replaced by another `MoveAlong`/`Teleport`/`StopMoving`.
Callbacks run after all units were ticked that frame (safe to destroy units inside them).

## 4. FxSystem (static API)

Effects are white/greyscale sprites tinted with `Ui.SchoolColor(school)`, pooled, simulated in one `Update`.
Airborne effects use `FxSystem.AirOrder` (above units; above the night overlay in unlit mode), ground effects
`FxSystem.GroundOrder`.

| Call | Notes |
|---|---|
| `float Projectile(from, to, School, string artKey = "", float speed = 14, Action onHit = null)` | `""` → `fx_arrow` for Physical, else `fx_bolt`; arrows arc, bolts trail glowing dots and end with an `Impact`; returns flight time; `onHit` runs on arrival |
| `Impact(pos, School)` | flash + ring + school particles (embers, frost shards, leaves, shadow wisps, holy stars, arcane twinkles, physical sparks+dust) |
| `Burst(pos, radius, School)` | AoE explosion: flash, expanding ground ring, fill, many particles, gentle shake (`ShakeOnBurst`) |
| `int Beam(from, to, School, duration)` | channel; `StopBeam(handle)`, `MoveBeam(handle, from, to)` |
| `HealSparkles(feet)` / `HealSparkles(feet, unitHeight)` | rising green-gold plus signs + ground glow |
| `AuraPulse(pos, radius, color)` | expanding ground ring |
| `Slash(pos, dir)` | weapon arc + streaks |
| `GroundRing(pos, radiusMetres, color, duration)` | fixed ring (traps, zones, telegraphs) |
| `Sparkles(pos, color, count = 12)`, `Puff(pos, color, size = 1)` | extras (chest, lantern, blink/vanish) |
| `ClearAll(bool clearPreviews = true)`, `ActiveSprites`, `ParticleDensity` | |

**Targeting previews** (persistent until hidden; re-showing an id reuses its sprites — fine to call every frame):

| Call | Notes |
|---|---|
| `ShowCircle(id, center, radius, color)` | true metric circle on the ground (fill + crisp outline) |
| `ShowCone(id, origin, dir, radius, angleDeg, color)` | full angle in degrees |
| `ShowLine(id, from, to, width, color)` | 9-sliced rounded rect |
| `ShowPath(id, points, color, dashed = true)` | `IList<Vector2>` or `IReadOnlyList<Vec2>`; dashed = marching dots; end marker |
| `ShowMoveRange(id, Func<Vector2,bool> canReach, Rect area, float cellSize, color)` | bakes one small mask texture (cell resolution, blurred, upsampled ×≤4) → one soft sprite with a bright outline. Re-bake only when the reachable set changes |
| `ShowMoveRange(id, ReachMap reach, color)` | convenience for `NavGrid.ReachableWithin` |
| `Hide(id)`, `HideAll()`, `IsShowing(id)` | |

Previews draw at `FxSystem.PreviewOrder` (on the ground, under units).

## 5. FloatingText

```csharp
FloatingText.Spawn(Vector2 worldPos, string text, Color color, float scale = 1f, bool crit = false);
FloatingText.Damage(unit.HeadPosition, 123, crit, school);   // white / school-tinted; crit = yellow, bigger, pops, "123!"
FloatingText.Heal(pos, 80, crit);                            // "+80" green
FloatingText.Miss(pos, "Dodge");                             // Dodge, Parry, Block, Resist, Immune, Absorb, Evade, Miss
FloatingText.Resource(pos, 20, ResourceType.Rage);           // "+20 Rage"
FloatingText.Status(pos, "Stunned", color);  FloatingText.Clear();
```
Rises ~1 m and fades over 1.2 s (crits 1.55 s); texts spawned near the same spot within 0.7 s stack upwards.
Drawn in its own `OnGUI` at `GUI.depth = FloatingText.GuiDepth` (10 — i.e. **under** the regular UI at depth 0);
`SizeScale` for accessibility. Uses `Ui.BoldFont` with a cached style and a 4-way ink outline (no per-frame allocations).

## 6. Audio

`GameAudio` auto-initialises after the first scene load (`RuntimeInitializeOnLoadMethod`): loads volume prefs,
synthesizes SFX on a worker thread (~0.2 s, clips appear a moment after boot; `Sfx.Ready`), creates the music source,
hooks `Ui.Sfx = id => Sfx.Play(id)` and adds an `AudioListener` to the camera if none is active
(CameraRig's camera has none).

```csharp
GameAudio.MasterVolume / MusicVolume / SfxVolume (0..1), GameAudio.Muted; GameAudio.SavePrefs() / LoadPrefs()
Sfx.Play("hit_crit", worldPos);                 // worldPos optional: gentle stereo pan + softer when off-screen
Sfx.Play(id, worldPos, volume, pitch);
Sfx.Impact(School.Fire, pos, crit);             // impact_<school>; Physical → hit_physical / hit_crit
Music.PlayForMap(map.music);                    // music_lanternvale→village, music_whisperwood→forest, music_shrine→shrine
Music.Play("combat"); Music.Play("menu"); Music.Stop(2f); Music.Mood; Music.MoodForMap(mapDef)
```

SFX ids: `ui_click ui_open ui_close hit_physical hit_crit swing bow cast_start impact_fire impact_frost impact_arcane
impact_shadow impact_holy impact_nature heal buff debuff death level_up quest coin footstep_grass chest_open door`
(aliases: click, open, close, hit, crit, footstep, gold/loot, levelup, chest, cast). Same id is rate-limited (30 ms,
max 4 overlapping); hits/footsteps/coins get ±6% pitch variation.

Music moods (`Music.Moods`): **village** (F major 3/4 waltz, harp oom-pah-pah, music-box melody), **forest** (D dorian,
airy pads, flute, sparse harp), **shrine** (A minor, slow, long reverb), **combat** (D minor, harp ostinato, soft
kick/shaker pulse), **menu** (C lydian, dreamy arpeggios). Pentatonic phrase-based melodies (A A′ B rest), pads,
Karplus-Strong harp, music box, bass, ping-pong delay + small reverb; equal-time crossfades (default 3 s).
Synthesized in `OnAudioFilterRead` on the audio thread (≈0.5–1.5% of one core measured on .NET; no allocations).
**WebGL**: `OnAudioFilterRead` is unsupported there, so music is silent (SFX work).

## 7. DioramaPreview (developer QA)

Add `DioramaPreview` to an empty GameObject (set `mapId`, empty = `config.startMap`) or call
`DioramaPreview.Run("whisperwood")`. If no maps are loaded it builds `DioramaPreview.DemoMap()`. Spawns all
`char_*` keys + wolf, mossling, bandit, wisp, hollow spirit and the Hollow Warden; the map's NPCs; optionally the
encounters (E). `DioramaPreview.Active` lets the game flow skip its own boot.

Keys: WASD pan (arrows also pan via CameraRig), wheel zoom, **T** dawn/day/dusk/night, **V** fast cycle, click =
select unit / walk / open chest / toggle lantern, right-click = attack towards cursor, **Tab** next unit,
**1** fire bolt, **2** arrow volley, **3** frost burst (with circle preview), **4** shadow beam, **5** heal + text,
**6** melee (crit/dodge), **7** targeting previews at the cursor, **8** move range, **9** next animation,
**0** next state visual, **F** aura pulse + ground ring, **G** all chests/lanterns, **E** encounters, **L** labels,
**M** music mood, **N** next map, **H** help.

---

## 8. Integration notes (game flow / UI)

* **Picking order**: `GameInput.PointerOverUi` → `UnitView.Pick(mouse)` → `MapView.Pick(mouse)` → ground click.
  Call `SetHovered`/`SetHighlighted` on change only.
* **Labels/nameplates**: the presentation draws no text except floating combat text. Use
  `UnitView.NameplatePosition`/`DisplayName`, `MapObject.LabelPosition`/`Label` with `CameraRig.WorldToGui(p) / Ui.Scale`.
* **NPCs, party, encounters** are not spawned by `MapView` — create `UnitView`s from `MapNpcDef` (respect
  `requireFlag`/`hideFlag`, `flip`), companions, and `EncounterDef.enemies` (`CreatureDef.sprite`, `size`). `hidden`
  encounters: create them when triggered (or `SetVisible(false)` until then).
* **Lanterns**: `view.SetAllLanternsLit(flags.IsSet("lanterns_rekindled"))` after building; per-lantern
  `SetLanternLit(id, true)` for story beats (animated).
* **Combat recipe** (per `CombatEvent`):
  * melee: `attacker.PlayAttack(target.FeetPosition)`; after `AttackHitTime`: `FxSystem.Slash`, `target.PlayHit()`,
    `FloatingText.Damage`, `Sfx.Impact(School.Physical, pos, crit)`; dodge/parry → `target.PlayDodge()` + `FloatingText.Miss`.
  * ranged: `PlayShoot(target)` + `Sfx.Play("bow")`; after `ShootReleaseTime`: `FxSystem.Projectile(..., onHit)`.
  * spells: `PlayCast(Ui.SchoolColor(s))` + `Sfx.Play("cast_start")`; after `CastReleaseTime`: projectile / `Burst` /
    `Beam`; heals → `HealSparkles` + `FloatingText.Heal` + `Sfx.Play("heal")`.
  * pending (telegraphed) casts: `SetCasting(color, progress)` each turn, `StopCasting()` when resolved/interrupted.
  * downed → `PlayDowned()`, help up/resurrect → `PlayRevive()`, enemy death → `PlayDeath()` then `Dispose()` after the
    returned duration. Crits: `CameraRig.Instance.Shake(0.08f, 0.2f)` reads well.
* **Turn UI**: `SetActiveTurn(true)` on the acting unit, `SetTargetable(color)` on valid targets while aiming,
  `ShowMoveRange("move", reach, color)` at turn start, `ShowPath("path", navPath.Points, color)` while hovering.
* **Map change**: `FxSystem.ClearAll()`, `FloatingText.Clear()`, dispose old `UnitView`s, `map.Dispose()`, build the new
  map, then `Music.PlayForMap`.

## 9. Performance notes

* No per-object `Update`s: one loop each in `UnitViewSystem` (units; re-sort only when y moved), `SwayManager`
  (off-screen entries skipped), `AmbientParticles` (≤ ~200 pooled sprites), `FxSystem` (pooled sprites/particles,
  no runtime allocation after warm-up), `MapView.LateUpdate` (parallax, sky, fades, light flicker, markers).
  Static props/decals/ground have no script at all.
* Parallax layers are tiled only across the visible width (a handful of quads per layer); the ground is a single
  tiled renderer.
* Light2D animation uses typed delegates bound once (no reflection/boxing per frame); lights are only touched when
  their value changes, night-only lights are disabled by day.
* Procedural textures are generated once and cached; silhouettes (hover/hit) are read back once per sprite and
  pre-warmed one unit per frame; the move-range texture is reused while its size is unchanged.
* SFX synthesis runs on a worker thread; music synthesis runs on the audio thread with fixed voice pools.

## 10. Art expectations (for the art pipeline)

* `fx_arrow` / `fx_bolt` point **right** (+x); `fx_slash` arcs towards +x; `fx_shadow` is a soft ellipse;
  effects are white/greyscale. Missing `fx_*` keys use procedural fallbacks.
* Chest open art: `<chest art>_open`, falling back to `prop_chest_open`. Spirit lanterns: `prop_spirit_lantern` / `_dark`.
* Prop ground shadows appear only for manifest entries with `"shadow": true`.
* Character/creature art is assumed to face right; pivots at the feet.
