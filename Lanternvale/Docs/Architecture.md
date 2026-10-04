# Lanternvale — Architecture overview

Three assemblies, strictly layered. Everything that decides *what happens* is pure C# in `Lanternvale.Core`
(no UnityEngine, testable headlessly). The Unity layer only *presents* it and turns input into commands.

```
                        Assets/Lanternvale/Resources
              ┌───────────────────────────┬────────────────────────────────┐
              │ Data/**.json              │ Art/** PNG + art_manifest.json │ Fonts/
              │ (classes, content, maps)  │ (placeholders from artgen)     │
              └─────────────┬─────────────┴───────────────┬────────────────┘
                            │ TextAssets                  │ Resources.Load by key
  ══════════════════════════╪═════════════════════════════╪═══════════════════════════════════════════
  Lanternvale.Core          ▼  (asmdef noEngineReferences — pure C# 9, no UnityEngine)
  ┌──────────────────────────────────────────────────────────────────────────────────────────────┐
  │ Util (Rng, Vec2, Log)   Json (reader/writer, reflection mapper)                              │
  │ Data:  GameDatabase.Load ──▶ Defs/Enums ──▶ DataValidator            (Docs/DataSchema.md)    │
  │                                                                                              │
  │ Rules (Lanternvale.Rules)                     World (Lanternvale.World)                      │
  │  Unit, UnitStats, UnitFactory                  NavGrid / ReachMap (A*, 0.5 m cells)          │
  │  AbilityRules, Targeting, Tooltip              FlagStore, QuestLog, MapRuntime               │
  │  Battle (turns, hit table, auras, threat)      DialogueRunner, SkillChecks (d20)             │
  │  AI, Items/Loot, Progression, Specials         WorldState (+ save DTOs)                      │
  │        │ emits CombatEvent stream                        ▲                                   │
  │        ▼                                                 │                                   │
  │ Session (Lanternvale.Session) ── GameSession: the ONE API the Unity layer talks to ──────────│
  │  party/bags/gold · maps & movement · dialogue · battles · vendors/trainers/talents · clock   │
  │  commands return null | reason          raises SessionEvent          SaveGame()/LoadGame()   │
  │  (Docs/CoreAPI.md, WorldAPI.md, SessionAPI.md)                             JSON string       │
  └──────────────────────────────────────────────────────────────────────────────────────────────┘
           ▲ commands                        │ SessionEvent / CombatEvent         │ JSON
  ═════════╪═════════════════════════════════╪════════════════════════════════════╪═══════════════
  Lanternvale.Game (Unity, references Core)  ▼                                    ▼
  ┌──────────────────────────────────────────────────────────────────────────────────────────────┐
  │ Boot/GameRoot ── loads Data; creates GameInput driver, CameraRig, GameFlow, UiRoot, GameAudio│
  │                                                                                              │
  │ Flow/GameFlow (hub, Docs/GameFlow.md)                     Flow/SaveFiles                     │
  │   owns GameSession, map view, UnitView registry           persistentDataPath/saves/*.json    │
  │   Exploration (click-to-move, interact, openers)                                             │
  │   FieldPresenter (out-of-combat CombatEvents, rest)                                          │
  │   CombatController (Docs/CombatFlow.md): beats → visuals, targeting, AI pacing               │
  │   relays every SessionEvent ──▶ SessionEventRaised      CombatEventPresented ──▶ HUD         │
  │          │ drives                                                                            │
  │          ▼                                                                                   │
  │ Presentation (Docs/PresentationAPI.md)                                                       │
  │   World/MapView (diorama, parallax, props, DayNight)   Units/UnitView   Fx/FxSystem          │
  │   Fx/FloatingText   Audio/Sfx + Music (synthesized)   Art/ArtLibrary (manifest + fallbacks)  │
  │   Rendering/Lighting2D (URP Light2D via reflection, else overlay)   Rendering/CameraRig      │
  │                                                                                              │
  │ UI (IMGUI only; Docs/UI_HUD.md, UI_Panels.md)                                                │
  │   UiRoot hosts every IUiScreen (found by reflection), Ui toolkit, UiText tooltips            │
  │   Hud/* (frames, action bar, turn order, toasts…)   Panels/* (windows, dialogue, menus)      │
  │   talk only to GameFlow.Instance / .Session / .Combat; clicks are queued, run next Update    │
  │                                                                                              │
  │ Input/GameInput ── the only input path: legacy Input Manager, or IMGUI events when only the  │
  │   new Input System is enabled; UI rects block world clicks (BlockRectGui)                    │
  └──────────────────────────────────────────────────────────────────────────────────────────────┘
  Lanternvale.Editor (references Core + Game): art importer, Create Game Scene, Validate Data, URP 2D setup

  Tools/check.sh (no Unity needed)
    data / core ─▶ net8.0 console app = Scripts/Core/** + Tools/harness/CoreTests/*.cs → validate data, run [Test]s
    unity       ─▶ net472 builds of Scripts/** against UnityEngine/UnityEditor reference DLLs (editor + player)
```

## One frame, one click

```
 mouse/keys ─▶ GameInputDriver.Update (execution order −1000)
                  │
                  ▼
 GameFlow.Update (−50) ── Session.Tick(dt) ── Exploration / Combat.Update(unscaled dt)
                  │                                   │ e.g. a click on an enemy in combat
                  │                                   ▼
                  │                     Battle.UseAbility(...)  ── resolves instantly in the rules
                  │                                   │ CombatEvents appended
                  │                                   ▼
                  │                     CombatController queue → beats → UnitView/Fx/FloatingText/Sfx
                  │                                   │ NotifyCombatEventPresented
                  ▼                                   ▼
 UiRoot.Update (100): screens Tick, hotkeys      HUD reads the "presented" state (HudPresented)
 UiRoot.OnGUI: HUD layers + panels draw; button clicks → Hud.Post / PanelKit.Do → run next Update
```

## Rules of the road

* **Core never references UnityEngine.** `Lanternvale.Core.asmdef` has `noEngineReferences: true`, and
  `Tools/check.sh core` compiles it as a plain .NET 8 program.
* **The Unity layer never re-implements a rule.** Hit chances, costs, ranges, reasons, level scaling and
  tooltips all come from `Battle` / `AbilityRules` / `Tooltip` / `GameSession`.
* **One way in, one way out.** Commands go to `GameSession` (or `Battle` through the `CombatController`).
  State changes come back as `SessionEvent`s and `CombatEvent`s, which the flow presents in order.
* **Determinism.** All randomness goes through `Lanternvale.Util.Rng`. `new GameSession(db, seed)` with the
  same seed and the same inputs gives the same game (seed 0 = time based). The tests use fixed seeds, so
  their battles and playthroughs are reproducible.
* **Data-driven.** Classes, abilities, talents, items, creatures, NPCs, dialogue, quests and maps are JSON
  (`Docs/DataSchema.md`). Class behaviour that data cannot express is a named C# *special*
  (`Core/Rules/Specials/*`), and the validator checks that every special named in the data is implemented.
* **Version-safe Unity.** C# 9, Unity 2021.3 → Unity 6 APIs only: no `Find*ObjectOfType`, no
  TextMeshPro/UI Toolkit/new Input System types, no custom shaders, no URP compile-time types. URP is
  reached by reflection (`Lighting2D`, the editor's URP setup). The Game asmdef also defines
  `LANTERNVALE_URP` when the URP package is installed, but no code depends on it today.
