# 三国志-霸王的世界 · 第四版城池视图设计（CITY-VIEW）

Player request 8 of v4: zoom into a city to see its model — farms, stables, barracks and the rest — and see them look better, with more features, as the city advances. This file specifies that view: the building set per culture family, how each building's look follows the city's numbers in five visual stages, the layout, the life in the streets, the camera, the way the domestic vignettes of `DOMESTIC-FX.md` play inside it, the budgets and the module plan. It also carries the city-side half of request 13 (a non-battle game that feels rewarding to build).

Read `DESIGN-V2.md` §1 (common rules) and §6 (culture codes), `DESIGN-V3.md`, `CONTRACT.md`, the sibling v4 specs `DOMESTIC-FX.md` (vignettes, tiers, stings), `ITEMS.md` (the 市场 tiers, 府库, mounts) and `titles.json` first, and **`INTEGRATION.md`** (it wins for cross-system rules: the city-level table `SG.Balance.CityLevel` that rules read, the triangle budget that now includes ENGAGEMENT's projects, the shared day clock). Where this file conflicts with the other specs, this file wins for the city view; the others win for their rules. Platform order: browser first, then the Unity port (§12). The art direction is the v4 art pilot's (`js/lowpoly.js`, `js/lp-pilot.js`): Lanternvale-style stylized low-poly, cozy Ghibli-warm palette, flat-shaded props and buildings with per-face brightness jitter, soft-lump foliage with the warm-top / cool-teal ramp, ink outlines on buildings, props and figures (not on terrain, crops, grass or particles), soft painted light, golden hour by day and warm lantern pools at night. Every building below is to be modelled with `SG.LP.MeshBuilder` in that style; `SG.LPPilot.gateTower` is the reference for the Han gate and the quality bar.

Status: design spec only. No game code was changed. Supporting prototypes in `design-v4/domestic/` (Node, read-only on the game data):

| File | What it is |
|---|---|
| `domestic/city-stages.js` | The 16 buildings, their stat drivers, stage thresholds, hysteresis, city level, triangle budgets; `node design-v4/domestic/city-stages.js` prints the stage histograms of both scenarios at game start, develops per stage step and the triangle estimates, and checks the budgets (ALL OK) |
| `domestic/city-templates.js` | The 12 family layout templates, the placement resolver, house lots and field tiles, water-side rotation; `node design-v4/domestic/city-templates.js [--plan]` checks overlaps, wall containment, street clearance and capacity for every family and turn (ALL OK) and prints ASCII plans |

---

## 0. Key numbers at a glance

| Item | Value |
|---|---|
| Entry | 「入城」 in the city panel, double-tap a city on the map, or the 「⤵ 入城」 chip when the map camera is closer than 18 to the selected city. Exit 「出城」, Esc, or pinch out past the far limit. Transition 0.9 s (map fly-down 0.55 s + ink-wash wipe 0.35 s) |
| Plot | 160 m × 160 m diorama per city; walls 39–47 m from the centre; field ring outside the walls; deterministic from the city key, culture, terrain and stats |
| Families | 18 culture codes → **12 city families** (华夏, 三韩, 倭, 草原, 绿洲, 天竺, 安息, 阿拉伯, 库施·阿克苏姆, 罗马, 北方, 南海) with skins inside a family |
| Buildings | **16**: 城墙·城门, 官署/宫城, 农田, 仓廪, 市, 兵器铺, 工坊, 民居, 兵营, 校场, 马厩, 学馆, 祠庙, 酒肆, 府库, 港/津渡 (coastal or river cities) + a historical **landmark** in 29 cities + the 草庐 spawned by 搜索 |
| Stages | 5 per building (S1–S5), driven by live stats: 农田 = 土地, 工坊 = 产业, 民居 = 町, 城墙 = 防御 (20 + 町/12), 市 / 兵器铺 = ITEMS shop tier, 兵营 = troops, 校场 = training, 马厩 = strong generals + mounts, 学馆 = officers' 智力, 祠庙 = 德 + population, 府库 = gold, 港 = 产业 + 町 … Thresholds 300 / 450 / 600 / 800 for the three develop stats; 5 % hysteresis on the way down |
| Pacing | a stage step takes 7–13 开发 of one stat (政治 90 → 50); measured: about 11 % of 开发 commands cause a stage-up (DOMESTIC-FX §4.3) — a visible upgrade every 2–3 months for an active player |
| City level | 邑 · 县城 · 郡城 · 大郡 · 都会 = mean of the five core buildings, read from the **rules table `SG.Balance.CityLevel`** (thresholds, hysteresis, hubs; INTEGRATION §12) so ENGAGEMENT and ITEMS use the same numbers; at game start (world, 196 cities) 63 / 97 / 31 / 5 / 0 |
| Budgets | big building ≤ **3 000** triangles per stage, small prop ≤ **400**, ENGAGEMENT project ≤ **900** (wonder ≤ 2 000; medium LOD halves them); whole scene drawn (with ink-outline shells) ≤ 250 k desktop / ≤ 150 k phone (estimate for an all-S5 capital with all 13 projects: 216 k / 129 k; without projects 193 k / 119 k); draw calls ≤ 90 / ≤ 55; build ≤ 120 ms in ≤ 8 ms slices; 60 fps desktop, 30 fps iPhone class |
| Life | crowd 10–56 (desktop) / 6–24 (phone) by population, soldiers by troops, oxen, horses, camels or elephants by family, smoke by industry, boats by harbour stage; seasons by month and latitude; the sun moves from 07:00 to 20:00 as the month's tokens are spent (the one shared day clock, INTEGRATION §10.7); lanterns light at dusk |
| Camera | drag pan, wheel / pinch zoom 16–150 m, Q/E / right-drag / two-finger twist rotate, pitch 30°–58° by distance (±10° tilt), tap a building = info chip with stage progress and context commands, double-tap = focus, idle orbit after 25 s |

---

## 1. What the player sees and does

- The city view is a living diorama of one city: its walls and gate, its government seat, its markets and workshops, its homes, its soldiers, its fields and herds, its temple, its harbour — all in the culture's own architecture, all reflecting the city's real numbers.
- It is a **place to govern**: the command buttons work here as on the map; every domestic vignette plays at the building it concerns (the camera goes to the farm for 开发·土地, to the forge for 产业, to the barracks for 征兵, DOMESTIC-FX §5); tapping a building shows what drives it, how far it is from its next stage, and offers its command.
- It is **feedback and reward**: each stage adds visible features (the irrigation channel and its water lift, a second storey on the houses, a barbican on the gate). Buildings close to their next stage show a small bamboo-scaffold marker. Stage-ups are celebrated (DOMESTIC-FX §9.2). Famous cities have their landmark (白马寺, the Colosseum, the Pharos, Palmyra's Temple of Bel …).
- It is **calm**: by day the streets bustle in golden light; spending the month's tokens moves the sun across the sky; at dusk the lanterns come on in warm pools along the streets.

---

## 2. Entering, leaving and the city-view UI

### 2.1 Which cities

Every city can be viewed (the game already shows every city's numbers in its city panel). Own cities show the command dock; allied and enemy cities are view-only (their gate flies their faction's banner, the dock shows only the city stats and 「出城」). Neutral (empty) cities show empty streets and a closed gate.

### 2.2 Entry

1. City panel → 「入城」 (new button in the panel header, icon a gate tower), or double-tap / double-click the city on the world map, or zoom the map camera below distance 18 while a city is selected: a chip 「⤵ 入城」 appears above the city label; tapping it enters. There is no automatic entry (no surprises while panning).
2. The map camera flies to the city (`rig.flyTo`, 0.55 s, ease-in-out) down to distance 14 over the city model, keeping its yaw.
3. An **ink-wash wipe** (0.35 s): a horizontal brush stroke mask (an SVG path with a dry-brush edge) sweeps across the screen from left to right; behind it the city scene is already rendering at the matching shot (`est` with the map's yaw).
4. The top bar stays; the world-map city labels hide (`sg-screen-active`, existing behaviour of the screen stack); the city panel turns into the **dock** (§2.4).

The city scene is built in idle time when the city is selected (§9.3); if it is not ready when the wipe starts, the wipe holds on paper with a small spinning seal for up to 0.5 s (build slices continue), then reveals.

### 2.3 Exit

「出城」 (dock), Esc, a long pinch-out past the far limit (150 m + 15 % with a rubber-band and a 「松开出城」 hint), or the map button in the top bar. Reverse wipe, the map camera rises from distance 14 to the previous map distance (0.45 s). Switching to another own city: the dock's ◀ ▶ arrows (own cities ordered by id) do a short wipe (0.3 s) without returning to the map.

### 2.4 The dock and overlays

- **Desktop / iPad**: the dock replaces the city panel **on the right** (UI-V4 §5: the city panel is the right column, 376 px; the city view keeps it there so the player's eye and hand do not move), with: city name, family name, city-level badge (邑 · 县城 · 郡城 · 大郡 · 都会, with five small towers filled by level), the stats (土地 产业 町 金 粮 防御 兵力 人口) each with a thin stage bar for the building it drives, the generals list (portraits; tap → the character menu of request 9), the command dock (UI-V4 §5's 4 × 4 grid, DOMESTIC-FX §7.1), and ◀ ▶ / 「出城」.
- **Phone landscape (844 × 390)**: the dock collapses to a bottom bar: the same 16 tiles of UI-V4 §5 in one scrollable row (56 × 60 px), grouped by the **four group stripes** (治 · 人 · 运 · 策, the stripe colours of UI-V4 §5.9) and snapping per group, inside the safe-area insets (UI-V4 §15.1); stats and generals are a pull-up drawer. The top bar keeps its current height.
- **Building chip** (tap a building): a small paper panel anchored above the building (`SG.UI.follow`), 260 px wide: the building's family name and generic name (「仓楼 · 仓廪」), stage S3 / 5 with the stage name (「仓楼」), the driver line (`粮 7 200 · 存粮 64 %`), the progress line to the next stage (「土地 452 / 600 → S4 仓城」), one-line effect explanation from the rules (「土地 × 2.2 = 秋收」), and up to two context buttons (§3.2). Tapping elsewhere closes it.
- **Name labels** appear over the buildings when the camera is closer than 40 m (decluttered like the map's city labels, max 8 visible).
- **Scaffold markers**: a small bamboo-scaffold glyph floats above any building whose driver is within 8 % of its next threshold — the "almost there" nudge.

---

## 3. Culture families

### 3.1 Mapping

| Family | Name | Cultures | Skins (chosen by culture or city) |
|---|---|---|---|
| `hua` | 华夏 | han, nanman | 中原 (north, rammed earth, grey tile) · 江南 (south of the Huai: white walls, canals, darker tile) · 南中 (nanman and 建宁 / 云南: bamboo palisade, 干栏 stilt houses) |
| `han3` | 三韩 | korea | 高句丽 (mountain stone fortress) · 三韩 (south: earth-and-timber walls, paddies) |
| `wa` | 倭 | wa | one skin (Yayoi moat-ring settlement) |
| `steppe` | 草原 | steppe, sarmatian | 毡帐 (Xianbei / Wuhuan / Xiongnu) · 篷车 (Sarmatian wagons; 潘提卡彭 uses the roman family as a Greek city) |
| `oasis` | 绿洲 | tarim | one skin (rammed-earth oasis city) |
| `indic` | 天竺 | kushan, india | 犍陀罗 (Kushan: stone, Sirkap grid, stupas) · 南亚 (brick, moats, torana gates, coconut palms) |
| `persia` | 安息 | persia | 平原 (Mesopotamia: mud brick, date palms) · 高原 (Iranian plateau: qanat orchards, poplars) |
| `arabia` | 阿拉伯 | arab, moor | 商城 (Palmyra / Hatra / Edessa) · 也门 (Saba: terraces and the dam) · 毛里 (moor: dry-stone, grain fort, tents) |
| `nile` | 库施·阿克苏姆 | africa | 库施 (Meroë, Napata, Karanog: mud brick, pyramids, iron) · 阿克苏姆 (Aksum, Adulis: granite, stepped podia) |
| `roman` | 罗马 | roman (and the Greek city 潘提卡彭) | 意大利 · 东方 (Antioch, Alexandria, Ephesus: colonnades) · 边省 (Britain, the Rhine and the Danube: timber and stone forts) · 非洲 (Carthage, Lepcis: limestone) |
| `north` | 北方 | celt, german | 凯尔特 (roundhouses, brochs in Caledonia) · 日耳曼 (longhouses) |
| `nanhai` | 南海 | seasia, yi | 扶南·林邑 (canal grid, brick temple platforms) · 夷洲 (Taiwan: tribal village with a men's house and watchtower) |

`SG.cultureBase` fallbacks are not needed: every culture code has a family. The skin is chosen by the culture code and a small city table (`CITY_SKIN`, §11).

### 3.2 Context commands per building

| Building | Tap offers |
|---|---|
| 城墙·城门 | 出征 · 移动 |
| 官署 / 宫城 | 任命 · 赏赐 · 授官 (TITLES) |
| 农田 | 开发·土地 |
| 仓廪 | 交易 · 输送 |
| 市 | 市场 (ITEMS) · 交易 |
| 兵器铺 | 市场 (opens on the weapons tab) |
| 工坊 | 开发·产业 |
| 民居 | 开发·町 |
| 兵营 | 征兵 |
| 校场 | 训练 |
| 马厩 | 征兵 · the mounts list (ITEMS 坐骑 in the armoury) |
| 学馆 | 搜索 · the officers sorted by 智力 |
| 祠庙 | the faction's 德 / 人望 and what raises them (read-only) |
| 酒肆 | 搜索 · 登用 (when discovered free talents are in the city; they are shown sitting at a table outside) |
| 府库 | 府库 (ITEMS armoury screen) · 赏赐 |
| 港 / 津渡 | 输送 |

---

## 4. Buildings and stages

### 4.1 The sixteen buildings

Stages are integers 0–4 shown as S1–S5. `count(v, t)` = how many thresholds `v` has reached. Exact rules in `domestic/city-stages.js`.

| id | Building | Driver | Thresholds S2 / S3 / S4 / S5 | Triangles per stage S1 … S5 (before outlines) |
|---|---|---|---|---|
| `walls` | 城墙·城门 | 防御 `20 + 町/12` (+ garrison titles, TITLES) | 45 / 55 / 65 / 80 | 1800 / 2400 / 2800 / 3000 / 3000 |
| `palace` | 官署 / 宫城 | 人口; +1 stage when the ruler resides | 250 k / 400 k / 600 k / 850 k | 1200 / 1700 / 2200 / 2600 / 3000 |
| `farms` | 农田 | 土地 | 300 / 450 / 600 / 800 | 1400 / 2000 / 2600 / 3000 / 3000 |
| `granary` | 仓廪 | capacity = 农田's stage; fill = 粮 / (1.25 × land × 2.2) | (as 农田) | 500 / 800 / 1200 / 1600 / 2000 |
| `market` | 市 | ITEMS shop tier `max(1 + [产业+町 ≥ 520] + [≥ 800] + [≥ 1100], hub)`; S5 at 产业 + 町 ≥ 1500 | tier 2 / 3 / 4 / 4 + 1500 | 700 / 1300 / 1900 / 2500 / 3000 |
| `shop` | 兵器铺 | shop tier; S5 when the stock holds an epic or two rares (ITEMS §6.4) | tier 2 / 3 / 4 / rare stock | 400 / 700 / 1000 / 1400 / 1800 |
| `workshop` | 工坊 | 产业 | 300 / 450 / 600 / 800 | 600 / 1100 / 1600 / 2200 / 2800 |
| `houses` | 民居 | 町 (density, storeys); population drives the crowd | 300 / 450 / 600 / 800 | ≤ 400 each, instanced: 10 / 16 / 24 / 34 / 46 houses (phone 8 / 12 / 16 / 22 / 28) |
| `barracks` | 兵营 | troops in the city | 4 k / 10 k / 20 k / 40 k | 600 / 1100 / 1600 / 2200 / 2800 |
| `drill` | 校场 | troop-weighted training | 40 / 60 / 80 / 95 | 300 / 500 / 800 / 1100 / 1400 |
| `stables` | 马厩 | officers with 武力 ≥ 75 + equipped mounts (ITEMS), +2 for 草原 | 1 / 3 / 5 / 8 | 500 / 900 / 1300 / 1700 / 2200 |
| `academy` | 学馆 | Σ max(0, 智力 − 60) / 10 over the officers | 1 / 2.5 / 5 / 9 | 500 / 900 / 1400 / 1900 / 2400 |
| `temple` | 祠庙 | 德 / 25 + 人口 / 250 k (+1 capital) | 2 / 3 / 4.5 / 6 | 600 / 1000 / 1500 / 2100 / 2800 |
| `tavern` | 酒肆 | 町 (lantern sign = discovered free talents present) | 250 / 400 / 550 / 750 | 250 / 300 / 340 / 380 / 400 |
| `treasury` | 府库 | 金 | 500 / 1 500 / 4 000 / 10 000 | 400 / 600 / 900 / 1200 / 1600 |
| `harbour` | 港 / 津渡 | 产业 + 町, +1 stage for a tier ≥ 3 trade hub; only coastal or river cities | 500 / 750 / 1000 / 1400 | 700 / 1200 / 1800 / 2400 / 3000 |

The thresholds of the five core buildings (农田, 工坊, 民居, 城墙, 市) and the city level are the rules table `SG.Balance.CityLevel` (INTEGRATION §12: ENGAGEMENT's prestige for each new level and the wonder's level ≥ 郡城 read it, and ITEMS' shop tier is the market's driver); this file's other eleven buildings are view-only and stay in `js/city-layout.js`. ENGAGEMENT's 13 projects (scaffold → shell → finished model, ENGAGEMENT §8.4) attach to their anchor buildings and count in the scene budget (§9.5).

The **landmark** (≤ 3 000 triangles, §6.5) appears in its city from S3 of the city level (or always in a capital), and the **草庐** cottage (≤ 400) is spawned only by the 搜索 vignette.

Stage histograms at game start (`city-stages.js`; S1 / S2 / S3 / S4 / S5):

| Building | Classic (38) | World (196) |
|---|---|---|
| 城墙 | 12 / 20 / 4 / 2 / 0 | 68 / 95 / 27 / 6 / 0 |
| 官署 | 4 / 11 / 12 / 6 / 5 | 59 / 71 / 40 / 18 / 8 |
| 农田 | 0 / 23 / 11 / 4 / 0 | 24 / 127 / 37 / 8 / 0 |
| 市 | 8 / 18 / 7 / 5 / 0 | 40 / 96 / 42 / 18 / 0 |
| 工坊 | 11 / 16 / 9 / 2 / 0 | 64 / 98 / 29 / 5 / 0 |
| 民居 | 12 / 21 / 3 / 2 / 0 | 68 / 110 / 13 / 5 / 0 |
| 兵营 | 12 / 11 / 11 / 4 / 0 | 106 / 63 / 22 / 5 / 0 |
| 校场 | 10 / 2 / 26 / 0 / 0 | 70 / 26 / 100 / 0 / 0 |
| 马厩 | 20 / 12 / 2 / 4 / 0 | 142 / 44 / 6 / 4 / 0 |
| 学馆 | 21 / 6 / 5 / 5 / 1 | 149 / 28 / 8 / 10 / 1 |
| 祠庙 | 0 / 10 / 20 / 8 / 0 | 40 / 56 / 63 / 30 / 7 |
| 府库 | 12 / 23 / 3 / 0 / 0 | 49 / 136 / 11 / 0 / 0 |
| City level | 11 / 16 / 9 / 2 / 0 | 63 / 97 / 31 / 5 / 0 |

So nobody starts with a S5 farm, workshop, market or wall: the top stage is always earned. 洛阳 and 长安 start with S5 palaces (population ≥ 850 k with the emperor's court) — the capitals look like capitals from turn one.

Develops per stage step (gain = 6 + 政治/6 + 2 on average): 300 → 450 and 450 → 600: 10 / 8 / 7 develops for 政治 50 / 70 / 90; 600 → 800 and 800 → 999: 13 / 11 / 9.

### 4.2 The stage grammar (all families)

Every building follows the same additive grammar, so a player learns to read any culture's city:

| Stage | Rule | Example (华夏 农田) |
|---|---|---|
| **S1** 草创 | one main mass, 2–3 props, rough materials (thatch, earth, raw timber), muted colours | scattered small plots, a well, hand hoes |
| **S2** 成形 | regular footprint, a second mass, the roof or surface upgraded (thatch → tile, felt → painted felt), one accent of the faction colour | strip fields with raised paths (阡陌), an ox team, a scarecrow |
| **S3** 技艺 | the family's signature technology appears and **moves** (water wheel, bellows, drum, loom) | irrigation channels from the river, the square-pallet water lift (翻车), the ox-drawn seed drill (耧车) |
| **S4** 规制 | institution: an enclosure or court, a second storey, attendants or guards, lanterns | a water-powered trip-hammer mill (水碓), a threshing floor, orchards, a field granary tower |
| **S5** 鼎盛 | a landmark silhouette visible from the overview shot, gold or vermillion trim, a night glow, doubled ambient life | a golden sea of grain over terraces, mulberry groves, the village 社 tree hung with lanterns |

Parts never disappear when a building advances (except S1 thatch replaced by S2 tile); the recipe tags every part with the stage it appears at, which makes the upgrade animation a simple "grow the new parts" (§4.4).

### 4.3 华夏 in detail (the reference family)

| Building | S1 | S2 | S3 | S4 | S5 |
|---|---|---|---|---|---|
| 城墙·城门 | 土垣: low rammed-earth wall, timber gate | 夯土城: tall rammed wall, gate tower (the pilot's 城门楼) | 砖城: brick facing, corner towers (角楼) | 马面: bastions every 12 m, double-eave gate tower, moat with a bridge | 瓮城: barbican gate, crenellations, banners, watch-fires at night |
| 官署 / 宫城 | 县廷: one hall | 郡府: hall with side wings, a drum at the gate | 府署: courtyard compound, a pair of 阙 pillar towers | 王府: two-tier platform, double-eave hall | 宫城: three-tier platform, golden ridge ornaments (the v3 capital), ruler's banners and guards |
| 农田 | 垦荒 | 阡陌 | 沟渠灌溉 (翻车, 耧车) | 水碓 (water trip-hammer), threshing floor, orchards | 沃野千里: terraces, mulberry, 社 tree |
| 仓廪 | 囷: round thatched bins | 仓: a granary hall | 仓楼: the multi-storey granary tower of the Han tomb models | 仓城: a walled granary compound | 敖仓: great granary rows; fill shown by sacks in the yard and open doors |
| 市 | 草市: stalls under awnings (ITEMS 村市) | 市集: rows of shops and the market flag tower 旗亭 | 大市: walled market square with gates and the market drum | 都会: two-level arcades, foreign merchants with camels | 万国之市: crowds, banners of many cultures, night market lanterns |
| 兵器铺 | a stall with spears | a shopfront with weapon racks | an armoury hall with armour stands | a forge-shop showing its best three items as their real models (ITEMS `SG.ItemArt`) | a lit display case on a light pillar holding the shop's epic / rare piece |
| 工坊 | 小冶: one hearth, hand bellows | 冶坊: two forges, charcoal stacks | 水排鼓铸: the water-powered bellows wheel (杜诗) | 铁官: a tall furnace, a casting yard with moulds | 百炼: several furnaces, tool racks, a glowing smoke column at night |
| 民居 | 茅舍: thatched huts | 闾里: tiled courtyard houses in walled lanes | 市楼: two-storey houses, the market drum tower | 坊市: dense two-storey rows, 里门 gate arches, wells | 阛阓: lantern streets, three-storey towers, crowds |
| 兵营 | 营: a few tents | 营垒: timber barracks, palisade | 营寨: tiled barracks, a watchtower | 大营: several halls, an armoury, a drum tower | 连营: the camp spills outside the walls, banners in rows |
| 校场 | 空场: bare ground | 校场: straw targets | 演武场: a reviewing platform (点将台), weapon racks | 都试场: archery range, horse track | 讲武台: a grand terrace with banners and drums |
| 马厩 | 马棚: lean-to, 2 horses | 厩: stable row, 4 horses | 马坊: paddock, 8 horses | 牧苑: large paddock, grooms' quarters, 12 horses | 天厩: grand stables; the faction's named mounts (ITEMS 赤兔, 的卢 …) stand in their own stalls |
| 学馆 | 私塾: a reading hut | 学舍 | 郡学: a courtyard with a lecture hall | 精舍: a scholar's retreat with a library tower | 太学 (洛阳): the stone classics 熹平石经 (175–183) stand at the gate; elsewhere 文学馆 |
| 祠庙 | 社: an earth-god shrine under a tree | 祠: an ancestral shrine | 庙: a temple hall | 坛庙: altar terrace and hall | 明堂-type ritual hall; 洛阳 adds 白马寺 as its landmark |
| 酒肆 | 酒垆: a wine stall | 酒肆 | 酒楼: two storeys | 酒家: with a courtyard | 名楼: a lantern-lit tower |
| 府库 | 库 | 府库 | 钱库: coin-string stacks visible | 金库: a vault with guards | 内府: overflowing chests, a gold glow |
| 港 / 津渡 | 渡口: a ferry landing | 津: wooden pier, fishing boats | 港: piers and warehouses, sampans | 水寨: naval yard with 艨艟 warships | 楼船: tower ships and a beacon tower |

Skins: 江南 replaces rammed earth by white plaster walls, adds a canal along the main street with stone bridges, darker tile, more boats; 南中 (nanman) replaces the walls by a bamboo palisade (S1–S3) and stone (S4–S5), the houses by 干栏 stilt houses, the workshop by a bronze-drum foundry (the Dian bronze tradition), the stables by an elephant enclosure from S3, and the temple by a sacred tree with an altar.

### 4.4 Stage changes

- **Hysteresis**: a stage drops only when its driver falls below 95 % of the threshold it had reached (recruiting lowers population; battles lower troops), so buildings do not flicker.
- **Upgrade animation** (`director.grow(id, toStage)`, 0.8 s): the parts tagged with the new stage rise from the ground (y −h → 0 with a spring, overshoot 6 %), roofs drop in from 3 m above, props pop (scale 0 → 1.1 → 1), dust puffs at their bases, `fx-build` SFX; for S4 and S5 a bamboo scaffold wraps the building for the first 0.3 s and falls away. Used by the great tier of the domestic commands (DOMESTIC-FX §5) and by the month-start celebration (DOMESTIC-FX §9.2).
- **Downgrade**: no animation; the building simply looks one stage poorer the next time it is shown; its stage bar in the dock turns amber.
- **Scars after a siege**: for two months after the city was attacked (`G.log` / the battle setup records the month), the walls show crack decals and scorch marks, one house is a burnt shell, extra soldiers stand on the walls; it fades over the second month. A captured city also shows the new owner's banners going up when first viewed.
- **Save**: the last shown stage vector per city (`G.fxStages`, DOMESTIC-FX §10.3) avoids celebrating twice.

---

## 5. The families

Each paragraph gives the layout character, the building names (S1 → S5 signature), materials and palette, the crowd and animals. All are modelled in the Lanternvale low-poly style; palettes are warm and readable, each family has one signature colour pair.

### 5.1 华夏 `hua`

See §4.3. Square walls, south gate, an axial main street to the government seat in the north (as the v3 `culture-art.js` Han city). Palette: rammed-earth ochre `#bdb4a0`, grey-blue tile `#465163`, vermillion columns `#9a3226`, cream walls. Crowd: 袍 robes and 帻 head-cloths in earth and indigo tones, porters with shoulder poles, ox carts. Animals: oxen (farms), horses, pigs in the courtyards (the Han tomb models show pigsties with every farmhouse), chickens, egrets over the fields.

### 5.2 三韩 `han3`

- **高句丽 skin**: a stone mountain fortress — the walls follow the ridge around a hill (the template raises a 6–10 m hill under the centre), the royal compound on the summit, terraced dry fields below. 「多大山深谷，无原泽……好治宫室」 (三国志·东夷传). Storage: no great granary but every house has its own small raised storehouse, 桴京 (「无大仓库，家家自有小仓，名之为桴京」) — the granary building is a cluster of 桴京 that grows in number and size by stage. Temple: the big ritual houses beside the residence where spirits, 灵星 and 社稷 are worshipped (same source), S5 adds the 东盟 festival ground. Horses are small ponies 「便登山」. Tavern: 「善藏酿」 — a brewery-tavern with jars. Landmark: the stone-piled tombs (积石冢) outside the walls.
- **三韩 skin** (徐罗伐, 慰礼城, 目支, 金官): earth-and-timber walls, paddies, tile-roofed halls. 金官 (Gaya) shows its iron trade: stacks of iron ingots (铁铤) at the workshop and market (弁辰 iron supplied 韩, 濊 and 倭, 三国志·东夷传).
- Palette: grey stone `#a6a296`, dark tile `#4e5560`, white plaster, pine green. Crowd: 折风 caps for officials, white robes.

### 5.3 倭 `wa`

A Yayoi moat-ring settlement after Yoshinogari: double moat (环壕) with a palisade, watchtowers (望楼) at the gates and corners, an inner enclosure for the ruler's ritual hall and residence — 「居处宫室楼观，城栅严设，常有人持兵守卫」 (魏志·倭人传). Granary: raised-floor storehouses (高床仓库; the 倭人传 mentions 邸阁 storehouses where taxes are collected) grouped near the market; market: 「国国有市，交易有无，使大倭监之」 — S3 adds the overseer's booth. Houses: pit dwellings (竖穴住居) with thatched cone roofs, then raised-floor houses at S4–S5. Workshop: bronze casting (dōtaku bells, mirrors) and bead making. **No horses**: 「其地无牛马虎豹羊鹊」 — the 马厩 slot becomes a boat shed (舟屋) with dugout and plank boats, and 征兵 vignettes show warriors on foot. Academy: a divination hut (the 倭人传 describes reading cracks in burnt bone, 灼骨而卜). Palette: thatch gold `#a88a52`, cedar brown `#8a6a46`, vermilion only on the ritual hall at S5. Crowd: 贯头衣 tunics, tattooed men.

### 5.4 草原 `steppe`

No walls: a wagon laager (车阵) ring that thickens by stage (S1 a few carts, S5 a double ring with gate towers on wheels). The khan's tent in the centre (S5 golden roof finial), felt gers in rings (民居 = gers; density by 町), herds instead of fields: **农田 becomes 牧场** — 土地 drives the number of sheep, cattle and horses grazing the field ring (S1 a flock, S5 herds to the horizon with herders on horseback). Granary: storage pits and felt store-tents; market: 互市 trade fair at the edge with Han carts and Sogdian camels; workshop: leather, felt and bronze; 马厩 = big horse corrals (+2 to the driver); 校场 = archery range and a horse-race track; 祠庙 = an ovoo cairn and a sacred tree with blue and white cloth (S5 a sacrificial altar); 酒肆 = a kumis tent; 学馆 = a shaman's tent. Excavated Xiongnu settlements (Ivolga) show that walled sites with houses existed too: the 柳城 / 宁城 frontier cities use the 华夏 family. Palette: felt cream `#e8dcc0`, sky blue cloth, red-brown leather. Animals: horses everywhere, sheep, oxen pulling tent-carts, hawks.

### 5.5 绿洲 `oasis`

A rammed-earth rectangle with corner towers (交河 / 楼兰 type), flat-roofed houses, a citadel in one corner, rows of poplars along irrigation canals (渠), and outside the walls vineyards, alfalfa (苜蓿) and orchards — 大宛's vines and "heavenly horses" are the Shiji's famous image; 大宛's 马厩 S5 shows 汗血马 (and ITEMS 大宛天马 in its stall when owned). Temple: a Buddhist stupa and shrine court (于阗, 龟兹, 鄯善), S5 a large stupa with prayer flags; market: a caravan court with camels and bales; 于阗's market shows jade from the jade river. Beacon towers (烽燧) on the field ring at S4+. Palette: loess `#cfb487`, poplar green, turquoise door frames.

### 5.6 天竺 `indic`

- **犍陀罗 skin** (富楼沙, 塔克西拉, 迦毕试, 蓝氏城, 秣菟罗, 迦湿弥罗, 巴格兰, 康居, 撒马尔罕): a grid city on the Sirkap model — one main avenue with perpendicular streets, stone walls with rectangular bastions, the palace in a walled block, a stupa court (temple) with a harmika and stacked parasols, a vihara monastery (学馆 S3+), the apsidal temple at S4. Stables for horses; the 贵霜 royal stables at S5 with caparisoned horses.
- **南亚 skin** (华氏城, 乌贾因, 巴里加扎, 派坦, 纳西克, 阿马拉瓦蒂, 南印度诸城, 阿努拉陀 …): moat and brick walls with semicircular bastions, barrel-vaulted (chaitya-arch) gate houses and palace roofs, torana gateways at the stupa (the v3 `culture-art.js` india model), a bathing tank (pushkarini) in the centre at S3+, rice paddies and coconut palms, **elephant stables** (象厩) instead of horse stables from S3. 华氏城's walls are the famous timber palisade with towers described by Megasthenes (via Arrian and Strabo; fragments excavated at Bulandi Bagh) — a liberty for 190 AD, when the city had declined, used for its legend. Ports (穆济里斯, 巴里加扎, 普哈尔, 耽摩栗底) have busy harbours with Roman-type merchant ships at S4–S5.
- Palette: red brick `#b46a4a`, white plaster domes, saffron cloth.

### 5.7 安息 `persia`

A round mud-brick city (the Parthian and early Sasanian round plans: Hatra's neighbour cities, Darabgerd, Merv's walls are square — the 高原 skin uses square walls for 木鹿, 尼萨, 雷伊): palace and an iwan court in the centre — the iwan, a barrel-vaulted hall open to a courtyard, is the Parthian signature (S3 one iwan, S4 four iwans round the court, S5 a domed hall as at Old Nisa), a fire temple with a domed sanctuary (chahar taq) at S3+, a vaulted bazaar ring. Outside: date palms (平原 skin) or orchards watered by qanat shafts (高原 skin; a line of mounds marking the underground channel). Stables: the Nisaean horses of the cataphracts; S5 shows armoured horses. Palette: mud brick `#c8a679`, glazed blue-green tile frames on the iwan from S4, white plaster. Crowd: trousers, long coats, tiaras for nobles.

### 5.8 阿拉伯 `arabia`

- **商城 skin** (帕尔米拉, 哈特拉, 埃德萨, 波斯特拉, 埃梅萨, 尼西比斯, 查拉克斯, 法奥): Palmyra's colonnaded main street from the gate to the great temple (祠庙 = Temple of Bel at 帕尔米拉, the Great Temple's iwans at 哈特拉), an agora-like caravan court (市; Palmyra's tariff court), cisterns, flat stone houses, date palms; camels in the 马厩 (驼厩) beside the horses. 哈特拉 uses round walls.
- **也门 skin** (马里卜, 扎法尔, 沙布瓦): mud-brick tower houses, terraced fields climbing the hills, frankincense at the market; 马里卜's farms S5 is the great dam and its sluices.
- **毛里 skin** (巴夸特): dry-stone walls, a fortified communal granary, flat stone houses, nomad tents outside.
- Palette: honey limestone `#d8c49a`, palm green, indigo cloth.

### 5.9 库施·阿克苏姆 `nile`

- **库施 skin** (麦罗埃, 纳帕塔, 卡拉诺格): a mud-brick royal city on the river (east–west temple axis along the water), the Amun temple with pylons (祠庙), the palace on a stepped podium, iron furnaces with growing slag heaps at the workshop (Meroë's iron industry — the large slag mounds are mostly from earlier and later periods, a liberty), a hafir water reservoir at the fields, Nubian archers at the 校场 (the bow was Kush's emblem), elephants and camels in the stables. Landmark (麦罗埃): the steep pyramids of the royal cemetery and the Lion Temple of Apedemak.
- **阿克苏姆 skin** (阿克苏姆, 阿杜利斯): granite palaces on stepped podia with the "monkey-head" beam ends, terraced fields, round thatched houses; landmark (阿克苏姆): a field of rough, uncarved stelae (the great carved stelae are 4th-century and are not shown); 阿杜利斯 has the Red Sea harbour.
- Palette: Nile mud `#a0704a`, sandstone, granite grey, white robes.

### 5.10 罗马 `roman`

A castrum-like grid: the cardo and the decumanus cross at the **forum** (官署 = forum, basilica and curia; S5 a marble basilica with a statue of the emperor), the Capitolium temple on a podium (祠庙), horrea (仓廪: buttressed walls, raised floors on piers, few doors, as at Ostia and Housesteads), the macellum (市), tabernae shops (兵器铺 = an armorum negotiator's shop), a fabrica (工坊), domus and insulae (民居: S4 three-storey insulae, S5 the town gains baths, thermae, and an aqueduct arriving over the fields), castra barracks, a campus for drill, a bibliotheca (学馆), popinae (酒肆), and a portus with a lighthouse (港). Skins change the stone and the extras (边省: timber-and-stone fort walls and a vicus; 东方: colonnaded streets). Landmarks: 罗马 (Colosseum, Pantheon), 亚历山大 (Pharos lighthouse at the harbour), 以弗所 (Library of Celsus, Temple of Artemis), 迦太基 (Antonine Baths), 伦迪尼乌姆 (the basilica-forum), 安条克 (the colonnaded street). Palette: travertine `#cdbf9f`, terracotta roof `#b4553a`, red stucco, white marble at S5.

### 5.11 北方 `north`

An oval hill-fort: earth rampart and timber palisade (S1–S3), stone-faced rampart and a gate tower (S4–S5), the chief's hall on the crest (roundhouse hall for 凯尔特, a longhouse hall for 日耳曼), roundhouses or longhouses, four-post raised granaries, small square fields, a smithy, a horse paddock, a mead hall (酒肆), a sacred grove (祠庙: a ring of oaks with offerings, S5 a timber shrine), and a boat landing. 喀里多尼亚 has a broch (dry-stone tower) as its landmark; 塔拉 the royal earthworks of the Hill of Tara. Palette: turf green, oak brown, ochre plaster, woad blue cloth, checked cloaks.

### 5.12 南海 `nanhai`

- **扶南·林邑 skin**: a canal grid on the Óc Eo model — a central canal with transverse canals, stilt houses along them (the early phase's primary house form), a temple on a raised brick platform (S3), a palace on high piles, a quay market where boats are the stalls, bead and bronze workshops (Óc Eo's bead making), rice paddies, an elephant kraal (马厩), and a harbour with large ocean-going ships (S4–S5) — Roman coins of Antoninus Pius and Marcus Aurelius were found at Óc Eo.
- **夷洲 skin**: a tribal village on a slope — stilt houses, a men's house (palace / barracks), a watchtower, millet fields, a bamboo palisade.
- Palette: palm thatch `#a08a50`, teak, brick red, jade-green water.

---

## 6. Layout

### 6.1 Plot and coordinates

- Plot 160 m × 160 m, x east, z north, centre at the origin; the city scene is its own `THREE.Scene` (§9.1), so no conflict with the map's coordinates. One building is "life-size" relative to the figures (1.8 m), but distances are compressed: the whole city is a diorama, like a Lanternvale village.
- **Templates** (`domestic/city-templates.js`, one per family): wall shape and size (`square`, `round`, `oval`, `hill`, `moat`, `wagon`, `grid`, `canal`), gates, the main street (and the cross street for 罗马), a preferred centre and size for each of the 16 slots inside or outside the walls, preferred residential areas, the field ring, and the template's water side. Authored on a 120 m plot and spread ×1.3 (building sizes unchanged).
- **Resolver** `resolve(T)`: every slot is placed at its preferred centre, or the nearest free spot on a 1 m spiral (≤ 24 m away): inside slots stay 2 m inside the walls and off the streets, outside slots stay outside the walls and in the plot, 1.5 m clearance between buildings. Deterministic.
- **House lots**: a 5 m grid (4 m house + 1 m lane) over all free space 3 m inside the walls, ordered so the town fills outward from its main street (preferred residential areas first). Measured capacity 60–127 lots per family (needed: 46).
- **Field tiles**: an 8 m grid in the ring between the walls and the plot edge, off the gate roads and outside slots, ordered from the water side (irrigation spreads from the river). Capacity 127–150 (needed: 40 at S5).

Measured (`node design-v4/domestic/city-templates.js`, all checks ALL OK):

| Family | Walls | Gates | House lots | Field tiles |
|---|---|---|---|---|
| hua | square | S | 123 | 142 |
| han3 | hill | S E | 73 | 150 |
| wa | moat | S W | 84 | 146 |
| steppe | wagon | S N | 70 | 144 |
| oasis | square | S E | 91 | 146 |
| indic | grid | S N | 117 | 139 |
| persia | round | S E W | 79 | 140 |
| arabia | oval | S W E | 60 | 140 |
| nile | square | W S | 91 | 146 |
| roman | square | S N E W | 114 | 127 |
| north | hill | S E | 68 | 146 |
| nanhai | canal | S E | 127 | 144 |

The 华夏 plan (2 m per character; `#` wall, `:` main street, P 官署, $ 府库, T 祠庙, A 学馆, M 市, S 兵器铺, G 仓廪, W 工坊, B 兵营, V 酒肆, h house lots (half shown), D 校场, H 马厩, L landmark, R harbour, `.` field tiles):

```
    ............ ############################################## ............
    ............ #   h  h h       h h  h h  h h  h h  h h  h  # ............
    ............ #          $$$$                              # ............
    ............ #                PPPPPPPPPPPP                # ............
    ............ #   TTTT         PPPPPPPPPPPP       AAAAA    # ............
    ............ #   TTTT    h h  PPPPPPPPPPPP   h h AAAAA    # ............
    ............ #   h  h h  h h  h h  :::  h              h  # ............
    ............ #   h  GGGGGGG   h h  :::  h  MMMMMMMMM   h  # ............
    ............ #      GGGGGGG        :::     MMMMMMMMM      # ............
    ............ #   h  h h  h h  h h  :::  h h  h h  SSSS h  # ............
    ............ #      WWWWWW         :::        BBBBBBB     # ............
    ............ #   h  WWWWWW h  VVV  :::  h h   BBBBBBB  h  # ............
    ............ ############################################## ............
    ....HHHHHHHHH   ................        ................  DDDDDDDDDDD
```

### 6.2 Terrain, water side and orientation

- The scene reads the world map around the city: `SG.Game.map.terrainInfo(x, z)` at the city and at 8 directions × 2 radii (1.2 and 2.5 map units): biome, slope, water (`sea` / `river` / `lake`), forest, snow, sand, road.
- **Water side**: the direction of the nearest water sample (sea first, then river, then lake). The template is rotated in 90° steps so its water side faces it; the harbour (`港`) goes on that edge when the city is coastal (sea within 2.5) and a river quay (`津渡`) when only a river is within 1.2. Fallback when the map is not loaded (tests, Unity export): coastal = the city has a sea link (`G.seaLinks`); water side = towards the sea-link partner.
- Inland cities without water get a random (key-hashed) mirror east ↔ west for variety; the gate keeps facing the side of the city's most important road link (towards the linked city with the largest population).
- **Relief**: the ground is a 64 × 64 vertex grid with a gentle tilt following the map's slope at the city, low hills where the map has forest or hills nearby, a central hill for `han3` and `north` (6–10 m; the walls follow it), a river channel along or across the field ring when a river is near, and the shore with sand or rock on the water side.
- **Biome colour**: grass, steppe, desert, tropical and snow palettes for the field ring and the background hills (warm-top / cool-teal ramp for foliage); farms use the family's crop (millet and wheat in the north, rice south of 32° N and in 南海 / 南亚, vines and alfalfa in the oases, dates in Mesopotamia and Arabia, olives and wheat around the Mediterranean, barley and pasture in the north).

### 6.3 Streets and lots filling

- The main street (and 罗马's cross street) is paved from S3 of 民居 (earth → gravel → stone slabs at S4 → stone with lantern posts at S5).
- Houses fill lots in order; the number shown = the 民居 count for the current stage and device (§4.1). Each house picks one of 6 family variants (hash of the lot), its storeys by stage (S1–S3: 1, S4: 1–2, S5: 2–3 on the main street).
- Fields fill tiles in order; the number of tiles shown grows with 农田's stage (10 / 16 / 24 / 32 / 40), and S3+ adds the channel network along the tile rows from the water side.

### 6.4 Capitals and the ruler

The faction's capital (the ruler's city) gets the palace variant of the family (the v3 capital models are the reference: Han 宫城 with golden roofs, Roman amphitheatre, Persian great iwan, Kushan great stupa, steppe golden tent …), the ruler's banners on the gate and the palace, and two guards at the palace gate. When the ruler leaves, the banners go with him.

### 6.5 Landmarks

One per famous city, ≤ 3 000 triangles, placed in the landmark slot (outside the walls) or replacing a building's S5 look. Liberties are marked.

| City | Landmark | Note |
|---|---|---|
| 洛阳 | 白马寺 (temple), 太学 with the 熹平石经 steles (academy S5), 灵台 observatory | the steles 175–183; Dong Zhuo burned the city in 190–191 — if a scenario event models it, the landmarks show as ruins |
| 长安 | 未央宫 hall on its platform (palace S5) | the Han palace used by the court 190–195 |
| 成都 | 锦官城 brocade workshops (workshop skin) | 蜀锦 |
| 宛 | 南阳铁官 ironworks with the water-powered bellows (workshop skin) | 杜诗, 31 AD |
| 国内城 | the mountain fortress and stone-piled tombs | 三国志·东夷传 |
| 邪马台 | the great raised ritual hall and towers | Yoshinogari / 倭人传 |
| 金官 | iron-ingot stacks at the market | 弁辰 iron |
| 于阗 | jade from the river at the market | |
| 大宛 | the heavenly-horse stables | links to ITEMS 大宛天马 |
| 富楼沙 | the great stupa of Kanishka | 2nd century |
| 塔克西拉 | the Dharmarajika stupa; the Sirkap grid | |
| 华氏城 | the timber palisade with towers | Megasthenes; a liberty for 190 |
| 阿努拉陀 | the Ruwanwelisaya dagoba and a reservoir tank | 2nd century BC |
| 穆济里斯 | Roman merchant ships at the quay | the Muziris trade |
| 泰西封 | the Parthian palace with its iwan court | no Taq Kasra (Sasanian) |
| 尼萨 | the square hall of the royal citadel | Old Nisa |
| 哈特拉 | the Great Temple with its iwans; round walls | |
| 帕尔米拉 | the Temple of Bel and the great colonnade | dedicated 32 AD |
| 马里卜 | the great dam and sluices (farms S5) | |
| 阿克苏姆 | a field of rough stelae | the great carved stelae (4th c.) are not shown |
| 麦罗埃 | the royal pyramids and the Lion Temple | |
| 罗马 | the Colosseum and the Pantheon | 80 AD; c. 126 AD |
| 亚历山大 | the Pharos lighthouse at the harbour | |
| 以弗所 | the Library of Celsus and the Temple of Artemis | library c. 117–135 |
| 迦太基 | the Antonine Baths by the sea | 145–162 |
| 伦迪尼乌姆 | the basilica-forum | c. 100 AD |
| 安条克 | the colonnaded main street | |
| 喀里多尼亚 | a broch tower | Iron Age |
| 塔拉 | the royal earthworks on the hill | |

### 6.6 Time of day, seasons, weather, festivals

- **Clock** (`SG.Clock.hour()`, the one day clock shared with the map light and the shell, INTEGRATION §10.7): in the player's command phase the city's hour is `7 + 13 × tokensUsed / tokensTotal` (07:00 at the month's start, 20:00 when the last token is used); during the AI phase and the news it is 20:30. Mood keys (`SG.LP.Mood.set` with hours): 07–09 golden morning, 10–15 noon, 15–18 golden, 18–19.5 dusk, ≥ 19.5 night with lantern pools (palace gate, tavern, market, temple, main-street posts every 12 m). Viewing a foreign city uses the current hour. A small sun-dial glyph in the dock shows the hour.
- **Seasons** from the month and the city's latitude: spring (2–4) blossom trees (peach and plum in 华夏 and 三韩, almond in 安息 and 罗马), summer (5–7) lush, autumn (8–10) golden fields and red maples, winter (11–1) snow north of 32° N and in the steppe, bare trees; tropics (latitude < 23.5°) stay green with monsoon rain showers in months 6–9 (light rain particles, wet ground tint). Month 7 fields are golden (the harvest, DOMESTIC-FX §9.1).
- **Weather**: decorative, deterministic per (city, month): drizzle, snow, morning mist, a dusty haze in the oases and Arabia. Never at a vignette's Reveal (the director clears it for 2 s).
- **Festivals** (decor and crowd only; no rule effect unless another spec adds one): 华夏 庙会 in month 3 (ITEMS §14.2 market festival: lanterns and extra stalls), 高句丽 东盟 in month 10 (「以十月祭天，国中大会，名曰东盟」), 鲜卑 spring gathering on the 饶乐水 in month 3 (后汉书·鲜卑传), 罗马 Saturnalia in month 12, 安息 Nowruz in month 3.

---

## 7. Domestic vignettes inside the city view

### 7.1 Anchors

Each placed building exposes an **anchor**: `{ id, pos, radius, front (yaw of its main face), spots: { door, yard, roof, side }, path }`. Spots are named points where actors stand (the farm's furrow line, the forge's anvil, the barracks yard, the reviewing platform, the tavern door, the palace steps, the gate inside and outside). `gate.in` and `gate.out` are anchors on the two sides of the main gate; `cottage` is the 草庐 spot in the field ring for 搜索.

### 7.2 Standard shots

| Shot | Target | Pitch | Distance | Yaw |
|---|---|---|---|---|
| `est` | plot centre | 50° | 120 m | the player's yaw (or the template's south) |
| `bldg(id)` | anchor pos + 0.4 × height | 36–40° | 3.2 × radius + 10 m | anchor front + 25° |
| `close(id)` | the action spot | 22–30° | 1.6 × radius + 6 m | anchor front + 15° |
| `gate.in` / `gate.out` | the gate | 26° | 28 m | looking out of / into the city |
| `track(actor)` | follows an actor with 0.4 s lag | 28° | 18 m | behind-left of the actor |
| `crane(id)` | from `close` to `bldg` | +18° | +15 % | orbit +25° (great tier) |

Camera moves use ease-in-out cubic, max 60°/s yaw and 45°/s pitch, one move per beat (DOMESTIC-FX §3.9). After a vignette the camera returns to where the player had it (0.45 s), unless the player moved it during the vignette (then it stays).

### 7.3 Director API (`SG.CityView.director(cityId)`, used by `js/domestic-fx.js`)

```
d.anchor(id) → anchor | null                d.shot(kind, id?, opts?) → Promise           d.returnCamera() → Promise
d.actor(kind, opts) → Actor                 kind: 'general' (opts.gen, uses the general's figure and look), 'worker', 'farmer',
                                            'smith', 'soldier' (opts.color), 'recruit', 'envoy' (opts.culture, opts.color), 'merchant',
                                            'ox', 'oxTeam', 'horse', 'camel', 'elephant', 'cart', 'boat'
actor.walk(path | spotName, speed?) → Promise   actor.pose(name, dur?) → Promise   actor.face(target)   actor.remove()
d.crowd(kind, n, area, opts) → Group        instanced block (soldier formations, recruit columns)
d.path(fromSpot, toSpot) → polyline along the streets
d.tiles('farms', k) → the next k field tiles (the ones that sprout)     d.lots(k) → the next k house lots (the ones that rise)
d.grow(id, toStage, opts?) → Promise        the stage-up animation (§4.4)
d.spawn(prop, at) → handle                  'cottage', 'jar', 'sacks', 'ingots', 'tallyBoard', 'priceBoard' …
d.burst(pos, preset) / d.light(pos, color, dur) / d.mood(key | hour, dur) / d.weatherClear(sec)
d.at(t, fn) / d.wait(sec)                   timeline helpers on the vignette clock (scaled by the sting, DOMESTIC-FX §3.4)
d.skipToEnd()                               jump every tween to its end state
d.end()                                     remove everything spawned, restore mood and weather
```

Poses needed from `figure.js` (art pilot skeleton): `idle`, `walk`, `run`, `command` (point), `bow` (拱手), `kneel`, `shrug`, `raiseWeapon`, `strike` (smith / drill), `thrust` (drill), `carry` (porter), `ride` (on a horse), `cheer`, `sit`. Figures in vignettes use the art pilot's general at its mid LOD (≤ 4 k triangles) for named generals and the soldier / villager kit (≤ 300 triangles, instanced) for everyone else.

### 7.4 Command → stage map

| Command | Anchor(s) | First shot | Notes |
|---|---|---|---|
| 开发·土地 | farms | `bldg('farms')` | sprouting tiles = `d.tiles('farms', k)` |
| 开发·产业 | workshop | `close('workshop')` | the anvil spot |
| 开发·町 | houses | `bldg('houses')` over the main street | rising houses = `d.lots(k)` |
| 征兵 | gate.in → barracks | `gate.in` then `track` | column via `d.path('gate.in', 'barracks.yard')` |
| 训练 | drill | `bldg('drill')` | formation block `d.crowd('soldier', …)` |
| 搜索 | tavern → gate → (cottage / willow / granary) | `bldg('tavern')` street level | the only branching vignette |
| 登用 | palace guest hall (or cottage, or courtyard for captives) | `close('palace')` | |
| 移动 | gate.out | `gate.out` | then the world-map march |
| 输送 | granary or treasury → gate.out | `bldg('granary')` | then the cart march |
| 同盟 | palace → gate | `close('palace')` | the foreign envoy wears the partner's culture costume |
| 离间 / 拉拢 | (card stage over the dimmed view) | — | the subject is in another city |
| 出征 | gate.out | `gate.out` | the tiger tally overlay |
| 赏赐 | palace (or treasury) | `close('palace')` | |
| 交易 | market grain stalls → granary | `close('market')` | |
| 任命 | palace front steps and plaque | `close('palace')` | |
| 市场 | shop | `close('shop')` | ITEMS reveal; the item then stands on the shop's rack |
| 授官 | palace hall | `close('palace')` | TITLES |
| 秋收 | farms → granary | `bldg('farms')` | DOMESTIC-FX §9.1 |

---

## 8. Life and actors

| Element | Count (desktop / phone) | Driver | Look |
|---|---|---|---|
| Crowd walkers | 10 / 18 / 28 / 40 / 56 by population steps of 200 k (phone 6 / 10 / 14 / 18 / 24) | population | culture costumes (§5), some carrying baskets, poles, jars; walk along street splines between door spots; stop at stalls; at night half go home and a few carry lanterns |
| Soldiers | 1 per 1 000 troops in the city, max 24 / 12 | troops | faction-colour tunics; patrol the walls (2), stand at the gate (2), drill on the 校场 when training < 100 |
| Officers | up to 8 / 4 named generals | `officersIn(city)` | stand in the palace courtyard; the ruler on the steps; tap → character menu (request 9) |
| Draft and farm animals | oxen 1 per 农田 stage; pigs and chickens in courtyards (华夏) | 农田 / 民居 | grazing, ploughing loops |
| Mounts | horses (马厩 stage × 2 + equipped mounts), camels (oasis, arabia, nile), elephants (indic 南亚, nanhai, 南中) | 马厩 | idle and paddock-walk loops; named mounts (ITEMS) in their own stalls |
| Herds (草原) | 6 / 12 / 20 / 30 / 40 animals | 农田 stage (= pasture) | sheep, horses, cattle with mounted herders |
| Smoke | 1–4 plumes | 工坊 stage, 民居 at dusk (kitchen smoke) | soft-lump puffs rising and fading, warm-lit at night |
| Boats | 1 + harbour stage (+1 per sea link, max 8) | 港 | family hulls: sampans and 楼船, dugouts, outriggers and Kunlun ships, Roman merchantmen |
| Birds | egrets over paddies, crows on walls, hawks over the steppe, gulls at ports | biome | small flocks on looping paths |

All crowd, soldiers and animals are GPU-instanced with vertex-shader animation (per-instance phase), one draw call per kind (+ its outline shell).

---

## 9. Rendering and performance

### 9.1 Scene

- A full-screen screen on the existing stack: `SG.Gfx.pushScreen({ scene, camera, update, render, resize })` with its own `THREE.Scene` and `PerspectiveCamera` (34°, near 0.5, far 900), the shared renderer. Materials: `SG.LP.material()` and `SG.LP.outlineMaterial()` (ink shells) on buildings, props and figures; terrain, crops, grass and particles without outlines. Lighting: `new SG.LP.Mood(scene, { center: [0, 0, 0], extent: 85, shadow: true, shadowSize })` with `mood.set(hour)` and `mood.addLamp(...)` for the lantern pools. Fog: linear, warm, matching the Mood.
- **Background**: a ring of low-poly hills and the family's distant trees beyond the plot (one merged mesh, fogged), and the sky dome tinted by the Mood.
- **Water**: flat-shaded stylized water (Lanternvale look: vertex-coloured shallows, foam line at the shore, slow wave displacement in the vertex shader), one mesh.

### 9.2 Geometry library and caching

- `SG.CityKit.build(family, skin, building, stage, variant)` → `BufferGeometry` built with `SG.LP.MeshBuilder`, cached in `SG.LP.MeshCache` under `family|skin|building|stage|variant`. The library is shared by all cities of a family: a city scene is mostly instances and transforms of cached geometry.
- Every recipe returns `{ geometry, parts: [{ stage, range }] }` (vertex ranges tagged with the stage each part appears at) so `director.grow` can animate the new parts without separate meshes (a per-vertex `stageAt` attribute and a uniform `growT`).
- **Static merge**: per city, the static parts of the buildings of one district (core, market, residential, military, craft, sacred, outside) are merged into one mesh each (≤ 8 meshes + their outline shells). Houses, crops, trees, props, crowd and animals are instanced.
- **LOD**: `full` (desktop city view) and `medium` (phone city view and the DOMESTIC-FX diorama): houses −40 %, props −50 %, crowd −55 %, no props under 150 triangles, shadow map 1024, outline shells only on buildings and named figures.

### 9.3 Build and idle pre-build

- Building a city scene: ≤ 120 ms of work on desktop, run as a generator in ≤ 8 ms slices (like `map-view.js`'s chunked build). First use of a family additionally builds its geometry library lazily (≤ 300 ms total, sliced, cached for the session).
- When the player selects an own city on the map, its scene is built in `requestIdleCallback` slices (fallback `setTimeout` 0 with a time check), so 「入城」 and the DOMESTIC-FX diorama open without waiting. An LRU keeps 2 built city scenes (3 on desktop with ≥ 8 GB device memory hint); others are disposed (`geometry.dispose` only for per-city merged meshes; the family library stays).
- Rebuild on change: a stage change rebuilds only the affected district's merged mesh (≤ 15 ms, sliced) or swaps instances.

### 9.4 Quality tiers (`SG.Quality.tier`)

Set by `settings.quality` ('auto' by default): 'auto' picks `high` on desktop GPUs, `medium` on iPad / iPhone class (touch-primary devices or `devicePixelRatio ≥ 3` with a small screen), and drops to `low` if the city view's median frame time over 60 frames exceeds 40 ms (and tells the player once). `low` disables the city view's shadows and crowd and uses the card stage for vignettes; the city view itself still opens.

### 9.5 Budgets

| Item | Desktop | Phone (iPhone class) |
|---|---|---|
| One big building, any stage | ≤ 3 000 triangles (before outline shells) | same geometry, medium LOD drops small props |
| One small prop / house variant / crowd figure | ≤ 400 / ≤ 400 / ≤ 300 | ≤ 400 / ≤ 400 / ≤ 260 |
| One ENGAGEMENT project (scaffold, half shell or finished, ENGAGEMENT §8.4) | ≤ 900 triangles; the wonder ≤ 2 000 | medium LOD: half (≤ 450 / ≤ 1 000) |
| Scene triangles drawn incl. outline shells | ≤ 250 k (estimate: start median 92 k, all-S5 capital 193 k, **with all 13 projects 216 k**) | ≤ 150 k (start median 59 k, all-S5 capital 119 k, **with all 13 projects 129 k**) |
| Draw calls | ≤ 90 | ≤ 55 |
| Point lights (lantern pools) | ≤ 12 active (nearest to the camera target), the rest emissive + halo sprites | ≤ 6 |
| Shadow map | 2048, fitted to the plot | 1024 |
| JS per frame (camera, life, labels) | ≤ 4 ms | ≤ 6 ms |
| Frame rate | 60 fps | 30 fps minimum |
| Build | ≤ 120 ms total, ≤ 8 ms per slice; no synchronous stall > 50 ms | same |
| Geometry memory | ≤ 30 MB (family libraries + 2–3 city scenes) | ≤ 18 MB |
| Textures | none (vertex colours); labels and chips are DOM | — |

The estimates come from `domestic/city-stages.js` (`triEstimate(st, ctx, tier)`): per-building stage budgets + houses + props (40–120) + crowd + trees (30) + terrain (9 k) + capital extras + **`ctx.projects`** (900 each, wonder 2 000, halved on the phone tier), × (1 + outlined share 0.8 desktop / 0.6 phone). `node design-v4/domestic/city-stages.js` checks the all-S5 capital with every project against both budgets (ALL OK).

---

## 10. Camera controls

| Input | Action |
|---|---|
| Drag (mouse left / one finger) | pan along the ground (same feel as the world map's `CameraRig`), inertia τ = 0.18 s |
| Wheel / pinch | zoom 16–150 m (phone 20–150 m); pitch follows distance (30° at 16 m → 58° at 150 m), like the map's pitch-by-distance |
| Q / E, right-drag, two-finger twist | rotate (yaw, free 360°) |
| R / F, two-finger vertical drag | tilt ±10° around the automatic pitch |
| W A S D / arrows | pan |
| Tap / click a building | select: gold outline highlight (outline shell colour `--gold`, 2.8 px) + building chip (§2.4) |
| Double-tap / double-click | focus: `bldg(id)` shot (0.6 s) |
| Home / 「⌂」 button | reset to `est` |
| Esc / 「出城」 / long pinch-out past 150 m | exit (§2.3) |
| Idle 25 s | slow auto-orbit 3°/s around the target; any input stops it |

- Target bounds: |x|, |z| ≤ 70 m with a 6 m rubber band. The camera never goes below 6 m above the ground or inside a building (a sphere cast of radius 2 m against building bounds pushes it out).
- Touch: gestures are recognized like the map's (`camera.js` thresholds, `DRAG_THRESHOLD`); a tap is a press without drag; two-finger twist uses the angle between the touches (dead zone 6°).
- Keyboard focus: Tab cycles through the buildings in a fixed order (palace, market, …) and selects them; Enter opens the first context command.
- Reduced motion: no auto-orbit, camera moves are 50 % shorter and use cuts with a 0.2 s fade instead of long flights.

---

## 11. Module plan and integration points

| File | Status | Content |
|---|---|---|
| `js/city-layout.js` | new | pure logic (Node-testable): `FAMILY`, skins (`CITY_SKIN` table), `BUILDINGS`, `stageOf / stages / cityLevel` (port of `domestic/city-stages.js`; the five core thresholds, hysteresis and hubs are read from `SG.Balance.CityLevel`, INTEGRATION §12), `TEMPLATES`, `resolve / lots / tiles / layout` (port of `domestic/city-templates.js`), landmarks table, water side from a terrain sampler function or sea links |
| `js/city-kit.js` | new | building recipes per family × skin × stage with `SG.LP.MeshBuilder`, part tags, house variants, props, the 草庐, landmark recipes (lazy), crop and herd instances |
| `js/city-life.js` | new | crowd and animals (instanced, vertex animation), smoke, boats, birds, seasons, weather, clock → Mood, festivals |
| `js/city-view.js` | new | the screen (push / pop, transitions), camera controller, input, building picking (bounds raycast), dock and chips (DOM), labels, scaffold markers, the director API (§7.3), idle pre-build and LRU, quality tier, diorama rendering helper `renderInto(rect)` for DOMESTIC-FX |
| `tests/city-layout.js` | new | Node checks (§13.1) |
| `tests/city-kit.html` | new | gallery: every family × skin × building × stage, rotating, triangle counts with budget colours, outline on / off |
| `tests/city-view.html` | new | fly-through of one city per family plus 洛阳, 罗马, 亚历山大, 帕尔米拉, 麦罗埃; time-of-day, season and quality sliders; performance HUD (`renderer.info`) |
| `js/strategy-screen.js` | modified | 「入城」 button in the city panel header; double-tap on the map city; the `⤵ 入城` chip; the dock layout when the city view is open; `selectCity` triggers idle pre-build |
| `js/map-view.js` | modified (read-only API use) | `terrainInfo` is already public; add `cityFocusPoint(cityId)` for the fly-down if needed |
| `js/camera.js` | modified | export the gesture recognizer so the city view's controller reuses it (or copy it into `city-view.js`) |
| `js/model.js` / save | modified | `G.fxStages` (DOMESTIC-FX §10.3); the last-siege month per city for the scars (§4.4) if not already derivable from the log |
| `index.html` | modified | load `city-layout.js` after `model.js`; `city-kit.js`, `city-life.js`, `city-view.js` after `lowpoly.js` / `lp-pilot.js` and `figure.js` |

`js/culture-art.js` stays the world-map city model (far and near map LODs); the city view does not reuse its geometry, but its palettes and capital silhouettes are the starting point of each family's recipes so the map model and the city view read as the same city.

---

## 12. Unity port (after the browser build is stable)

- `CityLayout.cs`: pure port; the Node checks export fixtures (every city's layout and stage vector at game start) as JSON and the C# harness compares them (like `Tests~/battlemap`).
- `CityKit.cs`: the Lanternvale toolchain is native to Unity (`lowpoly.js` is a port of Lanternvale's `MeshBuilder.cs`, `Materials3D.cs`, `SceneLighting.cs` and its shaders), so the recipes port to C# on the original classes; the outline and low-poly shaders are the originals.
- `CityLife.cs`: `Graphics.DrawMeshInstanced` (or `RenderMeshInstanced`) with the same vertex animation.
- `CityView.cs`: a separate scene or an additive scene loaded on entry, its own camera rig, UGUI dock; the diorama is a second camera rendering into a `RenderTexture` shown in the DOMESTIC-FX scroll frame.
- Budgets are the same; on mobile Unity the outline shells may be replaced by the Lanternvale screen-space outline if it is cheaper there.

---

## 13. Test plan and targets

### 13.1 Node: `tests/city-layout.js`

1. Templates (port of `domestic/city-templates.js` checks): for each family and each of the 4 rotations: every slot placed (no "no room"), no overlaps (1 m clearance), inside slots inside the walls and off the streets, outside slots outside the walls and inside the plot, house lots ≥ 46, field tiles ≥ 40. Current: ALL OK.
2. Every city of both scenarios (38 + 196): `layout()` succeeds, is deterministic (same inputs → same JSON), harbour present iff coastal or river (with the sea-link fallback), the landmark only in its city, capitals get the capital variant.
3. Stages (port of `domestic/city-stages.js`): thresholds and hysteresis cases (value at 0.95 × threshold keeps the stage, below drops it), the start histograms of §4.1 reproduced exactly, budgets (big ≤ 3 000, small ≤ 400, scene estimate ≤ budget for an all-S5 capital).
4. No rules impact: loading the city modules does not change `tests/sim.js` results (state hash equal).

### 13.2 Browser: `tests/city-kit.html`

- Each family × skin × building × stage: triangle count ≤ budget (big 3 000, small 400, houses 400, figures 300), parts tags cover all vertices, no NaN normals, outline shell present on buildings and props, none on terrain and crops; screenshot gallery for art review against the pilot (`tests/lowpoly.html`).

### 13.3 Browser: `tests/city-view.html`

- For one city per family plus 洛阳, 罗马, 亚历山大, 帕尔米拉, 麦罗埃 at start stats and at all-S5: build time (total ≤ 120 ms desktop, max slice ≤ 8 ms), `renderer.info` triangles and calls (≤ the §9.5 budgets), median and p95 frame time over a scripted 20 s fly-through (desktop ≤ 16.7 ms p95; phone emulation at 4× CPU throttle ≤ 33 ms), memory after entering and leaving 10 cities in a row (no growth beyond the LRU), no `console.error` / `pageerror`.
- Transitions: enter / exit with no frame > 50 ms; the wipe never shows an empty frame.
- Time of day 07:00 → 20:30 and the four seasons in one city per family: screenshots; lantern pools appear only at dusk and night.
- Input: tap selects the right building (raycast against bounds) at 1280 × 720 and 844 × 390; gestures; keyboard Tab order.

### 13.4 Fun and readability targets (playtests)

- 4 of 5 testers can say, from the city view alone, which of 土地 / 产业 / 町 is the city's weakest after one minute of looking (the "readable growth" goal).
- An active domestic player sees a building stage-up at least every 3 months on average (measured in the DOMESTIC-FX Monte-Carlo: ≈ 11 % of 开发 commands, ~4 develops a month → one every ~2.3 months).
- Players open the city view voluntarily at least once every 5 months of play after the first hour (telemetry-free: a local counter in the settings object for playtest builds).

---

## 14. Open questions

1. ~~Commands in the city view on phones~~ — resolved: one scrollable bottom row of the 16 tiles, grouped by the four group stripes and snapping per group (§2.4, UI-V4 §5.9); building chips keep their two context commands.
2. **Scars and Dong Zhuo's burning of 洛阳.** The scars need the last-siege month per city; if the integration does not want a save field, derive it from `G.log` entries. Modelling the 190–191 burning of 洛阳 as a scenario event would make the landmark's ruins state meaningful; that is a scenario decision, not this spec's.
3. **Enemy cities' interiors.** The game shows enemy stats openly, so the full city view is consistent; a 「远望」 (outside-only, misted interior) mode for enemy cities is cheap to add if the scouting design of another task wants it.
4. **Skins per city.** The `CITY_SKIN` table (e.g. 潘提卡彭 as a Greek city in the roman family, 柳城 / 宁城 in 华夏) is short now; more exceptions will appear during art review.
5. **Two upgrades at once.** 开发·町 can raise both 民居 and 城墙 in one command; the chained 0.5 s gap may feel long in 快速 mode — maybe show only the bigger one there.
6. **Landmark list.** 29 landmarks need 29 bespoke models (≤ 3 000 triangles each); the art pilot may want to phase them (capitals first).

---

## 15. Sources

City layouts, buildings and the historical details behind the families and landmarks:

- Han granary and watchtower tomb models: [The Met, Granary Tower](https://www.metmuseum.org/art/collection/search/52010); [Toledo Museum, granary mingqi](https://emuseum.toledomuseum.org/objects/60543/mingqi-spirit-good-in-the-form-of-a-granary); [UMMA, model of a granary](https://umma.umich.edu/objects/model-of-a-granary-1993-1-76); [Ackland, model of a granary](https://ackland.emuseum.com/objects/22097/model-of-a-granary); [The Met, Multistoried Watchtower with Front Court](https://metmuseum.org/art/collection/search/49520); [Minneapolis Institute of Art, model of a watchtower](https://collections.artsmia.org/art/62040/model-of-a-watchtower-china); [Freer Gallery, tomb model of a watch tower](https://asia.si.edu/explore-art-culture/collections/search/edanmdm:fsg_F1907.68a-d); Jiazuo manor model: [Wikimedia Commons file page](https://zhwiki.oracleblog.org/zh-hant/File:Pottery_tower_6a.JPG).
- Han farming and industry: [Louche — Wikipedia](https://en.wikipedia.org/wiki/Louche); [Du Shi — Wikipedia](https://en.wikipedia.org/wiki/Du_Shi); [中国数字科技馆 · 翻车](https://www.cdstm.cn/theme/nytd/nyjx/201407/t20140710_462032.html); [中国数字科技馆 · 水排](https://www.cdstm.cn/theme/nytd/nyjx/201702/t20170215_462033.html).
- 熹平石经 at the 太学 (175–183): [儒家网](https://www.rujiazg.com/article/24308); [汉典 · 熹平石經](https://www.zdic.net/hant/熹平石經).
- 高句丽 (桴京, 大屋祭鬼神, 东盟, 小马, 积石为封) — 三国志·魏书·东夷传: [5000yan 三国志 · 东夷传](https://sanguozhi.5000yan.com/47154.html); Goguryeo tomb murals (kitchen, storehouse, stable): [Anak Tomb No. 3 — Wikipedia](https://en.wikipedia.org/wiki/Anak_Tomb_No._3); [Northeast Asian History Foundation, 안악3호분벽화](https://contents.nahf.or.kr/goguryeo/mobile/html/03_vod.html).
- 倭 (宫室楼观城栅, 国国有市, 大倭, 邸阁, no horses): [中國哲學書電子化計劃 · 三國志 · 倭人傳](https://ctext.org/text.pl?node=603372&if=gb&remap=gb); [青空文庫 · 魏志倭人傳](https://www.aozora.gr.jp/cards/001477/files/50926_41514.html); [Wajinden — Wikipedia](https://en.wikipedia.org/wiki/Wajinden); Yoshinogari: [Yoshinogari site — Wikipedia](https://en.wikipedia.org/wiki/Yoshinogari_site); [Yoshinogari Historical Park, moat-encircled village](https://yoshinogari.jp/en/information/zone/moat-encircled-village).
- Steppe settlements: [Ivolga archaeological site — Wikipedia](https://en.wikipedia.org/wiki/Ivolga_archaeological_site); [University of Washington Silk Road, Xiongnu archaeology](https://depts.washington.edu/silkroad/archaeology/mongolia/xiongnu/xiongnuarchhist/xiongnuarchhist.html).
- Gandhara grid city: [Sirkap — Wikipedia](https://en.wikipedia.org/wiki/Sirkap); [Livius, Taxila: Sirkap's sanctuaries](https://www.livius.org/articles/place/taxila/taxila-photos/taxila-sirkap-s-sanctuaries/).
- Pataliputra's palisade: [Bulandi Bagh — Wikipedia](https://en.wikipedia.org/wiki/Bulandi_Bagh); [Arrian on Indian rivers and cities](https://sententiaeantiquae.com/tag/pataliputra/).
- Parthian iwan and Nisa: [Halle University, Parthian architecture](https://www.orientarch.uni-halle.de/teach/archit/parther.htm); [Brewminate, art and architecture of the Parthian empire](https://brewminate.com/art-and-architecture-in-the-ancient-parthian-empire/).
- Roman granaries: [Horreum — Wikipedia](https://en.wikipedia.org/wiki/Horreum); [Ostia-antica.org, horrea](https://ostia-antica.org/dict/topics/horrea/intro.htm); [Housesteads granaries](https://odysseyadventures.ca/articles/hadrian-wall/article_hadrianswall-housesteads-hor.htm).
- Kush and Aksum: [UNESCO, Island of Meroe](https://whc.unesco.org/document/152412); [Brighton, ironworking remains at Meroe](https://research.brighton.ac.uk/en/publications/the-ironworking-remains-in-the-royal-city-of-meroe-new-insights-o/); [Humphris et al., early iron production at Meroe (UCL)](https://discovery-pp.ucl.ac.uk/10042893/9/Humphris%20et%20al.%20JFA%20approved.pdf); [Obelisk of Axum — Wikipedia](https://en.wikipedia.org/wiki/Obelisk_of_Axum); [SFU Museum, Aksumite stelae](https://www.sfu.ca/archaeology/museum/exhibits/virtual-exhibits/aksum/aksumite-stelae.html).
- Funan / Óc Eo: [Oc Eo — Wikipedia](https://en.wikipedia.org/wiki/Oc_Eo); [Nguyen Hoang Bach Linh, early Óc Eo dwellings (Kanazawa)](https://isac.w3.kanazawa-u.ac.jp/publication/doc/accr2-2_Nguyen.pdf); [Óc Eo – Ba Thê (tentative World Heritage)](https://worldheritagesite.org/tentative/id/6572).
- Festivals: 东盟 is in the 高句丽 section of 三国志·东夷传 (above); the 鲜卑 spring gathering on the 饶乐水 is quoted from memory of 后汉书·乌桓鲜卑列传 and must be checked against the text before the festival ships.
- Landmarks well known from standard references (Colosseum, Pantheon, Pharos, Library of Celsus, Antonine Baths, Temple of Bel, Hatra, Kanishka stupa, Dharmarajika, Ruwanwelisaya, Marib dam, Hill of Tara, brochs, 白马寺, 未央宫, 灵台) follow their Wikipedia articles; dates are given in §6.5 where they matter for 190 AD.
