# Lanternvale — UI panels & menus

`Assets/Lanternvale/Scripts/Game/UI/Panels/**` (namespace `Lanternvale.Game.Panels`) — every window of the game drawn
with the `Ui` toolkit: Paper panels for menus, sheets and dialogue, Ink for tooltips and context menus. Each screen is an
`IUiScreen` discovered by `UiRoot` (toggled panels extend `UiPanel` through `PanelWindow` and use the `UiPanels` ids;
state-driven screens use `Id ""`). Screens talk only to `GameFlow.Instance`, its `Session` and `Combat` (+ static
presentation helpers: `ArtLibrary`, `Sfx`, `GameAudio`, `Ui`, `UiText`).

## Screens

| Screen (file) | Order | Shown when | Modal |
|---|---|---|---|
| `PanelHost` (PanelHost.cs) | 100 | never drawn — per-frame housekeeping (below) | – |
| `VendorScreen` (VendorScreen.cs) | 110 | `Session.ActiveVendor` (exploration); opens the bags beside it | – |
| `TrainerScreen` (TrainerScreens.cs) | 111 | `Session.ActiveTrainer` | – |
| `CharacterPanel` **C** | 120 | `UiPanels.Character` | – |
| `SpellbookPanel` **P** | 122 | `UiPanels.Spellbook` | – |
| `PartyPanel` | 124 | `UiPanels.Party` (Journal button) | – |
| `InventoryPanel` **I / B** | 126 | `UiPanels.Inventory` | – |
| `JournalPanel` **J** | 130 | `UiPanels.Journal` | – |
| `MapPanel` **M** | 132 | `UiPanels.Map` | – |
| `TalentsPanel` **N** | 140 | `UiPanels.Talents` | – |
| `LootScreen` | 150 | `Session.PendingLoot` (exploration) | – |
| `LevelUpPopup` | 160 | `LevelUp` events (bottom right, waits for dialogue to end) | – |
| `RespecScreen` | 200 | `Session.ActiveRespecNpc != ""` | – |
| `QuestRewardScreen` | 205 | `Session.PendingQuestRewards` not deferred (exploration) | ✓ |
| `DialogueScreen` | 220 | `Session.Mode == Dialogue` (+ a lingering d20 roll) | ✓ |
| `PauseMenuPanel` **Esc** | 250 | `UiPanels.Pause` (GameFlow pauses time) | ✓ |
| `SaveLoadPanel` | 255 | `UiPanels.SaveLoad` (`SaveLoadPanel.OpenFor(save)`) | ✓ |
| `SettingsPanel` | 256 | `UiPanels.Settings` | ✓ |
| `HelpPanel` **F1** | 257 | `UiPanels.Help` | – |
| `MainMenuScreen` | 300 | no game and `GameRoot.Mode != CharacterCreation` | ✓ |
| `CharacterCreationScreen` | 310 | no game and `GameRoot.Mode == CharacterCreation` | ✓ |
| `GameOverScreen` | 340 | `Session.Mode == GameOver` | ✓ |
| `ContextMenuScreen` | 460 | right-click menus (`ContextMenuScreen.Open`) | – |
| `ConfirmScreen` | 470 | yes/no prompts (`ConfirmScreen.Ask`) | ✓ |
| `NoticeScreen` | 480 | feedback lines (`PanelKit.Notice`) | – |

Shared bodies: `SaveSlotsView` and `SettingsView` (MenuViews.cs) are used by the main menu, the pause panels and the
game-over screen. `PanelArt` makes the procedural textures (d20, arrow heads, discs, check marks, gradients).

## What each window does

* **Main menu** — logo (`logo_lanternvale`, Title text fallback) over the GameFlow backdrop, vignette (`ui_vignette`),
  warm gradient and drifting lantern motes. Continue (`GameFlow.LatestSave`, shows name/level/class/map), New Game
  (`GameRoot.SetMode(CharacterCreation)`), Load (slot list), Settings, Quit. Up/Down + Enter; Esc returns from sub-pages.
* **Character creation** — name field (sets `GameInput.SetTextFieldFocused` and `UiRoot.HotkeysSuppressed` while
  focused; Enter/Esc/Tab leave it; "?" picks a random name), 8 class cards (crest, class colour, roles, resource pill),
  hero art, details (description, resource, armour/weapons, design notes, health/mana/stats and every ability at the
  chosen level from a preview `UnitFactory.CreateCharacter`, tooltips via `UiText.Ability`), start level 1/10/…/60 with
  veteran options (gear, auto talents), Begin → `GameFlow.StartNewGame(NewGameOptions)` (errors from `LastError`).
  Laid out on a 1920×1080 canvas scaled to the screen.
* **Dialogue** — speaker portrait/name/title (NPC, companion, hero; narrator in italics), typewriter text (stable wrap:
  the unrevealed tail is transparent; click/Space completes; speed in Settings), numbered choices (1–9) with coloured
  `[TAG]`s (class tags in class colours, skills in gold), check previews ("Persuasion DC 12 · Seren +3 · 65% chance"),
  previously chosen lines greyed, choices hidden only by Gold / HasItem / Level / Companion approval / InParty shown
  locked with "Requires …" (`WorldRules.Check`). Continue / End conversation. **Skill checks** (`SkillCheck` events
  while a dialogue runs): the d20 tumbles and bounces with ticking sounds, lands on the natural roll, shows
  "d20 14 + 1 Spirit + 2 proficiency = 17 vs DC 12", then SUCCESS / FAILURE / CRITICAL with sparkles and sound;
  click/Space skips; the next line waits for the die and the overlay lingers if the conversation ends on the roll.
* **Loot** — coins already in the purse, items (quality colour, comparison tooltip, click = take), Take All
  (Space / E / Esc), Close asks before leaving items behind. **Quest reward** — pick one card, Claim (Enter) or Decide
  later (the Journal's "Choose your reward" brings it back).
* **Vendor** — Buy (price plates, unaffordable/sold out/unusable flags, Shift = a stack), Sell (sell prices, Sell junk,
  Shift = one of a stack, rare+ items confirm), Buy back. Right-click in the bags sells too. **Trainer** — member tabs for
  the classes taught, per ability the highest rank trainable now (missing lower ranks included in the cost), "New",
  upcoming ranks with their level, Train / Train all (affordability). **Respec** — per member points spent and fee,
  confirm, then the Talents window opens for that member.
* **Character sheet** — member tabs (party + camp), health/resource bars, paper doll (17 slots around the hero art;
  click = unequip), attributes (gear bonus in green), defence (armour + mitigation % vs a same-level attacker, dodge,
  parry, block, resistances), melee/ranged/spell stats, mana regen in and out of the five-second rule, XP (main
  character) or approval (companions), pet line.
* **Bags** — "Equip on" member tabs, filters (All / Gear / Consumables / Quest / Junk), sort (bag order / type / quality /
  name), quality frames, stack counts, unusable gear dimmed with a red corner, comparison tooltips with stat deltas.
  Left click: equip on the selected member or use (`UseItemOutOfCombat`, in combat `Combat.BeginItem`). Right click:
  Use / Equip on … (each member, reason when not allowed) / Sell / Sell one / Destroy… (confirm; quest items refuse).
* **Spellbook** — groups by school (physical named per class), talent abilities, the pet's abilities, general attacks,
  passives; "Rank 3 (next at 24)", cost, cast and cooldown; live cooldown sweep; click = use (field, or the member's turn
  in combat via `Combat.BeginAbility`).
* **Talents** — three trees, 7×4 grid, `x/y` badges, learnable talents glow, locked tiers dimmed with their point
  requirement, prerequisite arrows (gold when met), tooltips (`UiText.Talent` + why not), left click learns
  (`Session.LearnTalent`), "Recommended build" (`AutoAllocateTalents`, confirm), "Reset talents" only while a trainer's
  respec is open (`Session.Respec`, confirm).
* **Journal** — active (main first), completed and failed (collapsible) quests; detail: giver, level, summary, current
  stage, objectives with ticks, history, rewards (XP after `xpRate`, coins, items, pick-one choices). "Party & camp"
  opens the roster: lead, to camp / join, auto-play toggle, dismiss (confirm).
* **Map** — ground painted once per map from the nav grid; live party (leader ringed), NPC names, enemies, chests,
  lanterns (lit glow), signs and exits with labels/tooltips.
* **Pause** — Resume, Save (disabled with the reason), Load, Settings, Help, Main Menu (confirm), Quit (confirm).
  **Save/Load** — Quicksave + Slot 1–9 (save) or every save (load): name, class, level, map, day/hour, play time,
  modified date; overwrite / load / delete confirmations. **Settings** — master/music/effects volume + mute
  (`GameAudio`, saved), animation speed (`GameFlow.AnimationSpeed`, saved in PlayerPrefs `lv_anim_speed` and restored at
  boot), dialogue text speed (`lv_text_speed`), companion defaults (`Session.Settings`), combat move range overlay.
  **Help** — controls cheat-sheet and tips.
* **Game over** — "The lanterns dim…", embers, Load last save / Load a save… / Main Menu.
* **Level up** — merged card per member: level, health/mana gained, talent points (+ "Talents (N)" button), new ranks
  at the trainer or ranks learned by companions.

## Conventions (PanelKit)

* **Layers.** IMGUI gives a mouse-down to the first control drawn under the cursor, i.e. the screen *below*. Every
  screen wraps its draw in `PanelKit.BeginLayer(Order)` / `EndLayer` and registers its rects with
  `PanelKit.Occlude(rect, Order)` (modal screens: `OccludeAll`). While the mouse is over a rect of a higher screen (last
  frame's set, rolled by `PanelHost`), lower screens see the mouse "nowhere" — no clicks, hovers, wheel or tooltips.
  The HUD uses the same set through `PanelKit.CoveredAbove/Occlude` (called directly from `HudCore`).
* **Deferred commands.** Clicks never mutate the session inside `OnGUI`: `PanelKit.Do(action)` queues it and
  `PanelHost.Tick` runs it next `Update`. Lists drawn are snapshots refreshed in `Tick` or on session events.
* **Plain GUI with rects only** (no GUILayout); custom scroll views (`BeginScroll/EndScroll`: painted draggable
  scrollbar, rows outside the viewport are skipped and cannot be clicked). Clicks: `PanelKit.Click` on mouse-down (uses
  the MouseDown, never the MouseUp).
* **Esc.** `EscRouter` (in `PanelHost.Tick`, before UiRoot's hotkeys): confirm (1000) and context menu (900) first;
  then, if any UiRoot panel is open, UiRoot closes the top one; otherwise level-up card (500), loot = take all (400),
  respec (350), trainer (300), vendor (290), dialogue = open the pause menu (200; UiRoot never opens it over a modal
  screen, the conversation is the exception — the dialogue window ignores its keys while a panel is open over it),
  character creation / main-menu pages. Handled presses set `UiRoot.HotkeysSuppressed`.
* **Feedback.** Session command reasons ("Not enough money.") become `PanelKit.Notice` lines (red; confirmations gold)
  above the HUD's bottom block. `ConfirmScreen.Ask(title, text, yes, onYes, no, onNo, dangerous)` and
  `ContextMenuScreen.Open(pos, title, items)` are available to any screen.
* **Shared selection.** `PanelKit.Member` is the party member shown by the sheet, bags, spellbook and talents; it follows
  `GameFlow.Selected` when that changes and can be switched with the member tabs (without changing the leader).
* **Allocation-conscious.** Styles, money strings, counts, tooltips, row texts and labels are cached and rebuilt on
  change (stat sheet 4×/s); tooltips are built only while hovered.
