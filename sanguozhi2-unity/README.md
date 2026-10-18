# 三国志-霸王的世界 · Unity 重制版

A 3D remake of Namco's Famicom strategy game 三国志II 霸王的大陆 (1992), in
Chinese, grown into a world game, for Windows/Mac and iPad/iPhone. Everything
(terrain, castles, soldiers, portraits, UI, music, sound) is generated in code,
so the project needs no art or audio assets.

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
  K heavy, L guard, U parry (格开; also an on-screen button next to 格挡), I special; on-screen buttons on touch screens.
- **Music**: a C# synthesizer (`Core/MusicEngine.cs`, `Core/MusicInstruments.cs`)
  plays the same newly composed scores as the browser, with regional variants.
- **移动 / 输送** reach any own city connected through own territory; tapping a
  grey command explains why it is unavailable.

The data shared with the browser version (special attacks, world scenario,
geography, portraits, music scores) is generated into
`Assets/Sanguo/Scripts/Data/Generated/` by `../sanguozhi2-web/tools/export-unity-*.js`;
do not edit those files by hand. `UNITY-V2.md` describes the port.

## Version 3 features (ported from the browser version)

The game is renamed **三国志-霸王的世界**. Everything new in version 3 of the
browser version is ported; `UNITY-V3.md` describes the port.

- **Smooth 3D models** (`View/Figure.cs`, `FigureMesh.cs`, `FigureRig.cs`,
  `FigureAtlas.cs`, shaders `Figure` / `FigureCrowd`): skinned generals, soldiers
  and horses in a Dynasty Warriors-like style, with layered armour, capes, plumes
  and signature weapons (青龙偃月刀, 丈八蛇矛, 方天画戟 …); faces match the
  portraits; every culture has its own outfits. Soldiers are drawn as instanced
  crowds.
- **Smooth world map** (`View/MapView.cs`, shaders `Terrain` / `Foliage` /
  `Masonry` / `MapWater`) with blended terrain, farmland, villages, bridges,
  mountain passes, snow peaks, beaches and several kinds of trees.
- **Battles** are 18×13 and drawn from the real surroundings of each city on
  the world map (`Battle/BattleTerrain.cs`), turned so the attacker enters from
  the direction it marched in (sea attacks land on the beach). Lakes and sea are
  impassable water. The smooth board (`Battle/BattleLook.cs`, `BattleView.cs`,
  shaders `BattleTerrain` / `BattleProp` / `BattlePropFade` / `BattleWater`)
  shows each unit's mounted general and 1–6 soldiers in culture formations; the
  first turn waits until the models are ready.
- **攻击** cuts to a clash of the two armies with troop counters, staged in the
  culture and landscape of the battlefield (`Battle/Clash.cs`). Animation is on
  by default (an old saved 关 is reset once); the 动画 button or V switches it
  on / fast / off.
- **必杀 and growth**: every general keeps a unique special attack. Generals gain
  experience and levels (1–10) from fighting; with higher 武力 they learn more and
  stronger special attacks, and with higher 智力 more and stronger 计策 (18 tactics
  in 4 tiers, `Battle/Tactics.cs`). At each level-up the player picks from three
  choices. Veterans also hit harder: each level above 1 adds 4% to attack,
  defence and tactic power (老练, shown on the battle card and in the general's
  details). Under 委任 the AI casts learned tactics such as 伪报 and 挑拨. Version 2
  saves are upgraded on loading.
- **单挑** is a side-view fighting game on the new models (keyboard: ←→ move,
  ↑ jump, J light, K heavy, L guard, U parry, I special; touch: on-screen pad and
  buttons). A parry staggers the opponent and opens an unblockable counter; it
  has a longer cooldown than attacks and guard. Damage and speed come from 武力.
- **Portraits everywhere**: every list of generals, report and news line shows
  the general's portrait; reports are spoken by the general concerned.
- **World**: the world scenario grows to about 70 factions, 200 cities and 500
  generals (more of Rome's provinces and the peoples beyond its frontier,
  Parthia's vassal kings, the Kushan Empire, the Deccan and Tamil kingdoms and
  Sri Lanka, Arabia, Aksum and Kush, Korea, Japan, Taiwan and the steppe), and
  Indian and African cultures get their own looks.

### Testing without Unity

`Tests~/` (ignored by Unity) holds a compile check and headless tests that
compare the C# with the browser version: rules (whole games replay identically,
progression and tactics included), portraits (rendered to PNG), music (rendered
to WAV), the world map, battle maps from the surroundings, duel AI and parry,
the figure library, the battle board, the clash and the duel screen (geometry
and poses compared value by value, and drawn side by side with the browser by a
small software rasterizer), and the interface text (inline portraits, growth
texts). See `Tests~/README.md`.

### Not yet seen running in Unity

The port was compiled and tested headlessly, but nobody has opened it in the
Unity editor yet. On the first run, check: shaders compile (including the new
`LowPolyFade` and `SpecialFx`), fog on the map and in cinematics, the clash and
duel screens, touch controls on an iPhone/iPad, music playback and audio after
switching output devices, and frame rate on a phone during the world map and
the clash cinematic.

### Version 3: check on the first run

Version 3 compiles and its rules, maps, models, battle board, clash and duel
match the browser headlessly, but nobody has opened it in Unity yet. Check first:

1. **Colour space and names**: Player Settings show **Gamma** colour space (set by
   `Editor/SanguoSetup.cs`; colours and lighting are copied from the browser
   unconverted, so in Linear every colour comes out wrong), product name 三国志-霸王的世界, and the
   title screen reads 三国志 / 霸王的世界.
2. **Shaders compile** (no pink objects, no errors in the Console when they
   load): `Figure`, `FigureCrowd`, `Terrain`, `Foliage`, `Masonry`, `MapWater`,
   `Road`, `SeaLane`, `Cloud`, `TreeSprite`, `BattleTerrain`, `BattleProp`,
   `BattlePropFade`, `BattleWater`, `ClashGround`, `ClashProp`, `ClashGrass`,
   `ClashFlag`, `ClashWater`, `DuelGround`, `DuelProp`, `DuelFoliage`, `DuelSky`,
   `DuelWater` (24 in all; the alpha-to-coverage edges of `TreeSprite` need MSAA). They were reviewed line by line but never
   compiled; points to look at are the hand-written lighting functions,
   `texCUBElod` on the figure's material cubemaps, `VFACE` in `DuelProp`, and
   instanced properties in `FigureCrowd`, `BattleProp`, `BattlePropFade`,
   `ClashGrass` and `DuelFoliage`.
3. **Brightness**: the main scene's sun is 0.8625 with Trilight ambient × 0.56
   (`Core/Game.cs`, = the browser's lights / π). The world map and the battle
   board should look about as bright as the browser, no brighter. Flat ground on
   the map keeps its brightness, and the reflections on armour show the sun on the
   correct side (`Figure.cs` environment cubemap orientation).
4. **Smooth world map**: blended terrain without seams between terrain tiles,
   farmland, villages, bridges, passes, snow peaks, beaches and trees; city walls,
   bridges and passes show the brick and tile pattern up close and fade it out
   between 16 and 30 units; frame rate on a phone.
5. **Figures**: generals, soldiers and horses are smooth, their faces match the
   portraits, capes and plumes swing, and feet stay on the ground (ground clamp in
   the shader). The title screen keeps animating while the figure atlas and the
   Han soldiers are prepared in the background behind it (`Clash.WarmUp`; single
   steps of up to about 50 ms are known).
6. **Battle**: the battle waits for the models after the opening banner, then
   every unit shows its mounted general and soldiers (see the battle board list
   below).
7. **Clash**: the 布阵中… spinner appears only when a scene takes longer than
   0.3 s; walls, houses, trees and flags follow the culture of the battlefield
   (a Roman gate at Rome, mud walls in Africa, boats on a coast landing); the
   surname character is drawn on the banners (through a RenderTexture, untested);
   the HUD and the special-attack cut-in look as in the browser.
8. **Duel and parry**: U (or the 格开 button) parries; a parry shows the
   格开！ pop-up, the edge glow, slow motion and a short camera tilt; the cooldown
   ring updates on each fighter's bar and on the touch button.
9. **Growth**: level-up notice and learning dialog after a battle (see the
   interface list below).
10. **Avatars** in every list, report and news line (see the interface list).

### Version 3 interface: check on the first run

The version 3 interface (strategy screen, dialogs, battle menus) compiles and
its text matches the browser line by line (`Tests~/ui/run.sh`), but its layout
has not been seen in Unity. Check, in this order:

1. **Inline portraits and badges** (`InlineDeco` in `Scripts/UI/UIKit.cs`):
   the small faces in reports, news and the city panel's 太守 line, and the
   Lv / 新 / 君 / 守 / tier badges, sit exactly on their placeholders, on the
   right line and not shifted by a character. `InlineDeco` works out whether
   `TextGenerator` counts rich-text tags in its character indices; if faces are
   offset, start there. Also check a long report that scrolls, and the
   typewriter: a face appears when the text reaches it, a badge once the text is
   complete.
2. **Avatars in every list**: the general pickers of each command, 出征, 移动,
   登用, captives, 外交, the faction list and the city panel rows (with Lv and
   新).
3. **Speakers**: command results are spoken by the acting general, month news by
   the advisor with faces, the red 急报 shows the invading commander with a
   pulsing glow, and the battle result comes from our first general.
4. **大事** (chronicle) button in the top bar.
5. **Growth**: the level-up notice after a battle; the learning dialog (3
   candidates, 稍后再选 keeps the slot, × postpones the remaining generals); the
   month-start reminder; the general detail (level and EXP bar, record, 老练,
   all specials and tactics with the slot text, and 习得新招 returning to the
   detail).
6. **Battle**: the 必杀 list (every known special, signature first, unusable
   ones dimmed with the reason), the 策略 list with learned tactics, the cut-in
   showing the move picked, and the unit card's Lv, EXP bar, 老练 and 已施展
   lines.
7. **Toasts** move to the bottom of the screen while a dialog (other than the
   bottom speech box) is open.
8. **Help**: the 【单挑】 and 【成长】 sections (touch wording on a phone or
   tablet).
9. **Old saves**: loading a version 2 save shows the migration toast.
10. **Phones** (screen 430 pt tall or less, e.g. an iPhone in landscape): the
    city stats flow into about three smaller lines with the general list right
    below them; disabled list rows are dimmed as a whole; the 必杀 / 计策 badges
    in the general detail stay on one line.
11. **Names**: the title screen shows 三国志 / 霸王的世界, the app is called
    三国志-霸王的世界, and the iOS home-screen label is 霸王的世界.

### Version 3 battle board: check on the first run

The battle board matches the browser headlessly in 21 cases (`Tests~/battleview/run.sh`) but has not been seen in Unity. Check:

1. `BattleTerrain`, `BattleProp`, `BattlePropFade` and `BattleWater` compile (no pink).
2. Instanced trees, rocks and grass are drawn, cast shadows and keep their own colours.
3. Trees in front of a unit fade out and come back.
4. Water animates with foam on the shore; on sea battles (乐浪<北海, 罗马<迦太基) the sea skirt reaches the horizon.
5. Grid lines and the board border show.
6. The battle waits for `Ready`, then each unit shows its mounted general and 1–6 soldiers (4 riders for steppe peoples); routed units vanish with their soldiers.
7. Dash and shadow after-images are the right size (`BakeMesh(useScale: true)`).
8. The fire stratagem's light and ground glow are about as bright as in the browser (×2/π is an estimate).
9. After the `Game.cs` lighting change, the board is no brighter than the browser.

### Version 3 clash and duel: check on the first run

1. **Clash**: the five `Clash*` shaders compile (the glow arrows of a volley use
   tints above 1); the clash sun lights only layer 30 and the map's lights are
   restored afterwards; shadows reach about 45 units from the camera (the browser
   shadows only the strip of battlefield).
2. **Duel**: the five `Duel*` shaders compile; both fighters render on layer 30
   and their hit, parry, counter and charge glows show; the bowstring of
   黄忠 / 夏侯渊's bow special is visible; the castle wall behind the fighters takes
   no torch shadows; the mirrored fighter on the right (e.g. 吕布 in 关羽 vs
   吕布) is not inside-out and is shaded like the left one; in the clash and the
   duel the figures' shadowed sides take the scene's sky and ground tint.
3. **Duel touch buttons**: the parry ring updates on the 格开 button, and the
   pressed 格开 and 轻击 buttons keep their blue and red faces.

## Code layout

| Folder | Contents |
| --- | --- |
| `Scripts/Data` | Balance constants, formations, tactics, scenario tables |
| `Scripts/Model` | Game state, save/load, strategic commands |
| `Scripts/Strategy` | AI warlords, month processing, conquest, strategy screen |
| `Scripts/Battle` | Battle rules and AI, tactics, battle maps from the surroundings, 3D battlefield, clash cinematic, duel, battle flow |
| `Scripts/View` | Procedural meshes, world map, culture cities, figure library (generals, soldiers, horses), portraits, camera rig |
| `Scripts/UI` | UI built in code (system Chinese fonts, panels, dialogs) |
| `Scripts/Core` | Bootstrap, game flow, synthesized audio |
| `Resources/Shaders` | Figure, terrain, foliage, water, battle board, clash and duel shaders (built-in pipeline), plus the version 2 low-poly, sky, unlit and additive ones |
