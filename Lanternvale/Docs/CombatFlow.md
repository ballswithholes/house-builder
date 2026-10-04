# Lanternvale — Combat Flow (`CombatController`)

How a battle is driven and presented in the Unity layer, written for the UI engineers (hotbar, turn order, target
frame, combat log, toasts, prompts). Code: `Assets/Lanternvale/Scripts/Game/Flow/CombatController*.cs`.

| File | Contents |
|---|---|
| `CombatController.cs` | contract surface, lifecycle, per-frame loop, presentation speed, commands, ending/disengage |
| `CombatController.Queue.cs` | event intake (`Battle.TakeEvents(ref cursor)`), grouping into beats, the sequential runner |
| `CombatController.Present.cs` | beat presenters, `CombatEvent` → visuals, state visuals, pending casts/channels, combat log |
| `CombatController.Input.cs` | the player's turn: move range, path/AoE previews, hover text, targeting, smart clicks |
| `CombatController.AI.cs` | AI pacing (one step at a time, path previews, loop guards) |

UI screens reach it only through `GameFlow.Instance.Combat` (null outside combat).

---

## 1. Lifecycle

* `GameFlow` creates the controller on `CombatStarted` (`new CombatController(flow, battle)`), calls `Update(dt)` every
  frame with **unscaled** delta time, and disposes it on `CombatEnded`.
* Construction ensures a `UnitView` for every battle unit (`GameFlow.EnsureView`), walks/snaps views to their battle
  positions and applies state visuals (stealth, polymorph, control tints). It works whether it is constructed before
  `Battle.Begin` (synchronous `EventRaised`) or after (polled `TakeEvents`): all events from index 0 are presented.
* The rules engine is always ahead of the screen: actions resolve instantly in `Battle`, the controller then
  *presents* the produced events in order. `ActiveUnit` is the rules' active unit — use `IsPlayerTurn` / `IsBusy`
  to know what the player may do **now**.
* End: when `Battle.IsOver` and everything is presented, a short victory/defeat beat plays (sparkles + sound, 1.8 s; the "Victory!" / "Defeat" words are the HUD's banner,
  raised by the `BattleEnd` event),
  then the controller calls `Session.FinishBattle()` and sets `Finished = true` (the session raises `CombatEnded`
  synchronously; `GameFlow` disposes the controller). Defeat → the session raises `GameOver` after that.
* `Dispose()` hides every preview it owns, clears highlights/rings/cast glows/beams, syncs state visuals with the final
  rules state, removes views of dead enemies whose fade-out was running, restores `Time.timeScale = 1`, releases the
  camera focus and (unless defeated) returns to the map's music mood.

## 2. State the UI reads

| Member | Meaning |
|---|---|
| `IsPlayerTurn` | the active unit waits for player input **and** nothing is animating / no event is pending. Enable the hotbar, End Turn button, item use only when true. |
| `IsBusy` | events are animating, an AI unit is acting, the ending beat runs, or a disengage is in progress. |
| `ActiveUnit` | `Battle.ActiveUnit` (may be AI-controlled). Turn order: `Battle.TurnOrder`, `Battle.Round`. |
| `IsTargeting`, `TargetingAbility`, `TargetingItem` | targeting mode (for an item both are set: the ability is the item's `use`). Highlight the hotbar slot. |
| `HoverPreview` | one line describing what a left click does now; "" when nothing. Draw it near the cursor during `IsPlayerTurn`. |
| `HoveredTarget` | battle unit under the cursor during the player's turn (target-frame preview), or null. |
| `PendingSelfResurrection` | `SelfResOffer` (Soulstone, Reincarnation) for the active player unit → show "Rise now?" with Accept/Decline → `AnswerSelfResurrection(bool)`. |
| `LastError`, `LastErrorTime`, `ErrorRaised` | the last command failure ("Not enough rage (15).", "Out of range (needs 2.4 m more movement).") for a toast. `LastErrorTime` is `Time.unscaledTime`. |
| `LogLines` | presented combat-log lines, newest last, IMGUI rich text (`<color=#…>`), capped at ~300. Resource costs and small resource gains are omitted. |
| `ShowMoveRange` | settings toggle for the movement overlay (default on). |
| `FastForward`, `PresentationSpeed` | fast-forward toggle (×2.5, same as holding Shift); effective speed = `GameFlow.AnimationSpeed` × fast-forward. |
| `GameFlow.CombatEventPresented` | raised once per `CombatEvent`, **in event order**, at the moment it is shown (HUD flashes, floating portraits…). |

Cast bars: read `Unit.Pending` (`Ability`, `RemainingTime`, `Channel`) — the controller already shows a glow and a
"Casting X..." label over the caster; the bar is the HUD's job. Time/movement: `Unit.TimeLeft`, `Unit.MoveLeft`.

## 3. Commands

All return `null` on success or a reason (also stored in `LastError` and raised on `ErrorRaised`).

* `BeginAbility(id)` / `BeginItem(item)` — only during `IsPlayerTurn`, otherwise "Wait for the action to finish." /
  "It is not your turn.". `Battle.CanUseIgnoringTarget` (+ `Session.CannotUseItemReason` for items) is checked first.
  * **Execute immediately:** `Self` abilities (stances, buffs, Frost Nova, Arcane Explosion), `Pet` abilities,
    caster-centred circles; **auto attacks** when the unit has a living hostile `AttackTarget` (toggles it: pressing
    Attack while attacking stops); **next-swing** abilities (Heroic Strike, Raptor Strike) on the current target.
  * **Targeting mode** otherwise: unit abilities (Enemy/Ally/AllyOther/Any/DeadAlly), ground abilities (`Point`),
    and **aimed** cones/lines from the caster (Cone of Cold) which follow the mouse direction.
* `CancelTargeting()` — also right click (not over UI) and Esc.
* `TargetUnit(unit)` — confirms the ability/item being targeted on a unit picked in the UI (party frames, turn order),
  exactly like a left click on it in the world (walks into range first when needed). `null` when nothing is targeted.
* `EndTurn()` — Space / Enter / Keypad Enter; only during `IsPlayerTurn`. During a self-resurrection offer it waits
  (the offer comes back next round); use `AnswerSelfResurrection(false)` to decline for good.
* `Disengage()` — practice fights only (`Session.CannotLeaveCombatReason()` is returned otherwise). Plays a short
  "Disengaged" beat when the queue is idle, then `Session.LeaveCombat()`; the AI does not act in between.
* `SetAutoPlay(unit, on)` — via the session (also the unit's pet). An active unit switched to auto-play is taken over
  by the AI at the next idle frame (targeting is cancelled).

## 4. Input rules (player's turn)

All world input goes through `GameInput`; clicks over panels drawn with `Ui.Panel` / `Ui.Btn` / `Ui.Block` are
ignored (`GameInput.WorldClick`). World input is blocked entirely while `UiRoot.ModalActive` or
`GameFlow.WorldInputEnabled == false` (hover text still updates but clicks/keys do nothing).

| Input | No targeting | Targeting |
|---|---|---|
| hover ground | path preview (dashed, gold; orange when it stops short) + "Move 6.2 m (2.8 m left)" | AoE shape follows the mouse (circle / cone / line), affected units highlighted (enemies red, allies green) |
| hover enemy | red highlight + smart-attack preview ("Move 1.9 m, then Attack → Grey Wolf · 115–148 damage · 91% hit · auto attack at the end of your turn") | valid targets tinted (dimmer when out of range), hovered target bright; text "Fireball → Grey Wolf · 319–327 Fire · 99% hit · 217 Mana · 3.5 s cast" or the reason |
| hover downed ally | green highlight, "Help → Kael" (walks into reach first) | — |
| hover own unit | "Hero · 4.5 s and 6.0 m left this turn · Space ends the turn" | self-cast preview |
| left click ground | walk (exact path shown; partial/truncated paths walk as far as the budget allows) | ground abilities cast at the point (snaps to a hovered unit); cones/lines cast in the aimed direction |
| left click enemy | **smart attack**: hunters Auto Shot (melee Attack when too close); casters with a wand Shoot when out of melee reach; otherwise melee Attack — walking into reach first when the movement budget allows. Already attacking that enemy in reach → nothing (auto attacks swing at the end of the turn). | cast on it; when out of range but reachable this turn the unit walks into range first, then casts |
| left click downed ally | Help (walks into reach first) | — |
| right click / Esc | (Esc opens the pause menu as usual) | cancel targeting; Esc is consumed (`UiRoot.HotkeysSuppressed`) |
| Space / Enter | end turn | end turn (cancels targeting) |
| hold Shift | fast-forward the presentation (×2.5) | same |

While targeting (and no modal window is up — Esc then belongs to that window), `UiRoot.HotkeysSuppressed` is set every
frame (panel hotkeys are off; a HUD that also honours the
flag should still let number keys switch the targeted ability). A failed confirm keeps targeting active and sets
`LastError`.

Hover text pieces: magnitude via `Tooltip.Magnitude` ("14–22 Fire", "202–247 healing", weapon damage), hit chance
estimated from the engine's hit table (spells: miss/resist; weapons: miss/dodge/parry; behind the target no parry),
resource cost, time cost: "1.5 s", "3.5 s cast (pending: resolves next turn, can be interrupted)",
"8 s channel (continues next turn)", "1.5 s (+0.5 s time debt)", "free action". Reasons are the engine's sentences.

Overlays owned by the controller (FxSystem preview ids): `combat_move_range` (ReachMap of the battle NavGrid,
re-baked only when the unit's position/budget changes), `combat_path`, `combat_aoe`, `combat_range` (faint ring of
the ability's range), `combat_ai_path`. Do not reuse these ids.

## 5. Presentation

Events are grouped into **beats** and played strictly one after another; every event is announced exactly once and
in event order (`CombatEventPresented`, log). Commands issued by the controller carry an *intent* (actor, ability,
target, point) so a beat knows what was used even when the engine emits no header.

| Beat | Contents | Presentation |
|---|---|---|
| marker | `TurnStart` | active-turn ring moves, camera focuses the unit, party units are selected (`GameFlow.Select`) with a soft chime; 0.2 s (player) / 0.25 s (AI) |
| | `TurnSkipped` | "Stunned"/"Surprised!" over the unit, 0.6 s |
| | `CastStart` "continuing" | glow progress + "Casting X... (2.5 s)", 0.5 s |
| | `BattleStart`, `RoundStart`, `TurnEnd`, `Initiative`, `BattleEnd` | log only (BattleStart switches to the combat music mood) |
| action | one ability/item use with all consequences (costs, hits, heals, auras, deaths, procs, reactions) | see below |
| swing | one auto-attack swing (+ procs, rage, deaths) | melee lunge, blow at `AttackHitTime` (0.22 s); ranged: shoot, release at 0.2 s, arrow/bolt flight; follow-up swings of one volley are quicker |
| tick | periodic damage/heals/regen at a turn start (all units together) | small numbers, puffs/sparkles, 0.4 s (0 when nothing is visible) |
| move | one `Move` (`Path`) | `UnitView.MoveAlong` at 4.6 m/s (fear 5.4 m/s + "Feared"), camera follows, waits for arrival |

Action beats pick a **delivery** from the ability: melee lunge (`PlayAttack`, blow at 0.22 s, `Slash`), ranged weapon
(`PlayShoot`, arrow), wand (bolt in the wand's school), spell bolt (`PlayCast`, release at `CastReleaseTime` 0.45 s,
`FxSystem.Projectile`), friendly spell (cast pose → heal sparkles / buff), self cast, caster-centred burst
(`FxSystem.Burst` at the caster), ground burst (at the point), cone/line (burst along the aim), channel (beam to the
target or bursts on the area per `ChannelTick`, 0.32 s apart). Abilities with a cast time that resolve this turn get a
0.32 s wind-up with the casting glow. Charge-style abilities run in first (15 m/s), then strike. AI actors show the
ability name above their head. All hits of an AoE land together.

Pending casts (do not fit the remaining Time): cast pose + "Casting X..." + a persistent glow (and, for channels, the
beam) until `CastComplete`, `CastInterrupted` ("Interrupted"), `CastFailed` ("Failed"), death or downing.

Event visuals: `Damage` → `PlayHit`, number (crits bigger with a small camera shake, periodic smaller), impact or slash,
impact sound · `Heal` → sparkles + green number · `Miss/Dodge/Parry/Evade` → word + dodge hop · `Block/Resist/Immune`
→ word + sparkles · `Absorb` → "Absorb 42" · `AuraApplied/Removed/Broken` → state visuals from the presented aura
set (stealth 40% alpha, polymorph sheep, tints: frozen ice-blue, stunned yellow, feared violet, rooted green,
asleep/incapacitated grey-blue, banished, invulnerable gold) + control words ("Stunned", "Frozen", "Polymorphed"),
debuff names, buff names (not repeated when the buff is named like the ability) · `AuraPulse` around the caster for
group buffs · `Dispel` → "X removed" · `Summon` → view created, placed, revive glow + puff (totems: ground ring) ·
`Despawn` → puff, view removed · `Death` → `PlayDeath`, enemy views removed after the fade · `Downed` → lies down,
"Downed!" · `Revive` → stands up, sparkles · `Teleport` → puffs at both ends · `Knockback` → hop ·
`Taunt` → "Taunted!" · `ComboPoints` → "3 combo" · big resource gains → "+20 Rage" · `ItemCreated` → "+Soul Shard".

Robustness: a beat that throws is logged once and skipped (its events are still announced); missing views are
skipped; unknown event types are logged once; GameFlow failures (`ViewOf`, `EnsureView`, `Select`) fall back to
`UnitView.All` (`Tag` / `UnitId`).

### Speed

`Update(dt)` uses `min(dt, Time.unscaledDeltaTime)` × `PresentationSpeed` (≤ 0.1 s per frame). Because `UnitView`,
`FxSystem` and `FloatingText` animate with scaled time, the controller sets `Time.timeScale = PresentationSpeed`
during combat (never overriding a pause, i.e. `timeScale == 0`; while paused the presentation is frozen) and restores
1 when finishing/disposing. Settings should change `GameFlow.AnimationSpeed`, not `Time.timeScale`.

## 6. AI pacing

When the queue is idle and the active unit is AI-controlled (`Battle.IsAIControlled`: enemies, summons, totems,
auto-played companions and their pets, controlled party members): wait 0.2 s at the start of its turn → `AI.NextStep`
→ Move steps show the path (`combat_ai_path`) for 0.3 s first → `AI.Execute` (a failed EndTurn still ends the turn,
as `GameSession.RunAIStep` does) → the produced events are presented → 0.12 s pause → next step. Guards: two steps in
a row that change nothing end the turn; more than `AI.MaxStepsPerTurn + 8` steps end the turn.

## 7. Timing at speed 1 (measured headless with the real rules engine)

A 4-vs-3 fight presents in roughly 15–35 s per round depending on how many spells are cast (each instant ability
≈ 1.0–1.3 s, melee swing ≈ 0.5 s, move ≈ distance / 4.6 m/s, AI think pauses ≈ 0.3 s per step). Shift / fast-forward
divides that by 2.5.
