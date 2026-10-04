# Lanternvale — HUD (heads-up display)

`Assets/Lanternvale/Scripts/Game/UI/Hud/**` — the always-on IMGUI layers for exploration and combat, drawn with the `Ui`
toolkit (Ink surfaces over the painted dioramas). Every layer is an `IUiScreen` discovered by `UiRoot`; they talk only to
`GameFlow.Instance`, `GameFlow.Instance.Combat`, `GameFlow.Instance.Session` and static presentation helpers.

| File | Screens / types | Order |
|---|---|---|
| `HudCore.cs` | `Hud` (visibility, who acts, hand-offs, posted commands, errors, occlusion, portraits, colour caches), `HudLayout`, `HudText` (cached strings), `HudStyles`, `HudDraw` (9-sliced fills/rings, bars, icons, cooldown sweep, portraits, glyphs, arrows) | — |
| `NameplatesHud.cs` | `NameplatesHud` — enemy health plates + telegraphed cast bars in combat; hover labels + interaction prompts in exploration | 2 |
| `PartyFramesHud.cs` | `PartyFramesHud` — party frames (left) | 10 |
| `TurnOrderHud.cs` | `TurnOrderHud` — initiative strip (top centre) | 14 |
| `TargetFrameHud.cs` | `TargetFrameHud` — target frame (top centre, combat) | 16 |
| `QuestTrackerHud.cs` | `QuestTrackerHud` — clock/day/phase + gold, quest tracker (top right) | 18 |
| `ActionBarHud.cs` | `ActionBarHud` — action bar + consumables strip (bottom centre); runs posted commands | 20 |
| `TurnHud.cs` | `TurnEconomyHud` (Time/move/End Turn/status pill/cursor preview), `SelfResPromptHud` | 24, 80 |
| `CombatLogHud.cs` | `CombatLogHistory`, `CompactCombatLogHud` (26), `CombatLogPanel` (Id `UiPanels.CombatLog`, toggle **L**, 110) | 26, 110 |
| `ToastsHud.cs` | `ToastsHud` — toasts, red error lane, banners | 60 |

## Visibility

`Hud.WorldHud` = a game is running, `Session.Mode` is Exploration or Combat, no modal screen (`UiRoot.ModalActive`) and no
full-screen menu (Pause / Settings / SaveLoad / Map panels). Dialogue hides everything except toasts/banners;
game over hides everything (the game-over overlay belongs to the panels). `Hud.CombatHud` additionally needs
`GameFlow.Combat`. Toasts stay visible in dialogue (quest/item toasts from conversation outcomes).

## Layers

**Party frames** (left): portrait (`portrait_*`, else a crop of the unit sprite) in a class-colour frame, level badge, name,
health bar with absorb overlay (sum of `AuraInstance.AbsorbLeft`), resource bar coloured by type with numbers, rogue
combo pips, pending-cast bar, main character XP (exploration), buffs/debuffs (debuffs first, dispel-type frames, remaining
time, stacks/charges, `UiText.Aura` tooltip), pet / controlled-unit sub-frames, totems, dead hunter pet note, downed/dead
states, leader crown, **AUTO** toggle for companions (`Combat.SetAutoPlay` / `Session.SetAutoPlay`). Click → `GameFlow.Select`.
The active unit glows gold in combat.

**Action bar** (bottom centre) for `GameFlow.Selected` — in combat the active player unit (otherwise the selected member is
shown dimmed, "inspect"). Statuses come from `Session.GetAbilityBar(unit)` (battle in combat, field outside), refreshed on
unit/turn/targeting/opener/bag changes, on new battle events and every 0.3 s (0.6 s in combat). Icons: glyph + school
colour, clockwise cooldown sweep (live `Unit.Cooldowns`), dim + red reason in the tooltip when unusable (blue tint = not
enough resource, red = range), cost (bottom right, resource colour), cast time (bottom left), hotkey (top left), gold
"Active" glow for stances/aspects/auto attack/queued swings, the ability being targeted and an armed opener
(`GameFlow.PendingOpener`). Hotkeys **1–0 - =** act on the visible page; more than 12 abilities → pages (arrows left of
the bar, or the mouse wheel over it). **Drag** an icon onto another slot to swap them; the order is saved per character
(`PlayerPrefs "lv.hud.bar.<memberId>.<Class>"`, pets `"<owner>.pet.<creature>"`), **Shift+right-click** resets it.
**Right-click** = self-cast. Tooltips: `UiText.Ability` + reason + Time cost.
- Combat: click/hotkey → `Combat.BeginAbility(id)` (errors arrive through `Combat.LastError`).
- Field: `GameFlow.UseAbilityOutOfCombat`. Ally / other-ally / dead-ally abilities with more than one candidate enter a
  "choose a party member" mode (`Hud.FieldPick`): frames pulse green, click a portrait to cast, press again (or
  right-click the slot) for yourself, right-click / Esc / a world click cancels.

**Consumables strip** (left of the bar): potions, food, drink, bandages and other usable consumables from the party bags
(one icon per item id, total count, quality frame, shared cooldown sweep, reason from `Session.CannotUseItemReason`).
Food/drink are hidden in combat. Combat → `Combat.BeginItem`; field → `GameFlow.UseItemOutOfCombat` (ally items pick a target).

**Turn economy** (combat, above the bar): the 6.0 s Time bar as four 1.5 s GCD pips; hovering a bar slot highlights the
Time it would spend and warns when it overflows ("+0.8 s debt" for instants, "becomes pending" for casts); time debt
carried into the next turn; movement left / budget in metres (Rooted); **End Turn** (Space is handled by the combat
controller), **»** fast animations (`Combat.FastForward`, remembered across fights: `PlayerPrefs "lv.hud.fastForward"`), **Leave Fight** when `Battle.CanDisengage`. Status pill:
targeting hint, "Grey Wolf is acting…" for AI turns (with **Take control** for auto-played companions), "X is casting Y —
it resolves at the start of their next turn", else "Aria's turn". `Combat.HoverPreview` follows the cursor.

**Turn order** (top centre): `Battle.TurnOrder` from the acting unit (enlarged, gold), team-coloured frames, health
strips, pending-cast hourglass, elite/boss marks, a divider where the next round starts, "Round N"; hover → name, level,
rank, health, cast, surprised. Clicking a party portrait selects it; while an ability/item is being targeted, clicking
any portrait confirms it on that unit (`Hud.ClickUnit` → `Combat.TargetUnit`).

**Target frame** (below the turn order): hovered unit (`Combat.HoveredTarget`, `GameFlow.HoveredUnit`) → acting enemy →
the active unit's attack/combo target (sticky). Level (WoW difficulty colours, skull for bosses/+10), rank with a gold
winged "dragon" frame (silver for rares), creature type, health with absorbs and %, resource, the telegraphed **cast
bar** (pending casts resolve on the caster's next turn) with "Interrupt now: Kick [3]" when the active player unit has a
usable `Interrupt`-tagged ability, debuffs/buffs (yours glow), combo points, "Attacking Kael" / "Taunted by …" and
"Your threat 64%" (`Battle.ThreatOf` vs the current target's threat).

**Nameplates** (under everything): enemy health plates with level (`5+` elite, `??` boss), name when hovered/targeted/
elite/acting, absorbs and telegraphed cast bars; hovered allies get a plate too. Exploration: `GameFlow.HoveredLabel`
at `HoveredLabelWorld`, coloured by `HoveredKind`, with a prompt (Talk, Open, Pick the lock, Travel, Inspect, Attack,
"Open with Cheap Shot (Pip)").

**Toasts** (centre top; below the target frame in combat): every `SessionEvent` worth telling — quests (new/progress/
complete/failed/reward choice; the tracker flashes), items (quality colour, merged counts), money (merged), XP (merged),
abilities learned (merged per character), talent points, companions, approval, skill checks
("Aria · Sleight of Hand check: 14 + 2 = 16 vs DC 12 — Success"; world checks only — conversation checks are rolled
by the dialogue window, a toast would spoil the die), locked transitions, rest, time of day, flow toasts, and
telegraphs from `GameFlow.CombatEventPresented` (`CastStart` that left a pending cast: "Hollow Warden begins casting …!
Interrupt it before its next turn."). At most five on screen, the rest queue. **Error lane** (red, above the bottom
block): `Combat.LastError` and HUD failures (`Hud.Error`). **Banners** (one at a time): map title card (Title font,
`MapDef.subtitle`), region names (split at " — "), level up (merged for the whole party, + health/mana/talent points/
trainer note), Combat!, Victory!/Defeat/Disengaged (when `Combat.Battle.IsOver`, or `CombatEnded`), story moments
(`SpecialOutcome` with Amount 1).

**Quest tracker** (top right, exploration): active quests from `Session.Journal(false)` (main quests starred), objectives
with progress (`ObjectiveView.Display`), stage text when there are no objectives, "+N more"; click a quest → Journal;
the header collapses the list (saved). Above it: day, time (5-minute steps), phase (sun/moon) and the party's gold; on
rest-area maps (`MapDef.restArea`: Whisperwood, the Shrine) a moon button left of the clock makes camp
(`GameFlow.TryRest`, disabled with `Session.CannotRestReason()` in its tooltip).

**Combat log**: `CombatLogHistory` mirrors `Combat.LogLines` (rich text, kept across fights, "— Battle N —" separators,
800 lines). `CombatLogPanel` (L): draggable, virtualised scroll view with a slim gold scrollbar, sticks to the newest line,
Clear. `CompactCombatLogHud`: the last four lines of the current fight, bottom left, click → panel.

**Self-resurrection**: Soulstone/Reincarnation offer (`Combat.PendingSelfResurrection`) → Rise / Stay down
(`Combat.AnswerSelfResurrection`).

## Conventions & performance

- Plain `GUI` with rects (no `GUILayout`); state snapshotted in `Tick` (action bar) or at the top of `Draw`.
- Clicks never change game state inside `OnGUI`: `Hud.Post(action)` queues it; `ActionBarHud.Tick` runs the queue next
  frame. Clicks are detected on mouse-down (`HudDraw.Click`) or tracked press/release (action bar drag & drop) and never
  `Use()` mouse events (the IMGUI input driver must see every MouseUp).
- Occlusion: IMGUI gives clicks to the first (lowest) screen drawn; each HUD layer wraps its draw in
  `HudDraw.BeginLayer(Order)/EndLayer`, which hides the mouse while a higher screen covers it. Higher rects come from
  `Hud.Occlude` (the log panel) and the panels' `Lanternvale.Game.Panels.PanelKit.CoveredAbove/Occlude` (called directly;
  HUD rects are registered in the panels' set too).
- No per-frame allocations in the hot paths: 9-slice `GUIStyle`s and textures built once (cooldown sweep: 40 frames),
  cached numbers/durations/"a / b" labels, colour caches (`Hud.C`, `Hud.SchoolCol`…), tooltips built only while hovered.
  Visual-only layers (nameplates, toasts) skip non-Repaint events.
- Every `Draw`/`Tick` catches its exceptions (logged once per kind) — nothing throws out of `OnGUI`.
