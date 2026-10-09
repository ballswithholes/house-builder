# Lanternvale

A party-based, turn-based CRPG in the spirit of **Baldur's Gate 3**, in **full 3D**: a tilted perspective camera
you can zoom and rotate over a cosy, Ghibli-warm, stylized low-poly world, with **Final Fantasy X–inspired**
heroes. The eight classes — Warrior, Hunter, Paladin, Mage, Priest, Rogue, Warlock, Shaman — play like
their **World of Warcraft Classic (1.12)** counterparts: the same abilities and ranks, the same three talent
trees each, the same weapon/armour rules, roles and resources (rage, energy + combo points, mana and the
five-second rule, soul shards, hunter pets and the dead zone, shaman totems, threat).

The spirit-lanterns that protect the valley are going dark one by one. A creeping *Hollow* — a grey blight
that empties spirits — spreads from the old shrine, and your party has to rekindle the lanterns. Then the expansion,
**The Ember Road**, follows the Hollow's ash west and north through three new zones for levels 12–30, six hidden
dungeons and two ten-player raids to the Ashwyrm Vyrmathra.

> **Status — please read [Known limitations](#known-limitations).** The game runs from level 1 to about 33, but
> nobody has run it inside the Unity editor yet. The Unity scripts are compile-checked against Unity reference
> assemblies, the shaders are compile-checked offline, and the rules, world and save code is tested headlessly
> (469 tests, including full 1–12 playthroughs for every class, every new zone's quest chain, every hidden
> dungeon and both raids won with a party of ten). All 3D models are built in code.

---

## Features

### Eight WoW Classic classes

Each class is data (`Resources/Data/classes/<class>.json`) driven by one rules engine. It has its abilities
with WoW Classic ranks and learn levels, its talent trees, its basic attack and its armour and weapon
proficiencies, all following 1.12.

| Class | Roles | What makes it play like WoW Classic |
|---|---|---|
| **Warrior** | Tank, melee DPS | **Rage** from dealing and taking white damage (the 1.12 rage formula), no passive regen, decays out of combat. Battle / Defensive / Berserker **stances** gate abilities and change threat; Tactical Mastery keeps rage on a stance swap. Charge, Taunt, Heroic Strike / Cleave **replace the next swing** (and hold their rage while queued). The **opening swing** of a fight lands as the warrior engages, so its rage pays for a strike in the same turn. Mail, then Plate at 40; dual wield at 20. |
| **Rogue** | Melee DPS | **Energy** (+20 per 2 s) and **combo points** that stay with the rogue for the whole fight (even when the target falls); finishers scale with points (Eviscerate also with 7% AP per point: build 3-5 for a big hit). 1.0 s GCD. **Stealth** with openers (Cheap Shot, Ambush…) — opening a fight from stealth gives a surprise round. Pick Lock opens locked chests. Leather; **dual wields from level 1** (a dagger in each hand from the start, no trainer step). |
| **Hunter** | Ranged DPS | **Auto Shot** with the 8-yard **dead zone**, aspects, stings and **traps**. **Pets**: tame a beast (wolf, cat, boar, bear, owl, spider). Pets use Focus, take their own turn right after the hunter and are remembered between fights. Feign Death. Leather, then Mail at 40; dual wield at 20. |
| **Mage** | Ranged DPS, support | Mana with the **five-second rule**. Fire / Frost / Arcane, Polymorph, Blink, Counterspell, Ice Block, Evocation, conjured food and water. Cloth; staves, swords, daggers and wands. |
| **Priest** | Healer, ranged DPS, support | Downrankable heals, Power Word: Shield, Shadowform, Mind Control (a channelled spell: a humanoid enemy fights for you until the priest acts), Lightwell. Cloth; maces, staves, daggers and wands. |
| **Paladin** | Tank, healer, melee DPS, support | Seals and Judgement, Blessings, one Aura at a time, Righteous Fury for threat, Lay on Hands, Divine Intervention. Mail, then Plate at 40; shields. |
| **Warlock** | Ranged DPS, support | **Soul shards** are real bag items: Drain Soul and Shadowburn kills create them. Summoning any demon but the Imp, Soul Fire, Shadowburn and every Healthstone, Soulstone, Spellstone and Firestone use one. **Demons** (Imp, Voidwalker, Succubus, Felhunter; Inferno's Infernal for 5 minutes) are controllable pets. Curses, Life Tap, Enslave Demon. Cloth; staves, swords, daggers and wands. |
| **Shaman** | Healer, melee DPS, ranged DPS, support | **Totems**: one per element (Earth, Fire, Water, Air) acting at the end of the shaman's turn. Shocks share a cooldown. Weapon imbues (Windfury…), Ghost Wolf, Reincarnation (needs an Ankh). Leather, then Mail at 40; shields. |

* **Abilities and ranks.** 825 ability definitions (class, pet and creature) with WoW Classic numbers.
  Trainers in the village (and in Brightwater) teach new abilities and ranks for gold. **Downranking** works: pin a lower rank
  in the Spellbook and the action bar casts that rank, with its own cost, magnitude and cast time.
* **Talents.** 24 trees with 385 talents: 7 tiers (a new tier opens every 5 points spent in the tree)
  and prerequisite arrows. You get 1 point per level from level 10, so 51 at level 60. "Recommended build" auto-allocates. Respec at a
  trainer for 1g, then 5g, 10g…
* **Equipment.** 17 slots, item qualities from Poor to Legendary and random "of the Bear / Eagle /
  Monkey…" suffix greens. Class armour and weapon proficiencies are enforced. There are 939 items in
  total, among them **16 class armour sets** (two raid tiers × eight classes; bonuses at 2, 4 and 5 pieces, shown in
  the tooltips and on the character sheet, announced with a chime when one switches on) and **5 legendaries**. Loot
  pools favour gear your party can actually wear. **Off Hand weapons** (20 daggers, swords, fist weapons, maces and
  axes that go only in the off hand and need dual wield) are sold from level 1 by Garrow in Lanternvale and by Dunstan
  in Brightwater, and drop in every band, from hidden-dungeon bosses (Rare) and in both raids (Epic).
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
  Auto Shot dead zone, wand Shoot and next-swing abilities all work as in WoW. Dual wielders hold a One-Hand weapon
  or a real **Off Hand** weapon (daggers, swords, fist weapons… that go only in the off hand) in the off hand; the
  off hand swings at 50% damage.
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

Twelve recruitable companions — one per class in the valley, and four more in the new zones:

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
| Bruna, the Gate of Amberfield | Warrior (tank) | Amberfield Downs, Haybright Farm |
| Ysolde, Shield of Brightwater | Paladin (tank) | Brightwater, by the Heronguard Memorial |
| Liora, the River Lantern | Priest (healer) | Brightwater, by the Chapel of Small Lights |
| Nanami, Voice of the Fen | Shaman (healer) | Mirefen, Lowlantern |

You recruit companions through dialogue. The party holds **5** (you + 4); the rest wait at camp, and you
swap them in the Party window (**K**). Ordinary fights get a little tougher with a fifth member (enemy health ×1.2).
When a fight starts, the party steps into a battle formation: tanks in front, then melee, ranged and healers. By
default you control every party member in combat (BG3-style). Each companion has an **AUTO** toggle that hands it to
the companion AI. Companions have likes, dislikes and **approval**. Settings let them learn new ranks and spend
talent points on their preferred build automatically.

### Dialogue and skill checks

Dialogues branch, and some choices are tagged by class (`[PALADIN]`). Some choices also depend on gold,
items, level, approval or who is in the party. **d20 skill checks**: the party member with the best
modifier rolls `d20 + floor((stat − 20) / 10) + 2 if their class is proficient` against the DC. A natural 20
always succeeds and a natural 1 always fails. Check choices preview their odds ("Persuasion DC 12 · Seren +3 ·
65% chance"), and the d20 tumbles on screen. Locked chests use the same kind of check out in the world:
Sleight of Hand, or a rogue's Pick Lock with a bonus for level. **Perception** (Spirit; Hunters and Rogues are proficient) is rolled
passively in some places to notice hidden passages, caches and traps.

### World, maps and quests

Fifteen diorama maps. The **valley** (levels 1–12) is three maps, now much deeper, each with a northern band
of new places and side quests:

* **Lanternvale**, the village hub: elder, inn, merchants, a smith and an armourer, and four trainers who each teach
  two classes. North of the village: the west road, the millpond, the Lantern Meadow, Pipp Orchard and Hollyhock Farm.
* **Whisperwood**, the forest: wolves, boars, Mosslings, bandits, spiders, owls and blighted spirits; the old forest,
  its brook and fords and the Heron Watch to the north.
* **The Old Lantern Shrine**, with the boss *The Hollow Warden*, and a terraced climb to the High Terrace behind it.

**New zones for levels 12–30** (west from Lanternvale, then on from Brightwater):

| Zone | Levels | What's there |
|---|---|---|
| **Amberfield Downs** | 12–18 | golden downs, farmsteads, windmills and standing stones; Duskmane gnolls, Mudpaw tunnelers, hawks and the Ditchwater Gang; 12 quests |
| **Brightwater** | hub, 12–30 | a river trade town: an inn, shops for levels 12–30, four trainers, the archivist who carries the main story, a ferry; 6 quests |
| **Mirefen** | 18–24 | misty fen of boardwalks and stilt villages: mirelings, crocolisks, bog ghouls, fen wisps and Mother Mire's coven; 11 quests; the portal to the Hollow Heart |
| **Skyreach Peaks** | 24–30 | snow, pines and a mountain hut village: yetis, frost wolves, harpies, ogres, elementals, drakes and the Dragonsworn; 10 quests; the Roost Gate |

**Hidden dungeons.** Every outdoor map but Brightwater hides one — harder monsters (two levels up, elite packs), a
named boss with an Epic chance and a guaranteed Rare, two or three chests, no resting. You find the way in by noticing
it (a passive **Perception** check the first time you walk near it, rolled by your best party member), from an NPC's
hint, or by inspecting something odd; a gold "You discovered a hidden passage" banner announces it, and the cave
mouth, barrow door or stairs appear.

| Dungeon | Levels | Hidden in | Boss |
|---|---|---|---|
| The Root Hollows | 11–13 | Lanternvale, under Old Kusu's roots | The Rootwarden |
| Mossdeep Grotto | 12–14 | Whisperwood, the Mossy Hollow | King Umbercap, the Moss King |
| The Lantern Catacombs | 13–15 | the Shrine's High Terrace | Hazama, the Lantern Lich |
| The Barrow of King Aldwin | 18–20 | Amberfield, the King's Ring | King Aldwin the Unquiet |
| The Drowned Vault | 24–26 | Mirefen, the Sunken Statue | The Tidewitch |
| The Frozen Sanctum | 30–32 | Skyreach, the Frozen Falls | The Rimeheart |

**Raids for up to ten.** Walk into a raid's portal and the **raid picker** opens: tick up to ten characters from
everyone you have recruited (you always lead; it opens with a suggestion — the party you travel with, then companions from
camp, the tanks and healers the raid lacks first — and **Suggest** brings that back), check the role summary ("2 tanks · 2 healers · 6 dps") and **Enter the raid**
(Enter), or **Cancel** (Esc) to stay outside. Companions auto-play in raids by default (a check box in the picker; each
AUTO pill and two combat toggles, **Auto: all companions** and **Auto-battle**, change it). In a raid the party frames
become compact raid frames (two groups of five), the turn strip gets smaller portraits, fast animations start on and
the AI waits half as long between steps. Leaving the raid brings your old party back; a wipe sends everyone home
healed (no lockouts).

| Raid | Levels | Entrance | Bosses |
|---|---|---|---|
| **The Hollow Heart** | 21–23 | Mirefen, the Moon-Gate portal | Thornmaw the Rootbound; the Weeping Twins; Mother Mire; The Hollow Heart |
| **Ashwyrm's Roost** | 31–33 | Skyreach, the Roost Gate (revealed at the end of *Ash on the Wind*) | Frostclaw the Matriarch; the Cinder Drakes; Highlord Varkas; Vyrmathra the Ashwyrm |

Raid bosses bring adds, telegraphed raid-wide casts that cannot be interrupted, and enrages. Each drops class **set
armour** and **raid epics** for the classes in your raid (two of each in a raid of ten), gold and a Rare; the final
bosses can drop **legendaries** (Kindlewood and Solace in the Hollow Heart; Embersong, Dawnstring and Vyrmathra's Last
Scale in the Roost), never one the party already has.

* **62 quests.** Three main-story quests carry on from *The Lanterns Go Dark*: *The Ember Road* (Amberfield to
  Brightwater), *The Drowned Lanterns* (Mirefen) and *Ash on the Wind* (Skyreach), and the raids have their own. Many
  quests have several routes: fight, intimidate, pay, persuade, or use a class or companion option.
* **Quest markers.** NPCs wear WoW-style symbols above their heads: a yellow **!** (a quest you can take now), a yellow
  **?** (ready to hand in), a grey **?** (in progress; hand it in here later) and a grey **!** (a quest a few levels
  ahead); main-story quests are a little bigger, in a gold ring. Hovering an NPC names the quest. The quest tracker and
  the journal say "» Return to <NPC>" when a step can be handed in.
* **The Map panel (M)** shows the same symbols over the NPC dots, gold rings where your quests point, and names that
  never overlap (a crowd of shopkeepers folds into one label such as "Market Square"; hover for the rest). The mouse
  wheel zooms up to 3× around the cursor, dragging pans, and **Whole map** zooms back out. The title shows the zone's
  level band and a "Hidden dungeon" or "Raid (10)" badge.
* Most encounters are visible on the map (a few are ambushes). Walk into one, or click an enemy to start
  the fight. Enemies scale to the party's level, within their map's band. XP (kills and quests, every party member
  gets it in full) is paid at the rate of the content's level — the creature's, the quest's — never above yours
  (`config.xpRateByLevel`, `Docs/DataSchema.md`), so a zone pays the same whenever you arrive. Quest items such as
  pelts or embers only drop while a quest still needs them. Stealthed units are only noticed within 3 m. An ability
  used on an enemy outside combat becomes an **opener**, and opening from stealth gives a surprise round.
* Out of combat you get chests (some locked), signs, things to inspect (some start a conversation or a skill
  check), map transitions, vendors with buyback, inns, and camp on the wild maps (long rest; not in dungeons or
  raids). The day/night clock runs at 1 game hour per real minute.
* Saves: quicksave (F5/F9), autosave (after the opening, on travel and after each victory), and 9 slots.
  Each save is one JSON file in `Application.persistentDataPath/saves/`.

### Art, lighting and presentation

* **Full 3D, stylized low-poly, every model built in code** (`MeshBuilder`): the eight classes, companions,
  villagers, creatures, pets and demons are rigged and animated procedurally (walk cycles matched to speed,
  weapon swings, casts, hits, deaths). About 120 props have their own models: cottages, the inn, smithy, windmill,
  the great camphor tree, spirit lanterns, the torii gate, ruins, trees and grass, and for the expansion farmhouses,
  standing stones and gnoll tents, stilt huts and boardwalks, mountain huts and prayer flags, docks and a town hall,
  cave mouths, crypt furniture, ice, and raid set pieces such as the Hollow Heart's core and the dragon's nest. Each
  zone has its own **biome** (golden highlands, fen, snowy peaks, the ash-black roost): ground, ground cover, hills,
  backdrop scenery and particles (snow, ash, dust, cave drips). Rivers and ponds have fords and bridges, and caves and
  crypts are cut-away indoor dioramas with their own light. The terrain is painted and
  rolls into hills and mountains around the play area. Ink outlines keep edges clean.
* **A BG3-style camera**: perspective view of a point on the ground that follows the party. Zoom from close-ups
  that look out over the land (low pitch, sky and hills visible) to a near top-down tactical view. Rotate
  ±45° (Q/E or middle-drag) and pan (WASD/arrows).
* **Own lighting** (`Resources/Shaders`, `SceneLighting`). The shaders do their own lighting, so the game looks
  the same in any project setup (built-in pipeline; URP projects are switched to it by the build step).
  * Day/night (`DayNight`): a soft wrapped sun that rises and sets, sky and ground ambient, cool moonlight,
    windows and lanterns that glow at night.
  * Up to 16 point lights (lanterns, lamps, campfires, the forge, spells), distance fog, wind sway.
  * Dither fades for props between the camera and the party, and soft blob shadows.
* Effects in 3D: arrows fly real arcs, bolts trail light, beams, bursts and slashes; targeting previews (move
  range, paths, circles, cones) lie on the ground. Floating combat text and nameplates follow units.
* The painted 2D art (`Tools/artgen`, 305 keys) is still used for portraits, icons, the ground textures and
  path decals.
* The UI is **IMGUI** throughout: a WoW-like HUD (party frames, action bar, target frame, turn order,
  combat log, quest tracker) plus parchment windows. It needs no TextMeshPro or UI Toolkit. The fonts
  are Nunito and Fredoka (SIL OFL), and the interface size is adjustable (75–150%).

### Audio

There are no audio files: everything is **synthesized at runtime**.

* Sound effects, synthesized once at boot on a worker thread: soft harp plucks and music-box bells for the UI, and
  **physically informed combat sounds**. Every blow is a weapon layer (blade, blunt, bite, claw, arrow…) plus the
  material it lands on (plate, mail, leather, cloth, flesh, fur, chitin, bone, wood, stone, ice, scale…), louder for
  bigger hits; there are whooshes, bow releases, parries, shield blocks, spell wind-ups and impacts by school, creature
  voices (beasts, gnolls, frogs, spirits, dragons…), body falls with armour clatter, and footsteps on the map's ground.
  Each sound has several variants played round-robin with a little pitch and volume jitter, so repeats never sound
  identical. Loot fanfares by quality, a set-bonus chime, a secret-found cue, boss pulls and a raid warning.
  `Tools/sfxpreview` renders and checks them offline (`Tools/check.sh audio`).
* Music: a **generative score** synthesized on the audio thread. Each mood has its own scale and
  instruments:
  * village: F major waltz
  * forest: D dorian
  * shrine: A minor
  * combat: D minor ostinato
  * menu: C lydian
  * highlands: D mixolydian 6/8 with a piper's flute; town: a bustling G major; fen: a slow E dorian; peaks: an airy
    F lydian with high bells; dungeon: a slow C♯ phrygian with a heartbeat drum; raid: a heroic D harmonic-minor
    gallop with war drums

  Moods crossfade into each other.

---

## Getting started

You need **Unity 6 or Unity 2022.3 LTS**. The scripts avoid APIs newer than 2021.3, so 2021.3 LTS should
also work but is untried. No packages are required: the game renders with its own shaders on the built-in
render pipeline (a project that has URP assigned is switched over by the build step / the setup menu).

### Quickest: one command

From a clone of this repository, with Unity 6 (or 2022.3) installed through Unity Hub:

```bash
# macOS / Linux
Lanternvale/Tools/local/play.sh            # creates a Unity project, builds the game and launches it
Lanternvale/Tools/local/play.sh --editor   # or open it in the Unity editor (then press Play)
```
```powershell
# Windows
powershell -ExecutionPolicy Bypass -File Lanternvale\Tools\local\play.ps1          # build and launch
powershell -ExecutionPolicy Bypass -File Lanternvale\Tools\local\play.ps1 -Editor  # open in the editor
```

The script finds the newest Unity 6 editor that Unity Hub installed (set `UNITY_EDITOR` to override),
creates a project in `Lanternvale/Tools/.cache/local/LanternvaleProject` (an existing one, e.g. from the earlier
Universal 2D template, is reused and switched to the built-in pipeline),
copies `Assets/Lanternvale` into it, builds a player for your computer with your Unity license and starts
it. The first build imports all the art and takes several minutes; later runs are quicker. `--tour` / `-Tour`
starts the game with the autopilot tour (screenshots in `Tools/.cache/local/shots`).

### By hand in the Unity editor

1. Unity Hub → *New project* → any template (**3D (Built-In Render Pipeline)** is the simplest; a URP template
   also works, the setup step below unassigns URP) → create.
2. Close Unity. Copy `Lanternvale/Assets/Lanternvale` from this repository into the new project's
   `Assets/` folder, so you have `Assets/Lanternvale/Scripts`, `Assets/Lanternvale/Resources`, and so on.
3. Open the project. Choose **Lanternvale ▸ Create Game Scene**, which sets up rendering (built-in pipeline,
   4× MSAA), creates `Assets/Lanternvale/Scenes/Lanternvale.unity` and adds it to Build Settings. Then press
   **Play**. Pressing Play in *any* scene also works, because the game boots itself (`GameRoot.AutoBoot`).
   **Lanternvale ▸ Setup ▸ Use the Built-in Render Pipeline (3D)** redoes the rendering setup on its own.

The repository ships only `Assets/` (no `ProjectSettings/`, no `Packages/`). To open the folder itself as a
project, create an empty `Lanternvale/ProjectSettings` folder, add `Lanternvale/` in Unity Hub, then do step 3.
`Library/`, `Temp/`, `Logs/` and `UserSettings/` are git-ignored; leave the generated `ProjectSettings/` and
`Packages/` uncommitted unless you mean to pin them.

### Running headlessly (cloud, CI)

`Tools/cloud/run-game.sh` builds and runs the game without a desktop: it pulls a Unity 6 editor image
(GameCI, via Docker), assembles a project around `Assets/Lanternvale`, builds a Linux player
(`Lanternvale.EditorTools.LanternvaleBuild.BuildLinuxPlayer`) and runs it on a virtual display with the
in-game **autopilot** (`-lv-autopilot`), which plays a scripted tour and saves screenshots to
`Tools/.cache/cloud/shots/`. `run-game.sh start` plus `Tools/cloud/x.sh` (click / key / screenshot) lets you
drive it by hand. It needs a Unity license in the environment (`UNITY_LICENSE` = contents of
`Unity_lic.ulf`, or `UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD`) and network access to Unity's
license servers (Unity's package registry is not needed). Batch builds for other platforms: `BuildWindowsPlayer`,
`BuildMacPlayer`. The same autopilot works in any build or in the editor (`-lv-autopilot -lv-quit
-lv-shots <dir> -lv-class Mage -lv-level 20`).

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
| Q / E, middle-drag | rotate the camera (±45°) |
| WASD / arrow keys, Shift+middle-drag | pan the camera |
| Middle click | recentre the camera |
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

**Map panel (M)**

| Input | Action |
|---|---|
| Mouse wheel | zoom 1–3× around the cursor |
| Drag (when zoomed) | pan |
| **Whole map** button | zoom back out |
| Hover a dot or glyph | full name, title and quest lines (labels that did not fit show here) |

**Raids**

| Input | Action |
|---|---|
| Walk into (or click) a raid portal | open the raid picker |
| Click a row | tick or untick that character (you are always in; at most ten) |
| **Suggest** | the picker's suggested raid again |
| Enter, or **Enter the raid** | travel with the chosen party |
| Esc, × or **Cancel** | stay where you are |
| **Auto: all companions** / **Auto-battle** (above End Turn, in raids and big fights) | hand every companion, or the whole party, to the AI; turn off to restore each one's own setting |

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
    Art, Rendering    ArtLibrary (manifest + procedural fallbacks), CameraRig (BG3-style perspective rig)
    Rendering3D       World3D conventions (ground = XY, up = −Z), MeshBuilder, Materials3D, SceneLighting
    Input             GameInput: the only input path (legacy Input Manager or IMGUI events)
    World, Units, Fx  MapView (terrain, backdrop, sky, props via World/Props), day/night, particles, UnitView
                      (rigged procedural characters), 3D effects and previews, floating text
    Audio             synthesized SFX and the generative music engine
    Flow              GameFlow (hub), Exploration, FieldPresenter, CombatController, SaveFiles
    UI                Ui toolkit, UiRoot host, UiText tooltips, Hud/* (HUD layers), Panels/* (windows)
  Scripts/Editor    art import settings, Create Game Scene, Validate Data, rendering setup, batch builds
  Resources/Data    all game data as JSON (classes/*.json, content/*.json)
  Resources/Art     305 PNGs + art_manifest.json (portraits, icons, ground textures, decals; 2D-era sprites)
  Resources/Shaders the stylized shaders (own lighting) + LanternvaleCommon.cginc
  Resources/Fonts   Nunito & Fredoka (SIL OFL)
  link.xml          keeps the reflection-mapped assemblies from being stripped in IL2CPP builds
Docs/               design, data schema, API and flow docs, art keys and art prompts (index below)
Tools/check.sh      compile, validate and test everything without Unity (scripts, data, tests, shaders)
Tools/shadercheck/  offline HLSL check of every shader pass (glslangValidator + a UnityCG stand-in)
Tools/harness/      CoreTests: the headless test runner and all [Test]/[Sim] methods
Tools/artgen/       the placeholder-art generator (Python 3 + numpy + Pillow)
Tools/datagen/      the scripts that first generated the class/content JSON (read its README before running)
Tools/preview3d/    offline 3D renders of maps, units and props without Unity (render_previews.sh → Docs/previews3d)
Tools/sfxpreview/   offline renders, spectrograms and checks of the synthesized sounds and music
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
| `Tools/check.sh audio` | the procedural audio suite (`Tools/sfxpreview/sfxpreview.sh check`: determinism, clipping, spectral guard, material distinctness, variant variety, call-site ids, music) |
| `Tools/check.sh all` | `core` + `unity` + `audio` (the default) |

Harness arguments (after `data`/`core`/`all`):

* `--filter <test name>` runs only the matching tests, for example `--filter World` or `--filter Session`.
* `--grep <text>` only prints data problems that contain the text.
* `--sim` also runs the `[Sim]` methods.
* `--no-tests`, `--quiet`.
* `--allow-problems` doesn't fail on data problems. Don't use it for a real check.

The current state is `Data validation: OK` and **`Tests: 469/469 passed`**, both Unity configurations report
`compile OK`, all 32 shader stages compile and `sfxpreview check: 2978 passed, 0 failed`. The tests cover formulas
and the hit table, every class ability used through the real engine (smoke tests), AI battles, items, sets and loot
pools, progression, navigation (water, flag-gated props), dialogue, quests and quest markers, map labels, combat
sound mapping, saves, **full playthroughs** of levels 1–12 for every class through the `GameSession` API, and for the
expansion: every map entered with every spawn and exit reachable, each zone's quest chain scripted to completion, each
hidden dungeon revealed and cleared, and both raids won at their level with a raid of ten.

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
every key with its size and purpose. `Docs/art_previews/` has contact sheets of the current placeholders, and
`Docs/previews3d/` a curated set of offline 3D renders of the zones, dungeons and raids and the expansion's models
(`Tools/preview3d/render_previews.sh`).

Since the move to 3D, characters, creatures and props are procedural models (`Scripts/Game/Units`,
`Scripts/Game/World/Props`); the PNGs still drive portraits, icons, the painted ground, path decals and the
UI. Real 3D models can replace a procedural one behind the same `UnitView` / `PropModels` APIs.

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
| `Docs/ThreeD.md` | **the 3D presentation contract**: conventions, shaders, lighting, modelling, MapView/UnitView/Fx/camera APIs |
| `Docs/PresentationAPI.md` | the presentation APIs' semantics (2D-era wording; ThreeD.md wins where they differ), audio |
| `Docs/UI_HUD.md`, `Docs/UI_Panels.md` | every HUD layer and window, UI conventions |
| `Docs/ArtKeys.md`, `Docs/ArtPrompts.md` | art key catalog and production prompts |
| `Docs/Expansion.md` | **the expansion contract** ("The Ember Road": quest markers, combat sound, deeper maps and hidden dungeons, zones 12–30, party of 5, raids of 10, sets and legendaries) |

---

## Known limitations

* **Never run inside the Unity editor by its authors.** Every Unity-side script is compile-checked in editor
  and player configurations, but only against the Unity 2021.1 reference assemblies that the harness
  downloads from NuGet (`Unity3D.SDK`); the shaders are checked offline with glslang's HLSL front end. The 3D
  models were previewed with an offline software renderer, not in Unity. The rules, world, dialogue, quest and
  save code is tested headlessly. Expect first-run rough edges in everything that only the editor can show:
  lighting levels and colours (gamma vs linear projects), effect sizes, camera feel, animation timing, layout
  and scaling of the IMGUI windows, and frame rate. The presentation timings in the docs are estimates.
* **Review status.** Besides the tests, the code went through several rounds of independent review
  (reviewer → two skeptical verifiers → fixer). The last round still confirmed a handful of new issues
  (they were fixed), so the code is converging but not proven clean; a first play session in the editor is
  the most valuable next check.
* **Procedural art.** Every 3D model is built in code (stylized low-poly) and the 305 images are generated
  placeholders: charming, but not production art. Real models and art can replace them later.
* **Content ends at level 33.** There are 15 maps and 62 quests, tuned for levels 1–30 plus the second raid at 31–33.
  Classes, abilities and talents go to level 60, and character creation offers veteran starts at 10–60 with
  level-appropriate gear; the valley's enemies scale to the party, the new zones' enemies are clamped to their band.
  Raid balance was tuned in simulations (AutoResolve with a party of ten), not by players.
* **Stand-in models.** A few NPCs reuse another model (Granny Sen and other spirits use the forest-spirit model, the
  goats are sheep) and the Barrow's dart trap shows the hunter-trap fallback (`Docs/ArtKeys.md`).
* **Turn-based adaptation.** WoW's real-time rules are compressed into 6-second turns, so some numbers and
  rules are adapted. The rage gain from dealing damage is scaled ×1.75. Hunter traps can be laid in combat,
  and Feign Death does not drop combat. Resurrection works in combat, and party members are downed rather
  than killed. These choices are documented in `Docs/Design.md`.
* **Unity project files are not included.** The repository has no `ProjectSettings/` and no `Packages/`, so
  the editor version and input settings are whatever your project has (see Getting started); the render
  pipeline is set by the build step.
* **Input.** Mouse and keyboard only: no gamepad, no rebinding, no touch.
* **Audio.** Everything is synthesized. On WebGL the music is silent, because `OnAudioFilterRead` is
  unsupported there; sound effects still play.
* **Other.** The game is in English only, with no localisation. UI preferences live in `PlayerPrefs`, not in
  the save. The action bar order and pinned ranks are keyed by character name and class, so two saves with
  the same hero share them.
