# 三国志II 霸王的大陆 · Unity 重制版

A 3D low-poly remake of Namco's Famicom strategy game 三国志II 霸王的大陆 (1992),
in Chinese, for Windows/Mac and iPad/iPhone. Everything (terrain, castles,
soldiers, UI, music, sound) is generated in code, so the project needs no art
or audio assets.

## Opening the project

1. Install **Unity 2022.3 LTS** (or newer) with the iOS build module if you want
   an iPhone/iPad build.
2. In Unity Hub choose **Add → Add project from disk** and pick this
   `sanguozhi2-unity` folder. Pick your installed editor version when asked.
3. On first open, an editor script creates `Assets/Sanguo/Scenes/Main.unity`,
   adds it to the build list, and sets landscape orientation. Press **Play**.

The scene stays empty: `Bootstrap` builds the camera, lights, map, UI and audio
at runtime, so the game also starts from any other scene.

If Unity reports *"You are trying to read Input using the UnityEngine.Input
class, but you have switched active Input handling to Input System package"*,
set **Project Settings → Player → Active Input Handling** to **Input Manager
(Old)** or **Both**.

### iPhone / iPad build

**File → Build Settings → iOS → Switch Platform → Build**, then open the
generated Xcode project, set your signing team, and run on a device.

## How to play

- **Map**: drag to pan, scroll wheel or pinch to zoom, Q/E to rotate, tap a
  city for its details. Arrow keys / WASD also pan.
- **令牌 (command tokens)**: each month you may issue a limited number of
  commands. 1 city gives 3 tokens, and every 2 more cities give one more (max 10).
  赏赐, 交易 and 任命 cost no token.
- **Commands** (in the city panel): 开发 (land → autumn harvest, industry →
  monthly gold, town → population and defense), 征兵, 训练, 搜索 (find hidden
  talents), 登用, 移动, 输送, 外交 (同盟 / 离间 / 拉拢), 出征, 赏赐, 交易, 任命.
- **War**: pick an adjacent city, up to 5 generals (the first is the
  commander) and the food to carry. Command the battle yourself (亲自指挥) or let
  it resolve instantly (委任).
- **Battle**: tap a unit to see where it can move (blue) and which enemies are
  adjacent (red), tap a tile to move, then choose 攻击, 策略 (火计, 落石, 混乱,
  激励), 单挑, 阵型 (8 formations) or 待机. Each side's daily action points come
  from its ruler's 人望 (fame). Win by routing the enemy commander, occupying
  the central keep, or wiping out the defenders. The attacker must win within
  30 days and keep enough food.
- Captured generals can be recruited (登用), released or executed. The game
  autosaves every month; **记录** saves at any time.

## About the rules

Strategy guides for the original were not reachable while this was written, so
the rules are rebuilt from memory of the original game: monthly command tokens,
development, recruitment, diplomacy and formation-based grid battles. All
tunable numbers are in one place, `Assets/Sanguo/Scripts/Data/Defs.cs`
(`Balance`), and the scenario (cities, warlords, generals and their stats) is
plain text tables in `ScenarioData.cs`, so details can be corrected to match the
original.

The scenario is **190 年 · 董卓专横**: 38 cities, 18 warlords and about 150
generals, including hidden talents such as 诸葛亮, 郭嘉 and 周瑜 that you must
find with 搜索.

## Version 2 features (ported from the browser version)

Everything in the browser version (`../sanguozhi2-web`) is ported here:

- **Two scenarios**: 董卓专横 · 中原 (the original campaign) and 天下大势 · 世界
  (Eurasia in 190 AD: 55 factions, about 140 cities, sea routes, culture-styled
  cities). World rulers are chosen by region.
- **Portraits** for every general, drawn by a small C# vector rasterizer
  (`View/Raster.cs`, `View/Portrait.cs`).
- **攻击** plays a side-view clash cinematic (`Battle/Clash.cs`); the 动画 button
  (or V) switches it on / fast / off.
- **必杀**: a unique special attack per general, once per battle, with its own
  effect (`Battle/BattleViewFx.cs`).
- **单挑** is a fighting game (`Battle/Duel*.cs`): ←→ move, ↑ jump, J light,
  K heavy, L guard, I special; on-screen buttons on touch screens.
- **Music**: a C# synthesizer (`Core/MusicEngine.cs`, `Core/MusicInstruments.cs`)
  plays the same newly composed scores as the browser, with regional variants.
- **移动 / 输送** reach any own city connected through own territory; tapping a
  grey command explains why it is unavailable.

The data shared with the browser version (special attacks, world scenario,
geography, portraits, music scores) is generated into
`Assets/Sanguo/Scripts/Data/Generated/` by `../sanguozhi2-web/tools/export-unity-*.js`;
do not edit those files by hand. `UNITY-V2.md` describes the port.

### Testing without Unity

`Tests~/` (ignored by Unity) holds a compile check and headless tests that
compare the C# with the browser version: rules (whole games replay identically),
portraits (rendered to PNG), music (rendered to WAV), map maths and duel AI. See
`Tests~/README.md`.

### Not yet seen running in Unity

The port was compiled and tested headlessly, but nobody has opened it in the
Unity editor yet. On the first run, check: shaders compile (including the new
`LowPolyFade` and `SpecialFx`), fog on the map and in cinematics, the clash and
duel screens, touch controls on an iPhone/iPad, music playback and audio after
switching output devices, and frame rate on a phone during the world map and
the clash cinematic.

## Code layout

| Folder | Contents |
| --- | --- |
| `Scripts/Data` | Balance constants, formations, tactics, scenario tables |
| `Scripts/Model` | Game state, save/load, strategic commands |
| `Scripts/Strategy` | AI warlords, month processing, conquest, strategy screen |
| `Scripts/Battle` | Battle rules and AI, 3D battlefield, battle flow |
| `Scripts/View` | Procedural meshes, map of China, camera rig |
| `Scripts/UI` | UI built in code (system Chinese fonts, panels, dialogs) |
| `Scripts/Core` | Bootstrap, game flow, synthesized audio |
| `Resources/Shaders` | Low-poly, water, sky, unlit and additive shaders |
