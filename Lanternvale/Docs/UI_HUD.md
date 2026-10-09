# Lanternvale — HUD (heads-up display)

`Assets/Lanternvale/Scripts/Game/UI/Hud/**` — the always-on IMGUI layers for exploration and combat, drawn with the `Ui`
toolkit (Ink surfaces over the painted dioramas). Every layer is an `IUiScreen` discovered by `UiRoot`; they talk only to
`GameFlow.Instance`, `GameFlow.Instance.Combat`, `GameFlow.Instance.Session` and static presentation helpers.

| File | Screens / types | Order |
|---|---|---|
| `HudCore.cs` | `Hud` (visibility, who acts, hand-offs, posted commands, errors, combat targeting from frames, occlusion, portraits, colour caches), `HudLayout`, `HudText` (cached strings), `HudStyles`, `HudDraw` (9-sliced fills/rings, bars, icons, cooldown sweep, portraits, glyphs, arrows) | — |
| `RankPins.cs` | `RankPins` — per-character pinned ability ranks (downranking; PlayerPrefs) | — |
| `HudPresented.cs` | `HudPresented` — the battle as the player has *seen* it: health, dead/downed state and the acting unit with the not-yet-presented events (`GameFlow.CombatEventPresented`) undone, so bars, plates, frames and the turn strip never run ahead of the animation; live state while the controller waits for input | — |
| `NameplatesHud.cs` | `NameplatesHud` — enemy health plates + telegraphed cast bars in combat; hover labels + interaction prompts in exploration | 2 |
| `PartyFramesHud.cs` | `PartyFramesHud` — party frames (left; hidden in a raid) | 10 |
| `RaidFramesHud.cs` | `RaidFramesHud` — compact raid frames, two groups of five (left; only in a raid) | 11 |
| `TurnOrderHud.cs` | `TurnOrderHud` — initiative strip (top centre) | 14 |
| `TargetFrameHud.cs` | `TargetFrameHud` — target frame (top centre, combat) | 16 |
| `QuestTrackerHud.cs` | `QuestTrackerHud` — clock/day/phase + gold, quest tracker (top right) | 18 |
| `ActionBarHud.cs` | `ActionBarHud` — action bar + consumables strip (bottom centre); runs posted commands | 20 |
| `MenuBarHud.cs` | `MenuBarHud` — the window button bar (top right): one button per window (Character, Bags, Spellbook, Talents, Journal, Party, Map, Help) with hotkey corners and tooltips | 22 |
| `TurnHud.cs` | `TurnEconomyHud` (Time/move/End Turn/status pill/cursor preview), `SelfResPromptHud` | 24, 80 |
| `CombatLogHud.cs` | `CombatLogHistory`, `CompactCombatLogHud` (26), `CombatLogPanel` (Id `UiPanels.CombatLog`, toggle **L**, 105 — not 110, which is `VendorScreen`'s: occlusion tests `Order > order` strictly, so two windows must never share an order) | 26, 105 |
| `ToastsHud.cs` | `ToastsHud` — toast intake, red error lane, banners (60); `ToastLaneHud` — draws the single toast lane above windows and menus (480) | 60, 480 |

## Visibility

`Hud.WorldHud` = a game is running, `Session.Mode` is Exploration or Combat, no modal screen (`UiRoot.ModalActive`) and no
full-screen menu (Pause / Settings / SaveLoad / Map panels). Dialogue hides everything except toasts/banners;
game over hides everything (the game-over overlay belongs to the panels). `Hud.CombatHud` additionally needs
`GameFlow.Combat`. Banners stay visible in dialogue; the toast lane (`ToastLaneHud`) is visible whenever it has toasts —
in dialogue, over windows, in menus, on the title and the game-over screen (notices and load errors land there).

Everything scales with `Ui.Scale` = max(0.5, screen height / 1080) × **`Ui.UserScale`** (Settings ▸ Interface size,
0.75–1.5, PlayerPrefs `lv_ui_scale`; applied in `Ui.BeginFrame`, also by the flow's bark bubbles via `Ui.ComputeScale()`).

## Layers

**Party frames** (left): portrait (`portrait_*`, else a crop of the unit sprite) in a class-colour frame, level badge, name,
health bar with absorb overlay (sum of `AuraInstance.AbsorbLeft`), resource bar coloured by type with numbers, rogue
combo pips, pending-cast bar, main character XP (exploration), buffs/debuffs (debuffs first, dispel-type frames, remaining
time, stacks/charges, `UiText.Aura` tooltip), pet / controlled-unit sub-frames, totems, dead hunter pet note, downed/dead
states, leader crown, **AUTO** toggle for companions (`Combat.SetAutoPlay` / `Session.SetAutoPlay`). Click → `GameFlow.Select`.
The active unit glows gold in combat. Cast bars use `PendingCast.TotalTime`/`Progress` (`Hud.PendingProgress`).
**Targeting from the frames (healers):** while an ability/item is targeted in combat, a click on a character frame or a
pet sub-frame confirms it on that unit (`Hud.ClickUnit` → `CombatController.TargetUnit`, posted; refusals arrive in the
red error lane). `Hud.TargetStateOf(unit)` (cached per battle event / ability / rank) drives the look: valid targets
pulse green, out-of-range/sight ones pulse softer (the click walks into range first), invalid ones dim; the tooltip says
"Click to use Flash Heal on Kael." Outside targeting the click selects (exploration "choose a party member" picks
work as before).

**Raid frames** (left, only while `Session.InRaid`; `PartyFramesHud.Visible` steps aside then): two groups of five,
column-major (members 1–5 left, 6–10 right, 145 × 64 px each, right edge at 310 px so the turn strip keeps its room):
a class-colour bar, the name in class colour (leader crown), health with absorb (as presented) and "Dead"/"Downed" on it,
a thin resource bar (the pending cast replaces it), up to four debuffs (dispel-type frames; stacks; tooltip with time
left and caster), the **AUTO** pill (companions, and the main character while the AI plays it — Auto-battle), the gold
pulse ring of the acting unit, the white ring of the selection, a red ring with an attacker count for aggro, and pets /
controlled summons as thin 15 px rows under their owner (at most two; when a column would reach the bottom block, the
pet rows give way). **The click contract is the party frames'**: `Hud.FieldPick` / `Hud.IsValidPickTarget` /
`Hud.ResolveFieldPick` for the out-of-combat pick, `Hud.TargetStateOf` for the combat targeting look (valid pulse,
reachable soft pulse, invalid dim; the tooltip says what the click does), `Hud.ClickUnit` otherwise — healers target
the raid from the frames. Frame tooltips: level, class, role, health, resource, cast; built only while hovered.
Layout (Ui.Width × Ui.Height): 1920 × 1080 at interface size 1.0 and 1280 × 720 at 1.5 (the tightest): five frames
are 340 px tall against 482 px of room above the compact log at 1.5.

**Action bar** (bottom centre) for `GameFlow.Selected` — in combat the active player unit (otherwise the selected member is
shown dimmed, "inspect"). Statuses come from `Session.GetAbilityBar(unit, includeTooltips: false)` (battle in combat,
field outside; pets/controlled units: `ctx.GetAbilityBar(unit, true, false)`), refreshed on unit/turn/targeting/opener/
bag/rank-pin/queued-swing changes, on new battle events and every 0.3 s (0.6 s in combat) — **without description text**;
a slot's tooltip (`UiText.Ability(unit, ability, rank)`) is built only while it is hovered. Icons: glyph + school
colour, clockwise cooldown sweep (live `Unit.Cooldowns`), dim + red reason in the tooltip when unusable (blue tint = not
enough resource), **red tint when an enemy ability is out of range of the unit's attack target**
(`AbilityStatus.InRangeOfAttackTarget == false`: range, dead zone or line of sight — WoW's red icon; "Out of range of …"
in the tooltip), cost (bottom right, resource colour), cast time (top right), hotkey (top left), gold
"Active" glow for stances/aspects/auto attack/queued swings, the ability being targeted and an armed opener
(`GameFlow.PendingOpener`). **Downranking:** a rank pinned in the Spellbook (`RankPins`) is fetched with
`Battle.GetStatus(unit, ability, false, rank, false)` (cost, cast time, usability of that rank), shows a gold "R3" plate
(bottom left; top centre on Soul Shard spells), its tooltip describes that rank ("Pinned to Rank 3 of 7…"), and the slot,
its hotkey and right-click cast it (`Combat.BeginAbility(id, rank)` / `GameFlow.UseAbilityOutOfCombat(.., rank)`).
Pins are per character: `PlayerPrefs "lv.bar.ranks.<Name>.<Class>"` (pets `"<Owner>.<Class>.pet.<creature>"`), because the
session save has no slot for UI data; pinning the highest known rank clears the pin (the bar follows new ranks). Hotkeys **1–0 - =** act on the visible page; more than 12 abilities → pages (arrows left of
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

**Turn economy** (combat, above the bar): the 6.0 s Time bar as four 1.5 s GCD pips; hovering a bar slot (or targeting —
at `Combat.TargetingRank`, so a faster low rank shows its own cast time) highlights the
Time it would spend and warns when it overflows ("+0.8 s debt" for instants, "becomes pending" for casts); time debt
carried into the next turn; movement left / budget in metres (Rooted); **End Turn** (Space is handled by the combat
controller), **»** fast animations (`Combat.FastForward`, remembered across fights: `PlayerPrefs "lv.hud.fastForward"`;
raids keep their own choice, `"lv.hud.fastForwardRaid"`, **on by default**), **Leave Fight** when `Battle.CanDisengage`.
In raids and big battles (`RaidPlanning.IsBigBattle`: more than 14 units) two toggles sit above End Turn (above Leave
Fight when it shows): **Auto: all companions** (every companion's `SetAutoPlay`, pets follow) and **Auto-battle** (the
whole party, the main character too). Turning one on captures the flags it changes (`AutoPlaySnapshot`); turning it off
puts each unit's own flag back (members captured in no snapshot get auto-play off). The snapshots are dropped when the
raid begins or ends (the session restores the party's flags itself) and with a new game. The toggles read on when every
unit they cover auto-plays, so a single AUTO pill switched off shows them off. Status pill:
targeting hint, "Grey Wolf is acting…" for AI turns (with **Take control** for auto-played companions), "X is casting Y —
it resolves at the start of their next turn", else "Aria's turn". `Combat.HoverPreview` follows the cursor.

**Turn order** (top centre): `Battle.TurnOrder` from the acting unit (enlarged, gold), team-coloured frames, health
strips, pending-cast hourglass, elite/boss marks, a divider where the next round starts, "Round N"; hover → name, level,
rank, health, cast, surprised. Clicking a party portrait selects it; while an ability/item is being targeted, clicking
any portrait confirms it on that unit (`Hud.ClickUnit` → `Combat.TargetUnit`). **Big battles** (more than 14 units,
`RaidPlanning.IsBigBattle`; raids) use the compact strip: 56/40 px portraits instead of 70/50 and room for up to 24; when
the living units of the round still do not fit, the last place goes to a gold "+k" chip ("3 more units act after
these"). `TurnOrderHud.Bottom` (and `Big`, `Small`) are properties now: the target frame and the combat toast lane
(`ToastsHud.LaneTop`) follow the strip's height.

**Target frame** (below the turn order): hovered unit (`Combat.HoveredTarget`, `GameFlow.HoveredUnit`) → acting enemy →
the active unit's attack/combo target (sticky). Level (WoW difficulty colours, skull for bosses/+10), rank with a gold
winged "dragon" frame (silver for rares), creature type, health with absorbs and %, resource, the telegraphed **cast
bar** (pending casts resolve on the caster's next turn) with "Interrupt now: Kick [3]" when the active player unit has a
usable `Interrupt`-tagged ability, debuffs/buffs (yours glow), combo points, "Attacking Kael" / "Taunted by …" and
"Your threat 64%" (`Battle.ThreatOf` vs the current target's threat).

**Nameplates** (under everything): enemy health plates with level (`5+` elite, `??` boss), name when hovered/targeted/
elite/acting, absorbs and telegraphed cast bars (`PendingCast.Progress`); hovered allies get a plate too. Exploration:
`GameFlow.HoveredLabel` at `HoveredLabelWorld`, coloured by `HoveredKind`, with a prompt (Talk, Open, Pick the lock,
Travel, Inspect, Attack, "Open with Cheap Shot (Pip)"). **Encounter enemies** get a plate from `Session.PreviewEncounter`
(`GameFlow.HoveredEnemy` / `HoveredEncounter`): name, a level badge in WoW difficulty colours for the level the battle
will scale it to (a range for rolled levels, `12+` elite, `??` + red for bosses or 10+ levels above the party), a gold
winged mark for elites (silver for rares) or a skull for bosses, and "pack of 3" / "group of 4 · Boss" / "passive".
A hovered **NPC with a quest marker** gets a slim plate above its name: the "!" / "?" in its colour and the quest line
("Quest: Wolves at the Fold", "Turn in: …", "In progress: …", "Quest (level 18): …", "+N" for more quests; gold rim for
the main story) — `GameFlow.HoveredNpcId` → `Session.QuestMarkerOf`, cached per `QuestMarkersVersion`.

**Toasts — the single toast lane** (centre top; below the target frame in combat; at the top in dialogue and menus),
drawn by `ToastLaneHud` at Order 480 so it is seen above windows and menus: every `SessionEvent` worth telling — quests
(new/progress/complete/failed/reward choice; the tracker flashes), items (quality colour, merged counts), money (merged),
XP (merged), abilities learned (merged per character; not right after that unit's level-up — the level-up card lists
them), talent points (likewise), companions, approval, skill checks
("Aria · Sleight of Hand check: 14 + 2 = 16 vs DC 12 — Success"; world checks only — conversation checks are rolled
by the dialogue window, a toast would spoil the die), locked transitions, rest, time of day, flow toasts, and
telegraphs from `GameFlow.CombatEventPresented` (`CastStart` that left a pending cast: "Hollow Warden begins casting …!
Interrupt it before its next turn."), and the **panels' notices**: `PanelKit.Notice` → `GameFlow.Toast(text, colour)`
(red refusals "Not enough money.", gold confirmations "Saved to Slot 2."; `GameFlow.ToastColorOf(e)` gives the colour
during the relay) — there is no separate notice screen any more. At most five on screen and never lower than the room
above the error lane (three lines kept free; in dialogue the upper half), the rest queue. **Error lane** (red, above the
bottom block): `Combat.LastError` and HUD failures (`Hud.Error`). **Banners** (one at a time, pushed below the toast
stack so they never overlap it): map title card (Title font, `MapDef.subtitle`), region names (split at " — "), a
**short level-up banner** ("Level 12" + the names, merged for the whole party, 3 s — the details are the panels'
level-up card, bottom right; the world only gets sparkles and the chime), Combat!, Victory!/Defeat/Disengaged (when
`Combat.Battle.IsOver`, or `CombatEnded` — the only place these words appear: the combat presenter adds sparkles/sound,
no world text), story moments (`SpecialOutcome` with Amount 1), hidden passages (`SecretFound`: a gold story banner with
`e.Text`, "You discovered a hidden passage: The Root Hollows" — the flow no longer toasts it, so it is said once).
**Raids:** `RaidStarted` and `RaidEnded` (Amount 0) are gold flow toasts (`GameFlow.Events`). A **raid wipe** reads as ONE
notice: when the presented `BattleEnd` (or `CombatEnded`) is a defeat on a map where `RaidPlanning.WipeSendsHome` holds,
the banner is "The raid has wiped" instead of "Defeat"; the session's follow-ups (`MapEntered` of the return map — its
title card is skipped, `RaidEnded` Amount 1, `PartyHealed` "The raid has wiped. You come to in Mirefen, …") only fill in
its second line (`RaidPlanning.WipeDetail`), and nothing else is toasted.

**Quest tracker** (top right, exploration): active quests from `Session.Journal(false)` (main quests starred), objectives
with progress (`ObjectiveView.Display`), stage text when there are no objectives, "» Return to <NPC>" in gold when someone
can take the current step now (`QuestTrackerHud.ReturnLine` ← `Session.QuestTurnInOf`; left out when an objective already
names them; rebuilt when `QuestMarkersVersion` moves), "+N more"; click a quest → Journal;
the header collapses the list (saved). Above it: day, time (5-minute steps), phase (sun/moon) and the party's gold; on
rest-area maps (`MapDef.restArea`: Whisperwood, the Shrine and the four new zones; never in dungeons or raids) a moon button left of the clock makes camp
(`GameFlow.TryRest`, disabled with `Session.CannotRestReason()` in its tooltip).

**Menu bar** (top right, exploration and combat, between the clock/gold pill and the quest tracker): a button per
window so everything reachable by hotkey is also reachable with the mouse — Character **C**, Bags **I** (or B), Spellbook
**P**, Talents **N**, Journal **J**, Party **K**, Map **M**, Help **F1**. The hotkey sits in the button's corner and in
its tooltip; open windows are framed gold; a click toggles the panel through `UiRoot.Toggle` (posted, run next frame).
The Talents button glows while any party character has unspent talent points (checked every 0.5 s; the tooltip adds
"Unspent talent points!"). The first time the exploration HUD appears (after the opening conversation) a one-time toast
points at the menus and F1 and the Help button glows for 25 s (PlayerPrefs `lv.hud.menuHint`).

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
