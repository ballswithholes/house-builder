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
| `InventoryPanel` **I / B** | 126 | `UiPanels.Inventory` | – |
| `JournalPanel` **J** | 130 | `UiPanels.Journal` | – |
| `MapPanel` **M** | 132 | `UiPanels.Map` | – |
| `PartyPanel` **K** | 134 | `UiPanels.Party` — K (bound in `PartyPanel.Tick`, not in UiRoot's hotkey table), the HUD menu bar's Party button or the Journal's "Party & camp" button | – |
| `TalentsPanel` **N** | 140 | `UiPanels.Talents` | – |
| `LootScreen` | 150 | `Session.PendingLoot` (exploration) | – |
| `LevelUpPopup` | 160 | `LevelUp` events (bottom right, waits for dialogue to end; the detailed half of a level-up) | – |
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
| (HUD) `ToastLaneHud` | 480 | the single toast lane — also every `PanelKit.Notice` (see UI_HUD.md) | – |

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
  in combat via `Combat.BeginAbility(id, rank)`). **Downranking:** every ability with more than one known rank has a rank
  button ("Ranks…", gold "R5 on bar" while a rank is pinned; Shift+click or right-click on the entry does the same) that
  opens a list of **every known rank** — highest first, current choice marked, cost per rank, each with its own full
  tooltip (`UiText.Ability(unit, ability, rank)` → `Tooltip.AbilityFull(.., rank)`). Picking a rank pins it to the action
  bar (`RankPins.Pin`; the bar's slot, its hotkey and a click here cast that rank); picking the highest clears the pin.
  The entry tooltip describes the pinned rank.
* **Talents** — three trees, 7×4 grid, `x/y` badges, learnable talents glow, locked tiers dimmed with their point
  requirement, prerequisite arrows (gold when met), tooltips (`UiText.Talent` + why not), left click learns
  (`Session.LearnTalent`), "Recommended build" (`AutoAllocateTalents`, confirm), "Reset talents" only while a trainer's
  respec is open (`Session.Respec`, confirm).
* **Journal** — active (main first), completed and failed (collapsible) quests; detail: giver, level, the quest's zone
  and its level band (`QuestDef.zone`), summary, current stage, objectives with ticks, "» Return to <NPC>" when the
  step can be handed in now (`Session.QuestTurnInOf`, shared with the tracker), history, rewards (XP as granted:
  `Progression.QuestXp(db, xp, PartyLevel)`, coins, items, pick-one choices). "Party & camp" opens the roster: lead, to
  camp / join, auto-play toggle, dismiss (confirm).
* **Map** — ground painted once per map from the nav grid; live party (leader ringed), NPC dots with **quest glyphs**
  above them ("!" / "?", yellow or grey, main quests in a gold ring; `Session.QuestMarkerOf`), enemies, chests, lanterns
  (lit glow), signs, exits (hidden transitions only once `MapRuntime.IsTransitionVisible`) and **objective hints**
  (pulsing gold rings on encounters holding a Kill target, Reach regions and exits, chests with a Collect item;
  `Session.QuestHintsOnMap`). **Labels** never overlap (Core `MapLabels.Plan` / `LabelLayout`, re-planned only when the
  map, flags, markers, view or rect change): short names (`NpcDef.shortName`, else the honorific stripped), quest NPCs
  first, then exits, then service crowds — named one by one when they fit, else one label with the region's name
  (`RegionDef.name`, else "Training Yard" from `reg_training_yard`) — then everyone else; labels that fit nowhere and
  crowd members show their name on hover; tooltips give full names, titles and quest lines. Mouse wheel zooms 1–3×
  around the cursor, drag pans when zoomed, "Whole map" resets. The title shows the subtitle, the zone band
  ("Levels 12–18") and a "Hidden dungeon" / "Raid (10)" badge. Legend: party, folk, enemies, lantern, chest, exit,
  "!" quest, "?" turn in, objective (wraps to two rows).
* **Pause** — Resume, Save (disabled with the reason), Load, Settings, Help, Main Menu (confirm), Quit (confirm).
  Loading a save or returning to the main menu from here resumes in real time (`Time.timeScale = 1`, no pause, no
  fast-forward rate left behind — GameFlow.md §1).
  **Save/Load** — Quicksave + Slot 1–9 (save) or every save (load): name, class, level, map, day/hour, play time,
  modified date; overwrite / load / delete confirmations. **Settings** — master/music/effects volume + mute
  (`GameAudio`, saved), **Interface size** slider (75–150 % in 5 % steps → `Ui.UserScale`, applied when the slider is
  released, PlayerPrefs `lv_ui_scale`), animation speed (`GameFlow.AnimationSpeed`, saved in PlayerPrefs `lv_anim_speed`
  and restored at boot), dialogue text speed (`lv_text_speed`), companion defaults (`Session.Settings`), combat move range
  overlay. The settings scroll when the window is shorter than them (large interface sizes).
  **Help** — controls cheat-sheet and tips.
* **Game over** — "The lanterns dim…", embers, Load last save / Load a save… / Main Menu.
* **Level up** — merged card per member: level, health/mana gained, talent points (+ "Talents (N)" button), new ranks
  at the trainer or ranks learned by companions. Bottom right, apart from the HUD's short "Level N" banner (top centre)
  and the toast lane: it never rises above 38 % of the screen height (with many entries it shows fewer), and it steps left
  of an open loot window.

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
* **World-click blockers inside scroll views.** `GameInput.BlockRectGui` converts with `GUIUtility.GUIToScreenPoint`
  (minus the top-level origin the input driver records), so a rect drawn inside a scroll view / group / clip blocks the
  screen area it really covers; `BeginScroll` registers its viewport as a block clip (`GameInput.BeginBlockClip` /
  `EndBlockClip`), so rows scrolled out of sight block nothing. `PanelKit.Btn` is therefore just `Ui.Btn` (the old
  "no blocker inside scroll views" workaround is gone).
* **Esc.** `EscRouter` (in `PanelHost.Tick`, before UiRoot's hotkeys): confirm (1000) and context menu (900) first;
  then, if any UiRoot panel is open, UiRoot closes the top one; otherwise level-up card (500), loot = take all (400),
  respec (350), trainer (300), vendor (290), dialogue = open the pause menu (200; UiRoot never opens it over a modal
  screen, the conversation is the exception — the dialogue window ignores its keys while a panel is open over it),
  character creation / main-menu pages. Handled presses set `UiRoot.HotkeysSuppressed`.
* **Feedback.** Session command reasons ("Not enough money.") become `PanelKit.Notice` lines (red; confirmations gold),
  routed through `GameFlow.Toast(text, colour)` into the HUD's **single toast lane** (drawn above windows and menus,
  never overlapping the error lane or the banners). `ConfirmScreen.Ask(title, text, yes, onYes, no, onNo, dangerous)` and
  `ContextMenuScreen.Open(pos, title, items)` are available to any screen.
* **Shared selection.** `PanelKit.Member` is the party member shown by the sheet, bags, spellbook and talents; it follows
  `GameFlow.Selected` when that changes and can be switched with the member tabs (without changing the leader).
* **Allocation-conscious.** Styles, money strings, counts, tooltips, row texts and labels are cached and rebuilt on
  change (stat sheet 4×/s); tooltips are built only while hovered.
