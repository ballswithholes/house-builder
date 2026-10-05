# Lanternvale

A party-based, turn-based CRPG in the spirit of **Baldur's Gate 3**, played on hand-painted **2.5D
orthographic dioramas** in a cosy, Ghibli-inspired watercolour style, with **Final Fantasy X–inspired**
heroes. The eight classes — Warrior, Hunter, Paladin, Mage, Priest, Rogue, Warlock, Shaman — play like
their **World of Warcraft Classic (1.12)** counterparts: the same abilities and ranks, the same three talent
trees each, the same weapon/armour rules, roles and resources (rage, energy + combo points, mana and the
five-second rule, soul shards, hunter pets and the dead zone, shaman totems, threat).

The spirit-lanterns that protect the valley are going dark one by one. A creeping *Hollow* — a grey blight
that empties spirits — spreads from the old shrine, and your party has to rekindle the lanterns.

> **Status — please read [Known limitations](#known-limitations).** This is a complete vertical slice, but
> nobody has run it inside the Unity editor yet. The Unity scripts are compile-checked against Unity reference
> assemblies, and the rules, world and save code is tested headlessly (223 tests, including full
> playthroughs of the slice for every class). The art is placeholder art.

---

## Features

### Eight WoW Classic classes

Each class is data (`Resources/Data/classes/<class>.json`) driven by one rules engine. It has its abilities
with WoW Classic ranks and learn levels, its talent trees, its basic attack and its armour and weapon
proficiencies, all following 1.12.

| Class | Roles | What makes it play like WoW Classic |
|---|---|---|
| **Warrior** | Tank, melee DPS | **Rage** from dealing and taking white damage (the 1.12 rage formula), no passive regen, decays out of combat. Battle / Defensive / Berserker **stances** gate abilities and change threat; Tactical Mastery keeps rage on a stance swap. Charge, Taunt, Heroic Strike / Cleave **replace the next swing**. Mail, then Plate at 40; dual wield at 20. |
| **Rogue** | Melee DPS | **Energy** (+20 per 2 s) and **combo points** stored on one target; finishers scale with points. 1.0 s GCD. **Stealth** with openers (Cheap Shot, Ambush…) — opening a fight from stealth gives a surprise round. Pick Lock opens locked chests. Leather; dual wield at 10. |
| **Hunter** | Ranged DPS | **Auto Shot** with the 8-yard **dead zone**, aspects, stings and **traps**. **Pets**: tame a beast (wolf, cat, boar, bear, owl, spider). Pets use Focus, take their own turn right after the hunter and are remembered between fights. Feign Death. Leather, then Mail at 40; dual wield at 20. |
| **Mage** | Ranged DPS, support | Mana with the **five-second rule**. Fire / Frost / Arcane, Polymorph, Blink, Counterspell, Ice Block, Evocation, conjured food and water. Cloth; staves, swords, daggers and wands. |
| **Priest** | Healer, ranged DPS, support | Downrankable heals, Power Word: Shield, Shadowform, Mind Control (a channelled spell: a humanoid enemy fights for you until the priest acts), Lightwell. Cloth; maces, staves, daggers and wands. |
| **Paladin** | Tank, healer, melee DPS, support | Seals and Judgement, Blessings, one Aura at a time, Righteous Fury for threat, Lay on Hands, Divine Intervention. Mail, then Plate at 40; shields. |
| **Warlock** | Ranged DPS, support | **Soul shards** are real bag items: Drain Soul and Shadowburn kills create them. Summoning any demon but the Imp, Soul Fire, Shadowburn and every Healthstone, Soulstone, Spellstone and Firestone use one. **Demons** (Imp, Voidwalker, Succubus, Felhunter; Inferno's Infernal for 5 minutes) are controllable pets. Curses, Life Tap, Enslave Demon. Cloth; staves, swords, daggers and wands. |
| **Shaman** | Healer, melee DPS, ranged DPS, support | **Totems**: one per element (Earth, Fire, Water, Air) acting at the end of the shaman's turn. Shocks share a cooldown. Weapon imbues (Windfury…), Ghost Wolf, Reincarnation (needs an Ankh). Leather, then Mail at 40; shields. |

* **Abilities and ranks.** 515 ability definitions (class, pet and creature) with WoW Classic numbers.
  Trainers in the village teach new abilities and ranks for gold. **Downranking** works: pin a lower rank
  in the Spellbook and the action bar casts that rank, with its own cost, magnitude and cast time.
* **Talents.** 24 trees with 385 talents: 7 tiers (a new tier opens every 5 points spent in the tree)
  and prerequisite arrows. You get 1 point per level from level 10, so 51 at level 60. "Recommended build" auto-allocates. Respec at a
  trainer for 1g, then 5g, 10g…
* **Equipment.** 17 slots, item qualities from Poor to Legendary and random "of the Bear / Eagle /
  Monkey…" suffix greens. Class armour and weapon proficiencies are enforced. There are 290 items in
  total.
* **Threat.** Every enemy keeps a WoW threat table. It switches targets only when someone passes 110%
  (melee) or 130% (ranged) of its current target's threat. Stance and class threat modifiers apply, healing
  draws threat, and taunts work.

### Combat — "six-second turns"

WoW is real-time; Lanternvale slices it into **rounds of 6 seconds** (`Docs/Design.md` §2).

* Initiative is `d20 + (Agi − 20) / 10`. Pets act right after their owner.
* Each turn a unit has **6.0 s of Time** for abilities and a separate **movement budget** (9 m).
  Abilities cost `max(cast time, GCD)`. Off-GCD abilities (stances, Bloodrage, Presence of Mind…) cost only
  their cast time.
* If an ability doesn't fit the Time left:
  * an **instant** ability resolves now, and the overflow becomes **time debt** on the next turn;
  * a **cast** becomes **pending**. It resolves at the start of the caster's next turn, so the cast is
    **telegraphed** and can be **interrupted** (Kick, Pummel, Counterspell, Earth Shock…).
* **Auto attacks run on WoW swing timers** in parallel and swing at the end of the turn. Dual wield, the
  Auto Shot dead zone, wand Shoot and next-swing abilities all work as in WoW.
* Resources regenerate per turn: energy +60, mana from Spirit with the five-second rule (MP5 always),
  pet Focus +36.
* Durations and cooldowns are in seconds: 6 s pass per round. Out of combat the clock runs in real time.
* Party members at 0 HP are **Downed** (BG3), and an adjacent ally can **Help** them up. Soulstone and
  Reincarnation offer a self-resurrection.
* Previews come from the engine itself. Hovering shows the path and its cost, the AoE shape, and hit and
  crit chances taken from the same attack table the engine rolls with. Each preview also shows the
  resource cost and how much Time the ability takes.
* Hold Shift (or toggle **»**) to fast-forward animations. The training dummy fight can be left at any time.

### Companions

Eight recruitable companions, one per class:

| Companion | Class | Where |
|---|---|---|
| Kael, the Red Wanderer | Warrior | Lanternvale |
| Aldric, Knight-Errant of Sunmere | Paladin | Lanternvale |
| Pip, Cogwright Scavenger | Rogue | Lanternvale |
| Rook, Islander Ringball Captain | Hunter | Whisperwood |
| Seren, Summoner on Pilgrimage | Priest | Whisperwood |
| Lys, the Belted Sorceress | Mage | Whisperwood |
| Torvan, Hornkin Oathkeeper | Shaman | Old Lantern Shrine |
| Morwen, Scholar of Demons | Warlock | Old Lantern Shrine |

You recruit companions through dialogue. The party holds **4** (you + 3); the rest wait at camp, and you
swap them in the Party window (**K**). By default you control every party member in combat (BG3-style).
Each companion has an **AUTO** toggle that hands it to the companion AI. Companions have likes, dislikes
and **approval**. Settings let them learn new ranks and spend talent points on their preferred build
automatically.

### Dialogue and skill checks

Dialogues branch, and some choices are tagged by class (`[PALADIN]`). Some choices also depend on gold,
items, level, approval or who is in the party. **d20 skill checks**: the party member with the best
modifier rolls `d20 + floor((stat − 20) / 10) + 2 if their class is proficient` against the DC. A natural 20
always succeeds and a natural 1 always fails. Check choices preview their odds ("Persuasion DC 12 · Seren +3 ·
65% chance"), and the d20 tumbles on screen. Locked chests use the same kind of check out in the world:
Sleight of Hand, or a rogue's Pick Lock with a bonus for level.

### World, maps and quests

* Three diorama maps:
  * **Lanternvale**, the village hub: elder, inn, merchants, a smith and an armourer, and four trainers
    who each teach two classes.
  * **Whisperwood**, the forest: wolves, boars, Mosslings, bandits, spiders, owls and blighted spirits.
  * **The Old Lantern Shrine**, the dungeon, with the boss *The Hollow Warden*.
* **6 quests.** One main quest, *The Lanterns Go Dark*, and five side quests. Many have several routes:
  fight, intimidate, pay, persuade, or use a class or companion option.
* Most encounters are visible on the map (a few are ambushes). Walk into one, or click an enemy to start
  the fight. Enemies scale to the party's level. Stealthed units are only noticed within 3 m. An ability
  used on an enemy outside combat becomes an **opener**, and opening from stealth gives a surprise round.
* Out of combat you get chests (some locked), signs, map transitions, vendors with buyback, an inn, and
  camp on the wild maps (long rest). The day/night clock runs at 1 game hour per real minute.
* Saves: quicksave (F5/F9), autosave (after the opening, on travel and after each victory), and 9 slots.
  Each save is one JSON file in `Application.persistentDataPath/saves/`.

### Art, lighting and presentation

* An orthographic camera over the XY plane, with parallax background layers, a tiled ground and
  y-sorted props. Foreground and tall props fade when a unit is behind them.
* **Soft 2D lights.** With URP and the 2D Renderer, the game uses real `Light2D` components: a global
  light driven by the day/night cycle, and point lights on lanterns, lamps and fires, which can flicker or
  turn on only at night. Fireflies, halos and spells add soft glow sprites. URP is reached through
  reflection only, so nothing depends on URP at compile time. Without URP (built-in pipeline), a tinted
  night overlay and the glow sprites fake the same mood.
* Clean silhouettes: hover and hit outlines, ground rings and pooled effects (bolts, beams, bursts,
  sparkles). Floating combat text.
* **All 292 art keys have painted placeholder art**, made by a deterministic Python generator
  (`Tools/artgen`). Missing keys fall back to procedural sprites at runtime. See
  [Replacing the art](#replacing-the-art).
* The UI is **IMGUI** throughout: a WoW-like HUD (party frames, action bar, target frame, turn order,
  combat log, quest tracker) plus parchment windows. It needs no TextMeshPro or UI Toolkit. The fonts
  are Nunito and Fredoka (SIL OFL), and the interface size is adjustable (75–150%).

### Audio

There are no audio files: everything is **synthesized at runtime**.

* Sound effects: Karplus-Strong harp plucks, music-box bells and filtered-noise impacts, synthesized once
  at boot on a worker thread.
* Music: a **generative score** synthesized on the audio thread. Each mood has its own scale and
  instruments:
  * village: F major waltz
  * forest: D dorian
  * shrine: A minor
  * combat: D minor ostinato
  * menu: C lydian

  Moods crossfade into each other.

---

## Getting started

You need **Unity 6 or Unity 2022.3 LTS**. The scripts avoid APIs newer than 2021.3, so 2021.3 LTS should
also work but is untried. No packages are required besides the optional URP.

### Option A (recommended): Universal 2D template

With this template, 2D lights work out of the box.

1. Unity Hub → *New project* → **Universal 2D** template (called **2D (URP)** in older editors) → create.
   If you pick a built-in-pipeline template instead (plain *2D* or *3D*), also run **Lanternvale ▸ Setup ▸
   Configure URP 2D Renderer** after step 3. Otherwise the game falls back to unlit sprites with faked
   lighting.
2. Close Unity. Copy `Lanternvale/Assets/Lanternvale` from this repository into the new project's
   `Assets/` folder, so you have `Assets/Lanternvale/Scripts`, `Assets/Lanternvale/Resources`, and so on.
3. Open the project. Choose **Lanternvale ▸ Create Game Scene**, which creates
   `Assets/Lanternvale/Scenes/Lanternvale.unity` and adds it to Build Settings. Then press **Play**.
   Pressing Play in *any* scene also works, because the game boots itself (`GameRoot.AutoBoot`).

### Option B: open this folder directly

The repository ships only `Assets/`. It has no `ProjectSettings/` (so no pinned editor version) and no
`Packages/manifest.json` (so no URP). Unity Hub only recognises a folder as a project when it has a
`ProjectSettings/` folder, so:

1. Create an empty `Lanternvale/ProjectSettings` folder. Then use Unity Hub → *Add* → *Add project from
   disk* → pick `Lanternvale/`, and choose an installed editor version for it. Or skip Hub and launch that
   editor with `-projectPath <path to>/Lanternvale`.
2. Unity generates default settings and a default package manifest. That gives a built-in-pipeline project
   in 3D mode, without URP, which is fine: the art importer (`LanternvaleArtImporter`) forces sprite import
   settings, and the game boots in any scene.
3. Run **Lanternvale ▸ Setup ▸ Configure URP 2D Renderer**. The first run only offers to install the URP
   package (this needs network access). Wait for Unity to finish importing, then **run it a second time**.
   The second run creates and assigns a URP asset with the 2D renderer, so sprites react to 2D lights.
   It also adds URP's 2D sprite shaders to *Always Included Shaders* (a build step does the same before
   every player build, or use **Lanternvale ▸ Setup ▸ Include 2D Sprite Shaders in Builds**), because the
   game creates its sprites at runtime and a build would otherwise strip the lit sprite shader.
4. Choose **Lanternvale ▸ Create Game Scene**, then press **Play**.

Unity writes `ProjectSettings/`, `Packages/`, `Library/` and other folders into the project folder.
`Library/`, `Temp/`, `Logs/` and `UserSettings/` are git-ignored. `ProjectSettings/` and `Packages/` are not,
so leave them uncommitted unless you mean to pin them. If you skip step 3 (no URP), the game still runs and
fakes the lighting with glows and a night overlay.

### Active Input Handling

Any setting works, and you don't need to change anything. *Project Settings ▸ Player ▸ Active Input
Handling* chooses which input path the game uses:

* **Input Manager (Old)** or **Both**: `GameInput` polls the legacy `Input` class.
* **Input System Package (New)**: the legacy class is unavailable, so `GameInput` reads keyboard and mouse
  from IMGUI events, which Unity delivers whatever the backend. The game does not reference the Input
  System package.

All game code reads input through `GameInput` (`Scripts/Game/Input/GameInput.cs`). Click into the Game view
so it has keyboard focus.

### Other editor menus

* **Lanternvale ▸ Validate Data** checks all JSON and logs the problems.
* **Lanternvale ▸ Reimport Art** forces the art import settings again.
* **Lanternvale ▸ Open Save Folder** opens the save folder.

---

## Controls

These are the keys and clicks actually bound in code. Press **F1** in game for the same cheat-sheet.

**Everywhere in a game**

| Input | Action |
|---|---|
| Mouse wheel | zoom (over the action bar: page the bar) |
| Arrow keys, middle-drag | pan the camera |
| 1–0, -, = | action bar slots of the visible page (in combat and out of it) |
| F5 / F9 | quick save / quick load |
| F1 | help |
| Esc | close the last window, otherwise open the pause menu |

**Windows** (also reachable from the menu bar at the top right; for the combat log, click the compact log
at the bottom left)

| Key | Window |
|---|---|
| C | character sheet |
| I or B | bags |
| P | spellbook |
| N | talents |
| J | journal |
| K | party & camp (swap companions) |
| M | map |
| L | combat log |

**Exploration**

| Input | Action |
|---|---|
| Left click ground | walk there (hold the button to keep walking towards the cursor) |
| Left click person / chest / sign / exit | talk / open (pick the lock) / read / travel |
| Left click enemy | start the fight (with an armed opener: use it first) |
| Left click party portrait | select that member (becomes the leader) |
| Action bar ability on an enemy | arms an **opener**; the next enemy click uses it |
| Right click, or Esc | disarm the opener; right click also stops the party |
| Tab / Shift+Tab | next / previous party member |

**Combat**

| Input | Action |
|---|---|
| Left click ground | move (the path shows the cost) |
| Left click enemy | smart attack: melee Attack, Auto Shot or wand Shoot, walking into reach first |
| Left click downed ally | Help them up |
| 1–0, -, = or click a bar slot | pick an ability, then click a target (or a party frame / turn-order portrait) |
| Right click / Esc | cancel targeting |
| Space / Enter | end the turn (auto attacks swing now) |
| Hold Shift | fast-forward the animations |

**Action bar and windows (mouse)**

| Input | Action |
|---|---|
| Right click a bar slot | cast on yourself |
| Shift+right click a bar slot | reset the bar order |
| Drag a bar icon onto another slot | swap the two slots |
| Right click / Shift+click a spellbook entry | choose a rank (downranking) |
| Right click an item in the bags | context menu: use, equip on…, sell, destroy |

**Dialogue, loot and menus**

| Input | Action |
|---|---|
| 1–9 (top row or keypad) | choose a dialogue line |
| Space / Enter | reveal the text, continue, or skip the dice roll |
| Space / E / Esc | loot: take all |
| Enter | quest reward: claim the picked reward |
| Up/Down or W/S, Enter/Space | main menu |
| Left/Right/Up/Down or WASD, Enter | character creation: change class, begin |

There is no gamepad support and no key rebinding.

---

## Project layout

```
Assets/Lanternvale/
  Scripts/Core      pure C# 9, asmdef Lanternvale.Core with noEngineReferences: no UnityEngine anywhere
    Util, Json        Rng, Vec2, logging; the JSON reader/writer and data mapper
    Data              definitions (Defs.cs, Enums.cs), GameDatabase, DataValidator
    Rules             the engine: units & stats, abilities, Battle, AI, items, progression, class specials
    World             navigation grid & pathfinding, flags, dialogue runner & skill checks, quests, map state
    Session           GameSession (the single API the game talks to), save/load, starting gear
  Scripts/Game      Unity layer (asmdef Lanternvale.Game)
    Boot              GameRoot: loads data, creates input, camera, flow, UI, audio
    Art, Rendering    ArtLibrary (manifest + procedural fallbacks), Lighting2D (URP by reflection), CameraRig
    Input             GameInput: the only input path (legacy Input Manager or IMGUI events)
    World, Units, Fx  MapView dioramas, day/night, ambient particles, UnitView, effects, floating text
    Audio             synthesized SFX and the generative music engine
    Flow              GameFlow (hub), Exploration, FieldPresenter, CombatController, SaveFiles
    UI                Ui toolkit, UiRoot host, UiText tooltips, Hud/* (HUD layers), Panels/* (windows)
  Scripts/Editor    art import settings, Create Game Scene, Validate Data, URP 2D setup
  Resources/Data    all game data as JSON (classes/*.json, content/*.json)
  Resources/Art     292 PNGs + art_manifest.json (placeholders — replace with final art, same names)
  Resources/Fonts   Nunito & Fredoka (SIL OFL)
  link.xml          keeps the reflection-mapped assemblies from being stripped in IL2CPP builds
Docs/               design, data schema, API and flow docs, art keys and art prompts (index below)
Tools/check.sh      compile, validate and test everything without Unity
Tools/harness/      CoreTests: the headless test runner and all [Test]/[Sim] methods
Tools/artgen/       the placeholder-art generator (Python 3 + numpy + Pillow)
Tools/datagen/      the scripts that first generated the class/content JSON (read its README before running)
```

`Docs/Architecture.md` has a diagram of how the layers fit together.

---

## Validate and test without Unity

`Tools/check.sh` builds and runs everything with the .NET SDK. Run it from `Lanternvale/`:

| Command | What it does |
|---|---|
| `Tools/check.sh data` | compiles only Json/Data/Util and validates the JSON. This still works while Rules code is being edited. |
| `Tools/check.sh core` | compiles the whole pure-C# core, validates the data (must print `Data validation: OK`) and runs every `[Test]` |
| `Tools/check.sh core --sim` | the same, plus the `[Sim]` balance reports and long simulations (win rates over many seeds, boss replay) |
| `Tools/check.sh unity` | compile-checks every Unity script against Unity reference assemblies, in an **editor** configuration (with `UnityEditor`, legacy-input define) and a **player** configuration (no editor scripts, IMGUI-input path) |
| `Tools/check.sh all` | `core` + `unity` (the default) |

Harness arguments (after `data`/`core`/`all`):

* `--filter <test name>` runs only the matching tests, for example `--filter World` or `--filter Session`.
* `--grep <text>` only prints data problems that contain the text.
* `--sim` also runs the `[Sim]` methods.
* `--no-tests`, `--quiet`.
* `--allow-problems` doesn't fail on data problems. Don't use it for a real check.

The current state is `Data validation: OK` and **`Tests: 223/223 passed`**, and both Unity configurations
report `compile OK`. The tests cover formulas and the hit table, every class ability used through the real
engine (smoke tests), AI battles, items, progression, navigation, dialogue, quests, saves, and **full
playthroughs** of the slice for every class through the `GameSession` API.

**Requirements:**

* bash on Linux, macOS, or **WSL** on Windows.
* The **.NET 8 SDK**, with `dotnet` on the PATH. Under WSL that means the Linux SDK installed inside the
  distribution. The harness builds `net8.0` and `net472` projects.
* For `unity` mode, also `curl`, `unzip` and network access to nuget.org on the first run. That run
  downloads the Unity reference assemblies (NuGet package `Unity3D.SDK` 2021.1.14.1, cached in
  `Tools/.cache/unityrefs`) and restores `Microsoft.NETFramework.ReferenceAssemblies.net472`.

Build output goes to `Tools/.cache/build`. Set `LV_BUILD_DIR` to use a different (private) directory, for
example when several builds run at once.

**Line endings and Windows shells:**

* The script needs LF line endings. `.gitattributes` pins `*.sh` and `*.py` to LF in every checkout, even
  with Git for Windows' default `core.autocrlf=true`.
* A working copy checked out before that file existed keeps a CRLF `check.sh`. Running it from WSL then
  fails at once with `/usr/bin/env: 'bash\r': No such file or directory` (or
  `set: pipefail: invalid option name` via `bash Tools/check.sh`). To fix it, delete `Tools/check.sh` and
  run `git checkout -- Tools/check.sh`, or clone inside the WSL filesystem instead.
* Git Bash / MSYS2 / Cygwin are **not** supported. The script writes POSIX paths (`/c/Users/…`) into the
  generated `.csproj` files, which the native Windows `dotnet` cannot resolve. `core` then fails to build,
  and `unity` would compile nothing yet report OK.

---

## Working on the game

* **Data** is plain JSON — see `Docs/DataSchema.md`. `Scripts/Core/Data/Defs.cs` is the source of truth for
  field names, and unknown keys are errors. Validate with `Tools/check.sh data` or **Lanternvale ▸ Validate
  Data**.
* The JSON is the source of truth. The generators in `Tools/datagen/` are partly stale: re-running some of
  them would undo hand fixes. Read `Tools/datagen/README.md` first.
* **Rules** are pure C# and covered by the tests above. The Unity layer never re-implements a rule. It asks
  `GameSession` / `Battle` and presents the events they emit.
* **Developer QA scene.** Add a `DioramaPreview` component to an empty GameObject, or call
  `DioramaPreview.Run("whisperwood")`. It loads a map with every hero and a set of creatures. Debug keys
  play effects and animations, cycle the time of day, music moods and maps, and show the encounters (see
  `Docs/PresentationAPI.md` §7).

## Replacing the art

`Docs/ArtPrompts.md` has a style bible and a production prompt for every asset. `Docs/ArtKeys.md` lists
every key with its size and purpose. `Docs/art_previews/` has contact sheets of the current placeholders.

To replace art, drop finished PNGs over the placeholders in `Resources/Art/**` with the **same file names**.
Nothing else needs to change, because `ArtLibrary` loads by key and the editor importer applies the sprite
settings. Drawn height and pivot live in `art_manifest.json`. The exception is creature, pet, demon and
totem sprites, which are drawn at the `size` of their creature entry (`Docs/ArtPrompts.md` §9 step 3).

Back up your art before re-running `Tools/artgen/generate.py`. It rewrites the manifest and overwrites
whole jobs of PNGs (§9 explains which).

## Documentation

| Doc | Contents |
|---|---|
| `Docs/Architecture.md` | layer diagram, data and event flow |
| `Docs/Design.md` | game design and rules specification (time model, formulas, threat, progression, content) |
| `Docs/DataSchema.md` | how to author classes, abilities, auras, talents, items, creatures, NPCs, dialogue, quests, maps |
| `Docs/CoreAPI.md` | rules engine API (`Lanternvale.Rules`) |
| `Docs/WorldAPI.md` | navigation, flags, dialogue, quests, map state (`Lanternvale.World`) |
| `Docs/SessionAPI.md` | `GameSession`: the one API the Unity layer talks to, events, saves |
| `Docs/GameFlow.md` | Unity game flow: states, exploration input, combat hand-off, saves |
| `Docs/CombatFlow.md` | `CombatController`: battle presentation, targeting, input rules |
| `Docs/PresentationAPI.md` | maps, units, effects, floating text, audio, the QA preview |
| `Docs/UI_HUD.md`, `Docs/UI_Panels.md` | every HUD layer and window, UI conventions |
| `Docs/ArtKeys.md`, `Docs/ArtPrompts.md` | art key catalog and production prompts |

---

## Known limitations

* **Never run inside the Unity editor by its authors.** Every Unity-side script is compile-checked in editor
  and player configurations, but only against the Unity 2021.1 reference assemblies that the harness
  downloads from NuGet (`Unity3D.SDK`). Compatibility with 2022.3 and Unity 6 (URP types, `GraphicsSettings`
  properties, `Light2D` assemblies) was handled by reflection and review, not verified in those editors. The
  rules, world, dialogue, quest and save code is tested headlessly. Expect first-run rough edges in
  everything that only the editor can show: layout and scaling of the IMGUI windows, sorting and lighting
  values, input feel, animation timing and frame rate. The presentation timings in the docs are estimates.
* **Review status.** Besides the tests, the code went through several rounds of independent review
  (reviewer → two skeptical verifiers → fixer). The last round still confirmed a handful of new issues
  (they were fixed), so the code is converging but not proven clean; a first play session in the editor is
  the most valuable next check.
* **Placeholder art.** All 292 images are generated placeholders, painterly but not production quality.
  Real art is meant to replace them (see above).
* **Vertical slice.** There are three maps and six quests, tuned around levels 1–12. Classes, abilities and
  talents go to level 60, and character creation offers veteran starts at 10–60 with level-appropriate
  gear. Enemies scale to the party's level, so the slice plays at any level, but there is no content beyond
  it.
* **Turn-based adaptation.** WoW's real-time rules are compressed into 6-second turns, so some numbers and
  rules are adapted. The rage gain from dealing damage is scaled ×1.75. Hunter traps can be laid in combat,
  and Feign Death does not drop combat. Resurrection works in combat, and party members are downed rather
  than killed. These choices are documented in `Docs/Design.md`.
* **Unity project files are not included.** The repository has no `ProjectSettings/` and no `Packages/`, so
  the editor version, URP and input settings are whatever your project has (see Getting started).
* **Input.** Mouse and keyboard only: no gamepad, no rebinding, no touch.
* **Audio.** Everything is synthesized. On WebGL the music is silent, because `OnAudioFilterRead` is
  unsupported there; sound effects still play.
* **Other.** The game is in English only, with no localisation. UI preferences live in `PlayerPrefs`, not in
  the save. The action bar order and pinned ranks are keyed by character name and class, so two saves with
  the same hero share them.
