# Lanternvale

A party-based, turn-based CRPG in the spirit of **Baldur's Gate 3**, played on hand-painted **2.5D
orthographic dioramas** in a cosy, Ghibli-inspired watercolour style, with **Final Fantasy X–inspired**
heroes. The eight classes — Warrior, Hunter, Paladin, Mage, Priest, Rogue, Warlock, Shaman — play like
their **World of Warcraft Classic** counterparts: the same abilities and ranks, the same three talent
trees each, the same weapon/armour rules, roles and resources (rage, energy + combo points, mana and the
five-second rule, soul shards, hunter pets and the dead zone, shaman totems, threat).

## Getting started (Unity 6 or 2022.3 LTS)

**Recommended — Universal 2D template (2D lights work out of the box):**

1. Unity Hub → *New project* → **Universal 2D** template → create.
2. Close Unity. Copy `Lanternvale/Assets/Lanternvale` from this repository into the new project's
   `Assets/` folder (so you have `Assets/Lanternvale/Scripts`, `Assets/Lanternvale/Resources`, …).
3. Open the project. Menu **Lanternvale ▸ Create Game Scene**, then press **Play**.
   (Pressing Play in *any* scene also works — the game boots itself.)

**Alternative — open this folder directly:** open `Lanternvale/` as a project in Unity Hub. Unity creates
default settings; then run **Lanternvale ▸ Setup ▸ Configure URP 2D Renderer** (installs URP if needed
and assigns a 2D renderer) so sprites react to 2D lights. Without URP the game still runs and fakes the
lighting with glows and a night overlay.

No other packages are required: the UI is immediate-mode (no TextMeshPro import), input works with the
legacy Input Manager *or* the new Input System, and all art/audio has procedural fallbacks.

## Controls

| | |
|---|---|
| Left click | move / interact / select target |
| Right click | cancel targeting / context |
| Mouse wheel | zoom |
| Arrows, middle-drag | pan the camera |
| 1–0, -, = | hotbar |
| Space / Enter | end turn |
| Tab | cycle party member |
| C · I · N · P · L · J · M | character · inventory · talents · spellbook · combat log · journal · map |
| F5 / F9 | quick save / quick load |
| Esc | menu |

## Project layout

```
Assets/Lanternvale/
  Scripts/Core     engine-independent rules (pure C#, no UnityEngine): JSON, data, rules, world logic
  Scripts/Game     Unity layer: boot, art, rendering & lighting, input, world, units, FX, audio, UI
  Scripts/Editor   import settings for art, scene creation, data validation, URP setup
  Resources/Data   all game data as JSON (classes, abilities, talents, items, creatures, maps, dialogue)
  Resources/Art    PNG art + art_manifest.json (placeholders — replace with final art, same names)
  Resources/Fonts  Nunito & Fredoka (SIL OFL)
Docs/              design, data schema, APIs, art keys and art-generation prompts
Tools/check.sh     compile + validate + test everything without Unity
Tools/artgen/      the placeholder-art generator
```

## Working on it

* **Data** is plain JSON — see `Docs/DataSchema.md`. Validate in Unity with **Lanternvale ▸ Validate
  Data**, or from a shell with `Tools/check.sh data`.
* **Rules** are pure C# and covered by tests and headless combat simulations:
  `Tools/check.sh core` (add `--sim` for balance reports).
* **Unity scripts** can be compile-checked without Unity: `Tools/check.sh unity`.
* **Art**: `Docs/ArtPrompts.md` has production prompts for every asset; drop finished PNGs over the
  placeholders in `Resources/Art/**` with the same file names (sizes/pivots in `art_manifest.json`).
