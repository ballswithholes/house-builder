# Lanternvale — Game Flow (Unity layer)

`GameFlow` (`Scripts/Game/Flow/`) is the hub between the rules/world state (`GameSession`, see `SessionAPI.md`) and the
presentation (`MapView`, `UnitView`, `FxSystem`, `FloatingText`, `Sfx`/`Music`, see `PresentationAPI.md`).
**UI screens talk only to `GameFlow.Instance`, `GameFlow.Instance.Combat` and `GameFlow.Instance.Session`** (plus the
static presentation helpers). The flow owns the session's lifetime, the diorama, every `UnitView`, exploration input,
out-of-combat presentation, saves, and hands battles to the `CombatController`.

| File | Contents |
|---|---|
| `Boot/GameRoot.Flow.cs` | `OnBoot`: adds `GameFlow` to the persistent GameRoot object, `UiRoot.Create()`, `GameAudio.Init()` |
| `Flow/GameFlow.cs` | the contract surface: selection, registry entry points, hover, lifecycle/save entry points |
| `Flow/GameFlow.Lifecycle.cs` | title backdrop, new game / load / main menu / quit, the per-frame loop, pause, autosave |
| `Flow/GameFlow.Events.cs` | reactions to every `SessionEvent` (then relayed to `SessionEventRaised`) |
| `Flow/GameFlow.Views.cs` | `UnitView` registry: party/pets/summons/battle units, map NPCs, encounter enemies, sync, idle life |
| `Flow/GameFlow.Overlay.cs` | fades, story flash, NPC bark bubbles, the lantern-rekindling sequence (IMGUI, `GUI.depth` 8) |
| `Flow/Exploration.cs` | hover, click-to-move, interactions, armed openers, hotkeys |
| `Flow/FieldPresenter.cs` | out-of-combat abilities/items and the presentation of their `CombatEvent`s, long rest |
| `Flow/SaveFiles.cs` | `persistentDataPath/saves/<slot>.json`, atomic writes, cached headers |

---

## 1. States

```
                 boot (GameRoot.Awake → OnBoot)
                              │
                              ▼
   ┌──────────────────────────────────────────────┐   ReturnToMainMenu (from any state)
   │ MAIN MENU  Session == null, HasGame == false   │◀──────────────────────────────────────┐
   │ title backdrop: map "lanternvale" at dusk,     │                                        │
   │ lanterns lit, NPCs idling, slow camera drift,  │                                        │
   │ Music "menu"                                    │                                        │
   └──────────────────────────────────────────────┘                                        │
        │ StartNewGame(options)            │ LoadFromSlot(slot) / QuickLoad                     │
        ▼                                  ▼                                                    │
   ┌──────────┐  DialogueEnded    ┌──────────────┐  walk into an encounter / click an enemy ┌──────────┐
   │ DIALOGUE │ ────────────────▶ │ EXPLORATION  │ ───────────────────────────────────────▶ │  COMBAT  │
   │ (opening)│ ◀──────────────── │              │ ◀─────────────────────────────────────── │          │
   └──────────┘  TalkTo, encounter└──────────────┘   CombatEnded (Victory / Left)            └──────────┘
        │        dialogue              ▲   │                                                     │
        │ StartCombat outcome          │   │ LoadFromSlot (any in-game state, also GameOver)     │ CombatEnded (Defeat)
        └──────────────────────────────┼───┼─────────────────────────────▶ COMBAT                 ▼
                                       │   │                                               ┌──────────┐
                                       └───┴──────────── LoadFromSlot / StartNewGame ──────│ GAMEOVER │
                                                                                           └──────────┘
```

| State | `HasGame` | `Session.Mode` | `GameRoot.Mode` | `Map` | `Combat` | World input |
|---|---|---|---|---|---|---|
| Main menu | false | (no session) | `MainMenu` (UI may set `CharacterCreation`; the flow leaves it) | null (backdrop hidden from the API) | null | none |
| Exploration | true | `Exploration` | `Exploration` | current `MapView` | null | hover, clicks, Tab, F5/F9 |
| Dialogue | true | `Dialogue` | `Dialogue` | ✓ | null | none (dialogue UI) — clock paused, camera frames speaker |
| Combat | true | `Combat` | `Combat` | ✓ | `CombatController` | hover only (the controller owns clicks), F5/F9 |
| Game over | true | `GameOver` | `GameOver` | ✓ (battlefield kept) | null | none — UI shows the game-over screen |

`StartNewGame` and `LoadFromSlot` always create a **fresh `GameSession`** (seed 0) — a failed load leaves the running game
(or the main menu) untouched. Every session is subscribed with `QueueEvents = false`; UI code must use
`GameFlow.SessionEventRaised`, not `TakeEvents()`. Successful starts/loads close every UI panel (`UiRoot.CloseAll()`).

**Pause.** While the `UiPanels.Pause` panel is open in a game, `Time.timeScale` is 0 (views, FX, floating text and the
session clock freeze; IMGUI keeps working) and is restored when it closes. The combat controller never overrides a
time scale of 0.

## 2. Frame loop (`GameFlow.Update`, execution order −50)

1. pause bookkeeping (above);
2. main menu: camera drift (rebuilds the backdrop if it is missing);
3. in a game: `Session.Tick(Time.deltaTime)` (play time always; clock/regen only in exploration), F5/F9, then by mode —
   exploration (party walk + trigger reports, clicks, Tab), dialogue (camera between leader and speaker),
   combat (`Combat.Update(unscaledDeltaTime)`; `Selected` follows the active unit when a player unit's turn begins);
   hover; stealth visuals (5×/s); flag-driven world refresh (1×/s in exploration);
4. `DayNight.WorldHour = Session.GameHour` with `DayNight.Paused = true` (the session is the clock);
5. NPC/enemy idle life, delayed view removals, lantern sequence, overlay fades, pending autosave, `GameRoot.SetMode`.

## 3. Session events → reactions

Every event is handled first, then relayed **unchanged** through `SessionEventRaised` (exceptions in either step are
logged, never rethrown). The flow also raises a few **flow-local `Toast` events** through the same channel ("Quick
saved.", "You can't reach that.", "The lock holds…", load/opener messages) — UI toasts need no other source.

| Event | Reaction |
|---|---|
| `GameStarted` | autosave requested (written once the opening conversation ends) |
| `MapEntered` | dispose views/combat/old map, `MapView.Build(def, Session.Flags.Test)`, opened chests shown open (silent), lanterns lit silently if `LanternsRekindled`, views for `PartyUnits()`, `VisibleNpcs()`, `VisibleEncounters()`, camera follows the leader (`SnapToTarget`), `Music` for the map, fade-in from dark; travel (`Id2` = a transition spawn) requests an autosave |
| `DialogueStarted` / `DialogueEnded` | stop the party, zoom in and keep the camera between the leader and the current speaker (NPC, companion, encounter, or the owner when the hero speaks); both face each other. On end: restore zoom/follow, re-evaluate flags (chests, transitions, NPCs, encounters) |
| `ChestOpened` | `Map.SetChestOpen(id)` (pop + sparkles) + `chest_open` |
| `SpecialOutcome` `RekindleLanterns` | `Amount 0`: `SetAllLanternsLit(true)` silently. `Amount 1`: warm flash, chime, party sparkles, then every dark lantern relights one by one (nearest first, 0.38 s apart, holy impacts) and a final `level_up` chime |
| `PartyChanged` `PetChanged` `CompanionRecruited` `CompanionDismissed` | view sync (out of combat): a recruit steps out of its NPC spot and walks to its party position; a dismissed companion walks back to its NPC spot; pets/demons appear with sparkles / vanish in a puff |
| `LeaderChanged` | camera follows the new leader, `Selected` = leader |
| `LevelUp` | `level_up` + golden sparkles/ring/heal glow + "Level N!" (once per unit per frame) |
| `ItemReceived` / `GoldChanged` | `ui_open` / `coin` |
| `QuestStarted` / `QuestCompleted` | `quest` |
| `SkillCheck` (outside dialogue: locks) | floating "17 vs 15 · Success" over the roller + `buff`/`debuff` |
| `TransitionLocked` | marker dimmed (`SetLocked`), violet pulse, `debuff` |
| `CombatStarted` | see §5 |
| `CombatEnded` | see §5 |
| `GameOver` | movement/hover cleared, music fades out; the map and views stay |
| `Rested` | fade to dark (the lighting keeps the evening until black), the clock jumps, the party wakes with heal sparkles |
| `PartyHealed` | heal sparkles on everyone, downed views stand up |

## 4. Exploration input

Gate: `Session.Mode == Exploration && WorldInputEnabled && !UiRoot.ModalActive && !GameInput.PointerOverUi`, not paused,
not during a full-screen fade. All input goes through `GameInput`.

**Hover** (exploration and combat): `UnitView.Pick(mouse)` first, then `MapView.Pick(mouse)` (exploration only). On
change only: `UnitView.SetHovered`, `MapView.SetHighlighted`. Results:

| `HoveredKind` | `HoveredUnit` | `HoveredLabel` | `HoveredLabelWorld` |
|---|---|---|---|
| `PartyMember` (party, pets, own totems) | the `Unit` | "" | nameplate position |
| `Enemy` — battle unit | the `Unit` | "" | nameplate |
| `Enemy` — exploration encounter view (no rules unit yet) | null | creature name | nameplate |
| `Npc` (map NPC / unrecruited companion) | null | display name | nameplate |
| `Object` | null | "Chest" / "Locked chest" / "Empty chest", "To Whisperwood", "Spirit Lantern", "Signpost"… | `MapObject.LabelPosition` |
| `None` | null | "" | — |

Hovering an NPC with a `bark` shows it as a speech bubble above it (once per 16 s per NPC).

**Clicks**

| Input | Action |
|---|---|
| Left click ground | `Session.PlanPartyMove` → leader and followers walk their paths (3.4 m/s, jogging up to 4.8 m/s on long trips; followers scale their speed to arrive together); a small ground ring marks the destination (red when unreachable). Holding the button keeps re-planning towards the cursor. |
| Left click party member | `Select(u)` → becomes the leader (`SetLeader`), camera follows |
| Left click NPC / companion | walk until within `InteractionRange − 0.35` m → both face each other → `TalkTo(id)` (a bark-only NPC answers with a bubble) |
| Left click chest | walk next to it → `OpenChest`; when `Locked`: a party rogue knowing `rogue_pick_lock` tries `PickLock`, otherwise `TryUnlockChest` (results arrive as `SkillCheck`/`ChestOpened`/`LootOpened`; a failed roll says "The lock holds. You can try again.") |
| Left click transition marker | walk to it (entering the rectangle travels by itself), else `UseTransition` |
| Left click prop with an interact id | walk close → `InspectProp` (its text arrives as a `Toast`) |
| Left click enemy (exploration encounter) | walk until within 9 m → `EngageEncounter(id)` — the party strikes first, enemies are surprised when the leader is stealthed (encounter dialogue skipped) |
| Left click enemy with an **armed opener** | walk until the caster is in the opener's range of that enemy's spot (the encounter is kept from triggering by itself during the final approach) → `EngageEncounter(id, caster, ability, enemyIndex)` |
| Right click | disarm an opener, else stop the party and cancel the pending interaction |
| Tab / Shift+Tab | cycle `Selected` (and the leader) through `Session.Party` |
| Esc with an armed opener | disarms it (the pause menu does not open that frame) |
| F5 / F9 | `QuickSave` (toast "Quick saved." or the reason) / `QuickLoad` (toast on error); any mode while a game runs |

While walking, positions are reported with `Session.UpdatePartyPositions(leader, others)` every 0.2 m and on arrival;
`Stop` (dialogue, combat, travel, locked transition) halts everyone. Before planning a new move, view positions are
copied into the units (the session lags by at most 0.2 m). Footsteps play every 0.34 s while the leader walks.

## 5. Combat hand-off

**`CombatStarted`** (raised by the session *before* `Battle.Begin`):
1. stop the party, clear hover/highlights/armed opener, restore the dialogue zoom;
2. the exploration views of `Session.BattleEncounter` are re-bound to the new enemy units (same creature id, nearest);
   remaining units get `EnsureView` (hidden ambushes appear in a puff);
3. every view walks (4.5 m/s) to its unit's battle position; out-of-combat summons that did not join are removed;
4. `Music.Play("combat")`, then `Combat = new CombatController(this, battle)`. If the constructor throws, the flow logs it
   and auto-resolves the fight next frame so the game cannot soft-lock.

**During combat** the controller animates events, owns clicks/targeting and calls `EnsureView`/`ViewOf`/`RemoveView`/
`Select`; the flow keeps hover (`HoveredUnit` etc.), `Selected` (follows each player turn), F5/F9 and the clock.
Field `CombatEvent`s are ignored while a battle is presented.

**`CombatEnded`** (raised inside `FinishBattle`/`LeaveCombat`, i.e. usually inside `Combat.Update`): `Combat.Dispose()`,
`Combat = null`, then
* Defeat: everything stays (game-over screen over the battlefield);
* otherwise: dead enemies finish fading and are removed (1.4 s), living ones (training dummy, a fight left early) walk
  back and become encounter views again, downed allies stand up, party/NPC/encounter views are re-synced (dead pets
  gone, totems still in the field kept), the party walks to its session positions, map music returns, the camera
  follows the leader, and a **victory requests an autosave** (written next frame — after the loot window opened, so
  the save contains it).

## 6. Field actions (out of combat)

* `UseAbilityOutOfCombat(caster, abilityId, target)` → `Session.UseAbility` in the field context. Self/ally abilities
  default to the caster, pet abilities to its pet. **Enemy-target abilities arm an opener** instead (`PendingOpener`,
  enemies pulse red, a toast explains): the next click on an enemy starts the fight with it; right click/Esc/any other
  click disarms (`CancelOpener()`). Returns null or a reason ("You are in combat.", the rules' reason…).
* `UseItemOutOfCombat(user, item, target)` → `CannotUseItemReason` then `Session.UseItem` (food/drink, potions, scrolls).
* `TryRest()` → `Session.LongRest()` (restArea maps); the `Rested` event plays the fade (also for the innkeeper's Rest).
* Presentation of field `CombatEvent`s: casts glow (`PlayCast` + `cast_start`), direct heals (+number, sparkles, `heal`;
  periodic food/HoT ticks stay quiet), damage, misses, buffs/debuffs from an action just taken (ring + name +
  `buff`/`debuff`; area auras re-applied while walking are not shown), summons (view + puff/sparkles), despawns,
  deaths, revives, teleports (Blink), conjured items ("+2 Conjured Water"), resource gains (Life Tap, Evocation).

## 7. Saves

`Application.persistentDataPath/saves/<slot>.json`; slot names are sanitised (letters, digits, `-`, `_`, lower case).
Conventional slots: `auto` (autosave), `quick` (F5), `slot1`…`slot9`.

| Member | Notes |
|---|---|
| `SaveToSlot(slot)` | `Session.TrySaveGame` → atomic write (temp file + `File.Replace`, delete+move fallback). Returns null or the reason ("Cannot save during combat.", "Cannot save during a conversation.", I/O errors) |
| `LoadFromSlot(slot)` | works from the main menu, in game and after a game over; null or the error ("There is no save in slot 'Quicksave'.", "This save was made by a newer version…"). On success toasts "Loaded quicksave." |
| `QuickSave()` / `QuickLoad()` | slot `quick` |
| `ListSaves()` | newest first; each `SaveSlotInfo` has `Header` (null + `Error` when unreadable/newer). Headers are cached by file time/size and the folder listing for 1 s, so calling it every frame is fine |
| `LatestSave()` / `HasAnySave` | newest readable save (for "Continue") |
| `DeleteSave(slot)` | null or the error |
| `LastError` | reason of the last failed start/load/save ("" after a success) |

Autosaves (`auto`): after the opening conversation of a new game, after travelling to another map, after each victory —
whenever saving is allowed (they wait for a running conversation to end; failures are only logged).

## 8. Views, selection, names

* `ViewOf(unit)` / `UnitOf(view)` / `EnsureView(unit)` / `RemoveView(unit)` — the registry for rules units (party, pets,
  totems, summons, battle units). `EnsureView` picks the art (`Unit.Sprite`, companion/class art, creature art),
  height (`CreatureDef.size` for creatures, manifest height for characters), ring colour (class colour; enemies red).
* Map NPCs (incl. unrecruited companions) and exploration encounter enemies are views **without** a rules unit:
  `UnitOf` returns null for them; they are reported through `HoveredKind`/`HoveredLabel`.
* `Select(u)` / `Selected`: exploration → also `SetLeader`; combat → follows the active player unit at each turn start
  (the UI may select another member in between to inspect it). The selected view shows its ring.
* `NameOf(u)`: companion names via `Session.NpcName`, else `Unit.Name`, creature or class name.
* `PartyUnits`: `Session.PartyUnits()` cached per frame and after every session event — read it freely, but do not
  keep the list across frames.

## 9. Overlay, camera, audio

* `GameFlow.OnGUI` draws at `GUI.depth` 8 — above floating combat text (10), below the regular UI (0): map fade-ins,
  rest fades, the lantern flash and NPC bark bubbles (`Ui.InkPanelSoft`). Plain `GUI` on Repaint only.
* Camera: follows the leader's view; dialogue focuses between speakers (zoom ≤ 5.3, restored after); the combat
  controller takes over focus in battle; the title uses a slow eased pan (140 s period) with manual pan disabled.
* Music: `menu` on the title, `Music.MoodForMap(map)` on maps, `combat` in battle, fade out on game over.
* NPCs with `NpcDef.wanders` stroll within 2.2 m of their spot; others glance around and turn towards the party when
  it comes within 3.6 m; beasts of visible encounters prowl within 1.4 m.

## 10. Checklist for UI screens

* Subscribe to `GameFlow.Instance.SessionEventRaised` (it exists before any screen is constructed) for toasts, windows
  (`VendorOpened`, `TrainerOpened`, `LootOpened`, `QuestRewardChoice` …), banners (`MapEntered.Text`,
  `RegionEntered.Text`, `SpecialOutcome.Text`) and the game-over screen (`GameOver`).
* Main menu: visible while `!GameFlow.HasGame`; call `StartNewGame`, `LoadFromSlot`, `LatestSave`, `ListSaves`,
  `HasAnySave`, `QuitGame`. Check `LastError` after `StartNewGame`.
* Hotbar out of combat: `UseAbilityOutOfCombat(Selected, id)` — enemy-target abilities arm `PendingOpener` (highlight
  that button while `PendingOpener == id`); in combat use `Combat.BeginAbility(id)`.
* Nameplates/labels: `HoveredUnit` / `HoveredLabel` at `CameraRig.Instance.WorldToGui(HoveredLabelWorld) / Ui.Scale`,
  coloured by `HoveredKind`.
* Set `WorldInputEnabled = false` while dragging items over the world; panels must call `Ui.Panel`/`Ui.Btn`/`Ui.Block`.

## 11. Public members added beyond the contract

* `enum HoverKind { None, PartyMember, Npc, Enemy, Object }` and `GameFlow.HoveredKind`
* `GameFlow.LastError`
* `GameFlow.DeleteSave(string slot)`
* `GameFlow.PendingOpener`, `GameFlow.PendingOpenerCaster`, `GameFlow.CancelOpener()`
