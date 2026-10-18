# 三国志-霸王的世界 · 第四版界面设计（UI-V4）— 漆 · 纸 · 铜 · 灯

This spec covers the player's v4 requests **1** (menu and settings buttons on the main interface), **9** (a Generals button and a character menu per general), **11** (the main interface looks ugly and "like an AI game": make it animated, attractive, with properly designed buttons), and the **screens** of requests **3** (shops and equipment), **10** (titles) and **14** (武林大会). It also sets the shared look, motion and UI sound for every other v4 screen.

Read first: `DESIGN-V2.md` §1 (common rules: classic scripts + IIFE, no network, everything generated in code, Chinese UI text, ≥ 44 px touch targets, 844×390 / 1280×720 / 1920×1080 / 1024×768, 8 ms JS per frame, no `pageerror`), `DESIGN-V3.md`, `CONTRACT.md`, `UI-CLASSES.md` (the `SG.UI` contract and `sg-*` class names), and the sibling v4 specs: `DOMESTIC-FX.md` (command icons, command buttons §6–§7, vignettes, the 「内政演出」 setting), `ITEMS.md` (§5.3–§5.5 icons and rarity, §6.7 shop flow, §14.1 the 装备 tab API), `TITLES.md` (§9 官制 board, grant flow, ceremony, 官爵 tab API), `COMBAT-V4.md` (§5.3 duel settings, §12.1 the 武艺 tab content), `ENGAGEMENT.md` (§16 the top-bar widgets and menu entries it asks for), `AUDIO-V4.md` (§B10.4 audio settings rows, voice cues), `CITY-VIEW.md` (§2 入城 and the dock, §9.4 quality tiers, §10 camera) and the tournament spec (`design-v4/TOURNAMENT.md`, being written in parallel; this file follows its decisions as of its progress log: single elimination, draw size 2^⌈log₂N⌉ between 8 and 128 with byes, final best-of-three plus a third-place match, the 平乐观 stadium with sixteen landmark façades).

Precedence: **`INTEGRATION.md`** wins for cross-system rules (the settings and icon registries' full row lists, voice cues, the shared day clock, the month-start time budget, the test matrix). Otherwise this file wins for **how things look, move and sound in the shell** (top bar, city panel layout, dock layout, menu, settings, generals list, character menu frame, the visual skin of every screen) and for the component library. The sibling specs win for **what their screens contain** (ITEMS for items, TITLES for offices, COMBAT for the 武艺 content, ENGAGEMENT for its widgets, the tournament spec for brackets and prizes). Platform order: browser first, then the Unity port (§18). Every visual assumes the v4 art direction (Lanternvale: stylized low-poly, cozy Ghibli-warm, flat-shaded props, soft-lump foliage with a warm-top / cool-teal ramp, ink outlines, soft painted light with warm lantern pools at night).

**Deliverables of this task**

| Path | What |
|---|---|
| `design-v4/UI-V4.md` | this spec |
| `design-v4/ui/current/*.png` | 22 screenshots of the v3 interface (desktop 1280×720 and phone 844×390, seed 7), taken by `design-v4/ui/shoot-current.mjs` |
| `design-v4/mockups/*.html` | 10 self-contained HTML/SVG mockups built from a shared kit (`kit.css`, `kit.js`); they load the game's own `js/portrait.js` for real portraits and the art pilot's Guan Yu render as the 3D preview stand-in |
| `design-v4/mockups/shots/*.png` | 30 screenshots of the mockups (desktop and phone), made by `node design-v4/mockups/shoot.mjs` |

The mockups are design documents, not game code: they use `rem` exactly like the game (`html { font-size: clamp(13px, .5vw + 1.333vh, 24px) }`), so their proportions carry over. The headless screenshots use the OFL font *LXGW WenKai* (installed only on the screenshot machine) as a stand-in for the system Kai fonts (STKaiti / KaiTi) that Mac, iOS and Windows players have; nothing in the repo loads it (§3.5, Q1).

---

## 0. Key numbers at a glance

| Item | Value |
|---|---|
| Visual language | **漆 · 纸 · 铜 · 灯**: lacquered-wood frames hold paper pages; bronze discs are the icon buttons; lantern light marks hover, focus and selection; vermillion seals confirm; ink outlines (2 px `#241a21`, the same colour as the 3D outline pass) bound everything |
| Palette | 59 colour tokens in 15 groups (§3.2); every text colour ≥ 4.5 : 1 against its surface (measured: ink on paper 13.7, paper on lacquer 14.1, family/rarity text tokens 5.0–5.6) |
| Fonts | system Kai for display (`STKaiti, Kaiti SC, KaiTi, BiauKai, …`), system sans for body, a tabular old-style serif for numbers; minimum rendered text 13 px on phones, 12.5 px (captions only) on desktop |
| Main interface (1280×720) | top beam 58 px (+6 px painted band), city panel 376 px on the right, command dock 4 × 4 tiles (治 · 人 · 运 · 策) inside it, agenda column 238 px on the left (≤ 4 slips), end-month seal 84 px |
| Phone (844×390) | top beam 44 px with all 6 bronze buttons (44 px), city panel 476 px with two columns (ledger 214 px + dock 4 × 4 tiles 56 px), end-month seal 62 px bottom-left |
| Commands | 16 tiles in 4 groups: 治 开发 营建 征兵 训练 · 人 搜索 登用 赏赐 任命 · 运 移动 输送 交易 市场 · 策 通商 探索 外交 出征; hotkeys `1–9 0 - =` `Z X C V` |
| Top-bar buttons | 武将 · 官制 · 势力 · 大事 · 设置 · 菜单 (+ the 结束本月 seal); badges are red-wax dots |
| Menu | 7 entries: 继续 · 记录 · 读取 · 卷宗 · 设置 · 操作说明 · 返回标题; **4 save slots** (月初自动 + 3 manual), each with a map thumbnail and ruler portrait |
| Settings | 6 tabs (音频 · 画面 · 游戏 · 镜头 · 操作 · 辅助), 53 rows (INTEGRATION §10.1); one registry `SG.Settings`, each owner keeps its own `localStorage` key |
| Character menu | 8 tabs: 总览 · 装备 · 武艺 · 计略 · 部队 · 官爵 · 关系 · 履历; 300 px 3D stage (turntable, 4 poses: 待机 · 演武 · 骑乘 · 得胜); prev / next through the list that opened it; actions 赏赐 · 赐宝物 · 移动 · 授官 · 演武 · 罢免 · 习得新招 |
| Generals list | 8 columns (portrait, name + office, level, 武 · 智 · 政 with gear stripes, 兵 · 训, 忠, 所在, 6 gear icons); 4 family filters, 5 status filters, 8 sort keys; list ↔ card view; tabs 我方 · 他国 · 在野 |
| Motion | 5 duration tokens (80 / 140 / 220 / 380 / 650 ms), one spring (k 420, c 26), ≤ 12 ambient loops on the strategy screen, everything on `transform` / `opacity`; reduced-motion keeps all information and drops all movement |
| UI sound | 22 synthesized UI sounds (§14.1), −32 to −16 dBFS, hover sounds rate-limited to 1 / 80 ms; 6 shell voice cues, registered in AUDIO-V4 (`voice-lines.js`) |
| Budgets | strategy-screen DOM ≤ 900 nodes; procedural textures ≤ 25 ms total in ≤ 8 ms idle slices (≈ 120 KB of data URLs); icons cached as strings; counters write text at ≤ 30 Hz |
| Saves | classic save 56 KB, world 196 KB today (measured); v4 adds ≈ 90–120 KB; 4 slots × ≤ 330 KB + thumbnails (≤ 12 KB each) ≈ 1.4 MB of the ≈ 5 MB `localStorage` quota |
| Tests | `tests/ui-v4.html` (component sheet + automatic checks), `tests/ui-layout.html` (5 viewports, 14 screens: overlap, touch targets, text size, clipping), `tests/settings.js` and `tests/saves.js` (Node: registry, migration, defaults, slots, quota); targets in §19 |

---

## 1. Files, modules and ownership

| File | Status | Content |
|---|---|---|
| `css/style.css` | rewritten in place | v4 tokens (§3.2) at the top, then every existing `sg-*` class restyled (names kept: `UI-CLASSES.md` stays valid for battle-view.js, clash.js, duel-game.js) |
| `css/ui-v4.css` | new | the new components and screens (§4–§12); loaded after `style.css` |
| `js/ui-art.js` | new | `SG.UIArt`: procedural textures (paper, lacquer, silk, patina, deckle mask), bronze corner caps, brush lines and masks, SVG filters, lantern and seal drawings; writes the CSS variables (§3.3); idle-sliced |
| `js/ui-icons.js` | new | `SG.UIIcons`: the icon registry as **data** (primitive lists, §3.6) → cached SVG strings; includes DOMESTIC-FX's command icons (that spec's `SG.CmdIcons` reads from here) and the shell / stat / slot / tab / settings icons |
| `js/ui.js` | modified | `SG.UI.button` kinds, `SG.UI.tabs`, `segmented`, `toggle`, `slider`, `counter`, `badge`, `tip`, `window` (lacquer window with paper page), `sheet` (phone bottom sheet), focus management, the sound hooks (§4) |
| `js/settings.js` | new | `SG.Settings` registry and the UI-owned settings (§7); Node-testable |
| `js/shell.js` | new | `SG.Shell`: top beam, counters, quiver, calendar, agenda, end-month seal, month transition, city panel layout and dock (built for `strategy-screen.js`), map label skin |
| `js/menu-screen.js` | new | the 菜单 window, save slots, 卷宗 page, help pages (§6) |
| `js/settings-screen.js` | new | the 设置 window rendering the registry (§7) |
| `js/generals-screen.js` | new | the 武将录 list (§8) |
| `js/character-screen.js` | new | the character menu frame, 总览 / 计略 / 部队 / 关系 / 履历 tabs, stage (§9); hosts the tabs owned by other specs (装备 ← ITEMS `SG.Gear` / `SG.ItemArt`; 武艺 ← COMBAT `SG.Combos`; 官爵 ← TITLES `SG.TitleUI.tab`) |
| `js/tourney-screen.js` | new | announcement, entry, bracket, match card, podium, hall of fame (§12) over the tournament spec's `SG.Tourney` model |
| `js/shop-screen.js` | ITEMS owns | adopts this kit (§10 gives its layout and motion) |
| `js/title-ui.js` | TITLES owns | adopts this kit (§11) |
| `js/strategy-screen.js` | modified | `buildHud`, `refreshAll`, `refreshCityPanel`, `addCmd` delegate to `SG.Shell`; 记录 / 音乐 buttons removed from the top bar (§5.2); `showGeneral` opens the character menu |
| `js/game.js` | modified | `titleFlow` builds the v4 title (§5.11); `HelpText` moves to help pages; loading screen restyle |
| `index.html` | modified | load `settings.js` after `core.js`; `ui-art.js`, `ui-icons.js` after `ui.js`; the screen modules after `strategy-screen.js`; `ui-v4.css` after `style.css`; loading screen markup (§5.11) |
| `tests/ui-v4.html`, `tests/ui-layout.html`, `tests/settings.js`, `tests/saves.js` | new | §19 |
| `tools/export-unity-ui.js` | new | exports tokens, icon data and texture parameters to Unity (§18) |

Parallel-work rule (DESIGN-V2 §3) still holds: each task edits only its own files and lists the rest under INTEGRATION (§17.4).

---

## 2. Audit: why the v3 interface looks like an "AI game"

Screenshots: `design-v4/ui/current/` (`desk-*.png`, `phone-*.png`, seed 7, 刘备 in the classic scenario). What a player sees, and why it reads as generated rather than designed:

| # | Where (screenshot) | What is wrong | v4 answer |
|---|---|---|---|
| 1 | everywhere | **One surface for everything**: every panel, dialog, list row and button is the same translucent near-black glass (`rgba(18,18,26,.9)` + `backdrop-filter: blur(16px)`), 1 px gold hairline, 0.9 rem radius. It is the default "premium dark UI" template; it has no material and no relation to the warm, bright 3D world underneath | two contrasting materials, **lacquer frames holding paper pages** (§3.3); glass only for the scrim |
| 2 | `desk-08-citypanel` | **Twelve identical text pills** for the commands: same size, same grey gradient, no icons; only 出征 is red. Nothing tells 开发 from 外交 at a glance; the disabled ones (登用, 移动, 输送) look broken rather than locked | 16 command **seal tiles** with unique icons, grouped 治 / 人 / 运 / 策, token pips, a lock seal with the reason (§5.4, DOMESTIC-FX §7) |
| 3 | `desk-07-strategy` | **Dashboard top bar**: a row of label-value chips (`190年1月 令牌●●● 城1 金400 粮6000`) and five identical text buttons — it reads like an admin console. Resources have no icons and never move | a lacquered **beam** with the ruler's medallion, a navy-and-gold faction plaque (the same plaque as the 3D gate in the art pilot), a paper calendar, a quiver of command arrows, icon counters that roll, bronze icon buttons (§5.2–§5.3) |
| 4 | `desk-01-title` | **Gold gradient text with a moving sheen** over the map, a rounded red square with 霸, and three stacked pill buttons in the centre: the most recognisable "AI-generated game" composition | brush title with a dry-brush filter and a seal; menu as plaques hanging from a beam; a 「继续征程」 paper card with the ruler's portrait (§5.11) |
| 5 | all text | **System sans everywhere**; Kai only where installed, and on Linux / Android the "calligraphic" title falls back to a heavy sans. Letter-spacing `.06em` on everything | a three-family type system (§3.5): Kai for names, titles and labels with a stroke-thickening and brush filter, sans for body, tabular serif for numbers |
| 6 | `desk-11-factions`, `desk-09-develop-dialog` | **Spreadsheet lists**: every list is the same rounded dark rectangles with grey right-aligned stats (`城 3 将 8 兵 22000`); every interaction is the same centred modal | per-content layouts: bars for stats, portraits with rank frames, rows with sections; menus as windows with page tabs; paper cards (§4.5, §8) |
| 7 | `desk-08-citypanel` | **Paragraph of numbers**: 太守 · 人口 · 土地 · 产业 · 町 · 金 · 粮 · 防御 · 兵力 · 武将 in one text block; the panel covers 30 % of the map permanently | four brush bars (土地 · 产业 · 町 · 民心) with values out of 999 / 100, one icon line for 金 · 粮 · 兵 · 防, generals as rows with stat columns; the panel closes on map tap (§5.4) |
| 8 | everything | **Nothing moves** except fades: no idle life, no feedback on gain or loss, no ceremony when the month ends | ambient lanterns and pennants, rolling counters with delta tags, a quiver that loses an arrow per command, a month transition, press physics on every button (§5.7–§5.8, §13) |
| 9 | glyphs | **Emoji-like symbols as icons**: `■` faction colour, `●○` tokens, `✦` in toasts, `×` close, `▼` continue, `Lv1` pill | drawn flags, arrows, seals, brush cross, a moving brush-tip "continue" mark (§3.6) |
| 10 | `desk-10-general` | **Arbitrary colours**: salmon / sky / mint bars for 武智政, a yellow `Lv1` gacha pill, pink `君` tag; the colours do not mean anything elsewhere | one semantic colour set used everywhere: 武 vermillion · 智 indigo · 政 jade (= TITLES' 武官 · 谋臣 · 文官), rarity colours from ITEMS, gold for level / experience (§3.7) |
| 11 | `desk-12-endmonth-confirm` | **The most important button is just another pill** (结束本月) at the far right of the top bar, same size as 音乐 | a large vermillion **end-month seal** at the bottom right with the remaining tokens; the month change is a short ceremony (§5.7) |
| 12 | `phone-08-citypanel` | **Phone layout is the desktop squeezed**: 6-column command row, the general list shows two rows, the top bar has text buttons cut to two characters | a phone layout designed separately: two-column ledger + 4 × 4 dock, icon buttons with long-press labels, agenda as icons (§5.9) |
| 13 | `desk-03-intro-say`, all dialogs | **No hierarchy of moments**: a story intro, a "not enough gold" note and a level-up use the same dark box | four registers: paper card (information), scroll (edicts, menu, ceremonies), lacquer window (screens), slip / toast (feedback) (§4.3) |
| 14 | missing | No **menu**, no **settings** (music is an on/off text button; animation settings live on the battle screen), no **generals** overview | 菜单, 设置 and 武将 buttons on the top beam (§6–§8) |

What v3 does well and v4 keeps: portraits everywhere, grey commands that explain themselves when tapped (`aria-disabled` + `explainCmd`), keyboard support in every dialog (Enter / Space / Esc / ↑↓ / 1–9), safe-area handling, label decluttering on the map, and the `rem` scaling that matches Unity's CanvasScaler.

---

## 3. Visual language: 漆 · 纸 · 铜 · 灯

### 3.1 Principles

1. **The interface is an object from the world.** Frames are lacquered wood like the hall's pillars and plaques; pages are paper like the windows in the 宾阳楼 render; controls are bronze; selection is lantern light. Nothing is glass, chrome or neon.
2. **Two surfaces, never a third.** Dark lacquer frames (navigation, chrome, buttons) and light paper pages (content). Text on lacquer is paper-coloured; text on paper is ink. A screen has one lacquer window and one or two paper pages.
3. **Ink outlines bound shapes.** 2 px `#241a21` outlines on panels, buttons, slots, chips and icons, matching the 3D outline pass (`SG.LP` outline colour `#241a21`, width 2.2). Shadows are warm brown, offset downwards, never pure black.
4. **Flat facets, not gradients.** Shapes get one light facet (upper left, +6–10 % value) and one shade facet (lower right, −8–14 %, hue 6° toward teal), like the flat-shaded props. Gradients are allowed only for lacquer depth (3 stops), bronze discs and the vermillion seal, and never on text.
5. **Light means attention.** Hover, focus, the selected city, the current match and a free office seat glow with a warm lantern pool (`--lamp-glow`), not with outlines or neon rings.
6. **Seals mean "decided".** Every confirmation is a seal press (squash + ink spatter + stamp sound); the results of decisions carry seals (DOMESTIC-FX's 丰穰 / 得贤 …, 天下第一, 册封).
7. **Brush strokes carry rhythm.** Dividers, progress bars and underlines are brush strokes with dry ends (SVG), titles get a dry-brush edge filter, important words write themselves on with a stroke-reveal.
8. **Asymmetry and hand-made irregularity** (±1° rotation on paper cards, slightly irregular seals, deckled paper edges), but **straight grids** for anything the player compares (stats, slots, brackets).

### 3.2 Palette tokens

All colours are CSS custom properties on `:root` (`css/style.css`), also exported to Unity (§18). The scene colours come from the art pilot (`js/lp-pilot.js`, `js/lowpoly.js`), so the UI and the world share hues.

| Token | Hex | Use | Source in the world |
|---|---|---|---|
| `--lq-0` / `--lq-1` / `--lq-2` / `--lq-3` | `#170f0e` `#241816` `#33231f` `#4a3229` | lacquer: grooves, base, raised, lit facet | dark wood of plaque frames `#3A2A24`, `#3E2C24` |
| `--zhu` / `--zhu-hi` / `--zhu-lo` / `--zhu-ink` | `#b23b2a` `#d65a3e` `#7a2419` `#5a1a12` | vermillion lacquer: seals, primary actions, 武 accents | pillars and doors `#A83E2E`, lanterns `#C8402E` |
| `--paper` / `--paper-2` / `--paper-3` / `--paper-edge` / `--paper-shade` | `#f3e7cc` `#eadbb8` `#dcc79c` `#c3a574` `#b89a68` | paper pages; `--paper-3` and darker are fills only, never behind body text | window paper `#E9D7AE`, cream `#EADFC4` |
| `--ink` / `--ink-2` / `--ink-3` | `#241a21` `#4b3b38` `#6e5d53` | text on paper: primary, secondary, tertiary (≥ 5.1 : 1) | outline colour `#241a21` |
| `--gold` / `--gold-hi` / `--gold-lo` | `#e8b54a` `#f8dc8e` `#a8742e` | gilt: level, experience, focus ring, plaque characters | gilding `#D9A441`, plaque ink `#F2C45C` |
| `--bronze` / `--bronze-hi` / `--bronze-lo` | `#a0703f` `#d2a06a` `#5e3f22` | bronze discs, corner caps, sliders' frame | — |
| `--patina` / `--patina-hi` | `#4e8c86` `#86bcae` | bronze patina, 守 tag, the beam's painted band | painted beam `#4E8C86`, soffit `#4F6E6A` |
| `--dai` / `--dai-hi` / `--dai-lo` | `#2e4466` `#3f5b88` `#1f2f49` | navy plaque (faction name, selected city label) | hall plaque `#2E4466` |
| `--tile` | `#808da6` | roof-tile slate: neutral icon fill | roof tiles `#808DA6` |
| `--lamp` / `--amber` / `--lamp-glow` | `#ffe2a6` `#f2a23c` `rgba(255,196,110,.55)` | lantern light: hover, selection, night pools | lamp `#ffe2a6`, `AMBER` |
| `--war` / `--int` / `--pol` (+ `-t` text variants) | `#c8452f` / `#3e6aa8` / `#3f8f5f`; text `#a8331f` / `#335d96` / `#2c6b45` | 武 · 智 · 政, and TITLES' 武官 · 谋臣 · 文官 | — |
| `--r-common` … `--r-legend` (+ `-t`) | `#9aa3ad` `#5fae6e` `#4a8fdc` `#a464d8` `#f0a52a`; text `#555c64` `#2f6e3e` `#2a62a3` `#7a3bb0` `#8f5208` | ITEMS §4.1 rarity frames and names | — |
| `--good` / `--bad` (on paper) | `#2e6e3a` / `#a8371f` | gains / losses on paper | — |
| `--good-hi` / `--bad-hi` (on lacquer) | `#8ddc93` / `#ff8f73` | gains / losses on lacquer | — |
| DOMESTIC-FX §6.1 icon tokens | `--cinnabar #c8382c --jade #4f9a76 --ochre #c99a2e --indigo #3b4f7a --teal #3aa5a0 --plum #8a5fc0 --ember #e07a3a --amber #d9933a` | icon accents | — |
| `--combo-l` / `--combo-m` / `--combo-h` | `#5fb3a8` `#e0a24a` `#d2553a` | the combo glyphs ● 轻 (teal circle) · ◆ 中 (amber diamond) · ▲ 重 (vermillion triangle) everywhere: the duel HUD ribbon, touch buttons, the 武艺 tab, the tournament entry list and match cards (COMBAT-V4 §5, §8, §12.1). Fills only, always with the ink outline; the shape carries the meaning for colour-blind players | — |

Measured contrast (WCAG 2.x relative luminance; the colour checker in `tests/ui-v4.html` recomputes these):

| Pair | Ratio | | Pair | Ratio |
|---|---|---|---|---|
| ink on paper | 13.7 | | paper on lacquer | 14.1 |
| ink-2 on paper | 8.6 | | caption `#cdbb9f` on lacquer | 9.2 |
| ink-3 on paper | 5.1 | | muted `#b7a690` on lacquer | 7.3 |
| `--war-t` / `--int-t` / `--pol-t` on paper | 5.4 / 5.4 / 5.2 | | gold-hi on lacquer | 12.8 |
| rarity `-t` texts on paper | 5.0–5.6 | | plaque gold on navy | 6.0 |
| `--good` / `--bad` on paper | 5.0 / 5.3 | | paper on vermillion seal | 5.4 |
| `--good-hi` / `--bad-hi` on lacquer | 10.5 / 7.7 | | disabled label on lacquer | 5.4 (still readable: the reason is shown on tap) |

Rules: the base colours (`--war`, `--r-legend` …) are for fills, bars and frames; text uses the `-t` variants. Semantic text colours follow the **nearest surface**, not the window: each surface declares `--c-muted`, `--c-good`, `--c-bad` (lacquer: `#b7a690`, `--good-hi`, `--bad-hi`; paper: `--ink-3`, `--good`, `--bad`) and `.sg-muted` / `.sg-good` / `.sg-bad` read them, so a paper page inside a lacquer window never inherits the light variants (a v3-style descendant selector such as `.sg-panel .sg-good` would). Faction colours as text keep v3's `readable()` lift on lacquer and get a `-t` darkening on paper (`mix(color, #241a21, 0.35)` until ≥ 4.5 : 1, computed once per faction and cached).

### 3.3 Materials

All textures are drawn once into canvases by `SG.UIArt.init()` in idle slices (≤ 8 ms each, ≈ 25 ms in total on a desktop, ≈ 60 ms spread over 9 slices on an iPhone 12) and set as CSS variables (`--tx-paper`, `--tx-lacquer`, `--tx-silk`, `--tx-patina`, `--cap-tl/tr/br/bl`, `--brush-line`, `--brush-under`, `--brush-mask`, `--lock-ico`). Until they exist, the flat base colours render (the first frame is never blank). Decorative randomness uses `SG.SeededRandom` with fixed seeds, never `SG.Random`. The mockup kit (`design-v4/mockups/kit.js`, `K.init`) is the reference implementation.

| Material | Recipe | Size |
|---|---|---|
| **Paper 宣纸** `.sg-paper` | base `--paper`; 60 soft blotches (radius 20–80 px, alpha 0.035–0.075, half warm `rgba(196,160,100)`, half light), wrapped across edges; 260 fibres (quadratic curves 4–22 px, width 0.4–1.1, 60 % brown at 0.06–0.14 alpha, 40 % white at 0.10–0.22); 90 specks | 256² PNG tile, ≈ 45 KB |
| **Lacquer 漆** `.sg-lacquer` | base `--lq-1`; 220 vertical brush streaks (Bézier, width 0.5–2.7, alpha 0.02–0.07, alternating warm and dark), a diagonal sheen at 0.03 | 256² tile, ≈ 50 KB |
| **Mount 装裱** | lacquer frame with 6 px padding, paper page inside with `inset 0 0 18px rgba(184,154,104,.35)` vignette and a 1 px `--paper-edge` inner line | — |
| **Corner caps 铜包角** | L-shaped bronze plate whose arms end in 如意 cloud scrolls (`kit.js` `cap()` path), gradient `#f0c98a → #b8834a → #6e4a28`, ink outline 1.1, a highlight dot; four rotations as background images | 22 px, 4 SVG data URLs |
| **Silk 绢** | 3 px diagonal weave (light / dark crossing lines at 0.06–0.07) over a flat colour | 64² tile |
| **Patina 铜绿** | 26 radial teal spots (2–9 px) multiplied over bronze | 64² tile |
| **Plaque 匾** `.sg-plaque` | navy board (`--dai-hi → --dai → --dai-lo`), 2 px dark-wood frame `#3a2a24`, an inner gold hairline at 45 %, gold Kai text `#f2c45c`; the same object as the 3D gate plaque | — |
| **Seal 印** `.sg-seal` | `--zhu` square (radius 3 px), white-on-red text (阳文) with a 1 px inner white border at 70 %, SVG displacement filter `#sg-brush-edge` (fractal noise 0.9, scale 1.6) for the etched edge; `.yin` = red text on a red outline (阴文); `.round` for 望 / 佳 | — |
| **Scroll 卷轴** `.sg-scroll` | paper page between two wooden rods (`#8a5a36 → #3a2414`, 18 px, ink outline) with gold rod ends | — |
| **Brush line** | a 400×8 SVG path with tapered, dry ends (alpha 0.5 tail), `preserveAspectRatio="none"` | — |
| **Deckle edge** (optional, high quality) | an alpha mask with 2–4 px noisy edges for paper cards | 256×32 strip, 9-sliced |

### 3.4 Shape language and sizes

| Element | Radius | Outline | Shadow |
|---|---|---|---|
| Lacquer window | 10 px | 2 px ink + inner gilt hairline at 4–5 px | `0 18px 40px -12px rgba(30,12,4,.7), 0 6px 12px rgba(30,12,4,.35)` |
| Paper page | 4 px (6 px when tabbed) | 1 px `--paper-edge` inner + 2 px ink when standalone | `0 2px 0 rgba(90,60,30,.25)` |
| Plaque button | 7 px | 2 px ink | a **3 px solid ink "base"** under the button (`0 3px 0 var(--ink)`) + soft drop; pressing removes the base (the button sits down) |
| Bronze disc | 50 % | 2 px ink | same base |
| Command tile | 22 % of the side (12 px in the dock) | 2 px ink + 1 px gilt inner at 35 % | base + soft drop |
| Slot (item) | 10 px | 2 px ink + 2 px rarity colour inner | rare+ glow (§4.6) |
| Chips, tags | 3–4 px (tags), 9–12 px (pills) | 1.5 px ink | none |

Spacing scale: 2 · 4 · 6 · 8 · 10 · 12 · 16 · 24 px (at 1280×720; `rem`-based in CSS: .125 · .25 · .375 · .5 · .625 · .75 · 1 · 1.5 rem).

### 3.5 Typography

| Family | Stack | Used for |
|---|---|---|
| `--f-display` | `"STKaiti","Kaiti SC","KaiTi","BiauKai","Noto Serif SC","Source Han Serif SC","Songti SC",serif` | names, titles, button labels, tabs, city names, seals, numbers of years / months in Chinese |
| `--f-body` | `"PingFang SC","Hiragino Sans GB","Microsoft YaHei","Noto Sans SC","WenQuanYi Zen Hei",system-ui,sans-serif` | sentences, descriptions, tooltips, small labels |
| `--f-num` | `"Iowan Old Style","Palatino Linotype","Book Antiqua","Bitstream Charter","DejaVu Serif",Georgia,serif` + `font-variant-numeric: tabular-nums lining-nums` | all numbers (gold, troops, stats, prices, dates) |

Scale (rendered px at 1280×720 / at 844×390):

| Token | rem | Desktop | Phone | Family |
|---|---|---|---|---|
| `--fs-hero` | 6.4 | 102 | 58 (3.6 rem override) | display, title screen only |
| `--fs-h1` | 2.1 | 34 | 27 | display |
| `--fs-h2` | 1.45 | 23 | 19 | display |
| `--fs-h3` | 1.15 | 18 | 15 | display |
| `--fs-btn` | 1.12 | 18 | 15 | display |
| `--fs-body` | 1.0 | 16 | 13 | body |
| `--fs-small` | 0.82 | 13 | 13 (floored) | body |
| `--fs-caption` | 0.78 | 12.5 | 13 (floored) | body |

`--fs-small` and `--fs-caption` are `max(13px, …)` on phones (DESIGN-V2: text never below 13 px on a phone); on desktop the smallest text is 12.5 px and is never essential (captions, units).

Display text gets `-webkit-text-stroke: .025em currentColor` (system Kai fonts are thin; this thickens strokes like a loaded brush) and `letter-spacing: .06–.16em` by size (wider when shorter). **Brush treatment** (`filter: url(#sg-brush-ink)`: fractal noise 0.65, 2 octaves, displacement 2.2, dilate 0.25) is applied to at most these strings: the game title, month names in the month transition, seal texts, the 授官 edict's post name, 天下第一, VS 「对」, victory / defeat titles. **Stroke reveal** (write-on) is a CSS mask: a 120 % wide linear gradient sweeping left → right per character with 40 ms stagger, 70 ms per character, used for the month name, the edict post name, city names on 入城 and chapter titles in help.

Font bundling is not needed: Mac, iOS, iPadOS and Windows ship a Kai (STKaiti / KaiTi); Android and Linux fall back to Noto Serif SC (a good Song) and the stroke-thickening keeps it calligraphic enough. Bundling a Kai subset (≈ 600 glyphs of fixed UI strings, ≈ 250 KB woff2 as base64 in a script; or ≈ 1 600 glyphs with all names, ≈ 650 KB) is Q1.

### 3.6 Icons

The language is DOMESTIC-FX §6.1 (64×64 viewBox, ink outline 2.5 at 64, flat facets, one accent + at most one secondary, free-standing silhouettes, recognisable as a 24 px silhouette and ≥ 18 % pixel difference from every other icon). This file adds the non-command icons and turns the set into data.

**Format** (`js/ui-icons.js`): each icon is a list of primitives so the browser can build SVG and Unity can rasterise the same data with `Raster.cs` (§18):

```js
SG.UIIcons.def('gold', {
  parts: [
    ['path', 'M8 40 Q8 24 20 26 Q24 18 32 18 Q40 18 44 26 Q56 24 56 40 Q56 50 32 50 Q8 50 8 40Z', 'gold', 'ink'],
    ['path', 'M20 30 Q32 40 44 30 Q42 38 32 38 Q22 38 20 30Z', 'goldLo'],
    ['path', 'M14 36 Q16 30 20 30', null, 'goldHi', 2.4],
  ],
  anim: { hover: 'bounce' },          // micro-loop on hover (CSS keyframes on <g class="ci-a">)
});
// primitives: ['path', d, fill, stroke?, width?] · ['circle', cx, cy, r, fill, stroke?] · ['ellipse', cx, cy, rx, ry, …] · ['rect', x, y, w, h, rx, …]
//             ['g', 'ci-a' | 'ci-b', [...children]] for animated groups; colours are palette names or '#hex'
SG.UIIcons.svg(id, size?) → string (cached)      SG.UIIcons.list() → ids      SG.UIIcons.el(id, size) → <svg>
```

**The non-command set** (motifs; the mockup kit draws a first version of each, `01-kit-desk.png` bottom):

| Group | Icons |
|---|---|
| Top beam | 武将 helmet with a writing brush behind · 官制 进贤冠 on a red cushion with three stars · 势力 map scroll with three pins · 大事 stitched bamboo-slip book · 设置 bronze 规矩 (square and compass) · 菜单 three bamboo slips with red cords · 结束本月 sun setting behind a gate tower (DOMESTIC-FX §6.4 offered these; adopted) |
| Resources | 金 马蹄金 ingot · 粮 millet sack with three ears · 令 command arrow with a red pennon (the quiver of §5.3 uses it) · 城 wall and gate · 兵 three spears over a banner · 声望 round seal 望 · 国策 three slips · 年历 crescent over a bell |
| Stats | 武 jian sword · 智 feather fan · 政 brush over a jade seal · 训 target · 忠 heart knot (同心结) · 等级 eight-point star · 连段 ● ◆ ▲ · 计策 scroll with crossed lines · 必杀 four-point burst · 阵型 five squares · 坐骑 horse head · 羁绊 endless knot · 履历 inscribed slips |
| Slots | 兵器 guandao · 副手 round shield / bow · 铠甲 lamellar coat · 头盔 crested helmet · 坐骑 horse head · 宝物 bamboo book (ITEMS' per-item icons replace these once equipped) |
| Menu and settings | 继续 play · 记录 brush on an inkstone · 读取 opened scroll · 卷宗 slips with a seal · 帮助 ? on paper · 返回标题 gate · 音频 琴 · 画面 eye · 游戏 play · 镜头 bronze camera · 操作 hand · 辅助 figure |
| Lists | 筛选 funnel · 排序 arrow with bars · 列表 / 卡片 · 搜索 magnifier made of a bronze mirror · 收（关闭）two brush strokes · 确认 brush tick · 锁 small seal with a lock |
| Tournament | 武林大会 crossed swords in a jade ring · 奖杯 bronze 爵-shaped cup · 签表 branching slips |

Sizes: 20 (chips), 24–28 (phone buttons, rows), 32–40 (desktop buttons, tiles), 64 (headers), 160 (DOMESTIC-FX card stage). The browser renders inline SVG from cached strings (no canvas atlas needed for ≤ 120 icons); `tests/ui-v4.html` checks the 24 px silhouette distance over the whole set.

### 3.7 Colour semantics (one meaning per colour)

| Meaning | Colour | Where |
|---|---|---|
| 武 / 武官 / 武艺 / physical | vermillion `--war` | stat bars, office names, 必杀 chips, 武 filter |
| 智 / 谋臣 / 计策 | indigo `--int` | stat bars, 计策 chips, 谋 filter |
| 政 / 文官 / 内政 | jade `--pol` | stat bars, 文 filter, development bars (土地 uses jade too) |
| level, experience, gilt, focus | gold | level seals, experience bars, focus ring |
| primary action / decision | vermillion seal | seal buttons, end month, 出征 tile |
| rarity | ITEMS §4.1 slate · jade · lapis · amethyst · gold-cinnabar | slot frames, item names, glow |
| own faction | the faction colour | flags, label pennants, bracket highlights (always with a shape: the flag, a stripe) |
| gain / loss | jade-green / brick-red, always with `+` / `−` | deltas, compare columns |
| lantern light | `--lamp-glow` | hover, selection, current item, free seats |

Colour is never the only signal (§16).

### 3.8 Light and time of day

The UI shares the world's light: `--night` (0 … 1) is set on `<html>` by `SG.Shell` from the map's mood (`SG.LP.Mood` night factor when the art pilot's renderer drives the world map; otherwise 0). At `--night` > 0.3 the top-beam lanterns light (their glow pools opacity = `--night`), paper pages get a warm inner glow (`inset 0 0 60px rgba(255,190,110,.45 × night)`), and selected-city rings and the end-month seal brighten (`03-main-night-desk.png`). **One day clock** (INTEGRATION §10.7, shared with CITY-VIEW §6.6): `SG.Clock.hour()` = 07:00 at the month's start → 20:00 when the last token is used (`7 + 13 × tokensUsed / tokensTotal`), 20:30 (night) for the AI turns and the news, dawn at the month transition; the map's sun follows it when the map renderer adopts `SG.LP.Mood` (Q2), so "a month passes" is seen in the light. The former 08:00 → 18:00 proposal is replaced.

---

## 4. Components (`SG.UI` v4)

All components are built through `SG.UI` (so every screen gets the same sounds, focus handling and reduced-motion behaviour) and styled by `css/ui-v4.css`. The component sheet is `design-v4/mockups/01-kit.html` → `shots/01-kit-desk.png`; the shipping test page `tests/ui-v4.html` renders the same sheet from the real code.

### 4.1 Buttons

```js
SG.UI.button(parent, html, onClick, {
  kind: 'plaque' | 'seal' | 'paper' | 'ghost' | 'icon' | 'tile',   // default 'plaque' (v3 .primary → 'seal')
  icon: 'gold',            // SG.UIIcons id, placed before the label (tile / icon: the main glyph)
  label: '武将',           // icon kind: the name tag shown on hover / long-press and the aria-label
  why: null | '原因…',     // non-null → aria-disabled, lock seal, tap explains (v3 explainCmd behaviour, now generic)
  badge: null | 2 | '新',  // red-wax badge
  sound: 'wood' | 'bronze' | 'stamp' | 'paper' | 'none',   // default by kind
  hotkey: 'G',             // shown as a keycap on hover (desktop only)
  size: 'small' | 'normal' | 'big',
}) → HTMLButtonElement     // btn.setLabel(html), btn.setEnabled(bool | why), btn.setBadge(v), btn.pulse()
```

| Kind | Look | Size (desktop / phone) | Sound (pointer-down → release) | Used for |
|---|---|---|---|---|
| **plaque 漆牌** | lacquer gradient `--lq-3 → --lq-1`, gilt inner hairline at 38 %, Kai label in paper colour, ink base 3 px | min 44 × 44 px, 1.12 rem label, padding .3 / 1.1 rem | `ui-press-wood` → — | ordinary actions |
| **seal 印钮** | vermillion radial (`--zhu-hi → --zhu → --zhu-lo`), white inner border, wide letter-spacing | same; `big` 3.6 rem tall | `ui-press-wood` → `ui-stamp` | the one primary action of a window: 出阵, 授官, 购买, 报名, 记录于…, 结束本月 |
| **paper 纸签** | paper with ink label (high contrast on lacquer) | same | `ui-press-paper` | secondary actions on lacquer (试装, 试听), list choices |
| **ghost 空框** | transparent, 1 px bronze inner line | same | `ui-press-wood` | cancel, 删除, 不参加 |
| **icon 铜钮** | bronze disc 2.85 rem (≥ 44 px), patina multiply, an inset lacquer face holding the icon | 46 / 44 px | `ui-press-bronze` | top-beam buttons, close (`收`), small round tools |
| **tile 指令印** | DOMESTIC-FX §7.1 seal plate | 72 × 56 (dock desktop), 76 × 76 (elsewhere), ≥ 56 × 44 phone | DOMESTIC-FX press sounds | city commands |

States (all kinds; the mockup shows them with `.is-hover`, `.is-focus`, `.is-press`, `.is-disabled`):

| State | Visual | Timing |
|---|---|---|
| idle | static; available command tiles "breathe" (DOMESTIC-FX §7.2) | — |
| hover (mouse) | lifts 2 px (`translateY(-2px)`), base grows to 5 px, gilt line → 85 %, a **lantern pool** fades in behind (`::before`, radial `--lamp-glow`, scale .8 → 1); icon buttons also turn −6° and show their name tag below; the icon plays its micro-loop once | 140 ms `--e-out`; `ui-hover` (≤ 1 per 80 ms, mouse only) |
| keyboard focus | as hover + 2 px `--gold` outline, offset 3 px | — |
| press | sinks 2 px and scales .97 (seal: squash `scale(1.04, .9)` like a stamp), base removed, inner shadow | 80 ms; sound on pointer-down |
| release / activate | spring back (k 420, c 26 → overshoot 1.04, ≈ 220 ms); seal: 10 vermillion ink droplets burst from the press point (CSS, 300 ms), plus a ring for tiles | the action starts at +120 ms (the motion is not a delay) |
| disabled with a reason | saturation .25, brightness .8, label `#a89a86` (5.4 : 1), a small lock seal lower-right; still focusable; tap → `ui-deny`, 0.38 s shake (v3 `sg-cmd-deny`), the reason as a toast | — |
| busy | `aria-busy`, a slow gilt conic ring (1.6 s / turn), input ignored | — |
| selected (toggle, tab, seg) | vermillion (segments), paper joined to the page (tabs), stamped seal (toggles) | 220 ms spring |

Rules: one seal button per window; destructive actions (罢免, 解任, 删除存档, 返回标题) are ghost buttons that open a confirm whose confirm button is a **plaque** with brick-red text (not a seal: the seal means "good decision"). Labels are verbs of two to four characters, with a full-width space inside two-character seal labels (「出　阵」).

### 4.2 Command tiles

DOMESTIC-FX §7 owns the tile (plate, icon, token pip, states, press sounds, result echo). This file fixes the **dock geometry** (§5.4) and two additions:

- `tile.key` keycap at lower left on hover (desktop): `1 2 3 4` / `5 6 7 8` / `9 0 - =` / `Z X C V` by dock row (WASD, Q / E and R / F stay with the camera).
- Inside the dock the tile is 56 px tall on desktop (DOMESTIC-FX's 76 × 76 is used on the city-view dock and the card stage), with a 32 px icon and a 0.9 rem (14.4 px) label. Phone: 56 × 56 px, 26 px icon, label `max(13px, .78rem)` = 13 px (every command name is two characters, 2 × 13 px + spacing fits a 56 px tile); a long-press shows the tag with the command's description.

### 4.3 Windows, pages, scrolls and sheets

| Register | Component | Open / close | Used for |
|---|---|---|---|
| **Screen window** | `SG.UI.window(id, { title, seal, tabs, width, height, onClose })` → lacquer window (inset 8 px on desktop, full screen on phone) with header (seal glyph, Kai title, info line, close disc), optional left nav or top tabs, one or two paper pages, optional action bar | scrim fade 180 ms; window `scale(.96) translateY(12px) opacity 0 → 1` in 260 ms `--e-out`; close 160 ms reverse | 菜单, 设置, 武将录, character menu, 市场, 府库, 官制, 武林大会 |
| **Scroll 卷轴** | `SG.UI.scroll({ … })` paper between rods | unrolls: `clip-path: inset(0 0 100% 0) → inset(0)` 380 ms while the bottom rod travels; roll-up 240 ms | edicts (授官 confirm), the month's AI-turn scroll, help chapters, 年鉴 |
| **Paper card** | the v3 dialogs (`say`, `choose`, `chooseMany`, `pickNumber`, `confirm`, `notice`) restyled: paper body, lacquer header strip, ink outline, ±1° tilt on `say` | 220 ms rise + fade | quick choices, reports |
| **Sheet** (phone) | `SG.UI.sheet(...)`: slides up from the bottom edge, 70–92 % height, grab handle, swipe down to close | 280 ms spring | filters, candidate lists, long pickers |
| **Slip / toast** | paper slip with a seal at the left, slides in | §4.7 | feedback |

Modal stack: at most 3 levels (window → card → confirm). The scrim is the only blurred layer (`backdrop-filter: blur(3px) saturate(.85)`, radial warm-brown `rgba(40,24,14,.35) → rgba(24,14,8,.72)`); low quality turns the blur off. Esc closes the top level; the browser Back gesture on phones does the same (a `popstate` guard pushed while a window is open).

### 4.4 Form controls

| Control | Look | Behaviour |
|---|---|---|
| **Tabs 书签** `SG.UI.tabs(parent, [{ id, icon, label, badge }], onChange)` | lacquer bookmarks along the top edge of a paper page; the active one turns paper, joins the page and shows a vermillion tick; icon + Kai label (phone: label only, 0.8 rem) | ← → / Ctrl+Tab cycle; 140 ms cross-fade + 8 px slide of the page content in the tab direction; `ui-tab` |
| **Bamboo slips 竹牌** (vertical tabs) | wooden slips with a binding line, used in 设置 | as tabs, ↑ ↓ |
| **Segmented 木牌** `SG.UI.segmented(parent, options, value, onChange)` | a row of lacquer plaques, selected = vermillion | ← →; min 44 × 40 px per option |
| **Toggle 印章开关** `SG.UI.toggle` | a lacquer groove 74 × 36 with a square seal that slides and stamps: off = empty seal outline 「关」, on = vermillion 「开」 | spring 220 ms; `ui-toggle-on` / `-off` |
| **Slider 玉珠** `SG.UI.slider(parent, { min, max, step, value, format, onInput, onCommit, preview })` | lacquer groove, gilt fill, tick marks every 10 %, a jade bead knob 24 px (hit area 44 px) | drag, click-to-jump, ← → by step, PgUp / PgDn by 10 %; `ui-bead` per 10 % crossed (pitch rises a semitone per step); `preview` plays the channel's sample on release |
| **Stepper** (`pickNumber` v4) | big tabular number between − / ＋ discs, a slider below, a forecast line | hold − / ＋ to repeat (400 ms delay, then 12 / s) |
| **Search field** | paper pill with a bronze-mirror icon | filters as you type (pinyin initials and characters) |
| **Dropdown** | plaque with ▾ opens a paper list (`choose`-like), anchored | — |

### 4.5 Lists and rows

- **General row** (city panel, 武将录, pickers): portrait (40 px desktop / 34 px phone, rank frame §4.6) · name in Kai with 君 / 守 tags, level seal, 新 tag · office name in its family colour · 武 智 政 (numbers in the family `-t` colours; in 武将录 with bars and gold gear stripes) · troops and training · loyalty (heart + number, brick-red under 60) · location · six gear icons · state stamps (「已行动」 as a rotated red stamp over the portrait, 「外出」 with the caravan / expedition icon, 「伤」). Height 46 px (phone 44). Hover = lantern pool; selected = gilt inner line + pool.
- **Item row / card**: slot icon with rarity frame · name in rarity text colour · key stats · price or source on the right · requirement failures greyed with the reason.
- Rows enter with a 6 px rise + fade, 18 ms stagger, capped at 12 rows (216 ms). Lists over 40 rows are virtualised (render 2 screens of rows).
- Empty states are a line of Kai text on paper with a small ink drawing (e.g. an empty armoury shows an open chest: 「府库空空如也」).

### 4.6 Data display

| Component | Spec |
|---|---|
| **Brush bar** `SG.UI.bar(v, color, { gear })` | 10 px (13 px for the character menu attributes); track `rgba(36,26,33,.14)` with an inner shadow; fill masked by a brush-stroke shape (round head, dry tail); gear bonus as a gold diagonal-hatch segment after the fill; the value text sits outside |
| **Counter** `SG.UI.counter(el, { value, format, icon, sound })` | odometer: each digit column rolls; duration `clamp(0.35 + 0.12·log10(abs(Δ)+1), 0.35, 0.9)` s, `easeOutCubic`; intermediate values for more than 24 steps are interpolated in ≤ 24 steps; a **delta tag** (`+120` jade / `−300` brick, ink outline, pill) appears above, rises 18 px and fades over 1.2 s; the icon bounces (scale 1 → 1.18 → 1, 240 ms); changes within 250 ms merge |
| **Level seal** | gold disc with the level number in serif bold, 「等级」 tag above (character menu); compact pill `5` in rows |
| **Experience bar** | gold brush bar with `1,240 / 2,000` and the 老练 bonus |
| **Portrait frame (rank)** | 2 px ink + a 2 px coloured ring + 2 px ink; ring by the general's office tier / ruler rank (TITLES §9.6): R1 wood `#8c6a48`, R2 dark lacquer `#7a2419`, R3 vermillion `#b23b2a`, R4 gold `#e8b54a`, R5 pale gold `#f8dc8e` + glow; non-office generals use R1; enemy generals keep their own |
| **Item slot** | ITEMS §5.5: background tinted 18–26 % with the rarity colour, 2 px rarity inner line; rare = soft glow, epic = strong glow + shimmer sweep (3.2 s), legendary = gold glow + shimmer + slow gilded edge; empty = dashed ink outline at 25 %; label tag below on the character page |
| **Badge** | red-wax disc 20 px, ink outline, number in serif bold or a single character (新); pulses twice (1.2 s) when it grows |
| **Tags** | 君 vermillion, 守 patina, 新 jade, 宝 gold (unused 宝具), 舶来 teal, 胡商 ochre, 虚衔 hatch |
| **Stat card** | small paper box: label (Kai .82 rem), value (serif bold 1.1 rem), breakdown (body .66 rem) — e.g. 攻击 ×1.42 · 老练 +24% · 装备 +14% |

### 4.7 Feedback

| Component | Spec |
|---|---|
| **Tooltip 纸签** `SG.UI.tip(target, html)` | paper tag with ink outline and a vermillion string on top; appears after 350 ms hover (mouse) or on long-press 450 ms (touch); max 22 rem wide; stays inside the viewport; never covers its target |
| **Toast 纸条** `SG.UI.toast(html, { seal, face, seconds })` | slip from the right edge of the free map area (or above the dock on phones), left seal (记 / 成 / 败 / 宝 …), optional portrait; 260 ms in, 2.6 s, 300 ms out; stack of ≤ 3 |
| **Banner** | a horizontal brush stroke with Kai text across the upper third (month banner, battle start), 0.3 s stroke-in, holds, 0.4 s fade |
| **Deny** | the element shakes (±4 px, 0.38 s), `ui-deny`, explanation toast (v3 behaviour) |
| **Loading seal** | the 霸 seal rotating slowly with an ink ring filling (loading screen, long builds) |
| **Continue mark** | in `say` cards: a brush-tip chevron that breathes (replaces `▼`) |

### 4.8 The v3 dialogs, restyled

`SG.UI.say / choose / chooseMany / pickNumber / confirm / notice / genCard` keep their signatures and keyboard behaviour. Their new skin: `.sg-dialog` becomes a paper card with a lacquer header strip (`.sg-dlg-head`), `.sg-item` rows become paper rows with lantern hover, `.sg-close` becomes the bronze 收 disc, `.sg-confirm-btns` uses plaque + seal, and `say` gets the speaker's portrait in a rank frame on the left edge of the card with the name on a small plaque. Long lists in `choose` get a search field when they have > 12 items.

---

## 5. The main interface (strategy screen)

Mockups: `03-main.html` → `shots/03-main-desk.png` (day, notes), `03-main-night-desk.png` (night), `03-main-month-desk.png` (month transition), `03-main-phone.png`.

### 5.1 Layout

Desktop 1280×720 (root 16 px; all sizes in `rem` so 1920×1080 scales by 1.5 and 1024×768 by 0.96):

```
┌─[medallion]─[plaque 刘备军]──[年历 正月 ⚔]─[令箭 2/3]─[金 1,980][粮 6,000][城 1]─[国策 ▮▮▯]──[武将][官制²][势力][大事] │ [设置][菜单]─┐  y 6–64, band to 70
│   ◉ lantern                                                                                 ◉ lantern                         │
│ ┌ agenda ────────┐                                                        ┌ city panel (376) ──────────────────┐ │
│ │ 武林大会  选将 › │                    world map                            │ 平原  ⚑刘备·太守刘备  [入城] (收)   │ │
│ │ 可习得新招 查看 › │                                                        │ ┌ paper ─────────────────────────┐ │ │
│ │ 官制空缺2 授官 › │          [paper city labels, selected = navy plaque     │ │ 土地 384/999 ▬▬▬  产业 … 町 … 民心 │ │ │
│ │ 胡商抵达 市场 ›  │           + lantern ring on the ground]                  │ │ 金 400 粮 6,000 兵 13,500 防 40  │ │ │
│ └────────────────┘                                                        │ │ 武将 4 ─────────── 兵 / 训       │ │ │
│                                                                            │ │ [row][row][row][row]            │ │ │
│                         toasts (top of the free area)                      │ └────────────────────────────────┘ │ │
│                                                                            │ 治 [开发][营建][征兵][训练]          │ │
│                                                         ( 结束本月 )        │ 人 [搜索][登用][赏赐][任命]          │ │
│                                                         [余 2 令]           │ 运 [移动][输送][交易][市场]          │ │
│                                                                            │ 策 [通商][探索][外交][出征]          │ │
└────────────────────────────────────────────────────────────────────────────┴────────────────────────────────────┘
```

| Region | Position (1280×720) | Notes |
|---|---|---|
| Top beam | x 8–1272, y 6–64; painted band to y 70 | always visible on the strategy screen and in the city view; hidden in battle, duel, clash and full-screen windows |
| Lanterns | hang from the beam's underside at x = 268 and at `right: panel width + 44`; 22 × 46 px | sway; light at night (§3.8) |
| Agenda | left 10, top 96, width 238, ≤ 4 slips | hidden while a window is open |
| City panel | right 10, top 78, bottom 10, width 376 (23.5 rem) | opens on city selection; closes on 收, Esc or an empty-map tap (v3) |
| End-month seal | 84 px disc + ribbon; 16 px left of the city panel, bottom 16 (without a panel: right 16) | always visible on the strategy screen |
| Toasts | top 96, centred in the free map area | ≤ 3 stacked |
| Map labels | world layer | declutter (v3) treats the beam, agenda, panel and seal as occluders |

1024×768 (iPad) uses the same layout (panel 23.5 rem = 361 px). Below 1100 px width and above 560 px height (iPad portrait is excluded by the rotate hint), the agenda collapses to icons as on phones. Height < 560 px switches to the phone layout (§5.9) through `html.sg-short` (`matchMedia('(max-height: 559px)')`).

### 5.2 The top beam

A lacquered beam (`.sg-beam`, 58 px, radius 10, corner caps, a 6 px painted band below it: patina green with gold dots, like the hall's painted beam), holding left to right:

| Element | Spec | Interaction |
|---|---|---|
| **Ruler medallion** | 72 px round portrait in the ruler's rank frame (R1–R5, TITLES §9.6), overlapping the beam's top-left (`top: -2px`, `left: 6px`), `win` mood | click → the ruler's character menu; long-press / hover tag: 「刘备 · 公 · 德 82 · 人望 60」 |
| **声望 chip** (ENGAGEMENT §16.1) | paper pill at the medallion's lower right: 望 seal 22 px + prestige number | click → 天下榜; it pulses and rolls on change |
| **Faction plaque** | navy plaque (§3.3) with the faction pennant (12 × 16 flag in the faction colour) and the faction name; a caption line under it: 「君主 刘备 · 公孙瓒属」 (vassal relation if any, else the scenario short name) | click → 势力 (faction overview) |
| **年历 calendar** (ENGAGEMENT §16.1) | a paper card rotated −1°: small year (`190 年`, serif), the month in Kai (`正月 二月 … 腊月`; vermillion), the season icon, and a "next thing" cell after a dashed divider: icon + two lines (`本月 / 武林大会`, `三月 · 庙会`, `七月 · 秋收`) | hover / long-press: the 12-month strip with icons; click → the year calendar (ENGAGEMENT's 年历 page in 卷宗) |
| **令箭筒 quiver** | a red lacquer quiver with one arrow per token this month (max 10; arrows 8 px apart, the quiver widens), spent arrows drawn as empty dashed sockets; number `2/3` beside it | hover: 「本月令牌 2 / 3 · 城 1 → 每 2 城 +1 枚（上限 10）」 |
| **Counters** | 金 (ingot) · 粮 (millet sack) · 城 (gate): §4.6 counters; gold and food are the faction totals (as v3) | hover: per-city breakdown, monthly income / upkeep forecast (`每月 +58 金 · 秋收约 +4,200 粮`) |
| **国策 slips** (ENGAGEMENT §7.4) | three small slips (filled = active mission, outline = free), label 国策, badge when offers wait | click → 国策 board |
| **王道 tracker** (ENGAGEMENT §15.2) | appears only once two conditions are met: five tiny seals + a ring; collapsible into the calendar's next-thing cell | click → 王道 page |
| **Bronze buttons** | 武将 (`G`) · 官制 (`T`, badge = free seats with an eligible candidate, TITLES §9.1) · 势力 (`P`) · 大事 (`H`) · divider · 设置 (`O`) · 菜单 (`Esc` when nothing else is open) | 46 px discs, 8 px gaps; name tags on hover (desktop) or long-press (touch); badges: 武将 「新」 when a general can learn a move or a level-up is pending |

Removed from the v3 top bar: **记录** (now 菜单 → 记录, plus `Ctrl/⌘+S` quick save to the last manual slot with a toast) and **音乐** (now 设置 → 音频; `M` toggles mute globally with a toast 「静音」). **结束本月** moves to the seal (§5.7).

Overflow rule (narrow desktops, e.g. 1100–1279 px): first the plaque caption hides, then the 国策 label, then the calendar's next-thing text (icon stays), then the 城 counter folds into the gold tooltip. Nothing wraps to a second line (v3 wrapped and then pushed the city panel down).

### 5.3 Animated counters, tokens and calendar

| Event | Animation | Sound |
|---|---|---|
| Gold / food change (command, income, purchase) | counter roll + delta tag (§4.6); on income at month start the three tags appear in sequence (金 → 粮 → 民心 on the city panel) 200 ms apart | `ui-coins` (gain; 3–8 clinks by magnitude), `ui-coin-out` (loss), `ui-grain` (food) |
| A command spends a token | the rightmost arrow lifts out of the quiver (rotate −20°, 120 ms) and flies to the pressed tile along a 220 ms arc, disappears in a small gold puff; the socket turns dashed; the number rolls | `ui-token` (whoosh + thock) |
| Month start refill | arrows drop into the quiver one by one, 90 ms apart, each with a 6 px bounce | `ui-token-refill` (wood clack per arrow) |
| No tokens left | the quiver tips 8° and the end-month seal starts its "suggested" glow (§5.7) | — |
| Month change | the calendar card flips (rotateX 0 → −90°, swap, −90 → 0; 2 × 160 ms), new month written on (stroke reveal 300 ms) | `ui-page` |
| Prestige / mission change | the chip rolls; mission slips fill with a gold sweep | `ui-badge` |

### 5.4 City panel and command dock

The panel is a lacquer frame (`.sg-citypanel`) with a header strip, a paper ledger and the dock.

**Header** (lacquer): city name in gold-leaf Kai 1.85 rem; owner line (pennant · ruler · 太守 name); level line (city-level icon + 「县城 · 二级」 from ENGAGEMENT §8.5 / CITY-VIEW, + population `30万人`); **入城** plaque button (gate icon; CITY-VIEW §2.2) and the close disc. A small **演出速度 chip** (DOMESTIC-FX §3.5: ⏵ / ⏩ / ✕) sits at the right end of the dock's first row label column on desktop and in the header on phones.

**Ledger** (paper):

1. Development bars: 土地 (jade), 产业 (ember), 町 (amber) as `value / 999` with brush bars; 民心 (vermillion, ENGAGEMENT §3.1) `/ 100`. A gold hatch marks the part of the bar added this month; a scaffold mark shows when a building stage is within 8 % (CITY-VIEW §2.4).
2. One icon line: 金 · 粮 · 兵 · 防 (no labels; tooltips give names, monthly forecasts and the defence breakdown).
3. 「武将 n」 header with a brush line and the column hint 「兵 / 训」; rows as §4.5 (portrait 40, name + tags + level, office in family colour, 武 智 政 numbers, troops and training). Click a row → character menu; long-press → quick actions (赏赐 · 移动 · 授官).
4. Engagement additions (ENGAGEMENT §16.1): the active 营建 project ring next to the level line; plaques of finished projects as tiny icons after the population; caravan / expedition icons in the row of the general who is away.

**Dock** (lacquer), 4 rows × 4 tiles, each row led by a group seal (vertical Kai on the group colour):

| Row (seal) | Tiles (hotkeys) | Group colour |
|---|---|---|
| **治** govern the city | 开发 `1` · 营建 `2` · 征兵 `3` · 训练 `4` | jade |
| **人** people | 搜索 `5` · 登用 `6` · 赏赐 `7` · 任命 `8` | plum |
| **运** logistics and trade | 移动 `9` · 输送 `0` · 交易 `-` · 市场 `=` | ochre |
| **策** outward strategy | 通商 `Z` · 探索 `X` · 外交 `C` · 出征 `V` | vermillion |

This resolves DOMESTIC-FX §7.1 ("the shell task owns the exact layout"): 16 tiles, no 授官 tile (TITLES §9.1: 授官 is not a city command; it lives in 官制, 任命 → 官爵 and the character menu). Rows are fixed, so muscle memory works in every city; tiles that do not apply are shown disabled with the reason (never hidden), except in **other factions' cities**, where the dock is replaced by a view-only strip: 「入城观看 · 外交 · 出征（相邻时）」 and, for allied cities next to an own city, 「市场 ×1.15」 (ITEMS §6.1).

The hotkeys work while the city panel is open and no window is open. They avoid the camera keys (WASD pan, Q / E rotate, R / F tilt in the city view) and the shell keys (G T P H O M, Esc, Enter).

### 5.5 Agenda 案头

A column of up to four paper slips at the left edge listing what is waiting for the player this month, so a quiet month always shows something worthwhile to do (request 13). Each slip: coloured left edge, 32 px icon, Kai title, one line, a vermillion verb with ›; click opens the place to act. Sources and priority (highest first, max 4; the rest under 「另有 n 件」):

| Priority | Source | Example | Opens |
|---|---|---|---|
| 1 | scheduled events (ENGAGEMENT §5): tournament entry, 文会, festival, harvest | 武林大会 · 本月于洛阳平乐观举行 · 选派至多 5 人 | entry screen (§12.2) |
| 2 | event cards waiting (ENGAGEMENT §6) | 灾异 · 东郡蝗灾 | the card |
| 3 | generals who can learn a new move / level-ups pending | 可习得新招 + two portraits | first general's 武艺 / 计略 tab |
| 4 | free office seats with eligible candidates (TITLES §9.1) | 官制空缺 2 · 关羽已可出任五品官 | 官制 at that seat |
| 5 | shop news: rare in stock, caravan arrived (ITEMS §6.5, §14.2) | 胡商抵达 · 平原市中来了一位大宛马商 | 市场 |
| 6 | projects finished, caravans / expeditions returned | 营建落成 · 平原 市集 | the city |
| 7 | low loyalty (< 50) generals | 忠诚堪忧 · 孙乾 58 | character menu |

A new slip slides in from the left (320 ms) with `ui-notify`; a slip is dismissed by acting on it or by a left swipe / its × (it then stays dismissed for the month). Phone: icons only (44 px), the slip expands on tap.

### 5.6 Map labels

City labels become **paper tags** on a short ink post: pennant in the owner's colour, city name in Kai 1.02 rem, troops for own cities in small serif. The selected city's tag becomes a navy plaque with gold text (the 3D gate plaque) and a lantern ring appears on the ground around the city model (a warm ellipse 150 × 64 px, pulsing 2.4 s). Compact mode (far camera, v3 rule) drops the troops and uses 0.92 rem. Enemy labels at war with the player get a small crossed-spears glyph; allied ones a knot. The v3 declutter logic is unchanged; occluders now include the agenda and the seal.

### 5.7 End month 结束本月 and the month transition

**The seal**: an 84 px vermillion disc with the endMonth icon (sun behind a gate tower) and a paper ribbon below it, 「结束本月 余 2 令」. While tokens remain it is calm; with no tokens left (or nothing useful left: all generals moved) it glows (ember pulse 2.4 s) and bobs 2 px — the suggested action. Shortcut: `Enter` when no window is open (v3 had none), or `Space` held 0.4 s.

Press → if tokens remain, a paper confirm card 「还有 2 枚令牌未用，确定结束本月？」 with the unused cities listed (v3) and a checkbox-toggle 「不再询问」 (setting 游戏 → 结束本月确认). Then:

| t (s) | Beat | Visual | Sound |
|---|---|---|---|
| 0.00 | stamp | the seal squashes and stamps; the disc turns 180° (sun → moon face) | `ui-stamp` |
| 0.15 | dusk | a warm-to-blue grade over the map (`--night` 0 → 0.7 over 0.6 s); lanterns light | `cmd.endMonth` voice line (AUDIO-V4), music ducks |
| 0.6 – … | AI turns | a hanging scroll at the top centre 「诸侯行动中」 with a row of faction pennants; each pennant flips as that faction finishes (v3 progress), events and battles interrupt as today | soft page turns |
| end of AI | month name | full-screen paper wash in the free area, the new month written in big Kai (8 rem) with the stroke reveal (0.5 s), the year, season and solar term below (`一九〇年 · 仲春 · 惊蛰`), seasonal particles for 2 s (spring petals, summer fireflies, autumn leaves, winter snow; ≤ 26 sprites) | `ui-month` (temple bell; the ruler suite's culture colours it, AUDIO-V4 §A10) |
| +1.2 | dawn | wash fades, `--night` → 0, quiver refills (§5.3), income tags roll | `ui-token-refill`, `ui-coins` |
| +1.6 | news | month news and the agenda (v3 news cards, restyled) | — |

Any click / Space skips to the next beat; reduced motion replaces the whole sequence with a 0.3 s cross-fade and the month banner. In month 1 the ENGAGEMENT year review (年鉴) plays before the news. **Month-start budget** (INTEGRATION §6.6): the non-interactive beats of a month start — the year review, achievement seals, stage-up celebrations, the harvest montage and this transition — total **≤ 8 s**; whatever would exceed it becomes an agenda slip (§5.5) or a news line instead of playing (order of priority: the transition, the year review, the harvest montage, stage-ups, seals). The AI turns and v4 month phases (INTEGRATION §6.3) compute under the scroll in ≤ 8 ms JS slices; the January tournament's AI-only resolution is outside the 60 ms per-faction cap.

### 5.8 Ambient motion on the strategy screen

| Element | Motion | Period / amplitude |
|---|---|---|
| Lanterns (2) | pendulum around the hook | 3.1 s, ±3°, phase offset 1.3 s |
| Faction pennant on the plaque | 3-segment skew flutter | 1.6 s, ±6° |
| Available command tiles | breath (DOMESTIC-FX) | 6–9 s, scale 1.03 |
| 出征 tile when targets exist | ember glow | 2.4 s |
| End-month seal (suggested) | glow + 2 px bob | 2.4 s |
| Selected city ring | glow pulse | 2.4 s |
| Free office seat / new badge | glow, 2 pulses | 1.2 s |
| Calendar (spring / night) | one petal or firefly drifts across the card | every 4 s |
| Plaque gilt glint | a narrow highlight sweeps across the plaque | every 12 s, 0.8 s |

At most 12 animated elements at once on this screen; all loops pause when the tab is hidden, when a window covers the screen, and under reduced motion. Only `transform` and `opacity` animate (no layout, no `box-shadow` animation on large elements: glows animate the opacity of a pre-blurred pseudo-element).

### 5.9 Phone landscape 844×390

`03-main-phone.png`. Root 13 px.

| Region | Layout |
|---|---|
| Top beam | 44 px, inset 4 px; medallion 46 px (overhangs by 6 px); plaque with name only; calendar compact (`正月` + next-event icon); quiver 30 px + number; 金 · 粮 · 城; the six bronze buttons (44 px, gaps 4 px); lanterns and 国策 label hidden (国策 slips live in the calendar's popup) |
| Agenda | a column of 44 px icon slips at the left edge (top 58); tap expands one slip to 238 px |
| City panel | right 4, top 54, bottom 4, **476 px wide, two columns**: ledger (214 px; bars, icon line, general rows 44 px with portrait 34, no stat columns) and dock (4 × 4 tiles 56 × 56 px, 4 px gaps, no group seals: the group colour is a 3 px stripe at each tile's left) |
| End-month seal | 62 px disc + ribbon at the bottom-left (left thumb), so the right thumb works the dock |
| Toasts | above the end-month seal, left-aligned |
| Long-press | 450 ms on any icon button or tile → name and description tag (no hover on touch) |

In the city view (CITY-VIEW §2.4) the phone dock becomes the bottom bar CITY-VIEW describes (one scrollable row of the same 16 tiles, grouped by the four group stripes, snap per group) and the ledger becomes a pull-up drawer; on desktop the dock stays on the right.

### 5.10 Battle, clash and duel HUDs

The battle screen keeps its structure (`UI-CLASSES.md` §5) and takes the new skin: `.sg-battle-top` becomes a short beam with day / tokens / troops counters (arrows for 行动力), `.sg-battle-card` a paper unit card with portrait, office insignia (TITLES §9.6), troops bar and morale, `.sg-battle-actions` plaques and a seal 「结束回合」. Unit info labels above units become small paper tags with a brush troop bar. The clash and duel HUDs are owned by COMBAT-V4 and use the same tokens (duel HP bars as brush bars, the combo ribbon as seals, crit numbers in Kai with ink splashes).

### 5.11 Title and loading screens

`02-title.html` → `shots/02-title-desk.png`, `02-title-phone.png`.

- Background: the world map at golden hour, orbiting slowly (v3 behaviour) under a paper wash (radial `rgba(243,231,204,.82)` around the title) so the title reads on any terrain.
- Title block (left 7 %, top 13 %): 「三国志」 line with brush rules, 「霸王的世界」 6.4 rem Kai with the brush filter and stroke thickening, 「霸」 enlarged ×1.22 in deep vermillion, a rotated 霸 seal at the upper right, the subtitle 「群雄逐鹿 · 四海归一」. On first load the title writes itself on (stroke reveal 1.2 s), the seal stamps at 1.3 s (`ui-stamp` + a soft gong).
- Menu board (right 6 %, vertically centred): a lacquer board hanging from a wooden beam with gold rod ends; items: **新的征程** (seal), **继续征程** as a paper card with the save's ruler portrait and `董卓专横 · 刘备军 · 192年3月 · 城 5`, **读取记录**, **功业 · 图鉴** (ENGAGEMENT's 功业阁 medals, ITEMS' 图鉴), **设置**, **操作说明**. The board sways ±0.6° (4 s). Hover lights the plaque from below.
- Phone: the same, title on the left 54 %, board on the right (300 px).
- Loading screen (`#loading` in `index.html`): paper background (CSS-only, before scripts run: a radial cream gradient), the 霸 seal with an ink ring filling instead of the sweeping gold bar, 「正在绘制山河……」 in Kai; the error state turns the ring brick-red.

---

## 6. MENU 菜单 (request 1)

`04-menu.html` → `shots/04-menu-desk.png`, `04-menu-phone.png`.

**Open**: the 菜单 bronze button, `Esc` when nothing else is open, the phone Back gesture on the bare map. It opens as a lacquer window (max 980 × 600 px, full screen on phones) that unrolls from the button (scale-in from the button's corner, 260 ms). The game is paused while it is open (it is between commands anyway; battle and duel have their own pause menus that reuse this window with fewer entries).

**Navigation** (left column, 230 px; phone 168 px): bookmark items, the current one joined to the page:

| Entry | Page |
|---|---|
| 继续 | closes the menu |
| 记录 | the save page (below) |
| 读取 | the load page (same slots, load action) |
| 卷宗 (badge 「新」 when an achievement or a 图鉴 page completed since last visit) | a grid of large tiles: 图鉴 (ITEMS §14.2) · 功业录 (ENGAGEMENT §14) · 天下榜 (§15.5) · 大事记 (v3 chronicle) · 英雄榜 (tournament hall, §12.8) · 国策 (§7) · 营建总览 (§8) · 商队 · 探索 (§9, §12) · 外交 (§10) · 年历 (§5 calendar) — each tile: icon 48 px, Kai name, one line of status (`212 / 567`, `14 项`, `第 3 名`) |
| 设置 | opens the settings window on top (§7) |
| 操作说明 | help chapters (below) |
| 返回标题 | ghost at the bottom; confirm card 「返回标题？本月进度已自动记录于『自动』」 |

The footer shows `剧本 · 势力 · 年月 · 已游玩 6 小时 12 分` and the save version.

**Save slots** (replacing v3's single key): one automatic slot and three manual slots.

| Slot | Written | Key |
|---|---|---|
| 自动 | at every month start after the news (where v3 saved), and before 返回标题 | `sanguozhi2_v4_save_auto` |
| 一 · 二 · 三 | by 记录 | `sanguozhi2_v4_save_1` … `_3` |
| index | on every write | `sanguozhi2_v4_saves` = `[{ slot, scenario, factionKey, factionName, ruler, year, month, cities, generals, gold, savedAt, playMs, version, thumb }]` |

- `thumb`: a 300×172 JPEG (quality 0.7) of the map canvas at save time with the HUD hidden, ≈ 9–12 KB as base64; the ruler portrait is generated at display time from the name (not stored).
- Size: classic 56 KB and world 196 KB today (measured with `GameState.newGame`), ≈ +90–120 KB with the v4 systems (ITEMS +60–90, ENGAGEMENT +25, TITLES / COMBAT small) → ≤ 330 KB per world save, 4 slots ≈ 1.4 MB of the ≈ 5 MB `localStorage` quota. A write that throws `QuotaExceededError` shows 「存储空间不足：请删除一份记录」 and keeps the old slot intact (write to a temporary key first, then swap).
- The v3 save `sanguozhi2_save_v3` (and v2) appears on the 读取 page as a card 「旧版记录（第三版）」 and loads through the existing migration; v4 never writes the old keys.
- Slot card: number tag (自动 in patina, 一二三 in vermillion), thumbnail 150 × 86 with ink outline, ruler portrait 26 px in rank frame + faction name in Kai, `剧本 · 年月` (serif), `城 · 将 · 金`, `保存时间 · 游玩时长`. Selected = lantern pool + gilt line. Actions: 记录于「一」 (seal; overwriting shows both summaries side by side in a confirm), 读取 (seal on the load page; confirm if the current game has unsaved months), 删除 (ghost, confirm).
- `Ctrl/⌘ + S` quick-saves to the last manual slot (or 一) with a toast 「已记录于『一』」; there is no quick load. Private-mode browsers that refuse storage show the page with 「此浏览器不允许本地存档」.

**操作说明** (help) replaces v3's single `say` box: chapters on the left (入门 · 地图与城池 · 指令 · 武将与成长 · 装备与市场 · 官制 · 战斗 · 单挑 · 武林大会 · 快捷键), each a paper page with icons and short paragraphs; the 单挑 and 快捷键 chapters show the bindings of the current device (keyboard or touch) and of the current presets (COMBAT §5). The first time a system appears in play, its chapter gets a 「新」 tag.

---

## 7. SETTINGS 设置 (request 1)

`05-settings.html` → `shots/05-settings-desk.png`, `05-settings-phone.png`.

**Open**: the 设置 bronze button (`O`), 菜单 → 设置, the title screen. Lacquer window (max 980 × 610), six **bamboo-slip tabs** on the left (phone: 96 px), a paper page of rows on the right, footer 「恢复本页默认」 (ghost) and 「完成」 (seal). Every change applies immediately and is saved at once (no "apply" step); sliders preview on release; the footer notes that settings belong to this device, not to the save.

**Registry** (`js/settings.js`): every row is registered by the module that owns it, so each sibling spec keeps its own storage key and the screen simply renders the registry.

```js
SG.Settings.define({
  id: 'fx.domestic', tab: 'game', order: 20, label: '内政演出', sub: '指令动画的长短',
  type: 'seg' | 'slider' | 'toggle' | 'button',
  options: [['smart', '智能'], ['full', '完整'], ['highlights', '精彩'], ['fast', '快速'], ['off', '关']],   // seg
  min, max, step, format,                       // slider
  default: 'smart',
  store: { key: 'sanguozhi2_fx_v4', path: 'domestic' },   // owner's localStorage object (try / catch)
  apply: v => {},                               // live effect
  when: () => true,                             // hide when not applicable (e.g. desktop-only rows on touch)
});
SG.Settings.get(id) → value      SG.Settings.set(id, v)      SG.Settings.on(id, fn) → off()
SG.Settings.rows(tab) → [defs]   SG.Settings.reset(tab)      SG.Settings.export() / import(obj)   // for Unity parity tests
```

UI-owned rows live in `sanguozhi2_ui_v4`. Reads and writes are wrapped in try / catch and fall back to defaults in memory (private mode).

**All rows** (default first in each option list; owner in brackets):

| # | Tab | Row | Options / range | Default | Owner · key |
|---|---|---|---|---|---|
| 1 | 音频 | 主音量 | 0–100 % | 100 | AUDIO · `sanguozhi2_audio_v4` |
| 2 | 音频 | 音乐 | 0–100 % (试听) | 100 | AUDIO |
| 3 | 音频 | 音效 | 0–100 % (试听) | 100 | AUDIO |
| 4 | 音频 | 语音 | 0–100 % (试听) | 100 | AUDIO |
| 5 | 音频 | 界面音效 | 开 · 关 (§14 sounds only) | 开 | UI · `sanguozhi2_ui_v4` |
| 6 | 音频 | 音乐风格 | 君主乐章 · 地域（第三版） | 君主乐章 | AUDIO |
| 7 | 音频 | 语音语言 | 原声（各说其语） · 一律汉语 · 关 | 原声 | AUDIO |
| 8 | 音频 | 语音频率 | 全部 · 精简 · 关键 | 全部 | AUDIO |
| 9 | 音频 | 字幕 | 原文＋中文 · 仅中文 · 关 | 原文＋中文 | AUDIO |
| 10 | 音频 | 后台静音 | 开 · 关 | 开 | UI |
| 11 | 画面 | 画质 | 自动 · 高 · 中 · 低 | 自动 | CITY-VIEW / DOMESTIC-FX · `sanguozhi2_fx_v4.quality` |
| 12 | 画面 | 渲染精度 | 自动 · 100 % · 75 % · 50 % (pixel ratio cap) | 自动 (≤ 2, phones ≤ 1.5 on low) | UI |
| 13 | 画面 | 帧率上限 | 60 · 30 | 60 | UI |
| 14 | 画面 | 昼夜光影 | 开 · 关 (§3.8, Q2) | 开 | UI |
| 15 | 画面 | 界面动效 | 完整 · 精简 (no ambient loops) · 关 (no loops, minimal transitions) | 完整 | UI |
| 16 | 画面 | 季节粒子 | 开 · 关 (petals, snow, fireflies in the shell) | 开 | UI |
| 17 | 画面 | 地图城名 | 全部 · 我方与邻近 · 仅选中 | 全部 | UI |
| 18 | 游戏 | 攻击画面 | 开 · 快速 · 关 | 开 | COMBAT / clash · `sanguozhi2_clash_v3` (existing) |
| 19 | 游戏 | 内政演出 | 智能 · 完整 · 精彩 · 快速 · 关 | 智能 | DOMESTIC-FX · `sanguozhi2_fx_v4.domestic` |
| 20 | 游戏 | 仪式动画 | 完整 · 简 (授官, 神兵揭晓, 换月, 颁奖 → 0.6 s fade + stamp) | 完整 | UI (read by TITLES, ITEMS, tournament) |
| 21 | 游戏 | 电脑回合速度 | 正常 · 快 · 极快 (battle AI turns and 委任) | 快 | UI (read by battle-controller) |
| 22 | 游戏 | 单挑观战 | 观看 · 精彩片段 · 只看结果 (AI-vs-AI duels in battle and tournament) | 精彩片段 | UI (read by duel / tournament) |
| 23 | 游戏 | 文字速度 | 慢 (20 字/秒) · 中 (40) · 快 (80) · 即时 | 快 | UI (`SG.UI.say` typewriter) |
| 24 | 游戏 | 对话自动推进 | 关 · 开 (2.5 s + 0.05 s per character) | 关 | UI |
| 25 | 游戏 | 月初自动记录 | 开 · 关 | 开 | UI |
| 26 | 游戏 | 结束本月确认 | 有令牌时询问 · 不询问 | 询问 | UI |
| 27 | 游戏 | 提示与引导 | 开 · 关, button 「重置提示」 | 开 | UI (also clears `sanguozhi2_tip_move`) |
| 28 | 镜头 | 缩放速度 | 50–200 % | 100 | UI (`CameraRig`) |
| 29 | 镜头 | 平移速度 | 50–200 % | 100 | UI |
| 30 | 镜头 | 拖动方向 | 正常 · 反转 | 正常 | UI |
| 31 | 镜头 | 屏幕边缘平移 | 关 · 开 (desktop only) | 关 | UI |
| 32 | 镜头 | 镜头飞行 | 平滑 · 快速 · 直接切换 | 平滑 | UI (map, city view, battle) |
| 33 | 镜头 | 空闲环绕 | 开 · 关 (title, city view) | 开 | UI / CITY-VIEW |
| 34 | 镜头 | 画面震动 | 正常 · 减弱 · 关 | 正常 | UI (read by clash, duel, DOMESTIC-FX seals) |
| 35 | 操作 | 单挑键位 | v4 · v3习惯 | v4 | COMBAT · `sanguozhi2_duel_v4.keys` |
| 36 | 操作 | 格挡键 | 关 · 开 | 关 | COMBAT |
| 37 | 操作 | 招式提示 | 开 · 关 | 开 | COMBAT |
| 38 | 操作 | 招式谱条 | 自动 · 开 · 关 | 自动 | COMBAT |
| 39 | 操作 | 触控按钮尺寸 | 标准 · 大 (+15 %) (touch only) | 标准 | UI |
| 40 | 操作 | 长按时长 | 0.35 · 0.45 · 0.6 秒 (touch only) | 0.45 | UI |
| 41 | 操作 | 惯用手 | 右手 · 左手 (phone: mirrors the city panel and the end-month seal) | 右手 | UI |
| 42 | 操作 | 快捷键一览 | button → key map card | — | UI |
| 43 | 辅助 | 界面字号 | 90 % · 100 % · 115 % · 130 % (scales the root font size; layouts re-flow) | 100 % | UI |
| 44 | 辅助 | 减少动态 | 跟随系统 · 开 · 关 | 跟随系统 | UI |
| 45 | 辅助 | 高对比度 | 关 · 开 (opaque paper, no texture behind text, 3 px outlines, no glows) | 关 | UI |
| 46 | 辅助 | 色觉辅助 | 关 · 红绿 · 蓝黄 (patterns on faction pennants and family bars, gain / loss also as ▲ ▼) | 关 | UI |
| 47 | 辅助 | 闪光 | 正常 · 无闪光 (no white flashes, seal flashes become colour fades) | 正常 | UI |
| 48 | 辅助 | 单挑速度 | 100 % · 85 % · 70 % (the whole duel runs slower, AI included) | 100 % | UI (read by COMBAT `duel-game.js` time scale in battle duels and watched matches; **not in tournament matches the player fights**, which run at 100 % because slowed AI reactions would favour the human in a prize duel — the entry screen notes it; INTEGRATION §8.5) |
| 49 | 辅助 | 朗读 | 关 · 开 (system speech synthesis for dialogue, local only) | 关 | AUDIO |
| 50 | 辅助 | 字幕底板 | 透明 · 实底 | 透明 | UI |

| 51 | 游戏 | 内政委任 | 小事委任 · 全部询问 (ENGAGEMENT R2: minor event cards answered with ★ and listed in the news) | 小事委任 | ENGAGEMENT · `sanguozhi2_engage_v4.delegate` |
| 52 | 游戏 | 节庆照此办理 | button 「清除记忆」, shows the remembered festival choice (ENGAGEMENT R3; the value is in the save, `faction.eng.festivalAuto`) | — | ENGAGEMENT |
| 53 | 游戏 | 武林大会 | 每次询问 · 自动派遣（推荐五人） · 只看结果 | 每次询问 | TOURNAMENT · `sanguozhi2_tourney_v4.entry` |

53 rows (INTEGRATION §10.1). Rows that do not apply are hidden (`when`): desktop-only rows on touch devices and vice versa. A row whose owner module is not loaded does not appear (the registry is the contract).

Interactions: sliders tick (`ui-bead`) every 10 %; 试听 plays a 1.5 s sample of that channel (a guzheng phrase, `ui-coins`, a voice line of the player's ruler); toggles stamp; segments clack. `恢复本页默认` asks once. On the title screen the same window opens with all tabs.

---

## 8. GENERALS 武将录 (request 9)

`06-generals.html` → `shots/06-generals-desk.png`, `06-generals-phone.png`.

**Open**: the 武将 bronze button (`G`), the city panel's 「武将 n」 header (pre-filtered to that city), the ruler medallion's long-press menu.

**Window**: inset 8 px (phone full screen). Header: seal 将, 「武将录」, a count line (`麾下 21 人 · 本月可行动 14 · 可习得 2 · 无官 9`), close. Tabs at the right of the header: **我方 · 他国 · 在野** (other factions' and free generals are read-only and show only what the player knows: 在野 only those found by 搜索; 他国 all, with unknown gear and combo steps as 「？」).

**Filter bar**: search (name / city, pinyin initials) · family segments 全部 / 武 / 谋 / 文 (highest attribute; ties broken as TITLES §2.1) · status multi-select 可行动 · 可习得 · 无官 · 低忠 (< 60) · 外出 · city dropdown (全部城 / one city) · sort dropdown (武力 · 智力 · 政治 · 等级 · 兵力 · 忠诚 · 官品 · 姓名; click a column header to sort, again to reverse) · view toggle list / cards. Filter and sort persist for the session.

**List columns** (desktop; widths as fractions of the remaining width):

| Column | Content |
|---|---|
| 50 px | portrait 42 px in rank frame; 「已行动」 stamp over it when moved this month |
| 1.25 fr | name (Kai 1.1 rem) + 君 / 守 / 新 tags; office name in its family colour (or 「—」) |
| 0.95 fr | `Lv 7` + experience bar |
| 1.6 fr | 武 智 政: number (gear bonus as a small gold superscript `+6`) over a 5 px brush bar with the gold hatch for gear |
| 0.8 fr | troops (serif) and 训 n |
| 56 px | loyalty heart + number (brick-red under 60) |
| 0.8 fr | city (Kai) and role / state (太守, 外出 · 商队) |
| 1.1 fr | six 26 px gear icons with rarity frames (empty = dashed) |

Row height 52 px; ≈ 10 visible at 1280×720; virtualised beyond 40. Click selects (updates the side panel); double-click, Enter or 「详情」 opens the character menu with this filtered, sorted list as its ‹ › sequence.

**Side panel** (300 px): the selected general on a small stage (the same 3D stage as §9.2 at 240 px height, idle pose, no controls), name, 字 and birthplace, office line, a one-line summary (signature weapon, counts of 必杀 / 计策, combo mastery), four actions: 详情 (seal), 赏赐, 装备, 移动.

**Card view**: 120 × 168 cards (portrait 96, name, office, three stat pips, level seal), 7 per row at 1280.

**Batch** (desktop shift / ctrl-click, touch long-press to start): select several generals **in one city** → a bar at the bottom: 赏赐 (gold each, one dialog), 移动 (one destination, one token — v3 移动 rule) and 清除选择.

**Phone**: no side panel; columns portrait · name + office · 武 智 政 · 兵 训 · 忠; tap opens the character menu; filters and sort live in a 「筛选」 sheet; status segment hidden behind it.

---

## 9. Character menu 武将 (request 9)

`07-character.html` → `shots/07-character-desk.png` (总览), `07-character-gear-desk.png` (装备), `07-character-skills-desk.png` (武艺), `07-character-army-desk.png` (部队), `07-character-phone.png`, `07-character-gear-phone.png`.

### 9.1 Frame

- **Open** from: any general row (city panel, 武将录, pickers with a 「详情」 link), a portrait in a dialog or news line (long-press / right-click), the ruler medallion, a battle unit card (read-only during battle). `SG.CharacterScreen.open(gen, { list, tab })`; `list` = the sequence for ‹ ›, `tab` = initial tab (e.g. the agenda opens 武艺).
- **Window**: inset 8 px (phone: full screen). **Header**: ‹ (←, swipe right on the stage) · portrait 52 px in rank frame · name in gold-leaf Kai 2.2 rem · two meta lines (faction pennant · role · office in its family colour; 字 · birthplace · birth year and age) · level seal (gold disc, 「等级」 tag) + experience bar (`1,240 / 2,000`, 老练 +24 %) · › · close.
- **Stage** (left, 300 px; phone 168 px), §9.2.
- **Tabs** (top of the paper page; phone: labels only, 0.8 rem): 总览 · 装备 · 武艺 · 计略 · 部队 · 官爵 · 关系 · 履历. Badges: 「新」 on 武艺 / 计略 when a slot is open, on 装备 when the armoury holds a better item for an empty slot, on 官爵 when he qualifies for a higher seat that is free.
- **Action bar** (bottom, lacquer): 赏赐 · 赐宝物 · 移动 · 授官 · 演武 · (spacer) · 罢免 (ghost; only if he holds an office) · **习得新招** (seal; only when a slot is open, else hidden). Each action uses the existing command with this general preselected (赏赐 and 移动 keep their token rules; disabled with the reason when he is not in an own city, has moved, or no tokens are left). Phone: 赏赐 · 赐宝物 · 授官 + seal; the rest under a 「⋯」 sheet.
- **Voice**: `ui.generalOpen` (AUDIO-V4, pri 1, ≥ 6 s apart).
- **Other factions / free generals**: read-only; tabs 总览 · 装备 (view only) · 武艺 (known steps only, COMBAT §12.1) · 官爵 (read-only) · 关系 · 履历; actions become 登用 (free and found, in an own city) or 拉拢 (ENGAGEMENT 外交 sub-action), both with their usual costs.

### 9.2 The 3D stage

- Content: the general's figure at high LOD (≤ 15 k triangles, DESIGN-V3 budget) wearing his equipped gear (ITEMS §5.2 look overrides, `SG.ItemArt.preview(gen, opts)` when ITEMS is loaded, else `SG.Figure`), on a round wooden platform (lacquer rim, 24 segments, ≈ 600 triangles), two paper lanterns, background a warm paper gradient drawn by the DOM behind a transparent viewport.
- Rendering (one renderer only, DESIGN-V2 §2): the character window pushes a **stage screen** (`SG.Gfx.pushScreen({ scene, camera, render })`) whose `render(renderer)` sets `setScissorTest(true)` and `setViewport`/`setScissor` to the stage element's rectangle (in device pixels, updated on resize) and clears the rest of the canvas to transparent; the window's DOM covers everything else. Light: `SG.LP.Mood('golden')` + the two lanterns as point lights (warm pools on the platform). 60 fps while visible; the screen pauses the render when the stage is scrolled off or another window covers it.
- Controls: drag = rotate (yaw, inertia τ 0.25 s), wheel / pinch = zoom 0.8–1.6, double-click = reset; idle 6 s → slow turntable 12°/s (off under reduced motion or 空闲环绕 关). Pose segments: **待机** (idle), **演武** (plays his 8-step string, COMBAT §12.1 preview, 2.6 s loop), **骑乘** (on his equipped mount; disabled with 「未乘马」 when none), **得胜** (victory pose + gilt motes).
- Gear changes in the 装备 tab swap the parts on the stage with a small ink puff (0.25 s) and the slot sound.
- Fallback (WebGL lost, 画质 低 on a weak phone): the large portrait (256 px, `win` mood) with a painted platform; the pose segments hide.

### 9.3 Tabs

| Tab | Content (owner) | Layout |
|---|---|---|
| **总览** | this spec | two columns: **能力** (武 智 政 with 13 px brush bars to 110, gear hatch, source note `青龙偃月刀 +6`, 「最高：武 → 武官」), **战场与单挑** six stat cards (攻击 ×1.42 · 防御 ×1.33 · 会心 9 % · 计策威力 +24 % · 单挑体力 118 · 机动 5, each with its breakdown line), **招式** chips (必杀 vermillion, 计策 indigo, the open slot as a dashed jade chip, the combo string as glyphs with mastery stars); right column: **官爵** card (insignia, post, family and grade, since when, green effect line), **装备** strip (six 48 px slots, click → 装备 tab), **生平** (the history paragraph with a 「史」 seal and its source; world generals use v3 `generalInfo`, classic generals need new short bios — Q7), **忠诚 · 羁绊** (heart + number, bond portraits with their bond names) |
| **装备** | ITEMS §14.1 content (`SG.Gear.slots / candidates / equip / unequip / summary`, `SG.ItemArt.icon`) | six slot cards in a 2 × 3 paper-doll grid (兵器 · 副手 / 铠甲 · 头盔 / 坐骑 · 宝物), each with icon in rarity frame, slot label, item name in rarity colour, key stats; the selected slot glows; a **candidates column** (府库 first, then this city's market with prices; deltas in green / red; requirement failures greyed with the reason); a **summary row** of before → after numbers (攻击, 机动, 会心, 单挑体力); the **abilities** of equipped rare+ items with 「演示」 (plays the item fx on a dummy in the stage) |
| **武艺** | COMBAT §12.1 content (`SG.Combos.of(gen)`, mastery, knowledge) + 必杀 (v3 `Progress`) | the 招式谱 row: 8 seals (● 轻 teal `--combo-l`, ◆ 中 amber `--combo-m`, ▲ 重 vermillion `--combo-h` — the COMBAT-V4 glyph colours, §3.2; the finisher larger with a gilt ring) with the key for the current device above each (J / K / L or the touch glyph) and two chips below (damage share; safety tag 安全 / 小破绽 / 大破绽 / 可迟滞 / 可被看破 / 终结); right: finisher name in brush Kai, totals line (全中约 28 % · 用时 1.4 秒 · 终结会心 +10 %), mastery stars, 「演武场」 seal (COMBAT §12.4); below: a preview viewport (the stage's 演武 pose, or a second small viewport on desktop) and the 必杀 cards (专属 first; tier tags; open slot card → v3 learn dialog restyled) |
| **计略** | v3 `Progress` + `SG.Tactics` (+ ITEMS / TITLES tactic bonuses) | tactic cards in a grid (name, tier, power with all bonuses, terrain note, success estimate vs an average target), slot line (`已学 2 / 3 · 3 级可学下一计 · 上限中阶`), open slot card; office and item bonuses listed under a 「加成」 line |
| **部队** | this spec over v3 rules | left: **兵力** big number with max (`SG.maxTroops`) and a bar, the city's recruit pool and the 征兵 forecast; **训练** number and bar with the decay note; 「征兵（城）」「训练（城）」 plaques that start the city command with him preselected; right: **惯用阵型** 4 × 2 cards of the eight formations (`Defs.formations`: icon of the unit layout, name, 攻 / 防 / 机动 multipliers, locks with the requirement 「需智 80」), selecting one sets `gen.formation` (the default formation at battle start, v3 field); **兵种 · 坐骑**: the unit type by culture with its figure icon, the mount slot |
| **官爵** | TITLES §9.4 (`SG.TitleUI.tab(gen)`) | hosted as is, in the kit's skin |
| **关系** | ENGAGEMENT §11.4 | a list of bonds (portrait, bond type with its shape icon: red knot 结义, green leaf 知己, blue brush 师徒, purple flame 宿敌, black crack 不和, affinity bar) and a small radial graph with him in the centre; click a portrait → that general's menu |
| **履历** | this spec (data from the v4 logs: `gen.officeLog` (TITLES), battle stats, tournament results (`G.tourney.stats`, titles from `G.tourney.wins`: TOURNAMENT §7.5's per-general record lives here), item acquisitions, ENGAGEMENT achievements) | a vertical timeline of slips (`191年3月 · 汜水关 · 斩 华雄`), filters 全部 / 战功 / 官职 / 宝物 / 大会; a stats strip (战 n · 胜 n · 单挑 胜 n 负 n · 斩将 n · 冠 n) |

Derived numbers in 总览 come from one aggregator so they always match the battle maths: `SG.CharSheet.derive(gen) → { atkK, defK, crit, tacK, duelHp, mob, lines: { atkK: ['老练 +24%', '装备 +14%', …] } }`, implemented in `character-screen.js` by calling the owners (`SG.Progress.vetK`, `SG.Gear.summary`, `SG.Titles.fx`, `SG.BattleMath.unitPreview(gen)` in `js/battle-math.js`, the integration module that also holds `SG.Mods.atkMul` / `defMul` / `critChance`, so the sheet and the battle can never disagree; INTEGRATION §3.2, §17.4). Any owner that is not loaded simply contributes nothing.

### 9.4 Phone

`07-character-phone.png`: header in one line (‹, name, office, level seal, experience, ›, close); stage 168 px without controls (tap = cycle poses); tabs as labels; 总览 shows one column (能力, 战场与单挑, 招式) and the right column moves below; 装备 shows the six slots as a 2 × 3 grid and tapping a slot opens the candidates as a bottom sheet; action bar with three plaques and the seal.

---

## 10. Shop and equipment screens (request 3)

ITEMS owns the content, the rules and `js/shop-screen.js`; this section fixes their layout, motion and sound in the kit. `08-shop.html` → `shots/08-shop-desk.png`, `08-shop-phone.png` (洛阳, tier 4, stock taken from `items.json`: 尚方斩马剑 1 190 = 1 400 × the city factor 0.85, 卅湅环首刀 270, 鱼鳞甲 310, 铁札甲 80 …).

### 10.1 市场 window

| Region | Desktop (1280×720) | Phone |
|---|---|---|
| Awning | a teal-and-paper striped awning along the window's top edge with scalloped hem (CSS gradients) | 14 px stripe |
| Header | market icon · 「洛阳 · 市」 gold-leaf Kai · tier tag (`都会 · 四级`, patina) · tabs 购买 · 出售 · 府库 (`27/40`) · 图鉴 · money (`金 2,140 洛阳府库` and `全军 9,860`) · close | tabs as labels, city gold only |
| Keeper (210 px) | merchant portrait 62 px (a generic merchant per culture: `SG.Portrait` with `{ name: '洛阳贾人', war 22, intel 66, pol 70 }`-style specs), a speech bubble with a context line (a rare in stock: 「尚方作坊流出的斩马剑，十年难遇一回」; a caravan; the festival; nothing new: 「客官随意看看」), shop facts (slots used, next restock, 庙会 in month 3), the caravan card (`胡商在此 · 还有 2 个月 · 舶来货 ×1.5`), 图鉴 progress | hidden (the bubble line becomes a toast on open) |
| Stock grid | 4 columns of 128 px item cards: rarity slot 56 px, name in rarity colour, price with the ingot icon, quantity `×2`, tags 舶来 (teal) / 胡商 (ochre); unaffordable cards at 55 % with 「金不足」 | 3 columns, 96 px cards |
| Detail (350 px) | 86 px slot, name (1.55 rem), type line (`珍品 · 兵器 · 斩马刀 · 汉`), requirement check (`武力需 60 · 关羽 98 ✓`), rarity seal (凡 · 良 · 珍 · 神 · 天), a **compare** line with a general picker (default: the best fit in this city), a stat table with three columns (stat · item value · Δ vs his current item, green / brick-red), the ability box for rare+ (name in brush Kai, effect text, fx line in muted ink), the lore with its source (`——《汉书·杨胡朱梅云传》`), buttons 试装 (paper: shows the item on the general's stage) and **购买 1,190** (seal) | 300 px, lore hidden |

Keyboard: arrows move in the grid, Enter = 购买, `C` cycles the compared general, Tab switches between grid and detail.

### 10.2 Purchase, equip and reveal

| Moment | Motion (timeline) | Sound |
|---|---|---|
| Purchase press | the seal stamps (§4.1); 6–12 coins (by price) fly from the top-beam gold counter (or the city gold in the header) into the item card (arcs, 40 ms stagger, 420 ms each) while the counter rolls down | `ui-stamp`, `ui-coin-out` per coin (max 8) |
| Common / uncommon | the card flips (rotateY 180°, 300 ms) and bursts in its rarity colour (12 paper confetti in the rarity tint) | `coins` (ITEMS) |
| Rare | as above, then the item's model spins on a small light pillar in the stage for 1.2 s (ITEMS §5.5) | rare sting (AUDIO / DOMESTIC-FX `sting:market`) |
| Epic / legendary (spoils, hidden items, prizes) | full-screen reveal: 0–0.4 s ink wash spreads from the centre; 0.4–1.4 s the item silhouette is brushed in (stroke reveal of the icon at 280 px); 1.4–2.0 s colour fills, rarity glow; 2.0 s the seal 「神品」 / 「天宝」 stamps the lower-right corner (scale 1.8 → 1, 160 ms, ink spatter, 3 px shake); 2.2 s the holder's portrait slides in with his cry (`item.epic` / `item.legendary`); holds until tap (min 2.6 s); skippable after 0.6 s | rarity sting, voice |
| After buying | a paper card: 「立即装备给 关羽」 (seal) · 「存入府库」 · 「赠予 …」 (ITEMS §6.6 gift) | — |
| Equip | the icon flies from the list or card to its slot (Bézier, 300 ms), the slot flashes its rarity colour, the stage figure swaps the part with an ink puff | `clink` (weapon) / `rustle` (armour, helmet) / two hoofbeats (mount) / `ui-paper-open` (treasure) (ITEMS §5.5) |

`仪式动画: 简` and reduced motion turn the reveal into a 0.6 s fade + seal, and the coins into a counter roll.

### 10.3 出售, 府库, 图鉴

- **出售**: the faction's items in the armoury and on generals (marked with the holder's portrait), multi-select, sell price per item in this market (`sellPrice`, ITEMS §4.3), total at the bottom, seal 「出售 n 件 · +金」. Signature and bound items cannot be selected (lock with the reason).
- **府库** (also from 武将录's side panel and the character menu's 装备 tab): a 6-column grid with filters by slot, rarity and culture and a sort (rarity, value, recently acquired); each item shows its holder; actions 装备给… (general picker with compare), 赠予 (gift through 赏赐), 出售 (only inside a market).
- **图鉴**: one paper page per culture (18) plus 「君主神兵」 and 「天宝 · 神品」 pages; a grid of silhouettes (never seen = ink silhouette, seen = coloured, owned once = coloured with a small 「藏」 seal); a completion bar per page and the page reward (ITEMS §14.2); tapping an entry opens its history card (source note and references).

---

## 11. Title screens 官制 (request 10)

TITLES §9 owns the content and `SG.TitleUI`; this section fixes the skin. `09-titles.html` → `shots/09-titles-desk.png`, `09-titles-grant-desk.png`, `09-titles-phone.png` (曹操 at rank 公, 国势 7.4, the TITLES §9.2 example board).

### 11.1 官制 board

- **Open**: the 官制 bronze button (`T`), 任命 → 官爵, an agenda slip (opens at the seat), the character menu's 官爵 tab (授官 / 晋升 buttons).
- **Header**: the ruler's portrait in his rank frame (58 px), 「官　制」 gold-leaf, a rank line (`曹操 · 公　国势 7.4 / 10.0 → 王（各品再增一席，开一品）`) over a gold brush bar to the next rank, a status line (`已授 9 / 10 · 本年礼金 450 金 · 本月可任免`), 「按功绩授官」 (plaque; proposes the AI's choices for every fillable seat as a checklist confirm), close.
- **Board** (paper, ink-outlined grid): a 112 px row-header column (grade in Kai 1.35 rem, `used / cap` in serif, requirement line `5 级 · 属性 > 80`), then three family columns with tinted backgrounds and coloured header bands with their icon: 武官 (vermillion, sword) · 谋臣 (indigo, fan) · 文官 (jade, brush). Rows top to bottom 一品 → 九品. Each post: a small seal icon + post name, then its **seat chips** (52 × 58 px; TITLES' 56 × 64 reference px): filled = portrait 34 px + name; **free with an eligible candidate** = dashed ink outline, a lantern pool that breathes (2.2 s) and a vermillion ＋; full = 「名额满」 in muted ink; locked = diagonal hatch + lock seal + reason (`需位阶 王`); suspended = paper hatch with 「虚衔」.
- Interactions: click a free seat → candidate sheet; click a filled seat → the holder's character menu at 官爵; long-press / right-click → 晋升 · 改任 · 罢免. A chip moved into a seat animates 0.35 s ease-out with an ink-splash settle (TITLES §9.3).
- **Phone**: family tabs (武官 · 谋臣 · 文官) in the header; the board shows one family; rows keep the grade column (70 px).

### 11.2 Grant flow

1. **Candidate sheet** (paper slips on the left, 360 px; phone: bottom sheet): every general of the faction for this post's family, eligible first (sorted by merit), each row with portrait, family attribute, level, current office, loyalty and a status chip (「可任」 jade, or the first failed requirement in muted ink: 「需 5 级（现 4）」 · 「武力需 > 80」 · 「最高属性为智力」). Ineligible rows stay visible at 60 % so the ladder is visible (TITLES §9.3).
2. **Edict scroll** (centre, 470 px): unrolls (380 ms) with rods and gold rod ends; the insignia icon (TITLES §9.7, 92 px); the post name in brush Kai (2.4 rem, stroke reveal 0.5 s); the original reading and the seal grade (`pòlǔ jiāngjūn · 金印紫绶 · 孙坚曾任此职`); name chips when the post has several names; a brush divider; effects in two columns with the change in brackets (`攻击 +11%（+3%）`); cost and loyalty (`礼金 300 金（自 许昌）`, `忠诚 +16`); warnings with portraits (`越级者不悦：乐进 −4 李典 −4`); ghost 取消 + big seal 「授　官」; the ruler's seal (`魏公`) printed at the lower right in vermillion with the brush-edge filter.
3. **Ceremony**: TITLES §9.5 (`SG.TitleArt.ceremony`, small / medium / grand); the setting 仪式动画 (row 20) shortens it.
4. **Afterwards**: toast `关羽 拜 荡寇将军` with portrait and the seal 「册」; the chip flies into its seat; the badge on the 官制 button updates. A **rank-up** upgrades the ruler medallion's frame on the top beam (ring colour morph 600 ms + a gold sweep) after its ceremony.

---

## 12. Tournament screens 武林大会 (request 14)

The tournament spec (`design-v4/TOURNAMENT.md`, `SG.Tourney`) owns the event: eligibility, the draw (single elimination, draw size S = 2^⌈log₂N⌉ with 8 ≤ S ≤ 128, byes to top seeds, faction separation), match rules (final best of three, a third-place match), prizes (ITEMS §8.9, ENGAGEMENT §13.5), the stadium (the 平乐观 grand altar with its twelve-tier five-colour canopy, after 《后汉书》's account of 灵帝's 188 review at 平乐观, ringed by sixteen landmark façades) and its music. Its prototype measures a 64 draw with 46 entrants in the classic scenario and a 128 draw with 71 entrants (and 7 play-in matches) in the world scenario. These screens work for any S from 8 to 128. `10-tourney.html` → `shots/10-tourney-desk.png` (bracket), `10-tourney-entry-desk.png`, `10-tourney-vs-desk.png`, `10-tourney-podium-desk.png`, phone versions of the bracket and the podium.

### 12.1 Announcement (month 1)

After the year review (ENGAGEMENT §16.4), an **event card** in ENGAGEMENT §16.2's format with a vermillion header band: a painted vignette of the venue (the stadium at golden hour), 「诏告天下：第{n}届武林大会于{city}{venue}举行，天下英雄，各遣勇士五人」 (templated: `n = G.tourney.count + 1`, `city` and `venue` from the host, INTEGRATION §6.5; e.g. 193: 「第三届武林大会于洛阳平乐观举行」), the prizes line (冠军 稀有兵器 / 甲 / 宝物或招式 + 400 金 + 声望 50 · 亚军 精良品 + 200 金 · 季军 凡品 + 100 金), last year's champion with portrait and title count, and the 金刚杵 note (三冠者得). Buttons: **选派武将** (seal → entry), 「稍后」 (an agenda slip stays until the deadline), 「不参加」. A herald voice line (`ui.tourneyAnnounce`, §14.3) and the tournament fanfare.

### 12.2 Entry 选派出战者

- Left (paper): all own generals sorted by expected result; columns: portrait (44 px, rank frame) · name, level, loyalty · **duel strength** (the tournament's `duelWar`, big serif in 武 colour) · combo glyph string (● ◆ ▲, finisher after ·) · weapon name · recommendation stars (★–★★★ from `SG.Tourney.estimate`: expected round reached). Selected rows have a green inner line and a light green fill; ineligible generals (by the tournament's rules: minimum duel strength, injury, captivity, away on a caravan or expedition) are greyed with the reason.
- Right: **five faction banners** (56 × 120 px swallow-tail flags in the faction colour) — a chosen general's portrait and name appear on his banner; empty banners are hatched with ＋; drag a row onto a banner or tap a row to fill the next banner; tap a banner to remove. Below: expected seeds (`关羽 2 号 · 张飞 3 号 · 赵云 6 号`), the prizes, the champion history line, ghost 「不参加」 and seal **报名**.
- Keyboard: ↑↓ + Space toggles, Enter = 报名.

### 12.3 Bracket 签表

- Header: seal 武, 「第{n}届 武林大会」, `{city} · {venue} · {year}年正月 · {N}人` (e.g. 「第三届 武林大会」 `洛阳 · 平乐观 · 193年正月 · 六十四人`), the host ruler's seal `{hostName}主办`, round tabs 首轮 · 三十二强 · 十六强 · 八强 · 四强 · 决赛 (plus 预赛 when there is a play-in) and **我方** (shows only the paths of own entrants), close.
- Canvas (paper): a window of three rounds at a time (16 players → final on desktop; 8 → final on phones), the two halves mirrored left and right, the final in the centre with a bronze cup, 「决赛 · 三局两胜 · 另有季军战」, a **prize pedestal** (the champion item's silhouette in its rarity frame, revealed at the podium) and the relic line. Match boxes 150 × 58 px: two rows (faction stripe · portrait 22 · name · duel strength); winner's number in vermillion, loser at 45 % and struck through; own entrants with a green inner line; **the current match** with a lantern glow; byes as 「轮空」; future slots as 「待定」. Brush connector lines.
- A **mini-map** of the whole draw (64 or 128) at the top-left with the viewport rectangle; drag the canvas or the rectangle, pinch / wheel to zoom between the 3-round window and the full draw.
- **Next-match card** (right, 300 px; phone 236 px): both portraits (64 px), names, strength and level, a win-estimate bar in the two faction colours (`胜率约 58%`), 「已见典韦招式 5 / 8」 and both combo strings with unseen steps as 「？」 (COMBAT §12.2), buttons **亲自出战** (seal; only when one side is the player's), 观战, 只看结果 (spectating default from setting 22), and the round's results log (`吕布 胜 甘宁（第 2 合 · 终结技「人中吕布」）`).

### 12.4 Match card 对阵

Full screen over the stadium scene (the tournament's intro camera), 2.0 s for spectated matches, waiting for input for the player's: both generals in **angry mood** portraits 210 px (or their 3D figures at the arena gates when the stadium scene is ready), names in brush Kai 3 rem with ink shadow, info pills (faction, strength, level, combo string), the round label on a lacquer pill, a giant brush 「对」 in vermillion, and the three buttons. Sound: crowd swell + taiko hit; voices `tourney.enter` (own) and `duel.accept` (opponent).

### 12.5 During the duel

COMBAT-V4's duel HUD unchanged, plus a **tournament ribbon** at the top centre: round name, both factions' pennants, best-of-three pips in the final (● ● ○). Spectated duels add 「×1 · ×2 · 跳过」 plaques at the bottom right. Between matches a 1.2 s **result card**: the winner's portrait in `win` mood, a 「胜」 seal stamp, the deciding move (`终结技 万众取首`).

### 12.6 Podium 颁奖

The stadium's prize-podium camera (south slot). Three tiers rise from the arena floor in order 2nd → 3rd → 1st (0.8 s each, 0.3 s overlap, spring), each with the placer (3D figure in the victory pose when the scene is loaded, else the portrait in `win` mood, 140 / 104 / 96 px), a paper prize card (item slot + name in rarity colour, gold, prestige; 冠 ×n for the champion), and the tier block (gold 一, silver-grey 二, bronze 三). Then the champion's **「天下第一」** seal slams onto the scene (scale 1.8 → 1, 160 ms back-out, ink spatter, 3 px shake, crowd cheer); the champion item is revealed by ITEMS §10.2 rules (rare: light-pillar spin; the 金刚杵 on a third title: the full legendary reveal). Closing the podium flies the gold and prestige into the top-beam counters. Voices: `tourney.win` (champion), `cmd.reward.thanks` for own placers.

### 12.7 Phone

Bracket window of two rounds (8 → final) plus the next-match card (236 px); entry as a list with the banners in a top strip; match card portraits 120 px; podium scaled 0.62 (`10-tourney-podium-phone.png`).

### 12.8 Hall of fame 英雄榜

In **菜单 → 卷宗 → 英雄榜** (the 卷宗 grid's tile, §6), from the announcement card and from the podium (「历届」); a general's own record is his **履历** tab (§9.3): a scroll table of every tournament (year · venue · champion portrait · 2nd · 3rd), the title counts (冠 ×n per general, with the 金刚杵 holder marked), and records (most titles, longest streak, biggest upset by strength difference).

---

## 13. Motion design

### 13.1 Tokens

| Token | Value | Use |
|---|---|---|
| `--t-instant` | 80 ms | press, toggle snap |
| `--t-quick` | 140 ms | hover, tab content, small fades |
| `--t-base` | 220 ms | spring settles, cards, toasts in |
| `--t-slow` | 380 ms | scroll unroll, panel slide, month name write-on (per 2 characters) |
| `--t-scene` | 650 ms | camera flights, podium rise, beam drop at game start |
| `--e-out` | `cubic-bezier(.2,.8,.2,1)` | entrances, hovers |
| `--e-in` | `cubic-bezier(.5,0,.75,0)` | exits |
| `--e-stamp` | `cubic-bezier(.3,1.6,.5,1)` | seals, badges |
| `--e-spring` | `linear(0, .25 6%, .74 14%, 1.02 22%, 1.06 28%, 1.03 36%, .995 48%, 1.002 62%, 1)` ≈ k 420, c 26, m 1 (DOMESTIC-FX §7.3), fallback `cubic-bezier(.34,1.56,.64,1)` | button release, toggles, tab tick, chip into seat |

`SG.Motion` (in `js/ui.js`): `SG.Motion.reduced` (setting 44 or the media query), `SG.Motion.level` ('full' | 'lite' | 'off' from setting 15), `SG.Motion.play(el, keyframes, { dur, ease, delay })` (Web Animations API, returns a Promise, resolves immediately when reduced), `SG.Motion.seq([...])`, `SG.Motion.stagger(els, fn, step = 18, cap = 216)`.

### 13.2 Choreography rules

1. **One hero motion per moment** (the seal stamp, the month name, the reveal); everything else is subordinate (≤ 60 % amplitude, starts ≥ 60 ms later).
2. **Things come from where they were called**: windows scale in from their button's corner, the agenda from the left, toasts from the right edge of the free area, arrows fly from the quiver to the tile, coins from the counter to the item.
3. **Exits are 0.6–0.7 × the entrance** and use `--e-in`.
4. **Stagger 18–40 ms, total ≤ 240 ms**; lists longer than 12 rows enter as a block.
5. **Input is never blocked for more than 250 ms**: actions start at +120 ms during the release spring; every sequence longer than 0.6 s is skippable by click / tap / Space / Esc.
6. **Amplitude caps**: panel translation ≤ 24 px, control scale 0.94–1.06, rotation ≤ 6° (lanterns ±3°), screen shake ≤ 3 px for ≤ 0.12 s (setting 34).
7. **Only `transform` and `opacity` animate**; glows animate the opacity of pre-blurred pseudo-elements; `will-change` is set only during an animation; no animated `filter`, `box-shadow` or layout properties on elements larger than 64 px.
8. **Reduced motion keeps every piece of information**: counters jump with a 0.3 s colour flash of the delta tag, seals appear without scale, windows fade 120 ms, ambient loops stop, camera flights become cuts with a 0.2 s fade, the month transition becomes a 0.3 s cross-fade + banner.

### 13.3 Screen transitions

| Transition | Motion | Duration |
|---|---|---|
| Game start (after ruler pick) | the beam drops from −70 px (spring), lanterns swing in from 12° and settle, counters roll up from 0, agenda slips slide in | 650 ms |
| City panel open / switch city | slide from the right 24 px + fade; ledger rows stagger; switching cities cross-fades the content (160 ms) and writes the city name on | 280 / 160 ms |
| Window open / close | scrim 180 ms; window from the button corner `scale(.96) translateY(12px)` | 260 / 160 ms |
| Scroll (menu edict, help, AI turns) | unroll with the bottom rod travelling | 380 / 240 ms |
| Tab change | content cross-fade + 8 px slide in the tab direction; the vermillion tick slides | 140 ms |
| Character prev / next | the stage figure slides out 40 px and fades, the new one slides in from the other side; the header cross-fades | 220 ms |
| 入城 / 出城 | CITY-VIEW §2.2 ink wipe | 350 ms |
| Battle start | a horizontal brush banner with the city name, then the battle HUD beam drops | 900 ms |

### 13.4 Performance budget for motion

At most 12 simultaneous ambient loops on the strategy screen, 6 in other windows; one Web Animation sequence at a time per window; counter text updates ≤ 30 Hz; particles (petals, coins, confetti, ink droplets) ≤ 40 DOM nodes at once, pooled; the month transition's particles ≤ 26. Measured target: UI work ≤ 2 ms per frame on the strategy screen at idle, ≤ 4 ms during a counter roll, no long task > 50 ms when opening any window (lists build in idle slices after the first 12 rows).

---

## 14. Sound design for the interface

### 14.1 UI sounds

Synthesized in `js/audio.js`'s SFX table (pre-rendered to buffers once, like the v3 SFX). Levels are peak dBFS before the SFX bus; all except the coin family are mono, centred. The 界面音效 setting (row 5) mutes this table only. Hover sounds play only for mouse pointers and at most once per 80 ms; repeated identical sounds within 60 ms are merged.

| # | Name | When | Recipe | Length | Level |
|---|---|---|---|---|---|
| 1 | `ui-hover` | mouse enters a button | sine 2.2 kHz + 3.3 kHz partial at −9 dB, attack 2 ms, exp decay τ 8 ms | 25 ms | −32 |
| 2 | `ui-press-wood` | plaque / ghost / seal pointer-down | band-passed noise (1.1 kHz, Q 6, 18 ms) + sine 620 Hz (decay 30 ms) | 40 ms | −20 |
| 3 | `ui-press-bronze` | bronze disc pointer-down | FM bell: carrier 1.6 kHz, ratio 1.41, index 2 → 0 over 180 ms | 180 ms | −24 |
| 4 | `ui-press-paper` | paper button pointer-down | high-passed noise (2.5 kHz) with a 2-bump envelope (paper flick) | 60 ms | −24 |
| 5 | `ui-stamp` | seal release, 结束本月, 授官, 报名 | sine 110 → 70 Hz thump (90 ms) + noise HP 2 kHz crush (60 ms, +15 ms) + a wood knock (#2 at −6 dB) | 120 ms | −16 |
| 6 | `ui-deny` | tap on a disabled control | sine 90 Hz 120 ms + square 140 Hz low-passed at 600 Hz, 80 ms, slight detune | 120 ms | −20 |
| 7 | `ui-paper-open` | window / card open | pink noise through a band-pass sweeping 1.5 → 5 kHz, amplitude swell 0 → 1 → 0 | 260 ms | −26 |
| 8 | `ui-paper-close` | window close | the reverse sweep 5 → 1.5 kHz | 180 ms | −28 |
| 9 | `ui-scroll` | scroll unroll | wooden roll rumble (noise LP 400 Hz with 12 Hz tremolo) + a paper sweep (#7) | 380 ms | −24 |
| 10 | `ui-page` | calendar flip, help page turn | noise burst HP 3 kHz, two quick bumps | 90 ms | −26 |
| 11 | `ui-tab` | tab or slip change | two damped sines 1.25 kHz and 1.9 kHz, 35 ms each, 30 ms apart (bamboo clack) | 70 ms | −22 |
| 12 | `ui-toggle-on` | toggle on | small bell 2.4 kHz FM (ratio 2.0, index 1.2 → 0) | 250 ms | −24 |
| 13 | `ui-toggle-off` | toggle off | wood tick (#2 at −4 dB, pitch ×0.8) | 40 ms | −24 |
| 14 | `ui-bead` | slider crossing each 10 % | sine 3.1 kHz 12 ms + a noise tick; pitch +1 semitone per step from 0 % | 15 ms | −30 |
| 15 | `ui-badge` | badge appears or grows | two sines 1.76 kHz + 2.64 kHz, decay 300 ms | 300 ms | −28 |
| 16 | `ui-notify` | agenda slip arrives | wind chime: three pentatonic notes (E6 G6 A6) 70 ms apart, decay 600 ms | 800 ms | −26 |
| 17 | `ui-coins` | gold gain | 3–8 clinks (`n = clamp(⌈log10 Δ⌉ × 2, 3, 8)`), each FM partials 2.8–4.2 kHz (decorative RNG), 25 ms, 45 ms apart, panned ±0.3 | ≤ 400 ms | −22 |
| 18 | `ui-coin-out` | gold spent | two descending clinks + a soft purse thud (sine 160 Hz 60 ms) | 200 ms | −24 |
| 19 | `ui-grain` | food gain | rustle: noise band-pass 3 kHz with a 25 Hz grain envelope | 200 ms | −28 |
| 20 | `ui-token` | a token spent | arrow whoosh (noise band-pass sweep 600 → 3 000 Hz, 220 ms) + thock on arrival (#2) | 260 ms | −24 |
| 21 | `ui-token-refill` | month start | one wood clack per arrow (#11 at −4 dB, pitch rising 1 semitone per arrow), 90 ms apart | ≤ 900 ms | −24 |
| 22 | `ui-month` | month name written | temple bell (inharmonic partials 220 / 552 / 912 / 1 320 Hz, decay 2.5 s) + low gong swell; the culture family of the player's ruler suite may replace the bell with its gong / chime (AUDIO-V4 §A10) | 2.5 s | −18 |

Borrowed, not redefined: DOMESTIC-FX's per-command press sounds and stings (§8), ITEMS' `coins`, `clink`, `rustle`, hoofbeats (§5.5), TITLES' ceremony sounds, the tournament's crowd and fanfares. UI sounds never duck music; voices duck it (AUDIO-V4 §B10.2).

### 14.2 Music in the shell

The shell plays the ruler-suite pieces (AUDIO-V4 Part A): the map piece on the strategy screen, the council piece in 官制 and the character menu (−3 dB, crossfade 0.8 s), the market piece if the suite has one (else the map piece) in 市场, the tournament pieces (tournament spec) in its screens; the menu and settings keep the current piece at −6 dB (low-pass 2.5 kHz while a window is open: "muffled behind the screen").

### 14.3 Voices for menu actions (request 12)

Existing AUDIO-V4 cues used by the shell: `ui.generalOpen` (character menu), `cmd.endMonth` (end-month seal), `title.*` (官制), `item.epic` / `item.legendary` (reveals), `tourney.enter` / `tourney.win`, all `cmd.*` (commands). The six shell cues below are **registered** in `audio/voice-lines.js` (AUDIO-V4 §B2, INTEGRATION §10.2; all priority 1, each with its own cooldown ≥ 20 s, never more than one shell voice per 6 s, off under 语音频率 精简 / 关键 except where noted). The merchant and the herald are not generals: they speak through `SG.Voice.profile('merchant' | 'herald')` (a culture-default voice per lingua):

| Cue | Speaker | Line (中文) | When |
|---|---|---|---|
| `ui.generalsOpen` | ruler | 众将何在？/ 诸君听令！ | 武将录 opens |
| `ui.titleBoard` | ruler | 论功行赏！ | 官制 opens |
| `ui.shopOpen` | merchant (culture default voice) | 客官里面请！/ 新到的好货！ | 市场 opens |
| `ui.save` | the ruler's chief adviser (highest 智力 in the capital) | 已记录在案。 | manual save |
| `ui.tourneyAnnounce` | herald (culture default voice, priority 2) | 天下英雄，会于平乐！ | tournament announcement |
| `ui.monthStart` | ruler | 新的一月，诸事待理。 | month start, only with 语音频率 全部, at most every 3rd month |

---

## 15. Responsive layout and input

### 15.1 Viewports

| Class | Rule | Root font | Layout |
|---|---|---|---|
| wide | ≥ 1600 × 900 | up to 24 px | desktop layout, scales with `rem` |
| desktop | ≥ 1100 wide, ≥ 560 tall | 16 px at 1280×720 | §5.1 |
| tablet | < 1100 wide, ≥ 560 tall (iPad 1024×768) | 15.4 px | desktop layout, agenda as icons, 武将录 side panel hidden below 1000 px |
| short (`html.sg-short`) | height < 560 (phones 844×390, 667×375) | 13 px | phone layouts (§5.9 and each screen's phone section) |
| portrait phone | `orientation: portrait` and width < 600 and coarse pointer | — | the v3 rotate hint (kept) |

Safe areas: every edge-anchored element adds `env(safe-area-inset-*)` (v3 variables `--sg-sat` …); the phone top beam sits below the notch inset in landscape (left / right insets of 44–48 px on iPhones are added to the beam's padding, not to its height).

**Inset viewport** (the worst phone case, tested): 844 × 390 with **47 px left and right insets** leaves 750 × 390 of usable width. Everything still fits by this **overflow order** (each step applies only if the previous one did not make room): 1 the agenda collapses to its 44 px icon column; 2 the calendar goes compact (`正月` + icon); 3 the top beam's bronze buttons lose their gaps (4 → 2 px) and the 城 counter hides behind the 金 · 粮 popup; 4 the ledger column of the city panel narrows from 214 to 180 px (stat bars keep their numbers, labels go to icons); 5 the end-month seal shrinks 62 → 56 px. The 4 × 4 dock never shrinks below 56 px tiles and touch targets never below 44 px.

### 15.2 Pointer and touch

- Touch targets ≥ 44 × 44 CSS px everywhere (Apple HIG); the setting 触控按钮尺寸 大 scales interactive elements by 1.15.
- Long-press (setting 40, default 0.45 s) = hover on touch: name tags, tooltips, quick-action menus; a ring fills around the finger during the press.
- Swipes: left on an agenda slip dismisses it; left / right on the character stage = prev / next; down on a sheet closes it.
- Pinch on the bracket and on the character stage zooms; two-finger pan in lists is native scroll with momentum.
- 惯用手 左手 mirrors the city panel to the left and the end-month seal to the right on phones.

### 15.3 Keyboard (desktop)

| Key | Action | Scope |
|---|---|---|
| `G` `T` `P` `H` `O` | 武将 · 官制 · 势力 · 大事 · 设置 | strategy screen, no window open |
| `Esc` | close the top window / card; with none open: 菜单 | everywhere |
| `Enter` | the window's seal button; on the bare map: 结束本月 (with its confirm) | — |
| `1–9 0 - =` `Z X C V` | dock commands (§5.4) | city panel open |
| `M` | mute / unmute all | everywhere |
| `Ctrl/⌘ + S` | quick save | strategy screen |
| `← →` `Ctrl+Tab` | prev / next tab; in the character menu `[` `]` = prev / next general | windows |
| `↑ ↓` `1–9` `Enter` `Space` | lists and v3 dialogs (unchanged) | dialogs |
| `WASD` `Q` `E` (`R` `F` in the city view) | camera (unchanged) | map |

Focus order follows reading order (beam left → right, agenda, panel header → ledger → dock rows, seal); windows trap focus (v3 `trapTab`); the focused element shows the gold ring and the lantern pool.

---

## 16. Accessibility

- **Contrast**: all text tokens ≥ 4.5 : 1 on their surfaces (§3.2 table); `tests/ui-v4.html` recomputes them from the live CSS. 高对比度 (row 45) removes textures behind text, makes paper opaque, thickens outlines to 3 px and drops glows.
- **Text size**: row 43 scales the root font (90–130 %); every layout is tested at 130 % on desktop and 115 % on phones (`tests/ui-layout.html`).
- **Motion**: rows 15 and 44; `prefers-reduced-motion` is the default for 减少动态 跟随系统. No information is carried by motion alone.
- **Flashes**: no full-screen white flash anywhere; ≤ 3 flashes per second (DOMESTIC-FX §3.9); 无闪光 (row 47) turns seal flashes into colour fades.
- **Colour vision**: family colours always come with their characters (武 / 智 / 政 / 武官 …), rarity with its name and seal (凡 良 珍 神 天), gain / loss with + / − (and ▲ ▼ under 色觉辅助), faction colours with the pennant shape; 色觉辅助 adds hatch patterns to faction pennants and bars (red-green: diagonal hatch on 武 and 政 bars; blue-yellow: dots on 智).
- **Timing**: nothing in the shell is timed; duels can be slowed (row 48); dialogue auto-advance is off by default (row 24).
- **Screen readers** (best effort for a game): buttons carry `aria-label` (icon buttons use their label), windows are `role="dialog" aria-modal="true"` with their title, tabs `role="tablist"`, toasts go to an `aria-live="polite"` region, disabled commands keep `aria-disabled` with the reason as `aria-description`.
- **Subtitles** for every voice line (AUDIO-V4 §B10.5); 字幕底板 实底 (row 50).

---

## 17. Implementation plan

### 17.1 Phases (browser)

| Phase | Work | Done when |
|---|---|---|
| P0 Kit | tokens in `style.css`; `ui-art.js` (textures, caps, filters); `ui-icons.js` (data + SVG cache; DOMESTIC-FX command icons included); `SG.UI` v4 components (§4); restyle of every existing `sg-*` class; `tests/ui-v4.html` | every v3 screen renders in the new skin with no layout change; `node tests/sim.js` untouched; no `pageerror` |
| P1 Shell | `shell.js`: beam, counters, quiver, calendar, agenda, end-month seal, month transition, map labels, city panel + dock (4 × 4), phone layout; `strategy-screen.js` delegation | `03-main` desktop / phone / night / month mockups reproduced from the real game at seed 7 |
| P2 Menu, saves, settings | `menu-screen.js` (slots, 卷宗 grid, help), `GameState` slot API (model owner), `settings.js` + `settings-screen.js`, the UI-owned rows wired (camera, text speed, frame cap, pixel ratio …), registry rows from siblings as they land | `tests/settings.js` and the save tests pass; v3 save loads from the 读取 page |
| P3 Generals | `generals-screen.js`, `character-screen.js` frame + stage + 总览 / 计略 / 部队 / 关系 / 履历; hosts for 装备 (ITEMS), 武艺 (COMBAT), 官爵 (TITLES) with placeholders until they land | every general opens from every list; prev / next works; stage 60 fps on desktop, ≥ 30 fps on iPhone-class |
| P4 Title, loading, battle skin | `game.js` title; loading screen; battle / clash / duel HUD skin (classes only) | — |
| P5 Feature screens | 市场 / 府库 / 图鉴 (with ITEMS), 官制 board / grant / ceremony hooks (with TITLES), 武林大会 screens (with the tournament spec) | the corresponding mockups reproduced |
| P6 Polish | UI sounds and voices, accessibility rows, performance pass, `tests/ui-layout.html` green at 5 viewports, Unity export | §19 targets met |

### 17.2 CSS architecture

`css/style.css` (tokens + base + restyled v3 classes) → `css/ui-v4.css` (components, shell, screens). Naming stays `sg-*` (new: `sg-beam`, `sg-quiver`, `sg-calendar`, `sg-agenda`, `sg-slip`, `sg-dock`, `sg-tile`, `sg-endseal`, `sg-window`, `sg-page`, `sg-scroll`, `sg-tabs`, `sg-tab`, `sg-seg`, `sg-toggle`, `sg-slider`, `sg-counter`, `sg-delta`, `sg-badge`, `sg-slot`, `sg-face`, `sg-tip`, `sg-sheet`, `sg-plaque`, `sg-seal`, `sg-char-*`, `sg-gen-*`, `sg-shop-*`, `sg-title-*` (the board; the title screen keeps `sg-title`), `sg-tourney-*`). `UI-CLASSES.md` gets a v4 section listing them with their HTML structure (as v3 did). The modules inject no CSS of their own except one-off keyframes (the v3 `injectStyle` pattern is retired for shell styles).

### 17.3 APIs (summary)

```js
// js/ui.js (v4 additions)
SG.UI.button(parent, html, onClick, { kind, icon, label, why, badge, sound, hotkey, size })
SG.UI.tabs(parent, items, onChange) · SG.UI.segmented(…) · SG.UI.toggle(…) · SG.UI.slider(…)
SG.UI.window(id, opts) → { el, page, setTabs, close, onClose } · SG.UI.scroll(opts) · SG.UI.sheet(opts)
SG.UI.counter(el, opts) → { set(v, { delta: true }) } · SG.UI.badge(el, v) · SG.UI.tip(el, html)
SG.UI.face(gen, { size, rank, mood }) · SG.UI.slot(item | null, { size, label })
SG.Motion.{ reduced, level, play, seq, stagger }
// js/ui-art.js, js/ui-icons.js
SG.UIArt.init() → Promise · SG.UIArt.ready · SG.UIArt.lantern(size, lit) · SG.UIArt.seal(text, opts)
SG.UIIcons.def(id, def) · SG.UIIcons.svg(id, size) · SG.UIIcons.el(id, size) · SG.UIIcons.list()
// js/settings.js
SG.Settings.define(def) · get(id) · set(id, v) · on(id, fn) · rows(tab) · reset(tab) · export() · import(o)
// js/shell.js
SG.Shell.build(strategyScreen) · refresh(g) · cityPanel(c) · addCmd(name, why, action, opts) · agenda.add(slip) · agenda.refresh(g)
SG.Shell.spendToken(fromEl, toEl) · SG.Shell.monthTransition(g) → Promise · SG.Shell.setNight(v)
// screens
SG.MenuScreen.open(page?) · SG.SettingsScreen.open(tab?) · SG.GeneralsScreen.open({ city, filter })
SG.CharacterScreen.open(gen, { list, tab }) · SG.CharacterScreen.registerTab(id, { label, icon, build(gen, el), badge(gen) })
SG.TourneyScreen.announce(t) · entry(t) → Promise<gens> · bracket(t) · match(m) → Promise<mode> · podium(result)
SG.CharSheet.derive(gen) → { atkK, defK, crit, tacK, duelHp, mob, lines }
// test hook (only with ?uitest=1)
SG.UITest.open(screen, opts) · SG.UITest.layoutReport() → { overlaps, small, clipped, offscreen }
```

`registerTab` is how ITEMS (装备), COMBAT (武艺) and TITLES (官爵) plug their content into the character menu without editing `character-screen.js`.

### 17.4 INTEGRATION (changes in files owned by others)

| File | Function | Change |
|---|---|---|
| `js/strategy-screen.js` | `buildHud` | replace the v3 top bar and city panel construction with `SG.Shell.build(this)`; drop the 记录 and 音乐 buttons |
| `js/strategy-screen.js` | `refreshAll`, `refreshCityPanel` | call `SG.Shell.refresh(g)` / `SG.Shell.cityPanel(c)`; keep the command-availability logic (`why` strings) and pass it through `SG.Shell.addCmd` |
| `js/strategy-screen.js` | `addCmd` | build tiles through `SG.CmdIcons.button` (DOMESTIC-FX) with the dock order of §5.4; keep `data-cmd`, `data-why`, `explainCmd`, `hintCmd` |
| `js/strategy-screen.js` | `confirmEnd`, month loop after news (`G().save()`, `UI().banner(...)`) | `SG.Shell.monthTransition(g)`; save via `GameState.saveSlot('auto')` when setting 25 is on |
| `js/strategy-screen.js` | `showGeneral` | `SG.CharacterScreen.open(gen, { list })` |
| `js/game.js` | `titleFlow`, `loadingOverlay`, `HelpText` | v4 title (§5.11), slot-based continue / load, help chapters |
| `js/model.js` | `GameState.save / load / saveInfo / hasSave` | slot API: `saveSlot(slot)`, `loadSlot(slot)`, `listSaves()`, `deleteSlot(slot)`; temporary-key write then swap; v3 keys read-only |
| `js/camera.js` | `CameraRig` | fields `zoomK`, `panK`, `invertDrag`, `edgePan`, `flyMode`, `idleOrbit` read from `SG.Settings` |
| `js/art.js` | `Gfx` | `setPixelRatioCap(k)`, `frameCap(fps)` (30 or 60), viewport / scissor support for a pushed screen (character stage) |
| `js/audio.js` | SFX table, buses | the 22 `ui-*` recipes (§14.1); `SG.Sfx.uiOn` flag; bus volumes from AUDIO's settings |
| `js/battle-controller.js` | AI turn pacing | read 电脑回合速度 (row 21); HUD classes (§5.10) |
| `js/duel-game.js` | time step | multiply by 单挑速度 (row 48); shake amplitude from row 34 |
| `js/clash.js` | `Clash.mode` | exposed as settings row 18 (key unchanged) |
| `js/battle-model.js` | — | `SG.BattleMath.unitPreview(gen)`: pure attack / defence / crit multipliers for the character sheet (no state change) |
| `js/map-view.js` | city labels, mood | label skin through `SG.Shell`; optional `setMood(hour)` for §3.8 (Q2) |
| `index.html` | head, scripts | `ui-v4.css`; script order (§1); loading markup |
| `UI-CLASSES.md` | — | v4 section (§17.2) |

### 17.5 Performance budgets

| Item | Budget |
|---|---|
| Strategy-screen DOM | ≤ 900 nodes (v3 ≈ 600 with labels); city labels stay one node + text each |
| Texture generation | ≤ 25 ms desktop / ≤ 70 ms iPhone total, ≤ 8 ms per idle slice; ≈ 120 KB of data URLs; decoded ≤ 2 MB |
| Icons | ≤ 140 definitions, ≤ 60 KB of source; SVG strings cached per (id, size) |
| UI per frame | ≤ 2 ms idle, ≤ 4 ms during counters / sequences; ambient loops on the compositor only |
| Window open | first paint ≤ 50 ms; lists beyond 12 rows built in idle slices; character stage figure built asynchronously (placeholder portrait first) |
| Character stage | ≤ 16 k triangles, ≤ 12 draw calls, 60 fps desktop, ≥ 30 fps iPhone-class; paused when hidden |
| Memory | portraits use v3 caches; item icons share ITEMS' atlas; save thumbnails decoded only on the save pages |

---

## 18. Unity port (after the browser build is stable)

- **Tokens and data**: `tools/export-unity-ui.js` writes `UIStyle.g.cs` (all colour tokens, durations, easing control points, sizes in reference px), `UIIcons.g.cs` (the icon primitive lists, §3.6) and `UISettings.g.cs` (the 50 row definitions with defaults); `--check` mode fails CI when the web data changed without an export (as `export-unity-portraits.js` does).
- **Textures**: `UIArt.cs` re-implements the paper / lacquer / silk / patina generators with the same xorshift seeds and draw lists into `Texture2D` (pixel loops), and draws the corner caps, brush lines, seals and lanterns with the existing `View/Raster.cs` (it already supports SVG path strings, gradients and strokes); frames become 9-sliced sprites (lacquer with caps in the corners).
- **Icons**: rasterised at 64 and 128 px by `Raster.cs` from `UIIcons.g.cs` into one atlas at first use (≈ 151 icons incl. DOMESTIC-FX's 11 addendum icons, 2 × 1 MB), hover micro-loops as small `UITween` animations of the group transforms.
- **Fonts**: as v3 (`Font.CreateDynamicFontFromOSFont` with the Kai list for titles); stroke thickening with a 1 px `Outline` in the text colour; the brush effect for the few hero strings via a UI shader with a noise-displacement pass (`UI/BrushText`).
- **Motion**: `UITween.cs` (no external packages) evaluates the same cubic-Béziers and the spring (k 420, c 26); `UIMotion.Reduced` from the setting or `Application.isMobilePlatform && …` accessibility flags.
- **Screens**: `Strategy/Shell.cs` (beam, agenda, panel, dock, seal, month transition), `UI/MenuScreen.cs`, `UI/SettingsScreen.cs`, `UI/GeneralsScreen.cs`, `UI/CharacterScreen.cs` (the stage is a second camera rendering into a `RenderTexture` shown in a `RawImage`; Unity allows this cheaply), `UI/TourneyScreen.cs`. Settings in `PlayerPrefs` under the same keys and JSON shapes; save slots as files in `Application.persistentDataPath` (`save_auto.json`, `save_1.json` …, thumbnails as PNG).
- **Tests**: `Tests~/ui`: token / icon hash parity with the web export, a headless layout pass of the strategy screen at 1600×900 and 844×390 reference sizes (overlap and touch-target checks as in §19), CPU-rendered icon sheet compared with the browser's `tests/ui-v4.html` capture (per-icon mean pixel difference ≤ 3 %).

---

## 19. Test plan and targets

### 19.1 Automated

| Test | Checks | Target |
|---|---|---|
| `tests/ui-v4.html` (browser, self-checking) | renders the component sheet from the real code; contrast of every text token on its surfaces; icon silhouettes at 24 px pairwise ≥ 18 % different (DOMESTIC-FX §6.1); every icon path parses; texture generation slice times; each button kind has the 7 states; reduced-motion produces no running animations | all green; textures ≤ 8 ms per slice |
| `tests/ui-layout.html` (browser) | loads `index.html?seed=7&uitest=1` in iframes at 1920×1080, 1280×720, 1024×768, 844×390, **844×390 with 47 px left / right safe-area insets** (the overflow order of §15.1), 667×375 (and 1280×720 at 130 % text) and opens 14 screens through `SG.UITest.open`: title, strategy (no city / city), dock disabled states, agenda full, month transition, menu (save, 卷宗, help), settings (each tab), 武将录, character (each tab), 市场, 官制, tournament (entry, bracket, match, podium) | no overlaps among beam / agenda / panel / seal / toasts; interactive elements ≥ 44 × 44 px on touch viewports and ≥ 32 × 32 on desktop; no text node < 13 px on short viewports; no clipped single-line labels (`scrollWidth > clientWidth`); nothing outside safe areas; no horizontal scroll; no `pageerror` or `console.error` |
| `tests/settings.js` (Node) | 53 rows registered (with stubs for sibling owners; INTEGRATION §10.1), defaults, round trip through a fake `localStorage`, corrupted JSON → defaults, throwing storage (private mode) → in-memory, `export` / `import` identity, `reset(tab)` | exit code 0 |
| `tests/saves.js` (Node) | 4 slots write / read / delete / list, quota error with a 1 MB fake storage leaves the old slot intact, v3 save listed and loaded through migration, slot index rebuilt if missing | exit code 0 |
| `node tests/sim.js`, `tests/world.js`, `tests/duel-bots.js` | unchanged: the UI never touches rules or `SG.Random` | ALL OK as before |
| Screenshot review | `tools/ui-shots.mjs` (dev only, Playwright as in `design-v4/mockups/shoot.mjs`) captures the 30 mockup situations from the real game | reviewed against `design-v4/mockups/shots/` |

### 19.2 Performance targets (measured in the browser on a 2020 laptop and an iPhone 12)

| Measure | Target |
|---|---|
| strategy screen idle, UI main-thread time | ≤ 2 ms / frame |
| counter roll + token flight | ≤ 4 ms / frame, no dropped frames at 60 fps on desktop |
| opening 武将录 with 120 generals (world) | first paint ≤ 50 ms, complete ≤ 250 ms in slices |
| character menu open → stage figure visible | ≤ 300 ms (portrait placeholder at ≤ 50 ms) |
| month transition (skippable) | presentation ≤ 3.2 s with skip available after 0.25 s; all non-interactive month-start beats ≤ **8 s** together (INTEGRATION §6.6), no JS slice > 8 ms |

### 19.3 Playtest targets (5 players × 30 minutes, desktop and phone)

| Target | Measure |
|---|---|
| "It no longer looks like an AI game" | in a blind side-by-side of v3 and v4 screenshots, ≥ 4 of 5 pick v4 as "hand-made / warm / designed", and nobody describes it with "template", "generic" or "dashboard" |
| Generals reachable | open a named general's equipment from the main screen in ≤ 3 clicks / taps (武将 → row → 装备) and ≤ 10 s |
| Settings reachable | change the music volume in ≤ 3 clicks (设置 → 音频 slider is the first page) and ≤ 8 s |
| Saving understood | every player finds 记录 and saves into a manual slot without help |
| Disabled commands understood | 0 players call a grey command "broken"; they tap it and read the reason |
| End month | ≤ 2 clicks with tokens left, 1 without; nobody ends a month by accident (confirm with tokens left) |
| Phone ergonomics | all strategy actions reachable without regrip (right thumb dock, left thumb seal); no mis-taps between adjacent tiles in 30 minutes (≥ 4 px gaps, 56 px tiles) |
| Motion comfort | nobody turns 界面动效 off in the first 30 minutes; if ≥ 2 of 5 do, cut the ambient loops by half |

---

## 20. Open questions

- **Q1. Bundle a Kai font?** Mac / iOS / Windows have system Kai; Android / Linux get Noto Serif SC. A bundled OFL Kai subset (LXGW WenKai: ≈ 600 glyphs of fixed UI strings ≈ 250 KB; all general names ≈ 1 600 glyphs ≈ 650 KB, as base64 in a script so no network is used) would make all platforms match the mockups. Proposed: no for v4.0, revisit after a phone playtest.
- **Q2. A day passing on the strategy map** (§3.8): the clock itself is decided (07:00–20:00, INTEGRATION §10.7); the map renderer still has to adopt `SG.LP.Mood` (art pilot / map owner). The UI works either way (`--night` = 0).
- **Q3. Agenda size**: 4 slips is the cap; should players be able to hide the agenda entirely (a setting), or is the icon-collapse enough?
- **Q4. Save export / import**: add 「导出记录」 (download the slot JSON) and 「导入记录」 (file picker) to the menu for backups and moving between devices? It needs no network; it does add a file-input path to test.
- **Q5. Character stage on weak phones**: the static-portrait fallback loses the "see your gear on the model" payoff of request 9; should 画质 低 still render the figure at 30 fps with no shadows instead?
- **Q6. Dock contents** — resolved: 16 fixed tiles (治 / 人 / 运 / 策), no 授官 tile; DOMESTIC-FX §7.1 now lists exactly this grid.
- **Q7. Bios for classic generals**: world generals have v3 `generalInfo`; the ~160 classic generals need short, sourced bios (a research pass) or the 生平 block hides for them.
- **Q8. City-view dock side** — resolved: right on desktop; the phone bottom bar with the four group stripes (CITY-VIEW §2.4 edited, §5.9).
- **Q9. Tournament numbering** — resolved: the first is held in 191, `n = G.tourney.count + 1`, so 193 = 第三届 in every spec (INTEGRATION §6.5).
- **Q10. New voice cues** — resolved: registered in `voice-lines.js`; merchant and herald through `SG.Voice.profile` (AUDIO-V4 §B4).
- **Q11. Title-screen music**: play the last save's ruler suite under the title (a personal touch) or always the title theme?
- **Q12. Light faction colours as text on paper**: the darkened `-t` variant keeps names legible but shifts some hues (yellow factions read ochre). Acceptable, or give every faction an authored text colour in `world-data.js`?

---

## 21. Sources

Research for the visual language and the tournament venue (web lookups in this session; verify quotations against printed editions before showing them to players):

- Hunan Museum, *The Flowing Charm of Charcoal and Cinnabar — the beauty of lacquerware in the Chu and Han dynasties* (red and black as the foundational lacquer colours; Mawangdui): https://web.hnmuseum.com/en/node/10179
- *Characterization and identification of lacquer films from the Qin and Han dynasties*, BioResources (carbon black and cinnabar as the pigments of red-black lacquer): https://bioresources.cnr.ncsu.edu/resources/characterization-and-identification-of-lacquer-films-from-the-qin-and-han-dynasties/
- World History Encyclopedia, *Chinese Lacquerware* (cinnabar red; red, black and yellow as the main lacquer colours): https://www.worldhistory.org/article/1090/
- Fukuoka City Museum via Google Arts & Culture, the gold seal 「漢委奴国王」 (seal materials and ribbon colours by rank; used for the seal and 印绶 motifs): https://artsandculture.google.com/asset/the-gold-seal%E2%80%9Ckan-no-wa-no-na-no-kokuou%E2%80%9D-han-dynasty/-gG-6FNFVDNmvQ
- 《汉书·武帝纪》元封六年「京师民观角抵于上林平乐馆」(corpus entry): https://chinesenotes.com/words/135028.html
- 平乐观 and 灵帝's review of 中平五年 (188) with the great altar and the twelve-tier five-colour canopy (《后汉书·何进传》, secondary summaries): https://baike.baidu.com/item/%E5%B9%B3%E4%B9%90%E8%A7%82/24437524 , https://zh.wikipedia.org/zh-hans/%E4%BD%95%E8%BF%9B
- 《三国志·蜀书·关羽传》 for the sample bio in `07-character.html` (「关羽字云长，本字长生，河东解人也……先主与二人寝则同床，恩若兄弟」), quoted from memory of the standard text: check against the 中华书局 edition.
- The command-token icon (令箭, a command arrow) follows the 《三国演义》 stage image of commanders issuing arrows; it is a game convention, not a Han-period object.

Interface standards:

- W3C, *Web Content Accessibility Guidelines 2.2* (contrast 1.4.3, target size 2.5.8, animation from interactions 2.3.3): https://www.w3.org/TR/WCAG22/
- Apple, *Human Interface Guidelines — Accessibility* (44 × 44 pt minimum hit target): https://developer.apple.com/design/human-interface-guidelines/accessibility
- MDN, `prefers-reduced-motion`: https://developer.mozilla.org/en-US/docs/Web/CSS/@media/prefers-reduced-motion
- MDN, the `linear()` easing function (spring approximation): https://developer.mozilla.org/en-US/docs/Web/CSS/easing-function/linear
- LXGW WenKai (SIL OFL 1.1), the stand-in Kai font used only for the mockup screenshots: https://github.com/lxgw/LxgwWenKai

In-repo references: the art pilot palette and outline colour (`js/lowpoly.js`, `js/lp-pilot.js`, renders in the art-pilot scratch folder: the 宾阳楼 plaque `#2E4466` with gold `#F2C45C`, the city-gate plaque, lantern and pillar reds), `UI-CLASSES.md`, `DESIGN-V2.md` §1, and the sibling v4 specs named at the top.

---

## Appendix A. Mockup index

All files in `design-v4/mockups/`; open any `.html` directly (`file://`), add `?phone` for the 844×390 layout and `?nonotes` to hide the yellow annotation notes. `node design-v4/mockups/shoot.mjs [filter]` regenerates the screenshots.

| Mockup | Shows | Screenshots |
|---|---|---|
| `01-kit.html` | palette, materials, type scale, all button kinds and states, bronze discs, controls, command tiles in all states, tabs, bars, counters with delta tags, rank frames R1–R5, rarity slots, tooltip, toast, the icon set | `01-kit-desk.png` (full page) |
| `02-title.html` | title screen with brush title, seal, hanging menu board and 继续 card | `02-title-desk.png`, `02-title-phone.png` |
| `03-main.html` | the strategy screen: beam, medallion, plaque, calendar, quiver, counters with a +120 tag, bronze buttons with badges, agenda, paper city labels, selected-city plaque and ring, city panel with ledger and 4 × 4 dock (a 大成 echo, a hovered tile, locked tiles), end-month seal, toast; `?night`, `?month` | `03-main-desk.png`, `03-main-phone.png`, `03-main-night-desk.png`, `03-main-month-desk.png` |
| `04-menu.html` | menu window with the save page (auto + 3 slots, thumbnails) | `04-menu-desk.png`, `04-menu-phone.png` |
| `05-settings.html` | settings window, 音频 tab | `05-settings-desk.png`, `05-settings-phone.png` |
| `06-generals.html` | 武将录 with filters, columns, gear icons, side panel with the 3D stage stand-in | `06-generals-desk.png`, `06-generals-phone.png` |
| `07-character.html` | character menu; `?tab=gear`, `?tab=skills`, `?tab=army` | `07-character-desk.png`, `07-character-gear-desk.png`, `07-character-skills-desk.png`, `07-character-army-desk.png`, `07-character-phone.png`, `07-character-gear-phone.png` |
| `08-shop.html` | 市场 of 洛阳 with real `items.json` stock and prices, detail with compare | `08-shop-desk.png`, `08-shop-phone.png` |
| `09-titles.html` | 官制 board (TITLES §9.2 example); `?view=grant` candidate sheet and edict | `09-titles-desk.png`, `09-titles-grant-desk.png`, `09-titles-phone.png` |
| `10-tourney.html` | bracket with next-match card; `?view=entry`, `?view=vs`, `?view=podium` | `10-tourney-desk.png`, `10-tourney-entry-desk.png`, `10-tourney-vs-desk.png`, `10-tourney-podium-desk.png`, `10-tourney-phone.png`, `10-tourney-podium-phone.png` |

The v3 screenshots used for the audit (§2) are in `design-v4/ui/current/`.
